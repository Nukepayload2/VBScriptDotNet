# 脚本模式下 `End` 语句被拒（顶层裸 `End` → BC30678，块内/方法体内 → BC30615）

* 状态：**Open**
* 发现日期：2026-10-07
* 发现场景：用户报告（作者）+ 本机实测复现（宿主 = 本地 `dotnet tool install` 的 `vbi` 2.0.0-RC2）
* 证据等级：**已运行**（触发面与判别性对照均实跑；「修复后」一节为目标行为，尚无修复构建可跑）

## 触发面

`.vbx` 脚本与 REPL 提交里的 `End` 语句有**两条不同的拒绝路径**，都到不了运行期（`EXITCODE=1`，`End` 之前的语句也不执行）：

| 位置 | 诊断 | 真名 |
|---|---|---|
| 顶层**裸** `End`（后面只有行终止符/`:`） | `BC30678` `'End' statement not valid.` | `ERR_UnrecognizedEnd` |
| 顶层**块内**（`If`/`For`/`Using`/… 任一）、方法体内、lambda 体内 | `BC30615` `'End' statement cannot be used in class library projects.` | `ERR_EndDisallowedInDllProjects` |

### 实测（探针目录 `tmp\probe-end-stmt\`）

顶层裸 `End`（`a-end-top.vbx`）：

```vbx
Console.WriteLine("A1")
End
Console.WriteLine("A2")
```
```
a-end-top.vbx(2) : error BC30678: '"End" 语句无效。
The script has error. See the output for more information.
EXITCODE=1
```

顶层 `If` 块内（`d-end-in-if.vbx`）——注意这里已经不是 BC30678：

```vbx
If True Then
    System.Console.WriteLine("D1")
    End
End If
System.Console.WriteLine("D2")
```
```
d-end-in-if.vbx(3) : error BC30615: '在类库项目中不能使用 "End" 语句。
EXITCODE=1
```

方法体内（`c2-end-sub.vbx`）与 lambda 体内（`e-end-in-lambda.vbx`）同为 `BC30615`。

REPL 逐条提交（`vbi /i`，喂 `End`）同顶层路径：

```
(1) : error BC30678: '"End" 语句无效。
```

## 根因

两道**互相独立**的门，判据分别落在解析语境与 `OutputKind` 上，都与 `End` 语句本身无关 ⇒ 必须各修一处。

### 门 1：顶层裸 `End` 被当成「块结束语句」

`Compilers\VisualBasic\Portable\Parser\Parser.vb:829-830`（声明/语句分发器的 `EndKeyword` 臂）：

```vb
Case SyntaxKind.EndKeyword
    Return ParseGroupEndStatement()
```

无条件按块结束语句解析。`End` 后跟行终止符时，`ParseGroupEndStatement`（`:1756-1778`）算出 `endKind = GetEndStatementKindFromKeyword(EOL) = SyntaxKind.None`，于是 `:1769`：

```vb
statement = ReportSyntaxError(ParseStopOrEndStatement(), ERRID.ERR_UnrecognizedEnd)
```

——节点其实被解析成了 `StopOrEndStatementSyntax`，错误是**多挂上去的**。

**方法体语境的分发器是对的**：`Parser.vb:1005-1006` 调 `ParseEndStatement()`（`:1719-1749`），后者用 `CanFollowStatementButIsNotSelectFollowingExpression`（`Parser\ParseScan.vb:84-89`）判别「`End` 后面跟的是语句终止符」⇒ 走 `ParseStopOrEndStatement()`，零诊断。

⇒ 缺口 = **顶层脚本没有复用方法体那条判别**。同一 switch 里紧邻的 `AddHandler`/`RemoveHandler` 臂（`Parser.vb:838`、`:844`）已经有 `If IsTopLevelScript Then Return ParseStatementInMethodBodyInternal()` 守卫，`EndKeyword` 臂独缺（`IdentifierToken` 臂在 `:819` 也有同类守卫）。

### 门 2：块内/方法体内的 `End` 撞「DLL 项目」门

`Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb:5434-5440`：

```vb
Private Function BindEndStatement(endStatementSyntax As StopOrEndStatementSyntax, diagnostics As BindingDiagnosticBag) As BoundStatement
    If Not Compilation.Options.OutputKind.IsApplication() Then
        ReportDiagnostic(diagnostics, endStatementSyntax, ERRID.ERR_EndDisallowedInDllProjects)
    End If

    Return New BoundEndStatement(endStatementSyntax)
End Function
```

脚本提交**写死**了 `OutputKind.DynamicallyLinkedLibrary`——`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:241-242`（`CreateSubmission` 的 `CreateScriptCompilation` 实参）⇒ `IsApplication()` 恒假 ⇒ **`End` 一旦进到绑定层就必报 BC30615**。该门对两个宿主（`vbi` 与商店版 `vbicore`/`vbifw`）一致，因为都走同一条 `CreateSubmission`。

判别性对照（同一份源码，只换 `OutputKind`，用 `vbi` 的编译模式）：

```powershell
vbi normal.vb /target:exe     /out:k-exe.exe    # EXITCODE=0，零诊断
vbi normal.vb /target:library /out:k-lib.dll    # EXITCODE=1，BC30615
```

⇒ 门是 `OutputKind`，不是 `End` 本身。

### `End` 被接受之后会做什么（预期语义的来源）

`Compilers\VisualBasic\Portable\Lowering\LocalRewriter\LocalRewriter.vb:865-873`：`BoundEndStatement` 下降为对 `Microsoft.VisualBasic.CompilerServices.ProjectData.EndApp()` 的调用（`WellKnownMember.Microsoft_VisualBasic_CompilerServices_ProjectData__EndApp`）——即 VB 语言定义的「立即结束进程、退出码 0」。

标准 VB 对照（**SDK 自带编译器**，`OutputType=Exe`，`tmp\probe-end-stmt\sdkctl\`）：

```vb
Module M
    Sub Main()
        System.Console.WriteLine("before")
        End
        System.Console.WriteLine("after")
    End Sub
End Module
```
```
before
RUN_EXITCODE=0        ' 无 after
```

⇒ 与用户预期逐字一致：`End` 生效时**等效于 `Environment.Exit(0)`**。

## 预期行为

`.vbx` 脚本与 REPL 提交里的 `End` **合法**，语义 = 立即以退出码 `0` 结束进程（`ProjectData.EndApp()`）：`End` 之前的输出已落盘、`End` 之后的语句不执行、`/check` 下零诊断。

脚本内 `System.Environment.Exit(0)` 今天已正常工作（`x-exit.vbx` → 打印 `X1`、`EXITCODE=0`、无 `X2`），`End` 应当同形。

## 修复方向

两道门都要开，缺一不可。

1. **`Parser.vb:829-830`**：`EndKeyword` 臂加脚本守卫，与紧邻的 `AddHandler`/`RemoveHandler` 臂同形——`IsTopLevelScript` 时走方法体分发（`ParseStatementInMethodBodyInternal()`，其 `:1005-1006` 即 `ParseEndStatement()`），使裸 `End` 落到 `ParseStopOrEndStatement()`。
   **边界**：真正的块结束语句（`End If`/`End Sub`/`End Class`…）在顶层仍须报既有诊断——`ParseEndStatement` 内部按下一 token 天然分流到 `ParseGroupEndStatement`，`DeclarationContext.vb:451` 那一臂不动。
2. **`Binder_Statements.vb:5435`**：把判据从「输出类型是 Application」放宽到「脚本/提交编译也允许」（候选：`OutputKind.IsApplication() OrElse <脚本提交谓词>`；`IsScriptClass`/`IsSubmission` 类的谓词在本仓 issue 28/29 已有先例）。
   **禁止**无条件删除该门——普通 `OutputKind.DynamicallyLinkedLibrary` 的类库项目仍须报 BC30615，那是该诊断的正当用途，且有既有用例钉住（`Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:9680`、`Binder_Statements_Tests.vb:3620`/`:3766`）。

## 修复后的新增行为变化

**有行为变化，必须登记**：脚本/REPL 里 `End` 从「编译期拒绝」变为「合法且立即结束进程」；`End` 由**不产生可运行产物**变为**产生可运行产物**（这是新增的可观测形状，脚本会在 `End` 处提前结束而不是跑完全文）。

须回收/须补的回归用例：

- `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:388-397`（`TopLevelEndStatement_IsReported`，钉 `BC30678`）与 `:399-407`（`EndInsideMethod_IsReported`，钉 `BC30615`）**当前钉的是负向行为**，修复后须改写为正向格：零诊断 + 运行到 `End` 前输出 + 退出码 `0` + `End` 之后输出不出现（进程级格，可参照同文件 `:415` `AssertEmits` 的既有形态）。
- 覆盖率台账 `..\tasks\script-mode-coverage-parity\test-plan.md:583` 的 `End` 语句行（两栏现按「负向，裁定 5 算覆盖」登记）须随用例改写同步更新；`:584` 的 `Stop` 行不受影响。
- 反例锁：`/target:library` 仍报 BC30615（既有 `BindingErrorTests` 已锁）；顶层 `End If`/`End Sub` 等块结束语句的诊断不变（建议补一条正向锁）。
- 对照格：脚本内 `Environment.Exit(0)` 的既有行为不得变化。

## 相关

- 同为「脚本容器的合成语境与普通编译不同 ⇒ 既有判据失效」的姊妹问题：`issue-script-class-explicit-me-in-member-bodies.md`（28）、`issue-submission-member-inherited-object-lookup.md`（29）、`issue-submission-shared-member-implicit-me.md`（08）。
- `Stop` 语句**不受本缺陷影响**：顶层已可发射（`ScriptModeStatementConformanceTests.vb:415` 只验发射，因为运行会把进程交给调试器）。
- 关联文件（本 issue 未改动，仅登记证据）：`tmp\probe-end-stmt\`（`a-end-top.vbx`、`d-end-in-if.vbx`、`c2-end-sub.vbx`、`e-end-in-lambda.vbx`、`x-exit.vbx`、`normal.vb`、`sdkctl\`）。
