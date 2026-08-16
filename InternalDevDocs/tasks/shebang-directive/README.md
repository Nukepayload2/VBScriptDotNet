# 任务：`.vbx` 首行 `#!` shebang 指令（shebang-directive）设计任务

本文件夹是 `proposal-shebang-directive` 的设计任务存储（Vortex 代办列表 + 设计产物）。

- **依据链**：`../..\proposals\proposal-shebang-directive.md`（提案，Active）→ `../..\meetings\meeting-shebang-directive.md`（LDM 会议，RESOLUTION #1-#8，**Active**）→ `../..\compilers-index.md`（编译器索引）→ `../..\decisions.md`（D4 优先级判定）。
- **交付物**：概要设计、详细设计、测试计划、实现、spec（`../..\spec\`）。全部要求无副作用测试。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/`。
- **拿不准的策略看 C#**（用户指令 2026-08-16）：C# 参考实现是行为基准（`Compilers\CSharp\Portable\Parser\DirectiveParser.cs:36,111-120,687-695`、`Lexer.cs:2485-2534`、`ShebangDirectiveTriviaSyntax.cs`、`IgnoredDirectiveParsingTests.cs`/`ScriptParsingTests.cs` 测试）。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`） |
| F4 | M0 实现：Syntax 节点 + 再生成 + Content API | 编译零错误；`ShebangDirectiveTriviaSyntax` 进公开 API；3 个 Generated.vb 零删除纯新增 | **done**（编译零错误；节点 + `Content`/`WithContent` 进 `PublicAPI.Unshipped.txt`；3 个 Generated.vb 零删除纯新增） |
| F5 | M1 实现：派发 + ParseShebangDirective + 错误码 | 解析行为对齐 C#（位置/门控/整行消费/severity）；错误码 37003/37004 | **done**（`Case SyntaxKind.ExclamationToken` 派发 + `ParseShebangDirective` 三步（门控/位置 0/整行消费）+ `ConsumeShebangContentAsTrailingTrivia`；37003/37004 error 落地） |
| F6 | M2 测试实现 + 修复轮 | test-plan 四层全绿；`Scripting\VisualBasicTest` 跑程序集 `-automated` | **done**（`VisualBasicSyntaxTest` 新增 18 个 `ShebangDirectiveParsingTests` 全绿；`Scripting\VisualBasicTest` shebang 用例 7 个全过（L3 3 + L4 4）） |
| F7 | 集成验证 + 文档 | 七门 gate 全绿；spec + proposals/README 索引 + p1-immediate + upstream-merge 登记 | **done**（Syntax 门基线更新 4070/4067/3/0；spec `../..\spec\spec-shebang-directive.md` + proposals/README 索引 + p1-immediate + upstream-merge + 提案/会议状态行全部登记） |

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-16）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。C# 参考实现位置同样给出。

### 设计定案（用户 2026-08-16，遵循 C# 实现）

1. **severity = error**（镜像 Roslyn 实现；C# spec 的 warning 文字不采用）。两枚错误码：`ERR_ShebangDirectiveOnlyAllowedInScripts`（模式门控）、`ERR_ShebangDirectiveNotOnFirstLine`（位置违规）。
2. **模式门控：仅 script（`IsScript`）**，遵循 `#R` 先例；不引入 C# 的 `FileBasedProgram` 特性标志（VB 无该概念，`.vbx` 恒为 `SourceCodeKind.Script`）。
3. **位置规则：`#` 必须是文件首字符**（位置 0、无前导 trivia、连 BOM 都不能在前），镜像 C# `hashPosition != 0 || hash.HasTrailingTrivia`；即使违规仍解析为 shebang trivia——**例外**：BOM 残留源码文本（`U+FEFF#!`）不被识别为指令（无节点、泛化解析错误 BC30037/BC30201，test-plan L2-9）。
4. **整行消费（唯一新增机械件）**：parser 级把 `#!` 后路径吞为尾随 trivia；VB 无 C# 的 `PreprocessingMessageTrivia` 机制，用既有 `SkippedTokensTrivia` 承载裸文本（语义等价：整行作 trivia、不参与语义、不残留 token）。
5. **Content API：加**——镜像 C# `Content`/`WithContent`（路径作 `StringLiteralToken`），供未来 LSP/工具读取。
6. **REPL / BOM / `#If False` = 遵循 C#；M2 实测收敛**：REPL 首行 `#!` 接受并空转；BOM 残留源码文本（`U+FEFF#!`）不被识别为指令（无节点、泛化错误 BC30037/BC30201，test-plan L2-9）；`#If False` 禁用区内 `#!` 不被识别为指令（无节点、无诊断，test-plan L2-10）——位置/模式检查仅对被识别的指令（激活区）生效。
7. **指令三分（现状）**：`#R`（编译器，上游）、`#Load`（编译器指令 + 宿主多树加载，fork 已实现）、`#!`（编译器，本提案）——加入后三者同为编译器指令 trivia。

### 现状机制（`Compilers\VisualBasic\Portable\`）

- **派发**：`ParseConditionalCompilationStatement`（`Parser\ParseConditional.vb:22`）内 `Select Case CurrentToken.Kind`（`:41`）。`#Load` 模板：`Case SyntaxKind.IdentifierToken` → `PossibleKeywordKind = LoadKeyword` → `ParseLoadDirective(hashToken, isFollowingToken)`（`:85-86`）。**`#!` 需在外层 `Select Case` 加 `Case SyntaxKind.ExclamationToken`**（`#` 后 `!` 是标点 token，非 identifier/keyword 路径）。
- **门控模板**：`ParseLoadDirective`（`:471`，`If Not IsScript Then AddError(ERR_LoadDirectiveOnlyAllowedInScripts)` :479-483）；`ParseReferenceDirective`（`:451`，门控 :459-460）。`isFollowingToken` 参数来自 `TryScanDirective`（`Scanner\Directives.vb:36` `_directiveIsFollowingToken = _leadingTriviaStartOffset > 0`；属性 `DirectiveIsFollowingToken` :25-29）。
- **扫描入口**：`TryScanDirective`（`Directives.vb:31`）：`parser.ParseConditionalCompilationStatement(_directiveIsFollowingToken)`（`:66`）→ `parser.ConsumeStatementTerminatorAfterDirective(directiveTrivia)`（`:67`）。**若 `ParseShebangDirective` 不吞行，残留 token 会在 `ConsumeStatementTerminatorAfterDirective`（`Parser\Parser.vb:5775-5794`）触发 `ERR_ExpectedEOS`**。
- **词法零改动**：`#` 经 `ScanDateLiteral`（`Scanner\Scanner.vb:1175-1177`）对 `#!` 失败回退 `MakeHashToken`；`!` 词法化为 `ExclamationToken`（VB 字典访问符既有 token）。
- **错误码**：`Errors\Errors.vb` fork 空隙策略（:1600-1604 注释「e.g. #! shebang directives」）；`ERR_LoadDirectiveOnlyAllowedInScripts = 36967`（:1605）、`ERR_PPLoadFollowsToken = 37002`（:1631）。**新码 `ERR_ShebangDirectiveOnlyAllowedInScripts = 37003`、`ERR_ShebangDirectiveNotOnFirstLine = 37004`**（37003-37049 为已核实空隙，紧邻 37002、官方 37050+ 之前）。
- **Syntax 节点**：`Syntax\Syntax.xml` 的 `DirectiveTriviaSyntax`（:9376，abstract，`HashToken` child）、`ReferenceDirectiveTriviaSyntax`（:9544）、`LoadDirectiveTriviaSyntax`（:9552，`LoadKeyword` + `File` child）。**VB 指令节点无 `EndOfDirectiveToken`**（C# 专属），行尾由 `ConsumeStatementTerminatorAfterDirective` 处理。VB trivia 含 `SkippedTokensTrivia`（:9169，可承载裸 token 文本）。
- **生成文件**：`Generated\Syntax.xml.Syntax/Main/Internal.Generated.vb` 标「DO NOT HAND EDIT」，用**基线 `{{Roslyn}}`（本机 `C:\Users\james\Projects\roslyn`）的 VBSyntaxGenerator** 再生成（`#Load` 先例见 `..\..\issues\issue-vbx-load-span-shift.md:82`：零删除、纯新增）。生成器读 fork 的 `Syntax.xml`。
- **公开 API**：`PublicAPI.Unshipped.txt` 当前为空，新节点/工厂/visitor 增量进 Unshipped 段；`PublicAPI.Shipped.txt` 与基线字节一致，不破坏 parity。
- **宿主层**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb` `CreateSubmission`（`:143`，`kind:=SourceCodeKind.Script`）——编译器接受 `#!` 即零宿主改动生效；`#Load` 多树加载 `LoadReferencedTrees`（`:56`）不受影响。REPL 提交同为 Script。
- **指令发现**（`#!` 不需要，但模板存在）：`Syntax\CompilationUnitSyntax.vb:33` `GetLoadDirectives()` = `firstToken.GetDirectives(Of LoadDirectiveTriviaSyntax)`。

### C# 参考实现（拿不准时对照）

- **位置**：`DirectiveParser.cs:36` `hashPosition = lexer.TextWindow.Position`；`:111-120` 派发 `#`+`ExclamationToken` 恒按 shebang，`hashPosition != 0 || hash.HasTrailingTrivia` → `ERR_PPShebangNotOnFirstLine`（9378，error）；即使报错仍解析。
- **门控**：`ParseShebangDirective`（`:687-695`）`SourceCodeKind != Script && !FileBasedProgram` → `ERR_PPShebangInProjectBasedProgram`（9314，error）。
- **整行消费（lexer 层）**：`ParseEndOfDirectiveWithOptionalPreprocessingMessage`（`:694`）→ `Lexer.cs:2485-2534` `LexOptionalPreprocessingMessage` 把 `#!` 后到行尾作 `PreprocessingMessageTrivia` 挂 `EndOfDirectiveToken` 前导。
- **节点/API**：`ShebangDirectiveTriviaSyntax`（Hash + Exclamation + EndOfDirective + IsActive）；`DirectiveTriviaSyntax.cs:57-58` `DirectiveNameToken` 返回 `ExclamationToken`；`ShebangDirectiveTriviaSyntax.cs` `Content`/`WithContent`（路径作 `StringLiteralToken`）。
- **行为测试**（severity/位置/树形权威）：`Compilers\CSharp\Test\Syntax\Parsing\ScriptParsingTests.cs:10201-10324`、`IgnoredDirectiveParsingTests.cs:160-814`——script 首行通过、第二行/前置空格/`# !` 报 CS9378 error 仍解析、常规编译报 CS9314、`#x` 报 CS1003、`//#!` 纯注释。**均无 shebang × BOM / `#If False` 禁用区边界测试**（2026-08-16 核实）。
- **测试纪律**：`Scripting\VisualBasicTest` 跑程序集 `-automated`（dotnet test 静默不跑）；`scripts\verify-vb-compiler-tests.ps1` 七门 gate。

## F1 验收条件（概要设计 pass 标准）

- 覆盖三层落点（Syntax 节点 / Parser / 宿主零改动）与判定原则（仅 script、位置 0、整行消费、error severity、Content API）。
- 含行为对照表（首行通过 / 第二行 / 前置空白 / `# !` 间隔 / 常规编译 / `//#!` 注释 / REPL / BOM）。
- 说明 C# `hashPosition != 0` / `PreprocessingMessageTrivia` 与 VB 落点差异（`DirectiveIsFollowingToken` 不足、`SkippedTokensTrivia`）。
- 说明行号不漂移收益与「编译器层而非宿主剥行」。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号区域 + 改动形状**，可被实施者直接照做。
- M0：`Syntax.xml` 新节点 XML 片段 + 再生成流程（基线 `{{Roslyn}}` VBSyntaxGenerator 怎么跑）+ `Content`/`WithContent` 手写 partial + `PublicAPI.Unshipped.txt` 增量。
- M1：`ParseConditional.vb` 派发分支；`ParseShebangDirective` 三步（`IsScript` 门控 / 位置检查 / 整行消费到 `StatementTerminatorToken` 作 `SkippedTokensTrivia` 尾随）；错误码 37003/37004 落点；**位置 0 判定的确切 VB 机制**（候选：`_leadingTriviaStartOffset` + 指令前是否消费 whitespace；镜像 C# 语义，M1 源码核实后定）。
- 无副作用测试矩阵（编译诊断 + Scripting 层），含 C# 对齐用例与 BOM/REPL/`#If False` 边界。
- 边界与迁移影响（`.vbx` 首行从报错变合法；常规 `.vb` 仍报错）。

## F3 验收条件（测试计划 pass 标准）

- 四层矩阵（L1 解析树形 / L2 语义诊断 / L3 Scripting 编译 / L4 REPL+边界）+ 错误码/行号/severity 断言。
- 无副作用纪律；既有测试翻转清单（如 `CompilationAPITests.vb:271` 的 `#!` 字符测试是否受影响——已核实为文件名非法字符测试，不受影响）。
- 与 proposal/meeting RESOLUTION 一致（error、仅 script、位置 0、Content API）。
