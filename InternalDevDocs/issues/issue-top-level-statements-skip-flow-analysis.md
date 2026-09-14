# 顶层语句（实例初始化器）拿不到流分析警告

* 状态：**Open**
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
