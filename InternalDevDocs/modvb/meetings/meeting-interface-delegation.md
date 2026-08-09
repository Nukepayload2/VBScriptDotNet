# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次议题来自 Anthony 设计博客第 15 章 "Inheritance, Interface Implementation, and Extension" 的一个代码片段——把接口实现委托给字段/属性。本周议程排得满，可空性流分析与隐式接口实现（companion 建议）都在队列里；本建议恰好与后者共享同一片"接口成员归属"的实现面，所以两场讨论必须合起来看。

## Agenda

* [Proposal: 接口实现委托给字段/属性（Interface Implementation Delegation）](#proposal-接口实现委托给字段属性)

## Proposal: 接口实现委托给字段/属性

_Related: ModVB `proposal-implicit-interface-implementation.md`（companion，共享成员归属算法）；[vblang #37 – Implicit Interfaces](https://github.com/dotnet/vblang/issues/37)；[vblang 2017.05.19 LDM – Default Interface Implementations](../../vblang/meetings/2017/vbldm-notes-2017.05.19.md)；[vblang 2018.02.21 LDM – C# Default interface methods](../../vblang/meetings/2018/vbldm-notes-2018.02.21.md)_

### 场景与缺口

We started from an observation about composition. 组合优于继承（Composition Over Inheritance）是 VB 的传统——VB6 没有多重继承，把契约转交给子对象一直是惯用法。但今天这个惯用法是有代价的：每转发一个接口成员，就要手写一个转发桩。

```vb
' 今天：IContractA 的每个成员都要一个转发方法。
Class MyObject
    Implements IContractA

    Private ReadOnly _provider As New ContractAProvider()

    Private Sub M() Implements IContractA.M
        _provider.M()
    End Sub
End Class
```

接口越"宽"，桩越多——`INotifyPropertyChanged`、`IEnumerable`、`IEquatable(Of T)` 这类接口尤甚。Anthony 的建议把转发升格为声明（原文第 15 章代码片段，标题 "Interface implementation delegation to fields/properties"，注释 "(Composition Over Inheritance FTW!)"）：

```vb
' 建议：字段 Implements 接口，编译器代写全部转发方法。
Class MyObject
    Implements IContractA

    Private WholeProvider As ContractAProvider Implements IContractA
End Class
```

We see a real gap here——样板是真实的、方向是 VB 的。但我们进会议室时的第一个反应不是"好特性"，而是三个问题：**语法归属**（`Implements` 长在字段声明上意味着什么）、**成员归属规则**（哪些成员归字段、哪些归宿主，怎么判定）、以及**"为什么不用工具"**（IDE 代码生成与 Source Generator 可能零语言改动地交付大半价值）。We'll take them one at a time。

补充一句跨语言背景：**C# 没有对应特性**（`Probably`——C# 的答案一直是 "Implement Interface" 代码生成与源生成器，未见语言级字段委托）。Swift 的 delegate 是设计模式而非语言机制；协议扩展提供的是默认实现，不是向存储属性转发。F# 的 object expression 可以内联生成匿名接口实现，但那是"现写现用"，也不是"委托给既有字段"。没有先例可循意味着我们得自担全部语法与语义设计风险——这在本语言的历史上既是机会也是负担。

### 候选方案

**PROPOSAL A — 字段级整体委托。** 按建议原文：`Private WholeProvider As ContractAProvider Implements IContractA`，字段类型实现完整接口，宿主转发全部成员。

**PROPOSAL B — A + 部分委托（宿主显式覆盖）。** 建议原文的核心主张：字段满足它实现的部分（`X`），宿主用显式 `Implements IContractB.Y` 补上自己负责的成员（`Y`）。

**PROPOSAL C — 属性载体。** 以属性而非字段作为委托载体，getter 每次可返回不同实例，支持运行期动态切换实现（decorator / strategy）。建议标题写的是 "fields/properties"，但正文只有字段示例。

**PROPOSAL D — 不做语法。** 依赖 IDE "Implement Interface" 代码生成与 Source Generator：源生成器在 partial 类里替宿主生成转发桩，零语言改动。

**PROPOSAL E — 并入隐式接口实现（companion）。** 不引入新的字段语法，而是把"委托字段/属性"作为隐式接口实现的一种成员来源，统一进 companion 建议的成员归属算法。

**OTHER DESIGNS CONSIDERED — 新关键字 / 类级 Forward 子句。** 如 `Delegates To` 关键字，或类级 `Implements IContractA Forward To WholeProvider`。引入新关键字在 VB 是高门槛（原则 #3、#8）；类级子句把"契约"与"转发目标"分开写，读起来两步，不如字段声明一处表达。We 不推进这两条。

### 权衡：Q&A

- **A vs B：部分委托值得吗？** 值得，但**建议的表述有误**。在 VB 里 `ContractBProvider Implements IContractB` 必须实现 `IContractB` 的**全部**成员（`X` 和 `Y`）——"它实现的部分（X）"这句话在合法 VB 里不成立，provider 不可能只实现 X。所以"部分委托"的真实形态是：**整体委托 + 宿主显式覆盖**。`PartialProvider` 覆盖全部 `IContractB`，宿主用 `Private Sub IContractB_Y() Implements IContractB.Y` 在 Y 上覆盖字段。一旦把规则定义为"宿主显式成员优先于字段委托"，部分委托就不再需要独立的"已覆盖集合判定"——它就是同一机制的必然结果。这是本次会议最重要的澄清。

- **字段 vs 属性（A/C）**：属性载体（C）让 provider 可动态切换，很诱人（decorator）。但 getter 可能返回 `Nothing`——转发调用在哪个点 NRE？getter 可能每次返回不同实例——宿主的接口"身份"是移动靶（引用相等性、锁对象、`SyncLock` 目标都会漂移）。字段是稳定的：宿主生命期内只有显式赋值才换目标。**v1 只做字段**；属性作为自然的后续扩展，但要先把身份与 null 语义设计完（见 OPEN QUESTIONS）。

- **语法 vs 工具（A vs D）**：这是整场最尖锐的追问。VB 已有源生成器；一个 `<DelegateTo>` 属性 + partial 类生成器，理论上能替宿主生成全部转发桩——**零语言改动**交付 80% 价值。反对：生成器产出的代码是可见的噪音、接口变更后需要重新生成（stale 风险）、且"声明即契约"的可读性丢了。但 We 不得不承认：**工具是认真的竞争者**，建议原文的 Alternatives 只列了"手工转发"，没回应工具。我们要求建议作者补一个 Source Generator 对照 POC 再谈语法。

- **新语法 vs 并入 companion（A vs E）**：companion（隐式接口实现）和本建议共享同一片实现面——"哪个成员由谁满足"。2017.05.19 主线 LDM 讨论默认接口实现时明确说过：*"Because of the way the CLR looks up interface implementations (by name), if an interface declares an overridable member _and_ an implementor declares or inherits a public `Overridable`/`Overrides` member of the same name it will implicitly be picked up as the implementation/override of that interface member."* 并最终**"Decision Let's make it an error for now and evaluate implicit interface implementations separately."**——主线当时把隐式实现按错误处理、另行评估。ModVB 的 companion 正是在推翻这个"for now"。如果两个建议都落地，**成员归属算法必须是同一份 spec**：显式字段委托 > 宿主显式成员 `Implements` > 隐式同名匹配 >（未来）默认接口方法。分开设计就是两套心智模型，会违反原则 #3。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Implements` 在 VB 是保留字（`Probably`，待编译器保留字表核对）——标识符不能叫 `Implements`，所以把它加到字段声明上没有词法歧义。真正的歧义在**文法位置**与**多字段声明**：

```vb
' 歧义 1：Implements 相对于类型与初始化器的位置。
Private WholeProvider As ContractAProvider = New ContractAProvider() Implements IContractA
'                                          ^^^^^^^^^^^^^^^^^^^^^^^^^   ^^^^^^^^^^^^^^^^^
' 是紧跟类型之后，还是允许跟在初始化器之后？

' 歧义 2：逗号多字段声明时 Implements 修饰谁？
Private a As ContractAProvider, b As ContractBProvider Implements IContractA
'                            ^^^ Implements IContractA 是修饰 a、b 还是整体？
```

2017.05.19 LDM 讨论默认接口实现时遇到同类问题：*"Are there any syntactic ambiguities around default interface implementations in VB? In C# a definition with a body or without one can be determined by the presence or absence of a semicolon."* 并标记 **Follow-up: Need to look at parser and come back.** 我们 v1 的答案是：`Implements` 紧跟 `As Type` 之后，**禁止**多字段声明携带 `Implements`（`Private a As T, b As U Implements I` 直接报错）。文法待编译器团队确认。

#### 2. 角案例与边界语义

**未赋值字段（Nothing）。** 委托字段不要求编译器强制初始化。接口调用转发到 `Nothing` 字段 → 调用点 NRE：

```vb
Dim o As IContractA = New MyObject()   ' WholeProvider 从未赋值
o.M()                                  ' NullReferenceException
```

这**与手工转发完全一致**（手工 `_provider.M()` 在 `_provider` 为 Nothing 时同样 NRE）。我们不插入特殊空检查、不改变异常时机——"委托应与手工转发可替换"是硬约束。但要注意：异常发生在调用点而非构造点，且接口方法的调用方看不到字段，排查需要 IDE 提示。

**字段可重赋值。** 宿主生命期内字段可换实例——这是特性不是 bug（可替换 provider），但接口"身份"随实例漂移。文档化，不设防。

**值类型 provider。** 结构体实现接口并委托：

```vb
Structure StructProvider
    Implements IContractA
    Public _value As Integer
    Public Sub M() Implements IContractA.M
        _value += 1
    End Sub
End Structure

Class MyObject
    Implements IContractA
    Private _p As StructProvider Implements IContractA
End Class
```

转发 `M()` 必须**就地**变更 `_p._value`——编译器要生成 `ldflda` + `constrained` 调用。若误把字段拷贝求值，增量就丢了。可做，但属于实现陷阱。**v1 仅限类类型 provider**。

**泛型与变体。** 泛型宿主字段委托可行（类型参数满足泛型接口）：`Private _p As Provider(Of T) Implements I(Of T)`。但协变/逆变接口的变体匹配（`I(Of Out T)`）v1 不启用——要求**精确**接口实现，与手工转发语义一致。

**事件转发。** 接口成员可以是事件（`AddHandler`/`RemoveHandler`/`RaiseEvent`）。转发事件不是普通调用：`RaiseEvent` 的触发上下文（谁触发、在宿主上触发还是 provider 上触发）需要专门设计。2017.05.19 LDM 对接口事件的处理也留了尾巴（Follow-up: Verify event behavior makes sense with regard to WinRT events）。**事件转发整体 Table**，v1 不承诺。

**重载、Optional、ByRef、泛型方法。** 直接转发，宿主不重新声明签名——这带来一个**隐蔽优点**：VB 显式接口实现要求 Optional 参数默认值完全一致（2014.02.17 LDM 讨论隐式接口时也确认过默认值规则），而委托天然一致，因为宿主根本没有自己的签名。这个优点值得写进提案。

#### 3. 作用域与绑定

语义模型里，`o.M()`（`o` 声明为 `IContractA`）的成员符号是什么？We 认为：**合成转发方法是一个真实符号**，带 `Implements IContractA.M` 子句与 "Forwarded to `WholeProvider.M`" 元数据；`GetSymbolInfo` 返回它，Go to Definition 指向 provider 成员，Quick Info 显示转发标记。部分覆盖的成员（宿主的 `IContractB_Y`）返回宿主方法符号。绑定到字段还是 backing field 的问题在 v1 不存在——只绑字段符号；属性载体落地后再定（绑到 getter）。

#### 4. 与既有特性的交互

- **与隐式接口实现（companion）**：必须共享成员归属算法。优先级：显式字段委托 > 宿主显式成员 `Implements` > 隐式同名匹配。若 companion 先落地，字段委托须能在其之上叠加——否则两套算法互相覆盖。
- **与默认接口方法（DIM，C# 8）**：主线 2018.02.21 讨论 C# 默认接口方法时说过：*"C# has a proposal for default interface members. A compelling scenario for this involves allowing interfaces to change over time without breaking all implementors."* 若 VB 未来采纳 DIM，接口成员有默认实现时，宿主未覆盖的成员**不需要**自实现。委托是否仍然转发有默认实现的成员？我们的答案：是——显式委托覆盖默认实现。但"委托缺口诊断"（见 RESOLUTION 4）在 DIM 下应放宽为"未覆盖成员可继承默认实现"。DIM 落地后再对齐。顺带：主线 2018.02.21 该段末尾注明 *"Talk to @AnthonyDGreen for background."*——与本建议作者是否为同一人待核实（`Suspect`），但至少说明主线曾在 DIM 问题上指向同一来源。
- **与 `WithEvents` / `Handles`**：字段同时 `WithEvents` + `Implements`？允许，但事件转发与 `WithEvents` 的挂钩语义需设计。v1 不承诺。
- **与构造函数 definite assignment**：字段初始化器 `= New ContractAProvider()` 推荐；未初始化字段的调用 NRE（见上）。不强制。

#### 5. Breaking change 与兼容性

**无破坏性变更**——这是本特性最强的加分项，在本语言里罕见。`Implements` 出现在字段声明之后，旧代码本来就编译失败（`Implements` 不允许在该位置），所以**没有任何旧代码行为变化、没有重编译行为变化**。唯一要守住的不变量：未来把委托并入成员归属算法时，不得改变已显式 `Implements` 的既有代码的归属。优先级规则（显式 > 委托 > 隐式）保证这一点。

#### 6. Option Strict / 编译选项分叉

无分叉。委托要求字段类型在严格与宽松两条路径上都**静态**实现接口；`Object` 类型字段无法委托（`Object` 没有静态的接口实现信息），宽松路径的晚期绑定与委托无关。两路径行为一致，这点与可空流分析、类型收窄那种需要分叉审计的特性不同，设计上省心。

#### 7. IDE / IntelliSense

"Implement Interface" 补全应新增"委托到字段"选项；宿主上的转发成员在补全里**只读**显示（标注 Forwarded）；Go to Definition 到 provider；字段声明上的 `Implements` 有语法着色。诊断：委托缺口（见 RESOLUTION 4）需要专门错误文案。全部在原型验证。

#### 8. 数据 / 普遍性

组合优于继承在 adapter / decorator / facade 代码里真实存在，但"类有多个 1:1 映射的字段 + 接口"的频率**没有数据**。Anthony 原文只有代码片段、无论述——价值主张全靠常识。VBScript.NET 的脚本受众里，脚本作者惯于组合小组件（推测），频率可能高于企业 VB。`Suspect`：价值真实，普遍性未量化，需数据。

#### 9. 更简替代

- **手工转发（现状）**：样板多但确定。
- **IDE "Implement Interface" 代码生成**：一键生成转发桩，但接口变更要重新生成，代码膨胀。
- **Source Generator（最强竞争者）**：VB 已有源生成器，可生成 partial 类转发桩，**零语言改动**。建议作者未回应。
- **companion（隐式接口实现）**：不同方向消样板，与委托共享归属算法——不是替代，是必须统一。

#### 10. 复杂度 / 成本 / 优先级

编译器成本中等：字段文法 + 成员归属算法 + 合成转发方法 + 符号模型 + IDE。主要成本在归属算法与 companion 的统一。优先级低于可空性流分析、模式匹配；但"零破坏 + 自包含 + 可增量落地"意味着它适合作为小步快跑的特性排在后面。v1 若砍掉属性/事件/值类型/Shared，成本显著下降。

#### 11. 运行时 / CLR 硬约束

无硬约束。合成转发方法就是普通显式接口实现（`MethodImpl` 映射），标准元数据；值类型 provider 的 `constrained` 调用是既有指令。不触达 CLR 存储规则，PEVerify 无碍。这与类型收窄（纯编译期）是同一类"无运行时足迹"的特性。

#### 12. 值不值得做

价值（消除高频样板、零破坏、极 VB 的组合表达）：中-高。成本（归属算法与 companion 统一）：中。风险（部分委托规则被我们重写、隐蔽转发的阅读负担、工具竞争）：低-中。**值得做——但必须重写成"整体委托 + 宿主显式覆盖"的干净设计**，且与 companion 统一算法后再谈语法落地。若坚持原始"部分委托"表述（provider 只实现一部分），我们会直接建议不做——那是一条说不清的规则。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心——声明即转发，一个字段消灭 N 个转发桩。
- **不引入"第二种做事方式"（原则 #3）**：最大张力点。字段委托 + 显式成员 `Implements` +（未来）隐式实现 = 三种成员归属来源。不统一就是三种做接口的方式。我们的解法：单份成员归属算法 + 严格优先级，让三者落在同一心智模型下。
- **避免隐蔽的语义变化（原则 #7）**：字段委托是**隐蔽转发**——调用点看不到目标。这是与原则 #7 的核心张力。对冲：IDE 转发标记 + 语义模型指向 provider。原则 #7 原本针对"细微字符改变语义"，本特性无字符变化，但调用目标是隐式的，需要可见性补足。
- **读起来像英语、对新手友好（原则 #5）**：`Private WholeProvider As ContractAProvider Implements IContractA` 声明即契约，英语可读；但"字段同时是接口实现"违背"字段只是数据"的直觉（建议的 Drawbacks 也自认这一点）。
- **不与既有语法冲突（原则 #8）**：`Implements` 是保留字（`Probably`），加新位置无词法歧义；字段声明的 modifier 序列与多字段限制需精确定义。
- **默认跟随 C#（原则 #4）**：C# 无对应特性（`Probably`）——无对齐压力、也无先例可循；本特性构成对 C# 的差异化（水维度），但需自担全部设计风险。
- **与主线关系（对照表 2.3）**：主线**无接口委托工作**——Anthony 独立延伸。与 companion 隐式接口实现（主线 2014 #37 *"Tentatively approved, but still needs design work. Parity with C#."*、*"One typical scenario for it is code-generators and partial classes."*）共享成员归属算法。2014 主线的决议方向我们引用一句：*"RESOLUTION: Yes. Use Proposal3. We will look for a keyword combination that seems nice."* 以及当时的一个观察——*"it's a weird situation that the more desirable syntax "Implicitly Implements" is more verbose than the less desirable traditional syntax "Implements""*——这个"想要的语法反而更长"的张力，在本建议里同样存在（字段委托比成员级 `Implements` 短，好）。

### RESOLUTION:

1. **原则上接纳**组合式接口实现的**方向**，但**不采纳**当前建议的完整语法形态作为 v1。重写为窄设计：**类类型字段整体委托 + 宿主显式成员覆盖**。字段类型必须实现（含继承的）完整接口；宿主的显式 `Implements` 成员在该成员上**优先于**字段委托——这即"部分委托"的干净定义，不需要额外的"已覆盖集合判定"规则。
2. **v1 范围**：字段（非属性）作为委托载体；仅类类型（非 Structure）provider；不支持 `Shared`；**禁止**多字段声明携带 `Implements`；不支持泛型变体匹配（要求精确接口实现）。
3. **成员归属优先级**（先于 companion 落地，作为单份 spec）：**显式字段委托 > 宿主显式成员 `Implements` >（未来）隐式同名实现 >（未来）默认接口方法**。三者共享同一成员归属算法。
4. **委托缺口诊断**：编译器计算"接口成员集合 − 字段覆盖集合 − 宿主实现集合"，非空即报错并逐成员列出；错误文案指明"该成员由字段 X 委托，或由宿主实现"。
5. **语义模型与 IDE**：合成转发方法为真实符号，带 `Implements` 子句与 "Forwarded to {字段}.{成员}" 元数据；Go to Definition 到 provider；Quick Info 显示转发标记；补全只读显示转发成员。
6. **null 语义**：无特殊处理——与手工转发一致，未赋值字段的调用在调用点 NRE。不插入空检查，不改变异常时机。
7. **属性载体、事件转发、值类型 provider、`Shared`、DIM 交互 ⇒ Table**。先补 grammar、Compatibility、语义模型章节，状态行去占位链接（当前 `PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1` 是红旗）。
8. **不引入新关键字**；`Implements` 继续作为接口绑定的唯一关键字（与 2017.05.19 LDM 对 `Implements` 子句的确认一致）。

### Implication:

- 重写 proposal：标题改为"字段级接口实现委托"；补 Grammar、Compatibility、语义模型与 IDE 章节；移除占位链接。
- 最小原型：字段文法 + 合成转发方法（`MethodImpl`）+ 集合差诊断 + 语义模型符号 + IDE 补全。
- 与 companion（implicit-impl）团队对表：成员归属算法单份 spec，优先级表落地。
- Source Generator 对照 POC：评估 `<DelegateTo>` 属性的生成器能否交付 80% 价值。
- 未决问题移交 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：属性载体（动态切换 provider）——getter 返回 `Nothing` / 不同实例时，接口身份与 null 语义如何设计。
- `OPEN QUESTIONS`：接口事件转发——`RaiseEvent` 的触发上下文（宿主触发 vs provider 触发）。
- `OPEN QUESTIONS`：与默认接口方法（C# 8 DIM）交互——未覆盖成员是否继承默认实现而非要求宿主实现。
- `OPEN QUESTIONS`：值类型 provider 的 `constrained` 转发（字段就地变更）是否值得 v1 支持。
- `TODO`：量化 adapter / decorator / facade 场景的普遍性数据；对比 Source Generator POC 的代码量与维护成本。
- `Follow-up`：与 companion 对齐成员归属算法；核对 Anthony 原文第 15 章——当前"部分委托"规则是对代码片段的**解读**（原文无论述），须在提案中标注来源与解读性质。

### 状态

- **LDM 状态：Consider（限定范围重写后可升 Active）**。
- **三态判定：Consider** — 价值真实、零破坏、极 VB 的组合表达；但语法未定型、原始"部分委托"表述被我们重写、工具替代未回应、fields/properties 不一致。窄设计（整体委托 + 宿主显式覆盖）是干净的落地面。

---

## 附录：特性评价

# 建议评价报告：proposal-interface-delegation.md

## 评价对象

- 建议：proposal-interface-delegation.md — 接口实现委托给字段/属性
- 来源：Anthony 原文第 15 章 "Inheritance, Interface Implementation, and Extension"（`..\AnthonyDesign_wordpress.txt` L2430–2520；接口委托代码片段 L2473–2510，标题 "Interface implementation delegation to fields/properties"，注释 "(Composition Over Inheritance FTW!)"；**原文只有代码，无论述**）
- 配方目标：类把接口实现整体/部分转发给字段/属性，消除手工转发样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。整体委托目标清晰、示例可编译（来自原文）；但"部分委托"归属规则是一段描述而非 spec，属性载体无示例，"fields/properties" 标题与字段示例不符 | 已检查 | 无原型封顶 3；部分委托核心规则未定型（被 LDM 重写为"整体委托+显式覆盖"） |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。复用 `Implements` 关键字（VB 基因）、组合优于继承（VB 传统）；但"字段同时是接口实现"打破"字段只是数据"直觉，且一个语法打包整体/部分/属性三能力 | 已检查 | 隐蔽转发的阅读负担；三种成员归属方式并存风险（原则 #3 张力） |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全；缺 Grammar/BNF、缺 Compatibility 章节；状态行占位链接（红旗）；归属规则一段话；4 个未决问题均关键 | 已检查 | 占位链接；部分委托规则未 spec 化；fields/properties 不一致未解决 |
| 属性 | 3/5 | 锚点 3："有得有失"。水（差异化 C#）、光（组合资产复用）正向；风（与 companion 重叠、三种消样板方式并存）、暗（隐蔽转发、Nothing 语义）负向 | 已检查（预测待定） | 与 companion 重叠未权衡；工具替代（Source Generator / IDE 代码生成）未探讨 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。材料=Anthony 第 15 章代码片段，建议未标注章节/来源；详细设计的归属规则是对代码片段的**解读**（原文无论述）而未说明；无借鉴 C#（正确——C# 无对应）；继承 VB `Implements` 惯用法未点明 | 已检查 | 未标注章节；解读性规则未标 Suspect |

## 设计原则对照

- **与 VB 基因：部分一致**——消除样板（#9）正中；复用 `Implements`（#8 无词法冲突）是基因；但隐蔽转发（#7）与"字段只是数据"直觉（#1 简单性）有张力。
- **与主线关系：Anthony 独立延伸**——主线无接口委托工作；与 companion 隐式接口实现（主线 2014 #37 tentative approved）共享成员归属算法，需统一。
- **破坏性变更：无**——纯增量语法；旧代码中字段后 `Implements` 本就编译失败，无旧代码行为变化。

## 总评

- **达成程度：部分达成**——概念成立、零破坏、方向极 VB；但设计未成熟（部分委托规则、fields/properties 不一致）、工具替代未回应。
- **LDM 三态建议：Consider**——窄设计（类字段整体委托 + 宿主显式覆盖）原则上采纳；全量建议（属性载体、事件、Shared、值类型、DIM 交互）Table。
- **主要问题**：① 原始"部分委托"表述不成立（VB 里 provider 必须实现完整接口），被重写为"整体委托+显式覆盖"；② 与 companion 未定义优先级/共享归属算法；③ "fields/properties" 不一致且属性载体语义未设计；④ 未回应 Source Generator / IDE 代码生成这一更简替代。

## 返工建议

- **补充章节**：Grammar（字段 modifier 序列、`Implements` 与类型/初始化器的相对位置、多字段限制）；Compatibility（无破坏论证）；语义模型与 IDE（合成符号、转发标记、补全只读）；与 companion 的统一优先级表。
- **补充证据**：最小原型（字段文法 + 合成转发 + 集合差诊断）；Source Generator 对照 POC；adapter / decorator / facade 场景普遍性数据。
- **未决问题处理**：属性载体 → Table 并设计身份/Nothing 语义；事件转发 → 设计 `RaiseEvent` 语义；`Shared` → v1 禁用；值类型 → v1 禁用（`constrained` 实现陷阱）；DIM 交互 → 留待默认接口方法落地后对齐。
- **设计探索**：与 companion 统一"成员归属算法"的单份 spec；"显式字段委托 > 宿主显式成员 > 隐式同名 > 默认实现"的优先级表；委托缺口诊断的错误文案；**标注来源**——当前规则是对 Anthony 第 15 章代码片段的解读。

---

## 附录：C# 生态与互操作考量

> 本附录为 ModVB 评估追加，依据 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）与 `..\..\csharplang-index.md`（索引，主题 T2/T8/M7 相关）。C# 原文逐字引用并标注来源文件；无法核实处标 **Suspect** / **OPEN QUESTIONS**。本提案（接口实现委托给字段/属性）与 C# 生态的关系是**兼容为主、无冲突，且互操作面干净**：C# **没有语言级"委托实现接口"**（正文 §场景与缺口已标 `Probably`，本附录在镜像中的检索亦未见字段/属性级委托提案），其生态用三条路径覆盖同一需求——①DIM（接口默认实现，C# 8）+ ②手写 composition pattern（转发桩）+ ③source generator / IDE "Implement Interface" 代码生成；工作组曾探索"interface-implementing extensions"（roles）作为语言级出口，因运行时表示困难而搁置。本提案因此构成对 C# 的**差异化（水维度）**，而非对齐或冲突。真正的桥接点集中在：成员归属优先级与 C# "most specific implementation" 规则的同构、DIM 交互规则（RESOLUTION 7 Table）、以及 `.vbx` 编译器消费 C# DIM / `allows ref struct` 元数据的既有缺口。按「现实方向 → 判定 → 适应建议 → 对 RESOLUTION 影响」展开。

### 相关 C# 现实方向

本提案主题在 C#/CLR/.NET 生态中的对应走向，集中在以下五条。

**1. C# 8 Default Interface Methods（DIM）：C# 对"接口成员由谁实现"的第一个语言级答案——接口自带默认实现。** Summary 逐字：

> "Add support for _virtual extension methods_ - methods in interfaces with concrete implementations. A class or struct that implements such an interface is required to have a single _most specific_ implementation for the interface method, either implemented by the class or struct, or inherited from its base classes or interfaces. Virtual extension methods enable an API author to add methods to an interface in future versions without breaking source or binary compatibility with existing implementations of that interface." → `proposals\csharp-8.0\default-interface-methods.md`（Summary）

DIM 与本提案共享同一片"接口成员归属"实现面（正文 Q&A 已述 companion / 本提案共享归属算法）——C# 的归属规则是：

> "We require that every interface and class have a *most specific implementation* for every virtual member among the implementations appearing in the type or its direct and indirect interfaces." → 同上（Detailed design → The most specific implementation rule）

其"类实现优先于接口默认"的规则，与本提案 RESOLUTION 3 的"宿主显式成员优先于字段委托"是同一抽象在 C# 侧的先例：

> "`T2` is an interface type but `T1` is not an interface type." → 同上（The most specific implementation rule）

LDM 进一步钉死"类永远赢接口默认"：

> "A class implementation of an interface member should always win over a default implementation in an interface, even if it is inherited from a base class." → `meetings\2017\LDM-2017-04-19.md`（Diamonds with classes）

**2. C# 无语言级字段/属性委托——手写 composition pattern 是唯一"转发"手段，工具是 IDE 生成与 source generator。** 本附录作者在 `..\..\csharplang` 镜像中检索 composition / interface implementation 主题，**未见任何字段/属性级委托提案**（相关命中全是 DIM 及其边界讨论）；C# 生态对转发样板的正规答复是手写转发桩 + "Implement Interface" 代码生成 + source generators（索引 T6）。与本提案关系：**没有语法可以对齐，也没有语法可以冲突**——本提案是 C# 未做的语言级能力。

**3. struct + DIM 的 boxing wart（2017 LDM）：C# 明确不为值类型的接口默认实现做特殊代码生成，且因"以后再做会是 breaking change"而永不修改。** 这直接背书本提案 RESOLUTION 2 的"v1 仅限类类型 provider"：

> "It is very hard to use default implementations from structs without boxing." → `meetings\2017\LDM-2017-04-19.md`（Structs and default implementations）

> "Let's stick with option 3. It would be a breaking change to do 2 later, so we can never change it." → 同上（Conclusion）

本提案值类型 provider 的 `constrained` 转发（OPEN QUESTION）若做，是在 C# 明确放弃的同一片地里另辟蹊径——v1 禁用的判定与 C# 现实同向，且须吸取"不可逆"的教训。

**4. ref struct interfaces（C# 13）：C# 新增"接口实现者"类别，但其与 DIM 的边界规则恰好是"强制实现全部成员"。** 动机逐字：

> "The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET." → `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）

ref struct 不能吃 DIM 红利（默认实现可能 box `this`），C# 强制其实现全部成员：

> "Default interface methods pose a problem for `ref struct` as there are no protections against the default implementation boxing the `this` member." → 同上（Detailed Design → ref struct interfaces）

> "To handle this a `ref struct` will be forced to implement all members of an interface, even if they have default implementations." → 同上

这与本提案无直接关系（VB 的 ref struct 支持目前是分析器层面，决策文件 D1），但佐证两条生态共识：**接口实现者与"接口默认实现"的边界必须被语言显式划定**（C# 划在"ref struct 不能依赖 DIM"处，本提案划在"委托缺口诊断须把 DIM 成员排除在外"处）；且 `allows ref struct` 用元数据 flag 表达（`CorGenericParamAttr.gpAllowByRefLike(0x0020)`）——VB 编译器需能解析这类约束元数据。

**5. extensions / roles（C# 14/15 工作组）：C# 唯一探索过"替别处实现接口"的语言级方向，因运行时表示困难而搁置。** 这是 C# 侧最接近"接口委托"的思考：

> "If extension members could somehow help a type implement an interface without the involvement of that type, this would facilitate similar adaptation capabilities to what type classes provide in Haskell, and would greatly aid software composition." → `meetings\working-groups\extensions\the-design-space-for-extensions.md`

但该方向折戟：

> "This approach ran into several consecutive setbacks: We couldn't find a reasonable way to represent interface-implementing extensions in the runtime." → 同上

> "The type-based syntax lends itself to a future where extensions implement interfaces on behalf of underlying types." → 同上（Interface implementation）——注意这只是 type-based extensions 语法"留了口子"的远期可能性，未进入任何版本计划。

含义：C# 生态把"为类型提供接口实现"的语言级能力判了缓刑——本提案若落地，是 C# 想做但没做成的事。若未来 roles 复活，VB 字段委托与 C# roles 是"同一需求、不同声明位置"的竞争解法，须关注 C# 15/16 走向（索引 T8）。

### 现实 vs 提案

| 本提案要点 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| 核心：字段声明 `Implements IContractA`，编译器合成全部转发方法 | C# 无语言级委托；手写 composition pattern + IDE/source-gen | **兼容 + 脱节（纯 VB 差异化）** | C# 生态对转发样板的正规答复是手写桩与代码生成，从未做语言语法；合成转发方法就是普通显式接口实现（`MethodImpl` 映射），标准元数据，C# 消费方零桥接（正文 §运行时/CLR 硬约束） |
| 成员归属优先级：显式字段委托 > 宿主显式 `Implements` > 隐式同名 > 默认接口方法 | C# DIM "most specific implementation" 规则 + "类实现永远赢接口默认" | **兼容（同构先例）** | C# 已有同一抽象的语言规则，可借鉴其措辞与菱形冲突诊断；本提案优先级表是 C# most-specific 在"委托 vs 显式 vs 隐式"三个归属源上的实例化 |
| 部分委托 = 整体委托 + 宿主显式覆盖（RESOLUTION 1） | C# DIM 的 reabstraction / explicit override 处理"接口内覆盖"与 diamond 冲突 | **兼容** | C# 也要求"更具体的实现由程序员显式解决冲突"——"宿主显式成员赢字段委托"与"类实现赢接口默认"同构 |
| 委托缺口诊断：集合差（接口成员 − 委托覆盖 − 宿主实现） | DIM 下"未覆盖成员可继承默认实现"，缺口可为 0 | **需桥接（RESOLUTION 7 Table，但可提前定规则）** | 若 C# 接口成员带默认实现，集合差须排除之，否则误报缺口；C# "实现者可依赖默认实现"语义下"未覆盖成员继承默认实现"成立 |
| 值类型 provider v1 禁用（RESOLUTION 2） | struct + DIM boxing wart（C# 明确 "leave it as a wart" 且不可逆） | **兼容（生态背书）** | C# 不为值类型接口默认实现做特殊代码生成；VB v1 排除值类型与 C# 现实同向；`constrained` 转发若做属 VB 独有，须自证且先定不可逆语义 |
| Source Generator 竞争（PROPOSAL D） | C# 生态的官方答案是 source-gen / IDE "Implement Interface"（索引 T6） | **需桥接（工具先于语言）** | C# 早已把接口样板交给生成器；VB 的 `<DelegateTo>` 生成器 POC 是 C# 对齐路径，语言特性落定后应生成与 C# 手写转发相同的 `MethodImpl` 形状，两条路径互为退路 |
| 消费 C# DIM / ref struct 接口元数据 | DIM（`RuntimeFeature.DefaultInterfaceImplementation`）+ `allows ref struct`（`gpAllowByRefLike(0x0020)`） | **需桥接（既有缺口，非本提案引入）** | `.vbx` 编译器须识别 C# 接口默认实现与 `allows ref struct` 约束，否则接口演化 / C# 13 泛型接口消费时断裂（决策文件 M4/M7；vblang 2018.02.21 已标为 VB 的 "serious problem"） |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**。字段委托是纯静态、编译期合成 `MethodImpl` 转发，无反射、无运行时动态 → AOT/trimming 友好（索引 T5/T6）；与 C# 弱化 dynamic / 强化类型系统同向（索引 T7/T8）。这是本提案与 C# 生态关系里最强的正面结论——它不加重 `.vbx` 的 AOT 负担，反而提供"声明即契约"的类型化出口。
2. **source-gen 桥先于语言**。按 RESOLUTION Implication 先做 `<DelegateTo>` Source Generator 对照 POC（生成 partial 类转发桩），这与 C# 生态"接口样板交给生成器"的现实一致；语言特性落定时，编译器合成的转发方法应与 source-gen 产出**同一 `MethodImpl` 元数据形状**（显式接口实现映射到 provider 成员），两条路径可互换、可并存。
3. **识别新元数据是消费 C# 生态的共同前提**：
   - C# 8 DIM 元数据（接口成员带 body 的 MethodImpl 映射、reabstraction、`RuntimeFeature.DefaultInterfaceImplementation`）：`.vbx` 编译器须识别，否则委托缺口诊断会对"有默认实现的接口"误报缺口，接口演化时 VB 侧断裂（决策文件 M7；vblang 2018.02.21 的 "serious problem" 警告）。
   - C# 13 `allows ref struct` 泛型反约束 flag（`CorGenericParamAttr.gpAllowByRefLike(0x0020)`）与 `[UnscopedRef]` 接口成员：绑定 C# 13 泛型接口时须解析，最低要求是不崩、报可理解错误。
   - 与 `proposal-delegate-enhancements` / `proposal-implicit-interface-implementation`（companion）的"识别新元数据"建议合并跟踪。
4. **合成转发方法的元数据形状与 C# 手写 composition 完全一致**：编译器为 `Private _p As Provider Implements I` 生成的转发桩，与 C# 手写 `void IA.M() => _provider.M();` 落盘元数据相同（显式接口实现 + `MethodImpl`）——`.vbx` 程序集在 C# 生态里表现为"普通显式实现"，无需任何 VB-specific marker。这是跨语言互操作零成本的关键。
5. **跟踪 roles 复活风险**：C# extensions/roles 工作组把 "interface-implementing extensions" 搁置（运行时表示困难）；若 C# 15/16 复活，VB 字段委托与 roles 是同一需求的两条解法，届时再评估对齐/桥接（索引 T8，OPEN QUESTIONS）。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION 1（整体委托 + 宿主显式覆盖）不受影响，且获 C# 先例背书**：C# "A class implementation of an interface member should always win over a default implementation in an interface" 与本提案"宿主显式成员优先于字段委托"是同一优先级抽象——本提案的"部分委托"重写与 C# 解决 diamond 冲突的原则同构。
- **RESOLUTION 2（v1 仅类类型 provider）获生态背书**：C# 对 struct + DIM 的 boxing 问题明确选择 "leave it as a wart" 且永不修改（"It would be a breaking change to do 2 later, so we can never change it"）；本提案值类型 provider 的 `constrained` 转发属 VB 独有、无 C# 借鉴，v1 禁用的判定正确。OPEN QUESTION（值类型转发是否值得）应参照 C# 的"不可逆"教训：若未来做，须先定不可逆的语义。
- **RESOLUTION 3（成员归属优先级）与 C# most-specific-implementation 同构**：可引用 C# 规则措辞作为 spec 起点（"类/结构实现优先于接口默认"→"宿主显式成员优先于字段委托优先于隐式同名"）；但注意 C# 规则解决的是"接口默认 vs 类实现"的菱形，本提案解决的是"多个归属源"的并集——是同构而非复制。
- **RESOLUTION 4（委托缺口诊断）在 DIM 下须放宽**：接口成员有默认实现时，集合差应排除之（C# "实现者可依赖默认实现"语义下缺口为 0）；这与正文 Q&A 第 4 项"DIM 落地后再对齐"一致，本附录给出 C# 侧依据：C# 语义就是"实现者可以依赖默认实现"。
- **RESOLUTION 7（DIM 交互 Table）被生态确认，且可提前定一条规则**：显式字段委托 > DIM（与 C# "类实现赢接口默认"同向，正文 Q&A 第 4 项已答"是——显式委托覆盖默认实现"）；"未覆盖成员继承默认实现"的放宽在 C# 语义下成立。其余（属性载体、事件转发、值类型、`Shared`）维持 Table。
- **RESOLUTION Implication（Source Generator 对照 POC）被强化**：C# 生态里接口样板的官方答案就是生成器，`<DelegateTo>` POC 不是可选项而是前置条件——先证明工具能否交付 80% 价值，再谈语法。
- **三态 Consider 维持**。补充口径：本提案与 C# interop 关系"兼容为主、无冲突"——它是 C# 未做的语言级差异化；唯一桥接点在消费 C# DIM / ref struct 接口元数据的既有缺口，不构成采纳阻力。

### 引用纪律与未决项

已核实并可逐字引用的 C# 原文（全部在 `..\..\csharplang` 镜像中核对）：

| 原文（节选） | 来源 |
|---|---|
| "Add support for _virtual extension methods_ - methods in interfaces with concrete implementations. A class or struct that implements such an interface is required to have a single _most specific_ implementation for the interface method..." | `proposals\csharp-8.0\default-interface-methods.md`（Summary） |
| "We require that every interface and class have a *most specific implementation* for every virtual member among the implementations appearing in the type or its direct and indirect interfaces." | 同上（The most specific implementation rule） |
| "`T2` is an interface type but `T1` is not an interface type." | 同上（The most specific implementation rule） |
| "A class implementation of an interface member should always win over a default implementation in an interface, even if it is inherited from a base class." | `meetings\2017\LDM-2017-04-19.md`（Diamonds with classes） |
| "It is very hard to use default implementations from structs without boxing." / "Let's stick with option 3. It would be a breaking change to do 2 later, so we can never change it." | 同上（Structs and default implementations） |
| "The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET." | `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation） |
| "Default interface methods pose a problem for `ref struct` as there are no protections against the default implementation boxing the `this` member." / "To handle this a `ref struct` will be forced to implement all members of an interface, even if they have default implementations." | 同上（Detailed Design → ref struct interfaces） |
| "If extension members could somehow help a type implement an interface without the involvement of that type, this would facilitate similar adaptation capabilities to what type classes provide in Haskell, and would greatly aid software composition." | `meetings\working-groups\extensions\the-design-space-for-extensions.md` |
| "This approach ran into several consecutive setbacks: We couldn't find a reasonable way to represent interface-implementing extensions in the runtime." | 同上 |
| "The type-based syntax lends itself to a future where extensions implement interfaces on behalf of underlying types." | 同上（Interface implementation） |
| "The implication is that people will then evolve interfaces, and if VB can't handle this, it will have a serious problem." | `..\..\vblang\meetings\2018\vbldm-notes-2018.02.21.md`（DIM 议程） |

**OPEN QUESTIONS / 未核实项**：
- extensions / roles 的 "interface-implementing extensions" 是否会在 C# 15/16 复活：工作组成员文档（2024 语境）显示已搁置，但未见正式 LDM 决议记录；若复活，VB 字段委托与之的关系需重估（**Suspect**）。
- C# 14 extensions 提案本体（`proposals\csharp-14.0\extensions.md`）是否保留"扩展类型可实现接口"的远期条目：未深挖（**Suspect**——本附录只核对了 `the-design-space-for-extensions.md` 工作组成员文档）。
- VB 编译器（Roslyn VB）当前对 C# 8 DIM 元数据与 `allows ref struct` 约束的具体 import 行为未在本仓库核实（**Suspect**：与决策文件 M7 / `meeting-default-methods.md` 附录同源缺口）。
- 本提案与 C# DIM 交互的最终规则（显式委托是否覆盖默认实现、未覆盖成员是否继承默认实现）已在正文 RESOLUTION 7 Table——需在 DIM 落地时按本附录"可提前定的那条规则"先行定稿。
