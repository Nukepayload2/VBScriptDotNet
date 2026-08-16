# `#!` shebang 指令 / Shebang Directive

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete（`../tasks/shebang-directive/`，M0-M2 完成）
* [x] Specification: [Complete](../spec/spec-shebang-directive.md)

## Summary
[summary]: #summary

本提案让 VB fork 编译器在**语法层**识别脚本文件首行的 `#!` shebang（如 `#!/opt/vbi-n2fork/vbi`），把它解析为一条指令 trivia 并忽略，从而支持在 Linux/macOS 上**直接执行** `.vbx` 脚本：deb 包装安装到 `/opt/vbi-n2fork/vbi` 后，脚本首行 `#!/opt/vbi-n2fork/vbi` 由内核通过 POSIX shebang 机制调用 `vbi`，`vbi` 读取该文件交给 fork 编译器编译，编译器必须把首行当作合法 trivia 而非语法错误。

这是**编译器语法特性**（对应 `proposal-vbscript-lsp.md` 甄别结论：`#!` 归属编译器层，C# 有、VB 无，非 LSP 范畴），不是宿主层剥行。做法完全对齐 C#：

- `#` 后跟 `!` 时按 shebang 指令解析（仅 script 模式允许）；
- `#!` 之后到行尾的整行（解释器路径）作为 trivia 消费，不参与任何语义；
- 仅允许位于文件第一个字符（位置 0、无前导 trivia）；
- 常规 `.vb` 编译（非 script）出现 `#!` 报错误，对齐 C# `ERR_PPShebangInProjectBasedProgram`。

## Motivation
[motivation]: #motivation

- **Linux 执行 vbx 不可或缺**：POSIX shebang（`#!`）是可执行脚本的标准机制，内核根据首行选择解释器。deb 包把 `vbi` 装到 `/opt/vbi-n2fork/vbi`，脚本首行即 `#!/opt/vbi-n2fork/vbi`。这是 Linux 上分发 `.vbx` 可执行脚本的必经路径，不可绕过。
- **必须是编译器语法层**：`#!` 的消费者不只是 `vbi`——任何解析该文件的程序（LSP、编辑器、语法高亮）都必须认识它，否则在 Linux 上打开一个带 shebang 的 `.vbx` 就满屏错误。把 `#!` 做成语法树的一部分（trivia），所有消费者共享同一份干净的树（详见 Alternatives 对「宿主剥行」的否决）。
- **对齐 C# 生态**：C# 的 shebang（issue #3507）经 LDM 决策链落地——`LDM-2020-07-20`（triage 进 C# 10 讨论）→ `LDM-2020-09-28`（改判 X.0，以 `dotnet run csfile` 工具为再议条件）→ **C# 14 `ignored-directives`**（champion #8617，正式落地）：`#!` 与 `#:` 同为 **ignored preprocessing directive**（语言忽略、工具可识别），`ShebangDirectiveTriviaSyntax` 即其实现。VBScript.NET 的 `.vbx` 脚本执行（`vbi script.vbx`）与 `dotnet run file.cs` 同构——工具链先落地、语言后跟进。
- **上游机制已就绪，改动最小**：基线 Roslyn 的 VB 编译器已自带 `#R` 指令的完整管道（`ReferenceDirectiveTriviaSyntax` 节点、`ParseReferenceDirective` 派发、`IsScript` 门控、`ERR_ReferenceDirectiveOnlyAllowedInScripts`）——`#!` 扩展的是**上游已有指令机制**，非 fork 发明，与 Roslyn 设计同源。

## Detailed design
[design]: #detailed-design

### 1. 语法层定位（对齐 C# 参考实现）

C# 的做法（源码核实，作为 VB 的镜像基准）：

| 环节 | C# 位置 | 行为 |
|------|--------|------|
| 文法 | `CSharp.Generated.g4:1404` `shebang_directive_trivia : '#' '!'` | `#` + `!`（行尾由 `ParseEndOfDirectiveWithOptionalPreprocessingMessage` 消费） |
| 派发 | `Parser\DirectiveParser.cs:111-120` | `#` 后下一 token 是 `ExclamationToken` 时**恒**按 shebang 解析；若 `hashPosition != 0 || hash.HasTrailingTrivia` 报 `ERR_PPShebangNotOnFirstLine`（9378） |
| 模式门控 | `ParseShebangDirective`（`DirectiveParser.cs:687-695`） | `SourceCodeKind != Script && !FileBasedProgram` 时报 `ERR_PPShebangInProjectBasedProgram`（9314） |
| 节点 | `ShebangDirectiveTriviaSyntax`（= `#` + `!` + EndOfDirectiveToken + IsActive） | 路径文本落在 trivia 里，语义上无内容 |
| 行尾 | `ParseEndOfDirectiveWithOptionalPreprocessingMessage` | 整行消费为 trivia，不留多余 token |

### 2. 语法与语义（VB 侧规则）

- **位置**：必须是文件第一个字符——位置 0、`#` 无前导 trivia（含空白）、**连 BOM 都不能在前**（C# 14 `ignored-directives.md` 原文）。违规报 **error**（已定镜像 Roslyn 实现：`hashPosition != 0 || hash.HasTrailingTrivia` → `ERR_ShebangDirectiveNotOnFirstLine`；C# spec 文字虽写 warning、Roslyn 落地是 `AddError`，已定遵循实现）。即使违规，仍解析为 shebang trivia（树结构完整、路径照常消费，C# `ShebangNotFirst` 测试同）——**例外（M2 实测）**：BOM 残留源码文本（`U+FEFF#!`）时 `#` 不被识别为指令，无 shebang 节点、产生泛化解析错误 BC30037/BC30201（test-plan L2-9），不走 `NotOnFirstLine`。
- **模式**：仅 script 模式（`IsScript`，即 `SourceCodeKind.Script`）允许；常规 `.vb` 编译出现 `#!` 报 **error**（已定遵循 `#R` 先例——不引入 C# 的 `FileBasedProgram` 特性标志，VB 无对应概念、`.vbx` 恒为 Script）。复用 fork/上游 `#R` 的门控模式：`If Not IsScript Then AddError(... ERR_ReferenceDirectiveOnlyAllowedInScripts)`（`ParseConditional.vb:459-460`）。
- **内容**：`#!` 之后到行尾的整行（解释器路径，如 `/opt/vbi-n2fork/vbi`）作为 trivia 消费——**不**作为 token、**不**解析、**不**参与语义。注意路径是裸文本（非字符串字面量），**不能**复用 `#R` 的 `StringLiteralToken` 模式（`#R "..."` 才引号包裹），这是与 `#R` 的关键差异。
- **REPL**：每个提交也是 `SourceCodeKind.Script`；首字符 `#!` 按 trivia 接受（无害空转，不执行任何事），与 C# `csi` 行为一致（已定遵循 C#，不新增「非文件场景」诊断）。
- **行号**：`#!` 作为 trivia 保留在树里，后续诊断行号**不漂移**——这是「编译器层而非宿主剥行」的关键收益。

### 3. 实现落点（文件清单，均已源码核实）

1. **新节点**：`Compilers\VisualBasic\Portable\Syntax\Syntax.xml` 增加（模板：`ReferenceDirectiveTriviaSyntax` @ `Syntax.xml:9544`；基类 `DirectiveTriviaSyntax` @ `:9376`，含 `HashToken` child）：

   ```xml
   <node-structure name="ShebangDirectiveTriviaSyntax" parent="DirectiveTriviaSyntax">
     <description>Represents a #! shebang line appearing at the start of a script file.</description>
     <node-kind name="ShebangDirectiveTrivia"></node-kind>
     <child name="ExclamationToken" kind="ExclamationToken"></child>
   </node-structure>
   ```

   重新生成 `Syntax.xml.Syntax.Generated.vb` / `Syntax.xml.Main.Generated.vb` / `Syntax.xml.Internal.Generated.vb`（节点 + `SyntaxFactory.ShebangDirectiveTrivia` + visitor）。公开 API 增量进 `PublicAPI.Unshipped.txt`（fork 与基线 `PublicAPI.Shipped.txt` 字节一致，新增走 Unshipped 段，与 fork 既有扩展同一路径）。

   随附 C# 对等的 `Content` / `WithContent` 便利 API（已定加）：把解释器路径作为 `StringLiteralToken` 暴露，供未来 LSP/工具读取（C# 实现在 `ShebangDirectiveTriviaSyntax.cs`）。

2. **派发**：`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb` 的 `ParseConditionalCompilationStatement`（`:22`）内 `Select Case CurrentToken.Kind`（`:41`）加分支。模板：`Case SyntaxKind.IdentifierToken → ReferenceKeyword → ParseReferenceDirective`（`:82-83`）：

   ```vb
   Case SyntaxKind.ExclamationToken
       statement = ParseShebangDirective(hashToken)
   ```

3. **解析**：新 `ParseShebangDirective(hashToken)`，三步：
   - `IsScript` 门控（报「仅 scripts 允许」错误，模式同 `ParseReferenceDirective` `:459-460`）；
   - 首行检查（位置 0、无前导 trivia，镜像 C# `hashPosition != 0 || hash.HasTrailingTrivia`）；
   - **整行消费（本提案唯一新增机械件）**：显式把 `#!` 后的路径吞成尾随 trivia。这是必需的——VB 的 `ConsumeStatementTerminatorAfterDirective`（`Parser\Parser.vb:5774-5793`）对指令行遗留的多余 token 报 `ERR_ExpectedEOS`；若 `ParseShebangDirective` 不吞行，`/opt/vbi-n2fork/vbi` 会被词法化为 `/`（除号）、`opt`、`vbi` 等 token 留下，触发 `ERR_ExpectedEOS`。对应 C# `ParseEndOfDirectiveWithOptionalPreprocessingMessage`（`DirectiveParser.cs:694`）。实现细节：跳过 token 直到 `StatementTerminatorToken`，把跳过的内容作为 `ExclamationToken` 的尾随 trivia。**机制差异（源码核实）**：C# 在 **lexer 层**消费（`Lexer.cs:2485-2534`，路径作 `PreprocessingMessageTrivia` 挂 `EndOfDirectiveToken` 前导）；VB **无 `PreprocessingMessage` 机制**（grep 零命中，VB `#Region` 标题也要求 `StringLiteralToken`），故用 **parser 级 skip**——语义等价（整行作 trivia、不参与语义、不残留 token），机械件实现位置不同。

4. **错误码**：`Compilers\VisualBasic\Portable\Errors\Errors.vb` + `Errors\ErrorFacts.vb` 新增两枚，**已定 fork 风格命名**：`ERR_ShebangDirectiveOnlyAllowedInScripts`、`ERR_ShebangDirectiveNotOnFirstLine`（措辞对齐 fork 既有 `ERR_LoadDirectiveOnlyAllowedInScripts = 36967` / `ERR_PPLoadFollowsToken = 37002`，不用 C# 的 `ERR_PPShebang...` 语义命名）。`ERRID` 编号沿用 fork 错误号策略（官方空隙段 36959–36983，见 `Errors.vb:1600-1604` 注释「e.g. #! shebang directives」）；两枚均报 **error**（镜像 Roslyn）。

5. **扫描器：无需改动**。`#` 经 `ScanDateLiteral`（`Scanner\Scanner.vb:1171-1173`）对 `#!` 失败（`!` 非日期字符）回退 `MakeHashToken`；`!` 单独词法化为 `ExclamationToken`（VB 字典访问符 `!` 既有 token）。已核实 `#!` 正确产出 `HashToken` + `ExclamationToken` 序列。

6. **脚本层：无需改动**。`vbi` 跑 `.vbx` 走 `Scripting\VisualBasic\VisualBasicScriptCompiler.CreateSubmission`（`:143`，`kind:=SourceCodeKind.Script`），编译器接受 `#!` 即自动生效。`#Load` 的宿主多树加载（`LoadReferencedTrees` @ `:56`）不受影响。

7. **编辑器渲染（分类，Workspaces 层）：`#!` 整行 → 注释色**。C# 落地先例（权威）：`Workspaces\CSharp\Portable\Classification\Worker.cs:207-210` 把 `ShebangDirectiveTrivia` 与 `//`、`/* */` 注释**归为一组 → `ClassificationTypeNames.Comment`**（整行含路径）；与之对照，`#:` ignored 指令因带真实工具内容走 `PreprocessorKeyword`（`Worker_Preprocesser.cs:332-350`）。VB 侧（基线 `Workspaces\VisualBasic\Portable\Classification\Worker.vb`）`ClassifyTrivia`（`:115-150`）按 `HasStructure` 分派——新 `ShebangDirectiveTrivia` 目前**无 case 命中、会不着色**，必须新增：

   ```vb
   Case SyntaxKind.ShebangDirectiveTrivia
       AddClassification(trivia, ClassificationTypeNames.Comment)
   ```

   放**注释组**（`CommentTrivia` 旁），不放指令组（否则 `#`/`!` 变 PreprocessorKeyword）。`REM` 先例印证：VB 里 `REM` 词法即 `CommentTrivia`（注释，非关键字），天然注释色——`#!` 同构。**此代码在 Workspaces 层，正是 LSP 提案要复制的层，是 LSP M0/M1 的一个具体适配点**（见 §5）。

   **注意：`Worker.vb` 不在当前 fork 树**（2026-08-16 核实）——fork 的 `Workspaces` 仅含 `SharedUtilitiesAndExtensions\Compiler\Core\`（无语言侧 Classification 层），`Worker.vb` 属基线 Roslyn，随 LSP 提案复制 Workspaces 层时落地。共享层已有基线继承的 shebang 钩子：`ISyntaxKinds.ShebangDirectiveTrivia`（`int?`，VB 侧当前无对应 kind）＋ `IsShebangDirectiveTrivia`（`ISyntaxFactsExtensions.cs:699`）＋ `AbstractFileBannerFacts` 把 shebang 当文件头注释匹配（`FileBannerFacts\AbstractFileBannerFacts.cs:38`）。M0 加 VB 节点后，VB 的 `SyntaxKinds` 具体实现（随 Workspaces 层复制）需暴露新 kind，共享 `IsShebangDirectiveTrivia`/FileBannerFacts 才对 VB 生效——这是文档原清单外的**一个附加适配点**。

### 4. 与 `#R` / `#Load` 的分工（脚本指令三分）

| 指令 | 层级 | 机制 |
|------|------|------|
| `#R` | 编译器指令 trivia | `ParseReferenceDirective`（`ParseConditional.vb:451`），解析 `StringLiteralToken`，脚本层 resolver 解析 nuget:/路径 |
| `#Load` | 编译器指令 trivia + 宿主多树加载 | `LoadDirectiveTriviaSyntax`（`Syntax.xml:9552`）+ `ParseLoadDirective`（`ParseConditional.vb:471`，`IsScript` 门控）；宿主 `LoadReferencedTrees`（`VisualBasicScriptCompiler.vb:56`）每文件独立树加载 |
| `#!` | 编译器指令 trivia（本提案） | 新 `ShebangDirectiveTrivia`，整行吞为 trivia |

`#!` 必须进编译器：`#!` 的消费者是所有解析该文件的程序（vbi、LSP、编辑器），必须共享同一棵树（宿主剥行会让行号漂移、只修 vbi 一个消费者，见 Alternatives）。`#Load` 现同为编译器级（`LoadDirectiveTriviaSyntax` + `ParseLoadDirective`）+ 宿主多树加载（修复 `issue-vbx-load-span-shift.md`）。与 C# 的对应关系：`#r` 与 shebang 是编译器级；`#load` 在 C# 是宿主级、在 VB fork 是**编译器指令 + 宿主执行双层**。

### 5. 对 LSP / 编辑器的影响（跨提案）

`proposal-vbscript-lsp.md` 的甄别结论把 `#!` 判为**编译器语法特性（C# 有、VB 无），非本提案范畴**。本提案补齐编译器侧：`#!` 成为 script 模式语法树的一部分（trivia），LSP 的语义模型（两模式唯一区别是 script mode）自动继承——Linux 上带 shebang 的 `.vbx` 在编辑器/LSP 中**零诊断**。

LSP 侧唯一实质工作（很薄）在**语义层面之外**：当 LSP 提案从基线复制 Workspaces/Features 层时，VB `Worker.ClassifyTrivia` 需要把 `ShebangDirectiveTrivia` 接进分类 switch（见 §3-7，注释色）——这是「修改语法与 Features 适配点」清单里的一个**已定位的具体实例**，纳入 LSP M0/M1 适配点盘点。

### 6. 测试（`Scripting\VisualBasicTest`，无副作用）

- `.vbx` 文件首行 `#!/opt/vbi-n2fork/vbi` + 正常代码：编译通过，`#!` 解析为 `ShebangDirectiveTrivia`，后续诊断行号不漂移。
- `#!` 不在首行（第二行起）→ 报错。
- `#!` 带前置空白（`  #!/...`）→ 报错。
- `# !...`（`#` 与 `!` 之间有空格）→ 报错（hash 有尾随 trivia，镜像 C# `ShebangWithTriviaInBetween`），仍解析为 shebang。
- 常规 `.vb` 编译（非 script）含 `#!` → 报「仅 scripts 允许」错误。
- `#!` 行内容任意（含空格、`/`、`#`、`-`）→ 整行作为 trivia，无错误。
- `#!` 与 `#R`/`#Load` 共存：`#!` 首行 + 后续 `#R`/`#Load` 正常。
- 违规 severity 断言 **error**（镜像 C# 测试 `error CS9378/CS9314`，非 warning）；`Content` 返回解释器路径字符串（`StringLiteralToken`）。

## Drawbacks
[drawbacks]: #drawbacks

- **新增公开语法节点 + 错误码**：`ShebangDirectiveTriviaSyntax` 进公开 API（Unshipped 段），语法树 API 面积扩大。
- **Syntax.xml 重新生成**：3 个生成文件 churn（`Syntax.Generated.vb` / `Main.Generated.vb` / `Internal.Generated.vb`）。
- **`#!` 在 REPL 中被接受为无害 trivia**：与 C# `csi` 一致，但语义上空转，需文档说明「REPL 首行 `#!` 无效果」。
- **首个字符检查与 BOM/编码交互**：已定镜像 C# 的位置 0 判定（`hashPosition != 0`）——文件读入路径 BOM 被解码吞掉时 `#` 仍位置 0；BOM 作为字符（U+FEFF）留在源码文本时不被识别为指令，无 shebang 节点、产生泛化解析错误 BC30037/BC30201（test-plan L2-9）。M2 已按实测收敛。

## Alternatives
[alternatives]: #alternatives

1. **宿主剥行**（`vbi` 读文件时删首行 `#!`）：否决。
   - 诊断行号整体漂移 1 行——`.vbx` 是错误上报的语言场景，行号偏移不可接受；
   - 只修 `vbi`——编辑器/LSP 打开同一文件仍报错，Linux 开发体验断裂；
   - 与 `#R`（编译器级指令）层级不一致。
2. **扫描器整体词法化**（`#!...EOL` 直接作为单个 comment 式 trivia token）：可行但偏离 C# 结构，丢失指令节点形态（`GetDirectives` 无法发现），不做。
3. **复用 `#R` / `#ExternalSource`**：语义不同——`#R` 要求字符串字面量，`#!` 路径是裸文本；`#ExternalSource` 是行映射，都不是 shebang。
4. **不支持**：Linux/macOS 直接执行 `.vbx` 无从谈起（deb 包装 `#!/opt/vbi-n2fork/vbi` 首行必然编译报错），与动机直接冲突。

## Resolved questions
[resolved]: #resolved-questions

> 2026-08-16 用户定案（整体遵循 C# 实现）+ 源码核实，以下 OPEN QUESTIONS 全部关闭；残留边界验证进 M2 测试。

1. **severity**：**已定 error（镜像 Roslyn 实现）**。CS9378/CS9314 均为 `AddError`（`DirectiveParser.cs:116,691`），测试断言 `error CS9378/CS9314`；C# spec（`ignored-directives.md:63-69`）的 warning 文字与「对 `#!` 豁免 project-based 报错」的意图**均不采用**。VB 侧对等错误码同为 error。
2. **错误码命名**：**已定 fork 风格**——`ERR_ShebangDirectiveOnlyAllowedInScripts` + `ERR_ShebangDirectiveNotOnFirstLine`（对齐 `ERR_LoadDirectiveOnlyAllowedInScripts`，不用 C# 的 `ERR_PPShebang...`）。
3. **模式门控**：**已定仅 script（遵循 `#R` 先例）**——不引入 C# 的 `FileBasedProgram` 特性标志（VB 无该概念，`.vbx` 恒为 Script）。
4. **Content API**：**已定加**——镜像 C# `Content`/`WithContent`（解释器路径作 `StringLiteralToken`），供未来 LSP/工具读取。
5. **REPL 首行 `#!`**：**已定遵循 C#**——接受为无害 trivia、空转（`csi` 行为一致），不新增「非文件场景」诊断。
6. **BOM / 位置 0**：**已定遵循 C#，M2 实测收敛**——`hashPosition != 0` 判定（`DirectiveParser.cs:36,114`）。文件读入路径 BOM 被解码吞掉时 `#` 仍位置 0，shebang 正常识别；BOM 作为字符（U+FEFF）留在源码文本时**不被识别为指令**（无 shebang 节点、泛化解析错误 BC30037/BC30201，test-plan L2-9）。
7. **`#If False` 禁用区内的 `#!`**：**M2 实测收敛**——禁用区内 `#!` **不被识别为指令**（无节点、无诊断），位置/模式检查仅对实际被识别的指令（激活区）生效；激活区内错位 shebang 仍报 `NotOnFirstLine`（test-plan L2-10）。该边界在 C# 侧无对等测试（`IgnoredDirectiveParsingTests.cs` / `ScriptParsingTests.cs` 无 shebang × 禁用区用例），VB 现状为不识别。

## 相关文档
[references]: #references

- `proposal-vbscript-lsp.md`——甄别结论：`#!` 属编译器语法特性（C# 有、VB 无），非 LSP 范畴；本提案补齐编译器侧落点。
- C# 参考实现：`Parser\DirectiveParser.cs:36,111-120,687-695`（`hashPosition` / 派发 / `ParseShebangDirective`）、`Parser\Lexer.cs:2485-2534`（`PreprocessingMessageTrivia` 整行消费）、`CSharpParseOptions.cs:245`（`FileBasedProgram` 特性标志）、`Syntax\ShebangDirectiveTriviaSyntax.cs`（`Content`/`WithContent`）、`CSharp.Generated.g4:1404`（`shebang_directive_trivia : '#' '!'`）、错误码 9314/9378。
- C# LDM 决策链：`InternalDevDocs\csharplang\meetings\2020\LDM-2020-07-20.md:45-60`、`LDM-2020-09-28.md:89-98`（issue #3507）；正式落地设计 `InternalDevDocs\csharplang\proposals\csharp-14.0\ignored-directives.md`（champion #8617）——`#!`/`#:` 为 ignored preprocessing directive，首行规则（连 BOM 不能在前）+ severity 立场（spec 写 warning、Roslyn 落地 error，已定遵循实现）。
- C# 行为测试（severity/位置/树形权威来源）：`Compilers\CSharp\Test\Syntax\Parsing\ScriptParsingTests.cs:10201-10324`（shebang region）、`Compilers\CSharp\Test\Syntax\Parsing\IgnoredDirectiveParsingTests.cs:160-814`（feature flag / 位置 / 树形）——**均无 shebang × BOM / `#If False` 禁用区边界测试**（2026-08-16 核实）。
- VB 侧模板（fork 内，`#Load` 比 `#R` 更完整）：`Parser\ParseConditional.vb:471-489`（`ParseLoadDirective` 派发 + `IsScript` 门控 `ERR_LoadDirectiveOnlyAllowedInScripts`）、`Syntax\Syntax.xml:9552`（`LoadDirectiveTriviaSyntax`）、`Syntax\CompilationUnitSyntax.vb:33`（`GetLoadDirectives`）；上游 `#R` 模板 `ParseConditional.vb:451-469`、`Syntax.xml:9376/9544`（`DirectiveTriviaSyntax` 基类 / `ReferenceDirectiveTriviaSyntax`）。
