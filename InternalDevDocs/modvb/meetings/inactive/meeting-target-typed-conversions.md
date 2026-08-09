# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-target-typed-conversions.md`（Anthony 原文第 18 章 18.1 "Target-typed conversions?"，"Needs more bake time"）。按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期"，且"It is perfectly fine for work to happen on inactive or rejected proposals, and for them to be resurrected later."。所以今天会议的真正问题是：**这个想法值得激活、继续搁置，还是把它拆开、留下能用的部分？** 我们开场就知道这不会是一场轻松通过的会议——建议自己在原文里标注了 `' I think this will cause fist fights.`，而我们恰好把"打架"当作设计审慎的信号，而不是魅力。

## Agenda

* [Proposal: 目标类型化转换（Target-Typed Conversions）](#proposal-目标类型化转换target-typed-conversions)

## Proposal: 目标类型化转换（Target-Typed Conversions）

_Related: [vblang #59 – New conversion operator/syntax（`As Type`/`CVal`）](https://github.com/dotnet/vblang/issues/59)；[vblang #46 – Fix the ternary If operator](https://github.com/dotnet/vblang/issues/46)；[vblang #135 – Late-binding without `Option Strict Off`](https://github.com/dotnet/vblang/issues/135)；2014-02-17 会议 #23（TryCast 可空目标）/ #43（字面量非目标类型化）；2014-03-12 会议（快速整型转换）；2015-01-14 会议（dominant type / "Object Assumed"）；ModVB：`meeting-postfix-casting`、`meeting-conditional-best-common-type`、`proposal-any-pseudotype`_

### 场景与缺口

We started from a scenario that belongs to a minority style but is easy to describe：有一批开发者偏好把类型写在变量声明处（"For those of us who prefer variable types on the variable."），于是 `Dim d As Duck = DirectCast(animal, Duck)` 里同一个类型 `Duck` 出现了两次——声明一次、转换再一次。建议主张：转换目标类型既然已经由声明决定，转换表达式再写一遍类型就是冗余且不一致；编译器应当**从目标类型推断转换的目标**：

```vb
' For those of us who prefer variable types on the variable.

Let obj As Object = 1S

Let i As Integer = DirectCast obj

Let v As T = Trycast expression

' I think this will cause fist fights.
M(CType(x))
```

We acknowledged the opening intuition：如果"类型集中到变量声明一处"是你的美学，那么 `DirectCast obj` 确实比 `DirectCast(obj, Integer)` 干净。但这场会我们反复回到三个事实：**第一**，这个美学本身不是主线风格（`Let` 替换 `Dim` 是 Anthony 独立延伸，主线对照表 2.3 明确标注）；**第二**，VB 已经有一个为"类型在变量上"风格准备的转换简写族（`CStr`/`CInt`/`CBool`…），本建议试图服务的 85% 场景可能已经被覆盖；**第三**，建议自己把最危险的位置（实参位 `M(CType(x))`）标为"会打架"，而这恰恰是它全部设计困难所在。三个事实合起来，让我们对"激活"持很强的保留。

### 候选方案

**PROPOSAL A — 完整目标类型化转换（建议原文）。** `DirectCast`/`TryCast`/`CType` 全部允许省略类型实参，目标类型由上下文推断：变量声明（`Let i As Integer = DirectCast obj`）、实参位（`M(CType(x))`）、以及（未明确列出但语法上不可避免的）属性赋值、`Return`、集合初始化器等一切"存在目标类型"的位置。

**PROPOSAL B — 仅限变量声明位的目标类型化。** 只允许在"转换表达式是整个初始化的右值、且声明带显式 `As T`"时省略类型实参。实参位、属性赋值、`Return` 一律不允许。直接掐掉"会打架"的 `M(CType(x))`，把问题缩小到"类型在变量上"这一个风格。

**PROPOSAL C — 排除 `CType`、仅 `DirectCast`/`TryCast` 的目标类型化。** 在 B 的范围上再砍一刀：`CType` 是 "the all-around conversion operator"（2017-04-12 会议原句），它的省略在实参位引发最重的重载冲突；`DirectCast`/`TryCast` 是引用/拆箱方向，语义更窄更安全。内建简写族（`CInt` 等）继续覆盖 `CType` 到内建类型的部分。

**PROPOSAL D — 什么都不做，保持 inactive。** 转换目标类型显式写出是自文档；"冗余"是原则 #10（"冗长只在有用时是美德"）里那类有用的冗长。依赖既有机制：`Option Infer` + 类型写在转换上，或 `CInt`/`CStr` 简写族 + 类型写在变量上。

### 权衡：Q&A

- **A vs D：去掉重复类型，值不值？** 我们对"值"做了三问。一问：它服务哪个风格？——"类型在变量上"，少数派，且该风格在主线没有站住脚。二问：它是否消除真实样板？——对 `Dim d As Duck = DirectCast(animal, Duck)` 是消除的，但对 `Dim i As Integer = CInt(animal)` 它没有位置，因为 `CInt` 已经把类型写进操作符名里了。三问：它的成本是什么？——见下面"循环决议"与"上下文依赖语义"。三问之后，We are not convinced 值能盖过成本。
- **B vs A：只做变量声明位，能不能救活？** 能救活一部分，但不能救活全部。B 把最坏的实参位划出去之后，剩下的"声明位"语法是加法（今天 `DirectCast obj` 是解析错误，无既有代码重解释），破坏面小。但 B 仍然要为"省略类型实参"这一新文法付钱，而它覆盖的场景与 `CInt`/`CStr` 简写族大面积重叠（见深度追问 #9）。**We see a thin slice of residual value, not a compelling feature。**
- **C 是 B 的自然收窄吗？** 是，但收窄后更薄了。`CType` 到用户定义类型 + `DirectCast`/`TryCast` 的严格语义，是 `CInt`/`CStr` 简写族盖不到的两块。C 留下的残余需求真实存在，但它是"边缘中的边缘"——设计原则 #6（"不为边缘场景加特性"）正对它开火。且 C 没有解决文法问题：`DirectCast obj` 的绑定范围在复合表达式里如何界定，与选择哪种转换语义无关。
- **"会打架"的 `M(CType(x))` 到底难在哪？** 见深度追问 #4。一句话：**循环决议**。`M` 的重载选择依赖实参 `CType(x)` 的类型，而 `CType(x)` 的目标类型又依赖 `M` 选哪个重载。这不是"再想细一点"就能解决的工程问题，而是一个没有原则可依的语义坑。Anthony 把它标注为"会打架"，我们认同这个自评，并且不打算替它背锅。
- **`Let obj As Object = 1S` 真的演示了目标类型化吗？** 不。`1S` 是带后缀 `S` 的 Short 字面量，类型由后缀固定；`Short → Object` 是既有的加宽装箱转换。今天用 `Dim obj As Object = 1S` 就能编译，**不需要任何新特性**。2014-02-17 会议 #43 对字面量有一条一字不差的裁定："Literals are NOT target-typed in VB. They are integers (unless with type suffix)." 建议把这一行当作"目标类型化转换"的例子，是**误标**——它削弱的是建议自己的动机，因为主角示例并不依赖本特性。
- **`Trycast` 与 `TryCast` 的拼写之争是不是伪问题？** `Probably` 是。VB 关键字大小写不敏感，`Trycast` 与 `TryCast` 是同一个关键字。原文 18.1 写 `Trycast`、18.2（Guarded `Let`）写 `TryCast`，只是排版不一致，不构成新关键字。建议的 Unresolved #3 问"是否为最终拼写"——在大小写不敏感的语言里这个问题自行消解。`Suspect`：作者或许是想区分一种"不抛异常的 CType"，但那已是 `TryCast` 的既有语义，无需新名。
- **与既有会议的延续性。** 上一场 `If()` 最佳公共类型会议把"与目标类型转换协调'谁定类型'的裁决链"列为 Follow-up；postfix-casting 会议把"转换写法全景"备忘列为 TODO。今天这份建议正好是那张全景图里的又一名成员——它、`(As Type)`、`(As Any)`、`CInt` 简写族共享同一片"转换怎么写"的设计面。我们要求任何关于转换语法的决定都先读那份备忘，避免各建议各自发明一套。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

今天的 `DirectCast`/`TryCast`/`CType` 是要求 `(expr, Type)` 的关键字。省略类型实参意味着三件事之一：`DirectCast obj`（无括号前缀形式）、`CType(x)`（单实参括号形式）、或两者并存。建议原文**两种形态都出现了**——`Let i As Integer = DirectCast obj` 是无括号，`M(CType(x))` 是单实参括号。两份示例两种文法，这是建议自己的不一致。

无括号前缀形式一旦存在，绑定范围立刻成问题：

```vb
Option Strict On

Dim box As Box = New Gift()
Dim gift As Gift = DirectCast box.Content
' 目标类型 Gift。两种解释：
' (a) DirectCast(box.Content)：源 Object，拆箱/下行转换到 Gift —— 唯一可能成立的绑定。
' (b) (DirectCast box).Content：(DirectCast box) 是 Gift，.Content 是 Object，
'     Object → Gift 在严格模式无隐式转换 —— 编译错误。
' 两者并存的语法空间里，解析器必须先定 (a)，但建议没有给这条优先级规则。

Dim counter As Integer = DirectCast value + 1
' DirectCast 若吞掉整个 RHS，目标 Integer 作用于 (value + 1)；
' 若只作用于 value，则 value + 1 再被声明目标吸收。
' 省略类型实参的转换在复合表达式中的作用域，建议未定义。
```

`As` 在这里不参与消歧（与 postfix-casting 不同——那里 `(As Type)` 靠 `As` 起消歧），纯粹是新前缀运算符的文法、优先级与结合性问题。`Await DirectCast x`、`DirectCast x.ToUpper()`、数组类型目标（`Dim arr As String() = DirectCast o`）全部需要显式规则。**没有一条是致命的，但每一条都要写进 spec——建议一行都没有。**

#### 2. 角案例与边界语义

- **`TryCast` × 非可空值类型目标。** 2014-02-17 #23 决议："TryCast will allow a target type that's either Reference type or a nullable type. It will work exactly as in C#. Note that TryCast bypasses the latebinder."。`TryCast` 失败返回 `Nothing`，`Nothing` 不能赋给非可空值类型。因此：

```vb
Dim o As Object = 42

Dim i As Integer = TryCast o    ' 非法：目标 Integer 非可空，失败路径的 Nothing 无处安放
Dim i2 As Integer? = TryCast o  ' 合法：目标 Integer?，Nothing 可赋
```

"目标类型来自声明"与"目标类型必须是引用或可空"两条规则必须合取，否则 `Let i As Integer = Trycast expression` 就是一条必然类型错误或运行期崩溃的语法。建议未触及。

- **类型参数目标。** `Let v As T = Trycast expression` 中 `T` 是类型参数时，2017-04-12 有现成退化规则："Additionally CType will always behave like DirectCast for type parameter conversions."。省略形式必须继承这条退化，否则宽类型路径与窄类型路径行为分裂。`Probably` 这条可抄，但建议没写。

- **`Nothing` 源。** `Let x As String = DirectCast Nothing`——`Nothing` 直接作为转换源，目标类型存在但源无类型。这不是新问题（`DirectCast(Nothing, String)` 今天即可编译），省略形式继承了它，无额外困难。

- **复合 RHS。** 见 #1 的 `DirectCast value + 1`。省略类型实参的转换一旦不是"整个右值"，目标类型就不再单一。建议的 B 方案（仅变量声明位）可以顺带规定"省略形式必须是整个初始化右值"，但这是设计决策，不是天然事实。

- **多重声明。** `Dim a As Integer = DirectCast x, b As Long = DirectCast y`——每个声明各自的 `As` 提供各自的目标，无交互。这一条反而是干净的。

- **嵌套转换。** `Let s As String = DirectCast CType x`——内层 `CType x` 的目标是什么？外层 `DirectCast` 的目标是 `String`，内层没有任何目标可依。省略形式的嵌套没有可推断的来源。**建议应对此显式报错，但语法上必须先能解析到这一层。**

#### 3. 作用域与绑定

语义模型需要一个新的"省略目标类型的转换节点"，其 `GetTypeInfo` 返回上下文的声明类型。在声明位这很直接。但在实参位，`CType(x)` 的绑定符号与目标类型取决于**重载决议的结果**——而重载决议又依赖实参类型，见 #4。这意味着语义模型在实参位可能要先"猜"一个目标类型去跑重载，再回来修正；Roslyn 现有转换绑定没有这条路径。`Suspect`：这接近 C# 目标类型化 `new`/条件表达式引入的"回填"（backfill）机制，但 C# 只为**对象创建**做过，从未为**转换**做过——转换的操作语义（抛异常 vs 返回 Nothing vs 四舍五入）随目标类型而变，回填的代价完全不同。

#### 4. 与既有特性的交互

- **重载决议的循环（"会打架"本体）。** `M(CType(x))` 在 `M(Object)` 与 `M(String)` 并存时选哪个？目标类型依赖重载选择，重载选择依赖目标类型——没有原则可依。若只有一个 `M(As Object)`，则 `CType(x)` 的目标恒为 `Object`，用户想转成 `String` 的意图被静默吞掉；若未来有人给 `M` 加重载，既有调用"重编译后换行为"——2015-01-14 会议对插值字符串记录过同一类风险："Then the user's call will change behavior upon recompilation."。这与 `If()` LUB 会议里"重载重解析"是同族风险，而**转换比推断更糟**：推断改的是变量类型，转换改的是"运行期执行哪个转换操作"。
- **Late binding / Option Strict Off。** 宽松模式下 `obj.Member` 本来就晚期绑定。`Let s As String = DirectCast obj` 在宽松模式下目标类型已知，`DirectCast` 是显式转换不涉晚绑定；但 `M(CType(x))` 在宽松模式、`M(As Object)` 下，`CType(x)` 目标为 `Object`，等于一个无操作转换再晚绑定调用——比"类型写在转换上"的版本多一层没人要的间接。`TryCast` 还有专门约束："TryCast bypasses the latebinder"，宽松模式下的省略形式必须继承这一条。**两条路径行为一致性义务**要求把所有这些写进 spec。
- **ByRef。** `M(DirectCast x)` 中 `DirectCast` 结果是 rvalue，天然不能作 `ByRef` 实参——安全，与 postfix-casting 结论一致。但若 `M(As Integer)` 且参数 `ByRef`，`CType(x)` 作实参会触发 copy-in/copy-out，目标类型推断失败时诊断文案要能解释"转换结果不能 ByRef"。
- **Lambda 推断。** `M(Function(x) DirectCast x)`——`x` 的类型来自 lambda 参数推断，`DirectCast x` 的目标类型无来源。省略形式在 lambda 体内应禁止。建议未提。
- **表达式树。** `DirectCast obj` 若下略为 `DirectCast(obj, T)`，表达式树经 `Expression.Convert`/`TypeAs` 可表示；但若目标类型来自声明，而声明类型是变量（非 lambda 参数），表达式树场景通常不出现。`Probably` 可表示，须原型验证。
- **与 TypeOf 流分析。** 流分析（已决议 Active，限定范围）在 `If TypeOf animal Is Duck Then` 后让 `animal` 收窄；`Let d As Duck = DirectCast animal` 是这条收窄的**显式**版本。若两者都做，同一需求两条路径；若只做流分析，`DirectCast animal` 在收窄区内的意义就减弱了。这是跨建议的重叠，不是冲突，但要在"转换写法全景"备忘里对齐。
- **与 `If()` 目标类型化（B，Active）。** `Dim x As Integer? = If(True, CType a, Nothing)`——`CType a` 的目标类型来自声明 `Integer?` 还是来自 `If()` 的目标类型化？两条"谁定类型"的规则必须给出一条裁决链。上一场会议把这一条列为 Follow-up，今天再次确认。
- **与 postfix-casting / `(As Any)`。** `(As Type)`（Table）与省略形式共享"转换目标来自上下文"的概念空间，但文法完全不同。任何决定都要先读"转换写法全景"备忘，避免第三、第四种转换写法各自为政。

#### 5. Breaking change 与兼容性

**基干是加法的。** `DirectCast obj`、`CType(x)`（单实参）今天都是解析错误或"实参太少"错误，不存在"既有合法代码被重解释"的破坏面——这是本建议与 `If()` LUB 建议（宽松模式 late→early 替换）最本质的区别，我们如实记录。

**但有"重编译敏感性"。** 一旦 `M(CType(x))` 合法，往 `M` 上加一个重载就会改变 `CType(x)` 的目标类型、改变既有调用的绑定与运行期行为——同一源码，重编译后行为不同。这与 2015-01-14 记录的插值字符串重载风险同族，而插值字符串那次是"字符串赢，全文结束"（"End of story"）才压住的；转换省略形式没有这样一刀切的规则可压。**打破的瞬间是未来，不是现在。** 按 2018-06-13 的基调——"We will almost never make breaking changes to Visual Basic"、且对扩展表面积 "bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"——光"现在是加法"不足以让我们放行一个制造未来重编译敏感性的写法。

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径对省略形式的意义：

- **Option Strict On：** `Let i As Integer = DirectCast obj` 目标明确，走既有转换规则，无分叉。干净的路径。
- **Option Strict Off：** 同一句法的转换目标仍是声明类型，`DirectCast`/`TryCast` 的显式转换语义不变；但实参位 `M(CType(x))` 在宽松模式下会与晚绑定纠缠（见 #4）。两条路径必须行为一致——我们要求省略形式与显式写全的版本在**两种 Option 下语义逐字等价**，任何"省略了就分叉"的设计都不可接受。建议没有做这个等价性论证。

#### 7. IDE / IntelliSense

- 输入 `DirectCast `（无括号、无类型实参）时，补全应显示什么？——没有类型实参，没有类型名可补全；输入 `CType(` 时，补全是参数帮助还是类型名帮助，IDE 无法自明。
- InfoTip：实参位的 `CType(x)` 在重载决议完成前**没有目标类型可显示**——对 IntelliSense 是坏消息，用户看到的是"等待上下文"而非一个确定的转换。
- 新诊断文案：嵌套省略、lambda 体内省略、非可空值类型目标 + `TryCast`，每一类都要专用错误信息。
- 这些都不是做不进原型的借口，但没有原型验证就放进规范，等于没设计。

#### 8. 数据 / 普遍性

- 建议服务"类型在变量上"风格。该风格在主线没有量化支持——`Option Infer` 是项目模板默认，主流是 `Dim i = ...`（类型在右）。
- `Dim d As Duck = DirectCast(animal, Duck)` 这类"类型写两遍"的冗余，在真实代码库中的占比**没有任何数据**。
- 内建类型的"类型在变量上 + 转换"场景已被 `CStr`/`CInt`/`CBool` 简写族覆盖（`Dim i As Integer = CInt(x)`），这类代码今天就是零冗余的。残余缺口 = 用户定义类型 + `DirectCast`/`TryCast` 严格语义 + "类型在变量上"风格，三个条件的交集。**这是窄中之窄。** `Suspect`：真实但边缘，普遍性证据不足，按评价标准（如 Implicit-default-optional 的 85% 统计是分水岭）不达标。

#### 9. 更简替代

- **`CStr`/`CInt`/`CBool` 简写族**：内建类型的目标已经写进操作符名，`Dim i As Integer = CInt(obj)` 与 `Let i As Integer = DirectCast obj` 要消除的样板是一样的，而前者今天就能编译。这是对我们的最有力竞争者。
- **`Option Infer`**：把类型从变量移到转换上（`Dim i = DirectCast(obj, Integer)`），类型只写一遍，主流风格。
- **TypeOf 流分析 + 模式匹配**：`Case x As T` 模式变量（2018-12-19 主线文法已有 `'As' TypeName` 类型检查模式）在"测试 + 绑定 + 收窄"一步内完成强类型访问，覆盖大量"转换后访问成员"场景。
- **编译器优化**：2014-03-12 对"快速整型转换"场景的取向——"This is a compiler optimization, pure and simple. It adds no new syntax or concepts or library functions."——那是主线**拒绝** `d As Integer` 一类新语法时的原话。尽管那场会议针对的是性能而非可读性，取向一致：优先用既有机制，而非新语法。
- **Analyzer / 重构**：可以提示"此处类型重复、可改用 `CInt` 或 `Option Infer`"，但**不能**让 `DirectCast obj` 变得合法。作脚手架可，作替代不可。

#### 10. 复杂度 / 成本 / 优先级

新文法（前缀运算符 + 优先级 + 复合表达式作用域）、新绑定路径（上下文目标推断 + 实参位回填）、新诊断、IDE 三处改动（补全/InfoTip/错误文案）。没有原型、没有文法、没有 spec。价值是"窄风格下的样板消除"且与 `CInt` 族重叠。优先级：**低**，排在流分析、模式匹配、`If()` 目标类型化之后。`We're not excited enough to spend compiler time on this。`

#### 11. 运行时 / CLR 硬约束

无。省略形式若落地，下略为既有的 `DirectCast`/`TryCast`/`CType` IL（`castclass`、`unbox.any`、运行期转换调用），PEVerify 无碍，不触达 CLR 存储规则。唯一运行期影响来自目标类型推断**选错**导致的转换语义改变——那是设计风险，不是 CLR 约束。

#### 12. 值不值得做

逐维打分。**价值**：低——窄风格、窄场景、与 `CInt` 族重叠；真实但不普遍。**成本**：中——文法 + 绑定 + IDE 三处都动，且"会打架"的实参位没有干净解法。**风险**：实参位是循环决议，高；声名位是加法但制造"第三种转换写法"的心理/生态负担（2018-06-13："We strongly believe that Visual Basic has a stance - a way of doing things."）。**结论：现在不值得做。** 这不是"太难的否决"（fantastic idea, too hard to do），而是"价值 × 成本 × 风险"逐条打分后，价值填不满成本与风险的缺口。建议保持 inactive 是对的。

### VB 基因对照

- **永不破坏既有代码（原则 #1）**：基干是加法，不破坏；但实参位制造未来重编译敏感性，与 2015-01-14 "will change behavior upon recompilation" 同族。边缘通过，靠"省略形式与显式写全逐字等价"守住。
- **不引入"第二种做事方式"（原则 #3）**：**硬违背，重罚项。** VB 已有 `CType`/`DirectCast`/`TryCast` + `CStr`/`CInt` 简写族，本建议是第 N 种写法。2018-06-13 明说扩展表面积 "making a second way to do things" 的门槛 "will be relatively high even when it's a good idea"——而这甚至不是 good idea 的确认版本。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# **没有**目标类型化转换——C# 9 的目标类型化覆盖 `new`、条件表达式、方法组，但转换永远要求显式类型实参。本建议偏离 C# 且没有给出充分理由；2018-12-19 "we will follow C# unless there is a compelling reason" 正对它的门。
- **读起来像英语、对新手友好（原则 #5）**：负分。`CType(x)` 离开声明上下文不可读；把类型从表达式移到声明，等于把语义藏在别处。原则 #10 的"冗长只在有用时是美德"——转换目标类型正是"有用的冗长"，它是自文档。
- **避免隐蔽语义变化（原则 #7）**：实参位的 `M(CType(x))` 是教科书级例子——目标类型随重载决议而变，重编译换行为，`Return?` 被拒的同类理由。
- **消除常见样板（原则 #9）**：部分命中，但被 `CInt`/`CStr` 简写族抢先覆盖了 85%，残余缺口不构成"高频痛点"。
- **保持 VB-like（原则 #2）**：偏离。VB 的转换传统是前缀函数式显式类型（`CType(x, T)`）；省略目标类型在 VB/VB6 史上无对应物，2014-03-12 拒绝 `d As Integer` 时已亮过红灯。
- **与主线关系（对照表 2.3）**：主线对照表标注"目标类型转换 | 无 | 有（自评'会打架'）| **Anthony 独立延伸**"。它与主线最近接的触点——#59（`As Type`/`CVal`，2017-04-12 无决议）、2014 "字面量非目标类型化"、2014-03-12 "优化优先"——全部是**反向**的。这是对照表里"故意不跟 C#/F#"阵营的又一名成员，且没有主线牵引力。

### RESOLUTION:

1. **保持 inactive（Table），不激活全貌（PROPOSAL A）。** 理由叠加：① 实参位 `M(CType(x))` 是循环决议，没有原则可依，建议自己标注"会打架"；② 目标场景与 `CInt`/`CStr` 简写族大面积重叠，残余缺口窄中之窄；③ 偏离 C#（C# 无目标类型化转换）且无充分理由；④ 服务"类型在变量上"这一主线未认可的少数派风格。**对 A 的实参位，我们的姿态是方向性 Reject**——不是"暂缓"，而是"这条路线没有干净解"。
2. **PROPOSAL B/C（仅变量声明位、仅 `DirectCast`/`TryCast`）= Table，绑定两条复活信号**：
   - (a) "类型在变量上"风格（`Let`/`Dim ... As T`）出现主线或 ModVB 内可核实的采用数据，证明该风格的用户群值得单独服务；
   - (b) 实测显示"用户定义类型 + `DirectCast`/`TryCast` 严格语义 + 类型在变量上"交集场景在真实代码中有显著占比。
   两条都不满足，就不值得为它引入第三种转换写法。
3. **"会打架"的实参位不进任何版本的设计。** 除非未来出现"重载决议先行、转换目标回填"的完整设计（我们 `Suspect` 这条路要触达 Roslyn 转换绑定的根），否则 `M(CType(x))` 保持非法。
4. **`Trycast` 拼写问题关闭。** VB 关键字大小写不敏感，`Trycast` 与 `TryCast` 是同一关键字；Unresolved #3 在大小写不敏感下自行消解。
5. **一致性义务。** 若 B/C 任一复活，省略形式必须与显式写全的版本在**两种 Option 下语义逐字等价**；必须给出 `DirectCast obj` 在复合表达式中的作用域规则；必须继承 2017-04-12 的"类型参数转换 CType 退化为 DirectCast"规则；必须与 `If()` 目标类型化定出"谁定类型"的裁决链；必须先读 postfix-casting 会议委派的"转换写法全景"备忘。

### Implication:

- 将本建议的状态标注更新为 **LDM Considering（Table，实参位方向 Reject）**，并在建议文档头部注明关联 #59、2014 字面量裁定、以及两条复活信号。
- 把"目标类型化转换"写入 postfix-casting 会议委派的"转换写法全景"备忘，作为新增转换语法提案的前置阅读材料。
- 给 `If()` 目标类型化（B，Active）团队回执：裁决链"目标类型 > dominant type > 转换目标类型 > `Object`"中，转换省略形式应被 `If()` 的目标类型化**吸收**，而非与之竞争。
- 若复活评估启动，前置条件：可编译示例（含 `DirectCast`/`TryCast`/`CType` 三种与值类型/可空/类型参数目标的矩阵）、BNF 文法与优先级、复合表达式作用域规则、Compatibility 分析（重编译敏感性）、最小原型验证语义模型与 IDE。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：实参位循环决议是否有任何可接受的部分解（如"仅当所有候选重载的参数类型相同才允许省略"）——我们 `Suspect` 没有，但不关闭讨论。
- `OPEN QUESTIONS`：省略形式与"类型参数目标（`T`）"的交互——2017-04-12 的 CType→DirectCast 退化规则是否足以覆盖 `Let v As T = Trycast expression`。
- `OPEN QUESTIONS`：`Dim a As Integer = DirectCast x, b As Long = DirectCast y` 多重声明已验证无交互；但模块级（非局部）声明的省略形式是否影响公开 API 表面，未评估。
- `TODO`：量化"类型在变量上 + 显式转换"在真实代码中的占比；量化 `CInt`/`CStr` 简写族对"类型写两遍"的覆盖度——这是复活信号 (b) 的证据来源。
- `TODO`：postfix-casting 委派的"转换写法全景"备忘须收录本建议（含 `M(CType(x))` 的循环决议分析）。
- `Follow-up`：跟踪主线 #59（`As Type`/`CVal`）的最终状态；若主线对转换语法出现任何决议，重审本建议。

### 状态

- **LDM 状态：建议整体 LDM Considering（Table）**；实参位（PROPOSAL A 的"会打架"部分）方向 Reject；声明位窄形式（B/C）Table，绑定两条复活信号。
- **三态判定：Table（保持 inactive）** — 价值真实但窄、与 `CInt` 简写族重叠、实参位无干净解、偏离 C# 无理由、服务少数派风格。值得记住（收录进"转换写法全景"），不值得现在做。

---

## 附录：特性评价

# 建议评价报告：proposal-target-typed-conversions.md

## 评价对象

- 建议：proposal-target-typed-conversions.md — 目标类型化转换（`DirectCast`/`TryCast`/`CType` 省略类型实参，由上下文目标类型推断）
- 来源：Anthony 原文第 18 章 18.1 "Target-typed conversions?"（`..\..\AnthonyDesign_wordpress.txt` L2715–2728；示例与注释 `' I think this will cause fist fights.` 逐字出自该章）
- 配方目标：为"类型写在变量上"的风格消除转换表达式中重复的类型实参，把类型信息集中到变量声明一处

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。改进目标（去重复类型）清晰但不可衡量；无原型无文法，证据止于书面；主角示例 `Let obj As Object = 1S` 是**误标**——`1S` 是后缀 Short 字面量，今天即可编译，不依赖本特性（2014-02-17 #43 "Literals are NOT target-typed in VB."）；关键子效果"实参位省略"被自评"会打架"且无解 | 已提供/已检查（状态行 Prototype/Implementation/Specification 为占位链接，无运行证据） | 声称的效果在多数场景已被 `CInt`/`CStr` 简写族覆盖；残余场景无数据 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化"（此处为原创但反 VB 文化）。偏离核心基因：原则 #3（第三种转换写法，重罚项）、#7（实参位隐蔽语义变化）、#4（偏离 C# 且无理由——C# 无目标类型化转换）、#5（`CType(x)` 脱离上下文不可读） | 已检查 | "类型在变量上"风格本身是主线未认可的 Anthony 独立延伸；特性是"为主流语言造少数派写法" |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题具体诚实（1–3 健康区间，加分）；但 Detailed design 仅 4 行代码无文法/spec、边界极含糊（允许哪些上下文未定且波及核心设计）、无 Compatibility 分析、状态行占位链接、`1S` 误标构成示例与正文矛盾 | 已检查 | 无文法、无优先级规则、无 Option Strict 分叉；`M(CType(x))` 的循环决议被 Drawbacks 一句带过而非分析 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。一致性断裂——同一 `CType(x)` 在不同上下文语义不同，转换写法家族再添一员且与 postfix-casting/`(As Any)` 各自为政；风（演化一致性）受损，与主线"字面量非目标类型化"、2014-03-12"优化优先"取向相背；唯一干净点是基干为加法（不破坏既有合法代码） | 已检查（预测待定） | "会打架"的实参位无缓解设计；重编译敏感性（加重载换行为）无应对；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源标注准确（Anthony 18.1，无 C# 借鉴、无 VB6 继承、原创）；但**未标注**相关主线先例——2014 "Literals are NOT target-typed in VB" 直接驳斥 `1S` 示例、`CInt`/`CStr` 简写族（继承 VB6 的既有机制）是"更简替代"却未提及、2017 #59 转换语义讨论未参与；"1S 目标类型化"的标注与实际影响有偏差 | 已检查 | 无杂质；但主角示例与既有关键机制的关系未标注，先例继承关系不透明 |

## 设计原则对照

- **与 VB 基因：偏离（主体）。** 唯一一致性正向是"消除重复样板"的直觉（原则 #9 部分命中），但被 `CInt`/`CStr` 简写族抢先覆盖 85%。核心基因负分：原则 #3（第三种写法）、#7（实参位隐蔽语义变化）、#4（偏离 C# 无理由）、#5（脱离上下文不可读）、#10（显式转换目标是"有用的冗长"）。
- **与主线关系：Anthony 独立延伸（对照表 2.3 明确标注，"自评'会打架'"）。** 与主线最近触点为反向：2014 字面量裁定、2014-03-12"优化优先"、#59 转换语法讨论（无决议）。无 C# 对应物（C# 9 目标类型化覆盖 `new`/条件/方法组，不含转换）。
- **破坏性变更：基干无（加法语法，`DirectCast obj`/`CType(x)` 今天即非法）；但有重编译敏感性**——实参位省略合法化后，为 `M` 加重载会改变既有调用绑定与运行期行为（2015-01-14 "will change behavior upon recompilation" 同族）。无 langversion 门控、无警告策略设计。

## 总评

- **达成程度：未达成（作为可采纳特性）/ 部分达成（作为记录在案的实验性想法）。** 建议自身诚实标注 inactive、标注"会打架"，作为"Needs more bake time"的存档是合格的；作为可落地语言特性，效果、基因、属性三维均不达标。
- **LDM 三态建议：Table（保持 inactive）**；实参位（`M(CType(x))`）方向 Reject——循环决议无干净解；声明位窄形式（PROPOSAL B/C）Table，绑定两条复活信号（风格采用数据 + 残余场景占比数据）。面向 VBScript.NET 的优先级同样为 **Table**：脚本风格代码依赖宽松类型与 `CStr`/`CInt` 简写族，本特性不服务脚本迁移痛点，反而在实参位引入循环决议。
- **主要问题**：① 实参位循环决议无解（自评"会打架"且确实如此）；② 目标场景与 `CInt`/`CStr` 简写族重叠，残余缺口窄中之窄且无数据；③ 偏离 C# 无理由、服务少数派风格；④ `1S` 主角示例误标，削弱自身动机；⑤ 无文法、无 Compatibility 分析、无原型。

## 返工建议

- **补充章节**：文法（BNF）——省略类型实参的前缀/单实参括号形态的统一、与 `.`/索引/`Await` 的优先级、复合表达式作用域；上下文分类学——逐类评估声明/赋值/`Return`/实参/集合初始化器/ lambda 体的目标推断可靠性，并显式给出"实参位不做"的裁定；Compatibility——重编译敏感性、Option Strict 分叉、late binding、表达式树。
- **补充证据**：最小原型（声明位 + `DirectCast`/`TryCast`，含值类型/可空/类型参数目标矩阵）；"类型在变量上 + 显式转换"与"`CInt`/`CStr` 覆盖度"的真实代码测量；语义模型 `GetTypeInfo` 与 IDE 补全验证。
- **未决问题处理**：`Trycast` 拼写关闭（大小写不敏感下消解）；`M(CType(x))` 除非出现"重载先行、转换目标回填"的完整设计，否则保持非法并记录为方向 Reject；与 `If()` 目标类型化的"谁定类型"裁决链并入该建议的 speclet。
- **设计探索**：把"省略目标类型的转换"重新表述为"转换的语法糖"而非新特性，并对照 `CInt`/`CStr` 简写族逐例证明净增益；与 postfix-casting 委派的"转换写法全景"备忘合并，避免第三、第四种转换写法各自为政。

---

## 附录：C# 生态与互操作考量

> 本附录评估本提案（目标类型化转换——`DirectCast`/`TryCast`/`CType` 省略类型实参、目标类型由上下文推断）在 C#/CLR/.NET 生态中的对应走向。ModVB 索引（`..\..\..\csharplang-index.md`）未单列 target-typing 主题，以下均直接据 csharplang 镜像原文核实（逐字引用 + 来源路径）。

### 相关 C# 现实方向

C# 的 target-typing（目标类型化）是一条真实且持续的主线，但它**严格限定在"构造形式"上，从未延伸到"转换"**：

1. **C# 7.1 target-typed `default`**（→ `proposals\csharp-7.1\target-typed-default.md`）。动机原文：「The main motivation is to avoid typing redundant information.」——与本提案"消除冗余类型"的直觉同源，但对象是 `default` 字面量，不是转换。

2. **C# 9 target-typed `new`**（→ `proposals\csharp-9.0\target-typed-new.md`）。Summary：「Do not require type specification for constructors when the type is known.」。规范把 `new()` 建模为对每一类型都存在的一种转换：「A *target_typed_new* expression does not have a type. However, there is a new *object creation conversion* that is an implicit conversion from expression, that exists from a *target_typed_new* to every type.」关键性质：落到每个目标类型上就是"对应的构造表达式"，**转换操作语义不随目标类型变化**（同一批构造函数参数）。C# 同时划出禁区：「It is a compile-time error if a *target_typed_new* is used as an operand of a unary or binary operator, or if it is used where it is not subject to an *object creation conversion*.」，并在 Miscellaneous 列出 `new().field`、`foreach`、`using`、deconstruction、`await`、匿名类型属性、`lock`、`sizeof`、`fixed`、`is` 操作数、`??` 左操作数、LINQ 查询等一串禁位。**dynamic 被明确排除**：「**dynamic:** we don't allow `new dynamic()`, so we don't allow `new()` with `dynamic` as a target type.」，且禁用于「in a dynamically dispatched operation (`someDynamic.Method(new())`)」。

3. **C# 9 target-typed conditional**（→ `proposals\csharp-9.0\target-typed-conditional-expression.md`）。对无公共类型（或某分支无隐式转换到公共类型）的 `c ? e1 : e2`，「we define a new implicit *conditional expression conversion* that permits an implicit conversion from the conditional expression to any type `T` for which there is a conversion-from-expression from `e1` to `T` and also from `e2` to `T`.」为压制歧义，C# 把该转换降为最低优先级：「we prefer any other conversion to a *conditional expression conversion*, and use the *conditional expression conversion* only as a last resort.」——这是 ModVB 的 `If()` 目标类型化（B，Active）可整体借用的 C# 先例。

4. **C# 12 collection expressions**（→ `proposals\csharp-12.0\collection-expressions.md`）。规范明写「Collection literals are target-typed.」；「A *collection expression conversion* allows a collection expression to be converted to a type.」；空字面量「The empty literal `[]` has no type. However, similar to the *null-literal*, this literal can be implicitly converted to any *constructible* collection type.」——`[]` 无类型、完全靠目标类型化，是"目标类型化"的字面量版。互操作面：collection expression 依赖 `[CollectionBuilder(...)]`（`System.Runtime.CompilerServices.CollectionBuilderAttribute`）与 `[InlineArray(N)]` 元数据，且可下略到 span/stackalloc（C# 低层主线 T2）。

5. **方法组（meeting 正文所提）与未来方向**。方法组的 target-typing 是隐式方法组转换（`System.Action a = MethodGroup;`），C# 13 只给它加了"弱自然类型"（→ `proposals\csharp-13.0\method-group-natural-type-improvements.md`：「That type is a "weak type" in that it only comes into play when the method group is not target-typed (ie. it plays no role in `System.Action a = MethodGroup;`).」）。未来（2025–2026）继续扩展 target-typing：`proposals\target-typed-generic-type-inference.md`（「Generic type inference may take a target type into account.」，服务 unions/closed hierarchies 的构造）与 `proposals\target-typed-static-member-access.md`（`.Xyz` 目标类型化静态成员访问）。后者 Notes 里有一条与本提案直接相关，可视为 **C# 对一切 target-typed 表达式的铁律**：「As with target-typed `new`, overload resolution is not influenced by the presence of a target-typed static member expression. If overload resolution was influenced, it would become a breaking change to add any new static member to a type.」（→ `proposals\target-typed-static-member-access.md`，Notes）

**负向确认（OPEN QUESTIONS）**：在 csharplang 镜像全库检索，proposals 目录含 "target-typed" 的 17 个文件无一涉及"转换省略目标类型"——C# 的 cast 永远要求显式类型实参 `(T)E`，不存在目标类型化转换。`Suspect`：C# LDT 对"转换省略"从未立项，本提案 `M(CType(x))` 的循环决议在 C# 侧没有可对照的落地先例。

### 现实 vs 提案

- **动机兼容（部分）**：C# 从 7.1 到 12 一路在做"上下文已知时省掉冗余类型"，与本提案直觉同源。C# 对"类型写两遍"的回答是 `new()`/`[]`/`default`（构造与字面量）；VB 对同一痛点的既有回答是 `CInt`/`CStr` 简写族（转换）。**本提案夹在两者之间，而 C# 侧从未选择"转换省略"这条路**——与本提案最近的 C# 先例是 `new()` 禁入 operand/dynamic 位置的"构造版划界"。
- **结构性冲突（核心）**：C# 的 target-typing 一律作用于**自身无转换语义的表达式**（new=构造、default=零值、`[]`=字面量、conditional=分支选择），目标类型改变的是"构造哪个类型"，不是"执行哪个转换操作"。本提案的 `DirectCast`/`TryCast`/`CType` 恰恰相反——目标类型决定"抛异常 vs 返回 Nothing vs 用户定义转换 vs 舍入"，操作语义随目标类型而变（正文深度追问 #4）。**C# 从未为"转换"做 target-typing，不是遗漏，是这条构造/转换分界的必然结果。** 表面同构的"省略类型实参"（`new()` vs `DirectCast x`），分属两个世界。
- **需桥接（实参位/重载决议）**：`M(CType(x))` 的循环决议，在 C# 侧有一条可援引的反向证据：C# 宁可让 `new()`、`.Xyz` **完全不参与重载决议**（见上文 Notes 铁律），也不让"目标类型 ← 重载决议"的反向依赖存在。LDM-2020-03-25 记录过同族风险：「if a user uses `new()`, adding a constructor to a type can produce an ambiguity. Similarly, if a method is called with `new()` that can produce an ambiguity if more overloads of that method is added. This is analogous with `null` or `default`, which can convert to many different types and can produce ambiguity.」（→ `meetings\2020\LDM-2020-03-25.md`）。VB 的实参位要求**正好相反**——目标类型由重载决议回填——所以 C# 的回避策略不能照搬，只能桥接：把省略形式限死在"不参与重载决议"的声明位（PROPOSAL B/C），或承认实参位无解（RESOLUTION #3 的方向 Reject 由此被 C# 证据加固）。
- **脱节（dynamic/晚期绑定）**：C# 的 target-typing 与 dynamic 明确互斥（`new()` 不以 dynamic 为目标、禁入 dynamic 分派位置）。VBScript.NET 的核心面向是 Any 伪类型 + 晚期绑定（决策文件 M2），若在宽松模式/Any 上下文中引入转换省略，将直接落入 C# 明确回避的地带。正文"两条路径行为一致性"义务在 C# 侧找到对产物：C# 用"禁 dynamic 目标"一刀切，VB 若要保留动态场景，须明确"省略形式仅限 Option Strict On 的非动态上下文"。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：若 B/C 任一复活，把省略形式限定为「Option Strict On + 声明位 + 非 Any/非 dynamic 目标」，与 C# "target-typed 表达式不影响重载决议、禁 dynamic 目标"的划界同构——C# 的先例可直接平移为 VB 的声明位规则。
2. **识别新元数据（source-gen 桥）**：若 VBScript.NET 需与 C# 12 生态互操作，.vbx 编译器必须识别 `[CollectionBuilder(...)]` 与 `[InlineArray(N)]` 元数据（均属 `System.Runtime.CompilerServices`），否则无法消费以 collection-builder 暴露的 C# 集合类型（如 `ImmutableArray<T>`）。target-typing 本身是编译期概念、零运行期成本、不触 AOT/trimming，但**它依赖的新元数据必须被 .vbx 编译器读懂**——与决策文件 M4/M8 的"识别新元数据"主线一致。
3. **"last resort"降级规则可借用**：C# 用"其他转换优先、条件表达式转换仅作最后手段"压制歧义。VB 若做声明位省略，可借用同一策略（"省略形式在转换候选里优先级最低"）来守住"与显式写全逐字等价"的判定；但该策略对实参位循环决议无效——那是"目标类型依赖决议"而非"候选间排序"，不可混用。
4. **对 `If()` 目标类型化（B，Active）的直接利好**：C# 9 target-typed conditional 就是 `If()` 提案的 C# 对应物，已落地且有完整规范（含 last-resort 规则与 cast 优先旧语义的细节）。`If()` 团队应直接读该 speclet 作参考——这是本提案附录意外指出的、ModVB 可免费借用的 C# 资产。

### 对既有 RESOLUTION/三态判定的影响

- **RESOLUTION #3（实参位方向 Reject）被加固**：C# 全库无"目标类型化转换"先例，且对 `new()`/`.Xyz` 一律以"不参与重载决议"回避循环——证明 `M(CType(x))` 的"重载先行、转换目标回填"不是"还没人想清楚"，而是与 C# 的设计惯例（乃至可判断的重载决议原则）相悖。方向 Reject 维持。
- **RESOLUTION #2（B/C 复活信号）应增补一条 C# 对齐要求**：复活后的省略形式必须复制 C# "target-typed 表达式不影响重载决议"的划界（即声明位限定），否则任何实参位扩展都会撞上 C# 也回避的循环。
- **原则 #4（默认跟随 C#）措辞精化**：正文"C# 没有目标类型化转换"成立，但附录要精确：C# **有** target-typing 方向（default/new/conditional/collection expressions/方法组），**只是从未把它用到转换上**。本提案偏离的不是"target-typing 方向"，而是"把 target-typing 延伸到转换"——恰好越过 C# 精心维护的构造/转换分界。偏离事实成立，且理由更充分。
- **三态判定维持 Table**。C# 证据没有给本提案加分，反而解释了"为什么 C# 也没做"——两侧都选了更简的替代（C# 构造位 `new()`/`[]`，VB 转换位 `CInt`/`CStr` 简写族）。

### 引用纪律

本附录所引 C# 原文均逐字摘自 csharplang 镜像并标注来源（见各小节内嵌 `→` 路径）；以下为可直接复用的核对清单：

| 原文（逐字） | 来源 |
|---|---|
| "The main motivation is to avoid typing redundant information." | `proposals\csharp-7.1\target-typed-default.md` |
| "Do not require type specification for constructors when the type is known." | `proposals\csharp-9.0\target-typed-new.md` |
| "A *target_typed_new* expression does not have a type. However, there is a new *object creation conversion* that is an implicit conversion from expression, that exists from a *target_typed_new* to every type." | `proposals\csharp-9.0\target-typed-new.md` |
| "It is a compile-time error if a *target_typed_new* is used as an operand of a unary or binary operator, or if it is used where it is not subject to an *object creation conversion*." | `proposals\csharp-9.0\target-typed-new.md` |
| "**dynamic:** we don't allow `new dynamic()`, so we don't allow `new()` with `dynamic` as a target type." | `proposals\csharp-9.0\target-typed-new.md` |
| "we define a new implicit *conditional expression conversion* that permits an implicit conversion from the conditional expression to any type `T` for which there is a conversion-from-expression from `e1` to `T` and also from `e2` to `T`." | `proposals\csharp-9.0\target-typed-conditional-expression.md` |
| "we prefer any other conversion to a *conditional expression conversion*, and use the *conditional expression conversion* only as a last resort." | `proposals\csharp-9.0\target-typed-conditional-expression.md` |
| "Collection literals are target-typed." | `proposals\csharp-12.0\collection-expressions.md` |
| "A *collection expression conversion* allows a collection expression to be converted to a type." | `proposals\csharp-12.0\collection-expressions.md` |
| "The empty literal `[]` has no type. However, similar to the *null-literal*, this literal can be implicitly converted to any *constructible* collection type." | `proposals\csharp-12.0\collection-expressions.md` |
| "That type is a "weak type" in that it only comes into play when the method group is not target-typed (ie. it plays no role in `System.Action a = MethodGroup;`)." | `proposals\csharp-13.0\method-group-natural-type-improvements.md` |
| "Generic type inference may take a target type into account." | `proposals\target-typed-generic-type-inference.md` |
| "As with target-typed `new`, overload resolution is not influenced by the presence of a target-typed static member expression. If overload resolution was influenced, it would become a breaking change to add any new static member to a type." | `proposals\target-typed-static-member-access.md` |
| "if a user uses `new()`, adding a constructor to a type can produce an ambiguity. Similarly, if a method is called with `new()` that can produce an ambiguity if more overloads of that method is added. This is analogous with `null` or `default`, which can convert to many different types and can produce ambiguity." | `meetings\2020\LDM-2020-03-25.md` |

**OPEN QUESTIONS**：① "C# 无目标类型化转换"为全库检索的负向结论（proposals 目录 17 个含 "target-typed" 的文件无一涉及转换省略；LDM 会议侧未逐一穷尽，但提案层无先例）；② collection expression 与动态/反射的精确互操作细节（非泛型 `ICollection`/`IList` 实现以支持数据绑定，见 collection-expressions.md）未深挖；③ COM source generator 生态属 dotnet/runtime，不在本库（沿用索引 OPEN QUESTIONS）。
