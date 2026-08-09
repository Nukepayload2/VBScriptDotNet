# Visual Basic Language Design Meeting
August 8, 2026

上一场 `TypeOf ... Is` 流分析留了一条尾巴：`If(TypeOf quadruped Is Bird, quadruped.Hindlimbs, quadruped.Forelimbs)` 把"分支区域收窄"与"表达式整体类型"两件事绑在一起。今天我们把后者单独拎出来讨论——`If()` 条件表达式的类型推断。它恰好与 2014 年的未竟工作（vblang #46 "Fix the ternary If operator"）正面相遇，也撞上 Anthony 原文第 1 章 Type-Inference Enhancements 里那段动物学示例。开场十分钟我们就意识到：这不是一个"Object 不够好，改改就好"的提案，它把三件互不相干的事情——推断算法、破坏性变更、推断结果的可用性——压进了同一个决定里。

## Agenda

* [Proposal: 条件表达式（If()）的最佳公共类型推断](#proposal-条件表达式if的最佳公共类型推断)

## Proposal: 条件表达式（If()）的最佳公共类型推断

_Related: [vblang #46 – Fix the ternary If operator](https://github.com/dotnet/vblang/issues/46)；2014 LDM 笔记 #18 "Introduce 'Null' literal"（dominant-type inference 规则，Rejected on 2014-01-06）；[vblang #304 – Select TypeOf（并入模式匹配）](https://github.com/dotnet/vblang/issues/304)；ModVB：`proposal-typeof-flow-analysis.md`、`inactive/proposal-target-typed-conversions.md`（Anthony 自评"会打架"）_

### 场景与缺口

We started from the proposal's observation：当 `If()` 的第 2、3 操作数不能互相转换时，推断失败，回退 `Object`。于是 `If(someCondition, New Cat, New Dog)` 得到一个 `Object`，只能在宽松模式下晚期绑定访问成员，或是在严格模式下直接编译报错：

```vb
' Option Strict On + Option Infer On：
Dim animal = If(someCondition, New Cat, New Dog)
animal.SecreteMilk()   ' 今天：animal 为 Object，严格模式下对 Object 的成员访问是编译错误
                       ' 宽松模式下则晚期绑定，"既慢又易错"（建议原文 Motivation）
```

建议期望：推断为两个操作数的最近公共基类型（best common type），编译期即可解析公共成员。动物学示例里，`Cat` 与 `Dog` 的公共分类阶元是 `Carnivora`：

```vb
' ModVB：animal 推断为 Carnivora
Let animal = If(someCondition, New Cat, New Dog)
animal.SecreteMilk()   ' SecreteMilk 定义在 Mammal 上，Carnivora 继承之，故可用
```

到这里，三个问题立刻浮出水面。**第一**，`Carnivora` 是"正确但意外"的结果——没有任何用户写出过 `Carnivora` 这个词，它是分类学的偶然产物，不是意图的表达（Anthony 自己在注释里承认 "that's not actually useful because not all carnivorans are carnivores"）。**第二**，宽松模式下这是一个实打实的破坏性变更：`Object` 的晚期绑定会被替换成编译期绑定。**第三**，建议自称"可包括接口"，但没有给出接口与类冲突时的优先级。这场会大部分时间都在围绕这三个问题转。

### 候选方案

**PROPOSAL A — 完整 LUB 推断（建议原文）。** 无目标类型时，第 2、3 操作数无单向转换则计算最近公共基类型（类与接口皆可），仍无公共基则回退 `Object`。这是建议的原样形态。

**PROPOSAL B — 目标类型化 `If()`（2014 #46 的未竟工作）。** 不改变无目标类型时的推断；仅在已知目标类型时，让两个操作数都在目标类型语境下解释。这正是 2014 笔记 #46 的记录：

> "Proposal is that we should fix this for them as follows. When interpreting If(x,y,z) in a context in which the desired type is known, then interpret both y and z in the context of that type."

**PROPOSAL C — 显式类型参数 `If(Of T)(cond, a, b)`。** 由用户写出公共类型，推断零改动。**PROPOSAL D — 什么都不做。** 维持 `Object` + 既有 "Object Inferred"/"Object Assumed" 警告（2014 与 2015 笔记均记录了这条回退路径），文档化并建议用户显式写出类型。

### 权衡：Q&A

- **B 与 A 是竞争还是互补？** 互补。`Dim animal As Animal = If(cond, New Cat, New Dog)` 今天就能编译——目标类型 `Animal` 存在，两操作数都可转换。A 只填补"无目标类型"的缺口（`Dim animal = If(...)`）；B 只修补"有目标类型但解释错"的缺口（`Dim x As Integer? = If(True, 0, Nothing)`）。真正的完整修复是 B + A 的组合。但风险极不对称：B 的破坏面是 2014 已裁定可接受的；A 的破坏面是晚绑定语义替换。
- **`Carnivora` 算成功还是事故？** 见下方角案例讨论。我们的共识是：它在语义模型里是"对"的，但作为推断结果对用户是"不可预期的"——这两者必须分开评价。A 的整个争议都集中在后者。
- **C 的仪式感。** `If(Of Animal)(cond, cat, dog)` 与 `CType(If(cond, cat, dog), Animal)` 是同一件事的两种写法，C 没有减少任何样板，只是移动了位置。且 `If` 是编译器内建运算符，`Of` 类型实参文法今天不存在，是纯新增语法。**结论：C 缺乏存在的理由。**
- **D 是伪装的对照组吗？** 不。B 的证据链（2014 #46 的真实用户痛点 + 2016 笔记 `customer?.Name` 展开式 `If(customer Is Nothing, Nothing, customer.Name)`）使 B 能正面击败 D。A 则不同——A 击败 D 的唯一论据是"严格模式未类型化 `If()` 目前报错"，这个频次没有数据。所以 D 对 A 来说仍然活着。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

A 与 B 都不引入任何新语法——`If()` 与 `Dim =` 均已存在。唯一的新文法是 C 的 `If(Of T)`，今天 `Of` 跟在 `If` 后是解析错误，需要文法与优先级定义。既然 C 已被否，本项无歧义。`Suspect`：若未来做泛型 `If(Of T)`，与泛型方法调用的 `Foo(Of T)(args)` 文法的统一是现成模板，但 `If` 不是普通方法，语法绑定要走内建特殊路径。

#### 2. 角案例与边界语义

**推断结果 ≠ 可操作类型。** 这是全场的中心角案例。`If(cond, New Cat, New Dog)` 的 LUB 是 `Carnivora`，但用户实际想要的成员 `SecreteMilk` 定义在更宽的 `Mammal` 上。LUB 算法对"哪个祖先携带用户需要的成员"一无所知——它只能给出最近公共祖先，而最近公共祖先常常不是最可用的祖先。`Carnivora` 恰好能编译（继承 `Mammal.SecreteMilk`），所以示例"工作"了；但它是"刚好够用"而不是"对"。反例更刺眼：若 `Cat`/`Dog` 共同实现接口 `IPet`，而类层次没有共同的非 `Object` 基类，则 LUB 是接口或 `Object`——用户期待的是 `IPet` 还是 `Carnivora`，没有任何原则可依。

**值类型。** 两个无转换关系的值类型，如 `If(cond, New Integer, New Date)`，其类层次公共祖先只到 `System.ValueType`。推断出 `ValueType` 意味着一切访问都装箱——比 `Object` 好不了多少，还引入装箱性能罚金。建议应明确：值类型无 widening/identity 转换时，LUB 计算是否应排除 `ValueType` 直接回退 `Object`。建议未写。

**`Nothing` 操作数。** 2014 笔记 #18 的 dominant-type 规则对裸 `Null` 字面量有专门处理：收集候选类型时 "replace each non-nullable-value-type candidate 'T' with Nullable(Of T)"，例 `Dim x = If(b,5,Null)` 因此推断 `Integer?`。但那条规则属于被拒绝的 `Null` 字面量，不适用于 `Nothing`。今天的 `Dim x = If(b, 5, Nothing)` 仍推断 `Integer`（`Nothing` 不贡献候选类型），正是 #46 记录的那句抱怨——"I wrote the following code, expecting that Nothing would mean a nullable integer, but it came back as the integer 0." A 不解决这个（`Nothing` 不贡献类型，LUB 无从谈起）；B 在有目标类型时解决它。这进一步支持 B。

**泛型实例化。** `If(cond, New List(Of Integer), New List(Of String))`——两个不同的封闭构造类型的公共基只有 `Object`，共享的 `IList` 只有非泛型形态。推断会给出 `Object` 或非泛型 `IList`，两者都不是用户想要的。类型参数与泛型变体的 LUB 计算完全未定义。建议的三个未决问题之一正是"泛型实例化"未确定——这不是一个可以留到实现期的细节，它决定特性是否可用。

**委托。** 两个匿名 lambda（`Sub() ...` 与 `Sub() ...`）作为操作数，LUB 是什么？共同委托类型未定义。`Probably`：与匿名函数/委托推断共用一套候选合并，但建议未提。

**数组字面量一致性。** 2015 笔记记录了数组字面量的分叉：`Dim x = {$"hello", CType(Nothing, IFormattable)}` "will pick 'Object Assumed' in Option Strict Off, and give an error in Option Strict On. The reason is that there is no dominant type between the candidate types 'String' and 'IFormattable'. (There's no widening/identity conversion from one to the other, and there is a narrowing conversion from each to the other)." 若 A 落地而数组字面量不同步，同一个"候选集无 dominant type"的情形会得到两种结果——`If()` 走 LUB、数组字面量走 `Object`。**一致性义务**：A 复活时必须与数组字面量同步，或明确论证为何分叉。

**菱形接口。** `Cat` 与 `Dog` 共享多个接口（`I4Legged`、`IPet`）时，"最近公共"在接口集合上没有唯一定义。建议 Alternatives 里提到"优先选择单个继承类而非接口"作为备选算法——这是承认该问题，但没有选择。

#### 3. 作用域与绑定

A 的推断结果是真实类型，语义模型 `GetTypeInfo` 返回 `Carnivora`，成员查找沿继承链正常解析。没有属性/字段绑定问题（这是局部推断）。但存在一个隐蔽点：**声明的类型变化会泄漏**。`Dim animal = If(...)` 若是模块级（非局部）声明，推断出的类型会成为模块的公开字段类型，改变外部可见 API。建议只给了局部示例，未声明这一范围。

#### 4. 与既有特性的交互

- **晚期绑定 / Option Strict Off**：A 在宽松模式下把 `Object` 晚期绑定替换为编译期绑定。见第 5 条的 `Shadows` 反例。这是 A 最大的交互风险。
- **Option Infer Off**：`Dim animal = ...` 无 `As` 时，Infer Off 下类型固定为 `Object`，不受影响。A 只作用于 Infer On。**但 Infer On 在 VB 中已是多数默认**（项目模板默认开）。
- **目标类型优先序**：`Dim animal As Animal = If(cond, New Cat, New Dog)` 目标类型今天赢。A 落地后必须定义优先级链：目标类型 > dominant type > LUB > `Object`。建议未写这条链。
- **重载解析**：`F(If(cond, cat, dog))`，`F` 有 `F(Object)` 与 `F(Carnivora)` 两个重载——今天选 `F(Object)`，A 后选 `F(Carnivora)`。同一源码重编译换重载。这与 `Shadows` 是同一类风险。
- **与流分析交叉**：上场会议解析的 `If(TypeOf quadruped Is Bird, quadruped.Hindlimbs, quadruped.Forelimbs)` 中，真操作数收窄为 `Bird`、假操作数保持宽类型——该表达式自身的类型是两个分支收窄后类型的公共类型。A 或 B 都必须定义与收窄的优先级；B 的目标类型化规则天然覆盖"表达式位于有目标类型的语境"，与收窄正交。**跨会议一致性义务**。
- **与目标类型转换建议**：Anthony 的 `inactive/proposal-target-typed-conversions.md` 自评"会打架"——若转换表达式靠目标类型省掉类型实参，同时 `If()` 又靠推断给类型，两者会争夺"谁定类型"。B 恰好消解一半冲突（都有目标类型时，目标类型统一裁决）。

#### 5. Breaking change 与兼容性

这是全场最尖锐的追问，且与流分析会议的结论有一个关键差异。流分析收窄可以靠"启用型"规则保全既有绑定（只启用宽类型上绑定失败的访问）；**推断不能**——推断的结果就是变量的类型，变量类型一变，所有下游绑定全变，没有"只启用新增能力"的中间态。`Shadows` 反例：

```vb
Option Strict Off
Option Infer On

Class Mammal
    Public Overridable Sub SecreteMilk()
    End Sub
End Class
Class Carnivora : Inherits Mammal
End Class
Class Cat : Inherits Carnivora
    Public Shadows Sub SecreteMilk()   ' 猫科专属：分泌不同成分
    End Sub
End Class
Class Dog : Inherits Carnivora
End Class

Dim animal = If(someCondition, New Cat, New Dog)
animal.SecreteMilk()
' 今天（Object，宽松模式）：晚期绑定按运行期最派生成员 ⇒ 调用 Cat.SecreteMilk
' A 之后（Carnivora）：编译期绑定 ⇒ 调用 Mammal.SecreteMilk
' 同一源码，重编译后调用不同方法 —— 正是设计原则 #7 所警惕的隐蔽语义变化
```

这与流分析会议记录里的 `Shadows` 例是同一个坑，但结局不同：流分析能靠"启用型"规则躲开，A 躲不开。**结论：A 在宽松模式下必然破坏既有代码，且没有对冲手段。** B 同样是破坏性变更，但有 2014 笔记的裁定背书——"Note that it will be a breaking change. We decided in LDMs in years past that this would nevertheless be good for the language."——且 B 破坏的是"用户预期与实际不符"的错误行为，而不是"用户依赖的正确行为"。这是两者最根本的区别。

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径对 A 的意义截然相反：
- **Option Strict On + Infer On**：今天 `animal.SecreteMilk()` 编译报错（Object 上严格模式禁晚期绑定）。A 让它能编译——**纯增益，无既有行为可破坏**。
- **Option Strict Off + Infer On**：今天晚期绑定工作正常。A 替换为早期绑定——**破坏**。

两条路径行为不一致，且不一致的方向恰好是"严格受益、宽松受损"。可能的分叉策略：A 只在严格模式启用？——这违背清单第 6 条"严格/宽松行为一致"；用 langversion 门控？——可行但建议未写。`Probably`：唯一干净的路径是 langversion + 警告策略（宽松模式下推断类型变化时给警告），但它把一个推断特性变成了解释负担。这是 A 的又一道门。

#### 7. IDE / IntelliSense

补全与签名帮助显示 `Carnivora` 的成员。InfoTip 显示推断类型 `Carnivora`——用户会问"Carnivora 是哪来的"。需要新的诊断文案（如"Inferred type 'Carnivora' from the best common type of the operands"）解释推断来源，否则这个类型是神秘的。既有的 "Object Inferred"（2014）/ "Object Assumed"（2015）警告是现成的文案锚点。B 无此问题——目标类型是用户自己写的。

#### 8. 数据 / 普遍性

A 的唯一无争议场景是"严格模式 + Infer On + 未类型化 `If()` + 操作数无 dominant type"。这个组合的真实频次**没有任何数据**。B 的场景则有 12 年文献记录：2014 #46 记录了真实用户的三段抱怨（"I don't know what Nothing means"等），2016 的 `?.` 提案把 `If(customer Is Nothing, Nothing, customer.Name)` 当作标准展开式。`Suspect`：A 是"改进可演示但普遍性未证"，B 是"痛点被反复记录但幅度小"。按评价标准，Quantitative 数据（如 Implicit-default-optional 的 85% 统计）是分水岭——两者都缺，但 B 有质量更高的定性证据。

#### 9. 更简替代

- **目标类型化（B）**：覆盖"用户心里有目标类型"的绝大多数真实场景（`Dim x As Integer? = If(True, 0, Nothing)`、`Dim x As String = If(customer Is Nothing, Nothing, customer.Name)`、`Dim animal As Animal = If(...)`）。今天已能编译的那一半场景不用动。
- **CType / 类型注释**：零成本、显式、可读。严格模式下的报错用 CType 一行解决，样板是"确定且一次性"的。
- **Analyzer**：可以提示"此处可写显式类型"或建议 LUB 结果，但**不能改变推断类型**，因此无法把严格模式报错变成能编译的代码。Analyzer 只能当脚手架，替代不了语言特性。这与流分析会议的结论一致。
- A 独有的、B 和 CType 都覆盖不到的场景：**严格模式下用户确实想要"编译期绑定 + 不用写类型"**。这个场景真实存在，但频次与价值都未量化。

#### 10. 成本 / 优先级

B 的实现是小的：已知目标类型时改变操作数解释方向，Roslyn 的转换绑定已有目标类型感知路径。A 的实现是中等偏大的：需要一个新的"找公共基 + 接口集合交集"算法，且要在建议完全未定的泛型、值类型、委托、菱形上做设计决定。优先级上，A 排在模式匹配、可空性流分析之后——它服务的场景更窄，且依赖的推断引擎与流分析共享，宜等引擎稳定后搭车。B 可以独立先行。

#### 11. 运行时 / CLR 硬约束

无。A 与 B 都是纯编译期概念；不引入新 IL，不触达 CLR 存储规则，PEVerify 无碍。唯一运行期影响来自 A 的"副作用"——把晚期绑定调用换成直接调用，这正是它破坏性的来源。值类型推断为 `ValueType` 会引入装箱，属性能罚金而非正确性障碍。表达式树按最终类型生成，无额外约束。

#### 12. 值不值得做

逐维打分。**价值**：A 窄（严格模式未类型化 `If()` + 无 dominant type），B 宽（12 年有据可查的 `Nothing` 误判 + 目标类型语境全覆盖）；**成本**：B 小，A 中偏大且算法未定型；**风险**：B 有 2014 裁定背书且破坏的是错误行为，A 破坏正确行为（`Shadows`）且无对冲。结论很清晰：**B 值得做，A 在算法与门控策略定型前不值得。** 这不是"太难的否决"，而是"价值 × 成本 × 风险 逐条打分后的分期"。

### VB 基因对照

- **默认跟随 C#（原则 #4）**：C# 的条件运算符与 VB 同规则（要求分支互相可转换），C# 9 走的是 target-typed conditional expression——即 B 的路线。**A 偏离 C#，且建议没有给出"充分理由"**。这是 A 最重的基因扣分。
- **永不破坏既有代码（原则 #1）**：A 在宽松模式必然破坏（`Shadows`、重载重解析）；B 破坏但被 2014 裁定豁免且破坏的是错误行为。A 顶格违反，B 边缘通过。
- **避免隐蔽语义变化（原则 #7）**：A 是教科书级例子——`Carnivora` 凭空出现，晚期绑定悄悄变早期绑定。这与被拒的 `Return?` 同族。
- **读起来像英语、对新手友好（原则 #5）**：`Dim animal = If(cond, cat, dog)` 然后发现类型是 `Carnivora`——新手无法解释。B 无此问题。
- **消除常见样板（原则 #9）**：B 正中靶心（消除 `CType(..., Integer?)`）；A 也消除样板，但以意外类型为代价，样板换的是"确定性"。
- **不引入第二种做事方式（原则 #3）**：A/B 零新语法（加分）；C 引入 `If(Of T)` 新文法是纯减分。
- **与主线关系（对照表 2.3）**：A 在主线**无对应物**——主线从未计划给 `If()` 做 LUB；最近接的主线工作是 2014 #46（目标类型化，已入 B）。A 属"Anthony 独立延伸且比主线激进"，与 `Null` 字面量（2014 拒绝、Anthony 重点推进）同族；B 属"主线一致"（#46 未竟工作的收尾）。**同一条来源，两条完全不同的主线关系。**

### RESOLUTION:

1. **拆分采纳。** 建议以"完整 LUB 推断"（PROPOSAL A）为全貌，我们**不按原样采纳 A**；采纳其相邻且被 2014 先例覆盖的**目标类型化 `If()`**（PROPOSAL B）。
2. **PROPOSAL B = Active。** 已知目标类型时，两个操作数都在目标类型语境下解释（逐字遵循 2014 #46 的提案句）。修复 `Dim x As Integer? = If(True, 0, Nothing)` 一族 12 年的误判；与 C# 9 target-typed conditional 对齐；破坏性变更获 2014 裁定背书（"nevertheless good for the language"），需配 langversion 门控与警告策略。
3. **PROPOSAL A = Table。** 三个原因叠加：① 算法未定型（接口 vs 类优先、泛型实例化、值类型 → `ValueType`、委托、菱形接口）；② 宽松模式 late→early 绑定是**无对冲**的破坏性变更——推断没有流分析那样的"启用型"中间态；③ `Carnivora` 是"正确但意外"的结果，反直觉、反新手友好。复活条件：先定算法、设计 langversion / 警告门控、用真实代码测量严格模式失败频次。
4. **PROPOSAL C（显式类型参数）= 暂 Table（方向 Reject）。** `If(Of T)` 新文法换来的只是移动 `CType` 的位置，不减少仪式；除非 A 复活并提供比 CType 更省的语义，否则不引入。
5. **PROPOSAL D（什么都不做）对 A 成立、对 B 不成立。** B 的证据链（#46 三段用户抱怨 + `?.` 展开式）击败 D；A 没有足够数据击败 D。
6. **一致性义务。** 若 A 复活，必须与数组字面量（2015 笔记 String/IFormattable 例）同步；必须定义 目标类型 > dominant type > LUB > `Object` 的优先级链；必须与流分析（`If(TypeOf ..., 收窄, 宽)` 表达式的类型）对表。

### Implication:

- 起草 speclet：目标类型化 `If()`——目标类型的判定范围（声明类型、赋值目标、返回类型、实参位置）、`Nothing` 与可空值类型在目标语境下的解释、与 dominant-type 的回退顺序、langversion 门控与警告策略。
- 原型最小实现：目标类型化 `If()` 的操作数重解释；验证 `Dim x As Integer? = If(True, 0, Nothing)`、`If(customer Is Nothing, Nothing, customer.Name)`、`Dim animal As Animal = If(...)` 三族行为。
- 与流分析团队对表：`If(TypeOf ..., 收窄, 宽)` 表达式类型的计算顺序（先收窄、后目标类型化、再 dominant）。
- 补一份数据调查：真实代码库中"严格模式 + Infer On + 未类型化 `If()` + 无 dominant type"的占比，为 A 的复活收集证据。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：A 的 LUB 算法——接口集合与单继承类的优先级、泛型实例化/变体、值类型是否排除 `ValueType`、委托的公共类型、菱形接口。算法不定，A 不复活。
- `OPEN QUESTIONS`：宽松模式下 A 的 late→early 是否有任何可接受的缓解（langversion 门控？推断变更警告？严格/宽松分叉？）——"推断无启用型中间态"的论断是否成立。
- `OPEN QUESTIONS`：B 的破坏面清单——`Shadows`、重载重解析、目标类型化是否影响 `If()` 在查询/表达式树/ByRef 实参中的行为。
- `TODO`：量化严格模式未类型化 `If()` 的失败频次；量化 `Dim x = If(b, 5, Nothing)` 一族在真实代码中的占比（B 的验收数据）。
- `Follow-up`：与 `inactive/proposal-target-typed-conversions.md`（自评"会打架"）协调"谁定类型"的裁决链；与数组字面量 dominant-type 行为的差异表。

### 状态

- **LDM 状态：建议整体 Consider；B（目标类型化）Active；A（LUB 回退）LDM Considering（Table 至算法与门控定稿）。**
- **三态判定：Consider** — 价值真实但被"算法未定型 + 破坏性变更无对冲 + 推断结果反直觉"三重问题拖累；先行增量（目标类型化 `If()`）落地，LUB 回退 Table 待算法与证据补齐。

---

## 附录：特性评价

# 建议评价报告：proposal-conditional-best-common-type.md

## 评价对象

- 建议：proposal-conditional-best-common-type.md — 条件表达式（`If()`）的最佳公共类型推断
- 来源：Anthony 原文第 1 章 "Type-Inference Enhancements"（`..\AnthonyDesign_wordpress.txt` L194–209；`Carnivora`/`SecreteMilk` 示例与注释逐字出自该章）
- 配方目标：`If(cond, a, b)` 第 2、3 操作数无单向转换时，推断最近公共基类型（可含接口），替代 `Object` 回退；使声明变量编译期访问公共成员、避免晚期绑定

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。严格模式未类型化 `If()` 的编译错误确实可被修复（主效果），但① 无原型，证据止于书面；② 关键子效果"可用的成员表面"部分落空——`Carnivora` 不是携带 `SecreteMilk` 的类型，示例靠继承"刚好够用"而非"对"；③ 宽松模式是回归而非改进 | 已提供/已检查（状态行 Prototype/Implementation 为占位链接，无运行证据） | 效果只在严格模式 + Infer On 的窄组合下显现；普遍性无数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。延续 VB 自有 dominant-type 推断基因（2014 #18 的候选集机制），但打包了"意外类型"这一杂质；偏离 C# 默认（C# 条件运算符同规则、C# 9 走 target-typed）且未给充分理由 | 已检查 | "可包括接口"无优先级规则；推断结果 `Carnivora` 违背可读性/新手友好基因 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题诚实具体（1–3 健康区间，加分）；但① 无 Compatibility/breaking-change 分析（`Shadows`、重载、Option Strict 分叉均缺席）；② 核心算法（接口 vs 类、泛型、值类型）停在"规则要点"级，无 spec 精度；③ 状态行为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 破坏性变更在 Drawbacks 里一句带过，未分析"无对冲"的性质；`ValueType`/泛型角案例未覆盖 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光（盘活 dominant-type 资产、消除 CType 样板）与雷（实现面小）正向；暗（宽松模式破坏既有绑定）与风（偏离 C#、与目标类型转换"会打架"）受损，文档未对冲 | 已检查（预测待定） | 破坏性变更无缓解设计；与数组字面量、目标类型转换的一致性义务未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 1 章（标注于示例，未标章节号）；**未标注**与 2014 #46 目标类型化先例、2014 #18 dominant-type 机制的关系——这两者是本建议最该引用的主线先例；未声明偏离 C# | 已检查 | 无杂质但来源欠标注；把"扩展既有 dominant-type 机制"写成"新特性"，先例继承关系不透明 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离。** 一致：零新语法（#3）、消除样板（#9）。偏离：① 宽松模式破坏既有代码（#1 顶格违反）；② 隐蔽语义变化——`Carnivora` 凭空出现、晚期绑定悄悄变早期绑定（#7）；③ 推断结果反直觉、反新手友好（#5）；④ 偏离 C# 默认且无充分理由（#4）。
- **与主线关系：Anthony 独立延伸（偏激进）。** 主线从未计划给 `If()` 做 LUB；本建议与 2014 拒绝的 `Null` 字面量同族（都是"类型推断感知"的延伸）。但其中可拆出的目标类型化部分（B）恰是主线 2014 #46 未竟工作的延续——**同源两条路，主线关系不同**。
- **破坏性变更：有。** 宽松模式下 late→early 绑定语义替换（`Shadows` 例：同一源码重编译调用不同方法）；推断类型泄漏至模块级字段会改变公开 API；重载解析重选。无对冲设计、无 langversion 门控、无警告策略。

## 总评

- **达成程度：部分达成。** 痛点（`Object` 回退）真实、方向（让 `If()` 更聪明）正确；但特性以"全貌"形态交付时，算法未定型 + 破坏性变更无对冲 + 推断结果反直觉三问题使其不可按原样采纳。
- **LDM 三态建议：Consider。** 拆分为两半：目标类型化 `If()`（PROPOSAL B）= **Active**（2014 #46 先例背书、C# 9 对齐、实现小、破坏面是"错误行为"）；完整 LUB 回退（PROPOSAL A）= **Table**（算法与门控策略定稿、真实代码频次数据齐备后复活）。显式类型参数（C）= 暂 Table / 方向 Reject。
- **主要问题**：① LUB 算法（接口/泛型/值类型/委托/菱形）未定型；② 宽松模式破坏性变更无"启用型"式对冲，推断没有流分析那样的中间态；③ `Carnivora` 结果是"正确但意外"的类型，反可读性；④ 未与数组字面量、目标类型转换、流分析定义一致性；⑤ 普遍性无数据。

## 返工建议

- **补充章节**：Compatibility / breaking-change 分析（`Shadows`、重载重解析、模块级字段泄漏、Option Strict 分叉、langversion 门控与警告策略）；优先级链 目标类型 > dominant type > LUB > `Object`。
- **补充证据**：目标类型化 `If()` 最小原型（`Dim x As Integer? = If(True, 0, Nothing)` 一族）；LUB 算法的最小可编译示例矩阵（接口、泛型、值类型、委托、菱形）；严格模式未类型化 `If()` 失败频次的真实代码测量。
- **未决问题处理**：三个未决（算法、可操作性、显式类型参数）中，前两个是 A 复活的前置条件而非可拖延项；第三个随 C 一并 Table。`Nothing` 操作数与 2014 #18 的 `Nullable(Of T)` 规则的关系需在 B 的 speclet 里定论。
- **设计探索**：拆分为两份建议（B 独立成提案、A 标注"算法未定稿"）——当前"一建议二主线关系"的混合形态削弱了它自己的论证；与 `inactive/proposal-target-typed-conversions.md` 的"会打架"裁决链需并案协调。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的 C# 生态考量，不改写正文与 RESOLUTION。它回答一个问题：当 C# 现实方向压在 CLR/.NET 生态上时，本提案（`If()` 条件表达式的最佳公共类型推断）是兼容、冲突、还是需要桥接？所有 C# 引用均逐字取自 ..\..\csharplang 镜像并标路径；无法核实的标注 **Suspect** 或 **OPEN QUESTIONS**。

### 相关 C# 现实方向

本提案的主题——条件表达式的类型统一——在 C# 生态中恰恰是一条**被反复讨论、已被拍板划线**的主线，而非空白地带。C# 的现实结构是「自然类型优先、target-typing 兜底」双轨，外加「算法不改进」的明确决议：

1. **C# 条件运算符的自然类型 = "best common type of a set of expressions"，约束是"不推断输入之外的类型"。** C# 在 LDM 里把这条约束讲得极清楚：

   > …by looking back to an earlier rule: never infer a type that was not one of the input types.
   > → `meetings\2019\LDM-2019-10-28.md`（§Common Type Specification）
   > …preserving the original constraint of the common type algorithm, where no type is inferred that isn't present in any of the inputs.
   > → 同上

   这条原则的直接推论：C# 的条件表达式**不会**为 `b ? new Cat() : new Dog()` 外推出 `Carnivora` 式的最近公共基类——两分支无相互转换时报编译错（CS0173），而不是算 LUB。VB 2014 #18 的 dominant type（候选集中选唯一可转换目标）与 C# 的"候选集内"模型更接近；本提案 A 的 LUB（外推出 `Carnivora`）恰恰越出了这条线。

2. **C# 9 target-typed conditional expression = 目标类型兜底，正是本提案 PROPOSAL B。** 提案正文：

   > For a conditional expression `c ? e1 : e2`, when 1. there is no common type for `e1` and `e2`, or 2. for which a common type exists but one of the expressions `e1` or `e2` has no implicit conversion to that type … we define a new implicit *conditional expression conversion* that permits an implicit conversion from the conditional expression to any type `T` for which there is a conversion-from-expression from `e1` to `T` and also from `e2` to `T`.
   > → `proposals\csharp-9.0\target-typed-conditional-expression.md`（§Conditional Expression Conversion）

   版本史将其记为 C# 9 feature：`Target-typed conditional expressions: conditional expressions which lack a natural type can be target-typed (int? x = b ? 1 : null;).` → `Language-Version-History.md`。配套的 betterness 规则（重载解析时优先非 conditional-expression-conversion）与 cast 场景的最后手段规则（"we prefer any other conversion to a *conditional expression conversion*, and use the *conditional expression conversion* only as a last resort." → 同上）是 B 落地时重载解析的现成模板。

3. **C# 明确拒绝"增强 common type"——不是没想，是讨论后拍板不做。** 这与本提案 A 正面相遇：

   > …improving inference is a breaking change after target-typing is introduced. The proposal introduced is to not improve type inference in the future and consider this an acceptable outcome, given that target typing would satisfactorily resolve most of the examples given, and potentially in a clearer way than improving the common type algorithm.
   > → `meetings\2019\LDM-2019-10-28.md`（§Common Type Specification）

   最贴近本提案 A 的 C# 提案是 #33 "Nullable-enhanced common type"（让 `condition ? 1 : null` 得 `int?`），其原文：`With this change, an expression such as condition ? 1 : null would result in a value of type int?, and an expression such as condition ? x : 1.0 where x is of type int? would result in a value of type double?.` → `proposals\rejected\nullable-enhanced-common-type.md`（Summary）。LDM-2020-09-09 把它移到 **Likely Never**：`Given that the target-typing we added more generally addresses this in most scenarios, we don't believe that the additional "break glass in case of emergency" bar is met with these, and they are moved to Likely Never.` → `meetings\2020\LDM-2020-09-09.md`（§Champion "Nullable-enhanced common type"）。注意：#33 只提升到 `T?`（输入 `int` 与 `null` 的可空化），比 A 的"接口+类 LUB"温和得多，仍被拒——A 比 C# 已判项更激进。

4. **C# 14 first-class span：隐式 span 转换进入标准隐式转换集，扩大的正是条件表达式可统一的"类型面"。** `An implicit span conversion permits array_types, System.Span<T>, System.ReadOnlySpan<T>, and string to be converted between each other as follows…`；`We also add implicit span conversion to the list of standard implicit conversions…` → `proposals\csharp-14.0\first-class-span-types.md`（§Span conversions）。配套 betterness 规则（span 转换 vs 非 span 转换、`ReadOnlySpan<T>` 优先于 `Span<T>`）随 `LangVersion >= 14` 生效（同上 §Better conversion from expression / §Better conversion target）。含义：C# 条件运算符参与重载解析时的类型统一结果会随版本改变（例如数组/string/`Span<T>`/`ReadOnlySpan<T>` 现在可互通），这是索引 T2/T8 所指的"类型统一方向"。

5. **NRT：C# 把 null 状态与类型统一分离处理。** 条件运算符的 null 状态按分支 null 状态计算：`The null state of E1 ? E2 : E3 is based on the null state of E2 and E3…` → `proposals\csharp-9.0\nullable-reference-types-specification.md`（§The conditional operator）。这与本提案把 `Nothing` 处理放进目标类型语境（B）的结构一致——统一算法不管 null，null 由独立规则处理。

### 现实 vs 提案

| C# 现实方向 | 本提案响应 | 关系 | 理由 |
|---|---|---|---|
| C# 9 target-typed conditional（conditional expression conversion 兜底，`int? x = b ? 1 : null;`） | PROPOSAL B（目标类型化 `If()`）= Active | **兼容（同向）** | B 就是 C# 9 特性本身；C# 版本史给出教科书例。C# 的 betterness carve-out（优先非 conditional-expression-conversion）与 cast 最后手段规则是 B 落地时重载解析的现成模板 |
| C# "never infer a type that was not one of the input types" + 拒绝增强 common type（LDM-2019-10-28） | PROPOSAL A（完整 LUB）= Table | **冲突（方向相反）** | `Carnivora` 恰是"输入中不存在的类型"，被 C# 原则直接否决。C# 连温和的 `b ? 1 : null → int?` 都判 Likely Never，A 的接口+类 LUB 比之更激进，且踩中 C# 已拍板的"改进算法=破坏性变更"问题 |
| #33 Nullable-enhanced common type → Likely Never（用 target-typing 覆盖 Nothing 场景） | `Dim x = If(b, 5, Nothing)` 由 B 解决 | **兼容** | C# 的选择正是"靠 target-typing 覆盖多数场景、不碰算法"——VB 的 B 是同一答案；A 等于重提 C# 已判项的更激进版本 |
| C# 14 span 隐式转换进入标准隐式转换集（类型统一面扩大） | `If()` 类型统一未定义 span 分支 | **需桥接（面向未来）** | 若 .vbx 未来支持 ref struct/span，`If(b, intArray, someSpan)` 的公共类型与重载解析须纳入 span 转换与 betterness 规则；VB 的 ref struct 支持目前是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error，消费 C# span 类型分支时应优雅回退 `Object`/报错，不误绑 |
| NRT：null 状态与类型统一分离 | `Nothing` 走目标类型语境（B），算法不碰 null | **兼容** | C# 的结构与本提案 B 一致——统一算法不管 null，null 由独立规则处理 |
| unsafe-evolution：C# 官方声明 VB 无 unsafe/指针、不需 requires-unsafe | 本提案不涉指针/不安全 | **脱节（无交集）** | 仅背景：C# 已把 VB 的"默认安全模型"作为独立现实对待，本提案无需为 unsafe 承担义务 |

**判断：B 与 C# 同向（就是 C# 9 特性）；A 与 C# 已判项冲突（C# 拒绝 enhanced common type，其原则直接否决 `Carnivora`）；一处面向未来的需桥接（C# 14 span 类型统一面）；其余兼容或脱节。** 不硬凑与低层指针/COM/AOT 主线的关联——本提案除 C# 14 span 的间接影响外与这些主线无直接交集。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态：** B=Active 即"默认安全"路线（无 late→early 意外、无 `Carnivora` 惊喜）。建议把 C# 的 "no type is inferred that isn't present in any of the inputs" 直接写进 speclet，作为 A 永久 Table 的判据——它与正文"宽松模式绝不静默改绑定"是同一哲学：推断结果必须是用户可预期的。
- **source-gen 桥：** 条件运算符本身是纯编译期（生成 select/分支 IL），不需要 source-gen。桥点在 **betterness**：若 .vbx 复用 Roslyn 转换绑定，需同步 C# 9 target-typed conditional 的 carve-out（"we prefer any other conversion to a *conditional expression conversion*"）与 C# 14 span 的 betterness 规则，否则同一源码在重载解析上会与 C# 分叉。
- **识别新元数据：** 消费 C# 库时，`If()` 分支可能是 C# 9 target-typed 产出的结果类型，也可能是 C# 14 的 `Span<T>`/`ReadOnlySpan<T>`（ref struct）。VB 当前不支持 ref struct，编译器须能识别 span 转换与 ref struct 元数据，至少做到"遇 ref struct 分支回退 `Object`/报错"，不能把 `Span<T>` 当普通类型误绑。NRT 标注同理（nullability 跨程序集传播，见 null-literal 附录）。这与决策文件 M8 的"必须桥接"要点同源。
- **B 的已知短板（C# 注释过）：** C# LDM 指出 target-typing 在目标类型过宽时失效——`Target-typing may allow more viable types, but it falls over in the presence of type inference or if the target is so broad as to be useless (like if the target-type is object).` → `meetings\2019\LDM-2019-09-18.md`（§Target-typed conditional expression and nullable-enhanced common type）。这意味着 `Dim x As Object = If(cond, cat, dog)` 一族 B 也救不了——正文把 A 的场景定义为"严格模式 + 无目标类型"正是这条短板的 VB 侧镜像。
- **与 C# 15 unions 的关系（未来项）：** 若 C# 15 的 union/closed hierarchy 成为常见公共类型候选，A 若复活其 LUB 与 C# 的穷尽性模型如何交互需提前定调；但按本 RESOLUTION（A=Table）当前不构成动作项。

### 对既有 RESOLUTION / 三态判定的影响

- **不改变三态判定**（Consider；B Active，A Table，C Table/Reject，D 对 A 成立对 B 不成立）。C# 现实双向支持：B 有 C# 9 背书（就是同一特性）；A 有 C# "Likely Never" + "no type not in the inputs" 双重否决。
- **证据升级（建议，非阻断）**：正文「VB 基因对照」说"A 偏离 C#"——更准确的表述是：C# 不是"没做 LUB"，而是**讨论过并明确拒绝** enhanced common type（LDM-2019-10-28、LDM-2020-09-09），拒绝理由正是 A 卡住的两个问题（改进算法=破坏性变更；推断输入之外的类型）。A 的扣分从"偏离默认"升级为"与 C# 共同原则冲突"。
- **补充正文缺口**：正文说"C# 9 走 target-typed（即 B）"——现在可补上"C# 为什么没顺手做 A"：因为 target-typing 引入后改进 common type 算法即破坏性变更（LDM-2019-10-28 逐字）。这为 B 优先、A 缓行提供了来自 C# 的独立佐证，且与正文"B 与 A 是互补而非竞争"的判断相互印证。
- **一处交叉引用确认**：正文引用 2014 #46 "When interpreting If(x,y,z) in a context in which the desired type is known…"——C# 9 target-typed conditional 的 "we define a new implicit *conditional expression conversion*…" 是同一思路的成熟工程实现，可作 B 的 speclet 蓝本。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「never infer a type that was not one of the input types.」→ `meetings\2019\LDM-2019-10-28.md`（§Common Type Specification）
- 「…where no type is inferred that isn't present in any of the inputs.」→ 同上
- 「improving inference is a breaking change after target-typing is introduced. The proposal introduced is to not improve type inference in the future and consider this an acceptable outcome, given that target typing would satisfactorily resolve most of the examples given, and potentially in a clearer way than improving the common type algorithm.」→ 同上
- 「we define a new implicit *conditional expression conversion* that permits an implicit conversion from the conditional expression to any type `T` for which there is a conversion-from-expression from `e1` to `T` and also from `e2` to `T`.」→ `proposals\csharp-9.0\target-typed-conditional-expression.md`（§Conditional Expression Conversion）
- 「It is an error if a conditional expression neither has a common type between `e1` and `e2` nor is subject to a *conditional expression conversion*.」→ 同上
- 「`C1` is not a *conditional expression conversion* and `C2` is a *conditional expression conversion*」→ 同上（Better Conversion from Expression）
- 「we prefer any other conversion to a *conditional expression conversion*, and use the *conditional expression conversion* only as a last resort.」→ 同上（Cast Expression）
- 「Target-typed conditional expressions: conditional expressions which lack a natural type can be target-typed (`int? x = b ? 1 : null;`).」→ `Language-Version-History.md`
- 「Given that the target-typing we added more generally addresses this in most scenarios, we don't believe that the additional "break glass in case of emergency" bar is met with these, and they are moved to Likely Never.」→ `meetings\2020\LDM-2020-09-09.md`（§Champion "Nullable-enhanced common type"）
- 「With this change, an expression such as `condition ? 1 : null` would result in a value of type `int?`, and an expression such as `condition ? x : 1.0` where `x` is of type `int?` would result in a value of type `double?`.」→ `proposals\rejected\nullable-enhanced-common-type.md`（Summary）
- 「Target-typing may allow more viable types, but it falls over in the presence of type inference or if the target is so broad as to be useless (like if the target-type is `object`).」→ `meetings\2019\LDM-2019-09-18.md`（§Target-typed conditional expression and nullable-enhanced common type）
- 「An implicit span conversion permits `array_types`, `System.Span<T>`, `System.ReadOnlySpan<T>`, and `string` to be converted between each other as follows…」→ `proposals\csharp-14.0\first-class-span-types.md`（§Span conversions）
- 「The null state of `E1 ? E2 : E3` is based on the null state of `E2` and `E3`…」→ `proposals\csharp-9.0\nullable-reference-types-specification.md`（§The conditional operator）

### OPEN QUESTIONS / Suspect

- **Suspect**：`nullable-reference-types-specification.md` §The conditional operator 的 null 状态第三条写为「Else the null state is "not null"」——从语义看疑为笔误（应为 "maybe null"）。本附录不据此下结论，仅提示核实。
- **OPEN QUESTION**：C# common type 算法的现代精确条文位于 dotnet/csharpstandard（spec §11.6.3.15 / §12.6.3.16 在本镜像为链接索引），如需在 speclet 里逐字对照 C# 规则，须到 csharpstandard 仓库核实。
- **OPEN QUESTION**：C# 14 span 隐式转换对条件运算符自然类型的精确影响（本镜像 `first-class-span-types.md` 未直接写条件表达式，仅有标准隐式转换集与 betterness 规则），如需在 .vbx 实现期明确 `If()` 遇 span 分支的行为，须再核实 LDM-2024 的 first-class span 会议记录。
- **关系弱申明**：本提案与 C# 低层内存互操作主线（T2 Span/ref、T3 指针/函数指针、T4 COM、T5 AOT/trimming、T6 source-gen）无直接冲突；唯一接触面是 C# 14 span 隐式转换"扩大条件运算符可统一的类型面"这一间接影响（需桥接，见上）。对 T8（C# 15 unions/closed hierarchies）仅在未来 LUB 复活时相关，当前不构成动作项。
