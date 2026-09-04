# 概要设计：Toolset 编译器包补 net472 桌面分支

> 状态：概要设计。依据链：`../../proposals/proposal-net472-desktop-branch.md` → 任务 README RESOLUTION（双老登评审融合裁决 R1–R9）→ 本设计。
> 本设计为详细设计提供落点与边界；不涉及实现代码细节。源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。拿不准的策略对照上游 Roslyn `{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\` 参考实现。

## 1. 背景与目标

**目标（一句话）**：给 `Nukepayload2.Compilers.VBScriptDotNet` Toolset 编译器包补**真 net472 桌面分支**（`tasks/net472/`），使 Visual Studio 的 .NET Framework MSBuild（`MSBuild.exe`，Full host）能加载 fork VB 编译器构建 SDK 风格 .vbproj——修正 v1 基于未实测假设（「VS SDK 项目走 MSBuild Core」）的「net472 允许缺失」定案。

- **现状缺口**：fork 包只发 net10.0 一套产物（`tasks/netcore/` + `bincore/`），只能在 `dotnet build`（Core MSBuild）下消费；VS build host 是 Full CLR 的 MSBuild.exe，对 net10 target 任务 DLL `LoadFrom` 绑不到 net10 BCL 引用集 → `MSB4062` 构建失败。v1 定案「VS 里 SDK 项目走 MSBuild Core」被推翻（用户确认当时未实测 VS 兼容性）。
- **状态**：proposal Proposed；双老登评审附带条件支持（条件 = RESOLUTION R1–R9，已在任务 README 逐条固化为验收门）。

## 2. 总体架构

**核心观察**：上游 Roslyn 的 MSBuild 任务**不在 MSBuild.exe 进程内编译 Roslyn**——netfx 桌面下 Vbc 任务 spawn `tasks/net472/vbc.exe`(net472) 子进程，`UseSharedCompilation=false` 时 100% 子进程、不需要 VBCSCompiler；真实编译器引擎是 **netstandard2.0** 编译器库，由 vbc.exe 进程自己加载。这把 net472 支持成本压缩到「vbc + MSBuildTask 双目标 net472 + 桌面产物铺 `tasks/net472` + props 按 `MSBuildRuntimeType` 分派」，编译器源码零改动。

### 2.1 产物结构（新增 `tasks/net472/`，netcore 不回归）

```
tasks/net472/
  Microsoft.Build.Tasks.CodeAnalysis.dll   # net472 任务 DLL（MSBuild.exe 经 UsingTask 加载）
  vbc.exe  vbc.exe.config  vbc.rsp         # net472 桌面 vbc 宿主 + netfx 全引用 rsp
  Microsoft.CodeAnalysis.dll                # netstandard2.0（Core）
  Microsoft.CodeAnalysis.VisualBasic.dll    # netstandard2.0（VB）
  ...*.resources.dll                        # Core + VB 卫星资源
  Microsoft.VisualBasic.Core.targets        # VB-only targets（布局对齐；R5 定案非活动驱动者）
  Microsoft.Managed.Core.targets
  Microsoft.Managed.Core.CurrentVersions.targets
  System.Memory.dll System.Collections.Immutable.dll ...   # net472 不自带的 ns2.0 polyfill
  Microsoft.DiaSymReader.Native.x86.dll / amd64.dll        # PDB 生成（全 PDB 场景）
```
不铺：CSharp.dll / csc* / VBCSCompiler / bridge Sdk 壳（VB-only 边界承继 v1）。

### 2.2 双目标

- `Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`：`net10.0` → `net10.0;net472`。net472 条件复制源树 netfx `..\vbc.rsp` + 镜像 `App.config`（→ `vbc.exe.config`）。R1（rsp 冲突）/ R2（LangVersion）应用。
- `Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`：`net10.0` → `net10.0;net472`。net472 条件补 System.Memory/Unsafe。
- 编译器库**不改**（多目标 `netstandard2.0;net10.0` 已满足 net472 消费 ns2.0 资产）。

### 2.3 打包与 props

- `Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`：`net10.0` → `net472;net10.0` 双 inner build；`_GetFiles` 按 `$(TargetFramework)` 分支：net472 → 普通 Build 收集铺 `tasks/net472`（R9 钉 TFM）、net10 → v1 现状 Publish 收集铺 `tasks/netcore(+bincore)`。
- `build\Nukepayload2.Compilers.VBScriptDotNet.props`：引入 `MSBuildRuntimeType` 分派（Core → `tasks/netcore(bincore)`，解析值=现状；Full → `tasks/net472`）；`RoslynCompilerType=Custom` / `UseSharedCompilation=false` / 只注册 Vbc 两分支通用（R6）。不设 `VisualBasicCoreTargetsPath`（R5，选项 b）。

### 2.4 消费效果（目标状态）

```xml
<PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet" Version="..." PrivateAssets="all" />
```
- `dotnet build`（Core）：netcore 路径，v1 同构。
- VS / `MSBuild.exe`（Full）：Vbc 任务从 `tasks/net472/Microsoft.Build.Tasks.CodeAnalysis.dll` 加载 → spawn `tasks/net472/vbc.exe` → net472 CLR 加载 netstandard2.0 编译器库编译。

## 3. 行为对照表

| 场景 | 现状（v1） | 提案后 |
|------|-----------|--------|
| `dotnet build` SDK 风格 .vbproj 引用包 | fork vbc（tasks/netcore/bincore） | **不变**（Core 分派解析值一致） |
| VS / `MSBuild.exe`（Full host）构建同一项目 | **MSB4062 失败**（net10 任务 DLL 无法加载） | **fork vbc**（tasks/net472 桌面 vbc.exe） |
| 同一解决方案 C# 项目 | SDK 自带 csc（不注册 Csc） | **不变** |
| VB 编译器源码面（`Compilers\VisualBasic\Portable\`） | 零改动 | **零改动**（复用 ns2.0 资产） |

## 4. 代价与边界

- **双目标维护成本**：vbc/MSBuildTask 从 net10-only 扩为双目标；休眠 netfx 条件源码首次激活（编译期风险，R3 顺序门解锁）；随上游 merge 需跟随 net472 条件。
- **包结构复杂度**：打包项目单→双 TFM、props 引入 host 分派；netcore 路径以「nupkg 结构与 v1 逐项 diff」守护回归。
- **无编译服务器**：`UseSharedCompilation=false` 下 Full host 每次编译冷启 vbc.exe（可接受取舍，上游 Toolset 同支持）。
- **VB-only**：包仅供 VB 项目引用（C# 项目引用因不注册 Csc 而不可用）——R6 文档声明。
- **Full host 端到端**：只能在 VS/`MSBuild.exe` 下验证（L2/L3 集成，用户手动；manual checklist 增量 R8）。

## 5. 零改动清单

- 编译器三库（`Compilers\Core\Portable\Microsoft.CodeAnalysis.csproj` / `Compilers\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj`）——不改，ns2.0 target 已具备。
- VB 编译器源码（`Compilers\VisualBasic\Portable\*.vb`）——零改动。
- netcore 产物目录与 props Core 分支解析——v1 现状保持。
- `Interactive\vbi` / `Installer\vbifw` net48 宿主——不改（仅作 R7 常设冒烟对象）。
