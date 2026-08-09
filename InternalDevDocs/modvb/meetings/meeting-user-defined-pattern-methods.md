# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周回到模式匹配家族——上周我们已对 `ShapeOf` 模式匹配做了整场评审，今天处理它的近邻：用户定义模式方法（User-Defined Pattern Methods）。这个建议的核心主张是：**任意一个带 `Out` 参数、返回 `Boolean` 的普通函数都能当模式用**。我们进场时很喜欢这个场景，出场时只喜欢概念的一半——和 `ShapeOf` 那场一样，问题出在载体语法与契约强度上。

## Agenda

* [ModVB Proposal — User-Defined Pattern Methods / 用户定义模式方法](#modvb-proposal--user-defined-pattern-methods)

## ModVB Proposal — User-Defined Pattern Methods

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；[vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)；[vblang #60 – Out variables](https://github.com/dotnet/vblang/issues/60)；ModVB：`ShapeOf` 模式匹配、命名模式、`Out` 实参/形参_

_Note on honesty: this note is a review of a ModVB proposal (`proposal-user-defined-pattern-methods.md`, source: Anthony D. Green 原文第 7 章 "General Pattern Matching" 的用户定义模式方法片段，依赖第 8 章 `Out` 实参/形参)。陈述分层为 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。凡引用 vblang 主线会议的具体决定与引文，均取自 `..\..\vblang\meetings/` 原始笔记并逐字核对；找不到直接对应材料处，基于主线设计原则与评价标准独立审慎论证，并明确标注 `Suspect` / `OPEN QUESTIONS`。_

---

### 场景与缺口

内建类型模式只能回答"是不是某类型"。真实世界的匹配往往**同时要求判定与取值**：例如"流里还有没有下一个字符？有的话取出来"。今天这一段逻辑要拆成三条语句：

```vb
' 今天：判定与取值拆成两步，逻辑被 `If/ElseIf` 与变量声明切开。
Dim ch As Char
If Not stream.EndOfFile() Then
    ch = stream.PeekChar(0)
    If ch <> Nothing Then
        buffer.Append(ch)
        stream.EatNextChar()
    End If
End If
```

We see a clear gap here，而且它正好命中我们 2017.10.18 记下的模式引入原则——"the ceremony of _inspecting_ and object(-graph) obscures the structure of the data"（事实，2017.10.18，JSON Pattern Matching 段落；原文此处笔误，`and` 当为 `an`）。判定是结构，取值也是结构，把它们拆散就是仪式感遮蔽结构。

用户定义模式方法把"判定 + 提取"封装成一个普通函数，让 `If`/`Select Case` 一次完成。原文示例（Anthony 第 7 章，逐字）：

```vb
' User-defined pattern methods.
Let hasAnotherChar =
      Function(reader As TextStream, Out ch As Char) As Boolean
          If reader.EndOfFile() Then
              Return False, ch:=Nothing
          Else
              Return True, ch:=reader.PeekChar(0)
          End If
      End Function

If ShapeOf stream Is hasAnotherChar(c) AndAlso c <> Nothing Then
    buffer.Append(c)
    stream.EatNextChar()
End If
```

The idea is immediately attractive. 它对 VB 的 `Try*` 血统（`TryParse` / `TryGetValue`）做了一等化：过去"返回布尔 + `ByRef` 输出"只是惯例，现在它成为语言的模式机制。**这个血缘是真实的、极 VB 的。** 而且——这很重要——主线 2014.02.17 在讨论 MatchFunction 时就明确许愿过："It would be nice to dispatch on `Integer.TryParse`"（事实，2014.02.17，OPEN QUESTIONS 第 1 条）。我们十四年前就想要一个"把 `Try*` 方法当模式用"的语言，今天它送上门了。

但场景的真实不等于建议的成立。本次会议的核心矛盾：**建议唯一的调用点示例，用的是我们上一场已经判给 Table 的 `ShapeOf ... Is` 表面语法**，而且函数调用出现在 `Is` 右侧——恰是 2018.12.19 我们警告过的歧义区。

---

### 候选方案

**PROPOSAL A — 任意 `As Boolean` + `Out` 函数即模式（建议原文，Anthony 第 7 章）**

```vb
' PROPOSAL A：无标记、无校验，任何满足形状的函数都是模式。
If ShapeOf stream Is hasAnotherChar(c) AndAlso c <> Nothing Then
    buffer.Append(c)
End If
```

`ShapeOf <主语> Is <模式函数>(<Out 绑定>)`：主语隐式映射为模式函数第一个形参，`Out` 绑定隐式声明并赋值。无新关键字（`ShapeOf` 是既有家族建议）、无特性、零仪式。

**PROPOSAL B — 显式 `Pattern` 标记，编译器严格校验契约**

用特性或修饰符显式声明"这是一个模式方法"，编译器验证其满足契约：只读输入、所有路径上都给 `Out` 赋值、返回 `Boolean`。不满足则拒绝。这直接回应主线 2014.02.17 的未决问题——"Or maybe each active pattern should be explicitly indicated with an attribute (like extension methods)"（事实，2014.02.17，OPEN QUESTIONS 第 1 条）。

**PROPOSAL C — 只允许单个 `Out` 参数**

模式方法至多一个 `Out`；多值匹配（如命名模式的 `ConstructorCall(kind, args)`）全部交给命名模式建议承担。把本建议的绑定规则压到最简。

**PROPOSAL D — 并入主线 `Matches` 文法，不引入 `ShapeOf ... Is`**

调用点改用我们 2018.12.19 认可的关键字 `Matches`，与主线模式文法（`If o Matches x As String`）统一：

```vb
' PROPOSAL D：家族表面。`Matches` 是 2018.12.19 提议的关键字（事实）。
If stream Matches hasAnotherChar(Out c) When c <> Nothing Then
    buffer.Append(c)
    stream.EatNextChar()
End If

Select Case stream
    Case hasAnotherChar(Out c)
        buffer.Append(c)
End Select
```

`ShapeOf`（若保留）只作为家族"形状匹配"的读法，不承载用户定义模式方法。

---

### 权衡：Q&A

- **A vs B：契约该不该硬？** 建议自认宽松——Drawbacks 第一条就是"编译器难以静态证明其'只读输入、只在成功时写输出'的性质，可能允许写出有副作用的模式"。我们对此零容忍：如果任何 `Function(..., Out x) As Boolean` 都能当模式，那么副作用模式、失败路径不给 `Out` 赋值的模式、读输入两次返回不同结果的模式都会混进来。2014.02.17 我们没把 MatchFunction 落地的原因之一就是绑定与契约不明。**结论：v1 取 B 的契约，不要 A 的裸约定。** 标记形式（特性 vs 修饰符）留待家族统一。
- **A vs C：多 `Out` 要不要？** 建议原文只演示了一个 `Out`，但命名模式建议（`ConstructorCall`）是三个 `Out`，且 2014.04.23 的 Match 操作符设想就是多输出的（`operator Matches(Point self, out int X, out int Y)`，事实）。若本建议只做单 `Out`，它就沦为"命名模式的子集"；若做多 `Out`，绑定规则必须写清（详见角案例）。**结论：语法上支持多 `Out`，但绑定/赋值规则必须先定；C 的激进限缩不必要。**
- **A vs D：`ShapeOf ... Is` 还是 `Matches`？** 这是整场最尖锐的分歧。`If ShapeOf stream Is hasAnotherChar(c)` 有两个独立问题：(1) `ShapeOf` 独立操作符在上一场已判 Table；(2) 把函数调用塞进 `Is` 右侧。2018.12.19 我们对 `Is` 的表态逐字是："We think `Is` will have ambiguity issues with the existing use for reference equality"（事实）。`hasAnotherChar(c)` 在 `Is` 右侧今天看起来就是个函数调用表达式——解析器必须远视才知道这是模式，IDE 补全与阅读者也要猜。**结论：D 的 `Matches` 表面胜出。** 这与我们"默认跟随 C#，除非有充分理由"的总则一致（2018.12.19，事实），也避免在家族文法里再开一条 `Is` 的歧义支线。

---

### 深度追问：LDM 拷问清单

We worked through the twelve-question checklist. The decisive questions were 1, 2, 4, 5 and 9.

#### 1. 语法 / 文法歧义

`If ShapeOf stream Is hasAnotherChar(c)` 的双重歧义：

- **`Is` 的语义重载。** 2018.12.19 的担心在此原样成立。`Is` 今天只有引用相等（`x Is y`、`x Is Nothing`）一种含义；把"模式匹配"塞进右侧等于让同一个操作符承担两种完全不同的事。我们 2018.12.19 说过"We might be able to finesse the use of `Is` with operators, by expanding thinking about what a pattern is, but other scenarios are more problematic"（事实）——用户定义模式方法正是那个"more problematic"的场景，因为右侧是**带副作用的函数调用**，不是类型名。
- **函数调用 vs 模式调用。** `hasAnotherChar(c)` 是普通调用还是模式绑定，取决于 `hasAnotherChar` 是否被标记为模式、`c` 是否已声明。若 `c` 已在外层声明，`hasAnotherChar(c)` 是对既有变量的读；若未声明，则是隐式 `Out` 声明。同一串字符、两种含义，靠上下文判定。我们 2014.02.17 就对这个判定不放心："How does that even work when invoking MatchFunctions? Overload resolution? It suggests putting overload resolution into the late-binder, which we don't like"（事实）。**这一条直接压向 D**——`Matches` 是显式二元操作符，模式语境不再依赖"猜这是个模式调用"。

`Case hasAnotherChar(Out c)` 在 `Case` 子句内则相对干净：`Case` 今天只接表达式，模式是新增子句形态（家族文法，`Case` 子句内无关键字，2018.12.19 认可"an additional keyword is not required in all `Case` cases"，事实）。但逗号问题仍在：`Case hasAnotherChar(Out c), 47` 是否允许？我们 2018.12.19 的决议是"the comma to remain a special feature of `Case`, not a part of the pattern syntax"（事实）。`Probably`：逗号合并留给家族文法收口。

#### 2. 角案例与边界语义

**失败赋值保证。** 建议示例显式写 `Return False, ch:=Nothing`，但未声明这是编译器保证还是用户纪律。2014.02.17 的 Out 参数决议给出了现成答案："The declaration method will default-initialize all Out parameters on entry (this can be optimized away by the compiler). Warnings will be emitted for both use-before-assign and return-without-assign"（事实，2014.02.17 #42）。**结论：采用同一规则**——`Out` 形参入口默认初始化 + 未赋值路径警告。这样失败分支的 `ch` 必然有定义，`c <> Nothing` 才有意义。

**值类型陷阱（本建议最隐蔽的角案例）。** `Out ch As Char` 是值类型。失败时 `ch := Nothing` 得到 `Chr(0)`；成功时若 `PeekChar(0)` 恰巧返回默认字符，`c <> Nothing` 为假——**`c <> Nothing` 无法区分"匹配失败"与"匹配成功但值是默认值"**。对引用类型（`Out s As String`）此问题不存在。建议正文把这行 `AndAlso c <> Nothing` 当作守卫写进核心示例，但对值类型它是语义脆弱的一行。**这正是我们偏好 `When` 的原因**——`When c <> Nothing` 是显式守卫，语义与"匹配成功后的附加条件"一致，且 2018.12.19 我们明确"We like `When`"（事实）。

**Null 主语。** `ShapeOf Nothing Is hasAnotherChar(c)`——主语为 `Nothing` 时是否调用模式函数？2014.04.23 的 Match 操作符设想已给答案："the T's Match operator is not necessarily invoked... e.g. if f() returns null, then it won't be called!"（事实，2014.04.23）。`Probably`：用户定义模式方法遵循同一规则——主语为 `Nothing`（或类型不匹配）时不调用函数、直接不命中。**但这要求模式函数与 `TypeOf` 一样有"形状预检"**，而建议原文没有定义"主语与第一形参类型不匹配"时的行为。`OPEN QUESTIONS`。

**主体类型不匹配。** 若 `stream` 的静态类型无法转换为 `TextStream`（第一形参），是编译期报错还是运行期不命中？`Probably`：编译期报错（模式函数签名已知，主语类型已知，无法满足则拒绝）。宽松模式下主语为 `Object` 时走运行期预检。`Suspect`：建议未覆盖，需要写进规范。

**多次求值与副作用。** `If ShapeOf stream Is hasAnotherChar(c)` 中 `stream` 若为属性，`ShapeOf stream` 求值一次；模式函数被调用一次。但若出现在 `Case` 里被多分支共享，或 IDE 实时求值，副作用模式（B 契约要禁的那类）会反复执行。这就是契约必须硬的理由之一。

**重入与递归。** 模式函数内再调模式函数（命名模式的递归嵌套）——家族文法里天然需要。本建议未提深度限制。`Probably`：无语言级深度限制，运行时栈自限。

**重载与泛型。** 建议未决问题第 3 条自承"是否支持重载与泛型参数，原文未展示"。`Probably` 需要支持（`TryParse(Of T)` 是现实的模式函数），但重载解析与 2014.02.17 的担心（把重载解析放进 late-binder）如何规避——因为我们这里主语显式传给第一形参、模式函数是**具名引用**而非按类型分发，`Probably` 走普通重载解析即可，不触碰 late-binder。`OPEN QUESTIONS`。

#### 3. 作用域与绑定

隐式声明的 `c` 作用域到哪？2018.12.19 文法注释把 `If` 引入变量的作用域定为"The scope of introduced variables is the surrounding scope"（事实），`While` 定为块内。`Probably`：模式绑定的 `c` 同此——若绑定在 `If` 条件里，作用域覆盖整个 `If` 块及其 `ElseIf`/`Else`；失败路径上 `c` 已默认初始化（见上），所以**"匹配失败时 c 不参与后续逻辑"应改写为"c 已定义但无意义，编译器可给出使用警告"**。

与已有变量重名时：`Out c` 若 `c` 已在外层存在——2014.02.17 #42 调用点规则是"you don't need an 'Out/Output' keyword to match an out parameter. However, you do need the keyword in order to declare the variable inline, and if you use the keyword then it is an error to pass it something that's not an lvalue"（事实）。据此：显式 `Out c` = 必须绑定既有 lvalue 或隐式声明新变量；本建议的裸 `hasAnotherChar(c)`（无 `Out` 关键字）应遵循 `For` 的规则——"you don't need Dim, and instead a new variable is implicitly declared if there's an 'As' clause or if it doesn't bind an existing name"（事实，2014.02.17 #42 未决问题）。`OPEN QUESTIONS`：若 `c` 已存在，是复用（覆盖）还是遮蔽？我们倾向遮蔽新变量，避免把外层变量偷偷改写——这与 `For` 循环变量语义一致。

#### 4. 与既有特性的交互

- **`Out` 实参/形参建议（第 8 章）。** 本建议完全依赖 `Return True, ch:=reader.PeekChar(0)` 与 `Out` 形参——而那是独立建议（`proposal-out-arguments.md`，主线 2014.02.17 #42 "Approved in principle, but needs design work. Aligns with C# 'out vars' feature."，事实）。**本建议必须排在 Out 建议之后**，或至少与之同批落地。没有 `Out` 形参，模式方法无从谈起。
- **`ByRef` copy-in/copy-out。** 模式函数的 `Out` 形参是只写；但若调用点把既有变量传入且是 `ByRef`（非 `Out`）语义，copy-in/copy-out 会读回旧值。契约 B 必须把模式函数的形参限定为 `Out`（纯输出），禁止 `ByRef` 读写混合——否则"只读输入、只写输出"的保证在形参层就破了。
- **`When`。** 与 `When` 组合是家族法文的自然扩展（2018.12.19 "We like `When`"，事实）。建议用 `AndAlso c <> Nothing` 表达守卫，功能等价但语义混在布尔表达式里；`When` 表达更清晰且天然与模式绑定顺序一致。
- **`IsTrue`/`IsFalse`、`AndAlso`/`OrElse`。** 模式返回 `Boolean`，不引入新真值操作符。`AndAlso` 短路与绑定顺序：`ShapeOf stream Is hasAnotherChar(c) AndAlso ...` 中绑定先于右侧求值——这正是建议想要的。`OrElse` 右侧（匹配失败时求值）`c` 已默认初始化，可用但无意义，应给警告。与 `IsTrue`/`IsFalse` 无交互。
- **Late binding / Option Strict Off。** 模式函数本身是强类型 lambda（`Function(reader As TextStream, ...) As Boolean`），不依赖宽松模式；主语在宽松下可为 `Object`，走运行期预检。**不得**把既有晚期绑定调用改成早期绑定（与 `TypeOf` 流分析建议同一条红线）。`Suspect`：需在宽松路径实测。
- **`TypeOf ... Is` 并存。** 2018.05.30 我们记过："TypeOf...Is is not an exact match, but whether a cast can occur"（事实），而用户定义模式方法的主语预检也应是"能否转换"而非"精确类型"——语义对齐，不构成重复：`TypeOf` 是内建类型测试，模式方法是用户可编程判定，二者是家族的两层。

#### 5. Breaking change 与兼容性

- **`Is` 右侧新增含义**：今天的 `If x Is Foo()` 是引用相等（`Foo()` 返回引用类型）。若 `Foo` 是模式函数，同一语法变成模式绑定——**对现有合法代码是潜在行为改变**。这是把"模式调用"塞进 `Is` 右侧的直接代价，也是 D 方案的最大优势：`Matches` 是新关键字，旧代码不可能合法地写 `x Matches y`，零破坏。
- **新关键字（`Matches`/`ShapeOf`）标识符冲突**：任何新关键字都有 identifier-collision 破坏风险（上一场已记录）。`Matches` 比 `ShapeOf` 更安全（词频更低、不是自然动词变体），但仍在家族层统一评估。
- **隐式声明语义**：`hasAnotherChar(c)` 中 `c` 若与既有变量同名，遮蔽 vs 复用直接改变旧代码行为。必须按 2014.02.17 的 `For` 规则明确定死（见作用域小节）。

#### 6. Option Strict / 编译选项分叉

两条路径必须一致：严格下主语类型在编译期对齐第一形参，宽松下主语可为 `Object`、运行期预检。模式函数自身永远强类型。`Out` 绑定 `c` 的类型推断在严格/宽松下应相同（宽松下也不得退化为 `Object`——那会让 `buffer.Append(c)` 变成晚期绑定，改变异常时机）。`Suspect`：建议未写任何 Option Strict 内容，需在原型中双路径验证。对 VBScript.NET（默认宽松）这条尤其重要。

#### 7. IDE / IntelliSense

`c` 在 `If` 条件里隐式声明、命中后才"有意义"——补全在该行如何表现？2018.12.19 #8 iv 我们说过"We don't usually let variables be used before they are declared, so it would look quite weird to allow this in one case"（事实，关于循环后置绑定，但精神适用）。InfoTip 需要显示"Char（仅匹配命中后有意义）"。2014.02.17 曾问过 pretty-lister 是否在输入函数调用时插入 `Out`（事实，#42 未决问题）——模式函数调用处同理。调试器在 `Case hasAnotherChar(Out c)` 断点处应能求值 `c`。语义模型：`c` 是分支/块局部变量，`GetTypeInfo` 返回 `Char`。这些都要在原型里验证，写不进规范等于没设计。

#### 8. 数据 / 普遍性

主线证据是真实的：2014.02.17 的 MatchFunction 许愿（"It would be nice to dispatch on `Integer.TryParse`"）和 2014.04.23 的 `Case e As PlusExpr When e.Matches(0, Out rhs)`（事实，2014.04.23）都是"判定 + 提取"的用户请求。VBScript 血统（`Try*`、运行时分发）也天然契合。**但必须诚实**：`Out` 实参建议单独就能覆盖 `If Integer.TryParse(s, Out i)` 这个最常见的 `Try*` 场景——**用户定义模式方法的边际价值不在单次 `Try*` 调用，而在"可命名、可复用、可组合的判定+提取"，尤其是递归解构（命名模式）里。** 若只想要"少写两行"，Out 建议就够了。这条是本建议最大的范围风险，也是它必须与命名模式合并设计的理由。建议自身无量化数据——证据等级止于"已提供/已检查"。

#### 9. 更简替代

- **`Out` 实参单独落地**：`If Integer.TryParse(s, Out i) Then` 已覆盖单次 `Try*`（2014.02.17 #42 愿景，事实）。对不组合的场景，不需要模式方法。**这是对我们最强的竞争者。**
- **内建类型模式 + 手动方法调用**：现状，样板多但确定。
- **Analyzer / 重构**：可提示"此处可抽成模式函数"，但不能让组合语法成立，只能当脚手架。

替代方案存在但都付出现场感或放弃组合性；模式方法的真实价值只在组合场景显现。`Probably`：价值成立，但必须与命名模式一起评估，不能单独看。

#### 10. 成本 / 优先级

实现成本**完全取决于家族文法是否先落地**。若 `Matches` + `Case` 模式子句（家族 Phase 1）不存在，用户定义模式方法没有立足点——它必须显式依附于模式文法。而有了模式文法后，模式方法只是多一种 Pattern 形态（2014.02.17 文法里早有 `M ::= ... | MatchFunction(M, …)`，事实）。成本中等偏低，优先级排家族 Phase 2（声明模式、命名模式之后），且在 Out 建议之后。**"值得做但太难"不成立——它是"值得做，但时序靠后"。**

#### 11. 运行时 / CLR 硬约束

无硬约束。模式函数就是普通函数调用 + `<Out> Byref` 形参（2014.02.17 #42 的发射方式，事实），`castclass`/isinst 预检是既有安全操作，PEVerify 无碍。不触达 CLR 存储规则；表达式树里模式不出现（绑定在编译期展开）。**唯一的 2014 遗留红线是"不依赖反射"**（2014.02.17：开放泛型"impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"，事实）——本建议的具名函数引用天然不碰反射，`Probably` 通过。

#### 12. 值不值得做

价值（组合判定+提取、`Try*` 一等化、VBScript 契合）× 成本（依赖家族文法，本身低）× 风险（契约松、`Is` 歧义）。逐条打分：概念价值 8 / 表面 A 的语法成本 6、风险 8（`Is` 重载 + 无契约）→ **A 表面不值得**；D 表面 + B 契约 价值 8 / 成本 4 / 风险 3 → **值得，作为家族 Phase 2**。一句话：**我们要这个能力，不要这个表面，契约必须硬。**

---

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — `Matches` 表面零破坏（新关键字）；`Is` 右侧加含义有潜在破坏（现有 `If x Is Foo()` 引用相等），是 A 表面的扣分项。
2. **保持 VB-like** — 模式方法是 `Try*` 血统的一等化，`Return True, ch:=...` 读起来像 VB；`If ShapeOf x Is hasAnotherChar(c)` 不像。
3. **不引入"第二种做事方式"** — 单次 `Try*` 场景与 `Out` 实参建议重复；本建议必须把自己定位在"组合场景"，否则违背。
4. **默认跟随 C#，除非有充分理由** — C# 用 `is T x` 模式；主线 VB 选 `Matches`；A 的 `ShapeOf...Is` 额外开道，无充分理由。
5. **读起来像英语、对新手友好** — "stream Matches hasAnotherChar(Out c)" 可读；"ShapeOf stream Is hasAnotherChar(c)" 拗口且 `Is` 误导。
6. **不为边缘场景加特性** — 判定+提取不是边缘（`Try*` 满世界都是）；但必须区分"少写两行"（Out 已够）与"可组合"（模式方法的真价值）。
7. **避免隐蔽控制流/语义变化** — `Is` 右侧语义重载接近隐蔽语义变化；`Matches` 显式、无此问题。
8. **不与既有语法冲突** — `Is` 重载是冲突面；`Matches`/`Out c` 无冲突。
9. **消除常见样板** — 判定与取值两段合一，正中靶心；但样板主要是被 Out 建议消掉的那部分。
10. **冗长只在有用时是美德** — `Out c` 是显式美德（声明了绑定）；`ShapeOf` 在模式方法场景是无用冗长。

**主线对照（评价标准 2.3 表）**：`Out` 参数/隐式声明 = 主线"讨论中"（2014.02.17 #42 原则批准）、Anthony "完整" → **主线一致**；`Select Case TypeOf` / 模式匹配 = 主线"最期待、分阶段"、ModVB `ShapeOf`+`Matches` → **主线一致（`Matches` 即 Anthony 提出）**；用户定义模式方法本身 = 主线 2014.02.17 的 MatchFunction **开放式未决问题**、2014.04.23 的 Match 操作符 **未决（"There were questions raised, heated discussions, and no agreed-upon answers"，事实）**，Anthony 把它系统化复活 → **主线一致但比主线激进**（主线止于"想要"，Anthony 给出文法）。与 2.3 表里"`Null` 字面量：主线保守，Anthony 激进"同类，但方向不冲突。

**Breaking change 结论**：D 表面零破坏；A 表面有 `Is` 右侧潜在行为改变与标识符级新关键字风险；隐式声明遮蔽规则需显式写死。

---

### RESOLUTION

1. **概念成立，方向正确。** "判定 + 提取"作为一等模式机制，是我们 2014.02.17 就许愿过（`Integer.TryParse`）、2014.04.23 认真谈过（Match 操作符）的东西，也是 VBScript `Try*` 血统的一等化。**用户定义模式方法是模式匹配家族 Phase 2 的成员**，排在声明模式、命名模式之后，且必须依赖 Out 实参/形参建议。

2. **我们不接受 A 的调用点表面 `If ShapeOf stream Is hasAnotherChar(c)`。**
   - `ShapeOf` 独立操作符已在上一场判 Table；
   - `Is` 右侧塞函数调用，撞 2018.12.19 的明示警告——"We think `Is` will have ambiguity issues with the existing use for reference equality"（事实）；
   - 函数调用 vs 模式调用的上下文判定，触碰 2014.02.17 "putting overload resolution into the late-binder, which we don't like"（事实）的同一根神经。
   - **改用 `Matches`**：`If stream Matches hasAnotherChar(Out c)`、`Case hasAnotherChar(Out c)`。

3. **契约必须硬（PROPOSAL B 的地盘）。** "任意 `As Boolean` + `Out` 函数即模式"的裸约定被拒。模式函数需显式标记（特性或修饰符），编译器校验：输入只读、所有路径给 `Out` 赋值、返回 `Boolean`。这是 2014.02.17 "each active pattern should be explicitly indicated with an attribute"（事实）的现代化实现。标记的具体形式与命名模式、家族文法统一收口。

4. **失败语义与守卫。** `Out` 形参入口默认初始化 + 未赋值路径警告（2014.02.17 #42 规则，事实）。守卫用 `When`（2018.12.19 "We like `When`"，事实），不建议正文的 `AndAlso c <> Nothing` 作为模式守卫惯用法——它对值类型 `Out` 无法区分"失败"与"成功但默认值"。

5. **主语映射与预检。** 主语隐式映射为模式函数第一形参；其余实参为 `Out` 绑定。主语为 `Nothing` 或与第一形参不匹配时不调用函数、直接不命中（2014.04.23 "it won't be called!" 规则，事实）。宽松模式下主语可为 `Object`，走运行期预检。`Suspect`：这些规则建议文档未写，作为规范草案交给家族统一文法。

6. **范围收敛：本建议不自立。** 单次 `Try*` 场景由 Out 实参建议覆盖；模式方法的边际价值在组合与递归解构。因此它必须与 `proposal-named-patterns.md` 合并为一个"用户定义模式"工作项，共享文法、契约与作用域规则。

### Implication

- 起草家族统一文法的 Pattern 形态表：声明模式（`x As T`）→ 命名模式（`ConstructorCall(kind, args)`）→ 用户定义模式方法（`hasAnotherChar(Out c)`），标注主语→第一形参映射规则。
- 与 Out 建议团队对表：确定 `Return True, ch:=...`、入口默认初始化、未赋值警告三条的最终规则；确认模式函数形参只允许 `Out`（禁 `ByRef` 读写混合）。
- 设计 `Pattern` 标记：特性 vs 修饰符、扩展模式函数（`Imports SyntaxPatterns` 批量引入，见命名模式建议）如何满足 2014.02.17 的 attribute 路线。
- 最小原型：`Matches` + 单/双 `Out` 模式函数 + `When`，验证语义模型、definite assignment、Option Strict 双路径与 IDE 补全。
- `TODO`（供提案返工）：文法/BNF、失败赋值保证、值类型陷阱、Null 主语、重载/泛型、`OrElse` 右侧警告、兼容性分析、主线 2014/2018 对照表。

---

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：主语与第一形参静态类型不匹配时——编译期报错还是运行期不命中？`Probably` 编译期报错（严格模式），需确认。
- `OPEN QUESTIONS`：裸 `hasAnotherChar(c)`（无 `Out` 关键字）的隐式声明规则——`c` 已存在时复用还是遮蔽？`Probably` 遮蔽（对齐 `For` 循环变量），需写进规范。
- `OPEN QUESTIONS`：模式函数重载解析的具体规则（2014.02.17 的 late-binder 担心在此是否重现）。
- `OPEN QUESTIONS`：`Pattern` 标记形式（特性/修饰符）与"模式函数是否允许 `Optional`/默认值参数"。
- `TODO`：量化"判定+提取"在真实 VBScript 代码里的占比，区分"少写两行"（Out 已够）与"必须组合"（模式方法）两档。
- `TODO`：家族统一文法草案中收口 2014.02.17 遗留的逗号/When/作用域问题。
- `Follow-up`：与 `proposal-json-pattern-matching.md` 对表——JSON 模式是否也按"模式函数 + 字面量形状"两条腿设计。
- `Follow-up`：与 `proposal-typeof-flow-analysis.md` 对表——`TypeOf ... Is` 流分析是否可复用于"主语预检通过"后的类型收窄。

---

### 状态

- **LDM 状态：本建议（独立）— Table**；分解后：概念与 `Matches` 表面并入家族 Phase 2 — Active（待家族统一文法）；`ShapeOf ... Is` 表面 — 沿用上一场判定（Table/Reject）；契约设计（`Pattern` 标记）— Active。
- **三态判定：Table（整体）** — 概念值得、表面语法与契约强度未达标、文档太薄，且必须与命名模式、Out 建议、家族文法合并；单独的"任意 Boolean 函数即模式 + `ShapeOf...Is`"建议会被 Reject。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-user-defined-pattern-methods.md`（用户定义模式方法）
- 来源：Anthony D. Green 原文第 7 章 "General Pattern Matching"（用户定义模式方法片段，`..\AnthonyDesign_wordpress.txt` L1327–1340）；依赖第 8 章 `Out` 实参/形参（`proposal-out-arguments.md`）
- 配方目标：让任意"带 `Out` 参数、返回 `Boolean`"的函数可作为模式，在 `If`/`Select Case` 中一次完成判定与取值，消除两段式样板

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 3/5 | 锚点≈"只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（判定+提取合一）、示例可演示；但核心场景（单次 `Try*`）被 Out 建议单独覆盖，边际价值只在组合场景；失败赋值、Null 主语、重载/泛型等关键子效果缺失；无原型、无量化数据 | 已提供 / 已检查（未运行） | 无原型封顶 4；主场景与 Out 建议重叠未识别；组合场景（真正的价值点）未展开 |
| 特性 | 3/5 | 锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Try*` 血统一等化是纯 VB 基因；但表面语法借自 Anthony 的 `ShapeOf...Is`（与引用相等 `Is` 冲突），"任意 Boolean 函数即模式"是无契约的杂质特性，与家族 `Matches` 方向分叉 | 已检查 | `Is` 重载冲突；无契约导致副作用模式混入；与 `Matches`/命名模式未对齐 |
| 品质 | 3/5 | 锚点≈"缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、未决问题 3 个且具体（1–3 健康区间）；但无文法/BNF、无角案例小节、无兼容性/Option Strict/semantic model 分析；主语→第一形参映射、失败保证、"`AndAlso` 守卫"的脆弱性均未定义 | 已检查 | 核心语义（主语映射、失败赋值、值类型陷阱）缺席；状态栏为占位链接（项目惯例，不计重） |
| 属性 | 3/5 | 锚点≈"有得有失——某维度受益、某维度受损，文档未充分权衡"。水（盘活 `Try*` 惯用法）、光（VBScript 差异化）正向；暗风险：`Is` 右侧语义重载（对现有代码潜在破坏）、无契约副作用、与家族文法分叉、与 Out 建议重叠——文档未识别前两者 | 已检查（预测待定） | 风=与家族 `Matches` 分叉未识别；暗=`Is` 重载与副作用契约；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源标注准确（第 7/8 章）；但未标注主线 2014.02.17 MatchFunction / 2014.04.23 Match 操作符这两个最直接先例（概念并非原创）；"任意函数即模式"借鉴 C# out vars / F# active pattern 未注明 | 已检查 | 主线 2014 先例未标注；与命名模式/Out 建议的交叉血缘未说明；无杂质但成分标注不全 |

### 设计原则对照
- **与 VB 基因**：主体一致（`Try*` 一等等化、样板消除、`Return True, ch:=` 可读）；偏离点 = `ShapeOf...Is` 表面（`Is` 重载，违背原则 7"隐蔽语义变化"、原则 8"与既有语法冲突"）、裸 Boolean 契约（违背原则 7 的隐蔽副作用风险）。
- **与主线关系**：**主线一致但比主线激进**——`Out` 参数是主线 2014.02.17 #42 原则批准的；模式匹配是主线"最期待、分阶段"；用户定义模式方法对应主线 2014.02.17 MatchFunction 未决问题与 2014.04.23 Match 操作符未决讨论，Anthony 给出完整文法。不属于与主线冲突，属于"主线推迟、Anthony 系统化"。
- **破坏性变更**：`Matches` 表面零破坏；`ShapeOf...Is` 表面有（`Is` 右侧新增含义、`Is` 右侧现有引用相等调用的重解析、新关键字标识符冲突）；隐式声明遮蔽规则若不写死会造成行为歧义。

### 总评
- **达成程度**：**部分达成** — 概念（判定+提取的一等模式机制）成立且被主线历史印证；表面语法与契约强度不达标；价值定位（组合场景 vs 单次 `Try*`）未收敛；必须并入家族文法。
- **LDM 三态建议**：整体 **Table**；分解后——概念 + `Matches` 表面 + `Pattern` 契约 → **Active（家族 Phase 2）**，`ShapeOf...Is` 表面 → **Reject**（沿用家族判定），单 `Out` 限缩（PROPOSAL C）→ **Table**。
- **主要问题**：① 表面语法撞 `Is` 歧义且与家族 `Matches` 分叉；② 契约松散，副作用模式可混入；③ 价值与 Out 实参建议重叠，边际价值未定性；④ 主语映射、失败赋值、值类型陷阱等核心语义缺席；⑤ 未标注主线 2014 先例。

### 返工建议
- **补充章节**：`Grammar/BNF`（模式方法作为家族 Pattern 形态 + 主语→第一形参映射）；`Edge cases`（失败赋值保证、值类型 `Out` 陷阱、Null 主语、主语类型不匹配、重载/泛型、`OrElse` 右侧警告、重复 Out 名遮蔽）；`Contract` 设计（`Pattern` 标记、编译器静态校验、禁止 `ByRef` 读写混合）；兼容性分析（`Is` 右侧重解析、新关键字、隐式声明遮蔽规则）；`Option Strict` 双路径验证。
- **补充证据**：最小原型（`Matches` + 单/双 `Out` + `When`）；与"Out 实参单独落地"的场景对比表（证明组合场景的边际价值）；主线 2014.02.17 MatchFunction / 2014.04.23 Match 操作符 / 2018.12.19 `Matches` 对照表；"判定+提取"真实占比数据。
- **未决问题处理**：把现有 3 个未决问题升级为规范小节；新增：主语映射边界、`Pattern` 标记形式、重载解析规则、与命名模式合并后的作用域/definite assignment 统一规则。
- **设计探索**：与命名模式合并为单一"用户定义模式"工作项；`Pattern` 特性与扩展模式函数（`Imports SyntaxPatterns`）的组合；`TypeOf...Is` 流分析（`proposal-typeof-flow-analysis.md`）复用为模式主语预检后的类型收窄。

---

## 附录：C# 生态与互操作考量

> 本附录面向 VBScript.NET（.vbx）必须适应 C#/CLR/.NET 现实的问题。主题对应物：**用户定义模式 / 可编程的"判定+提取"**。C# 原文均逐字核对，来源相对 `..\..\csharplang`；无法核实处标 **Suspect** / **OPEN QUESTIONS**。

### 相关 C# 现实方向

1. **C# 模式匹配只认内建形状，无用户定义/active patterns。** 从 C# 8 recursive patterns（`proposals\csharp-8.0\patterns.md`）到 C# 11 list patterns（`proposals\csharp-11.0\list-patterns.md`），可扩展点全部是**固定命名约定**：位置模式经 `Deconstruct` 方法——原文："a method is selected by searching in *type* for accessible declarations of `Deconstruct` and selecting one among them using the same rules as for the deconstruction declaration"（`proposals\csharp-8.0\patterns.md`，事实）；列表模式经 `Length`/`Count` + 索引器、`Range` 索引器/`Slice`——原文："Lets you to match an array or a list with a sequence of patterns e.g. `array is [1, 2, 3]` will match an integer array of the length three with 1, 2, 3 as its elements, respectively"（`proposals\csharp-11.0\list-patterns.md`，Summary，事实）。用户代码**无法新增"模式形状"**——这正是 C# 与"任意 Boolean 函数即模式"的根本分歧点。

2. **active patterns 被 C# 多次讨论但始终推迟。**
   - 2015 起步设想（`meetings\2015\LDM-2015-01-21.md`，事实）："In addition it is worth considering something along the lines of 'active patterns', where a type can specify logic to determine whether a pattern applies to it or not." 其示例与本提案形状几乎同构：
     ```csharp
     class Point {
         public Point(int x, int y) {...}
         void Deconstruct(out int x, out int y) { ... }
         static bool Match(Point p, out int x, out int y) ...
         static bool Match(JObject json, out int x, out int y) ...
     }
     ```
   - 2016 明确"此轮不做"（`meetings\2016\LDM-2016-04-12-22.md`，事实）："There's a proposal where one type gets to specify deconstruction semantics for another, along even with logic to determine whether the pattern applies or not. We do not plan to support that in the first go-around…" 并预言"we could reasonably rely on our future selves to invent a separate specification mechanism for active patterns without us having to accommodate it now"。
   - 2022 triage 再次确认 backlog（`meetings\2022\LDM-2022-02-16.md`，事实）："User-defined patterns, also known as active patterns in F#, are extremely powerful, but when designing them we need to be sure we're not painting ourselves into a design corner for other future pattern enhancements. There are also some interesting questions we will have to answer around exhaustiveness." 结论："Into the backlog, for after we finish the current pattern features." 同一段还把 active patterns 称为"one of the last pattern-related things that need to be added to the pattern feature to make it generally 'complete'"。
   - 2022-02-23 讨论 `Span<char>` 常量模式时，再次把该场景指向 active patterns（`meetings\2022\LDM-2022-02-23.md`，事实）："and instead the scenario should wait for [active patterns](https://github.com/dotnet/csharplang/issues/1047)."
   - 结论：C# 把用户定义模式明确排在 unions/closed hierarchies 之后，且反复因穷尽性与"设计死角"担心而推迟——**至今无 active patterns 落地**。

3. **C# 15 unions 的"模式成员约定"与本提案形状最接近，但它是闭集约定。** `proposals\unions.md` 定义 `[Union]` 类型的编译器识别形状：`Value` 属性 + `HasValue`/`TryGetValue` 非装箱访问模式——原文："Union matching: Pattern matching against union values implicitly "unwraps" their contents, applying the pattern to the underlying value instead"；"A `TryGetValue` method for each case type. The method returns `bool` and takes a single out-parameter of a type that is identity-convertible to either the case type or optionally the underlying value type, if the case type is a nullable value type"（`proposals\unions.md`，事实）。这本质就是"Boolean + Out 即模式"——**但只认 `UnionAttribute`/`IUnion`/`Value`/`HasValue`/`TryGetValue` 这些固定名字，不是任意函数**；对"坏 API"的决议是原文"The compiler should silently ignore such APIs without reporting errors or diagnostics, aligning with prior art for optimization scenarios"（`proposals\unions.md`，Resolved，事实）。C# 15 Kickoff（`meetings\2025\LDM-2025-08-18.md`，Unions 段，事实）称："We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area." 用户定义模式不在其 C# 15 议程上。

4. **穷尽性（exhaustiveness）是 C# 模式的核心价值，与用户定义模式存在张力。** unions 的卖点正是"switch 可证明穷尽"（`proposals\unions.md` Union exhaustiveness 段）；LDM-2022-02-16 推迟 active patterns 的明确理由之一就是 "there are also some interesting questions we will have to answer around exhaustiveness"（事实）。

### 现实 vs 提案

- **兼容（概念层）**：C# 的 `Deconstruct`、list patterns、union `TryGetValue` 与本提案共享同一核心直觉——"判定 + 提取"。C# 从未否定"Boolean+Out 函数当模式"这一形态（2015 年 `static bool Match(Point p, out int x, out int y)` 设想几乎就是本提案），只是因穷尽性与设计死角担心而推迟。方向不冲突。
- **需桥接（机制层）**：C# 选"编译器识别的固定约定成员"（`Deconstruct`/`TryGetValue`/`HasValue`/`Value`/`Length`+索引器），VB 选"任意函数 + 显式 `Pattern` 标记"。跨语言时两条路必须桥接：VBScript.NET 编译器应能**消费** C# 的形状成员；而 VB 的 `Pattern` 标记在 C# 侧无对应物（C# 不认识），反向桥只能靠 source-gen（见适应建议）。
- **脱节（方向层，非冲突）**：VB 不承诺 C# 式穷尽性（`Select Case` 无穷尽性分析传统），因此"任意 `Pattern` 函数即模式"在 VB 内不破坏任何既有保证；C# 则因穷尽性而无法接受同一形态。这是**有意的语言哲学分叉**，不是互操作冲突。同理，RESOLUTION 判掉 `ShapeOf...Is` 表面与 C# 用 `is` 类型模式的走向一致——C# 也从不在 `is` 右侧接受任意函数调用。
- **对 RESOLUTION 的验证**：C# 三次推迟 active patterns（2015→2016→2022）恰好支持我们"契约必须硬、表面归家族文法"的结论。若采纳 PROPOSAL A 的裸约定，等于在 C# 因同样的理由（"painting ourselves into a design corner" + 穷尽性）拒绝/推迟的地方，用更松的契约走更远——这在互操作上尤其危险，因为副作用模式一旦进入公开 API，C# 侧无任何约定可识别。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：模式函数自身强类型；主语在 `Option Strict Off` 下可为 `Object` 走运行期预检（本提案 OPEN QUESTIONS 已有此设计）。与决策文件 M2/M8 的"默认安全、按需动态"双模路线一致。
- **识别新元数据（必须桥接）**：C# 15 unions 将带来 `System.Runtime.CompilerServices.UnionAttribute`、`IUnion`、可能的 `CompilerFeatureRequired` 门控，unsafe-evolution 另有 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`——后者对 VB 的表述见 `proposals\unsafe-evolution.md`（VB 段，事实）："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." VBScript.NET 编译器若想在 `If Matches`/`Select Case` 中消费 C# union 类型，必须识别这些成员形状，否则 C# 的 `[Union]` 类型在 VB 侧只是不透明 struct，模式匹配无从谈起。
- **source-gen 桥**：C# 无用户定义模式，VB 的 `Pattern` 标记在 C# 侧不可见。互操作出口有二：(1) 消费向——VB 编译器原生识别 C# 的 `Deconstruct`/`TryGetValue`/`HasValue`/`Value` 约定形状，其中 `TryGetValue` 与本提案的模式函数形状（Boolean + Out）**几乎同构**，可零成本映射；(2) 生产向——source generator 把 VB 模式方法展开为 C# 可调用的约定成员（生成 `Deconstruct`/`TryGetValue` 形成员），让 C# 至少能调用底层函数。与索引 T6（编译期 source-gen 替代运行时反射）同向。
- **转换规则借鉴**：C# union 对 `TryGetValue` 匹配限定了原文"Only implicit identity, or reference, or boxing conversions are considered"（`proposals\unions.md`，Resolved，事实）。本提案 OPEN QUESTIONS 里"主语与第一形参类型不匹配"的判定可借鉴此规则收窄。
- **`[Out]` 契约强度直接关系到 C# 安全模型**：unsafe-evolution 原文警告——"Since for example VB doesn't guarantee that `[Out]` parameters are initialized, in combination with `[SkipLocalsInit]`, calling such parameters could be considered `unsafe` in C#"（`proposals\unsafe-evolution.md`，§`[Out]` and `[SkipLocalsInit]`，事实）。本提案 RESOLUTION 4 采纳"`Out` 形参入口默认初始化 + 未赋值路径警告"（2014.02.17 #42 规则），恰好把 VB 模式方法的 `Out` 契约强化到 C# 可接受的程度——这是跨语言安全的一个隐性收益，应在规范草案中显式声明"模式函数 `Out` 入口必初始化"以保证与 C# `[Out]` 语义互通。

### 对既有 RESOLUTION/三态判定的影响

- **无推翻，有加强**。C# 三次推迟 active patterns（2015/2016/2022）从生态侧印证了 RESOLUTION 2（不接受 A 的裸表面）与 RESOLUTION 3（契约必须硬）。本附录不改变三态判定（整体 Table；概念 + `Matches` 表面 + `Pattern` 契约 Active / 家族 Phase 2；`ShapeOf...Is` 表面 Reject）。
- **补充一条互操作设计约束**：`Pattern` 标记的编译器识别形状建议与 C# union 的 `TryGetValue(out T)` 语义对齐（单一 Out、返回 Boolean、转换限定为 identity/reference/boxing），以便 C# 侧 union 类型可直接映射为 VB 模式函数。这不改变判定，只给出规范草案的互操作边界。
- **脱节点声明**：VB 不追求穷尽性，因此不必跟随 C# 对用户定义模式的排斥；这是"VB 特色"的正当面，与决策文件 M4（type-predicates / shapeof-pattern-matching 与 unions 同向）一致。

### 引用纪律

本附录引用的 C# 原文均已逐字核对，来源相对 `..\..\csharplang`：

- `meetings\2015\LDM-2015-01-21.md`（Patterns / active patterns 设想）
- `meetings\2016\LDM-2016-04-12-22.md`（Deconstruction 探索 / "Growing up to active patterns"）
- `meetings\2022\LDM-2022-02-16.md`（Triage—User-defined positional patterns，#4131）
- `meetings\2022\LDM-2022-02-23.md`（`Span<char>` 常量模式，指向 issue #1047）
- `meetings\2025\LDM-2025-08-18.md`（C# 15 Kickoff—Unions）
- `proposals\unions.md`（Union matching / `TryGetValue` / Resolved 决议多条）
- `proposals\csharp-8.0\patterns.md`（`Deconstruct` 选择规则）
- `proposals\csharp-11.0\list-patterns.md`（Summary）
- `proposals\unsafe-evolution.md`（VB 段；§`[Out]` and `[SkipLocalsInit]`）

**OPEN QUESTIONS / Suspect**：

- **Suspect**：C# active patterns 的 champion issue #1047 的**当前状态**（本库只有 LDM 引用、无 proposal 正文，需在 dotnet/csharplang issues 核实；`proposals\unions.md` 对 `TryGetValue` 的转换限定等规则仍可能演进）。
- **Suspect**：`proposals\closed-hierarchies.md` 的穷尽性原文措辞（本索引未深挖，本文未直接引用其正文）。
- **OPEN QUESTIONS**：unions 是否进入 C# 15 正式版（2025-08-18 仅称 "at least preview versions"，事实；正式状态需后续核实）。
