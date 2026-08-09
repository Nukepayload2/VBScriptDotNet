# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们翻出 Anthony 第 18 章里一份标注为"实验性 / 未定稿"的念头：按 `Structure` 约束重载。它不是一份成熟提案，而是一个"待办"——一句"考虑如何从源码中移除这个假参数，即使在元数据里仍需要它"挂在 cheat 示例后面。我们的任务不是替它背书，而是判断：这个想法值得激活，还是继续搁置。

## Agenda

* [Proposal: `Structure` 约束重载（inactive，第 18 章实验性想法）](#proposal-structure-约束重载inactive第-18-章实验性想法)

## Proposal: `Structure` 约束重载（inactive，第 18 章实验性想法）

_Related: 主线 [#170 – 重载决议 Tie-Breaker](https://github.com/dotnet/vblang/issues/170)；[#228 – Default property for reading and setting bit flags](https://github.com/dotnet/vblang/issues/228)；[#211 – 使用任意已安装语言](https://github.com/dotnet/vblang/issues/211)；ModVB：`proposal-nullable-reference-types.md`（inactive）、`proposal-nullability-flow-analysis.md`、`proposal-module-enhancements.md`_

### 场景与缺口

We started from a fact that every VB 泛型作者迟早撞上：**你不能按泛型约束重载方法**。`Class` 与 `Structure` 的两种实现无法写成两个同名方法，因为元数据里方法的签名是"名 + 参数表 + 泛型元数"，**约束不是签名的一部分**——两个只差约束的方法在 CLR 眼里是同一个方法，会撞重复定义。

但 VB 有一个现成的、能绕过它的惯用法。因为 **VB 在判定哪些重载"可应用"时会考虑类型参数约束**，给其中一个重载补一个可选假参数就能让两个方法签名错开，并且调用点按值/引用类型正确分派：

```vb
' （众所周知……）不能按泛型约束重载。这是非法的：
' Sub M(Of T As Class)(p As T)
' Sub M(Of T As Structure)(p As T)

' 但这不是（合法，且行为正如预期）：
Sub M(Of T As Class)(p As T)
    Console.WriteLine("reference")
End Sub

Sub M(Of T As Structure)(p As T, Optional ignored As Object = Nothing)
    Console.WriteLine("value")
End Sub

M("")   ' 命中引用类型重载。
M(1)    ' 命中值类型重载。
```

Anthony 的动机一句话带过：处理泛型时，"有时希望为值类型和引用类型提供不同实现，尤其是涉及任何可空性时"（"especially when any kind of nullability is involved"）。期望结果是让这种写法更整洁：方法作者不用写假参数，调用者也不用看见它。原文把它明确标为"待办"（to-do）：**考虑如何从源码中移除这个假参数，即使在元数据里仍需要它。**

We 确认这里有一个真实但很窄的缺口：cheat 的假参数 `ignored` 暴露在公共 API 签名里，调用者可能误传，作者要为每个需要分派的方法写一份"装饰"签名。但我们也立刻注意到：**缺口是"化妆"层面的，不是"能力"层面的**——该能力今天已经能编译、能运行、行为正确。这决定了整场讨论的走向：我们在评估的是一个纯粹的表象改进，它的成本却要落在编译器和 Roslyn 的骨头里。

### 候选方案

**PROPOSAL A — 编译器识别"判别参数"，源码级语法糖。** 让两个源码签名相同的按约束重载合法，编译器自动为其中一个（`Structure` 版本）补一个元数据判别参数。源码里看不到假参数，元数据里保留。这是建议原文暗示的方向。

**PROPOSAL B — 真·按约束重载（突破 CLR 签名规则）。** 让运行时/CLR 支持按约束分派的方法，签名规则放宽，不需要判别参数。改动横跨元数据签名规则、JIT 调度、PEVerify。这超出 VB 单方能力。

**PROPOSAL C — 维持现状，把 cheat 文档化 + analyzer。** 保留 `Optional ignored As Object = Nothing`，但把它写成正式惯用法：文档说明何时适用，analyzer 检测误传、提示更清晰的替代（运行时 `IsValueType` 分派或拆分方法名）。

**PROPOSAL D — 方法内运行时分派（零语言成本）。** 不写两个方法，一个方法内用 `GetType(T).IsValueType` 分叉。作者得到一个 `T` 类型上的运行期分支，调用者看到单一方法：

```vb
Sub M(Of T)(p As T)
    If GetType(T).IsValueType Then
        ' 值类型路径
    Else
        ' 引用类型路径
    End If
End Sub
```

**PROPOSAL E — 拆分方法名 / 扩展方法。** `MRef`/`MVal` 或 `MReference`/`MValue`，明确、无魔法，但污染 API 表面，且调用者必须记得选对名字。

### 权衡：Q&A

- **A 到底买了什么？** 唯一的收获是"看不到假参数"。能力、分派语义、元数据形态，A 与今天的 cheat 完全相同。We 认为，当语言特性只改变"谁看见什么"而不改变"能做什么"时，它必须非常便宜，或者非常频繁，才值得。A 两者都不占。
- **B 为什么不考虑？** 这是整场最容易回答的一个。"Fantastic idea, and too hard to do."——这句出自主线 [#211](https://github.com/dotnet/vblang/issues/211) 的会议记录，原话用于评估"使用任意已安装语言"，但我们一致认为它同样精确地描述了 B。它需要"rewriting considerable parts of Roslyn"，并且首先需要运行时层面改签名规则——那属于 C# LDM 与运行时团队的领地，主线原则是"CLR/库层变更直接委托 C# LDM"。B 不因价值不足被否，因不可由 VB 单方执行而搁置。
- **A vs D：编译期拆分值得吗？** 值得，但只在需要"编译期就知道走哪条路"时才值得。D 的代价是两条路径都被编译、`GetType(T)` 是运行期判定、无法在编译期对路径做类型检查。A 的代价是整套隐藏参数机制。We 的中间判断：**大部分真实场景不需要编译期拆分**——序列化、Option/Result 风格包装器里，"分派"本身只是选择实现，运行期 `If` 足够。需要编译期拆分的场景（例如两条路径的签名/返回类型必须不同）恰好是 A 给不了的东西——因为判别参数不改参数表之外的一切。`Probably`：A 的真实受益面比它看起来窄得多。
- **A 的"隐藏"是 VB 味儿吗？** 争论很激烈。一方认为：VB 从不羞于在元数据里做手脚换取源码整洁（默认属性、`Module` 的命名空间平面化、`Overloads` 的隐式修饰符都是先例），隐藏一个纯内部判别参数与此同族。另一方认为：VB 的核心是"读起来像英语、对新手友好"，而一个**你调用时看不见、反射时看得见、C# 消费者看得见**的参数，正是原则所警惕的"隐蔽语义"——它让"这个方法的签名到底是什么"取决于你在哪一层观察。We 倾向于后者，但 We 承认前者的先例是真实的。这正是需要更多设计探索的原因，而不是直接拍板。
- **C 是不是答案？** 我们反复回到一个主线口头禅："Treat it as an optimization, not a feature."（评价标准 1.4 归纳的主线模式）。cheat 已经存在，缺的是**纪律与文档**，不是语法。C 用一个 analyzer 就能处理"误传假参数"和"建议改名"，不动语言。对一份 inactive 提案，C 是我们认为本周唯一有产出物的路径。
- **D 与 A 的边界**：如果未来真要做编译期分派，D 的语法形态（单方法 + `If GetType(T).IsValueType`）已经给出了一半答案——把它静态化（约束条件体）可能比"隐藏参数"更 VB。We 把这记进激活信号。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

A 需要一个声明期特例：两个源码签名相同的成员，在什么条件下被允许并存？`Overloads Sub M(Of T As Class)(p As T)` 与 `Overloads Sub M(Of T As Structure)(p As T)` 是唯一的"合法碰撞"形态，还是任何约束组合都行（`IComparable` vs `IFormattable`）？如果只允许 `Class`/`Structure` 对，它是硬编码规则——VB 已经有一个"只有 `Class` 和 `Structure` 是互斥约束"的事实，这算自然边界；但如果放开，签名区分就失去锚点。`OPEN QUESTIONS`：判别参数附加到哪一方、判别参数放参数表哪个位置、是否要求 `Overloads` 关键字显式写出。语法本身没有歧义——它**在文法上就是非法声明**，要新加的是"允许这种声明"的特例，而不是新记号。但正是因为它没有记号，编译器只能靠"签名相同 + 约束互补"来猜测意图，而这正是声明绑定最不该做的猜测。

#### 2. 角案例与边界语义

**无约束 `T` 的天生空洞（最尖锐）。** 约束分派只在"调用点的 `T` 已知是值类型或引用类型"时成立。当调用方自己的 `T` 无约束时，编译器无法判定：

```vb
Class Holder(Of T)
    Private value As T

    Sub Use()
        ' 错误：T 无约束——编译器不知道 T 是值类型还是引用类型，
        ' 两个按约束重载在此都不可应用。这是"按约束分派"的天生空洞。
        M(value)
    End Sub
End Class
```

这是整份提案最致命的一角：**泛型库最需要这种分派的地方（把调用转发给约束分派）恰好用不了**。`OPEN QUESTIONS`：此处报错、还是引入无约束兜底重载（会破坏二分性）、还是运行时降级？原文完全没有触及。

**可空性动机的可疑展开。** `Nullable(Of Integer)` 本身是 `Structure`，所以：

```vb
M(Of String)(Nothing)      ' String 是引用类型 ⇒ 引用重载，p = Nothing。
M(Of Integer?)(Nothing)    ' Nullable(Of Integer) 是 Structure ⇒ 值类型重载。
```

一个"为 null 的值类型"（`Integer? = Nothing`）会走进值类型重载。作者若指望值类型重载处理"非 null 的真实值"，这里就是语义陷阱。原文说"especially when any kind of nullability is involved"却未展开——`Suspect`：这是动机里最含糊的一环。主线对可空值类型的态度是保留 VB 独有怪癖："Nullable value types also works some place in VB where it doesn't in C#."（[C# Issue #967](https://github.com/dotnet/csharplang/pull/967)，2018.02.28 会议谈元组相等时顺带确认）。但把"可空"作为本特性的支点，恰好是 ModVB 可空引用类型（inactive #90）与可空性流分析已接管的地盘——动机重叠，而不是互补。

**`AddressOf` / 委托签名断裂。** 今天的 cheat 里，`Structure` 重载有 2 个参数，`AddressOf M` 到 `Action(Of Integer)` 本来就绑不上（形参表不匹配）——但这在 cheat 里是**可见、诚实**的。A 把它隐藏之后，作者会自然期待 `AddressOf M` 能按 `Action(Of String)` / `Action(Of Integer)` 区分两个重载，但 `Action(Of Integer)` 只有一个参数，永远匹配不了 2 参数的判别签名。`OPEN QUESTIONS`：隐藏参数后 `AddressOf`、方法组转换、`nameof(M)` 的语义。

**尾随可选参数 / `ParamArray` 碰撞。** 判别参数必须是 `Optional ... = Nothing` 且必须放末尾（否则位置实参整体错位）。作者自己的方法若已有尾随可选参数或 `ParamArray`，判别参数往哪放？cheat 今天已经撞这个墙，A 不解决它，只是把它藏得更深。

**ByRef / copy-in-copy-out。** `ByRef p As T` 与判别参数无交互（判别参数是值传递的可选参数），`OPEN QUESTIONS` 程度较轻，但按约束分派 + `ByRef` 的组合需要一条明确规则，防止 `M(x)` 之后 `x` 的类型在调用方眼里变来变去。

#### 3. 作用域与绑定

语义模型层面：`M("")` 处的 `GetSymbolInfo` 必须返回 `Class` 重载。两个源码声明都对应"源码签名相同、元数据签名不同"的符号——Roslyn 需要某种 `IsHiddenDiscriminatorParameter` 标志来标记元数据判别参数，并确保 IDE 的签名帮助、`GetTypeInfo`、`FindAllReferences` 全部按"源码签名"去重。主线在 2014 年就为 `nameof` 趟过同一片浑水（LDM-2014-10-15，原话）："It's also not clear how CodeLens and other tools would count nameof(M). Would it count it as a reference to all overloads of M? There are probably similar issues all over the IDE."——两个源码签名相同的成员，`FindAllReferences`、重命名、CodeLens 的引用计数全部需要重新定义。We 认为这是 A 最贵的部分，而它今天在 cheat 形态下**根本不存在**（cheat 的签名不同，IDE 天然区分）。

#### 4. 与既有特性的交互

- **重载决议与 `Overloads`**：cheat 能工作的地基是"VB 在适用性阶段考虑约束"。这个行为是**事实**（提案原文与我们的实验都支持），但它是否是**规范化的、稳定的行为**则存疑——`Suspect`：VB 规范对"何时在适用性阶段剔除违反约束的候选，何时在选定后校验"没有精确表述；C# 恰恰是在选定候选**之后**查约束，所以同样的两个重载在 C# 下调用 `M(1)` 会选错再报错，cheat **只在 VB 工作**。把一个未精确规范的行为升级成正式特性的地基，We 不愿意。
- **继承 / 覆写**：基类声明 `MustOverride Sub M(Of T As Class)(p As T)`，派生类要补 `Structure` 变体——判别参数导致元数据签名不同，`Overrides` 对不上；需要 `Overloads` 显式声明。主线 2014 年把 `Overrides` 改为"隐式 `Overloads`"（LDM-2014-02-17，原话）："Previously, VB libraries had to write both modifiers 'Overrides Overloads' to play nice with C# users. Now, 'Overrides' members are also implicitly Overloads."——这条规则在判别参数下如何与"签名碰撞特例"交互，完全没有设计。
- **hide-by-name / hide-by-sig**：主线确认"C# language is more 'hide-by-name' while VB supports both 'hide-by-name' and 'hide-by-sig'"（LDM-2014-10-08）。按约束重载本质上是给"同名同签名"再加一维区分，它挤压的是 hide-by-sig 的空间——一个尚无先例的新轴。
- **晚期绑定 / Option Strict Off**：`Dim obj As Object = 1 : M(obj)` 在宽松模式下走晚期绑定器，而晚期绑定器按"名 + 参数个数"解析——判别参数会让它命中错误重载或报错。严格路径按约束分派、宽松路径按参数个数分派，**两条路径行为分叉**。`OPEN QUESTIONS`：这是 A 无法回避的分叉，提案一字未提。
- **表达式树**：`M(x)` 出现在表达式树 lambda 里时，树捕获的是已选定的具体方法，不编码约束分派本身。`Probably`：无新问题。

#### 5. Breaking change 与兼容性

A 的新语法是**新增**（今天非法 ⇒ 明天合法），对既有代码零破坏。但有一个隐蔽的破坏源：**若编译器"识别"既有 cheat 并改变其行为**（例如把 `Optional ignored As Object = Nothing` 标记为判别参数、开始隐藏它），那么所有已经用了 cheat 的代码库——它们的元数据、反射结果、C# 调用方看到的签名——都会变。我们几乎从不为破坏性变更松口（"We will almost never make breaking changes"）。**结论：A 必须使用全新、显式的形态（新修饰符或新属性），绝不回溯识别既有 cheat。** 这意味着 A 的语法设计工作量比"放开声明"更大——它需要一个新记号来显式标注，而这又回到第 1 条追问的矛盾：为了"看不见参数"我们愿意加一个新记号吗？

#### 6. Option Strict / 编译选项分叉

见第 4 条晚期绑定分叉。两条路径对"已收窄区域"的成员可用性应一致；对宽松路径，`M(obj)` 应保持晚期绑定，而不是被按约束分派抢走。`OPEN QUESTIONS` 完整列在未决清单。

#### 7. IDE / IntelliSense

签名帮助必须隐藏判别参数；两个源码同签名成员的补全、重命名、CodeLens、引用高亮全部需要消歧。借用 LDM-2014-10-15 的教训："There are probably similar issues all over the IDE."——这是 A 最贵、最不性感的工程。We 对此没有低估，也没有高估：它是"做得出来"的，但纯粹是给"化妆价值"付的账单。

#### 8. 数据 / 普遍性

Anthony 的动机无数据、无 use case 量化。`Probably`：cheat 惯用法在泛型库（序列化、Result/Option 风格包装器）里真实存在，主线 [#228](https://github.com/dotnet/vblang/issues/228) 那句"An enum constraint by itself does not allow this to be solved via extension methods. Darn."证明主线在泛型约束边角也吃过瘪——但那是"约束不够用"，不是"约束分派不够用"。我们没有任何证据表明"假参数"是高频痛点。按主线原则"不为边缘场景加特性"，A 连一个量化的痛点都没有。`TODO`：若能重开，第一件事是量化 cheat 在真实代码库的占比。

#### 9. 更简替代

- 今天的 cheat（现状）：能力已经在了，只是丑。
- PROPOSAL D（运行时分派）：零语言成本，覆盖大部分场景。
- PROPOSAL E（拆分方法名）：丑但直白，不需要编译器知道任何新东西。
- 一个 analyzer + 文档：把"哪个更清晰"的判断交给工具而不是语法。
- ModVB 的可空引用类型（inactive #90）+ 可空性流分析：若落地，本特性的最强动机（"涉及可空性"）直接消失。

We 找不到一个场景是"上述替代全部不行、只有 A 能解决"的。

#### 10. 复杂度 / 成本 / 优先级

A 的账本：声明绑定特例 + 元数据判别参数发射 + 重载决议微调（小，因为地基已存在）+ Roslyn 符号模型（`IsHiddenDiscriminator`）+ IDE 消歧全家桶 + 跨语言表面积分析（C# 调用方）+ 晚期绑定分叉处理。这是一个完整语言特性，不是微调。而它服务的是一个**今天就能编译**的惯用法的化妆。价值 × 成本 × 风险：不成比例。优先级上，它排在 ModVB 的可空性、模式匹配、流分析之后很远。

#### 11. 运行时 / CLR 硬约束

**事实**：CLR 要求同一类型内方法签名唯一（名 + 参数表 + 泛型元数），约束不计入签名。因此任何形式的"按约束重载"在元数据层面**必须**有判别参数或等价区分物——"即使在元数据里仍需要它"是硬约束，A 只是承认它。判别参数本身（`Optional As Object = Nothing`）不违反 PEVerify，无存储规则问题。B 若要突破，是运行时签名规则变更，超出本会议范围。

#### 12. 值不值得做

We 逐项打分：**价值**（中低——化妆性改进，动机与可空性工作重叠，无数据）；**成本**（高——完整编译器 + IDE 特性）；**风险**（中——隐藏参数违反显式基因，跨语言表面积，晚期绑定分叉，IDE 消歧）。三项都不支撑 A 激活。We 明确说：**这不是一个"值得但太难"的提案，而是一个"现在不该做、且缺一个非隐藏参数形态"的提案。** 与 B（Fantastic idea, and too hard to do）不同，A 的问题不是可行性，是它本身的设计方向与 VB 基因有张力。

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：A 是典型违例——cheat 已经是"第一种做事方式"，A 只是把它再包一层，让"隐藏参数版"与"假参数版"并存于语言。两个版本行为相同、语法不同，这是扩展表面积的教科书案例。
- **避免隐蔽的控制流/语义变化（原则 #7）**：隐藏的判别参数是"同一个方法，签名取决于观察层"的隐蔽语义。`Return?` 被主线拒的理由是"细微字符改变语义"；A 是"看不见的参数改变元数据与调用规则"。同族。
- **读起来像英语、对新手友好（原则 #5）**：cheat 丑但诚实；A 干净但说谎。一个参数看得见时，新手能推理；看不见时，`M(1)` 为什么命中"值"重载变成魔法。
- **消除常见样板（原则 #9）**：唯一为 A 辩护的条款——假参数是样板。但主线对样板的态度是"Treat it as an optimization, not a feature"：先用文档 + analyzer 消，消不掉再谈语言。且这里的样板频率无数据。
- **维护简单 / 低仪式感的业务用户群**：`GetType(T).IsValueType` 分派（D）对业务作者已足够直白；引入隐藏参数反而是向相反方向走。
- **与主线关系（对照表 2.3）**：主线从未讨论"按约束重载"；重载决议仅在 [#170](https://github.com/dotnet/vblang/issues/170) 被触碰过一次，且是为回填 `in`（readonly reference）参数以"protect VB customers from breaks"——那是**兼容性工程**，不是新分派机制。本提案是纯粹的 **Anthony 独立延伸**，且与主线"默认跟随 C#"相抵触（C# 做不到，也不打算做；B 的运行时路径才可能让两语言共享）。与 ModVB 可空性工作（#90、流分析）动机重叠。

### RESOLUTION:

1. **不激活语言级语法糖（PROPOSAL A）——保持 inactive / Table。** 能力今天已由 cheat 提供，A 只买"看不到参数"这一项化妆价值，代价却是声明绑定特例、元数据判别参数、IDE 消歧全家桶、跨语言表面积与晚期绑定分叉。价值 × 成本 × 风险不成立。
2. **不追求 CLR 级按约束重载（PROPOSAL B）**："Fantastic idea, and too hard to do"（[#211](https://github.com/dotnet/vblang/issues/211) 会议原话）。运行时签名规则变更属 C# LDM 与运行时团队领地，与主线"CLR/库层变更直接委托 C# LDM"一致。若未来 C# / 运行时实现真·约束分派，VB 再跟随。
3. **把 cheat 文档化为正式惯用法（PROPOSAL C）。** 在语言文档中记录 `Optional ignored As Object = Nothing` 模式与其成立前提（VB 在适用性阶段考虑约束；只在 VB 工作；无约束 `T` 不可转发），并配 analyzer：检测假参数误传、建议 `GetType(T).IsValueType` 运行时分派或拆分方法名。
4. **近期推荐 PROPOSAL D**（方法内 `GetType(T).IsValueType` 分派）：零语言成本，覆盖大部分场景；需要编译期拆分时再重开本建议。
5. **可空性动机移交。** "涉及可空性时想要值/引用两种实现"的动机与 ModVB 可空引用类型（inactive #90）和可空性流分析重叠，应由那些工作承接，而非催生一个隐藏参数的 overload 机制。
6. **`We're proud not to do anything`**——本周对 A 的唯一诚实输出是：一份文档化惯用法 + 一个 analyzer + 一份激活信号清单。

### Implication:

- 撰写 cheat 惯用法的文档条目，写明前提、适用场景、替代方案。
- 评估一个 analyzer（可作为 ModVB 工具链的一部分）：标记假参数、建议 D / E。
- 若未来重开，先回答四个未决问题（语法形态、判别参数放置、无约束 `T`、跨语言表面积），再谈原型。
- 与可空性团队对表，确认动机不重复。
- 本建议在 `inactive/` 中保留，状态标注为 `LDM Reviewed: No Plans`（等待激活信号）。

### 激活所需信号

- **数据信号**：cheat 惯用法在真实代码库中的占比达到可辩护的阈值（`TODO`：设计一次测量）。
- **形态信号**：出现一个**不是隐藏参数**的更 VB 语法形态——例如把 PROPOSAL D 静态化（单一方法 + 按约束的条件体，编译器在 `T` 已知时静态裁掉另一条路径）。
- **外部信号**：C# / 运行时层面出现真·约束分派（届时 VB 跟随，而非单方发明）；或 ModVB 可空性工作明确需要编译期值/引用分派而 D 不够用。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：若语法形态问题被解决，判别参数附加到哪一方、放参数表哪个位置（尾随可选参数 / `ParamArray` 冲突怎么办）。
- `OPEN QUESTIONS`：无约束 `T` 转发调用（`Holder(Of T).Use()`）的语义：报错、引入无约束兜底、还是运行时降级。
- `OPEN QUESTIONS`：隐藏判别参数后 `AddressOf`、方法组转换、`nameof(M)`、CodeLens / 重命名 / FindAllReferences 的语义。
- `OPEN QUESTIONS`：晚期绑定（Option Strict Off）与按约束分派的行为分叉；`ByRef` 组合的规则。
- `OPEN QUESTIONS`：VB "在适用性阶段考虑约束"的确切边界——是否要把它规范化为正式语义（`Suspect`：这是 C#/VB 重载决议的已知差异，但 VB 侧表述不精确）。
- `TODO`：量化 cheat 惯用法的真实占比。
- `Follow-up`：与可空性团队确认动机不重叠；跟踪 C# / 运行时是否有约束分派动向。

### 状态

- **LDM 状态：`LDM Reviewed: No Plans`**（保持 inactive）。
- **三态判定：Table** — 非 Reject（惯用法真实、机制被理解、激活信号清晰）；非 Consider（无人领走、设计太薄、形态方向本身有基因张力）。

---

## 附录：特性评价

# 建议评价报告：proposal-structure-constraint-overloading.md

## 评价对象

- 建议：proposal-structure-constraint-overloading.md — `Structure` 约束重载（按 `Class`/`Structure` 约束重载泛型方法，源码层隐藏元数据判别参数）
- 来源：Anthony 原文第 18 章（实验性想法，`..\..\proposals\inactive/`）；原文明确标注"实验性 / 未定稿"，Summary 自陈"to-do"（移除假参数）
- 配方目标：让"按值/引用类型分派泛型方法"的写法更整洁——方法作者不写假参数、调用者看不见它；能力已由 cheat（`Optional ignored As Object = Nothing`）提供

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 是"有时希望不同实现"一句带过；示例演示的是 cheat——**今天就能编译运行的行为**，不演示本建议新增的任何能力；目标"隐藏参数"无语法、无原型、不可验收 | 已检查 | 4 个关键未决设计点（语法、判别参数、无约束 T、可空性）→ 效果封顶；无原型/运行证据；"期望结果"是化妆而非新能力 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。地基是 VB 现成行为（约束参与适用性判定）——这是 VB 基因；但"隐藏参数"是新魔法，与显式/可读基因（原则 #5、#7）相悖，且构成"第二种做事方式"（#3） | 已检查 | 隐藏参数与 VB 可读性张力未识别；与 cheat 形成双轨；动机与 ModVB 可空性工作（#90）重叠 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、Drawbacks/Alternatives 诚实；但无 BNF/spec、无兼容性分析、无 Option Strict 分叉、无跨语言（C#/反射）分析；未决问题列出但"定夺方式"缺失 | 已检查 | 红旗命中：状态行占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）、无兼容性分析；"未决问题 ≥4"→ 效果证据封顶的典型样本 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。光（低仪式）略有、水（盘活现成行为）略有；但暗风险突出且未识别：跨语言表面积（C# 看到判别参数）、晚期绑定分叉、IDE 消歧、AddressOf/委托签名断裂；风=与可空性工作方向断裂 | 已检查（预测待定） | 文档对暗风险零权衡、无对冲设计；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源=Anthony 第 18 章实验性（标注准确）；继承 VB 现成"约束参与适用性"行为与 cheat 惯用法，**未点明**；正确未借鉴 C#（C# 在选定后查约束，cheat 不工作）；对主线重载决议工作（[#170](https://github.com/dotnet/vblang/issues/170)）零引用 | 已检查 | 材料来源层级（VB 现成行为 vs Anthony 原创）未标；"VB 在适用性阶段考虑约束"这一地基行为未被当作待规范化的资产对待 |

## 设计原则对照

- **与 VB 基因：偏离**。唯一的辩护点是"消除样板"（原则 #9），但样板频率无数据、且主线主张先文档化/analyzer（"Treat it as an optimization, not a feature"）；"避免隐蔽语义变化"（#7）与"不引入第二种做事方式"（#3）均扣分——隐藏参数正是隐蔽语义与双轨做法的结合体。
- **与主线关系：Anthony 独立延伸**。主线从未讨论按约束重载；重载决议仅在 [#170](https://github.com/dotnet/vblang/issues/170) 作为**兼容性回填**（`in` 参数）被触碰，与"新分派机制"无关；与主线"默认跟随 C#/CLR"相抵触（B 才可能让两语言共享，但那是运行时领地）。动机与 ModVB 可空性工作重叠。
- **破坏性变更：语言糖本身无**（新增语法）；**有条件有**——若编译器"识别"既有 cheat 并改变其行为（隐藏参数、改决议），将破坏所有已用 cheat 的代码库及其 C# 调用方。文档未分析此点，须以"新形态显式标注、绝不回溯识别既有 cheat"对冲。

## 总评

- **达成程度：未达成**——概念成立（cheat 真实存在）、设计未成（无语法、无原型、无兼容性分析）、动机被可空性工作接管。
- **LDM 三态建议：Table（保持 inactive，`LDM Reviewed: No Plans`）**——非 Reject（惯用法真实、机制被理解、激活信号可写）；非 Consider（无人领走、设计太薄、形态方向与 VB 基因有张力）。
- **主要问题**：① 无约束 `T` 转发调用是天生空洞，提案未触及；② 隐藏判别参数违反 VB 显式基因，且元数据/反射/C# 三个观察层看到不同签名；③ 缺乏普遍性数据（无 use case 量化）；④ 最强动机（可空性）与 ModVB 可空性工作重叠。

## 返工建议

- **补充章节**：明确的语法形态（BNF 或 spec 修改描述——现状完全没有）；Compatibility / breaking-change 分析（含 C# 调用方、反射、`AddressOf`/委托、`nameof`、Option Strict 分叉）；与 [#170](https://github.com/dotnet/vblang/issues/170) 重载决议工作的关系。
- **补充证据**：cheat 惯用法在真实代码库的占比数据（`TODO`）；若重开，最小原型（声明绑定特例 + 判别参数发射 + 语义模型验证）。
- **未决问题处理**：定义判别参数放置规则（末尾、禁止与 `ParamArray` 共存）；明确无约束 `T` 语义（建议：报错，不引入兜底）；明确"不回溯识别既有 cheat"；把"VB 在适用性阶段考虑约束"的行为写入规范（这是整个地基）。
- **设计探索**：探索"单一方法 + 按约束条件体"的编译期分派形态（把 PROPOSAL D 静态化）——若真要编译期分派，这比隐藏参数更 VB；若该形态成立，本建议应据此重写而非原地修补。

---

## 附录：C# 生态与互操作考量

> 主题对齐：本提案核心是「按泛型约束（`Class`/`Structure`）重载 + 源码层隐藏元数据判别参数」。C#/CLR 生态对应轴有三条：① 泛型约束体系的持续演化（`where T : class/struct/unmanaged`、C# 13 的 `allows ref struct` 反约束）；② 基于约束的重载决议（C# 7.3 improved overload candidates 之后，约束在候选收集阶段参与筛选）；③ 重载消歧的现代机制（C# 13 `[OverloadResolutionPriority]` 属性，而非隐藏参数）。本附录基于 `..\..\..\csharplang-index.md`，所有引文均在 `..\..\..\csharplang` 逐字核实。

### 相关 C# 现实方向

#### R1 约束体系持续演化，但约束从不进入方法签名

C# 的泛型约束在 7.3 之后明显扩容，方向是「把类型能力分派放进约束」，但从未触碰「约束=签名一部分」这条 CLR 底线：

- **C# 7.3 引入 `unmanaged` 约束**，把「非托管 / 可作指针」的类型集合做成泛型可复用。动机原文（索引第四节可直接用）：**"The primary motivation is to make it easier to author low level interop code in C#."** → `proposals\csharp-7.3\blittable.md`
- 关键互操作细节：**该约束不被 CLR 强制、只由语言强制，靠 mod-req 阻止其他语言误用**。原文：**"The `unmanaged` constraint is not enforced by CLR, only by the language. To prevent mis-use by other languages, methods which have this constraint will be protected by a mod-req."** → `proposals\csharp-7.3\blittable.md`。这意味着 VB/VBScript.NET 消费 C# 的 `where T : unmanaged` 方法时，编译器必须**认识该约束及其 mod-req** 才能正确校验——这是「识别新元数据」的直接实例。
- **C# 13 引入反约束 `allows ref struct`**，方向相反（不是收窄类型集合，而是放宽泛型对 `ref struct` 的默认排斥）：**"This is effectively an anti-constraint as it removes the implicit constraint that `ref struct` cannot satisfy a generic parameter."** → `proposals\csharp-13.0\ref-struct-interfaces.md`。元数据表示是泛型参数标志：原文 "…will be encoded in metadata… by using the `CorGenericParamAttr.gpAllowByRefLike(0x0020)` or `System.Reflection.GenericParameterAttributes.AllowByRefLike(0x0020)` flag value." → `proposals\csharp-13.0\ref-struct-interfaces.md`。C# LDM 在 2024 年明确把 VB 列为反约束影响的检查对象：**"If we were to add anti-constraints to existing library types we would need to check for problems in VB and F# too."** → `meetings\2024\LDM-2024-01-22.md`

结论：C# 生态把「约束」当作**类型能力分派**的载体持续投资（unmanaged、allows ref struct），却始终没有把约束纳入方法签名——与本提案第 11 条追问的 CLR 硬约束一致。**C# 从未试图让「仅约束不同」的两个方法在元数据里共存。**

#### R2 决议层：C# 7.3 起「按约束筛候选」已是正式行为

本提案正文第 118 行断言 C#「在选定候选之后查约束」，据此得出「cheat 只在 VB 工作」。这个断言是 **C# 7.3 之前的旧行为**，需要更新：

- C# 7.3 的 improved overload candidates 把「约束不满足的泛型候选」在候选收集阶段剔除。原文：**"When a method group contains some generic methods whose type arguments do not satisfy their constraints, these members are removed from the candidate set."** → `proposals\csharp-7.3\improved-overload-candidates.md`。版本史确认：原文 **"Improved overload candidates: Some overload resolution candidates can be ruled out early, thus reducing ambiguities."** → `Language-Version-History.md`（C# 7.3 段）
- LDM 对这项能力的概括甚至直接用上「重载」一词：原文 **"When gathering candidates, if one of them has inferred type arguments that do not satisfy constraints, we'll discard the candidate. This means you can overload on constraints!"** → `meetings\2018\LDM-2018-02-26.md`

含义：**在决议（resolution）层，C# 与 VB 的「约束参与适用性判定」已经趋同**——两语言都会在候选收集阶段剔除约束不满足的候选。本提案正文把「VB 在适用性阶段考虑约束」当作 VB 独特资产（PROPOSAL C 成立的地基之一）需要**降级为程度差异**：真正的差异在声明层（见 R3）。现代 C# 下，正文那个 cheat（两方法形参表不同、仅约束不同）在 C# 里同样能按约束筛出正确候选——「cheat 只在 VB 工作」的表述不再准确。

#### R3 声明层：C# LDM 对同一缺口的答案从不是「隐藏参数」

C# 同样受 CLR 签名规则限制；声明「仅约束不同」的同签名方法在 Roslyn 里以 CS0111 报错（诊断编号属编译器实现层，csharplang 仓库无正文，此点标注为编译器事实而非仓库引用）。C# LDM 早在 2013 年就撞上与本提案完全相同的缺口，即按值/引用（乃至可空性）分派泛型方法：

- `TryConvert<TSource, TResult>` 需要按「`TResult` 是非空值类型还是可空类型」走不同逻辑。C# LDM 原话：**"The problem is that you cannot actually implement `TryConvert` efficiently: It needs different logic depending on whether `TResult` is a non-nullable value type or a nullable type. But you cannot overload a method on constraints alone, so you need two methods with different names (or in different classes)."** → `meetings\2013\LDM-2013-10-07.md`

这句话几乎逐字对应本提案 PROPOSAL E（拆分方法名）与 PROPOSAL C（文档化惯用法）——**C# 面对同一问题的答案是改名/改类，从未考虑过「编译器自动补一个判别参数」**。此外 C# LDM 在 COM interop 语境也承认「哑元参数」是真实现象（原文 "the somewhat common scenario of passing dummies to ref or out parameters that you don't need (common in COM interop scenarios)" → `meetings\2014\LDM-2014-09-03.md`），但处理方式是接受其为调用约定，而不是发明语法隐藏它。

#### R4 重载消歧的现代机制是「元数据属性」，不是隐藏参数

C# 13 的 `[OverloadResolutionPriority]` 是 C# 对「重载太多 / 歧义」的当代标准答案：**一个源码与反射都可见的属性，不改变签名、不隐藏任何东西**。原文：**"We introduce a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type as a means of steering API consumers to use specific APIs, even if those APIs would normally be considered ambiguous or otherwise not be chosen by C#'s overload resolution rules."** → `proposals\csharp-13.0\overload-resolution-priority.md`

这给本提案的 PROPOSAL A 提供了一个它没有考虑过的参照系：C# 生态解决「重载该选谁」的方式是**显式的、元数据可审计的属性**，其哲学与本提案 A 的「隐藏判别参数」（正文第 69、126 行争论的隐蔽语义）正好相反。

### 现实 vs 提案

| 维度 | 判定 | 理由 |
|---|---|---|
| CLR 签名规则（约束不进签名） | **兼容** | C# 与 VB 同受此限；C# 也从未试图突破（R1、R3）。本提案 PROPOSAL B 属运行时领地，C# 侧同样无迹象。 |
| 决议层「约束参与适用性」 | **需修正（正文第 118 行）** | 正文称 C#「选定候选之后才查约束」是 7.3 前行为；C# 7.3 起已改为候选收集阶段剔除（R2）。「cheat 只在 VB 工作」不准确——真正不可复制的只是声明层（C# 不能声明同签名双方法，R3）。 |
| 「隐藏判别参数」的语法形态 | **冲突** | C# 生态的解法是显式属性调优（R4），其基因与「你看不见的参数」相悖；C# 2013 LDM 对同一缺口的答案即改名/改类（R3）。正文第 69、126 行的基因张力在 C# 生态得到外部印证。 |
| 新约束元数据的识别 | **需桥接** | `unmanaged` 的 mod-req、`allows ref struct` 的 `gpAllowByRefLike`、`[OverloadResolutionPriority]` 属性——VB/VBScript.NET 编译器不识别则无法正确消费/复现 C# 泛型 API（R1、R4）。属决策文件 M8「必须桥接」点。 |

### 对 VBScript.NET 的适应建议

1. **识别新元数据（最高优先）**：`.vbx` 的 Roslyn 分支需要支持 ① `unmanaged` 约束及其 mod-req 保护（否则无法对 C# `where T : unmanaged` 方法做编译期校验）；② `allows ref struct` 反约束的 `gpAllowByRefLike(0x0020)` 泛型参数标志（否则无法消费 C# 13+ 的泛型 ref struct 接口方法）；③ `[OverloadResolutionPriority]` 属性（否则 `.vbx` 调用带该属性的 BCL 方法时重载选择与 C# 不一致）。这三点与「约束分派」同域，是本附录认为比 PROPOSAL A 更值得投入的互操作工作。
2. **重载决议对齐 C# 7.3 语义**：既然 C# 已在候选收集阶段剔除约束违规候选，`.vbx` 应把「约束参与适用性判定」规范化为正式语义（正文 OPEN QUESTIONS 已列此待办），并明确与 C# 行为对齐——避免同一调用点在 `.vbx` 与 C# 下选到不同重载。
3. **若仍要「约束分派的整洁写法」：优先借鉴属性形态而非隐藏参数**。C# 13 `[OverloadResolutionPriority]` 证明「元数据属性调重载」可行且被生态接受；若要探索，应是「源码同名 + 显式属性标注」（属性在源码、反射、C# 消费方三方可见），而不是 PROPOSAL A 的「伪装成可选实参的隐藏参数」。但注意：CS0111 式签名碰撞依然存在，属性形态只解决「消歧」不解决「声明共存」——这再次说明最现实路径仍是正文 RESOLUTION 第 3、4 条（文档化 cheat + analyzer + 运行时分派）。
4. **默认安全 / 按需动态**：约束分派是纯编译期/类型系统工作，与晚期绑定域无交互；`.vbx` 的「默认安全、按需动态」路线不受本提案影响，也无需为它调整。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION 第 1 条（不激活 A）不受动摇，反而被 C# 生态现状加强**：C# 对同一缺口（`TryConvert`，R3）与「重载消歧」（R4）的投入方向分别是「改名/改类」与「显式属性」，没有任何一方为「隐藏判别参数」背书。A 的形态方向与整个 C#/CLR 生态的解法风格相左。
- **RESOLUTION 第 3 条的前提需要修订措辞**：正文「cheat 只在 VB 工作（C# 在选定后查约束）」应改为「C# 自 7.3 起同样在候选收集阶段考虑约束，但 C# 不能声明同签名双方法（CS0111），故 cheat 作为惯用法仍是 VB 独有——差异在声明层，不在决议层」。
- **激活信号的「外部信号」细化**：C# 13 的 `[OverloadResolutionPriority]` 不构成激活信号（它不改变签名规则、不启用约束重载）；真正的激活信号仍是「C#/运行时允许按约束声明重载」（未见任何迹象）或 ModVB 可空性工作对编译期值/引用分派的硬需求。
- **三态判定 Table 不变**：C# 生态的现状（不突破签名、不隐藏参数、用属性调优）与正文 RESOLUTION 的 PROPOSAL C + E 完全同向，进一步支持「保持 inactive、等形态信号」。

### 引用纪律与未决

已逐字核实并标注来源的 C# 原文见 R1–R4 内嵌引用，来源包括 `proposals\csharp-7.3\blittable.md`、`proposals\csharp-7.3\improved-overload-candidates.md`、`proposals\csharp-13.0\overload-resolution-priority.md`、`proposals\csharp-13.0\ref-struct-interfaces.md`、`meetings\2013\LDM-2013-10-07.md`、`meetings\2014\LDM-2014-09-03.md`、`meetings\2018\LDM-2018-02-26.md`、`meetings\2024\LDM-2024-01-22.md`、`Language-Version-History.md`。

- `OPEN QUESTIONS`：C# 7.3 improved overload candidates 的精确作用时机（候选收集 vs 适用性判定）在规范正文中的表述未深挖——csharpstandard 正文已迁出本仓库（`spec\` 目录仅链接），「VB 在适用性阶段考虑约束」与 C# 7.3 行为的精确对齐仍需规范层核实。
- `OPEN QUESTIONS`：CS0111（声明层签名碰撞）的诊断语义属 Roslyn 编译器实现，本仓库无正文；本附录「声明层差异」的结论由 LDM-2013-10-07 与 improved-overload-candidates 推得，未引用编译器源码。
- `Suspect`：索引 T4 将 LDM-2014-09-03 概括为「out/ref 哑元参数在 COM interop 场景」——本附录核实该会议原文是 declaration-expression 作用域讨论中的一句旁注，并非专门针对哑元参数的设计决定；引用时已按原文措辞限定范围。
