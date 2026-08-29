# 任务：vbi 脚本模式诊断检查 `/check`（vbi-script-diag-mode）设计任务

本文件夹是 `proposal-vbi-script-diag-mode` 的设计任务存储（Vortex 代办列表 + 设计产物）。

- **依据链**：`../..\proposals\proposal-vbi-script-diag-mode.md`（提案，冻结输入，原 `/diag`）→ `../..\meetings\meeting-vbi-script-diag-mode.md`（LDM 会议，RESOLUTION #1-#12，**Active**，**命名裁决 `/diag`→`/check`，本任务以 RESOLUTION 为准**）→ `../..\compilers-index.md`（编译器索引）。
- **交付物**：概要设计、详细设计、测试计划、spec（`../..\spec\`）。测试要求无副作用。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

> **命名说明**：提案正文沿用提案期名字 `/diag`（冻结输入不改）；会议 RESOLUTION #1 采纳 `/check`（AI 先验标准）。本任务所有设计、实现、测试一律用 **`/check`**。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | pending（`design-overview.md`） |
| F2 | 详细设计 | 见下「F2 验收条件」 | pending（`design-detailed.md`） |
| F3 | 测试计划 | 见下「F3 验收条件」 | pending（`test-plan.md`） |
| F4 | 设计验证 + 一致性 | 三份交付物交叉一致、源码锚点真实、无副作用纪律 | pending |
| F5 | 实现（解析器脚本分支 `/check` + 基类 `Check` + 宿主 Check 分支 + 互斥规则 + help） | 见「实现阶段（F5+）」 | pending |
| F6 | 测试实现 + 修复轮 | 见「实现阶段（F5+）」 | pending |
| F7 | 集成验证 + spec | 全量构建、测试全绿、`../..\spec\spec-vbi-script-diag-mode.md` | pending（**plan 阶段不执行**） |

> 本任务当前为 **plan 阶段**（F1–F4）；F5/F6/F7 为实现阶段，plan 阶段不执行。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-29，含两位老登独立复核）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。

### 模式分派（`Interactive\vbi\`）

- `Vbi.vb:58-60`：`OnStartupAsync` 中 `IsCompileInvocation(args)` 命中 → 编译模式（`VbiCompileMode.Run`）；否则 `VisualBasicScript.RunInteractiveAsync`（脚本/交互）。
- `Vbi.Compile.vb:32-59`（`IsCompileInvocation`）：只认 `.vb` 源 / `/out:` / `/target:` → 编译模式；`Case "i","i+"` 直接 `Return False` 强制脚本。`/check` 不触发编译模式、不强制交互，自然落脚本模式。**`vbi /check t.vb` 会因 `.vb` 进编译模式、`/check` 落 BC2007 警告（文档写明，RESOLUTION #7）。**

### 命令行解析（`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`）

- `:33` `Script` parser（`isScriptCommandLineParser:=True`），vbi 脚本宿主用 `VisualBasicCommandLineParser.Script`（`Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:18`）。
- 脚本专属分支 `If IsScriptCommandLineParser Then Select Case name`（`:474-542`）：现有 `-`/`i`/`i+`/`i-`/`nostdlib`/`vbruntime-`/`loadpath`/`optimize` 系。新增 `Case "check"` 落这里。
- `:97` 附近布尔区（`optimize` 等）：新增 `check` 布尔（默认 False）。
- `:1542` `interactiveMode = interactiveMode Or (IsScriptCommandLineParser AndAlso sourceFiles.Count = 0)`——无源文件强制交互。**`check` 置位须抑制此自动交互并在无文件时产出错误（RESOLUTION #3）。**
- `:1546-1551` 构造 `VisualBasicCommandLineArguments`：`.InteractiveMode = interactiveMode`（`:1549`）；新增 `.Check = check`。
- `:1341` 兜底 `WRN_BadSwitch`（BC2007）——未识别开关报警告、不阻断。
- `/removeintchecks*` 在 `:369,:378`（共享分支，脚本解析器也识别）；`checksumalgorithm` 在 `:353`；`analyzer` 在 `:233`。均与 `check` 精确匹配零冲突（RESOLUTION #8）。

### 基类参数（`Compilers\Core\Portable\CommandLine\CommandLineArguments.cs`）

- `:31` `InteractiveMode` 属性在**基类**——脚本解析器只置位、共享 runner 直接读，不掺语言专属 cast。**`Check` 属性仿此放基类（RESOLUTION #2）。**（行号 2026-08-29 两老登核，F2 实现前复核。）

### 宿主执行（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）

- `:49` 注释 "csi.exe and vbi.exe entry point"——csi 与 vbi 共用此 runner；本 fork 无 `Scripting\CSharp` 宿主，仅 VB 消费。
- `RunInteractiveCoreAsync`（`:80-153`）：`:136-140` 先报解析错误；`:144` `_compiler.Arguments.InteractiveMode` → `RunInteractiveLoopAsync`（`:224`）；否则 `RunScriptAsync`（`:201-222`）。
- **`RunScriptAsync`（`:201-222`）**：`:206` `Script.CreateInitialScript<int>`；`:209` `return (await script.RunAsync(...)).ReturnValue;`（**成功路径零诊断输出，警告被吞**）；`:212-216` `catch (CompilationErrorException)` → `ReportDiagnostics(e.Diagnostics)`。**`Check` 分支在此插入：`script.Compile()` → `ReportDiagnostics` → 返回。**
- `BuildAndRunAsync`（`:296-320`，交互模式）：`:298` `newScript.Compile()` → `:299` `DisplayDiagnostics` → `:300` `HasAnyErrors()` 有错不跑——**"先编译拿诊断再决定跑不跑"的既有先例**。
- `DisplayDiagnostics`（`:378-408`）：`MaxDisplayCount = 5`（`:380`）截断——**`/check` 不用它，复用 `_compiler.ReportDiagnostics` 全量显示（RESOLUTION #5）。**
- `GetScriptOptions`（`:155-182`）：构造 `ScriptOptions`；`warningLevel: 4`（`:180`）。

### 编译/诊断 API（`Scripting\Core\Script.cs`、`CommonCompiler.cs`）

- `Script.cs:231` `Compile(CancellationToken)` public；`:332-346` `CommonCompile`——成功返回 `GetDiagnostics().Where(Warning)`（**:340 仅警告**）、失败 catch `CompilationErrorException` 返回 Error+Warning（**:344**）；不抛异常。`:337-338` `GetPrecedingExecutors`（`#load` 链前置脚本也构建 executor → 内存 emit）。
- `Script.cs:361-369` `GetExecutor` → `Builder.CreateExecutor<T>`：内存发射 submission 程序集（脚本引擎标准路径，无落盘无执行）。
- `CommonCompiler.cs:515-583` `ReportDiagnostics`：打印全部非 Hidden 非抑制诊断（`:540-544` Hidden 不打印），**返回 `hasErrors`（`:574-577`）**——`/check` 退出码可直接 `return _compiler.ReportDiagnostics(...) ? Failed : Succeeded`（RESOLUTION #4）。

### 脚本 submission 语义（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`）

- `:194` `CreateScriptCompilation`（`.vbx` 作为脚本 submission）；`:204` `optionStrict:=OptionStrict.Off` 默认——**源级 `Option Strict On` 逐文件覆盖**（实证 BC30574 晚绑定 / BC30512 收窄），`/check` 严格性由文件内容决定。

### 参数语义（`Compilers\Core\Portable\CommandLine\CommandLineParser.cs`）

- `:539-545`：脚本模式下**一旦看到源文件，之后所有参数一律当脚本参数**（不解析为开关）——`/check` 必须在脚本文件**之前**。
- `:586`：`--` 之后不解析为开关。
- MSYS 裸开关：无冒号裸开关经 git-bash/MSYS 被转成 `C:/Program Files/Git/<name>` → BC2001（实证 `/nostdlib` 同样）。`/check` 是第 4 个受影响裸开关（前三个 `/i`/`/nostdlib`/`/optimize+`）——**零代码处理 + `MSYS2_ARG_CONV_EXCL='*'` + 帮助文本统一说明（RESOLUTION #9）。**

### 实证（2026-08-29，vbi Debug net10.0）

- warning-only `.vbx`（未使用变量）→ 打印 `RAN OK`、无警告、exit 0（警告被吞）。
- error `.vbx`（未声明变量）→ 报 BC30451、中止、exit 1。
- 源级 `Option Strict On` → 晚绑定 BC30574、收窄 BC30512。
- 脚本模式传 `/warnaserror:on` 等 vbc 专属开关 → BC2007 警告 + 脚本照跑 exit 0。
- 裸开关经 git-bash → BC2001；设 `MSYS2_ARG_CONV_EXCL='*'` → 正常。

## 关键设计决策（源自 meeting RESOLUTION #1-#12，设计文档必须吸收）

1. **采纳 `/check`（RESOLUTION #1）**：以"消除 AI 幻觉"为首要标准，`/check` 的 AI 先验（cargo/node/biome check = 只检查不产产物）与语义吻合；`/diag` 的 AI 先验（msbuild `/diag`/`dotnet -v:diag` = 详细日志）方向相反故否决。**帮助文本必须写**：`/check` = 只编译、不运行、不产出文件。
2. **`Check` 标志放基类 `CommandLineArguments`（仿 `InteractiveMode`，`CommandLineArguments.cs:31`）**：VB 脚本解析器置位，共享 runner 直接读，不掺语言专属 cast（#2）。
3. **互斥语义落实成规则**：解析器层面 `check` 置位抑制 `:1542` 自动交互、无文件产出错误诊断；runner 层面 `Check` 置位无视 `InteractiveMode`（`:144-152`）（#3）。
4. **退出码契约**：0 = 无编译错误（可有警告），1 = 有错误；实现用 `ReportDiagnostics` 返回值（#4）。
5. **输出措辞**：错误+全部警告（Info/Hidden 被滤），文档不写"完整诊断"（#5）。
6. **严重度配置 v1 不带**：`/check` 严格度即默认 `dotnet build`/`vbc` 门；`/warnaserror`/`/nowarn` 留未来分析器面（oxlint 类比）（#6）。
7. **`/check` 配 `.vb` 不生效**：`IsCompileInvocation` 会把 `.vb` 带进编译模式、`/check` 落 BC2007；文档写明（#7）。
8. **命名边界**：文档一行区分 `/check`（编译门）与 `/removeintchecks`/`csc /checked`（运行时溢出）；`Option Checked`（源级 Option 表达溢出）为未来语言特性、非本提案准入条件（#8）。
9. **MSYS**：帮助文本统一一条裸开关族说明（`/i`/`/nostdlib`/`/optimize+`/`/check`），要求 `MSYS2_ARG_CONV_EXCL='*'`，零代码（#9）。
10. **内存 emit 接受**：`Script.Compile()` 的 executor 构建是脚本引擎标准路径，措辞统一"无落盘无执行"；`#load` 链前置脚本同样内存 emit，不阻塞（#10）。
11. **分析器/规则诊断独立立项**：`/check` 是编译器诊断（tsc 类比）；fxcop 级规则诊断（oxlint 类比）需 `CompilationWithAnalyzers` + 脚本路径分析器管线，单独提案（#11）。
12. **Follow-up（不并线）**：普通执行路径 `vbi foo.vbx` 成功路径同样吞警告（`CommandLineRunner.cs:209`）——"普通执行也先 `Compile()` 显示警告再 `RunAsync`"单独立项（#12）。

## F1 验收条件（概要设计 pass 标准）

- 覆盖四层触点：解析器脚本分支 `/check` 开关 + `check` 布尔；基类 `CommandLineArguments.Check`；宿主 `RunScriptAsync` Check 分支 + `RunInteractiveCoreAsync` 无视 InteractiveMode；help 资源。
- 含行为对照表（默认 / `/check` 干净 / `/check` 有错误 / `/check /i` / `/check` 无文件 / `/check` 配 `.vb`，退出码与输出）。
- 说明互斥规则落实点（`:1542` + `:144-152`）与无文件错误语义。
- 说明命名边界（RESOLUTION #8）与帮助文本消歧（"只编译、不运行、不产出文件"）。
- 说明 `Check` 放基类、csi 不受影响（无 `Scripting\CSharp` 宿主）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号 + 改动形状**，可被实施者直接照做（C1 解析器 `/check` 开关 + C2 基类 `Check` + C3 互斥规则 + C4 宿主 `RunScriptAsync` 分支 + C5 宿主 `RunInteractiveCoreAsync` + C6 help + C7 测试）。
- 确认 `Check` 在基类 `CommandLineArguments`（仿 `InteractiveMode` `:31`）可达、脚本解析器置位、共享 runner 直接读。
- 无副作用测试矩阵（解析断言 + 冒烟 + 退出码 + 互斥 + `.vb` 边界），含 `Script.Compile()` 消费端证明。
- 边界与迁移影响（默认行为零变化、csi 无关、MSYS 文档、`#load` 内存 emit）。

## F3 验收条件（测试计划 pass 标准）

- 四层矩阵：L1 解析（`/check` 识别 + 互斥无文件错误）；L2 语义（N/A，无绑定改动）；L3 API（`Script.Compile()` 返回警告/错误语义）；L4 宿主（`CommandLineRunnerTests`：`Check` 解析断言、默认 false、`/check` 脚本文件冒烟 + 诊断显示 + 退出码、`/check /i` 优先、`/check` 无文件报错、`/check` 配 `.vb` BC2007、`/debug` 等 vbc 开关不变）。
- 无副作用纪律：内存 `TestConsoleIO`/`StringReader`；临时 `.vbx`/rsp 文件复用 `CreateIsolatedTempDirectory`；不启动进程、不网络。
- 验收 gate：L1/L3/L4 全绿 + `Scripting\VisualBasicTest` 全量 0 失败（MTP 项目，直接跑程序集 `-automated`）。

## 实现阶段（F5+，plan 阶段不执行）

- **F5 实现**：C1 解析器脚本分支 `Case "check"` + `check` 布尔；C2 基类 `CommandLineArguments.Check`；C3 互斥规则（`:1542` 抑制自动交互 + 无文件错误）；C4 宿主 `RunScriptAsync` Check 分支（`script.Compile()` → `ReportDiagnostics` → 返回）；C5 宿主 `RunInteractiveCoreAsync` Check 无视 InteractiveMode；C6 help 资源。验收：`/check` 脚本文件出诊断不执行、退出码 0/1、`/check /i` 优先、`/check` 无文件报错、默认行为零变化。
- **F6 测试实现**：按 `test-plan.md` 分层用例，无副作用。验收：全绿 + 回归面（`CommandLineRunnerTests` 既有用例不破）。
- **F7 集成 + spec**：全量构建、测试全绿；`../..\spec\spec-vbi-script-diag-mode.md` 与 proposal/meeting RESOLUTION 一致（命名用 `/check`）；提案头部 `Specification` 进度更新。
