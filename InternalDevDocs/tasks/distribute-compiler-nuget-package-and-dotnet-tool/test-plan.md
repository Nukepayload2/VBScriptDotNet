# 测试计划：分发 fork 编译器（Toolset NuGet 包 + .net tool）

> 状态：测试计划（F3）。依据链：`design-overview.md`（行为对照表）→ `design-detailed.md`（A1-A5/B1-B4 落点）。
> 测试强度参考上游 Toolset 打包验证（`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\` 集成）+ fork 编译器测试矩阵。
> **无副作用纪律（CLAUDE.md）**：单测禁止网络/文件写入/进程启动/注册表写入；`dotnet pack` + 临时项目引用属**集成验证**（涉及进程/文件），由用户手动跑或单独说明边界。

## 落点

| 层 | 测试项目 | 文件 |
|----|---------|------|
| L1 编译器单测（无副作用） | `Compilers\VisualBasicTest\` / `Compilers\VisualBasicCommandLineTest\` | `CommandLineTests.vb`（追加：vbc 编译产出/模式分发相关，走内存） |
| L2 打包产物验证（集成，用户手动） | 手动 `dotnet pack` + 临时 SDK 项目 | `tmp\exp-distribute\`（临时验证区） |
| L3 `vbi` tool 命令面（集成，用户手动） | 手动 `dotnet tool install`（或本地源） | 同上 |

**运行方式**：`Scripting\VisualBasicTest` 是 MTP 项目，`dotnet test` 静默不跑——**直接跑程序集 `-automated`**（记忆：`vb-scripting-test-runner`）；编译器测试走 `scripts\verify-vb-compiler-tests.ps1` 七门 gate。

## 测试边界（重要）

- **L1 单测无副作用**：编译器 API 调用（`VisualBasicCompilation`/`Vbc.Run` 内存输入）可无副作用测；但 `Vbc.Run` 写文件（emit 到磁盘）属副作用——**用内存 `Emit` 到 `MemoryStream`** 或编译到内存程序集验证，不落盘。
- **L2/L3 集成验证（用户手动）**：`dotnet pack`（写文件）、临时项目 `dotnet build`（启进程、写文件）、`dotnet tool install`（写全局工具目录）均涉及副作用——**归集成验证**，测试计划列出命令与预期，由用户手动跑；单测覆盖不到的部分明确标注。

## L1 编译器单测（无副作用）

> 目标：验证 fork 编译器**能力面**（编译/执行/版本）在改动后不回归；打包/工具安装面归 L2/L3 集成验证。

| # | 用例 | 断言 | 副作用 |
|---|------|------|--------|
| L1-1 | `VisualBasicCompilation` 编译 `.vb` 源（内存） | 0 诊断，emit 到 `MemoryStream` 非空 | 无（内存） |
| L1-2 | `.vbx` 源（`SourceCodeKind.Script`）编译/解析 | 0 诊断（验证 vbi 执行核心未被 tool 改动破坏） | 无（内存） |
| L1-3 | netcore 编译器**不带 rsp**（A2 定案） | 打包产物不含 `vbc.rsp`（对齐上游 `CoreClrCompilerArtifacts.targets` 零 rsp）；现 `WriteNet10ResponseFile`（`vbc.csproj:35-51`）生成的 `$(TargetDir)vbc.rsp` 不进入包 | 无（读内存/现有 target） |
| L1-4 | 编译器三库 `IsPackable` 元数据 | `Microsoft.CodeAnalysis.csproj:15-16` 等 `IsPackable=true` 仍在（打包项目引用不破坏） | 无 |
| L1-5 | `AssemblyInformationalVersion` 落 `2.0.0-Beta` 后（B3 选项 a） | `GetSelfVersion` 读到 `2.0.0-Beta`；`GetRoslynVersion` 仍读 Roslyn 上游版本（`Vbi.vb:39-50` 逻辑） | 无（反射读属性） |

## L2 打包产物验证（集成，用户手动）

> 涉及 `dotnet pack`（写文件）+ 临时项目 `dotnet build`（启进程/写文件）——**用户手动跑**。临时验证区 `tmp\exp-distribute\`（git-ignored 或清理）。

| # | 步骤 | 预期 |
|---|------|------|
| L2-1 | `dotnet pack Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj -c Release` | 产出 `Nukepayload2.Compilers.VBScriptDotNet.2.0.0-Beta.nupkg` |
| L2-2 | 解包 nupkg | `build\` props + `tasks/netcore/bincore\`（`Microsoft.CodeAnalysis.dll` + `Microsoft.CodeAnalysis.VisualBasic.dll` + `vbc.dll` + deps/runtimeconfig）存在；**无** `vbc.rsp`（netcore 编译器不带 rsp）、**无** `Microsoft.CodeAnalysis.CSharp.dll`、无 `csc`、无 VBCSCompiler |
| L2-3 | 临时 SDK 项目 `PackageReference` 该包（PrivateAssets=all）| `dotnet build` 用 fork vbc（VB 面）；C# 项目仍走 SDK csc（不注册 `Csc` 任务） |
| L2-4 | 临时项目默认 `/imports`（MSBuild 任务参数传入） | 构建正常，VB 面默认 imports 生效（不依赖 rsp——MSBuild 任务直接传 `Imports`） |
| L2-5 | net472 场景 | **允许缺失**——不打包 `tasks/net472`；VS 桌面老式项目用不上（用户定案），不验证为缺陷 |

## L3 `vbi` tool 命令面（集成，用户手动）

> 涉及 `dotnet tool install`（写全局工具目录）+ 进程执行——**用户手动跑**。

| # | 命令 | 预期 |
|---|------|------|
| L3-1 | `dotnet tool install Nukepayload2.Compilers.VBScriptDotNet.Cli` | 安装成功，命令名 `vbi` |
| L3-2 | `vbi --version` | 双行：`Nukepayload2's fork of Visual Basic Interactive Compiler [Version 2.0.0-Beta]` + `Based on Roslyn [Version 5.9.0]`（`Vbi.vb:31-50`） |
| L3-3 | `vbi src.vb /out:app.dll` | 编译到 `app.dll`（新增编译模式，走 `Vbc.Run`/`BuildClient.Run`） |
| L3-4 | `vbi script.vbx -- arg1 arg2` | 脚本执行，`Args` 收到 `arg1 arg2`（`VisualBasicScript.RunInteractiveAsync`） |
| L3-5 | `./script.vbx`（首行 `#!/usr/bin/env vbi`，Linux） | shebang 解释器执行（`proposal-shebang-directive.md` done 衔接） |
| L3-6 | `vbi`（无参数） | 交互 REPL（既有能力顺带保留） |
| L3-7 | `vbi /?` | 帮助文案（`vbi.rsp` 默认 imports 说明） |

## 既有测试影响核查

| 受影响面 | 影响 | 处理 |
|---------|------|------|
| `vbi.vbproj` 加 `PackAsTool` + 版本覆盖（B1/B3） | 商店版 wapproj（net10.0-windows/net48 TFM）引用是否受影响——`PackAsTool` 不影响 TFM 独立构建 | L2/L3 集成验证时确认商店版 `vbichooser`/`vbifw` 仍能构建 |
| `vbc.csproj` 加 publish 产出（A2） | publish 产物不含 rsp（netcore 编译器无 rsp 需求）；`WriteNet10ResponseFile` 生成的 `$(TargetDir)vbc.rsp` 仍留在输出目录供命令行编译用，但不进包——现命令行编译路径不变 | L1-3 回归 + L2-2 产物核对（无 rsp） |
| 新 MSBuildTask 项目（A1） | 不侵入现有编译器路径（并列新增）；`Contracts.projitems` 已有 | 编译通过即验证 |
| 新打包项目（A3） | `VBInteractive.sln` 登记，不影响现有构建 | 解决方案构建回归 |

## 判定通过标准

- L1 单测全绿（无副作用，内存 `StringReader`/`MemoryStream`）；`Scripting\VisualBasicTest` 直接跑程序集 `-automated` 通过。
- L2/L3 集成验证：用户手动跑，预期全部符合（打包产物核对、tool 命令面、shebang 衔接）。
- 七门 gate 全绿（`scripts\verify-vb-compiler-tests.ps1`）。
- 测不了的部分（L2/L3 集成副作用）向用户说明并询问，不静默跳过。
