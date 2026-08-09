# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周这份建议来自 Anthony 第 18 章"实验性想法"（`inactive/`），只有三段话：独占上界 `To <`。它直接落在我们上周刚处理过的范围表达式决议之上——当时我们把 `1 To 10` 的 inclusive 语义钉死，今天这份建议却要用 `To <` 重新打开 exclusive 的门。所以本次会议的核心不是"这个语法好不好"，而是：**这份实验性想法在范围表达式决议之后，是否值得激活，还是继续搁置。**

## Agenda

* [Proposal: 独占上界 `To <`（Exclusive For "Upper" Bound）](#proposal-独占上界-to--exclusive-for-upper-bound)

## Proposal: 独占上界 `To <`（Exclusive For "Upper" Bound）

_Related: [vblang #25 – Range `1 To 10 Step 2` Expressions](https://github.com/dotnet/vblang/issues/25)；[vblang #180 – For-loop should use larger type to avoid overflow exception](https://github.com/dotnet/vblang/issues/180)；[vblang #48 – Multiple `For` Control Variables](https://github.com/dotnet/vblang/issues/48)；ModVB：`proposal-range-expressions.md`（inclusive `To` 已钉死）、`proposal-for-enhancements.md`、Anthony 原文 §18.4 "Exclusive `For` 'upper' bound"_

### 场景与缺口

We started from Anthony 的原话，它只有一句动机：

> "I'm a little tired of `arr.Length - 1`. It didn't bother me in QB/VB6 because of the `UBound` function (which only works with arrays). On the other hand, some APIs take exclusive ranges so this `x To < y` syntax might be generally useful."

这句话里有**两个**不同的缺口，We think 必须拆开看：

**缺口 A —— 上界减一的仪式。** `For i = 0 To arr.Length - 1` 的 `- 1` 是真实存在的样板。这不是我们的臆测：2016-05-06 主线 LDM 在元组示例里就写了 `For i = 1 To numbers.Length - 1`（"This feels like idiomatic VB."）——这个模式在真实代码里四处都是。Anthony 说得对，QB/VB6 时代有 `UBound` 顶着，.NET 时代只剩 `Length - 1`。

**缺口 B —— off-by-one 错误类别。** 这才是 We think 更严重的缺口。2018-02-28 主线在讨论 C# Range 时亲口承认：

> "We believe that there is non-trivial amounts of VB code that are imprecise about the length of arrays - Dimming with the length instead of 1 minus the length."

```vb
Dim numbers As Integer() = {1, 2, 3}

' 常见 bug：忘减一，最后一次访问越界。
For i = 0 To numbers.Length
    Console.WriteLine(numbers(i))   ' i = 3 时 IndexOutOfRangeException
Next
```

`To <` 的隐含价值恰恰在这里：它把"严格小于 `arr.Length`"的**正确心智模型**写进语法，让错误的 `To arr.Length` 一眼就显得可疑。**但建议原文没有说出这层价值**——它只说了"厌倦了 `- 1`"。

这两层缺口的大小完全不同。缺口 A 是美观问题，今天已有一个零语法的现成答案（见下）；缺口 B 是正确性问题，但它能不能靠新语法解决，We 持怀疑态度——给容易犯错的人群**多一种**上界写法，未必减少错误。这是本场反复回访的点。

### 候选方案

**PROPOSAL A — 建议原文（`To <` 全面铺开）。** `To <` 同时用于 `For` 头上界与通用范围表达式：

```vb
For i = 0 To < arr.Length
    Console.WriteLine(arr(i))
Next

' 范围表达式形式（建议第二个示例）
Console.WriteLine(String.Join(", ", 0 To < 10))
```

**PROPOSAL B — 仅限 `For` 头上界。** 独占写法只在 `For` 循环头的上界位置开放；范围表达式维持 inclusive（与我们上周的决议一致）。

**PROPOSAL C — 不造语法（更简替代优先）。** 复兴 `UBound` 作为数组遍历惯用法；必要时用 analyzer 抓 `To arr.Length` 这类 off-by-one。零语言成本。

**PROPOSAL D — 范围整体半开。** 让 `1 To 10` 直接表示 `[1, 10)`，不再需要 `<`。**本场不重新讨论**——姊妹会议已否决：`For` 头从 VB6 起就是 inclusive，2018-02-28 主线原话 "The To as a Range separator would be weird if the range was exclusive as anticipated."，且范围表达式会议已把 inclusive 钉死。D 是破坏性的，不成立。

**PROPOSAL E — 显式关键词替代 `<`。** 不用 `<` 这个细微字符，改用显式连接词，如 `For i = 0 Until arr.Length`（读作"直到 `i` 到达 `arr.Length`"，即 `i < arr.Length`），或 `To ... Exclusive` 后缀。

### 权衡：Q&A

- **A vs B：范围表达式形式能不能要？** 不能，作为 v1 不能。`String.Join(", ", 0 To < 10)` 要求范围表达式是**一般表达式**（可传参、可求值），而范围表达式会议已把一般表达式形式列为 PROPOSAL A、明确 Table——v1 只做 `For Each`/`From` 迭代源。即使范围表达式复活，我们刚把 inclusive 钉死，立即在同一字面量里再开 exclusive，等于一次引入两种上界语义。**结论：A 的"范围表达式"半边被姊妹决议封死，只剩 `For` 头半边可谈。**
- **B vs C：`UBound` 够不够？** `UBound` 在 VB.NET 的 `Microsoft.VisualBasic` 运行时里**一直存在**（`Information.UBound(arr)`，返回上界），它比 `arr.Length - 1` 和 `To < arr.Length` 都短，且零新语法。它的缺点建议原文自己也说了：只对数组有效。但反过来说——`For` 头独占上界的使用场景**恰恰几乎全是数组**（`To < arr.Length`）。当特性真正高频的场景与现有函数完全重合时，"造语法"的论证就变弱了。`Probably`：B 的真实价值是"通用化"（非数组标量上界也能用），但这份建议没有给出任何一个非数组的真实用例。
- **"一般有用"的论断成立吗？** `Suspect`：不成立。哪些 .NET API 接受"独占范围**值**"？`System.Range`/`Slice`——2018-02-28 主线已裁决走 API-only（"If the API support is good enough, don't do the language work in VB."）。`Random.Next(min, max)` 接受独占**上界**，但收的是两个标量，不是范围字面量——没有 `Random.Next(0 To < 10)` 这种 API 等着我们去填。So "some APIs take exclusive ranges" 在 .NET 生态里没有形成值得造语法的普遍类别。
- **`To <` 是不是正好回应了 2018 年主线对 `To` 的顾虑？** 有意思。2018-02-28 弃 `To` 是因为"range separator **如果**是 exclusive 会很怪"。Anthony 的 `To <` 把 exclusivity 变成**显式**——这是对那声顾虑的部分回应：`To` 不怪了，因为 `<` 明说了。但范围表达式会议已决定用 inclusive `To` + 迭代源形式落地，`To <` 与它是两种并存的区间模型，必须给出取舍而非共存理由。这个论证在建议原文里缺失。
- **正确性护栏 vs 第三种写法。** 缺口 B 的 off-by-one 是真实的（主线自己承认）。但给 bug 高发人群第三种选择（`To arr.Length` 错、`To arr.Length - 1` 对、`To < arr.Length` 对），是减少还是增加混乱？We 认为**没有证据**证明新增语法会减少 off-by-one；而 analyzer（"你是想 `To arr.Length - 1` 吗？"）能以零语言成本抓住同样的错误类别。这一点让缺口 B 对语法的论证大幅削弱。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`To <` 是这份建议最硬的技术伤。VB 没有一元 `<`；`<` 是二元比较运算符，且是 XML 字面量的起界符。当解析器在 `For` 头上界位置读到 `To` 后紧跟 `<`，它必须决定这是新前缀标记还是"缺失左操作数"。

```vb
' 今天就能解析：上界是布尔表达式 (x < 10) —— 对整数循环是类型错误，但语法上合法。
For i = 0 To x < 10

' 建议的新写法：< 作前缀 —— 语义是 [0, x)。
For i = 0 To < x

' 若 < 后是负数或括号，前缀位置依然要成立：
For i = 0 To < -1          ' [0, -1)，空范围
For i = 0 To <(10)         ' 括号内表达式，仍是 [0, 10)
```

关键矛盾：`To` 后的上界**本来就是完整表达式**，`x < 10` 的 `<` 是表达式的一部分。要让 `To < x` 工作，语言必须保留"`To` 后紧随 `<`"为独占标记。这样 `For i = 0 To x < 10`（布尔上界，虽然类型错）与 `For i = 0 To < x`（独占）就被**位置**区分——一个字符之差，两种语义。`Suspect`：还有 XML 字面量隐患——`<` 在表达式位置可能触发 XML 解析尝试（`< arr.Length` 因 `<` 后有空格不构成合法 XML 起始，但这是脆弱的上下文判定）。建议原文的 Drawbacks 承认了解析问题，但停在"需要特别注意"，没有任何文法。

#### 2. 角案例与边界语义

**Step 组合。** 建议完全没提 Step：

```vb
For i = 0 To < 10 Step 2     ' i = 0, 2, 4, 6, 8
For i = 10 To < 0 Step -1    ' i = 10, 9, ..., 1（不包含 0）
```

负步长时独占比较的方向要翻转（`i > x` 而非 `i < x`）。`For` 头现有的 inclusive 终止规则是 `i <= end`（负步长 `i >= end`），exclusive 必须改比较符，等于给 `For` 循环引入一种**新的循环形状**。

**降级不能是 `To x - 1`。** 这是最深的实现坑。直觉上 `To < x` 可降级为 `To x - 1`，但：

```vb
Dim x As Integer = Integer.MinValue
For i = 0 To < x
    ' 若降级为 "To x - 1"：x - 1 溢出为 Integer.MaxValue → 死循环。
    ' 正确降级是 "while i < x" 的比较终止，且 x 只求值一次（与 For 头一致）。
Next
```

`x - 1` 在 `x = Integer.MinValue` 时溢出。所以独占上界必须降级为**比较终止**（缓存 `x` 到临时变量，`i < x` 判出口），这与 #180（For-loop should use larger type to avoid overflow exception）共用同一段整数运算修复面。**这使"一行小语法"的实现成本不再是零。** 空范围没问题：`Dim empty As Integer() = {}` 后 `For i = 0 To < empty.Length` 零次迭代，正确。

#### 3. 作用域与绑定

语义模型层面：`To < arr.Length` 中的 `<` 不绑定任何符号——它是语法标记，`arr.Length` 仍是普通的属性访问。`For` 头本就求值上界一次；exclusive 降级为"缓存 + 比较终止"后，`arr.Length` 的求值次数必须保持一次（当前 `For` 语义），不能退化为每轮求值。没有新的符号、没有新的绑定规则，但 `For` 语句的**降级 IR** 需要新形状。

#### 4. 与既有特性的交互

- **范围表达式（姊妹决议）**：inclusive `To` 已钉死，`To <` 与它并存 = 同一连接词两种语义。若 `To <` 只进 `For` 头，范围表达式不受影响；若进范围表达式，必须同时复活被 Table 的一般表达式形式。**这是本场最关键的交互，建议原文未提。**
- **`Case 1 To 10` 范围模式 / 数组切片 `a(1 To 3)` / 数组边界 `Dim a(1 To 10)`**：三处既有 `To`。若 `To <` 只进 `For` 头，不触碰它们；若进表达式，文法优先级又要重排（范围模式 > 数组切片 > 范围表达式）。We 的立场与范围表达式会议一致：**别碰。**
- **`For` 增强（多计数器）**：`For z = 0 To maxDepth, y = 0 To maxHeight, x = 0 To maxWidth` 中每个计数器上界是否都允许 `To <`？文法上要逐项开放。**Probably**：如果 B 落地，多计数器每个上界都支持独占——但这把特性互相缠住，先做哪个要排队。
- **Lambda 按迭代捕获修复（BC42324）**：`For i = 0 To < arr.Length` 中 `i` 的按迭代捕获与上界语义正交，无交互，`Probably` 无影响。
- **晚期绑定 / Option Strict Off**：`For i = 0 To < obj.SomeLateBound` 在宽松模式下上界可晚期绑定——`<` 标记不受影响，但比较终止的缓存临时变量类型需按宽松规则推断。非分叉，但要写进 spec。

#### 5. Breaking change 与兼容性

好消息：`To <` 是全新语法，今天没有任何合法程序包含 `To <`（VB 无一元 `<`），**零破坏**——这是 B 形式（仅 `For` 头）的最大卖点，与范围表达式会议的 B 同构。坏消息在语义漂移：一旦上线，同一 `For` 头里存在 inclusive/exclusive 两种上界，**写错一个 `<` 就少一次迭代**——这是"新代码层面的隐性破坏"，无法靠兼容性分析对冲。

#### 6. Option Strict / 编译选项分叉

无实质分叉：`For i = 0 To < arr.Length` 在 Strict On/Off 下行为一致（整数上界，无晚期绑定）；若上界晚期绑定，`Probably` 两条路径保持现有宽松规则即可。不需要为独占形式单独定义编译选项行为。

#### 7. IDE / IntelliSense

`<` 作前缀的着色需要新规则；`For i = 0 To <` 处补全应提示"独占上界标记"；当 `<` 后不是表达式时的错误文案（"应为表达式"）需要区别于"缺失左操作数"。这些都不大，但**不做进原型等于没设计**——当前无原型。

#### 8. 数据 / 普遍性

- **数组遍历**：高频，但已被 `UBound`/`For Each`/`Length - 1` 三重覆盖。这是可读性改进，不是能力缺口。
- **off-by-one 错误类别**：真实（主线 2018 自认），但没有量化——没有数据显示业务代码里 `To arr.Length` 的误用率高到值得为它造语法，而非写 analyzer。
- **"接受独占范围的 API"**：`Suspect`，不构成类别。`Slice` 走 API-only，`Random.Next` 收标量。
- 主线 2018 的判断依然有分量："There is a high probability that there are more important features for VB." VBScript.NET 的目标代码库里，`For i = 0 To arr.Length - 1` 确实常见，但**减少一次 `- 1` 的敲键**离"值得造语法"很远。

#### 9. 更简替代

- **`UBound(arr)`**：已存在、更短、零语法、覆盖数组主场景。这是 C 方案的核心，也是我们最有力的竞争者。
- **analyzer**：抓 `For i = 0 To arr.Length`（忘减一）并建议修复——零语言成本捕获缺口 B 的同一错误类别。`Probably` 这是 off-by-one 问题的最优解。
- **`For Each` / LINQ**：不需要下标时直接用 `For Each x In arr`，根本不需要上界。
- **PROPOSAL E（`Until`/`To ... Exclusive`）**：若未来坚持要语法，显式关键词比 `<` 更符合"读起来像英语"与"冗长只在有用时是美德"（原则 #10）。`For i = 0 Until arr.Length` 把语义变化做成**可见的**，而不是一个字符。但 `Until` 只解决 `For` 头，不解决范围表达式；且它仍是"第二种上界写法"。

#### 10. 成本 / 优先级

文法小，但降级形状不简单：比较终止 + 临时缓存 + 负步长翻转 + 与 #180 共用溢出修复面，且必须与范围表达式、`For` 增强两个工作项对表。优先级**低**——排在范围表达式 v1、#180 溢出修复、管道运算符之后。若只做 B 且接受"与 `UBound` 重叠"的论证，成本可降到最小；即便如此，价值也是"敲键减少"而非"能力增加"。

#### 11. 运行时 / CLR 硬约束

无 CLR 约束：纯编译期降级（比较终止循环是既有 IL）。表达式树是唯一交互：`0 To < 10` 若出现在表达式树 lambda 中，只能降级为 `Enumerable.Range(0, 10)` 调用（`For` 形式不是表达式，天然出不了表达式树）。`Probably`：独占范围表达式与 inclusive 一样，表达式树内仅允许 `Enumerable.Range` 降级形态。

#### 12. 值不值得做

- **价值**：缺口 A 是美观（`- 1` 敲键减少，且 `UBound` 已覆盖主场景）；缺口 B 的护栏价值真实但可用 analyzer 零成本获取。**低。**
- **成本**：文法小 + 降级非平凡（比较终止、负步长、溢出、三工作项对表）。**中低。**
- **风险**：`To <` 与 inclusive `To` 并存 = 一字符语义翻转（原则 #7 的典型形态）；`<` 前缀与比较/XML 字面量边界脆弱。**中。**
- **结论**：**不值得现在做。** 若未来数据证明 off-by-one 高发且 analyzer 不够，B 形态（仅 `For` 头）可作为候选重新审视；A 形态（含范围表达式）被姊妹决议封死，不做。**若只做 A 而不收敛到 B，我们会直接建议不做。**

### VB 基因对照

- **继承 VB6/`For` 的 `To` 基因**："from the `For` loop down"——这是本建议唯一的历史血统。`To <` 是从循环头向下长的，与 C# 从 `Span<T>` 向上长相反。这一面是纯正的 VB。
- **消除常见样板（原则 #9）**：命中，但**被 `UBound` 截胡**——样板的主场景已有更短的现成答案。
- **不引入"第二种做事方式"（原则 #3）**：**偏离**。`For` 头从此有 inclusive/exclusive 两种上界，靠一个 `<` 区分。
- **避免隐蔽语义变化（原则 #7）**：**直接违背**。`0 To 10`（11 次迭代）与 `0 To < 10`（10 次迭代）只差一个字符——这正是 `Return?` 被拒的同类理由。
- **读起来像英语（原则 #5）**：`To < arr.Length` 读作 "to less-than arr.Length"，语义直觉反而好——但这是它唯一全分的维度。
- **与主线关系（对照表 2.3）**：主线**从未**提出 `To <`（Anthony 独立延伸）；且它**顶撞**主线 2018 的 exclusive 顾虑（"The To as a Range separator would be weird if the range was exclusive"——虽然显式 `<` 部分回应了它），并与 ModVB 刚钉死的 inclusive `To` **直接冲突**。

### RESOLUTION:

1. **整体：Table（维持 inactive）**。概念真实（off-by-one 是真实错误类别、`To <` 读起来像英语），但建议原文只有动机、没有设计：无文法、无 Step、无降级语义、无与范围表达式的交互，且两个示例指向两个互相冲突的范围。
2. **若未来复活，限定 PROPOSAL B（仅 `For` 头上界）**。范围表达式形式被姊妹决议封死（inclusive 已钉死、一般表达式已 Table），不作为复活路径。
3. **采纳"更简替代先行"**：`UBound(arr)` 已是主场景的零语法答案；off-by-one 错误类别由 analyzer（抓 `For i = 0 To arr.Length` 并建议修复）以零语言成本承担。
4. **明确拒绝 `<` 作范围分隔符的推广**（A 的范围表达式半边），与 inclusive `To` 钉死保持一致；不引入 `..`，不做 `System.Range` 隐式转换（姊妹决议第 4 条）。
5. **降级纪律**：若未来实现 B，独占上界**不得**降级为 `To x - 1`（`Integer.MinValue` 溢出），必须为"缓存上界 + 比较终止"，负步长翻转比较方向，并与 #180 共用溢出修复面。
6. **语法候选**：若未来要语法而非 analyzer，PROPOSAL E（显式 `Until`/`To ... Exclusive`）比 `<` 更符合 VB 基因（可见的语义变化）；`<` 前缀标记列为不推荐。

### Implication:

- 通知 Anthony 与范围表达式作者：inactive 保留；激活信号见下。
- 若激活：补 speclet，覆盖 `To <` 文法（含 `<(x)`、负字面量、XML 字面量消歧）、`Step` 组合、比较终止降级、与 `For` 增强多计数器的逐项开放。
- 与范围表达式 / `For` 增强团队对表：四向文法优先级（`For` 头独占 > `Case` 范围模式 > 数组切片 > 范围表达式）与降级共享面。
- 收集数据：`For i = 0 To arr.Length`（忘减一）与 `Length - 1` 在 VBScript.NET 目标代码库的占比，决定缺口 B 是否真值得语法而非 analyzer。
- 原型（若激活）：仅 `For` 头 + 整数族 + 比较终止降级，验证语义模型与 IDE。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：独占上界与 `Case 1 To 10` 范围模式是否需要互动（`Case 1 To < 10` 作独占范围模式）？本场倾向"不碰"，但未定。
- `OPEN QUESTIONS`：若 PROPOSAL E 的 `Until` 形式被采纳，`For i = 0 Until arr.Length` 与 `Do Until`、LINQ `From ... Until` 的关键词负载是否可接受？未评估。
- `TODO`：量化 `To arr.Length` off-by-one 误用率（缺口 B 的证据）。
- `TODO`：盘点 VBScript.NET 目标代码库里接受"独占上界"的非数组 API，验证或推翻"一般有用"论断。
- `Follow-up`：analyzer 原型（抓 `For i = 0 To arr.Length`）先行，验证能否以零语言成本消除缺口 B。

### 状态

- **LDM 状态：inactive（维持搁置）**；若复活，仅限 `For` 头上界（PROPOSAL B），范围表达式半边为 Table。
- **三态判定：Table**——动机真实但价值低（`UBound` 已覆盖主场景）、成本非平凡（降级形状）、风险明确（`<` 一字符翻转 + 与 inclusive `To` 冲突），且建议原文无设计只有动机。激活所需信号：① 范围表达式 v1 落地且证明与独占上界能共享降级面；② off-by-one 误用率数据证明 analyzer 不够；③ 有人补出 B 形态的完整 speclet（文法 + 降级 + Step）。

---

## 附录：特性评价

# 建议评价报告：proposal-exclusive-for-upper-bound.md

## 评价对象

- 建议：proposal-exclusive-for-upper-bound.md — 独占上界 `To <`（`[x, y)`）
- 来源：Anthony 原文第 18.4 章 "Exclusive `For` 'upper' bound"（`..\..\AnthonyDesign_wordpress.txt` L2845–2857；`For i = 0 To < arr.Length` 与 `Console.WriteLine(String.Join(", ", 0 To < 10)` 逐字出自该章，且原章与建议转录均缺右括号）
- 配方目标：以 `x To < y` 表示左闭右开区间，消除 `arr.Length - 1` 减一写法，并对接受独占范围的 API"一般有用"
- 状态定位：`inactive/` 实验性想法（Anthony 自标"还需要时间酝酿"），非正式草案

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。主效果（`For i = 0 To < arr.Length` 免写 `- 1`）可演示；关键子效果"对独占范围 API 一般有用"无任何真实用例，且 `String.Join(", ", 0 To < 10)` 依赖被姊妹决议 Table 的一般表达式范围 | 已检查 | 无原型；"一般有用"`Suspect`；无 off-by-one 数据支撑缺口 B；示例缺右括号不可编译 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。继承 VB6/`For` 的 `To` 基因（读起来像英语全分）；但打包了"上界减一"与"独占范围通用化"两个弱相关能力，且违背原则 #7（一字符语义翻转）与 #3（第二种上界写法） | 已检查 | `<` 作前缀与比较/XML 字面量边界脆弱；与 inclusive `To` 并存 = 两区间模型 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。三段话忠实转录、自标实验性、3 个未决问题诚实（1–3 健康区间）；但无 Detailed design/文法/Step/交互分析，状态行为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`），示例不可编译，且与姊妹范围决议的冲突完全未覆盖 | 已检查 | Drawbacks/Alternatives/Unresolved 三节是模板骨架；"解析歧义"停在"需要特别注意"无文法 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。雷/光轻度正向（敲键减少、读起来像英语）；风（演化一致性）**受损无应对**——顶撞主线 2018 exclusive 顾虑、与 ModVB inclusive `To` 决议直接冲突；暗（一字符 off-by-one 漂移、`<` 前缀脆弱）无对冲设计 | 已检查（预测待定） | 与范围表达式工作项的关系未识别；冲突风险文档未权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料来源（§18.4）标注准确、继承 VB6 `For`/`UBound` 血统明确；但未声明与主线 2018 exclusive 顾虑（"The To as a Range separator would be weird if the range was exclusive"）的顶撞关系，未声明与 C# `Range`/Swift `..<` 半开语义的亲和（`0 To < 10` 与 `System.Range(0,10)` 逐位同构），`System.Range` 互操作未分析 | 已检查 | 半开区间在 .NET 的既有语义（`Slice`/`Random.Next`）未盘点；"一般有用"无材料支撑 |

## 设计原则对照

- **与 VB 基因：部分一致**——继承 `For` 的 `To`（"from the `For` loop down"）、读起来像英语（#5）、零破坏（全新语法）；**偏离**于原则 #7（`<` 细微字符改语义——`Return?` 被拒的同类理由）、#3（inclusive/exclusive 第二种上界写法）、#8（`<` 与比较运算符/XML 字面量冲突风险）；原则 #9（消除样板）被 `UBound` 截胡。
- **与主线关系：Anthony 独立延伸，且与主线/ModVB 既有决议冲突**——主线从未提出 `To <`（对照表 2.3 无此条目）；直接顶撞主线 2018-02-28 的 exclusive 顾虑（"The To as a Range separator would be weird if the range was exclusive"——显式 `<` 部分回应但未处理）；与 ModVB 范围表达式"inclusive 钉死"决议**直接冲突**；与 `For` 增强（#48/#180）在降级面共享上有交互。
- **破坏性变更：无（编译层面）**——`To <` 是全新语法，无既有合法代码包含 `To <`；**有（语义漂移层面）**——上线后 `0 To 10` vs `0 To < 10` 一字符差一次迭代，off-by-one 由"忘减一"转为"忘写 `<`"。

## 总评

- **达成程度：未达成 / 部分达成**——动机真实（off-by-one 错误类别、减一仪式），但建议只有动机没有设计：无文法、无 Step、无降级、无与范围表达式的交互，且两个示例分别依赖被 Table 的一般表达式范围。主效果（`For` 头减一）可演示，但被 `UBound` 与 analyzer 以零语言成本覆盖。
- **LDM 三态建议：Table（维持 inactive）**——概念值得记录、不值得激活。若复活，仅限 PROPOSAL B（`For` 头上界）+ 显式关键词替代（PROPOSAL E），并满足激活信号（范围表达式 v1 落地、off-by-one 数据、B 形态 speclet）。
- **主要问题**：① "一般有用"论断 `Suspect`，无真实非数组用例；② 降级若为 `To x - 1` 则 `Integer.MinValue` 溢出，必须比较终止——成本非平凡；③ `To <` 与 inclusive `To` 并存 = 一字符 off-by-one 漂移（原则 #7）；④ 与范围表达式"inclusive 钉死"决议冲突未识别；⑤ 文档无设计仅动机。

## 返工建议

- **补充章节**：Detailed design（`For` 头文法 BNF 或降级规则）、Step 组合、溢出安全降级（比较终止、对齐 #180）、Compatibility/breaking-change（含 off-by-one 漂移分析）、与范围表达式 / `Case` 范围模式 / 数组切片的文法优先级表。
- **补充证据**：`UBound` 与 analyzer 两条更简替代的对照实测；off-by-one 误用率数据；非数组"独占上界 API"清单（验证或推翻"一般有用"）；最小原型（仅 `For` 头 + 整数族 + 比较终止降级）。
- **未决问题处理**：解析歧义须给文法答案而非"特别注意"；独占上界是否进 `Case` 范围模式（倾向不进）；`For` 增强多计数器是否逐项开放（倾向不捆绑）。
- **设计探索**：PROPOSAL E（`For i = 0 Until arr.Length` 显式关键词）与 `<` 前缀的可读性对照测试；`0 To < 10` 与 `System.Range(0, 10)` 半开同构的互操作设计（若一般表达式范围未来复活）；analyzer 抓 `To arr.Length` 的原型。

---

## 附录：C# 生态与互操作考量

> 交叉核对对象：ModVB C# interop 索引（`..\..\..\csharplang-index.md`，来源镜像 `..\..\..\csharplang`）。C# 引文逐字摘自镜像，附 `→` 来源路径；无法核实的标 **Suspect** / **OPEN QUESTIONS**。本附录只追加，不改写正文与既有 RESOLUTION。

### 相关 C# 现实方向

本提案（独占上界 `To <`）落在 C# 生态的三条线索上。三者共享同一个痛点——"区间/循环上界的减一仪式"——但 C# 的选择与本提案**不同位**：排上是共识，**语法不是**。

**(a) C# `for`：排他是惯用法，不是语法。** C# `for` 由 `init; condition; iterator` 组成，**没有上界标记**；数组遍历的标准形态 `for (int i = 0; i < arr.Length; i++)` 让实际最后下标天然是 `Length - 1`。C# 从未为 `for` 引入"排他上界"语法——`condition` 本是一般布尔表达式，`i < n` 已经是排他表达。方向与本提案同向，但实现路径是**惯用法**，不是新标记（本节无 csharplang 原文直接背书，见 OPEN QUESTIONS）。

**(b) C# 8 `Range`/`..`：排他上界的**显式语言裁决**，且位点在"索引/切片"。** 这是 C# 最接近本提案的一次排他决策，LDM-2018-01-22 完整走了一遍利弊：

> "Not many languages have *only* inclusive ranges. Apart from F#, they tend to either have exclusive ranges or have notations for both (at the upper end; the lower is always inclusive)." → `meetings\2018\LDM-2018-01-22.md`

> "The obvious advantage of course is that the collection's `Length` is directly allowed at the end:" → `meetings\2018\LDM-2018-01-22.md`（例：`var s = a[0..a.Length];`）

> "In fact, in F# it is often a bit of a pain to write a `for` loop for array iteration, for instance, because you need to subtract one at the end to avoid overrunning." → `meetings\2018\LDM-2018-01-22.md`

第三句几乎是本提案"缺口 A"（`arr.Length - 1` 样板）的逐字镜像——F# 与 VB 在同一点上痛过。但 LDM 的结论把位点锁死在索引/切片，而不是循环上界：

> "Let us go with `..` means exclusive. Since we've chosen to focus on the indexing/slicing scenario, this seems the right thing to do:
> * It allows `a.Length` as an endpoint without adding/subtracting 1.
> * It lets the end of one range be the beginning of the next without overlap
> * It avoids ugly empty ranges of the form `x..x-1`" → `meetings\2018\LDM-2018-01-22.md`

后续 LDM 反复确认 `Range` 端点排他，并曾浮想过"含上界循环语法"但未采纳：

> "Arguably, then, all of this bending-over-backwards is a consequence of wanting to express "zero from end" as the upper bound of an exclusive range." → `meetings\2018\LDM-2018-02-14.md`

> "In ranges, the only way to express "to the end" is then to omit the end point (since they're also exclusive)." → `meetings\2018\LDM-2018-02-26.md`（原文自注 raw notes）

> "One idea is that when we start talking about scenarios outside of indexing, we simply have a different syntax; maybe one that is built in to language constructs for containment and iteration:" → `meetings\2018\LDM-2018-01-22.md`（例：`foreach (var x in 0 to 100) { ... }`，未进语言）

实现落点（`proposals\csharp-8.0\ranges.md`）：`System.Index`/`System.Range` 是 BCL 类型，`..`/`^` 只是构造语法糖；`^x` 由编译器降级为 `receiver.Length - x`——C# 把减一仪式**收编进编译器**，而不是造循环上界标记：

> "This feature is about delivering two new operators that allow constructing `System.Index` and `System.Range` objects, and using them to index/slice collections at runtime." → `proposals\csharp-8.0\ranges.md`

> "When the argument is of the form `^expr2` and the type of `expr2` is `int`, it will be translated to `receiver.Length - expr2`." → `proposals\csharp-8.0\ranges.md`

> "C# has no syntactic way to access "ranges" or "slices" of collections. Usually users are forced to implement complex structures to filter/operate on slices of memory, or resort to LINQ methods like `list.Skip(5).Take(2)`. With the addition of `System.Span<T>` and other similar types, it becomes more important to have this kind of operation supported on a deeper level in the language/runtime, and have the interface unified." → `proposals\csharp-8.0\ranges.md`

**(c) 数组长度语义跨语言分叉：`Length`（数量）vs `UBound`（含上界）。** C# 侧数组 `Length` = 元素**数量**，排他上界 = `Length - 1`；VB 侧 `UBound(arr)` = **含**上界，且分配约定不同：VB `Dim a(10)` 是 11 个元素（索引 0..10），C# `new int[10]` 是 10 个元素。VBScript.NET 继承 VB 约定。这条分叉把"缺口 B"（off-by-one 错误类别）在互操作边界放大为真实 bug 源：调用 C# API 时把"数量"当"上界"（或反之）。

### 现实 vs 提案

| C# 现实方向 | 与本提案关系 | 判定 |
|---|---|---|
| `for` 排他上界是惯用法（`i < n`），无上界语法 | 排他主张同向；C# 的答案不是语法而是惯用法 | **兼容** |
| `..` 半开 `[start, end)`，`Length` 可直接作端点 | 与"缺口 A"同源；C# 在索引/切片位点解决，非循环位点 | **兼容**（部分对齐，位点不同） |
| `foreach` 迭代集合无需上界 | 提案主场景（数组遍历）在 C# 无痛点 | **兼容**（减一仅剩"需下标"循环） |
| `Length`=数量 vs `UBound`=含上界；`Dim a(10)` vs `new int[10]` | 跨语言 off-by-one 阻抗（缺口 B 的边界放大） | **需桥接** |
| `System.Range`/`System.Index` 为 BCL 类型 | VBScript.NET 消费 C# 索引/切片 API 需识别类型与降级成员 | **需桥接**（元数据/类型识别） |
| 动态/晚期绑定边缘化（索引 T7） | 与 `<` 标记正交，宽松模式上界仍可晚期绑定 | **脱节弱**（如实说明，基本无关） |

理由要点：

- **兼容**：若复活 B 形态，其"缓存上界 + 比较终止"降级产出的 IL 与 C# `for (int i = 0; i < n; i++)` 逐段同形——互操作零摩擦（同一段循环 IL，元数据无差别）。C# 生态先例（`for` 惯用法 + `..` 排他）给提案的"排他是共识"背书。
- **位点差异是关键**：C# 面对同一减一痛点时**没有**造"循环排他上界语法"——它用 `foreach`（无上界）、`..`（切片端点）、`i < n`（惯用法）三条路覆盖，与 RESOLUTION 的 `UBound`/analyzer/`For Each`"更简替代"结构同构。
- **需桥接是类型层，不是语法层**：真正的互操作功夫在 `Length`↔`UBound` 换算与 `System.Range`/`System.Index` 消费，与 `To <` 是否激活无关。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：若 `To <` 复活，它是纯编译期降级（缓存上界 + 比较终止），无运行时依赖、无反射，天然兼容 AOT/trimming 与 C# `for` 的 IL 形状；宽松模式下上界仍可晚期绑定（动态仅限上界求值一次），不新增动态操作。
2. **source-gen 桥（跨语言 Range）**：VBScript.NET 向 C# API 传范围/切片时，在边界生成 `System.Range`/`System.Index` 构造（或降级 `Skip(start).Take(count)`）；若未来给 VB 提供 `..`/`^`，直接映射 ranges.md 降级规则（`^x` → `receiver.Length - x`）。
3. **识别新元数据**：编译器需识别 `System.Range`/`System.Index`、`int→Index` 隐式转换、`Index.GetOffset`/`RuntimeHelpers.GetSubArray`，才能消费 `array[^1]`、`str[1..^2]` 类 C# API——属"识别 BCL 新类型"范畴，与本提案无语法依赖。
4. **跨语言数组分配约定**：文档/analyzer 应警示 `Dim a(n)` = n+1 元素（与 C# `new int[n]` 差一）；`UBound(arr)` 仍是对 C# 数组的最短排他上界（= `arr.Length - 1`）。
5. **生态背景（非行动项）**：C# 动态/晚期绑定被 AOT 边缘化（索引 T7）；unsafe-evolution 明言 VB 无指针、无需 requires-unsafe——「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节）。本提案不落这两条线。

### 对既有 RESOLUTION / 三态判定的影响

**三态判定维持 Table（inactive）。** C# 生态核对**补强而非推翻**本场决议：

1. **方向同向不构成价值**：C# 的排他裁决确认"排他上界"是生态共识，给缺口 A 背书；但 C# 解决减一仪式时**没有**造循环排他上界语法，生态先例反而支持"不造语法"。
2. **IL 一致性削弱语法必要性**：B 形态降级产出与 C# `for` 同形的 IL，跨语言调用已在 IL/元数据层无缝——`To <` 的剩余价值只剩源层面敲键，正是本场"低价值"判定。
3. **桥接点在类型层而非语法层**：互操作边界的功夫在 `Length`↔`UBound` 与 `Range`/`Index` 消费，与 `To <` 激活无关；RESOLUTION 第 3/5/6 条在 C# 生态下依旧成立，无需修改。

若未来复活，B 形态降级设计的跨语言一致性（与 C# `for` 天然对标）可作为激活信号③（speclet）的一个**支持项**。

### OPEN QUESTIONS / Suspect

- `OPEN QUESTIONS`：`System.Range`/`System.Index` 在 VBScript.NET 目标代码库的实际使用占比未量化——"需识别 Range 元数据"是方向性建议，缺数据。
- `Suspect`：LDM-2018-01-22 浮想的 `foreach (var x in 0 to 100)` 含上界语法，后续是否有正式跟进/否决——本索引未深挖更早 vblang/csharplang 记录，仅确认其**未进语言**。
- `OPEN QUESTIONS`：C# `for` 排他上界"从未被 LDM 明确表述为设计决策"——本附录将其作为惯用法陈述（事实成立）但无 csharplang 原文直接背书；如需逐字出处，须查 dotnet/csharpstandard 的 `for` 语句规范（本库 `spec\statements.md` 仅链接，无正文）。
