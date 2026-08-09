# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这次我们把**接口实现**这一整块拿到桌面上。它比预想更贴着主线历史的边缘：vblang 在 2014 年就把"隐式接口"标为 *Tentatively approved, but still needs design work. Parity with C#.*，随后从未落地；2017 年在默认接口实现（DIM）的讨论里又把它定为 error，并留了一句 *evaluate implicit interface implementations separately* 的口子。所以今天我们不只是在评估 Anthony 原文第 15 章的两段代码，也是在跟主线的旧账对话。

## Agenda

* [Proposal: 隐式接口实现与签名放宽（Implicit Interface Implementation）](#proposal-隐式接口实现与签名放宽)

## Proposal: 隐式接口实现与签名放宽

_Related: [vblang 2014-02-17 LDM #37 – Implicit Interfaces](https://github.com/dotnet/vblang)；vblang 2017-05-19 LDM – Default Interface Implementations；`spec/general-concepts.md` §Implementing Methods；Anthony 原文第 15 章 "Inheritance, Interface Implementation, and Extension"_

本建议分两半，必须分开谈——它们恰好是两条独立的特性，只是 Anthony 把它们写进了同一段代码块。

- **半 A（隐式接口实现）**：类写了 `Implements IDisposable` 之后，同名同签名的 `Public Sub Dispose()` 自动成为 `IDisposable.Dispose` 的实现，不再需要 `Implements IDisposable.Dispose` 子句。Anthony 的标注是 *"(Great for code generation!)"*。
- **半 B（显式接口实现的签名放宽）**：一个方法可以用协变返回类型同时满足多个接口成员——`Function GetEnumerator() As IEnumerator(Of Student) Implements IEnumerable(Of Student).GetEnumerator, IEnumerable.GetEnumerator`，注释 *"No need for second method."*。

### 场景与缺口

先摆现状。`spec/general-concepts.md` §835 对接口实现匹配的要求是：

> A type *implements* a type member of an implemented interface by supplying a method with an `Implements` clause. The two type members must have the same number of parameters, all of the types and modifiers of the parameters must match, including the default value of optional parameters, the return type must match, and all of the constraints on method parameters must match.

关键在 "**the return type must match**"——返回类型必须严格相等。而 §861 又允许一个方法实现任意多个接口成员，只要"以上判据全部成立"：

> A single method may implement any number of interface type members if they all meet the above criteria.

所以半 B 的痛点是直接由规范制造的：`IEnumerable(Of Student)` 继承了非泛型 `IEnumerable`，两个接口成员一个要求 `IEnumerator(Of Student)`、一个要求 `IEnumerator`。今天你**不能**让一个方法同时满足两者（返回类型不等），又**不能**写两个同名方法（VB 不允许按返回类型重载），于是规范自带的示范就是两个不同名字的方法：

```vb
' 今天的现状：必须写两个不同名字的方法（无法按返回类型重载）。
' 参照 spec/general-concepts.md 的 String/Exception 范例（§1976–1987）。
Class StudentCollection
    Implements IEnumerable(Of Student)

    Public Function GetEnumerator1() As IEnumerator(Of Student) _
        Implements IEnumerable(Of Student).GetEnumerator
        ...
    End Function

    Public Function GetEnumerator2() As IEnumerator _
        Implements IEnumerable.GetEnumerator
        ...
    End Function
End Class
```

任何实现泛型集合的类都要为 `GetEnumerator` 付出这一份样板——这是**普通得不能再普通**的场景。相比之下，半 A 的场景更窄：代码生成器（source generator / T4）输出的类要逐成员填满 `Implements` 子句，纯机械噪音。

而这片地不是空白的。2014-02-17 LDM 的 **#37 Implicit Interfaces**：

- 标题旁标着 *"Tentatively approved, but still needs design work. Parity with C#."*，主场景正是 **"code-generators and partial classes"**——与 Anthony 的动机一字不差。
- 讨论了三个方案：Proposal 1（先找显式实现，找不到再按名隐式匹配，命中即警告）、Proposal 2（始终按名匹配、取最派生类型、劫持已编译代码时警告）、Proposal 3（**新语法** `Implicitly Implements I1` / `Auto Implements I2`，整体照搬 C#）。
- 结论是 **"RESOLUTION: Yes. Use Proposal3. We will look for a keyword combination that seems nice."**——主线当年选择了**声明式**（在类型上显式声明"本类型按名匹配"），而不是静默式。

2017-05-19 LDM（Default Interface Implementations）又补了一刀：

> Because of the way the CLR looks up interface implementations (by name), if an interface declares an overridable member *and* an implementor declares or inherits a public `Overridable`/`Overrides` member of the same name it will implicitly be picked up as the implementation/override of that interface member.

> **Decision Let's make it an error for now and evaluate implicit interface implementations separately.**

所以我们看到的不是"空白需求"，而是主线两次走到边缘、两次都没落地的一块地。今天必须把账算清。

### 候选方案

**PROPOSAL A — 裸隐式（Anthony 原案 = 静默式）。** 类 `Implements IDisposable`，编译器在找不到显式子句时，按名+签名把 `Public` 成员认作接口实现。零新语法、零额外仪式。这就是 2014 的 Proposal 1/2 路线（先 fallback、按名取最派生）。

**PROPOSAL B — 声明式隐式（2014 Proposal 3 路线）。** 在类型级 `Implements` 上加意图声明（`Implicitly Implements I1`），只有被声明的类型才启用按名匹配。显式子句存在时优先，缺失时"start looking in the most derived type"。2014 的判断是这条路能让劫持 **"impossible"**。

**PROPOSAL C — 维持显式 + IDE/analyzer 补全。** 语言不动，用 Roslyn 重构/快速修复把缺失的 `Implements` 子句补到同名同签名的公共成员上。2014 自己就留了这句：*"There may be IDE codespit ameliorations."* 这是本建议 Alternatives 第三条的展开。

**PROPOSAL D — 协变返回签名放宽（半 B，与 A/B/C 正交）。** 放宽 §835 的返回类型判据为"相同或可引用转换（协变）"，编译器为不精确匹配的接口槽合成**桥接方法**。只影响"今天会报错的代码"，纯增量。

### 权衡：Q&A

- **A 的核心问题：劫持。** 2014 讨论里唯一反复回访的就是"把接口实现加进基类不应是破坏性变更"（*"adding interface-implementation to a base case should not be a breaking change"*）。A 恰恰在这个原则上失守：一个当前能编译的类，重编译后接口分发可能落到另一个方法。见下方 Breaking change 追问的具体例子。**这是 A 被否的核心理由。**
- **A vs B：静默 vs 声明。** B 把"按名匹配"变成类型的显式意图，所以从未声明过的既有代码永远不会被劫持；A 把"按名匹配"变成默认行为，既劫持又难发现。2014 在 A 与 B 之间已经选过一次，答案是 B。
- **A 的"可发现性"债。** 2014 问了 *"Q. How discoverable will it be?"*。显式 `Implements` 子句是 VB 实现关系的**可见契约**；A 把它移进编译器。改 IDE 补全、改"查找实现"，成本全部后置到工具链。
- **B 的尴尬：越想要的语法越啰嗦。** 2014 原话：*"it's a weird situation that the more desirable syntax 'Implicitly Implements' is more verbose than the less desirable traditional syntax 'Implements'."* 这意味着 B 并不是"消除仪式"，而是"把仪式换了个样子"。对代码生成器是净赚（生成器反正要打字），对普通手写代码是零赚。
- **D 为什么比 A 安全得多。** D 只允许一种**今天写出来就是编译错误**的新写法，不改变任何现有合法代码的含义。它不需要"按名猜测"，仍然要求显式列出两个接口成员——实现关系依旧可见。**纯增量、零破坏。**
- **D 的机制欠账。** CLR 的接口槽（MethodImpl）要求签名精确相等，协变返回必须靠编译器合成精确返回类型的桥接方法。这在 2014 就有先例——C# 侧 *"in C#, the CLR likes interface implementations to be virtual, so C# compiler creates a bridge method."* 本建议对桥接方法只字未提（详见成本追问）。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

A 与 D 都不新增任何 token——A 只是把"当前报错"改为"匹配成功"，D 只是放宽绑定判据，`Implements` 逗号列表文法早已支持多成员（`spec` §873：`Sub F(i As Integer) Implements ITest.F, ITest.G`）。**文法层面无歧义；歧义全部在语义层**（哪个成员匹配哪个接口成员）。B 若落地则是真正的文法问题：`Implicit` / `Auto` 目前都不是 VB 关键字，把它们变成关键字会与用作标识符的既有代码冲突（罕见但真实），2014 的 "look for a keyword combination that seems nice" 正说明这条路的地雷在措辞上。

#### 2. 角案例与边界语义

**同名不同签名的接口成员。** `ILeft.Test(x As Integer)` 与 `IRight.Test(x As String)`：隐式匹配下，两个公共重载各自匹配一个成员，语义是确定的；`ILeftRight` 继承 `ILeft, IRight` 时 spec（§697–706）仍要求显式引用 `ILeft.Test` / `IRight.Test`，隐式匹配反而更宽松——这是收益还是隐患？我们 `Probably` 认为收益（与 C# 一致，一个同名成员天然满足多个同签名成员），但需写清楚"同名**同签名**才合并，同名不同签名按各自签名匹配"。

**同名同签名的多接口合并。** spec 允许一个方法实现任意多个接口成员；隐式匹配下这是自然行为：

```vb
Interface ILeft2
    Sub Test(x As Integer)
End Interface

Interface IRight2
    Sub Test(x As Integer)
End Interface

' 隐式实现：一个公共 Test 同时满足 ILeft2.Test 与 IRight2.Test。
Class Both
    Implements ILeft2, IRight2

    Public Sub Test(x As Integer)
    End Sub
End Class
```

**重载的"劫持"不是重载。** 接口成员 `Sub M(x As Integer)` 只被签名完全相等的公共方法匹配，不发生重载解析（spec 判据是参数**类型**必须相等）。2014 讨论里担心的"换成另一重载"场景（§738–743）是"基类新增接口实现后，外层调用点的重载解析漂移"，那是另一种破坏——但说明**接口状态会影响重载解析**，值得在兼容性分析里单列。

**可访问性。** 今天的 VB 允许 `Private Sub Dispose() Implements IDisposable.Dispose`（spec §1656）。隐式匹配**不能**把 `Private` 成员当候选——CLR 按名分发看不到私有方法，C# 也要求 public。所以隐式匹配的候选门槛是 `Public`（`Friend`/`Protected` 是否可作候选——`OPEN QUESTIONS`），而显式子句继续允许 `Private`。**两者共存，不互相替代。**

**MustOverride / MustInherit。** 抽象公共成员可否作为隐式实现？C# 允许（抽象成员照常满足接口）。VB 的 spec §710–736 展示了 `MustOverride Sub Test2() Implements ITest.Test2` 的显式形态；隐式形态应同样允许，派生类 `Overrides` 后接口分发自动跟随。

**泛型接口的类型实参对应。** spec §878–901 要求实现方法用类型实参对应接口的类型参数（`Implements I1(Of W, X).M`）。隐式匹配同样要处理这种替换后的签名对齐——这不是新问题，但要在匹配算法里显式做。

#### 3. 作用域与绑定

A 与 D 都要求语义模型回答"接口成员 X 由哪个成员实现"——这在 Roslyn 里是 `FindImplementations` 的领地。显式子句下这是语法直连；隐式下必须提供查找入口。D 额外要求：桥接方法是**合成的**，语义模型应把**声明的方法**（而非桥）报告为两个接口成员的实现，IDE 的"转到实现"才不会指到不可见的方法。`Suspect`：符号 API 需要新设计，spec 未写，只能靠原型验证。

#### 4. 与既有特性的交互

- **`Shadows` / 重实现**：这是劫持的主战场（见第 5 条）。
- **`Overridable` / `Overrides`**：半 B 的桥接方法必须是虚拟的（若实现方法是 `Overridable`），派生类的 `Overrides` 才能继续满足接口——这正是 C# bridge method 存在的原因（2014 引文）。**若派生类再用更派生返回类型 `Overrides`**（Anthony 第 15 章上半段就是协变覆盖 `Clone() As Derived Overrides Base.Clone`），CLR 对覆盖签名要求精确，派生类又得再合成一层桥——必须与 `proposal-override-sig`（组 16）对表。
- **协变泛型接口的菱形歧义**：spec §1972–1994 的 `IEnumerable(Of String)` + `IEnumerable(Of Exception)` 警告与本特性正交，D 不改变它。`StudentCollection` 只 `Implements IEnumerable(Of Student)`，菱形不出现。
- **COM / WinRT**：编译器始终生成显式 MethodImpl，A/D 都不改变这一事实，COM 映射无回归。
- **`Handles`、XML 文档、partial member 的 `Implements` 子句**（Anthony 的 partial-members 建议）：与代码生成场景共享实现面，需协调。

#### 5. Breaking change 与兼容性

这是 A 的死刑现场，也是 D 的免死金牌。

**A 的破坏性例子**（等价于 2014 的 E 例，§729–733）：

```vb
Class Base
    Implements IDisposable

    Public Overridable Sub Dispose() Implements IDisposable.Dispose
    End Sub
End Class

Class Derived
    Inherits Base
    Implements IDisposable ' 重实现

    Public Shadows Sub Dispose()
    End Sub
End Class

' 今天：通过 IDisposable 调用 Derived 实例 → Base.Dispose。
'       （Derived.Dispose 没有 Implements 子句，spec §738–776 规定未重实现的成员沿用基类。）
' 隐式实现后（Proposal 2 的"always prefer the most derived type"规则）：
'       → Derived.Dispose。同一源码、同一编译器开关，重编译后调用不同方法。
```

这正是 2014 把 Proposal 1/2 否掉、转向 Proposal 3 的核心理由——主线当时对 Proposal 2 的裁决是：*"we'll look up implicitly by name, and always prefer the most derived type. But we'll add a warning when you would be hijacking code that already compiles."* 而我们一贯的原则是**对破坏性变更几乎零容忍**。靠"警告"对冲破坏不是我们的默认路径。

**D 无破坏**：它只把"编译错误"变成"合法代码"。既有合法代码的含义一行都不变；唯一需要的是 `langversion` 门控（避免旧编译器不认新语义）。

#### 6. Option Strict / 编译选项分叉

接口实现是静态解析的，`Option Strict` 不参与接口槽的绑定；半 A 半 D 在严格/宽松两条路径下行为一致。**无分叉。**（宽松模式下 `Object` 上的晚期绑定与接口分发无关。）

#### 7. IDE / IntelliSense

- "查找实现"、`GetTypeInfo` 的符号链接、补全里的"implements"字形都必须反映隐式关系。
- 对 D：桥接方法不可见，IDE 必须把声明方法标为两个接口成员的实现。
- 对 A：开发者无差别看到一堆公共方法，无法判断谁在满足哪个接口——可发现性债最大。2014 的 *"How discoverable will it be?"* 至今没有答案。
- 对 C：这是 IDE 的舒适区（补全/重构生成子句），风险为零。

#### 8. 数据 / 普遍性

半 B 的普遍性**硬**：每个泛型集合都撞上 `IEnumerable(Of T)` + `IEnumerable` 的双 `GetEnumerator`。这是 85% 场景。半 A 的普遍性**软**：code generator 输出确实高频，但"手写类想少打子句"的需求没有数据。`Suspect`：真实但未量化——2014 把它归因于 code-generators 和 partial classes，与我们看到的一致。

#### 9. 更简替代

- 半 A 的替代就是 **PROPOSAL C**：Roslyn 的 "Implement Interface" 已经能整包生成子句；再加一个"给匹配成员补 `Implements` 子句"的快速修复，代码生成器场景 80% 的价值就到手，语言风险为零。**我们倾向先把钱花在这。**
- 半 B **没有**更简替代：`GetEnumerator1`/`GetEnumerator2` 泄漏到公共面（虽然它们不是公共成员之外的负担——实际上它们必须 public 才能实现接口），双方法样板是规范强制的，语言层面不改就没有出路。

#### 10. 复杂度 / 成本 / 优先级

- A：实现极便宜（去掉一个报错 + 按名查找），但 spec 工作（劫持规则、可发现性、警告策略）与风险成本全在后端。**"值得做但太难"的典型**——热情不抵消可行性。
- B：新关键字 + 文法 + IDE 全链路，成本中等，价值与 C 重叠。2014 没落地不是偶然。
- D：绑定放宽 + 桥接方法合成 + 语义模型/IDE 适配，成本**中低**（桥接合成是编译器已有模式——C# 一直这么干），价值高（高频样板）、风险低（纯增量）。**性价比最高。**

#### 11. 运行时 / CLR 硬约束

- 桥接方法：CLR 要求接口槽签名精确，所以 D 必须为 `IEnumerable.GetEnumerator` 槽合成一个返回 `IEnumerator` 的私有方法、`Return Me.GetEnumerator()`，再以 MethodImpl 挂到该槽；`IEnumerator(Of Student)` 与 `IEnumerator` 之间是引用转换，PEVerify 无碍。若 `GetEnumerator` 是 `Overridable`，桥内调用走虚拟分发，派生覆盖继续生效。**这条路 CLR 完全允许，且是 C# 已验证的先例。**
- 半 A 绝不能依赖 CLR 运行时按名分发——VB 大小写不敏感、值类型/结构实现、以及确定性要求都意味着**编译器必须生成显式 MethodImpl**，把隐式匹配变成"编译器定义"而非"运行时启发"。

#### 12. 值不值得做

| | 价值 | 成本 | 风险 | 结论 |
|---|---|---|---|---|
| A 裸隐式 | 中（codegen） | 低-中 | **高**（劫持/破坏、可发现性） | 不做 |
| B 声明式隐式 | 中 | 中-高（新关键字） | 低（opt-in） | Table |
| C IDE 补全 | 中-高 | 低 | 无 | 先做 |
| D 协变返回放宽 | **高**（高频样板） | 中低 | 低（纯增量） | 做 |

### VB 基因对照

- **消除常见样板（原则 #9）**：D 正中靶心，且命中 85% 场景。A 命中 codegen 窄场景。
- **不引入"第二种做事方式"（原则 #3）**：A 制造"隐式 + 显式"两套实现语义，违反。D 不制造——它只放宽显式匹配，仍然只有一种表达。B 制造"第二种方式"但靠 opt-in 隔离。
- **避免隐蔽的语义变化（原则 #7）**：A 是教科书级别的隐蔽语义变化（同一源码、重编译、不同调用），`Return?` 被拒的同类理由。D 无隐蔽变化。
- **保持 VB-like / 读起来像英语（原则 #2、#5）**：显式 `Implements` 子句是 VB 实现关系的可见契约，是"冗长只在有用时是美德"（原则 #10）的正面教材。A 把它藏进编译器，是对基因的偏离；D 保留显式列名，只省第二个方法。
- **永不破坏现有代码（原则 #1）**：A 违反（见第 5 条）；D 不违反。
- **与主线关系（对照表 2.3）**：本建议**不在** 2.3 表的任何一行里——它是 Anthony 在接口领域的独立延伸。主线锚点是 2014-02-17 #37（Tentatively approved，选声明式）与 2017-05-19 DIM（隐式实现暂为 error、留 separate evaluation 口子）。**半 A 与主线"声明式"路线冲突**；**半 D 主线从未考虑过**，属 Anthony 比 C# 更激进的延伸（C# 对接口实现同样要求返回类型精确匹配——`Probably`，基于 C# 9 协变返回仅限 override 的公开事实与 CLR 槽签名约束推断）。

### RESOLUTION:

1. **把建议拆成两条独立特性。** 半 A 与半 B 风险画像完全相反，捆绑评审没有意义。
2. **半 B（协变返回签名放宽）原则上采纳（Active）。** 范围收敛为：仅**引用类型协变**（返回类型相同或经引用转换可向上转换）；仅方法（只读属性的 `Get` 访问器同理可行——`OPEN QUESTIONS` 确认；事件无返回类型）；仍要求显式 `Implements` 列出全部成员，不做隐式合并。**编译器为不精确匹配的槽合成精确返回类型的桥接方法，实现方法为 `Overridable` 时桥内走虚拟分发。**
3. **半 A（裸隐式）否决不落地（Reject）。** 核心理由：劫持破坏（2014 Proposal 1/2 因此被否、转选 Proposal 3）、可发现性债、与"显式 `Implements` 是 VB 基因"相悖。代码生成场景交由 **PROPOSAL C**（IDE 快速修复补 `Implements` 子句）承接——这是零风险、高覆盖的替代。
4. **半 A 的声明式变体（PROPOSAL B）留档为 Consider。** 若 IDE 路径被证明不足、且社区对"类型级声明按名匹配"有明确呼声，按 2014 Proposal 3 路线（`Implicitly Implements`）重提，但需一并解决新关键字与既有标识符的冲突与"越想要越啰嗦"的仪式问题。
5. **spec 修订项（半 B）**：改写 §835 "the return type must match" 为"相同或可引用转换（协变）"；在 §861 的"single method may implement any number of interface type members"后补协变判据与桥接方法说明；`langversion` 门控 + 警告策略（对菱形歧义保持既有警告不变）。
6. **与 `proposal-override-sig`（组 16）对表**：协变覆盖与协变接口实现的桥接方法在派生类里会叠加，必须共用一套合成规则。

### Implication:

- 起草 speclet：匹配判据（参数类型、修饰符、可选参数默认值**保持严格**，仅返回类型放宽为协变）、桥接方法合成规则（精确返回类型、虚拟性、访问级别）、语义模型/IDE 表面。
- 撰写最小原型：`IEnumerable(Of Student)` + `IEnumerable` 双 `GetEnumerator` 场景，验证 MethodImpl 布局、`Overridable` 派生覆盖、PEVerify。
- 为 PROPOSAL C 立项：Roslyn 快速修复"把 `Implements` 子句补到同名同签名公共成员"，作为半 A 的官方替代交付。
- 补 Compatibility 分析：`Shadows`/重实现/`MustOverride`/泛型类型实参逐条列证；`langversion` 门控策略。
- 与 partial-members、iface-delegation（第 15 章其余建议）对表，避免代码生成场景的实现面分叉。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：半 A 若未来以声明式（B）落地，隐式匹配候选是否允许 `Friend`/`Protected`（仅 `Public`？）。
- `OPEN QUESTIONS`：半 B 的协变放宽是否扩展到只读属性的 `Get` 访问器（属性无参、返回类型协变等价于方法）。
- `OPEN QUESTIONS`：桥接方法的可发现性——`FindImplementations` / `GetTypeInfo` 对"声明方法 vs 桥"的报告契约。
- `OPEN QUESTIONS`：若同一接口族的两个成员返回类型**不是**继承关系（如 `String` 与 `Exception` 对 `Object`），半 B 不适用；是否要报明确错误而非静默失败。
- `TODO`：量化 codegen 场景对隐式实现的真实需求（若 IDE 快速修复覆盖 80%，剩 20% 值不值得新语法）。
- `Follow-up`：与 `proposal-override-sig` 的桥接合成规则共享一份 speclet；跟踪主线（若 vblang 重启隐式接口评估，对齐 2014 Proposal 3 的措辞选择）。

### 状态

- **LDM 状态：Active（半 B）**；半 A Reject（裸隐式）、Consider（声明式）；PROPOSAL C 作为替代立项。
- **三态判定：部分采纳** — 拆包后，协变返回签名放宽 Active；裸隐式 Reject；声明式隐式 Table/Consider。

---

## 附录：特性评价

# 建议评价报告：proposal-implicit-interface-implementation.md

## 评价对象

- 建议：proposal-implicit-interface-implementation.md — 隐式接口实现 + 显式接口实现签名放宽
- 来源：Anthony 原文第 15 章 "Inheritance, Interface Implementation, and Extension"（`..\AnthonyDesign_wordpress.txt` L2430–2510；`ResourceHandle` 与 `StudentCollection` 两例逐字对应 L2450–2471）
- 配方目标：类实现接口免逐成员 `Implements` 子句（服务于代码生成）；一个方法以协变返回类型同时满足多个接口成员（消除 `GetEnumerator` 双方法样板）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。半 B 的改进明确且示例可操作（双 `GetEnumerator` 样板真实可复现，spec §1976–1987 佐证）；半 A 的效果仅覆盖 codegen 窄场景，且其"实现接口更省事"的主张被劫持风险部分抵消 | 已检查 | 状态行为占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`），无原型封顶 3；半 A 的破坏性后果（重编译行为漂移）未在效果侧评估 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。半 B 保留显式 `Implements` 列名（VB 基因），协变放宽是 Anthony 超 C# 的延伸；半 A 是 C# 隐式接口的**直接照搬**未做 VB 化（主线 2014 明确不要静默版）；且一份建议捆绑两条独立特性 | 已检查 | 半 A 与"显式可见契约"基因冲突（原则 #2/#10）；半 B 与 `proposal-override-sig` 的桥接叠加未提 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文逐字一致、3 个未决问题具体诚实（1–3 健康区间） | 已检查 | 无 Compatibility/breaking-change 章节（Drawbacks 一句带过劫持却不给例子）；**桥接方法机制全文未提**（半 B 的 CLR 关键路径）；半 A/半 B 未拆分；状态行占位链接 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。半 B 对水/光明显正向（高频样板、差异化优于 C#）；半 A 的暗风险（兼容破坏、隐蔽语义变化）突出且无对冲设计；文档未对两半分开权衡 | 已检查（预测待定） | 风=与主线 2014 声明式路线断裂未识别；暗=半 A 劫持破坏无 langversion/警告策略；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。半 A 本质是 C# 隐式接口实现（主线 2014 已标注 Parity with C#）全文未点明；桥接方法是 C# 已验证机制也未标注；半 B 属原创延伸未自认 | 已检查 | 未声明"借鉴 C#"；未声明"比 C# 更激进"（C# 接口实现要求返回类型精确匹配——`Probably`）；"继承 VB 显式 `Implements` 传统"隐含但未点明 |

## 设计原则对照

- **与 VB 基因：部分一致 / 部分偏离**。半 B 一致（消除样板 #9、纯增量不破坏 #1、保留显式列名 #10）；半 A 偏离（隐蔽语义变化 #7、第二种做事方式 #3、弱化可见契约 #2/#10）。
- **与主线关系：Anthony 独立延伸**，且半 A 与主线 2014 决策**方向冲突**（主线选声明式 Proposal 3 弃静默式 1/2，Anthony 回到静默式）；半 B 主线从未考虑，Anthony 比 C# 更激进。2.3 对照表无本建议对应行，锚点为 2014-02-17 #37 与 2017-05-19 DIM。
- **破坏性变更：半 A 有**（重实现/`Shadows` 劫持导致重编译行为漂移；2014 E 例即证）；**半 B 无**（只放宽"今天报错"的写法，纯增量，需 `langversion` 门控）。

## 总评

- **达成程度：部分达成**。半 B（协变返回签名放宽）概念成立、范围可缩、纯增量，是合格的候选；半 A（裸隐式）价值真实但风险与基因双失，作为独立特性不成立。
- **LDM 三态建议：半 B Active；半 A Reject（裸隐式）/ Consider（声明式）；PROPOSAL C（IDE 补全）替代半 A 交付**。整体建议须拆包后按此处理。
- **主要问题**：① 半 A 的劫持/破坏性分析缺失，且与主线 2014"声明式"决策方向冲突；② 桥接方法这一半 B 的 CLR 关键路径全文未提；③ 两份独立特性捆绑，风险画像互相稀释；④ 无 Compatibility 章节、状态行占位。

## 返工建议

- **拆分**：把隐式接口实现与协变返回签名放宽拆成两份独立建议，各自配 Drawbacks/Alternatives/Compatibility。
- **补充章节（半 B）**：spec §835/§861 的修订文字（"return type must match"→"相同或可引用转换"）；桥接方法合成规则（精确返回类型、`Overridable` 虚拟分发、与 override-sig 的叠加）；引用类型协变范围界定（结构体枚举器 `List(Of T).Enumerator` 不适用）；菱形歧义（§1972–1994）交互确认。
- **补充章节（半 A 若重提为声明式）**：2014 Proposal 3 措辞选择的回访；新关键字冲突分析；隐式匹配候选的可访问性（`Public`/`Friend`/`Protected`）；most-derived 匹配规则与 `Shadows`/重实现的显式判例。
- **补充证据**：最小原型（`IEnumerable(Of T)`+`IEnumerable` 双 `GetEnumerator` 场景）验证 MethodImpl 布局与 PEVerify；语义模型 `FindImplementations` 对桥接方法的报告；codegen 场景需求数据；IDE 快速修复（PROPOSAL C）的覆盖率评估。
- **未决问题处理**：可选参数默认值**保持严格**（2014 已决议 "Stick with current VB rules... match based on signature, and then give an error if the defaults don't match"）；只读属性 `Get` 协变纳入范围；桥接可发现性契约待原型定。

---

## 附录：C# 生态与互操作考量

> 本附录把本提案放到 C#/CLR/.NET 的现实坐标系里看。素材来自 csharplang 索引（`..\..\csharplang-index.md`，重点 M7/T8）与对库内原文的逐字核实。C# 原文引用处均标注来源文件路径，无法核实的标 **Suspect** / **OPEN QUESTIONS**。只追加，不改写正文。

### 相关 C# 现实方向

本提案的主题——「接口成员由谁实现」——在 C# 侧不是一条平线，而是三条互相咬合的走向：

1. **C# 1.0 起「public 成员即隐式接口实现」是出生即默认。** C# 从未有过 VB 式的 `Implements` 子句；接口映射（interface mapping）规则自 C# 1.0 起就是「同名同签名的类成员自动实现接口成员」——这正是半 A 的目的地。但注意 C# 的映射判据是**精确匹配**。`proposals\csharp-9.0\covariant-returns.md` 在修改映射规则时引用的现行条文是：

   > For purposes of interface mapping, a class member `A` matches an interface member `B` when: … `A` and `B` are methods, and the name, type, and formal parameter lists of `A` and `B` are identical.

   → `proposals\csharp-9.0\covariant-returns.md`（Implicit Interface Implementations 节）。这与 VB §835 "the return type must match" 在结构上是同一句——**C# 与 VB 在「接口实现要求返回类型精确匹配」上本就一致**。

2. **C# 9 协变返回：只放行 override，不放行接口映射。** C# 9 让 override 方法/只读属性可变派生返回类型，动机原文：

   > It is a common pattern in code that different method names have to be invented to work around the language constraint that overrides must return the same type as the overridden method.

   → `proposals\csharp-9.0\covariant-returns.md`（Motivation）。提案正文曾想顺手把协变扩展进接口映射，但立刻撞上破坏性变更判例：

   ```csharp
   interface I1 { object M(); }
   class C1 : I1 { public object M() { return "C1.M"; } }
   class C2 : C1, I1 { public new string M() { return "C2.M"; } }
   ```

   > This is technically a breaking change, as the program below prints "C1.M" today, but would print "C2.M" under the proposed revision.
   > Due to this breaking change, we might consider not supporting covariant return types on implicit implementations.

   → `proposals\csharp-9.0\covariant-returns.md`（Implicit Interface Implementations 节）。2020-01-08 LDM 拍板把接口映射排除在外：

   > Interface implementation would not change return types, while overriding would.

   → `meetings\2020\LDM-2020-01-08.md`（Covariant returns）。设计会议小节也留下结论 *"Offline discussion toward a decision to support overriding of class methods only in C# 9.0."* → `proposals\csharp-9.0\covariant-returns.md`（Design meetings）。**结论：C# 今天的接口实现（无论隐式/显式）仍要求返回类型精确匹配——半 B 在 C# 侧明确未放开。**

3. **C# 8 DIM：接口可带默认实现，接口实现语义从「纯抽象槽」变为 most-specific-implementation。** 动机原文：

   > Default interface methods enable an API author to add methods to an interface in future versions without breaking source or binary compatibility with existing implementations of that interface.

   → `proposals\csharp-8.0\default-interface-methods.md`（Motivation）。DIM 还带来接口内 `override` 成员，以及一条与本提案「隐式匹配候选必须 Public」直接同向的决议：

   > Only public members may be implicitly overridden, and the access must match.

   → `proposals\csharp-8.0\default-interface-methods.md`（Overriding non-public interface members，closed issue，2017-04-18 决议）。

4. **C# 13 ref struct 接口：接口实现面继续扩张，但 DIM 对 ref struct 关上。** `ref struct` 可经 `allows ref struct` 实现接口，却：

   > A `ref struct` cannot participate in default interface members

   → `proposals\csharp-13.0\ref-struct-interfaces.md`。且添加 DIM 对既有实现者是破坏性的——与半 A 的劫持担忧同源：

   > API authors need to be aware that adding DIMS will break `ref struct` implementors until they are recompiled. This is similar to existing DIM behavior where by adding a DIM to an interface will break existing implementations until they are recompiled.

   → `proposals\csharp-13.0\ref-struct-interfaces.md`。

5. **元数据层：接口槽由 InterfaceImpl + MethodImpl 记录，签名精确。** 半 B 的桥接方法（为 `IEnumerable.GetEnumerator` 合成返回 `IEnumerator` 的私有方法挂 MethodImpl）是 C# 已验证的先例——C# 协变覆盖同样靠编译器合成精确返回类型的桥接方法（机制表述；逐字出处见下方 OPEN QUESTIONS）。跨语言读元数据时，桥接方法对 C# 消费者透明：C# 编译器读 VB 产物的 InterfaceImpl/MethodImpl 表，看到的是标准条目。

### 现实 vs 提案

| 提案条目 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| 半 A 裸隐式（Reject） | C# 1.0 起 public 即实现 | 兼容（目的地）／冲突（落地方式） | 目的地与 C# 一致（就是 vblang 2014 标的 "Parity with C#"）；但「重编译后接口分发漂移」的劫持风险，恰是 C# 自己拒绝扩展的同类风险——C# 9 的 C1.M/C2.M 判例与 LDM 的半 A 破坏性例子（Base.Dispose/Derived.Dispose）结构同构。C# 的应对是「宁可拉回也不放开」，与 LDM「对破坏性变更零容忍」同一原则。 |
| 半 A 声明式变体（PROPOSAL B，Consider） | C# 无「从显式迁移到隐式」的先例（C# 出生即隐式） | 需桥接 | 声明式 `Implicitly Implements` 是 VB 独有的迁移安全装置；C# 只能提供机制参考（mapping 规则、桥接方法、most-specific-implementation），迁移安全要靠 VB 自己的 `langversion` 门控。 |
| 半 B 协变返回放宽（Active） | C# 明确 "Interface implementation would not change return types" | 脱节（超 C#） | 半 B 不是追赶 C#，而是**超越 C#**——C# 因 C1.M/C2.M 破坏性变更把整个「接口实现协变」拉掉；半 B 只放宽**显式实现**（仍列出两个接口成员），恰好绕开 C# 的雷区，纯增量。产出桥接方法=标准 MethodImpl，CLR/C# 消费者无感。这是 Anthony 比 C# 更激进的正面案例，但无法从 C# 侧获得先例背书，speclet 须自证桥接安全。 |
| DIM / 接口默认实现 | C# 8 DIM | 需桥接 | VB 编译器读取 C# 8 程序集时须正确处理：带体的接口成员、接口内 `override`、most-specific-implementation、「只 public 可隐式 override」。DIM 的「添加即破坏实现者」与半 A 劫持同源，互相印证。 |
| PROPOSAL C（IDE 补全） | C# source-gen 驱动的互操作方向（索引 T6/T5） | 兼容 | 用编译期生成把 `Implements` 子句写回源码=实现关系可见且元数据干净，与 C#「编译期生成替代运行时动态」的方向一致。 |
| 未来：C# 15 extensions（索引 T8/M7） | extensions 扩展现有类型 | 观望 | 与本提案无直接冲突，但会改变「接口式能力」的供给方式，留档跟踪。 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：.vbx 默认保留「显式 `Implements` 为可见契约」，半 B 作纯增量放宽；**不要**把半 A 的静默按名匹配设为默认——它重演 C# 9 判例的破坏性变更，而 C# 自己对这类扩展的态度是「宁可不做」。
2. **source-gen 桥**：采纳 PROPOSAL C（生成器/IDE 快速修复输出规范 `Implements` 子句）作为 codegen 场景的默认出口。这直接对齐 C# 的 source-generator 互操作主线——VBScript.NET 的生成器桥产出显式 MethodImpl，元数据对 C#/CLR 完全标准。
3. **识别新元数据**：.vbx 编译器读取 C# 程序集时须正确消化——
   - C# 8 DIM：接口成员的默认实现体、接口内 `override`、most-specific-implementation 冲突；
   - C# 9 协变覆盖：C# 类的 `override` 返回派生类型时，基类/派生类里的桥接方法（MethodImpl）——VB 的 `Overrides` 匹配与接口实现判定须看**声明返回类型**而非桥的精确签名，否则会把 C# 的合法协变覆盖误判为未实现；
   - C# 13 `allows ref struct`：ref struct 接口不可参与 DIM（"A `ref struct` cannot participate in default interface members"），VB 实现此类接口时须匹配该禁令；
   - `CompilerFeatureRequired` 系列特性标志（决策文件 M4 已提示）。
4. **跨语言对称性说明**：半 B 是 VB-only——VB 类可用「一个方法 + 桥」满足 `IEnumerable(Of Student)` 与 `IEnumerable` 双槽，C# 写同形类仍需两个方法（显式非泛型 + 隐式泛型）。产出的 InterfaceImpl 表对消费者一致，但**同形源码在不同语言下实现形态不同**；VBScript.NET 文档须明示这一不对称，避免 C# 开发者读 VB 产物时困惑。
5. **对齐 2014 声明式路线**：若未来半 A 以声明式（PROPOSAL B）重提，把 C# 的 interface mapping 规则、桥接方法、most-specific-implementation 作为机制蓝本；但迁移安全（`langversion` 门控 + 劫持警告）只能靠 VB 侧自证——C# 没有可抄的先例。

### 对既有 RESOLUTION / 三态判定的影响

C# 现实**整体支持**现有 RESOLUTION，三态判定不需改动，反而各有印证：

- **半 A = Reject 获 C# 判例背书**：C# 9 曾考虑把协变扩展进接口映射，因 C1.M/C2.M 破坏性变更而拉回（"Due to this breaking change, we might consider not supporting covariant return types on implicit implementations"）。C# 的「宁可不做」与 LDM「零破坏」原则一致，RESOLUTION 第 3 条可引此为外部证据。
- **半 B = Active 被界定为「超 C#」**：C# 明确 "Interface implementation would not change return types"——半 B 不是追赶而是超越，是 Anthony 特色延伸的正面案例；它不触碰隐式映射，故不重演 C# 的破坏性变更。RESOLUTION 第 2 条「仅引用类型协变、仍显式列名」恰好落在 C# 唯一没放开、但能安全放开的那一格。
- **RESOLUTION 第 5 条的 spec 修订**：措辞蓝本可参考 C# 9 协变返回对「接口映射判据」的提议修改（identity 或 implicit reference conversion），但须**明确只放宽显式实现**，避开 C# 放弃的隐式映射区。
- **PROPOSAL C 立项**：与 C# source-gen 方向兼容，强化其作为半 A 官方替代的正当性。

### OPEN QUESTIONS / Suspect

- `Suspect`：C# 接口实现「返回类型必须精确匹配」为现状——依据是 covariant-returns.md 所引 mapping 规则与 LDM-2020-01-08 结论；但 C# 标准正文已迁至 dotnet/csharpstandard，本库 `spec\interfaces.md` 只是链接索引。如需逐字引用标准条文（对应 VB §835），须到 csharpstandard 核实。
- `Suspect`：C# 协变覆盖在 BCL 的实际采用度（如 `Stream`/`FileStream` 等是否用上）——决定 .vbx 读取 C# 程序集时处理协变桥的优先级。BCL 代码不在 csharplang 库内，无法核实。
- `OPEN QUESTIONS`：半 B 的桥接方法与 C# 协变覆盖的桥接方法在「同一类型同时被两者命中」时如何叠加（与 `proposal-override-sig` 组 16 对表）——C# 侧无先例（C# 不做接口实现协变），须 VB 原型自证。
- `OPEN QUESTIONS`：C# 13 `allows ref struct` 反约束 + DIM 禁令在 VB 消费侧的呈现深度——VB 的 ref struct 支持目前是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error，接口实现判定如何处理「实现了 ref struct 接口的 C# 类型」。
- `OPEN QUESTIONS`：半 B 若落地，VB 合成的桥接方法对 C# 的 IDE/调试器是否完全透明（C# 自己造桥，按理透明，但跨语言调试断点落在桥还是声明方法上，须原型验证）。
