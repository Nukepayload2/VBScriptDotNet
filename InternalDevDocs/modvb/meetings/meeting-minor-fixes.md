# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们回到一份短小但不简单的提案：Anthony 的"杂项修复"清单把七项互不相关的小改动打包进一份文件。我们决定逐项过，而不是整包背书——因为其中既有我们有把握的（`Optional` 默认值推断、访问器 `value` 类型推断），也有我们打算直接否决的（`Optional` 关键字省略）。

## Agenda

* [Proposal: Bug Fixes & Other Minutiae / 杂项修复](#proposal-bug-fixes--other-minutiae--杂项修复)

## Proposal: Bug Fixes & Other Minutiae / 杂项修复

_Related: [vblang #197 – Inferred `Set` Parameter Type](https://github.com/dotnet/vblang/issues/197)；[vblang #196 – Implicit Property Backing Fields](https://github.com/dotnet/vblang/issues/196)（被拒）；[vblang 2014-02-17 会议 – GetName operator（#38）](https://github.com/dotnet/vblang/tree/main/meetings/2014)；[vblang 2014-10-15 / 10-23 会议 – nameof 修订 spec](https://github.com/dotnet/vblang/tree/main/meetings/2014)；[主线提案 – Implicit Default Optional Parameters](https://github.com/dotnet/vblang/blob/main/proposals/proposal-Implicit-default-optional-parameters.md)；ModVB：简写属性与事件、Immersive Files、Agile Async_

### 场景与缺口

We 拆出七项各自独立的痛点，先摆桌面上：

1. **`Async Function Main` 合法化**。今天 VB 的入口点不能 `Await`——想异步启动必须包一层 `Sub Main` 调 `helper.GetAwaiter().GetResult()` 的绕行。C# 7.1 起已经可以直接 `async Task<int> Main`。
2. **`Optional` 参数默认值推断**。`Optional trim As String` 被迫写 `= Nothing`，而 Roslyn 仓库里 85% 的 `Optional` 默认值就是类型默认值。
3. **`Optional` 关键字在首个可选参数后可省略**。`Optional hour, minute, second` 想把 `Optional` 只写一次。（原文自标 "?"。）
4. **访问器 `value` 参数类型推断**。`Set(value As String)` / `AddHandler(value As EventHandler(Of EventArgs))` 的形参类型只是对已声明类型的重复，想写 `Set(value)`。
5. **`Custom` 关键字在自定义事件中可省略**。`Custom Event` 直接写作 `Event` + 访问器块。
6. **`NameOf` 省略括号**。`NameOf HeaderText` 而非 `NameOf(HeaderText)`。（原文自标 "?"。）
7. **`NameOf` 支持开放泛型**。`NameOf Dictionary(Of,)` 而非 `NameOf(Dictionary(Of Object, Object))`。（原文标题对"开放泛型方法"也存疑。）

We 先说一句结构意见：**这是七个特性，不是一份**。主线 vblang 的惯例是每特性一份提案（`README.md`：proposal 是 living document，跟踪 status 与 mailing list 讨论）。一份"杂项修复"打包文件作为**追踪单**可以接受，但作为**提案**会让我们无法对单项给 `Active / Reject` 状态机。We 先逐项裁决，随后讨论打包问题本身。

---

### 项 1：`Async Function Main` 合法化

**候选方案**

- **PROPOSAL A — 允许 `Main` 返回 `Task` / `Task(Of Integer)`**，是否 `Async` 均可，与 C# 7.1 对齐。编译器在无同步入口时合成包装入口，调用 `.GetAwaiter().GetResult()`。
- **PROPOSAL B — 同时允许 `Async Sub Main`**。这是"火并忘"形态：`Async Sub` 返回 `void`，运行时的入口点拿到的是调度后的 continuation，进程可能在异步工作完成前退出。C# 明确拒绝 `async void Main`。
- **PROPOSAL C — 什么都不做**，维持绕行 helper。现状是样板，且样板里有 `.GetAwaiter().GetResult()` 这种对新手不友好的仪式。

**权衡：Q&A**

- **A 还是 B？** B 的进程提前退出风险是我们不能接受的隐蔽控制流；A 的 `Task(Of Integer)` 由编译器合成同步包装，进程会等它完成。**取 A，明确排除 B。**
- **是否要求 `Async`？** C# 7.1 允许非 `async` 的 `Task Main`（手写返回 `Task.CompletedTask`）。我们保持一致：`Function Main As Task` 与 `Async Function Main As Task` 都合法。语法零改动——这只是入口点签名校验的放宽。
- **示例的笔误。** 原文的 `Async Function Main(...) As Task(Of Integer) ... End Sub` 以 `End Sub` 收尾——`Function` 必须 `End Function`。这会让示例无法编译，"示例可运行"一栏不过关。We 把示例修正如下，同时记入 OPEN QUESTIONS（原文 Unresolved 已自认笔误）。

```vb
Async Function Main(args As String()) As Task(Of Integer)

    Await Console.Out.WriteLineAsync("This is an example.")

    Return 0
End Function
```

- **与既有 `Sub Main` 冲突？** 若同程序集既有 `Sub Main` 又有 `Async Function Main As Task`，报"入口点指定多次"。这是既有规则的延展，不引入新语法。

**→ 判定：Active。** 明确的 C# 对齐缺口、语法零改动、编译器合成同步包装（C# 已验证过同款模式，PEVerify 无碍）。代价集中在编译器入口点处理，值得。

---

### 项 2：`Optional` 参数默认值推断

**候选方案**

- **PROPOSAL A — `Optional x As T` 省略 `= 默认值` 时按类型默认值补 `Nothing`**。与主线提案 `proposal-Implicit-default-optional-parameters.md` 完全一致，该提案已带原型与统计。
- **PROPOSAL B — 仅当显式写出 `= Nothing` 时才接受省略**（即只去掉 `= Nothing` 这六个字符，`= 0`、`= ""` 仍必须写）。价值稍小，但零语义歧义。
- **PROPOSAL C — 不做**。

**权衡：Q&A**

- **数据有多强？** 主线提案的作者用 Roslyn 仓库实测："of all the optional parameter declarations in VB in Roslyn 2229 out of 2612 of them, or > 85% use the default value of the parameter type as the default value of the parameter. So your suggestion would clean up 85% of the uses of Optional in Roslyn. Sounds like value to me!"（主线提案引用，`>85%`）。这是我们见过的少数有硬数据的提案之一。而且这份主线提案的潜力场景里就引用了 Anthony 的代码（"As pointed out by @AnthonyDGreen"）——本建议的项 2 实际上是把 Anthony 早年在主线推动的提案拿回 ModVB。**证据等级：已提供 + 主线原型已运行。**
- **`String` 取 `Nothing` 还是空串？** 取 `Nothing`。与主线提案映射一致（`Foo(Optional arg1 As String)` → `= Nothing`），且与"可选参数省略=没传"的直觉一致；空串是"传了空串"，语义不同。**决议：一律 `Nothing`（值类型即 0/False），不做逐类型特判。**
- **无类型 `Optional arg0` 呢？** 保持现状：无 `As` 的形参默认 `Object`，`Optional arg0` 仍等价 `Optional arg0 As Object = Nothing`。主线提案明确以不破坏此行为为前提——这是兼容红线，我们照搬。
- **`<CallerLineNumber> Optional x As Integer`（无显式默认）呢？** CallerInfo 参数省略时由编译器填调用点信息，默认值只在非 CallerInfo 调用路径兜底。主线提案的 Invariants 章节对 CallerLineNumber 做了验证（输出 `R=5 / X=5`）。我们要求 ModVB 的 speclet 同样覆盖此不变式。
- **接口实现 / 重写的默认值匹配**（本场最尖锐追问）。2014-02-17 会议已定调："Currently VB requires explicit interface implementation to have exactly the same optional parameter defaults."，RESOLUTION："Stick with current VB rules... we should match based on signature, and then give an error if the defaults don't match." 若接口声明 `= 5`、实现写 `Optional x As Integer`（推断为 `= Nothing`），现有检查会**报新错误**而非静默错配。这是新错误面，不是静默破坏——可接受，但必须写进 spec 并给清晰错误文案。

**→ 判定：Active。** 85% 数据 + 主线原型 + 极 VB（消除样板、低仪式）。注意：这与主线提案基本是同一份东西，ModVB 直接跟进主线实现即可，**不需要 ModVB 自行发明**。

---

### 项 3：`Optional` 关键字在首个可选参数后可省略

**候选方案**

- **PROPOSAL A — 尾规则**：从第一个 `Optional` 起，后续形参不再需要写 `Optional`（默认值仍可省略，配合项 2）。
- **PROPOSAL B — 保持每形参显式 `Optional`**（现状 + 项 2）。
- **PROPOSAL C — C# 式彻底取消关键字**：`Sub F(a As Integer, b As Integer = 5)` 中 `b` 因带默认值而可选。这是比 A 更大的步子，会改变元数据 `[Optional]` 的发射规则，直接否决。

**权衡：Q&A**

- **尾规则在语法上是安全的吗？** `Probably` 安全。VB 编译器已强制"可选参数必须居尾"（`Sub F(Optional a As Integer, b As Integer)` 今天就是错误）。既是已强制的不变式，尾规则不产生新语法分支。
- **那为什么我们不喜欢它？** 因为**读者不可见**。`Sub F(a As Integer, b As Integer, Optional c As Integer, d As Integer)`——`d` 是可选，但扫读的人从 `d As Integer` 这一行看不出它可选。`Optional` 是"必需段/可选段"的边界标记，这正是"冗长只在有用时是美德"（原则 #10）里的那种**有用的冗长**。我们花了多年让错误参数列表报"可选参数必须居尾"，现在却要把这个信息藏进位置语义。
- **与项 2 的叠加风险。** 项 2 通过后 `Optional c As Integer` 已省去 `= Nothing`；再省 `Optional` 会得到 `Sub F(..., c As Integer, d As Integer)` 一串长得一模一样的形参，其中 c、d 可选而 a、b 必需——从文本上无从分辨。这是**隐蔽的语义差异**，触犯原则 #7。
- **原文自标 "?"。** 提案本身对这项最没把握，且无原型、无数据。未决问题列表里它占了两条。

**→ 判定：Reject。** 我们很乐意保持显式 `Optional`。语法安全不等于设计正确；把边界标记藏起来换省几个关键字，不值。**本项独立否决，不随项 2 走。**（如果未来有社区数据显示"尾规则"的需求强烈，可以 Table 回访——`OPEN QUESTIONS` 留一条。）

---

### 项 4：访问器 `value` 参数类型推断

**候选方案**

- **PROPOSAL A — 属性 `Set(value)` 与自定义事件访问器全推断**（按原文 After 示例，含 `RaiseEvent(sender, e)`）。
- **PROPOSAL B — 仅属性 `Set(value)` 推断，自定义事件访问器暂不动**。
- **PROPOSAL C — 仅靠 IDE 层面"染色"**：2017-11-15 会议对主线 #197 的评估里提到——"Technically the entire parameter list on `Set` is already optional and we could get most of the value of this proposal by stopping the IDE from generating it because an implicit parameter named `Value` is created. We could just color it blue."（即 IDE 停止生成类型、把隐式 `Value` 标蓝）。这不改变语言，只改变生成代码与展示。

**权衡：Q&A**

- **主线已批准过一半。** vblang #197 "Inferred `Set` Parameter Type" 于 2017-11-15 **Approved-in-Principle**，理由正是我们认同的："it's also fairly easy to just do the 'inference' as when we're creating the setter `MethodSymbol` we already have the type of the property in hand." 属性 Set 的推断在实现上几乎免费——`MethodSymbol` 构造时手边就有属性类型。**项 4 的 Set 部分不是新设计，是主线已批准特性的落实。**
- **`Set(value)` 今天编译成什么？** 关键角案例。VB 允许无类型形参（默认 `Object`，2016-05-06 会议确认过这种"只写名字不写类型"的 API 形态：`Sub S(a, ByRef b, Optional c = Nothing)`）。所以今天 `Set(value)` 在 Option Strict Off 下**已经合法**，`value` 是 `Object`。提案把它重解释为属性类型——这是**对既有合法代码的语义重解释**，触碰"永不破坏"红线。
  - 破坏面多大？`Probably` 很小：写自定义 setter 的人几乎总是写全类型，typeless `Set(value)` 罕见。但"罕见"不豁免原则，我们必须给规则。
  - **候选规则（我们倾向）**：推断只在"属性类型显式声明且 setter 形参无类型"时生效；一旦 setter 显式写了类型，一律尊重显式类型。同时配 `langversion` 门控，使旧编译器/旧语言版本仍把 typeless `Set(value)` 当 `Object`。此规则必须写成 spec 段落。
- **自定义事件的 `RaiseEvent(sender, e)` 推断靠谱吗？** 比 Set 重。`AddHandler(value)` / `RemoveHandler(value)` 的形参就是事件委托类型，推断简单且语义确定。但 `RaiseEvent` 的形参**不严格等于**委托的 Invoke 签名——VB 只要求 RaiseEvent 访问器能把自己收到的实参转交给委托。按 `EventHandler(Of EventArgs)` 推断 `(sender As Object, e As EventArgs)` 是惯例而非强制。把惯例固化成推断，是对既有宽松代码的又一次重解释。**结论：`AddHandler`/`RemoveHandler` 随 Set 一起推断；`RaiseEvent(sender, e)` 暂缓（Table），除非"简写属性与事件"建议先定义了事件访问器的统一简写。**
- **PROPOSAL C 够吗？** 不够。IDE 染色能让"看起来"干净，但源码里的 `Set(value)` 仍是 `Object`，语义模型、补全、`Option Strict On` 下的编译结果都不对。IDE 染色是**脚手架**，不是特性。不过 C 描述了一种退路：若 breaking-change 分析不可接受，我们可先只做 IDE 端并保留 `Set(value As T)` 全写——但那就等于放弃特性。

**→ 判定：Active（限定范围）。** Set / AddHandler / RemoveHandler 推断（对应主线 #197，Approved-in-Principle），须附 breaking-change 分析与 `langversion` 门控；`RaiseEvent(sender, e)` Table。项 5 的 `Custom` 省略与本项共享同一组示例，见下。

---

### 项 5：`Custom` 关键字在自定义事件声明中不要求

**候选方案**

- **PROPOSAL A — `Custom` 可省略**：`Event NameChanged As EventHandler(Of EventArgs)` 后跟访问器块即视为自定义事件。
- **PROPOSAL B — 保留 `Custom`**。

**权衡：Q&A**

- **有语法歧义吗？** 无。普通事件是单行声明、没有 `End Event`；自定义事件有 `AddHandler`/`RemoveHandler`/`RaiseEvent` 块和 `End Event`。访问器块的存在性决定分类，解析器不需要 `Custom` 消歧。
- **值得吗？** 价值是省一个关键字；风险是"第二种做事方式"（`Custom Event` + 访问器 与 `Event` + 访问器并存）。但这不是两个不同语义，而是把冗余关键字变可选——类似于允许 `Dim` 的许多变体。We 倾向于**价值不足**：`Custom` 一字在自定义事件里是明确信号（"这个事件有访问器，读到 AddHandler 别惊讶"），且自定义事件本身不常见，样板不构成高频痛点。
- **与项 4 的捆绑。** 项 4 的 After 示例同时演示了省略 `Custom`——两项共享同一段代码。若项 5 否决而项 4 通过，示例要改写回 `Custom Event`。**两项独立裁决，不互相绑架。**

**→ 判定：Consider（低优先级）。** 语法安全、风险小，但价值未达 Active 门槛。并入"简写属性与事件"建议统一设计（该建议本就要定义事件访问器简写），不单独放行。

---

### 项 6：`NameOf` 省略括号

**候选方案**

- **PROPOSAL A — `NameOf` 作类一元运算符**：`NameOf HeaderText`，括号可选。
- **PROPOSAL B — 维持括号强制**。

**权衡：Q&A**

- **有先例吗？** 有。`TypeOf ... Is`、`AddressOf x`、`Not x` 都是免括号的运算符形式，`NameOf` 作为一元运算符有 VB 基因先例。2014 年 GetName 讨论里我们自始至终用括号写法（`GetName(Text)`、`GetName(Point.X(Of ,).Y)`），从未考虑免括号。
- **歧义与优先级。** `NameOf a + b` 是什么意思？`Not a + b` 因优先级是 `Not (a + b)`；`NameOf` 的实参"必须有一个名字"（2014-10-15 修订 spec 的原则 (1)："the expression must 'have a name'"），`a + b` 没有名字——所以 `NameOf a + b` 若按 `Not` 的优先级解析就报错，而按 `(NameOf a) + b` 解析则是字符串拼接。**必须给 `NameOf` 定义优先级**，这比括号形式多一整块文法要写的 spec。
- **"第二种做事方式"。** 括号强制时只有一种写法；可省略后 `NameOf(HeaderText)` 与 `NameOf HeaderText` 并存。2014-10-15 spec 的原则 (2) 是"must resolve to one single symbol"——这是对**实参**的要求，不是对**写法**的要求，但 IDE 的 rename/find-all-references 要把两种写法都当 `NameOf` 实参跟踪（2014-02-17 的 GENERAL FEEDBACK 就提过："Concern from Aleksey and IDE team about overloads and dealing with ambiguity"）。
- **真实场景。** WPF 的 `DependencyProperty.Register(NameOf(HeaderText), ...)` 是 2014 年就演示过的场景（`DependencyProperty.Register(GetName(Text), GetType(String), GetType(MyClass))`），少一对括号有实际价值，但对 VBScript.NET 的目标用户（脚本化、控制台、文本处理）不是高频痛点。

```vb
' 提案形态（无括号，用于 XAML 依赖属性注册）
Public Shared ReadOnly HeaderTextProperty As DependencyProperty =
    DependencyProperty.Register(
        NameOf HeaderText,          ' <-- 无括号
        GetType(String),
        GetType(MyControl))

Public Property HeaderText As String
    Get
        Return DirectCast(GetValue(HeaderTextProperty), String)
    End Get
    Set(value As String)
        SetValue(HeaderTextProperty, value)
    End Set
End Property
```

**→ 判定：Consider。** 有 VB 一元运算符先例，但需新增优先级文法、制造两种写法、且 C# 无此形态。价值（省一对括号）不匹配文法成本。对脚本化语境（`NameOf` 更接近自然语言）可回访；现不 Active。

---

### 项 7：`NameOf` 支持开放泛型

**候选方案**

- **PROPOSAL A — `NameOf` 允许未绑定泛型**：`NameOf Dictionary(Of,)`，与 `GetType(Dictionary(Of,))` 对称。
- **PROPOSAL B — 维持封闭泛型限定**。

**权衡：Q&A**

- **主线历史：先 Yes 后 No。** 这是本场最有意思的考古。2014-02-17 会议，GetName 讨论中直问："Q. Should we allow unbound generic types, similar to GetType?"——**"A. Yes."** 但当月的修订 spec（2014-10-15 / 10-23，与 C# 对齐）把 `unbound-type-name Dictionary<,>` 明确列为**不允许**，C# 侧示例为 `nameof(List<>)` → "result error 'type expected': Unbound types are not valid expressions"。出货的 VB 跟随修订 spec：`NameOf` 只能用封闭泛型。**本项是把 2 月的 'Yes' 捡回来，对抗 10 月与 C# 对齐的修订。**
- **语义是什么？** `NameOf(Dictionary(Of Object, Object))` 的结果本来就是 `"Dictionary"`——开放泛型不改变结果，只改变**写法**。价值在：① 与 `GetType(Dictionary(Of,))` 对称（同一段代码里一个能写一个不能写，很别扭）；② 改泛型实参不破坏 `NameOf`（引用未绑定类型不随实参变化而失效）。
- **与项 6 叠加的歧义。** `NameOf Dictionary(Of,)` 需要解析器在 `NameOf` 之后做**类型名上下文**的 lookahead（`Dictionary` 是标识符，`(Of ,)` 是类型实参列表）。若项 6 也过，则 `NameOf` 实参文法从"括号内表达式"变成"免括号名字或类型"——文法复杂度叠加。**项 6 未过前，本项独立落地时建议保留括号形式 `NameOf(Dictionary(Of,))`；若将来项 6 过，再统一文法。**

```vb
' Vanilla VB：GetType 允许未绑定泛型，NameOf 不行。
Shared ReadOnly DictionaryType As Type = GetType(Dictionary(Of,))
' 以下在 Vanilla VB 是错误；ModVB 使其合法：
Shared ReadOnly DictionaryName As String = NameOf(Dictionary(Of,))
```

- **开放泛型"方法"呢？** 原文标题写"Open generic types/methods(?)"。方法没有未绑定形态（方法类型实参 `(Of ,)` 不存在），`NameOf` 对泛型方法本来就可以只写方法名（`NameOf(GenericMethod)` 引用方法组需报"must provide its arguments"——2014-10-15 spec 的规则）。**决议：开放泛型只适用于类型，不适用于方法；原文的 "methods(?)" 澄清为否。**

**→ 判定：Active（VBScript.NET 语境）。** 有 2014-02-17 白纸黑字的 'Yes' 支撑，与 `GetType` 对称，成本低；但明确记录：这是对出货主线行为（跟随 C# 的修订 spec）的**有意的分叉**，ModVB 文档须声明。若 ModVB 目标是"与主线行为严格一致"，则本项降为 Table——We 认为 VBScript.NET 作为分叉值得这个对称。

---

### 打包问题：一份提案装七个特性

这是评价标准里的弱提案红旗（"一份提案混杂多个独立特性，边界模糊"），我们作为 LDM 必须直面。

- **不可原子化。** 七个特性中三个 Active、一个 Reject、一个 Consider（项 5）、两个 Consider（项 6/7 其中一个 Active 于分叉语境）。若当整包投票，我们会把项 3 的否决和项 2 的通过绑在一起——两个方向相反的裁决互相污染。
- **状态机无法跟踪。** 主线 README 明言 proposal 承载 status（active/inactive/rejected/done）与 mailing list 讨论。一个文件装七个状态，跟踪表没法写。
- **正面价值：追踪单。** "杂项修复"作为一个 issue/追踪单（tracking issue）列出待办清单是合理的——C# 也这么干（working set）。**我们的裁决：拆分。** 每项独立提案或独立 speclet，共享一个追踪单编号；本会议纪要作为各提案的 _Related_ 引用。

---

### 深度追问：LDM 拷问清单（跨项综合）

按惯例逐条过。

#### 1. 语法 / 文法歧义

- 项 1 无文法改动（入口点签名校验放宽）。
- 项 2 无文法歧义（`Optional x As T` 缺默认值是既有文法中"缺省"的合法化，注意不是新增产生式——今天的 `Optional x As Integer = ...` 中 `= 默认值` 本就可选，只是缺省时目前会报错；`Probably` 可在 binder 层处理）。
- 项 3 尾规则：无新分支（居尾不变式已强制），但引入"可选段起点"的位置性知识。
- 项 4/5：`Set(value)` 与 `Event X As T` + 访问器块均可由上下文确定，无歧义。
- 项 6：`NameOf x` 需新增优先级文法；与二元运算符的交互必须 spec。
- 项 7：`NameOf(Dictionary(Of,))` 需在 `NameOf` 实参位置接受未绑定类型名；与项 6 叠加时 lookahead 成本上升。

#### 2. 角案例 / 边界语义

- 项 2：值类型 → `Nothing`（0）；`String` → `Nothing` 而非空串；可空 `Integer?` → `Nothing`；无类型 `Optional arg0` → 维持 `Object`。
- 项 2 + CallerInfo：`<CallerLineNumber> Optional x As Integer` 无显式默认——调用方省略时由编译器填调用点信息，默认值兜底；不变式必须随主线提案验证。
- 项 4：无类型 `Property P`（属性本身无类型）时 `Set(value)` 推断无来源 → 报错或回落 `Object`，需定规则；`RaiseEvent(sender, e)` 的委托签名推导。
- 项 1：`Function Main As Task(Of Integer)` 非 `Async` 也要合法（C# 同）。

#### 3. 作用域与绑定

- 项 4：语义模型中 `Set(value)` 的 `value` 符号类型 = 属性类型；`GetTypeInfo` 在访问器内返回推断类型；`AddHandler(value)` 的 `value` = 事件委托类型。IDE 补全随之。实现面正是 2017-11-15 说的"creating the setter `MethodSymbol` we already have the type of the property in hand"。
- 项 2：推断出的默认值是合成常量；语义模型对省略的默认值报告 `Nothing`/default。

#### 4. 与既有特性的交互

- 项 2 × 接口实现/重写：VB 要求显式接口实现与重写具有**相同**的可选参数默认值（2014-02-17 RESOLUTION："match based on signature, and then give an error if the defaults don't match"）。推断默认值可能触发新错误——需清晰文案。
- 项 2 × COM interop：COM 可选参数普及，推断 `Nothing` 正确。
- 项 4 × Option Strict Off / late binding：typeless 形参重解释（见项 4 权衡）——两路径行为必须一致。
- 项 1 × 既有 `Sub Main`：入口点冲突检测延展。
- 项 6/7 × rename / find-all-references：2014 年 IDE 团队的 ambiguity 担忧依然成立；两种 `NameOf` 写法都要跟踪。

#### 5. Breaking change 与兼容性

- 项 4 是唯一**真**破坏面：typeless `Set(value)` 在宽松模式已是合法代码，重解释其类型 = 重编译行为变化。破坏面 `Probably` 小，但必须给"仅当属性显式类型 + 形参无类型 + `langversion` 门控"的规则。
- 项 2 的新错误面（接口/重写默认值不匹配）是新增编译错误，不是静默破坏。
- 项 1/5/6/7 均为新能力，不改变既有代码；项 3 新代码才生效但被否决。
- 其余各项均无 `Shadows`/`IsTrue`/`AndAlso` 层面的语义根基触碰。

#### 6. Option Strict / 编译选项分叉

- 项 4 最关键：typeless 形参在 On/Off 下都合法，On 下用法受限（Object→窄化报错）、Off 下宽松。推断后两路径行为一致（都推断为属性类型）。必须验证。
- 项 2 的 `Nothing` 在 On/Off 下都合法，无分叉。

#### 7. IDE / IntelliSense

- 项 4：`value` 补全显示属性类型成员；参数信息显示推断类型。
- 项 6/7：`NameOf` 染色、跳转、rename 跟踪两种写法与未绑定类型名；错误文案（"NameOf 实参必须是单一符号"）延续 2014 spec 原则 (2)。
- 项 1：IDE 入口点识别（`Async Function Main` 高亮为入口点）。

#### 8. 数据 / 普遍性

- 项 2：`>85%`（2229/2612，Roslyn 仓库实测）——本场唯一有硬数据的项。
- 项 4：#197 已 Approved-in-Principle，属性 setter 是通用场景；自定义事件少见。
- 项 1：console/脚本入口高频但形态简单。
- 项 6：WPF `DependencyProperty` 场景真实但非 VBScript.NET 主用户群。
- 项 3/5/7：无数据支撑。项 7 有 2014 'Yes' 的设计先例。

#### 9. 更简替代

- 项 1：`Sub Main` + `helper.GetAwaiter().GetResult()` 绕行——正是要消灭的样板。
- 项 2：显式 `= Nothing`——85% 的样板。
- 项 4：IDE 染色（#197 讨论提出）——脚手架，不改变语言语义，不足以替代。
- 项 6/7：字符串字面量——无 rename 安全，是 NameOf 存在的全部理由的背面。
- 项 3/5：没有"更简"的替代，正因为它们太简——简到不值得做（项 3）或价值不足（项 5）。

#### 10. 复杂度 / 成本 / 优先级

- 低成本：项 2（binder 补默认值）、项 7（binder 允许未绑定类型名）、项 5（parser 放行 `Event`+访问器块）。
- 中成本：项 1（入口点校验 + 合成包装）、项 4（访问器 MethodSymbol 类型推导 + breaking-change 分析）、项 6（优先级文法 + 两种写法）。
- 项 3 成本虽低，**设计否决**，不因便宜而放行。

#### 11. 运行时 / CLR 硬约束

- 无项触碰 PEVerify 或 CLR 存储规则。项 1 的合成包装（`.GetAwaiter().GetResult()`）是 C# 已用生产模式。项 7 不产生运行时操作（`NameOf` 是编译期常量，2014-10-15 spec："The nameof expression is a constant"）。

#### 12. 值不值得做

价值×成本×风险逐项打分见各节判定。汇总：**项 1/2/4 值得，项 3 不值得，项 5/6 待价，项 7 在分叉语境值得。**

---

### VB 基因对照

- **消除常见样板（原则 #9）**：项 1/2/4 正中靶心——`Async Main` 绕行、`= Nothing`、访问器类型重复，都是高频样板。
- **保持 VB-like（原则 #2）**：项 2/4/5/6/7 是低仪式、读起来像英语的方向；项 6 的一元运算符形态有 `TypeOf`/`AddressOf` 先例。
- **不引入"第二种做事方式"（原则 #3）**：项 6（两种 `NameOf` 写法）与项 5（两种自定义事件写法）都有此张力；项 2/4 是减少仪式而非新增方式，干净。
- **避免隐蔽语义变化（原则 #7）**：项 3 被否决的核心理由——位置性的隐式可选段是文本不可见的语义差异。项 4 的重解释需要 `langversion` 门控对冲。
- **冗长只在有用时是美德（原则 #10）**：`Optional` 关键字是"必需/可选"的边界标记，是有用的冗长——项 3 否决在此。
- **与主线关系（对照表 2.3）**：
  - 项 2 = 主线 `Implicit Default Optional Parameters`（Anthony 亦参与），**主线一致**；
  - 项 4 = 主线 #197（Approved-in-Principle），**主线一致**；
  - 项 1 = 跟随 C# 7.1，符合"默认跟随 C#"；
  - 项 6/7 = 回访 2014 GetName/nameof 讨论，其中项 7 的 'Yes' 被主线修订 spec 收回，属 **Anthony 延伸（与主线分叉）**；
  - 项 3/5 = 无主线对应，**Anthony 独立延伸**。
- **根本张力再现**：主线"对扩展设高门槛、默认跟随 C#"；本建议把 2014 被修订掉的设计捡回来（项 7），并对一个主线已拒的小语法（项 3）说否。ModVB 作为沙盒可以激进，但**分叉必须显式标注**——项 7 尤其如此。

---

### RESOLUTION:

1. **`Async Function Main`（项 1）：Active。** 允许 `Main` 返回 `Task` / `Task(Of Integer)`，`Async` 可选；编译器合成同步入口包装（C# 7.1 同款）。**明确排除 `Async Sub Main`**（进程提前退出风险）。修正原文 `End Sub` 笔误为 `End Function`。
2. **`Optional` 默认值推断（项 2）：Active。** `Optional x As T` 缺省默认值时一律补 `Nothing`（值类型即 default）；无类型 `Optional arg0` 维持 `Object`；CallerInfo 不变式照主线提案验证；接口实现/重写默认值匹配的新错误面写进 spec。ModVB 直接跟进主线 `Implicit Default Optional Parameters` 实现，不另起炉灶。
3. **`Optional` 关键字省略（项 3）：Reject。** 位置性隐式可选段是文本不可见的语义，触犯原则 #7/#10；语法安全不等于设计正确。不随项 2 走。
4. **访问器 `value` 推断（项 4）：Active（限定范围）。** 属性 `Set(value)`、`AddHandler(value)`、`RemoveHandler(value)` 推断（主线 #197，Approved-in-Principle）；**`RaiseEvent(sender, e)` Table**。必须附：typeless 形参重解释的 breaking-change 分析、"仅当属性显式类型 + 形参无类型"规则、`langversion` 门控。
5. **`Custom` 关键字省略（项 5）：Consider。** 语法无歧义、风险小，但价值不足；并入"简写属性与事件"统一设计，不单独放行。
6. **`NameOf` 省略括号（项 6）：Consider。** 有 VB 一元运算符先例，但需新增优先级文法、制造两种写法；脚本化语境可回访。
7. **`NameOf` 开放泛型（项 7）：Active（VBScript.NET 语境）。** 恢复 2014-02-17 白纸黑字的 'Yes'，与 `GetType(Dictionary(Of,))` 对称；只适用类型不适用方法；明确标注为**对出货主线行为的有意分叉**。若 ModVB 追求主线严格一致则降 Table。
8. **打包问题：拆分。** 本建议作为追踪单保留；每项独立提案/speclet，各自给状态机。我们不再对整包投一次票。

### Implication:

- 撰写/更新独立 speclet：项 1（入口点签名 + 合成包装 + 冲突检测）、项 2（默认值补全 + CallerInfo 不变式 + 接口/重写匹配错误）、项 4（推断规则 + breaking-change 分析 + `langversion` 门控）、项 7（未绑定类型名文法 + 分叉声明）。
- 与主线对表：项 2 直接跟踪主线 `Implicit-default-optional-parameters` 原型；项 4 跟踪 #197。
- 与"简写属性与事件"团队对表：项 4/5 的事件访问器简写归属。
- 修正原文本：`End Sub` → `End Function`；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）替换为真实链接或删除。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：项 4——typeless `Set(value)` 在宽松模式下的既有代码占比没有数据；若破坏面被证显著，"推断仅当形参名是 `value`"的窄化规则是否足够。
- `OPEN QUESTIONS`：项 4——`RaiseEvent(sender, e)` 的委托签名推导与"惯例 vs 强制"的边界，待"简写属性与事件"建议定稿后回访。
- `OPEN QUESTIONS`：项 2——接口声明 `= 5` 而实现推断 `= Nothing` 的报错文案设计；是否需要把推断默认值"等于接口默认值"才接受省略。
- `OPEN QUESTIONS`：项 7——未绑定泛型方法的 `NameOf` 明确否；未绑定泛型**类型**的 `NameOf` 是否需要 `(Of ,)` 逗号计数与 `GetType` 完全一致（应一致）。
- `TODO`：项 3 保留一条低优先级回访记录（若社区数据反对否决）。
- `Follow-up`：为 2014-02-17 'Yes' / 10-15 修订 spec 的拉锯补一条 ModVB 文档注记，说明分叉理由与影响面。
- `Suspect`：项 3 依赖的"可选参数必须居尾"不变式未在本仓库材料中逐字核实（`Probably` 为现行编译器行为），实现前需由 binder 团队确认。

### 状态

- **LDM 状态**：项 1 Active / 项 2 Active / 项 3 Rejected / 项 4 Active（限定范围）/ 项 5 LDM Considering / 项 6 LDM Considering / 项 7 Active（分叉语境）。
- **三态判定**：整包不作单一判定；拆分后按上述状态机执行。**项 1/2/4 为本周可交付，项 7 紧随，项 5/6 待价，项 3 永不。**

---

## 附录：特性评价

# 建议评价报告：proposal-minor-fixes.md

## 评价对象

- 建议：proposal-minor-fixes.md — 七项杂项修复（Async Main / `Optional` 默认值推断 / `Optional` 关键字省略 / 访问器 `value` 推断 / `Custom` 省略 / `NameOf` 括号 / `NameOf` 开放泛型）
- 来源：Anthony 杂项清单；成分各不相同——项 2 直接对应主线 `Implicit-default-optional-parameters`（Anthony 参与）；项 4 对应主线 #197；项 1 借鉴 C# 7.1；项 6/7 回访 2014 GetName/nameof 会议；项 3/5 为原创延伸。建议文档**未标注任何来源**。
- 配方目标：消除入口点异步绕行、`Optional` 默认值样板、访问器类型重复、`NameOf` 冗余括号与封闭泛型限制

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。七项各有示例，多数改进明确；但证据止于书面（无 ModVB 原型），Async Main 示例不编译（`End Sub` 笔误），项 3/6/7 原文自标 "?" 未定，项 5/6 价值未量化。项 2 的 85% 数据是唯一硬证据（且来自主线提案而非本建议） | 已检查（项 2 有主线"已运行"原型） | 无原型封顶；3 项特性处于"未定案"状态使效果无法验收 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。项 2/4/5/6/7 延续低仪式/消除样板基因；项 1 借鉴 C# 但 VB 化；项 3 引入位置性隐式语义（anti-VB）；一份提案捆绑 7 个强无关能力（边界模糊红旗） | 已检查 | 项 3 与 VB 基因冲突应拆出；打包掩盖单项基因差异 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文一致；但 Async Main 示例笔误（红旗）、状态行占位链接（红旗）、3 项特性原文自标 "?" 而未决列表未给明确定夺路径、Drawbacks/Alternatives 偏薄、无兼容性/breaking-change 章节 | 已检查 | 笔误 + 占位链接 + 打包 = 三条红旗同时命中；每项边界含糊 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光/水正向（盘活既有惯用法、低仪式）、雷正向（小改动提速迭代）；风=打包使演化一致性受损（需拆分）；暗=项 3 隐蔽语义、项 4 重解释既有代码 | 已检查（预测待定） | 打包风险未自评；项 4 破坏面未识别 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。成分实际为：项 1=C# 7.1 对齐、项 2=主线提案（Anthony 参与）、项 4=主线 #197、项 6/7=2014 GetName 会议回访（2 月 'Yes' 被修订 spec 收回）、项 3/5=原创延伸——建议文档零来源标注 | 已检查 | 借鉴 C# 未提；项 7 与主线分叉未声明；项 2 与主线提案重复却未引用 |

## 设计原则对照

- **与 VB 基因：大体一致**（消除样板 #9、低仪式、可读）；项 3 偏离（位置性隐式语义，触犯 #7/#10）；项 4 触碰"永不破坏"（#1）需 `langversion` 门控；项 6/7 有"第二种做事方式"（#3）张力。
- **与主线关系：混合**——项 2 主线一致（有主线提案+原型+数据）；项 4 主线一致（#197 Approved-in-Principle）；项 1 默认跟随 C#；项 7 为对出货主线行为的有意分叉（Anthony 延伸）；项 3/5 Anthony 独立延伸。
- **破坏性变更**：项 4 潜在真破坏（宽松模式 typeless setter 重解释）；项 2 产生接口/重写默认值不匹配的新错误面；项 1/5/6/7 无。

## 总评

- **达成程度：部分达成**——七项中三项（1/2/4）价值与证据充分、两项（6/7）设计成立但有代价、一项（5）价值不足、一项（3）应否决；打包形态本身不合格。
- **LDM 三态建议**：拆分后逐项——项 1 Active / 项 2 Active / 项 3 Reject / 项 4 Active（补 breaking-change）/ 项 5 Consider / 项 6 Consider / 项 7 Active（VBScript.NET 分叉语境）。整包不通过。
- **主要问题**：① 七特性打包，单项价值与风险被捆绑；② Async Main 示例笔误 + 状态行占位链接（品质红旗）；③ 项 4 typeless 重解释的 breaking-change 未分析；④ 项 7 与主线分叉未声明；⑤ 项 2 与主线提案重复却未引用（成分标注缺失）。

## 返工建议

- **补充章节**：每项独立 proposal/speclet 后，各补 Compatibility/breaking-change；项 4 特别补"typeless 访问器重解释 + `langversion` 门控 + 破坏面数据"；项 2 补接口/重写默认值匹配与 CallerInfo 验证；项 6 补 `NameOf` 优先级文法；项 7 补"与 `GetType` 完全一致的未绑定类型名文法 + 分叉声明"。
- **补充证据**：修正 Async Main 示例为 `End Function`；状态行替换真实链接或删除占位符；为项 3/5/6 补用户请求或场景数据，否则维持否决/待价。
- **未决问题处理**：项 3 直接否决（不进入未决）；项 4 `RaiseEvent(sender, e)` Table 待"简写属性与事件"定稿；项 7 开放泛型方法明确否、类型明确是；项 6 括号形式在未定稿前保持现状。
- **设计探索**：项 2 是否直接复用主线原型（强烈建议）；项 4 与主线 #197 实现的差异表；项 7 的 `NameOf(Dictionary(Of,))` 结果与封闭泛型结果的等价性验证（应为同一字符串）。

---

## 附录：C# 生态与互操作考量

> 本附录把本次七项杂项修复逐一与 dotnet/csharplang 官方仓库的 C# 现实方向对照。本提案是**杂项修复集**，各项互不相关——不追求统一主线，**逐项判定**兼容 / 冲突 / 需桥接 / 脱节。C# 原文引用一律逐字并标注来源文件路径（`→` 后为 `..\..\csharplang` 镜像内路径）；无法核实的标 **Suspect** / **OPEN QUESTIONS**。索引来源：`..\..\csharplang-index.md`（C# interop 浓缩索引）。

### 总览对照表

| 项 | 主题 | C# 现实方向 | 对照判定 |
|----|------|------------|---------|
| 1 | `Async Function Main` 合法化 | C# 7.1 async-main（已出货，合成入口包装） | **兼容** |
| 2 | `Optional` 默认值推断 | C# 可选参数始终需显式默认值；C# 14 放宽表达式树里的可选/具名参数 | **兼容**（元数据默认值发射需桥接） |
| 3 | `Optional` 关键字省略 | C# 无 `Optional` 关键字（其模型即本场否决的 PROPOSAL C） | **脱节**（已否决，零互操作） |
| 4 | 访问器 `value` 类型推断 | C# 自 1.0 起 `value` 即为隐式类型参数 | **兼容**（破坏面需桥接） |
| 5 | `Custom` 关键字省略 | C# 事件无「自定义/普通」之分 | **脱节**（零互操作） |
| 6 | `NameOf` 省略括号 | C# `nameof` 恒带括号；C# 的演进在作用域/可引用对象，不动语法 | **脱节**（零互操作） |
| 7 | `NameOf` 开放泛型 | C# 14 候选 `unbound generic types in nameof`（LDM 原则批准） | 由「有意分叉」**转「同向」** |

### 逐项对照

#### 项 1：`Async Function Main`

**相关 C# 现实方向**：C# 7.1 已出货 async main（→ `proposals\csharp-7.1\async-main.md`）。动机逐字：「We can remove the need for this boilerplate and make it easier to get started simply by allowing Main itself to be `async` such that `await`s can be used in it.」非 `async` 入口的立场逐字：「The language / compiler will not require that the entrypoint be marked as `async`, though we expect the vast majority of uses will be marked as such.」合成入口机制逐字：「the compiler will synthesize an actual entrypoint method that calls one of these coded methods」（示例 `private static void $GeneratedMain() => Main().GetAwaiter().GetResult();`）。

**现实 vs 提案**：**兼容**。本场 PROPOSAL A 与 C# 7.1 是同一形态：允许 `Task` / `Task(Of Integer)` 返回、`Async` 可选、编译器合成同步包装；与既有 `Sub Main` 冲突报「入口点指定多次」对应 C# 侧「To avoid compatibility risks, these new signatures will only be considered as valid entrypoints if no overloads of the previous set are present.」。对 `Async Sub Main`（= C# `async void Main`）的排除与 C# 一致——C# 未选 `async void` 替代方案（「There are also concerns around encouraging usage of `async void`.」）。本项是「默认跟随 C#」的教科书案例，无需桥接。

**对 VBScript.NET 的适应建议**：直接复用 C# 的合成包装模式（`.GetAwaiter().GetResult()` 是 C# 生产模式）；VBScript.NET 若提供「脚本即入口」，async 入口对脚本化控制台/文本处理是刚需。无特殊元数据要求。

#### 项 2：`Optional` 默认值推断

**相关 C# 现实方向**：C# 可选参数自 C# 4 起一直**要求显式默认值表达式**（`int x = 0`），语言层面无「推断默认值」特性。C# 对可选参数做的小修正是另一类——C# 14 移除表达式树里「调用省略可选实参 / 使用具名实参」的报错（→ `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`，Motivation 逐字：「Errors are reported for calls in `Expression` trees when the call is missing an argument for an optional parameter, or when arguments are named.」与「The compiler restrictions should be removed if not needed.」）——即 C# 也在做「移除不必要的编译器限制」式小修正，与本项精神同构。

**现实 vs 提案**：**兼容**（元数据发射需桥接）。C# 不禁止、也无对应特性，VB 侧推断默认值是纯样板消除。互操作面在**元数据**：VB 的 `Optional` 由 `[Optional]` 属性表达；推断后形参将携带真实默认值常量，使反射与 C# 调用侧对它的消费方式与「显式写默认值」一致（COM 场景今天已靠 `[Optional]` + `Type.Missing`，本场「项 2 × COM interop」已确认推断 `Nothing` 正确）。与 C# 无冲突，但实现期需确认推断默认值确实进入元数据默认值槽位（见 OPEN QUESTIONS）。

**对 VBScript.NET 的适应建议**：默认值发射与显式写法等价；「默认安全」语义下 `Nothing`（值类型即 default）与「省略参数 = 没传」的直觉一致，利于脚本化调用方。

#### 项 3：`Optional` 关键字省略（Reject）

**相关 C# 现实方向**：C# 没有 `Optional` 关键字——可选性完全由默认值表达式表达，C# 侧无对应讨论。

**现实 vs 提案**：**脱节**（已否决，零互操作）。C# 的「无关键字」模型正是本场否决的 PROPOSAL C（会改变 `[Optional]` 发射规则）；否决意味着 VB 保留 `Optional` 作为「必需/可选段」边界标记，不向 C# 模型靠拢。关键字是纯语法，不进元数据，无互操作牵连。

#### 项 4：访问器 `value` 类型推断

**相关 C# 现实方向**：C# 属性/事件访问器从 C# 1.0 起 `value` 就是**隐式参数**，类型即属性/事件类型——C# 从未存在「无类型 value」形态，故无对应 proposal。C# 仍在演进访问器语义：C# 14 的 `field` 关键字（→ `proposals\csharp-14.0\field-keyword.md`）在访问器内引入新的隐式 `field` 参数，且被当作**需要 breaking-change-warnings 的破坏**——→ `proposals\breaking-change-warnings.md` 以它为例（逐字）：「this new `field` parameter would shadow access to any field etc. called `field` in existing property accessors, potentially altering its meaning」并伴随「a warning in C# 12 and lower」。

**现实 vs 提案**：**兼容**（破坏面需桥接）。VB `Set(value As String)` → `Set(value)` 使 VB 访问器模型向 C# 的隐式 `value` 收敛，缩小 VB/C# 语义差距。唯一真破坏面——Option Strict Off 下 typeless `Set(value)` 的重解释——在 C# 侧**无对应**（C# 从未允许 typeless value），所以不能照搬 C# 行为，只能照搬 C# 的**治理机制**（见下）。

**对 VBScript.NET 的适应建议**：破坏面治理建议走 C# breaking-change-warnings 双轨（Summary 逐字）：「Retroactively add warnings in previous language versions to help identify and fix user code that would be vulnerable to such breaks upon a language version upgrade.」即：在旧 `langversion` 对 typeless `Set(value)` 发**警告**（提示未来版本将重解释为属性类型），新版本再重解释——比单一 `langversion` 门控更平滑，与 C# 对 `field` 关键字同款治理一致。本场项 4 已要求 `langversion` 门控，附录建议扩展为「旧版本警告 + 新版本行为」双轨。

#### 项 5：`Custom` 关键字省略（Consider）

**相关 C# 现实方向**：C# 事件没有「自定义事件 / 普通事件」的分类，`add`/`remove` 访问器是语言固定部分，无对应关键字。

**现实 vs 提案**：**脱节**。纯 VB 语法减负；C# 侧无对应、无互操作影响。与 C# 生态关系弱，如实记录，不硬凑。

#### 项 6：`NameOf` 省略括号（Consider）

**相关 C# 现实方向**：C# 6 `nameof` 恒带括号；C# 对 `nameof` 的演进是**扩展作用域**（C# 11，→ `proposals\csharp-11.0\extended-nameof-scope.md`，Summary 逐字：「Allow `nameof(parameter)` inside an attribute on a method or parameter.」）与**扩展可引用对象**（C# 14 未绑定泛型，见项 7），从未动过括号。

**现实 vs 提案**：**脱节**（零互操作）。VB 免括号形式是 VB 一元运算符基因（`TypeOf ... Is` / `AddressOf` / `Not` 先例），C# 无此形态。由于 `NameOf` / `nameof` 均为编译期常量、不产生运行时操作也不进元数据，此差异对互操作完全中性；但与项 7 不同——它没有 C# 侧同向证据，维持 Consider。

#### 项 7：`NameOf` 开放泛型（Active，VBScript.NET 语境）

**相关 C# 现实方向**：**这是本场与 C# 生态关联最强的一项，且 C# 已转向**。
- 旧方向（本场「分叉」叙事的依据）：→ `meetings\2014\LDM-2014-10-15.md` 明确 `nameof(List<>)` 报错，原文注释逐字：`result error "type expected": Unbound types are not valid expressions`。这正是 2014-10-15 / 10-23 VB 修订 spec 与之对齐的 C# 出处。
- 新方向：C# 14 候选提案（→ `proposals\csharp-14.0\unbound-generic-types-in-nameof.md`），Summary 逐字：「Allows unbound generic types to be used with `nameof`, as in `nameof(List<>)` to obtain the string `"List"`, rather than having to specify an unused generic type argument in order to obtain the same string.」Motivation 逐字：「It's very odd to require something to be specified within an operand when it has no impact on the result. Notably, `typeof` does not suffer from this limitation.」该提案在 → `meetings\2024\LDM-2024-10-16.md` 获**原则批准**（逐字：「Approved in principle.」）。

**现实 vs 提案**：由「有意分叉」**转「同向」**。VB 项 7 恢复 2014-02-17 的 'Yes'，C# 14 也在做同一件事——两者都从「必须填未使用的泛型实参」的别扭中解脱（`nameof(Dictionary<,>)` 结果同样是 `"Dictionary"`）。差异要如实记录：① 语法不同——VB `NameOf(Dictionary(Of,))` vs C# `nameof(Dictionary<,>)`；② C# 14 更进一步支持未绑定类型上的成员链（`nameof(A<>.B)`），VB 项 7 只做类型名；③ 双方一致排除部分未绑定（C# 原文「Support is not included for partially unbound types, such as `Dictionary<int,>`。」）与嵌套未绑定，也一致排除「开放泛型方法」。因此项 7 不再是「对抗 C# 对齐行为的修订」——而是**与 C# 当前方向并行**。`nameof` 是编译期常量，元数据零影响，跨语言源码互读仅是写法不一致（尖括号 vs `Of ,`）。

**对 VBScript.NET 的适应建议**：C# 14 动向可作为 ModVB 文档注记的重要补充（见影响一节）；实现上仍按本场 RESOLUTION 只做类型、不做方法、与 `GetType(Dictionary(Of,))` 逗号计数一致。

### 跨项综合：对 VBScript.NET 的适应建议

1. **小修正按版本走 opt-in + breaking-change-warnings 双轨**（C# 模式，→ `proposals\breaking-change-warnings.md`）：本次七项里唯一真破坏面是项 4；建议在 `langversion` 门控之上加「旧版本警告 + 新版本行为」，与 C# 对 `field` 关键字同款治理对齐。这正回应决策文件 M8 对 ModVB 的「默认安全、按需动态」要求——让宽松模式下的既有代码先收到警告、再按新语义编译。
2. **Async Main 直接照搬 C# 7.1 合成包装**：`$GeneratedMain() => Main().GetAwaiter().GetResult()` 是 C# 生产模式，VBScript.NET 脚本入口同样适用。
3. **Optional 推断默认值的元数据发射**：确认推断值与显式 `= Nothing` 在元数据里等价（默认值常量进入参数槽位），使 C# 调用方与反射消费一致；COM 场景维持 `Nothing` → `Type.Missing` 映射。
4. **NameOf 语法扩展零运行时影响**：项 6/7 均为编译期常量，VBScript.NET 可安全携带；但跨语言源码互读需文档注明语法差异。
5. **识别新元数据的通用前提**（背景，与本次七项无直接关系，但作为脚本宿主与 .NET 生态互通的前提）：C# 未来 unsafe 演进要求 VB 编译器认识新元数据（`RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute` / `RefSafetyRules` / `allows ref struct` 等）；unsafe-evolution 原文对 VB 的表态（逐字）：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」（→ `proposals\unsafe-evolution.md`）——VB 无需 unsafe 语法，但**必须能读**这些元数据才能调用 C# 低层库。

### 对既有 RESOLUTION / 三态判定的影响

本附录**不改写**既有 RESOLUTION（项 1/2/4 Active、项 3 Reject、项 5/6 Consider、项 7 Active（分叉语境）、整包拆分）。生态视角仅作补充：

- **项 7**：RESOLUTION 第 7 条的「对出货主线行为的有意分叉」标注需要**修订语气**——C# 14 已在 `nameof` 支持未绑定泛型（LDM-2024-10-16 原则批准），VB 项 7 与 C# 同向而非对抗。建议在 ModVB 文档注记（Follow-up 那一条）补充 C# 14 动向，并将「有意分叉」调整为「与 C# 14 候选同向的先行实现」。**不影响 Active 判定，反而强化之**——不再承担「孤军分叉」的合规成本。
- **项 1**：「C# 7.1 同款」措辞得到 → `proposals\csharp-7.1\async-main.md` 直接佐证（合成包装 + async void 拒绝），判定稳固。
- **项 4**：`langversion` 门控建议扩展为 breaking-change-warnings 双轨（见适应建议 1），不影响 Active 判定本身。
- **项 3/5/6**：无生态影响，维持原判定。

### 引用纪律与 OPEN QUESTIONS

**已核实逐字引用（来源均为 `..\..\csharplang` 镜像内路径）**：
- → `proposals\csharp-7.1\async-main.md`：Summary / 合成入口 / 非 `async` 立场 / `async void` 顾虑（项 1 所引全部句子）。
- → `proposals\csharp-14.0\unbound-generic-types-in-nameof.md`：Summary 与 Motivation（「Allows unbound generic types...」/「It's very odd to require something to be specified within an operand when it has no impact on the result.」）。
- → `meetings\2024\LDM-2024-10-16.md`：「Approved in principle.」（Unbound generic types in `nameof` 一节结论）。
- → `meetings\2014\LDM-2014-10-15.md`：`nameof(List<>)` 示例注释 `result error "type expected": Unbound types are not valid expressions`。
- → `proposals\breaking-change-warnings.md`：Summary（「Allow very limited breaking changes in C# when this enables significantly simpler feature designs...」/「Retroactively add warnings in previous language versions...」）与 `field` 遮蔽示例。
- → `proposals\csharp-11.0\extended-nameof-scope.md`：Summary（「Allow `nameof(parameter)` inside an attribute on a method or parameter.」）。
- → `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`：Motivation（「Errors are reported for calls in `Expression` trees...」/「The compiler restrictions should be removed if not needed.」）。
- → `proposals\unsafe-evolution.md`：VB 小节（requires-unsafe 与 VB）——来自索引第四节预核实清单，已在本仓库逐字复核。

**OPEN QUESTIONS / Suspect**：
- **Suspect**：C# 14 `unbound generic types in nameof` 的**最终出货状态**——proposal 已入 `proposals\csharp-14.0\` 且 LDM-2024-10-16 原则批准，但索引版本历史（C# 14.0 = first-class Span + extensions）未列此项，本附录不能确认其是否已在 C# 14.0 出货；需在 dotnet/csharplang 与 roslyn 仓库核实。
- **OPEN QUESTIONS**：unbound-nameof 提案文件 Champion 记为 #8662，而 LDM-2024-10-16 记为 #8480——编号不一致，未深究（不影响结论）。
- **Suspect**：项 2 的元数据细节——「VB `Optional` 无默认值时仅发射 `[Optional]`、推断后发射默认值常量」属 ECMA-335 / 编译器实现层面，本附录未逐字核实；建议实现期由 binder/metadata 团队确认。
- **OPEN QUESTIONS**：项 4 typeless `Set(value)` 在宽松模式的既有代码占比无数据（沿用正文 OPEN QUESTIONS）；若破坏面显著，是否需要「仅当形参名是 `value`」窄化规则（沿用正文）。
