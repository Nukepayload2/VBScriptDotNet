# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们做了一件主线 12 年前留了尾巴的事。2014 年 2 月的 roll-up 里有一条 "More implicit line continuations"（#51）一直标着 *Still under design*，从未被提成正式 proposal，也从未被否决。Anthony 的设计语料把它和另外两个新点子一起写成了 §2.7，于是它作为 ModVB 建议再次回到桌面。我们这次的目的不是给它盖章，而是先把那条 12 年前的尾巴认真讨论完。

## Agenda

* [Proposal: 新增隐式行继续（New Implicit Line Continuations）](#proposal-新增隐式行继续)

## Proposal: 新增隐式行继续

_Related: vblang 2014-02-17 roll-up [#51 – More implicit line continuations](../../vblang/meetings/2014/LDM-2014-02-17.md#51-more-implicit-line-continuations)；[#2 – Comments after implicit line-contuations](../../vblang/meetings/2014/LDM-2014-02-17.md)；vblang 2014-04-02 – 隐式行继续与插值字符串；`spec/lexical-grammar.md` – Implicit Line Continuation；`spec/statements.md` – BlockIfStatement；`spec/type-members.md` – FunctionSignature_

### 场景与缺口

We started from the same observation the mainline had in 2014, and it has not aged: VB 只在少数位置允许隐式行继续（运算符后、逗号后、`(` 后、`)` 前、赋值符后、查询运算符前后），其余地方必须行尾 `_`。而 `_` 有两条著名的坑：必须是行尾最后一个非空白字符，且 `_` 之后不能有注释。长条件、长签名、事件处理器三件套被 `_` 切得支离破碎：

```vb
' 今天：长签名 + Handles 必须用 _ 接续。
Private Sub Button1_Click(sender As Object, e As MouseMoveEventArgs) _
    Handles Button1.MouseMove
End Sub
```

主线 2014 年就精确列出过缺口的形状。我们逐字引述（[LDM-2014-02-17.md#51](../../vblang/meetings/2014/LDM-2014-02-17.md#51-more-implicit-line-continuations)）：

> Proposal: allow implicit line-continuations in these places:
> * Before "Then" in If … Then statements
> * Before "Implements" and "Handles" clauses
> * Others?

> We generally like the implicit LC before the Then token. The prettylister would put "Then" aligned with the "If" and "EndIf". But if you omit the Then token (an omission only allowed with multiline ifs), then what? ...? Needs more investigation.

We note that *Still under design* 一挂就是十二年：三人组（`Then` / `Implements` / `Handles`）从 2014 年起就没有下文。ModVB 建议把它重新端上桌，并追加了两个 #51 没有的位置：`)` 与 `As` 之间、查询子句之间夹注释。我们很高兴终于可以谈它。

### 今天的基线（我们核对过的现行规则）

我们回读了 `spec/lexical-grammar.md` 的 Implicit Line Continuation 一节。现行隐式继续的完整清单是：逗号后、`(` 后、`{` 后、`<%=` 后；成员限定符 `.` 后（且确有对象被限定）；`)` 前、`}` 前、`%>` 前；属性上下文 `<` 后与 `>` 前；非文件级属性上下文 `>` 后；**查询运算符前后**（`Where`、`Order`、`Select` 等）；表达式上下文二元运算符后；任意上下文赋值运算符后。规范还有两条对本次讨论生死攸关的原则：

> Implicit line continuations will only ever be inferred directly before or after the specified token. They will not be inferred before or after a line continuation.

> Line continuations will not be inferred in conditional compilation contexts.

以及 2014-04-02 对插值字符串的一锤定音：

> implicit line continuations happen before or after specific characters.

这给我们的判断定了基调：隐式继续从来是"特定字符锚定"的词法机制，不是"按缩进猜"。任何新位置都必须能落到"某个 token 前后"，否则就违反 VB 的非自由格式本体（spec 开头即写明 *Because the Visual Basic language is not free-format*）。

### 候选方案

**PROPOSAL A — 全量四锚点 + 查询注释。** 按建议原文：`Then` 前、`Handles` 前、`Implements` 前、`Function` 的 `)` 与 `As` 之间，并允许查询子句之间夹注释。

**PROPOSAL B — 主线三人组。** 只做 2014 #51 的 `Then` / `Handles` / `Implements` 三处，砍掉 `) As`，查询注释另行处理。实现面与语义风险最小，且与主线历史完全对齐。

**PROPOSAL C — 仅 `Then`。** 唯一被 2014 笔记明确表达过 "We generally like" 的位置。做最小验证，其余暂缓。

**PROPOSAL D — 不改语言，做编辑器"幽灵下划线"。** 编辑器自动插入/隐藏 `_`（建议原文的 Alternatives 3）。零文法风险。

### 权衡：Q&A

- **A vs B：`) As` 值不值得进 v1？** 我们把 `FunctionSignature` 的文法摊开来看：`'Function' Identifier TypeParameterList? ( OpenParenthesis ParameterList? CloseParenthesis )? ( 'As' Attributes? TypeName )?`。`As` 是可选的、且正好跟在 `)` 之后——这个位置在语法上"结构上必然未结束"，与 `)` 前、逗号后同族。但 `As` 是 VB 里最高频的 token 之一，锚定必须极端依赖上下文。真正让我们犹豫的是**作用域滑坡**：为什么 `Function F() ` 换行 `As Integer` 可以，而 `Dim x` 换行 `As Integer` 不可以？`Dim` 的 `As` 是逐变量绑定的（`Dim x, y As Integer` 里只有 `y` 是 Integer），一个居左的 `As` 会歧义地绑定整个变量表；而 `)` 之后的 `As` 绑定整个签名，没有这个歧义。**结论：v1 可以做，但必须锚定在")之后"，绝不扩展到 `Dim`。** 这个限定要写进 spec，否则滑坡是必然的。
- **B vs C：三人组一次做还是先只做 `Then`？** 2014 只对 `Then` 表达过正面偏好，`Implements`/`Handles` 只是"被点名"。但从词法机制看三者是完全同构的——都是"锚定关键字前的行终止符按空白处理"。分开做反而要维护两套规则。`Handles` 在 WinForms 代码里几乎遍地都是（事件处理器三件套），`Implements` 在生成代码里常见，两者价值不输 `Then`。**结论：三人组一次做，语法面统一。**
- **查询子句夹注释：这需要语言改动吗？** We checked the spec and were surprised. 现行规则已经允许**查询运算符前后**隐式继续（"before and after query operators (`Where`, `Order`, `Select`, etc.)"），而注释在词法上透明。2014 #2 的示例恰恰就是一行注释夹在两个查询子句之间：

  > Comments are allowed after implicit line-continuation characters.

  ```vb
  Dim addrs = From i In invites     ' go through list
              Let addr = Lookup(i)  ' look it up
              Select i,addr
  ```

  也就是说，`From customer In db.Customers` 换行 `' in Illinois` 换行 `Where ...`——注释行被词法器跳过，行终止符落在 `Where` 前，本就是合法的隐式继续。**这个子建议在当前语言里已经成立，不需要任何文法改动。** 真正缺的是文档与格式化器对"注释行对齐"的支持。另外我们 `Suspect` 建议原文的查询示例作为语句不能编译——裸 `From ... Take 10` 是一个值表达式，VB 不允许值表达式作语句，需要一个接收者（`Dim q = From ...`）或 `For Each`。这与 2014 #2 的写法（`Dim addrs = From i ...`）不一致。
- **D vs 文法改动：幽灵下划线够吗？** 它有真实价值（在 IDE 里不再看到 `_`，零风险），但只作用于屏幕：文件里的 `_` 原样存在，命令行编译、评审工具、其它编辑器全都看不到"幽灵"。`_` 的坑（行尾不能有注释、容易忘写）在文件层面依然存在。We think it is a reasonable stopgap, not a substitute——而且它与本建议不互斥，可以后置。
- **`End If` 收尾的笔误。** 建议原文的 `Handles` 示例以 `End If` 结尾，应作 `End Sub`。我们查了 Anthony 原文 §2.7，这个笔误是逐字继承的——原文就这么写的。**结论：必须修正，且作为品质红旗记录在案。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

Keyword-anchored continuation 是"下一个非注释 token 恰好是锚定关键字"才触发——这是把双刃剑。好的一面：几乎不可能吞行，因为吞行需要撞上关键字本身。坏的一面：**锚定必须上下文敏感**。`Then` 只在 If/ElseIf 语境是锚；`Handles`/`Implements` 只在成员声明签名之后是锚；`As` 只在成员签名 `)` 之后是锚。在非锚定语境（如 `Dim x = F()` 换行 `As Integer`），`As` 不是锚，维持现状报错——这正是我们要守住的作用域边界。

`Then` 有一个 2014 就搁置的问题，我们这次把它结算了。现行文法（`spec/statements.md`）：

```antlr
BlockIfStatement
    : 'If' BooleanExpression 'Then'? StatementTerminator
      Block?
      ElseIfStatement*
      ElseStatement?
      'End' 'If' StatementTerminator
    ;
```

`'Then'?`——**块 If 的 Then 本就可省略**。所以今天两种写法并存：`If x <换行> body <换行> End If`（省略 Then）合法，而 `If x <换行> Then`（Then 独占一行）是错误。我们选择的机制是词汇级：If/ElseIf 语境里，锚定关键字 `Then` **之前的行终止符按空白处理，合并逻辑行**。这样：

```vb
If x
Then Return 15          ' 合并为逻辑行 "If x Then Return 15" —— 单行 If。
```

```vb
If x
Then
    Return 15           ' 合并为逻辑行 "If x Then"，随后是块体 —— 块 If。
End If
```

两例都精确对应 2014 #51 的原始示例。省略 Then 的写法（`If x` 换行 body）不含 `Then` token，不触发合并，行为不变。**两者共存，无新歧义。** 2014 的 "then what?" 就此有了答案。

#### 2. 角案例与边界语义

- **`ElseIf x` 换行 `Then`**：同一锚（ElseIf 的 `Then` 也是 `'Then'?` 可选），一并纳入。
- **条件编译指令不能横跨锚定**：规范明确 *Line continuations will not be inferred in conditional compilation contexts*；2014 也确认 *Neither Roslyn nor Native allow implicit line continuations in preprocessor directives. (although they sadly allow explicit line continuations)*。所以 `If x` 换行 `#If DEBUG` 换行 `Then` 不成立——`#If` 行会终结逻辑行。这个限制要写进 spec 的"不允许"清单。`Probably`：这不是缺陷，而正是防"未编译分支被意外拾取"的安全阀。
- **多 `Handles`**：`Handles Button1.Click, Button2.Click`——逗号后本就可继续，锚只负责第一个 `Handles`。
- **无参成员**：`Function F()` 换行 `As Integer` 有 `)` 锚，成立。`Function F`（无括号）换行 `As Integer` 无 `)` 锚——v1 是否覆盖，见 OPEN QUESTIONS。
- **`MustOverride` / 接口成员**：无 `HandlesOrImplements` 子句，锚无意义，自然不触发。
- **`:` 同行冒号**：`If x` 换行 `Then : y = 1` 合并后如何解释（块 If 头 + 同行冒号体，还是别的），`Suspect` 需要专门定案，写进 speclet。
- **类级 `Inherits`/`Implements`**：类声明里 `Class Foo` 换行 `Inherits Bar` 换行 `Implements IBaz` **今天就是合法的**——类级子句自带行继续。缺口只在成员级 `Implements`/`Handles`，不要误伤类级。
- **空参数表**：`Function F()` 换行 `As Integer` 与空表共存，无冲突。

#### 3. 作用域与绑定

纯词法特性，语义模型零变化：逻辑行合并发生在语法树生成前，`If` 语句的 `GetLineSpan` 跨物理行（与 `_` 继续的现状同构），trivia 保留换行。绑定、符号、重载解析一概不动。

#### 4. 与既有特性的交互

- **`_` 显式继续并存**：锚定位置从此有两种写法（带 `_` / 不带）。这是原则 #3 的张力所在，见 VB 基因对照。我们不打算废除 `_`——非锚定位置仍需要它。
- **运算符继续**：建议的 `Then` 示例里条件部分（`AndAlso` / `OrElse` 行尾）**今天就已合法**；新增的只是 `Then` 本身。示例的复杂度没有高估——它演示的正是"条件早已可断行、只有 `Then` 卡壳"的真实痛点。
- **单行 If**（`If x Then y`）：无换行，不受影响。
- **插值字符串 / XML 字面量**：各自的继续规则不变；洞里的 `Then` 是普通标识符，不构成锚。
- **`AndAlso`/`OrElse`/IsTrue/IsFalse**：本次只动词法，这些语义根基不受扰动——与 TypeOf 流分析那种触碰绑定解析的特性形成鲜明对比。

#### 5. Breaking change 与兼容性

这是本特性最漂亮的一项：**四个锚定位置全部是"错误 → 合法"，没有任何既存合法代码改变含义。**

- `If x` 换行 `Then ...` —— 今天报错，未来合法。
- `Sub Foo()` 换行 `Handles X` —— 今天报错，未来合法。
- `Function Foo()` 换行 `Implements X` —— 今天报错，未来合法。
- `Function Foo()` 换行 `As Integer` —— 今天报错，未来合法。

与 TypeOf 流分析（Shadowing 会改写既有绑定）不同，这里不存在"同一源码重编译后调用不同方法"的可能。唯一的代价是**错误检测变宽松**：锚定位置上忘写 `_` 不再报错。我们认为这是特性本身（不再需要 `_`），且被锚定集合严格界定——非锚定位置忘写 `_` 依旧报错。尽管如此，我们建议保守地加 `langversion` 门控与警告策略，给任何未预料到的边界留退路。

#### 6. Option Strict / 编译选项分叉

无分叉。词法规则与 Option Strict/Explicit/Infer/Compare 完全正交，严格与宽松两条路径行为一致。

#### 7. IDE / IntelliSense 影响

- **格式化器**：2014 就预言过 prettylister 会把 `Then` 对齐 `If`/`End If`。四个锚点都需要格式化规则（`Handles`/`Implements` 与成员关键字对齐，`As Car` 与 `Function` 对齐）。
- **回车智能断行**：VB 编辑器今天在某些位置回车会自动补 `_`；锚定位置启用后，编辑器应改为"直接断行、不补 `_`"。这是会改变老用户肌肉记忆的行为，必须配选项。
- **`_` 清理工具**：一个 analyzer + code fix，"此处 `_` 已不必要，删除"。这是把样板真正清掉的手段。
- **补全/签名帮助**：跨锚定断行基于 trivia，本就能工作，需在原型中验证。

#### 8. 数据 / 普遍性

`Suspect`：建议原文没有任何量化数据。我们的定性判断：`Handles` 三件套在 WinForms/ASP.NET 事件代码里近乎必然出现，长 `If` 条件在校验代码里常见，`Implements` 在生成代码里常见，`) As` 在长工厂函数签名里常见。2014 年三条位置被点名本身就是"社区痛点"的证据。但"数十万安静客户"里到底多少被 `_` 真正绊倒过，没有数字。`TODO`：为数据/普遍性补一次真实代码库的 `_` 使用率采样。

#### 9. 更简替代

- **什么都不做**：保留现状。但 2014 年就标了 *Still under design*，12 年未决本身说明这不是"没价值"而是"没排期"。
- **幽灵下划线**：见 Q&A D——停火方案，不解决文件层面。
- **全自由格式**（C# 式空白无关）：直接否决。VB 的非自由格式是它的身份（spec 开篇即声明），改成 C# 式是对原则 #2 的根本背叛，且破坏面无限大。2014-04-02 的插值字符串问答也说明主线的立场一直是"继续在特定字符前后做文章"。
- **缩进敏感的继续**（Python 式）：否决，对 VB 用户陌生且易错。
- **仅 analyzer 提示**：只能提示"这里可以不加 `_`"，不能消除它——是补品不是替代品。

#### 10. 成本 / 优先级

词法层：小。给三个锚定关键字加"行终止符可作空白"的规则，逻辑行合并复用 `_` 的既有管道。真正成本在格式化器与工具链。对 VBScript.NET：这是 DX 抛光，不是头条特性，优先级中低；脚本式代码长 `If` 链与事件胶水不少，`Then`/`Handles` 两锚的性价比最高。"值得做但太难"不适用——它不难，难的是决定要不要忍受"两种写法"。

#### 11. 运行时 / CLR 硬约束

无。纯编译期词法，不触达 IL、PEVerify、表达式树、存储规则。

#### 12. 值不值得做

价值（移除高频 `_` 样板、零破坏、主线先例强）中；成本（文法小 + IDE 中）中低；风险（吞行、滑坡）低且有锚定纪律对冲。**值得做——但必须缩小。** 全量 A 会把 `) As` 的滑坡与查询注释的 no-op 一起背在身上；按 B 的范围落地，才配得上"极 VB"。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心。`_` 正是那条"结构上必然未结束却被迫显式接续"的样板，且自带两个坑（行尾约束、禁注释）。
- **保持 VB-like（原则 #2）**：行式语法是 VB 的本体，扩展"特定字符锚定"清单是**在本体里加内容**，不是引入外来形态。C# 没有行继续概念，本特性无 C# 可对齐——按原则 #4 的"除非有充分理由"，VB 的"理由"就是它的身份本身。
- **不引入"第二种做事方式"（原则 #3）**：这是唯一真正的扣分项。锚定位置从此有 `_` 与隐式两种写法。但我们认为这是**缩小**而非扩大：语言本来就并存"隐式（运算符后等）"与"显式 `_`"两套，本特性只是把一部分位置从显式归入隐式，让两套的边界更靠近"结构未结束处"。样式的引导语是"锚定位置优先隐式"。
- **避免隐蔽语义变化（原则 #7）**：零语义变化，纯词法，错误→合法。与 `Return?`、与 TypeOf 流分析的绑定重解析形成强烈对照——这是我们敢推它的底气。
- **永不破坏（原则 #1）**：见追问 #5，无破坏。
- **读起来像英语（原则 #5）**：`If <长条件>` 换行 `Then`，`Then` 与 `If`/`End If` 对齐，是自然的英语阅读节奏。
- **不为边缘场景加特性（原则 #6）**：`) As` 比三人组更靠边缘，这也是我们把它单独拎出来的原因。

**与主线关系（对照表 2.3）**：三人组 = **主线一致**——直接复活 2014 #51（主线留了 12 年的 *Still under design*，ModVB 把它做完）；`) As` = **Anthony 独立延伸**（#51 的 "Others?" 之后的发明）；查询注释 = **主线已支持**（spec "before and after query operators" + #2 已批准），ModVB 把它当新特性提出是一处失察。这与 2.3 表里"主线保守、Anthony 激进"的总张力一致——但这次 Anthony 激进的方向恰好与主线未竟事项重合。

### RESOLUTION:

1. **采纳"关键字锚定隐式行继续"原则**：在 If/ElseIf 的 `Then` 前、成员声明的 `Handles`/`Implements` 前，行终止符按空白处理、合并逻辑行——即 2014 #51 的三人组。机制与 `_` 同构（"happen before or after specific characters"），非破坏，极 VB。
2. **`) As` 限定采纳**：仅锚定"成员签名 `)` 之后的 `As`"（Function/Property/Event），**不扩展到 `Dim`**。扩展与否看 speclet 论证；滑坡即否决。
3. **查询子句夹注释：不需要语言改动**。现行规范已允许查询运算符前后继续、注释透明、2014 #2 已批准——关闭为 no-op，剩余工作是文档与格式化器对齐。
4. **修正 §2.7 继承的笔误**：`Handles` 示例 `End If` → `End Sub`。
5. **示例必须可编译**：查询示例需补接收者（如 `Dim q = ...`），不允许演示"值表达式当语句"。
6. **条件编译指令不得横跨锚定**：继承现行规则（不在 CC 语境推断隐式继续），写入 spec 的"不允许"清单。
7. **配套 IDE/工具链**：四锚格式化规则；回车智能断行（锚定位置不补 `_`）；"删除不必要 `_`" analyzer + code fix。
8. **保守门控**：`langversion` 门控 + 警告策略，给边界留退路。

### Implication:

- 起草 speclet：词法文法修改（三个锚定 token 的 `LineTerminator?` 规则，复用 `OpenParenthesis`/`CloseParenthesis` 的先例）；`BlockIfStatement`/`FunctionSignature` 文法本身不动，全部在词法层合并逻辑行；明确"允许/不允许"两个清单。
- 最小原型：三个锚 + `) As`（限定）+ 逻辑行合并语义 + 省略 Then 共存验证；检查 `Then : body` 同行冒号与 `#If` 横跨两种边界。
- 与格式化团队对齐：`Then` 对齐 `If`/`End If`（2014 已预言），`Handles`/`Implements`/`As` 的对齐约定。
- 补数据：真实代码库 `_` 使用率采样，支撑普遍性论证。
- 未决问题移交 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`) As` 是否覆盖无括号签名（`Function F` 换行 `As Integer`，无 `)` 锚）——v1 排除还是纳入？
- `OPEN QUESTIONS`：`Then` 合并后的 `:` 同行冒号形式（`Then : y = 1`）的精确解释。
- `OPEN QUESTIONS`：是否延伸到 `Case ... When`（2014 roll-up 里有社区提议让 `When` 可被隐式行继续接续、以对齐到 `Case` 下方，原文拼写为 "to the preceded"），本场未决，倾向后置。
- `OPEN QUESTIONS`：Property/Event 是否随 Function 一起进 `) As`。
- `TODO`：量化 `_` 在四锚位置的占比。
- `TODO`：最小原型。
- `Follow-up`：主线 resurrect——若 VBScript.NET 采纳，考虑把三人组写回 vblang 主线 proposal。

### 状态

- **LDM 状态：Consider**——原则采纳、范围收敛后才能转 Active。
- **三态判定：Consider**——按 RESOLUTION 缩小为三人组 + 严格 `) As` 则为 Active；`) As` 作用域定案前不启动；查询注释关闭为 no-op。

---

## 附录：特性评价

# 建议评价报告：proposal-implicit-line-continuations.md

## 评价对象

- 建议：proposal-implicit-line-continuations.md — 在更多位置引入隐式行继续（`Then` / `Handles` / `Implements` 前、`Function` `) As` 之间、查询子句夹注释），减少 `_` 样板。
- 来源：Anthony 原文 §2.7 "New Implicit Line Continuations"（`..\AnthonyDesign_wordpress.txt` L566–651；四个子建议与全部示例逐字来自该节，含 `End If` 笔误）。主线先例：vblang 2014-02-17 #51（三人组）、#2（查询注释）。
- 配方目标：把"结构上必然未结束"的位置纳入隐式继续，改善长条件/长签名/事件与接口成员声明的排版体验。

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 明确（去 `_` 样板）、示例可操作；但无原型（封顶 3）；查询注释子效果经核查是**既成事实**——语言层面无改进可显现（消退）；`) As` 范围未定（主效果的一部分悬浮） | 已检查 | 无数据支撑普遍性；无原型/运行证据；两处子效果（查询注释、`Handles` 笔误修正）需要先行澄清 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"（实为无外来味，接近满格）。延续主线 #51 与 spec "before/after query operators" 先例；行式语法即 VB 本体；消除样板（原则 #9）；无外来语法 | 已检查 | 原则 #3"第二种做事方式"张力未自行识别；`) As` 的 `Dim` 滑坡未分析 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例忠实原文；但缺**文法/spec 改动章节**（词法特性的核心章节缺位）；缺兼容性分析；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；`End If`/`End Sub` 笔误（源自原文）；查询示例作为语句不成立 | 已检查 | 未引用 #51 主线先例；未识别查询注释已支持；"各新增位置的精确词法规则"被留进 Unresolved（本应设计定案而非悬置） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/水/光正向（提速排版、强化 VB 行式身份、盘活既有代码）；风/暗风险：两种写法张力 + `Dim` 滑坡 + IDE 格式化成本，文档未权衡 | 已检查（预测待定） | 格式化器/回车行为/`_` 清理工具链的影响未提；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。三人组 = 继承主线 #51 **未标注**；查询注释 = 继承 VB 既有规则**未识别**（反当新特性）；`) As` = Anthony 原创延伸未标注；示例逐字继承 §2.7（含笔误） | 已检查 | 材料真实、无外来杂质（无闭源、无照搬 C#——C# 根本无此概念）；但来源标签基本缺失 |

## 设计原则对照

- **与 VB 基因：一致**（主要）——行式语法是 VB 本体（#2）、消除样板（#9）、零破坏（#1）、无隐蔽语义变化（#7）、读起来像英语（#5）；轻微张力于原则 #3（锚定位置两种写法），靠"优先隐式"的样式引导对冲。
- **与主线关系：主线一致**——三人组直接复活主线 2014 #51（*Still under design* 12 年）；`) As` 为 Anthony 独立延伸；查询注释为主线已支持（no-op）。与 2.3 表"主线保守、Anthony 激进"的总张力一致，但方向恰好重合主线未竟事项。
- **破坏性变更：无**——四锚位置全部是"错误 → 合法"，无既存合法代码改义。但文档未显式分析，且未给 `langversion` 门控/警告策略建议（我们已在 RESOLUTION 补上）。

## 总评

- **达成程度：部分达成**——概念与价值成立、非破坏、主线先例强；但范围未收敛、证据止于书面、查询注释为 no-op、含一处继承的笔误。
- **LDM 三态建议：Consider**——按 RESOLUTION 缩小为三人组 + 严格 `) As` 则 Active；否则维持 Consider。
- **主要问题**：① 未识别主线 #51 先例与查询注释已支持（方向性失察）；② 缺文法/spec 改动章节与兼容性分析；③ `) As` 的作用域滑坡（`Dim`）未分析；④ 状态行占位链接、`End If` 笔误、查询示例不成立等品质红旗；⑤ 无普遍性数据。

## 返工建议

- **补充章节**：文法/spec 改动（词法层锚定规则、逻辑行合并、允许/不允许清单）；Compatibility/breaking-change（"错误→合法"逐条列证 + `langversion` 门控与警告策略）。
- **补充证据**：最小原型（三锚 + 限定 `) As` + 省略 Then 共存）；真实代码库 `_` 使用率采样；格式化器/回车行为影响评估。
- **未决问题处理**：`) As` 明确"成员签名 `)` 之后"限定、排除 `Dim`（写明理由）；查询注释关闭为 no-op + 文档/格式化器工作项；`Handles` 示例改 `End Sub`；查询示例补接收者（`Dim q = From ...`）。
- **设计探索**：`Then : body` 同行冒号解释；`Case ... When` 是否纳入（2014 社区提议）；Property/Event 是否随 Function 进 `) As`；主线 resurrect 的可行性（把三人组写回 vblang）。

---

## 附录：C# 生态与互操作考量

> 定位声明：**隐式行继续是纯词法/语法层特性**，与 C# interop 主线（Span/ref、unsafe、AOT/trimming、source-gen、COM）关系弱。本附录据实简短说明，不硬凑对应物。依据：`..\..\csharplang-index.md`（dotnet/csharplang interop 索引）＋ csharplang 镜像 Grep 核实。

### 相关 C# 现实方向

- **C# 没有"行继续"概念，也不需要它。** C# 是自由格式语言：换行对词法器基本等同空白；语句以 `;` 终结、块以 `{}` 界定、表达式是否延续由括号/方括号是否闭合决定。因此在 csharplang 全仓库中**不存在**隐式行继续或续行符的讨论——对本仓库 Grep `line continuation` 无真命中（误报见引用纪律备注）。
- C# 标准把换行归入词法结构，章节为 §6.3.2 **Line terminators**、§6.3.4 **White space**（→ `spec\lexical-structure.md`，链接索引；正文已迁 dotnet/csharpstandard）。除终止 `//` 行注释与预处理指令外，换行不参与语句边界判定。
- C# 生态对"长语句排版"的回应不是行继续，而是**结构闭合 + 格式化器重排**：括号未闭合即逻辑未结束，Roslyn formatter 可任意折行。这与本提案"在结构上必然未结束处免 `_`"的直觉同源，但 C# 无 `_` 可免，故无对应物可比对。

### 现实 vs 提案

| 维度 | 判定 | 理由 |
|------|------|------|
| 与 C# interop 主线（索引 T1–T8 / M1–M8） | **脱节（本就不相关）** | 纯编译期词法特性，不触 IL、元数据、ref 安全模型、AOT/trimming、source-gen、COM 任一表面；索引各主题均无对应项。 |
| 与 C# 自由格式语法 | **兼容（哲学分歧，非互操作摩擦）** | C# 换行即空白；VB 非自由格式（本 meeting 已引 spec 开篇 *Because the Visual Basic language is not free-format*）。提案在"本体里加内容"并明确否决全自由格式——是 VB 身份选择，不影响与 C# 生态互操作。 |
| 与 C# 元数据/二进制互操作 | **兼容（零接触）** | 逻辑行合并发生在语法树生成前；产物 IL/元数据与 `_` 显式继续完全同构；C#+VB 混编产物无任何差异。 |

### 对 VBScript.NET 的适应建议

- **无需桥接**。与 C# 生态唯一的交点是实现面：新锚点写进 .vbx 词法器，复用 `_` 的逻辑行合并管道，无元数据/运行时适配。
- **工具链透明**：Roslyn 源生成器/analyzer 消费语法树而非源码文本，行终止符作为 trivia 保留，四锚对生成器与 IDE 补全透明。
- **混编零负担**：.vbx 脚本与 C# 同程序集编译时，换行规则各自独立，不产生符号表或编译顺序差异。
- **与 AOT/trimming 完全正交**：无动态/反射面，是少数不拖累现代 .NET（NativeAOT）的 VB 特色之一，可作为"VB 特色与 C# 生态共存"的正面例证。

### 对既有 RESOLUTION / 三态判定的影响

无。RESOLUTION 1–8 全为词法/工具链决策，不涉 C# 互操作面；三态判定（Consider，收敛后转 Active）不受影响。本附录仅作"与 C# 生态关系弱"的如实记录，呼应正文「VB 基因对照」中"本特性无 C# 可对齐"的判断。

### 引用纪律备注

- **未逐字引用任何 C# 提案原文**——本主题在 csharplang 仓库无对应正文（已 Grep 核实）；索引第四节 6 段已验证引文（native-integers / function-pointers / blittable / unsafe-evolution / span-safety / ref-struct-interfaces）均与换行无关，强行引用即硬凑，故不引。
- 仅引用 csharplang 链接索引的章节标题 **Line terminators** / **White space**（→ `spec\lexical-structure.md`）。
- **OPEN QUESTIONS**：csharplang 的 spec 目录仅为链接索引，正文已迁 dotnet/csharpstandard，本镜像未含正文——§6.3.2 / §6.3.4 的具体条文原文无法在本镜像核实，如需逐字引用请到 dotnet/csharpstandard 复核。
- 两处 Grep 误报已核实：`proposals\csharp-11.0\raw-string-literal.md` 与 `meetings\2014\LDM-2014-05-21.md` 中的 "backslash" 均为字符串转义语境，与行继续无关。
