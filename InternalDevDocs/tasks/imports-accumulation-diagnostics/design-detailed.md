# 详细设计：`Imports` 跨提交累积的解析失败降级为诊断

> 状态：详细设计。依据链：`../../spec/spec-scripting-dialect.md`（`:213`，唯一验收口径）→ `../../meetings/meeting-scripting-dialect.md`（RESOLUTION R5 + TODO `:171`）→ `../../proposals/proposal-scripting-dialect.md`（`§6`，正文冻结）→ `design-overview.md` → 本设计。
> 本设计把概要落到「可被实现者直接照做、验证者可无人值守核对」的代码级底稿：每条改动给 `文件:行号` + 改动形状 + **自动裁决规则**（当 X 发生做 Y 不做 Z）+ **pass 条件**。
> 源码锚点均为本计划撰写时逐条 Read/Grep 核实的**当前工作树**状态（分支 `with-modified-vbsyntax`）。文件相对路径均相对仓库根。

## 0. 核心判定原则（贯穿全文）

- **只改宿主**：全部代码改动落在 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` + 新增测试文件。`Compilers\**` 与 `Scripting\Core` **零 diff**；`PublicAPI.*.txt` 零增量。
- **延申现有机制，不发明通道**：诊断用编译器既有非抛重载产出（`GlobalImport.vb:103-108`），上报用宿主既有先例 `CompilationErrorException`（`VisualBasicScriptCompiler.vb:62-64` 的 `#Load` 路径），消费端是既有 `Script.Compile` 捕获 + `CommandLineRunner` 打印（`Script.cs:332-346`、`CommandLineRunner.cs:370-375`）。
- **无坏子句 = 逐字节零行为变化**：收集结果、去重语义、顺序、返回类型全部与今天等价。
- **不得静默**：坏子句一定产出诊断（深度 ≥2 时「已报过」是可证明的，不算静默）。
- **测试纪律**：单测无副作用（不网络/不写文件/不 spawn 进程/不注册表）；REPL 用例用内存 `StringReader`/`StringWriter`（`TestConsoleIO`）。
- **测试执行规约（本仓特有）**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**不可用 `dotnet test`**（EXIT 0 但静默不跑）；一律先 `dotnet build`，再直跑 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或追加 `-class <FullyQualifiedName>`（类级）/`-method <FullyQualifiedName>`（单测；FQN 须完整到 `命名空间.类.方法`，缺段或拼错会**静默 0 跑**——EXIT 0 且无报错，须核对 `discovery-complete` 的 `TestCasesToRun > 0`）。见 memory `vb-scripting-test-runner`。

## 1. 改动清单总览

| # | 文件（相对仓库根） | 改动形状 | 目的 |
|---|---|---|---|
| F1-1 | `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` | 新增私有子句条目类型 + 改造 `GetGlobalImportsForCompilation` / `AddImportNames` / `AddPreviousSubmissionImports` 签名（§F1-1） | 收集结构携带位置与深度 |
| F1-2 | 同上 | `Friend Shared ParseAccumulatedClause` 纯函数 + `GetGlobalImportsForCompilation` 内逐子句非抛解析 + 坏子句丢弃（§F1-2） | 降级，不再抛 `ArgumentException`；L1 可直测 |
| F1-3 | 同上 | 诊断 `WithLocation` 锚定 + 深度 1 去重（§F1-3） | 位置与去重 |
| F1-4 | 同上 | 待报集非空 → `Throw New CompilationErrorException`（§F1-4） | 上报通道 |
| F1-T | `Scripting\VisualBasicTest\ImportsAccumulationDiagnosticsTests.vb` | 新增（§F1-6 + `test-plan.md`） | 全部收口断言 |

> 无共享层改动 ⇒ **`upstream-merge.md` 无新增义务**（实现者在收口时以 `git diff --stat` 确认）。

---

## F1-0. 为什么不需要改的两处（防误改，含源码锚点）

### F1-0.1 规范化三条为什么不动

会议 R5 要求「规范化补齐三类：同别名不同目标、XML namespace 前缀重定义、`Global` 前缀」。三条**已由编译器实现**，落在 project-import 绑定路径：

- 宿主把累积文本交给 `VisualBasicCompilation.CreateScriptCompilation(..., globalImports:=globalImports, ...)`（`VisualBasicScriptCompiler.vb:208-232`，`globalImports` 于 `:216`）；编译器在 `SourceModuleSymbol.GetBoundImports` 里逐条 `Options.GlobalImports` 用 `BinderBuilder.CreateBinderForProjectImports` 绑定（`Compilers\VisualBasic\Portable\Symbols\Source\SourceModuleSymbol.vb:381-400`），别名/前缀冲突由 `ImportData`/`Binder.BindImportClause` 判「先到胜」并报 `ERR_DuplicateNamedImportAlias1`(BC30572, `Errors.vb:439`)/`ERR_DuplicatePrefix`(BC30573, `Errors.vb:440`)。
- 既有绿测：`Compilers\VisualBasicSymbolTest\SymbolsTests\SymbolErrorTests.vb:6056-6077`（BC30572 先到胜）、`:6080-6103`（BC30573 文件级）、`:6106-6125`（BC30573 project-level）。
- **宿主不该重复实现这些规则**：它只负责「把子句文本变成 `GlobalImport` 对象」这一件事。F1 只处理这一步**失败**的情形。

**裁决规则**：当实现过程中发现某条规范化行为不符合 spec 措辞 → **不做 Z**（不在本任务改），记为 spec 措辞收敛项浮出（见 §F1-7 边界 B3）。

### F1-0.2 为什么不动编译器侧的 throwing 重载

`GlobalImport.Parse(IEnumerable(Of String))`（`Compilers\VisualBasic\Portable\GlobalImport.vb:77-86`，throw 在 `:83`）是 **public API**，且有两个既有测试**显式断言它抛 `ArgumentException`**：

- `Compilers\VisualBasicSymbolTest\UsedAssembliesTests.vb:4283`：`Assert.Throws(Of System.ArgumentException)(Sub() GlobalImport.Parse({"global"}))`
- `Compilers\VisualBasicSyntaxTest\Parser\ParseXml.vb:4504`：`Assert.Throws(Of ArgumentException)(Sub() GlobalImport.Parse(import))`

改它 = 动共享编译器树 + 破既有断言 + 需登记 `upstream-merge.md`；而宿主改用**同文件已存在的非抛重载**（`:103-108`）即可达成规范要求，代价为零。

**裁决规则**：当实现者认为「在编译器侧加个不抛的重载更干净」→ **不做 Z**：非抛重载已存在（`:103-108`），宿主直接调用；新增编译器面属越权。

---

## F1-1. 收集结构改造（携带位置与深度）

**文件**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`
**现状（已核实）**：

```vb
113  Private Shared Function GetGlobalImportsForCompilation(script As Script) As IEnumerable(Of GlobalImport)
114      Dim importNames = New List(Of String)()
115      Dim seenImports = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
116
117      AddImportNames(script.Options.Imports, importNames, seenImports)
118      AddPreviousSubmissionImports(script.Previous, importNames, seenImports)
119      Return GlobalImport.Parse(importNames)
120  End Function
122  Private Shared Sub AddImportName(importName As String, importNames As List(Of String), seenImports As HashSet(Of String))
128  Private Shared Sub AddImportNames(importList As IEnumerable(Of String), importNames As List(Of String), seenImports As HashSet(Of String))
134  Private Shared Sub AddPreviousSubmissionImports(script As Script, importNames As List(Of String), seenImports As HashSet(Of String))
139      AddPreviousSubmissionImports(script.Previous, importNames, seenImports)     ' 最老的先收集
146      For Each globalImport In previousSubmission.Options.GlobalImports
147          AddImportName(globalImport.Clause.ToString(), importNames, seenImports)
150      For Each syntaxTree In previousSubmission.SyntaxTrees
156          For Each importsStatement In root.Imports
158              For Each clause In importsStatement.ImportsClauses
159                  AddImportName(clause.ToString(), importNames, seenImports)
```

**改动形状**：

1. 新增私有嵌套类型（放 `VisualBasicScriptCompiler` 内，`Friend`/`Private` 均可，不新增 public 面）：

```vb
Private NotInheritable Class AccumulatedImportClause
    Friend ReadOnly Text As String
    Friend ReadOnly Location As Location
    Friend ReadOnly Depth As Integer
End Class
```

2. `AddImportName` 改为收 `(text As String, location As Location, depth As Integer)`；**去重键仍是 `text` + `OrdinalIgnoreCase`**（`:115` 的语义逐字保留：先到胜、大小写不敏感）。
3. `AddImportNames`（`ScriptOptions.Imports` 来源）固定传 `location:=Location.None, depth:=0`（宿主选项无源码位置）。
4. `AddPreviousSubmissionImports(script, clauses, seen, depth)`：入口调用为 `depth:=1`，递归时 `depth + 1`；两类来源的位置取值：
   - `previousSubmission.Options.GlobalImports` 的 `globalImport.Clause`（`:146-148`）：其 `Clause` 指向 `OptionsValidator.ParseImports` 造的**合成树**（`OptionsValidator.vb:29-30`），`GetLocation()` 得到的是合成树里的位置（path 为空）。**裁决**：仍取该 `Location`（比 `Location.None` 更精确，且 `MapDiagnostic` 本就不用它）；测试只对「源码树来源」断言 path/行号（见 `test-plan.md` §3 注）。
   - `root.Imports` 的 `clause`（`:156-159`）：**真实源码位置** → `clause.GetLocation()`。
5. `GetGlobalImportsForCompilation` 返回类型保持 `IEnumerable(Of GlobalImport)`（调用点 `:206`/`:216` 零改动）。

**裁决规则**：

- 当某来源无法给出 `Location` → 用 `Location.None`，**不做 Z**：不为了凑位置去改编译器或重建语法树。
- 当实现者想让 `depth` 由「链上第几层」改成「第几棵树」→ **不做 Z**：深度定义 = 距离当前提交的提交层数（见 §F1-3），树数是同一提交内的细节。
- 当去重顺序需要调整 → **不做 Z**：`OrdinalIgnoreCase` 文本先到胜是既有语义（`:115`），保持逐字等价。

**pass 条件**：

- `AddImportName`/`AddImportNames`/`AddPreviousSubmissionImports` 全部携带 `(text, location, depth)`；`GetGlobalImportsForCompilation` 之外无调用点变化。
- 无坏子句时，`Imports_CrossSubmission`（`InteractiveSessionTests.vb:24-33`）与 `Imports_DoNotReplaceInheritedOptionsImports`（`:35-47`）**零改动**通过（L2 S1/S2 用例，见 `test-plan.md`）。
- 本任务的文件 diff 中不出现 `Scripting\Core\`、`Compilers\`。

---

## F1-2. 逐子句非抛解析与降级

**文件**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`（`GetGlobalImportsForCompilation` 内）

**改动形状**：把 `:119` 的一次性 `GlobalImport.Parse(importNames)` 换成逐条解析 + 过滤：

```vb
Dim accumulated = New List(Of GlobalImport)()
Dim failures = New List(Of Diagnostic)()

For Each clause In clauses
    Dim clauseDiagnostics As ImmutableArray(Of Diagnostic) = Nothing
    ' 必须传第二个参数：命中 :103-108 的非抛重载（见下「重载陷阱」）
    Dim parsed = GlobalImport.Parse(DirectCast({clause.Text}, IEnumerable(Of String)), clauseDiagnostics)

    If clauseDiagnostics.Any(Function(d) d.Severity = DiagnosticSeverity.Error) Then
        ' 降级：该子句不进累积集；诊断留待 §F1-3 过滤 + 锚定
        failures.AddRange(clauseDiagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error))
    Else
        accumulated.AddRange(parsed)
    End If
Next
```

**重载陷阱（必须绕开，已检查）**：

- `GlobalImport.Parse(ParamArray String())`（`GlobalImport.vb:93-95`）委托到 **throwing** 重载（`:77-86`）——只传一个参数会命中它。
- `GlobalImport.Parse(String, ByRef diagnostics)`（`:68-70`）内部对结果做 `(0)` 索引（`:69`），而 `OptionsValidator.ParseImports` 会把有错子句**过滤掉**（`OptionsValidator.vb:55-57`）→ 结果可能为空。
- **本设计统一使用 `Parse(IEnumerable(Of String), ByRef ImmutableArray(Of Diagnostic))`（`:103-108`）**，并用 `DirectCast({text}, IEnumerable(Of String))` 明确类型，避免 VB 重载解析歧义。

**可测性 seam（本设计裁决）**：把「单条子句 → (GlobalImport, 诊断)」抽成一个 `Friend Shared` 纯函数，例如

```vb
Friend Shared Function ParseAccumulatedClause(
    text As String,
    <Out> ByRef diagnostics As ImmutableArray(Of Diagnostic)) As GlobalImport
```

`VisualBasicScriptCompiler` 本身是 `Friend NotInheritable Class`（`:18`），且 `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:24` 已把 IVT 授给 `Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests` ⇒ 测试工程可直接调，**不新增 public 面、不触 `PublicAPI.*.txt`**。L1 层（`test-plan.md` §2）据此直测，无需绕过 `Script` 公共入口。

**语义等价性论证（无坏子句时）**：今天一次性解析 = 把 N 条子句拼成一份文本解析；本设计 = N 次单条解析。产出对象都是 `GlobalImport`（`_clause` 各自指向所在合成树 + `Name` = 原始文本），去重已在收集期完成；编译器只按 `Options.GlobalImports` 逐条绑定（`SourceModuleSymbol.vb:381-400`），不依赖它们共享同一棵树。⇒ 无坏子句时行为等价（由 L2 S1/S2 与 L1 P-用例背书）。

**裁决规则**：

- 当某子句同时产生 Warning 与 Error → 只把 Error 计入失败集（Warning 维持今天「被 `:80-84` 静默丢弃」的等价语义），**不做 Z**：不新增 Warning 上报。
- 当 `parsed` 非空但带 Warning → 照常收进累积集。
- 当实现者想「坏子句保留文本但跳过编译」→ **不做 Z**：坏子句必须彻底不进 `GlobalImports`，否则编译器侧会二次失败。
- 当发现某类**合法**子句被逐子句解析误判 → 先判是否 `OptionsValidator` 的既有行为（同一文本在一次性解析里也会失败），是 → 属既有语义，记录并转 L1 用例；否 → 属实现缺陷，修（不得放宽为「跳过解析」）。

**pass 条件**：

- `ParseAccumulatedClause` 为 `Friend Shared` 且不抛异常（坏文本只回吐诊断）；`PublicAPI.*.txt` 零增量。
- 合法子句集合（成员导入 / 别名 / XML 前缀 / `Global` 前缀 / 带首尾空白）逐条解析全部成功且进累积集（L1 P1–P4）。
- 坏子句被丢弃且**不**出现在 `GlobalImports` 中（L2 S3）。
- 混排时合法子句仍生效（L2 S3 断言 `System.Text` 仍可用）。
- 文件内不出现 `GlobalImport.Parse(importNames)` 形式的一次性调用。

---

## F1-3. 诊断位置锚定 + 多提交链去重

### 位置

**改动形状**：对每条失败诊断执行 `diag.WithLocation(clause.Location)`（`Diagnostic.WithLocation` 是 public API，对 `VBDiagnostic`+`DiagnosticInfo` 形态有效）。`clause.Location` 的取值规则见 §F1-1.4。

- 源码树来源（`previousSubmission.SyntaxTrees` 的 `Imports` 子句）⇒ 真实 `FilePath` + 行/列，`GetLineSpan()` 可直接断言。
- 合成树来源（`previousSubmission.Options.GlobalImports` 的 `Clause`）⇒ 位置存在但 `Path` 为空；**测试不对这类断言行号**（`test-plan.md` §3 注）。
- 宿主选项来源（`script.Options.Imports`）⇒ `Location.None`。

### 去重（深度规则）

**定义**：子句条目的 `Depth` = 该条目的**首次（最老）出现**距当前提交的提交层数。当前提交的 `script.Options.Imports` = `0`；`script.Previous` = `1`；再往前每级 `+1`。收集顺序最老先（`:139` 递归在前），配合 `seenImports` 先到胜 ⇒ 条目保留的是**最老那次出现**的文本、位置与深度。

**规则**：

| 条目深度 | 处理 |
|---|---|
| `0`（宿主选项） | **报**（宿主选项是程序化输入，出现坏串即宿主缺陷；见边界 B1） |
| `1`（最近一个前序提交） | **报** |
| `≥ 2` | **丢弃子句但静默**（该文本成为深度 1 时的那次编译已报过；见边界 B2） |

**为什么这样能「只报一次」**：坏文本 T 首现于提交 K。编译 K+1 时 T 深度 1 → 报一次；编译 K+2… 时 T 深度 ≥2 → 静默。⇒ 每条**不同文本**的坏子句在整个会话里恰好报一次（`test-plan.md` R2 用例钉住）。

**为什么不去重「同一次编译内」的重复**：`seenImports` 已在收集期按文本去重（`:115`），同一文本在一次编译里最多一个条目 ⇒ 一次编译内天然无重复。若同一文本在两棵不同的树里出现（`#Load` 场景）→ 仍是同一条目。

**裁决规则**：

- 当坏子句在深度 1 报出后，用户又写了**同样的坏文本** → 那是一次新提交里的新坏文本；它成为「更新的一次出现」时深度 1 → 再报一次。**不做 Z**：不按文本做跨提交记忆（无状态可依赖）。
- 当实现者想「所有深度都报」→ **不做 Z**：会让坏子句在会话剩余生命周期内每次提交都刷屏（用户无法修改历史提交）。
- 当实现者想「只在最老那次报，后续静默」→ 已等价于本规则（深度 ≥2 静默）。
- 当某次编译里同时有深度 0 与深度 1 的失败 → 全部上报（诊断数组按收集顺序）。

**pass 条件**：

- 深度 1 用例：提交 1 含坏 `Imports`，提交 2 的诊断锚点为提交 1 的该行（`test-plan.md` R1）。
- 深度 ≥2 用例：提交 3 不再报（R2）。
- 宿主选项坏串用例：诊断 `Location.None` 且消息含该文本（R3）。
- 诊断 id/消息与 `OptionsValidator` 产出的原始诊断一致（L1 P5：断言 `Id` 与消息子串 `Error in project-level import`）。

---

## F1-4. 上报通道

**改动形状**（`GetGlobalImportsForCompilation` 末尾）：

```vb
If failures.Count > 0 Then
    Dim distinct = failures.
        GroupBy(Function(d) (d.Id, d.Location)).        ' 同一次编译内同 (id,位置) 只留一条
        Select(Function(g) g.First()).
        ToImmutableArray()
    Throw New CompilationErrorException(distinct(0).GetMessage(), distinct)
End If
Return accumulated
```

**为什么是 `CompilationErrorException`（已检查的消费链）**：

- 宿主先例：同文件 `:62-64` 的 `ThrowLoadDirectiveError` 用同一类型上报 `#Load` 诊断。
- `Script.Compile()` 捕获它并转成诊断返回：`Scripting\Core\Script.cs:332-346`（`Catch CompilationErrorException e → Return e.Diagnostics.Where(Error or Warning)`）。
- REPL：`CommandLineRunner.BuildAndRunAsync`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:370-375`）`Compile` → `DisplayDiagnostics` → `HasAnyErrors()` 则 `return (state, options)`——**跳过本提交、会话状态与选项不丢、循环继续**。
- 文件脚本：`RunScriptAsync` 的 `RunAsync` 抛出 → `:259-263` `ReportDiagnostics(e.Diagnostics, _console.Error, …)` → 返回 `CommonCompiler.Failed`。
- 打印形状：`DisplayDiagnostics` 逐条 `diagnostic.ToString()`（`：450-473`），带位置渲染。

**后果与边界**：本提交（复现坏子句的那一次）**不执行**。这是「报诊断」的必然代价（宿主没有第二个诊断出口），已由 `design-overview.md` §7 备选 B 记录并否决。

**裁决规则**：

- 当实现者想「不抛，改成写 `Console.Error`」→ **不做 Z**：`ScriptCompiler` 层拿不到 console（`VisualBasicScriptCompiler` 无 console 依赖），且 `Scripting\Core` 共享层不得改动。
- 当实现者想「把诊断塞进 `compilation.GetDiagnostics()`」→ **不做 Z**：无 API 支持（诊断只能来自 options/语法树/符号；宿主产物无注入点），且 `CreateSubmission` 的返回值是 `Compilation`。
- 当 `failures` 只含 Warning → 不会发生（§F1-2 只收 Error）。

**pass 条件**：

- `script.Compile()` 返回的诊断含坏子句诊断（不抛）；`RunAsync` 抛 `CompilationErrorException` 且 `ex.Diagnostics` 同一条（L3 A1/A2）。
- REPL 用例：坏子句提交后循环继续，下一条提交正常执行（L4 R1 的第二段）。
- 无坏子句时 `GetGlobalImportsForCompilation` 不构造任何诊断、不抛（L2 S1/S2）。

---

## F1-5. 替代方案与代价（记录在案，不采用）

| 方案 | 实现面 | 影响面 | 测试面 | 代价 / 否决理由 |
|---|---|---|---|---|
| **A（采用）** 宿主逐子句 + 丢弃 + `CompilationErrorException` | 单文件 ~40 行（`VisualBasicScriptCompiler.vb`） | 仅宿主；无坏子句零变化；坏子句提交不执行一次 | L1–L4 全内存可判 | 唯一代价 = 「坏子句提交不执行」；这是宿主唯一可用的诊断出口 |
| B 新增宿主诊断 sink（非阻塞） | 需在 `ScriptCompiler`/`ScriptOptions` 或新 seam 上加 `Friend` 回调 + 装配 + 宿主订阅 | 新面跨程序集；`VisualBasicScript` 公共入口也要订阅才不漏报 | 需新 fake sink | 否决：为一个「已能表达」的事实引入第二出口；且 `Scripting\Core` 共享层改动需 merge 账本 |
| C 改编译器 throwing 重载 | `GlobalImport.vb:77-86` 去 throw | 共享编译器树 + public API 语义变更 + 破两处 `Assert.Throws` + merge 账本 | 需改既有测试 | 否决：成本远高于 A，收益相同 |
| D 把坏文本塞进 `GlobalImports` 让编译器报 | 需构造 `GlobalImport`（ctor `Friend`，且需 `ImportsClauseSyntax`） | 编译器侧仍经 `MapDiagnostic` 丢位置（`:111-130`）；且坏子句仍会让编译器解析失败 | — | 否决：不可行（无法在不改编译器的前提下构造） |

---

## F1-6. 边界与角案例

| # | 情形 | 期望行为 | 收口用例 |
|---|---|---|---|
| E1 | 同一坏文本在多棵树里出现 | 收集期去重 → 一次编译内只报一次 | R4 |
| E2 | 坏子句与合法子句混排 | 合法子句照常生效（降级不连坐） | S3 |
| E3 | 当前提交自己写错 `Imports` | **不进**累积路径 → 由文件级导入报错（本任务不改变该行为） | S4（负测试） |
| E4 | 首个提交（`script.Previous Is Nothing`） | 只收 `script.Options.Imports`（depth 0） | S5 |
| E5 | `#Load` 树里的坏 `Imports` | 与主树同属一个提交（同一 depth），位置指向被加载文件的 `FilePath` | S6（可选，若基建允许） |
| E6 | 别名 / XML 前缀 / `Global` 前缀子句 | 逐子句解析不误报（`GlobalImport.Parse({"<xmlns:db=…>"})` 等既有可用性见 `SymbolErrorTests.vb:6106`、`UsedAssembliesTests.vb:2008/2016`） | P3/P4 |
| E7 | 大小写不同的同一文本（`imports system.text` vs `Imports System.Text`） | 去重命中 → 只解析一次、只报一次 | P6 |
| E8 | 子句带首尾空白 / 内嵌引号 | `clause.ToString()` 不含 trivia；文本形态与今天一致 | P3 |
| E9 | 坏文本在深度 1 报过之后用户又写同一坏文本 | 新出现的那次成为深度 1 → 再报一次（新提交的新错误） | R5 |
| E10 | 宿主选项坏串（depth 0） | 每次编译都报（选项每次重新施加） | R3 + 边界 B1 |
| E11 | 诊断数组的顺序 | 按收集顺序（最老先）→ 可断言 | R1 |

---

## F1-7. 不留遗留问题的收口方式（每项带单元测试断言）

| 设计点 | 收口断言（测试编号见 `test-plan.md`） |
|---|---|
| F1-1 收集结构 | S1/S2 断言无坏子句时既有跨提交 Imports 行为逐字不变（含 options 导入不被覆盖） |
| F1-2 降级 | S3 断言坏子句不进累积集、合法子句仍生效；P5 断言诊断 id/消息来自既有 `OptionsValidator` 产物 |
| F1-3 位置 | R1 断言 `Location.GetLineSpan().Path/Line` = 肇事提交的行号；R3 断言 `Location.None` 回退 |
| F1-3 去重 | R2 断言第二次起不再报；R4 断言同一编译内同文本只报一次 |
| F1-4 通道 | A1 断言 `script.Compile()` 返回诊断；A2 断言 `RunAsync` 抛 `CompilationErrorException` 且 `Diagnostics` 一致；R1 断言 REPL 继续 |
| 零越权 | F1.5 收口 gate：`git diff --stat` 仅 `Scripting\VisualBasic\` + 新测试文件；`PublicAPI.*.txt` 零增量 |
| 全量回归 | `Scripting\VisualBasicTest` 直跑 `-automated` 全量 0 失败 + 七门 gate 与基线一致（`test-plan.md` §7） |

### 已记录边界（不是遗留问题，是设计选择，需 spec/会议知晓）

- **B1**：depth 0（宿主选项）失败每次编译都报（选项每提交重新施加）。理由：不得静默优先于不重复；宿主选项是程序化输入，坏串即宿主缺陷。
- **B2**：depth ≥2 静默丢弃。理由：该文本成为深度 1 的那次编译已报过；历史提交不可修改，重复报只会刷屏。
- **B3**（与 spec 措辞的差异，浮出给 spec）：spec `:349` 说规范化冲突的诊断「落在失败的那条子句的位置」，但实现里 project-level import 诊断按设计恒 `NoLocation`（`SourceModuleSymbol.vb:460-465` 注释、`GlobalImport.MapDiagnostic:111-130`），子句身份靠消息文本（`ImportDiagnosticInfo.vb:21-24`）。本任务**不**改这条既有设计；本任务只保证**解析失败**类诊断带真实位置。
- **B4**：合成树来源（`Options.GlobalImports` 的 `Clause`）的位置 `Path` 为空——测试不对其断言行号。

---

## 11. 关卡决策

- **详细设计关卡：通过**——文件级落点、改动形状、状态变化（子句条目生命周期）、错误处理（降级 + 抛出通道）、验证方法（L1–L4 编号用例）齐备；无「实现时再定」的悬空设计；四项待定全部裁决。
- **任务列表关卡：见 `README.md` Vortex 表（F1.1–F1.5）**。
- **停止条件**：若实施中发现规范要求与既有编译器行为冲突（例如某类合法子句无法逐条解析）→ 停止并回本设计打回，不得静默放宽为「跳过解析」。
