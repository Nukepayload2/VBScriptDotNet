# 测试计划：脚本编译优化级别（/optimize 透传）

> 状态：测试计划（F3）。**已实现：L3 A1/A2 + L4 H1-H9 共 11 用例全绿、`Scripting\VisualBasicTest` 194 全量 0 失败**。依据链：`../../proposals/proposal-script-optimization-level.md` → `../../meetings/meeting-script-optimization-level.md`（Active，RESOLUTION #1-#9）→ 详细设计 `design-detailed.md`。
> 测试宿：`Scripting\VisualBasicTest\`（MTP 项目，`dotnet test` 静默不跑，须直接跑程序集 `-automated`）。
> 无副作用纪律：内存 `TestConsoleIO`/`StringReader`；不启动进程、不网络、不注册表；rsp 用例需临时 rsp 文件（宿主读取必需，复用 `CreateIsolatedTempDirectory`，`CommandLineRunnerTests.vb:42-46`）。

## 1. 测试分层矩阵

| 层 | 适用性 | 说明 |
|----|--------|------|
| L1 解析 | **N/A** | 本特性无语法/绑定改动；解析器仅脚本专属分支加 `/optimize` 开关（C1b，由 L4 解析断言覆盖） |
| L2 语义 | **N/A** | 本特性无绑定/语义改动 |
| L3 API | ✅ | 证明编译消费端已就位（`VisualBasicScriptCompiler.vb:209`） |
| L4 宿主/REPL | ✅ | `CommandLineRunnerTests`：解析断言 + 冒烟 + `/debug` 不变 |

## 2. L3 API（消费端证明，`Scripting\VisualBasicTest\`）

| # | 用例 | 断言 | 无副作用 |
|---|------|------|---------|
| A1 | `VisualBasicScript.Create("x = 1", ScriptOptions.Default)`（命名空间 `Microsoft.CodeAnalysis.VisualBasic.Scripting`，`VisualBasicScript.vb:18,45`）→ `script.GetCompilation()` | `DirectCast(...).Options.OptimizationLevel = Debug`（默认） | 纯内存 |
| A2 | `VisualBasicScript.Create("x = 1", Default.WithOptimizationLevel(Release))` | `Options.OptimizationLevel = Release`（`VisualBasicScriptCompiler.vb:209` 透传） | 纯内存 |

> 用途：证明编译消费端已就位，把端到端证明收敛到宿主透传（C1）+ 解析器脚本分支扩展（C1b，L4）。

## 3. L4 宿主（`CommandLineRunnerTests.vb`）

### 3.1 解析断言（透传输入侧）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| H1 | 默认 Debug | `CreateRunner(args:={"/R:System"})` | `runner.Compiler.Arguments.CompilationOptions.OptimizationLevel = Debug` | 纯内存 |
| H2 | `/optimize+` | `CreateRunner(args:={"/optimize+", "/R:System"})` | 同上 `= Release` | 纯内存 |
| H3 | `/optimize-` | `CreateRunner(args:={"/optimize-", "/R:System"})` | 同上 `= Debug` | 纯内存 |
| H4 | rsp `/optimize+` | 临时 rsp 文件写 `/optimize+`，`CreateRunner(args:={"/R:System"}, responseFile:=<path>)` | 同上 `= Release` | 临时 rsp 文件（宿主读取必需） |
| H5 | rsp 无 `/optimize` | 临时 rsp 文件无 `/optimize` | 同上 `= Debug` | 临时 rsp 文件 |

> `runner.Compiler` 为 internal（`CommandLineRunner.cs:46`），测试同程序集可访问。此层证明 `/optimize` 经命令行/rsp → `arguments.CompilationOptions.OptimizationLevel` 的链路（透传代码的输入）。
>
> **H1/H3/H5 默认路径不受解析器改动影响；H2、H4 依赖脚本解析器新增 `/optimize` 开关（C1b/F5b，方案 A），修复后转绿**。

### 3.2 冒烟（透传后执行正常，REPL 与脚本）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| H6 | `/optimize+` REPL 冒烟 | `CreateRunner(args:={"/optimize+", "/R:System"}, input:="? 1 + 2")` → `RunInteractive()` | 输出含 `3`，无错误（`TestConsoleIO` 内存） | 纯内存 |
| H7 | `/optimize+` 脚本文件冒烟 | 临时 `main.vbx` + `CreateRunner(args:={"/optimize+", "main.vbx"}, workingDirectory:=directory)`（仿 `TestLoadDirectiveInScriptFile` `:248`，`TestQuestionDirectiveInScriptFileDoesNotSetExitCode` `:264`） | 退出码/输出正确，无错误 | 临时 `.vbx` 文件（既有脚本测试基建） |
| H8 | 默认（无开关）冒烟 | `CreateRunner(input:="? 1 + 2")` | 输出含 `3`（既有行为不破） | 纯内存 |

> **H6、H7 依赖脚本解析器新增 `/optimize` 开关（C1b/F5b，方案 A），修复后转绿**。

### 3.3 `/debug` 不变（RESOLUTION #8）

| # | 用例 | 断言 | 无副作用 |
|---|------|------|---------|
| H9 | 带 `/debug:portable` 跑（方案 A） | `Arguments.Errors` 含 `Id="BC2007"` + `Console.Error` 含 BC2007 + `Out` 含 `3`（不做全文匹配） | 纯内存（行为断言） |

> `/debug` 在脚本模式被脚本解析器拒为**警告**（BC2007，`WRN_BadSwitch`）但 REPL 继续执行，故 H9 断言改为：`Arguments.Errors` 含 BC2007、`Console.Error` 含 BC2007、`Out` 含 `3`——**不做全文匹配**（`TestConsoleIO` 的 `TeeWriter` 会把 error 夹带进 Out）。`/debug` 不透传的强断言（`emitDebugInformation` 具体值）仍受宿主黑盒限制，见 §4。

## 4. 端到端透传观察点（待实现时定）

宿主创建的 `Script` 实例在 `RunScriptAsync`/`RunInteractiveLoopAsync` 内局部，测试不可达；`Script.Options` public（`Script.cs:69`）但无钩子。两种方案：

- **方案 A（internal 测试钩子）**：`CommandLineRunner` 加 internal 属性暴露最近一次构造的 `ScriptOptions`（或 `Script`）。可直接断言 `script.Options.OptimizationLevel` 端到端。代价：超出一行改动的改动面。
- **方案 B（组合证明，推荐首版）**：L3 A1/A2（消费端）+ L4 H1-H5（解析输入）+ C1 一行透传的 code review 组合证明；冒烟 H6-H8 兜底。零额外改动面。

> 端到端观察点采用**方案 B（组合证明，零额外改动面）**：L3 A1/A2（消费端）+ L4 H1-H5（解析断言）+ C1 透传/C1b 解析器扩展的 code review 组合证明；冒烟 H6-H8 兜底。未引入 internal 钩子。

## 5. 回归面

| 面 | 风险 | 处置 |
|----|------|------|
| `CommandLineRunnerTests` 既有用例（help/logo/imports/load 等） | C1 不改 `:131`/`:178-181`，默认行为零变化 | 全量回归 |
| help 断言 | C2 改 `InteractiveHelp` 资源——但 `s_logoAndHelpPrompt`（`CommandLineRunnerTests.vb:28-34`）不含 `InteractiveHelp`，当前无 `/help` 全文本断言用例 | **无需改既有断言**（验证者核验）；仅 C2 资源改动本身 |
| `Scripting` 其它测试（`ScriptOptionsTests`、`CompilationAPITests` 脚本相关） | 无直接关联 | 全量回归 |

## 6. 验收 gate

- **结果（2026-08-24）**：L3 A1/A2 + L4 H1-H9 共 **11 用例全绿**；`Scripting\VisualBasicTest` **194 全量 0 失败**。
- L3 A1/A2 全绿；L4 H1-H8 全绿；H9（`/debug` 拒为 BC2007 警告、REPL 继续）通过。
- 回归面全量通过（`Scripting\VisualBasicTest` 直接跑程序集 `-automated`）。
- 无副作用纪律：临时文件仅限宿主必需——H4/H5 临时 rsp、H7 临时 `.vbx`（复用 `CreateIsolatedTempDirectory` 既有基建）；其余零文件写入、零进程、零网络。
