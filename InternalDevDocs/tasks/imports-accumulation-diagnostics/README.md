# 任务：`Imports` 跨提交累积的解析失败降级为诊断（imports-accumulation-diagnostics）——任务计划

> **状态：已关闭（无需改动）** —— 2026-09-09 用户裁决。本计划的 F1 前提经实证**不成立**：`GlobalImport.Parse(IEnumerable)` 的 `ArgumentException` 经**真实用户输入不可达**（直投 `ScriptOptions.Imports` 的字符串已被同一解析器验证过；回放只收零错误提交的子句，17 种合法 `Imports` 形态实测全不抛；`.vbx` 单文件提交只是普通编译错误）。故**零代码改动**关闭 F1，spec 保留该规范要求为「宣称支持」。实证证据：`Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb`（19 用例全绿、全量 318 通过、产品源码零改动）。实证中发现的**可达**缺陷另立 `../../issues/issue-vbi-imports-switch-nre.md`（`/imports:` 坏值 NRE）。本文件夹保留为「已评估 / 无需改动」记录；若日后库 API 防御性加固需求上升可复活。

本文件夹是 `proposal-scripting-dialect` 后续实施项 **F1** 的任务计划存储（Vortex 代办拆分 + 概要/详细设计 + 测试计划）。本任务是「让宿主的 `Imports` 跨提交累积路径在遇到无法解析的累积子句时**报诊断而非抛异常**」的实现计划：宿主逐子句解析、坏子句降级丢弃、诊断锚定在肇事子句位置、多提交链只报一次；编译器侧零改动。

- **一句话**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的累积路径把「无法解析的子句」从 `GlobalImport.Parse` 的 `ArgumentException`（宿主/会话崩溃）改为一条锚定在肇事子句位置的 `CompilationErrorException` 诊断（宿主打印后继续），无坏子句时全链路零行为变化。
- **依据链**（唯一权威 = 会议 RESOLUTION + spec 规范要求）：
  `../../spec/spec-scripting-dialect.md`（§`Imports` across submissions / Normalization / **Failure**，`:213` 明文规范要求）→ `../../meetings/meeting-scripting-dialect.md`（2026-09-09，RESOLUTION R5 + TODO 条目 `:171`；R11/`:172` 属 F2，**已移交**）→ `../../proposals/proposal-scripting-dialect.md`（`§6` `:98-104`；正文冻结，以 RESOLUTION 为准）→ `../../decisions.md`（M2/M5）→ `../../compilers-index.md`（源码地图）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`，改动蓝图 + 裁决规则 + pass 条件）、测试计划（`test-plan.md`，L1–L4 分层 + 无副作用纪律 + 全量回归口径）、本 README（Vortex 代办拆分表 + 共享源码事实 + accepted 门 + 状态行）。
- **调度方式**：Vortex 涡流（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度，串行交替、不可催促。流水账：`<项目根>/tmp/vortex-logs/`。

## 范围与非范围

**范围内（F1）**：

- 宿主累积路径的**收集结构改造**：从「纯字符串 + `HashSet(Of String)` 去重」改为携带 `(子句文本, 位置, 链深度)` 的子句条目，去重键与语义保持不变（`OrdinalIgnoreCase` 文本，先到胜）。
- **逐子句解析 + 降级**：用 `GlobalImport.Parse(IEnumerable(Of String), ByRef diagnostics)` 非抛重载（`Compilers\VisualBasic\Portable\GlobalImport.vb:103-108`）替代 throwing 重载（`:77-86`）；含 Error 级诊断的子句**丢弃**，其余照常进 `GlobalImports`。
- **诊断锚定**：坏子句的诊断重锚到该子句所在语法树的真实 `Location`（`clause.GetLocation()`）；无源码来源的子句回退 `Location.None`。
- **多提交链去重**：只在肇事子句距当前提交**深度 1**（即最近一个前序提交）时报告一次；更深层静默丢弃（该子句成为深度 1 时已报过）。
- **无副作用单测**（L1–L4，见 `test-plan.md`）+ 全量回归收口。

**非范围（显式）**：

- **提交类 `WithEvents`/`Handles` 支持（原 F2）不在本任务范围**。用户 2026-09-09 裁决：该能力是**新特性**（非 bug 修复），必须走 **proposal → LDM meeting → 再修正任务计划** 的流程。F2 的深挖成果已另存工作材料 `tmp\proposals\with-events-in-submissions\design-notes.md`（git-ignored，不进 `InternalDevDocs\`），供 propose 阶段直接取用。
- **不改编译器侧** `GlobalImport.Parse` throwing 重载（`:77-86`）。它是 public API 且**有测试钉住其抛异常行为**（`Compilers\VisualBasicSymbolTest\UsedAssembliesTests.vb:4283`、`Compilers\VisualBasicSyntaxTest\Parser\ParseXml.vb:4504` 的 `Assert.Throws(Of ArgumentException)`）——改它会动共享编译器树 + 破既有断言 + 需要 merge 账本，收益为零。
- **不改规范化三条**（同别名不同目标 → 先到胜 + BC30572；XML 前缀重定义 → BC30573；`Global` 前缀）。它们由编译器的 project-import 绑定路径实现，宿主只负责把文本喂进 `GlobalImports`；本任务只处理「喂不进去」的子句（理由与锚点见 `design-detailed.md` §F1-0）。
- **不改「同别名不同目标 / XML 前缀重定义」的期望行为**（会议 OPEN QUESTION `:170`，与本项正交，仍留会议跟踪）。
- **不新增宿主诊断 sink / 不新增 public API 面**（备选方案见 `design-detailed.md` §F1-5，已否决并记录理由）。
- **不动 `Scripting\Core` 共享层**：全部改动落在 `Scripting\VisualBasic\`（宿主层），`PublicAPI.*.txt` 零增量。

## 前置决策

**无。** F1 的所有待定项（诊断码、诊断位置、多提交去重、net48/Desktop 分支）已在本计划中带源码锚点裁决完毕（见 `design-detailed.md` §F1-0–§F1-5）。原 F2 的 A/B 二选一不再是本任务的前置决策——它已整体移交 proposal 流程（见上「非范围」）。

## Vortex 代办拆分表（供实施者/验证者无人值守串行）

> 编号 = 功能拆分标识，**非执行顺序**；执行顺序见各条目「前置」。每项 pass 条件可自动判（实施者做完 → 验证者按 pass 条件核对 → 打回/通过）。状态初始 `todo`。所有实现改动以 `design-detailed.md` §F1-0–§F1-5 为唯一底稿，不得偏离。
>
> **测试执行规约**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**禁用 `dotnet test`**（EXIT 0 但静默不跑）；验证者先 `dotnet build`，再直跑 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或加 `-class <FQN>`（类级）/`-method <FQN>`（单测；FQN 须完整到 `命名空间.类.方法`，缺段或拼错会静默 0 跑，须核对 `TestCasesToRun > 0`）。见 `design-detailed.md` §0 + memory `vb-scripting-test-runner`。

| # | 功能（design-detailed 章节） | pass 条件（全部满足才算过） | 前置 | 状态 |
|---|---|---|---|---|
| F1.1 | 收集结构改造：子句条目 `(Text, Location, Depth)` + 递归深度参数（§F1-1） | `AddImportName`/`AddImportNames`/`AddPreviousSubmissionImports` 改造后仍以 `StringComparer.OrdinalIgnoreCase` 文本去重（先到胜不变）；`AddPreviousSubmissionImports` 递归时 `depth + 1`；`GetGlobalImportsForCompilation` 返回值形状不变（`IEnumerable(Of GlobalImport)`）；无坏子句时 `Imports_CrossSubmission`/`Imports_DoNotReplaceInheritedOptionsImports` 两既有测试零改动通过 | 无 | todo |
| F1.2 | 逐子句解析 + 降级 + 诊断锚定（§F1-2/§F1-3） | 用 `GlobalImport.Parse(DirectCast({text}, IEnumerable(Of String)), diags)` 非抛重载（**不得**用 `Parse(String, ByRef)` 重载，见 §F1-2 陷阱）；坏子句丢弃、其余照常累积；诊断经 `diag.WithLocation(clauseLocation)` 锚定，`Location.None` 回退仅在无源码来源时出现；无坏子句时不构造任何诊断 | F1.1 | todo |
| F1.3 | 多提交链去重 + 抛出通道（§F1-3/§F1-4） | 仅 `depth = 1` 的子句产生诊断（深度 ≥2 静默丢弃）；待报诊断非空时 `Throw New CompilationErrorException(msg, diags)`（沿用 `:62-64` `ThrowLoadDirectiveError` 先例）；`script.Compile()` 返回该诊断且 `RunAsync` 抛 `CompilationErrorException` 携带它；REPL 该提交被跳过、会话继续（由 F1.4 用例钉住） | F1.2 | todo |
| F1.4 | 单元测试（L1–L4 全矩阵，`test-plan.md` §2–§5） | `test-plan.md` 编号用例 P1–P7 / S1–S7 / A1–A5 / R1–R6 全部落地且全绿；无副作用（Grep 抽查无 `File.Write`/`Directory.Create`/`Process.Start`/`HttpClient`/`Registry`）；位置断言用 `Location.GetLineSpan().Path/Line` 而非消息字符串匹配 | F1.3 | todo |
| F1.5 | 全量收口 gate（§`test-plan.md` §7） | `Scripting\VisualBasicTest` 直跑 `-automated` **全量 0 失败**；七门 gate（`scripts\verify-vb-compiler-tests.ps1`）逐门与基线数字一致；`PublicAPI.*.txt` 零增量；`Scripting\Core`/`Compilers\**` 零 diff（本任务宿主单侧）；无遗留 Unresolved | F1.1–F1.4 全过 | todo |

> **执行范围**：本表全部为**无人值守串行**项（pass 全自动判，无真实网络/进程/写盘）。本任务不引入任何门控集成验收项（无真实 restore / 无跨平台资产 / 无 net48 物理机需求）。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-09-09，本代理逐条 Read/Grep 复核行号；工作树状态 = 当前 `with-modified-vbsyntax` 分支）。引用以 `文件:行号` 给出；证据等级按 `manifest.md` 证据阶梯标注。文件相对路径均相对仓库根。

### 宿主累积路径（改动面）

- **`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:113-120`** `GetGlobalImportsForCompilation`：`AddImportNames(script.Options.Imports, …)` → `AddPreviousSubmissionImports(script.Previous, …)` → `Return GlobalImport.Parse(importNames)`（`:119`，**throwing 重载**）。**证据等级：已检查。**
- **`…:115`** 去重键 `New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)`；**`:122-132`** `AddImportName`/`AddImportNames`（只收字符串）；**`:134-162`** `AddPreviousSubmissionImports`：先递归 `script.Previous`（`:139`，最老的先收集），再收 `previousSubmission.Options.GlobalImports` 的 `globalImport.Clause.ToString()`（`:146-148`）与各语法树 `CompilationUnitSyntax.Imports` 的 `clause.ToString()`（`:150-161`）。**证据等级：已检查。**
- **`…:206`** `Dim globalImports = GetGlobalImportsForCompilation(script)` → **`:216`** 作为 `globalImports:=globalImports` 传入 `VisualBasicCompilation.CreateScriptCompilation`。**证据等级：已检查。**
- **`…:170-179`** `CreateSubmission` 内 `Dim diagnostics = DiagnosticBag.GetInstance()` … `'  TODO report Diagnostics` … `diagnostics.Free()`——宿主已有「拿到诊断却没处报」的既有 TODO；本任务不改这条通道（见 §F1-4 的抛出方案）。**证据等级：已检查。**
- **`…:62-64`** `ThrowLoadDirectiveError(diagnostic)` → `Throw New CompilationErrorException(diagnostic.GetMessage(), ImmutableArray.Create(diagnostic))`——**宿主 `CreateSubmission` 内用 `CompilationErrorException` 上报诊断的既有先例**（`#Load` 文件找不到 / 循环）。**证据等级：已检查。**

### 编译器侧（零改动，但决定诊断形状）

- **`Compilers\VisualBasic\Portable\GlobalImport.vb:77-86`** `Parse(IEnumerable(Of String))`：`OptionsValidator.ParseImports` 后取首个 Error 级诊断，非空则 `Throw New ArgumentException(firstError.GetMessage(...))`（`:82-84`）。**这是本任务要绕开的 throw。** **证据等级：已检查。**
- **`…:103-108`** `Parse(IEnumerable(Of String), ByRef diagnostics As ImmutableArray(Of Diagnostic))`：**非抛重载**，坏子句被过滤、诊断原样回吐。**本任务改用它。** **证据等级：已检查。**
- **`…:68-70`** `Parse(String, ByRef diagnostics)`：内部 `Return Parse({importedNames}, diagnostics)(0)`（`:69`）——结果可能为空（见下条）时索引 `(0)` 不可靠，**本任务不使用该重载**。**证据等级：已检查。**
- **`…:93-95`** `Parse(ParamArray String())` 委托到 `:77`（throwing）——**重载解析陷阱**：`Parse({text})` 会命中 throwing 路径，必须显式传第二个 `diagnostics` 参数。**证据等级：已检查。**
- **`Compilers\VisualBasic\Portable\OptionsValidator.vb:21-67`** `ParseImports`：把每个子句拼成 `"Imports " + name + vbCrLf + vbCrLf` 造**合成语法树**（`:29-30`）→ 逐子句取 `clause.GetSyntaxErrors(tree)`（`:41`）→ 经 `GlobalImport.MapDiagnostic` 映射（`:50-53`）→ **只有无语法错误的子句才进结果**（`:55-57`）。**证据等级：已检查。**
- **`Compilers\VisualBasic\Portable\GlobalImport.vb:111-130`** `MapDiagnostic`：两个分支都返回 `NoLocation.Singleton`（`:113`、`:128`），消息由 `ImportDiagnosticInfo` 渲染为 `Error in project-level import '{0}' at '{1}' : {2}`（`GlobalImport.ImportDiagnosticInfo.vb:21-24`，ERRID `ERR_GeneralProjectImportsError3`）。**结论：编译器侧的 project-import 诊断天然无位置，肇事子句身份靠消息文本——所以「位置」只能由宿主补。** **证据等级：已检查。**
- **`Compilers\VisualBasic\Portable\Symbols\Source\SourceModuleSymbol.vb:381-400`** 逐条 `Options.GlobalImports` 绑定 + `MapDiagnostic` 映射（跳过 `ERR_DuplicateImport1`）；**`:460-465`** 注释明文「Do not expose any locations for project level imports」——**规范化三条的实现位置**（宿主不参与）。**证据等级：已检查。**
- **既有测试钉住 throwing 行为**：`Compilers\VisualBasicSymbolTest\UsedAssembliesTests.vb:4283`、`Compilers\VisualBasicSyntaxTest\Parser\ParseXml.vb:4504` 均 `Assert.Throws(Of ArgumentException)`；`SymbolErrorTests.vb:6106-6125` 钉住 project-level BC30573 的**无位置消息形状**。**证据等级：已检查。**
- **规范化三条的既有绿测**：`SymbolErrorTests.vb:6056-6077`（BC30572 先到胜）、`:6080-6103`（BC30573 文件级有位置）、`:6106-6125`（BC30573 project-level 无位置 + 消息含子句文本）。**证据等级：已检查。**

### 宿主诊断通道（决定「诊断怎么被用户看到」）

- **`Scripting\Core\Script.cs:332-346`** `CommonCompile`：`Try GetPrecedingExecutors/GetExecutor` → `Catch CompilationErrorException e` → `Return ImmutableArray.CreateRange(e.Diagnostics.Where(Error or Warning))`。**结论：`CreateSubmission` 抛出的 `CompilationErrorException` 会被 `Script.Compile()` 转成诊断返回，不会打死宿主。** **证据等级：已检查。**
- **`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:370-375`** REPL 每提交：`newScript.Compile(...)` → `DisplayDiagnostics(diagnostics)` → `HasAnyErrors()` 则跳过执行并 `return (state, options)`（会话继续、状态不丢）。**证据等级：已检查。**
- **`…:259-263`** 文件脚本路径：`script.RunAsync` 抛 `CompilationErrorException` → `_compiler.ReportDiagnostics(e.Diagnostics, _console.Error, …)` → 返回 `CommonCompiler.Failed`。**证据等级：已检查。**
- **`…:450-473`** `DisplayDiagnostics`：逐条 `_console.Error.WriteLine(diagnostic.ToString())`（**带位置渲染**）+ 超出上限时补一行汇总。**证据等级：已检查。**
- **`…:344-352`** REPL 编译前 seam（NuGet 协调器）已示范「编译前拿到诊断 → `DisplayDiagnostics` → 有错则 `continue`」的宿主形状。**证据等级：已检查。**

### 测试基建

- **`Scripting\VisualBasicTest\InteractiveSessionTests.vb:14-22`** `Fields`（`RunAsync → ContinueWith → ContinueWith`，断言跨提交可见）；**`:24-33`** `Imports_CrossSubmission`；**`:35-47`** `Imports_DoNotReplaceInheritedOptionsImports`。**证据等级：已检查。**
- **`Scripting\VisualBasicTest\ScriptTests.vb:571-587`** `TestMissingLoadDirectiveFileReportsAtLoadLine`：`Try Await script.RunAsync() … Catch ex As CompilationErrorException` + `ex.Diagnostics.Single()` + `Location.GetLineSpan().Path` + 行号断言——**本任务 L3/L4 位置断言的模板**；**`:525-548`/`:550-569`** 示范「内存 resolver + 真实文件路径 + 行号断言」，**不落盘**。**证据等级：已检查。**
- **`Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb:5-10`** `Friend NotInheritable Class TestConsoleIO`（`New(input As String)`，内存 `StringReader`/`StringWriter`）。**证据等级：已检查。**
- **`Scripting\VisualBasicTest\ScriptTests.vb:463-468`** `AssertDiagnosticsContainAny` 走 `script.GetCompilation().GetDiagnostics()`——**注意**：本任务的诊断在 `CreateSubmission` 抛出，**不在** `GetCompilation()` 的诊断集里，用例必须走 `script.Compile()` / `RunAsync` + catch（见 `test-plan.md` §5 注）。**证据等级：已检查。**
- **`scripts\verify-vb-compiler-tests.ps1:7-15`** 七门 gate（Phase2/Syntax/Symbol/Semantic/IOperation/Emit/CommandLine）与基线数字；`:27-29` 走 `dotnet test`（与 `Scripting\VisualBasicTest` 的直跑规约不同）。**证据等级：已检查。**

## Accepted 门（计划被批准进入实施的条件）

验证者（或作者）核对以下全部成立，计划才算 accepted，之后才允许按上表无人值守串行实施：

1. **规范吸收完整**：spec `:213` 的两句要求（「报诊断而非抛异常」「诊断落在肇事子句位置」）在 `design-overview.md`/`design-detailed.md` 各有落点；会议 TODO `:171` 被显式吸收；F2（R11/`:172`）在「非范围」显式移交并给出 design-notes 路径。
2. **源码事实真实**：本 README「共享源码事实」与 `design-detailed.md` 每条 `文件:行号` 经验证者 Read/Grep 复核与源码一致。
3. **决策点收敛**：无待作者决策点（F1 四项待定已裁决，见 `design-detailed.md` §F1-0–§F1-5；F2 已移交）。
4. **pass 条件可判**：Vortex 表每项 pass 条件客观可自动判（实施/验证无需问人）；无「实现时再定」的悬空设计。
5. **测试计划齐备**：`test-plan.md` 的 L1–L4 矩阵与 Vortex 各项测试面一一对应；无副作用纪律与全量回归口径明确；用例编号可数、断言点可判。
6. **零越权承诺**：不改编译器侧 throwing 重载、不改规范化三条、不新增 public API、不动 `Scripting\Core`。
7. **文档纪律**：正文简体中文；引用仓库相对路径；无本机绝对路径/用户名/机器特定状态；无过程日志文体。

## 关键设计决策摘要（源自本计划裁决，实现期不得翻转）

1. **改动落点全在宿主**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`（累积路径）+ 新测试文件；`Scripting\Core`/`Compilers\**` 零 diff。
2. **诊断码复用现有 BC 码**：沿用 `OptionsValidator.ParseImports` 产出的原始 `Imports` 子句语法错误诊断 id/文案（经 `ImportDiagnosticInfo` 渲染为 `Error in project-level import '…' at '…' : …`），**零新码、零 xlf 改动**。
3. **诊断位置由宿主补**：`diag.WithLocation(clause.GetLocation())`（前序提交语法树里的真实位置）；无源码来源（`script.Options.Imports`）回退 `Location.None`。
4. **上报通道 = `CompilationErrorException`**：`Script.Compile()` 会转成诊断（`Script.cs:332-346`），REPL 打印后继续（`CommandLineRunner.cs:370-375`）；与 `#Load` 先例（`:62-64`）同形。
5. **多提交去重 = 深度 1 才报**：坏子句在距当前提交最近一层时报一次；更深层静默丢弃（避免每次提交重复刷屏，同时不永久阻塞会话）。
6. **不改编译器 throwing 重载**：public API + 既有 `Assert.Throws` 断言钉住；宿主用非抛重载即可。

## 状态行

- **计划状态：Accepted 待验证者核对**（本文件夹四件套已产出；无待作者决策点）。
- **实现状态：未开始**（Vortex 表 F1.1–F1.5 全 `todo`）。
- **移交项**：F2（提交类 `WithEvents`/`Handles`）→ `tmp\proposals\with-events-in-submissions\design-notes.md` → proposal → LDM meeting。
