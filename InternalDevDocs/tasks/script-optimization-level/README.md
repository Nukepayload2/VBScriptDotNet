# 任务：脚本编译优化级别（script-optimization-level）设计任务

本文件夹是 `proposal-script-optimization-level` 的设计任务存储（Vortex 代办列表 + 设计产物）。

- **依据链**：`../..\proposals\proposal-script-optimization-level.md`（提案，Active）→ `../..\meetings\meeting-script-optimization-level.md`（LDM 会议，RESOLUTION #1-#11，**Active**）→ `../..\compilers-index.md`（编译器索引）。
- **交付物**：概要设计、详细设计、测试计划、spec（`../..\spec\`）。测试要求无副作用。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | pending（`design-overview.md`） |
| F2 | 详细设计 | 见下「F2 验收条件」 | pending（`design-detailed.md`） |
| F3 | 测试计划 | 见下「F3 验收条件」 | pending（`test-plan.md`） |
| F4 | 设计验证 + 一致性 | 三份交付物交叉一致、源码锚点真实、无副作用纪律 | pending |
| F5 | 宿主实现（`CommandLineRunner.cs:177` 透传） | 见「实现阶段（F5+）」 | pending（**plan 阶段不执行**） |
| F6 | 测试实现 + 修复轮 | 见「实现阶段（F5+）」 | pending（**plan 阶段不执行**） |
| F7 | 集成验证 + spec | 全量构建、测试全绿、`../..\spec\spec-script-optimization-level.md` | pending（**plan 阶段不执行**） |

> 本任务当前为 **plan 阶段**（F1–F4）。实现（F5–F7）按 Vortex 循环另起，实施者/验证者 background agent 串行交替。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-22/23）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。

### 现状机制（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）

- `:49` 注释 "csi.exe and vbi.exe entry point"——csi 与 vbi 共用同一 `CommandLineRunner`。
- `GetScriptOptions`（`:155-182`）构造 `ScriptOptions`，**:177** 写死 `optimizationLevel: OptimizationLevel.Debug`；**:178-180** 写死 `allowUnsafe: true` / `checkOverflow: false` / `warningLevel: 4`；**:181** `parseOptions: arguments.ParseOptions` 透传。
- `:131` `emitDebugInformation = !_compiler.Arguments.InteractiveMode`——`/debug` 不透传（**定案保持**），REPL 不发 PDB、脚本文件必发（经 `ScriptBuilder.cs:168-169` `GetEmitOptions` + `:51-53`，CoreCLR→PortablePdb，`PdbHelpers.cs:14-24`）。
- `RunScriptAsync`（`:201-222`）脚本文件执行；`RunInteractiveLoopAsync`（`:224`）REPL；`UpdateOptions`（`:322-342`）只更新 resolver、**保留 OptimizationLevel**（REPL 启动时定，全程一致）。

### 命令行解析（`Compilers\VisualBasic\Portable\CommandLine\`）

- `/optimize` 解析：`VisualBasicCommandLineParser.vb:806-821`（布尔 `optimize`，默认 False）；`:1496` `optimizationLevel:=If(optimize, OptimizationLevel.Release, OptimizationLevel.Debug)` 进 `VisualBasicCommandLineArguments.CompilationOptions`（类型见 `VisualBasicCommandLineArguments.vb:29`）。
- `CompilationOptions.OptimizationLevel` public getter：`Compilers\Core\Portable\Compilation\CompilationOptions.cs:138`——`arguments.CompilationOptions.OptimizationLevel` 可读。
- 脚本宿主接线：`Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:14-19`（`VisualBasicInteractiveCompiler` 用 `VisualBasicCommandLineParser.Script`）→ `_compiler.Arguments`。
- 默认 rsp：`vbi.vbproj:21-26`（`vbi.coreclr.rsp`/`vbi.desktop.rsp` 经 `<Link>vbi.rsp</Link>`），内容无 `/optimize`、无 `/define`（`vbi.coreclr.rsp`、`vbi.desktop.rsp` 已核）。rsp 经 `CommonCompiler.cs:130-132` 前置拼入 args。

### 编译消费端（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`）

- `:209` `optimizationLevel:=script.Options.OptimizationLevel` 透传进 `VisualBasicCompilationOptions`（**已就位**，断点只在宿主 `:177`）。
- 上游 C# 同：`{{Roslyn}}src\Scripting\CSharp\CSharpScriptCompiler.cs:61`。

### DEBUG 符号（不引入 configuration）

- VB 默认不定义 DEBUG：`PredefinedPreprocessorSymbols.vb:48-62` 只加 `VBC_VER`/`TARGET`；`defines` 仅来自显式 `/define`（`VisualBasicCommandLineParser.vb:237-244`）。
- `/define` 累积语义：`VisualBasicCommandLineParser.vb:2108/2115` `SetItem`（覆盖不移除）——「默认 rsp 定义 DEBUG + release rsp 覆盖」不可行（定案否决）。
- 脚本默认 rsp 不加 DEBUG（**定案保持现状**）。

## 关键设计决策（源自 meeting RESOLUTION #1-#11，设计文档必须吸收）

1. **传递途径 = 命令行参数 + rsp**（RESOLUTION #6）：`/optimize` 由命令行或 `@vbi.rsp` 展开进 args，经 `GetScriptOptions` 透传；环境变量、脚本头指令不采用（#4/#5）。
2. **核心改动一处**：`CommandLineRunner.cs:177` 改 `optimizationLevel: arguments.CompilationOptions.OptimizationLevel`，默认仍 Debug。
3. **`/debug` 硬编码不透传**（#8）：`emitDebugInformation = !InteractiveMode`（`:131`）保持，遵循 csi 策略；本特性只透传 `/optimize`。
4. **默认 rsp 不加 DEBUG**（#7）：保持现状；「Debug 配置」由用户自行 `/define:DEBUG`。
5. **Release 语义**（proposal Unresolved #3 已定案；meeting RESOLUTION #3 是 `/debug` 正交，勿混淆）：`/optimize+`（无 `/define:DEBUG`）= 优化 + `#If DEBUG` False，与 MSBuild Release 配置一致；VB 默认无 DEBUG 符号，无需「清除 DEBUG」。
6. **REPL 中途不可切**（#2）：优化级别启动时固定（`UpdateOptions` 保留），不引入运行时切换指令。
7. **help 简略提及 vbc 同款参数**（#9）：`/help` 注明支持 vbc 同款编译参数（`/optimize`、`/define` 等），不逐条展开（当前 help 由 `Vbi.vb:52-54` `PrintHelp` → `VBScriptingResources.InteractiveHelp`）。
8. **csi 同步受益**：同一 `CommandLineRunner.cs`，对 csi 是行为变化（今天也硬编码 Debug）。

## F1 验收条件（概要设计 pass 标准）

- 覆盖三层（解析 / 宿主 / 编译消费端）与唯一断点（`CommandLineRunner.cs:177`）。
- 含行为对照表（默认 / `/optimize+` / `/optimize-` / rsp `/optimize+`，交互 vs 脚本）。
- 说明 `/debug` 不透传、`emitDebugInformation` 不变（RESOLUTION #8）。
- 说明 Release 语义（`/optimize+` 无 `/define:DEBUG` = `#If DEBUG` False）与默认 rsp 不加 DEBUG。
- 说明 REPL 启动时定、中途不可切；csi 同步受益（行为变化）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号 + 改动形状**，可被实施者直接照做（预期仅 `CommandLineRunner.cs:177` 一行）。
- 确认 `arguments.CompilationOptions.OptimizationLevel` 类型可达（`CompilationOptions.cs:138` public getter）与默认 Debug（`optimize` 布尔默认 False，`VisualBasicCommandLineParser.vb:97/806-821`）。
- 无副作用测试矩阵（REPL 与脚本模式），含解析断言（`runner.Compiler.Arguments.CompilationOptions.OptimizationLevel`）与冒烟。
- 边界与迁移影响（REPL 中途不可切、csi 行为变化、/debug 不变、rsp 继承）。

## F3 验收条件（测试计划 pass 标准）

- 四层矩阵：L1/L2 不适用（无语法/绑定变化，标注 N/A）；L3 API（`ScriptOptions.WithOptimizationLevel` → `VisualBasicCompilationOptions` 消费端）；L4 宿主（`CommandLineRunnerTests`：`/optimize+` 解析断言、默认 Debug、`/optimize-`、rsp、交互/脚本冒烟、`/debug` 不变）。
- 无副作用纪律：内存 `TestConsoleIO`/`StringReader`；rsp 用例需临时 rsp 文件（宿主读取必需，复用现有 `CreateIsolatedTempDirectory` 基建）；不启动进程、不网络。
- 端到端透传观察点（`Script.Options` public，`Script.cs:69`，但宿主 script 局部不可达）——标注两种方案（internal 测试钩子 vs 解析断言+API 组合证明），实现时定。

## 实现阶段（F5+，plan 阶段不执行）

- **F5 宿主实现**：`CommandLineRunner.cs:177` 改 `optimizationLevel: arguments.CompilationOptions.OptimizationLevel`（一处）。验收：`/optimize+` 脚本/REPL Release 编译、默认 Debug、`/debug` 不变、rsp 继承。
- **F6 测试实现**：按 `test-plan.md` 四层用例，无副作用。验收：全绿 + 回归面（`CommandLineRunnerTests` 既有用例不破）。
- **F7 集成 + spec**：全量构建、测试全绿；`../..\spec\spec-script-optimization-level.md` 与 proposal/meeting RESOLUTION 一致；提案头部 `Specification` 进度更新。
