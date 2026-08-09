# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上回我们把字符串模式匹配（插值逆运算）判为 Table，并把 v1 形状收敛到"前缀锚定 + 字面量片段 + 单一尾捕获 + 无回溯 + 线性扫描零中间分配"。今天讨论的是同族里另一份被归档在 inactive 的建议——Anthony 第 18 章 18.10 节"字符串模式的前瞻与回溯"。它不是一份通常意义上的特性建议：原文没有给任何语法，只列了三个问题和一个设计要求。所以本次会议的问题不是"该用什么语法"，而是更前置的：**这些问题是特性、约束、还是该扔给 Regex 的东西？这份 inactive 建议应不应该被激活？**

_Note on honesty: this note is a review of an inactive ModVB proposal (`inactive/proposal-string-pattern-lookahead.md`，来源：Anthony D. Green 原文第 18.10 节，`..\..\AnthonyDesign_wordpress.txt` L2984–3006)。与同系列会议一致，我们把**事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`** 显式分层；凡引用 vblang 主线会议的内容均与 `..\..\..\vblang\meetings/` 逐字核对过，找不到对应材料处如实标注。C# 相关描述为背景知识（不在 vblang 记录内），已标注。_

## Agenda

* [ModVB Proposal（inactive）— 字符串模式的前瞻与回溯](#modvb-proposalinactive字符串模式的前瞻与回溯)

## ModVB Proposal（inactive）— 字符串模式的前瞻与回溯

_Related: [vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；ModVB：字符串模式匹配（Table）、string-span-utf8（inactive，18.11）、named-pattern-inputs（inactive，18.12）、case-insensitivity（inactive）、插值字符串优化_

We opened with the proposal's own framing. 建议通篇只有动机，没有设计：三个问题加一个"应考虑"的设计要求。我们把 Anthony 原文逐字摘出（18.10）：

> As-is pattern-matching evaluates patterns from the outside to the inside, left to right. This raises several problems:
> - A naïve implementation of String pattern matching would allocate many needless intermediate garbage strings.
> - Certain common patterns need backtracking.
> - Inner patterns can't influence decomposition of the outer pattern and lazy or greedy ways.
>
> It would be good in the design of the string pattern matching to consider how the feature would apply to a text window/stream or a span.

We agree with all three observations——它们在上回的字符串模式会议里其实已经以"边界未定义"的形式出现过。但我们也立刻注意到：**这三条不是同一种东西**。第一条是"实现不能怎么怎么做"（约束），第二条是"匹配引擎要支持什么"（能力），第三条是"匹配结果要不要给用户调节"（语义选择）。把三种不同性质的问题并成一份提案，是这份文档最值得商榷的地方——我们分头讨论，结论也各不相同。

### 场景与缺口

逐条展开三个问题，并各给一个能编译的最小例子，否则没法谈。

**问题 1：中间垃圾字符串。** 朴素实现按"外→内、左→右"匹配，每解开一层就用 `Substring` 切一次：

```vb
' 朴素实现（坏）：先 StartsWith、再 Substring(6)，为每条命令分配一个中间串。
If command.StartsWith("/echo ") Then
    Dim rest = command.Substring(6)   ' ← 分配的中间串，用完即弃。
    ' ... 继续在 rest 上匹配 {message}
End If
```

在路由 / 日志这类"每条消息都要过一遍"的热路径上，这是可测的浪费。`Probably` 问题 1 是三个问题里**唯一没有争议**的一个：它不改变语义，只约束实现。零中间分配应当是对匹配器的硬性要求，而不是可选项。

**问题 2：回溯。** "常见模式需要回溯"具体指什么？我们想出的最小例子是"定界符出现在捕获内容里"：

```vb
' 模式 $"{key}={value}" 对 "name=John=Smith"
'   非贪婪（左到右、字面量锚定）：key 取到第一个 '=' 前 → key = "name"，value = "John=Smith"。
'   贪婪：key 取到最后一个 '=' 前 → key = "name=John"，value = "Smith"。
' 两种切分都"说得通"，语义必须二选一，否则同一模式在不同实现下结果不同。
```

以及"嵌套定界符"：

```vb
' 模式 $"({content})" 对 "(a(b)c)"
'   非贪婪（左到右、字面量锚定）：content = "a(b"，然后模式结束、字符串还剩 "c)" → 整串不命中。
'   回溯：先按上面失败，再试 content = "a(b)c"，尾字面量 ')' 命中 → 整串命中。
' 注意：真·嵌套括号需要的是平衡匹配（计数），连正则的经典实现都不保证——
'   回溯能救 "a(b)c"，救不了 "((a)(b))"。这是"回溯"与"嵌套"两个不同的能力。
```

`Suspect`：Anthony 说"某些常见模式需要回溯"，但没给清单。我们列出的两个场景里，"delimiter-in-capture"在非贪婪默认下其实**能得到直观结果**（`key="name"` 对大多数人是对的），不一定需要回溯；真正需要回溯的是"想让尾巴上的字面量决定前面的切分"，例如 `$"{protocol}://{rest}"` 对 `"http://x:y"` 时让 `rest` 吃掉冒号。需要真实数据才能判断这类场景的普遍性。

**问题 3：内层模式影响外层分解 + lazy/greedy。** 上一条的 `{level}: {message}` 就是例子——`{level}` 该贪婪吃到最后一个 `:` 还是停在第一个 `:`？Anthony 的表述"inner patterns can't influence decomposition of the outer pattern"指向一个更深的问题：当模式嵌套（如 `{a} {b As Integer}` 里 `{b As Integer}` 要按整数解析），内层的**解析失败**是否应该让外层重新选择切分？例如 `$"{name} {count As Integer}"` 对 `"x y 5"`——先按非贪婪 `name="x"`、`count="y 5"` → 转换失败；是否应回溯重试 `name="x y"`、`count="5"`？这比纯字符串回溯更贵，而且引入"转换失败驱动的回溯"这一整类语义。

**设计要求：文本窗口 / 流 / span。** 原文末句要求匹配能作用到 `text window/stream` 或 `span`。这与 18.11（string-span-utf8，inactive）是同一件事的两面——一个讲匹配器要能啃 span，一个讲 VB 字符串处理要对 span 同样出色。`Probably` 这应当并入 18.11 工作线，而不是在本建议里单独开花。

### 候选方案

We considered five shapes the feature could take. 注意，因为建议本身没有语法，方案之间的分界是"把三个问题分别处理到什么程度"。

**PROPOSAL A — 零分配线性匹配（把问题 1 落实为对匹配器的硬性约束）。** 模式被编译成对源字符串/span 的索引游标操作；字面量片段用 `StartsWith` / `IndexOf` 等价操作定位；只有最终绑定的捕获段才切片、且只切一次。匹配顺序保持"外→内、左→右"，失败即停，不产生中间字符串。这就是上回家族会议已经定下的 v1 实现面。本方案不新增任何语言语法，只是把"不许分配垃圾"写进规范。

**PROPOSAL B — 受限回溯（问题 2 的最小支持）。** 在每个捕获处允许尝试若干个切分点，并设一个**回溯预算**（例如整条模式最多重试 N 次，超限视为不命中）。候选顺序取"最早成功"（即非贪婪优先）。这让 `$"{protocol}://{rest}"` 这类"尾巴定切分"的模式可用，同时用预算挡住指数级陷阱。代价：匹配不再是单遍线性扫描，编译器要为每个模式生成回溯机；IDE 的"此模式能否匹配"分析也随之变复杂。仍解决不了嵌套定界符。

**PROPOSAL C — 显式 lazy/greedy 语法（问题 3 的直接答案）。** 把贪婪程度暴露成语法，两种味道：**C1** 正则式符号（`*?`、`+?` 或专用标记）；**C2** VB 关键字（如 `Lazy` / `Greedy` 修饰捕获）。我们倾向两种都拒绝：C1 既不 VB、又与 `Like` 模式里 `*`/`?` 的既有含义撞车（见 Q1）；C2 给每个捕获加修饰词，仪式感与"读起来像英语"的原则对着干。`Probably` 更正确的答案不是"让用户选贪婪"，而是"定死一个默认，不给选择"。

**PROPOSAL D — 编译期降级为正则表达式（18.12 线索）。** 把插值模式在编译期翻译成正则、交给 `System.Text.RegularExpressions.Regex`（带超时）执行，再把捕获组绑定到变量。回溯、贪婪、前瞻、字符类全部由 BCL 引擎背书，我们一行匹配代码都不用写。Anthony 自己在 18.12 节留了门缝（原文逐字）："Some hypothetical patterns would benefit from additional 'inputs' (e.g. `Regex("\d+", n)`) but currently the 'argument' list for a named pattern is output only." 代价是把正则的失败语义、组号索引、转义规则（`\` vs `{`）泄漏进语言；`{x As Integer}` 到 `(\d+)` 的映射也是新一套约定。放 `OTHER DESIGNS CONSIDERED` 档——它是实现 B 的最廉价引擎，但不是语言设计的终点。

**PROPOSAL E — 什么都不做（保持 inactive，并入 family speclet）。** 三个问题作为"匹配语义 speclet"的设计约束记录：零分配是硬要求（A 的内容）；回溯 v1 不做、交给 Regex（D 的生态）；贪婪定死单一默认、不暴露语法（C 的否答案）；span 划给 18.11。建议本身不激活，只作为约束日志留存，附激活信号。

### 权衡：LDM 追问清单

We worked through the twelve-question checklist. 决定性的是 Q2、Q4、Q9、Q12；Q1 因为"没有语法"而提前退场。

**Q1. 语法/文法歧义：** 无——这份建议没有语法，歧义无从谈起。`Probably` 这是文档"诚实"的地方：它知道自己没资格谈语法。但反过来，任何想把贪婪/回溯做成特性的方案（C、D）都会立刻撞上已有的字符串模式符号：家族文法（主线 2018.12.19）已经收入 `'Like' StringExpression // Like pattern`，而 `Like` 用 `*`（零或多）、`?`（单字符）做通配符；插值模式若复用 `*`/`?` 表达量词，与 `Like` 语义冲突。**未来任何量词语法都要先解决与 `Like` 的符号冲突**——这是本家族最便宜的"语法禁令"。

**Q2. 角案例/边界语义：** 这是问题 2/3 的主战场。逐条：

- **匹配边界**：`Case` 语境下是整串匹配（正则 `^...$` 意义）还是子串搜索？`Like` 的既有语义是整串通配（`"abc" Like "b"` 为 False，事实）——我们 `Probably` 沿用它：**整串锚定，不是子串搜索**。这样 `Case $"/echo {message}"` 对 `"x/echo hi"` 不命中。锚定是让"无回溯 + 确定性"成立的前提。
- **贪婪默认**：`$"{key}={value}"` 对 `"name=John=Smith"`——非贪婪得 `key="name"`、贪婪得 `key="name=John"`。二者都"合理"，必须定死一个。我们 `Probably` 定**非贪婪**（每个捕获取到下一个字面量片段前沿为止），因为它与"外→内、左→右"的直觉一致，也是上回家族会议 v1 形状的推论。贪婪留给 Regex。
- **空捕获**：`$"{key}={value}"` 对 `"=x"`——`key` 取空串？`Like` 先例是允许 `*` 匹配空串（`"bc" Like "*bc"` 为 True，事实）。我们 `Probably` 允许空捕获，但要在规范里明说"两个相邻字面量片段之间的捕获可以为空"。
- **相邻 hole**：`$"{a}{b}"`——两个捕获之间没有字面量锚点，非贪婪下 `a=""`、`b=整个串`，切分不定。v1 应**禁止相邻 hole**（这在上回家族会议已隐含：字面量片段是切分锚点，没有锚点就没有切分）。
- **转换失败驱动的回溯**（问题 3 的深层形式）：`$"{name} {count As Integer}"` 对 `"x y 5"`——先切 `name="x"`、`count="y 5"` 转换失败，要不要重试 `name="x y"`、`count="5"`？We think 这是最危险的设计空间：它把**类型解析**变成匹配的一部分，等于在 TryParse-shape（`proposal-string-narrowing-conversions.md`）之外再造一个类型驱动解析入口。v1 明确**不做**——typed hole 的转换失败 → 分支不命中，不做回溯重试。类型驱动的多字段解析是另一个特性，不是本建议的问题。
- **`Nothing` 主语**：模式是否命中？未定义。`Probably` 不命中，与 `TypeOf ... Is` 的精神一致（"cast can occur"的否定侧）。
- **大小写**：字面量片段在 `Option Compare Text` 下的比较行为——见 Q6。

**Q3. 作用域与绑定：** 回溯不改变捕获变量的作用域（仍是 Case 子句作用域的新局部变量），但它改变**"哪个切分结果被绑定"**。因此语义必须定义"匹配成功 = 哪个解"：v1 定死**左到右、非贪婪、最早成功的切分点**——这与 .NET Regex 的默认（左到右、贪心最长）不同，我们刻意选非贪婪因为它确定性强、实现简单。`Suspect`：如果未来引入 B 的受限回溯，"最早成功"与"预算内最早成功"在边界上（预算耗尽但存在更晚的解）需要明确的失败语义——超预算算"不命中"还是"编译期警告"？我们 `Probably` 算不命中并给出可选的诊断，但这是 OPEN QUESTION。

**Q4. 与既有特性的交互：** 三重重叠 + 一个先例。

- **与 Regex（最大对手）**：.NET BCL 已经有一个成熟的回溯引擎，带超时（防止灾难性回溯）、贪婪/懒惰量词、前瞻/后顾、原子组。`Probably` 这是本建议最重的对手：**回溯能力在平台上已存在，语言再造一遍 = 第二种做事方式**（原则 #3）。我们反复说过"扩展表面积的门槛极高"。唯一能说服我们做 B 的理由是"Regex 的仪式感太重"——但那是字符串模式匹配整个特性的动机，不是本建议的增量。
- **与 `Like` 运算符（先例）**：VB 从 VB6 起就内置通配字符串模式——`Like`，`?`/`*`/`#`/`[...]`，且 `*` 的语义（零或多）本身**要求运行时在多种长度间尝试**——即"回溯"在 VB 的 `Like` 里早已存在。`Probably` 实现层面 `Microsoft.VisualBasic.CompilerServices.LikeOperator` 内部就做回溯式匹配（需验证具体算法，`Suspect`）。这意味着：**回溯不是 VB 的新概念**，但 VB 从未把贪婪程度暴露成语法。历史答案一直是"运行时给你一个合理的默认，不许用户调"。我们倾向延续这个答案。
- **与插值字符串**：若模式复用 `$"..."`，则 `{x}` 捕获/引用翻转（上回家族会议 Q1/Q5 的硬伤）原样继承。回溯不改变这个，只是再叠一层语义。上回已判"插值即模式"载体 Table，本建议不复活它。
- **与 Span/UTF-8（18.11）**：若匹配器要啃 `ReadOnlySpan(Of Char)`，回溯机更难写——span 是 ref struct，回溯状态机（保存多个切分候选）不能把 span 存进堆上对象，只能在栈内或内联展开。零分配 + 受限回溯 + span 三个要求同时满足，实现难度非线性上升。这是把"回溯"和"span"绑在同一条线上的真实成本。
- **与 Option Compare / case-insensitivity**：case-insensitivity 提案（inactive）引原文确认——"This issue becomes worse with String and JSON pattern matching"，且 `Option Compare Text` "只影响比较运算符、`Select Case` 以及 VB 运行时库中的某些 API"。字面量片段的比较归属必须在模式 speclet 里显式定义，不能默认继承。

**Q5. Breaking change：** 本建议没有任何语法，直接零破坏。但它是给"一个还没出生的特性"写的约束——真正的兼容性风险全部在它服务的字符串模式匹配上（上回已判：`Case $"/echo {name}"` 今天合法，模式化后改变重编译行为）。`Probably`：本建议的激活**不会引入新的 breaking**，但它必须在字符串模式匹配的 Compatibility 分析里作为"匹配语义"一节出现，否则就与载体脱节。

**Q6. Option Strict / 编译选项分叉：** 匹配本身是运行时行为，与 Strict 无关；但两件事分叉。其一，`Option Compare Text` 下字面量片段的比较（见 Q4）——回溯与否不影响比较语义，但 `Probably` 建议把字面量比较统一收口到 case-insensitivity 提案，模式 speclet 不自己定义。其二，typed hole 的转换（划给 TryParse-shape）在 Strict On/Off 下的行为须一致（运行时行为，`Suspect` 需宽松路径验证）。本建议自身不引入新分叉。

**Q7. IDE / IntelliSense 影响：** 无回溯的 v1（A）对 IDE 是友好的：模式可以线性分析，"此分支永不命中""捕获未使用"等诊断是精确的。一旦允许回溯（B）或贪婪（C），IDE 就退化到近似分析——它得模拟回溯机才能给出可靠诊断，否则就会误报。`Probably` 这是反对 B/C 的一个被低估的成本：**确定性匹配是 IDE 诊断正确性的免费午餐**。

**Q8. 数据 / 普遍性：** 三个问题里，问题 1（分配）对路由/日志热路径真实；问题 2（回溯）我们只能举出"delimiter-in-capture"这类自造例子，没有真实代码占比；问题 3（贪婪）我们认为用户其实不要"调节"，只要"一个直觉默认"。建议零数据、零用户请求。`Suspect`：VBScript 的分发惯用法（字符串命令 + 晚期绑定）`Probably` 是最强的普遍性证据，但它主要支撑**字符串模式匹配**，不支撑**回溯**——VBScript 时代的命令分发都是简单前缀匹配。`Probably` 80/20 拆法：80% 的命令模式是"前缀锚定 + 尾捕获"（A 就够），20% 需要真解析（Regex 已经是答案）。

**Q9. 更简替代：** 本建议的每一条都有更简替代。

- 问题 1 → 实现约束，不是语言特性。写进 speclet 即可，零语法。
- 问题 2 → `System.Text.RegularExpressions.Regex`，成熟、带超时、支持贪婪/懒惰/前瞻。今天就能写：
  ```vb
  ' 需要回溯/贪婪时，直接 Regex（BCL 自带超时，防灾难性回溯）：
  Dim m = Regex.Match("name=John=Smith", "^(.+?)=(.+)$")   ' 非贪婪 (.+?) → 取第一个 '='
  '   m.Groups(1) = "name"，m.Groups(2) = "John=Smith"。
  ' 改贪婪 "^(.+)=(.+)$" → Groups(1) = "name=John"，Groups(2) = "Smith"。
  ' 这正是"读起来像正则"——语言内建字符串模式 v1 不做这些。
  ```
- 问题 3 → 定死一个默认（非贪婪），不给语法。用户要贪婪就 Regex。
- span → 并入 18.11（string-span-utf8）。
- 结论：**三个问题的"特性化"都没有独立增量价值**；它们的价值全在于约束那个尚未出生的字符串模式匹配。

**Q10. 成本 / 优先级：** 完整 B（受限回溯机 + 预算 + 代码生成 + 诊断）是中等偏上的实现量；C 是负价值；D 是低实现成本但高语义泄漏。优先级上，字符串模式匹配家族整体还是 Table——**一个 Table 的家族，它的子约束没资格先行 Active**。在家族文法定型、字符串模式 v1 获得真实载体之前，本建议的任何激活都是空转。18.12（named-pattern-inputs）若先落地，D 路线会变便宜，但那也不是本建议自己的事。

**Q11. 运行时 / CLR 硬约束：** 无新 IL、无 PEVerify 问题。A 的实现走既有字符串/span 指令；D 走 BCL。唯一 CLR 侧关切是**零分配 + span 的合成**：`ReadOnlySpan(Of Char)` 是 ref struct，不能作为字段/闭包捕获（18.11 的 string-span-utf8 已列此 drawback），回溯机若要在 span 上保存切分候选，只能栈内处理。`Suspect`：表达式树语境无法宿主 span 匹配器（表达式树不支持 ref struct），但这不影响普通代码。

**Q12. 值不值得做：** 逐条打分（价值 × 成本 × 风险）。

- **价值**：三个问题作为"特性"几乎没有用户可感知的价值——用户不会说"我想要回溯"，只会说"我要匹配这个串"；作为"约束"则价值真实（零分配、确定性）。价值 4/10。
- **成本**：A 是写规范；B 是中等匹配引擎；C 负价值；D 中等。综合 5/10。
- **风险**：激活即与 Regex 分道（第二种做事方式，原则 #3）；贪婪语法不 VB（原则 #2）；转换失败驱动的回溯会侵入 TryParse-shape 的地盘（原则 #3 再犯）；回溯预算的失败语义未定。风险 7/10——**这不是因为本建议危险，而是因为它把自己放到了 Regex 与 Like 都站着的战场上**。

一句话：**三个问题都真实，但作为约束处理是免费的，作为特性处理是昂贵且重复的。** 这份 inactive 建议的正确归宿是"约束日志"，不是"待激活特性"。

### RESOLUTION

1. **18.10 不是特性提案，是三组设计约束。** 三个问题性质不同，必须拆开处理，不得作为一份"特性建议"激活。家族（字符串模式匹配）整体仍是 Table，其子约束没有独立优先权。
2. **问题 1（零分配）→ 采纳为对匹配器的硬性要求。** 这是三个问题里唯一无争议的：匹配按索引/切片进行，不产生中间垃圾字符串；与 18.11 的 span 关切共享实现面。写进 family"匹配语义 speclet"。
3. **问题 2（回溯）→ v1 明确不支持，交还 Regex。** .NET BCL 已有成熟的回溯引擎（带超时、贪婪/懒惰、前瞻/后顾）；语言再造一个 = 第二种做事方式。`Like` 先例证明 VB 接受"运行时给合理默认、不给用户调"，我们延续该答案。`"delimiter-in-capture"` 在非贪婪默认下已有直观结果；`"nested delimiters"` 需要的是平衡匹配，连回溯都不解决——`Probably` 本家族永远不覆盖它。
4. **问题 3（lazy/greedy）→ 定死单一默认（非贪婪 + 字面量片段 = 切分锚点 + 失败即停），不暴露量词语法。** 用户要贪婪 → Regex。我们不接受 C1（符号）与 C2（关键字）任何一种。
5. **转换失败驱动的回溯（typed hole）→ 明确禁止。** 类型解析划给 TryParse-shape（`proposal-string-narrowing-conversions.md`）；字符串模式 v1 的 typed hole 转换失败 = 分支不命中，不做"失败后重切分"。
6. **文本窗口 / 流 / span → 划给 18.11（string-span-utf8）工作线。** 本建议不单独承接；匹配器零分配与 span 兼容是两条工作线共享的实现约束，需对表。
7. **编译期正则路线（PROPOSAL D）→ Consider，受 18.12 制约。** Anthony 的 `Regex("\d+", n)` 线索（原文逐字："Some hypothetical patterns would benefit from additional 'inputs' (e.g. `Regex("\d+", n)`)")是 named-pattern-inputs（inactive，18.12）的地盘；若命名模式先支持输入参数，D 才谈得上。不预判结论。
8. **本建议保持 inactive。** 它有价值，但价值是"约束日志"；激活条件见下方"激活所需信号"。

### Implication

- 起草 family"匹配语义 speclet"，内含：整串锚定（`Like` 同义）、非贪婪默认、字面量片段为切分锚点、禁止相邻 hole、空捕获允许、`Nothing` 不命中、零分配实现约束、回溯预算 = 0（v1）、typed hole 转换失败 = 不命中、`Option Compare` 字面量比较归属（对接 case-insensitivity 提案）。
- 在 `inactive/proposal-string-pattern-lookahead.md` 上批注激活信号与本次决议，保持 inactive；在 `proposal-string-pattern-matching.md`（Table）的 Follow-up 里链回本决议，避免两份建议脱节。
- 与 `inactive/proposal-string-span-utf8.md` 对表 span 实现约束；与 `inactive/proposal-case-insensitivity.md` 对表 `Option Compare` 归属。
- 与 18.12 named-pattern-inputs 工作线建立连接：`Regex("\d+", n)` 是 D 路线的前置，不在此建议内推进。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：回溯预算的失败语义——若未来 B 被重新提起，预算耗尽算"不命中"还是"警告"？`Probably` 不命中 + 可选诊断。
- `OPEN QUESTIONS`：`"delimiter-in-capture"` 场景的真实普遍性——非贪婪默认是否在多数情况下已给出直观结果？需要真实代码证据，不预判。
- `OPEN QUESTIONS`：`"nested delimiters"` 若真的高频出现，是否意味着字符串模式需要"平衡匹配"（超出回溯、超出正则经典能力）？`Probably` 是，但我们认为这类场景应留在 Regex/专用解析器，而不是语言内建。
- `TODO`：找真实代码库里的字符串分发惯用法，量化三个问题各自的占比（尤其"需要回溯才能得到用户想要的切分"的比例）。
- `Follow-up`：与 string-pattern-matching（Table）、string-span-utf8、case-insensitivity、named-pattern-inputs 四条工作线对表后，把本次 RESOLUTION 折进各自的未决问题清单。

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 本建议无语法、零破坏；但它服务的载体（`$"..."` 模式）在上回已判有破坏风险。保持 inactive 无损。**一致**。
2. **保持 VB-like** — 无语法中立；任何贪婪标记（`*?`）都是 Regex 味、不 VB；关键字（`Greedy`/`Lazy`）仪式化。**倾向一致（因为倾向不给语法）**。
3. **不引入"第二种做事方式"** — 最重的原则。Regex 已存在、`Like` 已存在；若字符串模式 v1 只做无回溯子集，则"回溯"部分就是第三条道。**本建议的"特性化"违反，作为约束不违反**——这是它应保持 inactive 的核心理由。
4. **默认跟随 C#，除非有充分理由** — C# 没有插值字符串模式，它的"跟随"答案一直是"用 Regex"（以及 C# 11 列表模式用 `..` 切片的受限中段匹配，`Probably` 为背景知识、非 vblang 记录，未在主线核实）。VB 没有理由在回溯能力上比 C# 走得更远。
5. **读起来像英语、对新手友好** — 无回溯的简单模式（`$"/echo {message}"`）读起来像命令；带贪婪控制的模式读起来像正则。**支持 v1 不做回溯**。
6. **不为边缘场景加特性** — 回溯是 20% 场景且已被 Regex 覆盖。**不做**。
7. **避免隐蔽的语义变化** — 贪婪/非贪婪的默认选择改变同一模式的结果；这正是 2018.05.30 拒掉 `Return?` 的理由（"Control flow would be altered by a very subtle character"，事实）的同类。**定死非贪婪 + 字面量锚点，消除隐蔽性**。
8. **不与既有语法冲突** — 本建议无语法；但未来任何量词符号都要避让 `Like` 的 `*`/`?`。**不激活即无冲突**。
9. **消除常见样板** — 本建议不消除样板；它服务于消除样板的那个特性。**间接一致**。
10. **冗长只在有用时是美德** — 不引入新关键字最好。**一致**。

**主线对照（评价标准 2.3 表）**：主线在字符串模式上没有任何对应物——主线 2018.12.19 的模式家族里字符串成员只有 `'Like' StringExpression`。本建议是 **Anthony 独立延伸（第 18 章实验性）**，主线无先例。它和主线的关系是：**零分配 + 无回溯 + 非贪婪**的方向与主线 2018.12.19 的整体判据（"No breaking changes."、"we want pattern matching to be VB-like as possible"，均为事实）一致；而"语言内建回溯"与主线"默认跟随 C#"（此处 = 用 Regex）存在张力。JSON 模式匹配先例（主线 2017.10.18 决议："Table, wait for feedback/scenarios and more matching"，事实）直接支持我们对本建议保持 Table——**家族成员在"场景与证据"不足时先搁置，等更多 scenario 再说**。

**Breaking change 结论**：无（本建议零语法）。未来若"回溯/贪婪"被做进语言，其 breaking 来自载体语法（`$"..."` 模式）与符号冲突（`Like` 的 `*`/`?`），不在本建议内部。

### 三态判定

- **本建议（整体）**：**保持 inactive（Table）**——不是 Reject（三个问题真实、作为约束有价值），是"家族文法定型前不激活"。状态标签 `LDM In Process`（家族内），不标 `LDM Rejected`。
- **分解后**：
  - 零分配实现约束（问题 1）→ **采纳为 family speclet 硬性要求**（Active within speclet）；
  - 回溯能力（问题 2）→ **Table**（v1 不做，Regex 覆盖；激活信号见下）；
  - lazy/greedy 语法（问题 3）→ **Reject**（不 VB、`Like` 符号冲突、Regex 已覆盖）；
  - 文本窗口 / 流 / span → **划给 string-span-utf8（18.11）**；
  - 编译期正则路线（PROPOSAL D）→ **Consider**（受 18.12 named-pattern-inputs 制约，家族 Phase 之后）。

**激活所需信号**（本建议从 inactive 升为 active 必须同时满足）：
1. **家族文法定型**：主线 2018.12.19 的声明模式 / `Like` 模式 / `When` 家族文法在 ModVB 内落地，字符串模式获得真实语法载体（当前 `$"..."` 载体的 `{x}` 翻转问题未解前不成立）。
2. **字符串模式 v1 进入 Active**，本建议作为其"匹配语义 speclet"而非独立特性回归。
3. **出现真实代码中 "delimiter-in-capture" / "需要尾巴定切分" 的高频证据**，证明非贪婪 v1 不够——`TODO`：找数据。
4. （仅当走 D 路线时）18.12 named-pattern-inputs 先行，`Regex("\d+", n)` 成为命名模式输入。

**后续动作**：① 起草 family 匹配语义 speclet（见 Implication）；② 在 string-pattern-lookahead 与 string-pattern-matching 两份建议上互链本决议；③ 与 string-span-utf8、case-insensitivity、named-pattern-inputs 对表；④ `TODO` 量化"delimiter-in-capture"占比。

---

## 附录：特性评价

### 评价对象
- 建议：`inactive/proposal-string-pattern-lookahead.md`（字符串模式的前瞻与回溯）
- 来源：Anthony D. Green 原文第 18.10 节（`..\..\AnthonyDesign_wordpress.txt` L2984–3006）；相关：18.11（span/UTF-8）、18.12（命名模式输入，含 `Regex("\d+", n)` 线索）
- 配方目标：在设计字符串模式匹配时解决"中间垃圾分配 / 回溯 / lazy-greedy / span"四件事。原文仅陈述问题与设计要求，未给语法。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 2/5 | 锚点≈"Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。三个问题真实，但无任何示例、无原型、无量化的改进指标；"回溯"无场景清单；无法判断达到什么程度算达成。未决问题 4 个且全是"如何设计"→ 效果证据封顶。 | 已检查（已提供，未运行） | 无示例、无原型、无数据；"回溯场景"未列 |
| 特性 | 3/5 | 锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。本身无语法载体；作为约束，"零分配/无回溯/非贪婪"与 VB 保守基因一致，但"回溯/贪婪"与 Regex 完全重叠且未做 VB 化论证；打包了三个性质不同的问题（约束/能力/语义选择）而无区分。 | 已检查 | 三个问题性质混淆未分置；未对照 `Like` 先例 |
| 品质 | 2/5 | 锚点≈"多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节模板齐全但**全部空泛**：Detailed design 只是"原文未给语法"的记录；Drawbacks/Alternatives/Unresolved 各只有条目无论证；全文零示例。诚实标注"实验性/未定稿"是加分，但不足以抵消实质内容缺失。 | 已检查 | 无语法、无示例、无论证；未决问题都是"如何设计"而非设计点 |
| 属性 | 3/5 | 锚点≈"有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（零分配真优化）正向；但若激活会拖慢迭代（匹配引擎工作量）、与 Regex/`Like` 同质化或符号冲突（风/暗）；"转换失败驱动的回溯"侵入 TryParse-shape 地盘未识别。文档 Drawbacks 三条均轻描淡写，未触及最重的"与 Regex 重复"。 | 已检查（待定，预测性） | 与 Regex 重复未权衡；TryParse-shape 边界未识别；无对冲设计 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注；标注与影响有偏差"。来源标注准确（18.10，逐字引用）；但未标注 18.12 的 `Regex("\d+", n)` 线索（直接决定 D 路线可行性）、未对照 `Like` 先例（回溯在 VB 非新概念）、未交叉标注同族 string-pattern-matching（Table）与 string-span-utf8（18.11）。 | 已检查 | 18.12 线索未标注；`Like` 先例未对照；家族血缘未交叉标注 |

### 设计原则对照
- **与 VB 基因：混合**——"零分配 / 无回溯 / 非贪婪 / 不给语法"符合保守基因（原则 3、5、7、8）；"语言内建回溯 / 贪婪控制"偏离（Regex 味、与 `Like` 符号冲突）；无语法载体本身中立。整体倾向"约束化"而非"特性化"。
- **与主线关系：Anthony 独立延伸（第 18 章实验性），主线无对应物**——主线模式家族字符串成员仅 `Like`；本建议方向（确定性、零破坏、VB-like）与主线 2018.12.19 判据一致，但与"默认跟随 C#（用 Regex）"存在张力，可通过"v1 不做回溯"消解。与 18.11（span）、18.12（named-pattern-inputs）同属第 18 章实验族。
- **破坏性变更：无**——零语法。未来若做贪婪/量词语法，潜在破坏来自与 `Like` 的 `*`/`?` 符号冲突，需避让；不在本建议内部。

### 总评
- **达成程度：未达成（作为特性）；部分达成（作为约束记录）**——三个问题被真实陈述并逐字引用，作为"约束日志"有价值；但没有任何设计、示例、证据或机制论证，作为特性完全未成型。
- **LDM 三态建议：保持 inactive（Table）**——不是 Reject；激活信号 = 家族文法定型 + 字符串模式 v1 进入 Active + delimiter-in-capture 真实数据（（或）named-pattern-inputs 先行以走 D 路线）。
- **主要问题**：① 三个问题（分配/回溯/贪婪）性质不同却并置一份提案，导致无法单独判定；② 回溯与贪婪能力与 .NET Regex 完全重叠，未论证"语言为何再造一遍"；③ 未对照 `Like` 先例（回溯在 VB 已存在）；④ 未标注 18.12 `Regex("\d+", n)` 线索；⑤ 与 string-pattern-matching（Table）的依赖关系未标注——它服务的载体已被判 Table。

### 返工建议
- **拆分**：把一份提案拆成三份材料——①"匹配器实现约束"（零分配 + 整串锚定 + 非贪婪 + 无回溯 + 禁止相邻 hole），作为 family speclet 的一章；②"回溯需求收集"（列出 delimiter-in-capture / 尾巴定切分的具体示例 + 真实代码占比数据），作为"v1 后是否扩展"的证据；③"贪婪语法探索"（对比 Regex 符号 vs VB 关键字 vs 不给语法），明确倾向"不给语法"。
- **补充示例**：能编译的 VB 代码演示"今天如何用 Regex / `Like` / `StartsWith` 完成各回溯场景"——作为对照基线，证明现状已覆盖。
- **补充对照表**：Regex 能力矩阵（回溯 / 贪婪 / 懒惰 / 前瞻 / 后顾 / 原子组 / 超时）vs 字符串模式 v1 能力，逐格说明归属。
- **补充血缘**：标注 18.12 `Regex("\d+", n)` 与 named-pattern-inputs 的关系；对照 `Like` 的 `*` 回溯先例；交叉链接 string-pattern-matching（Table）、string-span-utf8（18.11）、case-insensitivity。
- **未决问题处理**：把四个"如何设计"改写为具体设计决策点，并给出 LDM 的倾向答案——回溯预算 = 0（v1）；贪婪默认 = 非贪婪；锚定 = 整串；typed hole 转换失败 = 不命中、不做重切分；span = 划给 18.11。

---

## 附录：C# 生态与互操作考量

_本节把本提案（18.10 字符串模式的前瞻与回溯，inactive）放到 C#/CLR/.NET 现实方向上对照。C# 现实方向取自 `..\..\..\csharplang-index.md`（索引，重点 T2/T5/T7）；凡引 C# 原文均与 `..\..\..\csharplang` 镜像逐字核对并标注来源路径；镜像内无法核实处标 `Suspect` 或 **OPEN QUESTIONS**。与正文一致，C# 侧描述属背景知识，不在 vblang 记录内。本提案与 C# interop 的关系集中在"字符串/序列模式匹配"与"span/正则生态"两条窄线上，其余 C# 低层主线（ref fields、函数指针、unsafe evolution）与本提案正交，如实说明、不硬凑。_

### 相关 C# 现实方向

**R1 — C# 11 列表模式（list patterns）对 string 的字符序列匹配。** C# 11 引入列表模式，把"序列匹配"做成语言特性，且直接适用于 `string`（string 满足 countable + indexable）。原文（Summary，`proposals\csharp-11.0\list-patterns.md`）：

> "Lets you to match an array or a list with a sequence of patterns e.g. `array is [1, 2, 3]` will match an integer array of the length three with 1, 2, 3 as its elements, respectively."

关键语义：`list_pattern` 逐个匹配元素，`slice_pattern`（`..`）只允许出现一次、匹配"零或多个"元素；降级为 `Length` 检查 + 索引器测试；对 `string` 的切片走 `string.Substring`。原文（Lowering 节，`proposals\csharp-11.0\list-patterns.md`）：

> "The *input type* for the *slice_pattern* is the return type of the underlying `this[Range]` or `Slice` method with two exceptions: For `string` and arrays, `string.Substring` and `RuntimeHelpers.GetSubArray` will be used, respectively."

**R2 — C# 11 `Span<char>`/`ReadOnlySpan<char>` 对常量字符串的模式匹配。** 同版 C# 允许把 `Span<char>`/`ReadOnlySpan<char>` 与常量字符串模式匹配（`span is "123"`）。原文（Summary，`proposals\csharp-11.0\pattern-match-span-of-char-on-string.md`）：

> "Permit pattern matching a `Span<char>` and a `ReadOnlySpan<char>` on a constant string."

匹配语义用 `MemoryExtensions.SequenceEqual`/`AsSpan` 定义（原文 Detailed design）：
> "If *e* is of type `System.Span<char>` or `System.ReadOnlySpan<char>`, and *c* is a constant string, and *c* does not have a constant value of `null`, then the pattern is considered matching if `System.MemoryExtensions.SequenceEqual<char>(e, System.MemoryExtensions.AsSpan(c))` returns `true`."

其动机（原文 Motivation，"perfomance"为原文拼写）："For perfomance, usage of `Span<char>` and `ReadOnlySpan<char>` is preferred over string in many scenarios." LDM 另定两条结论（`meetings\2022\LDM-2022-02-23.md`）："We will not allow `Span<char>`s to be matched against `null` constants."；"Input type must be `Span<char>` to be matched as a `Span<char>`. No implicit type tests will be emitted."

**R3 — 正则 = BCL 生态，非 C# 语言特性。** C# 语言内没有前瞻/回溯/正则语法：镜像 `proposals` 目录内 Grep `lookahead|backtracking` 仅命中词法级 lookahead（collection-expressions.md）与字符串字面量示例（extending-partial-methods.md 中的 `new RegularExpression("(dog|cat|fish)")`），无任何正则语言提案。回溯、贪婪/懒惰、前瞻/后顾、超时全部落在 BCL `System.Text.RegularExpressions`（dotnet/runtime 生态）。`[GeneratedRegex]`（.NET 7+ source generator）把正则纳入"编译期生成、AOT 友好"路线；其属性定义在 dotnet/runtime 而非本镜像——`Suspect`：属性名与命名空间未在镜像内核实。

**R4 — Span 第一公民化（C# 14）+ AOT/trimming 压力（索引 T5/T7）。** C# 14 引入隐式 span 转换，`string`、`T[]`、`Span<T>`、`ReadOnlySpan<T>` 互相可隐式转换。原文（Summary，`proposals\csharp-14.0\first-class-span-types.md`）：

> "We introduce first-class support for `Span<T>` and `ReadOnlySpan<T>` in the language, including new implicit conversion types and consider them in more places, allowing more natural programming with these integral types."

转换规则含 "* From `string` to `System.ReadOnlySpan<char>`"（原文 Detailed design）。同时 C# 15 主线（unsafe evolution、unions/closed hierarchies、extensions）整体把职责压给类型系统与 source-gen，动态/反射被视为 AOT 负担（索引 T5/T7）；这与本提案"编译期降级"的路线同向，但 unions/closed hierarchies 属类型系统工作，与字符串序列匹配正交。

### 现实 vs 提案

| 本提案要点 | C# 现实方向 | 关系 | 理由 |
|---|---|---|---|
| v1 形状：整串锚定 + 字面量片段 + 单一尾捕获 + 无回溯 + 线性扫描 | C# 11 list patterns：整串匹配 + 单一 `..` 切片 + Length 检查 + 无回溯 | **兼容（强）** | C# 已在语言里证明"无回溯、单切片、确定性"的序列模式可落地；`Probably` 本提案 v1 实现面有 C# 先例背书，家族 speclet 风险降低 |
| 零中间分配（问题 1） | C# list pattern on string 用 `string.Substring`（**产生分配**） | **兼容但更严格** | 本提案要求零垃圾，比 C# 更强；不是冲突，但实现不得照抄 C# 的 `Substring` 降级，需索引/span 算术（见下） |
| `Nothing` 主语不命中 | C# LDM："We will not allow `Span<char>`s to be matched against `null` constants." | **兼容（同向）** | 两者都把"null/空"排除在模式命中之外，语义一致 |
| 回溯（问题 2）→ v1 不做、交还 Regex | C# 无语言级回溯，正则 = BCL 生态 | **强化（无张力）** | C# 的答案同样是"用 BCL Regex"；本提案不落后于 C#，也不构成竞争 |
| lazy/greedy（问题 3）→ 定死非贪婪、不给语法 | C# list patterns 无 lazy/greedy 控制（`..` 即"零或多"，无修饰词） | **强化** | C# 同样不给量词语法；"定死一个默认"与 C# 一致 |
| 设计要求：text window / 流 / span | C# 11 常量 span 模式 + C# 14 隐式 span 转换 | **需桥接（提供协议）** | C# 已定 well-known 协议（`MemoryExtensions.SequenceEqual`/`AsSpan`）可复用；VB 的 ref struct 支持是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error，识别 `ReadOnlySpan(Of Char)` 是前置 |
| 抽象层级 | list patterns 按元素（char）逐位匹配 | **互补不可互换** | 本提案按字面量片段切分子串，C# 按 char 枚举；同一字符串两种视角，互操作边界需显式声明 |
| 插值即模式（载体 `$"..."`） | C# 无插值字符串模式（`$` 仅插值） | **脱节（独立延伸）** | 载体是 Anthony 第 18 章独立实验，无 C# 对标；不产生兼容义务也无从借鉴 |
| typed hole 转换失败 → 不重切分（问题 3 深层） | C# 无类型驱动重切分机制 | **脱节（无对应物）** | 无张力；C# 也没有"解析失败驱动回溯" |

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：v1 编译期降级 + 无回溯 → 天然 AOT/trimming 友好（无反射、无 `DynamicMethod`），与索引 T5 方向一致。把"解释执行 / 动态 Regex"做成显式 opt-in 的传统兼容层（决策文件 M5/M8），保持"默认安全、按需动态"。
2. **source-gen 桥**：20% 回溯/贪婪场景引导到 BCL `Regex` + `[GeneratedRegex]`（.NET 7+ source generator，AOT 友好），而非语言内建量词——与 C#"正则 = BCL、编译期生成替代运行时动态"完全同路。这也为 RESOLUTION 7（PROPOSAL D，受 18.12 制约）提供现成发射目标：`Regex("\d+", n)` 若落地，发射到 `Regex`/`GeneratedRegex` 而非自造引擎。
3. **识别新元数据**：① 若 18.11（string-span-utf8）落地，需识别 `ReadOnlySpan(Of Char)`/`Span(Of Char)` 与 `MemoryExtensions.SequenceEqual`/`AsSpan` 为 well-known 成员（照 C# 11 协议）；② 需认识 C# 侧新元数据属性（`CompilerFeatureRequired`、`RefSafetyRules` 模块属性，以及 unsafe-evolution 的 `RequiresUnsafe`/`MemorySafetyRules`，决策文件 M8）。unsafe-evolution 对 VB 的原话（`proposals\unsafe-evolution.md`，索引第四节已核实）：
   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   注意其含义是"VB 不必支持 requires-unsafe 成员"，但 VB 编译器**仍须识别**这些属性，才能正确校验对 C# `requires-unsafe` 成员的跨语言调用（决策文件 M8 明确列为"必须桥接"）。
4. **span 第一公民化的应对**：C# 14 隐式 span 转换后，BCL 会持续给 `ReadOnlySpan(Of Char)` 加重载；VB 匹配器与字符串处理（18.11）应把 `ReadOnlySpan(Of Char)` 当第一类输入，否则在"string 重载被 span 重载逐步取代"的生态里掉队。

### 对既有 RESOLUTION / 三态判定的影响

C# 现实**整体强化** RESOLUTION，不需要改动正文：

- RESOLUTION 3（v1 不支持回溯、交还 Regex）→ **强化**：C# 无语言级回溯，正则是 BCL 生态；VB"不比 C# 走得更远"（VB 基因第 4 条）有 C# 现实背书。
- RESOLUTION 4（定死单一非贪婪默认、不给语法）→ **强化**：C# list patterns 同样无 lazy/greedy 控制。
- RESOLUTION 6（span → 划给 18.11）→ **强化并给出协议**：C# 11 常量 span 模式已定义匹配语义与 well-known 成员，18.11 可直接复用，不必自造。
- RESOLUTION 5（typed hole 转换失败 → 不命中、不重切分）→ **无 C# 对应物**，无张力。
- 三态判定（保持 inactive/Table、作为约束日志）→ **不受影响**。C# 证据说明的是"v1 子集可落地"（家族 speclet 内容），不是"应激活本建议"，不构成激活信号。

**一处需补注（对 family speclet，而非本建议）**：C# 11 list pattern on string 的官方降级用 `string.Substring`（产生分配），而本提案 RESOLUTION 2 要求零中间分配。实现者可能拿 C# 先例当豁免——speclet 应显式写明"本家族零分配约束**强于** C# 11 列表模式在 string 上的官方降级"，禁止照抄 `Substring` 路线（改用索引算术或 `MemoryExtensions`/span 切片）。

### 引用纪律

逐字引用 + 来源（均已在 `..\..\..\csharplang` 镜像内核实）：

- "Lets you to match an array or a list with a sequence of patterns e.g. `array is [1, 2, 3]` will match an integer array of the length three with 1, 2, 3 as its elements, respectively." → `proposals\csharp-11.0\list-patterns.md`（Summary）
- "The *input type* for the *slice_pattern* is the return type of the underlying `this[Range]` or `Slice` method with two exceptions: For `string` and arrays, `string.Substring` and `RuntimeHelpers.GetSubArray` will be used, respectively." → `proposals\csharp-11.0\list-patterns.md`（Lowering）
- "Permit pattern matching a `Span<char>` and a `ReadOnlySpan<char>` on a constant string." → `proposals\csharp-11.0\pattern-match-span-of-char-on-string.md`（Summary）
- "If *e* is of type `System.Span<char>` or `System.ReadOnlySpan<char>`, and *c* is a constant string, and *c* does not have a constant value of `null`, then the pattern is considered matching if `System.MemoryExtensions.SequenceEqual<char>(e, System.MemoryExtensions.AsSpan(c))` returns `true`." → `proposals\csharp-11.0\pattern-match-span-of-char-on-string.md`（Detailed design）
- "We will not allow `Span<char>`s to be matched against `null` constants." → `meetings\2022\LDM-2022-02-23.md`
- "Input type must be `Span<char>` to be matched as a `Span<char>`. No implicit type tests will be emitted." → `meetings\2022\LDM-2022-02-23.md`
- "We introduce first-class support for `Span<T>` and `ReadOnlySpan<T>` in the language, including new implicit conversion types and consider them in more places, allowing more natural programming with these integral types." → `proposals\csharp-14.0\first-class-span-types.md`（Summary）
- "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（索引第四节已核实）

**OPEN QUESTIONS**：
- `.vbx` 是否需要与 C# list-pattern 协议互操作的"char 级序列匹配"模式（`Length` + `this[int]` + `this[Range]`/`Substring`），与"字面量片段切分"模式并存？
- `[GeneratedRegex]` 属性名与命名空间（`System.Text.RegularExpressions.GeneratedRegexAttribute`？）——定义在 dotnet/runtime，**不在 csharplang 镜像内**，本附录未能核实，标 `Suspect`。
- 18.11 若复用 C# 常量 span 模式协议，VB 的 ref struct 支持时间线需单独排期（决策文件 M7：当前 VB 基本不支持）。
