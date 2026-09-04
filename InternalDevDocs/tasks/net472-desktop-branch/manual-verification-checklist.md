# 手动验证清单：net472 桌面分支 Full-host 端到端（集成）

> 状态：F7 登记（2026-09-04）。本清单按 test-plan.md 的 L2-4/L3-2/L4 整理，命令与预期基于 Full host 端到端实测结果（Full host 消费 0 错误、R5 选项 b 成立）。**Supersede** distribute v1 checklist 的 L2-5「net472 允许缺失」——该定案已被 proposal-net472-desktop-branch 修正，net472 桌面分支现为支持能力。
> **无副作用纪律（CLAUDE.md）**：以下命令含 `dotnet pack` / 临时项目 `MSBuild.exe` build 等**集成副作用**（写文件、启进程）——由用户手动跑，不在单测/自动化范围。
> **Shell**：命令为 git bash；临时产物放 `<项目根>/tmp/exp-net472/`（git-ignored 或验证后清理）。

## 0. 前置：产物

- 最终 nupkg：`Installer/Toolset/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.2.0.0-Beta.nupkg`（F6 repack 含 F5 分派 props + net472 分支 + DiaSymReader.Native 三 arch）。
- Full MSBuild：VS `MSBuild.exe`（本机 VS 18 Community：`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`）。
- **缓存 gotcha（重要）**：引用 `2.0.0-Beta` 前若已装过该版本，须 purge NuGet 全局缓存 `~/.nuget/packages/nukepayload2.compilers.vbscriptdotnet/2.0.0-beta`（否则采信旧包，props 是 v1 硬编码 netcore 版，Full 测试会误判）。F6 曾遇此坑。

## L2-4 Full host targets 消费（R5 选项 b 复验）

临时 SDK 风格 VB 项目（`<Project Sdk="Microsoft.NET.Sdk">`，net10.0 Exe，`Public Module M / Sub Main / End Sub / End Module`，PackageReference 本地 nupkg）：

```bash
cd <项目根>
mkdir -p tmp/exp-net472/consume && cd tmp/exp-net472/consume
# 建 .vbproj + Module1.vb（引用 <项目根>/Installer/Toolset/bin/Release 的本地 nupkg，RestoreSources 指向该目录）
"C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" consume.vbproj -t:Build -v:diag > full-diag.log 2>&1
grep -iE "RoslynTasksAssembly|Vbc|PathToTool|tasks.net472|VisualBasicCoreTargetsPath" full-diag.log
```

预期：
- EXIT 0；`Vbc` 任务 AssemblyFile = `...tasks\net472\Microsoft.Build.Tasks.CodeAnalysis.dll`（diag 含「正在使用程序集 ...net472... 中的 Vbc 任务」）。
- `PathToTool=...tasks\net472\vbc.exe`；`CompilerServer: tool - using command line tool by design`（`UseSharedCompilation=false`）。
- `VisualBasicCoreTargetsPath` 解析到 VS 自带 `Bin\Roslyn\Microsoft.VisualBasic.Core.targets`（**非** fork 铺包）→ R5 选项 b（SDK 自带 VB Core targets 链驱动 CoreCompile，props 不设该属性）成立。
- 记录 MSBuild 版本 ≥ 17.11。

## L4-1 VS 内构建（含 PDB 全 PDB 场景）

- VS 打开引用包的 SDK 风格 VB 项目，`<DebugType>full</DebugType>`，Build。
- 预期：成功；输出日志显示 Vbc 任务自 `tasks/net472` 加载、spawn `vbc.exe`；产 full PDB（`/debug:full`，MSF 7.00）——DiaSymReader.Native 三 arch 路径可用。
- 记录 VS/MSBuild 版本。

## L3-1 netcore 消费回归

同一临时项目 `dotnet build`：
预期：0 错误；`Vbc` 任务自 `tasks\netcore`（v1 现状）spawn `bincore\vbc.dll`；无 BC42376 噪声（Core 无该警告）。

## 已知观察（非缺陷，记录即可）

- Full net472 构建带 ~260 个 BC42376 警告：.NET 10 SDK NetAnalyzers 在 fork net472 `vbc.exe` 内绑定不到 `Microsoft.CodeAnalysis 3.11.0.0` 实例化失败——预存在噪声，Core 路径无，与 R5/本任务无关，不需消除。
