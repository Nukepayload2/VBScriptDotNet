# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。我们本周回到语言设计——过去几周被可空性流分析与 ShapeOf 模式匹配的工作占满了，但这两项工作恰好与本建议共享同一片实现面，所以本次讨论比预想的要顺。

## Agenda

* [Proposal: TypeOf ... Is 类型流分析（TypeOf Flow Analysis）](#proposal-typeof--is-类型流分析)

## Proposal: TypeOf ... Is 类型流分析

_Related: [vblang #304 – Select TypeOf（并入模式匹配）](https://github.com/dotnet/vblang/issues/304)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；ModVB：`ShapeOf` 模式匹配、可空性流分析_

### 场景与缺口

We started from an observation that has been around since VB6's `TypeOf`: 最普通的多态分发——一次 `TypeOf ... Is` 测试后紧跟成员访问——今天强迫作者把强转再写一遍。类型层次越深，样板越多：

```vb
' 今天：'animal.Quack()' 报错，必须写 CType(animal, Duck).Quack()
If TypeOf animal Is Duck Then
    animal.Quack()
End If
```

We see a clear DX parity gap here，而且它恰好是模式匹配想要消灭的那类"仪式遮蔽结构"——只是我们这里**不要求任何新语法**。

全场最强论据来自守卫惯用法。`If TypeOf x IsNot T Then Return` 之后的代码至今仍被卡在宽类型上，而这个模式在真实代码里极高频——Anthony 自己的编辑器工具链里就是 `If TypeOf textManager IsNot IVsTextManager Then Return Null` 这种写法（原文第 9 章）。如果我们能让守卫"付费"，就能在不增加一个关键字的前提下，从真实代码库中移除真实的样板。

```vb
' 守卫之后，animal 必然满足相反的测试 ⇒ 收窄为 Mammal。
If TypeOf animal IsNot Mammal Then Return

animal.ShedHair()
```

### 候选方案

**PROPOSAL A — 完整流敏感类型收窄。** 按建议原文实现：`If`、嵌套 `If`、`Select Case TypeOf`、`AndAlso` / `OrElse` 组合、守卫语句（`Return` / `Exit` / `Continue` / `Throw`）、`If()` 表达式、循环后置条件、LINQ `Where` 均收窄原变量。

**PROPOSAL B — 仅收窄显式局部变量与参数。** 覆盖 A 的 `If` / 守卫 / `AndAlso` 区域部分，但明确排除属性、字段与任意 lvalue。实现面小得多，流状态更健全。

**PROPOSAL C — 不做独立流分析，并入通用模式匹配（ShapeOf）。** `Select Case TypeOf` 本就是主线模式匹配的一部分（#304）。若我们先落 `ShapeOf` 声明模式（`Case pn As USPhoneNumber` 绑定新强类型变量），大部分样板自动消失；`TypeOf ... Is` 的收窄退化为模式变量的特例。

### 权衡：Q&A

- **A vs B：属性/字段值得收窄吗？** 值得——真实代码里 `Me.Customer` 非常多。但属性 getter 每次求值可能返回不同对象，字段可被别处改写；把"可重求值的 lvalue"当作稳定的类型来源，流状态并不健全。而局部变量与参数不可被 shadow、不可被重求值，且重赋值在词法上可见。**结论：v1 取 B 的地盘。**
- **A vs C：独立做会不会抢模式匹配的地盘？** 会。2018 年主线已把 `Select Case TypeOf` 并入模式匹配（#304，standalone 标为 "LDM Reviewed: No Plans"）。在这里单独实现 `Select Case TypeOf` 的收窄，等于为同一件事建两套机制。但 `If TypeOf ... Is ... Then` 与 `IsNot` 守卫**没有**被主线任何工作接管，且是纯增量。**结论：`If` / 守卫部分独立做，`Select Case TypeOf` 部分划给 ShapeOf。**
- **B 的核心主张：收窄只在"启用先前会报错"处生效。** 详见下方 Breaking change 追问。这是本特性安全性的关键，也是我们反复回访的点。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`TypeOf ... Is` 本身无歧义——它是布尔表达式。但建议里的 `Select Case TypeOf animal / Case Mammal` 是**新语法**：今天的 VB 不允许 `TypeOf` 作 `Select` 表达式，`Case IsNot` 也没有既有含义。裸 `Case Mammal` 在"表达式模式"语境（`Case 5` = 相等测试）下与类型名冲突，需要上下文相关文法来决定"这是类型名还是相等表达式"。这正是把它并入模式匹配的又一个理由——模式文法本来就要解决"`Case x As T` 里的 `As` 是模式还是声明"这类判定。

#### 2. 角案例与边界语义

**嵌套收窄与交集。** 嵌套 `If` 逐层收窄成立（`Vertebrate` → `Reptile` → `Snake`）。但：

```vb
If TypeOf animal Is Mammal AndAlso TypeOf animal Is ICanFly Then
    ' 这里需要 Mammal ∩ ICanFly —— 一个类型装不下。
    animal.SecreteMilk()
    animal.Fly()
End If
```

收窄状态必须是一组类型的**合取**，成员查找在合取上取并。这与 ModVB 的交/并类型建议（`proposal-intersection-union-types.md`）共用同一概念。`Probably`：v1 允许合取状态，但仅当合取中每个成员都是单收窄来源时才启用"启用型"绑定；与交并类型团队共用一套表示。

**AndAlso / OrElse 区域。** 规则：`AndAlso` 右侧与整个 `Then` 区域继承左侧的正向收窄；`OrElse` 右侧继承左侧"为假"时的收窄。

```vb
' AndAlso 右侧：TypeOf animal Is Bird 为真 ⇒ animal 收窄为 Bird。
If TypeOf animal Is Bird AndAlso animal.WingspanInMeters > 2 Then
End If

' OrElse 右侧：仅当 IsNot Mammal 为假时求值 ⇒ animal 必为 Mammal。
If TypeOf animal IsNot Mammal OrElse animal.IsMonotreme Then
    mayLayEggs = True
End If
```

该规则只在左侧是**裸类型测试（或 `AndAlso` 链）**时可提取事实；复合布尔（`(a Or b) AndAlso ...`）不产生任何事实。且被测试对象必须是简单 lvalue——`TypeOf GetAnimal() Is Duck` 收窄函数调用的结果无意义。

**负收窄不可表示。** `TypeOf x IsNot T` 的真区域内 `x` 是"非 T"，而类型补集不是 VB 类型——**负收窄不可表示**。它只在其否定的保证区域产生正收窄：守卫之后、正测试的 `Else` 侧、`Do While ... IsNot` 循环之后。因此原文 `Select Case TypeOf dinosaur / Case IsNot Bird` 分支内 `dinosaur.GoExtinct()` **并不演示收窄**——`GoExtinct` 是恐龙基类自己的成员，不依赖收窄即可访问；那个分支是语法演示，不是收窄演示。We `Suspect` 原文此处有概念混写（Anthony 自己都注释 "This metaphor is straining."）。

**守卫语句。** `If ... Then Return`（同样适用 `Exit` / `Continue` / `Throw`）之后，继续区收窄成立。`IsNot` 守卫是零新语法的最大价值点。

**循环体内重赋值与循环后置条件。** 循环结束时，`Do Until` / `Do While` 的条件保证循环后的类型：

```vb
Let animal As Animal = GetDolphin()

Do Until TypeOf animal Is ILandDwelling
    ' 循环体内：条件为假 ⇒ "非 ILandDwelling" 是负收窄 ⇒ 不可表示 ⇒ 无收窄。
    ' 且体内重赋值使收窄每次迭代重置。
    animal = animal.GetImmediateAncestor()
Loop

animal.Walk()
```

Anthony 原文注释自疑："Or maybe I should have said: Do While TypeOf animal IsNot ILandDwelling?"。我们的分析：`Do Until TypeOf animal Is T` 与 `Do While TypeOf animal IsNot T` 的**后置条件等价**（循环结束时都是 `animal Is T` 为真），所以循环后 `animal.Walk()` 均成立——自疑只影响可读性，不影响语义。循环可能零次执行：`Until ... Is T` 在入口即真时体不执行，条件仍真，后置收窄成立。`Do ... Loop Until` 底部测试同理。

**If() 表达式。** 两操作数收窄范围**不对称**：

```vb
Let holders = If(TypeOf quadruped Is Bird,
                 quadruped.Hindlimbs,    ' 真操作数：收窄为 Bird
                 quadruped.Forelimbs)    ' 假操作数："非 Bird" 负收窄 ⇒ 保持宽类型
```

真操作数区域继承正收窄；假操作数区域是"非 Bird" ⇒ 不可表示 ⇒ 保持宽类型，`Forelimbs` 必须是宽类型成员。二元 `If(cond, fallback)`（空值回退）不涉及类型测试，无收窄。

**LINQ Where。** `Where TypeOf mammal Is Bear AndAlso Not mammal.IsExtinct` 后，后续子句（`Order By mammal.HibernationPeriod`）是否继承收窄？范围变量在查询理解中的流分析与普通局部变量是**另一片实现面**。**Table**：核心落地后再议，v1 不做。

**值类型。** `TypeOf x Is Integer` 今天就是非法语法——`TypeOf` 只作用于引用类型与接口。所以收窄天然不涉及值类型/拆箱，`TryCast` 语义分叉不在此特性内。

#### 3. 作用域与绑定

收窄作用于"某个 lvalue 在某个区域内"的静态类型。语义模型里，`animal.Quack()` 处的 `GetTypeInfo` 应返回收窄类型 `Duck`，绑定符号为 `Duck.Quack`；而 `animal` 表达式本身仍是 `Animal`，成员访问前编译器插入等价 `DirectCast`。IDE 补全在收窄区域显示 `Duck` 成员；InfoTip 显示"Animal（收窄为 Duck）"更诚实。我们 v1 不收窄属性，所以"绑到 property 还是 backing field"的问题不存在。

#### 4. 与既有特性的交互

- **Late binding / Option Strict Off**：`animal.Quack()` 在宽松模式下本来就晚期绑定编译。收窄**不得**把既有晚期绑定调用改为早期绑定——那会改变运行期行为与异常时机（编译期报错 vs 运行期 `MissingMemberException`）。**规则：收窄只参与早期绑定解析，绝不改变先前已成功绑定的代码。**
- **ByRef**：把收窄的变量传给 `ByRef` 实参，被调方可改写成更宽类型 ⇒ 调用点后收窄作废。
- **闭包捕获**：lambda 可能在收窄区域外延迟执行 ⇒ 收窄**不流入 lambda 体**；被捕获且在 lambda 内赋值的变量，收窄在外侧作废。
- **多分支赋值**：`Dim x = If(cond, ...)` 等与条件最佳公共类型建议（`proposal-conditional-best-common-type.md`）交互：多分支赋值的收窄是各分支收窄的公共类型，不是其中任一。v1 明确"多分支赋值不产生跨分支收窄"。

#### 5. Breaking change 与兼容性

这是整场最尖锐的追问。收窄**确实**可能改变现有绑定。`Shadows` 是最直接的例子：

```vb
Class Animal
    Public Sub Speak() ' "Animal"
    End Sub
End Class

Class Duck : Inherits Animal
    Public Shadows Sub Speak() ' "Duck"
    End Sub
End Class

Dim animal As Animal = New Duck()
If TypeOf animal Is Duck Then
    animal.Speak()  ' 收窄前：绑定 Animal.Speak。
                    ' 收窄后若不做防护：改绑 Duck.Speak —— 同样源码，重编译后调用不同方法。
End If
```

这正是设计原则所警惕的"隐蔽的控制流/语义变化"（`Return?` 被拒的同类理由）。因此**"不改变运行时语义"的表述不成立**，必须改为**"启用型"规则**：收窄只启用"宽类型上绑定会失败"的成员访问（宽类型上找不到该成员）；先前已成功绑定的代码，绑定原样保留。这条规则要求编译器对同一表达式做"宽类型绑定 + 收窄类型绑定"的双轨试探——可行，但必须写进 spec，并配 `langversion` 门控与警告策略。

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径必须行为一致：收窄在两条路径下都不改变先前成功的绑定；在宽松路径下，先前晚期绑定的调用保持晚期绑定，收窄只在"宽类型上早期绑定失败、而收窄类型上早期绑定成功"时介入。两路径对"已收窄区域"的成员可用性保持一致。

#### 7. IDE / IntelliSense

补全与签名帮助必须反映收窄；需要新的错误文案（如"收窄后成员仍不存在"）。ByRef 改宽后可用成员的变化，IDE 展示需要设计。这些都在原型中验证，不做进规范等于没设计。

#### 8. 数据 / 普遍性

守卫惯用法（`If TypeOf x IsNot T Then Return`）在真实代码里高频——这是本特性最强的普遍性证据。但"数十万安静客户"的业务代码里，类型收窄的刚性需求频次没有量化数据。`Suspect`：这是真实的 DX 增益，但不是头条特性；优先级应排在模式匹配、可空性流分析之后，或与之共享引擎后并行。

#### 9. 更简替代

- 手动 `CType` / `DirectCast`：现状，样板多但确定。
- **ShapeOf 模式变量**（`Case x As T`）：大部分价值、更小实现面、不触碰原变量绑定——这是对我们最有力的竞争者，也正因如此 `Select Case TypeOf` 部分要划过去。
- Analyzer / 重构：可以提示"此处可收窄"，但不能让代码编译，只能当脚手架，不能替代语言特性。

#### 10. 成本 / 优先级

完整 A 的流敏感类型状态（含循环、闭包、查询）接近 C# 可空分析的实现量。B 范围（局部变量 + 守卫 + 区域收窄 + 启用型绑定）是明显可落地的子集。"值得做但太难"的热情不抵消成本——所以我们不启动 A 全量。

#### 11. 运行时 / CLR 硬约束

无。收窄是纯编译期概念；成员访问前插入等价 `DirectCast` 即可，`castclass` 是既有的安全操作，PEVerify 无碍。不触达 CLR 存储规则，无表达式树问题（表达式树按原宽类型生成）。

#### 12. 值不值得做

价值（消除高频样板、零新语法、极 VB）高；成本（最小增量）中；风险（绑定重解析）有"启用型"规则对冲。**值得做——但限定范围。** 若只做完整 A 而拒绝收敛，我们会建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心。这是本特性最亮的地方。
- **不引入"第二种做事方式"（原则 #3）**：`TypeOf ... Is` 已存在，我们只是让它更聪明——零新语法，扩展表面为零。
- **避免隐蔽语义变化（原则 #7）**：唯一扣分项，靠"启用型"绑定规则对冲。没有这条规则，本特性就是又一个 `Return?`。
- **读起来像英语、对新手友好（原则 #5）**：`If TypeOf animal IsNot Mammal Then Return` 之后直接 `animal.ShedHair()`，无需解释、无需多一次强转。
- **与主线关系（对照表 2.3）**：`Select Case TypeOf` 与模式匹配是主线"最期待、分阶段"的领域（#304 已并入模式匹配），本建议与其**主线一致**；`If` / 守卫收窄是主线未接管、Anthony 独立延伸但方向一致的增量。与可空性流分析（同一引擎）和交/并类型（合取表示）必须对齐，否则会造出两套流分析。

### RESOLUTION:

1. **原则上采纳** `TypeOf ... Is` / `TypeOf ... IsNot` 的**编译期纯类型收窄**：不引入新语法、不改变运行时语义、只影响成员解析与报错。这是典型的"让既有惯用法更聪明"——极 VB。
2. **v1 范围 = PROPOSAL A ∩ B**：只收窄**显式局部变量与参数**；不收窄属性、字段与任意 lvalue。
3. **"启用型"绑定规则**：收窄只启用"宽类型上绑定会失败"的成员访问；先前已成功绑定的代码绑定原样保留。`Not all of us are happy with` 双轨试探带来的复杂度，但这是零破坏的唯一路径。
4. **负收窄不可表示**：`IsNot` 只在其否定的保证区域（守卫后、正测试的 `Else`、负测试循环后）产生正收窄；真区域内不收窄。`Case IsNot` 分支无收窄。
5. **`Select Case TypeOf` 划给模式匹配工作项**（与 #304 一致），经 `ShapeOf` 声明模式（`Case x As T`）表达，不在本建议内独立实现。
6. **流分析引擎与可空性流分析共用**一个核心（可空状态 + 收窄类型 + definite assignment）；两建议是同一引擎的两张脸。
7. **收窄不流入 lambda**；局部变量被重赋值、作 `ByRef` 实参、被捕获并赋值 ⇒ 收窄作废。循环后置收窄成立（`Until ... Is T` 与 `While ... IsNot T` 等价）；循环体内无正收窄。
8. **`If()` 两操作数不对称**：真操作数收窄，假操作数保持宽类型。
9. **LINQ `Where` 收窄、属性/字段收窄、交集合取的多类型表示** ⇒ 均列为后续（`Table`）。

### Implication:

- 撰写最小原型：局部变量 + 参数 + `If` / 守卫 + `AndAlso` / `OrElse` 区域 + "启用型"绑定；验证语义模型与 IDE 补全。
- 起草 speclet：流状态模型、事实提取文法（哪些布尔表达式产生事实）、"启用型"双轨试探规则、`langversion` 门控与警告策略。
- 与 ShapeOf 团队对表：避免两套收窄实现；确定 `Case x As T` 复用本引擎。
- 补一份 Compatibility 分析：`Shadows` / 重载重解析、late binding、Option Strict 分叉逐条列证。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`AndAlso` 交集合取（`Mammal ∩ ICanFly`）的多类型状态表示，v1 是否启用，待与交/并类型建议对齐。
- `OPEN QUESTIONS`："启用型"双轨试探对编译时间的影响；若不可接受，退路是"收窄同时触发警告"（`Probably` 可行）。
- `OPEN QUESTIONS`：循环后置收窄与 `Exit Do` / `Continue Do` 交错时的精确规则。
- `TODO`：量化守卫惯用法的真实占比，为数据/普遍性补证据。
- `Follow-up`：与可空性流分析统一引擎后的行为差异表（`IsNot Null` 与 `IsNot Mammal` 必须走同一条路径）。

### 状态

- **LDM 状态：Active（限定范围）**；`Select Case TypeOf` 部分随模式匹配走（standalone 状态为 Table）。
- **三态判定：Active** — 价值真实、范围可缩、风险有"启用型"规则对冲；先行增量（局部变量 + 守卫 + 区域收窄）落地，其余 Table。

---

## 附录：特性评价

# 建议评价报告：proposal-typeof-flow-analysis.md

## 评价对象

- 建议：proposal-typeof-flow-analysis.md — `TypeOf ... Is` 类型流分析
- 来源：Anthony 原文第 1 章 "Type-Inference Enhancements"（`..\AnthonyDesign_wordpress.txt` L65–190；`Select Case TypeOf`、嵌套、`AndAlso`/`OrElse`、守卫、`If()`、循环、LINQ `Where` 全部出自该章）
- 配方目标：类型测试后免强转访问成员，消除多态分发样板；只影响编译期成员解析，不改变运行时语义

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 4/5 | 锚点 4："目标改进明确、边界完整，但证据止于书面（无原型/运行）"。Motivation 清晰（免强转样板）、示例可操作、边界覆盖广（If/嵌套/Select/AndAlso/OrElse/守卫/If()/循环/Where） | 已检查 | 无原型封顶 4；"不改变运行时语义"表述与 Shadowing 重解析事实不符 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"。零新语法、消除样板（原则 #9）、不引入第二种做事方式（#3）、读起来像英语（#5），几乎满格 | 已检查 | 原则 #7（隐蔽语义变化）风险未自行识别，需"启用型"规则对冲 |
| 品质 | 4/5 | 锚点 4："结构完整、边界基本清晰，仅个别细节不精确"。六章节齐全、示例与原文逐字一致、3 个未决问题具体诚实（1–3 健康区间） | 已检查 | 缺 Compatibility/breaking-change 章节；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；Do Until/While 语义差异其实可定却留给未决 |
| 属性 | 4/5 | 锚点 4："主维度明显受益，暗风险小且被显式识别"。雷/水/光正向（提速、差异化 DX、盘活既有惯用法）；暗风险（late binding）被识别但绑定重解析/Shadowing 未覆盖 | 已检查（预测待定） | 风=与模式匹配重叠需协调，文档未充分权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料=Anthony 第 1 章；未声明借鉴 C#（实际比 C# 更激进——C# 收窄的是模式**变量**，本建议收窄**原变量**）；继承 VB 惯用法（`IsNot` 守卫）未点明 | 已检查 | 未显式标注章节号；"更激进于 C#"的对齐论证缺失；无杂质 |

## 设计原则对照

- **与 VB 基因：一致**（消除样板、零新语法、可读、新手友好）；轻微张力于原则 #7（隐蔽语义变化），靠"启用型"绑定规则对冲。
- **与主线关系：主线一致**——`Select Case TypeOf` 是主线模式匹配的组成部分（#304 已并入）；类型收窄/模式匹配是主线"最期待"领域。`If` / 守卫收窄为 Anthony 独立延伸但方向一致。与 `proposal-nullability-flow-analysis.md` 共享引擎；与 `proposal-shapeof-pattern-matching.md` 需协调；与 `proposal-intersection-union-types.md` 在合取表示上交互。
- **破坏性变更：潜在有**——`Shadows` / 重载重解析、late binding 早期化均可能改变既有绑定；建议文档未分析且声称"不改变运行时语义"与事实不符。需"启用型"绑定规则 + `langversion` 门控。

## 总评

- **达成程度：部分达成**——概念与价值成立；安全性论证、兼容性分析、范围收敛未完成。
- **LDM 三态建议：Active（限定范围）**——以"局部变量 + 参数 + 守卫 / `If` / 区域收窄 + 启用型绑定"为 v1；`Select Case TypeOf` 并入模式匹配（standalone 为 Table）。
- **主要问题**：① 绑定重解析的 breaking 风险未分析（Shadowing / 重载）；② "不改变运行时语义"表述不成立，须改为"启用型"；③ 与模式匹配 / 可空流分析的重叠需要单一引擎；④ `Select Case TypeOf` 应划归模式匹配而非独立实现。

## 返工建议

- **补充章节**：Compatibility / breaking-change（`Shadows`、重载重解析、late binding、Option Strict 分叉、`langversion` 门控与警告策略）；明确"启用型"双轨绑定规则。
- **补充证据**：最小原型（局部变量 + 守卫 + 区域收窄）；语义模型 `GetTypeInfo` 与 IDE 补全的验证；编译时间成本估算；守卫惯用法的真实占比数据。
- **未决问题处理**：`If()` 假操作数 = 保持宽类型；`Do Until` / `While` 语义等价（后置条件同为 `T`）；闭包捕获 = 不流入 lambda；循环体负收窄 = 不可表示；交集合取（`Mammal ∩ ICanFly`）与交/并类型建议对齐后决定 v1 是否启用。
- **设计探索**：与可空性流分析统一引擎后的行为差异表（`IsNot Null` 与 `IsNot Mammal` 同一路径）；`Case x As T`（ShapeOf）复用本引擎的接口契约。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的考量，不改动正文与 RESOLUTION。它回答一个问题：当 C# 现实方向压在 CLR/.NET 生态上时，本提案（`TypeOf ... Is` / `IsNot` 编译期类型收窄）是兼容、冲突、还是需要桥接？

### 相关 C# 现实方向

本提案是**纯编译期类型流分析**，与 C#/CLR/.NET 生态的对应走向集中在三处：

- **模式匹配：C# 收窄的是「新变量」，不是「原变量」。** C# 8 的 `is` 表达式里，模式的标识符引入的是全新局部变量，且仅当模式匹配结果为真时 definite assigned：
  > Every *identifier* of the pattern introduces a new local variable that is *definitely assigned* after the `is` operator is `true` (i.e. *definitely assigned when true*).
  > → `proposals\csharp-8.0\patterns.md`（`is-expression` 一节）
  这正是本会议附录评价里那句「C# 收窄的是模式**变量**，本建议收窄**原变量**」的出处。C# 从 C# 8 到 C# 14 从未在「`is T` 测试后改变原变量的静态类型」上松口；类型收窄只发生在 `and` 模式组合的内部输入类型上（C# 9 已实现，`proposals\csharp-9.0\patterns3.md` §Type narrowing）。
- **可空性流分析：C# 唯一「收窄原变量」的先例，但只收窄 null-state。** C# 8/9 的可空性流分析确实对原变量做流敏感收窄，LDM 2017 就拍板过：
  > Feedback: when a variable of a nullable reference type (e.g. `string?`) is known to not be null, we should consider its value to be of the narrower type `string`.
  > → `meetings\2017\LDM-2017-10-11.md`（§Type narrowing）
  结论是混合策略——已知可空引用类型在非空状态下收窄其类型；类型参数因「可能实例化为可空引用类型」无法收窄，只跟踪 null-state（同上 §Conclusion）。这是 C# 中与本提案**最近亲**的机制，也是本会议 RESOLUTION #6「与可空性流分析共用引擎」的直接现实依据。
- **`is not` 负向模式：C# 明确不在这条路上收窄。** C# 9 讨论过 `if (e is not int i) return; M(i);` 是否可行，结论是不允许在 `not` / `or` 模式内声明模式变量：
  > Pattern variables can't be declared beneath a `not` or `or` pattern.
  > → `proposals\csharp-9.0\patterns3.md`（§Variable definitions and definite assignment，Result）
  也就是说，Anthony 提案里最高频的守卫惯用法 `If TypeOf x IsNot T Then Return` 之后收窄原变量，在 C# 里**没有对应物**——C# 宁可要求你 `is not T` 之后拿不到任何绑定，也不承诺负向区域内的类型收窄。
- **C# 15 unions / closed hierarchies：类型系统承担更多静态职责（AOT 驱动）。** 方向与本提案同向——都主张「编译期用类型信息替代运行期反射/分发」：
  > Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive.
  > → `proposals\unions.md`（Motivation）
  > Closed classes provide a way to indicate that a set of derived classes is complete, and allow consuming code to rely on that for exhaustiveness in switch expressions.
  > → `proposals\closed-hierarchies.md`（Motivation）
  但注意：C# 15 靠**新类型（union / closed class）**兑现穷尽性，而非对既有类型的流分析；本提案恰恰是后一条路。

### 现实 vs 提案

| C# 现实方向 | 本提案响应 | 关系 | 理由 |
|---|---|---|---|
| 模式匹配只收窄**模式变量**（新变量），不改原变量类型 | 收窄**原变量**（`If TypeOf x Is T Then` 后 `x` 按 `T` 绑） | **需桥接（差异化）** | C# 从未承诺改原变量类型；本提案更激进。桥接点是「启用型」绑定规则（RESOLUTION #3）——收窄只启用原本绑定失败处，杜绝 Shadowing/重载重解析破坏既有绑定。这是 C# 靠「新变量」自然绕开、VB 靠规则对冲的同一个问题 |
| 可空性流分析收窄原变量 **null-state** | 同一引擎把可空状态 + 收窄类型并起来（RESOLUTION #6） | **兼容** | C# 已确立「原变量可流敏感收窄」的先例；VB 只是把可空状态扩展到完整类型，方向上不冲突 |
| `is not` 负向模式不产生任何绑定（模式变量禁入 `not`/`or`） | `IsNot` 守卫之后收窄原变量 | **兼容 + VB 独有** | C# 主动放弃的区域，正是本提案最高频的守卫惯用法；无冲突，反而是差异化卖点 |
| C# 15 unions/closed hierarchies 靠新类型做穷尽性 | 收窄现有类型层次，不引入新类型 | **兼容** | 目标（编译期静态推理）同向，手段不同（流分析 vs 新类型）。VB 收窄可对 C# 15 的 closed class 直接生效——closed class 是元数据概念，编译器可见 |
| AOT/trimming 压力下减少反射、静态推理 | 收窄是纯编译期，产物仅为 `castclass` | **兼容** | 不引反射、不触 CLR 存储规则、无表达式树问题（正文 §11 已述）。恰好契合 AOT 取向 |

**判断：整体兼容，无冲突；一处需桥接（原变量收窄的安全规则），一处是 VB 独有差异化（`IsNot` 守卫）。** 不硬凑任何与低层内存互操作（Span/ref）、动态/晚期绑定主线的关联——本提案与这些主线无直接交集。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态：** 收窄必须默认保守。「启用型」绑定规则（RESOLUTION #3）正是「默认安全」的实现——宽类型上原本就能绑定的调用一律不动；只在宽类型绑定失败、收窄类型绑定成功处介入。这与 .vbx 在 `Option Strict Off` 下保留晚期绑定（正文 §4 已定：绝不把既有晚期绑定改为早期绑定）是同一安全哲学的两面。
- **识别新元数据：** C# 15 的 union / closed class 会以新特性标志（`CompilerFeatureRequired` 一族）与元数据形态出现。.vbx 若想让收窄在 C# 15 类型上生效，编译器需先**认识这些元数据**（识别 closed class 的「直接派生集合」，从而对 `TypeOf x Is ClosedBase` 的穷尽性推理与收窄边界更准）。这与决策文件 M4 对 union/closed hierarchy 的桥接建议一致。
- **source-gen 桥：** 收窄本身无需 source-gen；但若 .vbx 后续要把「`IsNot` 守卫 + 收窄」模式暴露给 AOT 友好的静态库，可考虑 source-gen 生成显式 `DirectCast`，把流分析的「隐式收窄」降级为「显式转换」，作为逃生舱。这是可选项，不是本提案的必需。
- **与 C# 侧可空流分析的互操作：** C# 可空分析连 `person.FirstName` 这类**属性成员访问**都跟踪（`nullable-reference-types-specification.md` §Member access），而本提案 v1 明确不收窄属性/字段（RESOLUTION #2）。这是可接受的安全取舍，但 .vbx 消费 C# 编译产物的可空标注（`[NotNull]` 流态）时，需知道 C# 侧的收窄可能已作用于属性——收窄的「宽类型绑定」要以 C# 侧标注过的可空状态为准，避免双方流状态不一致。

### 对既有 RESOLUTION / 三态判定的影响

- **不改变** 三态判定（Active，限定范围）——C# 现实方向上无任何迫使本提案降级或改道的因素。
- **强化 RESOLUTION #5**（`Select Case TypeOf` 划给 ShapeOf 的 `Case x As T` 新变量绑定）：这恰好与 C# 模式匹配「新变量」模型一致。C# 用「引入新变量」绕开原变量重绑定风险，ShapeOf 的 `Case x As T` 走的是同一条更稳的路；而独立的 `If` / `IsNot` 守卫收窄原变量才是 VB 的差异化延伸。**建议：** 在 speclet 里把「`If`/守卫收窄原变量 vs `Case x As T` 新变量」明确写成两条不同安全等级的通道，理由引 C# 的这一取舍。
- **强化 RESOLUTION #3 的「启用型」必要性：** C# 没有等价规则，是因为它从不承诺原变量收窄。VB 一旦承诺，就必须用「启用型」对冲——这印证了 LDM 的判定不是过度谨慎，而是对 C# 刻意绕开的那个问题的直接回应。
- **OPEN QUESTIONS（新增）：** C# 15 union/closed class 元数据的具体形态与 `CompilerFeatureRequired` 语义，本索引未深挖，需在 .vbx 实现期到 csharplang `proposals\unions.md` / `proposals\closed-hierarchies.md` 核实后再定收窄如何消费它们。
