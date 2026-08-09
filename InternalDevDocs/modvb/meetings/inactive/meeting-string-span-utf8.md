# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-string-span-utf8.md`（Anthony 原文第 18 章 18.11 "String vs Span/ReadOnlySpan(Of Char/Byte) and UTF-8"）。这份建议在三个文件的索引里都只有一句话：

> String handling in VB must be awesome! We cannot allow modern problems to introduce ugliness into our codebases.

没有语法、没有示例、没有实现面。它记录的是一个**价值判断**：VB 的字符串处理必须是出色的；不能让 `Span`、UTF-8 这些现代数据形态把丑陋带进 VB 代码库。把它放进 LDM 议程，是因为我们本周刚审完两件与它相邻的事——字符串模式匹配家族（第 6 章，18.10 明言匹配器要考虑 span）和 JSON 序列化器家族（18.8 明言 ref struct 最常见的辩护场景就是 `Utf8JsonReader`）。三份建议在"span / UTF-8 数据形态"这一点上互相指着对方。所以本场会议真正的问题不是"这份提案怎么实现"，而是：**这段方向注记值得激活、继续保持搁置，还是应该被"拆解"成它真正指向的那些设计线程？**

开场我们先预期了结论形状：Anthony 自己的评价标准（`evaluation-standard.md` 2.3 表）把这种"主线保守、Anthony 激进"的建议归入经典张力。今天我们有两条主线先例可以借用——2018.02.28 关于 C# Range/直接内存访问的讨论，和 2018.02.07 关于可空引用类型的"等 C# 生态"表态——它们几乎就是为这个议题写的。

## Agenda

* [Proposal: String vs Span/ReadOnlySpan(Of Char/Byte) and UTF-8](#proposal-string-vs-spanreadonlyspanof-charbyte-and-utf-8)

## Proposal: String vs Span/ReadOnlySpan(Of Char/Byte) and UTF-8

_Related: [vblang #304 – Select TypeOf](https://github.com/dotnet/vblang/issues/304)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；Anthony 18.8（JSON 序列化器 / ref struct）、18.10（字符串模式前瞻/回溯）、第 6 章（字符串模式匹配家族）；ModVB：`meeting-string-pattern-matching.md`、`meeting-json-serializers.md`、`meeting-interpolated-string-optimization.md`、`inactive/proposal-string-pattern-lookahead.md`、`proposal-runtime-library.md`_

### 场景与缺口

We started from the proposal's one sentence and asked: **"modern problems" 具体指什么？** 在我们的语境里，它们是三样东西：

1. **UTF-8 数据**。Web 框架、`System.Text.Json`、HTTP 请求体、网络协议把文本以 `ReadOnlySpan(Of Byte)`（UTF-8）形式交付。VB 里要处理它，唯一的桥是 `Encoding.UTF8.GetString`——一次完整的分配和解码，把调用方精心维护的零拷贝全毁了。
2. **UTF-16 切片**。`String.AsSpan()` / `MemoryExtensions` 给了零拷贝切片（`ReadOnlySpan(Of Char)`），但 VB 的字符串操作——`Mid`、`InStr`、`Left`、`Right`、`Len`、`&`、`Like`、`= `——全部只吃 `String`。切片必须物化成 `String` 才能继续用惯用 API。
3. **ref struct 生态**。`Utf8JsonReader`/`Utf8JsonWriter` 是 `ref struct`（18.8 的"only scenario I consistently hear"），它们的 `ValueSpan` 是 `ReadOnlySpan(Of Byte)`。

把"缺口"具体化，就是一段今天的 VB 代码：

```vb
' 今天：UTF-8 载荷进 VB 字符串世界，只有一座桥，桥上全是一次性分配。
Dim payload As ReadOnlySpan(Of Byte) = reader.ValueSpan        ' 从 Utf8JsonReader 得到
Dim jsonText As String = Encoding.UTF8.GetString(payload)      ' 唯一的桥——解码 + 分配
Dim city = jsonText.Split(","c)(0)                             ' 又一层分配

' 今天：UTF-16 切片进不了字符串 API。
Dim s As String = "hello, world"
Dim span As ReadOnlySpan(Of Char) = s.AsSpan(7)                ' "world"，零拷贝
Dim head = Mid(span, 1, 3)                                     ' 编译错误：Mid 只吃 String
```

`Mid(span, 1, 3)` 编译失败是"丑陋"的最直接形态——切片就在手里，却要物化成新字符串才能调用一个 1998 年就存在的函数。**We think the ugliness is real.** 但我们也立刻意识到：缺口真实，不等于语言需要新语法。2018.02.28 我们讨论 C# Range 时对"直接内存访问工作流"的表态几乎逐字适用：

> "These aren't expected to be high usage workflows in VB, but we don't want to block VB programmers from those scenarios."

以及同一天的下文——这是我们今天最重要的指南针：

> "If the API support is good enough, don't do the language work in VB."

### 候选方案

We considered five shapes the feature could take.

**PROPOSAL A — 转换面扩展（bridging conversions）。** `String` 仍是唯一"一等字符串类型"，但语言提供 `String` ↔ `ReadOnlySpan(Of Char)` 与 `ReadOnlySpan(Of Byte)`（UTF-8）的**内建转换**，并让 `&`、`=`、`Like`、`Mid`/`InStr`/`Left`/`Right` 接受 span 操作数。字符串 API 面整体向 span 打开，但不引入新的"字符串类型"。

**PROPOSAL B — UTF-8 字符串字面量（借 C# `u8` 后缀）。** 语言直接照 C# 的路线加一个字面量形态：

```vb
' 借 C#（Suspect：本仓库无 csharplang 材料，C# 以 u8 后缀提供 UTF-8 字面量，
' 产生 ReadOnlySpan(Of Byte)；版本号与细节未逐字核实）。
Let bytes As ReadOnlySpan(Of Byte) = "héllo"u8
```

**PROPOSAL C — Span 化的字符串 API 家族（运行时库层）。** 不动语言，在运行时库（`Microsoft.VisualBasic` / 辅助库）与 BCL 的 `MemoryExtensions` 上补齐 span 重载：`Mid(span, ...)`、`InStr(span, ...)`、`Left/Right`、`StringComparer` 驱动的 `=`、`StartsWith`/`IndexOf` 等。这正是 2018.02.28 的 "If the API support is good enough, don't do the language work in VB" 路线，也是 `proposal-runtime-library.md`（Anthony 第 17 章）的地盘。

**PROPOSAL D — 统一字符串抽象。** 引入一个接口/抽象，`String`、`Span(Of Char)`、UTF-8 缓冲都实现之；语言运算符、模式匹配、API 在这个抽象上统一。这是建议自己 Alternatives 里列的路线（"引入统一的字符串抽象或接口"）。

**PROPOSAL E — 什么都不做 / 维持现状。** `String` 是唯一一等公民；span 场景靠显式 `Encoding.UTF8.GetString`、`.AsSpan()`、`String.Create`、`MemoryExtensions`。零语言成本。显式"什么都不做"是 LDM 决策工具箱里的合法选项——对只有一句话的方向注记，不做本身就是结论。

**OTHER DESIGNS CONSIDERED**

- **C# interpolated string handlers**：`$"..."` 直接写入缓冲区，可同时解决 UTF-8 目标与零分配。Anthony 自己在第 6 章互操作清单里列了 "Interpolated string handlers"。这是编译器 + 库的大型协作，且需要 C# LDM 先落定——我们 2018.06.13 说过 "C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries"（事实）。作为远期方向记录。
- **`Utf8String` 值类型**：运行时库提供一个 UTF-8 的字符串值类型（延迟验证、可存储），语言层零改动。属 PROPOSAL C 的库层实现细节。
- **`ValueStringBuilder` / `StringBuilder` 扩展**：把 span 拼接语义委托给既有 builder 类型。属 C。

### 权衡：Q&A

- **缺口到底需要语言还是库？** 我们反复回到这一问。三个"现代问题"里，第 3 个（ref struct 生态）是 18.8 的地盘、第 2 个（UTF-16 切片进字符串 API）绝大部分能被 C 覆盖（span 重载），只有第 1 个（UTF-8 字面量）里有一小片"语法糖"语言价值——`"..."u8` 比 `Encoding.UTF8.GetBytes("...")` 少打字、且编译期转义。**结论倾向：缺口为真，语言增量只是其中很小一片；库层（C）覆盖大头。**
- **A 的转换面是否安全？** 不。方向分两种：`String → ReadOnlySpan(Of Char)` 是自然的零成本视图（`AsSpan`），`String → ReadOnlySpan(Of Byte)` 需要**编码工作**——这绝不该隐式。一旦给 `ReadOnlySpan(Of Byte)` 加了从 `String` 的隐式/内建转换，既有 API 面会静默张开：任何收 span 的方法突然可以收字符串，重载决议与调用点行为大面积漂移。而 `ReadOnlySpan(Of Char) → String` 是分配操作，作为隐式转换同样危险。**A 的隐式方向被我们否决**；显式方向（`CType`/明确转换）等价于 C，没有语言必要。
- **B 的 `u8` 字面量是"第二种做事方式"吗？** 边缘。`Encoding.UTF8.GetBytes` 已存在，`u8` 是它的语法糖——但它只编码、不分配？`Encoding.UTF8.GetBytes` 返回 `Byte()`（分配），`u8` 字面量是编译期字节数据（零分配）。所以它不只是糖，是真正的能力差。但它也是新的字面量形态、新的语法面。我们按 2018.06.13 的尺子量："our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"。**`u8` 是 additive（新后缀、不破坏旧代码），但它是"第二种做 UTF-8 的方式"。**
- **D 为什么几乎立刻出局？** 三条硬伤。其一，`Span(Of Char)`/`ReadOnlySpan(Of Byte)` 是 `ref struct`：不能装箱、不能入字段、不能被闭包/异步捕获——"统一字符串抽象"意味着 `String`（可存储、可捕获）与 span（不可存储、不可捕获）要共享同一套运算符语义，而这套语义恰恰在"存储/捕获"处断掉。其二，UTF-8 字节与 UTF-16 字符的语义不同源（见拷问 2 的 `Option Compare`）。其三，一个"字符串抽象"是一次类型系统大改，用一句话的提案去背它，代价与收益完全不成比例。**We are not going to build a "string interface".**
- **E（什么都不做）的代价有多大？** 真实但有限。今天 `Mid(span, ...)` 编译失败，等价手写是 `span.ToString()` 或 `span.Slice(...).ToString()`——一次分配。`Encoding.UTF8.GetString` 一次分配。热路径上这是真实损耗，但"数十万安静客户"（2018.05.30，事实）的主流业务代码里，UTF-8 span 场景占比我们没有任何数据，只有 18.8 的一句轶事。
- **与字符串模式家族的关系是不是本建议的真正归宿？** 是。18.10 明言："It would be good in the design of the string pattern matching to consider how the feature would apply to a text window/stream or a span."（事实，Anthony 原文逐字）——**span 的"字符串语义"第一个真正需要它、且能自然承载它的地方，是字符串模式匹配器的零分配匹配**（`meeting-string-pattern-matching.md` RESOLUTION #6 已把"线性扫描零中间分配、span 优先"写成 v1 约束）。本建议的意图，有一部分应该写进那个家族的规格，而不是自立门户。

### 深度追问：LDM 拷问清单

We worked through the twelve-question checklist. The decisive questions were 2, 4, 5, 8 and 12.

#### 1. 语法 / 文法歧义

本建议**没有提出任何语法**，所以直接文法风险为零——它是"继承"式的。若走 B（`u8` 后缀），才有文法问题：VB 字符串已有后缀 `c`（Char，`"a"c`）与类型字符 `$`（String，`Dim s$`）；`u8` 是**双字符后缀**，与既有单字符后缀/类型字符的关系需要厘清。更大的问题是与插值字符串的叠放：`$"hello {name}"u8` 是"UTF-8 编码的插值结果"吗？C# 对此有自己的答案（handler 生态），VB 若照搬会撞上我们 2015-01-14 定下的"插值字符串 *is* a string. End of story."（事实）——`u8` 让插值结果不再是 String，这条决议要重写。`OPEN QUESTION`：`u8` 与插值的组合形态。

#### 2. 角案例 / 边界语义

这是整场最实的一问，也是 D 出局的现场。

- **ref struct 存储边界**：`Span` 不能作字段、不能被闭包捕获、不能跨 `Await`（`Probably`：与 C# 相同的 CLR 约束，VB 侧需在原型中核实）。"像字符串一样使用 span"在"存储/捕获/异步"这三个字符串最常用的场景里**结构性不可能**。任何让 span 获得"一等字符串语义"的设计，都必须直面这条不对称。
- **`Option Compare Text` 与 UTF-8**：VB 的 `=` 受 `Option Compare Text` 影响（文化感知的大小写比较，事实）。`ReadOnlySpan(Of Byte)` 的字节比较是逐字节，**不是**文化比较。若让 `=` 接受 UTF-8 span，是解码成字符串再比较（重新引入分配）、还是逐字节比较（与 `Option Compare` 语义断裂）？两难，未定义。`Probably`：这是"span 即字符串"与 VB 既有字符串语义之间最深的裂缝。
- **UTF-8 合法性**：span 可以是无效 UTF-8（截断的多字节序列、孤立代理）。`String` 永远合法（构造时已验），span 不保证。把 span 当字符串处理时，"非法字符"是一个新的错误状态。
- **UTF-16 切片切开代理对**：`s.AsSpan(n)` 可以切开一个代理对。`Option Compare` / 模式匹配作用于该切片时不能产出损坏的 Char。
- **`Nothing` vs 空**：`String` 可空（`Nothing`）；`ReadOnlySpan(Of Byte)` 是值类型，`Nothing` 与空 span 的区分语义不同。运算符/模式的空值规则要重定义。

#### 3. 作用域与绑定

若 B 落地，语义模型里 `"héllo"u8` 绑定到 `ReadOnlySpan(Of Byte)`（字面量节点的新类型）；若 A 落地，`=`/`Mid` 的 span 重载要出现在 `GetTypeInfo` 与补全里。但本建议**零语法**，语义模型面为零——它不新增任何绑定面。真正有绑定影响的是模式家族（`Case` 子句形态、作用域），那是别人的规格。本建议只需回答"如果将来有 span 运算符，语义模型如实呈现"这一句。

#### 4. 与既有特性的交互

- **`&` 连接运算符**：VB 的字符串连接是 `String` 特化。对 `ReadOnlySpan(Of Byte)` 做 `&`，是"字节拼接"还是"解码后拼接"？运算符重载为 span 定义的行为未定。
- **`Like` 运算符 / 字符串模式家族**：18.10 明言匹配器要考虑 span。家族 v1 的"前缀锚定 + 尾捕获 + 线性扫描零中间分配"（`meeting-string-pattern-matching.md` RESOLUTION #6）**已经隐含 span 实现**——这是本建议意图被满足的最具体路径。
- **插值字符串**：`$"..."` 是 String（2015-01-14 决议）。`u8` 若与插值叠放，违反该决议；不叠放，则 UTF-8 只能来自字面量或运行时，热路径（插值 + UTF-8 输出）仍无解。C# 靠 handler 生态解决，VB 没有。
- **字符串窄化转换（TryParse-shape）**：`proposal-string-narrowing-conversions.md` 用 `TryParse` 把字符串按类型解析。对 span 同样适用（`Utf8Parser.TryParse` 就是 span 版）——这是 C 库层天然该覆盖的，与语言无关。
- **`StringBuilder &=`**：已在 `meeting-interpolated-string-optimization.md` 判 Reject（`Append` 已存在）。span 拼接同理——不造第二套。
- **JSON / ref struct 家族**：18.8 说 ref struct 最常见场景是 `Utf8JsonReader`；`meeting-json-serializers.md` RESOLUTION #3 已把这条证据传给 ref struct 工作项。本建议与它同源。

#### 5. Breaking change 与兼容性

- **E 零破坏**；**B 零破坏**（新后缀，additive）；**C 零破坏**（新重载，additive，除非与既有重载歧义——需要调重载决议）。
- **A 有**：任何隐式转换（尤其 `String → ReadOnlySpan(Of Byte)`，含编码）都会让既有调用点重编译后行为漂移——2018.06.13 "We will almost never make breaking changes to Visual Basic"（事实），这条直接否决 A 的隐式方向。
- **D 有**：类型系统大改，不可能零破坏。

#### 6. Option Strict / 编译选项分叉

- span 运算（`&`、`=`、`Mid`）在 Strict On 下是早期绑定重载决议；Strict Off 下 span 不是 `Object` 主道，晚期绑定路径不会命中 span 重载。**两条路径行为必须一致**——span 重载只在早期绑定存在，宽松模式下字符串照旧。`Probably` 这是安全的分叉，但需要显式写进规格（`Suspect`：需在宽松路径实测）。
- `u8` 字面量在两种模式下同物（编译期字节数据），无分叉。

#### 7. IDE / IntelliSense

`"..."u8` 的着色、悬停（显示 `ReadOnlySpan(Of Byte)`）、补全；若 C 落地，`Mid(` 的参数补全要显示 span 重载。这些都是可预期的成本，不是障碍。真正的 IDE 问题在模式家族（`Case` 子句新形态），不在本建议。

#### 8. 数据 / 普遍性

这是本场最需要诚实的地方。**主线的先例直接支持"低频"判断**：2018.02.28 讨论 C# Range 时我们写道 "These aren't expected to be high usage workflows in VB"（事实）——那是主线对"直接内存访问 / span 类工作流在 VB 中的使用频率"的官方评估。同一句话是 "but we don't want to block VB programmers from those scenarios"。Anthony 的证据则是 18.8 的一句轶事（"the only scenario I consistently hear for ref structs"）——`Suspect`：幸存者偏差，它反映的是移植到 VB 的 C# 开发者在抱怨，不是"数十万安静客户"的业务分布。VBScript.NET 目标上，脚本/自动化/文本处理主要是 `String` 与 VB6 遗产函数（`Mid`/`InStr`），UTF-8 span 是**窄中之窄**。结论：场景真实、频率存疑、无数据。证据等级封顶"已检查"。

#### 9. 更简替代

- `Encoding.UTF8.GetString(span)`：现状，一次分配。
- `String.Create` / `MemoryExtensions`（`IndexOf`、`StartsWith`、`AsSpan`）：BCL 已有，span 运算大头已被覆盖。
- `Utf8Parser`/`Utf8Writer`：BCL 的 span 版解析/写出，覆盖 TryParse-shape 的 span 侧。
- 运行时库 span 重载（C）：把 `Mid`/`InStr`/`Left`/`Right` 的 span 版本放进 `Microsoft.VisualBasic`——零语法、兼容、可测。
- **结论：替代方案强**。语言层唯一独占的价值是 `u8` 字面量的"编译期字节数据"（零分配），且这个价值只在字面量场景成立。

#### 10. 复杂度 / 成本 / 优先级

- 本建议**没有实现面**：无语法、无 API、无文法。激活它 = 先发明一个特性再实现，我们没有理由为一个能用库层解决大半的场景发明特性。
- B 的成本：文法（后缀）+ 字面量降级（发字节数据）+ 与插值叠放的语义决策。中等。additive 但语法面扩张。
- C 的成本：运行时库新增重载 + 重载决议调优。低。符合 2018.02.28 "API support" 路线。
- 优先级：**低于字符串模式家族、JSON 家族、runtime-library**——它们是载体，本建议是方向注记。

#### 11. 运行时 / CLR 硬约束

- span 是 `ref struct`：不可装箱、不可入字段、不可闭包/异步捕获。给 span 运算符语义时，操作数按 byref-like 规则处理，PEVerify 无碍（`readonly` byref + 实例方法调用是既有安全操作）。
- `u8` 字面量降级：编译器发一个字节数组的静态数据（`ldstr` 的字节表等价物），零运行时依赖，无 CLR 存储规则问题。
- 无反射依赖。**不触达 CLR 存储规则**（那是 C# 委托给 CLR 的事，2018.06.13 "C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries"）。

#### 12. 值不值得做

逐维打分。**作为独立特性**：价值中（场景真实但低频，主线 2018.02.28 已认证频率）、成本中高（A/D 高、B 中、C 低）、风险中（A 隐式转换破坏、D 类型系统大改、B additive 但语法面扩张）。**结论：不值得为它单独激活语言特性。** **作为方向注记**：价值高——它把"字符串是 VB 的核心，现代数据形态不能污染它"这条审美写进了档案，并为字符串模式家族（span 匹配）与运行时库（span 重载）提供了方向输入。我们选择后者。用 2018.02.28 的话收束："Wait to see what C# does and whether we need syntax."（事实）

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — E/C/B 零破坏；A 的隐式转换与 D 有破坏风险，**否决**。这是我们保持搁置的最大理由之一。
2. **保持 VB-like** — `u8` 后缀是外来味（C# 借物）；C 的 span 重载让 `Mid(span, ...)` 保持 VB 味（1998 年的函数继续用，只是更聪明）。C 更 VB。
3. **不引入"第二种做事方式"** — `Encoding.UTF8.GetBytes` 已存在，`u8` 是第二条；C 是"让既有写法更聪明"（补重载），低风险；D 是第三条大道，**危险区**。
4. **默认跟随 C#，除非有充分理由** — B 直接跟随 C#（`u8`）；但 2018.02.28 对 Range 的立场是"API 够好就不做语言工作 + 等 C#"——我们倾向先走 API。
5. **读起来像英语、对新手友好** — `"..."u8` 可读但不像 VB；`Mid(span, 1, 3)` 像 VB。新手对"同一个字符串有三个数据形态"是理解负担。
6. **不为边缘场景加特性** — 2018.02.28 主线评估直接内存访问工作流在 VB 低频——本特性边缘化风险高。
7. **避免隐蔽的控制流/语义变化** — `=` 对 UTF-8 span 的字节比较 vs `Option Compare Text` 的文化比较，是隐蔽语义分叉（`Return?` 同源）；A 的隐式转换也是。**扣分项。**
8. **不与既有语法冲突** — `u8` 与 `$` 类型字符、`c` 字符后缀、插值 `$"..."` 的共存需文法厘清。
9. **消除常见样板** — 目标是消除 span↔String 往返样板，正中靶心；但样板用库层（C）可消，不必语言。
10. **冗长只在有用时是美德** — `Encoding.UTF8.GetString` 是"有用的冗长"：显式转换把"这里发生一次分配/编码"摆在明处。隐藏成 `=` 或隐式转换反而是坏冗长。

**主线对照（评价标准 2.3 表）**：`Span`/UTF-8 在主线**没有独立建议**（`..\..\..\vblang\proposals/` 无 span/UTF-8/ref struct 条目，事实）；最近的接触点是 2018.02.28 的 Range/直接内存访问讨论（API-first + 等 C#）。本建议是 **Anthony 独立延伸**；`u8` 是借鉴 C#（`Suspect`，未本地核实细节）。与字符串模式家族（18.10 span 约束）和 JSON 家族（18.8 ref struct 证据）同族，本建议是这一族的方向性注脚。

### RESOLUTION:

1. **不激活 `proposal-string-span-utf8`。** 它是方向注记，不是提案：一句话、零语法、零示例、零数据。把它当作可实现的特性来激活，等于先发明一个特性再实现，而发明没有理由。
2. **"String handling must be awesome" 的意图，拆解给三条既有线程承载，不自立门户。**
   - **字符串模式家族**：18.10 已要求匹配器"consider how the feature would apply to a text window/stream or a span"——span 的字符串语义第一个且最自然的落脚点是**模式匹配器的零分配实现**（`meeting-string-pattern-matching.md` RESOLUTION #6 已定：线性扫描零中间分配、span 优先）。本建议的意图写进那个家族的规格。
   - **运行时库**：span 化的字符串 API（`Mid`/`InStr`/`Left`/`Right`/`StartsWith` 等的 `ReadOnlySpan(Of Char/Byte)` 重载、`Utf8Parser` 桥接）归 `proposal-runtime-library.md`（Anthony 第 17 章），按 2018.02.28 "If the API support is good enough, don't do the language work in VB" 路线走。
   - **JSON / ref struct 家族**：`Utf8JsonReader`/`Utf8JsonWriter` 的 ref struct 约束问题归 ref struct 工作项（`meeting-json-serializers.md` RESOLUTION #3 已移交证据），不在本建议发明语法。
3. **维持 `String` 为唯一一等字符串类型。** 不给 span 一等字符串语义。理由：ref struct 存储边界（不可字段/闭包/异步）使"像字符串一样用 span"在三个最常用场景结构性不可能；`Option Compare Text` 与 UTF-8 字节比较的语义裂缝无解；统一字符串抽象（D）是一次类型系统大改，代价与收益不成比例。
4. **否决 A 的隐式转换方向。** `String → ReadOnlySpan(Of Byte)` 含编码，绝不隐式；`ReadOnlySpan(Of Byte) → String` 是分配，也不隐式。显式转换等价于 C，归库层。这是本建议最可能被误读成"实现许可"的地方，必须写进档案：**不引入 span 的隐式字符串转换。**
5. **`u8` UTF-8 字符串字面量（B）：不否决，挂起。** 遵循主线两个先例——2018.02.28 "Wait to see what C# does and whether we need syntax" 与 2018.02.07 "We'll postpone this until we understand the uptake in C#"（事实）。它是 additive、有真正的"编译期字节数据零分配"价值，但也是语法面扩张与第二种做 UTF-8 的方式。**只有** C# 采纳率与 VB 场景数据到位才回访。插值叠放（`$"..."u8`）的语义与 2015-01-14 "interpolated string *is* a string" 决议的关系，列为 `OPEN QUESTION`。
6. **保持 inactive，定义为"收敛检查"提案。** 当字符串模式家族（span 匹配）、runtime-library（span 重载）、ref struct 工作项三线推进后回访，确认"现代问题"的丑陋被组合路线覆盖；若回访时仍有真实痛苦且可复现，再谈语言增量。
7. **激活所需信号（明确列出）**：
   - (a) 出现具体语法或 API 草案，附可编译示例——尤其是 `u8` 字面量与插值的组合形态；
   - (b) 数据：真实 VB 代码库中"span/UTF-8 字符串操作"代码占比超过可辩护阈值（量化普遍性，取代 18.8 轶事）；
   - (c) 生态评估：C# `u8` 字面量的采纳率与 handler 生态成熟度（对照 2018.02.07 / 2018.02.28 的"等 C#"先例）；
   - (d) 组合验证：模式家族 span 匹配 + runtime-library span 重载落地后，仍有可复现的"字符串处理丑陋"场景。

### Implication:

- 更新 `proposal-runtime-library.md`：新增工作流——span 化的字符串 API（`Mid`/`InStr`/`Left`/`Right`/比较 的 span 重载），作为本建议的落点之一。
- 更新字符串模式家族规格（`proposal-string-pattern-matching.md` / `inactive/proposal-string-pattern-lookahead.md`）：把 18.10 的 span 约束写进匹配器 v1 要求（已部分完成于 `meeting-string-pattern-matching.md` RESOLUTION #6），本建议作为该约束的出处被引用。
- 向 ref struct 工作项传第二条证据：除 18.8（JSON 序列化器）外，UTF-16 切片（`AsSpan`）与 UTF-8 载荷的字符串操作也是 ref struct 消费边界在 VB 中的真实用例。
- 不建原型；不新增文法；不新建独立提案。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`u8` 字面量与插值字符串的叠放（`$"..."u8`）——与 2015-01-14 "interpolated string *is* a string" 决议的关系，以及 C# handler 生态如何回答同一问题。
- `OPEN QUESTIONS`：`=` / `&` / `Like` 若未来接受 UTF-8 span，`Option Compare Text` 的文化比较语义与逐字节比较的取舍——当前无答案，也不在近期求解。
- `OPEN QUESTIONS`：ref struct 在 VB 中的精确消费边界（局部可消费、不可声明、不可捕获/装箱——`Probably`，需原型核实；`meeting-json-serializers.md` Follow-up 同款问题）。
- `TODO`：量化 span/UTF-8 字符串操作在真实 VB 代码库中的占比（激活信号 (b) 的证据来源）。
- `TODO`：核实 C# `u8` 字面量的确切语法、版本与生态现状（本仓库无 csharplang 材料，Suspect 待核）。
- `Follow-up`：与字符串模式家族、runtime-library、ref struct 工作项三线对表，确认本建议的落点归属，避免三份建议互相指认却无人承载。

### 状态

- **LDM 状态：保持 Inactive**；转为"收敛检查"角色——本建议是字符串/span/UTF-8 家族的方向注记，不独立排期。
- **三态判定：Table（保持搁置）** — 不是 Reject（动机真实、符合 VB 基因 #2/#5、与 18.8/18.10 主线呼应、无破坏面），也不是 Active（无可实现内容、无数据、无语法）。激活信号见 RESOLUTION #7，逐条待检。

---

## 附录：特性评价

# 建议评价报告：proposal-string-span-utf8.md

## 评价对象

- 建议：proposal-string-span-utf8.md — String vs Span/ReadOnlySpan(Of Char/Byte) 与 UTF-8（用方向注记声明"VB 的字符串处理必须出色，现代数据形态不得引入丑陋"）
- 来源：Anthony 原文第 18 章 18.11 "String vs Span/ReadOnlySpan(Of Char/Byte) and UTF-8"（`..\..\AnthonyDesign_wordpress.txt` L3010–3014，原文仅一句话）
- 配方目标：当字符串以 `Span(Of Char)`、`ReadOnlySpan(Of Char)` 或 UTF-8 `ReadOnlySpan(Of Byte)` 形式存在时，VB 提供同样优雅的字符串处理，避免笨拙的往返转换

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。改进目标（"优雅"）不可操作、不可衡量；全文**零示例**（模板"必须包含示例"未满足）；唯一"证据"是 18.8 的一句轶事（"only scenario I consistently hear"）；关键子效果（避免往返转换）已被 `MemoryExtensions`/`Encoding` 现状部分达成却未引用 | 已提供/已检查（状态行 Prototype/Implementation/Specification 为占位链接，无运行证据） | 目标无验收线；零示例；"现代问题"未展开；普遍性无数据 |
| 特性 | 2/5 | 锚点 2 之二："外来特性直接照搬未 VB 化"（此处为**无特性可评**）。没有语法/类型/API 载体，"特性"维悬空；唯一正向是其动机本身——字符串优先、拒绝丑陋是纯 VB 基因（原则 #2/#5）；若被实现成 `u8` 字面量，则是借 C# 的外来形态且未 VB 化 | 已检查 | 无载体无法评估"与既有语法和谐"；`u8` 若落地是外来味；D（统一抽象）若被误选则与 VB 基因冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、来源标注诚实（"原文仅有一句话"）、3 个未决问题具体诚实（1–3 健康区间，加分）；但 Detailed design **明确空置**（"原文未给出任何语法或示例"）、全文零示例（违反模板）、Drawbacks 仅两条、无 Compatibility/`Option Compare`/ref struct 分析 | 已检查 | 空 Detailed design + 零示例使文档止于"记录方向"，无法支撑设计评审下一步 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。无维度明显受益（无可实现内容，雷/水/光均无实质贡献）；暗风险真实但未对冲——若被误读为"给 span 一等字符串语义"或"引入隐式转换"，会撞 ref struct 存储边界、`Option Compare` 语义裂缝与破坏兼容（A/D 两条错误路径），文档未声明禁令 | 已检查（预测待定） | 作为 inactive 存档无破坏；实际影响须"已采纳"后定；文档未声明"不引入隐式转换/统一抽象"两条禁令 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注，标注与影响有偏差"。来源标注准确（18.11 一句话，逐字正确）；但未交叉标注相邻成分：18.8（ref struct/`Utf8JsonReader`）、18.10（span 匹配约束）、第 6 章字符串模式家族、C# `u8` 字面量参照、2018.02.28 Range 主线先例（API-first + 等 C#）——读者无法知道它站在哪条裁决线上 | 已检查 | 无杂质、无虚构语法；但成分关系不透明，疑似借 C# `u8` 却未声明 |

## 设计原则对照

- **与 VB 基因：方向一致、载体缺失。** 一致：原则 #2（保持 VB-like，字符串是 VB 核心）、#5（新手友好，拒绝丑陋）、#9（消除往返样板）。缺失：无载体使 #3（不引入第二种做事方式）、#7（避免隐蔽语义变化，`Option Compare` 裂缝）无法被正面评估——只能靠两条禁令（不引入隐式转换、不建统一抽象）兜底。
- **与主线关系：Anthony 独立延伸。** 主线 `..\..\..\vblang\proposals/` 无 span/UTF-8/ref struct 条目（事实）；最近的接触点是 2018.02.28 Range/直接内存访问讨论（"These aren't expected to be high usage workflows in VB" + "If the API support is good enough, don't do the language work in VB" + "Wait to see what C# does"）。`u8` 是借鉴 C#（Suspect，未本地核实）。与字符串模式家族、JSON 家族、runtime-library 同族，本建议是族内方向注记。
- **破坏性变更：无（零语法）。** 潜在破坏全部来自它可能诱导的两条错误路径（隐式 span 转换、统一字符串抽象），文档未显式声明禁令。

## 总评

- **达成程度：未达成（作为可采纳特性）/ 达成（作为方向注记存档）。** 作为特性它没有任何可采纳内容；作为档案它诚实地记录了一条符合 VB 基因的设计方向，值得保留在 inactive。
- **LDM 三态建议：Table（保持 inactive，转"收敛检查"角色）。** 面向 VBScript.NET 的优先级同样为 **Table**：脚本/业务代码的主流文本处理是 `String` + VB6 遗产函数，UTF-8 span 是窄中之窄；span 的字符串语义应由模式家族（零分配匹配）与运行时库（span 重载）承载，不需要语言特性。
- **主要问题**：① 无任何实现内容（零语法、零示例），不可执行；② "现代问题"未展开为具体场景与数据；③ 与 ref struct 消费边界、`Option Compare`、重载决议、插值字符串决议（2015-01-14）的交互全部未定；④ 误读风险——被实现为隐式转换或统一抽象会撞红线，文档未对冲；⑤ 动机证据是轶事，无数据。

## 返工建议

- **补充章节**：Detailed design（现空置）——要么给出组合路线（模式家族 span 匹配 + 运行时库 span 重载 + `u8` 字面量）的可验证代码路径，要么明确声明"本建议是收敛检查而非实现建议"，二选一；补 Compatibility（零破坏面 + 两条禁令：不引入隐式转换、不建统一抽象）；补与 `Option Compare`、插值 `$"..."`、ref struct 存储边界的交互分析。
- **补充证据**：span/UTF-8 字符串操作在真实 VB 代码库的占比数据（激活信号 (b)）；C# `u8` 字面量与 handler 生态的现状核实（Suspect 待核）；ref struct 在 VB 中的消费边界原型验证。
- **未决问题处理**：三个 Unresolved 要么在本建议回答，要么显式指向承载线程（18.8 → ref struct 工作项、18.10 → 字符串模式家族、UTF-8 载荷 → 运行时库）；"现代问题"的具体指涉必须展开，不能停在"现代问题"四个字。
- **设计探索**：`u8` 与插值叠放（`$"..."u8`）与 2015-01-14 "interpolated string *is* a string" 决议的关系——若 C# handler 生态证明这是正路，VB 是否值得为它重写一条插值决议；`Option Compare Text` 对 UTF-8 字节比较的语义裂缝是否有库层绕法（`StringComparer` 驱动，而非 `=` 运算符）。

---

## 附录：C# 生态与互操作考量

> 本附录依据 `..\..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 main 分支镜像的浓缩索引）与 `..\..\..\csharplang` 原文核对。决策文件 M6 已判定本提案主题（String/Span/UTF-8）与 C# 现实「**直接同向，可实现互操作**」；本附录展开该判定，逐字引用 C# 原文并对照本场 RESOLUTION 与三态判定。C# 原文均逐字摘自 `..\..\..\csharplang`，路径随文标注。

### 相关 C# 现实方向

C# 生态对本提案主题有三条已落地语言主线与一条相关决议，全部直接命中本场讨论的 PROPOSAL A/B 与"拆解"路线。

**1. C# 11 UTF-8 字符串字面量（`u8` 后缀）→ `proposals\csharp-11.0\utf8-string-literals.md`**

- Summary 原文：「This proposal adds the ability to write UTF8 string literals in C# and have them automatically encoded into their UTF-8 `byte` representation.」
- Detailed design 原文：「When the `u8` suffix is used, the value of the literal is a `ReadOnlySpan<byte>` containing a UTF-8 byte representation of the string.」降级方式原文（Lowering 节）：「This means the call site will be allocation free as C# will optimize this to be stored in the `.data` section of the PE file.」且「Since the literals would be allocated as global constants, the lifetime of the resulting `ReadOnlySpan<byte>` would not prevent it from being returned or passed around to elsewhere.」
- 设计演进关键决定（→ `meetings\2022\LDM-2022-04-18.md`）：原型曾允许 string 常量**隐式**编码为 UTF-8 字节序列，LDM 因「it is a significant breaking change」撤回（原文：「There are in fact quite a lot of methods with overloads for both `string` and e.g. `ReadOnlySpan<byte>`, and existing calls may now either fail or silently pick a different overload.」），结论原文「Let's require the `u8` suffix in order for string literals to be Utf8 encoded.」；同日另一结论定稿 natural type：「We are ok changing the natural type to `ReadOnlySpan<byte>`.」
- 转换种类（→ `meetings\2022\LDM-2022-01-26.md`）：string 常量 → UTF-8 字节被定义为一个**新的转换种类**，结论原文「We will introduce a new conversion kind for string constant to UTF-8 bytes.」，并「Not a standard conversion, for now.」；表达式树中被禁止（结论「Blocked.」）。

**2. C# 11 `Span<char>`/`ReadOnlySpan<char>` 对常量字符串的模式匹配 → `proposals\csharp-11.0\pattern-match-span-of-char-on-string.md`**

- Summary 原文：「Permit pattern matching a `Span<char>` and a `ReadOnlySpan<char>` on a constant string.」
- Motivation 原文：「In order to encourage adoption of `ReadOnlySpan<char>` we allow pattern matching a `ReadOnlySpan<char>`, on a constant `string`, thus also allowing it to be used in a switch.」
- 常量模式新增条目（Detailed design，逐字）：「If *e* is of type `System.Span<char>` or `System.ReadOnlySpan<char>`, and *c* is a constant string, and *c* does not have a constant value of `null`, then the pattern is considered matching if `System.MemoryExtensions.SequenceEqual<char>(e, System.MemoryExtensions.AsSpan(c))` returns `true`.」
- LDM 放行理由（→ `meetings\2022\LDM-2022-02-23.md`）：「We are ok with allowing a morally constant pattern, even if it's not semantically constant.」

**3. C# 14 first-class Span → `proposals\csharp-14.0\first-class-span-types.md`**

- Summary 原文：「We introduce first-class support for `Span<T>` and `ReadOnlySpan<T>` in the language, including new implicit conversion types and consider them in more places, allowing more natural programming with these integral types.」
- 隐式 span 转换清单含「From `string` to `System.ReadOnlySpan<char>`」——即 C# 在**零成本视图**方向（String → ReadOnlySpan(Of Char)）选择了标准隐式转换。
- 与动态/表达式树的摩擦（Expression trees 小节，逐字）：「Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine:」——与本场拷问 2 的 ref struct 存储边界、索引 T7（表达式树与 Span 冲突）同一摩擦面。
- 对 VB 的明确表述（Open questions → Unrestricted betterness rule，逐字）：「However, it should be possible to use it to avoid ambiguities from VB where the attribute should be recognized.」——C# 靠 `[OverloadResolutionPriority]`（C# 13）处理 span 时代重载歧义，并明言 VB 侧应识别该特性。

**4. `$"..."u8` 插值 + UTF-8 组合 → `meetings\2023\LDM-2023-10-16.md`**

- C# LDM 对 "u8 string interpolation"（issue #7072）的结论（逐字）：「.NET 8 has mostly addressed this request with some JIT work to make `TryWriteUtf8` extremely efficient. The remaining work here would be allowing usage outside of that API, and we're unsure whether the existing are addressed with this new work. We'll wait to see if the new work has sufficiently addressed requests here before proceeding with more design work.」**Conclusion: To the backlog.**
- 即：C# 也没有为"插值字符串 → UTF-8"做新语法，而是依赖 handler / `TryWriteUtf8`（库 + JIT 层），把剩余语言工作放进 backlog。这直接回答了本场 OTHER DESIGNS CONSIDERED 里"interpolated string handlers 需 C# LDM 先落定"的远期记录。

**5. 背景：span 安全模型 → `proposals\csharp-7.2\span-safety.md`（索引第四节已核实）**

- 「The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack.」

### 现实 vs 提案

| 本场判定 | C# 现实 | 关系 |
|---|---|---|
| RESOLUTION #2：拆解给模式家族 / 运行时库 / ref struct 三线，不建统一字符串抽象 | C# 恰好如此：span 匹配进语言（pattern-match-span-of-char），span 重载进 BCL/`MemoryExtensions`，ref struct 边界由类型系统演进（C# 13 ref struct 接口、C# 14 first-class span）承接 | **兼容**——C# 的现实就是"拆解"而非 D（统一抽象），印证本场 D 出局 |
| RESOLUTION #4：否决 String → ReadOnlySpan(Of Byte) 隐式（编码绝不隐式） | C# 原型做过 string 常量 → UTF-8 隐式转换，因 breaking change 撤回，改为强制 `u8` 后缀（LDM-2022-04-18） | **兼容**——编码方向 C# 同样拒绝隐式；`u8` 是"编译期字节数据"的显式形态 |
| RESOLUTION #5：`u8` 挂起，"等 C# 是否做、采纳率如何" | C# 11 已落地 `u8`（2022）；"C# 是否做"这一前置条件已得肯定答案 | **兼容但前置已解**——等待条件解决，激活信号 (c) 从"是否做"转为"采纳率数据" |
| PROPOSAL A 整体否决（含 String → ReadOnlySpan(Of Char) 零成本方向） | C# 14 把 `string → ReadOnlySpan<char>` 做成**标准隐式转换** | **分歧**——C# 在零成本视图方向放开，VB 维持全禁令；这是唯一实质分歧点（且 C# 该方向不含编码，不推翻本场核心禁令） |
| `$"..."u8` 插值叠放（OPEN QUESTION） | C# LDM 未做该语法，交回库层（`TryWriteUtf8` / JIT），To the backlog（LDM-2023-10-16） | **兼容**——C# 也选库层而非新语法，与 "If the API support is good enough, don't do the language work" 指南针同向 |

- **脱节面**：无实质性脱节。C# 在字符串/span/UTF-8 上的走向与本场"拆解 + 库层覆盖大头 + 编码不隐式"的分工高度一致。
- **需桥接点**：C# 14 之后消费 C# 库时，重载决议中会出现隐式 span 转换参与的新规则与 `[OverloadResolutionPriority]` 倾斜。`.vbx` 编译器必须理解这些语义，否则跨语言重载解析与 C# 侧不一致（C# 原文已明言该特性应被 VB 识别）。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：维持 String 唯一一等字符串类型（RESOLUTION #3）。若未来给 VB 加 `u8`，只作为"编译期字节数据"字面量形态（零分配、`additive`），编码仍不隐式；晚期绑定继续面向 COM/Office 场景（本场与 M2 同源的动态出口），span 运算走早期绑定重载。
- **source-gen / 编译期降级桥**：C# 把 `"hello"u8` 降级为 PE `.data` 段字节数据（allocation free）。`.vbx` 若采用 `u8`，沿用同一降级即可零依赖、零运行时成本；runtime-library 的 span 重载（`Mid`/`InStr`/`Left`/`Right`/`StartsWith`）是库层，不需要语言（本场 RESOLUTION #2 第二条不变）。
- **识别新元数据**：编译器必须识别 `OverloadResolutionPriorityAttribute`（C# 13）与 C# 14 隐式 span 转换的语义，才能正确消费现代 .NET 库；这是 M8 之外新增的"认识 C# 元数据"桥接点。
- **模式匹配沿 C# 先例**：字符串模式家族 v1 若实现 span 匹配，按 `pattern-match-span-of-char-on-string` 的 `MemoryExtensions.SequenceEqual` / "morally constant" 语义对齐（`meeting-string-pattern-matching.md` RESOLUTION #6 的"线性扫描零中间分配、span 优先"与 C# 直接同向），避免另起炉灶。
- **ref struct 消费边界**：C# 13 ref struct 接口 / C# 14 first-class span 持续把 ref struct 推进类型系统；`.vbx` 至少需能**消费** ref struct（读取、调方法、`AsSpan`、模式匹配输入），不必然要能声明——这正是本场 OPEN QUESTION（ref struct 在 VB 中的消费边界）的原型核实项。

### 对既有 RESOLUTION / 三态判定的影响

- **三态 Table（保持搁置）维持不变**。C# 现实没有引入需要 VB 立刻跟进的语言缺口；恰恰相反，C# 的"拆解而非统一抽象"印证了本场的结构判断。
- **RESOLUTION #5 需更新一处注记**："等 C#" 的前置条件已解决——C# 11 `u8` 于 2022 落地，`LDM-2022-04-18` 定稿 natural type = `ReadOnlySpan<byte>`、强制 `u8` 后缀。该条从"等待 C# 是否做"变为"VB 是否跟"，仍按激活信号 (b)(d) 等数据与组合验证，**不改变挂起状态**。
- **OPEN QUESTION（插值叠放）已获 C# 侧答案**：C# LDM 将 u8 string interpolation 交回库层（`TryWriteUtf8`，`meetings\2023\LDM-2023-10-16.md`，To the backlog）。本场同问可据此更新为"库层路线，不设语言语法"，但仍与 2015-01-14 "interpolated string *is* a string" 决议相关（若未来走 handler，插值结果仍可是 String）。
- **RESOLUTION #4 措辞建议微调**：核心禁令（编码不隐式）保持；但"String → ReadOnlySpan(Of Char) 零成本视图"方向 C# 已放开为标准隐式转换（C# 14），VB 若维持全禁令，宜在档案里注明这是**有意的差异化**而非默认跟随（避免未来被误读为"没跟上 C#"）。

### 引用纪律

- 上述 C# 原文均逐字摘自 `..\..\..\csharplang`（dotnet/csharplang main 分支镜像），来源路径已随文标注（`→ proposals\csharp-11.0\...` / `→ proposals\csharp-14.0\...` / `→ meetings\2022\LDM-2022-01-26.md` / `LDM-2022-02-23.md` / `LDM-2022-04-18.md` / `→ meetings\2023\LDM-2023-10-16.md`）。
- `OPEN QUESTIONS`：
  - C# 11 `u8` 的 natural type 在提案正文与 LDM 结论间存在演变（提案正文 `Detailed design` 示例仍写 `var s2 = "hello"u8; // type is ReadOnlySpan<byte>`，而 Resolved 一节记录过 `byte[]` 提案；`LDM-2022-04-18` 最终定稿 `ReadOnlySpan<byte>`）。本附录以 LDM 结论为准，提案正文示例保留为历史文档歧义。
  - `TryWriteUtf8` / .NET 8 JIT 工作对 `$"..."u8` 场景的实际覆盖程度未在本仓库核实，仅引用 `LDM-2023-10-16` 的结论原文。
- `Suspect`：C# `u8` 字面量在真实 C# 代码库的**采纳率**数据（激活信号 (c)）本仓库无量化来源，需另行调研（对照 2018.02.07 / 2018.02.28 的"等 C#"先例）。
