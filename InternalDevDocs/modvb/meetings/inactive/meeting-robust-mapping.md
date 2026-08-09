# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天审查的是一份 **inactive** 建议——`proposal-robust-mapping.md`（更健壮的映射，Anthony 第 18.7 节实验性想法）。第 18 章是 Anthony 明确标注"还需要时间酝酿"的一批想法，我们的任务不是背书它，而是认真决定：**它值得激活，还是保持搁置？**

这份建议恰好站在我们过去几场会议的交叉点上：`meeting-json-literals.md` 把 `&=` writer 形态裁给了本建议；`meeting-shapeof-pattern-matching.md` 与 `meeting-json-pattern-matching.md` 把 `ShapeOf ... Is { ... }` 操作符形态 Table/Reject；`meeting-null-literal.md` 维持 2014 年对 `Null` 字面量的拒绝；`meeting-ignore-warning-directive.md` 把"如何定向静音警告"指向主线 2014 年已拍板的 `#Disable Warning`。换句话说，本建议的两半各自踩在我们已经开过的会的地皮上——今天的工作是把这些线头收拢。

_诚实分层说明：本纪要逐句标注 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。所有引用的 vblang 主线会议决定、引文与 issue 编号均逐字核对自 `..\..\..\vblang\meetings/` 原始文件；Anthony 原文引文核对自 `..\..\AnthonyDesign_wordpress.txt`。找不到直接对应材料处，依据 vblang 设计原则与评价标准独立论证并显式标注。我们记录 rationale，以便日后回来看到我们为什么这样走。_

## Agenda

* [ModVB Proposal — 更健壮的映射（More Robust Mapping）](#modvb-proposal--更健壮的映射more-robust-mapping)

## ModVB Proposal — 更健壮的映射（More Robust Mapping）

_来源：Anthony D. Green 原文第 18.7 节 "More robust mapping"（`..\..\AnthonyDesign_wordpress.txt` L2906–2942，inactive / 实验性，无散文，只有两个问题 + 两段代码）。Related: [vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；[vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；主线会议：2017.10.18（JSON Literals）、2017.08.23（Late-bound Member-Access）、2014-07-01（#Disable Warning）、2018.12.19（Pattern Matching）；ModVB：`proposal-json-literals.md`、`proposal-json-pattern-matching.md`、`proposal-null-literal.md`、`proposal-shapeof-pattern-matching.md`、`proposal-ignore-warning-directive.md`_

### 场景与缺口

Anthony 在 18.7 里只问了两个问题，没有任何散文：

> Should there be some "natural join" syntax to map the members of one object to another?
> Should there be a way to indicate that an unmapped member is intentional?

即：是否应有某种"自然连接"语法把一个对象的成员映射到另一个对象？是否应有办法标明"某个未映射的成员是有意的"？后者针对的是编译器可能发出的"你忘了序列化某个属性"的警告——警告不该被整个关闭，而应能被定向地静音。建议用两段代码给出了它的答案（Anthony 原文逐字）：

```vb
' If there's a warning that you forgot to serialize a property,
' how do you silence it without turning off the warning entirely?
jsonWriter &= {
                "firstName": person.Name.Given,
                "lastName" : person.Name.Surname,
                null       : person.EmailAddress
              }

' Same question with deserializing.
' Pattern matching into existing variables/properties is
' still an open question, though.
Let address = New Address
If ShapeOf json IsNot {
     "line1": address.Street
     "line2": null,
     "city" : address.City
     "state": address.State
   }
Then
    Throw New DeserializationFailureException()
End If

Return address
```

这段代码实际含三件互不相关的事，我们决定拆开看：

1. **`jsonWriter &= { ... }`**：以对象字面量形式向 writer 追加序列化内容，`&=` 重载为"写片段"，不物化中间对象。这一形态在 `meeting-json-literals.md` 已被裁给本建议——它不是 JSON 字面量，而是"序列化 DSL"。
2. **`null : person.EmailAddress`（序列化半场）**：用 `null` 作键，表示"把 `person.EmailAddress` 显式排除在映射之外"，用于声明"我不打算序列化这个成员"。
3. **`If ShapeOf json IsNot { ... }`（反序列化半场）**：对 JSON 做形状模式匹配，把各字段匹配进**已存在的**变量/属性（如 `"line1": address.Street`），任一段不匹配则整体为 `IsNot` 成立，于是抛异常；`"line2": null` 表示该字段"有意不反序列化到任何成员"。

We started from the proposal's own framing，但立即发现它不是一张白纸：

- **序列化的"写"半场有真实需求，但"读"半场主线已判"被 `!` 蒸发"。** 主线 2017.08.23 在讨论 late-bound member-access 时原话说过："The JSON thing is great. Oh, but it already works with ! so all the value just evaporated." 主线还承认 JSON 一等支持的价值："JSON is the _lingua franca_ of the cloud. First-class JSON support could be a strong attractant for first-time developers."（2017.10.18，逐字）。今天缺的只有"构造/写"半场，而这半场正是 `jsonWriter &=` 想做的。
- **"定向静音警告"的通用机制，主线 2014 年已拍板。** 2014-07-01 的 `#Disable Warning` 决定就是"不把警告整个关掉、而是定向抑制"的答案；同场还明确拒绝了"下一行抑制"与 `#Ignore` 块式（`meeting-ignore-warning-directive.md` 已详述）。本建议想在**映射 DSL 内部**再造一个定向静音机制。
- **反序列化半场依赖的 `ShapeOf ... Is { ... }`，正是我们已 Table/Reject 的操作符形态。** `meeting-shapeof-pattern-matching.md` 与 `meeting-json-pattern-matching.md` 已把"JSON 形状模式"划给模式匹配家族 Phase 2，把 `ShapeOf ... Is` 操作符形态判为 Table/Reject（改用家族载体 `Case { ... }` / `Matches`）。而"模式匹配进已有变量/属性"在主线 2018.12.19 讨论中连"变量引入"都还没收口。

还有一个我们必须当场指出的结构性观察：**建议的第二个问题（"未映射成员是有意的"）只在第一个问题（自然连接映射）存在时才成立。** 今天的 VB 没有"自然连接"映射，也就没有"你忘了序列化某属性"的警告——本建议是在为一座还没建起来的房子设计消防通道。Q2 是 Q1 的附属品，而 Q1 本身从未被设计过（建议既没有给出"自然连接"的文法，也没有回答"自动按同名映射"的规则，它实际设计的是显式逐字段映射 + 一个排除标记）。

### 候选方案

**PROPOSAL A — 整体采纳（Anthony 18.7 原样）。** 自然连接对象字面量 + `null:` 排除键 + `&=` writer + `ShapeOf json IsNot { ... }` 校验，一次全给。范围最大、依赖的未定稿特性最多。

**PROPOSAL B — 只做序列化半场。** `jsonWriter &= { ... }` + `null:` 键作为"有意排除"标记，不碰 shape 校验。把 `&=` writer 形态从 json-literals 接收过来，独立定稿。

**PROPOSAL C — 只做反序列化半场。** `ShapeOf json IsNot { ... }` 校验，作为模式匹配家族 Phase 2 的一个客户端形态，用家族载体（`Case { ... }` / `Matches`）重写，不走独立操作符。

**PROPOSAL D — 用既有机制拼装，零新语法。** 序列化的"有意排除"用 `System.Text.Json` 已有的 `JsonIgnore` 特性（或 `JsonSerializerOptions` 的 `DefaultIgnoreCondition`）；"忘了序列化"警告若真要发，用分析器 + `#Disable Warning` / `#Enable Warning`（2014 已定）定向静音；反序列化校验用 `JsonSerializer.Deserialize(Of T)` + 手动 `If` 校验或家族 Phase 2 的形状模式（`Matches`）。本建议的两个动机都被更简机制覆盖。

**PROPOSAL E — 什么都不做，维持 inactive。** 承认 18.7 是"等待信号"的实验性想法，把可消化的部分（`&=` writer 的去向、pattern-into-existing-variables 的缺口）登记为待办，不进入语言面。

### 权衡：Q&A

- **`null:` 键真的是 JSON 吗？** 不是。JSON 的键必须是字符串，`null` 作键连 JSON 都不合法——`null : person.EmailAddress` 只有在 VB 里把 `null` 当**裸标识符**解析时才有意义（`null` 不是 VB 关键字，这里它是"恰好叫 null 的标识符作键"）。也就是说，这个形态是**魔法标识符**，不是 JSON 语义。我们刚从 `meeting-null-literal.md` 走出来——那里刚因为"把 `Null` 变成上下文魔法"而维持 2014 年拒绝。此处 `null` 作键是同类魔法，且更隐蔽：它在值位置与键位置有两个含义。
- **`null` 在建议内部就自相矛盾。** 序列化示例里 `null : person.EmailAddress` 中 `null` 是**键**，意思是"排除该成员"；反序列化示例里 `"line2": null` 中 `null` 是**值**，意思是"该字段不绑定到任何成员"。同一个 token，两种"不映射"含义，还要和 JSON 真 `null`（值语义）与 VB `Nothing`（默认值语义）区分。四个概念挤在一个拼写上——We think 这是本建议最重的一处设计缺陷。
- **"定向静音警告"主线有没有现成答案？** 有。2014-07-01 的 `#Disable Warning`/`#Enable Warning` 就是"不整个关闭、定向抑制"的机制，其决策文本我们逐字引在本节下一段。主线还留了一句重要的传统："The tradition in VB and its rich history of quick-fixes is that you resolve warnings by FIXING YOUR CODE"（2014-07-01，逐字）。把"这个成员我故意不序列化"表达为**数据**（`JsonIgnore` 特性）比表达为**语法**（`null:` 键）更符合这条传统，也更可被工具理解。
- **"自然连接"到底设计了吗？** 没有。建议的第一个问题问的是"按同名自动映射"，但示例展示的是显式逐字段映射。若真做自动同名映射，属性名大小写策略、嵌套成员、缺失/多余成员、多态、`null` 处理，全要定义——`meeting-json-literals.md` 已把这份"mapping spec"列为目标类型化 POCO 复活的前置条件。建议没有回答任何一个。
- **反序列化半场能不能直接并入家族 Phase 2？** 能，但建议的载体不行。`If ShapeOf json IsNot { ... }` 正是家族决议已 Reject 的独立操作符形态（2018.12.19："We think `Is` will have ambiguity issues with the existing use for reference equality."），且依赖"模式匹配进已有变量/属性"这个连主线都未收口的前置。**它应该降级为家族 Phase 2 的一个需求样例，而不是一份独立建议。**
- **`&=` writer 形态值得单独救吗？** `meeting-json-literals.md` 已把 `&=` 裁到这里，理由是"它是序列化 DSL，不是 JSON 字面量"。但 `&=` 今天在 VB 里是字符串/字符数组连接赋值——把它重载成"写给 writer"是原则 #7（避免隐蔽语义变化）的正面教材。且 `Utf8JsonWriter` 是 `ref struct`，`&=` 两侧的对象类型与调度机制在建议里完全未定义。We 不认为"流式写 JSON 不物化"这个卖点值得污染 `&=`。

### 深度追问：LDM 拷问清单

我们按评价标准第五部分逐条过。被否定的思路也留档。

#### 1. 语法 / 文法歧义

三个独立冲突面：

- **`{ ... }` 的读法爆炸。** `meeting-json-literals.md` 已列出 `{` 的五种既有/候选含义（数组字面量、`With` 成员初始化器、`From` 集合初始化器、JSON 对象字面量、schema 标注类型）。本建议再给 `{ ... }` 加"映射 DSL 的书写载体"——第六种，而且它的冒号内容与 JSON 对象字面量几乎无法从文法上区分：`{ "firstName": person.Name.Given, null: person.EmailAddress }` 与 JSON 字面量唯一的文法差异是键可以不是字符串字面量。
- **`null` 键的解析。** `null : expr` 中 `null` 是裸标识符。今天 `{ "a": 1 }` 因冒号非法而解析失败，`null : expr` 同理解析失败——所以语法上这是**增量**（不破坏旧代码），但绑定上必须定义"键位置出现裸 `null` 标识符"的特殊含义。这就是 `meeting-null-literal.md` 拒绝的"上下文魔法"的变体：一个未绑定 simple name 变成魔法。
- **`ShapeOf json IsNot { ... }` 的 `Is`。** 2018.12.19 已明确担心过：`Is` 右侧接模式与既有引用相等语义的歧义。此形态我们已在家族会议判 Reject，这里不重开。

#### 2. 角案例与边界语义

- **部分匹配的部分写入（反序列化半场）。** `If ShapeOf json IsNot { "line1": address.Street, ... }` 若前两个字段匹配、第三个不匹配，`address.Street`、`address.City` 已经写了，`address.State` 没写——校验失败后对象处于**半初始化**状态。建议抛出 `DeserializationFailureException` 但返回的 `address` 是脏的。`OPEN QUESTION`：失败时回滚？就地校验两次？还是先校验后写入？这依赖"匹配进既有变量"的副作用时机，建议未答。
- **`"line2": null` 的语义。** 是"该字段存在且值为 JSON null，但我不绑定它"还是"该字段不应出现在输入里"？若输入根本没有 `line2`，`"line2": null` 算匹配吗？JSON 模式匹配里 `null` vs `Nothing` vs 缺失是三回事（`meeting-json-pattern-matching.md` 的 OPEN QUESTION 原样继承）。
- **序列化半场的嵌套。** `null : person.EmailAddress` 只排除顶层成员。`person.Address.City` 中途某个成员不想序列化怎么办？`null` 键无法表达"深到这一层"。若 `person` 整个是 `null` 呢？
- **重复键与求值顺序。** 若同一键出现两次（一次真映射、一次 `null` 排除），谁赢？值表达式何时求值？建议未写。`Probably`：构造点一次性求值（与 `meeting-json-literals.md` 对 JSON 字面量的结论一致），但排除键的值表达式（`person.EmailAddress`）求值后丢弃——**为丢弃而求值**，若表达式有副作用（`person.ExpireToken()`）则诡异。

#### 3. 作用域与绑定

- **"匹配进既有变量/属性"是整个反序列化半场的地基，而它未定义。** Anthony 自己原注："Pattern matching into existing variables/properties is still an open question, though." 主线 2018.12.19 在讨论模式变量引入时都还没收口："It's not clear how variable introduction without typechecking would work, or how type checking without assignment differs from the available `TypeOf x Is <type>`."（逐字），并质疑"variables be used before they are declared"会很怪。**向既有变量写入**比"引入新变量"更进一步：赋值语义、部分写入（见追问 2）、属性 vs backing field 的绑定目标，全部未定。
- **语义模型返回什么？** 序列化半场里，`{ ... }` 若绑定为 writer 的 DSL，键不绑定任何符号，`jsonWriter &= { ... }` 的语义模型类型是什么？`&=` 重载在哪个类型上？建议未定义 `jsonWriter` 的类型（writer 接口？`Utf8JsonWriter`？）。这是 `meeting-json-literals.md` 已指出的 `response` 类型悬空的延续。

#### 4. 与既有特性的交互

- **`!` 字典访问。** 2017.08.23 已判读取半场"被 `!` 蒸发"。本建议是写半场，`&=` 与 `!` 互补不重叠——这是它唯一不与主线冲突的部分，也是它唯一的现实价值点。
- **`JsonIgnore` / `JsonSerializerOptions`。** `System.Text.Json` 已能表达"此成员不序列化"（`JsonIgnore`）与忽略条件（`DefaultIgnoreCondition`）。"有意不映射"是**数据**，不是语法。We think 这是对 Q2（未映射成员的有意性）最简的现成答案。
- **`#Disable Warning`。** 若真有"忘了序列化"警告，静音机制已有 2014 拍板的 `#Disable Warning`/`#Enable Warning`，且我们刚在 `meeting-ignore-warning-directive.md` 拒绝再加 `#Ignore Warning`。在映射 DSL 里再造第三个静音通道，直接违反原则 #3。
- **对象初始化器 / 匿名类型 / 字典字面量。** `New With { .name = "fred" }`（匿名类型）、字典成员初始化器（2014-02-17 #44：RESOLUTION "We like 1, 2 and 4."）、字典字面量（2014-02-17 #46：标为 "Haven't got to this. Don't think it's worth it."）。`{ "k": v }` 与这三者语法相邻而语义不同——本建议让 `{ ... }` 又多一层含义。
- **序列化与"给单个属性打标注"。** 主线 2014-04-23 讨论 record 类型时就问过："How would these 'record types' work with serialization? People commonly use Plain Old CLR Objects (POCO) when they want DataContract serialization. But there's no clear way with this proposal to attribute the individual properties."（逐字）——"如何标注单个属性"在主线是有先例的痛点，而属性标注（`JsonIgnore`）正是本建议想用语法解决的，方向反了。

#### 5. Breaking change 与兼容性

新语法本身是增量（`null : expr` 与 `{ "k": v }` 今天都解析失败），表面不破坏旧代码。但两处隐蔽风险：

- **`&=` 重载。** 若 `&=` 被扩展为 writer 语义，`Dim s As String : s &= { ... }` 与 `Dim w As JsonWriter : w &= { ... }` 走两条语义——同一运算符，两种完全无关的行为。这是原则 #7 的直接冲突，且对既有 `StringBuilder` 的 `&=` 连接习惯（Anthony 第 6 章有 `builder &= "Line" & vbCrLf`）形成概念污染。
- **映射规则的未来漂移。** 若"自然连接"某天复活，`Dim db As InMemoryDbContext = { "employees": [...] }` 从"类型不匹配错误"变成"合法映射"，而属性名大小写、缺失成员容忍度若随版本漂移，重编译会改变行为——`meeting-json-literals.md` 已要求 `langversion` 门控与警告策略。建议没有 Compatibility 章节。

#### 6. Option Strict / 编译选项分叉

两条路径必须行为一致。反序列化半场里，`ShapeOf json IsNot { "line1": address.Street }` 若 `json` 是强类型 POCO，走编译期属性映射；若是 `Object`/晚期绑定（VBScript.NET 默认宽松），走运行期字典/属性探测——两路径是两套实现，`meeting-json-pattern-matching.md` 已标记 `Suspect` 无法不经大规格就保持一致。序列化半场 `null:` 键的排除语义在 Strict On/Off 下应无分叉，但"为丢弃而求值"的值表达式在宽松下晚期绑定，行为可能不同。**两路径一致性未论证。**

#### 7. IDE / IntelliSense 影响

- **键补全。** `{` 后输入键，IDE 无法补全开放字符串键——除非目标类型已知（POCO 成员名补全），那又回到 mapping spec。`&=` writer 的键补全依赖 writer 类型定义，未定。
- **警告静音的 UX。** 2014-07-01 的 For-the-future 清单已计划 lightbulb 的 "Suppress in Source" 统一生成 `#Disable` 标识符。定向静音"忘了序列化"警告的最佳 UX 是**在该警告的 lightbulb 里给一个"添加 `JsonIgnore`"快速操作**——工具侧答案，零语法。
- **`null` 键的着色与语义高亮。** 魔法标识符需要 IDE 专门识别，为一种"未绑定名字的魔法"新增一类符号——`meeting-null-literal.md` 已指出此成本。

#### 8. 数据 / 普遍性

- 支持方最强的论据仍是主线 2017.10.18 的引述："First-class JSON support could be a strong attractant for first-time developers"——而它是引述不是统计。
- 反方：**"忘了序列化某属性"警告在今天不存在**，`null:` 键是为一个不存在的警告设计的排除语法。自然连接映射的频率、序列化样板占比，建议零数据。
- `Suspect`：VBScript.NET 脚本的序列化/反序列化确是真高频（HTTP payload、config 块），"写 JSON 不物化中间对象"是真实 DX 诉求——但这是工程判断，不是数据。主线 2017.10.18 对 JSON pattern 的处置是 "Decision: Table, wait for feedback/scenarios and more matching"，本建议没有提供 2017 年之后的新反馈或新场景。

#### 9. 更简替代

- **序列化半场**：`JsonIgnore` + `JsonSerializerOptions` 已覆盖"有意排除"；若真要流式，`Utf8JsonWriter` + 手动 `WriteString` 是运行时库现成答案，或者给 writer 一个**方法链**（`writer.Write("firstName", v)`）而非语法。警告静音用 `#Disable Warning`（2014 已定）。
- **反序列化半场**：`JsonSerializer.Deserialize(Of T)` 对强类型负载连校验都不需要；形状校验用家族 Phase 2 的 `Matches`/`Case { ... }`；"字段缺失即失败"可用 `JsonRequired`（`Suspect`：System.Text.Json 的 `JsonRequired` 特性存在性待核实，属运行库知识，非 vblang 材料）。
- **结论：两个动机都有更简替代。** "更简替代"这一条，本建议不占优。

#### 10. 成本 / 优先级

- 完整 A ≈ 编译器内置序列化器（映射规则 + `&=` 重载 + 形状模式 + pattern-into-existing-variables），量级接近一个独立序列化器团队。**不值得。**
- 序列化半场单独做 ≈ `&=` 重载 + 魔法键 + writer 契约，成本中但语义风险高。
- 反序列化半场 = 家族 Phase 2 的递归模式 + pattern-into-existing-variables（后者本身是一个大特性）。**优先级：低于模式匹配家族、可空性流分析；在家族 Phase 2 之前不可能落地。**

#### 11. 运行时 / CLR 硬约束

模式匹配本身是编译期/类型模型工作，无 PEVerify 问题。但 `&=` writer 若接触 `Utf8JsonWriter`——它是 `ref struct`，只能在流式形态里存活，`&=` 两侧的求值时机与 ref struct 的逃逸规则要专门设计。CLR 硬约束不构成障碍，但 `ref struct` 交互是真实的实现负担（与 `meeting-json-literals.md` 的结论一致）。

#### 12. 值不值得做

逐条打分（价值 × 成本 × 风险）：

- **序列化半场**：价值 4（写半场有真实 DX 诉求，但 `!` 已覆盖读半场，且 `JsonIgnore` 已覆盖排除）/ 成本 5（`&=` 重载 + 魔法键 + writer 契约）/ 风险 7（`&=` 语义污染 = 原则 #7 正面违反；魔法键 = 原则 #3/#8）→ 现在不值得。
- **反序列化半场**：价值 6（形状校验是家族 Phase 2 的合法需求）/ 成本 7（递归模式 + pattern-into-existing-variables 双重前置）/ 风险 5（`Is` 歧义 + 部分写入语义）→ 值得作为家族素材，不值得作为独立建议。
- **整体**：价值是两者之和的下界，成本与风险是两者之和的上界——**捆绑净负**。

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 表面无破坏（新语法增量）；`&=` 语义扩展与映射规则漂移是隐蔽破坏面，未分析。
2. **保持 VB-like** — `null : expr` 不是 VB 味（裸标识符魔法）；`jsonWriter &= { ... }` 的 `&=` 滥用是反例。
3. **不引入"第二种做事方式"** — **最大扣分项**：排除机制已有 `JsonIgnore`，静音机制已有 `#Disable Warning`，映射校验即将有家族 `Matches`。建议给每件事都开第三条路。
4. **默认跟随 C#，除非有充分理由** — C# 没有此特性；但"自然连接"是 SQL 行话，形状模式 C# 的 `is { Prop: v }` 属性模式也只是远亲。无主线先例，Anthony 独立延伸，需要比默认跟随更强的理由——未提供。
5. **读起来像英语、对新手友好** — `null:` 键对新手是新的困惑点（`null` 不是 JSON 键、不是 `Nothing`、不是"值为空"），不友好。
6. **不为边缘场景加特性** — "写 JSON 不物化"与"形状校验"不是边缘，但被 `&=`/魔法键拖累；真正的边缘是"为一个不存在的警告设计排除语法"。
7. **避免隐蔽控制流/语义变化** — `&=` 重载是**正面违反**；`null` 键两个含义（键/值）是隐蔽语义。这是本建议最违背的基因之一。
8. **不与既有语法冲突** — 三重冲突：`{`（字面量/初始化器）、`null`（标识符/魔法/JSON null/Nothing）、`&=`（连接/写入）。
9. **消除常见样板** — 动机成立（序列化样板），但样板已被 `JsonSerializer` + `JsonIgnore` 消掉大半，剩余增量不值语法成本。
10. **冗长只在有用时是美德** — `JsonIgnore` 特性是"显式关键字"，此处反而是美德；魔法键是假精简。

**主线对照（评价标准 2.3 表）**：

| 主题 | 主线 | ModVB（Anthony） | 本建议关系 |
|------|------|------------------|-----------|
| JSON 一等支持 | 2017.10.18 热情但 pattern 部分 Table | `ShapeOf`+`Matches` | 方向一致，但建议未引主线先例 |
| `!` 字典访问 | 2017.08.23 "value just evaporated" | 沿用 | 读半场已覆盖；本建议只补写半场 |
| `Null` 字面量 | 2014 拒绝 | 重点推进 | 建议的 `null` 键与 `Null` 字面量同源魔法，且语义更混 |
| 警告定向静音 | 2014 `#Disable Warning` 已决 | `#Ignore Warning` 被 Table | **与主线已决冲突** |
| 模式匹配 | 2018.12.19 最期待、分阶段 | `ShapeOf`+`Matches` | 反序列化半场应并入家族 Phase 2 |
| 序列化属性标注 | 2014-04-23 提问未决 | — | `JsonIgnore` 已是运行库答案 |

根本张力照旧：主线对扩展设高门槛，Anthony 是大规模扩张；本建议是"大规模扩张中把多个未定稿特性叠在一起的例子"。

### 诚实分层

- **事实**：Anthony 18.7 只有两个问题 + 两段代码（无散文）；`null` 作 JSON 键不合法（JSON 键必须是字符串）；今天的 VB 没有"忘了序列化"警告；`#Disable Warning`/`#Enable Warning` 是 2014-07-01 已决的定向静音机制（"The tradition in VB and its rich history of quick-fixes is that you resolve warnings by FIXING YOUR CODE"）；2017.08.23 原话"it already works with ! so all the value just evaporated"；2017.10.18 对 JSON pattern 的处置是 "Decision: Table, wait for feedback/scenarios and more matching"；2018.12.19 对 `Is` 的歧义担心与 pattern 引入变量的未决（"It's not clear how variable introduction without typechecking would work"）；`System.Text.Json` 有 `JsonIgnore`（运行库知识）；`Utf8JsonWriter` 是 `ref struct`。
- **Probably**：`"line2": null` 的意图是"该字段不绑定"而非"值必须为 null"；`&=` 若复活会污染 `StringBuilder` 连接习惯；VBScript.NET 脚本的序列化/反序列化是真高频，但无量化数据。
- **Suspect**：Anthony 的"自然连接"是否指自动按同名映射（示例实际是显式逐字段）；"忘了序列化"警告一旦存在会有多吵、多常见；`JsonRequired` 特性存在性（运行库知识，未核实）。
- **OPEN QUESTIONS**：① "模式匹配进已有变量/属性"的副作用时机（部分写入/回滚/先校验后写入）；② `"line2": null` 的精确语义（存在且 null？缺失也算？）；③ `null` 键两个含义（键=排除、值=不绑定）能否统一成一种概念；④ 自然连接的映射规则（同名自动？大小写？嵌套？）。
- **TODO**：把"pattern-into-existing-variables"从本建议剥离，升格为独立的开放设计项；与 `meeting-json-literals.md` 对表 `&=` writer 的最终去向；登记"写 JSON 不物化"场景的量化需求。

### RESOLUTION:

1. **整体维持 inactive，三态定为 Table。** 两个问题都没有被设计到可评估的程度，且各自都有更简替代；建议是在为一个尚不存在的警告设计排除语法。主线 2017.10.18 对 JSON pattern 的 "Table, wait for feedback/scenarios and more matching" 立场，本建议没有携带可推翻的新证据。

2. **`null:` 排除键（序列化半场的核心）Reject。** 三个理由：(a) `null` 在建议内部就有键/值两个含义，且与 JSON 真 `null`、VB `Nothing` 三者纠缠——`meeting-null-literal.md` 刚因同类"上下文魔法"维持 2014 拒绝；(b) "有意不映射"是数据，`JsonIgnore` 已表达；(c) 定向静音"忘了序列化"警告的主线答案是 `#Disable Warning`（2014 已决），`meeting-ignore-warning-directive.md` 已拒绝再开新通道。**为语法设计魔法的成本，主线已用属性与指令付过。**

3. **`&=` writer 形态交还序列化线程重新定性，但本建议不承载。** `meeting-json-literals.md` 把它裁到这里，我们确认它属于"序列化 DSL"而非 JSON 字面量；但它直接违反原则 #7（`&=` 语义污染），且 `Utf8JsonWriter` 的 `ref struct` 交互未定义。**除非**有人把"流式写 JSON 不物化"做成一份独立的、定义 writer 契约与 `&=` 调度的小建议并附数据，否则它保持搁置。

4. **反序列化半场降级为模式匹配家族 Phase 2 的需求样例。** 载体必须是家族载体（`Matches` / `Case { ... }`），不是 `ShapeOf ... Is` 操作符（2018.12.19 的 `Is` 歧义 + 家族决议已判）。"模式匹配进已有变量/属性"升格为家族文法的 OPEN QUESTION，**且部分写入/回滚语义（追问 2）必须随其设计**。

5. **与主线的关系如实记录**：JSON 一等支持方向与 #101 一致；但本建议未引 #101、2017.10.18、2017.08.23、2014-07-01 任何先例，是"Anthony 独立延伸、与主线已决事项（`#Disable Warning`）相抵"的又一例。

### Implication:

- 建议状态保持 `LDM Table`（inactive）；在 proposal 头部加一行 "LDM 2026-08-08: Table，理由见会议纪要"。
- 把 `null:` 键的 Reject 结论与理由写入提案的 Drawbacks/Alternatives，供将来重估时引用。
- 在模式匹配家族工作项上登记"pattern-into-existing-variables"为独立 OPEN QUESTION，附部分写入/回滚的语义清单。
- 与 `meeting-json-literals.md` 的 `&=` 决定保持一致：`&=` writer 不从本建议复活，除非出现独立小建议 + 数据。
- 建议作者若想重估，需补：Compatibility 章节、文法/BNF、`null` 四概念差异矩阵、映射规则清单、量化数据。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：pattern-into-existing-variables 的副作用时机（部分写入、回滚、先校验后写入）——这是反序列化半场复活的第一道闸。
- `OPEN QUESTIONS`：`"line2": null` 在形状模式里的精确语义（存在且 null / 缺失也算 / 二者皆可）。
- `OPEN QUESTIONS`：`null` 键的两个含义能否统一（键=排除、值=不绑定），还是说它们本就该是两个不同概念。
- `TODO`：量化"写 JSON 不物化中间对象"与"序列化样板"在 VBScript.NET 脚本代码库中的占比。
- `Follow-up`：与 `proposal-json-literals.md` 对表——该会议已把 `&=` 裁给本建议，本会议把它打回；两边的状态标注需同步，避免读者以为 `&=` 有去向。
- `Follow-up`：与 `proposal-json-pattern-matching.md` 对表——"匹配进既有变量"若在家族 Phase 2 落地，反序列化校验示例可作为家族的一个 acceptance test。

### 状态

- **LDM 状态：Table（保持 inactive）。** 两个问题均未达到可设计程度；`null:` 键 Reject；`&=` writer 搁置；反序列化半场降级为家族 Phase 2 素材；pattern-into-existing-variables 升格为独立开放项。
- **三态判定：Table** — 不 Reject 到死的理由：写半场的 DX 诉求真实（但被载体拖累）、形状校验是家族合法需求、pattern-into-existing-variables 是真实未决缺口。不 Active 的理由：载体全错、更简替代全覆盖、零数据、为不存在的警告设计语法。
- **激活所需信号**（明示）：① 家族 Phase 2 文法（`Matches`/`Case { ... }` + pattern-into-existing-variables 的副作用语义）落地；② 出现"流式写 JSON"的独立小建议 + 量化数据；③ 出现真实的"忘了序列化"警告场景（分析器原型）并证明 `JsonIgnore`/`#Disable Warning` 不够用。收到这些信号之前，不讨论。

---

## 附录：特性评价

# 建议评价报告：proposal-robust-mapping.md

## 评价对象

- 建议：proposal-robust-mapping.md — 更健壮的映射（自然连接映射 + `null:` 有意排除键 + `ShapeOf ... IsNot { ... }` 反序列化校验）
- 来源：Anthony 原文第 18.7 节 "More robust mapping"（`..\..\AnthonyDesign_wordpress.txt` L2906–2942；两段代码逐字来自该节，无散文）
- 配方目标：① 用对象字面量"自然连接"映射对象成员并流式写给 writer；② 用 `null` 键标明"未映射是有意的"以定向静音警告；③ 用 `ShapeOf` 形状模式校验反序列化完整性

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。两个问题被抛出但无目标量化；序列化示例依赖未定义的 `&=` 重载与 `null` 魔法键（`null` 连 JSON 键都不是），反序列化示例依赖未定的 pattern-into-existing-variables 与已 Reject 的 `ShapeOf ... Is` 形态——示例在今日 VB 中不可编译、在建议文法中也未定义 | 已提供 | 核心语法未定型 → 效果证据封顶 2–3；"为不存在的警告设计排除语法"使动机失真；无原型、无数据 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。捆绑三件强无关能力（`&=` writer、`null` 键、形状校验）；`null` 键是裸标识符魔法（非 JSON 键、非 VB 关键字语义）；`&=` 重载违反原则 #7 | 已检查 | 与 `JsonIgnore`/`#Disable Warning` 重复；`null` 键值双含义；自然连接未设计（答非所问） |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、示例与 Anthony 原文逐字一致、3 个未决问题具体诚实（1–3 健康区间，如实列出是加分）；但无文法/BNF、无 Compatibility 章节、整个语法面（`&=`、`null` 键、`{ ... }` 六义）未设计；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 缺 Grammar/Compatibility；依赖的每项前置特性都未定稿；未引主线 2017.10.18/2017.08.23/2014-07-01 先例 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风=与家族决议断裂（用已 Table/Reject 的 `ShapeOf ... Is` 形态）、与主线 `#Disable Warning` 已决相抵；暗=`&=` 语义污染、`null` 四概念混淆；雷（写半场 DX）是唯一亮色，但被载体拖累；无对冲设计 | 已检查（预测待定） | 一致性断裂未识别；`&=` 污染无对冲；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 18.7 标注正确；但未标注主线 #101（2017.10.18）JSON 先例、2017.08.23 "`!` 蒸发"评论、2014-07-01 `#Disable Warning` 先例——而这三个先例几乎逐条命中本建议；"自然连接"的 SQL 行话血缘未说明；`&=` 实际来自 18.7 与 5 章两处，json-literals 会议已裁定归属，未交叉 | 已检查 | 主线先例三连缺失；`&=` 归属未交叉标注；无杂质但成分标注严重不全 |

## 设计原则对照

- **与 VB 基因：偏离**。原则 #3（第二种做事方式，`null` 键 vs `JsonIgnore`、`&=` vs `#Disable Warning`）、#7（隐蔽语义变化，`&=` 重载、`null` 键值双含义）直接违反；#5（新手友好，`null` 魔法键不友好）不达标；#9（消除样板）动机成立但已被运行库消掉大半。仅"写半场 DX"（#9 的残余）勉强支持。
- **与主线关系：Anthony 独立延伸，且与主线已决事项冲突**。JSON 一等支持方向与 #101 一致（但 pattern 部分主线已 Table）；"定向静音"与主线 2014 `#Disable Warning` 已决相抵；`ShapeOf ... Is` 形态与家族决议（`meeting-shapeof-pattern-matching.md`、`meeting-json-pattern-matching.md`）冲突；`Null` 相关魔法与 `meeting-null-literal.md` 的维持拒绝同源。
- **破坏性变更：表面无，隐蔽有**。`{ "k": v }` 与 `null : expr` 今天解析失败（增量）；但 `&=` 语义扩展对既有 `StringBuilder` 连接习惯是原则 #7 级概念污染；自然连接映射若复活，目标类型化创建从"类型不匹配"变"合法映射"，映射规则漂移有重编译风险——需 `langversion` 门控。文档无兼容性分析。

## 总评

- **达成程度：未达成**。两个问题被诚实抛出，但没有一个被设计到可评估程度；三件捆绑的能力各踩在已判定的地皮上（`&=` 已裁入、`ShapeOf ... Is` 已 Reject、`null` 魔法已拒）；示例在今日与可预见的未来都不可编译。
- **LDM 三态建议：Table（保持 inactive）**。`null:` 键 Reject；`&=` writer 搁置（除非独立小建议 + 数据）；反序列化半场降级为家族 Phase 2 素材；pattern-into-existing-variables 升格为独立开放项。
- **主要问题**：① `null` 键键/值双含义 + 与 JSON null/`Nothing` 四概念混淆；② 为尚不存在的"忘了序列化"警告设计排除语法（Q2 依附于未建的 Q1）；③ 三件强无关能力捆绑，各踩已决地皮（`&=`/`ShapeOf ... Is`/`Null` 魔法）；④ 未引主线 #101、2017.08.23、2014-07-01 三连先例，而它们几乎逐条命中；⑤ 零数据、零原型、无兼容性分析。

## 返工建议

- **拆分建议**：拆为 (a) `&=` writer DSL（若复活）与 (b) pattern-into-existing-variables（升格为家族 OPEN QUESTION）与 (c) 形状校验需求样例（进家族 Phase 2）；拆分后分别评价。
- **补充章节**：Precedent（#101 / 2017.10.18 / 2017.08.23 / 2014-07-01 逐条引证 + 逐条回应）；Compatibility / breaking-change（`&=` 语义、映射规则漂移、`langversion` 门控）；文法/BNF（`{ ... }` 六义消歧、`null` 键判定、`&=` 调度）。
- **补充证据**：量化"流式写 JSON 不物化"与"序列化样板"占比；一个真实的"忘了序列化"警告场景（分析器原型）证明 `JsonIgnore`/`#Disable Warning` 不够用；家族 Phase 2 与 pattern-into-existing-variables 的前置实现。
- **未决问题处理**：把 `"line2": null` 语义、部分写入/回滚、`null` 键两含义统一为家族/序列化线程的规范条目；与 json-literals、json-pattern、null-literal、ignore-warning-directive 四场会议交叉标注，避免再造已判定的机制。

---

## 附录：C# 生态与互操作考量

_来源：`..\..\..\csharplang-index.md`（dotnet/csharplang C# interop 浓缩索引）与 `..\..\..\csharplang\`（dotnet/csharplang main 分支镜像）。C# 原文引文逐字核对自索引第四节 6 段与 csharplang 原文；System.Text.Json source-gen / AutoMapper 属 dotnet/runtime 与生态知识，本库无正文，标 **Suspect / OPEN QUESTIONS**。本附录只追加不改写正文。_

本提案主题上是「对象映射与序列化」——自然连接映射（Q1）、`null:` 有意排除（Q2）、`&=` writer DSL、`ShapeOf` 反序列化校验。C# 生态对这一主题的现实与 ModVB 的「语法解决映射」路线有本质分歧，但分歧点恰好**支持**本会议的三态判定（Table）。

### 相关 C# 现实方向

1. **C# 语言层没有「对象到对象映射」语法；映射是库与编译期生成的地盘。** 与 C# 最接近的语法投入是 Dictionary Expressions（C# 12 Collection Expressions 的延续），它处理 `{ "k": v }` 形态但只做**数据容器**，不把键绑定到另一对象的成员：

   > "Dictionary Expressions are a continuation of the C# 12 *Collection Expressions* feature. They extend that system with a new terse syntax, `["mads": 21, "dustin": 22]`, for creating common dictionary values." → `proposals\dictionary-expressions.md`（Summary）

   C# 对它的期望也止步于「构造字典」与「未来自然延伸到模式匹配」：

   > "It should also feel pleasant in the language, complement the work done with collection expressions, and naturally extend to pattern matching in the future." → `proposals\dictionary-expressions.md`（Motivation）

   即：C# 处理 `{ "k": v }` 的方式是**容器字面量 + 模式匹配的远亲**，与本提案的「映射 DSL + `null:` 魔法键」不共享语法血缘。

2. **C# 对「序列化模型」的未来是扩展驱动的 trait（Rust/Swift 模型），且官方明言尚无结论。** LDM-2023-10-04 整场讨论序列化模型，把它挂到 extensions（C# 14/15 主线，索引 T8）上：

   > "The serialization models adopted by newer systems, particularly Rust and Swift, are attractive, and potentially doable in C# when we add extensions that can implement interfaces on unowned types." → `meetings\2023\LDM-2023-10-04.md`

   并承认迁移是长路、有生态阻力：

   > "Even if individual users can gracefully bring old, outdated patterns into new serialization patterns, it's going to be a long road before users wouldn't have to introduce lots of adapter interface extensions, and that's not including any frameworks that may simply decide to avoid the new patterns." → `meetings\2023\LDM-2023-10-04.md`

   结论是明确的「没有结论」：

   > "There are no conclusions here today; serialization is an interesting scenario that we will continue to look at more." → `meetings\2023\LDM-2023-10-04.md`

3. **AOT/trimming 把「反射式映射」视为负担，source-gen 与类型系统是出口。** 这是 C# 对反射式场景的总立场（interceptors 讨论，索引 T5/T6）：

   > "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." → `meetings\2023\LDM-2023-07-24.md`

   且 C# 倾向把这类信息「放回类型系统」：

   > "To address this, we think that we need to take another look at the scenarios that are considering interceptors and see if we can put that information back into the type system" → `meetings\2023\LDM-2023-07-24.md`

   对 VB/ModVB 最重的一句在同场序列化讨论里：

   > "Currently, .NET AOT only works with C#, which is a detriment to the feature." → `meetings\2023\LDM-2023-10-04.md`

4. **流式写 JSON 的出口是 `Utf8JsonWriter`（ref struct）+ 库 API/source-gen，且 C# 正在松绑 ref struct 的抽象能力。** 本提案 `&=` writer 若触及 `Utf8JsonWriter`（ref struct），C# 侧现实是：ref struct 曾因不能实现接口而无法参与抽象（索引第四节已核实）：

   > "The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET." → `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）

   C# 13 已允许 ref struct 实现接口（索引 T2）——C# 的方向是给流式/低层类型更多抽象能力，**但不通过重载 `&=` 运算符**；ref struct 的栈上限定仍是硬约束（索引第四节已核实）：

   > "The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack." → `proposals\csharp-7.2\span-safety.md`（Introduction）

5. **unsafe 模型分裂（背景约束）。** C# 15 的 unsafe-evolution 对 VB 明确表态（决策文件 M8），与本提案无直接关系但约束 VBScript.NET 的「默认安全」定位：

   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（「VB」小节）

### 现实 vs 提案

| 提案项 | C# 生态现实 | 判定 | 理由 |
|---|---|---|---|
| 自然连接映射（Q1） | 语言层无此物；最近投入是 Dictionary Expressions（数据容器，不绑定成员）；映射由 AutoMapper（反射）/ System.Text.Json（含 source-gen）/ 手写转换承担；未来方向是扩展序列化 trait（未定论） | **脱节** | C# 把映射留在库层与编译期，本提案要做语言语法；方向上无 C# 支持，也无 C# 冲突——两路不交汇 |
| `null:` 排除键（Q2） | `System.Text.Json` 用 `JsonIgnore` / `DefaultIgnoreCondition` 以**数据/选项**表达「不映射」；source-gen 形态同用属性 | **冲突（被覆盖）** | C# 生态的答案与会议一致：「有意不映射」是数据不是语法；`null:` 键在 C# 侧同样无立足点（JSON 键必须是字符串） |
| `&=` writer DSL | 流式写 JSON 官方出口是 `Utf8JsonWriter`（ref struct）+ 库 API/source-gen；C# 13 才刚让 ref struct 能实现接口；从未考虑重载 `&=` | **冲突** | 运算符重载在 C# 无先例，与「避免隐蔽语义变化」的共识相悖；C# 把流式写当库 API 而非运算符 |
| `ShapeOf` 反序列化校验 | C# 有 `is { Prop: v }` 属性模式 + `Deserialize(Of T)` + record；C# 15 类型系统（unions/closed hierarchies）增强静态推理；但「匹配进已有变量/属性」在 C# 同样未决 | **需桥接** | 校验意图 C# 有对应，但载体两族不通（`ShapeOf ... Is` vs `is`/家族 `Matches`）；本会议已判降级家族 Phase 2，C# 侧无相反证据 |
| 整体序列化方向 | LDM-2023-10-04 明言「尚无结论」，靠扩展 trait + 编译期；AOT 只服务 C# | **脱节** | C# 走「类型系统/扩展/编译期」，本提案走「语言语法 + 魔法键」；方向分歧但未相撞——不构成必须跟进的压力 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态的映射双模路线。** C# 的 AOT/trimming 压力把反射式映射（AutoMapper 类）视为负担（索引 T5/T7）。VBScript.NET 脚本的序列化（HTTP payload、config 块）应默认走**编译期强类型映射**（消费 source-gen 或手写转换），反射/动态映射仅作为 COM/Office 脚本场景的显式 opt-in。这与决策文件 M2/M5/M8 的「脚本层允许动态、编译产物走类型化」双模路线一致。

2. **source-gen 桥。** .vbx 编译器应能**消费** C# source-gen 产出的序列化元数据（`JsonSerializerContext` / `JsonSourceGenerationOptionsAttribute`——精确 API 属 dotnet/runtime，本库无正文，见未决项），让脚本直接调用编译期序列化；C# 的拦截器/生成器生态是「把信息放回类型系统」，VB 若搭上同一班车可共享 AOT 出口（索引 T6）。

3. **识别新元数据。** C# 的类型系统扩展（unions/closed hierarchies 的 `CompilerFeatureRequired` 标志、扩展成员、requires-unsafe 元数据）会以新元数据形式出现在 .NET 11+ 程序集里；VB 编译器必须认识，才能正确校验跨语言调用（决策文件 M4/M8）。对映射：若 C# 未来落地扩展序列化 trait（LDM-2023-10-04 的方向），那些扩展成员以新元数据暴露，VB 需能消费。

4. **若「鲁棒映射」复活，建模成编译期映射而非魔法语法。** C# 生态给出的方向（source-gen、扩展 trait、数据属性）说明「映射」在 .NET 的未来是**编译期生成 + 类型系统**。若 VBScript.NET 仍想给脚本一个映射 DSL，应定义在编译期（绑定 POCO 成员名、生成映射代码），「排除」用数据表达（属性/选项），避开 `null` 魔法键——这与本会议对 `null:` 键的 Reject 理由同构。

### 对既有 RESOLUTION / 三态判定的影响

- **整体支持 Table。** C# 生态没有任何「正朝语言级映射前进」的信号（Dictionary Expressions 是数据容器、序列化 trait 未定论、AOT 只服务 C#），因此没有来自 C# 的跟进压力需要 ModVB 激活本提案。**脱节**判定不推翻「不 Reject 到死」的理由（写半场 DX 真实、形状校验是家族合法素材），但也不构成激活信号。
- **`null:` 键 Reject 被生态背书。** C#/System.Text.Json 用属性与选项表达「不映射」（数据），从不考虑语法级魔法；会议「为语法设计魔法的成本，主线已用属性与指令付过」的判断与 C# 现实一致。
- **`&=` writer 搁置被生态背书。** C# 对流式 JSON 的出口是 `Utf8JsonWriter`（ref struct）+ 库 API/source-gen；重载 `&=` 在 C# 无先例。会议「不认为流式写 JSON 的卖点值得污染 `&=`」与 C# 现实一致。
- **反序列化半场降级家族 Phase 2 不变。** C# 的「匹配进已有变量/属性」同样未决（主线 2018.12.19），C# 侧无相反证据；`ShapeOf ... Is` 的载体两族都不通，降级判定成立。
- **新增注意点（对激活条件③的补充）。** LDM-2023-10-04 的 "Currently, .NET AOT only works with C#, which is a detriment to the feature." 提醒：VBScript.NET 若把「映射/序列化」作为核心脚本场景，AOT 只服务 C# 的现实意味着 .vbx 的序列化栈必须设计为 **source-gen 优先**，否则与 AOT 无缘分叉会更深。任何「流式写 JSON」复活信号都应携带 AOT 姿态。

### 引用纪律与未决项

- 本附录 C# 原文逐字核对自：`proposals\dictionary-expressions.md`、`meetings\2023\LDM-2023-10-04.md`、`meetings\2023\LDM-2023-07-24.md`，以及索引第四节 6 段中已核实的 `proposals\csharp-13.0\ref-struct-interfaces.md`、`proposals\csharp-7.2\span-safety.md`、`proposals\unsafe-evolution.md` 三处。
- `OPEN QUESTIONS`：`JsonSerializerContext` / `JsonSourceGenerationOptionsAttribute` 的精确 API 名称与语义（System.Text.Json source generator 属 dotnet/runtime 生态，csharplang 库无正文，未核实）。
- `OPEN QUESTIONS`：AutoMapper 的具体实现（反射式运行时映射）——生态常识，本库无正文，未核实；其与 AOT 的兼容性论断基于索引 T5/T7 的通用方向。
- `Suspect`：C# 扩展序列化 trait 的未来落地形态（LDM-2023-10-04 明言无结论，本附录仅作为「方向」引用，不作为可依赖的路线图）。
