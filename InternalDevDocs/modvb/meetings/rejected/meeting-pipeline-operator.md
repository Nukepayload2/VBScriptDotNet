# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次回到第 8 章控制流/表达式增强里的管道运算符建议。它在一份清单上停着，因为我们一直在犹豫：它是"消除 VBScript 嵌套函数样板"的真药方，还是又一件"不引入第二种做事方式"清单上的违禁品。有趣的是，主线在 2017 年 8 月 23 日的 LDM 议程上**曾经**列出过 "Pipe-forward (`->`) operator"，但那份纪要没有给它留下任何决议——它安静地消失了。我们这次不打算让它再次安静地消失。

## Agenda

* [Proposal: Pipeline Operator `->`（管道运算符）](#proposal-pipeline-operator-)

## Proposal: Pipeline Operator `->`

_Related: [vblang LDM 2017.08.23 议程——"Pipe-forward (`->`) operator"（已议程化、未决议）](https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.08.23.md)；[vblang LDM 2017.08.30——`!` 与字典访问/类型字符的符号碰撞](https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.08.30.md)；[vblang LDM 2018.12.19——模式匹配与 F# 参照边界](https://github.com/dotnet/vblang/blob/main/meetings/2018/vbldm-notes-2018.12.19.md)_

### 场景与缺口

We 从 VBScript 最深的习惯之一出发：**自由函数的嵌套调用**。VBScript 没有方法链文化，`UCase`、`Trim`、`Left`、`Mid`、`CStr`、`Int`、`Round` 这些模块级函数把代码写成洋葱。Anthony 原文第 8 章的示例就是这个形状：

```vb
' 今天：从最内层往外读，括号配对是注意力黑洞。
Return Third(Second(First(x)))
```

多层嵌套的读序与执行序相反——读者必须从内层开始反推数据怎么流到外层。链条越长，收益越大；中途增删一个处理步骤，要动的是整串括号。这个痛点对 VBScript 尤其真实，因为它的函数库是"自由函数"形态（`Probably`：VBScript 遗产的字符串处理几乎全部是这类嵌套，`Mid(Left(Trim(s), 8), 3, 2)` 是再普通不过的写法）。

We see a clear DX gap for that specific population——但我们也立刻意识到，VB.NET 主线的业务代码里，这个缺口大半已经被两样东西填掉了：**方法链**（`.Where().Select()`）与 **LINQ 查询语法**（`From x In ... Where ... Select ...`）。自由函数嵌套是 VBScript 独有的残留痛点，不是 VB.NET 全体用户的痛点。这是贯穿整场讨论的张力：缺口是真的，但缺口的形状比建议书写的小得多。

### 候选方案

**PROPOSAL A — 通用二元管道运算符（F#/Elm `|>` 移植）。** `x -> f` 是普通二元运算符，语义为函数应用：值作为**末位实参**插入，右侧可以是任意表达式（方法组、函数值、lambda）。这是建议 Alternatives 里"把管道作为通用运算符实现（F#/Elm 式 `|>`）"的展开。它最接近 F# 惯用法，也最重——要为一等函数值、委托调用、方法组绑定、优先级整出一套新语义。

**PROPOSAL B — 调用式语法糖（值插入实参，纯语法重写）。** 右侧必须是"调用"形态，管道翻译为普通调用，值按固定位置插入实参列表；不引入函数值概念，不产生新的运行时概念。`First(x) -> Second()` 编译成 `Second(<插入>)`。实现面最小，编译器与 IDE 都能理解。

**PROPOSAL C — 省略实参式"接收者"（原文字面读法）。** 按建议正文读："`Second()`、`Third()` 在管道中作为接收者使用，接收前一段的输出作为其参数（此处省略了显式实参，由管道自动传递）"。即右侧调用**省略**待插入实参、空括号占位，管道把值补进去；仅当右侧调用无显式实参时成立，多实参场景未定义。

**PROPOSAL D — 什么都不做 + 既有手段。** 保持嵌套调用；或用中间变量逐步展开；或鼓励方法链/LINQ 查询语法。这是"为今天和明天的开发者"的默认盘——尤其因为"数十万安静客户"（评价标准 2.1）主要想保持现状。

### 权衡：Q&A

- **A vs B：要不要一等函数值？** 不要。VB 不是函数式语言，委托是"对象"，不是"表达式"。A 形态下 `x -> d`（`d` 是 `Func(Of T)`）要求 `->` 理解委托调用、方法组、lambda——这是一整片新语义，而 VBScript 用户要的只是"把嵌套函数摊平"。**结论：A 否决，走 B 的地盘。** We 也记得主线对宏大想法的标准回应（2018.02.07，对"任意语言互嵌"）："Fantastic idea, and too hard to do."
- **B vs C：空括号到底是什么意思？** 这是整场最尖锐的问题。今天 `DoWork()` 是确定无疑的"调用零参方法"。C 形态要它**在管道里**变成"调用 `DoWork(<值>)`"——同一记号、两个含义，取决于是否在 `->` 右侧。这正是设计原则 #7 说的"细微字符改变语义是坏设计"（`Return?` 被拒的同类理由）。而且 C 形态在多实参面前直接崩掉：`x -> Mid(3, 2)` 里待插入的实参**没有位置记号**——VB 没有部分应用，`Mid(3, 2)` 是 `Mid(3, 2)`，而 `Mid` 的首参是 `String`，`3` 编译失败。**结论：C 否决，或者要求显式记号。**
- **B 的核心问题：显式记号是什么？** 若"待插入实参"必须有记号，最自然的候选是 `It`（或通配 `*`）：`x -> Clean(It)`、`x -> Mid(It, 3, 2)`。注意这一步把"管道"变成了"把 lambda 应用到 x"——`Clean(It)` 就是 `Function(It) Clean(It)`。这会把管道降格为 ModVB **通配 Lambda**（`*.X`，Anthony 独立延伸）的语法糖，与 F# 的 `fun x -> ...` 加 `|>` 同构。We 对这个走向谨慎：它悄悄把特性换成了另一个特性。
- **D：既有手段够不够？** 对 VB.NET 主体：够。方法链 + LINQ 查询语法已经把"从左到右的数据流"解决得比 `->` 更 VB。唯一的裸区是**模块/共享函数**——它们不是扩展方法，没法 `x.First()`。但这块裸区是否值得一种新语法，取决于 VBScript 代码库的真实占比，而建议书没有数据。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`->` 是 VB 词法里**不存在**的双字符 token。今天 `a -> b` 无法解析：`a - > b` 若要硬拆成 `-` 和 `>`，`>` 缺左操作数，是语法错误。所以按最长匹配切出 `->` 不破坏任何既有程序——tokenizer 层无 breaking change（这是本建议唯一干净的部分）。

但歧义在阅读层：建议书自己的 Drawbacks 承认"`->` 与现有运算符集（如 `>`、`>=`、字典 `!`、Lambda `=>` 类符号）视觉接近，可能被误读"。C# 程序员看到 `->` 会想到指针成员访问与 `=>` lambda；F# 里 `->` 是 lambda 箭头（`fun x -> x*2`）。VB 的 lambda 是 `Function(x)`，所以**词法上**不撞，但**视觉上**在 C#/VB 双语程序员脑子里撞。还需定义 `->` 的**优先级与结合方向**：建议书完全没提。F# `|>` 是低优先级、左结合；`First(x) -> Second() -> Third()` 必须左结合才是 `(First(x)->Second())->Third()`。`Return a + b -> F` 是 `(a+b)->F` 还是 `a+(b->F)`？必须有 BNF。

#### 2. 角案例与边界语义

- **空括号/待插入记号（核心）。** `Second()` 在管道外是零参调用；在管道内若被改写为 `Second(<值>)`，是隐蔽语义变化。若 B 形态下要求显式记号 `Second(It)`，则语法改写规则要重写：`->` 不再是"自动传参"，而是"应用一个 lambda"。
- **多实参位置。** `x -> f(a)` 是 `f(a, x)`（末位，F# 规则）还是 `f(x, a)`（首位）？建议书未定。VB 用户直觉可能希望首位（"第一个参数被前段提供"），但 VBScript 遗产（`Mid(str, start, len)`）里被提供的通常是**首参**——两种直觉冲突。
- **命名实参。** `x -> Clean(preserveCase := True)`——插入的实参叫什么名字？`Clean(<值>, preserveCase := True)` 需要一个合成参数名，还是只允许位置实参？
- **重载决议。** 若 `Clean` 有 `Clean(s As String)` 与 `Clean(s As String, n As Integer)`，管道中的 `Clean(It)` 选哪个由"待插入实参的类型"决定——这是普通重载决议，可做，但必须写清。
- **可选参数。** `x -> F()` 若 `F` 无必选参但有 `Optional` 参，插入值合法吗？插入到哪一位？
- **泛型调用。** `x -> Sort(Of Integer)()`——类型实参在括号里，值实参插哪？语法 `x -> Sort(Of Integer)(It)` 是唯一自然形态，需要文法。
- **属性/索引器/字典。** 右侧能否是 `obj.Prop`、`items(i)`、`d!key`？`x -> d!key` 与 `!` 的字典语义纠缠（见 2017.08.30 先例）——我们倾向 v1 只允许方法/函数调用，不允许属性与索引器。
- **lambda 段。** `xs -> Function(y) y * 2` 期望 `(Function(y) y*2)(xs)`。语法上要括号才成立，读起来是灾难。若走 B 形态（仅调用），lambda 段天然出局——我们认为是好事。
- **链中段求值。** `First(x) -> Second() -> Third()` 中 `Second` 的返回值作为 `Third` 的实参。若某段抛出或返回 `Nothing`，与普通调用一致，无新语义。
- **`Nothing` 段。** `Nothing -> F()` 合法，无特殊处理。

#### 3. 作用域与绑定

B/C 形态下，`->` 本身不产生绑定对象；`First(x) -> Second(It)` 语义模型里只有一个普通调用 `Second(<prev>)`，`GetSymbolInfo` 返回 `Second` 的选中重载。A 形态则需要一个"管道"绑定概念，我们已否决 A。方法组在右侧如何绑定：`-> Second`（无括号）算不算调用？建议书只用 `Second()` 形态，v1 应只允许括号调用，把裸方法组留作 A 形态的废墟。

#### 4. 与既有特性的交互

- **ByRef。** 前段结果是**表达式**（函数返回值）。若 `Second` 首参是 `ByRef`，`x -> Second(It)` 传入的是值，copy-back 被丢弃——与 `Second(First(x))` 的现行为一致，无新问题，但要写进 spec。
- **扩展方法。** VB 扩展方法调用里"接收者即首参"。若有人写 `x -> obj.Extension()`，B 形态下是 `obj.Extension(x)`（值作末位实参）；C 形态的"接收者"读法会把它误读成 `obj` 是接收者——两种语义天差地别。**"接收者/参数"必须删掉一个词。**
- **LINQ 查询语法。** `From v In xs Where ... Select ...` 本身就是 VB 味管道。`->` 加入后，同一段数据流有嵌套调用、方法链、查询语法、`->` 四种写法——原则 #3（不引入"第二种做事方式"）直接踩线。
- **第 8 章 `Return` 命名 / `Out` 赋值。** `Return x -> F()` 与 `Out y = x -> F()` 的优先级与结合方向未定义。`Return` 是抓整条链还是只抓 `x`？建议书未决问题里列了，但没给候选。
- **`!` 字典访问。** `x -> d!key` 中 `->` 与 `!` 相邻，视觉与语义都纠缠。2017.08.30 主线的原话："the damnit operator `!` conflicts with both VBs dictionary-access operator `dict!key` and the type character for single-precision floating-point numbers `Dim radius!`"。符号家族已经够挤了。

#### 5. Breaking change 与兼容性

- **Token 层：无。** `a -> b` 今天非法，最长匹配切 `->` 不影响既有源码。
- **语义层：有（隐蔽的）。** C 形态让 `F()` 的含义取决于是否在管道右侧——同一源码片段在管道内外绑定不同方法。这是原则 #7 的红线。B 形态若要求显式 `F(It)`，则无此问题（`It` 是全新记号）。
- **A 形态：** 若 `->` 成为通用运算符并参与委托调用解析，可能与既有表达式树的委托语义纠缠。我们已否决 A，此风险随否决而消失。

#### 6. Option Strict / 编译选项分叉

宽松模式下 `x -> obj.Foo()`（`obj` 为 `Object`）是否晚期绑定？建议书未提。我们的倾向：管道**不改变既有绑定的松散性**——宽松模式下右侧调用照常晚绑定（与 `obj.Foo()` 一致）；严格模式下早期绑定并插入实参。两路径对"插入实参"这一核心行为必须一致：`x -> F(It)` 在两条路径下都把 `x` 传给 `F`。任何"宽松模式不插入"的分叉都是坏设计。

#### 7. IDE / IntelliSense

`x ->` 之后的补全是一个全新体验：IDE 要按"接受 `x` 类型作实参"过滤所有可见函数/方法——本质是把"扩展方法接收者补全"推广到任意函数。Roslyn 已有扩展方法接收者补全的骨架，但"任意函数按参数类型过滤 + 重载高亮插入后的签名"是新工作。签名帮助需要展示插入实参后的完整签名；InfoTip 需要"管道段"的文案与图标。没有 IDE 原型，等于没设计——这是建议书完全缺失的部分。

#### 8. 数据 / 普遍性

这是本建议最诚实的软肋。建议书没有数据。我们推测（`Probably`）：
- **VBScript 遗产代码库**中，自由函数嵌套是高频样板——`Mid(Left(Trim(s), 8), 3, 2)` 是真实写法。这是**唯一强论据**。
- **VB.NET 主线业务代码**中，模块函数嵌套存在但方法链/LINQ 已覆盖大半；新增运算符的边际价值低。
- 主线 2017.08.23 把 Pipe-forward 列进议程就再没回来——主线对此价值的冷处理本身就是信号。主线对"锦上添花但增加表面"的特性用过一句很准的评价（2017.08.23，对晚绑定成员访问）："There's a feature here, but it might not add its weight."

#### 9. 更简替代

- **方法链**：若函数是扩展方法，`x.First().Second().Third()` 已解决。对 VB.NET 主体这是最有力的竞争者。
- **LINQ 查询语法**：`From ... Select ...` 是 VB 自带的"管道"，且是查询式（更声明式、更像英语——原则 #5）。
- **中间变量**：`Let a = First(x) : Let b = Second(a) : Return Third(b)`——冗长但显式、零新语法。
- **通配 Lambda（ModVB 已有）**：`x -> Clean(*)` 若配合管道就是 F# 式；若没有管道，`Clean(*)` 通配 lambda 本身就覆盖了"省一次临时变量"，只是没有从左到右的读序。
- **主线节奏**：C# 至今未做管道运算符（`Probably`，截至我们掌握的信息）。"默认跟随 C#，除非有充分理由"（2018.12.19 模式匹配纪要原话）意味着：我们不领先引入通用管道。

#### 10. 成本 / 优先级

B 形态（显式记号、纯语法糖、限定调用）实现成本中等：词法 + 文法 + 重载决议 + IDE。A/C 形态成本高或语义崩坏。即便 B 形态，它在 VBScript.NET 支持范围内也要排在 ShapeOf 模式匹配、可空性流分析、JSON 一等支持之后。建议书目前是"未定型语法 + 无优先级论证"，无法进入实现队列。

#### 11. 运行时 / CLR 硬约束

无硬约束。管道是纯语法糖，翻译为普通调用；不触达 CLR 存储规则，不破坏 PEVerify。若未来做 A 形态的 `Operators.Pipeline` 辅助方法，也是普通方法调用。表达式树中 `x -> F(It)` 翻译为 `F(x)` 调用，无碍。

#### 12. 值不值得做

- **价值**：真实缺口（VBScript 自由函数嵌套），但覆盖面窄，且方法链/LINQ 已覆盖 VB.NET 主体的数据流需求。
- **成本**：中等偏高；语义空洞（记号、位置、优先级、重载）先要补一年。
- **风险**：原则 #3（第二种做事方式）与 #7（隐蔽语义变化）双双踩线；符号 `->` 与 `=>`/`>=` 视觉碰撞；`!` 碰撞先例在前。
- **判定**：**当前形态不值得。** 缺口值得一个重新设计的窄方案——但那是另一个建议书，不是这一个。

### VB 基因对照

- **消除常见样板（原则 #9）**：对 VBScript 遗产，正中靶心——这是建议书唯一与 VB 基因一致的成分。
- **不引入"第二种做事方式"（原则 #3）**：**直接违反**。VB 已有嵌套调用、方法链、LINQ 查询语法三种数据流表达，`->` 是第四种。这条是我们否决通用运算符的主因。
- **避免隐蔽语义变化（原则 #7）**：C 形态（空括号变含义）踩线；B 形态用显式 `It` 记号可规避。
- **读起来像英语、对新手友好（原则 #5）**：`->` 是符号，不是英语。LINQ 查询语法的 `From/Where/Select` 才是 VB 味的数据流。`Probably`：即便重做，记号也应倾向关键词而非 `->`。
- **与主线关系（对照表 2.3）**：主线对管道是"议程化后沉默"（2017.08.23，未决议）；Anthony 的通用 `->` 运算符是**独立延伸且更激进**——主线保守、Anthony 大规模扩张的根本张力在此实例化。与 ModVB 通配 Lambda（`*.X`）在"lambda 应用"处交汇，需要明确边界。

### RESOLUTION:

1. **不引入通用 `->` 管道运算符（PROPOSAL A 否决）。** 一等函数值/委托管道与 VB 的面向对象形态冲突，价值×成本×风险三条都不过关。
2. **PROPOSAL C（省略实参式"接收者"）否决。** 空括号含义随上下文改变是隐蔽语义变化（原则 #7）；多实参场景无位置记号，`x -> Mid(3, 2)` 直接崩坏。
3. **缺口本身被承认。** VBScript 自由函数（`UCase`/`Trim`/`Left`/`Mid`/`CStr`/`Int`/`Round`）的嵌套调用样板是真实、高频、且**不被方法链覆盖**的痛点——模块函数不是扩展方法。这是本建议最有价值的部分，值得保留为一个独立的工作项。
4. **若未来重做，v1 形态必须满足**：显式"待插入实参"记号（如 `F(It)`，禁止空括号复用）；仅限方法/函数调用，不含属性/索引器/lambda 段；BNF 明确 `->` 低优先级、左结合；重载决议、命名实参、泛型调用、ByRef 逐条写 spec；配 IDE 补全原型。在这之前不落地任何语法。
5. **不使用 `->` 作为记号（`Probably`）。** 与 C# `=>`、F# lambda 箭头、VB `>=` 视觉混淆；`!` 已有与字典访问和类型字符碰撞的先例（2017.08.30 原话）。倾向关键词（如 `Into`/`Intoit`）或 `|>` 之类更清晰的 token——但那要重新设计。
6. **跟随主线节奏。** C# 未做管道（`Probably`），"默认跟随 C#" 意味着我们不领先；F# 只作灵感参考，2018.12.19 已明确 F# 的"full range of potential patterns ... several of these are not available in other .NET languages"。F# 惯用法默认不迁入 VB。
7. **优先补数据。** 在给出任何语法之前，量化 VBScript 代码库中自由函数嵌套调用的占比——这是"值不值得"的唯一实证。`Not all of us are happy with` 承认：我们在为一个尚未证明其体量的缺口做语言层面的大动作。

### Implication:

- 将建议书状态置为 **LDM Rejected（as-proposed）**；从建议目录移至 rejected/inactive 记录，保留"缺口"摘要为 backlog 工作项。
- 起草一份**独立的窄方案骨架**（不算新建议书，只是设计备忘录）：`F(It)` 记号、限定模块/共享函数、非 `->` 记号候选、与通配 Lambda 的边界。
- 与通配 Lambda 工作项对表：`F(*)` 与 `F(It)` 是否同一概念、管道是否降格为 lambda 应用。
- 补数据：抽样 VBScript/VB 代码库统计嵌套函数调用频率。
- 未决问题移交 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：多实参插入位置（末位 F# 规则 vs 首位 VBScript 直觉）——若窄方案重做，必须先定。
- `OPEN QUESTIONS`：命名实参的合成规则；`F(It)` 中 `It` 的作用域与遮蔽规则。
- `OPEN QUESTIONS`：与通配 Lambda（`*.X`）的关系——`F(*)` 是否足以覆盖缺口、是否还需要 `->`。
- `TODO`：量化 VBScript 自由函数嵌套占比，为数据/普遍性补证据。
- `Follow-up`：若 C# 未来落地管道运算符，重开本建议评估"跟随 C#"路径。
- `Suspect`：建议书正文"接收者/参数"二义——"接收者"一词在 VB 语境常指方法调用接收者，与"作为参数传递"是两个语义；原文未消歧。

### 状态

- **LDM 状态：LDM Rejected（as-proposed）**；缺口重设计列 **LDM Considering**（窄方案）。
- **三态判定：Reject（当前形态）**——通用 `->` 运算符不引入；若重设计为"显式 `It` 记号 + 非 `->` 记号 + 仅模块/共享函数"的窄方案，转 **Consider**；整体建议当前 **Table**（挂起，等待 VBScript 使用数据与 C# 形态）。

---

## 附录：特性评价

# 建议评价报告：proposal-pipeline-operator.md

## 评价对象

- 建议：proposal-pipeline-operator.md — 管道运算符 `->`（嵌套调用改写的从左到右数据流）
- 来源：Anthony 原文第 8 章（建议书自注"原文示例（第 8 章）"）；语法形态借鉴 F#/Elm `|>`（Alternatives 自承）；未声明继承 VBScript 自由函数遗产。
- 配方目标：`First(x) -> Second() -> Third()` 等价于 `Third(Second(First(x)))`，让调用链按执行顺序书写。

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 讲嵌套读序难（真实），但示例 `First(x) -> Second() -> Third()` **不能按原样编译**——`Second()` 空括号无法接收值，"等价于嵌套写法"依赖未定义的插入规则 | 已检查 | 示例与正文冲突；无原型；未决问题 ≥4 个关键设计点 ⇒ 效果证据封顶（核心语法未定型） |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`->` 直接借鉴 F#/Elm `|>`，未做 VB 化（无关键词、无查询语法对齐）；"接收者/参数"两个语义捆绑 | 已检查 | 违反原则 #3（第四种数据流表达）；`->` 视觉与 `>=`/`=>`/`!` 碰撞（建议书 Drawbacks 自承） |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节模板齐全，但自相矛盾（空括号 vs 自动传参）；无 BNF/优先级/结合方向；无兼容性分析；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 矛盾是最重的一条；未决问题全是未定而非"待定夺" |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。雷（迭代速度）不加速反拖慢（语义空洞）；风（演化一致性）断裂（与"默认跟随 C#"/LINQ 已覆盖方向相悖）；水（核心竞争力）微弱受益（VBScript 函数嵌套）但无对冲设计 | 已检查（预测待定） | 与主线 2017.08.23"议程化后沉默"形成对照；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。Alternatives 标注 F#/Elm `|>`（准确）；但未标注"继承 VBScript 自由函数遗产"这一最有价值的材料；`=>` 碰撞提及 C# 未展开"跟随 C#"原则冲突 | 已检查 | 最亮的成分（VBScript 遗产）反而是未声明的；无杂质但成分说明不全 |

## 设计原则对照

- **与 VB 基因：偏离**。原则 #3（不引入第二种做事方式）直接违反——嵌套调用、方法链、LINQ 查询语法之外再加第四种；#5（读起来像英语）——`->` 是符号非英语；#7（避免隐蔽语义变化）——C 形态下 `F()` 空括号含义随上下文改变。唯一一致处是原则 #9（消除 VBScript 嵌套样板）。
- **与主线关系：Anthony 独立延伸，且与主线节奏冲突**。主线 2017.08.23 议程化 "Pipe-forward (`->`) operator" 后未决议即搁置；主线"默认跟随 C#"（2018.12.19），C# 未做管道；F# 只作灵感参考（2018.12.19 明确 F# 模式列表"several of these are not available in other .NET languages"）。通用 `->` 运算符比主线激进。
- **破坏性变更：token 层无**（`a->b` 今天非法）；**语义层有隐蔽风险**——C 形态 `F()` 管道内外不同义；A 形态若进入委托调用解析可能纠缠表达式树。建议书未做兼容性分析。

## 总评

- **达成程度：未达成（as-proposed）**。概念缺口真实（VBScript 自由函数嵌套样板），但语法形态不可接受、核心语义空洞、示例与正文自相矛盾。
- **LDM 三态建议：Reject（当前形态）**；缺口重设计为"显式 `It` 记号 + 非 `->` 记号 + 仅模块/共享函数"的窄方案后转 **Consider**；整体 **Table**（挂起等待使用数据与 C# 形态）。
- **主要问题**：① 空括号接收规则自相矛盾；② 多实参位置/命名实参/重载决议/泛型调用未定义；③ 违反原则 #3 与 #7；④ 符号 `->` 与 `=>`/`>=`/`!` 视觉碰撞（`!` 碰撞有 2017.08.30 先例）；⑤ 与 LINQ 查询语法、方法链、通配 Lambda 三层重叠；⑥ 无 BNF、无优先级、无兼容性、无 IDE 设计、无数据。

## 返工建议

- **补充章节**：BNF/文法（`->` 优先级、左结合、`Return x -> F()` 与 `Out y = x -> F()` 的解析表）；"待插入实参"记号设计（`F(It)` vs 空括号，推荐前者并禁后者）；多实参插入位置（末位 vs 首位，需论证）；命名实参合成；重载决议规则；泛型调用 `F(Of T)(It)`；ByRef/可选参数；兼容性分析。
- **补充证据**：VBScript 真实代码库中自由函数嵌套调用占比数据（最有力论据必须数据化）；模块函数 vs 扩展方法的对照实验；IDE 补全原型。
- **未决问题处理**：删除"接收者/参数"二义（二选一）；删除通用运算符 A 形态；明确 `Second()` 空括号不成立，改为显式记号；与通配 Lambda 工作项对表，判定 `F(*)` 是否已覆盖缺口。
- **设计探索**：非 `->` 记号（关键词 `Into`/`Intoit` 或 `|>`）；限定"模块/共享函数"范围；与 LINQ 查询语法/方法链的转换重构；C# 管道运算符落地后的"跟随 C#"重评估路径。

---

## 附录：C# 生态与互操作考量

本附录把本提案放到 C#/CLR/.NET 生态的现实坐标里核对。结论先行：**管道运算符与 C# 互操作机制的关系是弱的**（纯语法糖、零 CLR 足迹），真正的对照在「生态习惯」——C# 用扩展成员 + LINQ 表达从左到右的数据流，而非某个 `|>` 运算符。因此本附录不硬凑互操作细节，聚焦「C# 现实方向 vs 本提案响应」。

### 相关 C# 现实方向

- **C# 没有管道运算符（已核实）。** 在 csharplang 官方仓库的正式记录（`meetings/` + `proposals/`）内，全文没有 `|>` 提案、没有 "pipe-forward" 字样、也没有把 "pipe operator" 当语言特性讨论。唯一一次说出 "pipeline operator"，是在**否决**「点后缀 `await`」时顺带表态——原文（LDM-2020-11-11）：
  > "While we are sympathetic to the desire to make awaits more chainable, and the `.` can be viewed as the pipeline operator of the OO world, we don't think this solves enough to make it worth it."
  → `meetings\2020\LDM-2020-11-11.md`
  注意措辞：这是把「成员访问 `.`（方法链）」称为"面向对象世界的管道运算符"，**不是**提议一个通用 `|>`。C# 的态度可概括为：**方法链就是 C# 的管道**。
- **数据流由扩展方法 + LINQ 承担。** 扩展方法自 C# 3 起就是把「模块级能力」挂到任意类型、让 `.` 之后可链式发现的基础设施。扩展方法工作组文档的 Motivation 原文：
  > "Extension methods are wildly popular! They first appeared in C# 3 as a smallish feature intended to support the syntactic rewrites of LINQ's query expression syntax."
  → `meetings\working-groups\extensions\extensions-an-evolution-of-extension-methods.md`（Motivation）
- **C# 14 正在把扩展方法升级为「扩展成员」。** `Language-Version-History.md` 对 C# 14 的登记原文：
  > "allows extending an existing type with instance or static methods and properties."
  → `Language-Version-History.md`（C# 14 条目）
  这是 C# 对「给既有类型添加行为」的一等语言化（`proposals\csharp-14.0\extensions.md`，C# 15 续；`..\..\csharplang-index.md` T8）。
- **互操作主线与管道正交。** C# 的 interop 现实方向（Span/ref 低层内存、unsafe evolution、source-gen/AOT，`..\..\csharplang-index.md` T2–T6）均不涉及"从左到右的普通调用流"。管道运算符不产生新 CLR 元数据、不触达存储规则、不破坏 PEVerify（本提案 §11 已确认）——它不在 C# interop 的雷达上。

### 现实 vs 提案

| C# 现实方向 | 本提案 | 判定 | 理由 |
|---|---|---|---|
| C# 无管道运算符，以 `.` 方法链为管道（LDM-2020-11-11） | 主线"议程化后沉默"（vblang 2017.08.23）；本提案 REJECTED（as-proposed） | **兼容** | 双方都拒绝通用管道；"默认跟随 C#"（vblang 2018.12.19）被现实印证 |
| C# 用扩展方法/LINQ 表达数据流，C# 14 升级为扩展成员 | PROPOSAL A（F# 式通用 `|>`）已否决；PROPOSAL B（纯语法糖）是残留窄方向 | **需桥接** | 若窄方案落地，应以扩展成员为参照系（把模块/共享函数"成员化"成可链成员），而非引入第四种数据流表达 |
| 模块级自由函数（`UCase`/`Mid`…）在 C# 无对应（它们不是类型成员） | 缺口被承认（RESOLUTION #3） | **需桥接** | C# 生态没有"自由函数嵌套"问题，因为一切函数都是成员/扩展成员；VBScript.NET 的最优出口是"成员化"而非"管道化" |
| F# `|>` 是管道先例，但 C# LDM 从未引用它 | 建议书 Alternatives 自承借鉴 F#/Elm `|>` | **脱节** | C# 生态无采纳 F# 管道惯用的迹象；vblang 2018.12.19 亦明确 F# 模式"several of these are not available in other .NET languages"，不默认迁入 |

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态不变。** 管道不改变宽松/严格模式的绑定规则（本提案 §6 已定：两条路径对"插入实参"行为必须一致）。若窄方案落地，继续走 PROPOSAL B——纯语法糖、翻译为普通调用、零 CLR 足迹——这是与 C#/F#/VB 三语言元数据互通最安全的形态，不需要任何新元数据或运行时概念。
- **source-gen 桥：不适用。** 管道无"反射/动态"成分，不触及 source-gen/AOT 压力（`..\..\csharplang-index.md` T5/T6）；真正需要 source-gen 桥的是 `Any`/晚期绑定（M2），与管道无关。如实记录：本提案不需要编译期生成。
- **识别新元数据（外围必修）**。管道自身不产生新元数据；但 VBScript.NET 若要消费 C# 14 扩展成员（`ExtensionAttribute` 编码的扩展容器），编译器需识别其元数据形态，才能把 C# 扩展成员当作可链成员提供给 `.vbx` 的 IntelliSense/重载决议——这是"模块函数成员化"路径的前置工作。另：C# 15 unsafe evolution 会给成员标 `requires-unsafe` 元数据，索引已核实 C# 对 VB 的原文表态："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."（→ `proposals\unsafe-evolution.md`，「VB」小节）——管道若链到这类 C# 成员，VB 无 unsafe 上下文可呼应，应照常当作普通调用（不参与 C# 的 requires-unsafe 校验边界）。
- **记号选择**。RESOLUTION #5 倾向非 `->` 记号。C# 词法中没有 `|>`（已核实），但 `|>` 与 `||`/`>` 视觉碰撞；关键词（`Into`/`Intoit`）在任何语言都无歧义，是更稳的选择。`->` 则与 C# 指针成员访问、F# lambda 箭头在双语程序员脑中撞（本提案 §1 已述）。

### 对既有 RESOLUTION/三态判定的影响

- **RESOLUTION #6 的 `Probably` 升级为"已核实"。** "C# 未做管道"在 csharplang 正式记录内成立（无提案、无 LDM 讨论、无 `|>`），"默认跟随 C# 意味着我们不领先"的证据从 `Probably` 变为实证——**强化**原判定，不改变它。
- **三态判定不变：Reject（as-proposed）/ Table。** C# 现实未提供任何"该引入管道"的反向压力；反而以"扩展成员 + LINQ"指示了替代出口。
- **Follow-up 触发条件收窄。** 原 Follow-up 是"若 C# 未来落地管道运算符，重开评估"。按正式记录，管道不在 C# 任何 milestone（Working Set/Backlog/Any Time，`..\..\csharplang-index.md` T1）上，近期无触发。真正值得 VBScript.NET 盯的是 C# 15 扩展成员的演化——若扩展成员覆盖"模块/共享函数链式化"，本提案窄方案也会随之 Table（缺口被 C# 路径消化），而不再需要任何管道语法。

### 引用纪律

**本次已逐字核实并引用**：
- "While we are sympathetic to the desire to make awaits more chainable, and the `.` can be viewed as the pipeline operator of the OO world, we don't think this solves enough to make it worth it." → `meetings\2020\LDM-2020-11-11.md`
- "Extension methods are wildly popular! They first appeared in C# 3 as a smallish feature intended to support the syntactic rewrites of LINQ's query expression syntax." → `meetings\working-groups\extensions\extensions-an-evolution-of-extension-methods.md`（Motivation）
- "allows extending an existing type with instance or static methods and properties." → `Language-Version-History.md`（C# 14 条目）
- "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（「VB」小节；索引第四节已核实）

**本次已核实的事实（非引用）**：
- csharplang 正式记录（`meetings/` + `proposals/`，镜像于 `..\..\csharplang`）全文不存在 `|>` 运算符、无 "pipe-forward" 提案、无 "pipe operator" 语言特性讨论；`meetings/` 中唯一含 "pipeline operator" 字样处即 LDM-2020-11-11。

**OPEN QUESTIONS**：
- GitHub issue 追踪器（dotnet/csharplang/issues）不在本镜像内；"C# 社区层面是否有人请求过 `|>`"无法据此核实，只能断言"正式 LDM 记录内无"。
- `proposals\csharp-14.0\extensions.md` 无 Motivation 章节（直接进入 Declaration），扩展成员的动机散见于工作组文档与 champion issue #8697；champion issue 正文不在镜像内。
