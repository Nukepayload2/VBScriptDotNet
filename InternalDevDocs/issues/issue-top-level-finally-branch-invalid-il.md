# 脚本顶层 `Finally` 里离开该块的分支 → 运行期 `InvalidProgramException`、编译期零诊断

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-13
* 发现场景：崩溃形状穷尽扫描（`tmp\probes\sweep\`，390 探针，批次 `b6_finally`）

## 触发面

**任何离开顶层 `Finally` 块的分支**，不只是 `GoTo`：

```vbx
Console.WriteLine("A")
Try
    Console.WriteLine("T")
Finally
    GoTo out                      ' Return / Exit For·While·Do·Select / Continue For 同症状
End Try
out:
Console.WriteLine("Z")
```

形状面（扫描批次逐条实测，全部 `EXITCODE=2148734266`）：

| 维度 | 取值 |
|---|---|
| 分支种类 | `GoTo`（`fin-goto-out`）、`Return`（`fin-return`）、带值 `Return`（`fin-return-value`）、`Exit For` / `Exit While` / `Exit Do` / `Exit Select`（`fin-exit-*`）、`Continue For`（`fin-continue-for`） |
| 嵌套 | `Finally` 套 `Finally`（`fin-nested-return`） |
| 落在别的块内 | `Using` 里的 `Finally`（`fin-goto-out-in-using`）、`Catch` 里的 `Finally`（`fin-in-catch-goto-out`）、多 `Catch` 的 `Try`（`fin-multi-catch-goto-out`）、循环里的 `Finally`（`fin-goto-out-in-loop`） |
| 与 `Await` 叠加 | `Await` 之后再跳（`fin-goto-out-with-await-before`） |
| 跨树 | `#Load` 进来的文件里的 `Finally`（`load-finally-in-loaded` / `load-main-finally-goto-out`） |

**只按 `GoTo` 修会漏掉一整组**——判据必须按「块内 pending 分支」整体取，不能按语句种类枚举。

## 症状与实测退出码

编译期**零诊断**（不是「报了错再崩」），运行期：

```
System.InvalidProgramException: Common Language Runtime detected an invalid program.
EXITCODE=2148734266
```

## 根因

**顶层语句序列不在流分析的覆盖范围内，于是这条判据从不执行。**

- 顶层语句住在合成初始化器 `<Initialize>` 里，而它的方法体是**空壳**——只含一条 `BoundLabelStatement(ExitLabel)`：`Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`。
- 初始化器路径上留着 `Compilation\MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?`，它紧接在两条初始化器桶的绑定之后（`:607-623`）。
- `BC30101`（`ERR_BranchOutOfFinally`）的报点**只有**流分析 `Analysis\FlowAnalysis\ControlFlowPass.vb` 的 `VisitFinallyBlock`（`:151-181`，`diagnostics.Add` 在 `:177`）；`GoTo` 的诊断位置取 `GoToStatementSyntax.Label`（`:169-173`）。

**全称主张剪枝（实锤，grep 后再数）**：`ERR_BranchOutOfFinally` 在**编译器产品源码**（`Compilers\VisualBasic\Portable\`）的 `.vb` 文件里共 **4 处**命中——定义 `Errors.vb:166`（`= 30101`）、`ErrorFacts.IsBuildOnlyDiagnostic` 清单 `ErrorFacts.vb:138`、**报点** `ControlFlowPass.vb:167`（`VisitFinallyBlock` 里 `errId = errId.ERR_BranchOutOfFinally`，唯一的 `diagnostics.Add` 来源）、以及 `Binder_Initializers.vb:292` 的**过滤读取**（本 issue 的收口点：从流分析的一次性诊断袋里只挑这一条转写，不报点）。同一目录的 `VBResources.resx:492` 另有一条**文案**条目（不是判定逻辑）；**测试源码**（`Compilers\VisualBasicSemanticTest\` 的 `Binding\BindingErrorTests.vb` 与 `Semantics\ScriptSemanticsTests.vb`）也含该标识符，均不计入。⇒ 判据从不执行时，坏形状一路活到发射期。

**机制的确切落点仍待查（推测）**：`Binding\Binder_Initializers.vb:58-75` 的 `EnsureInitializersAnalyzed` 会对初始化器块调用 `Analyzer.AnalyzeMethodBody`（`ControlFlowPass` 也是它的下游），但实测该判据从未报出 ⇒ 缺口不在「这条调用是否存在」，而在该分析对顶层语句的实际覆盖范围。同一条未决项记在 `issue-top-level-statements-skip-flow-analysis.md`（17）的「仍待查」。

**边界（实锤，扫描批次）**：

- 同一个跳出形状放进**顶层 lambda** 会被正常报 `BC30101`（`fin-goto-out-in-lambda`，exit 1）——缺口严格限于顶层语句路径。
- 反方向（跳**进**保护块）由绑定层另行报错：`goto-into-finally` / `goto-into-catch` 给 `BC30754`，`fin-exit-try` 给 `BC30393`，全部 exit 1，**不属本文**。

## 预期行为

普通方法里同形状报 `BC30101`：

```vb
Module M
    Sub Main()
        Try
            Console.WriteLine("T")
        Finally
            GoTo out
        End Try
out:
        Console.WriteLine("Z")
    End Sub
End Module
```

实测 `vbc.exe` exit `1`，诊断锚在 `GoTo out` 的**标签**上（`tmp\probes\u6bc\jumpout-finally-ordinary.vb`）。⇒ 判定**报错 `BC30101`**，复用既有诊断码，**不需要新码**。

## 修复方向

复用既有的「逐顶层语句补判据」模式，但**必须按语法子树判**而不是按顶层语句种类判——`Finally` 常常嵌在 `For` / `While` / `Using` / `Catch` 里面，那时顶层语句是 `ForBlock` / `UsingBlock` / 不带 `Finally` 的 `TryBlock`，按种类判会整批漏掉。

落点：`Binding\Binder_Initializers.vb:144`（调用点，与既有的 `CheckAwaitInTryHandler` 并列）、`:264-296`（判据，只取 `ERR_BranchOutOfFinally`，其余流分析结果丢弃）、`:301-314`（子树扫描 `ContainsFinallyBlock`）。

## 修复后的新增行为变化

**新增诊断 `BC30101`**（此前静默产出坏 IL）：

```
error BC30101: 从“Finally”中分支无效。
```

- 诊断位置与普通上下文一致（`GoTo` 指向标签，其余指向语句本身）。
- 今天能跑得过编译的这批脚本，修复后变成 `error` ⇒ **不发射、不产生可运行产物**；`EXITCODE` 由 `2148734266` 变为 `1`。
- 非 `Finally` 的块跳转形状行为不变：`GoTo` 越过顶层 `Try` / `Catch` / `Using` / `For` / `While` / `Select` / `With` / `SyncLock` 各一块，逐条 exit `0` 且跳转生效（实测 `tmp\probes\u6bc\out-of-*.vbx`）。

实测（当前编译器，`tmp\probes\u6bc\`）：`GoTo` / `Return` / `Exit For` / `Continue For` / 嵌套 `Finally` / `Finally` 在 `Using` 内，六种形状全部 exit `1` + `BC30101`，无 `InvalidProgramException`。

回归用例：`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`（`BC30101`，`:757` / `:775` 一带），`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:888` / `:1005`。

## 预存在行为（不是本 issue 引入，登记备查）

`Finally` 套 `Finally` 时，流分析会对**每一层**各报一次，于是 API 层在同位置得到 2 条同 id 诊断；命令行/宿主层仍只打 1 条。普通方法体在 API 层同样如此 ⇒ 属 `ControlFlowPass.VisitFinallyBlock` 的预存在行为。

## 相关

- 缺流分析的其余后果（`BC42104` 等警告仍拿不到）：`issue-top-level-statements-skip-flow-analysis.md`（17）——与本条同一个入口（`MethodCompiler.vb:625`），本条只收口了其中会产出坏 IL 的那一条。
- 同为「顶层语句拿不到方法体级检查」的问题：`issue-script-top-level-await-in-try-crash.md`（10）、`issue-script-top-level-goto-await-crash.md`（11）。
