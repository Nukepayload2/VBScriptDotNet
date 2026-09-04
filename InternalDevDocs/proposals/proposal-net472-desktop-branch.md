# 为 Toolset 编译器包补 net472 桌面分支（让 Visual Studio 的 .NET Framework MSBuild 用上 fork 编译器）/ Add a net472 Desktop Branch to the Toolset Compiler Package

> **评审与 RESOLUTION**：双老登（VB/C#）评审意见 + 融合裁决 R1–R9 见 `../tasks/net472-desktop-branch/README.md`。对 `distribute-compiler-nuget-package-and-dotnet-tool` v1 RESOLUTION 中「net472 允许缺失」项的**修正提案**——该定案基于未实测的假设（「VS 里 SDK 项目走 MSBuild Core」），真实 VS build host 是 .NET Framework MSBuild，fork 编译器包必须在 VS 下可用。本次为能力扩展，关联既有 distribute proposal/meeting/task。

* [x] Proposed
* [ ] Prototype: [Not Started](https://github.com/Nukepayload2/VBScriptDotNet/)
* [ ] Implementation: [Not Started](https://github.com/Nukepayload2/VBScriptDotNet/)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`Nukepayload2.Compilers.VBScriptDotNet` Toolset 包目前只有 net10.0 一套产物（`tasks/netcore/` + `tasks/netcore/bincore/`），只能在 **MSBuild Core**（`dotnet build`）下消费。但 **Visual Studio 的 build host 是 .NET Framework MSBuild**（`MSBuild.exe`，`MSBuildRuntimeType='Full'`）——它对 net10 target 的 `Microsoft.Build.Tasks.CodeAnalysis.dll` 做 `LoadFrom` 会因绑定不到 net10 BCL 引用集而失败（典型 `MSB4062`）。v1 排除 net472 的依据（「VS SDK 项目走 MSBuild Core」）是**未经实测的猜测**，本次修正为硬约束：**VS 下编译必须可用 fork 编译器**。

本提案为包新增**真 net472 桌面分支**（对齐上游 `Microsoft.Net.Compilers.Toolset.Framework` 桌面模型 + fork VB-only 裁剪），产物铺 `tasks/net472/`，使 .NET Framework MSBuild（VS / `MSBuild.exe`）经 `UseSharedCompilation=false` 直接 spawn `vbc.exe`(net472) 子进程编译。编译器库复用已有 netstandard2.0 target，不需要改动编译器代码面，只改构建/打包/props。

**策略**：延申既有 `distribute` 的打包机制（`Installer\Toolset\...Package.csproj` 的 publish + `_GetFiles`），把「vbc + MSBuildTask 双目标到 net472 + 桌面产物铺 `tasks/net472` + props 按 `MSBuildRuntimeType` 分派」接进去。不发明新机制，不引入 bridge（`SDK_TASK`）与 VBCSCompiler（沿用 `UseSharedCompilation=false` 进程内/子进程编译）。

## Motivation
[motivation]: #motivation

### v1 定案回顾与根因修正

v1 `distribute-compiler-nuget-package-and-dotnet-tool` 的 RESOLUTION 与设计文档把 net472 标为「**允许缺失**」：
- `tasks\distribute-compiler-nuget-package-and-dotnet-tool\README.md:38`（调查结论）：「VS 里 SDK 项目走 MSBuild Core → 用 bincore，缺 net472 无影响」。
- `design-detailed.md:32,123`、`design-overview.md:55`、`test-plan.md:44`、`manual-verification-checklist.md:143-145,280` 同据此假设。
- proposal `Unresolved`（`proposal-...-distribute...md:147`）记录 net472「允许缺失…仅老式非 SDK .NET Framework 项目用不上」。

该结论依赖的前提——「**VS 内 SDK 项目构建走 MSBuild Core**」——**未经实测**（用户确认：当时没测 VS 兼容性即盲目定论）。事实核查：
- VS 的 build host 是 `MSBuild.exe`，即 **.NET Framework MSBuild**（`MSBuildRuntimeType='Full'`），SDK 风格项目在 VS 里也在 Full runtime 下构建（fork 侧调研，见证据节）。
- fork 包现有任务 DLL 是 **net10 target**（`Installer\Toolset\...Package.csproj:54` 把 net10 的 `Microsoft.Build.Tasks.CodeAnalysis.dll` 铺 `tasks/netcore`）。netfx CLR 无法 `LoadFrom` 一个引用 `System.Runtime, Version=10.0.0.0` 的 net10 程序集 → **VS 里引用本包的 SDK 项目直接构建失败**（`MSB4062`）。

因此「VS 可用」不是可选增强，是**发布硬约束**；v1 定案此条作废。

### 上游执行模型（已核实，见证据节）

现代 Roslyn 的 MSBuild 任务不在 MSBuild.exe 进程内编译 Roslyn——任务把编译请求 **spawn 外部工具进程**（netfx 桌面 = `vbc.exe`(net472)；netcore = `dotnet vbc.dll`），或经命名管道发同目录 `VBCSCompiler`。`UseSharedCompilation=false` 时 **100% 走外部 `vbc.exe` 子进程，完全不需要 VBCSCompiler**。真实编译器引擎是 **netstandard2.0** 的 `Microsoft.CodeAnalysis*.dll`，由 vbc.exe 进程自己加载。

这把「net472 支持」的成本压缩到：**vbc.exe 与 MSBuildTask 双目标到 net472 + 桌面产物铺包 + props 分派**。编译器库 netstandard2.0 target **fork 已产出且已在 netfx 真实运行**（vbi/vbifw net48 宿主链）。

### fork 侧现状（已核实，见证据节）

- 编译器三库已多目标 `netstandard2.0;net10.0`，netstandard2.0 产物真实存在；VB Portable 源码零 TFM 条件编译 → net472 分支库层无阻塞。
- fork 源树已预埋 netfx/桌面条件源码但全部休眠（未启用对应 TFM）：`#if NET472`（`Compilers\Shared\BuildClient.cs`、`NamedPipeUtil.cs`）、`#if NETFRAMEWORK`（`Utilities.cs`、`Vbc.cs:388-401` bridge 钩子、`NativeMethods.cs`）、`#if NET`/`SDK_TASK`（`ManagedToolTask.cs`）。
- netfx 风格 `vbc.rsp` 已在源树（`Compilers\VisualBasic\vbc\vbc.rsp`，内容与上游 net472 rsp 逐字节一致）但**不进任何产物**。
- netcore-only 残留集中四处：`vbc.csproj:4`（net10 TFM）、`Microsoft.Build.Tasks.CodeAnalysis.csproj:7`（net10 TFM）、Package.csproj `_GetFiles`（`tasks/netcore` 写死）、两份 build props（`tasks\netcore` 写死、无 `MSBuildRuntimeType` 分派）。

### 用户定案（技术路线与范围）

- **技术路线：真 net472 桌面分支**（`tasks/net472/` 桌面产物），**不用** bridge（`netcore/binfx` Sdk 壳）、**不用** VBCSCompiler（沿用 `UseSharedCompilation=false`）。
- **范围**：VS 下 **SDK 风格项目**构建可用即为目标。
- **评审**：由 VB/C# 双老登评审本设计；评审意见与 RESOLUTION 见 `../tasks/net472-desktop-branch/README.md`（提案正文为冻结输入，细节以 RESOLUTION 为准）。

## Detailed design
[design]: #detailed-design

### 目标产物结构（对齐上游 `Framework` 桌面包 + fork VB-only 裁剪）

`Nukepayload2.Compilers.VBScriptDotNet` nupkg 新增 `tasks/net472/` 目录；`tasks/netcore/` 与 `tasks/netcore/bincore/`（v1 现状）**保持不动**：

```
tasks/net472/
  Microsoft.Build.Tasks.CodeAnalysis.dll        # net472 任务 DLL（MSBuild.exe 经 UsingTask 加载）
  vbc.exe  vbc.exe.config  vbc.rsp              # net472 桌面 vbc 宿主 + netfx rsp
  Microsoft.CodeAnalysis.dll                     # netstandard2.0（同 vbc.exe 目录被 CLR 解析）
  Microsoft.CodeAnalysis.VisualBasic.dll
  ...*.resources.dll                             # 卫星资源（Core + VB）
  Microsoft.VisualBasic.Core.targets             # CoreCompile 定义（VB-only，不铺 C# 面）
  Microsoft.Managed.Core.targets
  Microsoft.Managed.Core.CurrentVersions.targets
  System.Memory.dll  System.Collections.Immutable.dll        # net472 不自带的 netstandard2.0
  System.Reflection.Metadata.dll  System.Runtime.CompilerServices.Unsafe.dll  polyfill
  System.Threading.Tasks.Extensions.dll  System.Text.Encoding.CodePages.dll
  System.Buffers.dll  System.Numerics.Vectors.dll
  Microsoft.DiaSymReader.Native.x86.dll / amd64.dll          # PDB 生成（VB 项目产 PDB 时需要）
```

> 不铺：`Microsoft.CodeAnalysis.CSharp.dll`、`csc*`、`VBCSCompiler.exe`、`Microsoft.CSharp.Core.targets`、bridge Sdk 壳——VB-only 边界与 v1 一致。

**目录布局依据**（上游）：net472 桌面任务经 `ManagedToolTask.GetToolDirectory()`（`ManagedToolTask.cs:233-235`，netfx 桌面 = 任务 DLL 所在目录自身）在同目录 spawn `vbc.exe`；任务 DLL 与 netstandard2.0 编译器库、System.* polyfill 同目录即被 `LoadFrom`/CLR 解析。参照 `DesktopCompilerArtifacts.targets:31-55,79-81` 与 `Framework` 包 `_GetFilesToPackage`（`Framework.Package.csproj:37-41` 铺 `tasks/net472`）。

### 改动清单

#### G1. `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`：双目标 + netfx rsp

- `<TargetFramework>net10.0</TargetFramework>` → `<TargetFrameworks>net10.0;net472</TargetFrameworks>`。
- **net472 条件产物**（镜像上游 `vbc.csproj:11-15`）：
  - 把源树 `..\vbc.rsp`（netfx 全引用版，fork 已有一份）`Condition="'$(TargetFramework)'=='net472'"` `CopyToOutputDirectory` 进 net472 产物。
  - `App.config`（上游 `src\Compilers\VisualBasic\vbc\App.config` → net472 产出 `vbc.exe.config`）；fork 无则从上游镜像（`eng/targets/GenerateCompilerExecutableBindingRedirects.targets:8-18` 机制 fork 用 reference assemblies 后可选，先镜像静态 App.config）。
- **`WriteNet10ResponseFile` target（`:36-52`）限定 net10**：加 `Condition="'$(TargetFramework)'=='net10.0'"`——它 `AfterTargets=Build` 无条件写 net10 rsp + 复制 net48 引用（fork 为命令行测试造的），net472 build 不应触发。
- **net472 reference assemblies**：fork 无 Arcade，对齐 vbi net48 先例（`vbi.vbproj:56-59` 用 `Microsoft.NETFramework.ReferenceAssemblies.net48`），net472 target 加 `PackageReference Microsoft.NETFramework.ReferenceAssemblies.net472`（`Condition="'$(TargetFramework)'=='net472'"`）——避免依赖机器装 net472 targeting pack。
- ProjectReference 到 `Microsoft.CodeAnalysis.csproj` / `Microsoft.CodeAnalysis.VisualBasic.vbproj` 不变（多目标库，net472 target 自动解析 netstandard2.0 资产）。

#### G2. `Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`：双目标

- `<TargetFramework>net10.0</TargetFramework>` → `<TargetFrameworks>net10.0;net472</TargetFrameworks>`。
- net472 额外依赖（对齐上游 `MSBuildTask\Directory.Build.targets:7-8`）：`Condition="'$(TargetFrameworkIdentifier)' != '.NETCoreApp'"` 时补 `System.Memory` + `System.Runtime.CompilerServices.Unsafe`。
- 保留 `MICROSOFT_CODEANALYSIS_CONTRACTS_NO_VALUE_TASK`（`:9` 两 TFM 通用）；`#if NETFRAMEWORK` 分支（`Utilities.cs` 等）随 net472 自动激活，不改源码。
- net472 reference assemblies：`Microsoft.NETFramework.ReferenceAssemblies.net472`（同上）。
- Microsoft.Build.Framework/Tasks.Core 17.11.48 `ExcludeAssets="Runtime"`（`:82-83`）两 TFM 通用（netfx MSBuild 自带宿主程序集，net472 下 ExcludeAssets 仍成立）。

#### G3. `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`：双目标 + 桌面 `_GetFiles`

- `<TargetFramework>net10.0</TargetFramework>` → `<TargetFrameworks>net472;net10.0</TargetFrameworks>`（对齐上游 AnyCpu `Package.csproj:4`）。
- ProjectReference 分 TFM：
  - **net472 inner build**：ProjectReference → `vbc.csproj`、`MSBuildTask.csproj` **普通 Build 引用**（net472 桌面产物是 build 输出，非 publish），收集其 net472 输出到中间目录；
  - **net10.0 inner build**：维持 v1 的 `Targets="Publish"` + `PublishDir` 方式（`:33-40`）收集 netcore 产物。
  - 对齐上游 `AnyCpu\Package.csproj:38-43` 的 `ProjectReference Update` 条件分派思路，但 fork 需要的是**两种收集方式**（publish vs build），在 `_GetFiles` 里按 `$(TargetFramework)` 分支。
- `_GetFiles`（`:51-66`）加 net472 分支：`Condition="'$(TargetFramework)'=='net472'"` 时把 vbc.exe/vbc.exe.config/vbc.rsp + netstandard2.0 编译器库 + targets 三件套 + System.* polyfill + DiaSymReader.Native 铺 `tasks/net472`；net10 分支保持 v1 现状。
- `Microsoft.Build.Tasks.CodeAnalysis.dll` net472 与 netstandard2.0 `Microsoft.CodeAnalysis*.dll` 的**具体引用路径实现期以「vbc / MSBuildTask net472 build 输出」实测为准**（fork 无 Arcade ArtifactsBinDir，用 ProjectReference `TargetPath`/`OutputPath` 定向收集，参照 v1 `_GetFiles` 的 publish 收集写法）。

#### G4. build props：按 `MSBuildRuntimeType` 分派

`Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props`（当前写死 netcore，`:11-15`）改造为分派：

```xml
<PropertyGroup>
  <_RoslynTargetsDirectoryName Condition="'$(MSBuildRuntimeType)' == 'Core'">netcore</_RoslynTargetsDirectoryName>
  <_RoslynTargetsDirectoryName Condition="'$(MSBuildRuntimeType)' != 'Core'">net472</_RoslynTargetsDirectoryName>
</PropertyGroup>
<!-- RoslynTasksAssembly / RoslynAssembliesPath / UsingTask Vbc 全部指向 tasks\$(_RoslynTargetsDirectoryName)\ -->
```

- **Core（dotnet build）**：`RoslynTasksAssembly = tasks/netcore/Microsoft.Build.Tasks.CodeAnalysis.dll`（net10，v1 现状）；`RoslynAssembliesPath = tasks/netcore/bincore`。
- **Full（VS / netfx MSBuild）**：`RoslynTasksAssembly = tasks/net472/Microsoft.Build.Tasks.CodeAnalysis.dll`；`RoslynAssembliesPath = tasks/net472`。任务 DLL 与桌面 `vbc.exe`、netstandard2.0 库同目录，`ManagedToolTask` spawn `vbc.exe` 子进程（无 bridge、无 `SDK_TASK` 符号）。
- `UseSharedCompilation=false`、`RoslynCompilerType=Custom`、只注册 `Vbc` 三处两分支通用，不引入 `_UseRoslynBridgeTask`（fork 无 bridge 壳）。
- `buildMultiTargeting\` 转介 props 不变（已 `<Import Project="..\build\$(MSBuildThisFile)" />`）。

#### G5. 文档登记

- `InternalDevDocs\tasks\distribute-compiler-nuget-package-and-dotnet-tool\README.md` 及相关 design/test-plan：把「net472 允许缺失」改为「net472 桌面分支已支持（本提案）」，更新 F 表追加本条或加注。**proposal 冻结纪律**：不改 distribute proposal/meeting 正文，本提案独立登记，plan/spec 以本提案 + 未来 RESOLUTION 为准。

### 执行路径（net472，`UseSharedCompilation=false`）

1. netfx MSBuild 导入 props → `UsingTask Vbc → tasks/net472/Microsoft.Build.Tasks.CodeAnalysis.dll`。
2. `Microsoft.VisualBasic.Core.targets` 的 `CoreCompile` 实例化 Vbc 任务并填参（`NoConfig=true`，引用走 `@ReferencePathWithRefAssemblies`，ToolPath 空=用内置工具）。
3. `ManagedCompiler.ExecuteTool` 判定 `!UseSharedCompilation` → `base.ExecuteTool` spawn。
4. `GenerateFullPathToTool`：`tasks/net472/vbc.exe` 存在（apphost）→ 直接 spawn，不包 `dotnet exec`。
5. `vbc.exe`(net472) 进程内 `Vbc.Run` → `responseFile=tasks/net472/vbc.rsp`（NoConfig 跳过），从同目录加载 netstandard2.0 编译器库 + System.* polyfill，编译并回退出码。

### 风险与开放项

- **netstandard2.0 全量重建**：HEAD 的 `consume ref readonly`（2026-09-02）改动 VB Portable 在最后一次 ns2.0 build（08-30）之后，**需做一次 netstandard2.0 构建验证**（G 实现第一步）。
- **vbc App.config**：fork 是否已有 `App.config`；无则从上游镜像（净文件复制，非维护机制）。
- **desktop 任务 `GenerateFullPathToTool` net472 需 System.* 在同目录**：polyfill 列表来自上游 `DesktopCompilerArtifacts.targets:79-81`（csi net472 输出），fork 实际清单实现期按 vbc net472 输出核对（可能含更多/更少）。
- **PDB/DiaSymReader.Native**：VB SDK 项目默认产 PDB → net472 需带 native dll；若 fork 用 core 符号库则核对架构（x86/amd64）。
- **netfx host 具体 VS 版本**：VS2022 netfx MSBuild 17.x；`UseSharedCompilation=false` 规避对 VBCSCompiler 的需求。

## Drawbacks
[drawbacks]: #drawbacks

- **桌面面双目标维护成本**：vbc 与 MSBuildTask 从 net10-only 扩为 `net10.0;net472`，上游 merge 时 net472 条件源码/引用需跟随。
- **System.* polyfill 与 netfx rsp 体积**：`tasks/net472` 文件较多，包体积上升（对齐上游 Framework 包同样取舍）。
- **包结构从单 TFM 变双 TFM**：打包项目与 props 复杂度上升，v1 已验证的 netcore 路径不得回归（需全量测试守护）。

## Alternatives
[alternatives]: #alternatives

- **bridge 转发（`netcore/binfx` Sdk 壳）**：让 netfx host 经 net472 小任务壳转发到 netcore 编译器（上游 .NET10 默认取向）。改动可能更小（不需 net472 vbc.exe/rsp/polyfill），但目标机需可用 dotnet 10 host，且背离用户「真 net472 桌面分支」定案与「VS 不依赖 .NET SDK 运行时」倾向。**用户已否决**。
- **上游式双分支（桌面 + bridge 都要）**：最接近上游但工程最大，bridge 面 fork 已定案 v1 不做。**不在本次范围**。
- **维持 net472 缺失**：VS 下无法编译，违背硬约束。**否决**。

## Unresolved questions
[unresolved]: #unresolved-questions

> **已闭合**：实现阶段全部收口（详见 `../tasks/net472-desktop-branch/`）。实现后无开放设计问题。

1. **vbc net472 的 `App.config`**：实现镜像上游静态 App.config。
2. **desktop 任务对 `RoslynAssembliesPath` 的实际消费**：实现确认其为兼容性遗留属性，props 保留指向 `tasks/net472`。
3. **`UseSharedCompilation` 默认值在 netfx 分支**：沿用 `false`，Full host 实测走命令行工具编译。
4. **实现路径细节**（非设计问题）：`_GetFiles` 双 TFM 收集、net472 目录精确清单、DiaSymReader.Native 三 arch 均按实测收口。

## 证据来源与证据等级

- 上游源码（已检查）：
  - 执行模型：`{{Roslyn}}src\Compilers\Core\MSBuildTask\ManagedCompiler.cs:514-539`（`!UseSharedCompilation` → `base.ExecuteTool` spawn）、`ManagedToolTask.cs:151-165`（`GenerateFullPathToTool`：apphost 存在则直接 `vbc.exe`）、`:227-237`（`GetToolDirectory`：netfx 桌面=任务目录自身 / bridge=`..\bincore` / net=`bincore`）、`:70-82`（`UseAppHost` 探测）、`Shared\BuildClient.cs:148-165`（服务器失败→本地回退）、`:190-194`（`RunLocalCompilation`）。
  - net472 产物清单：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\DesktopCompilerArtifacts.targets:31-55`（netstandard2.0 库 / net472 vbc.exe+config+rsp / net472 任务 DLL / Core.targets）、`:79-89`（System.* polyfill + 卫星资源）、`Framework\Microsoft.Net.Compilers.Toolset.Framework.Package.csproj:37-41`（铺 `tasks/net472`）、`Framework\build\Microsoft.Net.Compilers.Toolset.Framework.Core.props:5-17`（`_RoslynTargetsDirectoryName=net472`、`RoslynTasksAssembly=tasks/net472/...`）。
  - 双目标参照：`{{Roslyn}}src\Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj:6`（`$(NetRoslynSourceBuild);net472`）、`:11-15`（net472 复制 `vbc.rsp`）；`{{Roslyn}}src\Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj:7`（`$(NetRoslynSourceBuild);net472`）、`MSBuildTask\Directory.Build.targets:5-8`（net472 补 System.Memory/Unsafe）。
- fork 源码（已检查）：
  - `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj:14`（net10 单 TFM）、`:33-40`（ProjectReference Publish）、`:51-66`（`_GetFiles` 铺 `tasks/netcore`）。
  - `Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props:11-15`（写死 `tasks\netcore`、无 `MSBuildRuntimeType` 分派）。
  - `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj:4`（net10 单 TFM）、`:36-52`（`WriteNet10ResponseFile` AfterTargets=Build，需 net48 ref，写 net10 rsp）、`:17`（vbc.runtime.rsp Link 不进 publish）。
  - `Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj:7-9`（net10 单 TFM + 注释「net472 留后续」）、`:47`（Csc.cs 编译但 props 不注册）。
  - 编译器库多目标：`Compilers\Core\Portable\Microsoft.CodeAnalysis.csproj:8`、`Compilers\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj:6`（`netstandard2.0;net10.0`）；netstandard2.0 产物存在于 bin/obj（Core/VB Debug+Release）。
  - netfx rsp 已就位：`Compilers\VisualBasic\vbc\vbc.rsp`（netfx 全 `/r:` + imports，与上游逐字节一致）。
  - net48 先例：`Interactive\vbi\vbi.vbproj:7,55-59`（net48 TFM + `Microsoft.NETFramework.ReferenceAssemblies.net48`）；`Installer\vbifw\vbifw.vbproj`（net48 宿主壳）。
  - v1 定案痕迹：`tasks\distribute-compiler-nuget-package-and-dotnet-tool\README.md:38,47,54,66`、`design-detailed.md:10,32,36,52,123-124`、`design-overview.md:23,55,109,116`、`test-plan.md:41,44,100`、`manual-verification-checklist.md:143-145,280`。
- 已运行（netstandard2.0 在 netfx 上真实运行）：`Interactive\vbi\bin\Debug\net48` 与 `Installer\vbifw\bin\Debug\net48` 产物存在（net48 exe 消费编译器 netstandard2.0 资产）。
- 待验证（计划/实现阶段）：net472 桌面 `vbc.exe` 双目标构建、`tasks/net472` 目录精确产物、VS netfx host 端到端消费（L2/L3 集成由用户手动或说明边界）。
- 用户定案（2026-09-03/04）：真 net472 桌面分支（非 bridge）、VS SDK 项目可编译为硬约束、通用 plan 流程 + 双老登评审。
