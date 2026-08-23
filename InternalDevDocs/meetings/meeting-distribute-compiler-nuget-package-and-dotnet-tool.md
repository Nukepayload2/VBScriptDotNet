# Visual Basic Language Design Meeting
August 22, 2026

本周议题是 `proposal-distribute-compiler-nuget-package-and-dotnet-tool`——把 fork 编译器（VBScript.NET 的修剪/本地化 Roslyn，`Compilers\VisualBasic\Portable\` 是 vbc 与 vbx 脚本/REPL 的同一份编译器）从「仅微软商店 MSIX 分发 vbi REPL」扩展为两条编译器中转分发渠道：Part A Toolset 风格编译器 NuGet 包、Part B `.net tool`（`vbi` 命令）。范围已由用户定案（2026-08-22）：包名 `Nukepayload2.Compilers.VBScriptDotNet`、版本 `2.0.0-Beta`、不含 csc、不替代 dotnet 工具链、专注 VB fork、包描述不提及 Microsoft、tool 名 = `vbi`、tool 包名 = `Nukepayload2.Compilers.VBScriptDotNet.Cli`。策略是从 fork **已有**机制延申（三库已 `IsPackable=true`、`Vbc.Run` 进程内编译入口已存在、脚本执行核心已被 vbi 使用），不发明新机制。

这场讨论的骨架是四条 Unresolved，进 meeting 前我们把每条都翻透了备选方案——这是新定的前置门槛（用户 2026-08-22：未定项必须备选列全、调查彻底才进 meeting）。翻源码时有几个发现值得先说：`CommandLineRunner` 的**参数隐式分发**机制已经完全够 U1 延申用；`CommonCompiler.GetProductVersion` 的版本链路**早已就位**且 Logo 文案与「不提及 Microsoft」要求一致；net472 的成本**只在 vbc 驱动**而不在库层。细节见下文。评审用五维评分与 C# 生态考量另存于 `evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md`，本纪要只记讨论与落定。

## Agenda

* [Proposal: 分发 fork 编译器：Toolset 风格 NuGet 包 + .NET tool](#proposal-分发-fork-编译器toolset-风格-nuget-包--net-tool)

## Proposal: 分发 fork 编译器：Toolset 风格 NuGet 包 + .NET tool

_Related: [`../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`](../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md)；关联 `../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（编译器能力消费，本提案是其分发使能层）、`../proposals/proposal-shebang-directive.md`（`#!` 已实现，`vbi` 可作解释器）、`../proposals/proposal-vbscript-lsp.md`（M4 dotnet tool 打包先例）_

### 场景与缺口

今天的局面：编译器三库早已声明可打包——`Microsoft.CodeAnalysis.csproj:15-16`（`IsPackable=true` + `PackageId=Microsoft.CodeAnalysis.Common`）、`Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`Microsoft.CodeAnalysis.CSharp.csproj:13`——但**没有**任何打包产物/发布 feed：仓库无 `.nuspec`、无 `PackAsTool`、`NuGet.config:3-21` 全为 dnceng 消费源，无发布源，`eng\` 目录不存在（无 CI/发布脚本）。唯一分发是微软商店 MSIX（`VBInteractive.WindowsDesktop.Installer.wapproj`，`N2ForkVBInteractivePreview` / Identity Version `1.2.0.0`）。开发者若要在普通 `.vbproj` 里用 fork 编译器，**无渠道**。

我们认同的缺口陈述是：`proposal-consume-csharp-extension-and-interface-shared.md` 的编译器能力（消费 C#14 扩展成员 / SAIM）只有**能被项目构建消费**才有意义——能力实现得再好，用户拿不到编译器就无从使用。Toolset 包让普通 VB 项目构建用上 fork 编译器；`vbi` tool 让脚本/CI 无需装商店版即可用 `.vbx`。这是编译器能力分发的使能层。

### 翻源码定位：机制核实

提案的 Detailed design 给了源码锚点，我们逐行翻过去核实。先确认「延申现有机制」的地基确实存在。

**打包现状**：三库 `IsPackable=true` 属实（`Microsoft.CodeAnalysis.csproj:15-16`、`Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`Microsoft.CodeAnalysis.CSharp.csproj:13`）。全树 grep `*.nuspec`/`PackAsTool` 零命中，`eng\` 不存在——确认「有包可打、无打包动作」。

**Toolset 需要的两个被裁剪组件**：`Compilers\Core\MSBuildTask\`（MSBuild 任务 DLL）与 `Compilers\Server\VBCSCompiler\`（编译服务器）在 fork 均零命中。上游参考：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:30-36`（ProjectReference csc/vbc/csi/VBCSCompiler/MSBuildTask，`Targets="Publish"`）+ `:51-64`（`_GetFiles` 铺产物）；build props `Microsoft.Net.Compilers.Toolset.props:38-41`（`UsingTask Vbc/Csc`、`RoslynAssembliesPath`、`UseSharedCompilation=true`、`RoslynCompilerType=Custom`）。

**进程内编译回退已内建**：`Compilers\Shared\BuildClient.cs:148-165`——`hasShared` 时先试服务器，`RunServerCompilation` 失败或不可用时回退 `RunLocalCompilation`（`_compileFunc` = `Vbc.Run`，进程内，`BuildClient.cs:190-194`）。这一条是 U2 的定案基础：**无 VBCSCompiler 也能正确编译**。

**执行核心与驱动入口**：`Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`（net10.0 Exe，`AssemblyName=vbc`）→ `Program.cs:39` `BuildClient.Run(args, RequestLanguage.VisualBasicCompile, Vbc.Run, ...)`；`Compilers\Shared\Vbc.cs:22-29` 进程内 `Vbc.Run` 入口。脚本执行：`Scripting\VisualBasic\VisualBasicScript.vb:150-170` `RunInteractiveAsync` 构造 `VisualBasicInteractiveCompiler` + `CommandLineRunner`，vbi 就经它跑（`Interactive\vbi\Vbi.vb:57`）。

全部锚点核实结果（`文件:行号` 均已逐行翻过）汇总如下：

| 断点 | 提案证据 | 复核 |
|------|---------|------|
| 三库 IsPackable 已声明 | `Microsoft.CodeAnalysis.csproj:15-16`、`Microsoft.CodeAnalysis.VisualBasic.vbproj:13`、`Microsoft.CodeAnalysis.CSharp.csproj:13` | ✅ 已核实 |
| 无打包产物/发布 feed | 全树无 `.nuspec`/`PackAsTool`；`NuGet.config:3-21` 无发布源；`eng\` 不存在 | ✅ 已核实 |
| MSBuildTask 被裁剪 | fork `Compilers\Core\MSBuildTask\` 零命中；上游在 `{{Roslyn}}src\Compilers\Core\MSBuildTask\` | ✅ 已核实 |
| VBCSCompiler 被裁剪 | fork `Compilers\Server\` 零命中 | ✅ 已核实 |
| 进程内编译回退 | `Compilers\Shared\BuildClient.cs:148-165`（服务器失败→本地）、`:190-194`（`RunLocalCompilation`） | ✅ 已核实 |
| vbc 驱动 net10.0-only | `vbc.csproj:4` `TargetFramework=net10.0`（上游 `$(NetRoslynSourceBuild);net472` 双目标） | ✅ 已核实 |
| 库层 netstandard2.0 | `Microsoft.CodeAnalysis.csproj:8` 等三库 `netstandard2.0;net10.0` | ✅ 已核实 |
| 版本输出链路已就位 | `Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:31-50`（`PrintLogo`）→ `GetSelfVersion`（`:39-42`，读 `AssemblyInformationalVersion`）+ `GetRoslynVersion`（`:48-50`）；`CommonCompiler.cs:159-170`（`GetProductVersion`） | ✅ 已核实 |
| Logo 文案与「不提及 Microsoft」一致 | `VBScriptingResources.resx:120-159`（`LogoLine1` = "Nukepayload2's fork..."、`LogoLine2` = "Based on Roslyn..."、`LogoLine3` = pre-release） | ✅ 已核实 |
| vbi 已有 net48 | `Interactive\vbi\vbi.vbproj:7` `TargetFrameworks=net10.0-windows;net10.0;net48` | ✅ 已核实 |
| 参数隐式分发先例 | `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:118-128`（`sourceFiles` 空→交互/非空→脚本）、`:144-152`（`InteractiveMode` 切换） | ✅ 已核实 |
| 扩展名分派基建 | `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:51-65`（`RegularFileExtension=".vb"`、`ScriptFileExtension=".vbx"`） | ✅ 已核实 |
| dotnet tool 范式 | `{{Roslyn}}src\LanguageServer\...\Microsoft.CodeAnalysis.LanguageServer.csproj:12-14`（`PackAsTool=true` + `ToolCommandName`） | ✅ 已核实 |

翻源码时我们有一个发现值得单独说。

**发现：`vbi --version` 的完整链路早已就位，且版本与版权措辞都无需从零做。** `VisualBasicInteractiveCompiler.PrintLogo`（`Vbi.vb:31-50`）已经实现：第一行 `GetSelfVersion()` 读 `AssemblyInformationalVersionAttribute`（`:39-42`），第二行 `GetRoslynVersion()` 读 Roslyn 程序集版本（`:48-50`）；资源 `LogoLine1` = "Nukepayload2's fork of Visual Basic Interactive Compiler [Version {0}]"、`LogoLine2` = "Based on Roslyn [Version {0}]"、`LogoLine3` = pre-release 提示（`VBScriptingResources.resx:120-159`）。这**修正了提案 U4 的表述**：版本输出不是「要新增的机制」，而是**把 `InformationalVersion` 落到产品版本 `2.0.0-Beta` 即可**，`LogoLine2` 继续显示 Roslyn 上游版本。且 Logo 文案完全没有提及 Microsoft，与「包描述不提及 Microsoft」要求天然一致。另一个入口 `VisualBasicReplServiceProvider.Logo`（`:31-35`）走 `CommonCompiler.GetProductVersion`（`CommonCompiler.cs:159-170`），同一机制。

### 候选方案与权衡（四条 Unresolved）

**U1：`vbi` 子命令形态。**

**PROPOSAL A — 隐式分发（采用）。** 复用 `CommandLineRunner.cs:118-128` 现有参数判定：`.vbx` 文件无 `/out:`/`/target:` → 执行脚本；`.vb` 或带 `/out:`/`/target:` → 编译到程序集。零新语法面，与 csi/vbi 现行为一致（csi 无子命令，只按文件/参数行为分派）。扩展名分派基建已有（`VisualBasicCommandLineParser.vb:51-65`）。

**PROPOSAL B — 显式子命令（`vbi build`/`vbi run`）。** 语义更显式，但引入新解析层 + 帮助文案，偏离 csi 惯例。**We think** 对一个脚本工具来说，隐式分派已经够直觉——`.vbx` 天然是「执行」意图，`/out:` 天然是「编译」意图。

**PROPOSAL C — 纯隐式 + `--` 全当脚本参数。** 同 A，但 `.vb` 无 `/out:` 时残留歧义（是编译还是执行？）。**否决**——`.vb` 无输出参数应明确走编译，避免歧义。

**权衡**：A 延申现有 `CommandLineRunner` 参数判定（成本低）、无新语法面、`.vbx` 用户无感；`--` 参数分隔对齐 csi 惯例。**落定 A**。

**U2：VBCSCompiler 是否 v2 补回。**

**PROPOSAL A — 先不做（采用，用户定案）。** `BuildClient.cs:148-165` 回退已保证无服务器也能正确编译；`UseSharedCompilation=false`（`Microsoft.Net.Compilers.Toolset.props:33` 属性）即进程内。用户明示：**不打算复用 dotnet cli 的 shared compiler server 做法，先不做**。

**PROPOSAL B — v2 补回。** 镜像上游 `{{Roslyn}}src\Compilers\Server\VBCSCompiler\`（~25 文件 C# Exe，`VBCSCompiler.csproj:6` `$(NetRoslynSourceBuild);net472` 双目标）。收益是共享编译加速、多项目增量快；成本高（补回 `Compilers\Server\` + 打包 + 双目标）。

**权衡**：服务器是**纯性能优化非功能需求**——正确性由进程内回退保证，编译服务器缺失不改变编译结果，只影响大解决方案的构建时长。fork 定位是轻量脚本/编译器 fork，用户明确不复用 dotnet cli 的 server 做法。**落定 A（先不做）**，B 留作后续独立评估。

**U3：net472（VS 桌面 MSBuild）支持排期。**

**PROPOSAL A — 允许缺失，不做 net472（采用，用户定案）。** 库层已兼容（三库 `netstandard2.0` 可被 net472 加载），但 vbc 驱动 `net10.0-only`（`vbc.csproj:4`）——net472 的**成本在 vbc 驱动 + MSBuildTask 双目标**（上游 `DesktopCompilerArtifacts.targets:46-55`：`vbc.exe`/`vbc.rsp` net472 + `Microsoft.Build.Tasks.CodeAnalysis.dll` net472 + targets）。

**net472 缺失的真实影响（调查已记录）**：我们翻 Toolset 的选择逻辑核实，**net472 不是「VS 编译 VB 项目」的必要条件，而是「VS 桌面 MSBuild（net472 运行时）调用 netcore 编译器」的桥**。Toolset 包按 MSBuild 宿主运行时二选一加载（`Microsoft.Net.Compilers.Toolset.props:9-13`）：MSBuild Core（`dotnet build`、VS 的 SDK 项目默认）走 `tasks/netcore/bincore` 直接用 netcore 编译器；桌面 MSBuild（老式 .NET Framework 项目）走 `tasks/net472` 加载 bridge 任务 `Microsoft.Build.Tasks.CodeAnalysis.Sdk.dll`（`Sdk\...csproj:7` net472-only），其 `GetToolDirectory()`（`ManagedToolTask.cs:233-235`）指回 `..\bincore`——**桌面 MSBuild 也调 netcore 编译器，net472 版只是壳**。因此：

| 场景 | MSBuild 宿主 | 需要 net472 | 缺 net472 的影响 |
|------|-------------|------------|------------------|
| `dotnet build`（SDK 项目） | Core | 否 | 无——直接走 bincore |
| VS 里 SDK 风格 VB 项目（.NET 6/8/10+） | Core | 否 | **无——VS 里能用** |
| VS 里老式非 SDK .NET Framework 项目（传统 `.vbproj`） | 桌面 MSBuild（net472） | 是（需 bridge 壳） | 这类项目 VS 里用不上 fork 编译器 |

**PROPOSAL B — v2 加 net472。** `vbc.csproj` + `MSBuildTask.csproj` 加 net472 目标。**我们核实了关键事实**：vbi 现状已有 net48（`Interactive\vbi\vbi.vbproj:7` `TargetFrameworks=net10.0-windows;net10.0;net48`），说明 fork 对 netfx 运行时并不陌生；且库层 netstandard2.0 天然可被 net472 加载——**进 netfx 只需编译器层（vbc 驱动）补 net472 目标**，库层无需额外改动。成本比直觉低。

**PROPOSAL C — 永远不做。** 明确拒绝桌面 MSBuild。**We think** 现在不急于承诺——`proposal-shebang-directive.md` 已指向 Linux 场景，跨平台优先；但 net48 的既有存在说明桌面场景不是要放弃的市场，v2 按需补即可。

**权衡与落定**：**允许缺失（用户 2026-08-22）**——.NET Framework SDK 与 .NET SDK 的 target 基本是两套东西，只是复用了很多材料；新 SDK 只要 VS 里能正常编译即可，老式非 SDK 项目不在 v1 目标。**落定 A（允许缺失，不做 net472）**；v2 若补，只需 vbc 驱动 + MSBuildTask 双目标（库层 netstandard2.0 已兼容、vbi net48 先例已证明 netfx 面可行）。

**U4：`vbi --version` 版本来源。**

**PROPOSAL A — 双行版本（采用，机制已就位）。** 现有 `PrintLogo` 已天然双行：`LogoLine1` = 自版本（`GetSelfVersion` 读 `AssemblyInformationalVersion`，`Vbi.vb:39-42`）+ `LogoLine2` = Roslyn 上游版本（`GetRoslynVersion`，`:48-50`）。把 `InformationalVersion` 落到产品版本 `2.0.0-Beta` 即可，`LogoLine2` 继续显示 Roslyn 上游（`5.9.0`）。与「Based on Roslyn [Version {0}]」文案天然自洽（`VBScriptingResources.resx:124`）。

**PROPOSAL B — 单版本。** `--version` 只输出 `2.0.0-Beta`，隐藏 Roslyn 上游。**We think** 丢了上游溯源，且要改 `PrintLogo` 逻辑——现有双行机制完全够用，没必要。

**PROPOSAL C — 沿用 Roslyn 版本。** `--version` 仍 `5.9.0`。无法体现 fork 版本，用户已定版本 `2.0.0-Beta`，不成立。

**权衡**：A 零新机制——现有 `PrintLogo`（`Vbi.vb:31-50`）就是为双行设计的，只需设置 `AssemblyInformationalVersion`；且版权文案（`LogoLine1/2/3`）已与「不提及 Microsoft」一致。**落定 A**。

### VB 基因对照

本提案**没有新增任何语法表面积**——它是分发/工具链投资，不触碰语言语义。与「不引入第二种做事方式」原则相容：两条渠道都从 fork 已有机制延申（三库 `IsPackable`、`Vbc.Run` 进程内入口、`CommandLineRunner` 隐式分派、`PrintLogo` 版本链路），不发明新机制。它不替代 dotnet 工具链（C# 仍走 SDK 自带编译器），只让 VB 编译路径可被项目构建/脚本消费——补充而非替换，符合「We will almost never make breaking changes to Visual Basic」的保守基因。与 shebang（已实现）衔接：`vbi` 作解释器（`#!/usr/bin/env vbi`），Linux 可执行脚本链路闭环。

### RESOLUTION:

1. **方向定案（Part A + B）**：fork 编译器分发为 Toolset 风格 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta）+ `.net tool`（`Nukepayload2.Compilers.VBScriptDotNet.Cli`，`vbi` 命令）。两条渠道都延申 fork 已有机制。
2. **边界定案（用户 2026-08-22）**：不含 csc、不替代 dotnet 工具链、专注 VB fork、包描述「基于 .NET Foundation 的 Roslyn 编译器改造而来」不提及 Microsoft、tool 名 = `vbi`。
3. **U1 落定（隐式分发）**：`vbi` 按参数隐式分派——`.vbx` 无 `/out:` 执行、`.vb` 或带 `/out:` 编译、`--` 分隔脚本参数；延申 `CommandLineRunner.cs:118-152`，不引入子命令。
4. **U2 落定（先不做）**：不补 VBCSCompiler，`UseSharedCompilation=false` 走进程内（`BuildClient.cs:148-165` 回退保证正确性）；不复用 dotnet cli 的 server 做法。
5. **U3 落定（允许缺失 net472）**：net472 不做（用户定案）——.NET Framework SDK 与 .NET SDK 的 target 基本是两套东西，只是复用材料；新 SDK 只要 VS 里能正常编译即可。影响面已调查：VS 里 SDK 风格项目走 MSBuild Core（`Microsoft.Net.Compilers.Toolset.props:9-13`），缺 net472 无影响；仅老式非 SDK .NET Framework 项目（需 bridge 壳 `ManagedToolTask.cs:233-235`）用不上。v2 若补：vbc 驱动 + MSBuildTask 双目标。
6. **U4 落定（双行版本）**：`vbi --version` 用现有 `PrintLogo` 双行机制——`LogoLine1` 自版本（`AssemblyInformationalVersion` 落 `2.0.0-Beta`）+ `LogoLine2` Roslyn 上游；版权文案与「不提及 Microsoft」一致。

### Implication

- proposal 更新：Unresolved 四条全部闭合（U1 隐式、U2 先不做、U3 允许缺失 net472、U4 双行版本）；新增「边界定案」段（不含 csc/不替代工具链/包描述措辞）。
- Toolset 包落地：补回 `Compilers\Core\MSBuildTask\`（只注册 Vbc）、`vbc.csproj` 加 publish 产出、新建 `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`、`UseSharedCompilation=false` props。
- `vbi` tool 落地：`Interactive\vbi\vbi.vbproj` 加 `PackAsTool=true` + `ToolCommandName=vbi`；编译模式新增走 `Vbc.Run`/`BuildClient.Run`（`Compilers\Shared\Vbc.cs:22-29`、`vbc\Program.cs:39`）；`InformationalVersion` 落 `2.0.0-Beta`。
- 补测试（无副作用）：Toolset 包引用后 .vbproj 构建用 fork vbc；`vbi src.vb /out:app.dll` 编译；`vbi script.vbx` 执行；`vbi --version` 双行输出。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`（`Suspect`）：`vbi` 编译模式接入 `CommandLineRunner` 的具体落点——`RunScriptAsync`（`CommandLineRunner.cs:151`）执行路径 vs `CommonCompiler.Run`（`CommonCompiler.cs:747`）编译路径的分派边界，任务计划阶段细化。
- `TODO`：`vbi` tool 打包时 net10.0-windows/net48 TFM 与 `PackAsTool` 的互斥关系——tool 只发 net10.0，商店版宿主场景（net48/net10.0-windows）不受影响，需实现验证。
- `TODO`：`AssemblyInformationalVersion` 的产品版本号映射——`Directory.Build.props:6` `VersionPrefix=5.9.0`（Roslyn 上游）如何落 `2.0.0-Beta`，需定属性策略。
- `Follow-up`：net472 允许缺失（用户定案），v2 若补走 vbc 驱动 + MSBuildTask 双目标；VBCSCompiler（后续独立评估）不进 v1。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——分发使能层，纯工具链投资、零语言语义变化、无 breaking change；源码锚点全部复核、四条 Unresolved 备选列全并调查彻底（各方案证据锚定 `文件:行号`）；机制延申现有（`IsPackable`、`Vbc.Run` 进程内、`CommandLineRunner` 隐式分派、`PrintLogo` 版本链路）。提案归 active 根目录。

---

_五维评分、返工建议与 C# 生态/互操作考量见 [evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md](evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md)。_
