# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论 Anthony 第 8 章（General Modernization and Evolution II，Declarations）尾部的委托增强建议。这份建议把**两件成熟度差一个量级的事**捆在一起：`+=`/`-=` 委托合并（2014 年主线就 in-principle 批准过、C# 早有 parity，价值与成本都清晰）与**显式匿名委托类型** `<Function(Double, Double) As Double>`（Anthony 原创语法、主线无对应、类型系统工作量巨大）。会议的主要工作因此与递归 Lambda 那场一样，从**解绑**开始。

## Agenda

* [Proposal: 委托增强与匿名委托类型（Delegate Enhancements）](#proposal-委托增强与匿名委托类型)

## Proposal: 委托增强与匿名委托类型

_Related: [vblang 2014-02-17 LDM 纪要第 #31 条 – Combine delegates with +=（Approved in principle, parity with C#）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-02-17.md)；同纪要第 #26 条 – Allow delegates in object initializers；[vblang LDM 2015-01-14 – delegate relaxation level（`DelegateRelaxationLevelReturnWidening`）](https://github.com/dotnet/vblang/tree/main/meetings/2015/LDM-2015-01-14-VB.md)；VB 规范 `overload-resolution.md` §delegate relaxation levels（31–36）；ModVB：`proposal-null-literal.md`（`Null`，本组会议判定 Table）、`proposal-local-declarations.md`（`Let`）、`proposal-recursive-lambda-inference.md`（Lambda / 委托类型推断）、`proposal-wildcard-lambdas.md`（Lambda 语法糖家族）_

### 场景与缺口

We started from Anthony 第 8 章的六行示例（`..\AnthonyDesign_wordpress.txt` L1509–1517，原样摘录）：

```vb
' Strongly typed [Delegate].Combine/Remove with + and -.
Let handlers As EventHandler = Null
handlers += AddressOf Button_Click
handlers -= AddressOf Button_Click

' Explicit syntax for anonymous delegate types.
Let binOp As <Function(Double, Double) As Double>

' Not shown: Relaxed delegates can be removed.
```

We see a genuine DX parity gap in the first half. 在本编译器（VBScriptDotNet 所基于的 Roslyn VB）里，维护委托变量列表今天只能手写 BCL 调用：

```vb
' 今天（vanilla VB / 本编译器，已检查）：
Dim handlers As EventHandler = Nothing
handlers = [Delegate].Remove([Delegate].Combine(handlers, AddressOf Button_Click), AddressOf Button_Click)
```

- `[Delegate].Combine` / `[Delegate].Remove` 冗长、易错，且 `.Remove` 每次都要整条重写。
- 事件有 `AddHandler` / `RemoveHandler`，但**委托变量**（不是事件）没有等价的自然写法——主流语言（C# 自 1.0 起）的标准写法是 `+=` / `-=`。这是主线自己承认过的缺口：**2014-02-17 会议第 #31 条原话："Approved in principle, but need design work. Parity with C#."**，且当时就明确了与 C# 语义一致、但**不**把 `event += value` 当 `AddHandler` 的同义词。
- 第二个缺口（匿名委托类型）是另一代人：声明"匹配某签名的委托"今天要么写一条具名 `Delegate Function`（样板），要么用 `Func(Of T, T)`（只能覆盖一部分签名，ByRef / 自定义委托签名表达不了）。需要一个**简洁的委托签名类型**。

但 We think 把这两个缺口绑进一份建议是不诚实的边界：前半是"主线已批准、只差落地"，后半是"原创语法、类型系统工作量、语法与 XML 冲突"。会议主线从解绑开始。

### 候选方案

**PROPOSAL A — 全文捆绑。** 按建议原文一次落地：`+=` / `-=` 委托合并 **和** `<Function(Double, Double) As Double>` 显式匿名委托类型，外加 `Null` 字面量初始化（依赖本组 Table 的 `proposal-null-literal.md`）。

**PROPOSAL B — 只做 `+=` / `-=`（含二元 `+` / `-`），匿名委托类型交给运行时 `Func/Action`。** 前半为强类型 delegate 变量提供 `+=` / `-=` 复合赋值与 `+` / `-` 二元运算符，语义 parity C#；匿名委托类型不引入，`Func/Action` 能覆盖的场景继续用 `Func/Action`。

**PROPOSAL C — `+=` / `-=` + 匿名委托类型，但语法换成 `Delegate(Of Func(Of Double, Double) As Double)`。** 用 `Of` 泛型语法（VB 原生）包一层，避开尖括号与 XML 的冲突。建议原文 Alternatives 列的路线之一。

**PROPOSAL D — 什么都不做。** 保留 `[Delegate].Combine` / `[Delegate].Remove` 与具名委托；匿名签名继续靠 `Func/Action` 与样板委托。代价：委托变量管理仍是"多步手写"；`Func/Action` 覆盖不了的签名（ByRef、自定义返回）仍只能写具名委托。

### 权衡：Q&A

- **前半是不是已经在主线里？** 这是全场第一个要钉死的事实。我们在本编译器里核过：`BinaryOperatorKind` 枚举（`Compilers/VisualBasic/Portable/CodeGen/OperatorKind.vb`）只有 `Add / Concatenate / Subtract / ...`，**没有** `DelegateCombine / DelegateRemove` 及其赋值变体；全编译器检索 `DelegateCombine|DelegateRemove` 零命中；测试目录也搜不到 `+= AddressOf` 或 `Combine(`。规范 `expressions.md` §Addition Operator 的运算类型表里也只有数值与 `Date`，无 delegate。**结论（已检查）：本编译器不支持委托 `+` / `-` / `+=` / `-=`——2014 年的 in-principle 批准从未落地为特性。** 所以前半不是"重申既有行为"，而是填补一条真实缺口；但它也不是新设计，是复活一条 12 年前的批准。
- **只做 `+=` / `-=`，还是连二元 `+` / `-` 一起？** C# 两者都有。2014 纪要明确留了尾巴："STILL TO DO: figure out exact semantics for "+""（`m_ClickEventHandler = m_CLickEventHandler + value`）。We think 二元 `+` / `-` 与复合 `+=` / `-=` 是一套语义的两面，只做复合不做二元会造出"为什么 `+` 不行"的疑问；且 `x = x + y` 是复合 `x += y` 的去糖基础。**结论：一起做，去糖为 `Delegate.Combine` / `Delegate.Remove`。**
- **事件怎么办？** 主线 2014 年就给了逐字答案："However we will not also allow "event += value" as a synonym for "AddHandler event, value"." 我们维持：`AddHandler` / `RemoveHandler` 是事件唯一入口；本特性只作用于**委托变量**。这也顺带回答了"第二种做事方式"的追问——对事件，`+=` 不会成为第二条路；对委托变量，今天没有自然的第一条路，`+=` 是补第一条路，不是加第二条。
- **`Null` / `Nothing` 边界需要语言层特例吗？** We think 不需要。BCL 对 `Delegate.Combine` / `Delegate.Remove` 的 `Nothing` 行为是文档化的、确定的：`Combine(Nothing, d) = d`、`Combine(d, Nothing) = d`、`Remove(d, Nothing) = d`、`Remove(Nothing, d) = Nothing`、`Remove(d, d) = Nothing`（`Remove` 移除调用列表中**最后出现**的实例，找不到时原样返回 `source`）。建议原文的未决问题"`handlers` 为 `Null` 时 `+=` / `-=` 的边界行为"因此有干净答案：**直接委托给 BCL 语义，语言层不另设规则**。前提是把 `Null` 当作 `Nothing` 处理（见 RESOLUTION 第 4 条——依赖本组 Table 的 `Null` 字面量，落地时以 `Nothing` 为基）。
- **宽松（relaxed）委托能不能移除？** 这是全场的第二个尖锐追问，也是建议原文唯一用 "Not shown" 留白的点。`Delegate.Remove` 的匹配靠 `Delegate.Equals`（比较目标对象 + 方法）；Option Strict Off 下 `AddressOf 一个无参方法` 绑到有参委托（spec 的 "Drop return or arguments delegate relaxation"，level 34）时，编译器要合成一个适配器 lambda。`+=` 与 `-=` **必须用同一宽松规则、合成同一个适配器**，`-=` 才能命中。若 `+=` 宽松而 `-=` 不宽松（或反之），`Remove` 找不到实例 → 静默不删。这不是"能不能"的问题，是"编译器必须保证确定性"的问题。**结论：可行，但必须写进 spec；原型先钉死"同一宽松转换产生同一适配器"这一不变量。** 若原型证明做不到，v1 的保守退路是：Option Strict Off 下对宽松委托的 `-=` 报错，只允许严格 `AddressOf`（identity，level 36）移除。
- **`+=` / `-=` 的"算术/合并"双义是不是问题？** 建议 Drawbacks 担心"同时有算术加法（不可用）与合并/移除语义"。We think 这是**伪歧义**：delegate 类型不是数值类型，`+` 在 delegate 操作数上**没有**算术含义可选，运算符由操作数类型决定（与 C# 同一机制）。真正的歧义在别处——见深度追问第 1 条（尖括号）。
- **匿名委托类型的尖括号语法。** 见深度追问第 1 条与第 10 条。提前给结论：We think `<Function(...)>` 是这份建议**最弱的一环**——它不是 VB 味儿，撞上 XML 字面量/轴的既有主人，且 C# 无对应语法（无 parity 论据）。这也是 PROPOSAL C（`Delegate(Of ...)`）值得保留的原因。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`+=` / `-=` 无歧义。** 操作数类型决定运算符：两个 delegate 类型（或 delegate 与 `AddressOf`/lambda 目标类型化）→ 合并/移除；数值 → 算术。VB 文法里 `+`/`-`/`+=`/`-=` 均已存在，本特性只是给"delegate 类型操作数"补上绑定与去糖。`+` 的优先级/结合性与既有 `+` 一致，无新文法。

**`<Function(Double, Double) As Double>` 是真歧义。** `<` 在 VB 里是 **XML 字面量与 XML 轴属性的**既有关键字符：`<root/>`、`xml.<name>`、`<?pi?>`。类型位置（`As` 之后）今天没有任何类型文法以 `<` 开头，所以引入它等于给类型文法开一个以 `<` 起头的新产生式——tokenizer 的 XML 字面量扫描是上下文驱动的，`<Function` 在某些位置（尤其作为泛型实参 `List(Of <Function(...)>)`、或 `CType(x, <Function(...)>)` 的第二个参数）极易被误判进 XML 词法。这直接踩中设计原则第 8 条"不与既有语法冲突（如 `!`、类型字符）"。We 无法给出干净的 tokenizer 规则让 `<Function(` 在类型上下文里稳定地不进 XML 分支——`Suspect`：这需要原型验证，且失败概率不低。

```vb
' 类型位置的新文法（提案）：
Sub Register(handler As <Function(Double, Double) As Double>
End Sub

' `<` 的既有主人——XML 字面量与轴：
Dim doc = <root><name>a</name></root>
Dim names = doc.<name>.Value
```

#### 2. 角案例 / 边界语义

**`Null` 边界。** 见 Q&A——BCL 定义行为即所需语义：`handlers = Null; handlers += h; handlers -= h` 走 `Combine(Nothing, h) = h`、`Remove(h, h) = Nothing`，整条链是安全的。`-=` 在 `Nothing` 上**不抛异常**（`Remove(Nothing, d) = Nothing`）。建议原文的未决问题①可关闭。

**重复添加与"移除最后出现"。** `handlers += h; handlers += h` 后调用列表含两个 `h`；`handlers -= h` 按 BCL 只移除**最后一个**。语义与 C# 一致（`Delegate.Remove` 的文档行为），无需语言层额外规则——但要写进 spec 的角案例表，防止用户误以为"移除全部"。

**异构委托类型。** `Combine` 要求两个 delegate 类型兼容（`Delegate` 是引用类型，`MulticastDelegate` 是基类）。若 `handlers As EventHandler` 而右侧是 `Action`，`+=` 应报错（不可转换），而不是静默包成 `[Delegate]` 再 Combine。绑定阶段要拒绝类型不匹配——与 C# 一致（C# 要求 `+` 两侧同 delegate 类型）。

**基类类型变量。** 变量声明为 `System.Delegate` / `System.MulticastDelegate` / `Object` 时，spec 的 delegate relaxation level 33（*Widening delegate relaxation to delegate without signature*）适用：`AddressOf` 可放宽到无签名委托。`+=` 在这些基类变量上是否允许？We think **v1 不允许**：`Delegate.Combine` 返回 `Delegate`，需要强转回具体类型才有意义，而基类变量本就没有签名可言。保守起见只对**具体委托类型**开放。

**返回值委托 vs `Sub` 委托。** `+=` 一个 `Function` 委托（有返回值）与 `+=` 一个 `Sub` 委托（无返回值）是两种 `Delegate` 实例，`Combine` 可混（运行时调用列表本来就可混）。但我们不为此加语言规则——`+` 的语义就是 `Delegate.Combine`，混编由 BCL 决定。`Suspect`：真实代码里混编返回值委托的调用列表是极边缘场景，spec 提一句即可。

#### 3. 作用域与绑定

**`AddressOf Button_Click` 在 `+=` 右侧的绑定。** 与 `AddHandler e, AddressOf Button_Click` 相同的绑定路径——`Button_Click` 是方法组，经委托创建表达式绑定到目标 delegate 类型。语义模型里 `GetSymbolInfo` 返回方法组/方法符号；`+=` 整式是复合赋值，`GetSymbolInfo` 返回委托变量。无新符号。

**匿名委托类型的符号面（Table 那半）。** `<Function(Double, Double) As Double>` 若落地，语义模型 `GetTypeInfo` 返回什么？编译器已为"无目标类型的 lambda"合成匿名委托类型（见下），但那是对编译器内部的；显式语法要求用户能**命名**一个合成类型——它需要稳定的符号展示（签名）、可被泛型实例化引用。这是类型系统工作，不是绑定器小修。

**VB 已有匿名委托类型的隐式形态（已检查）。** 规范 `expressions.md` §Lambda Expression 明确："If the target type is not known, then the lambda method is interpreted as the argument to a delegate instantiation expression of an anonymous delegate type with the same signature of the lambda method."——`Dim x = Function(a, b) a + b` 会推断出一个与 `Func(Of Object, Object, Object)` **等价**的匿名委托类型。所以"匿名委托类型"概念不是新东西，新的是"**显式、可命名**"。`Probably`：简单签名的隐式匿名委托会被映射到 `Func/Action` 系（spec 用"equivalent to"表述）；显式语法需要决定"结构相等 vs 映射到 Func"——见第 8 条 OPEN。

#### 4. 与既有特性的交互

- **`AddHandler` / `RemoveHandler`（事件）**：互不干扰。事件入口维持现状；本特性只动委托变量。自定义事件（`Custom Event`，含 `AddHandler`/`RemoveHandler`/`RaiseEvent` 访问器）不受影响。
- **`[Delegate].Combine` / `[Delegate].Remove` 显式调用**：保持可用。`x = x + y` 与 `[Delegate].Combine(x, y)` 是同一语义的两种拼写——这里确有"第二种做事方式"张力，但 `+=` 是主线 2014 批准的 parity 特性，且更短、更符合 C# 用户直觉；显式 BCL 调用保留给需要 `[Delegate].RemoveAll` 等高级场景。
- **lambda 目标类型化**：`handlers += Function(s, e) ...`（lambda 直接给 `+=`）应允许——与 `AddHandler` 接受 lambda 同理；`AddressOf` 方法组同理。target-typed 转换路径已存在（delegate-creation），本特性复用。
- **表达式树**：`handlers += h` 出现在 `Expression(Of T)` 不可表示（复合赋值不在表达式树子集内）——但 `handlers + h` 作为二元表达式理论上可表示 `Delegate.Combine` 调用？`Probably` 不开放：表达式树场景几乎不会合并委托，且 `Delegate.Combine` 返回 `Delegate` 需强转，表示成本高。v1 不碰表达式树。
- **晚期绑定（Option Strict Off + `Object` 变量）**：`handlers` 若声明为 `Object`，`+=` 走后期绑定路径。2014 纪要把这个留成了开放问题（"STILL TO DO: figure out ... how much should make it into the late-binder?"）。我们 v1 的保守答案是：**只对静态类型为具体委托类型的操作数生效**；`Object` 变量的 `+=` 维持当前行为（晚期绑定调用 `op_Addition` 失败 → 运行期 `MissingMemberException`，与今天一致），不新增 late-bound `+=`。原因：晚期绑定下无法静态保证两侧是 delegate，`+=` 的含义依赖运行期类型，破坏面不可控。

#### 5. Breaking change 与兼容性

**两半都是纯启用（additive）。** 今天 `handlers += AddressOf Button_Click` 在本编译器里是编译错误（`+` 未对 delegate 定义），`As <Function(...)>` 也是错误（类型文法无 `<` 起头）。给错误代码补上合法语义 = 纯启用，不改变任何已编译成功的代码。**这是本特性安全性的核心——不触发"隐蔽语义变化"（原则 #7），因为之前根本没有成功绑定。** 与递归 Lambda / TypeOf 收窄需要"启用型"防护不同，这里没有要保的既有绑定。

**残留风险点。** ① 宽松委托移除若编译器不保证适配器确定性，会把"应删除"变成"静默保留"（运行期行为差，非编译期破坏）；② 匿名委托类型的**结构相等**若定义不当，可能影响重载解析（两个签名相同的 `<Function(...)>` 是否视为同一类型、是否与 `Func` 相互转换）——这是破坏面最大的一处，但只存在于 Table 那半。`langversion` 门控：`+=` / `-=` 与 `<Function(...)>` 都应在新 langversion 下启用，旧版本维持报错。

#### 6. Option Strict / 编译选项分叉

**严格路径（On）**：`AddressOf` 必须 identity 匹配（relaxation level 36），无适配器合成，`-=` 移除**恒安全**。`+=` / `-=` 全部可用。这是主要路径。

**宽松路径（Off）**：`AddressOf` 可放宽（level 34 丢参/丢返回）。`+=` 可用；`-=` 依赖适配器确定性（见 Q&A），原型未证明前 v1 对宽松 `-=` 报错。且宽松路径下 lambda 推断已产生匿名委托类型（spec:103），显式 `<Function(...)>` 若落地，在 Off 下必须与隐式匿名委托"等价则同类型"保持一致——两条路径行为必须一致，否则会造成"On 下能赋、Off 下不能赋"的分叉。

**`Option Infer`**：与 `Dim x = Function(a, b) a + b` 的既有推断无冲突；`+=` / `-=` 不涉及推断。`Let`（依赖 `proposal-local-declarations.md`）只影响拼写，不影响语义。

#### 7. IDE / IntelliSense 影响

- **补全**：`handlers += ` 之后应列出匹配方法组（同 `AddHandler` 的补全体验）；`handlers -= ` 同理。
- **类型展示**：若匿名委托类型落地，InfoTip 显示 `<Function(Double, Double) As Double>` 而非编译器合成的 `VB$AnonymousDelegate_0` 之类名字。`Probably`：需要新的 `SymbolDisplay` 规则。
- **`GoTo Definition` / 引用计数**：`AddressOf Button_Click` 在 `+=` 内的导航与 `AddHandler` 一致；无新负担。
- **重构**：`Extract Method` 等对 `+=` 右侧方法组的处理沿用 `AddHandler` 路径。

#### 8. 数据 / 普遍性

- **`+=` / `-=`**：这是 C# 从 1.0 就有的惯用法、主线 2014 年 in-principle 批准的 parity 特性、"数十万安静客户"里靠 `[Delegate].Combine` 手写管理事件处理器列表的业务代码是实打实的样板来源。普遍性**高**，但缺量化数据（TODO）。
- **匿名委托类型**：`Func/Action` 已覆盖大部分"签名即类型"的场景（85% 是 `Probably` 的估判，无数据）；真正需要显式匿名委托类型的是 API 作者（要 ByRef、要自定义签名、要免具名样板）。面向业务应用的客户群对它的需求频次**明显低于** `+=` / `-=`。We Suspect 后半是"消除样板（原则 #9）"的尾巴，不是头条。

#### 9. 更简替代

- **`[Delegate].Combine` / `[Delegate].Remove`**：现状，冗长但确定——前半要消灭的就是它。
- **`Func` / `Action` 泛型**：覆盖签名子集；ByRef 与自定义返回类型表达不了；且 `Func(Of Double, Double, Double)` 的参数顺序（最后一个是返回）对新手不如 `<Function(Double, Double) As Double>` 直观——但"新手可读性"不足以抵消尖括号的代价。
- **具名 `Delegate Function`**：样板但零新语法——匿名委托类型要证明自己比"一条 `Delegate` 声明"值得一个新文法。
- **Analyzer / 重构**：可以提示"此处可把 `Delegate.Combine` 折叠为 `+=`"，但不能让 `+=` 编译——脚手架，不能替代语言特性。

#### 10. 成本 / 优先级

- **`+=` / `-=` 半：低成本。** 新增两个 operator kind（`DelegateCombine` / `DelegateRemove`，连同赋值变体）、绑定器按操作数类型选算子、去糖为 `Delegate.Combine` / `Delegate.Remove` 调用（`castclass` 到具体委托类型）。C# 已有全套实现可对照。这是"小语法、大收益、主线批准过"的教科书型增量。
- **匿名委托类型半：高成本。** 类型文法新产生式、tokenizer XML 冲突、合成类型的生命周期与元数据发射、结构相等规则、泛型/事件/返回类型作为第一类类型、IDE 展示。且语法是原创（无 C# parity、无 VB 惯例）。按"缩小范围、分阶段落地"的取向，必须拆出来单独评估。

#### 11. 运行时 / CLR 硬约束

**无新约束。** `Delegate.Combine` / `Delegate.Remove` 是 BCL 的既有关键方法，去糖后是普通调用，`castclass` 是既有的安全操作，PEVerify 无碍。匿名委托类型需要**发射合成委托类型**的元数据——但编译器为 lambda 推断已经在做这件事（spec:103），机制存在，只是显式化之后要保证签名相同的显式/隐式匿名委托可互操作（结构相等）。不触达 CLR 存储规则。

#### 12. 值不值得做

按价值 × 成本 × 风险逐条打分：

| 半特性 | 价值 | 成本 | 风险 | 判定 |
|--------|------|------|------|------|
| `+=` / `-=`（含二元） | 高（C# parity、主线 2014 批准、高频样板） | 低（operator kind + 去糖） | 低（纯启用） | **值得，Active** |
| `<Function(...)>` 匿名委托类型 | 中（API 作者为主，`Func/Action` 已覆盖多数） | 高（类型系统 + tokenizer + IDE） | 中高（XML 冲突、结构相等破坏面） | **不急于做，Table** |

**捆绑不值得；解绑后前半值得。** 若只允许整份推进而不许拆分，我们会建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：`+=` / `-=` 正中靶心——把多步 `[Delegate].Combine/Remove` 压成一行，这是它最亮的地方。
- **默认跟随 C#（原则 #4）**：前半是教科书级的"follow C# unless compelling reason"——2014 年主线亲自批准、语义 parity C#，理由充分且没有偏离理由。后半**没有** C# 对应（C# 无匿名委托类型语法），parity 论据为零，反而要论证"为什么离开 C#"。
- **不引入"第二种做事方式"（原则 #3）**：对事件，`AddHandler` 维持唯一入口（主线 2014 逐字决定）；对委托变量，今天没有自然的第一条路，`+=` 是补第一条而非加第二条。后半则确实给"签名类型"引入了第二种拼写（具名委托已存在）。
- **避免隐蔽语义变化（原则 #7）**：两半都是纯启用（今天是错误），不触发重绑定。唯一风险面是宽松委托移除的"静默不删"，靠"同一宽松规则产生同一适配器"的不变量对冲。
- **读起来像英语、对新手友好（原则 #5）**：`handlers += AddressOf Button_Click` 无注释自解释；`<Function(Double, Double) As Double>` 读起来也清楚，但**写**起来撞 XML——可读性救不了语法冲突。
- **不与既有语法冲突（原则 #8）**：**后半的直接失败项**。`<` 是 XML 字面量/轴的主权字符，类型文法引入 `<` 起头的新产生式风险高；前半零冲突。
- **与主线关系（对照表 2.3）**：`+=` / `-=` = **主线一致**（2014 in-principle 批准，parity C#，从未落地）；匿名委托类型 = **Anthony 独立延伸**（主线无对应）；`Null` 依赖 = **主线保守、Anthony 激进**（2014 拒绝，本组 Table）；`Let` = **Anthony 独立延伸**。

### RESOLUTION:

1. **解绑**。`+=` / `-=` 委托合并与显式匿名委托类型是两份建议，不再捆绑。建议文档按两半拆分重写。
2. **`+=` / `-=`（含二元 `+` / `-`）原则上采纳**，语义 parity C#，作用于**强类型 delegate 变量**；去糖为 `Delegate.Combine` / `Delegate.Remove`（`+` = Combine、`-` = Remove），复合赋值 = 二元 + 赋值。与 2014-02-17 #31 "Approved in principle, but need design work. Parity with C#." 一致。
3. **不新增 `event += value`**。事件保持 `AddHandler` / `RemoveHandler` 唯一入口；与 2014 决议逐字一致："We will not also allow "event += value" as a synonym for "AddHandler event, value"."
4. **`Null` / `Nothing` 边界由 BCL 语义承担**，语言层不另设规则：`Combine(Nothing, d) = d`、`Remove(d, Nothing) = d`、`Remove(Nothing, d) = Nothing`。`Null` 依赖本组 Table 的 `proposal-null-literal.md`，落地以 `Nothing` 为基线（`Dim handlers As EventHandler = Nothing` 在 vanilla 等价可编译）。
5. **宽松委托移除 = OPEN**。`-=` 必须与 `+=` 应用**同一宽松转换规则**并合成**同一适配器**，`Delegate.Remove` 才能命中；原型先验证该不变量。原型不能证明时，v1 退路：Option Strict Off 下对宽松 `-=` 报错，只允许 identity（level 36）移除。
6. **异构类型拒绝**：`+` / `-` / `+=` / `-=` 两侧必须是同一具体委托类型；基类类型（`Delegate` / `MulticastDelegate` / `Object`）变量与晚期绑定变量 v1 不开放。
7. **显式匿名委托类型 `<Function(...)>` = Table**。语法与 XML 冲突、无 C# parity、类型系统工作量高。待 `+=` / `-=` 落地后，以替代语法（PROPOSAL C 的 `Delegate(Of ...)` 或 `Function`-typed 关键字形式）重新评估；重新评估前不引入 `<` 起头的类型文法。
8. **晚绑定不扩展**：v1 仅静态类型为具体委托类型的操作数生效；`Object` 变量的 `+=` 维持现状（2014 "STILL TO DO: ... how much should make it into the late-binder?" 的保守答案）。

### Implication:

- 撰写最小原型：`+=` / `-=` + 二元 `+` / `-` 的绑定与去糖（新增 operator kind、`castclass` 到具体委托类型）；验证语义模型、`Null`/`Nothing` 边界、异构类型报错。
- 原型第二项：Option Strict Off 下宽松 `AddressOf` 的 `+=` / `-=` 适配器确定性——这决定 RESOLUTION 第 5 条走哪条路。
- 起草 speclet：运算符表新增 delegate 行、去糖规则、`langversion` 门控、角案例表（重复移除/移除最后出现、`Nothing` 边界、异构类型、基类类型）。
- 单元测试（无副作用约束：不启动进程、不写文件、无网络——纯编译期/emit 断言）：`Delegate.Combine`/`Remove` 的 emit 正确性、宽松移除、`langversion` 门控。
- 匿名委托类型工作项单列，等 `+=` / `-=` 落地后以替代语法重启。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：宽松委托 `-=` 的适配器确定性是否成立——`+= AddressOf NoParamMethod`（Option Strict Off，合成适配器）后 `-= AddressOf NoParamMethod` 能否命中？这是整份建议唯一悬着的运行时语义问题。
- `OPEN QUESTIONS`（Table 半，重启时定）：`<Function(...)>` / 替代语法的**结构相等**规则——签名相同的两个显式匿名委托是否同一类型？与隐式匿名委托（lambda 推断）是否互操作？与 `Func/Action` 是否互相转换？作泛型实参/事件类型/返回类型的行为？
- `OPEN QUESTIONS`：`+=` 右侧 lambda 的目标类型化细节——`handlers += Function(s, e) ...` 的参数类型必须从 `handlers` 类型推导（`EventHandler` 的 `(Object, EventArgs)`），推导失败时的报错文案。
- `TODO`：量化委托变量管理代码（`[Delegate].Combine/Remove`）在真实代码库中的占比，为普遍性补数据。
- `Follow-up`：与 `proposal-wildcard-lambdas.md`、`proposal-recursive-lambda-inference.md` 对表——Lambda 目标类型化与委托类型的共享实现面。

### 状态

- **LDM 状态：Consider（解绑后）**；`+=` / `-=` 半为 Active（in-principle），匿名委托类型半为 Table。
- **三态判定：Consider** — 前半价值真实、成本低、主线批准过，值得立刻推原型；后半语法冲突、无 parity、成本高，挂起等替代语法。VBScript.NET 优先级：`+=` / `-=` > 匿名委托类型；事件的 `AddHandler` / `RemoveHandler` 入口不动。

---

## 附录：特性评价

# 建议评价报告：proposal-delegate-enhancements.md

## 评价对象

- 建议：proposal-delegate-enhancements.md — 委托增强与匿名委托类型（`+=` / `-=` 委托合并 + `<Function(Double, Double) As Double>` 显式匿名委托类型）
- 来源：Anthony 原文第 8 章 "General Modernization and Evolution II (Declarations)"（`..\AnthonyDesign_wordpress.txt` L1509–1517；`+=`/`-=`、匿名委托类型、"Not shown: Relaxed delegates can be removed." 全部出自该处）
- 配方目标：① 强类型 `Delegate.Combine/Remove` 经 `+=` / `-=` 表达；② 提供匿名委托签名的显式类型语法

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。`+=`/`-=` 的 Motivation 清晰、示例可操作（Anthony 原文逐字）；但匿名委托类型半无文法、无边界定义，四个未决问题全部落在它上面，核心语法未定型；"宽松委托移除"显式 Not shown——关键子效果缺失；无原型 | 已检查 | 无原型封顶 3；捆绑使两半的成熟度差被掩盖；宽松移除未展示 = 效果悬空 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`+=`/`-=` 半：C# parity + 主线 2014 批准，VB 化良好（仅委托变量、事件不动）；`<Function(...)>` 半：尖括号原创语法、与 XML 主权字符冲突、无 C#/VB 惯例，外来味重；两半捆绑 = 打包了成熟度不同的两个能力 | 已检查 | 前半满分、后半低分被捆绑抹平；`<Function(...)>` 违反原则 #8 未自行识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、4 个未决问题具体诚实（列全不藏）；但核心边界含糊——宽松移除"Not shown"、匿名委托类型无文法/相等规则/互操作规则、无 breaking-change 分析、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；两半风险不分层 | 已检查 | 无 BNF、无兼容性章节；"Not shown"留白未给设计方向；占位链接（红旗 4.2） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（消除样板、迭代提速）与光（C# parity、对齐主流直觉）受益；风（两半成熟度不同、`Null` 依赖 Table 的特性）与暗（`<Function(...)>` 语法冲突、结构相等破坏面）受损；文档未权衡 | 已检查（预测待定） | 暗风险（XML tokenizer、结构相等重载解析）未被文档识别；`Null` 依赖未标注为本组 Table |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料 = Anthony 第 8 章（清晰，L1509–1517）；`+=`/`-=` 借鉴 C# 语义（由"与 C# parity"暗示，未显式标注）；未引用主线 2014-02-17 的 in-principle 批准（重要遗漏——前半本可借主线权威）；`Null`/`Let` 依赖未标注；继承 VB6/VBScript 遗产（`AddHandler`/`GetRef` 时代）未点明 | 已检查 | 未标注 2014 主线批准、`Null`/`Let` 依赖；借鉴 C# 未展开 |

## 设计原则对照

- **与 VB 基因：前半一致、后半偏离。** `+=`/`-=`：消除样板（#9）、跟随 C#（#4）、不新增事件入口（#3 局部）、零语法冲突（#8）；`<Function(...)>`：与既有语法冲突（#8 直接失败）、无 C# parity（#4 无论据）、给签名类型引入第二种拼写（#3）。捆绑评分取中值 3。
- **与主线关系：前半主线一致，后半 Anthony 独立延伸。** `+=`/`-=` 是 2014-02-17 #31 的 in-principle 批准复活（C# parity、不碰事件），从未落地；匿名委托类型主线无对应；`Null` 依赖与主线保守立场冲突（2014 拒绝、本组 Table）；`Let` 为 Anthony 独立延伸。与 `proposal-null-literal.md`（Table）、`proposal-local-declarations.md`（`Let`）、`proposal-recursive-lambda-inference.md`（Lambda 目标类型化）同族。
- **破坏性变更：无（两半均为纯启用）。** 今天 `+=`/`-=` 与 `As <Function(...)>` 在本编译器都是编译错误，给错误代码补合法语义不改变任何已编译成功代码。唯一运行期语义风险面是宽松委托移除的"静默不删"（需原型验证适配器确定性），与匿名委托类型结构相等对重载解析的潜在影响（Table 半）。

## 总评

- **达成程度：部分达成**——`+=`/`-=` 半概念与价值完全成立、有主线权威背书、纯启用零破坏；匿名委托类型半语法未定、边界含糊、风险未识别；捆绑是不诚实边界。
- **LDM 三态建议：Consider（解绑后）**——`+=`/`-=`（含二元 `+`/`-`）为 **Active**，推进原型与 speclet；`<Function(...)>` 显式匿名委托类型为 **Table**，待替代语法（`Delegate(Of ...)` 等）重启。
- **主要问题**：① 两半成熟度不同却捆绑，风险不分层；② 宽松委托移除 "Not shown" 无设计方向，是唯一悬着的运行时语义问题；③ `<Function(...)>` 尖括号与 XML 主权字符冲突，tokenizer 风险未评估；④ 匿名委托类型的结构相等/互操作规则缺失；⑤ 未引用主线 2014-02-17 的 in-principle 批准（前半最强论据）；⑥ `Null` 依赖本组 Table 的特性，落地应以 `Nothing` 为基线。

## 返工建议

- **补充章节**：按两半拆分（Summary / Motivation / Detailed design 各自独立）；Compatibility / breaking-change（明确"纯启用"论证、`langversion` 门控）；Spec 补 BNF（`+=`/`-=` 的运算符表新增 delegate 行；`<Function(...)>` 若保留须给类型文法产生式与 tokenizer 判定流程）。
- **补充证据**：最小原型（`+=`/`-=` 绑定与去糖、emit 断言 `Delegate.Combine/Remove`）；原型第二项——Option Strict Off 宽松 `AddressOf` 的 `+=`/`-=` 适配器确定性（决定宽松移除走"可行"还是"报错"）；量化委托变量管理代码占比。
- **未决问题处理**：`Null` 边界按 BCL 语义关闭；异构/基类类型 v1 报错；宽松移除绑定到适配器确定性原型结果；`<Function(...)>` 的结构相等、泛型/事件/返回类型行为、参数命名/`Optional`/`ByRef` 规则移交 Table 工作项，重启时以替代语法一并定。
- **设计探索**：`Delegate(Of Func(Of Double, Double) As Double)`（PROPOSAL C）作为匿名委托类型的替代拼写；`Func/Action` 覆盖率的抽样（"85%"待量化）；与 `proposal-wildcard-lambdas.md` 的 Lambda 目标类型化共享实现面。

---

## 附录：C# 生态与互操作考量

_本附录依据 `..\..\csharplang-index.md`（C# interop 浓缩索引，主题 T2/T3/M7 相关）建立世界观，并对 `..\..\csharplang` 镜像中与委托/lambda/函数指针直接相关的原文逐字核对。所有 `→` 引用均可溯源到镜像仓库的具体文件；未核实项标注 **Suspect** / **OPEN QUESTIONS**。_

### 相关 C# 现实方向

本提案主题（委托合并 + 委托签名类型）在 C#/CLR/.NET 生态中的对应走向有五条：前两条直接对应本提案两半，后三条决定"委托的形态"在 C# 中往哪走。

**1. 委托 `+=` / `-=`：C# 1.0 基线，永续有效但不在前沿。** C# 自 1.0 起 `+` / `-` / `+=` / `-=` 就作用于委托类型（predefined delegate operators），本提案前半的 parity 源头即在此；vblang 主线 2014-02-17 的 "Parity with C#." 指的也是这条基线。C# 不会移除它（语言兼容性承诺），但 C# 的前沿投入早已不在"多播委托列表"上——`Delegate.Combine` / `Delegate.Remove` 是运行时既有原语，语言侧没有围绕它的新 work item。含义：`+=` / `-=` 是**追平 C# 历史基线**，不是**跟随 C# 现代方向**。

**2. C# 10 lambda 自然类型：C# 对"签名即类型"的回答。** `proposals\csharp-10.0\lambda-improvements.md` 引入 _function_type_（natural function type）。已逐字核对的原文：
- "An _anonymous function_ expression … has a natural type if the parameters types are explicit and the return type is either explicit or can be inferred" → `proposals\csharp-10.0\lambda-improvements.md`
- "A _function_type_ exists at compile time only: _function_types_ do not appear in source or metadata." → 同上。**C# 刻意不让函数签名成为可在源码里命名的类型。**
- "Requiring explicit delegate types for lambdas and method groups has been a friction point for customers" → 同上（Motivation）——与本提案"消除样板"（原则 #9）的动机同源。
- 映射规则："if `R` is `void`, then the delegate type is `System.Action<P1, …, Pn>`; otherwise the delegate type is `System.Func<P1, …, Pn, R>`"；签名不满足（>16 参、含 ref/out、非合法泛型实参）时 "the delegate is a synthesized `internal` anonymous delegate type" → 同上。**注意 C# 10 的合成委托类型是 `internal`，跨程序集不可命名。**
- 结构相等先例："If two anonymous functions or method groups in the same compilation require synthesized delegate types with the same parameter types and modifiers and the same return type and modifiers, the compiler will use the same synthesized delegate type." → 同上。

**3. C# 9 函数指针 `delegate*`：显式不安全通道。** `proposals\csharp-9.0\function-pointers.md`：
- 动机原文："This proposal provides language constructs that expose IL opcodes that cannot currently be accessed efficiently, or at all, in C# today: `ldftn` and `calli`." → `proposals\csharp-9.0\function-pointers.md`（Summary）。
- `delegate*` 是指针类型：仅 unsafe 上下文、不可转 `object`、不可作泛型实参；支持调用约定（managed / unmanaged[Cdecl] / unmanaged[Stdcall, SuppressGCTransition] 等），与 `[UnmanagedCallersOnly]` 配合（索引 T3）。
- 命名被拒原文："After discussion we decided to not allow named declaration of `delegate*` types." → 同上（Considerations）。

**4. C# 13 ref struct 接口 + ref struct closures：委托的"未来形态"。** `proposals\csharp-13.0\ref-struct-interfaces.md` 动机原文："The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET." → 该文件。候选提案 `proposals\ref-struct-closures.md` 让 lambda 转 `allows ref struct` 的 `IFunc/IAction` 泛型约束、闭包为 ref struct（零分配、可内联）。动机原文："One of the biggest problems with the existing LINQ API is that the delegates (`Func<T>` et. al) and the backing synthesized closures are hard to inline, and thus hard to optimize, for the .NET Runtime." → `proposals\ref-struct-closures.md`。含义：C# 的委托/lambda 演进前沿是"单调用、可内联的函数接口"，与多播委托列表（`Delegate.Combine` 链）**正交**——后者永远可用，但 C# 不再在其上投资。

**5. C# 2026 LDM 仍在扩展 lambda 机器。** `meetings\2026\LDM-2026-04-15.md`（deconstruction in lambda parameters）："When a deconstructed lambda has a natural type, that natural type participates in overload resolution in the usual way." → 该文件；结论 "We want to proceed with the general feature for lambdas." 表明 C# 在 C# 10 natural type 基础上继续扩展 lambda 能力。

### 现实 vs 提案

| 本提案要点 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| `+=` / `-=`（含二元 `+` / `-`）委托合并 | C# 1.0 基线委托运算符，永续有效 | **兼容** | 直接 parity；C# 已存在 25 年、无冲突；注意"C# 前沿不在多播委托" |
| 异构 / 基类类型 v1 拒绝 | C# 要求 `+` 两侧同 delegate 类型；`System.Delegate` 基类变量上无 `+` 运算符 | **兼容** | C# 同一规则，v1 决策与 C# 对齐 |
| 事件 `AddHandler` 唯一入口 | C# 用 `event +=` | **兼容** | VB 保持差异；C# 无对应压力（2014 决议不变） |
| 宽松委托 `-=` 适配器确定性 | C# 无宽松委托（方法组→委托为精确/变体转换，不合成丢参/丢返回适配器） | **无对应（VB 特有）** | 本不变量只能自证，C# 无借鉴；RESOLUTION #5 维持 OPEN |
| `<Function(...)>` 显式匿名委托类型 | C# 10 natural function type **仅编译期、不落源码/元数据**；named `delegate*` 被拒 | **脱节 + 需桥接** | C# 刻意不提供可命名函数类型；VB 若做是在 C# 设计抉择的另一侧，须定义结构相等并与 C# 合成委托类型 / `Func-Action` 对齐 |
| 函数指针互操作（本提案未含） | C# 9 `delegate*` / `ldftn` / `calli` | **需桥接** | VB 无 unsafe/指针；但 C# 程序集元数据会出现 fnptr 签名，VB 编译器 import 时须处理 |
| ref struct closures / `IFunc`（未来） | C# 13 `allows ref struct` + 候选 ref-struct-closures | **需桥接（未来）** | VB 的 ref struct 支持目前是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error；脚本若消费 NLinq 式 API 须识别 function-interface 约束 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**。`+=` / `-=` 主路径走 Option Strict On 的 identity 匹配（relaxation level 36），与 C# 严格语义对齐；宽松路径是 VB 特有，原型证明适配器确定性前保守（RESOLUTION #5）。晚绑定不扩展（RESOLUTION #8）与 C# 弱化 `dynamic` 同向（索引 T7）。
2. **source-gen 桥 / AOT 友好**。`+=` / `-=` 去糖为 `Delegate.Combine` / `Delegate.Remove` 是纯静态调用，无反射负担，天然兼容 AOT/trimming（索引 T5/T6）。脚本解释模式保留为显式 opt-in 的传统兼容层，默认"编译到受管程序集"（决策文件 M5）。
3. **识别新元数据**（本提案落地与消费 C# 13 生态的共同前提）：
   - C# 13 `allows ref struct` / function-interface 约束（`IFunc` / `IAction`，连同 `[CompilerFeatureRequired]` 标志）：VB 编译器需解析这些约束元数据，才能绑定 C# 13 泛型 API（决策文件 M4/M7）。
   - C# 9 fnptr 类型签名：import C# 程序集时须处理 `delegate*`（ECMA-335 中函数指针是独立签名类别，非普通 TypeSpec）；VB 无 unsafe 无法直接调用，最低要求是不崩、报可理解错误，理想是提供 delegate 桥（索引 T3/M1）。
   - `RefSafetyRules` 模块属性（C# 11）：跨语言 ref 安全边界只对 C# 模块生效，VB 侧须明示 ByRef 逃逸语义（决策文件 M3）。
4. **匿名委托类型若重启（Table 半）**：结构相等规则仿 C# 10"同签名→同合成类型"先例（上节第 2 条引用）；映射策略对齐（≤16 参且无 ByRef → `Func/Action`，否则合成委托类型）；注意 C# 10 合成类型是 `internal`、跨程序集不可命名——VB 显式可命名版本须自定跨程序集约定（public 发射或文档化规则），否则结构相等在程序集边界失效。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #2（`+=` / `-=` Active）不受影响，维持。** 补充口径：parity 论据是"C# 1.0 基线"，即追平历史，不是跟随 C# 现代方向——不影响采纳，但宣传时应避免暗示"与 C# 前沿同步"。
- **RESOLUTION #5（宽松移除 OPEN）维持**：C# 无宽松委托，该 OPEN 不会因 C# 生态演进而关闭，只能靠 VB 原型自证。
- **RESOLUTION #7（`<Function(...)>` Table）维持，且获新证据**：C# 10 natural function type 仅编译期 + named `delegate*` 被拒，说明 C# 刻意回避可命名函数类型；这支持"不急于做、换替代语法"的判定。重启时的互操作要求已在"适应建议"第 4 条列出。
- **RESOLUTION #8（晚绑定不扩展）**：与 C# 弱化 dynamic / AOT 压力同向，维持。
- **三态判定维持**：Consider（解绑后）不变；前半 Active、后半 Table 的分层与 C# 生态现实一致。

### 引用纪律与未决项

已核实并可逐字引用的 C# 原文（全部在 `..\..\csharplang` 镜像中核对）：
- "A _function_type_ exists at compile time only: _function_types_ do not appear in source or metadata." → `proposals\csharp-10.0\lambda-improvements.md`
- "if `R` is `void`, then the delegate type is `System.Action<P1, …, Pn>`; otherwise the delegate type is `System.Func<P1, …, Pn, R>`." → `proposals\csharp-10.0\lambda-improvements.md`
- "If two anonymous functions or method groups in the same compilation require synthesized delegate types with the same parameter types and modifiers and the same return type and modifiers, the compiler will use the same synthesized delegate type." → `proposals\csharp-10.0\lambda-improvements.md`
- "This proposal provides language constructs that expose IL opcodes that cannot currently be accessed efficiently, or at all, in C# today: `ldftn` and `calli`." → `proposals\csharp-9.0\function-pointers.md`（Summary）
- "After discussion we decided to not allow named declaration of `delegate*` types." → `proposals\csharp-9.0\function-pointers.md`（Considerations）
- "The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET." → `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）
- "One of the biggest problems with the existing LINQ API is that the delegates (`Func<T>` et. al) and the backing synthesized closures are hard to inline, and thus hard to optimize, for the .NET Runtime." → `proposals\ref-struct-closures.md`（Motivation）
- "When a deconstructed lambda has a natural type, that natural type participates in overload resolution in the usual way." → `meetings\2026\LDM-2026-04-15.md`

**OPEN QUESTIONS**（未核实/未深挖）：
- C# 拒绝 named function pointer 的具体 LDM 决议会议出处（`function-pointers.md` 的 Considerations 有逐字句，但当时的会议决议记录未查）。
- `ref-struct-closures.md` 的最终状态：仍是候选提案（未归入任何 `csharp-X.0`），是否进入 C# 15/16 未定；若未落地，"委托未来形态"的判断需随 LDM 更新。
- VB 编译器（Roslyn VB）当前对 fnptr 元数据签名的具体 import 行为未在本仓库核实（**Suspect**：可能作为不支持类型 / `Object` 处理）。
