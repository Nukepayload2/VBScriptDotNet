# 详细设计：脚本编译优化级别（/optimize 透传）

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-script-optimization-level.md` → `../../meetings/meeting-script-optimization-level.md`（Active，RESOLUTION #1-#9）→ 概要设计 `design-overview.md`。
> 改动清单逐条给出，实施者可照做。源码事实以任务 README「共享源码事实」为基准。

## 1. 改动清单（宿主透传 + 解析器脚本分支 + help 资源一处）

> **勘误（2026-08-24）**：核心改动实际为**两处**——C1 宿主透传 + C1b VB 脚本解析器脚本分支新增 `/optimize` 开关（方案 A）。原「开关已被解析只差透传」仅对普通编译器（vbc）成立；脚本模式解析器（`IsScriptCommandLineParser`）原不解析 `/optimize`。

### C1. 宿主透传（核心改动之一）

- **文件**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`
- **函数**：`GetScriptOptions`（`:155-182`，`private static`）
- **行号**：`:177`
- **改动形状**：一行替换

```csharp
// 现状（:177）
optimizationLevel: OptimizationLevel.Debug,
// 改为
optimizationLevel: arguments.CompilationOptions.OptimizationLevel,
```

- **可达性核实**：`arguments` 为 `CommandLineArguments`（`CommonCompiler.Arguments`）；`CompilationOptions.OptimizationLevel` 是 public getter（`Compilers\Core\Portable\Compilation\CompilationOptions.cs:138`）；VB 侧 `VisualBasicCommandLineArguments.CompilationOptions` 为 `VisualBasicCompilationOptions`（`VisualBasicCommandLineArguments.vb:29`）。`VisualBasicInteractiveCompiler`（`Vbi.vb:14-19`）用 `VisualBasicCommandLineParser.Script`（`isScriptCommandLineParser:=True`，`VisualBasicCommandLineParser.vb:33`）解析；脚本模式解析 `/optimize` 依赖 C1b（脚本分支 :525-541），`:1514` 构造进 CompilationOptions（脚本/非脚本共用）。
- **默认值**：`optimize` 布尔默认 False（`:97`）→ 无 `/optimize` 时 `OptimizationLevel.Debug`，与现状完全一致。
- **不动**：`:131` `emitDebugInformation`、`:178-180` `allowUnsafe`/`checkOverflow`/`warningLevel`、`:181` `parseOptions`。

### C1b. VB 脚本解析器新增 `/optimize` 开关（方案 A，核心改动之一）

**勘误前提**：`VisualBasicCommandLineParser` 的 `/optimize`、`/debug` 解析位于 `Else`（非脚本）分支的 `Select Case`（`/optimize` :824-840、`/debug` :789-822）；脚本专属分支（`If IsScriptCommandLineParser Then`，:474-524）原不解析两者，脚本模式传 `/optimize+` 原落 `WRN_BadSwitch`（BC2007 警告）、optimize 保持 False。

- **文件**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`
- **函数**：`Parse`（脚本专属 `Select Case`，:475-541）
- **改动形状**：脚本专属 `Select Case` 末尾新增两个 Case（:525-541）
  - `Case "optimize", "optimize+"`：校验无 value 后 `optimize = True`（:525-531）
  - `Case "optimize-"`：校验无 value 后 `optimize = False`（:534-541）
- **复用**：`:97` `optimize` 布尔（默认 False）、`:1514` `optimizationLevel:=If(optimize, OptimizationLevel.Release, OptimizationLevel.Debug)`（脚本/非脚本共用）。

### C2. help 资源（定案 RESOLUTION #9）

- **文件**：`Scripting\VisualBasic\VBScriptingResources.resx`（`InteractiveHelp` 字符串）+ `Scripting\VisualBasic\VBScriptingResources.Designer.vb`
- **改动形状**：`InteractiveHelp` 文本加一句「支持 vbc 同款编译参数（`/optimize`、`/define` 等）」；不逐条展开。
- **注意**：`/help` 输出经 `Vbi.vb:52-54` `PrintHelp` → `VBScriptingResources.InteractiveHelp`。既有 `s_logoAndHelpPrompt` 断言（`CommandLineRunnerTests.vb:28-34`）**不含** `InteractiveHelp`，当前也无断言 `/help` 全文本的用例——**实现 C2 无需改既有断言**（验证者 2026-08-23 核验）。

### C3. 测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

见 `test-plan.md`；此处列宿主测试所需访问点：

- `CreateRunner(args, input, responseFile, workingDirectory)`（`CommandLineRunnerTests.vb:91-116`）构造 runner。
- 解析断言：`runner.Compiler.Arguments.CompilationOptions.OptimizationLevel`（`runner.Compiler` 为 internal，`CommandLineRunner.cs:46`）。
- 端到端观察：宿主创建的 `Script` 局部不可达；`Script.Options` public（`Script.cs:69`）但无钩子——见测试计划 §4 的两种方案。

## 2. 不做的事（明确排除）

- **`/debug` 透传**：`emitDebugInformation = !InteractiveMode`（`CommandLineRunner.cs:131`）保持硬编码（RESOLUTION #8）。
- **默认 rsp 加 `/define:DEBUG`**：默认 `vbi.rsp` 保持现状（RESOLUTION #7）。曾评估「默认定义 DEBUG + release rsp `/define:DEBUG=False` 覆盖」——`/define` 累积 `SetItem` 只覆盖不移除（`VisualBasicCommandLineParser.vb:2108/2115`），release rsp 无法靠「不写 DEBUG」取消，需 `DEBUG=False` 或 `/noconfig`，成本高于收益，否决。
- **环境变量 / 脚本头指令 / configuration 开关**：不引入。
- **REPL 运行时切换指令**（如 `#optimize+`）：submission 已编译代码无法重编，不支持（RESOLUTION #2）。
- **语法/绑定层改动**：不涉及。**解析器改动仅限 VB 脚本专属分支**（C1b 新增 `/optimize` 开关）；C# 侧与非脚本分支均不改。

## 3. 边界与迁移影响

| 项 | 影响 |
|----|------|
| 默认行为 | 无 `/optimize` → Debug，与现状完全一致（零回归面） |
| `/optimize+` 脚本文件 | Release 编译（`RunScriptAsync`，`:201-222`） |
| `/optimize+` REPL | 启动即定 Release（`UpdateOptions` `:322-342` 保留），全程一致 |
| rsp 写 `/optimize+` | 全局默认 Release（`CommonCompiler.cs:130-132` 展开进 args） |
| csi | **不自动受益**（方案 A 不扩展 C# 解析器）：C# 脚本解析器（`CSharpCommandLineParser.cs:308-357`）同构不解析 `/optimize`，共享 `CommandLineRunner.cs:177` 只让 vbi 生效（csi 仍硬编码 Debug） |
| `/debug` | 不变（`!InteractiveMode`） |
| `#If DEBUG` | 不变（VB 默认无 DEBUG 符号，`/define` 控制） |

## 4. 测试矩阵（概要）

- **L1/L2（解析/语义）**：N/A——本特性无语法/绑定层改动。
- **L3（API）**：`Script.Create` + `ScriptOptions.WithOptimizationLevel(Release)` → `DirectCast(script.GetCompilation(), VisualBasicCompilation).Options.OptimizationLevel = Release`（证明消费端 `VisualBasicScriptCompiler.vb:209` 已就位）。
- **L4（宿主）**：`CommandLineRunnerTests`——解析断言（`runner.Compiler.Arguments.CompilationOptions.OptimizationLevel`：默认 Debug、`/optimize+` Release、`/optimize-` Debug、rsp `/optimize+` Release）+ 交互/脚本冒烟 + `/debug` 不变断言。
- 详见 `test-plan.md`。
