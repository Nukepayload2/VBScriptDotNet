# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-duplicate-declarations.md`（重复声明处理与 `Appendable` 方法）。它对应的原文是 Anthony 第 18 章 18.17，全文只有两段话、零代码示例，是整份设计清单里最接近"想法碎片"的建议。按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期"，且 "It is perfectly fine for work to happen on inactive or rejected proposals, and for them to be resurrected later."。所以今天会议的问题与上一场目标类型化会议相同，但答案可能更干脆：**这个想法值得激活、继续搁置，还是我们根本没有被给到足够的材料来讨论它？**

我们开场就遇到了一个结构性的反常：这份建议声称要解决的痛点（源生成器作者的名称冲突与重复定义）**真实存在**，但建议没有给出任何一层、任何一种具体处理方案——它是一张"问题的地图"，不是一份"设计"。我们把整场会议的大部分时间花在判断：这到底是一个等待具体化的语言特性，还是一个应该留在生成器层、靠库与诊断解决的工具问题。会议室对这两者给出了不同的温度。

## Agenda

* [Proposal: 重复声明处理与 `Appendable` 方法（Duplicate Declarations & "Appendable" Methods）](#proposal-重复声明处理与-appendable-方法)

## Proposal: 重复声明处理与 `Appendable` 方法

_Related: [vblang #107 – Replaceable Members](https://github.com/dotnet/vblang/issues/107)；[vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；[vblang #102 – Support Top-Level Statements in a Single Entry-Point File](https://github.com/dotnet/vblang/issues/102)；2017-11-15 LDM（源生成器状态调查）；2018-02-07 LDM（#211 "Fantastic idea, and too hard to do"）；2014-02-17 LDM #20（Partial modules and interfaces）；2014-10-08 LDM（隐式 shadowing 的反复）；ModVB：`proposal-partial-members.md`（13.3）、`proposal-replacement-modifiers.md`（13.2）、`proposal-smart-attributes.md`（13.1）、`proposal-semantic-preprocessing.md`（13.4）、`meeting-top-level-code.md`（Immersive Files）_

### 场景与缺口

We started from the proposal's own motivation, which quotes Anthony's original two sentences. We read the original in full (18.17) and want to record it verbatim, because the meeting is substantially a commentary on these words:

> "Source generator authoring (and performance) could be negatively impacted trying to avoid name clashes, duplicate definitions (or ensuring there aren't). I've thought through some ways to handle this at various layers."

> "I personally dislike stitching together statements from across multiple files into one method. I don't want two VB script files to just run back-to-back or for end-developers to have to think too hard about the order partial files would be combined in. With that said, a source generator analog to a "multicast delegate" could impact how certain scenarios are addressed between my various declarative programming approaches."

三个负约束，一个正类比。负约束是**钉死的**：不拼接、不背靠背、不要求终端开发者思考顺序。正类比（"源生成器版的多播委托"）是**悬空的**——原文和本建议都没有把它落成一个可操作的模型。我们在会议里反复回到这三负一正，并最终认为：三负把绝大多数"重复声明处理"的朴素方案（把 N 个文件拼成一个方法体）直接否决了；剩下的设计空间，比标题暗示的要窄得多。

We also noted a scope fact that the proposal itself does not acknowledge：18.17 的标题其实是三件事——"Handling duplicate declarations, 'File' accessibility, 'Appendable' methods?"——本建议只接管了其中两件，把 "File" accessibility 静默丢弃了。`Suspect`：这一项要么应该并入 Immersive Files / top-level-code 的"文件作用域可见性"讨论，要么是 C# 11 `file` 修饰符的 VB 对应物；无论如何，静默丢弃原标题的 1/3 而没有一句说明，是建议文档自己给品质埋的雷。

**真正的缺口是什么？** 我们把它分成三层：

1. **生成器作者的名称冲突与去重成本**——写源生成器时，要为每个输出挑不撞车的名字、避免与手写代码或另一个生成器的输出重复。这是本建议唯一有具体受害者的部分，也是我们认为"真实但需要量化"的部分。
2. **声明式编程家族（13.1–13.4）的"多源贡献同一目标"模型**——Smart Attributes 标记、`Replaces` 替换、Partial 成员补元数据、`##If` 语义预处理，这四份建议各自对"同一目标被多份声明引用时怎么办"做了**不同的**假设（见权衡 Q&A 第 4 条）。本建议想当这块的连接件，但它自己也没有模型。
3. **多脚本文件的组合语义**——"两个 VB 脚本文件不能背靠背跑"。这不是重复声明问题，是执行模型问题，与 Immersive Files 会议直接相关（见深度追问 4）。

### 候选方案

因为没有具体语法，我们把"方案"定义为**层**——Anthony 说 "various layers"，我们把它枚举出来，并逐层判断值不值得。

**PROPOSAL A — 语言层 `Appendable` 语法。** 给一个成员标上某种标记（`Appendable` 或别的新关键字/修饰符），多个文件可以各自声明"我向这个目标贡献一段行为"，编译器把贡献合并为一次多播调用。这是"多播委托类比"直接语言化的唯一形态。代价最高：新语法、新绑定、新语义、新 IDE 面；收益未知。

**PROPOSAL B — 生成器/API 层契约。** 不碰语言。定义一个源生成器库契约：生成器声明"这个成员是目标，我的输出是向它追加的处理器"，编译器只负责提供**诊断**（检测到生成代码与手写代码、或两个生成器之间的重复成员时，把两个声明都指出来）。合并逻辑由生成器框架在"生成文本"这一步完成。这是 "various layers" 落到生成器层。

**PROPOSAL C — 元数据层复用。** 不新增机制。`Partial` 成员（13.3）已经能合并特性与 `Handles`；`##If`（13.4）已经能按条件包含/排除声明；`Replaces`（13.2）已经能表达"唯一赢家"。把这些既有机制组织成一个一致的"贡献模型"，并补上诊断。

**PROPOSAL D — 什么都不做（维持 inactive）。** 生成器作者继续自己处理冲突；语言提供更好的重复成员诊断文案。这就是主线的现状（2017-11-15 之后主线对源生成器的姿态是"生成器优先"），也是我们给"不作为"的辩护。

We agreed early that B 与 C 是天然可以合并的（生成器层契约 + 元数据层复用 + 诊断层补强 = 一个不需要新语法的完整答案）；A 是唯一要求新语言表面的方案，也是唯一值得我们花大力气审问的方案。

### 权衡：Q&A

- **A 的核心：`Appendable` 是"方法"还是"目标"？** 如果 `Appendable Sub` 的含义是"一个方法体由来自多个文件的语句片段拼成"，那**它就是拼接**——18.17 第一句就否决了它。我们在房间里确认：建议标题里的"Appendable 方法"与正文的"不喜欢拼接"之间，只有一个读法不矛盾——`Appendable` 指的不是一个可拼接的方法体，而是一个**贡献目标**（target），每个来源贡献的是完整、独立、可独立调用的单元（handler），目标按某种顺序调用它们。**任何把"片段"当贡献单元的形态，都是自相矛盾的。** 这个读法一旦确定，A 就与事件同构了——见下一问。

- **VB 已经有这个模型：事件。** `Handles` + `RaiseEvent` 就是 VB 的多播委托；多处理器注册、按注册顺序调用、处理器之间相互独立，全部现成。我们问了那个必须问的问题：`Appendable` 相比 `Event` 多给了什么？候选答案有三个，但都未经验证：① 不需要手写 `RaiseEvent` 触发点（生成器"方法形"目标省一层仪式）；② 可能是 `Function`（而 `Event` 是 `Sub` 形）；③ 终端开发者完全看不到组合过程。这个"增量"到底值不值一个新语法，建议没有论证。**我们 `Probably` 认为增量是真实的，但它是"薄增量"。**

- **顺序三角：Anthony 不想让开发者思考顺序，但多播委托本质是顺序敏感的。** 这是整场会议最尖锐的问题，我们给它起名"顺序三角"：

  - 两个贡献若**改写同一状态**，顺序决定结果：
  ```vb
  ' 贡献 1：results.Insert(0, "first")
  ' 贡献 2：results.Add("last")
  ' → 交换顺序，最终列表内容不同。
  ```
  - 两个贡献若**互不相关**，顺序无关：
  ```vb
  ' 贡献 1：count = count + 1
  ' 贡献 2：count = count + 2
  ' → 加法可交换，顺序无关。
  ```
  - 所以"顺序无关"只能靠**限制贡献的副作用**保证，而"允许任意副作用 + 不指定顺序"等于非确定性——两者都不可接受。剩下的路只有"显式顺序"，但那是 Anthony 明确拒绝的。**三边都不通。** 我们的结论：任何 `Appendable` 设计必须先把"贡献是否允许有顺序敏感的副作用"这一条写死；建议对这个问题完全沉默，这是它不能激活的硬理由之一。

- **返回值、`Exit Sub`、异常。** 多播委托的既有语义：某个目标抛异常，调用链中断；`Function` 形委托的返回值取"最后一个"或弃用。如果 `Appendable` 允许 `Function`，哪个贡献的返回值赢？`Exit Sub`/`Return` 出现在某个贡献内部时，是只退出该贡献还是整条链？我们倾向：**`Sub`-only（事件式）**，贡献者若要产出，走 `ByRef` 或集合参数。但这意味着"方法形目标"的体面叙事打了折扣——它其实就是一个名字更好听的事件。

- **家族内不一致是真实发现，不是吹毛求疵。** 13.x 家族对"同一目标被多份声明引用"分别假设了什么：`Replaces`（13.2）假设**恰好一个赢家**；`Partial` 成员（13.3）假设**一个主体 + 合并元数据**（"Only one method can supply a body"）；`Appendable`（本建议）假设**多个贡献者并存**；`##If`（13.4）假设**条件筛选**。四个假设互不兼容。本建议如果要做，第一步不是设计语法，而是先在家族层面裁定"同一目标的声明关系"到底有几种、每种什么语义。We do not believe any one of the four can be designed in isolation.

- **"各层处理"到底是哪几层？** 建议引述原文 "I've thought through some ways to handle this at various layers"，但从未枚举这些层。我们替它枚举了：语言层、生成器层、元数据层、诊断层。观察：**最便宜的层（诊断）与次便宜的层（生成器）都不需要新语法**。一张"地图"不给出任何一层的设计，在我们看来就是"想法清单"，不是提案。

- **B/D 的辩护：这是不是"为生成器作者造语言"？** 受益者是源生成器作者——小众、技术型、且恰恰是能自己写库解决自己问题的一群人。主线 2017-11-15 的判词是我们的锚：`INotifyPropertyChanged` 是源生成器特性的 poster child，且 "We're fairly confident that if we had `Replaces` w/ source generators we wouldn't do `Bindable` or `WithPropertyEvents`"。主线的战略是**生成器优先**——让生成器自己消化问题，语言不介入。本建议的"名称冲突成本"若真疼，疼的该是生成器作者，而他们有能力在库层解决；语言层介入需要一个更强的理由，建议没给。

- **"背靠背跑"其实已经部分被另一场会议解决了。** Immersive Files 会议（`meeting-top-level-code.md`）裁定"每次编译至多一个沉浸式文件可含顶层可执行语句；其余只贡献声明"。如果这个裁定成立，那么"两个脚本文件不能背靠背跑"的担忧已经由"只有一个入口文件会跑"回应了。本建议与那份裁定没有交叉引用，`Suspect`：它写于或独立于那份裁定的语境之外。**两份建议必须对表**（见 RESOLUTION 第 7 条）。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

无语法可审。这是我们第一次在 LDM 里对"没有语法"感到放心而不是失望——因为建议诚实声明"原文未给出任何 VB 代码示例，故本建议不虚构语法"。**这份诚实值得记下来**：在 `..\..\AnthonyDesign_wordpress.txt` 18.17 里确实没有任何代码，`proposal-duplicate-declarations.md` 没有像某些建议那样凭空造一个会编译的假语法来充门面。这符合评价标准里"凭空虚构原文没有的语法"是品质最重违规的红线。

但我们仍要指出文法层面的未来地雷，供激活时参考。任何 `Appendable` 形态一旦出现，它要坐落的语法空间已经有住户：`Partial`（13.3）、`Replaceable`/`Replaces`/`MustReplace`（13.2）、`Overridable`/`Overrides`、`Shadows`、`Event`/`Handles`。一个新修饰符与它们之间的**优先级/互斥表**完全没有。此外，18.17 原文里唯一的代码片段是第 18 章早前那句 "`&=` should work whether concatenating a string variable or a StringBuilder"——它在同一大章里，与本建议的"追加"意象（append）同词，但指的是字符串拼接。`Probably` 无关，但 `Appendable` 这个词在 VB 里天然撞"追加/拼接"的日常语义，命名本身就是第一道文法障碍。

#### 2. 角案例与边界语义

我们把 A 假想为"贡献目标"模型后，逐条问角案例，答案没有一条来自建议文档（全部是我们构造）：

- **零贡献 / 一贡献 / 多贡献**：零贡献的目标是否合法（像 partial method drop-out，还是像未实现接口那样报错）？若事件式，零处理器 = 空转发，自然合法；若方法式，零主体 = 不能 drop-out（13.3 会议对非 `Private` 成员的裁定）——两种模型给出相反答案，必须先定模型。
- **贡献者之间的顺序与中断**：抛异常是否中断后续贡献（多播委托是中断的）？`Exit Sub` 只退出当前贡献？
- **共享可变状态**：贡献者改写同一集合/计数器时，顺序三角（见 Q&A）。
- **`Me` 与局部变量**：贡献体是独立方法，`Me` 绑定到所属类型（事件处理器同款）；贡献体内声明的局部变量互不可见。
- **`Handles` 与 `WithEvents`**：贡献者若来自不同文件的类，怎么"注册"到目标——编译期静态 `Handles` 还是运行期 `AddHandler`？前者要求目标可静态解析，后者引入运行期注册时序问题。
- **生成器执行顺序**：两个生成器各自输出的"贡献"，在哪个阶段合并？Roslyn 里生成器执行顺序不确定——贡献的合并顺序若依赖生成器顺序，就是非确定性的。**这是 B 层的致命角案例**，也是"让生成器层自己做合并"必须首先解决的。
- **重复声明保持报错**：无论激活哪一层，**今天会报错的重复成员在激活后仍必须报错**。合并是 opt-in（新语法/新标记），不是 reinterpretation。

#### 3. 作用域与绑定

语义模型必须回答：合并后，语义模型返回**一个符号**（含合并信息）还是**多个符号**（每个贡献一个）？两个模型各有代价：

- 单符号 + 合并信息：`Me.Target` 的 `GetTypeInfo` 是一个方法/目标；但调试器、反射、`GetType().GetMethods()` 看到的是哪个？若编译期为多播委托字段，则符号是字段 + 委托调用，`Me.Target()` 是委托调用。
- 多符号（每个贡献是独立方法 + 一个调度目标）：事件处理器现状——每个处理器是普通方法，导航、断点、`Find All References` 都独立工作。**We lean to 这个**，因为它是现成的、IDE 已验证的模型。

贡献体内部的绑定（`Me`、成员查找、重载决议）按普通方法处理即可，无新意。真正的新问题是**目标本身的绑定**：`RaiseEvent` 对应的是事件符号；`Appendable` 目标对应什么？一个合成方法、一个委托字段、还是一个"伪事件"？建议无答案。

#### 4. 与既有特性的交互

- **事件（最有力的既存替代）**：`Event` + `Handles`/`AddHandler` 已是多源贡献模型。`Appendable` 若落地，必须给出一张与 `Event` 的对照表：`RaiseEvent` 缺省怎么补、`Sub`-only 限制、`Custom Event` 的 add/remove 语义、`WithEvents` 字段。这张表建议一行都没有。
- **Partial 成员（13.3）**：上一场会议已裁定"元数据合并大部分已是规范现状"（`spec/type-members.md` §Partial Methods），且 "Only one method can supply a body"。`Appendable` 若想提供"多个主体"，与这条规范正面冲突；要么放宽（13.3 已把"非 `Private`/`Function` 的恰一实现"定为原则上采纳，多主体不在内），要么另立模型。
- **`Replaces`（13.2）**："唯一赢家" vs "全体贡献"的关系未定义。一个成员可以同时 `Replaceable`（允许被替换默认实现）与 `Appendable`（允许他人追加）吗？优先级/冲突模型缺位。
- **`Shadows` / 重载**：2014-10-08 会议是 shadowing 的深坑现场——我们当时 "never came up with a policy around CONSUMPTION of such things"，最后走向 PROPOSAL4b（干净地禁止声明 C）并留下 "If we hear pain, then we can re-visit."。`Appendable` 若让"同名多声明"变得合法，等于在声明层重建一个 shadowing 式的歧义面，而我们上次在 shadowing 上的历史是反复的、谨慎的。**We are not eager to reopen that file**。
- **顶层代码 / Immersive Files**："两个 VB 脚本文件不能背靠背跑"与 top-level-code 会议"单入口文件"裁定直接相关（见 Q&A）。贡献模型若允许"多个脚本文件都贡献可执行语句"，会破坏那个裁定；若只允许"多个声明文件贡献声明/处理器"，则与之一致。
- **Smart Attributes（13.1）**：最有希望的触发场景——一个 `<Attribute>` 把某成员标记为贡献目标，另一个生成器产出贡献。但 13.1 会议对"注入"的评审结果需要与本建议对表（见 Follow-up）。

#### 5. Breaking change 与兼容性

**无语法即无破坏。** 这一点干净。但激活红线必须写死：

- **重复成员声明保持编译错误**。今天的 `'X' is already declared in 'Y'` 是既有行为的护栏；任何"合并重复声明"的语义都不得触碰它。合并只能通过新语法/新标记 opt-in。
- **生成器输出确定性**：今天生成器输出的重复成员就是编译错误（好，确定性）；若未来"重复 = 合并"，错误变成合法，则**重编译行为改变**——这正是 2015-01-14 会议对插值字符串记录的 "the user's call will change behavior upon recompilation" 的同族风险。用新语法 opt-in 可避免。
- **多播委托若为运行期模型**：把"方法形目标"编译为一个委托字段，`GetType().GetMethods()` 上不出现该方法——对反射用户是行为变化，须在 Compatibility 分析中列明。

#### 6. Option Strict / 编译选项分叉

声明层的合并与类型推导基本正交，但脚本场景（Option Strict Off）下的"两个文件贡献"有运行时状态交互：贡献的执行顺序影响最终状态，而两条 Option 路径必须**行为一致**。另外沿用 Immersive Files 会议立下的规则——**每个示例必须能在 `Option Strict On` 下编译**；宽松模式下合法、严格模式下报错的贡献体（晚期绑定成员访问）要在文档里显式说明其归属，而不是默默依赖 `Off`。两条路径的分叉表建议未提供。

#### 7. IDE / IntelliSense 影响

若采纳"贡献者 = 独立方法"模型，IDE 负担是**有界的**：每个贡献独立导航、独立断点、独立重命名，与事件处理器完全一致。真正的新工作集中在**目标端**：目标在 Class View/补全里怎么显示（一个方法？一个集合？）；跳转到目标时是否给出"所有贡献者"清单；合并后成员的签名帮助、InfoTip 文案。这些都有现成先例（事件的目标端显示），`Probably` 可抄。若采纳"拼接体"模型，IDE 负担无界（跨文件断点、重构、折叠），这是又一个拒绝拼接的理由。

#### 8. 数据 / 普遍性

受益者是谁？两群人：**源生成器作者**（小众、技术型）与**声明式编程的终端用户**（Anthony 的 13.x 愿景，尚无形）。主线信号：2017-11-15 判定源生成器是值得调查的战略领域，但量化的痛点是 `INotifyPropertyChanged` 样板，**不是名称冲突成本**——"We need to know the status of source generators and how far out they are before deciding." 之后主线再无跟进。`Suspect`：本建议的痛点（生成器作者避免名称冲突）可能是 Anthony 自己框架（13.x 生成器生态）的**框架内部问题**——"我的生成器框架会生成撞车代码，所以语言要帮我"——而框架作者在库层就能解决。**真实但窄，且无任何数据。** 我们不会拿一张没有示例、没有测量的地图去说服任何人。

#### 9. 更简替代

按可行性排序，全部不需要新语法：

- **事件**：`Handles` + `RaiseEvent` 已给出多源贡献语义。
- **生成器层契约库**：定义"目标 + 贡献"的生成器 API，合并发生在生成文本阶段，确定性由库保证，语言不参与。
- **更好的重复成员诊断**：检测到生成代码之间的重复时，把两个声明、各自的生成器来源都指出来——这是"成本最小、痛点最准"的一步，且是编译器本来就该做的事。
- **Partial 成员元数据合并（13.3，已原则上采纳子集）**：覆盖"补元数据"那部分贡献。
- **`Replaces`（13.2）**：覆盖"唯一赢家"那部分。
- **命名约定 / 命名空间隔离**：今天的默认解法。

其中"诊断"与"生成器层契约"是我们要重点推荐的组合——它把 Anthony 的"各层处理"落到**不需要语言改动**的两层，并把确定性留给生成器层自己负责。

#### 10. 成本 / 优先级

A（语言层）成本横跨文法、绑定、语义、IDE 四张面，且顺序三角未解；在 13.1–13.4 各自未定稿之前，A 没有任何可靠的优先级判断基础。B+C（生成器层 + 元数据层 + 诊断）成本中低，且可在 13.x 家族工作项内作为"生成器协作"的一部分排期。**优先级：家族最低**——它依赖的四个兄弟建议都还没定型，连接件的优先级不可能高于被连接物。

#### 11. 运行时 / CLR 硬约束

两个方向的落点都不触 CLR 红线：若 `Appendable` 编译为**委托字段 + 多播调用**（`Delegate.Combine`/`Invoke`），是既有的运行期模型，PEVerify 无碍，只是需要处理实例 vs 静态目标的注册语义；若编译为**合并方法体**，是纯编译期概念。唯一真正的 CLR 侧问题是：方法形目标若编译为委托，就失去了"方法"的元数据身份（反射、虚分派、接口实现都不同），这属于 Compatibility 分析而非 PEVerify。`Probably` 无硬约束。

#### 12. 值不值得做

逐维打分。**价值**：底层需求（多个来源以声明式方式贡献到同一目标）对 13.x 家族是真实的，但它更多是**家族一致性需求**而非终端功能需求；对终端用户的增值不可演示（零示例）。**成本**：语言层全谱大且顺序三角无解；生成器层/诊断层中低。**风险**：顺序/非确定性、与事件/`Replaces`/`Partial`/shadowing 的关系未定、家族内四个假设互相打架。**结论：作为语言特性现在不值得做；作为家族 speclet 的议题值得记录。** 这不是 "Fantastic idea, and too hard to do"（那是 2018-02-07 对 #211 的判词），而是"**材料不足**"——我们无法对一个没有层、没有模型、没有示例的想法做价值判断，只能给它标上"待家族 speclet 喂养"。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：通过（无语法）；激活红线是"重复声明保持报错 + 合并 opt-in"。
- **保持 VB-like（原则 #2）**：无法评估——无语法。"事件式贡献"若成真，是 VB 血脉；"拼接式"则是反 VB 的。
- **不引入"第二种做事方式"（原则 #3）**：若 A 落地，是**第三种**组合方式（事件之外）；2018-06-13 的高门槛正对它的门。但我们承认：它服务的新场景（生成器协作）主线从未提供过方案，门槛论证与 top-level-code 会议同款。
- **默认跟随 C#（原则 #4）**：C# 无对应物。C# 生成器用 partial + 命名约定解决，不引入语言特性。偏离 C# 需要 "compelling reason"，本建议没有。
- **读起来像英语、对新手友好（原则 #5）**：负分风险——"谁贡献了什么"跨文件分布，整份可读性依赖 IDE 聚合，违反"读起来像英语"的直觉。
- **不为边缘场景加特性（原则 #6）**：生成器作者是窄受众；痛点无数据。`Suspect` 落在边缘区间。
- **避免隐蔽控制流/语义变化（原则 #7）**：**顺序三角是教科书级违反**——贡献的执行顺序若依赖生成器顺序或未指定，就是隐蔽的非确定性。拼接体模型还自带"相同源码、不同合并"的风险。这是本建议与原则 #7 最大的冲突面。
- **消除常见样板（原则 #9）**：它消除的是**生成器作者**的样板，不是终端用户的样板——射程外。终端用户反而要承受"目标从哪来"的抽象负担。
- **冗长只在有用时是美德（原则 #10）**：不适用。
- **与主线关系（对照表 2.3）**：`Appendable`/重复声明处理在主线对照表**无对应行**，属 **Anthony 独立延伸**（"生成器协作"家族）。与主线最近触点是 2017-11-15 的"生成器优先"战略——主线把 `INotifyPropertyChanged` 交给生成器（poster child），本建议却在提议**语言层介入**生成器协作，方向与主线战略有张力；落回生成器层则无张力。2014-02-17 LDM #20 批准 partial module/interface 时写过 "Partial methods inside will be allowed; they will just drop out. We can't think of any design gotchas."——十年后 13.3 会议已经发现了 gotchas，本建议是又一处把"合并/重复"往深处推的地方，与那条乐观记录反向。

### RESOLUTION:

We came into the room expecting to argue about a feature; we leave having argued about a map. The findings, in order:

1. **保持 inactive（Table），不激活全貌。** 理由叠加：① 无任何一层、任何模型的落地设计，材料不足；② 顺序三角无解（不指定顺序 = 非确定性，指定 = 违背 Anthony 的负约束，要求顺序无关 = 限制副作用）；③ 与 `Event`/`Replaces`/`Partial` 的关系未定义，家族内四个假设互相打架；④ 受益者（生成器作者）窄且能在库层自解；⑤ 无数据、无示例、无原型。
2. **"贡献者模型"是唯一不矛盾的读法。** `Appendable` 若有一天存在，它必须是**贡献目标**（每个来源贡献完整、独立的处理器），**绝不可能是拼接体**。拼接 = 18.17 第一句否决的东西。此条记录为方向性裁定，任何未来的复活提案都必须从它出发。
3. **顺序三角必须先裁决。** 任何复活提案必须给出"贡献是否允许顺序敏感的副作用"的明确回答，以及"生成器执行顺序是否影响合并结果"的确定性证明。**绝不接受顺序未指定的设计。**
4. **语言层语法（A）= Table，且绑定家族 speclet 先行。** 在 13.1–13.4 的"同一目标的声明关系"模型统一之前，A 无优先级的判断基础。生成器层契约 + 诊断（B+C）= **Consider**，可在"生成器协作"工作项内排期。
5. **重复声明保持编译错误。** 合并必须 opt-in（新语法/新标记）；任何"把既有错误重解释为合法"的设计都是破坏性变更，直接 Reject。
6. **`Sub`-only 倾向。** 若贡献模型落地，返回值/`Exit Sub`/异常的语义我们倾向沿用事件（多播委托）既有语义：`Sub`-only，异常中断调用链，`Exit Sub` 只退出当前贡献。此条为倾向，非定案。
7. **与 Immersive Files 对表。** "两个脚本文件不能背靠背跑"的担忧已被 top-level-code 会议"单入口文件"裁定部分回应；本建议若允许"多个脚本文件贡献可执行语句"会破坏该裁定，若只允许"贡献声明/处理器"则一致。两份建议必须交叉引用。
8. **"File" accessibility 的去留必须说明。** 18.17 标题的 1/3 被静默丢弃，不可接受；要么并入 Immersive Files 讨论，要么单独成议，要么明确 Reject 并说明理由。

**激活所需信号**（任何未来复活必须携带）：

- (a) **至少一层的具体设计**，优先生成器层契约 + 诊断（可编译示例，含"目标 + 两个贡献者"的最小演示）；
- (b) **量化的成本证据**——真实源生成器作者在名称冲突/去重上的时间或性能损耗测量（我们的 `Suspect` 是"真实但可能很小"）；
- (c) **顺序三角的解决方案**——贡献模型明确"顺序无关 or 显式顺序"，并证明生成器执行顺序不影响合并；
- (d) **家族模型统一**——13.1–13.4 就"同一目标的声明关系"给出一个一致模型，本建议作为其中"多贡献"一格的实现；
- (e) **一个具体场景**（如 Smart Attributes + 两个生成器）证明事件/partial/`Replaces`/生成器库四者确实表达不了。

### Implication:

- 将本建议状态标注更新为 **LDM Considering（Table）**；方向性裁定（贡献者模型 > 拼接模型；语言层 Table、生成器层/诊断层 Consider）记入建议文档头部。
- 委派**家族 speclet**：由"生成器协作"工作项（13.1–13.4 + 本建议）统一"同一目标的声明关系"模型；speclet 未出之前，本建议的 A 不进入任何排期。
- 将"重复成员诊断增强"（指向两个声明及其生成器来源）作为编译器侧的最小增量列 TODO，`Probably` 单独可做、无需家族 speclet。
- 与 top-level-code 会议交叉引用"单入口文件"裁定；把"两个脚本文件背靠背"担忧的归属写入 Immersive Files 的 OPEN QUESTIONS。
- "File" accessibility 开一个独立 OPEN QUESTION，交 Immersive Files 与 `module-enhancements` 团队认领或否决。
- 未决问题移交至下方 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：顺序三角——贡献是否允许顺序敏感的副作用？若允许，顺序如何表达而不违背"不思考顺序"？
- `OPEN QUESTIONS`：`Appendable` 相比 `Event` 的增量到底够不够一个新语法？需一张对照表（`RaiseEvent` 缺省、`Sub`-only、`Custom Event`、`WithEvents`）。
- `OPEN QUESTIONS`：返回值语义——`Function` 形贡献哪个值赢？我们倾向 `Sub`-only，未定案。
- `OPEN QUESTIONS`：语义模型返回单符号还是多符号；目标符号是合成方法、委托字段还是伪事件。
- `OPEN QUESTIONS`：生成器执行顺序是否影响合并结果——B 层的确定性证明。
- `OPEN QUESTIONS`：18.17 标题的 "File" accessibility 的去留。
- `OPEN QUESTIONS`：`Appendable` ∩ `Replaces` ∩ `Partial` 的优先级/冲突模型。
- `TODO`：生成器作者名称冲突成本的量化测量（激活信号 b 的证据来源）。
- `TODO`：编译器重复成员诊断增强的最小原型。
- `Follow-up`：复查 2017-11-15 之后主线对源生成器的后续决定；若 C# 生成器生态的成熟让"名称冲突"自然消解，本建议的动机随之减弱。
- `Follow-up`：与 13.1（Smart Attributes）会议对表——注入场景是否就是"贡献者模型"的触发场景。

### 状态

- **LDM 状态：建议整体 LDM Considering（Table）**；方向性裁定：贡献者模型成立、拼接模型 Reject；语言层语法 Table（绑定家族 speclet）；生成器层契约 + 诊断 Consider。
- **三态判定：Table（保持 inactive）** — 材料不足、顺序三角无解、家族不一致、受益者窄且可在库层自解。值得记住（作为 13.x 家族 speclet 的议题），不值得现在做。与目标类型化会议同判，但理由不同：那边是"方案已被更简替代覆盖"，这边是"根本没有方案"。

---

## 附录：特性评价

# 建议评价报告：proposal-duplicate-declarations.md

## 评价对象

- 建议：proposal-duplicate-declarations.md — 重复声明处理与 `Appendable` 方法（"源生成器版多播委托"类比，处理声明式编程下的名称冲突与重复定义）
- 来源：Anthony 原文第 18 章 18.17 "Handling duplicate declarations, 'File' accessibility, 'Appendable' methods?"（`..\..\AnthonyDesign_wordpress.txt` L3058–3066，仅两段话、零代码示例）
- 配方目标：让多个来源以"追加"（Appendable）方式共同贡献到同一目标，避免生成器作者人工处理拼接与顺序；不拼接、不背靠背、不要求终端开发者思考顺序

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。痛点（生成器作者名称冲突/去重成本）真实但未量化；**零示例、零语法、零原型**，改进完全不可演示；"各层处理"是哪几层未说明 | 已提供/已检查（状态行 Prototype/Implementation/Specification 均为占位链接，无运行证据） | 核心语法未定型 = 效果未显现（未决问题 ≥4 个关键设计点，效果封顶）；无任何一层落地 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。标题捆绑"重复声明处理"+"Appendable"+"File accessibility"（后者被静默丢弃）；"多播委托类比"继承 .NET 委托模型但未 VB 化、未与既有 `Event`/`Handles` 模型对照；无任何 VB 语法形态 | 已检查 | 唯一不矛盾的读法（贡献者模型）恰与 `Event` 同构，增量未论证；拼接读法与 18.17 自身矛盾 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节结构完整、诚实不虚构语法（`Suspect` 为加分项，避免品质最重违规）、4 个未决问题具体诚实；但 Detailed design 明确"无设计"（边界全部含糊）、静默丢弃 18.17 标题 1/3、状态行占位链接 | 已检查 | 无文法、无 Compatibility、无示例；"Appendable 确切语义"未决且波及核心设计；未引用 13.1–13.4 家族任何一份建议的决议 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/火潜在正向（声明式家族连接件、服务脚本/生成器生态）；风/暗明显受损——顺序/非确定性、与事件/`Replaces`/`Partial`/shadowing 的冲突面未识别、家族内四个假设打架、与主线"生成器优先"战略有张力 | 已检查（预测性结论，标"待定"） | 顺序三角无对冲设计；"合并是否改变重复声明错误行为"的破坏面未分析；无应对方案 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源标注准确（Anthony 18.17，标注了章节，无虚构材料）；但**未标注**关键血缘——"多播委托"继承 .NET 委托模型、VB 既有 `Event`/`Handles` 机制是最近的血亲却未点明；"各层处理"未枚举；与 13.1–13.4 家族（同作者、同主题）未交叉引用；"File accessibility" 丢弃未说明 | 已检查 | 无杂质（诚实是亮点）；但成分间依赖关系与先例继承不透明 |

## 设计原则对照

- **与 VB 基因：偏离为主（方向未定）。** 无语法无法谈 #2（保持 VB-like）；"贡献者模型"若成真则较 VB（与事件同族），"拼接模型"则反 VB；#7（隐蔽控制流/语义变化）是最大冲突面——顺序三角的未指定顺序 = 隐蔽非确定性；#9（消除样板）只服务生成器作者，不服务终端用户，射程外；#3（第二种/第三种做事方式）若走语言层则撞高门槛。
- **与主线关系：Anthony 独立延伸（"生成器协作"家族）。** 主线对照表无对应行；与主线"生成器优先"战略（2017-11-15：`INotifyPropertyChanged` = poster child，`Replaces` w/ generators 覆盖多数）在语言层方向上有张力，在生成器/诊断层无张力；与 2014-02-17 #20 "Partial methods ... will just drop out. We can't think of any design gotchas." 的乐观记录反向；与 2014-10-08 shadowing 反复史同属"同名多声明"敏感区。
- **破坏性变更：无（当前）**。无语法即无破坏；但激活红线必须写死——重复成员声明保持编译错误、合并 opt-in（新语法/新标记），否则会产生 2015-01-14 同族"重编译换行为"风险。建议未做任何兼容性分析。

## 总评

- **达成程度：未达成（作为可落地特性）/ 部分达成（作为记录在案的实验方向）。** 建议诚实记录原文要点、不虚构语法，作为 inactive 存档合格；作为语言特性，效果、特性、属性三维均不达标，且没有任何一层的设计可供评估。
- **LDM 三态建议：Table（保持 inactive）**。语言层语法 Table（绑定"家族 speclet 先行"信号）；生成器层契约 + 诊断 Consider（可在"生成器协作"工作项内排期）。面向 VBScript.NET 的优先级同为 **Table**——脚本/生成器生态需要的是"多源贡献"的确定语义，而本建议既未给语义、也未给示例，优先级低于 13.1–13.4 任何一份。
- **主要问题**：① 无任何一层、任何模型的落地设计（"地图"不是"提案"）；② 顺序三角无解——不指定顺序 = 非确定性、显式顺序 = 违背负约束、要求顺序无关 = 限制副作用；③ 与 `Event`/`Replaces`/`Partial` 的关系未定义，家族内四个假设互相打架；④ "Appendable" 与"不拼接"的唯一不矛盾读法（贡献者模型）恰与事件同构，增量未论证；⑤ "File accessibility" 被静默丢弃；⑥ 无数据、无示例、无原型。

## 返工建议

- **补充章节**：至少一层的具体设计（优先生成器层契约 + 重复成员诊断增强，附"目标 + 两个贡献者"的可编译最小示例）；贡献模型的语义（注册、合并、顺序、异常、返回值、`Exit Sub`）；与 `Event`/`Handles`、`Replaces`（13.2）、`Partial`（13.3）、`##If`（13.4）的交互矩阵；Compatibility/breaking-change（重复声明保持报错、反射身份、重编译敏感性）。
- **补充证据**：真实源生成器作者的名称冲突/去重成本测量；生成器层契约的最小原型（含确定性证明：生成器执行顺序不影响合并）；`Sub`-only 与返回值语义的对照试验。
- **未决问题处理**：逐条给出采纳标准——顺序三角先裁决（倾向"要求顺序无关或显式顺序，绝不未指定"）；`Appendable` vs `Event` 增量对照表；语义模型单/多符号选择；"File accessibility" 去留；`Appendable` ∩ `Replaces` ∩ `Partial` 优先级模型。
- **设计探索**：把本建议重新表述为 13.x 家族 speclet 的"多贡献"一格，而非独立语言特性；与 top-level-code 会议"单入口文件"裁定对齐，明确"多个脚本文件贡献声明/处理器"与"仅一个入口文件可执行"的兼容边界；跟踪 C# 生成器生态成熟是否自然消解动机。

---

## 附录：C# 生态与互操作考量

> 本附录由 ModVB 评估项目 meeting agent 追加，对照 dotnet/csharplang 官方镜像（`..\..\..\csharplang`）逐字核实。主题定位：本提案不是指针/内存互操作议题，而是「类型系统与源生成器协作」议题——与 C# 的 `partial` 成员族、C# 11 `file`-local types、「生成器优先」战略直接相关。C# 侧引用均标来源；无法在本仓库正文核实的标 **Suspect** / 列入 **OPEN QUESTIONS**。

### 相关 C# 现实方向

**方向一：`partial` 是 C# 处理「跨文件同名声明」的唯一语言机制，且正被系统化扩展为生成器契约。**

- C# 自 2.0 起用 `partial` 修饰符把同一类型拆到多文件（spec 入口 `spec/classes.md` → ECMA-334 §14.2.7 Partial declarations）。
- C# 9 `extending-partial-methods` 放开 partial 方法签名限制，动机明确指向源生成器：
  > "This proposal aims to remove all restrictions around the signatures of `partial` methods in C#. The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods."
  → `proposals\csharp-9.0\extending-partial-methods.md`（Summary）

  同一份文件记录 partial 方法的「擦除」语义——定义缺失时语言整体擦除调用点，这是「零贡献是否合法」问题的 C# 答案：
  > "One behavior of `partial` methods is that when the definition is absent then the language will simply erase any calls to the `partial` method."
  → `proposals\csharp-9.0\extending-partial-methods.md`

- C# 13 `partial-properties`、C# 14 `partial-events-and-constructors` 提案把 partial 扩到属性/索引器/事件/构造器；核心不变式是**恰好一份 defining + 一份 implementing**，且只有 defining 参与查找与元数据发射：
  > "A partial property must have one *defining declaration* and one *implementing declaration*."
  → `proposals\csharp-13.0\partial-properties.md`

  > "Allow the `partial` modifier on events and constructors to separate declaration and implementation parts, similar to partial methods and partial properties/indexers."
  → `proposals\csharp-14.0\partial-events-and-constructors.md`（Summary）

- C# 15/16 候选 `partial-extension-members` 继续把 partial 扩到 extension members（生成器产出实现、用户写定义），方向是**深化 partial 契约**：
  > "It is possible to have a source generator produce the implementation of a partial classic extension method:"
  → `proposals\partial-extension-members.md`（Motivation）

- 演进节奏谨慎：2022 LDM 曾推迟 partial events（后由 C# 14 提案接续）：
  > "It does mean that we have only one non-field member type that can't be partial now: `event`s. We think extending the feature to events goes a bit farther than we'd like for now."
  → `meetings\2022\LDM-2022-11-02.md`（Partial Properties）

**方向二：源生成器「名字冲突」的 C# 官方解法是 `file`-local types（C# 11）——即 18.17 标题被静默丢弃的第 3 项 "File accessibility" 的现成参照系。**

> "Our primary motivation is from source generators. Source generators work by adding files to the user's compilation.
> 1. Those files should be able to contain implementation details which are hidden from the rest of the compilation, yet are usable throughout the file they are declared in.
> 2. We want to reduce the need for generators to "search" for type names which won't collide with declarations in user code or code from other generators."
→ `proposals\csharp-11.0\file-local-types.md`（Motivation）

「不同文件同名 = 运行时不同符号」由编译器生成 unspeakable 元数据名保证：

> "The implementation guarantees that file-local types in different files with the same name will be distinct to the runtime. The type's accessibility and name in metadata is implementation-defined."
→ `proposals\csharp-11.0\file-local-types.md`（Naming）

**方向三：C# 对重名声明的严格性从未放宽——同一作用域内重名 = 编译错误，`partial` 是唯一的 opt-in 例外。**

- C# 从不把「同一作用域两个同名成员」重解释为合法；`partial` 之所以合法，是因为它**显式声明「这两段是同一个成员的多个部分」**，最终在元数据里坍缩为一个符号。C# 侧同族错误为 CS0101/CS0111（Roslyn 诊断，编号不在本仓库正文，`Suspect`）。
- CLR 元数据硬约束：类型内成员身份 = name + signature；同名同签名的两个 MethodDef 在同一类型中不能共存（ECMA-335；本镜像无 ECMA 正文，`Suspect`）。file-local types 的 Naming 段正是 C# 对这一约束的承认——源码层允许「同名不同文件」，但运行时层必须 distinct（经 unspeakable 名）。

**方向四：C# 的「多源贡献」= 事件（多播委托），顺序 = 注册顺序；没有 `Appendable`。**

- C# 没有方法级「多实现体并存」。多个处理器贡献到同一目标用 `event`（多播委托：`+=`/`AddHandler` 注册、按注册顺序调用、异常中断调用链）。这正是本会议 RESOLUTION #2「贡献者模型 = 事件」的 C# 对应物。
- 顺序三角在 C# 的答案与 VB 事件一致：顺序是**显式注册顺序**，不是 source 顺序，更不是「顺序无关」。「多贡献且不指定顺序」在 C# 与 VB 同样是禁区。

**方向五：生成器优先战略——C# 用「partial 契约 + file-local 类型 + 生成器 API」解决生成器协作，未引入语言级 Appendable。**

### 现实 vs 提案

| 维度 | C# 现实 | 本提案 | 判定 |
|---|---|---|---|
| 跨文件同名成员 | `partial`：一份 defining + 一份 implementing，元数据坍缩为一符号 | 无语法；「贡献者模型」允许多个独立 handler | **需桥接**：VB 若支持 partial 成员（13.3）应直接对齐 C# 的「恰一份 defining/implementing」与签名匹配规则（`proposals\csharp-13.0\partial-properties.md` §Matching signatures） |
| 多实现体并存 | 不存在；CLR 元数据 name+signature 唯一性禁止 | `Appendable` 若读作「多 body」即被双重禁止 | **冲突**：与 C# partial、与 CLR 元数据均正面冲突；`Appendable` 只能读作「贡献目标」→ 即事件 |
| 生成器名字冲突 | `file`-local types（C# 11）已落地 | 痛点真实但未量化 | **兼容 / 已被覆盖**：C# 在类型层已解决大半，本提案动机相应减弱 |
| 多源贡献 / 多播 | `event`（多播委托，注册顺序） | 贡献者模型 = 事件 | **兼容**：VB 的 `Event`/`Handles` 即答案，无新机制 |
| 重复声明严格性 | 同作用域重名 = 错误；合并仅经 `partial` opt-in | RESOLUTION #5：保持报错、合并 opt-in | **兼容**：两语言互证 |
| "File accessibility" | `file`-local types（C# 11） | 被静默丢弃 | **需桥接**：18.17 第 3 项有现成 C# 参照系，应并入 Immersive Files 讨论 |
| 语言层新语法 | 无 Appendable；2022 LDM 甚至推迟 partial events | A（语言层）= Table | **脱节**：C# 用非语言机制覆盖全部动机，未留语言层空间 |

**总结判定**：本提案与 C# 的关系不是「指针/互操作」式的硬约束关系，而是「类型系统 + 生成器协作」的**同向但已超前**关系。C# 现实方向**强化**了本会议的 Table 判定：C# 用三个非语言特性机制（partial 成员契约、file-local types、事件）+ 诊断，覆盖了本提案想覆盖的全部场景；「Appendable」语言特性在 C# 侧没有任何对应物，也不被 CLR 元数据允许。此关系**不算弱**，但也不是 interop 红线，而是「生成器协作模型」的参照系。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：脚本层的「多源贡献」沿用 VB 既有的 `Event`/`Handles`（多播委托），与 C# `event` 元数据互通，不引入 `Appendable`。
2. **source-gen 桥**：采用 C# 的 partial 契约作为 .vbx 生成器协作的默认形态——用户写 defining declaration（含属性），生成器补 implementing；属性合并、caller-info 忽略、签名匹配规则直接引用 C# 13/14 已定规范（`proposals\csharp-13.0\partial-properties.md` §Matching signatures、§Attribute merging）。这正好落在本会议 RESOLUTION #4 的 B+C = Consider 区间。
3. **识别新元数据**：VB 编译器必须正确识别 C# 生成的 partial 成员形态（defining/implementing 分置、属性合并产物）与 file-local types 的 unspeakable 生成名——不得对 C# 生成的 `file` 类型误报「重复命名」，应视其为文件内可见的 internal 类型。
4. **诊断对齐**：重复成员诊断增强（指向两个声明 + 各自的生成器来源）与 C# 侧诊断方向一致，是「成本最小、痛点最准」的第一步。
5. **"File accessibility"**：18.17 标题第 3 项建议以 C# 11 file-local types 为参照系并入 Immersive Files 讨论（见下方 OPEN QUESTION 更新）。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #1（保持 inactive / Table）——被 C# 现实强化。** C# 侧从未出现任何同类语言特性；2022 LDM 对 partial 扩展的节奏是「推迟、谨慎」，且用非语言机制覆盖了动机。三态判定维持 **Table**。
- **RESOLUTION #2（贡献者模型 > 拼接模型）——与 C# 一致。** C#「多源贡献」的唯一形态就是事件（多播委托）；「拼接」在 C# 也从未被允许（CLR 元数据禁止同名同签名多 body）。
- **RESOLUTION #4（A = Table，B+C = Consider）——与 C# 生成器优先战略同向。** C# file-local types 可作为 B 层「命名冲突」痛点的**已实现例证**，供本提案激活信号 (a) 参考。
- **RESOLUTION #5（重复声明保持报错）——与 C# 严格性一致，互证。**
- **OPEN QUESTION #6（"File" accessibility 去留）——新增 C# 参照系。** C# 11 `file`-local types 已给出「文件内可见 + unspeakable 元数据名」的完整答案；本 OPEN QUESTION 应改为「对齐 C# 11 file-local types，或明确 Reject 并说明理由」。

### OPEN QUESTIONS / 引用核实状态

- `OPEN QUESTIONS`：CLR 元数据对「同名同签名 MethodDef」的精确禁止条款位于 ECMA-335（不在 csharplang 镜像内），本附录以 file-local-types Naming 段作为间接证据；如需逐字引用 ECMA-335 原文，需另行核实。
- `Suspect`：C# 重复成员错误号 CS0101/CS0111 属 Roslyn 诊断，不在 csharplang 仓库正文；编号准确性待核实。
- `Follow-up`：跟踪 C# 15/16 `partial-extension-members` 与 partial 修饰符排序放宽（`proposals\relaxed-partial-ref-ordering.md`）的最终落地——两者共同表明 C# 的方向是**深化 partial 契约**，而非引入「多 body」形态。
