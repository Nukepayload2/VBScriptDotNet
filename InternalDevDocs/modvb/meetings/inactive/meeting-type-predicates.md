# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这是"类型收窄三连"的第三场——前两场分别把 TypeOf 流分析（内建事实源收窄）判为 Active、把交/并集类型（`{A, B}` 限定局部变量）判为 Consider。今天要处理的 Anthony 18.26「Type Predicates in Flow-Sensitive Typing」是这三份里最原始的一条线头：它自己就写着 *"I haven't given much thought to this at all"*，全文是四段文字加一段伪代码。我们的任务不是审批一份设计——它没有设计——而是回答两个更前置的问题：**这条"任意谓词即类型"的方向值不值得激活？如果搁置，激活要等到什么信号？**

## Agenda

* [Proposal: 类型谓词（流敏感类型）](#proposal-类型谓词流敏感类型)

## Proposal: 类型谓词（流敏感类型）

_Related: [LDM-2014-02-17](../../../vblang/meetings/2014/LDM-2014-02-17.md)（#7 TypeOf IsNot 已批准）· [vbldm-notes-2018.05.30](../../../vblang/meetings/2018/vbldm-notes-2018.05.30.md)（#304 Select TypeOf 并入模式匹配、#305 TypeOf...Is 语义困惑、#167 Return? 否决）· [vbldm-notes-2018.12.19](../../../vblang/meetings/2018/vbldm-notes-2018.12.19.md)（模式匹配路线、and/or/not 模式仍不确定）· [vbldm-notes-2018.02.07](../../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（可空引用类型推迟）· [vbldm-notes-2017.10.18](../../../vblang/meetings/2017/vbldm-notes-2017.10.18.md)（`{...}` JSON 字面量类型）· AnthonyDesign section 18.26「Type Predicates in Flow-Sensitive Typing」· ModVB `inactive/proposal-type-predicates.md`、`meeting-typeof-flow-analysis.md`、`meeting-intersection-union-types.md`、`meeting-nullability-flow-analysis.md`_

### 场景与缺口

We started from the three lines Anthony actually drew. 18.26 的开场是诚实的自我暴露——*"I haven't given much thought to this at all but it occurred to me when I was writing the type-inference section that it might be simple to generalize things a little further."* 他把已经确立的两件事（TypeOf 测试后的交集类型 `{IEnumerable, IDisposable}`、可空性即 `{String, Not Null}`）再推一步：**任何"对值成立与否"的谓词都可以成为类型的一部分**。原文给出的不是语法，而是概念图：

```vb
' Pseudo-code, not proposed syntax.（Anthony 18.26 原文，逐字）
Interface Negative
    Shared Function IsNegative(x As Integer) As Boolean
        Return x < 0
    End Function
End Interface

Interface Positive
    Inherits NonNegative

    Shared Function IsPositive(x As Integer) As Boolean
        Return x > 0
    End Function
End Interface
```

> "That is to say if I could provide the system I designed a named type which represented some function returning true then it would start plumbing things through like `{Integer, Not Negative}` or have a declaration like `count As {Integer, Positive}`. That's enough that it's an interesting thing to explore _at some point_ but I haven't thought too deeply about it yet."

我们把这个想法放回前两场会议已建成的地基上，缺口立刻变得具体。今天的 VB 里，"检查后获得更强类型信息"只在引用类型上成立（`TypeOf ... Is`），而**值类型的守卫**完全拿不到类型级信息：

```vb
Module Program
    Sub Main()
        Dim count As Integer = GetCount()
        If count < 0 Then Throw New ArgumentOutOfRangeException(NameOf(count))
        ' 此后 count 在人类语义上"保证非负"，但编译器不持有任何类型信息。
        Console.WriteLine(count * 2)
    End Sub

    Function GetCount() As Integer
        Return 42
    End Function
End Module
```

这是类型谓词提案里唯一我们没有在任何既有会议上见过的东西：**值类型精化（value-type refinement）**。`TypeOf x Is Integer` 在今天的 VB 里是非法语法，TypeOf 流分析因此天然不涉及值类型——但 `Positive` / `Negative` 恰恰是关于 `Integer` 的谓词。`count As {Integer, Positive}` 若成立，编译器就能把"count > 0"当作类型事实传导下去，消除范围检查、给出更强的推断。这是真实的能力缺口。

但我们同样看到三根**警示线**，它们构成了整场讨论的张力：

1. **信任模型**：TypeOf 流分析的事实源是编译器内建的运行时类型测试，编译器验证它；谓词的事实源是用户写的函数——用户函数可以撒谎、可以副作用、可以随时间变化。收窄建在什么样的信任地基上？
2. **语法债务**：`{Integer, Not Negative}` 同时撞上三样东西——交/并集类型的 `{A, B}`（上一场刚判 Consider）、2017.10.18 讨论过的 `Dim a As {"contact"}` JSON 字面量类型、以及 `Of T As {Control, New}` 约束列表。
3. **负谓词**：`Not Negative` 是负收窄，而 TypeOf 流分析会议刚确立"负收窄不可表示"。这个提案的语法示例从第一个就踩在既定结论上。

### 候选方案

**PROPOSAL A — 全量：任意谓词即命名类型（Anthony 原案）。** 接口上约定一个 `Shared Function Is...(x As T) As Boolean`，接口名成为类型的一部分；`count As {Integer, Positive}` 声明后编译器跟踪"满足谓词"的状态。接口继承即谓词蕴含（`Positive Inherits NonNegative` ⇒ 正蕴含非负）。

**PROPOSAL B — 只做内部谓词状态，不暴露用户语法。** 流分析引擎内部增加"谓词事实"这一状态域（`Integer × {IsPositive}`），语义模型与 IDE 可见，但用户不能写 `{Integer, Positive}` 声明。最窄，零语法，只服务编译器自身可验证的形状（比如将来对 `Integer` 内建 `>= 0` 分析）。

**PROPOSAL C — 不引入谓词机制，依赖既有收窄 + 交/并集。** 引用类型用 TypeOf 流分析（已 Active）；值类型维持现状（守卫 + 样板）；`{A, B}` 组合用交/并集（Consider）。零新机制，代价是值类型精化这一整片缺口不补。

**PROPOSAL D — 谓词载体用委托类型，而非接口。** `Dim positive As Predicate(Of Integer)` 之类的一等函数作为谓词来源，声明处绑定委托。回避"接口语义混淆"，但引入"哪个委托"的命名与查找问题，且委托的真伪同样不可验证。

**PROPOSAL E — 什么都不做（保持 inactive）。** 现状就是 PROPOSAL C；Anthony 原文自己就说 "haven't thought too deeply about it yet"。

### 权衡：Q&A

- **A 的接口机制：接口是结构契约，还是命题声明？** 这是整场最尖锐的语义质疑。接口在 VB 里描述"能做什么"（`IDisposable` ⇒ 有 `Dispose`），谓词声明的是"关于值的一个命题为真"（`count` 是正的）。两者不同源。更致命的是 `Positive Inherits NonNegative`：它要求编译器把"正 ⇒ 非负"当作**接口继承**来理解——但那是一道关于整数的数学定理，不是一条类型声明。编译器可以查继承链，不能证定理。We think 接口机制在 A 里是占位符，不是设计——Anthony 自己在提案里就写接口"仅用于说明概念"，`NonNegative` 甚至没有定义。
- **信任模型：编译器能不能收窄在任意用户函数上？** We think 不能。TypeOf 的事实是编译器验证的（运行时类型测试，`isinst` 语义），收窄是健全的。用户谓词没有这个保证：`IsPositive` 可以返回 `x > 0`，也可以返回 `DateTime.Now.Second Mod 2 = 0`，编译器无法区分。若在不可信谓词上收窄，我们等于把"启用型"绑定规则（TypeOf 流分析会议 RESOLUTION #3 的安全护栏）交还给用户代码的诚实度。**这一条让 A 在类型层不可行——不是"难"，是不健全。**
- **值类型是不是这条线的真正价值？** 我们拆开承认：**是**。`TypeOf` 碰不到 `Integer`，所以"检查后获得更强类型信息"对值类型是一片空白；`count < 0` 守卫是高频样板。但正因如此，值类型精化是一个**全新的实现面**（装箱、`Equals`/比较、可空值类型交互），TypeOf 流分析会议明确不覆盖，交/并集会议的值类型并集也被挂起。在一个未定型的想法上开一片全新战场，成本不成比例。`Suspect`：Anthony 没有意识到他的例子其实在呼吁一个 TypeOf 从不曾覆盖的领域——他以为自己是在"泛化 TypeOf"，实际上是在"发明值类型收窄"。
- **C# 8/9 的对照：主流怎么做"谓词类型化"？** 我们对照了 C# 的两个机制。一是**模式收窄**：`if (x is string s)` 收窄的是**模式变量** `s`，不是原变量 `x`——C# 没有"任意谓词收窄原变量"。二是**声明式契约属性**：`[NotNullWhen(true)]` / `[MemberNotNull]`——这是主流里最接近"谓词类型化"的东西，但它是**参数/返回值的契约标注**，编译器只对受支持的形状做检查，且不创建命名类型、不进入类型文法。**结论：连流分析最发达的 C# 都没有把"任意谓词即命名类型"做进类型系统**。按"默认跟随 C#"的纪律，A 从一开始就站在需要辩护的位置。
- **与 TypeScript 的关联：是真参照还是远亲？** Anthony 的关联是 *"Skipping a long story … effect system … TypeScript … 'future-proof?' …"*——一句省略号。TypeScript 确实有类型谓词（`function isFish(pet: Animal): pet is Fish`），但那是**返回类型标注**，函数体完全不检查（不健全、纯信任）。effect system 的关联我们只能猜：若谓词函数有副作用，收窄就不健全——效果系统恰好在追踪这个。`Suspect`：这个关联对设计没有产出，只是 Anthony 预感"这里有一条更长的故事"。
- **A vs B：内部状态 vs 用户语法。** B 把"谓词事实"限制在编译器能验证的形状内（如内建的范围分析），这是健全的；A 的全部价值在"用户可书写的拼写"，而那个拼写恰好踩在不健全的信任模型上。**顺序不能反**：先有可验证的谓词形状，才有值得暴露的语法。这与交/并集会议"内部表示先行、用户语法随后"的结论同构。
- **D 的委托载体是不是更好的拼写？** 委托比接口诚实（"这是一个函数"而不是"这是一个类型"），但它解决不了信任问题——委托体同样不可验证。且委托没有继承蕴含（`Predicate(Of Integer)` 的"正 ⇒ 非负"如何表达？），等于放弃了 Anthony 例子里唯一有趣的部分。We think D 不比 A 更可辩护，只是换了个藏问题的位置。
- **什么都不做的代价。** E 的代价是值类型精化缺口原样保留（`count < 0` 守卫继续拿不到类型信息）。但注意：这个缺口的现有解法（手动守卫 + 断言）是**确定且健全**的——它只是样板多。用"样板多"来为"不健全的类型机制"辩护，不成立。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`count As {Integer, Positive}` 在类型位置是**全新文法**——而 `{...}` 在类型位置已经有三个住户：交/并集类型（`{IDisposable, ICloneable}`）、JSON 字面量类型（2017.10.18：`Dim a As {"contact"} ' Type: {"contact"}`）、约束列表（`Of T As {Control, New}`）。第四个住户需要第四套上下文判定。更麻烦的是 `{Integer, Positive}` 里第二个元素 `Positive` 是**谓词名**，与交/并集里第二个元素是**类型名**、JSON 里是**字面量形状**语义都不同——同一个花括号内不同位置的元素种类不同。这是连语法元规则都要新定义的一层。**OPEN QUESTION。**

#### 2. 角案例 / 边界语义

- **谓词的一致性**：`IsPositive(x)` 第一次求值 `True`、第二次求值 `False`（谓词有状态或时间依赖），收窄状态就崩塌。TypeOf 测试无此问题（`isinst` 幂等、无副作用）。流分析引擎的任何新事实源都必须证明"同值同结果"。
- **异常**：谓词抛异常（`x` 是 `Integer` 时 `IsPositive` 不会，但通用谓词可以）——收窄区域内的每次重求值都重跑谓词？缓存？还是收窄后假定为真不再求值？语义未定。
- **负谓词**：`Not Negative` 是补集类型，而负收窄不可表示是既定结论（TypeOf 流分析会议）。A 的语法示例从第一个就撞墙。
- **值类型 + 可空**：`{Integer, Positive}` 与 `Integer?` 如何交互？`Nothing` 满足还是不满足谓词？未定义。
- **谓词间的复合**：`{Integer, Positive, Not Zero}` 是三个谓词的合取？与交/并集的"类型合取"如何统一？合取表示上一场会议刚定为流分析共享部件，但那是**类型**合取，不是**命题**合取。
- **`Positive` 的等价性**：两个谓词 `IsPositive` 与 `IsGreaterThanZero` 语义相同，编译器怎么知道？不知道 ⇒ 收窄无法跨谓词合并。

#### 3. 作用域与绑定

语义模型对 `count` 的 `GetTypeInfo` 返回什么？一个携带"满足 `Positive`"注解的类型符号？`Positive` 是接口符号、委托符号、还是新的"谓词类型符号"？绑定 `count.Positive`（若谓词是成员）与成员查找如何交互——`Integer` 上突然多了一组谓词成员？IDE 补全要不要显示 `IsPositive`？这些在 A 里全是空白。

#### 4. 与既有特性的交互

- **TypeOf 流分析**：A 想共享引擎，但事实源种类不同（内建 vs 用户自定义），信任模型必须分轨——否则把"启用型"规则的安全护栏交给了用户代码。
- **交/并集类型**：`{Integer, Positive}` 与 `{Integer, Not Negative}` 的字面形态是交/并集语法的推广，但语义是"类型 ∩ 命题"，不是"类型 ∩ 类型"。上一场会议的交集类型符号（合成 type symbol）装不下命题成员。
- **可空性流分析**：`{String, Not Null}` 被 Anthony 当作同类——但可空性会议已把 `Not Null` 处理为流分析状态域（Null/NotNull/MaybeNull），**不是类型**。谓词若想接管可空性，是在重做已定的事。
- **泛型约束**：`Of T As {Positive}`？约束是类型约束，谓词不是类型。无交互，除非先定义"谓词约束"——又是一片新领域。
- **重载解析 / 表达式树**：收窄类型参与重载解析（沿用 TypeOf 流分析的"启用型"规则）；谓词事实无法进入表达式树。这些都要逐条写，proposal 未提。

#### 5. Breaking change 与兼容性

无直接破坏——A/B/D 都是新语法或新状态，不改变旧代码绑定。**潜在破坏在健全性**：若未来以"用户谓词收窄"落地且收窄不健全，重编译后可能出现此前不存在的"编译期相信谓词为真"的代码路径——这是隐蔽语义变化（`Return?` 被拒的同类理由，2018.05.30 逐字：*"Control flow would be altered by a very subtle character"*）。没有信任模型就没有安全论证。

#### 6. Option Strict / 编译选项分叉

`Option Strict Off` 下 `Integer` 本来就有晚期绑定路径；谓词收窄若在宽松路径下参与早期绑定解析，会不会把晚期绑定调用早期化？必须与 TypeOf 流分析同一条铁律：**收窄只参与早期绑定解析，绝不改变先前已成功绑定的代码**。两条路径行为必须一致。

#### 7. IDE / IntelliSense

谓词名在类型位置的补全、`{Integer, Positive}` 的 InfoTip 显示、收窄区域的成员可用性变化——全链路要原型验证。合成谓词类型符号要进 Roslyn 符号层级，影响 parser→binder→symbol→IDE 四层。任何新类型符号都是管道成本。

#### 8. 数据 / 普遍性

**零数据。** Anthony 没有给 use case（"interesting to explore at some point"），proposal 没有给场景频次。我们承认值类型守卫（`If count < 0 Then Throw`）是真实样板，但那是一个**普遍性未量化**的样板；而"任意谓词即类型"的刚性需求连例子都只是概念性的。对照 2018.05.30 #305 的处理方式——*"Not moving forward unless we see significant real world cases, and still have concerns."*

#### 9. 更简替代

- **引用类型收窄**：TypeOf 流分析已 Active，零新语法——本建议的第一根线头（`{IEnumerable, IDisposable}`）已被它接住。
- **可空性**：`{String, Not Null}` 已被可空性流分析的状态域接住（轨道 1 Active，轨道 2 Table）。
- **值类型精化**：唯一没被接住的部分。但更简替代是**内建形状**——编译器对 `Integer` 等内建类型做"`>= 0` 检查后视为非负"的有限分析（PROPOSAL B 的形态），不引入用户自定义谓词。这是"声明式契约"层级的思路，与 C# `[MemberNotNull]` 同族。
- **analyzer**：一个 Roslyn analyzer 今天就能提示"`count < 0` 检查后可假定非负"（数据流分析），不碰语言、零健全性风险。

#### 10. 复杂度 / 成本 / 优先级

A 的全量成本 = 新类型符号 + 命题合取状态 + 信任模型 + 值类型收窄新实现面 + IDE 四层，**只多不少**于交/并集的成本，而交/并集已被判定"限定局部变量 + 内部表示先行"。B 的内建形状成本中等偏低（复用流分析引擎的状态域），但那是可空性/流分析工作项的地盘，不归本建议。**优先级：本建议排在所有已 Active / Consider 的收窄工作之后——它不是任何工作的前置。**

#### 11. 运行时 / CLR 硬约束

CLR 元数据装不下"谓词类型"。若以接口承载（A），`Positive` 接口是真实可存储的类型——但那是"一个叫 Positive 的接口"，编译器如何把它当命题用没有 CLR 先例。2014-02-17 在泛型模式上碰过的墙（*"Answer: this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"*）在"命题即类型"上更硬：不是无法存储，是**无法存储"命题"这个语义**。谓词函数的信任问题最终只能靠编译器内建认识（B 的路）或属性契约（C# 的路），两者都绕开"类型"。

#### 12. 值不值得做

逐维打分：

- **价值**：值类型精化（真实但未量化）；任意谓词即类型（概念有趣，无场景）。
- **成本**：A 极高（新类型机制 + 值类型新实现面 + 信任模型）；B 中低但归其他工作项。
- **风险**：A 的核心风险是**不健全收窄**——把编译器对"事实"的验证责任交给用户函数，这与 TypeOf 流分析会议的整个安全论证相悖。

**结论：作为设计不值得做——它没有设计；作为方向，值得把"值类型精化"单独拎出来记为真问题。** 热情不抵消可行性：Anthony 自己的热情程度就写着 "at some point"，我们对"方向有趣"的认同不构成激活理由。

### VB 基因对照

逐条对照设计原则（评价标准第二部分）：

- **原则 1「永不破坏现有代码」**：A/B/D 未落地，无直接破坏；但收窄健全性风险一旦实现即破坏"编译期事实"的可信度，属潜伏破坏。
- **原则 2「保持 VB-like」**：`count As {Integer, Positive}` 读起来不像 VB——`{}` 在 VB 里是约束列表/字面量类型的形态，`Positive` 作类型成员是外来词汇。**明显外来味。**
- **原则 3「不引入第二种做事方式」**：违反。值类型守卫已有确定健全的写法；A 引入第二套"带类型的断言"。
- **原则 4「默认跟随 C#，除非有充分理由」**：C# 没有任意谓词类型；连最接近的 `[NotNullWhen]` 都是属性契约不是类型。**无先例可跟，且没有"充分理由"偏离到无先例地带。**
- **原则 5「读起来像英语、对新手友好」**：`{Integer, Positive}` 对新手不友好——"类型里嵌了一个谓词"比"检查后更强"难解释得多。
- **原则 6「不为边缘场景加特性」**：这是本建议最危险的一条。值类型精化本身可能是主流场景，但"任意谓词即类型"是为一个未量化的场景预支一整类新类型机制。
- **原则 7「避免隐蔽的控制流/语义变化」**：不健全谓词收窄正是隐蔽语义变化；`Return?` 的教训（2018.05.30）直接适用。
- **原则 8「不与既有语法冲突」**：`{...}` 语法预算已超载（交/并集、JSON 字面量、约束列表），加第四个住户是明确的冲突。
- **原则 9「消除常见样板」**：值类型守卫样板是真实痛点——但内建形状（B）与 analyzer 已能覆盖，不需要 A 的语法。
- **原则 10「冗长只在有用时是美德」**：A 的冗长（接口 + Shared Function）没有换来可验证的语义，不是"有用的冗长"。

**2.3 主线对照表**：类型谓词属于 `临时交/并集类型` 一行的延伸——主线"无"、Anthony"有"、关系"Anthony 独立延伸"，且比交/并集更激进（在"类型"概念上叠"命题"）。它与主线的真实关系是：**踩在主线已划定边界的领域上**——TypeOf 语义困惑（#305）、模式匹配的 TypeCheck 与"非匹配需要 compelling cases"（2018.12.19）、以及"模式是 VB 最期待的东西"（*"We all want to do pattern matching, it's the thing we are most excited about after C# interop issues"*）。本建议试图在模式匹配/流分析建成之前抢先定义类型级谓词，方向与主线相反。

### 诚实分层

- **事实**：
  - Anthony 18.26 原文：*"I haven't given much thought to this at all"*；伪代码标注 *"Pseudo-code, not proposed syntax"*；"Skipping a long story … effect system … TypeScript … 'future-proof?' …"；*"That's enough that it's an interesting thing to explore _at some point_ but I haven't thought too deeply about it yet."*（逐字转引自 proposal 与原文）。
  - 2014-02-17 #7：`TypeOf IsNot` *"Approved. Already in Main. VB-specific."*；*"You could already use the 'IsNot' operator previously, but only for reference comparisons e.g. 'If sender IsNot Nothing Then'"*。
  - 2018.05.30 #305：*"TypeOf...Is is not an exact match, but whether a cast can occur. This is a concern with this feature since there is already some confusion around how TypeOf...Is."*；*"Not moving forward unless we see significant real world cases, and still have concerns."*
  - 2018.05.30 #304：*"Will consider part of pattern matching."*（Labels: Pattern Matching and LDM Reviewed: No Plans）；#167 `Return?`：*"Control flow would be altered by a very subtle character."*
  - 2018.12.19：*"We all want to do pattern matching, it's the thing we are most excited about after C# interop issues"*；*"Maybe _and_ and _or_ and _not_ patterns later (still uncertain on this)"*；*"It's not clear how variable introduction without typechecking would work, or how type checking without assignment differs from the available `TypeOf x Is <type>`."*；*"If we do _and_ and _or_ later, AndAlsoMatches/OrElseMatches is one option. Conjunctions are probably not in first or second version, C# thinking is these are much lower need/usage."*
  - 2018.02.07（可空引用类型）：*"it may feel like a 'not VB' thing as we understand its usage"*；*"We'll postpone this until we understand the uptake in C#"*。
  - 2017.10.18：`Dim a As {"contact"} ' Type: {"contact"}`（`{...}` JSON 字面量类型）。
  - 2014-02-17（泛型模式）：*"Answer: this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection."*
  - TypeOf 流分析会议（本仓）：TypeOf 只作用于引用类型与接口（`TypeOf x Is Integer` 今天即非法）；负收窄不可表示；"启用型"绑定规则。
  - 交/并集会议（本仓）：`{A, B}` 交集 Consider（限定局部变量）；合取状态与流分析共享。
- **Probably**：值类型精化的内建形状（编译器对 `Integer` 做"`>= 0` 检查后视为非负"）可复用流分析引擎状态域；C# `[NotNullWhen]`/`[MemberNotNull]` 是"声明式谓词契约"的成熟先例（外部参照）；谓词信任问题只能靠内建形状或属性契约解决，绕不开"类型"。
- **Suspect**：Anthony 未意识到他的例子在呼吁值类型收窄这一 TypeOf 从不覆盖的领域；effect system / TypeScript 关联对设计无产出（省略号 = 未展开）；值类型守卫样板在真实 VB 代码中的占比无数据。
- **OPEN QUESTIONS**：
  1. 谓词命名机制（接口约定方法？委托？）如何对应——原文未说明。
  2. `{Integer, Not Negative}` / `count As {Integer, Positive}` 的精确语义与文法——原文标注为伪代码，非建议语法。
  3. 谓词的一致性、异常、可空值类型交互——流分析事实源的新要求。
  4. 与 effect system、TypeScript 类型谓词的具体关联是什么。
  5. 值类型精化的内建形状（PROPOSAL B）归属哪个工作项（可空性流分析？独立 speclet？）。
- **TODO**：
  - 撰写"值类型精化"设计空间笔记（把 18.26 的方向内核存档，供未来取用）。
  - 为内建形状（B）评估复用流分析引擎的技术路径。
  - 量化"`count < 0` 守卫 + 断言"样板在真实代码的占比，作为普遍性证据。

### RESOLUTION:

1. **整体判定：Table（保持 inactive），不激活、不否决。** 本建议是"方向笔记"而非"设计"——Anthony 自述未深入思考、伪代码标注非建议语法、无 use case、无数据。我们认同"收窄 = 谓词承载类型"这个理念层面的观察，但理念不是可评估的设计内容。2018.05.30 对 #305 的处理方式逐字适用：*"Not moving forward unless we see significant real world cases, and still have concerns."*
2. **谓词型接口机制（PROPOSAL A 的核心）Reject。** 三个独立理由：① 接口是结构契约、谓词是命题声明，语义不同源；`Positive Inherits NonNegative` 要求编译器把"正 ⇒ 非负"这一数学定理当接口继承理解，超出类型系统能力；② 编译器无法验证任意用户谓词的真伪，收窄不健全——这直接破坏 TypeOf 流分析会议"启用型"绑定规则的安全护栏；③ CLR 元数据无法表达"命题"语义。**"谓词作为命名类型"在信任模型上不可行。**
3. **把"谓词即类型"拆成三个事实源层级，各归其位：**
   - **层级 1（内建事实源）＝ TypeOf 流分析，已 Active**。编译器验证的运行时类型测试；不归本建议。
   - **层级 2（声明式契约）＝ 可空性 / 流分析工作项的候选接口**。C# `[NotNullWhen]` / `[MemberNotNull]` 同族：属性契约、编译器对受支持形状验证、非"命名类型"。这是"谓词类型化"的成熟形态。
   - **层级 3（用户自定义谓词即命名类型）＝ Reject（作为类型机制），方向保留为"值类型精化"**。见第 4 条。
4. **值类型精化是真问题，但以"内建形状 / 契约"而非"新类型种类"推进。** `count As {Integer, Positive}` 想表达的"检查后获得更强类型信息"在值类型上是真实缺口（TypeOf 碰不到 `Integer`），但解法是 PROPOSAL B 的内建形状或层级 2 的契约属性——不是让用户写任意谓词。**语法面 `{Integer, Positive}` 不可行（当前）**：负谓词撞负收窄不可表示；值类型收窄是新实现面；`{...}` 语法预算已超载（交/并集、JSON 字面量类型、约束列表）。
5. **驳回 PROPOSAL D 作为替代载体**——委托换了个藏信任问题的地方，且丢失了"蕴含"这一唯一有趣的部分。

**激活所需信号（明确写给未来）**：

- **信号 1（场景 + 数据）**：一个真实高频的值类型精化场景（如参数校验消除样板、范围分析），带量化数据与用户请求。
- **信号 2（信任模型落定）**：不是"任意函数即谓词"，而是编译器可验证的谓词形状——层级 2 的契约属性在 VB 的形态（继承 C# `[NotNullWhen]` 族）或内建形状（B）被写成一页 spec。
- **信号 3（语法预算）**：`{...}` 在类型位置的语法预算定案（交/并集、JSON 字面量类型、约束列表、谓词四方协调后），且负谓词问题被解决或显式放弃。
- **信号 4（effect system 澄清）**："Skipping a long story" 被写成文档：类型谓词与效果系统/TypeScript 的具体关联，证明这不是一句省略号。

### Implication:

- proposal 头部加一行："LDM 2026-08-08: Table。激活信号见 `meetings/inactive/meeting-type-predicates.md`"。
- 撰写"值类型精化"设计空间笔记：把 18.26 的方向内核（检查后获得更强类型信息 × 值类型）存档为流分析引擎的未来状态域参照，不作为激活清单。
- 与可空性 / 流分析团队对表：层级 2（声明式契约属性）纳入轨道 2 的候选接口（与 `[NotNullWhen]` 族对照）；层级 1 归 TypeOf 流分析。
- 与交/并集会议对表：确认合取表示只承载"类型合取"，命题合取不在 v1；`{Integer, Positive}` 的形态不进入交/并集文法。
- 未决问题移交至 OPEN QUESTIONS；跟踪 Anthony 若对 18.26 展开修订（目前无此迹象）。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：谓词命名机制如何对应（接口约定方法 / 委托 / 属性契约）——原文未说明；`{Integer, Not Negative}` 与 `count As {Integer, Positive}` 的精确语义与文法；谓词的一致性、异常、可空值类型交互；effect system / TypeScript 关联具体是什么；内建形状（B）归属哪个工作项。
- `TODO`：值类型精化设计空间笔记；内建形状复用流分析引擎的可行性评估；`count < 0` 守卫样板的量化数据。
- `Follow-up`：C# `[NotNullWhen]` / `[MemberNotNull]` / `[MemberNotNullWhen]` 属性族作为层级 2 的外部参照（标注来源：借鉴 C# 实现层，非照搬语法）；TypeScript 类型谓词的信任模型（函数体不检查）作为"不可信事实源"的反例参照。

### 状态

- **LDM 状态：Table（保持 inactive）**；谓词型接口机制 Reject；值类型精化方向以"内建形状 / 契约"形态转交流分析工作项。
- **三态判定：Table**——理念观察有价值（收窄 = 谓词承载类型），但零设计、零数据、零语法；激活必须等到信号 1（数据）与信号 2（信任模型）兑现。

---

## 附录：特性评价

### 评价对象

- 建议：`inactive/proposal-type-predicates.md` — 类型谓词（流敏感类型）：让"某个谓词返回 true"成为命名类型，`count As {Integer, Positive}`，编译器跟踪复合类型。
- 来源：Anthony 原文 18.26「Type Predicates in Flow-Sensitive Typing」（四段文字 + 一段伪代码，自述未深入思考）；隐含借鉴 TypeScript 类型谓词 / effect system（原文一句省略号，未展开）；与交/并集类型、可空性流分析同源（TypeOf 收窄的推广）。
- 配方目标：把流敏感类型从"TypeOf / nullness"泛化到"任意谓词"，让编译器在谓词为真的执行路径上跟踪复合类型。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 是"interesting to explore at some point"，无 use case、无受益者、无数据；示例只有声明接口的伪代码，**没有一行代码演示收窄在工作**；"值类型精化"这一核心能力缺口未被作者点名。 | 已提供 | 效果不可验收：无语法、无示例可运行、无原型；连"特性在动作"的演示都写不出 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。核心机制（谓词型命名接口）是外来概念（TypeScript 类型谓词 / 结构精化）的直接拼装，`{Integer, Positive}` 不是 VB 词汇；捆绑了 effect system、TypeScript、nullness、交/并集四根互不相干的线头；与 VB 接口语义冲突（接口 = 结构契约，谓词 = 命题声明）。 | 已检查 | 未 VB 化；未声明 TypeScript/effect system 借鉴；接口机制语义混淆；`Positive Inherits NonNegative` 要求编译器证数学定理 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、对"伪代码非建议语法"状态诚实（未虚构语法，这是加分）；但 Detailed design 只有原文伪代码的转引、Drawbacks 3 条浅、无文法/无兼容性/无 Option Strict/无 CLR 分析；未决问题 5 条具体但把"语法语义未定义"当普通条目；状态行全占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）。 | 已检查 | 核心章节空洞；占位链接；关键角案例（谓词一致性、信任模型）完全未触及 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。得=雷（点出"收窄 = 谓词承载类型"的理念、把值类型精化记为真问题）、水（差异化 vs C#）；失=风（`{...}` 语法预算超载、与交/并集/JSON 字面量/约束列表四方冲突，一致性断裂风险未识别）、暗（**不健全收窄**——把编译器对"事实"的验证责任交给用户函数，直接威胁 TypeOf 流分析"启用型"规则的安全护栏）；文档对这两项零应对。 | 已检查（预测待定） | 信任模型未被作者识别为问题；语法预算冲突未分析；不健全收窄的潜伏破坏未提及 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。Anthony 章节标注正确；TypeScript 类型谓词与 effect system 作为省略号一笔带过、未展开成成分；**未提 C# 声明式契约（`[NotNullWhen]` 族）这一最接近的成熟替代**；`{String, Not Null}` 混入可空性概念未说明与可空性流分析（已 Active）的关系。 | 已检查 | 未声明 TypeScript/C# 借鉴；省略号掩盖了 effect system 关联的未定状态；与可空性/交并集工作项的亲缘未标注 |

### 设计原则对照

- **与 VB 基因：偏离**。原则 2（`{Integer, Positive}` 非 VB 词汇）、原则 3（第二种做事方式，违反）、原则 4（C# 无先例、且无"充分理由"偏离到无先例地带）、原则 6（为未量化场景预支新类型机制）、原则 7（不健全收窄 = 隐蔽语义变化，`Return?` 教训直接适用）、原则 8（`{...}` 语法冲突）。唯一同向的是原则 9 的目标（值类型守卫样板），但解法不必是 A 的语法。
- **与主线关系：Anthony 独立延伸**（2.3 对照表 `临时交/并集类型` 一行，更激进地叠上"命题"维度）。与主线真实接触面是负向的：踩在 #305（TypeOf 语义困惑）、2018.12.19（TypeCheck / 非匹配需 compelling cases）、以及"模式匹配是 VB 最期待"的既定方向上，试图在模式匹配/流分析建成前抢先定义类型级谓词。
- **破坏性变更：无直接**（无语法落地）；**潜在有**（若以"用户谓词收窄"实现，收窄不健全会造成重编译后新的"编译期相信谓词为真"路径——隐蔽语义变化，且无信任模型对冲）。

### 总评

- **达成程度：未达成**（作为设计）／**部分达成**（作为方向简报）。它成功留下了一个理念观察（收窄 = 谓词承载类型）与一个真问题（值类型精化），但没有交付任何可评估、可演示、可实现的内容；效果维度因"零可运行示例"封顶 2 分。
- **LDM 三态建议：Table**（保持 inactive；谓词型接口机制 Reject；值类型精化以"内建形状 / 契约"形态转交流分析工作项）。
- **主要问题**：① 零设计内容——伪代码非建议语法，Anthony 自述未深入思考；② 谓词型接口机制语义混淆且收窄不健全（编译器无法验证任意用户谓词真伪），信任模型是致命伤；③ 语法 `{Integer, Positive}` 撞负收窄不可表示 + 值类型新实现面 + `{...}` 语法预算超载三重墙；④ 未点明其真实呼吁的是值类型收窄这一 TypeOf 从不覆盖的领域；⑤ 未引主线 #305 / 2018.12.19 / 2017.10.18 先例。

### 返工建议

- **补充章节**：一页"信任模型"——谓词真伪由谁验证、为什么编译器不能收窄在任意用户函数上（对照 TypeOf 的 `isinst` 语义与"启用型"绑定规则）；一页"值类型精化"独立 speclet 草案（内建形状：编译器对 `Integer` 等内建类型的有限分析），作为把方向转化为可评估设计的入口。
- **补充证据**：一个可编译的"检查后获得更强类型信息"演示（哪怕用现有 `TypeOf` 引用类型 + 一个值类型守卫对比）；`count < 0` 守卫 + 断言样板的量化数据；与 C# `[NotNullWhen]` / `[MemberNotNull]` 族的对照表（外部参照，标注来源）。
- **未决问题处理**：把 5 个 OPEN QUESTIONS 归类——① 谓词命名机制：指向层级 2 的契约属性形态；② 语法语义：标注"非建议语法，由内建形状取代"；③ 谓词一致性/异常：作为内建形状的流分析要求，不进入用户语法；④ effect system 关联：写成 TODO（"Skipping a long story" 必须被展开或显式放弃）；⑤ 内建形状归属：转交可空性/流分析工作项。
- **设计探索**：把"值类型精化"与交/并集会议的合取表示、可空性流分析的 Null/NotNull/MaybeNull 状态域放在同一张状态表上，确认内建形状是同一引擎的新状态域；用 TypeScript 类型谓词（函数体不检查）作为"不可信事实源"的反例，论证层级 2（契约属性、编译器验证）是唯一健全路径。

---

## 附录：C# 生态与互操作考量

> 依据：csharplang 索引 T8（C# 15/16 未来方向：unions / closed hierarchies）、M4（type-predicates 与 unions 同向，可借鉴但需识别新元数据）。本提案（类型谓词，流敏感的运行时类型谓词）在 C#/CLR/.NET 生态的对应走向是 **C# 模式匹配的类型测试 + C# 15 unions/closed hierarchies 的穷尽性**。以下 C# 原文均逐字转引自 csharplang 镜像并已核实。

### 相关 C# 现实方向

C# 从未把"任意用户谓词即命名类型"做进类型系统；它对"检查后获得更强类型信息"走的是三条**可验证**的路，恰好与本会议 RESOLUTION #3 的"事实源层级"同构：

**1. 模式匹配的类型测试（`is` / declaration pattern，C# 7 起）＝ 层级 1（内建事实源）的 C# 镜像。**

C# 7 的 `is` 运算符扩展为对 *pattern* 的测试：

> "The `is` operator is extended to test an expression against a *pattern*."（→ `proposals\csharp-7.0\pattern-matching.md`，Is expression）

类型测试的载体是 *declaration_pattern*——"both tests that an expression is of a given type and casts it to that type if the test succeeds"。其运行时语义逐字：

> "The runtime semantic of this expression is that it tests the runtime type of the left-hand *relational_expression* operand against the *type* in the pattern. If it is of that runtime type (or some subtype), the result of the `is operator` is `true`."（→ `proposals\csharp-7.0\pattern-matching.md`，Declaration pattern）

两个对本提案关键的语义事实：① C# 的类型测试收窄的是**模式引入的新变量**（`if (x is string s)` 收窄 `s`，不是 `x`）；② 原文明说：

> "The declaration pattern is useful for performing run-time type tests of reference types"（→ `proposals\csharp-7.0\pattern-matching.md`，Declaration pattern）

——**只针对引用类型**，与 VB `TypeOf...Is` 的覆盖面（引用类型与接口）一致。C# 的类型测试是编译器验证的（`isinst` 语义 + pattern-compatible 静态检查），这就是本会议"层级 1（内建事实源，编译器验证的运行时类型测试）"在 C# 的对应物。

**2. C# 15 unions / closed hierarchies / closed enums ＝ 用「穷尽性」承载"谓词职责"（元数据驱动）。**

C# 对"任意谓词"的答案不是引入谓词函数，而是让"一组封闭的运行时类型"成为可声明的、编译器强制的**事实**。closed hierarchies 摘要逐字：

> "Since all derived classes are declared in the closed class' assembly, a consuming `switch` expression that covers all of them can be concluded to 'exhaust' the closed class - it does not need to provide a default case to avoid warnings."（→ `proposals\closed-hierarchies.md`，Summary）

动机逐字：

> "Closed classes provide a way to indicate that a set of derived classes is complete, and allow consuming code to rely on that for exhaustiveness in switch expressions."（→ `proposals\closed-hierarchies.md`，Motivation）

LDM 2026-04-20 把这条原则钉死：

> "Our guiding principle for `closed` is that it is an exhaustiveness feature, not merely a way of blocking outside inheritance."（→ `meetings\2026\LDM-2026-04-20.md`，Concrete intermediate nodes）

unions 的动机直接把穷尽性当作信任来源：

> "Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive."（→ `proposals\unions.md`，Motivation）

工作组概述把 closed hierarchies / closed enums 概括为"assume only ... can occur, avoiding exhaustiveness warnings"：

> "Allow enums to be declared `closed`, preventing creation of values other than the explicitly declared enum members. A consuming switch expression can assume only those values can occur, avoiding exhaustiveness warnings."（→ `meetings\working-groups\discriminated-unions\union-proposals-overview.md`，Closed enums）

> "Allow classes to be declared `closed`, preventing its use as a base class outside of the assembly. A consuming switch expression can assume only derived types from within that assembly can occur, avoiding exhaustiveness warnings."（→ `meetings\working-groups\discriminated-unions\union-proposals-overview.md`，Closed hierarchies）

Runtime Type Unions 概述逐字（原引文 "all cases types" 为原文拼写，保留逐字）：

> "Patterns apply to the contents of a union. Switches on all cases types are exhaustive:"（→ `meetings\working-groups\discriminated-unions\Runtime Type Unions.md`，Summary）

C# 15 kickoff 确认这是 15 时间窗的主线：

> "We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."（→ `meetings\2025\LDM-2025-08-18.md`，Unions）

**元数据面是互操作义务的核心**（决策文件 M4 判定"需识别新元数据"的落点）。closed 类用 `IsClosedType` 属性落盘，并用 `[CompilerFeatureRequired]` 封锁跨语言派生：

> "Closed classes are generated with an `IsClosedType` attribute, to allow them to be recognized by a consuming compiler."（→ `proposals\closed-hierarchies.md`，Lowering）

> "Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes."（→ `proposals\closed-hierarchies.md`，Blocking subtyping from other languages/compilers）

LDM 2026-02-09 明确这是跨语言闸门：

> "Compilers that do not understand the feature will see the `CompilerFeatureRequired` attribute and block derivation, effectively preventing unauthorized subtyping from older C# compilers or other .NET languages."（→ `meetings\2026\LDM-2026-02-09.md`，Blocking subtyping from other languages）

**3. 声明式契约属性（`[NotNullWhen]` / `[MemberNotNull]` 族）＝ 层级 2 的成熟形态。**

C# 里最接近"谓词类型化"的机制。这些是 BCL 的代码分析属性（`System.Diagnostics.CodeAnalysis`），正文不在 csharplang proposals 中——精确定义属 dotnet/csharpstandard / BCL 范畴，本附录只做**外部参照**，不逐字引用 csharplang 原文（`Suspect`）。它们是参数/返回值的契约标注、编译器只对受支持形状验证、不创建命名类型、不进类型文法——与本会议"层级 2（声明式契约）"的定义逐字吻合。

### 现实 vs 提案

- **兼容（层级 1 ↔ 声明式类型测试）**：VB `TypeOf...Is` 流分析（已 Active）与 C# declaration pattern 同源——都是编译器验证的运行时类型测试（`isinst` 语义），覆盖面同为引用类型/接口（C# 原文 "run-time type tests of reference types"）。唯一语义分叉：C# 收窄**新变量**、VB 收窄**原变量**（"启用型"绑定规则）——跨语言调用时需在桥接层明示（见适应建议 4）。
- **兼容（层级 2 ↔ `[NotNullWhen]` 族）**：RESOLUTION #3 层级 2 的候选接口，C# 已有直接先例；同一批 CodeAnalysis 属性可在两种语言的编译单元间传递，是零摩擦互操作点。
- **脱节 + 需桥接（PROPOSAL A ↔ C# 无对应）**：C# 现实方向用「可声明的封闭类型集合 + 穷尽性」而非「用户谓词函数」承载"谓词职责"——穷尽性检查让编译器在"集合完备"时免去 catch-all，把"值属于哪个 case"当作**类型系统事实**，而不是**用户函数返回 true**。PROPOSAL A（`count As {Integer, Positive}`，接口承载命题）在 C# 连影子都没有：最接近的 `[NotNullWhen]` 是属性契约，`closed`/`union` 是元数据 + 穷尽性。这从 C# 侧独立佐证了 RESOLUTION #2（谓词型接口机制 Reject）与 #3（事实源分层）——**C# 同样拒绝"用户谓词进入类型文法"**。
- **值类型精化（差异化空间）**：C# 唯一覆盖值类型封闭集的是 closed enums——但封闭的是**枚举成员**，不是**谓词**。`Integer >= 0` 这类内建形状（PROPOSAL B）在 C# 同样缺失。VBScript.NET 若走"内建形状/契约"，是 C# 未占的差异化地带，且与 closed enums 的"值类型封闭集"精神同向、不冲突。

### 对 VBScript.NET 的适应建议

1. **识别新元数据（硬义务）**：C# 15 closed hierarchies/unions 落盘 `[IsClosedType]`、`[Union]`、`[CompilerFeatureRequired("ClosedClasses")]` 等。VB 编译器必须识别这些属性才能：(a) 消费 C# 库的 closed/union 类型时正确做穷尽性检查；(b) 阻止从 VB 派生 closed 类型（否则跨语言破坏 C# 的封闭性）；(c) 对 union 的 case 类型正确绑定。这与决策文件 M8 对 unsafe-evolution 的提示（VB 需认识 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`）是同一类义务——**VB 编译器必须跟上 C# 的元数据词汇表**。
2. **默认安全 / 按需动态**：值类型精化走"内建形状 + 契约属性"（编译期验证、无反射），与 C#/AOT/trimming 同向；不要默认把"用户谓词函数"作为类型事实源——那会把动态/反射行为带回 AOT 不友好的路径。若未来要做"任意谓词"的脚本层能力，应限定为按需动态（与 `Any` 晚期绑定同层，显式 opt-in）。
3. **source-gen / 编译期桥**：C# unions/closed 本质是"编译期可静态推理的类型系统职责"（索引 T5）。VBScript.NET 脚本运行时若想与现代 .NET 库互通，应以"编译到受管程序集 + source-gen 桥"为主路径，让脚本层的"谓词"降级为编译期可验证的形状。
4. **模式匹配跨语言语义映射**：C# 收窄模式变量、VB `TypeOf` 收窄原变量。跨语言调用时若 C# 侧类型测试要传导到 VB 侧事实源，需明示"哪个变量被收窄"，避免 pattern variable 语义与 VB 的"启用型"规则错位。

### 对既有 RESOLUTION / 三态判定的影响

- **无实质改变**。C# 现实方向**独立佐证**本会议核心判定：RESOLUTION #2（谓词型接口机制 Reject）——C# 用 `[IsClosedType]` + `[CompilerFeatureRequired]` + 穷尽性检查承载"封闭集合"，未走"接口承载命题"；RESOLUTION #3（事实源分层）——层级 1/2 在 C# 有直接对应；#4（值类型精化以内建形状/契约推进）——C# 仅 closed enums 覆盖值类型封闭集，`Integer >= 0` 内建形状仍是差异化空间。
- **建议补充 TODO（互操作义务）**：在 Implication 加一条——"识别 C# closed/union 元数据（`[IsClosedType]` / `[Union]` / `[CompilerFeatureRequired]`）的能力评估"，作为 VB 编译器消费 C# 15 类型的入口。
- **三态判定维持 Table**。激活信号（信号 1 数据、信号 2 信任模型）不变；C# closed/union 元数据识别可作为与"信号 3 语法预算"并列的互操作前置条件记录。

### 引用纪律与未决项

- 本附录全部 C# 原文均逐字转引自 csharplang 镜像并已用 Grep/Read 核实，来源文件：`proposals\csharp-7.0\pattern-matching.md`、`proposals\closed-hierarchies.md`、`proposals\closed-enums.md`、`proposals\unions.md`、`meetings\working-groups\discriminated-unions\union-proposals-overview.md`、`meetings\working-groups\discriminated-unions\Runtime Type Unions.md`、`meetings\2025\LDM-2025-08-18.md`、`meetings\2026\LDM-2026-02-09.md`、`meetings\2026\LDM-2026-04-20.md`。
- `Suspect`：`[NotNullWhen]` / `[MemberNotNull]` 的精确定义与使用规范属 BCL/csharpstandard 范畴，本镜像无 csharplang 正文，仅作外部参照，未逐字引用。
- **OPEN QUESTIONS**：C# 15 unions 的最终元数据形态（`UnionAttribute` 的具体成员、与 closed hierarchies 的互斥/共存关系）仍在设计中（`proposals\unions.md` 为标注 "Specletdisclaimer" 的草稿）；VB 若做穷尽性检查，与 C# closed 类型的 use-site 诊断对齐策略（LDM-2026-04-20 的"更可帮助的警告"方向）需在实现时对表。
