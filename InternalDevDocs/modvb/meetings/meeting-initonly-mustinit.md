# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次讨论 `InitOnly` / `MustInit` 属性——一份把"初始化期可写、之后只读"与"必填成员"两个痛点打包进两个新属性修饰符的建议。开场我们承认这是本周最容易产生"听起来都懂、落地全不懂"的主题：`ReadOnly` 属性与构造内赋值的规则主线 2014 年就批准过（LDM-2014-10-01），C# 也在 `init`（C# 9）与 `required`（C# 11）上给出了完整答案，我们满以为这里只有"命名之争"。但随着讨论推进，我们撞上了比预想硬得多的墙：**对象初始化器在构造之后执行**这一 VB 事实，让 `InitOnly` 的存储模型几乎无法复用 CLR 的 `initonly` 语义——而建议里的核心示例恰好正是这么写的。这场会议大部分时间都花在"把示例拆开看它到底能不能编译"上。

## Agenda

* [Proposal: InitOnly / MustInit 属性](#proposal-initonly--mustinit-属性)

## Proposal: InitOnly / MustInit 属性

_Related: [vblang 2014 纪要 #8 – Readonly autoprops（LDM-2014-02-17）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-02-17.md)；[vblang 2014-10-01 – 构造函数中向只读 autoprop 赋值（LDM-2014-10-01）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-10-01.md)；[vblang 2014-04-23 – 不可变数据与 record（LDM-2014-04-23）](https://github.com/dotnet/vblang/tree/main/meetings/2014/LDM-2014-04-23.md)；[vblang 2017-11-15 #194 – WithPropertyEvents 提及 "freezable" 类型（vbldm-notes-2017.11.15）](https://github.com/dotnet/vblang/tree/main/meetings/2017/vbldm-notes-2017.11.15.md)；[C# 9 `init`（csharplang proposals/csharp-9.0/init.md）](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/init.md)；[C# 11 `required`（csharplang proposals/csharp-11.0/required-members.md）](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/required-members.md)；ModVB：`proposal-initonly-mustinit.md`（本建议）、`proposal-key-fields-auto-constructors.md`、`proposal-with-enhancements.md`、`proposal-initializer-enhancements.md`_

### 场景与缺口

We started from a gap that has been visible since the 2014 immutable-data request——"Please make it easier for me to declare immutable data-types"（LDM-2014-04-23）。不可变/初始化一次的对象在 DTO、配置、值对象场景里高频出现，但 VB 的表达力卡在两道缺口上：

1. **"构造后可读、仅初始化期可写"没有属性级表达。** 今天要么用已批准的 `ReadOnly` autoprop + 构造函数参数（样板多），要么用带可变 backing field 的 getter-only 属性（`Set` 全删或 private——后者仍可在类内写）。而**对象初始化器 `With {}` 写不了 `ReadOnly` 属性**——这恰好是声明式初始化的主战场。
2. **"必填成员"没有编译期强制。** 忘填一个成员，结果不是 `Nothing` 引用异常就是空默认值在运行期爆炸。把错误从运行期挪到编译期，正是"消除隐蔽缺陷"式的东西。

Anthony 在第 16 章（Performance and Interoperability）给出的示例（`..\AnthonyDesign_wordpress.txt` L2537–2567）：

```vb
Class Account
    ReadOnly _Id As Guid

    InitOnly Property Id As Guid
        Get
            Return _Id

        Set(value)
            If value = Guid.Empty Then Throw

            _Id = value
    End Property
End Class

? New Account With {.Id = Guid.NewGuid()}
```

```vb
Class Person
    MustInit Property Name As String = ""
    MustInit Property Age As Integer
End Class

? New Person With {.Name = "Anthony", .Age = 41}
```

第二个示例干净地表达了两件事：`MustInit` 成员在 `New ... With {}` 处缺任一项即报错；`Name` 带默认值 `= ""` 仍是必填。第一个示例则让我们**在门口就停下了**——它能不能编译，本身就是一个设计问题（见深度追问 11）。

### 候选方案

**PROPOSAL A — 按建议原文：两个新属性修饰符一起做。** `InitOnly`（getter + 仅初始化期可写的 setter）与 `MustInit`（必填，对象创建点强制）。两者配合 `With {}` 完成声明式初始化。示例即 Anthony 原文。

**PROPOSAL B — 只做 `InitOnly`，`MustInit` 交给分析器。** `MustInit` 的"必填"本质是构造点静态检查——C# 把它做进语言，但分析器（Roslyn analyzer）也能发诊断。先做 `InitOnly`（写上下文扩展），`MustInit` 用 analyzer + `None`-able 警告试水。

**PROPOSAL C — 只做 `MustInit`，`InitOnly` 用已批准领地表达。** `ReadOnly` autoprop 构造内赋值 2014 年已批准（LDM-2014-10-01），构造函数已能覆盖"初始化期"的大半；只缺对象初始化器。若 `MustInit` 更紧迫，先落它，`InitOnly` 留到与对象初始化器语义一起设计。

**PROPOSAL D — 不与 C# 对齐，走"freezable"运行时路线。** 2017 年主线讨论 `WithPropertyEvents` 时提到过 "freezable" 类型："You could imagine implementing a 'freezable' type immutable class whose properties all throw if you try to set them after the object has been 'frozen'"（vbldm-notes-2017.11.15）。用运行时 `IsFrozen` 标志在 setter 里抛异常，而不是编译期调用点限制。We 立即否掉：这是"隐蔽的控制流"（原则 #7），且把语言错误变成运行期异常，与 MustInit 想消灭的东西同源。

### 权衡：Q&A

- **A vs B/C：`InitOnly` 与 `MustInit` 是同一个特性吗？** 不是。`InitOnly` 是**写上下文的扩展**——把"哪里能写"的集合扩到构造期之外的对象初始化器，主体工作在前端调用点限制 + 语义模型；`MustInit` 是**构造点强制**——对象创建表达式的完整性检查 + 泛型/继承传播，主体工作在"哪些代码路径满足必填"的定序分析。两者风险画像、规格面、实现面都不同。B 与 C 都只压一半。评估标准把"一份提案混杂多个独立特性，边界模糊"列为红旗。**结论：拆开评审**，即便最终一起落地。
- **为什么不做纯 analyzer（B 的 MustInit 一半）？** 分析器能发诊断、能建议，但不能让"忘了初始化"变成**编译错误**。`MustInit` 的价值锚点是"漏填即拒绝编译"——这是语言级保证，分析器给不了（它默认被跳过、可被抑制、按 warning 对待）。这一点与 C# 把 `required` 做进语言的判断一致。但 analyzer 是**很好的试水工具**，可作为 MustInit 正式进语言前的数据收集器。
- **C# 已经给了完整答案，为什么不直接照搬？** 两处不能照搬。(1) **存储模型**：C# `init` 不用 CLR `initonly` 存储，纯靠编译期调用点限制；而 VB 的 `ReadOnly` 字段就是 CLR `initonly`——建议示例把两者混写了（见追问 11）。(2) **必填满足方式**：C# 的 `required` 只在对象初始化器/带 `[SetsRequiredMembers]` 的构造处满足；Anthony 的 piecewise 疑问（先 `New` 再逐条赋值）在 C# 里是直接报错的。VB 是否沿用，见追问 2。
- **命名：`MustInit` vs C# `required` vs `Required`？** 主线取向是"默认跟随 C#，除非有充分理由"（评价标准 1.5），但命名是可读性/“VB 味儿”的独立判断领地。`MustInit Property Name` 读起来是英语祈使句，比 `Required Property Name` 更"动作化"，也比 C# 的形容词 `required` 更明确"必须被初始化"。Anthony 沿用的 `MustInit` 我们喜欢。`InitOnly` 则有名不副实之嫌——见追问 11，它并不承诺 IL `initonly` 存储。
- **与已批准的 readonly autoprop 重叠是不是"第二种做事方式"（原则 #3）？** 这是本场最尖锐的自我拷问。2014 年批准的是"`ReadOnly` autoprop + 构造内赋值"；`InitOnly` 若只是把它扩到对象初始化器，那是**同一特性的边界扩展**，不是新做一件事。若 `InitOnly` 被设计成与 `ReadOnly` 并行的独立语法（两种写法都能表达"初始化期可写"），那就制造了第二种方式。**结论：`InitOnly` 必须被定位为既有 readonly-autoprop 语义的扩展**，而不是又一个平行的只读写法。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`InitOnly` 与 `MustInit` 今天都是合法标识符——`Dim InitOnly As String`、`Dim MustInit As Integer` 现在能编译。若把它们做成**保留字**，就是 parse 级 breaking change，直接撞上原则 #1（"We will almost never make breaking changes"）。因此 v1 必须是**上下文关键字**（如 `Key`、`MustInherits` 这类按位置识别），靠 lookahead 消歧：

- `InitOnly Property Id As Guid` → `InitOnly` 后跟 `Property` ⇒ 修饰符（今天这是非法句法，无旧代码可破坏）。
- `Dim InitOnly As String` → `InitOnly` 后跟 `As` ⇒ 标识符（今天合法，解析不变，**零 breaking**）。

修饰符排序需并入既有规则：`Public MustInit InitOnly Property Name As String` 中 `InitOnly` 与 `MustInit` 的先后、与 `ReadOnly`/`Shared`/`Shadows` 的排序，要有一份确定性文法。`MustInit ReadOnly Property` 需特别定义——见追问 2。`Probably`：排序跟随现有修饰符词典序，`InitOnly` 与 `MustInit` 同时出现时语义上 `InitOnly` 修饰 setter、`MustInit` 修饰必填，二者正交。

#### 2. 角案例与边界语义

**初始化期的精确边界（`InitOnly`）。** 谁可以写一个 `InitOnly` 属性？候选集：

- 声明该属性的类型的构造函数体（已批准 readonly autoprop 规则的自然扩展）；
- 派生类构造函数体（写基类 `InitOnly` 属性）；
- 对象初始化器 `New X With {.Id = ...}`；
- 另一个 `InitOnly` 的 `Set` 体内（C# 允许 init 访问器内再调 init——链式初始化）；
- 字段/属性初始化器（`InitOnly Property Id As Guid = Guid.NewGuid()`——声明处初始化算不算"初始化期"？`Probably` 算，与 autoprop 初始化器先例一致）。

边界之外的一切写 = 编译错误。**构造完成后对 `Id` 的写入属于编译错误**（建议原文的期望结果），这个"之后"的判据必须在 spec 里用"对象初始化器展开之后"这样的定序语言写清，而不是模糊的"构造期间"——因为 `With {}` 的赋值在 IL 层面发生在 `Newobj` 之后。

**Piecewise 初始化（Anthony 的 `?`）。** 建议原文带问号的问题：

```vb
Let p = New Person
p.Name = "Anthony"
p.Age = 41
? p.ToString()
```

先 `New Person` 再逐条赋值再使用其他成员，是否算满足 `MustInit`？两种立场：

- **(i) 表达式完整性模型（C# 模型）**：`New Person` 是独立表达式，创建点即须完整；上面第一行就报错。简单、与 C# 一致、无流分析。
- **(ii) 定序分析模型（definite-assignment 式）**：编译器跟踪"自 `New` 到首次使用其它成员之间，所有 `MustInit` 成员是否都被赋值"。上面三段满足，`? p.ToString()` 不报错。更宽容、更 VB（VB 用户习惯先建后配），但实现面大、与"对象创建即完整"的直觉相悖，且 `p` 转义到方法外（`Pass(p)`）后无从追踪。

We 倾向 **(i)**：v1 仅允许在 `New ... With {}` 表达式内或"已满足型"构造函数内满足 `MustInit`，piecewise 直接报错。理由：`MustInit` 的卖点是"漏填即编译错误"，而 (ii) 把检查从"创建点"移到"首次使用点"，错误定位变差，且 `If TypeOf` 分支/闭包/`ByRef` 都会破坏跟踪。Anthony 的 `?` 答案我们定为**否**，但留作 OPEN QUESTION 交由社区验证（见 OPEN QUESTIONS）。这与 2014-10-01 对结构体 piecewise 的裁定同构——当时我们拒绝让 readonly autoprop 比 settable autoprop"更能干"（"readonly autoprops would end up being more expressive than settable autoprops which would be odd!"，LDM-2014-10-01）；这里拒绝让"先建后填"比"初始化器一次性填"更受优待。

**默认值与 `MustInit`。** `MustInit Property Name As String = ""`——有默认值还"必填"吗？三条路：

- **(a) 默认值不满足必填**：创建点仍强制；默认值只在反射/反序列化等绕过路径作回退值。与 C# `required` 行为一致（`Probably`——C# 允许 `required` 成员带初始化器，但调用点仍强制，初始化器几乎总被对象初始化器覆盖）。语义自洽：编译期保证 + 运行期兜底。
- **(b) 默认值使必填失效**：`= ""` 表示"有安全默认，可省略"。那 `MustInit` 就成了摆设，两个修饰语互相矛盾——建议应禁止这种声明，或直接拿掉 `MustInit`。
- **(c) 声明即错误**：`MustInit` 与初始化器互斥。最干净，但会让 Anthony 的示例直接非法。

We 倾向 (a)：它保留了示例，且"编译期强制 + 反射回退"恰好是建议 Drawbacks 里"反序列化/反射容易绕过"的对冲。但必须在 spec 里写死"默认值不满足必填"——否则 `= ""` 会让新手以为可以不填。**这是本建议最需要一句话定死的语义。**

**`MustInit` + `ReadOnly` / 不可写成员。** `MustInit ReadOnly Property`（getter-only）无法在对象初始化器里写 → 只能在显式构造函数里满足。`MustInit ReadOnly Field`（CLR initonly 字段）同理：构造内可满足，`With {}` 不可。这两者合法但把满足路径限定到构造——需要 spec 明确；`Probably` v1 允许，但文档标注"仅构造可满足"。

**泛型。** `Function Make(Of T As New)() As T` 里的 `New T`——T 的 `MustInit` 成员无从得知，编译器无法强制。要么在 `New T` 处跳过检查（泛型不保证），要么运行时反射检查（慢、且与"编译期保证"的卖点矛盾）。We `Probably`：v1 泛型内不强制，文档承认这是洞；与"运行时库"建议（`proposal-runtime-library.md`）协作可加一个辅助检查。C# 的答案也是调用点不强制泛型内 required——`Probably` 同样。**这是一个必须写进 Drawbacks 的真实漏洞。**

**继承。** 派生类创建时，基类的 `MustInit` 成员也要被满足。`New Employee With {.Name = "Alice", .Age = 30, .Department = "R&D"}`（`Employee` 继承 `Person`）——`Name`/`Age` 是基类必填，`Department` 是自身必填，初始化器要一次全填。派生类显式构造函数若不写 `MyBase.New` 的必填参数或不在体内赋基类必填成员，需在 `MyBase.New(...)` 调用点强制。这条与 `Key` 自动构造建议的 `MyBase` 链问题同源（见 `meeting-key-fields-auto-constructors.md` 追问 2）。

**结构体。** 结构体永远有隐式无参构造，`New S With {.X = 1}` 在 `InitOnly` 结构体属性上——2014-10-01 对结构体 readonly autoprop 的 definite-assignment 规则（`Option4`：构造内对 autoprop 的读写按 backing field 处理）可复用。但结构体的 `With` 对象初始化器与 `Me` 的 definite-assignment 交互（未全字段初始化前不能读 `Me`）需要重新对表。

**ByRef。** 把 `InitOnly` 属性传给 `ByRef` 实参：copy-out 会写属性。2014-10-01 对 readonly autoprop 的裁定是"构造函数/初始化器内 copy-in 走访问器、copy-back 写 backing field；之外 copy-in 不回写"（`Option2`）。`InitOnly` 的"之外"怎么办？写属性 = 编译错误（因为 `InitOnly` 构造后不可写）⇒ `Probably`：`InitOnly` 属性在任何上下文都**不得作为会 copy-back 的 `ByRef` 实参**（对象初始化器内也不行——被调方改写一个正在初始化的属性会破坏不变量），直接报错。这比 readonly autoprop 的"静默不回写"更严格，我们接受——因为 `InitOnly` 的 setter 本身就是"受限写"，静默丢弃 copy-out 会让用户困惑。

**Lambda。** 构造函数内 lambda 里写 `ReadOnly` 变量已有先例错误 `BC36602`（"'ReadOnly' variable cannot be the target of an assignment in a lambda expression inside a constructor"，LDM-2014-10-01）。`InitOnly`/`MustInit` 成员被 lambda 捕获并赋值——lambda 可能在初始化期外执行，必须同样报错。`Probably`：复用 `BC36602` 的检测路径，扩展错误文案。

**引用可变性的假象。** `InitOnly Property Items As List(Of String)`——只锁引用，不锁内容；`Items.Add(...)` 永远合法。文档必须写明 `InitOnly`/`MustInit` 表达的是"引用/成员级别的初始化契约"，不是深度不可变。这与 2014-04-23 record 讨论的教训一致（可变引用类型成员的 `GetHashCode` 不稳定是旧账）。

#### 3. 作用域与绑定

`New Person With {.Name = "x"}` 中 `.Name` 绑定到属性（不是 backing field）——沿用 2014-10-01 语义模型裁定："they refer to the property x. Not the backing field"。语义模型里，`InitOnly` 属性的 setter 在"初始化上下文"内可写、块外只读；`GetTypeInfo` 应能区分"此符号在此上下文是否可写"。IDE 补全在 `New Person With {` 处列出 `MustInit` 成员并标记"必填"；块外对 `InitOnly` 属性的赋值显示红线。

#### 4. 与既有特性的交互

- **readonly autoprop 构造内赋值（2014-10-01 全套规则）**：`InitOnly` 复用其"绑定到属性、definite assignment 按 backing field、代码生成走访问器"三件套，扩展的是"写上下文集合"。
- **对象初始化器既有规则**：VB `With {}` 成员赋值走 setter；`InitOnly` 属性让 setter 在此上下文可调用——这是纯扩展，不改变既有 `With` 行为。
- **`Key` 自动构造（`proposal-key-fields-auto-constructors.md`）**：`Key` 字段生成构造参数与赋值——若字段是 `MustInit`，自动构造天然满足必填，**强协同**。若字段是 `InitOnly` 属性，自动构造内的赋值恰在初始化期，也成立。两份建议应共享"初始化期"的定义。
- **`With` 增强与初始化器增强**：命名 `With` 变量、`.Me`、嵌套初始化器（`proposal-with-enhancements.md`、`proposal-initializer-enhancements.md`）在 `MustInit` 强制下——嵌套 `With` 里漏填必填成员，错误应定位到最内层初始化器。
- **可空性流分析（`proposal-nullability-flow-analysis.md`）**：`MustInit Property Name As String` 语义上隐含"非空"（创建点已赋值）。若可空注解落地，`MustInit` 成员应默认视为非空，需对齐注解模型。
- **晚期绑定 / Option Strict Off**：`New Person With {.Name = "x"}` 在宽松模式下——`Name` 缺失仍是编译错误（`MustInit` 检查的是"是否出现"，与类型检查无关），两条路径行为必须一致（见追问 6）。
- **隐式接口实现 / 扩展属性**：接口属性带 `InitOnly`/`MustInit`，实现成员须匹配写上下文（组 17 工作项）。

#### 5. Breaking change 与兼容性

- **文法层**：`InitOnly`/`MustInit` 若做上下文关键字，零 parse breaking（追问 1 的 lookahead 论证）。
- **语义层**：纯新修饰符——只有标了新修饰符的新代码才产生新错误，旧代码重编译行为不变。
- **元数据面**：`InitOnly`/`MustInit` 需要属性标注（类似 C# 的 `IsExternalInit` modreq / 自定义 attribute）才能让**其它语言**（尤其 C#）看到"这个属性是 init-only / required"吗？若 VB 程序集要被 C# 消费，C# 的 `init` 和 `required` 消费者识别的是 C# 侧的 modreq/attribute——VB 需要决定发射什么元数据。**这是建议完全没提、但跨语言消费必踩的地雷**：VB 标 `InitOnly` 的属性，C# 里应是 `init`，否则 C# 端写 `x.Id = v` 编译通过（作为普通 setter）。`Probably`：发射与 C# 相同的元数据约定（init 用 `modreq(IsExternalInit)` 或兼容 attribute），spec 必须含这一节。
- **与 2014-10-01 "Absolute PEVerify" 的关系**：见追问 11——这是本建议最大的一枚隐藏雷。

#### 6. Option Strict / 编译选项分叉

`MustInit` 强制是**编译错误**，不随 `Option Strict` 关闭而退化为警告——与 definite assignment、`BC36602` 同类，是语言级保证。宽松模式下 `New Person With {.Name = "x"}` 漏 `Age`：检查"成员是否出现"不依赖类型严格性，两条路径一致报错。晚期绑定不影响 `MustInit` 的存在性检查。We 明确：`MustInit` 不得受 `Option Strict` 开关影响——否则"必填"在宽松项目里就是可选的，卖点崩塌。

#### 7. IDE / IntelliSense

- `New Person With {` 补全：列出全部 `MustInit` 成员并高亮"必填"；漏填时错误波浪线直接落在 `With` 块。
- `InitOnly` 属性：初始化上下文内补全显示"可写"，块外显示只读；重命名/提取对象初始化器时保持。
- 建议"把某个属性标为 `MustInit`"的重构，与"Generate Constructor"脚手架联动（`Key` 自动构造建议同款）。
- 参数提示（构造 + `With` 组合）对新手尤其重要——这是该特性的"可发现性"承重墙。

#### 8. 数据 / 普遍性

不可变 DTO、配置对象、请求模型在业务代码里高频；`required` 与 `init` 在 C# 生态被证明受欢迎（`Probably`——无本仓库数据，仅 C# 社区观察）。2014-04-23 的不可变数据请求证明这是十年以上的长期诉求。但对"数十万安静客户"的一般业务代码，**量化数据缺失**：有多少 VB 项目会主动采用声明式不可变？We `Suspect`：价值真实、频率中等——不够头条，但够"值得做"。VBScript.NET 语境下，脚本化的 DTO/记录构造可能更依赖 `With {}`，`InitOnly` 的价值被放大——`Suspect`，待原型验证。

#### 9. 更简替代

- **`ReadOnly` autoprop + 构造函数**：已批准、零新语法。代价：初始化器/`With` 场景写不了、必填无强制。`InitOnly` 的价值正在于补上 `With` 这一半。
- **analyzer**：能发"漏填"诊断，不能拒绝编译；`MustInit` 试水工具，非替代。
- **源生成器**：生成 `Sub New` + 校验样板，能模拟大半 `MustInit`，但生成代码与手工维护失配同源（同 `meeting-key-fields-auto-constructors.md` 的 B2 论证）。
- **运行时 "freezable"**（追问 PROPOSAL D）：已否——运行期异常不是编译期保证。
- **什么都不做**：保持现状。代价：不可变 DTO 继续样板 + 漏填继续运行期炸。对 C# 侧已存在的 `init`/`required` 消费，VB 无从表达（见追问 5 元数据），跨语言互操作会持续吃亏。

#### 10. 复杂度 / 成本 / 优先级

- **`InitOnly`**：前端调用点限制 + 语义模型可写性标记 + `With` 上下文扩展，复用 2014-10-01 已批准的构造内赋值实现面，**成本中低**。
- **`MustInit`**：构造点完整性检查 + 泛型/继承/默认值/`MyBase` 链的规格，**成本中高**，且多处规格未定。
- **拆分排序**：`InitOnly` 先（增量、在已批准领地、值高），`MustInit` 后（强制面全新、需定规格）。这与 `meeting-key-fields-auto-constructors.md` 的"B1 先、B2 后"排序直觉一致。

#### 11. 运行时 / CLR 硬约束

**这是本场最深的一脚。** 建议的核心示例：

```vb
ReadOnly _Id As Guid
InitOnly Property Id As Guid
    Get
        Return _Id
    End Get
    Set(value)
        If value = Guid.Empty Then Throw New ArgumentException(...)
        _Id = value
    End Set
End Property
```

`? New Account With {.Id = Guid.NewGuid()}` 必须编译。但 VB 的对象初始化器 `With {}` 的赋值**在 IL 层面发生在 `Newobj` 之后**——它是"建临时对象、调构造、逐成员赋值、使用"的展开。而 `_Id` 是 `ReadOnly` 字段，即 CLR `initonly` 字段。在构造之外写 `initonly` 字段 = 违反 CLR 存储规则：验证失败、运行期 `FieldAccessException`。2014 年主线明确过天花板："We can't be more permissive in what we allow with readonly autoprops than we are with readonly fields, because this would break PEVerify"（LDM-2014-10-01）。把"可写期"从构造函数扩展到对象初始化器，**恰好越过了这条线**。

推论（`Suspect`，但基于上述 2014 年裁定推导）：

1. `InitOnly` 的 backing field **不能是 CLR `initonly`**——示例里的 `ReadOnly _Id` 必须改为普通字段，否则示例编译出的 IL 非法。
2. 因此 `InitOnly` **不具备 CLR 级只读保证**：它是纯编译期调用点纪律。反射、IL 手写仍可写——与 C# `init` 相同，也与 2014-02-17 Note1 一致（"The CLR allows assignment-to-readonly by reflection at any time"）。文档必须诚实：`InitOnly` 名字里的 "initonly" 是**借用 IL 词汇，不是 IL 语义**。
3. 若想获得真 CLR 只读存储，只有一条路：把可写期限定回构造函数（即已批准的 readonly autoprop），那 `With {.Id = ...}` 就写不了——建议的动机示例当场失效。**两个目标只能选一个**：要么 CLR 真只读（构造内写），要么对象初始化器可写（普通字段 + 调用点纪律）。C# 选了后者。We 也选后者，但要求 spec 把它写成一节"为什么不是 CLR `initonly`"。

`MustInit` 纯编译期，无 CLR 硬约束；唯一元数据面是跨语言消费（追问 5）。

#### 12. 值不值得做

逐条打分：

- **价值**：高。不可变/必填 DTO 是真实高频；与 C# `init`/`required` 对齐的 DX 让 VB 在跨语言场景不掉队；`InitOnly` 在已批准领地上做增量，风险低。**价值：高。**
- **成本**：`InitOnly` 中低；`MustInit` 中高（规格未定处多）。**成本：中。**
- **风险**：中，且集中在"存储模型与 CLR 约束""piecewise 边界""默认值语义"三处未定。**风险：中。**

**值得做——但拆开、收敛、把存储模型和边界先定死。** 若原样整体采纳（PROPOSAL A），我们会因示例自身违反 CLR 规则、默认值语义自相矛盾、piecewise 悬而未决而建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心。`InitOnly` 消灭"私有可变 backing field + getter + 构造参数"的三重复；`MustInit` 把"漏填"从运行期样板挪到编译期。这是本建议最强的基因继承。
- **读起来像英语、对新手友好（原则 #5）**：`MustInit Property Name` / `InitOnly Property Id` 是英语祈使句，新手能猜大意——加分。`MustInit` 尤其比 C# 的形容词 `required` 更"动作化"。
- **不引入"第二种做事方式"（原则 #3）**：主要扣分项。`InitOnly` 若被写成与 `ReadOnly` autoprop 平行的又一只读语法，就是第二种方式。化解：定位为"readonly-autoprop 写上下文的扩展"，并明确 `ReadOnly Property`（getter-only）与 `InitOnly Property`（getter + init-setter）的分工，不是同一语义的两个名字。
- **避免隐蔽语义变化（原则 #7）**：双面。`MustInit` 把隐蔽的运行期失败变成显式编译错误，是加分；但 piecewise 若采用定序分析模型（追问 2 的 (ii)），"先建后填"的合法性就是一种隐蔽控制流——v1 选 (i) 正是为守这条原则。
- **保持 VB-like（原则 #2）**：属性修饰符 + `With {}` 消费，句法面全在 VB 既有形状内。`InitOnly` 名字的 IL 撞车是教育问题，不是句法问题。
- **默认跟随 C#（原则 #4）**：功能面跟随 C# `init`/`required`（方向一致），命名面偏离（`MustInit` vs `required`）——落在评价标准 1.5 的"VB 风格优先于 C# 对齐"授权范围内。但**元数据发射**必须跟随 C#（追问 5），否则跨语言互操作断裂。
- **与主线关系（对照表 2.3）**：主线 vblang 快照（2014–2018）**没有**讨论过 C# `init`/`required`——那是 C# 9/11 的事，晚于主线纪要。主线批准过的是 readonly autoprop 构造内赋值（2014-10-01）与不可变数据诉求（2014-04-23）。因此 `InitOnly` = **在主线已批准领地上的增量扩展**（方向与主线一致，主线未接管对象初始化器这块）；`MustInit` = **Anthony 独立延伸**（全新强制面，主线无对应）。对照表中 Anthony"故意不跟 C#/F#"的风格在本建议体现为命名独立；功能本体仍跟随 C#，与主线的"默认跟随 C#"取向相合。

### RESOLUTION:

1. **拆分为两个正交特性评审。** `InitOnly`（写上下文扩展）与 `MustInit`（构造点强制）价值、风险、实现面不同，捆绑会使两者都得不到公允判断。分而治之。
2. **`InitOnly`：原则上采纳方向，`Consider`。** 模型定为**编译期调用点纪律 + 普通可变 backing field**，**不承诺 CLR `initonly` 存储**。必须新增一节"为什么不是 CLR `initonly`"：对象初始化器赋值在 `Newobj` 之后，写 CLR initonly 字段违反 2014-10-01 的 PEVerify 天花板（"We can't be more permissive... because this would break PEVerify"）。建议示例中的 `ReadOnly _Id` 改为普通字段；`Set` 内校验逻辑保留。
3. **初始化期边界（v1）**：构造函数体、派生类构造函数体、对象初始化器 `With {}`、另一个 `InitOnly` 的 `Set` 体、声明处初始化器。之外任何写 = 编译错误。spec 用"对象初始化器展开之后"的定序语言写清"之后"。
4. **`MustInit`：`Table`。** 概念有价值、C# 已验证，但规格未熟。落地前必须定：**默认值语义**（v1 倾向 (a)：默认值不满足必填，仅作反射/反序列化回退——一句话定死）；**piecewise**（v1 取表达式完整性模型：仅 `New ... With {}` 或已满足型构造函数可满足，`Let p = New Person` 后逐条赋值不满足；Anthony 的 `?` 答案为否）；**泛型**（`New T` 内不强制，文档承认为洞）；**继承**（派生创建须满足基类 `MustInit`；派生构造在 `MyBase.New` 调用点强制）。
5. **命名**：保留 `MustInit`/`InitOnly`（VB 味、Anthony 原创）；`InitOnly` 与 IL `initonly` 的撞名在文档显式澄清"无 IL 只读存储语义"。`MustInit` 不跟 C# `required`。
6. **元数据/互操作**：spec 必须含"VB 程序集如何让 C# 识别 `init`/`required`"一节，`Probably` 跟随 C# 发射约定（init 用 `modreq(IsExternalInit)` 兼容方案），否则跨语言消费把 `InitOnly` 当普通 setter。
7. **`MustInit` 强制不随 `Option Strict` 退化**；`InitOnly` 属性不得作会 copy-back 的 `ByRef` 实参；lambda 内赋值复用 `BC36602` 检测路径。
8. **状态：`Consider`**。`InitOnly` 方向接近 Active（在已批准领地做增量），需最小原型验证调用点限制与 IDE 可写性标记；`MustInit` 规格定稿前不排期（`Table` 方向）。未到 Reject——需求真实，方向合理。

### Implication:

- 将 `InitOnly` 写成独立 speclet：文法（上下文关键字 + lookahead 消歧 + 修饰符排序）、初始化期边界的定序定义、存储模型与 CLR 约束一节、语义模型可写性标记、ByRef/λ/结构体交互、`langversion` 门控。
- 将 `MustInit` 写成独立 speclet：默认值语义（v1 倾向 (a)）、piecewise 判定（表达式完整性模型）、泛型豁免、继承/`MyBase` 链强制、元数据发射。
- 起草跨语言互操作分析：VB `InitOnly`/`MustInit` 成员在 C# 消费端的可见语义（`init`/`required` 识别、modreq 方案）。
- 最小原型：`InitOnly` 调用点限制 + `With {}` 内可写；`MustInit` 构造点完整性检查 + 对象初始化器错误定位。验证语义模型 `GetTypeInfo` 与 IDE 补全。
- 与 `Key` 自动构造团队对表：共享"初始化期"与"必填满足路径"的定义；`Key` 字段自动构造应天然满足 `MustInit`。
- 修复建议示例（`End Get`/`End Set`、`Throw` 表达式、`ReadOnly _Id` → 普通字段），使示例可编译。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`MustInit` 默认值语义三选一（(a) 不满足必填仅回退 / (b) 使必填失效 / (c) 互斥报错）。We 倾向 (a)，需社区验证——尤其"为什么 `= ""` 还必填"的文档解释。
- `OPEN QUESTIONS`：piecewise——v1 取"仅 `New ... With {}` 满足"，但若社区强烈要求"先建后填再使用"，是否引入定序分析模型？待原型后数据决定。
- `OPEN QUESTIONS`：`MustInit` + `ReadOnly` 字段/属性（仅构造可满足）的组合规则。
- `OPEN QUESTIONS`：结构体 `InitOnly` 属性的 definite-assignment 与 `With` 初始化器的交互细则。
- `OPEN QUESTIONS`：元数据方案——modreq 跟随 C#，还是自定义 attribute（VB 自有语义但 C# 端不识别）？
- `TODO`：为 `MustInit`/`InitOnly` 的普遍性补量化数据（DTO/配置对象在开源 VB 代码中的占比；C# 侧 `init`/`required` 采用率，`Probably` 无本仓库数据）。
- `Follow-up`：重读 2014-04-23 record 讨论全文，逐条核对"哪些反对适用于 `InitOnly` 存储模型"——尤其是 GetHashCode/可变引用类型成员的旧账。
- `Follow-up`：与可空性流分析对表——`MustInit` 成员的非空默认语义。

### 状态

- **LDM 状态：`Consider`**——`InitOnly` 方向采纳、规格待补（存储模型一节是门槛）；`MustInit` 挂起（`Table` 方向）。
- **三态判定：Consider**——需求真实、方向合理、`InitOnly` 在已批准领地上增量风险低；但示例自身违反 CLR 规则暴露的存储模型问题、`MustInit` 的默认值/piecewise/泛型/继承四处未定，使整体不到 Active。远未到 Reject。

---

## 附录：特性评价

# 建议评价报告：proposal-initonly-mustinit.md

## 评价对象

- 建议：proposal-initonly-mustinit.md — `InitOnly`（初始化期可写、之后只读）/ `MustInit`（必填）属性
- 来源：Anthony 原文第 16 章 "Performance and Interoperability"（`..\AnthonyDesign_wordpress.txt` L2537–2567，`InitOnly`/`MustInit` 两示例与 piecewise 问句均逐字对应）
- 配方目标：`InitOnly` 让属性仅初始化期可写（对应 C# init）；`MustInit` 让对象创建点强制赋值必填成员（对应 C# required）；两者配合 `With {}` 完成声明式初始化

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（不可变 DTO 样板 + 漏填运行期爆炸）、示例可操作；但 4 个未决问题均为关键设计点 ⇒ 效果证据封顶；核心示例不能按所示编译（`End Get`/`End Set` 缺失、`Catch` 外的裸 `Throw`、`ReadOnly` 字段在构造后被 setter 写——CLR initonly 违规） | 已检查 | 未决问题 ≥4 个关键点；无原型（状态行占位 `PROTOTYPE_OWNER/...`、`pr/1`）；"构造完成后对 Id 的写入属于编译错误"的"之后"边界未定义 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。属性修饰符 + `With {}` 消费是极 VB 形态；`MustInit` 命名比 C# `required` 更 VB；但两特性捆绑、`InitOnly` 与已批准 readonly autoprop 重叠未论证（原则 #3 风险） | 已检查 | `InitOnly` 与既有 readonly autoprop 分工不清，有"第二种做事方式"之嫌 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、4 个未决问题具体诚实；但无文法/BNF、无兼容性/breaking 分析、无元数据/跨语言节；核心示例三处不能编译（性质最重的"示例与正文冲突"）；默认值语义、piecewise、泛型/继承均未定 | 已检查 | 状态行占位链接；`InitOnly` 存储模型未谈（示例自证其反）；`Set` 缺 `End Set`、裸 `Throw` 缺异常表达式 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/光正向（消除 DTO/配置样板、声明式、与 C# DX 对齐盘活跨语言资产）；暗风险突出（存储模型与 CLR 约束未识别、与已批准 readonly autoprop 领地重叠、piecewise 边界未定）且 Drawbacks 只列未权衡 | 已检查（预测待定） | 风=与 C# `init`/`required` 的演进一致性需元数据跟随，未分析；实际影响须"已采纳"后定 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料=Anthony 第 16 章（核实准确）；借鉴 C# `init`/`required` 已部分点明（示例注释 "(required in C#)"、正文提及"对应 C# 的 required"）；继承 VB `With` 块 / readonly-autoprop 基因未点明；命名 `InitOnly`/`MustInit` 为原创（非 C# 直译） | 已检查 | C# 侧借鉴仅提 required 未提 init 的系统标注缺失；`InitOnly` 与 IL `initonly` 的撞名/语义差异未澄清；无 VB6 血缘（`With` 块继承自 VB6/VB 家族，未标注） |

## 设计原则对照

- **与 VB 基因：部分一致**——消除样板（#9）、可读英语（#5）、VB 句法面（#2）满格；张力于原则 #3（与 readonly autoprop 重叠的第二种方式）、原则 #7（piecewise 若采用定序分析则隐蔽控制流，v1 取表达式完整性模型规避）。
- **与主线关系：主线一致方向的独立延伸**——主线批准过 readonly autoprop 构造内赋值（LDM-2014-10-01）与不可变数据诉求（LDM-2014-04-23）；`init`/`required` 是主线 vblang 快照之后 C# 的演进，主线未讨论。`InitOnly` 是在主线已批准领地（readonly autoprop 写上下文）上的增量扩展，方向与主线一致；`MustInit` 是 Anthony 独立延伸（全新强制面）。功能跟随 C#、命名偏离 C#，符合 Anthony"故意不跟 C#"的风格与主线"默认跟随 C#"取向的调和。
- **破坏性变更：无（前提）**——`InitOnly`/`MustInit` 做上下文关键字则文法层零 breaking（`Dim InitOnly As String` 解析不变）；语义层新错误只作用于标了新修饰符的新代码。若误做保留字则 parse breaking（原则 #1 红线）。元数据发射若跟随 C#（modreq），对 C# 消费端是加法、非破坏。

## 总评

- **达成程度：部分达成**——价值与方向成立、`InitOnly` 在已批准领地上风险可控；存储模型、默认值、piecewise、泛型/继承四处规格未完成。
- **LDM 三态建议：Consider**——拆分后 `InitOnly` 可升 Active（增量、低风险，但须先定存储模型一节并修示例）；`MustInit` 规格定稿前保持 Table。整体不达 Active，未到 Reject。
- **主要问题**：① `InitOnly` 存储模型未定，核心示例 `ReadOnly _Id` + 对象初始化器写自证其反（违反 2014-10-01 PEVerify 天花板）；② `MustInit` 默认值语义（`= ""` 与必填）自相矛盾未解；③ piecewise 边界悬而未决（Anthony 的 `?`）；④ 泛型/继承的必填强制规则缺失；⑤ 与已批准 readonly autoprop 的重叠未论证；⑥ 示例不能按所示编译（品质红线）。

## 返工建议

- **补充章节**：文法/BNF（上下文关键字 + lookahead 消歧 + 修饰符排序）；"为什么不是 CLR `initonly`"的存储模型一节（引用 LDM-2014-10-01 Absolute PEVerify）；初始化期边界的定序定义；Compatibility/breaking 分析；元数据/跨语言互操作（C# 消费端 `init`/`required` 识别）；泛型豁免与继承/`MyBase` 链强制；`langversion` 门控与诊断策略。
- **补充证据**：最小原型（`InitOnly` 调用点限制 + `With {}` 内可写；`MustInit` 构造点完整性检查 + 错误定位）；修复示例至可编译（补 `End Get`/`End Set`、`Throw` 改 `Throw New ArgumentException(...)`、`ReadOnly _Id` 改普通字段）；DTO/配置对象占比量化数据；C# `init`/`required` 采用率（`Probably` 无本仓库数据）。
- **未决问题处理**：默认值语义三选一定一句（v1 倾向 (a) 默认值不满足必填、仅反射回退）；piecewise 定"仅 `New ... With {}` 满足"（v1 否）；泛型 `New T` 内不强制、文档承认为洞；继承要求派生创建满足基类 `MustInit`。
- **设计探索**：`InitOnly` 与已批准 readonly autoprop 的合并/区分论证（消除原则 #3 第二种方式风险）；与 `Key` 自动构造的协同（`Key` 字段自动满足 `MustInit`）；与可空性流分析的非空默认语义对齐；与 C# `init`/`required` 的行为差异表（piecewise、默认值、ByRef 三处 VB 侧更严格）。

---

## 附录：C# 生态与互操作考量

本附录评估本提案主题（"初始化期可写、之后只读" + "必填成员"）在 C#/CLR/.NET 生态中的对应走向。C# 已为这两个痛点给出完整答案：C# 9 `init` 访问器（`InitOnly` 的对应物）与 C# 11 `required` 成员（`MustInit` 的对应物）。两套特性**元数据编码完全不同**，跨语言消费必须逐一对齐。以下基于 `..\..\csharplang` 原文，逐字引用并标注来源。

### 相关 C# 现实方向

**C# 9 `init` accessor（`proposals\csharp-9.0\init.md`）**
- 摘要原文：「This proposal adds the concept of init only properties and indexers to C#. These properties and indexers can be set at the point of object creation but become effectively `get` only once object creation has completed.」→ `proposals\csharp-9.0\init.md`（Summary）。
- 可写时机（与提案"初始化期边界"直接对应）：「An instance property containing an `init` accessor is considered settable in the following circumstances, except when in a local function or lambda: During an object initializer; During a `with` expression initializer; Inside an instance constructor of the containing or derived type, on `this` or `base`; Inside the `init` accessor of any property, on `this` or `base`; Inside attribute usages with named parameters」→ `proposals\csharp-9.0\init.md`（Detailed Design）。注意 C# 明确把可写期定义为"construction phase"（原文：「collectively referred to in this document as the construction phase of the object」），与提案要写的"对象初始化器展开之后"定序语言是同一意图。
- 元数据编码（`InitOnly` 互操作的核心）：「Property `init` accessors will be emitted as a standard `set` accessor with the return type marked with a modreq of `IsExternalInit`.」→ `proposals\csharp-9.0\init.md`（Metadata encoding）。
- **对 `readonly` 字段的写权限（与 VB 会议存储模型结论直接冲突）**：原文「Hence an `init` accessor is allowed to take the following actions in addition to what a normal `set` accessor can do: 1. Call other `init` accessors available through `this` or `base`; 2. Assign `readonly` fields declared on the same type through `this`」→ `proposals\csharp-9.0\init.md`（Detailed Design）。C# **允许** `init` 访问器写同类型的 `readonly` 字段。
- IL 验证：原文「When .NET Core decides to re-implement IL verification, the rules will need to be adjusted to account for `init` members.」→ `proposals\csharp-9.0\init.md`（IL verification）。CLR/JIT 究竟如何放行"从访问器（非构造）写 `initonly` 字段"，csharplang 仓库未文档化（属 dotnet/runtime）→ **OPEN QUESTIONS**。

**C# 11 `required` members（`proposals\csharp-11.0\required-members.md`）**
- 摘要原文：「This proposal adds a way of specifying that a property or field is required to be set during object initialization, forcing the instance creator to provide an initial value for the member in an object initializer at the creation site.」→ `proposals\csharp-11.0\required-members.md`（Summary）。
- 强制点（与提案追问 2 的 piecewise 判定直接对表）：原文「For every constructor `Ci` in type `T` with required members `R`, consumers calling `Ci` must do one of: Set all members of `R` in an _object_initializer_ on the _object_creation_expression_, Or set all members of `R` via the _named_argument_list_ section of an _attribute_target_, unless `Ci` is attributed with `SetsRequiredMembers`.」→ `proposals\csharp-11.0\required-members.md`（Enforcement）。C# 只有**对象初始化器 / 属性具名参数**两条满足路径，**没有 piecewise**。
- 元数据（`MustInit` 互操作的核心，**非 modreq**）：原文「We don't use a `modreq` here because it is a goal to maintain binary compat: if the last `required` property was removed from a type, the compiler would no longer synthesize this `modreq`, which is a binary-breaking change and all consumers would need to be recompiled.」→ `proposals\csharp-11.0\required-members.md`（Metadata Representation）。发射约定：成员标 `RequiredMemberAttribute`、类型标 `RequiredMemberAttribute`（有 required 成员时）、豁免构造标 `SetsRequiredMembersAttribute`、非豁免构造标 `CompilerFeatureRequiredAttribute("RequiredMembers")` + 错误级 `ObsoleteAttribute`。
- 泛型 `new()` 约束：原文「A type with a parameterless constructor that advertises a _contract_ is not allowed to be substituted for a type parameter constrained to `new()`, as there is no way for the generic instantiation to ensure that the requirements are satisfied.」→ `proposals\csharp-11.0\required-members.md`（`new()` constraint）。
- 结构体 `default`：原文「Required members are not enforced on instances of `struct` types created with `default` or `default(StructType)`.」→ `proposals\csharp-11.0\required-members.md`（`struct` defaults）。
- `required` 字段不可 `readonly`（直接回答提案的 `MustInit`+`ReadOnly` OPEN QUESTION）：原文「It is an error to mark a member required if the member cannot be set in any context where the containing type is visible. If the member is a field, it cannot be `readonly`.」→ `proposals\csharp-11.0\required-members.md`（Accessibility）。
- 命名候选（与 `MustInit` 直接撞名）：原文「Is `required` the right modifier? Other alternatives that have been suggested: `req`, `require`, `mustinit`, `must`, `explicit`」→ `proposals\csharp-11.0\required-members.md`（Syntax questions）。**`mustinit` 当年就是 C# 的备选名之一**。

**相关 LDM 纪要（2021–2022，required 定稿过程）**
- LDM-2021-10-25（开案：强制用 error 而非 warning）：「They're not really suppressions though. It's changing the semantics of the code, so we think it's fine (and safer) for us to use errors on construction, not warnings.」→ `meetings\2021\LDM-2021-10-25.md`。与提案 RESOLUTION #7（`MustInit` 不随 `Option Strict` 退化）同构。
- LDM-2022-01-05（可见性 + 构造保护）：「We will go with option 1: all required members must be at least as visible as their containing type.」→ `meetings\2022\LDM-2022-01-05.md`。
- LDM-2022-01-24（元数据定稿）：「We will adjust the specification to put a `RequiredMemberAttribute` on every member that is marked required, and to put it on any type that contains such members.」→ `meetings\2022\LDM-2022-01-24.md`。
- LDM-2022-03-21（required 成员可带初始化器、不警告）：讨论场景 `public required int Field = 1;`，原文「An analyzer seems appropriate if a user wants to forbid redundant assignments here.」，结论「No warning.」→ `meetings\2022\LDM-2022-03-21.md`。这正是提案追问 2 默认值三选一里 **(a)** 的 C# 先例。
- LDM-2022-03-23（required 字段 readonly / ref 属性）：「We will require that required fields cannot be readonly, and that required properties are settable from every constructor.」→ `meetings\2022\LDM-2022-03-23.md`。

### 现实 vs 提案

**兼容点（C# 已给答案，方向一致）**
- `InitOnly` ↔ C# `init`：同向。可写时机集合高度重合（对象初始化器、构造、另一个 init 的 Set 体内、属性具名参数）；都**不承诺 CLR `initonly` 存储**（C# 用 modreq 标记而非存储位）。
- `MustInit` ↔ C# `required`：同向。强制点都在"对象创建表达式"，C# 明确只有对象初始化器 / 属性具名参数两条满足路径——正是提案追问 2 的 **表达式完整性模型 (i)**。C# 同样拒绝 piecewise，Anthony 的 `?` 在 C# 里同样报错。
- 错误等级：C# 明确"强制用 error 而非 warning"（LDM-2021-10-25），与提案 RESOLUTION #7 一致。
- 默认值语义：C# 允许 required 成员带初始化器、调用点仍强制、不警告（LDM-2022-03-21）——正是提案倾向的 **(a)**。这为 VB 的 (a) 提供了可直接引用的 C# 先例。

**需桥接 / 分歧点**
1. **存储模型（RESOLUTION #2 需修正）**：VB 会议结论"`InitOnly` 的 backing field 不能是 CLR `initonly`，示例 `ReadOnly _Id` 必须改普通字段"——**比 C# 更保守**。C# 明确允许 `init` 访问器写**同类型 `readonly` 字段**（init.md Detailed Design），Anthony 示例的 `ReadOnly _Id` 模式在 C# 语义下**合法**（写入发生在 InitOnly Set 体内，等同 C# init 访问器）。真正要定的不是"字段能否 initonly"，而是"谁能写"：对象初始化器永远经访问器写、访问器内部可写同类型 readonly 字段。CLR/JIT 如何放行该写入（modreq 识别？验证器放宽？）在 csharplang 仓库未文档化 → **OPEN QUESTIONS**（属 dotnet/runtime）。VB 可二选一：镜像 C#（允许 readonly backing + init 访问器内写，依赖运行时放行），或保留保守的普通字段模型（对 PEVerify 更稳、但与 C# 语义分叉）。**这是本附录最重要的分歧点。**
2. **元数据发射（RESOLUTION #6 需补全）**：C# 对 `init` 与 `required` 用了**两套完全不同的编码**。`InitOnly` 要跟 C# `init`：setter 标 `modreq(IsExternalInit)`；`MustInit` 要跟 C# `required`：成员+类型标 `RequiredMemberAttribute`、构造标 `SetsRequiredMembersAttribute` 或 `CompilerFeatureRequired("RequiredMembers")`+错误级 `Obsolete`——**不是 modreq**（C# 为二进制兼容明确弃用 modreq）。提案 RESOLUTION #6 只给了 init 的 modreq 方案，required 的 attribute 编码缺失，spec 必须补全两套。
3. **`MustInit` + `ReadOnly` 的 C# 答案**：C# 规定 required 字段**不可 readonly**（Accessibility），required 属性必须有 setter/initer（LDM-2022-03-23）。提案 OPEN QUESTION（`MustInit ReadOnly Field/Property`）在 C# 侧答案清晰：字段不行；属性可行但需 setter/initer。VB spec 应对齐。
4. **泛型**：C# 对 `new()` 约束做了**硬限制**（带 required 成员的类型不得替代 `new()` 类型参数）——比提案"泛型内不强制"更严格。C# 是把"泛型构造无法保证必填"的洞**堵上了**，VB 若只做"调用点不检查"会留下 C# 没有的洞；`Probably` 值得借鉴 C# 的 `new()` 排除。
5. **命名**：`mustinit` 是 C# required 讨论时被否决的备选名之一（Syntax questions）。Anthony 的 `MustInit` 与 C# 否决名单撞名——不是问题，但文档应点明这是"VB 主动选择 C# 放弃的名字"，以区分"不知道 C# 讨论过"与"知道但选择不同"。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：脚本运行时默认强制 `MustInit`（与 C# 同为 error 级），`With {}` 是脚本 DTO 声明式构造的主通道；解释模式可保留显式宽松入口，但默认编译产物走强制路径，`MustInit` 不受 `Option Strict` 影响（RESOLUTION #7）。
- **source-gen 桥**：C# required 的动机之一正是 ORM（EF Core 需要公共无参构造 + 基于属性可空性驱动行可空性，见 required-members.md Motivation）。VBScript.NET 的 source-gen 可为脚本类型生成满足 `MustInit` 的构造/校验样板，并读取 C# 侧 `RequiredMemberAttribute` 在脚本调用点补强制。
- **识别新元数据（双向）**：.vbx 编译器必须认识 `modreq(IsExternalInit)` 与 `RequiredMemberAttribute`/`SetsRequiredMembersAttribute`/`CompilerFeatureRequiredAttribute`——(a) 消费 C# 程序集时，对 C# init/required 类型正确强制（`New` 时校验 required、`With` 里允许 init 写）；(b) 发射时输出同等元数据，否则 C# 端把 VB `InitOnly` 当普通 setter、`MustInit` 无强制。这是"必须桥接"的又一实例（同决策文件 M8 类型）。
- **AOT/trimming**：required 的元数据是纯 attribute（无 modreq），对 AOT 友好；VB 发射时跟随 attribute 路线（而非 modreq）与 C# 二进制兼容目标一致。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #2（存储模型）**：方向保留，但"为什么不是 CLR `initonly`"一节需重写为 C# 一致的表述——不是"字段不能 initonly"，而是"对象初始化器的写永远经访问器；访问器内部可写同类型 readonly 字段（C# 先例）"。这**可能让 Anthony 原示例不必改 `ReadOnly _Id`**（`Suspect`，依赖 CLR 放行机制未核实），需最小原型验证。
- **RESOLUTION #6（元数据/互操作）**：升级为 spec 必备章节，且要覆盖**两套编码**：`InitOnly`→`modreq(IsExternalInit)`，`MustInit`→`RequiredMemberAttribute`+`SetsRequiredMembersAttribute`+`CompilerFeatureRequired`。原第 6 条只覆盖了一半。
- **OPEN QUESTION（默认值 (a)）**：C# LDM-2022-03-21 先例直接背书 (a)，可从"待验证"降级为"有 C# 先例支持"。
- **OPEN QUESTION（`MustInit`+`ReadOnly`）**：C# 答案（字段不可 readonly；属性需 setter/initer）为 VB spec 提供可直接引用的规则。
- **三态判定**：`Consider` 维持。C# 侧完整答案（init 存 modreq、required 存 attribute、强制点在创建表达式）说明方向正确且元数据互通可达；但 MustInit 的规格未熟（泛型/继承/默认值）仍是 Table 级门槛，不因 C# 先例自动升 Active。

### 引用与待核实

- **已逐字核实并引用**：`proposals\csharp-9.0\init.md`（Summary / Detailed Design / Metadata encoding / IL verification）、`proposals\csharp-11.0\required-members.md`（Summary / Enforcement / Metadata Representation / Accessibility / `new()` constraint / `struct` defaults / Syntax questions）、`meetings\2021\LDM-2021-10-25.md`、`meetings\2022\LDM-2022-01-05.md`、`meetings\2022\LDM-2022-01-24.md`、`meetings\2022\LDM-2022-03-21.md`、`meetings\2022\LDM-2022-03-23.md`。
- **OPEN QUESTIONS**：CLR/JIT 对 init 访问器写 `readonly` 字段的放行机制（csharplang 仓库未文档化，属 dotnet/runtime）；`CompilerFeatureRequiredAttribute` 的完整发射/识别规则（提案已列概要，BCL 落点未在本仓库逐一核实）；C# `required` 在泛型普通 `New T` 调用点（非 `new()` 约束）是否另有豁免（提案只写 `new()` 排除）。
