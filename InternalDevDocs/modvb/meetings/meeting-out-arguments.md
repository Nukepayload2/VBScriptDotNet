# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这是组 9 的第一条——`Out` 实参与形参。它与组 6 的 `Return` 赋值建议（`proposal-return-byref.md`）是同一张皮的两面：那边的所有示例都写着 `Return True, root:=1`，而这边要为 `root` 提供承载语法。因为上一条会议已经把 `Return` 赋值的求值顺序、copy-in/copy-out、括号分歧都定过一轮，本次讨论可以站在那个结论上，把火力集中在 `Out` 自己身上：调用点修饰符、形参侧修饰符、隐式声明的作用域，以及它与 2014 年以来主线 Out 参数讨论的关系。

## Agenda

* [Proposal: Out 实参与形参](#proposal-out-实参与形参)

## Proposal: Out 实参与形参

_Related: [vblang #42 – Out parameters and implicit Out parameter declaration](https://github.com/dotnet/vblang/issues/42)（2014-02-17，原则批准）；[vblang #34 – Declaration expressions](https://github.com/dotnet/vblang/issues/34)（2014-02-17，否决）；[vblang #152 – Out arguments](https://github.com/dotnet/vblang/issues/152)（2017-08-23）；[vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)（2018-05-30，Out 部分 LDM Reviewed: No Plans）；[vblang #60 – Out variables](https://github.com/dotnet/vblang/issues/60)；[vblang #124 – Pattern matching](https://github.com/dotnet/vblang/issues/124)；[vblang #167 – Return?](https://github.com/dotnet/vblang/issues/167)（否决）；ModVB：`proposal-return-byref.md`（组 6）、`proposal-shapeof-pattern-matching.md`（组 7）、`proposal-user-defined-pattern-methods.md`（组 7）、`proposal-nullability-flow-analysis.md`（组 1）、`proposal-intersection-union-types.md`（组 16）_

### 场景与缺口

We started from the most ordinary pattern in the BCL：`Try*` 家族。`Integer.TryParse`、`Dictionary(Of TKey, TValue).TryGetValue`，每一个 VB 项目里都有。今天的写法要求先把变量声明好，再传进去：

```vb
Dim value As String = Nothing
If cache.TryGetValue(name, value) Then
    Return value
End If
```

We see a clear DX parity gap here，而且它有两层。第一层是**样板**：为了接住输出，必须先声明一个可能用不上的变量。第二层更扎心——若把声明写成 `Dim value As String`（不初始化），`Return value` 会触发"变量在赋值前被使用"的 definite-assignment 警告；于是作者被迫用 `= Nothing` 这种"我其实没想让它是 Nothing"的写法把警告摁掉。主线 2018 年把 Out 变量划进模式匹配后标了 No Plans（#305，"Out variables are probably covered better in #60 and pattern matching by #124"），C# 却在 C# 7 拿到了 `out var`。这个落差不是 C# 的过错，是我们欠的债。

第二块缺口是**惯用多返回值**。VB 的表达从来只有三条路，2014 年我们就点名过这三条路都不体面（2014-04-23）：`out` 参数（"Ugly 1"）、元组（"Ugly 2"）、自定义类型（"Ugly 3"）。元组改签名形状，`Try*` 形态根本不适用；自定义类型为一次调用写一个类，仪式感盖过价值。

第三块缺口最要害：**模式方法（第 7 章）天然要求"成功判定 + 值提取"一次交付**。`Function IsPerfectSquare(number As Integer, Out root As Integer) As Boolean` 这种签名，是 `proposal-user-defined-pattern-methods.md` 和 `proposal-shapeof-pattern-matching.md` 的结构件——没有 `Out` 形参，那些 `ShapeOf x Is hasAnotherChar(c)` 的匹配就无从提取值。这条线从 2014-04-23 就画下来了："Once we add Out expressions to VB, you can get more pattern-matchey"。主线把 Out 并进模式匹配的直觉是对的，只是它停在了 No Plans；我们这次把它完整做出来。

### 候选方案

**PROPOSAL A — 完整版（建议原文）。** 四个表面同时落地：调用点 `Out value`（隐式声明，`If cache.TryGetValue(name, Out value) Then`）；形参侧 `Out root As Integer`（只写形参）；`Return True, root:=1` 命名赋值（由 return-byref 承载）；显式 `ByRef` 实参语法（`ReportErrors(ByRef diagnostics)`）。

**PROPOSAL B — 只做调用点 `Out value`。** 隐式声明是 Try* 样板与警告的主要来源；形参侧 `Out` 只是"多返回值"的一种表达，元组与模式方法可以部分替代。B 只解决调用侧，不碰声明侧。它的好处是面最小，且调用 BCL 里已有的 C# `out` 方法（元数据带 OutAttribute）不需要 VB 自己的声明侧语法。

**PROPOSAL C — 只做形参侧 `Out`（declaration-site），调用点不加关键字。** 这最贴近 2017-08-23 的方向："Recognize the OutAttribute, require no In. Don't add an Out argument modifier. Out parameter separately."——识别元数据里的 OutAttribute、不要求调用点写修饰符、形参侧单独做。调用点继续沿用今天的无关键字 `ByRef` 匹配（2014 #42 的 back-compat 规则：匹配 out 形参本就不需要关键字）。

**PROPOSAL D — 只做显式 `ByRef` 实参语法。** 用 `ReportErrors(ByRef diagnostics)` 表达"这个实参是按引用传的"，换取清晰性与严格检查（要求 true lvalue）。

**PROPOSAL E — 什么都不做 / 元组兜底。** 维持现状；多返回值交给元组（2016 已落地）或模式方法自己的提取语法。

### 权衡：Q&A

- **A vs B：形参侧 `Out` 值得吗？** 值得，但不是为了"多返回值"这个泛泛的理由，而是因为它同时服务三件事：(1) 2014 #42 主线原则批准过它（declaration-site `Out x As Integer`）；(2) 模式方法没有它就不成立（`Out root As Integer` + `Return True, root:=1` 是 `user-defined-pattern-methods` 的签名骨架）；(3) return-byref 的 `Return name:=expr` 需要一个只写形参作为赋值目标——若只有调用点 `Out`，那条建议就失去了另一半。B 的"调用点只配 BCL 现成 out 方法"是真实场景，但那是 C 的覆盖范围，不是 B 独有的。**结论：B 不构成独立方案，它的调用点部分并入 A，声明侧由 C 论证。**
- **A vs C：调用点修饰符到底要不要？** 这是整场最尖锐的分歧，因为 2017-08-23 白纸黑字写了 "Don't add an Out argument modifier"。但请把它读完整：同一行还有 "Recognize the OutAttribute" 与 "Out parameter separately"。我们把这条极简笔记读作"**不要加一个强制性的实参修饰符**"——匹配 out 形参靠元数据识别、靠既有 ByRef 规则，不要求作者写关键字。这与 2014 #42 的 back-compat 规则完全一致："you don't need an 'Out/Output' keyword to match an out parameter. However, you do need the keyword in order to declare the variable inline"。**换言之，调用点关键字只在一个时候是必需的：你想让编译器在调用处隐式声明一个新变量。** 既存变量当实参时不加关键字照常匹配。我们采这个读法：A 的 `Out value` 不是"新增一个实参修饰符"，而是 2014 #42 早就预留的"隐式声明开关"。`Probably`——2017 那行字太电报体，我们 mark 为 `Suspect`，留 OPEN QUESTION 让社区确认。
- **A vs D：显式 `ByRef` 实参单独做吗？** 不做。理由两条。其一，它是"第二种做事方式"（原则 #3）——今天传 ByRef 实参无需任何标记，`ReportErrors(ByRef diagnostics)` 与 `ReportErrors(diagnostics)` 是同一件事的两种写法，多出的只有一次 lvalue 检查；这个检查确实有价值（后面第 2 条追问会展开），但价值密度配不上一个新语法表面。其二，它与 2017-08-23 的 "Don't add an Out argument modifier" 正面相撞——那边拒绝的正是这种"调用点修饰符"。`Out value` 之所以例外，是因为它有隐式声明这个**今天做不了**的事；而 `ByRef` 标记什么新事都不做。**结论：D 并入 A 作为可选项（Table），不独立立项。** 但 D 的"严格 lvalue 检查"想法吸收进 `Out` 修饰符——加关键字即要求 true lvalue（2014 #42："if you use the keyword then it is an error to pass it something that's not an lvalue"）。
- **A vs E：为什么不是元组 / 什么都不做？** 2014-04-23 我们就把 out 参数、元组、自定义类型并列为三条"丑陋路径"，没有任何一条是优雅的；今天的选择题没有变。元组改签名、调用方要解构，`TryGetValue` 的"失败时值仍确定"形态元组表达不了——`TryGetValue` 返回的是 `Boolean`，值在 out 里，这是它的 API 形状。我们不打算为了不做 Out 而把 BCL 重写一遍。**E 不构成替代，A 是增量。**
- **最大的反对：`Out value` 的隐式声明会让变量的作用域"漏"出去。** 这是 2014 #42 自己的担心（"Is this synergy with 'For' a step too far? Given that the scope of the out in statements will bleed outside. Shouldn't we help people avoid the dangerous situation?"）。For/ForEach 的循环变量今天就是隐式声明的，`Out` 在机制上与之同族——但 For 变量的作用域被圈在循环块里，而 `Out` 的变量在 `If` 之后还活着。我们的回应分两半：作用域收敛到**最近的外层块**（见追问 3），把"漏"限定在一个可预期的范围；而且"漏"本身有时正是想要的——2014 #34 讨论隐式声明时就写过 `Integer.TryParse("15", Out x) : Console.WriteLine(x) ' REALLY DO WANT x to escape scope`。**We like the bleed, bounded.**
- **为什么这次不等主线？** 主线 2018 年把 Out 标了 No Plans、并进模式匹配（#305/#60/#124），但此后没有任何产出。我们与主线的分歧不是方向而是节奏：主线把它当模式匹配的附属品，我们把它当模式方法的地基——地基不先打，上面什么都盖不起来。关系照 2.3 表是"一致"，只是我们走完整。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`Out` 不是保留字。** 这是最关键的事实。`Dim Out As Integer` 今天合法，`M(Out)`、`M(Out.value)` 今天都合法且各有含义。所以 `Out` 作为实参修饰符必须是**上下文相关**的。我们拟定规则：

```vb
Dim Out As Integer = 42
M(Out)          ' 变量 Out 作实参 —— 今天合法，保持合法
M(Out.value)    ' 成员访问 Out.value —— 今天合法，保持合法（Out 后跟 '.'，不是标识符开头）
M(Out value)    ' 今天：语法错误（两个实参之间没有逗号）
                ' 新语法：Out 修饰符 + 实参 value
M(Out Dim x As Integer)   ' 2014 #42 的完整形式（[Dim] x [As Integer]）
```

规则：**当 `Out` 出现在实参位置且下一个记号是一个标识符（或 `Dim`）时，把 `Out` 读作修饰符**；否则 `Out` 是普通标识符。这样 `M(Out)`、`M(Out.value)`、`M(Out + 1)`（`Out` 是变量参与运算）都不受影响。`M(Out GetValue())`——`GetValue()` 是方法调用不是标识符，按"`Out` 是变量"解析；若作用域里没有名为 `Out` 的变量，则报"变量 Out 未声明"，不当作修饰符错误——宁可给出误导性错误也不要抢走既存的变量解析。

**`Out value As Integer` 的 `As` 歧义。** 2014 #42 允许 `Out [Dim] x [As Integer]`。`Out value As Integer` 在实参位置必须与"命名实参"区分——VB 命名实参用 `:=`，所以 `As` 不会被误读为命名。但与 `Dim` 的互动：`Out Dim x As Integer` 里 `Dim` 是声明关键字，`Out value As Integer` 里 `As` 依附于 `Out` 的隐式声明。两条产生式都要写进文法，`As` 只允许跟在由 `Out` 引入的新名字后；既存 lvalue 的 `Out` 后面不许跟 `As`（没有"重新声明类型"这回事）。

**`Return True, root:=1` 的逗号与括号。** 这条在 return-byref 会议已定：`Return (True, root:=1)` 是具名元组字面量，`Return True, root:=1` 是本特性；文法以括号切分。本次不再展开，只记录：`Out` 形参使这个语法分歧从"理论风险"变成"日常必需"——`IsPerfectSquare` 的每一条 `Return True, root:=n` 都在用。

#### 2. 角案例与边界语义

**Definite assignment 与默认初始化。** 2014 #42 定了两条：进入方法时编译器默认初始化所有 Out 形参（"The declaration method will default-initialize all Out parameters on entry (this can be optimized away by the compiler)"）；对 use-before-assign 与 return-without-assign 都发警告。这两条合起来给调用点一个漂亮的保证：**调用返回后，`Out` 实参变量必然已赋值**（要么方法写的，要么默认初始化兜底）。所以 `If cache.TryGetValue(name, Out value) Then Return value` 里 `value` 在 Then 分支必然 definite-assigned，`Return value` 不再触发警告——这正是本特性要消除的那类警告。代价是"值可能是 `Nothing`"：TryGetValue 失败时 `value` 是默认值。这与 `proposal-nullability-flow-analysis` 在同一引擎上要达成一致：definite-assigned ≠ non-null，失败分支里 `value` 应被流分析标记为"可能为 Nothing"。

**`Return False, root:=Nothing` 与"每条 Return 全赋值"。** return-byref 会议定的规则：含 `Out` 形参的方法，每条 `Return` 必须对所有 `Out` 形参赋值，否则编译警告（承接 #42 的 return-without-assign）。本建议的 `IsPerfectSquare` 全部路径都写了 `root:=...`，包括失败路径 `Return False, root:=Nothing`。这条纪律比 2014-04-23 模式匹配的弱保证（"definitely assigned only if the 'e matches T(args)' returns true"）更严，但它换来调用点"总是已赋值"的简单性。**我们选更严的**；模式方法建议的"失败时不参与逻辑"改由流分析表达，不靠未赋值。

**`Optional Out`。** 建议的未决问题之一。我们的立场：**禁止**。`Out` 的语义是只写；`Optional Out x As Integer = 5` 的默认值 5 永远不会被读到，是一个谎言。且它与"每条 Return 全赋值"规则直接冲突（可选形参允许省略 → 省略时违反全赋值）。`Out` 与 `Optional` 互斥，报错"Out 参数不能是 Optional"。

**Property 作 `Out` 目标。** 今天把 property 传给 `ByRef` 形参是合法的，走 copy-in/copy-out（VB 著名的坑）。但对 `Out` 形参，copy-in 是浪费甚至有害——它读取一个我们承诺绝不读取的值。**规则：`Out` 实参必须是 true lvalue（变量、字段、数组元素）；property 目标直接报错。** 无关键字的 `ByRef` 匹配（今天的行为）保持 copy-in/copy-out 不变——这制造了"加不加关键字两条路径"的分叉，但 2014 #42 早就为这个分叉背书（"if you use the keyword then it is an error to pass it something that's not an lvalue"）。2016-05-06 ByRef Returns 讨论过 "how to reconcile VB's copy-in-copy out semantics for properties passed ByRef with returning properties ByRef"——我们在这里的答案很简单：`Out` 根本不让 property 进来，copy-in/copy-out 的分歧就不存在。

**值类型与泛型。** Out 形参可以是值类型（`Out root As Integer`），默认初始化即 `0`；`Return False, root:=Nothing` 对 `Integer` 是合法赋值（`Nothing` = default(T)）。泛型 Out（如 `TryGetValue(Of TKey, TValue)`）见追问 3。

**Async / Lambda。** `Async Function` 不允许 `ByRef` 形参（与 C# 一致），`Out` 形参同理被排除——所以"声明侧 Out"不进 Async 函数体。但**调用点** `Out value` 在 Async 函数里完全合法：`If Integer.TryParse(s, Out n) Then` 的目标方法是同步的，隐式声明的局部变量 `n` 是普通局部。Lambda 内不能捕获并按引用写外层 `Out` 形参（与 ByRef 捕获规则一致）；多行 lambda 自己的 `Out` 形参只能在 lambda 体内写回。

#### 3. 作用域与绑定

**隐式声明的变量作用域 = 最近的外层块。** 这是 2014 #42 的悬而未决问题（"What should the scope of 'x' be? C# LDM is also wrestling with this, and the answers are sometimes unexpected"）。我们定：`Out value` 引入的变量作用域是**包含该调用的最近语句块**（方法体、`If`/`Select` 块、循环体），与 C# out var、与 VB `For` 循环变量的块作用域一致。

```vb
If Integer.TryParse(input, Out number) Then
    Return number
Else
    Return 0
End If
' number 在 If 之后仍可见（最近外层块 = 方法体），且 definite-assigned。
Console.WriteLine(number)
```

这回答了建议的未决问题"失败分支中 value 是否可见、是否保证已赋值"：**可见，且已赋值**（默认初始化兜底），只是可能是 `Nothing`/默认值。作用域"漏"是刻意的、有限的。

**绑定规则：先绑名字，再决定"声明 or 传参"。** 语义模型里，`Out value` 处的名字解析分两步：(1) 若 `value` 已在作用域内且是 lvalue → 绑定到既有变量，`Out` 只作修饰符（要求 true lvalue）；(2) 若未绑定 → 编译器以被解析 Out 形参的类型隐式声明一个局部变量，标识符绑定到这个新局部符号。这与 VB `For` 循环变量的既有规则同构。`GetTypeInfo` 返回新局部的类型（= 形参类型）；`GetSymbolInfo` 返回新局部符号或既有变量符号。若 `value` 已绑定但**不是** lvalue（如 `Me`、方法调用结果）→ 报错。

**泛型 / 重载的推断。** `Out` 实参**不贡献类型推断的输入**——它只接收。`TryGetValue(Of TKey, TValue)(key As TKey, ByRef value As TValue)` 里 `TValue` 只能从其它实参推断；推断不出时允许 `Out value As Integer` 显式给出（2014 #42 的 `Out [Dim] x [As Integer]`）：

```vb
' 重载集里 TryGetValue 的 out 类型各不相同，歧义时用 As 消歧。
If multi.TryGetValue(name, Out value As String) Then
```

这同时回答了建议的 drawback"形参是泛型或重载，推断歧义可能增加"——解法是显式 `As` 类型，而不是禁止隐式声明。`Out value As T` 的 `As` 也约束重载解析（形参类型必须与声明的 `As` 类型可转换），使歧义重载可解析。

#### 4. 与既有特性的交互

- **return-byref（组 6）**：`Return True, root:=1` 是本特性的承载语法。两建议**同一份 spec、同一原型**——return-byref 会议已定求值顺序为"先求返回表达式，再从左到右赋值"，`IsPerfectSquare` 的常量赋值不受影响，但模式方法 `Return True, ch:=reader.PeekChar(0)` 必须遵守同一顺序。
- **模式匹配（ShapeOf / user-defined-pattern-methods / named-patterns，组 7）**：`Out` 形参是模式方法的签名骨架。2014-04-23 的 `Case e As PlusExpr When e.Matches(0, Out rhs)` 正是本特性的调用点形式在模式语境中的应用。三份建议共享同一套"Out 形参 + 布尔返回"的契约，`Optional Out` 的禁止也一并约束模式方法。
- **nullability-flow-analysis（组 1）**：Out 变量的 definite-assignment 由本特性保证，non-null 由流分析判定。两条引擎必须同源（与 TypeOf 会议决议一致）。
- **intersection-union-types（组 16）**：模式方法提取值时，Out 目标类型可能是一个交/并类型（如 `Out value As (IReadable And IWritable)`）。v1 不展开，仅保留接口；`Out value As T` 的类型语法天然允许任何 `As` 子句里出现的类型，包括未来的交并类型。
- **`Let`（proposal-local-declarations，组 3）**：`Let intRoot As Integer = Math.Sqrt(number)` 是显式局部声明，`Out value` 是隐式声明。两种声明机制并存是刻意的——一个给局部值，一个给输出槽。没有遮蔽冲突：`Out x` 绑定到既有 `x` 时是传参不是重新声明。
- **Late binding / Option Strict Off**：见追问 6。
- **互操作矩阵（2017-08-23："Investigate C# -> VB -> C# and C# -> VB -> VB"）**：VB 调用 C# `out` 方法 = 识别元数据 OutAttribute（2017-08-23 "Recognize the OutAttribute, require no In"）；C# 调用 VB `Out` 方法 = VB 发射 `<Out> ByRef`，C# 看到的就是 `out`。**New warning on a VB override**：当 VB override 把基类的 Out 形参改写成 ByVal、或改变 Out/ByRef 属性时发新警告——这是 2017-08-23 明确要求的新警告，写进 spec。

#### 5. Breaking change 与兼容性

**纯语法增量，但 `Out` 上下文关键字是唯一风险面。** 三个语法表面逐一核查：

- `M(Out value)`：今天 `Out value` 是两个实参无逗号 → 语法错误。新语法让它可解析。**从错误到合法，非破坏。**
- `M(Out)` / `M(Out + 1)` / `M(Out.value)`：今天合法（`Out` 是变量）。上下文规则保证 `Out` 后不跟标识符时维持变量解析。**行为不变。**
- `Function F(Out root As Integer)`：今天 `Out root` 两个标识符无逗号 → 语法错误。新语法合法。**非破坏。** `Function F(Out As Integer)`（形参名叫 Out）保持合法。
- `Return True, root:=1` / `ReportErrors(ByRef diagnostics)`：裸逗号列表与 `ByRef` 在实参位置今天都是语法错误。**非破坏。**

所以整个特性不改变任何既有代码的解析、绑定或运行行为——唯一要守护的是"`Out` 后跟标识符"这条上下文规则，不能比"下一个记号是标识符"更宽（否则 `M(Out value)` 若 `Out` 是变量就悄悄改变了语义）。**加 `langversion` 门控不需要**（零 break 的纯增量），但需要一个专门的兼容性测试矩阵（`Dim Out As Integer`、`M(Out)`、`M(Out.value)`、`M(Out + 1)`、`Function F(Out As Integer)` 逐条列证）。

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径必须行为一致。逐点核查：

- **隐式声明的类型**：`Out value` 的类型来自形参类型，与 `Option Infer`/`Strict` 无关（同 ForEach 变量）。`Option Infer Off` 下 `Out value` 不靠 `= 表达式` 推断，仍然可用——它是形参类型驱动，不是表达式推断。**两条路径行为一致。**
- **示例在 Strict On 下的可编译性（`Suspect`）**：建议原文的 `If cache.TryGetValue(name, Out value) Then Return value` 要编译，`cache` 必须是类型化字典（如 `Dictionary(Of String, String)`），使 `value` 推断为 `String`；若 `cache` 是 `Object`/松散字典，`value` 推断为 `Object`，Strict On 下 `Return value`（Object → String）报窄化错误，需要 CStr。**建议未声明 cache 的类型——这是文档精度问题。**
- **`Let intRoot As Integer = Math.Sqrt(number)`（`Suspect`，示例不编译）**：`Math.Sqrt` 返回 `Double`，`Double` → `Integer` 是窄化转换，**Option Strict On 下编译错误**（BC30512）。逻辑上这个例子的意图是"取平方根并截断到整数"，正确写法应是 `Let intRoot As Integer = CInt(Math.Round(Math.Sqrt(number)))` 或让 `intRoot` 推断为 `Double` 再比较 `(intRoot * intRoot) = number`。**我们要求修掉这个示例**——一条以"能编译"为卖点的建议，主示例在默认严格模式下必须能编译。
- **宽松路径**：`Out value` 目标形参是 `Object` 时，后续 `value.成员` 走晚期绑定，与既有宽松模式行为一致。严格路径下 `value` 是 `Object`，成员访问需强转。两条路径对"Out 只保证已赋值、不保证类型变窄"保持一致。

#### 7. IDE / IntelliSense

- **pretty-lister / 补全**：2014 #42 问过 "Should the pretty-lister insert 'Out' as you type a function invocation?"。我们答：是的——调用签名带 OutAttribute/Out 形参的方法时，补全列表在形参位置提供 `Out <name>` 候选；快速操作"内联声明输出变量"把 `M(value)` 改写成 `M(Out value)`。
- **`Return True, ` 之后补全 Out/ByRef 形参名**：复用命名实参补全与 `:=` 插入机制（return-byref 会议已定）。
- **InfoTip / 签名帮助**：Out 形参与 ByRef 形参在签名帮助里应可视化区分（"Out 参数"标记）；`Return True, root:=1` 的写回项 InfoTip 标"写回：参数 root"。
- **语义模型**：`Out value` 隐式声明的局部在 `GetSymbolInfo`/`GetTypeInfo` 里是普通局部符号，IDE 的"查找所有引用"、重命名、definite-assignment 检查器无需新机制。

#### 8. 数据 / 普遍性

`Try*` 是每个代码库都有的惯用法——这是本特性最强的普遍性证据。但量化数据缺位：没有"每千行代码多少 `Try*` 调用"、"隐式声明消除多少 definite-assignment 警告"的统计。`Suspect`：真实收益集中在 Try* 与模式方法两条线上，单次省 2–3 行；"数十万安静客户"对 Out 的刚性需求没有调查支撑。我们参考 2018-05-30 主线的自我怀疑（可空性"unclear how popular this feature will be"）——Out 比可空性收敛得多，但**普遍性论证仍停留在直觉**。TODO：从真实代码库采样量化。

#### 9. 更简替代

- **Analyzer / 代码修复**：可以建议"把预声明改写为内联"，但 analyzer 不能提供隐式声明的语义——`Dim value As String` 之后的 `Return value` 在 definite-assignment 层面就是需要一个初始化。analyzer 只能当脚手架。
- **元组**：改签名形状，`Try*` 不适用（见 Q&A）。
- **`ByRef` + 预声明**：现状。样板与警告正是本特性要消除的。
- **并入模式匹配（主线 #60/#124 的路线）**：模式方法的 `ShapeOf x Is hasAnotherChar(c)` 覆盖的是"带条件的匹配"；但裸 `TryGetValue` 不进模式语境。主线的"并进模式匹配"解决不了 90% 的 `Try*` 调用——这就是为什么我们坚持独立做调用点 `Out`，同时让形参侧与模式方法共享。
- **泛型 `Deconstruct` / 解构（2016 Tuples 的 `Dim (x, y) = GetPoint()`）**：解构要求方法返回元组或具有 `Deconstruct`；`Try*` 返回 `Boolean`，解构不适用。

#### 10. 复杂度 / 成本 / 优先级

实现面可切三片：**(a) 调用点 `Out value` + 隐式声明 + `As` 类型注解**（Try* 主场景，价值最高）；**(b) 形参侧 `Out` + OutAttribute 发射 + use-before-assign / return-without-assign 警告**（模式方法结构件，价值高但依赖 return-byref）；**(c) 显式 `ByRef` 实参**（价值薄，Table）。binder 工作集中在上下文关键字解析、隐式声明、重载推断（Out 实参不贡献输入）、copy-out 代码生成、OutAttribute 发射——中等量级，且与 return-byref 的代码生成（`ret` 前赋值序列）完全复用。**优先级随组 7（模式匹配）与组 6（return-byref）走：a 可独立先落，b 必须与 return-byref 同一交付。**

#### 11. 运行时 / CLR 硬约束

无硬约束。`Out` 形参发射为 `<Out> ByRef`（OutAttribute + ByRef），与 C# `out` 的 IL 形态完全一致（2014 #42："The declaration-site will be emitted as <Out> Byref"）。默认初始化可在编译期优化掉（#42 明示）。PEVerify 无碍；不触达 CLR 存储规则；无表达式树问题（Out 实参是普通实参，隐式声明的局部是普通 `stloc`）。互操作上，C# 消费 VB 的 Out 方法与消费自己的 `out` 方法一致。

#### 12. 值不值得做

逐维打分。**价值**：高——Try* 高频样板、模式方法地基、主线 2014 原则批准过的欠账。**成本**：中——三片可分，binder 中等，且与两条既有建议共享实现面。**风险**：低——纯语法增量、零 break，唯一风险面（`Out` 上下文关键字）有明确的兼容矩阵。**结论：值得做，但收范围。** 若把 (c) 显式 `ByRef` 与 (a)(b) 捆绑成"必须全做"，我们不会批——(c) 是"第二种做事方式"，配不上这个特性的名声。**以 (a)+(b) 为 v1，(c) Table。**

### VB 基因对照

- **永不破坏（原则 #1）**：纯语法增量，零 break——唯一风险是 `Out` 上下文关键字，用"后跟标识符才作修饰符"守住。这是全场最强论据之一。
- **保持 VB-like（原则 #2）**：`Out` 是英语单词、低仪式；`If cache.TryGetValue(name, Out value) Then` 读起来像句子。2014 #34 对更宽的声明表达式说过 "None of this feels naturally 'VB'ish. We're happy if VB sticks merely to Out parameters and implicit declaration of Out arguments"——我们做的正是"merely"那部分，没有滑向声明表达式。
- **不引入"第二种做事方式"（原则 #3）**：**最大扣分项，且我们要诚实面对**。`Out value` 是新语法，但它不是第二种方式——今天"隐式声明输出变量"这件事根本没有第一种方式。真正的第二种方式是 (c) 显式 `ByRef` 实参：`ReportErrors(ByRef diagnostics)` 与 `ReportErrors(diagnostics)` 并存。这正是我们 Table (c) 的原因。
- **默认跟随 C#，除非有充分理由（原则 #4）**：调用点 `Out value`、`Out value As T` 都是 C# `out var` 的 VB 化；2014 #42 标注 "Aligns with C# 'out vars' feature"。跟随 C# 有主线背书；`Out` 关键字与 `As` 注解是 VB 的充分偏离理由（保留英语读感、消歧）。
- **读起来像英语、对新手友好（原则 #5）**：`If Integer.TryParse(s, Out n) Then Return n` 不需要注释就懂。`Return True, root:=1` 稍绕，但显式 `name:=` 救场。
- **不为边缘场景加特性（原则 #6）**：(c) 显式 `ByRef` 正是边缘场景（清晰性只有，新能力没有）——Table 的处理符合本条。
- **避免隐蔽语义变化（原则 #7）**：`Out value` 是加关键字，不是细微字符（对比 `Return?` #167）。真正的隐蔽点在前一条会议定的元组括号分歧（`Return (a, b)` vs `Return a, b:=x`），已由显式 `name:=` + 文法切分对冲。
- **不与既有语法冲突（原则 #8）**：`Out` 不是保留字、不占类型字符、不与 `!` 冲突——但正因为不是保留字，上下文规则必须窄（后跟标识符才作修饰符），这是本特性对原则 #8 最大的敬意。
- **消除常见样板（原则 #9）**：正中靶心。Try* 的预声明 + 警告压制是教科书级样板。
- **冗长只在有用时是美德（原则 #10）**：`Out` 标记"只写"意图，有用；`Out value As Integer` 的 `As` 只在消歧时出现，有用才冗长。

**与主线关系（对照表 2.3）**：`Out 参数/隐式声明` 行——主线"讨论中"，ModVB"完整"，关系"一致"。完整的主线时间线是：2014 #42 原则批准（对齐 C# out vars）→ 2017-08-23 #152 拆分（识别 OutAttribute、不加强制实参修饰符、形参侧单独做）→ 2018-05-30 #305 并入模式匹配 / No Plans（#60 out variables、#124 pattern matching）。我们与主线的张力只有一处：2017 "Don't add an Out argument modifier" 与调用点 `Out` 修饰符。我们的调和读法（关键字只在隐式声明时必需）已在前文展开并标 `Suspect`。其余全部方向一致——本建议是主线 Out 参数讨论的完整落地，不是背离。

### RESOLUTION:

1. **原则上采纳 `Out` 实参/形参建议**，作为模式匹配（组 7）、return-byref（组 6）与 Try* 改造（本条）共享的结构件。**v1 范围 = (a) 调用点 `Out` + 隐式声明 + `As` 类型注解 + (b) 形参侧 `Out` 只写形参 + OutAttribute 发射。**
2. **调用点 `Out` 是"隐式声明开关"，不是强制修饰符**（2014 #42 规则）：实参已绑定且是 lvalue 时不加关键字照常匹配 Out 形参；`Out value` 中 `value` 未绑定时隐式声明，`value` 已绑定时按传参处理（要求 true lvalue）。`Out` 后跟 `As` 时隐式声明的类型 = `As` 声明的类型，并参与重载消歧。
3. **上下文关键字规则**：`Out` 仅当其后的记号是标识符或 `Dim` 时作修饰符；否则维持变量/成员访问解析。配套兼容性测试矩阵（`Dim Out As Integer`、`M(Out)`、`M(Out.value)`、`M(Out + 1)`、`Function F(Out As Integer)`）。
4. **形参侧 `Out`**：发射为 `<Out> ByRef`（OutAttribute）；进入方法默认初始化（可优化）；use-before-assign 与 return-without-assign 双警告（2014 #42）。含 `Out` 形参的方法，每条 `Return` 必须对全部 `Out` 形参赋值，否则警告（与 return-byref 会议一致）。
5. **`Optional Out` 禁止**——默认值语义与"只写"承诺冲突，且违反"每条 Return 全赋值"。
6. **`Out` 实参要求 true lvalue**（变量、字段、数组元素）；property 目标报错。无关键字的 `ByRef` 匹配保持既有 copy-in/copy-out 不变。
7. **隐式声明的变量作用域 = 最近的外层块**，调用返回后必然已赋值（默认初始化兜底），可能是 `Nothing`/默认值；失败分支可见。
8. **`Return True, root:=1` 承载于 return-byref 建议**，两建议同一份 spec、同一原型；求值顺序沿用"先求返回表达式、再从左到右赋值"。
9. **显式 `ByRef` 实参语法（PROPOSAL D / 表面 c）Table**——"第二种做事方式"、与 2017 "Don't add an Out argument modifier" 正面相撞、新能力只有 lvalue 检查；其"加关键字即要求 true lvalue"的检查吸收进 `Out` 修饰符。
10. **new warning on a VB override**：override/接口实现把基类 Out 形参改写成 ByVal 或改变 Out/ByRef 属性时发新警告（2017-08-23）。
11. **不加 `langversion` 门控**（纯语法增量），但修建议原文的两个示例精度问题（`Let intRoot As Integer = Math.Sqrt(number)` 在 Strict On 下不编译；`cache` 未声明类型）——见 TODO。
12. **与 return-byref、shapeof-pattern-matching、user-defined-pattern-methods、nullability-flow-analysis 共用引擎与 spec**；`Out value As T` 的 `As` 语法为未来的交/并类型保留接口。

### Implication:

- 将本建议与 `proposal-return-byref.md` 合并为一份 spec：`Out` 上下文关键字文法（BNF）、调用点隐式声明规则、`Out value As T` 消歧、作用域（最近外层块）、`Optional Out` 禁止、true lvalue 规则、OutAttribute 发射、双警告、override 一致性警告。
- 最小原型按表面 (a) 先行：调用点 `Out` + 隐式声明 + `As` 注解，验证上下文关键字解析、重载推断（Out 实参不贡献输入）、copy-out 代码生成、语义模型（新局部符号）。
- 补互操作矩阵：C# → VB → C# 与 C# → VB → VB（2017-08-23），确认 OutAttribute 双向识别与 C# `out` 对齐。
- 补兼容性测试矩阵与示例修正（见 TODO）。
- 与组 7（user-defined-pattern-methods）对表：确认"模式方法每条 Return 全赋值"与"失败时不参与逻辑"的调和由流分析承担。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：2017-08-23 "Don't add an Out argument modifier" 的读法——我们采"不加强制修饰符、关键字仅用于隐式声明"的调和读法，但该笔记极简，需要社区/主线确认；若主线本意是"连隐式声明的关键字都不要"，则调用点 `Out` 降级为纯隐式声明语法、改走 `Dim x` 前缀或其他形式。
- `OPEN QUESTIONS`：`M(Out value)` 中若同时存在变量 `Out` 与变量 `value`——上下文规则按"后跟标识符"解析为修饰符，`Out` 变量被忽略。是否需要在"`Out` 变量存在且紧邻标识符"时发警告（"此处的 Out 被解析为修饰符"）？v1 不发，保持简单；待反馈。
- `OPEN QUESTIONS`：模式方法匹配失败时 Out 变量的 definite-assignment——本建议"每条 Return 全赋值"与 2014-04-23 "definitely assigned only if the 'e matches T(args)' returns true" 的调和，是否允许模式方法声明"失败路径可省略赋值"的例外？v1 不开放例外。
- `TODO`：修 `IsPerfectSquare` 示例——`Let intRoot As Integer = Math.Sqrt(number)` 改为 `Let intRoot As Integer = CInt(Math.Round(Math.Sqrt(number)))`（或声明为 `Double`），并给 `cache` 声明类型，使主示例在 `Option Strict On` 下可编译。
- `TODO`：量化 Try* 隐式声明消除的样板与 definite-assignment 警告数据（数据/普遍性证据）。
- `Follow-up`：`Optional Out` 被禁止后，与 `proposal-user-defined-pattern-methods` 的可选输出参数需求（若有）对表。
- `Follow-up`：显式 `ByRef` 实参（Table 状态）若后续社区提出强需求，重审时需回答"为什么它不是第二种做事方式"。
- `Follow-up`：与 `proposal-intersection-union-types.md` 的 Out 结合（交并类型目标）在 v1 后评估。

### 状态

- **LDM 状态：Active（限定范围）**——表面 (a)+(b) 为 v1，表面 (c) 显式 `ByRef` 实参为 Table；与 return-byref、模式方法同一交付线。
- **三态判定：Active** — 主线 2014 原则批准的欠账、Try* 高频样板、模式方法地基，价值真实；零 break、范围可缩、风险（上下文关键字）有明确对冲。以调用点 `Out` 为先切片落地。

---

## 附录：特性评价

# 建议评价报告：proposal-out-arguments.md

## 评价对象

- 建议：proposal-out-arguments.md — `Out` 实参与形参
- 来源：Anthony 原文第 8 章 "General Modernization and Evolution II (Declarations)"（`Out` 实参/形参与 `IsPerfectSquare` 案例）；第 7 章模式方法（`Out` 形参作为模式方法签名骨架）；`Return True, root:=1` 为 §3.13 `Return`（见 `proposal-return-byref.md`）
- 配方目标：调用点 `Out value` 隐式声明消除 Try* 预声明样板与 definite-assignment 警告；形参侧 `Out root As Integer` 提供惯用多返回值，作为模式方法（第 7 章）的承载语法；`Return True, root:=1` 单语句完成"返回 + 写回"；显式 `ByRef` 实参提供清晰性与严格检查

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Try* 隐式声明与多返回值主效果清晰、示例可操作；但证据止于书面（无原型，按规则封顶 4）；关键子效果缺失：作用域规则未定、`Optional Out` 未定、泛型/重载推断未展开、与 return-byref 的集成未声明、主示例在 `Option Strict On` 下不编译（`Let intRoot As Integer = Math.Sqrt(number)`） | 已检查 | 无原型；示例精度问题；作用域/推断/集成未定 ⇒ 效果未完全显现 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。调用点 `Out` = C# `out var` 的 VB 化（2014 #42 背书 "Aligns with C# 'out vars' feature"）；形参侧 `Out` 承接主线基因；但打包了显式 `ByRef` 实参标记——次要无关能力、无主线先例、与 2017 "Don't add an Out argument modifier" 冲突 | 已检查 | 三表面捆绑；显式 `ByRef` 切片与原则 #3 张力；`Out value` 与 For 变量隐式声明的同族关系未讨论 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design 极薄（每表面仅一个示例，无 BNF、无作用域/推断/兼容性展开）；示例在 Strict On 下可能不编译；`cache` 类型未声明；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 缺 BNF 与兼容性分析；示例可编译性存疑；未覆盖 Option Strict 分叉 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（提速 Try*）/光（盘活惯用法）/水（强化模式方法差异化）正向；风=与主线"讨论中→No Plans"及 2017 "don't add modifier" 的张力、暗=`Out` 上下文关键字风险、作用域外溢、`Return` 括号分歧——文档对得失未权衡 | 已检查（预测待定） | 暗风险未在 Drawbacks 中显式识别（关键字风险、作用域外溢）；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料 = Anthony 第 7/8 章（原文示例已标章节）；未声明借鉴 C# `out var`（2014 #42 即对齐，血缘未标注）；未声明与主线 #42/#152/#305/#60/#124 的继承关系；`Let` 依赖 `proposal-local-declarations`、`Return` 赋值依赖 `proposal-return-byref` 均未标注 | 已检查 | 借鉴 C# 未标注；与主线讨论同源未标注；跨建议依赖未点明；无杂质 |

## 设计原则对照

- **与 VB 基因：基本一致但有张力**——零 break（原则 #1）、英语化低仪式（#2/#5）、消样板（#9）、`Out` 关键字有用才冗长（#10）一致；张力在原则 #3（显式 `ByRef` 实参 = 第二种做事方式；`Out value` 紧贴 C#）、原则 #7（`Return True, root:=1` 括号分歧同族，靠显式 `name:=` 对冲）。
- **与主线关系：主线一致方向上的完整化延伸**——2.3 表"Out 参数/隐式声明：主线讨论中 → ModVB 完整，关系一致"。完整时间线：2014 #42 原则批准 → 2017-08-23 #152 拆分处理 → 2018 #305 并入模式匹配 / No Plans。本建议是这条线的完整落地；唯一张力是 2017 "Don't add an Out argument modifier" 与调用点修饰符，以"关键字仅用于隐式声明"调和（`Suspect`，待确认）。与 `proposal-return-byref.md`（组 6）、`proposal-shapeof-pattern-matching.md` / `proposal-user-defined-pattern-methods.md`（组 7）强耦合；与 `proposal-nullability-flow-analysis.md`（组 1）共享引擎。
- **破坏性变更：无**——三个语法表面今天都是语法错误（纯增量）；唯一风险面是 `Out` 上下文关键字（`Dim Out As Integer`、`M(Out)`、`M(Out.value)` 必须保持既有解析），需兼容性矩阵守护。

## 总评

- **达成程度：部分达成**——概念与主线 2014 原则批准一致、零 break、是模式方法的结构件，价值成立；但三表面捆绑使范围过大、示例在 Strict On 下不编译、作用域/推断/集成规则未定、显式 `ByRef` 切片价值薄且与主线冲突。
- **LDM 三态建议：Active（限定范围）**——v1 = 调用点 `Out`（隐式声明 + `As` 消歧）+ 形参侧 `Out`（OutAttribute + 双警告），与 return-byref 同一份 spec；显式 `ByRef` 实参切片为 Table。调用点 `Out` 可作第一独立切片。
- **主要问题**：① 三表面捆绑，显式 `ByRef` 是"第二种做事方式"且撞 2017 "don't add modifier"；② 作用域与泛型/重载推断规则未定；③ 主示例在 `Option Strict On` 下不编译；④ 与 return-byref / 模式方法 / 流分析需统一引擎与 spec，建议未声明；⑤ 未标血缘（C# out var、主线 #42/#152/#305）。

## 返工建议

- **补充章节**：Detailed design 扩写——`Out` 上下文关键字文法（BNF：实参位置 `Out` [Dim] 标识符 [As 类型]）、隐式声明作用域（最近外层块）、`Optional Out` 禁止、true lvalue 规则、泛型/重载推断（Out 实参不贡献输入）、OutAttribute 发射与互操作矩阵（C#→VB→C# / C#→VB→VB）、override 一致性警告、Compatibility/breaking-change 分析（`Out` 上下文关键字兼容矩阵）。
- **补充证据**：最小原型（调用点 `Out` + 隐式声明 + `As` 注解，验证解析/推断/代码生成/语义模型）；IDE 补全（pretty-lister 插入 `Out `）验证；量化 Try* 样板与 definite-assignment 警告数据。
- **未决问题处理**：作用域定最近外层块；`Optional Out` 定禁止；`Return` 省略 Out 赋值定报错（承接 return-without-assign 警告）；显式 `ByRef` 切片与主线对表后定（Table）；示例修正（`CInt(Math.Round(Math.Sqrt(...)))`、声明 `cache` 类型）使 Strict On 下可编译。
- **设计探索**：与 return-byref 合并后的求值顺序说明；与 nullability-flow-analysis 统一引擎后的"已赋值 ≠ 非空"行为差异表；与交并类型的 Out 结合（`Out value As T` 为未来类型保留）；2017 "don't add modifier" 的社区确认（OPEN QUESTION）。

---

## 附录：C# 生态与互操作考量

本附录把本条 Out 建议放进 dotnet/csharplang 的现实坐标：C# 怎么做了 out var、C#/CLR 对 out 的元数据与安全模型走向，以及 VBScript.NET（.vbx）要为此准备什么。基于 `..\..\csharplang-index.md` 的 T2/M3 与 `..\..\csharplang` 关键文件深挖；C# 原文均逐字引用并标注来源。

### 相关 C# 现实方向

**out var（C# 7）——调用点声明，与本提案的调用点 `Out value` 同源。** `proposals\csharp-7.0\out-var.md` 开门见山：

> "The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument."

其文法为 `argument_value : 'out' type identifier`，作用域与模式变量同轨（"The scope will be the same as for a *pattern-variable* introduced via pattern-matching."），champion issue 正是 `dotnet/csharplang#60`——本提案正文引用的 `vblang #60 – Out variables` 与 C# 侧同为各自仓库的 #60，同名同源。重载推断规则与本提案"Out 实参不贡献类型推断输入"逐字对应：

> "An implicitly-typed out variable argument has no type."
> "The type of an implicitly-typed out variable is the type of the corresponding parameter in the signature of the method selected by overload resolution."

**作用域的历史摇摆：C# 先收后放，且始终在"漏"上设卡。** 声明表达式（out var 前身，C# 6 尝试）在 `meetings\2014\LDM-2014-09-03.md` 里被收掉溢出，且理由恰好是 COM 场景：

> "The spill-out is actually a bit of a nuisance for the somewhat common scenario of passing dummies to ref or out parameters that you don't need (common in COM interop scenarios)"
> "Let's get rid of the spilling. Every declaration expression is now limited in scope to it nearest enclosing statement."

两年后 C# 7 的 out var 把它放回来一部分。`meetings\2016\LDM-2016-07-15.md` 采纳 Option 3，作用域边界 = 块 + `for`/`foreach`/`using` + 所有嵌入语句：

> "**Option 3:** Expression variables are scoped by blocks, for, foreach and using statements, as well as all embedded statements… The consequence is that variables would always escape the condition of an `if`, but never its branches."

而 `meetings\2016\LDM-2016-04-12-22.md` 明确把 if 条件里引入的变量排除出 else 分支（为嵌套 else-if 复用名字留路）：

> "…they generally be in scope within all of the nearest enclosing statement, except when that is an if-statement, where they would not be in scope in the else-branch."

**调用点强制标注：C# 必须写 `out`。** `proposals\csharp-12.0\ref-readonly-parameters.md` 的调用点矩阵，`out` 形参列只有 `out` 标注是 Allowed，`ref`/`in`/无标注都是 Error。这与本提案/2014 #42 的 back-compat（既存变量不加关键字照常匹配）形成表面差异。

**元数据：out = byref + `[Out]`；in 另有 `IsReadOnly` + `modreq(In)`。** `proposals\csharp-7.2\readonly-ref.md` 对 in 的元数据编码：

> "When `System.Runtime.CompilerServices.IsReadOnlyAttribute` is applied to a byref parameter, it means that the parameter is an `in` parameter."

（且 abstract/virtual 的 in 形参需 `modreq[System.Runtime.InteropServices.InAttribute]`。）out 侧，C# 函数指针把 `OutAttribute` 作 `modreq` 直接钉在参数 ref specifier 上——`proposals\csharp-9.0\function-pointers.md`：

> "We use `System.Runtime.InteropServices.OutAttribute`, applied as a `modreq` to the ref specifier on a parameter type, to mean that the parameter is an `out` parameter."

**C# 11：out 隐式 `scoped`，out 参数不再可按 ref 返回。** `proposals\csharp-11.0\low-level-struct-improvements.md`：

> "…the language will change the default *ref-safe-context* value for `out` parameters to be *function-member*. Effectively `out` parameters are implicitly `scoped out` going forward."
> "An argument to an `out` parameter does not contribute to the return, it is simply an output."

`[UnscopedRef]` 可以把 out 参数恢复到 C# 10 的 unscoped（原表：`out` parameter：function-member → return-only）。

**C# 15 unsafe evolution：`[Out]` 初始化保证进入安全判据——与本提案关系最重的一段。** `proposals\unsafe-evolution.md` 的 Open Question：

> "Since for example VB doesn't guarantee that `[Out]` parameters are initialized, in combination with `[SkipLocalsInit]`, calling such parameters could be considered `unsafe` in C#."

以及同段对跨语言契约的期望：

> "…currently those are from C# which guarantees correct usage of `[Out]` but if other languages implement the new rules, they should guarantee that too."

同文「VB」小节还明确了 VB 在 unsafe-evolution 里的定位——VB 不需要 *requires-unsafe* 成员，因为 VB 今天没有 unsafe 上下文、也没有指针：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（「VB」小节）

### 现实 vs 提案

| 维度 | C# 现实 | 本提案 | 判定 |
|---|---|---|---|
| 调用点声明 | `out var x` / `out Type x`（C# 7） | `Out value` / `Out value As T` | **兼容**——同源（#60）；VB 化偏离（`Out` 上下文关键字、`As` 注解）有充分理由 |
| 重载推断 | 隐式 out var 无类型，取所选形参类型 | Out 实参不贡献推断输入，`As` 消歧 | **兼容**——语义逐字一致 |
| 形参侧/元数据 | `out` = byref + `[Out]`；C# 识别 [Out] 消费 | 形参侧 `Out` 发射 `<Out> ByRef`（OutAttribute） | **兼容**——IL 形态一致，双向识别可行 |
| 调用点标注 | 必须写 `out`（否则 Error） | 不加关键字照常匹配（2014 #42 back-compat） | **需桥接/显式记录**——不改 IL 与语义，只影响跨语言源码可读性；C# → VB 调用 VB Out 方法需写 `out` |
| 作用域 | Option 3：块 + 嵌入语句为界；if 条件里的变量**不在 else 分支**；2014 曾整体移除 spill-out | 最近外层块；显式欢迎 bleed（"We like the bleed, bounded"） | **需校准**——大方向一致，但 else 分支与"漏出 if 语句"的精细差异见 OPEN QUESTIONS |
| out 的返回性/逃逸 | C# 11 隐式 `scoped out`；`[UnscopedRef]` 恢复 | VB 无 ref-safe-context；.vbx 模块的 out 参数会被 C# 按隐式 scoped 分析 | **需桥接**——.vbx 需识别 `RefSafetyRules(11)` 模块属性与 `[UnscopedRef]`，避免跨语言调用被 C# ref 安全规则卡住 |
| `[Out]` 初始化保证 | C# 保证（callee 必先赋值）；C# 15 正考虑把"不保证 [Out] 初始化的调用"判为 unsafe | 默认初始化 + "每条 Return 全赋值" | **兼容且是加分项**——本提案把 VB 拉到 C# 的保证线上，是互操作安全契约，不只是语法糖 |
| override 一致性 | ref/out/in OHI 签名严格匹配（不匹配=错误） | 新警告（VB override 改 Out→ByVal 时） | **需桥接**——VB 警告比 C# 错误宽松；.vbx 生成 override 时应保持基类 out/ByRef 属性 |
| in / ref readonly / ref fields（T2 主线） | in（C# 7.2）、ref readonly（C# 12）、ref fields/scoped（C# 11） | 不涉及（本提案只做 out） | **脱节/低相关**——对 VB 是"消费侧"问题，非本提案语法侧；决策文件 M3 已覆盖 |

### 对 VBScript.NET 的适应建议

1. **把 Out 的"初始化保证"当作安全契约落地，而不是可选项。** unsafe-evolution 的原文显示 C# 正在把"VB 不保证 `[Out]` 初始化"当作调用方 unsafe 的候选判据。本提案的"进入方法默认初始化（可优化）+ 每条 Return 全赋值"恰好满足 C# 15 的期望（"if other languages implement the new rules, they should guarantee that too"）。.vbx 编译器应把这两条做成内建强制并写进元数据保证，让 C# 侧能安全消费 VB Out 方法。
2. **识别新元数据。** .vbx 至少要能分类 C# 形参侧元数据：`IsReadOnlyAttribute`（in）、`modreq(InAttribute)`（abstract/virtual 的 in）、`OutAttribute`（byref 参数；函数指针签名里是 modreq）、`[UnscopedRef]`（out 参数恢复 unscoped）、`[module: RefSafetyRules(11)]`（模块级 ref 安全规则版本）。至少做到：调用 C# `in`/`ref readonly` 方法时正确分类参数；消费 C# 11 模块的 out 方法时按"隐式 scoped out"理解调用点安全。
3. **默认编译到受管 IL，解释器模式显式 opt-in（决策文件 M5）。** Out 的 copy-out 语义在编译模式下直接映射为 byref + `[Out]` + `stloc`（正文第 11 节已确认无 CLR 硬约束）；解释模式需要为 Out 形参模拟引用槽（copy-out 到调用栈槽），成本高且与 AOT/trimming 冲突——interpreted 模式应做成显式传统兼容层。
4. **补强互操作矩阵。** 2017-08-23 要求的 C# → VB → C# 与 C# → VB → VB 矩阵，加入三条本附录验证过的支线：(a) VB 不加关键字调用 C# out 方法（元数据识别即可）；(b) C# 调用 VB Out 方法必须写 `out`；(c) 跨语言 override 链的 out/ByRef 属性一致性——VB 侧警告、C# 侧错误，.vbx 生成的程序集应在 override 处保持基类签名。
5. **source-gen 桥（索引 T6）。** C# 方向是编译期生成替代运行时动态。Out 本身不涉反射，但 .vbx 脚本层若走"编译到受管程序集 + source-gen 桥"，调用点隐式声明可在 binder 阶段直接完成（声明新局部符号），无需运行时兜底。

### 对既有 RESOLUTION/三态判定的影响

- **三态判定不变**：本附录不改变 RESOLUTION 1–12 与 Active 判定；反而补强"兼容"与"加分"证据——尤其 unsafe-evolution 的 `[Out]` 保证，把本提案从语法糖升格为互操作安全契约。
- **RESOLUTION 7（作用域）需校准**：正文称"与 C# out var 一致"（最近外层块）——对普通语句位置成立，对 if 条件位置需对照 C# 的 else 分支排除规则（见 OPEN QUESTIONS）。建议 spec 合并时以 `meetings\2016\LDM-2016-07-15.md` 的 Option 3 为基准重写作用域节，显式决定 VB 是否保留"else 分支可见"。
- **RESOLUTION 11（不加 langversion 门控）**：不受影响。C# 侧 out var 是 C# 7 特性、无门控先例；VB 侧零 break 判断独立成立。
- **Implication 的互操作矩阵条目**：得到本附录实证支撑（`[Out]` 保证、`OutAttribute` modreq、`RefSafetyRules(11)`），可把 unsafe-evolution 的 `[Out]` 讨论作为"VB 必须保证 Out 初始化"的硬性论据写进 spec。

### OPEN QUESTIONS / Suspect

- **`Suspect`——"作用域与 C# out var 一致"在 if 条件位置是否精确成立？** C# 的 Option 3（`meetings\2016\LDM-2016-07-15.md`）把 if 的 else 分支当嵌入语句边界、明确排除 else（`meetings\2016\LDM-2016-04-12-22.md`），且 2014 年曾整体移除 spill-out（`meetings\2014\LDM-2014-09-03.md`）；本提案"最近外层块 + We like the bleed, bounded"在 else 分支与"漏出 if 语句"处与 C# 有精细差异。C# 7 最终规范的逐字作用域规则已迁出本库（dotnet/csharpstandard），无法在此逐字核实 → 需在 spec 合并时以 C# 7 规范原文对表。
- **OPEN QUESTIONS——unsafe-evolution 对"不保证 [Out] 初始化的调用"是否判 unsafe 未裁决**（原文语态是 "could be considered unsafe" / "If we decide those cases should be `unsafe`"）。VBScript.NET 应跟踪裁决：若判 unsafe，VB 的初始化保证从"建议"变"硬性互操作要求"。
- **OPEN QUESTIONS——override 警告 vs C# 签名错误**：VB 用警告、C# 用错误处理 out/ByRef 属性不一致。跨语言 override 链是否需要 .vbx 侧升级为错误以匹配 C#？v1 保持警告，待社区反馈。
- **OPEN QUESTIONS——函数指针的 `OutAttribute` modreq**：.vbx 无函数指针，v1 可不识别；若未来消费 C# `delegate*` 的安全包装，需识别该 modreq。列入未来项。
