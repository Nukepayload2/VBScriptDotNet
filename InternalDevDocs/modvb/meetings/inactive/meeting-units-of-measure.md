# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天处理一份 **inactive** 建议——`proposal-units-of-measure.md`（Anthony 原文第 18.19 节 "Units of Measure?"）。按提案生命周期惯例，inactive 意味着"值得记下但不打算现在做"；我们要回答的不是"怎么落地"，而是**它值不值得被激活、保持搁置，还是归档 Reject**。开场前我们核实了三件跨场事实，它们会贯穿全场：

- **主线从未讨论过度量单位。** 我们对 `..\..\..\vblang` 全文检索 `unit of measure` 零命中；`F#` 在主线会议里只作为对照语言出现（2014-02-10 模块设计、2014-02-17 二进制字面量 "It will be nice for enum literals. F# has them."、2018-12-19 模式匹配）。这印证了我们的评价标准里那条定位：**F# 只作灵感参考，不作为默认**。本建议是主线外、纯 Anthony 的 F# 延伸。
- **这份建议不声称任何特性。** 它不是一份设计，而是一份诚实的"思考状态"记录：Anthony 喜欢单位的感觉但不确信是 no-brainer，练习只得出一个"无争议"的结论（泛型运算符），外加一个他自认非必需的语法糖 `Weight(in lbs)`，并明说"exploring the space is absolutely worth a dedicated and detailed in-depth posting"。我们对一份没有设计可评的建议，评价对象其实是"这个空间值不值得投入设计时间"。
- **平台已经替我们回答了一半。** 泛型运算符——这份建议唯一的"无争议"结论——能力上已被 .NET 7 起的泛型数学（static abstract interface members / `INumber(Of T)`）基本覆盖，而那条设计轨属于 C#/运行时，不是 VB。这一点后面反复回来。

## Agenda

* [Proposal: 度量单位（Units of Measure）](#proposal-度量单位units-of-measure)

## Proposal: 度量单位（Units of Measure）

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)（2018-12-19 会议，LDM 对待 F# 特性的态度）；[vblang #215 – Attributes on Generic Type Parameters](https://github.com/dotnet/vblang/issues/215)（2017-12-06，泛型参数相关 Approved-in-Principle）；ModVB：`inactive/proposal-annotated-types`（18.20，注释模型与单位相互引用）、`proposal-any-pseudotype`（注释类型先例）、`meeting-runtime-library`（运行时方法/表达式树改动的既有约束）、`meeting-typeof-flow-analysis`（守卫惯用法同一流引擎）_

### 场景与缺口

We opened with the scenario the proposal gestures at, and we do not dispute that unit errors are real. 工程史上因单位混用造成的灾难（火星气候轨道飞行器的公制/英制失配是教科书案例）是编译期量纲检查的经典辩护。F# 正是这么做的：`[<Measure>] type kg` 声明量纲，值写作 `1.0<kg>`，类型检查器在编译期做量纲代数——`kg * m / s^2` 就是 `N`，运行期擦除为底层数值（`float<kg>` 运行时就是 `float`）。（此段 F# 机制为平台常识，非 vblang 内容。）

但缺口必须精确。Anthony 自己在 18.19 里的表述逐字摘录：

> "When F# debuted with many cool ideas. I like the feeling of units of measure but I'm not convinced it's a no-brainer. I've gone through several exercises to see how a VB user might implement a unit of measure system in a library without language support at all, or, what the minimal set of features could be added to make the experience of creating and using such a system acceptable. I will continue to investigate but so far my exercises have yielded 1 uncontroversial feature: **Generic Operators**."

三个观察：

1. **缺口的形状是"创建和使用这类系统的体验"，不是"单位本身"。** 他考察的是库级实现能在 VB 里走多远、最少加什么语言特性才可用。换句话说，他先承认语言级单位系统可能不成立，转而问"库能不能把体验做到可接受"。
2. **"无争议"这个词很重。** 主线对 F# 的态度是"灵感参考、不默认"，而本建议连自己都只给出一条无争议结论。我们稍后会论证：那条结论其实不属于 VB 语言特性，而是平台特性。
3. **与 18.20 的相互引用是真实的裂缝。** 18.19 说"像真实类型"，18.20 反问"what if it were tracked as an annotation on a numeric variable rather than a real type?"。**两个模型是两种不同的价值主张和成本结构，却被同一份建议文档同时引用、不加区分**——这是我们整场最看重的一点。

### 候选方案

**PROPOSAL A — 完整真实类型单位系统（F# 式）。** 单位作为真实类型：编译期量纲代数、运行期擦除、`Newtons = kg * m / s^2` 这类量纲组合。这是 F# 的完整形态，也是"编译期单位安全"的唯一正解。

**PROPOSAL B — 最小特性：仅泛型运算符。** 采纳 Anthony 唯一"无争议"的结论作为全部：让运算符可作用于泛型类型参数，单位系统完全靠库构建。语言不引入任何单位语法。

**PROPOSAL C — 注释模型（18.20 方向）。** 单位作为数值变量上的**注释**而非真实类型（`As Double<lbs>`），IDE 据此提供补全与诊断，编译器不做量纲检查。复用 annotated-types 的"类型风味"机制。

**PROPOSAL D — 什么都不做 / 保持 inactive。** 保持搁置。它的两个产物（泛型运算符、语法糖）都不需要 VB 动作：泛型运算符已由平台交付，语法糖未定义且作者自认非必需。

### 权衡：Q&A

We walked the design through our questioning list. The points below are the ones that actually bit; not in priority order.

**Q1：需求在哪里？我们手上是零数据。** 主线从未讨论过单位（已核实零命中），没有 user-voice、没有 connect 请求、没有 issue。对"数十万安静客户"的业务代码，单位混用是真实缺陷源，但频率没有量化。更尖锐的是：**业务应用里最有价值的"单位"是货币/金额，而货币转换是动态的（市场汇率），固定系数的编译期量纲系统恰恰解决不了它**。物理单位（lbs、m/s）是窄垂直面，不是横向需求。`Suspect`：需求真实但狭窄，无数据支撑优先级。

**Q2：泛型运算符真的"无争议"吗——它甚至是不是 VB 特性？** 这是整场最重要的追问。要做到在泛型类型参数上写算术，需要 CLR 级别的静态抽象接口成员；我们当场写了两段对照：

```vb
' Anthony 想要的泛型运算符——今天不能直接写：
Function Convert(Of T)(value As T) As T
    Return value + value        ' 编译错误：T 上没有 + 运算符
End Function

' 平台答案：.NET 7 起泛型数学（SAIM / INumber(Of T)）。
' Probably：VB 对 As INumber(Of T) 约束的完整支持需编译器验证。
Imports System.Numerics
Function DoubleIt(Of T As INumber(Of T))(value As T) As T
    Return value + value        ' 平台交付的能力，设计轨属 C#/运行时，非 VB 语言特性
End Function
```

平台已通过 .NET 7 的 `INumber(Of T)` 交付泛型数学，且那条设计轨走的是 C# LDM / 运行时设计，不是 VB 语言特性。我们评价标准明确："CLR/库层变更直接委托 C# LDM"。所以 Anthony 口中"最小、无争议"的结论，落地形态是**平台特性，VB 只需要跟上而不是发明**。这与主线"默认跟随 C#，除非有充分理由"完全一致。`Probably`：Anthony 想要的"泛型运算符"能力与平台泛型数学基本重合（他的原文未给语法，我们无法逐字对齐，故标 Probably）。我们后续不再把"泛型运算符"当作 VB 特性候选来评。

**Q3：真实类型 vs 注释——最深的岔路，而建议把它留给读者。** 真实类型 = 编译期安全 + 大编译器工程 + 元数据/互操作包袱。注释 = 只有工具层体验，无安全，但便宜、贴合 VBScript.NET 的脚本/数据受众。这两者**不是同一特性的两种写法**：一个是"杜绝单位错误"，一个是"编辑体验更顺"。建议同时引用两个模型却不选择，等于把特性定义本身悬置。我们倾向注释模型先行（见 RESOLUTION），但把它严格归入 18.20 的地盘。

**Q4：`Weight(in lbs)` 是什么？** 建议唯一的具体语法，三种读法全未定义：(a) 单位标注的类型应用；(b) 声明注释（18.20 风味）；(c) 构造式调用。我们把它摆到今天的文法旁边看：

```vb
' Weight(in lbs) 是什么？三种读法，全未定义：
Dim w As Weight(in lbs)       ' (a) 单位标注的类型应用？（Weight(...) 不在今天文法内——泛型要 Of，数组是 () 修饰名）
Dim w2 As Weight(Of In T)     ' (b) 撞上变体修饰符 In/Out 的既有文法：Interface IEnumerable(Of Out T)
For Each x In widgets         ' (c) in 是 For Each / LINQ 查询的既有关键字（From x In list）
```

而且它撞现有语法三处——`in` 是 `For Each x In ...` 与查询子句的关键字；`In`/`Out` 是泛型接口的变体修饰符（`IEnumerable(Of Out T)`），`Weight(in lbs)` 在类型实参位置读起来就像在写变体标注；VB 泛型类型必须 `Of`（`Weight(Of lbs)`），`Weight(in lbs)` 没有 `Of`，根本不在今天文法内。**我们对语法糖按"否决的备选语法"留档**——不是概念错误，而是未定义片段 + 作者自认非必需 + 与既有文法撞车。若未来单位系统成立，语法必须重新设计（届时参考 2018-12-19 模式匹配的做法：`Is` 因引用相等歧义被弃，`Matches` 成为候选关键字——命名与读法要过一遍"像不像 VB"的审美关）。

**Q5：库路线到底能走多远？** 我们当场画了一个 `Structure Weight` 包 `Double` 的库方案：

```vb
' 库级方案：普通结构体 + 运算符重载——同单位安全的切片今天就有。
Structure Weight
    Private ReadOnly _value As Double
    Public Sub New(value As Double)
        _value = value
    End Sub
    Public Shared Operator +(a As Weight, b As Weight) As Weight
        Return New Weight(a._value + b._value)
    End Operator
    Public Shared Operator *(w As Weight, scale As Double) As Weight
        Return New Weight(w._value * scale)
    End Operator
End Structure

Dim total = New Weight(12.5) + New Weight(3.0)   ' 编译通过：类型即单位
Dim bad = New Weight(12.5) + New Length(3.0)     ' 编译错误：+ 未对 Length 定义

' 复合单位：每对组合都要手写运算符，类型系统算不出单位乘积。
Structure Speed
    Public Shared Operator /(d As Distance, t As Duration) As Speed
        Return New Speed(d.Meters / t.Seconds)   ' 还要手写 Distance * Frequency = Speed、…
    End Operator
End Structure
```

结论：**便宜的切片今天就有，贵重的切片语言不支持**。同单位加减、单位+纯数乘法、类型级拒绝"Length 加 Weight"——普通结构体 + 运算符重载就能做到。但复合单位（`Distance / Duration = Speed`）需要为每一对组合手写运算符，类型系统算不出单位乘积；单位换算（kg↔lbs）需要显式系数；数值基类若要泛型化，又回到 Q2 的泛型数学。Anthony 的库级练习**低估了缺口**：能库化的那部分已经库化了，真正值钱的量纲代数恰恰是语言/平台不支持的。

**Q6：真实类型模型的成本。** F# 式的单位系统是大编译器特性：类型级量纲代数、擦除、元数据承载、表达式树规则、IDE 全链路、Option Strict 分叉。F# 自己磨了十年，且主要用户群是科学计算。对一份价值未量化、主线零讨论、F# 只算灵感的特性，按我们"价值 × 成本 × 风险"的尺子，**现在启动是不负责任的**。

**Q7：单位系统"像 VB"吗？** F# 的量纲语法（`[<Measure>]`、`<kg>` 后缀、量纲代数）没有一丝 VB 味。若真做，需要从 VB 的英语读法重新想象（对照模式匹配：主线把 C#/F# 的模式念成 `Matches`/`Case x As T`，见 2018-12-19）。`Weight(in lbs)` 读起来像函数调用，不是 VB 的声明语态。`Not all of us are happy` 于这个方向：有人主张单位安全是脚本/数据产品值得押注的差异化（光），有人主张脚本化受众不付仪式成本（暗）——但**双方都没有数据，也没有可评的设计**，所以我们不在此预支立场。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Weight(in lbs)` 的三处撞车已在 Q4 列出。若走真实类型模型，声明语法完全空缺（F# 用 attribute + 后缀，VB 用什么？attribute？`Structure`？没给）。若走注释模型，`As Double<lbs>` 复用 18.20 的 `As <PurchaseOrder>` 角括号注释文法：

```vb
' 18.20 注释风味：单位是数值变量上的注释，不是真实类型。
' 编译器不检查 kg 与 lbs 混用；weight. 的补全由工具基于单位注释提供。
Sub Ship(weight As Double<lbs>)

' 对照 18.20 的同源注释文法：schema 类型也是注释，不是新类型。
Sub ProcessOrder(order As <PurchaseOrder>)
```

`<` 在类型位置是新文法，但与 XML 字面量的 `<` 上下文可区分（类型位置 vs 表达式位置）。**每一条都可定义，但没有一条是免费的**。泛型运算符若作为 VB 特性发明新文法（而非吃平台 SAIM），则与 `Of`、约束 `As`、变体 `In`/`Out` 全部交互——我们明确不发明。

#### 2. 角案例与边界语义

- **量纲代数**：`kg * m / s^2 = N` 要求类型级计算——单位乘积、幂、倒数（`s^-1`）。这是整个特性最硬的核。
- **无量纲量与零值**：弧度是无量纲单位；`0` 字面量无量纲，F# 里可隐式提升到任意单位，VB 若做需定义字面量提升规则。
- **非线性换算**：`°C ↔ °F` 是仿射不是线性（`* 9/5 + 32`），编译期固定系数系统表达不了；货币是动态汇率（Q1）。**两个最有商业价值的换算都不吃固定系数。**
- **整数单位**：`Integer<kg>` 存在吗？除法的整数舍入与单位组合如何交互？
- **单位消去**：`10<m> / 2<s> = 5<m/s>`，复合单位的消去规则要在类型检查器里实现。
- **属性/getter**：返回单位的属性、单位数组、`Nullable(Of Double<kg>)`——注释模型下这些只是"贴了注释的数值"，真实类型模型下是全新类型族。

#### 3. 作用域与绑定

真实类型模型：语义模型绑定到单位类型，元数据必须携带单位信息（F# 用自定义属性 + 签名文件承载，跨语言基本读不到）。注释模型：语义模型仍绑定 `Double`，注释作为附加信息供工具读取——绑定简单，但"编译器不检查"就是它的代价。两模型都需要在 spec 里定义 `GetTypeInfo` 返回什么。

#### 4. 与既有特性的交互

- **Option Strict On/Off**：真实类型模型只在严格模式有意义——宽松模式下一切退化为 `Object`/晚期绑定，单位是虚构。**单位只参与严格路径**，必须显式规定（这是既有严格/宽松分叉的又一处，可接受但要说清）。
- **数值字面量与运算符**：`12.5 * 2<m>` 结果带单位？混合 `Double` 与 `Double<kg>` 的算术规则？
- **表达式树**：擦除机制下，`Expression(Of Func(Of Double<kg>))` 生成什么？单位信息在树里存活吗？`meeting-runtime-library` 已经演示过表达式树形状是运行时方法改动的硬约束（#218 `Operators.CompareString`）；单位擦除同样要过这一关。
- **ByRef / 泛型 / 可空**：`ByRef` 传单位与底层数值的互转？泛型单位参数？这些全部未定义。

#### 5. Breaking change 与兼容性

全部形态都是新语法，今天写 `As Double<lbs>` 或 `Weight(in lbs)` 是错误→程序，**兼容面为零**，这一条是清白的。注释模型改变 IDE 呈现但不改变行为。唯一反向风险：若真实类型走"擦除为底层数值"的元数据方案，跨语言反射看到的仍是 `Double`——那是互操作问题，不是破坏性变更。无 `Return?` 式的隐蔽语义变化。

#### 6. Option Strict / 编译选项分叉

见第 4 条：单位只在 `Option Strict On` 下有意义。建议对 Option Strict **只字未提**——这是品质扣分项，也意味着所有示例都没在双路径下验证过。严格路径下 `Weight + Length` 编译错误；宽松路径下对象晚期绑定，单位检查无从谈起。需要显式声明"单位是严格模式特性"。

#### 7. IDE / IntelliSense 影响

真实类型模型：量纲错误的波浪线、单位感知的重构、Quick Info 显示单位。注释模型：**整个价值都在 IDE**（补全 + 诊断由工具提供，编译器不参与），这要求编辑器扩展读取注释元数据——18.20 已经规划了这套机制。两者都是全链路 IDE 工作，建议完全没有讨论。

#### 8. 数据 / 普遍性

零数据。主线零讨论；Anthony 自认"not convinced it's a no-brainer"；没有 user-voice 数据。对照 Implicit-default-optional 的 85% 统计标准，这里连"我觉得重要"都只有作者一人。`Suspect`：需求的普遍性无法支撑任何优先级。

#### 9. 更简替代

- **库结构体**（`Structure Weight` + 运算符重载）：同单位安全的切片今天就有，零语言成本。
- **泛型数学 `INumber(Of T)`**：泛型运算符的平台答案，已交付。
- **Analyzer + 属性**：用 attribute 标单位、analyzer 检查混用——不改变语言，纯工具层。
- **注释模型（18.20）**：工具体验，编译器不检查。
- **文档/编码规约**：最便宜，也最弱。

**每个价值切片都有一个更强或同等的既有/规划答案，而语言级真实类型方案是其中最贵的。**

#### 10. 复杂度 / 成本 / 优先级

真实类型系统：极高成本（量纲代数 + 擦除 + 元数据 + IDE），无需求数据，无主线锚点，优先级最低。注释模型：中成本，复用 18.20 机制，贴合产品方向（脚本化 + 数据），可与 annotated-types 绑定推进。泛型运算符：平台轨，VB 无独立成本。对早期产品，**雷（迭代速度）** 比 **光（差异化）** 更值钱，一个投机的大特性会烧掉迭代速度。

#### 11. 运行时 / CLR 硬约束

真实类型模型需要擦除（F# 路线）或元数据承载（自定义属性路线）。擦除是纯编译期概念，PEVerify 无碍；但表达式树、反射、泛型实参、跨语言互操作都需要规则——VB 单位对 C# 来说什么都不是，混合语言生态里这是一个断层。注释模型无运行时影响（纯工具元数据）。不触达 CLR 存储规则，但工程面巨大。

#### 12. 值不值得做

价值（杜绝真实缺陷）× 成本（极高）×× 风险（投机、无锚点、F# 导入未过审）——**现在不值得做**。但注意我们的结论是"现在不值得"，不是"永不"：注释模型提供了一个低成本路径，泛型数学已经清掉了"最小特性"的地基。保持 inactive 并登记激活信号，比 Reject 更符合诚实。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）**：清白——全新增量，零破坏面。
- **原则 2（保持 VB-like）**：正面撞墙。F# 量纲语法与 `Weight(in lbs)` 都不是 VB 语态；若要复活必须从英语读法重新想象（2018-12-19 对 `Matches` 的取舍是正确参照：命名过一遍审美关）。
- **原则 3（不引入第二种做事方式）**：真实类型模型 = 数值类型系统之外并行的一套单位类型族；注释模型 = 类型风味系统（18.20）的一部分。两者都扩展表面积。
- **原则 4（默认跟随 C#）**：C# 没有单位特性，但 C#/CLR 给了泛型数学。**VB 应该骑在平台泛型数学上，而不是发明自己的泛型运算符文法**——这是本建议最干净的一条对齐。
- **原则 5（读起来像英语）**：`Weight(in lbs)` 读起来像配方，但未定义；真正的 VB 化读法不存在。
- **原则 6（不为边缘场景加特性）**：单位是垂直面（物理/工程），不是横向需求；货币这个横截面需求又吃不了固定系数。
- **原则 7（避免隐蔽语义变化）**：不适用，无控制流。
- **原则 8（不与既有语法冲突）**：`in`/`In`/`Out`/`Of` 四重撞车，本建议唯一的具体语法全中。
- **原则 9（消除常见样板）**：今天的库方案样板在**库作者**身上，不在用户身上；泛型数学已经砍掉其中一部分。单位语法是给用户加仪式，不消样板。
- **原则 10（冗长只在有用时是美德）**：单位注释是否值得它加的仪式，无证据。

对照主线表（2.3）：度量单位在主线对照表**没有对应行**——纯 **Anthony 独立延伸**（F# 灵感）。泛型运算符是平台/C# 轨（符合"跟随 C#"的委托）；注释模型归属 18.20（同为 Anthony 独立延伸的"类型风味"伞下）。**本建议与主线不冲突，但也零锚点；它是最纯粹的沙盒式激进延伸样本。**

### RESOLUTION:

**RESOLUTION:** We keep the proposal inactive. This is not a feature we can activate: there is no design to evaluate, no demand data, no main-line anchor, and its only "uncontroversial" conclusion has been delivered by the platform. Neither do we reject it — the space is worth a dedicated posting, and the annotation model gives a low-cost path aligned with our product.

1. **三态判定：`Table`（保持 inactive），不 Reject。** 理由：占位文档无设计、无需求数据、无主线锚点、唯一"无争议"结论已由平台交付。但 F# 灵感的方向正当，18.20 注释模型提供了低成本延续路径；Reject 是过度反应。
2. **"泛型运算符"子项移交平台轨，不再作为 VB 特性候选。** 能力由 .NET 泛型数学（SAIM / `INumber(Of T)`）覆盖，设计权属 C#/运行时。VB 侧动作：审计 `As INumber(Of T)` 约束在 VB 的语法/IntelliSense 支持是否完备（`Probably` 已支持，需验证），而不是发明新的泛型运算符文法。
3. **语法糖 `Weight(in lbs)`：按"否决的备选语法"留档。** 与 `In`/`Out` 变体修饰符、`For Each In`、泛型 `Of` 文法四重撞车，语义未定义，且 Anthony 自认非必需。若未来单位系统成立，语法重新设计并过"像不像 VB"审美关（参照 2018-12-19 模式匹配关键字的取舍）。
4. **两模型保持开放，注释模型（18.20）优先考察。** 注释模型符合 VBScript.NET 脚本/数据受众、复用 annotated-types 机制、成本中；真实类型模型无需求数据，不启动。两者**不得混为一谈**：一个是编译期安全，一个是工具体验。
5. **激活信号（真实类型模型，缺一不可）：**
   ① **一份专门的深入设计贴**（Anthony 自己要求的），完整设计两模型之一（建议先做注释模型），含文法、语义、角案例。
   ② **库原型证据**：演示现有库方案能走多远、复合单位缺口在哪，证明"哪些语言特性真的必需"（是否泛型数学就够）。
   ③ **需求量化数据**：单位混用/样板在真实代码中的占比，或至少一个付费用户的强需求。
   ④ **元数据/跨语言互操作方案**：真实类型模型的擦除或属性承载，表达式树与 C# 互操作规则。
   ⑤ **Option Strict 分叉与 IDE 全链路**：明确"单位是严格模式特性"及工具侧工作。

### Implication:

- 在 `annotated-types` 工作项登记依赖：若注释类型（类型风味）落地，**单位注释是它的第一个用例候选**；两建议共享同一套角括号注释文法，不得各造一套。
- 向平台轨登记：VB 对 SAIM/`INumber(Of T)` 的约束支持审计（`Probably` 支持，需验证），确认泛型数学在 VB 的可用面。
- 关闭本建议的"泛型运算符"独立条目，指向平台泛型数学；把语法糖归档进"OTHER DESIGNS CONSIDERED"。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 整体 `Table`（保持 inactive）。PROPOSAL D 为当前裁决；PROPOSAL C（注释模型）是唯一有复活候选路径的方向，随 18.20 走；PROPOSAL B 的平台部分移交，VB 部分关闭；PROPOSAL A 待激活信号。

### OPEN QUESTIONS / TODO / Follow-up

- [ ] 真实类型 vs 注释模型的取舍：倾向注释模型（18.20）先行，但真实类型模型的成本/价值对比表未做。
- [ ] VB 对 `As INumber(Of T)`（static abstract interface members）约束的完整支持程度——`Probably` 支持，需编译器验证。
- [ ] 复合单位（`Distance / Duration = Speed`）在库方案里的确切缺口演示——这是"哪些语言特性真的必需"的证据核心。
- [ ] 货币（动态汇率）与温度（仿射换算）在固定系数量纲系统里的表达——最可能的商业用例恰好吃不到固定系数。
- [ ] 单位擦除在表达式树、反射、泛型实参中的规则（真实类型模型）。
- [ ] "单位是严格模式特性"的 Option Strict 分叉声明。
- [ ] 单位注释与 `As <PurchaseOrder>` 注释文法（18.20）的统一语法——若各造一套即违反"不引入第二种做事方式"。

### 状态

- **LDM 状态：`LDM No Plans`（保持 inactive）。** 本建议不排队进入任何版本的候选集；其泛型运算符子项已由平台轨消化，注释子项挂 18.20。
- **三态判定：Table** — 无设计、无数据、无锚点；唯一结论归属平台。不 Reject：F# 灵感方向正当，注释模型是低成本延续，Anthony 自己要求的"dedicated and detailed in-depth posting"就是我们登记的激活信号①。

---

## 附录：特性评价

### 评价对象

- 建议：`inactive/proposal-units-of-measure.md` — 度量单位 / Units of Measure。
- 来源：Anthony 原文第 **18.19 节 "Units of Measure?"**（`..\..\AnthonyDesign_wordpress.txt` L3078–3086）；与 18.20（L3150）"annotation on a numeric variable rather than a real type"相互引用。
- 配方目标：评估"VB 用户如何仅靠库实现单位系统、或最少加什么特性使体验可接受"；结论（泛型运算符）与语法糖 `Weight(in lbs)` 均为 18.19 原文内容。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。动机诚实（作者自认"not convinced it's a no-brainer"）但无目标改进；唯一示例 `Weight(in lbs)` 是不能编译的语法片段；无原型。改进的"显现"定义不清——编译期安全 vs 工具体验二选一都没定 | 已检查 | 无原型；无工作示例；唯一的"结论"（泛型运算符）是平台特性而非 VB 改进；未决关键点 ≥4 → 效果证据封顶 |
| 特性 | **2/5** | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。单位概念整体借自 F# 未 VB 化；语法糖是函数调用式外来语态；与 `In`/`Out`/`Of` 撞车；真实类型 + 注释两模型（两种不同价值主张）被捆绑进一份文档 | 已检查 | 无 VB 基因延续；无主线锚点（对照表 2.3 无对应行）；泛型运算符误判为 VB 小特性（实为平台大特性） |
| 品质 | **3/5** | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、Drawbacks/Alternatives/Unresolved 诚实（不虚构语法，4 个未决点具体）——加分；但 Detailed design 是占位（原文明确无完整 VB 示例），无文法、无 Option Strict、无兼容性小节，状态栏为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 红旗：占位链接；Detailed design 空；语法糖无语义；两模型不加区分；依赖（18.20 注释文法、泛型数学平台轨）未声明 |
| 属性 | **2/5** | 锚点 2："某关键维度受损且无应对"。对 VBScript.NET 早期产品：暗=投机大特性 + F# 导入无数据烧掉迭代速度（雷）；风=无主线锚点、泛型运算符平台归属误判（一致性断裂风险）；光=单位差异化潜力存在但未挣得，无对冲设计。诚实性是唯一缓解 | 已检查（预测性，标"待定"） | 与平台轨重叠未识别；注释模型与 18.20 的复用关系未交代；无任何对冲 |
| 炼金成分 | **3/5** | 锚点 3："部分来源未标注；标注与影响有偏差"。F# 灵感（18.19）与 18.20 交叉引用标注正确；但泛型运算符的平台属性未声明（误标为 VB 特性候选）；F# 导入未过"只作灵感参考"这一关；"无争议"结论未经核查 | 已检查 | 泛型运算符成分实际影响（平台轨）与文档预估（VB 特性）明显不符；无杂质（未借鉴闭源） |

### 设计原则对照

- **与 VB 基因**：**偏离为主**。一致：原则 1（零破坏面）、原则 4（跟随 C#/CLR 的泛型数学轨）。偏离/触墙：原则 2（F# 量纲语法与 `Weight(in lbs)` 非 VB 语态）、原则 3（并行单位类型族/类型风味系统）、原则 6（垂直面特性）、原则 8（`in`/`In`/`Out`/`Of` 四重撞车）、原则 9（给用户加仪式不消样板）。
- **与主线关系**：**Anthony 独立延伸**（对照表 2.3 无对应行，纯 F# 灵感）。泛型运算符子项 = 平台/C# 轨（委托一致）；注释模型子项 = 18.20（同为 Anthony 独立延伸）。与主线不冲突，但零锚点。
- **破坏性变更**：**无**（全为新语法，"错误→程序"）。无隐蔽语义变化。真实类型模型的元数据方案是互操作问题，非破坏。

### 总评

- **达成程度**：**未达成**（作为规范文本）。这是一份诚实的"思考状态"记录，不是设计：无 Detailed design、无工作示例、无需求数据；唯一"无争议"结论是平台特性；唯一具体语法未定义且撞车。
- **LDM 三态建议**：**Table（保持 inactive）**。泛型运算符移交平台轨；语法糖按否决留档；注释模型（18.20 方向）为唯一复活候选路径；真实类型模型待激活信号五条。
- **主要问题**：(1) 无设计可评——这是决定性的；(2) 泛型运算符被误判为 VB 小特性，实为平台特性；(3) 真实类型 vs 注释两模型混为一谈；(4) `Weight(in lbs)` 与 `In`/`Out`/`Of` 四重撞车且无语义；(5) 无需求数据、主线零讨论；(6) 货币/温度这两个最有商业价值的换算恰好吃不到固定系数。

### 返工建议

- **范围切分**：拆成「泛型运算符（→平台轨）」「语法糖（→否决留档）」「真实类型单位系统（→待激活）」「注释模型（→移交 18.20）」四块分别裁定，不再捆绑。
- **补充章节**：择一模型做完整的 Detailed design（建议注释模型先行，对齐 `As <PurchaseOrder>` 文法）；文法；Option Strict 分叉（"单位是严格模式特性"）；Compatibility/互操作（元数据承载、表达式树、跨语言）；与 18.20 的对表。
- **补充证据**：库原型（`Structure Weight` 走多远、复合单位缺口的具体代码演示）；泛型数学（SAIM/`INumber(Of T)`）在 VB 的约束支持审计；需求量化数据；货币/温度换算的案例研究。
- **未决问题处理**：两模型按"注释模型优先、真实类型待信号"定夺方向；`Weight(in lbs)` 归档为否决语法；泛型运算符关闭并指向平台；激活信号五条逐条登记为 TODO（RESOLUTION 第 5 条）。

---

## 附录：C# 生态与互操作考量

> 本文档为本系列为 ModVB 提案追加的 C# 生态背景附录，索引依据 `..\..\..\csharplang-index.md`（来源目录 `..\..\..\csharplang`，dotnet/csharplang 官方仓库镜像）。本提案（度量单位）与 C# 的关系是 102 个提案里**最弱的一档**：对 `..\..\..\csharplang` 全库 Grep `unit of measure` / `Units of Measure` / `measurement unit` 零命中（已核实），决策文件 M6 判定"C# 无对应，F# 有，是 VB 可保留的特色，但 CLR 元数据无天然表达"。本附录因此不硬凑 C# 对应物，而是如实回答：C#/CLR/.NET 生态里单位问题**由谁承担、承担得怎样**，以及 VBScript.NET 若激活本提案需要桥接什么。

### 相关 C# 现实方向

C# 侧没有单位系统，也没有单位系统的候选提案（全库检索零命中，已核实）。但本提案唯一"无争议"的结论——泛型运算符——在 C#/CLR 生态有明确的平台对应；另有三条生态背景线值得记录。

1. **泛型数学（static abstract interface members / SAIM，C# 11，.NET 7）——本提案"泛型运算符"结论的平台对应。** 提案 RESOLUTION 第 2 条已把"泛型运算符"移交平台轨。C# 侧原文依据（逐字核实）：

   > "There is currently no way to abstract over static members and write generalized code that applies across types that define those static members. This is particularly problematic for member kinds that *only* exist in a static form, notably operators."
   > "This feature allows generic algorithms over numeric types, represented by interface constraints that specify the presence of given operators."
   → `proposals\csharp-11.0\static-abstracts-in-interfaces.md`（Motivation，逐字核实）

   VB 侧消费形态即 `As INumber(Of T)` 约束（`System.Numerics.INumber(Of T)`，BCL 交付，非 csharplang 仓库内容）。该特性设计讨论见 → `meetings\2021\LDM-2021-04-05.md`（static abstract 运算符示例；未逐字引用正文）。

2. **C# 数值底层类型方向：原生整数（nint/nuint，C# 9，C# 11 数值化）。** C# 对数值类型的投入集中在"原生大小整数 / 句柄 / 指针"，而非"单位感知数值"。动机原文（逐字核实）：

   > "The motivation is for interop scenarios and for low-level libraries."
   → `proposals\csharp-9.0\native-integers.md`（Summary，逐字核实）

   这印证 C# 数值方向的优先级在互操作与低层库，与"编译期量纲安全"正交——C# 不会替 VB 铺单位这条路。

3. **F# Units of Measure——.NET 生态里唯一的语言级先例，且不在 csharplang。** F# 是 .NET 生态唯一做语言级单位系统的语言：`[<Measure>] type kg` 声明量纲、值写作 `1.0<kg>`、编译期量纲代数、运行期擦除为底层数值（`float<kg>` 运行时即 `float`）。该机制属 F# 语言设计，**不在 dotnet/csharplang 仓库**——本附录无法给出 C# 原文出处；机制描述沿用本 meeting 正文"场景与缺口"段（平台常识，非 vblang 内容）。C# 生态对单位的主流表达是**泛型包装/库**（`struct` + 运算符重载的量纲库，无语言参与）——这正是本提案 PROPOSAL B / Q5 库路线的现实写照，两语言在复合单位缺口上处境相同。

4. **C# 15 unsafe-evolution 带来的元数据表面扩张——对"编译期特性 + 元数据承载"类 VB 特性的背景压力。** unsafe-evolution 明确 VB 不参与 requires-unsafe（逐字核实）：

   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   → `proposals\unsafe-evolution.md`（VB 小节，逐字核实）

   但同一提案规定：按新 memory-safety-rules 编译的模块由编译器合成模块级 `MemorySafetyRulesAttribute`（标记语言版本），requires-unsafe 成员由编译器合成 `RequiresUnsafeAttribute`（`proposals\unsafe-evolution.md`，Metadata 小节，已核实）。决策文件 M8 已判定这是 VB 编译器**必须桥接**的点：不识别这些新元数据就无法正确校验"调用 C# requires-unsafe 成员"的安全性。

### 现实 vs 提案

| 本提案成分 | C#/CLR/.NET 现实 | 判定 | 理由 |
|---|---|---|---|
| 泛型运算符（唯一"无争议"结论） | C# 11 SAIM 泛型数学，`INumber(Of T)` | **兼容（已由平台交付）** | RESOLUTION 第 2 条已移交平台轨；VB 只需跟上，不发明文法 |
| 真实类型单位系统（PROPOSAL A） | C# 无对应；F# 有先例；CLR 元数据无天然表达 | **脱节（VB 独有特色，非 C# 轨）** | 擦除/属性承载跨语言基本读不到；"VB 单位对 C# 来说什么都不是"（正文第 11 条） |
| 注释模型（PROPOSAL C / 18.20） | 无 C# 对应；纯工具层元数据 | **脱节但零冲突** | 不改变类型系统、无运行时痕迹，与 C# 互操作无摩擦 |
| 库级 `Structure Weight` 路线 | C# 生态主流（泛型包装/库） | **兼容** | 同一套路，C# 库已证明可行；复合单位缺口两语言同样存在 |

结论：**本提案与 C# 的主要关系不是"冲突"而是"缺席"**——C# 侧没有单位系统，因此没有需要对抗的 C# 方向；真正的约束是 **CLR 元数据**（跨语言看不到单位）与 **平台泛型数学**（已吞掉唯一"无争议"结论）。

### 对 VBScript.NET 的适应建议

1. **"默认安全、按需动态"总方针在本提案最轻量。** 单位只在 `Option Strict On` 严格路径有意义（正文 Q6 / 第 6 条），与 VBScript.NET 双模路线天然吻合：严格脚本吃量纲检查，宽松脚本退化为数值——无需额外桥接。
2. **吃平台泛型数学，不发明 VB 泛型运算符文法。** 审计 `As INumber(Of T)` 约束在 VB 的语法/IntelliSense 支持完整度（RESOLUTION 第 2 条，`Probably` 需验证），让 VB 直接消费 SAIM 能力。
3. **若激活注释模型（18.20）：单位注释是纯工具元数据，跨语言零摩擦。** 注释不改变类型系统、不生成运行时痕迹——这是本提案在"默认安全 + 现代 .NET"框架下成本最低、与 C# 生态最无害的落点。
4. **若激活真实类型模型：必须把"跨语言/元数据"当一等公民设计。** 参照 F# 先例，擦除或属性承载二选一都需定义反射 / 表达式树 / 泛型实参规则（正文 OPEN QUESTIONS 第 5 条）；且要避开 unsafe-evolution 后膨胀的元数据表面——单位属性（若有）不得与编译器合成的 `RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute` 冲突。source-gen 桥思路（对齐索引 T6 方向）：用增量生成器把"单位→数值"的换算/校验生成到强类型方法，减少运行时反射依赖。
5. **识别 C# 15 新元数据是并行必做项（决策文件 M8），与单位无直接关系。** VBScript.NET 若要消费 .NET 11+ 的 C# 成员，必须识别 `MemorySafetyRulesAttribute` / `RequiresUnsafeAttribute`（→ `proposals\unsafe-evolution.md`）。

### 对既有 RESOLUTION / 三态判定的影响

**无实质影响。** 本附录确认了 RESOLUTION 的既有判断，不改写三态判定：

- RESOLUTION 第 2 条（泛型运算符移交平台轨）得到 C# 侧原文支持：SAIM 动机正是"generalized code ... across types"与"generic algorithms over numeric types"。
- RESOLUTION 第 5 条激活信号④（元数据/跨语言互操作方案）在 C# 生态侧无可借力——C# 无单位系统可对齐，F# 先例跨语言读不到。这印证该信号必须由 VB 侧自行设计，而非等待 C# 铺路。
- 三态 `Table`（保持 inactive）不因 C# 生态而改变：C# 侧的"缺席"既不是反对理由也不是支持理由。

### 引用纪律

- **逐字引用（已核实）**：SAIM 动机两段 → `proposals\csharp-11.0\static-abstracts-in-interfaces.md`（Motivation）；nint 动机 → `proposals\csharp-9.0\native-integers.md`（Summary）；unsafe-evolution VB 表态 → `proposals\unsafe-evolution.md`（VB 小节）。元数据属性事实（`MemorySafetyRulesAttribute` / `RequiresUnsafeAttribute` 编译器合成）→ `proposals\unsafe-evolution.md`（Metadata 小节）。
- **会议引用**：SAIM 设计讨论 → `meetings\2021\LDM-2021-04-05.md`（static abstract 运算符示例；未逐字引用正文）。
- **OPEN QUESTIONS**：
  - F# Units of Measure 的精确元数据编码（measure 属性如何落盘、C# 反射能看到什么）——本附录未在 csharplang 核实，属 F# 语言设计 / `.fsi` 签名文件领域，需查 dotnet/fsharp 或 F# spec 才能定论。
  - .NET 泛型数学 `INumber(Of T)` 在 VB 的约束支持完整度（RESOLUTION 第 2 条，`Probably`，待编译器验证）。
  - C# 生态量纲库（如 UnitsNet 类）的元数据形态是否可作为 VBScript.NET 注释模型的参照——本附录未深挖，标 **Suspect**。
