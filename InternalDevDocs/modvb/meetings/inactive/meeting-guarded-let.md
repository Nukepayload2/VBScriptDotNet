# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天处理一份 **inactive** 建议——`proposal-guarded-let.md`（Anthony 原文第 18.2 节 "Guarded `Let`"）。按提案生命周期惯例，inactive 意味着"值得记下但不打算现在做"；我们今天要回答的不是"怎么落地"，而是**它值不值得被激活、保持搁置，还是归档 Reject**。会议开始前我们明确了三条已经定案的依赖线，它们会贯穿全场：

- `local-declarations` 会议已把 `Let` 声明关键字判为 **Table**，并明确列出一条复活信号——"与 top-level-code、guarded-let、query-enhancements 联合做一次'声明方言'整体评审"。本建议正是那四个依赖方之一。
- `typeof-flow-analysis` 与 `nullability-flow-analysis` 两场会议已定案：守卫惯用法（`If ... Then Return` / `If x Is Nothing Then Return`）之后继续区收窄，两者与 definite assignment 共用**同一流分析引擎**。
- `Null` 字面量已被 Table（字面量 Reject / 可空推断 Consider）；可空性会议明确"守卫写 `IsNot Nothing`，不用 `IsNot Null`"。

## Agenda

* [Proposal: Guarded `Let`（守卫式声明）](#proposal-guarded-let)

## Proposal: Guarded `Let`

_Related: [vblang #190 – `Try` assignment](https://github.com/dotnet/vblang/issues/190)（主线唯一一次正面讨论守卫式赋值）；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)（2018-12-19 会议，声明模式）；[vblang #167 – `Return?`](https://github.com/dotnet/vblang/issues/167)（隐蔽语义变化被拒的对照）；[vblang #48 / #104 – 多 `For` 变量](https://github.com/dotnet/vblang/issues/48)；ModVB：`proposal-local-declarations`（`Let` 关键字 Table）、`proposal-nullability-flow-analysis`、`proposal-typeof-flow-analysis`、`proposal-shapeof-pattern-matching`、`proposal-set-statement`、`proposal-out-arguments`、`proposal-null-literal`、`proposal-top-level-code`、`inactive/proposal-case-classes`_

### 场景与缺口

We opened with the scenario the proposal names, and we do not dispute that the pain is real. Anthony（18.2）从一位 F# 开发者"函数式表达式缺少提前返回"的抱怨出发，把一段典型的 VS 互操作服务获取代码改写成了 ModVB。原文摘录：

```vb
Function GetIVsTextView() As IVsTextView

    Let textManager = GetService(Of SVsTextManager)
    If TypeOf textManager IsNot IVsTextManager Then Return Null

    Let rdt = GetService(Of SVsRunningDocumentTable)
    If TypeOf rdt IsNot IVsRunningDocumentTable Then Return Null

    Let hresult = FindAndLockDocument(rdt, Out docData As IntPtr)
    If Not ErrorHandler.Succeed(hresult) Then Return Null
    If docData = IntPtr.Zero Then Return Null

    Let textBuffer = Marshal.GetObjectForIUnknown(docData)
    If textBuffer Is Null Then Return Null

    hresult = GetActiveView(textManager, 1, textBuffer, Out textView)
    If Not ErrorHandler.Succeeded(hresult) Then Return Null

    Return textView

End Function
```

他的自述是："This was oppressively monotonous and I started thinking about simplifying with tuples and nullability or `Out` variables and anything I could think of to break the boring pattern. This made me realize that repeating the variable name to check for null was annoying. I knew I didn't like the `if let`/`guard let` syntax in other languages (e.g. Swift) because it doesn't read naturally to me. Eventually I realized I was actually cool with the concept, I just didn't like that syntax and came up with this to eliminate a lot of duplication"（18.2 原文）。**我们认可这个缺口的形状**："取服务 → 判空 → 提前返回"的守卫链是真实的样板，而且"重复写变量名做空检查"的诊断是准确的。作者对 Swift 语法"读起来不自然"的直觉，我们同样有共鸣——这与我们一贯"读起来像英语"的审美一致。

But——这是本纪要第一条结构观察——**动机示例的守卫条件与本特性声称的守卫条件并不匹配。** 摘要说"变量初始化值**是否为空**决定执行指定的控制流语句"；但动机示例里**没有一条**是空检查。`If TypeOf textManager IsNot IVsTextManager Then Return Null` 是**类型**测试，不是空测试——`GetService` 返回 `Object`，可能为空也可能为错误类型。把示例改写成守卫语法，唯一能覆盖它的是 `Let v As T = TryCast e Then/Else` 这个**块形式**，而不是头牌的 `Let v = e Else Return` 单语句形式。换句话说：**头牌语法服务不了动机示例，动机示例服务的语法是块形式，而块形式恰恰是作者自己最拿不准的部分。** 我们后面会反复回到这一点。

还有一个更刺眼的叠影：动机示例里那句 `If TypeOf textManager IsNot IVsTextManager Then Return Null`，**正是 `typeof-flow-analysis` 会议用来论证"守卫付费"的那句原话**（Anthony 第 9 章）。也就是说，本建议想消灭的样板，一半已经被另一份建议消灭了——`TypeOf` 收窄让守卫之后的 `textManager` 直接可按 `IVsTextManager` 使用。本建议的增量只剩"把检查这一行本身折进声明里、省掉一次变量名重复"。这决定了对它价值的全部评估。

### 候选方案

**PROPOSAL A — 完整落地 Anthony 18.2 全部五种形态。** 单语句 `Else <控制流语句>`、单语句 `Then <控制流语句>`、块形式 `Then ... End Let`、`Then`/`Else` 组合块、以及条件 `Set`（`Let list As List(Of Object)` + `Set list = ... Else ... End Set`）。

**PROPOSAL B — 只做单语句守卫，砍掉块形式与条件 `Set`。** `Let v = e Else <CFS>` / `Let v = e Then <CFS>` 两种形态，变量作用域为"从声明到当前块结束"。实现面最小，也是五种形态里唯一与动机示例能沾上边（经 `TryCast` + `As T` 改写）的。

**PROPOSAL C — 零新语法：既有守卫惯用法 + 共享流分析引擎。** 依赖已经定案的 `TypeOf` 收窄与可空性流分析，守卫之后的继续区自然聪明；本建议只省"一行检查 + 一次变量名重复"，由重构与 analyzer 承担，不碰语言。这是"更简替代"的完整版。

**PROPOSAL D — Swift 式 `if let` / `guard let`。** 作者明确不喜欢这种读法；主线 2017-10-18 在 `Try` 赋值（#190）上已经提过它——"Swift accomplishes this design with `if let` for positive cases and `guard let` for negative cases. Might that be a better design to explore?"——当时没有采纳。We 把它列为被否决的备选，仅留档：它既不符合 VB 读法，也没有主线动力。

**PROPOSAL E — 库层/模式方案。** `TryGetValueOrCreate` 之类的集合辅助方法（作者自指）、Result Pattern 结果类型 + `Case` 类（作者兴趣，见 18.2 末段）、以及声明模式 `Case x As T`。把"守卫"从声明语法里拿掉，放进模式或库。

### 权衡：Q&A

We walked the design through our questioning list. The points below are the ones that actually bit; not in priority order.

**Q1：`Let` 关键字是地基，而地基是 Table 的。** 本建议的每个形态都以 `Let` 为声明关键字。`local-declarations` 会议已把 `Let` 判为 Table：2014-02-17 曾明确问过——`Q. Does the keyword "Let" cause ambiguities if we're using this inside query expressions? Note that "Let" is bad for query expressions.`——并以 `RESOLUTION: None of this feels naturally "VB"ish.` 收场；该会议的复活信号之一是"与 top-level-code、guarded-let、query-enhancements 联合做一次'声明方言'整体评审"。**本建议就是那个评审的四个成员之一，不能在自己参与评审之前抢跑。** 若 `Let` 最终被否决，本建议全文作废；若被采纳，本建议也只是把 `Let` 的载荷又加重了一档——`Let` 现在要同时撑起：查询子句、语句声明、以及"声明 + 守卫分支"。2014 年那句"Let is bad for query expressions"与其说是对当时场景的抱怨，不如说是对 `Let` 这个字长期可复用性的警告；本建议是这条警告的最新证据。

**Q2：守卫条件与动机示例不匹配（承接开场）。** 头牌形态 `Let v = e Else <CFS>` 只在"v 为空"时触发。但动机示例的每个守卫都是 `TypeOf ... IsNot` 类型测试。`GetService` 返回的错误类型对象非空但类型不对，`Else Return` 不会触发。唯一能表达类型守卫的是 `Let v As T = TryCast e Then/Else` 块形式。**这说明建议的作者自己在"消灭样板"时看到的样板和设计出的语法是两种不同的东西。** 修正示例需要把每个 `TypeOf` 测试改写为 `TryCast` + `As T`，而改写后的形态恰恰是他自疑的块形式。We `Suspect` 这份建议的"看似收敛"是假象：它把两种守卫（空守卫与类型守卫）缝进了一个语法面，但只详细设计了其中一种。

**Q3：作用域——作者的自疑是整个设计的七寸。** 原文原样摘录：

> `' Not sure about this:`
> `Let list = lists.TryGetValue("key") Else`
> `    list = New List(Of Object)`
> `    lists.Add("key", list)`
> `End Let`
> `list.Add(value)`
> `' But list shouldn't be in scope here.`
> `' Should it have been End Else?`

作者问"是不是该用 `End Else`"。我们的分析：**这不是终结符问题，是作用域与赋值合并问题。** "get-or-create"模式要求 `list.Add(value)` 在构造之后访问 `list`，且此时 `list` 必须非空——两分支（Else 分支赋了 `New List`；若无守卫分支则... 本形态没有 Then）都走到构造后时，`list` 是 definite-assigned 的。有两个自洽设计：(i) **变量只在构造块内作用域**——`list.Add(value)` 编译错误，get-or-create 必须移进块内或交给库函数；(ii) **变量在构造后继续作用域**——靠 definite-assignment 分支合并证明构造后非空，`list.Add(value)` 合法。设计 (ii) 正是 `If ... Then Return` 守卫惯用法在流分析引擎里已经建立的"守卫退出 ⇒ 继续区收窄"事实的**声明化版本**。无论选哪个，都需要明确规则，而建议把"是 `End Else` 吗"这种问题留给读者，等于承认语义未定型。`End Else` 本身我们直接否决——VB 不按"哪个关键字收尾"表达作用域，`End Let` 收尾即可，作用域规则是语义问题不是终结符问题。

**Q4：块形式与声明模式的价值重叠。** 2018-12-19 模式匹配会议把第一阶段定义为 `Declaration pattern - type match with assignment equivalent and When clause`。`Let v As T = TryCast e Then ... Else ... End Let` 做的就是这件事：类型测试 + 把结果绑进作用域受限的、已收窄的变量。`ShapeOf` 的 `Case x As T`（ModVB）已经承诺"声明式模式变量"。**如果声明模式落地，块形式是它的 `Let` 语法重述；如果声明模式不落地，块形式才值得独立考虑。** 而声明模式是主线"最期待"的领域——2018-12-19 原话："We all want to do pattern matching, it's the thing we are most excited about after C# interop issues." 我们不能为了一份 inactive 建议去抢主线最期待特性的地基。这是块形式判 Reject/Table 的核心论证。

**Q5：`Then Return v` 示例编译不了。** 原文：`Let v = dictionary.TryGetValue("key") Then Return v`。`TryGetValue` 返回 `Boolean`——`v` 被初始化为 `False` 或 `True`，`Then Return v` 返回的是 Boolean，不是字典里的值。**示例与它声称要表达的语义（"查到了就返回这个值"）不一致。** 要表达那个语义，必须捕获 `ByRef` 输出参数——这又依赖 `out-arguments` 建议的内联 `Out` 声明形态。也就是说，这个最有吸引力的单语句形态，实际是"声明守卫 + out 参数捕获"两个特性的合体，而建议只写了其中一半。We `Suspect` 这是把 `Dim value As String` + `If dict.TryGetValue(k, value) Then Return value` 的样板在头脑里压缩时漏掉了 out 捕获环节。

**Q6：`Null` 依赖是已被下架的拼写。** 动机示例写 `Return Null`、`Is Null`。`Null` 字面量已被 Table（`proposal-null-literal`）；可空性流分析会议已定"守卫写 `IsNot Nothing`"。本建议的示例若不改为 `Nothing`，连同 `Null` 一起作废。这不是致命伤——只是拼写依赖一个被搁置的字面量，但说明建议没有与邻近工作对表。

**Q7：条件 `Set` 形态是另一份建议的地盘。** `Let list As List(Of Object)` + `Set list = lists.TryGetValue("key") Else ... End Set` 里，守卫挂在 `Set` 上，不是 `Let` 上；`End Set` 今天只作属性访问器的终结符。`set-statement` 会议已裁定：语句级 `Set` 与属性访问器 `Set`、#197 的参数推断方向相撞，"继承 VB6 的皮、换 VB6 的骨"。条件 `Set` 把 `Set` 从"赋值前缀"进一步扩成"带守卫的赋值块"，语法面再翻一档。而"get-or-create"的真身是集合自带的语义——作者自己的注释点破了："This logic should maybe be in a `TryGetValueOrCreate`?"——**库函数能做得更干净。** 我们倾向把条件 `Set` 形态 Reject，理由与 set-statement 对复合前缀的裁定同源：它在为一个库函数就能解决的模式造语言语法。

**Q8：流分析吃掉了大半价值。** 单语句守卫 `Let v = e Else Return` 的收益分成两半：省掉"检查那一行"（语法增量），以及"继续区 v 非空"（流事实）。后一半**必须**由流分析引擎提供——否则 `Let v = e Else Return` 之后 v 仍可能是 Nothing，用户还得再查一次。而流分析引擎已经为 `If v Is Nothing Then Return` 这个既有惯用法提供同一个事实（nullability 会议已定案）。所以单语句守卫是**挂在既有流引擎上的一层糖**：它不引入新能力，只压缩书写。这把我们引向一个诚实的问题：省一行、少写一次变量名，值不值一套新文法 + 作用域规则 + IDE 工作？We are not saying the pain is fake; we are saying the marginal value over PROPOSAL C is small.

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Let v = e Else <语句>` 的 `Else` 在语句位置没有既有含义——`Else` 今天只出现在 `If`/`Select`/`Case` 的块内和 `If` 单行形式里，一个**声明语句**后面跟 `Else` 是全新文法。`Then` 同理：单语句守卫 `Let v = e Then Return v` 让 `Then` 从 `If` 的专属词变成了声明语句的续接词。上下文可区分（`Let` 开头即声明，`If` 开头即条件），但 parser 要处理"同一批词在相邻语句族里扮演不同角色"——这正是设计原则警惕的"细微字符改变语义"的邻域。块形式引入新终结符 `End Let`（VB 无先例；`Let` 从未有过块），条件 `Set` 引入 `End Set` 第二义（与属性访问器终结符撞名）。文法上每条都可定义，但**没有一条是免费的**。此外设计正文说"声明并初始化一个变量（或一组变量）"——多变量守卫形态 `Let a, b = ... Else Return` 的逗号语义没有给出；而 `local-declarations` 已把无括号解构的逗号判为与 `Dim` 逗号列表冲突（2017-12-06 明示先例：`Imports`, `Dim`, `Next`, `From`, `Let`, `Case` 都是逗号列表），多变量守卫若走无括号路径，撞的是同一堵墙。

#### 2. 角案例与边界语义

- **值类型。** `Let count As Integer = GetCount() Else Return`——`Integer` 永不为 Nothing（除非 `Nullable(Of Integer)`），守卫对非可空值类型**恒假**。是编译错误（守卫无意义）还是恒不触发（死代码）？必须规定：守卫只对引用类型与 `Nullable(Of T)` 有意义。建议未提。
- **`TryCast` 语义。** `Let v As T = TryCast e Then ... Else ... End Let` 里，Then 分支 v 非空、Else 分支 v 为 Nothing——两分支的空状态不同，这正是流分析引擎要建模的分支敏感事实。若引擎只做"守卫后收窄"不做"分支敏感声明"，块形式就无从谈起。
- **`Continue`/`Exit` 的语境约束。** `Else Continue For` 只在 `For` 循环内合法，`Else Exit Sub` 只在 Sub 内合法。单语句守卫的控制流语句清单（`Return`/`Throw`/`Exit`/`Continue`）需要一个"在什么语境合法"的规则表；建议只给了清单没给语境。
- **守卫叠加。** 动机示例里 `If docData = IntPtr.Zero Then Return Null` 是值比较守卫——`IntPtr.Zero` 不是空，是零值。这暴露第三类守卫（**值/成功标志守卫**）：`ErrorHandler.Succeed(hresult)`、`docData = IntPtr.Zero`。这些根本不能用"空"表达。**动机示例的守卫其实是三种：类型、空、布尔成功标志。** 建议的语法只覆盖其中一种。这是"消灭样板"与"语法能消灭什么"之间最大的裂缝。

#### 3. 作用域与绑定

单语句守卫的变量作用域自然为"从声明到当前块结束"（与 `Dim` 一致），这条没有争议。块形式 Then 分支的变量"在 `End Let` 后失去作用域"（设计明说）——这是模式作用域，好设计。**真正的裂缝在 Else 块**（Q3）：作者想让 `list` 在构造后可用，又自疑"不该可用"。我们的定夺方向：若复活，采用"变量在构造块内作用域；构造后由两分支赋值合并（definite-assignment merge）决定非空"——这与流分析引擎的分支合并是同一机制。语义模型上，`Let v = e Else Return` 中的 `v` 是普通 `LocalSymbol`；`Then Return v` 里的 `v` 绑定同一符号。没有新符号类别。

#### 4. 与既有特性的交互

- **流分析引擎（最重要）。** 单语句守卫的收益以"声明守卫 ⇒ 继续区 NotNull"这一流事实为前置条件。该事实与 `If v Is Nothing Then Return` 守卫**完全相同**。不能另建一套——nullability 会议已定"同一引擎"；本建议应当把这条登记为引擎的一个新事实源，而不是新机制。
- **`TypeOf` 收窄。** 动机示例的守卫本来就是 `typeof-flow-analysis` 的领域。若 `TypeOf` 收窄落地，示例的继续区已经聪明；本建议只是把检查行折进声明。两者在同一段代码上工作，必须共享事实提取规则。
- **声明模式（ShapeOf）。** Q4 已述：块形式是 `Case x As T` 的重述。`Matches` 是主线提议的关键字（2018-12-19："`Matches` is the proposed keyword."）；ModVB 用 `ShapeOf`/`Matches`。块形式若独立落地，会与模式文法抢"类型测试 + 绑定"这一概念。
- **out-arguments。** Q5 已述：`Then Return v` 的真身需要内联 `Out` 捕获。
- **`?.` 与 `If()`。** `dictionary.TryGetValue` 类模式已有 `?.` 与 `If(x Is Nothing, fallback)` 覆盖一部分"可空回退"；守卫链条（提前返回）与回退表达式是两种不同的控制流形状，`?.` 不替代守卫，但说明"空处理"已是多机制领域——再加一层守卫语法，机制数量不降反升。
- **`Let` 查询子句。** Q1 已述。
- **`Set` 语句。** 条件 `Set` 是 set-statement 的延伸，Q7 已述。

#### 5. Breaking change 与兼容性

全部形态都是新语法；语句位置 `Let` 今天报错，本建议把它变成程序——"错误→程序"不构成破坏。`End Let` 今天无含义；`End Set` 今天只在属性访问器内合法，独立 `End Set` 是错误→程序。**兼容面为零**，这一条是清白的。但注意反向推理不成立：`Let` 作为 contextual keyword 的解析器表面（查询子句 `Let` 必须原样保留）是真实回归面，`local-declarations` 与 top-level-code 两场会议都把它列为"必须先行核查"的 TO DO；本建议若激活，等于在 `Let` 关键字八字还没一撇时就把它的载荷加到三份语法。`Suspect` 无遗留破坏，但 `langversion` 门控与回归矩阵是必要成本。

#### 6. Option Strict / 编译选项分叉

`Let v = e Else Return` 在 `Option Strict On` 下 `v` 静态定型，守卫测试是编译期可判定的"类型是否可空"；在 `Option Strict Off` 下 `v` 可为 `Object`，守卫退化为运行期 Nothing 比较。两条路径行为应当一致（守卫对非可空值类型恒不触发），但必须显式规定并给双路径示例。**建议对 Option Strict 只字未提**——这是品质扣分项，也意味着所有示例（含动机示例的 `TypeOf` 守卫）都没在 `Option Strict On` 下验证过（`GetActiveView(textManager, ...)` 在无收窄时按 `Object` 绑定会失败）。

#### 7. IDE / IntelliSense 影响

输入 `Let v = e Else` 之后补全应建议控制流语句；`Then Return v` 的 `v` 绑定要正确显示；块形式的 `End Let` 缩进与作用域灰化；`Else` 分支内变量"是 Nothing"的状态提示——这些全链路的 IDE 工作建议完全没有讨论。沿用我们的老规矩：不做进规范等于没设计。

#### 8. 数据 / 普遍性

守卫链样板真实，但**没有一条可量化数据**。VS 互操作服务获取链是 Anthony 自己的日常，不是"数十万安静客户"的日常；对业务代码，`If x Is Nothing Then Return` 之后继续区已被 `?.` 和流分析覆盖大半。对 VBScript.NET 的脚本化产品，早期退出守卫链确实更 idiomatic——产品使命可以算作数据的一部分，但这份建议连"它是为脚本化受众设计的"都没说。对照 Implicit-default-optional 的 85% 统计标准，这里只有"我觉得更顺手"。

#### 9. 更简替代

- `If ... Then Return` + 流分析（PROPOSAL C）：继续区价值已有，省"一行 + 一次变量名重复"，零新语法。
- 库函数 `TryGetValueOrCreate`（作者自指）：get-or-create 的完整答案，无语言成本。
- 声明模式 `Case x As T`：块形式的全部价值，且是主线最期待的地基。
- `?.` / `If(cond, fallback)`：回退表达式场景的既有答案。

我们的总结：**这份建议的每个子形态都有一到两个更强的现有或规划中的竞争者，而它自己的语法面是最重的。**

#### 10. 复杂度 / 成本 / 优先级

成本=新文法（`Let` 后的 `Then`/`Else`/`End Let`/`End Set`）+ 作用域规则 + 分支敏感流状态 + IDE 全链路；且依赖三个未定的邻近特性（`Let` 关键字、out 参数、声明模式）。价值=守卫链省一行。**成本/价值比是这份建议最大的问题。** 它排在所有依赖项之后；在 `Let` 关键字联合评审、ShapeOf 声明模式、out-arguments 三者落地之前，没有独立启动的理由。

#### 11. 运行时 / CLR 硬约束

无。全部是编译期/降级层概念；守卫测试是既有的 Nothing 比较，`TryCast` 是既有的 `castclass` 安全操作，无 PEVerify、无表达式树问题（表达式树按原宽类型生成）。

#### 12. 值不值得做

价值：真实但小（省一行 + 少写一次变量名），且大半被流分析预支。成本：中，且绑三份未定依赖。风险：中——`Let` 关键字过载、作用域未定、与声明模式抢地盘。**结论：不值得现在做；值得留在 inactive 等待激活信号。** 这不是对概念的否决——"把守卫折进声明"是一个正当的、作者直觉可辩护的方向（尤其是对 Swift 语法的拒绝，我们完全同意）——而是对**当前状态**的裁决：设计未收敛、依赖未落地、示例不能编译。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 5（读起来像英语、对新手友好）**——本建议唯一无可争议的正资产。`Let v = e Else Return Nothing` 是祈使句；作者"repeating the variable name to check for null was annoying"的诊断我们同意。**它服务的是脚本化产品的调性，而 VBScript.NET 正是这个产品。** 这条原则撑起了"保留 inactive 而非 Reject"的结论。
- **原则 7（避免隐蔽控制流/语义变化）**——这里要分两面。**好的一面**：守卫是显式拼写的，`Else Return` 把控制流写在纸上，不靠细微字符（对照 #167 `Return?`："We think this is a bad idea. Control flow would be altered by a very subtle character."——本建议**不是**那种隐蔽变化）。**坏的一面**：一个**声明语句**承载分支语义，把"声明"这一最安静的话法变成控制流语句，是 VB 从未有过的范畴。显式性给它加分，范畴新颖性给它减分。
- **原则 3（不引入第二种做事方式）**——正面撞墙。守卫链已有 `If ... Then Return`；块形式是声明模式的第二写法。这是"第二种做空守卫的方式"+"第二种做模式绑定的方式"。
- **原则 9（消除常见样板）**——正中靶心，但靶心已被流分析打掉一半。这是本建议与 typeof-flow / nullability-flow 共享的直觉：同一个样板，两份建议，一份零语法，一份重语法。
- **原则 8（不与既有语法冲突）**——`Let`（查询子句）、`End Set`（属性访问器）、`Then`（If 专属词）三处复用，每一处都靠上下文消歧。
- **原则 2（保持 VB-like）**——`Let ... End Let` 块、声明语句带 `Then`/`Else`，无先例可循。VB 的块构造（`If`/`For`/`While`/`Using`/`SyncLock`/`Try`）都是**语句**块，不是声明块；声明带分支是外来范畴。

对照主线表（2.3）：`Let` 替换 `Dim` 一行是 **Anthony 独立延伸**（主线"无"）；流分析、模式匹配、元组解构是**主线一致/最期待**的地基。**本建议是叠在主线一致地基上的 Anthony 独立延伸**——它与主线不冲突，但若不顾及地基（声明模式、流引擎）自行落地，就会变成与主线特性重叠的第二机制。这正是"沙盒式激进延伸"的根本张力在单份建议上的投影：直觉共享，节奏与范围不同。

### RESOLUTION:

**RESOLUTION:** We keep the proposal inactive. The concept is sound; the document, as a specification, is not converged, and it is blocked on three neighbors it does not acknowledge.

1. **三态判定：`Table`（保持 inactive），不 Reject。** 概念正当（守卫折进声明、对 Swift 语法的拒绝可辩护、英文祈使读法符合原则 5），面向 VBScript.NET 脚本化产品有真实吸引力；但设计未收敛、依赖未落地、示例不能编译，没有现在激活的理由。
2. **单语句守卫（`Let v = e Else <CFS>` / `Then <CFS>`）是唯一干净的候选子形态。** 但它的收益必须以共享流分析引擎为前置：把"声明守卫 ⇒ 继续区 NotNull"登记为 `typeof-flow` / `nullability-flow` 共享引擎的一个**新事实源**（与 `If x Is Nothing Then Return` 同一条路径），不得另建一套流机制。PROPOSAL B 记为后续候选，若复活先做它。
3. **块形式（`Then ... End Let`）与声明模式价值重叠，重审时机在 ShapeOf 之后。** 2018-12-19 已把 `Declaration pattern - type match with assignment equivalent` 定为主线最期待的第一阶段；`Let v As T = TryCast e Then ... End Let` 是它的 `Let` 语法重述。**若声明模式落地，块形式 Reject；若不落地，块形式复议。** 在此之前不承诺。
4. **条件 `Set` 形态 Reject。** 与属性访问器 `End Set` 撞名、文法未定义、get-or-create 由库函数（`TryGetValueOrCreate`，作者自指）承担更干净。`End Else` 直接否决：作用域是语义问题，不是终结符问题。
5. **作用域规则定夺方向（供复活时采用）：** 块形式变量在构造块内作用域；构造后由两分支赋值合并（definite-assignment merge）决定非空。单语句守卫变量作用域与 `Dim` 一致（到当前块结束），守卫以提前退出建立继续区 NotNull。
6. **激活信号（三条缺一不可）：**
   ① **`Let` 声明关键字联合评审先行**——`local-declarations` 复活信号①明确点名本建议为"声明方言"评审四方之一；本建议在评审完成前不得抢跑关键字。
   ② **共享流分析引擎就位**——引擎支持"声明守卫 ⇒ 继续区 NotNull"与分支敏感空状态。
   ③ **文档返工到可编译、可收敛**——全部示例在 `Option Strict On` 下用 `Nothing`（非 `Null`）编译通过；`Then Return v` 示例改用 out 参数捕获修正或删除；明确三种守卫（类型/空/成功标志）各自的语法覆盖；作用域规则落成 definite-assignment 描述；补文法与双路径验证。
   ④ **ShapeOf 落地后重估与声明模式的重叠**——若重叠确认，块形式并入模式工作项。

**Implication:**

- 把"声明守卫 ⇒ NotNull"登记进共享流分析引擎的 TODO，与 `TypeOf x Is T` 真分支＝收窄＋NotNull（nullability 会议 RESOLUTION 第 7 条）并列。
- 在 `local-declarations` 的"声明方言"联合评审中登记本建议为依赖方之一，声明其需要 `Let` 关键字才能被评估。
- 与 `shapeof-pattern-matching` 对表：块形式的价值归属（声明模式 vs 本建议），待 ShapeOf 设计落地后裁定。
- 条件 `Set` 的拒绝结论回传 `set-statement`：语句级 `Set` 若未来落地，不得扩出守卫块。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 整体 `Table`（保持 inactive）。PROPOSAL D 按"否决的备选方案"留档；条件 `Set` 形态按"否决的子特性"留档；单语句守卫为唯一的复活候选子集。

### OPEN QUESTIONS / TODO / Follow-up

- [ ] 三种守卫（类型 `TypeOf`/`TryCast`、空 `Nothing`、成功标志 `Boolean`）在"守卫式声明"里应各自有怎样的语法覆盖？还是只做其中一种？
- [ ] `Else` 块形式的 definite-assignment 分支合并规则：两分支都赋值（或退出）时构造后非空；若 Else 分支不赋值，构造后状态如何？
- [ ] 多变量守卫形态 `Let a, b = ... Else Return` 是否存在？若存在，逗号语义与 `Dim` 逗号列表/解构括号（2016 定案）如何统一？
- [ ] 守卫对非可空值类型是编译错误还是恒不触发（倾向：编译错误或警告）。
- [ ] 控制流语句清单（`Return`/`Throw`/`Exit`/`Continue`）的语境合法性规则表。
- [ ] `Then Return v` 的 out 参数捕获与 `out-arguments` 建议的交互——这是单语句 `Then` 形态能否成立的先决条件。
- [ ] 块形式与 `Case x As T`（ShapeOf 声明模式）的重叠分析，待 ShapeOf 落地。
- [ ] 动机示例在 `Option Strict On` 下的可编译性——无收窄时 `GetActiveView(textManager, ...)` 按 `Object` 绑定失败，需 `TryCast` 改写。

### 状态

- **LDM 状态：`LDM No Plans`（保持 inactive）。** 本建议不排队进入任何版本的候选集。
- **三态判定：Table** — 概念正当、方向可辩护，但设计未收敛、三重依赖未落地、示例不可编译；激活信号见 RESOLUTION 第 6 条。不 Reject：VBScript.NET 的脚本化受众会让"守卫链省样板"重新值得，而那需要先兑现 `Let` 关键字与流分析引擎。

---

## 附录：特性评价

### 评价对象

- 建议：`inactive/proposal-guarded-let.md` — Guarded `Let`（`Let ... Then/Else/End Let` 守卫式声明）。
- 来源：Anthony 原文第 **18.2 节 "Guarded `Let`"**（`..\..\AnthonyDesign_wordpress.txt` L2732–2819）；动机示例为 VS 互操作服务获取链。
- 配方目标：把"声明 + 空/类型检查 + 提前退出"压缩进一条声明语句，消除守卫链样板；作者不喜欢 Swift `if let`/`guard let` 读法，自创 `Let ... Then/Else/End Let` 语法。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | Motivation 生动、痛点真实，但改进不可衡量；头牌形态（`Else <CFS>`）只覆盖"空守卫"，而动机示例的守卫是"类型/成功标志"守卫——**示例不能演示本特性的主效果**；`Then Return v`（TryGetValue）示例编译不了（`TryGetValue` 返回 `Boolean`，`v` 未捕获 out 参数）；未决关键点 ≥4（作用域、条件 Set、多变量、Result Pattern）→ 效果证据封顶。锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进" | 已检查 | 无原型；主示例与守卫条件不匹配；单语句形态需要 out-arguments + 流引擎两个未定依赖才能兑现 |
| 特性 | **2/5** | 语法为作者自创，不继承 VB 基因；复用 `If` 的 `Then`/`Else`、属性访问器的 `End Set`、查询子句的 `Let` 并赋予新含义；块形式是声明模式（`Case x As T`）的 `Let` 重述；条件 `Set` 是"继承 VB6 的皮、换 VB6 的骨"（set-statement 会议同款诊断）；只有英文祈使读法符合原则 5。锚点 2："外来/自创特性未 VB 化；或多个强无关能力捆绑"（单语句 + 块 + 条件 Set 三形态捆绑） | 已检查 | 三形态血缘不同却捆绑；与声明模式、set-statement 的概念重叠未识别；无主线锚点（`Let` 为 Anthony 独立延伸） |
| 品质 | **2/5** | 六章节模板完整、Drawbacks/Alternatives/Unresolved 诚实（作者自疑如实保留——加分）；但状态栏为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；无文法（BNF）；无 `Option Strict` 分叉；无 Compatibility/breaking-change 小节；`Then Return v` 示例与正文冲突；"变量（或一组变量）"的多变量形态未定义；条件 `Set` 的 `End Set` 文法凭空出现。锚点 2："示例与正文冲突；多处章节缺失" | 已检查 | 红旗：占位链接；示例冲突；守卫条件与动机示例错位；依赖（`Null` 字面量、`Let` 关键字、流引擎、out 参数）全部未声明 |
| 属性 | **2/5** | 对 VBScript.NET：守卫链省样板、祈使读法（光/雷）真实正向；但风=与流分析/声明模式的价值重叠未识别、`Let` 关键字载荷再加一档（一致性断裂风险）；暗=作用域未定、`Null` 依赖、`End Set`/`End Else` 未定、示例不可编译；文档未对冲任何一项。锚点 2："某关键维度受损且无应对" | 已检查（预测性，标"待定"） | 与 pattern/flow/set 三份建议的重叠与依赖无一处交叉引用；暗风险无对冲设计 |
| 炼金成分 | **3/5** | 材料来源（Anthony 18.2）标注正确；但未声明：守卫概念源自 Swift `if let`/`guard let`（作者明确以它为参照系拒绝，2017-10-18 主线已在 #190 提过同型方案）、块形式借自声明模式（`Case x As T`）、`Null`/`Let`/流引擎/out 参数四份依赖未交叉引用。锚点 3："部分来源未标注；标注与影响有偏差" | 已检查 | 概念血缘（Swift 守卫 + 声明模式）未点明；依赖关系未交代；无杂质（未借鉴闭源） |

### 设计原则对照

- **与 VB 基因**：**偏离为主**。一致：原则 5（英文祈使读法，唯一强正资产）、原则 9（消样板，但大半被流分析预支）。偏离/触墙：原则 3（第二种做空守卫/模式绑定的方式）、原则 7（声明语句承载分支语义——显式性加分、范畴新颖性减分，不同于 `Return?` 的隐蔽变化）、原则 8（`Let`/`End Set`/`Then` 三处关键字复用）、原则 2（`Let ... End Let` 声明块无先例）。
- **与主线关系**：**Anthony 独立延伸**（对照表 2.3 `Let` 行：主线"无"）。叠在**主线一致**的地基上（流分析、模式匹配、元组解构都是主线方向）；不与主线冲突，但若不顾地基自行落地即成重叠的第二机制。
- **破坏性变更**：**无**（全为新语法；语句位置 `Let` 今天报错，"错误→程序"）。风险面：`Let` contextual keyword 的解析器表面（查询子句 `Let` 必须保留），需 parser 核查 + `langversion` 门控。

### 总评

- **达成程度**：**未达成**（作为规范文本）。概念是一个正当方向，但文档是未收敛的草图：守卫条件与动机示例错位、`Then Return v` 不能编译、作用域未决由作者自疑供认、条件 `Set` 文法凭空出现、四份依赖全部未声明。
- **LDM 三态建议**：**Table（保持 inactive）**。单语句守卫为唯一复活候选子集；块形式随声明模式（ShapeOf）重估；条件 `Set` Reject；不 Reject 全文——概念与作者对 Swift 语法的直觉有辩护价值，且面向 VBScript.NET 脚本化受众的守卫链需求真实。
- **主要问题**：(1) 守卫条件（空）与动机示例（类型/成功标志）不匹配，头牌形态服务不了自己的动机；(2) `Let` 关键字（Table）+ 流引擎 + out 参数 + 声明模式四重未定依赖；(3) 块形式与 `Case x As T` 价值重叠；(4) 作用域规则未定（`End Else` 是错误的问题提法）；(5) `Then Return v` 与 `Null` 依赖使示例不可编译；(6) 条件 `Set` 与属性访问器 `End Set` 撞名且文法未定义；(7) 无文法、无 Option Strict、无兼容性小节、状态占位链接。

### 返工建议

- **范围切分**：拆成「单语句守卫」「块形式」「条件 `Set`」三块分别裁定；单语句守卫为主体候选，块形式移交模式工作项评估，条件 `Set` 归档。
- **补充章节**：文法（`Let` 守卫语句 BNF、`End Let`、`Then`/`Else` 在声明后的位置文法、多变量形态的逗号规则）；作用域与 definite-assignment（Else 块两分支赋值合并，明确否决 `End Else`）；与共享流分析引擎的状态契约（"声明守卫 ⇒ 继续区 NotNull"）；`Option Strict On/Off` 双路径验证；Compatibility/breaking-change 小节。
- **补充证据**：修正全部示例在 `Option Strict On` 下用 `Nothing` 编译通过；`Then Return v` 改为 out 参数捕获的完整形态（与 `out-arguments` 对表）或删除；动机示例用 `TryCast` + `As T` 重写以覆盖类型守卫；原型（在 `Let` 关键字 + 流引擎之上的最小增量）。
- **未决问题处理**：作用域按"构造块内作用域 + 分支合并"定夺；多变量守卫与解构括号（2016 定案）统一；值类型守卫按编译错误处理；控制流语句语境规则表；Result Pattern/Case 类交互移交 `case-classes` 工作项；激活信号三条（RESOLUTION 第 6 条）逐条登记为 TODO。

---

## 附录：C# 生态与互操作考量

### 相关 C# 现实方向

C# 从未把「守卫折进声明」做成一等语言特性。它在模式匹配（C# 7）与流分析（definite assignment + C# 8 可空引用类型）上把同一直觉拆成了三件套：**模式绑定**（`is` / `switch` 模式变量）、**负向守卫**（`is not`）、**out 参数捕获**（`out var`）。VBScript.NET 若要把守卫链样板「脚本化」，面对的不是「C# 有对应语法要模仿」，而是「C# 已经用这三件套覆盖了本提案几乎全部价值主张」。逐字原文与出处：

**① 模式绑定——块形式在 C# 的等价物（C# 7 声明模式）**

> "Every *identifier* of the pattern introduces a new local variable that is *definitely assigned* after the `is` operator is `true` (i.e. *definitely assigned when true*)."
> → `proposals\csharp-7.0\pattern-matching.md`

> "The *declaration_pattern* both tests that an expression is of a given type and casts it to that type if the test succeeds. This may introduce a local variable of the given type named by the given identifier, if the designation is a *single_variable_designation*. That local variable is *definitely assigned* when the result of the pattern-matching operation is `true`."
> → `proposals\csharp-8.0\patterns.md`

**② 模式变量作用域——与本提案 Q3 七寸直接对撞（C# 7/8）**

> "The scope of a variable declared in a pattern is as follows: - If the pattern is a case label, then the scope of the variable is the *case block*. ... Otherwise if the expression is in some other statement form, its scope is the scope containing the statement."
> → `proposals\csharp-7.0\pattern-matching.md`

> "The condition of the `if` statement is `true` at runtime and the variable `v` holds the value `3` of type `int` inside the block. After the block the variable `v` is in scope but not definitely assigned."
> → `proposals\csharp-8.0\patterns.md`

**③ 负向守卫——单语句 `Else Return` 在 C# 的等价物（C# 9 `is not`）**

> "A common use of a combinator will be the idiom `if (e is not null) ...` More readable than the current idiom `e is object`, this pattern clearly expresses that one is checking for a non-null value."
> → `proposals\csharp-9.0\patterns3.md`

> "This does not work today because, for an *is-pattern-expression*, the pattern variables are considered *definitely assigned* only where the *is-pattern-expression* is true ("definitely assigned when true")."
> → `proposals\csharp-9.0\patterns3.md`

> "Result: Pattern variables can't be declared beneath a `not` or `or` pattern."
> → `proposals\csharp-9.0\patterns3.md`

**④ out 参数捕获——`Then Return v` 形态在 C# 的组合（C# 7 `out var`）**

> "A variable declared this way is called an *out variable*. You may use the contextual keyword `var` for the variable's type. The scope will be the same as for a *pattern-variable* introduced via pattern-matching."
> → `proposals\csharp-7.0\out-var.md`

**⑤ switch 表达式与穷尽性——类型守卫链的表达式化（C# 8）**

> "A switch expression is said to be *exhaustive* if some arm of the switch expression handles every value of its input. The compiler shall produce a warning if a switch expression is not *exhaustive*."
> → `proposals\csharp-8.0\patterns.md`

**⑥ C# 15 unions / closed hierarchies——类型守卫的未来形态（2025–2026 主线，索引 T8/M4）**

> "*Union matching*: Pattern matching against union values implicitly "unwraps" their contents, applying the pattern to the underlying value instead."
> → `proposals\unions.md`

> "*Union exhaustiveness*: Switch expressions over union values are exhaustive when all case types have been matched, without need for a fallback case."
> → `proposals\unions.md`

> "Any class or struct type with a `System.Runtime.CompilerServices.UnionAttribute` attribute is considered a *union type*"
> → `proposals\unions.md`

> "Allow a class to be declared `closed`. This prevents directly derived classes from being declared in a different assembly"
> → `proposals\closed-hierarchies.md`

> "Since all derived classes are declared in the closed class' assembly, a consuming `switch` expression that covers all of them can be concluded to "exhaust" the closed class - it does not need to provide a default case to avoid warnings."
> → `proposals\closed-hierarchies.md`

### 现实 vs 提案

逐条对照：

- **块形式（`Let v As T = TryCast e Then ... Else ... End Let`）↔ C# 声明模式：正面重叠（同直觉、同机制、同作用域难题）。** C# 自 7.0 起 `if (e is T v) { ... }` 就是"类型测试 + 绑进块内作用域、块外 in scope but not definitely assigned"（② 原文）。本提案块形式想做的"测试 + 绑定 + 分支敏感空状态"，C# 模式绑定早已量产；唯一差异是 C# 把"继续区"写进真分支块，本提案想用声明块表达同一件事。**结论：兼容但冗余**——C# 现实强烈支持 RESOLUTION 第 3 条（块形式并入声明模式，随 `Case x As T` 重估）。
- **单语句守卫（`Let v = e Else <CFS>`）↔ C# 9 `is not` + 流分析：价值已由 C# 兑现一半。** `if (e is not null) return;` 是"负向守卫 + 提前退出"的 C# 形态，且不绑定新变量（③ 原文：`not`/`or` 下不许声明模式变量）。本提案单语句守卫绑定 `v` 并在继续区收窄 `v`——C# 不这么做：它收窄**原表达式** `e`，不产生守卫后仍存活的新变量。**差异点正是作用域语义**（见下）。**结论：兼容但 C# 走了更省的路**（即 PROPOSAL C 的 C# 版本）。
- **`Then Return v`（TryGetValue 形态）↔ C# 7 `out var`：C# 已量产，且就是"两特性组合"。** `if (dict.TryGetValue(k, out var v)) return v;` 是 C# 惯用法；`out var` 作用域与模式变量相同（④ 原文）。这坐实了本会议 Q5 的 `Suspect`：单语句 `Then` 形态的真身是"声明守卫 + out 参数捕获"的合体，而 C# 证明这个合体不需要新文法——`out var` + `if` + `return` 足矣。
- **get-or-create（Q3）↔ C# `out var` + if/else 分支合并：本 RESOLUTION 第 5 条的决策在 C# 有验证。** C# 对"构造后 `list` 是否可用"的回答正是 definite-assignment 分支合并：`if (dict.TryGetValue(k, out var list)) { } else { list = New List(...); } list.Add(value);` 在 else 分支补赋值后，合并点 `list` 即 definite-assigned。C# 没有 `End Else`，也没有"守卫后新变量继续作用域"——它靠普通 if/else + DA 合并，与本 RESOLUTION 的定夺方向一字不差。**结论：兼容，且为 RESOLUTION 提供外部验证。**
- **类型守卫链（TypeOf IsNot）↔ C# `is not` 模式 + unions：C# 15 会让链条更短。** 动机示例的 `If TypeOf textManager IsNot IVsTextManager Then Return Null` 在 C# 是 `if (svc is not IVsTextManager) return null;`；若服务对象是 C# 15 union，穷尽 switch 表达式 + 模式绑定（"unwraps contents"，⑥ 原文）把整个守卫链折叠成穷尽匹配。**结论：本提案的动机场景在 C# 侧正被模式/union 持续吸收。**
- **脱节点：C# 没有、也不需要「守卫式声明」。** C# 的选择是把"绑定"放进测试表达式（`is`）、把"退出"留给 `if`/`return`、把"收窄"交给流分析，三者皆是既有语法。`pattern-variables.md`（disjunctive pattern 变量重声明，csharplang #8622）只是放宽"同一变量在多处声明以共享代码"，仍未触碰"声明 + 守卫"合成。本提案若独立落地，等于在 C# 都认为"三件套已够用"的领域新增一套声明文法——**这是本提案与 C# 现实最根本的错位**，但错位在「表面」，不在「直觉」。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：守卫式声明是纯编译期概念（`castclass` + Nothing 比较 + 分支，会议第 11 条已判无 CLR 硬约束），不引入 dynamic/反射，天然落在「默认安全」一侧，与 C# 的 AOT/trimming 压力（索引 T5）无冲突。若复活，应作为编译期语言特性而非脚本解释器特性——保持「编译到受管程序集」的主线，与 interpreted 模式（决策文件 M5）解耦。
- **source-gen 桥（索引 T6）**：守卫式声明的降级是局部改写（测试 → 分支 → 绑定），source generator / incremental generator 完全可承担，也符合「编译期生成替代运行时动态」的生态方向。若做成 `Let` 关键字语法糖，其展开逻辑与 `Dim` + `If` 展开共用同一降级管线即可。
- **识别新元数据（决策文件 M4/M8）**：守卫式声明本身不产生新元数据；但 VBScript.NET 消费 C# 15 union / closed hierarchy 类型时，必须识别 `UnionAttribute`（⑥ 原文）与 closed 元数据，否则「穷尽 switch 表达式」与「模式绑定 unwrap」在跨语言边界的语义会失真。unsafe-evolution 对 VB 的表态——"We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."（`proposals\unsafe-evolution.md`）——说明 VB 侧只需**识别**新元数据、不必实现对应机制；同一原则适用于 union 元数据。
- **声明模式桥**：与 C# 声明模式对等的是 VB 的 `Case x As T`（ShapeOf 声明模式）。VBScript.NET 若想消费 C# 侧"类型测试 + 绑定"的 API 直觉，应优先落地声明模式而非守卫式声明——后者是前者的语法重述（RESOLUTION 第 3 条）。守卫链样板在 VB 侧的最终答案与 C# 相同：**声明模式 + 共享流分析引擎**（PROPOSAL C）。

### 对既有 RESOLUTION/三态判定的影响

本附录不改变三态判定（`Table`），但为其中三条核心裁决提供 C# 侧外部证据：

1. **RESOLUTION 第 2 条（单语句守卫依赖共享流引擎、登记"声明守卫 ⇒ 继续区 NotNull"）**——C# 可空流分析对 `e is not null` 的收窄是同一事实（③ 原文）；C# 证明"守卫收窄"由流引擎承担是成熟路线，无需新机制。
2. **RESOLUTION 第 3 条（块形式与声明模式重叠、随 ShapeOf 重估）**——C# 7 起声明模式就是"测试 + 绑定 + 块内作用域"的主力，且 C# 15 unions 进一步把类型守卫折叠进穷尽匹配。**C# 现实加重"块形式并入声明模式"的分量。**
3. **RESOLUTION 第 5 条（作用域用 definite-assignment 分支合并）**——C# 对 get-or-create 的既有答案（`out var` + if/else + DA 合并）与本条定夺方向完全一致，可作为规范文本的外部佐证。
4. **Q5（`Then Return v` 需 out-arguments）**——C# `out var` 证明该形态是"out 捕获 + if + return"的组合而非新文法，支持"单语句 Then 形态依赖 out-arguments"的判定。

### 引用纪律核对

- 以上全部 C# 原文均在本仓库 `..\..\..\csharplang` 内逐字核实，标注 `proposals\...` 路径；未发现需要 **Suspect** 的引用。
- 索引第四节 6 段已核实原文中，与本文直接相关的是 unsafe-evolution 对 VB 的表态（已核实于 `proposals\unsafe-evolution.md` L377）；其余（native-integers / function-pointers / blittable / span-safety / ref-struct-interfaces）与本提案主题关系弱，未引用。
- **OPEN QUESTIONS**：(1) C# 侧"守卫式声明"是否曾在 LDM 被正面讨论过——索引未含、本附录未深挖全部会议纪要，若未来需要可补查；(2) union 模式绑定（Try Both / Syntax Decides，`meetings\working-groups\discriminated-unions\union-patterns.md`）尚属工作组的方案对比文档而非定案规范，本附录将其标注为"工作草案"，引用 unions.md / closed-hierarchies.md 定案正文时不受影响。
