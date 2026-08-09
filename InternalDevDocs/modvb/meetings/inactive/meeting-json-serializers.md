# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题同样来自建议目录的 **inactive** 子目录——`proposal-json-serializers.md`（Anthony 原文第 18 章 18.8 "VB Idiomatic custom JSON serializers"）。这份建议只有一段话，没有语法、没有示例、没有实现面；它记录的是一个方向性判断：**VB 写自定义 JSON 序列化器的惯用方式不该照抄 C# 的 `Utf8JsonReader` + ref struct 写法，VB 需要一个属于自己的"自然抽象"来推迟、缓解或绕开 ref struct 的一般性问题。** 上一场 JSON 字面量会议我们已经把 `&=` writer 形态 Reject 出 json-literals、归给了 robust-mapping；今天这份建议恰好落在同一条线上。所以本场会议真正的问题是：**这段方向注记值得激活、继续保持搁置，还是应该被"拆解"成它真正指向的那些设计线程？** 开场我们就预期：这不会是一次"通过某份提案"的会议，而是一次"确认我们该不该为它发明一个特性"的会议。

## Agenda

* [Proposal: VB 惯用自定义 JSON 序列化器（VB Idiomatic Custom JSON Serializers）](#proposal-vb-惯用自定义-json-序列化器)

## Proposal: VB 惯用自定义 JSON 序列化器（VB Idiomatic Custom JSON Serializers）

_Related: [vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；[vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)；[vblang #27 – Guid literals](https://github.com/dotnet/vblang/issues/27)；ModVB：`proposal-json-literals.md`、`proposal-json-pattern-matching.md`、`inactive/proposal-robust-mapping.md`、`proposal-runtime-library.md`、`inactive/proposal-string-span-utf8.md`、`meeting-json-literals.md`_

### 场景与缺口

We started from the proposal's own motivation，它把一个问题摆到了桌面上：

> This goes with the previous example. The only scenario I consistently hear for "ref structs" is custom JSON serializers with `Utf8JsonReader`. Setting aside the general issue, this assumes that the idiomatic way to write a deserializer in VB is the way it's done in C#. I could delay, mitigate, or side-step the issue if VB had a natural abstraction for this.（Anthony 18.8，逐字引用）

拆开看，这句话里有三层意思：

1. **事实层**：人们为 "ref struct" 辩护时，最常引用的场景是用 `Utf8JsonReader` 写自定义 JSON 序列化器。
2. **假设层**：这个动机默认了"VB 写反序列化器的方式应该照搬 C#"。
3. **主张层**：如果 VB 有自己的自然抽象，就能**推迟、缓解或绕开** ref struct 的一般性问题。

我们先把"缺口"具体化：VB 开发者今天需要什么？

```vb
' 今天：低性能需求交给 JsonSerializer，动态读取交给 JsonNode + `!`。
Let order = JsonSerializer.Deserialize(Of Order)(jsonText)   ' 强类型映射
Let node = JsonNode.Parse(jsonText).AsObject()               ' 动态 DOM
Let lines = node!lines.AsArray()

' 只有流式 / 自定义转换需求才被迫走向 Utf8JsonReader + ref struct 的 C# 惯用法。
```

`JsonSerializer.Deserialize(Of T)` 覆盖了 POCO 强类型映射；`JsonNode` + `!` 覆盖了动态读取——而这两者**都不是 ref struct**。真正把开发者推向 C# 惯用法的，是"流式高性能"与"运行时映射规则覆盖不到的自定义转换"这两个残留缺口。**We think：把缺口讲成"VB 需要一种写序列化器的新语法"，是把一个本可由组合解决的场景，误当成一个需要新特性的场景。**

### 候选方案

**PROPOSAL A — 组合路线：字面量 + 形状匹配 + 运行时。** "自然抽象" = `json-literals B`（`{ ... }` 数据字面量 → `JsonObject`）+ `!` 字典访问 + `ShapeOf` 形状匹配（json-pattern、robust-mapping）+ `JsonSerializer`/转换器（运行时）。**不新增任何"序列化器专用语法"**；`JsonNode` 是普通类，天然绕开 ref struct。写半场靠字面量构造，读半场靠 `!` 与形状校验，强类型映射委托运行时。

**PROPOSAL B — 运行时库 / 源生成器层。** 在库层提供 VB 友好的序列化辅助：`JsonSerializer` 的 VB 惯用包装、针对自定义映射的**源生成器**（生成 `Utf8JsonWriter`/`Utf8JsonReader` 的逐字段代码）。语言零改动；"自然抽象"放到底层而非语法层——这正是建议自己 Alternatives 里列出的路线，也是 `proposal-runtime-library.md`（Anthony 第 17 章）的地盘。

**PROPOSAL C — 正面解决 ref struct 一般性问题（不设 aside）。** 给 VB 一等公民的 `ref struct` 支持（声明 + 放宽存储/捕获约束）。这**不**给 VB 一个"自然抽象"——它只是让 C# 的惯用法在 VB 里可用，但确实能把本建议的动机场景整体关掉。范围大（声明文法、存储规则、闭包/异步/泛型交互），且建议明确把它 set aside。

**PROPOSAL D — 专用序列化 DSL（`&=` writer 形态）。** 复活 Anthony 18.7 的 `jsonWriter &= { ... }` 作为"自然抽象"本体。**上一场 json-literals 会议已裁定 Reject**（RESOLUTION #4：`&=` 语义与字符串连接差异大、`response` 类型未定义、`null:` 不是合法 JSON 键 → 它是"序列化 DSL"而非 JSON 字面量，归 robust-mapping）。今天我们不重新审理，只确认裁定的适用范围。

**OTHER DESIGNS CONSIDERED**

- **什么都不做 / 维持现状**：`JsonSerializer` + `JsonNode` 覆盖 90%，流式残留场景用 C# 辅助程序集写（C# 有完整 ref struct）。零语言成本；但代价是"ref struct 动机最常被引用的场景"依然悬着，ref struct 支持的压力不会消失。
- **Schema / 标注类型路线**：`{"http://.../schema"}` 标注类型 + `!` 的 IntelliSense（json-pattern）。2017.10.18 会议已表态 *"We might be able to do a lot with no compiler changes and should investigate with IDE team."*（逐字引用）——这是与序列化正交的"读取增强"线，不是本建议的地盘。
- **编译器内置反序列化器**（json-literals A 的路径）：把 JSON 映射规则写进编译器。上一场会议已判"等于在编译器里重写一遍 System.Text.Json"，不采纳；本建议若被误读为这条路线，我们要显式堵死。

### 权衡：Q&A

- **A vs B：抽象放语法层还是库层？** 我们先把"自然抽象"这个词拆成三半：构造（写半场）、读取（读半场）、强类型映射。读取半场的答案**已经存在**——`JsonNode` 是一个普通类，`!` 走默认属性访问，`ShapeOf` 做形状校验，全程不碰 ref struct。强类型映射的答案也已经存在——`JsonSerializer` + 转换器 + 源生成器。真正还缺的只有**构造半场的语法糖**（json-literals B）。所以 A 不是一个新特性，而是若干既有设计线程的交集；B 覆盖剩下的流式残留。**We like the composition，因为它的每一个零件都在别的建议里被设计着；我们不喜欢"为序列化器发明一个新语法"，因为那会造出第二套做事方式。**
- **C 到底是不是本建议该做的？** 不是，但值得说清。正面解决 ref struct 能满足"能在 VB 里写 C# 惯用法"，却**不满足**"VB 有自己的惯用法"——它只是让 C# 的 idiom 可用，和"自然抽象"是两回事。而且 ref struct 是大而横切的类型系统改动（声明文法、存储规则、闭包/异步/泛型），用一个 JSON 场景去背书它，代价与收益不成比例。**但我们如实记录**：如果"the only scenario I consistently hear"这句轶事为真，那么解决 ref struct 会**整体关闭**本建议的动机场景——这正是我们坚持"把这条证据传给 ref struct 工作项"的原因。
- **D 为什么不重新审理？** 上一场 json-literals 会议已经把 `&=` 的否决理由写成了 RESOLUTION #4，今天再讨论一遍是对既有裁定的浪费。补一句独立理由：`null:` 作为键在 JSON 语法里根本不合法（`null` 是值不是键），这足以证明 18.7 的形态本质是"序列化 DSL"而不是 JSON 字面量——DSL 归 robust-mapping，`Utf8JsonWriter` 的 ref struct 约束也归那边解决。
- **"自然抽象"的提问方向是不是反了？** 我们反复回到这个点上。不问"该发明什么语法"，而问"VB 开发者写出什么，读起来像 VB？"——答案是**数据形状**：`{ "firstName": person.Name.Given }` 读起来像数据本身；`Utf8JsonReader` 的逐 token 走读读起来像管道工在修水管。数据形状恰恰是字面量 + 模式匹配给的，不是序列化器给的。**We think：Anthony 想要的"自然抽象"，他其实已经在隔壁建议里逐件设计完了，只差一句"组合起来"。**
- **与 System.Text.Json 的分层怎么看？** 这是本场最关键的对齐。运行时在 JSON 上投了重资：`JsonSerializer`（选项、转换器、多态、循环引用）、`Utf8JsonReader`/`Utf8JsonWriter`（流式）、`JsonNode`/`JsonObject`/`JsonArray`（DOM）、源生成器。任何语言特性都必须在这张图里找自己的位置，而不是重画一张。**We 的分层主张：语言提供数据形状的表达力（字面量、模式、`!`）；运行时提供映射机制（`JsonSerializer`、转换器）；库/源生成器提供 VB 化的组装层。编译器不得成为序列化器。** 这同时符合主线"默认跟随 C#"的分层——C# 的 JSON 路线就是库 + 源生成器，没有语言特性。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

本建议**没有提出任何语法**，所以文法风险是"继承"来的，不是"新增"的。若走 A 组合路线，文法负担落在兄弟建议上：`{` 的第五种含义（json-literals，已在上一场会议做了文法演练）、`ShapeOf ... Is { ... }` 的形状模式（json-pattern）、`"line1": address.Street` 的映射进已有 lvalue（robust-mapping）。若走 D，`&=` 的双义性回归——已被否决。**结论：本建议不直接贡献任何文法行，因此也不新增文法歧义；它应当被写入兄弟建议的规格，而不是自立门户。**

#### 2. 角案例 / 边界语义

自定义反序列化器到底需要什么、而 `JsonSerializer`/`JsonNode` 给不了的？我们列出三类残留：

```vb
' (a) 流式 / 大 payload：不能整体物化进 JsonNode，要边读边写。
' (b) 运行时映射规则覆盖不到的自定义转换（如历史格式、非标准时间戳）。
' (c) 反序列化进已有对象（robust-mapping 的 ShapeOf 校验示例）。
```

角案例逐个过：

- **`Nothing` 与 JSON `null`**：`{ "a": Nothing }` 表示 `null` 还是"缺省不映射"？robust-mapping 用 `null:` 键表示"有意不映射"，与 JSON 字面量里的空值语义是**两套**——这个区分在 robust-mapping 的 Unresolved 里还挂着，本建议的"自然抽象"若想消化它，得先回答它。
- **反序列化进已有变量/属性**：robust-mapping 的 `ShapeOf ... IsNot { "line1": address.Street }` 依赖"pattern matching into existing variables/properties"，Anthony 自己注明 *"is still an open question, though."*（18.7 原注）——**A 组合路线的读半场有一个未建好的前置**。
- **JSON 键大小写**：这是 VB 特有分叉。VB 标识符大小写不敏感（`FirstName` 与 `firstname` 同物），而 JSON 键是**大小写敏感的数据**。`"firstName"` → `FirstName` 的映射在 VB 里比在 C# 里更"自然"（文化上不敏感），但映射层必须显式决定策略——`System.Text.Json` 默认 `PropertyNameCaseInsensitive := False`，`Probably` 这个默认值与 VB 直觉相反。该策略属于 mapping spec（json-literals A 的 Table 项），本建议不重造。
- **大小写之外还有重名**：VB 里 `Name` 与 `name` 是同一个成员，JSON 里 `"name"` 与 `"Name"` 是两个键。映射层收到两个大小写变体键时是报错、后者覆盖、还是按声明序取？未定义。

#### 3. 作用域与绑定

若 A 组合路线引入"JSON 键绑定到成员"的映射语法（那是 json-literals A 的 POCO 目标类型化，已 Table），`"firstName"` 绑到 property 还是 backing field、getter/setter 求值时机——我们在 TypeOf 流分析会议处理过同一类问题的教训：**绑定语义一旦不透明，IDE 与语义模型的呈现跟着不透明。** 本建议不新增绑定面；它必须等 mapping spec 把"键到成员的绑定"定清楚，再谈"自然抽象"。`ShapeOf` 的读半场绑进已有 lvalue，属 robust-mapping 的开放问题。

#### 4. 与既有特性的交互

- **`!` 字典访问**：2017.08.23 会议记录过 *"The JSON thing is great. Oh, but it already works with ! so all the value just evaporated."*（逐字引用）——读取半场已被 `!` 覆盖。本建议若只做构造半场（A 的字面量），恰好与 `!` 互补；若误做成序列化 DSL（D），则与 `!` 无关、与 `&=` 冲突。
- **Async**：`Utf8JsonWriter`/`Utf8JsonReader` 是 ref struct，**不能跨 `Await` 捕获**。一个 async 的自定义序列化器（流式写 + 周期 `FlushAsync`）在 C# 里也要绕开 ref struct——这是 `JsonNode`（普通类）路线在 VB 里格外有说服力的技术原因之一，也是"自然抽象"不限于审美偏好的证据。`Probably`：这条对 VB 与 C# 同样成立。
- **Late binding / Option Strict Off**：与 json-literals 会议的既定规则一致——值表达式按各自模式求值（Strict Off 下晚期绑定），字面量降级为 `JsonObject` 构造调用不变；新语法不得改变先前已成功绑定的代码。
- **`&=` 字符串连接**：已被 `&=` writer 的否决覆盖，不重复。
- **XML 字面量先例**：JSON 数据字面量是 XML 字面量基因的延续（构造调用降级、免行延续），这是 A 组合路线最稳固的基因锚点。

#### 5. Breaking change 与兼容性

本建议**零语法**，因此零直接破坏面——它没有"旧代码被重解释"的问题。破坏面全在它可能诱导的两条已否决路径上：

- **复活 `&=` writer**（D）：`&=` 从字符串连接变成"写片段"，重编译即换行为——原则 #7 的正面教材，已 Reject。
- **编译器内置反序列化器**（json-literals A 路径）：`Dim db As InMemoryDbContext = { "employees": [...] }` 从"类型不匹配错误"变成"合法映射"，而映射规则（大小写、缺失容忍度）随版本漂移会使重编译改变行为——需要 `langversion` 门控。已在 json-literals 会议判 Table。

**We 要求**：本建议的任何复活版本，都必须在文档里显式声明"不属于编译器内置序列化器"与"不复活 `&=`"两条禁令，防止方向注记被读成实现许可。

#### 6. Option Strict / 编译选项分叉

- A 组合路线：字面量降级与 `ShapeOf` 匹配在 Strict On/Off 下行为一致（值表达式按各自模式求值），已在 json-literals / json-pattern 规格中覆盖。
- B（源生成器）：生成代码在项目自己的 Option Strict 下编译——**源生成器不得在 Strict Off 项目里生成晚期绑定代码**，反之 Strict On 项目里生成代码要过严格检查。`Probably`：这是源生成器设计面的硬约束，须原型验证。
- C（ref struct）：声明与使用是纯早期绑定，与宽松模式无交互。
- **结论：无新分叉。** 两条路径一致性义务主要落在兄弟建议身上。

#### 7. IDE / IntelliSense 影响

- **键补全**：`{` 后输入 `"`，对 `JsonObject` 目标无法补全键（开放字符串）；只有当目标类型是 POCO 且走 mapping spec 才能按成员补全——那是 json-literals A 的杀手级场景，已 Table。B（源生成器）里 IDE 需要支持"跳转到生成代码"与断点映射。
- **`!` 补全**：依赖 annotated types（json-pattern 的 `{"uri"}` 标注），2017.10.18 会议已指出 *"We might be able to do a lot with no compiler changes and should investigate with IDE team."*（逐字引用）——这是 IDE 团队路线，不是语言特性。
- **调试体验**：组合路线下断点落在用户写的映射代码上（清晰）；编译器内置反序列化器路线下断点落在编译器魔法里（不可调试）——这是我们反对该路线的又一个理由。

#### 8. 数据 / 普遍性

这是本场最需要诚实的地方。Anthony 的动机证据是**一句轶事**："the only scenario I consistently hear"。`Suspect`：这句话有幸存者偏差——它反映的是"移植到 VB 的 C# 开发者抱怨什么"，而不是"数十万安静客户"的业务代码里序列化代码的分布。后者的主路径是 `JsonSerializer.Deserialize(Of T)` + `!`，两者都已就位。自定义流式反序列化器是**窄中之窄**。但我们同时记录它的不成比例的重要性：**它是 ref struct 支持动机里"最常被引用"的场景**——论频次它排不上号，论论辩价值它排第一。正因为如此，它值得被认真回答，而回答的方式可以是"用组合绕开"或"给 ref struct 工作项供证据"，而不必是"发明一个特性"。

#### 9. 更简替代

- `JsonSerializer.Deserialize(Of T)` + 自定义 `JsonConverter(Of T)`：现状，覆盖绝大多数自定义转换。
- `JsonNode` DOM：动态读取，`!` 访问，非 ref struct。
- **源生成器**（B）：`System.Text.Json` 的 `JsonSerializable`/源生成器路线（`.NET 6+`，`Probably`——属生态事实，本仓库无法核实到逐字规格）在编译期生成逐字段映射代码，兼顾性能与强类型，且不需要语言改动。
- C# 辅助程序集：VB-only 团队不接受，但对混合团队是零成本答案。
- **结论：替代方案强。** 唯一语言层能独占的价值是构造半场的**数据字面量体验**（json-literals B）与读半场的**形状校验**（robust-mapping）——这两件都在兄弟建议里，不在本建议里。

#### 10. 复杂度 / 成本 / 优先级

- 本建议**没有实现面**：无语法、无 API、无文法。把它激活，等于先发明一个特性再实现它——我们没有理由为一个已经能用组合解决的场景发明特性。
- A 组合路线的成本**已经由兄弟建议支付**：json-literals（字面量文法）、json-pattern（形状模式）、robust-mapping（映射/校验）、runtime-library（辅助与源生成器）。
- C（ref struct）成本大而横切，应作为独立议题，由 ref struct 工作项决定优先级。
- **优先级：低于 json-literals、json-pattern、robust-mapping**——它们是载体，本建议只是方向注记。这是一份"收敛检查"提案，不是"实现"提案。

#### 11. 运行时 / CLR 硬约束

- `Utf8JsonReader`/`Utf8JsonWriter` 是 `ref struct`，受 CLR 存储规则约束：不可装箱、不可入字段、不可被闭包/异步捕获。`Probably`：VB 能在局部作用域内消费 ref struct，但不能声明新 ref struct 类型；"一般性问题"真实存在，但本建议明确 set aside。
- `JsonNode` 路线是普通类，无任何 CLR 约束，无 PEVerify 问题。
- B（源生成器）生成的是普通 IL，无约束。
- 表达式树：字面量/模式不可进表达式树（构造调用不可进），与 XML 字面量一致——已在 json-literals 会议记录。

#### 12. 值不值得做

逐维打分。**作为独立特性**：价值中（场景真实但窄）、成本无（无实现面）但"发明一个特性"的隐藏成本高、风险低但误读风险真实（编译器内置序列化器 / `&=` 复活两条已否决路径）。**结论：不值得为它单独激活。** **作为方向注记**：价值高——它把"序列化应该通过数据形状表达，而不是 token 走读"这条 VB 审美写进了档案，并为 ref struct 工作项提供了动机证据。我们选择后者。

### VB 基因对照

- **保持 VB-like（原则 #2）**：本建议的全部灵魂在这一条——拒绝照抄 C# 惯用法、要求 VB 自己的惯用法，是字面上的"保持 VB-like"。这是我们愿意为它保留 inactive 位置、而不是直接 Reject 的最大理由。
- **读起来像英语、对新手友好（原则 #5）**：`{ "firstName": person.Name.Given }` 读起来像数据；`Utf8JsonReader` 走读是管道工活。数据形状论是这条原则的直接推论。
- **消除常见样板（原则 #9）**：命中，但只在其场景常见时才成立——`Suspect`，数据未量化。
- **不引入"第二种做事方式"（原则 #3）**：**危险区。** `JsonSerializer` 已经是"那种方式"。A 组合路线安全，因为它让既有 `JsonObject` 路径更顺手而非新增竞争机制；D 与"编译器内置序列化器"都造第二种方式，均否决。
- **避免隐蔽的控制流/语义变化（原则 #7）**：`&=` 复活即违反，已 Reject。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 的 JSON 路线是库 + 源生成器，没有语言抽象。本建议如果走"语言特性"路线就偏离了 C# 且没有充分理由；如果走"库 + 源生成器 + 字面量糖"路线则与 C# 分层一致。2018.02.07 会议对可空引用类型的态度是 *"We'll postpone this until we understand the uptake in C#."*（逐字引用）——对"先看 C# 生态怎么走"我们有先例可依。
- **不为边缘场景加特性（原则 #6）**：自定义流式反序列化器是窄中之窄。这条原则直接指向"不发明特性"。
- **与主线关系（对照表 2.3）**：JSON 数据字面量方向与主线 #101 讨论**主线一致**；"自定义序列化器抽象"是 **Anthony 独立延伸**（主线没有这个概念）；与 json-literals / json-pattern / robust-mapping / runtime-library 同族，本建议是这一族的方向性注脚，不是新成员。

### RESOLUTION:

1. **不单独激活 `proposal-json-serializers`。** 它是方向注记，不是提案：无语法、无实现面、无数据。把它当作可实现的特性来激活，等于先发明特性再实现，而发明没有理由。
2. **"自然抽象" = 组合，而非新特性。** 写半场 = JSON 数据字面量（json-literals B，构造 `JsonObject`）；读半场 = `!` 字典访问 + `ShapeOf` 形状匹配（json-pattern、robust-mapping）；强类型映射 = `JsonSerializer` + 转换器 + 源生成器（运行时）。**`JsonNode` 是普通类，不是 ref struct——它已经是现成的"自然抽象"。** 本建议的意图应被写进兄弟建议的规格，而不是自立门户。
3. **流式高性能残留缺口（`Utf8JsonReader`/`Utf8JsonWriter`）不在此发明语法。** 两条出路：(B) 运行时库 / 源生成器（`proposal-runtime-library.md` 地盘）；(C) 正面解决 ref struct 一般性问题（另立建议）。**本建议的动机作为 (C) 的输入证据移交**——它是 ref struct 支持动机里"最常被引用"的场景。
4. **不复活 `&=` writer 形态**（维持 meeting-json-literals RESOLUTION #4 的裁定）；`Utf8JsonWriter` 的 ref struct 约束问题归 robust-mapping / string-span-utf8 处理。
5. **明确两条禁令写进档案**：任何复活版本不得是"编译器内置反序列化器"（json-literals A 路径，Table 且需 mapping spec），不得复活 `&=`。防止方向注记被读成实现许可。
6. **保持 inactive，定义为"收敛检查"提案。** 当 json-literals B / json-pattern / robust-mapping 落地后回访，确认"写自定义反序列化器"的场景被组合路线覆盖；若回访时仍有真实痛苦且可复现，再谈新增抽象。
7. **激活所需信号（明确列出）**：
   - (a) 出现具体语法或 API 草案，附可编译示例；
   - (b) 数据：真实 VB 代码库中"自定义反序列化器"代码占比超过可辩护阈值（量化普遍性，取代轶事）；
   - (c) 组合验证：字面量 + `ShapeOf` 落地后，仍有可复现的"写反序列化器很痛苦"场景；
   - (d) 生态评估：System.Text.Json 源生成器经评估仍不能覆盖该场景。

### Implication:

- 更新 `proposal-json-literals.md` / `proposal-json-pattern-matching.md` / `proposal-robust-mapping.md` 规格，各加一节"序列化惯用路径"，把写半场（构造）与读半场（匹配 + `!`）的衔接写清楚；本建议作为这些章节的出处被引用。
- 向 ref struct 工作项传一条证据：自定义 JSON 序列化器是 ref struct 动机最常被引用的场景；解决 ref struct 即关闭此动机（但"自然抽象"仍以数据形状为准）。
- 与 `proposal-runtime-library.md` 对齐：把"VB 风格的序列化辅助 + 源生成器"列为该建议的设计面之一。
- 不建原型；不新增文法；不新建独立提案。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：若组合路线回访后仍不足，额外需要的"自然抽象"形态是什么？——目前**没有任何候选语法**，我们不猜。
- `OPEN QUESTIONS`：流式残留场景（`JsonNode` + `JsonSerializer` 覆盖不到的自定义反序列化器）的真实占比——尚无测量。
- `OPEN QUESTIONS`：JSON 键大小写映射策略（VB 标识符大小写不敏感 vs JSON 键大小写敏感）——归 mapping spec，本建议不先定。
- `TODO`：量化自定义反序列化器在真实 VB 代码库中的占比（激活信号 (b) 的证据来源）。
- `TODO`：System.Text.Json 源生成器能力清单（`JsonSerializable`、自定义转换器、AOT 兼容）——作为激活信号 (d) 的借镜清单。
- `Follow-up`：与 ref-struct 工作项 / `proposal-string-span-utf8.md` 对齐"ref struct 在 VB 中的消费边界"事实（`Probably`：局部可消费、不可声明、不可捕获/装箱，需在原型中核实）。
- `Follow-up`：与 json-literals 的 mapping spec（A 的 Table 项）对齐，确保"键补全"与"绑定到成员"不在本族重复设计。

### 状态

- **LDM 状态：保持 Inactive**；转为"收敛检查"角色——本建议是 JSON 家族的方向注记，不独立排期。
- **三态判定：Table（保持搁置）** — 不是 Reject（动机真实、方向符合 VB 基因 #2/#5、与主线 #101 呼应、无破坏面），也不是 Active（无可实现内容、无数据、无语法）。激活信号见 RESOLUTION #7，逐条待检。

---

## 附录：特性评价

# 建议评价报告：proposal-json-serializers.md

## 评价对象

- 建议：proposal-json-serializers.md — VB 惯用自定义 JSON 序列化器（用"自然抽象"推迟/缓解/绕开 ref struct 一般性问题）
- 来源：Anthony 原文第 18 章 18.8 "VB Idiomatic custom JSON serializers"（`..\..\AnthonyDesign_wordpress.txt` L2946–2952，引文逐字）
- 配方目标：让 VB 写自定义 JSON 序列化器不必照抄 C# 的 `Utf8JsonReader` + ref struct 惯用法，改用一个 VB 自己的自然抽象

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。场景命名具体（自定义反序列化器），但"自然抽象"的改进目标**没有任何可演示载体**——全文零示例、零语法；唯一证据是轶事引述（"only scenario I consistently hear"）；关键子效果（绕开 ref struct）已被 `JsonNode` 现状部分达成却未引用 | 已提供/已检查（状态行 Prototype/Implementation/Specification 为占位链接，无运行证据） | 声称的效果与"组合路线已覆盖大部分"的事实未对齐；普遍性无数据 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化"（此处为**无特性可评**）。没有语法/类型/API 载体，"特性"维悬空；唯一正向是其动机本身——拒绝照抄 C# 惯用法（原则 #2 保持 VB-like）、数据形状论（#5），这两条是纯粹 VB 基因 | 已检查 | 无具体载体意味着无法评估"与既有语法和谐"；若被实现成编译器内置序列化器或 `&=` 复活，特性维会滑向 1 分 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、引述准确、3 个未决问题具体诚实（1–3 健康区间，加分）；但 Detailed design **完全空置**（明确"留白"）、全文零示例（违反模板"必须包含示例"）、Drawbacks 泛泛、无 Compatibility/分层分析 | 已检查 | 空 Detailed design + 零示例使文档止于"记录方向"，无法支撑任何设计评审的下一步 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。无维度明显受益（无可实现内容，雷/水/光均无实质贡献）；暗风险低但真实——若被误读为"编译器内置序列化器"或"`&=` 复活"会重复 json-literals 会议已否决的两条路径，文档未对冲此误读风险 | 已检查（预测待定） | 作为 inactive 存档无破坏；实际影响须"已采纳"后定；文档未声明两条禁令 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注，标注与影响有偏差"。来源标注准确（18.8 引文逐字正确）；但未标注相邻成分：与 18.7 robust-mapping 的 "previous example" 关系、主线 #101 先例（JSON 数据字面量方向）、C# `Utf8JsonReader` 参照、`System.Text.Json` 运行时先例、json-literals 会议已否决 `&=` 的裁定 | 已检查 | 无杂质；但成分关系不透明——建议引"previous example"却不指向 robust-mapping，读者无法知道它站在哪条已定裁决线上 |

## 设计原则对照

- **与 VB 基因：方向一致、载体缺失。** 一致：原则 #2（保持 VB-like，拒绝照抄 C# 惯用法是字面命中）、#5（数据形状论）、#9（消除序列化样板，频次 Suspect）。缺失：无载体使 #3（不引入第二种做事方式）、#7（避免隐蔽语义变化）无法被正面评估——只能靠两条禁令（不复活 `&=`、不做编译器内置序列化器）兜底。
- **与主线关系：分裂。** JSON 数据字面量方向与主线 #101（2017.10.18）讨论**主线一致**（数据字面量 / 字典初始化超集直觉）；"自定义序列化器抽象"是 **Anthony 独立延伸**（主线无此概念，对照表 2.3 的 JSON 行未覆盖序列化器）；与 json-literals / json-pattern / robust-mapping / runtime-library 同族，属族内方向注记。
- **破坏性变更：无（零语法）。** 潜在破坏全部来自它可能诱导的两条已否决路径（`&=` 语义污染、编译器内置序列化器的重编译敏感性），文档未显式声明禁令。

## 总评

- **达成程度：未达成（作为可采纳特性）/ 达成（作为方向注记存档）。** 作为特性它没有任何可采纳内容；作为档案它诚实地记录了一条符合 VB 基因的设计方向，值得保留在 inactive。
- **LDM 三态建议：Table（保持 inactive，转"收敛检查"角色）。** 面向 VBScript.NET 的优先级同样为 **Table**：脚本/业务代码的主流序列化路径（`JsonSerializer` + `!` + 未来的数据字面量）不需要一个"序列化器专用抽象"；流式残留场景属于运行时库/源生成器或 ref struct 议题，不属于语言特性。
- **主要问题**：① 无任何实现内容（零语法、零示例），不可执行；② "自然抽象"具体形态未定义且无候选；③ 误读风险——被实现为编译器内置序列化器或 `&=` 复活会重复已否决路径，文档未对冲；④ 动机证据是轶事（"only scenario I consistently hear"），无数据；⑤ 与兄弟建议（robust-mapping / json-literals）的"previous example"关系未标注，读者无法定位裁决链。

## 返工建议

- **补充章节**：Detailed design（现空置）——要么给出组合路线（字面量 + `ShapeOf` + `JsonSerializer`）的可验证代码路径，要么明确声明"本建议是收敛检查而非实现建议"，二选一；补 Compatibility（零破坏面 + 两条禁令）；补与 `System.Text.Json` 的分层图。
- **补充证据**：自定义反序列化器在真实 VB 代码库的占比数据（激活信号 (b)）；`JsonNode` vs `Utf8JsonReader` 在 VB 中的实际差距测量（流式残留的真实边界）；System.Text.Json 源生成器能力清单（激活信号 (d)）。
- **未决问题处理**：三个 Unresolved 要么在本建议回答，要么显式指向兄弟建议（robust-mapping 的 `null:` 语义、ref-struct 互操作、与 robust-mapping 共享机制）；"自然抽象"形态的 OPEN QUESTION 在组合路线回访前不猜测。
- **设计探索**：组合路线 vs 正面 ref struct（C）的交叉评估——把"解决 ref struct 即关闭本动机"写进 ref struct 工作项的输入；JSON 键大小写映射策略（VB 不敏感 vs JSON 敏感，归 mapping spec）；async 序列化器中 ref struct 不可跨 `Await` 捕获的具体影响（这是 `JsonNode` 路线最有力的技术论据）。

---

## 附录：C# 生态与互操作考量

> 本附录记录本提案与 dotnet/csharplang 官方仓库（`..\..\..\csharplang`，main 分支）所反映的 C#/CLR/.NET 生态现实方向的对应关系，供 VBScript.NET（.vbx）适应评估用。引用纪律：C# 原文逐字 + 标来源路径（`proposals\...` / `meetings\...`）；无法在 csharplang 仓库核实到逐字规格的生态事实标 `Probably`；无法核实的标 **Suspect** / 列入 **OPEN QUESTIONS**。
>
> **关系强度声明**：本提案（JSON 序列化器）与 C# interop 的关系是**经生态/库层中介**，而非语言级互操作特性——C# 生态的 JSON 序列化发生在 BCL 库层（`System.Text.Json`）+ source generation，**不是语言特性**（索引 T5/T6）。因此本附录聚焦"序列化的生态现实如何作用于本提案的判定"，不硬凑语言级 interop 点。

### 相关 C# 现实方向

**1. C# 生态的 JSON 序列化在库层 + 源生成器，非语言特性（T5/T6）。** C# 语言本身从未把 JSON 序列化做成语言特性；`System.Text.Json` 属 dotnet/runtime（BCL），其 source generator 是 AOT/trimming 友好出口。csharplang 仓库内对 JSON 的直接讨论极少，且都发生在"serialization 作为类型系统 / 扩展的用例"层面。`JsonSerializer`/`JsonSerializable` 的逐字规格不在本仓库——属 dotnet/runtime 生态事实，标 `Probably`。

**2. `Utf8JsonReader`/`Utf8JsonWriter` 是 C# 生态里 ref struct 的"招牌"用例（T2）。** C# 11 的 `low-level-struct-improvements.md`（ref fields + `scoped` + `[UnscopedRef]` 的原始提案）在 "Existing samples" 一节把这两个类型列为样板，并逐字描述其 span 安全摩擦：

> This particular snippet requires unsafe because it runs into issues with passing around a `Span<T>` which can be stack allocated to an instance method on a `ref struct`. Even though this parameter is not captured the language must assume it is and hence needlessly causes friction here.（逐字引用）→ `proposals\csharp-11.0\low-level-struct-improvements.md`

未来的 `proposals\expand-ref.md` 仍在用 `ref struct Deserializer { ref scoped Utf8JsonReader reader; ... }` 作为 ref field 到 ref struct 的设计示例（→ `proposals\expand-ref.md`）。这印证了本提案引用的轶事——"人们为 ref struct 辩护时最常举的例子是用 `Utf8JsonReader` 写自定义序列化器"——在 C# 生态内部同样真实且持续。与之互补，C# 13 的 ref-struct-interfaces 提案逐字说明了 ref struct 的抽象障碍：

> The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET.（逐字引用）→ `proposals\csharp-13.0\ref-struct-interfaces.md`

这正是"`Utf8JsonReader` 路线难以被抽象包装"的底层原因，也是 VB 需要"自然抽象"（而非 ref struct 本身）的生态侧旁证。

**3. AOT/trimming 对"反射型序列化"的持续压力（T5）。** C# LDM 明确把"信息在类型系统之外、靠反射影响运行时"的场景视为 AOT 难点。LDM-2023-07-24 讨论 interceptors 时逐字写道：

> This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible.（逐字引用）→ `meetings\2023\LDM-2023-07-24.md`

反射型序列化（含 `JsonSerializer` 默认基于反射的映射）正属这类场景；`System.Text.Json` 的 source generator 路线正是把"运行时反射发现的信息"搬到编译期（`.NET 6+`，`Probably`）。

**4. .NET AOT 目前只支持 C#——跨语言是明确阻碍（T5）。** LDM-2023-10-04（Trimming and AOT 专场）逐字记录：

> Currently, .NET AOT only works with C#, which is a detriment to the feature.（逐字引用）→ `meetings\2023\LDM-2023-10-04.md`

同场还指出语言特性缺乏运行时表示会加剧跨语言摩擦：

> Any further addition of language features that doesn't have real runtime representation makes it harder to work with from a cross-language perspective（逐字引用）→ `meetings\2023\LDM-2023-10-04.md`

这两条对 VBScript.NET 直接相关：.vbx 编译产物若走 NativeAOT，等于要求一个"C# 专属"工具链同时服务 VB——结构性障碍，非本提案能单独解决，但决定"默认安全 / 按需动态"分层的取舍。

**5. C# 15 正考虑"用扩展把序列化模型放回类型系统"（T8）。** LDM-2023-10-04 记录了把 Rust/Swift 的 trait 式反序列化引入 C# 的讨论：

> The serialization models adopted by newer systems, particularly Rust and Swift, are attractive, and potentially doable in C# when we add extensions that can implement interfaces on unowned types.（逐字引用）→ `meetings\2023\LDM-2023-10-04.md`

并让 extensions 工作组把序列化列为潜在用例。C# 在序列化方向的前进动力是**类型系统 + 扩展 + source-gen**，不是专用 DSL、不是编译器内置序列化器——与本提案 RESOLUTION #5 的两条禁令同向。

**6. dynamic / 晚期绑定不是 C# 的前进方向（T7）。** C# 的 `dynamic`（C# 4）长期无大演进；LDM-2024-04-24 当日引言（Quote of the day）逐字为：

> Once people are in dynamic-land they've already chosen（逐字引用）→ `meetings\2024\LDM-2024-04-24.md`

语境是收紧 dynamic 绑定规则。对本提案的直接意义有限，但印证"JSON 读取半场用 `!` / `JsonNode`（普通类、强类型化 DOM）而非动态对象"与 C# 生态同向。

### 现实 vs 提案

- **A 组合路线（字面量 + `ShapeOf` + `JsonSerializer`）——兼容。** C# 的分层同样是"库（`System.Text.Json`）+ 源生成器 + 数据形状表达力"，无专用序列化语法；`JsonNode` 是普通类，避开 ref struct，正好不踩 C# 的 span 安全约束（T2）。读半场的 `ShapeOf` 形状校验**没有** C# 对应物——这是 VB 差异化特色，但作用于 `JsonNode` / 映射层之上，不冲突。
- **B 运行时库 / 源生成器——完全兼容，且是 C# 生态主方向（T6）。** `System.Text.Json` 的 source-gen 正是 AOT 压力（T5）下的首选出口；本提案把流式残留交给源生成器，与 C# 现实重合。**需桥接**：VB 源生成器须生成与 C# 元数据互通的逐字段代码，并消费 C# 侧 `JsonSerializerContext` 派生类型（`Probably`：具体元数据形状需到 dotnet/runtime 核实）。
- **C 正面解决 ref struct——与 C# 现实"同行"但方向背离"VB 自然抽象"。** C# 已在 C# 11–14 及未来（expand-ref、first-class-span-types）持续投资 ref struct 工具链，`Utf8JsonReader` 始终是招牌用例——C# 的选择是"把 ref struct 做得更好用"，不是"给 VB 造一个抽象"。本提案把 C 判为"能让 C# 惯用法在 VB 可用、但不满足自然抽象"，与 C# 现实一致。
- **D 专用序列化 DSL（`&=` writer）——脱节（良性）。** C# 生态没有这种 DSL；`Utf8JsonWriter` 是编程式 API。RESOLUTION #4 的否决与 C# 现实一致——C# 都未发明序列化 DSL，VB 更无理由。这是"现实 vs 提案"里唯一的**脱节**点，且双方都不做。
- **编译器内置反序列化器（json-literals A 路径）——脱节且危险。** C# 明确不把 JSON 映射规则放进编译器；LDM 在"把序列化模型放回类型系统"时仍走 extensions / source-gen，而非语言内置（见现实方向 5）。RESOLUTION #5 禁令的生态依据充分。

**冲突点（如实记录）**：
1. **反射型映射 vs AOT**：A 组合路线的强类型映射委托运行时 `JsonSerializer`（默认反射）；NativeAOT / trimming 下这条路径本身受压（T5）。VBScript.NET 若愿景含 AOT，须把 source-gen 当默认、把运行时反射序列化当显式 opt-in。
2. **跨语言 AOT 现状**：LDM-2023-10-04 明言 .NET AOT 目前只支持 C#（见现实方向 4）——.vbx 编译产物的 AOT 化是**结构性障碍**，非本提案能单独解决；"默认安全 / 按需动态"分层（编译产物类型化 + source-gen；解释/动态模式 opt-in）可把冲突范围最小化。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：编译脚本默认走 `JsonSerializer` + source-gen 的强类型路径（RESOLUTION #2 的强类型映射半场）；`Option Strict Off` / 晚期绑定（含 `!` 的宽松用法）限定在显式脚本 / 交互模式，避免把反射负担带进编译产物。
- **source-gen 桥**：在 `proposal-runtime-library.md` 的设计面中明确"VB 源生成器生成与 `System.Text.Json` 兼容的逐字段读写代码"，并要求能与 C# 生成的 `JsonSerializerContext` 派生类型互操作（`Probably`：到 dotnet/runtime 核实元数据形状与 AOT 兼容清单，作为激活信号 (d) 的借镜）。
- **识别新元数据**：VB 编译器需能识别 C# 侧伴随 ref struct / unsafe-evolution 出现的元数据（`RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute`，见决策文件 M8；unsafe-evolution 明言 VB 无需 *requires-unsafe*，但**必须认识**这些属性才能正确校验对 C# 成员的调用安全）。对本提案直接影响小——`JsonNode` / `JsonSerializer` 是普通类——但自定义序列化器与 C# 低层互操作的边界会碰到。
- **不重造序列化器**：RESOLUTION #5 两条禁令（非编译器内置、不复活 `&=`）被 C# 生态现实加固——C# 自己都没把 JSON 做成语言特性，VB 更应保持"数据形状在语言、映射在库/源生成器"的分层。

### 对既有 RESOLUTION / 三态判定的影响

- **无实质影响，且被 C# 现实加固。** C# 生态对 JSON 序列化的分层（库 + source-gen + 数据形状，无语言特性）独立验证了本场核心判定：
  - RESOLUTION #1（不单独激活）：C# 都未把序列化器做成语言特性，进一步支持"不发明特性"。
  - RESOLUTION #2（自然抽象 = 组合）：与 C# 分层一致。
  - RESOLUTION #3（流式残留 → B 源生成器 / C ref struct）：T5/T6 表明源生成器是 AOT 压力的主出口，B 路线优先级保持。
  - RESOLUTION #4（不复活 `&=`）：C# 无 DSL，脱节点良性。
  - **三态 Table（保持搁置）：维持。** 无信号改变判定。
- **激活信号 (d) 升级为关键闸门**：RESOLUTION #7 的 (d)（"System.Text.Json 源生成器经评估仍不能覆盖该场景"）正是 AOT/trimming 压力下最需持续盯的生态点——若 source-gen 把流式残留也覆盖，本提案剩余动机进一步萎缩；若不能，则 B 路线在 runtime-library 内补充。建议把 (d) 的评估材料列为 `TODO` 并纳入回访议程。

### 引用纪律与 OPEN QUESTIONS

- 本附录引用的 C# 原文均经 `..\..\..\csharplang` 逐字核实并标来源（见上各段 `→ proposals\...` / `→ meetings\...`）。`System.Text.Json` source generator 的具体能力与元数据（`JsonSerializable`、`JsonSerializerContext`、AOT 兼容边界）**不在 csharplang 仓库**，属 dotnet/runtime 生态，全部标 `Probably`，未逐字核实。
- `OPEN QUESTIONS`：`System.Text.Json` source generation 在 .NET 6+ 的确切能力清单（自定义转换器、AOT 兼容、`JsonSerializerContext` 元数据形状）——需到 dotnet/runtime 核实，作为激活信号 (d) 的借镜清单。
- `OPEN QUESTIONS`：NativeAOT 对"非 C# 语言编译产物"的支持现状（LDM-2023-10-04 仅记录"C# 专属"是阻碍，未含路线图）——直接影响 .vbx 编译产物 AOT 化的可行性评估。
- 本附录与决策文件 M2（dynamic / COM 与 AOT 张力）、M5（runtime-library / scripting 与 source-gen / AOT 摩擦）交叉引用，是它们在"JSON 序列化"主题上的实例化。
