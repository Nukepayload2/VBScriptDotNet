# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。我们进入 Anthony 第 10 章「Dynamic Programming Enhancements」——`Any` 伪类型、无类型声明的默认类型、`Default` 方法、`Async Sub` 属于同一片实现面。本场会议只讨论 `proposal-typeless-declarations.md`；但它的地基 `proposal-any-pseudotype.md`（`Any` 伪类型）自己的 LDM 还没开，我们必须在一个"地基未验收"的前提下讨论这座楼。这个事实从会议第一分钟就悬在桌上，后面几乎每个追问都被它拉回。

## Agenda

* [Proposal: 无类型声明的默认类型（Default Type for Typeless Declarations）](#proposal-无类型声明的默认类型)

## Proposal: 无类型声明的默认类型

_Related: [2017.08.23 `Dynamic` 伪类型讨论](../../vblang/meetings/2017/vbldm-notes-2017.08.23.md)（#135/#136/#137/#43/#106）· [2015.01.14 "Object Assumed"](../../vblang/meetings/2015/LDM-2015-01-14-VB.md) · [2018.02.07 块级 Option 评价](../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（#117/#255）· [2018.05.30 "quiet customers" / #167 Return?](../../vblang/meetings/2018/vbldm-notes-2018.05.30.md) · [2018.02.28 可空值类型相等 "quirks in a good way"](../../vblang/meetings/2018/vbldm-notes-2018.02.28.md) · ModVB [proposal-any-pseudotype.md](../proposals/proposal-any-pseudotype.md)（地基，未过 LDM）· [proposal-async-sub.md](../proposals/proposal-async-sub.md)（`Task(Of Any)` 依赖）· AnthonyDesign section 10「Dynamic Programming Enhancements」_

### 场景与缺口

建议想改的对象是 VB 里一块二十五年的既成契约：**无类型声明默认 `Object`**。省略 `As` 子句的形参、返回类型、属性与字段，一直是合法的，类型一律落到 `Object`。建议把它改成默认 `Any`（动态分发），并给出等价展开：

```vb
' New default type for "typeless" declarations.
' Great for RAD/prototypes with gradual typing.
Function Add(left, right)
'Function Add(left As Any, right As Any) As Any

Sub New(ParamArray args())

Property Description

Async Function Fetch(objectId)

Private State
```

我们承认缺口是真实的，而且它确实带 VBScript 血统：在 VBScript/VB6 里，无类型即 `Variant`，天生动态。对一门想要承接脚本迁移的语言，`Function Add(left, right)` 之后直接 `Add(1, 2).ToString()` 不用强转，是很自然的直觉。今天的 VB 在这条路上卡了两道：

```vb
' 今天，Option Strict On：
Function Add(left, right)
    Return left + right     ' 错误：left/right 是 Object，+ 无法静态解析。
End Function

Dim sum = Add(1, 2)
Dim len = sum.Length        ' 错误：Object 上没有 Length。
```

`Option Strict On` 下省略类型几乎不可用；`Option Strict Off` 下又能用，但那是把整个文件都拖进宽松模式的钝器。

但讨论一开始，我们就把建议拆成两个**可分离**的命题，正如可空性会议把"机制"与"语法面"分开一样：

1. **`Any` 伪类型是否应该存在**（`proposal-any-pseudotype.md` 的事，本场不裁决）；
2. **无类型声明是否应该默认 `Any`**（本场的事）。

本建议完全寄生在命题 1 上——`Any` 不存在，"默认 `Any`"就没有默认对象。而命题 1 有一个主线留下的悬案：2017.08.23 讨论 `Dynamic` 伪类型（#136）时，记录逐字写道：*"Feels like it could be a light-weight solution."* 以及 *"Q: Is it just a safe alias for `Object` or is it more like C# `dynamic` in that it always late-binds even if a static member is available."* 这个问题主线没有回答就搁置了。我们的默认类型争论，恰好就是这个问题换了件外套。

### 候选方案

**PROPOSAL A — 全局默认 `Any`。** 按建议原文落地：所有无类型声明（形参、返回、属性、字段、`ParamArray`、异步返回）默认 `Any`。注释给出了等价展开 `Function Add(left As Any, right As Any) As Any`——本质是一条**省略规则**（elision rule）："省略 `As` = 动态"。

**PROPOSAL B — 维持 `Object` 现状。** 无类型默认仍为编译期检查的 `Object`；需要宽松绑定时用 `Option Strict Off` + 既有晚期绑定，需要单变量动态时显式写 `As Any`（若命题 1 落地）。这是 25 年契约的延续，也是"什么都不做"的代价方。

**PROPOSAL C — 可配置默认。** 新增一个 `Option`/项目开关（如 `Option Typeless Any` 或 langversion 式门控），让无类型默认在 `Any` 与 `Object` 之间选择。用选项而非硬编码来承载默认类型。

**PROPOSAL D — 作用域化默认（讨论中浮现）。** 不改变 VB.NET 的全局默认；只在**显式的脚本兼容模式**（如 `Option Explicit Off` + 模式开关，或 `proposal-embedded-vb-mode.md` 的嵌入模式）内，无类型声明默认 `Any`。VBScript.NET 的产品保真需求在这个模式里兑现，VB.NET 语义完全不动。

### 权衡：Q&A

- **A vs B：`Any` 比 `Object` + `Option Strict Off` 到底多给什么？** 这是整场最尖锐的问题，也是 2017.08.23 悬案的现世报。在 `Option Strict Off` 下，`Object` 上的成员访问**本来就晚期绑定**，`Add(1, 2).Length` 今天就能编译：

  ```vb
  Option Strict Off
  Function Add(left, right)
      Return left + right     ' 今天就已经晚期绑定。
  End Function
  Dim len = Add(1, 2).Length  ' 今天就能编译。
  ```

  把默认从 `Object` 换成 `Any`，在宽松路径下唯一的变化是调用点机制：从 `Microsoft.VisualBasic.CompilerServices.LateBinding` 换成 DLR 动态调用点。**净新增能力约等于零**，换来的是运行期错误时机与错误文案的整套迁移。这让我们对 A 的核心价值主张打了大问号。
- **A vs C：加一个 `Option` 值不值？** 2018.02.07 我们逐字评估过四个 `Option`：*"Option Strict: Would provide value, possible to do / Option Explicit: Not seeing value, difficult to do / Option Compare: Not seeing high value, could create confusing code / Option Infer: This doesn't really make sense to us"*。默认类型的可配置确实"possible to do"，但新增一个 Option 会把"无类型默认是什么"这一问从编译期规则变成**配置状态**，所有跨文件行为分析都要背一份。而且 2017.08.23 对 `Dynamic` 类型与块级 `Option` 的态度已经有倾向：*"Between this and a `Dynamic` type, I like this better."* ——主线更喜欢"作用域化的 Option"而非"动态类型"本身。C 是"作用域化"的正解方向，但应以文件/模式为单位，而不是再叠一个四态开关。
- **A 分裂成 A1/A2：Option Strict On 下怎么办？** 这是让我们最终放弃 A 的那一刀。`Option Strict On` 的契约就是"禁止晚期绑定、禁止隐式窄化"。若无类型默认 `Any`，那么一个写 `Option Strict On` 的文件里 `Function Add(left, right)` 会**静默变成动态代码**——这直接架空 `Option Strict On`。没有中间态：要么严格模式下禁止无类型默认 `Any`（报错或回退 `Object`），要么 `Any` 无视 `Option Strict On`（那 `Option Strict On` 就成了摆设）。A1（严格下报错）等于把这特性关在唯一会有人省略类型的严格用户门外；A2（严格下也动态）等于宣布 `Option Strict On` 是一张废纸。两条路都不通。
- **"渐进类型化"的主张。** 建议说"原型阶段不加类型直接用，之后补上 `As SomeType` 即可"。我们对照 C# `var`：`var` 的省略换来的是**最具体的静态推断**，方向是"越省略越安全、越精确"；本建议的省略换来的是**最动态的默认**，方向完全相反。`Function Add(left, right)` 看起来像"类型待定"，实际是"最不安全的那个类型"。对新手，"我省了个类型"得到"最动态的行为"，是陷阱不是礼包。`Suspect`：建议把"渐进类型化"与 `var` 式推断混为一谈了，概念方向相反。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

本建议**自身不引入新语法**——省略 `As` 的声明语法早已存在，它改的是 binder 的默认类型。语法面风险低。真正的歧义在 `Any` 这个标识符上：一旦 `Any` 成为伪类型，`Dim x As Any` 与用户自定义类型 `Any`、`System.Linq.Enumerable.Any` 扩展方法、`myList.Any()` 调用是否冲突？`List(Of Any)` 泛型实参位置是否允许？C# 的 `dynamic` 在泛型实参上有一整套专门规则（多数位置退化为 `object`），`Any` 要照抄还是另定？这些属于命题 1 的账，但本建议默认 `Any` 会把 `Any` 的使用面从"显式写才出现"扩大到"每个无类型声明都出现"，冲突概率随之放大。

#### 2. 角案例与边界语义

逐条过建议列出的五类：

- **`Function Add(left, right)`**：三个位置（两个形参 + 返回）默认 `Any`。但注意——无类型形参在 `Option Strict On` 下今天就是 `Object` 且合法；`left + right` 报错是**使用处**的问题，不是声明处。改默认只改使用处行为。
- **`Sub New(ParamArray args())`**：元素类型默认 `Any` → `args As Any()`。`args(0).ToUpper()` 从报错变动态。合理，但 `ParamArray` 在元数据里要求 `ParamArrayAttribute` 与具体元素类型，`Any()` 的元数据表达（`Object()` + `DynamicAttribute`）需要 spec。
- **`Property Description`**：`Get`/`Set` 同为 `Any`。两个未决：① `Set(value)` 的 `value` 参数无类型，默认 `Any`——OK；② **带初始化器的无类型属性**，如 `Property Description = "hello"`，今天在 `Option Infer On` 下是否由初始化器推断类型？我们 `Suspect` 属性不走推断、仍为 `Object`，但建议原文没写，也未决。若初始化器能推断出 `String`，那"默认 `Any`"与"初始化器推断"两条省略路径直接打架，必须裁决哪条优先。
- **`Async Function Fetch(objectId)`**：返回类型 `Task(Of Any)`，`Await` 结果 `Any`。C# 的 `async Task<dynamic>` 合法，但 VB 的异步重写（state machine）要把 `Any` 当 `object` 传，调用点动态化——与 `proposal-async-sub.md` 的 `Async Sub` 默认类型配置叠加后，`Task(Of Any)` / `ValueTask(Of Any)` 的矩阵要一起定。
- **`Private State`**：字段默认 `Any`。这是**元数据暴露面最大**的一类——字段是公开表面，`DynamicAttribute` 会泄露到消费方；跨程序集、跨语言（C# 看到 `object`）的"动态性"传播规则必须写清。
- 建议自己列进未决的：**局部变量与 `ParamArray` 是否涵盖**。局部变量是最大雷区：`Dim x`（无 `As` 无初始值）是极高频写法，今天默认 `Object`；若随默认改为 `Any`，`Dim x` 从"编译期检查的 Object"变成"动态"，且与 `Option Infer` 的推断规则纠缠（`Dim x = 5` 有初始值走推断，`Dim x` 无初始值走默认——默认一改，`Dim x` 就动态了）。还有 `Optional p`（无类型无默认值的可选参数）：`Any` 参数的默认值在编译期如何表达？

#### 3. 作用域与绑定

语义模型对无类型声明应返回 `Any` 伪类型符号；`Function Add(left, right)` 的形参符号类型是 `Any`，`Add(1, 2).Length` 处的 `GetTypeInfo` 返回 `Any` 且**没有绑定到任何成员**——动态调用点在运行期解析。这是与 TypeOf 流分析最要紧的交互：我们已定共享流引擎，`If TypeOf x Is Duck Then x.Quack()` 在 `x As Any` 下要不要收窄？`Any` 是一个"类型可收窄"的输入，还是收窄后直接**替换**调用点策略（从动态变静态）？收窄后成员是否从动态变早期绑定——那会改变异常时机（编译期报错 vs 运行期 `MissingMemberException`）。这是本建议必须与流引擎团队对表的条目，原文一个字没提。

#### 4. 与既有特性的交互

- **`Option Strict On` / `Off` 分叉**：见第 6 条，这是决定性的。
- **`Option Infer`**：`Dim x = 5` 走推断，不受影响；`Dim x` 无初始值走"默认类型"——默认一改就中招。两条省略路径（推断 / 默认）的边界必须画清。
- **`Option Explicit Off` 隐式声明**：今天，`Option Explicit Off` 下未声明的变量隐式声明为 `Object`。若无类型默认 `Any`，未声明的变量是跟着变 `Any` 还是留在 `Object`？建议 Drawbacks 自己承认了与 `Option Explicit Off` 的分叉，但没给答案。两处"没写类型"（省略 `As` vs 压根没声明）给出两个不同的默认，是把同一直觉劈成两半。
- **重载解析**：`Any` 实参参与重载解析的方式必须 spec。C# 的 `dynamic` 实参把重载决议推迟到运行期。今天的 `Object` 实参在 `Option Strict On` 下是静态决议：

  ```vb
  Sub Report(x As Object)
  End Sub
  Sub Report(x As String)
  End Sub

  Function Pick(flag As Boolean)
      If flag Then Return "text" Else Return 42
  End Function

  Report(Pick(True))   ' 今天（返回 Object）：静态绑到 Report(Object)。
                       ' 默认 Any 后：重载决议推迟到运行期 —— 重编译后行为变化。
  ```

  这是"隐蔽语义变化"的教科书案例，见第 5 条。
- **表达式树 / LINQ**：`dynamic` 在 C# 里**不能进表达式树**。VB 的查询理解构建表达式树；若无类型返回默认 `Any`，`From item In GetItems() Select item.Name` 的范围变量是 `Any`，查询还能编译吗？`Suspect`：这是实现面会撞墙的位置，建议没提。可空会议里那句 "quirks in a good way"（2018.02.28，讲可空值类型相等）提醒我们，VB 的查询/运算符有自己的一套运行时语义，`Any` 默认会与这套语义在查询层短兵相接。
- **`TypeOf ... Is` / `Is Nothing` / `TryCast`**：`TypeOf x Is T` 在 x 为 `Any` 时是否合法？C# 的 `dynamic` 不能用于 `typeof`/`is` 的某些位置；VB 对 `Object` 用 `TypeOf` 合法。`Any` 必须继承其中一套。
- **`CallByName` / 反射 / COM**：不受影响；`Any` 反而更贴近 COM `IDispatch`。
- **`.Value` 与可空**：`Any` 装值类型（`x = 5`）时是否像 `object` 一样装箱？动态算术（`x + 1`）走 DLR 还是 VB 运行库的 `Operators.AddObject`？与可空值类型的交互（`Any` 上的 `Is Nothing`）未写。

#### 5. Breaking change 与兼容性

这是与 Return? 教训（2018.05.30 #167：*"Control flow would be altered by a very subtle character."*）正面相遇的地方。省略 `As` 是一个**极微小的字符差异**，本建议让这个微小差异改变整个绑定策略——正是我们最警惕的那一类。

- **重编译行为变化**：上面的 `Report(Pick(True))` 一例，同一份源码从静态决议变成运行期决议。任何今天依赖"无类型 = `Object` 静态检查"的代码，重编译后行为都可能漂移。虽然漂移大多无害，但我们"几乎从不做破坏性变更"。
- **元数据表面**：`Public Function Lookup(id)` 今天生成 `Object` 参数与返回；默认 `Any` 后生成 `Object` + `DynamicAttribute` + 动态调用点。C# 消费者两种都看到 `object Lookup(object id)`，但"动态性"通过 `DynamicAttribute` 泄露——这等于把**内部绑定策略写进了公开表面**。库作者没有机会逐个选择，是整个程序集的省略位置集体改弦。
- **"Object Assumed" 先例**：2015.01.14 我们处理数组字面量无主导类型时，编译器在 `Option Strict Off` 下给出 *"Object Assumed"*、`On` 下报错。也就是说，"推断到 `Object`"今天是一条**有警告的路径**，用户看得到它发生了。把 `Object` 换成 `Any` 默认，等于把这条有警告的路径换成一条**无警告、更动态**的路径——警告的可见性还倒退一步。
- **迁移报告**：任何推进都必须给"运行期行为变化 / 绑定策略变化 / 元数据表面变化"三张矩阵。建议没有。

#### 6. Option Strict / 编译选项分叉

我们在 Q&A 里已经把它定为 A 的死刑。再钉一遍：`Option Strict On` 的契约就是"没有晚期绑定"。无类型默认 `Any` 在严格文件里要么被禁（A1），要么架空契约（A2）。且注意一个反讽——**会省略类型的用户，恰恰大多是 `Option Strict On` 的沉默用户**（他们省略是因为认为省略不改变安全性）；把他们的省略变成动态，正是对这群最安静的用户最重的背叛。而 `Option Strict Off` 的用户今天已经拿到晚期绑定，`Any` 默认对他们只是调用点机制替换。**A 在"该生效的地方失效，能生效的地方冗余"。**

#### 7. IDE / IntelliSense

VB 用户把 IntelliSense 当氧气。省略类型是高频动作；默认 `Any` 后，`Add(1, 2).` 补全弹不出成员（C# `dynamic` 的体验：无补全、无波浪线、闪电图标）。让**最高频的省略**变成**补全黑洞**，对"低仪式、可发现"的定位是倒行逆施。2017.10.18 讨论 JSON 时我们说过：*"This is one end of the spectrum of providing a better tooling experience for untyped data over the wire."*——给无类型数据提供更好的工具体验是正当目标，但把无类型**默认**变动态，是把工具体验的反面推给所有人。需要完整的 IDE 计划（补全、悬停、签名帮助在 `Any` 上长什么样），建议没有。

#### 8. 数据 / 普遍性

建议没有给任何量化数据：无类型声明在真实 VB.NET 业务代码中的占比、用户请求数、VBScript 迁移者想要"省略即动态"的样本。2018.05.30 那句 *"hundreds of thousands of quiet customers ... primarily want VB to keep doing what it does now"* 在此依然压舱——我们有理由相信现代 VB 业务代码大量显式写 `As`，省略类型集中在早期脚本化代码与示例里。最有力的普遍性证据恰恰是**VBScript 血统本身**：脚本世界"无类型即动态"是数十年直觉。但这把普遍性指向的是"脚本模式"，不是"VB.NET 全局默认"。

#### 9. 更简替代

- **显式 `As Any`**（命题 1 落地后）：`Function Add(left As Any, right As Any) As Any` 覆盖"我要动态"的意图，不改变任何省略位置的默认。这是零破坏路径。
- **Analyzer**：对无类型声明发提示（"此处省略 `As` 默认 `Object`，若需动态请写 `As Any`"），给脚手架不给语义。能覆盖 80% 的发现性需求。
- **脚本模式**（PROPOSAL D）：把"省略即动态"关进一个显式进入的模式，这是把 VBScript 保真做进产品的正道，也是唯一让"渐进类型化"叙事成立的地方——原型在脚本模式里写，收敛时把文件迁出模式补类型。
- **什么都不做**：`Object` 默认 + `Option Strict Off` + 未来的显式 `Any`，三件套已经覆盖"宽松"与"动态"两个档位。缺的只有"默认档位本身是动态"这一格。

#### 10. 成本 / 优先级

把无类型默认变 `Any`，意味着**每个省略类型的位置**都成为动态调用点：编译器（binder 默认类型 + 动态调用点生成 + `DynamicAttribute` 发射）、运行库（DLR 或改造既有 `LateBinding`）、IDE（补全/悬停/签名）、重载决议（Any 实参的推迟决议）、表达式树（限制或特殊处理）、异步（`Task(Of Any)`）、流分析（`Any` 状态）。这是 C# `dynamic` 级别的工作量，但 C# 是把它做成**显式 opt-in 关键字**；本建议把它做成**隐式默认**。成本分布在没有新增能力的路径上（见 Q&A：Off 下净新增≈0），优先级应该很低。真正的优先级在命题 1（`Any` 伪类型本身）和脚本模式（命题 D），而不是全局默认。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍：动态调用点编译为 `object` + `DynamicAttribute`，C# `dynamic` 已有先例，CLR 存储规则不受影响。真正的硬约束是**运行库选择**：走 DLR（`Microsoft.CSharp`/`CallSite`）会改变与既有 VB `LateBinding` 的运行期错误文案与重试语义；走既有 `LateBinding` 则 `Any` 与 `Object` 在宽松模式下几乎没有运行期差异（又一次证实净新增≈0）。表达式树限制是编译器约束，不是 CLR 约束，但它是真实的墙。

#### 12. 值不值得做

- **PROPOSAL A（全局默认）**：价值低（Off 下冗余、On 下不自洽）、成本高（dynamic 级）、风险高（重编译行为变化 + `Option Strict` 契约架空 + IntelliSense 回归 + 表达式树撞墙）。**不值得做。**
- **PROPOSAL D（脚本模式）**：价值高（VBScript.NET 产品保真、脚本直觉兑现）、成本中等（模式内实现，不影响 VB.NET）、风险可控（门控、不碰存量）。**值得做——以模式而非默认。**
- 一句话：我们愿意为"脚本里省略即动态"买单，不为"所有 VB 省略即动态"买单。

### VB 基因对照

逐条对照设计原则：

- **原则 1「永不破坏现有代码」**：**违反**。无类型默认从 `Object` 改 `Any`，重编译后绑定策略与元数据表面集体漂移，且没有迁移报告。
- **原则 3「不引入第二种做事方式」**：**违反**。`Object` + `Option Strict Off`（或未来的显式 `Any`）已经是动态的既有途径；"省略即动态"是第二套动态机制，且是隐式的。2017.08.23 主线对 `Dynamic` 类型的态度也只是"light-weight solution"的试探，没认可它做默认。
- **原则 5「读起来像英语、对新手友好」**：`Function Add(left, right)` 读起来确实漂亮——但"看见的省略"与"得到的最动态行为"之间的落差，对新手是最不友好的那一类隐藏语义。
- **原则 7「避免隐蔽的控制流/语义变化」**：**直接踩雷**。省略一个 `As` 是比 `Return?` 的 `?` 更细微的字符，换来的是从静态决议到动态决议的整套切换。#167 的教训逐字适用：*"Control flow would be altered by a very subtle character."*
- **原则 9「消除常见样板」**：唯一得分项——它确实消掉了强转与 `As Object`。但消掉样板的方式是让默认更危险，这条得分被原则 7 全额没收。
- **原则 10「冗长只在有用时是美德」**：`As Object` 在这个语境里是**有用的冗长**——它让"我没写类型"与"我知道它是 Object"等价。抹掉这个等价，代价是隐性。
- **2.3 主线对照表**：`Any`/无类型默认不属于主线的任何一行，属于 **Anthony 独立延伸**。主线最接近的资产是 2017.08.23 的 `Dynamic` 伪类型试探（#136），未落地、留了"安全别名 or C# dynamic"的悬案。本建议把悬案的答案写死成"C# dynamic 且是默认"——比主线激进两档。VBScript 血统（无类型即 `Variant`）是真实资产，但它指向"脚本模式"，不是"全局默认"。

### 诚实分层

- **事实**：无类型声明在 VB 中一直合法，默认 `Object`（`Option Strict On` 下成员访问静态检查、无法解析即报错；`Off` 下晚期绑定）；2015.01.14 编译器对无主导类型数组字面量在 `Off` 下给 "Object Assumed"、`On` 下报错；2017.08.23 对 `Dynamic` 伪类型（#136）的评价与悬案逐字如上，且对块级 `Option`（#117）说 "Between this and a `Dynamic` type, I like this better."；2018.02.07 对四个 `Option` 的评价逐字如上；2018.05.30 对 #167 `Return?` 的否决理由逐字如上；VBScript/VB6 无类型即 `Variant`（动态）；Anthony 原文第 10 章给出本建议全部示例，proposal 未标章节号；`proposal-any-pseudotype.md` 未过 LDM（无会议纪要）。
- **Probably**：若 VBScript.NET 成为产品，脚本模式里"省略即动态"是保真需求；`Any` 若实现大概率走 C# `dynamic` 的 DLR 调用点 + `Object`/`DynamicAttribute` 元数据模型；`Option Strict On` 下让无类型默认 `Any` 只有"报错/回退"或"架空契约"两条路，没有中间态。
- **Suspect**：建议把"渐进类型化"与 `var` 式推断混写——方向相反（`var` 越省略越精确，本建议越省略越动态）；无类型属性带初始化器时是否由推断覆盖默认（原文未写，我们 `Suspect` 属性不走推断仍为 `Object`）；无类型声明在真实 VB.NET 业务代码中的占比没有数据，`Dim x` 无初始值在 `Option Infer On` 下的精确错误/警告号未核；`Any` 进表达式树是否撞墙。
- **OPEN QUESTIONS**：① `Any` 伪类型自己的 LDM 何时回答 2017.08.23 悬案——这是本建议的全部前提；② 脚本模式的开关形态（新 `Option` / langversion / 复用 `Option Explicit Off` / 嵌入 VB 模式）；③ 若做 D，字段（元数据暴露）与局部变量（推断冲突）是否排除；④ `Option Strict On` 下无类型默认的精确行为；⑤ `Async Function` 无类型返回 `Task(Of Any)` 与 `Await` 的交互细节；⑥ 表达式树中 `Any` 的处理。
- **TODO**：跟踪 `Any` 伪类型 LDM；量化无类型声明占比；若推进脚本模式，写"模式边界 speclet"。

### RESOLUTION:

1. **否决 PROPOSAL A（全局默认 `Any`）**。理由三层：`Option Strict On` 下不自洽（A1 禁用它、A2 架空契约，都不可接受）；`Option Strict Off` 下与既有晚期绑定重复、净新增能力约等于零；重编译后绑定策略与元数据表面漂移，违反"几乎从不破坏"。省略 `As` 是比 `Return?` 更细微的字符，让这个字符切换整套绑定策略，正是原则 7 与 #167 明令拒绝的。
2. **本建议的一切都挂在 `proposal-any-pseudotype.md` 上**。在 `Any` 的 LDM 回答 2017.08.23 悬案（"安全别名 for `Object`" or "C# `dynamic`"）之前，本建议**没有地基**，不得 Active。
3. **把"省略即动态"定位为模式而非默认（PROPOSAL D）**。VBScript 血统的普遍性证据指向的是脚本模式，不是全局默认。若做，以显式进入的脚本兼容模式（`Option Explicit Off` + 模式开关，或嵌入 VB 模式）承载：模式内无类型声明默认 `Any`，模式外 VB.NET 语义一字不动。这与 2017.08.23"更喜欢作用域化 Option 而非 Dynamic 类型"的主线倾向一致，也是"渐进类型化"唯一成立的叙事。
4. **若 D 被推进，v1 范围收窄**：只覆盖形参、返回类型与属性访问器；**排除字段**（元数据暴露面最大，`DynamicAttribute` 泄露公开表面）；**排除局部变量**（`Dim x` 极高频、与 `Option Infer` 纠缠）；`ParamArray` 的 `Any()` 元数据与 `Async Function` 的 `Task(Of Any)` 各自等专项 spec（与 `proposal-async-sub.md` 对表）后再进。
5. **`Option Strict On` 与无类型默认的交互写进 spec**：任何模式下，`Option Strict On` 文件里的无类型声明保持 `Object` 编译期检查——`Any` 默认只存在于明确关闭严格检查的脚本模式内。绝不让 `Any` 静默架空 `Option Strict On`。
6. **动态调用点机制复用既有 `LateBinding` 而非 DLR**（若做 D）——避免运行期错误文案与重试语义的两套迁移；与 `proposal-runtime-library.md` 对表。
7. **要求补交**：重编译行为变化 / 元数据表面 / 绑定策略三张迁移矩阵；`Any` 下的 IDE 计划（补全、悬停、签名）；表达式树与重载解析交互；与共享流引擎的交互规则（`Any` 状态下 `TypeOf` 收窄是否把动态调用点切换为早期绑定，必须回答——那会改变异常时机）。

### Implication:

- 建议状态标注 `LDM Rejected（全局默认）/ Considering（脚本模式）`；在 proposal 头部加一行 "LDM 2026-08-08"。
- 拆分出两个独立工作项：`Any` 伪类型 LDM（命题 1，含 2017.08.23 悬案裁决）；脚本模式 speclet（命题 D，模式开关形态 + v1 范围）。
- 与 `proposal-any-pseudotype.md`、`proposal-async-sub.md`、`proposal-embedded-vb-mode.md`、`proposal-runtime-library.md`、TypeOf/可空流引擎团队逐一对表。
- 未决问题移交至 OPEN QUESTIONS，不得在本建议内硬定。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Any` 伪类型 LDM 何时开、2017.08.23 悬案如何裁决。
- `OPEN QUESTIONS`：脚本模式开关的确切形态；`Option Explicit Off` 文件的"未声明变量"是否与无类型声明同轨。
- `OPEN QUESTIONS`：`Option Strict On` 下无类型声明保持 `Object` 后，脚本模式内是否能**逐文件**关闭严格检查（2018.02.07 块级 `Option` 的续章）。
- `OPEN QUESTIONS`：带初始化器的无类型属性/字段，推断与默认的优先级。
- `TODO`：量化无类型声明占比与 VBScript 迁移需求。
- `Follow-up`：`Any` 进表达式树的可行性笔记；`Any` 下 `TypeOf` 收窄与动态调用点的切换规则（与流引擎共享条目）。

### 状态

- **LDM 状态：Rejected（全局默认 PROPOSAL A）/ Considering（脚本模式 PROPOSAL D）**；整体特性 **Table**——等 `Any` 伪类型 LDM 给出地基后再整体重估。
- **三态判定：Table（拆分后 A→Reject、D→Consider）**——与 `Null` 字面量会议同构：把"机制/方向"与"落地形态"拆开，留下可独立推进的部分，否定不可接受的部分。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-typeless-declarations.md` — 无类型声明（形参/返回/属性/字段/`ParamArray`/异步返回）默认 `Any` 动态类型，替代编译期检查的 `Object`。
- 来源：Anthony 原文第 10 章「Dynamic Programming Enhancements」（第二个代码块起，未标章节号）；隐性继承 VBScript/VB6"无类型即 `Variant`"血统（未声明）；隐性借鉴 C# `dynamic` 模型（未声明）；依赖 `proposal-any-pseudotype.md`（未在状态/依赖中显式声明）。
- 配方目标：RAD/原型省略类型即可自由使用成员，随后补 `As SomeType` 实现渐进类型化。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。目标（RAD 免强转）说清了，但**净新增改进不可衡量**：`Option Strict Off` 下今天已能晚期绑定，`On` 下该特性不自洽（要么被禁要么架空契约）。建议的示例（`Add(1,2).Length`）在 Off 下今天就能编译，不构成特性演示；"渐进类型化"主张与 `var` 方向相反。 | 已检查 | 主效果在 Off 下已存在、On 下不可成立；无原型（状态行占位链接）；无数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。把 `As` 省略（VB 低仪式语法）与 `Any` 动态默认（C# `dynamic` / VB6 `Variant` 移植）缝合；省略语法是 VB 基因，但"省略 = 最动态"是外来默认。打包了 `Any` 依赖与 `Async`/`ParamArray` 一揽子未定交互。 | 已检查 | 外来默认未 VB 化到可落地；混入未验收依赖 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文一致、5 个未决问题具体；但缺文法/spec、缺兼容性/breaking-change 分析、缺 `Option Strict` 分叉、缺交互（重载/表达式树/流分析/`Option Explicit Off`）、状态行占位链接。"渐进类型化"机制未展开。 | 已检查 | 边界关键点（严格/宽松分叉）缺失；兼容性未分析 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。光/水（盘活 VBScript 血统、脚本直觉）有正向，但暗（重编译行为与元数据表面漂移、`Option Strict` 契约被架空、IntelliSense 补全黑洞）突出且无对冲设计；风（依赖未验收的 `Any`）断裂。文档 Drawbacks 承认了兼容分叉但无应对。 | 已检查（预测待定） | 破坏性无迁移矩阵；与主线（2017.08.23 试探、2018.02.07 Option 立场）一致性断裂 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。零来源标注（Anthony 章节、VBScript 血统、C# `dynamic` 均未声明）；影响预估（"适合 RAD、渐进类型化"）低估了绑定策略切换与元数据泄露；依赖 `Any` 伪类型未在依赖中显式声明；混入 `Async`/`ParamArray` 未定交互。 | 已检查 | 成分零标注；净新增能力被高估 |

### 设计原则对照

- **与 VB 基因：偏离**。违反原则 1（破坏既有代码）、原则 3（第二套动态机制，且为隐式）、原则 7（省略 `As` 的细微字符切换绑定策略——#167 `Return?` 同类）；原则 9 得分被 7 没收。VBScript 血统对齐只在"脚本模式"语境成立。
- **与主线关系：Anthony 独立延伸，与主线保守立场偏离**。主线对 `Dynamic` 伪类型只有 2017.08.23 的浅探（#136，"Feels like it could be a light-weight solution."）并留悬案，且明确更偏好作用域化 `Option`；主线对"无类型默认 `Object`"是 25 年契约。本建议把悬案答案写死为"C# dynamic 且默认"，比主线激进两档。
- **破坏性变更：有**。重编译后绑定策略（静态→动态）与元数据表面（`DynamicAttribute` 泄露）变化；`Option Strict On` 契约被架空的风险；`Option Explicit Off` 未声明变量与无类型声明分叉。文档无迁移矩阵、无 `langversion`/模式门控。

### 总评

- **达成程度：未达成（作为 VB.NET 全局默认）**。缺口真实、方向可辨，但"全局默认"这个落地形态既不自洽（On）也不增值（Off），且破坏面大。
- **LDM 三态建议：Table（拆分后 A→Reject、D→Consider）**——先等 `Any` 伪类型 LDM（命题 1）回答 2017.08.23 悬案；把"省略即动态"收进显式脚本兼容模式（命题 D）再谈推进；全局默认不进入 Active。
- **主要问题**：① `Option Strict On` 下不自洽、`Off` 下冗余，净新增≈0；② 重编译破坏与元数据表面泄露无迁移矩阵；③ 依赖未验收的 `Any` 伪类型而未声明；④ 与 `Option Explicit Off`/`Option Infer`/重载解析/表达式树/流分析的交互全部缺失；⑤ IntelliSense 补全黑洞与"低仪式"定位冲突；⑥ "渐进类型化"与 `var` 概念方向相反。

### 返工建议

- **补充章节**：`Option Strict`/`Option Explicit`/`Option Infer` 三向分叉专节（严格文件下的精确行为：报错/回退/警告三选一并给理由）；兼容性/breaking-change 矩阵（重编译行为变化、元数据表面、跨语言消费者、`langversion`/模式门控）；依赖声明（`Any` 伪类型、`Async Sub`、流引擎）；`ParamArray`/属性初始化器/局部变量的逐一规则。
- **补充证据**：无类型声明在真实 VB.NET 业务代码中的占比；VBScript 迁移者对"省略即动态"的需求样本；`Any` 进表达式树的可行性笔记；DLR vs 既有 `LateBinding` 的运行期行为差异表。
- **未决问题处理**：把"渐进类型化"改写为"脚本模式内原型、收敛时补类型并迁出模式"的明确两步叙事；`Option Strict On` 下无类型保持 `Object` 定为 spec 硬规则；`Any` 伪类型 LDM 前不承诺任何默认行为。
- **设计探索**：脚本模式开关形态（复用 `Option Explicit Off` / 新 `Option` / 嵌入 VB 模式 / langversion 门控）的对比；`Any` 在共享流引擎中的状态表示（收窄后是否把动态调用点切换为早期绑定）。

---

## 附录：C# 生态与互操作考量

> 立场声明：C# 是 CLR 新特性与 .NET 生态的主要推动者（索引 T1）；VB LDM 主线已退化为「只做与 C# 兼容」，Anthony 主张 VB 保持特色（决策文件通用背景）。本提案（无类型声明默认 `Any`）与 C# 的**隐式类型声明**（`var`，C# 3）、**`object` / `dynamic`**（C# 4，索引 T7/M2）、**NRT**（C# 8）直接相关。本附录只追加「C# 现实方向 vs 本提案响应」的对应与对 VBScript.NET 的适应建议，**不改写正文 RESOLUTION**。引用均来自 `..\..\csharplang` 镜像，标注到文件；无法核实的列入末尾 `OPEN QUESTIONS`。

### 相关 C# 现实方向

1. **C# 的「省略」方向是更具体，不是更动态。** C# 3 的 `var`（隐式类型局部变量）是 C# 唯一「省略类型」的语言特性（版本史条目见 `Language-Version-History.md` 的 C# 3 节「Implicitly typed local variables」）。它对 `var` 的硬约束是**必须有初始化式**、类型从初始化表达式**推断**且推断出**最具体**的静态类型；`var x;`（无初始化式）与 `var x = null;`（推断不出类型）都是编译错误。NRT（C# 8）下推断还带上可空注解：
   - 原文：「`var` infers an annotated type for reference types. For instance, in `var s = "";` the `var` is inferred as `string?`.」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（「nullable implicitly typed local variables」节）。
   - 原文：「The type inferred for local variables declared with `var` is informed by the null state of the initializing expression.」→ 同文件（「Type inference for `var`」节）。
   - 含义：C# 里「写不出类型」不是「默认动态」，而是「由初始化式推导，推导失败即报错」。这与本提案「省略 = 默认 `Any`（最动态）」**方向相反**，正文 Q&A 已用 `var` 对比钉死这一条（「Suspect」），C# 现实不改变该结论。
2. **C# 的「动态」是显式 opt-in 关键字，不是默认；`object` 与 `dynamic` 底层几乎等价。** C# 4 引入 `dynamic`（版本史 C# 4 节「Dynamic binding」）。在 C# 类型系统里二者 identity-convertible，动态性只是 `DynamicAttribute` 注解层，差别全在**调用点绑定策略**：
   - 原文：「*Merge*(`object`, `dynamic`) = *Merge*(`dynamic`, `object`) = `dynamic`」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（「Fixing」节）。
   - 含义：印证正文第 11 条的元数据判断——`Any` = `Object` + `DynamicAttribute` + 动态调用点，与 C# `dynamic` 的 CLR 表达**完全同构**，无新 CLR/元数据要求。C# 把这份「动态性」做成显式关键字，本提案（A）把它做成隐式默认，正是二者差异所在。
3. **dynamic / 晚期绑定被 AOT/trimming 边缘化，unsafe-evolution 甚至质疑其安全性。**（索引 T7）
   - 原文（open question，未决议）：「`dynamic` (probably should match what BCL decides for reflection APIs)」→ `proposals\unsafe-evolution.md`（「Should more constructs be `unsafe`?」节）。
   - 动态/晚期绑定 = 运行期反射，与 NativeAOT/trimming 张力最大（决策文件 M2/M8）；C# 靠类型系统 + source-gen 取代之（索引 T7）。
4. **表达式树 / 查询是动态值的已知禁区。** C# 表达式树自 C# 4 起不支持 dynamic 调用（既定行为，spec 见 `spec\expressions.md` 的 §11.3.3 dynamic binding 链接索引）；C# 14 又给表达式树加了一层限制（ref struct 无法被解释器支持）：
   - 原文：「Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine:」→ `proposals\csharp-14.0\first-class-span-types.md`（「Expression trees」节）。
   - 含义：VB 查询理解构建表达式树（正文第 4 条），与正文 OPEN QUESTION ⑥（`Any` 进表达式树）直接对撞；C# 有同构先例。
5. **COM 语言层投入少，转向 source-gen。**（索引 T4）C# 语言层对 COM 新增投入很少，重心转向 AOT 友好的 source-gen 互操作（`[LibraryImport]`、COM source generators 属 dotnet/runtime 生态）；`dynamic`/晚期绑定面向 COM `IDispatch` 仍是 VB 传统强项，但 C# 侧没有对应演进可参照。

### 现实 vs 提案

| 本提案要素 | C# 现实对应 | 判定 |
|---|---|---|
| 省略 `As` 声明（形参/返回/属性/字段）默认 `Any`（动态） | C# 无「省略类型」；`var` 必须有初始化式、推断最具体静态类型、无类型即错误 | **方向冲突（概念相反）**：C# 的省略 = 越省越精确；本提案的省略 = 越省越动态。正文 Q&A「渐进类型化」一条的 `Suspect` 在 C# 视角下成立且被强化 |
| 默认类型 Any（动态分发） | C# 无「默认动态类型」；`dynamic` 是显式 opt-in 关键字，`object` 才是通用默认；二者 identity-convertible | **需桥接（VB 特色）**：「动态即默认」不是 C# 路线，C# 也无此先例；VBScript 血统的「无类型即 `Variant`」只能落在 VB 自己的脚本模式（正文 PROPOSAL D），C# 无法背书 |
| `Option Strict On` 下无类型声明保持 `Object`（RESOLUTION #5） | C# `object` 是引用类型通用默认，成员访问静态检查 | **兼容**：与 C# `object` 的静态定位一致，零冲突 |
| 动态调用点（`Any` 实参的重载决议推迟到运行期） | C# `dynamic` 实参同样把重载决议推迟到运行期（既定行为） | **兼容（机制对齐）**：正文第 4 条的重载推迟与 C# `dynamic` 行为一致；C# 消费方看到的都是 `object` + `DynamicAttribute` |
| `Any` 进表达式树 / LINQ 查询（正文 OPEN QUESTION ⑥） | C# 表达式树不支持 dynamic 调用；C# 14 又限制 ref struct 进解释器 | **撞墙（双方一致）**：VB 查询理解若默认 `Any`，撞上与 C# 相同的墙——是编译期限制，不是 CLR 限制 |
| 元数据表面（`Object()` + `DynamicAttribute` 泄露公开表面） | C# `dynamic` 在公共签名同样经 `DynamicAttribute` 把动态性写进元数据 | **兼容（共同负担）**：现象同构；差异是 C# 为显式 opt-in、本提案（A）为默认——放大泄露面 |
| 面向 COM `IDispatch` 的晚绑定 | C# 对 COM 语言层投入少，转向 source-gen | **兼容但 C# 无演进**：VB 保留晚绑定面向 COM/Office 是差异化；C# 没有「更安全的 COM 动态」可学 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态（决策文件 M2 主线）。** C# 现实证明「动态默认」与 AOT/trimming 张力最大、且动态是显式 opt-in。把「省略即动态」收进脚本模式（正文 RESOLUTION #3 PROPOSAL D），模式外 VB.NET 语义一字不动。要「省略」就给**推断**（`Option Infer` 已覆盖 `Dim x = 5`），要「动态」就显式 `As Any`——两档都落在「模式内显式进入」的边界上。
2. **`var` 的约束可反哺 VB 的 `Option Infer` / 无类型默认。** C# 的省略 = 必须有初始化式 + 推断最具体类型；VB 保持「无类型默认 `Object`」即让「省略」留在静态域，与 C# 同向。若未来要给 `Dim x`（无初始值）加警告，可对齐 C#「`var x;` 即错误」的直觉——虽然强度不必一致（VB 允许 `Dim x`）。
3. **source-gen 桥与 AOT。** 若 VBScript.NET 愿景包含 NativeAOT，`Any`/晚绑定是主要障碍（决策文件 M8）。脚本层允许动态、编译产物走类型化；把脚本模式的入口/出口做成显式边界——`As Any` 进入动态、postfix-casting `(As T)`（见 `meeting-postfix-casting.md` 附录）作为**单表达式类型化出口**，可成为 AOT 编译器静态化该点的钩子。
4. **识别新元数据。** `dynamic` 靠 `DynamicAttribute`、C# 15 的 unions/closed hierarchies 靠 `[CompilerFeatureRequired]`。VBScript.NET 的 VB 编译器必须能识别 `DynamicAttribute`（`Any` 的元数据表达，正文第 11 条）与新特性标志，才能跨语言消费/被消费——这是决策文件 M8 的硬桥接点，与本提案是否 Active 无关。
5. **调用点机制复用既有 `LateBinding`（正文 RESOLUTION #6）与 C# 无冲突。** C# 的 `dynamic` 走 DLR；VB 走 `Microsoft.VisualBasic.CompilerServices.LateBinding`。跨语言时 C# 消费方看到的元数据一致（`object` + `DynamicAttribute`），运行期行为差异封装在 VB 侧，不产生互操作债务。

### 对既有 RESOLUTION / 三态判定的影响

- **C# 现实强化「Reject（全局默认）/ Consider（脚本模式）」的判定，不推翻。** C# 对「省略」的回答是 `var`（推断、必须初始化、最具体）；对「动态」的回答是显式 opt-in 的 `dynamic`（且被 AOT/trimming 边缘化）。C# 没有任何「省略即默认动态」的先例或演进迹象——正文 RESOLUTION #1（否决全局默认）与 C# 方向**完全一致**；RESOLUTION #3（脚本模式承载「省略即动态」）是 VB 差异化，C# 无对应，但也不冲突。
- **「渐进类型化」叙事在 C# 视角下只有一种成立方式。** C# 的 `var` 是「从具体到更具体」；VBScript 的「省略即动态」若要渐进，必须是正文 RESOLUTION #3 的两步叙事——脚本模式内原型、收敛时补类型迁出模式。C# 现实不提供「动态默认」的渐进路线，「渐进」一词在本提案里的正当用法只有这一种。
- **表达式树撞墙是双向确认。** 正文 OPEN QUESTION ⑥（`Any` 进表达式树）在 C# 有同构先例（dynamic 不入表达式树 + ref struct 不入解释器）。若脚本模式（D）推进，v1 范围把查询/表达式树交互列为 spec 前置（RESOLUTION #4 已部分覆盖），可引用 C# 先例定边界。
- **生态侧注脚。** RESOLUTION #5（`Option Strict On` 下无类型保持 `Object`）与 C# `object` 静态定位兼容；`Any` 元数据（`Object` + `DynamicAttribute`）与 C# `dynamic` 完全同构（NRT spec `Merge` 证实 identity-convertible），无新 CLR/元数据要求——「脚本模式内做」不构成互操作债务，与 `meeting-postfix-casting.md` 附录的结论同构。

### 引用与核实状态

本附录引用的逐字原文均已核实（源文件位于 `..\..\csharplang`）：

- 「`var` infers an annotated type for reference types. For instance, in `var s = "";` the `var` is inferred as `string?`.」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（「nullable implicitly typed local variables」节）
- 「The type inferred for local variables declared with `var` is informed by the null state of the initializing expression.」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（「Type inference for `var`」节）
- 「*Merge*(`object`, `dynamic`) = *Merge*(`dynamic`, `object`) = `dynamic`」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（「Fixing」节）
- 「`dynamic` (probably should match what BCL decides for reflection APIs)」→ `proposals\unsafe-evolution.md`（「Should more constructs be `unsafe`?」节，open question）
- 「Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine:」→ `proposals\csharp-14.0\first-class-span-types.md`（「Expression trees」节）
- C# 3「Implicitly typed local variables」、C# 4「Dynamic binding」→ `Language-Version-History.md`（C# 3 / C# 4 节，标题级引用）

`OPEN QUESTIONS`：

- **`var` 必须初始化、`var x = null;` 非法**的精确规格原文（ECMA-334 局部变量声明节）位于 dotnet/csharpstandard，本镜像 `spec\` 目录只是链接索引（`spec\types.md`、`spec\expressions.md`），**无法在本镜像逐字核实**。本附录将其作为 C# 既定行为描述性引用，标注待外部核实。
- **C# 表达式树不支持 dynamic 调用**的规格原文（§11.3.3 dynamic binding）同理位于 csharpstandard，本镜像无正文；此处以 first-class-span-types 对表达式树限制的逐字原文 + spec 链接索引作旁证，未逐字核实。
- COM source generator 与语言层协作细节（属 dotnet/runtime，本镜像无正文，索引已列为 OPEN QUESTIONS）。
