# Visual Basic Language Design Meeting
August 22, 2026

议题是 `proposal-script-optimization-level`——脚本编译优化级别。触发点是产品侧真实用例：`.vbx` 脚本执行里有高 CPU 压力代码，当前只能以 Debug 优化跑。调查发现现状比预期更「哑」：`/optimize` 开关在脚本命令行解析器里**被完整解析**（`VisualBasicCommandLineParser.vb:806-821` → `:1496`），但脚本编译路径**根本不读**它——`CommandLineRunner.GetScriptOptions` 硬编码 `OptimizationLevel.Debug`（`CommandLineRunner.cs:177`）。开关存在、解析、却无效。本会议对照 C# 生态（`dotnet run` File-based apps vs `csi`）评估修复方向。

## Agenda

* [Proposal: 脚本编译优化级别：让 `/optimize` 对脚本生效](#proposal-脚本编译优化级别让-optimize-对脚本生效)

## Proposal: 脚本编译优化级别：让 `/optimize` 对脚本生效

_Related: [`../proposals/proposal-script-optimization-level.md`](../proposals/proposal-script-optimization-level.md)；关联 `../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（`vbx` tool 编译路径，`/optimize` 天然支持）_

### 场景与缺口

高 CPU 压力代码（数值循环、状态机密集路径）在 Debug IL 下吃亏，且 **JIT tiered compilation 补不了**——Debug 编译的 nop 与未优化局部布局已经固定进程序集。源码证实 Release 优化全在 codegen 层：

- `ILBuilder.cs:849,874`（label/局部变量优化）、`SynthesizedLocalKind.cs:268-270`（slot 复用）、`CodeGenerator.vb:89`（`debugFriendly:=_ilEmitStyle <> ILEmitStyle.Release`）——全部 `OptimizationLevel.Release` 门控。

现状是脚本永远 Debug。对照 C# 生态：

| 形态 | 编译模型 | 优化级别控制 |
|------|---------|-------------|
| `dotnet run app.cs`（File-based apps） | 完整编译一次再运行 | `-c Release` / `-O`（MSBuild 配置概念） |
| `csi` | 逐 submission 编译 | 硬编码 Debug（与 vbi 同缺陷，共享 `CommandLineRunner.cs:177`） |
| `vbi` / `.vbx` | 逐 submission 编译 | 硬编码 Debug（本提案修复） |

### 证据核实：断点只有一行

逐行翻源码后，结论收敛得很干净：

1. **开关已被解析**：`/optimize` 在 `VisualBasicCommandLineParser.vb:806-821` 解析（`optimize` 布尔，默认 False），`:1496` 构造 `optimizationLevel:=If(optimize, OptimizationLevel.Release, OptimizationLevel.Debug)` 进 `VisualBasicCommandLineArguments.CompilationOptions`（`VisualBasicCommandLineArguments.vb:29`）。
2. **脚本路径不读它**：`CommandLineRunner.GetScriptOptions`（`:155-182`）构造 `ScriptOptions` 时第 177 行写死 `OptimizationLevel.Debug`，不读 `arguments.CompilationOptions`。
3. **编译消费端已就位**：`VisualBasicScriptCompiler.vb:209` 已把 `script.Options.OptimizationLevel` 透传进 `VisualBasicCompilationOptions`（上游 C# 同，`CSharpScriptCompiler.cs:61`）。

**断点 = `CommandLineRunner.cs:177` 一行**。且 `CompilationOptions.OptimizationLevel` 是 public getter（`CompilationOptions.cs:138`），`VisualBasicInteractiveCompiler`（`Vbi.vb:14-19`，构造传 `VisualBasicCommandLineParser.Script`）的 `Arguments` 可达——透传技术上零障碍。

### 传递途径盘点（含环境变量证据）

除命令行参数外，核查了其它途径的现状证据：

| 途径 | 现状证据 | 结论 |
|------|---------|------|
| 命令行参数 `/optimize+` | 已解析未生效（上节） | 首版核心 |
| rsp（`@vbi.rsp`） | 参数展开进 args 同走 `/optimize` 解析（`VisualBasicScript.vb:158`） | 自动继承命令行，无独立改动 |
| 环境变量 | `Scripting\` 零读取；`Compilers\Core\Portable\` 仅 4 处全是 native/测试工具（`SymUnmanagedFactory.cs:114`、`ClrStrongName.cs:39-41`、`Debug.cs:60`、`CompilerOptionParseUtilities.cs:42`）——**编译选项零 env 先例** | 不并入首版（Unresolved #4 备选） |
| 脚本头指令（仿 `' Attribute TargetFramework`） | `.vbx` 头部注释宿主识别已有先例 | 备选（Unresolved #5） |
| ScriptOptions API | `ScriptOptions.cs:368` `WithOptimizationLevel` 已可用 | 程序化宿主途径，不变 |

> **运行期正交**：`DOTNET_TieredCompilation=1`（默认）让 JIT 优化热点，是 CLR 运行期维度；本提案只管编译期 `OptimizationLevel`，两者独立。

### 候选方案

**PROPOSAL A — 透传 `/optimize`（提案采用）。** `CommandLineRunner.cs:177` 改读 `arguments.CompilationOptions.OptimizationLevel`。`/optimize+` 对脚本文件执行与 REPL 启动生效；默认仍 Debug；csi 与 vbi 共用同一文件、两端受益。改动一处，管道全现成。rsp（`@vbi.rsp`）写 `/optimize+` 即全局默认 Release，自动继承 A。

**PROPOSAL B — 环境变量兜底。** 在 `GetScriptOptions` 读 `VBI_OPTIMIZE`，优先级 命令行 > env > 默认 Debug。会话级「全局默认」更直接，但编译选项 env 零先例、与 `DOTNET_*` 运行期变量易混淆——不并入首版（Unresolved #4）。

**PROPOSAL C — 对标 `dotnet run` 的编译运行模式。** `vbi /out:app.exe script.vbx` 把脚本完整编译成独立 Release 程序集再运行。形态不同（完整编译 vs 逐 submission），且与 `vbx` tool 编译路径重叠——二期候选，不并线。

**PROPOSAL D — 维持现状。** 高 CPU 脚本永远 Debug，缺口不解决。

### 权衡：Q&A

- **为什么不用 `-c Release`？** 脚本宿主不是 MSBuild 项目，无 `Configuration` 概念。`dotnet run app.cs` 的 `-c Release` 属于「完整编译一次」形态；脚本是逐 submission 编译，语义对不上。编译器开关惯例（`/optimize+`，csc/vbc 既有语义）更贴合，且管道已经解析好只差透传。
- **REPL 中途能切吗？** 不能，也不该。submission 已编译代码无法重编，切换只影响后续提交——语义不干净。优化级别在启动时固定（`UpdateOptions` 只更新 resolver、保留 OptimizationLevel，`CommandLineRunner.cs:322-342` 已保证全程一致）。
- **Release 的调试损失？** 用户显式 `/optimize+` 即知情承担；默认 Debug 交互体验零变化。与 `?` 可选（optional-question-prefix）同理——显式动作才改行为，不引入静默漂移。
- **与 `vbx` tool 的边界？** `vbx` tool 编译路径走 `Vbc.Run` 普通编译，`/optimize+` 天然支持，不依赖本提案。本提案只补脚本执行路径，两者不重叠、不冲突。
- **`#If DEBUG` 怎么办？** 普通编译器靠 MSBuild 配置清 DEBUG 符号；脚本无配置层，`/optimize+` 下 `DEBUG` 符号是否清除是 Unresolved #3，需实现时定。

### RESOLUTION:

1. **采纳 A 方案方向（Active）**：`CommandLineRunner.cs:177` 透传 `arguments.CompilationOptions.OptimizationLevel`，`/optimize+` 对脚本生效，默认仍 Debug。
2. **REPL 中途不切换**：优化级别启动时固定，不引入运行时切换指令（submission 已编译代码无法重编）。
3. **与调试符号正交**：`/debug` 透传不并入本提案（Unresolved #1），`emitDebugInformation` 维持 `!InteractiveMode` 现状（`CommandLineRunner.cs:131`）。
4. **环境变量不采用（用户定案 2026-08-22）**：编译选项 env 零先例，且与运行期 `DOTNET_*` JIT 变量易混淆；「全局默认 Release」走 rsp（`@vbi.rsp` 写 `/optimize+`）即达，无需新机制。
5. **脚本头指令不采用（用户定案 2026-08-22）**：自定义机制、脱离 Roslyn 惯例，文件粒度需求由 rsp/命令行覆盖。
6. **传递途径定案：命令行参数 + rsp（用户定案 2026-08-22）**：`/optimize` 由命令行或 `@vbi.rsp` 展开进 args，经 `GetScriptOptions` 透传到编译器；不再评估环境变量/脚本头指令。
7. **默认 rsp 不定义 DEBUG（用户定案 2026-08-22）**：默认 `vbi.rsp` 保持现状（无 `/optimize`、无 `/define`）；「Debug 配置」由用户自行 `/define:DEBUG` 组合，Release 用 `/optimize+`。曾评估「默认 rsp 定义 DEBUG + release rsp 覆盖」——`/define` 只可覆盖不可移除（`SetItem`，`VisualBasicCommandLineParser.vb:2108/2115`），release rsp 无法靠「不写 DEBUG」取消，需 `/define:DEBUG=False` 或 `/noconfig` 替换，否决。
8. **`/debug` 硬编码不透传（用户定案 2026-08-22）**：vbi 遵循 csi 策略——`emitDebugInformation = !InteractiveMode`（`CommandLineRunner.cs:131`）保持现状；只透传 `/optimize`。
9. **help 简略提及 vbc 同款参数（用户定案 2026-08-22）**：`/help` 不逐条展开，简略注明支持 vbc 同款编译参数（`/optimize`、`/define` 等）。
10. **不引入 `-c Release`**：脚本宿主不用 MSBuild 配置概念，用编译器开关惯例 `/optimize+`。
11. **编译运行模式推迟**：`/out:` 形态与 `vbx` tool 编译路径统一规划，不并线本提案。

### 勘误（2026-08-24）

实现阶段证据核实推翻了「断点只有一行」的核心前提：`/optimize`、`/debug` 解析位于 `VisualBasicCommandLineParser.vb` **非脚本分支**（`Else` 分支 `Select Case`：`/optimize` :824-840、`/debug` :789-822）；脚本模式解析器（`IsScriptCommandLineParser`，`VisualBasicCommandLineParser.vb:33`）的脚本专属分支（:475-524）**原不解析**它们——脚本模式传 `/optimize+` 原落 `WRN_BadSwitch`（BC2007 警告），optimize 保持 False（`:97`）。上文「开关已被解析只差透传」仅对普通编译器（vbc）成立。

**方案 A 已定案（用户 2026-08-24）**：
- **C1b**：扩展 VB 脚本解析器——脚本专属分支新增 `Case "optimize", "optimize+"`（optimize=True）与 `Case "optimize-"`（optimize=False），现 :525-541；复用 :97 `optimize` 布尔、:1514 `optimizationLevel:=If(optimize, ...)` 构造。
- **C1**：`CommandLineRunner.cs:177` 透传 `optimizationLevel: arguments.CompilationOptions.OptimizationLevel`。
- `/debug` 保持不透传（RESOLUTION #8）：脚本模式仍被拒为 BC2007 警告，但 REPL 继续执行。
- **csi 不自动受益**：C# 脚本解析器（`CSharpCommandLineParser.cs:308-357` 脚本分支）同构不解析 `/optimize`（C# 非脚本分支 :859-869），共享 `CommandLineRunner.cs` 只让 vbi 受益；方案 A 不扩展 C# 侧。

测试已实现：A1/A2 + H1-H9 共 11 用例全绿，`Scripting\VisualBasicTest` 194 全量 0 失败。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——改动一行、管道全现成、默认行为零变化、直接服务高 CPU 脚本用例；传递途径已定案（命令行参数 + rsp），`#If DEBUG` 语义已定案（VB 编译器默认不定义 DEBUG，`/optimize+` 即 Release 语义，2026-08-22）。进入实现规划。
