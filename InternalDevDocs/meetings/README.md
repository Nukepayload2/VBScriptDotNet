# VBScript.NET 产品会议索引

本文档登记 **VBScript.NET 产品自身**的设计会议（meetings 层）。会议纪要评估对应的产品提案（`../proposals/`），产出「**LDM 状态** + **三态判定**」，是提案状态分类（active/inactive/rejected/done）的判定依据。

**与 modvb 的关系**：`../modvb/meetings/` 是 Anthony 提案库的 LDM 会议纪要（AI 生成的参考材料）；本目录是 VBScript.NET 产品自身的设计会议，与产品提案 1:1 同步组织。

## 目录组织

- `meetings/` ↔ `proposals/`
- `meetings/inactive/` ↔ `proposals/inactive/`
- `meetings/rejected/` ↔ `proposals/rejected/`
- `meetings/vbscript-<版本>/` ↔ `proposals/vbscript-<版本>/`（done 归档，当前无成员）

## 会议纪要

| 文件名 | 对应提案 | 主题 | 三态判定 |
|--------|---------|------|---------|
| `meeting-vb-repl-parity-with-csharp-repl.md` | 现状盘点 + 评估 `proposal-optional-question-prefix.md` 与 `proposal-avalonia-ise-repl-ui.md` | 让 VB REPL 的功能追上 C# REPL 的讨论（2.0 beta 现状盘点、借鉴 csharplang、评估两个新提案） | optional-question-prefix = **Active**；avalonia-ise-repl-ui = **Consider** |
| `meeting-optional-question-prefix.md` | 评估 `proposal-optional-question-prefix.md`（REPL `?` 可选） | 深度评审：REPL 表达式开头问号可选——源码机制核实（PrintStatement / BC30545 / HasSubmissionResult）、候选方案（A/B/C/D）、实现落点（编译器层，跟随 C# REPL 设定）、边界与诊断族枚举 | optional-question-prefix = **Active** |
| `meeting-vscode-extension-ise-repl-ui.md` | 评估 `proposal-vscode-extension-ise-repl-ui.md`（VS Code 扩展 REPL/脚本编辑器） | 深度评审：VS Code 扩展运行方式全考虑（A 一进程 REPL+LSP+DAP / B 独立 stdio LSP / C Zed 扩展 / D 仅编辑）、Zed 竞品分析（本体内存低、stdio LSP、tree-sitter）、LSP 宿主作为跨客户端资产、与 Avalonia 双路线互补 | vscode-extension-ise-repl-ui = **Consider** |
| `meeting-byref-like-repl-safety.md` | 前置议题：为 `../proposals/proposal-byref-like-safety.md`（已建，byref-like 安全）与 `../tasks/p1-immediate.md` 前置-1（D1 RefStructHelper 移植 + suppress obsolete error）铺路 | 现状盘点（REPL submission 机制源码核实：顶层 `Dim`=脚本类字段、末尾表达式装箱到 Object、`<Initialize>` 恒 async）+ 三个碰撞点（字段持久化/结果打印/跨 Await）+ C# 借鉴（span-safety、CSX 持久化冲突）+ 定案：byref-like 提交内可用、不跨提交持久化，顶层变量/结果/跨 Await 编译错误 | 方向定案（D1 REPL 侧语义契约）；提案 `proposal-byref-like-safety.md` = **Active（Proposed）** |
| `meeting-vbscript-lsp.md` | 评估 `../proposals/proposal-vbscript-lsp.md`（vbscript.net LSP：普通 VB 项目 + VBX 脚本） | 深度评审：vb-ls 成本基线（2 patch + launcher）、本地基线补层机制四证据（public API 一致 / IVT 不授 IDE 栈 / 程序集身份一致 / Arcade 依赖使拷贝直引不可行）、两模式唯一区别是 script mode、语言服务器独立进程 + dotnet tool 分发（LDM 拍板）、里程碑 M0–M4 | vbscript-lsp = **Active（Proposed）** |
| `meeting-shebang-directive.md` | 评估 `../proposals/proposal-shebang-directive.md`（`.vbx` 首行 `#!` shebang） | 深度评审：C# 对等语法 LDM 决策链（issue #3507 → LDM-2020-07-20/09-28 → C# 14 ignored-directives #8617，`#!`/`#:` 为 ignored 指令）、VB 落地表面（上游 `#R` 管道为模板、词法零改动、整行消费是唯一新增机械件）、模式门控（仅 script）、位置规则（首字符 + BOM 不能在前）、severity（spec warning vs 实现 error） | shebang-directive = **Active（Proposed）** |
| `meeting-consume-csharp-extension-and-interface-shared.md` | 评估 `../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（消费 C# 扩展成员与接口共享成员） | 深度评审（vblang 官方叙事结构）：四层延申消费扩展成员 + SAIM 约束接口绑定；**源码锚点逐一复核**并发现运行时钩子已在位（`RuntimeCapability.VirtualStaticsInInterfaces`）与 `ExtensionMarkerNameAttribute` 术语对齐；范围定案消费/声明分离；五维评分与 C# 生态考量见 `evaluation-consume-csharp-extension-and-interface-shared.md` | consume-csharp-extension-and-interface-shared = **Active** |
| `meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` | 评估 `../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（分发 fork 编译器：Toolset NuGet 包 + .net tool） | 深度评审（vblang 官方叙事结构）：分发现状源码核实（三库 IsPackable 无产物、MSBuildTask/VBCSCompiler 被裁剪、进程内编译回退在 `BuildClient.cs:148-165`）；四条 Unresolved 备选列全调查后闭合（U1 隐式分派 / U2 不补 VBCSCompiler / U3 v1 仅 .NET SDK / U4 双行版本）；发现 `vbi --version` 链路已就位（`Vbi.vb:31-50`）且版权文案已「不提及 Microsoft」；五维评分与 C# 生态考量见 `evaluation-distribute-compiler-nuget-package-and-dotnet-tool.md` | distribute-compiler-nuget-package-and-dotnet-tool = **Active** |

## 会议纪要格式

- 标题：`# Visual Basic Language Design Meeting` + 日期行。
- 结构：开场白 → `## Agenda` → 各 Proposal 讨论（场景与缺口 / 候选方案 / 权衡 Q&A / RESOLUTION / 状态）→ 附录。
- 状态：每个讨论项给 **LDM 状态** + **三态判定**（仿 modvb meeting 写法）。
