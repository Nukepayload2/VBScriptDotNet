# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的主题来自 Anthony 原文第 13 章——智能属性（Smart Attributes）。这不是我们第一次看"用特性改写成访问器"：2018 年 3 月的主线会议上，Anthony 在 Design Safari（#282）里演示了"用特性影响行为 + 编译期改写"，当时"整体印象是正面的"。但演示获好评不等于机制获准——主线从未批准任何这样的机制。今天我们把它从演示推进到机制来审。我们把 replacement-modifiers（13.2）与 `?=` 两份建议也带上桌，因为三者共享同一片"声明式编程与代码生成"的地面。

## Agenda

* [Proposal: 智能属性（Smart Attributes / `PropertyHandlerAttribute`）](#proposal-智能属性)

## Proposal: 智能属性

_Related: [vblang #282 – Design Safari：INotifyPropertyChanged 场景](https://github.com/dotnet/vblang/issues/282)；[vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；[#107 – Replaceable Members](https://github.com/dotnet/vblang/issues/107)、[#198 – Bindable Classes and Properties](https://github.com/dotnet/vblang/issues/198)、[#194 – WithPropertyEvents Modifier](https://github.com/dotnet/vblang/issues/194)；[#196 – Implicit Property Backing Fields](https://github.com/dotnet/vblang/issues/196)、[#197 – Inferred Set Parameter Type](https://github.com/dotnet/vblang/issues/197)；ModVB：`proposal-replacement-modifiers.md`、`proposal-null-equality-operators.md`_

### 场景与缺口

The scenario is real, and it is the same one the main line has been circling since 2017. 在自动属性上叠加校验、通知、撤销、格式化等横切行为，今天要么手写冗余访问器，要么引入源码生成器。原文第 13 章的引言说得直白——"These strategies make different trade-offs in end-developer experience, tool-author experience, performance, complexity, etc. The same scenario might be addressed using more than one approach or a combination." 本建议选择最直接的那条路：让特性本身成为行为。

```vb
' 今天：给 Title 加裁剪、通知、撤销、长度校验，得手写四个访问器，或上生成器。
<Trim, Notify, Undoable, MaxLength(25)>
Property Title As String = "New Listing"
```

We see a real declaration-site readability win here. 把 `Trim`、`Notify`、`Undoable`、`MaxLength(25)` 排成一行读出来，几乎就是英文句子——这正是原则 #5（读起来像英语）想要的形状，也是 2018 年 3 月那次演示给全场留下正面印象的原因：_"We spent the rest of the meeting with Anthony's demo for affecting behavior using attributes and compile time rewriting. Overall impression was positive."_（#282，2018.03.21）

但我们必须同时把主线的记忆摆上桌。2017 年 11 月，主线在 #219（"Implementing INotifyPropertyChanged is Tedious"）上把三份建议放成一条从最一般到最具体的光谱：最一般的是 #107 `Replaceable Members`，最具体的是 #198 `Bindable Classes and Properties`，#194 `WithPropertyEvents` 落在中间，当时判定 #194 是最佳平衡——_"We're fairly confident that if we had `Replaces` w/ source generators we wouldn't do `Bindable` or `WithPropertyEvents` as `INotifyPropertyChanged` is basically the poster child for the source generator feature."_ 两个月后（2017.12.06）：#107 推迟到下个版本、#198 拒绝、#194 批准做原型与 speclet，而源码生成器被判定为无法赶工——_"We absolutely cannot rush full meta-programming solution"_（转述其意：绝不能在元编程方案上赶工）。也就是说：**INotifyPropertyChanged 这个场景，主线认定的答案从来不是"特性注入"，而是生成器与 #194。** 我们的沙盒可以偏离主线，但不能假装没有这段历史。

### 候选方案

**PROPOSAL A — 开放可扩展机制（按建议原文）。** 任何用户都能写一个继承 `PropertyHandlerAttribute` 的特性，用 `OnPropertySet` / `OnPropertyGet` 静态方法声明注入逻辑，编译器按名字约定把方法注入到自动属性/事件的访问器调用点，按声明顺序组合，构造参数复制到调用点，返回 `Boolean` 可短路。这是"系统化"后的完整形态。

**PROPOSAL B — 并入源码生成器 / `Replaceable`–`Replaces`（13.2）。** 用生成器重写访问器，生成代码可见、可调试、可被人的微调接管。能力更强，工具与体验更复杂；这正是主线 #219 决策线偏好的方向。

**PROPOSAL C — 收敛为内置处理器集。** 不开放"任意自定义处理器 + 命名约定签名匹配"，改为编译器内置一小撮高频行为（`Notify`、`ThrowOnNull`，也许 `MaxLength`），每个行为有确定的下沉（lowering）与确定的管线阶段。自定义扩展走显式 `Implements` 接口式声明（`BeforeWrite` / `AfterWrite` 两个相位），不靠静态方法名猜。这是把"机制"收成"特性"的 VB 路线。

**PROPOSAL D — 什么都不做。** 手写访问器 + 现有 MVVM 框架 + analyzer 提示，维持现状。主线的决策模式里确实有这个选项——_"We're proud not to do anything."_（2014.02.17 的 RESOLUTION 原话，此处转引其精神）；这个场景已有 #194 与生成器两条路，第三条路必须有压倒性理由。

### 权衡：Q&A

- **A vs B：生成器是可见的，A 是隐藏的。** 生成器方案把生成的访问器写进磁盘或至少写进 IDE 的"生成的代码"视图，开发者断点、单步、审计都能看到真实行为；A 的注入只存在于 IL，源码里 `<Trim, Notify, ...>` 一行，背后的行为是黑盒。调试"为什么 MaxLength 把 Set 拦了"时，A 要跳去查特性类；B 直接读生成代码。这是本场分歧最大的一点：A 的拥护者说"声明式就该隐藏实现"；反对者说 VB 的原则 #7 恰恰是"避免隐蔽的控制流"。`Not all of us are happy` 让编译器代表用户做这件事。
- **A vs C：机制还是特性。** A 把"在属性上组合任意行为"的权力全部交给用户，代价是编译器必须为一个开放约定（名字匹配、参数形状、顺序、短路）负责，角案例无穷多（见下方追问）。C 只为一小撮已知行为负责，每个行为单独设计、单独优化、单独写文档。主线的习惯是把特性收窄（"Treat it as an optimization, not a feature"）。C 更 VB；A 更像 C# 里 source generator 的语言化——能力全给用户，规范压力全给编译器。
- **顺序语义是 A 的头号暗雷。** 建议说"按声明的顺序注入"，但**没定义后备字段写回发生在管线中的哪个点**。我们推演了"先跑完所有处理器、末尾一次写回"的管线：

  ```vb
  ' 若按"先跑完所有处理器、末尾一次写回"（Suspect——原文未定）：
  ' <Trim, Notify, Undoable, MaxLength(25)> Property Title As String
  Public Property Title As String
      Get
          Return m_Title
      End Get
      Set(ByRef value As String)
          Dim backingField As String = m_Title
          TrimAttribute.OnPropertySet(Me, "Title", backingField, value)        ' 1. 裁剪
          NotifyAttribute.OnPropertySet(Me, "Title", backingField, value)      ' 2. 通知 —— 字段还没写回！
          UndoableAttribute.OnPropertySet(Me, "Title", backingField, value)    ' 3. 撤销登记
          MaxLengthAttribute.OnPropertySet(Me, "Title", backingField, value, 25) ' 4. 长度校验（可能抛）
          m_Title = value
      End Set
  End Property
  ```

  问题立现：`Notify` 排在 2 号位，在 `m_Title = value` 之前就抛出了 PropertyChanged——INPC 语义下通知必须发生在字段变更之后；而 `MaxLength` 若在 4 号位抛出，UI 已被通知了一个非法值。建议原文自己的示例 `<Trim, Notify, Undoable, MaxLength(25)>` 在这个管线假设下是**自相矛盾**的。要救活它，要么要求用户把 `Notify` 永远写在最后（把易错的责任推给用户），要么给处理器分 `BeforeWrite` / `AfterWrite` 相位（C 方案的方向），要么写回点定义得极其小心。`Undoable` 必须在写回前读旧值、`Notify` 必须在写回后触发——同一个管线里两种相位并存，这不是"按声明顺序"能表达的。
- **事件注入是 A 最危险的面。** `<IgnoreExceptions> Event Closing As EventHandler` 与 `<RaiseAsync> Event Saved As EventHandler` 会改写 `RaiseEvent` 的异常语义：前者吞掉订阅者抛出的异常，后者把同步抛出变成 fire-and-forget。这就是 #167 `Return?` 被主线拒绝的同款理由——_"We think this is a bad idea. Control flow would be altered by a very subtle character."_ 一个 `<RaiseAsync>` 让调用者的异常时机从"同步抛"变成"永远不抛"，比 `?` 那个字符隐蔽得多。
- **反射面：这还算"特性"吗？** 属性（attribute）在 .NET 里首先是元数据，`GetCustomAttributes` 能读到它。若编译器把构造参数复制到调用点而特性实例"不必保存任何东西"，那它是不是还要照常落元数据？要——否则反射读不到；但若同时落，同一份配置就有两个表示（元数据 + 注入的调用点），反射读到的是构造参数，运行时用的是注入的调用点，二者靠编译器保持一致。这意味着 `PropertyHandlerAttribute` 不是普通特性，而是一种**编译器伪特性**（compiler-recognized pseudo-attribute）。伪特性在 VB 里有先例（`<Out>` 那个案例，2014 年的讨论里就提到"编译器一般不在源码里破解特性"），但把伪特性做成可扩展的基类，是新的。命名上 We 也犹豫：叫"attribute"会让新手以为可以 `GetCustomAttributes` 拿行为配置，而实际上行为在 IL 里。
- **B 的能力覆盖 A。** 生成器能生成任何注入能生成的代码，还能看到完整的方法体、做跨属性分析；A 只能在既定的 `Set`/`Get` 两点里塞调用。A 的独有优势只剩两条：零工具链依赖（一个编译器开关都没有，编译即生效），和声明点的局部性（行为写在属性上，不在另一个文件里）。这两条值不值得为它们造一个隐藏控制流的机制——是本场反复回访的问题。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`<Trim, Notify, Undoable, MaxLength(25)>` 是标准属性列表语法，无词法歧义。真正的歧义在语义层：**一个属性到底是普通元数据还是注入指令，由基类决定**。于是：

```vb
' 若 Wrapper 忘了 Inherits PropertyHandlerAttribute：
<Wrapper>
Public Property Rating As Integer = 3
```

会静默编译成一个什么都不做的普通特性——用户以为注入了行为，实际没有。反过来，`<Wrapper>` 若继承的是 `PropertyHandlerAttribute`，却被贴在方法或类上，是报错还是忽略？建议未定义。还有一个语法面的事实约束：VB 属性语法不允许命名实参——_"Because in VB named argument syntax does not correspond to constructor arguments but field/property initialization this is not permitted in attribute."_（2017.08.09），所以 `Regex`、`MaxLength` 的配置只能走位置实参，这限制了配置的可读性。

#### 2. 角案例 / 边界语义

- **初始化器绕行。** VB 自动属性初始化器在构造函数里直接写字段，不走 `Set` 访问器。因此：

  ```vb
  <ThrowOnNull, Regex("^\d{3}-\d{2}-\d{4}$")>
  Property ListingCode As String = "<None>"   ' 初始值不匹配正则 —— 但不会抛，因为初始化器绕过了处理器。
  ```

  建议原文的示例恰好用了不匹配正则的初始值 `"<None>"`。这要么是作者没意识到绕行，要么是"初始化器不校验"的语义没写出来。两者都必须进 speclet，且用户很可能期待初始化器也过校验。
- **只读属性的 `Set` 处理器是死代码。** `<NotSupported> Public ReadOnly Property CanRead As Boolean`——ReadOnly 属性没有 `Set` 访问器，`OnPropertySet` 永远不会被注入。是编译错误、警告，还是静默忽略？建议未说。
- **`Overridable` / `Overrides`。** 处理器注入基类访问器；派生类 `Overrides` 之后，基类访问器不再被调用，处理器随之失效——除非派生类重新声明。而 `NotOverridable` 则让派生类永远无法补注入。建议对继承面只字未提。
- **结构与 `Shared` 属性。** 结构上的自动属性：处理器签名里的 `sender As Object` 会**装箱整个结构**；`ByRef backingField` 对结构字段合法，但结构和"身份/可变"语义纠缠。`Shared` 属性没有实例，`sender` 传什么？类型对象还是 `Nothing`？建议未定义。
- **泛型处理器与值类型。** `IdempotentAttribute` 的 `OnPropertySet(Of T)` 用 `?=` 比较新旧值。`?=` 是 `proposal-null-equality-operators.md` 里的二值逻辑相等运算符，而那份建议自己还有 3 个未决问题（运算符是否适用于非可空值类型、优先级、结合性）。一个处理器示例依赖另一份未定型的建议，原型链是脆的。若 `T = Integer`，`?=` 在无 null 值类型上退化成什么，两份建议都没说。

#### 3. 作用域与绑定

处理器调用点按**名字约定**绑定到 `OnPropertySet` / `OnPropertyGet`：编译器在 `PropertyHandlerAttribute` 派生类上找同名静态方法。名字匹配之后是**参数形状匹配**——标准前四个参数（`sender`、`propertyName`、`ByRef backingField`、`ByRef value`）按属性类型具体化（`TrimAttribute` 用 `String`、`AutoRoundAttribute` 用 `Decimal`、`NotSupportedAttribute` 用 `(Of T)`），尾部再拼接构造参数（`AutoRound` 的 `digits`）。We `Suspect` 这里没有真正的重载决议规则：同名方法多个泛型变体时怎么选、`ByRef` 形状不匹配时是跳过还是报错、构造参数与处理器形参的个数/类型对应怎么校验——建议全部留白。语义模型里，注入后的访问器主体是合成代码，`backingField` 引用的是用户无法命名、无法引用的生成字段——IDE 的"查找所有引用"和重命名工具必须知道这个字段与属性的映射，否则重命名属性会留下旧字段名。

#### 4. 与既有特性的交互

- **`CallerMemberName` / `CallerInfo`。** 处理器收到 `propertyName As String`，编译期注入的是字符串字面量。若属性被重命名，字面量随代码生成一起更新——这反而比手写 `CallerMemberName` 更可靠。但处理器能否也用 `CallerMemberName` 拿属性名？两份机制并存会造成写法分裂。
- **表达式树。** 属性**读取**出现在表达式树里时，树捕获的是编译好的 getter 调用，注入发生在访问器内部——表达式树无碍。但属性**赋值**不能进表达式树，本特性不引入新的表达式树场景。无新增风险。
- **`Handles` / `WithEvents` / `Custom Event`。** `<IgnoreExceptions> Event Closing As EventHandler` 是隐式事件（自带 AddHandler/RemoveHandler/RaiseEvent）；若事件显式声明为 `Custom Event ... End Event`，注入点在哪？`WithEvents` 字段上的事件、模块里的事件，行为都不同。建议的未决问题承认"事件注入的语义与位置"未定——We 认为这不是未决问题，而是**没设计**。
- **`Async`。** 属性 `Set` 不能 `Async`，所以处理器必须同步。`<RaiseAsync>` 只能在 `RaiseEvent` 处做 fire-and-forget——异常丢失、时序漂移，这不是异步增强，是语义削弱。

#### 5. Breaking change 与兼容性

对**既有代码零破坏**——新基类、新特性，旧代码不重编译不受影响。We 认为真正要回答的是"重编译行为变不变"的两个方向：

- **库作者采用。** 一个库在新版本给 `Title` 加上 `<Trim>`，消费方重编译后 `Title` 的写入行为悄悄改变（值被裁剪）。这不算语法破坏，但是**行为破坏**——对"数十万安静客户"的稳定预期是个威胁。主线对这类"重编译后行为漂移"几乎零容忍。
- **调试体验。** 给既有属性加上处理器后，断点/单步看到的不再是源码里的访问器。这不是语法 breaking，是工具体验 breaking。
- **与 #196 的关系。** 主线曾拒绝 #196 `Implicit Property Backing Fields`——_"The design team found this idea deeply unsettling."_（Rejected，2017.11.15）。本建议虽然没有"隐式字段"的语法，但把后备字段作为 `ByRef` 参数**暴露给任意用户代码改写**，是同一片让人"不安"的地面。We 尊重这份不安。

#### 6. Option Strict / 编译选项分叉

处理器的形参常写成 `As Object`（如 `ThrowOnNullAttribute`）。在宽松模式下，处理器作者写 `value = value.Trim()` 会对 `Object` 做晚期绑定，编译通过；在严格模式下这是编译错误。同一份处理器源码，行为随项目选项分叉——文档和训练负担。`?=` 在严格/宽松下的解析也要一致。建议对这两条路径只字未提。

#### 7. IDE / IntelliSense

- 键入 `<` 时，补全应只列出**适用于该属性类型**的处理器（`Trim` 只对 `String` 合法，`AutoRound` 只对 `Decimal`）——这要求 IDE 解析处理器参数形状并做类型过滤，属于新的分析器基础设施。
- 断点与"转至实现"：属性访问器是合成的，断点落在哪一行？"转到定义"是跳到特性类吗？
- 生成访问器在 `Peek Definition` 里显示吗？显示什么？
- 建议对这些全部沉默。`Probably`：不做进原型就等于没设计，这是本特性能否落地的及格线之一。

#### 8. 数据 / 普遍性

场景是普遍且高频的——INotifyPropertyChanged 就是 poster child，主线为此开了 #219 专项。但**对"特性注入"这个机制本身，没有用户请求数据**：2018 年的正面印象来自一个场景演示（#282），不是来自用户需求。`Suspect`：真实代码里的高频痛点是"INPC 样板"，那已被 #194（批准做原型）和源码生成器两路覆盖；`Trim`/`Undoable`/`MaxLength` 的组合式自定义行为是 Anthony 的系统化延伸，需求频率没有量化。数据支撑是软的。

#### 9. 更简替代

- **手写访问器 + MVVM 框架**：现状，样板多但完全确定、可断点、无黑盒。
- **#194 `WithPropertyEvents`**（主线已批准做原型）：专门解决 INPC，比本建议窄、比本建议稳。
- **源码生成器 / `Replaces`**（13.2，B 方案）：能力覆盖 A，生成代码可见可调。
- **Analyzer**：可以提示"这个属性该加 Trim/Notify"，但不能替代编译——只能做脚手架。
  真正只有 A 能做、且别人做不了的：**在属性声明点一行内组合多个行为**。这个"局部性 + 组合性"是 A 的护城河；其余价值都有更简替代。

#### 10. 成本 / 优先级

实现面：识别 `PropertyHandlerAttribute`、按名字/形状解析处理器、生成调用链、定义顺序与写回、事件注入、IDE 补全与断点。比 C 方案（内置集）大一个量级，比 B（生成器）小。优先级：ModVB 排在前面的还有模式匹配、可空流分析、交/并类型——智能属性在实现难度中等、设计风险偏高（隐藏控制流）、且与主线机制重叠，排序上不该插队。`Probably`：若做，C 方案的"内置 `Notify` 先走通"比 A 方案的全开放更配得上这个优先级。

#### 11. 运行时 / CLR 硬约束

- 处理器调用本身无 CLR 障碍：字段 `ldflda` 传 `ByRef` 合法，静态方法调用是普通 `call`。
- `sender As Object` 对值类型属性会装箱；结构属性上每次 `Set` 一次装箱，性能与语义都有代价。
- `PropertyHandlerAttribute` 必须继承 `System.Attribute` 并满足 `AttributeUsage`；它同时是伪特性——编译器识别 + 元数据发射，两个表示的一致性由编译器维护。PEVerify 无碍。
- 无表达式树约束（见第 4 条）。

#### 12. 值不值得做

逐条打分。价值：高（声明式组合真实有用、演示获好评、消除样板正中原则 #9）。成本：中（编译器下沉 + IDE 基建，但远小于全元编程）。风险：高（隐藏控制流违背原则 #7、与 #219 决策线的机制重叠、顺序/事件/反射角案例成串）。**净结论：作为开放可扩展机制（A）不值得；作为收敛特性集（C）值得考虑；作为现状（D）也能过。** 热情不抵消可行性——We 对 `<Trim, Notify, Undoable>` 的读感没有异议，但对"编译器替用户执行看不见的访问器"这件事，必须用最保守的姿势推进。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心，全特性最亮的部分。`<Trim, Notify, Undoable, MaxLength(25)>` 一行替代四个访问器。
- **读起来像英语、对新手友好（原则 #5）**：声明面无可挑剔，这解释了 2018 年演示的正面印象。
- **避免隐蔽的控制流 / 语义变化（原则 #7）**：**最大的扣分项**。`Return?` 因"一个细微字符改变控制流"被主线拒，而一个 `<RaiseAsync>` 改写的是一整个 `RaiseEvent` 的异常语义。A 方案在 #7 上比 `Return?` 严重一个量级。
- **不引入"第二种做事方式"（原则 #3）**：**第二个扣分项**。INPC 场景已有 #194 与源码生成器两条路，A 是第三条。主线 2017 年明确说过"若有了 `Replaces` + 生成器，就不会做 `Bindable`/`WithPropertyEvents'"——机制重叠是主线最警惕的。
- **不为边缘场景加特性（原则 #6）**：场景本身不边缘，但"开放可扩展机制"为的是边缘场景的组合自由，这一点要拆开看。
- **永不破坏既有代码（原则 #1）**：静态层面满足；"库采用后重编译行为漂移"是软破坏，需文档与版本策略。
- **与主线关系（对照表 2.3）**：关系标为"一致"——Smart Attributes 注入在主线"演示获好评"、Anthony"系统化"。We 认可这个方向一致性，但要点破一句：**演示获好评 ≠ 机制获准**。主线对 #282 演示只留下一句正面印象，从未把"特性注入"提上决策台；决策台上坐的是 #194 与生成器。ModVB 系统化它是合理延伸，但要带着这条主线记忆，别把演示当授权。

### RESOLUTION:

1. **场景与声明面（读感）原则上认可**：在属性声明点用特性列表组合行为，读起来像英语、消除样板，值得继续探索。
2. **不接受 A 方案的开放形态作为当前方向**：按名字约定的静态处理器签名匹配、未定义的顺序与写回点、可短路的隐式控制流、事件注入改写异常语义——这四个面合计违背原则 #7 与 #3，角案例成串，规范不可收敛。
3. **若推进，走 C 方向（收敛为内置处理器集）**：先做编译器内置的 `Notify`、`ThrowOnNull` 这类高频行为，每个行为有确定的下沉与确定的管线相位（写回前/写回后）。自定义扩展不靠静态方法名猜，改为显式 `Implements` 接口式声明，且必须通过同样的相位模型。
4. **顺序与写回管线必须先行定义**：单一写回点 + 处理器相位（`BeforeWrite` / `AfterWrite`）+ 短路（返回 `True` = 跳过后续处理器与写回）。在管线定型前，`<Trim, Notify, Undoable, MaxLength(25)>` 这类示例不许进规范——它现在的顺序在任意一个合理管线假设下都自相矛盾。
5. **事件注入（`IgnoreExceptions` / `RaiseAsync`）标为 `No Plans` 直到另行论证**：它们改写 `RaiseEvent` 的异常语义，是 #167 同款理由。`Probably`：最终不会做，除非有极其充分的使用案例。
6. **与 13.2（`Replaceable`/`Replaces`）和 #194 对表后再定去留**：这是 #219 决策线的延续。若生成器 + `Replaces` 覆盖了 90% 场景，内置集只该保留生成器覆盖不到的部分（如 `Trim` 这类就地变换）——避免重复造第三种机制。
7. **判定：Table**——等待三个信号：(a) 与源码生成器/`Replaces` 的调和表；(b) 一份收敛的 speclet（内置集 + 管线相位 + 角案例 + 反射面 + IDE）；(c) 一个最小原型（内置 `<Notify>` 走通语义模型与断点）。信号齐了再回 Active。

### Implication:

- 起草 speclet：处理器相位模型（`BeforeWrite`/`AfterWrite`）、单一写回点、短路规则、内置处理器集范围、初始化器绕行的显式声明、只读属性死处理器的诊断策略。
- 与 `proposal-replacement-modifiers.md`（13.2）团队对表：确定"注入 vs 替换"的分工，合并成一份"声明式编程"路线图，避免两套机制抢同一片地面。
- 写最小原型：内置 `<Notify>` 生成 `PropertyChanged` 调用，验证语义模型 `GetSymbol` 返回合成访问器、IDE 断点落点、重命名属性的字段映射。
- 补一份事件注入的风险分析（异常语义、fire-and-forget、`WithEvents`/`Custom Event` 交互），作为 No Plans 的证据存档。
- 与 `proposal-null-equality-operators.md` 对表：确认 `?=` 在无 null 值类型（`T = Integer`）上的语义，否则 `IdempotentAttribute` 示例不能作为处理器范式。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：处理器签名解析规则——同名多泛型变体如何选择、`ByRef` 形状不匹配的诊断、构造参数与处理器形参的对应校验。`Suspect`：现有设计完全无法回答，需在 C 方向下重做。
- `OPEN QUESTIONS`：伪特性的反射面——`PropertyHandlerAttribute` 是否照常落元数据？`GetCustomAttributes` 读到什么？若读，如何保证"元数据表示"与"注入调用点"永不漂移？
- `OPEN QUESTIONS`：`<Wrapper>` 被贴在非属性/事件声明上时，是错误还是忽略；未继承 `PropertyHandlerAttribute` 的 `<Wrapper>` 是静默无行为还是诊断。We 倾向两个都报诊断。
- `OPEN QUESTIONS`：结构属性 + `sender As Object` 装箱的取舍；`Shared` 属性的 `sender` 语义。
- `TODO`：把建议原文引用的未声明处理器（`Wrapper`、`Notify`、`MaxLength`、`Regex`、`IgnoreExceptions`、`RaiseAsync`）全部补齐声明，或从示例中删除——现在示例面比机制面大。
- `TODO`：确认原文 1959 行 `#End Region` 残留为笔误并标注删除；`IdempotentAttribute` 原文的 `Function ... As Boolean` + `End Sub`（原文第 1936 行）被建议静默改成 `End Function`——修正应注明，避免来源漂移。
- `Follow-up`：与主线 `#282` 记录核对演示的确切范围（当时演示的是 INPC 场景，非通用机制），把"演示获好评"精确到场景而非机制。

### 状态

- **LDM 状态：Table**（开放机制不进入 Active；等待管线 speclet + 最小原型 + 与生成器/`Replaces` 的调和信号）。
- **三态判定：Table** — 场景真实、声明面获好评（原则 #9、#5），但开放机制违背 #7、#3，且与主线 #219 决策线机制重叠；收敛为内置集（C）后可以重新评估为 Active。

---

## 附录：特性评价

# 建议评价报告：proposal-smart-attributes.md

## 评价对象

- 建议：proposal-smart-attributes.md — 智能属性（`PropertyHandlerAttribute`）
- 来源：Anthony 原文第 13.1 节 "Smart Attributes – Declarative w/o Source Generators"（`..\AnthonyDesign_wordpress.txt` L1867–2003；使用点与六个处理器声明逐字来自该节）；关联 `proposal-replacement-modifiers.md`（13.2）、`proposal-null-equality-operators.md`（`?=`）；主线锚点 #282（2018.03.21 演示）、#219 决策线（2017.11.15 / 2017.12.06）
- 配方目标：以特性列表在属性/事件声明点声明式组合校验、通知、撤销、格式化行为，无需源码生成器；简单性优先

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。声明式组合的读感真实且示例可操作（全部来自原文），但顺序/写回点/事件语义未定义，建议自带的 `<Trim, Notify, Undoable, MaxLength(25)>` 示例在合理管线假设下自相矛盾（Notify 在写回前触发）；无原型 | 已提供/已检查 | 未决问题 5 个（≥4）封顶效果；核心语法未定型 = 效果未显现到 4 分以上 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。表层是 VB 属性列表惯用法（angle-bracket、自动属性），但"特性即代码注入"是外部概念（近于 C# source generator 的语言化，比生成器更激进）；打包了事件注入（`IgnoreExceptions`/`RaiseAsync`）这一强无关能力；违背原则 #7（隐藏控制流）、#3（第二种做事方式） | 已检查 | 继承面（Overrides/Shared/结构）与初始化器绕行未覆盖；与 #196 被拒的"隐式后备字段"同属一片让主线不安的地面 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全但：无文法/speclet 修改、无兼容性分析；签名匹配、注入顺序、字段写回相对顺序、反射面全部含糊；引用了六个未声明的处理器（`Wrapper`/`Notify`/`MaxLength`/`Regex`/`IgnoreExceptions`/`RaiseAsync`）；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 静默修正原文 `End Sub`→`End Function`（原文 L1936）未标注；`#End Region` 残留（原文 L1959）被列进未决问题但未处置；示例面大于机制面 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/水/光正向：消除样板（#9）、声明读感（#5）、盘活自动属性资产、演示获好评；暗风险突出：隐藏控制流（调试/异常时机）、与主线 #194/生成器机制重叠（风=演化一致性断裂）、事件注入改写异常语义；Drawbacks 诚实但浅，未覆盖顺序/初始化器/事件/IDE | 已检查（预测待定） | 与主线 #219 决策线（INPC = 生成器 poster child）的冲突未在文档中直面；实际影响须"已采纳"后定 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料 = Anthony 13.1（标注于 Summary/Motivation，章节号未显式写出）；`?=` 明确引用 `proposal-null-equality-operators.md`；13.2 在 Alternatives 中被点名；继承 VB 自动属性/属性列表惯用法 | 已检查 | 未声明 2018.03.21 #282 演示出处与主线 #219/#194 的反对方立场；修正原文笔误未标注；`?=` 自身有 3 个未决问题、无原型，被引用时未提示其未定型状态 |

## 设计原则对照

- **与 VB 基因：偏离**（部分一致）——符合原则 #9（消除样板）、#5（读起来像英语）、#1（静态无破坏）；严重偏离 #7（隐藏控制流，`<RaiseAsync>` 与 #167 `Return?` 同源）、#3（第二种做事方式，与 #194/生成器重复）；#6（不为边缘场景加特性）与"开放可扩展机制"的定位存在张力。
- **与主线关系：主线一致（方向）+ 机制冲突（决策线）**——对照表 2.3 标"Smart Attributes 注入 = 演示获好评 / Anthony 系统化 / 一致"属实（2018.03.21 #282 演示获好评）；但主线 #219 决策（2017.11.15 / 2017.12.06）已认定 INPC = 源码生成器 poster child、#194 批准做原型，本建议的开放机制与这两者重复。**演示获好评 ≠ 机制获准。**
- **破坏性变更：静态无，动态有**——既有代码不重编译不受影响；但库作者采用处理器特性后，消费方重编译行为漂移（值被裁剪、事件异常被吞）；事件注入改写异常时机。

## 总评

- **达成程度：部分达成**——声明面（读感、消除样板）与场景价值成立；机制（顺序/写回/签名/反射/IDE/继承面）、兼容性论证、范围收敛未完成；建议自带示例的管线自相矛盾是硬伤。
- **LDM 三态建议：Table**——不作为开放机制（A）进入 Active；收敛为内置处理器集（C：`Notify`/`ThrowOnNull` + 显式相位管线 + 最小原型）后重新评估。不 Reject：场景真实、演示获好评、原则 #9/#5 命中。
- **主要问题**：① 开放机制违背 #7（隐藏控制流）/ #3（第二种做事方式），与主线 #219 决策线机制重叠；② 注入顺序与字段写回点未定义，示例自相矛盾；③ 事件注入（`IgnoreExceptions`/`RaiseAsync`）改写异常语义，建议标 No Plans；④ 签名匹配、反射面、IDE、继承面、初始化器绕行全部未覆盖；⑤ 依赖未定型的 `?=`；⑥ 状态行占位、无原型、引用六个未声明处理器。

## 返工建议

- **补充章节**：处理器相位模型（`BeforeWrite`/`AfterWrite` + 单一写回点 + 短路规则）与顺序语义；签名解析规则（显式 `Implements` 接口式 vs 命名约定）；事件注入的风险分析；反射面（伪特性元数据行为、`GetCustomAttributes`）；IDE/断点/重命名映射；兼容性（库采用后的重编译行为、`langversion` 门控）；`Option Strict` 分叉；继承面（`Overridable`/`Overrides`/`Shared`/结构）。
- **补充证据**：最小原型（内置 `<Notify>` 走通语义模型 + 断点 + 重命名）；与 #194、`Replaceable`/`Replaces`（13.2）的取舍表；2018.03.21 #282 演示的精确范围核实；`?=` 在无 null 值类型上的语义确认。
- **未决问题处理**：只读属性死处理器 = 诊断（错误或警告，建议二选一并写明）；初始化器绕行 = 显式声明"初始化器不过处理器"并给示例；`Do Nothing` 构造形态 = 说明这是"伪特性 + 元数据仍发射"的必然形状而非推荐风格；`#End Region` 残留 = 确认笔误并删除；`End Sub`/`End Function` 修正 = 标注来源；未声明处理器 = 补齐声明或从示例删除。
- **设计探索**：把 A 方案改写为 C 方案的一页 speclet（内置集 + 相位 + 管线），验证"收敛"后是否还保留组合价值的核心；与 13.2 合并为单一"声明式编程"路线图，明确注入（A/C）与替换（B）的分工边界。

---

## 附录：C# 生态与互操作考量

_主题映射：本提案（智能属性 / `PropertyHandlerAttribute`）＝「属性(attribute)既是元数据（ECMA-335）又是编译器行为」+ 编译期代码改写。在 C# interop 索引中对应 T6（Source generators / 元编程）、T5（AOT/trimming/反射）、T1（治理与节奏）；与 C# 的 caller-info attributes、`[Experimental]`、`[InterpolatedStringHandler]` 等 well-known attributes 机制强相关。C# 原文均逐字取自 `..\..\csharplang`，标 `→` 路径。_

### 相关 C# 现实方向

**1. C# 有整族「编译器识别特性」（well-known attributes），但集合是封闭的。** 编译器 "specially recognizes" 一小组由框架持有、sealed 的特性类型，每个特性有确定的 lowering 或诊断策略：

- **caller-info 族**（C# 5 的 `CallerMemberName`/`CallerFilePath`/`CallerLineNumber`；C# 10 的 `CallerArgumentExpression`）。调用点由编译器填入字符串：
  > "The compiler specially recognizes the attribute on `Debug.Assert`. It passes the string associated with the argument referred to in the attribute's constructor (in this case, `condition`) at the call site."
  → `proposals\csharp-10.0\caller-argument-expression.md`
  > "Like the other `Caller*` attributes, such as `CallerMemberName`, this attribute may only be used on parameters with default values."
  → 同上
- **`[InterpolatedStringHandler]`**（C# 10）：编译器识别特性**改变插值字符串的 lowering**（调用点改写）——这是 C# 里离「特性影响代码生成」最近的先例：
  > "The compiler recognizes the `System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute`:"
  → `proposals\csharp-10.0\improved-interpolated-strings.md`
  > "This attribute is used by the compiler to determine if a type is a valid interpolated string handler type."
  → 同上
- **`[Experimental]`**（C# 12）：编译器识别特性承载**诊断策略**（门控实验性 API），不含代码改写：
  > "Report warnings for references to types and members marked with `System.Diagnostics.CodeAnalysis.ExperimentalAttribute`."
  → `proposals\csharp-12.0\experimental-attribute.md`
  > "Although the diagnostic is technically a warning, so that the compiler allows suppressing it, it is treated as an error for purpose of reporting."
  → 同上
- **低层/互操作 well-known 特性群**：`[UnscopedRef]`（C# 11，改 ref-safe-context）、`[InlineArray(N)]`（C# 12）、`[OverloadResolutionPriority]`（C# 13，改重载决议）、`[ModuleInitializer]`/`[SkipLocalsInit]`（C# 9）：
  > "To fix this the language will provide the opposite of the `scoped` lifetime annotation by supporting an `UnscopedRefAttribute`. This can be applied to any `ref` and it will change the *ref-safe-context* to be one level wider than its default."
  → `proposals\csharp-11.0\low-level-struct-improvements.md`
  > "We introduce a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type as a means of steering API consumers to use specific APIs…"
  → `proposals\csharp-13.0\overload-resolution-priority.md`
- LDM 对「well-known attribute」的措辞：
  > "Let's let any static method be a module initializer, and mark that method using a well-known attribute."
  → `meetings\2020\LDM-2020-04-08.md`

**机制事实**：这些特性类型都由 runtime/compiler 持有、sealed、集合**封闭**——C# 没有「让编译器认识任意用户基类」的开放扩展点；用户自定义的声明式代码改写，唯一的官方出口是 Source Generators（C# 9/10，本镜像无提案正文，见引用纪律）。

**2. 编译器识别特性照样落元数据（非伪自定义特性）。** interceptors 工作组被问到「`[InterceptsLocation]` 是否在 emit 时被丢弃」时明确回答：
> "No. Other attributes which are specially recognized by the compiler (which also are not pseudo-custom attributes) aren't dropped in this way."
→ `meetings\working-groups\interceptors\IC-2023-04-04.md`

言下之意：「编译器识别」≠「伪自定义特性」（`Nullable`/`Dynamic` 那种 emit 时丢弃的）。这直接回应了本提案的 OPEN QUESTION（伪特性的反射面）。

**3. 反射面场景被 AOT 压力推向「信息放回类型系统」。** interceptors 的动机原文：
> "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible. To address this, we think that we need to take another look at the scenarios that are considering interceptors and see if we can put that information back into the type system."
→ `meetings\2023\LDM-2023-07-24.md`

**4. C# 对 VB 安全模型的表态。** unsafe-evolution 明确 VB 无 unsafe 上下文、无指针，故不需要 *requires-unsafe* 支持：
> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
→ `proposals\unsafe-evolution.md`

### 现实 vs 提案

| 提案面 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| 概念：属性同时是元数据与编译器行为 | well-known attributes 家族（T6 周边） | **兼容** | C# 有整族先例（caller-info / InterpolatedStringHandler / Experimental / UnscopedRef / OverloadResolutionPriority）；本提案是 VB 对同一块地（attribute = behavior）的走法 |
| PROPOSAL A：开放可扩展基类 + 名字约定注入 | well-known 集合封闭，无「开放基类 → 编译器识别」机制 | **冲突** | C# 把「用户自定义代码改写」全部路由到 Source Generators；编译器硬编码认识一小组 sealed 框架特性。A 的开放形态在 C# 里无对应机制，且被 C# 的取向（信息放回类型系统、生成代码可见）反对 |
| PROPOSAL C：收敛为内置处理器集 | 正是 well-known attribute 模型 | **兼容** | `CallerMemberName`、`[InterpolatedStringHandler]` 就是「内置一小撮、每个有确定 lowering」的先例；C 是唯一有 C# 先例支撑的形态 |
| 反射面（伪特性是否落元数据） | interceptors WG 明确「编译器识别特性不丢弃」 | **兼容（有先例答案）** | IC-2023-04-04 原文；本提案 OPEN QUESTION 的 C# 先例答案是「照常落元数据，编译器维护双表示一致」 |
| 顺序/写回管线 | C# well-known 特性从不注入用户代码到访问器管线 | **脱节** | caller-info 是调用点改写、InterpolatedStringHandler 是调用点 lowering、Experimental 只报诊断——没有一个先例是「把任意用户方法排进 Set/Get 管线并定义写回点」。相位模型对 C# 也是新的 |
| 事件注入（`<RaiseAsync>` / `<IgnoreExceptions>`） | 无对应 | **脱节** | C# 没有「特性改写 RaiseEvent 异常语义」的任何近例 |
| 隐藏控制流 vs AOT | 编译器识别特性的行为是编译期静态的 | **兼容（机制层面）** | 注入在编译期完成，产物是普通 IL，AOT 无碍；但「行为来自黑盒」与 C#「生成代码可见、可断点」取向相反 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：`.vbx` 若做内置处理器集（C 方向），把注入固定为**编译期 lowering**（安全、AOT 友好），不引入运行时反射/`DynamicMethod` 变体；事件注入（`<RaiseAsync>`）这类「运行时改语义」维持 No Plans。
2. **source-gen 桥**：开放可扩展性（A 的诉求）应走 Source Generators / `Replaceable`–`Replaces`（13.2），与 C# 对齐——能力覆盖 + 生成代码可见 + 可断点；`.vbx` 若支持 Roslyn 生成器管线，可直接复用 C# 生态的生成器。
3. **识别新元数据**：`.vbx` 编译器必须**认识 C# well-known 特性的元数据**才能正确互操作——`[Experimental]`（调用方应得诊断）、`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`（AOT 风险提示）、`[UnscopedRef]`/`[InlineArray]`（低层类型布局）、`[OverloadResolutionPriority]`（重载决议）。它们只是 ECMA-335 特性，C# 库照常发射；VB 不认识不会崩，但会漏掉编译器保证的语义。若 `PropertyHandlerAttribute` 落地，也照常发射元数据，让反射/工具（含 C# 侧工具）能读到「行为配置」。
4. **与 `CallerMemberName` 并存**：处理器若被 C# 侧消费，`propertyName` 字面量注入（定义点）与 `CallerMemberName`（调用点）是两种机制；建议在 speclet 里明确二选一或按调用点语义对齐，避免「VB 特性注入拿到的名字」与「C# 调用方看到的 CallerMemberName」漂移。
5. **闭集合 + 显式相位**：若走 C，编译器持有一张「内置处理器表」（仿 Roslyn 的 well-known attribute 表），每个处理器有确定 lowering 与 `BeforeWrite`/`AfterWrite` 相位——这是与 C# 机制最同构、最可互操作的形状，也顺带解决了本提案「顺序语义」的暗雷。

### 对既有 RESOLUTION / 三态判定的影响

- **强化 RESOLUTION 第 2 条（拒 A 的开放形态）**：C# 机制现实（well-known 集合封闭、开放注入无先例、自定义改写路由 source-gen）为「不接受开放机制」提供了**生态层**论据，不只是 VB 原则 #7/#3。
- **支持第 3 条（走 C 方向）**：C 形态与 C# well-known attribute 模型同构，是唯一有跨语言先例的路线。
- **给伪特性反射面一个先例答案**：C# interceptors WG 已裁定编译器识别特性不丢弃（IC-2023-04-04）；本提案应照此「照常落元数据」，把「双表示不漂移」写为编译器责任。
- **三态判定维持 Table，不变**：生态考量没有把 (a)(b)(c) 三个信号从「缺失」变成「齐备」；附录只是把「为什么不开 A」的理由补得更实。

### 引用纪律

- 本附录 C# 原文全部逐字取自 `..\..\csharplang` 并经 Grep 复核，标 `→` 路径；索引第四节 6 段已核实原文中，本附录采用了 unsafe-evolution VB 小节与 span-safety Introduction 两条（均复核逐字一致）。
- `OPEN QUESTIONS`：本附录「C# well-known 特性集合封闭」的断言基于 csharplang 文本（interceptors WG 措辞 + 各提案）；Roslyn 实现侧的 `WellKnownAttribute` 完整清单在本镜像之外，如需精确清单应查 dotnet/roslyn。
- `OPEN QUESTIONS`：Source Generators 提案正文不在本镜像（属 dotnet/roslyn docs），故 source-gen 相关只引用索引摘要与 LDM-2023-07-24（interceptors/AOT 动机），未引用 source-generators.md 原文。
