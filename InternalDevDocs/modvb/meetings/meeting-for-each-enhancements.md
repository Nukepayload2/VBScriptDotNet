# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上一场我们把 `local-declarations` 的三件套拆成四份各自裁定，这次同一把剪刀又派上用场：本建议把四样东西捆在一起——`For Each` 元组解构、`Exit For` / `Continue For` 显式变量、循环头内联 `Where` 过滤、`Await Each`。四样的血缘、主线状态与成本画像完全不同，不能同生共死。

而且本场与队列里的姊妹会议 `for-enh` 共享一片地盘：`Exit For x` / `Continue For y` 同时出现在两份建议里。我们只从 `For Each` 侧裁定，跨 `For` 的一致性由 for-enh 那场确认。`Await Each` 则与组 13 队列的 `async-iterator` 建议（生产者侧 `Async Iterator Function`）构成消费/生产对称，本场只定消费端方向，落地依赖那场。

## Agenda

* [Proposal - For Each 增强](#proposal---for-each-增强)

## Proposal - For Each 增强

_Related: [vblang #48 – Multiple `For` or `For Each` Control Variables Per Statement](https://github.com/dotnet/vblang/issues/48)；[vblang #104 – Extend `For Each` Statement with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)；[vblang #186 – `Exit For j` Statement to Break Out of Nested `For` and `For Each` Loops](https://github.com/dotnet/vblang/issues/186)；主线 2016-05-06 元组设计笔记（`For Each (x, y) In ...` 括号定案）；ModVB：`proposal-for-enhancements.md`（姊妹，共享 `Exit For`/`Continue For` 显式变量）、`proposal-async-iterator.md`（`Await Each` 的生产者侧）、`proposal-pipeline-operator.md`（`Where ch -> ...` 的 `->` 依赖）、`proposal-query-enhancements.md`（`From x, y In ...` 无括号解构的先例）_

### 场景与缺口

We opened with the four pains the proposal names, and we do not dispute that they are real.

第一，迭代字典想同时拿键和值。今天必须 `For Each kvp In dictionary` 再 `kvp.Key` / `kvp.Value`——两行样板，每轮都写。

第二，嵌套 `For Each` 无法直接退出/继续指定层。今天的 `Exit For` / `Continue For` 只能作用于最内层；想跳出外层只有两条路：`Exit For` 只跳一层、或者动用 `Goto`——而主线 2017-12-06 讨论 #186 时已经明说 `Goto` 是 code-smell：

> "The real motivator for this is that `Goto` is a code-smell and people want a less smelly solution to this problem."

第三，简单过滤必须写成 `For Each ... If ... Then` 或引入 LINQ 查询。`str.Where(...)` 一次调用还好，但每个过滤项都要包一个 `Function(c) ...` 闭包。

第四，异步流（`IAsyncEnumerable`）尚无循环语法。今天的消费方式是 `Await stream.ToListAsync()` 一次性物化，或手写 `MoveNextAsync` 状态机。

But——这是本纪要的第一条结构观察——**这不是一个特性。** 四件事里，有两件是主线已经批准的原则（#48 多控制变量、#186 `Exit For j`），一件与主线 #104 相关但主线未裁决（循环头查询理解），一件完全无主线先例（`Await Each`）。把它们捆在一起，等于让已批准项为未裁决项背书，让未裁决项为无先例项背书。We will split them.

### 候选方案

**PROPOSAL A — 元组解构，无括号（Anthony 原文形态）。**

```vb
For Each key, value In dictionary
    Console.WriteLine($"{key} = {value}")
Next
```

**PROPOSAL B — 元组解构，带括号（2016-05-06 主线定案形态）。**

```vb
For Each (key, value) In dictionary
    Console.WriteLine($"{key} = {value}")
Next
```

**PROPOSAL C — 多控制变量 / 解构合并设计。** 不把解构当作独立语法，而是作为 #48"多控制变量"的 `For Each` 情形：`For Each key, value In dictionary` 的逗号就是 #48 的逗号列表，语义定义为"逐元素位置解构"。One syntax, one feature.

**PROPOSAL D — `Exit For` / `Continue For` 显式变量。** `Continue For child`、`Exit For parent`，即 #186 原文（已 Approved-in-Principle，且含 `Continue For`）。单变量，拒绝逗号列表（2017-12-06 对 `Continue For x, y` 已明确 "No."）。

**PROPOSAL E — 带标签循环（Java 风格）。** #186 讨论过：标签可读性更好，但"doing the proposal doesn't preclude us doing what Java does later"——先做变量命名，标签留作未来增量。

**PROPOSAL F — 循环头内联 `Where` 过滤（含 `->` 管道）。**

```vb
For Each ch In str Where ch -> Char.IsDigit()
    Console.Write(ch)
Next
```

**PROPOSAL G — 循环头查询理解全量（#104 范围）。** `Where` 之外再扩 `Order By` / `Select` 等运算符。We rejected this candidate early——`Select` 在循环头里改变循环体所见的值，是"投影式迭代"，语义与 `For Each` 的"逐元素访问"根本不同，是另一个特征。

**PROPOSAL H — 不做查询理解，维持 LINQ / `If ... Then`。** 过滤用现有两条路。

**PROPOSAL I — `Await Each` 消费 `IAsyncEnumerable`（新语句形态）。**

```vb
Await Each item In sequence
    Await HandleAsync(item)
Next
```

**PROPOSAL J — 复用 `For Each` 模式检测，不引入 `Await Each` 字样。** 让 `For Each x In asyncSequence` 自动识别 awaitable 枚举器。We rejected this candidate early——"细微字符改变语义"正是设计原则所禁（`For Each` 静默变成异步迭代是隐蔽语义变化）；异步必须显式。

**PROPOSAL K — 什么都不做，`For Each x In Await sequence.ToListAsync()`。** 一次性物化，语义正确但失去流式价值。

### 权衡：Q&A

**Q1：解构与 #48 的逗号是同一个特征吗？—— 这是本场最重的一问。**

主线 2017-12-06 对 #48（多 `For` 或 `For Each` 控制变量）的记录是：

> "**Approved-in-Principle**. None of us could think of a good reason why this doesn't already work. Allowing the `For Each` case is virtually required by #104."

同一场记录里，主线给 VB 的逗号列表先例划了界：

> "VB already has a strong precedent for multiple consecutive statements/constructs of the same kind being combinable into a comma-separated list: `Imports`, `Dim`, `Next`, `From`, `Let`, `Case` (required in this case)."

而 2016-05-06 的元组笔记早已把"解构"这一概念写进了 `For Each` 子句，用的正是 `For Each (x, y) In GetPoints()`。把三份记录放在一起看：**`For Each key, value In dictionary` 这个语法，就是 #48 的"多控制变量 + 逗号列表"，只是把语义定义为位置解构。** 提案把它写成"元组解构"而完全不提 #48，等于把同一个特征的两个名字摆成了两个特征。We think the honest design is **PROPOSAL C**：解构不是新语法，是 #48 的 `For Each` 情形。

那么 2016 年括号定案怎么办？2016 笔记自己都自疑过：

> "Question: Superfluous parentheses seem not very VB-ish." → "Yes, but they need parenthesis. Resolves some ambiguities, such as the last example, which would be a breaking change."

括号解决的是歧义——而歧义的来源是"逗号在其他上下文（`Dim`、`Let`、`Select` 投影）里已有别的含义"。上一场 `local-declarations` 会议否决 `Dim suit, rank = GetCard()` 无括号解构，正是因为它改写 `Dim` 逗号列表的既有语义。**但 `For Each` 今天没有逗号列表语义**——`For Each x, y In c` 今天就是语法错误，不存在"把既有合法代码改成另一个含义"的问题。错误变程序，无破坏。这条把 For Each 与 `Dim` 彻底区分开了：括号在 `Dim` 里是保护既有语义的护栏，在 `For Each` 里是多余的装饰。我们因此接受无括号形式，并在 RESOLUTION 里显式声明对 2016 括号定案的偏离**仅限 `For Each` 子句**。

**Q2：一旦 #48 落地，逗号在 `For Each` 里就有了正式语义——两条路必须合并成一条。**

这是 Q1 的推论，也是我们反复回访的点。若本建议的解构与 #48 的多控制变量各自进语言，同一个 `For Each x, y In ...` 会被两份 spec 各定义一次——那就是真正的灾难。所以解构项**必须以 #48 的形态落地**，语义统一定义为：单 `In` 源，逐元素位置解构。`For Each` 里不存在"两个独立枚举"的读法——只有一个 `In` 子句，一个枚举器，多个名字只能绑定到元素结构上。若未来有人想要 `For Each x In a, y In b`（两个集合并行），那是另一个特征，必须另立语法（如元组范围），不能占用这个逗号。`Probably`：主线不会反对这个合并，但 #48 的 issue 正文我们无法核实其 `For Each` 情形的精确语义，这点留给 spec 定夺。

**Q3：解构出的变量是只读的吗？名字如何标识循环？**

VB 的 `For Each` 控制变量今天**只读**——循环体内不能赋值。解构出的 `key`、`value` 必须沿用同一规则，否则 `For Each` 变量语义被悄悄改写。名字标识循环：解构后一个循环有多个名字，`Exit For key` 和 `Exit For value` 必须都指向同一个循环。`#186` 的整件事就是"用控制变量名标识循环"，解构让"一个循环多个名字"成为新角案例，spec 必须写死"同循环的所有控制变量名等价"。

**Q4：`Exit For` / `Continue For` 显式变量——主线已批准，我们只需确认边界。**

#186 的记录：

> "**Approved-in-Principle, also do this for `Continue For` statement**."

讨论里否决了逗号列表：

> "**No**. In fact, for the `Continue` case that's not at all what's happening. One isn't continuing the inner loop _then_ continuing the outer one; one is simply skipping to the next iteration of the outer loop."

我们完全同意单变量。`Continue For child` 是"跳到 `child` 层的下一轮"，不是"继续 child 再继续外层"——逗号列表在这两个读法下都讲不通。标签循环（PROPOSAL E）主线没否决（"Maybe, but doing the proposal doesn't preclude us doing what Java does later"），我们同样留作未来增量，不进入本建议。与 for-enh 的共享：`Exit For` / `Continue For` 的解析与名字解析规则（重名优先级）应在一份 spec 里写，跨 `For` / `For Each` 一致。

**Q5：`Where` 子句的 `->` 依赖是多余的载荷。**

`For Each ch In str Where ch -> Char.IsDigit()` 的 `->` 是 ModVB 管道运算符（`proposal-pipeline-operator.md`，Anthony 原文第 8 章）。但 `Where` 子句要的是一个**布尔谓词**——`ch -> Char.IsDigit()` 在管道语义下等于 `Char.IsDigit(ch)`，纯属绕路。同一个过滤用普通布尔表达式完全等价：

```vb
For Each ch In str Where Char.IsDigit(ch)
    Console.Write(ch)
Next
```

不依赖 `->`，去掉一个未落地特征的耦合。至于为什么要 `Where` 而不是 `If ... Then` 或 LINQ——Anthony 原文（L2636）给了一个更硬的论据：**融合优化**：

> "Optimized imperative generation of `For Each ... Where ...`. (When appropriate, i.e. in-memory, not `IQueryable`, well-known methods.)"

即 `For Each x In c Where p` 降级为一条融合循环，**不分配 LINQ 枚举器、不分配闭包**。这是"优化而非特性"——设计原则里的美德。但注意引文里的限定："in-memory, not `IQueryable`"。一旦源是 `IQueryable`（LINQ to SQL 等），`Where` 必须翻译回 `Queryable.Where` 否则会错误地在内存里过滤；v1 只该覆盖内存源，其余报诊断引导到 LINQ。

**Q6：`Await Each` 该不该有独立语法？**

`For Each` 静默支持 awaitable 枚举器（PROPOSAL J）是隐蔽语义变化——被拒。`Await Each` 作为显式措辞，读起来像英语，是极 VB 的。但成本与依赖都重：① 必须在 `Async` 方法内；② 底层 `GetAsyncEnumerator` / `MoveNextAsync` / `DisposeAsync` 的状态机与模式检测是整块新实现；③ 与组 13 队列的 `async-iterator`（生产者侧）是同一枚硬币的两面，单独落消费端没有意义——没有生产者，`Await Each` 消费谁？主线会议记录里对 `IAsyncEnumerable` 没有任何讨论（我们对 `..\..\vblang\meetings/` 全量检索未命中），C# 8 的 `await foreach` 是外部队标（`Suspect`：vblang 记录中无对应条目，方向一致但不可在本仓库核实）。这是 Anthony 独立延伸，且依赖未落地的姊妹建议——本场只能定方向。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

- **解构逗号**：`For Each x, y In c` 今天语法错误；#48 落地后逗号是"多控制变量列表"。无歧义，前提是 Q2 的合并设计——两份 spec 不得各自定义同一个逗号。
- **`Where` 子句**：`For Each ch In str Where ...` 中，`Where` 在完整表达式之后出现。今天 `Where` 不是运算符，此位置报错；作为上下文关键字引入子句，parser 用"`In` 表达式之后遇 `Where`"判定。无歧义，但需要文法（BNF）。
- **`Await Each`**：`Await` 是 Async 方法内的上下文关键字；`Await Each item In sequence` 必须与 `Await <表达式>` 区分。`Each` 是 `For Each` 的上下文关键字，组合 `Await Each` 需 parser 联合判定。可定义，必须写进文法。
- **无 body 区分**：三种形态都靠关键字（`Where` / `Each`）引入，不依赖缩进；`For Each ch In c Where p : body` 冒号单行形态的优先级需在文法里明确。

#### 2. 角案例与边界语义

- **解构数量不匹配**：`For Each key, value In dictionary` 中元素若不是二元结构（无 `Deconstruct`、非元组、非 `KeyValuePair`），是编译错误；三元结构绑两个名字也错误。解构依赖 `Deconstruct` 或元组转换（2016 笔记已定元素级转换规则），对自定义类型需要显式 `Deconstruct` 方法——这正是建议 Drawbacks 承认的"依赖 `Deconstruct`"。
- **零元素 / 空序列**：`For Each` 循环体一次不执行，解构名字不产生运行时访问；无特例。
- **解构变量只读**：Q3——`key`、`value` 循环体内不可赋值，与现行 `For Each` 变量一致。
- **`Exit For` 命名解构循环**：`Exit For key` 与 `Exit For value` 等价；两者都退出同一个循环。若解构名字与外层循环变量重名（如外层也有 `key`），名字解析优先级必须定义——`Probably`：就近原则，内层先匹配。
- **`Where` 融合 + `Exit For`**：`Exit For` 提前退出不破坏融合优化（`For` 循环的 `Exit` 本就有）。空过滤结果 = 循环体零次执行，无特例。
- **`Await Each` 空序列 / 提前退出**：提前退出必须 `DisposeAsync` 底层枚举器（C# `await foreach` 的既定行为）；取消（`CancellationToken`）与 `ConfigureAwait` 的传递规则待 spec。
- **`Await Each` 与 `Exit For`**：`Exit For` 在异步循环里同样成立，代价是 DisposeAsync 调用点。

#### 3. 作用域与绑定

- 解构出的 `key`、`value` 是循环局部变量，语义模型返回 `LocalSymbol`；作用域与现行 `For Each` 变量一致（整个循环）。2016 笔记对元组名字有一段警示："names are associated with declarations, not values"——晚期绑定场景下元组名在运行时不可见。解构是编译期动作，不经过晚期绑定，`key`/`value` 是编译器合成的位置组件名，无需运行时元组名支持。
- `Where` 子句的过滤表达式绑定到循环变量 `ch`（类型 = 源元素类型），语义模型里 `ch` 在 `Where` 谓词与循环体内都可解析。
- `Await Each` 的 `item` 同样是循环局部变量，类型 = `IAsyncEnumerable(Of T)` 的 `T`（或模式检测的枚举器元素类型）。

#### 4. 与既有特性的交互

- **#48 多控制变量**：最大的交互面，Q1/Q2——同一逗号两份 spec 是唯一的"禁止项"。
- **`For Each` 变量只读**：解构变量继承；`Where` 过滤不改变只读性；`Await Each` 的 `item` 同样只读。
- **lambda 捕获**：VB 的 `For Each` 循环变量在捕获时是否逐迭代生成新副本，我们 `Suspect` 现行行为与 C# 5 后一致（逐迭代），但未核实；for-enh 建议里 `For` 计数变量的按迭代捕获修复（BC42324）与本建议无关，但 `For Each` 侧若行为不一致会分裂。留给 spec 核实。
- **LINQ / 查询表达式**：`Where` 循环头与 `From ... Where ...` 查询重叠（原则 #3 的风险面）；融合优化是唯一的辩护，见 Q5。
- **`Using` / `SyncLock` 内的 `Await`**：现行 `Await` 在 `SyncLock`/`Using`（无 `DisposeAsync` 形态）内受限，`Await Each` 继承同一套约束。
- **for-enh 的 `For` 多计数器**：`Exit For z` 在多计数器 `For z = 0 To ..., y = 0 To ...` 里用变量名标识层；For Each 解构里多个名字标识同一层。两种"多名字"语义不同（For=嵌套层，For Each=同一层的多个名字），spec 必须分写清楚。

#### 5. Breaking change 与兼容性

四件套今天全部是语法错误：`For Each x, y In c`、`Exit For child`、`For Each ... Where ...`、`Await Each ...` 在现行编译器里都不接受。**错误变程序，无既有代码破坏**——这是本建议最干净的地方，也是它区别于 `local-declarations` 的 `Let`（查询子句回归面）的关键。风险只有一个：若 #48 的解构/多控制变量落地，而本建议的解构**不**与它合并，未来 `For Each x, y In c` 会被两份 spec 定义两次——这不是"破坏旧代码"，而是"自造歧义"。合并即消除。`Where` 融合对 `IQueryable` 源的错误降级是行为风险，v1 用"仅内存源 + 其余诊断"规避。

#### 6. Option Strict / 编译选项分叉

- **解构**：`For Each key, value In dictionary` 在 `Option Infer On` 下推断 `key As String`、`value As Integer`；`Option Strict Off` + 无显式类型时 `key`/`value` 为 `Object`/`Any`（与 typeless-declarations 建议交互）。严格/宽松两路径的类型推断必须逐位一致。
- **`Where`**：谓词必须 `Boolean`；宽松模式下非布尔谓词走现行强制转换规则，与 `If ... Then` 的行为对齐。
- **`Await Each`**：异步模式检测（`GetAsyncEnumerator`）与 `Option Strict` 无关，但宽松模式下元素类型推断需验证。建议对两路径只字未提——必须补。

#### 7. IDE / IntelliSense 影响

- 解构变量的补全与 InfoTip：`key` 显示 `String`（从 `KeyValuePair(Of String, Integer).Deconstruct` 推导）；`For Each (key, value)` 括号形式的语法高亮需与 `Dim` 解构一致。
- `Where` 子句的补全：过滤表达式里 `ch` 可用；错误文案（"`Where` 谓词必须返回 Boolean"）需新设计。
- `Await Each` 的 async 上下文提示：非 `Async` 方法里输入 `Await Each` 应立即报"此上下文无 Await"。这些都要原型验证，不做进规范等于没设计。

#### 8. 数据 / 普遍性

字典迭代是 VB 业务代码最高频的 `For Each` 场景之一，解构的普遍性我们有信心；嵌套循环命名跳转是真实痛点（`Goto` code-smell 引文即证）。但 Where 过滤与 Await Each 没有量化数据——对照 Implicit-default-optional 的 85% 统计标准，这里一条都没有。We are not saying the pain is fake; we are saying we cannot measure it. 主线对 #48/#186 的批准是唯一的外部信号：主线团队认为多控制变量与命名跳转"没有理由不成立"。

#### 9. 更简替代

- 解构：`For Each kvp In dict` + `kvp.Key`/`kvp.Value` 已存在，两处成员访问；`For Each (key, value) In dict`（2016 定案）已存在。无括号形式的增量是**少一对括号**——诚实地说，这是本场所有增量里最小的一个，但它同时解锁 #48 的 For Each 情形，价值不在括号而在统一。
- `Exit For` / `Continue For` 显式变量：`Goto` 存在但臭；`Exit For` 单层存在但不够。这是没有更简替代的项。
- `Where`：LINQ `Where` 与 `If ... Then` 存在；增量是融合优化的分配消除 + 少一层缩进。
- `Await Each`：`For Each x In Await src.ToListAsync()` 存在；增量是流式（不物化全部）+ 逐元素 await。
- 一个尖锐的总结：**四块里解构的"更简替代"几乎就是现状本身，Exit/Continue 的替代是 Goto（臭），Where 的替代只差性能，Await Each 的替代丢流式。** 这决定了优先级排法。

#### 10. 复杂度 / 成本 / 优先级

解构（合并 #48）与 Exit/Continue（#186）是主线已批准项，实现面小（parser + 绑定 + 语义模型），无 CLR 介入——这是第一梯队。`Where` 融合是中量：新子句文法 + 降级优化 + 边界（`IQueryable`），且依赖一个未落地的 `->` 可选耦合——第二梯队。`Await Each` 是大：异步状态机 + 模式检测 + Dispose 语义 + IDE，且依赖组 13 的 async-iterator——第三梯队，成本不与本建议的收益匹配，而与异步迭代生态决策匹配。

#### 11. 运行时 / CLR 硬约束

无。解构、`Where`、Exit/Continue 全是编译期概念；解构降级为"取元素 + 按 `Deconstruct` 拆字段/`Item1`/`Item2`"，`Where` 融合降级为 `For` 循环内 `If`，`Exit For` 是既有跳转。`Await Each` 使用 `IAsyncEnumerable` / `IAsyncEnumerator`（既有运行时类型）与异步状态机，无 PEVerify 问题。无表达式树问题（解构不进表达式树，与匿名类型同理）。

#### 12. 值不值得做

- 价值：解构（高频样板）+ Exit/Continue（消 Goto）+ Where（优化）都是真实增量；Await Each 方向正确但依赖未定。
- 成本：第一梯队小，第二梯队中，第三梯队大。
- 风险：四件套捆绑是唯一结构风险；合并 #48 后语法歧义消除；`IQueryable` 降级有规避。
- **结论：拆开做。** 已批准项先行，优化项限定后行，依赖项等生态。若坚持四件套同生共死，我们会建议不做——那会把已批准项拖在未裁决项后面。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）**——四件套全部 error→program，通过。这是本建议最强的一张牌。
- **原则 2（保持 VB-like）**——`Await Each item In sequence` 读起来像英语，是极 VB 的措辞；`For Each key, value In dictionary` 的逗号列表是 VB 自身的先例（#48 引文里 `Imports`/`Dim`/`Next`/`From`/`Let`/`Case` 都是 VB 血缘）。
- **原则 3（不引入第二种做事方式）**——唯一撞墙处是 `Where`：循环头过滤 vs LINQ `Where` vs `If ... Then`。辩护只有一条：融合优化让循环头过滤"不是第二种写法，是第一种写法的免费加速"。若没有性能论据，`Where` 就是纯粹的语法重叠。
- **原则 4（默认跟随 C#，除非有充分理由）**——解构与 Exit/Continue 无 C# 对应（C# 没有变量命名循环），VB 走自己的 #48/#186 主线；`Await Each` 与 C# 8 `await foreach` 方向一致但措辞 VB 化（`Await` 前置，符合 VB 的英文语序）。
- **原则 5（读起来像英语、对新手友好）**——`Await Each item In sequence`、`For Each key, value In dictionary` 都是祈使句，无需解释。
- **原则 6（不为边缘场景加特性）**——字典解构与嵌套跳转不是边缘；Where 与 Await Each 缺普遍性数据，是扣分点。
- **原则 7（避免隐蔽控制流/语义变化）**——全部显式关键字（`Where`、`Await`），无细微字符变义；J 方案（静默 awaitable）被拒正是这条原则。通过。
- **原则 8（不与既有语法冲突）**——合并 #48 后无冲突；`Await` 与 `Each` 的组合是唯一新语境，parser 联合判定即可。
- **原则 9（消除常见样板）**——字典 kvp 样板、嵌套 Exit 样板、过滤样板、异步流样板，四件全中。这是本建议最亮的地方。
- **原则 10（冗长只在有用时是美德）**——`Where`/`Await` 都是显式关键字，在"让意图可见"处保留，符合。

对照主线表（2.3）：**"多 `For` 变量 / `Exit For j`"一行，主线 "Approved-in-Principle"、Anthony "直接采用"、关系 "一致"**——本建议的 Exit/Continue 与解构（合并 #48）正是这一行的落地。`Where` 对应主线 #104（未决）。`Await Each` 在对照表无对应行，是 **Anthony 独立延伸**（方向与 C# 一致）。解构的括号问题：2016 定案要求括号，本建议无括号是**对主线定案的显式偏离**，必须声明而非默认。

### RESOLUTION:

**RESOLUTION:** We split the document into four items and rule on each separately. 主线 2017-12-06 已批准的 #48 / #186 是锚点，不允许被未裁决项拖累。

1. **元组解构 / 多控制变量 — `Active`（合并 #48 设计）。** 采纳 `For Each key, value In dictionary`，但**不是**作为独立"解构语法"，而是作为 #48 多控制变量的 `For Each` 情形：逗号 = 多控制变量列表，单 `In` 源，语义 = 逐元素位置解构。2016-05-06 的括号定案在 **`For Each` 子句内**被本决定显式取代（documented deviation），原因：For Each 今天无逗号列表语义，无括号形式是 error→program，不存在 `Dim` 侧的保护既有语义问题。`Dim`/`Let`/`Select` 等其它子句维持 2016 定案（括号必须），与 `local-declarations` 会议一致。解构变量只读，作用域 = 循环。
2. **`Exit For` / `Continue For` 显式变量 — `Active`（#186，与 for-enh 同案）。** 单变量命名循环（`Exit For parent` / `Continue For child`），拒绝逗号列表（2017-12-06 对 `Continue For x, y` 已明示 "No."）。解构循环的多个名字指向同一循环，spec 写明"同循环所有控制变量名等价"。标签循环留作未来增量。与 for-enh 共享解析与名字解析 spec。
3. **循环头 `Where` 过滤 — `Consider`（限定范围）。** v1 仅支持 `Where` 单一子句 + 布尔谓词；**取消 `->` 管道依赖**（`Where Char.IsDigit(ch)` 即可，`->` 是多余的载荷）；仅对内存源（数组、`List(Of T)`、`IEnumerable(Of T)`）做融合降级（不分配 LINQ 枚举器/闭包，依据 Anthony 原文 L2636 的 "in-memory, not `IQueryable`" 限定）；`IQueryable` 等需查询翻译的源报诊断引导到 LINQ。`Order By`/`Select` 全量查询理解（#104 范围）不进 v1。复活信号：① 融合降级的性能基准数据；② 用户对循环头过滤的量化需求；③ 管道运算符 `->` 是否落地。
4. **`Await Each` — `Table`（依赖 async-iterator）。** 方向正确、措辞极 VB，但消费端语法必须与组 13 的 `async-iterator`（生产者侧）联合设计——没有生产者，消费端无从落地。无主线先例（vblang 会议记录零命中 `IAsyncEnumerable`），C# 8 `await foreach` 是外部队标。落地前置：async-iterator 原型完成、模式检测（`IAsyncEnumerable(Of T)` vs 鸭子类型 `GetAsyncEnumerator`）定案、`DisposeAsync`/取消/`ConfigureAwait` 语义写入 spec、非 `Async` 方法的诊断策略。

**Implication:**

- 返工建议原文：把四块拆成四份文档或显式切分章节；解构与 #48 合并为一个"多控制变量 / 解构"特征。
- 与 for-enh 对表：`Exit For`/`Continue For` 显式变量的一份共享 spec（解析、名字解析优先级、跨 `For`/`For Each` 一致）；多计数器 `For`（层）与解构（同一层多名字）的语义差异分写清楚。
- 与 async-iterator 对表：`Await Each` 消费端与 `Async Iterator` 生产者端联合设计，本建议不抢先落消费端。
- 与 pipeline-operator 对表：`Where` 子句取消 `->` 依赖后，与管道运算符解耦。
- 最小原型：解构（合并 #48）+ Exit/Continue + 语义模型 + IDE 补全验证；Where 融合的降级代码生成与 `IQueryable` 诊断。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 文档整体 `Consider`；其中元组解构/多控制变量 `Active`（合并 #48）、`Exit For`/`Continue For` 显式变量 `Active`（与 for-enh 同案）、`Where` 过滤 `Consider`（限定范围）、`Await Each` `Table`（依赖 async-iterator）。PROPOSAL B（括号形式）、E（标签循环）、G（全量查询理解）、J（静默 awaitable）按"否决/暂缓的备选方案"留档。

### OPEN QUESTIONS / TODO / Follow-up

- [ ] #48 issue 正文对 `For Each` 情形的精确语义（位置解构 vs 其它读法）——本仓库无法核实，需向主线确认或由 spec 定夺。
- [ ] 解构对自定义类型的 `Deconstruct` 要求与元组转换的精确规则；`KeyValuePair(Of TKey, TValue)` 是否语言内置识别（倾向：是）。
- [ ] `For Each` 循环变量在 lambda 捕获中是否逐迭代生成新副本（`Suspect` 与 C# 5 后一致，未核实）；若否，与 for-enh 的捕获修复是否同案处理。
- [ ] `Exit For key` 命名解构循环时，`key` 与外层同名控制变量的名字解析优先级（倾向：就近原则）。
- [ ] `Where` 子句的文法（BNF）与单行冒号形态优先级；`IQueryable` 源的诊断文案。
- [ ] `Await Each` 的模式检测范围（`IAsyncEnumerable(Of T)` 与鸭子类型 `GetAsyncEnumerator`）、`DisposeAsync` 调用点、`CancellationToken`/`ConfigureAwait` 传递——随 async-iterator 定案。
- [ ] `Await Each` 在 `SyncLock`/`Using` 内是否继承现行 `Await` 的禁用约束。
- [ ] 融合降级的性能基准（对照 LINQ `Where` 的分配与委托调用），为 Where 项的普遍性/效果补证据。

### 状态

- **LDM 状态：`Consider`**；元组解构/多控制变量子项 `Active`（合并 #48 返工后）、`Exit For`/`Continue For` `Active`（与 for-enh 同案）、`Where` 过滤 `Consider`（限定范围）、`Await Each` `Table`（依赖 async-iterator）。
- **三态判定：Consider** — 两个主线已批准项值得先行落地，优化项限定后可做，异步项等生态；四件套各自得其所。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-for-each-enhancements.md` — `For Each` 增强（元组解构 + `Exit For`/`Continue For` 显式变量 + 循环头 `Where` 过滤 + `Await Each`）。
- 来源：Anthony 原文第 3.5 节 "For Each"（`..\AnthonyDesign_wordpress.txt` L927–959，四段示例原样摘录；L2636 融合优化主张；`Query Operators in For Each` 演示视频链接）。
- 配方目标：字典解构免 kvp 样板、嵌套循环命名跳转消 Goto、循环头内联过滤、`IAsyncEnumerable` 消费语法。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **3/5** | 锚点 3："主效果显现但关键子效果缺失/消退"。Motivation 四点痛点真实；Exit/Continue 有主线批准背书（#186）；但解构示例与 #48 逗号语义的合流未识别，Where 的 `->` 依赖与融合优化未论证，Await Each 语义未定型；无仓库内原型（状态行占位链接；唯一"运行"证据是外部 YouTube 演示，未在仓库复现） | 已检查 | 无原型封顶 3；Await Each 因依赖未定效果悬置；Where 无性能基准 |
| 特性 | **3/5** | 锚点 3："明显借鉴外部/独立延伸但做了 VB 化改造，或打包了次要无关能力"。Exit/Continue 与多控制变量是主线 #48/#186 直接采用（主线一致）；解构是对 2016 括号定案的 Anthony 变体；Where 借鉴 LINQ + 管道 `->`（VB 化不完整）；Await Each 是极 VB 措辞但无主线先例；四块捆绑血缘混杂 | 已检查 | 解构偏离 2016 定案未声明；`->` 是未落地的外来载荷；四块同捆稀释了特性纯度 |
| 品质 | **3/5** | 锚点 3："缺某一章节或在关键处边界含糊"。六章节模板完整、示例与 Anthony 原文 3.5 逐字一致、3 个未决问题具体诚实（1–3 健康区间）；但无文法（BNF）、无 Compatibility/breaking-change、无 Option Strict 分叉、未与 #48/#104/2016 括号定案建立关联（最大的概念缺失）、`->` 依赖未声明、状态行占位链接 | 已检查 | 红旗：无文法；无兼容性分析；解构与 #48 的逗号合流这一核心语义风险未在 Drawbacks 中识别 |
| 属性 | **3/5** | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。对 VBScript.NET：解构 + Exit/Continue 加速迭代（雷/水正向）、消 Goto 强化循环 DX（光）；但四件套捆绑增加碎片化风险、Where 与 LINQ 重叠制造"第二种做事方式"（风 = 演化一致性张力）、2016 括号偏离是风（一致性）风险、Await Each 依赖未落地姊妹建议（暗），文档未对冲 | 已检查（预测待定） | 捆绑与依赖风险未权衡；暗风险无对冲设计；实际影响须"已采纳"后定 |
| 炼金成分 | **3/5** | 锚点 3："部分来源未标注；标注与影响有偏差"。主要材料可辨认：Anthony 原文 3.5、主线 #48/#186、2016 元组笔记、LINQ Where、`->` 管道；但未声明：与 #48 的语法合流、对 2016 括号定案的偏离、`->` 管道运算符依赖、C# 8 `await foreach` 平行物、与 for-enh/async-iterator 的共享特征 | 已检查 | 来源与影响有偏差；跨建议依赖未交叉引用；C# 平行物未标注 |

### 设计原则对照

- **与 VB 基因**：部分一致。Exit/Continue 与解构符合原则 #9（消样板）、原则 #1（不破坏，四件套皆 error→program）、原则 #5（`Await Each` 读起来像英语）；`Where` 撞原则 #3（第二种做事方式，与 LINQ 重叠），只有"融合优化"一条辩护；解构无括号是对 2016 定案的偏离，需按原则 #4 声明"充分理由"（For Each 无既有逗号语义）。
- **与主线关系**：**主线一致为主**——对照表 2.3 "多 `For` 变量 / `Exit For j`：Approved-in-Principle / 直接采用 / 一致"；解构是主线 2016 定案（括号必须）的 Anthony 变体（偏离但可论证）；`Where` 对应主线 #104（未决）；`Await Each` 无主线对应行，为 **Anthony 独立延伸**（方向与 C# 8 `await foreach` 一致）。与 for-enh（共享 Exit/Continue）、async-iterator（Await Each 生产侧）、pipeline-operator（`->` 依赖）交叉。
- **破坏性变更**：无——四件套今天均为语法错误，错误变程序。风险面：解构若不与 #48 合并，未来同语法两份 spec 自造歧义；`Where` 融合若错误覆盖 `IQueryable` 源会改变查询语义（v1 用"仅内存源"规避）。

### 总评

- **达成程度**：**部分达成**。Exit/Continue 与解构（合并 #48）价值真实、形态可论证、有主线批准背书；`Where` 是"优化而非特性"的有据主张但被 `->` 依赖与 LINQ 重叠拖累；`Await Each` 方向正确但依赖 async-iterator，独立落地无意义。
- **LDM 三态建议**：**Consider**。子项拆分后：元组解构/多控制变量 `Active`（合并 #48 返工后）、`Exit For`/`Continue For` `Active`（与 for-enh 同案）、`Where` 过滤 `Consider`（限定 Where + 内存融合 + 去 `->` 依赖）、`Await Each` `Table`（依赖 async-iterator）。
- **主要问题**：(1) 四件套捆绑，血缘不同、互相拖累；(2) 解构与 #48 逗号语义的合流未识别，而同语法两份 spec 是自造歧义；(3) `Where` 的 `->` 依赖未声明，融合优化主张无基准证据；(4) 无文法、无 Compatibility、无 Option Strict 分叉；(5) `Await Each` 与 async-iterator 的联合设计缺失。

### 返工建议

- **范围切分**：四块拆成四份文档或显式分节；解构与 #48 合并为一个"多控制变量 / 解构"特征文档，明确"逗号 = 位置解构"的单一语义。
- **补充章节**：文法（BNF：`For Each` 变量列表、`Where` 子句引入、`Await Each` 语句形态与 `Await <表达式>` 的区分）；Compatibility/breaking-change（解构与 #48 的合并、`Where` 的内存源限定、`langversion` 门控）；`Option Strict On/Off` + `Option Infer` 双路径验证示例；与 for-enh 的 Exit/Continue 共享 spec 章节。
- **补充证据**：最小原型（解构 + Exit/Continue + 语义模型 + IDE 补全）；`Where` 融合降级的性能基准（对照 LINQ `Where` 的枚举器/闭包分配，引用 Anthony L2636 的 in-memory 限定）；解构括号偏离 2016 定案的"充分理由"论证；`Await Each` 与 async-iterator 的联合模式契约。
- **未决问题处理**：解构与 #48 合并后，2016 括号定案在 For Each 内被显式取代（documented deviation，限 For Each 子句）；`Where` 谓词形态取普通布尔表达式（去 `->`）；`Await Each` 的模式检测范围随 async-iterator 定案；解构变量只读语义、`Exit For` 命名解构循环的名字解析、lambda 捕获行为逐迭代核实均列入 spec 待办。

---

## 附录：C# 生态与互操作考量

> 本附录把本提案的四块子项（元组解构/多控制变量、`Exit For`/`Continue For` 显式变量、循环头 `Where`、`Await Each`）逐一放到 C#/CLR/.NET 的现实走向面前，判断兼容/冲突/需桥接/脱节，并给出 VBScript.NET 的适应建议。C# 原文均逐字摘录自 `..\..\csharplang`，标注来源文件路径；无法在本仓库核实的点列入 **OPEN QUESTIONS**。

### 相关 C# 现实方向

本提案的主题几乎全部落在 C#「迭代协议」与「异步流」两条线上，另有 `params` 集合与 ref struct 迭代两个生态侧面：

1. **foreach 迭代协议（`GetEnumerator` / `Current` / `MoveNext`）**——C# 的 foreach 编译期按 §13.9.5 依次判定 collection type / enumerator type / element type：先查 `GetEnumerator()` 模式，失败再回退 `IEnumerable<T>` / `IEnumerable`。C# 9 把**扩展 `GetEnumerator`** 纳入模式：
   > "Allow `foreach` loops to recognize an extension method `GetEnumerator` method that otherwise satisfies the foreach pattern, and loop over the expression when it would otherwise be an error." → `proposals\csharp-9.0\extension-getenumerator.md`（Summary）

   其动机明确与解构、异步对表：
   > "This will bring `foreach` inline with how other features in C# are implemented, including async and pattern-based deconstruction." → `proposals\csharp-9.0\extension-getenumerator.md`（Motivation）

2. **异步流（`await foreach`，C# 8）**——C# 8 引入 `IAsyncEnumerable(Of T)` / `IAsyncEnumerator(Of T)` / `IAsyncDisposable` 与消费语法 `await foreach`，采用「模式优先、接口回退」的检测（`GetAsyncEnumerator` → `MoveNextAsync` → `DisposeAsync`），`MoveNextAsync` 的返回类型允许任意 awaitable（不只 `ValueTask(Of Boolean)`）。关键是 C# 把异步**显式化**、拒绝静默切换：
   > "To force `foreach` to instead only consider the asynchronous APIs, `await` is inserted as follows:" → `proposals\csharp-8.0\async-streams.md`（foreach 节）
   > "No syntax would be provided that would support using either the async or the sync APIs; the developer must choose based on the syntax used." → `proposals\csharp-8.0\async-streams.md`（foreach 节）

3. **foreach 控制变量解构（C# 7 + C# 14 扩展 `Deconstruct`）**——C# 7 的 Deconstruction 特性支持 `foreach (var (x, y) In ...)` 形态（正式文档在 dotnet/roslyn 的 `docs\features\deconstruction.md`，不在本镜像；`Language-Version-History.md` 记有 Deconstruction 条目）。C# 14 extensions 把扩展 `Deconstruct` 明确纳入 foreach 的模式化构造面：
   > "`GetEnumerator`/`GetAsyncEnumerator` in `foreach`" / "`Deconstruct` in deconstruction, in positional pattern and foreach" → `proposals\csharp-14.0\extensions.md`（pattern-based constructs 节）

4. **ref struct 迭代 / 低层迭代（C# 13，索引 T2）**——ref struct 现在可实现接口并参与 foreach（集合模式、枚举器模式、`IDisposable` 释放），但 **`await foreach` 仍禁止 ref struct 枚举器**：
   > "An `await foreach` statement will continue disallowing a ref struct enumerator and a type parameter enumerator that `allows ref struct`. The reason is the fact that the enumerator must be preserved across `await MoveNextAsync()` calls." → `proposals\csharp-13.0\ref-struct-interfaces.md`（await foreach 节）

   底层动机是把 `Span(Of T)` 这类「接近指针」的栈上类型纳入抽象体系（同文件 Motivation：*"The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET."*）。

5. **`params` 集合 / 非数组 `params`（C# 13，索引 T2）**——`params` 扩到 `ReadOnlySpan(Of T)` 等集合类型，且用**新元数据 `ParamCollectionAttribute`** 取代 `ParamArrayAttribute` 标记；C# 侧明确警告 VB 消费者：
   > "For example, the current VB compiler will not be able to consume them decorated with `ParamArrayAttribute` neither in normal, nor in expanded form. Therefore, an addition of 'params' modifier is likely to break VB consumers, and very likely consumers from other languages or tools." → `proposals\csharp-13.0\params-collections.md`（Metadata 节）

### 现实 vs 提案

按 RESOLUTION 的四个子项逐条对照：

**1. 元组解构 / 多控制变量（Active）——兼容，机制同源、入口不同。**
- C# 现实：`foreach (var (x, y) In dict)` 直接复用元素类型的 `Deconstruct` 模式（C# 7），C# 14 再补扩展 `Deconstruct`。C# 不做「多控制变量逗号列表」——逗号在 C# 是元组字面量的一部分，不是 #48 式控制变量列表。
- 本提案：逗号 = #48 多控制变量列表，语义 = 逐元素位置解构，底层仍依赖 `Deconstruct` / 元组转换。
- 结论：两者都想「循环头一次拆出多个名字」，且**都挂在同一个 `Deconstruct` 模式上**——这是天然的互操作接口。差别只在语法人门（C# 用括号元组、VB 用无括号逗号列表），语义模型可共享对 `Deconstruct` 的绑定。不冲突。
- 附带：C# 生态正把 `KeyValuePair` 朝「二元可解构」靠拢（`meetings\2024\LDM-2024-05-15-KeyValuePairCorrespondence.md` 主张 *"Any type that is constructible and deconstructible into two elements would be transparently supported"*，虽针对 collection expressions，方向与本提案的字典解构一致）。

**2. `Exit For` / `Continue For` 显式变量（Active）——脱节，无 C# 对应。**
- C# 现实：`break` / `continue` 只作用于最内层；无变量命名循环。C# 的 label + `goto` 是唯一「跳出多层」手段且被社区视为 code-smell。
- 本提案：消 Goto 的 VB 主线 #186，纯编译期控制流，不触碰迭代协议。
- 结论：**与 C# 完全脱节但零冲突**——不改变任何与 C# 互操作的类型/元数据/协议面。原则 4（默认跟随 C#）在本项不适用，因为 C# 没有对应物可跟随。

**3. 循环头 `Where` 过滤（Consider）——兼容、VB 差异化；融合降级的低分配方向与 C# 低层浪潮同向。**
- C# 现实：无循环头过滤；过滤的两种既有方式是 LINQ `Where`（分配枚举器/闭包）与 `If ... Then`。C# 生态的「避免分配」方向由 Span/ref struct/params span/collection expressions 承担（索引 T2），语言本身**不**把 `foreach + Where` 融合。
- 本提案：`Where` 子句 + 融合降级（不分配 LINQ 枚举器/闭包），限定内存源。
- 结论：功能上 C# 无对应（VB 差异化），但「免分配融合」与 C# 低层浪潮是**同一种价值取向**，可互为佐证。`IQueryable` 必须翻译回 `Queryable.Where` 的限制，与 C# 的 LINQ 翻译边界（内存 LINQ vs 查询翻译器）一致，照抄即可。风险在跨语言：VB 融合降级若走 `Enumerable.Where`，要消费 C# 类型时须正确解析 C# 扩展方法（含 C# 9 扩展 `GetEnumerator`）。

**4. `Await Each`（Table）——高度兼容，C# 有成熟先例可照抄。**
- C# 现实：C# 8 `await foreach` 就是「显式异步循环」，与 J 方案（静默 awaitable）被拒的决定**一字不差地一致**。
- 本提案：`Await Each item In sequence`，VB 措辞化（`Await` 前置），语义与 C# 8 相同。
- 结论：消费端模式检测（`GetAsyncEnumerator` / `MoveNextAsync` / `DisposeAsync`，模式优先接口回退）、`DisposeAsync` 调用点、取消 / `ConfigureAwait` 传递，C# 8 全部已有逐字规范可参考。**Table 状态不受影响**——本提案落地仍受制于生产者侧 async-iterator，而非 C# 侧缺口。
- 一处 C# 侧边界可直接引用：ref struct 枚举器在 `await foreach` 中被禁（见上），VB 若未来支持 ref struct 也须遵守同一限制。

### 对 VBScript.NET 的适应建议

1. **模式检测对齐（迭代协议桥）**——VB spec 的 collection-type 判定已含「instance, shared or extension method `GetEnumerator()`」（vblang `spec\statements.md` §For Each...Next，行 1349；注：spec 的现行性待核实），即 VB 已具备 C# 9 扩展 `GetEnumerator` 的同等能力。建议 VBScript.NET 的 `For Each` / `Await Each` 模式检测与 C# 规则**逐条对齐**：实例方法 → 扩展方法 → 接口回退，且 `Current` / `MoveNext` / `MoveNextAsync` 的成员资格规则一致，避免同一类型在 C# 可迭代、在 .vbx 不可迭代（或反之）的跨语言分裂。
2. **识别新元数据**——现代 C# 库会携带：`ParamCollectionAttribute`（非数组 `params`，params-collections.md 明确 VB 旧编译器无法消费，VBScript.NET 必须认识才能调用 C# 13 的 `params ReadOnlySpan(Of T)` 方法）；`allows ref struct` 泛型标志（`GenericParameterAttributes.AllowByRefLike` / `RuntimeFeature.ByRefLikeGenerics`，ref-struct-interfaces.md）与 `CompilerFeatureRequired` 系特性。不认识这些元数据，.vbx 对 C# 新库的调用会「看起来能编译、语义却错」或直接失败。
3. **默认安全、按需动态 + source-gen 桥（决策文件 M5）**——本提案本身不引入动态，但 `Await Each` 的异步状态机与 `Where` 的融合降级都是**编译期代码生成**：正好落在「.vbx 应默认编译到受管程序集 + source-gen 桥，把解释/动态做成显式 opt-in」的结论上，与 C# 的 source-gen / AOT 取向同向。
4. **融合降级与 C# LINQ 边界对齐**——`Where` 融合只对内存源生效、`IQueryable` 报诊断引导到 LINQ，与 C# 的 LINQ-to-objects / 查询翻译边界一致；实现时以 C# `Enumerable.Where` 的绑定为参照，确保对同一重载解析结果产生相同行为。

### 对既有 RESOLUTION / 三态判定的影响

- **无子项需要改变状态。** 四块的判定（解构 / Exit-Continue `Active`、Where `Consider`、Await Each `Table`）与 C# 现实不冲突，且 C# 先例对两处 `Suspect` 有实质补强：
- 正文 Q6 把 C# 8 `await foreach` 标为「外部队标（`Suspect`：vblang 记录中无对应条目）」。**此点升级**：C# 侧已在本仓库核实为原文（async-streams.md），vblang 无记录只说明 VB 主线未讨论，C# 先例现在是**已核实的一手来源**，不再是外部队标。
- 正文 OPEN QUESTION「`For Each` 循环变量在 lambda 捕获中是否逐迭代生成新副本」：C# 5 已明确改行为逐迭代新变量（`Language-Version-History.md` 行 169：*"foreach loop was changed to generates a new loop variable rather than closing over the same variable every time"*），为「`Suspect` 与 C# 5 后一致」提供了 C# 侧证据；VB 侧行为仍需 vblang / 原型核实。
- 解构项「依赖 `Deconstruct`」：C# 7 + C# 14 扩展 `Deconstruct` 证明该依赖是生态共识，不是 VB 专属包袱——可放心沿用。

### 引用纪律 / OPEN QUESTIONS

**已核实 C# 原文（本仓库 `..\..\csharplang`）：**
- 「Allow `foreach` loops to recognize an extension method `GetEnumerator` method that otherwise satisfies the foreach pattern, and loop over the expression when it would otherwise be an error.」→ `proposals\csharp-9.0\extension-getenumerator.md`
- 「This will bring `foreach` inline with how other features in C# are implemented, including async and pattern-based deconstruction.」→ `proposals\csharp-9.0\extension-getenumerator.md`
- 「To force `foreach` to instead only consider the asynchronous APIs, `await` is inserted as follows:」→ `proposals\csharp-8.0\async-streams.md`
- 「No syntax would be provided that would support using either the async or the sync APIs; the developer must choose based on the syntax used.」→ `proposals\csharp-8.0\async-streams.md`
- 「For example, the current VB compiler will not be able to consume them decorated with `ParamArrayAttribute` neither in normal, nor in expanded form. Therefore, an addition of 'params' modifier is likely to break VB consumers, and very likely consumers from other languages or tools.」→ `proposals\csharp-13.0\params-collections.md`
- 「An `await foreach` statement will continue disallowing a ref struct enumerator and a type parameter enumerator that `allows ref struct`. The reason is the fact that the enumerator must be preserved across `await MoveNextAsync()` calls.」→ `proposals\csharp-13.0\ref-struct-interfaces.md`
- 「The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET.」→ `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation，索引第四节已验证）
- 「`GetEnumerator`/`GetAsyncEnumerator` in `foreach`」/「`Deconstruct` in deconstruction, in positional pattern and foreach」→ `proposals\csharp-14.0\extensions.md`（pattern-based constructs 节）
- 「foreach loop was changed to generates a new loop variable rather than closing over the same variable every time」→ `Language-Version-History.md`（C# 5 条目）

**OPEN QUESTIONS / Suspect：**
- `KeyValuePair(Of TKey, TValue)` 在 BCL 是否内置 `Deconstruct` 方法（从而 C# 的 `foreach (var (k, v) In dict)` 与 VB 的解构都开箱可用）——本仓库无 dotnet/runtime 正文，未核实（正文 OPEN QUESTIONS 倾向「语言内置识别」仍成立，但 BCL 层事实未确认）。
- vblang spec（`spec\statements.md` §For Each...Next，行 1349）「instance, shared or extension method `GetEnumerator()`」表述的现行性——spec 可能滞后于 Roslyn VB 实现。
- C# 7 Deconstruction 的 foreach 形态逐字原文在 dotnet/roslyn 的 `docs\features\deconstruction.md`，不在此镜像；本附录仅以 `Language-Version-History.md` 条目与 extensions.md 的「Deconstruct ... in foreach」为据。
