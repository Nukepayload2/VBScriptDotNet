# `#!` shebang 指令 / Shebang Directive

* [ ] Proposed
* [ ] Prototype
* [ ] Implementation
* [ ] Specification

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
| 文法 | `CSharp.Generated.g4:1312` `shebang_directive_trivia : '#' '!' end_of_directive` | `#` + `!` + 行尾 |
| 派发 | `Parser\DirectiveParser.cs:111-120` | `#` 后下一 token 是 `ExclamationToken` 时**恒**按 shebang 解析；若 `hashPosition != 0 || hash.HasTrailingTrivia` 报 `ERR_PPShebangNotOnFirstLine`（9378） |
| 模式门控 | `ParseShebangDirective`（`DirectiveParser.cs:687-695`） | `SourceCodeKind != Script && !FileBasedProgram` 时报 `ERR_PPShebangInProjectBasedProgram`（9314） |
| 节点 | `ShebangDirectiveTriviaSyntax`（= `#` + `!` + EndOfDirectiveToken + IsActive） | 路径文本落在 trivia 里，语义上无内容 |
| 行尾 | `ParseEndOfDirectiveWithOptionalPreprocessingMessage` | 整行消费为 trivia，不留多余 token |

### 2. 语法与语义（VB 侧规则）

- **位置**：必须是文件第一个字符——位置 0、`#` 无前导 trivia（含空白）、**连 BOM 都不能在前**（C# 14 `ignored-directives.md` 原文）。违规 severity 见 Unresolved questions（C# spec 记为 warning、Roslyn 实现为 error）。
- **模式**：仅 script 模式（`IsScript`，即 `SourceCodeKind.Script`）允许；常规 `.vb` 编译出现 `#!` 报错，对齐 C# `ERR_PPShebangInProjectBasedProgram`。复用 fork/上游 `#R` 的门控模式：`If Not IsScript Then AddError(... ERR_ReferenceDirectiveOnlyAllowedInScripts)`（`ParseConditional.vb:456-458`）。
- **内容**：`#!` 之后到行尾的整行（解释器路径，如 `/opt/vbi-n2fork/vbi`）作为 trivia 消费——**不**作为 token、**不**解析、**不**参与语义。注意路径是裸文本（非字符串字面量），**不能**复用 `#R` 的 `StringLiteralToken` 模式（`#R "..."` 才引号包裹），这是与 `#R` 的关键差异。
- **REPL**：每个提交也是 `SourceCodeKind.Script`；首字符 `#!` 按 trivia 接受（无害空转，不执行任何事），与 C# `csi` 行为一致。
- **行号**：`#!` 作为 trivia 保留在树里，后续诊断行号**不漂移**——这是「编译器层而非宿主剥行」的关键收益。

### 3. 实现落点（文件清单，均已源码核实）

1. **新节点**：`Compilers\VisualBasic\Portable\Syntax\Syntax.xml` 增加（模板：`ReferenceDirectiveTriviaSyntax` @ `Syntax.xml:9540`；基类 `DirectiveTriviaSyntax` @ `:9372`，含 `HashToken` child）：

   ```xml
   <node-structure name="ShebangDirectiveTriviaSyntax" parent="DirectiveTriviaSyntax">
     <description>Represents a #! shebang line appearing at the start of a script file.</description>
     <node-kind name="ShebangDirectiveTrivia"></node-kind>
     <child name="ExclamationToken" kind="ExclamationToken"></child>
   </node-structure>
   ```

   重新生成 `Syntax.xml.Syntax.Generated.vb` / `Syntax.xml.Main.Generated.vb` / `Syntax.xml.Internal.Generated.vb`（节点 + `SyntaxFactory.ShebangDirectiveTrivia` + visitor）。公开 API 增量进 `PublicAPI.Unshipped.txt`（fork 与基线 `PublicAPI.Shipped.txt` 字节一致，新增走 Unshipped 段，与 fork 既有扩展同一路径）。

2. **派发**：`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb` 的 `ParseConditionalCompilationStatement`（`:22`）内 `Select Case CurrentToken.Kind`（`:41`）加分支。模板：`Case SyntaxKind.IdentifierToken → ReferenceKeyword → ParseReferenceDirective`（`:82-83`）：

   ```vb
   Case SyntaxKind.ExclamationToken
       statement = ParseShebangDirective(hashToken)
   ```

3. **解析**：新 `ParseShebangDirective(hashToken)`，三步：
   - `IsScript` 门控（报「仅 scripts 允许」错误，模式同 `ParseReferenceDirective` `:456-458`）；
   - 首行检查（位置 0、无前导 trivia，镜像 C# `hashPosition != 0 || hash.HasTrailingTrivia`）；
   - **整行消费（本提案唯一新增机械件）**：显式把 `#!` 后的路径吞成尾随 trivia。这是必需的——VB 的 `ConsumeStatementTerminatorAfterDirective`（`Parser\Parser.vb:5774-5793`）对指令行遗留的多余 token 报 `ERR_ExpectedEOS`；若 `ParseShebangDirective` 不吞行，`/opt/vbi-n2fork/vbi` 会被词法化为 `/`（除号）、`opt`、`vbi` 等 token 留下，触发 `ERR_ExpectedEOS`。对应 C# `ParseEndOfDirectiveWithOptionalPreprocessingMessage`（`DirectiveParser.cs:694`）。实现细节：跳过 token 直到 `StatementTerminatorToken`，把跳过的内容作为 `ExclamationToken` 的尾随 trivia。

4. **错误码**：`Compilers\VisualBasic\Portable\Errors\Errors.vb` + `Errors\ErrorFacts.vb` 新增两枚（对齐 C# 9314/9378）：如 `ERR_ShebangDirectiveOnlyAllowedInScripts`、`ERR_ShebangDirectiveNotOnFirstLine`；`ERRID` 编号沿用高位段（`ERR_ReferenceDirectiveOnlyAllowedInScripts = 36964` @ `Errors.vb:1593` 附近）。

5. **扫描器：无需改动**。`#` 经 `ScanDateLiteral`（`Scanner\Scanner.vb:1171-1173`）对 `#!` 失败（`!` 非日期字符）回退 `MakeHashToken`；`!` 单独词法化为 `ExclamationToken`（VB 字典访问符 `!` 既有 token）。已核实 `#!` 正确产出 `HashToken` + `ExclamationToken` 序列。

6. **脚本层：无需改动**。`vbi` 跑 `.vbx` 走 `Scripting\VisualBasic\VisualBasicScriptCompiler.CreateSubmission`（`:215`，`kind:=SourceCodeKind.Script`），编译器接受 `#!` 即自动生效。`#Load` 的宿主预处理（`ExpandLoadDirectives` @ `:54-158`）不受影响。

7. **编辑器渲染（分类，Workspaces 层）：`#!` 整行 → 注释色**。C# 落地先例（权威）：`Workspaces\CSharp\Portable\Classification\Worker.cs:207-210` 把 `ShebangDirectiveTrivia` 与 `//`、`/* */` 注释**归为一组 → `ClassificationTypeNames.Comment`**（整行含路径）；与之对照，`#:` ignored 指令因带真实工具内容走 `PreprocessorKeyword`（`Worker_Preprocesser.cs:332-350`）。VB 侧（基线 `Workspaces\VisualBasic\Portable\Classification\Worker.vb`）`ClassifyTrivia`（`:115-150`）按 `HasStructure` 分派——新 `ShebangDirectiveTrivia` 目前**无 case 命中、会不着色**，必须新增：

   ```vb
   Case SyntaxKind.ShebangDirectiveTrivia
       AddClassification(trivia, ClassificationTypeNames.Comment)
   ```

   放**注释组**（`CommentTrivia` 旁），不放指令组（否则 `#`/`!` 变 PreprocessorKeyword）。`REM` 先例印证：VB 里 `REM` 词法即 `CommentTrivia`（注释，非关键字），天然注释色——`#!` 同构。**此代码在 Workspaces 层，正是 LSP 提案要复制的层，是 LSP M0/M1 的一个具体适配点**（见 §5）。

### 4. 与 `#R` / `#Load` 的分工（脚本指令三分）

| 指令 | 层级 | 机制 |
|------|------|------|
| `#R` | 编译器指令 trivia | `ParseReferenceDirective`（`ParseConditional.vb:448`），解析 `StringLiteralToken`，脚本层 resolver 解析 nuget:/路径 |
| `#Load` | 脚本层源码预处理 | `VisualBasicScriptCompiler.ExpandLoadDirectives`（`:54-158`），读文件内联并删除指令行 |
| `#!` | 编译器指令 trivia（本提案） | 新 `ShebangDirectiveTrivia`，整行吞为 trivia |

`#!` 必须进编译器而 `#Load` 可以留在宿主：`#!` 的消费者是所有解析该文件的程序（vbi、LSP、编辑器），必须共享同一棵树；`#Load` 是执行期文件内联语义，宿主处理即可。与 C# 一致：`#r` 与 shebang 是编译器级，`#load` 是宿主级。

### 5. 对 LSP / 编辑器的影响（跨提案）

`proposal-vbscript-lsp.md` 的甄别结论把 `#!` 判为**编译器语法特性（C# 有、VB 无），非本提案范畴**。本提案补齐编译器侧：`#!` 成为 script 模式语法树的一部分（trivia），LSP 的语义模型（两模式唯一区别是 script mode）自动继承——Linux 上带 shebang 的 `.vbx` 在编辑器/LSP 中**零诊断**。

LSP 侧唯一实质工作（很薄）在**语义层面之外**：当 LSP 提案从基线复制 Workspaces/Features 层时，VB `Worker.ClassifyTrivia` 需要把 `ShebangDirectiveTrivia` 接进分类 switch（见 §3-7，注释色）——这是「修改语法与 Features 适配点」清单里的一个**已定位的具体实例**，纳入 LSP M0/M1 适配点盘点。

### 6. 测试（`Scripting\VisualBasicTest`，无副作用）

- `.vbx` 文件首行 `#!/opt/vbi-n2fork/vbi` + 正常代码：编译通过，`#!` 解析为 `ShebangDirectiveTrivia`，后续诊断行号不漂移。
- `#!` 不在首行（第二行起）→ 报错。
- `#!` 带前置空白（`  #!/...`）→ 报错。
- 常规 `.vb` 编译（非 script）含 `#!` → 报「仅 scripts 允许」错误。
- `#!` 行内容任意（含空格、`/`、`#`、`-`）→ 整行作为 trivia，无错误。
- `#!` 与 `#R`/`#Load` 共存：`#!` 首行 + 后续 `#R`/`#Load` 正常。

## Drawbacks
[drawbacks]: #drawbacks

- **新增公开语法节点 + 错误码**：`ShebangDirectiveTriviaSyntax` 进公开 API（Unshipped 段），语法树 API 面积扩大。
- **Syntax.xml 重新生成**：3 个生成文件 churn（`Syntax.Generated.vb` / `Main.Generated.vb` / `Internal.Generated.vb`）。
- **`#!` 在 REPL 中被接受为无害 trivia**：与 C# `csi` 一致，但语义上空转，需文档说明「REPL 首行 `#!` 无效果」。
- **首个字符检查与 BOM/编码交互**：文件首字符含 UTF-8 BOM 时 `#!` 是否仍算「位置 0」需实现时明确（镜像 C# 的相对树起点语义）。

## Alternatives
[alternatives]: #alternatives

1. **宿主剥行**（`vbi` 读文件时删首行 `#!`）：否决。
   - 诊断行号整体漂移 1 行——`.vbx` 是错误上报的语言场景，行号偏移不可接受；
   - 只修 `vbi`——编辑器/LSP 打开同一文件仍报错，Linux 开发体验断裂；
   - 与 `#R`（编译器级指令）层级不一致。
2. **扫描器整体词法化**（`#!...EOL` 直接作为单个 comment 式 trivia token）：可行但偏离 C# 结构，丢失指令节点形态（`GetDirectives` 无法发现），不做。
3. **复用 `#R` / `#ExternalSource`**：语义不同——`#R` 要求字符串字面量，`#!` 路径是裸文本；`#ExternalSource` 是行映射，都不是 shebang。
4. **不支持**：Linux/macOS 直接执行 `.vbx` 无从谈起（deb 包装 `#!/opt/vbi-n2fork/vbi` 首行必然编译报错），与动机直接冲突。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **REPL 首行 `#!`**：接受为无害 trivia（镜像 C#）还是要报「非文件场景」诊断？倾向镜像 C#，接受并空转。
2. **BOM**：C# 14 `ignored-directives.md` 明说「连 BOM 都不能在前」——实现时按此核对 VB 解析器 token 偏移语义。
3. **错误码命名**：镜像 C# 语义命名（`ERR_ShebangDirective...`）还是沿用 `ERR_ReferenceDirectiveOnlyAllowedInScripts` 的措辞风格（`ERR_ShebangDirectiveOnlyAllowedInScripts`）？
4. **`#If False` 禁用区内的 `#!`**：对齐 C# 的 `IsActive` 语义——禁用区 shebang 是否豁免首行错误？实现时对照 C# `ShebangDirectiveTriviaSyntax.IsActive` 行为。
5. **severity**：非首行违规按 C# spec（`ignored-directives.md`）是 **warning**（"otherwise shells won't recognize it"——代码无害），按 Roslyn 实现是 **error**（`ERR_PPShebangNotOnFirstLine`）；C# 对 project-based 报错还**明确说过对 `#!` 可豁免**。VB 侧镜像哪个？倾向镜像 Roslyn 实现（error，更干净），spec 的 warning 立场作备选。

## 相关文档
[references]: #references

- `proposal-vbscript-lsp.md`——甄别结论：`#!` 属编译器语法特性（C# 有、VB 无），非 LSP 范畴；本提案补齐编译器侧落点。
- C# 参考实现：`Compilers\CSharp\Portable\Parser\DirectiveParser.cs:111-120, 687-695`、`CSharp.Generated.g4:1312`、错误码 9314/9378。
- C# LDM 决策链：`InternalDevDocs\csharplang\meetings\2020\LDM-2020-07-20.md:45-60`、`LDM-2020-09-28.md:89-98`（issue #3507）；正式落地设计 `InternalDevDocs\csharplang\proposals\csharp-14.0\ignored-directives.md`（champion #8617）——`#!`/`#:` 为 ignored preprocessing directive，首行规则（连 BOM 不能在前）+ severity 立场。
- VB 侧模板（上游已有）：`Parser\ParseConditional.vb:82-83, 448-464`（`#R` 派发 + `IsScript` 门控）、`Syntax\Syntax.xml:9372, 9540`（`DirectiveTriviaSyntax` 基类 / `ReferenceDirectiveTriviaSyntax`）。
