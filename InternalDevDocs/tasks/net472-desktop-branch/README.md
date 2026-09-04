# 任务：Toolset 编译器包补 net472 桌面分支（net472-desktop-branch）

本文件夹是 `proposal-net472-desktop-branch` 的设计任务存储（Vortex 代办列表 + 设计产物）。能力：给 `Nukepayload2.Compilers.VBScriptDotNet` Toolset 包新增 **net472 桌面分支**（`tasks/net472/`），使 Visual Studio 的 .NET Framework MSBuild（Full host）能用 fork VB 编译器——修正 v1「net472 允许缺失」的未实测定案。**边界（承继 v1 + 本提案定案）**：真 net472 桌面分支（不做 bridge、不引 VBCSCompiler、沿用 `UseSharedCompilation=false`）、VB-only（不铺 C#/csc/VBCSCompiler/bridge）、VB 编译器源码面（`Compilers\VisualBasic\Portable\` .vb）零改动、netcore 路径与产物布局不回归。

- **依据链**：`../../proposals/proposal-net472-desktop-branch.md` → 双老登评审（`<项目根>/tmp/meetings/net472-desktop-branch/{vb-veteran,csharp-veteran}.md`，均附带条件支持）→ 本任务 RESOLUTION（融合裁决，见下）→ 概要/详细设计 + 测试计划。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`）、测试计划（`test-plan.md`）。三份均要求无副作用测试矩阵；net472 端到端（Full host）属集成验证，由用户手动跑（`manual-verification-checklist` 增量）。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## RESOLUTION（双老登评审融合裁决，2026-09-04）

> proposal 冻结（评审期间任何人不得改提案）；本 RESOLUTION 是评审裁决的唯一权威，plan 阶段以它为准确认/修正 proposal 细节。裁决合并 VB/C# 两老登「附带条件支持」的条件。

- **R1（G1 必改，VB+C#）**：`vbc.csproj:17` `<None Include="..\vbc.runtime.rsp" Link="vbc.rsp" CopyToOutputDirectory>` 无 TFM Condition → net472 inner build 会与 G1 源树 netfx `..\vbc.rsp` 拷贝撞同名输出（顺序敏感）。应对该 None 加 `Condition="'$(TargetFramework)'=='net10.0'"`（net10 命令行测试 rsp 反正由 `WriteNet10ResponseFile` 覆盖）。
- **R2（G1 必改，VB）**：`vbc.csproj` 未设 `LangVersion` → net472 TFM 默认 7.3，宿主 C#8+ 源码大面积编不过。加 `<LangVersion>latest</LangVersion>`（对照 `MSBuildTask csproj:10`）。核对 ImplicitUsings 在 net472 下的默认值。
- **R3（顺序门，VB+C#）**：实现顺序锁定为 ① VB Portable `netstandard2.0` 全量重建 + vbi(net48)/vbifw 冒烟（`consume ref readonly` 2026-09-02 后无 ns2.0 重建）→ ② `vbc.csproj` net472 单独 build 清错 → ③ `MSBuildTask.csproj` net472 单独 build 清错（含休眠 `#if NET472`/`NETFRAMEWORK`/`!NET` 条件源码激活）→ ④ 才动 Package.csproj/props。
- **R4（证据勘误，VB+C#）**：`Vbc.cs:388-401` bridge 钩子实指 `Compilers\Core\MSBuildTask\Vbc.cs:388-401`（守卫是运行时属性 `IsSdkFrameworkToCoreBridgeTask`，非 `#if NETFRAMEWORK`）；`Compilers\Shared\Vbc.cs` 仅 32 行无钩子。`NativeMethods.cs` 守卫是 `#if NET`/`#if !NET`（非 `#if NETFRAMEWORK`）。实施者按此找文件，勿按 proposal 原证据节误读。
- **R5（CoreCompile 驱动者定案 = 选项 b，C#；F6 实测确认，2026-09-04）**：props **不设** `VisualBasicCoreTargetsPath`（net472 与 netcore 均保持 v1 现状）；Full host 下 CoreCompile 由 SDK/VS 自带 VB Core targets 链实例化（`VisualBasicCoreTargetsPath` 实测解析到 VS 自带 `Bin\Roslyn\`，非 fork 铺包），Vbc 任务经 `RoslynTasksAssembly` 指向 fork——端到端 Full host 消费 **0 错误**，**无需回退选项 a**。包内 `tasks/net472/Microsoft.*.Core.targets` 铺包 = 布局对齐上游 + 手动覆盖钩子（用户设 `VisualBasicCoreTargetsPath` 时可用），**非活动驱动者**。net10 路径产物与 v1 nupkg 结构逐项 diff 防回归（F6 实测零差异）。
- **R6（C#）**：`RoslynCompilerType=Custom` 保留（.NET 10 SDK 对 `Framework` 编译器类型有弃用警告 `NETSdkWarning`，Custom 是 SDK 10 的非弃用 toolset 通道）；包 README 明示「仅供 VB 项目引用（VB-only 注册，C# 项目引用不可用）」。
- **R7（常设门禁，VB）**：凡改 VB Portable 源（自举编译器），验证循环须含一次 ns2.0 构建 + vbi(net48)/vbifw 冒烟，防桌面分支被无声打断。本任务先执行一次（=R3 步骤 ①）。
- **R8（文档补漏，VB+C#）**：G5 增补 ① 包内 `Installer\Toolset\README.md`（随包文本现称 Core-only / 老式桌面不支持 → 改「net10.0(Core) + net472(桌面 VS/Full MSBuild) 双 host」）；② `manual-verification-checklist.md` 新增 Full host 端到端用例（VS / `MSBuild.exe` 构建引用本包的 SDK 风格 VB 项目、含产 PDB，验证 Vbc 任务自 `tasks/net472` 加载并 spawn `vbc.exe`）；③ 声明最低 VS 2022 17.11（与 MSBuild.Framework 17.11.48 对齐）。
- **R9（C#）**：Package 双 TFM 收集钉 `SetTargetFramework=TargetFramework=net472` + 定向中间目录（镜像 v1 net10 publish 手法），避免多目标外建与路径猜测。
- 承继 proposal 的 open items：vbc net472 `App.config` 镜像（fork 无，上游有）；`RoslynAssembliesPath` net472 指向 `tasks/net472` 属兼容保险（保留即可）；net472 分支 `UseSharedCompilation` 强制 false。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 前置验证：VB Portable `netstandard2.0` 重建 + vbi/vbifw net48 冒烟 | `consume ref readonly`(09-02) 后 ns2.0 可构建；net48 宿主链实跑通过；产物落盘 | **done**（2026-09-04 实施+验证 PASS：ns2.0 0 错误，vbi/vbifw net48 构建通过，net48 CopyLocal 哈希==ns2.0 产物） |
| F2 | `vbc.csproj` 双目标 + net472 build 清错 | `<TargetFrameworks>net10.0;net472</...>`；R1/R2 应用；net472 产物 = vbc.exe(+config)+netfx vbc.rsp；`WriteNet10ResponseFile` 限 net10；net472 ref assemblies 引用 | **done**（2026-09-04 实施+验证 PASS：net472 首编 `--no-incremental` 0 错误，vbc.rsp 与源树 byte-identical 57 行，net10 无回归，App.config 落位） |
| F3 | `MSBuildTask.csproj` 双目标 + net472 build 清错 | `<TargetFrameworks>net10.0;net472</...>`；net472 补 System.Memory/Unsafe；休眠 `#if NETFRAMEWORK` 分支激活编译通过 | **done**（2026-09-04 实施+验证 PASS：net472 全量 0 错误，休眠 #if NETFRAMEWORK 分支激活；`ManagedCompiler.cs:683,786` 为 `#if BOOTSTRAP` 误引已勘误） |
| F4 | 打包项目双 TFM + net472 产物 | `<TargetFrameworks>net472;net10.0</...>`；net472 inner build 普通 Build 收集 + R9 钉 TFM；`_GetFiles` 按 TFM 分支铺 `tasks/net472`；net10 分支与 v1 nupkg 结构 diff 无回归 | **done**（2026-09-04 实施+验证 PASS：三次 pack 全 EXIT=0，net10 子树与 v1 结构 diff 零差异，net472 43 文件；DiaSymReader 缺口转 F4a） |
| F4a | 收口 DiaSymReader.Native（net472 条件引入） | vbc.csproj net472 条件引 `Microsoft.DiaSymReader.Native`（3 arch），重新 pack 后 `tasks/net472` 含 native dll | **done**（2026-09-04 实施+验证 PASS：17.0.0-beta1.21524.1 net472 条件，pack 后三 arch 落位，net10 无泄漏） |
| F5 | props 按 `MSBuildRuntimeType` 分派 | `Core→tasks/netcore(bincore)`（解析值=现状）；`非 Core→tasks/net472`；R5/R6 应用；buildMultiTargeting 转介不变 | **done**（2026-09-04 实施+验证 PASS：Core/Full 双分支实跑解析正确，Core= v1 现状，Full=net472；Full MSBuild 实跑确认） |
| F6 | L2 打包产物验证 | nupkg 同时含 `tasks/netcore`(v1 同构) + `tasks/net472`（vs 上游 DesktopCompilerArtifacts 清单逐项核对，VB-only 裁剪）；`tasks/net472` 含 vbc.exe/netfx rsp/netstandard2.0 库/targets/System.* polyfill；Full host 消费方（R5 L2 门） | **done**（2026-09-04 实施+验证 PASS：net472 46 文件全断言，禁铺项 0 命中；**R5 选项 b 端到端实测成立**——SDK 自带 VB Core targets 驱动，Full host 消费 0 错误，无需回退 a） |
| F7 | 文档登记（G5+R8） | 包内 README 双 host 表述 + VB-only 声明；manual-verification-checklist 增 Full host 冒烟；proposal/tasks 文档状态同步 | **done**（2026-09-04 实施+验证 PASS：包内 README 双 host、本任务 manual checklist（supersede distribute L2-5）、proposal 状态行、ManagedCompiler 勘误全部落位） |
| F8 | 集成验证 + 全量回归 | 七门 gate（`scripts\verify-vb-compiler-tests.ps1`）+ Scripting 程序集 `-automated` 全绿；L2/L3 集成副作用由用户手动跑（Full host 冒烟） | **done**（2026-09-04 实施+验证 PASS：七门 gate 全精确匹配基线，Scripting 213/213，Phase2/CommandLine 抽查实跑；Full host 冒烟清单在 manual-verification-checklist，由用户按 L4 手动跑） |

## 共享源码事实（Vortex agent 以此为基准，行号实现期复核）

- 打包项目：`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj`（`:14` net10 单 TFM、`:33-40` ProjectReference `Targets=Publish`+`SetTargetFramework=net10.0`、`:51-66` `_GetFiles` 铺 `tasks/netcore`+`bincore`、`:45-46` build Content、`:25` `TargetsForTfmSpecificContentInPackage`）。
- props：`Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props`（`:11-12` 写死 `tasks\netcore`、`:10` `RoslynCompilerType=Custom`、`:13` `UseSharedCompilation=false`、`:15` UsingTask Vbc）；`buildMultiTargeting\` 同转介。
- vbc：`Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj`（`:4` net10、`:17` runtime.rsp Link、`:36-52` `WriteNet10ResponseFile`）；netfx rsp 源树 `Compilers\VisualBasic\vbc\vbc.rsp`（57 行，与上游逐字节一致）。
- MSBuildTask：`Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj`（`:7` net10、`:9` 常量、`:10` LangVersion=latest、`:82-83` MSBuild.Framework/Tasks.Core 17.11.48）；bridge 钩子 `Compilers\Core\MSBuildTask\Vbc.cs:388-401`（`IsSdkFrameworkToCoreBridgeTask` 运行时守卫）。
- 编译器库：`Compilers\Core\Portable\Microsoft.CodeAnalysis.csproj:8`、`Compilers\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj:6`（`netstandard2.0;net10.0`）。
- 上游桌面包模型：`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\DesktopCompilerArtifacts.targets:31-55,79-89`、`Framework\Microsoft.Net.Compilers.Toolset.Framework.Package.csproj:37-41`、`Framework\build\Microsoft.Net.Compilers.Toolset.Framework.Core.props:5-17`、`AnyCpu\Microsoft.Net.Compilers.Toolset.Package.csproj:4,26-27,38-43`。
- 休眠 netfx 条件源码：`Compilers\Shared\BuildClient.cs:12`(#if NET472)、`NamedPipeUtil.cs:97`(#if NET472)、`NativeMethods.cs:51,99`(#if NET/!NET)、`Compilers\Core\MSBuildTask\Utilities.cs:168`(#if NETFRAMEWORK)、`ManagedToolTask.cs:34`(#if NETFRAMEWORK && SDK_TASK)。（`ManagedCompiler.cs:683,786` 是 `#if BOOTSTRAP` 区，非 netfx 条件——见 design-detailed G2 注。）
- net48 先例：`Interactive\vbi\vbi.vbproj:7,55-59`（net48 TFM + `Microsoft.NETFramework.ReferenceAssemblies.net48`）；`Installer\vbifw\vbifw.vbproj`。

## F1 验收条件（前置验证 pass 标准）

- `netstandard2.0` target 全量构建通过（Core + VB，Debug/Release 至少一个配置）：09-02 `consume ref readonly` 改动未引入 net10-only BCL 依赖。
- `vbi`(net48) / `vbifw`(net48) 构建 + 冒烟（若可无副作用执行则跑，否则至少构建通过 + 产物落盘）。
- 未通过则收口修复（缺什么补什么，不带遗留）。

## F2 验收条件（vbc 双目标 pass 标准）

- `vbc.csproj` 多目标 `net10.0;net472`；net472 build 0 错误（C#8+ 语法经 LangVersion 放行）。
- R1：`vbc.runtime.rsp` None 限 net10；net472 产物 `vbc.rsp` = 源树 netfx 全引用版（57 行）。
- net472 产物含 `vbc.exe` + `vbc.exe.config`（App.config 镜像）；`WriteNet10ResponseFile` 不再 net472 触发。
- net10 产物/行为与现状 diff 无回归。

## F3 验收条件（MSBuildTask 双目标 pass 标准）

- `Microsoft.Build.Tasks.CodeAnalysis.csproj` 多目标 `net10.0;net472`；net472 build 0 错误（休眠 `#if NETFRAMEWORK`/`!NET` 分支激活编译通过）。
- net472 补 System.Memory/System.Runtime.CompilerServices.Unsafe 条件引用；Microsoft.Build.* 17.11.48 两 TFM 通用。

## F4 验收条件（打包双 TFM pass 标准）

- 打包项目多目标 `net472;net10.0`；net472 inner build 走普通 Build 收集（R9 钉 TFM）；`_GetFiles` 按 `$(TargetFramework)` 分支：net472→`tasks/net472`、net10→`tasks/netcore(+bincore)`（v1 现状）。
- `dotnet pack` 产出 nupkg：net10 目录结构与 v1 nupkg **逐项 diff 一致**（无回归）；`tasks/net472` 内容对上游 DesktopCompilerArtifacts 清单（VB-only 裁剪后）。

## F5 验收条件（props 分派 pass 标准）

- props 引入 `MSBuildRuntimeType` 分派：`Core→netcore`、`Full→net472`；Core host 下解析值与 v1 现状完全一致。
- `RoslynCompilerType=Custom`、`UseSharedCompilation=false`、只注册 Vbc 三处两分支通用；无 `_UseRoslynBridgeTask`。
- R5 执行路径表述按「SDK 自带 VB Core targets 链驱动 CoreCompile、Vbc 经 RoslynTasksAssembly 指向 fork」改述。

## F6 验收条件（L2 产物验证 pass 标准）

- nupkg 解包：`tasks/netcore`（=v1 同构）+ `tasks/net472`（vbc.exe/vbc.rsp/netstandard2.0 编译器库/targets 三件套/System.* polyfill/DiaSymReader.Native）。
- `tasks/net472` 不含 CSharp.dll/csc/VBCSCompiler/bridge。
- R5 L2 门：确定 Full host 实际消费的 targets 链（SDK 自带 vs fork 铺包），记录证据；若 SDK 链不兼容则回退选项 a 并更新 props。

## F7 验收条件（文档登记 pass 标准）

- 包内 `Installer\Toolset\README.md`：双 host 表述 + VB-only 声明 + VS 2022 17.11 下限。
- `manual-verification-checklist.md`：新增 Full host 端到端用例（VS/`MSBuild.exe` + SDK 风格 VB 项目 + PDB）。
- proposal/tasks/meetings 状态与 RESOLUTION 同步。

## F8 验收条件（全量回归 pass 标准）

- 七门 gate（`scripts\verify-vb-compiler-tests.ps1`）全绿 + Scripting `-automated` 全绿。
- Full host 端到端冒烟（用户手动，manual checklist）；netcore 消费路径回归通过。
