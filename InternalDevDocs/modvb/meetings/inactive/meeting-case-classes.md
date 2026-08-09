# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天评审的是一份**特殊的 inactive 建议**：它是 Anthony 全稿的最后一节（18.21），全文只有一张截图、一个命名直觉和一句"I'll dive into this another day, but sooner than later."——连 Anthony 自己都承认对判别联合 "I mostly don't" get it。我们照例按"评审一份真实建议"对待它，而不是因为它在 inactive 文件夹就跳过：问题不是"要不要现在做"，而是**这份建议有没有值得激活的信号，还是应该继续搁置**。

诚实声明：正文区分 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。引用的主线会议引文与 issue 编号均与 `..\..\..\vblang` 原文逐字一致；标注 `Suspect` 的判断是基于设计原则的独立推理，非主线原文，也**不是** Anthony 截图的转录。

## Agenda

* [ModVB Proposal — Case 类、结构与接口（Case Classes, Structures, and Interfaces）](#modvb-proposal--case-类结构与接口)

## ModVB Proposal — Case 类、结构与接口

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf（已并入模式匹配）](https://github.com/dotnet/vblang/issues/304)；[LDM-2014-02-17](../../../vblang/meetings/2014/LDM-2014-02-17.md)（`Select Case Typeof` 预览、`Case b As Button`）；[LDM-2014-04-23](../../../vblang/meetings/2014/LDM-2014-04-23.md)（records / `<expr> matches T(args)`）；[vbldm-notes-2018.12.19](../../../vblang/meetings/2018/vbldm-notes-2018.12.19.md)（模式匹配文法与穷尽性决议）；AnthonyDesign section 18.21（`..\..\AnthonyDesign_wordpress.txt` L3200–3212）· ModVB `proposal-case-classes.md`、`meeting-shapeof-pattern-matching.md`、`meeting-named-patterns.md`、`meeting-intersection-union-types.md`_

### 场景与缺口

We opened by acknowledging what this proposal actually is——**一个命名 + 一个截图 + 一个自我否定**。Anthony 18.21 原文：

> "I've known 'discriminated unions' were on the 'look at this one day' list for dotnet (like many F# features) so I have been watching functional videos and reading articles to appreciate them. And I mostly don't. But I did see 2 examples that cracked the door and I've reframed the issue for myself and that has produced some syntax sketches that I'll share for illustration purposes."（事实，Anthony 18.21）

"撬开门缝的 2 个例子"是理解这份建议价值的唯一钥匙——而它们**在截图里**，原文没有转成文字，我们也看不到。这是全场反复回到的一个点：我们评审的是一份作者本人都不确定、且把关键论据留在图片里的建议。

但把概念剥离出来，它瞄准的缺口是真实的。今天的 VB 表达"运行时按形状分发到一组互斥分支"长这样：

```vb
' 现状（今天就能编译）：开放类型体系 + TypeOf/DirectCast 分发。
Public Class Shape
End Class

Public Class Circle
    Inherits Shape
    Public ReadOnly Property Radius As Double
        Get
            Return _radius
        End Get
    End Property
    Private _radius As Double
End Class

Public Class Rectangle
    Inherits Shape
    Public ReadOnly Property Width As Double
    Public ReadOnly Property Height As Double
End Class

Function Area(shape As Shape) As Double
    If TypeOf shape Is Circle Then
        Dim c = DirectCast(shape, Circle)
        Return Math.PI * c.Radius ^ 2
    ElseIf TypeOf shape Is Rectangle Then
        Dim r = DirectCast(shape, Rectangle)
        Return r.Width * r.Height
    Else
        Throw New NotSupportedException()
    End If
End Function
```

`Select Case` 是 VB 的惯用分发语句，但它今天只做等值、范围与 `Is` 比较，不做类型分发。这个缺口我们不是第一次见了——2018.05.30 我们把它并入模式匹配："Will consider part of pattern matching."（事实，Issue #304）；2018.12.19 我们明确 "We all want to do pattern matching, it's the thing we are most excited about after C# interop issues."（事实）。

所以第一层结论是清楚的：**Case 类想解决的"类型分发"缺口，模式匹配家族已经接管了**。今天的会议要回答的不是"缺口在不在"——那没有争议——而是**在模式匹配之外，`Case Class` 还剩下什么独有主张**。

### 候选方案

**PROPOSAL A — 激活本建议，把 `Case Class / Case Structure / Case Interface` 设计成正式语言特性。**

按建议原文的方向走：`Case` 前缀修饰既有 `Class`/`Structure`/`Interface` 声明，表达"封闭的、按形状分发的类型族"。这是对判别联合的重新框定——不引入新的 sum 类型构造器，而是给既有声明形式加一个修饰符。

**PROPOSAL B — 保持 inactive；把类型分发场景完全交给模式匹配家族。**

`Select Case shape / Case c As Circle / Return Math.PI * c.Radius ^ 2` 的声明模式（2014-02-17 预览 `Case b As Button ' now works`，事实；2018.12.19 文法 `'As' TypeName` 类型检查模式，事实）已经覆盖"运行时判型 + 绑定强类型变量 + 分发"。Case 类在这个世界里没有独立位置。

**PROPOSAL C — 把本建议重构成"封闭家族（closed family）"的显式设计。**

只保留 Case 类概念里的"封闭"部分：编译器知道某类型族的所有分支，从而能做**穷尽性检查**。这是 Case 类相对模式匹配的**唯一增量**——模式匹配不要求封闭（2018.12.19 的声明模式对开放类型体系照常工作）。

**PROPOSAL D — 只保留 `Case` 命名，并入模式匹配家族作阅读建议。**

不引入新类型语义，仅把 `Case c As Circle` 这种"分支即类型"的读法发扬光大——`Case` 已经在 `Select Case` 里意味着"一个分支"，让类型也读作分支。

### 权衡：LDM 追问清单

We worked through the twelve-question checklist. 决定性的问题是 1、2、4、5、8、9、11。

#### 1. 语法 / 文法歧义

`Case` 是 VB 的**保留字**（`Select Case` 使用），所以 `Case Class Circle` 不会把合法标识符保留字化——这点比 `ShapeOf`/`Matches`（新关键字，有 identifier-collision 风险）干净。但代价是另一面：`Case` 今天的唯一语法位置是 `Select Case` 语句内部；把它用作**类型声明前的修饰符**，是 VB 从没有过的语法角色。解析器要在"类型声明上下文"里识别 `Case`，而 `Case` 现在进入不了任何类型声明的开始。这是新增文法，不是歧义化旧文法——可解析，但管道成本真实（parser→binder→symbol 四层都要认识一个新修饰符）。

第二层歧义更麻烦：`Case Class`、`Case Structure`、`Case Interface` 三个变体分别意味着什么？`Case Structure` 里的 `Structure` 本来就不可继承（值类型无继承），"封闭"对它没有增量含义——那 `Case Structure` 是声明"这是一个分支值类型"，还是只是把 `Structure` 换个写法？`Case Interface` 呢？接口的分支语义（"实现这个接口的众多类型都是分支"）与"接口本身是一个分支"是两回事。建议原文一个都没定义。

**文法上的自由也许是好事，但语义上的自由是致命的。** 名称已经有了，语法位置有了（声明前缀），可"`Case` 到底改变了一个类型声明的什么"——没有任何文字。

#### 2. 角案例 / 边界语义

**封闭性（closedness）是根本冲突。** `Case Class` 预设编译器知道一个家族的所有分支——但 VB 的 `Class` 默认开放，任何程序集都可以 `Inherits` 一个公开类：

```vb
' 今天的等价物：NotInheritable 已经表达"不能有子类"。
Public NotInheritable Class Circle
    Inherits Shape
End Class

' 若 Shape 被标成"Case 类家族"，今天合法的继承——
Public Class MyCircle
    Inherits Shape
End Class

' ——要么被禁止（破坏性变更），要么封闭性只是一个编译器约定，
' 而 CLR 元数据里根本没有"封闭类型族"这种东西可写。
```

`NotInheritable`（sealed）今天就能表达"此类型不可派生"。`Case Class` 要增加的不是密封本身，而是**"编译器知道整个家族、可穷尽"**。CLR 无法表达"类型族封闭"——除非编译器用特性（attribute）或元数据自行登记，而这恰恰是 2014-02-17 我们警惕的那类"语言特性依赖编译器私有的元数据约定"。穷尽性检查本身是我们明确拒绝过的：2018.12.19 "If you mean exhaustiveness of cases in `Select Case`, we don't have this today and introducing it is backwards breaking."（事实）。

**值类型分支。** `TypeOf shape Is Circle` 只作用于引用类型与接口——2018.05.30 Issue #305 我们确认过 "TypeOf...Is is not an exact match, but whether a cast can occur"（事实）。`Case Structure` 的值类型分支要靠装箱 + `Equals`/模式，是整片未开发领域（我们的流分析会议与交/并类型会议都撞过同一堵墙）。建议原文没提。

**泛型。** 2014-02-17 对 `Case As IEnumerable(Of T)` 的判例直接适用："Answer: this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection. Therefore this scenario is out of consideration."（事实）。Case 类家族若支持开放泛型，同样撞墙；若只支持封闭泛型，表达能力骤减。

**`Nothing` 分支、Partial 类、跨程序集家族。** `Case Class` 能 Partial 吗？封闭家族 + Partial 是概念矛盾。家族分支能分布在不同程序集吗？能，但封闭性检查就跨程序集了。全部未定义。

#### 3. 作用域与绑定

`Probably`：若 `Case Class Circle` 只是"Circle 是 Shape 家族的一个分支"，那它不引入任何新绑定——`Circle` 的成员查找、构造、继承都照旧，`Case` 只是给编译器一个"这个类型属于某封闭族"的登记。若是这样，它就只是一个**元数据标注**，几乎不带语言语义。若 `Case` 还要改构造（比如像 C# records 自动生成属性/`Equals`/`GetHashCode`——2014-04-23 我们讨论过，"questions raised, heated discussions, and no agreed-upon answers"，事实），那它就变成记录（records）的变体——而记录本身从未被我们批准。两条路之间，建议没有任何线索告诉我们走哪条。

#### 4. 与既有特性的交互

**这是本场最关键的交火点。** 模式匹配家族已经给出了"类型分发 + 绑定 + 分发"的完整答案：

```vb
' 模式匹配家族（沙盒内已设计；`Case c As Circle` 源自 2014-02-17 预览 `Case b As Button ' now works`
' 与 2018.12.19 文法 `'As' TypeName`，均为事实）。
Function Area2(shape As Shape) As Double
    Select Case shape
        Case c As Circle
            Return Math.PI * c.Radius ^ 2
        Case r As Rectangle
            Return r.Width * r.Height
        Case Else
            Throw New NotSupportedException()
    End Select
End Function
```

Case 类相对它**只增加**：封闭性（编译器知道没有别的分支）与穷尽性。而穷尽性是 2018.12.19 明言拒绝的；封闭性是 CLR 表达不了的。也就是说——**Case 类想加的两样东西，一样我们明确不要，一样 CLR 给不了。** 2014-02-17 的那句话此刻显得格外刺眼："Pattern-matching is odd in a language without algebraic datatypes, because there's no inbuilt canonical way to decompose an object."（事实）——Case 类想做那个"canonical way"，但同一场会议我们也列出了四种分解方案（主构造函数、属性名、Scala Unapply、F# active patterns），且从未批准任何一种成为语言设施。

**与 ad-hoc 并集类型的三角关系。** Anthony 自己还有另一条线：section 14 的 `Let u As {IDisposable Or ICloneable}`（`..\..\AnthonyDesign_wordpress.txt` L2392–2393）。我们在 `meeting-intersection-union-types.md` 已经裁决：**并集 Table，并入模式匹配工作项**。现在同一个"sum"缺口上有三份建议——并集类型（类型表达式层）、Case 类（类型声明层）、模式匹配（分发语法层）——在抢同一片地。三份里模式匹配是主线、另两份是 Anthony 独立延伸，且互相没有交叉标注。这是家族管理问题，不是一个特性问题。

**与 `Select Case` 既有子句的互动。** 2018.12.19 对逗号有决议："Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax."（事实）——Case 类若引入"分支类型"的概念，与 `Case` 逗号合并（`Case 3, As String`）的关系又是 Design1/Design2 那桩 2014-02-17 悬案。We are not reopening that today.

#### 5. Breaking change 与兼容性

对现有代码**无直接破坏**：`Case` 是保留字，`Case Class` 位置今天必然是语法错误，新增不影响旧形态。但语义层面的破坏风险很高，且是"限制型"破坏：一旦某个公开类型被标成 Case 类家族，**今天合法编译的派生类明天会报错**。任何"封闭化"一个既有类型的方案都是把可编译代码变不可编译——这与 2018.12.19 "No breaking changes."（事实）直接冲突。即使只对新类型生效，`langversion` 门控也解决不了"同一家族里新旧类型混合"的问题。We think 这是比新关键字更隐蔽的破坏：不是语法撞车，是**继承权利的收回**。

#### 6. Option Strict / 编译选项分叉

Case 类若只做封闭性登记，两条路径行为一致（无新绑定规则）。但若带值类型分支或自动生成成员（records 路线），`Option Strict Off` 下宽松路径与严格路径的分叉必须一次定清——尤其 VBScript.NET 默认宽松。建议没有任何分析。`Suspect`：值类型分支在宽松路径下的收窄/装箱行为是这条特性最容易语义漂移的地方，且完全未触碰。

#### 7. IDE / IntelliSense

穷尽性若做，IDE 要显示"分支未穷尽"的标记与补全家族成员列表——这是 2018.12.19 明确不为 `Select Case` 引入的行为（穷尽性 backwards breaking），给 Case 类做就是给模式匹配明确拒绝的东西做 IDE 投资。若不穷尽，IDE 增量只是"把 `Case Class` 显示成一个修饰符"，收益极小。

#### 8. 数据 / 普遍性

零数据，而且是本沙盒里证据最薄的建议之一。作者自己 "I mostly don't"（事实）；"撬开门缝的 2 个例子"在截图里没转述；没有任何用户请求、issue 统计或场景量化。2018.05.30 我们确认 "there are hundreds of thousands of quiet customers each month [who] primarily want VB to keep doing what it does now"（事实）——"封闭类型族"显然不在安静客户的日常里。模式匹配家族是"最期待"的特性，Case 类是它身后一个没有证据的影子。`Probably`：真实代码里对"穷尽性"的刚性需求，远低于对"少写一次 DirectCast"的需求，而后者模式匹配已覆盖。

#### 9. 更简替代

- **模式匹配声明模式**（`Case c As Circle`）：覆盖"判型 + 绑定 + 分发"，机制小得多，且是主线方向。
- **`TypeOf ... Is` + 流分析**（我们自己的 Active 建议）：守卫惯用法零新语法。
- **`NotInheritable` + 文档约定**：封闭性在单类型层面今天就有，家族级封闭是"编译器登记"的额外负担。
- **Analyzer**：穷尽性检查**完全可以做成 analyzer**——它只需要"读取类型族登记 + 报告未穷尽分支"，不占语言表面积、没有 breaking change、不碰 CLR 元数据约束。这是本场最有力的一条替代论证：**Case 类唯一独有的价值（穷尽性）恰恰是 analyzer 能给的**。
- **ad-hoc 并集类型**（`{A Or B}`）：已 Table，并入模式匹配。

#### 10. 成本 / 优先级

激活 = 从一张截图设计出整套类型系统特性：封闭性机制（same-file? same-assembly? attribute?）、穷尽性检查（我们已拒绝）、值类型分支（新机制）、records 式成员生成（未批准）——每一项都是中到高成本的设计工作，而建议给我们的设计输入是零（截图不可转录为文法）。成本高、输入为零、价值重叠。优先级：**在模式匹配家族（Phase 1 声明模式已 Active）落地之前，Case 类没有任何先于它的理由。**

#### 11. 运行时 / CLR 硬约束

封闭性在 CLR 元数据中无对应物。穷尽性要编译器维护"家族成员表"，要么编译期在同一编译单元内扫描（限同文件/同程序集），要么写进特性由运行时反射读取——2014-02-17 的反射红线："we wouldn't want a language feature that depended on reflection"（事实）。值类型分支碰装箱。无 PEVerify 问题，但"封闭"本身就不是 CLR 概念。**这条特性从第一天就活在 CLR 的表达能力之外。**

#### 12. 值不值得做

逐条打分（价值 × 成本 × 风险）：
- **价值**：类型分发的部分被模式匹配接管（价值≈0 增量）；穷尽性部分有价值但被 2018.12.19 拒绝、且 analyzer 可替代（价值≈2）；命名美感（`Case Class` = "分支类"）有价值但不足以撑起特性（价值≈1）。
- **成本**：从零设计整套类型系统特性（成本=9）。
- **风险**：继承权利收回的破坏性（风险=7）；与模式匹配/并集三线纠缠（风险=5）。
- **打分**：价值 2 × 成本 9 × 风险 7 → **不值得现在做，没有任何激活信号。**

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 语法层无破坏（`Case` 保留字、新位置），但"封闭化"会收回今天合法的继承权——限制型破坏，与 2018.12.19 "No breaking changes." 冲突。
2. **保持 VB-like** — `Case Class Circle` 的字面读法"这是分支类"实际上**很 VB**——`Case` 在 `Select Case` 里本来就是"一个分支"，把它接到类型声明上是自洽的命名直觉，这是本建议唯一真正的资产。可惜只有命名，没有内容。
3. **不引入"第二种做事方式"** — 违反。类型分发已有 `TypeOf`/模式匹配两条路，Case 类是第三条。
4. **默认跟随 C#，除非有充分理由** — C# 走 records（2014-04-23 讨论过、未批准）；判别联合是 F# 传统（2018.12.19："The linked 'full range of potential patterns' is for F# and several of these are not available in other .NET languages"，事实）。无 C# 对齐点，无充分理由偏离。
5. **读起来像英语、对新手友好** — 命名可读，但"封闭家族"概念对业务开发者陌生，且穷尽性是新概念负担。
6. **不为边缘场景加特性** — 零数据；穷尽性对"数十万安静客户"是边缘中的边缘。
7. **避免隐蔽的控制流/语义变化** — 穷尽性 + 封闭会改变"今天能继承、明天不能"的既有预期，是隐蔽语义变化的类型级版本。
8. **不与既有语法冲突** — `Case` 保留字占位干净，这是少数加分项。
9. **消除常见样板** — 类型分发样板已被模式匹配消除，本建议无增量。
10. **冗长只在有用时是美德** — `Case Class` 修饰符本身不长，但它购买的语义（封闭/穷尽）没有实用价值。

**主线对照（评价标准 2.3 表）**：`临时交/并集类型` 一行已标"主线无、Anthony 有、独立延伸"；`Select Case TypeOf`/模式匹配是主线"最期待、分阶段"。Case 类属于**Anthony 独立延伸**，且与主线模式匹配工作项（#304 已并入、standalone "LDM Reviewed: No Plans"）及我们已裁决 Table 的并集类型（`meeting-intersection-union-types.md`）三方重叠。**与主线关系：不与主线冲突，但完全被主线覆盖。**

### 诚实分层

- **事实**：Anthony 18.21 原文（"I mostly don't"、截图、无文字语法）；2018.12.19 穷尽性 "backwards breaking"、`Case` 形态、`'As' TypeName` 文法、逗号决议、模式匹配 "most excited about"；2014-02-17 `Case b As Button ' now works`、开放泛型反射判例、"Pattern-matching is odd in a language without algebraic datatypes"、四种分解方案；2018.05.30 #304 "Will consider part of pattern matching."、Issue #305 `TypeOf...Is` 非精确匹配、安静客户引文；2014-04-23 records 讨论无结论；`Case` 是 VB 保留字；`NotInheritable` 已表达单类型密封。
- **Probably**：`Case Class` 的增量价值仅剩封闭性/穷尽性（模式匹配已覆盖其余）；穷尽性可用 analyzer 实现；Case 类需要编译器私有的家族登记元数据或特性。
- **Suspect**：截图中 Anthony 的"2 个例子"具体内容（我们看不到，无法转录，也**绝不虚构**）；Anthony 是否意识到本建议与他自己 section 14 的 `{A Or B}` 并集类型在抢同一个概念空间；值类型分支在 `Option Strict Off` 下的行为。
- **OPEN QUESTIONS**：① `Case Class` 到底改变一个类型声明的什么（封闭登记？成员生成？继承限制？）——建议无答案；② 封闭性边界（同文件/同程序集/跨程序集）与 Partial 类关系；③ `Case Structure`/`Case Interface` 的独立语义；④ 值类型分支机制。
- **TODO**：把截图内的"2 个例子"转为文字并回填建议（这是唯一可能改变结论的证据）；给家族做一份"类型分发三方案（模式匹配 / 并集类型 / Case 类）"的对比与归并表。

### RESOLUTION:

1. **本建议保持 inactive（Table）**，不激活。这不是否决概念——"以 `Case` 为前缀表达分支类型"的**命名直觉**我们喜欢，它自洽地接住了 `Select Case` 的"分支"含义——而是否决当前任何激活动作：建议没有任何可设计的语法、语义、示例或证据，只有一张截图和一个自我否定。

2. **场景（运行时类型分发）已被模式匹配家族接管。** `Select Case shape / Case c As Circle` 的声明模式（2014-02-17 预览、2018.12.19 文法 `'As' TypeName`）覆盖"判型 + 绑定 + 分发"；TypeOf 流分析覆盖守卫惯用法。Case 类在这条路上没有增量。

3. **Case 类独有的增量（封闭性 + 穷尽性）两样都站不住**：穷尽性被 2018.12.19 明言拒绝（"introducing it is backwards breaking"），且 analyzer 完全可以实现；封闭性 CLR 元数据无法表达，需要编译器私有登记，且"封闭化"既有类型会收回今天合法的继承权——限制型破坏，违反 "No breaking changes."。

4. **穷尽性需求作为 analyzer 场景记录在案**，不进入语言。若未来出现强真实世界数据表明穷尽性必须进语言，再单独评审，且必须与 2018.12.19 的决议正面辩论。

5. **家族归并**：类型分发领域的三份建议——模式匹配（主线，Phase 1 声明模式 Active）、并集类型（Anthony，已 Table 并入模式匹配）、Case 类（Anthony，今日 Table）——统一挂到模式匹配工作项下交叉标注，避免三线纠缠。Case 类的 `Case` 命名作为"分支即类型"的阅读建议并入家族讨论（PROPOSAL D 的剩余价值）。

### Implication

- `proposal-case-classes.md` 保持 inactive；标注 `Table` 与激活信号（见下）。
- 通知建议来源：若"撬开门缝的 2 个例子"转成文字，我们愿意重新看一眼——那是唯一可能改变结论的证据。
- 家族工作项下挂一份"封闭类型族 / 穷尽性"的 analyzer 场景记录。
- 在 `proposal-select-case-enhancements.md` 的家族文法讨论中，把 `Case` 命名的阅读价值（PROPOSAL D）与 Design1/Design2 逗号悬案一并收口。

### 激活所需信号（本次会议明确列出）

若以下信号逐步出现，本建议可回到 Active 评审：

1. **文字形式的语法与语义**：Anthony（或任何人）把截图内的 2 个例子与 `Case Class/Structure/Interface` 的文法转成文字，说清"`Case` 修饰符改变了类型声明的什么"。
2. **封闭性机制定义**：same-file/same-assembly/attribute 三选一，且论证对既有代码零破坏（尤其是继承权收回的路径）。
3. **穷尽性正当性论证**：能与 2018.12.19 "backwards breaking" 决议正面辩论的真实世界数据——没有它，穷尽性就是被拒特性。
4. **与模式匹配家族的非重叠论证**：说清 `Case Class` 在 `Case c As Circle` 之外还提供什么。
5. **值类型分支与 Option Strict 分叉**：`Case Structure` 的独立语义与宽松路径行为。

在 1–5 出现前，本建议是 Table。

### 状态

- **LDM 状态：LDM No Plans**（inactive 保持搁置）。
- **三态判定：Table** — 命名直觉保留、场景被接管、唯一增量被拒或不可实现、证据为零；不激活，不给排期。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-case-classes.md`（`Case` 类/结构/接口）
- 来源：Anthony 原文章节 18.21「Case Classes, Structures, and Interfaces」（`..\..\AnthonyDesign_wordpress.txt` L3200–3212）
- 配方目标：以 `Case` 前缀修饰 `Class`/`Structure`/`Interface`，表达封闭的、按形状分发的类型族；是对判别联合的重新框定（不引入新的 sum 类型构造器）

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 1/5 | 锚点 1："声称的效果与示例矛盾，或目标定义不清"。目标定义**完全不清**——无文法、无示例（仅一张未转录的截图）、作者自称 "I mostly don't" get it；"2 个撬开门缝的例子"不可见，改进不可衡量、不可演示。唯一的"效果"是命名直觉。 | 未提供（仅有概念名） | 无语法、无示例、无场景数据；关键论据留在图片里 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化"。判别联合是 F# 传统，本建议的"重新框定"只有命名（`Case` 前缀）做了 VB 化，语义零设计；`Case Structure`/`Case Interface` 与开放 `Class` 体系的冲突是根本性的，不是措辞问题。命名一处加分但整体未 VB 化。 | 已检查 | 封闭性 vs 开放类体系根本冲突；概念重叠模式匹配 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全（这份建议诚实遵守模板），且**未凭空虚构语法**（明确声明"不虚构"——符合 vblang 品质红线）；Drawbacks/Alternatives 如实。但 Detailed design 是存根（核心即"只有截图"）、4 个未决问题全是关键设计点、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）。文档诚实，内容空白。 | 已检查 | Detailed design 为空；未决问题≥4 个关键点；占位符 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。风（与模式匹配/并集三线纠缠、穷尽性与 2018.12.19 决议冲突）受损明显；暗（封闭化收回继承权的限制型破坏风险）存在；文档对这两点零权衡（Drawbacks 只列了泛泛三条）。唯一正面的水/光（命名盘活 `Select Case` 分发惯用）不足以对冲。 | 已检查（待定，预测性） | 三线重叠未识别；穷尽性冲突未识别；破坏风险未分析 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。主来源（18.21）标注正确，F# 血缘被 Anthony 本人点明（"like many F# features"）；但**关键交叉血缘全缺**：Anthony 自己 section 14 的 `{A Or B}` 并集类型（同一概念空间）、2014-04-23 records 讨论（同一"sum/不可变数据"需求）、主线 2018.12.19 模式匹配决议——全部未交叉标注。 | 已检查 | 未与 section 14/并集交叉标注；未标注 2014-04-23 records；未标注 2018.12.19 |

### 设计原则对照

- **与 VB 基因：整体偏离**。命名直觉（`Case Class` = "分支类"）与 `Select Case` 惯用自洽，符合原则 #2/#8 的部分；但封闭类型族、穷尽性检查与 VB 开放、保守、稳定的基因冲突（原则 #1 破坏、#6 边缘、#7 隐蔽变化）；类型分发已是"第二种/第三种做事方式"（原则 #3）；无 C# 对齐点（原则 #4）。
- **与主线关系：Anthony 独立延伸，且被主线覆盖**。模式匹配（主线"最期待、分阶段"）已接管类型分发；穷尽性被 2018.12.19 拒绝；并集类型已 Table 并入模式匹配。Case 类不与主线冲突，但完全站在主线已划定的地界上。
- **破坏性变更：语法层无、语义层潜在有**——"封闭化"既有类型收回今天合法的继承权（限制型破坏）；穷尽性若进 `Select Case` 是 2018.12.19 明确拒过的 backwards breaking。

### 总评

- **达成程度：未达成**——概念只有命名，没有可设计的内容；其唯一独有主张（封闭/穷尽）被主线决议拒绝或 CLR 不可表达。
- **LDM 三态建议：Table**（保持 inactive）——命名直觉作为阅读建议并入家族；穷尽性作为 analyzer 场景记录；在五个激活信号出现前不排期、不进入 VBScript.NET 语法面。
- **主要问题**：① 无文法/语义/示例，关键论据（2 个例子）在截图里未转录；② 封闭性 vs 开放 `Class` 体系根本冲突，CLR 元数据无法表达；③ 穷尽性被 2018.12.19 拒绝且 analyzer 可替代；④ 与模式匹配、并集类型三线重叠未识别；⑤ 继承权收回的限制型破坏未分析。

### 返工建议

- **补充章节**：语法/BNF（`Case` 修饰符在类型声明位置的文法）；"`Case` 改变了类型声明的什么"专节（封闭登记 / 成员生成 / 继承限制三选一并给 spec）；封闭性边界（same-file/same-assembly/attribute、Partial、跨程序集）；值类型分支与 `Case Structure`/`Case Interface` 独立语义；兼容性分析（继承权收回路径、`langversion` 门控）。
- **补充证据**：把截图内的 2 个例子转成文字（**这是唯一可能改变结论的证据**）；与 `proposal-intersection-union-types.md`、`meeting-intersection-union-types.md`、2014-04-23 records 讨论交叉标注；穷尽性需求在真实代码中的量化数据。
- **未决问题处理**：4 个未决问题升级为规范小节；新增：与 2018.12.19 穷尽性决议的正面辩论、analyzer 替代方案的对比。
- **设计探索**：穷尽性 analyzer 的可行性原型（读取家族登记 + 报告未穷尽分支，零语言表面积）；把 `Case` 命名作为"分支即类型"的阅读建议并入 `proposal-select-case-enhancements.md` 家族文法讨论。

---

## 附录：C# 生态与互操作考量

> 本附录按评估流程追加，只评估 C# 现实方向与本提案的关系，**不修改正文结论**。C# 原文均来自 `..\..\..\csharplang`（dotnet/csharplang 官方仓库镜像，main 分支），逐字引用并标注来源文件路径；路径省略前缀 `..\..\..\csharplang\`。索引依据：`..\..\..\csharplang-index.md`（T8、M4、M8）。

### 相关 C# 现实方向

**C# 在本提案瞄准的同一概念空间已经走出完整三步：records → record structs → unions/closed hierarchies/case declarations（C# 15 开发中）。** 本提案（Scala case class 式数据类 + 封闭家族）恰好落在 C# 9 以来的主线上。

1. **Records（C# 9）**——值相等 + 非破坏性更新的数据类。`proposals\csharp-9.0\records.md` 定义了合成相等成员（`EqualityContract`、`IEquatable<R>`、`Equals`、`GetHashCode`、`==`/`!=`）、打印成员（`PrintMembers` + `ToString`）、复制/克隆成员，以及位置记录的额外合成（主构造、`get`/`init` 自动属性、`Deconstruct`、`with` 表达式）。
   - 原文：「Records cannot inherit from classes, unless the class is `object`, and classes cannot inherit from records. Records can inherit from other records.」→ `proposals\csharp-9.0\records.md`（Inheritance）
   - 相等成员起点原文：record 类型包含合成只读属性 `Type EqualityContract { get; }`（sealed 时为 `private`，否则 `virtual` + `protected`）→ `proposals\csharp-9.0\records.md`（Equality members）
   - 原文：「A `with` expression allows for "non-destructive mutation", designed to produce a copy of the receiver expression with modifications in assignments in the `member_initializer_list`.」→ `proposals\csharp-9.0\records.md`（`with` expression）

2. **Record structs（C# 10）**——值类型的 record，无继承、无 `EqualityContract`。原文：「The synthesized equality members are similar as in a record class (`Equals` for this type, `Equals` for `object` type, `==` and `!=` operators for this type), except for the lack of `EqualityContract`, null checks or inheritance.」→ `proposals\csharp-10.0\record-structs.md`（Equality members）。元数据识别被 C# 自己列为开放问题——原文：「how to recognize record structs in metadata? (we don't have an unspeakable clone method to leverage...)」→ `proposals\csharp-10.0\record-structs.md`（Open questions）。

3. **Unions / Closed hierarchies / Case declarations（C# 15 开发中）**——本提案"封闭家族 + 穷尽性"的 C# 版，也是 C# 15 Kickoff 的主线之一。
   - **Unions**：`[Union]` 特性 + case types + union 转换 + union 模式匹配 + 穷尽性，以及 `union Pet(Cat, Dog)` 简写声明。原文：「Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive.」→ `proposals\unions.md`（Motivation）。注意它是"type unions"而非 F# 式判别联合——原文：「The proposed unions in C# are unions of *types* and not "discriminated" or "tagged". "Discriminated unions" can be expressed in terms of "type unions" by using fresh type declarations as case types.」→ `proposals\unions.md`（Motivation）。穷尽性原文：「A union type is assumed to be "exhausted" by its case types. This means that a `switch` expression is exhaustive if it handles all of a union's case types」→ `proposals\unions.md`（Union exhaustiveness）。另有非装箱访问模式（`HasValue`/`TryGetValue`）让模式匹配强类型访问各分支、避免 `object?` 装箱 → `proposals\unions.md`（Non-boxing access members）。
   - **Closed hierarchies**：`closed` 修饰符 + 同程序集限制 + switch 穷尽 + `IsClosedType` 特性 + `[CompilerFeatureRequired("ClosedClasses")]` 阻止跨语言派生。原文：「Allow a class to be declared `closed`. This prevents directly derived classes from being declared in a different assembly」→ `proposals\closed-hierarchies.md`（Summary）。原文：「Closed classes are generated with an `IsClosedType` attribute, to allow them to be recognized by a consuming compiler.」→ `proposals\closed-hierarchies.md`（Lowering）。原文：「Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes.」→ `proposals\closed-hierarchies.md`（Blocking subtyping from other languages/compilers）
   - **Case declarations**：C# 有与本提案同名的文件——discriminated-unions 工作组的 `meetings\working-groups\discriminated-unions\Case Classes.md` 重定向到 `proposals\case-declarations.md`。它在 closed 类型体内用 `case` 声明嵌套分支：

     ```csharp
     public closed record GateState
     {
         case Closed;
         case Locked;
         case Open(float Percent);
     }
     ```

     原文：「A case declaration is a shorthand syntax for declaring a nested case of a closed type. It infers much of what it is from context.」→ `proposals\case-declarations.md`（Summary）
   - 时间线：C# 15 Kickoff 把 unions 列为 C# 15 主题——原文：「We have a lot of union work in progress, and an overview can be found at … We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area.」→ `meetings\2025\LDM-2025-08-18.md`（Unions）。closed hierarchies 已进入 LDM 逐条决议 → `meetings\2026\LDM-2026-04-20.md`（champion issue 9499）。

### 现实 vs 提案

| 本提案主张 | C# 现实 | 判定 |
|---|---|---|
| `Case` 前缀修饰类型声明 = "分支类" 命名直觉 | C# case declarations 用同款 `case` 关键字，但**嵌套在 closed 类型体内** | **兼容**——命名直觉被 C# 独立复现，验证 PROPOSAL D；作用域选择不同 |
| 封闭家族（编译器知道所有分支） | C# `closed` + `IsClosedType` 特性 + `[CompilerFeatureRequired("ClosedClasses")]` | **需桥接**——"CLR 元数据无法表达"需修订：运行时仍无 sealed-hierarchy 概念，但 C# 15 已用特性约定实现"编译器私有登记"，.vbx 编译器必须识别这两个属性 |
| 穷尽性检查 | C# unions/closed hierarchies 正式拥抱（switch 穷尽免 warning） | **脱节（VB 语言层）/ 需桥接（.vbx 消费层）**——2018.12.19 VB 决议仍约束 VB；.vbx 消费 C# union/closed 类型时穷尽性从可选工具变为互操作必需 |
| `Case Structure` 值类型分支 | C# record structs（无 `EqualityContract` 的字段相等）+ union 非装箱访问模式（`HasValue`/`TryGetValue`） | **兼容且有现成答案**——本会议称"值类型分支是整片未开发领域"在 C# 侧已过时 |
| records 式成员生成（未批准） | C# records 合成 `EqualityContract`/`PrintMembers`/`Deconstruct`/clone/`with` | **需桥接**——元数据形状是 C# 特定的；.vbx case class 若互通须镜像该形状，否则 C# 消费者拿不到 record 语义 |

逐条展开：

1. **命名直觉被 C# 独立验证。** C# 工作组的原文件就叫 `Case Classes.md`（`meetings\working-groups\discriminated-unions\Case Classes.md`，现指向 `proposals\case-declarations.md`），其 `case Closed;` 读法与本提案 `Case Class` 完全同源。但 C# 把它限定为**封闭类型体内的嵌套声明**，而非顶级类型修饰符——这恰好消解了本会议开放问题 ②（封闭性边界）与 ③（`Case Structure`/`Case Interface` 独立语义）：嵌套使"分支"与"容器"同文件同程序集，Partial 与跨程序集问题被重新界定。C# 的"封闭容器 + 嵌套 case"是家族讨论可直接借鉴的具体形态。

2. **"封闭性 CLR 元数据无法表达"需要修订。** 正文 RESOLUTION 3 说"封闭性 CLR 元数据无法表达，需要编译器私有登记"。C# 15 的 closed-hierarchies 正是这样做的：运行时（CLR 类型系统）仍无 sealed-hierarchy 概念，但语言层用 `IsClosedType` 特性 + `[CompilerFeatureRequired("ClosedClasses")]` 在元数据里登记"此类封闭"，让消费方编译器识别并强制。所以："运行时表达不了"仍真，"元数据里没有约定可写"已不真。**这是本附录对既有 RESOLUTION 最重要的一条修订。**

3. **穷尽性：C# 采纳 vs VB 拒绝，是语言选择分歧，不是技术不可能。** C# unions/closed hierarchies 把穷尽性做成语言承诺（免 warning）；2018.12.19 VB 决议（"introducing it is backwards breaking"）是 VB 的兼容性选择，与 C# 的采纳不矛盾。但对 VBScript.NET：一旦 .vbx 脚本消费 C# union/closed 类型，`Select Case` 必须知道"哪些 case 已穷尽"；要么实现穷尽性分析（把 RESOLUTION 4 的 analyzer 从可选工具升格为互操作必需），要么始终要求 `Case Else`（更弱但安全）。

4. **值类型分支不是空白领域。** 正文权衡 #2 称 `Case Structure` 的装箱 + `Equals` 是"整片未开发领域"。C# record structs 用字段相等的合成 `Equals`（无 `EqualityContract`）解决"值类型的结构相等"；unions 的非装箱访问模式（`HasValue`/`TryGetValue`）让编译器在模式匹配时强类型访问各分支、避免 `object?` 装箱。这是 `Case Structure` 问题的现成 C# 答案。

5. **Record 元数据差异是互操作核心。** C# 编译器靠元数据形状识别 record（`EqualityContract`、`PrintMembers`、保留名 clone 方法）；record structs 的识别被 C# 自己列为 open question。若 .vbx case class 生成不同的成员形状，则：C# 消费者把 .vbx case class 当普通类（无 `with`/解构/值相等），.vbx 消费者把 C# record 当普通类。**互通的前提是镜像 C# record 的合成形状，或不互通并明确降级。**

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：消费 C# union/closed/record 类型时默认走"类型化 + 穷尽性感知"路径；`Option Strict Off`/动态晚期绑定作为显式 opt-in（与决策文件 M2/M5 的"脚本层动态、产物类型化"双模路线一致）。穷尽性警告在 .vbx 默认开启，避免脚本静默漏分支。
2. **source-gen 桥**：若 .vbx 需要 case-class 式数据类，优先用编译器/源生成器生成 **C# record 兼容形状**（`EqualityContract`、`PrintMembers`、`Deconstruct`、clone、`with`），而不是新增语言特性。这与 RESOLUTION 4（analyzer 路线）及索引 T6（source-gen 替代反射）同向，也避开 records 未获批准的旧路。
3. **识别新元数据（必须桥接）**：.vbx 编译器必须识别 `IsClosedTypeAttribute`、`CompilerFeatureRequiredAttribute`（feature 名 "ClosedClasses" 已核实用于 closed 类构造器）、`UnionAttribute`、`IUnion` 接口，以及 record-shape 成员。否则三个互操作场景全部失败：从 closed 类派生不被阻止（C# 侧靠 `[CompilerFeatureRequired]` 挡，.vbx 编译器不认识就挡不住）、union 值在模式匹配中语义错乱、record 相等语义被当普通类。这与决策文件 M8 的"unsafe 元数据识别"（`RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`）是同一类桥接义务。
4. **穷尽性分析升格**：RESOLUTION 4 的 analyzer 场景记录应扩展为"消费 C# union/closed 类型时的必需分析"，而不仅是 VB 侧可选工具；同时把 C# case declarations 的"封闭容器 + 嵌套 case"形态纳入家族对比表。
5. **值类型分支直接复用**：`Case Structure` 语义可映射到 C# record structs + union 非装箱访问模式，无需重新发明装箱机制。

### 对既有 RESOLUTION / 三态判定的影响

- **三态判定（Table）不变**：C# 现实的丰富不改变本提案自身的证据状况——仍无文法、无示例、无数据，激活信号 1–5 全部有效。C# 已做不等于 VB 该做。
- **两条论据需修订（不影响结论，影响未来重新评审时的判断）**：
  - RESOLUTION 3 的"封闭性 CLR 无法表达"→ 修订为"运行时无法表达，但 C# 15 已有 `IsClosedType`/`CompilerFeatureRequired` 元数据约定；VB 若做技术上可复用，兼容性与穷尽性仍是主要障碍"。
  - 权衡 #2 的"值类型分支未开发"→ 修订为"C# record structs / union 已提供现成答案，可参考"。
- **新增家族交叉标注**：C# case declarations / closed hierarchies / unions 与本提案、并集类型、模式匹配构成"同一概念空间"的 C# 侧对应，建议在家族工作项下加一条交叉引用（指向本附录）。

### OPEN QUESTIONS

- C# union 类型是否也使用 `[CompilerFeatureRequired("Unions")]` 阻止跨语言误造？`proposals\unions.md` 只描述 `UnionAttribute` + `IUnion` 接口，未核实 CompilerFeatureRequired 用于 union（closed 类用 `"ClosedClasses"` 已核实）。→ **OPEN QUESTION**。
- `proposals\case-declarations.md` 的 Grammar 标 TBD，且引用了未指定的"静态值运算符 / 目标类型成员查找"两个可选特性——最终语法未定，本附录以其 Summary 语义为准。→ **OPEN QUESTION**（该提案本身未完成）。
- .vbx 是否应把 C# record 视为"自动满足 case class 语义"的类型（值相等 + 解构 + with），还是按普通类对待？取决于 ModVB 对 records 的立场，超出本会议范围。→ **OPEN QUESTION**。
