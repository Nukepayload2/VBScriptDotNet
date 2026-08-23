# 概要设计：脚本编译优化级别（/optimize 透传）

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-script-optimization-level.md`（Active）→ `../../meetings/meeting-script-optimization-level.md`（Active，RESOLUTION #1-#9）。
> 本设计吸收会议 RESOLUTION #1-#9，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。

## 1. 背景与目标

**目标（一句话）**：让脚本（REPL 与 `.vbx` 文件执行）能以 **Release 优化** 编译——`/optimize+`（命令行或 `@vbi.rsp`）生效，默认仍 Debug；`/debug` 不透传（遵循 csi 策略）；不引入环境变量/脚本头指令/configuration 概念。

- **现状缺口**：脚本宿主把优化级别写死 `OptimizationLevel.Debug`（`CommandLineRunner.cs:177`），`/optimize` 虽被命令行解析进 `arguments.CompilationOptions`（`VisualBasicCommandLineParser.vb:806-821 → :1496`）但 `GetScriptOptions` 不读——**开关被解析却不生效**。高 CPU 压力脚本只能 Debug 跑，JIT tiered compilation 补不了编译器级优化缺失（`ILBuilder.cs:849,874`、`SynthesizedLocalKind.cs:268-270`、`CodeGenerator.vb:89`）。
- **状态**：提案与专用 LDM 会议均为 **Active**；传递途径定案为命令行参数 + rsp，`/debug` 硬编码不透传。

**RESOLUTION 吸收映射**：

| RESOLUTION | 内容 | 落在本设计 |
|-----------|------|-----------|
| #1 | 透传 `/optimize`，默认仍 Debug | 第 2、4 节 |
| #2 | REPL 启动时定、中途不可切 | 第 6 节 |
| #3 | Release 语义 = `/optimize+` 无 `/define:DEBUG`（VB 默认无 DEBUG 符号） | 第 3 节 |
| #4/#5 | 环境变量、脚本头指令不采用 | 第 7 节 |
| #6 | 传递途径 = 命令行参数 + rsp | 第 2 节 |
| #7 | 默认 rsp 不加 DEBUG | 第 3 节 |
| #8 | `/debug` 硬编码不透传 | 第 5 节 |
| #9 | help 简略提及 vbc 同款参数 | 第 8 节 |

## 2. 总体架构（唯一断点 + 已就位管道）

**核心观察**：编译消费端已透传（`VisualBasicScriptCompiler.vb:209` `optimizationLevel:=script.Options.OptimizationLevel`），命令行解析已就位（`arguments.CompilationOptions.OptimizationLevel`，`CompilationOptions.cs:138` public getter）——**唯一断点是宿主 `CommandLineRunner.cs:177` 写死 Debug**。

```
命令行 /optimize+ ──┐                    ┌─ ScriptOptions.OptimizationLevel ──> VisualBasicCompilationOptions
@vbi.rsp /optimize+ ─┴─> VisualBasicCommandLineParser ─> arguments.CompilationOptions ─> GetScriptOptions(:177 透传)
                                                        (VisualBasicCommandLineArguments.vb:29)   ^
                                                                                                 │ 断点
```

### 2.1 命令行解析层（已就位，不改）

- `/optimize`/`/optimize+`/`/optimize-` → `VisualBasicCommandLineParser.vb:806-821`（布尔 `optimize`，默认 False）→ `:1496` `optimizationLevel:=If(optimize, Release, Debug)` 进 `VisualBasicCommandLineArguments.CompilationOptions`（`VisualBasicCommandLineArguments.vb:29`）。
- rsp（`@vbi.rsp`）经 `CommonCompiler.cs:130-132` 前置拼入 args，与命令行同走此路径。

### 2.2 宿主层（唯一改动点）

- `CommandLineRunner.cs:177` `optimizationLevel: OptimizationLevel.Debug` → **`arguments.CompilationOptions.OptimizationLevel`**。
- 默认（`optimize=False`）→ Debug，行为零变化。

### 2.3 编译消费端（已就位，不改）

- `VisualBasicScriptCompiler.vb:209` 把 `script.Options.OptimizationLevel` 透传进 `VisualBasicCompilationOptions`；上游 C# 同（`CSharpScriptCompiler.cs:61`）。

## 3. 行为对照表

| 用法 | 优化级别 | `#If DEBUG` | 说明 |
|------|---------|-------------|------|
| 默认（无开关） | **Debug** | False | 现状，零变化 |
| `vbi /optimize+ script.vbx` | **Release** | False | 高 CPU 脚本 |
| `@vbi.rsp` 写 `/optimize+` | **Release** | False | 全局默认 Release |
| `vbi /optimize+`（REPL） | **Release** | False | 启动即定，全程 |
| `vbi /optimize-` | **Debug** | False | 显式 Debug |
| `/optimize+ /define:DEBUG` | Release | **True** | 正交组合（Release 优化 + DEBUG 符号） |
| `/debug:portable` 等 | 不变 | 不变 | **不透传**，`emitDebugInformation = !InteractiveMode`（`:131`） |

> Release 语义 = `/optimize+`（无 `/define:DEBUG`）= 优化 + `#If DEBUG` False，与 MSBuild Release 配置（`Optimize=true` 且 DefineConstants 无 DEBUG）一致。VB 编译器默认不定义 DEBUG（`PredefinedPreprocessorSymbols.vb:48-62`），无需「清除 DEBUG」动作。

## 4. 判定原则

- **透传单一开关**：只读 `arguments.CompilationOptions.OptimizationLevel`，不评估其它编译开关。
- **默认 Debug**：`optimize` 布尔默认 False（`VisualBasicCommandLineParser.vb:97,806-821`），无 `/optimize` 时行为与现状完全一致。
- **`/debug` 不透传**：`emitDebugInformation = !InteractiveMode`（`:131`）保持硬编码，遵循 csi 策略（RESOLUTION #8）。

## 5. 与 `/debug` 的关系（正交，不改）

- `/debug`（`VisualBasicCommandLineParser.vb:771-804`）控制 PDB 生成与格式（`full`/`pdbonly`/`portable`/`embedded`、`/debug+`/`/debug-`），进 `EmitOptions.DebugInformationFormat`（`:1503`）。
- 脚本路径当前忽略它（`emitDebugInformation = !InteractiveMode`）；**本特性保持该现状**，只透传 `/optimize`。

## 6. 代价与边界

- **Release 失去脚本调试体验**：nop 消除、局部变量复用后 REPL 断点/变量查看体验下降——用户显式 `/optimize+` 承担。
- **REPL 中途不可切**：submission 已编译代码无法重编；`UpdateOptions`（`:322-342`）只更新 resolver、保留 OptimizationLevel——优化级别启动时固定，全程一致。
- **csi 同步受益（行为变化）**：同一 `CommandLineRunner.cs`，csi 今天也硬编码 Debug；透传后 csi 也支持 `/optimize+`——对 csi 是行为变化（历史一直无法 Release）。
- **rsp 继承**：rsp 是命令行参数的载体（`CommonCompiler.cs:130-132` 展开进 args），透传后自动生效，无需独立改动。

## 7. 决策记录（原未决问题，全部已定案）

- **传递途径（定案）**：命令行参数 + rsp；环境变量（编译选项零 env 先例）与脚本头指令（自定义机制、脱离 Roslyn 惯例）不采用。
- **`/debug`（定案）**：硬编码 `!InteractiveMode` 不透传，遵循 csi 策略。
- **默认 rsp（定案）**：不加 DEBUG，保持现状；「Debug 配置」由用户自行 `/define:DEBUG`。
- **configuration 概念（定案不引入）**：脚本宿主不是 MSBuild 项目；`/optimize+` 无 `/define:DEBUG` 即 Release 语义，无需配置层。
- **REPL 中途切换（定案不支持）**：启动时定。
- **编译运行模式（定案推迟，RESOLUTION #11）**：`/out:` 完整编译形态与 `vbx` tool 编译路径统一规划，不并线本提案。

## 8. help（定案，实现细节）

`/help` 简略提及支持 vbc 同款编译参数（`/optimize`、`/define` 等），不逐条展开。当前 help 由 `Vbi.vb:52-54` `PrintHelp` → `VBScriptingResources.InteractiveHelp`；实现时在资源文本加一句即可（F5 范围内）。
