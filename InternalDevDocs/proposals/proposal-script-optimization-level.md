# 脚本编译优化级别的传递机制 / How the Script Host Passes the Compilation Optimization Level

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete（C1 宿主透传 `CommandLineRunner.cs:177` `optimizationLevel: arguments.CompilationOptions.OptimizationLevel` + C1b VB 脚本解析器脚本分支新增 `/optimize`、`/optimize+`、`/optimize-` 开关 `VisualBasicCommandLineParser.vb:525-541`；F6 测试 A1/A2 + H1-H9 共 11 用例全绿、`Scripting\VisualBasicTest` 194 全量 0 失败；F7 集成验证全绿）
* [x] Specification: [Complete](../spec/spec-script-optimization-level.md)

> **勘误（2026-08-24）**：实现阶段证据核实发现本提案「传递途径现状」第 1、3 点前提不成立——`/optimize`、`/debug` 的解析位于 `VisualBasicCommandLineParser.vb` **非脚本分支**（`Else` `Select Case`，`/optimize` :824-840、`/debug` :789-822），而脚本宿主用的是 `VisualBasicCommandLineParser.Script`（`isScriptCommandLineParser:=True`，:33）的**脚本专属分支**（:475-524，原只有 `-`/`i`/`i+`/`i-`/`nostdlib`/`vbruntime-`/`loadpath`），脚本模式传 `/optimize+` 原落 `WRN_BadSwitch`（BC2007 警告）。因此「开关被解析却不生效」仅对普通编译器（vbc）成立，对脚本不成立——脚本是**解析器不识别开关**，不只是宿主不透传。
>
> **定案（方案 A，用户 2026-08-24）**：核心改动实为**两处**——C1 宿主透传（`CommandLineRunner.cs:177`）+ C1b VB 脚本解析器脚本专属分支新增 `/optimize`、`/optimize+`、`/optimize-` 开关（:525-541）。`/debug` 保持不透传（RESOLUTION #8）。**csi 不自动受益**：C# 脚本解析器（`CSharpCommandLineParser.cs:308-357`）同构不解析 `/optimize`，方案 A 不扩展 C# 侧。本提案下文正文为勘误前调查记录，以本勘误块为准。

## Summary
[summary]: #summary

本提案研究并落地**优化级别（`OptimizationLevel`）如何从外部传递到脚本编译器**（REPL 与 `.vbx` 文件执行共用同一路径）。

现状断点：脚本宿主把优化级别**硬编码为 `OptimizationLevel.Debug`**（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:177`，csi 与 vbi 共用此入口，见 `:49` 注释 "csi.exe and vbi.exe entry point"）。命令行解析器**已把 `/optimize` 解析进 `Arguments.CompilationOptions`**（`VisualBasicCommandLineParser.vb:806-821` → `:1496` `optimizationLevel:=If(optimize, Release, Debug)`），但 `GetScriptOptions` 不读它——**开关被解析却不生效**；环境变量在编译选项中**零先例**（证据见 Motivation）。

**方案（用户定案 2026-08-22）**：优化级别传递途径**定案为命令行参数 + rsp**——`GetScriptOptions` 透传 `arguments.CompilationOptions.OptimizationLevel`（`/optimize` 由命令行或 `@vbi.rsp` 展开进 args，见 Detailed design），默认仍 Debug，既有行为零变化。**环境变量与脚本头指令不采用**（rsp 已覆盖「全局默认 Release」，见途径矩阵）。

## Motivation
[motivation]: #motivation

### 高 CPU 压力脚本用例

`.vbx` 脚本执行含高 CPU 压力代码时，只能以 Debug 优化跑。Debug vs Release 的差距是**编译器级**的，JIT tiered compilation 补不了（Debug IL 的 nop、未优化局部布局已固定进程序集）：

- `Compilers\Core\Portable\CodeGen\ILBuilder.cs:849,874` — Release 才做 label/局部变量优化
- `Compilers\Core\Portable\SynthesizedLocalKind.cs:268-270` — Release 才复用局部变量 slot（迭代器/async 状态机、闭包捕获收益最大）
- `Compilers\VisualBasic\Portable\CodeGen\CodeGenerator.vb:89` — `debugFriendly:=_ilEmitStyle <> ILEmitStyle.Release`

### 传递途径现状（源码核实）

1. **命令行参数 `/optimize` 已被解析**：`/optimize`（及 `optimize+`/`optimize-`）在 `VisualBasicCommandLineParser.vb:806-821` 解析（布尔 `optimize`，默认 False），`:1496` 构造 `optimizationLevel:=If(optimize, OptimizationLevel.Release, OptimizationLevel.Debug)` 进 `VisualBasicCommandLineArguments.CompilationOptions`（`VisualBasicCommandLineArguments.vb:29`，类型 `VisualBasicCompilationOptions`）。
2. **脚本路径不读它**：`CommandLineRunner.GetScriptOptions`（`:155-182`）构造 `ScriptOptions` 时第 177 行写死 `OptimizationLevel.Debug`，不读 `arguments.CompilationOptions`。
3. **编译消费端已就位**：`VisualBasicScriptCompiler.vb:209` 已把 `script.Options.OptimizationLevel` 透传进 `VisualBasicCompilationOptions`（上游 C# 同，`{{Roslyn}}src\Scripting\CSharp\CSharpScriptCompiler.cs:61`）。**唯一断点 = 宿主第 177 行写死 Debug。**
4. **环境变量零编译选项先例**：脚本核心（`Scripting\`）与 VB 命令行解析器无 `GetEnvironmentVariable` 命中；`Compilers\Core\Portable\` 仅 4 处读取，全是 native 加载/测试工具——`SymUnmanagedFactory.cs:114`（DiaSymReader DLL 路径）、`ClrStrongName.cs:39-41`（`COMPLUS_InstallRoot`/`COMPLUS_Version`）、`InternalUtilities\Debug.cs:60`（`HELIX_DUMP_FOLDER` 崩溃 dump）、`CompilerOptionParseUtilities.cs:42`（编译选项缓存路径）。Roslyn 的**编译选项一律走命令行参数**，环境变量不是惯例。

### 与 C# 生态对照

| 形态 | 编译模型 | 优化级别控制 |
|------|---------|-------------|
| `dotnet run app.cs`（File-based apps） | 完整编译一次再运行 | `-c Release` / `-O`（MSBuild 配置概念） |
| `csi`（C# 脚本 REPL） | 逐 submission 编译 | **硬编码 Debug**（与 vbi 同缺陷，共享 `CommandLineRunner.cs:177`） |
| `vbi` / `.vbx`（本产品） | 逐 submission 编译 | **硬编码 Debug**（本提案修复） |

C# 生态里 csi 与 vbi 同病；`dotnet run` 的 `-c Release` 属于「完整编译一次」的另一种形态。~~本提案一处修改，csi 与 vbi 两端受益（同一 `CommandLineRunner.cs`）。~~ **（勘误 2026-08-24：csi 不自动受益——C# 脚本解析器同构不解析 `/optimize`，方案 A 不扩展 C# 侧，见头部勘误块。）**

## Detailed design
[design]: #detailed-design

### 途径矩阵

「优化级别 → 脚本编译器」的全部可行途径及裁决：

| 途径 | 机制 | 现状证据 | 裁决 |
|------|------|---------|------|
| **A. 命令行参数 `/optimize+`** | 非脚本分支解析进 `Arguments.CompilationOptions`（`VisualBasicCommandLineParser.vb:824-840,1514`）；脚本专属分支原不解析，已新增 `/optimize`、`/optimize+`、`/optimize-`（:525-541，方案 A）；宿主透传（`CommandLineRunner.cs:177`） | 勘误前「已解析未生效」仅对 vbc 成立；脚本原落 `WRN_BadSwitch`（BC2007） | **首版采用**（两处改动：C1 透传 + C1b 解析器扩展） |
| **B. rsp 响应文件（`@vbi.rsp`）** | rsp 参数展开进 args 同走 `/optimize` 解析（`VisualBasicScript.vb:158` 拼 `vbi.rsp`；默认 rsp `vbi.coreclr.rsp`/`vbi.desktop.rsp`） | 同 A 被忽略 | **自动继承 A**，无独立改动；透传后在 rsp 写 `/optimize+` 即全局默认 Release |
| **C. 环境变量**（如 `VBI_OPTIMIZE`） | 需在宿主新增读取 | 编译选项零 env 先例（见 Motivation） | **不采用（用户定案 2026-08-22）**；非 Roslyn 惯例、与运行期 `DOTNET_*` JIT 变量易混淆；「全局默认」由 rsp 承担 |
| **D. 脚本头指令**（仿 `' Attribute TargetFramework = "net48"`） | `.vbx` 头部注释宿主识别已有先例 | 无优化级别版本 | **不采用（用户定案 2026-08-22）**；自定义机制、非 Roslyn 惯例，文件粒度需求由 rsp/命令行覆盖 |
| **E. ScriptOptions API `WithOptimizationLevel`** | `Scripting\Core\ScriptOptions.cs:368` | 已可用，宿主不用 | 程序化宿主途径，保持不变 |

> **运行期正交维度**：`.NET` 默认 `DOTNET_TieredCompilation=1` 会让 JIT 优化热点——这是 **CLR 运行期**优化，与本提案的**编译期**优化级别（`OptimizationLevel`）是两个独立层。本提案只管编译期。

### 核心方案（途径 A）

`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:177` 改为：

```csharp
optimizationLevel: arguments.CompilationOptions.OptimizationLevel,   // 由 /optimize 决定（默认 Debug）
```

- **两处改动（方案 A，勘误 2026-08-24）**：C1 宿主透传（上）+ C1b 脚本解析器脚本专属分支（`VisualBasicCommandLineParser.vb:525-541`）新增 `/optimize`、`/optimize+`（置 `optimize=True`）与 `/optimize-`（置 `optimize=False`），样式仿非脚本分支 `:824-840`。`/optimize` 的解析原本只在**非脚本分支**，脚本专属分支（:475-524）不识别，脚本模式传 `/optimize+` 原落 `WRN_BadSwitch`（BC2007 警告）。
- **消费管道已存在**：`optimize` 布尔（`VisualBasicCommandLineParser.vb:97` 默认 False）→ `VisualBasicCommandLineArguments.CompilationOptions`（共用构造 `:1514` `optimizationLevel:=If(optimize, Release, Debug)`）；`VisualBasicInteractiveCompiler`（`Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:14-19`，构造传 `VisualBasicCommandLineParser.Script`）→ `_compiler.Arguments`。`CompilationOptions.OptimizationLevel` 是 public getter（`Compilers\Core\Portable\Compilation\CompilationOptions.cs:138`）。
- **生效面**：
  - `vbi /optimize+ script.vbx` → 脚本文件 Release 编译（`RunScriptAsync`，`CommandLineRunner.cs:201-222`）。
  - `vbi /optimize+` → REPL 启动即 Release；每轮 submission 的 options 在 `UpdateOptions`（`:322-342`）只更新 resolver、**保留 OptimizationLevel**。
  - `@vbi.rsp` 内写 `/optimize+` → 同等生效（途径 B 自动继承）。
  - 默认（无 `/optimize`）行为不变 = Debug。
- **REPL 中途不可切换**：submission 已编译代码无法重编，切换只影响后续提交，语义不干净。本提案不支持中途切换，优化级别在启动时固定。
- **Release 语义完整定义**：`/optimize+`（无 `/define:DEBUG`）= 优化 + `#If DEBUG` False——与 MSBuild Release 配置（`Optimize=true` 且 DefineConstants 不含 DEBUG）精确一致。VB 编译器默认不定义 DEBUG（`PredefinedPreprocessorSymbols.vb:48-62` 只加 `VBC_VER`/`TARGET`，`defines` 仅来自显式 `/define`，`VisualBasicCommandLineParser.vb:237-244`），无 `/define` 时 `#If DEBUG` 天然为 False，**无需「清除 DEBUG」动作**。`/optimize+` 与 `/define:DEBUG` 两开关正交（前者不定符号、后者不影响优化级别），但**组合无实际用例**——Release 构建带 DEBUG 符号是反模式，Release 语义即 `/optimize+` 无 `/define:DEBUG`。
- **默认 rsp 不定义 DEBUG（用户定案 2026-08-22）**：默认 `vbi.rsp` 保持现状（无 `/optimize`、无 `/define`）。「Debug 配置」心智（`#If DEBUG` True + Debug 优化）由用户自行 `/define:DEBUG` 组合；Release 用 `/optimize+`（无 `/define:DEBUG`）。曾评估「默认 rsp 定义 DEBUG + release rsp 覆盖」——因 `/define` 只可覆盖不可移除（`SetItem`，`VisualBasicCommandLineParser.vb:2108/2115`），release rsp 无法靠「不写 DEBUG」取消，需 `/define:DEBUG=False` 或 `/noconfig` 替换，成本高于收益，**否决**。

### 正交维度：调试符号（`/debug`）

优化级别与调试符号是两个正交开关。当前脚本路径：

- `CommandLineRunner.cs:131` `emitDebugInformation = !_compiler.Arguments.InteractiveMode` —— REPL 不发 PDB，脚本文件模式**必发** PDB（经 `ScriptBuilder.cs:168-169` 的 `GetEmitOptions` + `s_EmitOptionsWithDebuggingInformation` `:51-53`，CoreCLR 用 PortablePdb，`Scripting\Core\Utilities\PdbHelpers.cs:14-24`）。
- 脚本文件模式无 `/debug-` 关闭、REPL 无 `/debug+` 开启。脚本模式传 `/debug` 类开关落 `WRN_BadSwitch`（BC2007 警告）——`/debug` 不被脚本解析器识别（解析在非脚本分支 `:789-822`），警告不阻断会话。（勘误 2026-08-24 修正原文「被忽略」表述。）

`/debug` **不透传（用户定案 2026-08-22）**：vbi 遵循 csi 策略——`emitDebugInformation = !InteractiveMode`（`CommandLineRunner.cs:131`）保持硬编码，不透传 `/debug`/`/debug-`/格式；本提案只透传 `/optimize`。

## Drawbacks
[drawbacks]: #drawbacks

- **Release 失去脚本调试体验**：nop 消除、局部变量复用后，REPL 断点/变量查看体验下降。用户须显式 `/optimize+` 承担这一取舍。
- **环境变量缺位**：会话级「全局默认 Release」需靠 rsp（`@vbi.rsp` 写 `/optimize+`）实现，而非环境变量。对「改一行就全局优化」的心智略绕，但 rsp 本就是 vbi 的既有配置面。
- **REPL 中途不可切**：高 CPU 场景需启动时定好，不能临时切换（见 Alternatives）。

## Alternatives
[alternatives]: #alternatives

- **环境变量兜底（途径 C）**：在 `GetScriptOptions` 读 `VBI_OPTIMIZE`，优先级 命令行 > env > 默认。**已否决（用户定案 2026-08-22）**——编译选项 env 零先例、与 `DOTNET_*` 运行期变量易混淆，且「全局默认」已由 rsp（`@vbi.rsp` 写 `/optimize+`）覆盖，无需新机制。
- **REPL 运行时指令 `#optimize+`**：交互式临时切换。但 submission 已编译代码无法重编，切换只影响后续提交，语义不干净——拒绝。
- **默认改 Release**：默认即优化，但破坏交互调试心智，且让「无开关也优化」成为隐式行为——拒绝。
- **脚本头指令（途径 D）**：`' Attribute Optimize = "release"` 按文件控制。**已否决（用户定案 2026-08-22）**——自定义机制、脱离 Roslyn 惯例；`.vbx` 头部注释已被 `TargetFramework` 占用语义，多属性叠加需设计，文件粒度需求可由 rsp/命令行覆盖。
- **引入 `-c Release` / `--configuration`（对标 dotnet run）**：脚本宿主不是 MSBuild 项目，无 `Configuration` 概念，硬造概念与脚本语义不符——拒绝。
- **维持现状**：高 CPU 脚本永远 Debug，缺口不解决。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **`/debug` 透传——已定案：不透传**（2026-08-22）。vbi 遵循 csi 策略，`emitDebugInformation = !InteractiveMode`（`CommandLineRunner.cs:131`）保持硬编码；本提案只透传 `/optimize`。
2. **帮助文本——已定案（2026-08-22）**：`/help` 简略提及支持 vbc 同款参数（`/optimize`、`/define` 等），不逐条展开（当前 help 由 `VisualBasicInteractiveCompiler.PrintHelp` 输出 `VBScriptingResources.InteractiveHelp`，`Vbi.vb:52-54`）。
3. **`#If DEBUG` 语义——已定案（2026-08-22）**：无需处理。VB 编译器默认不定义 DEBUG（`PredefinedPreprocessorSymbols.vb:48-62` 只加 `VBC_VER`/`TARGET`，`defines` 仅来自 `/define`）。两种配置互相对应：**Release = `/optimize+`（无 `/define:DEBUG`）**＝优化 + `#If DEBUG` False，与 MSBuild Release 配置一致；**Debug 配置 = `/define:DEBUG`（默认优化）**＝`#If DEBUG` True + Debug 优化。`/optimize+` 与 `/define:DEBUG` 两开关正交（前者不定符号、后者不影响优化级别），但**组合无实际用例**——Release 构建带 DEBUG 符号是反模式，Release 语义即 `/optimize+` 无 `/define:DEBUG`。
4. **环境变量兜底——已定案不采用（2026-08-22）**：原问是否加 `VBI_OPTIMIZE`（优先级 命令行 > env > 默认）；rsp 已覆盖「全局默认 Release」，不引入 env 途径。
5. **脚本头指令——已定案不采用（2026-08-22）**：原问是否 `' Attribute Optimize = "release"` 按文件粒度控制；文件粒度需求由 rsp/命令行覆盖。

## 证据来源与证据等级

- 源码（已核实）：
  - `Scripting/Core/Hosting/CommandLine/CommandLineRunner.cs:177`（`optimizationLevel: OptimizationLevel.Debug` 写死）、`:131`（`emitDebugInformation = !InteractiveMode`）、`:49`（csi/vbi 共用入口注释）、`:155-182`（`GetScriptOptions` 不读 `arguments.CompilationOptions`）、`:201-222`（`RunScriptAsync`）、`:322-342`（`UpdateOptions` 保留 OptimizationLevel）。
  - `Compilers/VisualBasic/Portable/CommandLine/VisualBasicCommandLineParser.vb:33`（`Script` parser）、`:806-821`（`/optimize` 解析）、`:1466`（普通 parser 用 Regular kind）、`:1496`（`optimizationLevel:=If(optimize, Release, Debug)`）、`:1467`（`AddPredefinedPreprocessorSymbols`）。
  - `Compilers/VisualBasic/Portable/CommandLine/VisualBasicCommandLineArguments.vb:29`（`CompilationOptions` 为 `VisualBasicCompilationOptions`）。
  - `Compilers/Core/Portable/Compilation/CompilationOptions.cs:138`（`OptimizationLevel` public getter）。
  - 环境变量零先例：`Scripting/` 全树 grep `GetEnvironmentVariable` 仅测试辅助命中（`VisualBasicTest/Helpers/AssertEx.vb:47` ROSLYN_DIFFTOOL）；`Compilers/Core/Portable/` 仅 `SymUnmanagedFactory.cs:79-114`、`ClrStrongName.cs:39-41`、`InternalUtilities/Debug.cs:60`、`CompilerOptionParseUtilities.cs:42`——均非编译选项。
  - `Scripting/Core/ScriptOptions.cs:34`（Default Debug）、`:368`（`WithOptimizationLevel`）。
  - `Scripting/VisualBasic/VisualBasicScriptCompiler.vb:209`（OptimizationLevel 透传）；`{{Roslyn}}src/Scripting/CSharp/CSharpScriptCompiler.cs:61`（C# 同）。
  - `Scripting/Core/ScriptBuilder.cs:168-169`（`GetEmitOptions`）、`:51-53`（`s_EmitOptionsWithDebuggingInformation`）；`Scripting/Core/Utilities/PdbHelpers.cs:14-24`（CoreCLR→PortablePdb）。
  - codegen：`Compilers/Core/Portable/CodeGen/ILBuilder.cs:849,874`、`Compilers/Core/Portable/SynthesizedLocalKind.cs:268-270`、`Compilers/VisualBasic/Portable/CodeGen/CodeGenerator.vb:89`。
  - 宿主接线：`Scripting/VisualBasic/Hosting/CommandLine/Vbi.vb:14-19`（`VisualBasicInteractiveCompiler` 用 `VisualBasicCommandLineParser.Script`）、`Scripting/VisualBasic/VisualBasicScript.vb:150-170`（vbi 执行入口）、`Scripting/VisualBasic/VisualBasicScript.vb:158`（`vbi.rsp` 拼进 args）。
- 上游参考：`{{Roslyn}}src/Scripting/CSharp/CSharpScriptCompiler.cs:61`（C# 侧 optimizationLevel 透传，与 VB 同构）。
- 预测性判断（待定）：`/debug` 透传、env/脚本头指令排期（Unresolved #1/#4/#5）。
