# 测试计划：`Imports` 跨提交累积的解析失败降级为诊断

> 状态：测试计划。依据链：`../../spec/spec-scripting-dialect.md:213`（规范要求）→ `../../meetings/meeting-scripting-dialect.md`（RESOLUTION R5 + TODO `:171`）→ `design-overview.md` → `design-detailed.md`（§F1-0–§F1-7 + pass 条件）→ 本测试计划。
> 测试宿：`Scripting\VisualBasicTest\`（MTP 项目：`dotnet test` 静默不跑，须直接跑程序集 `-automated`，见 memory `vb-scripting-test-runner`）。**本任务无共享编译器改动**，因此不新增七门 gate 内的用例；七门只作全量回归口径（§7）。
> 无副作用纪律（CLAUDE.md）：单测禁止网络 / 文件写入 / 进程启动 / 注册表写入。本任务全部用例为**内存 I/O**：L1 纯函数、L2/L3 走 `VisualBasicScript.Create/RunAsync/ContinueWith`、L4 走 `CommandLineRunner` + 内存 `TestConsoleIO`（`Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb:5-10`）。L4 复用 `CommandLineRunnerTests.CreateRunner`（`CommandLineRunnerTests.vb:91-116`）时其 `CreateIsolatedTempDirectory()` 是仓库既有隔离临时目录基建（仅宿主 `BuildPaths` 用，不写脚本文件）；若验证者判定该隔离目录不合纪律，改用纯 `VisualBasicScript` 内存链（L3）等价覆盖。

## 1. 测试分层矩阵

| 层 | 适用性 | 落点 | 说明 |
|----|--------|------|------|
| **L1 解析** | ✅ | `ParseAccumulatedClause`（`Friend Shared`，IVT 可达） | 单条子句 → 无错/有错；诊断 id/消息形状；去重不误判 |
| **L2 语义** | ✅ | `VisualBasicScript.RunAsync(...).ContinueWith(...)` | 跨提交累积：坏子句降级、合法子句仍生效、options 导入不被覆盖 |
| **L3 API/接线** | ✅ | `Script.Compile()` / `RunAsync` + `CompilationErrorException` | 上报通道：诊断在 `Compile()` 返回、`RunAsync` 抛出、位置正确 |
| **L4 宿主/REPL** | ✅ | `CommandLineRunner.RunInteractive()` + `TestConsoleIO` | 端到端：打印一次、会话继续、不重复报 |

> 四层全适用：本任务改动面在宿主累积路径，L1 可直测（新增 `Friend` seam），L2/L3 走公共 `Script` API，L4 走 REPL 宿主。

## 2. L1 解析层（`ImportsAccumulationDiagnosticsTests.vb`，新增）

> 落点 `Scripting\VisualBasicTest\ImportsAccumulationDiagnosticsTests.vb`（xunit `[Fact]`，风格参照 `ScriptTests.vb`）。调用 `VisualBasicScriptCompiler.ParseAccumulatedClause(text, ByRef diags)`（`Friend Shared`；`Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:24` IVT 已授）。全部纯内存。

| # | 用例 | 输入（子句文本，即 `clause.ToString()` 的产物形态） | 断言 | 无副作用 |
|---|------|------|------|---------|
| P1 | 成员导入 | `System.Text` | 无 Error 诊断；返回值非 Nothing 且 `.Name = "System.Text"` | 纯内存 |
| P2 | 别名导入 | `R = System.Text` | 无 Error；`.Name` 含 `R = System.Text` | 纯内存 |
| P3 | XML 前缀 + 首尾空白 | `<xmlns:db="http://example.org/database">`、`  System.Text  ` | 无 Error（首尾空白由 `OptionsValidator.ParseImports` 的 `Unquote`/拼接语义处理） | 纯内存 |
| P4 | `Global` 前缀 | `Global.System.Text` | 无 Error（`Global` 合法性由绑定期判定，解析期不得误判） | 纯内存 |
| P5 | 坏子句 | `Global`（既有反例：`UsedAssembliesTests.vb:4283`）、`Foo.`、`R =` | 至少一条 Error 诊断；诊断消息含 `Error in project-level import`（`ImportDiagnosticInfo.vb:21-24` 渲染形状） | 纯内存 |
| P6 | 大小写去重不误判 | `System.Text` 与 `system.text` | 两个条目在收集期被 `OrdinalIgnoreCase` 去重 → 解析只发生一次（断言收集条目数 1；见 L2 S7） | 纯内存 |
| P7 | 空输入 | 空列表 | 返回空累积集、零诊断、不抛 | 纯内存 |

> **不变量**：`ParseAccumulatedClause` 对坏文本**不抛异常**（对照既有 `GlobalImport.Parse({text})` 会抛 `ArgumentException`，`GlobalImport.vb:77-86`）。

## 3. L2 语义层（跨提交累积）

> 走公共 `VisualBasicScript` API（`InteractiveSessionTests.vb:14-47` 同款写法）。`C:\scripts\…` 形态的路径断言模板见 `ScriptTests.vb:525-569`（内存 `SourceReferenceResolver`，不落盘）。

| # | 用例 | 构造 | 断言 | 无副作用 |
|---|------|------|------|---------|
| S1 | 合法链不回归 | sub1 `Imports System.Text` → sub2 `Dim sb = New StringBuilder()` | 两提交零错、运行成功（既有 `Imports_CrossSubmission` 等价） | 纯内存 |
| S2 | options 导入不被覆盖 | options `.AddImports("System")`；sub1 `Dim c = GetType(Console)` → sub2 `Imports System.Text` → sub3 `? GetType(Console).FullName` | `System.Console`（既有 `Imports_DoNotReplaceInheritedOptionsImports` 等价） | 纯内存 |
| S3 | **降级不连坐** | sub1 = 坏子句 `Imports Foo.` + 合法 `Imports System.Text`；sub2 = `? New StringBuilder().GetType().Name` | sub2 编译成功且返回 `StringBuilder`；sub2 同时收到坏子句诊断（`Compile()` 返回） | 纯内存 |
| S4 | 当前提交自己写错 | 单提交 `Imports Foo.`（无前序） | 诊断由**文件级导入**路径产出（普通 `Imports` 语法错误 id），**不**出现 `Error in project-level import` 消息（负测试：本通道不接管） | 纯内存 |
| S5 | 首个提交只有 options | options `.AddImports("System.Text")`；单提交 `? New StringBuilder().GetType().Name` | `StringBuilder`，零诊断 | 纯内存 |
| S6 | `#Load` 树里的坏 `Imports`（可选） | 主文件 `#Load "a.vbx"`，`a.vbx` 含坏 `Imports` | 诊断位置 `GetLineSpan().Path` = 被加载文件路径（与主树同属一个提交、同一 depth） | 纯内存（内存 resolver，同 `ScriptTests.vb:529-541`） |
| S7 | 同文本跨树去重 | sub1 的坏 `Imports Foo.` + sub2 再写同样文本 | sub2 编译时该文本一次编译内只产一条诊断 | 纯内存 |

> **注（位置断言口径）**：S3/S6 的坏子句来自**源码树** ⇒ `Location.GetLineSpan().Path/Line` 可断言。若坏文本来自 `previousSubmission.Options.GlobalImports`（合成树，`OptionsValidator.vb:29-30`）⇒ `Path` 为空，**不断言行号**（`design-detailed.md` 边界 B4）。

## 4. L3 API / 通道层

> 关键事实（已检查）：本任务的诊断在 `CreateSubmission` 抛出，**不在** `compilation.GetDiagnostics()` 里（`design-detailed.md` §F1-4）；因此**不得**用 `ScriptTests.vb:463-468` 的 `GetCompilation().GetDiagnostics()` 写法，必须走 `Compile()` / `RunAsync`。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| A1 | `Compile()` 返回诊断（不抛） | sub1 坏 `Imports`，sub2 `script.Compile()` | 不抛；返回集含坏子句诊断；`Id` = 既有 `OptionsValidator` 产物 id；消息含 `Error in project-level import` | 纯内存 |
| A2 | `RunAsync` 抛 `CompilationErrorException` | 同上，sub2 `Await script.RunAsync()` | `Catch ex As CompilationErrorException`；`ex.Diagnostics` 与 A1 一致（模板：`ScriptTests.vb:571-587`） | 纯内存 |
| A3 | 位置锚定 | sub1（两行：第 1 行坏 `Imports Foo.`，第 2 行合法 `Dim a = 1`）→ sub2 | 诊断 `Location.GetLineSpan().Line + 1 = 1`（肇事提交的行号），且 `Location <> Location.None` | 纯内存 |
| A4 | `Location.None` 回退 | options `.AddImports("Foo.")`（坏串） | 诊断 `Location = Location.None`，消息含 `Foo.` | 纯内存 |
| A5 | 无坏子句零诊断 | 合法链（S1 形状） | `Compile()` 返回集不含任何本通道诊断 | 纯内存 |

## 5. L4 宿主 / REPL 层

> 落点同文件（`CommandLineRunner` + `TestConsoleIO`，基建 `CommandLineRunnerTests.vb:91-116`）。输入串用 `vbCrLf` 分隔多条提交。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| R1 | 端到端：报一次 + 会话继续 | `Imports Foo.`（坏）→ `Imports System.Text` → `? New StringBuilder().GetType().Name` | `io.Error` 含坏子句诊断文本（含 `Error in project-level import`）；`io.Out` 最终含 `StringBuilder`（会话未死、后续提交执行） | 纯内存 |
| R2 | 不重复报 | 接 R1 再追加一条 `? 1` | 第二条之后 `io.Error` 中该诊断文本**只出现一次**（深度 ≥2 静默） | 纯内存 |
| R3 | 用户再写同一坏文本 | R1 之后追加 `Imports Foo.` → `? 2` | 该诊断**再出现一次**（新提交的新错误，深度 1） | 纯内存 |
| R4 | 同一编译内同文本只报一次 | 单提交内两处 `Imports Foo.`（多 clause / 多语句） | 诊断只出现一次（收集期去重） | 纯内存 |
| R5 | 坏子句提交不执行 | 坏 `Imports Foo.` 与 `? 42` 同属一条提交 | 该提交的 `? 42` **不**输出（本提交被跳过，`CommandLineRunner.cs:370-375`）；下一条提交正常 | 纯内存 |
| R6 | 文件脚本路径不回归 | `CommandLineRunnerTests` 既有 `.vbx` 用例（合法脚本） | 零诊断、退出码与既有断言一致 | 见 §6 |

## 6. VB 特性维度

| 维度 | 覆盖 | 用例 |
|---|---|---|
| `Imports` 子句形态 | 成员导入 / 别名 / XML namespace 前缀 / `Global` 前缀 / 首尾空白 | P1–P4 |
| 子句来源 | 前序提交语法树（真实位置）/ 前序提交 `Options.GlobalImports`（合成树）/ 当前 `ScriptOptions.Imports`（无位置）/ `#Load` 树 | S3、S6、A4 |
| 提交链 | 单提交 / 两提交 / 三提交及以上（深度 ≥2 静默） | S5、S1、R2 |
| 大小写不敏感 | `OrdinalIgnoreCase` 去重（`VisualBasicScriptCompiler.vb:115` 语义保留） | P6、S7 |
| `Option Strict` | 无关（本路径不涉及绑定语义）；不新增用例 | — |
| 与普通 `Imports` 的边界 | 当前提交自己写错走文件级导入（本通道不接管） | S4 |
| 既有宿主行为 | 无坏子句时 `.vbx`/REPL 零行为变化 | S1/S2/A5/R6 |

## 7. 全量回归口径与判定标准

**plan 阶段标准**（本文件）：

- 分层矩阵完整（L1–L4 全适用）、用例编号可数（P1–P7 / S1–S7 / A1–A5 / R1–R6）、每例有可判断言点。
- 无副作用纪律明确：全部内存 I/O；无网络/文件写入/进程启动/注册表。
- 全量回归口径明确（下述两档）。

**实现阶段标准**：

1. `Scripting\VisualBasicTest` **直跑程序集全量 0 失败**：
   `dotnet build Scripting\VisualBasicTest\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.vbproj` →
   `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`
   （**禁用 `dotnet test`**：EXIT 0 但静默不跑；类级过滤用 `-class <FQN>`、单测过滤用 `-method <FQN>`，FQN 须完整到 `命名空间.类.方法`——缺段或拼错同样静默 0 跑，须核对 `discovery-complete` 的 `TestCasesToRun > 0`）。基线为当前全量通过数（vbi-nuget 收口时为 279 通过 / 0 失败，`../../tasks/vbi-nuget-reference/README.md`）；本任务新增用例后总数应等于「基线 + 新增数」且 Failed = 0。
2. **七门 gate** 与基线一致：`powershell -File scripts\verify-vb-compiler-tests.ps1`（Phase2 143/143、Syntax 4070、Symbol 3400、Semantic 5784、IOperation 1574、Emit 4330、CommandLine 475；见 `scripts\verify-vb-compiler-tests.ps1:7-15`）。本任务无编译器改动，**期望逐门数字不变**（不是「不跑」）。
3. **零越权核对**：`git diff --stat` 仅 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` + `Scripting\VisualBasicTest\ImportsAccumulationDiagnosticsTests.vb`；`PublicAPI.*.txt` 零增量；`Scripting\Core\`、`Compilers\`、`upstream-merge.md` 零 diff。
4. **无副作用抽查**（Grep）：新测试文件不含 `File.Write`/`Directory.Create`/`Process.Start`/`HttpClient`/`Registry`。
5. **规范闭环**：spec `:213` 的两条要求各有对应用例（R-诊断不抛 → A1/A2/R1；R-位置 → A3/A4）。

## 8. 既有测试影响核查

| 受影响面 | 影响 | 处理 |
|---|---|---|
| `InteractiveSessionTests.Imports_CrossSubmission`（`:24-33`） | 累积路径重构 | **零改动必须通过**（S1 等价） |
| `InteractiveSessionTests.Imports_DoNotReplaceInheritedOptionsImports`（`:35-47`） | options 导入与累积集合并 | **零改动必须通过**（S2 等价） |
| `ScriptTests` 既有 `#Load`/诊断位置用例（`:525-623`） | 不涉及累积路径 | 全量回归兜底 |
| `CommandLineRunnerTests` 既有 REPL 用例 | 宿主循环不变 | 全量回归兜底（R6） |
| 编译器七门 | 本任务零编译器改动 | 逐门数字不变（§7.2） |
| `upstream-merge.md` | 无共享树改动 | 无需新增类别（§7.3 核对） |
