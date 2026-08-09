# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天我们不讨论任何新语法——本建议是一份**运行时库（runtime library）**改进清单，提案头就自注"以运行时为主，非纯语法建议"。这在我们这里很少见：过去几周我们处理的都是语法或编译器绑定问题（可空性流分析、ShapeOf、交/并类型），而这次牵涉的是 `Microsoft.VisualBasic` 程序集、内建转换的 IL 生成、以及晚期绑定的运行时代码路径。我们请了运行时团队的人旁听，但和主线会议一样，**本文档只记录房间里的共同思考，不记录谁说了什么**。

我们花了相当多的时间讨论一个前提问题：**VB LDM 到底该不该为运行时库背书？** 主线在 2018 年 3 月处理 issue #280（"Can The Visual Basic DLL Be Open Sourced?"）时的表态是——"We'll think further on this which needs discussion with the runtime folks." 而我们自己的评价标准里也有"CLR/库层变更直接委托 C# LDM"的说法。但我们注意到一个反例：主线最近**真的**以语言提案的形式往 `System.Runtime.CompilerServices` 塞了一个新特性（`OverloadResolutionPriorityAttribute`，`proposals/overload-resolution-priority.md`）。所以"语言团队不管运行时"的说法不准确——准确的说法是：**凡影响语言可观察行为（绑定、转换、异常语义）的运行时改动，是我们的地盘；纯运行时内部工程（性能、分层、跨平台打包）是运行时团队的地盘**。这条分界线贯穿了今天的全部讨论。

## Agenda

* [Proposal: VB 运行时库改进（Runtime Library Enhancements）](#proposal-vb-运行时库改进)

## Proposal: VB 运行时库改进

_Related: [vblang #86 – 优化 Int/Fix/System.Math 已知返回整数的转换调用](https://github.com/dotnet/vblang/issues/86)；[vblang #280 – Can The Visual Basic DLL Be Open Sourced?](https://github.com/dotnet/vblang/issues/280)；[vblang #135 – Late-binding without `Option Strict Off`](https://github.com/dotnet/vblang/issues/135)；[vblang #218 – 表达式树字符串比较改写](https://github.com/dotnet/vblang/issues/218)；[vblang #170 – ByRef 差异重载的新决胜规则](https://github.com/dotnet/vblang/issues/170)；Anthony 原文第 17 章 "Runtime Library Enhancements"_

### 场景与缺口

这份建议（`proposal-runtime-library.md`）复述了 Anthony 原文第 17 章的一页要点清单，把它组织成八条"需要改进的 VB 运行时问题"：

1. 运行时方法性能，尤其是**内建转换**（`CInt`/`CDate`/`CLng` 等）；
2. **转换异常**更有用/更有信息量；
3. **晚期绑定异常**更有用/更有信息量；
4. 晚期绑定下支持更多**早绑定语言语义**（如 target-typing）；
5. 面向替代平台/目标更好的**分解（factoring）**；
6. **Bug 修复**：转换中对二进制字面量语法（以及数字分隔符?）的支持；
7. **`My` 助手现代化**（如 `Async` 方法——Anthony 自己注明"might be done already"）；
8. 附加功能（Additional functionality）。

We started from the observation that VB 的语言体验很大程度取决于运行时库。`CInt`/`CDate`/`Val`、晚期绑定、`My` 助手都是"语言即运行时"的一部分——这不是比喻，是字面事实：主线的 LDM 记录里，转换和运行时库的话题反复出现，而且**每一项都有真实对照**。

- **性能缺口是主线认证过的**。2014 年 3 月的会议记录直言："any time we call CInt, the compiler always codegens a call to Math.Round. Even CInt(Math.Round(x)) gets compiled into Math.Round(Math.Round(x))." 2018 年 3 月围绕 issue #86 我们又回来过："CInt and Fix call methods on System.Math, and are thus slow(ish)."
- **二进制字面量缺口也是主线认证过的**。2017 年 12 月的会议记录，在一次表达式树改写的讨论里原话写着："CInt doesn't support binary literals or digit group separators"。
- **分层/跨平台缺口有主线预演**。issue #280 讨论是否开源 Visual Basic DLL 时，我们的结论是"Looking at a new layering. Those things forever typed to Windows can't be handled the same way as things that could be in standard."；2018 年下半年的会议 README 里甚至有一条 "Issue review and Microsoft.VisualBasic.Runtime.dll planning"。
- **晚期绑定语义缺口是主线搁置过的**。Scenario #135（Late-binding without `Option Strict Off`）在 2017 年 8 月讨论过四种方案（`Dynamic` 伪类型、方法级 `Option`/`Imports`、晚期绑定成员访问表达式、变量级晚期绑定），最后没有一项进入语言。

所以这份建议的方向我们都认——它指向的都是真实缺口，不是幻觉。**但它的形态我们认不了**：八个工作流被绑成一个提案，而它们各自的成本、风险、负责人、验收标准完全不同。我们花了一整场会议来拆这把伞。

### 候选方案

我们没把八条当"一个特性"来选方案，而是把伞拆开，逐条枚举候选。对每条我们考虑过的选项：

**工作流一：内建转换性能**
- **PROPOSAL A — 编译器层安全优化**：完全复刻主线 #86 的精神——"Treat it as an optimization, not a feature"。仅当编译器已经为参数生成了对**已知返回整数**的数学函数调用（`Round(d)`、`Round(d, MidpointRounding)`、`Truncate(d)`、`Floor(d)`、`Ceiling(d)`、`VisualBasic.Fix(d)`、`VisualBasic.Int(d)`——这是 2014 年会议定下的精确清单）时，省略多余的 `Math.Round`，并对 `CInt(Math.Truncate(d))` 直接发射 `conv.i4`/`conv.ovf.i4`。**语义完全不变**，只是更聪明的 IL。
- **PROPOSAL B — 运行时方法重写**：改 `Microsoft.VisualBasic.CompilerServices.Conversions` 里的转换方法本身（内联、去掉包装、减少分配）。改动在运行时 DLL 内部，编译产物不变。
- **PROPOSAL C — 无条件优化所有 `CInt`**：把 `CInt(d)` 的舍入语义从"银行家舍入"改成截断或原生 opcode。主线 2018 年已经表态：this is a breaking change and can't be done。我们不会考虑。

**工作流二/三：转换异常与晚期绑定异常**
- **PROPOSAL A — 新异常子类型**：派生自 `InvalidCastException`/`MissingMemberException` 的新类型，携带结构化字段（输入值、期望类型、成员名、签名）。基类型 catch 仍工作，但 `GetType()` 与消息文本改变。
- **PROPOSAL B — 不改类型，只增强上下文**：保留异常类型与 `Message` 主文案，把上下文放进 `InnerException`、`Data` 字典，或一个可选的新属性。
- **PROPOSAL C — AppContext 门控的新消息**：新文案默认关闭，由 `AppContext` 开关/配置文件开启。
- **PROPOSAL D — 什么都不改，靠调用方包装**：建议里 Alternatives 部分自己也提了——"靠调用方自行捕获并包装异常，样板多、体验不一致"。我们同意它说得对，这就是不做这件事的代价，但**这恰恰是很多异常消息今天的样子**。

**工作流四：晚期绑定下更多早绑定语义（target-typing）**
- **PROPOSAL A — 扩展运行时 `NewLateBinding` 路径**：让运行时的晚期绑定调用尊重目标类型（把结果按目标类型转换）。
- **PROPOSAL B — 编译器绑定层扩展**：在 Option Strict Off 的绑定器里，让晚期绑定表达式携带目标类型信息，参与转换。
- **PROPOSAL C — 交给 ModVB 的 `Any` 伪类型建议**：把"动态对象的语义"整体交给 `proposal-any-pseudotype.md`，本建议只管异常。
- **PROPOSAL D — 不在此建议内做**：target-typing 本身有一份独立的、被作者自评为"I think this will cause fist fights"的建议（`proposal-target-typed-conversions.md`，现处于 inactive），把语义绑定到转换表达式上。

**工作流五：面向替代平台/目标的分解**
- **PROPOSAL A — 分层（layering）**：按 #280 的讨论，把"永远 Windows"的部分（`My.Computer.Audio`、注册表、COM 互操作）与"可进 standard"的部分（`FileSystem`、`Strings`、`Conversions`）拆成不同程序集/层。
- **PROPOSAL B — 不动，运行时团队主导**：这是工程任务，不是语言特性，我们标注方向、不亲自动手。

**工作流六：二进制字面量转换 bug**
- **PROPOSAL A — 编译器侧修复**：转换表达式（如 `CInt(&B101)`）按语言字面量语法解析，常量折叠正确。
- **PROPOSAL B — 运行时侧修复**：字符串→数字的转换路径（`CInt("&B101")`、`Val`）识别 `&B` 前缀与 `_` 分隔符，与语言自己的字面量语法对齐。
- **PROPOSAL C — 二者都修，但分号（separators）单独走**：Anthony 在原文里对分隔符用了问号，说明他也没想好。

**工作流七：`My` 助手现代化**
- **PROPOSAL A — 验证先行**：`My` 的 `Async` 方法"可能已经完成"（Anthony 原话 "might be done already"），先查清楚再谈改进。
- **PROPOSAL B — 全面现代化**：给 `My` 助手铺 `Async`/`ValueTask`/`IAsyncEnumerable`。

**工作流八：附加功能**
- **PROPOSAL A — 作为占位符保留**。我们当场拒绝了——按我们自己的原则，"不为边缘场景加特性"。

### 权衡：Q&A

- **性能为什么要选 A 而不是 B？** 因为 B 动了运行时方法的实现，而运行时方法是被**所有已编译代码**共享的——`Conversions.ToInteger` 的重写在旧程序集上不生效，只有重新编译才有意义；而 A 在编译器里做，天然只对重编译的代码生效。更重要的是：A 有主线先例并且语义无风险，B 需要在一份没有附任何基准的提案里赌性能收益。**结论：v1 走 A。**
- **A 的边界在哪？** 2014 年定下的清单（`Round/Truncate/Floor/Ceiling/Fix/Int`）就是边界。2014 年会议留了一句 TODO："verify that conv.i4 has exact same semantics as CInt(Math.Truncate)，including negative values, superlarge doubles and exceptions, NaN, Infinity"——这句话今天依然没做完，我们要求补上再谈合入。表达式树照旧："Expression trees themselves will be unchanged. The IL output of expression trees is not expected to be high performance."——所以共享 IL 生成路径的优化也会顺带发生在表达式树里，但我们不为表达式树单独做优化。
- **为什么不做 analyzer 提示慢 `CInt`？** 主线 2018 年讨论过："Do we want an analyzer that finds slow CInts? It would change the semantics of the program. Probably won't do this."——一个只提示不改的 analyzer 价值有限，改了语义又违反零破坏。我们维持主线的判断。
- **异常质量：A/B/C 哪个？** 分歧点：新子类型（A）最有"信息量"，但任何类型改变都会破坏 `GetType()` 精确匹配与消息断言；B 保类型、加上下文，最稳；C 把风险延迟到开关翻转。我们倾向于 **B + 可选 C 的混合**：类型与主文案 v1 不动，上下文走 `InnerException`/`Data`，新文案由 AppContext 门控。理由是我们对破坏性变更几乎零容忍——评价标准里那句"We will almost never make breaking changes"不是口号，是预算约束。**Note：这不是说 A 永远不可能，而是它需要一份独立、完整的兼容性分析，今天这份建议没有。**
- **晚期绑定：A/B/C/D 哪个？** 这是全场最尖锐的追问。我们先把概念劈开：**"晚期绑定异常"是运行时问题**（`NewLateBinding` 抛 `MissingMemberException`/`InvalidCastException` 时的文案），**"更多早绑定语义"是编译器绑定问题**（绑定器决定怎么解析成员、怎么对待目标类型）。把两者捆在"运行时库改进"一个标签下是概念混写——这点我们标 `Suspect`，建议作者自己也没区分。target-typing 的具体形状必须和 `Any` 伪类型、`target-typed conversions` 两份建议协调，否则会出现"同一动态表达式两种语义"。**结论：异常部分留在这份建议里；语义部分划走。**
- **分层：我们到底该做什么？** 我们明确：**这是工程任务，不是语言特性**。#280 当时说"We'll think further on this which needs discussion with the runtime folks."——六年过去了，这句话依然成立。但 VBScript.NET 有一个独有理由让它变成我们的问题：如果 VBScript.NET 要跑在非 Windows 目标上，`My.Computer.Audio` 这类东西不可能进核心层。这决定了 `My` 现代化（工作流七）必须和分层（工作流五）绑在一起审。
- **二进制字面量：A 还是 B？** 主线 2017 年的原话是"CInt doesn't support binary literals or digit group separators"——它出现在一次**表达式树改写**的讨论里，这暗示问题是编译器某条转换路径不认识 `&B` 字面量。但"转换"两个字可能指两条完全不同的路：`CInt(&B101)`（字面量表达式转换，编译器常量折叠）和 `CInt("&B101")`（字符串→数字，运行时 `Parse`）。**我们不知道确切的复现场景，这必须先钉死**。我们 `Suspect` 最可能是后者（`Val` 在 VB6 起就认 `&H`/`&O` 前缀却不认 `&B`），但必须验证。分隔符（`&B101_0001`）单独走，跟随主数字字面量语法。
- **`My` 现代化：先验证什么？** 一个字：`Async` 是否真的已完成。Anthony 自己都写了"might be done already"——**一份建议里出现"可能已经做完了"还不去查，这不是未决问题，是偷懒**。而且 `My` 命名空间由编译器合成（编译器生成 `My` 命名空间代码），所以"`My` 现代化"又是半个编译器特性——又一次标签错位。

### 深度追问：LDM 拷问清单

按我们的评价标准第五部分逐条过。说明：这是一份非语法建议，所以部分拷问（文法歧义）退化为"范围/标签拷问"。

#### 1. 语法 / 文法歧义

本建议**不引入新语法**——这是它的优点，也让拷问一基本清空。但工作流四（target-typing）一旦推进就会触及语法面：它和 `proposal-target-typed-conversions.md`（作者自评 "fist fights"）、`proposal-any-pseudotype.md` 的 `(As Any)` 后置转换共用同一片"目标类型推断"实现面。`M(CType(x))` 这种把省略类型实参的 `CType` 放进实参位置的写法，依赖参数类型作目标，语义可读性都成问题。**任何这类语法都必须在各自的提案里过完整的 LDM 拷问，不能借"运行时库"的名头溜进来。**

#### 2. 角案例与边界语义

**性能（A）**：负值、超大 double、`NaN`、`Infinity`、`MidpointRounding` 变体、checked/unchecked 两路（`conv.i4` vs `conv.ovf.i4`，随项目级溢出检查设置）。这正是 2014 年 TODO 没做完的清单。表达式树路径：共享 IL 生成则顺带优化，不单独做。

**异常**：异常文本被测试、日志、监控断言的事实必须直面。`CInt("abc")` 从 `InvalidCastException` 换成新子类型，`GetType().Name` 的断言就碎。C# 那边做过类似的（更可操作的工厂错误消息），但那是新诊断、新路径；**这里是既有运行时异常的文本/类型，改一个碎一片**。

**晚期绑定**：主线留下两条具体约束，我们照抄：
- 非末尾命名实参的晚期绑定调用在编译期报错（2017-08-09：make sure we produce an error *after* overload-resolution, not making method groups inapplicable）——这条今天的运行时异常改进**不得**推翻。
- 晚期绑定与 ByRef：`obj.LateBoundCall(M())` 需要 copy-back（2016-05-06：Make it work），而"把一个返回 ByRef 的晚期绑定属性赋值"（`obj.ByRefReturningProperty = ...`）——主线明确 **Doesn't work**。任何"更多早绑定语义"都必须先回答：**我们要不要改变这条边界？** We think 不要——那是另一场仗。

**二进制字面量**：`&B` 前缀 + `_` 分隔符 + 大小写 + 负数/溢出。若修的是 `Val`/`CInt` 字符串路径，还要和 `NumberStyles`、`CultureInfo`（千分位分隔符 vs 字面量分隔符 `_` 的区分）交互。

**`My`**：`My` 编译器合成，`Async` 方法若已完成，剩下的就是"哪些成员该异步化"的枚举——需要 use case 与优先序，不能默认全做。

#### 3. 作用域与绑定

运行时异常改进不影响语义模型；但若工作流四推进，语义模型必须如实反映"晚期绑定表达式是否携带目标类型"。`GetTypeInfo` 返回什么、补全显示什么，都要在原型里验证。我们 v1 明确：**语义模型按现状绑定，运行时异常上下文不进语义模型**。

#### 4. 与既有特性的交互

- **`CInt`/`CDate` 与 `Option Strict`**：优化必须两条路径行为一致（严格/宽松下同一表达式同一结果），因为优化只改变 IL 形式不改变语义——这天然满足。
- **`TryParse` 惯用法**：工作流二的一个备选是"用 analyzer 提示改用 `TryParse`"——建议 Alternatives 里也提到了。我们不把 analyzer 当替代品（它不能让代码编译），但**可以作为伴随改进**：异常质量提升 + analyzer 提示 = 一个完整的体验闭环。
- **表达式树 / LINQ provider**：2017-12-06 那次讨论的原话是"LINQ providers should fix their stuff"——运行时方法改签名/改行为会打断表达式树形状，`Operators.CompareString`（#218）已经演示过这类坑。任何运行时方法改动都要过"表达式树生成形状是否改变"这一关。
- **`MidpointRounding`、`OverloadResolutionPriority`**：前者是转换语义的一部分，后者是主线刚为运行时加的"语言可观察"先例——提醒我们运行时属性是合理工具，但必须走语言提案流程。

#### 5. Breaking change 与兼容性

这是全场的核心预算。三处高风险：

1. **异常文本/类型**：既有的 `Catch ex As InvalidCastException` 与消息断言。任何改动都要给兼容策略（B/C 方案的意义所在）。
2. **转换语义**：无条件改 `CInt(d)` 舍入是死路（主线已判），安全优化只允许语义不变的火力。
3. **晚期绑定解析**：任何"更多早绑定语义"都可能改变一个在 Option Strict Off 下**今天能运行**的调用。主线 2017 年已经定了底线：late-binding won't support the tie-breaker discussed in #170——**我们维持这条底线，不把它当成可牺牲品**。

重编译行为：性能优化只影响重编译代码，旧二进制不动——这是 A 方案比 B 方案更稳的又一理由。

#### 6. Option Strict / 编译选项分叉

晚期绑定只在宽松路径活跃；严格路径下 `CInt`/转换是编译期绑定。异常改进、性能优化两路径必须行为一致；**语义对齐类改进（工作流四）必须在"严格路径不受影响"的前提下设计**，否则等于给宽松路径单独开了一门语言。

#### 7. IDE / IntelliSense 影响

异常上下文不进语义模型，IDE 基本不受影响；性能优化无 IDE 面。真正有 IDE 面的是 analyzer 伴随改进（提示慢 `CInt`、提示 `TryParse`）——那需要新诊断 ID 与快速修复，单独排期。

#### 8. 数据 / 普遍性

**CInt 性能是主线反复确认的真实缺口**（2014、2018 两次独立确认），普遍性不用论证。**异常质量**：方向合理但没有数据支撑"最常失败的转换/绑定是什么"——我们要求基准与 top 场景清单。**`My` 现代化**：连是否已完成都没查。**二进制字面量**：有主线一句原话，但没有 issue 编号、没有复现。数据普遍性最扎实的是性能与二进制字面量两项，其余停在直觉层。

#### 9. 更简替代

- 性能：不优化 = 保持现状，但这是主线认证过的明确浪费。
- 异常：调用方包装（建议 Alternatives 自己都承认"样板多、体验不一致"）；analyzer 提示 `TryParse`（不能替代语言/运行时改进，可伴随）。
- 晚期绑定语义：手动 `CType`/`DirectCast` 桥接（现状样板）。
- 分层：交给运行时团队（#280 路线）。

#### 10. 复杂度 / 成本 / 优先级

八条工作流成本差三个数量级：二进制字面量修复可能是几天的活；分层与 My 现代化是年度级工程。**把它们放进一份提案，等于让最小的活被最大的活绑架**。优先级排序：性能（有先例、无风险、可测量）> 二进制字面量修复（小、实、主线认证）> 异常质量（方向对、需兼容设计）> 晚期绑定语义（需与 Any 协调）> 分层/My（工程级，运行时团队主导）。

#### 11. 运行时 / CLR 硬约束

性能优化的目标是原生 opcode（`conv.i4`/`conv.ovf.i4` 等），PEVerify 安全、无新 IL 概念——2014 年已经确认"adds no new syntax or concepts or library functions"。运行时方法改动受 `Microsoft.VisualBasic` 程序集版本化约束；分层受 .NET Standard/TFM 约束。无表达式树新问题（见工作流一权衡）。**None of this touches CLR storage rules.**

#### 12. 值不值得做

逐条打分（价值 × 成本 × 风险）：
- 性能：价值高、成本中、风险低（有先例）。**做。**
- 二进制字面量：价值中、成本低、风险低（修复既有缺陷）。**做。**
- 异常质量：价值中高、成本中、风险中（需兼容策略）。**做，但限定在不破坏的范围内。**
- 晚期绑定语义：价值中、成本高、风险高（破坏宽松路径语义的敞口）。**暂缓，交 Any。**
- 分层/My：价值中、成本高、风险中。**运行时团队主导，我们标注方向。**
- 附加功能：占位符。**不做。**

**伞形建议本身不值得作为一个特性做**——它是一份清单，不是设计。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：性能优化（语义不变）与二进制字面量修复（修既有缺陷）贴合；异常文本/类型改动直接撞线，必须用兼容策略对冲。
- **保持 VB-like / 新手友好（原则 #2、#5）**：更有信息量的转换异常对新手是直接的恩惠——`CInt("abc")` 报"无法将 'abc' 转换为 Integer"比笼统的运行时文案强得多。这是本建议最"VB"的一条。
- **不引入第二种做事方式（原则 #3）**：性能优化不改写法（`CInt` 还是 `CInt`），只是更快——零表面扩张。新异常子类型也遵守"捕获基类型"的既有惯用法，不算第二种方式。
- **避免隐蔽语义变化（原则 #7）**：无条件改 `CInt` 舍入正是这种"细微字符改变语义"的变体，主线已判死；我们的安全优化边界就是为这条原则划的。
- **不为边缘场景加特性（原则 #6）**：工作流八（附加功能）违反，当场拒绝。
- **冗长只在有用时是美德（原则 #10）**：异常上下文正是"有用的冗长"。
- **与主线关系（对照表 2.3）**：性能 = **主线一致**（#86 就是主线活）；二进制字面量 = 主线认证的缺陷，修复是**主线一致**；分层 = **主线 ongoing**（#280 + 2018 下半年 "Microsoft.VisualBasic.Runtime.dll planning"）；晚期绑定语义 = **主线保守、Anthony 激进**（主线搁置 #135 系列，Anthony 用 `Any` 全速推进）；target-typing = **Anthony 独立延伸**（主线无，且自评"会打架"）。`My` 现代化 = 主线 Windows 中心、无明确计划，Anthony 独立延伸。

### RESOLUTION:

1. **不把"运行时库改进"当作单一语言特性接受**。它是八条独立工作流的伞形标题；作为一份提案它 Reject——拆分后逐条跟踪。我们不为一整把伞背书。
2. **工作流一（内建转换性能）：Active（限定范围）**。采纳 PROPOSAL A——完全复刻主线 #86 精神："Treat it as an optimization, not a feature"。边界 = 2014 年会议定下的已知返回整数方法清单；语义不变；checked/unchecked 两路随项目设置；表达式树共享路径顺带、不单独优化。**前置条件：补完 2014 年 TODO——`conv.i4` 与 `CInt(Math.Truncate)` 的语义等价性（负值、超大 double、`NaN`、`Infinity`）逐条验证，附基准。**
3. **工作流六（二进制字面量转换 bug）：Active（限定范围）**。方向采纳，但**必须先钉死复现场景**：编译器侧（`CInt(&B101)` 常量折叠）还是运行时侧（`CInt("&B101")`/`Val` 字符串→数字路径）——We `Suspect` 后者，未验证。数字分隔符（`&B101_0001`）单独走，跟随主数字字面量语法。立项一个最小 issue，把主线 2017 年那句"CInt doesn't support binary literals or digit group separators"变成带复现的缺陷报告。
4. **工作流二/三（转换与晚期绑定异常）：Consider（限定范围）**。方向采纳，v1 取 **PROPOSAL B + 可选 C**：既有异常类型与主文案不动，上下文进 `InnerException`/`Data`；新文案由 AppContext 开关门控。新异常子类型（A）不否决，但**需要独立兼容性分析**，不绑进本建议。附带 analyzer 提示 `TryParse`（新诊断，单独排期）。
5. **工作流四（晚期绑定更多早绑定语义）：Table，划归 `Any` / target-typed 协调**。异常部分留；语义部分本建议标签不符（编译器绑定问题），且与 `proposal-any-pseudotype.md`、`proposal-target-typed-conversions.md` 重叠。维持主线底线：`#170` tie-breaker 不进入晚期绑定；非末尾命名实参错误、ByRef 晚期绑定边界（2016-05-06 的两条决策）**不推翻**。
6. **工作流五（分层/factoring）与工作流七（`My` 现代化）：Table，运行时团队主导**。按 #280 路线做 layering——"那些永远 Windows 的"与"可进 standard 的"分开；`My` 的 Windows 依赖部分随分层走。VBScript.NET 侧先出清单：哪些 `My` 成员是跨平台可用的。`My.Async` 是否已完成：**先验证再谈**（Anthony 自己都标了 "might be done already"）。
7. **工作流八（附加功能）：Reject**。占位符条目，按原则 #6 不作为。
8. **流程纪律**：本建议从 `proposals/` 移入拆分状态，各工作流各自拥有 speclet 与证据栏位；伞形标题作为索引文档保留，但不参与特性评估。

### Implication:

- 拆分八条为独立 speclet；每条有自己的状态行（Proposed/Prototype/Implementation/Specification）与证据栏位。
- 性能工作流：写最小原型（编译器转换优化 + 语义等价验证 + 基准），对照 #86 的结论补 2014 TODO。
- 二进制字面量：先立最小复现 issue，钉死"编译器侧 vs 运行时侧"，再决定修哪边。
- 异常工作流：起草兼容策略文档（类型不变 + AppContext 门控新文案 + `InnerException`/`Data` 约定），评审后才进原型。
- 与 `Any`、`target-typed conversions` 团队对表：确定"晚期绑定语义"归属，避免两套机制。
- 与运行时团队约一次例会：对齐 #280 的分层路线与 `Microsoft.VisualBasic.Runtime.dll` 规划（2018 下半年主线已埋线）。
- 验证 `My` 的 `Async` 方法现状，补 `My` 成员清单（跨平台可用 vs Windows 专用）。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：二进制字面量 bug 的确切复现场景（编译器侧 vs 运行时侧），以及 `Val` 是否真的不认 `&B` 前缀。
- `OPEN QUESTIONS`：异常兼容策略——新异常子类型（A）是否值得为它做一次独立的 breaking-change 评审；AppContext 开关的命名与默认值。
- `OPEN QUESTIONS`：晚期绑定"更多早绑定语义"的具体清单与边界——除 target-typing 外还包括什么（Anthony 原文没有展开）。
- `OPEN QUESTIONS`：性能优化的目标基准——哪类转换、多大规模优化算"成功"，VBScript.NET 的验收线。
- `TODO`：验证 `My` 的 `Async` 方法是否已完成（Anthony 标注 "might be done already"）。
- `TODO`：补数据——转换/晚期绑定失败异常的 top 场景清单。
- `Follow-up`：与运行时团队例会，对齐 #280 分层与 `Microsoft.VisualBasic.Runtime.dll` 规划。
- `Follow-up`：把主线 2017 年"CInt doesn't support binary literals or digit group separators"的语句转成带复现的缺陷 issue。

### 状态

- **LDM 状态**：伞形建议归档（LDM Rejected as written）；拆分后——性能 Active、二进制字面量 Active、异常 Consider、晚期绑定语义 Table、分层/`My` Table、附加功能 Rejected。
- **三态判定：Table（拆分后 Active/Consider 分项）**——价值真实且多处有主线认证（#86、#280、2017 二进制字面量），但形态是清单不是设计；最小可行增量（性能 + 二进制字面量）先行，其余按证据补足后逐项推进。

---

## 附录：特性评价

# 建议评价报告：proposal-runtime-library.md

## 评价对象

- 建议：proposal-runtime-library.md — VB 运行时库改进（性能 / 异常 / 晚期绑定支持 / 分层 / 二进制字面量 bug / `My` 现代化）
- 来源：Anthony 原文第 17 章 "Runtime Library Enhancements"（`..\AnthonyDesign_wordpress.txt` L2665–2703，八条要点清单，无代码示例）
- 配方目标：改善"语言即运行时"体验——内建转换更快、转换/晚期绑定异常更有信息量、晚期绑定复用早绑定语义、面向替代平台更好分层、修复二进制字面量转换 bug、`My` 助手现代化

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。八条改进均无目标量化（无基准、无验收线）；示例（`CInt("123")`/`obj.Clone()`）只是现状代码，**不能演示改进**；`My.Async` 连"是否已完成"都没验证；"附加功能"占位条目不可证伪 | 已检查 | 无原型、无基准、无验收标准；效果证据等级封顶（未决问题 ≥4 个关键设计点） |
| 特性 | 2/5 | 锚点 2 之二："多个强无关能力捆绑"。八个工作流（性能/异常/晚期绑定/分层/字面量/`My`/附加）职责各异，边界模糊（红旗清单逐条命中）；各成分单独看均为 VB 基因（内建转换、晚期绑定、`My` 都是 VB 专属），但捆绑本身破坏"职责单一" | 已检查 | 伞形建议，不能作为一个特性评价；概念混写（晚期绑定"语义"是编译器问题却挂"运行时库"标签） |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节结构齐全、来源标注诚实（Anthony 第 17 章）、5 个未决问题具体；但 Detailed design 是八条要点清单无设计细节、无兼容性/breaking-change 章节、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）、`My.Async`"可能已完成"未验证 | 已检查 | 详细设计无规格级细节；缺 Compatibility 章节；伞形边界模糊；"附加功能"条款为空谈 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/光正向（转换/异常/`My` 盘活既有"语言即运行时"资产）；雷/风受损（运行时改动迭代慢、伞形混编译器+运行时破坏一致性）；暗风险真实且无对冲设计（异常文本/类型、晚期绑定解析） | 已检查（预测待定） | 暗风险（breaking change）被 Drawbacks 一节承认但未给兼容策略；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源主标注准确（Anthony 第 17 章）；但未声明对主线 #86（性能优化先例）、#280（分层讨论）、2017 二进制字面量语句的**继承**；"附加功能"隐含不明确的材料；二进制字面量修复隐含 VB6-era 转换语义（`Val` 前缀传统）未点明 | 已检查 | 主线关系未标注；`My`（编译器合成特性）与"运行时库"标签偏差；无杂质、无虚构语法 |

## 设计原则对照

- **与 VB 基因：部分一致**——性能优化（语义不变、零表面扩张）与二进制字面量修复（修既有缺陷）完全贴合原则 #1/#3/#7；异常上下文贴合原则 #5/#10（新手友好、有用的冗长）；但伞形捆绑与"附加功能"占位条目违反原则 #6/#3，异常文本/类型改动威胁原则 #1。
- **与主线关系：混合**——性能 = 主线一致（#86）；二进制字面量 = 主线认证缺陷，修复一致；分层 = 主线 ongoing（#280、"Microsoft.VisualBasic.Runtime.dll planning"）；晚期绑定语义 = 主线保守、Anthony 激进（主线搁置 #135 系列，Anthony 用 `Any` 推进）；target-typing = Anthony 独立延伸（自评"会打架"）；`My` 现代化 = 主线无计划，Anthony 独立延伸。
- **破坏性变更：潜在有**——异常消息/子类型（测试与日志断言）、转换语义（若越界改舍入）、晚期绑定解析（若改宽松路径绑定）。建议文档未做兼容性分析，Drawbacks 仅承认风险未给策略。需兼容策略（类型不变 + AppContext 门控 + `InnerException`/`Data`）。

## 总评

- **达成程度：部分达成**——八条缺口均真实且多处有主线认证（#86、#280、2017 二进制字面量），但建议是清单不是设计；无基准、无兼容策略、无验收线、概念混写（运行时 vs 编译器）。
- **LDM 三态建议：Table（拆分后分项 Active/Consider）**——伞形 Reject as written；拆分后：性能（Active，复刻 #86）、二进制字面量修复（Active，先钉场景）、异常质量（Consider，限不破坏范围）、晚期绑定语义（Table，交 `Any`/target-typed 协调）、分层/`My`（Table，运行时团队主导）、附加功能（Reject）。
- **主要问题**：① 伞形捆绑八个成本差三个数量级的工作流；② 无基准与验收标准（性能/异常无量化）；③ 无兼容性分析，breaking-change 风险无对冲；④ 概念混写——"晚期绑定更多早绑定语义"是编译器绑定问题，`My` 是编译器合成特性，都挂在"运行时库"标签下；⑤ `My.Async`"可能已完成"未验证。

## 返工建议

- **拆分建议**：把八条工作流拆为独立 speclet，每条各自拥有状态行、证据栏位与验收标准；伞形标题降级为索引文档。
- **补充章节**：Compatibility / breaking-change（异常文本与子类型、转换语义、晚期绑定解析逐条列证）；基准与验收线（哪类转换、多大优化算成功）；兼容策略（类型不变 + AppContext 门控 + `InnerException`/`Data` 约定）。
- **补充证据**：性能——补完 2014 TODO（`conv.i4` 与 `CInt(Math.Truncate)` 语义等价性：负值/超大 double/`NaN`/`Infinity`）+ 基准；二进制字面量——最小复现 issue，钉死编译器侧 vs 运行时侧；异常——转换/晚期绑定失败 top 场景数据；`My`——验证 `Async` 现状与成员清单（跨平台可用 vs Windows 专用）。
- **未决问题处理**：二进制字面量场景优先钉死；异常新子类型独立评审；晚期绑定语义清单与 `Any`/`target-typed conversions` 对齐后定归属；维护主线底线（#170 tie-breaker、非末尾命名实参、ByRef 晚期绑定边界不推翻）。
- **设计探索**：与运行时团队约例会对齐 #280 分层路线；`My` 分层与跨平台可用性清单；analyzer 伴随改进（慢 `CInt` 提示、`TryParse` 快速修复）单独排期。

---

## 附录：C# 生态与互操作考量

> 本附录由 meeting 追加 agent 撰写，基于 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引）与 `..\..\csharplang` 原文核验。引用 C# 原文一律逐字并标注来源文件路径；无法核实的标 `Suspect` 或列入 `OPEN QUESTIONS`。**如实说明**：本提案（运行时库 / 脚本所需运行库）与 C# interop 的关系总体为**弱到中等**——八条工作流里只有性能、晚期绑定、分层三条有实质性 C# 生态对应，其余为 VB 独有或纯缺陷修复，不硬凑。

### 相关 C# 现实方向

本提案的主题是「语言即运行时」——`Microsoft.VisualBasic` 程序集里的内建转换、晚期绑定、`My` 助手。C#/CLR/.NET 生态与之对应的现实走向集中在三条主线上：

1. **运行库治理：C# LDT 不造库，BCL/runtime 归 dotnet/runtime（索引 T1）**。csharplang 的 `Design-Process.md` 白纸黑字划出职责边界：
   > "While much of that process takes place outside of this repository (https://github.com/dotnet/roslyn for the language feature implementation, https://github.com/dotnet/runtime for supporting BCL APIs and runtime changes, https://github.com/dotnet/csharpstandard/ for the specification changes, just to name a few), we track the overall implementation of the feature in this repository"
   → `Design-Process.md`（"Steps of the process"）

   本提案正文自己划的那条线——"凡影响语言可观察行为（绑定、转换、异常语义）的运行时改动，是我们的地盘；纯运行时内部工程是运行时团队的地盘"——与 C# 侧**完全同构**。但 C# 侧有一个例外先例：语言团队确实会以语言提案形式往 `System.Runtime.CompilerServices` 加运行时属性——`OverloadResolutionPriorityAttribute`：
   > "For this purpose, we want to have a way for API authors to guide overload resolution on resolving the ambiguity, so that they can evolve their API surface areas and steer users towards performant APIs without having to compromise the user experience."
   → `proposals\csharp-13.0\overload-resolution-priority.md`（Motivation）

   这正是正文提到的"主线把语言可观察的运行时改动当语言提案走"的实例，也说明「语言层加运行时属性」是有先例的合法通道。

2. **动态/反射被 AOT/trimming 视为负担，C# 的方向是用类型系统与编译期生成取代它（索引 T5/T6/T7）**。C# 的 `dynamic`（C# 4）长期无演进，表达式树与 `ref struct`/Span 冲突，unsafe-evolution 甚至把"dynamic 是否应标 unsafe"列为开放问题；2023 年 LDM 谈 interceptors 时把这段压力说得最直白：
   > "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible."
   → `meetings\2023\LDM-2023-07-24.md`（Interceptors）

   同一方向的落地是 source generators：`LibraryImport`（P/Invoke 的现代形态）把互操作从运行时 marshaling 改为编译期生成，unsafe-evolution 提案专设一小节讨论生成代码的 `safe`/`unsafe` 标记（`proposals\unsafe-evolution.md`，"(answered) Allow `safe` on non-`extern` members (`LibraryImport`)"）。

3. **低层内存互操作是 C# 主线投入，且明确服务"低层库"（索引 T2/T3）**。`nint`/`nuint` 的动机原文直接点名库作者：
   > "The motivation is for interop scenarios and for low-level libraries."
   → `proposals\csharp-9.0\native-integers.md`（Summary）

   这条与本提案相关度**中等**：`Conversions`/`Strings` 若未来接受 `ReadOnlySpan<T>`/`params` span 参数，会进入 C# 的 ref 安全模型管辖范围；但本提案八条工作流没有一条涉及 ref/span，故不作为重点。

### 现实 vs 提案

| 提案工作流 | 判定 | 理由 |
|---|---|---|
| 一、内建转换性能（`conv.i4`） | **兼容 / 同向** | C# 转换在语言层直接发射 `conv` opcode，不存在 `Conversions` 运行时中间层；proposal A（编译器层优化、语义不变）正是 C# 侧转换的天然形态。"转换性能的答案是语言层 codegen，不是 runtime DLL"从 C# 侧佐证了 v1 走 A 优于 B。 |
| 二/三、转换与晚期绑定异常 | **兼容（C# 侧中性）** | 异常类型/文本属 BCL/运行时层；C# 生态对既有异常同样保守，AppContext 门控（方案 C）与 .NET 运行时惯用的 opt-in 行为变更机制一致。C# 无对应"转换异常"，不冲突。 |
| 四、晚期绑定更多早绑定语义 | **冲突 / 张力最大** | C# 生态整体**去动态化**——AOT/trimming 把反射当负担（LDM-2023-07-24 原话），interceptors/unions/closed hierarchies/source-gen 都在"把信息放回类型系统"。`NewLateBinding` 的运行时反射路径在 NativeAOT 下不可用。C# 不会改善 `dynamic`，VB 改善晚期绑定属于逆生态而行。 |
| 五、分层/factoring | **兼容** | .NET 生态早有多层治理（.NET Standard/TFM、dotnet/runtime 内 Windows 专用与 standard 分离）；#280 的 layering 与 BCL 治理同向。 |
| 六、二进制字面量 bug | **兼容（C# 无关）** | 纯 VB 缺陷修复，C# 无对应物。 |
| 七、`My` 现代化 | **需桥接** | `My` 由编译器合成，C# 无对应物；但异步化必须对接 BCL 的 `ValueTask`/`IAsyncEnumerable` 形态，`My.Computer.*` 的 Windows 依赖与跨平台方向冲突。 |
| 八、附加功能 | **脱节** | C# 侧同样是"不为边缘场景加特性"。 |

**治理层面总判定：需桥接。** C# 生态的治理是"语言不造库、BCL 归 dotnet/runtime、互操作靠 source-gen"；本提案（以及 VBScript.NET 的 .vbx 脚本运行库）走"自建运行库"路线。二者不冲突，但需要显式桥接。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态双模**。.vbx 脚本运行时默认"编译到受管程序集 + source-gen 桥"，把 interpreted 模式做成显式 opt-in 的传统兼容层（决策文件 M5）。这与 C# 生态的 AOT/source-gen 方向同向，也是 `NewLateBinding` 在未来 .NET 上存活的前提。
2. **source-gen 桥**。效仿 `LibraryImport`（P/Invoke）与 COM source-generator 的模式，把脚本运行时时由运行时反射承担的热点（转换、成员访问、互操作）尽量提前到编译期生成；解释执行只保留给动态性真正必需的部分（Office/COM 自动化场景）。
3. **识别新元数据**。unsafe-evolution 后 C# 指针成员会更多在非 `unsafe` 上下文出现、成员标 *requires-unsafe*；C# 侧对 VB 的明确表态是：
   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   → `proposals\unsafe-evolution.md`（"VB" 小节）

   但"不支持 requires-unsafe"≠"不需要识别它"：.vbx 编译器与运行库必须认识 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute` 等新属性，才能在跨语言调用 C# 成员时正确校验安全边界（决策文件 M8 的必须桥接点）；运行库若消费 BCL 中带此类属性的成员，同样要能识别。
4. **运行时属性走语言提案流程**。`OverloadResolutionPriority` 先例说明：语言需要的运行时属性（异常上下文属性、AppContext 开关名、新的绑定/转换元数据）应由语言提案发起、runtime 团队配合实现，而不是绕过流程直接塞进 `System.Runtime.CompilerServices`。
5. **异常改动的 AppContext 门控**。与 .NET 运行时既有机制（AppContext switches 做 opt-in 行为变更）一致，无需发明新机制。

### 对既有 RESOLUTION / 三态判定的影响

本附录**不推翻**任何 RESOLUTION 分项，只补充一个"生态压力"维度：

- **工作流四（晚期绑定语义，Table）被 C# 生态进一步坐实**。原判定 Table 的理由是"与 `Any`/`target-typed` 协调、破坏宽松路径语义敞口"；C# 侧还要加一条：整个生态在去动态化（AOT/trimming/interceptors），VB 改善晚期绑定属于逆势。若 VBScript.NET 愿景包含 NativeAOT，工作流四应维持甚至调低优先级——这是"脚本层与编译产物层分道扬镳"的又一个论据。
- **工作流一（性能，Active）获得 C# 侧佐证**。C# 的转换就是语言层直接 codegen，proposal A 与 C# 现实同向，强化"v1 走 A"的结论。
- **工作流五（分层，Table / 运行时团队主导）获得 C# 治理佐证**。BCL 归 dotnet/runtime、语言层只 track 实现（Design-Process.md 原文）——VB 自建运行库应与 BCL 治理接轨：可进 standard 的部分（`FileSystem`/`Strings`/`Conversions`）考虑并入 dotnet/runtime 生态（#280 的开源路线），语言层只保留"语言可观察"那部分。
- **三态判定不变：Table（拆分后 Active/Consider 分项）**。

### OPEN QUESTIONS / 引用核验

- `OPEN QUESTIONS`：C# 侧没有与"VB 内建转换运行时库"直接对应的治理样板；"语言团队加运行时属性"目前只有 `OverloadResolutionPriority` 一例，样本量不足以推出完整流程。
- `OPEN QUESTIONS`：`NewLateBinding` 在 NativeAOT 下的精确限制属于 dotnet/runtime 生态，本库无正文（索引第四节已注明未深挖）。
- 引用核验：`native-integers`（Summary）与 `unsafe-evolution`（"VB" 小节）两处取自索引第四节已核实清单；`Design-Process.md`、`LDM-2023-07-24.md`（Interceptors）、`overload-resolution-priority.md`（Motivation）三处为本次直接在 `..\..\csharplang` Grep/Read 核实的逐字原文。
