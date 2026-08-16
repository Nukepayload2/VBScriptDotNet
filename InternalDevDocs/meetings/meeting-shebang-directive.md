# Visual Basic Language Design Meeting
August 15, 2026

本次会议是 `proposal-shebang-directive` 的专用 1:1 会议。承接 `proposal-vbscript-lsp.md` 的甄别结论——`#!` 被判定为**编译器语法特性（C# 有、VB 无），非 LSP 范畴**，若产品要支持 `.vbx` 的 `#!`，落点是 fork VB 编译器的 Parser/Scanner（一个编译器提案，与 `?` 前缀、byref-like 同类）。本次会议把这条线单独展开：回到源码核实 C# 的参考实现与 VB fork 的落地表面，逐条拍板（层级定位、模式门控、位置规则、行尾消费、`#Load` 分工、跨 LSP 影响），给出三态判定。

## Agenda

* [Proposal: 脚本文件 `#!` shebang 指令（编译器语法层）](#proposal-脚本文件--shebang-指令编译器语法层)

## Proposal: 脚本文件 `#!` shebang 指令（编译器语法层）

_Related: `../proposals/proposal-shebang-directive.md`（主检对象）；`../proposals/proposal-vbscript-lsp.md`（甄别结论：`#!` 属编译器语法特性，非 LSP 范畴；本会议兑现该结论的编译器侧落点）；C# 参考实现 `Compilers\CSharp\Portable\Parser\DirectiveParser.cs:111-120, 687-695` 与 `CSharp.Generated.g4:1312`；VB 侧上游模板 `Compilers\VisualBasic\Portable\Parser\ParseConditional.vb`（`#R` 管道）_

### 场景与缺口

- **Linux 执行 vbx 不可或缺**：POSIX shebang（`#!`）是可执行脚本的标准机制，内核根据首行选择解释器。deb 包装把 `vbi` 装到 `/opt/vbi-n2fork/vbi` 后，脚本首行即 `#!/opt/vbi-n2fork/vbi`。这是 Linux 上分发 `.vbx` 可执行脚本的必经路径，不可绕过。
- **今天做不到**：`.vbx` 首行 `#!...` 会被 VB 编译器当作条件编译指令解析失败（`#` 后跟 `!`，无对应分支 → `ParseBadDirective`）→ 编译错误，Linux/macOS 直接执行 `.vbx` 无从谈起。
- **消费者的广度**：`#!` 的消费者不只是 `vbi`——任何解析该文件的程序（LSP、编辑器、语法高亮）都必须认识它，否则在 Linux 上打开一个带 shebang 的 `.vbx` 就满屏错误。这决定了层级定位（见候选方案）。

### 现状机制（源码核实）

#### C# 参考实现（本会议的行为基准）

- **文法**：`shebang_directive_trivia : '#' '!' end_of_directive`（`Compilers\CSharp\Portable\Generated\CSharp.Generated.g4:1312`）。
- **派发**：`#` 后下一 token 是 `ExclamationToken` 时**恒**按 shebang 解析；`hashPosition != 0 || hash.HasTrailingTrivia` 时报 `ERR_PPShebangNotOnFirstLine`（9378）（`DirectiveParser.cs:111-120`）。
- **模式门控**：`SourceCodeKind != Script && !FileBasedProgram` 时报 `ERR_PPShebangInProjectBasedProgram`（9314）（`ParseShebangDirective`，`DirectiveParser.cs:687-695`）。
- **节点与行尾**：返回 `ShebangDirectiveTrivia(hash, exclamation, endOfDirective, isActive)`；`#!` 之后整行经 `ParseEndOfDirectiveWithOptionalPreprocessingMessage` 消费为 trivia——路径文本不解析、不参与任何语义（`:694`）。

#### C# 对等语法的 LDM 决策链（引用来源）

C# 侧 shebang 的 LDM 会议纪要（`..\csharplang` 镜像逐字核实）：

| 日期 | 会议 / 文档 | 决策 |
|------|------------|------|
| 2020-07-20 | `meetings\2020\LDM-2020-07-20.md:45-60`（issue #3507） | "This is part of the next step in the C# scripting discussion… the ability to have a C# file startup an environment… There is support among LDT members for this scenario"——triage 进 C# 10 讨论，明确「可能 C# 10 不发布任何东西，但要在未来数月开始讨论可能的方向」 |
| 2020-09-28 | `meetings\2020\LDM-2020-09-28.md:89-98`（issue #3507） | 改判 X.0："While [it] could eventually be an interesting proposal, the tooling is not there currently… if the .NET tooling looks to add `dotnet run csfile`, we can consider again"——以 `dotnet run csfile` 工具落地为再议条件 |
| C# 14 | `proposals\csharp-14.0\ignored-directives.md`（champion #8617，正式落地设计） | `#!` 与 `#:` 同为 **ignored preprocessing directive**（`PP_Ignored : PP_IgnoredToken Input_Character*`，`PP_IgnoredToken : '!' | ':'`）；语言忽略、编译器与工具可识别（先例：`#region` / `#pragma` / `#error version`）；`#!` 须在首个 token 之前、且**文件第一行的第一个字符（连 BOM 都不能在前）**，否则报 **warning**（因为 shell 不认）；project-based program 中可报 error，且**明确考虑过不为 `#!` 报错**（"it might invoke some other tool than `dotnet run`"） |

**脉络与对 VB 的启示**：shebang 本身是「loose files / file-based programs」现代化的一块小拼图（LDM-2020-07-20 原文 "just one, very small piece of the puzzle"），**工具链先落地、语言后跟进**（`dotnet run file.cs` 是再议条件）——与 VBScript.NET 的 `.vbx` 脚本执行（`vbi script.vbx`）同构。`#!` 的语义是**忽略的预处理指令**：语言不解释它、路径文本整行作为 trivia——正是本提案的设计。severity（非首行 warning vs error）留待本会议定（见 Q&A / OPEN QUESTIONS）。

#### VB fork 落地表面（全部已核实）

1. **`#R` 是上游已有指令管道，可直接作模板**。基线 `{{Roslyn}}` 的 VB 编译器自带完整 `#R`：`ReferenceDirectiveTriviaSyntax`（`Syntax.xml:9540`）、派发 `Case SyntaxKind.ReferenceKeyword`（`ParseConditional.vb:82-83`）、`ParseReferenceDirective`（`:448`）、script-only 门控 `If Not IsScript Then AddError(ERR_ReferenceDirectiveOnlyAllowedInScripts)`（`:456-458`）、错误码 `36964`（`Errors.vb:1593`）。`#!` 扩展的是**上游既有机制**，非 fork 发明。
2. **词法无需改动**：`#` 经 `ScanDateLiteral`（`Scanner\Scanner.vb:1171-1173`）对 `#!` 失败（`!` 非日期字符）回退 `MakeHashToken`；`!` 单独词法化为 `ExclamationToken`（VB 字典访问符既有 token）。`#!` 正确产出 `HashToken` + `ExclamationToken` 序列。
3. **`ConsumeStatementTerminatorAfterDirective` 会报行尾残留**：VB 解析器在 `TryScanDirective` 后调用它（`Scanner\Directives.vb:57`），对指令行遗留的多余 token 报 `ERR_ExpectedEOS`（`Parser\Parser.vb:5774-5793`）。含义：若 `ParseShebangDirective` 不显式吞行，`/opt/vbi-n2fork/vbi` 会被词法化为 `/`（除号）、`opt`、`vbi` 等 token 留下 → 触发 `ERR_ExpectedEOS`。**这是 VB 侧唯一新增机械件**，对应 C# `ParseEndOfDirectiveWithOptionalPreprocessingMessage`。
4. **`#Load` 是宿主层预处理，不构成先例**：`#Load` 由 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `ExpandLoadDirectives`（`:54-158`）内联删除，不进编译器。`#R` 与 `#!` 是编译器级；`#Load` 留在宿主（执行期文件内联语义）。
5. **脚本模式自动生效**：`vbi` 跑 `.vbx` 走 `VisualBasicScriptCompiler.CreateSubmission`（`:215`，`kind:=SourceCodeKind.Script`），编译器接受 `#!` 即零宿主改动生效。REPL 提交同为 `SourceCodeKind.Script`。

### 候选方案

**PROPOSAL A — 编译器语法层（镜像 C#，本提案定论）。** 新 `ShebangDirectiveTriviaSyntax` 节点 + `ParseConditional.vb` 派发 + `ParseShebangDirective`（`IsScript` 门控 + 首行检查 + 整行消费）+ 错误码。`#!` 成为 script 模式语法树的一部分（trivia），所有消费者（vbi / LSP / 编辑器）共享同一棵树，行号不漂移。**判定：采纳。**

**PROPOSAL B — 宿主剥行（`vbi` 读文件时删首行 `#!`）。** 改动最小（几行），但：诊断行号整体漂移 1 行（`.vbx` 是错误上报场景，不可接受）；只修 `vbi`——编辑器/LSP 打开同一文件仍报错，Linux 开发体验断裂；与 `#R`（编译器级）层级不一致。**判定：否决。**

**PROPOSAL C — 扫描器整体词法化（`#!...EOL` 直接作单个 comment 式 trivia token）。** 可行但偏离 C# 结构，丢失指令节点形态（`GetDirectives` 无法发现），且词法化原始文本难以复用。**判定：否决。**

**PROPOSAL D — 不支持。** Linux/macOS 直接执行 `.vbx` 无从谈起（deb 包装首行必然编译报错），与动机直接冲突。**判定：否决。**

### 权衡：Q&A

- **为什么语法层而非宿主剥行？** 剥行只解决 `vbi` 一个消费者，且行号漂移损坏诊断定位；语法层让 `#!` 进入所有解析器共享的语法树（编译器、LSP、编辑器同一棵树），行号保持正确。C# 的选择就是语法层——`.csx`/`dotnet-script` 的 `#!` 是编译器 trivia，编辑器零诊断。
- **为什么对齐 `#R` 的门控模式？** `#R` 是上游 VB 已实现的「仅 scripts 允许」指令，`IsScript` 门控 + 专属错误码是现成范式；`#!` 语义上同为脚本特性，复用同一门控，常规 `.vb` 编译报错，不污染项目编译。
- **`#!` 与 `#R` 的差异点在哪？** `#R` 要求 `StringLiteralToken`（`#R "..."` 引号包裹）；`#!` 的路径是**裸文本**（`#!/opt/vbi-n2fork/vbi` 无引号），不能复用字符串字面量模式，必须整行吞为 trivia——这就是新增机械件。
- **整行消费的实现细节？** 跳过 token 直到 `StatementTerminatorToken`，跳过的内容作为 `ExclamationToken` 的尾随 trivia。不做任何语义解释（路径是给内核看的，不是给编译器的）。
- **与 LSP 提案的关系？** `proposal-vbscript-lsp.md` 把 `#!` 判为非 LSP 范畴；本提案兑现编译器侧，LSP 的 script mode 语义模型**自动继承** `#!`——Linux 上带 shebang 的 `.vbx` 在编辑器/LSP 零诊断，LSP 侧零工作。甄别闭环。
- **REPL 里 `#!` 算什么？** 每个 REPL 提交也是 `SourceCodeKind.Script`；首字符 `#!` 按 trivia 接受（无害空转），与 C# `csi` 一致。不新增「非文件场景」诊断（见 OPEN QUESTIONS）。
- **severity：非首行违规是 warning 还是 error？** C# spec（`ignored-directives.md`）写的是 **warning**——"report a warning if the `#!` directive is not placed at the first line and the first character in the file (not even a BOM marker can be in front of it), because otherwise shells won't recognize it"——语义是「代码本身无害，只是 shell 不认」。但 Roslyn 实现用 `ERR_PPShebangNotOnFirstLine`（`DirectiveParser.cs:116` 的 `AddError`，code 9378）。本会议倾向**镜像 Roslyn 实现（error）**：`.vbx` 里 `#!` 不在首行几乎必是用户错误，报 error 更干净；C# spec 的 warning 立场记入 OPEN QUESTIONS 供实现时复核。同样地，C# 对 project-based 报错**明确说过对 `#!` 可以豁免**——VB 侧沿用 `#R` 的 `ERR_ReferenceDirectiveOnlyAllowedInScripts`（error）更符合既有门控模式，但该豁免意图值得记录。
- **编辑器渲染：`#!` 用预编译指令色还是注释色？** **注释色**。C# 落地先例（权威，源码核实）：`Workspaces\CSharp\Portable\Classification\Worker.cs:207-210` 把 `ShebangDirectiveTrivia` 与 `//`、`/* */` 注释**归为一组 → `ClassificationTypeNames.Comment`**（整行含路径）；对照 `#:` ignored 指令因带真实工具内容走 `PreprocessorKeyword`（`Worker_Preprocesser.cs:332-350`）。`REM` 先例同构：VB 里 `REM` 词法即 `CommentTrivia`（注释，非关键字），天然注释色——`#!` 整行是「语言不解释的忽略内容」，注释色最贴切。**VB 实现含义**：`Worker.ClassifyTrivia`（`Workspaces\VisualBasic\Portable\Classification\Worker.vb:115-150`）按 `HasStructure` 分派，新节点目前无 case 命中会不着色——必须新增 `Case SyntaxKind.ShebangDirectiveTrivia → Comment`（放注释组，不放指令组）。此代码在 Workspaces 层，是 LSP 提案复制该层时的**一个具体适配点**。

### 深度追问：LDM 拷问清单

1. **`#If False` 禁用区内的 `#!`？** C# 的 `ShebangDirectiveTriviaSyntax` 带 `IsActive`；禁用区 shebang 是否豁免首行错误需对齐 C# `IsActive` 语义。实现时对照（`DirectiveTriviaSyntax.cs:57` 的 DirectiveKind switch 同步更新）。
2. **BOM / 编码与「位置 0」？** 文件首字符含 UTF-8 BOM 时 `#!` 是否仍算首行？C# 的 `hashPosition` 相对树起点，需在实现时核对 VB 解析器 token 偏移语义是否同构。
3. **错误码命名？** 镜像 C# 语义命名（`ERR_ShebangDirective...`）还是沿用 `ERR_ReferenceDirectiveOnlyAllowedInScripts` 措辞风格（`ERR_ShebangDirectiveOnlyAllowedInScripts`）？倾向后者（VB 风格统一）。
4. **测试纪律？** 无副作用单测（CLAUDE.md 规约）：走 `Scripting\VisualBasicTest\`；不得发起网络/文件写入/进程启动/注册表写入。编译诊断测试复用编译器测试基类。

### RESOLUTION:

1. **方向判定：采纳，Active（Proposed）。** `#!` shebang 在**编译器语法层**实现（镜像 C#）：新 `ShebangDirectiveTrivia` 指令 trivia 节点，`#!` 后整行吞为 trivia。理由：`#!` 消费者是所有解析文件的程序（vbi / LSP / 编辑器），语法树共享 + 行号不漂移；宿主剥行（PROPOSAL B）只修 `vbi` 且行号漂移，否决。
2. **模式门控：仅 script 模式（`IsScript`）。** 常规 `.vb` 编译出现 `#!` 报错（镜像 C# `ERR_PPShebangInProjectBasedProgram`，复用上游 `#R` 的 `ERR_ReferenceDirectiveOnlyAllowedInScripts` 门控模式）。
3. **位置规则：仅文件首字符。** 位置 0、`#` 无前导 trivia、**连 BOM 都不能在前**（镜像 C# 14 `ignored-directives.md`）；违规 severity 倾向镜像 Roslyn 实现的 error（`ERR_PPShebangNotOnFirstLine`），C# spec 的 warning 立场记入 OPEN QUESTIONS 复核。
4. **整行消费是唯一新增机械件。** `ParseShebangDirective` 显式把 `#!` 后路径吞为尾随 trivia，对应 C# `ParseEndOfDirectiveWithOptionalPreprocessingMessage`；否则撞 `ConsumeStatementTerminatorAfterDirective` 的 `ERR_ExpectedEOS`（`Parser.vb:5774-5793`）。路径是裸文本，不复用 `#R` 的字符串字面量模式。
5. **指令三分不变**：`#R`（编译器）+ `#Load`（宿主预处理 `VisualBasicScriptCompiler.ExpandLoadDirectives`）+ `#!`（编译器）。`#Load` 留在宿主，`#!` 必须进编译器。
6. **跨提案：LSP 零工作。** `#!` 成为 script 模式语法树的一部分，`proposal-vbscript-lsp` 的语义模型自动继承——Linux 上带 shebang 的 `.vbx` 编辑器/LSP 零诊断。甄别闭环。
7. **里程碑：M0（Syntax.xml 节点 + 重新生成 3 个生成文件）→ M1（`ParseConditional.vb` 派发 + `ParseShebangDirective` + 错误码）→ M2（测试 + BOM/REPL/`#If False` 边界 + 与 LSP script 树交叉验证）。**
8. **编辑器渲染：`#!` 整行 → 注释色（对齐 C#）。** C# 把 `ShebangDirectiveTrivia` 与 `//`、`/* */` 归为一组 → `Comment`（`Worker.cs:207-210`），`#:` 才走 `PreprocessorKeyword`。VB 侧在 Workspaces 层 `Worker.ClassifyTrivia` 新增 `Case SyntaxKind.ShebangDirectiveTrivia → Comment`（放注释组）。**这同时是 LSP 提案复制 Workspaces 层时的一个已定位适配点**，纳入 LSP M0/M1 适配点清单。

### Implication:

- **提案状态**：`../proposals/proposal-shebang-directive.md` 判 **Active**，归 active 根目录；`../proposals/README.md` 索引与提案文件状态行的更新由验证/调度阶段处理。
- **对 LSP 提案的意义**：`proposal-vbscript-lsp.md` 甄别表里 `#!` 的「编译器语法特性（C# 有、VB 无），非本提案范畴」条目，由本提案闭环——语义层面 LSP 零工作（script 树自动继承）；但复制 Workspaces/Features 层时有一个**已定位的具体适配点**：`Worker.ClassifyTrivia` 给 `ShebangDirectiveTrivia` 接 `Comment` 分类（见 RESOLUTION #8），纳入 LSP M0/M1 适配点盘点。
- **对编译器增值面的影响**：新节点 `ShebangDirectiveTriviaSyntax` 进公开 API（`PublicAPI.Unshipped.txt` 段）——fork 与基线 `PublicAPI.Shipped.txt` 字节一致，新增走 Unshipped 与既有扩展同一路径，不破坏 parity。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：BOM 与「位置 0」语义（C# spec 明说「连 BOM 都不能在前」，实现时按此核对）；REPL 首行 `#!` 接受为无害 trivia vs 新增「非文件场景」诊断（倾向镜像 C#，接受并空转）；错误码命名（倾向 `ERR_ShebangDirectiveOnlyAllowedInScripts` / `ERR_ShebangDirectiveNotOnFirstLine`）；`#If False` 禁用区 shebang 的 `IsActive` 行为；**severity**——非首行按 C# spec 是 warning（shell 不认、代码无害）、按 Roslyn 实现是 error（`ERR_PPShebangNotOnFirstLine`），以及 C#「对 `#!` 的 project-based 报错可豁免」的意图——镜像 Roslyn（error）还是 spec（warning）？
- `TODO`：M0——`Syntax.xml` 加 `ShebangDirectiveTriviaSyntax`（child: `ExclamationToken`）+ 重新生成 `Syntax.xml.Syntax/Main/Internal.Generated.vb`。
- `TODO`：M1——`ParseConditional.vb` 加 `Case SyntaxKind.ExclamationToken` 派发 + `ParseShebangDirective`（`IsScript` 门控 + 首行检查 + 整行消费）+ `Errors.vb`/`ErrorFacts.vb` 错误码。
- `TODO`：M2——无副作用单测（首行通过、非首行报错、前置空白报错、常规模式报错、任意路径文本、与 `#R`/`#Load` 共存）+ BOM/`#If False` 边界 + LSP script 树交叉验证。
- `TODO`：Workspaces 分类适配点——`Worker.ClassifyTrivia` 加 `Case SyntaxKind.ShebangDirectiveTrivia → ClassificationTypeNames.Comment`（对齐 C# `Worker.cs:207-210`）；随 LSP 提案复制 Workspaces 层时落地，进 LSP M0/M1 适配点清单。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active（Proposed）**——方向成立（编译器语法层，镜像 C#）、全部方向问题定论（层级、门控、位置、行尾消费、`#Load` 分工、LSP 零工作）、实现表面源码证据充分（上游 `#R` 管道为模板、词法零改动、唯一新增机械件是整行消费、脚本模式零宿主改动）。归 active 根目录，进入实现规划（M0 → M1 → M2）。

---

## 附录：C# vs VB fork 落点对照

| 环节 | C#（参考基准） | VB fork（本提案落点） |
|------|---------------|----------------------|
| 文法/节点 | `shebang_directive_trivia`（`CSharp.Generated.g4:1312`）+ `ShebangDirectiveTriviaSyntax` | 新 `ShebangDirectiveTriviaSyntax`（`Syntax.xml`，child: `ExclamationToken`） |
| 派发 | `#` + `ExclamationToken` → `ParseShebangDirective`（`DirectiveParser.cs:111-120`） | `ParseConditional.vb` 的 `Select Case` 加 `Case SyntaxKind.ExclamationToken`（模板：`ReferenceKeyword` @ `:82-83`） |
| 模式门控 | `ERR_PPShebangInProjectBasedProgram`（9314） | 复用 `IsScript` + `ERR_ReferenceDirectiveOnlyAllowedInScripts` 模式（`ParseConditional.vb:456-458`） |
| 位置规则 | `ERR_PPShebangNotOnFirstLine`（9378） | 新增对等错误码（位置 0、无前导 trivia） |
| 行尾消费 | `ParseEndOfDirectiveWithOptionalPreprocessingMessage`（`:694`） | 新增整行消费（否则撞 `ERR_ExpectedEOS`，`Parser.vb:5774-5793`） |
| 词法 | 语法内 `'#' '!'` | **零改动**（`ScanDateLiteral` 对 `#!` 失败回退 HashToken；`!` 词法化为 ExclamationToken） |
| 宿主 | csi/dotnet-script 共享编译器树 | **零改动**（`vbi` 走 `CreateSubmission` Script 模式自动生效） |
