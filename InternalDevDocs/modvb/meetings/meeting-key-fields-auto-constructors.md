# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次讨论 `Key` 字段/属性与自动构造函数——一份把"类字段自动构造"与"结构体值对象三件套（`Sub New` / `Equals` / `GetHashCode`）"捆绑进同一个 `Key` 修饰符的建议。开场氛围是乐观的：消除构造样板是 VB 价值观（低仪式、面向业务应用）的直接体现。但随着讨论推进，我们不断回到 2014 年主线对主构造函数与 record 的旧账——那场会议"heated discussions, and no agreed-upon answers"（LDM-2014-04-23），而本次建议恰好重新踏入同一片领地。两份特性共享一个关键字、共享一段被主线判过刑的历史，这让整个下午的议程都围绕"分开谈"与"别偷运旧货"展开。

## Agenda

* [Proposal: Key 字段/属性与自动构造函数](#proposal-key-字段属性与自动构造函数)

## Proposal: Key 字段/属性与自动构造函数

_Related: [vblang 2014 纪要 #8 – Readonly autoprops（LDM-2014-02-17）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-02-17.md)；[vblang 2014 纪要 #41 – Primary constructors（LDM-2014-02-17，已拒）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-02-17.md)；[vblang 2014-04-23 – Records / primary constructors / pattern-matching](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-04-23.md)；[vblang 2014-10-01 – 构造函数中向只读 autoprop 赋值](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-10-01.md)；[vblang 2018-02-28 – 元组相等性](https://github.com/dotnet/vblang/tree/main/meetings/2018/vbldm-notes-2018.02.28.md)；ModVB：`proposal-key-fields-auto-constructors.md`（本建议）、可空性流分析、交/并类型_

### 场景与缺口

We started from the classic pass-and-store boilerplate. 依赖注入的构造样板是"声明字段 + 写构造函数 + 逐个赋值"，同一份信息在三个位置重复。Anthony 在第 2.2 节的注释说出了全场都认识的那句话：

```vb
Class AppointmentBookingService

    Private Key CalendarService As ICalendarService,
                PaymentService As IPaymentService,
                EmailService As IEmailService

    ' Look at this code I'm never going to have to type again!
    ''Public Sub New(calendarService As ICalendarService,
    ''               paymentService As IPaymentService,
    ''               emailService As IEmailService)
    ''
    ''    Me.CalendarService = calendarService
    ''    Me.PaymentService = paymentService
    ''    Me.EmailService = emailService
    ''End Sub

End Class
```

第二处缺口是不可变值对象。货币金额 `Money` 这类"值语义"类型，今天要手写 `Sub New`、`Equals`、`GetHashCode`，冗长且易错；而且 VB 结构体默认的 `GetHashCode` 走的是反射路径——主线纪要里原话是 "NB. Structure 'GetHashCode' finds the first instance field and calls GetHashCode on it, using reflection"（LDM-2014-02-17）。成员增删时手写相等性极易失配，这是真实缺陷源：

```vb
Structure Money
    Implements IEquatable(Of Money)

    Public Key ReadOnly Property Value As Decimal

    Public Key ReadOnly Property Currency As Currency

    ' Sub New(value, currency), Equals, and GetHashCode provided by compiler.

End Structure
```

We see a real DX gap in both halves. 但我们立刻注意到：这是**两个不同的缺口**。一个针对类的构造注入，一个针对值类型的相等性。它们只是恰好都表现为"样板代码"。

### 候选方案

**PROPOSAL A — 按建议原文：一个 `Key` 修饰符，双义捆绑。** 类字段上 `Key` ⇒ 自动生成构造函数参数与赋值；结构体只读属性上 `Key` ⇒ 自动生成 `Sub New` / `Equals` / `GetHashCode`。修饰符含义取决于"容器是类还是结构体、成员是字段还是属性"。

**PROPOSAL B — 拆分为两个正交特性（本场多数人的直觉）。**
- **B1：类字段自动构造函数。** 只做 `Key` 字段 ⇒ 自动构造参数这一半。
- **B2：结构体值对象三件套。** 只做 `Key ReadOnly Property` ⇒ `Sub New` / `Equals` / `GetHashCode` 这一半。
两者价值主张、风险画像、既有特性交互完全不同，唯一共享的是"编译器为你生成成员"这个抽象。

**PROPOSAL C — 主构造函数语法（类头签名）替代 `Key` 字段。** C# 式 `Class C(calendarService As ICalendarService, ...)`，字段即参数。建议原文 Alternatives 自己承认"它与'字段即参数'的现有声明方式风格差异较大"。主线 2014 年已经给出判决：`# 41. Primary constructors.` "Rejected for VB. Would have had parity with C# vNext feature."（LDM-2014-02-17）。我们不打算重开旧案。

**PROPOSAL D — 类型级 `Key`（整个结构体标 `Key`），而非逐属性。** 呼应 2014 年 record 讨论中"新修饰符 `record`"的路线（"`record class Point(int x, int y)`"，LDM-2014-04-23）。所有公开只读属性都参与相等性，避免逐属性枚举。2014 年的反对理由对我们同样成立：修饰符让同一语法在有无它时"mean such different things"，We `Probably` 不选这条路。

### 权衡：Q&A

- **A vs B：为什么必须拆？** 因为把两个特性压在同一个 `Key` 下，`Key` 的语义负担过重——它在类里表示"这是构造依赖"，在结构体里表示"这参与相等性"。两者唯一共同点是"编译器据此合成成员"，这个抽象太薄。评估标准也把"一份提案混杂多个独立特性，边界模糊"列为红旗。**结论：拆。** 拆开后，B1 与 B2 各自独立评审、独立落地、独立决定取舍。
- **C 为什么不重新考虑？** 主线的拒绝理由虽短（"Would have had parity with C# vNext feature"），但后续 2014-04-23 的讨论把深层问题摊开了：primary-constructor 语法与 pattern-matching 的耦合、record 修饰符与既有语法的"language-cleanliness"张力。Anthony 的字段变体绕开了类头签名——字段声明本来就在类体里，`Key` 只是加了个修饰符——**不是"换个名字偷运旧货"**，而是确实不同的句法面。这一点我们认可。
- **B1 的真正问题：自动构造是隐蔽语义。** 一个字段声明符，静默地：(1) 生成一个带参构造函数；(2) **取消默认无参构造函数**。对 WPF/XAML、序列化器、`Activator.CreateInstance`、以及依赖无参构造的 DI 容器，这是行为断裂。字段声明本应是纯声明，现在却改变了类型的可构造面。这是本场最尖锐的反对意见，反复回访。
- **B2 的真正问题：部分相等陷阱。** "只针对标记了 `Key` 的属性"意味着结构体可以有**非 `Key` 的额外字段**。若两个实例的 `Key` 成员相等而额外字段不同，`Equals` 返回 `True`——相等性定义被人为截断。加上 `GetHashCode` 只看 `Key` 成员，任何"既参与相等又可变"的状态都可能破坏字典不变量。建议要求 `Key` 属性**只读**正是为堵这个洞——但非 `Key` 可写字段照样能把洞挖回来。**结论：B2 需要一个"结构体要么全 `Key`、要么明确声明排除"的封闭规则。**
- **关键字复用：同词异义还是语义连续？** `Key` 今天是上下文关键字，用于匿名类型键成员与 `Group By`：`New With {Key .Name = "Alice"}`。它的现行语义恰恰是"**这个成员参与相等性与哈希**"。所以 B2 的 `Key ReadOnly Property` 与现行 `Key` **语义连续**——这是加分。但 B1 的 `Key` 字段表示"构造依赖"，是**同词异义**。我们引用 2018 年主线对 `Is` 的担心作类比——"We think `Is` will have ambiguity issues with the existing use for reference equality"（LDM-2018-12-19）——关键字复用若只在一半语义上自洽，就要被审问。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`Key` 的现行地位**：`Key` 是上下文关键字（匿名类型成员键、`Group By`），**不是保留字**——`Dim Key As String` 今天合法。把它加进成员修饰符集合，句法消歧靠 lookahead：

- `Private Key As String` → `Key` 后紧跟 `As` ⇒ 字段名（今天的代码不受影响，**零 parse breaking**）。
- `Private Key CalendarService As ICalendarService` → `Key` 后跟标识符再 `As` ⇒ 修饰符（今天这是非法句法，故无旧代码可破坏）。
- `Private Key(10) As String` → `Key` 后跟 `(` ⇒ 字段名（数组字段）。

所以文法层面是**加法**、可上下文区分。但语义负担在：一个上下文关键字同时充当"匿名类型键"与"成员修饰符"两套语法里的记号，IDE 高亮与文档需要分语境解释。`Probably`：若只留 B2，`Key` 的两种含义可以统一为"参与相等性"；若 B1 也要，就需要一份显式的消歧说明。B1 若改用别的词（如 `Required` / `Inject`）可彻底回避——这成为我们在 B1 里反复掂量的选项。

`Key` 与其它修饰符的组合顺序（`Public Key ReadOnly Property`）需与既有修饰符排序规则一致；逗号并列的字段组（`Private Key A As X, B As Y`）中 `Key` 应像 `ReadOnly` 一样作用于整组——这有现行先例（`Private ReadOnly A, B As Integer`）。

#### 2. 角案例与边界语义

**类的一半（B1）：**

- **初始化器冲突**：`Private Key Foo As IBar = GetDefault()` 与自动构造参数赋值冲突——初始值先跑、参数又覆写，还是参数优先、初始化器报错？需要明确优先级。We `Probably` 选"初始化器与自动构造互斥，同时出现则报错"，避免两套赋值语义并存。
- **`ReadOnly` 的 `Key` 字段**：`Private Key ReadOnly X As Integer` 合法——CLR 允许构造函数内对 `ReadOnly` 赋值（"The CLR allows an assignment-to-readonly instruction at any time, but it has no effect after the constructor has finished"，LDM-2014-02-17）。生成的构造正好在构造期内赋值，PEVerify 无碍。
- **`Shared` 的 `Key` 字段**：无实例构造可言 ⇒ 非法，报错。
- **继承**：这是建议完全没提的洞。基类有 `Key` 字段、派生类也有，或派生类需要给基类构造传参——自动生成的构造如何 `MyBase.New(...)`？VB 构造若未显式调用基类构造，默认调无参基类构造；基类 `Key` 字段取消了无参构造后，派生自动构造将无从链起。**必须规定：派生类含 `Key` 字段时，基类自动构造的参数如何汇聚（声明序？先基后派？），或直接禁止派生场景（v1）。**
- **参数名规则**：由字段名派生（`CalendarService` ⇒ `calendarService`）。VB 大小写不敏感，参数与字段其实是**同一标识符的两种大小写**——体内无 `Me.` 前缀的裸引用会命中参数而非字段。生成的 `Me.CalendarService = calendarService` 正好依赖 `Me.` 消歧，但用户随后在其它构造里手写赋值时极易踩"裸名绑到参数"的坑。需要在文档里讲清楚，`Probably` 配一条编译器提示。
- **可为空性**：字段为 `As ICalendarService?` 时，参数应携带相同注解——与可空性流分析建议（`proposal-nullability-flow-analysis.md`）共用注解模型，不能各搞一套。

**结构体的一半（B2）：**

- **非 `Key` 额外字段**：见 Q&A 的"部分相等陷阱"。需要"全 `Key` 或显式排除"的封闭规则，或者规定非 `Key` 字段必须是编译期不可观察的（如缓存、填充）。
- **`Key` 属性是否强制只读**：建议题目写"只读属性"。必须规定"结构体中 `Key` 属性非 `ReadOnly` ⇒ 报错"。理由就是 2014 年 record 讨论的原话："It's bad practice... for GetHashCode to calculate based on mutable properties."（LDM-2014-04-23）。只读是 `GetHashCode` 稳定性的承重墙。
- **引用类型的 `Key` 属性**：`Public Key ReadOnly Property Name As String` 安全（字符串不可变）；但 `Public Key ReadOnly Property Owner As Customer` 里 `Customer` 可变 ⇒ 哈希不稳定，这是 C# record 同样踩过的坑。`Probably`：v1 不区分、文档警示；或对"属性类型是可变引用类型"发警告。
- **生成的 `Sub New` 参数顺序**：按属性声明顺序（`New(value, currency)`）。结构体永远有隐式无参构造，所以 B2 的自动构造是**加法**，没有 B1 的"取消无参构造"问题——这是 B2 比 B1 干净的地方。
- **`Equals` 到底生成几个成员**：`IEquatable(Of Money).Equals` 一个，`Overrides Equals(Object)` 一个，`Overrides GetHashCode()` 一个。建议只说 "Equals"，spec 必须写清这是两个 `Equals` 重载加一个 `GetHashCode`，且两者哈希一致。

#### 3. 作用域与绑定

生成的 `Sub New`、`Equals`、`GetHashCode` 是**真实成员**（如同 autoprop 的 backing field），出现在语义模型与反射里。`Key` 成为字段/属性的**新修饰符**，`GetTypeInfo` 应能查到。IDE 补全在声明处显示 `Key`；生成的构造参数**不**作为可重命名符号暴露（重命名字段不重命名参数——参数是合成的）。

绑定的微妙点在 B1 生成的构造体内：`Me.CalendarService = calendarService` 中，LHS 经 `Me.` 绑到字段，RHS 绑到参数。VB 大小写不敏感使"字段名与参数名仅大小写不同"成为事实——参数遮蔽字段，`Me.` 是唯一出路。这与 2014-10-01 对 autoprop 绑定问题的处理同源：在合适的构造函数内，"assignment to an autoprop refers to the property, not the backing field"（LDM-2014-10-01）——我们这里反方向，生成的代码**主动使用** backing field 语义。

#### 4. 与既有特性的交互

- **Readonly autoprop 构造内赋值**：B2 的 `Sub New` 要写只读 autoprop 的 backing field。主线 2014-10-01 已定：构造内对 autoprop 的赋值"they refer to the property x. Not the backing field"并在 lowering 阶段改写为 backing field 访问。B2 直接复用这条已批准的规则，实现面小。
- **对象初始化器**：`New Money With {.Value = 1}` 不能用于只读成员；有了自动 `Sub New` 后应鼓励 `New Money(1, Currency.USD)`。两者并存不冲突。
- **`Implements` 的显式接口实现模型**：VB 没有 C# 的隐式接口实现。结构体写了 `Implements IEquatable(Of Money)`，生成的 `Equals(Money)` 必须携带 `Implements IEquatable(Of Money).Equals` 子句才能满足接口。**这是建议最大的规格缺口之一**：示例展示了 `Implements` 与生成 `Equals` 的"配合"，却没有解释 VB 显式实现机制下编译器如何接线。若接线规则不写死，用户在别的结构体里手写 `Implements` 会直接编译失败。
- **`Operator =` / `Operator <>`**：建议未涉及。VB 的 `=` 对结构体有自己的怪癖——2018-02-28 元组相等性讨论原话："the equality operator more different than Equals... Nullable value types also works some place in VB where it doesn't in C#"（LDM-2018-02-28）。`Key` 生成 `Equals` 后 `=` 走 `Equals` 还是需要显式 `Operator =`？We `Suspect` 需要独立决定，且必须按 VB 语义而非照搬 C#：那次讨论的结论是"likely to need different deep thought than C# (more than a port) to find VB behavior"。
- **属性初始化器**："VB initializers are allowed to refer to other members of a class"（LDM-2014-10-01）——B1 的自动构造与字段初始化器共存时，求值顺序与"初始化器读其它成员"的既有规则必须不变量一致。

#### 5. Breaking change 与兼容性

**这是 B1 的生死线。** 类加 `Key` 字段后：

1. 隐式无参构造被取代 ⇒ `New AppointmentBookingService()` 重编译后不再成立。WPF/XAML、`Activator.CreateInstance`、序列化器、依赖无参构造的 DI 容器全部受影响。主线对破坏性变更几乎零容忍（"We will almost never make breaking changes"），我们必须把"取消无参构造"列为**主要 breaking change**，并给两条路：(a) 保留无参构造（注入不被强制，`Key` 退化为普通字段式样板）；(b) 取消无参构造（强制注入，但工具链断裂）。We `Probably` 选 (b) 但要求显式 opt-in，且配诊断。
2. 反射成员面变化：`Type.GetConstructors()` 多了一个带参构造——对"按构造签名做动态发现"的框架（部分 IoC）是可见变化。
3. 文法层**零 breaking**：`Key` 是上下文关键字，今天所有合法代码（含 `Dim Key As X`）解析不变——见追问 1。

B2 的 breaking 面小得多：结构体加生成成员是加法；唯一风险是 `Equals` 覆盖 `ValueType.Equals` 后，**相等性语义收紧**——从"字段全等"变为"仅 `Key` 成员相等"。若用户依赖了"非 `Key` 字段也参与相等"的既有行为（今天 `ValueType.Equals` 是字段全等），重编译后行为变化。这正是"部分相等陷阱"的兼容性面孔。

#### 6. Option Strict / 编译选项分叉

B1：要求 `Key` 字段带显式 `As`，参数类型随之确定，严格/宽松两路径无分叉。若允许省略 `As`（`Private Key Foo = GetFoo()`，Option Infer On），参数类型走类型推断——`Probably` v1 禁止省略，保持构造参数类型显式。

B2：生成的 `Equals`/`GetHashCode` 是确定性代码，无晚期绑定参与，两路径行为一致。宽松模式下结构体成员访问的既有宽容（如 `Money.Value` 的 late-bound 访问）不受影响。

#### 7. IDE / IntelliSense

- 生成的 `Sub New` 出现在成员列表与 GoTo Symbol；宜带"生成"标记（同 autoprop 的合成 getter/setter 展示）。
- `Key` 加入修饰符下拉；`Key` 语义随上下文（匿名类型键 vs 成员修饰符）高亮不同。
- 重构联动：把某字段标 `Key` ⇒ 建议移除"Generate Constructor"脚手架；重命名 `Key` 字段不要求同步重命名（参数合成）；添加 `Key` 字段应触发"此类型将不再有无参构造"的警告预览。
- B2：显示生成的 `Equals`/`GetHashCode` 与其 `Implements` 目标；"结构体含非 `Key` 字段"给出例外诊断。

#### 8. 数据 / 普遍性

构造注入是真实代码库的高频场景，服务类动辄三五个依赖——Anthony 的 `AppointmentBookingService` 不是虚构。**没有量化数据**，但"字段+构造+赋值"三重复的频率直觉很强。值对象（`Money`/`Temperature`/`Point`）在 DDD 风格代码里常见，但对"数十万安静客户"的一般业务代码，自写 `Equals`/`GetHashCode` 的频率估计中低。We `Suspect`：B1 的普遍性证据扎实，B2 的普遍性中等——这是优先级排序的重要输入。

#### 9. 更简替代

- **B1**：现有 "Generate Constructor" 重构/分析器已存在，但它是**一次性**代码生成——加字段得重跑，维护负担仍在。`Key` 的价值是让样板**永不回归**。目标类型转换建议（`proposal-target-typed-conversions`）与"默认成员"建议与 B1 无关。字段初始化器能消"赋值"一处，但消不了"构造参数声明"这一处——B1 恰好补上。
- **B2**：第三方源生成器（`EqualsGenerator` 类）可做 90% 的事，但 (1) 生成器不接线 `IEquatable` 的显式 `Implements` 语义自动导航；(2) 生成器产出的是普通代码，仍与手工维护失配同源。语言内生成的额外价值在"与 autoprop 构造内赋值规则、只读规则、`=` 语义"的深度集成。不过 We 承认：**B2 的增量价值没有 B1 高**，且 2014 年 record 讨论已把这块的复杂度都摊开过（"heated discussions, and no agreed-upon answers"）。

#### 10. 成本 / 优先级

B1：合成构造 + 参数接线 + `MyBase` 链规则 + 显式构造互斥规则，Roslyn 改动中等（VB 已有构造合成的先例）。B2：`Equals`/`GetHashCode` 代码生成 + 接口接线 + 算法选择 + `Operator =` 决策，工作量大且规格未定。**优先级建议：B1 先行、B2 后置**——B1 值高成本低风险可控，B2 值中成本高且需再设计。

#### 11. 运行时 / CLR 硬约束

无新硬约束。只读 autoprop 构造内赋值是主线已批准的（"We can't be more permissive in what we allow with readonly autoprops than we are with readonly fields, because this would break PEVerify"——B2 的生成代码**没有**比既有规则更宽松，赋值仍发生在构造内，PEVerify 无碍）。生成的 `Equals`/`GetHashCode` 是普通方法调用。无表达式树问题（生成成员可按普通成员引用）。结构体自动构造不与 CLR 结构体默认构造规则冲突。

#### 12. 值不值得做

逐条打分：

- **价值**：B1 高（DI 样板高频、声明式、极 VB）；B2 中（值对象样板真实但频率低、且已有生成器替代）。**价值总和：中高。**
- **成本**：B1 中；B2 中高（`Equals`/`GetHashCode` 算法 + 接口接线 + 运算符决策）。**成本：中。**
- **风险**：B1 有"取消无参构造"的 breaking 与"隐蔽语义"担忧；B2 有"部分相等陷阱"与 `IEquatable` 接线不明。**风险：中，且集中在规格未定处。**

**值得做——但必须拆开、收敛、把破坏性决策摆到桌面上。** 若原样整体采纳（PROPOSAL A），我们会因 `Key` 语义过载与 B2 规格未熟而建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心，这是本建议最强的基因继承。`Key` 字段让"字段即依赖声明"成为可能，B1 是教科书级的"用最小语法解决高频痛点"。
- **不引入"第二种做事方式"（原则 #3）**：张力最大。VB 已有显式 `Sub New`，`Key` 自动构造是并存的第二种构造方式。缓解：`Key` 是声明式的，与字段初始化器（声明式构造的先例）同族，而非与显式构造竞争；但"第二种方式"门槛本就极高，需要在 spec 里论证为何不是给每个业务类多一条可选路径。
- **保持 VB-like（原则 #2）**：字段声明语法原样保留，只是加修饰符——VB 味足。C 方案的类头签名才是"看起来不像 VB"。
- **避免隐蔽语义变化（原则 #7）**：主要扣分项。字段声明静默生成构造、静默取消无参构造，正是"细微字符改变语义"的同类。生成成员在语义模型可见、诊断可解释只能缓解，不能消除。这一条与 2018 年模式匹配的"no breaking changes"原则（LDM-2018-12-19）直接对撞。
- **读起来像英语、对新手友好（原则 #5）**：`Key` 对新手语义不明（"key" 是钥匙？是关键？是参与相等？）。B2 里它与匿名类型 `Key` 的语义连续可救；B1 里它需要文档解释。
- **与主线关系（对照表 2.3）**：主线 2014 年**拒绝**主构造函数（LDM-2014-02-17 #41）；对 record 的自动相等性讨论留下"no agreed-upon answers"（LDM-2014-04-23）。Anthony 的 `Key` 字段变体是**在主线拒绝的句法面之外重开同一目标**——不是"主线一致"，也不是干净的"独立延伸"，而是"重开被拒领地的新路径"。主线拒绝的是**语法**，不必然是**目标**；但重开者必须承担"为何 2014 年的反对不适用于此"的举证责任。对可空性流分析（注解模型）与交/并类型（若 B2 的 `Equals` 遇多类型键）须对齐。

### RESOLUTION:

1. **拒绝 PROPOSAL A 的整体捆绑。** `Key` 修饰符的双义（类=构造依赖，结构体=相等性）语义过载，两个特性价值与风险不同、必须独立评审。拆为 B1（类字段自动构造）与 B2（结构体值对象三件套）。
2. **B1（类字段自动构造函数）：原则上采纳方向，处于 `Consider`。** 要求补完以下规格后方可 Active：
   - **显式构造互斥**：类型存在任何显式 `Sub New` 时，`Key` 自动构造被抑制，`Key` 字段退化为普通字段（仍须由显式构造赋值）。
   - **默认构造取代**：有 `Key` 字段时取消隐式无参构造是**显式决策**，须有 `langversion` 门控与"类型不再可无参构造"诊断，并配序列化/DI/Activator 影响分析。
   - **继承**：v1 禁止基类与派生类同时使用 `Key` 字段（或规定 `MyBase.New` 参数汇聚规则）。
   - `Shared` `Key` 字段非法；`Key` 字段带初始化器与自动构造互斥报错；v1 要求显式 `As`；`Key` 在逗号组内作用于整组。
   - B1 是否继续使用 `Key` 一词，或改用 `Required`/`Inject` 以回避与匿名类型 `Key` 的同词异义——**留作 OPEN QUESTION**。
3. **B2（结构体值对象三件套）：`Table`。** 概念有价值，但规格未熟，2014 年 record 讨论的旧账未清。落地前必须定：`Equals(Object)` 与 `IEquatable(Of T).Equals` 双成员与 VB 显式 `Implements` 的接线规则；非 `Key` 额外字段的封闭规则（全 `Key` 或显式排除）以堵部分相等陷阱；`Key` 属性强制只读；`GetHashCode` 算法（确定性、不依赖随机种子）；是否生成 `Operator =`/`<>`（须按 VB 语义而非照搬 C#）。B2 不随 B1 一起排期。
4. **`Key` 关键字复用**：B2 与匿名类型 `Key`（参与相等性）**语义连续**，可接受并应在文档中明确；B1 的 `Key` 是**同词异义**，必须单独消歧。若 B1 与 B2 最终同名共存，需一份统一的 `Key` 语义说明。
5. **最小化与诚实分层**：生成成员在语义模型可见、IDE 标"生成"、诊断可解释；对"隐蔽语义变化"（原则 #7）不回避，承认 B1 的默认构造取代是破坏性变更。

### Implication:

- 将 B1 写成独立 speclet：文法（`Key` 修饰符 + 逗号组作用域 + 与其它修饰符排序）、参数命名与 `Me.` 消歧、显式构造互斥、`MyBase` 链、`langversion` 门控与诊断。
- 将 B2 写成独立 speclet：双 `Equals` + `GetHashCode` 的成员签名与 `Implements` 接线、部分相等封闭规则、哈希算法、`Operator =` 决策（对照 2018-02-28 元组相等性讨论）。
- 起草一份 breaking-change 分析：B1 的无参构造取消对 WPF/XAML、序列化、Activator、DI 容器的逐条影响；B2 的相等性收紧。
- 与可空性流分析团队对表：B1 参数的可为空注解复用其注解模型。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：B1 是否沿用 `Key` 一词（vs `Required` / `Inject`）；若沿用，与匿名类型 `Key` 的统一语义说明怎么写。
- `OPEN QUESTIONS`：B1 的继承——v1 禁止派生共存，还是设计 `MyBase.New` 参数汇聚？
- `OPEN QUESTIONS`：B2 的 `Operator =` / `Operator <>` 生成与否；VB 结构体 `=` 在未定义运算符时回落 `Equals` 的确切规则（`Probably`，待 spec 核实）。
- `OPEN QUESTIONS`：B2 的"非 `Key` 额外字段"封闭规则的确切形态（全 `Key` 强制 / 显式 `NonKey` / 仅允许不可观察字段）。
- `OPEN QUESTIONS`：B2 的 `GetHashCode` 算法与进程间稳定性承诺。
- `TODO`：为 B1 的高频直觉补量化数据（DI 构造样板在开源 VB 库中的占比）。
- `Follow-up`：重读 2014-04-23 record 讨论全文，逐条核对"哪些反对在本设计中仍成立、哪些被字段变体化解"——这是 B2 复活前的必经步骤。

### 状态

- **LDM 状态：`Consider`**（B1 原则上采纳方向、规格未齐；B2 挂起）。
- **三态判定：Consider** — B1 价值真实、方向 VB 味十足，但"取消无参构造"的破坏性决策与关键字复用必须先行解决；B2 在 2014 年旧账结清前不排期。尚未达到 Active，也远未到 Reject。

---

## 附录：特性评价

# 建议评价报告：proposal-key-fields-auto-constructors.md

## 评价对象

- 建议：proposal-key-fields-auto-constructors.md — `Key` 字段/属性与自动构造函数（类字段自动构造 + 结构体只读属性值对象三件套）
- 来源：Anthony 原文第 2.2 节 "Key Fields & Properties, and Auto-Constructors"（`..\AnthonyDesign_wordpress.txt` L366–399，两个示例与"Look at this code I'm never going to have to type again!"注释均逐字对应）
- 配方目标：`Key` 修饰符让编译器自动生成（类）构造参数与赋值、（结构体）`Sub New`/`Equals`/`GetHashCode`，消除三处重复的 DI 样板与值对象样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。DI 样板消除的改进清晰、示例可操作（可编译）；但 B2 的核心语义（`Equals`/`GetHashCode`/`Implements` 接线）完全未定型，B1 的默认构造取代破坏性未分析——主效果可见，关键子效果缺失 | 已检查 | 未决问题 ≥4 个关键设计点 ⇒ 效果证据封顶（核心语法未定型 = 效果未显现）；无原型 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。字段声明 + 修饰符是极 VB 的形态（B1 消除样板原则 #9 满格）；但把两个正交特性捆绑进一个 `Key`，且 B2 明显呼应 2014 年 record / C# records 构想而未点明 | 已检查 | 捆绑杂质；`Key` 关键字同词异义；`Key` 修饰符对新手语义不明 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、Drawbacks/Alternatives 诚实；但无文法、无 spec 改动、无兼容性分析；5 个未决问题多处"原文未展开"；结构体示例的 `IEquatable` 接线在 VB 显式实现模型下无法按所示编译而不解释 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；继承（`MyBase` 链）完全未提；`Operator =` 未涉 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/光正向（消除样板提速、DI/值对象样板盘活）；暗风险突出（默认构造取消 = 破坏兼容、隐蔽语义变化、反射/序列化面变化、关键字复用歧义）且 Drawbacks 只列未权衡 | 已检查（预测待定） | 风=与主线拒绝的 primary-ctor 领域重叠，文档未回应"为何 2014 反对不适用"；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 2.2 节（准确）；B2 的自动相等性明显呼应 2014 年 record 讨论 / C# records（未标注）；B1 是"字段即参数"的原创反演（C# 主构造是"参数即字段"，方向相反）——这一对齐论证缺失 | 已检查 | 未显式标注章节号；对主线 2014 年 primary-ctor 拒绝史只字未提（评价标准 2.3 对照表明确标记"主线拒绝，Anthony 变体"）；无 VB6 血缘 |

## 设计原则对照

- **与 VB 基因：部分一致**——消除样板（#9）、保持字段声明形态（#2）、声明式与初始化器同族，方向极 VB；张力于原则 #7（隐蔽语义：字段声明静默取消无参构造）、原则 #3（第二种构造方式）、原则 #5（`Key` 对新手语义不明）。
- **与主线关系：与主线冲突的变体**——主线 2014 年拒绝主构造函数（LDM-2014-02-17 #41），record 自动相等性讨论无结论（LDM-2014-04-23）。Anthony 用字段变体绕开被拒句法面、重开同一目标：属"重开被拒领地的新路径"，既非主线一致，也非干净独立延伸。评价标准 2.3 对照表将其标为"主线拒绝，Anthony 变体"，准确。
- **破坏性变更：有**——B1 取消隐式无参构造（WPF/XAML、序列化、Activator、DI 容器受影响）；B2 相等性从"字段全等"收紧为"仅 `Key` 成员相等"。文法层零 breaking（`Key` 是上下文关键字，`Dim Key As X` 解析不变）。建议文档未做任何兼容性分析。

## 总评

- **达成程度：部分达成**——B1 的价值与方向成立且 VB 味十足；B2 有价值但规格未熟；两特性捆绑使整体不可直接采纳。
- **LDM 三态建议：Consider**——B1 以"显式构造互斥 + 默认构造取代显式化 + 继承限制 + 关键字消歧"为前置收敛后可行（Active）；B2 需先结清 2014 年 record 旧账并定稿 `Equals`/`GetHashCode`/`Implements`/`Operator =` 规则（Table）。整体不达 Active，未到 Reject。
- **主要问题**：① 双义捆绑违背单一职责；② B1 取消无参构造是无分析的主要 breaking change；③ B2 的 `IEquatable` 显式 `Implements` 接线无法按示例编译且未解释；④ 部分相等陷阱（非 `Key` 字段）无封闭规则；⑤ 对主线 primary-ctor 拒绝史零回应，重开被拒领地未举证。

## 返工建议

- **补充章节**：拆分为两份 speclet（B1 自动构造 / B2 值对象三件套）；各补文法（BNF）或 spec 修改、兼容性/breaking-change 分析（B1 无参构造取消逐场景列证；B2 相等性收紧）、`langversion` 门控与诊断策略、继承规则（`MyBase` 链）、`Key` 修饰符与匿名类型 `Key` 的消歧说明。
- **补充证据**：最小原型（B1 合成构造 + 显式构造互斥 + `MyBase` 限制；B2 双 `Equals` + `GetHashCode` + `Implements` 接线）；DI 构造样板占比的量化数据；`Operator =` 在 VB 结构体上的回落 `Equals` 行为核实。
- **未决问题处理**：B1——默认构造取代=显式决策（`Probably` 取消并配诊断，或保留无参构造让 `Key` 退化为普通字段）；`Shared`/初始化器/省略 `As` 均报错；继承 v1 禁止共存。B2——`Key` 属性强制只读；非 `Key` 额外字段需封闭规则；`GetHashCode` 算法确定性承诺；`Operator =`/`<>` 按 VB 语义独立决定（对照 2018-02-28 元组相等性讨论）。
- **设计探索**：B1 替代词（`Required`/`Inject`）与 `Key` 的取舍论证；B2 与 2014-04-23 record 讨论的逐条对账表（哪些反对仍成立、哪些被字段变体化解）；与可空性流分析（参数注解）和交/并类型（多类型键的 `Equals`）的接口对齐。

---

## 附录：C# 生态与互操作考量

> 本节追加自 dotnet/csharplang 官方仓库镜像 `..\..\csharplang`（main 分支，2013–2026）。C# 原文均逐字引用并标注来源文件路径（相对 `csharplang`）；无法核实的点标 **OPEN QUESTIONS**。索引见 `..\..\csharplang-index.md`。

### 相关 C# 现实方向

本提案的两半在 C# 侧都有**已落地的主流对应物**。这既是机会（规格已有参照系）也是压力（VB 的价值主张需要差异化）。C# 影响新 CLR 功能与 .NET 生态走向（索引 T1），VB LDM 主线已退化为"C# 变体"，因此这两条 C# 现实必须被正视。

**1. 主构造函数（C# 12，已发布）——B1 的 C# 对应物，但方向相反。**

C# 12 主构造函数走的是"参数即字段"：`class Point(int x, int y);`。原文：

> "Classes and structs can have a parameter list, and their base class specification can have an argument list. Primary constructor parameters are in scope throughout the class or struct declaration, and if they are captured by a function member or anonymous function, they are appropriately stored (e.g. as unspeakable private fields of the declared class or struct)."
> → `proposals\csharp-12.0\primary-constructors.md`（Summary）

与本提案 B1 直接相关的行为有两条：

- **取消隐式无参构造**——C# 与 B1 的"默认构造取代"是同一决策："A class or struct with a `parameter_list` has an implicit public constructor whose signature corresponds to the value parameters of the type declaration. This is called the ***primary constructor*** for the type, and causes the implicitly declared parameterless constructor, if present, to be suppressed."（同上，Detailed design）。本会议把"取消无参构造"列为 B1 的最大 breaking change；C# 已用相同语义上市并被 DI/序列化生态接受。这为 B1 提供了生态先例，但也暴露**可见性差异**：C# 的取消由类头参数列表明示，B1 藏在一个字段修饰符里——C# 自己在 Drawbacks 里承认 "The allocation size of constructed objects is less obvious"。
- **捕获的元数据表示**：主构造参数被捕获时成为带 mangled 名字的私有字段，spec 只承诺实现策略、不承诺具体名字（"A likely implementation strategy is via a private field using a mangled name"）。除此之外**没有**任何元数据标记能识别"这是主构造类"——`.vbx` 消费 C# 主构造类时，在 IL/反射层面看到的就是普通类 + 普通构造 + 私有字段。**双向透明**：C# 主构造对 VB 调用方零障碍，但工具要"认出"它只能靠启发式。

**2. "Combined parameter and member declarations" 扩展——C# 离 B1 最近、但明确推迟的形态。**

主构造提案的 Possible extensions 里有一条允许在参数上放访问修饰符、让它同时声明成员：

```csharp
public class C(bool b, protected int i, string s) : B(b) // i is a field as well as a parameter
```

C# 的评价："This is a potential future addition that can be adopted or not. The current proposal leaves the possibility open."（`proposals\csharp-12.0\primary-constructors.md`，Possible extensions）。C# 暂缓的理由正是参数/字段命名约定差异与内联属性语法不美观——B1 用 VB 的字段声明语法恰好绕开这两点。这构成 B1"不是偷运旧货"的又一佐证，但也说明该方向在 C# 侧**非主流**：C# 选择了 B1 的镜像（字段即参数 vs 参数即字段）。

**3. `field` 关键字（C# 14，已发布）——合成 backing field 成为一等公民。**

> "Extend all properties to allow them to reference an automatically generated backing field using the new contextual keyword `field`."
> → `proposals\csharp-14.0\field-keyword.md`（Summary）

C# 14 让属性访问器直接读写编译器合成的 backing field（`set => field = value`），并对合成字段的可空性定义了"null-resilience"专项规则。这与 B2 生成的 `Sub New` 写只读 autoprop backing field、B1 生成的构造写字段，共享同一 lowering 心智：**合成字段/成员是语言特性的正当产出**。B2 若落地，其生成代码的可空性可参考该模型。

**4. record / record struct（C# 9 / 10，已发布）——B2 的直接对应物。**

record struct 合成的相等性成员原文：

> "The synthesized equality members are similar as in a record class (`Equals` for this type, `Equals` for `object` type, `==` and `!=` operators for this type), except for the lack of `EqualityContract`, null checks or inheritance."
> → `proposals\csharp-10.0\record-structs.md`（Equality members）

record struct 还合成 `PrintMembers`/`ToString`、主构造、按参数名的 public 属性、`Deconstruct`，并支持 `with`。B2 的 `Sub New`/`Equals`/`GetHashCode` 三件套是 record struct 合成面的**子集**——C# 已把双 `Equals`、`GetHashCode` 算法、`==`/`!=` 运算符这些规格问题逐一解过，B2 的 spec 可直接以 `record-structs.md` 为参照系。**元数据识别**是 record struct 的历史痛点：`proposals\csharp-10.0\record-structs.md` 的 Open questions 原文 "how to recognize record structs in metadata? (we don't have an unspeakable clone method to leverage...)"；且 `meetings\2020\LDM-2020-11-11.md` 记录客户靠 `<Clone>$` 方法存在与否判断"是否 record"——record class 尚可启发式识别，**record struct 至今没有专用元数据标记**。

**5. VB 在 C# 语言设计中的明确定位——unsafe-evolution 的原文表态。**

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
> → `proposals\unsafe-evolution.md`（VB 小节）

C# 团队对 VB 的默认立场是"VB 无 unsafe 上下文、无指针，因此不需要为 VB 扩展 unsafe 语言面"。其隐含面与本提案相关：VB 被预期保持**安全/无指针**的世界观，B1/B2 都不引入指针或 unsafe——方向与 C# 对 VB 的定位相容（索引 T8 / M8）。

### 现实 vs 提案

| 提案 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| B1（类字段自动构造） | C# 12 主构造函数（参数即字段，已发布） | **兼容但需差异化**（同目标、不同句法面） | 两者都消除构造样板、都取消隐式无参构造；C# 从参数端、B1 从字段端出发。C# 已把"参数声明字段"推迟，B1 正好是互补镜像。元数据层对 VB 调用方透明（普通构造 + 私有字段），互操作无障碍。风险在**隐蔽语义**：C# 的取消无参构造在类头可见，B1 藏在字段修饰符里。 |
| B2（结构体值对象三件套） | record struct（C# 10，已发布） | **兼容，但"特色"叙事需重写** | B2 三件套是 record struct 合成面的子集。生态里 C# record struct 已占领同一生态位；B2 若只是"VB 版 record struct"，是对主流特性的追赶而非特色。真正的 VB 差异点在：`Key` 逐成员声明（vs 类型级 `record`）、VB 显式 `Implements` 接线、`Operator =` 按 VB 语义。 |
| `Key` 关键字 | C# 无对应 | **脱节（VB 特色面）** | C# 用 `record`/`init`/`required` 表达同类概念；`Key` 是上下文关键字、不是 CLR 特性，与 C# 元数据零冲突。 |
| 生成的 `Equals`/`GetHashCode` | record struct 合成相等性 | **兼容且可参照** | 算法、双 `Equals`、运算符问题 C# 已定案（`record-structs.md`）；B2 可复用它，避免重踩 2014 年 "no agreed-upon answers" 的旧账。 |

**冲突 / 需桥接的点：**

1. **record struct 无元数据标记**：C# 的 record struct 与普通结构体在元数据上不可区分（open question 未解决）。B2 若推出自己的"合成值对象"，CLR 层同样没有现成标记。若要反射/序列化/AOT 工具能认出"这是 `Key` 值对象"，`.vbx` 需要**自备标记属性**——这是空白地带而非冲突，是我们可以自行决定的桥接点。
2. **VB `=` 语义 vs C# record struct 的 `==`/`!=`**：C# record struct 合成 `==`/`!=` 运算符（"The record struct includes synthesized `==` and `!=` operators equivalent to operators declared as follows:..."，`record-structs.md`）。VB 结构体的 `=` 在未定义运算符时的回落规则与 C# 不同（本会议已引 2018-02-28 元组相等性讨论）。`.vbx` 消费 C# record struct 时，`=` 是否走 `op_Equality` 必须按 VB 语义核实——**OPEN QUESTION**。
3. **B1 的破坏性与 C# 主构造的"可见性差异"**：同一程序集内 C# 主构造（类头可见）与 VB `Key` 字段（修饰符暗示）并存时，DI/序列化工具对两者的行为预期应一致，但发现方式完全不同。桥接点是让 B1 在语义模型里给出与主构造等价的"构造签名"信息，供工具消费。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：B1 生成构造、B2 生成相等性都是**编译期确定性代码、零反射**——与 C# 的 source-gen / AOT 方向（索引 T5/T6：类型系统承担更多职责）同向，不冲突。真正与 AOT/trimming 张力大的仍是 `Any`/晚期绑定那条线（决策文件 M2/M5），本提案不加重该负担。`Key` 值对象应保持"默认安全"：生成的 `Equals`/`GetHashCode` 确定性、不依赖反射，天然 AOT 友好。
- **source-gen 桥**：C# 生态的互操作正转向 source generators（`[LibraryImport]`、序列化生成器）。`.vbx` 的 B2 生成成员应作为**真实 CLR 成员**发射（同 C# record 合成成员），使 C# 源生成器/分析器可照常消费。需注意：C# 主构造对**捕获参数**的 `field:` 目标特性明确不允许——primary-constructors.md Open questions 结论 "Not allowed"（LDM-2023-05-03）；但对 **record 参数**允许（属性合成时特性落到 backing field）。B2 若需字段/属性目标特性，应参照 record 的规则（允许），而非主构造捕获参数（不允许）。
- **识别新元数据**：`.vbx` 消费现代 C# 程序集时需要认识的编译器合成元数据清单：`IsExternalInit`（init-only）、`[CompilerFeatureRequired]`（特性门控标志）、`RequiredMemberAttribute`/`SetsRequiredMembersAttribute`（C# 11 `required`）、record class 的 `<Clone>$`（启发式识别 record）。**主构造捕获字段与 record struct 无专用标记**——工具识别只能靠成员形状 + `CompilerGenerated` 启发式，可靠性有限，应列为 **OPEN QUESTION**。
- **与 C# 主构造类的互操作**：`.vbx` 调用 C# 主构造类在 IL 层无障碍（就是普通构造）。但 B1 型工具（"列出此类型的构造依赖"）消费 C# 主构造类时**拿不到**参数与状态的对应信息——C# 侧无元数据。故 B1 的 IDE/DI 集成特性只能对 `.vbx` 自产类型完整生效，跨语言需降级为"仅构造签名"。

### 对既有 RESOLUTION / 三态判定的影响

**三态结论不变（B1=Consider、B2=Table），但举证内容被显著改变：**

1. **B1（Consider）→ 得到生态先例，但"VB 特色"叙事需收窄。** C# 12 已用主构造函数发布"取消无参构造 + 自动接线"，B1 的价值从"首创消除样板"收窄为"以 VB 字段声明形态达成同一目标"。这反而**支持** RESOLUTION 第 2 条的方向采纳——C# 证明该目标可落地；同时给"显式构造互斥""继承规则"等规格问题提供了 C# 参照系（C# 的 `this` 初始器链、`class_base` argument list、double-storage warning 均可借来审 B1）。
2. **B2（Table）→ `record-structs.md` 可作"参考答案"解封 2014 旧账。** 会议挂 Table 的理由是"规格未熟、2014 旧账未清"。C# 的 `record-structs.md` 已把 B2 的每个未决点（双 `Equals`、`GetHashCode` 算法、`==`/`!=`、PrintMembers）定过案；B2 复活时不必从零设计，而应**逐条对照 `record-structs.md` 决定采纳还是偏离**。这降低 B2 的成本估值，但**不改变 Table 状态**——"VB 显式 `Implements` 接线"与"`Operator =` 按 VB 语义"是 C# 文档未覆盖的 VB 特有缺口，仍需自研。
3. **新增一个必须桥接的 OPEN QUESTION**：`.vbx` 值对象（B2）与 C# record struct 在元数据层**互不可分**（都无标记）。若 VBScript.NET 愿景包含"脚本层与 C# 层共享值类型语义"，需决定是否自备识别标记——这是本附录新引入、原 RESOLUTION 未覆盖的问题。
4. **关键字 `Key` 与 C# 生态零冲突**：`Key` 不是 CLR 特性、不影响元数据，C# 侧无同名词汇冲突——RESOLUTION 第 4 条（`Key` 复用消歧）的讨论可局限于 VB 内部，不构成跨语言互操作负担。

### OPEN QUESTIONS（本附录引入）

- `OPEN QUESTION`：B2 值对象是否需要**自备元数据标记属性**以区别于普通结构体（C# record struct 无现成标记，是空白地带）。
- `OPEN QUESTION`：`.vbx` 的 `=` 在消费 C# record struct 时是否走其合成 `op_Equality`——需按 VB 结构体 `=` 回落规则核实（对照 2018-02-28 元组相等性讨论）。
- `OPEN QUESTION`：识别 C# 主构造类 / record struct 的启发式（成员形状 + `CompilerGenerated`）在 `.vbx` 工具链中的可靠性要求。
