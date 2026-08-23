# 分发 fork 编译器：Toolset 风格 NuGet 包 + .NET tool / Distribute the Fork Compiler as a Toolset NuGet Package and a .NET Tool

> **RESOLUTION Active**：见 `../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md`（2026-08-22，四条 Unresolved 闭合）。

* [x] Proposed
* [ ] Prototype: [Not Started](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

把本 fork（VBScript.NET 的修剪/本地化 Roslyn，`Compilers\VisualBasic\Portable\` 是 vbc 与 vbx 脚本/REPL 的同一份编译器）从「仅微软商店 MSIX 分发 vbi REPL」扩展为**两条编译器中转分发渠道**：

- **A. Toolset 风格编译器 NuGet 包**（包 ID **`Nukepayload2.Compilers.VBScriptDotNet`**，版本 **`2.0.0-Beta`**）：普通 SDK-style `.vbproj` 引用该包后，构建即用本 fork 的 VB 编译器（含 fork 已实现/待实现的全部编译器能力，如消费 C#14 扩展成员 / SAIM，见 `proposal-consume-csharp-extension-and-interface-shared.md`）。仿 `Microsoft.Net.Compilers.Toolset` 的 **VB 面**——**不含 csc**、不注册 C# 任务、不替代 dotnet 工具链整体，专注 VB fork。
- **B. `.net tool`（`vbi` 命令）**：把现有 `vbi`（`Interactive\vbi\vbi.vbproj`，`AssemblyName=vbi`）以 dotnet tool 分发，命令面聚焦**批量编译**（`.vb`/`.vbx` → 程序集，新增能力）与 **`.vbx` 脚本直接执行**（`vbi script.vbx [-- args]`，既有能力），并可作为已实现 shebang（`#!`）的 Linux 解释器；交互 REPL 为同一二进制既有能力，顺带保留。**Part A 的编译器 NuGet 包不是 vbi**——即「不是 NuGet dotnet tool vbi」的语义：编译器包是编译器，tool 才是 vbi。

**策略**：两条渠道都从 fork **已有**的机制延申——编译器三库已 `IsPackable=true`（证据见 Motivation），vbc 命令行驱动与 `Vbc.Run` 进程内编译入口已存在，脚本执行核心 `VisualBasicScript.RunInteractiveAsync` 已被 vbi 使用。本提案补齐的是**打包/分发壳**与**被裁剪组件的补回**（MSBuild 任务、可能含编译服务器），不发明新机制。

## Motivation
[motivation]: #motivation

- **现状断点（源码 + 结构核实）**：
  1. 编译器三库已声明可打包：`Microsoft.CodeAnalysis.csproj:15-16`（`IsPackable=true` + `PackageId=Microsoft.CodeAnalysis.Common`）、`Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`Microsoft.CodeAnalysis.CSharp.csproj:13`——但**无任何打包产物/发布 feed**：仓库无 `.nuspec`、无 `PackAsTool`、`NuGet.config` 全为 dnceng 消费源（`NuGet.config:3-21`），无发布源。`eng\` 目录不存在（无 CI/发布脚本）。
  2. 唯一分发 = 微软商店 MSIX（`VBInteractive.WindowsDesktop.Installer.wapproj`，包名 `N2ForkVBInteractivePreview` / Identity Version `1.2.0.0`，见 `spec\README.md:26`）。开发者若要在普通 `.vbproj` 里用 fork 编译器，**无渠道**。
  3. Toolset 风格需要但 **fork 已裁剪**的组件（grep 零命中）：
     - `Compilers\Core\MSBuildTask\`（`Microsoft.Build.Tasks.CodeAnalysis`，MSBuild 任务 DLL）——上游在 `{{Roslyn}}src\Compilers\Core\MSBuildTask\`，其 csproj 引用 `Contracts.projitems`（fork 有 `Dependencies\Contracts\`）。
     - `Compilers\Server\VBCSCompiler\`（编译服务器）——fork 无 `Compilers\Server\`。
  4. 上游参考实现（已检查）：
     - Toolset 包：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:30-36`（ProjectReference csc/vbc/csi/VBCSCompiler/MSBuildTask，`Targets="Publish"`）+ `:51-64`（`_GetFiles` 把产物铺进 `tasks/net472`、`tasks/netcore`、`tasks/netcore/bincore`、`tasks/netcore/binfx`）；build props `Microsoft.Net.Compilers.Toolset.props:38-41`（`UsingTask Vbc/Csc`、`RoslynAssembliesPath`、`UseSharedCompilation=true`、`RoslynCompilerType=Custom`）。
     - dotnet tool 范式：`{{Roslyn}}src\LanguageServer\...\Microsoft.CodeAnalysis.LanguageServer.csproj:12-14`（`PackAsTool=true` + `ToolCommandName=roslyn-language-server`）。
- **进程内编译回退已存在**：`Compilers\Shared\BuildClient.cs:148-165`——`hasShared` 时先试服务器，`RunServerCompilation` 失败或不可用时**回退 `RunLocalCompilation`**（`_compileFunc` = `Vbc.Run`，进程内）。因此 **v1 不打包 VBCSCompiler 也能工作**（props 设 `UseSharedCompilation=false` 走本地编译），与上游 `ManagedToolTask` 的 `UseSharedCompilation` 属性对应。
- **vbc 驱动与执行核心已存在**：
  - `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`（net10.0 Exe，`AssemblyName=vbc`）→ `Program.cs:39` `BuildClient.Run(args, RequestLanguage.VisualBasicCompile, Vbc.Run, ...)`；`Compilers\Shared\Vbc.cs:22-29` 是进程内 `Vbc.Run` 入口。
  - `.vbx` 脚本执行：`Scripting\VisualBasic\VisualBasicScript.vb:150-170` `RunInteractiveAsync` 构造 `VisualBasicInteractiveCompiler` + `CommandLineRunner`——vbi 就是经它跑（`Interactive\vbi\Vbi.vb:57`）。dotnet tool 复用**同一 `vbi` 二进制**，零新增执行面。
  - shebang 已实现（`proposal-shebang-directive.md`，done）：`#!` 已是编译器语法层 trivia，`vbi` 可作为解释器（`#!/usr/bin/env vbi`）。
- **生态价值**：C#14 扩展成员/SAIM 等编译器能力（`proposal-consume-csharp-extension-and-interface-shared.md`）只有**能被项目构建消费**才有意义。Toolset 包让普通 VB 项目构建用上 fork 编译器；`vbi` tool 让脚本/CI 无需装商店版即可用 `.vbx`。这是编译器能力分发的使能层。
- **边界（用户定案 2026-08-22）**：**不含 csc**、不注册 C# MSBuild 任务、不替代 dotnet 工具链整体；**专注 VB fork**。包描述写「基于 .NET Foundation 的 Roslyn 编译器改造而来」，**不直接提及 Microsoft**。

## Detailed design
[design]: #detailed-design

### Part A：Toolset 风格编译器 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet`）

> 范围（用户定案 2026-08-22）：包名 `Nukepayload2.Compilers.VBScriptDotNet`、版本 `2.0.0-Beta`、**不含 csc**、**不替代 dotnet 工具链**、**专注 VB fork**。v1 只支持 **.NET SDK（MSBuild Core / `dotnet build`）**，**net472 允许缺失**（VS 里 SDK 风格项目走 MSBuild Core 即可正常编译，仅老式非 SDK .NET Framework 项目用不上）——fork 编译器 vbc 仅 net10.0（`vbc.csproj:4`），与上游 `$(NetRoslynSourceBuild);net472` 双目标不同；net472 见 Unresolved。

**A1. 补回 MSBuild 任务（`Compilers\Core\MSBuildTask\`）**

从上游镜像 `Microsoft.Build.Tasks.CodeAnalysis`（C# 项目，`{{Roslyn}}src\Compilers\Core\MSBuildTask\`）：
- csproj 参照上游 `MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`（`TargetFrameworks=$(NetRoslynSourceBuild);net472` + `Import Contracts.projitems`）；fork 版 **只 target net10.0**（对齐 fork 编译器 net10.0-only），`net472` 留后续。
- 源文件：`ManagedCompiler.cs`、`ManagedToolTask.cs`、`Vbc.cs`、`Csc.cs`、`CopyRefAssembly.cs`、`MapSourceRoots.cs`、`GenerateMSBuildEditorConfig.cs`、`ErrorString.resx`、`Microsoft.*.Core.targets` 等（约 20 文件）。**仅注册/使用 Vbc 任务**——Csc.cs 保留源文件但产物不含 csc、props 不注册 Csc（见 A4），满足「不含 csc」。
- 依赖：`Microsoft.Build.Framework` / `Microsoft.Build.Utilities.Core`（PackageReference）+ `Dependencies\Contracts\Microsoft.CodeAnalysis.Contracts.projitems`（fork 已有）。**不直接引用编译器 DLL**——运行时经 `RoslynAssembliesPath` 加载 `bincore` 里的编译器程序集，并调用 `Vbc.Run` 进程内入口（fork `Compilers\Shared\Vbc.cs:22-29` 已有）。

**A2. vbc 发布布局（netcore）**

- `vbc.csproj` 增加 publish 产出（`dotnet publish`）：`vbc.dll` + `vbc.deps.json` + `vbc.runtimeconfig.json` + `vbc.rsp`。
- 编译器核心 DLL（`Microsoft.CodeAnalysis.dll`、`Microsoft.CodeAnalysis.VisualBasic.dll` + resources）随包铺进 `tasks/netcore/bincore/`（上游布局 `CoreClrCompilerArtifacts.targets:11-18`）。**不含 `Microsoft.CodeAnalysis.CSharp.dll`**——VB 编译只需 Core + VisualBasic。

**A3. 打包项目**

新项目（建议 `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`），仿 `Microsoft.Net.Compilers.Toolset.Package.csproj`：
- `IsPackable=true`、`NuspecPackageId=Nukepayload2.Compilers.VBScriptDotNet`、`IncludeBuildOutput=false`、`DevelopmentDependency=true`。
- 版本 `2.0.0-Beta`；包描述写「基于 .NET Foundation 的 Roslyn 编译器改造而来」——**不直接提及 Microsoft**；版权/作者标志按 fork 作者（`Nukepayload2`）署名。
- `ProjectReference` → `vbc`（`Targets="Publish"`，`ReferenceOutputAssembly=false`）+ `MSBuildTask`。
- `_GetFiles` target 收集产物到 `tasks/netcore/`（任务 DLL + targets + deps）+ `tasks/netcore/bincore/`（编译器 DLL + vbc 驱动）。
- `build\` + `buildMultiTargeting\` props/targets。

**A4. build props（仿 `Microsoft.Net.Compilers.Toolset.props`，fork 差异标注）**

```xml
<UsingTask TaskName="Microsoft.CodeAnalysis.BuildTasks.Vbc" AssemblyFile="$(RoslynTasksAssembly)" />
<!-- 只注册 Vbc：不含 csc，不替代 dotnet 工具链 -->
<RoslynCompilerType>Custom</RoslynCompilerType>
<RoslynAssembliesPath>$(MSBuildThisFileDirectory)..\tasks\netcore\bincore\</RoslynAssembliesPath>
<RoslynTasksAssembly>$(MSBuildThisFileDirectory)..\tasks\netcore\Microsoft.Build.Tasks.CodeAnalysis.dll</RoslynTasksAssembly>
<UseSharedCompilation Condition="'$(UseSharedCompilation)' == ''">false</UseSharedCompilation>
```
> `UseSharedCompilation=false` 是 v1 关键：fork 无 VBCSCompiler，进程内编译经 `BuildClient.cs:162-165` 的本地回退路径，避免打包编译服务器。

**A5. 消费效果（目标状态）**

```xml
<PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet" Version="2.0.0-Beta" PrivateAssets="all" />
```
`.vbproj` 构建即用 fork vbc（仅 VB 编译路径，C# 仍走 SDK 自带编译器）；可消费 C#14 扩展成员/SAIM（`proposal-consume-csharp-extension-and-interface-shared.md` 落地后自动生效，因同一份编译器）。

### Part B：`.net tool`（`vbi` 命令，编译 + 执行）

> **命名定案（2026-08-22，用户）**：tool 名 = `vbi`——即**复用现有 `vbi` 二进制**（`Interactive\vbi\vbi.vbproj`，`AssemblyName=vbi`）以 dotnet tool 分发，不新建 `vbx`/`vbc` 名字（`vbx` 与 `.vbx` 扩展名撞车、`vbc` 是编译器驱动名，均不自洽）。「不是 NuGet dotnet tool vbi」的语义：Part A 编译器 NuGet 包**不是** vbi，Part B 的 .net tool **才是** vbi。

**B1. 现有 `vbi.vbproj` 增加 tool 打包（无需新项目）**

- `Interactive\vbi\vbi.vbproj`（net10.0 TFM）加 `PackAsTool=true`、`ToolCommandName=vbi`；net10.0-windows/net48 TFM 维持商店版宿主场景不变（`vbi.vbproj:7` `TargetFrameworks`）。
- **tool 包名 = `Nukepayload2.Compilers.VBScriptDotNet.Cli`**（定案 2026-08-22，加 `.Cli` 后缀与编译器 NuGet 包区分，将来 GUI 界面产品可再分层）。
- 执行核心已就位：`vbi.vbproj:16-18` ProjectReference → `Microsoft.CodeAnalysis.VisualBasic.Scripting`，`Vbi.vb:57` 已调 `VisualBasicScript.RunInteractiveAsync`——tool 直接复用，零新增执行面。

**B2. 模式分发（编译 vs 执行）**

| 触发 | 行为 | 落点 |
|------|------|------|
| 输入含 `.vbx` 文件且无 `/out:`/`/target:` | **执行**脚本：`vbi script.vbx [-- args]` | `VisualBasicScript.RunInteractiveAsync`（`VisualBasicScript.vb:150-170`，即 `Vbi.vb:57` 现有路径） |
| 输入含 `.vb` 或 `/out:`/`/target:` | **编译**到程序集 | `Vbc.Run` 进程内（`Compilers\Shared\Vbc.cs:22-29`），或经 `BuildClient.Run`（`vbc\Program.cs:39`） |
| 无参数 | 交互 REPL（既有能力，顺带保留） | `Vbi.vb:57` |
| `/i` / `/?` / `--version` | 强制交互 / 帮助 / 版本 | `vbi.rsp`（`ReadMe.md:26-30`）、`VersionPrefix`（`Directory.Build.props:6`） |

> 编译模式为**新增**；执行/REPL 为 vbi 既有能力。分发规则可进一步显式化（如 `vbi build`/`vbi run` 子命令），留 Unresolved。

**B3. shebang / rsp / NuGet 引用**

- shebang：`#!/usr/bin/env vbi` 直接可执行——`#!` 已在编译器语法层实现（`proposal-shebang-directive.md` done），`vbi` 作为解释器读取脚本首行，无需宿主剥行。
- 默认 imports/引用：复用 vbi 的 rsp（`vbi.rsp`，默认 `System`、`Microsoft.VisualBasic`、`System.Linq`、`System.Xml.Linq`、`/optioninfer+`，见 `ReadMe.md:26-30`）。
- `#R "nuget: Package, Version"`：复用 Scripting 层已有 `NuGetPackageResolver`（`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs`，`proposal-vbscript-lsp.md:121` 已确认复用点），不重复实现。

**B4. 目标状态**

```bash
$ dotnet tool install Nukepayload2.Compilers.VBScriptDotNet.Cli
$ vbi --version                                  # 2.0.0-Beta
$ vbi src.vb /out:app.dll                        # 批量编译（新增，仅 VB 面，不含 csc）
$ vbi script.vbx -- arg1 arg2                    # 脚本执行（既有）
$ ./script.vbx                                   # shebang 解释器
```

## Drawbacks
[drawbacks]: #drawbacks

- **MSBuild 任务补回 = 新增维护面**：`Microsoft.Build.Tasks.CodeAnalysis` 是 fork 有意裁剪的组件（本提案首次补回），约 20 文件 + MSBuild 依赖，需随上游 merge 维护。
- **v1 仅 .NET SDK**：VS 桌面 MSBuild（net472）项目无法消费 Toolset 包，需等待 net472 编译器构建（fork vbc 目前 net10.0-only）。
- **无编译服务器**：`UseSharedCompilation=false` 牺牲共享编译加速，大解决方案构建变慢（上游 Toolset 默认 true）。
- **分发通道并行**：MSIX（商店 REPL）+ NuGet Toolset + dotnet tool 三通道，产物/版本需统一（见 Unresolved 版本策略）。

## Alternatives
[alternatives]: #alternatives

- **仅库包（当前 IsPackable 三库 `dotnet pack`）**：成本极低，但普通 `.vbproj` 构建不会自动用上 fork 编译器——不解决「让项目构建消费 fork 编译器」的核心诉求（用户已定案选 Toolset）。
- **只发编译器 NuGet 包、不发 tool**：项目构建可用，但脚本/CLI 场景（shebang、CI）仍需商店版，分发面不完整。
- **消费启发式裸包 vbc.exe**（不补 MSBuild 任务）：无法让 `.vbproj` 构建接入，只能手工命令行编译，等于没解决「项目构建」场景。
- **维持现状（仅 MSIX）**：开发者无中转渠道，`proposal-consume-csharp-extension-and-interface-shared.md` 的编译器能力无处可消费。

## Unresolved questions
[unresolved]: #unresolved-questions

> **已闭合（2026-08-22 LDM，`meeting-distribute-compiler-nuget-package-and-dotnet-tool.md`）**：
>
> - **`vbi` 子命令形态**：**隐式分发**——`.vbx` 无 `/out:` 执行、`.vb` 或带 `/out:` 编译、`--` 分隔脚本参数；延申 `CommandLineRunner.cs:118-152`，不引入子命令。
> - **VBCSCompiler**：**先不做**，`UseSharedCompilation=false` 走进程内（`BuildClient.cs:148-165` 回退保证正确性）；不复用 dotnet cli 的 server 做法。
> - **net472**：**允许缺失**——.NET Framework SDK 与 .NET SDK 的 target 基本是两套东西，只是复用材料；新 SDK 只要 VS 里能正常编译即可（SDK 项目走 MSBuild Core，缺 net472 无影响；仅老式非 SDK .NET Framework 项目用不上）。v2 若补：vbc 驱动 + MSBuildTask 双目标（库层 netstandard2.0 已兼容、vbi 已有 net48，`vbi.vbproj:7`）。
> - **`vbi --version`**：**双行版本**——现有 `PrintLogo` 机制（`Vbi.vb:31-50`）自版本落 `2.0.0-Beta` + Roslyn 上游；版权文案（`VBScriptingResources.resx:120-159`）已与「不提及 Microsoft」一致。

仍待定（实现问题，非设计问题）：

1. **`vbi` 编译模式接入 `CommandLineRunner` 的落点**：`RunScriptAsync`（`CommandLineRunner.cs:151`）执行路径 vs `CommonCompiler.Run`（`CommonCompiler.cs:747`）编译路径的分派边界，任务计划阶段细化。
2. **`vbi` tool 打包的 TFM 互斥**：tool 只发 net10.0，net10.0-windows/net48 TFM（商店版宿主场景）与 `PackAsTool` 的关系，实现验证。
3. **`AssemblyInformationalVersion` 产品版本映射**：`Directory.Build.props:6` `VersionPrefix=5.9.0`（Roslyn 上游）如何落 `2.0.0-Beta`，属性策略待定。

## 证据来源与证据等级

- 源码（已检查）：
  - fork 打包现状：`Compilers\Core\Portable\Microsoft.CodeAnalysis.csproj:15-16`（IsPackable + PackageId）、`Compilers\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`Compilers\CSharp\Portable\Microsoft.CodeAnalysis.CSharp.csproj:13`；全树 grep `*.nuspec`/`PackAsTool` 零命中；`NuGet.config:3-21` 无发布源；`eng\` 目录不存在。
  - 裁剪组件：grep `Compilers\**\MSBuildTask\**` 与 `Compilers\Server\**` 零命中；fork `Compilers\Shared\BuildClient.cs:148-165`（服务器失败→本地回退）、`Compilers\Shared\Vbc.cs:22-29`（进程内 Vbc.Run 入口）、`Compilers\VisualBasic\vbc\Program.cs:39`（BuildClient.Run）。
  - 执行核心：`Scripting\VisualBasic\VisualBasicScript.vb:150-170`（RunInteractiveAsync = vbi 入口）、`Interactive\vbi\Vbi.vb:57`；`ReadMe.md:26-30`（vbi.rsp 默认 imports）；`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs`（`#R nuget:` 解析）。
  - 上游参考：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:30-36/51-64`、`...\AnyCpu\build\Microsoft.Net.Compilers.Toolset.props:38-41`、`...\CoreClrCompilerArtifacts.targets:11-18`、`{{Roslyn}}src\Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`、`{{Roslyn}}eng\targets\TargetFrameworks.props:18-19/38/55`（`NetRoslynSourceBuild=net10.0`）；`{{Roslyn}}src\LanguageServer\...\csproj:12-14`（PackAsTool 范式）。
- 已提供（跨提案事实）：`proposal-shebang-directive.md`（`#!` 已实现）、`proposal-vbscript-lsp.md:121`（`#R nuget:` 复用点）、`proposal-consume-csharp-extension-and-interface-shared.md`（编译器能力消费）。
- 预测性判断（待定）：net472 支持、VBCSCompiler 补回、产品版本号映射（Unresolved #3/#4/#5）。
- 用户定案（2026-08-22）：包名 `Nukepayload2.Compilers.VBScriptDotNet`、版本 `2.0.0-Beta`、不含 csc、不替代 dotnet 工具链、专注 VB fork、包描述不提及 Microsoft、tool 名 = `vbi`、tool 包名 = `Nukepayload2.Compilers.VBScriptDotNet.Cli`（`.Cli` 后缀与编译器包区分，为 GUI 界面产品留分层）。
