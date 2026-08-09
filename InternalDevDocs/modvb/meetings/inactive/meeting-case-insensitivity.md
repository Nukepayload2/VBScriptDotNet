# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题同样来自建议目录的 **inactive** 子目录——`proposal-case-insensitivity.md`（Anthony 原文第 18 章 18.9 "More elegant integration of case-insensitivity/collation"，原文自标 "it needs more consideration"）。这又是一份"没有给出任何具体语法"的实验性想法：它精确地指出了痛点，但没有给出解药。按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期"，今天会议的真实问题是：**这个痛点值得激活一项新机制，还是维持现状、把其中能独立成立的小块拆出来分别对待？** 我们开场时的态度是审慎的——主线对 `Option Compare` 相关议题已经表过两次低价值的态，而本建议恰好在同一片设计面上。

## Agenda

* [Proposal: 大小写不敏感与排序规则集成（Case-Insensitive Collation Integration）](#proposal-大小写不敏感与排序规则集成)

## Proposal: 大小写不敏感与排序规则集成

_Related: [vblang #193 – Option Compare Ordinal (and OrdinalIgnoreCase?)](https://github.com/dotnet/vblang/issues/193)；[vblang #218 – Expression tree rewrite for string comparison in VB should product an operator invocation, not a method call to Operators.CompareString](https://github.com/dotnet/vblang/issues/218)；[vblang #117 – Block-scoped Option Statements](https://github.com/dotnet/vblang/issues/117) 与 [#255 – Localised Compiler Options](https://github.com/dotnet/vblang/issues/255)；2018-02-07 会议（块级 Option，含 Option Compare 表态）；2016-05-06 会议（标识符大小写不敏感）；ModVB：`meeting-string-pattern-matching`、`meeting-json-pattern-matching`、`meeting-runtime-library`_

### 场景与缺口

We started from an observation that touches the oldest DNA in the language：内置字符串比较是 "a hallmark of BASIC since time immemorial"（Anthony 18.9 原句）。VB 有许多内置构造参与字符串比较——二元表达式中的 `=`、`<>`、`<`、`>`、`<=`、`>=`，以及 `Select Case` 中的同一批运算符。而许多应用受益于大小写不敏感的字符串比较，**代价是放弃这些语法糖**：

```vb
' 今天：为了一次大小写不敏感，放弃语法糖
If name.Equals("admin", StringComparison.OrdinalIgnoreCase) Then
    GrantAccess(name)
End If

If Not name.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) Then
    Throw New UnauthorizedAccessException()
End If
```

`Option Compare Text` 是现成的逃生门，但 Anthony 指出了它的三重缺口：**第一**，"it has unexpected null handling"（意外的 null 处理）；**第二**，它只影响比较运算符、`Select Case` 以及 VB 运行时库中的某些 API（底层是 `<CallerOptionCompareSettingAttribute>` 这个 CallerInfo 机制）；**第三**，"And this doesn't include the `Distinct` query operator at all"。于是用户不得不放弃语法糖，改调 `Equals` 并传入 `ComparisonType` 或 `StringComparer`。

```vb
Option Compare Text

Module CasingDemo
    Function IsAdmin(name As String) As Boolean
        If name = "admin" Then      ' 受 Option Compare Text：大小写不敏感
            Return True
        End If
        Select Case name            ' 受 Option Compare Text
            Case "ADMIN"
                Return True
        End Select
        Return False
    End Function
End Module
```

```vb
Imports System.Linq

Module QueryDemo
    Function CountUnique(names As IEnumerable(Of String)) As Integer
        ' Option Compare Text 不覆盖 Distinct：这里仍然区分大小写
        Return names.Distinct().Count()
    End Function

    Function CountUniqueInsensitive(names As IEnumerable(Of String)) As Integer
        ' 显式比较器是今天的出路
        Return names.Distinct(StringComparer.OrdinalIgnoreCase).Count()
    End Function
End Module
```

Anthony 还断言，String 与 JSON 模式匹配加入后这个问题会更糟——"This issue becomes worse with String and JSON pattern matching"。`Case $"/echo {message}"` 里的字面量前缀 `/echo `、JSON 模式里的属性名，未来都要面对"是否大小写不敏感"的裁定。We see the gap clearly；但我们也立刻注意到：**这份建议的 Detailed design 一节没有任何语法**——它只是一份现状清点与调查起点清单。这一点将主导整场会议。

### 候选方案

**PROPOSAL A — 局部比较上下文（local comparison context）。** 建议 Alternatives 里提到的"引入显式的比较上下文对象，在特定代码块内局部切换排序规则"。形态可以是块级 `Option`/`End Option`（2017-08-23 会议提到过这种块形态），或新的比较器作用域。这是对"全局状态"问题的直接回应：把排序规则变成局部、显式、可读的。

**PROPOSAL B — 扩展现有 `Option Compare Text` 的覆盖范围。** 不动语法，把它的影响面补全：覆盖 `Distinct` 等查询操作符、覆盖 String/JSON 模式匹配的比较，并（可选）修正其 null 处理。这是"让既有机制更完整"的路线，符合原则 #3（不引入第二种做事方式）。

**PROPOSAL C — 显式比较器进入既有构造。** 让 `Select Case`、`Distinct`、甚至比较运算符可以显式携带 `StringComparison`/`StringComparer`，例如 `Case "admin", StringComparison.OrdinalIgnoreCase` 或 `Distinct(StringComparer.OrdinalIgnoreCase)`。这是"与现有 .NET API 集成"字面意义上的形态，也是 Anthony 想要的"优雅方案"的最直白解释。

**PROPOSAL D — 什么都不做，保持现状。** `Option Compare Text` 覆盖它覆盖的，`Equals` + `StringComparison`/`StringComparer` 覆盖其余的。显式的比较参数是"有用的冗长"（与上次 target-typed 会议对转换目标类型的结论同构）。

### 权衡：Q&A

- **A vs D：局部比较上下文值得吗？** 这是全场最容易被回答的问题，因为主线已经答过了。2018-02-07 会议审议块级 Option（#117/#255）时逐项过四个 Option："Option Strict: Would provide value, possible to do / Option Explicit: Not seeing value, difficult to do / **Option Compare: Not seeing high value, could create confusing code** / Option Infer: This doesn't really make sense to us"。主线对 `Option Compare` 局部化的估值是"价值不高、且会制造令人困惑的代码"。A 方案本质上就是给这句话翻案，而建议没有提供任何反证。**We are not inclined to overturn that assessment。**
- **B：补上 `Distinct` 的覆盖，是不是顺手的事？** 不是。第一，`Enumerable.Distinct()` 的默认对 `String` 走 `EqualityComparer(Of String).Default`——那是 BCL 的 ordinal 区分大小写，与 VB 运行时库的 `CompareString` 完全无关。要让 `Option Compare Text` 影响它，等于要求 VB 编译器重写每一个 `Distinct()` 调用为"注入当前比较设置的调用"。这正是主线 2017-12-06 会议记录 #218 时的那句原话所拒绝的领域："back compat concerns, trees, operators with methods, new VB runtime, fallbacks, **LINQ providers should fix their stuff**, ..."——查询比较的责任在 LINQ providers 与显式比较器，不在语言设置。第二，即便我们做了，这也是破坏性变更：既有程序里的 `Distinct()` 结果会随一个文件级指令改变。
- **C：显式比较器进 `Case`/运算符，是不是更"VB"？** 形态上它最接近 Anthony 想要的"与现有 .NET API 集成"，但它有一个立即可见的文法歧义：`Select Case` 的分支本来就可以写多个值（`Case 1, 2`），`Case "admin", StringComparison.OrdinalIgnoreCase` 无法与"两个分支值"区分。运算符级（`a = b` 携带比较器）则要求新的修饰符或新 token，与 `=`/`<>` 的既有文法冲突。C 不是不能做，而是它要为新文法付钱，而新文法的边界恰好全落在最敏感的重载/绑定区域。
- **null 处理："修正"它算不算免费午餐？** 不算。任何"修正"都是改变既有程序在 `Option Compare Text` 下的运行期行为——**破坏性变更**。主线对破坏性变更的基调是"we will almost never make breaking changes"；在没有数据证明"现有程序因 null 处理而出错"之前，"修正"的风险大于收益。它更适合被拆成独立的小议题（见 RESOLUTION）。
- **模式匹配的角落在哪边？** 在模式匹配那边，不在本建议这边。`Case $"/echo {message}"` 的字面量前缀比较、JSON 属性名匹配的大小写敏感与否，应当由模式匹配工作项（`meeting-string-pattern-matching`、`meeting-json-pattern-matching`）在设计时明确定义默认与 opt-in，而不是让一个文件级 `Option Compare` 指令悄悄改变匹配行为——那会把"模式是否命中"变成"这个文件的第 3 行写了什么指令"的隐蔽依赖。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**建议没有任何语法候选，所以"歧义"目前是潜在的而非存在的**——这是本建议品质上的第一缺口，也让我们无法对它做真正的语法评审。我们只能对三个候选方向分别预判：

- A（局部比较上下文）：需要 `Option`/`End Option` 块或等效作用域文法。`Option` 指令今天只允许出现在文件顶部、任何代码之前；把它放进方法体是一个新的文法位置。2017-08-23 会议讨论方法级 `Option`/`Imports` 时提出过 `Option`/`End Option` 块形态，但该会议同样没有落定。
- C（比较器进构造）：`Case "admin", StringComparison.OrdinalIgnoreCase` 与多分支值列表 `Case 1, 2` 无法区分——见 Q&A。这是 C 的命门。
- 运算符级修饰（`a = b` 带比较器）：需要新 token 或修饰符，与二元运算符的文法、优先级、结合性全部纠缠。

**没有候选形态，就没有歧义清单；反过来，不先画出一版可编译的示例，本建议无法进入语法评审。**

#### 2. 角案例与边界语义

- **null 与空字符串。** Anthony 断言 `Option Compare Text` "has unexpected null handling"，但没有给出具体差异。我们记录事实层面：`CompareString` 对 `Nothing` 与空字符串的处理在 Binary/Text 两种模式下不一致，这正是 "unexpected" 的来源。具体行为矩阵需要运行期验证——`Suspect`，见 OPEN QUESTIONS。修正它 = 破坏性变更。

```vb
Option Compare Text

Module NullHandlingDemo
    Sub Probe()
        Dim x As String = Nothing
        Dim y As String = ""
        If x = y Then
            ' Anthony 所称的 "unexpected null handling" 现场。
            ' Binary 与 Text 两种模式下 Nothing/空串 的比较结果不一致；
            ' 精确差异需运行期验证（见 OPEN QUESTIONS）。
            Stop
        End If
    End Sub
End Module
```

- **Unicode 大小写折叠与 culture。** `StringComparison.OrdinalIgnoreCase` 是简单的代码点折叠；`CurrentCultureIgnoreCase` 则带 locale 规则（土耳其 I、`ß`/`ss`）。VB6 的 `Option Compare Text` 走系统 locale 的文本排序（`CompareMethod.Text`）。这意味着：**同一份源码在不同 locale 的机器上行为不同**。全局排序规则是"隐蔽的控制流/语义变化"（原则 #7）的教科书案例——正是 `Return?` 被拒的同类理由。任何新机制如果保留"全局 collation"的性质，就继承了这一条。
- **`Select Case` 的关系分支。** `Case Is > "m"`、`Case "a" To "m"` 这类区间分支同样走 `CompareString`。若机制只覆盖相等比较而漏掉关系比较，`Select Case` 内部会自相矛盾；若全部覆盖，实现面再扩大一倍。
- **编译期常量折叠。** `"admin" = "ADMIN"` 若在 `Option Compare Text` 下可在编译期折叠，编译器必须在编译期执行 culture-aware 比较——运行期与编译期是两套实现，行为必须逐字一致。
- **`Distinct` 的默认。** BCL 的 `EqualityComparer(Of String).Default` 是 ordinal 区分大小写，与 VB 运行时无关——见 Q&A。
- **类型边界。** 比较运算符同时作用于 `Char`、枚举、用户定义类型（`IsTrue`/`IsFalse` 路径）。"让构造感知大小写不敏感"必须只作用于 `String`，不得误伤其他类型参与比较运算符的路径。

#### 3. 作用域与绑定

`Option Compare` 是编译期指令。它影响运行期行为的唯一通道是 `<CallerOptionCompareSettingAttribute>`——编译器在调用被该特性标注的 API 时，把当前的比较设置作为实参注入。这个机制只在特定 VB 运行时 API 上有效。**新机制若要触达 `Distinct`、模式匹配、表达式树，它必须绑定到新的符号与新的调用注入路径**——这不是 CallerInfo 的延伸，而是语言层到运行期的一次新承诺。语义模型要在"这个比较发生在哪个排序规则下"返回可查询的结果，否则 IDE 与 analyzer 无法判断一段代码的语义。

#### 4. 与既有特性的交互

- **表达式树。** 2017-12-06 #218 已经记录：VB 的比较运算符在表达式树里下略为对 `Operators.CompareString` 的方法调用而非运算符节点，主线对"改为运算符调用"的重写的回应是 "Eh, back compat concerns, trees, operators with methods, new VB runtime, fallbacks, ... needa solution"——**悬而未决**。任何"比较器进比较"的新机制都必须在表达式树上表达比较器，否则 LINQ-to-Objects 与 LINQ-to-Providers 再次分叉。
- **Late binding / Option Strict Off。** 宽松模式下比较运算符对 `Object` 操作数也走 `CompareString`。新机制必须保证严格/宽松两条路径行为一致；宽松路径上"隐藏的排序规则"更难被开发者察觉。
- **运算符重载。** `Char`、枚举、用户类型参与 `=`/`<>` 时走各自路径；机制只应触达 `String`。
- **与模式匹配、null 安全、交并类型建议。** String/JSON 模式匹配的比较语义要由模式匹配工作项定义（见 Q&A）；null 处理问题与 `meeting-null-equality-operators`、`meeting-null-safe-behaviors` 的 null 语义工作共享同一片"Nothing 如何参与比较"的设计面。

#### 5. Breaking change 与兼容性

三条路线里，A 与 C 的**新语法部分**是加法（今天 `Option` 块在方法内、`Case ... , StringComparison` 都不合法，无既有代码被重解释）；B 的**扩展部分**是彻底的破坏：

- 修正 null 处理：改变 `Option Compare Text` 下既有程序的运行期行为。
- 覆盖 `Distinct`：改变既有查询结果——同一源码，重编译后 `Distinct()` 可能返回不同的集合。这是"重编译后换行为"，与 2015-01-14 记录的插值字符串重载风险同族，而破坏面是一整个查询操作符族。

按 2018-06-13 的基调（已在 ModVB 系列会议引用）——"We will almost never make breaking changes to Visual Basic"、且扩展表面积的门槛是 "bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"。**B 的扩展部分在起跑线上就输了。**

#### 6. Option Strict / 编译选项分叉

`Option Compare` 与 `Option Strict` 正交：严格/宽松都受 `Option Compare` 影响。两条路径对新机制的行为一致性义务是硬性的——若新机制在宽松模式下因 late binding 而行为不同，等于在两条路径上定义两种语言。建议没有做这个等价性论证（它什么都没有论证）。

#### 7. IDE / IntelliSense

- 若 A 落地，`Option` 块的作用域高亮、InfoTip 显示"当前生效排序规则"、跳转，都要新工作。
- 若 C 落地，`Case` 分支的补全要同时提示"分支值"与"比较器实参"，两种候选在同一个逗号后混排，体验待设计。
- 新诊断：例如"此比较不受 Option Compare Text 影响（Distinct/模式匹配）"——反过来，这类诊断正是"更简替代"路线（analyzer）能提供的，而不必等语言特性。

#### 8. 数据 / 普遍性

没有任何数据。真实的痛点（"为一次不敏感比较放弃语法糖"）我们承认存在，但 workaround 覆盖率也高：`String.Equals(a, b, StringComparison.OrdinalIgnoreCase)`、`Distinct(StringComparer.OrdinalIgnoreCase)`、`Contains(StringComparer)` 今天全部可用，社区与文档长期默认这条路。主线 #193 "Option Compare Ordinal (and OrdinalIgnoreCase?)" 挂起多年没有牵引力——这是"用户并没有在敲这扇门"的弱信号。`Suspect`：痛点真实但低频，未达到 Implicit-default-optional 的 85% 统计那样分水岭式的普遍性标准。

#### 9. 更简替代

- `String.Equals` / `String.Compare` 的 `StringComparison` 重载：今天可用，显式、局部、无全局状态。
- `StringComparer` 传入 `Distinct`、`Contains`、`ToUpper` 等：今天可用。
- **Analyzer + code fix**：检测"作者可能在两个字符串字面量/变量上期望不敏感比较"（如 `a = "ADMIN"`）并建议改写为显式 `Equals`——这是"给语法糖诊脉但不给语言开刀"的路线。作脚手架可，作替代不可（它不能让 `a = "admin"` 合法地不敏感）。
- 模式匹配的默认：让 String/JSON 模式的字面量比较**默认 ordinal**、需要时不敏感走显式模式选项——比让全局指令渗透进匹配更简单。

#### 10. 复杂度 / 成本 / 优先级

实现面横跨：运算符绑定、`Select Case`、查询操作符重写、运行时库 CallerInfo 注入、String/JSON 模式匹配、表达式树、IDE 补全/InfoTip/诊断——**且每一处都要在两套 Option 分叉下保持一致**。价值缺乏数据，主线两次表态低价值（#117/#255 的 Option Compare 局部化、"LINQ providers should fix their stuff"）。优先级：**低**，排在模式匹配、流分析之后。`We're not excited enough to spend compiler time on this。`

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题（字符串比较本无存储规则约束）。但有两个真实的运行时约束：**第一**，culture-aware 比较依赖机器 locale 状态，同一程序在不同机器上行为不同——这是设计不可控性而非 CLR 缺陷；**第二**，表达式树上表达比较器会触达 #218 的 `Operators.CompareString` 重写坑（back compat、LINQ providers），主线至今 "needa solution"。

#### 12. 值不值得做

逐维打分。**价值**：保留 BASIC 标志、消除样板——真实但被 workaround 覆盖 85%，残余缺口无数据。**成本**：高——横跨面极大、每处两套分叉、且"全局 collation"机制与主线价值判断相悖。**风险**：中高——修正 null 是破坏、扩展 Distinct 是破坏、全局状态是隐蔽语义变化。**结论：现在不值得做。** 这不是"太难的否决"（fantastic idea, too hard to do），而是价值填不满成本与风险的缺口。建议保持 inactive 是对的——**但我们可以从中拆出值得单独处理的小块**。

### VB 基因对照

- **永不破坏既有代码（原则 #1）**：B 的扩展部分（null 修正、Distinct 覆盖）是直接的破坏性变更，重罚项。
- **保持 VB-like（原则 #2）**：方向"一致"——本建议想保留的正是 VB 最老的内置字符串比较语法糖；但任何实现都不可避免地改变既有构造的语义，张力集中在"保住语法糖 vs 保住既有语义"之间。
- **不引入"第二种做事方式"（原则 #3）**：A 与 C 都是第 N 种写法；B（扩展现有 `Option Compare`）是唯一"不新写"的路线，最 VB。
- **读起来像英语、对新手友好（原则 #5）**：`If name = "admin"` 是全语言最英语的表达式之一。**这是本建议全部价值所在，也是我们最同情它的一点**——我们如实记录这份同情，但它敌不过破坏面与全局状态。
- **避免隐蔽语义变化（原则 #7）**：**负分，重罚项。** 全局排序规则 + culture 依赖机器 locale = 同一源码在不同机器上行为不同。这与 `Return?` 被拒的核心理由同构。
- **消除常见样板（原则 #9）**：部分命中，但被显式比较器 workaround 覆盖 85%。
- **冗长只在有用时是美德（原则 #10）**：`StringComparison.OrdinalIgnoreCase` 是"有用的冗长"——它把排序规则显式化、局部化、可审计。这与我们上次在 target-typed 会议上对"显式转换目标类型"的结论同构：**显式比较参数是有用的冗长。**
- **与主线关系（对照表 2.3）**：#193（Option Compare Ordinal）挂起未解决、#218（表达式树 CompareString 重写）搁置、#117/#255（Option Compare 局部化）低价值。Anthony 18.9 落在主线长期关注但估值偏低的领域，方向与主线关切一致、节奏与估值偏离——**Anthony 独立延伸（延续型）**，无主线牵引力。

### RESOLUTION:

1. **保持 inactive（Table），不激活全貌。** 理由叠加：① 建议没有任何语法候选——无文法、无 spec、无可编译示例；② "全局排序规则"机制与主线价值判断相悖（#117/#255 对 `Option Compare` 局部化 "Not seeing high value, could create confusing code"）；③ B 的扩展部分是破坏性变更（null 修正、Distinct 覆盖）；④ 无普遍性数据，#193 挂起多年无牵引。
2. **PROPOSAL A（局部比较上下文）= 方向 Reject，不进设计队列。** 主线已对"局部化 `Option Compare`"表态低价值且易混淆；另立比较器作用域机制的成本与风险远超其残余价值。除非未来出现"值不值得"的新数据（见激活信号），本路线不再审议。
3. **PROPOSAL B 的 `Distinct` 覆盖部分 = Reject。** BCL 默认 ordinal、LINQ 查询比较责任在 providers（2017-12-06 "LINQ providers should fix their stuff"）、显式 `Distinct(StringComparer.OrdinalIgnoreCase)` 已可用、扩展即破坏。
4. **PROPOSAL B 的 null 处理 = 分拆为独立的小议题，进 runtime-library 工作项，但默认不做。** 任何修正都是破坏性变更；前置条件是"现有程序因 null 处理而出错"的可量化数据。没有数据，修正维持"记录在案、不修"。
5. **String/JSON 模式匹配的比较语义 = 划给模式匹配工作项。** 字面量前缀、JSON 属性名匹配的默认比较与显式 opt-in 由模式匹配设计时定义；本建议不在模式匹配之外另立比较机制。
6. **PROPOSAL C（显式比较器进构造）v1 不做，但值得记录**进"比较写法全景"备忘（与 postfix-casting 委派的"转换写法全景"同级）；`Case ... , StringComparison` 的文法歧义未解前不进入设计。
7. **一致性义务（复活前置）**：任何未来复活都必须保证——严格/宽松两条路径、表达式树与 LINQ、编译期常量折叠与运行期行为逐字一致；新机制只作用于 `String`，不误伤 `Char`/枚举/用户类型。

**激活所需信号（绑定，至少其一）：**
- (a) 出现一个**能编译**、与现有 .NET 比较 API 集成的最小语法草案（今天不存在）；
- (b) 主线 #193 或同类在主线/ModVB 获得明确设计牵引；
- (c) 数据证明"为不敏感比较放弃语法糖"是高频痛点（对标 Implicit-default-optional 的 85% 分水岭）；
- (d) VBScript.NET 迁移研究确认 VBScript 需要默认大小写不敏感的字符串比较语义（见 OPEN QUESTIONS——这是我们留给项目的脚本兼容信号）。

### Implication:

- 更新建议状态标注为 **LDM Considering（Table，保持 inactive）**；PROPOSAL A 与 B 的 `Distinct` 部分方向 Reject。
- 建立"比较写法全景"备忘：收录 `Option Compare Text` 现状、null 处理行为记录、#193/#218/#117/#255 先例、`Distinct` 与 LINQ 的分工、`Case` 携带比较器的文法歧义。任何新的字符串比较语法提案都先读这份备忘。
- 给模式匹配工作项（`meeting-string-pattern-matching`、`meeting-json-pattern-matching`）回执：字面量/属性名比较默认 ordinal，不敏感比较走显式模式选项；不依赖文件级 `Option Compare`。
- 给 runtime-library 工作项投递一个 `OPEN QUESTIONS`：核实 `Option Compare Text` 的 null 处理精确行为（运行期验证矩阵），作为"是否值得修"的数据基础。
- 核实 VBScript 字符串比较默认语义（大小写），作为激活信号 (d) 的证据。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Option Compare Text` 的 null 处理精确差异——`Nothing`/空串 × Binary/Text × `=`/`<>`/`<`/`>` 的完整行为矩阵，需运行期验证（`Suspect`：差异真实存在但具体语义待测）。
- `OPEN QUESTIONS`：VBScript 字符串比较的默认语义（大小写）——这是本建议对 VBScript.NET 是否有迁移价值的**决定性信号**，项目团队需核实。
- `OPEN QUESTIONS`：若未来考虑 C，`Case "admin", StringComparison.OrdinalIgnoreCase` 与多分支值列表的文法消歧是否有可接受解（如比较器只能是末位实参、且必须有 `StringComparison`/`StringComparer` 类型锚定）。
- `TODO`：修正建议中的事实错误——原文写 `ComparisonType`，.NET 实际枚举是 `StringComparison`（`ComparisonType` 不存在）。`Probably` 作者笔误，但作为规范材料必须修正。
- `TODO`：量化"为不敏感比较放弃语法糖"的真实占比，为激活信号 (c) 补证据。
- `TODO`：把 null 处理运行期验证矩阵补进"比较写法全景"备忘。
- `Follow-up`：跟踪主线 #193（Option Compare Ordinal）与 #218（表达式树 CompareString 重写）的状态；若主线对任一出现决议，重审本建议。
- `Follow-up`：与 `meeting-null-equality-operators` / `meeting-null-safe-behaviors` 对齐"Nothing 如何参与比较"的设计面。

### 状态

- **LDM 状态：建议整体 LDM Considering（Table，保持 inactive）**；PROPOSAL A 与 B 的 `Distinct` 部分方向 Reject；null 处理与模式匹配比较语义分拆移交对应工作项。
- **三态判定：Table** — 价值真实但窄、无语法、全局机制与主线价值判断相悖、扩展即破坏。值得记住（"比较写法全景"备忘收录），不值得现在做。

---

## 附录：特性评价

# 建议评价报告：proposal-case-insensitivity.md

## 评价对象

- 建议：proposal-case-insensitivity.md — 大小写不敏感 / 排序规则（collation）与 VB 内置字符串比较构造的集成
- 来源：Anthony 原文第 18 章 18.9 "More elegant integration of case-insensitivity/collation"（`..\..\AnthonyDesign_wordpress.txt` L2954–2982；"a hallmark of BASIC since time immemorial"、"unexpected null handling"、`<CallerOptionCompareSettingAttribute>`、`Distinct` 缺口、"it needs more consideration" 逐字出自该节）
- 配方目标：让 VB 内置字符串比较构造（`=`/`<>`/`<`/`>`/`<=`/`>=`、`Select Case`）声明或感知大小写不敏感 / 排序规则，并统一接入现有 .NET 比较 API，避免"为不敏感比较放弃语法糖、退回 `Equals` + 比较器"

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："示例不能演示改进"。Motivation 并不含糊（准确列出 Option Compare Text 三重缺口），但**没有任何语法候选**——核心效果（构造感知不敏感）未显现，示例只能演示"现状的苦"（`Equals` + 比较器样板）而非本特性；无原型无运行。未决问题 ≥4 个关键设计点 → 效果证据等级封顶，2 分已属宽容 | 已提供/已检查（状态行 Prototype/Implementation/Specification 全为占位链接） | 目标改进不可演示；`ComparisonType` 事实错误削弱可信度；痛点普遍性零数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造"（此处为"继承 VB6 遗产但未提出任何形态"）。方向延续 VB 最老基因（保留内置字符串比较语法糖），继承 VB6 `Option Compare` 遗产——这是本建议最亮的一点；但机制（全局 collation）制造隐蔽语义变化（原则 #7 负分）、culture 依赖机器 locale，且与 .NET 显式比较 API 的整合形态完全未 VB 化（无形态可言） | 已检查 | 无任何可评审的语法形态；"全局排序规则"是与"避免隐蔽语义变化"直接冲突的机制；类型边界（不误伤 Char/枚举）未提 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、Drawbacks/Alternatives/Unresolved 诚实（4 个未决问题具体，健康区间）；但 Detailed design 整体缺席——无文法、无 spec、无角案例，只是一份现状清点；有事实错误（`ComparisonType`）；状态行占位链接；Compatibility/breaking-change 分析不存在 | 已检查 | 核心章节（Detailed design）空置至"无内容"而非"边界含糊"；`ComparisonType` 事实错误；未参与任何主线先例 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。预测待定。风（演化一致性）受损——与主线 #117/#255（Option Compare 局部化低价值）、#193 挂起状态判断冲突；暗风险突出——全局状态、culture 依赖机器、破坏性 null 修正、重编译换行为（Distinct 覆盖）；唯一正向是水/光潜力（盘活 VB 内置比较资产、脚本迁移价值），但被不确定性完全抵消且文档未权衡 | 已检查（预测待定，实际影响须"已采纳"后定） | 全局 collation 的隐蔽语义变化无对冲设计；破坏性扩展无 langversion 门控/警告策略；文档未讨论任何暗风险对策 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源自标准确（Anthony 18.9，自标 "needs more consideration"，实验性诚实）；准确识别 `Option Compare Text` 与 `<CallerOptionCompareSettingAttribute>`（继承 VB6/既有运行时，标注正确）；但**未参与**主线先例——#193（Option Compare Ordinal）、#218（表达式树 CompareString 重写）、#117/#255（Option Compare 局部化）全部缺席；继承 VB6 `Option Compare` 遗产未显式标注；`ComparisonType` 事实错误构成"标注与影响有偏差" | 已检查 | 与主线同领域三个先例（#193/#218/#117/#255）零关联，材料成分缺失；`ComparisonType` 误标削弱规范可信度 |

## 设计原则对照

- **与 VB 基因：部分一致、部分冲突。** 一致面：保留 BASIC 内置字符串比较语法糖（原则 #2/#5 的方向）、B 路线不引入第二种做事方式（原则 #3）。冲突面：全局排序规则 + culture 依赖机器 locale = 隐蔽语义变化（原则 #7，重罚项）；修正 null / 扩展 Distinct = 破坏性变更（原则 #1）；显式比较参数是"有用的冗长"（原则 #10）——直接反驳"消除样板"的价值主张，与上次 target-typed 会议对显式转换目标的结论同构。
- **与主线关系：Anthony 独立延伸（延续型）**。方向与主线关切一致（主线也想更好地处理字符串比较：#193、#218），但主线对该领域的估值偏低（#117/#255 "Option Compare: Not seeing high value, could create confusing code"）；Anthony 未提出任何主线推动的形态。无 C# 对应物（C# 无内置字符串比较构造，比较永远显式走 `StringComparison`——本建议恰好想取消 C# 那种显式性，方向与"默认跟随 C#"相背且未给理由）。
- **破坏性变更：潜在有**——null 处理修正、Distinct 扩展均会改变既有程序行为（重编译换行为）；建议未分析、未给 langversion 门控或警告策略。新语法路线（A/C）基干为加法，但全局设置改变语义使"加法"名不副实。

## 总评

- **达成程度：未达成（作为可落地特性）/ 部分达成（作为记录在案的实验性想法）。** 作为"Needs more consideration"的存档，它诚实、完整地清点了现状与缺口，是合格的调查起点；作为语言特性，无语法、无数据、机制与主线价值判断相悖，效果/属性两维不达标。
- **LDM 三态建议：Table（保持 inactive）**；A（局部比较上下文）方向 Reject、B 的 `Distinct` 覆盖 Reject、null 处理与模式匹配比较语义分拆移交对应工作项。面向 VBScript.NET 的优先级**同样为 Table，但绑定一条可升舱信号**：若 VBScript 迁移研究确认需要默认大小写不敏感的字符串比较（OPEN QUESTION），本建议从"语言美学"升格为"脚本兼容刚需"，届时按激活信号 (d) 重审。
- **主要问题**：① 无任何语法候选，核心效果不可演示；② "全局排序规则"是隐蔽语义变化与破坏性变更的机制载体，与主线估值相悖；③ `ComparisonType` 事实错误；④ 与主线 #193/#218/#117/#255 同领域先例零关联；⑤ 无普遍性数据、无 Compatibility 分析。

## 返工建议

- **补充章节**：一份**可编译的最小语法草案**（哪怕只覆盖 `Select Case` 或运算符一个位置）与其文法（BNF）——这是本建议从"现状清点"升级为"设计草案"的唯一关键缺口；Compatibility/breaking-change（null 修正与 Distinct 扩展的破坏面、重编译换行为、表达式树与 LINQ 分叉、Option Strict 两条路径）。
- **补充证据**：运行期验证 `Option Compare Text` 的 null 处理行为矩阵（`Nothing`/空串 × Binary/Text × 各比较运算符）；量化"为不敏感比较放弃语法糖"的真实占比；VBScript 字符串比较默认语义的核实结果。
- **修正**：`ComparisonType` → `StringComparison`。
- **未决问题处理**：null 处理移交 runtime-library 工作项（默认不修，除非数据证明现有程序因此出错）；`Distinct` 覆盖关闭（BCL 默认 ordinal + 显式 comparer 已可用）；模式匹配比较语义移交模式匹配工作项（默认 ordinal）；`Case` 携带比较器的文法消歧列为 OPEN QUESTIONS。
- **设计探索**：若未来复活，优先探索"默认 ordinal + 显式局部 opt-in"而非"全局 collation"——例如 String/JSON 模式里显式的 `Case $"/echo {message}"` 不敏感变体；并参与主线 #193/#218，避免与表达式树 CompareString 重写工作各做一套。

---

## 附录：C# 生态与互操作考量

> 本附录对照 `..\..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 C# interop 浓缩索引）评估本提案（大小写不敏感与排序规则集成）与 C#/CLR/.NET 生态的互操作关系。核心结论：**C# 没有任何语言级"大小写/排序规则"机制，其 LDM 还明确拒绝过大小写不敏感路线；本提案的"全局 collation"与 C# 显式化、默认 ordinal 的方向相反（机制脱节）。但"C# 标识符大小写敏感 vs VB 大小写不敏感"是两语言的根本差异，构成互操作面必须桥接的摩擦点——且与本提案是否激活无关。**

### 相关 C# 现实方向

1. **C# 是大小写敏感语言，LDM 明确否决过"大小写不敏感"的念头。** 2020-10-21 LDM 讨论 primary constructor 的参数名与属性名匹配时，曾考虑大小写不敏感匹配后放弃，逐字记录为："We briefly entertained the idea of making parameter names match in a case-insensitive manner, but quickly backed away from this as case matters in C#, working with casing in a culture-sensitive way is a particularly hard challenge, and wouldn't solve all cases (for example, if a parameter name is shortened compared to the property)."（→ `meetings\2020\LDM-2020-10-21.md`）——注意其中 "culture-sensitive way" 与本提案"culture 依赖机器 locale"的顾虑同款；C# 连参数名匹配都拒绝 culture 化。
2. **C# 在共享特性上把"C# 敏感 / VB 不敏感"作为既定差异写进设计。** 元组元素名推断（C# 7.1）提案正文逐字记录："Rejecting reserved tuple names (case-sensitive in C#, case-insensitive in VB), as they are either forbidden or already implicit. For instance, such as `ItemN`, `Rest`, and `ToString`."；"If any candidate names are duplicates (case-sensitive in C#, case-insensitive in VB) within the entire tuple, we drop those candidates,"；"The same would also apply to VB tuples, using the VB-specific rules for inferring name from expression and case-insensitive name comparisons."（→ `proposals\csharp-7.1\infer-tuple-names.md`）——C# 团队的处理方式是把 VB 大小写不敏感当作 VB 专属规则、由 VB 侧适配，而非 C# 采纳。
3. **C# 字符串比较永远显式；生态方向是"默认 ordinal + 显式参数"。** C# 无 `Option Compare` 等价物；`==`/`string.Equals` 走 ordinal 区分大小写；BCL 默认比较（`EqualityComparer(Of String).Default`）同为 ordinal 区分大小写（本会议 Q&A 已确认）。生态还在推进"显式默认"：overload-resolution-priority 提案举例 "Making `string.IndexOf(string, StringComparison = Ordinal)` preferred over `string.IndexOf(string)`. This would have to be discussed as a potential breaking change, but there is some thought that it is the better default, and more likely to be what the user intended."（→ `proposals\csharp-13.0\overload-resolution-priority.md`）。
4. **C# 不为 VB 设计语言机制。** unsafe-evolution 提案对 VB 章节明言："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."（→ `proposals\unsafe-evolution.md`「VB」小节）——C# LDT 的设计义务止于 C# 自身；VB 侧的大小写/比较语义既不会被承载、也不会被跟随。
5. **C# 词法层存在极窄的"大小写不敏感"例外。** `u8` 后缀："The suffix is case-insensitive, `U8` suffix will be supported and will have the same meaning as `u8` suffix."（→ `proposals\csharp-11.0\utf8-string-literals.md`）——说明 C# 的敏感/不敏感是分面的：无标识符身份的词法元素（后缀）可放宽，**标识符与默认比较保持敏感**。

### 现实 vs 提案

| 轴 | C#/.NET 现实 | 本提案 | 判定 | 理由 |
|---|---|---|---|---|
| 比较机制 | 无语言级设置；显式 `StringComparison`/`StringComparer` | 全局/局部排序规则 + 构造感知 | **脱节** | C# 无对等物，方向相反（显式 > 全局设置）；C# 不会推动、不会跟随，生态无共振。 |
| 默认值 | BCL/C# 默认 ordinal（大小写敏感） | `Option Compare Text` 默认 culture 文本排序 | **冲突** | VBScript.NET 若默认大小写不敏感，与 .NET 生态默认主动背离；"默认安全"要求默认值与生态一致、不敏感走显式 opt-in。 |
| 标识符 | C# 元数据标识符大小写敏感 | VB/VBScript 大小写不敏感 | **需桥接** | 跨语言调用 C# 程序集时，大小写不敏感绑定须有确定性规则（精确匹配优先、大小写仅异冲突报错）；这是 binder/元数据层硬义务，独立于本提案。 |
| 表达式树/LINQ | C# 表达式树无比较设置概念 | 比较设置须流经表达式树/LINQ（#218 未解） | **需桥接** | C# 无模型可复制；VBScript.NET 须自行决定比较设置在表达式树上的表达，否则 LINQ-to-Objects 与 LINQ-to-Providers 分叉（本会议已述）。 |
| culture 依赖 | culture 比较依赖机器 locale（C# 亦同，但无语言级暴露） | 全局 collation 隐含 locale 依赖 | **同受其限** | 非 CLR 缺陷，是设计不可控性；ordinal 跨机器可复现，culture 不可。 |
| 晚绑定/动态 | C# `dynamic` 无比较设置概念，且 dynamic/表达式树在 C# 已边缘化（索引 T7） | `Option Compare Text` 经 `` `<CallerOptionCompareSettingAttribute>` `` 注入比较设置 | **脱节** | C# 动态路径连"比较设置"都没有；VBScript.NET 若做宽松模式脚本，比较语义完全自己定义，无生态参照。 |

补充：**PROPOSAL C 是唯一与 C# 显式比较器方向同向的路线**——把 `StringComparison`/`StringComparer` 显式写进构造，与 .NET API 集成形态一致、可互操作；但它的障碍（`Case "admin", StringComparison.OrdinalIgnoreCase` 与多分支值列表的文法歧义）是 **VB 侧文法问题，C# 生态方向救不了它**。这解释了为何 RESOLUTION 6 对它"记录但不做"。

### 对 VBScript.NET 的适应建议

1. **默认安全：默认 ordinal、与 .NET 生态一致。** VBScript.NET 的字符串比较默认应为 ordinal 区分大小写（对齐 `==`/`EqualityComparer(Of String).Default`）；只有当 VBScript 迁移研究（激活信号 (d)）证明需要"默认大小写不敏感"时，才把它做成**显式、作用域化**的脚本兼容 opt-in，而非全局隐蔽状态。C# 生态的 ordinal 默认方向提高了 (d) 的门槛——(d) 必须由迁移数据支撑，不能以"跟随 C#"为理由。
2. **比较语义走"编译期显式参数 / source-gen 桥"。** 若未来提供比较设置机制，应编译为显式 `StringComparison`/`StringComparer` 实参（对齐索引 T6：编译期生成替代运行时动态），而非 `` `<CallerOptionCompareSettingAttribute>` `` 式运行时全局注入——后者正是 #218 表达式树重写坑（back compat、LINQ providers）的源头。编译期显式化可静态推理，对 AOT/trimming 友好。
3. **识别新元数据 / 确定绑定规则。** 调用 C# 程序集时须识别：① `StringComparison`/`StringComparer` 重载参数（显式传入，避免命中 BCL ordinal 默认）；② 大小写仅异的标识符（如 `csharp` vs `CSharp`）——按精确匹配优先、大小写仅异冲突报错的确定性规则绑定。这是决策文件 M8"识别新元数据"义务在本提案轴上的具体化。
4. **跨语言边界显式化比较语义。** 脚本代码与托管库混编时，同一比较可能一侧走 VB 语义、一侧走 BCL ordinal；VBScript.NET 应在调用边界把脚本侧设置翻译为调用侧 `StringComparison` 实参，避免"同一逻辑两套语义"的隐蔽分叉。

### 对既有 RESOLUTION/三态判定的影响

- **三态不变（Table），C# 现实反而强化 RESOLUTION。** C# 无语言级比较机制、LDM-2020-10-21 明确拒绝大小写不敏感、方向是显式 ordinal——本提案的"全局 collation"在 C#/CLR/.NET 生态**无牵引力、无对等物**，为 RESOLUTION 1 的否决再添一票。RESOLUTION 1–7 无需修改。
- **RESOLUTION 3（B 的 `Distinct` 覆盖 = Reject）被 C# 生态方向进一步支持。** .NET 为 `Distinct`/`Contains` 等提供显式 `StringComparer` 重载——生态答案就是"用显式比较器"，而非改语言级默认。
- **RESOLUTION 6（C 记录不做）不受影响。** C 虽与 C# 显式比较器方向同向，但障碍在 VB 侧文法歧义；C# 生态方向既不构成复活理由、也不构成额外否决。
- **激活信号 (d) 保持唯一升舱路径，门槛提高。** VBScript.NET 默认大小写不敏感是与 .NET 默认的主动背离，须由 VBScript 迁移数据证明，而非"跟随 C#"。
- **新增一条 VBScript.NET 硬义务（RESOLUTION 未覆盖）。** 标识符大小写绑定桥与比较默认的显式声明属 binder/元数据层，**与本提案是否激活无关、必须做**。建议补进"比较写法全景"备忘（RESOLUTION Implication 已建立），新增"跨语言绑定规则"一节。
- **模式匹配移交结论不受影响。** 模式匹配工作项"默认 ordinal"与 C# 生态 ordinal 默认一致，无冲突。

### 引用出处（逐字，已核实）

- "We briefly entertained the idea of making parameter names match in a case-insensitive manner, but quickly backed away from this as case matters in C#, working with casing in a culture-sensitive way is a particularly hard challenge, and wouldn't solve all cases (for example, if a parameter name is shortened compared to the property)." → `meetings\2020\LDM-2020-10-21.md`
- "Rejecting reserved tuple names (case-sensitive in C#, case-insensitive in VB), as they are either forbidden or already implicit." / "If any candidate names are duplicates (case-sensitive in C#, case-insensitive in VB) within the entire tuple, we drop those candidates," / "The same would also apply to VB tuples, using the VB-specific rules for inferring name from expression and case-insensitive name comparisons." → `proposals\csharp-7.1\infer-tuple-names.md`
- "The suffix is case-insensitive, `U8` suffix will be supported and will have the same meaning as `u8` suffix." → `proposals\csharp-11.0\utf8-string-literals.md`
- "Making `string.IndexOf(string, StringComparison = Ordinal)` preferred over `string.IndexOf(string)`. This would have to be discussed as a potential breaking change, but there is some thought that it is the better default, and more likely to be what the user intended." → `proposals\csharp-13.0\overload-resolution-priority.md`
- "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（「VB」小节）

**OPEN QUESTIONS**：
- CLR 元数据对大小写仅异标识符（如 `Foo`/`FOO`）能否共存及其绑定优先级——本附录基于 C# 提案文档的 "case-sensitive in C#" 表述，未深挖 ECMA-335 原文（`Suspect`）。
- 表达式树/查询提供者上表达"比较设置"的既有 .NET 实践——C# 无模型，BCL 是否有等价物待核实。
