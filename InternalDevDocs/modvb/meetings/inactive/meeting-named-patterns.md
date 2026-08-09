# Visual Basic Language Design Meeting
August 8, 2026

## Agenda
* [ModVB Proposal — Named Patterns / 命名模式（解构语法）](#modvb-proposal--named-patterns--命名模式解构语法)

## ModVB Proposal — Named Patterns / 命名模式（解构语法）

We are continuing the pattern-matching thread of the ModVB sandbox series. Two meetings ago we settled the `Select Case TypeOf` 的流分析边界；上一场我们评审了 `ShapeOf` 模式匹配并得出"**我们要这个能力，不要这个操作符**"的结论。今天这份建议把"模式即函数"的想法推到一般化：任何一个带 `Out` 参数、返回 `Boolean` 的函数都可以作为模式，并以**构造函数调用式的解构语法**递归匹配对象结构。

它击中了一个我们久违的念头。2018.12.19 我们写过 "We all want to do pattern matching, it's the thing we are most excited about after C# interop issues"，而 2014-02-17 我们自己也追问过 "It would be nice to dispatch on `Integer.TryParse`"——把任意函数当模式正是那条线索的现代形态。所以我们是带着好感进场的。

但这份建议把太多未定机制堆在一个语法里：裸标识符绑定、常量输入、工厂自动反转、`Imports` 引入函数、`Return True, kind:=...` 多值返回。我们逐条拆开，发现核心示例依赖一个**从未被定义**的机制，并且与建议自己声明的"Out + Boolean 契约"相矛盾。这一场我们走完了十二问清单，结论是有条件的：概念保留，载体语法不采纳。

_诚实分层：正文区分 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。引用的主线会议引文与 issue 编号均与 `..\..\vblang` 原文逐字一致；标注为 `Suspect` 的判断是基于设计原则的独立推理，非主线原文。_

---

### 场景与缺口

今天的语法树形状分析是"过程式的深度遍历"。判断一个语句是不是 `Me.New(...)` 构造器调用，要写一长串 `TypeOf` + `DirectCast` + 成员访问：

```vb
' 现状（Motivation 的场景）：逐层下钻，每层一次 TypeOf + DirectCast。
Function IsMeNewCall(node As ExecutableStatementSyntax) As Boolean
    If TypeOf node Is ExpressionStatementSyntax Then
        Dim stmt = DirectCast(node, ExpressionStatementSyntax)
        If TypeOf stmt.Expression Is InvocationExpressionSyntax Then
            Dim inv = DirectCast(stmt.Expression, InvocationExpressionSyntax)
            If TypeOf inv.Expression Is MemberAccessExpressionSyntax Then
                Dim ma = DirectCast(inv.Expression, MemberAccessExpressionSyntax)
                If TypeOf ma.Expression Is MeExpressionSyntax Then
                    Return TypeOf ma.Name Is IdentifierNameSyntax AndAlso
                           DirectCast(ma.Name, IdentifierNameSyntax).Identifier.ValueText = "New"
                End If
            End If
        End If
    End If
    Return False
End Function
```

缺口是真实的，而且 2017.10.18 我们确立的原则正好对上：模式的价值出现在 "the ceremony of _inspecting_ an object(-graph) obscures the structure of the data"。语法树是嵌套最深、最"形状化"的 CLR 对象图，`ExpressionStatement(InvocationExpression(MemberAccess(MeExpression(), IdentifierName("New"))))` 这种声明式描述确实比上面的梯子好读一个数量级。

但我们要诚实：**这个场景的受众是编译器与工具链作者，不是"数十万安静客户"**。2018.05.30 欧洲之行我们确认 "there are hundreds of thousands of quiet customers each month [who] primarily want VB to keep doing what it does now"；语法树分析不在这群人的日常里。对 VBScript.NET 自身倒是很有价值——我们的编译器就是拿 VB 写、天天在语法树上行走的。这是本建议最强的加分项，也是它最窄的立足点。

---

### 候选方案

我们枚举了四个形态。

**PROPOSAL A — 建议原文：工厂镜像解构 + 用户定义命名模式（Anthony 第 7 章）**

```vb
' 模式函数：带 Out 参数、返回 Boolean。
Function ConstructorCall(
           node As ExecutableStatementSyntax,
           Out kind As VBSyntaxKind,
           Out arguments As SeparatedSyntaxList(Of ArgumentSyntax)
         )
         As Boolean

    ' Bring shared pattern functions into scope.
    Imports SyntaxPatterns

    ' Deconstruction syntax matches/inverts factory syntax.
    Select Case ShapeOf node
      Case ExpressionStatement(
             InvocationExpression(
               MemberAccess(
                 MeExpression(),
                 IdentifierName("New")
               ),
               argList
             )
           )

        Return True, kind:=VBSyntaxKind.MeExpression,
                     arguments:=argList.Arguments

      Case ExpressionStatement(
             InvocationExpression(
               MemberAccess(
                 MyBaseExpression(),
                 IdentifierName("New")
               ),
               argList
             )
           )

        Return True, kind:=VBSyntaxKind.MyBaseExpression,
                     arguments:=argList.Arguments

      Case Else

        Return False, kind:=Nothing, arguments:=Nothing

    End Select
End Function
```

以及 `If` 形态：

```vb
If ShapeOf firstStatement Is ConstructorCall(kind, args) Then
    If kind = VBSyntaxKind.MeExpression Then
        ProcessChainedConstructorCall(args)
    ElseIf kind = VBSyntaxKind.MyBaseExpression Then
        ProcessBaseConstructorCall(args)
    End If
Else
    ProcessOtherStatement(firstStatement)
End If
```

**PROPOSAL B — 只保留"函数即模式"契约，不用工厂镜像：模式函数签名统一为 `(subject, Out ...) As Boolean`，调用点显式绑定**

```vb
' 不依赖 ShapeOf 操作符；与用户定义模式方法建议同一条语法。
If firstStatement Matches ConstructorCall(kind, args) Then
    ' ...
End If
```

工厂镜像（`Case ExpressionStatement(InvocationExpression(...))`）降级为后续的"模式函数语法糖"，只有当"自动反转工厂"被真正定义后才谈得上。

**PROPOSAL C — 给"可作为模式"加显式契约（建议原文的 Alternatives）**

用 `Pattern` 关键字或特性标记模式函数，让编译器严格校验签名（只读输入、成功时必写输出），而不是"任何 Out + Boolean 函数都可以"。以可声明的契约换掉魔法。

**PROPOSAL D — 不做，沿用过程式 `TypeOf` 梯子 + 手动提取**

现状的确定性与可调试性，付出样板与可读性。

---

### 权衡：LDM 追问清单

We worked through the twelve-question checklist. The decisive questions were 1, 2, 3, 4, 5, 8 and 9.

#### 1. 语法 / 文法歧义

**`Case ExpressionStatement(...)` 今天已经是合法代码。** `Case` 子句接受表达式；`ExpressionStatement(...)` 是一次方法调用，作为 `Case` 子句时今天的意思是"与函数返回值的等值比较"：

```vb
' 今天完全合法：Case 子句把函数调用当等值比较。
Function GetPlaceholderStatement() As ExpressionStatementSyntax
    Return Nothing
End Function

Select Case node
    Case GetPlaceholderStatement()   ' 今天：node 与函数返回值的比较。
End Select
```

把同一语法重解释为"模式"是对**既有合法代码的语义改写**——这正是我们 2018.05.30 对 `Return?` 说的 "Control flow would be altered by a very subtle character" 的那类隐蔽语义变化。而且它比 `Case pn As Type` 更糟：上一场我们接受 `Case ... As Type`，理由是 `As` 不是表达式操作符、旧形态不可能合法；但 `Case 函数调用()` 是**可能合法**的。这意味着解析器必须"看函数是不是模式"，把决定权交给绑定阶段——2014-02-17 我们明确不喜欢这个方向：MatchFunction 绑定 "It suggests putting overload resolution into the late-binder, which we don't like."

`If ShapeOf x Is ConstructorCall(kind, args)` 则有第二层歧义：`Is` 右侧今天接引用相等操作数，2018.12.19 我们已表态 "We think `Is` will have ambiguity issues with the existing use for reference equality"。把模式塞进 `Is` 右侧正是那个悬而未决的雷。且 `ConstructorCall(kind, args)` 与 C# 式"位置模式 `类型名(实参)`"在字面上无法区分——右侧是方法还是类型，绑定前无解。

`Probably`：工厂镜像只能在"有显式标记的语境"里消歧（`Select Case ShapeOf` 或 `Matches` 右侧），而在那样的语境里 `Case ... As Type` 与 `Matches` 已经够用，工厂镜像带来的语法负担买不到新能力。

#### 2. 角案例与边界语义

**同一份建议里混着两种"模式契约"。** 这是本场最重要的发现。`ConstructorCall(node, Out kind, Out arguments) As Boolean` 是"主题显式 + Out 输出"（Model 1，与用户定义模式方法建议一致）；而 `Case ExpressionStatement(InvocationExpression(...))` 里的 `ExpressionStatement` 是"主题来自外层 `Select Case ShapeOf node`，实参即子模式"（Model 2）。Model 2 的 `ExpressionStatement` 若按"Out + Boolean"契约声明，它没有 Out 参数、返回的是节点不是 Boolean——**工厂不是模式函数，按建议自己的定义根本无法作为模式**。要么编译器对 `SyntaxFactory` 做特判自动反转（未定义），要么 SyntaxPatterns 里存在一批签名从未给出的高阶模式函数。无论哪条，示例都不能按建议的机制编译。

**常量输入与命名模式输入冲突。** `IdentifierName("New")` 需要把字符串 `"New"` 作为**输入**传给模式；但"命名模式的实参列表目前只输出"（`proposal-named-pattern-inputs.md` 原文："currently the 'argument' list for a named pattern is output only. This should be investigated."）——示例悄悄依赖了一个被兄弟建议标为"未调查"的能力。`"New"` 作为约束相等，VB 不区分大小写，是否按 `ValueText` 做大小写不敏感比较，未定。

**裸标识符重复出现的绑定规则。** `Case MemberAccess(a, b)` 与 `Case MemberAccess(b, b)`——`b` 重复出现时是"约束相等"还是"新建变量遮蔽"？建议原文明确说"原文未说明"。C# 位置模式是重复命名报错；F# 部分场景是相等约束。两条路都得选，且会改变错误信息与语义模型。`Suspect`：建议示例里 `argList` 只出现一次，没有给任何证据。

**无 discard。** 想匹配"任意名字的成员访问"，工厂镜像要求为每个子位提供一个值，而 2018.12.19 说过 "Probably add a discard identifier and a discard pattern"——本建议完全没有。

```vb
Case MemberAccess(expr, ???)   ' 没有 discard：不关心的子位也必须给值或绑变量。
```

**无 `When` 护栏。** 2018.12.19 "We like `When`"——主线把 `When` 当成模式的组成部分。工厂镜像的 Case 没有 When 的容身之处；建议把护栏写进了模式函数体内（`Return False, ...`），等于每个护栏都要造一个新模式函数。这是仪式，不是消除仪式。

**组合缺失。** 建议未决问题 #3 问"模式能否 Or/And/取反"。2018.12.19 的答案是 "Conjunctions are probably not in first or second version, C# thinking is these are much lower need/usage." 本建议不应先于主线打开这个门。

**`Nothing` 主语与递归深度。** `Select Case ShapeOf Nothing` 时所有模式都应不命中（`Probably`，需显式规范）；递归嵌套深度是否有限制，未定。而这份建议的全部价值都在**深层嵌套**——`ExpressionStatement(InvocationExpression(MemberAccess(...)))` 四层起步——可 2018.12.19 对嵌套的原始态度是 "What does 'nested patterns' mean? Maybe not V1"。**建议把主线明确推迟的东西当成了唯一卖点。**

#### 3. 作用域与绑定

`argList` 的作用域 = 所在 Case 子句块。2014-02-17 Design1 先例："The scope of the variable declaration is just that case clause, and follows the same principles as other blocks which define variables like ForEach." 建议示例在分支体内用 `argList`，与 Design1 一致。`If` 形态的 `kind`/`args` 作用域 = 外围块，2018.12.19 文法注释 "The scope of introduced variables is the surrounding scope"。

**Definite assignment 是硬伤。** `If ShapeOf x Is ConstructorCall(kind, args)` 命中则 `kind`/`args` 已赋值；失败路径上它们**未赋值**。建议的 `Else` 分支没用它们，但谁写 `Else` 里 `Console.WriteLine(kind)` 就该报错。2018.12.19 对 Do 循环底部的变量引入说过 "We don't usually let variables be used before they are declared, so it would look quite weird to allow this in one case"——同样的原则必须落到模式绑定的 definite assignment 上，建议没有给出规则。

语义模型：模式绑定的变量是新局部变量，绑定到模式函数的 `Out` 参数；IDE 应能显示 Out 形参的文档。命名实参匹配（`ConstructorCall(arguments:=args, kind:=kind)`）是否允许，未定——建议在 `Return` 侧用了命名实参，在模式调用侧只用了位置实参，两套规则未对齐。

#### 4. 与既有特性交互

- **`Select Case` 等值 Case**：工厂镜像改写了 `Case 函数调用()` 的既有含义（Q1）。逗号混排（`Case ExpressionStatement(...), 42`）撞上 2018.12.19 的逗号决议——"Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax"——以及 2014-02-17 悬了十二年的 Design1/Design2 分歧。`Case A(...), B(...)` 到底算两个模式还是模式加等值，未决。
- **`TypeOf ... Is` 流分析**（我们自己的前场决议）：`If ShapeOf x Is ConstructorCall(...)` 与 `If TypeOf x Is ExecutableStatementSyntax` 是"第二种做事方式"（设计原则 3），且与 `Is` 重载叠加。流分析会议已把 `If`/`IsNot` 守卫划给收窄引擎、把 `Select Case TypeOf` 划给模式匹配家族；本建议的载体夹在两条已定边界之间。
- **Out 实参/形参建议**：`Return True, kind:=...` 的多值返回与调用点隐式声明全部依赖第 8 章 Out 建议（2014-02-17 #42："If Integer.TryParse(s, Out [Dim] x [As Integer]) Then ..."）。这不是本建议能独立承担的设计——Out 的 definite-assignment 检查（2014-02-17："Warnings will be emitted for both use-before-assign and return-without-assign"）与模式失败的语义必须一次定清。
- **晚绑定 / Option Strict Off**：VBScript.NET 默认宽松。`Select Case ShapeOf node` 的 `node` 可能被声明为 `Object`；模式函数的主题参数是 `ExecutableStatementSyntax`。宽松下隐式收窄转换是否允许、模式是编译期构造还是运行期分发——未定义。`Suspect`：需在宽松路径实测，这是 VBScript.NET 特有的分叉。

#### 5. Breaking change 与兼容性

三条破坏面：

1. **`Case 函数调用()` 重解释**（Q1）——对现有合法代码的语义改写，最重。
2. **新关键字/标识符碰撞**——主线 `Matches`、本建议的 `ShapeOf`（已 Table）以及 `Pattern` 关键字/特性替代案都会把合法标识符用法保留字化。上一场我们已为 `ShapeOf` 操作符记了这一笔；本建议再叠一层。
3. **`Imports SyntaxPatterns` 的语义扩展**——`Imports` 今天绑定命名空间/类型；把"把一组函数引入表达式作用域"塞进 `Imports` 需要方法级 `Imports` 建议（本建议之外）先落地。若 `SyntaxPatterns` 是一个既有命名空间，改其含义就是破坏；若是新命名空间，则无破坏但引入新约定。

#### 6. Option Strict / 编译选项分叉

见 Q4。规则应该是：模式匹配是编译期构造，主题做运行时 `isinst`，不受 Strict On/Off 影响；宽松路径上主题 `Object` 时的模式绑定结果与严格路径一致。建议未给出任何分析。`Suspect`：VBScript.NET 默认宽松，这条不解决就不能进 1.0。

#### 7. IDE / IntelliSense 影响

模式绑定的变量（`argList`、`kind`、`args`）要在 Case 子句与分支体内正确补全、显示为局部变量；调试器在 Case 断点处能求值 `argList`。错误信息要定位到"嵌套模式的第几层第几个参数"——建议自己的 Drawbacks #2 承认了这一痛点，但没有任何设计。Roslyn 语法模型需要新增 Case 子句模式节点，与家族文法共享。成本中等偏上，可预期，但文档为零。

#### 8. 数据 / 普遍性

强论据只有一个：**这是 VBScript.NET 自己编译器的需求**——我们天天在语法树上行走，样板真实、收益直接。弱论据是其余的：建议没有提供任何量化数据或用户请求，受众窄（工具作者），与"数十万安静客户"脱节。2017.10.18 那个"inspecting 仪式遮蔽结构"的原则本来是写给 JSON/XML 数据分发场景的，语法树是其自然延伸但绝不是其主体。`Probably`：家族（类型分发）高频，命名模式（用户定义模式函数）是薄切片，工厂镜像是最薄的糖。

#### 9. 更简替代

- **手动 `TypeOf` 梯子**：现状，样板多但确定、可调试。
- **`Case x As Type` 声明模式 + `When` + 单独辅助函数**（家族 Phase 1，上一场已 Active）：覆盖大部分"判形状 + 取子值"，机制小得多。语法树场景可以写成 `Case stmt As ExpressionStatementSyntax When stmt.Expression Is ...` 的朴素组合，代价是可读性仍低于工厂镜像——但机器少得多。
- **`Case (latitude, longitude)` 式解构**：家族把解构位置模式（tuple/位置解构）划给了 Phase 2，与工厂镜像共用位置语法，但绑的是类型成员的 `Deconstruct`，不是工厂参数。
- **Analyzer / 生成器**：为 `SyntaxFactory` 生成模式函数（把"反转工厂"写成一个代码生成器而非编译器魔法）。如果价值在"少写字"，生成器也能给，且不占语言表面积。

#### 10. 成本 / 优先级

工厂自动反转是"Fantastic idea, and too hard to do"（2018.02.07 我们给跨语言方案的原话，此处适用）。它要求编译器理解任意工厂的形参列表并把形参位置变成解构槽位——若只对 Roslyn `SyntaxFactory` 特判，就是为自家 API 定制语法；若泛化，就是 2014-02-17 列出的四种分解机制的第三种（"like Scala a type has a canonical 'Unapply' method"），但那要显式契约（PROPOSAL C），不是魔法。家族阶段表已定：声明模式（Phase 1）→ 递归/元组模式（Phase 2）→ 之后再看 and/or/not。用户定义模式方法是 Phase 2+ 的事；工厂镜像在更后面。

#### 11. 运行时 / CLR 硬约束

无新 IL：模式编译为 `isinst` + 方法调用 + 成员访问，不触 PEVerify。**零反射依赖是硬红线**（2014-02-17：开放泛型 "this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"）——函数即模式天然不碰反射，这是本概念的唯一无争议优点。模式函数可以泛型化（它们只是函数），只要不靠反射消解。

#### 12. 值不值得做

逐条打分：**概念**（函数即模式）价值 6 / 成本 3 / 风险 3——值得，且在家族层与我们 2014 的 "dispatch on Integer.TryParse" 探索同源；**工厂镜像语法**价值 5 / 成本 8 / 风险 7——不值得现在做；**`If ShapeOf x Is` 载体**价值 3 / 成本 5 / 风险 8——上一场已倾向 Reject。合起来一句话：**我们要"函数可以是模式"，不要这份建议书写的语法。**

---

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — `Case 函数调用()` 重解释违反；`Pattern`/`Matches` 关键字有标识符碰撞风险。
2. **保持 VB-like** — "模式是一个函数"概念上 VB（复用、简单、低仪式）；但 `Case ExpressionStatement(InvocationExpression(MemberAccess(MeExpression(), IdentifierName("New"))))` 读起来像 XML，不像 VB 的英语式 `Case ... As`。工厂镜像尤其不 VB。
3. **不引入"第二种做事方式"** — `If ShapeOf x Is Pattern(...)` 与 `TypeOf x Is`/`Matches` 重复，违反。
4. **默认跟随 C#，除非有充分理由** — C# 走 `Deconstruct` 位置模式，主线选 `Matches`/`Case ... As`；"任意函数即模式"来自 F#/Scala 的 active patterns / Unapply，不是 C# 对齐点。我们 2014 年的兴趣是理由，但不足以作为 V1 偏离。
5. **读起来像英语、对新手友好** — 声明式形状描述可读，但嵌套镜像对新手不友好；`"New"` 这种常量槽位更是要懂 `SyntaxFactory` 才读得懂。
6. **不为边缘场景加特性** — 语法树分析对安静客户是边缘场景；对自家编译器不是。`Probably`：家族是高频，本建议是低频切片。
7. **避免隐蔽的控制流/语义变化** — `Case 函数调用()` 的重解释正是此类；`Return True, kind:=...` 的多值返回也是新控制流。
8. **不与既有语法冲突** — `Case 函数调用()` 与等值 Case 冲突、`Is` 与引用相等冲突（2018.12.19 明言）。
9. **消除常见样板** — 唯一且充分的原则支持点：确实消灭 `TypeOf` 梯子。但受众窄，且"少写字"用生成器也能给。
10. **冗长只在有用时是美德** — 工厂镜像的冗长描述形状，勉强有用；`ShapeOf`/`Is` 的仪式是无用冗长。

**主线对照（评价标准 2.3 表）**：模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`。本次细化：**函数即模式的概念**与主线方向一致（主线 2018 文法 Phase 2 之后的用户定义模式、我们 2014 的 `Integer.TryParse` 探索同源）；**工厂镜像语法** = Anthony 独立延伸，且依赖主线明确推迟的"嵌套模式"与 and/or/not；**`If ShapeOf x Is` 载体** = 与上一场 ShapeOf 决议冲突（那个操作符已 Table）。

---

### RESOLUTION

1. **场景成立，受众窄。** 语法树形状分析是真实的样板，符合 2017.10.18 原则，对 VBScript.NET 自家编译器有直接价值；但它不是"数十万安静客户"的场景，量化数据为零。

2. **"函数即模式"的概念原则上采纳，划归家族 Phase 2+。** 先例在我们自己：2014-02-17 "It would be nice to dispatch on `Integer.TryParse`"，2014-04-23 的 `<expr> matches T(args)` 与自动生成的 `operator Matches`（"if (f() matches T(var name, var x, var y))"）。它与用户定义模式方法建议是同一概念的两副面孔，应归并成一份设计。

3. **建议原文的语法载体不采纳。**
   - `Select Case ShapeOf node` 与 `If ShapeOf x Is Pattern(...)` 复用上一场已 Table 的 `ShapeOf` 操作符与 `Is` 塞模式（2018.12.19："We think `Is` will have ambiguity issues with the existing use for reference equality"；"Not all of us are happy with the reading of the `If` syntax."）。需要绑定的表达式语境按主线用 `Matches`。
   - `Case ExpressionStatement(...)` 与既有等值 `Case` 冲突，比 `Case x As Type` 的歧义更严重——那是"旧形态不可能合法"，这是"旧形态合法且含义翻转"。
   - 工厂镜像要求一个**从未定义**的机制（自动反转工厂），且与建议自己声明的"Out + Boolean"契约矛盾：工厂没有 Out 参数、返回节点而非 Boolean，按契约根本不是模式函数。示例不能按建议的机制编译。`Pattern` 显式契约（PROPOSAL C）是把它变可定义的前提，但那是另一个特性的设计。

4. **命名模式输入（`IdentifierName("New")` 的常量槽位）不得静默采用。** 兄弟建议 `proposal-named-pattern-inputs.md` 明确标"currently the 'argument' list for a named pattern is output only. This should be investigated."——本建议的示例依赖它却没调查它。要么随该建议调查后纳入，要么示例改写为无常量输入的形态。

5. **裸标识符绑定规则需要显式决策**：重复出现 = 新建变量（C# 式，重复命名报错）还是约束相等（F# 式）？作用域 = Case 子句块（Design1 先例）；`If` 形态 definite assignment：失败路径未赋值、使用即报错。无 discard（2018.12.19 "Probably add a discard identifier and a discard pattern"）——没有 discard 的递归模式是不完整的。

6. **工厂镜像的逗号混排、`When` 护栏、and/or/not 组合**均不先于主线打开：逗号守 2018.12.19 决议（comma 留在 Case，不进模式语法）；`When` 属于模式文法本身；组合不在前两个版本。

7. **深层嵌套就是本建议的卖点，而主线把嵌套标为 "Maybe not V1"。** 家族阶段仍按 2018.12.19：声明模式 → 递归模式（含元组模式）→ 之后再看组合。用户定义模式方法排进递归模式之后，工厂镜像再往后。

### Implication

- 通知提案作者返工：把"函数即模式"归并进用户定义模式方法建议（同一契约、同一 `Matches` 载体），本建议降级为"工厂镜像语法糖"的设计稿。
- 工厂镜像语法糖挂起，直到：① 自动反转工厂的机制被定义（`Pattern` 显式契约或 SyntaxFactory 特判，二选一，需 spec）；② `proposal-named-pattern-inputs.md` 的输入/输出区分出结论；③ discard 与 `When` 进入家族文法。
- `TODO`（供返工）：文法/BNF（Case 模式节点 + `Matches` 右侧）、绑定规则（裸标识符重复/作用域/definite assignment）、Option Strict 双路径验证、兼容性分析（`Case 函数调用()` 重解释 + 关键字碰撞）、semantic model/IDE 影响、与 `proposal-user-defined-pattern-methods.md` 的契约对比表。

### VB 基因对照（小结）

主体一致于"消除样板"与"低仪式"；偏离于原则 3/7/8（第二种做事方式、隐蔽语义变化、与既有 Case 冲突）——偏离集中在载体语法，不在概念。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：工厂镜像若保留，`ExpressionStatement(...)` 的子位是"常量输入 / 绑定 / 嵌套模式"三选一还是可混排？混排时解析如何远视？
- `OPEN QUESTIONS`：模式函数能否重载/泛型？`SyntaxFactory` 有多个 `ExpressionStatement` 重载，模式重载决议规则未定（2014-02-17 担心的"overload resolution in the late-binder"）。
- `OPEN QUESTIONS`：`Imports SyntaxPatterns` 的"引入函数"语义依赖方法级 `Imports` 建议；`SyntaxPatterns` 是命名空间还是模块约定，未定。
- `OPEN QUESTIONS`：裸标识符重复出现 = 约束相等还是新建变量（建议原文未答，我们也未定）——留给家族文法。
- `TODO`：量化"语法树形状分析样板"在真实工具链中的占比，为普遍性补证据（`Probably` 低，但 VBScript.NET 编译器自身可作样本）。
- `Follow-up`：与 `proposal-user-defined-pattern-methods.md` 合并契约；`proposal-named-pattern-inputs.md` 的输入调查结果回灌。

### 状态

- **LDM 状态：LDM Considering**（家族层）；本建议单列为 LDM No Plans 至返工。
- **三态判定（分解）**：概念"函数即模式" → **Active**（家族 Phase 2+，归并进用户定义模式方法）；工厂镜像语法 → **Table**（等机制定义）；`If ShapeOf x Is Pattern(...)` 表达式形态 → **Reject**（改用 `Matches`）。整体建议 → **Table**。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-named-patterns.md`（命名模式 / 解构语法）
- 来源：Anthony D. Green 原文第 7 章 "General Pattern Matching"（命名模式部分）；依赖第 8 章 Out 实参/形参建议与用户定义模式方法建议
- 配方目标：把"带 Out 参数、返回 Boolean"的函数用作模式，以工厂镜像解构语法在 `Select Case`/`If ShapeOf` 中递归匹配并拆解对象结构

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 3/5 | 主效果（声明式形状描述取代过程式遍历）概念清晰、示例来自原文且能说明意图；但**示例不能按建议的机制编译**——工厂镜像依赖未定义的自动反转机制，且与"Out + Boolean"契约自相矛盾；关键子效果（常量槽位、discard、When、组合）缺失。锚点≈"只覆盖部分场景；主效果显现但关键子效果缺失"。 | 已检查（已提供，未运行） | 无原型；核心机制未定义；受众窄、无量化数据 |
| 特性 | 3/5 | "模式即函数"是低仪式、可复用的 VB 式概念（延续消除样板基因）；但工厂镜像语法外来味重（读如 XML），且一份建议捆绑多个强能力：命名模式 + 工厂反转 + 常量输入 + `Imports` 引入函数 + 多值 `Return`。锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"（捆绑项已逼近 2 分边界）。 | 已检查 | 机制捆绑；载体与家族/上一场决议分叉 |
| 品质 | 3/5 | 六章节齐全、示例与原文逐字一致、未决问题 4 个且诚实；但**无文法/BNF、无边界小节、无 Option Strict/兼容性/semantic model 分析**，关键处边界含糊且**示例与自身契约冲突**（工厂非模式函数）。锚点≈"缺某一章节或在关键处边界含糊"（示例-正文冲突使其接近 2 分）。 | 已检查 | 无 Grammar 节；机制未定义；常量输入静默依赖未调查的兄弟建议；未标注主线 2018.12.19 先例 |
| 属性 | 3/5 | 对雷（VBScript.NET 自家编译器提速）、光（差异化——C# 无函数即模式）正向；暗风险突出：`Case 函数调用()` 重解释是语义破坏、`Is` 重载、新关键字、与 Table 掉的 `ShapeOf` 载体纠缠——文档未权衡。锚点≈"有得有失，文档未充分权衡"。 | 已检查（待定，预测性） | 破坏面未分析；载体与上一场决议冲突；依赖链过长（Out 建议 + 输入建议 + 方法级 Imports） |
| 炼金成分 | 3/5 | 来源标注准确（原文第 7 章、Out 建议、用户定义模式方法建议）；但最直接的先例未标注：2014-02-17 "dispatch on Integer.TryParse"、2014-04-23 `<expr> matches T(args)`、F# active patterns/Scala Unapply，以及主线 2018.12.19 的嵌套推迟决议——全部缺席。锚点≈"部分来源未标注；标注与影响有偏差"。 | 已检查 | 家族血缘未交叉标注；主线先例未标注 |

### 设计原则对照
- **与 VB 基因**：主体**部分一致**（消除样板 #9、低仪式 #10 的"模式即函数"概念）；**明显偏离**于 #3（`If ShapeOf x Is` 是第二种做事方式）、#7（`Case 函数调用()` 隐蔽语义变化）、#8（`Is` 与既有含义冲突）、#1（重解释破坏性风险）。
- **与主线关系**：概念 = **主线一致**（用户定义模式属家族 Phase 2+，2014 探索同源）；工厂镜像语法 = **Anthony 独立延伸**且依赖主线推迟的嵌套/组合；`If ShapeOf x Is` 载体 = **与上一场 ShapeOf 决议冲突**（该操作符已 Table，表达式形态倾向 Reject，改用 `Matches`）。
- **破坏性变更**：潜在有——`Case 函数调用()` 重解释（最大）、新关键字标识符碰撞、`Imports` 语义扩展；建议文档完全未分析。

### 总评
- **达成程度**：**部分达成**——"函数即模式"的概念成立且与我们自己的历史探索同源；但语法载体悬置、核心机制未定义、示例与契约矛盾、依赖链过长，离可设计状态很远。
- **LDM 三态建议**：整体 **Table**；分解后：概念 → **Active**（归并进 `proposal-user-defined-pattern-methods.md`，家族 Phase 2+）；工厂镜像语法 → **Table**（等 `Pattern` 契约或反转机制被定义）；`If ShapeOf x Is Pattern(...)` → **Reject**（改 `Matches`）。
- **主要问题**：① 工厂镜像依赖未定义的自动反转机制，示例按契约无法编译；② `Case 函数调用()` 与既有等值 Case 的语义冲突未分析；③ 常量输入静默依赖未调查的命名模式输入建议；④ 无 discard、无 `When`、无组合；⑤ 依赖链（Out 建议 + 输入建议 + 方法级 Imports）过长。

### 返工建议
- **补充章节**：`Grammar/BNF`（Case 模式节点、`Matches` 右侧、`Is` 消歧）；`机制定义`（工厂如何反转：`Pattern` 显式契约 vs SyntaxFactory 特判，二选一并给 spec）；`Edge cases`（常量槽位大小写、`Nothing` 主语、裸标识符重复、discard、深度限制、重载决议）；作用域与 definite assignment；`Option Strict` 双路径验证；兼容性分析（`Case 函数调用()` 重解释、关键字碰撞、`Imports` 语义扩展）；semantic model / IDE 影响。
- **补充证据**：主线 2018.12.19 嵌套/组合推迟决议对比表；2014-02-17 与 2014-04-23 家族先例交叉标注；原型分支与运行结果（状态栏链接）。
- **未决问题处理**：把 4 个未决问题升级为规范小节；新增：模式重载决议、命名实参匹配、`SyntaxPatterns` 命名空间约定、`Imports` 引入函数的语义边界。
- **设计探索**：与用户定义模式方法建议合并为一份契约（`subject + Out 输出 As Boolean`）；把"反转工厂"改造成代码生成器方案作为替代路线，评估是否比语言特性更划算。

---

## 附录：C# 生态与互操作考量

> 来源目录：`..\..\csharplang`（dotnet/csharplang 官方仓库镜像）。本附录中的 C# 引文均逐字核验于该镜像，路径相对其根目录。本提案（命名模式 / 函数即模式）与 C# interop 的关系为**中等**：不涉及内存布局、不产生新 IL、无反射依赖（正文 Q11 硬红线），但它正好落在 C# 模式匹配的两条主线上——「模式如何复用」与「类型系统如何替代手工分发」——所以值得单独对照。

### 相关 C# 现实方向

**C# 没有模式别名，也没有"给模式命名"的机制。** 模式匹配自 C# 7/8 落地（`proposals\csharp-8.0\patterns.md`）以来，模式复用只有两条路：

1. **提取为方法。** 位置模式绑定到类型的 `Deconstruct` 成员。2016 年 LDM 明确选择把解构定为实例/扩展方法：
   > "Deconstruction should be specified with an instance (or extension) method."
   > → `meetings\2016\LDM-2016-05-03-04.md`

   同一场会议记录了他们为什么不做"用户定义模式 / active patterns"：
   > "The choice limits the ability of the pattern to later grow up to facilitate "active patterns". We aren't too concerned about that, because if we want to add active patterns at a later date we can easily come up with a separate mechanism for specifying those."
   > → `meetings\2016\LDM-2016-05-03-04.md`

2. **用户定义模式（active patterns）仍在 backlog，未做。** 2022 年 LDM 重新审视 issue #4131：
   > "User-defined patterns, also known as active patterns in F#, are extremely powerful, but when designing them we need to be sure we're not painting ourselves into a design corner for other future pattern enhancements. There are also some interesting questions we will have to answer around exhaustiveness. Regardless of these questions, we see active patterns as one of the last pattern-related things that need to be added to the pattern feature to make it generally "complete", and are excited to look at them after we land the current pattern work."
   > → `meetings\2022\LDM-2022-02-16.md`

   结论是 "Into the backlog, for after we finish the current pattern features."（同上）。而 2015 年的设计笔记把 active patterns 与互操作直接挂钩：
   > "*active patterns* support interop and user-defined types"
   > → `meetings\2015\LDM-2015-03-25-Notes.md`

   即：**C# 的"函数即模式"构想（active patterns）与互操作动机同源，但十年来没有落地，2022 年仍被排在"当前模式特性全部完成之后"。** 另有 issue #1047 作为 active patterns 的追踪项（`meetings\2022\LDM-2022-02-23.md` 在讨论非恒定匹配时提到 "the scenario should wait for active patterns"）。

**C# 15 的 unions / closed hierarchies 是"模式复用"的另一条轴：靠类型系统闭合集合，而非命名模式。** `proposals\unions.md` 的 Motivation：
> "Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive."
> → `proposals\unions.md`

`proposals\closed-hierarchies.md` 的 Motivation：
> "Closed classes provide a way to indicate that a set of derived classes is complete, and allow consuming code to rely on that for exhaustiveness in switch expressions."
> → `proposals\closed-hierarchies.md`

C# 15 Kickoff（`meetings\2025\LDM-2025-08-18.md`，Unions 一节）对交付预期：
> "We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."

**关键互操作点：C# unions 的"非装箱访问模式"已经在用「返回 `bool` + Out 参数」作为模式访问契约。** `proposals\unions.md` 定义：
> "A `TryGetValue` method for each case type. The method returns `bool` and takes a single out-parameter…"
> → `proposals\unions.md`

编译器做 union 匹配时：
> "The compiler will prefer implementing pattern behavior by means of members prescribed by the non-boxing access pattern."
> → `proposals\unions.md`

这与本提案的模式函数签名（`(subject, Out ...) As Boolean`）**逐字同构**——C# 已经在内建特性里把"Boolean + Out"当作模式协议，只是没有把它开放成"任意用户函数"。

**元数据面**：closed hierarchies 以 `[IsClosedType]` + `[CompilerFeatureRequired("ClosedClasses")]` 暴露（`proposals\closed-hierarchies.md`，Lowering 一节）；unions 以 `[Union]` 特性 + `IUnion` 接口暴露（`proposals\unions.md`，Union interfaces）。VB 编译器若要消费 C# 15 的类型系统能力，必须认识这些属性。

### 现实 vs 提案

| 本提案元素 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| 概念：函数即模式 | 与 C# active patterns 构想同源（F#/Scala），但 C# 2016 选 `Deconstruct`、2022 仍把 active patterns 排最后 | **兼容（超前）** | 方向一致、无元数据冲突；C# 因 exhaustiveness 与"画地为牢"顾虑未做，VB 做是差异化，但不指望 C# 互操作伙伴 |
| 工厂镜像解构语法 | 相当于 active patterns / Scala Unapply；C# 明确推迟这类机制 | **兼容（同为搁置）** | C# 用"类型成员方法"替代"工厂反转魔法"；本提案 Table 工厂镜像与 C# 的取舍同向 |
| `If ShapeOf x Is` 载体 | C# 无对应（`Is` 右侧接类型/常量，不接用户函数） | **脱节** | 纯 VB 语法分歧，C# 无参照，不影响互操作 |
| 闭合类型分发（家族 Phase 1 `Case x As Type`） | unions / closed hierarchies 提供"闭合集合 + 穷尽" | **需桥接** | C# 用类型系统闭合集合换穷尽性；VB 若走命名模式路线得不到穷尽性（函数返回 bool，无闭合集），应与 M4 的复合类型/流敏感类型谓词并轨 |
| 零反射、编译期构造（Q11） | 与 C# AOT / trimming 方向一致（T5/T6） | **兼容** | 模式编译为 `isinst` + 方法调用，不触反射，正是 C# 想让类型系统承担的职责 |

**与 C# 关系总评（诚实简短）**：本提案最窄的立足点——语法树形状分析——在 C# 生态**没有对应物**（C# 编译器作者写的是同样的 `TypeOf` 梯子，C# 没给工具链一个声明式形状语法）。它的概念层（函数即模式）与 C# active patterns 同源但被 C# 明确推迟；它的家族层（闭合类型分发）恰是 C# 15 投入最大的方向。因此：**本提案对 C# 互操作不是障碍，但也不是借力点**——它落在 C# 已推迟与未设计的空白区，真正的互操作价值在家族层与 C# 类型系统能力对接。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态。** 模式匹配保持编译期构造（正文 Q6 已把 Option Strict 双路径定为硬约束）；与 unsafe-evolution 对 VB 的表态同向——C# 自己说：
   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   > → `proposals\unsafe-evolution.md`（本索引第四节已核实）

   命名模式概念不引入 unsafe、不引入反射，天然贴合"默认安全"的 .vbx 定位。

2. **source-gen 桥替代工厂魔法。** 正文"更简替代"已建议：把"反转 `SyntaxFactory`"写成代码生成器而非编译器魔法。这与 C# 的整体方向（编译期生成替代运行时动态，索引 T6）一致，且与第 3 点互补：`.vbx` 可用 source generator 为 C# union 类型生成模式函数。

3. **识别新元数据。** `.vbx` 编译器应能识别 C# 15 的 `[Union]`、`[IsClosedType]`、`[CompilerFeatureRequired]`、`IUnion` 与 `TryGetValue`，从而消费 C# 侧的闭合/穷尽类型。**最省力的互操作入口**：把 union 的 `TryGetValue(out T)` 直接映射到 VB 命名模式的"Out + Boolean"契约——C# 已把这个契约写进了元数据。

4. **穷尽性别指望命名模式，指望闭合类型。** 若 VBScript.NET 需要穷尽分发，C# 对齐路线是 unions / closed 类型谓词（决策文件 M4），不是命名模式；命名模式负责"可复用的形状测试"，两者分工。

### 对既有 RESOLUTION / 三态判定的影响

**无实质影响，三态判定维持；补一条家族层备注。**

- 概念 → **Active**（家族 Phase 2+）：与 C# 对 active patterns 的"最后才做"判断**同向但更积极**——VB 没有 C# 那套 exhaustiveness 的历史包袱，可把它提前到 Phase 2+。不冲突。
- 工厂镜像 → **Table**：C# 2016 已用 `Deconstruct` 方法替代该路线、2022 年仍在推迟 active patterns——Table 是安全且与 C# 一致的取舍。**维持。**
- `If ShapeOf x Is` → **Reject**：C# 无参照，纯 VB 语法决策。**维持。**
- 新增备注：家族 Phase 2+ 设计用户定义模式方法时，**应把 C# union 的 `TryGetValue` 访问模式纳入契约对照表**（正文 Implication 已列"与 `proposal-user-defined-pattern-methods.md` 的契约对比表"），确保 .vbx 能消费 C# 15 元数据。

### 引用纪律与 OPEN QUESTIONS

- 上文 C# 引文均逐字核验于 `..\..\csharplang`；来源路径已随文标注。
- `OPEN QUESTIONS`：
  1. C# 15 unions / closed hierarchies 的最终发布形态与元数据细节未定（本库工作区仍在演进，Kickoff 只承诺 "at least have preview versions"）；`.vbx` 对 `[Union]` / `[IsClosedType]` 的识别应等 C# 15 定稿后再冻结。
  2. C# active patterns（#4131）与本提案"函数即模式"是否会在语义上重叠——两者都未定义，无从比对；若未来 C# 落地，VB 需重评互操作边界。
  3. `TryGetValue` 契约在 C# 中是否会对"任意用户类型"开放（当前只对 `[Union]` 类型生效），未定义；若开放，将直接与本提案的命名模式竞争。
