# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-named-pattern-inputs.md`（Anthony 原文第 18 章 18.12 "Named pattern inputs"）。按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期"，且"It is perfectly fine for work to happen on inactive or rejected proposals, and for them to be resurrected later."。所以今天会议的真正问题是：**这份建议值得激活、继续搁置，还是把它拆开、留下能用的部分？**

这场会议的起点与上一场命名模式会议直接相连。上一场我们裁决了 `proposal-named-patterns.md`，其中指出：命名模式示例里的 `IdentifierName("New")` 静默依赖一个**从未被定义的机制**——把字符串 `"New"` 作为**输入**传给模式——而这份兄弟建议（即今天的议题）恰好是那个被依赖、却只有一句话的"调查指针"。所以今天不是评审一份新设计，而是**给上一场悬置的依赖补一场会**。

We opened with the sentence that is the entire substance of the proposal：`"Some hypothetical patterns would benefit from additional 'inputs' (e.g. Regex("\d+", n)) but currently the 'argument' list for a named pattern is output only. This should be investigated."`——一句话，没有语法、没有示例以外的任何设计。我们会认真对待它，因为缺口真实；但我们也得诚实：**这份建议自己没有任何可以采纳的东西，我们真正要裁决的是"缺口本身该怎么拆"。**

_诚实分层：正文区分 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。引用的主线会议引文与 issue 编号均与 `..\..\..\vblang` 原文逐字一致；标注为 `Suspect` 的判断是基于设计原则与评价标准的独立推理，非主线原文。_

## Agenda

* [ModVB Proposal — Named Pattern Inputs / 命名模式输入](#modvb-proposal--named-pattern-inputs--命名模式输入)

## ModVB Proposal — Named Pattern Inputs / 命名模式输入

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；[vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)；[vblang #124 – Pattern matching](https://github.com/dotnet/vblang/issues/124)；ModVB：`meeting-named-patterns`（上一场决议）、`proposal-user-defined-pattern-methods`（函数即模式概念）、`proposal-out-arguments`、`proposal-string-pattern-matching`、`proposal-shapeof-pattern-matching`_

### 场景与缺口

今天的命名模式把"模式即函数"建模为：带 `Out` 参数、返回 `Boolean` 的函数，其"实参"列表只承载**输出**（把子值绑定到调用方变量）。缺口有两处，且我们一开场就注意到它们是**两个不同的东西**：

**缺口一：值输入（参数语义）。** `Regex("\d+", n)` 需要一个正则字符串作为模式的输入，匹配出的值才是输出。这是"把值喂给模式函数"，行为由该值决定——像一个**普通参数**。

```vb
' 设想的调用形态（原文摘录）："\d+" 是输入（正则表达式），n 是输出（匹配结果）。
Select Case ShapeOf text
    Case Regex("\d+", n)
        ' 命中：text 匹配正则 "\d+"，n 绑定第一个捕获组（或整个匹配）。
End Select
```

**缺口二：常量槽位（约束语义）。** 上一场命名模式会议自己的示例 `IdentifierName("New")` 需要把 `"New"` 放在叶子槽位里，要求"该槽位的值等于 `"New"`"——这是**约束**（等值），不是喂值。命名模式建议原文把它当作"最内层只能绑定叶子值"的未决问题留了下来：

```vb
Case MemberAccess(MeExpression(), IdentifierName("New"))
    ' 若按函数调用语义，"New" 不是 lvalue，无法绑定 Out 参数 → 非法。
    ' 若按常量槽位语义，它是对 IdentifierName 的"名字 = New" 的等值约束。
```

我们把这两个缺口分开，是因为它们的**机制、先例与代价完全不同**（见候选方案）。把它们统称"输入"正是这份建议的混写点。

缺口是真实且**承重的**：命名模式会议已决议"函数即模式"概念原则上采纳（家族 Phase 2+），而值输入是这个概念的自然延伸；常量槽位则是上一场**没有**采纳的工厂镜像语法（Table）的组成部分。所以本建议横跨一条已激活的边界与一条已搁置的边界——这正是我们建议拆开它的第一个理由。

### 候选方案

**PROPOSAL A — 签名驱动值输入（普通形参 = 输入）。** 模式函数签名允许普通（非 `Out`）参数，调用点实参按签名**位置**绑定；落在普通形参上的实参是输入，落在 `Out` 形参上的实参是输出绑定。不引入新关键字：

```vb
Function RegexMatch(subject As String,
                    pattern As String,          ' 输入：普通形参
                    Out n As String) As Boolean ' 输出：Out 形参
    Dim m = System.Text.RegularExpressions.Regex.Match(subject, pattern)
    If m.Success Then
        Return True, n:=m.Value
    Else
        Return False, n:=Nothing
    End If
End Function

' 调用点：主语隐式来自上下文，"\d+" → pattern，n → Out n。
If ShapeOf text Is RegexMatch("\d+", n) Then
    ProcessMatch(n)
End If
```

**PROPOSAL B — 显式 `In` 关键字（与 `Out` 对称）。** 调用点用 `In` 标注输入实参：`RegexMatch(In "\d+", n)`。读者无需查签名即可区分输入与输出；与 `Out` 实参建议（`Out value`）形成镜像。代价是仪式，且与既有"调用点不需要关键字即可匹配 Out 形参"的规则不对称。

**PROPOSAL C — 子模式文法（C# 式）。** 每个槽位是一个**子模式**：常量字面量 = 等值约束（输入），裸标识符 = 变量绑定（输出），嵌套调用 = 递归。`IdentifierName("New")` 的 `"New"` 是常量模式，`Regex("\d+", n)` 的 `"\d+"` 是常量模式、`n` 是绑定。这是主线 2018.12.19 文法已经画出的方向（`Expression` 模式做等值、`'Like' StringExpression` 做字符串模式），但它是**文法**，不是**函数调用**——与"模式是函数"的契约正面冲突。

**PROPOSAL D — 常量槽位特判（最小方案）。** 在 A 之上加一条规则：编译期常量落在 `Out` 槽位时，解释为"绑定临时变量 + 等值约束"（等价于绑 `id` 后接 `When id = "New"` 的糖）。只解决 `IdentifierName("New")`，不解决运行期值输入。

**PROPOSAL E — 什么都不做。** 实参列表保持只输出。命名模式示例改写为绑变量 + `When` 护栏；Regex 模式用 `Regex.IsMatch` + 捕获变量或闭包。`"We're proud not to do anything"`（2014-02-17 对表达式体成员的原话，此处适用）。

### 权衡：Q&A

- **A vs B：位置绑定还是关键字标注？** 2018.05.30 我们审过 `#305 In and Out operators`，对 **`In` 作为二元操作符**的裁定是："This does not seem to have much value - is not significantly more expressive even if shorter - than .Contains against the list. Not moving forward for this reason."——那是 `x In list`，不是实参修饰符，先例不直接适用。但同一次审查对 `Out` 说："This is similar to Pattern Matching and Out variables in C#. Out variables are probably covered better in #60 and pattern matching by #124"，即 Out 的真正归宿是模式匹配。而 2014-02-17 #42 对 Out 实参有一条向后兼容裁定："for back-compat, you don't need an 'Out/Output' keyword to match an out parameter."——调用点匹配 Out 形参**本来就允许不写字**。若输入要求必写 `In`，则"输出可省略、输入必写"，不对称；若输入也可省略，则 `In` 只是可选装饰，A 的签名驱动已经是它的默认行为。**结论：B 是 A 的可选注记，不是独立方案。** 签名帮助能替代它，B 的独立价值很薄。

- **A vs C：函数模型还是文法模型？** 这是全场最深的分歧。C 是主线的方向（2018.12.19 文法已含等值 `Expression` 模式与 `'Like' StringExpression` 模式），且 2018.12.19 有一条我们反复引用的判据——"Think ahead so the initial design doesn't box us away from things we might want later"——C 让我们永不偏离主线的子模式文法。但我们的上一场决议明确采纳了**函数模型**（"模式是带 Out 参数、返回 Boolean 的函数"），并搁置了镜像文法。在函数模型里，A 是自然的：输入就是普通形参，编译器按签名解析，无需任何新文法。**两条路都说得通，但我们不能两条都占**——见 Q8。我们 `Suspect`：C 的完整形态要等主线模式文法（Phase 2 递归模式）落地后才谈得上，今天选 C 等于替未来做主。

- **值输入 vs `When`：更简替代在哪里？** 主线的 `When` 护栏 + 捕获变量已经能覆盖大多数"带条件的匹配"：

```vb
' 更简替代：不用任何"输入"，用 When + 捕获变量。
If ShapeOf text Is String AndAlso
   System.Text.RegularExpressions.Regex.IsMatch(text, "\d+") Then
End If
```

那值输入买到了什么？——**单次求值 + 封装复用**。`When Regex.IsMatch(...)` 只做判定，拿捕获组还要再 `Regex.Match` 一次；一个 `RegexMatch` 模式函数把"判定 + 提取捕获组"一次做完，且能像普通函数一样命名、复用、测试。这不是可有可无的糖，是"函数即模式"契约的自然补全。**但它的价值只随函数模型落地而兑现**，单独激活没有载体。

- **常量槽位的大小写语义是什么？** `IdentifierName("New")` 比较的是标识符文本。VB 标识符绑定大小写不敏感，`"New"` 与 `"new"` 应为同一名字；但 VB 字符串相等还受 `Option Compare`（Binary/Text）影响。语法 kind 的常量比较应跟随**标识符语义**（大小写不敏感）还是数据字符串语义（Option Compare）？`Suspect`：应走标识符语义——它是语法形状的描述，不是数据比较；但必须写进 spec，且 2018.12.19 那句 "C# restrictions regarding constant expressions may not be appropriate" 暗示我们**有权比 C# 更宽松**。这条不决，常量槽位不可设计。

- **常量槽位是谁的负担？** 常量槽位只在**工厂镜像语法**（`Case ExpressionStatement(...)` 这种嵌套形状）里出现；而工厂镜像语法在上一场已 Table。若今天把常量槽位单独激活，等于为一个搁置的特性设计配件。**这是我们把 D 也搁置、与镜像语法绑定的核心理由。**

- **A 与 C 的"未来语法毒化"。** `Regex("\d+", n)` 在 A 下是"函数调用 + 签名解析"，在 C 下是"子模式 + 常量/绑定"。同一字形、两种语义。若先实现 A，日后想转 C，`Regex("\d+", n)` 的含义会**重解释**——2018.05.30 对 `Return?` 说的 "Control flow would be altered by a very subtle character" 所警惕的那类事情，虽然这里不是控制流、是绑定语义。我们要求：**值输入（A）必须明确自己不承诺子模式文法的未来**，并在 spec 里写清"本语法与 C# 位置模式的字形冲突待主线解决"。

- **与 `Like` 先例的对照。** VB 从 VB6 就有 `Like` 操作符（`"abcd" Like "a*d"`），它的 RHS 就是一个字符串模式输入。2018.12.19 文法把 `'Like' StringExpression` 列为候选模式（"Like pattern"）。这说明"模式接收一个字符串输入"在 VB 基因里**有先例、且主线已看到**。`Regex("\d+", n)` 本质是"Like 的正则版 + 一个绑定"——先例在，但 Like 模式不绑定捕获值，`RegexMatch` 需要的是 Like 模式 + Out 的组合，这仍要靠函数模型。

- **`#304` 的归属提醒。** 2018.05.30 对 `#304 Select TypeOf` 的裁定是 "Will consider part of pattern matching."——主线把模式相关的一切都收进模式匹配工作项。本建议无论哪个 PROPOSAL 都不是独立主线特性，而是家族 Phase 2+ 的附件。**孤立激活它，就是让 ModVB 抢在主线模式文法之前造第二个文法。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

A（签名驱动）把歧义降到最低：`Case RegexMatch("\d+", n)` 的实参按 `RegexMatch` 的签名解析，`"\d+"` 匹配普通形参 `pattern`、`n` 匹配 `Out n`，无需新文法。但有一个残余歧义：**字面量落在 `Out` 槽位**怎么办？`RegexMatch("\d+", "\d+")`——第二个 `"\d+"` 想匹配 `Out n`，它不是 lvalue，函数调用语义下是错误；D 引入的"常量槽位 = 等值约束"只在**镜像语法**（工厂形参即槽位）下有意义。若 A 与 D 并存，同一句法（字面量实参）在"模式函数调用"与"工厂镜像"两种语境下语义不同，解析器必须靠"该名字是否是模式/工厂"来判定——这又把决定权交给绑定阶段，2014-02-17 我们明确不喜欢："It suggests putting overload resolution into the late-binder, which we don't like."

`Case RegexMatch(...)` 今天已是合法代码（等值 Case，见上一场），本建议不新增歧义，但**继承**了命名模式会议对"Case 函数调用"语义改写的既有担忧。

#### 2. 角案例与边界语义

- **运行期输入。** `RegexMatch(patternVariable, n)` 中 `patternVariable` 是普通变量——A 下它是普通实参，每次模式求值读取一次。必须规定求值顺序：输入在 `Out` 赋值**之前**求值，且只求值一次（`Suspect`：编译器应引入临时变量缓存输入表达式，避免副作用重复）。
- **`Nothing` 主语。** `Select Case ShapeOf Nothing` 时模式不命中（`Probably`，沿袭命名模式会议结论）；输入实参在主语为 `Nothing` 时是否仍求值？——应不命中即不求值（短路），需显式规范。
- **常量槽位的大小写与 `Option Compare`。** 见 Q&A；OPEN。
- **多次出现的常量。** `IdentifierName("New")` 与 `IdentifierName("new")` 是否同一约束？按标识符语义应是；按 `Option Compare Binary` 不是。两条路都得选。
- **值输入与"裸标识符重复"的交互。** 若输入形参接收一个**变量**（`RegexMatch(regexVar, n)`），它和"裸标识符重复 = 约束相等还是新建变量"（命名模式会议 OPEN QUESTION）共享字形空间——`regexVar` 是输入实参（读既有变量）还是输出绑定（隐式声明新变量）？在 A 下由签名决定（普通形参 = 读、Out = 写），干净；但**读者**仍需看签名才知道。IDE 责任（见 #7）。
- **输入是类型检查吗？** `RegexMatch(As Integer, n)` 这种带类型的输入槽位——是否允许把输入限制为某类型再喂给函数？A 下没必要：形参类型已经约束。C 下是自然属性。这是两条模型的又一裂缝。
- **嵌套深度。** 2018.12.19 对嵌套模式的原话："What does 'nested patterns' mean? Maybe not V1"——值输入不依赖嵌套（`RegexMatch` 是平的）；常量槽位依赖镜像嵌套。深度限制仍 OPEN。

#### 3. 作用域与绑定

- **输出绑定作用域。** 沿命名模式会议结论：`Case` 内输出变量作用域 = 该 Case 子句块（2014-02-17 Design1 先例："The scope of the variable declaration is just that case clause"）；`If` 形态 = 外围块（2018.12.19 "The scope of introduced variables is the surrounding scope"）。
- **输入实参不是绑定。** 输入实参是既有变量的读取，不引入新符号；语义模型 `GetTypeInfo` 返回形参类型，不返回新局部变量。若输入是字面量，它无符号。
- **definite assignment。** 输出变量在命中时已赋值、未命中时未赋值——2018.12.19 的原则："We don't usually let variables be used before they are declared, so it would look quite weird to allow this in one case (`DoBottomLoopStatement`)"，必须在 `Else` 分支使用输出变量时报错。输入无此问题。
- **命名实参。** 模式调用是否允许命名实参（`RegexMatch(pattern:="\d+", n:=x)`）？2017.08.09 我们刚处理过非尾部命名实参（对晚绑定调用报错）；A 下命名实参与普通调用一致即可，但"命名实参落在 Out 形参上"的隐式声明语义要一次性定清。

#### 4. 与既有特性的交互

- **`When`。** 输入与 `When` 正交：`Case RegexMatch("\d+", n) When n.Length > 3`——输入进模式函数，护栏在外面。这是最干净的组合，也是"更简替代"论证的落点。
- **`Like` 模式（主线）。** 若主线 `'Like' StringExpression` 落地，字符串模式输入有了第一等机制；`RegexMatch` 变成"Like + Out 捕获"的函数封装。**ModVB 不得在主线 Like 模式之外再造一个平行的字符串输入语法**（原则 #3）。
- **`Expression` 模式（主线）。** 常量槽位（D）的语义与主线 `Expression` 模式（等值测试）完全重叠——D 就是"把等值测试放到镜像语法里"。**应复用主线的等值语义，而不是发明第二套"常量 = 约束"规则。**
- **Out 实参建议。** A 完全建立在 `Out` 形参建议之上；`Return True, n:=...` 的多值返回是模式函数的载体。值输入不新增对 Out 的要求，只是允许形参列表里出现普通形参——这是 Out 建议的自然推广，**应与 Out 建议一次定清**，否则 `Return True, pattern:=..., n:=...`（同时回写输入与输出）的语义会打架。
- **TypeOf 流分析。** 值输入不触碰类型收窄；但 `If ShapeOf text Is RegexMatch("\d+", n)` 若命中，`text` 是否收窄为 `String`？——模式函数返回 `Boolean` 不携带类型事实，除非模式函数显式声明"命中即 `String`"。这是**函数模型固有的类型事实缺失**，跨建议记录，不在本建议解决。
- **插值字符串模式。** `proposal-string-pattern-matching` 用插值字符串做命令分发，Drawbacks 承认"与正则表达式相比表达能力有限"；`RegexMatch` 正是那个正则替代。两条建议服务同一场景，需对齐，避免重复。
- **Late binding / Option Strict Off。** 宽松模式下 `text` 可能是 `Object`；模式函数形参 `subject As String` 需要隐式收窄转换。**规则：模式匹配是编译期构造，主语做运行期 `isinst`，两条 Option 路径行为一致**（沿流分析会议立场），但宽松下 `Object` 主语能否隐式传给 `String` 形参，未定——`Suspect`：VBScript.NET 默认宽松，这条不解决不能进 1.0。

#### 5. Breaking change 与兼容性

**今天零破坏。** 模式语法尚未落地，`RegexMatch("\d+", n)` 不可能是既有合法代码（`RegexMatch` 若作为函数调用，`"\d+"` 作普通实参、`n` 作普通实参，是有含义的既有代码——**这是一个真实的破坏面**：若一个既有函数就叫 `RegexMatch`，把它的调用重解释为模式调用会改变语义。命名模式会议已记录"Case 函数调用"的重解释风险，这里同样适用）。**关键是未来**：A 与 C 的字形冲突（Q8）若不写清，未来转向子模式文法就是重编译行为变化——2018.06.13 "We will almost never make breaking changes to Visual Basic" 的长期压力落在设计选择上，不在今天的语法上。

#### 6. Option Strict / 编译选项分叉

输入实参在严格模式必须与普通形参类型一致或可隐式转换；宽松模式允许宽收窄。输出绑定两路径一致。**一致性义务**：值输入在两个 Option 下，模式函数的调用语义逐字等价于"同名普通函数调用 + Out 绑定"——这是 A 的验收定义。建议未给任何分析。

#### 7. IDE / IntelliSense 影响

- 签名帮助必须渲染模式函数形参，并区分普通形参（输入）与 `Out` 形参（输出）；输出实参在补全中显示为"隐式声明"。
- 常量槽位（若 D 复活）需要新的错误文案（"字面量槽位 = 等值约束"的提示），且 InfoTip 要解释比较语义（大小写不敏感 vs Option Compare）。
- 输入实参是普通表达式，IDE 无需特殊处理；难点全在输出侧（隐式声明、definite assignment 标记）。

#### 8. 数据 / 普遍性

- 正则分发是**真实且高频**的业务场景（日志解析、输入校验、路由），尤其符合脚本风格——这是值输入最强的普遍性证据，也是它面向 VBScript.NET 的价值点。`Suspect`：占比可量化，但**没有数据**，且"正则分发"今天已被 `Regex.IsMatch` + `When` 覆盖 90%，值输入买的是"单次求值 + 封装复用"，不是"能否表达"。
- 常量槽位只为语法树/工厂镜像场景服务——上一场已认定这是"编译器与工具链作者"的窄受众，不是"数十万安静客户"（2018.05.30："We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now."）。
- 两份场景的数据普遍性都不达标，但值输入离达标更近。

#### 9. 更简替代

- **`When` + 捕获变量 + `Regex.IsMatch`**：覆盖"带条件的匹配"，不解决"单次求值提取捕获组"。最强的竞争者。
- **闭包/上下文捕获**（建议原文的 Alternatives）：`Function() Using r = New Regex("\d+") ...` 把输入捕获进闭包，模式函数无输入形参。可行，但每次匹配要重建闭包，且模式函数失去"显式形参即文档"。
- **主线 `Like` 模式 + `Expression` 模式**：常量槽位和字符串输入的语义主线已画出，ModVB 直接沿用即可，不必独立发明。
- **Analyzer / 生成器**：为 `SyntaxFactory` 生成模式函数（上一场已有此替代路线），常量槽位可以由生成器展开为等值判断，不占语言表面。

#### 10. 成本 / 优先级

- A（值输入）成本**极低**：在 Out 形参建议的模型里，"允许普通形参"近乎免费，编译器按签名解析即可；没有新文法、没有新关键字。它是 Out 建议的一个段落，不是一个特性。
- D（常量槽位）成本**中**：需要"字面量在槽位 = 等值约束"的规则、比较语义、definite assignment 的约束形态，且只服务于镜像语法（Table'd）。
- C（子模式文法）成本**高**：等于提前实现主线的递归/子模式文法，且与函数模型冲突。**不值得 ModVB 独立做。**
- 优先级排序：值输入随用户定义模式方法（Phase 2+）；常量槽位随镜像语法；子模式文法不排期。

#### 11. 运行时 / CLR 硬约束

无。A 的模式函数是普通方法调用 + 布尔判断 + 输出赋值，不触达 PEVerify；常量槽位（D）若落地，下略为 `isinst` + 等值比较。零反射依赖是函数模型的硬红线（2014-02-17 对开放泛型："this is impossible in the current CLR without reflection"），A 天然满足。

#### 12. 值不值得做

逐维打分：**值输入（A）** 价值 5（业务场景真实、单次求值+封装复用）/ 成本 1（近乎免费）/ 风险 2（字形冲突需声明）——**值得，但它不是一个独立特性，是 Out + 函数模型的一个段落**。**常量槽位（D）** 价值 3（只为语法树场景）/ 成本 4（规则+比较语义）/ 风险 3（与主线 Expression 模式重叠）——**不值得现在做，等镜像语法**。**子模式文法（C）** 价值 4 / 成本 7 / 风险 6——**主线的事，ModVB 不做**。一句话：**缺口的"值输入"半边是免费的顺水推舟，"常量槽位"半边是搁置特性的配件，建议本身没有可以独立激活的"特性"实体。**

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 今天零破坏（模式未落地）；但 A 与 C 的字形冲突制造未来重解释风险，须在 spec 声明"本语法不承诺子模式文法的未来"。
2. **保持 VB-like** — 值输入继承 VB 的普通函数调用语义，低仪式；常量槽位若做成"字面量 = 等值约束"，与 C# 常量模式同形，VB 化程度取决于大小写/比较语义是否走标识符规则。
3. **不引入"第二种做事方式"** — **重罚项的一半。** 值输入不是第二种方式（它就是函数调用）；常量槽位若在主线 `Expression` 模式之外另立"常量=约束"规则，才是第二种。**复用主线等值语义，则不违；独立发明，则违。**
4. **默认跟随 C#，除非有充分理由** — 常量槽位的对标是 C# 常量模式（C# 位置模式的常量子模式）；值输入在 C# 无直接对标（C# 用属性模式/`When`），对标是 F# active pattern 参数——2018.12.19 提醒 "#4 ... is for F# and several of these are not available in other .NET languages"，F# 只作灵感。值输入是函数模型的自然结果，不算偏离 C#，但也不是 C# 对齐点。
5. **读起来像英语、对新手友好** — `RegexMatch("\d+", n)` 读作"用正则匹配，绑到 n"，可读；但读者需知道哪个实参是输入、哪个是输出（B 的 `In` 关键字为此而生）。常量槽位 `IdentifierName("New")` 对不懂 SyntaxFactory 的新手不可读（上一场已记）。
6. **不为边缘场景加特性** — 常量槽位只服务语法树场景，边缘；值输入服务正则分发，业务真实。D 靠这条否决，A 不靠。
7. **避免隐蔽的控制流/语义变化** — 常量槽位把"读起来像值的 `"New"`"变成"约束"，是隐蔽语义；值输入则完全透明（就是传参）。
8. **不与既有语法冲突** — `Case RegexMatch(...)` 与等值 Case 冲突（命名模式会议已记录）；值输入自身不与任何既有语法冲突。
9. **消除常见样板** — 值输入消除"判定 + 提取"两遍样板；常量槽位消除"绑变量 + When 等值"的样板。都有消除，但受众不同。
10. **冗长只在有用时是美德** — `In` 关键字是"有用的冗长"；签名帮助可替代时，`In` 是可选美德。

**主线对照（评价标准 2.3 表）**：模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`。本次细化：**值输入** = 函数模型（我们已 Active 的概念）的自然补全，方向与主线一致（主线的 `'Like' StringExpression` 已承认"模式可带输入"）；**常量槽位** = 工厂镜像语法的配件，而镜像语法是 **Anthony 独立延伸且已 Table**；**子模式文法** = 主线 Phase 2 的地盘，ModVB 不得抢先。**"输入"概念本身不独立于主线，它的两半各有所属。**

### RESOLUTION:

1. **建议整体保持 inactive（Table），不激活为一个独立特性。** 它没有可独立采纳的设计实体——"输入"是两个不同概念的混写，两半都属于其他已定的工作项。**这不是对缺口说"不"，是对"把缺口当特性"说不。**

2. **"值输入"半边（PROPOSAL A）随"函数即模式"概念落地，归并进用户定义模式方法设计。** 输入 = 模式函数的**普通形参**，调用点实参按签名位置绑定，落在普通形参 = 输入、落在 `Out` 形参 = 输出；**不需要新关键字**。它是 Out 实参/形参建议的一个自然段落，不是独立特性。复活信号：用户定义模式方法进入 Active（家族 Phase 2+）时，直接写进该设计。

3. **"常量槽位"半边（PROPOSAL D）随工厂镜像语法一起 Table。** 常量槽位只在镜像语法里出现；而镜像语法已 Table（自动反转机制未定义）。复活信号：工厂镜像语法被重新激活（`Pattern` 显式契约或反转机制被定义）时，一并评估常量槽位，且必须首先裁决比较语义（大小写不敏感标识符语义 vs Option Compare）。**常量槽位的语义不得与主线 `Expression` 模式（等值测试）重复发明**——若落地，复用主线的等值语义。

4. **显式 `In` 关键字（PROPOSAL B）不做独立设计。** 与 2014-02-17 #42 "you don't need an 'Out/Output' keyword" 的向后兼容裁定不对称；签名帮助可替代。`Probably`：仅在"调用点可读性被实测证明不足"时，作为 A 的可选注记回访。

5. **子模式文法（PROPOSAL C）划给主线模式匹配工作项（#337）。** ModVB 不在函数模型之外另造文法；`Regex("\d+", n)` 的字形与 C# 位置模式的字形冲突，作为已知冲突写进 spec，由主线模式文法统一裁决。

6. **命名模式建议的示例不得再静默依赖常量槽位。** 上一场我们已指出 `IdentifierName("New")` 依赖未定义的机制；本场确认该依赖无近期解。**要求命名模式建议返工**：示例改写为绑变量 + `When` 等值（`Case IdentifierName(id) When id = "New"`），直到常量槽位随镜像语法复活。

7. **一致性义务。** 值输入在严格/宽松两条 Option 下，模式函数调用语义逐字等价于"同名普通函数调用 + Out 绑定"；输入表达式只求值一次、在输出赋值之前求值、`Nothing` 主语时不求值（短路）；definite assignment 沿 2018.12.19 原则（"We don't usually let variables be used before they are declared"）。

### Implication:

- 将本建议状态标注更新为 **LDM Considering（Table，分解后两半各有归属）**，并在建议头部注明关联 #337/#304/#305、18.12 出处、以及分解结论。
- 通知 `proposal-user-defined-pattern-methods.md` 与 `proposal-out-arguments.md`：在两者进入 Active 的设计稿里，加入"普通形参 = 输入、Out 形参 = 输出、调用点按签名绑定"段落（本建议 A 的内容）。
- 通知 `proposal-named-patterns.md` 返工：`IdentifierName("New")` 改为绑变量 + `When` 等值，直到常量槽位复活。
- 与 `proposal-string-pattern-matching.md` 对齐：`RegexMatch` 是插值字符串模式的"正则版"替代，两建议共享命令/校验分发场景，避免重复设计。
- `TODO`（供返工）：比较语义（大小写/`Option Compare`）、求值顺序与短路、`Object` 主语在宽松路径的隐式收窄、A 与 C 字形冲突的 spec 声明——四件未决，写入复活评估前置条件。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：常量槽位的比较语义——标识符大小写不敏感（`Probably`）还是受 `Option Compare` 影响？`Suspect`：应走标识符语义，待镜像语法复活时定夺。
- `OPEN QUESTIONS`：值输入的求值顺序与副作用——输入表达式在 `Out` 赋值前只求值一次（`Probably`），但"`Nothing` 主语时是否短路不求值"未定。
- `OPEN QUESTIONS`：宽松路径下 `Object` 主语传给 `String` 形参的隐式收窄规则（VBScript.NET 默认宽松，硬约束）。
- `OPEN QUESTIONS`：A 与 C 的字形冲突——`RegexMatch("\d+", n)` 在函数模型与子模式文法下的语义二分，是否需要在 ModVB 侧先声明"只承诺函数模型"，待主线裁决。
- `TODO`：量化正则分发（判定 + 提取捕获组）在真实业务代码中的占比，为值输入的普遍性补证据。
- `Follow-up`：跟踪主线 #337（模式文法 Phase 2 递归模式）与 #124；若主线模式文法先落地，`RegexMatch` 应重审为"主线模式 + 封装函数"。

### 状态

- **LDM 状态：建议整体 LDM Considering（Table）**；分解后：值输入半边 → Active（并入用户定义模式方法 + Out 建议）；常量槽位半边 → Table（随工厂镜像语法）；子模式文法 → 划归主线；`In` 关键字 → 不独立设计。
- **三态判定：Table（保持 inactive）** — 缺口的"值输入"半边免费、随 Phase 2+ 落地即可；"常量槽位"半边是搁置特性的配件；建议自身没有可独立激活的实体。**激活所需信号**：① 用户定义模式方法进入 Active——值输入随其落地，无需单独激活；② 工厂镜像语法复活——常量槽位随之评估，前置条件是比较语义裁决；③ 主线 #337 模式文法 Phase 2 落地——本建议的全部内容重新对照主线，决定是并入还是放弃。

---

## 附录：特性评价

# 建议评价报告：proposal-named-pattern-inputs.md

## 评价对象

- 建议：proposal-named-pattern-inputs.md — 命名模式输入（实参列表从"只输出"扩展为可承载输入）
- 来源：Anthony 原文第 18 章 18.12 "Named pattern inputs"（`..\..\AnthonyDesign_wordpress.txt` L3018–3022；唯一内容为一句话："Some hypothetical patterns would benefit from additional "inputs" (e.g. `Regex("\d+", n)`) but currently the "argument" list for a named pattern is output only. This should be investigated."）。另有 `Regex(...)` 输入形态在 Smart Attributes 语境出现（L1892 `<Regex("^\d{3}-\d{2}-\d{4}$", "...")>`），说明"输入"母题跨章重复，但本建议未引用。
- 配方目标：让命名模式的实参列表既能承载输出（绑定 `Out` 参数）也能承载输入（如正则字符串），并研究输入/输出的区分方式。**原文明确"未给出结论或语法"。**

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。目标（实参列表承载输入）单一、改进不可衡量；无原型、无文法、无任何设计；唯一示例 `Regex("\d+", n)` 依赖未定义的命名模式机制，**不能独立编译演示**；关键子效果（常量槽位 `IdentifierName("New")`）建议自身未提及，反而由命名模式会议承重 | 已提供/已检查（状态行 Prototype/Implementation/Specification 全为占位链接，无运行证据） | 效果完全悬置于家族落地；无量化数据；示例不演示任何已定义的机制 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。**两个概念（值输入/常量槽位）捆绑未区分**，机制、先例、代价完全不同却共用"输入"一词；值输入对标 F# active pattern 参数、常量槽位对标 C# 常量模式，均未标注；VB 既有 `Like` 操作符（主线已列 `'Like' StringExpression` 模式）这一最直接的 VB 先例完全缺席 | 已检查 | 概念混写；血缘未标注；无 VB 化改造（未评估 `Option Compare`/标识符大小写语义） |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节模板齐全（如实列出 Drawbacks/Alternatives/Unresolved 是加分），但 Detailed design 明确"原文未给出结论或语法"，无文法、无边界、无交互；示例 `Case Regex("\d+", n)` 缺主语、缺 `Regex` 模式定义，不能编译；Drawbacks/Alternatives 是推测性罗列；未关联兄弟建议（named-patterns 的静默依赖、user-defined-pattern-methods、out-arguments）；状态行占位链接 | 已检查 | 无 Grammar/Edge cases/Compatibility 任何一节；示例不可编译；未决问题 3 个但都因"未设计"而空洞（未决问题的诚实性保住了 2 分，未跌到 1） |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风（演化一致性）受损——若按"位置混排"贸然实现会**毒化模式调用语法**，与主线 `Expression` 模式/子模式文法字形冲突，且完全未识别该风险；光（差异化）唯一正向是"正则分发"对脚本场景的价值，但已被 `Regex.IsMatch`+`When` 覆盖；雷（迭代速度）只有存档价值 | 已检查（预测待定） | 语法毒化风险未识别；依赖链（家族 Phase 2+ + Out + 镜像语法）未说明；实际影响须"已采纳"后定 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。来源标注**准确**（Anthony 18.12，一句话）；但血缘标注几乎全缺——F# active pattern 参数、C# 常量模式、VB `Like` 操作符（主线文法）、主线 #305 In/Out 审查结论、以及命名模式会议的静默依赖，全部未标注 | 已检查 | 血缘全缺；概念混写导致成分（值输入 vs 常量槽位）未区分；无杂质（诚实标注了"未定稿"） |

## 设计原则对照

- **与 VB 基因：部分一致、主体偏离。** 唯一正向是值输入直觉的低仪式与消除样板（原则 #9、#10 的部分）；核心偏离：概念混写未 VB 化（原则 #2）、未复用 `Like`/等值模式的既有机制（原则 #3 风险）、未考虑 `Option Compare`/标识符大小写语义（原则 #5 的可读性义务）、A/C 字形冲突制造未来重解释风险（原则 #7）。
- **与主线关系：Anthony 独立延伸，且两半各与主线不同距离。** 值输入与主线方向一致（主线 `'Like' StringExpression` 已承认"模式可带输入"，`Out` 归模式匹配 #124）；常量槽位与主线 `Expression` 模式（等值测试）语义重叠，若独立发明即"第二种做事方式"；子模式文法本就是主线 #337 Phase 2 的地盘。**无一处是主线未预见的独立特性。**
- **破坏性变更：今天无**（模式语法未落地）；**但有未来语法毒化风险**——`Regex("\d+", n)` 在函数模型（A）与子模式文法（C）下语义二分，若按 A 实现后再转 C 即重解释；且"Case 函数调用"的重解释风险（命名模式会议）继承。无 langversion 门控、无警告策略设计。

## 总评

- **达成程度：未达成（作为可采纳特性）/ 部分达成（作为记录在案的实验性指针）。** 它如实记录了一个真实缺口（实参列表只输出），且该缺口被命名模式会议证实是**承重**的；但建议自身没有语法、没有设计、没有血缘、没有边界，离"可设计状态"很远。
- **LDM 三态建议：Table（保持 inactive）**。分解：值输入 → **Active**（作为用户定义模式方法 + Out 建议的设计段落，非独立特性）；常量槽位 → **Table**（随工厂镜像语法，绑定比较语义裁决）；子模式文法 → 划归主线；`In` 关键字 → 不独立设计。面向 VBScript.NET 的优先级同样为 **Table**：正则分发在脚本场景已被 `Regex.IsMatch` + `When` 或插值字符串模式覆盖，值输入买的是"单次求值 + 封装复用"，只有函数模型（Phase 2+）落地后才值得随行。
- **主要问题**：① 概念混写——"输入"同时指值输入（参数语义）与常量槽位（约束语义），机制完全不同；② 无任何设计/文法/可编译示例；③ 血缘未标注（F#/C#/`Like`/主线 #305）；④ 静默依赖已被命名模式会议识破并成为本场会议的起点；⑤ A/C 字形冲突的未来毒化风险未识别。

## 返工建议

- **拆分为两份设计**：值输入（并入 `proposal-user-defined-pattern-methods.md`）与常量槽位（并入命名模式/镜像语法建议）。建议本身降级为"拆分说明"，不再作为独立提案。
- **补充章节**：`Grammar/BNF`——调用点实参如何绑定模式函数形参（跳过主语、普通形参=输入、`Out` 形参=输出、位置绑定、命名实参）；`Edge cases`——求值顺序与单次求值、`Nothing` 主语短路、`Object` 主语宽松路径收窄、多次常量、输入变量与输出绑定的字形区分；`Interaction`——与 `When`/`Like` 模式/`Expression` 模式/插值字符串模式/`Out` 建议的交互表；`Compatibility`——A/C 字形冲突声明、`Case 函数调用`重解释风险、Option Strict 双路径等价性。
- **补充证据**：引用 F# active pattern 参数、C# 常量模式、主线 2018.12.19 文法（`Expression` 模式、`'Like' StringExpression` 模式）、#305 In/Out 审查结论（In 对普通列表 No Plans、Out 归模式匹配）；给出基于用户定义模式方法模型的可编译最小示例（含 `RegexMatch` 单次求值 + 捕获组提取）。
- **未决问题处理**：把"输入与输出如何区分"列为该设计的**核心决策点**，显式给出三条路（签名驱动 A / 关键字 B / 子模式文法 C）与取舍；常量槽位的比较语义（大小写不敏感 vs `Option Compare`）作为复活镜像语法的前置裁决；与 `proposal-string-pattern-matching.md` 的正则场景对齐。
- **设计探索**：评估"用代码生成器把 `SyntaxFactory` 反转成模式函数"（上一场替代路线）是否让常量槽位彻底不需要——生成器可把 `IdentifierName("New")` 展开为"绑 `id` + 等值判断"，从而把 D 的语法负担移出语言表面。

---

## 附录：C# 生态与互操作考量

本附录补充本提案在 dotnet/csharplang 官方仓库中的 C# 生态对应走向。依据 `..\..\..\csharplang-index.md` 的 T8/M4（C# 15 unions / 类型系统方向），逐字引文均于 2026-08-08 直接核对 `..\..\..\csharplang` 镜像。先讲诚实结论：**本提案与 C# interop 的关联是「模式匹配家族」而非「互操作家族」**——它不触碰 COM、指针或低层内存，但其两个核心概念（模式是否可带输入、模式是否可用户定义）恰好落在 C# 模式匹配当前最薄弱、且正被 C# 15 unions 改变的位置。关联真实但不强，本附录只记录三件事：C# 模式的单一输入值形态、C# 用户定义模式的缺失、C# 15 穷尽性方向对本提案的牵制。

### 相关 C# 现实方向

**C# 模式的输入形态：只有一条「输入值」通道，没有参数通道。** C# 8 递归模式把模式定义为对单一匹配目标做形状比较，子模式沿 input/output value 链下钻：

> "Patterns are used in the *is_pattern* operator, in a *switch_statement*, and in a *switch_expression* to express the shape of data against which incoming data (which we call the input value) is to be compared. Patterns may be recursive so that parts of the data may be matched against sub-patterns."
> → `proposals\csharp-8.0\patterns.md`（Detailed design — Patterns）

各子模式形态也都只消费这一个 input value：位置模式 "A positional pattern checks that the input value is not `null`, invokes an appropriate `Deconstruct` method, and performs further pattern matching on the resulting values."（`proposals\csharp-8.0\patterns.md`）；属性模式 "A property pattern checks that the input value is not `null` and recursively matches values extracted by the use of accessible properties or fields."（同上）；关系模式 "Relational patterns require the input value to be less than, less than or equal to, etc a given constant."（`proposals\csharp-9.0\patterns3.md`）。

含义：**C# 模式没有「喂值给模式」的第二条通道**。关系模式接受的是**常量**而非运行期值——这正是本提案缺口一（值输入 `Regex("\d+", n)`）在 C# 里**没有对应物**的原因。C# 处理「带运行期条件的匹配」的唯一通道是 `when` 护栏（switch expression 的 `case_guard : 'when' null_coalescing_expression`，运行时语义见 `proposals\csharp-8.0\patterns.md`：At runtime, ... for which the expression on the left-hand-side ... matches the ... pattern, and for which the *case_guard* ... if present, evaluates to `true`.）——与本提案的「更简替代」（`When` + 捕获变量）**同形**。这验证了会议正文 §9 与「候选方案」的既有判断：C# 用 `when` 而非参数化模式来承载输入。

**常量槽位在 C# 里已有第一等实现 = 常量模式（等值约束）。** 本提案 PROPOSAL D 的「字面量落在槽位 = 等值约束」在 C# 中就是 `constant_pattern`：

> "A constant pattern tests the value of an expression against a constant value."
> "The pattern *c* is considered matching the converted input value *e* if `object.Equals(c, e)` would return `true`."
> → `proposals\csharp-8.0\patterns.md`（Constant Pattern）

且 C# 位置模式**已经**把常量放进子模式槽位——同一文档的示例 `(DoorState.Closed, Action.Open, _) => DoorState.Opened,`。即「常量槽位」不是 C# 缺失的东西，恰恰是 C# 模式文法的既有组成部分。这为 RESOLUTION 3 的「复用主线等值语义、不得重复发明」提供了 C# 侧背书。

**C# 用户定义模式缺失（active patterns 在 backlog）。** 本提案「函数即模式」（PROPOSAL A 的容器）在 C# 无对应；C# LDM 2022 年把 F# 式 active patterns 明确推迟：

> "User-defined patterns, also known as active patterns in F#, are extremely powerful, but when designing them we need to be sure we're not painting ourselves into a design corner for other future pattern enhancements. There are also some interesting questions we will have to answer around exhaustiveness. Regardless of these questions, we see active patterns as one of the last pattern-related things that need to be added to the pattern feature to make it generally "complete", and are excited to look at them after we land the current pattern work."
> 结论："Into the backlog, for after we finish the current pattern features."
> → `meetings\2022\LDM-2022-02-16.md`（Triage — User-defined positional patterns, #4131）

含义：C# 的模式是**文法形态**（递归子模式），不是函数。本提案 A 的「签名驱动函数模式」是 VB 相对 C# 的**差异化**，但也意味着没有 C# 侧先例可抄、也没有 C# 互认形态——这是跨语言桥接义务的根源（见下节）。

**C# 15 unions / closed hierarchies：穷尽性驱动的模式扩展（索引 T8/M4）。**

> "Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive."
> → `proposals\unions.md`（Motivation）
> "Union matching: Pattern matching against union values implicitly "unwraps" their contents, applying the pattern to the underlying value instead."
> → `proposals\unions.md`（Summary）
> "Closed classes provide a way to indicate that a set of derived classes is complete, and allow consuming code to rely on that for exhaustiveness in switch expressions."
> → `proposals\closed-hierarchies.md`（Motivation）

状态：DU 工作组 overview 将 Unions / Closed hierarchies 标为 **LDM: Approved**（`meetings\working-groups\discriminated-unions\union-proposals-overview.md`）；C# 15 kickoff 仅说 "We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."（`meetings\2025\LDM-2025-08-18.md`）——**预览有望，发布不承诺**。

**互操作元数据面**：unions 靠 `System.Runtime.CompilerServices.UnionAttribute` / `IUnion`（`proposals\unions.md`）；closed hierarchies 对跨语言继承显式设闸：

> "Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes."
> → `proposals\closed-hierarchies.md`（编译器元数据）

### 现实 vs 提案

| 维度 | C# 现实（已核实） | 本提案 | 判定 |
|---|---|---|---|
| 模式输入形态 | 单一 input value，无参数通道；子模式沿 input/output value 下钻 | 缺口一（值输入 A）给模式函数加普通形参 = 输入 | **冲突（语义模型不同）→ 需桥接**：C# 无「喂值给模式」的通道，`when` 护栏才是它的输入通道 |
| 常量 = 约束 | `constant_pattern` 用 `object.Equals` 做等值；位置模式槽位已可放常量 | 缺口二（常量槽位 D） | **兼容**：D 的语义 = C# 常量模式语义；RESOLUTION 3「复用主线等值语义」获 C# 背书 |
| 用户定义模式 | 缺失；active patterns 在 backlog（2022 LDM） | 函数即模式（A 的容器） | **脱节（C# 无对应）→ VB 差异化，需桥接**：C# 无法把 VB 模式函数当模式消费 |
| 子模式文法 | positional/property 子模式（C# 8 已落地） | PROPOSAL C | **C# 的主场**：RESOLUTION 5 正确，ModVB 不抢先 |
| 带运行期条件的匹配 | `when` 护栏（case_guard） | 「更简替代」（`When` + 捕获变量） | **兼容且同形**：本提案最简替代正是 C# 形态 |
| 穷尽性 | unions/closed hierarchies 让 switch 可穷尽 | 模式函数返回 Boolean、不携带类型事实 | **冲突/需桥接**：VB 函数模型无穷尽性概念（会议 §4 已记「类型事实缺失」） |
| 元数据识别 | `UnionAttribute` / `IUnion` / `[CompilerFeatureRequired("ClosedClasses")]` | 未涉及 | **需桥接**：VB 编译器须认识这些元数据才能对 C# union/closed 值做模式匹配 |

**总体判定：本提案与 C# interop 关系中等偏弱，但关键概念点全部落在 C# 模式匹配的「缺口区」。** 无一处硬冲突（不涉及指针/COM/低层内存），却有两处结构性分歧：① C# 模式无参数通道 vs 本提案想加输入通道；② C# 穷尽性依赖文法与封闭类型 vs 本提案的 Boolean 函数模型不携带类型事实。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：模式匹配是编译期构造。值输入（A）落为「普通方法调用 + Boolean 判定 + Out 赋值」，零反射——与 C# source-gen / AOT 方向（索引 T6）一致，是安全侧的顺风。**不要让「输入」退化成动态/反射通道**（如按字符串名查正则表），那会与 AOT/trimming 张力最大（决策文件 M8）。
2. **source-gen 桥**：常量槽位（D）用代码生成器展开为「绑变量 + 等值判断」（会议 §9 已列此路线），把语法负担移出语言表面——正是 C# 用 source generators 替代运行时反射的方向（索引 T6）。C# 侧无对应语言特性，生成器是唯一不造第二套语义的出口。
3. **识别新元数据**：C# 15 unions/closed hierarchies 落地后，VB 编译器必须认识 `UnionAttribute`、`IUnion`、closed 标记与 `[CompilerFeatureRequired("ClosedClasses")]`，否则 VB 的 `ShapeOf`/模式匹配无法对 C# union 值工作。注意 union 的 **unwrapping** 会改变「input value」的指向（C# 把模式应用到 `Value` 内容上，见 unions.md）——若 VB 用「函数即模式」包裹 C# union 值，主语在调用模式函数前的 unwrap 语义必须明确定义。
4. **`when` 护栏作互操作形态的输入通道**：90% 的「带条件匹配」用 `When` + 捕获变量（与 C# 同形、跨语言可读）；函数即模式只保留「单次求值 + 封装复用」的增量。这样 VBScript.NET 产出的模式代码与 C# 读者互认，不制造第二套模式方言。
5. **求值顺序一致性**：C# 允许编译器重排 `Deconstruct`/属性访问并要求「纯」（`proposals\csharp-8.0\patterns.md` 的 2018-04-04 LDM 决议）；本提案 RESOLUTION 7 的「输入表达式只求值一次、短路」方向一致，可作为跨语言约定写进 spec。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION 1（整体 Table）：维持。** C# 现实不构成激活信号——C# 连用户定义模式都还在 backlog，VB 更没有理由单独激活「命名模式输入」。
- **RESOLUTION 2（值输入并入函数模型）：补一条桥接义务。** VB 模式函数必须下略为普通 out 参数方法（`Public Function RegexMatch(...) As Boolean` + Out 形参），让 C# 侧看到的是**普通 API 而非模式**（C# 无法把 VB 模式函数当模式消费，见 LDM-2022-02-16 的 backlog 结论）。这是跨语言互操作的硬约束，应写进用户定义模式方法设计稿。
- **RESOLUTION 3（常量槽位随镜像语法）：C# 背书「复用等值语义」。** C# `constant_pattern`（`object.Equals` 等值）是 D 的天然对标；但 VB 特有的「大小写不敏感 / `Option Compare`」比较语义**在 C# 无先例可抄**（C# 大小写敏感、无 Option Compare），仍须 VB 自裁——本项结论不变。
- **RESOLUTION 5（子模式文法划归主线）：确认。** C# 8 已把 positional/property 子模式落地，这是 C# 主场，ModVB 不抢先的判断正确。
- **三态判定：Table 不变。新增一个外部触发信号。** C# 15 unions/closed hierarchies 以 preview 落地时（kickoff 乐观、不承诺），重审 VB 模式如何消费 C# union 值——unwrapping 语义与穷尽性可能反过来要求「函数即模式」补充类型事实（会议 §4 已记的「模式函数不携带类型事实」在 union 场景更尖锐）。此信号只影响重新对照，不改变当前 Table。

### 引用与核实

- 本节所有 C# 引文均于 2026-08-08 直接核对 `..\..\..\csharplang` 镜像，逐字一致；来源路径已随文标注。
- 索引第四节 6 段已核实引文全部属低层内存/unsafe 主题，与本提案无关，本节未使用；本节引文均为本次新核实。
- `OPEN QUESTIONS`：
  - unions 是否最终进入 C# 15 正式版——kickoff 仅言 "hopeful ... preview versions"，未承诺（`meetings\2025\LDM-2025-08-18.md`）。
  - `UnionAttribute` / `IUnion` 是否同样受 `CompilerFeatureRequired` 门控——`proposals\unions.md` 未提及（该属性仅出现在 closed-hierarchies 与 required-members），`Suspect`：unions 目前仅靠属性识别、无 feature gate。
  - C# 未来 active patterns（#4131）若最终落地，其输入形态是否会引入「参数化模式」——仍在 backlog、无设计，`OPEN`。
