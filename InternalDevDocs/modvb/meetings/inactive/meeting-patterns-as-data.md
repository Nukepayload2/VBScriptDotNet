# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天审查的是一份 **inactive** 建议——`proposal-patterns-as-data.md`（模式作为数据，Anthony 第 18.13 节实验性想法）。第 18 章是 Anthony 明确标注"还需要时间酝酿"的一批想法，本建议尤其薄：全文只有一句话的观察，没有任何语法。我们的任务不是背书它，而是认真决定：**它值得激活，还是保持搁置？**

这份建议恰好站在我们过去几场会议的正前方：`meeting-named-patterns.md` 把"函数即模式"的概念划归家族 Phase 2+、把工厂镜像语法判为 Table；`meeting-user-defined-pattern-methods.md` 确立了 `(subject, Out ...) As Boolean` 模式函数契约；`meeting-shapeof-pattern-matching.md` 把 `ShapeOf`/`Matches` 定为家族载体；`meeting-typeof-flow-analysis.md` 把 `Select Case TypeOf` 划给模式匹配。今天这份建议问的是更基础的问题：**模式能不能成为数据？**——它踩在家族地基的下面，所以必须先谈地基。

_诚实分层说明：本纪要逐句标注 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。所有引用的 vblang 主线会议决定、引文与 issue 编号均逐字核对自 `..\..\..\vblang/meetings/` 原始文件；Anthony 原文引文核对自 `..\..\AnthonyDesign_wordpress.txt`。找不到直接对应材料处，依据 vblang 设计原则与评价标准独立论证并显式标注。本文中的代码示例，凡标注"LDM 素描"的均为本会议自造的讨论用示例，**不是建议原文的语法**——建议原文没有任何语法。_

## Agenda

* [ModVB Proposal — Patterns as Data / 模式作为数据](#modvb-proposal--patterns-as-data--模式作为数据)

## ModVB Proposal — Patterns as Data / 模式作为数据

_来源：Anthony D. Green 原文第 18.13 节 "Patterns as data?"（`..\..\AnthonyDesign_wordpress.txt` L3026–3030，inactive / 实验性，仅一句话，无散文无示例）。Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；[vblang #119 – 社区 Case Match 讨论](https://github.com/dotnet/vblang/issues/119)；主线会议：2018.12.19（Pattern Matching）、2018.05.30（Europe trip / #304 / #167）、2017.10.18（JSON Literals / pattern principle）、2014-04-23（immutable data + pattern-matching）、2014-02-17（pattern decomposition / MatchFunction）、2018.03.21（expression trees）、2018.02.07（#117 等）；ModVB：`proposal-shapeof-pattern-matching.md`、`proposal-user-defined-pattern-methods.md`、`proposal-named-patterns.md`、`proposal-named-pattern-inputs.md`、`proposal-scripting-interpreted.md`_

### 场景与缺口

Anthony 在 18.13 只写了一句话，我们逐字引用：

> Composition is essential to managing complexity but as-is there isn't yet a way to pass patterns around as data or compose them. Fixing this could open some scenarios.

即：组合对管理复杂度至关重要，但按现状尚无办法把模式作为数据传递或组合它们；修复这一点可能打开一些使用场景。

We started from the proposal's own framing，并立即发现它其实藏着**两件事**，被一句话捆在了一起：

1. **"把模式作为数据传递"**（pass patterns around as data）——即**具体化（reification）**：让模式成为一种运行期值，可以存入变量、作为参数传递、在运行期构建。这是本建议真正的增量。
2. **"组合模式"**（compose them）——即 and/or/not 与嵌套复用。这是**组合**：让一个模式由其他模式构成。

这两件事**并不互相蕴含**。组合可以在匹配位置发生，不需要把模式变成运行期值；具体化则是一个完全不同量级的语言能力。把它们拆开，是本场讨论的第一步，也基本决定了我们后面的走向。

缺口陈述本身是诚实的，但它弱得惊人：Anthony 没有给出任何使用场景（"could open some scenarios"——**可能**打开一些场景），没有示例，没有语法，没有用户请求。对照 2017.10.18 我们给 JSON pattern 的处置——"Decision: Table, wait for feedback/scenarios and more matching"——这份建议连"scenarios"的边都没碰到。所以我们的开场立场是：**这是一根研究钩子，不是一份建议。**

### 候选方案

我们枚举了五种形态，覆盖"全做"到"什么都不做"。

**PROPOSAL A — 委托式：模式值 = 谓词 + 提取器。** 模式是 `Func(Of T, Boolean)` 判定的组合（And/Or/Not 组合子），提取（Out 绑定）由额外委托承担。今天就能写，零编译器改动。代价：不透明（不可内省、不可分析、IDE 无从展示），且"判定"与"提取"被拆成两半，模式失败与"提取结果为 Nothing"不可区分。

**PROPOSAL B — 表达式树式：把模式语法重化为 `Expression(Of Func(Of T, Boolean))`。** VB 已经会为 lambda 生成表达式树，可内省、可组合、可遍历。代价：2018.03.21 我们记录过 "Expression trees themselves will be unchanged. The IL output of expression trees is not expected to be high performance."；表达式树只覆盖可表示为表达式的子集；且它同样**无法承载 Out 绑定**（表达式树里没有可编译期验证的 ByRef 输出语义）。

**PROPOSAL C — 专用模式值类型：`Pattern(Of T)`（BCL 库）+ 语言重化操作符。** 一个带 `Match`/`And`/`Or`/`Not` 的类，配一个"把模式语法变成该类型值"的语法（引用/quote 机制）。这是最完整的"模式作为数据"，也是 F# quotation + active pattern 的路线。代价最大：需要 BCL 类型（按我们的分工，库层变更默认委托 C# LDM）、需要重化操作符（新语法）、需要先有模式语法可重化。

**PROPOSAL D — 只做组合，不做具体化。** 组合留在匹配位置：`AndAlso`/`OrElse` 式的 and/or/not 模式，这正是主线 2018.12.19 自己计划的东西——"Maybe _and_ and _or_ and _not_ patterns later (still uncertain on this)"。不引入任何"模式值"。

**PROPOSAL E — 什么都不做，维持 inactive。** 承认 18.13 是一根等待信号的研究钩子；把"组合"登记进家族 and/or/not 轨道，把"具体化"挂起直到出现具名场景。

### 代码示例（讨论用）

**示例 1 — 今天就能做的一半：把"判定"当数据传递（委托）。** 说明缺口比表面窄——谓词式"模式"今天已是数据：

```vb
' 今天合法：一个 Predicate(Of Animal) 就是"可作为数据传递的判定"。
Dim isDuck As Predicate(Of Animal) =
    Function(a As Animal) TypeOf a Is Duck

Dim ducks = pond.Where(Function(a) isDuck(a))
```

**示例 2 — 今天做不到的一半：把"判定 + 提取"一起打包。** 委托能带走"是否命中"，带不走"命中后绑定到哪个编译期变量"。绕过它的写法（用 `Nothing` 当哨兵）正好暴露缺陷：**"没命中"与"命中但提取结果为 Nothing"不可区分**：

```vb
' 绕过写法（今天能编译，但语义有缺陷）：
' Nothing 既是"没命中"也是"命中的 USPhoneNumber 为 Nothing"——两义。
Dim tryGetUSPhone As Func(Of Contact, USPhoneNumber) =
    Function(c As Contact)
        If TypeOf c.Number Is USPhoneNumber Then
            Return DirectCast(c.Number, USPhoneNumber)
        End If
        Return Nothing
    End Function
```

**示例 3 — LDM 素描：完全形态的"模式值"（非建议原文，仅讨论用）。** 一个运行期模式值必须同时携带"判定 + 提取 + 组合"，还要允许从配置/用户输入构建。它长什么样完全未定；我们这里只是把它画出来以讨论成本——注意这个形态已经是一个**库**（`Pattern(Of T)`），而不是语言特性：

```vb
' LDM 素描：看起来像库 API，而非语言语法。
' 若这个库能覆盖场景，语言特性（重化操作符）就是多余的。
Dim digit As Pattern(Of String) =
    Pattern.Literal("1").Or(Pattern.Literal("2"))
Dim token As Pattern(Of String) =
    digit.And(Pattern.Whitespace())
Dim state As Pattern(Of Contact) =
    Pattern.FromConfig(config("classifier"))
```

**示例 4 — 组合不必变成数据：在匹配位置组合（主线 and/or/not 轨道）。** 2018.12.19 已计划 "Maybe _and_ and _or_ and _not_ patterns later"；组合可以在 `Select Case` 匹配位完成，不需要任何"模式值"：

```vb
' 组合留在匹配位置——编译期构造，不引入运行期模式值。
Select Case ShapeOf contact
    Case b As BusinessContact When b.Revenue >= 1000000
        Return "enterprise"
    Case g As GovernmentContact
        Return "public"
End Select
```

### 权衡：Q&A

- **"模式是表达式吗？"——这是第一道墙。** 2018.12.19 我们为模式定下的基础定义是："a pattern is not an expression, but a thing that when matched results in an expression, in this context a Boolean expression."（逐字）。把模式变成数据，等于让模式成为值——**让模式成为表达式**。这不是小扩展，是对一条近期才定下的本体论决定的**反转**。支持具体化的一方需要拿出一个足够强的理由，而一句"could open some scenarios"显然不够。
- **"组合必须变成数据吗？"** 不必。组合（and/or/not、嵌套）可以在匹配位置表达，这正是主线 2018.12.19 的计划："Maybe _and_ and _or_ and _not_ patterns later (still uncertain on this)"、"Conjunctions are probably not in first or second version, C# thinking is these are much lower need/usage."（逐字）。**组合是编译期构造；具体化是运行期值。建议把两者捆在一起，是它最需要拆开的地方。**（见示例 4：组合在匹配位完成，不引入任何"模式值"。）
- **"谁真的需要把模式当数据传？"** 我们枚举了可能的受益者：(a) 规则引擎/校验库——从配置或数据构建一组判定再批量应用；(b) 编译器/工具链——语法树形状分析（我们自己在 `meeting-named-patterns.md` 讨论过）；(c) 脚本/交互环境——VBScript.NET 脚本模式里从用户输入构造模式再做分发。但 (a) 的"判定"今天就能用委托传（见示例 1），(b) 的受众是工具作者不是"数十万安静客户"，(c) 属于脚本引擎的领地而非编译语言（见下文 Q8 与 RESOLUTION 6）。**没有一个人群需要"VB 模式文法的具体化"本身。**
- **"模式区别于普通函数的地方是什么？"** 2018.12.19 说得明白："We need some compelling cases for non-matches. The most compelling case for patterns is TypeCheck/assignment"（逐字）——模式的独特价值在**类型判定 + 变量绑定**，而不在"判定"本身。而变量绑定（Out 输出）恰恰是**运行期模式值跨不过去的东西**：一个运行期值不可能把子值绑到调用方的编译期变量上。一旦模式成了数据，"命中后绑定到哪个变量"就死了，剩下的只是"调一个函数拿到布尔值"——这正是 2018.12.19 对 #119 的困惑："not sure whether this is a pattern or evaluation (or what distinctions matter here)"（逐字）。**把模式变成数据，模式就越发靠近普通函数，而普通函数今天就能传。**（见示例 2：绕过"绑定"的委托写法被迫用 `Nothing` 当哨兵，把"没命中"与"提取为 Nothing"混为一谈。）
- **"有没有现成的'模式值'？"** 有。家族 `proposal-user-defined-pattern-methods.md` 的契约——带 `Out` 参数、返回 `Boolean` 的函数——本身就是一种可传递、可组合的"模式值"：它就是个函数，函数当然可以存入变量。`meeting-named-patterns.md` 我们已确认 "函数即模式的概念原则上采纳，划归家族 Phase 2+"。**与其发明"模式值"，不如先问：`(subject, Out ...) As Boolean` 契约 + 委托，够不够？** `Probably`：对绝大多数场景够；不够的只是"从语法引用模式"和"Out 绑定跨边界"，而那正是最贵、最未验证的两块。
- **"会不会踩反射红线？"** 2014-02-17 讨论泛型模式时我们说过："this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"（逐字）。一个通用的"任意模式 → 运行期值 → 求值"引擎，必须证明自己不依赖反射。表达式树绕开了反射但受限且慢（2018.03.21）；委托也绕开但不透明。**这条红线不是不可绕，但它把方案的选项空间削得很窄。**
- **"静态分析怎么办？"** 运行期模式值不透明，编译器对"这个模式值穷尽吗、可达吗、能优化吗"一无所知。2018.12.19 对 Select Case 穷尽性说过："If you mean exhaustiveness of cases in `Select Case`, we don't have this today and introducing it is backwards breaking"（逐字）——今天没有穷尽性是事实，但**模式匹配的静态价值（判定+绑定、类型收窄）是我们正在建设的**，而数据驱动的匹配会在方向上堵死它。`meeting-typeof-flow-analysis.md` 刚把"收窄"做成编译期流分析——运行期模式值与此是两套哲学。
- **"VBScript.NET 脚本场景算不算强理由？"** 配置驱动的分发在脚本里是真实的（HTTP payload 分类、规则表）。但两件事：(1) 它今天就能做——`Select Case` 一个 tag 字符串，或 `Dictionary(Of String, Action)`，不需要把**模式文法**具体化；(2) 若真需要"运行期构造带类型的模式"，那属于 `proposal-scripting-interpreted.md` 的脚本引擎表示问题，由引擎用自己的表示解决，不进入编译语言面。**脚本场景是唯一听起来具体的理由，但它的落点不在编译语言。**

### 深度追问：LDM 拷问清单

我们按评价标准第五部分逐条过。被否定的思路也留档。

#### 1. 语法 / 文法歧义

建议没有任何语法，所以今天没有文法歧义可谈——但这恰恰是问题：**"模式值"在表达式位置的出现，直接踩在"a pattern is not an expression"上。** 若未来要加重化操作符（引用机制），它必须与家族文法的已决事项不冲突：`Matches` 是已定关键字（2018.12.19），`Is` 已被判有引用相等歧义（"We think `Is` will have ambiguity issues with the existing use for reference equality."，逐字），comma 守 "Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax."（逐字）。重化操作符只能是这三个之外的新形态。`OPEN QUESTION`：引用模式到值的语法是什么？没有任何候选被提出过。

#### 2. 角案例与边界语义

- **Out 绑定跨边界（最重）。** 匹配位上的模式把变量绑到分支块内，definite assignment 由编译器保证（2018.12.19："We don't usually let variables be used before they are declared, so it would look quite weird to allow this in one case"）。模式值在运行期求值——命中则"提取"发生，失败则提取的输出是什么？`Nothing`/默认值？那"失败"与"命中但输出为 Nothing"不可区分，正是 PROPOSAL A 委托式的老问题。**definite assignment 在数据边界的另一侧没有对应物。**
- **嵌套依赖主线推迟项。** 本建议的卖点是"可组合、可复用的模式"，而组合的载体（递归/嵌套模式）在主线 2018.12.19 被标为 "What does 'nested patterns' mean? Maybe not V1"（逐字）。`meeting-named-patterns.md` 也记录了家族阶段：声明模式 → 递归模式 → 之后再看组合。**先于家族打开"可组合模式值"，等于在依赖还没设计的东西。**
- **`Nothing` 主语、循环/自引用模式、递归深度。** 模式值若可被任意构建，能否引用自身（`Dim p As Pattern(Of T) = p.Or(...)`）？求值时栈深度？这些全部未定义，`Probably` 需要显式规范。
- **discard。** 2018.12.19 已记录 "Probably add a discard identifier and a discard pattern"（逐字）。没有 discard 的可组合模式值，在"不关心的子位"处会卡住。

#### 3. 作用域与绑定

模式引入的变量（`Case x As T` 的 `x`）作用域 = 所在 Case 子句块（2014-02-17 Design1 先例："The scope of the variable declaration is just that case clause"）。**模式值把"引入变量"从编译期概念变成运行期行为——语义模型对"一个模式值表达式"返回什么符号？** 它不绑定任何东西；它是值。IDE 无法在运行期模式值上给出任何静态信息。`meeting-named-patterns.md` 已把模式绑定的 definite assignment 列为硬伤；具体化把它放大到整个"模式值"类型。

#### 4. 与既有特性的交互

- **`Select Case` 与 comma。** 若模式值可以放进 `Case` 位置（`Case myPattern`），它与等值 `Case`、与 2018.12.19 的 comma 决议（comma 留在 `Case`，不进模式语法）如何共存？`Case myPattern, 42` 是"两个模式"还是"模式加等值"？这与 `meeting-named-patterns.md` 的工厂镜像逗号混排是同一个未决问题。
- **`TypeOf ... Is` 流分析（我们的前场决议）。** `meeting-typeof-flow-analysis.md` 刚把 `If TypeOf x Is T` 收窄做成编译期流分析。运行期模式值匹配与编译期收窄是两种哲学；若并存，就是原则 #3（不引入"第二种做事方式"）的正面教材。
- **用户定义模式方法。** 家族契约 `(subject, Out ...) As Boolean` 是最接近"模式值"的现成形态。`meeting-named-patterns.md` 已把它归并为家族 Phase 2+ 的一个概念。**具体化要么建立在该契约之上，要么与之竞争——建议没有提到它。**
- **表达式树。** VB 已会为 lambda 重化表达式树；`meeting-query-comprehensions.md` 之后查询理解与表达式树的关系我们还在整理。模式重化若走表达式树路线，需要先界定"模式表达式树"与"lambda 表达式树"的关系。
- **元组。** 2016-05-06 我们记录过元组的价值："Tuples allow multiple values, potentially of different types, to be bundled up and passed around as a unit"（逐字）。"把多个值打包传递"元组已解决；模式值解决的是"把一段匹配逻辑打包传递"——但后者用委托已解决大半。

#### 5. Breaking change 与兼容性

今天无破坏（无语法）。两个隐蔽面：

- **前向兼容的镜像问题。** 2014-04-23 我们问过主构造器/Select Case 的设计会不会 "block off a future world of pattern-matching"（逐字）。镜像问题：**当前家族文法会不会堵死未来的具体化？** 我们的评估：不会。and/or/not 留在匹配位置、comma 守 2018.12.19 决议、`Matches` 作为表达式操作符——当前设计都保留了一个未来"重化操作符"的余地。**这是对 inactive 想法的最优结果：今天不需要为它改变任何东西。**
- **BCL 类型归属。** 若 `Pattern(Of T)` 进库，那是库层变更。按我们的分工（评价标准 1.5），库/CLR 层变更默认委托 C# LDM。建议完全没谈这一层。

#### 6. Option Strict / 编译选项分叉

VBScript.NET 默认宽松。运行期模式值匹配一个 `Object` 主题：模式是**编译期构造**（把模式值编译成匹配器，`isinst` + 调用）还是**运行期分发**（对 Object 做动态探测）？两路径是两套实现。家族在 `meeting-named-patterns.md` 已把宽松路径标为 `Suspect`（需实测）；具体化把它放大到整个表示层。**两路径一致性未论证。**

#### 7. IDE / IntelliSense 影响

运行期模式值不透明：补全无法显示"这个模式值能匹配什么"；调试器无法展示其结构，除非表示是树形的（表达式树式）。对 VBScript.NET 的脚本场景，IDE 或许可以展示模式值的内容，但那需要表示层专门设计。`Probably`：这是具体化的确定性成本——用运行期灵活性换静态可展示性。

#### 8. 数据 / 普遍性

**零数据，且没有具名场景。** Anthony 自己写的是 "could open some scenarios"——一个假设，不是一个 use case。建议的四个未决问题没有一个提到受益者。对照 2018.05.30 的引述："We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now"（逐字）——本建议没有说明任何一位这样的客户要它。`Suspect`：规则引擎与编译器工具链可能是真实需求，但两者都是窄受众，且都被委托/库覆盖大半。

#### 9. 更简替代

- **谓词/委托**：把"判定"当数据传，今天就能写（见上文示例 1）。
- **`(subject, Out ...) As Boolean` 模式函数契约**：家族已提出，本身就是可传递的"模式值"。
- **表达式树**：VB 已会重化 lambda；若要"可内省的模式值"，这是现成表示，但慢、受限（2018.03.21）。
- **组合留在匹配位置**：主线 and/or/not（2018.12.19）。
- **`Pattern(Of T)` 库**：组合子 + `Match` 方法，不占语言表面积——若它够用，语言特性就是多余的。
- **源代码生成器**：从模式式声明生成模式函数——把"少写字"交给生成器，不碰语言。

**更简替代这一条，本建议不占优。** 唯一没有被覆盖的是"从 VB 模式**语法**引用出值"（重化操作符）——而它要求语法先存在。

#### 10. 成本 / 优先级

重化 = 编译器特性（新操作符/新类型绑定）叠在一个**尚未落地的前置**（模式匹配家族）之上。完整形态还牵动 BCL（委托 C# LDM）与 IDE。**优先级：低于整个模式匹配家族。** 在家族 Phase 2 之前，本建议没有可实现的载体。2018.02.07 我们对跨语言方案说过 "Fantastic idea, and too hard to do"（逐字）——此处适用它的后半句：**想法不一定错，但现在做就是 too hard to do。**

#### 11. 运行时 / CLR 硬约束

无新 IL 概念；真正的约束是**反射红线**（2014-02-17："we wouldn't want a language feature that depended on reflection"）。表达式树是绕开反射的现成机制但受限于表达式可表达集；委托绕开反射但丢弃结构；专用 `Pattern(Of T)` 需要 BCL 定义与求值器。**三个表示选型各有硬伤，建议一个都没讨论。**

#### 12. 值不值得做

逐条打分（价值 × 成本 × 风险）：

- **组合半场**：价值 6（组合是真实需求）/ 成本 3（匹配位置的 and/or/not，主线已计划）/ 风险 2 → **值得，但它是主线轨道的事，不是本建议的事。**
- **具体化半场（真正的"模式作为数据"）**：价值 3（唯一未被覆盖的是"语法重化 + Out 跨边界"，受众窄）/ 成本 8（新操作符 + BCL + IDE + 前置家族）/ 风险 7（反转 "pattern is not an expression"、反射红线、静态分析丢失）→ **现在不值得。**
- **整体**：价值是"组合"的（而它已属主线），成本与风险是"具体化"的——**捆绑净负。**

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 今天无破坏（无语法）；未来重化操作符与 `Matches`/`Is`/comma 的碰撞风险已由 2018.12.19 决议部分对冲。
2. **保持 VB-like** — 组合半场是普世价值；具体化半场是函数式语言口味（F# quotation/active patterns、Scala extractor），无语法可判断，但方向与"读起来像英语"天然紧张。
3. **不引入"第二种做事方式"** — **最大扣分项**：具体化 = "数据驱动的匹配"与"语法位的匹配"并存，正是本原则禁止的。`meeting-typeof-flow-analysis.md` 的编译期收窄与之哲学相斥。
4. **默认跟随 C#，除非有充分理由** — C# 没有"模式作为数据"；F# 有。本建议是 Anthony 独立延伸（仅 18.13 一句），需要的偏离理由远强于一句观察。
5. **读起来像英语、对新手友好** — 运行期模式值不透明，对新手是黑盒；无法展示、无法解释。
6. **不为边缘场景加特性** — 具名场景为零；可能受益者（规则引擎/编译器/脚本）全是窄受众。
7. **避免隐蔽控制流/语义变化** — 模式值在运行期被应用 = 匹配位看不到的动态分发；这是隐蔽控制流的变体。
8. **不与既有语法冲突** — 今天无语法可冲突；未来重化操作符需绕开 `Matches`/`Is`/comma 三个已决事项。
9. **消除常见样板** — 唯一可能支持点（"组合可复用"），但样板未被量化，且委托已消除大半"传递"仪式。
10. **冗长只在有用时是美德** — 无语法，暂不适用。

**主线对照（评价标准 2.3 表）：**

| 主题 | 主线 | ModVB（Anthony） | 本建议关系 |
|------|------|------------------|-----------|
| 模式匹配 | 2018.12.19 最期待、分阶段（声明→递归→之后再看组合） | `ShapeOf`+`Matches` | 前置缺失：先有模式，才能具体化模式 |
| and/or/not 组合 | "Maybe ... later (still uncertain on this)" | 家族 Phase 2+ | 组合半场与主线一致但抢跑；不要求数据 |
| "a pattern is not an expression" | 2018.12.19 基础定义 | 沿用 | **具体化半场与主线基础定义相抵** |
| 模式作为值/具体化 | 无 | 仅 18.13 一句 | Anthony 独立延伸；血缘 F#/Scala，未标注 |
| 元组作为数据单元 | 2016-05-06 "passed around as a unit" | 沿用 | "把东西打包传递"元组/委托已覆盖大半 |

根本张力照旧：主线"默认跟随 C#、对扩展设高门槛、维护 'pattern is not an expression'"；Anthony"大规模扩张、故意不跟 C#/F#"。本建议是"大规模扩张中踩在家族地基下面的一根钩子"。

### 诚实分层

- **事实**：Anthony 18.13 只有一句话（"Composition is essential to managing complexity but as-is there isn't yet a way to pass patterns around as data or compose them. Fixing this could open some scenarios."）；建议无语法、无示例；2018.12.19 "a pattern is not an expression"；2018.12.19 and/or/not "Maybe ... later (still uncertain on this)"；2018.12.19 "Conjunctions are probably not in first or second version"；2018.12.19 "What does 'nested patterns' mean? Maybe not V1"；2018.12.19 "We need some compelling cases for non-matches. The most compelling case for patterns is TypeCheck/assignment"；2018.12.19 对 #119 "not sure whether this is a pattern or evaluation"；2018.12.19 "Probably add a discard identifier and a discard pattern"；2014-02-17 "this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"；2014-02-17 "It would be nice to dispatch on Integer.TryParse"；2018.05.30 安静客户引述；2017.10.18 对 JSON pattern "Decision: Table, wait for feedback/scenarios and more matching"；2018.03.21 表达式树 "not expected to be high performance"；2016-05-06 元组 "passed around as a unit"；2014-04-23 "block off a future world of pattern-matching" 前向兼容提问。
- **Probably**：组合半场将由主线 and/or/not 轨道覆盖；"模式值"的大部分价值可用 `(subject, Out ...) As Boolean` 契约 + 委托覆盖；VBScript.NET 脚本是唯一可能产生真实需求的落点，但属脚本引擎。
- **Suspect**：具体化是否有任何"委托/库都覆盖不了"的场景（本会议没有找到）；`Pattern(Of T)` 库是否真的不够用（没有人写过原型）；脚本引擎是否会想要 VB 模式文法的重化，还是用自己更简单的表示。
- **OPEN QUESTIONS**：见下节。
- **TODO**：把"组合"与"具体化"拆成两个跟踪项；为激活信号登记证据需求。

### RESOLUTION:

1. **拆开观察。** 18.13 的两半必须分开对待：**组合**（and/or/not、嵌套复用）是主线 2018.12.19 已计划的轨道（"Maybe _and_ and _or_ and _not_ patterns later"），登记进家族工作项，**不需要把模式变成数据**；**具体化**（把模式作为运行期值传递/构建）才是本建议真正的增量，而它**没有任何具名场景**。建议原文把两半捆在一句话里，是它最大的结构缺陷。

2. **维持基础定义。** "a pattern is not an expression"（2018.12.19，逐字）是本建议的第一道墙。把模式变成数据 = 让模式成为值 = 反转这条近期才定下的本体论决定。**一句"could open some scenarios"不足以推翻它。** 若未来有人带着具名场景来，这条决定可以重开——但重开需要证据，不是热情。

3. **前置缺失，无可讨论。** 模式匹配家族（声明模式 → 递归模式 → 用户定义模式方法）本身尚未落地。**先有模式，才能把模式具体化。** 在家族 Phase 2 之前，本建议没有可实现的载体。

4. **更简替代覆盖大半价值。** 谓词/委托今天就能传递"判定"；`(subject, Out ...) As Boolean` 模式函数契约（`meeting-named-patterns.md` 已归并为家族 Phase 2+ 概念）本身就是一个可传递的"模式值"；组合留在匹配位置（主线 and/or/not）；`Pattern(Of T)` 库可不占语言表面积。**唯一未被覆盖的是"从 VB 模式语法引用出值"的重化操作符与 Out 绑定跨边界——而那正是最贵、最未验证、且踩反射红线的两块。**

5. **不改变家族文法以预留重化。** 2014-04-23 的前向兼容提问（"so that we don't block off a future world of pattern-matching"，逐字）的镜像版本我们评估过：当前家族设计（and/or/not 留匹配位、comma 守 2018.12.19 决议、`Matches` 作表达式操作符）**保留了一个未来重化操作符的余地**。今天不需要为本建议改变任何东西。

6. **脚本场景归脚本轨道。** 配置驱动分发的真实需求登记到 `proposal-scripting-interpreted.md`，由脚本引擎用自身表示解决；若脚本引擎证明需要"运行期构造带类型的模式"，那是引擎特性，不进入编译语言面。

### Implication:

- 建议状态保持 **LDM Table（inactive）**；在 proposal 头部加一行 "LDM 2026-08-08: Table，理由见会议纪要"。
- 把 18.13 拆为两个跟踪项：**组合** → 家族 and/or/not 轨道（2018.12.19 已计划）；**具体化** → 本建议（等信号）。
- 在模式匹配家族工作项登记 OPEN QUESTION："当家族 Phase 1–2 落地后，是否有把模式作为值的需求？"附触发条件。
- 与 `proposal-user-defined-pattern-methods.md` 对表：确认 `(subject, Out ...) As Boolean` 函数契约是最接近"模式值"的现成形态，作为重化替代评估的基线。
- 与 `proposal-scripting-interpreted.md` 对表：把"运行期构造模式"的潜在需求登记到脚本轨道。
- 若将来要提 BCL `Pattern(Of T)`：那是库层变更，按分工委托 C# LDM（评价标准 1.5）。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：重化操作符的语法（引用机制）是什么？——任何候选都未出现过，且必须绕开 `Matches`/`Is`/comma 三个已决事项。
- `OPEN QUESTIONS`：Out 绑定如何跨数据边界存活？definite assignment 在运行期失败路径上如何表达（"失败"与"命中但输出为 Nothing"如何区分）？
- `OPEN QUESTIONS`：表示选型：委托（不透明）/ 表达式树（受限、慢，2018.03.21）/ 专用 `Pattern(Of T)`（需 BCL + 重化操作符）——三选一未定，各有硬伤。
- `OPEN QUESTIONS`：运行期模式值的静态分析故事（穷尽性、可达性、优化）是什么？这与 `meeting-typeof-flow-analysis.md` 的编译期收窄哲学相斥。
- `OPEN QUESTIONS`：宽松模式（VBScript.NET 默认）下，运行期模式值与 `Object` 主题的交互（两套实现一致性）。
- `TODO`：为激活信号找证据——具名场景 + 量化收益 + 一个 `Pattern(Of T)` 库原型。
- `Follow-up`：与 `proposal-named-pattern-inputs.md`（输入/输出区分）、`proposal-user-defined-pattern-methods.md`（函数即模式契约）、`proposal-scripting-interpreted.md`（配置分发）三份建议交叉标注，避免读者以为具体化是一个独立空地。

### 状态

- **LDM 状态：Table（保持 inactive）。** 18.13 是一根研究钩子：组合半场归主线轨道，具体化半场无场景、无前置、反转基础定义。
- **三态判定：Table** — 不 Reject 到死的理由：组合需求真实（属主线）、具体化有 F#/Scala 先例、VBScript.NET 脚本可能产生真实需求。不 Active 的理由：前置缺失（模式家族未落地）、零场景零数据、与 "pattern is not an expression" 相抵、更简替代（谓词/委托/表达式树/and-or-not/库）覆盖大半价值。
- **激活所需信号**（明示）：① 模式匹配家族 Phase 1–2（声明 + 递归 + 用户定义模式方法）落地，确立"模式"这一语言概念；② 出现**具名场景 + 量化收益**，且证明 谓词/委托、表达式树、`Pattern(Of T)` 库、主线 and/or/not 均不够用；③ 一个库原型（`Pattern(Of T)` 组合子）展示"语言面差距"确实存在；④ 明确表示选型（委托/表达式树/专用类型）与静态分析故事；⑤ VBScript.NET 脚本轨道出现"运行期构造模式"的真实需求（此时它属于脚本引擎特性，不进编译语言面）。收到这些信号之前，不讨论。

---

## 附录：特性评价

# 建议评价报告：proposal-patterns-as-data.md

## 评价对象

- 建议：proposal-patterns-as-data.md — 模式作为数据（把模式作为运行期值传递/组合）
- 来源：Anthony 原文第 18.13 节 "Patterns as data?"（`..\..\AnthonyDesign_wordpress.txt` L3026–3030；**仅一句话**，无散文无示例）
- 配方目标：让模式可传递、可组合，成为一等数据（"Fixing this could open some scenarios"）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。改进（把模式当数据传）无示例、无语法、无量化；"could open some scenarios" 是假设不是 use case；4 个未决问题 = 核心语法未定型 → 效果证据封顶 | 已提供 | 无场景、无示例、无原型；组合/具体化两半被一句话捆绑 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。组合半场是普世价值（与主线一致）；具体化半场是 F#/Scala 口味（quotation/active pattern）且与主线基础定义 "pattern is not an expression" 相抵；无语法可判断 VB 化程度；捆绑两件独立能力 | 已检查 | 具体化与基础定义相抵；无 VB 化改造痕迹；先于家族抢跑 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、诚实标注"实验性/未定稿"、**没有凭空虚构语法**（明确说"具体形态完全待定"，这是相对多数兄弟建议的加分项）；但零设计实质：无示例、无文法、无兼容性、无 Option Strict、无主线先例标注；4 个未决问题具体诚实（≥4 项 → 效果封顶，不扣品质） | 已检查 | 无 Grammar/Compatibility/Precedent；无任何可运行示例；未引 2018.12.19 "pattern is not an expression" 与 and/or/not 轨道 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风=与主线基础定义相抵、先于其所依赖的家族（一致性断裂）；暗=若激活是类别反转 + 反射红线（2014-02-17）；雷/光=组合需求真实但属主线轨道；脚本场景是唯一亮色但归脚本轨道；无对冲设计 | 已检查（预测待定） | 与家族/主线一致性断裂未识别；反射红线未讨论；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源标注正确（18.13 一句、诚实标实验性、未给语法）；但未交叉标注：F# quotations/active patterns、Scala extractors、主线 2014-04-23 record+pattern 线程、2016-05-06 元组 "passed around as a unit"、2018.12.19 and/or/not 轨道——这些几乎逐条界定本建议的边界 | 已检查 | 家族血缘未交叉标注；主线 and/or/not 轨道未引；无杂质但成分标注严重不全 |

## 设计原则对照

- **与 VB 基因：主体未展开（无语法），方向性上偏离**。具体化半场是函数式语言口味（F#/Scala），与原则 #3（不引入第二种做事方式：数据驱动匹配 vs 语法位匹配）、#5（读起来像英语：运行期模式值不透明）、#7（隐蔽控制流：运行期动态分发）张力明显；组合半场与主线一致。仅 #9（消除样板）勉强支持，且样板未量化。
- **与主线关系：Anthony 独立延伸（仅 18.13 一句）**。组合半场与主线 2018.12.19 and/or/not 轨道方向一致但抢跑；具体化半场与主线基础定义 "a pattern is not an expression" **相抵**；与 `meeting-typeof-flow-analysis.md` 的编译期收窄哲学相斥。
- **破坏性变更：今天无（无语法）；未来有**。重化操作符与 `Matches`/`Is`/comma 的文法碰撞（2018.12.19 决议已部分对冲）；BCL `Pattern(Of T)` 属库变更（委托 C# LDM）。文档无兼容性分析。

## 总评

- **达成程度：未达成**（作为可设计提案）。18.13 是一根研究钩子而非建议：无场景、无语法、无前置、反转基础定义。但作为"等待信号"的 inactive 条目，它诚实、清晰，没有虚构语法——**它是弱建议，但不是坏建议。**
- **LDM 三态建议：Table（保持 inactive）**。组合半场归家族 and/or/not 轨道；具体化半场挂起，等具名场景 + 前置家族。
- **主要问题**：① 组合与具体化两半被一句话捆绑，评估被整体拖累；② 具体化无具名场景、无数据；③ 与 "pattern is not an expression" 基础定义相抵；④ 前置（模式匹配家族）未落地；⑤ 反射红线（2014-02-17）与静态分析故事缺失；⑥ 更简替代（谓词/委托/表达式树/and-or-not/库）覆盖大半价值。

## 返工建议

- **拆分建议**：把 18.13 拆为两份跟踪项——(a) 组合 → 家族 and/or/not 轨道；(b) 具体化 → 本建议（等信号）。拆分后分别评价。
- **补充章节**：Scenario（具名用户 + 量化收益，证明委托/库不够用）；表示选型对比（委托 / 表达式树 / 专用 `Pattern(Of T)`，逐条对照反射红线与 2018.03.21 性能记录）；Out 绑定跨边界设计（definite assignment 在运行期失败路径的表达）；静态分析故事；Compatibility（与 `Matches`/`Is`/comma 的碰撞矩阵）；Option Strict 双路径；Precedent（2018.12.19 "pattern is not an expression"、and/or/not 轨道、2014-04-23 前向兼容提问、F#/Scala 先例）。
- **补充证据**：一个 `Pattern(Of T)` 库原型，展示"语言面差距"真实存在；量化规则引擎/脚本场景；家族 Phase 2 前置实现。
- **未决问题处理**：把 4 个未决问题升级为规范条目；新增：表示选型、引用语法、静态分析、BCL 归属、宽松路径一致性 5 项。

---

## 附录：C# 生态与互操作考量

_本附录为本 meeting 追加的「C# 生态与互操作」章节，独立于上文的「附录：特性评价」。依据 `..\..\..\csharplang-index.md`（dotnet/csharplang 浓缩索引，下称「索引」）与 `..\..\..\csharplang\` 原始文件撰写。C# 原文一律逐字引用并标注来源（`→ proposals\...` / `→ meetings\...`）；无法在本库核实处显式标 **OPEN QUESTIONS**。_

### 相关 C# 现实方向

本提案的主题是「模式即数据」——把模式**具体化（reification）**为可传递、可组合的一等运行期值。在 C#/CLR/.NET 生态里**不存在**「模式作为数据」这一方向；恰恰相反，C# 的相邻方向全部把「模式」推向**更静态、更编译期**的一侧。五个相关方向：

**方向 A — C# 模式是编译期构造，被编译成决策树。** C# 8 递归模式把模式定义为「描述数据形状、与输入值比对」的语法，模式**可以是递归的**：

> Patterns are used in the *is_pattern* operator, in a *switch_statement*, and in a *switch_expression* to express the shape of data against which incoming data (which we call the input value) is to be compared. Patterns may be recursive so that parts of the data may be matched against sub-patterns.
> （逐字，→ `proposals\csharp-8.0\patterns.md`）

同一文档的「Some Possible Optimizations」表明编译器把模式编译成高效决策 DAG，**运行期不再保留模式结构**：

> The compilation of pattern matching can take advantage of common parts of patterns. For example, if the top-level type test of two successive patterns in a *switch_statement* is the same type, the generated code can skip the type test for the second pattern.
> （逐字，→ `proposals\csharp-8.0\patterns.md`）

这与「把模式作为运行期值」语义相斥：C# 的模式在编译期被优化掉，模式作为**语法**存在，不作为**数据**存在。

**方向 B — 「组合」已由语法解决：C# 9 的 `and`/`or`/`not` 组合子。** 本提案的「组合半场」（and/or/not、嵌套复用）在 C# 里不是数据，而是匹配位置的关键字组合子：

> Pattern *combinators* permit matching both of two different patterns using `and` (this can be extended to any number of patterns by the repeated use of `and`), either of two different patterns using `or` (ditto), or the *negation* of a pattern using `not`.
> （逐字，→ `proposals\csharp-9.0\patterns3.md`）

> Like all patterns, these combinators can be used in any context in which a pattern is expected, including nested patterns, the *is-pattern-expression*, the *switch-expression*, and the pattern of a switch statement's case label.
> （逐字，→ `proposals\csharp-9.0\patterns3.md`）

C# 用实际发布证明：**「组合」不需要把模式变成数据**——这正是本会议 RESOLUTION 1/4 的判断，C# 生态已把它做成了语法。

**方向 C — C# 15 类型系统承担更多模式职责（closed hierarchies + 编译期穷尽性）。** 索引 T5/T8：C# 15 正做 unions/closed hierarchies，由 AOT 驱动「类型系统承担更多本来靠运行期/反射完成的职责」。closed hierarchies 的实现已合并（LDM-2026-05-18 记录 "The initial implementation has now been merged, with the goal of getting the feature into .NET Preview 5"），其穷尽性警告是**编译期**行为：

> We also want the compile-time break when a closed hierarchy changes to remain useful: if a dependency adds a new subtype, many users want the compiler to point out the switches that need to be revisited.
> （逐字，→ `meetings\2026\LDM-2026-05-18.md`）

这是「模式作为类型」——模式匹配的价值被进一步锚定在**编译期可静态推理**上，与「模式作为运行期数据」正好反方向。

**方向 D — C# 的「形状/代码作为数据」三件套（表达式树 / 反射 / `dynamic`）全部受限或边缘化。** 索引 T7：
- 表达式树是 C# 最接近「代码作为数据」的机制，但受限于表达式可表达集，且与 ref struct 冲突（C# 14 first-class-span-types）：

> even inside expression trees - but ref structs are not supported by the interpreter engine
> （逐字，→ `proposals\csharp-14.0\first-class-span-types.md`；同页示例 `exp.Compile(preferInterpretation: true); // fails at runtime in C# 14`）

  C# 14 对表达式树仅做了一次小放宽（可选/具名参数）：

> Errors are reported for calls in `Expression` trees when the call is missing an argument for an optional parameter, or when arguments are named.
> （逐字，→ `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`）

- `dynamic` 在新安全模型下被质疑（unsafe-evolution 的开放问题）：

> `dynamic` (probably should match what BCL decides for reflection APIs)
> （逐字，→ `proposals\unsafe-evolution.md`）

- 反射类 API 被视为 AOT 负担（见方向 E）。

**方向 E — 编译期元编程（source generators / interceptors）替代运行期反射。** 索引 T6。LDM-2023-07-24 记录 interceptors 的动机正是「反射型场景难以 AOT 兼容」：

> This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible.
> （逐字，→ `meetings\2023\LDM-2023-07-24.md`）

方向上，C# 生态用**编译期生成**替代「运行期动态决定代码形态」，与本提案「运行期模式值 + 求值器」是两套哲学。

### 现实 vs 提案

| C# 现实方向 | 与本提案的关系 | 理由 |
|---|---|---|
| A：模式=编译期构造，编译成决策树（C# 8） | **冲突**（具体化半场） | C# 把模式当作语法并编译期优化掉，不保留运行期结构；「模式作为运行期值」与之语义相斥 |
| B：`and`/`or`/`not` 组合子留在匹配位置（C# 9） | **兼容**（组合半场，且 C# 已落地） | C# 用语法完成组合，不需要把模式变成数据——实证本会议 RESOLUTION 1/4 |
| C：类型系统承担模式职责 + 编译期穷尽性（C# 15） | **冲突**（具体化半场） | C# 15 把模式价值进一步锚定在编译期可静态推理；运行期模式值不透明、无法穷尽性检查 |
| D：表达式树/反射/`dynamic` 受限或边缘化（索引 T7） | **需桥接**（PROPOSAL B 路线） | 表达式树是唯一「形状作为数据」机制，但受限（ref struct 解释器不支持、性能、仅 C# 14 小放宽）；`dynamic` 被质疑；反射被 AOT 视为负担 |
| E：source-gen / interceptors 替代运行期反射（索引 T6） | **需桥接**（脚本场景） | 脚本「运行期构造匹配逻辑」与 C# 编译期生成方向相斥；需在编译产物层用生成器桥接 |
| 具体化整体 vs C# 生态全景 | **脱节** | C# 没有任何「模式重化」轨道（索引通查无此主题），方向 A/C 还把模式推向更静态；具体化若实现，将是 VB-only 的生态孤岛，无 C#/CLR 共享元数据表达 |

**与 VB 侧主线/家族的关系**：本会议引用的 VB 2018.03.21 表达式树记录（"Expression trees themselves will be unchanged. The IL output of expression trees is not expected to be high performance."）与 C# 方向 D 一致；VB 2014-02-17 反射红线（"we wouldn't want a language feature that depended on reflection"）与 C# 方向 E 的动机**同向但 C# 走得更远**——C# 不只是「不想依赖反射」，而是把「依赖反射」本身当作要消灭的对象（AOT/trimming 压力）。**具体化半场在 VB 侧踩反射红线，在 C# 侧同样被 AOT 红线拦住；两条线是同一条。** C# 没有「模式作为数据」，本提案的偏离理由在 C# 生态侧得不到任何支撑（原则 #4「默认跟随 C#，除非有充分理由」的生态证据指向维持 Table）。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态。** VBScript.NET 的编译语言面应跟随 C#：模式留在匹配位置、组合用语法（语义对齐 C# 9 的 `and`/`or`/`not`），编译期完成收窄/静态分析。需要「运行期构造匹配逻辑」的场景只出现在脚本解释层（`proposal-scripting-interpreted.md`），不进编译语言面——正是本会议 RESOLUTION 6。若未来脚本层要「从配置构建带类型的模式」，由脚本引擎用自身表示解决，避免 `Pattern(Of T)` 进编译语言面。
2. **source-gen 桥。** 若脚本需要「运行期数据 → 编译期匹配器」的映射（规则表、HTTP payload 分类），优先用源代码生成器在构建期生成编译的匹配函数，而不是反射 / `DynamicMethod` / 运行期表达式树编译。这与 C# 方向 E（source-gen 替代运行期反射）一致，也是 VBScript.NET 在「脚本 + 现代 .NET」双模路线下唯一能同时兼容 AOT/trimming 的出口。
3. **识别新元数据。** VB/.vbx 编译器必须能识别 C# 15 及 unsafe-evolution 引入的元数据，否则无法消费 C# 生态的新类型：`closed` 修饰符对应属性、`Union` 属性（discriminated unions 工作组）、`RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute`（unsafe-evolution）。C# 对 VB 的明确表态是：

   > We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.
   > （逐字，→ `proposals\unsafe-evolution.md`）

   但「VB 不需要 *requires-unsafe*」不等于「VB 可以无视这些属性」——调用标了 requires-unsafe 的 C# 成员时，.vbx 仍需正确校验安全边界（决策文件 M8）。同理，消费 C# 15 closed/union 类型时，VB 的 `Select Case`/模式匹配要认识其封闭性元数据才能给出合理的穷尽/不可达诊断（哪怕 VB 今天不做穷尽性）。这是**必须桥接**的点。
4. **模式家族文法与 C# 组合子对齐。** 家族 and/or/not（2018.12.19 轨道）若语义与 C# 9 `and`/`or`/`not` 对齐，跨语言读代码与互操作成本最低。**OPEN QUESTIONS**：VB 表达层已有 `AndAlso`/`OrElse` 运算符，模式组合子关键字选型（`And`/`Or`/`Not` vs `AndAlso`/`OrElse`）会否与 VB 既有运算符产生歧义——C# 无此问题（C# 没有 `AndAlso`），VB 需要单独裁决。
5. **BCL 归属与分工。** 若将来 `Pattern(Of T)` 真的进 BCL，按本会议 RESOLUTION 与评价标准 1.5，库层变更默认委托 C# LDM。但 C# LDM 目前**没有任何**「模式作为数据」的提案（索引通查无此主题）；这意味着任何 `Pattern(Of T)` 只能是 VB/.vbx 单方面提出的库类型，而非 .NET 生态共享类型——跨语言消费者（C#）不会理解它。这是具体化半场在生态层面的额外成本。

### 对既有 RESOLUTION / 三态判定的影响

**无改变；C# 生态证据强化既有结论。**

- RESOLUTION 1（拆开观察）：C# 9 `and`/`or`/`not`（方向 B）实证「组合」在匹配位置即可完成，无需数据——组合半场归主线轨道，C# 已先行落地。
- RESOLUTION 2（维持 "a pattern is not an expression"）：C# 把模式当语法、编译期优化掉（方向 A），是同一本体论决定的生态侧证据；C# 从未把模式重化为值。
- RESOLUTION 4（更简替代）：C# 9 组合子 = 「组合留匹配位」替代的发布版本；C# 没有反例表明「模式值」是刚需。
- RESOLUTION 6（脚本归脚本轨道）：C# 方向 E（source-gen）为「脚本层允许动态、编译产物走类型化」提供了可落地的桥。
- **三态判定维持 Table（inactive）不变。** 唯一新增观察：具体化半场不仅与 VB 侧基础定义、反射红线相抵，还与 C# 生态的方向 A/C（更静态、类型系统承担更多）**同向相斥**——C# 生态越往前走，具体化半场的生态立足点越窄。

### 引用纪律与来源

以下 C# 原文均逐字引自 `..\..\..\csharplang\`，已在本附录撰写时核实：

- "Patterns are used in the *is_pattern* operator, in a *switch_statement*, and in a *switch_expression* to express the shape of data against which incoming data (which we call the input value) is to be compared. Patterns may be recursive so that parts of the data may be matched against sub-patterns." → `proposals\csharp-8.0\patterns.md`
- "The compilation of pattern matching can take advantage of common parts of patterns. For example, if the top-level type test of two successive patterns in a *switch_statement* is the same type, the generated code can skip the type test for the second pattern." → `proposals\csharp-8.0\patterns.md`
- "Pattern *combinators* permit matching both of two different patterns using `and` (this can be extended to any number of patterns by the repeated use of `and`), either of two different patterns using `or` (ditto), or the *negation* of a pattern using `not`." → `proposals\csharp-9.0\patterns3.md`
- "Like all patterns, these combinators can be used in any context in which a pattern is expected, including nested patterns, the *is-pattern-expression*, the *switch-expression*, and the pattern of a switch statement's case label." → `proposals\csharp-9.0\patterns3.md`
- "We also want the compile-time break when a closed hierarchy changes to remain useful: if a dependency adds a new subtype, many users want the compiler to point out the switches that need to be revisited." → `meetings\2026\LDM-2026-05-18.md`
- "even inside expression trees - but ref structs are not supported by the interpreter engine" → `proposals\csharp-14.0\first-class-span-types.md`
- "Errors are reported for calls in `Expression` trees when the call is missing an argument for an optional parameter, or when arguments are named." → `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`
- "`dynamic` (probably should match what BCL decides for reflection APIs)" → `proposals\unsafe-evolution.md`
- "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." → `meetings\2023\LDM-2023-07-24.md`
- "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`

**OPEN QUESTIONS**（本库无法核实，需自行深挖）：
- VB 模式组合子关键字（`And`/`Or`/`Not` vs `AndAlso`/`OrElse`）与 VB 既有运算符的歧义裁决（C# 无此问题，VB 需单独设计）。
- C# LDM 是否可能在未来出现任何「模式重化」方向的提案（本索引通查未发现；若出现需更新本附录）。
- `Pattern(Of T)` 若作为 VB 单方库类型进入 CLR，C# 消费者（含 Roslyn 语义模型）对其不识别的具体表现（属 dotnet/runtime 生态，本库无正文）。
