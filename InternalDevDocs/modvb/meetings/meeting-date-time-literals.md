# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们继续逐章评审 Anthony 的"Type-System Enhancements"（第 14 章）——上次讨论交/并类型与数组伪类型时，我们注意到这一章还藏着一组字面量形态的改动，正好本次一并拿出。这组改动与我们最近在 JSON 字面量（主线 #101）与注解字符串（主线 #184/#27）上积累的思考直接相撞，所以讨论比预想的热。我们花了不少时间把"这到底是在扩展 `#...#` 字面量，还是在悄悄引入第四种字面量机制"这个前置问题摆到桌上。

## Agenda

* [Proposal: 日期/时间字面量增强（Date and Time Literal Enhancements）](#proposal-日期时间字面量增强date-and-time-literal-enhancements)

## Proposal: 日期/时间字面量增强（Date and Time Literal Enhancements）

_Related: [vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；[vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)（[#27 – Guid literals](https://github.com/dotnet/vblang/issues/27)）；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；2014-02-17 LDM "International date literals"（已实现，进入 Main）_

### 场景与缺口

We started from a genuinely VB-shaped observation：`#...#` 字面量今天表达不了毫秒。`Dim d As Date = #1/1/2026 10:00:00#` 只能走到秒。想要毫秒精度，就必须用构造器——而构造器不是常量，于是连一个"毫秒精度的 `Const` 日期"都写不出来：

```vb
' 今天：
Const c As Date = #2026-01-01#              ' 合法，Date 是 Const 合法类型
Dim d As Date = #1/1/2026 10:00:00#         ' 合法，精度到秒
Dim d2 As Date = New Date(2026, 1, 1, 10, 0, 0, 200)   ' 今天唯一的毫秒途径，但不能作 Const
```

这个缺口是真实的、无争议的、纯增量的。我们喜欢它。

But then the proposal 的其余部分开始狂奔。原文（Anthony 第 14 章，L2402–2420）一口气提出：`Date` 字面量内支持 `DateTimeKind`（`UTC`/`Local`）后缀；为 `DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 引入内置字面量（`#...#d`/`#...#t`/`#...#s` 与 `#1h 35m#s` 单位组合）；甚至造出一个"起止时刻 + 偏移量"的 `DateTimeOffset` 区间字面量。原文自标 `?`，明言未定稿。

We立刻想起一件事——**这个方向在 2014 年被主线显式划出过范围**。2014-02-17 LDM 的 "International date literals"（已实现、进入 Main）白纸黑字写着：

> "Design is merely to offer ISO syntax for the range of things that are covered by existing date literals for DateTime (so: no timezone, no milliseconds, no DateTimeOffset)."

也就是说，毫秒、时区、`DateTimeOffset` 三样东西不是被遗漏，而是被**有意排除**。这份建议等于要求主线重开一扇 2014 年亲手关上的门。重开不是不可以——但提案必须给出理由，而它没有。全篇没有一句提到 2014 年的这次决议。

另一条主线线索在 2017：对"日期/字面量"这类需求，主线最近的思考方向是**注解字符串而非新的字面量类型**。2017-10-18 讨论 `#27 Guid literals` 时：

> "After discussing the scenario for GUID literals with customers I don't believe they are necessary. There isn't a good case to be made for an expression typed as `System.Guid` as the dominant use case (attributes) can only take compile-time/CLR constants, i.e. strings. The only notable benefit I've heard from users is _validation_."

而注解字符串的设想要害是：日期（连同 GUID、URI、IP、路径）只是"可开放扩展的注解标识符集合"里的一个——**它刻意不给日期一个新的运行时类型**。这正是本提案与主线 2017 方向的正面对撞点。

### 候选方案

**PROPOSAL A — 全量按原文。** 毫秒 + Kind 后缀 + 四个类型后缀 + TimeSpan 单位子文法 + `DateTimeOffset` 区间语法，整包落地。`? #...#` 形态全部照单全收。

**PROPOSAL B — 仅毫秒扩展。** 只在既有 `#...#` 的"日期 + 时间"格式内支持小数秒（`.200`），不新增任何类型、后缀、Kind。范围最小，纯增量。

**PROPOSAL C — 毫秒 + 收紧的类型后缀。** 加 `#...#d`/`#...#t`/`#...#s`，但只覆盖 `DateOnly`/`TimeOnly`/`TimeSpan`，强制 ISO（`#2026-07-04#d`）杜绝文化歧义；不引入 Kind 后缀；不引入 `DateTimeOffset` 区间。

**PROPOSAL D — 不新增字面量类型，走主线既有轨道。** 需要校验的日期/时长交给 #184 注解字符串 + analyzer（对，`"..."#Guid` 那种 `#` 标签），需要结构化数据走 #101 JSON 字面量。字面量形态一个都不加。

**PROPOSAL E — `TimeSpan` 单独立法。** 承认"时长"是四个目标类型里唯一样板真的痛者，单独为它设计一个时长字面量（单位组合或 ISO 8601 风格 `#PT1H35M#`），不捆绑 `DateOnly`/`TimeOnly`/`DateTimeOffset`。

### 权衡：Q&A

- **A vs B：捆绑还是单一？** 一份建议里至少塞了五个彼此独立的能力（毫秒、Kind、类型后缀、单位子文法、区间语法），边界互相纠缠。这正是评价标准里"一份提案混杂多个独立特性"的红旗。毫秒是唯一无争议、无连带设计决策的项；其余每一项都各自欠一整套文法、语义与兼容性论证。**结论：A 作为整包不成立，先拆。**
- **毫秒的语法边界。** `.200` 位于闭 `#` 之前，属于 `#...#` 体内部，tokenizer 无需向前看——这是它干净的原因。但位数规则未定：`.2`/`.20`/`.200` 是否都合法？是否强制三位对齐 `DateTime` 的刻度粒度？`#1/1/2026 10:00:00.200 AM#` 与 AM/PM 共存是否允许？2014 年对 `24:00` 的答复（"The current DateTime constructor does not allow you to pass '24' for the hours… So we should disallow this."）提示我们：字面量不该比运行时构造器更宽容。这些都要写进 BNF。
- **Kind 后缀 vs 2014 决议。** 2014 年的 Q2/A2 是逐字可引的：
  > "Q2. Do these always produce a datetime with .Kind = DateTimeKind.Unspecified? A2. Yes, since current VB DateTime constants always do."
  
  引入 `UTC`/`Local` 就是推翻"always Unspecified"。`UTC` 本身可作常量（Kind=Utc 只是刻度 + 一个标记），但价值边缘——真需要 Utc 时 `#...#` + `.ToUniversalTime()` 也行；`Local` 则更糟：Kind=Local 表示"该刻度按本机时区解释"，把它烤进编译期常量，换一台机器部署语义就错。**结论：`Local` 与"常量"概念自相矛盾，Reject；`UTC` 若要做，需要显式书面理由推翻 2014 决议，现列为 Consider。**
- **类型后缀 vs VB 的字面量模型。** 2014 年 #43（Byte/SByte 后缀）留下了一句关键自白：
  > "Literals are NOT target-typed in VB. They are integers (unless with type suffix)."
  
  后缀决定字面量类型确实是 VB 的既有模型（`L`/`S`/`F`/`D`/`R`/`SB`/`UB`）——但那组后缀服务的全是**内置数值类型**。`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 不是 VB 内置类型，连关键字都没有（`Date` 是 `DateTime` 的关键字，`DateOnly` 不是）。更硬的问题是撞车：VB 大小写不敏感，`s` ≡ `S`（Short 类型字符），`d` ≡ `D`（Double 类型字符）。把 `#...#s` 定为 TimeSpan、`#...#d` 定为 DateOnly，等于同一个字形在两种字面量族里各表一义。2014 年团队对后缀字母冲突有多敏感？原文可证——Byte 缺后缀是因为 "the obvious choice 'B' is already taken by hex digit"。同一敏感度在此处被直接违反。
- **Const 兼容性——字面量的核心价值落空。** `Date` 是 VB 的 Const 合法类型（`Boolean`/数值/`Char`/`String`/`Date` 之内），所以 `Const c As Date = #...#` 成立。`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 都不在 Const 类型集合里（`Probably`，需核对最新 VB 规范；但 `Const t As TimeSpan = ...` 在今天的 VB 里不成立是确定的）。于是 `#1h 35m#s` 只能是一个**运行期初始化**的值，进不了 `Const`、进不了属性实参——而"字面量应当能作编译期常量"恰恰是 `#...#` 的核心资产。这与 #184 对 Guid 的判词同构：主导用例（属性实参）只能用编译期常量，而新类型字面量给不了。
- **文化：`#7/4/2026#d` 是倒退。** 2014 年整个 ISO 运动的动机就是消灭 `#2010/10/11#` 这种月日歧义——原文 "Everyone knows ISO syntax. They won't need to stop to wonder whether #2010/10/11# means November 10th or October 11th"。而本提案的 `DateOnly` 示例用的是 `#7/4/2026#d`（M/d 序）。为新字面量类型复用 M/d 格式 = 把 2014 年刚消灭的歧义带回一种**全新**的字面量。若做类型后缀，必须强制 ISO（`#2026-07-04#d`）。
- **`DateTimeOffset` 区间语法——我们 `Suspect` 概念混写。** `#4/2/2007 7:23:57 PM - 4/3/2007 2:23:57 AM = -07:00:00#` 读起来是一个"起止区间 + 偏移"的结构——它是两个时刻之间的**时长**，不是一个"带偏移的**单点**"。`DateTimeOffset` 在运行时是"一个时刻 + 一个偏移"，不是区间。这个语法造出的值，语义上装不进 `DateTimeOffset`。且 `-` 同时扮演"起止分隔"与"偏移符号"，`=` 是第三个角色——一个字面量里三个运算符重载。这不符合任何"读起来像英语"的标准。We `Suspect` 作者把"时间范围"与"时区偏移"两个概念叠在了一起。
- **示例不可编译。** 提案与原文示例都以 `? ` 前缀开头——这是探索/交互标记，不是合法 VB。`Dim d = ? #1/1/2026 10:00:00.200#` 今天编不过。`#12:00#t` 还依赖另一个未核实前提：纯时间字面量 `#12:00#` 今天是否合法（见 OPEN QUESTIONS）。评价标准把"示例无法编译"列为红旗；这里连"本特性的最小可运行演示"都没有。
- **与 #101/#184 的轨道关系。** 主线已有两条"复杂字面量"轨道：JSON 字面量（#101，2017 结论 "Decision: Table, wait for feedback/scenarios and more matching"）与注解字符串（#184，刻意不给新类型）。本提案是第三条轨道。更糟的是**视觉撞车**：#184 的注解字符串以 `"..."#Guid` 收尾，若本提案落地 `#...#s`，则一个以 `#s` 收尾的 token 可能是"日期字面量后缀"也可能是"字符串注解标签"——读者必须在两个机制之间猜。我们不想把后缀字形空间搞成公海。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

毫秒在 `#...#` **体内**，闭 `#` 前，无向前看问题——这是它干净的技术原因。真正的新文法在外侧：
- **后缀附着**：`#1/1/2026#d` 的 `d` 必须并入字面量 token。今天 `#...#` 后紧跟字母是文法错误，所以无既有代码破坏；但 `#...#d` 与 `#...# d`（空格 + 变量 `d`）必须由 tokenizer 精确区分。
- **`UTC`/`Local` 词内识别**：`#...#` 体今天是"固定格式"（日期 + 可选时间），要接受尾随单词 `UTC`/`Local`，体文法第一次引入"关键字式的自由词"。`#1/1/2026 UTC#` 与一个恰好叫 `UTC` 的变量无关——但读者要接受"体里出现了词"。
- **`#...#s` vs `"..."#s`**：见上，与 #184 注解字符串的 `#标签` 字形撞车。两条机制若要并存，需要极强的上下文区分。
- **`24:00`**：2014 年 A1 已答复 disallow；新文法应沿用，但 BNF 需显式写死。

#### 2. 角案例与边界语义

- **小数秒位数**：`.2`/`.20`/`.200` 是否都合法？是否强制三位？是否允许超过三位（`.2000` 超出 `DateTime` 刻度，应报错还是取整）？
- **负 `TimeSpan`**：原文未决。`#-00:30:00#s`？`#-30m#s`？`#...#` 体内允许负号后，与 `-` 其他用法如何区分。
- **零时长与单位合法组合**：`#0s#s`？`#1d 24h#s`（归一化规则）？`#1.5h#s`（小数单位）？
- **`DateOnly` 的月日歧义**：`#4/2/2007#` 到底是 4 月 2 日还是 2 月 4 日——强制 ISO 则消失，M/d 则复现。
- **闰秒/夏令时**：`Local` 若存在，DST 转换边界处的常量语义无解（这是 `Local` 必死的第二个理由）。
- **纯时间字面量**：`#12:00#t` 依赖"time-only 是否合法"；若今天不合法，则 `#...#t` 是给一种新字面量族奠基，而不是扩展现有格式。

#### 3. 作用域与绑定

语义模型对 `#...#d` 应返回 `DateOnly` 类型的常量符号。问题在元数据：`Date` 常量有 CLR 内建的元数据编码（`CorElementType` 级常量），而 `DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 没有——它们**无法作为常量写入元数据**。这意味着 `#...#s` 的字面量"常量"只能退化为构造调用，语义模型里它不是常量节点，`Const`/属性实参全部出局。这是第 3 项追问里最硬的一条 CLR 约束。

#### 4. 与既有特性的交互

- **类型字符撞车**：`s`/`d` 与 Short/Double 类型字符在大小写不敏感下同形。现有数值字面量不受影响，但"一个字形、两种字面量语义"的一致性账要还。
- **`#` 预处理指令**：行首 `#` 是 `#If`/`#Region`。`#...#` 在表达式内，无冲突——但未来若有人写行首 `#1h...`（不会，但文法书要防）。
- **与 JSON/XML 字面量分类器**：IDE 的 token 分类与着色要认识新后缀；2014 年笔记提到 "the prettylister prints into ISO format"——新子文法的 pretty-list 归一化必须同场设计。
- **晚绑定**：字面量强类型，`#...#s` 赋给 `Object` 走装箱，无晚绑定分叉。

#### 5. Breaking change 与兼容性

全部新增形态今天都是**非法语法**，没有既有代码会被重新编译改变行为——这一点我们确认，无硬破坏。但有三种"软破坏"：
- **文化回归**：`#7/4/2026#d` 的 M/d 语义随机器文化漂移，是 2014 年 ISO 决议想要消灭的（重编译换机器，含义变了）。
- **后缀一词两义**：`#...#s` 落地后，读者在"TimeSpan"与未来 `"..."#s` 注解标签之间被迫猜。
- **推翻 2014 范围决议**：重开"no timezone, no milliseconds, no DateTimeOffset"边界而不书面说明理由，破坏设计史的可审计性。

#### 6. Option Strict / 编译选项分叉

无实质分叉：字面量强类型，两条路径行为一致。`Dim x = #1/1/2026 UTC#` 的类型仍是 `Date`（Kind 不同而已），宽松模式下赋给 `Object` 也照旧。这条我们干净通过。

#### 7. IDE / IntelliSense

后缀要并入字面量 token 供分类器着色；`#...#` 体内的 `UTC`/`Local` 需要新的语法着色；`#1h 35m#s` 的单位部分需要语法高亮设计。2014 年 prettylister 已把日期规范为 ISO——新格式的规范形态（`#1h 35m#s` 还是 `#PT1H35M#`？）必须在 IDE 落地前定稿，否则两套显示并存。

#### 8. 数据 / 普遍性

没有任何量化数据。毫秒常量是真实的但小众（定时、日志、科学计算）；`TimeSpan` 时长是四个类型里唯一"样板确实痛"者（`TimeSpan.FromHours(1) + TimeSpan.FromMinutes(35)` vs `#1h 35m#s`）；`DateOnly`/`TimeOnly`/`DateTimeOffset` 的构造与 `Parse` 样板并不算狰狞。`Suspect`：整包是"类型系统章节练习"多于"用户痛点驱动"。参考 #101 在 2017 年的收尾——"Table, wait for feedback/scenarios and more matching"——主线对"新字面量类型"的胃口是等场景，不是造语法。

#### 9. 更简替代

- 毫秒：`New Date(2026, 1, 1, 10, 0, 0, 200)` 覆盖运行期，但覆盖不了 `Const`/属性实参——**这是毫秒字面量的独有价值**，替代不了。
- `TimeSpan`：构造器/`From*` 组合确实啰嗦，但 `TimeSpan.Parse("1:35:00")` 一次调用也可接受。
- `DateOnly`/`TimeOnly`/`DateTimeOffset`：`Parse`/`FromDateTime` 一行搞定，替代够近。
- 校验/意图场景：**#184 注解字符串 + analyzer**——主线 2017 已为"日期、GUID、URI"这类需求指了这条路；analyzer 能告诉人"这里应为合法日期"，代价是零语言面。
- 结构化数据：**#101 JSON 字面量**。

#### 10. 成本 / 优先级

全量 A 的实现面：一条新的 TimeSpan 单位子文法、一条区间子文法、tokenizer 后缀附着、体文法扩展（Kind 词）、常量折叠、元数据编码论证、分类器与 pretty-list。每一项都是独立成本，而价值密度递减。毫秒扩展是最小成本、最高确定性；`TimeSpan` 时长是"值得但值得单独立法"；其余配不上实现成本。

#### 11. 运行时 / CLR 硬约束

`Date` 常量可入元数据（内建编码）；其余四种类型无常量编码，`#...#s`/`#...#d`/`#...#t`/`#...#dto` 的"常量"退化为运行期构造——字面量的定义（compile-time constant）在 CLR 层面立不住。这是把 `DateOnly`/`TimeSpan` 挂上 `#...#` 的根因性障碍，不是文法打磨能绕过的。PEVerify 本身无碍；障碍在常量语义。

#### 12. 值不值得做

逐项打分：
- **毫秒**：价值 真（Const 缺口） × 成本 小 × 风险 无 = **值得**。
- **`UTC` Kind**：价值 边缘 × 成本 中（推翻决议、定比较规则） × 风险 小 = **Consider，不主动**。
- **`Local` Kind**：价值 低 × 成本 中 × 风险 高（常量机器相关性、DST） = **Reject**。
- **`DateOnly`/`TimeOnly` 后缀**：价值 低 × 成本 高（撞车、Const 矛盾、文化） × 风险 中 = **Table**。
- **`TimeSpan` 时长**：价值 中 × 成本 中 × 风险 中 = **值得单独立法**，不与整包捆绑。
- **`DateTimeOffset` 区间**：价值 疑（概念混写） × 成本 高 × 风险 高 = **Reject**。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：无硬破坏，但文化回归与后缀一词两义是"软破坏"，标准不该放过。
- **保持 VB-like（原则 #2）**：`#...#` 载体是 VB 的（继承 VB6），毫秒扩展正中；但 M/d 文化歧义正是 2014 年判定"不像 ISO、要消灭"的东西；后缀撞车违反 VB 对类型字符的洁癖。
- **不引入"第二种做事方式"（原则 #3）**：这是最重的一条。主线已有 JSON 字面量（#101）与注解字符串（#184）两条复杂字面量轨道，本提案是**第三条**。第三条的论证义务远高于常规特性。
- **默认跟随 C#（原则 #4）**：C# 没有日期/时长字面量（`DateOnly`/`TimeSpan` 都是 API 构造）。发明它们需要"compelling reason"，而本提案给的是"更易读"——对非头条特性不够。
- **读起来像英语（原则 #5）**：`#1h 35m#s` 尚可；`#4/2/2007 ... - 4/3/2007 ... = -07:00:00#` 是反例。
- **不为边缘场景加特性（原则 #6）**：`DateOnly`/`TimeOnly`/`DateTimeOffset` 落在边缘；毫秒与 `TimeSpan` 不落。
- **避免隐蔽语义变化（原则 #7）**：`#...#s` 的一词两义是隐蔽性的；毫秒扩展无此问题。
- **不与既有语法冲突（原则 #8）**：`s`/`d` 与 Short/Double 撞车，直接命中此条。
- **消除常见样板（原则 #9）**：`TimeSpan` 最痛，毫秒次之；这是本特性最强亮色。
- **冗长只在有用时是美德（原则 #10）**：`DateOnly.Parse("2026-07-04")` 不算病态冗长，不必为它发明字面量。
- **与主线关系（对照表 2.3）**：主线 2014 显式圈定 ISO 字面量范围（no ms / no timezone / no DTO）并已实现；2017 对日期类需求走向注解字符串（#184/#27，刻意不给新类型）；#101 JSON 字面量 Table 等场景。本建议属 **Anthony 独立延伸**，且与主线 2017 方向**部分冲突**（第三种机制 vs 注解/结构化轨道），与主线 2014 决议**范围冲突**（要求重开边界）。毫秒扩展本身则与主线完全相容——它是 2014 范围之外、但方向一致的纯增量。

### RESOLUTION:

1. **毫秒扩展（PROPOSAL B）原则上采纳**，作为本特性唯一立即落地项：`#...#` 体在闭 `#` 前支持小数秒，不新增类型/后缀/Kind。范围最小、无破坏、补上 `Const` 毫秒日期的真实缺口。
2. **`Kind` 后缀**：`Local` **Reject**（编译期常量与机器时区语义自相矛盾，DST 边界无解）；`UTC` **Consider**（价值边缘；若做，须书面推翻 2014 年 Q2/A2 "always Unspecified" 决议并定比较/转换规则，不默认做）。
3. **类型后缀包（`DateOnly`/`TimeOnly`/`DateTimeOffset`）Table**：四个障碍未解（非 Const 类型集合、与 Short/Double 类型字符撞车、非内置类型无关键字、M/d 文化歧义回归）之前不做。
4. **`TimeSpan` 时长字面量单独立法（Consider 方向）**：它是四者中唯一样板真痛者，值得一次独立、边界清晰的提案；必须先行论证 CLR 无常量编码下的"字面量"身份、强制无文化歧义的格式，以及负时长与归一化规则。
5. **`DateTimeOffset` 区间语法 Reject**：语义与 `DateTimeOffset`（单点 + 偏移）不符，`-`/`=` 三重角色，We `Suspect` 原文概念混写。
6. **移出范围**：`Half`/`Int128`/`UInt128`/`BigInteger`/`System.Numerics` 特殊处理、从 `IEquatable`/`IComparable` 推断运算符——均与字面量无关，属类型系统章节的其它议题，本次不做。
7. **与 #101/#184 对表**：任何新的字面量形态，先书面回答"为什么不是注解字符串（#184）或 JSON 字面量（#101）的子集"，否则不进入设计。

### Implication:

- 起草毫秒扩展 speclet：BNF（`#...#` 体内小数秒）、位数规则（`.2`/`.200`、超三位报错）、与 AM/PM 的共存、`24:00` 沿用 2014 disallow、pretty-list 规范形态。
- `TimeSpan` 时长字面量另立独立 proposal：先行论证常量身份、文化无关格式、负值语法、单位归一化。
- 补一份 Compatibility 与一致性文档：既有 `#...#` 语义不变性、类型字符一词两义的读者认知影响、与注解字符串 `#标签` 的字形冲突。
- 与 JSON 字面量 / 注解字符串团队对表：确认后缀字形空间（`#s`）不与之撞车。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：纯时间字面量 `#12:00#` 今天是否合法？`#...#t`（TimeOnly）若落地是在"扩展现有格式"还是"奠基新字面量族"——我们在会场上未能核实，标注 `Suspect`。
- `OPEN QUESTIONS`：小数秒位数规则（`.2`/`.200`/超三位）。
- `OPEN QUESTIONS`：若 `UTC` 前进，`Kind=Utc` 与默认 `Unspecified` 的比较/转换规则（`=` 只看刻度、`ToUniversalTime` 分叉）。
- `OPEN QUESTIONS`：`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 能否进入 VB Const 类型集合——需核对最新 VB 规范与 CLR 常量编码（`Probably`：不能；`Date` 能）。
- `TODO`：量化"毫秒 `Const` 日期"与"`TimeSpan` 时长"的真实占比，为普遍性补证据。
- `Follow-up`：若 `TimeSpan` 独立提案采用 ISO 8601 风格（`#PT1H35M#`），评估与单位风格（`#1h 35m#s`）的可读性差异与分类器成本。

### 状态

- **LDM 状态**：毫秒扩展 = **Active**；`TimeSpan` 时长字面量 = **Consider（单独立法）**；`UTC` Kind = **Consider**；`DateOnly`/`TimeOnly`/`DateTimeOffset` 类型后缀 = **Table**；`Local` Kind 与 `DateTimeOffset` 区间语法 = **Reject**。
- **三态判定：Active（仅毫秒）/ 拆分后各归各位**——毫秒先行落地；`TimeSpan` 以独立 proposal 续议；类型后缀包与区间语法不动。整包 A 我们不背书。

---

## 附录：特性评价

# 建议评价报告：proposal-date-time-literals.md

## 评价对象

- 建议：proposal-date-time-literals.md — 日期/时间字面量增强（毫秒、`DateTimeKind`、`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 类型后缀）
- 来源：Anthony 原文第 14 章 "Type-System Enhancements"（`..\AnthonyDesign_wordpress.txt` L2402–2420：毫秒、Kind、四类型后缀、`#1h 35m#s`、`DateTimeOffset` 区间、"Not shown" 均出自该段）
- 配方目标：让 `#...#` 表达毫秒与 Kind，并为 `DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 引入内置字面量，书写更直白、降低出错率

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。毫秒动机清晰，但整包示例以 `?` 前缀书写、不可编译；`#12:00#t` 依赖未核实的 time-only 前提；`DateTimeOffset` 示例语义与目标类型不符；未决问题 ≥4 个关键设计点，效果证据按规则封顶 | 已检查 | 无原型/运行结果；改进强度不可度量；核心语法（Kind、后缀、区间）均未定型 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。载体 `#...#` 是 VB 基因，但五个独立能力捆绑；类型后缀撞车 `s`/`S`（Short）、`d`/`D`（Double）；M/d 文化歧义复现；`DateTimeOffset` 区间语法非 VB 风格 | 已检查 | 毫秒子项单独评可到 4–5；整包被捆绑拖至 2；与主线 #184/#27 的注解字符串轨道冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全，但无 BNF/文法、无兼容性分析；Drawbacks 泛（"需小心界定"一句带过）；未决问题把"Not shown"（`Half`/`Int128`/`BigInteger`/接口推断）混入本特性；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 示例不可编译；关键边界（小数秒位数、后缀附着、`-` 双重含义）未展开；DateTimeOffset 语义矛盾未自察 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。暗风险突出：推翻 2014 范围决议而不书面说明；文化歧义回归；第三种字面量机制违反"不引入第二种做事方式"；`Local` 常量机器相关性 | 已检查（预测待定） | 风=与 #101/#184 轨道冲突未识别；光=毫秒盘活既有 `#...#` 资产是唯一正向；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 14 章，标注可溯源；但未提及继承 VB6 的 `#...#` 载体、未提 2014 ISO 决议与 2017 #184/#27 主线对照；`DateTimeOffset` 区间疑似概念混写（Suspect） | 已检查 | 未声明主线血缘（2014 范围决议、2017 注解字符串方向）；成分影响预估与实际（Const 矛盾）明显偏差 |

## 设计原则对照

- **与 VB 基因：部分一致、主体偏离**——毫秒扩展一致（消除样板 #9、载体继承 VB6）；类型后缀包偏离（"Literals are NOT target-typed in VB"、后缀一词两义、文化歧义回归、第三种字面量机制 #3）。
- **与主线关系：Anthony 独立延伸，且部分与主线冲突**——与主线 2014 年 ISO 决议（"no timezone, no milliseconds, no DateTimeOffset"，已实现进 Main）范围冲突，要求重开边界；与主线 2017 年注解字符串方向（#184/#27：日期列为"刻意不给新类型"的注解示例）机制冲突；与 #101 JSON 字面量（2017 Table 等场景）轨道冲突。毫秒扩展与主线相容、方向一致。
- **破坏性变更：无硬破坏**（新增形态今天全部非法）；但有三种软破坏——M/d 文化回归、后缀字形一词两义、重开 2014 决议边界而不书面说明。需在 spec 中显式交代。

## 总评

- **达成程度：部分达成**——毫秒子项成立（真实缺口、纯增量）；整包概念未成形，四个目标类型各欠独立文法/语义/兼容性论证。
- **LDM 三态建议：Active（仅毫秒）**——毫秒先行落地；`TimeSpan` 时长字面量以独立提案 Consider；`UTC` Kind Consider；`DateOnly`/`TimeOnly`/`DateTimeOffset` 类型后缀 Table；`Local` Kind 与 `DateTimeOffset` 区间语法 Reject。整包 A 不背书。
- **主要问题**：① 一份提案捆绑五个独立特性，边界模糊；② 示例不可编译（`?` 前缀、time-only 未核实）；③ `DateOnly`/`TimeSpan` 等非 Const 类型与"字面量=编译期常量"的 CLR 矛盾未分析；④ 类型后缀与 Short/Double 类型字符在大小写不敏感下撞车；⑤ `#7/4/2026#d` 的 M/d 文化歧义是 2014 年 ISO 决议明确要消灭的倒退；⑥ `DateTimeOffset` 区间语法概念混写。

## 返工建议

- **拆分特性**：毫秒扩展单独立文（BNF、小数秒位数 `.2`/`.200`/超三位、与 AM/PM 共存、`24:00` disallow、pretty-list 规范）；`TimeSpan` 时长字面量另立独立 proposal，先行论证常量身份、文化无关格式（ISO 8601 `#PT1H35M#` 与单位风格对比）、负值/归一化语法；删除或彻底重设计 `DateTimeOffset` 区间语法。
- **补充证据**：可编译的最小演示（去掉 `?` 前缀）；`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 是否进入 Const 类型集合的规范核对；文化敏感性矩阵（`#7/4#` 在 US/GB/zh-CN 下的含义）；与 2014 年 ISO 决议、"Literals are NOT target-typed"、2017 #184/#27 注解字符串的关系书面论证。
- **未决问题处理**：`Half`/`Int128`/`UInt128`/`BigInteger`/接口推断运算符移出本特性（属类型系统章节其它议题）；`UTC` 若做，先定 Kind 比较/转换规则并书面推翻 2014 Q2/A2；确认 time-only `#12:00#` 现状后再谈 `#...#t`。
- **设计探索**：与 #101 JSON 字面量共享文法基础的可能性；后缀字形空间（`#s` 与注解字符串 `"..."#s`）的防撞车约定；`TimeSpan` 时长字面量如果走 ISO 8601，分类器与 pretty-list 的成本。

---

## 附录：C# 生态与互操作考量

> 本附录补充本提案与 C#/CLR/.NET 生态的对应关系，不改写正文。依据：`..\..\csharplang-index.md`（下称「索引」）与 `..\..\csharplang` 镜像。本提案主题（日期/时间字面量）在 C# 侧**几乎不存在对应特性**，故本附录以「C# 生态如何表达日期/时间常量」为主线，逐项对照。C# 原文引用均先在 `..\..\csharplang` Grep 核实，逐字转抄并标注来源。

### 相关 C# 现实方向

#### D1 C# 没有日期/时间字面量——事实与机制

- 在 `..\..\csharplang\proposals` 全库 Grep `date literal`/`time literal`/`DateTime literal`/`DateOnly`/`TimeOnly`/`TimeProvider` **零命中**（已核实）。C# 从未设计、也从未提案任何日期/时间字面量；`#...#` 是 VB 继承 VB6 的独有载体。这与正文「默认跟随 C#（原则 #4）」的判断一致：C# 无此特性可跟随。
- C# 生态表达日期/时间「常量」靠三种机制，均**不是编译期常量**：
  1. **`static readonly DateTime` 字段 + 构造器**（`new DateTime(2026, 1, 1, 10, 0, 0, 200)`）——运行期一次性初始化；C# 的 `const` 类型集合不含 `DateTime`，故 C# 连「编译期日期常量」这个 VB 资产都没有（VB 的 `Const c As Date` 成立，正文已述）。
  2. **字符串解析**（`DateTime.Parse`/`DateOnly.ParseExact`/`TimeSpan.Parse`）——文化敏感，除非显式格式；这正是正文「更简替代」里给 `TimeSpan.Parse("1:35:00")` 的路径。
  3. **属性实参用 ISO 8601 字符串常量 + 使用点转换**——因为属性实参需要 CLR 常量而 `DateTime` 不是；这与 vblang #27/#184 对 GUID 的判词同构（主导用例是属性实参，只能用字符串常量），也再次指向正文第 4 项追问：非 `Date` 的日期类型字面量进不了属性实参。
- **C# 近邻先例：`u8` UTF-8 字符串字面量（C# 11）**——这是 C# 唯一「字面量产生非 `const` 类型」的落地特性，直接命中正文的 Const 兼容性议题：
  > "A `u8` literal doesn't have a constant value. That is because `ReadOnlySpan<byte>` cannot be the type of a constant today." → `proposals\csharp-11.0\utf8-string-literals.md`（Detailed design）
  > "literal cannot be used as the default value of an optional parameter."（同一段续句）
  C# 的答复是：**接受这个字面量，同时明确它不是 `const`**。但它有补偿性的编译期收益——lowering 成 `byte[]` 存进 PE `.data`：
  > "This means the call site will be allocation free as C# will optimize this to be stored in the `.data` section of the PE file." → `proposals\csharp-11.0\utf8-string-literals.md`（Lowering）
  `u8` 因此是「非 const 字面量」在 C# 生态里的**存在性证明**，但它的动机（编译期编码省启动/分配开销）恰是日期/时长构造不具备的——见下文「现实 vs 提案」对照表。

#### D2 C# 编译器规避运行时解析 API（对日期文法直接相关）

> "Historically the compiler has avoided using runtime APIs for literal processing. That is because it takes control of how constants are processed away from the language and into the runtime." → `proposals\csharp-11.0\utf8-string-literals.md`（Relying on core APIs）

- 该段下文给出 Roslyn 历史教训：早期用 `double.Parse` 处理浮点常量，导致「同一常量在不同运行时上编译、含义不同」，最终 Roslyn 自写浮点解析。对本提案的直接含义：**任何把 `#...#` 体文法交给 `DateTime.Parse`/`DateOnly.ParseExact`/`TimeSpan.Parse` 的构想，都会重新引入机器/文化/运行时依赖**，与 2014 年 ISO 决议（消灭 M/d 歧义）同向反制。毫秒扩展的 BNF 必须编译器自持、文化无关——这与正文「文化：`#7/4/2026#d` 是倒退」一致。

#### D3 C# 生态日期时间类型的现实：`DateOnly`/`TimeOnly`（.NET 6）、`System.TimeProvider`（.NET 8）

- `DateOnly`/`TimeOnly`/`TimeProvider` 在 `..\..\csharplang` **零命中**（已核实）——它们是 BCL（dotnet/runtime）类型，不是语言特性，csharplang 不主导（索引 T5：csharplang 不主导 NativeAOT/runtime）。
- `System.TimeProvider`（.NET 8）是**可注入时钟**抽象，方向是把「当前时间」从 `DateTime.Now` 的直接静态读取推向可测试、可注入。含义：C# 生态把时间往**运行时可注入**推，而非往**编译期固化**推——这恰好与正文 `Local` Kind **Reject** 的理由同向（机器相关的时间语义不该烤进编译期常量）。
- 影响：`.vbx` 编译器无论如何都要能绑定 `DateOnly.Parse`、`TimeProvider.GetUtcNow()` 等 BCL 成员；字面量只是附加面，不是互操作前提。

#### D4 索引可引用的 C# interop 背景原文（弱相关，仅作背景）

- 「The motivation is for interop scenarios and for low-level libraries.」→ `proposals\csharp-9.0\native-integers.md`（Summary）——C# 低层互操作动机（索引第四节已核实）。
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（VB 小节）——C# 对 VB 安全模型的官方表态（索引第四节已核实）。
- 这两条与本提案主题弱相关，仅支撑下文「对 VBScript.NET 的适应建议」的互操作背景，不硬凑进正文判定。

### 现实 vs 提案（逐项对照）

| 提案项（本次判定） | 现实 vs 提案 | C# 生态现实 | 理由 |
|---|---|---|---|
| 毫秒扩展（Active） | **兼容，且是 VB 独有优势** | C# 无日期字面量；毫秒靠 `new DateTime(...,200)` 构造器 | C# 侧无对应特性可冲突；VB `Const` 毫秒日期是 C# 做不到的差异化资产；`Date` 常量有 CLR 级元数据表示，跨语言可读（精确机制见 OPEN QUESTIONS） |
| `UTC` Kind（Consider）/ `Local` Kind（Reject） | **脱节 / 无对应** | C# 无常量 Kind 概念；`DateTime.SpecifyKind`/`TimeProvider` 是运行时值 | C# 生态用可注入时钟而非编译期 Kind；`Local` 的机器相关性论点与 `TimeProvider` 方向同向，反证 `Local` Reject 成立 |
| 类型后缀包 DateOnly/TimeOnly/DateTimeOffset（Table） | **部分兼容、多数脱节** | 类型存在（.NET 6）、无字面量；`u8` 先例证明「非 const 字面量」可存在 | `u8` 的动机是编译期编码省运行时开销，而日期/时长构造开销极小 → C# 先例**反而削弱**类型后缀的动机；`s`/`d` 撞车是 VB 内部问题，C# 无此约束 |
| `TimeSpan` 时长字面量（Consider 单独立法） | **兼容，动机需重估** | C# 用 `TimeSpan.FromHours`/`Parse`；无字面量 | C# 侧论据：`TimeSpan` 单 `Int64` ticks 字段可发射为静态初始化（近似零开销，同 `u8` 的 PE `.data` 路径），是绕过 Const 障碍的现实出口；但 C# 自己未做，说明生态接受一行 `Parse` |
| `DateTimeOffset` 区间语法（Reject） | **脱节** | C# 无区间字面量；`DateTimeOffset` 是单点 + 偏移 | 与正文的概念混写 Reject 一致，C# 生态无对应物 |

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：`#...#` 是编译期强类型特性，无反射、无晚期绑定，天然 AOT/trimming 友好（索引 T5/M5）。建议 `.vbx` 保持字面量强类型，**永不**让日期字面量退化为 `Object` 晚期绑定路径；任何「校验/意图」需求走 #184 注解字符串 + analyzer——这与 C# 的 source-gen 方向同向（索引 T6）。
- **source-gen 桥**：日期/时长/GUID/URI 这类「注解常量」在 `.vbx` 中以 source generator / analyzer 面世，而非运行时 `Parse`——与 C# interceptors/source-generators 替代运行时反射的方向一致（索引 T6/M5）。这也契合正文「更简替代：校验交给 analyzer」。
- **识别新元数据**：`.vbx` 编译器需识别 .NET 6+ 的 `DateOnly`/`TimeOnly`/`TimeProvider`（即便无字面量也能正常绑定成员调用）；并参照 unsafe-evolution 的 VB 原文，认识 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute` 等新元数据属性，避免调用 C# requires-unsafe 成员时校验缺失（决策文件 M8）。日期字面量本身不涉 unsafe，此项仅作互操作背景。

### 对既有 RESOLUTION / 三态判定的影响

- **实质判定不变**。C# 侧证据既不推翻也不追加本次任何结论，只补两点：
  1. **毫秒 Active 是 C# 生态最干净的兼容区**：C# 无日期字面量、无方向要求 VB 不做；`Const` 毫秒日期跨语言互操作无碍。这是本次唯一「纯 VB 独有、C# 零冲突」的项。
  2. **Table/单独立法方向不变**：`u8` 先例（「非 const 字面量」可在生态存在）被动机分析吸收后**不构成支持类型后缀的证据**——`u8` 有编译期编码收益而日期/时长没有。`TimeSpan` 独立提案反而多了一条 C# 侧论据（ticks 静态初始化路径），值得评估。
- 一处待补的 **OPEN QUESTIONS**：`TimeSpan` 独立提案若走「发射为静态初始化（ticks 常量字段）」的 lowering，可在 CLR 无常量编码的约束下保留「近似常量」语义——这是 C# `u8` 已验证的路径，不影响本次判定，只影响 `TimeSpan` 未来的立法方向。

### 引用纪律 / OPEN QUESTIONS

- 本附录 C# 原文均逐字摘自 `..\..\csharplang` 并标注来源（D1/D2/D4）；索引第四节 6 段中仅引用了与主题相关者。`DateOnly`/`TimeOnly`/`TimeProvider`/`DateTimeConstant` 在 csharplang 的零命中结论基于 Grep 核实。
- **OPEN QUESTIONS**：VB `Const c As Date` 的精确 CLR 元数据机制——正文第 3 项追问称「`CorElementType` 级常量」，但 `..\..\csharplang` 全库无 `DateTimeConstant` 命中（Roslyn 的精确发射形式属 dotnet/roslyn，本镜像未含）。无论 `Constant` 表还是 `DateTimeConstantAttribute` 自定义属性，结论一致：`Date` 有跨语言可读的元数据路径，而 `DateOnly`/`TimeSpan` 连该路径都没有——本附录据此不作更精确断言。
- **OPEN QUESTIONS**：`System.TimeProvider` 的 BCL 精确定位（.NET 8，dotnet/runtime 仓库，本库无正文）——标注 **Suspect**，仅供方向参考。
