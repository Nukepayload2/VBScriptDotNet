# 顶层语句（实例初始化器）拿不到流分析警告

* 状态：**Fixed**（2026-09-24 main 落地，commit 待作者提交后补；见下方「修复」节）
* 发现日期：2026-09-13
* 发现场景：随顶层崩溃族登记为显式非范围项（`../tasks/script-top-level-crashes-2/README.md` §一 非范围第 3 条）；其中会产出坏 IL 的那一条（`BC30101`）已单独收口，见 `issue-top-level-finally-branch-invalid-il.md`

## 触发面与实测

`vbi.exe /check`（只编译拿诊断、不执行不落盘，`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:245-252`）与 `vbc.exe` 对照，探针在 `tmp\probes\u6bc\`。

### 缺口：顶层语句**块内**的局部变量

```vbx
If True Then
    Dim s As String
    Console.WriteLine(s.Length)
End If
```

| 上下文 | 诊断 |
|---|---|
| 脚本顶层语句（`defasg-top-local.vbx`） | **零诊断**（`/check` exit 0）；直跑则运行期 `NullReferenceException`，exit `3` |
| 同样的形状放进顶层 `Sub`（`defasg-toplevel-sub.vbx`） | `warning BC42104` |
| 同样的形状放进嵌套类型的方法（`defasg-nested2.vbx`） | `warning BC42104` |
| 普通 `Module` 的 `Sub Main`（`defasg-ordinary-local.vb`） | `warning BC42104` |

顶层的 `Dim` 在**块内**是**局部变量**而不是字段（判别性实测：`If True Then Dim y As Integer = 5 End If` 之后读 `y` 报 `BC30451`「未声明」，即块内 `Dim` 的作用域止于块）；只有**块外的**顶层 `Dim` 才是脚本类字段。

### 不构成缺口的形状（复核后排除）

- **顶层 `Dim x As Integer` 后直接读 `x`**：零警告，但普通上下文里作为**字段**的同形状同样零警告——字段有默认值，本来就不参与 `BC42104`。这条形状**不能**用作缺口的证据。
- **`BC42105`（函数并非所有路径都返回值）不受影响**：顶层 `Function`（`noret-toplevel-sub.vbx`）与被嵌套类型的方法（`noret-nested2.vbx`）在 `/check` 下**都正常报** `warning BC42105`，与普通 `Module` 同码。这与「顶层语句缺流分析」的直觉不同，实测如此——`BC42105` 是**方法体**的判据，而顶层方法体走的是逐方法的分析路径。
- **不可达代码警告在本编译器里没有活的报点**（**实锤**，grep 后重数）：`WRN_UnreachableCode` 在产品源码里只有三处命中，全部在 `Analysis\FlowAnalysis\ControlFlowPass.vb:89` / `:100` / `:110`，且**都是注释**；该 id 在 `Errors.vb` 里**没有定义**（`VBResources.resx:4261` 只留下一条文案），既有的两条用例断言的也正是**零诊断**（`Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:21450` 的 `BC42356WRN_UnreachableCode_MethodBody` / `:21480` 的 `…_LambdaBody`，期望块为空）⇒ 与顶层语句无关。

## 根因

顶层语句序列（实例初始化器）不在流分析的覆盖范围内：`Compilation\MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?` 就落在这条路径上，紧接在两条初始化器桶的绑定（`:607-623`）之后——同一个缺口也是 `BC30101` 从不报出的原因（`issue-top-level-finally-branch-invalid-il.md`）。

**仍待查（推测）**：源码里另有一条会把初始化器块送进流分析的调用——`Binding\Binder_Initializers.vb:58-75` 的 `EnsureInitializersAnalyzed` 对合并后的初始化器块调用 `Analyzer.AnalyzeMethodBody`（调用点 `Compilation\MethodCompiler.vb:1286`），而 `Analyzer.AnalyzeMethodBody` 的下游正是 `FlowAnalysisPass`（`Analysis\Analyzer.vb:35`）。实测顶层语句块内的局部变量仍不报 `BC42104` ⇒ 缺口不在「是否存在这条调用」，而在该分析对顶层语句的实际覆盖范围。一个可能的机制（**推测**，未验证）：这条调用把**提交类构造器**当作 `method` 符号传入，而顶层语句里的局部变量属于合成的初始化器方法（`Symbols\Source\SynthesizedInteractiveInitializerMethod.vb`），因而未被计入报告范围。

## 预期行为

顶层语句块内的局部变量应拿到与普通方法体一致的流分析警告（`BC42104` 等）。今天这些形状静默通过并产出运行期空引用异常。

## 修复方向

让顶层语句序列进入方法体级流分析，可选两条：

1. 把顶层语句绑进 `<Initialize>` 的**真实**方法体（而不是 `Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142` 的空壳），让既有的 `MethodCompiler.BindAndAnalyzeMethodBody` → `Analyzer.AnalyzeMethodBody`（`MethodCompiler.vb:1821`）自然覆盖。
2. 在初始化器路径上对合成块直接跑 `FlowAnalysisPass`，并把诊断接回诊断袋——与 `BC30101` 的收口同一入口（`Binding\Binder_Initializers.vb:144` / `:264-296`）。这一条要同时接回 `DataFlowPass` 的输出，而不只是 `ControlFlowPass` 的。

## 修复后的新增行为变化

修复后会**新增警告**（`BC42104` 等）：今天这批形状静默通过，修复后变成 `warning`——不阻止发射、不改变产物，但脚本的编译输出会多出警告行，且 `/warnaserror` 下会变成错误。须补回归用例：

- 顶层语句块内的局部变量未赋值即用 ⇒ **恰好一条 `BC42104`**，位置与普通方法体一致；
- 顶层 `Function` / 嵌套类型方法的 `BC42105` ⇒ **零行为变化**（今天已报）；
- 顶层字段读取 ⇒ **零警告**（今天如此，修复后不得引入）。

## 相关

- 同一个缺口的另一条后果（会产出坏 IL 的那条，已收口）：`issue-top-level-finally-branch-invalid-il.md`（16）。
- 同族「顶层语句拿不到方法体级检查」：`issue-script-top-level-await-in-try-crash.md`（10）、`issue-script-top-level-goto-await-crash.md`（11）。

## 复核与纠正（2026-09-24，main；状态维持 Open——缺口未修）
本条经两轮误判（一度记"作者设计阻塞"、又一度记"Not A Bug"）后用 **C# 树内 oracle + 逐格读码** 定论：

1. **C# oracle（本仓树内，非 csi）**：`Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs:986` `ERR_UseDefViolation`——`int a; int b = a;`（顶层，提升为 `Program` 字段）**不报** CS0165；`void F(){ int c; int d = c; }` 与裸块 `{ int e; int f = e; }`（局部）**报** CS0165。⇒ 顶层字段豁免是**两侧一致的 parity**，非缺口。
2. **VB 现状（本会话自 dump 实测，当前码）**：顶层块外 `Dim s As String`（字段）读 → 无 BC42104（parity ✔，已在 `ScriptTopLevelDefiniteAssignmentTests.vb` 钉 4 格，Semantic→5883 七门全绿）；但**块内局部**——`If True Then Dim s…读 s…End If`、`For` 内、`Using` 内——三形**皆无 BC42104**，与 C# 对块内局部报 CS0165 相反 ⇒ **本 issue 的缺口（§触发面 line 27 的块内局部）在当前码仍成立，是真·可移植缺口，未修**。
3. **根因确认（原 §根因"推测"现降为"已定位"）**：顶层语句绑成 `BoundGlobalStatement`（`Binder_Initializers.vb:133`）置于合成 script 初始化器的桩体内；通用 `DataFlowPass` 不下钻其体（这正是逐条手跑 `CheckAwaitInTryHandler`/`CheckBranchOutOfTopLevelFinally` 之故），故块内局部的定义赋值警告接不进来。`EnsureInitializersAnalyzed`（`:58`）传的是合并初始化器块 + 提交构造器符号，非合成 script 初始化器本身。
4. **修法与风险（判改，但属高 blast radius 大改，未在本轮强推）**：路由 2——`EnsureInitializersAnalyzed` 以合成 script 初始化器为 `method`、令 `DataFlowPass` 访问 `BoundGlobalStatement.Statement` 的块局部并把 BC42104 接回诊断袋（对齐 `ControlFlowPass` 已手工触及的同一洞）。一旦接通，**所有**脚本测试里"块内未赋局部即读"会**新增 BC42104 警告**（含 conformance 套件自身可能中招），须逐格回归排雷、按新数改基线、必要时重建宿主档 2。非单格安全改，留作专做片段（`tasks\script-toplevel-flow-analysis\` FI-*，按"块内局部"重订）。

## 修复（2026-09-24 main 落地，✔）
承 §复核与纠正，实际采**展开式**（非"传合成初始化器为 method"的措辞，效果同）：
1. `Binder_Initializers.vb` `EnsureInitializersAnalyzed`：拼哑块时把 `BoundGlobalStatementInitializer` **展开为 `.Statement`**（普通字段/属性初始化器透传），使 `Analyzer.AnalyzeMethodBody` + `DiagnosticsPass.IssueDiagnostics` 下钻顶层语句体 ⇒ 块内局部拿 BC42104、成员体局部/普通方法体逐字不变。
2. **删除** issue-16 的 ad-hoc `CheckBranchOutOfTopLevelFinally` 及辅助 `ContainsFinallyBlock`：BC30101 现由通用路**单源**（保留 ad-hoc 会双报，实测 3 finally 格 1→2）。`CheckAwaitInTryHandler`（BC36943）保留——实测通用路不双报 await。
3. 新增 `ScriptTopLevelDefiniteAssignmentTests.vb` 7 格（含块内局部正反对照）。
- 验证（✔）：Semantic 5883→**5886/5782/104**、**七门全绿**（Emit 4382/4279 含 issue-16 finally/`InvalidProgramException` 用例 ⇒ BC30101 单源仍抑制坏 IL）、**L2 769/0**（脚本符合性套件无新增红）。建议补档 2：重建 `vbi` 跑一条顶层块内未赋局部，看新增 BC42104、运行期 NRE 形状不变。
- 账本：`upstream-merge.md` **§2.25(i)**；`tasks\script-toplevel-flow-analysis\`；`HANDOFF.md` §4.14/§5 行 I；队列 #7。
- 注：曾试"逐语句 `ControlFlowPass.Analyze` + 只放宽 filter"路线，实测 **Actual:[] 死路**（`ControlFlowPass.Analyze` 单独不产定义赋值警告，需完整 `Analyzer`+`DiagnosticsPass`），故走展开式。
