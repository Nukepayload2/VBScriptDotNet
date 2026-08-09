# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。我们本周回到建议审阅。这份建议在打开前就带着一个我们无法忽视的前提：它提议的正是主线 vblang 在 2018 年 5 月 30 日 [#305](https://github.com/dotnet/vblang/issues/305) 上**已经明确否决过**的东西——"`In` 对普通集合（非类型）"。当时主线的原话是它"does not seem to have much value - is not significantly more expressive even if shorter - than .Contains against the list"。所以这场会议在很大程度上是一场"重开旧案"的辩论：要么我们给出主线当年没有看到的理由，要么我们承认主线是对的。我们不打算只做橡皮图章。

## Agenda

* [Proposal: In / NotIn 运算符（映射 Contains）](#proposal-in--notin-运算符映射-contains)

## Proposal: In / NotIn 运算符（映射 Contains）

_Related: [vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)；[vblang #25 – Range `1 To 10 Step 2` Expressions](https://github.com/dotnet/vblang/issues/25)；ModVB：`proposal-range-expressions.md`、`proposal-select-case-enhancements.md`_

### 场景与缺口

建议的起点是成员测试的可读性。今天判断"一个值是否在某个集合里"要写 `bannedWords.Contains(input)` 或 `Array.IndexOf(...) >= 0`，动词藏在最前面，主语在宾语之后，读起来要心里做一次倒装：

```vb
' 今天：动词在句首，读者要在脑内还原成"input 在 bannedWords 里吗"。
If bannedWords.Contains(input) Then Throw New ArgumentException(NameOf(input))
```

建议提出 `input In bannedWords`，让主语、动词、宾语按自然语言顺序排列，并宣称与既有 `In` 语义一致。但我们在核对"既有语义"时发现第一处疑点。建议 Motivation 写道：

> 也符合 VB 中 `For Each x In coll`、`Select Case` 里 `Case x In range` 已有的 `In` 语义。

`For Each x In coll` 确实存在。但 **`Case x In range` 在主线 VB 里并不存在**——主线 `Select Case` 只有 `Case value`、`Case low To high`、`Case Is <比较>`、`Case Like pattern`，没有 `Case x In range`。`Case In bannedWords` 是 Anthony 设计 3.3 节的**提案**，不是现状。`Suspect`：这句 Motivation 把"提案中的语法"和"既有的 `In` 语义"混为一谈了。`For Each` 的 `In` 连接的是迭代变量与可枚举源，语义上是"从……中取"，不是"属于……"，与我们讨论的成员测试并非同一层。

### 候选方案

**PROPOSAL A — 通用二元运算符 `In` / `NotIn`，编译期映射 `Contains`。** 按建议原文：`x In coll` 改写为 `coll.Contains(x)`，`NotIn` 为其取反，可作用于任意集合；可与范围表达式配合做区间判定（`actScore NotIn 1 To 36`）。

**PROPOSAL B — 仅提供 `In`，否定用 `Not (x In coll)` 表达。** 建议原文 Alternatives 第二条。省掉 `NotIn` 关键字，但 `x NotIn coll` 的自然语言优势消失。

**PROPOSAL C — 只允许 `In` / `NotIn` 用于 `If` / `Select Case` 条件，不作为一般表达式。** 建议原文 Alternatives 第三条。缩小语法表面，`Case In bannedWords` 与条件中的 `x In coll` 共享同一实现。

**PROPOSAL D — 什么都不做。** 继续 `.Contains` / `IndexOf` / `Array.Exists`。这正是主线 #305 的立场，也是建议原文 Alternatives 第一条。

**PROPOSAL E — 缩小为"区间判定"专用：`x NotIn 1 To 36` 直接编译为区间比较（`x < 1 OrElse x > 36`），不做通用集合成员测试。** 这是我们自己补的候选，回应主线"对普通集合无明显增值"的批评——因为区间判定正是建议里唯一真正有新意的场景，但它的 lowering 在建议里完全没有定义。

### 权衡：Q&A

- **A 是不是在重开主线已否决的旧案？** 是，而且是**原封不动**地重开。主线 #305（2018.05.30）把 `In` 分成三块审：普通集合、类型列表、`Out`。我们的 A 恰好就是第一块：

  > In against a normal list (not types)：This does not seem to have much value - is not significantly more expressive even if shorter - than .Contains against the list. Not moving forward for this reason.

  主线的价值判断是"增值不够"。我们的反驳是：VBScript.NET 不是主线 VB，受众不同——主线在服务"数十万安静的存量客户"（同次会议原话："hundreds of thousands of quiet customers each month primarily want VB to keep doing what it does now"），而我们是面向脚本/首次开发者、把可读性当卖点的沙盒。对 `.Contains` 这种 .NET 习语，脚本背景的用户天然隔一层。**但**，我们在往下走时发现，这个"受众不同"的论证不足以抵消 A 的实质缺陷（见追问 #2、#6），它只能救活 E，救不活 A。

- **A vs D：`In` 比 `.Contains` 多给了什么？** 表达力上几乎没有增量——`coll.Contains(x)` 与 `x In coll` 是同一个调用。主线的判词我们用同一条理由：**更短不等于更有表达力**。如果只为了少敲几个字符而引入一个与既有 `In` 语法面冲突的二元运算符，这正是设计原则里"引入第二种做事方式"的典型。真正有新意的不是 A，而是 A 与范围表达式的组合。

- **区间判定（E）为什么值得单独看？** `actScore NotIn 1 To 36` 是真实的输入校验场景（建议自述为 ACT 分数合法性校验）。把它写成 `actScore < 1 OrElse actScore > 36` 并不难，但语义上"不落在 [1,36] 内"正是脚本代码里每天要写的守卫。它读起来像英文，也确实是建议里唯一"`.`Contains` 替代不了"的用法——因为 `Enumerable.Range(1, 36).Contains(actScore)` 是 O(n) 的物化加扫描，而对 `Double`（如体温 `temperature NotIn 36.0 To 39.0`）`Enumerable.Range` 根本不存在。**区间判定只有作为区间才有意义，作为序列是错的 lowering。** 而建议对 `1 To 36` 到底是区间还是序列没有任何定义——它把球踢给了 `proposal-range-expressions.md`，但那份建议自己也没有定（其未决问题第一、二条就是"是否只支持整数""惰性还是物化"）。**两个建议之间缺一张契约。** 这是本场我们最尖锐的发现之一。

- **`NotIn` 关键字值得吗？** 主线在模式匹配语境（#337，2018.12.19）说过一句话我们很认同："We are not ready for a negation pattern, but think that would work better than a `DoesntMatch` keyword."——否定**形式**（组合）优于否定**关键字**。同一逻辑适用到 `NotIn`：`Not (x In coll)` 用已有语法表达否定，而 `NotIn` 是新 token、破坏今天把 `NotIn` 当标识符用的代码（`Dim NotIn As String` 今天合法）。而且主线在同次会议还提醒："We need some compelling cases for non-matches."——否定的 case 我们一个都想不出比 `Not (x In coll)` 更省事的。

- **`Case In bannedWords` 呢？** 它属于 `proposal-select-case-enhancements.md`，操作数次序与运算符形式相反（主语来自 `Select` 表达式）。如果 A 被否，`Case In` 的"映射 `Contains`"内核同样被否——两者同生共死。`Case NotIn 1 To 36` 若走 E 的区间语义，则随 E 一并搁置。

- **我们有没有可能对 A 网开一面？** 有，且只有一个诱因：`If input In bannedWords Then` 的英语语序对首次开发者是真实磁铁。但代价清单太长：`Option Compare Text` 下与 `=` 语义分叉（追问 #6）、`Dictionary` 无 `Contains`、数组要依赖扩展方法、字符串右操作数跨目标框架行为不一致、`NotIn` 破坏标识符、无数据支撑、无原型。当代价与主线否决叠加，我们的结论是**不网开**。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`In` 是 VB 的**保留关键字**，今天在三个语法面有既定义务：`For Each x In coll`、查询 `From x In coll`、`Join` / `Group Join` 的 `On ... In`。把它扩展为二元运算符，等于给同一 token 增加第四个角色：

```vb
For Each word In bannedWords          ' 语句：迭代
From word In bannedWords              ' 查询：范围变量
If word In bannedWords Then Throw     ' 提案：二元运算符
```

三者的区分靠语法位置（`For Each` 头、`From` 子句、表达式语境），上下文相关文法可行但脆弱。最麻烦的是运算符优先级：`In` 相对 `=` / `Is` / `Like` / `AndAlso` / `OrElse` / `Not` 的绑定顺序必须定义。`Not x In coll` 是 `Not (x In coll)` 还是 `(Not x) In coll`？`x = y In coll` 是 `x = (y In coll)` 还是 `(x = y) In coll`？建议没有给任何文法（BNF）或优先级表。`OPEN QUESTIONS`。

还有一处我们特别在意：在查询体内写 `Where x In coll`——范围变量 `x` 后紧跟 `In`，和 `From x In coll` 的读法只差一个关键字位置。解析器能分，但**读代码的人**不能分。`Probably`：这是"细微字符改变语义"的家族成员。

#### 2. 角案例与边界语义

**`Option Compare Text` 分叉（本场最重的发现）。** `List(Of T).Contains` 用的是 `EqualityComparer(Of T).Default`，对 `String` 是序数比较（区分大小写）。而 VB 的 `=` 在 `Option Compare Text` 下不区分大小写。于是：

```vb
Option Compare Text

Dim coll As New List(Of String) From {"foo"}
Dim x As String = "FOO"

If x = "foo" Then      ' True —— Text 模式下 = 不区分大小写
    ...
End If

If x In coll Then      ' ??? —— 若映射 coll.Contains(x)，序数比较 → False
    ...
End If
```

**同一份代码、同一个比较意图，`=` 与 `In` 给出相反答案。** 这是建议 Drawbacks 完全没有意识到的隐蔽语义分叉，性质上正是设计原则 #7 所警惕的"细微变化改变语义"。要修就得让 `In` 对字符串走 VB 的比较语义（`Operators.CompareString` + `Option Compare`），但那样又与"映射 `Contains`"的定位矛盾，而且对非字符串集合无意义。`Suspect`：这个坑在 Anthony 原文里从未被讨论过。

**右操作数为字符串。** `ch In "abc"` 若映射 `"abc".Contains(ch)`，`Char` 参数没问题；但 `s In "abc"`（`s As String`）依赖 .NET Core 2.1+ 才有的 `String.Contains(String)`，.NET Framework 4.x 上没有这个重载——同一源码跨目标框架编译结果不同。建议未决问题第五条问"映射 `Contains`、`IndexOf` 还是正则"，但我们认为这条更应该问"**要不要支持字符串右操作数**"。`Probably`：v1 应拒绝字符串右操作数，只允许"元素类型为 String 的集合"，避免跨框架与 `Option Compare` 双重雷区。

**`Dictionary`。** `scores.Contains("Ada")` 不存在——`Dictionary(Of K,V)` 只有 `ContainsKey` 和 `ContainsValue`。建议的"映射 `Contains`"对字典直接编译失败。键成员测试（`key In dict`）是真实场景，但没有定义。`OPEN QUESTIONS`。

**数组。** `T()` 没有实例 `Contains`，得绑 `Enumerable.Contains(Of T)` 扩展方法。编译器要为数组、`IEnumerable(Of T)`、`List`、`HashSet` 各走一条绑定路径；若集合类型是 `Object`（Option Strict Off 下的晚期绑定），则晚期绑定 `Contains`。两套绑定语义，spec 只字未提。

**元素类型不匹配。** `x As String In List(Of Integer)` 在 Option Strict On 下无隐式转换 → 报错；Off 下走收缩转换（`CInt(x)`）。继承 VB 普通转换规则即可，但需要显式写出来——建议未写。

**`Nothing`。** `x In Nothing` 与今天 `Nothing.Contains(x)` 行为一致（运行期 NRE），无新增语义，可接受。`Nothing In coll` 对引用类型元素 = 检查是否存在 null 元素，行为正确但需要文档。

**区间 + `Step`。** `x In 1 To 10 Step 2` 若按序列 = 成员测试 {1,3,5,7,9}，若按区间 = 无意义。区间语义与 `Step` 不兼容，进一步证明 `1 To 36` 需要独立的类型契约而不是 `IEnumerable`。

**空集合**：`x In {}` 恒为 False，自然成立，无问题。

#### 3. 作用域与绑定

`x In coll` 的语义模型应该暴露什么符号？两个选择：绑定到合成操作符（如 `Microsoft.VisualBasic.Operators.In(x, coll)`）或直接改写为 `coll.Contains(x)`。直接改写意味着语义模型里根本不存在 `In` 运算符节点——IDE 补全、`GetSymbolInfo` 都要透传 `Contains`，这对诊断和重构是干净的（零新符号），但要求 binder 在改写前完成完整的重载解析。`Probably`：若做，选"改写前先绑定、无 `In` 符号"路线。但注意 `coll` 若有副作用只求值一次——`x In GetList()` 不能把 `GetList()` 算两遍，改写必须保证单次求值。

#### 4. 与既有特性的交互

- **`For Each` / `From` / `Join`**：靠语法位置区分，见追问 #1。
- **`Select Case`**：`Case In bannedWords` 与运算符形式次序相反、语义同源，必须与 `proposal-select-case-enhancements.md` 共用一套 lowering，否则造出两套成员测试。两建议当前都没有声明这个契约。
- **`Option Compare`**：见追问 #2，这是与既有比较语义最危险的交互。
- **晚期绑定**：Option Strict Off 下 `x In coll`（`coll As Object`）→ 晚期绑定 `Contains`。行为与今天写 `coll.Contains(x)` 完全一致，可接受；但 spec 要声明"绝不把 `In` 改成静态绑定"——与我们在类型流分析会议上的"启用型绑定"规则同一精神。
- **表达式树**：`x In coll` 在表达式树里改写为 `Expression.Call(ContainsMethod, coll, x)` 即可，无新节点需求。

#### 5. Breaking change 与兼容性

- `In` 作为运算符：**enable-only**。今天表达式位置出现 `x In coll` 是语法错误（`In` 是保留字，不能当普通二元运算），新增后从不合法变合法，重编译既有代码不会改变行为。这是我们能给出的最干净的兼容性结论。
- `NotIn` 作为关键字：**破坏性**。今天 `NotIn` 是合法标识符：

  ```vb
  Dim NotIn As String = "NotIn"   ' 今天合法
  Console.WriteLine(NotIn)
  ```

  新增 `NotIn` token 后，`x NotIn coll` 会重新解析为运算符。虽然 `Dim NotIn` 仍可用方括号逃逸（`[NotIn]`），但这属于"重编译行为变化"，违反原则 #1。这是 `NotIn` 独立关键字的死穴，也是我们倾向 B（组合否定）的硬理由。
- 无 `langversion` 门控、无警告策略、无兼容性章节——建议缺失全部三条，`TODO`。

#### 6. Option Strict / 编译选项分叉

见追问 #2 的 `Option Compare` 分叉。这里补充 Option Strict 本身：On 下静态绑定 `Contains` 并要求元素类型可转换；Off 下晚期绑定/收缩转换。两路径对**同一份代码**的运行期结果应一致（当静态绑定成功时）。这条可以做到，但 `Option Compare Text` 的分叉是行为级的不一致，**无法用"绑定路径一致"掩盖**——`In` 走 `EqualityComparer.Default`（序数），`=` 走 `Operators.CompareString`（Text 感知），两条路径给出不同布尔值。这是硬伤。

#### 7. IDE / IntelliSense

若走"无 `In` 符号、透传 `Contains`"路线，补全和签名帮助几乎免费——但 hover `In` 时 IDE 得解释"右操作数的 `Contains`"。`Case NotIn bannedWords` 里 IDE 应该补全什么？集合成员？不，补全的是 `NotIn` 之后的表达式，即集合——补全模型要新建。这些在原型里验证，否则不算设计完成。

#### 8. 数据 / 普遍性

主线的怀疑不是没有依据——我们没有 VBScript 风格代码里 `.Contains(` 使用频次的任何数据，也没有用户请求记录。唯一站得住的普遍性证据是"区间校验是脚本日常"，但没有量化。`Suspect`：可读性增益真实但不可衡量；区间判定是唯一具体用例，且它不需要 A。建议为数据补一个 analyzer 统计（对参考代码库跑 `\.Contains\(` 与 `IndexOf(...) >= 0` 的出现频次）——这是返工清单第一项。

#### 9. 更简替代

- `.Contains` / `IndexOf`：现状，主线已选。
- **Analyzer + 代码样式**：一个 analyzer 可以提示"这里可改写为 `x In coll`"，但不能让 `In` 编译。作为语言特性，`In` 与"just an analyzer"竞争——若语法增益只有可读性而没有表达力，analyzer 路径的成本远低于语言特性。
- **区间专用库**：若 `1 To 36` 成为带 `Contains`/`IsInInterval` 的一等区间类型，`x NotIn 1 To 36` 就是区间对象上一个库调用的糖衣，**不需要任何新运算符**。这正是主线 2018.02.28 对 C# Range 的立场原话：

  > If the API support is good enough, don't do the language work in VB.

  E 方案实际上是把区间判定的语法糖衣推迟到 range-expressions 定契约之后。

#### 10. 复杂度 / 成本 / 优先级

A 是一条端到端的新运算符：文法 + 优先级 + 绑定（实例/扩展/晚期三路）+ 转换规则 + `Option Compare` 语义 + IDE + 文档 + 兼容性分析。成本高，而增值被主线判为不足。E 若成立则极小（条件表达式内的区间特殊化），但前提是 range-expressions 先定 `1 To 36` 是什么。我们的排序：**E 依赖的前置工作（range 契约）优先于 E，E 优先于 A，A 排在队列末端。**

#### 11. 运行时 / CLR 硬约束

无。纯编译期改写为方法调用，无 PEVerify 问题，无 CLR 存储规则触达。表达式树按 `Call` 节点生成。唯一"硬约束"是跨目标框架的 `String.Contains(String)` 重载存在性（追问 #2），那是 API 面不是 CLR 面。

#### 12. 值不值得做

价值（可读性）中低且不可量化；成本（A）高；风险（`Option Compare` 分叉、`NotIn` 破坏、与主线冲突）中高且无对冲。**A 不值。** 价值（区间校验）中、成本（E，前置 range 契约）中、风险低——**E 值得，但要等前置**。整体：不建议投入，除非重写成 E 的窄范围。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：`If input In bannedWords Then` 正中靶心，这是建议最强的一点。`For Each x In coll` 给了它血缘合法性。
- **不引入"第二种做事方式"（原则 #3）**：A 违反。`.Contains` 已经存在且表达同一件事，`In` 只是短一点。原则 #9（消除常见样板）要求的是"最小语法解决高频痛点"，A 的样板削减量不足以跨过 #3 的门槛。
- **避免隐蔽的语义变化（原则 #7）**：`Option Compare Text` 下 `In` 与 `=` 分叉，正是此类。这是 A 的否决票。
- **永不破坏现有代码（原则 #1）**：`NotIn` 关键字破坏标识符，直接踩线；B 的组合否定则零破坏。
- **不引入第二关键字（呼应 #337 的 negation 判断）**：否定用组合优于关键字。
- **与主线关系（对照表 2.3）**：`Range 1 To 10 Step 2` 主线"speclet 中"，ModVB range-expressions 与其**主线一致**；`In` 运算符主线 #305 明确 No Plans，ModVB 建议与之**主线冲突**（且未声明冲突）；`Case In` 随 select-case-enhancements，与模式匹配方向一致但内核被 #305 波及。

### RESOLUTION:

1. **否决 PROPOSAL A**（通用二元 `In` / `NotIn` 映射 `Contains`）。理由与主线 #305 一致：对普通集合无明显增值；更关键的是我们新发现的 `Option Compare Text` 分叉——`In`（经 `EqualityComparer.Default`）与 `=`（经 `Operators.CompareString`）对同一比较意图给出相反答案，这是隐蔽语义变化，不可接受。此否决覆盖 `Case In` / `Case NotIn` 的 `Contains` 内核。
2. **`NotIn` 独立关键字否决**。`NotIn` 今天是可合法标识符，新增 token 破坏重编译；否定用组合（`Not (x In coll)`）或随模式匹配的否定形式。此为 PROPOSAL B 方向，但 B 本身随 A 一并搁置（没有 `In`，组合否定无意义）。
3. **PROPOSAL E 搁置（Table）**，等待 `proposal-range-expressions.md` 定契约：`1 To 36` 到底是惰性序列、物化集合、还是带 `Contains`/`IsInInterval` 的一等区间。**若**它成为一等区间，`x NotIn 1 To 36` 应作为区间库调用的糖衣落地，而非新运算符。这与主线 2018.02.28 的立场一致："If the API support is good enough, don't do the language work in VB."
4. **`Case In` / `Case NotIn`（Select Case）**：随 A 否决。若未来模式匹配/ShapeOf 工作推进，按主线标签（Pattern Matching）方向重新评估，standalone 状态为 Table。
5. **不重开 #305**：主线已为此特性留下明确判词与标签（`LDM Reviewed: No Plans`），VBScript.NET 沙盒若要偏离，必须先在建议里给出"受众不同"的系统性论证 + `Option Compare` 的解决方案。本次否决不闭死未来重开的路，但重开的前提是**重写建议**，不是当前这份。

### Implication:

- 把"区间判定"从本建议中拆出，视 range-expressions 契约而定是否形成独立小建议（E 方向）。
- 与 range-expressions 团队对表，先定 `1 To 36` 的类型与 `Contains`/`IsInInterval` API——这是解锁 E 的唯一前置。
- 若未来重开 A：必须先解决 `Option Compare Text` 分叉（`Probably`：让 `In` 对字符串走 `Operators.CompareString`，或明确拒绝字符串元素）。
- 不引入 `NotIn` token、不引入 `In` 运算符符号；`Case In` 的 lowering 与 select-case-enhancements 合并考虑。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`1 To 36` 的区间契约（sequence vs interval vs one-class type）——归属 range-expressions，本建议无法独立定夺。
- `OPEN QUESTIONS`：若重开 A，`Option Compare Text` 下 `In` 是否应跟随 `=` 的语义；字符串元素、`Dictionary` 键测试、数组扩展方法三条绑定路径如何统一。
- `OPEN QUESTIONS`：`In` 运算符若存在，其相对 `=` / `Is` / `Like` / `AndAlso` 的优先级与 `Not x In coll` 的解析。
- `TODO`：为"成员测试/区间校验频次"补数据（analyzer 统计 `.Contains(` 与 `IndexOf(...) >= 0`）；目前普遍性论证止于 `Suspect`。
- `TODO`：修复建议中的事实错误——`Case x In range` 并非既有语法（`Suspect`），并补一份与主线 #305 的显式冲突声明。
- `Follow-up`：与 select-case-enhancements、range-expressions 建立三份建议之间的 lowering 契约表。

### 状态

- **LDM 状态：LDM Reviewed: No Plans**（对 A）；E 为 `LDM Considering`（挂起，等 range 契约）。
- **三态判定：Table** — 核心语法 A 被否（对齐主线），区间碎片 E 有真实价值但依赖未定的前置契约；当前文档不成熟，不应进入 Active。

---

## 附录：特性评价

# 建议评价报告：proposal-in-notin-operators.md

## 评价对象

- 建议：proposal-in-notin-operators.md — `In` / `NotIn` 运算符（编译期映射 `Contains`）
- 来源：Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1632–1636：`In`/`NotIn` map to `Contains`、`actScore NotIn 1 To 36`）；并涉及第 3.3 节 `Select Case`（`..\AnthonyDesign_wordpress.txt` L876–881：`Case In bannedWords` / `Case NotIn bannedWords`）。两处源头建议未自行标注章节。
- 配方目标：`input In bannedWords` 接近自然语言、映射 `Contains`，并可与范围表达式配合做区间判定（`actScore NotIn 1 To 36`）
- 关键血缘：与主线 vblang #305（2018.05.30）"In against a normal list (not types)" 是**同一特性**，主线已 No Plans。

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。可读性改进无数据、无原型；示例 `If input In bannedWords Then Throw` 可写但未运行；主效果（通用成员测试）被主线 #305 判"无明显增值"；子效果（`NotIn 1 To 36`）的 lowering 未定义，无法编译演示 | 已提供 | 无原型；效果与主线结论正面冲突未声明；区间语义未定义 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`In` 有 `For Each`/`From` 的血缘（自然语言、可读），但通用成员测试是 SQL/Python 式；`NotIn` 需新关键字（破坏标识符）；区间判定是打包进来的无关能力，依赖 range-expressions | 已检查 | `For Each` 的 `In`（迭代）与成员测试（属于）并非同层语义，Motivation 混写（Suspect）；`Option Compare Text` 分叉未处理 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全但：状态栏占位符（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）；5 个未决问题全是核心设计点且无任何倾向答案（≥4 关键点 → 效果证据封顶）；无 Compatibility/breaking-change 章节；`Case x In range` 声称"已有"实为提案中（Suspect） | 已检查 | 与 range-expressions/select-case-enhancements 的契约缺失；`NotIn` 破坏性未分析；`Dictionary`/数组/字符串右操作数未覆盖 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水（差异化脚本体验）或受益；暗风险明显且未对冲（与主线冲突、`Option Compare` 分叉、`NotIn` 标识符破坏）；风（与 Select Case/range 的一致性）断裂未识别 | 已检查（预测待定） | 无对冲设计；与主线 #305 的冲突是最大暗风险，文档零提及；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料 = Anthony 第 9 章 + 3.3 节，未标注章节号；**最关键成分未声明**：本建议就是主线 #305 已否决的同一特性；区间语义借自 range-expressions 未声明契约；VB6/VBScript 自然语言血缘未点明 | 已检查 | 未声明与 #305 的关系（成分/影响的最关键缺口）；区间 lowering 借用未定义的兄弟建议 |

## 设计原则对照

- **与 VB 基因：部分一致**——原则 #5（读起来像英语）契合；原则 #9（消除样板）部分契合。但偏离：原则 #3（引入第二种做事方式：`In` 与 `.Contains` 并存）；原则 #7（`Option Compare Text` 下 `In` 与 `=` 隐蔽分叉）；原则 #1（`NotIn` 关键字破坏现有标识符）。
- **与主线关系：主线冲突**——`In` 运算符正是 #305 明确 No Plans 的件（"In against a normal list"）；区间部分与 #25 Range（主线 speclet 中）方向一致但契约未定；`Case In` 随 select-case-enhancements 与模式匹配方向一致。
- **破坏性变更：潜在有**——`NotIn` 新 token 改变 `Dim NotIn` 等既有标识符的重编译行为（可用 `[NotIn]` 逃逸，但仍属行为变化）；`In` 运算符本身 enable-only（表达式位置今天报错）。`Option Compare Text` 下与 `=` 的行为分叉是运行期语义层面的破坏，建议未分析。

## 总评

- **达成程度：未达成**——核心语法（`In` → `Contains`）被主线否决且建议未回应；子效果（区间判定）lowering 契约缺失；无原型、无兼容性分析、5 个未决问题无倾向。
- **LDM 三态建议：Table**——A（通用运算符）= Reject；E（区间判定）= 待 range-expressions 定契约后再议（Table）；当前文档整体搁置，不应 Active。
- **主要问题**：① 直接重开主线 #305 否决件，且无"为何 VBScript.NET 应偏离"的论证，也未声明冲突；② `Option Compare Text` 下 `In` 与 `=` 语义分叉（建议 Drawbacks 未覆盖的隐蔽变化）；③ `1 To 36` 区间 lowering 未定义，与 range-expressions 契约缺失；④ `Case x In range` "已有"表述与事实不符（Suspect）；⑤ `NotIn` 关键字破坏现有标识符；⑥ 状态栏占位符、无兼容性章节、未决问题全部悬空。

## 返工建议

- **补充章节**：Compatibility / breaking-change（`NotIn` 标识符、`Option Compare Text` 分叉、late binding、Option Strict On/Off、`langversion` 门控与警告策略）；文法（BNF、`In` 优先级、`Not x In coll` 解析、`NotIn` token 处置）；与 range-expressions、select-case-enhancements 的 lowering 契约表。
- **补充证据**：主线 #305 原话引用与"为何 VBScript.NET 应偏离"的系统性论证；`.Contains(` / `IndexOf(...) >= 0` 使用频次的 analyzer 统计（当前普遍性为 Suspect）；区间判定真实代码样例（ACT 校验、体温等）与 lowering 演示；最小原型。
- **未决问题处理**：逐条给倾向——字符串右操作数 → 拒绝（跨框架 + `Option Compare` 双重雷区）；`Dictionary` → 明确 `ContainsKey` 还是拒绝；数组 → `Enumerable.Contains`；`Option Compare` → 决策"跟随 `=`"还是"跟随 `Contains`"（本场倾向：不解决此条就否决 A）；范围 → 移交 range-expressions。
- **设计探索**：把 E 拆为独立小建议，前置依赖 `1 To 36` 区间类型及其 `Contains`/`IsInInterval` API（对齐主线 2018.02.28 "If the API support is good enough, don't do the language work in VB"）；否定形式跟随模式匹配的 negation 方向而非新关键字。

---

## 附录：C# 生态与互操作考量

### 关系强弱声明

本提案（`In` / `NotIn` 运算符，编译期映射 `Contains`）与 C# **底层互操作**的触达很薄：按 PROPOSAL A 落地也只是编译期改写为 BCL 方法调用，不产生新 IL、新元数据、新 CLR 存储规则（会议正文追问 #11 已判定"无 CLR 硬约束"）。因此本附录不谈底层互操作，只做三件**参照性**的事：① C# 对"集合成员测试 / 区间判定"到底有没有语言糖；② C# 的 `in` 关键字与 VB 提议的 `In` 运算符的重名风险；③ C# 生态对"区间测试该不该造运算符"的裁决如何反过来支撑本会议的 RESOLUTION。结论大多是"参照对齐"，不是"桥接需求"。

### 相关 C# 现实方向

**(a) C# 没有集合成员测试运算符；`in` 是参数修饰符，不是二元运算。** C# 的 `in` 属于 C# 7.2 "readonly references" 家族，语义是"按只读引用传参"：

> "`in` parameters are declared by using `in` keyword as a modifier in the parameter signature." — `proposals\csharp-7.2\readonly-ref.md`

> "`in` parameters allow both lvalues and rvalues and can be used without any annotation at the callsite." — `proposals\csharp-12.0\ref-readonly-parameters.md`

C# 表达式位置 `x in list` 是语法错误（`in` 只出现在参数签名、`foreach (var x in coll)`、查询 `from x in coll` 三个语法面，与 VB 的 `For Each` / `From` 结构同构，但都不构成"属于"语义）。C# 的集合成员测试现状是 **BCL 方法**：`List(Of T).Contains` / `HashSet(Of T).Contains` / `Enumerable.Contains(Of T)` 扩展方法——与 VB 今天写的 `.Contains` 是同一套 API，**没有任何语言糖**。`Probably`：`x in list` 为语法错误是基于 C# 文法与上述 `in` 定位的事实陈述，非逐字引用。

**(b) C# 用模式组合器表达"枚举集成员"与"区间判定"，不是新运算符。** C# 9 引入模式组合器 `and` / `or` / `not` 与关系模式，官方明确点名其用途之一是"测试值的范围"：

> "Pattern *combinators* permit matching both of two different patterns using `and` (this can be extended to any number of patterns by the repeated use of `and`), either of two different patterns using `or` (ditto), or the *negation* of a pattern using `not`." — `proposals\csharp-9.0\patterns3.md`

> "The `and` and `or` combinators will be useful for testing ranges of values" — 同文件，示例：

``` csharp
bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
```

即 C# 的"`x` 落在 `[a, b]` 内 / 外"的惯用表达是 `x is >= a and <= b` / `x is < a or > b`（关系模式 + 组合器，纯组合、无新运算符）；小枚举集成员用 `x is 1 or 2 or 3`。`not` 组合器同版本落地：`if (e is not null) ...`（同文件 lines 99–105）。

**(c) C# 的 `Range`（`..`）是"切片 / 下标"语义，不是数值区间。** C# 8 的 `..` 运算符构造 `System.Range`，用于**索引 / 切片集合**：

> "This feature is about delivering two new operators that allow constructing `System.Index` and `System.Range` objects, and using them to index/slice collections at runtime." — `proposals\csharp-8.0\ranges.md`

因此 C# 生态里"区间"有两个互不相干的落点：`..` → 集合切片（`array[2..^3]`）；数值区间测试 → 关系模式（b）。C# 没有把 `1..36` 当"数值区间集合"，而是把区间判定做成模式的组合——这直接回应本提案与 range-expressions 之间的未决契约。

**(d) 否定用组合、不用关键字——C# 已用 `not` 模式兑现。** 主线 vblang #337（2018.12.19）曾对否定模式表态"would work better than a `DoesntMatch` keyword"；C# 9 随后以 `not` 组合器落地（见 b）。C# 生态对"否定该不该造关键字"的裁决是**组合否定**——与本会议否决 `NotIn` 独立关键字的方向一致。

**(e) 背景：C# 驱动 CLR 新特性，VB 主线退化为"只做兼容"。** 本索引 T1/T8 的概括：C# 是 CLR 新特性与 .NET 生态的主要推动者；unsafe-evolution 对 VB 的表述是：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." — `proposals\unsafe-evolution.md`

含义：VBScript.NET 在"是否给语言加糖"上必须对照 C# 已经做过 / 拒绝过的路，否则会制造与 C# 生态脱节的双语差。

### 现实 vs 提案

| 提案碎片 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| A：通用 `In` / `NotIn` 映射 `Contains` | C# 无成员测试运算符；成员测试 = `Contains` API + 模式 `or` | **冲突（方向性）** | C# 明确不造该运算符，VB 造了 = 与生态"双语分叉"；且映射 `Contains` 无表达力增量，C# 也没有因此加糖 |
| `NotIn` 独立关键字 | C# 9 `not` 模式 = 组合否定 | **冲突** | C# 用组合、不用关键字；`NotIn` 新 token 破坏标识符，生态无先例 |
| E：区间判定 `x NotIn 1 To 36` | C# 关系模式 `x is < 1 or > 36`（无新运算符）；`..` 是切片不是区间 | **需桥接（可借鉴）** | 语义同向——区间测试不该是序列 / 运算符；C# 用组合比较实现，VBScript.NET 可照此把 E 定义为"区间对象的库调用 / 比较组合糖"，而非运算符 |
| `1 To 36` 的区间契约 | `System.Range` 明确是"切片索引"、数值区间走关系模式 | **需桥接** | C# 已替此契约做了裁决：区间 ≠ 集合；range-expressions 若做成带 `Contains` / `IsInInterval` 的一等区间类型，与 C# 关系模式语义可互通 |
| `Option Compare` / 字符串元素分叉 | C# `string.Contains` 序数比较（BCL），无 `Option Compare` | **脱节（VB 特有）** | C# 无此分叉——成员测试一律走 BCL 序数比较；VB 的 `=` 语义差异是 VB 侧问题，C# 帮不上忙，反而是 A 的额外否决票 |

**定性**：A 与 `NotIn` 与 C# 生态方向冲突（C# 不做的糖、不造的关键字，VB 再造 = 双语差）；E 与 C# 关系模式语义**同向**，是唯一可与 C# 对齐的碎片；`Option Compare` 分叉属 VB 特有，C# 无对应物。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：若未来重开成员测试，静态绑定应默认走 BCL `Contains`（`List` / `HashSet` / `Enumerable`），与 C# 共享同一套成员测试 API（零新元数据）；`Option Strict Off` 下 `coll As Object` 才走晚期绑定 `Contains`。这与决策文件 M2 的"默认安全、按需动态"原则一致。
- **source-gen / analyzer 桥**：C# 生态对"只是短一点"的糖的默认答案是 **API + 代码样式工具**，参照 vblang 主线的原话：

  > "If the API support is good enough, don't do the language work in VB." — `meetings\2018\vbldm-notes-2018.02.28.md`（vblang 主线 LDM，对 C# Range 的立场；正文第二句还提及"may come out reading more "VB like" than the dot dot range syntax"）

  VBScript.NET 可先给 `.Contains(` / `IndexOf(...) >= 0` 的改写建议做一个 analyzer / 代码样式规则（即会议追问 #8 返工第一项），把"语言特性"降级为"可编译的建议"，成本远低且不破坏兼容。
- **区间判定走关系模式对齐**：E 落地前，先看 C# 的 `x is >= 1 and <= 36`。VBScript.NET 若给 `x NotIn 1 To 36` 糖衣，语义模型应定义为"区间对象上的 `IsInInterval` / `Contains` 调用"（对齐上述 API 优先），或直接吃 VB 的比较表达式——避免引入 `In` 运算符符号。
- **识别新元数据**：本提案自身不产生新元数据，但 VBScript.NET 编译器必须**认识 C# `in` 参数的元数据**（`[IsReadOnly]` + `modreq(InAttribute)`，见 `proposals\csharp-12.0\ref-readonly-parameters.md`），否则调用 C# 库时会把 `in` 参数误当 `ByRef`。这是 VB 侧真正需要桥接的互操作点（属决策文件 M3，非本提案专属，但 `In` 关键字重名会放大混淆）。

### 对既有 RESOLUTION / 三态判定的影响

无推翻；C# 现实**强化**而非改变现有判定：

- A 否决：C# 生态同样不提供成员测试运算符，印证"更短 ≠ 更有表达力"；VB 造糖 = 与 C# 双语分叉，是额外一票。
- `NotIn` 关键字否决：C# 9 的 `not` 模式证明"否定用组合"是生态共识，不是 VB 特色的妥协。
- E 搁置：C# 关系模式表明区间测试**语义上不依赖集合 / 序列**，与"`1 To 36` 契约必须先定"的结论一致；C# 用组合比较而非 `Contains` 映射，进一步说明 E 的正确 lowering 是区间对象库调用，不是成员测试。
- 三态 Table 维持。

### 引用纪律

- 本附录逐字引用的 C# 原文全部经 `..\..\csharplang` 镜像核实，路径见上；vblang 一句经 `..\..\vblang\meetings\2018\vbldm-notes-2018.02.28.md` 核实。
- `x in list` 在 C# 为语法错误——C# 文法事实（`in` 仅出现在参数 / `foreach` / 查询语法面，非二元运算符），非逐字引用，标 **Probably**。
- `Enumerable.Contains` / `List(Of T).Contains` / `HashSet(Of T).Contains` / `string.Contains` 属 **BCL API 面**（System.Linq / System.Collections.Generic），不在 csharplang 仓库，故不附 csharplang 路径；跨框架 `String.Contains(String)` 重载存在性即会议追问 #2 所述，属 .NET API 面而非 CLR 面。
- `OPEN QUESTIONS`：csharplang 仓库未检索到"是否给 `Contains` 加语言糖"的专题 LDM 记录（Grep 无命中），本附录以 patterns3.md 的"组合器用于测试值的范围"作最近参照；若需 C# LDT 对该问题的专门表态，属 **OPEN QUESTIONS**。
