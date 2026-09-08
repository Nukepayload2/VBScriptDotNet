# VBScript.NET 产品会议索引

本文档登记 **VBScript.NET 产品自身**的设计会议（meetings 层）。会议纪要评估对应的产品提案（`../proposals/`），产出「**LDM 状态** + **三态判定**」，是提案状态分类（active/inactive/rejected/done）的判定依据。

**与 modvb 的关系**：`../modvb/meetings/` 是 Anthony 提案库的 LDM 会议纪要（AI 生成的参考材料）；本目录是 VBScript.NET 产品自身的设计会议，与产品提案 1:1 同步组织。

## 目录组织

- `meetings/` ↔ `proposals/`
- `meetings/inactive/` ↔ `proposals/inactive/`
- `meetings/rejected/` ↔ `proposals/rejected/`
- `meetings/vbscript-<版本>/` ↔ `proposals/vbscript-<版本>/`（done 归档，当前无成员）
- 会议做三态判定/优先级时，可对照 `../dream-of-vbdev.md`（§2 主线、§5 六元素、§7 三方张力）作动机/定位上下文（非权威输入）。

## 会议纪要

| 文件名 | 对应提案 | 主题 | 三态判定 |
|--------|---------|------|---------|
| `meeting-vb-repl-parity-with-csharp-repl.md` | 现状盘点 + 评估 `proposal-optional-question-prefix.md` 与 `proposal-avalonia-ise-repl-ui.md` | 让 VB REPL 的功能追上 C# REPL 的讨论（2.0 beta 现状盘点、借鉴 csharplang、评估两个新提案） | optional-question-prefix = **Active**；avalonia-ise-repl-ui = **Consider** |
| `meeting-optional-question-prefix.md` | 评估 `proposal-optional-question-prefix.md`（REPL `?` 可选） | 深度评审：REPL 表达式开头问号可选——源码机制核实（PrintStatement / BC30545 / HasSubmissionResult）、候选方案（A/B/C/D）、实现落点（编译器层，跟随 C# REPL 设定）、边界与诊断族枚举 | optional-question-prefix = **Active** |
| `meeting-vscode-extension-ise-repl-ui.md` | 评估 `proposal-vscode-extension-ise-repl-ui.md`（VS Code 扩展 REPL/脚本编辑器） | 深度评审：VS Code 扩展运行方式全考虑（A 一进程 REPL+LSP+DAP / B 独立 stdio LSP / C Zed 扩展 / D 仅编辑）、Zed 竞品分析（本体内存低、stdio LSP、tree-sitter）、LSP 宿主作为跨客户端资产、与 Avalonia 双路线互补 | vscode-extension-ise-repl-ui = **Consider** |
| `meeting-byref-like-repl-safety.md` | 前置议题：为 `../proposals/proposal-byref-like-safety.md`（已建，byref-like 安全）与 `../tasks/p1-immediate.md` 前置-1（D1 RefStructHelper 移植 + suppress obsolete error）铺路 | 现状盘点（REPL submission 机制源码核实：顶层 `Dim`=脚本类字段、末尾表达式装箱到 Object、`<Initialize>` 恒 async）+ 三个碰撞点（字段持久化/结果打印/跨 Await）+ C# 借鉴（span-safety、CSX 持久化冲突）+ 定案：byref-like 提交内可用、不跨提交持久化，顶层变量/结果/跨 Await 编译错误 | 方向定案（D1 REPL 侧语义契约）；提案 `proposal-byref-like-safety.md` = **Active（Proposed）** |
| `meeting-vbscript-lsp.md` | 评估 `../proposals/proposal-vbscript-lsp.md`（vbscript.net LSP：普通 VB 项目 + VBX 脚本） | 深度评审：vb-ls 成本基线（2 patch + launcher）、本地基线补层机制四证据（public API 一致 / IVT 不授 IDE 栈 / 程序集身份一致 / Arcade 依赖使拷贝直引不可行）、两模式唯一区别是 script mode、语言服务器独立进程 + dotnet tool 分发（LDM 拍板）、里程碑 M0–M4 | vbscript-lsp = **Active（Proposed）** |
| `meeting-shebang-directive.md` | 评估 `../proposals/proposal-shebang-directive.md`（`.vbx` 首行 `#!` shebang） | 深度评审：C# 对等语法 LDM 决策链（issue #3507 → LDM-2020-07-20/09-28 → C# 14 ignored-directives #8617，`#!`/`#:` 为 ignored 指令）、VB 落地表面（上游 `#R` 管道为模板、词法零改动、整行消费是唯一新增机械件）、模式门控（仅 script）、位置规则（首字符 + BOM 不能在前）、severity（spec warning vs 实现 error） | shebang-directive = **Active（Proposed）** |
| `meeting-consume-csharp-extension-and-interface-shared.md` | 评估 `../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（消费 C# 扩展成员与接口共享成员） | 深度评审（vblang 官方叙事结构）：四层延申消费扩展成员 + SAIM 约束接口绑定；**源码锚点逐一复核**并发现运行时钩子已在位（`RuntimeCapability.VirtualStaticsInInterfaces`）与 `ExtensionMarkerNameAttribute` 术语对齐；范围定案消费/声明分离；独立五维评审为 git-ignored 工作材料、不入库 | consume-csharp-extension-and-interface-shared = **Active** |
| `meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` | 评估 `../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（分发 fork 编译器：Toolset NuGet 包 + .net tool） | 深度评审（vblang 官方叙事结构）：分发现状源码核实（三库 IsPackable 无产物、MSBuildTask/VBCSCompiler 被裁剪、进程内编译回退在 `BuildClient.cs:148-165`）；四条 Unresolved 备选列全调查后闭合（U1 隐式分派 / U2 不补 VBCSCompiler / U3 v1 仅 .NET SDK / U4 双行版本）；发现 `vbi --version` 链路已就位（`Vbi.vb:31-50`）且版权文案已「不提及 Microsoft」；独立五维评审为 git-ignored 工作材料、不入库 | distribute-compiler-nuget-package-and-dotnet-tool = **Active** |
| `meeting-vbi-script-diag-mode.md` | 评估 `../proposals/proposal-vbi-script-diag-mode.md`（vbi 脚本模式诊断检查 `/check`） | 深度评审（vblang 官方叙事结构，多参会者流程）：现状缺口实证（成功路径警告被吞 / 错误中止 / vbc 专属开关 BC2007）；发现 `Script.Compile()` 现成 API + 交互模式同款编译前置已在使用；候选方案 A–E（`/check` 采用 vs `/diag` `/norun` `/analysis` 维持现状）；**命名裁决 `/diag`→`/check`**（AI 先验标准：`/diag` 撞 msbuild 详细日志、方向相反）；四 Unresolved 备选列全调查后闭合（U1 严重度配置=v1 不带 / U2 互斥=/check 优先 / U3 MSYS=排除转换 / U4 内存 emit=接受）；独立五维评审报告为 git-ignored 工作材料、不入库 | vbi-script-diag-mode = **Active** |
| `meeting-consume-ref-readonly.md` | 评估 `../proposals/proposal-consume-ref-readonly.md`（消费 C# `ref readonly` 返回） | 深度评审（vblang 官方叙事结构，多参会者流程）：根因收敛到一个 modreq（`CMOD_REQD(In) BYREF T`）；发现 C# 既有结构 `PEPropertySymbol.cs:363-367` 已白名单 InAttribute；候选方案 A–E；裁决：**统一豁免返回+参数双路径 In modreq**（csc 发射实证虚方法/委托 `in` 参数带 modreq）、直接赋值**复用 `ERR_LValueRequired`（30068）零新码**（显示退化自洽 + 30098→30068 既有迁移；拒绝面含复合赋值/Mid，VB 老登新增发现）、tooltip 显示退化 + `ByRef ReadOnly` 仅 debug 格式、丢弃写回接受（与字面量传 ByRef 一致）、`ReturnsByRefReadonly` 填既有占位、For Each 解锁、§4a v1 不做；C# 7.2 归属修正；独立五维评审为 git-ignored 工作材料、不入库 | consume-ref-readonly = **Active** |
| `meeting-vbi-nuget-reference.md` | 评估 `../proposals/proposal-vbi-nuget-reference.md`（.vbx/REPL `#R "nuget:"` NuGet 包引用） | 深度评审（vblang 官方叙事结构，双老登流程 + 复会 1/2）：**定性**=宿主/工具链契约而非 VB 语言特性（归 M5、进产品 spec 不进 vblang spec）；引擎 **C1 采用**（dotnet CLI 内容寻址临时工程 restore）、C2 与增量自解析否决；语法**逗号 + 大小写不敏感前缀**（附两义务：近失配显式诊断、共享层文档标注防 merge 误回滚）；**接线 R4 经复会 1 改判**——共享 Core 单 `#R` 只收 1 解析结果（`CommonReferenceManager.Resolution.cs:883-887` 上游 `// TODO: implement`），改为**扩展共享 Core 支持单 `#R`→N 引用**为 U9 spike 目标架构（受 spike 硬门控；不 alter 旧 `CSharpCompilation.GetDirectiveReference`、N 语义走新增 API/路径，产品 VB-only 故 C# 零 churn；spike 不过回退宿主拦截=干净剥行过渡）；宿主驱动环落点订正在共享 `CommandLineRunner.RunInteractiveLoopAsync`（`CommandLineRunner.cs:240-310`）；U1-B（锚点订正 `Scripting.Core.csproj:54-58`）/U2-A/U3-B/U4-A/U5-A/U6-A/U7-A/U8-A 采纳；**TFM 镜像经复会 2 收窄为只镜像运行宿主 TFM**（`' Attribute TargetFramework` 由 vbichooser 派发、vbi 引擎不读，TFM 镜像 spike 删除）；**U5 native 经复会 3**——net10 native 支持（sqlite 类先行 + Scripting loader seam `ResolvingUnmanagedDll` spike 验收；宿主供策略目录、机制落 loader seam），WindowsAppSDK 类 net10 后置，net48 范围外（R6 锚 `#R` 宿主能力诊断）；测试策略为新增非改断言；独立五维评审为 git-ignored 工作材料、不入库 | vbi-nuget-reference = **Active** |

## Inactive（`meetings/inactive/`）

| 文件名 | 对应提案 | 主题 | 三态判定 |
|--------|---------|------|---------|
| `inactive/meeting-vbi-console-completion.md` | 评估 `../proposals/inactive/proposal-vbi-console-completion.md`（vbi 控制台 REPL 行内补全） | 深度评审（vblang 官方叙事结构，双老登流程）：源码锚点整条复核命中；纠偏三处表述（Vbi.vb:86 输出侧≠输入侧谓词 / 多行态是真多行编辑器 / Windows-Unix 平台 ROI 不对称）+ dotnet/interactive ≈310 行 glue 改读为"工程级补全 = Features 层"反证；**裁决 Table**（此刻捆绑投入 L0+L1 不成立——补全能力将由已 Active 的 LSP 补层 / Consider 的 Avalonia/一进程编辑面承接）；L0/L1 拆分、剥离保留三件低成本资产、复活闸门 G1（现成读行库采纳）/G2（LSP 补层 M0/M1 落地接 Features top-1）/G3（浅档探针命中率/延迟基线）；修正点 a–f 记纪要不写回提案；独立五维评审为 git-ignored 工作材料、不入库 | vbi-console-completion = **Table（归档待触发）** |

## 会议纪要格式

- 标题：`# Visual Basic Language Design Meeting` + 日期行。
- 结构：开场白 → `## Agenda` → 各 Proposal 讨论（场景与缺口 / 候选方案 / 权衡 Q&A / RESOLUTION / 状态）→ 附录。
- 状态：每个讨论项给 **LDM 状态** + **三态判定**（仿 modvb meeting 写法）。
