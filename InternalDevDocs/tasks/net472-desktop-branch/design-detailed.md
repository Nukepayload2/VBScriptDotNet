# 详细设计：Toolset 编译器包补 net472 桌面分支

> 状态：详细设计。依据链：任务 README（RESOLUTION R1–R9）→ `design-overview.md` → 本文。本文逐条给出**改动文件 + 属性/项 + 改动形状**，实施者直接照做。源码事实行号实现期复核（git 漂移时以语义为准）。拿不准对照上游 `{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\` 与 `{{Roslyn}}src\Compilers\Core\MSBuildTask\`。

## 改动总览

| # | 文件 | 改动形状 |
|---|------|---------|
| G1 | `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj` | 双目标 `net10.0;net472`；R1 限 net10；net472 条件产物（rsp + App.config）；LangVersion（R2）；net472 ref assemblies |
| G2 | `Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj` | 双目标 `net10.0;net472`；net472 补 System.Memory/Unsafe；net472 ref assemblies |
| G3 | `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj` | 双目标 `net472;net10.0`；`_GetFiles` 按 TFM 分支（net472 → `tasks/net472` Build 收集；net10 → v1 Publish 收集） |
| G4 | `Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props` | `MSBuildRuntimeType` 分派 Core/Full；R5/R6 |
| G5 | 文档登记（本任务 README/overview + 包内 README + manual checklist） | R8 |

> 编译器三库零改动（net472 消费 ns2.0 资产）。VB 编译器源码（.vb）零改动。

---

## 实现顺序（R3，强制）

1. **F1 前置**：VB Portable `netstandard2.0` 重建 + vbi/vbifw net48 冒烟（`consume ref readonly` 09-02 后无 ns2.0 重建）。命令见 test-plan L1。若 ns2.0 断 → 收口修复，不进入 F2。
2. **F2**：`vbc.csproj` net472 双目标 → 单独 build net472 清错（激活休眠 `#if NET472`）。
3. **F3**：`MSBuildTask.csproj` net472 双目标 → 单独 build net472 清错（激活休眠 `#if NETFRAMEWORK`/`!NET`）。
4. **F4**：打包项目双 TFM + `_GetFiles` 分支 → `dotnet pack`。
5. **F5**：props 分派（等打包产物就位才能对路径）。
6. **F6**：L2 产物验证 → **F7** 文档 → **F8** 全量回归。

---

## G1. `vbc.csproj`（`Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`）

### 双目标
```xml
<!-- :4 --> <TargetFramework>net10.0</TargetFramework>
```
→
```xml
<TargetFrameworks>net10.0;net472</TargetFrameworks>
```

### R1：net10 专用 rsp Link 限 TFM
```xml
<!-- :16-17 -->
<None Include="..\vbc.runtime.rsp" Link="vbc.rsp" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" />
```
→ 加 `Condition="'$(TargetFramework)'=='net10.0'"`（net10 命令行测试 rsp 由 `WriteNet10ResponseFile` 覆盖，net472 不用这份）。

### net472 条件产物（镜像上游 `vbc.csproj:11-15`）
在 ItemGroup 加（net472 时）：
```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net472'">
  <!-- netfx 桌面 vbc.rsp：源树已就位（57 行全 /r: 引用，与上游逐字节一致） -->
  <None Include="..\vbc.rsp">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <!-- vbc.exe.config：fork 无 App.config，从上游 {{Roslyn}}src\Compilers\VisualBasic\vbc\App.config 镜像（supportedRuntime v4.0 sku=.NETFramework,Version=v4.7.2 + gcServer） -->
  <None Include="..\App.config" Link="vbc.exe.config" Condition="Exists('..\App.config')">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```
> 若镜像 App.config：文件落 `Compilers\VisualBasic\vbc\App.config`（从上游复制，含 .NET Foundation license header）。vbc.exe.config 是 net472 产物名（App.config → vbc.exe.config 由 SDK 自动，若 `<None Link>` 手动拷则 Link 名用 `vbc.exe.config`）。

### R2：LangVersion
PropertyGroup 加（net10/net472 通用，net10 现默认 latest 无感，net472 关键）：
```xml
<LangVersion>latest</LangVersion>
```
> 镜像自 MSBuildTask csproj:10 的同名做法。核对 `ImplicitUsings` 默认值在 net472（SDK 默认 net472 下 EnableDefaultImplicitUsings 为 false，宿主源码若不依赖隐式 using 则无碍；若依赖则需显式 `<ImplicitUsings>enable</ImplicitUsings>`——F2 build 时核对报错决定）。

### net472 reference assemblies
```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net472'">
  <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="all" />
</ItemGroup>
```
> 镜像 `vbi.vbproj:55-59` net48 先例；fork 无 Arcade 故走 NuGet reference assemblies，不依赖机器装 targeting pack。若机器只装了 net48 targeting pack，可临时用 net48 验证但发布用 net472（对齐上游）。

### `WriteNet10ResponseFile` target（`:36-52`）
加 `Condition="'$(TargetFramework)'=='net10.0'"`（当前无条件 `AfterTargets="Build"`，net472 inner build 触发会因缺 net48 ref 而 Error）。

---

## G2. MSBuildTask csproj（`Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`）

### 双目标
```xml
<!-- :7 --> <TargetFramework>net10.0</TargetFramework>
```
→
```xml
<TargetFrameworks>net10.0;net472</TargetFrameworks>
```

### net472 条件依赖（镜像上游 `MSBuildTask\Directory.Build.targets:7-8`）
现有 `:81-84` PackageReference 组（Microsoft.Build.Framework / Tasks.Core 17.11.48 `ExcludeAssets=Runtime`）后追加：
```xml
<PackageReference Include="System.Memory" Condition="'$(TargetFrameworkIdentifier)' != '.NETCoreApp'" />
<PackageReference Include="System.Runtime.CompilerServices.Unsafe" Condition="'$(TargetFrameworkIdentifier)' != '.NETCoreApp'" />
```
> 版本取编译器库 ns2.0 已用版本（`Microsoft.CodeAnalysis.csproj:36-40` System.Memory 4.6.3 / Unsafe 6.1.2），net472 分支对齐。

### net472 reference assemblies
```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net472'">
  <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="all" />
</ItemGroup>
```

### 其余不动
`MICROSOFT_CODEANALYSIS_CONTRACTS_NO_VALUE_TASK`（`:9` 两 TFM 通用）、`LangVersion=latest`（`:10` 已有）、`#if NETFRAMEWORK` 休眠分支随 net472 自动激活（`Utilities.cs:168`、`ManagedToolTask.cs:34,230`、`BuildServerConnection.cs:60`）——F3 build 清错收口。

> 注：`ManagedCompiler.cs` 无 `#if NET`/`NETFRAMEWORK`/`NET472` 条件编译；`:683`/`:786` 属 `#if BOOTSTRAP` 区（fork 未定义该符号，net10 与 net472 下均休眠）。net472 激活面不含该文件。

---

## G3. 打包项目（`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`）

### 双目标
```xml
<!-- :14 --> <TargetFramework>net10.0</TargetFramework>
```
→
```xml
<TargetFrameworks>net472;net10.0</TargetFrameworks>
```

### ProjectReference 分 TFM（R9：钉 TFM）
现有 `:33-40` 两 ProjectReference（vbc + MSBuildTask）都 `Targets="Publish"` + `SetTargetFramework=TargetFramework=net10.0` + `PublishDir` 收集。改造：
- **net10 inner build**：保持 v1 的 `Targets=Publish` + `SetTargetFramework=net10.0` + `PublishDir=obj\Release\net10.0\{vbc,msbuildtask}-publish\`（现状不动）。
- **net472 inner build**：改普通 Build 引用（不 Publish），`SetTargetFramework=TargetFramework=net472`，把被引用项目 net472 build 输出定向收集到 `obj\Release\net472\{vbc,msbuildtask}-desktop\`。

实现形状（镜像上游 AnyCpu `:38-43` 的 ProjectReference Update 条件分派思路）：
```xml
<ProjectReference Include="..\..\Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj"
                  PrivateAssets="All" ReferenceOutputAssembly="false"
                  SetTargetFramework="TargetFramework=$(TargetFramework)"
                  AdditionalProperties="..." />
<ProjectReference Include="..\..\Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj"
                  PrivateAssets="All" ReferenceOutputAssembly="false"
                  SetTargetFramework="TargetFramework=$(TargetFramework)"
                  AdditionalProperties="..." />
```
> 收集方式实现期以 `dotnet pack -p:TargetFramework=net472` 试跑定稿：net472 产物收集走 `_GetFiles` 里引用项目 `OutputPath/$(TargetFramework)` 的真实路径（vbc.csproj 多目标后 `bin\$(Config)\net472\`），或额外传 `PublishDir` 到一个目录再 Build。**验收判据**：`tasks/net472` 里拿到 net472 的 vbc.exe / MSBuildTask.dll 与 netstandard2.0 编译器库（非 net10 版）。

### `_GetFiles` 按 TFM 分支
`:51-66` 改：
```xml
<Target Name="_GetFiles" DependsOnTargets="Build">
  <ItemGroup Condition="'$(TargetFramework)' != 'net472'">
    <!-- 现状 net10 分支：msbuildtask-publish → tasks/netcore；vbc-publish → tasks/netcore/bincore（:54-63 原样） -->
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net472'">
    <!-- tasks/net472/（net472 桌面分支，VB-only） -->
    <_File Include="$(net472 任务收集)\Microsoft.Build.Tasks.CodeAnalysis.dll" TargetDir="tasks/net472" />
    <_File Include="$(net472 任务收集)\Microsoft.Managed.Core.targets" TargetDir="tasks/net472" />
    <_File Include="$(net472 任务收集)\Microsoft.Managed.Core.CurrentVersions.targets" TargetDir="tasks/net472" />
    <_File Include="$(net472 任务收集)\Microsoft.VisualBasic.Core.targets" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\vbc.exe" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\vbc.exe.config" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\vbc.rsp" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\Microsoft.CodeAnalysis.dll" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\Microsoft.CodeAnalysis.VisualBasic.dll" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\**\*.resources.dll" TargetDir="tasks/net472" />
    <!-- System.* polyfill / DiaSymReader.Native：随 vbc net472 输出 CopyLocal 落位；精确清单以 F6 产物核对收口 -->
    <_File Include="$(net472 vbc 收集)\System.*.dll" TargetDir="tasks/net472" />
    <_File Include="$(net472 vbc 收集)\Microsoft.DiaSymReader.Native.*.dll" TargetDir="tasks/net472" />
  </ItemGroup>
  <TfmSpecificPackageFile Include="@(_File)" PackagePath="%(_File.TargetDir)/%(_File.RecursiveDir)%(_File.FileName)%(_File.Extension)" />
</Target>
```
> 路径占位符（`$(net472 任务收集)` 等）实现期以 F4 build 实测的中间目录替换。**不含** Microsoft.CodeAnalysis.CSharp.dll / csc / VBCSCompiler / bridge（VB-only）。

---

## G4. build props（`Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props`）

`:9-17` 写死 `tasks\netcore` 改分派：
```xml
<PropertyGroup>
  <_RoslynTargetsDirectoryName Condition="'$(MSBuildRuntimeType)' == 'Core'">netcore</_RoslynTargetsDirectoryName>
  <_RoslynTargetsDirectoryName Condition="'$(MSBuildRuntimeType)' != 'Core'">net472</_RoslynTargetsDirectoryName>
  <RoslynCompilerType>Custom</RoslynCompilerType>
  <RoslynAssembliesPath Condition="'$(MSBuildRuntimeType)' == 'Core'">$(MSBuildThisFileDirectory)..\tasks\netcore\bincore\</RoslynAssembliesPath>
  <RoslynAssembliesPath Condition="'$(MSBuildRuntimeType)' != 'Core'">$(MSBuildThisFileDirectory)..\tasks\net472\</RoslynAssembliesPath>
  <RoslynTasksAssembly>$(MSBuildThisFileDirectory)..\tasks\$(_RoslynTargetsDirectoryName)\Microsoft.Build.Tasks.CodeAnalysis.dll</RoslynTasksAssembly>
  <UseSharedCompilation Condition="'$(UseSharedCompilation)' == ''">false</UseSharedCompilation>
</PropertyGroup>
<UsingTask TaskName="Microsoft.CodeAnalysis.BuildTasks.Vbc" AssemblyFile="$(RoslynTasksAssembly)" />
```
> R5：不设 `VisualBasicCoreTargetsPath`（选项 b）——SDK 自带 VB Core targets 链驱动 CoreCompile，Vbc 经 `RoslynTasksAssembly` 指向 fork；包内 targets 铺包为布局对齐 + 手动覆盖钩子。R6：保留 `RoslynCompilerType=Custom`（规避 .NET 10 SDK 弃用警告）。不加 `_UseRoslynBridgeTask`。
> `buildMultiTargeting\Nukepayload2.Compilers.VBScriptDotNet.props` 不变（已转介）。

---

## G5. 文档登记（R8）

1. 包内 `Installer\Toolset\README.md`：
   - `:52-53`「v1 release that ships the .NET (Core) toolset only. Classic (non-SDK) .NET Framework desktop projects are not supported.」→ 改「双 host：net10.0 (Core, `dotnet build`) + net472 (desktop, VS / .NET Framework MSBuild)。SDK-style 项目在 VS 与 dotnet 下均可消费」。
   - `:20-21` Requirements「.NET SDK targeting net10.0」→ 补 net472 消费方要求（.NET Framework 4.7.2+ / VS 2022 17.11+，对齐 R8）。
   - 增补 VB-only 声明已在 `:46-48`，保持；增补 `UseSharedCompilation=false` 冷启说明在 `:54-55`，保持。
   - `RoslynCompilerType=Custom` 说明可加注释（R6 rationale）。
2. `manual-verification-checklist.md`：新增 **L2-6 Full host 端到端**（VS 或 `MSBuild.exe` 构建引用本包的 SDK 风格 VB 项目、`<DebugType>full</DebugType>` 产 PDB，确认 Vbc 任务自 `tasks/net472` 加载并 spawn `vbc.exe`；记录 MSBuild 版本 ≥ 17.11）。
3. 本任务 README / design 状态表更新为 done（随 Vortex 推进）。

---

## 零改动清单

- 编译器三库 csproj / VB Portable 源码（.vb）/ `vbi` / `vbifw` / `buildMultiTargeting` props / netcore 产物目录与收集方式（net10 inner build）/ props Core 分支解析值。
