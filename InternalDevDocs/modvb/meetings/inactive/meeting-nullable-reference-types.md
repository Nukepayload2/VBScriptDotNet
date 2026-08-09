# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天讨论的是一份**自己承认没有设计内容**的建议：可空引用类型（NRT）的重新设计。它被 Anthony 标为"实验性 / 未定稿"，核心内容是一段失败判据与一句"原文未给出任何语法"。我们这场会议的主要任务不是"审批一个设计"，而是回答一个更前置的问题：**这份 inactive 建议值不值得激活？如果激活，我们要给什么方向？** 会议成果是一组激活信号与三个可分离的切片，而不是一份语法。

## Agenda

* [Proposal: 可空引用类型（Nullable Reference Types）](#proposal-可空引用类型)

## Proposal: 可空引用类型

_Related: [LDM-2014-02-17](../../../vblang/meetings/2014/LDM-2014-02-17.md)（2014-01-06 拒绝 `Null` 字面量）· [vbldm-notes-2017.08.30](../../../vblang/meetings/2017/vbldm-notes-2017.08.30.md)（NRT "Not ready yet"）· [vbldm-notes-2018.02.07](../../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（NRT 推迟决定）· [vbldm-notes-2018.02.28](../../../vblang/meetings/2018/vbldm-notes-2018.02.28.md)（可空值类型相等 quirk）· AnthonyDesign section 12「Null and Nothing」顶注 / 18.16「Nullable Reference Types」· ModVB `inactive/proposal-nullable-reference-types.md`、`proposal-nullability-flow-analysis.md`、`proposal-null-literal.md`、`proposal-null-coalescing.md`、`proposal-null-safe-behaviors.md`_

### 场景与缺口

我们把这周两场相关会议（`Null` 字面量、可空性流分析）的结论带进房间。它们在本建议的周边画好了两条边界：

- **可空性流分析会议**：可空值类型（`Nullable(Of T)`，轨道 1）的流分析已 **Active**——零新语法、`Is Nothing` 守卫后 `.Value` 合法；可空引用类型（`Animal?`，轨道 2）被 **Table**，明确"语法面归 NRT 工作项"。也就是说，**本建议（NRT）就是轨道 2 一直等的那个地基。**
- **`Null` 字面量会议**：新字面量维持 2014 年 Reject（字面量 Reject / 可空感知推断 Consider）。NRT 若落地，null 的"写"仍然是 `Nothing`，null 的"测"仍然是 `Is Nothing`。

在这个背景下，缺口是什么？我们用一段今天就能编译的 VB 代码来陈述：

```vb
' 今天：任何引用类型变量都可能为 Nothing，编译器不提供任何防护。
Dim customer As Customer = GetCustomer()
Console.WriteLine(customer.Name)   ' 运行期 NullReferenceException
' 守卫可写，但"必须记得写"——没有任何机制把"这里可能为 Nothing"写进类型或 API 契约。
If customer IsNot Nothing Then
    Console.WriteLine(customer.Name)
End If
```

We think 缺口有两个层次，建议原文只明确说清了第一个：

1. **运行期 NRE 的编译期防护**（流分析层）。这个层次由可空性流分析轨道 1 部分接管（值类型），引用类型部分卡在 NRT 上。
2. **API 边界的可空性契约**（标注层）。`Function GetCustomer() As Customer` 到底保证不保证非空？今天无法表达；C# 消费端也读不到 VB 的意图。这正是 2018.02.07 Part 2 讨论的"向 C# 表达 VB 的可空性"。建议原文对这一层几乎没写——它的 Alternatives 只提了"沿用 C# 方案 / 不引入 / 温和重设计"，没有触碰跨语言契约。

Anthony 的失败判据（proposal 原文转引 18.16）：

> Nullable Reference Types has received mix reviews in C#. And VB users have expressed confusion and concern about how it would be done in VB. I consider it a failure if users hate the new feature turn it off or advise others to do so. I must refine the design to consider these observations.

（第 12 章头部 Note 亦注明：NRT 本应是该节亮点，但收到反馈称其需要进一步迭代以降低困惑与烦扰，Anthony 正在处理。）

We think 这段判据本身是清醒的，但它暴露了本建议最大的方法论问题：**失败判据是事后指标，建议却没有给出任何事前测量计划。** "用户会不会关掉特性"没有对应到"我们在激活前应该收集什么数据"。下面所有讨论都围绕这个缝隙展开。

### 候选方案

**PROPOSAL A — C# 式 NRT 移植（`?` 注解 + `!` 运算符 + 按程序集 opt-in + 全量流分析）。**

```vb
' C# 8 NRT 形态在 VB 里的逐字映射
Dim s As String? = GetMaybeString()     ' ? 标注"可为 null"
Dim t As String = s!                    ' ! 空值宽恕运算符（damnit operator）
```

Anthony 自己否决（18.16 判据），2017.08.30 记录给过致命伤——`!` 与 `dict!key` 字典访问、`Dim radius!` 单精度类型字符冲突。按程序集 opt-in + 警告墙是 2018.02.07 记录直言的 *"wall of compiler warnings"* 体验。We think 直接移植等于把 C# 的口碑问题原样进口，且语法面已被 `!` 堵死一半。**作为完整方案否决。**

**PROPOSAL B — 反转默认：标记"保证非空"，而非"标记可空"（VB 基因版）。**

VB 的传统是引用类型即可空、`Nothing` 全通。因此把"可空"当默认、"非空"当需显式标记的例外，让存量代码**零新警告**：

```vb
' 现状语义不变：未标注 = 可能为 Nothing（今天就是如此）。
Dim s As String = GetMaybeString()

' 新增显式标记：我保证它非空——编译器有义务验证我的保证。
Dim t As String NotNothing = GetGuaranteed()
t.Trim()                     ' 若 GetGuaranteed 可能返回 Nothing，此处给出诊断
```

`NotNothing` 是占位措辞（`Required`、`Guaranteed`、`NonNothing` 都讨论过），语法待定。优点：不惩罚存量 VB（`Dim s As String` 今天合法，明天仍合法且无新警告）；用户只为"保证"付费（原则 10）；`TryCast`、字典 `Item`、库签名等"天然可能空"的既有 API 无需重新标注。缺点：跨语言映射时"未标注"会被 C# 消费端视为可空——详见权衡。

**PROPOSAL C — 纯分析、零语法（复用兄弟建议引擎）。**

不引入任何类型级标注；仅把可空性流分析轨道 1 的引擎推广到引用类型（`Is Nothing` / `IsNot Nothing` 守卫 + 警告），全部按文件/项目 opt-in，默认关闭。零语法面、零墙、零 API 契约。缺点：无法表达参数/返回值的非空保证，跨程序集不传播——这恰是"API 边界契约"这层缺口的全部价值所在。

**PROPOSAL D — 仅属性发射：VB→C# 空性契约，无 VB 可见语法。**

直接延伸 2018.02.07 Part 2 的猜想——原文逐字是：

> It is possible that VB could add the emitting attributes based on a simpler attributing system.

即：VB 基于一个更简单的标注系统（程序集级/类型级开关 + 推断默认）发射 C# 8 的 nullability 属性（`NullableAttribute` / `NullableContextAttribute`），让 C# 消费端读到 VB API 的可空意图，但 VB 源码里没有任何新语法。优点：跨语言价值独立兑现、VB 用户零打扰。缺点："更简单的标注系统"到底多简单未定；默认推断策略（全部当非空发射会误导 C#、全部当可空发射会骚扰 C# 消费端）是 2018 记录里那句 *"at least at the implementation level it won't be as simple as managing attributes"* 说的硬骨头。

**PROPOSAL E — 什么都不做（保持 inactive）。**

现状：引用类型即可空、`Nothing` 全通、NRE 运行期暴露、C# 契约不可见。这是 2018.02.07 "We'll postpone this until we understand the uptake in C#" 的延续。`Suspect`：到 2026 年，"C# 采用率"这个信号大概率已明朗（C# 8 于 2019 年交付 NRT，属外部常识），但主线那句 *"it may feel like a 'not VB' thing as we understand its usage"* 的深层顾虑并没有因为 C# 采用而被回答——那是关于 **VB** 用户会不会喜欢，不是 C# 用户会不会用。E 的代价是缺口原样保留。

### 权衡：Q&A

- **A vs B：默认方向决定一切。** 我们反复回到一个事实：VB 的 `Dim s As String` 与 C# 的 `string s;` 在"未初始化"语义上**根本不同**——C# 局部变量必须明确赋值，VB 引用类型默认 `Nothing`。C# NRT 把 `string` 当非空默认，于是 `string s;`（在 C# 里合法、赋值为 null）成为警告来源；若照搬到 VB，**最平常的 VB 声明 `Dim s As String` 会全员报警**。这不是"警告墙"，是"警告雪崩"。B 反转默认，恰好把这段雪崩关在门外：未标注即"可能为空"= 今天的语义，零新警告。**结论：A 的默认方向对 VB 不成立，B 是语法面的主线。**
- **B vs C：API 契约值不值得语法？** C 得到流分析但拿不到契约。B 得到契约但付出语法成本。我们拆开问：缺口的第二层（API 边界契约）到底有没有真实价值？有——`Function GetCustomer() As Customer` 今天对调用者零承诺，C# 消费端也读不到 VB 意图；而"读取空引用导致 NRE"是 .NET 运行时最常见的异常之一（外部常识，非本仓数据）。但"有价值"不等于"值得现在做"——见成本追问。
- **B vs D：同一笔账，谁来付。** B 是"VB 用户在源码里标记保证"，D 是"编译器按更简单系统推断并发射"。B 把仪式放在作者身上（作者最懂保证），D 把推断策略放在编译器身上（作者零仪式，但编译器得猜）。我们倾向：**D 是 B 的前置切片**——先证明属性发射/推断系统可行，再谈把标注暴露给 VB 语法；若 D 的推断策略足够好，B 可能永远不需要语法。
- **C# 采用率信号够不够？** 不够。2018.02.07 等待的是"uptake in C#"，这个信号已触发；但该决定还有另一半——*"it may feel like a 'not VB' thing as we understand its usage"*。C# 采用率高恰恰是在"用户被迫在警告墙与关闭之间选择"的背景下实现的。We think：**C# 采用率是必要条件，不是充分条件；VB 自己的需求数据才是。**
- **警告还是错误？** 与可空性流分析会议同一条铁律：**任何新诊断都是警告 + opt-in，默认关闭**。把"运行期 NRE"变成"编译期错误"会重演 C# 的迁移墙，且触碰"hundreds of thousands of quiet customers"的存量代码。本建议 Alternatives 第 3 条（"默认关闭、只在显式标注处警告"）方向对，但没写成机制。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`String?` 在类型位置其实无歧义**——`Nullable(Of String)` 因 CLR 约束（`Nullable(Of T)` 要求 `T : Structure`）今天是编译错误，所以 `String?` 若被采纳，编译器可以无歧义地按"引用类型可空注解"解释，与 `Integer?`（值类型可空）由底层类型决定含义，C# 正是这么做的。

但 ModVB 家族给 `?` 加了**第三、第四重身份**，这是真正的歧义源。我们核实过 `proposal-null-coalescing.md`：`?` 同时承担"值可空推断"（`True?` → `Boolean?`）、"可空引用类型标注"（`String?`）、"表达式后文档化可能为 null"（`lookup("key")?`）三职；`proposal-null-safe-behaviors.md` 又把 `?.` 铺到语句位置（`For Each item In collection?`、`Await someTask?`）。**同一符号在类型位置、表达式后缀、成员访问、语句位置四处出现，含义互相纠缠。** 在任何一份建议把 `?` 的语义钉死之前，`String?` 没有稳定的参照物——这是轨道 2 被 Table 的核心理由之一，今天依旧成立。

**`!` 已被永久否决**（2017.08.30：`dict!key` 与 `Dim radius!`）。任何依赖 `!` 的 C# 式方案在 VB 里没有对应物。

**关键字式标注（PROPOSAL B）的歧义：** `As String NotNothing` 里的 `NotNothing` 是上下文关键字，与 `IsNot` 守卫的 `Not Nothing` 拼写纠缠；写错成 `Not Nothing`（带空格）会先撞上既有的 `Not` 逻辑运算符。规则可写（`NotNothing` 单 token vs `Not` + `Nothing` 双 token），但 IDE 着色、语义模型都要区分。措辞最终选型是 B 的技术性难点，不是否决项。

#### 2. 角案例与边界语义

**未初始化声明（本特性最大的角案例）。** VB 的 `Dim s As String` 默认 `Nothing`。C# 式 NRT 下这会报警；B 方案下它不报警（未标注=可空）。但 B 引出一个对称的坑：**字段 `Private _name As String` 也默认 Nothing**——若作者后来把 `_name` 标成 `NotNothing`，构造函数/初始化器没赋值就得报警（等价于 C# 的 CS8618）。VB 大量类依赖字段默认值，这个迁移面要单独设计。

**`Nothing` 的写入。** `Nothing` 可赋给任何引用类型。若 `t As String NotNothing`，`t = Nothing` 必须报警——但 `t = CType(Nothing, String)`、`t = DirectCast(Nothing, String)` 呢？绕行语法必须一并堵住（C# 有同样的 `!` 逃逸门；VB 的逃逸门待定）。

**泛型。** `Function F(Of T)(x As T) As T`——无约束 `T` 本身"可能为 Nothing"（引用类型实参）且"可能为值类型"（结构实参，此时非空）。`T` 上加非空标记怎么与约束系统交互？`Of T As NotNothing`？这是 C# 也头疼的区域（C# 8/9 对无约束 `T?` 有特殊规则），VB 必须独立设计。`Suspect`：泛型 nullability 是 C# 花了两个版本才磨平的，VB 不要低估。

**数组。** `Dim a As String()`：数组引用本身可为 Nothing，元素也可为 Nothing。`String()` 的"元素非空"与"数组非空"是两个正交承诺（C# 用 `string?[]` vs `string[]?` 区分）。VB 的 `?` 后置在这种双位置场景下可读性更差。`Probably`：B 方案下数组元素保证（`String() NotNothing` 表元素）语义不清，v1 应只承诺数组引用非空、元素保持可空。

**`ByRef`。** `Sub S(ByRef s As String)`：`ByRef` 语义是"调用方传入 + 被调方可能改写"，参数的非空保证在调用点无法验证（被调方可以写 `Nothing`）。C# 靠 `[NotNull]` 等属性补。VB 的 `ByRef` 在引用类型上本来就传引用，`ByRef s As String NotNothing` 的保证是"进出都非空"还是"初始非空"？语义要写清。`OPEN QUESTIONS`。

**`TryCast` / `DirectCast`。** `TryCast(x, T)` 失败返回 Nothing ⇒ 结果永远"可能为空"；`DirectCast(x, T)` 成功则非空但要求输入非空。这与可空性流分析会议已定的规则一致，此处只是把返回值类型标注进来。`CType` 同理（可能抛，非空）。

**晚期绑定 / `Object`。** `Dim o As Object` 后 `o.Foo()`：`Object` 在 `Option Strict Off` 下是晚期绑定的温床，编译器无法跟踪可空性。C# 的 `dynamic` 对 NRT 完全 oblivious；VB 的 `Object` 应同样对待——**不参与可空分析，既不给警告也不给保证**。与可空流分析会议第 6 条一致：轨道 2 在 `Option Strict Off` 下整体不启用。

**`If()` / `AndAlso` / `OrElse` / 守卫 / 循环。** 全部沿用可空性流分析会议已定的共享引擎规则，不重复设计；NRT 只是给该引擎增加"类型层面的可空标注"这一状态输入。`TypeOf x Is T` 真分支同时收窄为非空 + T（已定）。

**`?.` / `??`。** `x?.Member` 永远安全，不产生诊断；`??` 合并结果非空。若 B 落地，`x?.Member` 在 `x` 已标非空时报冗余警告吗？我们倾向**不报**——`?.` 是既有惯用法，报冗余等于又给用户添墙。`??` 与 `?` 指示符（null-coalescing 建议）的解析纠缠（`a? ?? b`）留给那份建议。

**`Any` 伪类型。** 建议 Drawbacks 提到与 `Any` 交织。`Any` 在 ModVB 里是"任意类型"伪类型（`proposal-any-pseudotype.md`），与 `Object` 类似应作 oblivious。`Probably`：`Any` 与 NRT 无特殊交互，交给伪类型建议处理，本建议不承诺。

#### 3. 作用域与绑定

对轨道 2（若语法落地），`String?` 与 `String` 绑定到**同一个** `System.String` 类型符号，可空性是符号上的注解（`NullableAnnotation`），与 C# 同构；`GetTypeInfo` 需携带该注解，语义模型、符号显示、补全、InfoTip 全链路都要新增"可空标注"维度。这是 C# 已经付过一遍的 Roslyn 成本，VB 要再付一遍——具体量级 `Probably` 接近 C# 的实现面（属性传播进泛型、lambda、继承是 2018.02.07 Part 2 说的 *"it won't be as simple as managing attributes"* 所指）。对 PROPOSAL D（属性发射），绑定层基本不动，只有发射层 + 元数据读写，成本低一个量级。

#### 4. 与既有特性的交互

- **`Option Strict` 分叉**：见第 6 条。`Option Strict Off` 下轨道 2 不启用。
- **`Nothing` 双重语义**：`Nothing` 对引用类型是空引用、对值类型是 `default(T)`。NRT 只涉及其引用类型一面；值类型一面（`If(False, 0, Nothing)` 推 `Integer` 0）已被 `Null` 字面量会议另案处理（Table）。两案边界要写清，避免 NRT 顺手改掉值类型推断。
- **可空值类型的相等 quirk**：2018.02.28 记录——*"Nullable value types also works some place in VB where it doesn't in C#."*、*"quirks in a good way"*。NRT 的引用类型可空分析不得触碰这条既有 quirk；NRT 与可空值类型是两个正交轴（一个是注解、一个是真实类型）。
- **继承 / `Implements` / `Overrides`**：接口声明 `Function F() As Customer`（非空保证），实现方法声明 `As Customer NotNothing`——返回类型可空性协变、参数逆变，需要一套方差规则。C# 9 靠 `[NotNull]`/`[AllowNull]` 兜底；VB 若做 B，`Implements` 签名匹配要把可空标注纳入匹配规则。这是**规范工作量最大**的一块。`OPEN QUESTIONS`。
- **`CallerInfo` / 表达式树 / 迭代器 / async**：这些特性对类型参数、返回类型有改写（`Async` 方法返回 `Task(Of T)`），可空标注如何在改写中保持正确，C# 踩过坑，VB 要写进规范。`Follow-up`。

#### 5. Breaking change 与兼容性

- **B 方案：语法层对存量代码零破坏**（未标注 = 可空 = 现状），这是它最大的卖点。但**警告层有增量**：任何新诊断（`t = Nothing` 对 `NotNothing`、`Return Nothing` 对 `Function() As Customer NotNothing`）都是"重编译后出现新警告"，需 `langversion`/项目开关门控，默认关闭。
- **D 方案：破坏发生在 C# 消费端，不在 VB**。若 VB 开始发射 nullability 属性，C# 消费端会收到此前没有的警告（"VB 返回值可能为 null"）。这是**跨语言**破坏性变更：不是你的重编译行为变了，是**下游**的重编译行为变了。2018.02.07 Part 2 讨论时把它当价值（"allow VB projects to express their nullability to C# projects"），但没说清默认发射策略的破坏面。**发射策略必须默认保守（或默认不发射）**，否则我们替下游制造了 C# 的警告墙。
- **迁移矩阵**：若激活，必须产出"运行期 NRE → 编译期警告 → 如何修复"的样例矩阵，不能只说"更好"。

#### 6. Option Strict / 编译选项分叉

两条路径行为必须一致，且**不得在 `Option Strict Off` 下引入类型安全警告**——用户明确放弃类型安全时，不该得到类型安全噪音。B 方案的可空标注在 `Option Strict Off` 下应整体不启用（与可空流分析会议第 6 条同一立场）。`Option Infer` 与 NRT 无冲突（`Dim s = GetCustomer()` 推断类型不带可空标注，除非作者显式标注——`Probably`：v1 不推断标注，避免隐式改变推断结果）。

#### 7. IDE / IntelliSense

波浪线、快速操作（插入守卫 / 建议 `?.` / 建议 `??` / 建议 `NotNothing` 标注）、悬停显示"Customer（保证非空）"或"Customer（可能为 Nothing）"、符号显示 `String NotNothing`——全链路要原型验证。错误文案是上一场可空性流分析会议已否决过的坑（"`Move` is not a member of `Animal?`"），此处同样**禁止**把 `String?` 当包装类型来写文案；`String?` 与 `String` 是同一 CLR 类型，文案应走"可能为 Nothing 的解引用"路线。

#### 8. 数据 / 普遍性

这是本建议最弱的一环。我们有：C# 口碑混合（Anthony 判据）、主线"等待采用率"的既定决定（2018.02.07）、NRE 是常见运行期异常（外部常识）。我们**没有**：VB 用户请求数、VB 库作者对"向 C# 表达契约"的需求调查、一个分析器原型能捕获多少真实 bug 的遥测。Anthony 的失败判据（"用户会关掉它"）恰恰需要一个**事前**测量计划，而建议里没有。**在 VB 自己的需求数据出现之前，"普遍性"是假设，不是证据。** 这与 `Null` 字面量会议、可空性流分析会议的第 8 条结论完全同构。

#### 9. 更简替代

- **`?.` / `??`**：已存在，覆盖"调用者侧防御"，不覆盖 API 契约。
- **analyzer + 诊断**：一个 Roslyn analyzer 今天就能做引用类型的守卫流分析（近似轨道 2），不碰语言；能提示"`Return Nothing` 违背了你文档注释里的非空承诺"（XML doc `/<returns>` 解析）。分析器覆盖 C 的全部与 B/D 的部分价值，代价是拿不到语法级契约与 C# 可见属性。
- **PROPOSAL D 先行**：属性发射 + 推断系统是"无语法兑现跨语言价值"的最小面，也是 B 的前置实验。这是我们对"更简替代"的答案：**先做 D 的原型，拿数据，再决定 B 的语法。**

#### 10. 成本 / 优先级

- **B（完整语法面）**：Roslyn 语法 + 绑定 + 流分析 + 元数据 + IDE 全链路 ≈ C# 8 NRT 的实现量，VB 再付一遍；再加泛型方差、`Implements` 匹配、`ByRef` 规则这些 VB 特有的边角。**这是本仓最大体量的特性之一，仅次于模式匹配/生成式编译器。**
- **D（属性发射）**：元数据读写 + 推断策略 + 发射管线，一个量级小于 B。
- **C（纯分析）**：复用已 Active 的轨道 1 引擎，把事实提取表扩到引用类型守卫，中等偏低。
- **优先级排序建议**：C 已有轨道 1 在推进，不依赖本建议；D 是可独立验证的最小切片；B 必须排在 D 之后且等激活信号。**本建议当前状态对任何切片都不是阻塞项。**

#### 11. 运行时 / CLR 硬约束

NRT 的存储层是属性（C# 8 自创的 `NullableAttribute`，CLR 不识别、只有编译器读）。无 PEVerify 影响、不触 CLR 存储规则。真正的约束是 2018.02.07 Part 2 那句 *"at least at the implementation level it won't be as simple as managing attributes"*：**标注要通过泛型、lambda、继承、async 状态机传播**，这部分是编译器管线的复杂度，不是运行时约束。发射器版本兼容（新属性在老运行时上编译 = 无影响，属性被忽略）——`Probably`：无运行时依赖，VB 16 编译产物可在旧框架运行。

#### 12. 值不值得做

逐维打分：

- **价值**：真实但分层。运行期 NRE 防护（高，但流分析层 C 已部分兑现）；API 边界契约（中高，跨语言价值主要在这里）；VB→C# 契约（中高，D 可独立兑现）。
- **成本**：B 极高（C# 体量 + VB 特有边角）；D 中低；C 中低（复用引擎）。
- **风险**：语法默认方向（B 已把最大的"警告雪崩"风险关在门外，但仍欠规范细节）；跨语言破坏（D 的发射策略）；泛型/继承方差（B 的最大未定区）。

**结论：作为完整语言特性（B）现在不值得做——成本与未定区不成比例。但"什么都不做"（E）也错过真实的契约价值。真正的回答是拆分：D 是值得立刻投入原型的最小切片；B 留在 Table 等激活信号。** 热情不抵消可行性，可行性也不该被整体否决掩盖——这是上一场会议"机制与语法面拆开"的同一招。

### VB 基因对照

逐条对照设计原则（评价标准第二部分）：

- **原则 1「永不破坏现有代码」**：B 的"未标注=可空"设计是唯一零语法破坏的路径；但警告层仍属"重编译后新警告"，须门控。A 直接违反（警告雪崩）。
- **原则 2「保持 VB-like」**：`String?` 是 C# 的拼写，`String NotNothing` 读起来像英语（"不是 Nothing"）更 VB。但 `NotNothing` 与 `IsNot` / `Not Nothing` 的拼写纠缠是扣分项。
- **原则 3「不引入第二种做事方式」**：NRT 没有改变"写空用 `Nothing`、测空用 `Is Nothing`"的既有方式——它只增加"标记保证"这一新动作，算**扩展**而非"第二种方式"；前提是不发明第二个 null 字面量（`Null` 已 Reject，守住了）。
- **原则 4「默认跟随 C#，除非有充分理由」**：C# 的默认方向（非空为默认）对 VB 不成立，理由充分——VB 的 `Dim s As String` 默认 Nothing，C# 的 `string s;` 必须明确赋值，两个语言的声明语义根本不同。**这是本建议行使"充分理由偏离"的核心论据。**
- **原则 5「读起来像英语、对新手友好」**：`NotNothing`（若选它）比 `?` 更友好；但任何标注都是新概念，对首次开发者是负担。折中：默认关闭，新手不接触。
- **原则 6「不为边缘场景加特性」**：泛型方差、`ByRef`、数组元素保证都是边缘区——我们已把 v1 明确排除。
- **原则 7「避免隐蔽的控制流/语义变化」**：NRT 改变失败时机（运行期→编译期），必须警告 + opt-in + 门控，否则就是又一个 `Return?`。本建议未写这条，我们补上。
- **原则 8「不与既有语法冲突」**：`!` 冲突（2017.08.30，永久否决）；`?` 与 null-coalescing 建议的过载冲突（当前最大的未决语法问题）。
- **原则 9「消除常见样板」**：NRT 对"必须记得写守卫"的样板有正面价值，但这是流分析层的功劳（C 已部分兑现），不是标注层的。
- **原则 10「冗长只在有用时是美德」**：B 的"只为保证付费"正中靶心——大多数存量代码无需任何标记。

**2.3 主线对照表**：NRT 属于 `Null 安全全家桶` 一行——主线对 null 条件 AddHandler 已 No Plans、Anthony 大范围铺开、"主线保守，Anthony 激进"。更精确地说，NRT 是 2018.02.07 **主线明确推迟**、且**被自己团队标注"可能不 VB"**的特性；Anthony 想重设计但没有交付任何语法。本建议的激进不是"做了激进设计"，而是"在主线决定推迟后仍坚持推进"，却在设计上零进展。**与主线关系：主线保守推迟，Anthony 意图激进但无产出；本建议尚未与主线产生正面冲突，因为它还没有可冲突的设计。**

### 诚实分层

- **事实**：
  - 2017.08.30：NRT *"Not ready yet. Revisit after the C# Prototype is released and we have more user feedback and concrete design decisions. Also, the damnit operator `!` conflicts with both VBs dictionary-access operator `dict!key` and the type character for single-precision floating-point numbers `Dim radius!`"*。
  - 2018.02.07 Part 1：*"The user opts in per assembly. For other than a greenfield project, this results in a wall of compiler warnings, the user then works through these to resolve."*；*"We'll postpone this until we understand the uptake in C#."*；*"And it may feel like a 'not VB' thing as we understand its usage."*
  - 2018.02.07 Part 2：*"It is possible that VB could add the emitting attributes based on a simpler attributing system"*；*"at least at the implementation level it won't be as simple as managing attributes"*。
  - 2018.02.28：可空值类型相等在 VB 与 C# 行为不同（*"quirks in a good way"*）。
  - 2014-02-17：`Null` 字面量 *"Rejected on 2014-01-06"*，三条"Nothing 困惑"原话；2026-08-08 `Null` 字面量会议维持 Reject。
  - Anthony 18.16 判据与第 12 章顶注（见场景节，逐字转引自 proposal）。
  - proposal 原文：*"原文未给出任何语法或示例"*；状态行 Prototype/Implementation/Spec 均为占位链接。
  - `Nullable(Of String)` 因 CLR 约束今天即编译错误；`String?` 语法无文法歧义，但 ModVB 家族 `?` 已过载（null-coalescing 建议核实）。
  - VB 引用类型声明默认 `Nothing`，与 C# 明确赋值语义不同。
- **Probably**：到 2026 年 C# NRT 已被主流采用（2018 年等待的信号已触发，C# 8 于 2019 年交付——外部常识）；但 C# 采用率不能回答"VB 用户要不要它"；B 方案（未标注=可空）语法层对存量零破坏；D 方案发射层成本比 B 低一个量级；NRT 属性无运行时依赖、可在旧框架运行。
- **Suspect**：泛型 nullability 在 C# 是跨版本才磨平的复杂区，VB 会同样复杂；`ByRef` 非空保证的语义（进出都非空 vs 仅初始非空）无法从现有材料定论；VB 库作者对"向 C# 表达契约"的需求缺乏调查；`NotNothing` 措辞与 `Not Nothing` 拼写的混淆率无数据。
- **OPEN QUESTIONS**：
  1. B 方案的非空标记措辞选型（`NotNothing` / `Required` / `Guaranteed` / 其他）及其与 `Not Nothing` 的解析区分。
  2. `ByRef` 参数的非空保证语义；`Implements` / `Overrides` 的可空方差规则（返回协变、参数逆变）。
  3. 泛型无约束 `T` 的非空标记与约束系统交互。
  4. D 方案的默认发射策略（非空 / 可空 / 不发射）与跨语言破坏面。
  5. 数组元素级非空（`String() NotNothing` 表元素吗？）v1 是否承诺。
  6. `Option Strict Off` 与 `Option Infer` 下标注的精确行为。
- **TODO**：
  - 给 Anthony 的 18.16 修订版建立跟踪（他自认"正在修订"，交付日期未知）。
  - 撰写本仓 NRT 的设计空间探索文档（本纪要的可执行产出）。
  - 起草 PROPOSAL D 的最小原型（属性发射 + 保守默认策略），拿 C# 消费端反应数据。
  - 用 analyzer 原型统计"VB 库作者把返回值当非空承诺"的真实比例，为 B 的普遍性补证据。

### RESOLUTION:

我们将本建议**整体保持 inactive（Table）**，不激活、不否决；同时把它拆成三个可分离的切片，各自给出状态：

1. **切片 1（流分析机制）＝ 已 Active，不归本建议。** 可空性流分析轨道 1（`Nullable(Of T)`）与 TypeOf 收窄共享引擎，本周已 Active；轨道 2（引用类型守卫分析）只等本建议的语法面或直接以 C 的零语法形态先行。**本建议对切片 1 不是前置条件。**
2. **切片 2（VB→C# 空性契约发射，PROPOSAL D）＝ Consider。** 这是 2018.02.07 Part 2 猜想的最小兑现面：无 VB 可见语法、独立于语法设计、直接服务跨语言契约缺口。**先做原型：** 元数据读写 + 保守默认策略（默认不发射或全按可空发射，避免替下游制造警告墙）+ C# 消费端反应评估。
3. **切片 3（VB 可见标注语法）＝ Table。** 方向取 **PROPOSAL B 的反转默认**（标记"保证非空"而非"标记可空"），因为只有它避开"`Dim s As String` 警告雪崩"；明确否决 C# 式 `?` 默认方向（A）与 `!` 运算符（2017.08.30 永久冲突）。语法措辞、`ByRef`/泛型/`Implements` 方差、数组元素保证全部未定，列入 OPEN QUESTIONS。

**激活所需信号（明确写给未来）**：

- **信号 1（需求数据）**：VB 自身的需求量化——不是 C# 采用率（该信号已触发且不充分），而是 VB 用户/库作者对"API 边界非空契约"的实际需求数据（analyzer 原型捕获的 bug 数、issue/调查计数）。
- **信号 2（语法落定）**：一份不再写"原文未给出任何语法"的、具体的、通过反转默认设计的语法草案，且解决了 `NotNothing`/`Not Nothing` 解析与 `?` 过载问题。
- **信号 3（前置验证）**：切片 2（属性发射）原型跑通，证明"更简单的标注系统"成立；切片 1 引擎上线，证明共享流分析可用。
- **信号 4（雪崩测试）**：对典型 VB 代码库（含大量 `Dim s As String` 未初始化声明）跑通"默认关闭、显式标记才警告"的迁移模拟，证明无警告雪崩。
- **信号 5（跨语言策略）**：D 的发射默认策略定案，且下游 C# 消费端破坏面被接受或对冲。

**Verdict: Table（整体）＝ 切片 1 Active / 切片 2 Consider / 切片 3 Table；`!` 运算符 Reject；C# 式 `?` 默认方向 Reject。**

### Implication:

- proposal 头部加一行："LDM 2026-08-08: Table。切片拆分与激活信号见 `meetings/inactive/meeting-nullable-reference-types.md`"。
- 启动切片 2 最小原型：Roslyn 元数据读写 + nullability 属性发射 + 保守默认策略；验证 C# 消费端语义。
- 撰写设计空间探索文档，把 PROPOSAL A–E 的权衡、`NotNothing` 措辞选型、泛型/`ByRef`/`Implements` 方差问题归档，供 Anthony 修订版或未来会议取用。
- 跟踪 Anthony 18.16 修订版；若修订版给出语法，用本纪要的激活信号重新评估。
- 与 null-coalescing 建议对表：`?` 的语义必须在切片 3 动工前钉死，否则轨道 2 无稳定参照物。
- 与可空性流分析会议对表：轨道 2 的引擎接口（Null / NotNull / MaybeNull × 类型收窄）已就绪，切片 3 只增加"类型层面可空标注"这一输入。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：切片 2 的发射默认策略（不发射 / 全按可空 / 推断）；见诚实分层。
- `OPEN QUESTIONS`：切片 3 的语法措辞与解析规则（`NotNothing` vs `Not Nothing`）；`ByRef` / 泛型 / `Implements` 方差；数组元素级保证。
- `TODO`：切片 2 原型；设计空间探索文档；VB 库作者需求调查；"`Dim s As String` 雪崩"迁移模拟。
- `Follow-up`：C# 8/9 NRT 的泛型与 `[NotNull]`/`[AllowNull]` 属性方案作为外部参照（标注来源：借鉴 C# 的实现层，非照搬语法）；与 null-coalescing / null-safe-behaviors 建议的 `?` 语义协调会议。

### 状态

- **LDM 状态：Table（保持 inactive）**；切片 2 转入 Consider；切片 1 已在 Active；`!` 与 C# 式默认方向 Reject。
- **三态判定：Table**——本建议作为"设计简报"有价值（问题陈述、失败判据、方向偏好），作为"可实现设计"无内容（零语法、零数据、占位状态）。激活必须等到信号 1–5 中至少需求数据（1）与前置验证（3）兑现。

---

## 附录：特性评价

### 评价对象

- 建议：`inactive/proposal-nullable-reference-types.md` — 可空引用类型（NRT）的 VB 重设计：不让用户困惑、不关掉特性；原文未给出任何语法，自认"实验性 / 修订中"。
- 来源：Anthony 原文 18.16「Nullable Reference Types」（判据与失败观）+ 第 12 章「Null and Nothing」顶注（NRT 需迭代）；隐性借鉴 C# 8 NRT（唯一的具体参照物）；未引用 vblang 主线 2017.08.30 / 2018.02.07 决定。
- 配方目标：为 VB 重新设计可空引用类型，以"用户不关掉特性"为验收。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。问题陈述具体（C# 口碑、VB 困惑、失败判据逐字），但**没有任何示例能演示改进**——Detailed design 原文自认"未给出任何语法"；失败判据是事后指标，无事前测量计划。 | 已提供 | 效果不可验收：无语法、无原型、无数据；连"演示特性"的代码都写不出 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。意图是 VB 化重设计（反转默认、避免警告雪崩），但无具体语法可检查和谐度；"打包"了与 NRT 纠缠的 `Null` 字面量、空安全家族（在 Alternatives/协同关系里点名），边界未拆。 | 已检查 | 意图 VB 化但零落地；与 `Null` / 空安全家族边界不清 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、对自身"无语法"状态诚实（未虚构语法，这是加分）；但 Detailed design 是空壳、Drawbacks/Alternatives 各 3 条且浅、无 BNF/无兼容性分析/无 Option Strict 分叉、未决问题 4 条关键点但把"与 NRT 衔接"当普通条目、状态行全占位链接。 | 已检查 | 核心章节空洞；占位链接；未决问题低估；缺兼容性分析 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。得=雷（明确需求、为轨道 2 划地界）、水（跨语言契约差异化机会）、光（盘活 `Nothing`/`Is Nothing` 既有语义）；失=暗（若照搬 C# 方向则警告雪崩，文档只提"C# 口碑不佳"未做破坏面分析）、风（依赖未定型设计 + 与 `?` 过载冲突，一致性风险未识别）。 | 已检查（预测待定） | 破坏面（警告雪崩/跨语言发射）未被识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。Anthony 章节（18.16 / 第 12 章顶注）标注了；C# 借鉴（唯一参照物）作为"被否决对象"出现但未展开其实现层价值；**关键遗漏：vblang 主线 2017.08.30 与 2018.02.07 的 NRT 决定完全未引用**——这两条是本特性最重要的事实材料。 | 已检查 | 主线先例缺失；C# 借鉴仅当否定对象，未提取可用成分 |

### 设计原则对照

- **与 VB 基因：意图一致，落地未评估**。设计目标（不惩罚存量、默认关闭、避免第二种做事方式）与原则 1/3/4/10 同向；但没有任何语法可验证原则 2/5/8。唯一可判定的是它对 `!` 的回避（守住了原则 8）——尽管回避是被 2017.08.30 逼出来的，不是主动设计。
- **与主线关系：主线保守推迟，Anthony 意图激进但无产出**。2.3 对照表 `Null 安全全家桶` 一行成立；NRT 在主线是 2018.02.07 明确推迟、且被标注"可能不 VB"的特性。本建议尚未与主线正面冲突（无可冲突的设计），但它的"替代方案 1（沿用 C#）被否决"方向上与主线"默认跟随 C#"形成张力。
- **破坏性变更：潜在有，文档未分析**。语法层若取 B 可零破坏；若取 A 则有"`Dim s As String` 警告雪崩"；跨语言切片 D 有下游 C# 消费端破坏。三种破坏文档都未写。需警告 + opt-in 门控 + 迁移矩阵。

### 总评

- **达成程度：未达成**（作为设计）／**部分达成**（作为设计简报）。它成功陈述了问题、判据与方向偏好，但没有交付任何可评估、可演示、可实现的内容；效果维度因"零语法"封顶 2 分。
- **LDM 三态建议：Table**（切片 1 Active / 切片 2 Consider / 切片 3 Table；`!` Reject）。
- **主要问题**：① 零语法、零数据、占位状态——效果不可验收；② 失败判据无事前测量计划，"防止用户关掉"没有对应数据采集；③ 未引用主线 2017.08.30 / 2018.02.07 决定，错过最重要的先例材料；④ 未拆"机制 / 语法 / 跨语言契约"三切片，把整件事当一块；⑤ 破坏面（警告雪崩、下游 C# 消费端）未分析。

### 返工建议

- **补充章节**：语法规范（哪怕一页：反转默认 + 措辞选型 + 与 `Not Nothing` 解析区分）；兼容性/breaking-change（警告雪崩模拟、跨语言发射策略、langversion 门控）；与主线先例的关系专节（引用 2017.08.30 与 2018.02.07 逐字决定）；Option Strict Off / Option Infer 分叉。
- **补充证据**：切片 2 属性发射原型（保守默认策略 + C# 消费端反应）；analyzer 原型的 bug 捕获数据；VB 库作者需求调查；典型 VB 代码库的"`Dim s As String` 雪崩"迁移模拟。
- **未决问题处理**：把 6 个 OPEN QUESTIONS 逐一给定夺方式（措辞选型、`ByRef` 语义、泛型方差、发射默认策略、数组元素保证、Option Strict 行为）；明确"激活所需信号"的五条清单并跟踪。
- **设计探索**：泛型可空方差（返回协变 / 参数逆变）与 `Implements`/`Overrides` 签名匹配；`[NotNull]`/`[AllowNull]` 属性方案在 VB 的形态（借鉴 C# 实现层）；`?.`/`??` 与标注的冗余警告策略；`TryCast`/`DirectCast`/`CType` 返回值标注。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\..\csharplang`（dotnet/csharplang 官方仓库镜像）核实；C# 原文逐字引用并标注来源文件。本提案（可空引用类型，VB 的 NRT）与 **C# 8 NRT 直接对应**，是 VB 主线讨论过但从未落地的领域（2017.08.30 / 2018.02.07 推迟），因此本附录聚焦 **C# NRT 完整机制（`?` 注解、`!` null-forgiving、流分析、Nullable 元数据）vs 本提案的切片拆分**，以及跨语言 NRT 元数据互操作。

### 相关 C# 现实方向

C# 已在 2019 年交付完整的 NRT 机制（C# 8）。核实到的关键事实：

- **官方一句话定位**（`Language-Version-History.md` C# 8.0 条目，逐字）："Nullable reference types: express nullability intent on reference types with `?`, `notnull` constraint and annotations attributes in APIs, the compiler will use those to try and detect possible `null` values being dereferenced or passed to unsuitable APIs."
- **目标**（`proposals\csharp-8.0\nullable-reference-types.md` Summary，逐字）："Allow developers to express whether a variable, parameter or result of a reference type is intended to be null or not." 与 "Provide warnings when such variables, parameters and results are not used according to that intent."
- **默认方向＝非空**（同文件，逐字）："It is assumed that the intent of an unadorned reference type `T` is for it to be non-null." —— 这是与本提案 PROPOSAL B 反方向的关键事实。
- **`!` damnit 运算符**（同文件，逐字）："A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator), for when flow analysis cannot establish a non-null situation that the developer knows is there."
- **流分析**（同文件，逐字）："A flow analysis tracks nullable reference variables."（守卫后/赋值后视为非空——与本提案切片 1 共享引擎同构。）
- **元数据表示＝属性**（同文件，逐字）："Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them." —— 本提案切片 2（属性发射）的 C# 侧依据。
- **opt-in 体验是成败关键**（同文件，逐字）："The design of the opt-in/transition experience is crucial to the success and usefulness of this feature."；"you need to be able to opt in/out of: Nullable warnings / Non-null warnings / Warnings from annotations in other files"；"Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism." —— 与 Anthony 失败判据（"用户会不会关掉它"）直接对表。
- **上下文模型**（`proposals\csharp-8.0\nullable-reference-types-specification.md`，逐字）："Every line of source code has a *nullable annotation context* and a *nullable warning context*."；"If no project level settings are provided the default is for both contexts to be *disabled*."；四态 nullability："A given type can have one of four nullabilities: *Oblivious*, *nonnullable*, *nullable* and *unknown*."；"An unannotated reference type `C` in an *enabled* annotation context is *nonnullable*"。2018.02.07 记录里的 "per assembly" 后来被细化为 `#nullable` 指令 + 项目级 + `#pragma warning` 的三层粒度。
- **C# 9 打磨泛型**（`proposals\csharp-9.0\unconstrained-type-parameter-annotations.md`，逐字）："For return values, `T?` is equivalent to `[MaybeNull]T`; for argument values, `T?` is equivalent to `[AllowNull]T`." —— 印证本提案"泛型 nullability 是 C# 跨版本才磨平"的 Suspect 判断：C# 8 先给无约束 `T?` 特殊规则，C# 9 再用 `[MaybeNull]`/`[AllowNull]` 属性补。此条可从 Suspect 升级为已核实。
- **属性名与规范源**：`NullableAttribute`/`NullableContextAttribute` 的**名称**不出现在 C# 8 提案/规范正文（正文只写 "attributes"）；名称在仓库内见于 `proposals\csharp-14.0\extensions.md` 的示例 IL（".custom instance void NullableAttribute::.ctor(uint8) = (...)"）与 `meetings\2022\LDM-2022-01-24.md`（"there won't be nearly the proliferation of these attributes that `NullableAttribute` would have had as originally designed"，即发射压缩策略）。其**规范性定义在 dotnet/runtime 而非本仓**。`Suspect`：两个属性名的权威定义源与完整编码格式（参数个数/类型）需到 dotnet/runtime 或 Roslyn 核实。

**生态层面的现实方向**（基于索引 T5/T6/T7 与仓库证据）：可空契约是**编译期静态元数据**，无运行时反射、无 AOT 障碍——与索引 T5/T6 的"类型系统/编译期承担更多职责"同向；NRT 已是 .NET 8+ 库 API 契约的既定一部分（C# 消费端默认读取）。C# 侧 `dynamic` 与 `object` 恒等可转换（spec 原文 *Merge*(`object`, `dynamic`) = `dynamic`），其对可空分析的参与继承 `object` 的上下文敏感性；"dynamic 对 NRT 完全 oblivious" 应理解为 disabled 上下文未标注类型即 oblivious 的推广，精确语义 `Suspect`（本仓未单独为 `dynamic` 定义可空规则）。这与本提案把 `Object`/`Any` 作 oblivious 处理的立场同构。

### 现实 vs 提案

按本提案三个切片逐一对表：

- **切片 1（流分析机制）＝ 兼容，C# 已实证。** C# 的 "A flow analysis tracks nullable reference variables" 就是切片 1 轨道 2 要做的同一件事（`Is Nothing` 守卫 → 视为非空）。C# 证明该机制可独立于标注工作；本提案将其归入已 Active 的轨道 1，与 C# 现实一致。
- **切片 2（VB→C# 空性契约发射，PROPOSAL D）＝ 需桥接，方向与 C# 现实一致。** C# 的元数据载体就是属性、且 "downlevel compilers will ignore them"——D 发射的正是同一套载体，因此**无运行时破坏**（新属性在旧框架被忽略，与本提案第 11 节"无运行时依赖"互证）。桥接点：VB 必须发射 C# 消费端认识的确切属性（`NullableAttribute`/`NullableContextAttribute` + `[MaybeNull]`/`[AllowNull]` 等特殊行为属性），否则 C# 读到的是"无标注＝上下文默认"而非 VB 意图。C# 侧有现实证据支持 D 的保守默认策略（逐字，`proposals\csharp-8.0\nullable-reference-types.md`）："adding annotations to an existing API will be a breaking change to users who have opted in to warnings, when they upgrade the library." —— 跨语言发射就是"替下游加标注"，破坏面必须默认保守（对应信号 5）。
- **切片 3（VB 可见标注语法，PROPOSAL B）＝ 语义冲突，但元数据层可调和。** 冲突点：C# 默认方向是 "unadorned reference type `T` ... non-null"（enabled 上下文），本提案 B 默认方向是"未标注＝可空"。这是两个语言对"没写标注意味着什么"的根本分歧（Q&A 已论证 C# 方向对 VB 造成"警告雪崩"）。调和点：只要 VB 把 B 的标注翻译成 C# 读得懂的属性，C# 消费端看到的就是正确契约，两个默认世界在**元数据层不冲突**；真正不可调和的是**语法层**（VB 用户要 `NotNothing` 而非 `?`）与**消费方向**（C# 库的 `string` 非空保证，VB 默认可空世界是否采信——`OPEN QUESTIONS`）。
- **A 方案（C# 式移植）＝ 与 C# 现实一致，但口碑已被验证为负。** C# 自己把 opt-in 体验当成败关键、把"用户被迫在警告墙与关闭之间选择"当设计难题；Anthony 判据正是观察 C# 口碑后的产物。A 在 VB 里复制同样的 opt-in 复杂度 + `!` 语法冲突，C# 现实不但不帮忙，反而是反例。
- **脱节点（互操作核心）**：C# 的 *oblivious*（disabled 上下文未标注类型）在 VB 没有直接对应物——VB 的"未标注＝可空"更接近 C# 的 *nullable* 而非 oblivious。跨语言读取 C# 库时，VB 必须决定把 C# 的 *nonnullable*（enabled 上下文未标注）当作什么：按 B 语义是"非空保证"（可提供更强契约），但若 VB 编译器不读这些元数据，就退化为"可空默认"（丢失契约）。

### 对 VBScript.NET 的适应建议

- **识别可空元数据是跨语言互操作的最小必要条件**：VB 编译器/绑定层必须能读 `NullableAttribute`/`NullableContextAttribute` 及 `[NotNull]`/`[AllowNull]`/`[MaybeNull]`/`[MemberNotNull]` 特殊行为属性，否则消费 C# 8+ 库时契约不可见——与决策文件 M8 的"必须桥接"清单同类（不识别新元数据就无法正确校验）。`Suspect`：Roslyn VB 编译器当前读到什么程度，需查 Roslyn 实现（本仓无正文）。
- **默认安全 / 按需动态**：VB 侧保持"未标注＝可空"（B 的默认），对**来自 C# 的显式 nonnull 标注**按保证处理（调用端获得更强契约）；`Option Strict Off` / `Object` / `Any` 一律 oblivious，与 C# 的 disabled 上下文 + `dynamic` 行为对齐——用户放弃类型安全时不给类型安全噪音。
- **发射策略与切片 2 原型对齐**：D 原型应发射 `NullableContextAttribute`（程序集/成员级默认上下文）+ `NullableAttribute`（逐类型标注），按 C# 消费端约定；默认保守（不发射或全按可空），破坏面交给信号 5 决策。
- **source-gen 桥**：可空契约是编译期静态元数据，天然适合 source-gen/编译器管线，无需运行时反射——与索引 T6（source generators 替代运行时动态）和 AOT/trimming（T5）兼容；解释执行模式（scripting-interpreted）下契约照常发射，不增加运行时负担。
- **语法选型给生态留门**：若切片 3 落地 `NotNothing`，其编译产物应映射到与 C# 相同的元数据语义（标注＝非空），保证 .vbx 程序集对 C# 工具链（分析器、IDE 波浪线、反编译器）呈现一致契约。

### 对既有 RESOLUTION / 三态判定的影响

- **切片 2（Consider）被 C# 生态现实强化**：C# 已确立属性载体且消费端普遍读取，VB 发射契约的互操作价值真实存在；"downlevel compilers will ignore them"证实发射无运行时破坏。C# 现实不改变 "Consider" 状态，但把信号 5（发射默认策略）从"要不要做"推进为"格式怎么发才不被 C# 误读"。
- **切片 3（Table）不变**：B 与 C# 默认方向的冲突不构成否决——元数据层可翻译两种默认世界，分歧在语法层与 VB 用户习惯（信号 2 的范围）。C# 8/9 的泛型打磨经验把 meeting 的"泛型方差 Suspect"升级为"已核实 C# 走 `[MaybeNull]`/`[AllowNull]` 属性路线"，为 VB 的 `Implements` 方差提供具体参照（借鉴实现层，非照搬语法）。
- **整体 Table 判定不改变**：C# 采用率信号（2018.02.07 等待的 "uptake in C#"）已触发且不充分——C# 生态事实补充的是"载体已就绪、互操作有价值"，但**不能替代 VB 自身的需求数据**（信号 1 仍空缺）。C# 现实把 D 的成本预期进一步下调（复用既有载体，不是发明新元数据），是对优先级排序的边际强化，不是推翻。
- **`!` Reject 与 C# 式 `?` 默认方向 Reject 维持**：前者是 VB 语法冲突（2017.08.30），与 C# 无关；后者 C# 现实恰好是反例（C# 的 nonnull 默认对 VB 造成雪崩，Q&A 已论证）。

### 引用纪律与未决

- 本附录 C# 原文均**逐字**摘自 `..\..\..\csharplang` 并标注文件：`proposals\csharp-8.0\nullable-reference-types.md`、`proposals\csharp-8.0\nullable-reference-types-specification.md`、`proposals\csharp-9.0\unconstrained-type-parameter-annotations.md`、`Language-Version-History.md`、`proposals\csharp-14.0\extensions.md`、`meetings\2022\LDM-2022-01-24.md`。
- `Suspect`：`NullableAttribute`/`NullableContextAttribute` 的权威定义源在 dotnet/runtime（本仓仅见名称引用与压缩策略讨论）；`dynamic` 在 C# 可空分析下的精确规则（本仓未单列，spec 只写 `object`/`dynamic` 恒等可转换）；Roslyn VB 编译器当前对可空属性的读取程度（本仓无正文）。
- `OPEN QUESTIONS`：C# 库的 nonnull 标注在 VB 默认可空世界中的映射规则（"消费方向"）；VB `NotNothing` 产物与 C# 元数据语义的精确对齐方式；跨语言发射时 `NullableContextAttribute` 程序集级默认上下文的取值。
