# Visual Basic Language Design Meeting
August 8, 2026

## Agenda
* [Proposal - Null 字面量与可空推断](#proposal---null-字面量与可空推断)

## Proposal - Null 字面量与可空推断

_Related: [LDM-2014-02-17](../../vblang/meetings/2014/LDM-2014-02-17.md)（2014-01-06 拒绝 `Null` 字面量）· [vbldm-notes-2018.02.07](../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（NRT 推迟决定）· [vbldm-notes-2017.08.30](../../vblang/meetings/2017/vbldm-notes-2017.08.30.md)（NRT 未就绪）· AnthonyDesign section 12「Null and Nothing」/ 18.16「Nullable Reference Types」· ModVB `proposal-null-literal.md`_

### Scenario / 场景与缺口

这次讨论的起点是 Anthony 在 ModVB 建议中提出的 `Null` 字面量：一个与 `Nothing` 区分开的、统一的"真空"字面量，同时表示空引用与空值，**永不**表示值类型的 `CType(Nothing, T)`，并在 `If()` 等表达式的类型推断中强制把结果提升为可空类型（如 `DateTime?`）。

我们一致认为：Anthony 点出的缺口是**真实且长期存在**的。它并非新观察——2014-02-17 的会议记录把问题陈述写得很清楚，我们在 2026 年再看，三条抱怨一条都没过时：

```vb
' ① "I don't know what Nothing means."
' 我期望 Nothing 表示一个可空整数，结果它表示整数 0。
Dim x = If(False, 0, Nothing)

' ② "Nothing doesn't mean what I think it means."
' 我显式指定了类型，期望编译器知道用"可空的 Nothing"，但它没有。
Dim y As Integer? = If(True, 0, Nothing)
```

第三条原话是："No one else knows what Nothing means. MSDN 总说 'Use null (Nothing in VB)'，但这并不准确——Nothing 更像是 default(T)，而 VB 没有 null。" 这句话就是本建议的全部动机。VB 的 `Nothing` 在同一语法位置上既是"空引用"又是"值类型默认值"，双重语义把 `If(False, 0, Nothing)` 推成 `Integer` 0 而非 `Integer?`，这与 C#/大多数语言的 `null` 直觉（`cond ? 0 : null` 推得 `int?`）相反。

Anthony 的目标（proposal 原文，源自 section 12 首个代码块）：

```vb
' `Null` literal for null-references and values.
' Never means CType(Nothing, T) for value types.
? Null

' Forces nullable type inference.
' `endDate` is typed as `DateTime?` not `Date`.
Let endDate = If(HasFinished, EndDatePicker.Value, Null)
```

我们同意，**痛点是真的**。但"痛点真实"与"这个方案正确"之间隔着整整一场设计辩论。我们把它拆成两个可分离的命题：**(1) 是否引入 `Null` 这个新字面量 token；(2) 是否让"空字面量 + 混合值类型"的推断提升为可空类型**。这两件事经常被当成一件，但成本与风险完全不同。下面按候选方案逐一展开。

### PROPOSAL A: 引入 `Null` 字面量（Anthony 方案）

这是 2014 年 PROPOSAL B 的复活，加上"强制可空推断"这个新意。我们把它还原成三条规则（引自 2014 记录与建议原文）：

1. `Null` 是空引用与空值的统一字面量；在要求非可空值类型的上下文使用是**错误**：`Dim a As Integer = Null` 报错，`Dim b As Integer? = Null` 合法。
2. `Dim c = Null` 推断为 `Object`（与现状 `Dim c = Nothing` 在 `Option Strict On` 下推断 `Object` 一致）。
3. 在需要确定一组表达式的 dominant type 时，裸 `Null` 字面量会把非可空值类型候选 `T` 替换为 `Nullable(Of T)`：

```vb
Dim d = If(flag, 5, Null)            ' 候选 Integer → Integer?；d: Integer?
Dim e = {1, 2, Null}                 ' 候选 Integer → Integer?；e: Integer?()
Dim f = {1, "hello", Null}           ' 候选 Integer? 与 String 无主导类型 → Warning: Object Inferred
Dim g = {1L, 2, Null}                ' 候选 Integer?、Long? → 主导类型 Long?；g: Long?()
```

语法层面沿用 2014 的上下文关键字方案：解析到简单名 `Null` 且它不绑定任何命名空间、类型、变量，也没有限定符/索引/字典访问跟随其后，才当作 `Null` 字面量。2014 记录还留了一条有趣的档案：编译器至今已为裸 `null` 产生特殊错误，且我们认真考虑过 `Nothing?` 与 `Nuffink`（后者因为不够严肃而被否决——我们并不总是板着脸开会）。

### PROPOSAL B: 维持 `Nothing` 现状（"什么都不做"）

不引入任何新字面量，`Nothing` 语义不变；以显式类型注解表达可空（proposal 的 Alternatives 第 2 条）：

```vb
' 今天就能写的显式可空表达，零语法成本
Let endDate As DateTime? = If(HasFinished, EndDatePicker.Value, Nothing)
```

配套动作只有文档与诊断改进（在可空目标类型上对裸 `Nothing` 的用法给出更友好的提示）。这是 2014 年实际的选择，也是主线的默认立场。代价是：三重困惑原封不动地传给下一代开发者，`If(False, 0, Nothing)` 依然是 `Integer` 0。

### PROPOSAL C: 复用 `Nothing`，只改进可空感知的推断

不引入 `Null` 关键字，但修改 dominant-type 推断：当集合里出现"空"表达式且其余候选是非可空值类型时，把候选提升为 `T?`。也就是说，把 PROPOSAL A 的第 3 条规则搬到 `Nothing` 上：

```vb
' PROPOSAL C：Nothing 直接推可空
Let endDate = If(HasFinished, EndDatePicker.Value, Nothing)   ' DateTime?
' 反例：今天这类代码推断 Integer，C 通过后会变成 Integer?
Dim existing = If(flag, 0, Nothing)
```

这条路径的优点是不增加第二个拼写；致命缺陷是它对**存量代码**的推断结果做静默改变——`If(flag, 0, Nothing)` 从 `Integer` 变成 `Integer?`，属于比 A 严重得多的破坏性变更，因为 A 只影响写了新关键字 `Null` 的新代码，C 却重写了所有既有 `Nothing` 混合表达式的类型。除非用 langversion 门控，否则不可行。

---

### Trade-offs / 权衡

以下按 LDM 追问清单逐条过。我们把被否定的思路也留档，供后人回看为什么这么走。

#### 1. 语法与文法歧义

上下文关键字方案（未绑定 simple name 才作 `Null`）我们 2014 年就画过边界，技术上可辩护，但有两个现实问题：

- **语义漂移风险**：`Null` 作为"失败的名称绑定"是隐式魔法。用户写 `Dim obj As SomeLib.Null` 或成员名恰好叫 `Null`（第三方库、COM 组件里并不罕见）时，绑定规则必须严格回退；2014 记录已明确"不绑定任何命名空间、类型或变量、且无限定符/索引/字典访问"才算字面量。规则可写，但 IDE 里的语义模型、颜色化、补全要为它新增一类"字面量符号"，Roslyn 管道需要新节点/新 token 分支。
- **VB6/VBA 心智模型冲突**：在 VBScript/VB6 里 `Null` 是**保留字**，语义是 variant 的"无有效数据"（更接近 `DBNull`），与"空引用"是两回事。我们中有人指出，面向把 VBScript 代码迁到 .NET 的用户，`Null` 一词确实更眼熟；但眼熟的是**错误**的语义——把 `Null` 迁过去的人会以为 `Null` 表示 DBNull 式的"无值"，而不是空引用。Suspect：这个迁移群体有多大、能抵消原生 VB 用户的困惑吗？没有数据。

#### 2. 与 `Nothing` 的关系：基因与"第二种做事方式"

这是本建议最重的一击。对**引用类型**，`Null` 与 `Nothing` 在运行时完全等价（都是 `ldnull`），`String`、数组、类实例上没有任何区别——只有 `""` 是空串而非空引用，那是另一回事。也就是说，在绝大多数引用类型场景里，`Null` 不是表达新语义，而只是给同一事物换第二个拼写。

我们对照设计原则第 3 条"不引入第二种做事方式"：空引用早已有 `Nothing`。若 `Null` 只带来拼写冗余，它就该被拒；若 `Null` 要承担区分"空引用 vs 值类型默认值"的职责，那它的真实战场只在值类型上，而值类型场景又必须依赖未定型的 NRT 语义（见第 6 条）。两难：**在引用类型上是冗余，在值类型上是超前**。

我们还讨论了"是否让编译器对用 `Nothing` 写空引用的存量代码发提示，引导改用 `Null`"。我们很快否定了：那会骚扰海量安静的存量用户（"hundreds of thousands of quiet customers primarily want VB to keep doing what it does now"）；不发提示则两种拼写长期并存，新用户会问"到底该写哪个"——这正是我们想消除的困惑，只是把它从 `Nothing` 语义转移到了关键字选择上。

#### 3. 推断与破坏性变更

PROPOSAL A 的规则 3（强制可空推断）对**新代码**是净收益：`If(cond, x, Null)` 推成 `T?`，C# 就是这么做的。但要注意两件事：

- 它对"`Nothing` 在同样位置推 `T`（0）"形成对照，等于在同一表达式里 `Null` 和 `Nothing` 给出不同推断结果。两条"空"路径行为分叉，正是建议自己 Drawbacks 里承认的"概念区分需要学习成本"。
- 若规则 3 未来推广到 `Nothing`（即 PROPOSAL C），则是静默重写存量推断，绝对破坏性变更。我们**不会**在没有 langversion 门控与完整迁移报告的前提下考虑它。

一个被提及但没有深入的角度：`Option Infer` 与 `Option Strict` 的分叉。规则 2 让 `Dim c = Null` 推断 `Object`（与 `Nothing` 一致），这条在 `Option Strict Off` 下没有新问题；但规则 3 在 `Option Strict On` 下把值类型候选提升为 `T?`，后续 `endDate.Year` 直接调用会变成编译错误（`Nullable(Of T)` 没有 `Year` 成员，需要 `.Value`）——这正是特性想要的行为（暴露可空性），但对从脚本世界迁移的开发者是新的仪式。我们确认两条编译选项路径**行为必须一致**，Proposal A 满足这一点；Proposal C 因改变存量而不满足。

#### 4. 角案例

逐条过：

- **值类型默认值**：`Dim a As Integer = Null` → 错误（规则 1），这是 A 与 C# `null` 对齐的关键。我们喜欢这条。
- **可空值类型**：`Dim b As Integer? = Null` → `b` 为无值。与 `b = Nothing` 无差异。OK。
- **字符串**：`Dim s As String = Null` 与 `= Nothing` 等价（空引用），与 `""`（空串）不同。对字符串 `Null` 无增益，只有拼写。
- **数组/集合初始化**：`{1, 2, Null}` 推 `Integer?()`。C# 的 `{1, 2, null}` 也一样。这条规则干净。
- **晚期绑定/COM**：`CreateObject(...)` 返回 `Object`，`obj Is Null` 在晚期绑定下 `Is` 依赖静态类型。VBA 的 `Is Null` 是 variant 状态检查而非引用比较——迁移者会踩坑。OPEN QUESTIONS：`Is`/`IsNot` 是否对 `Null` 全面开放，还是只在 NRT 注解类型上开放？
- **`If x Is Nothing` 语义是否变化**：不变。`Is Nothing` 是引用比较（VB 对可空值类型有特例）；`Is Null` 若与之完全等价，则又是拼写重复；若不等价，语义裂痕更大。我们倾向要求 `Is Null` ≡ `Is Nothing`，并因此认为值类型场景的收益无法兑现到 `Is` 上。
- **参数默认值 / `Optional`**：`Optional p As Integer = Null` 合法吗？`Null` 作为常量默认值在编译期如何表达？建议原文未详述。TODO：若推进必须补。

#### 5. 与既有特性交互

- **`Option Strict`**：见第 3 条，两条路径应一致。
- **`?.` / `??` 家族**：Anthony section 12 的 `Null` 只是"Null 安全全家桶"的一员，旁边还有 `?=`/`?<>`（二值逻辑相等，即 T-SQL `IS [NOT] DISTINCT FROM`）、`??` 合并、后缀 `?` 空值指示、`For Each item In collection?`、`Await someTask?`、`AddHandler obj?.E`。主线对"null 条件 AddHandler"已明确 No Plans（见 2.3 对照表）。我们讨论时反复被拉回一个判断：**若只做 `Null` 字面量而不做家族，价值打折；若做家族，范围爆炸。** 这份建议把它包装成"亮点"，实际是家族的一个前哨。我们按字面量本身评估，不按家族评估。
- **数据库/JSON**：`DBNull.Value` 不是 `Null`。`reader("col") Is DBNull.Value` 的既有模式不受影响，但我们要在文档里白纸黑字写清"数据库 null、JSON null/缺省、DBNull、Null 字面量是四个概念"。We Suspect 很多人会混淆。
- **可空值类型的相等**：2018.02.28 记录提醒我们，VB 的 `=`/`<>` 对可空值类型用三值逻辑，且在某些位置与 C# 行为不同（被称作 "quirks in a good way"）。`Null` 若引入二值逻辑比较（Anthony 的 `?=`），等于把 C# 语义端进 VB——与这条既有 quirk 的相处方式完全没写。TODO：冲突矩阵。

#### 6. NRT 依赖与历史先例

这是我们最不能绕过的部分。

- **2014-01-06 已 Reject**：proposal 的 PROPOSAL B 当时被否，理由今天读来依然成立（语法魔法、`Nothing` 之外的第二个空、对新手更困惑——"Nothing?" 反而比 `Null` 更像 VB 而也被拒）。本建议没有提供推翻那次否决的新证据，只提供了"强制可空推断"这一个增量。
- **2018.02.07 主线对 NRT 的决定**：逐字引用——"There are probably a significant number of VB (and C#) projects where retrofitting this behavior is not desirable ... it may feel like a 'not VB' thing as we understand its usage. We'll postpone this until we understand the uptake in C#." VB 是否引入可空引用类型，主线明确选择**等 C# 采用率被观察清楚**再动。这是我们要的保守参照。
- **2017.08.30**：NRT "Not ready yet"，还点名 `!` 运算符与 VB 的字典访问 `dict!key`、单精度类型字符 `Dim radius!` 冲突。
- **Anthony 自认未定型**（section 12 顶注与 18.16）："Nullable Reference Types 本应是本节亮点，但收到反馈称其需要进一步迭代以降低困惑与恼人程度，我正在修订"；18.16 直说"NRT 在 C# 口碑混合，VB 用户表达困惑与担忧；如果用户讨厌它、关掉它、或劝别人别用，那就是失败，我必须修正设计。"

把这四条放在一起，我们的立场非常清晰：**`Null` 字面量是 NRT 语义的地上建筑，而 NRT 的地基主线没打、Anthony 自己还在重画**。把一个新字面量建立在未定型的语义上，是"把未决问题的成本前置到用户身上"。OPEN QUESTIONS：若 Anthony 修订后的 NRT 不再需要"字面量与推断"分离（比如可空标注走类型注解路线），`Null` 字面量还剩多少价值？

#### 7. CLR 约束

CLR 没有 null 字面量概念：引用类型空值就是 `ldnull`，可空值类型的空值是装箱为 null 引用。`Null` 字面量纯粹是编译期映射，不触 PEVerify、不触 CLR 存储规则——这条不构成障碍。真正的 CLR 层问题是 **NRT 表面标注跨程序集传播**（2018.02.07 Part 2 已指出"实现层面不会像管理属性那么简单"）。`Null` 的字面量身份不阻碍，但 nullability 标注才是那个硬骨头，又一次把问题推回 NRT。

#### 8. IDE/IntelliSense 影响

补全输入 "Nu" 会同时弹出 `Null` 与 `Nothing`，两者相邻、语义却不同——对新手是新的困惑点而非解脱。可空推断的可见性（`endDate` hover 显示 `DateTime?`）依赖 NRT 的 IDE 全链路（波浪线、quick actions），而那条链路在 VB 里根本还没建。语义模型要为 `Null` 引入新的 literal symbol 概念，Roslyn 改动覆盖 parser→binder→type-inference→IDE 四层。

#### 9. 数据/普遍性

痛点有**轶事证据**（2014 三条原话），但**没有量化数据**：我们没有用户请求数、没有 Stack Overflow/issue 的统计、没有迁移者调查。主线 2018 对 NRT 的顾虑（"it may feel like a 'not VB' thing as we understand its usage"）到今天没有被反驳。We think：在有数据之前，任何"普遍性"宣称都是未经验证的假设。

#### 10. 更简替代

- 显式注解（Proposal B/Alternatives 2）：`Let endDate As DateTime? = ...`，零语法成本，仪式恰好在我们觉得"显式关键字在有助理解时是美德"（原则 10）的范围内。它不解决 `If(False, 0, Nothing)` 的推断问题，但那个问题可以用 **analyzer + 诊断**缓解（对混合 `Nothing` 表达式建议显式注解），不动语言、不破坏代码。
- 我们可以把"可空感知推断"做进分析器诊断而不是语言：在 `If(cond, value, Nothing)` 且结果为 `T` 时给出提示"此 `Nothing` 将被解释为默认值；若期望可空，请显式注解 `As T?`"。This gives 80% 的价值 without 语言改动。

#### 11. 成本/优先级

编译器四层改动 + NRT 依赖 + IDE 全链路，这是一个大特性。它又必须排在 NRT 之后。主线至今没有 NRT 时间表，Anthony 的 NRT 修订版也没有交付。**优先级在可预见的窗口内是"等上游"**。缩小范围（比如只做规则 3 的推断、不引入 token）能显著降本，但那正是 PROPOSAL C，它的存量破坏又把它推回 langversion 门控的老问题。

#### 12. 值不值得做：价值 × 成本 × 风险

- **价值**：真实但未量化。对齐 C#/主流语言的 `null` 直觉，对迁移者友好是加分项。
- **成本**：高。四层管道 + 与 NRT 强耦合。
- **风险**：中高。基因冲突（第二个拼写）、迁移心智模型冲突（VB6 `Null`）、推断破坏（若推广到 `Nothing`）、以及把未定型 NRT 语义固化进新语法。

### VB 基因对照

我们逐条对照设计原则：

- **原则 2「保持 VB-like」**：`Nothing` 是 VB 招牌词，读起来是普通英语单词（"nothing"）。`Null` 是行话/外来词。2014 记录里连 `Nothing?` 都被嫌"looks strange"。
- **原则 3「不引入第二种做事方式」**：**违反**。空引用已有 `Nothing`；`Null` 对引用类型完全等价，是纯粹的第二拼写。这是否决它的最重砝码。
- **原则 5「读起来像英语、对新手友好」**：`Nothing` 胜出。`Null` 对首次开发者不更直觉，反而与 VB6/VBA 的 `Null` 语义相撞。
- **原则 4「默认跟随 C#，除非有充分理由」**：VB 用 `Nothing` 而非 `null`，本身就是一次"充分理由偏离"的既有决定（VB 味优先）。本建议要反向撤销它，但没有给出新的充分理由。
- **2.3 主线对照表**：`Null 字面量` 一行明确写着——主线 2014 拒绝，Anthony 重点推进，"主线保守，Anthony 激进"。本建议属于 **Anthony 独立激进延伸，与主线冲突**，且未携带可反驳主线的证据。

一条值得保留的 nuance：在 **VBScript.NET** 的语境里，`Null` 是 VBScript 的既有关键字，对它的一部分目标用户"更亲"。但正如第 1 条所述，亲的是错误的语义（variant 无值 vs 空引用）。我们把这条记为 Suspect，需要迁移者调查才能定论。

### 诚实分层

- **事实**：2014-01-06 已 Reject；2018.02.07 主线对 NRT"推迟到观察 C# 采用率"；2017.08.30 NRT 未就绪且 `!` 冲突；Anthony section 12 顶注与 18.16 自认 NRT 需修订；CLR 无 null 字面量概念，`Null` 是纯编译期映射；VB 的 `Nothing` 双重语义问题有 2014 三条原话佐证。
- **Probably**：到 2026 年，C# 的 NRT 已被主流采用（对照 2018 年的"等待信号"），但 C# 的采用率不等于 VB 用户想要它；VB 用户对 NRT 的接受度数据我们拿不到。
- **Suspect**：VB6/VBA 迁移者对 `Null` 的语义预期（DBNull 式而非空引用）；第三方库/COM 成员名 `Null` 的冲突比例；"引用类型上 `Null` 与 `Nothing` 完全等价"是否让新字面量在大半代码里失去意义。
- **OPEN QUESTIONS**：① `Is`/`IsNot` 对 `Null` 的开放范围；② `Null` 在 `Optional` 参数默认值、`Select`/`Case` 匹配中的语义；③ `Null` 与修订后 NRT 的关系；④ 是否值得单独把"可空感知推断"从 NRT 中解耦出来研究。
- **TODO**：跟踪 Anthony NRT 修订；写一份更窄的"可空感知推断"独立探索文档；为"Nothing 双重语义"收集量化数据；为 PROPOSAL C 的存量破坏准备 langversion 门控评估。

### RESOLUTION:

我们将本建议整体标记为 **Table**（暂缓），并拆分出两条不同轨迹：

1. **`Null` 字面量关键字：维持 2014 年 Reject 立场，不恢复。** 空引用/空值的拼写，VB 已经有 `Nothing`；`Null` 在引用类型场景与之完全等价，制造"第二种做事方式"；在值类型场景又依赖未定型的 NRT 语义。它把未决问题的成本前置到用户身上，与"几乎从不破坏、不引入第二种方式"的基因冲突。2014 的否决没有被新证据推翻。

2. **可空感知的 dominant-type 推断：作为独立、更窄的方向继续 Consider。** 真正的新意不在 `Null` 这个 token，而在"混合值类型与一个空表达式时把候选提升为 `T?`"（PROPOSAL A 规则 3 / PROPOSAL C 的精神）。这条可以脱离 `Null` 单独探索，也可以作为 NRT 的配套规则；但必须与 NRT 解耦、并在 NRT 方向明朗后再谈承诺。

**Implication:**
- 建议状态标注为 `LDM Table`；在 proposal 头部加一行"LDM 2026-08-08: Table，理由见会议纪要"。
- 更新 2014-02-17 拒绝记录的历史引用，注明"2026-08-08 再次评估后维持拒绝"。
- 分解出独立探索项："可空感知推断（不含新字面量）"，由 NRT 迭代带动，暂不进入 Active。
- 建议作者补充量化数据、Roslyn 语义模型方案、与 `Nothing` 的完整差异矩阵，供将来重估。

**Verdict: Table**（字面量部分 Reject / 推断部分 Consider）。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-null-literal.md` — 引入 `Null` 字面量，统一空引用与空值；永不表示值类型默认值；在 `If()` 等场景强制可空推断。
- 来源：Anthony 原文章节 12「Null and Nothing」（首个代码块），隐含 NRT 章节 12/18.16。
- 配方目标：解决 `Nothing` 双重语义（空引用 vs `default(T)`）造成的困惑，用 `Null` 对齐 C# 的 `null` 直觉。

### 五维评价

| 维度 | 得分 | 评价 | 证据等级 | 问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 目标改进清晰（Nothing 双重语义有 2014 原话佐证）、示例可演示且能编译（`If(HasFinished, EndDatePicker.Value, Null)` → `DateTime?`）；但证据止于书面：状态栏为占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`），无原型、无运行结果；关键子效果（与 NRT 的关系）作者自认未定。 | 已提供 / 已检查 | 效果未达"已运行"；NRT 未定型导致核心语义悬空 |
| 特性 | 2/5 | 核心是照搬 C# `null` 概念做"真·空"字面量，未做充分 VB 化（`Nothing` 才是 VB 基因，`Null` 是第二拼写，违反原则 3）；引用类型场景两者运行时等价，纯拼写冗余；同时打包 NRT 依赖。上下文关键字规则与"永不表示 `CType(Nothing,T)`"算有限的 VB 化尝试，故不判 1。 | 已检查 | 外来特性为主，VB 化不足；绑定无关职责（NRT） |
| 品质 | 3/5 | 结构符合 vblang 六章节模板；语法边界含糊（无 BNF、无上下文关键字判定流程）；无兼容性/breaking change 分析；未决问题 ≥4 个关键设计点且被轻描淡写；状态栏占位链接。 | 已检查 | 缺 BNF、缺兼容性分析、未决问题低估、占位符 |
| 属性 | 3/5 | 有得有失：得=解决真实困惑、对齐主流 null 直觉（雷/光）；失=与主线断裂（2014 Reject 维持）、推断破坏性风险、与未定 NRT 耦合（风/暗）。文档未充分权衡这些灵气维度。 | 已检查（待定） | 破坏性/一致性断裂风险未被文档识别；实际影响须"已采纳"后才可判定 |
| 炼金成分 | 3/5 | 来源标注部分准确（proposal 提及"更贴近 C# 的 null 直觉"，Drawbacks/Alternatives 如实）；但借鉴 C# 未展开，NRT 借鉴与自认未定只一笔带过；`Null` 字面量 + 推断 + 潜在 null-safe 家族边界模糊，混入杂质。 | 已检查 | 成分混合、NRT 来源标注弱 |

### 设计原则对照

- **与 VB 基因：偏离**。违反"不引入第二种做事方式"（原则 3）与"读起来像英语"（原则 5）的优先级；`Nothing` 是招牌词，`Null` 是行话。
- **与主线关系：与主线冲突**（2.3 对照表：主线 2014 拒绝、Anthony 激进推进）。
- **破坏性变更：有（间接）**。新关键字本身对旧代码安全（上下文关键字），但"强制可空推断"改变新代码 `If()` 推断结果（`Date`→`DateTime?`），且一旦推广到 `Nothing`（PROPOSAL C）即为存量破坏；文档未给出 langversion 门控/迁移报告。

### 总评

- **达成程度：部分达成**——痛点真实且被清晰陈述，示例自洽；但方案核心（新字面量）与基因冲突、建立在未定型 NRT 之上、证据止于书面。
- **LDM 三态建议：Table**（字面量 Reject / 推断部分 Consider，解耦后）。
- **主要问题**：① 依赖未定型的 NRT（Anthony 自认修订中）；② 违反"不引入第二种做事方式"；③ 强制可空推断的破坏性与兼容性分析缺失；④ 证据止于"已提供/已检查"（占位状态、无原型、无数据）；⑤ 与 VB6/VBA 的 `Null` 语义冲突，迁移者心智模型方向相反。

### 返工建议

- **补充章节**：语法规范（上下文关键字判定的 BNF/流程）、兼容性分析（langversion 门控、存量影响矩阵、`Nothing` 是否发提示的策略）、与 `Nothing` 的完整差异矩阵（按引用类型/可空值类型/字符串/数组/参数默认值/`Select Case` 逐格）、NRT 关系专节。
- **补充证据**：量化"Nothing 双重语义"的困惑数据（issue/论坛/迁移问卷）；Anthony NRT 修订版交付后重估语义基础；引用类型场景"拼写冗余"比例的抽样。
- **未决问题处理**：把三个 OPEN QUESTIONS（`Is` 开放范围、`Optional`/`Case` 语义、与 NRT 关系）逐一给出定夺方式；把"可空感知推断"拆为独立窄建议，脱离 NRT 先行探索规则 3 的可行性。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的 C# 生态考量，不改写正文。评估对象与正文一致：`Null` 字面量 + 可空感知推断。所有 C# 引用均逐字取自 csharplang 镜像并标路径；无法核实的标注 **Suspect** 或 **OPEN QUESTIONS**。

### 相关 C# 现实方向

本提案主题在 C#/CLR/.NET 生态中的对应物，主要不是低层互操作主线（Span/指针/COM/AOT），而是 **C# 8 可空引用类型（NRT）** 与 **C# 7.1 的 `default` 字面量**——属于"语言语义演进"层面，但有一处硬性互操作接触点（NRT 标注元数据跨程序集传播，见下）。

**C# 8 NRT**（`proposals\csharp-8.0\nullable-reference-types.md`）：
- 目标（首节，两枚 bullet）：「Allow developers to express whether a variable, parameter or result of a reference type is intended to be null or not.」「Provide warnings when such variables, parameters and results are not used according to that intent.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 推断规则（Type inference）：「In type inference, if a contributing type is a nullable reference type, the resulting type should be nullable. In other words, nullness is propagated.」→ 同上
- 但 **null 字面量本身**是否贡献 nullness，speclet 记为待定：「We should consider whether the `null` literal as a participating expression should contribute nullness. It doesn't today: for value types it leads to an error, whereas for reference types the null successfully converts to the plain type.」附例 `var z = b ? 7 : null; // Error today, could be int?` → 同上（Type inference）
- 空值抑制运算符（Checking of nullable references）：「A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator)…」→ 同上——印证正文引用的 2017.08.30 判断：C# 把 `!` 用作 NRT 抑制符，而 VB 的 `!` 已被字典访问占用。
- 元数据（Metadata representation）：「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」→ 同上

**C# 7.1 target-typed `default` 字面量**（`proposals\csharp-7.1\target-typed-default.md`）：
- C# 把 `null` 与 `default` **分离**：`default` 是 `default(T)` 的省写（Summary）：「The target-typed `default` feature is a shorter form variation of the `default(T)` operator, which allows the type to be omitted.」→ `proposals\csharp-7.1\target-typed-default.md`（Summary）；推断规则（Detailed design）：「The inference of the type for the *default* literal works the same as that for the *null* literal, except that any type is allowed (not just reference types).」→ 同上（Detailed design）
- 关键 Alternatives：「Extending the null literal: This is the VB approach with `Nothing`. We could allow `int x = null;`.」→ 同上（Alternatives）——C# 考虑过"把 `null` 扩展成 VB `Nothing` 式默认值"并**拒绝**，改走独立 `default` 字面量。

### 现实 vs 提案

- **语义方向：兼容且同向**。Anthony 的 `Null`「永不表示 `CType(Nothing, T)`」正是 C# `null` 的既有语义；`Nothing` 保留为 `default(T)`，正是 C# `default`。C# 的 null/default 分离结构，就是本提案想在 VB 复刻的语义模型；`If(cond, x, Null)` 推 `T?` 对应 speclet 的 "nullness is propagated"。
- **机制不同、迁移成本不对称**。C# 的分离**从 C# 1.0 起内建**（`null` 一直在，C# 7.1 才补 `default`），零迁移成本；VB 的 `Nothing` 双重语义是**历史遗产**，要在既有统一字面量上叠加 `Null`，等于给存量语言做"语义分叉手术"。C# 拒绝的 "VB `Nothing` approach"（`int x = null`）恰是现状 VB `Nothing` 的行为——连 C# 都不要"null = 值类型默认值"，这支持"`Nothing` 双重语义是异类"的动机；但 C# 的解法（从第一天就分离）VB 没有起点，强行叠加即制造第二拼写。
- **推断承诺：本提案比 C# speclet 记录得更激进**。正文第 3 条说"C# 就是这么做的"；更准确的说法是：对**引用类型** nullness 传播已落地（`string?`），对**值类型**的 `b ? 7 : null`，speclet 记的是 `Error today, could be int?`——正文规则 3 走的正是那条"could be int?"的**未落地**路径。Suspect：speclet 的 "today" 指写作时的 pre-NRT；最终发布行为本镜像无记录，未核实。
- **低层 interop：关系弱，无直接冲突**。本提案不涉及 Span/指针/函数指针/COM/AOT 任意主线；CLR 层无非 `ldnull` 与可空装箱（正文第 7 条已讲清）。与索引 T7（dynamic/晚期绑定边缘化）仅在对 VBScript 脚本层定位上有间接关系，不构成约束。

### 对 VBScript.NET 的适应建议

- **识别 C# NRT 元数据**：C# 把 nullability 以属性形式放入元数据（见 speclet 原文）。`.vbx` 必须**识别**这些标注，否则消费 C# 库时 nullability 信息丢失、`Null`/`Nothing` 语义无法跨语言校验。OPEN QUESTION：具体属性名（实现层面为 `System.Runtime.CompilerServices.NullableAttribute` / `NullableContextAttribute`）来自 dotnet/runtime，speclet 仅写 "attributes"，本镜像未收录。
- **产物反向互通**：若 `.vbx` 编译产物被 C# 消费，应**按 C# 约定的属性形式产出 nullability 标注**，保持 IDE/分析器链路（波浪线、quick actions）一致——与决策文件 M2 的"类型化出口"一致。
- **破坏性用 opt-in 管理（参照 C# 先例）**：speclet Breaking changes 逐字：「Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism.」→ `proposals\csharp-8.0\nullable-reference-types.md`。若推进 RESOLUTION 第 2 条"可空感知推断"，`.vbx` 应限定在显式 NRT 感知上下文 / langversion 门控内，与 C# 的 opt-in 模式同构，避免 PROPOSAL C 的存量破坏。
- **`default`↔`Nothing`、`null`↔`Null` 映射表**：即使不采纳 `Null` 字面量，也应在文档与诊断中提供这张映射，降低 C# 迁移者的语义误解。
- **脚本层保持动态**：VBScript 的 COM/Office 晚期绑定场景应保留；`Null` 若与 `??`/`?.` 家族一起进，interpreted 模式走动态、编译产物走类型化——与决策文件 M2/M5 的双模路线一致。

### 对既有 RESOLUTION/三态判定的影响

- **不改变判定**：仍为 Table（字面量 Reject / 推断 Consider）。C# 现实（NRT 主流化）**强化**"痛点真实"，但**不推翻** 2014 Reject 的理由（第二拼写、VB 基因、迁移心智模型）——C# 没有第二拼写问题，它从一开始就有 `null`。
- **强化第 2 条**：C# speclet 的 "nullness is propagated" + `b ? 7 : null` 案例证明"空表达式提升可空"是可参照的设计先例，且 C# 用 opt-in 管理了破坏性——为 `.vbx` 的"可空感知推断（不含新字面量）+ langversion 门控"探索项提供了直接参照。
- **一处证据修正（建议，非阻断）**：正文第 3 条"C# 就是这么做的"表述偏强；更准确的是"C# 对引用类型这样做（`string?` 已落地）、对值类型的 `b ? 7 : null` 在 speclet 里记为 could be int?（未核实发布行为）"。不改变结论，只提升证据等级。
- **C# 先例反而支持字面量部分 Reject**：C# 逐字拒绝 "Extending the null literal: This is the VB approach with `Nothing`"（保持"严格 null"），但那是靠"从第一天就有 `null`"实现的。两条 C# 证据合起来恰好支撑 RESOLUTION 的拆法：**语义上对齐 C#（严格空 + 可空推断），语法上不引入第二个字面量**。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「The goal of this feature is to:」「Allow developers to express whether a variable, parameter or result of a reference type is intended to be null or not.」「Provide warnings when such variables, parameters and results are not used according to that intent.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「In type inference, if a contributing type is a nullable reference type, the resulting type should be nullable. In other words, nullness is propagated.」→ 同上
- 「We should consider whether the `null` literal as a participating expression should contribute nullness. It doesn't today: for value types it leads to an error, whereas for reference types the null successfully converts to the plain type.」→ 同上；附例 `var z = b ? 7 : null; // Error today, could be int?`
- 「A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator)…」→ 同上
- 「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」→ 同上
- 「Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism.」→ 同上
- 「The target-typed `default` feature is a shorter form variation of the `default(T)` operator, which allows the type to be omitted.」→ `proposals\csharp-7.1\target-typed-default.md`
- 「The inference of the type for the *default* literal works the same as that for the *null* literal, except that any type is allowed (not just reference types).」→ 同上
- 「Extending the null literal: This is the VB approach with `Nothing`. We could allow `int x = null;`.」→ 同上（Alternatives）

### OPEN QUESTIONS / Suspect

- **Suspect**：C# `b ? 7 : null` 的**最终发布行为**（speclet 记 "Error today, could be int?"，写作时指 pre-NRT；本镜像无后续决议记录，未核实最终是否落地为 `int?`）。本附录已按 speclet 逐字标注，未声称最终行为。
- **OPEN QUESTION**：NRT 标注的具体属性名（`NullableAttribute` / `NullableContextAttribute`）来自 dotnet/runtime 实现，csharplang 镜像的 speclet 仅写 "attributes"，未点名。`.vbx` 若做元数据识别，需以 dotnet/runtime 为准。
- **关系弱申明**：本提案与 C# 低层 interop 主线（T2 Span/ref、T3 指针/函数指针、T4 COM、T5 AOT/trimming、T6 source-gen）**无直接冲突**；与 T7（dynamic/晚期绑定边缘化）仅在对 VBScript 脚本层定位上有间接关系（不构成约束）。本附录因此聚焦 NRT 与 `default` 字面量两个真正的语义对应物。
