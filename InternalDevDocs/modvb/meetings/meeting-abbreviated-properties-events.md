# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们把一份把**五个互不相关的语法压缩点**捆成一体的建议拆开逐条过：计算只读属性简写、访问器 `End` 语句省略、`Set(value)` 形参类型推断、自定义事件访问器简写、预处理条件特性。建议原文自己就留下了关键的问号（"`Return` or `Get`?"），讨论中我们反复回访"哪种简写真的属于 VB"。

## Agenda

* [Proposal: Abbreviated Properties & Events（简写属性与事件）](#proposal-abbreviated-properties--events简写属性与事件)

## Proposal: Abbreviated Properties & Events

_Related: [vblang #197 – Inferred `Set` Parameter Type](https://github.com/dotnet/vblang/issues/197)（2017-11-15 会议：Approved-in-Principle）；[vblang #196 – Implicit Property Backing Fields](https://github.com/dotnet/vblang/issues/196)（Rejected）；[vblang #195 – `Static` Property Variables](https://github.com/dotnet/vblang/issues/195)（Rejected）；[vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；ModVB：`proposal-minor-fixes.md`（访问器 value 参数类型推断、`Custom` 关键字省略）、`proposal-semantic-preprocessing.md`（`##If`）_

### 场景与缺口

We started from an observation that every VB author lives with daily: 一个"算一个表达式"的计算只读属性，今天要写五层嵌套。绝大多数业务代码里的计算属性只是"算个结果"，却被迫长这样：

```vb
' 今天：5 行，其中 3 行是块书签。
ReadOnly Property DiscountedPrice As Decimal
    Get
        Return Price - (Price * DiscountRate)
    End Get
End Property
```

We see a clear DX parity gap here。C# 从 C# 6 就有 expression-bodied 成员（`public decimal DiscountedPrice => Price - Price * DiscountRate;`），而 VB 主线从未讨论过对应物；对"读起来像英语、低仪式感"的业务用户来说，这是最日常的一块样板。访问器短体（一两行）还要被迫写 `End Get`/`End Set`，同理。第三个场景来自跨平台共享代码：`NetFX` 与其它平台希望同一类型挂不同的特性（`<Serializable>` vs `<DataContract>`），今天要么手工 `#If` 包整个类声明（重复类体），要么接受运行时反射判断——都笨重。

但正如我们拆开之后发现的，这份建议的五个部分成熟度参差：有的是真实缺口、有的是**主线已经批过的东西**、还有一部分 `Probably` 今天就已经能写。

### 候选方案

**PROPOSAL A — 计算只读属性：`Return` 表达式体。** `Return` 后面的表达式即整个 `Get` 访问器，`End Get` 省略：

```vb
' 原文形态：2 行（含省略 End Property）。我们下文会讨论 End Property 去留。
ReadOnly Property DiscountedPrice As Decimal
    Return Price - (Price * DiscountRate)
```

**PROPOSAL B — 计算只读属性：`Get` 后直接跟表达式。** 原文以注释 "`Return` or `Get`?" 标记分歧，尚未定案：

```vb
ReadOnly Property DiscountedPrice As Decimal
    Get Price - (Price * DiscountRate)
```

**PROPOSAL C — 访问器 `End` 语句普遍省略。** `Get`/`Set` 块结束由"下一个访问器关键字或 `End Property`"界定；`Set` 写作 `Set(value)`，省略形参类型：

```vb
Property Age As Integer
    Get
        Return _Age

    Set(value)
        If value < 0 Then Throw

        _Age = value
End Property
```

**PROPOSAL D — 自定义事件访问器简写。** `AddHandler`/`RemoveHandler`/`RaiseEvent` 同样省略 `End`，`RaiseEvent` 以空条件调用触发后备委托：

```vb
Event AgeChanged As EventHandler
    AddHandler(value)
        _AgeChanged += value

    RemoveHandler(value)
        _AgeChanged -= value

    RaiseEvent(sender, e)
        _AgeChanged?(sender, e)

End Event
```

**PROPOSAL E — 预处理条件特性。** 特性列表可被 `#If ... #Else ... #End If` 包裹：

```vb
#If PLATFORM = "NetFX" Then
    <Serializable>
#Else
    <DataContract>
#End If
Class Message
    ...
End Class
```

### 权衡：Q&A

- **A vs B：`Return` 还是 `Get`？** 我们一致选 `Return`。理由有三。其一，`Return` 复用既有语句语义——新形态不是"表达式访问器"这个新文法类别，而是"`Get` 访问器体恰好是一条 `Return`"，解析器只是省略了一个多余的 `End Get`；`Get` 裸表达式则把 `Get` 变成上下文敏感的表达式引入符，新增一个文法类别。其二，`Return Price - ...` 读起来是"这个属性算这个值"，是陈述句；`Get Price - ...` 读起来像祈使句，且 `Get` 后跟表达式在现有 VB 里没有前例，`Get` 作为块关键字和表达式前缀的身份会打架（设计原则 #8）。其三，Anthony 自己在原文就标了问号——既然语法未定型，我们作为评审必须定下来：**`Return`**。作为附带好处，`Return` 形态将来若想推广到 expression-bodied 方法（`Function` 体恰为一条 `Return`），文法扩展是同一套；但我们明确说**现在不做**。

- **A 的 "2 行" 表述要修正：`End Property` 必须保留。** 原文计"2 行"，即连 `End Property` 一并省去。我们认为不该省。省掉 `End Get` 之后，"下一个访问器或 `End Property`"是清晰的边界；若连 `End Property` 也省，"下一个成员关键字"作边界就把属性结束信号完全交给成员头关键字（`Public`/`Function`/`Private`…），对代码折叠、IDE 结构化导航、以及"属性块"这个概念本身都是损失。**结论：`Return` 体省 `End Get`，留 `End Property`，实际是 3 行**。3 行对 5 行仍然是真实的胜利。

- **C：省略 `End Get`/`End Set` 能推广到"任意语句体"吗？** 不能，这是整场最尖锐的讨论。VB 的块结构是"关键字成对"的：`If`/`End If`、`Sub`/`End Sub`，行结构与关键字书签是解析的全部依据（没有 C# 的大括号自界定）。把访问器边界改成"下一个访问器关键字"，等于在 VB 里引入一种前所未有的"缩进/邻居界定"块。两个具体危险：
  - `Set(value)` 的语句体里，一条以 `RaiseEvent`/`AddHandler`/`RemoveHandler` 开头的语句会与访问器声明冲突（见深度追问 #1）；
  - 读者必须数缩进才能确认 `Set` 体在哪里结束——这违背我们"显式关键字在有助理解时才保留"（原则 #10）与"不引入第二种做事方式"（原则 #3）。
  **结论：只对"体恰好是一条 `Return`"的 `Get` 访问器放开省略 `End Get`**；任何含多条语句的访问器体**必须保留 `End` 书签**。这条规则收得很窄，是"优化"而非"新块结构"。

- **C 的样例本身还有两个洞。** 其一，`Set(value)` 省略形参类型今天**并不合法**——spec `PropertySetDeclaration` 要求参数类型必须等于属性类型，形参省略时隐式声明名为 `Value` 的参数；`Set(value)` 带形参但无类型，在 Option Strict On 下是错误。这正是 vblang **#197 Inferred `Set` Parameter Type** 的领地，主线在 2017-11-15 已 **Approved-in-Principle**，讨论里还说"技术上 `Set` 的整个参数列表已是可选的……因为会创建一个名为 `Value` 的隐式参数"。所以这段应该**并入 #197 的实现**，而不是在这份建议里重新设计。其二，`If value < 0 Then Throw` 里的裸 `Throw` **不能编译**——裸 `Throw`（重抛）只在 `Catch` 块内合法，`Set` 访问器体外没有 `Catch`，正确写法是 `Throw New ArgumentOutOfRangeException(...)`。We `Suspect` 原文此处是笔误，但这意味着**旗舰示例整体不能编译**，这是品质上的硬伤。

- **D：自定义事件简写值得单独做吗？** 价值远低于 A。自定义事件在业务代码里本来就少（多数人用普通事件），"简化 AddHandler/RemoveHandler/RaiseEvent 样板"只在少数场景（日志、Freezable、自定义触发逻辑）有受众。我们喜欢其中一件事：`_AgeChanged?(sender, e)` 用空条件调用取代 `Dim TempHandlers = Handlers / If TempHandlers IsNot Nothing Then ...` 的经典样板——这正是 VB 14 空条件操作符（`?(`）存在的意义。但事件访问器的 `End` 书签省略我们不做（理由同 C，且事件体里出现 `RaiseEvent`/`AddHandler` 语句的歧义更真实）。另外，原文把 `Event` 前的 `Custom` 省去了——那是 `proposal-minor-fixes.md` 的特性（`Custom` 关键字可省略），spec 明确"自定义事件必须以 `Custom` 开头"，所以 D 依赖 minor-fixes，不该自己捆一份。

- **E：条件特性是新特性吗？`Probably` 不是。** 预处理指令工作在**逻辑行**层面（spec `preprocessing-directives.md`："conditional compilation determines which source is processed by the syntactic grammar"，包裹"sequences of logical lines"）。`#If` 包裹 `<Serializable>` 那一行、`#Else` 换成 `<DataContract>`、`#End If` 后跟 `Class Message`——预处理后 token 流就是 `<Serializable> Class Message`，与普通特性前置声明完全一致。**`Probably` 这在今天的 VB 里已经能编译**；我们的 `Suspect` 是 Roslyn 可能存在某些实现层限制（比如特性行与声明之间不得夹指令的检查），需要一条编译测试去证伪。如果确实已可用，E 就从"语言特性"降级为"文档 + 测试 + IDE 折叠"，不是语言变更。原文列的 `Conditional` 特性替代方案对本题**不适用**：`ConditionalAttribute` 只能放在方法/特性上、只抑制调用点的调用，不能在一对类级特性之间做选择——所以 `#If` 恰恰是对的（且已存在的）工具，E 的问题只是没人把它写清楚。

- **E 与 `##If` 的边界。** ModVB 的 `proposal-semantic-preprocessing.md`（`##If TYPE_EXISTS` 等）是**语义级**条件编译。我们的划分：`#If`（符号级，本项目已支持）负责"平台常量选择特性"这类**符号已知**的选择；`##If`（语义级）负责"这个 API 在目标框架是否存在"这类**符号未知、需查元数据**的选择。两者不重叠，也不该合并。E 若只是"符号选特性"，就是现有能力；若 Anthony 想要的是"按类型存在性选特性"，那属于 `##If` 建议，不在这份里。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Return` 体本身**无歧义**：`Property X As Decimal` 后直接跟 `Return` 语句，今天在此处是语法错误，新形态占据的是当前非法的语法位置，因此对既有代码零影响（下文 breaking change 处再展开）。真正的歧义在 C（`Get`/`Set` 语句体省略 `End`）里：事件访问器体中若出现一条以 `RaiseEvent` 开头的语句，与 `RaiseEvent` 访问器声明无法可靠区分：

```vb
' 若允许"任意单语句体省略 End"——这条 RaiseEvent 是语句还是访问器声明？
Custom Event E As EventHandler
    AddHandler(value As EventHandler)
        RaiseEvent Log()        ' 语句（触发事件 Log）？还是访问器声明（形参 Log）？
        _e += value
    End AddHandler
    ...
End Event
```

`RaiseEvent Log()` 按语句读是"触发 Log 事件"，按声明读是"`RaiseEvent` 访问器，形参 `Log`（无类型）"。两者在省略 `End` 的规则下都无法靠后续 token 区分。这是把 C 收紧为"仅 `Return` 体"的决定性理由——`Return` 永远不会是访问器起始关键字。

`Set(value)` 的形参推断（#197）也需要文法改动：`PropertySetDeclaration` 的参数类型可省略时从属性类型推断。这条在主线已批过（Approved-in-Principle），文法改动有据可依。

#### 2. 角案例与边界语义

**`End Property` 保留时的边界。** `Return` 体之后只能是另一访问器（`Get`/`Set`）或 `End Property`。若 `Return` 表达式用了显式续行符（`_`），下一行仍是表达式的一部分，访问器关键字必须出现在续行之外——这条与普通块无异，解析器只需在"下一个逻辑行以 `Get`/`Set`/`End Property` 开头"时终止 `Return` 体。`Probably` 无新坑，但要在 spec 里写明。

**`WriteOnly` 与 `Set`。** 只有 `Get` 能是 `Return` 体（`Return` 是取值），`WriteOnly` 属性不受 A 影响；若 #197 落地，`Set(value)` 单语句体（如 `_age = value`）仍**必须**写 `End Set`，与 C 的收紧一致。

**`Default`（索引）属性。** `Default Public ReadOnly Property Item(index As Integer) As String` + `Return _items(index)` 是 `Return` 体的真实高频用例，予以确认。

**`Iterator` 属性。** 迭代器 `Get` 体含 `Yield`，不可能是一条 `Return`，天然排除在 A 之外，无需特判。`Async` 属性不存在（属性访问器不可 `Async`），`MustOverride`/接口属性无体，均不受影响。

**`Overridable`/`Overrides`/`Shared`。** 有体的可重写属性用 `Return` 体没问题；重写方的 `Return` 体与基类 `Get` 块混用（一处简写一处不简写）在元数据层面完全相同，纯语法差异，`Overrides` 不要求两处形态一致。

**空体与 `End` 省略。** `Get` 体恰为一条 `Return` 是唯一豁免；空访问器体（没有语句的 `Get`/`Set`）仍照旧写 `End Get`/`End Set`——空块本身就是可疑代码，不值得为其新造规则。

#### 3. 作用域与绑定

`Set(value)` 推断后，`value` 绑定到隐式声明的参数（spec 保留名 `Value`，VB 大小写不敏感，所以 `value` 与 `Value` 同名——这正是 #197 讨论里"创建一个名为 `Value` 的隐式参数"所指）。`Return` 体内 `Price`、`DiscountRate` 照常绑定到其它成员；语义模型里该属性仍有一个含单条 `Return` 语句的 `Get` 访问器，`GetTypeInfo` 返回属性类型。IDE 签名帮助应对 `Set(value)` 显示推断出的类型（"value As Integer"），而不是留白。`_AgeChanged?(sender, e)` 中 `_AgeChanged` 绑定到后备委托字段，空条件调用对接收者求值一次，等价于经典"快照到临时局部再判空"的样板（`Dim TempHandlers = Handlers / If TempHandlers IsNot Nothing Then ...`）。

#### 4. 与既有特性的交互

**自动属性（auto-property）**：`Property X As Integer = 5` 已有初始化器（spec `AutoPropertyMemberDeclaration`），是"字段式"简写；`Return` 体是"计算式"简写，两者互补、不重叠，但建议应说明这一点——否则读者会问"为什么不直接 `= 5`"（答案：`DiscountedPrice` 是**从其它成员算出来的**，不是字段初始化）。

**访问器 `value` 参数推断与 CallerInfo**：`Return` 体在 `Get` 内求值，`CallerMemberName`/`CallerFilePath` 等照常工作。`Set(value)` 推断不改变这些。

**ByRef / copy-in-copy-out**：属性访问器参数列表与索引参数不可 `ByRef`（spec），`Return` 体不引入任何 lvalue 语义，无交互。

**Late binding**：见 #6 Option Strict 分叉——这是本建议最隐蔽的交互点。

**`RaiseEvent` 语句 / `AddHandler`/`RemoveHandler` 语句**：与访问器声明同形（见 #1），是 D 收紧的原因。

#### 5. Breaking change 与兼容性

`Return` 体是纯增量（占当前非法语法位），对既有代码零影响。`End` 省略同理——既有代码都写着 `End Get`。真正需要审视的是 `Set(value)` 推断：

- **Option Strict On**：`Set(value)` 无类型今天即编译错误 → 推断使其合法 = 纯增量。
- **Option Strict Off**：`Set(value)` 无类型今天是什么？参数无 `As` 子句时按宽松规则成为 `Object`（若 spec 的"参数类型必须等于属性类型"没有强制到宽松路径——这条我们 `Suspect`，需要实测）。**若宽松路径下它今天是 `Object`，推断成属性类型就是破坏性变更**：`Set` 体内原本晚期绑定的调用（`value.SomeMethod()`）会变成早期绑定，改变异常时机与重载选择。**规则：`Set(value)` 推断只在 Option Strict On 下生效；Option Strict Off 保持既有行为（`Object`）**。这与 #197 的既定方向一致，但建议原文完全没提这个分叉，必须补上。这是"隐蔽语义变化"（原则 #7）的一个真实案例。

`#If` 条件特性若已可用，同样零破坏（预处理是词法层面的既有能力）。

#### 6. Option Strict / 编译选项分叉

如上：`Set(value)` 推断在严格/宽松两条路径行为**不一致**是刻意为之——严格路径下它从不合法变为合法（纯增量），宽松路径下保持 `Object`（不改变既有晚期绑定）。两条路径对 `Return` 体、`End` 省略、`#If` 特性的行为完全一致（`Return` 表达式类型仍需匹配属性类型，严格路径下照常检查）。

#### 7. IDE / IntelliSense

三个 IDE 面需要设计而不是顺带：① 代码折叠——`Return` 体属性省了 `End Get`，折叠标记落在 `End Property` 上，折叠范围语义要定义（`Probably` 折叠整个属性头到 `End Property` 即可）；② 签名帮助——`Set(value)` 的推断类型如何展示，是否沿用 #197 的实现；③ 重构——"补全 `End Get`"（展开为完整 5 行）与"压缩为 `Return` 体"应该是一对 code fix，IDE 的"生成访问器"模板是否默认产出简写形态（我们倾向：**模板不默认简写**，简写是作者的选择，不是 IDE 强加）。`#If` 特性若已可用，IDE 的"条件编译淡化/高亮"应对特性行生效——这需要验证。

#### 8. 数据 / 普遍性

计算只读属性在业务代码里**高频到无需论证**——这是 A 最强的普遍性证据，也是我们愿意为它收紧规则的原因。但建议没有给出任何量化数据（对比主线 Implicit-default-optional 用过的 85% 统计），`Suspect`：真实但不精确。自定义事件简写（D）与条件特性（E）的频次要低一个数量级，且 E `Probably` 已可用。**优先级排序：A 最高，C 只保留单 `Return` 豁免，D/E 靠后。**

#### 9. 更简替代

- 计算属性：没有真正替代——自动属性初始化器解决不了"从其它成员计算"，C# 的 expression-bodied 又从未进 VB。接受 5 行样板是现状，但我们认为 3 行 `Return` 体值得。
- `Set(value)`：主线 #197 已在做，不必在这份里造轮子。
- `End` 省略：替代是"不省略"——`End Get`/`End Set` 正是设计原则 #10 里"有用时才保留的显式关键字"，对多访问器属性它们确实有用；所以我们只豁免单 `Return` 的 `End Get`。
- 条件特性：`#If` 是既有的、对的工具；`Conditional` 特性不适用；运行时反射判断是笨重替代。
- 自定义事件：现代替代是**源生成器**（vblang #219 的 INotifyPropertyChanged 讨论里，主线明确"若有了 Replaces + 源生成器，就不做 Bindable/WithPropertyEvents 这类语言特性"）。自定义事件简写同理——低频 + 源生成器可覆盖，这是我们给 D 打 `Table` 的另一个理由。

#### 10. 复杂度 / 成本 / 优先级

`Return` 体实现成本很低：解析器在 `PropertyGetDeclaration` 增一个替代分支（`'Get' LineTerminator ReturnStatement (PropertyAccessorDeclaration | 'End' 'Property')`），把缺失的 `End Get` 作为解析期 token 插入即可，语义层零改动。`Set(value)` 推断成本在 #197（主线已估）。C 的其余部分与 D 成本高、价值低、歧义真实，不做。E 若已可用则成本为零。**整体是"低成本、中价值、低风险"的 DX 优化，不是能力特性。**

#### 11. 运行时 / CLR 硬约束

无。全部是语法到既有语义的映射：`Return` 体生成与 `Get` 块相同的 `get_P` 方法；`Set(value)` 推断只影响参数类型注解；`End` 省略是解析期重写；`#If` 特性是词法既有能力。不触达 CLR 存储规则，无 PEVerify 问题，表达式树/委托转换不受影响。

#### 12. 值不值得做

价值：计算属性简写（A）真实、高频、极 VB，值得；`End` 普遍省略（C 宽版）违背块结构哲学、价值被歧义吃掉，不值得；自定义事件（D）低频且源生成器可覆盖，暂不值得；条件特性（E）可能已经免费。成本：A 低、C 宽版中、D 低、E 零。风险：A 零、C 宽版中（解析歧义）、D 低、E 零。**结论：做 A（含单 `Return` 豁免规则），`Set(value)` 交给 #197，D 并入 minor-fixes 后 Table，E 先验证。** 若要求"五件一起全量落地"，我们建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：A 正中靶心——这是本建议唯一"满格"的基因。
- **读起来像英语、对新手友好（原则 #5）**：`Return` 体是陈述句，比 5 行块更直白；`Get` 裸表达式（B）是祈使句，扣分。
- **冗长只在有用时是美德（原则 #10）**：这是对 C 的主要拷问。`End Get`/`End Set` 对多访问器属性是**有用的显式信号**；只有单 `Return` 时它们才是噪音。所以豁免收窄到单 `Return`，正好落在原则 #10 的"有用/噪音"分界上。
- **不引入"第二种做事方式"（原则 #3）**：A 有张力——它确实是 5 行形态之外的第二种写法。但自动属性（auto-property）本身就是"字段式的第二种写法"，VB 接受了；`Return` 体是"计算式的等价物"，同样是对既有样板的消除而非能力扩张。张力被"占非法语法位、纯增量"这一点对冲。
- **避免隐蔽语义变化（原则 #7）**：唯一扣分点是 `Set(value)` 在 Option Strict Off 下的推断（晚期绑定变早期绑定），用"仅严格模式推断"对冲。没有这条，`Set(value)` 就是又一个 `Return?`。
- **与主线关系（对照表 2.3）**：2.3 表没有简写属性/事件这一行——它是 **Anthony 独立延伸**。但其中 `Set(value)` 推断**主线一致**（vblang #197，Approved-in-Principle），我们明确把这段划回主线实现。`Return` 体与 C# expression-bodied 对齐但做了 VB 化（保留 `Return` 语句味道），是"默认跟随 C# 除非有充分理由"里的偏离方——理由就是"VB 是语句导向的"。`#If` 条件特性是既有规格行为。自定义事件简写是 Anthony 独立延伸且与主线"源生成器优先"的策略冲突（#219 讨论）。自动属性（#196 Implicit Property Backing Fields）曾被主线 **Rejected**（"the design team found this idea deeply unsettling"）——我们讨论中反复以此提醒自己：**对属性样板的"深挖"，主线是有过前科的，简写必须窄、必须纯增量**，这与 A 的收窄方向一致。

### RESOLUTION:

1. **采纳 PROPOSAL A（`Return` 表达式体计算只读属性）**：`Return` 后的表达式即整个 `Get` 访问器，`End Get` 省略，**`End Property` 保留**（3 行而非建议所称 2 行）。纯增量语法，占当前非法语法位，对既有代码零影响。
2. **否决 PROPOSAL B（`Get` 裸表达式）**：新文法类别、祈使语感、与 `Get` 块关键字身份打架。Anthony 的 "`Return` or `Get`?" 定案为 **`Return`**。
3. **否决 PROPOSAL C 的宽版（任意语句体省略 `End`）**：VB 是行结构 + 关键字成对的块语言，没有 C# 大括号的自界定；`RaiseEvent`/`AddHandler`/`RemoveHandler` 语句与访问器声明同形造成真实歧义。**豁免规则：只有"体恰好为一条 `Return` 语句"的 `Get` 访问器可以省略 `End Get`；一切含多条语句的访问器体必须保留 `End` 书签。**
4. **`Set(value)` 形参类型推断划归主线 vblang #197**（Inferred `Set` Parameter Type，2017-11-15 已 Approved-in-Principle），不在本建议内重新设计；**仅 Option Strict On 下推断，Option Strict Off 保持既有 `Object` 行为**，不改变晚期绑定。
5. **PROPOSAL D 拆解**：`_AgeChanged?(sender, e)` 空条件调用触发模式**采纳**为推荐写法；`Custom` 关键字省略与事件访问器 value 参数推断属于 `proposal-minor-fixes.md`；事件访问器的 `End` 书签省略**不做**（v1 Table，与 C 收紧一致）。
6. **PROPOSAL E 先验证**：`#If` 包裹特性行 `Probably` 已是今天可编译的既有行为（预处理作用于逻辑行）；需要一条编译测试证伪 Roslyn 实现层限制。若证伪失败（即已可用），E 改写为"文档 + 测试 + IDE 折叠"建议，**不是语言变更**；若确有实现层限制，则作为 bug 修，仍不算新特性。与 `##If`（语义级）的边界为：符号级归 `#If`，元数据级归 `##If`。
7. **修正样例**：`If value < 0 Then Throw` 不可编译（裸 `Throw` 仅限 `Catch` 内重抛），正确写法为 `If value < 0 Then Throw New ArgumentOutOfRangeException(...)`。旗舰示例必须能编译是硬门槛。

### Implication:

- 起草 speclet：`PropertyGetDeclaration` 新增替代分支文法；"单 `Return` 体省略 `End Get`"的解析规则与"下一逻辑行以访问器关键字/`End Property` 开头"的终止条件；`Return` 表达式显式续行的边界。
- 撰写最小原型：仅 A + 单 `Return` 豁免；验证语义模型（`Get` 访问器含单条 `Return` 的表示）、IDE 折叠与"展开/压缩"一对 code fix。
- 与 #197 实现团队对表：`Set(value)` 推断的 Option Strict 分叉行为；确认宽松路径保持 `Object`。
- 与 minor-fixes 团队对表：`Custom` 关键字省略、事件访问器 value 推断的归属与测试。
- 写一条编译测试：`#If` 包裹 `<Serializable>`/`<DataContract>` 特性行是否已可编译；结果决定 E 的定位。
- 补一份 Compatibility 分析：Option Strict On/Off 下 `Set(value)` 的行为差异表；`#If` 特性的词法语义；`Return` 体与自动属性初始化器的分工说明。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：Option Strict Off 下 `Set(value)` 无类型参数今天的实际绑定（`Object` 还是报错）——spec 的"参数类型必须等于属性类型"是否强制宽松路径？待实测。这决定 #4 的推断门控措辞。
- `OPEN QUESTIONS`：`#If` 包裹特性行是否已可编译——待编译测试证伪/证实；若已可用，E 的剩余价值只剩文档与 IDE 淡化。
- `OPEN QUESTIONS`：`Return` 体属性的 IDE 折叠范围（到 `End Property`）与结构导航标记的精确行为。
- `TODO`：给计算只读属性占比找量化证据（对比主线 Implicit-default-optional 的统计风格），为普遍性补数据。
- `Follow-up`：若 `Return` 体站稳，评估是否推广为 expression-bodied `Function`/`Sub`（单 `Return` 方法省略 `End Function`）——**明确列为后续，不是本建议范围**。
- `Follow-up`：`RaiseEvent` 语句与 `RaiseEvent` 访问器声明的解析歧义，若将来重新考虑事件访问器简写，需先解决；v1 靠"保留 `End`"回避。

### 状态

- **LDM 状态：LDM In Process**；`Return` 体计算属性部分 Active，`End` 宽版省略 Rejected（保留单 `Return` 豁免），`Set(value)` 划归主线 #197，自定义事件与条件特性 Table。
- **三态判定：Active（限定范围）** — 只推进 A + 单 `Return` 豁免；`Set(value)` 随主线；D/E 待证据（源生成器竞争、`#If` 可编译性）明朗后重新评估。

---

## 附录：特性评价

# 建议评价报告：proposal-abbreviated-properties-events.md

## 评价对象

- 建议：proposal-abbreviated-properties-events.md — 简写属性与事件（计算只读属性、访问器 `End` 省略、`Set(value)` 推断、自定义事件简写、`#If` 条件特性）
- 来源：Anthony 原文章节；原文无独立标题，仅标注 "New since summary published"（`..\AnthonyDesign_wordpress.txt`，具体行号未标注——见品质问题）
- 配方目标：压缩属性/事件声明样板，按平台条件选择特性

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。计算属性简写的主效果真实可演示（5 行→3 行），但核心语法未定案（"`Return` or `Get`?"）、旗舰示例不可编译（裸 `Throw`）、`Set(value)` 效果依赖 #197、E 疑似已可行——多个子效果悬空或消解 | 已检查 | 无原型封顶；核心语法未定型 → 效果证据封顶（未决问题 ≥4 项规则）；"2 行"表述与保留 `End Property` 的 3 行不符 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"。`Return` 体语句导向、读起来像英语（#5/#2）、消除样板（#9）；`Set(value)` 推断与主线 #197 一致；`#If` 用既有预处理；`_AgeChanged?(…)` 用既有空条件操作符 | 已检查 | `Get` 裸表达式（B）是外来味且最终被否；"普遍省略 `End`"背离 VB 块结构哲学（#10/#3），是特性维度的主要扣分来源 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文逐字一致、未决问题诚实列出（5 项，符合 1–3+ 健康区间的真实记录） | 已检查 | 旗舰示例不可编译（`Throw`）；省略 `End` 的边界规则明确留白；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；无文法/spec 改动章节；把 5 个独立特性捆一提案，边界模糊（弱提案红旗） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（提速）与光（盘活属性惯用法）正向；风（演化一致性：省略 `End` 是行块结构的结构性偏离）与暗（解析歧义、样例错误、E 未验证）负向 | 已检查（预测待定） | 文档未权衡"自动属性已解决部分样板"与"源生成器可覆盖自定义事件"两条主线竞争路径；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。标注了"New since summary published"但无章节号；借鉴 C# expression-bodied 未声明；`Set(value)` 继承主线 #197 未声明；`#If` 特性利用既有规格行为未声明；`_AgeChanged?(…)` 继承 VB 14 空条件操作符未声明 | 已检查 | 来源标注不全；与 minor-fixes、semantic-preprocessing 的交叉引用在正文缺失（只在未决问题里露头）；无杂质，但血缘梳理不完整 |

## 设计原则对照

- **与 VB 基因：部分一致**。`Return` 体符合 #9/#5/#2；`Set(value)` 推断与主线 #197 一致；"普遍省略 `End`"在 #10 上是减号（`End` 书签是"有用时才保留"的显式信号之一）、在 #3 上是"第二种做事方式"。核心张力与主线 #196（隐式后备字段，Rejected，"deeply unsettling"）同源：**对属性样板的深挖必须窄、必须纯增量**。
- **与主线关系：混合**。`Set(value)` 推断 = **主线一致**（vblang #197 Approved-in-Principle，2017-11-15）；`Return` 体 = **Anthony 独立延伸**，方向与 C# expression-bodied 对齐但 VB 化（保留 `Return`）；省略 `End` = **Anthony 独立延伸**且与 VB 行块结构哲学冲突；`#If` 条件特性 = 主线规格已支持的既有行为（待验证）；自定义事件简写 = **Anthony 独立延伸**，与主线"源生成器优先"策略（#219 讨论）存在竞争。
- **破坏性变更：潜在有**——`Set(value)` 在 Option Strict Off 下若推断为属性类型，会把 `value` 从 `Object` 变成强类型，晚期绑定调用转早期绑定（改变异常时机/重载选择）；需"仅严格模式推断"门控。其余 strands 纯增量。

## 总评

- **达成程度：部分达成**——A（`Return` 体）概念成立且极 VB；但整体把 5 个独立特性捆一提案，其中 1 个属主线已批（#197）、1 个疑似已可行（`#If`）、2 个价值/风险不平衡（宽版 `End` 省略、自定义事件），旗舰示例还不可编译。
- **LDM 三态建议：Active（限定范围）**——仅推进"`Return` 表达式体 + 单 `Return` 豁免省略 `End Get`"；`Set(value)` 并入主线 #197 实现；自定义事件并入 minor-fixes 后 Table；条件特性先验证后定位（已可用则非语言变更）。
- **主要问题**：① 一提案捆绑 5 个独立特性，边界模糊（红旗清单）；② 核心语法未定案（"`Return` or `Get`?"）→ 效果证据封顶；③ 旗舰示例不可编译（`If value < 0 Then Throw`）；④ "省略 `End`"需收紧为"单 `Return` 语句体"，与 VB 行块结构哲学对齐；⑤ `#If` 条件特性疑似已可行，提案未识别，`Conditional` 特性替代方案对本题不适用却未说明。

## 返工建议

- **拆分提案**：A 计算属性（`Return` 体）独立成案；`Set(value)` 推断并入 #197；`Custom` 省略与事件 value 推断并入 minor-fixes；`#If` 条件特性改写成"测试 + 文档"建议；宽版 `End` 省略直接记录为 Rejected。
- **补充章节**：文法（BNF）改动——`PropertyGetDeclaration` 的单 `Return` 体分支与"下一逻辑行终止"规则；Compatibility/breaking-change——Option Strict On/Off 下 `Set(value)` 行为差异表、`langversion` 门控；与自动属性初始化器的分工说明。
- **修正示例**：`If value < 0 Then Throw New ArgumentOutOfRangeException(...)`；补 `End Property`（3 行）；旗舰示例必须能编译。
- **补充证据**：一条 `#If` 包裹特性的编译测试；`Set(value)` 在 Option Strict Off 下今天实际绑定的实测；计算属性占比的量化数据；源生成器对自定义事件覆盖度的调研（决定 D 去留）。
- **未决问题处理**："`Return` or `Get`?" → 定案 `Return`；省略 `End` 边界 → 定案"仅单 `Return` 豁免"；`#If` 与 `##If` 边界 → 符号级归 `#If`、元数据级归 `##If`；`Set(value)` 严格/宽松分叉 → 仅严格模式推断。
- **设计探索**：`Return` 体推广为 expression-bodied `Function`/`Sub` 的扩展路径（明确列为后续）；IDE"展开/压缩"一对 code fix 与折叠范围设计。

---

## 附录：C# 生态与互操作考量

> 本附录为 ModVB 评估追加，依据 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）与 `..\..\csharplang-index.md`（索引）。C# 原文逐字引用并标注来源文件；无法核实处标 **Suspect** / **OPEN QUESTIONS**。本提案（简写属性与事件）与 C# 属性/事件生态直接相关，按「现实方向 → 判定 → 适应建议 → 对 RESOLUTION 影响」展开。

### 相关 C# 现实方向

本提案主题在 C#/CLR/.NET 生态的对应走向，集中在四条已完成/在版 feature 与一个 spec 概念。

**1. C# 14 `field` keyword：合成 backing field 一等公民（"有状态计算属性"主线）**

C# 14 的 `field` 上下文关键字让属性访问器可直接引用编译器合成的**无名**后备字段，桥接"自动属性只能直接读写"与"手写后备字段会泄漏到整个类作用域"之间的缺口。Summary 逐字：

> "Extend all properties to allow them to reference an automatically generated backing field using the new contextual keyword `field`. Properties may now also contain an accessor _without_ a body alongside an accessor _with_ a body." → `proposals\csharp-14.0\field-keyword.md`（Summary）

动机直指后备字段作用域泄漏：

> "The backing field name must then be kept in sync with the property, and the backing field is scoped to the entire class which can result in accidental bypassing of the accessors from within the class." → `proposals\csharp-14.0\field-keyword.md`（Motivation）

spec 侧确认合成字段为隐藏无名私有字段、可直接在访问器内引用：

> "a hidden **unnamed** backing field is automatically available for the property" → `proposals\csharp-14.0\field-keyword.md`（Specification changes → Properties，§15.7.4 改写）

Language-Version-History 的 C# 14 条目：

> "`field` allows access to the property's backing field without having to declare it." → `Language-Version-History.md`（C# 14 条目）

两个与本提案直接相关的边界裁决：

- **事件访问器内 `field` 不是关键字、不合成后备字段**：
  > "`field` is *not* a keyword within an event accessor, and no backing field is generated." → `proposals\csharp-14.0\field-keyword.md`（Answered LDM questions → "`field` in event accessor" → Answer）
- **与 partial property 协作**：实现部分**至少一个 accessor 手写，另一可自动**：
  > "At least one implementing accessor must be manually implemented, but the other accessor can be automatically implemented." → `proposals\csharp-14.0\field-keyword.md`（Interaction with partial properties → Auto-accessors → Answer）

**2. C# 13 partial properties：声明/实现分离（source-gen 使能）**

C# 13 把属性拆成"定义声明"（分号体 accessor）与"实现声明"（有体 accessor，通常由 source generator 写、惯用 `field`）。Language-Version-History 条目：

> "allows splitting a property into multiple parts using the `partial` modifier." → `Language-Version-History.md`（C# 13 条目）

查找规则（与 partial method 同构，最终元数据合并为一个属性）：

> "Only the defining declaration of a partial property participates in lookup, similar to how only the defining declaration of a partial method participates in overload resolution." → `proposals\csharp-13.0\partial-properties.md`

**3. C# 8 property patterns（+ C# 10 extended property patterns）：消费侧读取属性的官方通道**

property pattern 递归读取"可访问的属性或字段"，是 C# 消费计算属性的主要模式匹配入口：

> "A property pattern checks that the input value is not `null` and recursively matches values extracted by the use of accessible properties or fields." → `proposals\csharp-8.0\patterns.md`（Property Pattern）

C# 10 进一步允许嵌套成员路径 `{ Prop1.Prop2: p }` → `proposals\csharp-10.0\extended-property-patterns.md`。

**4. field-like events（spec §15.8.2）与 C# 14 partial events：事件侧对位**

C# 普通事件（`public event EventHandler E;`）是 field-like event：编译器合成后备委托字段 + add/remove 访问器；自定义事件（显式 add/remove）由作者手写后备字段。C# 14 partial events 把事件拆成定义/实现两部分，动机是 weak event 与互操作绑定（Xamarin 例），让 source generator 补实现。partial event 与 field-like 的边界原文：

> "A partial event is not field-like... It does not have any backing storage or accessors generated by the compiler. It can only be used in `+=` and `-=` operations, not as a value." → `proposals\csharp-14.0\partial-events-and-constructors.md`（Detailed design → General）

### 现实 vs 提案

| 本提案 strand | C# 对应 | 判定 | 理由 |
|---|---|---|---|
| A `Return` 体计算只读属性 | C# 6 expression-bodied members（纯计算）；C# 14 `field`（带状态计算） | **兼容** | 元数据上就是普通 `get_P`，C# 侧（含 property patterns）无感知；C# 把"纯计算/带状态计算"分两个机制，A 只做前者 |
| B `Get` 裸表达式（已否决） | 无对应 | **兼容（脱节于 C#，纯 VB 文法决策）** | C# expression-bodied 是 `=>`，无对位物；不影响互操作 |
| C 普遍省略 `End`（收窄为单 `Return` 豁免） | 无对应（大括号自界定） | **脱节** | VB 关键字成对块结构是 VB 架构差异，C# 无协调物；对互操作零影响 |
| `Set(value)` 推断（→ #197） | C# `value` 恒为属性类型 | **兼容** | 走向一致；参数类型在 IL 本就显式，无元数据差异；Option Strict 分叉是 VB 内部绑定策略 |
| D 自定义事件简写（Table） | field-like events；C# 14 partial events | **兼容（被 C# 侧强化）** | 两语言都拒绝"事件访问器合成后备字段"（C# `field` 明确不扩到事件）；C# 用 partial events + source-gen 覆盖样板，正合本 LDM 对 D 打 Table 的理由 |
| E `#If` 条件特性 | C# 同样支持 token 级预处理包裹特性行 | **兼容** | 共享词法模型，无互操作冲突 |

**重点 1：`field` 元数据合成面 vs `.vbx` 简写属性——消费透明、创作不对等。** C# `field`-backed 属性在元数据里是"普通属性 `get_P`/`set_P` + 一个隐藏无名私有字段（+ 可选 field-targeted 特性）"。VB 编译器消费它无需认识 `field`：看到的是普通属性，隐藏字段是 private、VB 语言层不可见；反过来，VB `Return` 体属性只发射 `get_P`，C# 消费方同样无感知。**消费方向完全透明，无需桥接**（且提案正文未要求发射 `CompilerFeatureRequired`——已核对 `proposals\` 目录，该特性仅被 required-members 与 closed-hierarchies 使用）。但**创作方向不对等**：VB 没有任何 `field` 对应物，`.vbx` 作者要写"惰性初始化 / INotifyPropertyChanged setter"这类 C# 14 一行半自动属性，仍须手写后备字段；而 vblang #196（Implicit Property Backing Fields）2017 年被主线 **Rejected**（正文已引 "the design team found this idea deeply unsettling"）。**C# 用了约 9 年从"深恶痛绝"走到 C# 14 落地**——该领域非永久禁区，但 ModVB 若要补洞，必须走"纯增量、占非法语法位"路线（与 A 的收窄同构）。

**重点 2：事件与 field-like event——两语言在"事件不合成后备字段"上同向。** C# `field` keyword 明确不扩展到事件访问器；VB 自定义事件的 `_AgeChanged` 后备委托字段同样手写。对"事件样板"两语言答案一致：**不靠合成，靠 source generator**（C# 14 partial events 的 weak event / 互操作动机；VB 侧 vblang #219 讨论"若有了 Replaces + 源生成器，就不做 Bindable/WithPropertyEvents"）。本 LDM 对 D 打 Table、只采纳 `_AgeChanged?(sender, e)` 空条件触发，正落在 C# 主线上；`_AgeChanged?(sender, e)` 与 C# 惯用 `E?.Invoke(sender, e)` 是同一模式的两语言拼写。

**重点 3：一般性元数据识别提示（与本提案无直接关系）。** 本提案全部 strand 不引入新元数据（语法→既有 `get_P`/`set_P`/事件条目）。但 C# 生态持续让类型系统/元数据承担更多：`CompilerFeatureRequired` 已用于 required members 与 closed classes；unsafe-evolution 将引入 RequiresUnsafeAttribute/MemorySafetyRulesAttribute（决策文件 M8）。`.vbx` 编译器整体必须能识别这些新元数据，否则消费新版 C# 程序集时校验会出错。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：`Set(value)` 推断的 Option Strict 分叉（On 推断 / Off 保持 `Object`）与 C# "类型系统优先、AOT 友好"方向一致，保持。
2. **source-gen 桥**：C# 13/14 把属性与事件样板定在"partial + source generator"。VBScript.NET 应：(a) 确保能消费 partial-property/partial-event 产物（最终元数据即普通属性/事件，无压力）；(b) 为 `.vbx` 作者提供可生成 VB 代码的 source generator 通道，D 类低频样板彻底交给生成器。
3. **识别新元数据**：在 `.vbx` 编译器/运行库实现 `CompilerFeatureRequiredAttribute` 的通用识别与诊断，并跟踪 unsafe-evolution 的 RequiresUnsafe/MemorySafetyRules 属性，支持跨语言校验调用 C# requires-unsafe 成员（决策文件 M8）。
4. **`field` parity 缺口列为 OPEN QUESTION**：若 VBScript.NET 愿景包含"C# 14 同等表达力"，需为"有状态计算属性"找 VB 化答案（候选：`.vbx` 专用半自动属性，允许访问器引用自身后备字段；须先证伪 #196 的 Rejected 理由在 `.vbx` 语境不成立）。
5. **pattern 消费零动作**：VB `Return` 体只读属性就是普通 `get_P`，C# 8/10 property patterns 天然可匹配，无需适配。

### 对既有 RESOLUTION/三态判定的影响

C# 现实方向**确认而非推翻** RESOLUTION：

- A 收窄为"单 `Return` 体 + 保留 `End Property`"：C# 用 `field` 专门处理"有状态计算"，A 只管"纯计算"，分工成立；三态维持 **Active（限定范围）**。
- D 的 Table 状态被 C# 14 partial events **强化**（source-gen 是 C# 官方答案）；仅采纳 `_AgeChanged?(...)` 与 C# `E?.Invoke(...)` 同构，无需修正。
- `Set(value)` 归 #197 + Option Strict 分叉，与 C# `value` 恒定型方向一致，无需修正。
- **新增一条记录（不改判定）**：RESOLUTION 未覆盖"有状态计算属性"（C# 14 `field` 目标场景）；建议在 ModVB 后续提案队列明确其为 OPEN QUESTION，避免 `.vbx` 作者在惰性初始化 / INotifyPropertyChanged 场景仍被迫回到 5 行样板。

### 引用来源与 OPEN QUESTIONS

引用来源（逐字，已在 `..\..\csharplang` 核实）：

| 原文（节选） | 来源 |
|---|---|
| "Extend all properties to allow them to reference an automatically generated backing field using the new contextual keyword `field`..." | `proposals\csharp-14.0\field-keyword.md`（Summary） |
| "The backing field name must then be kept in sync with the property, and the backing field is scoped to the entire class which can result in accidental bypassing of the accessors from within the class." | `proposals\csharp-14.0\field-keyword.md`（Motivation） |
| "a hidden **unnamed** backing field is automatically available for the property" | `proposals\csharp-14.0\field-keyword.md`（Specification changes） |
| "`field` is *not* a keyword within an event accessor, and no backing field is generated." | `proposals\csharp-14.0\field-keyword.md`（Answered LDM questions） |
| "At least one implementing accessor must be manually implemented, but the other accessor can be automatically implemented." | `proposals\csharp-14.0\field-keyword.md`（Interaction with partial properties） |
| "`field` allows access to the property's backing field without having to declare it." | `Language-Version-History.md`（C# 14 条目） |
| "allows splitting a property into multiple parts using the `partial` modifier." | `Language-Version-History.md`（C# 13 条目） |
| "Only the defining declaration of a partial property participates in lookup..." | `proposals\csharp-13.0\partial-properties.md` |
| "A property pattern checks that the input value is not `null` and recursively matches values extracted by the use of accessible properties or fields." | `proposals\csharp-8.0\patterns.md`（Property Pattern） |
| "A partial event is not field-like... It does not have any backing storage or accessors generated by the compiler. It can only be used in `+=` and `-=` operations, not as a value." | `proposals\csharp-14.0\partial-events-and-constructors.md` |

OPEN QUESTIONS：

- vblang #196 的 Rejected 理由在 `.vbx` 语境下是否仍成立——决定 `.vbx` 是否可为"有状态计算属性"引入 `field` 类机制；属 VB 侧设计，C# 原文无法回答。
- C# `field` keyword 合成字段在元数据里的**实际命名模式**未在提案正文给出（"hidden **unnamed** backing field"逐字可引，但具体名称是 Roslyn 实现细节）——**Suspect**；不影响互操作（private 字段本不应被跨语言引用）。
- `[field: ...]` field-targeted 特性在元数据上的落点（后备字段上的特性）对 VB 反射/序列化代码的影响未做实证——**OPEN QUESTIONS**。
