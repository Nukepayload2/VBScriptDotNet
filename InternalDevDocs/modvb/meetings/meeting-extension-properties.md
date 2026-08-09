# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周议题是扩展属性——Anthony 把既有的扩展方法机制推广到属性上。讨论比预想的更有根据：我们翻规范时发现，VB 规范里**早已两次出现"extension property"这个措辞**（Await 契约与 XML 轴），所以这不是外来语法，而是"把规范语言已经允许的东西开放给用户书写"。但同样的发现也让我们对绑定规则加倍谨慎。

## Agenda

* [Proposal: 扩展属性（Extension Properties）](#proposal-扩展属性extension-properties)

## Proposal: 扩展属性（Extension Properties）

_Related: ModVB `proposal-extension-properties.md`；vblang 规范 `expressions.md` §XML Member Access / §Await Operator；vblang 规范 `overload-resolution.md` §Overload Resolution；[vblang #228 – Default property for reading and setting bit flags](https://github.com/dotnet/vblang/issues/228)；[LDM-2014-02-17 #39 – Extension statics](https://github.com/dotnet/vblang/issues/__EXT_STATICS__)_

> **来源标注**：本节引用 vblang 主线文件的具体措辞均已逐字核对（标注文件名:行号）；`__EXT_STATICS__` 是**占位符**——2014 会议只记了"Extension statics 已离线讨论、笔记未整理"，未成文，我们不能编造 issue 编号，见下方 Suspect。

### 场景与缺口

今天 VB 只能为既有类型写扩展**方法**（`Function`/`Sub`），不能写扩展**属性**。对"派生视图/便捷取值"类需求（取第二个元素、取长度、取默认值），扩展方法逼我们写成 `list.SecondOrDefault()`；而属性是 VB 最自然的只读取值形态。我们认同的缺口陈述是：**读取侧声明式化**——`list.SecondOrDefault` 比 `list.SecondOrDefault()` 更接近"这是一个值"的直觉，也更像英语。

我们真正被说服的时刻是翻开规范：

- **Await 契约**（`expressions.md:4929`）逐字写着：
  > "`E` contains a readable **instance or extension property** named `IsCompleted` which takes no arguments and has type Boolean;"
- **XML 轴**（`expressions.md:4845`）逐字写着：
  > "The `AttributeValue` **extension method** (as well as the related **extension property** `Value`) is not currently defined in any assembly. If the extension members are needed, they are automatically defined in the assembly being produced."

也就是说，**VB 规范的语言契约已经承认扩展属性存在**——`Await` 依赖可等待类型暴露 `IsCompleted`，XML 轴在元素/属性访问时自动合成 `Value`。今天只是"规范允许、编译器内部合成、用户不可书写"。Anthony 的建议本质上是把编译器内部已实现的东西开放成一等公民语法。这让"这是不是 VB 的东西"的疑问基本消失。

**背景知识（Suspect）**：VB6 的 COM "extender 属性"概念（如 `ListBox.ColumnWidths` 这类由容器提供的伪属性）给了"属性可以不在类型声明里"的直觉遗产。但我们在 `..\..\vblang` 里检索不到任何对应的主线讨论，这条只能作为背景，不引用为证据。

### 候选方案

**PROPOSAL A — 只读扩展属性（按 Anthony 原文）。** `<Extension>` 特性 + `Module` 中声明 `ReadOnly Property`，接收者作为首个参数，泛型允许：

```vb
' 提案原文形态（依赖 ModVB 简写属性语法，见 Q&A #2）。
Module MyExtensions

    ' Note: Can be generic.
    <Extension>
    ReadOnly Property SecondOrDefault(Of T)(list As List(Of T)) As T
        Return If(list.Count > 1, list(1), Nothing)

End Module
```

调用形态（Anthony 未给出显式调用示例，语义与扩展方法一致）：

```vb
Dim second = someList.SecondOrDefault
```

**PROPOSAL B — 只读 + 可写扩展属性。** A 之上允许 `Set` 访问器，`list.X = value` 可写入。见 Q&A #3，我们强烈怀疑这一步。

**PROPOSAL C — 不做用户可写的扩展属性，维持编译器内部合成。** 规范已允许的（Await `IsCompleted`、XML `Value`）保持现状；用户需求继续用无参扩展方法表达。零新语法、零绑定风险，但"声明式读取"的缺口保留。

**PROPOSAL D — 用完整访问器块语法、独立于简写属性。** 在 A 的语义基础上，把示例写成完整的 `Get`/`End Get`/`End Property`，使本特性可单独落地、不被 ModVB 简写属性提案卡住。

### 权衡：Q&A

- **A vs C：为什么不维持现状？** 因为规范语言已经允许扩展属性，维持"编译器可合成、用户不可书写"是自相矛盾的边界——可等待类型若想自定义 `IsCompleted` 的语义，今天做不到；`SecondOrDefault` 类需求只能写方法。缺口真实。但 C 的保守提醒我们：**用户可写**是新增面，不是规范措辞的必然延伸。
- **A vs B：可写扩展属性成立吗？** 我们一致认为**不成立**。在他人类型上伪造 setter 语义危险且无合理语义——`list.SecondOrDefault = 42` 是什么？`Count = 5` 是什么？"便捷读取"的主场景全部是只读派生值。可写版本还需要设计 `Set(value)` 时接收者与隐式 `value` 参数并存的首参规则。**结论：B 否决（Table），v1 只读。**
- **A vs D：示例形态选哪个？** 提案原文示例缺 `Get`/`End Get`/`End Property`，只有在 ModVB 简写属性（`proposal-abbreviated-properties-events.md`）也落地时才可编译。我们不接受一个特性依赖另一个未定特性的语法才能演示。**结论：本特性示例一律用完整访问器块（D 形态），与简写属性解耦。** 这同时是品质审查的要求——示例必须独立可编译。
- **Q&A #1：`list.SecondOrDefault` 与同名无参扩展方法怎么区分？** 见下方"深度追问 §1"，这是全场最尖锐的绑定问题。
- **Q&A #4：泛型接收者的类型推断？** 扩展方法 currying 已处理——`expressions.md:975`："currying also removes any method type parameters that are a part of the type of the first parameter"，接收者 `List(Of T)` 推断出 `T` 并固定。属性复用同一机制即可，这是"实现成本低"的依据。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Property Name(Of T)(receiver As R) As U` 的声明语法无歧义——它复用既有属性声明的参数列表位置，只是参数角色变成"接收者"。真正的歧义在**读取侧**。

今天的 VB 里，`e.X` 其中 `X` 是无参 `Function` 时是**调用**。`expressions.md:1159` 逐字规定：
> "If the method group only contains one accessible method, including both instance and extension methods, and that method takes no arguments and is a function, then the method group is interpreted as an invocation expression with an empty argument list..."

现在引入同名扩展属性 `X`，`e.X` 就存在两种解读：无参方法调用 vs 属性读取。这是本特性**必须**先答的绑定优先级问题，提案未答。We see two candidate rules：

- **规则 1（兼容优先）**：同名无参扩展方法**继续获胜**（维持现状绑定），扩展属性仅在无同名无参方法时生效。优点：零破坏——一个库先发 `<Extension> Function X()`、后加 `<Extension> Property X`，消费者代码 `e.X` 的行为不变。缺点：扩展属性会被静默遮蔽，且"属性优先级低于方法"与直觉相反。
- **规则 2（直觉优先）**：属性读取获胜，无参方法需显式 `()`。缺点：这是**破坏性变更**——已编译的 `e.X`（当前=调用方法）在重编译后变成读取属性。

`Probably`：v1 取**规则 1**（兼容优先），并把"同名属性+方法并存"视为作者错误给警告。这符合设计原则 #1（永不破坏）压倒 #5（直觉）。**OPEN QUESTION**：警告策略与 `langversion` 门控的精确形态。

#### 2. 角案例与边界语义

- **示例可编译性（品质硬伤）**：提案原文示例缺 `Get`/`End Get`/`End Property`，仅在简写属性落地时可编译。我们要求本特性文档用完整块形式，示例必须能独立编译：

```vb
Imports System.Runtime.CompilerServices

Module MyExtensions

    <Extension>
    ReadOnly Property SecondOrDefault(Of T)(list As List(Of T)) As T
        Get
            Return If(list.Count > 1, list(1), Nothing)
        End Get
    End Property

End Module

Module Test
    Sub Main()
        Dim someList As New List(Of Integer) From {1, 2, 3}
        Dim second As Integer = someList.SecondOrDefault
        Console.WriteLine(second)
    End Sub
End Module
```

- **`Nothing` 接收者**：扩展属性是静态方法的语法糖——`list` 为 `Nothing` 时属性读取等价于调用静态 getter，不抛 `NullReferenceException`，与扩展方法一致（`expressions.md:1082` 前后对晚期绑定的排除同源）。`If(list.Count > 1, ...)` 在 `list` 为 `Nothing` 时会抛 `NullReferenceException`——这是**示例本身**的语义，不是特性的语义，文档应注明。
- **值类型接收者**：`Structure` 接收者经 currying 后按值传递，getter 内修改副本不影响原值；与扩展方法的 copy-in 规则对齐。需写进 spec（`Probably`）。
- **条件访问 `list?.SecondOrDefault`**：扩展属性应与 `?.` 组合，`Nothing` 短路。边界未定，列入 OPEN。
- **链式 `a.b.c`**：`a.P1.P2` 其中 `P1`、`P2` 均为扩展属性——每级按扩展属性绑定，语义模型应给出每级的收窄类型。无新机制，但需在 semantic model 验证。
- **`With` 语句**：`With someList : Dim s = .SecondOrDefault : End With`——扩展属性应参与 `With` 的成员访问。列入原型验证。

#### 3. 作用域与绑定

语义模型在 `someList.SecondOrDefault` 处应返回**属性符号**（property symbol），绑定到 getter，而不是 method group。收集规则复用扩展方法收集（`expressions.md:900`：从内到外查嵌套类型 → 命名空间 → Imports → 编译环境）；**收窄条件**应沿用"目标类型到首参类型存在 widening native conversion"（`expressions.md:907`），否则不收集。属性无"参数"只有接收者，因此**不存在方法参数的 overload 维度**——同名扩展属性收集后是 property group，需定义与同名无参方法的优先级（§1 规则 1/2）。

**泛型约束**：扩展方法的 currying 会应用"除 `New` 外的所有约束"，约束不满足或依赖未推断类型参数时忽略（`expressions.md:1004`）。扩展属性同样适用——`SecondOrDefault(Of T As Structure)` 对 `List(Of String)` 不收集。建议直接继承该规则。

#### 4. 与既有特性的交互

- **实例成员优先**：这是非破坏性的根基。`overload-resolution.md:89` 逐字规定：
  > "Extension methods are ignored if there are applicable instance methods to guarantee that adding an import (that might bring new extension methods into scope) will not cause a call on an existing instance method to rebind to an extension method."

  扩展属性必须继承同一保证：**类型上若有可用的实例属性/方法，扩展属性不参与**。这也是"加一个 Imports 不改变既有绑定"的既有承诺。
- **Late binding / Option Strict Off**：`expressions.md:1082` 规定扩展方法在晚期绑定时不被考虑（`o.M1()` 对 `Object` 忽略扩展方法）。扩展属性必须对齐——`Dim o As Object = someList : o.SecondOrDefault` 保持晚期绑定，忽略扩展属性，否则会改变运行期语义（编译期静态调用 vs 运行期 `MissingMemberException`）。这是**两条编译路径必须一致**的关键点。
- **`For Each` / `Await`**：规范已在 `statements.md:1349`（`GetEnumerator`）、`expressions.md:4929`（`IsCompleted`）允许"instance, shared or **extension**"成员参与模式。扩展属性作为一等成员后，`IsCompleted` 这类契约的**自定义实现**成为可能——这是特性带来的真实新价值（可等待类型不必是编译器合成）。列入后续设计。
- **`NameOf`**：`nameof(someList.SecondOrDefault)` 应绑定扩展属性符号；vblang 2014-10-23 的 nameof 讨论已把"extension members"纳入绑定（`LDM-2014-10-23.md`），扩展属性对齐即可。
- **集合初始化器 `From`**：`Add` 已含扩展方法（`expressions.md:1375`）；扩展属性不参与集合初始化，无冲突。

#### 5. Breaking change 与兼容性

唯一 breaking 面是 §1 的**同名无参扩展方法 vs 扩展属性**的优先级。若取规则 1（方法获胜），非破坏；若取规则 2，破坏。另有一个隐藏面：**新引入的扩展属性**可能改变某同名**实例成员遮蔽**后的解析——但这由"实例优先"规则兜底（实例属性/方法永远优先），不构成变更。

还有第三个更隐蔽的面：**语义模型与 IDE**。新特性让 `e.X` 的符号类型从"method group（被当调用）"变为"property group（被当读取）"，Find All References / rename / IntelliSense 的符号身份变化是必然的，需要 IDE 团队确认成本。这不算语言级 breaking，但算工具链 breaking。

**兼容性结论**：规则 1 + 实例优先 ⇒ 无语言级 breaking；仍需 `langversion` 门控让新旧行为可审计。**旧代码行为：不变。**

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径必须一致：晚期绑定路径（`Object` 接收者）在两条路径下都忽略扩展属性；早期绑定路径在两条路径下都用同一收集与优先级规则。宽松路径不得因"扩展属性"改变既有的晚期绑定结果——这是设计原则 #7 的直接要求。

#### 7. IDE / IntelliSense 影响

补全应把扩展属性显示为属性 glyph（而非方法），并标注"扩展"来源（module + 接收者类型），避免与实例属性混淆。`ByRef` 不适用（属性无 ByRef）。InfoTip 对 `e.X` 应显示"Extension property (from MyExtensions)"。原型必须覆盖 semantic model `GetSymbolInfo` 返回 property symbol、补全列表、签名帮助。这些不做进规范等于没设计。

#### 8. 数据 / 普遍性

"便捷读取"是真实高频需求，但我们**没有量化数据**证明"扩展方法写法 `X()` 是痛点、属性写法 `X` 是刚需"。vblang 2018-02-21 对位标志枚举的讨论（[#228](https://github.com/dotnet/vblang/issues/228)）留下一句逐字引文：
> "An enum constraint by itself does not allow this to be solved via extension methods. Darn."

——那是个**方法不够用**的例子，但扩展属性也解决不了它（它需要的是 API 层或枚举约束）。We `Suspect`：扩展属性的真实高频受益场景（`SecondOrDefault` 这类派生读取）可以用无参方法无损表达，差异是**仪式感**而非**能力**。这把本特性的普遍性证据等级压低。

#### 9. 更简替代

- **无参扩展方法 `list.SecondOrDefault()`**：能力完全等价，今天可用，零新语法。本特性只省一对括号。
- **`Default` 属性 / 包装类型**：对"接收者上的便捷读取"的既有 workaround，样板更多。
- **编译器内部合成（PROPOSAL C）**：对 Await `IsCompleted` 已够；对用户自定义场景不够。
- **Analyzer / 重构**：可以提示"此读取可写作扩展属性"，但不能让代码以属性形态编译。

我们认为"更简替代"能覆盖**大部分**场景，但覆盖不了"规范已允许的自定义契约成员"（`IsCompleted` 自定义）和"声明式读取"的审美诉求。价值是真实的，量级是温和的。

#### 10. 复杂度 / 成本 / 优先级

实现面集中在：成员收集（复用扩展方法收集）、property group 与无参 method group 的优先级、语义模型/IDE。**核心机制已在编译器内部存在**（XML 轴合成 `Value`、Await 契约读 `IsCompleted`），开放成用户语法不是从零造引擎。成本**中低**。优先级：排在模式匹配、可空性流分析之后——本特性是"锦上添花的对称性"，不是头条特性。

#### 11. 运行时 / CLR 硬约束

扩展属性在 CLR 无直接表示——没有"扩展属性"元数据。自然发射路径是 **`get_X`/`set_X` 静态方法 + `<Extension>` 特性**（XML 轴 `AttributeValue`/`Value` 的合成先例）。因此 C# 侧消费时看到的是**方法**（`get_SecondOrDefault`），不是属性。Anthony 原文把 "C# 'Extension everything' story/interop" 明确列为 **Not shown**（`..\AnthonyDesign_wordpress.txt:2588`）。**结论：CLR 无碍、PEVerify 无碍，但跨语言形态是真实缺口——VB 声明扩展属性、C# 消费者只能用方法形式。** OPEN QUESTION。

#### 12. 值不值得做

- **价值**：声明式读取 + 规范契约自定义（`IsCompleted`）+ 对称性。温和但真实。
- **成本**：中低（机制已存在）。
- **风险**：绑定优先级若取规则 1（兼容优先）则低；若不明确则高。
- **判定**：**值得做——但只在绑定规则明确、示例独立可编译的前提下。** 若只接受"按 Anthony 原文照单全收"，我们会建议不做。

### VB 基因对照

- **保持 VB-like（原则 #2）**：`Module` + `<Extension>` + `Property` 全是既有 VB 语法，无外来形态；相对 C#（无扩展属性）反而是 VB 领先。**一致。**
- **不引入"第二种做事方式"（原则 #3）**：**最大张力。** `list.X` 与 `list.X()` 已经是 VB 的两种既有写法（属性读取 vs 无参方法调用）；扩展属性没有创造第三种，只是把"属性读取"扩展到扩展成员。但同名冲突的优先级（§1）若处理不当，就是在给同一表达式造两套语义。**此条是全场拷问最重的一条。**
- **读起来像英语、对新手友好（原则 #5）**：`list.SecondOrDefault` 比 `list.SecondOrDefault()` 更接近"名词即值"的直觉。**一致。**
- **永不破坏（原则 #1）**：依赖"实例优先 + 规则 1（方法获胜）"双保险。无此则 Rejected。
- **避免隐蔽语义变化（原则 #7）**：`Return?` 被拒的教训在此适用——`e.X` 的解读变化（调用→读取）正是隐蔽语义变化。规则 1 正是为对冲它而设。
- **冗长只在有用时是美德（原则 #10）**：去掉 `()` 在"明确是值"时是"有用时的简洁"。**一致。**
- **与主线关系（对照表 2.3）**：主线对扩展设高门槛（2.3 表"主线保守，Anthony 激进"），但规范措辞已在两处承认"extension property"——本建议**借主线规范既有概念**，属 **Anthony 独立延伸、方向与规范措辞一致**，不与主线冲突。主线无用户可写扩展属性的成文计划（2014-02-17 #39 "Extension statics" 仅离线讨论、笔记未整理）。

### RESOLUTION:

1. **概念方向认可**：扩展属性与 VB 规范既有措辞一致（`expressions.md:4929` Await 契约、`expressions.md:4845` XML 轴），非外来语法。**但提案按现状不通过**——缺绑定规则、缺可编译示例。
2. **v1 = 只读扩展属性（PROPOSAL A ∩ D）**：仅 `Get`，接收者为首参，泛型允许（复用扩展方法 currying 的类型推断与约束应用）。示例一律用完整访问器块（`Get`/`End Get`/`End Property`），与 ModVB 简写属性提案解耦。
3. **绑定优先级（必写进 spec）**：
   - **实例成员优先于扩展成员**（继承 `overload-resolution.md:89` 的非破坏承诺）；
   - **同名无参扩展方法优先于扩展属性（规则 1，兼容优先）**，扩展属性仅在无同名无参方法时生效；同名并存给作者警告；
   - **晚期绑定忽略扩展属性**（对齐 `expressions.md:1082`），Option Strict 两条路径一致。
4. **可写扩展属性（Set）→ Table**：在他人类型上伪造 setter 语义危险且主场景不需要；`list.X = value` 的首参规则留待未来单独设计。
5. **C# 互操作形态 → OPEN**：发射为 `get_X`/`set_X` 扩展方法是自然路径；C# 消费者只见方法。Anthony 原文已把 "C# Extension everything" 列为 Not shown，本特性不替 C# 做决定。
6. **语义模型 / IDE**：`GetSymbolInfo` 返回属性符号；补全显示扩展属性 glyph；InfoTip 标注来源 module。原型验证。

### Implication:

- 撰写最小原型：只读扩展属性 + 收集复用 + 规则 1 优先级 + 晚期绑定排除；验证语义模型 `GetSymbolInfo` 与 IDE 补全。
- 起草 speclet：扩展属性收集（复用 `expressions.md:900` 收集序）、property group vs 无参 method group 优先级、`Nothing`/值类型接收者、`?.`/`With`/链式组合、`langversion` 门控与同名并存警告。
- 补一份 Compatibility 分析：实例优先 + 规则 1 的语言级非破坏论证、工具链 breaking（符号身份变化）逐条列证。
- 与简写属性提案团队对表：确认本特性不依赖该提案；示例改用完整访问器块。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：同名无参扩展方法与扩展属性并存的警告策略、`langversion` 门控精确形态（规则 1 的配套）。
- `OPEN QUESTIONS`：`list?.SecondOrDefault` 条件访问与扩展属性的组合语义（`Probably`：与扩展方法一致，`Nothing` 短路）。
- `OPEN QUESTIONS`：C# 互操作形态——发射 `get_X` 方法后，VB 侧 `e.X` 如何与 C# 侧 `e.get_X()` 保持可预测映射；是否提供显式 `get_` 调用入口。
- `OPEN QUESTIONS`：`With` 内 `.SecondOrDefault`、链式 `a.P1.P2` 的语义模型符号形态。
- `TODO`：为"便捷读取 vs 无参方法"的普遍性补数据（真实代码库中 `X()` 无参扩展方法的占比），把 §8 的 Suspect 升级或降级。
- `Follow-up`：与可等待契约（`IsCompleted` 自定义）和 XML 轴（`Value` 合成）统一：哪些场景继续编译器合成、哪些开放用户书写。
- `Suspect`：VB6 extender 属性（`ListBox.ColumnWidths`）作为遗产直觉，未在 vblang 主线检索到对应讨论，仅背景。

### 状态

- **LDM 状态：Consider**——概念方向认可（规范已承认），但提案文档需返工：绑定规则、可编译示例、兼容性分析补齐后，只读子集可上调 **Active**；可写与 C# 互操作形态维持 **Table**。
- **三态判定：Consider** — 价值真实（声明式读取 + 契约自定义）、成本中低（机制已存在）、风险可控（规则 1 + 实例优先双保险）；但提案证据止于书面、绑定歧义未答，未达到 Active 门槛。

---

## 附录：特性评价

# 建议评价报告：proposal-extension-properties.md

## 评价对象

- 建议：proposal-extension-properties.md — 扩展属性（Extension Properties）
- 来源：Anthony 原文第 15 章 "Inheritance, Interface Implementation, and Extension"（`..\AnthonyDesign_wordpress.txt` L2512–2520；同一章 L2588 把 "C# 'Extension everything' story/interop" 列为 Not shown）
- 配方目标：把扩展方法机制推广到只读属性，`list.SecondOrDefault` 式声明式读取；可泛型

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。读取主场景明确可演示，但可写子效果缺失（Table）、无原型/运行证实、≥4 未决问题把证据封顶 | 已检查 | 无原型；示例不能独立编译（依赖简写属性）；"方法已可表达、只省括号"使改进强度偏温和 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"。Module+`<Extension>`+Property 全为既有 VB 形态，复用扩展方法收集/currying，极 VB；唯一杂质是示例捆绑简写属性、同名冲突优先级未消化 | 已检查 | 同名无参方法 vs 属性的绑定歧义未答（§1）；示例捆绑另一未定特性；若不解除简写依赖则降至 3 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、4 个未决问题具体诚实；但 Detailed design 无文法/绑定规则、示例缺 `End Get`/`End Property` 不能独立编译、无 Compatibility 章节、状态行占位链接 | 已检查 | 无 BNF/绑定优先级 spec；示例不可编译（性质最重的扣分项之一）；无 breaking 分析；状态行 `PROTOTYPE_OWNER/...`、`pr/1` 占位 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（迭代提速）、光（相对 C# 差异化、规范契约自定义）正向；暗（绑定歧义 breaking 风险、C# 互操作洞、工具链符号身份变化）被 Drawbacks 提及但无对冲设计 | 已检查（预测待定） | 风=同名方法/属性歧义未决，实际影响须"已采纳"后定；暗风险无对冲方案（规则 1 是本文补的，非提案自带） |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 15 章但未标注章节；未点明规范已有"extension property"措辞（Await/XML 轴）这一关键先例；未说明发射为 `get_X` 的 CLR 形态；无借鉴 C#（Anthony 本就"故意不跟 C#"），但 C# 互操作列为未决 | 已检查 | 关键先例（规范已允许）未引用；VB6 extender 遗产未提；与简写属性的依赖未声明（即"混入无关特性未说明"） |

## 设计原则对照

- **与 VB 基因：大体一致**（保持 VB-like、读起来像英语、复用既有扩展机制）；主要偏离风险在**原则 #3（不引入第二种做事方式）**——同名属性/方法冲突若处理不当即违背；**原则 #7（避免隐蔽语义变化）**靠"规则 1（方法获胜）+ 实例优先"对冲，此为本文补入、提案未自带。
- **与主线关系：Anthony 独立延伸，方向与主线规范措辞一致**——规范已在 Await 契约与 XML 轴使用"extension property"，非与主线冲突；但主线对扩展设高门槛（2.3 表），用户可写扩展属性超出主线任何成文计划（2014-02-17 #39 Extension statics 仅离线讨论、笔记未整理）。借用规范概念是合理锚点，但不等于主线背书。
- **破坏性变更：语言级无（在规则 1 + 实例优先下）；工具链级有**——`e.X` 的符号身份从 method group 变为 property group，Find All References/rename/补全需适配。提案未分析。

## 总评

- **达成程度：部分达成**——概念成立且有规范措辞先例支撑；绑定规则、示例可编译性、兼容性/互操作分析未完成。
- **LDM 三态建议：Consider**——只读子集（PROPOSAL A ∩ D）补足绑定规则与可编译示例后可上调 Active；可写扩展属性与 C# 互操作形态为 Table。
- **主要问题**：① 同名无参方法 vs 扩展属性的绑定优先级未答（最大设计缺口）；② 示例缺访问器块、依赖简写属性，不能独立编译；③ 无 Compatibility/breaking 分析、无文法；④ C# 互操作形态是真实缺口（CLR 无扩展属性元数据）；⑤ 普遍性证据弱——"只省一对括号"的能力增益温和。

## 返工建议

- **补充章节**：文法（属性声明的参数角色 + 读取侧绑定）；绑定优先级规则（实例优先 + 规则 1 方法获胜 + 同名并存警告）；Compatibility/breaking（工具链符号身份变化、`langversion` 门控）；Option Strict 分叉（晚期绑定排除）。
- **补充证据**：最小原型（只读扩展属性 + 收集复用 + 规则 1 + 晚期绑定排除）；语义模型 `GetSymbolInfo` 返回 property symbol、IDE 补全 glyph 验证；无参扩展方法占比数据。
- **未决问题处理**：`?.`/`With`/链式组合列 OPEN；可写→Table；C# 互操作→OPEN（发射 `get_X` 方法 + 映射规则）；同名冲突→v1 取规则 1。
- **设计探索**：与 Await 契约（`IsCompleted` 自定义）和 XML 轴（`Value` 合成）统一"编译器合成 vs 用户书写"的边界；与简写属性提案解耦并更新示例。

---

## 附录：C# 生态与互操作考量

> 依据 `..\..\csharplang-index.md`（T8 extensions / M7）与 `..\..\csharplang` 原文逐字核实。本附录只追加、不改写正文；C# 原文引用逐字标注来源（路径风格与索引一致，行号以本次核对为准）。

### 相关 C# 现实方向

本提案（VB 扩展属性）在 C#/CLR/.NET 生态的直接对应物是 **C# 14 的「扩展成员」（extension members）**——它明确**包含属性**（实例与静态），且已随 .NET 10 / VS 2026 v18.0 发布：

- **C# 14 已含扩展属性**（`Language-Version-History.md:5`）："Extension methods and properties: allows extending an existing type with instance or static methods and properties."
- **解析规则与 VB 的 currying 直觉同源**（`proposals\csharp-14.0\extensions.md:362`）："Extension properties will be resolved like extension methods, with a single parameter (the receiver parameter) and a single argument (the actual receiver value)."
- **元数据编码是"双轨"**（extensions.md:592–608、示例 :783–789）：扩展属性在 extension grouping type 内保留**骨架 CLR 属性**（标 `[ExtensionMarkerName]`、body 替换为 `throw`），同时在顶层静态类发射 **`get_P`/`set_P` 静态实现方法**；实例扩展属性的实现方法在首参位置前插接收者。示例逐字：
  ```
  // Implementation for Property2
  public static int get_Property2<T>(IEnumerable<T> source) { ... }
  public static void set_Property2<T>(IEnumerable<T> source, int value) { ... }
  ```
- **实现 getter/setter 不标 `[Extension]`**（extensions.md:1147）："Confirm we should add `[Extension]` attribute to implementation getters and setters too. (answer: no, LDM 2025-03-10)"——只有"实例普通方法"的实现方法才标 `[Extension]`（extensions.md:608；"If the original member is an instance ordinary method, the implementation method is marked with an `[Extension]` attribute."）。
- **方法 vs 属性的绑定歧义在 C# 未决**（extensions.md:1227–1228）：
  > "Confirm that we're okay with having an ambiguity when both methods and properties are applicable (answer: we should design a proposal to do better than the status quo, punting out of .NET 10, LDM 2025-06-23)"
  > "Confirm that we don't want some betterness across all members before we determine the winning member kind (answer: punting out of .NET 10, WG 2025-07-02)"
  同次会议结论（`meetings\2025\LDM-2025-06-23.md:93`）："Ambiguity rules will be left in place while we work on creating rules for making code work as the user would expect."；当日 Quote of the Day（`:13`）侧面流露倾向："So we pick the method" / "But the spec says that should have been an ambiguity"。
- **`get_Prop` 显式入口的裁决**（`meetings\2025\LDM-2025-03-10.md:43`–`:49`）：允许 `C.get_Prop(new object())` 作 disambiguation 语法，**拒绝** `new object().get_Prop()` 作为扩展方法形式——Conclusion 逐字："`get_Prop` form is disallowed in extension method form."。提案侧同场景示例 `_ = E1.get_P(new object());`（extensions.md:338）。
- **模式构造参与面**（extensions.md:1046–1057）：扩展属性**参与**对象初始化器、字典初始化器、`with`、属性模式；**排除** `foreach` 的 `Current`、**`await` 的 `IsCompleted`**、list-pattern / 隐式索引器的 `Count`/`Length`。
- **其它限制**：ORPA 复制到属性实现方法（extensions.md:446）；`init` 访问器禁用（`:173`）、`readonly` 修饰符禁用（`:172`）；`nameof` 扩展属性从 .NET 10 移出（`:1025`）。
- **跨语言消费"稍后指定"**（`meetings\working-groups\extensions\extensions_v2.md:150`、`:152`）：
  > "The extension itself (E) will get emitted exactly as a static class would be that contains extension methods (allowing usage from older compilers and other languages without any updates post this mechanical translation)."
  > "New extension members (beyond instance members) will need to have their metadata form decided on. Consumption from older compilers and different languages of these new members will be specified at a later point in time."

### 现实 vs 提案

| 本提案维度 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| 发射 `get_X`/`set_X` 静态 + `<Extension>`（§11） | C# 14 扩展属性实现方法也是 `get_P`/`set_P` 静态方法（extensions.md:783–789） | **兼容（一半）** | 发射形态吻合；但 C# 不给属性 getter/setter 标 `[Extension]`，且额外发射 grouping/marker 骨架——VB 方案只对齐了"实现方法"半截，未对齐骨架/标记元数据。 |
| "相对 C#（无扩展属性）反而是 VB 领先"（VB 基因对照） | C# 14 已发布扩展属性 | **冲突（过时）** | 主张不再成立；真实差异退化为**形态**（Module+`<Extension>`+首参 vs extension block+receiver spec）与**元数据**（经典静态方法发射 vs grouping/marker 发射）差异。 |
| 同名方法 vs 属性：规则 1 方法获胜（§1） | C# 保留歧义、betterness 移出 .NET 10 | **需桥接** | VB 选规则 1 比 C# 现状更果断；但同一表达式在 C# 侧重编译可能报歧义（delegate-返回属性 + 方法并存场景即报错，extensions.md:1229–1244），混合语言解决方案文件里两侧绑定行为不一致。 |
| Await `IsCompleted` 扩展属性自定义（§4、§12） | C# 显式**排除**扩展 `IsCompleted` 参与 await（extensions.md:1056） | **冲突** | VB 规范允许、C# 拒绝；跨语言"可等待类型"契约在扩展属性面不一致。 |
| `get_` 显式入口（OPEN QUESTION） | 允许 `C.get_Prop(r)`、拒绝 `r.get_Prop()`（LDM-2025-03-10:49） | **需桥接** | VB 若提供显式 `get_` 入口，应与 C# 的 disambiguation 语义对齐。 |
| C# 消费者只见方法（§11 预测） | C# 元数据设计证实，且 C# 侧跨语言消费亦"稍后指定" | **兼容（预测证实）** | 会议预测被证实；且缺口是双向的——C# 扩展属性对 VB 消费者同样不保证"属性形态"。 |

### 对 VBScript.NET 的适应建议

- **识别 C# 14 新元数据（必须桥接）**：`.vbx` 编译器须认识 `ExtensionMarkerNameAttribute`、extension grouping/marker type（specialname、内容基名）、`<Extension>$` marker method，避免把骨架属性当普通属性、避免把实现方法当扩展方法（getter/setter 不标 `[Extension]`）。这与决策文件 M8 的"认识 RequiresUnsafe/MemorySafetyRules 类新元数据"同属一类义务——C# 每引入一种新元数据编码，VB 就得有对应的识别/忽略策略。
- **消费 C# 14 扩展属性为属性形态（可选增强）**：在收集规则上叠加一个 reader——经 `[ExtensionMarkerName]` 定位 grouping/marker type，把骨架 CLR 属性映射为 VB 扩展属性符号，`e.P` 编译为对 `E.get_P(e)` 的静态调用。成本中等；非 v1 必需，可列入原型之后。
- **被 C# 消费：务实走经典发射**：维持 `[Extension] get_X` 经典扩展方法发射（C# 侧以 `list.get_X()` classic 扩展方法调用、或 `C.get_X(list)` 显式 disambiguation 调用）；要获得 C# 侧 `list.X` 属性形态，需按 C# 14 grouping/marker 格式发射——成本高，长期可交给**互操作 source-gen 桥**（读对方元数据、生成适配成员），符合决策文件 M5/T6"source-gen 桥"路线。
- **默认安全/按需动态**：扩展属性是编译期静态分派，与 AOT/trimming（T5/T6）无冲突，适合作为 `.vbx` "默认安全"面；无需走 `Any`/晚期绑定动态路径。
- **绑定优先级写入 spec 并标注跨语言差异**：规则 1（方法获胜）是 VB 的独立设计决策，比 C# 现状（歧义报错）更果断；应在 spec 注明"C# 侧同一表达式可能为歧义错误"，避免混合语言文件行为不一致。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #5（C# 互操作形态 → OPEN）应更新**：C# 14 已发布扩展属性，其实现方法正是 `get_P`/`set_P` 静态方法（与 VB 自然发射吻合），实例属性 getter/setter 不标 `[Extension]`，且 C# 额外发射 grouping/marker 元数据；因此"VB 发射 `get_X` + C# 消费者只见方法"的预测**被证实**。需补充两条：C# 亦为自身扩展属性发射同一形态的 `get_P`/`set_P`；C# 侧对新成员元数据的跨语言消费"稍后指定"（extensions_v2.md:152）——缺口是双向的，不只是 VB 侧的问题。
- **"VB 领先 C#（无扩展属性）"（VB 基因对照）过时**：建议改写为"形态与元数据差异"（VB 简洁声明形态 vs C# extension block；经典发射 vs grouping/marker 发射），不要再以"有无"作为差异化论据。
- **三态判定维持 Consider**：C# 现实的注入不改变价值/成本/风险三角，但**削弱"差异化/领先"论据**；"`IsCompleted` 自定义"价值被 C# 的排除决策部分对冲（跨语言契约不一致）；同时 C# 也把方法/属性歧义移出 .NET 10，说明该问题在两侧都是真实未决项——VB 抢先给规则 1（方法获胜）是加分项，且与 C# 的"倾向 pick the method"直觉同向。
- **OPEN QUESTION（`get_` 入口）获得 C# 锚点**：对齐 `C.get_P(r)` 允许 / `r.get_P()` 拒绝，作为互操作映射的默认规则。

### OPEN QUESTIONS（本附录新增）

- `OPEN QUESTION`：VB 是否要按 C# 14 grouping/marker 元数据发射，以获得 C# 侧 `list.X` 属性形态——成本与收益未量化，列为长期互操作投资项。
- `OPEN QUESTION`：C# 后 .NET 10 的 betterness 规则最终取"方法获胜"还是"属性优先"——将影响与 VB 规则 1 的跨语言一致性（两侧同为方法获胜则一致，否则分叉）。
- `OPEN QUESTION`：C# 是否会放开扩展 `IsCompleted` 参与 await（当前明确排除，extensions.md:1056）——若放开则与 VB Await 契约收敛。
- `Suspect`：C# 未来是否会提供"把经典 `[Extension] get_X` 扩展方法识别为属性"的兼容读取路径，本库无证据，标 Suspect 不引用为事实。
