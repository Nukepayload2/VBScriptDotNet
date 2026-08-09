# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周的话题与最近几周的会议（`ShapeOf` 模式匹配、用户定义模式方法）同处一片实现面，所以讨论得以直接复用既有工作。不过我们今天很快意识到：这份建议表面上是一个特性，实际是**三个不同语义的机制捆在一起**——"存在 `Parse` 时的窄化转换""存在 `TryParse` 时的模式/形状操作符""字符串到枚举的窄化转换"。它们各自的失败语义、Option Strict 分叉和主线关系都不同，捆绑评估会让讨论失焦。我们把它们拆开谈。

## Agenda

* [Proposal: 字符串窄化转换（String Narrowing Conversions）](#proposal-字符串窄化转换)

## Proposal: 字符串窄化转换

_Related: [vblang #27 – GUID Literals](https://github.com/dotnet/vblang/issues/27)；[vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；ModVB：`ShapeOf` 模式匹配、用户定义模式方法、`Let` 声明_

### 场景与缺口

We started from a real and common gap: 从字符串得到 `Guid`、`IPEndpoint`、枚举等值是目前非常常见的操作，但今天的写法要显式调用 `Guid.Parse`、`IPEndpoint.TryParse`、`Enum.Parse`，样板重复且易错。建议的出发点是"让期望的类型直接写在声明处"：

```vb
' 今天：
Let nil As Guid = Guid.Parse("00000000-0000-0000-0000-000000000000")

' 建议：
Let nil As Guid = "00000000-0000-0000-0000-000000000000"
```

这个动机我们认同。把"要变成什么"写进声明、把"怎么变"交给编译器，是典型的消除样板诉求（评价标准原则 #9）。但**怎么变**是全部分歧所在。

### 候选方案

We treated the proposal as three independently assessable mechanisms, and added two alternatives the proposal itself listed.

**PROPOSAL A — 原样三合一。** 按建议原文整体实现：存在 `Parse` 时字符串常量窄化赋值；存在 `TryParse` 时 `ShapeOf ... Is ...` 模式匹配并绑定变量；字符串窄化到枚举。

**PROPOSAL B — 拆分三件、分别限定。**

- **B1 — `Parse` 窄化（赋值场景）。** 关键分叉：作用域是**字符串常量（字面量）**还是**任意字符串表达式**？建议的 Summary 写"字符串常量"，但 Detailed design 未重复该限制，而 `TryParse` 示例用的是变量 `str`。这是两种性质完全不同的特性：若仅限字面量，编译器可在编译期验证并直接发射类型值（接近"`Guid` 字面量"）；若放开到任意表达式，则每次求值都调用 `Parse`，运行时可能抛异常。
- **B2 — `TryParse` 形状操作符。** 并入用户定义模式方法（`proposal-user-defined-pattern-methods.md`）：`IPEndpoint.TryParse(String, Out T) As Boolean` 天然就是一个模式函数，`ShapeOf str Is ip As IPEndpoint` 只是"内置模式函数 + 类型声明模式"的组合。
- **B3 — 字符串到枚举窄化。** 仅限枚举成员名、不区分大小写。

**PROPOSAL C — 只做 B3（枚举窄化）。** `Parse`/`TryParse` 保持显式调用。

**PROPOSAL D — 不做语言特性，提供 analyzer/重构提示。** 建议原文未提，但主线对同类场景多次落到 analyzer（见下）。

**PROPOSAL E — 显式窄化转换操作符协议。** 不靠编译器"探测 `Parse` 方法"，而是由类型作者显式声明 `Narrowing` 转换操作符（建议原文 Alternatives 列出）。编译器不发明约定，只尊重显式声明。

### 权衡：Q&A

- **"三合一"还是拆分？** 必须拆分。三个机制面向三个不同的语义问题：B1 是"赋值时隐式调用可能抛异常的运行时转换"，B2 是"布尔型匹配 + 变量绑定"，B3 是"编译期/运行时的枚举解析"。把它们当同一特性设计，会让每个部分的角案例都得不到应有的注意力。这是"一份提案混杂多个独立特性、边界模糊"的典型红旗。

- **B1 的核心矛盾：字面量还是任意表达式？** 建议自相矛盾。Summary 说"字符串常量"，但给不出"非常量则报错"的规则，也未说明为什么只许常量。We think 若只做字面量，这实际上是 **`Guid`/`IPEndpoint`/`Enum` 字面量**，而主线在 2017 年已经正面看过这个场景，结论相当明确——"[After discussing the scenario for GUID literals with customers I don't believe they are necessary. There isn't a good case to be made for an expression typed as `System.Guid` as the dominant use case (attributes) can only take compile-time/CLR constants, i.e. strings. The only notable benefit I've heard from users is _validation_.]"(#27/#184，2017.10.18)。主线把"验证"需求导向 analyzer 与 tagged string（`"{...}"#Guid`），而不是让编译器解析字符串。若做任意表达式，则每次赋值都变成一次可能抛异常的 `Parse` 调用——这触碰了 B1 的失败语义，见下。

- **B2 与主线已批准的方向重叠吗？** 高度重叠。主线在 2014 年已原则上批准"带隐式声明参数的 Out 实参"，正是 `If Integer.TryParse(s, Out [Dim] x [As Integer]) Then ...`（2014.02.17 #42，标注 "Approved in principle, but needs design work. Aligns with C# "out vars" feature."）。`ShapeOf str Is ip As IPEndpoint` 在语义上就是 `If IPEndpoint.TryParse(str, ip) Then` 加内联声明。We think B2 的新增部分不大，但它把语法挂到了 `Is` 上——而主线在模式匹配里对 `Is` 的态度已经表达过顾虑（见深度追问 #1）。

- **B3 真的是新特性吗？** 未必。VB 运行时（`Microsoft.VisualBasic.CompilerServices.Conversions.ToEnum`）本就支持 `CType("mobile", PhoneKind)` 这种字符串→枚举转换；`Option Strict Off` 下隐式赋值也走运行时转换。B3 的真正含义是"把 `Option Strict Off` 下的行为提到 `Option Strict On` 下、并且由编译器在编译期承担部分验证"。这是一个 Option Strict 语义的移动，不是从无到有的新能力——需要先证明"为什么要在 Strict On 下放开这条窄化"。

- **失败语义是致命伤。** 建议的 Unresolved questions 第一条自己承认"`Parse` 窄化在失败时抛异常还是回退到 `Nothing`/默认值？原文未定义"。这在 2017.04.12 转换操作符专场就已被点名——"[TryCast like behavior – return null? Odd to throw sometimes and return null sometimes.]"对一条隐式窄化而言，"有时抛、有时返回默认值"是不可接受的。B1 若对字面量做编译期验证，失败应在编译期报错；若对任意表达式做运行时转换，失败应抛异常——两条路都必须明确，不能留给实现猜测。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`ShapeOf str Is ip As IPEndpoint` 中的 `Is` 是今天最尖锐的语法问题。主线在 2018.12.19 的模式匹配讨论里明确说过："[Both `Matches` and `Is` were discussed. We think `Is` will have ambiguity issues with the existing use for reference equality.]" `Is` 在 VB 里已经承担"引用相等"语义；把它同时用作"匹配操作符"，`ShapeOf x Is y` 会被读成引用比较还是模式匹配，需要上下文相关判定。且我们记得模式匹配的基本原则——"[a pattern is not an expression, but a thing that when matched results in an expression, in this context a Boolean expression]"——让 `Is` 同时做表达式操作符与模式关键字，模糊了这条分界。B2 若保留，应与 `ShapeOf` 建议（`Select Case ShapeOf input / Case pn As USPhoneNumber`）及用户定义模式方法建议（`ShapeOf stream Is hasAnotherChar(c)`）的既有文法对齐，而非发明 `Is ip As T` 的第三种拼写。

#### 2. 角案例与边界语义

- **B1 字面量路径**：`Let g As Guid = "00000000-..."` 若在编译期解析，编译器要内建 `Guid`/`IPEndpoint`/枚举的解析逻辑——这是把 BCL 的 `Parse` 逻辑复制进编译器（含区域设置、格式规则），编译器膨胀且与 BCL 版本解耦。若不同步更新，新 .NET 版本的 `Parse` 行为变化会让编译器与运行时不一致。
- **B1 表达式路径**：`Let g As Guid = GetUserInput()` 每次求值抛 `FormatException`；`Nothing` 字符串传给 `Parse` 抛 `ArgumentNullException`。与 `CStr(Nothing) = ""` 的既有宽松语义不一致。
- **B3 大小写**：建议要求"不区分大小写"以命中 `Mobile`。但 VB 有 `Option Compare Binary/Text` 作为文件级字符串比较开关，硬编码"枚举名不区分大小写"是与 `Option Compare` 并行的第三套规则。且 `Enum.Parse` 的 `ignoreCase` 参数默认是 `False`——语言要显式选择 `ignoreCase := True`，需要理由。
- **B3 flags 与数字字符串**：`"Home, Office"`（`[Flags]` 组合）、`"3"`（数字字符串）是否支持？建议只展示单个成员名。主线对 flagged enum 的态度见 2018.02.21 #228："[Flagged enums have challenges in both C# and VB. In VB they are rather painful.]" 若 B3 不支持组合，就只覆盖了 `Enum.Parse` 能力的一个子集；若支持，则要与 `CType`/`Enum.Parse` 的组合解析规则保持一致。

#### 3. 作用域与绑定

B2 中 `ip` 绑定到哪个符号？`ShapeOf str Is ip As IPEndpoint` 命中时 `ip` 是声明模式引入的局部变量（如同 `Case pn As USPhoneNumber`），语义模型应返回 `IPEndpoint` 类型的新局部符号；失败时 `ip` 是否被赋值 `Nothing`/默认值、是否进入作用域，都未定义。这与用户定义模式方法建议的 `Out` 变量规则（"匹配失败时 `c` 不参与后续逻辑"）需要一致，否则同一套 `Out` 绑定在"内置模式函数"和"用户模式函数"两条路径行为分裂。

#### 4. 与既有特性的交互

- **Option Strict Off**：B1/B3 在宽松模式下本就可用（运行时转换）。特性实际作用面是 `Option Strict On`。We think 这是本建议与语言核心契约最紧张的地方——`Option Strict On` 的承诺就是"隐式窄化要显式写 `CType`"，而 B1/B3 恰恰要在 `On` 下放行两条隐式窄化。
- **`CType` / `Convert`**：`CType("mobile", PhoneKind)`、`CStr` 等转换已存在且语义明确。B3 隐式化后，`Convert` 与语言内建转换并存，建议自身 Drawbacks 也承认"窄化转换与既有 `CType`/`Convert` 语义并存，容易造成混淆"。
- **`Let`**：示例用 `Let nil As Guid = "..."`。`Let` 在 ModVB 中是 `Dim` 的替换（Anthony 独立延伸），本特性与 `Let` 无特殊交互，但示例对标准 VB 读者需译回 `Dim`。
- **ByRef / 表达式树**：B1 若用于函数实参（建议 Unresolved 最后一条也问了"是否适用于函数实参、返回值等其他上下文"），窄化在 ByRef 位置的 copy-in 语义未定义；表达式树中发射 `Parse` 调用无碍。

#### 5. Breaking change 与兼容性

- 若 B1/B3 仅在"先前会报错的代码"上启用（`Option Strict On` 下今天不能编译），则对既有已编译代码零破坏；但**重编译语义**会变——用户升级编译器后，`Let kind As PhoneKind = "mobile"` 从"编译错误"变成"可编译"，这本身就是一种行为变化（原先靠编译错误拦住的问题开始静默通过，直到运行时才炸）。
- 若 B1 放开到任意表达式，且某类型新增了 `Parse` 方法（库演进），则该类型突然获得一条隐式转换路径——这是"库添加方法改变调用点语义"的隐式协议，破坏性虽小、可预测性差。
- 建议无 Compatibility/breaking-change 分析章节，状态行还是占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）。

#### 6. Option Strict / 编译选项分叉

这是 B1/B3 的命门。We think 在 `On`/`Off` 两条路径下行为**必须一致**——不能"`Off` 走运行时转换、`On` 走编译期 `Parse`"，否则同一份源码在两个项目里失败时机不同。建议未处理该分叉；而选择"两条路径都编译期解析"又等于在 `Off` 下也收紧了行为（原先宽松模式下可接受的运行时不合法字符串变成编译错误），同样是行为变化。

#### 7. IDE / IntelliSense

B2 中 `ip` 在 `Then` 分支内的补全应显示 `IPEndpoint` 成员（`ip.Port`），失败路径不可用——这与用户定义模式方法的 IDE 展示应共用一套实现。B3 的字符串字面量在声明处要不要显示枚举补全？字面量本身是字符串，补全内容是枚举成员名，IDE 需要新的"枚举感知字符串"元数据。这些都没设计。

#### 8. 数据 / 普遍性

从字符串取 `Guid`/枚举/端点是高频操作，我们相信。但主线已经为同类场景（`Guid` 字面量）给出过不同结论——"验证是唯一可见收益，导向 analyzer"。建议没有提供任何频率数据或用户请求支撑，也没有回应主线 #27/#184 的先例。`Suspect`：普遍性是真实的，但"编译器解析字符串"是否是最小解决方式，证据不足。

#### 9. 更简替代

- 显式 `Guid.Parse` / `IPEndpoint.TryParse` / `Enum.Parse`：现状，样板多但确定。
- **Out 实参 + 内联声明（主线 #42，已原则上批准）**：`If IPEndpoint.TryParse(str, ip) Then` 已覆盖 B2 的大部分价值，且不需要新语法、不需要 `Is`。
- **Analyzer / 重构**：主线对验证场景的既定答案（#184 的 tagged string `"{...}"#Guid` 就是为 analyzer 服务的）。B1 的"编译期验证"收益完全可以由 analyzer 提供，且不改变语言语义。
- **显式 `Narrowing` 操作符（PROPOSAL E）**：由类型作者声明，编译器不发明约定。这条对 B1 尤其有力——如果某个类型真的认为"字符串→自身"是合理的窄化，作者应显式说，而不是让编译器扫描 `Parse` 方法。

#### 10. 成本 / 优先级

B1（任意表达式路径）要求编译器认识 `Parse` 签名、选择重载、处理 `IFormatProvider` 与区域设置——接近重写一次 `Conversions` 运行时。B1（仅字面量路径）要求编译器内建多个 BCL 类型的解析逻辑，且与 BCL 版本同步。B2 复用用户定义模式方法，成本最小。B3 复用运行时 `ToEnum`，成本中等。建议把成本最高的 B1 与最敏感的 Option Strict 捆绑，优先级应先拆后评。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题：B1/B3 最终发射 `Parse`/`ToEnum` 调用或直接常量，都是既有 IL。唯一的"约束"是**语义层**的——`Parse` 不是 CLR 概念，编译器靠"方法名 + 签名"猜转换意图，这是纯语言约定，CLR 无法校验一致性。表达式树无碍。

#### 12. 值不值得做

逐条打分：

- **价值**：中等。样板确实存在，但主线已为其中的 `Guid` 场景找到更低风险的出口（analyzer + tagged string）；枚举场景价值最实。
- **成本**：B1 高、B2 低、B3 中。
- **风险**：B1/B3 的 Option Strict 语义移动与失败语义未定义，是真正的暗风险；B2 的 `Is` 语法与主线冲突。

**值不值得做——但绝不能按当前捆绑形态做。** 拆分后：B2 值得并入模式匹配工作；B3 值得单独考虑；B1 以"编译器解析字符串"形态不推荐。

### VB 基因对照

- **消除常见样板（原则 #9）**：动机正中靶心，这是本建议最亮的点。
- **不引入"第二种做事方式"（原则 #3）**：B1/B3 在 `Option Strict On` 下新增一条与 `CType` 并行的隐式路径——正是原则 #3 反对的扩展表面。
- **避免隐蔽的控制流/语义变化（原则 #7）**：B1 的"字面量赋值变成可能抛异常的 `Parse` 调用"是最典型的隐蔽语义变化（`Return?` 被主线拒绝的同类理由）。
- **保持 VB-like（原则 #2）**：`ShapeOf ... Is ...` 的拼写与主线 `Matches` 决策（#337）冲突，`Is` 二义性让"读起来像 VB"打折。
- **读起来像英语、对新手友好（原则 #5）**：`Let nil As Guid = "..."` 读起来确实像英语，这也是我们保留 B3 讨论的原因。
- **与主线关系（对照表 2.3）**：三条子特性分属三种关系——B1 与主线 #27/#184 **冲突**（主线判定 Guid 字面量不必要、验证归 analyzer）；B2 与主线 #42（Out 实参，已批准）+ #337（模式匹配，主线"最期待"）**一致**，但语法选择背道；B3 是主线未接管、Anthony 独立延伸，方向与 VB 宽松基因一致但 Option Strict 分叉未解决。

### RESOLUTION:

1. **拆分，不做三合一。** 捆绑形态下任一子特性的角案例都得不到充分设计，本建议按 A 原样不进入实现。
2. **B1（`Parse` 窄化赋值）：本建议形态下不采纳。** "编译器扫描 `Parse` 方法当作隐式转换"是魔法约定，且与主线 #27/#184 结论冲突（Guid 字面量不必要、验证导向 analyzer）。若未来要做，两条路二选一并配全语义：(a) 仅字符串常量 + 编译期验证（即"类型字面量"，须先正面回应主线 #27/#184）；(b) 显式 `Narrowing` 转换操作符协议（PROPOSAL E），类型作者声明而非编译器探测。失败语义必须在编译期报错与运行时抛异常之间明确选边。
3. **B2（`TryParse` 形状操作符）：并入用户定义模式方法 + `ShapeOf` 建议，不单独落地。** 语义与主线 #42 Out 实参（已原则上批准）重叠；语法不得使用与引用相等冲突的 `Is`，与 #337 的 `Matches`/模式文法对齐。`ip` 绑定规则与用户模式函数的 `Out` 变量规则统一。
4. **B3（字符串→枚举窄化）：Consider，独立成建议再评估。** 这是三条中最有 VB 基因的部分（运行时 `ToEnum` 已存在），但必须补齐：Option Strict `On`/`Off` 行为一致、大小写规则与 `Option Compare` 的关系、`[Flags]` 组合与数字字符串、失败语义（编译期报错 vs 运行时异常）。与主线 #228（flagged enums）工作保持对表。
5. **不引入新的编译器内建解析器。** 不在编译器中复制 `Guid.Parse`/`IPEndpoint.TryParse` 的逻辑（区域设置、格式规则）——那是 BCL 的职责，编译器内建会造成与 BCL 版本解耦的不一致。
6. **验证需求走 analyzer 路线。** 主线 #184 的 tagged string（`"{...}"#Guid`）思路对 B1 的"编译期验证"价值仍然适用，ModVB 若推进验证类场景应与该方向合并，而非改变语言语义。

### Implication:

- 起草拆分后的三份 speclet（或把 B2 并入既有 `ShapeOf`/用户定义模式方法建议、B3 独立成文）。
- 对 B3 做最小原型：`Option Strict On/Off` 下 `Let kind As PhoneKind = "mobile"` 的现状行为、`ToEnum` 的大小写与组合语义，用真实运行结果补证据（当前行为 `Probably` 走运行时转换，需验证）。
- 与主线 #27/#184、#337、#42 各写一段对照：分别说明冲突点、语法对齐点、已批准先例。
- 补 Compatibility 分析：重编译语义变化（`On` 下从编译错误变为可编译）、库新增 `Parse` 的隐式协议风险、Option Strict 两条路径行为表。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：B1 若走字面量路径，`IPEndpoint` 这类"字符串格式有歧义"的类型如何确定解析规则？编译期验证的失败报错文案与 BCL `Parse` 异常是否要一致？
- `OPEN QUESTIONS`：B3 的"不区分大小写"与 `Option Compare Text` 文件共存时，以哪个为准？
- `OPEN QUESTIONS`：B2 的 `ip` 在匹配失败路径是否进入作用域并赋默认值（与用户模式函数 `Out` 规则对齐后待定）。
- `TODO`：用原型验证 `Option Strict Off` 下 `Dim kind As PhoneKind = "mobile"` 与 `Dim g As Guid = "..."` 今天的实际行为（编译与运行时），把"宽松模式本就可用"从 `Probably` 升级为事实。
- `TODO`：量化"从字符串取 Guid/枚举/端点"在真实业务代码的占比，回应主线"验证归 analyzer"的替代方案。
- `Follow-up`：与 #228（flagged enums）对表，确认 B3 是否纳入 `[Flags]` 组合解析。

### 状态

- **LDM 状态：Table（捆绑形态）**；拆分后 B2 并入模式匹配工作（Active），B3 独立为 Consider，B1 以建议形态 Reject。
- **三态判定：Table** — 三机制捆绑、失败语义未定义、Option Strict 分叉未解决；拆分后最有希望的部分（B3）需补语义与证据再单独评审。

---

## 附录：特性评价

# 建议评价报告：proposal-string-narrowing-conversions.md

## 评价对象

- 建议：proposal-string-narrowing-conversions.md — 字符串窄化转换（`Parse`/`TryParse`/枚举）
- 来源：Anthony 原文第 6 章 "Strings and String Pattern Matching"（`..\AnthonyDesign_wordpress.txt` L1251–1309；三组示例 `Let nil As Guid = "..."`、`ShapeOf str Is ip As IPEndpoint`、`Let kind As PhoneKind = "mobile"` 全部出自该章）
- 配方目标：让"期望的类型"直接写在声明处，消除 `Guid.Parse`/`IPEndpoint.TryParse`/`Enum.Parse` 样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（消除样板）、示例可操作；但三条子特性只有 B3 有真实基座（运行时 `ToEnum`），B1 的核心语义（字面量 vs 表达式、失败时机）未定，效果无法在 `Option Strict On` 下稳定显现 | 已检查 | 无原型封顶；B1 语义未定义使主效果在严格模式下"消退"；未回应主线 #27/#184 的 analyzer 替代 |
| 特性 | 2/5 | 锚点 2："多个强无关能力捆绑"。三条机制（隐式运行时转换 / 模式绑定 / 枚举解析）语义互不相干，捆绑削弱 VB 基因；`ShapeOf ... Is` 与主线 `Matches`（#337）冲突；B3 继承 VB 宽松基因但 Option Strict 分叉未解 | 已检查 | 捆绑是"多强无关能力"红旗；`Is` 二义性违背原则 #2/#7 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文逐字一致、4 个未决问题具体诚实；但关键边界（常量 vs 表达式、失败语义、Option Strict 分叉）含糊，缺 Compatibility/breaking-change 章节，状态行占位链接 | 已检查 | 边界含糊集中在效果命门；未决问题 ≥4 个关键设计点 ⇒ 效果证据等级封顶 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/水正向（提速、差异化 DX）；暗风险突出——Option Strict 语义移动、魔法方法约定、与主线 #27/#184/#337 冲突，文档未对冲 | 已检查（预测待定） | 风=与主线模式匹配/验证路线断裂；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源标注准确（Anthony 第 6 章）；继承 VB 运行时转换基因（`ToEnum`）未点明；借鉴 C# `Out vars`/模式匹配概念未标注；未承认与主线 #27/#184 先例的冲突 | 已检查 | 借鉴主线 Out 实参（#42）与模式匹配（#337）未说明；成分（运行时转换/魔法约定）与实际影响有偏差 |

## 设计原则对照

- **与 VB 基因：部分偏离**。动机符合原则 #9（消除样板）、B3 方向符合 VB 宽松/可读基因（原则 #5）；但 B1/B3 在 `Option Strict On` 下新增与 `CType` 并行的隐式路径（违背 #3），`ShapeOf ... Is` 语法违背 #2/#7（与主线 `Matches` 冲突、字面量变抛异常调用是隐蔽语义变化）。
- **与主线关系：分裂**。B1 与主线 #27/#184 **冲突**（Guid 字面量已判定不必要、验证归 analyzer）；B2 与主线 #42（Out 实参，已批准）+ #337（模式匹配，"最期待"）**一致**但语法背道；B3 为 Anthony 独立延伸，方向与 VB 宽松基因一致但 Option Strict 分叉未解。属"主线保守、Anthony 激进"张力（对照表 2.3）的典型。
- **破坏性变更：潜在有**。`Option Strict On` 下"编译错误→可编译"的重编译语义变化；库新增 `Parse` 的隐式协议改变调用点语义；若放开到任意表达式，运行时失败时机变化。建议未做 Compatibility 分析。

## 总评

- **达成程度：未达成（捆绑形态）**——三条子特性中 B1 与主线结论冲突且语义未定义，B2 应并入既有工作，仅 B3 有独立成议的基础。作为单一建议，概念成立但设计不成熟。
- **LDM 三态建议：Table**——拆分后 B2 并入 `ShapeOf`/用户定义模式方法（Active），B3 独立为 Consider（需补 Option Strict 分叉、大小写、flags、失败语义），B1 以"编译器扫描 `Parse`"形态 Reject。
- **主要问题**：① 三机制捆绑、边界模糊；② B1 的失败语义与"字面量 vs 表达式"自相矛盾；③ Option Strict `On`/`Off` 分叉未解决；④ 与主线 #27/#184/#337 的关系未回应；⑤ 状态行占位链接、无兼容性分析。

## 返工建议

- **补充章节**：Compatibility/breaking-change（重编译语义、`Parse` 隐式协议、Option Strict 两路径行为表）；明确 B1 作用域（仅常量 or 任意表达式）；失败语义章节（编译期报错 vs 运行时异常，不得含糊）。
- **补充证据**：最小原型验证 `Option Strict Off` 下 `Dim kind As PhoneKind = "mobile"`、`Dim g As Guid = "..."` 的实际编译与运行时行为（把 `Probably` 升级为事实）；频率/用户请求数据。
- **未决问题处理**：B2 的 `ip` 绑定规则与用户模式函数 `Out` 变量规则统一；B3 的大小写规则与 `Option Compare` 关系、`[Flags]` 组合、数字字符串逐条定论；B1 的编译期验证价值改由 analyzer/tagged string（#184）承担，从语言特性中剥离。
- **设计探索**：PROPOSAL E（显式 `Narrowing` 操作符协议）作为 B1 的替代形态展开；B3 与主线 #228（flagged enums）对表后的完整枚举解析语义。

---

## 附录：C# 生态与互操作考量

> 范围：本提案主题是转换（conversion），正处在 C#/CLR/.NET 互操作表面积的中心，故本附录实质展开。所有 C# 引文均逐字核对自 `..\..\csharplang` 镜像；C# 规范正文（已迁至 dotnet/csharpstandard）在本镜像只有目录 stub，涉及规范原文处标注 **OPEN QUESTIONS**。决策机构称呼沿用正文习惯：C# 侧称 LDT，VB 侧称"主线"。

### 相关 C# 现实方向

**方向 1：C# 隐式转换的契约是"不失败、不抛出、不有损"——对非常量表达式永不隐式收窄。** 这条契约直接命中 B1/B3 的核心（"隐式窄化、运行时可能抛 `FormatException`"）。

- 2022 年 LDT 复查 `checked` 用户定义转换时逐字重申：「Generally, implicit conversions should not fail, throw or be lossy. That's already the case with (most) existing conversions and .NET guidelines.」唯一例外也写明：「There's an exception: integer to floating point conversion in C# does allow loss.」结论把"`implicit` 与 `checked` 互斥"定死：「Stick with proposed restriction (conversions cannot be `implicit` and `checked`)」→ `meetings\2022\LDM-2022-02-07.md`（"Checked implicit conversions"）。
- 提案文档对应表述更短：「In general, implicit conversion operators are not supposed to throw.」（Resolved: Should we support implicit checked conversion operators? → No）→ `proposals\csharp-11.0\checked-user-defined-operators.md`。
- 语法层证据：转换操作符文法里**隐式转换没有 `checked` 形态，只有显式转换才有**——`conversion_operator_declarator : 'implicit' 'operator' type '(' type identifier ')' | 'explicit' 'operator' 'checked'? type '(' type identifier ')'` → 同一文件。
- 即便 C# 自己引入的"可能抛的隐式转换"（数组→`Span<T>` 的协变检查），LDT 也当作 footgun：逐字「This issue, where an implicit conversion can throw, isn't entirely new to the language; after all, `dynamic` can be implicitly converted to any type, and that can throw. But it is likely more prominent than `dynamic`, as with that feature, the user usually intentionally started at `dynamic` and went to a specific type, rather than starting at array and then invisibly going to a `Span<T>` where `IEnumerable<T>` used to be fine.」→ `meetings\2024\LDM-2024-12-04.md`。C# 里能抛的隐式转换只存在于 `dynamic` 与这类特例，且都视为要消除的坑。

**方向 2：C# 唯一允许的"隐式收窄"是常量表达式，且编译期验证、失败即编译错误。** 这是 B1"仅字面量"路径的 C# 对应物（§10.2.11 隐式常量表达式转换）：

- 逐字：「An implicit constant expression conversion (§10.2.11) permits a constant expression of type `int` to be converted to `sbyte`, `byte`, `short`, `ushort`, `uint`, or `ulong`, provided the value of the constant expression is within the range of the destination type.」→ `proposals\csharp-10.0\constant_interpolated_strings.md`；C# 11 扩展至 `nint`/`nuint` 的同款表述见 `proposals\csharp-11.0\numeric-intptr.md`。
- 语义要点：值在目标值域内→编译期放行；越界→**编译错误**，绝无运行时抛。这正是正文对 B1 字面量路径要求的"失败在编译期报错"的 C# 形态。
- C# 11 UTF-8 字符串字面量是"字面量→类型值、编译期转换"的又一例：逐字「When the `u8` suffix is used, the value of the literal is a `ReadOnlySpan<byte>` containing a UTF-8 byte representation of the string.」转换表里 `ReadOnlySpan<byte> s3 = "hello"u8; // Okay.`、`byte[] s4 = "hello"u8; // Error - Cannot implicitly convert type 'System.ReadOnlySpan<byte>' to 'byte[]'.` → `proposals\csharp-11.0\utf8-string-literals.md`。编译期完成编码（动机是省运行时分配与启动开销），但转换只对字面量成立、失败在编译期、无运行时抛——与 B1"类型字面量"同构。

**方向 3：C# 的 `string` 与枚举之间没有任何转换（隐式、显式都没有）。** 证据是 `spec\conversions.md` 的目录结构：§10.2 隐式类目与 §10.3 显式类目里，枚举相关只有 §10.2.4 implicit enumeration conversions（常量 0→枚举）与 §10.3.3 explicit enumeration conversions（数值↔枚举），均不含 `string`。C# 取枚举只能 `(PhoneKind)Enum.Parse(typeof(PhoneKind), str)`，string→枚举永远需要显式步骤 → `spec\conversions.md`（正文在 dotnet/csharpstandard）。

**方向 4：B2 在 C# 有成熟对应——out var 与模式匹配。**

- out var 逐字：「The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument.」→ `proposals\csharp-7.0\out-var.md`；这是 vblang #42 "Aligns with C# "out vars" feature" 的 C# 侧出处。
- 模式不是表达式（正文所引基本原则的 C# 出处）逐字：「A pattern is not an expression, but a separate construct. It can be recursive.」→ `meetings\2015\LDM-2015-01-28.md`。
- `is` 表达式文法逐字：`is_pattern_expression : relational_expression 'is' pattern` → `proposals\csharp-8.0\patterns.md`。C# 的 `is` 只承担"测试+绑定"，没有 VB 侧 `Is` 同时当引用相等操作符的二义包袱。

**方向 5：编译器与类型协作的 C# 惯例是显式 opt-in，不按方法名发明约定。** interpolated string handler 是最近例证：类型作者必须用属性显式声明，编译器才与它协作——

- 逐字：「Type `T` is said to be an _applicable\_interpolated\_string\_handler\_type_ if it is attributed with `System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute`.」→ `proposals\csharp-10.0\improved-interpolated-strings.md`。
- 这直接支持 PROPOSAL E（显式 `Narrowing` 操作符，落成 `op_Explicit` 元数据），反对 B1 的"编译器扫描 `Parse` 方法"。

**方向 6：生态总趋势（索引 T5/T7/T8）。** 动态/晚期绑定不是 C# 前进方向；AOT/trimming 驱动"类型系统承担更多、编译期生成替代运行时反射"。会抛异常的隐式转换恰是这趋势要压制的形态。

### 现实 vs 提案

| 子机制 | C# 现实方向 | 关系 | 理由 |
|---|---|---|---|
| B1（任意表达式） | 隐式转换契约"不失败/不抛出/不有损" | **冲突** | C# 把可抛的隐式转换当 footgun（`dynamic` 是唯一例外且边缘化）；B1 每次求值抛 `FormatException` 正是被否定的形态 |
| B1（仅字面量） | §10.2.11 常量表达式隐式收窄 | **需桥接** | 形态与 C# 同构（编译期验证、失败编译报错），但 C# 只覆盖数值收窄、不覆盖 string→Guid/枚举；桥接=解析器放 BCL + 显式 operator 协议，不放编译器 |
| B2 | out var + `is` 模式（#42/#337 同向） | **兼容** | 语义同向；`Is` 语法冲突是 VB 内部文法问题，与 C# 无关 |
| B3 | C# 无任何 string→枚举转换 | **脱节** | C# 连显式 string→enum 都没有；VB 若在 Strict On 下放行，是纯 VB 特色，元数据无可映射的 `op_Implicit`/`op_Explicit`，C# 消费 .vbx 程序集时看不到这条转换 |
| 编译器探测 `Parse` | 编译器协作=显式 opt-in（属性/操作符） | **冲突** | `[InterpolatedStringHandler]` 先例证明 C# 要求类型作者显式声明，不按方法名发明约定 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**（决策文件 M2/M5 双模路线）：`Option Strict On` 保持"隐式收窄要显式 `CType`"承诺，B1/B3 不隐式化；脚本化的宽松语义（`Off` 走运行时转换）留给显式 opt-in 的宽松模式/interpreted 层。C# 官方文件对 VB 差异化是明确背书的——unsafe-evolution 逐字：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（索引第四节已核实）。
2. **字面量路径若做，以 §10.2.11 为语义模板**：仅限编译期可验证的字面量，失败=编译错误，绝不运行时抛；解析器不复制进编译器（RESOLUTION 5）——用 BCL `Parse` + analyzer/tagged string（#184）承担验证价值。
3. **显式 `Narrowing` 操作符协议（PROPOSAL E）是唯一"两语言都能看懂"的落点**：VB `Narrowing`/`Widening` 本就发射 `op_Explicit`/`op_Implicit` 元数据，与 C# user-defined conversions 互通；类型作者显式声明后，C# 侧看到 `op_Explicit`、VB 侧走 `CType`，语义一致。
4. **识别新元数据**：本提案发射 `Parse`/`ToEnum` 调用或常量，均为既有 IL，不引入新 CLR 概念，无 RefSafetyRules/CompilerFeatureRequired 之类新元数据需识别（决策文件 M4）。若未来走属性标记的转换协议，需带 `CompilerFeatureRequired` 门控以便新旧编译器正确诊断。
5. **B3 枚举窄化定位为差异化卖点**：C# 没有 string→enum 转换，B3 无 C# 兼容包袱、也无先例可借；文档须明示"与 C# 语义不同"，并在 `Option Strict On` 下把失败时机、大小写（`Option Compare`）、`[Flags]` 组合逐条定论后再独立评审。

### 对既有 RESOLUTION / 三态判定的影响

- **强化 RESOLUTION 2（B1 不采纳）与 5（不内建解析器）**：C# 隐式转换契约与"编译器不内建解析器"相互印证，B1 的 Reject 获得外部（C#）佐证，不再只是 VB 内政判断。
- **RESOLUTION 4（B3 Consider）不变，定位更清晰**：C# 无 string→enum 转换，说明 B3 无兼容包袱、也无先例可借；Option Strict 分叉是 VB 内政，C# 生态帮不上忙，仍须 VB 侧补齐语义。
- **RESOLUTION 6（验证走 analyzer）与 C# 生态一致**：C# 同样把验证场景导向 analyzer/refactoring，而非改变语言语义。
- **三态判定维持 Table**：C# 现实不改变拆分结论，反而给"B1 冲突、B2 兼容、B3 特色"各自补了外部注脚。

### 引用纪律与 OPEN QUESTIONS

已逐字核对并标注来源的 C# 原文（本附录全部引号内文字）：
- 「Generally, implicit conversions should not fail, throw or be lossy. …」「There's an exception: integer to floating point conversion in C# does allow loss.」「Stick with proposed restriction (conversions cannot be `implicit` and `checked`)」→ `meetings\2022\LDM-2022-02-07.md`
- 「In general, implicit conversion operators are not supposed to throw.」→ `proposals\csharp-11.0\checked-user-defined-operators.md`
- 「This issue, where an implicit conversion can throw, isn't entirely new to the language; after all, `dynamic` can be implicitly converted to any type, and that can throw. …」→ `meetings\2024\LDM-2024-12-04.md`
- 「An implicit constant expression conversion (§10.2.11) permits a constant expression of type `int` to be converted to `sbyte`, `byte`, `short`, `ushort`, `uint`, or `ulong`, provided the value of the constant expression is within the range of the destination type.」→ `proposals\csharp-10.0\constant_interpolated_strings.md`
- 「When the `u8` suffix is used, the value of the literal is a `ReadOnlySpan<byte>` containing a UTF-8 byte representation of the string.」→ `proposals\csharp-11.0\utf8-string-literals.md`
- 「The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument.」→ `proposals\csharp-7.0\out-var.md`
- 「A pattern is not an expression, but a separate construct. It can be recursive.」→ `meetings\2015\LDM-2015-01-28.md`
- 「Type `T` is said to be an _applicable\_interpolated\_string\_handler\_type_ if it is attributed with `System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute`.」→ `proposals\csharp-10.0\improved-interpolated-strings.md`
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（索引第四节已核实）
- C# 转换类目结构（§10.2/§10.3 无 string→enum）→ `spec\conversions.md`（本镜像为目录 stub，正文在 dotnet/csharpstandard）

**OPEN QUESTIONS**：
- C# 标准 §10.5 User-defined conversions 的规范正文（"user-defined implicit conversion should be designed to never throw…"一类表述）位于 dotnet/csharpstandard，本镜像 `spec\` 仅含链接，未在本库逐字核实；本节用 LDM-2022-02-07 与 checked-user-defined-operators.md 的 LDT 层表述替代，语义等价但非规范原文。
- 「C# 无 string→enum 转换」为目录结构证据（`spec\conversions.md` 类目清单），非逐字否定句；如需规范原文引用，须到 dotnet/csharpstandard §10.3 核实。
- `Enum.Parse`/`Enum.TryParse<TEnum>` 属 dotnet/runtime BCL，不在 csharplang 仓库，未核实。
