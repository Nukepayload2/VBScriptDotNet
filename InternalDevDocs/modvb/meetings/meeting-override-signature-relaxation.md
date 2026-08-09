# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论"覆写签名放宽"。它与另一份正在起草的建议（`proposal-implicit-interface-implementation.md`，隐式接口实现）是同一片实现面的两面：一个放宽覆写（`Overrides`）的返回类型匹配，一个放宽显式接口实现（`Implements`）的返回类型匹配。我们把两份建议放在一起读，因为它们共享"兼容成员"的定义。

## Agenda

* [Proposal: 覆写签名放宽 / Override Signature Relaxation](#proposal-覆写签名放宽--override-signature-relaxation)

## Proposal: 覆写签名放宽 / Override Signature Relaxation

_Related: [vblang LDM 2014-02-17 #5 – Overloads Overrides](https://github.com/dotnet/vblang/blob/master/meetings/2014/LDM-2014-02-17.md)（"Overrides" 成员隐式视为 Overloads，已入主线）；[vblang LDM 2014-02-17 #37 – Implicit Interfaces](https://github.com/dotnet/vblang/blob/master/meetings/2014/LDM-2014-02-17.md)（"按签名匹配，默认值不匹配则报错"）；[vblang LDM 2017-05-19 – Default Interface Implementations](https://github.com/dotnet/vblang/blob/master/meetings/2017/vbldm-notes-2017.05.19.md)（"先按名查找接口实现，隐式实现暂定为错误"）；ModVB sibling：`proposal-implicit-interface-implementation.md`_

### 场景与缺口

We started from the classic covariant-return gap. 一个抽象成员 `Clone() As Base` 被派生类覆写时，今天 VB 要求返回类型**完全一致**——派生类想返回 `Derived` 只能把返回类型钉死在 `Base`，调用方每取一次结果都要向下转型。Anthony 原文（第 15 章 "Inheritance, Interface Implementation, and Extension"）的注释写得很准：

```vb
' Signature (and name?) relaxation for member overriding.
' (à la relaxed delegates).
Class Base
    MustOverride Function Clone() As Base
End Class
```

今天的样板长这样：

```vb
Class Derived
    Inherits Base

    ' 今天：返回类型必须与基类一致。
    Public Overrides Function Clone() As Base
        Return New Derived()
    End Function
End Class

Dim d As Derived = New Derived()
Dim c As Derived = DirectCast(d.Clone(), Derived)   ' 每次调用都要强转。
```

Fluent builder 是最疼的场景——`WithName` 连写时，一旦某一步要向下转型，整条链就断了：

```vb
Class SpecialBuilder
    Inherits Builder

    Public Overrides Function WithName(name As String) As Builder
        Return Me
    End Function
End Class

Dim x As SpecialBuilder = DirectCast(New SpecialBuilder().
    WithName("a").WithName("b"), SpecialBuilder)   ' 链式被迫中断一次。
```

Anthony 把它类比为"宽松委托"（relaxed delegates）——VB 对方法到委托的绑定本来就有放宽（`overload-resolution.md` 的 *delegate relaxation levels*）。We like the instinct：VB 历来愿意在"绑定"环节放宽，这次不过是把同一哲学挪到覆写匹配上。但如我们下面要说的，类比是**建议性**的，机制并不相同。

期望结果：覆写可以收紧返回类型（协变），消除冗余转型与样板，且不破坏通过基类引用的调用。

### 候选方案

**PROPOSAL A — 仅放宽返回类型（协变），仅引用类型。** 覆写匹配由"返回类型必须一致"放宽为"返回类型必须对基类返回类型存在**引用可转换（widening reference conversion）**"。即返回类型是基类返回类型的派生类型（类、接口、数组、委托）。这是最小可行改动，也是 C# 9.0 covariant returns 的做法。

**PROPOSAL B — A + 只读属性协变。** 放宽只读属性 getter 的返回类型；读写属性与 `Set` 访问器不动。

**PROPOSAL C — 完整"签名（以及名字？）放宽"。** 在 A 之上再放开参数方向（逆变参数：`Overrides Sub Set(x As Derived)` 覆写 `Set(x As Base)`）与"名字放宽"（以不同名字覆写基类成员，`Function CloneDerived() As Derived Overrides Base.Clone`）。对应 Anthony 问号标注的"（and name?）"。

**PROPOSAL D — 什么都不做。** 维持严格匹配，靠调用点转型与 `Shadows` 双方法模式。

### 权衡：Q&A

- **术语勘误：这不是"签名"放宽。** 按 VB 规范，方法的签名 = 类型参数个数 + 参数的类型；返回类型**不在**签名里（`spec/general-concepts.md:37,47`）。而覆写规则要求"同名 + 同签名 + 返回类型一致"（`spec/general-concepts.md:1064`："The declaration context contains a single accessible inherited method with the same signature and return type (if any) as the overriding method."）。所以建议标题里的"签名放宽"其实是**返回类型放宽**——签名从来就没有返回类型。这个名字会误导，We 建议正名。
- **A vs C：参数方向的放宽为什么必须拒绝？** 逆变参数违反 Liskov。调用方持 `Base` 引用、按 `Set(x As Base)` 契约传入 `Base` 实参，若覆写只接受 `Derived`，调用在类型系统里就该失败。这是健全性问题，不是口味问题。返回方向（协变）是唯一安全方向：持 `Base` 引用得到的运行期对象是 `Derived`，`Derived` **是** `Base`，契约未被破坏。
- **A vs C："名字放宽"为什么必须拒绝？** 覆写在 VB 里的身份是"名字 + 签名"——2014 年 LDM 已定"`Overrides` 成员隐式视为 `Overloads`"（2014-02-17 #5），且 CLR 按**名字**查找接口实现（2017-05-19 记录）。允许换名覆写会打破 `MyBase.X` 的直觉、让"覆写了什么"要靠 `Overrides Base.Clone` 这种显式目标才能读懂，与 VB"读起来像英语"的基因冲突。**结论：名字放宽是死路，`Reject`。**
- **A vs D：什么都不做的代价。** 转型样板确定但真实；`Shadows` 双方法模式在本场景其实**写不出来**——因为返回类型不在签名里，`Overrides Function Clone() As Base` 与 `Shadows Function Clone() As Derived` 在同一个类里是**重复签名**，编译报错。所以今天的唯一选择就是"覆写钉死基类返回类型 + 调用点转型"，没有第二条语法出路。这让我们觉得值得做。
- **A vs B：属性协变要不要进 v1？** 只读属性 getter 的协变在语义上与函数相同；但读写属性的 `Set` 访问器一旦参数要变，就滑向逆变参数的地盘。属性比函数多一套访问器矩阵（Read/Write/ReadOnly，Get/Set），v1 不碰。**结论：B 独立评估，列为后续。**
- **A vs 值类型协变。** 值类型没有子类型关系，`Integer` 覆写 `Object` 不存在"派生返回类型"。CLR 的 MethodImpl 表允许引用类型的协变返回，不允许值类型。值类型方向要做得靠编译器造"桥接方法"（隐藏一个返回基类类型的方法去调真身），成本与收益不匹配。**结论：只做引用类型。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

先纠一个文法硬伤，We 全场一致认为这是建议必须先修的地方：**Anthony 示例里的 `Overrides Base.Clone` 不是合法 VB。** `Overrides` 是纯修饰符，**不带目标**（`spec/type-members.md:476`："The `Overrides` modifier indicates that a method overrides a base-type overridable method that has the same signature."）。带目标的子句是 `Implements I.M`、`Handles e.Click`；`Overrides` 从来没有 `Overrides Base.Clone` 这种形式。此外示例把 `Overrides` 放在 `As Derived` 之后，而 VB 修饰符一律前置。

结论：**不引入任何新语法。** 覆写目标照旧按"名字 + 签名"推断，只把返回类型从"必须一致"放宽为"必须引用可转换"。正确写法：

```vb
Class Derived
    Inherits Base

    Public Overrides Function Clone() As Derived   ' 建议后合法；今天报 BC30110 一类错误。
        Return New Derived()
    End Function
End Class
```

这一修正好处巨大：零新语法，扩展表面为零。`Overrides Base.Clone` 的写法如果真被采纳，反而会给"名字放宽"打开后门——现在连同 `Implements` 混淆一起被拒绝。

#### 2. 角案例与边界语义

**链式协变。** 多层继承每层都可以收紧：

```vb
Class Middle
    Inherits Base
    Public Overrides Function Clone() As Middle
        Return New Middle()
    End Function
End Class

Class Leaf
    Inherits Middle
    Public Overrides Function Clone() As Leaf
        Return New Leaf()
    End Function
End Class
```

`Leaf.Clone` 的返回类型 `Leaf` 必须对**紧邻基类** `Middle.Clone` 的返回类型 `Middle` 引用可转换；`Middle` 本身对 `Base` 可转换 ⇒ 传递成立。检查按"对直接基类可转换"即可，不必对整条链回溯。

**无关引用类型 = 报错。** 覆写 `Clone() As Base` 时写 `As String`——`String` 与 `Base` 无子类关系，拒绝。判定用既有的 **widening reference conversion**（identity、类/接口/数组/委托继承关系），`spec/conversions.md` 的 widening 体系直接复用，不发明新转换类别。

**`MyBase.Clone()`。** 覆写体内 `MyBase.Clone()` 返回 `Base`——自然，绑定到被覆写的槽，返回其元数据返回类型。

**`Overrides NotOverridable` 与 `Overrides MustOverride`。** 协变覆写可以 `NotOverridable`（封死下游），也可以在 `MustInherit` 里 `Overrides MustOverride` 且带协变返回，把抽象约束继续传递——两类都是既有规则在协变返回下的平凡延伸。

**协变覆写同时满足接口成员。** 这是与 sibling 建议咬合最紧的点：

```vb
Class Base
    Implements ICloneable

    ' 需要 sibling 建议：显式接口实现的返回类型由"必须一致"放宽为"引用可转换"。
    Public Overridable Function Clone() As Base Implements ICloneable.Clone
        Return Me
    End Function
End Class

Class Derived
    Inherits Base

    ' 本建议：覆写返回类型协变。
    Public Overrides Function Clone() As Derived
        Return New Derived()
    End Function
End Class
```

`ICloneable.Clone` 返回 `Object`；今天 `Base.Clone As Base` 都不能 `Implements` 它（`spec/general-concepts.md:835`："the return type must match"）。sibling 建议放宽接口实现侧之后，`Derived.Clone As Derived` 顺着 `Derived → Base → Object` 的引用转换同时满足覆写与接口契约。**两份建议必须共享同一份"兼容成员"定义**，否则一个方法能否同时满足 `Overrides` 与 `Implements` 会在两套规则间摇摆。

**值类型边界（`Nothing`）。** `Derived` 是类，`Nothing` 对任何引用类型可转换，`Return Nothing` 在协变覆写里照旧合法。值类型协变（如 `Function Clone() As Integer` 覆写 `As Object`）已在候选方案里拒绝。

**泛型。** `Class Base(Of T)` 的 `Overridable Function Clone() As Base(Of T)` 由 `Derived(Of T) : Inherits Base(Of T)` 覆写为 `Clone() As Derived(Of T)`——类继承不是 variance，`Derived(Of T)` 是 `Base(Of T)` 的子类型，引用可转换成立。但 `Base(Of Derived)` 覆写 `Base(Of Base)` **不成立**（类类型参数不协变）。判定交给"引用可转换"一个函数，两类情况自动分对。

#### 3. 作用域与绑定

覆写符号的 `ReturnType` 是 `Derived`。调用点 `d.Clone()` 在语义模型里的 `GetTypeInfo` 返回 `Derived`；通过 `Base` 引用调用返回 `Base`。返回类型不参与签名 ⇒ 不引入新的重载歧义；覆写解析仍按"最派生覆盖者胜出"。`MyBase.Clone()` 绑定到基类槽，返回 `Base`。

#### 4. 与既有特性的交互

- **`Overrides` 隐式 `Overloads`（2014 决定）。** 保持成立。协变方法与被覆写方法"同名同签名"，本来就是同一签名在层次里的一个槽，不产生重载。
- **`Shadows`。** 今天 `Shadows Function Clone() As Derived` 与 `Overrides Function Clone() As Base` 同签名撞车，**写不合法**。建议落地后，用户会自然选 `Overrides` + 协变返回，`Shadows` 维持其"完全独立新方法"的语义，不与协变纠缠。
- **`Async` / `Iterator`。** 按**声明返回类型**做引用可转换检查。`Iterator Function GetEnumerator() As IEnumerator(Of Base)` 可由 `As IEnumerator(Of Derived)` 覆写——`IEnumerator(Of T)` 是协变接口，成立。`Async Function ... As Task(Of Base)` 不能被 `Task(Of Derived)` 覆写——`Task(Of T)` 不变。We think 这不算缺陷：协变检查发生在"声明返回类型"这一层，异步的 `Task(Of T)` 本就不该被当作出行载荷。需要写进文档，防止用户期待 `Async` 协变。
- **表达式树。** 表达式树按声明类型生成，协变返回在编译期已定类型，无运行期表示问题。
- **Late binding / Option Strict Off。** 协变只作用于**声明的**引用类型。宽模式下 `Object` 上的晚期绑定调用不受影响。见第 6 条。
- **ModVB sibling：`Replacement modifiers`（`Replaceable`/`Replaces`）。** 那是"源码生成器协作"用的平行体系，与继承覆写不同域。We think 不必让两套体系共享"可替换签名"规则——`Replaces` 是工具契约，`Overrides` 是继承契约，放宽策略不必传染。**OPEN**：等 `Replaceable` 落地时再复核。

#### 5. Breaking change 与兼容性

这是本建议最干净的地方：**纯启用型改动**。今天返回类型不一致的覆写是**编译错误**——不存在"旧代码能编译、重编译后行为变"的情况。被放宽的全部是新近才能写的代码。C# 9.0 covariant returns 官方宣称为非破坏性（`Probably`——我们无法在本仓库核对 C# 侧文案）。库作者把 v2 的 `Clone() As Base` 改成 `Clone() As Derived`：源码兼容（调用方重编译后拿到更具体的类型，赋值给 `Base` 依旧成立）、二进制兼容（覆写槽与接口映射不变）。

唯一要留意的暗角：一个类既有 `Implements` 协变（sibling）又有 `Overrides` 协变时，方法表里的接口实现归属会变。这也是两份建议必须同步设计的原因——**单独落任何一个都会把另一侧推向不一致。**

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径行为必须一致。宽松路径下"返回类型必须一致"的既有错误被放宽为"引用可转换"；但**只接受 widening reference**——`Option Strict Off` 的隐式数值/自定义转换**不得**参与覆写匹配（`Integer` 覆写 `Long` 这种绝不能因为宽松模式而放行）。规则与 Option Strict 无关，两条路径同一套判定。

#### 7. IDE / IntelliSense

补全：在 `d.Clone().` 上补全 `Derived` 成员，在 `Base` 引用上补全 `Base` 成员。覆写向导（VS 生成 override stub）默认生成基类返回类型，并给出"更具体返回类型"的下拉。`Go To Definition` 落在协变方法上时显示 `As Derived`。语义模型验证必须在原型里做，不进原型等于没设计。

#### 8. 数据 / 普遍性

`Clone` / `Copy` / builder / fluent API 是真实代码里高频的形状，OOP 库作者会立即受益。但对"数十万安静客户"的业务代码，刚性需求频次**没有量化数据**。We `Suspect` 这是真实的 DX 增益，但不是头条特性；它的定位是"语言品质打磨项"，不该插队到可空性、模式匹配前面。

#### 9. 更简替代

- **调用点转型**：`DirectCast(x.Clone(), Derived)`——现状，样板多但确定。今天唯一的合法出路。
- **`Shadows` 双方法**：在同一类里与 `Overrides` 同签名撞车，**不合法**（返回类型不入签名所致）。换不同名方法（`CloneDerived`）可行但改 API 面。
- **泛型**：`Function Clone(Of T As Base)() As T` 不能用于覆写非泛型基方法——基方法签名固定，泛型方法对不上。对解决本场景**无效**。
- **Analyzer / 重构**：能提示"这里在转型"，不能移除转型。只能当脚手架。

结论：没有比"覆写返回类型放宽"更简的替代——问题本身出在覆写匹配规则上，只能改规则。

#### 10. 复杂度 / 成本 / 优先级

编译器改动集中在 VB binder 的覆写匹配（把"返回类型一致"换成"引用可转换"）与 override key 的维护。因为 VBScript.NET 基于修改版 Roslyn，而 Roslyn **已经为 C# 9 实现了 covariant returns**（`Probably`——共享 PE writer、覆写验证、方法表管线），VB 侧的增量主要在前端判定与错误文案，成本中低。优先级：排在可空性、模式匹配之后；与 sibling 建议绑定推进。

#### 11. 运行时 / CLR 硬约束

CLI 的 MethodImpl（覆写表）允许实现方法的返回类型为被覆写方法的返回类型的**引用类型子类型**——这是 C# 9 依赖的机制，PEVerify 无碍。We 无法在本仓库核对 CLI 规范（Partition II）原文，标注 `Suspect`，实现前由运行时专家确认。值类型协变在 CLR 层不允许，需桥接方法，已拒绝。不触达存储规则、无表达式树问题。

#### 12. 值不值得做

价值（消除真实样板、对齐 C# 9、零新语法、极 VB）中高；成本（共享管线上的前端增量）低；风险（纯启用型、非破坏）低。**值得做——但范围必须收敛。** 若把它做成"签名 + 名字 + 参数全放宽"，我们宁可拒绝。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心。`Clone()` / builder / `GetEnumerator()` 是真实高频样板。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 9 已有 covariant returns（`Probably`）；VB 跟随是最省力的默认。这里没有偏离的充分理由。
- **不引入"第二种做事方式"（原则 #3）**：**零新语法**（修正 Anthony 的 `Overrides Base.Clone` 后），扩展表面为零——这是本建议最亮的地方。
- **避免隐蔽的控制流/语义变化（原则 #7）**：纯启用型，旧代码行为零变化。无 `Return?` 式风险。
- **读起来像英语、对新手友好（原则 #5）**：`Overrides Function Clone() As Derived` 一望即懂，比 `Overrides Base.Clone` 那种带目标的写法更 VB。
- **与主线关系（对照表 2.3）**：vblang 主线**没有任何** covariant-return 的提议或会议讨论（我们在 `..\..\vblang\meetings/` 与 `proposals/` 全量检索过，"covariant" 只出现在数组协变与泛型 variance 的规范条文里）。它是 **Anthony 独立延伸**，但方向与主线"默认跟随 C#"**一致**——属于"主线没提、Anthony 先做、主线大概率会收"的品类。与 sibling `proposal-implicit-interface-implementation.md` 是同一实现面的两面，必须共享"兼容成员"定义。
- **宽松委托的血缘**：VB 的 delegate relaxation（`overload-resolution.md:15-27`）是**绑定期**放宽，用于 lambda/`AddressOf` 到委托类型的匹配；本建议放宽的是**覆写匹配**。机制不同，但"VB 愿意在匹配环节放宽"的哲学一脉相承。血缘成立，类比要打折扣。

### RESOLUTION:

1. **原则上接受"协变返回类型"**作为覆写匹配的放宽：覆写的返回类型须对基类返回类型存在 **widening reference conversion**。正名：这是**返回类型放宽**，不是"签名放宽"（签名不含返回类型，`spec/general-concepts.md:37,47`）。
2. **零新语法**：拒绝 `Overrides Base.Clone` 目标写法（`Overrides` 是纯修饰符，不带目标；该写法系与 `Implements` 子句混淆的误植）。覆写目标照旧按"名字 + 签名"推断。示例改写为 `Public Overrides Function Clone() As Derived`。
3. **v1 范围 = 函数（Function）、引用类型**：不含 `Sub`、不含 `ByRef`、不含属性（PROPOSAL B 独立评估）、不含值类型协变（CLR 不允许）。
4. **拒绝逆变参数（PROPOSAL C 的参数部分）**：Liskov 违规，健全性问题。仅返回方向可放宽。
5. **拒绝"名字放宽"（PROPOSAL C 的名字部分）**："名字即签名身份"；CLR 按名查找接口实现（2017-05-19）；`Overrides` 隐式 `Overloads`（2014-02-17 #5）的既有身份模型不容打破。
6. **与 sibling `proposal-implicit-interface-implementation.md` 共用一份"兼容成员"定义**：`spec/general-concepts.md:1064`（覆写）与 `:835`（接口实现）的返回类型条款同步放宽到同一判定；单独落任何一个都会造成两套规则不一致。
7. **Option Strict 无关**：两路径同一套判定；只接受 widening reference，不接受 narrowing、数值或用户定义转换。
8. **C# 9.0 已有 covariant returns（`Probably`）**：Roslyn 共享实现管线，VBScript.NET 移植成本中低；这是跟随 C# 的默认项（原则 #4）。

### Implication:

- 起草 speclet：修改 `spec/general-concepts.md:1064` 与 `:835` 的措辞；给出"widening reference conversion"判定与错误文案；明确 `Async`/`Iterator` 按声明返回类型检查、`Task(Of T)` 不协变。
- 写最小原型：在修改版 Roslyn 的 VB binder 里放宽覆写匹配；验证语义模型 `GetTypeInfo`、`MyBase` 绑定、IDE 补全与覆写向导。
- 与 sibling 建议团队对表：确定"兼容成员"单一定义；安排同一份原型同时验证 `Overrides` 与 `Implements` 两条路径。
- 让运行时专家核对 CLI 规范中 MethodImpl 协变返回的条文（`Suspect`）。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：只读属性协变（PROPOSAL B）是否值得作为独立后续——收益一致，但访问器矩阵带来额外边界。
- `OPEN QUESTIONS`：链式协变中 `Overrides MustOverride` 在 `MustInherit` 中间层的组合是否需要更多示例与测试（语法上平凡，语义模型上要验证）。
- `OPEN QUESTIONS`：与 `Replacement modifiers`（`Replaceable`/`Replaces`）是否共享"可替换签名"规则——`Probably` 不共享，待其落地复核。
- `Suspect`：CLI 规范关于 MethodImpl 协变返回的精确条文，无法在本仓库核实；C# 9.0 的上线时间与官方非破坏性表述同理标注 `Probably`。
- `TODO`：量化 `Clone`/builder/fluent 在真实代码库中的占比，为普遍性补证据。
- `Follow-up`：把"签名 ≠ 返回类型"的术语勘误写回 `proposal-override-signature-relaxation.md` 与 `proposal-implicit-interface-implementation.md`，两份建议统一术语。

### 状态

- **LDM 状态：Consider（原则上采纳，范围收缩后）**。
- **三态判定：Consider** —— 价值真实、零新语法、纯启用型非破坏；但它不是头条特性，且必须与 sibling 建议绑定推进。VBScript.NET 若优先 OOP 库作者体验可升 **Active**；否则挂起等待 sibling 建议先落地。名字放宽 / 逆变参数：**Reject**。

---

## 附录：特性评价

# 建议评价报告：proposal-override-signature-relaxation.md

## 评价对象

- 建议：proposal-override-signature-relaxation.md — 覆写返回类型协变（含"名字放宽"未定项）
- 来源：Anthony 原文第 15 章 "Inheritance, Interface Implementation, and Extension"（`..\AnthonyDesign_wordpress.txt` L2430–2471；`Clone`/`Derived`/`ICloneable`/`GetEnumerator` 全部出自该章）
- 配方目标：覆写可返回派生类型，消除转型样板，同时保持基类契约的兼容

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（消除转型样板）、场景真实（Clone/builder）；但核心示例 `Overrides Base.Clone` **不是合法 VB**，示例不能演示改进 | 已检查 | 无原型封顶 3；示例语法硬伤使"可操作"落空；未覆盖 async/Task、泛型、链式协变等边界 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。零新语法（修正后）极 VB、消除样板（#9）、跟随 C#（#4）基因在；但原文携带 `Overrides Base.Clone` 外来语法、"宽松委托"类比与机制实际不符 | 已检查 | 借鉴 C# 9 covariant returns 未做 VB 化论证；"名字放宽"若被采纳即违反"读起来像英语"（#5），幸运的是建议自己标了问号 |
| 品质 | 2/5 | 锚点 2："自相矛盾；示例与正文冲突"。六章节齐全，未决问题具体诚实（1–3 健康区间）；但标题"签名放宽"与规范"签名不含返回类型"自相矛盾；示例无法编译且与文法冲突；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；Drawbacks/Alternatives 薄 | 已检查 | 核心示例语法硬伤（`Overrides` 不带目标、修饰符后置）；无 Compatibility 章节；无 Option Strict 分叉；无 CLR/PEVerify 论证 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（迭代速度：代码生成更简）、水（核心竞争力的类型建模）正向；风（与 sibling 建议的重叠）需协调但文档未权衡 | 已检查（预测待定） | 暗风险小但未显式识别（实现面的双建议一致性、async/Task 用户期待落差）；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。Anthony 第 15 章章节可定位；但直接模型是 **C# 9 covariant returns**，建议只引了"宽松委托"（VB 自产机制），且该类比有偏差（绑定期 vs 覆写期） | 已检查 | 未声明借鉴 C# 9；"宽松委托"类比未标注其机制差异；sibling 建议共享材料未交叉引用 |

## 设计原则对照

- **与 VB 基因：一致（范围收缩后）**——零新语法（原则 #3）、消除样板（#9）、跟随 C#（#4）、无隐蔽语义变化（#7）；"名字放宽"若采纳则偏离（#5 可读性、#2 VB-like），会议已拒绝。
- **与主线关系：Anthony 独立延伸**——vblang 主线无 covariant-return 提议/讨论（meetings 与 proposals 全量检索确认）；方向与主线"默认跟随 C#"一致，属"主线没提、Anthony 先做"品类。与 sibling `proposal-implicit-interface-implementation.md` 为同一实现面两面。
- **破坏性变更：无**——纯启用型（被放宽形式今天全是编译错误）；`Probably` 对齐 C# 9 的非破坏性主张。库作者改返回类型为协变是源码/二进制双兼容。

## 总评

- **达成程度：部分达成**——概念与价值成立、范围可缩、非破坏；但示例语法硬伤、术语自相矛盾、兼容性论证缺失。
- **LDM 三态建议：Consider**——原则上接受"函数 + 引用类型 + 零新语法"的协变返回；名字放宽/逆变参数 **Reject**；属性协变与 `Replaceable` 交互列为后续。VBScript.NET 优先 OOP 库作者体验可升 Active，否则挂起等 sibling 建议先落地。
- **主要问题**：① 示例 `Overrides Base.Clone` 非法语法，须改写为 `Public Overrides Function Clone() As Derived`；② "签名放宽"名不副实（返回类型不在签名内），须正名"返回类型放宽"；③ 与 sibling 建议共享"兼容成员"定义，单独落地会造成两套规则不一致；④ 未分析 async/Task、泛型、链式协变等边界。

## 返工建议

- **补充章节**：重写 Detailed design 示例为合法 VB 语法并删除 `Overrides Base.Clone` 目标写法；补 Compatibility/breaking-change 章节（纯启用型论证）；补 Option Strict 分叉（宽松模式禁止数值/自定义转换参与匹配）；补 CLR/PEVerify 与 C# 9 实现管线说明；补与 `proposal-implicit-interface-implementation.md` 的协调章节。
- **补充证据**：修改版 Roslyn VB binder 的最小原型（覆写匹配放宽）；语义模型 `GetTypeInfo`、`MyBase` 绑定、IDE 补全与覆写向导验证；链式协变 + `Overrides MustOverride` 的测试；真实代码库中 Clone/builder 样板占比数据。
- **未决问题处理**：名字放宽明确 Reject；逆变参数明确 Reject；属性协变独立评估；`Task(Of T)` 不协变写进文档；CLI 规范条文交由运行时专家确认（Suspect 状态）；"签名 vs 返回类型"术语勘误写回两份建议。
- **设计探索**：与 sibling 建议共用"兼容成员"判定的接口契约；`ICloneable` 全链示例（`Derived → Base → Object`）作为两份建议联合验收样例。

---

## 附录：C# 生态与互操作考量

### 相关 C# 现实方向

本提案主题（覆写返回类型放宽）在 C#/CLR/.NET 生态的对应走向**已定型**——不是 C# 的实验方向，而是 C# 9.0 已发布的正式特性，且之后仓库内无针对该特性的进一步设计讨论（2020-01-08 之后仅 2020-03-23/03-30 在 records 语境下顺带提及，无实质设计推进）。对 VBScript.NET 的含义是"跟上一个已落地的 C# 特性"，而不是"追赶 C# 的前沿"。

- **C# 9.0 covariant returns 已发布，范围是引用类型**。`Language-Version-History.md` 的 C# 9.0 条目逐字列出：「Covariant return types: a method override on reference types can declare a more derived return type.」（→ `Language-Version-History.md`，C# 9.0 节）。这与本提案 PROPOSAL A 的"仅引用类型"范围一致。
- **C# 的判定就是"identity 或隐式引用转换"**。`proposals\csharp-9.0\covariant-returns.md` 把既有约束 "The override method and the overridden base method have the same return type." 改为 "The override method must have a return type that is convertible by an identity conversion or (if the method has a value return - not a ref return) implicit reference conversion to the return type of the overridden base method."——C# 的 "identity conversion or implicit reference conversion" ≈ VB 的 "widening reference conversion"，判定的是同一件事。
- **C# 把覆写与接口实现分开处理**。同提案 Summary 提到只读属性协变；`meetings\2020\LDM-2020-01-08.md` 明确："Interface implementation would not change return types, while overriding would."——C# 对"接口实现"与"覆写"是两条规则，这与 VBScript.NET 的 `Implements`（实现）与 `Overrides`（覆写）需要分清是同一结构。
- **C# 没有放宽参数方向（逆变参数）**。`meetings\2019\LDM-2019-08-28.md` 记录："We previously pushed out overrides with variance (covariant returns, contravariant parameters) due to requiring quadratic stubs to be emitted for the runtime." 结论是 runtime 可支持 "compatible" signatures，排期 C# 9.0；但仓库内后续 LDM（2019-08-28 之后）无参数逆变落地的证据——C# 9 实际只落了返回协变。
- **CLR 的 override 签名匹配约束正是本提案 `Suspect` 的那条**。`LDM-2019-08-28.md` 明说现况是 "exact matches, which is what is required right now"，靠 .NET 5 运行时改动才允许 "compatible" signatures。C# 9 在 .NET 5 上线本身就是"CLR 支持引用类型协变返回"的运行证据——`Suspect` 应降级为"运行时已支持（.NET 5 起），ECMA-335 Partition II 精确条文仍待核实"。
- **接口实现侧的协变在 C# 被标记为 breaking change**。`proposals\csharp-9.0\covariant-returns.md`（Implicit Interface Implementations 节）："This is technically a breaking change, as the program below prints "C1.M" today, but would print "C2.M" under the proposed revision."，并总结 "Due to this breaking change, we might consider not supporting covariant return types on implicit implementations."——这直接命中 sibling 提案 `proposal-implicit-interface-implementation.md` 的危险区。

### 现实 vs 提案

| 维度 | 判定 | 理由 |
|---|---|---|
| 返回类型协变（PROPOSAL A） | **兼容** | 与 C# 9 判定同源（identity/implicit reference ≈ widening reference）；范围同为引用类型 |
| 只读属性协变（PROPOSAL B） | **兼容，且 C# 已发布** | C# 9 Summary 含只读属性协变；本提案把它列为"后续"意味着主动落后一个 C# 已发布特性 |
| 逆变参数（PROPOSAL C 参数侧） | **兼容** | C# 同样未落地（2019 推出后无后续证据）；双方一致拒绝 |
| 名字放宽（PROPOSAL C 名字侧） | **脱节** | C# 按"名字+签名"匹配覆写，无按名放宽方向；本提案 Reject 与 C# 现实一致 |
| CLR override 签名匹配 | **需桥接** | C# 靠 .NET 5 运行时改动支持"兼容签名"（2019-08-28）；本提案对 CLI Partition II 条文标 `Suspect`。C# 9 已上线是强证据，精确条文仍待核实 |
| 隐式接口实现协变（sibling） | **冲突/需桥接** | C# 文档明确标为 breaking change 并"考虑不支持"；VBScript.NET 若放行 `Implements` 返回类型放宽，必须正面回应 C# 记录的接口映射破坏 |
| 可访问性约束 | **需桥接** | C# 要求 "The override method's return type must be at least as accessible as the override method"（covariant-returns.md）；本提案未覆盖，跨语言消费 C# 库时可能产生非法元数据 |

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：协变返回是纯编译期特性，零运行期开销、无反射依赖，与 AOT/trimming 完全兼容（索引 T5 方向）——对 VBScript.NET 的"默认安全"路线无威胁，也不会触碰 Any/晚期绑定动态面。
- **source-gen 桥**：协变返回自身不需要运行期桥（无运行期组件）。但 sibling `Implements` 侧应参考 C# 的做法——C# 宁可"考虑不支持隐式实现协变"也不引入接口映射破坏；VBScript.NET 若坚持放行，建议用 source generator / analyzer 在映射歧义处给出编译期诊断，把破坏面控制在编译期而非运行期。
- **识别新元数据**：消费 C# 9 编译的程序集时，覆写槽的返回类型是"更派生返回类型"。VB binder 必须按 covariant-returns.md 的 Name Lookup 规则（调用方静态拿到**最派生覆写**的返回类型）解析 `GetTypeInfo`，否则跨语言调用拿到的是基类返回类型，协变语义失效。同时执行 C# 的可访问性约束，避免返回类型比覆写方法更不可访问时产出非法元数据。
- **覆写 vs 实现分界**：`Overrides` 与 `Implements` 必须按 C# LDM-2020-01-08 的界线分开（实现不改变返回类型、覆写才允许），否则同一类在不同语言下接口映射不同，破坏跨语言一致性。

### 对既有 RESOLUTION/三态判定的影响

- **RESOLUTION #8 的 `Probably` 可升级**：C# 9.0 covariant returns 的存在已核实（`Language-Version-History.md` + `proposals\csharp-9.0\covariant-returns.md`），不再是 `Probably`。
- **RESOLUTION #3（v1 不含属性）值得复议**：C# 9 的 proposal Summary 已含只读属性协变（PROPOSAL B 范围）。若 VBScript.NET 默认"跟随 C#"（原则 #4），只读属性协变没有天然理由排除在 v1 外；原会议"访问器矩阵带来额外边界"的理由仍成立，但需显式权衡"主动落后 C# 一个已发布特性"的代价。
- **RESOLUTION #6（与 sibling 共用"兼容成员"定义）必须吸收 C# 的 breaking-change 警告**：本提案正文"纯启用型、非破坏"只对 `Overrides` 侧成立；sibling `Implements` 侧正是 C# 文档标注的破坏面。三态判定上：`Overrides` 侧维持 Consider（非破坏）；sibling 侧应因 C# 的警告而降档或附加条件，不宜与 `Overrides` 同速推进。
- **`Suspect`（CLI Partition II）降级**：运行时支持已被 C# 9 / .NET 5 证明，剩余只是 ECMA-335 精确条文待运行时专家核实。

### 引用纪律

- 本附录逐字引用的 C# 原文均已核实，来源如下：
  - "a method override on reference types can declare a more derived return type." → `Language-Version-History.md`（C# 9.0 节）
  - "permit the override of a method to declare a more derived return type than the method it overrides, and similarly to permit the override of a read-only property to declare a more derived type." → `proposals\csharp-9.0\covariant-returns.md`（Summary）
  - "The override method must have a return type that is convertible by an identity conversion or (if the method has a value return - not a ref return) implicit reference conversion to the return type of the overridden base method." → `proposals\csharp-9.0\covariant-returns.md`（Class Method Override）
  - "The override method's return type must be at least as accessible as the override method" → `proposals\csharp-9.0\covariant-returns.md`（Class Method Override）
  - "This is technically a breaking change, as the program below prints "C1.M" today, but would print "C2.M" under the proposed revision." 与 "Due to this breaking change, we might consider not supporting covariant return types on implicit implementations." → `proposals\csharp-9.0\covariant-returns.md`（Implicit Interface Implementations）
  - "We previously pushed out overrides with variance (covariant returns, contravariant parameters) due to requiring quadratic stubs to be emitted for the runtime." → `meetings\2019\LDM-2019-08-28.md`（Variant method overrides）
  - "Interface implementation would not change return types, while overriding would." → `meetings\2020\LDM-2020-01-08.md`（Covariant returns）
- **OPEN QUESTIONS**：C# 9 是否实际发布了**只读属性**协变——`Language-Version-History.md` 的 C# 9.0 条目只字面提到 method，proposal 文本含 read-only property，两者有出入；property 侧发布状态按 proposal 文本记，待核实。C# 参数逆变自 2019 年后是否有任何后续进展，仓库内无证据（按"无证据=未落地"处理，标 `Suspect`）。索引第四节 6 段已核实原文均与 override 无关，本附录未引用。
