# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这是插值字符串/字符串解析工作线的一次回访：我们先前已经审过 `ShapeOf` 模式匹配（并拒绝了独立操作符）与 TypeOf 流分析，今天轮到 Anthony 第 6 章"Strings and String Pattern Matching"里的第三块——**字符串模式匹配（插值逆运算）**。它与插值字符串、`Like` 运算符、TryParse-shape 匹配都沾边，结果是一场从"场景很漂亮"滑向"语义没想清楚"的会议。

_Note on honesty: this note is a review of a ModVB proposal (`proposal-string-pattern-matching.md`, source: Anthony D. Green 第 6 章 "Strings and String Pattern Matching"，原文正文 `..\AnthonyDesign_wordpress.txt` L1251–1309，含第 18.10/18.11/18.12 节自评)。与上次相同，我们记录判断依据时把**事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`** 显式分层；凡引用 vblang 主线会议的内容均与 `..\..\vblang\meetings\` 逐字核对过，找不到对应材料处如实标注。_

## Agenda

* [ModVB Proposal — String Pattern Matching（插值逆运算）](#modvb-proposal--string-pattern-matching插值逆运算)

## ModVB Proposal — String Pattern Matching（插值逆运算）

We started from the proposal's own claim: **字符串模式匹配是插值字符串的逆运算**——`Case $"/echo {message}"` 里的 `{message}` 不再把变量值塞进字符串，而是反过来从被匹配字符串里抽出一段绑定到 `message`。这是一个我们一听就喜欢的方向。命令解析、路由、日志、配置字符串是我们能想到的最典型的"业务程序员"场景，而"期望的形状直接以插值字符串书写"读起来几乎像英语。但读完全文后我们得出结论与对 `ShapeOf` 那次相似但更尖锐：**场景成立，语法载体与匹配语义远未定型，且它撞上了我们自己刚定下的决定与插值字符串的既有语义。** 这不是一个可以现在就 Active 的建议。

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；ModVB：`ShapeOf` 模式匹配、字符串窄化转换（TryParse-shape）、插值字符串优化、JSON 模式匹配、字符串模式前瞻/回溯（inactive）_

### 场景与缺口

今天的命令解析是三步仪式：前缀检查、切分、逐字段转换，样板多且每步都是写错的地方：

```vb
' 今天：StartsWith + Split + Integer.Parse，三段样板，三处易错。
Dim command As String = GetCommand()

If command.StartsWith("/beep ") Then
    Dim rest = command.Substring(6)
    Dim parts = rest.Split(" "c)
    Dim freq = Integer.Parse(parts(0))
    Dim dur = Integer.Parse(parts(1))
    Console.Beep(freq, dur \ 1000)
End If
```

建议的写法（原文第 6 章逐字，`&ZeroWidthSpace;` 为 HTML 伪影，正文即 `{message}`）：

```vb
' String pattern matching (inverse of interpolation).
Select Case ShapeOf command
    Case $"/echo {message}"

        ? message

    Case $"/beep {frequency As Integer} {duration As Integer}"

        Console.Beep(frequency, duration \ 1000)

    Case "/clear"

        Console.Clear()

    Case Else
        ? "Unrecognized command: " & command

End Select
```

Gap 是真实的：三步仪式遮蔽了"我要的只是一串以 `/echo ` 开头的命令"这个结构。这正是我们在 2017.10.18 为 JSON/XML 模式立的原则——"the ceremony of _inspecting_ and object(-graph) obscures the structure of the data"——在字符串世界的翻版。而且对 VBScript.NET 目标比对标准 VB 更重要：VBScript 的血统就是晚期绑定、字符串驱动、命令脚本分发，`Select Case` + 字符串匹配是那个世界的母语。`Probably` 本特性最强的普遍性证据是 VBScript 分发惯用本身，而不是建议里（缺席的）遥测。

但我们立刻注意到第一个不一致：建议把头建在 `Select Case ShapeOf command` 上，而 **`ShapeOf` 独立操作符正是我们在本系列 ShapeOf 会议上刚判为 Table 的东西**。同一份材料里两份建议互相引用，载体却已被另一份建议的会议推翻——这在家族内部是自相矛盾的。这一点贯穿了整场讨论。

### 候选方案

We considered four shapes the feature could take.

**PROPOSAL A — 建议原文：插值字符串即模式（inverse of interpolation）**

`Select Case ShapeOf command` + `Case $"..."`。占位符 `{x}` 从被匹配字符串抽取片段绑定到新变量；`{x As Type}` 先按该类型转换，转换失败则分支不命中；普通字符串字面量仍精确匹配；`Case Else` 兜底。

**PROPOSAL B — 前缀锚定 + 尾捕获的受限子集**

模式必须从字符串头开始匹配；字面量片段做精确比较；`{x}` 只允许作**单一尾部捕获**（捕获到字符串结尾，或到下一个字面量片段的前沿）；v1 不带类型标注。无贪婪选择、无回溯、无切分歧义，匹配语义完全确定，实现面是四个方案里最小的。typed hole 的"按类型解析"明确划给 TryParse-shape（`proposal-string-narrowing-conversions.md`），不在字符串模式里重复造。

**PROPOSAL C — 主线 `Like` 模式基线**

主线 2018.12.19 的模式匹配文法已经把 `Like` 字符串模式列为家族的成员（文法原文：`| 'Like' StringExpression                // Like pattern`）。`Like` 是 VB6 遗产的字符串匹配运算符（`?`/`*`/`#`/`[...]` 通配符），是 VB 自带的、无需新语法的字符串模式。插值逆运算若要做，应作为 `Like` 模式的**命名捕获扩展**或独立语法与它并存、显式划清边界，而不是另起炉灶。

**PROPOSAL D — 编译期为正则表达式的语法糖（OTHER DESIGNS CONSIDERED 档）**

把插值模式在编译期转成正则表达式，交给 `Regex.Match` 抽取捕获组。表达力与回溯性能由成熟引擎背书，源码保持 VB 形状。代价：`{x As Type}` 与正则字符类（`\d+` 等）的映射未定义、转义规则（`\` vs `{`）需要一套新约定、`Option Compare` 与正则大小写模型冲突。`Probably` 它是实现 A 最廉价的引擎，但不是语言设计的终点——正则的失败语义（不命中 vs 转换失败）与捕获作用域（`Match.Groups`）会泄漏进语言。放 `OTHER DESIGNS CONSIDERED`。

### 权衡：LDM 追问清单

We worked through the twelve-question checklist. The decisive questions were 1, 2, 4, 5 and 7.

**Q1. 语法/文法歧义：`{message}` 是捕获还是引用？**

这是整场最尖锐的一问，也是 A 方案与 `Case pn As T` 的**本质差别**：`Case pn As T` 今天是语法错误（`As` 不是表达式操作符），所以解析上是"新增子句形态、零破坏"；而 `$"..."` **今天就是合法表达式**，`Case $"/echo {name}"` 在今天的 VB 里是合法的等值子句——把插值字符串与 `Select Case` 主语比较。`Suspect`（需验证 Option Compare 下 Select Case 等值比较的确切行为，但语法合法性是事实）。

也就是说，同一个 `Case $"/echo {name}"`：
- **今天**：求值插值字符串（`{name}` 引用既有变量 `name`）→ 与主语比较 → 语义是"命令必须恰好等于 `/echo hi`"。
- **建议**：`{name}` 变成**捕获声明** → 语义是"任何以 `/echo ` 开头的内容，剩余部分绑定到 `name`"。

同一个写法、两种含义、**改变既有合法代码的语义**。插值语境里 `{x}` 是"把变量值放进来"，模式语境里是"抽一段出来绑定变量"——一个语法符号在相邻语境里语义相反。这正是我们在 2018.05.30 拒掉 `Return?` 的理由（"Control flow would be altered by a very subtle character"）的直系亲戚，只不过这里连字符都没变，变的是所在语境。`ShapeOf` 会议我们说过 `As T` 的隐式触发边界太松；`{x}` 的语境翻转比那更松。

可讨论的缓解：模式语境里 `{x}` 一律是**新捕获**（禁止引用既有变量，即"模式内部没有插值"），这样语义唯一——但代价是 `Case $"/log {level}: {message}"` 里你没法让 `level` 引用既有约束变量（做不到 `{allowedLevel}` 同时是"引用"）。或者引入新定界符/关键字把捕获与字面量显式分开，那就不再是"插值逆运算"的漂亮对称了。We went around on this and did not find a shape that keeps the elegance and kills the ambiguity. `OPEN QUESTION` 核心。

**Q2. 角案例/边界语义：匹配边界、贪婪、回溯、typed hole 切分**

A 方案的匹配边界完全没有定义（建议自己也承认）。逐条：

- **`{message}` 匹配到哪**？原文未定义：直到下一个字面量片段、行尾、还是空格？`$"/echo {message}"` 里 `{message}` 到尾是显然的；`$"/send {recipient} {body}"` 里两个 hole 夹一个字面量空格，切分点就回到 Q1 的贪婪问题。原文 18.10 节自评是**事实**："A naïve implementation of String pattern matching would allocate many needless intermediate garbage strings."、"Certain common patterns need backtracking."、"Inner patterns can't influence decomposition of the outer pattern and lazy or greedy ways."——作者自己知道贪婪/回溯未决，建议却把它降级成"原文未定义"四个字。
- **贪婪选择**：`$"/log {level}: {message}"` 对 `"INFO: x: y"`——`level` 贪婪取到 `"INFO: x"`、非贪婪取 `"INFO"`？两种都说得通，建议没有规则。`Probably` v1 只能定死"字面量片段之前的捕获取到该片段前沿"（即**非贪婪**），否则每条命令都要做完整回退匹配，正是建议未决问题里自己担心的性能问题。
- **typed hole 切分**：`{frequency As Integer} {duration As Integer}` 对 `"1000 2000"` 清晰；对 `"1000"`——`frequency` 取 `"1000"`、`duration` 取空串 → 转换失败 → 分支不命中，语义尚可；但 `{a As String} {b As String}` 对 `"x y z"` 就是纯切分不定。`Case` 没有 fall-through，多分支顺序匹配取第一个（事实，2014 起 Select Case 行为），但"第一个命中的切分"必须先有切分规则。
- **空捕获**：`$"/set {key} {value}"` 对 `"/set  x"`——`key` 取空串？空白折叠规则未定义。
- **`Nothing` 主语**：`Select Case ShapeOf Nothing`——模式是否命中？未定义。`Probably` 不命中，与 `TypeOf ... Is` 的"cast can occur"精神一致，需显式规范。
- **大小写**：见 Q4/Option Compare。**字面 `{`/`}`**：插值用 `{{`/`}}` 转义（2015-01-14 事实，`$"{{"` 得 `"{"`），模式是否继承？`$"/set {{key}} {value}"` 的字面 `{key}` 需要规则。`OPEN QUESTION`。

**Q3. 作用域与绑定**

`{message}` 的语义模型符号是什么？`Probably` 是 Case 子句作用域的新局部变量，与 `Case pn As T` 的绑定形态同规则（2014 Design1："The scope of the variable declaration is just that case clause"）。但有两个 A 方案特有的坑：其一，**既有同名变量**——`Dim message As String` 在外层存在时，`Case $"/echo {message}"` 里 `message` 按 Q1 是新捕获还是引用？若禁止引用，则与插值语法（`{x}` 引用既有变量）**语法同形、语义相反**，IDE 里 hover 一个 `{message}` 到底是"定义"还是"使用"？其二，若允许引用既有变量做约束（`Case $"/echo {maxLen}"`），那 `{x}` 就同时是模式匹配的约束与捕获两种形态，比 Q1 更乱。We think v1 只能选"模式内部 `{x}` 一律新捕获、无约束、无插值"，并把"约束"留给 `When` 子句（主线 2018.12.19 已定 `When` 是家族成员，"We like `When`"是事实）。

**Q4. 与既有特性的交互：三重重叠**

- **与 `Like` 运算符（原则 #3 "第二种做事方式"）**：VB 已有字符串模式匹配运算符——`Like`，VB6 遗产、零新语法、`?`/`*`/`#`/`[...]` 通配。主线 2018.12.19 已经把它**列入模式家族文法**（`'Like' StringExpression // Like pattern`）。插值逆运算若独立落地，VB 就有两条字符串模式道——一条简单通配、一条结构化捕获——而我们反复说过"扩展表面积的门槛极高"。`Probably` 这是 A 方案最重的对手：`Like` 已经在语言里，插值逆运算要证明自己不是第二条道。
- **与 TryParse-shape**：`{x As Integer}` 的"按类型解析"与 `proposal-string-narrowing-conversions.md` 的 `If ShapeOf str Is ip As IPEndpoint`（借 `TryParse` 解析）是**同一个机制族**。建议把类型转换内建进 hole，等于在 TryParse-shape 之外再造一个类型驱动解析的入口。We think 两者必须共用一条"字符串→类型"的规则表，typed hole 是 TryParse-shape 在模式里的语法糖，而不是独立语义。
- **与插值字符串本身**：`$"..."` 已有完整语义（2015-01-14 事实，"an interpolated string *is* a string"，重载解析/类型推断按 String 处理）。模式化是给它叠第二个含义——Q1 的翻转即由此而来。这条是 A 方案独有的、`Case pn As T` 没有的包袱。
- **与 `Select Case` 既有行为**：等值、范围、`Is`、逗号组合必须原样保留；字符串模式是新增子句形态，不得影响既有解析。逗号合并（2018.12.19 事实，"Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax."）——`Case "/clear", $"/echo {message}"` 是否允许？We lean 与 ShapeOf 会议一致：逗号留给未来的组合模式，`Case` 内今天允许的写法继续允许，新模式子句暂不与其它子句逗号合并。
- **与 TypeOf 流分析**：无关——字符串模式不触碰类型收窄。无交互。

**Q5. Breaking change**

这是 A 方案与 `Case pn As T` 的**第二个本质差别**，也是我们反复回访的点。`Case pn As T` 对现有合法代码零破坏（`As` 非表达式操作符）；`Case $"/echo {name}"` **在今天是合法代码**（等值比较），模式化之后：

```vb
Dim message As String = "hi"
Dim command As String = "/echo hi"

Select Case command
    Case $"/echo {message}"
        ' 今天：message 是既有变量，子句等价于 Case "/echo hi" → 命中。
        ' 若改为模式：{message} 变捕获，子句变成"任意以 /echo 开头的命令" → 命中但语义是捕获。
        ' 同一源码、同一编译器选项，重编译后行为不同——虽然这里恰好都命中，但 {message} 的含义变了。
End Select
```

更糟的是场景反转：`Dim command = "/echo hello"`、`Dim message = "bye"`，今天 `Case $"/echo {message}"` 对 `/echo hello` **不命中**（因为 `/echo bye` ≠ `/echo hello`）；模式化后**命中**且绑定 `message = "hello"`。**同一源码，重编译后从不命中变成命中。** 这违反我们"永不破坏现有代码"的第一原则，除非模式只在**新语法**（比如新的 Case 子句形态或新关键字）里启用。`Probably` 唯一零破坏路径是：模式不叫 `$"..."` 而是新定界语法，或者 `Select Case ShapeOf command` 里 `ShapeOf` 的存在本身把语境切到模式（此时 `$"..."` 内 `{x}` 才是捕获）——这又回到"`ShapeOf` 被我们拒了"的死结。`OPEN QUESTION`：能否以"仅 `ShapeOf` 语境生效"保住建议语法，取决于家族对 `ShapeOf` 的最终裁决。

**Q6. Option Strict / 编译选项分叉**

- **typed hole 转换**：`{x As Integer}` 的 `Integer.Parse` 语义在 Strict On/Off 下应一致（运行时行为，不受 Strict 影响，`Suspect` 需验证宽松路径）。但"转换失败 → 跳过分支"在两种模式下都要有**警告策略**——见 Q12 的"掩盖错误"。
- **`Option Compare Text`（大小写）**：`Select Case` 的字符串比较受 `Option Compare Text` 影响（事实，`proposal-case-insensitivity.md` 引原文确认"只影响比较运算符、`Select Case` 以及 VB 运行时库中的某些 API"）。字符串模式的**字面量前缀**是否继承该设置？`/Echo` 在 `Option Compare Text` 下应不敏感匹配 `/echo`？Anthony 自己在 18.x 节把这件事点破（事实）："This issue becomes worse with String and JSON pattern matching. An elegant solution that integrates with existing .NET APIs would be wonderful... but it needs more consideration." 模式必须显式定义与 `Option Compare` 的交互，不能默认继承（否则 `{x As Integer}` 的解析与字面量比较的行为分叉）。`OPEN QUESTION`，`Probably` 交给 case-insensitivity 提案统一收口。

**Q7. IDE / IntelliSense 影响**

若 `{x}` 是捕获，则 IDE 需：Case 内把 `x` 显示为新局部变量、重命名/查找引用进入 Case 作用域、调试器在命中分支求值 `x`。若允许"既有同名变量"（Q3），hover 语义需区分定义/使用。更大的问题：**模式诊断**——`Case $"/echo {message}"` 在 IDE 里怎么着色？插值语法着色器（`{...}` 高亮）会被复用，但语义从"值"变"捕获"，需要新的模式诊断（无法匹配的路径、冗余分支、捕获未使用）。A 方案的语法复用让 IDE 实现者分不清这是插值还是模式——除非语境（`ShapeOf`/Case 子句）在语法树上就有区别。成本中等偏上。

**Q8. 数据 / 普遍性**

命令解析、路由、日志、配置是真实的、高频的、业务程序员的痛点——这是本特性最强的论据，我们 no doubt about the scenario。但建议没有给任何量化数据或用户请求；VBScript 分发惯用（晚期绑定 + 字符串命令）`Probably` 是比 (absent) telemetry 更强的场景证据。与主线关系上，2018.12.19 我们写"we all want to do pattern matching"（事实）——字符串模式是家族的一部分，不是独立头条。`Suspect`：这是"家族内值得要的一块"，不是"值得为它单独开一条语言特性线"的东西。

**Q9. 更简替代**

- `StartsWith` + `Split` + `Convert`：现状，样板多但确定。
- **`Like` 运算符**：已存在，通配匹配零新语法。覆盖"前缀 + 通配"的 80% 简单场景。
- **正则表达式**：`System.Text.RegularExpressions.Regex`，表达力全覆盖（建议 Drawbacks 自认"与正则相比表达能力有限"）。代价是反斜杠转义与 ceremony——正是插值逆运算想消灭的东西，但正则的成熟度（回溯、性能、调试）是语言内匹配器短期内追不上的。
- **TryParse-shape**：单值解析（`If ShapeOf str Is ip As IPEndpoint`）已经覆盖"把一段文本按类型解析"；多字段结构解析是它的盲区，而这恰是字符串模式的增量。
- 结论：替代方案存在且部分足够；字符串模式的唯一且充分理由是"多字段 + 结构化 + 读起来像目标字符串"。单值场景我们推荐 TryParse-shape，通配场景推荐 `Like`。

**Q10. 成本 / 优先级**

完整 A：需要一套新的字符串匹配引擎（锚定、贪婪/回溯上限、中间分配优化——原文 18.10 明确要求"consider how the feature would apply to a text window/stream or a span"，即不能整串拷贝）。成本中高。PROPOSAL D（降级 Regex）能让实现成本骤降，但把正则的语义泄漏进语言。PROPOSAL B：前缀锚定 + 尾捕获，无回溯无贪婪，匹配器可写成线性扫描、零中间分配，成本明显可落地的子集。优先级上，**家族文法（声明模式、`When`、`Like` 模式）未定型之前，任何字符串模式的独立设计都是空转**——我们必须先有统一文法，再谈这一个成员。

**Q11. 运行时 / CLR 硬约束**

无新 IL、无 PEVerify 问题。若降级 Regex，走现有 BCL；若手写匹配器，编译器生成线性扫描代码。开放泛型/反射的红线不相关（2014-02-17 判例"this is impossible in the current CLR without reflection"针对的是类型模式）。唯一 CLR 侧关切是**中间字符串分配**（18.10 事实）——匹配应按索引/span 进行，不拼接中间串，这与 `proposal-string-span-utf8.md`（inactive）共享实现关切。

**Q12. 值不值得做**

逐条打分（价值 × 成本 × 风险）：

- **价值**：场景高频、VBScript 契合、读起来像英语——但**多字段结构化解析**是唯一增量，单值/通配分别被 TryParse-shape 与 `Like` 覆盖。价值 6/10（不是 9——重叠吃掉了一半）。
- **成本**：完整 A 含匹配引擎与语义 spec，中高；B 子集低。成本 6/10（若 D 降级则 4）。
- **风险**：`{x}` 捕获/引用翻转 = 改变既有合法代码（Q5），与插值/`Like`/TryParse 三重重叠（Q4），匹配边界未定义（Q2）。风险 8/10——**这是本家族里风险最高的一块**，因为它复用的是已有含义的语法。

一句话：**这个能力我们要（作为家族成员），这个语法（插值即模式）现在不要。** 场景没问题，载体有问题，语义比载体问题更大。

### RESOLUTION

1. **场景成立，作为家族成员方向正确。** 命令/路由/配置的"多字段结构化字符串解析"是真实高频痛点；`Case $"/echo {message}"` 的**可读性**是我们明确喜欢的（读起来就像命令本身）。这一点不动摇。
2. **我们不接受"插值字符串即模式"作为该能力的载体。** 决定性论据是 Q1/Q5：`$"..."` 已有完整语义，`{x}` 在插值里是引用、在模式里是捕获——同一语法两种含义，且 `Case $"..."` 今天是合法等值子句，模式化会**改变既有合法代码重编译后的行为**。这与 `Case pn As T`（今天必错、零破坏）有本质差别。若未来要推进，语法必须能显式区分"模式语境"与"等值子句"，或干脆换新定界语法。
3. **字符串模式是家族文法的一个成员，不是独立特性。** 主线 2018.12.19 已把 `Like StringExpression` 列为 pattern；单值类型解析走 TryParse-shape；类型分发走 `Case pn As T`。字符串模式必须与这三者归并出统一文法后再谈。`Case "/clear"` 保持等值子句不变。
4. **不采用 `Select Case ShapeOf command`。** `ShapeOf` 独立操作符已在 ShapeOf 会议判 Table；主线 "an additional keyword is not required in all `Case` cases"（2018.12.19，事实）。字符串模式若做，用 `Select Case command` + 新模式子句形态，不加关键字。
5. **typed hole（`{x As Integer}`）的"按类型解析"划给 TryParse-shape。** 转换失败 → 静默跳过分支会掩盖"命令形状对但参数不是数字"的错误（Q12），我们不喜欢把它藏在分支不命中里。类型解析用 TryParse-shape 的统一规则表；字符串模式 v1 只做纯字符串捕获。
6. **若本家族推进到字符串模式，v1 必须取 PROPOSAL B 的受限形状**：前缀锚定 + 字面量片段 + 单一尾捕获 + 无回溯 + 无类型标注 + 线性扫描零中间分配；并先解决 `Option Compare` 交互（对接 case-insensitivity 提案）与 `{{`/`}}` 转义。
7. **匹配语义（贪婪/回溯/切分）不冻结**：原文 18.10 自评（垃圾分配、回溯、lazy/greedy）与 18.11（span/UTF-8）作为 speclet 的前置约束，与 `proposal-string-pattern-lookahead.md`（inactive）合并考虑，不预判结论。

### Implication

- **本建议整体判 Table**，状态 `LDM In Process`（家族内）。通知作者：文档太薄，必须并入家族文法再回。
- 与 `Like` 模式、TryParse-shape、`Case pn As T` 声明模式归并出统一文法；字符串模式在其中占一个槽位，不在家族文法之外独立设计。
- 起草"匹配语义"speclet：锚定、贪婪规则（v1 非贪婪）、回溯上限、中间分配（span 优先）、`Option Compare` 交互、`{{`/`}}` 转义。
- 补一份 Compatibility 分析：调查今天 `Case $"/echo {name}"` 在 `Option Compare Binary/Text` 下的确切行为，作为"模式化是否破坏既有代码"的证据底座。
- `TODO`（供提案返工）：Grammar/BNF、Edge cases（空捕获/相邻 hole/`Nothing`/既有同名变量）、作用域与 definite assignment、`Option Strict` 双路径验证、semantic model/IDE 影响、主线 2018.12.19 `Like` 模式对比表。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`（核心）：`{x}` 捕获与插值引用的语义翻转是否有零破坏的语法化解——仅 `ShapeOf` 语境生效（取决于家族对 `ShapeOf` 的裁决）？还是新定界语法？
- `OPEN QUESTIONS`：匹配边界与贪婪规则——v1 定"非贪婪 + 字面量前沿"是否够用，回溯哪些场景必须支持（原文 18.10 未给清单）。
- `OPEN QUESTIONS`：`Option Compare Text` 是否影响字面量前缀；与 case-insensitivity 提案的统一方案。
- `OPEN QUESTIONS`：typed hole 划给 TryParse-shape 后，字符串模式是否还需要任何形式的内联类型要求（`Probably` 不需要，`When` + TryParse-shape 覆盖）。
- `TODO`：量化命令解析场景占比，为数据/普遍性补证据。
- `Follow-up`：与 `proposal-string-pattern-lookahead.md`（inactive）合并匹配语义；与 `proposal-string-span-utf8.md`（inactive）对表 span 实现关切。

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — `Case $"/echo {name}"` 今天是合法等值子句，模式化改变重编译行为（Q5）——**违反**，最重扣分项。`Case pn As T` 无此问题，字符串模式独有。
2. **保持 VB-like** — `Case $"/echo {message}"` 读起来像命令本身，是我们见过的"读起来像英语"的极端例子——**强一致**；但 `Select Case ShapeOf command` 复用被拒操作符，不 VB。
3. **不引入"第二种做事方式"** — 与 `Like`、TryParse-shape、插值三重重叠——**违反**，第二重扣分项。
4. **默认跟随 C#，除非有充分理由** — C# 无插值逆运算先例（C# 用正则/`is` 模式）；VB 要独立论证"为什么这里偏离 C#"——理由存在（可读性）但建议没写。中立偏弱。
5. **读起来像英语、对新手友好** — 最强加分项；但"同一 `{x}` 两种含义"恰恰是新手最困惑的隐蔽语义。
6. **不为边缘场景加特性** — 命令/路由/日志是高频，通过；但增量被 `Like`/TryParse 吃掉一半后，边缘度上升。
7. **避免隐蔽的控制流/语义变化** — `{x}` 引用→捕获翻转就是隐蔽语义变化，`Return?` 的同类（2018.05.30 "Control flow would be altered by a very subtle character"）——**严重违反**，与原则 1 并列的最重扣分。
8. **不与既有语法冲突** — 与插值字符串语法直接冲突（同一 `$"..."` 两个含义）——**违反**。
9. **消除常见样板** — StartsWith/Split/Convert 三步样板是真实痛点，正中靶心——强一致。
10. **冗长只在有用时是美德** — `ShapeOf` 关键字是无用冗长（主线已言 "not required in all `Case` cases"）；无关键字形态更优。

**主线对照（评价标准 2.3 表）**：`Select Case TypeOf` / 模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`。本次细化：**类型分发与 `Case pn As T` = 主线一致**（2018.12.19 声明模式 Phase 1）；**`Like` 模式 = 主线已列进文法**（我们反而在向主线靠拢）；**插值逆运算 = Anthony 独立延伸**，主线无先例，且与主线"跟随 C# 除非有理由"、"不引入第二种做事方式"存在张力——它与 `Like` 模式的关系需要主线先表态。`ShapeOf` 复用 = 与沙盒内已定决定冲突。

**Breaking change 结论**：**潜在有，且比家族其它成员更真**——`Case $"..."` 是既有合法语法，模式化改变其含义；不同于新关键字/新子句形态的 identifier-collision 或"新增即合法"式风险。必须在兼容性分析里给出"今天行为 → 模式化后行为"的逐条对照。

---

### 三态判定

- **本建议（整体）**：`Table` — 场景成立、机制未定型、文档太薄、与家族及插值既有语义冲突，必须并入家族文法并解决 `{x}` 语义翻转后回到 `Active`。
- **分解后**：
  - 插值字符串即模式（PROPOSAL A 语法）→ **Table**（Q1/Q5 未解之前不可进入语法面）；
  - 前缀锚定 + 尾捕获窄子集（PROPOSAL B）→ **Consider**（家族 Phase 后，作为字符串模式 v1）；
  - `Select Case ShapeOf command` → **移除**（`ShapeOf` 操作符已判 Table）；
  - typed hole `{x As Type}` → **划给 TryParse-shape**，不在字符串模式重复实现。

**后续动作**：① 通知作者返工并归并家族文法；② 在 `proposal-select-case-enhancements.md` 上开"Case 子句形态"分支，把"插值/`Like`/TryParse 三者的字符串模式归属"列入家族文法待决清单；③ Compatibility 调查 `Case $"..."` 现状行为；④ `Option Compare` 交互移交 case-insensitivity 提案。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-string-pattern-matching.md`（字符串模式匹配，插值逆运算）
- 来源：Anthony D. Green 原文第 6 章 "Strings and String Pattern Matching"（`..\AnthonyDesign_wordpress.txt` L1251–1309）；相关自评第 18.10/18.11/18.12 节（L2970–3023）
- 配方目标：让"多字段结构化字符串解析"（命令/路由/配置）以插值字符串形状直接书写，`Select Case ShapeOf` + `Case $"..."` 完成比对与捕获变量绑定，消除 `StartsWith`+`Split`+`Convert` 样板。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 3/5 | 锚点≈"只覆盖部分场景；主效果显现但关键子效果缺失/消退"。场景（命令解析）真实、示例可演示（能编译、能说明意图）；但证据止于书面、无原型；关键子行为全部未定义——匹配边界、贪婪/回溯、typed hole 切分、转义、转换失败策略、`Nothing`、`Option Compare`。未决问题 4 个且全是核心设计点 → 效果证据封顶。 | 已检查（已提供，未运行） | 无原型；匹配语义未定型；无量化数据 |
| 特性 | 3/5 | 锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。插值逆运算是"借插值字符串做 VB 化改造"且可读性极强（原则 5/9 强一致）；但打包了与 `Like`、TryParse-shape、插值本身的**三重重叠**，且复用被拒的 `ShapeOf` 操作符、`{x}` 语义翻转（原则 7/8 严重偏离）。 | 已检查 | 与家族成员重复；`{x}` 语义翻转；`ShapeOf` 复用 |
| 品质 | 3/5 | 锚点≈"缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文逐字一致（已核对 HTML 伪影）；但无文法/BNF、无边界小节、无兼容性/`Option Compare`/semantic model 分析；关键边界（贪婪/回溯）原文已自评（18.10）却未在建议正文引用；状态行占位链接（项目惯例，不计重）。 | 已检查 | 无 Grammar 节；边界未定义；未引用作者自己 18.10 的自评 |
| 属性 | 2/5 | 锚点≈"某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。水（盘活 Select Case 分发）、光（VBScript 差异化）正向；但暗风险突出且文档无应对：`{x}` 翻转改变既有合法代码（暗/破坏兼容）、与插值/`Like` 双语义冲突（风/一致性断裂）、复用被拒操作符（雷/演化不一致）。文档 Drawbacks 仅轻描淡写三条，未触及最重的 Q1/Q5。 | 已检查（待定，预测性） | 兼容破坏未识别；`{x}` 翻转无语法化解；与插值语义冲突未权衡 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注；标注与影响有偏差"。来源标注准确（原文第 6 章，逐字示例）；Alternatives 提及正则；但未标注主线 2018.12.19 `Like` 模式这一**最直接先例**，未引用原文 18.10/18.11/18.12 自我批判（直接决定可行性），未交叉标注同章同族（`proposal-string-narrowing-conversions.md`、`proposal-interpolated-string-optimization.md`）与 `inactive/proposal-string-pattern-lookahead.md`。 | 已检查 | 主线先例未标注；自我批判未引用；家族血缘未交叉标注 |

### 设计原则对照
- **与 VB 基因：偏离为主**——原则 5（读起来像英语）、9（消除样板）强一致，原则 10（无关键字）可选；但原则 1（永不破坏）、7（隐蔽语义变化）、8（与既有语法冲突）、3（第二种做事方式）集中偏离，是家族内偏离面最广的一份建议。
- **与主线关系：Anthony 独立延伸、与主线部分冲突**——主线方向是 `Like` 模式（文法已列）+ `Case pn As T` 声明模式 + `Matches`；插值逆运算在主线无先例，且与 `Like` 模式/插值语义重叠；复用被拒的 `ShapeOf` 操作符 = 与沙盒内已定决定冲突。
- **破坏性变更：潜在有（家族内最重）**——`Case $"/echo {name}"` 今天是合法等值子句，模式化把 `{name}` 从"引用"翻转为"捕获"，同一源码重编译后行为改变（不命中↔命中反转）。建议未做兼容性分析。

### 总评
- **达成程度：部分达成**——场景（多字段结构化字符串解析）成立且被验证为高频；载体语法（插值即模式）有 Q1/Q5 未解的硬伤；匹配语义与家族归并未做；文档太薄。
- **LDM 三态建议：整体 Table**；分解后：插值即模式语法 → Table；前缀锚定+尾捕获窄子集 → Consider（家族 Phase 后）；`ShapeOf` 复用 → 移除；typed hole → 划给 TryParse-shape。
- **主要问题**：① `{x}` 捕获/引用翻转的语义冲突与既有代码破坏（Q1/Q5，未解决前不可 Active）；② 匹配边界/贪婪/回溯/切分全部未定义（原文 18.10 自认）；③ 与 `Like`/TryParse-shape/插值三重重叠（原则 3）；④ 复用被拒的 `ShapeOf` 操作符；⑤ 无文法、无兼容性分析、无原型、未引用作者自己的 18.10/18.12 自评。

### 返工建议
- **补充章节**：`Grammar/BNF`（Case 子句新模式形态 + 插值语境切分规则）；`Edge cases`（前缀锚定、非贪婪 v1、空捕获、相邻 hole 切分、`Nothing` 主语、`{{`/`}}` 转义、既有同名变量、`Option Compare Text` 交互）；匹配语义 speclet（回溯上限、span/流、中间分配，引用 18.10/18.11）；`Compatibility`（今天 `Case $"..."` 在 `Option Compare Binary/Text` 下的行为调查，作"模式化破坏与否"的证据底座）；作用域与 definite assignment；`Option Strict` 双路径。
- **补充证据**：主线 2018.12.19 `Like` 模式对比表；与 `proposal-string-narrowing-conversions.md`（TryParse-shape）的机制归并草案；`inactive/proposal-string-pattern-lookahead.md` 合并；原型（可用 PROPOSAL D 正则降级先跑通命令解析示例）；命令解析场景占比数据。
- **未决问题处理**：现有 4 个未决问题全部升级为规范小节；新增——`{x}` 语义翻转的语法化解（仅 `ShapeOf` 语境 vs 新定界）、`Like` 模式与插值逆运算的边界、`Option Compare` 归属、typed hole 划给 TryParse-shape 后的作用域。

---

## 附录：C# 生态与互操作考量

> 本附录是**追加的考量**，不改写原 meeting 正文。依据 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引）评估本提案（字符串模式匹配 / 插值逆运算）在 C#/CLR/.NET 生态现实中的位置。C# 是 CLR 新特性与 .NET 生态的**主要推动者**，VBScript.NET（.vbx）必须能适应这些现实变更；Anthony 主张 VB 保留特色，而 VB LDM 主线曾取"做 C# 变体"、Anthony 离职后仅做兼容——本附录给出「C# 现实方向 vs 本提案响应」的对应。凡引用 C# 原文均逐字准确并标注来源文件；无法核实的标 **Suspect** 或 **OPEN QUESTIONS**。诚实声明：本提案与 C# 的字符串模式**关系中等偏弱**——C# 无插值逆运算先例（meeting 正文已言"C# 用正则/`is` 模式"），但 C# 的常量模式、列表模式、`Span<char>` 模式给本提案的**受限子集**提供了可对照的工程先例，而 `Option Compare` 大小写面是 C# 无对应的 VB 特有分叉。

### 相关 C# 现实方向（索引 T2：Span 低层主线与"免拷贝字符串匹配"）

C# 对"字符串模式匹配"的**全部语言投入**可归为三类，均不是本提案的"结构化捕获 / 插值逆运算"。

**方向一：常量模式（constant pattern，C# 7）——C# 对"字符串精确匹配"的全部语言支持。** `s is "123"`、`s switch { "ABC" => ..., _ => ... }`。捕获？没有——常量模式只做等值测试，不绑定任何变量。C# 原文逐字：

> "A constant pattern tests the value of an expression against a constant value."
> "Otherwise the pattern is considered matching if `object.Equals(e, c)` returns `true`."
> → `proposals\csharp-7.0\pattern-matching.md`（Constant pattern）

**方向二：列表模式（list patterns，C# 11）——把 `string` 当字符序列匹配，是 C# 最接近"前缀锚定 + 捕获"的构造。** `s is ['A', .. var rest]`：`..` 切片把"剩余字符"绑到 `rest`。对 `string` 的降低方式 C# 原文逐字：

> "For `string` and arrays, `string.Substring` and `RuntimeHelpers.GetSubArray` will be used, respectively."
> → `proposals\csharp-11.0\list-patterns.md`（Lowering）

**方向三：`Span<char>` / `ReadOnlySpan<char>` 常量模式（C# 11，Any Time）——"匹配但不想整串拷贝"的性能主线。** 在 string 常量模式上扩到 span，按 `SequenceEqual` 免拷贝匹配；大 switch 沿用字符串的 hash 跳转表优化。C# 原文逐字：

> "Permit pattern matching a `Span<char>` and a `ReadOnlySpan<char>` on a constant string."
> → `proposals\csharp-11.0\pattern-match-span-of-char-on-string.md`（Summary）

> "Over 6 cases, the compiler will take the hashcode of the string and use it to implement a jump table to reduce the number of string comparisons necessary."
> → `meetings\2020\LDM-2020-10-07.md`（ReadOnlySpan<char> patterns）

> "an input value of type `Span<char>` or `ReadonlySpan<char>` can be matched with a constant string pattern (`span is "123"`)."
> → `Language-Version-History.md`（C# 11 条目）

**方向四：正则表达式不是语言特性，是库 + source generator。** C# 语言层对正则的"帮助"只有让正则字符串好写（raw string literals 的动机之一）；编译期正则走 .NET 生态的 `[GeneratedRegex]`（属 dotnet/runtime，不在 csharplang 库）。C# 原文逐字：

> "Languages that have `{` as a core character (examples being JavaScript, JSON, Regex, and even embedded C#) would now need escaping, undoing the purpose of raw string literals."
> → `proposals\csharp-11.0\raw-string-literal.md`（Detailed design, interpolation case）

索引浓缩判断（T2）：C# 持续往「栈上安全低层类型」投入（Span/ref struct），`pattern-match-span-of-char` 正是该主线在模式匹配侧的落点——**匹配字符串但不想整串拷贝**。

### 现实 vs 提案：兼容 / 冲突 / 需桥接 / 脱节

- **等值子句（`Case "/clear"`）↔ 常量模式：兼容，无需桥接。** meeting RESOLUTION 3 决定 `Case "/clear"` 保持等值子句；C# 的字符串"模式"恰恰就是等值（`object.Equals`）。两边语义同构，VB 等值 Case 与 C# `is "..."` 互不干扰。
- **插值逆运算（PROPOSAL A）↔ C# 现实：无先例（强化 Table）。** C# 的模式语境（`is`/`switch`）**从不复用"字符串表达式"做捕获**——常量模式无捕获、列表模式捕获（`[.. var rest]`）只出现在显式 `[...]` 形态里。meeting Q1/Q5 的"同一 `$"..."` 两种含义"在 C# 没有对应物，因为 C# 压根没有"插值即模式"。C# 的实践反过来说明：**捕获必须落在显式模式语境**，与 meeting「插值即模式」Reject 的结论同向。
- **前缀锚定 + 尾捕获（PROPOSAL B）↔ 列表模式 slice：兼容、可借鉴。** C# 11 已交付"字面量前缀 + `..` 捕获剩余"，string 降低走 `Substring`（线性、无回溯）——证明"受限、无回溯、零分配"的字符串捕获是主流语言可落地的语义。但**语义层级不同**：C# 捕获的是 `string`/`ReadOnlySpan<char>` 切片（字符序列），不涉及"按类型解析字段"——typed hole 在 C# 无对应（C# 手动 `int.TryParse`），正好呼应 meeting 把 typed hole 划给 TryParse-shape 的决议。
- **正则（PROPOSAL D）↔ 生态：编译期正则可行，但属库/source-gen 层。** C# 现实 = raw string literals 免转义 + `[GeneratedRegex]`（AOT 友好）。PROPOSAL D 若落地应生成对 `Regex` 的编译期调用（或对接 `[GeneratedRegex]`），而不是把正则语义编进语言——与 meeting 放 OTHER DESIGNS CONSIDERED 的判断一致，但生态给出一条**可实现的下降路径**。
- **`Option Compare Text`（大小写）：VB 特有、C# 无对应（需桥接/脱节）。** C# 字符串比较默认大小写敏感（`==`/`object.Equals`），语言层无"编译选项影响模式匹配大小写"的先例。meeting Q6 把 `Option Compare` 交互列为 OPEN QUESTION、`Probably` 交 case-insensitivity 提案统一收口——C# 生态既无法背书也无义务背书这一 VB 特有面，这是 VB 必须自行定义的兼容面。
- **`Nothing` 主语：C# 有可引先例。** C# LDM 对 `Span<char>` 常量模式明确不允许用 `null` 匹配。C# 原文逐字：

> "We will not allow `Span<char>`s to be matched against `null` constants."
> → `meetings\2022\LDM-2022-02-23.md`

与 meeting Q2 对 `Nothing` 主语的 lean（`Probably` 不命中）同向，可作外部支撑。

### 对 VBScript.NET 的适应建议

1. **默认安全/按需动态：字符串分发保留。** 命令/路由/配置解析是 VBScript 血统的母语（meeting 已言），C# 的"零分配匹配"方向不改变这一点，只提示实现方式。
2. **受限子集（若家族推进）照 C# 零分配降低。** PROPOSAL B 的"线性扫描零中间分配"（RESOLUTION 6）有 C# 现成先例：`Span<char>` 常量模式按 `SequenceEqual` 免拷贝、列表模式的 string slice 按 `Substring`/`Range` 只在成功路径物化。.vbx 实现时应按**索引/span**进行、不拼中间串——这是 meeting Q10/Q11 与原文 18.11 的约束，C# 已证明工程可行。
3. **字符串 switch 跳转表优化可对齐。** C# 对大字符串 switch（>6 case）用 hashcode 跳转表（LDM-2020-10-07 原文，见上）。VB `Select Case` 字符串分发同样高频，.vbx 可直接借鉴该优化（跳转表放 `PrivateImplementationDetails`）——编译器已验证的优化，非新语言特性。
4. **source-gen 桥（PROPOSAL D 的现实出口，可选）。** 若未来做编译期正则，应生成对 `Regex` 的调用或引用 `[GeneratedRegex]`（dotnet/runtime）——AOT 友好、零运行时反射。细节本附录未核实（见 OPEN QUESTIONS）。
5. **元数据识别：本提案的桥接义务很轻。** 与 `ShapeOf`/closed classes（需识别 `IsClosedType` 等新元数据）不同，字符串模式不依赖新 CLR 元数据；唯一相关的是 C# 编译字符串 switch 时生成的哈希表（`PrivateImplementationDetails`），VB 按惯例生成即可，无"识别 C# 新属性"的义务。真正的**脱节点**是 `Option Compare Text`——C# 无对应，须在 case-insensitivity 提案内自行定义。

### 对既有 RESOLUTION/三态判定的影响

C# 生态考量**不改变**原判定，在四点提供外部证据：

1. **插值即模式（PROPOSAL A 语法）→ Table 被强化。** C# 现实证明"字符串捕获"从不在字符串字面量/表达式语法里发生——C# 的捕获（`[.. var rest]`）只在显式模式语境。meeting Q1/Q5 的翻转问题在 C# 无先例；RESOLUTION 2「语法必须显式区分模式语境」获主流语言背书。
2. **前缀锚定 + 尾捕获（PROPOSAL B）→ Consider 被强化。** C# 11 列表模式 + `Span<char>` 常量模式证明"受限、无回溯、零分配"的字符串捕获可落地且已交付；VBScript.NET 的受限子集有工程先例可循。
3. **RESOLUTION 7（匹配语义不冻结）补充实现依据。** 匹配语义 speclet 应把 `pattern-match-span-of-char-on-string`（`SequenceEqual`）与列表模式降低（`Substring`/`Range`）列为"零中间分配"的实现参考；meeting 的 18.10/18.11 约束在 C# 现实中有对应工程实践。
4. **新增一项轻量关注（原 RESOLUTION 未覆盖）**：若 .vbx 消费 C# 生态中 `[GeneratedRegex]` 生成的 partial 方法（`Match`/`IsMatch`），VB 编译器需能正常调用 source-gen 产物——普通方法调用即可、无特殊元数据，**OPEN QUESTION** 级别。

### OPEN QUESTIONS

- `[GeneratedRegex]` 的确切机制与生成方法签名（源在 dotnet/runtime，csharplang 库无正文）——本附录未核实，**OPEN QUESTION**。
- C# `string` 列表模式的 slice 捕获（`[.. var rest]`）是否提供 VB「尾捕获到下一个字面量片段前沿」的直接模板，还是需 VB 自定义（C# 捕获的是固定 `Range` 切片，非"到字面量前沿"）——C# 未覆盖该语义，本附录未核实先例，**Suspect**。
- `Option Compare Text` 与 C# 大小写敏感模型在互操作层的冲突面（.vbx 调用 C# 编译的字符串 switch 时，两边大小写行为不同是否产生可观察差异）——本附录未核实，**Suspect**。
- C# 11 UTF-8 字符串字面量（`u8` → `ReadOnlySpan<byte>`）是否允许模式匹配，LDM-2022-02-23 列为未决 open question——若 .vbx 走 UTF-8 字符串（原文 18.11 / `proposal-string-span-utf8.md` inactive），该未决点同样适用，**OPEN QUESTION**。
