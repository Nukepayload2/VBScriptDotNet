# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论 Null 安全全家桶中的"语句化"分支：把 `?.` 的空传播从表达式一路推进到 `For Each`、`Await`、赋值左侧、事件注册与委托创建。这个家族（`Null` 字面量、`??`、`?=`、语句 no-op）上周刚在 Null 字面量会上被整体标过 Table——那份建议的结论是"若只做字面量不做家族，价值打折；若做家族，范围爆炸"。今天我们直面"范围爆炸"的那一部分。

## Agenda

* [Proposal: 空安全行为 / Null-Safe Behaviors](#proposal-空安全行为--null-safe-behaviors)

## Proposal: 空安全行为 / Null-Safe Behaviors

_Related: [LDM-2014-02-17 #54](../../vblang/meetings/2014/LDM-2014-02-17.md)（`?.` 原则上批准）· [LDM-2014-04-01](../../vblang/meetings/2014/LDM-2014-04-01.md)（`?.` 设计笔记）· [vbldm-notes-2018.05.30](../../vblang/meetings/2018/vbldm-notes-2018.05.30.md)（Issue #303 / #167）· [vbldm-notes-2017.08.30](../../vblang/meetings/2017/vbldm-notes-2017.08.30.md)（`!` 冲突先例）· [vbldm-notes-2018.02.28](../../vblang/meetings/2018/vbldm-notes-2018.02.28.md)（NVT 相等 quirk）· AnthonyDesign section 12「Null and Nothing」（L1829–1855）· ModVB `proposal-null-safe-behaviors.md`、`proposal-null-coalescing.md`、`proposal-nullability-flow-analysis.md`_

### 场景与缺口

建议原文的动机一句话讲完：现代调用链处处可能命中 null 接收者，若每个语句形态都要手工 `If ... IsNot Nothing` 包裹，样板太多。把 `?.` 的"空即跳过"语义统一推广到语句与运算符：

```vb
' 空集合不枚举 / 空任务不等待 / 空目标不赋值 / 空接收者不注册事件 / 空接收者返回 null 委托
For Each item In collection?
For Each item In obj?.Member

Await someTask?
Await obj?.MemberAsync()
Let result = Await obj?.MemberAsync()

obj?.Member = value

AddHandler obj?.E, handler
RemoveHandler obj?.E, handler

Let d As Action? = AddressOf obj?.Method
```

We 不怀疑这份动机里有一半是真实的。`Await` 一个可能为 null 的任务、给可能为 null 的对象成员赋值，这两处样板在现代代码里确实高频。**但**，把建议读完，我们立刻遇到三个让整场讨论从"要不要做"变成"这份建议是什么"的问题：

1. **它捆绑了五种互不相干的能力**。空集合循环、空任务等待、空赋值、空事件注册、空委托创建，每一种的 lowering、类型规则、边界完全不同。它们共享的只有一个"空即跳过"的直觉。
2. **它混用了两种记法**。`obj?.Member`、`Await obj?.MemberAsync()` 用的是 `?.`（null-conditional，接收者判空后传播 null）；而 `collection?`、`someTask?` 用的是**后置 `?`**——同一个 token 在同族建议 `proposal-null-coalescing.md` 里已经被分配了两个含义（`True?` 推 `Boolean?`；`lookup("key")?` 文档化"可能为 null"并开启严格检查）。现在第三个含义（"空则跳过本语句"）叠上来。
3. **它复活的恰是主线明确停下的那件事**。`AddHandler obj?.E, handler` 正是 vblang Issue #303，主线 2018 年已标 `LDM Reviewed: No Plans`。建议没有携带任何反驳那次否决的新证据。

这三点不是边角批评，它们决定了我们用什么姿势评价这份建议。我们把五种能力拆开，逐一过。

### 候选方案

**PROPOSAL A — 全家桶照单全收（建议原文）。** 五种语句形态全部引入 no-op 语义；`?.` 与后置 `?` 都参与；`Await obj?.MemberAsync()` 的结果类型可空化并向上传播。

**PROPOSAL B — 只做"表达式化"形态，不动语句。** 只保留类型系统里说得清楚的两种：空条件赋值 `obj?.Member = value` 与空条件 Await `Await obj?.MemberAsync()`（带显式可空结果类型）。两者都能 lower 成 `If obj IsNot Nothing Then ...` 的表达式/语句形式，不引入后置 `?` 语句修饰符，不碰 `For Each` / 事件 / 委托。

**PROPOSAL C — 不静默，靠流分析 + 诊断。** 不做任何"空即跳过"。把 `If x IsNot Nothing` 的显式守卫交给可空性流分析（`proposal-nullability-flow-analysis.md`）去收窄，让编译器在"可能对 null 解引用"的位置给出警告，由开发者决定怎么写。零新语法、零隐蔽控制流。

**PROPOSAL D — 什么都不做。** 维持现状：`For Each` 空集合照旧 NRE、`Await` null 任务照旧 NRE、AddHandler 空接收者照旧 NRE；开发者用 `If ... IsNot Nothing` 包裹。这实际上就是主线 2018 年对 #303 的选择。

### 权衡：Q&A

- **A vs B：为什么把 For Each / AddHandler / AddressOf 拆出去？** 因为它们各自有一道 A 没回答的墙。For Each 撞上 Anthony 自己都标注的问题——"Should querying a null collection evaluate to null or empty?"；AddHandler 撞上主线 #303 的明确否决；`AddressOf obj?.Method` 撞上"返回 null 委托之后呢"——你拿到一个 null 委托，`d?.Invoke()` 才是有意义的下半句，而那只属于表达式世界。We think：A 不是一份建议，是五份建议的占位符。
- **B vs C：空条件赋值/Await 的静默跳过，能不能靠警告对冲？** 这是全场核心分歧。B 的支持者认为 `obj?.Member = value` 在 C# 语义里是自然的（C# 的 `?.` 本身就是"跳过后续"），而 C 的支持者搬出原则 #7 与 `Return?` 先例（见下）——**任何一个"细微字符改变控制流"的语法，在 VB 都需要异常高的证据门槛**。B 与 C 不是二选一：B 若被接受，也必须带"被跳过时不发警告"还是"发提示"的明确策略；C 是零语言风险的退路。
- **AddHandler：为什么我们不愿翻案？** 主线 2018.05.30 的原话逐字是："As suggested this would only work if special case lowering or similar occurs because the null is more or less on the assignment side."，"Considered in relation to .NET 3 focus on WinForms/WPF. However, even in that context this would be quite rare (generally humans don't do the AddHandlers thing and generally all controls are instantiated.)"，结论 "Really seems like a side case."，标签 `LDM Reviewed: No Plans`。实现需要"特判 lowering"、场景被主线标为稀有、且拿 WinForms 这种最可能用到它的框架来掂量都不值得——这就是我们面对的反驳。除非有人拿出量化数据说明"空接收者 AddHandler"在现代代码里不再稀有，否则 We 不认为有翻案基础。
- **一个反直觉的正面论据：VB 的事件本来就更安全。** 2014-04-01 讨论 C# 的 `OnFired?.Invoke(this, e)` 惯用法时，主线明确写："Trigger an event only if it's non-null (not needed in VB, since this is already built into RaiseEvent statement)"。也就是说**触发**那一半 VB 天生就空安全，不需要 `?.`。剩下的只有**注册/注销**（AddHandler/RemoveHandler），而那恰是 #303 判 No Plans 的部分。这把建议的最大卖点削掉了一半。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

建议里有两个不同的"空"记号，文法地位完全不同：

- `obj?.Member = value`、`Await obj?.MemberAsync()` 是**既有 `?.` 表达式**的复用——`?.` 已存在于 VB 文法，接收者判空后传播 null。这里真正的新东西是"`?.` 的结果出现在赋值左侧 / Await 操作数位置时，语义变成跳过"。
- `collection?`、`someTask?` 是**后置 `?` 修饰符**——它在文法是全新的。更糟的是它已被同族建议 `proposal-null-coalescing.md` 占用：`Let flag = True?`（可空推断）与 `Let result = lookup("key")?`（文档化可能为 null + 严格检查）。同一 token 在同族三份建议里承担三种语义，解析器需要在表达式求值、语句 no-op、类型标注之间做上下文判定。

We 想到了 2017.08.30 主线的先例：NRT 当时判 "Not ready yet"，需要重访，并点名 `!` 运算符是待解问题之一——"the damnit operator `!` conflicts with both VBs dictionary-access operator `dict!key` and the type character for single-precision floating-point numbers `Dim radius!`"。后置 `?` 正在制造同类冲突，只是冲突源从外部语法（字典访问）变成了家族内部。**规则越分散，解析成本与记忆成本越高**——这正是建议自己 Drawbacks 第一条承认的。

#### 2. 角案例与边界语义

逐个能力过：

**空集合 For Each 的"empty or null"。** Anthony 原问："Should querying a null collection evaluate to null or empty?"。We 的分析：对 `For Each item In collection?`，若 collection 为 null 时是"跳过"还是"空循环"，两者在**可观察行为上几乎等价**——循环体执行零次。真正的差别在：① 循环变量 `item` 在循环后的 definite assignment（跳过则更明确地"未赋值"，空循环则走"集合可能为空故未赋值"的既有规则）；② 若 `collection?` 是表达式而非语句修饰符，则它**有值**（null），`For Each` 只是消费一个"可能为 null 的源"，那"跳过"其实是 For Each 语义的变更而非表达式语义。这条不写清楚，编译器无从实现。建议把它留在未决，意味着核心语义悬空。

**`For Each item In obj?.Member` 复用 `?.` 的连锁。** `obj?.Member` 在 `obj` 为 null 时求值为 Nothing——这**今天就是合法表达式**，只是 `For Each ... In Nothing` 在运行期抛 NRE。要变成 no-op，编译器必须识别"此源表达式来自空条件"，即编译器要跟踪"这个 Nothing 是安全的"。这正是 `proposal-nullability-flow-analysis.md` 的领域。也就是说：这条不是"加一个 `?`"，而是**让 For Each 理解可空性流状态**。

**`Await someTask?` 与双重 null。** `Await obj?.MemberAsync()`：若 `obj` 非 null 但 `MemberAsync()` 返回 null 任务呢？`?.` 只判接收者，不判返回值——第二次 null 依然 NRE。`Await someTask?` 只在任务本身 null 时跳过。两条规则叠加，"哪个 `?` 管哪层 null"需要精确定义。且 `Await` 参与 async 状态机：若唯一的 `Await` 被跳过，整个方法要能**同步完成**——状态机必须有"未发生 await"的路径，这不是加个空检查那么简单。

**空条件赋值的复合形式。** 建议未决问题自己点了 `obj?.Member &= x`。`obj?.Member = value` 尚可 lower 为 `If obj IsNot Nothing Then obj.Member = value`（一次 set）；但 `obj?.Member += 1` 必须 lower 成"读一次、算、写一次"，若 `Member` 是带副作用的属性，两次求值问题立刻出现——2014-02-17 讨论 `?.` 时主线特意强调要把接收者放进临时变量 "(to avoid evaluating it twice, and to avoid race conditions)"，同样的纪律必须延伸到复合赋值。

**`AddressOf obj?.Method` 的下半句。** `Let d As Action? = AddressOf obj?.Method` 返回 null 委托。但拿到 null 委托后唯一有意义的用法是 `d?.Invoke()`——那又回到表达式世界。作为一个"产生一个可能为 null 的值"的特性，它更像委托可空性（`Action?`）的问题，而不是语句 no-op 的问题。价值边际。

**AddHandler/RemoveHandler 的对称性。** `RemoveHandler obj?.E, handler` 有个隐藏特性：VB 的 `RemoveHandler` 对未注册的处理器本来就是 no-op（不发异常）。所以 RemoveHandler 侧"跳过"与"照常执行"几乎不可区分；真正有语义的是 AddHandler 侧（自定义事件的 add 访问器可能有副作用）。这让"两条一起加"显得设计上并未对齐两侧的真实语义。

**扩展方法的坑。** VB 的扩展方法（`<Extension>`）可以合法地以 null 接收者调用——`obj.ExtMethod()` 中 `obj` 为 null 时方法体照样跑。若 `obj?.ExtMethod()` 被定义为"空则跳过"，它改变的是扩展方法**可以处理 null 参数**这一既有能力。任何 `?.` 扩展都必须与扩展方法先例对齐，建议完全没提。

#### 3. 作用域与绑定

- `obj?.Member = value` 的语义模型应返回"条件执行的赋值"——`obj` 表达式本身仍是原类型，赋值被编译成 `If` + 赋值两个节点。IDE 需要一种"跳过型赋值"的展示。
- 被跳过的 For Each 循环变量，语义模型按 definite-assignment 规则处理：循环后不可用（与空集合循环一致）。
- `AddressOf obj?.Method` 绑定到方法组：non-null 时是绑定委托，null 时是 `Nothing`——"绑定到一个方法，结果可能是 Nothing"在符号模型里是新概念。

#### 4. 与既有特性的交互

- **`RaiseEvent`**：VB 触发事件已空安全（2014-04-01 明文），不需要 `?.` 家族；家族只剩注册/注销，恰是 #303 No Plans 的部分。
- **async/await 状态机**：跳过 `Await` 需要同步完成路径（见第 2 条）。
- **扩展方法**：null 接收者语义先例冲突（见第 2 条）。
- **`With` / 对象初始化器**：`With obj?.Member` 是另一个潜在的 `?.` 使用点，建议未覆盖——说明"扩展到语句"的边界在原文里并没有穷举（未决问题自己承认）。
- **ByRef / copy-in-copy-out**：`obj?.Member = value` 若 `Member` 是索引器/属性则无 ByRef 问题；但若有一天允许 `obj?.Member(ByRef x)`，跳过意味着 `x` 不被写回——与 2014-02-17 主线把接收者入临时变量的纪律同一类问题，建议未展开。
- **可空值类型相等 quirk**：2018.02.28 记录指出 "Nullable value types also works some place in VB where it doesn't in C#"，且 "This is likely to need different deep thought than C# (more than a port) to find VB behavior (quirks in a good way)"。`Await obj?.MemberAsync()` 的结果可空化一旦铺开，必然撞上 VB 对可空值类型的既有三值逻辑怪癖——两个家族必须对齐，否则 `If result Is Nothing` 与 `If result = Nothing` 分叉。

#### 5. Breaking change 与兼容性

新语法都是 opt-in 写法，**对存量代码的直接破坏有限**——除了一个例外：若有人图省事把 `If coll IsNot Nothing Then For Each ...` 换成 `For Each ... In coll?`，行为从"显式守卫"变成"静默跳过"，同一代码在更早编译器的等价写法没有对应物，这是**新代码的语义跳跃**而非旧代码重编译变化。更本质的风险是**隐蔽语义变化**（原则 #7）：`?.` 进入赋值/await/循环后，一个字符的差异改变整个语句的控制流。2018.05.30 对 `Return?`（Issue #167）的否决原话正是："We think this is a bad idea. Control flow would be altered by a very subtle character. It's not the same meaning as other uses as ? (any alteration in control flow)"，标签 `LDM Reviewed: No Plans`。We 认为这句判词**逐字适用于** `collection?`、`someTask?`、`obj?.Member = value`。这不是巧合，是同一设计原则撞上同一种语法形态。

#### 6. Option Strict / 编译选项分叉

- `obj?.Member = value` 在 `Option Strict Off` 下 `obj` 为 `Object` 时晚期绑定；跳过语义应两路径一致（null 则不赋值，无论绑定早晚）。
- `Await obj?.MemberAsync()` 的结果可空化在 `Option Infer Off` 下必须仍能编译（显式类型注解时 `Let result As Task` 与可空结果的兼容性要定义）。
- 建议对两条编译选项路径**只字未提**，这是空白。流分析类特性（`proposal-nullability-flow-analysis.md`）已确立"两条路径行为必须一致"的纪律，本建议继承同一条纪律。

#### 7. IDE / IntelliSense 影响

- `obj?.Member = value` 在补全里显示什么？"条件赋值的左侧"需要新的展示；被跳过时是否给提示？
- `For Each item In collection?` 的循环变量补全：循环体不可用时，`item` 的成员补全必须按"可能未赋值"处理。
- `Await obj?.MemberAsync()` 的 hover 类型（可空结果）依赖 NRT 的 IDE 全链路——那条链路在 VB 里还没建（见 `proposal-null-literal.md` 纪要）。

#### 8. 数据 / 普遍性

- 主线对 #303 的判词是"quite rare"、"side case"（2018.05.30）。**没有量化数据反驳**：我们没有"空接收者 AddHandler"的占比、没有"Await 判空样板"的占比、没有用户请求数。Anthony 的动机陈述是直觉而非测量。
- 与 `TypeOf` 收窄不同（守卫惯用法有真实代码佐证），"语句级 no-op"的普遍性目前只有轶事。We 沿用 Null 字面量会的判断：**在有数据之前，任何"普遍性"宣称都是未经验证的假设**。

#### 9. 更简替代

- **显式守卫**（PROPOSAL D）：`If task IsNot Nothing Then Await task`、`If obj IsNot Nothing Then obj.Member = value`——样板多，但**语义是显式的**，这正是 VB 的基因。
- **流分析 + 警告**（PROPOSAL C）：对"可能对 null 解引用"的位置给出诊断，让开发者决定怎么改。这是 80% 的价值（消除隐患）以 20% 的风险（不动语言）获得。2018.02.07 对 NRT 的判断在此适用："it may feel like a 'not VB' thing as we understand its usage"——但那是说 NRT 本身；**警告式指引反而是最 VB 的**。
- **analyzer 提示**：`Await task` 中 `task` 可能为 null 时，建议 `If task IsNot Nothing Then`——零语言改动，把"样板"变成"被建议的显式写法"。

#### 10. 成本 / 优先级

五种语句形态 = 五套 lowering（For Each 判空、async 状态机跳过、赋值左值跳过、事件 add/remove 特判、委托 null 结果）+ 语义模型 + IDE + 与流分析引擎的接口。而 #303 主线已注明 AddHandler 需要 "special case lowering"。**这是一个大特性，为一个主线判为 side case 的家族买单**。优先级应排在可空性流分析、NRT 方向明朗之后。缩小到 B（表达式化两种形态）能显著降本。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍——全部是编译期 lowering（`If` + 原操作），`ldnull` 比较是既有操作。真正的实现难点在 async 状态机的"无 await 路径"和复合赋值的一次求值纪律，而非 CLR 层。NRT 表面标注跨程序集传播（2018.02.07 Part 2）才是那条硬骨头，又一次把问题推回 NRT。

#### 12. 值不值得做

- **价值**：真实但窄，未量化。最强的两块（空条件 Await、空条件赋值）恰好是 B 的地盘。
- **成本**：高。五套 lowering + 状态机 + IDE。
- **风险**：高。隐蔽控制流（`Return?` 先例）、记法冲突（后置 `?` 三义）、复活 #303、与扩展方法先例冲突。
- **结论**：**全家桶不值得，子集值得单独议。** A 的整体价值没有超过它引进的五倍表面积。

### VB 基因对照

我们逐条对照设计原则：

- **原则 3「不引入第二种做事方式」**：**违反**。`If x IsNot Nothing Then ...` 已存在且显式；`collection?`、`Await someTask?` 是同一件事的第二写法。空引用守卫是 VB 的高频惯用法，给每种语句形态各加一个 `?`，等于把惯用法拆成两套。
- **原则 7「避免隐蔽的控制流/语义变化」**：**违反**。`Return?` 判词（"Control flow would be altered by a very subtle character"）逐字适用于本建议。这是否决它的最重砝码。
- **原则 9「消除常见样板」**：唯一支持项。空条件 Await 与空条件赋值是真实样板；但原则 9 要求"用最小语法解决高频痛点"，而本建议用五种语法解决五类痛点，其中四类（For Each/AddHandler/AddressOf/RemoveHandler）都缺普遍性证据。
- **原则 5「读起来像英语、对新手友好」**：偏离。`For Each item In collection?` 里的裸 `?` 是行话，不是英语。
- **原则 4「默认跟随 C#，除非有充分理由」**：偏离且无充分理由。C# 没有语句级 `?.`（C# 的 `?.` 停在表达式；`obj?.Member = value` 在 C# 是编译错误），本建议既无 C# 对齐压力，也没给出偏离它的理由。不过 2014 主线确实考虑过更宽的 `?.` 家族（`x?(y)` 这种 VB 特有的空条件调用，C# 不提供），说明"比 C# 宽一点"在 `?.` 上有历史合法性——但那仍是表达式世界。
- **原则 1「永不破坏现有代码」**：新语法 opt-in，无旧代码破坏；但静默跳过制造"新代码语义跳跃"，且家族若被接受会诱使开发者删掉既有守卫——间接的行为改变。
- **2.3 主线对照表**："Null 安全全家桶"一行明确写着——主线 null 条件 AddHandler 已 No Plans，Anthony 大范围铺开，"主线保守，Anthony 激进"。本建议属于 **Anthony 激进延伸，且直接踩在主线划停的 #303 上**。

一条 nuance 值得保留：**`?.` 本身在主线有良好的"原则上批准"记录**（2014-02-17 #54："Approved, but needs more design-work"），且是 UserVoice 第二高票请求（2014-04-01："The `?.` operator is the second-highest voted request on UserVoice"）。所以家族的地基不 alien——**问题不在 `?.`，在语句级静默 no-op**。我们把这条记为会议的核心分野。

### 诚实分层

- **事实**：2014-02-17 #54 对 `?.`"Approved, but needs more design-work"，语义锚定 `Dim x = customer?.Name` ≡ `Dim x As String = If(customer Is Nothing, Nothing, customer.Name)` 且接收者入临时变量；2014-04-01 记录 `?.` 为 UserVoice 第二高票、"RaiseEvent 已内置空安全"；2018.05.30 #303（null 条件 AddHandler）判 `LDM Reviewed: No Plans`，判词含"side case"、"special case lowering"；#167（`Return?`）判 `LDM Reviewed: No Plans`，判词"Control flow would be altered by a very subtle character"；2017.08.30 记录 `!` 与字典访问、单精度类型字符冲突；2018.02.28 记录 NVT 相等在 VB 的 quirk；Anthony 原文 section 12 L1829–1855 为本建议全部示例来源；建议状态行为占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）。
- **Probably**：到 2026 年 C# 仍未提供语句级 `?.`（赋值/AddHandler/For Each），本建议超出 C# 覆盖面；空条件 Await/赋值在现代代码中的样板占比高于 For Each/AddHandler（无数据，但团队直觉如此）。
- **Suspect**：`Await someTask?` 与 `AddHandler obj?.E` 的真实使用频率（#303 主线的"rare/side case"判断未被反驳）；"跳过 Await 时 async 状态机同步完成"的实现复杂度估计；后置 `?` 三种语义并存是否真的造成用户困惑（需要可用性证据）。
- **OPEN QUESTIONS**：① 空集合 For Each 是"跳过"还是"空循环"，以及循环变量 definite-assignment 的精确规则；② `Await obj?.MemberAsync()` 的结果类型（可空任务？可空结果？）沿调用链如何传播；③ `obj?.Member &= x` 复合赋值的一次求值纪律；④ `obj?.ExtMethod()` 与扩展方法 null 接收者先例如何对齐；⑤ 若走 PROPOSAL C，警告策略的准确范围。
- **TODO**：为"空条件 Await/赋值样板"收集量化数据；跟踪 Anthony 对 NRT 的修订（`proposal-null-coalescing.md` 已注明 NRT 未定稿）；与 `proposal-null-coalescing.md` 对齐后置 `?` 的 token 所有权；确认 `?.` 在赋值左值位置的语法解析成本。

### RESOLUTION:

1. **PROPOSAL A（全家桶）整体不成立，作为一份建议 Reject。** 它捆绑五种独立能力、混用 `?.` 与后置 `?` 两种记法、复活主线 #303 而无新证据、且违反原则 #7。**我们不会以当前形态推进它。**
2. **`AddHandler obj?.E, handler` / `RemoveHandler obj?.E, handler`：维持主线 #303 的 `LDM Reviewed: No Plans`，明确 Reject。** 主线判词"quite rare"、"side case"、"special case lowering"未被反驳；`RaiseEvent` 侧 VB 已空安全，注册侧无数据证明高频。**在有人拿出量化数据之前不翻案。**
3. **后置 `?` 语句修饰符（`collection?`、`someTask?`）：Reject（当前形态）。** 它与 `proposal-null-coalescing.md` 的后置 `?`（可空推断、严格检查文档化）发生 token 冲突，且属于 `Return?` 同类的隐蔽控制流。若未来做"空安全语句"，语义必须在**语句层**单独设计，而不是复用表达式后置 `?`。
4. **`Await obj?.MemberAsync()` 与 `obj?.Member = value`：拆为两份独立窄建议，状态 Consider。** 它们是最真实的两块样板，且天然是"表达式化"形态。条件：(a) 与可空性流分析引擎（`proposal-nullability-flow-analysis.md`）共用流状态；(b) 等待 NRT/可空结果类型方向明朗；(c) 必须定义"被跳过时"的诊断策略——We 倾向默认发提示而非完全静默；(d) 复合赋值与扩展方法先例必须写进 spec。
5. **`For Each item In collection?` / `obj?.Member`：Table。** 核心的"empty or null"语义未决，且依赖可空性流状态跟踪；不进入 Active。
6. **`AddressOf obj?.Method`：Table。** 价值边际，依赖委托可空性（`Action?`）的独立设计。
7. **不做任何 For Each-over-Nothing 的既有语义变更。** 存量 `For Each x In MaybeNothing()` 维持 NRE。

**Implication:**

- 建议状态标注 `LDM 2026-08-08: Rejected (as a bundle)`；在 proposal 头部加一行说明，并链接本纪要。
- 从本建议中析出两个独立探索项："空条件赋值 `obj?.Member = value`" 与 "空条件 Await `Await obj?.MemberAsync()`"，各写独立窄建议，供 NRT/流分析方向明朗后重新评估。
- 与 `proposal-null-coalescing.md` 正式对表：后置 `?` 的 token 归属，杜绝三义并存。
- 建议作者补充：五种能力各自的使用频率数据、`Await` 可空结果类型的完整传播规则、复合赋值的一次求值纪律、与扩展方法的对齐、兼容性分析（含 Option Strict 分叉）。
- 更新 2.3 对照表记录：ModVB 侧对 null 条件 AddHandler 维持主线 No Plans。

### 状态

- **LDM 状态：Rejected（作为捆绑建议）**；析出的空条件 Await / 空条件赋值为 Consider；AddHandler 部分 Reject（维持主线）；For Each / AddressOf 部分 Table。
- **三态判定：Table（整体暂缓）/ Reject（AddHandler 与后置 `?` 语句修饰符）** —— 痛点真实但方案未成熟，记法冲突与隐蔽控制流不可放行；真实价值在子集，待流分析引擎与 NRT 地基就绪后以窄建议形式复活。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-null-safe-behaviors.md` — 把 `?.` 空传播扩展到语句（For Each / Await / 赋值左侧 / AddHandler·RemoveHandler / AddressOf）。
- 来源：Anthony 原文 section 12「Null and Nothing」L1829–1855（"Null-safe behaviors (no-op) and null-propagation from `?.` ..."，全部示例出自该段）。
- 配方目标：消除各语句形态的手工判空样板；对象为 null 时相关操作表现为 no-op。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 目标清晰（消除判空样板）、示例与原文逐字一致、示例可演示；但证据止于书面（状态栏占位链接，无原型/运行）；关键子效果缺失——"empty or null"未决、`Await` 可空结果类型未定，核心语义悬空 | 已提供 / 已检查 | 未决问题 ≥4 个关键设计点 ⇒ 效果证据封顶；无原型 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。五种独立能力捆绑进一份建议；静默 no-op 与 VB 显式风格冲突（不读像英语、隐蔽控制流），未做 VB 化改造。主体虽借用主线"原则上批准"的 `?.`，但语句级跳过是 Anthony 激进延伸 | 已检查 | 违反原则 3 / 7；捆绑杂质多；`?.` 与后置 `?` 记法混用 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例一致、3 个未决问题如实列出（健康区间）；但语法边界含糊（无 BNF、无 lowering、两种记法未区分）；无兼容性/breaking 分析；未与同族 `proposal-null-coalescing.md` 的 `?` 记法冲突对齐；"empty or null"这种核心语义被一句带过 | 已检查 | 缺文法/兼容性章节；记法冲突未识别；状态栏占位链接 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。得：雷（消除样板提速）、水（ModVB 差异化）；失：暗（静默跳过隐藏 bug，无对冲设计）、风（与主线保守方向断裂、与同族建议记法冲突）；Drawbacks 仅 3 条，未覆盖这些灵气维度 | 已检查（预测待定） | 暗风险无应对；与 #303 主线冲突未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料来源标注准确（原文 section 12）；但未标注"复活的恰是主线 #303 已 No Plans 的项"；`?.` 借鉴主线 2014 批准未点明；后置 `?` 与同族建议的关系未标注；五能力捆绑的成分混合 | 已检查 | 成分混合、主线先例未标注、同族 token 冲突未识别 |

### 设计原则对照

- **与 VB 基因：偏离**。违反"不引入第二种做事方式"（原则 3）与"避免隐蔽的控制流/语义变化"（原则 7，`Return?` 先例逐字适用）；部分符合"消除常见样板"（原则 9）但只覆盖两种形态；"读起来像英语"（原则 5）因裸 `?` 行话化而受损。
- **与主线关系：与主线冲突 / Anthony 激进延伸**。`?.` 表达式本身主线 2014"原则上批准"（地基不 alien）；但语句级 no-op 跨过了主线划停的线——`AddHandler`（#303）与后置 `?` 式隐蔽控制流（#167 `Return?`）主线均 No Plans。与 `proposal-null-coalescing.md`（`?` 记法）、`proposal-nullability-flow-analysis.md`（流引擎）、`proposal-null-literal.md`（`Null` 字面量依赖）同族交互。
- **破坏性变更：无对存量代码的直接破坏**（全部 opt-in 新语法）；但有"新代码语义跳跃"与家族诱使删守卫的间接行为改变；若"空集合 For Each 空循环"被推广到既有写法则为破坏。文档未做兼容性分析。

### 总评

- **达成程度：部分达成**——痛点真实（空条件 Await / 赋值两块最实），示例自洽；但方案以捆绑形态呈现，核心语义悬空，且直接撞上主线已落定的否决项。
- **LDM 三态建议：Table（整体）/ Reject（AddHandler 与后置 `?` 语句修饰符）/ Consider（析出后的空条件 Await 与空条件赋值窄建议）**。
- **主要问题**：① 一份建议捆绑五种独立能力，边界模糊（弱提案红旗）；② 混用 `?.` 与后置 `?`，后置 `?` 与同族 `proposal-null-coalescing.md` 发生 token 三义冲突；③ 静默跳过违反原则 #7，无警告/诊断对冲；④ 复活的 AddHandler 恰是主线 #303 已 No Plans 且判"side case"的项，无新证据；⑤ "empty or null"与 `Await` 可空结果类型等核心语义未决，效果证据封顶；⑥ 证据止于"已提供/已检查"（占位状态、无原型、无数据）。

### 返工建议

- **拆分建议**：从本建议析出 (a) 空条件赋值 `obj?.Member = value`；(b) 空条件 Await `Await obj?.MemberAsync()`；(c) 空集合 For Each 三个独立窄建议，各配完整 lowering、文法、类型传播规则；`AddHandler`/`AddressOf` 不单独成文（Reject/Table）。
- **补充章节**：语法规范（`?.` 在赋值左侧 / Await 操作数位置的文法与解析判定；明确放弃后置 `?` 语句修饰符）、兼容性分析（含 Option Strict On/Off、Option Infer Off、`langversion` 门控与警告策略）、与扩展方法 null 接收者先例的对齐、复合赋值（`obj?.Member += 1`）的一次求值纪律、async 状态机"无 await 同步完成"路径。
- **补充证据**：最小原型（空条件 Await + 空条件赋值，验证状态机跳过与语义模型）；"Await 判空样板"与"空接收者 AddHandler"的量化占比（反驳或坐实 #303 的"side case"判断）；被跳过时诊断策略的可用性验证。
- **未决问题处理**："empty or null"倾向定夺为"空循环 + 既有 definite-assignment 规则"（可观察等价、实现更简）；`Await` 可空结果传播与 NRT 对齐后定；后置 `?` token 归属移交 `proposal-null-coalescing.md` 一并对表。
- **设计探索**：与可空性流分析引擎共用流状态后，`obj?.Member = value` 是否可退化为"收窄 + 显式守卫"的语法糖（即 PROPOSAL C 路线）；`AddressOf obj?.Method` 的 null 委托与委托可空性建议（`Action?`）合并设计。

---

## 附录：C# 生态与互操作考量

### 相关 C# 现实方向

本提案主题（空安全行为 / null-safe 语句与集合调用）在 C# 生态的对应走向，与索引中占主线的低层内存互操作（Span/ref/指针，T2/T3）关系较弱——它本质是**语义对齐 + NRT 元数据互操作**问题，而非内存布局问题；但 C# 空安全的「编译期 lowering + 元数据属性」取舍，仍受索引 T5（AOT/trimming）、T7（dynamic/晚期绑定边缘化）方向塑造。C# 相关事实按「已落地 / 开发中」分层：

**C# 已落地：**

1. **C# 6 空传播 `?.` / `?[]`（表达式级）**。→ `Language-Version-History.md`（C# 6 节）：「Null propagator (null-conditional operator, succinct null checking)」。这是本提案 `?.` 部分的直接 C# 对应——同为「接收者判空后传播 null」，但 C# 停在表达式世界。
2. **C# 8 NRT（可空引用类型）**。→ `Language-Version-History.md`（C# 8.0 节）：「express nullability intent on reference types with `?`, `notnull` constraint and annotations attributes in APIs, the compiler will use those to try and detect possible `null` values being dereferenced or passed to unsuitable APIs」。流分析 + 警告而非静默跳过的机制，→ `proposals\csharp-8.0\nullable-reference-types.md`：「A flow analysis tracks nullable reference variables. Where the analysis deems that they would not be null (e.g. after a check or an assignment), their value will be considered a non-null reference.」；`!` null-forgiving：「A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator), for when flow analysis cannot establish a non-null situation that the developer knows is there.」
3. **C# 8 `??=`（空合并赋值）**。→ `Language-Version-History.md`（C# 8.0 节）：「`??=` allows conditionally assigning when the value is null.」→ `proposals\csharp-8.0\null-coalescing-assignment.md`（`a ??= b` 运行时为 `a ?? (a = b)`，a 只求值一次）。
4. **C# 14 空条件赋值 `a?.b = c`（本附录最关键的现实）**。已随 .NET 10 / VS 2026 18.0 发布（C# 14.0）。→ `Language-Version-History.md`（C# 14.0 节）：「permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression (`a?.b = c`)」。语义 → `proposals\csharp-14.0\null-conditional-assignment.md`：「`P?.A = B` is equivalent to `if (P is not null) P.A = B;`, except that `P` is only evaluated once.」；「All forms of compound assignment are allowed.」（`a?.b += M()` 合法）；增量/减量排除：「Increment/decrement operators are not supported」（`a?.b++; // error`）；左值纪律：「Conditional access expressions are still not lvalues」；动机：「Major motivations include: 1. Parity between properties and `Set()` methods. 2. Attaching event handlers in UI code.」，且示例含事件复合赋值 `c?.E += () => { ... }`。

**C# 开发中：**

5. **空条件 Await `await? e`**（champion issue dotnet/csharplang#8631，未发布，根目录工作提案）。→ `proposals\null-conditional-await.md`（Motivation）：「The feature exists to deal with the case where a task-returning expression happens to be null because some earlier step in the expression was null.」语义为 `((object)t == null) ? default(X) : await t`（t 只求值一次），结果类型按 §12.8.8 抬升（`Task<int>?` → `Nullable<int>`、引用类型 → `R?`、`void` → nothing）。

**NRT 元数据互操作：** → `proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation）：「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」——C# 空安全跨程序集传播靠元数据属性（NullableAttribute / NullableContextAttribute），这是任何消费 C# 库的语言（含 VB）的硬互操作要求。

### 现实 vs 提案

| 提案条目（本纪要） | C# 现实 | 判定 | 理由 |
|------|------|------|------|
| 空条件赋值 `obj?.Member = value`（RESOLUTION #4，Consider） | **C# 14 已发布**同语义 `a?.b = c`（语句/表达式两用，P 只求值一次，复合赋值允许） | **需桥接** | 本纪要正文「`obj?.Member = value` 在 C# 是编译错误」在 C# 14 落地后**已过时**；C# 给出了完整 lowering 与结果类型模型，VB 侧应直接对齐而非自造语义 |
| 空条件 Await `Await obj?.MemberAsync()`（RESOLUTION #4，Consider） | **开发中**：`await? e`（championed，未发布） | **兼容 + 需桥接** | 方向一致；但 C# 用**前缀** `await?`，与提案中被 Reject 的后置 `someTask?` 相反——C# 也避开了后置 `?`，反证本纪要的记法冲突判断 |
| `AddHandler obj?.E, handler`（RESOLUTION #2，Reject/#303 No Plans） | **C# 14 已发布** `c?.E += () => {...}`，动机含「Attaching event handlers in UI code」 | **需桥接 / 建议重审数据** | C# 把「空接收者事件注册」列为发布动机之一，与主线 #303「quite rare / side case」形成张力；但形态不同（C# 是表达式复合赋值，VB 是 AddHandler 语句），VB 维持 No Plans 仍自洽，只是「稀有性」论据需面对 C# 数据点 |
| 空集合 For Each `For Each item In collection?`（Table） | 无 C# 对应（`foreach` 空源仍 NRE） | **脱节** | C# 未走「语句级静默跳过」路线；无对齐压力也无锚点，维持 Table |
| `AddressOf obj?.Method`（Table） | 未核实 C# 是否有「`?.` 方法组 → 委托」等价物 | **OPEN QUESTION** | 未在 csharplang 找到直接原文 |
| 后置 `?` 语句修饰符（`collection?`/`someTask?`，Reject） | C# 空安全家族（`?.`/`??`/`??=`/`await?`）均无「后置 `?` 修饰语句」形态 | **兼容（佐证 Reject）** | C# 的 token 演进避开了后置 `?` 多义问题，间接印证「后置 `?` 三义」担忧 |
| PROPOSAL C（流分析 + 诊断） | **正是 C# 生态的实际答案**（NRT 流分析 + 警告） | **兼容 / 同向** | NRT 原文「provide warnings through flow analysis if that intent is contradicted」与本提案 C 同构；C# 已验证其可行、可跨程序集 |
| 全家桶静默 no-op（PROPOSAL A，Reject） | C# 仅对**赋值**单点采纳静默跳过（C# 14），未扩展到 For Each / 语句 | **部分冲突** | C# 在「赋值」接受隐蔽控制流，但未做「全家桶」；与 A 整体 Reject 同向谨慎 |

**一句话总结现实**：C# 正用「表达式级 `?.` + NRT 流分析 + `??=` + C# 14 空条件赋值 + 开发中 `await?`」逐步覆盖本提案的**两个最实子集**（赋值、Await），且全部是「显式写出 `?` 才生效」的 opt-in、纯编译期 lowering。本提案被否决的「语句级静默 no-op 全家桶」在 C# 没有对应。

### 对 VBScript.NET 的适应建议

1. **空条件赋值直接对齐 C# 14 已发布规格**。RESOLUTION #4 析出的 `obj?.Member = value` 应以 C# 14 语义为 spec 蓝本：P 只求值一次；语句位 `if (P is not null) P.A = B`；表达式位 `(P is null) ? (T?)null : (P.A = B)`；复合赋值（`+=`/`-=`）允许、`++`/`--` 禁止；仍非 lvalue、禁 ref。这满足原则 4「默认跟随 C#」，也一并回答了本纪要 OPEN QUESTION ③（复合赋值一次求值：C# 设计为 P 只求值一次）。
2. **空条件 Await 复用 C# `await?` 的结果类型抬升表**。C# 提案的 Table B（`Task<int>?`→`Nullable<int>`、引用→`R?`、`void`→nothing、未约束类型参数→编译错误）直接回答了本纪要 OPEN QUESTION ②「结果类型如何传播」。VB 若保留 `Await obj?.MemberAsync()` 记法，应采用同一结果分类；后置 `someTask?` 维持 Reject（与 C# 前缀选择一致）。
3. **NRT 元数据读取是硬要求**。VBScript.NET 消费 C# 库时，编译器必须识别 NullableAttribute / NullableContextAttribute，才能正确传播「C# 14 空条件赋值 / `await?` 的可空结果」与 NRT 流状态——这是决策文件 M4「需识别新元数据」在空安全域的体现。C# 14 空条件赋值本身是纯 lowering（无新 CLR 约束，与本纪要 §11 一致），但结果上的 NRT 标注跨程序集传播是元数据互操作点。
4. **默认安全、按需动态**：C# 空安全全是「显式 `?` 才跳过」的 opt-in。「默认安全」= 不写 `?` 就按显式守卫/NRE，写了 `?` 才跳过；「按需动态」（Option Strict Off 晚期绑定）路径下跳过语义两路径一致（本纪要第 6 节纪律）。C# 空安全 lowering 纯编译期、无反射，与索引 T5（AOT/trimming）相容——比 Any/晚期绑定（决策文件 M2）省心得多。
5. **`??=` 对表**：VB 同族 `proposal-null-coalescing.md` 的 `?=` 应锚定 C# 8 `??=` 语义（a 只求值一次、`a ?? (a = b)`），避免与 C# 生态的可空/合并语义脱节。
6. **source-gen 桥**：空条件 lowering 在「脚本 → 编译到受管程序集」管线中零额外成本（`if` + 原操作）；interpreted 模式语义照常，但 NRT 标注消费仍依赖编译器——这是把空安全放进编译期而非运行时的理由。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #4（空条件赋值 / Await → Consider）被 C# 现实强化**：C# 14 已发布赋值、`await?` 已 champion，两个子集都有 C# 锚点与现成 spec。条件 (b)「等待 NRT/可空结果类型方向明朗」——C# 14 的 `(T?)null` 结果模型已明朗一部分，VB 无需再悬空等自己的 NRT。
- **RESOLUTION #2（AddHandler Reject）出现外部数据点**：C# 14 将「Attaching event handlers in UI code」列为发布动机，不自动推翻 VB 主线 #303 的 No Plans（形态不同、VB 自有权衡），但「quite rare」论据第一次有了来自 C# 生态的反向数据，值得作为重审触发条件记录。
- **事实性修正**：本纪要正文「`obj?.Member = value` 在 C# 是编译错误」在 C# 14（.NET 10 / VS 2026 18.0）落地后**已过时**——现为合法 C# 表达式/语句。此处标注修正，正文不改写。
- **三态判定整体不变**：全家桶 A 仍 Reject、AddHandler 与后置 `?` 仍 Reject、For Each/AddressOf 仍 Table；但两个 Consider 子集从「无 C# 先例」升级为「有 C# 已发布 / 开发中先例」，复活条件显著放宽。

### 引用纪律

本节 C# 原文均逐字摘自 csharplang 镜像（路径见各条）；无法核实处已标 **OPEN QUESTION**。可复核原文汇总：

- 「Null propagator (null-conditional operator, succinct null checking)」→ `Language-Version-History.md`（C# 6 节）
- 「express nullability intent on reference types with `?`, `notnull` constraint and annotations attributes in APIs, the compiler will use those to try and detect possible `null` values being dereferenced or passed to unsuitable APIs」→ `Language-Version-History.md`（C# 8.0 节）
- 「`??=` allows conditionally assigning when the value is null.」→ `Language-Version-History.md`（C# 8.0 节）
- 「permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression (`a?.b = c`)」→ `Language-Version-History.md`（C# 14.0 节）
- 「A flow analysis tracks nullable reference variables. Where the analysis deems that they would not be null (e.g. after a check or an assignment), their value will be considered a non-null reference.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator), for when flow analysis cannot establish a non-null situation that the developer knows is there.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「`P?.A = B` is equivalent to `if (P is not null) P.A = B;`, except that `P` is only evaluated once.」→ `proposals\csharp-14.0\null-conditional-assignment.md`
- 「All forms of compound assignment are allowed.」「Increment/decrement operators are not supported」「Conditional access expressions are still not lvalues」「Major motivations include: 1. Parity between properties and `Set()` methods. 2. Attaching event handlers in UI code.」→ `proposals\csharp-14.0\null-conditional-assignment.md`
- 「The feature exists to deal with the case where a task-returning expression happens to be null because some earlier step in the expression was null.」→ `proposals\null-conditional-await.md`

**OPEN QUESTIONS**：① C# 是否有「`?.` 方法组 → 委托」（`Action d = a?.M;`）等价物，未在 csharplang 核实；② C# 14 空条件赋值对自定义事件 add 访问器是否完全等价于 VB AddHandler 侧语义，需 dotnet/csharpstandard 精确核实；③ `await?` 最终随哪一版本发布（champion issue #8631 跟踪中）。
