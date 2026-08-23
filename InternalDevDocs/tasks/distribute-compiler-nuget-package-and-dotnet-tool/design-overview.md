# 概要设计：分发 fork 编译器（Toolset NuGet 包 + .net tool）

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（Active，RESOLUTION Active）→ `../../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md`（RESOLUTION + 四条 Unresolved 闭合）→ `../../meetings/evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md`。
> 本设计吸收会议 RESOLUTION 与用户定案，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。拿不准的策略对照上游 Roslyn `{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\` 参考实现。

## 1. 背景与目标

**目标（一句话）**：把 fork 编译器（VBScript.NET 的修剪/本地化 Roslyn，vbc 与 vbx 脚本/REPL 的同一份编译器）从「仅微软商店 MSIX 分发 vbi REPL」扩展为两条编译器中转分发渠道：**Part A** Toolset 风格编译器 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta，.vbproj 引用即用 fork VB 编译器）、**Part B** `.net tool`（`Nukepayload2.Compilers.VBScriptDotNet.Cli`，`vbi` 命令：批量编译 + `.vbx` 执行 + shebang 解释器）。

- **现状缺口**：编译器三库已声明可打包（`C\Core\Portable\Microsoft.CodeAnalysis.csproj:15-16` `IsPackable=true` 等）但**无任何打包产物/发布 feed**（无 `.nuspec`、无 `PackAsTool`、`NuGet.config:3-21` 无发布源、`eng\` 不存在）；唯一分发是微软商店 MSIX（`VBInteractive.WindowsDesktop.Installer.wapproj`，`N2ForkVBInteractivePreview` / `1.2.0.0`）。开发者若要在普通 `.vbproj` 里用 fork 编译器，无渠道。
- **状态**：提案与专用 LDM 会议均为 **Active**；四条 Unresolved 全部闭合（U1 `vbi` 隐式分派 / U2 不补 VBCSCompiler / U3 net472 允许缺失 / U4 双行版本）；用户定案（2026-08-22）锁定包名/版本/边界（不含 csc、不替代 dotnet 工具链、专注 VB fork、包描述不提及 Microsoft）。

**用户定案吸收映射（2026-08-22）**：

| 定案 | 内容 | 落在本设计 |
|------|------|-----------|
| 包名 | `Nukepayload2.Compilers.VBScriptDotNet`（编译器包）/ `Nukepayload2.Compilers.VBScriptDotNet.Cli`（tool 包） | 第 2、3 节 |
| 版本 | `2.0.0-Beta`（产品版本；Roslyn 上游 `VersionPrefix=5.9.0`） | 第 2、3 节 |
| 不含 csc | 不注册 `Csc` 任务、bincore 不含 `Microsoft.CodeAnalysis.CSharp.dll` | 第 2 节 |
| 不替代 dotnet 工具链 | 只接管 VB 编译路径，C# 仍走 SDK 自带编译器 | 第 2 节 |
| 专注 VB fork | 只补回 VB 面所需组件（MSBuildTask 只注册 Vbc） | 第 2 节 |
| net472 允许缺失 | v1 只发 netcore（`tasks/netcore` + `bincore`）；net472 缺省 | 第 2 节 |
| 包描述 | 「基于 .NET Foundation 的 Roslyn 编译器改造而来」，不提及 Microsoft | 第 2、4 节 |
| tool 名 | `vbi`（复用现有二进制）；`.Cli` 后缀为 GUI 留分层 | 第 3 节 |
| 模式分发 | 隐式——`.vbx` 无 `/out:` 执行、`.vb` 或带 `/out:` 编译 | 第 3 节 |
| VBCSCompiler | 不做，`UseSharedCompilation=false` 进程内 | 第 2 节 |
| `vbi /version` | 双行（自版本 + Roslyn 上游），用现有 `PrintVersion`（`Vbi.vb:39-42`） | 第 3 节 |

## 2. 总体架构：Part A Toolset 编译器 NuGet 包

**核心观察**：Toolset 包的本质是「让 .NET SDK 的 MSBuild 用包内编译器替代内置 vbc」——通过 `build\` props 里 `UsingTask Vbc` 指向包内任务 DLL、`RoslynAssembliesPath` 指向包内编译器程序集。fork 的编译器三库已 `IsPackable=true`，**缺的是被裁剪的 MSBuildTask 组件与打包项目**。四步落点如下。

### 2.1 补回 MSBuild 任务（`Compilers\Core\MSBuildTask\`，新目录）

- 从上游镜像 `Microsoft.Build.Tasks.CodeAnalysis`（`{{Roslyn}}src\Compilers\Core\MSBuildTask\`）：csproj 参照上游 `MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`（net10.0，`Import Contracts.projitems`）+ `Directory.Build.props:26-54` 源文件清单（`ManagedCompiler.cs`/`ManagedToolTask.cs`/`Vbc.cs`/`Csc.cs`/`CopyRefAssembly.cs`/`MapSourceRoots.cs`/`GenerateMSBuildEditorConfig.cs`/`ErrorString.resx`/`Microsoft.*.targets`）。
- **fork 依赖文件已核实存在**：`C\Shared\ConsoleUtil.cs`、`C\Core\Portable\InternalUtilities\{CommandLineUtilities,CompilerOptionParseUtilities,ReflectionUtilities}.cs`——无需额外移植。
- **只注册 Vbc**：任务 DLL 保留源文件（含 `Csc.cs`），但 `build\` props **只写 `UsingTask Vbc`**，不注册 `Csc`（满足「不含 csc」）。

### 2.2 vbc publish 布局（`vbc.csproj` 增补）

- `vbc.csproj`（net10.0 Exe，`:4-5`）增加 publish 产出：`vbc.dll` + `vbc.deps.json` + `vbc.runtimeconfig.json`（`dotnet publish` 产物；对齐上游 `CoreClrCompilerArtifacts.targets:24-26`）。
- **不带 rsp**：netcore 编译器经 `dotnet exec vbc.dll` 运行，无响应文件需求（上游 `CoreClrCompilerArtifacts.targets` 零 rsp 匹配）；`vbc.rsp` 是 net472 桌面 `vbc.exe` 专属（`DesktopCompilerArtifacts.targets:46-48`，含 netfx `/r:` 全引用），net472 允许缺失故不打包。
- **bincore 只含 VB 面**：`Microsoft.CodeAnalysis.dll` + `Microsoft.CodeAnalysis.VisualBasic.dll` + resources，**不含** `Microsoft.CodeAnalysis.CSharp.dll`（仿上游 `CoreClrCompilerArtifacts.targets:11-18` 布局，但仅取 VB 面——上游 :11-18 实际含 Core+CSharp+VisualBasic 三组，fork 自选缩减为 VB，属用户定案非上游既有子集）。

### 2.3 打包项目（`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`，新项目）

- 仿 `{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj`：`IsPackable=true`、`NuspecPackageId=Nukepayload2.Compilers.VBScriptDotNet`、`IncludeBuildOutput=false`、`DevelopmentDependency=true`、版本 `2.0.0-Beta`。
- `ProjectReference` → `vbc`（`Targets="Publish"`，`ReferenceOutputAssembly=false`）+ `MSBuildTask`。
- `_GetFiles` target 铺产物：`tasks/netcore/`（`Microsoft.Build.Tasks.CodeAnalysis.dll` + targets + deps）+ `tasks/netcore/bincore/`（编译器 DLL + vbc 驱动）。

### 2.4 build props（`build\` + `buildMultiTargeting\`）

- 仿 `Microsoft.Net.Compilers.Toolset.props:9-35`，fork 差异：**只注册 Vbc**、**`UseSharedCompilation=false`**（`BuildClient.cs:148-165` 进程内回退）、`RoslynCompilerType=Custom`、`RoslynAssembliesPath` → `tasks/netcore/bincore/`。
- **net472 缺省**：props 只覆盖 MSBuild Core 路径（`MSBuildRuntimeType=Core` → `tasks/netcore/bincore`）；net472（桌面 MSBuild bridge 壳）不打包——VS 里 SDK 项目走 Core 无影响，仅老式非 SDK 项目用不上（调查结论已记录 meeting）。

### 2.5 消费效果（目标状态）

```xml
<PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet" Version="2.0.0-Beta" PrivateAssets="all" />
```
`.vbproj` 构建即用 fork vbc（仅 VB 编译路径，C# 仍走 SDK 自带编译器）；可消费 C#14 扩展成员/SAIM（`proposal-consume-csharp-extension-and-interface-shared.md` 落地后自动生效，因同一份编译器）。

## 3. 总体架构：Part B `vbi` .net tool

**核心观察**：`vbi` 二进制已存在（`Interactive\vbi\vbi.vbproj`，net10.0 TFM），脚本执行核心已被 vbi 使用（`Vbi.vb:57` → `VisualBasicScript.RunInteractiveAsync`，`VisualBasicScript.vb:150-170`）；`vbi /version` 链路已就位（`Vbi.vb:31-50`）。**缺的是 tool 打包声明 + 编译模式 + 版本号**。

### 3.1 tool 打包（`vbi.vbproj` 增补，无需新项目）

- `Interactive\vbi\vbi.vbproj`（net10.0 TFM）加 `PackAsTool=true` + `ToolCommandName=vbi` + 包名 `Nukepayload2.Compilers.VBScriptDotNet.Cli`；net10.0-windows/net48 TFM 维持商店版宿主场景不变（`:7`）。
- 版本 `2.0.0-Beta`（`AssemblyInformationalVersion` 落产品版本）。

### 3.2 模式分发（编译 vs 执行，新增编译路径）

| 触发 | 行为 | 落点 |
|------|------|------|
| `.vbx` 文件且无 `/out:`/`/target:` | **执行**脚本 | `VisualBasicScript.RunInteractiveAsync`（`VisualBasicScript.vb:150-170`，即 `Vbi.vb:57` 现有路径） |
| `.vb` 或带 `/out:`/`/target:` | **编译**到程序集 | `Vbc.Run` 进程内（`C\Shared\Vbc.cs:22-29`），或经 `BuildClient.Run`（`vbc\Program.cs:39`）；编译+Emit 主流程 `CommonCompiler.Run`（`CommonCompiler.cs:747`） |
| 无参数 | 交互 REPL（既有能力，顺带保留） | `Vbi.vb:57` |
| `/i` / `/?` / `/version` | 强制交互 / 帮助 / 版本 | 现有 `PrintLogo`（`Vbi.vb:31-50`） |

> 编译模式为**新增**；执行/REPL 为 vbi 既有能力。隐式分派延申 `CommandLineRunner.cs:118-152` 的参数判定，不引入子命令。

### 3.3 shebang / rsp / 版本

- shebang：`#!/usr/bin/env vbi` 直接可执行——`#!` 已在编译器语法层实现（`proposal-shebang-directive.md` done），`vbi` 作为解释器读取脚本首行。
- rsp：**`vbi.rsp` 随 tool 打包**——tool 运行依赖它加载默认 imports/references（`Vbi.vb:20` `InteractiveResponseFileName="vbi.rsp"`）；`vbi.coreclr.rsp` 已 Link 为 `vbi.rsp`（`vbi.vbproj:21-24`）。**不是 `vbc.rsp`**（那是 net472 桌面 `vbc.exe` 的 netfx `/r:` 引用文件，与 vbi 工具无关）。
- 版本：`vbi /version` 双行——`LogoLine1` 自版本（`AssemblyInformationalVersion` 落 `2.0.0-Beta`）+ `LogoLine2` Roslyn 上游（`GetRoslynVersion`，`Vbi.vb:48-50`）；Logo 文案已「不提及 Microsoft」（`VBScriptingResources.resx:120-159`）。

### 3.4 目标状态

```bash
$ dotnet tool install Nukepayload2.Compilers.VBScriptDotNet.Cli
$ vbi /version                                   # 2.0.0-Beta / Based on Roslyn 5.9.0
$ vbi src.vb /out:app.dll                        # 批量编译（新增，仅 VB 面）
$ vbi script.vbx -- arg1 arg2                    # 脚本执行（既有）
$ ./script.vbx                                   # shebang 解释器
```

## 4. 行为对照表

| 场景 | 现状 | 提案后 |
|------|------|--------|
| `.vbproj` 引用编译器包 + 构建 VB 面 | 用 SDK 自带 vbc | **用 fork vbc**（`UsingTask Vbc` → 包内任务 → bincore 编译器） |
| 同一解决方案 C# 项目 | 用 SDK 自带 csc | **不变**（不注册 `Csc` 任务，C# 仍走 SDK） |
| `vbi src.vb /out:app.dll` | 无法编译（vbi 只执行/REPL） | **编译到 app.dll**（新增 `Vbc.Run`/`BuildClient.Run` 路径） |
| `vbi script.vbx` | 执行（既有） | **不变**（`VisualBasicScript.RunInteractiveAsync`） |
| `vbi /version` | 双行已实现，版本为 Roslyn 上游 | **自版本 `2.0.0-Beta` + Roslyn 上游**（`AssemblyInformationalVersion` 落产品版本） |
| net472（VS 桌面老式项目） | — | **允许缺失**（v1 只发 netcore；SDK 项目 VS 里可用） |
| 包描述 | — | 「基于 .NET Foundation 的 Roslyn 编译器改造而来」，不提及 Microsoft |

## 5. 代价与边界

- **MSBuildTask 补回 = 新增维护面**：`Microsoft.Build.Tasks.CodeAnalysis` 是 fork 有意裁剪的组件（本任务首次补回），~20 文件 + MSBuild 依赖，需随上游 merge 维护。
- **无编译服务器**：`UseSharedCompilation=false` 牺牲共享编译加速，大解决方案构建变慢（上游 Toolset 默认 true）。
- **net472 缺失**：仅 VS 桌面老式非 SDK .NET Framework 项目用不上 fork 编译器（SDK 项目走 MSBuild Core 无影响）。
- **不含 csc**：混合解决方案 C# 面不覆盖（既定边界，非缺陷）。
- **打包产物验证属集成测试**：`dotnet pack` + 临时项目引用涉及进程/文件，单测部分无副作用，集成验证由用户手动跑或说明边界。

## 6. 零改动清单

- 编译器三库（`IsPackable` 已在位，不新增 pack 逻辑，只补打包项目引用）。
- 脚本执行核心（`VisualBasicScript.RunInteractiveAsync`）——vbi 已用，零改动。
- `PrintLogo`/`GetSelfVersion`/`GetRoslynVersion`（`Vbi.vb:31-50`）——机制已就位，只改 `AssemblyInformationalVersion` 值。
- 现有扩展方法路径、`.vbx`/`.vb` 解析（`VisualBasicCommandLineParser.vb:51-65`）——不改。
