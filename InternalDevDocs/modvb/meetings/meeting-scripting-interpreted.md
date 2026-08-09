# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题同样来自 **inactive** 子目录——`proposal-scripting-interpreted.md`（Anthony 原文第 18 章 18.24 "Scripting and Interpreted Code"）。与上一场"生成式编译器脚本"（18.18，一句自评"坏主意"）相反，这份建议的原文是一句**肯定**：

> "Of course."（原文 18.24，逐字）

只有两个字，一个句号。没有动机、没有语法、没有示例、没有取舍。所以今天的任务不是评审一个设计——那里同样没有设计可评审——而是回答三件事：**"脚本与解释执行"在 VB 语境下到底可能指什么；它值不值得离开 inactive；如果值得，激活需要什么信号。** 我们开场就知道这不会是轻松通过的一场：'Of course.' 是热情，不是规格，而 LDM 的纪律是把热情与可落地分开对待。

## Agenda

* [Proposal: 脚本与解释执行（Scripting and Interpreted Code）](#proposal-脚本与解释执行scripting-and-interpreted-code)

## Proposal: 脚本与解释执行（Scripting and Interpreted Code）

_Related: [vblang #102 – Support Top-Level Statements in a Single Entry-Point File](https://github.com/dotnet/vblang/issues/102)（2017-12-06 讨论 try.dot.net 脚本方言后推迟）；[vblang #211 – Discussion / proposal: usage of any installed programming language](https://github.com/dotnet/vblang/issues/211)（"Fantastic idea, and too hard to do."）；ModVB：`meeting-top-level-code.md`（Immersive Files 窄子集，2017-12-06 #102 的延续回应）、`meeting-generative-compiler-scripting.md`（编译器脚本，同章 18.18，已明确区分）、`proposal-local-declarations.md`（`Let`）、`proposal-set-statement.md`（`Set`，VBScript 遗产）_

### 场景与缺口

We opened by admitting an uncomfortable asymmetry：**主线对这个话题有真实的历史，而 Anthony 只有两个字。** 2017-12-06 的主线记录是我们在讨论 #102 顶层语句时留下的，逐字如下：

> "Last time we discussed this proposal we decided to take a wait-and-see approach with https://try.dot.net/. Today they use the scripting dialect of C# by virtue of using the Scripting API but based on discussions with them it would be beneficial to have the simplicity of top-level statements in the language(s) proper. For one, it means that all of the documentation sites/pages that are making use of trydotnet are technically teaching a slightly different version of the language with slightly different semantics. Let's try to reconcile the standard and scripting dialects in the new year."

结论是 "Deferred to January 2018"——然后**再也没有回来过**（这与 `meeting-top-level-code.md` 的记录一致）。**"调和标准方言与脚本方言"这一目标，主线留下了一个从未兑现的欠账。** 今天我们手里这份建议，恰好就坐在那个欠账的"脚本方言"一侧：Anthony 用 "Of course." 回答的是"VB 该不该可以被脚本化/解释执行"——答案是肯定的，但肯定之后什么都没有。

我们把"脚本与解释执行"拆成三个互相纠缠、但必须分开对待的读法：

1. **REPL / 即时交互**——敲一行，立刻得结果，状态在会话间延续。这是 VB6 时代就有的东西（IDE 的 Immediate Window，`? 2+2`），是 C# Scripting API / `csi.exe` 今天的形态，也是 try.dot.net 文档站演示的形态。
2. **脚本宿主**——把一个 `.vb` 文件当脚本直接运行（`vbscript foo.vb`），不需要工程、不需要 `Sub Main` 外壳、不需要编译产物。这是 VBScript/WSH 时代的形态，也是"顶级代码"在运行侧的对应物。
3. **真·解释器**——一个全新的执行引擎，逐句解释 VB 源文本，不产 IL、不走 CLR 编译管线。这是 "interpreted code" 字面上的最强读法。

关键背景：**我们服务的产品叫 VBScript.NET。** 这不是一个边缘的、可为可不为的沙盒实验——脚本化是产品身份的一部分。而 VB 的血统里本来就带着脚本基因：VBScript（1996）是 VB 的官方脚本方言，VBA 以解释式 IDE 著称，VB6 的 Immediate Window 就是今天所谓 REPL 的祖先。**"脚本"不是从 C# 借来的外来词，它是 VB 自己丢下的 DNA**——VB.NET 走向纯编译后把它留在了 2001 年。这一层血缘是整场讨论的暗线。

但缺口不是"今天做不了什么"。缺口更准确地说是三层叠在一起：

- **产品侧**：VBScript.NET 需要一个"敲下去就运行"的入口形态，而当前管线只有"建工程 → 编译 → 跑 exe"，仪式与 #102 讨论的顶层语句缺口同源。
- **语言侧**：主线欠下的"标准方言 / 脚本方言调和"从未完成；try.dot.net 至今仍在用 C# Scripting API 教"a slightly different version of the language"。**主线的担心——文档站在教一门语义略有不同的 VB——正是今天任何 VB 脚本体验都会踩的坑，除非我们从同一个编译器出发。**
- **迁移侧**：现存的 VBScript/WSH/经典 ASP 代码库是一个真实的迁移人群，它们的松绑定、不声明变量、`Set` 语句、`On Error` 风格在 .NET 里没有现成的宿主接住。

### 候选方案

**PROPOSAL A — 自建真·解释器（greenfield interpreter）。** 按 "interpreted code" 字面意思：一个逐句解释 VB 的新运行时，支持 REPL、脚本文件、即时反馈，不经过 IL。Anthony "Of course." 若取最强读法即落于此。

**PROPOSAL B — 跟随 C# Scripting API 路线（Roslyn scripting for VB）。** 不造解释器，复用编译器即服务：`Microsoft.CodeAnalysis.Scripting` 的 VB 版——内存中逐段编译、submission 模型、`#r` 引用、跨提交状态类。这是 try.dot.net 背后的 C# 机制，也是 2018-06-13 确立的 "C# will take the lead" 的天然落点。

**PROPOSAL C — 脚本宿主 + 顶层代码窄子集（host over the entry-point subset）。** 不独立做语言特性，只做一个宿主命令（`vbscript foo.vb`）+ 复用 `meeting-top-level-code.md` 已判定的"单文件入口窄子集"（隐式模块 + 合成 `Main`）。"解释执行"在这里只是"编译进内存立即跑"的用户观感，不是新引擎、不是新语法。

**PROPOSAL D — 什么都不做（status quo）。** 保持 VB 纯编译，脚本场景交给 try.dot.net / dotnet-script / PowerShell 这些相邻工具，等主线或 C# 的采用率信号再说。

We 也同时确认了一个范围纪律：**本建议与 `proposal-generative-compiler-scripting.md`（18.18）必须分开。** 上一场会议已经把界线钉死了——脚本方言是把 VB **拿出去当语言运行**（对那条建议的回应是 "Of course."），编译器脚本是**在编译器内部跑脚本**。两者除了共享"脚本"这个词没有任何共享实现面，互不暗示、互不依赖。今天只谈前者。

### 权衡：Q&A

- **A 的诱惑在哪？** 全部诱惑是"字面上的 interpreted"——没有编译步骤，真正的即时反馈。但我们立刻意识到这是错误的资产方向。C# 面对同一个诉求选了 Scripting API 而非解释器，2018-06-13 的立场是 "C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries."。**一个解释器等于重造运行时**：语法树逐句求值、绑定、晚期绑定、异常、调试器、性能，全都要在 CLR 之外再实现一遍——2018-02-07 对 #211 那句 "It would require rewriting considerable parts of Roslyn." 在这里以更差的形式重演（那不是多语言混合，是整套语义的第二次实现）。且解释器产出的不是 IL，脚本就失去了 .NET 互操作、工具链、调试、性能这些 VB.NET 存在的原因。**A 的价值主张与 VB.NET 的存在理由相抵触。**
- **B 是不是"正确答案"？** `Probably` 是。机制上它完全合规——同一编译器、同一绑定器、同一语义，只是把"编译一次出 exe"换成"边提交边编译、状态跨提交"。这正是 #102 那句 "reconcile the standard and scripting dialects" 的字面解法：**不教第二门语言，把脚本方言做成标准语言在内存里的一个运行模式。** 但我们没有天真到忽略它的实现成本：submission 编译、跨提交状态类、`#r` 解析、调试器对脚本文件的断点映射，是实打实的 Roslyn 工程量，且 VB 没有现成的 Scripting API 可抄（C# 有，VB 没有）。
- **B vs C：REPL 和脚本宿主是一回事吗？** 不是，但共享基座。C 只解决"文件即脚本"（复用顶层代码窄子集的合成入口），B 额外解决"会话即程序"（REPL 状态类、跨提交变量）。C 是 B 的一个真子集 + 一个宿主命令。**我们的直觉：C 先落地，B 的 REPL 是 C 之上的增量**，两者必须共用同一编译器，否则就是 2017-12-06 担心的语义分叉。
- **D 是不是太保守？** 对一般 VB 受众，"quiet customers"（2018-05-30："the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now"）确实没在要脚本。但对 VBScript.NET，脚本不是"新特性"，是**产品定义**。D 等于说产品不做自己该做的事。而且 D 把主线的欠账继续挂账——try.dot.net 会继续教那门"略有不同的语言"。**D 对主线成立、对 VBScript.NET 不成立。**
- **"Of course." 值几个钱？** 我们想得很清楚：**它是方向信号，不是设计批准。** 它有真实分量——来自一个二十年 VB 从业者、对 "back-compat, tooling, performance" 声称已深思的人（原文第 18 章引言），它的肯定比"没想过"强得多；但它没有给出任何语法、任何取舍、任何场景，无法被规格化。**热情不能替代可行性——"Fantastic idea, and too hard to do" 与 "Of course, and here is the design" 之间隔着一整份建议。**
- **会不会我们过度解读了"脚本"？** 可能。`Suspect`：Anthony 的 "Of course." 更像是对"VB 应当可脚本化"这个**观点**的认同，而不是对某个具体运行形态的承诺。他没有说 REPL、没说脚本宿主、没说解释器。我们把这列为 OPEN QUESTION，并按"方向认可、形态未定"来对待。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**没有新语法可查——这是本建议的诚实，也是它的空洞。** 但我们把候选形态各自的文法面摆开：

- **脚本文件（C）**：一个 `.vb` 文件本身就是合法编译单元，不需要新文法；"文件即程序"的语法面已由顶层代码窄子集承担（`meeting-top-level-code.md` 已判定：`Imports` + 成员 + 语句在文件作用域，可解析、成员与语句句法不相交）。这里真正要定的不是文法，是**宿主规则**：什么样的文件算脚本、入口点合成规则、与"有 `Sub Main` 的普通程序"的优先级。
- **REPL（B）**：提交单元不是完整编译单元，是语句/声明序列——`Dim x = 10` 在文件里是合法语句，在"会话作用域"里语义要单独定义（见 #3）。
- **`.vbs` 扩展名陷阱**：复用 `.vbs` 会立刻拖进 VBScript 语义（无类、处处 `Dim`、`Set` 语句、`Option Explicit` 缺席）。`Probably`：新宿主应跑 `.vb`，把 `.vbs` 留给迁移工具去转换，而不是让语言退化成 VBScript。**这条必须写进激活信号。**

语法歧义本身不构成否决——但"没有语法"意味着我们无法进入任何有意义的文法讨论，正如编译器脚本那场。**一个无法进入文法讨论的建议，无法成为 Active。**

#### 2. 角案例 / 边界语义

即使只谈概念，角案例也足够把我们按回椅子上：

```vb
' 我们自造的 REPL 会话草图（非 VB 语法、非 Anthony 原文）——跨提交状态如何定义？
> Dim x = 40
> x + 2
42
> x = "现在是字符串了"        ' Option Strict Off 下允许重绑不同类型——状态类字段的类型漂移?
```

- **跨提交的 definite assignment**：C# scripting 里每个提交编译为一个 submission 类的成员，顶层变量变成该类的字段。VB 若照搬，`Dim x = 40` 在第 1 个提交、`x + 2` 在第 2 个提交——`x` 是什么作用域？字段还是会话状态？重绑定不同类型时字段类型怎么办？
- **错误与会话状态**：第 3 个提交抛异常，第 4 个提交继续——前 2 个提交的变量还在吗？C# scripting 的答案是"在，但可能处于半初始化"；VB 要给出同样的明确规则。
- **`Exit Sub` / `Return` 在脚本顶层**：顶层代码窄子集里 `Return` 语义已 `Probably` 定为"退出合成 `Main`"；REPL 提交里 `Return` 退出什么？会话？
- **`End` 语句**：`End` 在脚本里是终止会话还是终止进程？VBScript 里 `WScript.Quit` 是进程级——语义要钉死。
- **多脚本文件**：脚本能 `#r` 引用别的脚本吗？C# scripting 用 `#r "path"` 引程序集；VB 的引用面要单独设计（`#r` 文法对 VB 是全新的——又一个 `#` 开头的指令，与 `##If` 语义预处理共用前缀空间，见 #4）。
- **`Await` 顶层**：脚本顶层 `Await` 需要合成 `Async Main` 或异步提交——`Probably` 可做，未定。

#### 3. 作用域与绑定

**这是 REPL（B）真正的设计难点，也是它与顶层代码窄子集（C）分道扬镳的地方。** C 里顶层 `Let` 绑定到合成 `Main` 的**局部变量**（`meeting-top-level-code.md` 已定）；B 的跨提交变量在 C# 模型里是 submission 类的**字段**——两种绑定目标不同。语义模型要回答"第 2 个提交里的 `x` 是什么符号"：是状态类字段，还是对第 1 个提交局部变量的引用？**如果 VB 的 REPL 复用顶层代码的"每文件一个入口"模型，跨提交状态根本无处安放——这是 B 必须引入 submission 状态类、而非复用 `Main` 局部的结构性理由。** 反过来，若只做 C（无 REPL），这个问题不存在。**这个发现让我们更坚定：C 先行、B 是独立增量，两者不是一回事。**

#### 4. 与既有特性的交互

- **松绑定 / Option Strict Off（最关键）**：VBScript 的看家本领是晚期绑定。好消息是 **VB 编译器今天就有这套机器**——不需要解释器，`Option Strict Off` 下 `CreateObject` + 动态成员调用本就是合法 VB：

```vb
' 今天就能编译（Option Strict Off 下）——VBScript 的晚期绑定血统已经在编译器里了。
Option Strict Off

Dim fso = CreateObject("Scripting.FileSystemObject")
Dim count = fso.Drives.Count
```

  这条代码**今天**在 `Option Strict Off` 的模块里就编译通过。它说明一个关键事实：**VBScript 迁移最依赖的松绑定机器没有被 2001 年丢掉，它就在编译器里，只是需要一个宿主去接住 "把文件当脚本跑" 这个形态。** 这是本建议最硬的一张牌，也是我们不把 "脚本化" 打成纯外来特性的依据。
- **`Set` 语句**：VBScript 用 `Set obj = CreateObject(...)`，VB.NET 已移除语句级 `Set`（`meeting-set-statement.md` 正在讨论要不要请回来）。**脚本宿主若要兼容 VBScript 源码，`Set` 是否在脚本文件里复活是一个必须单独决策的问题**——不能由本建议顺手决定。
- **`On Error` / `Resume`**：VBScript 遗留的 `On Error Resume Next` 风格与 `proposal-retry-resume.md`（18.6）直接交互；脚本文件里是否允许非结构化错误处理，需要独立裁定。
- **`##If` 语义预处理**：`#r`（若采用）与 `##If` 共享 `#` 指令前缀空间，文法需要防冲突。`##If` 本身就是"声明式、有界、确定性"的编译期机制，与脚本的运行时诉求正交，不冲突，但前缀空间要划界。
- **`Handles` / WinForms**：顶层 `Handles` 已被 `meeting-top-level-code.md` 判为 Table（缺 `WithEvents` 字段与设计器映射）；脚本场景（WSH 无 UI 传统）更不需要它。不引入。

#### 5. Breaking change 与兼容性

**无直接破坏**——没有新语法，就没有旧代码重解释。C 形态把"今天编译报错的裸语句文件"变成"脚本"，是错误→程序，不是破坏，与顶层代码窄子集的判断一致。但我们把隐患标清楚：

- **语义分叉是最大的潜在破坏**，不是对现有代码，而是对**语言一致性**：如果脚本宿主悄悄改变了任何语义（哪怕一处），2017-12-06 的警告就成真——"a slightly different version of the language with slightly different semantics"。**铁律：脚本走同一编译器、同一绑定器、同一 Option 语义，一个字节都不许分叉。** 这不是可选质量目标，是本建议的成立条件。
- **宿主命令的占位**：`vbscript foo.vb` 若与未来某个同名工具冲突，是工具命名问题，不是语言破坏。

#### 6. Option Strict / 编译选项分叉

**这是我们愿意给本建议立规矩的地方，也是它区别于"第二门语言"的分水岭。** 一个危险的倾向是把脚本化做成"自动 `Option Strict Off` 的宽松模式"——那正是 #102 所警告的语义分叉，也是"第二种做事方式"（2018-06-13："our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"）的最坏形态。

我们的立场：**脚本文件声明自己的 `Option`，逐字照现行规则执行**。VBScript 迁移文件大可以写 `Option Strict Off`（松绑定就在那里等着），但那是**文件级选择**，不是"脚本方言"的默认属性。REPL 会话有一个**会话级** Option 设置，同样默认 `Strict On`、显式放宽。这样"标准方言与脚本方言"就不再是两种语义，而是**同一门语言的两种运行形态，Option 仍是那个 Option**。这是本建议能站住的前提，写进 RESOLUTION。

#### 7. IDE / IntelliSense

**这是把 B 从"可能"推向"慎重"的追问。** REPL 不是终端玩具，是开发体验：补全、签名帮助、变量检查、断点、调用栈。VB6 的 Immediate Window 曾是 VB 开发者的肌肉记忆（`? 2+2`、`Debug.Print`），我们要承认这是一笔真实的"光"资产——但也正因为它真实，做砸了伤害更大。脚本文件（C）的断点映射是标准 Roslyn 工作（`meeting-top-level-code.md` 已提）；REPL（B）的断点/局部变量展示、以及"提交即编译"下诊断如何渐进呈现（第 N 行报错时前 N-1 行已执行），需要一整块 IDE 设计。**没有 IDE 设计的 REPL 规格等于没设计。**

#### 8. 数据 / 普遍性

- **迁移人群**：VBScript/WSH/经典 ASP 是真实存在的代码量，这是最硬的普遍性证据——但它不是"用户请求数据"，是"存量代码数据"，且本建议没有量化。
- **主线信号**：#102 的"调和脚本方言"是主线自己点的题，但 2018-05-30 的"quiet customers"提示我们：一般 VB 受众不在要这个。两股信号方向相反。
- **C# 先例的采用率**：2018-02-07 对可空引用类型说过 "We'll postpone this until we understand the uptake in C#."——C# Scripting API 存在多年，采用率数据我们手里没有，`Suspect` 整体偏低（教程/实验多、生产少）。但产品定位改变天平：**对 VBScript.NET，脚本是核心身份，不是边缘特性。** 数据缺口真实，产品使命本身就是数据——这与 `meeting-top-level-code.md` 的结论同构。

#### 9. 更简替代

- **try.dot.net / C# Scripting API**：已经是"脚本方言"的现有实现，但教的是"略有不同的语言"——替代了我们想要的体验，也替我们踩了坑。问题是它不可被 VB 复用为语言能力，只是相邻工具。
- **dotnet-script / PowerShell**：脚本场景的外部替代，同样不服务于"VB 自己可以被脚本化"。
- **最重要的一条**：**"脚本"也许根本不是语言特性，而是宿主工具。** C 形态的语言工作量几乎全部已被顶层代码窄子集承担（合成入口、文件作用域语句）；宿主命令（`vbscript foo.vb`）是工具，不是语法。**把本建议重新表述为"顶层代码 + 一个宿主 + 一个 REPL 工具"，语言侧的工作量立即塌缩。** 这是我们在"更简替代"上最大的发现。

#### 10. 复杂度 / 成本 / 优先级

- **C（脚本宿主）**：复用顶层代码窄子集 + 宿主命令 + 调试映射。成本中低，无新运行时、无新语法。**价值/成本比最好。**
- **B（REPL + submission）**：Roslyn submission 编译、状态类、`#r`、IDE 集成。成本高，是 C 之上的独立工程。
- **A（真解释器）**：重造运行时，成本失控，且与 VB.NET 存在理由相抵触——**不启动**。
- **优先级**：低于模式匹配（2018-12-19："it's the thing we are most excited about"）与可空性流分析；但在第 18 章实验清单里，它是对**这个产品**最高优先级的一项。成本的排序是 C < B << A，价值的排序对这个产品是 C ≈ B >> A。

#### 11. 运行时 / CLR 硬约束

无直接 CLR 禁令，但有质量悬崖。真解释器（A）不产 IL，等于把脚本放在 CLR 之外——失去互操作、调试、性能、工具链，且需要一个自研的绑定/晚期绑定引擎（`Probably` 会以反射或表达式树实现，性能与语义都打折扣）。Scripting API（B）一切照旧：提交编译出 IL、正常进 CLR，PEVerify 无碍，晚期绑定走既有 `IDispatch`/动态路径。**"解释执行"的用户观感可以通过在内存中编译实现，不需要解释器——这是把 A 判死、把 B 扶正的技术根据。**

#### 12. 值不值得做

逐维打分。**价值**：对产品高（核心身份）、对一般 VB 受众低（quiet customers）；**成本**：C 中低、B 高、A 失控；**风险**：语义分叉（有"同一编译器"铁律对冲）、与顶层代码建议的重叠（有边界划定）、`.vbs` 兼容陷阱（有扩展名规则回避）。**结论：方向值得做——但"值得做"不等于"现在做"，更不等于"按字面做"。** 字面读法（真解释器）我们明确不启动；文件脚本 + REPL 的正确路线是 B/C，而那需要一份真正写出来的设计。今天这份建议没有设计，所以我们的落点是"方向 Consider、文档保持 inactive、给出激活信号"，与编译器脚本那场（Table）的差别在于：**那边是"不该做"，这边是"该做但还没写出来"。**

### VB 基因对照

- **继承 VB6/VBScript 遗产（光 / 水）**：这是本建议最强的一笔。脚本、REPL（Immediate Window）、松绑定都是 VB 自己丢下的 DNA，不是从 C# 借来的——"脚本化"对 VB 是**回归**而非**引进**。与 `proposal-set-statement.md`（复活 `Set`）、`proposal-retry-resume.md`（复活 `Resume` 思路）同属"VB6 遗产清理"谱系。
- **保持 VB-like / 读起来像英语（原则 #2、#5）**：脚本文件 = "打进去就跑"，正是 QBasic/VB6 的直觉；`meeting-top-level-code.md` 已论证这是"极 VB"的形态。REPL 的 `? 2+2` 比任何现代 REPL 都更像母语。
- **不引入"第二种做事方式"（原则 #3）**：**重罚项，但可辩护。** 2018-06-13 的高门槛针对"已有方案的替代"；而"标准方言 / 脚本方言"的缺口是主线自己点题、自己推迟、从未填补的（#102）。脚本不是抢占已有方案的地盘，是补上主线欠账——前提是**它必须是同一门语言**（Option 铁律），而不是第二门。这一辩护并不轻松，但房间多数认为成立。
- **默认跟随 C#（原则 #4）**：这里我们**跟 C#**——Scripting API 是已验证机制（2018-06-13 "C# will take the lead"），用它的形态而非自研解释器。这是少见的"主线依赖 C# 直接给出答案"的例子，我们照单全收，不加戏。
- **不做隐性语义变化（原则 #7）**：语义分叉是本建议最大的危险，靠"同一编译器 + Option 铁律"对冲。没有这条，它就是又一个隐蔽分叉。
- **不为边缘场景加特性（原则 #6）**：对一般 VB 是边缘，对 VBScript.NET 是核心——依产品定位而定，与顶层代码的判定一致。
- **消除常见样板（原则 #9）**：脚本是"消除样板"的终极形态——连工程、`Main`、编译产物都省了。
- **与主线关系（对照表 2.3）**：顶层语句行主线是"推迟考虑"，Anthony 是 "Immersive Files"（一致 / 更激进）；本建议是那条线的**运行侧延伸**——主线从未对"脚本方言"给出正面方向（#102 推迟后无下文），Anthony "Of course." 是独立延伸但方向与主线欠账同向。C# Scripting API 是主线可引用的机制先例。

### RESOLUTION:

1. **方向：Consider。** "VB 应当可脚本化 / 可解释执行"得到认可——`Of course.` 作为方向信号被记录为 **LDM Considering**。它的分量来自三处：① Anthony 二十年经验的肯定；② 主线 #102 自己点的"调和脚本方言"欠账；③ 产品身份（VBScript.NET）使脚本成为核心而非边缘。
2. **真·解释器（PROPOSAL A）：Reject（方向性）。** 重造运行时、把脚本放在 CLR 之外、失去互操作/调试/性能，且与 2018-06-13 "C# will take the lead" 相悖。**"解释执行"的用户观感必须通过"同一编译器 + 内存中编译"实现，而不是新引擎。**
3. **路线：PROPOSAL B/C，C 先行。** ① C（脚本宿主 `vbscript foo.vb` + 顶层代码窄子集合成入口）先落地，语言侧工作量基本已被 `meeting-top-level-code.md` 窄子集承担，宿主是工具不是语法；② B（REPL / submission）是独立增量，引入 submission 状态类，**不复用** `Main` 局部模型——两者分开排期、共用同一编译器。
4. **Option 铁律（分水岭）：脚本文件声明自己的 `Option`，逐字照现行规则执行；REPL 会话有会话级 Option，默认 `Strict On`。** 脚本化绝不隐含"自动放宽 Option Strict"。这是"一门语言、两种运行形态"而非"两门语言"的成立条件，也是 #102 "reconcile the standard and scripting dialects" 的字面兑现。
5. **激活所需信号（文档从 inactive 升为 Active 的条件，任一被满足即重审）**：
   - **(a) 一份重写的建议**：从 "Of course." 扩写为 B/C 形态的实际设计——宿主命令规则、脚本文件判定、入口合成优先级、Option 铁律、`#r`（若用）文法与 `##If` 前缀划界、错误/会话状态语义；每一条按六章节模板展开，含可编译示例。
   - **(b) 一个最小原型**：`vbscript` 宿主 + 顶层代码窄子集跑通一个真实 VBScript 迁移脚本（含 `Option Strict Off` 松绑定路径），证明"同一编译器、零语义分叉"可成立；REPL 提交状态类原型作为第二阶段。
   - **(c) 迁移数据**：VBScript/WSH/经典 ASP 存量与迁移诉求的量化证据——目前只有定性直觉。
   - **(d) C# Scripting API 采用率数据**：按 2018-02-07 可空性先例——"We'll postpone this until we understand the uptake in C#"——跟踪 C# 侧信号作为外部气压计。
   - **(e) 与相邻建议的对表**：`.vbs` 扩展名规则与 `Set`/`On Error` 复活问题须与 `meeting-set-statement.md`、`proposal-retry-resume.md` 交叉确认，避免脚本宿主顺手改变语言面。

### Implication:

- 在 `proposal-scripting-interpreted.md` 头部记录本次会议判定（LDM Considering / 保持 inactive）与五条激活信号，并关联 #102（2017-12-06 会议）、`meeting-top-level-code.md`。
- 与顶层代码窄子集团队对表：脚本宿主作为窄子集落地后的第一个消费者；确认"每编译至多一个脚本含可执行语句"与宿主命令的边界。
- 建立"VB 运行形态地图"（对标编译器脚本那场的"编译期可扩展性地图"）：收录编译产物、脚本宿主、REPL/submission、try.dot.net、C# Scripting API——任何"VB 怎么被运行"的新提案先读此地图。
- 跟踪两条气压计：C# Scripting API 采用率（2018-02-07 先例）；VBScript 迁移生态的活跃度。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：Anthony "Of course." 到底指 REPL、脚本宿主、还是真解释器？原文无任何线索。`Suspect`：指"VB 应当可以被脚本化/解释运行"的宽泛认同，纯属推测。
- `OPEN QUESTIONS`：REPL 跨提交状态的绑定模型——submission 状态类 vs 其它；变量重绑定不同类型时的字段类型漂移规则。
- `OPEN QUESTIONS`：脚本文件的错误处理与 `On Error Resume Next` / `Set` 复活是否随宿主一起，还是独立决策。
- `OPEN QUESTIONS`：`.vbs` 扩展名是否进入宿主范围，还是留给迁移工具（我们倾向后者）。
- `TODO`：量化 VBScript/WSH/经典 ASP 存量，为激活信号 (c) 补证据。
- `TODO`：确认 C# Scripting API 的采用率与设计时工具成熟度，作为信号 (d)。
- `Follow-up`：与 `meeting-set-statement.md`、`proposal-retry-resume.md` 对表，确定脚本宿主不隐含改变语言面。

### 状态

- **LDM 状态：Considering（方向）/ 保持 inactive（文档）**；PROPOSAL A（真解释器）方向 Reject；路线为 B/C（C 先行）。
- **三态判定：Consider** — 方向真实、血统纯正（VB6/VBScript 遗产）、产品核心，但文档是占位符（两个字），无设计可规格化。激活须满足五条信号中的一条，尤其是一份重写的实际设计（信号 a）或最小原型（信号 b）。**与编译器脚本那场（Table，不该做）的区别在此：这一场是该做但还没写出来。**

---

## 附录：特性评价

# 建议评价报告：proposal-scripting-interpreted.md

## 评价对象

- 建议：proposal-scripting-interpreted.md — 脚本与解释执行（VB 可以被脚本化 / 解释运行）
- 来源：Anthony 原文第 18 章 18.24 "Scripting and Interpreted Code"（`..\..\AnthonyDesign_wordpress.txt` L3278–3282；全文仅一句："Of course."）
- 配方目标：无——原文未给出任何动机、语法或设计；建议如实记录了"一句话肯定"

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 1/5 | 锚点 1："目标定义不清"（无目标可测）。无动机、无语法、无示例、无原型；"脚本与解释执行"指什么（REPL/脚本宿主/真解释器）未定。唯一内容是 "Of course."——方向信号，不是效果声明 | 未提供 | 效果完全未显现；三个读法未定 = 核心语法未定型，效果证据封顶为未提供 |
| 特性 | 2/5 | 锚点 2/3 之间：**方向**继承 VB6/VBScript 基因（脚本、REPL、松绑定、低仪式）——这在本建议是真实遗产而非外来词；但文档本身**零特性可交付**，且最危险的读法（真解释器）是反 VB 的外来形态 | 已检查 | 无语法即无"继承"；方向正确但载体为空；若取字面读法（A）则与 VB.NET 存在理由相悖 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、如实声明原文仅一句、Drawbacks/Alternatives/Unresolved 实质性非空且诚实（3 个未决问题具体诚实，1–3 健康区间）；但 Detailed design 为空、无任何示例、状态行全部占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）、未与 #102 / try.dot.net / 2018-06-13 对话 | 已提供/已检查 | 诚实不等于有内容；"空"是本质；未引用主线最相关的 #102 记录与 C# Scripting API 先例 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。对 VBScript.NET（火=脚本新阶段、水=差异化、光=盘活 VBScript/VB6 遗产）明显正向；但暗=语义分叉风险（若无"同一编译器"铁律）、与顶层代码/`Set`/`Resume` 重叠、`.vbs` 兼容陷阱，文档零对冲 | 已检查（预测待定） | 未识别语义分叉风险；未划定与顶层代码窄子集的边界；未提 `.vbs` 陷阱；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源标注准确（Anthony 18.24 "Of course." 逐字引用，无 C# 借鉴、继承 VB6/VBScript 遗产未点明）；**但未标注**最相关的两件材料——主线 #102（2017-12-06 try.dot.net 脚本方言调和）与 C# Scripting API（现成机制先例），使"影响预估"缺了关键参照 | 已检查 | 成分本身只有一句原文；缺主线欠账标注与 C# 机制先例标注；"继承 VB6 遗产"未显式声明 |

## 设计原则对照

- **与 VB 基因：方向一致、载体为空。** 脚本化继承 VB6/VBScript 原始 DNA（REPL/Immediate Window、松绑定、低仪式），符合原则 #2、#5、#9；但文档本身不交付任何基因。风险在原则 #3（第二种做事方式）与 #7（隐蔽语义分叉），分别靠"补主线欠账而非抢占方案"与"同一编译器 + Option 铁律"对冲——两者都是会议论证，不是文档内容。
- **与主线关系：主线一致 / 更激进（运行侧延伸）。** 主线 #102（2017-12-06）点了"调和标准方言与脚本方言"的题并推迟，从未兑现；Anthony "Of course." 是对该欠账的独立肯定回应，方向同向。机制先例（C# Scripting API）来自主线默认跟随 C# 的原则。与 `meeting-top-level-code.md`（源形态）互补，与 `meeting-generative-compiler-scripting.md`（编译器内脚本）已明确分离。
- **破坏性变更：无**（无语法、无实现，无从破坏）；但潜在风险是**语义分叉**（脚本悄悄变语义 = 对语言一致性的破坏），须"同一编译器 + Option 铁律"作为成立条件。

## 总评

- **达成程度：未达成（作为可落地特性）/ 达成（作为方向信号）。** "Of course." 是一句有价值的肯定——它确认了 VB 应当可脚本化，且与主线欠账、产品身份、VB 血统三方共振；但作为语言特性，效果、特性、品质三维全部不达标，设计本体为空。
- **LDM 三态建议：Consider（方向）；文档保持 inactive（LDM No Plans，直至重写）。** 真解释器读法 Reject；路线为 Scripting API 形态（B）+ 脚本宿主（C），C 先行。面向 VBScript.NET 的优先级为 **Consider 偏重**——脚本是产品核心身份，但必须先有一份实际设计或原型，否则不离开 inactive。
- **主要问题**：① 无动机/无设计/无示例，Anthony 只有两个字；② "脚本/解释"三个读法未定，字面读法（真解释器）反 VB；③ 未引用主线 #102 欠账与 C# Scripting API 先例，缺最关键参照；④ 语义分叉风险无对冲设计（须 Option 铁律）；⑤ 与顶层代码窄子集、`Set`/`Resume` 建议的边界未划定。

## 返工建议

- **补充章节**：按 B/C 形态重写 Detailed design——宿主命令规则（`vbscript foo.vb`）、脚本文件判定与入口合成优先级、Option 铁律（脚本声明自己的 Option、REPL 会话级 Option 默认 Strict On）、跨提交状态模型（submission 状态类 vs 复用 `Main`）、错误/会话状态语义、`#r`（若用）文法与 `##If` 前缀划界；每个角案例配可编译示例。
- **补充证据**：引用主线 #102（2017-12-06 逐字）与 2018-06-13（"second way to do things" 高门槛、"C# will take the lead"）作为对话对象；最小原型（脚本宿主 + 顶层代码窄子集 + `Option Strict Off` 松绑定路径）；VBScript 迁移存量数据；C# Scripting API 采用率。
- **未决问题处理**：三个读法（REPL/宿主/解释器）须在文档中选定并说明为何（A 拒、B/C 取）；`.vbs` 扩展名规则（倾向留给迁移工具）；`Set`/`On Error` 复活问题移交 `meeting-set-statement.md` / `proposal-retry-resume.md`。
- **设计探索**：与 `meeting-top-level-code.md` 窄子集联合写一份"单文件即程序"端到端示例（同一文件：`vbscript` 跑 = 脚本，`vbc` 编译 = 程序），作为"一门语言、两种运行形态"的验收演示。

---

## 附录：C# 生态与互操作考量

> 本附录依据 `..\..\..\csharplang-index.md`（dotnet/csharplang 官方仓库镜像 `..\..\..\csharplang`，main 分支，含 2013–2026 LDM notes / proposals / spec）编写。所有 C# 原文均逐字核对并标注来源文件；无法核实或纯属推断处标 **Suspect** / **OPEN QUESTIONS**。本提案的 C# 关系并非"弱相关"——它恰好坐在 C# 生态两个最强趋势的交叉点上：一边是 C# 把"脚本式入口"收编进编译语言的行动（top-level statements、simple C# programs），一边是 AOT/trimming 对整个生态"压缩运行时动态"的压力。

### 一、相关 C# 现实方向

**D1. C# 已用"顶层语句"回答了"脚本方言调和"，答案是编译期入口，不是脚本运行时。** top-level statements（C# 9，`proposals\csharp-9.0\top-level-statements.md`）把"一个文件、一串语句、直接跑"做成语言本体特性——编译期合成 `Main`、文件即程序。逐字：

> "Allow a sequence of *statements* to occur right before the *namespace_member_declaration*s of a *compilation_unit* (i.e. source file)."

> "The primary goal of the feature therefore is to allow C# programs without unnecessary boilerplate around them, for the sake of learners and the clarity of code."

→ `proposals\csharp-9.0\top-level-statements.md`（Summary / Motivation）。这与本提案候选 C（脚本宿主 + 顶层代码窄子集）几乎是同一形态，只是 C# 已把它做成**编译语言的一部分**。

**D2. C# 正在探索 "simple C# programs"——把 `#r`/`#load` 等脚本式指令放进工具/项目层，而非语言本体。** LDM-2021-05-12 逐字承认"文件即脚本"是来自脚本语言用户的真实期待，REPL 只是次要形态：

> "These users instead expect to be able to simple make a `.cs` file and run it, with potentially more ceremony as they start adding more complex dependencies or other scenarios. Other expectations exist (such as repls), but our studies have shown that this is the most popular."

关于单文件内控制 dll/exe、single-file、trimming 等输出设置，LDM 明确尚无答案（这正是"脚本形态 vs 部署形态"界线悬而未决的官方证据）：

> "What about output settings such as dll vs exe, or single-file and trimming settings? We don't have answers for these today"

→ `meetings\2021\LDM-2021-05-12.md`（"Simple C# programs"，含结论 "Overall, we're extremely excited to take this challenge on"）。

**D3. C# Scripting API / csi.exe / try.dot.net 不是语言特性，而是 Roslyn 工具。** 经对 `..\..\..\csharplang` 全文 Grep（关键词 `scripting` / `csx` / `dotnet-script` / `REPL` / `try.dot.net`，2026-08 镜像）**零命中**——语言设计库从未讨论过脚本运行时。C# 的"解释执行"用户观感由 Roslyn Scripting（`Microsoft.CodeAnalysis.CSharp.Scripting`，内存中逐段编译、submission 模型）在**编译器即服务**一侧提供，语言本体不做解释器。`Suspect`：零命中是 Grep 实证，但"零命中 = 刻意把脚本留在语言之外"是推断；可确定的是 csharplang 无任何"把解释器做进 C# 语言"的讨论记录。

**D4. AOT / trimming / source-gen 是生态主线，反射型运行时动态被系统性压缩。** interceptors 的动机原文逐字，把"反射型场景难以 AOT 兼容"说得最直白：

> "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible."

→ `meetings\2023\LDM-2023-07-24.md`（Interceptors）。LDM 的解法倾向是"把信息放回类型系统"（该段后续："see if we can put that information back"），即用类型系统、source generator、interceptor 取代运行时反射——这正是本提案"解释执行"路线的反向力。

**D5. dynamic / 晚期绑定相对边缘化。** unsafe-evolution 的 open question 甚至问 dynamic 是否应标 unsafe：

> "`dynamic` (probably should match what BCL decides for reflection APIs)"

→ `proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?" open question）。动态/晚期绑定在 AOT 时代被 C# 侧视为负担而非资产。

**D6. unsafe-evolution 对 VB 的明确表态：VB 无指针、无 unsafe 上下文，无需 requires-unsafe 支持——但 VB 编译器必须认识 C# 侧的新元数据。** 逐字：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."

→ `proposals\unsafe-evolution.md`（VB 小节）。含义见下（决策文件 M8：RequiresUnsafeAttribute / MemorySafetyRulesAttribute 的识别是**必须桥接**点）。

### 二、现实 vs 提案：逐条对应

| 本提案项 | C# 现实对应（上节） | 判定 | 理由 |
|---|---|---|---|
| PROPOSAL A 真·解释器（RESOLUTION 2 已 Reject） | D3（无解释器先例）、D4（AOT 压缩反射型动态） | **冲突** | C# 生态从未把解释器做进语言；A 不产 IL、运行时动态，与 D4 主线正面相撞。C# 现实**强化**既有 Reject，而非削弱 |
| PROPOSAL B Roslyn Scripting API for VB（RESOLUTION 3） | D3（Scripting API 是 C# 唯一"解释执行"实现） | **兼容 / 需桥接** | 机制先例成立、与 C# 同构；但 VB 版 Scripting API 不存在（C# 有、VB 没有，须自建）；submission/`#r` 依赖运行时程序集加载，与 D4 的 trimming 有张力——作为"开发时/交互形态"可共存，作为"部署形态"冲突 |
| PROPOSAL C 脚本宿主 + 顶层窄子集（RESOLUTION 3，C 先行） | D1（top-level statements）、D2（simple C# programs） | **高度兼容** | C 与 C# 顶层语句几乎一一对应（合成入口、文件即程序），C# 已证明"编译期入口"路线可行且被采用；宿主命令是工具不是语法，与 D3"脚本留在工具侧"的取向一致 |
| PROPOSAL D 什么都不做（未采纳） | D1/D2（C# 已在主动做脚本入口） | **脱节** | C# 的信号早已存在（C# 9 顶层语句、2021 simple C# programs），"等 C# 信号"等于无视已有信号；对 VBScript.NET（产品核心身份）尤其不成立 |
| Option 铁律（RESOLUTION 4，脚本声明自己的 Option、REPL 默认 Strict On） | D5（dynamic 边缘化）、D6（unsafe 收紧、默认安全） | **兼容 / 被强化** | C# 生态整体朝"默认安全、动态显式 opt-in"走，与 Option 铁律方向一致；C# 现实是本铁律的外部佐证 |
| 激活信号 (d) C# Scripting API 采用率 | D2（simple C# programs 探索中）、D3（采用率数据不在本库） | **有效 / 需补数据** | 信号方向正确；但采用率数据须去 Roslyn/工具链测，本库无正文 |

**总体判断：方向兼容，一处结构性冲突。** 本提案的 B/C 双轨与 C# 的"top-level statements（语言内）+ Scripting API（工具侧）"双轨同构，方向**兼容**；唯一的结构性摩擦是决策文件 M5 早已判定的——**"解释执行 / 运行时动态生成"与 AOT/trimming 天然冲突**（反射、DynamicMethod、运行时程序集加载都是 D4 要消灭的对象）。冲突的解法不是不做脚本，而是**默认编译、把 interpreted 做成显式 opt-in 的传统兼容层**。

### 三、对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态（双模路线）**：脚本文件默认 `Option Strict On`，动态能力（`Option Strict Off`、`Any` 晚期绑定）限定在脚本/COM/Office 层显式启用；编译产物走类型化出口。这与 RESOLUTION 4 的 Option 铁律同向，且 D5/D6 给出 C# 侧理由：C# 生态正在收紧动态与不安全，VB 若反向把"脚本=宽松模式"当默认，将与生态脱节。
2. **source-gen / 编译桥**：C 形态的产物是**受管程序集**（复用顶层窄子集 + 合成 `Main`，正是 D1 的 C# 路线）；B 形态的 REPL 用 submission 编译出 IL、正常进 CLR（本文件 §11 已论证 PEVerify 无碍）。"解释执行"的用户观感用"内存中编译"实现，**不需要解释器**——这是本文件已定的路线，C# 现实（D3）确认这是生态唯一被验证的路线。
3. **interpreted 模式只作显式 opt-in 的传统兼容层**：真·解释语义（若为 VBScript 迁移保留）不进 AOT/trimming 部署图；脚本=开发时/交互形态，部署形态走编译。若 VBScript.NET 愿景含 NativeAOT，脚本层必须在 AOT 图之外（运行时动态加载与 D4 不兼容）。
4. **识别新元数据（必须桥接）**：unsafe-evolution 后 C# 成员可标 requires-unsafe（RequiresUnsafeAttribute / MemorySafetyRulesAttribute），且指针会在更多"非 unsafe 上下文"出现（D6 + 决策文件 M8）。VB/.vbx 编译器必须识别这些元数据，才能正确校验"脚本/程序调用 C# requires-unsafe 成员"的安全性——这同时影响脚本宿主的跨语言互操作面。
5. **跟踪气压计**：① C# simple C# programs（`#r`/`#load`）是否最终进入语言/工具链——若落地，是 C 形态宿主的直接对标（激活信号 (d) 的补强）；② C# Scripting API 采用率与设计时工具成熟度（数据在 Roslyn/工具链，本库无正文）。

### 四、对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION 2（A Reject）：C# 现实强化，不改写。** D3（C# 无解释器先例）+ D4（AOT 压缩运行时动态）为"方向性 Reject"补上生态侧证据。
- **RESOLUTION 3（B/C、C 先行）：与 C# 顶层语句先例一致。** C 与 D1 几乎同构，C 先行与 C#"编译期入口"取向一致；B 与 D3 同构。**无需修改。**
- **RESOLUTION 4（Option 铁律）：被 C# 现实强化。** D5/D6 证明生态在"默认安全、动态显式化"，与铁律方向一致。
- **三态判定（Consider / 保持 inactive）：不变。** C# 生态证据是本方向的外部佐证，不构成任何一条激活信号本身；激活仍须五条信号（尤其重写设计 a / 最小原型 b）。
- **可补充一条外部信号（与 (d) 同类）**：C# simple C# programs（`#r`/`#load`）的落地状态作为脚本宿主的直接对标——建议并入信号 (d) 跟踪。

### 五、引用纪律与 OPEN QUESTIONS

**已逐字核实的 C# 原文（本附录使用）**：
- "Allow a sequence of *statements* to occur right before the *namespace_member_declaration*s of a *compilation_unit* (i.e. source file)." → `proposals\csharp-9.0\top-level-statements.md`（Summary）
- "The primary goal of the feature therefore is to allow C# programs without unnecessary boilerplate around them, for the sake of learners and the clarity of code." → `proposals\csharp-9.0\top-level-statements.md`（Motivation）
- "These users instead expect to be able to simple make a `.cs` file and run it, with potentially more ceremony as they start adding more complex dependencies or other scenarios. Other expectations exist (such as repls), but our studies have shown that this is the most popular." → `meetings\2021\LDM-2021-05-12.md`（"Simple C# programs"）
- "What about output settings such as dll vs exe, or single-file and trimming settings? We don't have answers for these today" → `meetings\2021\LDM-2021-05-12.md`（"Simple C# programs"）
- "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." → `meetings\2023\LDM-2023-07-24.md`（Interceptors）
- "`dynamic` (probably should match what BCL decides for reflection APIs)" → `proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"）
- "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（VB 小节）

**OPEN QUESTIONS**：
- C# Scripting API 的采用率与设计时工具成熟度数据——csharplang 库零正文（Grep 实证），须去 Roslyn / dotnet/sdk 生态测，作为激活信号 (d)。
- simple C# programs（`#r`/`#load`）最终是否进入语言本体、与项目文件的"cliff of complexity"落在哪——LDM-2021-05-12 明言 "We don't have answers for these today"，后续（含 2026 会议）无专门跟进记录，状态未定。
- VBScript.NET 若含 NativeAOT 愿景，脚本层具体如何在部署时排除于 AOT 图之外——属 dotnet/runtime 侧约束，本库无正文。

**Suspect**：D3 的"csharplang 零脚本讨论 = 刻意把脚本留在语言之外"是推断（Grep 实证零命中，但意图是推断）。
