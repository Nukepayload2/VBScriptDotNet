# 概要设计：`.vbx` 首行 `#!` shebang 指令

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-shebang-directive.md`（Active）→ `../../meetings/meeting-shebang-directive.md`（Active，RESOLUTION #1-#8，OPEN QUESTIONS 已由用户 2026-08-16 定案）。
> 本设计吸收会议 RESOLUTION 与用户定案，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。拿不准的策略对照 C# 实现（`DirectiveParser.cs` / `Lexer.cs` / C# 测试）。

## 1. 背景与目标

**目标（一句话）**：`.vbx` 脚本首行 `#!/opt/vbi-n2fork/vbi` 由 fork 编译器在**语法层**解析为一条指令 trivia 并忽略，使 Linux/macOS 内核通过 POSIX shebang 机制直接执行 `.vbx`（`vbi script.vbx` 路径；deb 包装装到 `/opt/vbi-n2fork/vbi` 后脚本首行即解释器路径）。

- **现状缺口**：`.vbx` 首行 `#!...` 被 VB 编译器当条件编译指令解析失败（`#` 后跟 `!` 无对应分支 → `ParseBadDirective`，`ParseConditional.vb:491`）→ 编译错误，Linux 直接执行 `.vbx` 无从谈起。
- **状态**：提案与专用 LDM 会议均为 **Active**；会议采纳 PROPOSAL A（编译器语法层，镜像 C#），RESOLUTION #1-#8；OPEN QUESTIONS 已由用户定案（error severity / fork 错误码命名 / 仅 script 门控 / 加 Content API / REPL·BOM·`#If False`），其中 BOM 与 `#If False` 边界在 M2 按 VB 实测收敛（不识别、无节点无诊断，见第 6、7 节）。

**用户定案吸收映射（2026-08-16）**：

| 定案 | 内容 | 落在本设计 |
|------|------|-----------|
| severity | **error**（镜像 Roslyn 实现，C# spec 的 warning 文字不采用） | 第 3、4 节 |
| 错误码命名 | fork 风格 `ERR_ShebangDirectiveOnlyAllowedInScripts` / `ERR_ShebangDirectiveNotOnFirstLine`（37003/37004） | 第 3 节 |
| 模式门控 | **仅 script**（遵循 `#R` 先例，不引入 FileBasedProgram 特性标志） | 第 2、4 节 |
| Content API | **加**（镜像 C# `Content`/`WithContent`） | 第 2、5 节 |
| REPL/BOM/`#If False` | **遵循 C#；M2 实测收敛**（REPL 空转；BOM/禁用区按 VB 实测不识别，test-plan L2-9/L2-10） | 第 5、6、7 节 |

## 2. 总体架构（三层落点）

**核心观察**：`#!` 的消费者不只是 `vbi`——任何解析该文件的程序（LSP、编辑器、语法高亮）都必须认识它。因此必须把 `#!` 做成**语法树的一部分（trivia）**，所有消费者共享同一棵树，行号不漂移。宿主剥行（PROPOSAL B）只修 `vbi` 且行号漂移，否决。三层落点如下。

### 2.1 Syntax 层（M0，`Compilers\VisualBasic\Portable\Syntax\`）

- 在 `Syntax.xml` 新增 `ShebangDirectiveTriviaSyntax`（parent `DirectiveTriviaSyntax`，child `ExclamationToken`）——模板：`LoadDirectiveTriviaSyntax` @ `:9552`。**VB 指令节点无 `EndOfDirectiveToken`**（C# 专属），路径文本放 `ExclamationToken` 的尾随 trivia（`SkippedTokensTrivia`，VB 无 C# `PreprocessingMessageTrivia` 机制）。
- 用基线 `{{Roslyn}}` 的 VBSyntaxGenerator 再生成 3 个 `Generated\Syntax.xml.*.Generated.vb`（零删除、纯新增，`#Load` 先例 `issues\issue-vbx-load-span-shift.md:82`）。
- 手写 partial `ShebangDirectiveTriviaSyntax.vb` 加 `Content`/`WithContent`（路径作 `StringLiteralToken`，镜像 C# `ShebangDirectiveTriviaSyntax.cs`）。
- 公开 API 增量进 `PublicAPI.Unshipped.txt`（当前为空）；`Shipped.txt` 字节不变。

### 2.2 Parser 层（M1，`Compilers\VisualBasic\Portable\Parser\`）

- **派发**：`ParseConditionalCompilationStatement`（`ParseConditional.vb:22`）外层 `Select Case CurrentToken.Kind`（`:41`）加 `Case SyntaxKind.ExclamationToken` → `ParseShebangDirective(hashToken, isFollowingToken)`。`#` 后 `!` 是标点 token，不走 identifier/keyword 路径（对照 `#Load` 的 `Case SyntaxKind.IdentifierToken` → `LoadKeyword`，`:85-86`）。
- **`ParseShebangDirective` 三步**：
  1. `IsScript` 门控 → 非 script 报 `ERR_ShebangDirectiveOnlyAllowedInScripts`（error，镜像 `ParseLoadDirective` :479-483）；
  2. 位置检查 → 非首字符报 `ERR_ShebangDirectiveNotOnFirstLine`（error，镜像 C# `hashPosition != 0 || hash.HasTrailingTrivia`）；
  3. **整行消费（唯一新增机械件）**：跳过 token 直到 `StatementTerminatorToken`，路径文本作 `SkippedTokensTrivia` 尾随 `ExclamationToken`。否则残留 token 撞 `ConsumeStatementTerminatorAfterDirective`（`Parser.vb:5775-5794`）的 `ERR_ExpectedEOS`。
- **即使违规仍解析为 shebang**（镜像 C#：树结构完整、路径照常消费），severity 由错误码体现——**例外**：BOM 残留源码文本（`U+FEFF#!`）与 `#If False` 禁用区内 `#!` 不被识别为指令（无节点、无诊断，test-plan L2-9/L2-10）。

### 2.3 宿主层（零改动）

- `vbi` 跑 `.vbx` 走 `VisualBasicScriptCompiler.CreateSubmission`（`:143`，`kind:=SourceCodeKind.Script`），编译器接受 `#!` 即自动生效。`#Load` 多树加载（`LoadReferencedTrees`，`:56`）不受影响。REPL 提交同为 Script，首行 `#!` 接受并空转（遵循 C#）。

## 3. 行为对照表

| 输入（`.vbx` / 提交） | 现状 | 提案后 |
|------|------|--------|
| `#!/opt/vbi-n2fork/vbi`（首行）+ 正常代码 | 编译错误（`#!` 无对应分支 → `ParseBadDirective`） | **通过**；`#!` 解析为 `ShebangDirectiveTrivia`，行号不漂移 |
| `#!` 在第二行 | 编译错误 | **error** `ERR_ShebangDirectiveNotOnFirstLine`，仍解析为 shebang |
| `  #!/...`（前置空白） | 编译错误 | **error**（位置非 0），仍解析 |
| `# !/...`（`#` 与 `!` 间隔空格） | 编译错误 | **error**（`#` 有尾随 trivia，镜像 C# `ShebangWithTriviaInBetween`），仍解析 |
| 文件首字符含 UTF-8 BOM + `#!` | 编译错误 | **泛化解析错误**（BOM 作为字符 U+FEFF 不被识别为指令、无 shebang 节点 → BC30037/BC30201，test-plan L2-9；文件读入路径 BOM 被解码吞掉时 `#` 仍位置 0 → 通过） |
| 常规 `.vb` 编译（非 script）含 `#!` | 编译错误 | **error** `ERR_ShebangDirectiveOnlyAllowedInScripts`（不引入 FileBasedProgram 标志） |
| `//#!/...`（注释内） | 就是注释 | **不变**（纯注释，无 shebang） |
| REPL 首行 `#!` | 编译错误 | **接受并空转**（无害 trivia，遵循 C# `csi`） |
| `#x`（`#` 后非 `!`） | `ParseBadDirective` | **不变**（`ERR_ExpectedConditionalDirective`，C# 对等 CS1003） |

## 4. 判定原则

- **模式门控 = 仅 `IsScript`**（`If Not IsScript Then AddError(ERR_ShebangDirectiveOnlyAllowedInScripts)`），复用 `#R`/`#Load` 门控模式。VB 无 C# `FileBasedProgram` 概念，`.vbx` 恒为 Script → 零宿主改动自动生效；常规 `.vb` 报错。
- **位置规则 = `#` 是文件首字符**：位置 0、无前导 trivia、连 BOM 不能在前（镜像 C# `hashPosition != 0 || hash.HasTrailingTrivia`）。severity **error**（镜像 Roslyn 实现，非 C# spec 的 warning）。
- **整行消费 = 路径作 trivia 不参与语义**：裸文本（非字符串字面量），不复用 `#R` 的 `StringLiteralToken` 模式。
- **Content API = 镜像 C#**：`Content` 把 `ExclamationToken` 尾随 trivia（路径文本）作 `StringLiteralToken` 暴露，`WithContent` 可改写。

## 5. C# 先例与跟随设定

- **C# 机制**：`#`+`ExclamationToken` 恒按 shebang 解析（`DirectiveParser.cs:111-120`）；`hashPosition != 0 || hash.HasTrailingTrivia` → CS9378（error，`:116`）；`SourceCodeKind != Script && !FileBasedProgram` → CS9314（error，`:691`）；路径经 `LexEndOfDirectiveWithOptionalPreprocessingMessage`（`Lexer.cs:2485-2534`）在 **lexer 层**作 `PreprocessingMessageTrivia` 消费；节点带 `Content`/`WithContent`。
- **VB 跟随同一语义，但机制不同**：
  - C# 的 `hashPosition`（lexer 绝对位置）→ VB 需在 parser 定位 `#` 的绝对位置；`DirectiveIsFollowingToken`（`_leadingTriviaStartOffset > 0`，`Directives.vb:36`）只覆盖「非首 token 的 leading trivia」，**不覆盖**「文件首 token 的 leading trivia 内有空白/换行」（如文件以 `\n` 开头）——确切 VB 位置判定留 F2 定（候选见 README）。
  - C# 的 `PreprocessingMessageTrivia`（lexer 级整行消费）→ VB 无此机制，用 **parser 级 skip** + `SkippedTokensTrivia`（`Syntax.xml:9169`）承载路径，语义等价。
- **不可盲目照搬 C#**：VB 指令节点无 `EndOfDirectiveToken`、无 lexer 预处理消息模式、无 FileBasedProgram——照搬错位，需按 VB 既有机制落地（详见 README 差异）。

## 6. 代价与边界

- **`.vbx` 首行行为变化（预期内）**：首行 `#!` 从「编译错误」变「合法 trivia」——这是特性本身的目标；常规 `.vb` 仍报错，不污染项目编译。
- **行号不漂移**：`#!` 作为 trivia 保留在树里，后续诊断行号不变——这是「编译器层而非宿主剥行」的关键收益（`.vbx` 是错误上报场景）。
- **REPL 首行 `#!` 空转**：接受为无害 trivia，不执行任何事（遵循 C# `csi`），不新增「非文件场景」诊断。
- **`#If False` 禁用区**：禁用区内 `#!` 不被识别为指令（无节点、无诊断）；位置/模式检查仅对被识别的指令（激活区）生效，激活区错位仍报 `NotOnFirstLine`。该边界 C# 侧无对等测试，VB 现状为不识别（test-plan L2-10）。
- **编辑器渲染（Workspaces 层，随 LSP 落地）**：`#!` 整行注释色（镜像 C# `Worker.cs:207-210` 归 `Comment` 组）。`Worker.vb` 不在当前 fork 树（fork Workspaces 仅 `SharedUtilitiesAndExtensions\Compiler\Core\`），随 LSP 提案复制 Workspaces 层时落地；共享层已有 `ISyntaxKinds.ShebangDirectiveTrivia`（`int?`）＋ `IsShebangDirectiveTrivia` ＋ `AbstractFileBannerFacts` 钩子，VB `SyntaxKinds` 需暴露新 kind。

## 7. 决策记录（原未决问题，2026-08-16 全部定案）

- **severity（已定案）**：**error**（镜像 Roslyn 实现；C# spec `ignored-directives.md:63-69` 的 warning 文字与「对 `#!` 豁免 project-based 报错」意图均不采用）。
- **模式门控（已定案）**：**仅 script**（遵循 `#R` 先例）；不引入 C# `FileBasedProgram` 特性标志（VB 无对应概念）。
- **错误码命名（已定案）**：fork 风格 `ERR_ShebangDirectiveOnlyAllowedInScripts` / `ERR_ShebangDirectiveNotOnFirstLine`（37003/37004，对齐 `ERR_LoadDirectiveOnlyAllowedInScripts = 36967`）。
- **Content API（已定案）**：**加**，镜像 C# `Content`/`WithContent`。
- **REPL（已定案）**：首行 `#!` 接受并空转，遵循 C# `csi`。
- **BOM（已定案，M2 实测收敛）**：文件读入路径 BOM 被解码吞掉时 `#` 仍位置 0；BOM 作为字符（U+FEFF）留在源码文本时不被识别为指令（无节点、泛化解析错误 BC30037/BC30201，test-plan L2-9）。
- **`#If False`（已定案，M2 实测收敛）**：禁用区内 `#!` 不被识别为指令（无节点、无诊断）；位置/模式检查仅对被识别的指令（激活区）生效（test-plan L2-10）。
- **残留验证点**：位置 0 判定的确切 VB 机制（候选 `_leadingTriviaStartOffset` + 指令前 whitespace 消费）留 F2 详细设计 + M1 实现源码核实。

> 记录：本节原为 OPEN QUESTIONS（meeting 记录），2026-08-16 用户定案全部关闭。
