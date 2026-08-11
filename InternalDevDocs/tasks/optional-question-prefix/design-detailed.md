# 详细设计：REPL 表达式开头 ? 可选

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-optional-question-prefix.md`（Active）→ `../../meetings/meeting-optional-question-prefix.md`（Active，RESOLUTION #1-#5）→ `design-overview.md`（F1 概要，已通过）。
> 本设计吸收 F1 验证问题清单 A-F 与 vortex 失败点 P-001/P-002/P-003，把概要落到「可被 F3 实施者直接照做」的代码级设计。
> 源码事实均已用内置 Read 精读核实，引用以 `文件:行号` 给出；文件相对路径均相对仓库根 `G:\Projects\VBScriptDotNet\`，编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`）。

## 0. 核心判定原则（贯穿全文，防 P-001）

自动打印的**充要条件** = 「脚本提交的**末尾**语句是表达式语句，且该表达式**按常规绑定的结果形态判定为『值引用（变量/字段/属性）』**」。

- **触发条件锚定在绑定结果形态（`BoundKind`），不锚定在「所有末尾表达式」，也不枚举错误码**：绑定结果为**值引用**（`Local`/`Field`/`Parameter`/`PropertyAccess`/`LateInvocation`+PropertyGroup）→ 值是值 → **自动打印**；`MySub`（Void Sub 调用）、`Console.WriteLine("hi")`（Void 调用）绑定为 `BoundCall` → 属于合法语句，**不改义、不打印**。只有「绑定结果为值引用」的末尾语句才打印（`PropertyAccess`/`LateInvocation`+PropertyGroup 路径需抑制 `ERR_PropertyAccessIgnored` 改走 RValue；`Local`/`Field`/`Parameter` 路径本就无诊断）。错误码不是稳定契约、解析层错误与绑定层错误不同质，故不按错误码枚举触发。
- 解析层对「**裸表达式导致标签/误解析**」的形态与「**裸标识符**」都产出表达式语句；裸标识符（`before`/`Now`/`MySub`）解析为**裸表达式**（`IdentifierName`，不包成 `X()`，§2.1(c)），方法组 vs 变量/属性的消歧**留在绑定层**（§3.3 方法组消歧；P-001：落点措辞与判定原则必须同一段表述）。
- 「是否末尾」binder 线程机制**已定案**为语法节点同一性比较（P-003，见 §3.2）。

---

## 1. 改动清单总览

| 文件（相对仓库根） | 改动函数 | 改动形状 | 目的 |
|---|---|---|---|
| `Compilers\VisualBasic\Portable\Parser\Parser.vb` | `ParseStatementInMethodBodyCore` — `IntegerLiteralToken` 分支（:1092-1095）与 `Case Else`（:1241-1251） | 修改 | Script 顶层语句首裸数值/字面量（`1 + 2`、`"a" & "b"`、`True`）从标签/误解析改为表达式语句；`1:` 标签保留 |
| `Compilers\VisualBasic\Portable\Parser\ParseStatement.vb` | `ParseAssignmentOrInvocationStatement`（:1087-1114） | 修改 | Script 顶层语句首被 `MakeInvocationExpression` 误包的**二元表达式**（`x > 5`、`x + 1`）改为完整表达式语句；**裸标识符**（`before`/`Now`/`MySub`）改为**裸表达式**（不包成 `X()`）；赋值不变 |
| `Compilers\VisualBasic\Portable\Parser\ParseStatement.vb` | 新增 `ParseScriptExpressionStatement()` 与 `ParseBinaryExpressionContinuation(left)` | 新增 | 复用 `ParseExpressionCore()` 产出 `ExpressionStatement`（字面量起始）；从已解析左操作数继续二元解析（标识符起始） |
| `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb` | `BindExpressionStatement`（:2608-2630） | 修改 | 末尾脚本语句抑制「值被丢弃」诊断（BC30545）；非末尾裸表达式报 BC31003；**Case Else 增加方法组拦截**（裸 `MySub`/`MyFunc` 按调用语句重分类，变量/属性引用走 RValue） |
| `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb` | `BindInvocationExpressionAsStatement`（:2715-2717）与 `ReclassifyInvocationExpressionAsStatement`（:2719-2755） | 修改（加可选参数） | 透传「末尾脚本语句」标志，抑制 `ERR_PropertyAccessIgnored`（:2726/:2742 两处） |
| `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb` | 新增 `IsFinalStatementOfSubmission(statement As StatementSyntax)` | 新增 | P-003 定案：binder 线程「是否提交末尾语句」的同一性判定 |
| 其余（宿主/编译信息/退出码机制） | — | **零改动** | 依据见 §5、§6 |

零改动清单：`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb`（`HasSubmissionResult` :816-868）、`Compilers\VisualBasic\Portable\Analysis\InitializerRewriter.vb`（`BuildScriptInitializerBody` :174-186、结果回传判定 :202-223）、`Compilers\VisualBasic\Portable\Compilation\VisualBasicScriptCompilationInfo.vb`（`PreviousScriptCompilation`）、`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（:201/:224/:296-320）、`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCompiler.vb`（:96）。

---

## 2. 解析层详细设计

### 2.0 门控：`IsTopLevelScript`

解析层所有新分支统一加门控：

```vb
Private ReadOnly Property IsTopLevelScript As Boolean
    Get
        ' 仅 SourceCodeKind.Script 且位于编译单元顶层（提交顶层），排除 Regular 与脚本内嵌套方法。
        Return IsScript AndAlso Context.BlockKind = SyntaxKind.CompilationUnit
    End Get
End Property
```

- `IsScript` 已存在：`Parser.vb:79-83`（`_scanner.Options.Kind = SourceCodeKind.Script`）。
- `Context.BlockKind` 用法先例：`Parser.vb:800/:817/:823`（`If Context.BlockKind = SyntaxKind.CompilationUnit`）。
- **Regular 编译零影响证明**：`SourceCodeKind.Regular` 时 `IsScript=False` → `IsTopLevelScript=False` → 新分支全部不进入，走原路径（`1 + 2` 仍 BC30801 标签错、`x > 5` 仍误包、`?` 语句在 Regular 仍 BC31003）。**嵌套方法零影响**：脚本文件里 `Sub`/`Function` 体内 `Context.BlockKind <> CompilationUnit` → 行为不变（`.vbx` 嵌套代码的裸表达式仍报原错误，见 §6 代价面）。

### 2.1 语句首裸表达式识别与分发

#### (a) `1 + 2` vs `1:` —— `IntegerLiteralToken` 分支（`Parser.vb:1092-1095`）

现状：`IntegerLiteralToken` + `IsFirstStatementOnLine` → `ParseLabel`（`ParseStatement.vb:1573`）；无冒号 → `ERR_ObsoleteLineNumbersAreLabels`（BC30801，`ParseStatement.vb:1578-1579`）。

改为：

```vb
Case SyntaxKind.IntegerLiteralToken
    If IsTopLevelScript AndAlso Not (IsFirstStatementOnLine(CurrentToken) AndAlso PeekToken(1).Kind = SyntaxKind.ColonToken) Then
        ' 裸数值表达式（1 + 2）：解析为表达式语句；1:（标签）由下一条保留。
        Return ParseScriptExpressionStatement()
    End If
    If IsFirstStatementOnLine(CurrentToken) Then
        Return ParseLabel()
    End If
```

- token 判定：**`IsFirstStatementOnLine(CurrentToken) AndAlso PeekToken(1).Kind = SyntaxKind.ColonToken` 为真 → 标签**（`1:`）；否则在顶层脚本 → 表达式语句（`1 + 2`）。与 `ShouldParseAsLabel()`（`ParseStatement.vb:1569-1571`）在 IntegerLiteralToken 下的判据一致。
- 顶层脚本里 `x = 5 : 1 + 2`（非行首）也会走表达式语句分支（`IsTopLevelScript` 为真且非标签），与「裸表达式即表达式语句」语义一致。

#### (b) `x > 5` 被 `MakeInvocationExpression` 误包 —— `ParseAssignmentOrInvocationStatement`（`ParseStatement.vb:1087-1114`）

现状：`ParseTerm()` 取到 `x` → 非赋值 → `MakeInvocationExpression(x)`（定义 `ParseStatement.vb:1116`；无参调用创建 `:1153`）→ `>` 非 `CanEndExecutableStatement`（`ParseScan.vb:104-106`）→ `ParseArguments` 期待 `(` 却遇 `>` → `ERR_ObsoleteArgumentsNeedParens`（BC30800，`ParseStatement.vb:1151`）且 `> 5` 残留。

在现有 `ParseTerm()` 之后、`MakeInvocationExpression` 之前插入 `IsTopLevelScript` 分支（**关键：不把整句交给 `ParseExpressionCore`，因为 `ParseExpressionCore` 会把 `=` 当比较二元运算符，`x = 5` 会被误解析为比较而非赋值**——证据：`ParseExpression.vb:134` `If Not CurrentToken.IsBinaryOperator Then Exit Do`，`=` 在表达式上下文是二元比较）：

```vb
Private Function ParseAssignmentOrInvocationStatement() As StatementSyntax
    Dim target As ExpressionSyntax = ParseTerm()

    If target.ContainsDiagnostics Then
        target = ResyncAt(target, SyntaxKind.EqualsToken)
    End If

    If SyntaxFacts.IsAssignmentStatementOperatorToken(CurrentToken.Kind) Then
        ' ===== 现有赋值分支（原样保留，x = 5 / a.b = 5）=====
        Dim operatorToken As PunctuationSyntax = DirectCast(CurrentToken, PunctuationSyntax)
        GetNextToken()
        TryEatNewLine()
        Dim source = ParseExpressionCore()
        If source.ContainsDiagnostics Then
            source = ResyncAt(source)
        End If
        Return MakeAssignmentStatement(target, operatorToken, source)
    End If

    If IsTopLevelScript AndAlso CurrentToken.IsBinaryOperator Then
        ' ===== 新增：x > 5 / x + 1 —— ParseTerm 只取到左操作数，继续二元解析 =====
        Return SyntaxFactory.ExpressionStatement(ParseBinaryExpressionContinuation(target))
    End If

    If IsTopLevelScript AndAlso target.Kind = SyntaxKind.IdentifierName AndAlso CanEndExecutableStatement(CurrentToken) Then
        ' ===== 新增：裸标识符（before/Now/MySub）解析为裸表达式，不包成 X()（§2.1(c)）=====
        Return SyntaxFactory.ExpressionStatement(target)
    End If

    Return SyntaxFactory.ExpressionStatement(MakeInvocationExpression(target))
End Function
```

- 判定要点：
  - **`CurrentToken.IsBinaryOperator`（且 `IsTopLevelScript`）→ 二元延续**：`x > 5`（`>` 二元）→ `ExpressionStatement(x > 5)`；`x + 1` 同理。`ParseBinaryExpressionContinuation`（见 §2.2）从已解析的 `target` 起，按 `ParseExpressionCore` 同款二元循环（`ParseExpression.vb:130-166`）续建 `BinaryExpression`。
  - **非二元（EOF/换行/`:` 等）→ 分两类**：若 `target` 是**裸标识符**（`SyntaxKind.IdentifierName`）→ 解析为**裸表达式**（`ExpressionStatement(target)`，不包 `()`，见 §2.1(c)）；若 `target` 是**成员访问** → 保持现状 `MakeInvocationExpression` 包成 `X()`：`obj.MySub`/`DateTime.Now`/`Console.WriteLine("hi")` 保留调用形状。这样**不破坏无括号成员 Sub 调用**（`obj.MySub` 是合法语句）——若把裸成员访问打成裸表达式，`BindRValue(方法组)` 会报错（BC30491 `ERR_VoidValue`），故**绝不能**把 MemberAccess 打成裸表达式。
  - **`=` 不进入二元分支**：`=` 是赋值运算符，被上方赋值分支先截获；因此 `x = 5` 保持赋值语义，不受影响。
- 与上下文关键字（`Mid`/`Async`/`Iterator`/`Await`/`Yield`）的消歧已在 `Parser.vb:1107-1138` 先于 `ParseAssignmentOrInvocationStatement` 完成（如 `Mid(` → `ParseMid`），本分支不重复处理。

#### (c) `before`/`Now`/`MySub`（裸标识符）—— 解析为**裸表达式**（不包成 `X()`）

- 在 (b) 二元延续分支之后、`MakeInvocationExpression` 之前插入 `IsTopLevelScript` 分支（`target.Kind = IdentifierName` 且 `CanEndExecutableStatement(CurrentToken)`，即语句在此结束）：

  ```vb
  If IsTopLevelScript AndAlso target.Kind = SyntaxKind.IdentifierName AndAlso CanEndExecutableStatement(CurrentToken) Then
      ' 裸标识符（before/Now/MySub）：解析为裸表达式，不包成 X()。
      Return SyntaxFactory.ExpressionStatement(target)
  End If
  Return SyntaxFactory.ExpressionStatement(MakeInvocationExpression(target))
  ```

- `CanEndExecutableStatement`（`ParseScan.vb:104-106`）：EOF/换行/`:` 为真 → 裸表达式；`(`（显式调用，如 `MySub(...)`）为假 → 仍走 `MakeInvocationExpression` 解析实参；二元运算符被 (b) 分支先截获。
- 解析层**不**区分 `before`（变量，值）与 `MySub`（方法，无括号调用）——两者语法同形（裸标识符）。区分交给绑定层（§3.3 方法组消歧），这是 P-001 的消歧落点。
- **成员访问不适用本分支**：`obj.MySub`/`DateTime.Now` 是 `SimpleMemberAccessExpression`（`target.Kind <> IdentifierName`），保持 `X()` 调用形状（§2.1(b) 底线：不得把 MemberAccess 打成裸表达式，否则无括号成员 Sub 调用会破坏）。

#### (d) 其它字面量/表达式起始 token —— `Case Else`（`Parser.vb:1241-1251`）

`"hello"`、`True`、`Nothing`、`CDate(...)` 等目前落入 `Case Else` → `CanFollowStatement` 不成立 → `ReportUnrecognizedStatementError`。顶层脚本下改为：

```vb
Case Else
    If IsTopLevelScript AndAlso Not CanFollowStatement(CurrentToken) Then
        ' 字面量/表达式起始 token（字符串、True/False、Nothing 等）：顶层脚本按裸表达式处理。
        Return ParseScriptExpressionStatement()
    End If
    If CanFollowStatement(CurrentToken) Then
        Return InternalSyntaxFactory.EmptyStatement
    End If
```

- `CanFollowStatement(CurrentToken)` 为真（该 token 可跟在语句后，如另一语句起始）时保持原 EmptyStatement 行为，避免误吞。
- 该分支保守，只补「顶层脚本 + 非语句延续」这一空白；所有已知语句关键字仍被上方 Select 各 `Case` 先截获。

### 2.2 新 helper 签名草案

```vb
' ParseStatement.vb（与 ParseExpression.vb 同属 partial Parser，可直接调用其私有成员）
Private Function ParseScriptExpressionStatement() As StatementSyntax
    ' 字面量/其它表达式起始 token 的裸表达式：无赋值歧义，直接整句解析。
    Dim expr As ExpressionSyntax = ParseExpressionCore()
    If expr.ContainsDiagnostics Then
        expr = ResyncAt(expr)
    End If
    Return SyntaxFactory.ExpressionStatement(expr)
End Function

Private Function ParseBinaryExpressionContinuation(left As ExpressionSyntax) As ExpressionSyntax
    ' 从已解析左操作数继续，复刻 ParseExpressionCore 的二元循环（ParseExpression.vb:130-166）。
    ' 只处理真正的二元运算符（CurrentToken.IsBinaryOperator），= 不会进入（被赋值分支截获）。
    Dim expression = left
    Do
        If Not CurrentToken.IsBinaryOperator Then
            Exit Do
        End If
        Dim precedence As OperatorPrecedence = KeywordTable.TokenOpPrec(CurrentToken.Kind)
        If precedence <= OperatorPrecedence.PrecedenceNone Then
            Exit Do
        End If
        Dim operatorToken As SyntaxToken = ParseBinaryOperator()
        Dim rightOperand As ExpressionSyntax = ParseExpressionCore(precedence)
        expression = SyntaxFactory.BinaryExpression(GetBinaryOperatorHelper(operatorToken), expression, operatorToken, rightOperand)
    Loop
    Return expression
End Function
```

（`ParseExpressionCore` 签名：`Private Function ParseExpressionCore(Optional pendingPrecedence As OperatorPrecedence = PrecedenceNone, Optional bailIfFirstTokenRejected As Boolean = False) As ExpressionSyntax`，`ParseExpression.vb:49-52`；`IsBinaryOperator`/`TokenOpPrec`/`ParseBinaryOperator`/`GetBinaryOperatorHelper` 均为 `ParseExpression.vb` 既有成员。）

---

## 3. 绑定层详细设计

### 3.0 门控：`BindingTopLevelScriptCode` + `IsScriptInitializer`

绑定层新逻辑一律先判定「当前在绑定脚本提交顶层语句」：

```vb
Private Function IsBindingTopLevelScriptInitializer() As Boolean
    Return BindingTopLevelScriptCode AndAlso DirectCast(ContainingMember, MethodSymbol).IsScriptInitializer
End Function
```

- `BindingTopLevelScriptCode`：`Binder.vb:396-411`（顶层脚本代码，含 script constructor / script initializer / 脚本字段初始化器）。
- `IsScriptInitializer`：`MethodSymbol.vb:500` 重写于 `SynthesizedInteractiveInitializerMethod.vb:39-43`（顶层脚本语句被合成为该方法的函数体，`TopLevelCodeBinder.vb` 绑定）。
- **Regular 零影响**：Regular 无 script initializer → `IsScriptInitializer=False` → 新逻辑不进入。**脚本内嵌套方法**：`ContainingMember` 是子方法而非 initializer → 不进入。

### 3.1 触发条件与改走 RValue 的精确落点

> P-001：先按 `BindExpressionStatement` 常规绑定，**仅当绑定结果形态为『值引用（`Local`/`Field`/`Parameter`/`PropertyAccess`/`LateInvocation`+PropertyGroup）』** 且是提交末尾语句时才打印（`PropertyAccess`/`LateInvocation`+PropertyGroup 需抑制 `ERR_PropertyAccessIgnored` 改走 RValue）；**方法组（`BoundMethodGroup`）按调用语句重分类不打印**；不能写成「所有末尾表达式一律 BindRValue」。

`BindExpressionStatement`（`Binder_Statements.vb:2608-2630`）改后形状：

```vb
Private Function BindExpressionStatement(statement As ExpressionStatementSyntax, diagnostics As BindingDiagnosticBag) As BoundStatement
    Dim expression = statement.Expression
    Dim isFinalStatement = IsFinalStatementOfSubmission(statement)
    Dim boundExpression As BoundExpression

    Select Case expression.Kind
        Case SyntaxKind.InvocationExpression,
             SyntaxKind.ConditionalAccessExpression
            ' 常规绑定；仅末尾脚本语句抑制「值被丢弃」诊断（BC30545 两处）。
            boundExpression = BindInvocationExpressionAsStatement(expression, diagnostics,
                                                                  suppressPropertyAccessIgnored:=isFinalStatement)

        Case SyntaxKind.AwaitExpression
            boundExpression = BindAwait(DirectCast(expression, AwaitExpressionSyntax), diagnostics, bindAsStatement:=True)

        Case Else
            ' 现状 Case Else 本就 BindRValue 无诊断（注释：交互式顶层表达式，:2622-2624）。
            ' 顶层脚本非末尾的裸表达式（1 + 2 / x > 5 / before）必须保持「中间行仍报错」：补 BC31003。
            If IsBindingTopLevelScriptInitializer() AndAlso Not isFinalStatement Then
                ReportDiagnostic(diagnostics, statement, ERRID.ERR_UnexpectedExpressionStatement)
                boundExpression = BindRValue(expression, diagnostics)
                Return New BoundExpressionStatement(statement, boundExpression)
            End If
            ' F5 方法组消歧：裸标识符先 BindExpression（不能直接 BindRValue）。
            '   BindRValue(方法组) 会把 Void Sub 自动无参调用后报 BC30491（ERR_VoidValue，Binder_Expressions.vb:1257-1261），
            '   破坏「无括号 Sub 调用合法」不变量，故方法组必须按「调用语句」重分类而非按值绑定。
            Dim candidate = BindExpression(expression, diagnostics)
            If candidate.Kind = BoundKind.MethodGroup Then
                boundExpression = ReclassifyInvocationExpressionAsStatement(
                    BindInvocationExpression(candidate.Syntax, candidate.Syntax, ExtractTypeCharacter(candidate.Syntax),
                                             DirectCast(candidate, BoundMethodGroup), s_noArguments, Nothing,
                                             diagnostics, callerInfoOpt:=candidate.Syntax),
                    diagnostics)
            Else
                boundExpression = MakeRValue(candidate, diagnostics)
            End If
    End Select

    WarnOnUnobservedCallThatReturnsAnAwaitable(statement, boundExpression, diagnostics)
    Return New BoundExpressionStatement(statement, boundExpression)
End Function
```

- **Invocation/ConditionalAccess 分支**：`DateTime.Now`（成员访问，仍为 `DateTime.Now()` 形状）→ `BindExpression` 得 `BoundPropertyAccess` → `ReclassifyInvocationExpressionAsStatement` 在 `PropertyAccess` 分支 `MakeRValue` 后**若 `isFinalStatement` 则跳过** `ERR_PropertyAccessIgnored`（:2726）→ 以 RValue 形态返回，无错误。`Console.WriteLine("hi")` → `BoundCall`（Void）→ 不进 PropertyAccess 分支 → 无诊断 → 合法语句，不打印。
- **Case Else 分支**：
  - **方法组拦截**：裸 `MySub`（裸标识符 → `BoundMethodGroup`）→ 无参调用重分类 → `BoundCall`(Sub→Void) → 合法语句，不打印；裸 `MyFunc` → `BoundCall`(非 Void) → 现状已打印（不变）。
  - **值引用**：裸 `before`/`Now`（`BoundLocal`/`BoundPropertyGroup` → `MakeRValue` → `BoundLocal`/`BoundPropertyAccess` 值，无诊断）→ `HasSubmissionResult` 非 Void → 打印。
  - **字面量/二元**：`1 + 2`（末尾）→ 非方法组 → `MakeRValue` 无诊断 → 合法 → 打印 3；`x > 5` → 打印 False。
  - 非末尾 → BC31003 → `HasAnyErrors` 短路（`CommandLineRunner.cs:300`）→ 整个提交不运行，中间行裸表达式保持报错。

### 3.2 P-003 定案：「是否末尾」binder 线程机制

在两种候选间**定案机制①（binder 内语法节点同一性比较）**，可行性论证如下。

```vb
Private Function IsFinalStatementOfSubmission(statement As StatementSyntax) As Boolean
    If Not IsBindingTopLevelScriptInitializer() Then
        Return False
    End If
    Dim root = DirectCast(statement.SyntaxTree.GetRoot(), CompilationUnitSyntax)
    Return root.Members.LastOrDefault() Is statement
End Function
```

- **为什么可行**：绑定阶段操作的是红节点（`BindStatement(node As StatementSyntax)`，bound 节点的 `.Syntax` 即原语法树节点）；`statement.SyntaxTree.GetRoot()` 得到同一棵树的 `CompilationUnitSyntax`，其 `Members`（`CompilationUnitSyntax.Members`，`Generated\Syntax.xml.Syntax.Generated.vb:493`）中 `LastOrDefault()` 与正在绑定的 `statement` **引用同一节点**（绿节点单树不复用保证，见 `CompilationUnitContext.vb:205-209` DEBUG 断言）。`HasSubmissionResult`（`VisualBasicCompilation.vb:833`）用同一 `root.Members.LastOrDefault()`，两处口径天然一致。
- **机制②（解析层仿 C# 缺分号打标记）不采用**：需给 `ExpressionStatementSyntax` 增加语法标志位（改 `Syntax.xml` + 生成代码 + 绿节点工厂），改动面大；且 C# 的触发信号是「缺分号」这一语法事实，VB 无分号、等价信号本就是「绑定结果形态（`BoundKind`）」，机制① 与信号同源。
- 性能：`GetRoot()` 为树缓存常量操作，每表达式语句一次可接受。
- `ContinueWith`（交互后续提交）天然正确：每次提交是独立新树，`root.Members` 只含本次新语句。

### 3.3 `MySub` / `Console.WriteLine("hi")` 排除不变量（P-001）

判定不变量（绑定层，单一判定）：

> **仅当绑定结果形态为「值引用」（`BoundKind.Local`/`Field`/`Parameter`/`PropertyAccess`/`LateInvocation`+PropertyGroup）且位于提交末尾时才自动打印。其中 `PropertyAccess`/`LateInvocation`+PropertyGroup 需抑制 `ERR_PropertyAccessIgnored` 改走 RValue（`Local`/`Field`/`Parameter` 本就无诊断）；绑定为 `BoundMethodGroup` 的语句（裸 `MySub`/`MyFunc`）按调用语句重分类不打印；绑定为 `BoundCall` 的语句（Void Sub 调用、带值调用）与赋值行为不变。**

| 提交 | 解析形状 | 绑定结果（`BoundKind`） | 触发判定 | 末尾行为 |
|---|---|---|---|---|
| `before`（`Dim before = Now` 后） | `ExpressionStatement(before)`（裸标识符） | `BoundLocal` | 变量引用（值）→ 打印 | RValue → 打印（**本版新增**） |
| `Now` | `ExpressionStatement(Now)`（裸标识符） | `BoundPropertyGroup` → `MakeRValue` → `BoundPropertyAccess` | 属性引用（值）→ 打印 | RValue → 打印（无需 BC30545 抑制） |
| `MySub` | `ExpressionStatement(MySub)`（裸标识符） | `BoundMethodGroup` → 无参调用重分类 → `BoundCall`(Sub→Void) | 方法组 → 调用语句不打印 | 合法语句，不打印 |
| `MyFunc`（裸 Function 调用） | `ExpressionStatement(MyFunc)`（裸标识符） | `BoundMethodGroup` → 无参调用重分类 → `BoundCall`(非 Void) | 方法组 → 调用语句不打印 | **现状已打印**（非 Void 表达式语句），不变 |
| `Console.WriteLine("hi")` | `ExpressionStatement(Console.WriteLine("hi"))`（成员访问，`X()` 形状） | `BoundCall`(Sub→Void) | 真正调用 → 不打印 | 合法语句，不打印 |
| `DateTime.Now` | `ExpressionStatement(DateTime.Now())`（成员访问，`X()` 形状） | `BoundPropertyAccess` | 属性引用（值）→ 打印 | 抑制 BC30545 → RValue → 打印 |
| `SomeFunction()`（带返回值调用） | `ExpressionStatement(SomeFunction())` | `BoundCall`(非 Void) | 真正调用 → 不打印 | **现状已打印**（非 Void 表达式语句），不变 |
| `x = 5` | `SimpleAssignmentStatement` | `BoundAssignment`（不走本函数） | 赋值 → 不打印 | 合法语句，不打印 |

> 注：`before`/`Now`/`MySub`（裸标识符）解析同形，绑定层靠「**值引用（`BoundLocal`/`BoundPropertyAccess`）**」vs「**方法组（`BoundMethodGroup` → 无参调用）**」区分——这就是 P-001 要求的消歧方案（按绑定结果形态，不枚举错误码）。F1-A 第二分支（解析层把裸标识符打成裸表达式 + 绑定层方法组消歧）被采纳。

### 3.4 `ReclassifyInvocationExpressionAsStatement` 加参透传

`Binder_Statements.vb:2715-2755`：

```vb
Private Function BindInvocationExpressionAsStatement(expression As ExpressionSyntax, diagnostics As BindingDiagnosticBag,
                                                     Optional suppressPropertyAccessIgnored As Boolean = False) As BoundExpression
    Return ReclassifyInvocationExpressionAsStatement(BindExpression(expression, diagnostics), diagnostics, suppressPropertyAccessIgnored)
End Function

Friend Function ReclassifyInvocationExpressionAsStatement(boundInvocation As BoundExpression, diagnostics As BindingDiagnosticBag,
                                                          Optional suppressPropertyAccessIgnored As Boolean = False) As BoundExpression
    Select Case boundInvocation.Kind
        Case BoundKind.PropertyAccess
            boundInvocation = MakeRValue(boundInvocation, diagnostics)
            If Not boundInvocation.HasErrors AndAlso Not suppressPropertyAccessIgnored Then
                ReportDiagnostic(diagnostics, boundInvocation.Syntax, ERRID.ERR_PropertyAccessIgnored)   ' :2726
            End If
        Case BoundKind.LateMemberAccess
            boundInvocation = DirectCast(boundInvocation, BoundLateMemberAccess).SetAccessKind(LateBoundAccessKind.Call)
        Case BoundKind.LateInvocation
            Dim lateInvocation = DirectCast(boundInvocation, BoundLateInvocation).SetAccessKind(LateBoundAccessKind.Call)
            boundInvocation = lateInvocation
            If Not lateInvocation.HasErrors AndAlso TryCast(lateInvocation.MethodOrPropertyGroupOpt, BoundPropertyGroup) IsNot Nothing AndAlso
               Not suppressPropertyAccessIgnored Then
                ReportDiagnostic(diagnostics, boundInvocation.Syntax, ERRID.ERR_PropertyAccessIgnored)   ' :2742
            End If
        Case BoundKind.ConditionalAccess
            ' 递归调用补透传参数（:2750）：按本函数签名递归处理 WhenNotNull，透传 suppressPropertyAccessIgnored
            boundInvocation = ReclassifyInvocationExpressionAsStatement(
                DirectCast(boundInvocation, BoundConditionalAccess).WhenNotNull, diagnostics, suppressPropertyAccessIgnored)
    End Select
    Return boundInvocation
End Function
```

- `Call` 语句路径 `BindCallStatement`（:2709-2713）也调用 `BindInvocationExpressionAsStatement`，因 `suppressPropertyAccessIgnored` 默认 `False` 而**行为不变**（`Call Now` 仍报 BC30545，合法）。
- **本路径服务的成员访问起始形态**：裸标识符（`before`/`Now`/`MySub`）已改走 Case Else（§3.1），不再经本函数；**成员访问**（`DateTime.Now`/`obj.MySub`/`Console.WriteLine("hi")`）仍为 `X()` 调用形状、经本函数——`DateTime.Now` 需抑制 BC30545 打印，`obj.MySub`/`Console.WriteLine("hi")` 为 `BoundCall`(Void) 不进 PropertyAccess 分支、保持不打印。

### 3.5 `HasSubmissionResult` 互动

- 末尾 `before`/`Now`（裸标识符）经 Case Else 绑定为 `BoundExpressionStatement(BoundLocal`/`BoundPropertyAccess)`（值引用、无错、非 Void）→ `HasSubmissionResult`（`VisualBasicCompilation.vb:846-849`）走 `ExpressionStatement` 分支：`info.Type <> Void` → **True**。
- `InitializerRewriter.RewriteInitializersAsStatements`（:202-223）：REPL 提交 `ResultType = Object` → 末尾 `GlobalStatementInitializer` 是 `ExpressionStatement` 且非 Void → `submissionResult = expr` → 方法返回该值 → 宿主 `HasReturnValue()` True → `globals.Print(state.ReturnValue)`（`CommandLineRunner.cs:313-315`）。
- **不需要**走 PrintStatement「恒 True」分支（:840-844）；ExpressionStatement 非 Void 分支已足够，因为自动打印只针对非 Void 值表达式。这是对 F1 概要 §2.2「绑定为 PrintStatement 语义」的细化：**不新建 PrintStatementSyntax，而是把 ExpressionStatement 绑定为 RValue**，`HasSubmissionResult` 复用现有分支。

---

## 4. 触发判定与排除清单（BoundKind 形态，F1 §4/RESOLUTION #5 回归确认）

> **判定依据 = 绑定结果形态（`BoundKind`），不枚举错误码**。错误码不是稳定契约（长期维护面大），且解析层错误（标签/误包）与绑定层错误（BC30545）本质不同、塞进一张清单是缝合；`1 + 2`/`x > 5` 在**解析层就失败**，根本不产生绑定层错误码——清单还没碰到它们就短路。故解析层错误靠「修误解析」解决（§2），绑定层只按「绑定结果形态」判定触发。

| BoundKind 形态（绑定结果） | 提交示例 | 判定 | 诊断码（仅辅助说明，不作判定依据） |
|---|---|---|---|
| `BoundKind.Local` | `before`（`Dim before = Now` 后） | **打印**（变量引用，值是值） | 无诊断 |
| `BoundKind.Field` | 模块/实例字段 | **打印**（字段引用，值是值） | 无诊断 |
| `BoundKind.Parameter` | `Sub Test(x As Integer)` 内敲 `x` | **打印**（参数引用，值是值） | 无诊断 |
| `BoundKind.PropertyAccess` | `DateTime.Now`（成员访问，`X()` 形状）；裸 `Now` 经 `BoundPropertyGroup → MakeRValue` 转此形态 | **打印**（属性引用，值是值） | 成员访问形态常规绑定报 BC30545（`Binder_Statements.vb:2726`）；末尾抑制 |
| `BoundKind.MethodGroup` | 裸 `MySub` / `MyFunc` | **不打印**（方法组 → 无参调用重分类为调用语句；Sub→Void 不打印，Function→非 Void 现状已打印） | 无诊断（若走 `BindRValue` 会报 BC30491 `ERR_VoidValue`，故必须拦截） |
| `BoundKind.Call` | `Console.WriteLine("hi")` / `SomeFunction()` | **不打印**（真正调用，保持合法语句） | 无诊断 |
| `BoundKind.LateMemberAccess` | `obj.Prop`（Object 接收者，晚绑定成员访问） | **不打印**（保持调用语义） | 无诊断 |
| `BoundKind.LateInvocation` + PropertyGroup | 晚绑定属性访问（Object 接收者） | **打印**（晚绑定属性） | 常规绑定报 BC30545（`Binder_Statements.vb:2742`）；末尾抑制 |
| `BoundKind.ConditionalAccess` | `a?.Prop` | **递归**：按 `WhenNotNull` 子形态判定 | 递归调用 `ReclassifyInvocationExpressionAsStatement`（:2750） |
| 其余（`BindExpressionStatement` Case Else → 非方法组 → `MakeRValue` 无诊断） | `1 + 2` / `x > 5` / `"a" & "b"` | **打印**（绑定即值，无「值被丢弃」信号） | 解析层已修误解析（BC30801/BC30800 消除），绑定层无诊断 |

- **`MySub` 排除不变量**：裸 `MySub`（`BoundMethodGroup` → 无参调用重分类）与 `Console.WriteLine("hi")`（`BoundCall`(Void)）→ 无诊断 → 合法语句不改义。区分靠「**值引用（`Local`/`PropertyAccess`）** vs **方法组/调用（`MethodGroup`→`BoundCall`）**」形态，不靠错误码。
- **Option Strict 无分叉（已定案）**：本表对 Strict On/Off 一视同仁——裸表达式作语句**没有目标类型上下文**（宽松模式隐式转换无处发生）；打印路径 `globals.Print`（`CommandLineRunner.cs:315`）不经任何 CType/隐式转换、`HasSubmissionResult` 只判「非 Void」。值引用路径（`Local`/`PropertyGroup → MakeRValue`）无 `OptionStrict` 门控；成员访问形态（`DateTime.Now`）走 `ReclassifyInvocationExpressionAsStatement`，其无 `OptionStrict` 门控，On/Off 都报 BC30545 → 末尾抑制 → 都打印。晚绑定（Strict Off）下 `obj.Prop` 绑定为调用 → 无诊断 → 不打印，天然正确（见表中 LateMemberAccess 行）。

**回归确认步骤（实现期逐个确认；RESOLUTION #5 吸收为「触发形态回归」而非「错误码枚举」）**：
1. 建一张 REPL 行为矩阵（§7 用例），逐行记录「绑定结果 `BoundKind`」与「是否自动打印」。
2. 对每个候选表达式（属性、无参方法、比较、算术、字符串、`Object` 变量、Option Strict Off 下同批），在绑定层打临时断言记录 `boundExpression.Kind`，对照本表。
3. 表外新发现的「值被丢弃」形态并入 `BoundKind` 分支或本表，更新并回归（不按错误码累加清单）。

---

## 5. 宿主层说明（零改动）

- **零改动依据（现状即够）**：
  - 交互循环 `RunInteractiveLoopAsync`（`CommandLineRunner.cs:224`）→ `BuildAndRunAsync`（:296）：`Compile` → `diagnostics.HasAnyErrors()` 短路（:300）→ `RunAsync` → `newScript.HasReturnValue()`（:313）为真 → `globals.Print(state.ReturnValue)`（:315）。
  - F2 绑定层产出「无错 + 非 Void 末尾表达式语句」后，`HasReturnValue()` 恒真，统一走 `globals.Print`。
  - `InitializerRewriter` 的 `ResultType.IsObjectType()` 门控（`InitializerRewriter.vb:208-211`）负责「REPL 打印 / 脚本丢弃」的最终分叉，与绑定层改动正交。
- **「重试为 `? <raw>`」fallback 仅作备用（主方案不采用）**：设计把解析层歧义收敛到「`IsTopLevelScript` 门控 + 绑定层方法组消歧」，`before`/`Now`/`MySub` 解析同形由绑定层区分，无需宿主文本重写兜底。主方案为解析/绑定层修正；fallback 仅当解析层裸表达式歧义风险过大、或未来需要「让 `.vbx` 里的裸表达式仍报错」时才启用 REPL/脚本区分开关（RESOLUTION #4 之 fallback 场景）。

---

## 6. `.vbx` 影响与退出码正交

- `SourceCodeKind.Script` 同时服务 `.vbx` 与交互提交：`VisualBasicCompiler.vb:96` `scriptParseOptions = parseOptions.WithKind(SourceCodeKind.Script)`；`SourceCodeKind.Interactive` 已 Obsolete → 编译器层无法区分。
- **`.vbx` 裸表达式**：从「报错」（BC30801/BC30800/BC30545）变「**静默合法 no-op**」（解析为表达式语句、绑定无错、不打印）——随 C# csx 先例接受（LDM-2020-04-15「we are ok with this remaining distance」）。
- **退出码正交机制（实证）**：`Script.CreateInitialScript<int>`（`CommandLineRunner.cs:206`）→ `ResultType = Int32` → `InitializerRewriter.vb:208` 的 `submissionResultType.IsObjectType()` 为 False → 末尾表达式**不回传**为方法返回值 → `RunAsync` 的 `ReturnValue` 仅由显式 `Return` 决定。现有测试实证：`? 21` → 退出 0、`? New System.Guid()` → 0、`Return 21` → 21、裸 `Return` → 0（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb:238-285`）。
- **受影响测试**：`:239-285` 四个退出码用例行为**不变**（绑定层改动不碰 `Return`/PrintStatement 语义）；新增 `.vbx` 裸表达式 no-op 用例（§7）。
- `.vbx` 嵌套 `Sub` 内的裸表达式仍报原错误（`IsTopLevelScript` 的 `BlockKind` 门控排除了嵌套方法）——代价面比「全文件放开」更窄，行为更保守。

---

## 7. 无副作用测试矩阵（F1-E 吸收，本设计单列）

> 约束：不发起网络、不写文件（除既有 `CreateIsolatedTempDirectory` 测试装置自身）、不启动进程、不写注册表。REPL 用例走 `CreateRunner(input:=...)` + `TestConsoleIO`（内存 StringReader/StringWriter，`CommandLineRunnerTests.vb:66-91`），纯内存。

### 7.1 REPL 交互（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

| # | 用例（建议测试方法名） | 输入（`input`） | 断言 | 无副作用 |
|---|---|---|---|---|
| 1 | `TestBarePropertyAccessPrints` | `Now` | 输出含 `Now` 求值结果（`> Now` 后跟日期值行），无错误 | 纯内存 REPL |
| 2 | `TestBareVariablePrints` | `Dim before = Now`（多行提交定义）后再 `before` | 输出日期值（`before` 绑定为 `BoundLocal` 值引用 → 非 Void → 自动打印） | 纯内存 REPL |
| 3 | `TestBareArithmeticExpressionPrints` | `1 + 2` | 输出 `3`，无 BC30801 | 纯内存 REPL |
| 4 | `TestBareComparisonExpressionPrints` | `x = 1` / `x > 5`（或单行 `x = 1 : x > 5`） | 末尾 `> x > 5` 后输出 `False` | 纯内存 REPL |
| 5 | `TestAssignmentDoesNotPrint` | `x = 5` | 无输出（仅 `>` 提示符） | 纯内存 REPL |
| 6 | `TestSideEffectCallDoesNotPrint` | `Console.WriteLine("hi")` | 无输出（WriteLine 是 Void，不自动打印） | 纯内存 REPL |
| 7 | `TestBareSubCallDoesNotPrint` | `Sub MySub()`+`End Sub`（多行提交定义）后再 `MySub` | `MySub` 不打印（裸 `MySub` → 方法组重分类为无括号 Sub 调用，合法不打印） | 纯内存 REPL |
| 8 | `TestExplicitQuestionStillPrints`（回归） | `? Now` | 输出与现状一致 | 纯内存 REPL |
| 9 | `TestRealSyntaxErrorStillErrors` | `Now +` | 仍报语法错误（缺失右操作数；错误码由现状 BC30800 类变为「缺失表达式」类 BC3xxxx，均为错误、不打印、不吞错） | 纯内存 REPL |
| 10 | `TestNonFinalBareExpressionInSubmissionStillErrors` | `1 + 2 : x = 5` | 报 BC31003（`1 + 2` 非末尾裸表达式仍报错），无打印 | 纯内存 REPL |
| 11 | `TestBareQualifiedPropertyPrints` | `DateTime.Now` | 输出日期值（成员访问保持 `DateTime.Now()` 形状 → 绑定层 BC30545 抑制 → 自动打印） | 纯内存 REPL |

> 补充：`DateTime.Now : x = 5`（成员访问，非末尾）仍报 **BC30545**（Invocation 分支不抑制，现有行为不变）；`Now : x = 5` / `1 + 2 : x = 5`（裸标识符/字面量，非末尾）报 **BC31003**——分别覆盖「非末尾裸表达式的两种报错形态」。

### 7.2 脚本模式 `.vbx`（`CommandLineRunnerTests.vb`，沿用既有 `CreateIsolatedTempDirectory` 装置）

| # | 用例 | 文件内容 | 断言 | 副作用 |
|---|---|---|---|---|
| 12 | `TestBareExpressionInScriptFileIsSilentNoOp` | `1 + 2` | 退出码 0、无输出（静默 no-op，不设退出码） | 仅临时目录（既有装置） |
| 13 | `TestBarePropertyInScriptFileIsSilentNoOp` | `Now` | 退出码 0、无输出 | 仅临时目录 |
| 14 | `TestTrailingQuestionDirectiveStillNoExitCode`（回归） | `? 21` | 退出码 0、无输出（`:239-248` 不变） | 仅临时目录 |
| 15 | `TestReturnStillSetsExitCode`（回归） | `Return 21` | 退出码 21（`:264-273` 不变） | 仅临时目录 |

### 7.3 `Regular` 编译零影响证明（`Compilers\VisualBasicTest`）

用 `VisualBasicCompilation.Create` + `SourceCodeKind.Regular`（不进宿主、不跑脚本），断言诊断不变，纯编译器 API 无副作用：

| # | 用例 | 源码 | 断言（诊断不变） |
|---|---|---|---|
| 16 | `TestRegularLiteralStatementStillLabelError` | `1 + 2`（Sub 内） | 仍产 BC30801 `ERR_ObsoleteLineNumbersAreLabels` |
| 17 | `TestRegularBareCallStillBinds` | `MySub`（Sub 内） | 仍合法、无 BC30545 |
| 18 | `TestRegularPrintStatementStillUnexpected` | `? 1` | 仍产 BC31003 `ERR_UnexpectedExpressionStatement` |
| 19 | `TestRegularPropertyAccessStatementStillIgnored` | `Now`（Sub 内） | 仍产 BC30545 `ERR_PropertyAccessIgnored` |

---

## 8. 边界与默认值（F1-F）

- **自动打印作用域（已定案，跟随 C#）**：**仅交互式 REPL**。`@vbi.rsp` / stdin 脚本输入：**不启用**（与 `.vbx` 同源、走 `RunScriptAsync` 不打印；不引入 REPL/脚本区分开关——「恢复脚本侧报错」不在本任务范围）。本版本按「编译器层无法区分」接受 `.vbx` 静默 no-op，作用域边界由「交互走 `HasReturnValue` 打印、脚本走退出码」的既有宿主分叉天然划定（C# 同一设定：interactive 打印、脚本丢弃）。
- **多行续行（`.` 续行）末尾表达式**：与单行一致——`IsFinalStatementOfSubmission` 基于**整个提交树**的 `root.Members.LastOrDefault()`，与行结构无关；整段提交以表达式语句收尾且绑定结果形态为值引用则打印。`. ` 续行中间出现的裸表达式（罕见）走「非末尾 → BC31003」。
- **Option Strict 无分叉（已定案）**：自动打印路径对 Strict On/Off 一视同仁。理由：① 裸表达式作语句时**没有目标类型上下文**，宽松模式的隐式转换根本无处发生；② 打印路径 `globals.Print(state.ReturnValue)`（`CommandLineRunner.cs:315`）不经任何 CType/隐式转换，`HasSubmissionResult` 只判「非 Void」；③ 值引用路径（裸 `before`/`Now` → `BindExpression` → `MakeRValue`）无 `OptionStrict` 门控，`Now` 静态绑定属性 On/Off 都求值为值 → 都打印；成员访问形态（`DateTime.Now`）走 `ReclassifyInvocationExpressionAsStatement`（`Binder_Statements.vb:2719-2755`）也无 `OptionStrict` 门控，On/Off 都报 BC30545 → 末尾抑制 → 都打印；晚绑定表达式（`obj.Prop`）绑定为调用 → 无诊断 → 不打印，天然正确。无需排除清单（§4 回归表确认即可）。
- **裸变量 `x` 单独打印（已定案，纳入本版本）**：`Dim before = Now` 后敲 `before` **自动打印**（对齐 C# `csi` 敲变量名即打印）。`before`（局部变量）走裸标识符 → **裸表达式**（`IdentifierName`，不包成 `X()`，§2.1(c)）→ 绑定为 `BoundLocal` 值引用 → 非 Void → RValue → 打印（§3.3）。方法名（`MySub`/`MyFunc`）同为裸标识符，绑定为 `BoundMethodGroup` → 无参调用重分类（§3.3 方法组消歧）：`MySub`（Sub）合法不打印、`MyFunc`（Function）维持现状打印。
- **「`?` 带变量名」已定案非开放问题**：显式 `?` 前缀路径（`Parser.vb:1233-1239` → `ParsePrintStatement` → `ParseExpressionCore`）把整句送进表达式上下文，`=` 是二元比较运算符（`ParseExpression.vb:134`、`Syntax\InternalSyntax\SyntaxToken.vb:308` EqualsToken ∈ IsBinaryOperator）→ `? x = 5` 打印比较结果 False；想赋值就写 `x = 5`（语句首赋值分支截获，`ParseStatement.vb:1096`），两者解析层就分开，无共存冲突。未来若确需新赋值语法，在 `ParsePrintStatement` 处扩展即可，与本设计的裸表达式路径互不干扰。

---

## 9. 迁移与兼容影响

- **`.vbx` 错误面变化**：裸表达式从「报错」变「静默 no-op」，退出码不变（Return-only）。这是已接受的代价（§6），但会改变既有 `.vbx` 脚本的成败：任何依赖「裸表达式报错」的脚本/CI 断言需更新为「不报错、不设退出码」。
- **语义模型树形状变化**：`1 + 2`、`x > 5`、`before`/`Now`（裸标识符）变为「`ExpressionStatement(裸表达式)` 合法树」——`1 + 2`/`x > 5` 不再有 `x()` 误包 + `> 5` 残留双错误；`before`/`Now` 不再有 `()` 调用形状，绑定层按值语义求值（`BoundLocal`/`BoundPropertyGroup → BoundPropertyAccess`）。**成员访问起始**（`DateTime.Now`/`obj.MySub`）因「不得破坏无括号成员 Sub 调用」而**保留 `X()` 调用形状**——`DateTime.Now` 绑定为 `BoundPropertyAccess` RValue、`obj.MySub` 保持调用语句，`GetTypeInfo` 类型正确。这是 P-001 的取舍：F1「不再停留 `Now()` 调用形状」在**二元/字面量/裸标识符**裸表达式上完全达成；对**成员访问起始**的表达式以语法形状换 `MySub`/`obj.MySub` 无歧义，绑定语义不受影响。
- **IntelliSense/错误恢复**：新增的 `ExpressionStatement(裸表达式)` 是合法语法节点，错误恢复路径更干净（二元表达式不再产生误包 `x()` + 残留 `> 5` 的双错误）；对 `Now`/`DateTime.Now` 的 `()` 形状，编辑器补全/高亮仍显示调用括号形态，属于已接受的 Cosmetic 差异。
- **Regular 零影响**：所有新分支经 `IsTopLevelScript` / `IsScriptInitializer` 双门控，Regular 编译路径（含既有 `?` 的 BC31003 行为）完全不变（§7.3 证明）。

---

## 10. F1 问题清单 A-F 吸收状态

> 注：2026-08-10 核心设计决策修正——触发判定由「错误码（诊断族）枚举」改为「`BoundKind` 绑定结果形态判定」；Option Strict 分叉定案为**无分叉**；`? x = 5` 定案为**比较（非开放问题）**；**裸变量打印纳入本版本**（原误标 follow-up，见 §8）。下表 A/F 行已同步，全文不再以错误码枚举为触发依据。

| 问题 | 内容 | 吸收状态 | 落点 |
|---|---|---|---|
| A（高） | §2.2 落点措辞与判定原则张力：触发必须锚定「绑定结果形态（BoundKind）」，不锚定「所有末尾表达式」，不枚举错误码 | **已吸收** | §0 判定原则、§3.1 触发条件、§3.3 排除不变量、§4 触发判定表 |
| B（高） | binder 线程「是否末尾语句」机制未定 | **已吸收（定案）** | §3.2 P-003：机制①语法节点同一性，拒绝机制② |
| C（trivial） | `? (` 分发表述过宽（`CanStartConsequenceExpression` 只认 `.`/`!`） | **已吸收** | §2.1(c) 与 §8：`? (` 走 `ParsePrintStatement`；文档表述已修正 |
| D（trivial） | C# 文件路径 `Binding\` → `Binder\` | **已吸收** | 本文所有 C# 引用均用 `Compilers\CSharp\Portable\Binder\Binder_Statements.cs`（P-002）；README/overview 中同段笔误建议随 spec 一并修 |
| E（低） | 无副作用测试矩阵单列 | **已吸收** | §7 独立成节，含 REPL/脚本/Regular 三块 |
| F（低） | 自动打印作用域与多行续行边界需倾向默认 | **已吸收** | §8：默认仅交互式 REPL、多行续行与单行一致、Option Strict 无分叉（已定案） |

> 2026-08-10 F5 修正：**裸变量打印纳入本版本**（原 F2 §8 误标 follow-up）。改动：① 解析层裸标识符（`before`/`Now`/`MySub`）解析为**裸表达式**（§2.1(c)，不包成 `X()`）；② 绑定层 `BindExpressionStatement` Case Else 增加**方法组消歧**（§3.1、§3.3）——`BoundMethodGroup` → 无参调用重分类为调用语句（`MySub` 不打印、`MyFunc` 现状打印），值引用（`Local`/`Field`/`Parameter`/`PropertyGroup`）→ `MakeRValue` 打印；③ §4 判定表新增 `Local`/`Field`/`Parameter`/`MethodGroup` 行；④ §7.1 新增裸变量用例 #2。可行性经源码核实：`BindRValue(方法组)` 会把 Void Sub 报 BC30491（`ERR_VoidValue`，`Binder_Expressions.vb:1257-1261`），故方法组必须拦截重分类而非走既有 `BindRValue`。
