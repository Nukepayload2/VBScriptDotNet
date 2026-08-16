# Visual Basic Language Design Meeting
August 15, 2026

本次会议是 `proposal-vbscript-lsp` 的专用 1:1 会议。承接 `meeting-vscode-extension-ise-repl-ui.md`（2026-08-10，proposal-03 判 **Consider**，其 RESOLUTION #2 明确「LSP 宿主是两端共同的最大资产，必须先立起来」）。本次会议把「LSP 服务器」这条线从 proposal-03 单独展开：回到源码核实**补层机制**（本地基线 `{{Roslyn}}` 复用 vs 自建 LSP 宿主——修正 proposal-03 会议「fork 编译器树无 Workspaces/LSP 层、宿主须自建」的旧假设），逐条拍板剩余方向问题（普通项目编译器语义、宿主形态、分发），给出三态判定。

## Agenda

* [Proposal: VBScript.NET LSP（普通 VB 项目 + VBX 脚本）](#proposal-vbscriptnet-lsp普通-vb-项目--vbx-脚本)

## Proposal: VBScript.NET LSP（普通 VB 项目 + VBX 脚本）

_Related: `../proposals/proposal-vbscript-lsp.md`（主检对象）；姊妹 `../proposals/proposal-vscode-extension-ise-repl-ui.md`（Consider，其会议明确 LSP 宿主须先立起来）；`../proposals/proposal-byref-like-safety.md`（fork 编译器对常规模式生效，普通项目吃 fork 能力的硬理由）；`../proposals/proposal-optional-question-prefix.md`（`?` 前缀，修改语法适配点之一）；外部参照 `{{VbLs}}`（成本基线）；本地基线 `{{Roslyn}}`（补层来源）_

### 场景与缺口

- **vbx 脚本编辑体验是产品最短板**：`.vbx` 无语法高亮、无补全、无 hover、无诊断；REPL 是控制台 `vbi.exe`。普通 `.vbproj` 项目用户同样无 IDE 级编辑能力，且 fork 编译器对常规模式也有增强（byref-like 安全），需要一个能用上这些增强的编辑前端。
- **vb-ls 覆盖普通 VB 项目但不支持 `.vbx`**：`{{VbLs}}`（成本基线）的做法是 vendor 整个上游 Roslyn + 打 2 个微型 patch（VB Features 进 MEF 组合、autoload `*.vbproj`）+ 28 行 launcher（spawn Roslyn LSP DLL）——**覆盖普通 VB 项目成本 ≈ 几行 patch**，因为上游 IDE 栈现成。但上游编译器不认 `.vbx` 扩展、`#R`/`#Load`/`' Attribute TargetFramework`、Script 语义与修改语法（`?` 前缀、byref-like），Features 层绑定原版 `SyntaxKind`，故 vb-ls 做不了 vbx。
- **本会议新增的场景维度**：本项目仓库的 fork 编译器树（`Compilers\`，剪枝自 Roslyn，仅编译器）**没有** Workspaces/Features/LanguageServer 层——补层是走「vendor 上游」还是「复用本地基线」，是本会议要核实的核心。

### 现状机制（源码核实）——本会议新增价值所在

#### 基线澄清（修正 proposal-03 会议的旧假设）

- `{{Roslyn}}` 是**完整、未修改**的 Roslyn 源码树（`release/stable`，实测 ≈ 20724 files），含全部 `Workspaces` / `Features` / `LanguageServer` 层。本仓库只复制了其中编译器部分并做产品修改；**未复制的 IDE 层项目就是基线原样**——补层不必从网络 vendor，也不必「自建 LSP 宿主」（proposal-03 会议因当时未确认基线而写的「宿主须自建」假设**修正为：IDE 栈从本地基线复用**）。
- fork 与基线**同源同线**：`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb` 逐字一致、`LanguageVersion` 同为 `VisualBasic17_13`、public API（`PublicAPI.Shipped/Unshipped.txt`）**逐字一致**——fork 只改内部实现。
- fork 编译器相对基线的**增值修改**（源码 diff 核实）：`Binder_Expressions.vb` 的 host object 绑定（基线为 `TODO (tomat)` 注释）与 `IsRefLikeOrAllowsRefLikeType`、`SynthesizedSubmissionFields.vb` 的 host object 字段（基线注释掉）、`VisualBasicCompilation.vb` 的提交结果判定（method-group 意识 + `ReturnStatement`）。这些是 vbx 脚本语义（globals / 打印 / 退出码）的编译器侧实现，LSP 要的就是这套语义。

#### 补层机制调查（复制进仓库可行）

1. **IDE 栈只消费编译器 public API**：编译器 `InternalsVisibleTo` 只授 `Scripting` / `vbi` / 测试程序集，**不授 `Workspaces` / `Features` / `LanguageServer`**（`Microsoft.CodeAnalysis.VisualBasic.vbproj` IVT 列表核实）；fork 与基线 public API 逐字一致 → 复制项目对着 fork 编译器编译无兼容障碍。
2. **程序集身份一致**：同源同线、同一强名公钥（`MicrosoftSharedPublicKey.snk`）、TFM 同为 `net10.0`（fork 编译器 `netstandard2.0;net10.0`；基线 `NetVSCode` = `net10.0`）→ 同仓库编译产物可共存。
3. **裁剪面小**：Razor ≈ 3279 files 可整块裁掉——LanguageServer 对 Razor/ExternalAccess 的**代码引用仅 4 处**（`LanguageServerProjectLoader.cs` targets 路径 / `LanguageServerExportProviderBuilder.cs` MEF DLL 声明 / `CopilotCompletionResolveContextHandler.cs` / `Razor\TelemetryReporterWrapper.cs`）；VS 专用 ExternalAccess 均为薄壳（实测 1–32 files）。复制范围收敛到 Workspaces / Features / LanguageServer / Protocol（~4000 files）。LanguageServer 对 CSharp.Features **无直接代码引用**（csproj 引用仅为 MEF 组合）。
4. **Arcade 可剥离**：IDE 项目脱离 Arcade 需在 fork 的 `Directory.Build.props` 补 `NetVSCode` / `IsShipping` 等属性 fallback——fork 的 `Compilers\` 已证明可脱离 Arcade 独立构建（先例成立）；csproj 相对路径（`..\..\..\Compilers\...`）指向 fork 的 `Compilers\` 即可（机械性批量调整）。
5. **基线 LanguageServer 不含 VB Features**：csproj 只引用 C# Features + ExternalAccess——vb-ls 的 `lsp-vb-support.patch`（加 `Microsoft.CodeAnalysis.VisualBasic.Features.vbproj` 引用）**仍然需要**；`autoload-vb-projects.patch`（加 `*.vbproj` 枚举）同样需要。

#### 脚本模式支持是现成的

- 基线 Workspaces 原生支持 Script 文档（`CommandLineProject.cs` / `Solution.cs` 的 `SourceCodeKind.Script` / `CreateScriptCompilation` 路径）——vbx 作为 script-mode 松散文件接入 LSP 有现成地基，无需自研脚本编译管线。
- 松散文件语言映射（vb-ls 已验证）：`.vb` 已按扩展名映射 `VisualBasic`；`.vbx` 只需补扩展名注册 → `VisualBasic` + Script。补全 / hover / 诊断 / 格式化由同一套 `Microsoft.CodeAnalysis.VisualBasic.Features` 自动生效。

### 候选方案（补层 / 实现路径）

**PROPOSAL A — 完整拷贝 IDE 栈（含 Razor / VS 专用依赖）。** 把基线 Workspaces + Features + LanguageServer + Razor + ExternalAccess 全部复制进 fork，不裁剪。功能最全、省事；但 Razor ≈ 3279 files 与 VS 专用 ExternalAccess（Copilot / Xaml / AspNetCore）对 VB 场景无价值，仓库膨胀到近完整 `src/`——**判定不必要**。

**PROPOSAL B — 轻量自建 LSP 宿主。** 只建 LSP 宿主、fork 编译器直连、能力自研（proposal-03 会议的旧设想要找的路径）。但**标题即 LSP**——Roslyn LSP + Features 栈现成可用（基线），自研补全/分类/格式化纯属重复造轮子——**判定不需要**。

**PROPOSAL C — 复制 IDE 栈项目进仓库 + 裁剪 VS 专用依赖（本提案定论）。** 从本地基线 `{{Roslyn}}` 复制 `Workspaces`（Core + VisualBasic + MSBuild）、`Features`（Core + VisualBasic）、`LanguageServer`、`Protocol` 进 fork，对着 fork 编译器对齐构建；**裁剪 Razor**（≈ 3279 files，LanguageServer 对其代码引用仅 4 处：targets 路径字符串 / MEF DLL 声明 / `CopilotCompletionResolveContextHandler` / Razor `TelemetryReporterWrapper`，删除即可）+ VS 专用薄壳 ExternalAccess（Copilot / AspNetCore / Xaml / TestDiscovery / VisualDiagnostics，~100 files）；套用 vb-ls 两个 patch；在此之上把 `.vbx` 接入为 script-mode 松散文件（扩展名注册 + 脚本引用解析 + 修改语法适配）。仓库自包含、构建只在 fork 内完成。**判定：采纳。**

**两模式唯一区别是 script mode。** 普通项目走 MSBuild workspace（`SourceCodeKind.Regular`），vbx 走松散文件脚本编译（`SourceCodeKind.Script`，`CreateScriptCompilation` + `#R` / `#Load` / `' Attribute TargetFramework` 引用语义）。同一套 Features 栈，差异点收敛为：`SourceCodeKind`、引用来源、松散文件路由、修改语法适配。

### 权衡：Q&A

- **为什么普通项目也用 fork 编译器而非标准 VB（vb-ls 成品）？** fork 编译器对常规编译模式也有增强（byref-like 安全 `proposal-byref-like-safety.md` 明确"对 vbx 与常规编译模式都生效"）；单一编译器、双模式共享语义是产品自洽；若用 vb-ls 成品则需维护两套编译语义/两个服务器进程。**决策：普通项目用 fork 编译器。**
- **复制方案的长期约束是什么？** fork 编译器不得破坏 public API 面（`PublicAPI.Shipped.txt` 把关，当前与基线一致）；IDE 栈只消费 public API（IVT 不授），故编译器内部增值不影响 IDE 栈。IDE 栈版本随复制时点固定，升级需重新复制对齐。
- **宿主形态：一进程（REPL+LSP+DAP）还是独立进程？** proposal-03 会议 RESOLUTION #2 曾倾向 vscode-powershell 一进程模式（REPL 状态同步优势）。本次 LDM **拍板：language server 是独立进程**——不塞进 `vbi`、不与 REPL/DAP 并载，保持 REPL 执行器与长驻服务分离；proposal-03 的扩展作为外部客户端经命名管道连接。理由：独立进程是跨客户端的最大复用资产（stdio 可被任意客户端消费），一进程的 REPL 状态同步优势属于 proposal-03 的 VS Code 壳，不应耦合进服务器本身。
- **分发形态？** **dotnet tool 打包（照 vb-ls）**，可配合 IDE 扩展——dotnet tool 满足任意 LSP 客户端（NeoVim / Zed / VS Code 经扩展配置），IDE 扩展只做薄客户端。
- **与 avalonia 的关系：LSP 不服务自家 GUI。** avalonia ISE（`proposal-avalonia-ise-repl-ui`）是**单进程、纯代码直连** fork 编译器——直接调用编译器 API，**不走 LSP、无 JSON-RPC 序列化/反序列化开销**；本 LSP 的独立进程 + JSON-RPC 对它是额外开销，avalonia **不依赖也不使用本 LSP**。两条独立集成路径：LSP 供外部编辑器（VS Code / Zed / 完整版 VS 潜在），avalonia 走进程内直连。本 LSP 复用的 IDE 栈与完整版 VS 的 VB 语言服务同源，是潜在附加价值（未排期）。
- **TFM 与跨平台？** net10.0（对齐 fork 编译器与基线 LanguageServer）；LSP 服务器 net10.0 跨平台，net48 脚本宿主仅 Windows（`' Attribute TargetFramework` 只影响脚本引用解析目标）；首版 Windows 优先，服务器代码无跨平台壁垒。

### 深度追问：LDM 拷问清单

1. **`#Load` 多文件脚本在 LSP 里以什么形态呈现？** 定论：**同一脚本 Project 的多 Document 模型**——被加载文件在编辑器打开时作为普通 Document 纳入，未打开按磁盘文本纳入隐藏文档；Workspaces 原生 Script 支持 + `SemanticModel` 自动覆盖被加载文件的符号与诊断。不用虚拟文档/只读投影，最简且语义完整。
2. **修改语法（`?` 前缀 / `#R` / byref-like）与 Features 语法假设的适配面多大？** fork 编译器的 `SemanticModel` 天然支持这些语义（诊断/补全/hover 基于它）；风险点在 Features 中匹配标准语法树的代码路径（补全上下文、格式化、分类）。**规模未知，M0 定位**——用最小 LSP（一个 `.vbx` + 一个 `.vbproj`）实测，这是剩余的主要不确定项。
3. **测试纪律？** 无副作用单测（CLAUDE.md 规约）：LSP 能力（补全/诊断/格式化/hover）复用编译器测试基类 `Test\Utilities\VisualBasic\BasicTestBase`；REPL 语义沿用 `Scripting\VisualBasicTest\`；不得发起网络/文件写入/进程启动/注册表写入。
4. **与 proposal-03 的次序？** 本提案 Active 是 proposal-03（Consider）升级 Active 的**闸门前置**——proposal-03 会议 RESOLUTION 明确"待 LSP 宿主最小原型验证补全/诊断后升级 Active"，本提案的 M0/M1 正是这个原型。次序：本提案 M0 → M1（vbx 接入）→ 原型通过 → proposal-03 升级 Active。

### RESOLUTION:

1. **方向判定：采纳，Active（Proposed）。** LSP 服务器是 proposal-03 会议明确「必须先立起来的最大资产」；本提案把所有方向问题定论（普通项目 fork 编译器、补层机制、TFM、跨平台、`#Load` 模型、独立进程、dotnet tool 分发），成本量化到「复制 IDE 栈 + 裁剪 + vbx 接入」，且源码证据（public API 一致、IVT 不授 IDE 栈、Razor 裁剪面小、Arcade 可剥离、Script 支持现成）已把最大风险（API 缺口）基本排除。**判 Active**（归 active 根目录），进入 M0 实现规划。**升级实现的闸门：M0 原型验证通过**（复制 IDE 栈 + 裁剪 + 对齐构建 + 最小 LSP 实测）。
2. **补层机制：复制 IDE 栈项目进仓库 + 裁剪 VS 专用依赖（PROPOSAL C）。** 源码已本机，直接复制——仓库自包含、构建不依赖外部基线。依据：IDE 栈只消费编译器 public API（IVT 不授）、fork 与基线 public API 一致、Razor 引用点仅 4 处可裁（~3279 files 整块裁掉）、Arcade 可剥离（`Compilers\` 先例）。**长期约束：fork 编译器不得破坏 public API 面；IDE 栈版本随复制时点固定。**
3. **两模式唯一区别是 script mode。** 同一套 Features 栈；vbx 差异点 = `SourceCodeKind` + 引用来源 + 松散文件路由 + 修改语法适配。
4. **语言服务器是独立进程**（LDM 拍板）——不塞进 `vbi`；stdio 起步、命名管道可选（供 proposal-03 扩展连接）。
5. **dotnet tool 分发**（LDM 拍板），可配合 IDE 扩展。
6. **TFM net10.0、首版 Windows 优先**（服务器本身跨平台）；`#Load` 用同一脚本 Project 多 Document 模型。
7. **里程碑：M0（复制 IDE 栈 + 裁剪 + 对齐构建 + 最小 LSP 原型）→ M1（vbx 接入：诊断 + completion + hover）→ M2（signatureHelp / semanticTokens / 格式化 / `#Load` 跳转 / `#R` 导航）→ M3（普通项目 + 自动模式切换）→ M4（dotnet tool 打包 + IDE 扩展集成）。** M0 是剩余不确定项（修改语法适配规模）的唯一实测入口。

### Implication:

- **提案状态**：`../proposals/proposal-vbscript-lsp.md` 判 **Active**，归 active 根目录；`../proposals/README.md` 索引与提案文件状态行的更新由验证/调度阶段处理。
- **对 proposal-03 的意义**：本提案 Active 消除了其升级闸门的不确定性来源——LSP 宿主（独立进程）是它的一进程壳（REPL+LSP+DAP）与 Zed stdio 壳共享的核心；本提案 M0/M1 通过后，proposal-03 可升级 Active。**本提案不重复实现 proposal-03 的 TS 客户端 / REPL / DAP，只做服务器。**
- **仓库结构**：新根目录项目 `LanguageServer\`（`VBScriptDotNet.LanguageServer\` LSP 宿主 + 双模式路由；`VBScriptDotNet.Scripting.Server\` vbx 脚本编译接入：扩展名注册 + 引用解析；`VBScriptDotNet.Server.Test\` 无副作用单测）。IDE 栈构建在本地基线 `{{Roslyn}}`（不拷贝进 fork 仓库，保持"剪枝"）。
- **最小原型（M0，串行第一步）**：从基线复制 IDE 栈项目进 fork → 裁剪 Razor/VS 专用依赖 → 对齐 fork 构建（路径 / Arcade 属性 / MEF 验证）→ 跑通最小 LSP（一个 `.vbx` 松散文档 + 一个 `.vbproj`）→ 实测补全/诊断/hover，定位修改语法与 Features 的适配点。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：修改语法（`?` 前缀 / `#R` / byref-like）与 Features 语法假设的适配规模——M0 实测后定量，是升级实现的闸门。
- `TODO`：M0——复制 IDE 栈项目进仓库 + 裁剪 Razor/VS 专用依赖 + 对齐构建（含 vb-ls 两 patch）+ 最小 LSP 原型（`.vbx` + `.vbproj`）。
- `TODO`：`.vbx` 扩展名注册 + 松散文件 script 编译接入（对齐 `vbi.coreclr.rsp` 引用集）。
- `Follow-up`：M0/M1 通过后评估 proposal-03（Consider）升级 Active；dotnet tool 打包走 vb-ls 先例。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active（Proposed）**——方向成立、所有方向问题已定论、补层机制源码证据充分（public API 一致 + IVT 不授 IDE 栈 + Razor 裁剪面小 + Arcade 可剥离 + Script 支持现成）、成本量化到「复制 IDE 栈 + 裁剪 + vbx 接入」。归 active 根目录，进入实现规划；**升级实现的闸门：M0 原型验证通过**（复制 + 裁剪 + 对齐构建 + 最小 LSP 实测，定位修改语法与 Features 的适配规模）。

---

## 附录：成本基线对照（vb-ls vs 本项目）

| 维度 | vb-ls（外部参照） | 本项目（proposal-vbscript-lsp） |
|------|------------------|-------------------------------|
| 编译器 | vendored 上游 Roslyn 原版 | fork 编译器（`Compilers\`，增值：host object / 提交结果 / byref-like / `?` 前缀） |
| IDE 栈来源 | vendor 完整上游（网络） | 本地基线 `{{Roslyn}}`（复制项目进仓库） |
| IDE 栈接入 | 原样构建 | 复制 + **裁剪 Razor/VS 专用依赖** + 对齐 fork 构建（public API 一致保证兼容） |
| 覆盖范围 | 普通 VB 项目（`.vbproj` / 松散 `.vb`） | 普通 VB 项目 + **vbx 脚本**（script mode 松散文件） |
| 成本 | 2 个微型 patch + launcher | 复制 IDE 栈（~4000 files）+ 裁剪 + vb-ls 两 patch + `.vbx` 接入 |
| 宿主 | 独立进程（spawn Roslyn LSP DLL） | **独立进程**（LDM 拍板） |
| 分发 | dotnet tool + VS Code 扩展 | **dotnet tool**（LDM 拍板），可配 IDE 扩展 |
