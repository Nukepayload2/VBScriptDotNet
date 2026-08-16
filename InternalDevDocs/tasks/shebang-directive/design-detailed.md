# 详细设计：`.vbx` 首行 `#!` shebang 指令

> 状态：详细设计（F2）。依据链：`design-overview.md`（F1，三层落点 + 行为对照 + 判定原则）。
> 源码事实以任务 README「共享源码事实」为基准；C# 参考实现位置一并给出。改动以 `文件:行号区域` 定位，实施者可照做；标「**M1 核实**」的为需在实现时对照源码/C# 确认的细节。

## M0 — Syntax 节点 + 再生成 + Content API

### M0.1 `Syntax.xml` 新增节点

文件：`Compilers\VisualBasic\Portable\Syntax\Syntax.xml`，在 `LoadDirectiveTriviaSyntax` 之后（`:9558` 后）追加：

```xml
<node-structure name="ShebangDirectiveTriviaSyntax" parent="DirectiveTriviaSyntax">
  <description>Represents a #! shebang line appearing at the start of a script file.</description>
  <node-kind name="ShebangDirectiveTrivia"></node-kind>
  <child name="ExclamationToken" kind="ExclamationToken"></child>
</node-structure>
```

- parent `DirectiveTriviaSyntax`（:9376，abstract，自带 `HashToken` child）→ 节点 = `HashToken` + `ExclamationToken`。
- **VB 指令节点无 `EndOfDirectiveToken`**（C# 专属），路径文本不进子节点（放 `ExclamationToken` 尾随 trivia，见 M1.3）。
- 模板对照：`LoadDirectiveTriviaSyntax`（:9552，`LoadKeyword` + `File` child）。

### M0.2 再生成 3 个生成文件

文件：`Compilers\VisualBasic\Portable\Generated\Syntax.xml.Syntax.Generated.vb` / `Syntax.xml.Main.Generated.vb` / `Syntax.xml.Internal.Generated.vb`（标「DO NOT HAND EDIT」）。

- 流程：用**基线 `{{Roslyn}}`（本机 `C:\Users\james\Projects\roslyn`）的 VBSyntaxGenerator** 读取本 fork 修改后的 `Syntax.xml`，输出三个文件，替换回 fork。
- 验收：**零删除、纯新增**（`#Load` 先例 `..\..\issues\issue-vbx-load-span-shift.md:82`）；生成器具体调用方式（`{{Roslyn}}` 下的 generator 工程 / build target）由实施者在 M0 对照 `#Load` 再生成记录（`tmp\vortex-logs\` 或 `{{Roslyn}}` 工具）核实后执行。
- 生成结果应包含：`ShebangDirectiveTriviaSyntax` 节点、`SyntaxFactory.ShebangDirectiveTrivia` 两个重载、visitor `VisitShebangDirectiveTrivia`。

### M0.3 `Content` / `WithContent` 便利 API

文件：新建 `Compilers\VisualBasic\Portable\Syntax\ShebangDirectiveTriviaSyntax.vb`（手写 partial，镜像 C# `ShebangDirectiveTriviaSyntax.cs`）：

```vb
' 路径文本在 ExclamationToken.TrailingTrivia（SkippedTokensTrivia，见 M1.3）
Public ReadOnly Property Content As SyntaxToken
    Get
        Dim text = Me.ExclamationToken.TrailingTrivia.ToFullString()
        ' M1 核实：镜像 C# 用 StringLiteral factory 使 Kind=StringLiteralToken、ToString() 得路径文本
        Return SyntaxFactory.StringLiteralToken(text)
    End Get
End Property
```

- C# 参考：`ShebangDirectiveTriviaSyntax.cs` `Content` 取 `EndOfDirectiveToken.LeadingTrivia.ToString()` → `SyntaxToken.StringLiteral`；VB 侧路径在 `ExclamationToken` 尾随 trivia，取值位置不同、语义对等。
- `WithContent` 镜像 C#：改写尾随 trivia（`WithTrailingTrivia` 重建），`M1 核实` 具体 factory。

### M0.4 公开 API

文件：`Compilers\VisualBasic\Portable\PublicAPI.Unshipped.txt`（当前为空）。新增：

```
Microsoft.CodeAnalysis.VisualBasic.Syntax.ShebangDirectiveTriviaSyntax
...
```

- 条目按 Roslyn PublicAPI 约定（节点类型 + 成员）；精确文本以生成文件/其他 fork 扩展（`LoadDirectiveTriviaSyntax` 先例）对齐。
- `PublicAPI.Shipped.txt` 不动（字节一致）。

## M1 — Parser 派发 + ParseShebangDirective + 错误码

### M1.1 派发

文件：`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb`，`ParseConditionalCompilationStatement`（`:22`）外层 `Select Case CurrentToken.Kind`（`:41`）加分支（放在 `Case Else` 之前）：

```vb
Case SyntaxKind.ExclamationToken
    statement = ParseShebangDirective(hashToken, isFollowingToken)
```

- 派发后 `#!` 不再落入 `Case Else` → `ParseBadDirective`（`:92-93`）。
- `hashToken` 已在 `:38` 取出、`:39` `GetNextToken()` 后 `CurrentToken` = `ExclamationToken`。
- 对照 `#Load` 派发（`Case SyntaxKind.IdentifierToken` → `LoadKeyword`，`:85-86`）——`#!` 走标点路径，不进 identifier/keyword 分支。

### M1.2 `ParseShebangDirective`（新函数）

文件：`ParseConditional.vb`，放 `ParseReferenceDirective`（`:451`）/`ParseLoadDirective`（`:471`）旁。形状：

```vb
Private Function ParseShebangDirective(hashToken As PunctuationSyntax, isFollowingToken As Boolean) As DirectiveTriviaSyntax
    Debug.Assert(CurrentToken.Kind = SyntaxKind.ExclamationToken,
                 NameOf(ParseShebangDirective) & " called with wrong token.")

    Dim exclamation = DirectCast(CurrentToken, PunctuationSyntax)
    GetNextToken()

    ' 1) 模式门控（镜像 ParseLoadDirective :479-483）
    If Not IsScript Then
        exclamation = AddError(exclamation, ERRID.ERR_ShebangDirectiveOnlyAllowedInScripts)
    End If

    ' 2) 位置检查（镜像 C# hashPosition != 0 || hash.HasTrailingTrivia，DirectiveParser.cs:114-116）
    If <# 不在文件首字符> OrElse hashToken.HasTrailingTrivia Then
        hashToken = AddError(hashToken, ERRID.ERR_ShebangDirectiveNotOnFirstLine)
    End If

    ' 3) 整行消费（唯一新增机械件，见 M1.3）
    exclamation = ConsumeShebangContentAsTrailingTrivia(exclamation)

    Return SyntaxFactory.ShebangDirectiveTrivia(hashToken, exclamation)
End Function
```

- **即使违规仍返回 shebang 节点**（错误挂在 token 上，树结构完整）——镜像 C#（`ShebangNotFirst` 测试：error + 仍解析）——**例外**：BOM 残留源码文本（`U+FEFF#!`）与 `#If False` 禁用区内 `#!` 不被识别为指令（无节点、无诊断，test-plan L2-9/L2-10）。
- `isFollowingToken` 参数暂留（与 `#R`/`#Load` 签名一致）；`#!` 的更强位置约束由位置检查承担，`M1 核实` 是否还需 `ERR_PP...FollowsToken` 类检查（倾向不需要——`#!` 的位置错误已由 `NotOnFirstLine` 覆盖）。

### M1.3 整行消费（`SkippedTokensTrivia` 尾随）

```vb
Private Function ConsumeShebangContentAsTrailingTrivia(exclamation As PunctuationSyntax) As PunctuationSyntax
    Dim tokens As New List(Of SyntaxToken)()
    While CurrentToken.Kind <> SyntaxKind.StatementTerminatorToken
        tokens.Add(CurrentToken)
        GetNextToken()
    End While

    If tokens.Count > 0 Then
        Dim skipped = SyntaxFactory.SkippedTokensTrivia(SyntaxFactory.List(tokens))
        exclamation = exclamation.AddTrailingTrivia(skipped)
    End If

    Return exclamation
End Function
```

- **为什么必须吞行**：不吞则 `/opt/vbi-n2fork/vbi` 词法化为 `/`、`opt`、`vbi` 等 token 残留，`ConsumeStatementTerminatorAfterDirective`（`Parser.vb:5775-5794`）报 `ERR_ExpectedEOS`。
- **VB 无 C# `PreprocessingMessageTrivia`**（lexer 级整行消费，`Lexer.cs:2485-2534`），用既有 `SkippedTokensTrivia`（`Syntax.xml:9169`）承载裸 token 文本——语义等价（整行作 trivia、不参与语义、不残留 token）。`M1 核实`：`SkippedTokensTrivia` 的构造 factory 与尾随追加方式（对照 `ResyncAndConsumeStatementTerminator` / 现有 `AddTrailingTrivia` 用法）。
- 吞行后 `ConsumeStatementTerminatorAfterDirective` 看到 `StatementTerminatorToken` → 正常消费，无错误。

### M1.4 错误码

文件：`Compilers\VisualBasic\Portable\Errors\Errors.vb` + `Errors\ErrorFacts.vb`。

- `Errors.vb` 在 `ERR_PPLoadFollowsToken = 37002`（`:1631`）附近新增（fork 空隙 37003-37049 已核实）：
  ```
  ERR_ShebangDirectiveOnlyAllowedInScripts = 37003
  ERR_ShebangDirectiveNotOnFirstLine = 37004
  ```
- `ErrorFacts.vb`：两枚加入与 `ERR_LoadDirectiveOnlyAllowedInScripts` 同类的分类（`M1 核实`：该码在 ErrorFacts 的哪个分类函数——参考 `#Load` 先例登记位置；若 `#Load` 未登记则跳过）。
- **消息文案**：`VbErrors.resx`（或 Errors.vb 内嵌文案，`M1 核实` 所在位置）——镜像 C# 消息语义：
  - `'#!' directives can be only used in scripts`（对齐 `ERR_LoadDirectiveOnlyAllowedInScripts` 措辞）。
  - `'#!' must be the first characters on the first line of the file`（对齐 C# CS9378 消息）。

## 位置 0 判定：确切 VB 机制（M1 核实，目标语义固定）

**目标**（镜像 C# `hashPosition != 0`，`DirectiveParser.cs:36,114`）：`#` 必须是文件首字符。以下场景必须报 `ERR_ShebangDirectiveNotOnFirstLine`：` #!`（首行前置空格）、`\n#!`（文件以换行开头）、第二行、`# !`（`#` 有尾随空格，走 `hashToken.HasTrailingTrivia` 分支）。**BOM 残留（`U+FEFF#!`）不在此列**——`#` 不被识别为指令、无 shebang 节点、产生泛化解析错误 BC30037/BC30201（test-plan L2-9）。

**VB 既有信号不足**（已核实）：

- `DirectiveIsFollowingToken`（`Directives.vb:36` `_leadingTriviaStartOffset > 0`）只判「leading trivia 不起始于树起点」；文件首 token 的 leading trivia 内有内容（` #!`、`\n#!`）时 offset 仍为 0 → 漏判。
- `hashToken.HasLeadingTrivia` 不可用——前置空白是**同级 trivia**（`TryScanDirective` :38-42 把指令前空白扫进 tList），不在 hashToken 上。

**实现候选（M1 对照源码/C# 核实后定）**：

1. **扫描器位置信号（首选）**：`TryScanDirective`（`Directives.vb:31`）入口 `_lineBufferOffset` 即指令前已扫描字符数；`#` 为首字符 ⇔ 入口处 `_lineBufferOffset == 0`。新增 scanner 字段（如 `_directiveAtTreeStart`）/属性，或扩展传给 parser 的信号，`ParseShebangDirective` 据此判位置 0。镜像 C# `hashPosition = lexer.TextWindow.Position` 的取法。
2. **parser 位置 API**：`M1 核实` VB parser 能否在指令解析期拿到 hashToken 的绝对位置（green node position / `_lineBufferOffset` 传播）。

> 目标行为是硬约束（C# 对齐测试全过）；具体机制 M1 实施者选实现成本最低且通过 C# 对齐测试的方案。

## M2 — 测试计划落点（详见 test-plan.md）

- L1 解析树形：`ShebangDirectiveTrivia` 节点形状、`HashToken`/`ExclamationToken`/尾随 `SkippedTokensTrivia`、`GetDirectives` 可发现、行号不漂移。
- L2 语义诊断：两枚错误码 + severity=error + 位置（首行/第二行/前置空白/`# !`）+ 门控（script 通过、常规 `.vb` 报 `OnlyAllowedInScripts`）+ BOM 边界（不被识别，test-plan L2-9）+ `#If False` 边界（禁用区不识别，test-plan L2-10）。
- L3 Scripting：`.vbx` 首行 shebang + 正常代码编译通过；与 `#R`/`#Load` 共存。
- L4 REPL/边界：REPL 首行 `#!` 空转；BOM；`Content` API 返回路径。
- 测试位置：`Compilers\VisualBasic\Test\...`（编译诊断）+ `Scripting\VisualBasicTest\`（脚本层，跑程序集 `-automated`）。

## 边界与迁移影响

- **`.vbx` 首行**：从「编译错误」变「合法 trivia」——特性目标；行号不漂移。
- **常规 `.vb`**：仍报 `ERR_ShebangDirectiveOnlyAllowedInScripts`，不污染项目编译。
- **REPL**：首行 `#!` 接受并空转（遵循 C# `csi`）。
- **`#If False` 禁用区**：禁用区内 `#!` 不被识别为指令（无节点、无诊断）；位置/模式检查仅对被识别的指令（激活区）生效。
- **编辑器/LSP**：零诊断（语法树自动继承）；注释色分类适配点随 LSP 复制 Workspaces 层落地（本 fork 无该层）。
- **公开 API**：`ShebangDirectiveTriviaSyntax` + `Content`/`WithContent` 进 Unshipped；`Shipped.txt` 字节不变（上游合并纪律）。

## 与 C# 对照（拿不准时看这里）

| 环节 | C# | VB fork |
|------|-----|---------|
| 派发 | `#`+`ExclamationToken` 恒按 shebang（`DirectiveParser.cs:111-120`） | `Case SyntaxKind.ExclamationToken`（`ParseConditional.vb:41`） |
| 位置 | `hashPosition != 0 \|\| hash.HasTrailingTrivia` → CS9378（:116） | 同语义 → `ERR_ShebangDirectiveNotOnFirstLine`（位置信号 M1 核实） |
| 门控 | `!= Script && !FileBasedProgram` → CS9314（:691） | `If Not IsScript` → `ERR_ShebangDirectiveOnlyAllowedInScripts` |
| 整行消费 | lexer 级 `PreprocessingMessageTrivia`（`Lexer.cs:2485-2534`） | parser 级 skip → `SkippedTokensTrivia` 尾随 `ExclamationToken` |
| 节点/API | `ShebangDirectiveTriviaSyntax` + `Content`（`ShebangDirectiveTriviaSyntax.cs`） | `ShebangDirectiveTriviaSyntax` + `Content`（手写 partial） |
| severity | error（`AddError`） | error（两枚错误码） |
| 树形测试 | `ScriptParsingTests.cs:10201-10324` / `IgnoredDirectiveParsingTests.cs:160-814` | test-plan L1/L2 镜像 |
