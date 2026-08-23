# 详细设计：分发 fork 编译器（Toolset NuGet 包 + .net tool）

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md` → `../../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` → 任务 README「共享源码事实」+ `design-overview.md`。
> 本文逐条给出**改动文件 + 属性/项 + 行号区域 + 改动形状**，实施者直接照做。源码事实以任务 README「共享源码事实」为准；拿不准对照上游 `{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\` 与 `{{Roslyn}}src\Compilers\Core\MSBuildTask\`。

## 改动总览

| # | 文件 | 改动形状 |
|---|------|---------|
| A1 | `Compilers\Core\MSBuildTask\**`（新目录 ~20 文件） | 从上游镜像 MSBuildTask，net10.0-only |
| A2 | `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj` | 加 publish 产出（deps/runtimeconfig/rsp） |
| A3 | `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`（新项目） | 打包项目：`_GetFiles` 铺 `tasks/netcore` + `tasks/netcore/bincore` |
| A4 | `Installer\Toolset\build\Microsoft.Net.Compilers.Toolset.props` + `buildMultiTargeting\`（新文件） | props：只注册 Vbc、`UseSharedCompilation=false`、`RoslynAssembliesPath`→bincore |
| B1 | `Interactive\vbi\vbi.vbproj` | 加 `PackAsTool=true` + `ToolCommandName=vbi` + 包名 `.Cli` |
| B2 | `Interactive\vbi\`（新增编译模式入口） | `vbi` 编译模式：`.vb`/`/out:` → `Vbc.Run`/`BuildClient.Run` |
| B3 | 版本号：`AssemblyInformationalVersion` | 落 `2.0.0-Beta`（产品版本） |
| B4 | `VBInteractive.sln` | 登记新打包项目 + MSBuildTask 项目 |

---

## Part A：Toolset 编译器 NuGet 包

### A1. 补回 MSBuild 任务（`Compilers\Core\MSBuildTask\`）

**上游参考**：`{{Roslyn}}src\Compilers\Core\MSBuildTask\`（`Directory.Build.props:26-54` 源文件清单、`MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`、`Sdk\Microsoft.Build.Tasks.CodeAnalysis.Sdk.csproj`）。

**改动**：
1. 新建目录 `Compilers\Core\MSBuildTask\`，镜像上游源文件（**~20 文件**）：
   - 任务类：`ManagedCompiler.cs`、`ManagedToolTask.cs`、`Vbc.cs`、`Csc.cs`、`CopyRefAssembly.cs`、`MapSourceRoots.cs`、`GenerateMSBuildEditorConfig.cs`、`CanonicalError.cs`、`CommandLineBuilderExtension.cs`、`PropertyDictionary.cs`、`RCWForCurrentContext.cs`、`ShowMessageForImplicitlySkipAnalyzers.cs`、`MvidReader.cs`、`Utilities.cs`、`ValidateBootstrap.cs`、`InteractiveCompiler.cs`、`Csi.cs`、`ICompilerOptionsHostObject.cs`、`IVbcHostObject6.cs`、`IAnalyzerConfigFilesHostObject.cs`、`ICscHostObject5.cs`、`TaskCompilerServerLogger.cs`。
   - 资源：`ErrorString.resx`。
   - targets：`Microsoft.*.targets`（`Microsoft.Managed.Core.targets`、`Microsoft.VisualBasic.Core.targets`、`Microsoft.CSharp.Core.targets`，随任务 DLL 铺包）。
   - **不镜像** `Sdk\Microsoft.Build.Tasks.CodeAnalysis.Sdk.csproj`（net472 bridge 壳）——net472 允许缺失（用户定案），桌面 MSBuild 不在 v1 范围。
2. 新建 `MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`（对齐上游）：
   ```xml
   <TargetFramework>net10.0</TargetFramework>
   <!-- 只 net10.0（对齐 fork 编译器 net10.0-only）；上游 $(NetRoslynSourceBuild);net472 的 net472 留后续 -->
   <!-- 路径 4 层 ..：csproj 在 Compilers\Core\MSBuildTask\MSBuild\（距仓库根 4 层） -->
   <Import Project="..\..\..\..\Dependencies\Contracts\Microsoft.CodeAnalysis.Contracts.projitems" Label="Shared" />
   ```
   - `Directory.Build.props:26-54` 源文件清单（Compile Include `..\..\Shared\ConsoleUtil.cs` + `..\Portable\InternalUtilities\{CommandLineUtilities,CompilerOptionParseUtilities,ReflectionUtilities}.cs` + MSBuildTask 目录文件 + EmbeddedResource `ErrorString.resx` + Content `Microsoft.*.targets`）。
   - PackageReference：`Microsoft.Build.Framework`、`Microsoft.Build.Tasks.Core`（**对齐上游 `MSBuildTask\Directory.Build.targets:5-6`**——上游用 Tasks.Core，非 Utilities.Core）。
   - **依赖文件 fork 已有**（已核实）：`C\Shared\ConsoleUtil.cs`、`C\Core\Portable\InternalUtilities\{CommandLineUtilities,CompilerOptionParseUtilities,ReflectionUtilities}.cs`、`Dependencies\Contracts\Microsoft.CodeAnalysis.Contracts.projitems`。
3. **只注册 Vbc**：任务 DLL 保留 `Csc.cs`/`Vbc.cs` 源文件（同一 DLL 含两类），但打包 props 只写 `UsingTask Vbc`（见 A4），`Csc` 不注册——满足「不含 csc」。
4. `VBInteractive.sln`：登记此项目（Compilers 分组下）。

### A2. vbc publish 布局（`Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`）

**上游参考**：`CoreClrCompilerArtifacts.targets:24-26`（netcore `vbc.dll`/deps/runtimeconfig——**无 rsp**）。

**改动**（在 `vbc.csproj`，net10.0 TFM）：
1. 增加 publish 产出目标（`TargetsForTfmSpecificContentInPackage` 或独立 `_GetFiles` 收集）：`vbc.dll` + `vbc.deps.json` + `vbc.runtimeconfig.json`（`dotnet publish` 产物）。
   - **不带 rsp**：netcore 编译器经 `dotnet exec vbc.dll` 运行，无响应文件需求（上游 `CoreClrCompilerArtifacts.targets` 零 rsp 匹配）；`vbc.rsp` 是 net472 桌面 `vbc.exe` 专属（`DesktopCompilerArtifacts.targets:46-48`，含 netfx `/r:` 全引用），net472 允许缺失故不打包。
2. **bincore 只含 VB 面**：`Microsoft.CodeAnalysis.dll` + `Microsoft.CodeAnalysis.VisualBasic.dll` + resources（**不含** `Microsoft.CodeAnalysis.CSharp.dll`）——对齐「不含 csc」。仿上游 `CoreClrCompilerArtifacts.targets:11-18` 布局，但仅取 VB 面（上游 :11-18 实际含 Core+CSharp+VisualBasic 三组，fork 自选缩减为 VB）。

### A3. 打包项目（`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`，新项目）

**上游参考**：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:30-64`。

**改动**：
```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <NuspecPackageId>Nukepayload2.Compilers.VBScriptDotNet</NuspecPackageId>
  <Version>2.0.0-Beta</Version>
  <IncludeBuildOutput>false</IncludeBuildOutput>
  <DevelopmentDependency>true</DevelopmentDependency>
  <PackageDescription>基于 .NET Foundation 的 Roslyn 编译器改造而来，专注 Visual Basic 的编译器包。</PackageDescription>
  <Authors>Nukepayload2</Authors>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);_GetFiles</TargetsForTfmSpecificContentInPackage>
  <NoWarn>$(NoWarn);NU5100;NU5128</NoWarn>
</PropertyGroup>
<ItemGroup>
  <!-- 两个 ProjectReference 都设 Targets="Publish" + ReferenceOutputAssembly="false"，publish 产物经 PublishDir 落到统一中间目录 -->
  <ProjectReference Include="..\..\Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj"
                    PrivateAssets="All" Targets="Publish" ReferenceOutputAssembly="false"
                    SetTargetFramework="TargetFramework=net10.0"
                    AdditionalProperties="PublishDir=$(IntermediateOutputPath)vbc-publish\;SelfContained=false" />
  <ProjectReference Include="..\..\Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj"
                    PrivateAssets="All" Targets="Publish" ReferenceOutputAssembly="false"
                    SetTargetFramework="TargetFramework=net10.0"
                    AdditionalProperties="PublishDir=$(IntermediateOutputPath)msbuildtask-publish\;SelfContained=false" />
</ItemGroup>
<Target Name="_GetFiles" DependsOnTargets="Build">
  <ItemGroup>
    <!-- tasks/netcore/（来自 MSBuildTask publish 产物） -->
    <_File Include="$(IntermediateOutputPath)msbuildtask-publish\Microsoft.Build.Tasks.CodeAnalysis.dll" TargetDir="tasks/netcore" />
    <_File Include="$(IntermediateOutputPath)msbuildtask-publish\Microsoft.*.targets" TargetDir="tasks/netcore" />
    <!-- tasks/netcore/bincore/（来自 vbc publish 产物） -->
    <_File Include="$(IntermediateOutputPath)vbc-publish\vbc.dll" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\vbc.deps.json" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\vbc.runtimeconfig.json" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\Microsoft.CodeAnalysis.dll" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\Microsoft.CodeAnalysis.VisualBasic.dll" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\**\Microsoft.CodeAnalysis.resources.dll" TargetDir="tasks/netcore/bincore" />
    <_File Include="$(IntermediateOutputPath)vbc-publish\**\Microsoft.CodeAnalysis.VisualBasic.resources.dll" TargetDir="tasks/netcore/bincore" />
    <TfmSpecificPackageFile Include="@(_File)" PackagePath="%(_File.TargetDir)/%(_File.RecursiveDir)%(_File.FileName)%(_File.Extension)" />
  </ItemGroup>
</Target>
```
- **不含**：`Microsoft.CodeAnalysis.CSharp.dll`、`csc`、`VBCSCompiler`、`csi`（对齐用户定案）。
- **版权/描述**：不提及 Microsoft；`Authors=Nukepayload2`。
- `VBInteractive.sln`：登记此项目（Packaging 分组下）。

### A4. build props（`Installer\Toolset\build\` + `buildMultiTargeting\`，新文件）

**上游参考**：`Microsoft.Net.Compilers.Toolset.props:9-41`。fork 差异标注如下。

`build\Microsoft.Net.Compilers.Toolset.props`：
```xml
<Project>
  <PropertyGroup>
    <RoslynCompilerType>Custom</RoslynCompilerType>
    <RoslynAssembliesPath>$(MSBuildThisFileDirectory)..\tasks\netcore\bincore\</RoslynAssembliesPath>
    <RoslynTasksAssembly>$(MSBuildThisFileDirectory)..\tasks\netcore\Microsoft.Build.Tasks.CodeAnalysis.dll</RoslynTasksAssembly>
    <UseSharedCompilation Condition="'$(UseSharedCompilation)' == ''">false</UseSharedCompilation>
  </PropertyGroup>
  <UsingTask TaskName="Microsoft.CodeAnalysis.BuildTasks.Vbc" AssemblyFile="$(RoslynTasksAssembly)" />
  <!-- 只注册 Vbc：不含 csc，不替代 dotnet 工具链 -->
</Project>
```
`buildMultiTargeting\Microsoft.Net.Compilers.Toolset.props`：`<Import Project="..\build\$(MSBuildThisFile)" />`（对齐上游 `buildMultiTargeting\...props:3`）。
- **net472 缺省**：不提供 `tasks/net472`，props 只覆盖 MSBuild Core（`dotnet build`/VS SDK 项目）；桌面 MSBuild 老式项目用不上（用户定案允许缺失）。
- **不含** `_UseRoslynBridgeTask` 逻辑（那是 netfx host 桥，v1 不覆盖）。

### A5. 消费验证（集成，用户手动跑）

```bash
dotnet pack Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj -c Release
# 临时 SDK 项目引用包，dotnet build 确认用 fork vbc（VB 面）、C# 仍走 SDK
```

---

## Part B：`vbi` .net tool

### B1. tool 打包（`Interactive\vbi\vbi.vbproj`）

**上游参考**：`{{Roslyn}}src\LanguageServer\...\Microsoft.CodeAnalysis.LanguageServer.csproj:12-14`（`PackAsTool=true` + `ToolCommandName`）。

**改动**（`vbi.vbproj`，net10.0 TFM 属性组）：
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>vbi</ToolCommandName>
<PackageId>Nukepayload2.Compilers.VBScriptDotNet.Cli</PackageId>
<Version>2.0.0-Beta</Version>
```
- net10.0-windows/net48 TFM 维持商店版宿主场景（`:7`）；tool 只发 net10.0（`dotnet tool` 跨平台）。
- `AssemblyInformationalVersion` 落 `2.0.0-Beta`（B3）。
- **`vbi.rsp` 随 tool 打包**：tool 运行依赖 `vbi.rsp`——`VisualBasicScript.RunInteractiveAsync` 按 `InteractiveResponseFileName="vbi.rsp"`（`Vbi.vb:20`）加载默认 imports/references；`vbi.coreclr.rsp` 已 `Link=vbi.rsp` + `CopyToOutputDirectory`（`vbi.vbproj:21-24`），`PackAsTool` 打包时确保 `vbi.rsp` 进 `tools/`（`PackAsTool` 默认收 `CopyToOutputDirectory` 项，实现期确认）。**不要**用 `vbc.rsp`（那是 net472 桌面 `vbc.exe` 的 netfx `/r:` 引用文件，`{{Roslyn}}src\Compilers\VisualBasic\vbc\vbc.rsp:11-57`，与 vbi 工具无关）。

### B2. 编译模式（新增入口）

**现状**：`Vbi.vb:57` 恒走 `VisualBasicScript.RunInteractiveAsync`（执行/REPL）。编译模式是新增能力。**前置约束**：`BuildClient`/`Vbc` 均为 `internal`，且只被编译进 vbc.exe 程序集（`vbc.csproj:17-26` Compile Include Shared 源文件）——vbi 程序集当前访问不到这两个类型，必须先补 Shared 源文件编译（见下）。

**改动**：
1. **vbi.vbproj 补 Shared 源文件**（对齐 `vbc.csproj:17-26`）：加
   ```
   <Compile Include="..\..\Compilers\Shared\{BuildClient,BuildProtocol,BuildServerConnection,CompilerServerLogger,ConsoleUtil,ExitingTraceListener,NamedPipeUtil,NativeMethods,RuntimeHostInfo,Vbc}.cs" />
   ```
   使 vbi 程序集内可用 `BuildClient.Run`/`Vbc.Run`。注意 `vbi.vbproj:16-18` 已引用 `Microsoft.CodeAnalysis.VisualBasic.Scripting`，这些 Shared 文件是额外编译项（`InternalsVisibleTo` 已含 vbi，`Microsoft.CodeAnalysis.VisualBasic.vbproj:52`）。
2. `Interactive\vbi\Vbi.vb`（或新增 `Vbi.Compile.vb`）在 `OnStartupAsync` 前加模式判定：
   - 参数含 `.vb` 文件或 `/out:`/`/target:` → 走编译路径：`BuildClient.Run(args, RequestLanguage.VisualBasicCompile, Vbc.Run, BuildClient.GetCompileOnServerFunc(logger), logger)`（对齐 `C\VisualBasic\vbc\Program.cs:39`；`Vbc.Run` 进程内入口 `C\Shared\Vbc.cs:22-29`）。
   - 否则维持现有 `VisualBasicScript.RunInteractiveAsync`（执行/REPL）。
   - 隐式分派延申 `CommandLineRunner.cs:118-152` 的判定逻辑，不引入子命令。

### B3. 版本号（`AssemblyInformationalVersion` 落 `2.0.0-Beta`）

**现状**：`PrintLogo` 已就位（`Vbi.vb:31-50`），`GetSelfVersion` 读 `AssemblyInformationalVersionAttribute`（`:39-42`）。

**改动**：
- 产品版本 `2.0.0-Beta` 映射：`Directory.Build.props:6` `VersionPrefix=5.9.0` 是 Roslyn 上游。选项：
  - a) 在 `vbi.vbproj` 覆盖 `<VersionPrefix>2.0.0</VersionPrefix>` + `<VersionSuffix>Beta</VersionSuffix>`（影响 `AssemblyInformationalVersion`）；
  - b) 显式 `<AssemblyInformationalVersion>2.0.0-Beta</AssemblyInformationalVersion>`。
  - **倾向 a**（复用 `VersionPrefix`/`VersionSuffix` 机制，`GetSelfVersion` 读到 `2.0.0-Beta`）；`LogoLine2` 继续显示 Roslyn 上游（`GetRoslynVersion` 读 `Microsoft.CodeAnalysis.VisualBasic.dll` 版本，`:48-50`，不受影响）。
- 版权文案（`VBScriptingResources.resx:120-159`：Nukepayload2's fork / Based on Roslyn / pre-release）已「不提及 Microsoft」，不改。

### B4. 方案登记（`VBInteractive.sln`）

- 无新项目（B1 改现有 `vbi`），但需确认 `PackAsTool` 不影响商店版 wapproj 引用（net10.0-windows/net48 TFM 独立）。

---

## 零改动清单

- 编译器三库（`IsPackable` 已在位）。
- 脚本执行核心（`VisualBasicScript.RunInteractiveAsync`）。
- `PrintLogo`/`GetSelfVersion`/`GetRoslynVersion`（`Vbi.vb:31-50`）——机制已就位，只改版本值。
- 现有扩展方法路径、`.vbx`/`.vb` 解析（`VisualBasicCommandLineParser.vb:51-65`）。

## 实现顺序建议

1. **A1 补回 MSBuildTask** → 2. **A2 vbc publish** → 3. **A3 打包项目** → 4. **A4 props** → 5. **集成验证**（`dotnet pack` + 临时项目）→ 6. **B1/B2/B3 `vbi` tool** → 7. **B4 方案登记 + 全量回归**。

## 风险与待定

- **`vbi` tool 打包 TFM 互斥**：net10.0-windows/net48 TFM 与 `PackAsTool` 的打包行为——tool 只发 net10.0，商店版宿主场景不受影响；实现期验证（meeting TODO 已记录）。
- **`vbi.rsp` 随 tool 打包**：`PackAsTool` 是否自动收 `CopyToOutputDirectory` 项（`vbi.coreclr.rsp` Link 产物）进 `tools/`——实现期确认；若不含，需显式把 `vbi.rsp` 加进 `PackageFiles`/`TfmSpecificPackageFile`。
- **`AssemblyInformationalVersion` 覆盖范围**：选项 a（`VersionPrefix` 覆盖）会否影响 Scripting 库/商店版 vbi 版本——实现期核对。
