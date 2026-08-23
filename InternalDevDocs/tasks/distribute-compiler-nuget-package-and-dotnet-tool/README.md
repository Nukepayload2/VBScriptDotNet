# 任务：分发 fork 编译器（Toolset NuGet 包 + .net tool）（distribute-compiler-nuget-package-and-dotnet-tool）

本文件夹是 `proposal-distribute-compiler-nuget-package-and-dotnet-tool` 的设计任务存储（Vortex 代办列表 + 设计产物）。本任务分两段能力：**Part A** Toolset 风格编译器 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta，.vbproj 引用即用 fork VB 编译器）、**Part B** `.net tool`（`Nukepayload2.Compilers.VBScriptDotNet.Cli`，`vbi` 命令：批量编译 + `.vbx` 执行 + shebang 解释器）。**边界定案（用户 2026-08-22）**：不含 csc、不替代 dotnet 工具链、专注 VB fork、net472 允许缺失、包描述不提及 Microsoft。

- **依据链**：`../../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（Active，RESOLUTION Active）→ `../../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md`（RESOLUTION + 四条 Unresolved 闭合）→ `../../meetings/evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md`（五维评分 + C# 生态）→ `../../compilers-index.md`（编译器树索引）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`）、测试计划（`test-plan.md`）。三份均要求无副作用测试矩阵（本任务是打包/工具链任务，测试以「构建产物验证」为主，进程启动/文件写入仅在 `dotnet pack` 集成验证阶段由用户手动跑，单测部分无副作用）。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

> 本任务仅覆盖**设计阶段**（F1-F4）；实现阶段（F5+）待设计通过后按 design-detailed.md 改动清单逐条再拆（实施者 + 验证者串行交替），届时回填本表。

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`，2026-08-23 验证者复验 PASS：8 修复到位 + IVT 行号 :52 修正） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`，2026-08-23 验证者复验 PASS：8 修复到位 + IVT 行号 :52 修正） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`，2026-08-23 验证者复验 PASS：rsp 矛盾消除） |
| F4 | 一致性审计 + 修复 + 复验 | F1/F2/F3 三份交付物与 proposal/meeting/evaluation 交叉一致（包名/版本/边界/四条闭合、不含 csc、net472 允许缺失、`vbi` 命名、`.Cli` 后缀）；源码事实行号全部真实 | **done**（2026-08-23 验证者交叉核对：一致性强、40+ 引用核实、rsp 归属正确，无阻断） |
| F5 | 实现：补回 MSBuildTask（`Compilers\Core\MSBuildTask\`，A1） | 按 design-detailed A1：~20 文件镜像 + csproj（4 层 `..` Contracts 引用）+ Tasks.Core 依赖；编译通过；不注册 Csc | 待开始 |
| F6 | 实现：vbc publish 布局（A2） | 按 design-detailed A2：publish 产出（dll/deps/runtimeconfig），无 rsp，bincore 只含 VB 面 | 待开始 |
| F7 | 实现：打包项目（A3）+ props（A4） | 按 design-detailed A3/A4：`Installer\Toolset\...Package.csproj` + `build\` props；`dotnet pack` 产出 nupkg | 待开始 |
| F8 | 实现：`vbi` tool 打包（B1）+ 版本（B3） | 按 design-detailed B1/B3：`PackAsTool` + `.Cli` 包名 + `vbi.rsp` 打包 + `AssemblyInformationalVersion` 落 `2.0.0-Beta` | 待开始 |
| F9 | 实现：`vbi` 编译模式（B2）+ 方案登记（B4） | 按 design-detailed B2/B4：vbi.vbproj 补 Shared 源文件 + 模式判定 + sln 登记 | 待开始 |
| F10 | 集成验证 + 回归（L2/L3 + 七门 gate） | 按 test-plan：L2 打包产物核对（无 rsp）、L3 `vbi` 命令面；`scripts\verify-vb-compiler-tests.ps1` 全绿 | 待开始 |

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-22，proposal/meeting 深挖逐条 Read/Grep）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。编译器部分统一前缀 `Compilers\`（下文简写 `C\`），文件相对路径均相对仓库根。

### Part A Toolset 编译器 NuGet 包（打包落点）

- **三库已 IsPackable**：`C\Core\Portable\Microsoft.CodeAnalysis.csproj:15-16`（`IsPackable=true` + `PackageId=Microsoft.CodeAnalysis.Common`）、`C\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`C\CSharp\Portable\Microsoft.CodeAnalysis.CSharp.csproj:13`。目标框架三库 `netstandard2.0;net10.0`（`Microsoft.CodeAnalysis.csproj:8` 等）。
- **vbc 驱动**：`C\VisualBasic\vbc\AnyCpu\vbc.csproj`（net10.0 Exe，`AssemblyName=vbc`，`:4-5`）→ `C\VisualBasic\vbc\Program.cs:39` `BuildClient.Run(args, RequestLanguage.VisualBasicCompile, Vbc.Run, ...)`；进程内入口 `C\Shared\Vbc.cs:22-29`。
- **被裁剪组件（grep 零命中）**：`C\Core\MSBuildTask\`（`Microsoft.Build.Tasks.CodeAnalysis`）与 `C\Server\`（VBCSCompiler）fork 均无。上游参考：`{{Roslyn}}src\Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`（net10.0 + net472，`Import Contracts.projitems`）、`{{Roslyn}}src\Compilers\Core\MSBuildTask\Directory.Build.props:26-54`（源文件清单：`ManagedCompiler.cs`/`ManagedToolTask.cs`/`Vbc.cs`/`Csc.cs`/`CopyRefAssembly.cs`/`MapSourceRoots.cs`/`GenerateMSBuildEditorConfig.cs`/`ErrorString.resx` + `Microsoft.*.targets`；依赖 `C\Shared\ConsoleUtil.cs`、`C\Core\Portable\InternalUtilities\{CommandLineUtilities,CompilerOptionParseUtilities,ReflectionUtilities}.cs`——**fork 均已有**）。
- **进程内编译回退**：`C\Shared\BuildClient.cs:148-165`（服务器失败→本地）、`:190-194`（`RunLocalCompilation` = `_compileFunc`）。`UseSharedCompilation=false` 即进程内。
- **上游 Toolset 包结构**：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:30-36`（ProjectReference vbc/csc/csi/VBCSCompiler/MSBuildTask）+ `:38-43`（`ProjectReference Update` 设 `Targets="Publish"`）+ `:51-64`（`_GetFiles` 铺 `tasks/net472`、`tasks/netcore`、`tasks/netcore/bincore`、`tasks/netcore/binfx`）；`AnyCpu\build\Microsoft.Net.Compilers.Toolset.props:9-35`（`_UseRoslynBridgeTask`、`RoslynAssembliesPath`、`RoslynTasksAssembly`、`UseSharedCompilation`、`RoslynCompilerType=Custom`）。
- **net472 允许缺失**（调查结论）：`Microsoft.Net.Compilers.Toolset.props:9-13` 按 `MSBuildRuntimeType` 选 `netcore`/`net472`；VS 里 SDK 项目走 MSBuild Core → 用 bincore，缺 net472 无影响；桌面 MSBuild 需 bridge 壳 `Microsoft.Build.Tasks.CodeAnalysis.Sdk`（`Sdk\...csproj:7` net472-only，`GetToolDirectory` → `..\bincore`，`ManagedToolTask.cs:232-234`）。

### Part B `vbi` .net tool（工具落点）

- **vbi 项目**：`Interactive\vbi\vbi.vbproj`（`TargetFrameworks=net10.0-windows;net10.0;net48`，`:7`；ProjectReference → `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj`，`:16-18`）。
- **执行入口**：`Interactive\vbi\Vbi.vb:57` `VisualBasicScript.RunInteractiveAsync(args, vbiDirectory, InteractiveResponseFileName)`；`Scripting\VisualBasic\VisualBasicScript.vb:150-170`（构造 `VisualBasicInteractiveCompiler` + `CommandLineRunner`）。
- **模式分发**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:118-128`（`sourceFiles` 非空分支读脚本）、`:144-152`（`InteractiveMode` 切换：真→交互循环 `:146`、假→`RunScriptAsync` `:151`）。
- **编译路径**：`C\VisualBasic\vbc\AnyCpu\vbc.csproj` + `C\VisualBasic\vbc\Program.cs:39`（`BuildClient.Run`）；`C\Core\Portable\CommandLine\CommonCompiler.cs:747`（`Run` 编译+Emit）。
- **版本链路（已就位）**：`Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:31-50`（`PrintLogo` → `GetSelfVersion` 读 `AssemblyInformationalVersion` `:39-42` + `GetRoslynVersion` `:48-50`）；`CommonCompiler.cs:159-170`（`GetProductVersion`）；Logo 文案 `Scripting\VisualBasic\VBScriptingResources.resx:120-159`（Nukepayload2's fork / Based on Roslyn / pre-release）。
- **rsp**：`vbi` tool 用 `vbi.rsp`（`Vbi.vb:20` `InteractiveResponseFileName="vbi.rsp"`；`vbi.coreclr.rsp` Link 产物，`vbi.vbproj:21-24`，默认 imports/references 见 `vbi.coreclr.rsp` 本体，不在此枚举）——随 tool 打包。**netcore 编译器（Part A bincore）不带 rsp**（上游 `CoreClrCompilerArtifacts.targets` 零 rsp 匹配；`vbc.rsp` 是 net472 桌面 `vbc.exe` 专属，net472 允许缺失故不打包）。
- **版本号**：`Directory.Build.props:6` `VersionPrefix=5.9.0`（Roslyn 上游）；目标产品版本 `2.0.0-Beta`。
- **shebang（已实现）**：`proposal-shebang-directive.md`（done）——`#!` 已在编译器语法层，`vbi` 可作 `#!/usr/bin/env vbi` 解释器。

## 关键设计决策（源自 meeting RESOLUTION + proposal，设计文档必须吸收）

1. **包名/版本**：编译器 NuGet 包 = `Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta；tool 包 = `Nukepayload2.Compilers.VBScriptDotNet.Cli`（`.Cli` 后缀为 GUI 界面产品留分层）。
2. **边界**：不含 csc、不替代 dotnet 工具链、专注 VB fork、net472 允许缺失、包描述「基于 .NET Foundation 的 Roslyn 编译器改造而来」不提及 Microsoft。
3. **Part A 只注册 Vbc**：`Microsoft.Build.Tasks.CodeAnalysis` 补回但只注册 `Vbc` 任务；`bincore` 只铺 Core + VisualBasic（不含 `Microsoft.CodeAnalysis.CSharp.dll`）；`UseSharedCompilation=false`。
4. **Part B `vbi` 命名**：复用现有 `Interactive\vbi\vbi.vbproj`，`PackAsTool=true` + `ToolCommandName=vbi`，不新建 `vbx`/`vbc` 名。
5. **Part B 模式分发**：隐式——`.vbx` 无 `/out:` 执行、`.vb` 或带 `/out:` 编译、`--` 分隔脚本参数。
6. **`vbi --version` 双行**：现有 `PrintLogo` 机制，`AssemblyInformationalVersion` 落 `2.0.0-Beta` + Roslyn 上游。
7. **VBCSCompiler 不做**：`UseSharedCompilation=false` 进程内，不复用 dotnet cli 的 server 做法。

## F1 验收条件（概要设计 pass 标准）

- 覆盖**现状断点**（三库 IsPackable 无产物、MSBuildTask/VBCSCompiler 被裁剪、唯一分发 MSIX、`vbi` 无打包）与**总体架构落点**（Part A Toolset 包四步：补回 MSBuildTask / vbc publish / 打包项目 / props；Part B `vbi` tool：现有项目加打包 + 模式分发 + 版本链路）。
- 含**行为对照表**：.vbproj 引用包后构建用 fork vbc（VB 面）、C# 仍走 SDK、`vbi src.vb /out:app.dll` 编译、`vbi script.vbx` 执行、`vbi --version` 双行、net472 缺失时 VS SDK 项目可用。
- 说明延申原则（复用 `IsPackable`/`Vbc.Run`/`CommandLineRunner`/`PrintLogo`，现有路径并列不改写）与上游 Toolset 参考跟随（`Microsoft.Net.Compilers.Toolset` 包结构）。
- 说明代价与边界（MSBuildTask 补回维护面、无编译服务器构建变慢、net472 缺失仅影响老式非 SDK 项目）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 属性/项 + 行号区域 + 改动形状**（csproj 加 pack 配置、新建打包项目结构、props/targets 内容），可被实施者直接照做。
- **Part A 补回**：`Compilers\Core\MSBuildTask\` 源文件清单（对齐上游 `Directory.Build.props:26-54`）+ fork 依赖文件核对（`C\Shared\ConsoleUtil.cs`、`C\Core\Portable\InternalUtilities\` 三文件已有）+ `Contracts.projitems` 引用；只注册 `Vbc` 任务。
- **Part A 打包**：`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj` 结构（`NuspecPackageId`/`IncludeBuildOutput=false`/`DevelopmentDependency=true`/`_GetFiles` 铺 `tasks/netcore` + `tasks/netcore/bincore`）；`build\` + `buildMultiTargeting\` props（`UseSharedCompilation=false`、`RoslynAssembliesPath`、`RoslynCompilerType=Custom`）。
- **Part A 发布**：`vbc.csproj` publish 产出（`vbc.dll` + deps + runtimeconfig + rsp）；bincore 只含 Core + VisualBasic。
- **Part B 打包**：`Interactive\vbi\vbi.vbproj` 加 `PackAsTool=true` + `ToolCommandName=vbi` + 包名 `Nukepayload2.Compilers.VBScriptDotNet.Cli`；net10.0 TFM 与 net10.0-windows/net48 的关系（tool 只发 net10.0）。
- **Part B 模式分发**：`vbi` 编译模式接入（`Vbc.Run`/`BuildClient.Run` vs `CommandLineRunner.RunScriptAsync` 分派边界）；`AssemblyInformationalVersion` 落 `2.0.0-Beta` 的版本映射。
- 零改动清单 + 边界（不含 csc、不替代 dotnet 工具链、net472 允许缺失）。

## F3 验收条件（测试计划 pass 标准）

- 分层测试矩阵（L1 编译器单测无副作用 / L2 打包产物验证 / L3 `vbi` tool 命令面），参考上游 Toolset 打包测试强度。
- 用例矩阵覆盖：Toolset 包引用后 .vbproj 构建用 fork vbc；bincore 不含 CSharp.dll；`vbi src.vb /out:app.dll` 编译；`vbi script.vbx` 执行；`vbi --version` 双行；net472 缺失不影响 SDK 构建。
- 无副作用纪律：单测不网络/不写文件/不启动进程/不注册表；打包产物验证（`dotnet pack` + 临时项目引用）属集成验证，由用户手动跑或说明测试边界，测不了就问用户。
