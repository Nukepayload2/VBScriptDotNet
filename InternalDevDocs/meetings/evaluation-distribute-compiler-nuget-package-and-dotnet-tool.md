# 评审报告：分发 fork 编译器：Toolset 风格 NuGet 包 + .NET tool

> 本报告是 `meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` 的**独立评审文档**（内部评价），与官方 LDM 会议记录分开存放。依据 `modvb\evaluation-standard.md` 五维框架，证据等级按六档阶梯标注。**评分落到提案当前状态：Proposed（源码锚点全部复核、四条 Unresolved 备选列全并调查彻底、无原型实现、无 spec）**。

## 五维评价

| 维度 | 得分 | 评价 | 证据等级 | 问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 目标改进清晰（编译器中转分发渠道：项目构建 + 脚本/CLI）、边界完整（不含 csc/不替代工具链/专注 VB fork）；**问题侧已源码核实**（三库 IsPackable 无打包产物、MSBuildTask/VBCSCompiler 被裁剪、无发布 feed），**方案侧无原型**（Prototype: Not Started）——打包/补回机制止于源码锚定 | 已检查 / 方案无原型 | 方案需原型验证（Toolset 包引用后 .vbproj 构建、`vbi` tool 编译/执行双模式） |
| 特性 | 4/5 | 延申 fork 已有机制：三库 `IsPackable`、`Vbc.Run` 进程内入口、`CommandLineRunner` 隐式分派、`PrintLogo` 版本链路；职责单一（纯分发使能层，不触碰语言语义）；无新增语法表面积；`vbx` 命名已改正为 `vbi`（消除与扩展名撞车） | 已检查 | 无 |
| 品质 | 4/5 | 六章节结构完整 + 证据来源附录；源码锚点逐行标注、用户定案透明（包名/版本/边界/tool 名）；**四 Unresolved 已闭合**（U1 隐式/U2 先不做/U3 v1 不做 v2 加 net472/U4 双行版本），备选方案列全、证据锚定 `文件:行号`；个别实现细节留任务计划（`vbi` 编译模式接入落点、tool TFM 与 PackAsTool 互斥） | 已检查 | 实现级细节留任务计划 |
| 属性 | 4/5 | 主维度明显受益：雷（分发使能层，能力可被消费）、风（对齐 .NET 生态 Toolset/dotnet tool 分发范式）、水（脚本/CLI 免商店版差异化）、光（与 shebang 衔接盘活 Linux 可执行脚本）；**暗风险被显式识别**（MSBuildTask 补回维护面、v1 仅 .NET SDK、无编译服务器构建变慢、三通道版本统一）；四条 Unresolved 定案收窄风险 | 已检查（待定） | Toolset 补回维护面、构建性能（无 server）需原型确认 |
| 炼金成分 | 4/5 | 材料来源标注准确完整：上游 Toolset 包（`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\`）、dotnet tool 范式（`Microsoft.CodeAnalysis.LanguageServer.csproj:12-14`）、fork 源码锚点、跨提案衔接（shebang/LSP）；成分影响符合预期（借鉴 Toolset=风属性兼容、延申 fork 现有机制=盘活资产、`.Cli` 后缀分层=为 GUI 留位）；用户定案清单完整记录 | 已检查 | 无 |

## 总判定

- 达成程度：**达成**（分发方向明确、机制可执行、源码锚点全部复核、四条 Unresolved 备选列全并调查彻底后闭合；未决问题收窄到实现级）。
- LDM 三态建议：**Active**（分发使能层，零语言语义变化）。
- 主要问题：①方案无原型（效果证据止于源码核实，Toolset 补回 MSBuildTask + `vbi` tool 打包需原型验证）；②`vbi` 编译模式接入 `CommandLineRunner` 的具体落点（执行路径 vs 编译路径分派边界）留任务计划；③tool 打包时 net10.0-windows/net48 TFM 与 `PackAsTool` 的互斥关系需实现验证。

**未决问题计数的「≥4 封顶」规则审视**：提案 Unresolved questions 节原始 5 项中，**包名/版本（#1）与不含 csc（#2）已由用户定案移除**，剩余 4 项在本次 meeting **全部定案闭合**（U1 隐式分派、U2 不补 VBCSCompiler、U3 v1 仅 .NET SDK + v2 明确路径、U4 双行版本）。当前 Unresolved 剩余仅实现级待定（`vbi` 编译模式接入落点、tool TFM 互斥、`InformationalVersion` 版本映射）——**核心设计问题全部闭合**，效果 3/5 由「方案无原型」单独支撑，非未决问题驱动。

## 返工建议

- 补充证据：把 `vbi --version` 完整链路已就位（`Vbi.vb:31-50` + `CommonCompiler.cs:159-170`）与「库层 netstandard2.0 已兼容 net472、vbi net48 先例」写入提案——分别支撑 U4（零新机制）与 U3（v2 成本比直觉低）。
- 原型优先级：先 Toolset 包最小闭环（补 MSBuildTask 只注册 Vbc + vbc publish 产出 + `UseSharedCompilation=false` props，验证 .vbproj 构建用 fork vbc）→ 再 `vbi` tool 编译模式（`Vbc.Run`/`BuildClient.Run`，`Compilers\Shared\Vbc.cs:22-29`、`vbc\Program.cs:39`）。
- 未决问题处理：`vbi` 编译模式接入 `CommandLineRunner`（执行 vs 编译分派边界）、tool TFM 与 PackAsTool 互斥、`AssemblyInformationalVersion` 产品版本映射并入任务计划阶段。

---

## 附录：C# 生态与互操作考量

> 依据 `..\csharplang-index.md` 与 `InternalDevDocs\csharplang\` 镜像。`decisions.md` 的 M1–M8 / D1–D4 为 VBScript.NET 侧权威。

**C#/.NET 现实方向 vs 提案**：
- **Toolset 分发范式（Microsoft.Net.Compilers.Toolset）**：.NET 生态中「让项目构建用指定编译器」的成熟机制——包引用后 `UsingTask Vbc/Csc` 指向包内任务 DLL，`RoslynAssembliesPath` 指向包内编译器程序集（`{{Roslyn}}src\NuGet\Microsoft.Net.Compilers.Toolset\AnyCpu\build\Microsoft.Net.Compilers.Toolset.props:38-41`）。本提案仿其 VB 面，**不含 csc**（用户定案）——C# 编译仍走 SDK 自带编译器，这是与上游 Toolset 的关键差异。
- **dotnet tool 分发范式**：Roslyn 语言服务器以 dotnet tool 分发（`Microsoft.CodeAnalysis.LanguageServer.csproj:12-14`，`PackAsTool=true` + `ToolCommandName`）；`proposal-vbscript-lsp.md` M4 已定 dotnet tool 打包先例。本提案 `vbi` tool 复用同一范式。
- **shared compiler server**：上游 Roslyn 用 VBCSCompiler（`{{Roslyn}}src\Compilers\Server\VBCSCompiler\`，~25 文件双目标）加速共享编译。**用户明确不复用 dotnet cli 的 server 做法，先不做**——`BuildClient.cs:148-165` 进程内回退保证正确性，仅牺牲大解决方案构建速度，属纯性能取舍。
- **net472 桌面 MSBuild**：上游 Toolset 双目标（`$(NetRoslynSourceBuild);net472`）服务 VS 桌面老式 .vbproj。**用户定案：允许缺失**——.NET Framework SDK 与 .NET SDK 的 target 基本是两套东西，只是复用材料；新 SDK 只要 VS 里能正常编译即可。影响面已调查：Toolset 按 MSBuild 宿主运行时二选一加载（`Microsoft.Net.Compilers.Toolset.props:9-13`），VS 里 SDK 风格项目走 MSBuild Core → 缺 net472 无影响；仅老式非 SDK 项目需 bridge 壳（`ManagedToolTask.cs:233-235`）才用不上。fork 库层 netstandard2.0 已可被 net472 加载，vbi 已有 net48（`vbi.vbproj:7`）——若 v2 补，只需 vbc 驱动 + MSBuildTask 双目标。

**对 VBScript.NET 的适应建议**：
- Toolset 包**只注册 Vbc**（不含 csc、不替代 dotnet 工具链）——`.vbproj` 构建 VB 面用 fork 编译器、C# 面仍走 SDK，与「专注 VB fork」定位一致。
- `vbi` tool 命名用现有 `vbi`（复用 `Interactive\vbi\vbi.vbproj`，`AssemblyName=vbi`），不新建 `vbx`/`vbc` 名——`vbx` 与 `.vbx` 扩展名撞车、`vbc` 是编译器驱动名；tool 包名加 `.Cli` 后缀（`Nukepayload2.Compilers.VBScriptDotNet.Cli`）为将来 GUI 界面产品留分层。
- `vbi --version` 双行版本（自版本 `2.0.0-Beta` + Roslyn 上游）用现有 `PrintLogo` 机制（`Vbi.vb:31-50`）；版权文案（`VBScriptingResources.resx:120-159`：Nukepayload2's fork / Based on Roslyn / pre-release）已与「不提及 Microsoft」一致。
- 包描述写「基于 .NET Foundation 的 Roslyn 编译器改造而来」不直接提及 Microsoft——与 fork 署名（Nukepayload2）一致，规避品牌混淆。

**对 RESOLUTION 的影响**：无变化——以上均支持 RESOLUTION 的 Active 判定与四条 Unresolved 定案，并为实现提供现成落点（`BuildClient.cs` 进程内回退、`CommandLineRunner` 隐式分派、`PrintLogo` 版本链路、netstandard2.0 库层兼容）。
