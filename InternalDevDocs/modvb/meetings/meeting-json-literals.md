# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上周 JSON 模式匹配与 XML Schema 类型已经过了一轮初读，这次我们回到它的姊妹建议——JSON 字面量与目标类型创建。会议开头我们预计这是一次轻松的设计评审；结束时我们发现这份建议把一个价值真实的"数据字面量"想法和一个范围没有边界的"编译器内置序列化器"想法绑在了一起，还顺手带上了一个语义可疑的 `&=` 重载。我们花了大量时间拆解这三件事，并对照主线 vblang #101（2017 年那场关于 JSON Literals 的真实讨论）逐条校验我们的直觉。

## Agenda

* [Proposal: JSON 字面量与目标类型创建（JSON Literals & Target-Typed Creation）](#proposal-json-字面量与目标类型创建)

## Proposal: JSON 字面量与目标类型创建（JSON Literals & Target-Typed Creation）

_Related: [vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；[vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)；[vblang #27 – Guid literals](https://github.com/dotnet/vblang/issues/27)；ModVB：`proposal-json-pattern-matching.md`、`proposal-xml-schema-types.md`、`inactive/proposal-target-typed-conversions.md`、`inactive/proposal-robust-mapping.md`、`inactive/proposal-json-serializers.md`_

### 场景与缺口

We started from the proposal's own motivation，它一点也不新鲜——主线 2017 年就说过几乎相同的话：

> JSON is the _lingua franca_ of the cloud. First-class JSON support could be a strong attractant for first-time developers. The power of copy/paste/modify to jump start a project can't be overstated.（vblang #101，2017.10.18 会议，逐字引用）

场景是真实的：初始化测试数据、为服务构造参数、序列化输出，数据以 JSON 形态书写最直观，而今天 VB 需要手写集合初始化器或逐字段调用 API。样板代码的确冗长：

```vb
' 今天：手写对象初始化器 + 集合初始化器逐层铺开。
Let dbContext = New InMemoryDbContext()
Let emp1 = New Employee()
emp1.FirstName = "John"
emp1.LastName = "Doe"
dbContext.Employees.Add(emp1)
Let emp2 = New Employee()
emp2.FirstName = "Anna"
emp2.LastName = "Smith"
dbContext.Employees.Add(emp2)
```

但我们也想起 2017.08.23 会议上关于 `!` 字典访问的评论：

> The JSON thing is great. Oh, but it already works with ! so all the value just evaporated.（2017.08.23 会议，Late-bound Member-Access expressions 一节，逐字引用）

这句评论针对的是"读取"半场——用 `!` 从 `JObject`/字典取数据。今天缺的只是"**构造**"半场：以 JSON 形态把数据写下来。构造半场该怎么设计，正是本次会议的全部内容。

### 候选方案

**PROPOSAL A — 完整照搬 Anthony 第 5 章。** `{ ... }`/`[ ... ]` 是 JSON 字面量，同时支持两种用途：① 目标类型化对象创建（`Let dbContext As InMemoryDbContext = { "employees": [...] }`）；② `&=` 写给 writer/序列化器（`response &= { "firstName": e.FirstName, ... }`），不物化中间对象。

**PROPOSAL B — JSON 数据字面量，原生类型为 `JsonObject`/`JsonNode`。** 仿照 XML 字面量先例（XML 字面量 → `XElement`/`XDocument` 构造调用，spec `expressions.md` §XML Literal Expressions）：`{ ... }` 是数据字面量，其原生类型是 `System.Text.Json.Nodes.JsonObject`（或 `JsonNode`），`[ ... ]` 是 `JsonArray`，值位置是任意 VB 表达式。目标类型化 POCO 创建留作后续（Table），`&=` 完全剥离。

**PROPOSAL C — JSON 字面量 + 显式转换。** 字面量只构造 `JsonObject`（同 B），但要创建 `InMemoryDbContext` 必须显式写 `Deserialize(Of InMemoryDbContext)({ ... })` 或 `{ ... }.ToObject(Of InMemoryDbContext)()`。不做目标类型推断，编译器只负责把字面量降级为 `JsonObject` 构造调用。

**PROPOSAL D — 仅 `&=` writer 流式形态。** 不做对象创建，只让 `response &= { ... }` 把 JSON 片段写给 writer/序列化器；Anthony 在 18.7 里把它当作"更健壮的映射"（`jsonWriter &= { "firstName": person.Name.Given, ..., null: person.EmailAddress }`）的一部分。

**OTHER DESIGNS CONSIDERED**

- **字典初始化超集路线（主线 #101 的直觉）**：把 `{ "k": v }` 视为 `New With { k := v }` 的字典化扩展（2014.02.17 会议曾讨论 `New With { arg1 := "hello", arg2 := "world" }`，结论是 "Haven't got to this. Don't think it's worth it."）。主线 2017 年对 #101 的反馈明确说：*"I like that it's so close to a general dictionary init syntax that it may be better thought of as that"*。这条路线把"JSON 字面量"降格为字典初始化器的一种形态，键用 `:=` 而非 `:`——VB 味更足，但**不再能原样拷贝粘贴 JSON**。
- **纯反序列化 API 糖衣**：不引入任何字面量语法，只加 `Deserialize(Of T)` 的友好重载。零文法成本，但也零"数据字面量"体验。
- **Schema 标注类型**（`Let employee As {"http://.../employee"}`）：来自 json-pattern 建议，是给 JSON 数据附加类型信息供 IntelliSense 使用的"注解"路线，2017.10.18 会议已有讨论（`Dim a As {"contact"}`），结论是 *"We might be able to do a lot with no compiler changes and should investigate with IDE team."*——We 认为这是与字面量正交的另一条线，不并入本建议。

### 权衡：Q&A

- **A 的核心卖点到底是哪个？** 我们把 Anthony 的示例拆开看：`Let dbContext As InMemoryDbContext = { "employees": [...] }` 的卖点是"编译器按目标类型把 JSON 变成强类型对象"；`response &= { ... }` 的卖点是"流式、零物化"。这两个卖点之间没有任何共享机制——一个是编译器内置反序列化器，一个是运算符重载。**We think：这是一份建议捆绑两个强无关能力**，按评价标准是典型的弱提案红旗。拆开后各自的价值和成本才看得清。
- **A vs B：目标类型化 POCO 创建值得做吗？** 主线 2017 年就在最朴素的多层嵌套处卡住了——*"The problem is the nested case. How do we know the List type? Do you restate the type or have a separate list syntax."*（#101，逐字引用）。`{ "employees": [ {...}, {...} ] }` 里，编译器要决定 `"employees"` 对应 `InMemoryDbContext.Employees`（集合属性）、元素类型、每个 `{...}` 如何变成 `Employee`、`"firstName"` 如何匹配 `FirstName`、缺失/多余属性怎么处理、`null` 怎么办、大小写策略是什么、要不要支持默认值/多态。把这些问题答完，等于**在编译器里重写一遍 System.Text.Json**。运行时 `JsonSerializer.Deserialize(Of T)` 已经用远低得多的成本做完了这件事。We 不认为编译器内建序列化器是语言特性该做的事。
- **B 的 XML 先例有多强？** 很强。VB 是唯一把 XML 写成字面量的主流语言，这是真正的 VB 基因（spec 有整节 XML Literal Expressions，含嵌入式表达式 `<%= %>`、免行延续、缺 `System.Xml.Linq` 时编译报错等既定规则）。JSON 字面量是这个传统的自然延续：数据标记语言的构造形态。而且 JSON 比 XML 更简单——键是字符串字面量，值位置天然就是 VB 表达式，**根本不需要 `<%= %>`**（#101 反馈对嵌入式语法的意见是 _No strong feelings_，因为值就是表达式）。B 把 JSON 字面量的原生类型定为 `JsonObject`，正对应 XML 字面量的原生类型 `XElement`。
- **C 是不是更诚实？** C 把"目标类型化"降级为显式转换。它保留 B 的所有好处（数据字面量、拷贝粘贴），只是拒绝编译器猜测映射。We like 这一点——Anthony 自己在 `proposal-target-typed-conversions.md` 里对目标类型化转换都写了 *"I think this will cause fist fights"*，目标类型化**对象创建**比目标类型化**转换**更激进，因为它连转换规则都要编译器发明。但 C 的代价是 `InMemoryDbContext` 示例失去魔力，样板从"手写初始化器"变成"手写 `Deserialize` 调用点"——样板省了一半，不是全部。**We 认为 C 是 B 的配套形态，不是独立方案**：先给 B（字面量），再讨论是否给目标类型化创建。
- **D（`&=` writer）为什么被质疑？** 三个理由。第一，`&=` 今天在 VB 里的语义是字符串/字符数组连接赋值，把它重载成"写给 writer"，是原则 #7（避免隐蔽的控制流/语义变化）的正面教材——同一符号，两种完全不相关的语义，还是重载在语言级算子上。第二，`response` 的类型在建议里完全没有定义（writer 接口？具体序列化器？），`&=` 的重载机制（对什么类型、怎么调度）也没有定义。第三，Anthony 自己在 18.7 里用 `null: person.EmailAddress` 表示"有意忽略的成员"——**`null` 不是合法 JSON 键**，说明这个形态本质上是"序列化 DSL"，不是 JSON 字面量。We 认为 D 属于 `proposal-robust-mapping.md` 的地盘，应从本建议剥离。
- **A vs B：值位置的表达式怎么求值？** 两种方案都要求值位置是任意 VB 表达式（`e.FirstName`、LINQ 查询）。这在 B 里简单：每个值表达式求值后作为 `JsonObject` 的属性值（`JsonValue`/装箱）。在 A 里则每个值表达式要先做"目标成员类型"的转换——嵌套越深，求值顺序与转换时机越复杂。**We 不反对值位置表达式本身，我们反对的是"每个值都要再走一层目标类型转换"。**
- **`{ }` 的语法负担谁来付？** 见下方拷问第 1 条。这里先给结论：`{` 在 VB 里已经有四种含义（数组字面量、`With` 成员初始化器、`From` 集合初始化器、未来的 schema 标注类型），加 JSON 对象是第五种。B 至少让"裸 `{ ... }` 在表达式位置"的含义从一种（数组）变成两种（数组/JSON 对象），靠冒号区分；A 则让同样的裸 `{ ... }` 还要依目标类型决定是数组、JSON 对象、还是"POCO 的映射启动器"。**范围越大，文法越重，这正是 A 比 B 更危险的地方。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`{` 在今天的 VB 里已经有四种含义，本建议要加第五种，而 json-pattern 建议还要加第六种（schema 标注 `{"http://.../employee"}`，花括号里一个字符串字面量）：

```vb
Dim a = {1, 2, 3}                             ' ① 数组字面量（既有）
Dim b = New List(Of Integer)() From { 1, 2, 3 } ' ② 集合初始化器（既有，From 之后）
Dim c = New Address() With { .City = "Peoria" } ' ③ 成员初始化器（既有，With 之后）
Dim d = { "city": "Peoria" }                  ' ④ 新的 JSON 对象字面量？
Dim e As {"http://.../employee"} = ...        ' ⑤ schema 标注类型（json-pattern，花括号里一个字符串）
```

区分 ① 与 ④ 的关键是冒号：`{ "a", 1 }` 是数组字面量（无公共类型→今天报错），`{ "a": 1 }` 有冒号→今天直接解析失败（冒号在数组字面量里非法）。**所以 ④ 的新语义主要是增量的**——但我们不放心两处：

- **空 `{}`**。`Dim x As Object = {}` 今天合法（空数组 → `Object`）。加了 JSON 语义后它是空数组还是空 JSON 对象？We 的意见：**默认保持现状（空数组）**，JSON 空对象写 `New JsonObject()`，或者依目标类型消歧（目标类型是 `JsonObject` 才解释为空对象）。这个规则必须写进 spec。
- **`[` 与转义标识符**。`[foo]` 是转义标识符。`[ "a": 1 ]` 作为 JSON 数组值出现时，词法器要在"表达式位置出现 `[`"时区分"转义标识符"与"JSON 数组"。Roslyn 的词法/语法是上下文相关的，能做，但要专门设计。We 担心这比看起来费劲。

键的定界符本身也是文法决策：JSON 原样 `:`（拷贝粘贴保真） vs VB 化 `:=`（字典初始化路线，2014 年 `New With { arg1 := "hello" }` 的先例）。详见 OPEN QUESTIONS。

#### 2. 角案例 / 边界语义

- **顶层数组**。#101 反馈说过：*"If you want a top-level array we should drop the square brackets"*（逐字引用）——顶层数组要不要 `[ ... ]`，还是裸写。B 里顶层 `[ ... ]` 就是 `JsonArray` 字面量，We 倾向保留，但要与"表达式位置 `[` = 转义标识符"的歧义一起定。
- **嵌套 List 类型推断**。这是 #101 卡住的原点。B 里不存在该问题（`JsonArray` 的每个元素都是 `JsonNode`）；A 里存在且未解决。`Probably`：A 若要复活，必须从"目标成员的集合属性元素类型"或"显式重述类型"二选一——这正是 #101 问的 *"Do you restate the type or have a separate list syntax"*。
- **重复键、尾随逗号、字符串转义**。JSON 标准不允许重复键与尾随逗号；VB 的集合初始化器/数组字面量允许尾随逗号吗？（`{1, 2,}` 今天非法。）键重复时报错还是后者覆盖？JSON 转义（`\"`、`\n`、`\uXXXX`）与 VB 字符串字面量的转义规则是否混用——**字面量里的字符串是 JSON 语义还是 VB 语义**？这些细节建议里一个都没提。
- **多行**。XML 字面量免行延续（spec 明确：XML 字面量内换行是空白，不需要 `_`）。JSON 字面量是否仿效？如果要支持 Anthony 那种缩进排版的多行对象，答案是必须仿效，否则每个逗号后都要写 `_`——那体验就毁了。
- **空值与 `Nothing`**。`{ "a": Nothing }` 表示 JSON `null` 还是缺省值？值类型 `Nothing` 与引用 `Nothing` 在 `JsonValue` 里怎么区分？
- **求值时机**。`{ "subtype": GetType(DayOfWeek).GetEnumUnderlyingType().Name }`（#101 原例）里，值表达式在构造点一次性求值——We 认为这是唯一的合理语义，但建议原文没有写。

#### 3. 作用域与绑定

- B 路线下，`{ ... }` 的语义模型类型是 `JsonObject`，键不绑定任何符号——它们只是字符串，属性访问走 `!` 或 `Item`。语义模型返回 `JsonObject`，简单、无绑定歧义。
- A 路线下，"`"employees"` 绑定到 `InMemoryDbContext.Employees` 属性还是 backing field"，取决于映射规则的绑定目标；属性 getter/setter 是否存在、`ReadOnly` 集合怎么处理，全是开放问题。We 在 TypeOf 流分析会议上刚处理过"绑到 property 还是 backing field"的同类问题——那里的教训是：**一旦绑定语义不透明，IDE 和语义模型的呈现都会跟着不透明**。

#### 4. 与既有特性的交互

- **四种 `{` 含义共存**：见第 1 条。文法演练 + IDE 括号着色是必须的。
- **`!` 字典访问**：2017.08.23 的评论说 JSON 读取的价值"已被 `!` 蒸发"。本建议若只做构造半场，恰好与 `!` 互补而不重叠；若做 schema 标注（json-pattern），则 `!` 要按 schema 补全——那属于 json-pattern 建议，这里不展开。
- **XML 字面量**：同族先例，机制上最可移植（构造调用降级、免行延续、缺类型时报错）。We 明确要求 B 的规格逐条对照 XML 字面量规格写。
- **`&=` 字符串连接**：如果 D 形态复活，`&=` 的语义就从"拼接"变成"写片段"，且要与 `StringBuilder` 的既有 `&=` 行为（Anthony 第 6 章提出 `builder &= "Line" & vbCrLf`）协调。我们不认为值得为一个 writer DSL 污染 `&=`。
- **匿名类型 / `With` 初始化器**：`New With { .a = 1 }` 与 `{ "a": 1 }` 都是"键值对集合"，语法相邻而语义不同（前者是匿名类型成员，后者是 JSON 属性）。IDE 补全、重构、错误提示都要区分。这里也是主线"字典初始化"直觉的落点——如果用户分不清 `With { .x }` 和 `{ "x": }`，说明设计没有划清边界。
- **Late binding / Option Strict Off**：宽松模式下 `Dim x = { "a": SomeLateBoundExpr }`，值位置是晚期绑定表达式，字面量本身仍按 `JsonObject` 构造。We 认为这可行，但必须与 TypeOf 收窄会议的规则一致：**新语法不得改变先前已成功绑定的代码**。

#### 5. Breaking change 与兼容性

好消息：`{ "a": 1 }`（带冒号）今天在 VB 里是解析错误，新增语义**几乎不改变任何现有合法程序**。需要专门排查的只有一个：**空 `{}` 的目标类型消歧**。`Dim x As Object = {}` 今天编译为"空数组 → `Object`"，若某天在 `JsonObject` 目标下它变成空 JSON 对象，则 `Dim j As JsonObject = {}` 从"类型不匹配错误"变成"合法"——这是增量，不是破坏。真正的破坏面在 A：如果目标类型化创建复活，`Dim db As InMemoryDbContext = { "employees": [...] }` 从"类型不匹配错误"变成"合法映射"，而映射规则（大小写、缺失成员容忍度）若随版本漂移，重编译会改变行为。**这是把 `langversion` 门控与警告策略写进 mapping spec 的原因。** 建议原文完全没有 Compatibility 章节——这是品质上的实质缺口。

#### 6. Option Strict / 编译选项分叉

B 路线的核心路径（构造 `JsonObject`）在 Strict On/Off 下应行为一致：值表达式按各自模式求值（Strict Off 下晚期绑定），字面量降级为构造调用不变。A 路线的映射转换在 Strict On/Off 下会分叉——Strict On 要求值可转换到成员类型，Strict Off 允许晚期绑定/`Object` 兜底。We 认为这正是"A 需要单独设计"的又一个证据：**两条路径行为一致是硬要求，而映射转换天然不一致**。

#### 7. IDE / IntelliSense 影响

- **键补全**：B 里 `{` 后输入 `"`，IDE 无法补全键（键是开放字符串）——除非目标类型已知且是 POCO，那可以按成员名补全（这是 A 唯一真正优于 B 的杀手级场景，但代价是映射规则）。对 `JsonObject` 目标，IDE 只能给通用键提示，价值有限。
- **括号匹配与着色**：五种 `{` 含义要求 IDE 正确识别上下文。We 明确：没有 IDE 支持的语言特性不发布。
- **`!` 补全**：schema 标注路线的 `jsonObject!` 补全（Anthony 18.x 提过）依赖 json-pattern / xml-schema 建议，这里只留接口。
- **JSON 语义着色与格式化**：字面量内应享受 JSON 语法高亮（键/值/字符串），这需要编辑器识别"这是 JSON 字面量"。

#### 8. 数据 / 普遍性

- 支持方数据：*"First-class JSON support could be a strong attractant for first-time developers"*（#101 引文）是唯一的"数据"，且是引述而非统计。测试数据初始化、服务参数构造、序列化输出是真实场景，但"数十万安静客户"的业务代码里，这三类场景的占比没有量化。`Suspect`：这是真实 DX 增益，但不是头条特性。
- 反方数据：`JsonSerializer.Deserialize(Of T)`、`JObject.Parse` 已是成熟替代，说明"JSON → 对象"的痛点已被运行时缓解了一半。构造字面量的增量价值是"省掉 Parse 调用 + 编译期形状校验"——这个增量没被量化。
- 首次开发者吸引论与范围成反比：**拷贝粘贴 JSON 就能跑的体验**（B，数据字面量）最吸引新手；**把 JSON 变成 POCO**（A）是进阶用户的需求。We 认为首次开发者论点反而支持 B 不支持 A。

#### 9. 更简替代

- `JsonSerializer.Deserialize(Of T)` / `JObject.Parse`：现状。成本最低，映射规则由运行时库维护（有成熟的 `JsonSerializerOptions`：大小写策略、缺失成员、循环引用、多态——全是编译器版要重新发明的）。
- `!` 字典访问：已覆盖读取半场（2017.08.23）。
- Collection initializer + `With`：已覆盖强类型构造，但样板冗长——这正是本建议的动机，成立。
- **Analyzer**：可以用 analyzer 校验"这个字符串是不是合法 JSON"，但 analyzer 不能把 JSON 变成对象图，当脚手架可以，不能替代。
- **结论：替代方案强，尤其是运行时序列化器。B 必须论证"编译期形状校验 + 数据字面量体验"比"运行时 Deserialize"多出的价值，值得文法成本。**

#### 10. 复杂度 / 成本 / 优先级

- A 完整实现 ≈ 编译器内置反序列化器：映射规则（大小写、缺失/多余、嵌套集合、默认值、`null`、多态、转换器）+ 错误报告 + IDE 补全 + `langversion` 门控。成本量级接近一个独立的序列化器团队工作量。**不值得在这个建议里做**。
- B 数据字面量 ≈ 仿 XML 字面量：文法 + 降级为 `JsonObject`/`JsonArray`/`JsonValue` 构造调用 + 键的常量/表达式校验 + IDE 支持。成本中等，XML 字面量是现成的实现模板。
- D 的 `&=` ≈ 运算符重载 + writer 契约设计，成本中等但语义风险高。
- 优先级：低于模式匹配、可空性流分析（它们在 2026 年语境里是更大的主线）；在 ModVB 内部，与 json-pattern、xml-schema 共享 shape 文法，应先定 shape 文法再做字面量。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题：B 降级为 `System.Text.Json.Nodes` 的构造调用，等同 XML 字面量降级为 `XElement` 构造调用（spec 原文：*"The values are generated through constructor calls translated from the XML literal expression"*）。表达式树里 JSON 字面量不可用（构造调用不可进表达式树），与 XML 字面量一致。不触达 CLR 存储规则。`Suspect`：`JsonObject` 在 .NET 8+ 的 `JsonNode` 是类（引用类型），无 `ref struct` 约束问题；`Utf8JsonWriter` 是 `ref struct`，只能在 `&=`/D 形态里接触——这是 D 与 A/B 的又一个断层。

#### 12. 值不值得做

- **价值**：数据字面量体验 + 首次开发者吸引 + 消除测试数据样板（B 的范围）——中高。
- **成本**：B 中等（XML 先例可移植）；A 巨大；D 中高且语义风险高。
- **风险**：`{` 五义文法冲突、`&=` 语义污染、映射规则爆炸（A）——A 和 D 的风险没有对冲设计。
- **We 的判断**：**B 值得做，A 的 POCO 目标类型化 Table，D Reject 出本建议。** 若坚持把 A/B/D 绑在一起做，我们会建议整份 Table。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：JSON 字面量的全部论据都在这一条——拷贝粘贴/修改即可启动项目（#101）。B 正中靶心。
- **消除常见样板（原则 #9）**：测试数据初始化样板真实，B 用最小语法解决（85% 场景优先）。A 把样板从"手写初始化器"搬到"编译器内序列化器"——样板没有消失，只是移动了。
- **不引入"第二种做事方式"（原则 #3）**：**扣分项**。对象构造已经有 `New` + `With` 和 `From` 集合初始化器，JSON 字面量是第三种数据形态——但 XML 字面量先例说明"数据标记语言字面量"在 VB 里是**自成一类的既有类别**，不是对象构造的重复。B 把它归入"数据字面量"类别，规避了"第三种构造方式"的批评；A 把它和 `New` 直接竞争，避不开。
- **避免隐蔽的控制流/语义变化（原则 #7）**：D 的 `&=` 是这条原则的正面教材——**拒**。B 无此问题（构造调用，语义透明）。
- **保持 VB-like（原则 #2）**：争议最大。`{ "a": 1 }` 用冒号，VB 没有冒号做键值分隔的先例（VB 用 `:=` 命名实参）。JSON 原样保真（`:`）与 VB 化（`:=`）是真实分叉——主线 #101 的"字典初始化"直觉倾向 `:=`，但"拷贝粘贴 JSON"论据要求 `:`。We 尚未定论（见 OPEN QUESTIONS）。
- **默认跟随 C#（原则 #4）**：C# **没有** JSON 字面量。C# 12 有集合表达式 `[1, 2, 3]`（目标类型化集合构造，`Probably`——无法在本仓库核实到逐字规格），但那是集合不是 JSON。本特性是 VB 走在 C# 前面——偏离默认跟随原则，需要有"充分理由"，XML 先例 + 首次开发者论据是仅有的理由，We 认为在 B 范围成立。
- **不与既有语法冲突（原则 #8）**：`{` 四义变五义、`[` 与转义标识符——冲突面是本建议最大的技术债，B 最小化它（只新增一种裸 `{` 含义），A 和 schema 标注最大化它。
- **与主线关系（对照表 2.3）**：数据字面量方向与 vblang #101 讨论**主线一致**（#101 的模式匹配部分 2017 年已 Decision: Table；方向指向"字典初始化超集"）；目标类型化对象创建是**Anthony 独立延伸**（对照表"目标类型转换｜Anthony 独立延伸，自评'会打架'"的直接后继）；`&=` writer 无主线对应。**整份建议只有 B 的核心与主线共享直觉。**

### RESOLUTION:

1. **本建议捆绑了两个强无关能力**（目标类型化对象创建 + `&=` writer 流式），必须拆分。We 只在本建议内推进"JSON 数据字面量"（PROPOSAL B 的核心）。
2. **JSON 字面量的原生类型定为 `JsonObject`/`JsonArray`/`JsonValue`（`System.Text.Json.Nodes`）**，降级为构造调用，机制逐条对照 XML 字面量规格（spec `expressions.md` §XML Literal Expressions）。值位置是任意 VB 表达式，在构造点一次性求值。
3. **目标类型化 POCO 创建（`InMemoryDbContext` 示例）标 Table**：映射规则（属性名大小写、缺失/多余成员、嵌套集合元素类型、默认值、`null`、多态、转换器）整体未定义，等于要求编译器内置反序列化器；先与 `json-serializers` / `robust-mapping` 合并出一份 mapping spec，再谈语法。A 的杀手级场景（键补全）也在那时一并设计。
4. **`&=` writer 形态 Reject 出本建议**：`&=` 语义与字符串连接差异大（原则 #7）；`response` 类型与重载机制未定义；Anthony 18.7 的 `null:` key 非法，说明它是"序列化 DSL"，归 `proposal-robust-mapping.md`。`Utf8JsonWriter` 的 `ref struct` 问题也在该建议解决。
5. **文法安全线**：`{ "key": value }`（键为字符串字面量或常量表达式）进入表达式文法；空 `{}` 依目标类型消歧，默认保持空数组现状；`[ ... ]` 仅作为对象内的数组值，与转义标识符 `[foo]` 的区分需词法设计；五种 `{` 含义（数组 / `With` / `From` / JSON 对象 / schema 标注）做 spec 级文法演练 + IDE 括号着色。
6. **与主线的关系如实记录**：数据字面量方向与 #101 讨论一致（"general dictionary init ... superset of JSON"）；模式匹配半场随 #101/#139 的 Table 决定，由 `proposal-json-pattern-matching.md` 承接；schema 标注半场随 annotated-types（#184 邻域）走 IDE 调查路线（2017.10.18 已指出 *"We might be able to do a lot with no compiler changes"*）。
7. **键定界符（`:` vs `:=`）不在此次会议定死**——它决定"拷贝粘贴保真"与"字典初始化 VB 味"的取舍，需要一次专门的 shape-grammar 会议，与 json-pattern 的 `ShapeOf ... Is { ... }` 文法一起定。

### Implication:

- **重写建议**：拆分 `&=`，把目标类型化 POCO 标 Table，补文法小节（含 `{` 五义、`[` 转义标识符、空 `{}` 消歧、行延续）、补 Compatibility 章节、补 `langversion` 门控。
- **最小原型**：JSON 数据字面量 → `JsonObject`/`JsonArray` 构造调用（XML 字面量为模板）；验证语义模型返回类型、`!` 访问、IDE 键补全与括号着色。
- **shape-grammar 会议**：与 json-pattern、xml-schema 对表，一次定下 `{ ... }` 的构造语义与模式语义、schema 标注的 `{"uri"}` 形态，避免第六种 `{` 含义再撞车。
- **mapping spec**：为 A 的复活准备——属性名匹配（大小写策略）、缺失/多余成员、嵌套集合、`null`/`Nothing`、多态与转换器、`langversion` 门控与警告策略。
- **与主线对话**：把 B 的设计反馈给主线 #101（方向一致），把 `&=` 与 robust-mapping 的合并建议挂到对应提案。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：键定界符 `:`（JSON 保真）vs `:=`（字典初始化 VB 味）——待 shape-grammar 会议。
- `OPEN QUESTIONS`：空 `{}` 的目标类型消歧精确规则（默认空数组？`JsonObject` 目标才解释为空对象？）。
- `OPEN QUESTIONS`：键是否允许任意常量表达式（如 `Select {f.Name: CInt(...)}` 的 `f.Name`）——非字面量键与"字面量"概念的关系。
- `OPEN QUESTIONS`：字面量内字符串的转义语义（JSON 转义 vs VB 转义）与重复键/尾随逗号的处置。
- `OPEN QUESTIONS`：行延续——是否仿 XML 字面量免 `_`（We 倾向是）。
- `TODO`：量化"测试数据初始化/服务参数/序列化输出"在真实代码库中的样板占比，为普遍性补证据。
- `TODO`：对比研究 `System.Text.Json` 的 `JsonObject`/`JsonSerializerOptions` 行为，作为 mapping spec 的借镜清单。
- `Follow-up`：与 json-pattern 统一 shape 文法；与 xml-schema 统一"字面量 vs 标注类型"的边界；与 robust-mapping / json-serializers 合并 `&=` 与 mapping。

### 状态

- **LDM 状态：Consider（范围待重定）**；JSON 数据字面量（B）方向认可，目标类型化 POCO（A）Table，`&=` writer（D）Reject 出本建议。
- **三态判定：Consider** — 价值真实但范围未定；核心语义（映射规则）未设计；文法冲突需先做 shape-grammar 演练。不成熟为 Active，但也不至于 Reject（首次开发者吸引 + XML 字面量基因 + 主线 #101 未关闭）。

---

## 附录：特性评价

# 建议评价报告：proposal-json-literals.md

## 评价对象

- 建议：proposal-json-literals.md — JSON 字面量与目标类型创建（`{ ... }` 目标类型化对象创建 + `&=` writer 流式）
- 来源：Anthony 原文第 5 章 "JSON and JSON Pattern Matching"（`..\AnthonyDesign_wordpress.txt` L1208–1247：目标类型化创建、`&=` writer 模式；`&=` 亦见于 18.7 robust-mapping，L2920–2924）
- 配方目标：以 JSON 形态书写数据，由编译器按目标类型创建对象；`&=` 写给 writer 避免物化中间对象

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："改进不可衡量，示例不能演示改进"。Motivation 命名了三个真实场景（非泛泛），但核心示例均不可编译：`InMemoryDbContext` 的映射规则未定义、`response` 的类型与 `&=` 机制未定义；"消除样板"无量化口径 | 已检查 | 未决问题 4 个关键设计点 → 效果证据等级封顶（≤4 分）；无原型；示例停留在"宣称可编译" |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。目标类型化创建 + `&=` writer 是捆绑的两个强无关能力；`:` 键值分隔无 VB 先例（VB 用 `:=`），未做 VB 化改造；XML 字面量先例未被引用，且正是唯一能为本特性提供基因辩护的材料 | 已检查 | 与"字典初始化"（#101 直觉）未对齐；`&=` 与 18.7 `null:` key 使"JSON 字面量"名不副实 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、4 个未决问题具体诚实（如实列出是加分）；但无文法/BNF、无 Compatibility/breaking-change 章节、映射规则与 `response` 类型整体悬空、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 键定界符、空 `{}`、行延续等文法关键点均未触及；示例不能编译与"示例来自原文"自洽但暴露设计未完成 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风（`{` 五义文法冲突）、暗（`&=` 语义污染、映射规则爆炸）受损且文档未权衡、无对冲设计；水（差异化，C# 无此特性）与光（JSON 拷贝粘贴）正向 | 已检查（预测待定） | 无 Compatibility 分析；`langversion` 门控缺失；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注，标注与影响有偏差"。标注"原文示例（第 5 章）"正确；但未标注主线 #101 先例（方向、字典初始化直觉、Table 决定）、未标注 XML 字面量基因、`&=` 实际来自 5 章与 18.7 两处、`null:` key 的 DSL 本质未点破 | 已检查 | 借鉴的是 JSON 语法本身（非 C#），成分归属未说明；无杂质，但"JSON 字面量"名与 18.7 的 DSL 内容有偏差 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离**。一致：新手友好（原则 #5，JSON 拷贝粘贴）、消除样板（#9）、XML 字面量"数据字面量"先例（原则 #2 的辩护线）。偏离：`{` 冲突面（#8）、`&=` 隐蔽语义变化（#7，正面违反）、`:` 无 VB 先例（#2）、捆绑双能力（#3）。
- **与主线关系：分裂**。数据字面量方向与主线 #101 一致（且 #101 模式匹配已 Table、指向"字典初始化超集"）；目标类型化创建为 Anthony 独立延伸（对照表"目标类型转换｜自评'会打架'"的直接后继）；`&=` writer 无主线对应。与 json-pattern / xml-schema / robust-mapping / json-serializers 四个建议在同一 shape 文法上交互。
- **破坏性变更：基本无，但未论证**。`{ "a": 1 }`（带冒号）今天即解析错误，新语义增量；唯一存疑点是空 `{}` 的目标类型消歧（`Dim x As Object = {}` 现状为空数组）。A 若复活，`InMemoryDbContext` 从"类型不匹配"变"合法映射"，映射规则漂移会带来重编译行为变化——需要 `langversion` 门控。

## 总评

- **达成程度：部分达成**——"数据字面量"概念成立且有 XML 先例；但范围未定、核心语义（映射规则）未设计、`&=` 语义风险未对冲。
- **LDM 三态建议：Consider**——收敛为"JSON 数据字面量 → `JsonObject`"后值得进入设计管线；目标类型化 POCO（A）Table、`&=` writer（D）Reject 出本建议。
- **主要问题**：① 捆绑两个强无关能力，范围不清；② 目标类型化创建 = 编译器内置序列化器，成本/风险不可接受且映射规则整体未定义；③ `&=` 语义与字符串 `&=` 冲突，属 robust-mapping 的 DSL 而非 JSON 字面量；④ `{` 五义、`[` 转义标识符、空 `{}` 消歧等文法冲突未设计；⑤ 未引用主线 #101 先例与 XML 字面量基因，成分标注不全。

## 返工建议

- **补充章节**：文法小节（`{` 五义、`[` 转义标识符、空 `{}` 消歧、行延续、键定界符 `:`/`:=`）；Compatibility / breaking-change（空 `{}`、`langversion` 门控、警告策略）。
- **补充证据**：最小原型（JSON 数据字面量 → `JsonObject` 构造调用，仿 XML 字面量）；键补全与括号着色的 IDE 验证；测试数据样板的真实占比数据。
- **未决问题处理**：剥离 `&=`（归 robust-mapping）；目标类型化 POCO 标 Table 并附 mapping spec 清单（属性名匹配、缺失/多余、嵌套集合、`null`/`Nothing`、多态、转换器）；`{ }` 构造语义与 `ShapeOf ... Is { ... }` 模式语义、`{"uri"}` schema 标注在 shape-grammar 会议统一。
- **设计探索**：C# 12 集合表达式 `[1, 2, 3]` 的目标类型化集合构造与本特性的对照（`Probably`，待外部核实规格）；`!` 字典访问（2017.08.23）与本特性"构造半场"的互补关系；首次开发者吸引论为何支持 B 而非 A。

---

## 附录：C# 生态与互操作考量

> 本附录核对 dotnet/csharplang 官方仓库镜像 `..\..\csharplang` 中与本提案（JSON 字面量）相关的 C# 现实方向。正文「VB 基因对照」第 165 行把「C# 12 集合表达式」标注为 `Probably`（"无法在本仓库核实到逐字规格"）——本附录已逐字核实，可升级为已核实（见 A.1.2）。总体判断：C# 生态对「嵌入式数据字面量」的哲学是**只给框架中立集合形状（collection expressions）与不透明字符串载体（raw string literals）做语言特性，把「数据→对象」的序列化留给 BCL + source-gen**；本提案的 B（JSON 数据字面量）恰是 C# 没有的 VB 特色面。

### A.1 相关 C# 现实方向

**A.1.1 C# 对「JSON literals」的明确不采纳（2017）。** 这是 csharplang 历史上唯一一次把「JSON 字面量」当作语言特性 triage，结论只有一句：

> # JSON literals
> No

→ `meetings\2017\LDM-2017-03-15.md`

与 vblang #101（2017 年 Table）时间几乎同步——两大语言都未把「JSON 字面量」做成语言特性。C# 此后未再单独立项 JSON 字面量，而是把相关能量投入 A.1.2/A.1.4 的方向。

**A.1.2 C# 12 集合表达式：只做「框架中立集合形状」，不做键值映射与对象映射。** Summary 逐字（正文 `Probably` 的核实）：

> Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values.

→ `proposals\csharp-12.0\collection-expressions.md`（Summary）

转换规则明确枚举目标类型集合，**只含线性集合形状**（首句逐字，列表为概述）：「An implicit *collection expression conversion* exists from a collection expression to the following types:」——单维 `T[]`、`Span<T>`/`ReadOnlySpan<T>`、带 `[CollectionBuilder]` *create method* 的类型、实现 `System.Collections.IEnumerable` 的结构/类、`IEnumerable<T>` 等接口类型。**其中没有键值映射（dictionary），也没有 POCO 对象映射**。→ `proposals\csharp-12.0\collection-expressions.md`（Conversions）

空字面量规则与本提案空 `{}` 议题同构（逐字）：

> The empty literal `[]` has no type.  However, similar to the *null-literal*, this literal can be implicitly converted to any *constructible* collection type.

→ `proposals\csharp-12.0\collection-expressions.md`（Empty collection literal）

C# 用 `[` 而非 `{` 的理由直接点中本提案的 `{` 五义痛点（逐字）：

> We attempted to make `{`...`}` work with list patterns and ran into insurmountable issues.

→ `proposals\csharp-12.0\collection-expressions.md`（Drawbacks）

**A.1.3 C# 11 raw string literals：把嵌入的 JSON 当「不透明字符串数据」。** C# 对「在源码里写 JSON」的答案不是字面量语义，而是「原样字符串 + 运行库解释」（逐字）：

> This prevents easily having literals containing other languages in them (for example, an XML, HTML or JSON literal).

→ `proposals\csharp-11.0\raw-string-literal.md`（Motivation）

带插值孔的 JSON 用 `$$"""..."""`，插值定界符由 `$` 数量决定（`{{...}}`），内容仍是字符串——反序列化交给 `JsonSerializer.Deserialize`/`Utf8JsonReader`。原文引导句（逐字）：「For example a JSON literal containing interpolation holes can be written like so:」→ `proposals\csharp-11.0\raw-string-literal.md`（Detailed design (interpolation case)）。

**A.1.4 C# collection-literals 工作组（2024）：字典表达式是下一设计空间，JSON 语法被明确讨论但工作组倾向 no。** 与「嵌入式数据字面量」最接近的 C# 讨论在这里（逐字）：

> Broadly speaking, we consider dictionary expressions (tentatively `[k1:v1, k2:v2]`) to be the most important design space for us to front load.

→ `meetings\working-groups\collection-literals\CL-2024-01-23.md`

工作组把「采纳 JSON 语法」列为候选项（逐字）：

> Adopting `JSON` syntax here.  Specifically, allowing `{ "key": value }` to work as legal dictionary-expression syntax.  Working group leans no, but we definitely want to run by LDM for thoughts.

Pros 方直言（逐字）：*"Great parity with a very popular data format.  This would allow users to also instantiate Newtonsoft J-Etcs or System.Text.JXXX types just with real JSON literals.  Copy/pasting to/from C# becomes very nice."*

Cons 方四条（逐字，与正文对 A 的「映射规则爆炸 / `{` 歧义」顾虑同构）：

> * Moves us away from `[...]` being the lingua franca for all collection types.
> * Adds a lot of parsing/ambiguity complexity around `{...}`.
> * Impacts our future design space around `{...}` (for example, expression blocks).
> * Is very difficult to have pattern-parity.  `{ k: ... }` is already legal as a property pattern.  Needing that to work as a dictionary-pattern is non-trivial (and potentially very confusing for users).

→ `meetings\working-groups\collection-literals\CL-2024-01-23.md`

**A.1.5 System.Text.Json 序列化在 .NET 生态的现状：BCL 库 + source-gen，非编译器职责（索引 T5/T6）。** csharplang 仓库无 STJ 正文——它只以 dotnet/runtime 文件链接形式出现在 `proposals\csharp-11.0\utf8-string-literals.md` 的示例里（`src/libraries/System.Text.Json/src/System/Text/Json/JsonConstants.cs`）。STJ 的 source-gen（`JsonSerializerContext`）与 `[LibraryImport]`、COM source generators 同属「编译期生成替代运行时反射」生态（索引 T6），是 NativeAOT/trimming 压力（索引 T5）下序列化的标准出口。序列化在 .NET 里是**库 + 生成器**的职责，语言不内置反序列化器。

### A.2 现实 vs 提案

| 提案部分 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| **B**：JSON 数据字面量 → `JsonObject`/`JsonArray`/`JsonValue` | C# 无数据标记语言字面量；唯一表达是 raw string + 运行库 Parse/Deserialize（A.1.1–A.1.3） | **兼容（VB 领先）** | 不撞 C# 任何特性；`System.Text.Json.Nodes`（.NET 8+）是稳定、框架中立的目标类型；与 XML 字面量基因一致，是「VB 走在 C# 前面」的差异化点——正文原则 #4 偏离的「充分理由」在此成立 |
| **A**：目标类型化 POCO 创建 | collection expressions 刻意只做集合形状；字典表达式对 JSON 语法倾向 no（A.1.4）；序列化属 BCL + source-gen（A.1.5） | **冲突 / 需桥接** | 「编译器内置反序列化器」违背 C#/BCL 分工——C# 把「数据→对象」交给 STJ 与 source-gen。A 若复活，应以「字面量 → `JsonNode` + 显式 source-gen `Deserialize(Of T)`」桥接，而非编译器发明映射 |
| **D**：`&=` writer 流式 | `Utf8JsonWriter` 是 `ref struct`（仅栈）；C# 低层主线（索引 T2/T7） | **需桥接 / 触及低层** | 与「低层类型仅栈」安全模型直接相关；属库层 API 面（robust-mapping），非语言层。Reject 出本建议与 C# 现实一致 |
| 空 `{}`、`[` 转义标识符、行延续、键定界符 `:`/`:=` | C# 无对应（C# 用 `[` 做集合字面量、`{` 留给 object/collection initializer 与语句块） | **脱节** | 纯 VB 文法债，无 C# 可借鉴；与 C# 2017-03-15「JSON literals: No」同源——两大语言都因 `{` 歧义避开了 JSON 字面量 |

### A.3 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：B 的 `JsonObject` 路径在 Strict On/Off 下行为一致（正文 §6 已定）；保持「字面量 → 构造调用」的透明降级，不引入反射。这与 C#「类型系统承担更多职责、动态边缘化」（索引 T7）同向。
- **source-gen 桥**：A 的 POCO 价值（键补全、编译期形状校验）不要用「编译器内置反序列化器」实现，而走「字面量 → `JsonObject`/`JsonArray` + 显式 `JsonSerializerContext` / `Deserialize(Of T)`」——编译期生成 + 零反射，贴合 NativeAOT（索引 T5/T6）；映射规则交给 `JsonSerializerOptions`（大小写/缺失/多态/转换器，正文 §9 已列为借镜清单）。
- **识别新元数据**：.vbx 消费 C# 13+ 集合表达式产物时需识别 `[CollectionBuilder]`、`CompilerFeatureRequired` 等元数据（决策文件 M4/M8 已提示），否则无法与「C# 集合形状互操作」对接。
- **ref struct 边界**：若 D 形态以库形式复活（robust-mapping 路线），VB 需能消费/包装 `Utf8JsonWriter`（`ref struct`）或提供安全 wrapper，否则碰不到流式 writer（正文 §11 已点到 `Utf8JsonWriter` 的 `ref struct` 断层）。

### A.4 对既有 RESOLUTION / 三态判定的影响

- **无推翻**。C# 现实支持本次 RESOLUTION：B 方向与 C#「序列化属库、数据字面量非语言特性」分工**兼容**；A 的 Table 与 C# 生态把序列化放运行库/source-gen 的做法**一致**；D 的 Reject 与 C#「ref struct 低层、不污染运算符」**一致**。
- **三态判定（Consider）不受影响**；反而因 C# LDM-2017「JSON literals: No」+ CL-2024 工作组倾向反对，强化「B 是 VB 独有、C# 无竞争」的差异化论点——这是正文「默认跟随 C#（原则 #4）」偏离的一个可辩护的「充分理由」。

### A.5 引用与未核实清单

- **已核实逐字引用**：A.1.1（`meetings\2017\LDM-2017-03-15.md`）；A.1.2（`proposals\csharp-12.0\collection-expressions.md` Summary / Conversions / Empty collection literal / Drawbacks）；A.1.3（`proposals\csharp-11.0\raw-string-literal.md` Motivation / Detailed design (interpolation case)）；A.1.4（`meetings\working-groups\collection-literals\CL-2024-01-23.md`）。路径均为 `..\..\csharplang` 相对路径。
- `OPEN QUESTIONS`：C# 字典表达式（`[k1:v1]`）与「JSON 语法」采纳的**最终 LDM 裁决**——本附录只核实到工作组 2024-01-23「leans no」，未在库内找到 LDM 最终拍板记录。
- `OPEN QUESTIONS`：STJ source-gen（`JsonSerializerContext`）的具体语言协作细节属 dotnet/runtime 生态，csharplang 仓库无正文，本附录未能在库内核实其规格。
- `Suspect`：A.3 对 STJ 行为（大小写策略、多态、转换器、`JsonSerializerOptions`）的描述基于正文 §9 与生态常识，未在 csharplang 库内核实（本就不是该库内容）；mapping spec 借镜时应以 dotnet/runtime 文档为准。
