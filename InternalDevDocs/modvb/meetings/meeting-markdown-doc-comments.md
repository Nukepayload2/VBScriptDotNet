# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题表面上像"工具问题"——文档注释怎么写——但聊到一半我们发现它恰好坐在**语言、IDE 与互操作格式（XML doc 文件）**三者的接缝上。上一场我们处理了 `TypeOf` 流分析，实现了"让既有惯用法更聪明"；这一场我们面对的是完全相反的命题：**给一个已经在主线定案并配齐工具链的格式，换一套新的书写语法**。我们花了相当多的时间确认自己不是在重复 2014 年已经做过的事。

## Agenda

* [Proposal: Markdown 文档注释语法](#proposal-markdown-文档注释语法)

## Proposal: Markdown 文档注释语法

_Related（真实主线材料）：[vblang 2014-02-17 特性汇总会 — Improved XML doc-comments](https://github.com/dotnet/vblang/blob/main/meetings/2014/LDM-2014-02-17.md)；[2017-10-18 — 注解字符串字面量 / 元数据](https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.10.18.md)；[2018-02-07 — 可空性、跨语言支持](https://github.com/dotnet/vblang/blob/main/meetings/2018/vbldm-notes-2018.02.07.md)。主线 proposals 目录经检索**不存在** Markdown 文档注释对应提案；本建议是 Anthony 原文第 2.5 章的独立延伸。_

### 场景与缺口

We started from a pain we recognize: 今天的 `'''` XML 文档注释要求逐行嵌套标签，写一个三段式的 `F` 函数文档要敲一整墙 `<param>` / `<returns>` / `<exception>`：

```vb
''' <summary>Returns a function from a description of a line in slope-intercept form.</summary>
''' <param name="m">The slope of the line. Must not be
'''     <see cref="F:Double.PositiveInfinity"/>, <see cref="F:Double.NegativeInfinity"/>,
'''     or <see cref="F:Double.NaN"/>.</param>
''' <param name="b">The y-intercept of the line.</param>
''' <returns>An instance of <see cref="T:System.Func(Of Double, Double)"/> which
'''     returns the y-coordinate given an x-coordinate.</returns>
''' <exception cref="T:System.ArgumentOutOfRangeException">Either <paramref name="m"/>
'''     or <paramref name="b"/> is infinite.</exception>
Function F(m As Double, b As Double) As Func(Of Double, Double)
    If Double.IsInfinity(m) OrElse Double.IsNaN(m) Then
        Throw New ArgumentOutOfRangeException(NameOf m)
    ElseIf Double.IsInfinity(b) OrElse Double.IsNaN(b) Then
        Throw New ArgumentOutOfRangeException(NameOf b)
    End If
    Return Function(x) (m * x) + b
End Function
```

（示例为示意；`cref` 精确写法以文档为准。上面这段不是编造的"想要的样子"——这正是 Anthony 第 2.5 章想用 Markdown 取代的那段 XML 的等价物。）

Anthony 的建议（`..\AnthonyDesign_wordpress.txt` L488–538，与 `..\proposals\proposal-markdown-doc-comments.md` 逐字一致）把这段换成：

```vb
'''
''' Returns a function from a description of a line in slope-intercept form.
'''
''' # Parameters
''' - @m: The slope of the line. Must not be @Double.PositiveInfinity,
'''       @Double.NegativeInfinity, or @Double.NaN.
''' - @b: The y-intercept of the line.
'''
''' # Returns
''' An instance of `Func(Of Double, Double)` delegate type which
''' returns the y-coordinate given an x-coordinate.
'''
''' # Exceptions
''' - @ArgumentOutOfRangeException: Either @m or @b is infinite.
'''
''' # Examples
''' ## Normal usage
''' ``` vb.net
''' Let y = F(3 / 2, -5)
''' Graph(y, 0 To 100)
''' ```
Function F(m As Double, b As Double) As Func(Of Double, Double)
    ...
End Function
```

We agree：`# Parameters` 加列表项确实比 `''' <param name="m">` 可读得多，`## Normal usage` 加分节示例也确实像人话。**但**——在我们按下同意键之前，必须先讲清楚两件背景事实，因为它们决定了这个建议真正的性质：

1. **基线不是空白。** 主线在 2014 年 2 月 17 日的特性汇总会上已经处理过 XML 文档注释，结论写得很清楚（原文逐字）："Improved XML doc-comments."、"*Approved. Already in Main. Parity with C#.*"、"These are now supported to the same extent as C#, e.g. crefs and paramrefs are parsed correctly by the language. (The IDE team also added a much richer IDE experience will full syntax colorization, quick-info and rename support."。所以这不是一个"补缺口"的建议，而是一个**推翻已定案格式**的建议。我们对此要给足理由，而不是默认它成立。

2. **文档注释不是纯注释。** 今天编译器把 `'''` 当文档注释 trivia 解析，由 doc 文件 emitter 产出 **XML doc 文件**——跨项目 IntelliSense、Sandcastle、文档分析规则都吃这个文件。任何想让 Markdown 文档"真正生效"（出现在别人的 IntelliSense 里、出现在生成文档里）的设计，都必须穿过这条管线。绕不开这个管线的设计，就只能停留在"本编辑器高亮"层面。

### 候选方案

**PROPOSAL A — 编译器原生理解 Markdown 文档注释（按建议原文全量）。** 小节标题、`@` 引用、反引号代码跨度、围栏代码块全部进入注释文法；编译器在语义模型里为 `@` 引用做绑定与校验；emitter 把 Markdown 结构映射回 XML doc 文件；IDE 渲染 Markdown 并把 `@` 引用接入跳转/重命名。

**PROPOSAL B — 工具层约定：编译器不动，生成器/分析器负责 Markdown→XML doc。** 把 A 的"结构语法"从语言移到构建期：一个 MSBuild 任务或 Roslyn 生成器读取 `'''` 注释、解释 Markdown 小节、产出 XML doc 文件。语言面保持为零，现有编译行为分毫不动。

**PROPOSAL C — 保留 XML 结构，内容层引入 Markdown。** `<param name="m">`、`<returns>`、`<exception cref="...">` 这些**结构标签原样保留**作为互操作格式；只允许标签**内容**里使用 Markdown 子集（列表、反引号代码跨度、粗体、编号列表）。这是"软化书写体验"，不是"换一套体系"。

**PROPOSAL D — 什么都不做，把预算投给 IDE 写作体验。** XML doc 已到 C# parity、IDE 已有一整套着色/quick-info/rename；`'''` 敲下即自动生成文档骨架。书写笨重的痛点主要发生在**键盘**上，而不是**格式**上——用 snippet、自动生成 `<param>`、自动生成 cref 解决，比换格式便宜得多。

### 权衡：Q&A

- **A vs B：谁拥有管线？** XML doc 文件的生成在编译器 emitter 里。要让 Markdown 作者写的东西出现在**其他项目**的 IntelliSense 里，要么编译器自己映射（A），要么构建期任务合成 doc 文件（B）。B 的代价是：每个想用 Markdown 的项目都得显式接入任务，没接入的项目跨项目文档静默退化为空白——而这个"静默退化"正是我们最警惕的那种行为。A 的代价是：把一套文档**标记语言**焊进编译器、语义模型与 emitter。两边都不免费，但方向不同：B 破坏的是"不接入就退化"，A 破坏的是"语言表面积"。
- **A vs C：原则 #3 是判官。** "不引入第二种做事方式"（设计原则 #3）在本建议上不是抽象训诫，而是直接命中：XML doc 是已被批准、已到 parity、已有完整 IDE 体验的第一种方式。A 字面意义上就是第二种。C 保住了结构标签（互操作格式不变），只把**内容**从"裸文本"升级为"Markdown 子集"——这是对既有特性的增强，不是平行体系。`Probably`：C 拿走了 A 大约六成的书写收益，而成本低一个数量级。
- **A 的 `@` 引用是全新的语义面。** 编译器要在注释里做绑定：`@m` 绑到参数 `m`、`@ArgumentOutOfRangeException` 绑到异常类型、`@Double.PositiveInfinity` 绑到字段路径。这与 XML cref 有本质区别——cref 是显式目标（`cref="F:Double.PositiveInfinity"`，`F:` 前缀表明这是字段），而 `@` 把参数、类型、成员三种引用压进同一个记号，消歧规则建议原文**没有给**。见下方追问 #1。
- **关于"取代"。** 注释是 trivia——换标记不会破坏编译；但"取代 XML"会让 Sandcastle 与跨项目 IntelliSense 的消费端断供。**这不是语言破坏性变更，是工具链破坏性变更**，而我们两者都负责。建议原文的 Drawbacks 承认了"兼容与信息损失风险"，但没有给出迁移路径，等于承认了风险没承认代价。
- **D 的辩护最强的地方**：2014 年我们把 XML doc 做到 C# parity 时配的 IDE 体验，今天的作者痛点很大程度是"IDE 没有再进一步"（自动补 `<param>`、自动 cref、把 `<see>` 渲染成链接），而不是"XML 这个格式错了"。We think the pain is real but the write-side is already half-solved, and the read-side is a rendering problem, not a syntax problem。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`@` 是三重含义符号，消歧规则缺失——这是整份建议最大的文法缺口。**

```vb
''' - @m: The slope of the line.                       ' @m = 参数 m
''' - @ArgumentOutOfRangeException: Either @m or @b…  ' @ArgumentOutOfRangeException = 类型
'''       Must not be @Double.PositiveInfinity, …      ' @Double.PositiveInfinity = 成员路径
```

`@m` 可以是"名为 m 的参数"，也可以是"名为 m 的类型"，甚至（在别的上下文）"名为 m 的成员"。`@Double.PositiveInfinity` 是成员路径，但 `@Double` 单独出现时是类型。我们试着列了几种可能的消歧策略：

- **按区段**：`# Parameters` 区内先试参数名，`# Exceptions` 区内先试类型名。区段之外（正文 prose）出现 `@` 呢？未定义。
- **按绑定顺序**：先参数、再类型、再成员。但同名参数与同名类型并存时（`@Customer` 既是参数也是类），按顺序静默选一个，读者无法得知绑到哪个。
- **词法区分**：像 XML cref 那样给 `@` 加前缀（`@param:m`、`@type:ArgumentOutOfRangeException`）。语法上明确，但回到"逐行标注"的老路，把刚拿回来的可读性又还回去了。

建议原文四个未决问题里第一个就是它（"`@` 引用与 XML 中 `<param name="..."/>`/cref 的对应规则"），**但把它列在 Unresolved 里不等于可以立项**——对我们来说，一个连"引用记号如何消歧"都没定的特性，核心语法未定型，效果证据就要封顶（见附录）。

**`@` 不是 VB 的词法基因。** VB 的标识符不以 `@` 开头——那是 C# 的逃生舱语法。VB 里 `@` 已有的含义是 **XML 轴属性**：`xml.@attr` 取属性值。注释与代码词法环境不同，不会产生编译冲突；但"VB 味儿"的问题不是冲突，是**心智**：读者看到 `@Double.PositiveInfinity`，会条件反射地想"这跟 XML 轴有什么关系？"。对照设计原则 #2（保持 VB-like），`@` 前缀更像 doxygen/JavaDoc 血统（Suspect：建议未标注该来源），不是 VB 血统。

**行首 `#` 与正文 `#`。** Markdown 把行首 `#` 当标题。正文里写 "use #1 approach"、或列表里 "`#` denotes a header" 时怎么办？小节标题判定规则（行首、`# ` 后跟空格、大小写是否敏感）未定义。`# Parameters` 必须大小写精确匹配吗？`# PARAMETERS` 呢？`# Parameters:`（带冒号）呢？

**围栏块是多行结构，与 `'''` 的"逐行 trivia"模型交互。** 每个 `'''` 行是独立的文档注释行，而围栏块的开始与结束要在**多行之间**追踪状态。未闭合的围栏在注释结束时算什么——错误、警告、还是容忍？围栏里的 `'''` 行会被当成代码文本还是注释内容？这些 Roslyn 的 `DocumentationCommentTrivia` 解析模型都没准备（现有模型是逐行 XML 拼接）。Suspect：改成块级 Markdown 需要重写文档注释的 trivia 解析器。

#### 2. 角案例与边界语义

**小节→XML 元素映射没有给出——而映射就是这个特性的合同。** 建议说"仍可机器化地映射回文档生成管线"，但没给出映射表。我们必须知道：

- 无小节 prose（`'''` 后直接是描述）映射到 `<summary>`？今天它就是这个行为。
- prose **和** `# Parameters` 混合时，prose 归 `<summary>` 还是被吞掉？
- `# Exceptions` 列表项 `- @T: ...` → `<exception cref="T">...`？`# Examples` 的 `##` 子标题 → 多个 `<example>` 还是拼成一个？
- `# Returns` → `<returns>`、`# Remarks` → `<remarks>`——那 `# Summary` 存不存在？自定义标题（如 `# Type Parameters`、`# See Also`）是允许还是固定集合？

建议的未决问题问"小节标题是否允许自定义标题或仅限固定集合"——但更根本的映射表根本没起草。**没有映射表的 Markdown 文档注释，在 doc 管线里是无定义的。** 这正是评价标准里"效果未显现"的典型：作者端的语法有了，消费端的语义没有。

**cref 生成需要类型信息。** XML doc 的 `cref` 按 CLR 成员种类带前缀：字段 `F:`、属性 `P:`、方法 `M:`、类型 `T:`。`@Double.PositiveInfinity`（字段）要生成 `F:Double.PositiveInfinity`，编译器得先解析出这是字段。无前缀的 `@X.Y` 走全名绑定，遇到重载（`@Math.Sqrt`）呢？cref 可以不区分重载（C# 的 cref 允许歧义，仅告警），但"不区分"本身要写进规则。建议对此只字未提。

**`@` 与邮箱。** 正文里 `mail@example.com` 中的 `@` 会被当成引用起点；`@m` 后面紧跟标点（`@m.` 还是 `@m,`）怎么切分引用边界？未定义。

#### 3. 作用域与绑定

**`@m` 的作用域是"紧邻声明"。** 文档注释附加在紧随其后的声明上，`@m` 只能绑到该声明的参数。对属性/字段，`@x` 绑到该成员；对类型，`@T` 绑到该类型。语义模型要为"注释里的引用"返回符号——Roslyn 今天对 `'''` 只产出 `DocumentationCommentTrivia`（XML 结构），**没有**为注释内引用建符号节点的先例。为 `@` 建引用节点 = 新的语义表面，要新 API（`GetSymbolInfo` 能接受注释节点）、新的绑定代码、新的重命名参与规则。

**与 `NameOf` 的对照。** `NameOf m` 是编译期求值、无运行期引用的表达式，走表达式的名字解析规则。`@m` 是文档期引用，走注释规则。两者可共享 resolver，但作用域、报错策略、重命名参与度都不一样。我们不喜欢"为同一件事维护两条解析规则"——这也是把我们推向 C（内容层增强、结构仍用已验证的 cref）的原因之一：cref 的解析规则 2014 年就"parsed correctly by the language"了，不需要再造一条。

#### 4. 与既有特性的交互

- **XML doc comments 共存判定。** 同一文件里既有 XML 注释又有 Markdown 注释，编译器怎么分派？内容嗅探（首行是不是 `<`）脆弱——一篇以 `'<summary>'` 开头的 Markdown 文档会被误判。显式标记（首行 `# Markdown` 或指令）又违背 VB"低仪式"原则。We can't find a clean coexistence rule, and "两种形态共存"正是建议自己列的 Alternatives 之一——它承认了共存难，却没解决。
- **XML 轴属性 `@`（代码侧）。** 不同词法环境，无编译冲突；但"VB 里 `@` 是轴属性"的先入观念让 `@` 引用在 VB 语境里格外陌生。
- **CallerInfo / `IsTrue` / `AndAlso` / late binding / 隐式行续：无交互。** 注释不参与绑定与流分析（除 cref 校验外）。这是本建议里最干净的一问。
- **注释后隐式续行。** 2014 年我们批准过"隐式续行后允许注释"；`'''` 注释天然跨行（每行一个 `'''`）。Markdown 的块级结构（围栏、标题区段）要求**跨行状态**，与"注释=行集合"的现有模型不同——见追问 #1 的 trivia 问题。

#### 5. Breaking change 与兼容性

**语言面：无。** 注释是 trivia，改标记不改变编译结果、不改变 IL、不改变重编译行为。这是本建议少数可以写死的事实。

**工具链面：有，而且很隐蔽。** 若 Markdown 注释不再产出 XML doc 文件（或映射有损），受影响的是：

- 跨项目 IntelliSense：引用方项目读不到文档；
- 第三方文档生成器（Sandcastle 等）：解析不到 `<summary>` / `<param>`；
- 文档质量分析规则：检查"公开成员必须有 `<summary>`"的规则对 Markdown 注释失明；
- 现有 XML 注释代码库：迁移工具？双向（XML→Markdown 和 Markdown→XML）？建议原文没提迁移，等于默认"推倒重写"。

**"注释不是破坏性的"的完整表述**应该是：不破坏**编译**，但可以破坏**下游文档生态**。我们两样都管。

#### 6. Option Strict / 编译选项分叉

**无分叉——这是本建议最干净的一问。** 注释不参与类型检查；`@` 引用校验（若做）是文档级告警，与 `Option Strict` 开/关无关，严格与宽松两条路径行为一致。`Option Explicit`、`Option Compare`、`Option Infer` 同样无关。如果这个特性活着，它会是少数几条不需要写"Option Strict 分叉"小节的设计——**但**这也提醒我们：一个在所有编译选项下都无分叉的特性，通常意味着它的真正实现地不在编译器。

#### 7. IDE / IntelliSense

**价值大头和成本大头都在这里，而且是一枚硬币的两面。**

- **价值**：quick-info 渲染 Markdown、`@` 引用跳转与重命名、围栏代码块的语法着色——作者体验的改进几乎全部发生在 IDE。这正是 2014 年我们为 XML doc 配齐的那套（"the IDE team also added a much richer IDE experience"）。
- **成本**：为 Markdown 再建一套渲染管线 = IDE 里**两套文档渲染并存**。XML quick-info 渲染 `<see>`/`<paramref>` 是既有管线；Markdown 渲染是全新管线。两套都要维护、都要跟上主题系统、都要处理转义。
- **跨项目 IntelliSense 走 XML doc 文件**：Markdown 若不映射到 XML，其他项目看到的就是空白 quick-info。B（构建期生成器）能补这块，但每个项目要显式接入。
- **`@` 引用参与重命名吗？** 把参数 `m` 改名 `slope`，注释里的 `@m` 要不要跟着改？要，IDE 就得在注释里做语义引用跟踪；不要，重命名就留下一个悬空的 `@m`。这个决策 A 必须回答，建议没有回答。

#### 8. 数据 / 普遍性

**零数据。** 建议没有用户请求数、没有使用统计、没有"哪个真实代码库因为 XML 文档太重而少写文档"的证据。我们相信痛点存在——我们在座的人都写过 `<param>`——但"数十万安静客户"里，**重度文档作者是库作者**（library author），占客户基数很小；业务应用开发者大量靠 IDE 自动生成的文档骨架，痛点被 stub 缓解了大半。`Probably`：这是真实但低频的 DX 增益，不是头条特性。

2017-10-18 我们在注解字符串字面量的讨论里已经说过（原文逐字）："Using attributes would be better than a special tag."，并且追问 "Could this be done with an analyzer? Does IOperation have the perf it needs to do this? _Test it_."。这次我们对 `@` 引用体系有同样的第一反应：**先用工具层回答，不要直接造语言语法。**

#### 9. 更简替代

- **IDE stub / snippet（D）**：`'''` 已自动生成文档骨架；为 `<param>`/cref 生成再进一步，是在键盘上消灭痛点，不在格式上消灭痛点。
- **PROPOSAL B（生成器/分析器）**：拿到 A 的大部分作者体验价值（作者写 Markdown，工具转 XML doc），零语言面。代价是"不接入就退化"，但可以用项目模板/构建属性把"接入"变成默认值。
- **PROPOSAL C（XML 结构 + Markdown 内容）**：保住互操作，只软化内容。`<remarks>` 里写 `- 列表` 和反引号，比 `<param>` 里堆 `see cref` 舒服得多，而结构标签一条不用改。
- **2014 年的定案本身就是"更简替代"的基线**：XML doc 已 parity + 已配 IDE 体验，改动它的边际收益必须超过推翻定案的边际成本——我们目前看不到。

#### 10. 成本 / 优先级

A 全量的账单：注释 trivia 解析器重写 + 语义模型新引用节点 + emitter 的 Markdown→XML 映射 + IDE 双渲染管线 + 迁移工具 + 分析器/规则适配。**这是"编译器+语义+emitter+IDE"的全栈特性，为了把 `<param>` 换成 `# Parameters`。** 我们引用 2018-02-07 会议（issue #211）的原话——不是字面意思，但精神适用："Fantastic idea, and too hard to do." 这不是说 Markdown 文档注释做不出来，而是说**为它付出的全栈成本与其换取的可读性不成比例**。C 的成本是 A 的零头；B 的成本在编译器之外。

优先级上：VBScript.NET 的脚本用户更在意"文档在源码里读得懂、Quick Info 好看"，而不是"跨项目 XML doc 完整无损"——后者是库作者的诉求。先做 IDE 内容渲染增量，把结构语法留给工具层，是投入产出最高的路径。

#### 11. 运行时 / CLR 硬约束

**无。** 注释不进 IL；围栏是注释文本；无 PEVerify 问题；无表达式树；无 CLR 存储规则。`Probably`：这是本建议唯一的"零摩擦"维度，也侧面印证了它本质上是**源文本 + IDE** 的问题，不是运行时的问题。

#### 12. 值不值得做

- **价值**：作者体验提升真实但有限，且大头可在 IDE/工具层兑现（C+B）；库作者的跨项目文档需求必须走 XML doc 互操作。
- **成本**：A 全栈（编译器+语义+emitter+IDE）；C 小；B 中。
- **风险**：第二种做事方式（#3）、工具链断供、IDE 双渲染、`@` 消歧文法缺口——集中在 A 上。

2014-02-17 汇总会上，我们在表达式体成员议题下写过一句（原文逐字）："RESOLUTION: We're proud not to do anything. None of the proposals buy that much, and none are that special." 这句话今天对 **A 的语言面**依然成立——除了 C 的窄版本。我们愿意为一个**内容层增强**买单，不为一个**平行标记体系**买单。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）——本建议最强的基因论点。** `# Parameters` / `- @m: The slope of the line.` 确实比 `''' <param name="m">` 更像人话。We like this a lot, and it's exactly what C 保留下来的部分。
- **不引入"第二种做事方式"（原则 #3）——最大扣分项。** XML doc 已 Approved、已 parity、已配齐 IDE 体验；A 是字面意义的第二套。这条原则我们几乎不破例。
- **保持 VB-like（原则 #2）。** `@` 前缀不是 VB 词法基因（VB 的 `@` 是 XML 轴属性；标识符前缀是 C# 的）。即使用工具层实现，`@` 也是外来血统。
- **冗长只在有用时是美德（原则 #10）。** XML 的冗长在结构化互操作上有用（cref、显式 param 名、例外声明）；Markdown 的"短"在丢掉结构时的短。我们不认为"短"天生优于"结构化"——脚本场景看中短，库场景看中结构。
- **与主线关系（对照表 2.3）。** 主线**没有** Markdown 文档注释提案（Grep 核实）；本建议是 Anthony 独立延伸，方向不与主线正面冲突，但**方法上越过了主线画好的线**：2014 年 XML doc-comments 已 "Approved. Already in Main. Parity with C#."。改它不叫"推进主线"，叫"推翻定案"——这在 ModVB 沙盒里可以做，但我们要清楚地知道自己在做什么。

### RESOLUTION:

1. **不采纳 PROPOSAL A（编译器原生 Markdown 文档注释）作为语言特性。** 理由：设计原则 #3（第二种做事方式）、`@` 引用消歧文法缺口（追问 #1）、Markdown→XML doc 映射未指定（追问 #2）、IDE 双渲染管线成本（追问 #7）、工具链断供与迁移缺失（追问 #5）。We are not rejecting Markdown's readability——we are rejecting welding a markup language into the compiler, semantic model and doc emitter.
2. **采纳 PROPOSAL C 的核心**：XML doc 结构标签（`<summary>` / `<param>` / `<returns>` / `<exception>` / `<remarks>` / `<example>`）**保持为互操作格式不变**；允许标签**内容**内使用受控 Markdown 子集——列表、反引号代码跨度、粗体、编号列表。结构仍是已验证的 cref 与 paramref；内容软化。
3. **PROPOSAL B 作为随附工具**：为 VBScript.NET 提供构建期 Markdown→XML doc 生成器/分析器，满足"Markdown 优先"写作的库作者，语言面零改动。`# Parameters` 小节、`@` 引用这类**结构语法**不进编译器，留在这个工具里，消歧规则由工具契约定义而非语言规范。
4. **`@` 引用形式不引入。** 参数/成员引用继续用 XML cref/paramref（2014 已 "parsed correctly by the language"）。若嫌 cref 啰嗦，那是 IDE 写作体验问题（自动生成 cref、渲染链接），不是语言问题。
5. **围栏代码块语言标注统一为 `vb`，不用 `vb.net`**——文档生态既有约定。Suspect：待与文档/生态团队核实 GitHub、Sandcastle、着色器实际识别名后定稿。
6. **对 VBScript.NET 的落地建议**：脚本场景文档消费主要在源码内（Quick Info 渲染 Markdown 内容子集），跨项目 XML doc 非核心；**先做 IDE 内容渲染增量，编译器与 emitter 不动**，作为 C + B 的第一步。

### Implication:

- 起草 C 的 speclet：XML 结构标签不变，内容内允许的 Markdown 子集清单、转义规则（`&lt;` 与 Markdown 的交互）、内容渲染的 IDE 行为。
- 原型只在 IDE 层：quick-info 渲染 Markdown 内容子集；不改语法、不改语义、不改 emitter。验证 2017-10-18 那次我们要求过的命题——"Does IOperation have the perf it needs to do this? _Test it._"。
- 调研 B：Roslyn 生成器/分析器读取 `'''` 并产出 XML doc 文件的最小实现；评估"不接入即退化"的默认值策略（项目模板默认接入）。
- 补一份**迁移影响分析**：现有 XML 注释 + 第三方生成器 + 跨项目 IntelliSense 的兼容矩阵，量化"推翻定案"的真实代价，供后续若重启 A 时引用。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：若 B 的 `@` 引用由工具层实现，其消歧规则（参数 vs 类型 vs 成员）仍需定义——但那是工具契约，不是语言契约；先定工具契约还是直接弃 `@`，待原型反馈。
- `OPEN QUESTIONS`：Markdown 内容在 XML 标签内与 XML 实体（`&lt;`/`&amp;`）的转义交互，speclet 起草时定。
- `OPEN QUESTIONS`：Sandcastle/文档生态对"XML 结构 + Markdown 内容"的现状支持程度——未核实，Suspect，需外部验证。
- `TODO`：量化文档注释作者的真实痛点（数据/普遍性证据缺失——建议没有数据，我们也不该在没有数据的情况下给语言面立项）。
- `Follow-up`：与 IDE 团队确认双渲染管线的成本；若 IDE 统一渲染 Markdown 内容，XML 结构是否可退居"文件格式"，C 与 A 的边界随之移动。
- `Follow-up`：Anthony 第 2.5 章示例使用 `Let y = F(...)`，依赖 `Let` 替换 `Dim` 的跨建议（`proposal-set-statement.md`）；文档示例今天无法编译——跨建议依赖要显式标注。

### 状态

- **LDM 状态：LDM Considering（作为内容层增强 C + 工具层 B）**；编译器结构语法面 = 无计划（Table）。
- **三态判定：Table（语言面）/ Consider（工具层 + IDE 内容渲染）** — 价值真实但应由工具层与 IDE 兑现；把 `@` 引用、Markdown 小节结构文法请出编译器。

---

## 附录：特性评价

# 建议评价报告：proposal-markdown-doc-comments.md

## 评价对象

- 建议：proposal-markdown-doc-comments.md — `'''` 文档注释支持 Markdown 小节/`@` 引用/围栏示例
- 来源：Anthony 原文第 2.5 章 "Markdown Documentation Comment Syntax"（`..\AnthonyDesign_wordpress.txt` L488–538；建议正文与原文逐字一致）
- 配方目标：以 Markdown 组织参数约束/返回值/异常/说明/示例，取代冗长 XML 标签式文档注释，并"机器化映射回文档生成管线"

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。作者端书写体验可演示、示例与原文一致；但消费端（Markdown→XML doc 映射、跨项目 IntelliSense、生成器）未定义，"取代"后的下游行为缺失 | 已检查 | 无原型封顶 3；核心语法（`@` 消歧）未定型，效果证据封顶；"机器化映射"有口号无映射表 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。Markdown 是外来格式、整体引入未见 VB 化改造；`@` 与 VB 的 XML 轴属性 `@` 心智冲突（非 VB 味）；违反原则 #3（第二种做事方式），与 2014 已 parity 定案的 XML doc 正面相撞 | 已检查 | 原则 #3/#2 双扣分；内容层增强（C）与结构文法（A）混为一谈，未分层 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全，但 Drawbacks/Alternatives 单薄；缺 Compatibility/迁移章节；`@` 消歧与 Markdown→XML 映射是核心设计点却停在未决；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 示例依赖 `Let`（跨建议）今天不可编译；映射/消歧/围栏三处边界含糊；4 个未决问题具体诚实，但核心语法未定型 ⇒ 效果封顶（评价标准 3.3） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光/雷正向（写作体验、盘活文档价值）；风/暗受损（第二套体系、工具链断供、IDE 双渲染、迁移成本）均被识别但未量化、无对冲设计 | 已检查（预测待定） | 风=与已定案 XML doc 断裂；暗=第三方生成器失明、跨项目 IntelliSense 空白；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。Markdown 与 `@` 引用（似 doxygen/JavaDoc 血统）来源未标注；未提主线 2014 已批准 XML doc parity 的定案；未声明 `Let` 跨建议依赖 | 已检查 | 材料来源（第 2.5 章）可追溯但内部血缘未标注；"取代"与主线定案的冲突未识别；无杂质但有隐含依赖 |

## 设计原则对照

- **与 VB 基因：部分一致、核心偏离。** 一致：可读性 #5（Markdown 像人话）、对新手友好。偏离：原则 #3（引入第二种做事方式）、原则 #2（`@` 非 VB 词法基因）、原则 #10（用短换掉结构化互操作）。
- **与主线关系：Anthony 独立延伸，方法上与主线定案冲突。** 主线无对应提案（Grep 核实）；但主线 2014-02-17 已将 XML doc-comments 定案 "Approved. Already in Main. Parity with C#."——本建议的字面目标是推翻该定案，冲突未被建议自行识别。内容层增强（C）方向与主线兼容。
- **破坏性变更：语言面无**（注释是 trivia，不改变编译）；**工具链面有**（XML doc 断供、Sandcastle/规则失明、跨项目 IntelliSense 空白、迁移缺失）。

## 总评

- **达成程度：部分达成。** 痛点真实（XML 书写笨重）、方案方向可辩（Markdown 可读）、示例清晰；但把"内容层增强"与"编译器结构文法"混为一谈，且 `@` 消歧与映射表两个核心设计点未定型。
- **LDM 三态建议：Table（语言面）/ Consider（工具层 B + IDE 内容渲染 C）。** 编译器原生 Markdown 文档注释不应立项；"XML 结构 + Markdown 内容子集 + 构建期生成器"是价值/成本/风险三者最优的落点。
- **主要问题**：① `@` 消歧与 Markdown→XML 映射未指定 = 核心语法未定型；② 违反原则 #3，且与主线 2014 定案正面冲突而未识别；③ 无 Compatibility/迁移分析，工具链断供风险未量化；④ 示例依赖 `Let` 跨建议，今天不可编译；⑤ 零数据支撑普遍性。

## 返工建议

- **补充章节**：Compatibility/迁移矩阵（现有 XML 注释、第三方生成器、跨项目 IntelliSense、文档规则）；Markdown→XML doc 元素映射表（含 cref 生成 `P:`/`F:`/`M:` 前缀规则）；`@` 消歧规则——或直接论证弃 `@`、沿用 cref/paramref。
- **补充证据**：IDE 双渲染管线的成本估算；B（构建期生成器/分析器）最小原型与 IOperation 性能验证；文档生态（Sandcastle/GitHub 渲染）对 Markdown 内容的现状调研；文档作者痛点的量化数据。
- **未决问题处理**：语言面 vs 工具层分界先行（RESOLUTION 1–4 已给出倾向）；`vb.net`→`vb`；围栏闭合与转义规则；`# Parameters` 等小节标题的固定集合与大小写。
- **设计探索**：降级为 PROPOSAL C（XML 结构 + Markdown 内容子集），把 `# Parameters`/`@` 交给 PROPOSAL B 的工具契约；或把 A 的全部价值移到 IDE 渲染层，编译器与 emitter 分毫不动。

---

## 附录：C# 生态与互操作考量

> 主题定位：本提案（Markdown 文档注释）与 C#/CLR 的**低层互操作主线**（Span/ref 安全、指针/函数指针、COM、AOT/trimming、dynamic 边缘化）关系很弱——正文追问 #11 已判过：注释不进 IL、无 CLR 存储规则、无表达式树、无 PEVerify 问题，**本质上是「源文本 + IDE」的问题，不是运行时的问题**。因此本附录不硬凑那些主题，只聚焦与本提案真实相关的两个 C# 现实方向：**XML doc 互操作格式**与 **source generators / 编译期元编程**。在「VB 主线只做 C# 兼容」的大背景下（决策文件），本提案的落点（C + B）恰好是「兼容优先」的样板——保留互操作格式、语言面不越线。

### 相关 C# 现实方向

**D1 — XML doc comments 是 .NET 文档互操作的事实格式（编译器产出、生态消费）。**
C# 的 `///` 由编译器 emitter 产出 XML doc 文件，跨项目 IntelliSense、docfx、Sandcastle、文档质量分析规则全部消费这个 XML；`ISymbol.GetDocumentationCommentXml()` 是跨工具的标准读取面。C# 语言设计把 doc comments 当 API 表面来规范，而不是纯注释：

- 逐字（已核实）："When doc comments are present on only one of the parts of the property, those doc comments are used normally (surfaced through `ISymbol.GetDocumentationCommentXml()`, written out to the documentation XML file, etc.)." → `proposals\csharp-13.0\partial-properties.md`（Documentation comments 节）。
- 逐字（已核实）："For xml docs, the docID for an extension member is the docID for the extension member in metadata." → `proposals\csharp-14.0\extensions.md`（XML docs 节）——C# 为 extension 成员设计**稳定 docID**（跨重编译、跨重排不变），说明「XML doc 文件里的引用标识」是 C# 当作稳定性契约来维护的互操作层。这正是 .vbx 若换文档格式必须对得上的那一层。

**D2 — source generators：C# 把「生成/元编程」从运行时反射搬到编译期。**
C# 9 Source Generators + C# 10 Incremental Generators（索引 T6）：生成代码在 Roslyn 编译内运行、可访问语义模型、可增量缓存、可写辅助文件；interceptors（实验性）在 AOT 下做方法拦截。方向本质：**编译期生成替代运行时动态**，给互操作提供「零反射」出口。
说明：镜像内 `proposals\csharp-9.0\` **没有** source-generators.md——`Language-Version-History.md`（第 94 行）只链到外部 devblogs 文章 `https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/`。故此方向只能按索引 T6 转述，**本镜像无逐字原文可核实** → OPEN QUESTIONS（需原文须到 dotnet/csharplang main 或 devblogs 核对）。

### 现实 vs 提案

| 本提案元素 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| RESOLUTION 2 · PROPOSAL C：XML 结构标签保持互操作格式、内容允许 Markdown 子集 | D1 | **兼容** | 结构标签 + cref/paramref 原样保留，docfx/Sandcastle/跨项目 IntelliSense 无感；Markdown 只出现在标签内容层，消费端仍解析到合法 XML。零摩擦落点。 |
| RESOLUTION 1 · 拒绝 PROPOSAL A（编译器原生 Markdown 文档注释） | D1 | **冲突已规避** | A 若落地，`# Parameters`/`@` 不进 XML 结构，C# 工具链对 .vbx 文档失明——正文追问 #5 的「工具链断供」正是对 D1 的尊重。 |
| RESOLUTION 3 · PROPOSAL B：构建期 Markdown→XML doc 生成器 | D2 | **方法同向、实现需桥接** | B 的「编译期生成」方法论与 C# 同向；但 C# 的 source generator 是 Roslyn 托管的第一公民（增量、语义模型、辅助文件），B 若停在 MSBuild 任务/分析器，是「弱化版 source-gen」。桥接见下节。 |
| RESOLUTION 4 · 弃 `@`，继续 cref/paramref | C# 无 `@` 记号；cref 是唯一跨语言引用面（2014 已 "parsed correctly by the language"） | **兼容** | 弃 `@` = 保持与 C# 同一套引用面；保留 `@` 则 C# 库 ↔ .vbx 存在第二套记号，徒增桥接。 |
| RESOLUTION 5 · 围栏语言标注统一 `vb` | 文档渲染生态（GitHub/Sandcastle/着色器）对 `vb.net` 的识别 | **需桥接 · Suspect** | 与 C# 语言侧关系弱，与 docfx 等文档渲染生态相关；正文已标 Suspect，外部验证后定稿。 |

**脱节点（如实说明）**：索引第四节与低层互操作主题相关的 6 段已核实原文（native-integers、function-pointers、blittable、unsafe-evolution 的 VB 表述、span-safety、ref-struct-interfaces）均与本提案无关——doc comments 不进 IL、无元数据、无反射、无运行期语义，「默认安全/按需动态」「识别新元数据」等张力在此**不适用**，故不引。

### 对 VBScript.NET 的适应建议

- **XML doc 文件是 .vbx 与 C# 库互通文档的唯一载体。** .vbx（修改版 Roslyn VB 编译器）必须持续产出与 C# 格式一致的 XML doc 文件。落地 C 时 **doc 文件 emitter 分毫不动**（RESOLUTION 6）；Markdown 内容子集只在 IDE 渲染层解释，消费端（C# 项目、docfx）看到的仍是合法 XML。
- **source-gen 桥（本附录最重要的桥接点）**：若实现 B，建议显式靠拢 C# 的 Incremental Generator 模型——作为 Roslyn 编译内运行的 source generator/analyzer（可访问语义模型与 `ISymbol.GetDocumentationCommentXml()`、可增量缓存、可写辅助文件），而不是游离的 MSBuild 任务。这样 B 与 C#「编译期生成」共用同一运行时与 API，未来同一生成器可同时服务 .vbx 与 C# 库。
- **docID/cref 跨语言一致性**：C# 的 cref 是 CLR docID（`T:`/`P:`/`M:`/`F:` 前缀、泛型反引号），VB 的 cref 走 VB 语法（`T:System.Func(Of Double, Double)`，见正文示例）。2014 年 parity 已解决 "parsed correctly by the language"，但 .vbx 需回归验证 VB 侧 cref 产出后与 C# 侧 docfx 消费的 docID 一一对应（尤其泛型/嵌套类型）——与正文追问 #2 的 cref 前缀规则是同一件事的两面。
- **不适用维度（如实说明）**：「默认安全/按需动态」与「识别新元数据」两条对**其他** ModVB 提案的通用建议，在本提案上无对应物——注释是 trivia，无运行期语义、无元数据面。

### 对既有 RESOLUTION / 三态判定的影响

- **无推翻，有强化。** 本附录从 C# 生态侧印证 RESOLUTION 1–4：拒绝 A、保留 XML 结构（C）是对 D1（XML doc 是事实格式）的正确响应；弃 `@` 与 C# 唯一引用面一致。
- **一个可再推进点**：RESOLUTION 3 的 B 目前定位「构建期生成器/分析器」，建议显式对齐 C# source generator 模型（见适应建议）——不改判定（工具层仍 Consider），但把 B 的「接入成本」从「每项目显式接任务」降到「与 C# 生成器同一运行时」。可在 Implication 调研条目追加「B 采用 Roslyn source generator 而非 MSBuild 任务的可行性」。

### OPEN QUESTIONS / Suspect

- `OPEN QUESTIONS`：source-generators.md 提案正文不在本镜像（`proposals\csharp-9.0\` 缺失，Language-Version-History.md 仅链外部 devblog），D2 无逐字原文可引；如需引用，到 dotnet/csharplang main 或 devblogs 原文核对。
- `OPEN QUESTIONS`：docfx/Sandcastle 对「XML 结构 + 标签内 Markdown 内容子集」的渲染支持程度（docfx 属 dotnet/docfx 生态，不在本镜像）——正文 OPEN QUESTIONS 已列，本附录从 C# 生态侧再确认：这是 .vbx 换 Markdown 内容后**唯一**需要外部验证的生态点。
- `Suspect`：`vb` 围栏标注被 GitHub/Sandcastle/着色器识别的现状（RESOLUTION 5 的待核实项，跨 docfx 渲染生态，非 C# 语言侧）。
