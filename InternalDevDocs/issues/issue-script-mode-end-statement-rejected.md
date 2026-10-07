# 脚本模式下 `End` 语句被拒（顶层裸 `End` → BC30678，块内/方法体内 → BC30615）

* 状态：**Fixed**（已验证，commit 待作者提交后补）——落地形状与读数见文末「修复落地（2026-10-07）」一节；两道门各修一处，改动面＝`Parser.vb` +8 / `Binder_Statements.vb` +2−1。
* 发现日期：2026-10-07
* 发现场景：用户报告（作者）+ 本机实测复现（宿主 = 本地 `dotnet tool install` 的 `vbi` 2.0.0-RC2）
* 证据等级：**已运行**（触发面与判别性对照均实跑；**修复后**亦已实跑：七门 gate 全绿、`Scripting\VisualBasicTest` 直跑 777/0、重建 Debug `vbi` 后 `tmp\probe-end-stmt\` 六格档 2 对账 —— 见文末「修复落地」）

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

## 修复落地（2026-10-07，主线亲跑）

任务计划 `..\tasks\script-mode-end-statement\`（`README.md` + `test-plan.md`）、流水账 `..\..\tmp\vortex-logs\script-mode-end-statement\`（`01-main-plan` / `02-implementer-f02-f03` / `03-verifier`）、账本 `..\upstream-merge.md` §2.25(l)。

### 两道门各修一处

1. **门 1（解析）** `Compilers\VisualBasic\Portable\Parser\Parser.vb:829`（`ParseDeclarationStatementInternal` 的 `EndKeyword` 臂）加脚本守卫，与紧邻 `AddHandler` 臂（`:845-847`）同形：

   ```vb
   Case SyntaxKind.EndKeyword
       ' …（五行为何脚本顶层要问与方法体相同的问题）
       If IsTopLevelScript Then
           Return ParseStatementInMethodBodyInternal()
       End If
       Return ParseGroupEndStatement()
   ```

   于是顶层裸 `End` 经 `ParseEndStatement`（`:1727`）分流到 `ParseStopOrEndStatement`，不再被 `ParseGroupEndStatement`（`:1764`）在 `:1777` 多挂 `ERR_UnrecognizedEnd`；真正的块结束语句仍由 `ParseEndStatement` 交给块结束路径。

2. **门 2（绑定）** `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb:5437` 判据放宽（`ReportDiagnostic` 原码原位 `:5438` 未动）：

   ```vb
   If Not (Compilation.IsSubmission OrElse Compilation.Options.OutputKind.IsApplication()) Then
   ```

   **落点理由**：`Compilation.IsSubmission` 在 VB 编译器树内**唯一写点**是 `VisualBasicCompilation.vb:388`（`CreateScriptCompilation` 内的 `isSubmission:=True`）⇒ 词法上就是脚本提交本身。曾考虑并**否掉** `ContainingType.IsScriptClass`（＝`DeclarationKind.Script OrElse DeclarationKind.Submission`，会把**非提交**的脚本树编译一起放宽；本仓 D5 实例 4 正记着"测试用 `DeclarationKind.Script`、产品走 `Submission`，两条不等价"）。**明示接受的代价**：REPL 提交里**普通类成员体内**的 `End` 也随之合法（提交整体不是类库），与"脚本/REPL 里的 `End` 合法"同向，不静默。

### 非脚本面免疫（D10 硬约束；两处都是上游同名文件）

- 门 1 的开关 `IsTopLevelScript`（`Parser.vb:85-93`）＝ `Context.AcceptsScriptTopLevelStatement`（`Parser\BlockContexts\BlockContext.vb:207-211`）＝ `Parser.IsScript AndAlso BlockKind = SyntaxKind.CompilationUnit`，而 `IsScript` 要求 `SourceCodeKind.Script` ⇒ Regular 输入下恒 `False`，`EndKeyword` 臂对普通输入逐字退回 `Return ParseGroupEndStatement()`。
- 门 2 的开关 `Compilation.IsSubmission` 见上"唯一写点"⇒ 为 `False` 时新判据**退化为原文**。
- 锁格（已跑）：非提交的 `SourceCodeKind.Script` 树 + `ReleaseDll` 仍报 `BC30615`；Regular 顶层裸 `End` 仍 `BC30678@0+3`；脚本顶层 `End If`/`End Sub` 仍 `BC30087@0+6`/`BC30429@0+7`（且与同形状 Regular 读数同签名）；脚本与 Regular 的 **24 种** `End <块关键字>` 形状逐字一致；Dev10#708061 争议形状（`From … In Sub() End Select`）两侧同为 `BC30088`。

### 读数（亲跑）

- 七门（`scripts\verify-vb-compiler-tests.ps1`，一次跑完、`exit 0`）：`Phase2 143/143/0/0`、`Syntax 4106/4103/3/0`、`Symbol 3407/3383/24/0`、`Semantic 5919/5815/104/0`、`IOperation 1574/1566/8/0`、`Emit 4389/4286/103/0`、`CommandLine 483/476/7/0` ⇒ **除本任务新增格外（Syntax +8、Semantic +5、Emit +1），逐门数字与基线一字未动**；门脚本内基线已同步。
- `Scripting\VisualBasicTest` 直跑 `-automated`：`TestsTotal 777 / TestsFailed 0 / TestsSkipped 0 / TestsNotRun 0`。
- 档 1 新增/改写的格：语法层 8 格（含 Regular 顶层裸 `End` 仍 `BC30678`、脚本块结束语句诊断不变）、语义层 5 格（含**非提交脚本树 + DLL 仍 `BC30615`** 的窄化锁与提交链）、发射层 1 格（IL 里恰一处 `ProjectData.EndApp()`）、宿主层两格负向钉改写为 `AssertEmits` 正格。
- 档 2 宿主（重建 Debug `vbi`）：`a-end-top.vbx` → `A1`/`exit 0`/无 `A2`；`d-end-in-if.vbx` → `D1`/`exit 0`/无 `D2`；`c2-end-sub.vbx` → `C1`+`C-in`/`exit 0`/无 `C2`；`e-end-in-lambda.vbx` → `E1`/`exit 0`/无 `E2`；REPL（先喂 `R1` 再喂 `End`）→ 打印 `R1` 后 `exit 0`、其后提交不执行、零诊断；`vbi /check a-end-top.vbx` 与 `/check d-end-in-if.vbx` 零诊断 `exit 0`（对照 `/check c-end-sub.vbx` 报 BC30183/BC30205、`exit 1` ⇒ `/check` 通道确实在工作）。**哨兵逐字未变**：`b-exit-top.vbx`（`B1`/`exit 0`/无 `B2`）、`x-exit.vbx`（`X1`/`exit 0`/无 `X2`）、`normal.vb /target:exe`（`exit 0`、零诊断）、`normal.vb /target:library`（`exit 1`、`BC30615`）。
- 独立验证者（不与实施者同一上下文）：产品 diff 最小性、非脚本面免疫**自建探针**复测、宿主两格未被弱化、对抗性解析形状（含 24 种块结束形态与单行 lambda 争议形状）、BOM/意外改动、节点种类钉法 —— 8 项全过；未验项＝`#Load` 树里的 `End`（无解析器夹具）。

### 须回收 / 须补（已收）

- `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb`：`:392-397`（钉 `BC30678`）与 `:399-407`（钉 `BC30615`）两格改写为正格 `TopLevelEndStatement_EmitsWithoutRunning`（现 `:395`）与 `EndInsideMethod_EmitsWithoutRunning`（现 `:408`），doc prose 重写；**允许**只到"零诊断 + 发射成功"，因为**进程内跑 `End` 会杀掉整门**（`ProjectData.EndApp()`），进程级语义归档 2 宿主；`Stop` 哨兵（现 `:423`）未动。
- 覆盖率台账 `..\tasks\script-mode-coverage-parity\test-plan.md`：`End` 行（`:583`）、R9 行（`:675`）、R9 段（`:860`）、计行裁定段（`:1092-1094`）已同步；行内已腐的源码锚（原写 `Parser.vb:996`/`:1710`）改正为 `:829`（守卫 `:835`）/`:1013-1014`/`:1727`；`Stop` 行仅校正因本轮改写造成的行号位移（`:415`→`:423`）。
- 规格：`..\spec\spec-scripting-dialect.md` 新增 `### `End` at the top level and in member bodies`（英文正本）＋ `..\spec\zh-CN\spec-scripting-dialect.md` 对应译文；两份 `## Testing` 各补一条 `End` 测试条目。

### 仍未验 / 范围外

- `#Load` 进来的树里的 `End`（验证者无解析器夹具，标未验）。
- 进程级行为仅在 Windows / .NET 10 / Debug 宿主上实测；Release 优化路径未跑宿主探针（`Scripting` 侧 `ReleaseOptimizedIteratorAndAsync_Conform` 那类格与本条无关）。

## 相关

- 同为「脚本容器的合成语境与普通编译不同 ⇒ 既有判据失效」的姊妹问题：`issue-script-class-explicit-me-in-member-bodies.md`（28）、`issue-submission-member-inherited-object-lookup.md`（29）、`issue-submission-shared-member-implicit-me.md`（08）。
- `Stop` 语句**不受本缺陷影响**：顶层已可发射（`ScriptModeStatementConformanceTests.vb:423` 只验发射，因为运行会把进程交给调试器；本轮 `End` 两格改写使其行号由 `:415` 位移到 `:423`，内容未动）。
- 关联文件（本 issue 未改动，仅登记证据）：`tmp\probe-end-stmt\`（`a-end-top.vbx`、`d-end-in-if.vbx`、`c2-end-sub.vbx`、`e-end-in-lambda.vbx`、`x-exit.vbx`、`normal.vb`、`sdkctl\`）。
