# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周议程来自 LINQ 增强这一组：范围表达式、管道运算符、查询增强三份建议共享同一片"迭代源"实现面。我们上一次遇到 `1 To 10 Step 2` 是在主线的 2017 年——当时它叫 vblang Proposal #25。今天 Anthony 把它放进 ModVB 的第 9 章 "LINQ Enhancements" 重新端上来，我们先把当年的账对完，再决定它该以什么形态进 VBScript.NET。

## Agenda

* [Proposal: 范围表达式 Range Expressions（`1 To 10 Step 2`）](#proposal-范围表达式-range-expressions)

## Proposal: 范围表达式 Range Expressions

_Related: [vblang #25 – Range `1 To 10 Step 2` Expressions](https://github.com/dotnet/vblang/issues/25)；[vblang #180 – For-loop should use larger type to avoid overflow exception](https://github.com/dotnet/vblang/issues/180)；[vblang #104 – Extend `For Each` Statement with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)；[C# Range 设计（`features/range.md`）](https://github.com/dotnet/roslyn/blob/features/range/docs/features/range.md)；ModVB：`proposal-range-expressions.md`（Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"）_

### 场景与缺口

We started from the observation that the idea is as old as the language's modern form. 2017 年 12 月 6 日主线 LDM 讨论 #25 时我们说过一句至今成立的话：

> "This proposal was envisioned specifically for VB and from the `For` loop down, so to speak. Independently, [the C# Range proposal] was envisioned for C# and designed from `Span<T>`/slices up."

这就是本建议最"VB"的基因：`1 To 10 Step 2` 是从 `For i = 1 To 10 Step 2` 的循环头向下长出来的，读者一眼就懂。今天的缺口也确实存在——生成等差整数序列只能写 `Enumerable.Range(0, 61)`（计数语义，`0` 到 `60` 是 61 个值，off-by-one 极易写错）、`For` 循环（样板多），或手写数组字面量（无法表达大范围与步长）。

```vb
' 今天：count 语义反直觉，60 是含的、61 是数出来的。
Let a = Enumerable.Range(0, 61)

' 想要的：inclusive、读起来像英语。
For Each n In 0 To 60
    Console.WriteLine(n)
Next
```

但我们同时要面对一个令人不安的事实：**主线在 2018 年已经转向**。当 C# Range（`..`，half-open/exclusive）进来时，我们在 2018-02-28 明确说过：

> "The To as a Range separator would be weird if the range was exclusive as anticipated. Not using that. `..` would work."

并且最终的结论是：

> "If the API support is good enough, don't do the language work in VB."

主线没有拒绝 `1 To 10 Step 2` 这个方向本身——#25 的裁决是 `**Speclet needed**`，随后被 2018 年 C# Range 的到来打断了，从未正式否决。Anthony 的建议等于把 2017 年的球捡回来继续踢。所以我们今天要回答的不是"要不要这个语法"这么简单，而是：**这个语法值不值得在主线已经转向之后，作为 VBScript.NET 的独立延伸重新落地**。

还有一个更尖的缺口信号：`Rnd(1 To 100)`。这里想表达的是"区间"语义——在 1 到 100 之间取值——与"序列"语义（`1 To 100` 就是 1..100 这 100 个数）是**两种不同的东西**，却被塞进同一个字面量。We think 这是本建议最需要拆开的地方，后面详述。

### 候选方案

**PROPOSAL A — 完整一等表达式（建议原文）。** `1 To 10 Step 2` 是通用表达式，可赋值、传参、迭代：

```vb
Let odds = 1 To 10 Step 2
Let guess = Rnd(1 To 100)
For Each n In 0 To 60
```

**PROPOSAL B — 仅限迭代源。** 范围表达式只允许出现在 `For Each` 的集合位置与 LINQ `From` 子句，不做一般表达式、不传参：

```vb
For Each n In 0 To 60
Let evens = From n In 0 To 100 Step 2 Select n
```

**PROPOSAL C — 库/API 路线（主线 2018 的实际走向）。** 不造语法。保证 `Index`/`System.Range` 的隐式转换可用、API 手感好，让 `Slice((2, 3))`、`Range.StartAt(2)` 这类库调用直接成立。主线原话："These APIs will allow older versions of Visual Basic (and C#) access these APIs and may come out reading more 'VB like' than the dot dot range syntax."

**PROPOSAL D — 区间参数语义独立化。** `Rnd(1 To 100)` 代表的"区间参数"是另一个需求，应走独立的 `Range(Of T)` 类型与重载（`Rnd(1 To 100)` 编译成传一个区间值），与"序列字面量"彻底分开设计，不共享语法。

### 权衡：Q&A

- **A vs B：一般表达式值不值得做？** 不值得作为 v1。一般形式立刻撞上三个问题：(1) `1 To 10 Step 2` 的静态类型是什么——`IEnumerable(Of Integer)`、一个自定义 `Range(Of T)` 值、还是数组？建议没写；(2) 惰性序列还是立即物化？建议没写；(3) 一但成为表达式，`Case 1 To 10` 范围模式、`arr(1 To 10)` 数组切片、`Dim arr(1 To 10)` 数组边界声明这三处既有 `To` 语法就全是边界冲突（见下文追问 #1）。B 只在 `For Each`/`From` 的"集合位置"开放，那个位置今天不可能出现 `To`（`For Each x In 0 To 60` 今天是语法错误），**零碰撞、零破坏**。v1 取 B，A 的地盘留给 speclet。
- **A vs C：我们是不是该干脆听主线的，什么都不做？** C 有一个无法回避的盲点：它只能表达**切片区间**（`Slice(0..11)`），表达不了"生成一个带步长的等差序列"。C# 的 `Range` 是给索引用的，不是给"1 到 100 每隔 2 取一个"用的。主线的 "don't do the language work" 是建立在"API 足够好"的前提上——对切片成立，对序列生成不成立。所以 C 是我们必须尊重的基线，但它在 VBScript.NET 里解决不了 `For Each n In 0 To 60`。**结论：C 作为退路，不作为答案。**
- **`To` 的 inclusive 语义 vs 主线对 exclusive 的担心。** 2018 年我们弃 `To` 是因为当时预期 C# Range 是 exclusive/half-open，"The To as a Range separator would be weird if the range was exclusive"。但本建议的 `1 To 10` 明确是 inclusive——这与 `For i = 1 To 10` 循环头的语义完全一致（VB 从 VB6 起 `For` 就是 inclusive，这是最深的用户直觉）。**只要我们把 inclusive 钉死，`To` 就不 weird。** 但代价是与 C# `Range` 的 half-open 语义在互操作时必须划清边界：`System.Range` 是 `[start, end)`，`1 To 10` 是 `[1, 10]`。两者不能互相隐式转换，否则又是隐蔽的 off-by-one。
- **`Rnd(1 To 100)` 的示例本身成立吗？** `Suspect`：不能。VB 运行时的 `Rnd` 签名是 `Function Rnd(Optional Number As Single = 1) As Single`，返回 `[0, 1)` 的 `Single`；`1 To 100` 到 `Single` 没有转换，`Rnd(1 To 100)` 在 Option Strict On 下直接编译失败，在 Off 下晚期绑定通过但运行期才炸。更重要的是它演示的根本不是"序列"——它演示的是"区间参数"，是 D 的地盘。**这个示例是概念混杂，不是语法演示。** 建议的 Drawbacks 与 Unresolved questions 均未识别这一点，我们 `Suspect` 原文在此处把两个特性缝在了一起。
- **枚举物化与步长。** 若 v1 只在迭代源位置出现，降级规则其实很简单：`For Each n In a To b [Step s]` 编译为等价的 `For n = a To b Step s` 循环体（或 `Enumerable.Range`）。这恰好与 #180（For-loop should use larger type to avoid overflow exception）共用同一段整数运算代码。`Integer.MaxValue - 1 To Integer.MaxValue` 的溢出语义必须与 `For` 循环头**完全一致**，不能出现"循环能跑、范围表达式炸"的分叉。
- **空范围与负步长。** `0 To 10 Step -1` 是什么意思？建议的未决问题里列了"空范围（start > end 且正步长）"。我们的立场：与 `For` 循环头语义对齐——`For i = 10 To 1` 循环体零次执行，范围表达式同样返回空序列，**不是错误**。负步长允许但语义必须与 `For ... Step -1` 一致。这些都是 `For` 头已经定义好的行为，我们只是在借它。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`To` 与 `Step` 在 VB 里已经有**三种**活跃的既有语法，本建议一上来就要与它们共存：

```vb
' ① Select Case 范围模式：Case 1 To 10 已是合法语法（2018-12-19 模式文法里明确列为
'    | Expression 'To' Expression  // Range pattern）。
Select Case score
    Case 1 To 10
End Select

' ② 数组边界声明：Dim arr(1 To 10) 声明下界 1 的 10 元素数组（VB6 遗产）。
Dim a(1 To 10) As Integer

' ③ 数组切片：a(1 To 3) 返回子数组。
Dim slice = a(1 To 3)
```

若 `1 To 10` 成为一般表达式，`Case 1 To 10` 的解析就变成二义：是"范围模式"（现有，匹配 [1,10] 内的值）还是"表达式然后相等测试"（新）？`Case (1 To 10)` 加括号更暴露问题——括号内是表达式，这会让既有的 `Case 1 To 10` 在加括号后改变含义。**这是本建议最尖锐的文法问题，建议原文完全没有处理。** 我们自己的立场：范围模式与范围表达式共享 `To` 恰恰证明 `To` 在 VB 里是"范围"的天然词，但文法层面必须让范围模式优先级更高，且禁止在 `Case` 内出现"范围表达式值参与相等比较"的歧义解析——`Case 1 To 10` 永远按范围模式解释，直到有人设计出显式的转义。

#### 2. 角案例与边界语义

- **inclusive 端点**：`1 To 10` 含 10。必须钉死，与 `For` 头一致。
- **空范围**：`10 To 1`（正步长）→ 空序列，非错误，与 `For` 头一致。
- **步长为零**：`1 To 10 Step 0` → 与 `For ... Step 0` 一致（死循环/编译期警告？）。`For` 头现在怎么处理，这里就怎么处理。
- **溢出**：`Integer.MaxValue - 1 To Integer.MaxValue`，或 `Int32.MinValue To Int32.MaxValue Step 2`——`last + step` 的中间值溢出。必须复用 #180 的"用更大类型计算"策略，不能各自为政。
- **非整数类型**：建议未决问题问是否支持 `Decimal` / `Double` / `Date`。`For` 头本就支持这些类型，所以语法上无障碍；但 `Double` 步长的浮点累加会产生经典的 0.1+0.2 问题（`0 To 1 Step 0.1` 到底多少次迭代？），`Date` 范围的单位是 `Date` 增量。**v1 只做整数族，`Probably`：浮点与 `Date` 延后。**
- **`For Each n In 0 To 60` 中 n 的类型**：`For Each` 的迭代变量类型推断按既有规则走——若范围表达式的静态类型是 `IEnumerable(Of Integer)`，则 `n` 是 `Integer`；若 Option Infer 需要从集合元素推断，规则照旧。这里没有新概念。

#### 3. 作用域与绑定

范围表达式的语义模型：`For Each n In 0 To 60` 中 `0 To 60` 的 `GetTypeInfo` 应该返回什么？若 v1 只在迭代源位置，答案是 `IEnumerable(Of Integer)`（`Probably`，语义模型层面以"编译器生成的范围迭代器"呈现）。若做一般表达式，就必须定一个真实的静态类型——这又回到 A 的地盘。We think 在 B 的范围里，这个问题的答案是被**自然压扁**的：范围表达式不产生用户可见的运行时值，它只是一个被降级的迭代源。

#### 4. 与既有特性的交互

- **`For` 循环头**：`For n = 1 To 10 Step 2` 与 `For Each n In 1 To 10 Step 2` 并存。前者是既有语句，后者是新迭代源。视觉上几乎一样、语义上一个是计数循环一个是枚举——这会不会违反"不引入第二种做事方式"（原则 #3）？会，但要区分：`For` 头**本来就是**范围语义，`For Each` 范围源只是把同一种直觉延伸到枚举。We think 这不是"第二种方式"，是"同一个词根的两种屈折"——前提是**实现上共用同一条降级路径**，不许出现两套端点/溢出/步长规则。
- **`Case 1 To 10`**：见追问 #1，冲突未解。
- **数组边界/切片 `a(1 To 10)` / `a(1 To 3)`**：若 `1 To 10` 成为一般表达式，数组下标内的 `To` 解析需要保持数组优先。`a(1 To 3)` 继续是切片，不能变成"用范围值做下标"。
- **LINQ `From` 子句**：`From n In 0 To 100 Step 2` 是 B 的一部分，与 #104（Extend `For Each` with Query Comprehensions）共享语法点。ModVB 把它归为"LINQ 增强"是对的——范围表达式的最大价值场景是查询理解与 `For Each`，不是一般赋值。
- **Option Strict Off / 晚期绑定**：`Rnd(1 To 100)` 在 Off 下晚期绑定编译、运行期失败（示例本身坏）。这条路径我们拒绝为它设计——区间参数是 D 的事，不在序列字面量里。
- **表达式树**：范围表达式出现在表达式树 lambda 里怎么办？`From ... Select` 可转表达式树；若 `1 To 10` 降级为 `For` 循环，则**无法**进表达式树（循环不是表达式）。`Probably`：声明 v1 的范围表达式不可出现在表达式树上下文，除非它降级为 `Enumerable.Range` 调用。这是 speclet 必须写清楚的一个交互。

#### 5. Breaking change 与兼容性

好消息：今天**没有任何**合法表达式上下文里会出现裸 `1 To 10`（`To` 不是一般中缀运算符），所以让 `For Each x In 0 To 60` 从语法错误变成合法程序，**不改变任何既有代码的行为**——这是零破坏的最干净的增量，也是 B 的最大卖点。坏消息全在边界：若一般表达式形式让 `To` 泄漏进 `Case` 与数组下标，就会改变既有的 `Case 1 To 10` / `a(1 To 3)` 的解析。**结论：任何让既有上下文里的 `To` 重新解释的写法都是破坏性的，规格必须给出"范围模式 > 数组切片 > 范围表达式"的优先级表。**

#### 6. Option Strict / 编译选项分叉

B 的范围里无分叉：`For Each n In 0 To 60` 与 `For n = 0 To 60` 一样，在 Strict On/Off 下行为一致（整数范围，无晚绑定）。若做 A/D，`Rnd(1 To 100)` 在 Off 下的晚绑定路径必须明确"不设计"——我们不接受"Off 下能编译、On 下不能"这种跨编译选项语义漂移的区间参数。

#### 7. IDE / IntelliSense

- `For Each n In 0 To 60` 中 `To`/`Step` 的着色、`n` 的类型提示（`Integer`）。
- 若做 D 的区间参数，`Rnd(1 To 100)` 的签名帮助必须显示重载接收"区间"。**但 v1 不做 D，所以这条不存在。**
- 补全：迭代源位置输入 `0 To` 后是否提示 `Step` ？这是小甜点，原型验证即可。

#### 8. 数据 / 普遍性

- **序列生成**：高频但已有能力——`Enumerable.Range` + `For` 循环都可做。这是**可读性与正确性**（off-by-one）的改进，不是能力缺口。`Suspect`：没有量化数据证明业务代码里 `Enumerable.Range` 的误用率高到值得造语法。
- **区间参数**（`Rnd(1 To 100)`）：这才是能力缺口——没有现成的语言级"区间"表达。但也正因如此，它需要 API/类型配合，是更大的工程，不是一句话示例能带过的。
- 主线 2018 年的判断依然有分量："There is a high probability that there are more important features for VB." VBScript.NET 的迭代源场景（脚本里 `For Each n In 0 To 60` 遍历帧/索引）确实高频，这是 B 值得 Consider 的理由。

#### 9. 更简替代

- `Enumerable.Range(start, count)`：现状，count 语义反直觉。
- `For` 循环：现状，样板多但确定。
- **迭代器/局部函数**：写一个 `Iterator Function Range(a, b, Optional step)`——与 B 等价能力，但每次调用点都重复传参，且不解决"读起来像英语"。
- **Analyzer**：提示"此处可换成 `Enumerable.Range`"——能教育，不能让 `0 To 60` 直接编译。
- **模板的证词**：vblang 的 `proposal-template.md` 里，本特性的示例就是 `Dim range = 1 To 10`，注释写 "Creates an IEnumerable object that goes from 1 to 10."——**原设计意图就是 `IEnumerable`**。这印证了 B 的降级目标是合理的，也说明 A 的"赋值给变量"不过是"得到一个 `IEnumerable`"，本身并不需要新类型。

#### 10. 成本 / 优先级

实现面小：B 只是 `For Each` 集合位置的一小段文法 + 降级到既有的 `For`/`Enumerable.Range` 语义。优先级上，它排在 #180（For 溢出修复）之后——**先让 `For` 头的整数运算正确，再谈范围表达式**，否则我们会在两个地方修两遍溢出。与管道运算符、查询增强共享"迭代源"实现面，合起来做比单独做划算。

#### 11. 运行时 / CLR 硬约束

无硬约束。B 降级为 `For` 循环或 `Enumerable.Range`，两者都是既有 IL，PEVerify 无碍。`System.Range` 互操作是真正的坑：C# `Range` 是 half-open 结构，我们的 `1 To 10` 是 inclusive——**任何隐式转换都会制造 off-by-one**，`Probably`：不做隐式转换，只在必要时提供显式工厂（`Range.ToExclusive()` 之类）。Anthony 自己在原文里把 `System.Range interop` 列在 "Not shown"，这个空白不是疏忽，是设计还没想好。

#### 12. 值不值得做

- **价值**：`For Each n In 0 To 60` 是零破坏、读起来像英语、消灭 off-by-one 的增量——真实但不大。**中等偏低。**
- **成本**：B 很小（一段文法 + 降级）。**低。**
- **风险**：A 的边界冲突（Case/数组）是真实的语法雷区；D 的区间参数是另一个特性，不该混进来。**中，且集中在 A/D。**
- **结论**：B 值得以窄形态进入 VBScript.NET；A 与 D 在核心语法未定型前**不值得**。若只做 A 而不收敛到 B，我们会建议不做。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：正中靶心。`For Each n In 0 To 60` 不需要解释，"从 0 到 60"。这是本特性最亮的地方。
- **消除常见样板（原则 #9）**：命中，但注意——它消除的是"样板里的错误"（`Enumerable.Range(0, 61)` 的 off-by-one），不只是样板本身。
- **不引入"第二种做事方式"（原则 #3）**：**偏离风险**。同一字面量被赋予"序列"与"区间"两种语义，等于一次引入两个概念；且 `For Each` 范围源与 `For` 头并存，必须靠共用降级路径来论证"这不是第二种方式"。
- **不与既有语法冲突（原则 #8）**：**直接冲突**。`Case 1 To 10`、`a(1 To 3)`、`Dim a(1 To 10)` 三处既有 `To`。B 避开冲突（迭代源位置无既有 `To`），A 撞上去。
- **继承 VB6 / For 循环基因（"from the `For` loop down"）**：这是本建议唯一的历史定位——它是**从循环头向下长出来的**，不是从 `Span<T>` 向上长的（那是 C#）。这句 2017 年的判断今天仍然准确。
- **与主线关系（对照表 2.3）**：表中标"主线 speclet 中，ModVB LINQ 增强，一致"。我们今天的复核：**"一致"对 2017 年成立**（#25 Speclet needed，从未否决）；**对 2018 年之后的走向则 `Suspect`**——主线在 2018 已弃 `To`（exclusive 顾虑）转向 `..` 与 API-only，Anthony 回到的是 2017 的原点，而非 2018 的现状。这是"主线一致"标签下的一个隐藏断裂，必须写进 speclet。

### RESOLUTION:

1. **整体：Table**。概念真实、VB 基因纯正，但核心语义（类型、惰性、与 C# `Range` 的关系）未定型，且建议原文把"序列"与"区间"两种语义缝在同一个字面量里。
2. **v1 范围 = PROPOSAL B**：范围表达式只作为 `For Each` 的迭代源与 LINQ `From` 子句出现；不做一般表达式（A Table）、不做区间参数（D 另行设计）。`0 To 60` 降级为 `For` 循环语义或 `Enumerable.Range`，与 #180 共用整数运算路径。
3. **语义钉死 inclusive**：`1 To 10` 含 10，与 `For` 头一致；空范围返回空序列非错误；负步长与零步长行为全部对齐 `For ... Step`，不造新规则。
4. **不引入 `..`，也不做 `System.Range` 隐式转换**：C# `Range` 是 half-open，与 inclusive 语义不互转；`System.Range` 互操作列为 `Follow-up`，由显式工厂承担。
5. **`Rnd(1 To 100)` 示例作废**：现有 `Rnd` 签名收 `Single`，示例无法编译且演示的是 D 的区间参数语义。区间参数若要做，走独立 `Range(Of T)` 类型 + 重载，另立建议。
6. **文法优先级**：`Case ... To`（范围模式）> 数组边界/切片 `(... To ...)` > 范围表达式；任何让既有上下文里的 `To` 重新解释的写法都是破坏性的。
7. **范围表达式不进表达式树**，除非降级为 `Enumerable.Range` 调用（`Probably`，speclet 细化）。

### Implication:

- **Speclet needed**——这是对 2017-12-06 主线裁决的延续：`#25` 当时也是 `**Speclet needed**`，今天 ModVB 必须补上主线没来得及写的这份规格。
- 起草范围文法（`For Each`/`From` 子句内）与降级规则（到 `For`/`Enumerable.Range`），含端点、步长、溢出（对齐 #180）、空范围。
- 起草文法优先级表（范围模式 > 数组切片 > 范围表达式）与禁止项（`Case` 内无转义的范围值相等比较）。
- 与模式匹配团队对表：`Case 1 To 10` 范围模式的文法边界。
- 与管道运算符、查询增强团队对表：共享"迭代源"实现面。
- 补一份 Compatibility 分析：证明 B 零破坏（无既有合法表达式含裸 `1 To 10`）。
- 修建议原文：删 `Rnd(1 To 100)` 示例或移到 D；补类型/惰性章节；补 `System.Range` 互操作分析。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：一般表达式形式（A）若未来要做，`1 To 10 Step 2` 的静态类型是 `IEnumerable(Of Integer)` 还是自定义 `Range(Of T)`？与 C# `Range` 是否共用类型？——**先答这个，A 才能复活。**
- `OPEN QUESTIONS`：`Case` 内是否允许显式转义（如 `Case (1 To 10) As Range`）以表达"范围值相等比较"？我们倾向不允许，待模式匹配团队确认。
- `OPEN QUESTIONS`：浮点与 `Date` 范围（`0 To 1 Step 0.1` 的迭代次数）若 v2 要做，语义怎么定？v1 只做整数族。
- `TODO`：量化 `Enumerable.Range`/`For` 循环在 VBScript.NET 目标代码库里的误用率，为普遍性补数据。
- `TODO`：实现最小原型（`For Each`/`From` 迭代源 + 降级），验证语义模型与 IDE。
- `Follow-up`：区间参数（D）单独成案，`Rnd`/随机 API 场景作为动机。
- `Follow-up`：`System.Range` 互操作设计（显式工厂，inclusive↔half-open 转换的语义审查）。

### 状态

- **LDM 状态：Table（整体）**；v1（`For Each`/`From` 迭代源，即 PROPOSAL B）为 Consider。
- **三态判定：Table**——概念值得保留、窄形态可落地，但建议原文以 A 的形态呈现、核心语义未定型、含一个不能编译的概念混杂示例；须收敛到 B、补 speclet 后重新审。VBScript.NET 优先级：排在 #180 溢出修复与管道运算符之后。

---

## 附录：特性评价

# 建议评价报告：proposal-range-expressions.md

## 评价对象

- 建议：proposal-range-expressions.md — 范围表达式 `1 To 10 Step 2`
- 来源：Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1527–1537；`Let odds = 1 To 10 Step 2`、`For Each n In 0 To 60`、`Rnd(1 To 100)` 三示例逐字出自该章；§3.4 多 `For` 变量、§18.4 `x To < y` 独占上界为其延伸）
- 配方目标：以一等表达式产生等差整数序列，消除 `Enumerable.Range`/循环样板，并支持 `Rnd(1 To 100)` 式区间传参

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。主效果（`For Each n In 0 To 60` 可读性、off-by-one 消除）真实可演示；但关键子效果"区间参数"（`Rnd(1 To 100)`）对现有 API 不可达，且未决问题 5 个（≥4 关键设计点）→ 核心语法未定型，效果证据封顶 3 | 已检查 | 无原型；`Rnd` 示例无法编译且演示的是另一特性；无普遍性数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。主体是**继承 VB6/For 循环基因**（非外部），读起来像英语（原则 #5）、消除样板（#9）高度命中；但同一字面量打包了"序列"与"区间"两种无关语义，且与既有 `Case`/数组 `To` 语法冲突（原则 #8 偏离） | 已检查 | 概念混杂未自识；"与主线 2018 转向一致"的标签 `Suspect` |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与 Anthony 原文逐字一致、5 个未决问题诚实列出；但类型、惰性、空范围、负步长全部留在未决（关键边界含糊），无文法、无 Compatibility 章节，状态行占位链接 | 已检查 | `Rnd(1 To 100)` 示例与正文冲突（演示区间非序列）；`To`/`Step` 与 Case、数组切片的文法冲突完全未覆盖 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（脚本快速迭代、少样板）与光（读起来像英语）正向；风（与主线 2018 年 `..`/API-only 转向断裂）受损且未识别；暗（Case/数组 `To` 解析冲突、inclusive↔half-open off-by-one）风险存在 | 已检查（预测待定） | 与主线关系未对齐（2.3 表"一致"实为 2017 年状态）；歧义风险无对冲设计 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料 = Anthony 第 9 章，继承 VB6 `For` 的 `To`/`Step`；未声明与主线 #25（2017 Speclet needed）的继承关系，未声明与主线 2018 转向（`..`/API-only）的偏离，未声明 `Rnd` 示例隐含 API 改造；`System.Range` interop 明确 "Not shown"（Anthony 自列） | 已检查 | 材料来源标注缺主线/继承血缘；与 C# `Range` 的冲突语义未说明 |

## 设计原则对照

- **与 VB 基因：部分一致**——读起来像英语（#5）、消除样板（#9）命中；**偏离**于"不引入第二种做事方式"（#3，一字面量两语义）与"不与既有语法冲突"（#8，Case/数组三处 `To`）。
- **与主线关系：主线一致（2017）但有断裂（2018）**——直接继承 vblang #25（"Speclet needed"，从未否决），2.3 表标"一致"；但主线 2018 因 exclusive 顾虑弃 `To`、转 `..` 与 API-only，Anthony 回到 2017 原点而非 2018 现状，此断裂 `Suspect` 需 speclet 澄清。与 #104（`For Each` 查询理解）、#180（`For` 溢出）直接交互。
- **破坏性变更：B 范围无**（无既有合法表达式含裸 `1 To 10`）；**A 范围潜在有**（`Case 1 To 10`、`a(1 To 3)`、`Dim a(1 To 10)` 若被一般表达式形式重新解释则破坏既有解析）。

## 总评

- **达成程度：部分达成**——`For Each`/LINQ 迭代源的窄价值成立；一般表达式与区间参数的语义、类型、兼容性均未设计完成。
- **LDM 三态建议：Table（整体）；窄 v1（`For Each`/`From` 迭代源）为 Consider**——先补 speclet、收敛范围、修示例，再复审。
- **主要问题**：① "序列"与"区间"两语义缝在一个字面量，`Rnd(1 To 100)` 示例不能编译且演示另一特性；② 无类型/惰性设计；③ `To` 与 Case 范围模式、数组边界/切片的三处文法冲突未覆盖；④ 与主线 2018 转向（`..`、API-only、inclusive↔half-open）的关系未对齐；⑤ `System.Range` 互操作缺失（Anthony "Not shown"）。

## 返工建议

- **补充章节**：范围表达式文法（BNF，注明仅限 `For Each`/`From` 位置）、降级规则（到 `For`/`Enumerable.Range`）、端点/步长/溢出（对齐 #180）、Compatibility 分析、文法优先级表（范围模式 > 数组切片 > 范围表达式）。
- **补充证据**：最小原型（迭代源 + 降级）；语义模型与 IDE 补全验证；`Enumerable.Range` 误用率数据；与 `For` 头行为差异表。
- **未决问题处理**：v1 只做整数族、inclusive 钉死、空范围=空序列、负步长对齐 `For`；一般表达式类型（`IEnumerable(Of Integer)` vs `Range(Of T)`）作为 A 复活前提单独立项；浮点/`Date` 范围 v2；`Case` 内禁止范围值相等比较。
- **设计探索**：区间参数（D）独立成案（`Range(Of T)` 类型 + 重载，`Rnd` 场景作动机）；`System.Range` 显式工厂的 inclusive↔half-open 转换审查；与管道运算符、查询增强共享"迭代源"实现面的接口契约。

---

## 附录：C# 生态与互操作考量

> 依据：`..\..\csharplang` 镜像（dotnet/csharplang 官方仓库）。本提案（范围表达式）与 C# 8 Range/Index（`..`、`^`、`System.Range`/`System.Index`）直接同主题，是本次附录的重点；背景索引见 `..\..\csharplang-index.md`（T2 低层内存主线——C# Range 正是「从 `Span<T>`/切片向上长」的那一支；T6 source-gen）。
> 引用纪律：C# 原文逐字引用并标注来源文件；本节引文均已在镜像中核读。无法在本镜像核实的标 **Suspect** 或列入 **OPEN QUESTIONS**。

### 相关 C# 现实方向

C# 8 的 Range/Index 是一个**切片/索引**特性，不是「生成等差序列」的特性：

- 官方定位（→ `proposals\csharp-8.0\ranges.md`，Summary）：「This feature is about delivering two new operators that allow constructing `System.Index` and `System.Range` objects, and using them to index/slice collections at runtime.」
- 设计起点被钉在 indexing/slicing（→ `meetings\2018\LDM-2018-01-18.md`）：「The scenario we are eager to address right now relates to indexing and slicing, where the elements of the range are contiguous integral indices into some data structure.」
- 范围被**刻意收窄**为只服务索引（→ `meetings\2018\LDM-2018-01-18.md`）：「It therefore seems that we should build a `Range` type specifically for indexing purposes, and have language support *only* for that, but in a way that we can generalize later.」
- 降级方式为普通调用、无新 IL（→ `proposals\csharp-8.0\ranges.md`，IL Representation）：「These two operators will be lowered to regular indexer/method calls, with no change in subsequent compiler layers.」

**inclusive/exclusive 之争，C# 与 VB 面对同一个问题、选了相反答案。** C# 曾在 2018-01-18 倾向 inclusive（→ `meetings\2018\LDM-2018-01-18.md`）：「We're fairly certain we want an inclusive `start..end` syntax.」但四天后在 2018-01-22 因「聚焦索引/切片场景」改为 exclusive（→ `meetings\2018\LDM-2018-01-22.md`）：

> "The one that seems to have the least amount of computational gymnastics across the scenarios is the exclusive option."
> "Let us go with `..` means exclusive. Since we've chosen to focus on the indexing/slicing scenario, this seems the right thing to do:"
> "It lets the end of one range be the beginning of the next without overlap"
> "It avoids ugly empty ranges of the form `x..x-1`"

**C# 讨论过 `foreach` 遍历范围，但最终没有落地。** 在讨论「Range 是否作为表达式自然类型」时提到（→ `meetings\2018\LDM-2018-01-18.md`）：「support for `foreach` naturally falls out: `foreach (var x in 3..5) { ... }`.」但同节随即否定其表达力（→ 同文件）：「the `foreach` support based on this natural type wouldn't be very expressive: It wouldn't allow starting at negative numbers, or going backwards.」终稿 `ranges.md` 通篇无 `foreach`/枚举支持；`System.Range` 是用于切片的半开结构值，**不是带步长的等差序列生成器**。C# 侧「生成序列」仍只能靠 `Enumerable.Range(start, count)`（计数语义）。

**模式化（结构性）Index/Range 支持**是 C# 该特性的互操作核心（→ `proposals\csharp-8.0\ranges.md`）：按「Countable」形状（有 `Count`/`Length` 属性、返回 `int`）自动提供隐式 `this[Index]`/`this[Range]` 成员，`Range` 索引器降级到 `Slice(int, int)`，`string` 特判走 `Substring`、数组走 `RuntimeHelpers.GetSubArray`；且「Moreover, `System.Index` should have an implicit conversion from `System.Int32`...」（同文件，System.Index 节）。通用范围类型（对任意 comparable 的 `Range<T>`）被明确搁置（→ `meetings\2018\LDM-2018-01-10.md`）：「Wouldn't it be nice to have a range type that work on any comparable? Possibly, but we're not eager to solve this right now.」

### 现实 vs 提案

- **同名冲突（真实，需管理）**：C# `System.Range` 是 half-open 的**切片值**；本提案 `1 To 10` 是 inclusive 的**等差序列字面量**。同一「Range」词下两个概念。若未来做 A/D（一般表达式 / 区间参数），类型若叫 `Range` 或直接复用 `System.Range`，将与 C# 语义撞车。结论：VB 侧需要自己的 inclusive 序列/区间类型，与 `System.Range` 划清。
- **语义冲突（off-by-one 雷区）**：`System.Range` 是 `[start, end)`，`1 To 10` 是 `[1, 10]`。C# 选 exclusive 是为了「相邻切片首尾相接不重叠」，VB 选 inclusive 是为了与 `For i = 1 To 10` 循环头一致——各自的取舍都成立，但**任何隐式互转都是隐蔽的 off-by-one**。本附录与 RESOLUTION #4 立场一致：不互转，只提供显式工厂。
- **兼容 / 互补（本提案最该强调的）**：C# 明确**没有**做「带步长的等差序列生成」；其 `foreach` 遍历范围的想法因「不能负数、不能倒退、不表达步长」被自己否定。这三条恰恰是 VB `For ... Step` 头的传统地盘。因此 v1 的 B 形态（`For Each`/`From` 迭代源）填的是 C# 明确留下的空，**与 C# 现实不冲突、不重叠**。C# Range 的「从 `Span<T>` 向上」与本提案的「从 `For` 向下」（meeting 引言）是两个方向的互补，而非竞争。
- **需桥接（VBScript.NET 编译器侧）**：C# 生态（BCL 与第三方）已有大量以 `System.Range`/`System.Index` 为参数/索引的 API（如 span 切片、`AsSpan(Index)`/`AsSpan(Range)`、`MemoryExtensions` 系列）。VB 没有 `..`/`^` 语法，但 `.vbx` 必须能**调用**这些 API。这要求编译器把 `System.Range`/`System.Index` 注册为 well-known 类型，并能识别 `Countable`/`Slice` 结构模式，否则 C# 写的切片风格库对 VB 不可消费。这是本提案（乃至整个 ModVB）真正的互操作工作项。
- **脱节 / 弱相关**：C# ranges.md 的「Index target type conversion」（把 `Index` 表达式作为 target-typed 转换推广到所有 Countable 成员调用）仅列在 Considerations，未全量落地，对 VB 影响小；`System.Index` 的 `^` 与本提案无交集（VB 没有「从尾索引」需求，那是切片的事）。

### 对 VBScript.NET 的适应建议

- **默认安全**：v1 B 形态不需要新 CLR 类型、不需要运行时依赖——降级为 `For` 循环或 `Enumerable.Range` 是纯编译期工作，符合 C#/生态「编译期取代运行时动态」的方向（索引 T6）。**不要**把范围表达式降级为「构造一个 `System.Range` 对象再枚举」——那会把 half-open 语义与装箱/分配带进一个本应零成本的迭代源。
- **按需动态**：若未来做 D 区间参数，定义独立的 inclusive 类型（如 `Range(Of T)`），**不**复用 `System.Range`；与 C# 互操作时用显式工厂转换（`ToExclusive()` 产出 `System.Range`）。原则：inclusive 是 VB 原生语义，half-open 只是**边界交换格式**，永不隐式。
- **识别新元数据 / well-known 类型**：把 `System.Range`/`System.Index` 纳入 VB 编译器的 well-known 类型表，支持调用以它们为参数的 API；并可复用 C# 的 `Countable` 结构模式（`Count`/`Length` 为 `int` + 单 `int` 索引器）来识别「可切片」类型，为 VB 侧提供一致的切片形状——这是 source-gen/编译器前端的活，不是运行时反射（呼应决策文件 M8 的「认识新元数据」桥接点）。
- **Option Strict 分叉**：与 C# interop 无关；维持 RESOLUTION #6 立场（整数范围在 Strict On/Off 下行为一致，不引入晚绑定区间参数）。

### 对既有 RESOLUTION / 三态判定的影响

- **无推翻，方向一致并获 C# 侧证据加固**：RESOLUTION #4「不引入 `..`、不做 `System.Range` 隐式转换」现在有了完整证据链支撑——C# 从 inclusive 初案（LDM-2018-01-18）转向 exclusive 定案（LDM-2018-01-22）恰恰是因为切片场景，反过来证明 VB 为 For 头选 inclusive 同样合理；两套语义不可互转是「各自场景各自正确」，而非一时任性。
- **RESOLUTION #3（inclusive 钉死）保持**：与 C# 的选择是**对照**而非冲突，各得其所。
- **对 meeting 的 OPEN QUESTIONS「A 若复活，与 C# `Range` 是否共用类型」给出数据点**：**不应共用**。`System.Range` 是 half-open 切片值，A 的「等差序列值」若与它共用类型会永久背上 off-by-one 语义包袱；A 复活时应定义独立的 inclusive 序列类型（或直接以 `IEnumerable(Of Integer)` 为静态类型）。这为 speclet 先答掉了一个悬置问题。
- **三态判定（Table；v1 = B 为 Consider）不变**：本附录不改变「v1 只做迭代源、A/D 延后」的裁决；只补充了 `System.Range` 互操作（meeting 的 `Follow-up`）为何必须走显式工厂的生态依据。

### 引用纪律与 OPEN QUESTIONS

- 本节引用的 C# 原文均已在本镜像逐字核读：→ `proposals\csharp-8.0\ranges.md`；→ `meetings\2018\LDM-2018-01-10.md`；→ `meetings\2018\LDM-2018-01-18.md`；→ `meetings\2018\LDM-2018-01-22.md`。
- **Suspect**：meeting 正文引用的「These APIs will allow older versions of Visual Basic (and C#) access these APIs and may come out reading more 'VB like' than the dot dot range syntax.」出自 dotnet/roslyn 的 `features/range.md`（meeting 已标注），**不在 csharplang 镜像内**，本附录无法核实其逐字原文 → 标 **Suspect**，引用时以 meeting 既有表述为准。
- **OPEN QUESTIONS**：BCL 中实际以 `System.Range`/`System.Index` 为参数的切片 API 清单（`MemoryExtensions`/`AsSpan` 系列）需在 dotnet/runtime 核实，不在本库；`System.Range`/`System.Index` 在 .NET 各目标框架的可用性面（older frameworks 需包支持）未在本库展开——这两项是 speclet 写互操作章节前要补的实证。
