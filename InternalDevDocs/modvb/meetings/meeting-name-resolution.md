# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这一次我们面对的是名称解析——一个我们曾经碰过、但一直小心翼翼地没敢再往前推一步的领域。2014 年主线做过一轮 "Smarter name resolution"（2013-12-13 会议，2014-02-17 纪要 #19 确认已进 Main、VB-specific），那之后我们一直把它当"已解决"放在一边。Anthony 在第 16 章用一条 `Fix:` 注释把这个旧伤口重新掀开了，所以我们今天把它正式摆上桌。

## Agenda

* [Proposal: 名称解析优化（`Console` 命名空间遮蔽修复）](#proposal-名称解析优化console-命名空间遮蔽修复)

## Proposal: 名称解析优化（`Console` 命名空间遮蔽修复）

_Related: 主线 2013-12-13 / [2014-02-17 #19 – Smarter name resolution](https://github.com/dotnet/vblang/blob/master/meetings/2014/LDM-2014-02-17.md)（已进 Main、VB-specific）；[2014-10-08 – 不相关接口歧义（spec 4.3.2）](https://github.com/dotnet/vblang/blob/master/meetings/2014/LDM-2014-10-08.md)；ModVB：`proposal-method-level-imports.md`（方法级 `Imports`）_

### 场景与缺口

We started from a scenario that我们并不陌生——2014 年 2 月的纪要里就摆着一整屏的 `Console` / `Threading` / `Xml` / `Diagnostics` 前缀歧义例子。今天的版本长这样：

```vb
' Anthony 第 16 章原文（Performance and Interoperability）：
' Fix: Smarter name resolution to avoid `Console` namespace
' hiding `System.Console` class, necessitating an explicit `Imports System`
Imports Microsoft.Extensions.Logging

' error BC30456: 'WriteLine' is not a member of 'Microsoft.Extensions.Logging.Console'
Console.WriteLine("Hello World!")
'       ~~~~~~~~~
```

The gap：`Imports Microsoft.Extensions.Logging` 之后，简名 `Console` 的候选集合里同时出现**命名空间** `Microsoft.Extensions.Logging.Console`（来自导入）与**类型** `System.Console`（来自项目级 `System` 导入）。现有规则让命名空间赢，于是 `Console.WriteLine` 在命名空间上找不到 `WriteLine`，报 BC30456。用户被迫补 `Imports System`、写全限定名 `System.Console`、或引入别名——每一招都只为消一次遮蔽，而现代 DI/logging 生态里 `Microsoft.Extensions.Logging.Console` 这类"与 `System` 里常见类同名"的命名空间越来越多，这个坑会越来越常见。

We see the appeal immediately：这不就是 2013 年那轮 "Smarter name resolution" 的同一个直觉吗？当时我们解决的是"前缀在多个命名空间之间歧义时，选唯一能延续到合法类型的那一个"（`N1.T` 选 `NA.N1.T`）。现在 Anthony 要的是它的表亲：**前缀解析唯一命中但走进死胡同时，回退到能继续延续的候选**。同一个"延续驱动"原则，只是触发点从"前缀歧义"挪到了"前缀死胡同"。这让我们没法一句话打发掉。

但我们也立刻闻到了它和主线口味冲突的地方：主线历来**宁可保留歧义**也不悄悄改绑定——2014 年我们对着 `Sin(1.0)` 的双 Module 歧义说过 "This case seems very minor. Not worth worrying about."，而 `GetName` 讨论里我们坚持 "Point (the prefix) must be unambiguous"。今天这个建议要求我们在这条线上松一扣。所以这次我们不打算当背书者。

### 候选方案

**PROPOSAL A — 成员访问回退（Anthony 的字面建议）。** 名称解析做成员访问（`.WriteLine`）时，若命中的是"仅命名空间"（命名空间上没有该成员可继续解析），则回退到其他同名候选中的类型（`System.Console` 类）。这是对"命名空间优先"规则的定向修补，无需新语法。

**PROPOSAL B — 延续驱动回退（窄版，把 2013 年原则推广到死胡同）。** 不引入"命名空间优先 → 失败 → 类型优先"的两段式，而是统一表述为：**当某个候选的延续在当前位置不成立（死胡同）时，若有且仅有一个其他候选能继续，则选择它**。B 只覆盖两类死胡同：(i) 类型位置（`Dim x As Console`——命名空间不能当类型用）；(ii) 成员访问位置命名空间上**根本没有**该成员名（`Console.WriteLine`——命名空间无 `WriteLine`）。重载解析失败**不算**死胡同（见深度追问 #2）。B 与 A 的区别：A 是"类型回退"，B 是"延续成功者胜出"，B 把规则收敛回 2013 年那个已经进 Main 的原则，spec 改动面更小、语义模型更可预测。

**PROPOSAL C — 不动编译器，只做 IDE 诊断/快速修复。** 当遮蔽引发 BC30456 时，错误列表给出可操作的建议（补 `Imports System`、改用 `Global.System.Console`、或建议 `Imports` 别名），并在编辑器里点亮电灯泡。零绑定变化、零 breaking change，完全符合 2014 年 #Disable Warning 讨论里我们立下的传统——"The tradition in VB and its rich history of quick-fixes is that you resolve warnings by FIXING YOUR CODE"。

**PROPOSAL D — 方法级 `Imports` 作为侧翼。** `proposal-method-level-imports.md` 允许把 `Imports System.Console` 收到方法体内，把冲突面从文件级缩小到方法级。2017-08-23 我们对方法级 `Imports` 的第一反应是 "Q: Do we really want `Imports`? Seems kinda crazy. Could be useful."——如果 D 落地，A/B 的紧迫性会明显下降（冲突可以在作用域层面绕开），但 D 不改变"同一作用域内命名空间遮蔽类型"的根因，只给用户更多逃生舱。

### 权衡：Q&A

- **A vs B：到底是"类型回退"还是"延续胜出"？** 表面等价，语义模型上差很多。A 的两段式意味着同一个 `Console` 标识符在不同上下文（`.WriteLine` 与 `Dim x As Console`）绑定到不同符号——语义模型、find-all-references、rename 都要为"上下文相关绑定"开特例。B 把规则收敛为"死胡同候选出局，延续成功者胜出"，这正是 2013 年已经进 Main 的规则（`N1.T` 选 `NA.N1.T`），只是把触发条件从"前缀歧义"泛化到"前缀死胡同"。**结论：v1 取 B 的地盘。** 但即便 B，"类型位置死胡同"（安全、干净、几乎无人反对）与"成员访问死胡同"（本建议真正想要的、也是风险所在）也必须分开评估——我们要的是后者，而后者恰恰是更危险的那个。
- **B vs C：编译器为什么不能只给提示？** C 零风险，但它是"脚手架不是语言特性"——用户每次都要手动消一次。我们的判断是：如果 A/B 只解决 `Console.WriteLine` 一个高频样例，C 的价值主张（不用改绑定规则）就压过 A/B；如果真实数据显示这种遮蔽是一个**成族**的痛（不只 `Console`，还有 `Diagnostics`、`Threading`、任何 `System` 类型被第三方命名空间遮蔽），编译器级修复才值回风险。**这取决于数据——我们没有数据。** 见深度追问 #8。
- **B 的生死线："启用型"约束。** 我们只允许回退**开启**"当前绑定必然报错"的访问；命名空间上能成功绑定的任何代码（哪怕绑定到的是个晦涩的扩展方法）都必须原样保留，回退不得介入。这条约束把 A/B 从"可能改绑定"压回"只让原本编译失败的代码变得可编译"——这是本建议唯一能站住脚的安全依据，也是我们反复回访的点。`Not all of us are happy with` 这意味着编译器要为同一表达式做"命名空间绑定 + 类型回退绑定"的双轨试探，且要逐字写进 spec。
- **D 与 A/B 的关系。** D 不解决根因，但 D 落地后 A/B 的优先级应该下调：能收窄作用域就不必赌解析器。建议 A/B 与 D 分开走，D 已有独立提案，这里不再展开。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

零新语法、零新关键字——这是本建议的先天优势。但**绑定歧义**被换成了另一种形态：同一个简名 `Console` 按上下文绑到命名空间或类型。解析器不用改，但语义模型必须回答"这个 `Console` 是什么"。2014 年我们在 `GetName` 讨论里对这类事的态度很明确——"Concern from Aleksey and IDE team about overloads and dealing with ambiguity"，当时的规则是 "Point (the prefix) must be unambiguous ... This is the current name lookup rules." 我们一度坚持前缀必须无歧义；A/B 恰恰要松开这条。这不是文法的歧义，是**符号模型的歧义**，成本比语法歧义高。

#### 2. 角案例与边界语义

**重载解析失败不是死胡同。** 这是必须写进 spec 的第一条边界：若 `Microsoft.Extensions.Logging.Console` 命名空间里**存在**名为 `WriteLine` 的成员（哪怕参数不匹配），调用 `Console.WriteLine("Hi")` 今天的报错是"重载解析失败"（BC30518 一族），**不是** BC30456。回退只在"命名空间上根本找不到该成员名"时触发；一旦触发条件放宽到"重载解析失败"，回退会变成重绑定的洪水（用户改一个实参类型就悄悄换到 `System.Console` 的方法上）。**规则：成员名缺失 ⇒ 死胡同 ⇒ 可回退；成员名存在但重载失败 ⇒ 保持报错。**

**命名空间上存在同名成员时绝不可回退。** `Microsoft.Extensions.Logging.Console` 里真有 `ConsoleLogger`、`ConsoleLoggerExtensions` 这些类型。`Console.ConsoleLogger` 今天合法，回退不得碰它——否则就是重劫持。这就是"启用型"约束的核心测试用例。

**类型位置死胡同。** `Dim c As Console`——命名空间不能当类型用，今天报"命名空间不能用作类型"。B(i) 在此处回退到 `System.Console`。这一半干净、安全，但——注意——**它根本不是本建议想要的场景**，用户很少写 `Dim c As Console` 被卡住。本建议真正要的是成员访问死胡同 B(ii)。说句不好听的：容易的那半没人要，要的那半是难的。

**多个类型候选。** `Imports Microsoft.Extensions.Logging` + 某第三方 `Foo.Console` 类也带 `WriteLine`——两个类型候选都能延续。**必须报歧义错误**，绝不能按导入顺序静默选一个。2013 年规则的前提就是"唯一能延续的候选"；候选多于一个时延续驱动原则自动退化为歧义报错。这既是 2013 规则的特性，也是本建议的天花板——它只解决"恰好一个类型能延续"的情形。

**参数/局部遮蔽。** `Sub Test(console As TextWriter)` 里 `Console.WriteLine` 绑到参数——本地符号优先级最高，回退永远轮不到。这条是既有规则，不用改，但要写进测试。

**`Imports` 别名优先级。** `Imports C = System.Console` 是显式选择，回退不得与别名竞争——别名胜出，这是"显式胜过隐式"的既有约定。

**泛型类型。** `Console(Of T)`——命名空间不能是泛型实例化，类型位置死胡同的又一个实例；延续驱动规则天然覆盖，无需特判。

**`NameOf(Console.WriteLine)` 交互。** 今天 `NameOf(Console.WriteLine)` 因 `Console` 绑到命名空间且无 `WriteLine` 而报错；若回退让它编译并返回 `"WriteLine"`，这是**净收益**还是隐患？`NameOf` 的绑定必须与普通成员访问一致，否则语义模型会出现"同文本不同符号"的又一处裂缝。我们倾向：`NameOf` 走同一条回退路径（一致性优先），但必须验证 IDE 的 rename/find-all-references 不会因此漏报。

#### 3. 作用域与绑定

语义模型里，`Console.WriteLine` 处的 `GetSymbolInfo` 应返回 `System.Console.WriteLine` 还是 `Microsoft.Extensions.Logging.Console` 命名空间加一个失败成员？B 方案下应返回回退后的 `System.Console.WriteLine`——但 IDE 补全（`Console.` 后面显示命名空间子成员还是 `System.Console` 的成员？）必须明确。We `Suspect` 这是本建议最大的隐性成本：**同一个标识符的符号身份从"编译期绑定"延伸到了"光标位置依赖"**，而 IDE 的所有引用计数、重命名、签名帮助都以"一个位置一个符号"为地基。2014 年 `GetName` 讨论里我们担心的正是这个。

#### 4. 与既有特性的交互

- **方法级 `Imports` / 块作用域 `Option`**（#117/#255）：若落地，用户在方法内 `Imports System.Console` 即可绕开遮蔽。回退规则与方法级导入的优先级关系需定义（方法级导入的命名空间遮蔽类型时，回退还触发吗？——不触发，方法与文件导入同等对待，本地导入仍是"命名空间优先"）。
- **`Global` 前缀**：`Global.System.Console.WriteLine` 是今天的硬逃生舱。回退不改变它的语义，但它的存在是"为什么还要回退"的反对票之一——逃生舱够多了。
- **`Option Strict` 分叉**：命名空间成员访问**永远**是编译期检查，宽松模式不会把命名空间当 `Object` 晚期绑定。所以回退在 Strict On/Off 下行为必须一致，且宽松模式下若存在同名变量，走晚期绑定、回退不介入。两条路径天然一致，这是本建议少有的干净角落。
- **模块提升成员**：VB `Module` 把成员提升进命名空间。若被遮蔽命名空间里某 `Module` 恰好暴露了 `WriteLine`（扩展方法等），今天该调用是**合法的**——"启用型"约束正好挡住这类重劫持，这是该约束的第二个测试用例。

#### 5. Breaking change 与兼容性

这是整场最尖锐的追问。严格讲，A/B 在"启用型"约束下**不改变任何今天能编译的代码的绑定**——回退只让今天报 BC30456 的调用变成可编译。这比 2014 年那次 spec 4.3.2 的改动（PROPOSAL3，合并查找再交给重载解析，"We think this is a minor breaking change"）还要温和。**但**有三处不破自破：

- **语义模型与诊断 API 变化**：基于 Roslyn 的分析器、生成器看到 `Console` 的符号从命名空间变类型（仅在新可编译的代码里）。这不算语言 breaking change，但对工具生态是行为变化。
- **"过去报错、现在通过"本身**：按主线"几乎不做破坏性变更"的立场这是可接受的（启用型），但按"避免隐蔽语义变化"（`Return?` 被拒的同类理由）它需要 `langversion`/`Option` 门控或至少警告策略。`Suspect`：VB 历史上没有 C# 式 `langversion` 门控的传统——大多数特性是整体出货的；要门控得先引入机制，成本另计。
- **错误信息时序变化**：用户依赖 BC30456 定位"我导入了错误的命名空间"的情况会变少（错误消失），这是特性本身的目标，但诊断质量的净变化需要实测。

#### 6. Option Strict / 编译选项分叉

如上（#4），命名空间成员访问不走晚期绑定，回退在 Strict On/Off 下同一路径。唯一要留意的是宽松模式下**对象**遮蔽（局部变量名 `console`）——既有规则，回退不碰。两路径对"已回退区域"的成员可用性保持一致。此项无分叉，是我们给这个建议唯一开的绿灯。

#### 7. IDE / IntelliSense

`Console.` 的补全列表是最大 UX 决策点：显示命名空间子成员（今天）、显示 `System.Console` 成员（回退后）、还是**两者都显示并标注**？我们倾向"两者都显示，回退目标做弱化标注"，但这对 find-all-references 是灾难——`Console` 在命名空间上下文和类型上下文里是两个符号，重命名 `Console` 会分裂。2014 年 `GetName` 讨论里 Aleksey 与 IDE 团队的担忧在这里原样复现。**结论：语义模型与 IDE 行为必须在原型里验证，不做进规范等于没设计。**

#### 8. 数据 / 普遍性

这是本建议最弱的一环。Anthony 只给了一个样例（`Microsoft.Extensions.Logging.Console`）。2014 年 2 月那份纪要里的 `ComponentModel` / `Threading` / `Xml` / `Diagnostics` 例子说明"命名空间前缀歧义"是真实的成族痛——但那些是**前缀在两个命名空间之间歧义**（且随项目类型变化），与本建议的"同名命名空间遮蔽同名类型"是两回事，不能当同一份数据用。我们需要的是：**现代 .NET（尤其 DI/logging 生态）里"第三方命名空间与 `System` 类型同名并遮蔽成员访问"的实测频率**。`Suspect`：直觉上它高频（logging 被导入后 `Console` 即废），但没有数字，主线 "We'll postpone this until we understand the uptake" 的传统在这里适用——先量化再决定。

#### 9. 更简替代

- **IDE 诊断 + 快速修复（C）**：提示补 `Imports System` / `Global.System.Console` / 别名。零风险，捕获大部分 DX 价值。我们的传统是"resolve warnings by FIXING YOUR CODE"——虽然 BC30456 是错误不是警告，但"给用户一条可操作的出路"与这个传统完全一致。
- **方法级 `Imports`（D）**：作用域层面绕开，已有独立提案。
- **别名 / 全限定 / `Global`**：现状逃生舱，确定但啰嗦。
- **错误信息增强**：把 BC30456 的文案改成"`WriteLine` is not a member of namespace `Microsoft.Extensions.Logging.Console`；did you mean type `System.Console`?"——不改绑定，只改诊断。这是 C 的一个廉价子集，我们很喜欢这个折中。
- 没有任何更简方案能让"原本编译失败的代码"变可编译——如果那个价值必须由语言兑现，就绕不开 A/B。问题只是：那个价值值不值语言级风险。

#### 10. 成本 / 优先级

2013 年的 "Smarter name resolution" 已经在 Main 里，延续驱动查找的基建存在；新增的是"命名空间死胡同 → 类型模式重查"的试探路径与语义模型双符号。实现成本中等，但 spec（4.3.2 及周边）、IDE、语义模型三处都要动。优先级：**低到中**——这不是头条特性，不解决任何"数十万安静客户"正在尖叫的痛点，且与 C#（没有同类特性）无对齐压力。我们不启动全量 A。

#### 11. 运行时 / CLR 硬约束

无。纯编译期行为；不触达 IL、PEVerify、表达式树。唯一"运行时影响"是原本编译失败的代码变得可编译并运行——那是特性本身的目的，不是约束问题。

#### 12. 值不值得做

价值（消除高频遮蔽样板、零新语法、延续 2013 年 VB-specific 原则）真实但**窄**——它只解决"恰好一个类型候选能延续"的死胡同。成本中等。风险集中在语义模型/IDE 的"一个位置一个符号"地基。**若只做 B(ii) 成员访问回退而拒绝收敛，我们会建议不做**；若接受 B 的"启用型"窄版 + C 的诊断增强作为配套，则值得一个原型。但今天这份建议原文——没有算法、没有触发条件定案、没有兼容性分析——**不足以支撑 Active**。

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：这是最亮的点，也是最刺的点。亮在零新语法；刺在"隐式回退"本身就是第三种消歧方式——在显式 `Imports System`、全限定、别名之外，又加了一条"编译器替你猜"。好在 2013 年主线已经开过这个口（"Smarter name resolution" 就是隐式选唯一延续者），所以本建议不是**新**引入"第二种方式"，而是把已有的"第二种方式"扩大了一格。
- **避免隐蔽语义变化（原则 #7）**：唯一扣分项。"启用型"约束（只开启原本报错的绑定）是对冲，但"同一标识符上下文相关绑定"是隐蔽性的新形态。没有"启用型"约束，这就是又一个 `Return?`。
- **永不破坏现有代码（原则 #1）**："启用型"下不改变任何可编译代码的绑定，勉强过关；但语义模型/诊断 API 的工具生态变化需要单独立项交代。
- **读起来像英语、对新手友好（原则 #5）**：`Console.WriteLine("Hello World!")` 无需解释——新手视角这特性"本该如此"，这是它的最强直觉引力。
- **不为边缘场景加特性（原则 #6）**：反方证据。一个样例、无数据、无普遍性论证——按这条原则我们应回 Reject 或 Table，直到数据补齐。
- **与主线关系（对照表 2.3）**：机制**主线一致**（2013 年已进 Main 的 VB-specific 特性）；范围是 **Anthony 独立延伸**（主线在 `Sin(1.0)` 处明确选择"保留歧义，不值得操心"，Anthony 选择把延续驱动推到死胡同）；与主线的保守节奏**方向一致但张力明显**——主线对扩展设高门槛，而此处是往既有规则里再挖一层。与 `proposal-method-level-imports.md`（D）是互补关系：D 从作用域侧缓解，本建议从解析侧缓解。

### RESOLUTION:

1. **原则上认可"延续驱动名称解析"的定位**——2013 年已进 Main、VB-specific，本建议是其自然延伸，不是外来语法。这不是本场会议的争议点。
2. **v1 只取 PROPOSAL B 的"启用型"窄版**：回退只在"当前绑定必然报错"时触发（类型位置死胡同 + 命名空间成员名缺失死胡同），且**有且仅有一个**类型候选能延续；多个候选一律报歧义。重载解析失败不构成死胡同。
3. **回退不得重劫持**：命名空间上存在同名成员（含扩展成员/模块提升成员）时，绑定原样保留。这是 "启用型" 约束的测试用例，必须写进 spec。
4. **语义模型必须单符号化**：`Console` 在给定代码位置上只绑定一个符号，回退后 `GetSymbolInfo` 返回回退目标；IDE 补全需展示"命名空间子成员 + 回退类型成员"并标注来源，find-all-references 不得分裂。此项在原型中验证，否则本特性不上线。
5. **C 的诊断增强先行**：无论 A/B 是否落地，先把 BC30456 的文案改为提示"did you mean type `System.Console`?"并附快速修复（补 `Imports System` / 改 `Global.System.Console`）。这是零风险的立即可交付项。
6. **`NameOf` 与普通成员访问走同一条回退路径**，一致性优先，但在原型中验证 rename 行为。
7. **与方法级 `Imports`（D）并行推进**，D 不阻塞 A/B，A/B 也不依赖 D；两者在真实数据出来前都不得升 Active。

### Implication:

- 撰写最小原型：仅 B 的成员访问死胡同 + 类型位置死胡同 + "唯一延续者"约束 + 语义模型单符号；验证 `GetSymbolInfo`、补全列表、rename/find-all-references。
- 起草 speclet：死胡同判定（哪些错误构成死胡同）、"启用型"双轨试探、多候选歧义规则、`NameOf` 一致性、与模块提升成员/扩展成员的交互。
- 先交付 C：错误文案增强 + 快速修复（独立于 A/B 可上线）。
- 补数据：量化"第三方命名空间遮蔽 `System` 类型成员访问"在现代 .NET 项目中的频率；对比 2014-02-17 那份纪实的"前缀歧义"例子，确认是同一族痛还是两回事。
- 与 D 团队对表：方法级 `Imports` 落地后，A/B 的优先级是否下调。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：多个类型候选但**仅一个**在目标作用域内可见（其余被遮蔽）时，是否算"唯一延续者"？——We `Probably` 应算，但需 spec 措辞。
- `OPEN QUESTIONS`：IDE 补全的"两栏展示"（命名空间子成员 + 回退类型成员）与 find-all-references 的符号分裂，原型是否可接受。
- `OPEN QUESTIONS`：是否需要门控（VB 无 `langversion` 传统）；若需要，是 `Option` 还是警告。
- `TODO`：验证建议原文的"补 `Imports System` 即可消除遮蔽"是否在真实编译器成立——`System` 通常已被项目级导入，显式 `Imports System` 为何能改变优先级，我们 `Suspect` 此断言未经证实，可能是 Anthony 的理想化表述。
- `Follow-up`：与 2014-10-08 spec 4.3.2 改动（合并查找再重载解析）的行为差异表——两者都改名称解析，必须确认互不干扰。

### 状态

- **LDM 状态：Consider**（v1 窄版 B + 诊断增强 C 并行；宽版 A 划为 Table）。
- **三态判定：Consider** — 机制有主线先例（2013）、零新语法、有"启用型"安全约束，但建议原文无算法、无数据、无兼容性分析，且语义模型/IDE 的"一位置一符号"风险未解。先交付零风险的诊断增强与数据收集，再谈编译器级回退。若数据显示这是成族的痛，升 Active 并做窄版原型；否则维持 Consider 或落 Table。

---

## 附录：特性评价

# 建议评价报告：proposal-name-resolution.md

## 评价对象

- 建议：proposal-name-resolution.md — 名称解析优化（`Console` 命名空间遮蔽修复）
- 来源：Anthony 原文第 16 章 "Performance and Interoperability"（`..\AnthonyDesign_wordpress.txt`，L2528–2535 的 `Fix:` 注释块）。原文仅给出问题与方向，未给出具体算法（建议文档自认："Anthony 原文仅给出问题与方向，未给出具体算法"）
- 配方目标：当 `Imports` 的命名空间（如 `Microsoft.Extensions.Logging`）内含与目标类同名（`Console`）的子命名空间导致遮蔽时，编译器更智能地解析，避免显式 `Imports System`

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。目标改进清晰（免显式 `Imports System`）、示例可演示（BC30456 是真实错误），但：① 无原型/运行证据封顶；② 建议原文唯一示例同时出现在 Motivation 与 Detailed design（无"编译成功"的正例演示）；③ 关键子效果——类型位置（`Dim x As Console`）、`NameOf`、多候选——完全未覆盖；④ "补 `Imports System` 即修复"断言未经证实（`Suspect`）。未决问题 ≥4 个（4 个关键设计点），按标准效果证据等级封顶 | 已检查 | 无运行证据；唯一样例即报错样例；无普遍性数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造"（此处更准确说：借鉴**主线自身 2013 年**的 VB-specific 特性做延伸）。零新语法、延续"延续驱动"VB 原则、读起来像英语（#5）——强项；但"同一标识符上下文相关绑定"是隐蔽语义变化（#7）的新形态，原则 #3（不引入第二种做事方式）张力未化解；与 `Sin(1.0)` 处主线"保留歧义"的选择方向相左 | 已检查 | 上下文相关绑定；与主线对歧义的容忍度冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、Drawbacks/Alternatives 诚实；但 Detailed design 只有报错示例、无算法、无 spec 章节、无边界/角案例；Unresolved questions 4 个关键设计点（按标准属健康区间，但无一被定夺，效果封顶）；状态行为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；无 Compatibility/breaking-change 分析；未提 2013 年主线先例（重大遗漏，见炼金成分） | 已检查 | 缺算法与触发条件定案；缺兼容性分析；占位链接 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/光受益（盘活 2013 资产、消除高频遮蔽样板、新手直觉友好）；风受损（语义模型"一位置一符号"地基被动摇，与 2014 `GetName` 讨论的 IDE 担忧正面相撞）；暗风险（上下文相关绑定、重劫持风险）有"启用型"约束对冲但文档未显式识别 | 已检查（预测待定） | 风=语义模型/IDE 风险未权衡；暗=重劫持边界未写；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注……标注与影响有偏差"。材料=Anthony 第 16 章 `Fix:` 注释；**未声明借鉴主线 2013 年 "Smarter name resolution"**——这是本建议最重要的成分，其"延续驱动"原则并非原创而是主线已进 Main 的 VB-specific 特性，文档未提；未声明与 `Sin(1.0)` 主线"保留歧义"立场的关系；无杂质（未照搬 C#、未混入闭源） | 已检查 | 遗漏 2013 主线先例这一关键成分；对"补 `Imports System` 即修复"的断言未核实 |

## 设计原则对照

- **与 VB 基因：部分一致**——零新语法、延续 VB-specific 的 2013 原则（#3 的正面）、读起来像英语（#5）；张力于原则 #7（上下文相关绑定的隐蔽语义变化，靠"启用型"约束对冲）、原则 #6（无数据、单一样例，边缘场景论证不足）、原则 #3（隐式回退是第三种消歧方式）。
- **与主线关系：主线一致（机制）/ Anthony 独立延伸（范围）**——"延续驱动查找"2013 年已进 Main 且 VB-specific；把触发条件从"前缀歧义"扩展到"前缀死胡同"是主线未做、Anthony 的延伸；与主线在 `Sin(1.0)` 处"保留歧义、不值得操心"的保守选择**方向相左**但非冲突（主线可因数据改变立场）。与 `proposal-method-level-imports.md` 互补。
- **破坏性变更：语言层面无（启用型下不改变可编译代码绑定）；工具生态有**——语义模型/诊断 API 对"原本报错、现在通过"代码的符号可见性变化，需单独交代。

## 总评

- **达成程度：部分达成**——问题的真实性（BC30456 遮蔽）与机制的正统性（2013 先例）成立；但建议原文的可实现性（无算法）、数据支撑（无普遍性证据）、风险对冲（IDE/语义模型）均未到位。
- **LDM 三态建议：Consider**——机制值得一个窄版原型（B：启用型 + 唯一延续者 + 语义模型单符号），但建议原文不足以升 Active；宽版 A（成员访问失败即回退）划为 Table。零风险的诊断增强（C）与数据收集先行。
- **主要问题**：① 无具体算法与触发条件定案；② 无普遍性数据（单一样例）；③ 语义模型/IDE"一位置一符号"风险未设计；④ 未引用 2013 年主线先例（既是成分标注遗漏，也是设计对话缺失）；⑤ "补 `Imports System` 即修复"断言未经证实。

## 返工建议

- **补充章节**：Detailed design 需给出可实现的判定算法（死胡同的精确错误集合、"启用型"双轨试探、唯一延续者的歧义规则）；Compatibility/breaking-change 分析（语义模型/诊断 API、`NameOf`、重载解析失败边界、模块提升成员/扩展方法不重劫持）。
- **补充证据**：最小原型（仅成员访问死胡同 + 类型位置死胡同）；语义模型 `GetSymbolInfo`、补全、rename/find-all-references 验证；现代 .NET 项目中"命名空间遮蔽 `System` 类型"的实测频率数据。
- **未决问题处理**：多候选"仅一个可见"算不算唯一延续者；`NameOf` 走同一回退路径；门控机制（VB 无 `langversion` 传统）需定夺；重载解析失败明确排除在死胡同之外。
- **设计探索**：与 2014-10-08 spec 4.3.2 改动（合并查找再重载解析）的行为差异表；与方法级 `Imports` 的优先级矩阵；"延续驱动"原则能否统一表述 2013 前缀歧义规则与本建议，作为 spec 的一次整体重述而非补丁。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang-index.md` 与 `..\..\csharplang` 镜像核实。先说结论性质的话：本提案（名称解析：重名/遮蔽/名称绑定）与 C# 的 **CLR/低层互操作主线（Span/unsafe/COM/AOT）关系很弱**——meeting #11 已确认纯编译期、不触达 IL。C# 侧真正相关的现实方向在**语言语义层**：简单名称/成员访问/重载决议规则，以及 `nameof`。以下只对照这几个点，不硬凑 interop。

### 相关 C# 现实方向

**C# 名称解析是「由内向外、命中即止」的确定性有序查找；导入冲突不合并、显式报错。**
- C# spec（Simple names）逐字，转引自 → `proposals\csharp-11.0\extended-nameof-scope.md`：「Otherwise, for each namespace `N`, starting with the namespace in which the *simple_name* occurs, continuing with each enclosing namespace (if any), and ending with the global namespace, the following steps are evaluated until an entity is located:」；末句「Otherwise, the simple_name is undefined and a compile-time error occurs.」——查找是「逐层外扩、命中即止」的确定性过程，落空即报错。
- 同一简名若由多个 `using` 导入共同贡献（类型 `System.Console` 与命名空间 `Microsoft.Extensions.Logging.Console`），C# 在简名 `Console` 处报歧义错误，**绝不**按「命名空间优先」静默偏向一方。C# 没有 VB 的 import 冲突合并行为：VB 把各导入的候选**合并成一个候选集**、再按既定优先序定夺（命名空间胜出，拖到 `.WriteLine` 成员访问才报 BC30456）；C# 在名字出现处就把冲突**显式化为错误**，逼用户用别名/全限定/`using static` 消歧。`Suspect`：CS0104 的精确错误文案属 Roslyn 诊断（dotnet/roslyn），本镜像无正文、未逐字核实——见 OPEN QUESTIONS。
- 生态侧（.NET 6+ SDK 默认 `<ImplicitUsings>enable</ImplicitUsings>`，属 dotnet/sdk，非 csharplang）：趋势是**默认导入更多、减少书写**，而不是让编译器在冲突里做启发式。方向与本提案的「隐式回退」相反。

**`nameof`：编译期常量，绑定复用普通规则，且 C# 持续投入。**
- 逐字（→ `meetings\2014\LDM-2014-10-15.md`，nameof spec v5）：「The nameof expression is a constant. In all cases, nameof(...) is evaluated at compile-time to produce a string. Its argument is not evaluated at runtime, and is considered unreachable code (however it does not emit an "unreachable code" warning).」；成员访问绑定逐字：「The normal rules of expression binding are used to evaluate "E", with _no changes_.」
- C# 对 nameof 工具层「一位置多符号」早有容忍（→ `meetings\2014\LDM-2014-05-21.md`）：nameof 参数允许绑定方法组与多种元重载类型——「The ambiguity is a neat trick at the language level, but a bit of a pain at the tooling level.」；面对「Should we limit the application of `nameof` to situations where it is unambiguous?」，结论逐字：「No. Let's keep the current design. We can come up with reasonable answers for the tooling challenges. Hobbling the feature would hurt real scenarios.」
- C# 11（→ `proposals\csharp-11.0\extended-nameof-scope.md`）：「Allow `nameof(parameter)` inside an attribute on a method or parameter.」；C# 14（→ `proposals\csharp-14.0\unbound-generic-types-in-nameof.md`）：「Allows unbound generic types to be used with `nameof`, as in `nameof(List<>)` to obtain the string "List", rather than having to specify an unused generic type argument in order to obtain the same string.」——nameof 作为编译期安全的字符串来源，方向未衰。

**C# 对「API 层绑定歧义」的手段是显式属性，不是隐式重绑定。**
- 逐字（→ `proposals\csharp-13.0\overload-resolution-priority.md`，Summary）：「We introduce a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type as a means of steering API consumers to use specific APIs, even if those APIs would normally be considered ambiguous or otherwise not be chosen by C#'s overload resolution rules.」
- 方向本质：歧义由 **API 作者用属性显式裁决**、编译器照办；编译器不做「替你猜」的启发式重绑定。

### 现实 vs 提案

- **兼容**
  - RESOLUTION #6（`NameOf` 与普通成员访问走同一回退路径）有 C# 直接先例——C# nameof 的成员访问绑定就是对普通绑定「_no changes_」的复用。若 B 落地，`NameOf` 走同路径在 C# 视角是「正确姿势」。
  - C# 对 nameof 的「一位置多符号」（方法组）容忍说明「符号分裂」不必然致命——C# 用 FindAllReferences 对话框承载多符号。**但范围差很大**：C# 只对 nameof 参数容忍多符号；本提案要动的是**所有**成员访问的绑定，这个恐惧不能靠 nameof 先例全免。
- **冲突**
  - **确定性 vs 隐式回退**。同一段代码（`Imports Microsoft.Extensions.Logging` 后 `Console.WriteLine`）：C# 报歧义错误、今天的 VB 报 BC30456、B 落地后的 .vbx 编译成功——三种结果。C# 把「命名空间遮蔽类型」当**需显式处理的错误**，B 把它当**可自动消解的摩擦**。若 .vbx 追求与 C# 行为可互换，B 是负向的；若 .vbx 是 VB 特色脚本层（Anthony 立场），B 正是差异化。
  - **消歧哲学相反**。C# 的逃生舱是别名/全限定/`using static`/隐式 using = 减少书写 + 显式选择；OverloadResolutionPriority = API 作者显式裁决。C# 生态没有「编译器在冲突中隐式选唯一延续者」的机制。
- **需桥接 / 脱节**
  - **跨语言诊断一致性**：`.vbx` 引用含 `Microsoft.Extensions.Logging.Console` 这类命名空间的程序集时，C# 调用方与 .vbx 调用方给出不同诊断（歧义错误 vs BC30456 vs 编译成功）。共享 Roslyn 管道的分析器/源码生成器在跨语言边界会看到不一致符号。
  - **nameof 互操作**：VB 与 C# 共用 Roslyn 绑定管线。若 VB 侧 `NameOf` 走回退而 C# 侧 `nameof` 不走，则「同文本、不同符号」在跨语言重命名/查找引用场景出现——正是 meeting #7 担心的裂缝，且在跨语言维度被放大。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态（名称解析版）**：名称解析保持确定性优先。v1 保守——诊断增强 C 先行（错误文案 "did you mean type `System.Console`?"）与 C#「歧义显式化」一致；隐式回退（A/B）设为 .vbx 脚本层的显式 opt-in，避免默认行为与 C# 分叉。
- **source-gen 桥 / 识别新元数据**：VB 编译器与 IDE 需识别 C# 13 的 `OverloadResolutionPriorityAttribute`——BCL 已开始实际使用（该提案 Langversion 一节自述 `Debug.Assert(bool)` 在 .NET 9 被降优先级）。否则 .vbx 对同一 API 的重载决策与 C# 不一致。这与决策文件 M8「识别 `RequiresUnsafeAttribute` 等新元数据」是同一类「读 C# 新元数据」的桥接任务。
- **跨语言名称解析差异表**：把「同一源码结构在 VB/C# 的绑定与诊断」做成对照表（本提案 `Console` 例：C#=歧义报错、VB 现状=BC30456、B 落地=.vbx 编译成功），供规范、测试与文档共用。
- **原型必测 `NameOf`**：在最小原型里同时验证 `NameOf(Console.WriteLine)`（回退后）与 C# 侧同名表达式的行为差异，避免共享绑定管线在跨语言下不一致。

### 对既有 RESOLUTION / 三态判定的影响

- RESOLUTION #6 获得 C# 先例支持（nameof=普通绑定复用），但「一致性优先」需加注：C# nameof 在遮蔽场景同样报歧义、不存在「回退后的 nameof」——#6 的一致性只在 VB 内部成立，不与 C# 对齐。
- RESOLUTION #2（唯一延续者）在 C# 无对应物，不构成对齐压力（meeting #10 已判断「与 C#（没有同类特性）无对齐压力」）；C# 的「显式消歧」事实进一步确认本特性是 **VB-specific 的 Anthony 特色面**，不是兼容面。
- 三态判定（Consider）**不变**，但新增一个决策维度：若 .vbx 的核心卖点是「脚本易用 + 与 C# 生态可互换」，B 的隐式回退应更保守；若卖点是「VB 特色智能解析」，B 值得窄版原型。这取决于产品定位，不由 C# 方向决定。

### 引用纪律

- 本附录 C# 原文均逐字引用并标注来源（→ `proposals\...` / → `meetings\2014\...`，均相对 `..\..\csharplang`）。
- **OPEN QUESTIONS**
  - CS0104 精确错误文案未在本镜像核实（属 dotnet/roslyn 诊断正文）；「同一简名由多个 `using` 贡献时报歧义」行为可确认，错误码字符串列为 **Suspect**。
  - C# spec（csharpstandard）中 simple-name/member-access 的现行完整条文不在本镜像（`spec\` 目录仅为链接桩）；本附录只引用 extended-nameof-scope.md 内嵌的 spec 片段，其余不引。
