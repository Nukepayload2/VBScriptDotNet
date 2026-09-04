# 测试计划：Toolset 编译器包补 net472 桌面分支

> 状态：测试计划。依据链：任务 README（RESOLUTION）→ `design-overview.md` / `design-detailed.md`。四层矩阵：**L1 编译器/库层**（无副作用单测/构建验证）/ **L2 打包产物验证**（`dotnet pack` + nupkg 结构核对）/ **L3 集成消费验证**（Full host / netcore 消费，进程级）/ **L4 手动冒烟清单**（VS `MSBuild.exe`，用户手动）。无副作用纪律：L1 单测禁网络/写文件/启进程/写注册表；L2/L3 涉及 `dotnet pack`/构建属集成，由 Vortex 验证者跑或用户手动（test-plan 标注边界）。

## 分层矩阵

| 层 | 覆盖 | 无副作用 | 执行者 |
|----|------|---------|--------|
| L1 | VB Portable `netstandard2.0` 可构建（F1）；vbc/MSBuildTask net472 编译产物断言（F2/F3） | ✔（构建即验证，进程为本仓库自身 msbuild/dotnet，构建写 obj 属正常编译非测试副作用） | 验证者 agent |
| L2 | nupkg 结构：`tasks/netcore`(v1 同构) + `tasks/net472`（F4/F6） | 需 `dotnet pack`（写文件）→ 集成 | 验证者 agent 或用户 |
| L3 | netcore 消费路径回归（引用包 `dotnet build` 临时 .vbproj）；Full host 消费（`MSBuild.exe`） | 需构建进程 → 集成 | 用户手动（checklist） |
| L4 | VS 内 SDK 风格 VB 项目（含 PDB）用 fork vbc（R8 冒烟） | 需 VS/`MSBuild.exe` | 用户手动（checklist 增量） |

## 用例清单

### L1（F1/F2/F3 前置门）

- **L1-1 VB Portable netstandard2.0 可构建**（R7 门，F1 第一步）：`dotnet build Compilers\VisualBasic\Portable\Microsoft.CodeAnalysis.VisualBasic.vbproj -f netstandard2.0`（及 Core `Microsoft.CodeAnalysis.csproj -f netstandard2.0`）0 错误。09-02 `consume ref readonly` 后首次 ns2.0 重建。
- **L1-2 vbi/vbifw net48 冒烟**：`dotnet build Interactive\vbi\vbi.vbproj -f net48` + `Installer\vbifw\vbifw.vbproj`；产物落盘（vbi.exe + netstandard2.0 编译器库）；若可无副作用则跑最小脚本，否则构建通过即可。
- **L1-3 vbc net472 编译产物断言**（F2）：`dotnet build Compilers\VisualBasic\vbc\AnyCpu\vbc.csproj -f net472`；断言输出目录含 `vbc.exe`（net472）、`vbc.exe.config`、`vbc.rsp`（源树 netfx 全引用版 57 行，非 net10 `/sdkpath` 版）、netstandard2.0 编译器库 CopyLocal；不含 net10 `vbc.runtime.rsp` 内容。休眠 `#if NET472`（BuildClient.cs/NamedPipeUtil.cs/NativeMethods.cs !NET）编译通过。
- **L1-4 MSBuildTask net472 编译产物断言**（F3）：`dotnet build Compilers\Core\MSBuildTask\MSBuild\Microsoft.Build.Tasks.CodeAnalysis.csproj -f net472`；休眠 `#if NETFRAMEWORK`（Utilities.cs/ManagedToolTask.cs/BuildServerConnection.cs）编译通过；System.Memory/Unsafe 引用正确。（`ManagedCompiler.cs` 无 netfx 条件，仅 `#if BOOTSTRAP` 区——见 design-detailed G2 注。）

### L2（F4/F6 打包产物验证）

- **L2-1 net10 产物无回归**：`dotnet pack Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj -c Release -p:TargetFramework=net10.0`；解包 nupkg 的 `tasks/netcore` + `tasks/netcore/bincore` 与 v1 nupkg **逐项 diff 一致**（目录树 + 文件名 + deps/rsp 存在性）。**验收判据**：无多余/缺失文件。
- **L2-2 net472 产物内容**：`dotnet pack ... -p:TargetFramework=net472`；解包 `tasks/net472` 断言含：`vbc.exe`/`vbc.exe.config`/`vbc.rsp`(netfx 57 行)、`Microsoft.Build.Tasks.CodeAnalysis.dll`(net472)、`Microsoft.CodeAnalysis.dll`+`VisualBasic.dll`+resources(netstandard2.0)、targets 三件套、System.* polyfill、DiaSymReader.Native（全 PDB 场景）。**不含** CSharp.dll / csc / VBCSCompiler / bridge。与上游 `DesktopCompilerArtifacts.targets:31-55,79-89` 清单（VB-only 裁剪后）逐项核对。
- **L2-3 props 分派解析**（F5）：Core host 下 `_RoslynTargetsDirectoryName=netcore`（`RoslynAssembliesPath=...bincore\`，= v1 现状）；Full host 下 `=net472`。可在临时项目里 `dotnet msbuild -pp:` 或直接核对 props 文本。
- **L2-4 R5 targets 消费门**：确定 Full host 实际消费哪份 VB Core targets（SDK 自带 vs fork 铺包）。方法：`MSBuild.exe`（Full）下对引用包临时项目 `/v:diag` 查 `VisualBasicCoreTargetsPath` 解析值 + `UsingTask` 落点。若 SDK 自带 targets 链正确驱动 → R5 选项 b 成立；若 SDK 链与 fork 不兼容（要求更前缀属性/路径）→ 回退选项 a（G4 Full 分支设 `VisualBasicCoreTargetsPath=..\tasks\net472\Microsoft.VisualBasic.Core.targets`）并更新 props/design。

### L3（集成消费，用户手动或说明边界）

- **L3-1 netcore 消费回归**：临时 SDK 风格 .vbproj 引用本地 nupkg，`dotnet build` 确认 VB 面用 fork vbc（netcore 路径未回归）。
- **L3-2 Full host 消费**：`MSBuild.exe`（非 dotnet）构建同一临时项目，确认 Vbc 任务自 `tasks/net472` 加载、spawn `vbc.exe` 编译成功（R5/R8 冒烟）。

### L4（manual checklist 增量，用户手动）

- **L4-1 VS 内构建**：VS 打开引用包的 SDK 风格 VB 项目（`<DebugType>full</DebugType>` 产 PDB），Build 成功，输出日志显示 Vbc 任务 AssemblyFile=`tasks/net472/Microsoft.Build.Tasks.CodeAnalysis.dll`。记录 MSBuild/VS 版本 ≥ 17.11。
- **L4-2 net10.0 + net472 目标项目各一**：确认双 host 消费。

## 无副作用边界

- L1 无副作用（构建产物断言，非测试副作用）。
- L2/L3 `dotnet pack`/`MSBuild.exe` 构建临时项目：进程启动 + 文件写入属**集成验证**，Vortex 验证者可跑 `dotnet pack`（只读产物路径不污染测试），Full host 端到端（L3-2/L4）需 Full MSBuild → 用户手动。
- 不写测试基座 mock MSBuild host（net472 任务宿主即 MSBuild.exe，非本仓库可无副作用实例化的对象）。

## 全量回归（F8）

- 七门 gate：`scripts\verify-vb-compiler-tests.ps1` 全绿。
- Scripting 程序集 `-automated` 直接跑（MTP 项目，dotnet test 静默不跑）：`Scripting\VisualBasicTest` 全绿。
- netstandard2.0 构建门（L1-1）纳入常设（R7）：本任务回归含一次 ns2.0 + net48 宿主构建。
