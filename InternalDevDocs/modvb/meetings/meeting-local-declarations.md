# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上一场我们刚拆解过"沉浸式文件"——把一份捆绑了四个承诺的建议切成窄子集与全家桶分别裁定，效果不错。今天这份建议同样捆绑了三样东西，我们打算用同一把剪刀。而且 `Let` 又在场——上场的 TO DO 列表里有一条："与 `local-declarations`（`Let`）的解析兼容性必须先行核查"，今天就是那个"先行核查"。

## Agenda

* [Proposal - Local Variable Declarations / 局部变量声明增强](#proposal---local-variable-declarations)

## Proposal - Local Variable Declarations
_Related: 建议原文 [proposal-local-declarations.md](../proposals/proposal-local-declarations.md)；Anthony 原文第 3.1 节 "Local variables"；交互建议：[top-level-code（顶层 `Let`）](../proposals/proposal-top-level-code.md)、[guarded-let（`Let` 守卫式声明）](../proposals/inactive/proposal-guarded-let.md)、[set-statement（`Set` 解构赋值）](../proposals/proposal-set-statement.md)、[query-enhancements（查询元组解构）](../proposals/proposal-query-enhancements.md)；主线先例：2016-05-06 元组设计笔记、2014-02-17 LDM（声明表达式/`Let`）、2017-12-06 LDM（#48/#104/#102）_

### 场景与缺口

We opened with the three pains the proposal names, and we do not dispute that they are real.

第一，多返回值要拆。`GetCard()` 返回二元组，今天必须写 `Dim (suit, rank) = GetCard()` 才能一次拿到两个元素——Anthony 想要一条更直的写法。

第二，`As New` 对数组失效。`As New` 今天只能用于"带 `New` 构造函数的对象类型"（建议原文如此表述），数组必须在类型名之外单独 `New Byte(0 To 1023) {}`；"声明"与"创建"被拆成两段。

第三，匿名类型没有声明式写法。匿名类型只能通过表达式 `New With {emailAddress, userId}` 出现，不能写进声明的位置。

But——这是本纪要的第一条结构观察——**这不是一个特性。** 文档把三件事捆在一起：(i) 引入 `Let` 声明关键字（Anthony 全文使用的那个）；(ii) 修复 `As New` 对数组与匿名类型的支持；(iii) 无括号元组解构 `Let suit, rank = GetCard()`。三件事的血缘、风险与成本画像完全不同，不应同生共死。

### 候选方案

**PROPOSAL A — `Let` 作为通用声明关键字（Anthony 完整版）。** 采纳 `Let` 为声明关键字，与 `Dim`、`Const` 并存；无括号解构 `Let suit, rank = GetCard()`；`Let foreignKey As New With {emailAddress, userId}` 声明匿名类型。这是 Anthony 的一致愿景——他的整套设计（顶层代码、guarded-let、query-enhancements）都用 `Let` 作声明，本建议只是把这个关键字落进局部变量层。

**PROPOSAL B — 仅修复 `As New`（数组 + 匿名类型），不引入 `Let`。** 保留 `Dim`，把 `As New` 扩展到数组类型与 `New With` 匿名形式；解构维持 2016 年定案的括号形式 `Dim (suit, rank) = GetCard()`。`As New` 修复与关键字之争正交。

**PROPOSAL C — `Dim` 上的无括号解构。** 不引入 `Let`，只在 `Dim` 上放开 `Dim suit, rank = GetCard()`。We rejected this candidate early——它正面撞 `Dim` 逗号列表语义（见权衡 Q2）。

**PROPOSAL D — 维持现状。** 解构用 `Dim (suit, rank) = ...`，数组用 `Dim buffer(0 To 1023) As Byte`，匿名类型用 `New With {...}`。建议原文的 Alternatives 承认这保留了样板，我们同意——但"保留样板"的代价要用本场下面 Q8/Q9 的普遍性证据来称量。

### 权衡：Q&A

**Q1：为什么要有第二个声明关键字？** 这是 `Let` 最硬的一道墙。2014-02-17 LDM 讨论声明表达式时明确问过——"Q. Does the keyword "Let" cause ambiguities if we're using this inside query expressions? Note that "Let" is bad for query expressions."——并最终以 `RESOLUTION: None of this feels naturally "VB"ish.` 收场。We want to be precise about the citation: 那次否决的是声明**表达式**（在表达式里声明变量），与声明**语句**关键字不是一回事；但它把 `Let` 这个字与"不 VB"绑定过一次，且那句对查询歧义的担心与今天完全相同。`Dim` 是 VB 自 .NET 起唯一的变量声明关键字，是百万行代码的正统血脉。我们问：引入 `Let` 到底买到了什么？答得出来的只有"读起来像英语"（`Let name = "Ada"` 是祈使句，原则 #5）——这对脚本化产品是真实的吸引力，但对"数十万安静客户"是又一个要学的词。还有一个历史反讽：VB6 里 `Let` 是**赋值**关键字——`Let x = 1` 把值放进变量——与我们今天讨论的"声明"含义正好相反。复活一个曾表赋值的词来表示声明，概念上是拧着的。`Suspect`：VB.NET 已不接受语句位置的 `Let x = 1`（解析器按查询子句上下文拒绝），所以把它改为声明是"错误→程序"的增量而非破坏；但这条必须对 parser 逐字验证，不能凭印象。

**Q2：无括号解构是歧义，不是糖。** 2016-05-06 设计笔记对元组解构定过案。当时的笔记列了一组解构语法——`Dim (x, y) = GetPoint()`、`For Each (x, y) In GetPoints()`、`From (x, y) In GetPoints()`、`Let (x, y) = GetPoint()`、`Select (x, y) = GetPoint() ' Can't actually do this, breaking change.`——然后记下了一句关键的自疑：`Question: Superfluous parentheses seem not very VB-ish.` 并给出结论 `Yes, but they need parenthesis. Resolves some ambiguities, such as the last example, which would be a breaking change.` 换句话说：**括号是解法，不是装饰。** 为什么？因为 VB 的 `Dim` 是逗号列表：

```vb
Dim a, b = GetCard()    ' 今天：a 声明为 Object（无初值）；b = GetCard()，b 绑定整个元组
```

而 Anthony 的 `Let suit, rank = GetCard()` 要求同一个逗号把 `suit`、`rank` 变成解构目标。同一逗号两种语义，靠右侧是不是元组来猜——这正是 2016 年笔记说括号必须保留的那类歧义。2017-12-06 会议又补了一刀，讨论多 `For` 变量时明说：`VB already has a strong precedent for multiple consecutive statements/constructs of the same kind being combinable into a comma-separated list: Imports, Dim, Next, From, Let, Case (required in this case).` `Dim` 的逗号列表语义是语言级先例，不能为无括号解构让路。这把我们引向"隐蔽语义变化"的禁地——正是 `Return?` 被拒的同类理由。

**Q3：`Let` 与查询子句 `Let` 的解析冲突，比想象中更近。** 与顶层代码那场会议的判断一致（那里已把此点记为 TO DO）：查询 `Let` 只出现在 `From ... Let ... Select` 上下文，语句位置的 `Let` 是另一个 parse，`Probably` 可上下文区分。但注意一个更尴尬的事实：2016 年笔记里的查询解构语法之一就是 `Let (x, y) = GetPoint()`——`Let` 在查询里**已经**是解构关键字了。于是本建议的语句级 `Let suit, rank = GetCard()` 与查询级的 `Let (x, y) = GetPoint()` 是同形字：

```vb
From p In products
Let q = p.Price * 1.1      ' 查询子句 Let（既有语法）
Select q

Let q = 42                 ' 语句级声明 Let（新语法）—— 解析器需区分上下文
```

而且 ModVB 里 `Let` 的载荷正在变重：guarded-let 的 `Let v = e Else Return Null`、query-enhancements 的 `Let firstMatch = From ...`、本建议的 `Let suit, rank = ...`。同一个 contextual keyword 撑三个语法面，parser 测试矩阵会显著变宽。2014 年那句 "Let is bad for query expressions" 不是对当时场景的抱怨，而是对 `Let` 这个字长期可复用性的警告。

**Q4：`As New` 数组——边界与初始化器如何共存。** `Dim buffer As New Byte(0 To 1023) {}` 里，`(0 To 1023)` 是边界，`{}` 是元素初始化器。空 `{}` 好办（零初始化）；非空 `{}` 的元素数必须与边界校验。而现行 VB 里 `Dim buffer(0 To 1023) As Byte = {1, 2, 3}` 是错误——显式边界与集合初始化器在一条 `Dim` 里今天**不共存**。也就是说，`As New` 形式是语言里第一个让"边界 + 初始化器"并存的语法点：它要么创立新先例并回头放宽 `Dim` 的既有限制，要么被套进同一条校验规则。两边的语义必须一致，不能出现 `Dim` 禁止、`As New` 放行的新旧两套行为。另一个文法点：`As New` 之后的 `Byte(0 To 1023)` 在现有文法里是"类型名 + 数组边界"，而不是"类型 + 构造参数表"——parser 必须特判，否则 `New Byte(...)` 会被当成构造调用。可定义，但必须写进文法。

**Q5：`As New With` 匿名类型的绑定。** `Let foreignKey As New With {emailAddress, userId}` 中，裸标识符 `emailAddress`、`userId` 复用既有匿名类型属性名推断（`New With {emailAddress}` 今天就会推断出属性 `emailAddress`）。真正的设计问题是：`As` 后面跟的不是类型名——匿名类型无法具名——那么语义模型里 `foreignKey` 的类型是什么？答案是统一后的匿名类型（同程序集、同名同型同序 → 同一匿名类型），但这条要写进 spec。另一个更直白的问题：`As New With` 是否必须与 `Let` 绑定？`Dim foreignKey As New With {emailAddress, userId}` 能否独立合法？建议把三件事耦合死了，我们看不到独立的理由。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Let` 与查询子句、`Let` 与 VB6 赋值、无括号解构与 `Dim` 逗号列表——三重歧义已在 Q1–Q3 展开。`As New` 数组的 `(0 To 1023)` 在 `As New` 位置是数组边界文法而非构造参数表（Q4）。`As New With` 的 `With` 子句不是类型（Q5）。句法层面没有一条干净的路绕过这些判定，每一处都是上下文相关文法。这与评价标准第五部分第一条拷问直接对上：有 body 没 body 怎么区分？这里更尖锐——同一串字符（`Let x = e`）在查询内/外、在声明/赋值历史中含义不同。

#### 2. 角案例与边界语义

- **解构不匹配**：`Let suit, rank = GetCard()` 若 `GetCard()` 返回三元组或非元组，是解构错误还是退化为两条独立声明？语法检查与类型检查的先后、交互未定义。
- **`As New` 数组边界 + 初始化器**：非空 `{}` 元素数与边界不符怎么办（超/不足）？`Dim buffer(0 To 1023) As Byte = {1, 2, 3}` 今天就是错误——`As New` 形式必须复用同样的校验，不另造一套（Q4 详述）。求值顺序：先按边界分配再填充，还是初始化器推导边界？若同时给边界与初始化器，边界是否可省略（`As New Byte() {}`，由初始化器推导长度）？
- **匿名类型统一**：`As New With` 变量与 `New With` 表达式产生的匿名类型是否统一？匿名类型按属性名、类型、声明顺序统一，顺序敏感。
- **`For Each` / `Using` 解构**：2016 笔记已支持 `For Each (x, y) In ...`；Anthony 原文在 `Using one, two, three = GetTriplet()` 也用了解构。本建议只碰声明，不改 `For Each`/`Using`，但必须声明这个边界，避免与 query-enhancements 的 `From x, y In ...` 各说各话。

#### 3. 作用域与绑定

`Let suit, rank = GetCard()` 中 `suit`、`rank` 是局部变量，语义模型返回 `LocalSymbol`；`As New With` 的 `foreignKey` 类型是匿名类型。匿名类型不可具名，所以 `As New With` 不会引入名字冲突——这是它比 `Let` 干净的地方。解构目标名与作用域内既有变量重名时的处理（`Dim x` 在同一方法内重复声明今天就是错误，解构目标是否沿用同规则）建议未讨论。`Probably`：沿用 `Dim` 规则即可，但需写明。

#### 4. 与既有特性的交互

- **`Dim` 逗号列表**：最直接的交互，Q2——无括号解构直接改写既有逗号语义。
- **查询子句 `Let`**：Q3；2016 笔记里 `Let (x, y) = GetPoint()` 已占用了"`Let` + 解构"的查询上下文。
- **`For Each` / `Using` 解构**：2016 定案与本建议应保持一致语义，不得分叉。
- **`Set` 赋值语句**（姊妹建议）：`Set suit, rank = GetNextCard()` 对既有变量解构赋值，与 `Let` 声明式解构互为镜像——如果 `Let` 无括号解构被否决，`Set` 的无括号解构同样站不住，两份建议应同案裁决。
- **匿名类型与 late binding**：匿名类型是真实 CLR 类型，`Option Strict Off` 下成员访问晚期绑定正常；无特殊交互。
- **`Const`**：`Let` 是否允许常量声明（`Let Const`）？建议未涉及。
- **typeless-declarations**：无类型声明默认 `Any` 与 `Let x = ...` 的推断交互（见第 6 条）。

#### 5. Breaking change 与兼容性

`As New` 数组与 `As New With` 都是增量（今天的错误变合法，错误变程序不构成破坏）。真正的兼容面是 `Let`：语句位置 `Let` 今天报错，改成声明是"错误→程序"；但查询 `Let` 子句必须原样保留，任何让语句 `Let` 与查询 `Let` 共享解析器的改动都会触及百万行 LINQ 查询。`Suspect` 无遗留破坏（VB6 `Let x = 1` 在 VB.NET 不接受），但这是三条里唯一需要 `langversion` 门控与回归测试矩阵的。与顶层代码那场会议一致：`Let` 与查询子句的解析兼容性"必须先行核查"。

#### 6. Option Strict / 编译选项分叉

`Let x = ...` 必须与 `Dim x = ...` 的推断规则逐位一致（`Option Infer On/Off`；无类型默认 `Object`/`Any`——后者与 typeless-declarations 建议交互，若默认改 `Any`，`Let x = 5` 的推断随之变化）。无括号解构在 `Option Strict Off` + `Option Explicit Off` 下的行为需要专项验证：宽松模式下 `suit` 是否会被当作既有变量隐式赋值而非新声明？严格/宽松两路径行为必须一致，否则又是一套分叉。建议对两路径只字未提。

#### 7. IDE / IntelliSense 影响

输入 `Let` 后的补全：语句 `Let` 与查询 `Let` 的补全上下文不同，编辑器必须知道用户站在哪个上下文。`As New With` 变量在 InfoTip 里显示什么（匿名类型名是编译器生成的 `VB$AnonymousType_0` 之类，对新用户是噪音）？错误文案需要新设计（如"解构目标数量与元组元素不匹配"）。`As New` 数组的边界语法在补全中如何高亮。这些都要原型验证，不做进规范等于没设计。

#### 8. 数据 / 普遍性

`As New` 数组的场景真实且高频（`Dim buffer(0 To 1023) As Byte` 的样板确实常见），但边际价值要打问号——今天已有零初始化的写法，`As New` 只是把创建挪了个位置并允许内联初始化器。`Let` 关键字替换没有任何用户请求数据；解构的刚性需求已被 `Dim (x, y) = ...` 满足。这份建议的普遍性证据全部是"我觉得更顺手"，没有一条可量化——对照 Implicit-default-optional 的 85% 统计标准，这里没有数据。We are not saying the pain is fake; we are saying we cannot measure it.

#### 9. 更简替代

- 解构：`Dim (suit, rank) = GetCard()` 已存在（2016 定案），零新增。
- 数组：`Dim buffer(0 To 1023) As Byte` 已存在；`As New` 只对内联初始化器有增量价值。
- 匿名类型：`Dim foreignKey = New With {emailAddress, userId}` 已存在，少一个 `As` 而已。
- `Let`：纯关键字替换，无能力增量。

一个尖锐的总结：**这份建议的三块里，有两块（解构、匿名类型）的"更简替代"就是现状本身，只有 `As New` 数组有真实的能力增量。** 这不是否决——能力增量少不代表不做——但它决定了优先级的排法。

#### 10. 复杂度 / 成本 / 优先级

`As New` 数组 + 匿名类型 = 中量 spec + 文法 + 语义模型工作，无 CLR 介入。`Let` 关键字 = parser + spec + IDE + 全部下游特性（guarded-let、top-level-code、query-enhancements）的地基；它的成本不与本建议的收益匹配，而与整个 ModVB 生态的"声明方言"决策匹配。若只做 `Let` 而不做方言整体决策，是本末倒置——这正是我们把 `Let` 推到 Table 的成本论证。

#### 11. 运行时 / CLR 硬约束

无。三块都是纯语法/降级层概念；数组、匿名类型、元组都是既有 CLR 概念。`As New` 数组只是把 `Newarr` 的生成挪进声明里。无 PEVerify、无表达式树问题（匿名类型不可进表达式树，与现状一致）。2016 笔记对元组晚期绑定的担心——`We might need to update the late-binder to support tuple conversions but not for tuple names. Lot of work and so many plot holes as names are associated with declarations, not values.`——不适用于本建议：解构是编译期动作，不经过晚期绑定。

#### 12. 值不值得做

价值：`As New` 数组修复真实但边际；`As New With` 价值中低；无括号解构价值被歧义抵消；`Let` 是方言决策。成本：中（前三块）/ 高（`Let` 若带全生态）。风险：`Let` 有查询冲突与方言分叉风险；无括号解构有语义变化风险。**结论：`As New` 数组值得做且只该做它；`As New With` 值得继续设计；无括号解构该否决；`Let` 该上 Table 等方言决策。**

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）**——`As New` 两块通过（增量，错误→程序）；`Let` 待 parser 核查（`Suspect` 无遗留破坏，但查询子句是真实回归面）。
- **原则 2（保持 VB-like）**——`Dim` 是正统血脉；`Let` 作声明关键字在主线对照表 2.3 中标注为 **Anthony 独立延伸**（主线"无"，Anthony"全文使用"）。
- **原则 3（不引入第二种做事方式）**——`Let` 正面撞墙：`Dim` 与 `Let` 并存就是第二种声明方式，且 2014 年已把 `Let` 与"不 VB"绑定过一次。`As New` 修复不撞——它让既有 `As New` 覆盖更多类型，不新增方式，这是本建议最干净的一条。
- **原则 4（默认跟随 C#，除非有充分理由）**——C# 7 有 `var (x, y) = ...` 解构；VB 已有 `Dim (x, y) = ...`，方向一致。Anthony 的无括号形式无 C# 对应，`Let` 关键字 C# 无对应（C# 用 `var`）。两条都是"偏离但无充分理由"。
- **原则 5（读起来像英语、对新手友好）**——`Let name = "Ada"` 是 `Let` 唯一无可争议的正资产。但它服务的是脚本化产品的调性，不是"读代码"的可读性——`Dim` 与 `Let` 都一样的短。
- **原则 6（不为边缘场景加特性）**——解构与数组 `As New` 不是边缘；`Let` 关键字替换是边缘偏好，没有请求数据支撑。
- **原则 7（避免隐蔽控制流/语义变化）**——本建议无控制流；但无括号解构的"逗号变义"是隐蔽语义变化的又一个例子，与 `Return?` 被拒同类。这是本场最重的扣分项。
- **原则 8（不与既有语法冲突）**——`Let` 与查询子句冲突是真实的冲突面，2014 原话直接点名；无括号解构与 `Dim` 逗号列表冲突是第二个冲突面。
- **原则 9（消除常见样板）**——`As New` 数组正中靶心；解构被 `Dim (...)` 已满足；`Let` 不消样板，只是换字。
- **原则 10（冗长只在有用时是美德）**——`Dim` 不冗长，`Let` 不更短（等长），这条不构成换字理由。

对照主线表（2.3）：`Let` 替换 `Dim` 一行，主线"无"、Anthony"全文使用"——**Anthony 独立延伸**。元组解构是主线已定案特性（2016-05-06），Anthony 的改法是变体。`As New` 数组修复是主线无记录、方向一致的增量。三块血缘各不相同，这正是我们坚持拆开裁定的原因。

### RESOLUTION:

**RESOLUTION:** We split the document into four items and rule on each separately.

1. **`As New` 数组修复 — `Active`（返工后）。** 采纳把 `As New` 扩展到数组类型的做法，但**与 `Let` 解耦**：`Dim buffer As New Byte(0 To 1023) {}` 必须独立合法。规格必须定义：`As New` 后数组边界文法的判定、边界与集合初始化器并存时的元素数校验（与 `Dim` 侧的既有限制统一，不另造一套）、无 `{}` 时的零初始化、求值顺序。多维与交错数组列为 OPEN QUESTION，v1 可不做。
2. **`As New With` 匿名类型 — `Consider`。** 有价值的声明式匿名类型写法，但需要单独 speclet 定义"`As` 后接不可具名类型"的语义模型表示与匿名类型统一规则，并裁定 `Dim x As New With {...}` 是否独立合法。低优先级，排在 `As New` 数组之后。
3. **无括号解构 `Let suit, rank = GetCard()` — `Reject`（作为语法）。** 逗号在 `Dim`/`Let` 中已有"独立声明列表"语义（2017-12-06 明示先例），无括号解构把同一逗号变成"解构目标列表"，是隐蔽语义变化。2016-05-06 已定案括号必须保留以消解歧义（`Yes, but they need parenthesis.`）。解构价值由既有 `Dim (suit, rank) = GetCard()` 承担；若未来 `Let` 落地，只接受 `Let (suit, rank) = GetCard()`（带括号）。姊妹建议 `set-statement` 的 `Set suit, rank = GetNextCard()` 同案否决其无括号形式。
4. **`Let` 声明关键字 — `Table`。** 不否决：脚本化产品里"声明即祈使句"的价值真实，Anthony 生态对其有一致依赖。但它是一个**方言决策**（是否放弃 `Dim` 这一正统血脉、与查询子句 `Let` 长期共处），不是本建议能单独承载的。复活信号：① 与 top-level-code、guarded-let、query-enhancements 联合做一次"声明方言"整体评审；② parser 完成语句 `Let` 与查询 `Let`、VB6 赋值 `Let` 的兼容性核查；③ 出现可量化的用户需求证据。在此之前，本建议不把 `Let` 当作已采纳前提。

**Implication:**

- 返工建议原文：把三块拆成三份文档或显式切分章节；`As New` 数组写文法（BNF）与角案例；补 Compatibility/breaking-change 小节；补 `Option Strict On/Off` 双路径验证示例。
- 与 `set-statement` 对表：解构赋值的括号规则必须与声明式解构同案裁定。
- 与 top-level-code、guarded-let、query-enhancements 对表：`Let` 依赖方联合作出方言决策，本建议不得抢先落关键字；顶层代码会议的 TO DO（"`Let` 解析兼容性必须先行核查"）在本场后仍开放。
- 最小原型：`As New` 数组（含边界 + 空/非空初始化器）+ 语义模型 + IDE 补全验证。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 文档整体 `Consider`；其中 `As New` 数组 `Active`（返工后）、`As New With` `Consider`、无括号解构 `Reject`（语法）、`Let` 关键字 `Table`（附复活信号）。PROPOSAL C 按"否决的备选方案"留档。

### OPEN QUESTIONS / TODO / Follow-up

- [ ] `As New Byte(0 To 1023) {1, 2, 3}` 元素数与边界不符时的精确诊断与校验规则；与 `Dim` 侧既有限制的语义统一。
- [ ] `As New Byte(0 To 1023)` 不带 `{}` 是否合法（倾向：合法，零初始化）。
- [ ] 多维 `As New Integer(0 To 9, 0 To 9) {}`、交错数组是否纳入 v1（倾向：v1 不做，OPEN QUESTION）。
- [ ] `As New With` 变量在语义模型 / InfoTip / 补全中的类型表示；匿名类型统一规则（顺序敏感）。
- [ ] `Dim x As New With {...}` 是否独立于 `Let` 合法（倾向：应独立合法）。
- [ ] 语句位置 `Let x = 1` 在现行编译器中是否报错（`Suspect` 是），parser 兼容性核查——顶层代码会议遗留的 TO DO。
- [ ] 无类型声明默认 `Any`（typeless-declarations）与 `Let x = ...` 推断的交互。
- [ ] `Option Strict Off` + `Option Explicit Off` 下无括号解构与隐式声明的行为验证（若语法未被否决——现已否决，本条关闭，改记入留档）。
- [ ] 解构目标名与作用域内既有变量重名时的处理（沿用 `Dim` 规则？）。

### 状态

- **LDM 状态：`Consider`**；`As New` 数组子项 `Active`（返工后），`As New With` `Consider`，无括号解构 `Reject`，`Let` 关键字 `Table`。
- **三态判定：Consider** — 价值真实但捆绑了风险画像不同的三块；拆分后各得其所，`As New` 数组值得先行落地。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-local-declarations.md` — 局部变量声明增强（`Let` 声明关键字 + 元组解构 + `As New` 数组/匿名类型）。
- 来源：Anthony 原文第 3.1 节 "Local variables"（`..\AnthonyDesign_wordpress.txt` L817–827，三行示例原样摘录：`Let suit, rank = GetCard()`、`Dim buffer As New Byte(0 To 1023) {}`、`Let foreignKey As New With {emailAddress, userId}`）。
- 配方目标：一次从方法返回值取多个变量（解构）、`As New` 一处完成数组/匿名类型创建、减少样板。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **3/5** | Motivation 三点痛点真实，但改进可衡量性差：无括号解构与 `Dim` 逗号列表冲突使主示例存疑；`As New` 数组相对既有 `Dim buffer(0 To 1023) As Byte` 仅在内联初始化器上有增量；匿名类型已有 `Dim x = New With {...}`。锚点 3："主效果显现但关键子效果缺失/消退" | 已检查 | 无原型；解构示例的歧义未承认；三块未切分，效果证据相互拖累 |
| 特性 | **3/5** | `As New` 修复是纯 VB 基因（让既有构造延续），但 `Let` 是主线对照表 2.3 的 **Anthony 独立延伸**，无括号解构偏离 2016 定案的括号形式，匿名类型块借鉴既有 `New With` 推断。锚点 3："明显借鉴外部/独立延伸但做了 VB 化改造，或打包了次要无关能力" | 已检查 | `Let` 与查询子句冲突（2014 原话点名）；与 `Dim` 逗号列表语义冲突；三块血缘不同却捆绑 |
| 品质 | **3/5** | 六章节模板完整，示例与原文逐字一致，3 个未决问题具体诚实（1–3 健康区间）；但状态栏为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`），无文法（BNF），无 Compatibility/breaking-change 小节，无 `Option Strict` 分叉讨论，解构示例未标注与逗号列表的冲突。锚点 3："缺某一章节或在关键处边界含糊" | 已检查 | 红旗：状态占位链接；无文法；无兼容性分析；无括号解构这一关键语义风险未在 Drawbacks 中识别 |
| 属性 | **3/5** | 对 VBScript.NET：`As New` 数组/匿名类型加速迭代（雷/水正向）、盘活既有构造（光）；但 `Let` 引入方言分叉（风=与主线 2014 否决相断裂）、查询冲突与全生态依赖（暗），文档未对冲。锚点 3："有得有失，文档未充分权衡" | 已检查（预测待定） | `Let` 的方言分叉与三块捆绑的风险未权衡；暗风险无对冲设计 |
| 炼金成分 | **3/5** | 主要材料（Anthony 原文 3.1、VB 既有 `As New`/匿名类型/元组解构）可辨认；但未声明：无括号解构对 2016 定案（括号必须）的偏离、`Let` 对 2014 否决的偏离、C# 7 `var (x,y)` 解构的存在、与 set-statement/guarded-let/query-enhancements 的 `Let` 依赖。锚点 3："部分来源未标注；标注与影响有偏差" | 已检查 | 来源与影响有偏差；跨建议依赖未交叉引用 |

### 设计原则对照

- **与 VB 基因**：部分一致。`As New` 数组修复符合原则 1/9（不破坏、消样板）；无括号解构违反原则 7（隐蔽语义变化）与 8（与既有语法冲突）；`Let` 违反原则 3（第二方式）并撞 2014 年"不 VB"判定，仅有原则 5（可读性）一项正资产。
- **与主线关系**：**Anthony 独立延伸**（对照表 2.3 `Let` 替换 `Dim` 一行：主线"无"，Anthony"全文使用"）；元组解构是主线已定案特性（2016-05-06）的变体；`As New` 数组修复为方向一致的增量。
- **破坏性变更**：无（`As New` 两块为错误→程序增量；`Let` `Suspect` 无遗留破坏）。风险面：语句 `Let` 与查询 `Let` 的解析兼容，需 parser 核查 + `langversion` 门控。

### 总评

- **达成程度**：**部分达成**。`As New` 数组修复的动机与形态清晰、可论证、无破坏；`As New With` 有价值但未定型；无括号解构是错误方向的尝试（撞 2016 定案与逗号列表先例）；`Let` 是超出本建议承载能力的方言决策。
- **LDM 三态建议**：**Consider**。子项拆分后：`As New` 数组 `Active`（返工后）、`As New With` `Consider`、无括号解构 `Reject`、`Let` `Table`（附复活信号）。
- **主要问题**：(1) 三块捆绑，风险画像不同，互相拖累；(2) 无括号解构与 `Dim` 逗号列表的语义冲突未识别，而 2016 已定案括号必须；(3) `Let` 是方言决策且与查询子句冲突、与 2014 否决相断裂，证据不足；(4) `As New` 数组的边界 + 初始化器语义、文法未写；(5) 无兼容性小节、无文法、状态占位链接。

### 返工建议

- **范围切分**：三块拆成三份文档或显式分节；声明 `As New` 数组为独立小特性，`As New With` 独立 speclet，`Let` 移交"声明方言"整体评审。
- **补充章节**：`As New` 数组的文法（`As New` 后数组边界文法的判定）与角案例（边界 + 空/非空初始化器、求值顺序、与 `Dim` 侧边界/初始化器不共存的统一）；Compatibility/breaking-change 小节（语句 `Let` vs 查询 `Let`、`langversion` 门控）；`Option Strict On/Off` + `Option Infer` 双路径验证示例。
- **补充证据**：最小原型（`As New` 数组 + 语义模型 + IDE 补全）；在 `Dim (x, y)` 已满足解构需求的前提下，`Let` 无括号形式的用户需求数据；`Let` 在现行 parser 下语句位置的报错行为验证。
- **未决问题处理**：无括号解构按 `Reject` 归档（引用 2016-05-06 与 2017-12-06 两个先例）；`Let (suit, rank)`（带括号）若 `Let` 落地可复议；`As New With` 与 `Dim x As New With` 的独立合法性问题；与 set-statement 同案裁定解构赋值括号规则。

---

## 附录：C# 生态与互操作考量

> 本附录依据 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引）并在 `..\..\csharplang` 中逐条核实。本提案三块（`As New` 数组、`As New With` 匿名类型、无括号解构、`Let` 关键字）均为**纯语法/降级层概念，零新元数据**（本场第 11 条已确认），与 C# interop 的直接关系不在"新 CLR 能力"，而在两点：**C# 对同一组痛点（局部声明样板、解构、类型重复）的解法差异**，以及 **.vbx 与 C# 局部变量 IDE 体验的对齐**。C# 原文逐字引用均标注来源文件；无法在库内取得逐字原文的标准属性，如实标注为「标准属性」并给 spec 链接。

### 相关 C# 现实方向

C# 在"局部变量声明"这一主题上的现状与走向：

1. **声明位置自由是 C# 的既定底仓，不是演进点。** C# 自 1.0 起局部变量声明就是语句，可出现在块内任意语句位置；作用域为所在块、声明点前引用为错误。这是 ECMA-334 标准属性，本库 `spec` 目录只保留链接（`spec\variables.md` → dotnet/csharpstandard §9.2.8 Local variables、§9.4.4.5 Declaration statements）。C# 没有为"允许在哪里声明"再做过提案，因为自由度早已给足；它反而把这份自由度往调用点推——C# 7 `out var` 的原文：**"The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument."** → `proposals\csharp-7.0\out-var.md`。

2. **`var` + NRT：隐式类型局部变量被可空注解接管。** C# 3 引入 `var`（隐式类型局部变量）后，C# 8 NRT 进一步规定推断规则：**"`var` infers an annotated type for reference types. For instance, in `var s = "";` the `var` is inferred as `string?`."** → `proposals\csharp-8.0\nullable-reference-types-specification.md`（§nullable implicitly typed local variables）；以及 **"The type inferred for local variables declared with `var` is informed by the null state of the initializing expression."** → 同文件（§Type inference for `var`）。即 C# 的"隐式类型"在现代 NRT 下默认带可空注解。

3. **元组解构（C# 7）用括号，且自认括号是消歧义手段。** C# LDM 2016 定案解构声明：**"In most of the places where local variables can be introduced and initialized, we'd like to allow deconstructing declarations - where multiple variables are declared, but assigned collectively from a single tuple (or tuple-like value):"** → `meetings\2016\LDM-2016-04-12-22.md`（Deconstructing declarations）。同一段给出的定案形态是：

   ```csharp
   var (first, last) = GetName();   // Optional shorthand for all var
   ```

   括号是语法的一部分，C# 与 VB（`Dim (suit, rank) = ...`）在此同构——本场 Q2 引用 VB 2016 笔记的 "Yes, but they need parenthesis."，C# 侧是同一结论。

4. **target-typed `new`（C# 9）明确排除数组；数组样板由 C# 12 collection expressions 另辟蹊径。** C# 9 的 `new()` 免去类型重复，但规范 Miscellaneous 明列数组排除：**"Array types: arrays need a special syntax to provide the length."** → `proposals\csharp-9.0\target-typed-new.md`。同一份规范把 target-typed `new` 从 `foreach` 集合、`using`、**解构**等无目标类型上下文排除。C# 对"数组 + 初始化器"痛点的答案不是扩展 `new`，而是 C# 12 集合表达式：**"Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values."** → `proposals\csharp-12.0\collection-expressions.md`（Summary）；其 Motivation 点名数组样板：**"Arrays, which require either `new Type[]` or `new[]` before the `{ ... }` values."** → 同文件。注意 nuance：collection expressions 覆盖"由元素初始化"的形态，**不覆盖**"固定长度 + 零初始化"（`new byte[1024]`）——本提案 `As New Byte(0 To 1023)` 的零初始化场景在 C# 至今没有糖。

5. **C# 拒绝过"声明作为表达式"。** C# 曾提案 declaration expressions（`(var st = _type.SpecialType).IsValueType() ...` 与 ref 参数位置声明），归档于 rejected：**"Support declaration assignments as expressions."** → `proposals\rejected\declaration-expressions.md`。它与本提案 `Let` 共享"把声明塞进更紧凑位置"的动机，但 C# 的落点是 out var / pattern variables / 目标类型推断，而不是第二声明关键字或声明表达式——这与本场 `Let` 上 Table、无括号解构 Reject 的取向同向。

6. **声明能力随低层主线继续扩张。** C# 13 允许 async/iterator 方法在无 await/yield 的段内使用 ref/unsafe 局部（`proposals\csharp-13.0\ref-unsafe-in-iterators-async.md`）——C# 对局部声明的演进方向是"更多位置可声明、更多类型可声明"，而非"引入新声明关键字"。

### 现实 vs 提案

按本场 RESOLUTION 四个子项与 C# 现实对表：

| 子项（判定） | C# 现实 | 关系 | 理由 |
|---|---|---|---|
| `As New` 数组（`Active`） | target-typed `new` 排除数组；C# 12 用 `[1, 2, 3]` 覆盖"元素初始化"、仍无"固定长度零初始化"糖 | **兼容，不同源** | 解决同一痛点（数组样板），但 C# 走新字面量语法、VB 走扩展既有 `As New`。产出都是普通数组（`Newarr`），无互操作冲突；属原则 4 例外条款下"有充分理由"的偏离（VB 基因、延续既有构造、零初始化场景 C# 无对应） |
| `As New With` 匿名类型（`Consider`） | target-typed `new` 在匿名类型属性位置被禁（`new { Prop = new() }`）；C# 匿名类型自 C# 3 起无声明式写法 | **兼容，VB 特色** | C# 无对应形态，亦无阻碍；匿名类型是真实 CLR 类型（本场第 4 条确认），该写法不与任何 C# 元数据冲突 |
| 无括号解构 `Let suit, rank = ...`（`Reject`） | C# 解构同样要求括号 `var (first, last) = GetName();` | **与 C# 一致** | Reject 不是偏离 C#，而是与 C# LDM 2016 同向——括号是消歧义手段（本场 Q2），C# 也这么定 |
| `Let` 声明关键字（`Table`） | 推断声明关键字是单一 `var`；declaration expressions 被拒 | **无对应、无压力、无阻碍** | C# 生态既不产生"跟随"压力（不会因 C# 演化而被迫做 `Let`），也不构成互操作障碍（`Let` 声明产生普通局部变量，零新元数据）。Anthony 的 VB 特色主张在此自主空间最大；本场 Table 定位正确，C# 现实不改变该定位 |

结论性观察：**本提案四子项中，与 C# 生态真正"脱节"的一项没有**——`As New` 数组与 C# 不同源但方向平行（都是消灭数组样板），`Let` 是纯方言自主，无括号解构反而与 C# 同向。这与 M8 所指的"unsafe 模型分裂"类硬冲突不是同一性质。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：`As New` 数组、`As New With` 匿名类型都是静态声明，天然落在"默认安全"侧，与 C# 方向一致。建议 .vbx 保持这条线，把动态留给 `Any`/晚期绑定（决策文件 M2 的双模路线：脚本层允许动态、编译产物走类型化），不要把"声明式类型写法"与动态绑定混为一谈。
2. **NRT 感知的推断**：若 .vbx 的 `Dim x = ...` / `Let x = ...` 走 `Option Infer`，需对齐 C# 的 NRT 规则——`var` 推断可空注解类型（`var s = ""` → `string?`）。.vbx 若要消费 C# 库的可空注解局部推断，须能识别 NRT 注解元数据（`[Nullable]` 特性），否则推断结果与 C# 不一致。
3. **source-gen 桥**：本提案三块均不产生新元数据，对 source-gen 零障碍。但 C# 12 collection expressions 引入构造元数据：**"A *create method* is indicated with a `[CollectionBuilder(...)]` attribute on the *collection type*."** → `proposals\csharp-12.0\collection-expressions.md`——.vbx 若要消费 C# 集合构造 API（自定义集合类型的字面量构造），需识别 `[CollectionBuilder]`。
4. **识别新元数据（无直接关系但需具备）**：按决策文件 M8，.vbx 需能识别 unsafe-evolution 引入的 `RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute`（已在 `proposals\unsafe-evolution.md` 核实存在）。C# 对 VB 的原文表态：**"We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."** → `proposals\unsafe-evolution.md`。局部声明不涉及指针，但 .vbx 运行在 .NET 11+ 并与 C# 代码互操作时需具备该识别能力，否则无法校验"调用 C# requires-unsafe 成员"的安全性。
5. **IDE 对齐**：C# 的声明位置自由（块内任意位置）是 Roslyn/VS 已建模的常态；.vbx 复用修改版 Roslyn VB 编译器，天然继承"声明位置决定作用域边界"的工作区模型。新增语法面（`As New` 数组的边界文法、`As New With` 的匿名类型表示）必须与 C# 局部变量的补全 / InfoTip / 作用域建模对齐，否则同一工作区里 .vbx 与 C# 的声明体验分叉——这正是本场第 7 条 IDE 追问在跨语言视角下的版本。

### 对既有 RESOLUTION / 三态判定的影响

**无推翻性影响**；C# 证据只增不减，四子项判定原样成立。

- **Reject（无括号解构）获得 C# 旁证**——C# 也要求括号，进一步坐实本场 Q2 "括号是解法不是装饰"。
- **`As New` 数组 `Active` 获得参照物**——C# 12 collection expressions 是"元素初始化 + 数组"的长期替代路线，但 C# 至今无"固定长度零初始化"糖，本提案该场景仍是无竞品增量；建议把"是否长期引入字面量构造"列入 OPEN QUESTION。
- **`Let` Table 地位不受影响**——C# 现实既不推动也不阻碍方言决策，权重仍在本场原论证（查询子句冲突、2014 否决、`Dim` 血脉）。

### OPEN QUESTIONS / 需进一步核实

- [ ] .vbx 是否长期引入 C# 12 collection expressions 风格的字面量构造（`[1, 2, 3]`）作为 `As New` 数组的替代/补充？若引入，与 `As New` 并存是否构成原则 3 的"第二种做事方式"风险？
- [ ] .vbx 的 `Option Infer` 是否/如何对齐 C# NRT 的 `var` 可空注解推断（`var s = ""` → `string?`）？本库 NRT 规范正文已迁 dotnet/csharpstandard，逐字断言前须另行核实。
- [ ] "C# 自 1.0 允许局部变量声明出现在块内任意语句位置"为 ECMA-334 标准属性，本库无逐字原文；本附录以 `spec\variables.md` 的 §9.2.8 / §9.4.4.5 链接为准（正文在 dotnet/csharpstandard），如需逐字引用须查标准仓库。
