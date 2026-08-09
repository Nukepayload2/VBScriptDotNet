# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上一场 TypeOf 流分析会议把我们推进到这里：那次讨论反复撞上"一个值同时是两种类型"的表示问题——`If TypeOf animal Is Mammal AndAlso TypeOf animal Is ICanFly` 之后的合取状态装不进任何单个类型。当时我们把合取表示问题挂起，明确交给交/并类型团队。今天就是来还这笔账的。

## Agenda

* [Proposal - 交集 / 并集类型（Intersection and Union Types）](#proposal---交集--并集类型)

## Proposal - 交集 / 并集类型

_Related: [LDM-2014-02-17](../../vblang/meetings/2014/LDM-2014-02-17.md)（`Select Case Typeof` / CLR 反射顾虑）· [vbldm-notes-2018.05.30](../../vblang/meetings/2018/vbldm-notes-2018.05.30.md)（#304 Select TypeOf 并入模式匹配）· [vbldm-notes-2018.12.19](../../vblang/meetings/2018/vbldm-notes-2018.12.19.md)（模式匹配的 and/or/not 模式）· [vbldm-notes-2017.10.18](../../vblang/meetings/2017/vbldm-notes-2017.10.18.md)（JSON 字面量类型的 `{...}` 语法）· AnthonyDesign section 14「Type-System Enhancements」/ 18.26「Type Predicates」· ModVB `proposal-intersection-union-types.md`、`meeting-typeof-flow-analysis.md`_

### 场景与缺口

We started from a gap that Anthony's section 14 states in two lines:

```vb
' Ad-hoc intersection types.
Let i As {IDisposable, ICloneable} = ...

' Ad-hoc union types.
Let u As {IDisposable Or ICloneable} = ...
```

有一个值必须**同时**满足多个接口（既要 `Dispose` 又要 `Clone`），或**只需**满足其中之一。今天的 VB 表达第一种约束只有三条路：为每个组合预定义命名接口（样板）、退回 `Object`（丢类型信息）、或在泛型约束里写 `Of T As {IDisposable, ICloneable}`（只能活在泛型方法里）。第二种约束更尴尬——没有"是 A 或 B 之一"的静态类型，只能 `Object` + `TypeOf` 手工收窄。

但我们很快意识到，这提议真正接住的线头不在 Anthony 的两行示例，而在上一场会议留给我们的那个挂账。TypeOf 流分析需要一种"类型合取"的内部表示；这个提议恰好给了它一个**用户可书写的拼写**。`{Mammal, ICanFly}` 既是流分析引擎的状态，也是这里想引入的语法。这是整场讨论里交集部分最站得住脚的论据。

还有第二条暗线：Anthony 18.26 提到把空值也当类型谓词处理，变量的类型可以是 `{String, Not Null}`。这条把交集类型推进了可空性标注的领地——但那依赖未定型的 NRT 语义，我们按惯例悬置。

### 候选方案

**PROPOSAL A — 全量交/并集（Anthony 原文方案）。** `{A, B}` 交集 + `{A Or B}` 并集，都作为即席类型，无需预先定义命名类型。

**PROPOSAL B — 只做交集，并集并入模式匹配工作项。** 理由先行：并集类型的全部价值几乎都在"收窄"上，而收窄机器是 TypeOf 流分析 / ShapeOf 模式匹配的地盘。并集类型在模式匹配落地之前是"有类型没动作"的空壳。

**PROPOSAL C — 只用既有机制，不新增类型。** 交集 = 命名接口或泛型约束（语法已在：`Of T As {A, B}`）；并集 = `Object` + `TypeOf` 流分析收窄（流分析已 Active）。零新语法，代价是样板与仪式感。

**PROPOSAL D — 只做编译器内部合取表示，不暴露用户语法。** 交/并类型只是流分析引擎的中间形态，语义模型可见、IDE 可展示，但用户不能写。最窄，解决上一场挂账，但放弃"声明即契约"的价值。

**PROPOSAL E — 引入语法但限定局部变量。** 承认 CLR 无法在元数据中表示交/并类型，因此语法只允许出现在局部变量声明（以及推断产生的内部类型），禁止字段、属性、参数、返回类型、泛型实参。PROPOSAL A 的子集，但把"能在哪用"说清楚。

### 权衡：Q&A

- **A vs B：并集是不是并入了模式匹配？** 我们对照 2018.12.19 的记录，主线对模式匹配的路线是分阶段的，而且明确说 "Maybe _and_ and _or_ and _not_ patterns later (still uncertain on this)"——连**模式级**的 or/and/not 都在"仍不确定"之列，**类型级**的并集当然更没有位置。并集类型的用户旅程是 `TypeOf u Is X` 然后收窄——这正是 ShapeOf 声明模式 `Case x As X` 要消灭的样板。把并集类型先于模式匹配落地，等于把收窄机器建两遍。**结论：并集部分至少在模式匹配前不该做。**
- **A vs E：CLR 装得下交/并类型吗？** 装不下。CLR 没有交/并类型的表示，C# 也没有。2014-02-17 记录里我们曾在泛型模式 `Case As IEnumerable(Of T)` 上碰过同一堵墙，当时的结论是 "Answer: this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"。这里更硬：字段、属性、参数、返回类型、泛型实参都需要元数据可表示的类型，而 `{IDisposable, ICloneable}` 在元数据里没有对应物。唯一可行的降级是：局部变量槽用 `Object` 承载，`i.Dispose()` 在代码生成时展开成 `DirectCast(i, IDisposable).Dispose()`。**这是纯编译期特性，跨不了方法边界。** We think E 不是可选项而是事实——不限定局部变量，这个特性就没有实现路径。
- **交集与泛型约束的关系：是不是新语法？** 这里有个对我们有利的既有事实。VB spec 早已规定 "Multiple type constraints can be specified for a single type parameter by enclosing the type constraints in curly braces (`{}`)"，`Class ControlFactory(Of T As {Control, New})` 是今天的合法代码。也就是说 `{A, B}` 这套花括号逗号语法**不是外来语法**，它就是约束列表语法的推广。更重要的是语义先例：`Of T As {IDisposable, ICloneable}` 里的 `T` 允许无强转调用 `Dispose()` 和 `Clone()`——交集成员的查找逻辑在类型参数上**早就存在**。这个提议只是把"类型参数的约束集"推广到"具体类型的即席交集"。这是整个提案最漂亮的落点。We Suspect：Anthony 自己没意识到这层血缘——他的 proposal 没提约束列表先例。
- **并集的值类型问题。** `{Integer Or String}` 并集里出现值类型时，`TypeOf u Is Integer` 在今天的 VB 里是非法语法（`TypeOf` 只作用于引用类型与接口）。值类型并集的收窄需要全新机制（装箱 + `Equals`/模式），这不是小事。而流分析会议已确认 `TypeOf` 天然不涉及值类型。**并集与值类型的交叉是一整片未开发领域，proposal 没有触碰。**
- **`{Dog, Cat}`：两个类类型的交集。** 一个值不可能同时是两个类，所以交集中出现两个类类型应是编译错误（类只能有一个，接口可有多个）。`{Mammal, ICanFly}` 合法（一个类 + 接口们）。这条规则简单，但 proposal 未写。
- **A vs C：命名接口样板到底多痛？** We think 痛点真实但窄。真实业务代码里"同时满足两个接口"的组合数量通常有限，命名接口是可维护的。即席交集的甜点在于组合爆炸的场景（N 个接口两两组合），而这在业务代码里不常见——更像工具链/框架代码。没有数据支撑普遍性。**更简替代的判断：交集是"消除样板"（原则 #9）的正面案例，并集是"引入第二种做事方式"（原则 #3）的负面案例。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`{A, B}` 在类型位置是全新的——但注意它与**约束列表**位置的冲突。`Of T As {IDisposable, ICloneable}` 里 `{...}` 是约束列表（`T` 必须同时满足两者），`Let i As {IDisposable, ICloneable}` 里 `{...}` 是即席交集类型。同一括号形态、两种解析，语法层面可区分（约束列表只出现在 `Of ... As` 之后），但对读代码的人是同一回事——这**恰好**是好事，因为语义一致。真正的歧义在并集：

```vb
' 并集与交集混写时没有定义优先级。
Let x As {A Or B, C}   ' 是 (A∪B)∩C，还是 A∪(B∩C)？
```

proposal 没有给优先级规则。若加括号语法 `{(A Or B), C}`，又增加一层学习成本。此外 `Or` 在类型位置是关键字，`{A Or B}` 会不会与布尔表达式混淆？不会——类型位置无布尔上下文，但**新增关键字在类型文法中的出现**需要为 Roslyn 加新的语法节点类型，这是管道成本。还有一条来自主线的碰撞：2017-10-18 记录里我们讨论过 `Dim a As {"contact"}` 的 JSON 字面量类型——`{...}` 语法被 JSON 字面量类型预定了（"Type: {"contact"}"）。若两条线都落地，同一个 `{}` 括号要同时表达"交集类型"与"JSON 字面量类型"，IDE 与解析器都要在上下文敏感之外再叠加语义判断。OPEN QUESTION。

#### 2. 角案例 / 边界语义

- **交集中出现两个类** → 编译错误（上面已述）。
- **并集出现值类型** → `TypeOf` 收窄失效，需新机制（已述）。
- **交集/并集为空**：`Let x As {}`？无意义，应拒绝。
- **单元素交集/并集**：`{A}` 与 `A` 等价，是简化或同义语法，无存在必要。
- **`Nothing` 赋值**：`Let i As {IDisposable, ICloneable} = Nothing` 合法（引用类型交集可空）。并集同理。
- **流分析合取**：`AndAlso` 产生的合取状态 `{Mammal, ICanFly}` 直接落到交集类型表示上——这是本特性与流分析共享引擎的核心协议。上一场会议我们把这条写成 OPEN QUESTION，今天有了落点：**合取状态 v1 用交集类型表示，且只在单收窄来源时启用（沿用既有 RESOLUTION #9 的限定）。**

#### 3. 作用域与绑定

语义模型对 `i.Dispose()` 应返回什么？`i` 的 `GetTypeInfo` 应返回一个**合成的交集类型符号**（compound/synthesized type symbol），成员查找在合取上取并——这正是类型参数带多约束时成员查找的既有逻辑（`Of T As {A, B}` 时 `T.X` 在所有约束的成员上查找）。绑定 `Dispose()` 到 `IDisposable.Dispose`，`Clone()` 到 `ICloneable.Clone`，各自按声明类型发出调用，无需强转。IDE 补全在交集上显示并集成员、在并集上显示公共成员——这组对偶很干净。

#### 4. 与既有特性的交互

- **泛型约束**：`{A, B}` 语法在约束位置语义不变（`Of T As {A, B}`），但在类型位置获得新含义。两条解析路径必须共存且不互相污染。
- **TypeOf 流分析**：交集类型 = 流分析合取状态的用户可写拼写 + 内部表示。这是最大的协同面。
- **ShapeOf / 模式匹配**：并集类型的唯一消费方式是收窄，收窄正是模式匹配的地盘。并集在模式匹配落地前价值近乎为零。
- **`Any` 伪类型**：Anthony 的 `Any`（动态分发）与并集是两条不同的路——`Any` 放弃编译期检查，并集保留静态类型。但用户困惑点一致："什么时候用 `Object`、`Any`、还是 `{A Or B}`"。三选一的决策表必须写清楚。
- **ByRef / lambda 捕获**：交/并类型限定局部变量后，`ByRef` 传参与 lambda 捕获的交集变量如何降级？`ByRef` 实参必须是真实 CLR 类型，`Object` 承载的交集变量传 `ByRef Object` 会丢失交集的静态身份，调用后收窄作废。需要与流分析会议相同的 ByRef 规则。
- **表达式树**：交集变量被捕获进 lambda 且该 lambda 转表达式树时，交集类型无法进入表达式树元数据，必须降级为 `Object`。OPEN QUESTION。

#### 5. Breaking change 与兼容性

新语法（类型位置 `{...}`）此前是语法错误，因此**旧代码不会变**——这是"新增语法"的天然后向兼容。真正的风险在三处：① 约束列表与交集类型共享 `{...}`，若实现时误改约束解析路径会破坏现有泛型代码（`Of T As {Control, New}` 必须一字不动地保持原语义）；② `{...}` 与 JSON 字面量类型语法冲突，若主线先落 JSON 字面量类型，本特性需让路或换语法；③ 语义模型新增合成类型符号，第三方 analyzer 若对 `TypeInfo` 的类型做穷举判断可能遇到未知类型种类——但这属于新种类通常可接受的范畴。**整体判定：无直接破坏，有共享语法被污染的间接风险。**

#### 6. Option Strict / 编译选项分叉

`Let i As {IDisposable, ICloneable}` 有显式类型，`Option Strict` 两条路径下都是早期绑定，无分叉。并集 `Let u As {IDisposable Or ICloneable}` 的成员访问在 `Option Strict On` 下只暴露公共成员；`Option Strict Off` 下是否允许晚期绑定访问非公共成员（`u.SomeRandomMethod()`）？proposal 未定义。两条路径行为必须一致，否则并集在 Off 下变成"半个 `Object`"，语义漂移。We think：**并集在 Off 下若开放晚期绑定，等于把并集退化为 `Any`，那就真没必要存在了。**

#### 7. IDE / IntelliSense

交集补全 = 各成员类型成员并集；并集补全 = 公共成员交集。InfoTip 显示 `{IDisposable, ICloneable}`。调试器变量窗口的类型显示需支持合成类型。合成类型符号的显示名（"IDisposable, ICloneable"）需要设计。语义模型的 Roslyn 管道新增一种 TypeSymbol 子类，影响面覆盖 parser→binder→symbol→IDE 四层。

#### 8. 数据 / 普遍性

没有量化数据。proposal 的 Motivation 是真实的（多接口约束样板），但没有用户请求数、没有 issue 统计。Anthony 自己 18.21 写他对判别联合 "I mostly don't" get it——他原话承认对这类特性的欣赏有限。我们 Suspect：并集的真实需求远低于交集；交集的真实需求又远低于流分析（因为流分析是隐式的，交集是显式的）。**普遍性证据缺失，优先级证据链断裂。**

#### 9. 更简替代

- 交集：命名接口（样板但确定）；泛型约束 `Of T As {A, B}`（语法已在、语义已有，但限泛型方法）；TypeOf 流分析合取（隐式、免语法）。
- 并集：`Object` + TypeOf 流分析收窄（已 Active）；ShapeOf `Case x As X` 声明模式（模式匹配落地后覆盖）；`Any` 伪类型（放弃静态检查）。
- **我们认真考虑过 PROPOSAL D（只做内部合取表示）**：它用零语法解决上一场挂账，但牺牲"声明即契约"。We think 内部表示是**必须做**的，用户语法是**可选加**的——顺序不能反。

#### 10. 复杂度 / 成本 / 优先级

编译器新增一种合成类型符号 + 成员查找 + 代码生成降级（`Object` 槽 + `DirectCast` 展开）+ 语义模型 + IDE。成本中等偏上，且必须与流分析引擎共享合取表示（否则两套实现）。**优先级：排在 ShapeOf 模式匹配之后**——因为并集部分依赖模式匹配，而交集部分的内部表示价值（流分析合取）不依赖用户语法。

#### 11. 运行时 / CLR 硬约束

CLR 无交/并类型；元数据无法表示；字段/参数/返回类型/泛型实参一概不行。降级路径是局部变量用 `Object` 槽 + 每次成员访问插入 `DirectCast`（`isinst`/`castclass`，PEVerify 安全）。**特性被 CLR 硬约束钉死在"方法体内"**。跨方法边界传递必须显式强转到具体成员类型——这严重压缩了交集的价值（你声明了契约，却不能把契约传给下一个人）。2014-02-17 的反射顾虑在这里没有触发（我们不依赖反射），但代价是特性只能活在局部。

#### 12. 值不值得做

- **价值**：交集（消除多接口样板 + 流分析合取的用户拼写）中；并集（需收窄机器，价值几乎全部外包给模式匹配）低。
- **成本**：中高，且与流分析/模式匹配有强耦合。
- **风险**：低（无直接破坏），但有 `{...}` 语法与 JSON 字面量类型的碰撞风险、并集与 `Any`/`Object` 的概念重叠风险。
- **打分**：交集 = 价值中 × 成本中 × 风险低，**值得做但必须限定局部变量**；并集 = 价值低 × 成本高 × 依赖未落地的模式匹配，**不值得现在做**。

### VB 基因对照

- **消除常见样板（原则 #9）**：交集正中靶心——组合爆炸场景的命名接口样板是真实负担，`{A, B}` 用最小语法解决。
- **不引入"第二种做事方式"（原则 #3）**：交集不违反——它是既有约束列表语法在类型位置的推广，语义先例（类型参数多约束成员查找）已存在。**并集违反**——`Object`+`TypeOf` 收窄已覆盖该场景，并集是第二套写法。
- **保持 VB-like（原则 #2）**：`{A, B}` 的括号逗号形态就是 VB 的约束列表，读起来是"同时是 A 和 B"，非常 VB。`{A Or B}` 的 `Or` 让类型读起来像英语句子，勉强及格，但 `Or` 在类型位置的语义需要新惯例。
- **默认跟随 C#（原则 #4）**：C# 没有交/并类型，无先例可跟。按"除非有充分理由否则跟随 C#"的纪律，这个特性从出发点就站在需要辩护的位置——交集靠约束列表先例辩护，并集没有可辩护的先例。
- **不为边缘场景加特性（原则 #6）**：并集最危险——它的消费场景几乎全部依赖尚未落地的模式匹配，属于"为未来的边缘场景预支语法"。
- **与主线关系（2.3 对照表）**：`临时交/并集类型` 一行明确写着主线"无"、Anthony"有"、关系"Anthony 独立延伸"。今天我们的结论把它再拆细：**交集**借力主线已有的约束列表语法与流分析合取需求，属"独立延伸但方向与主线共享实现面"；**并集**与主线模式匹配（#304 已并入 pattern matching，standalone 标 "LDM Reviewed: No Plans"）重叠，若做，必须并入模式匹配工作项。

### 诚实分层

- **事实**：VB 已有 `{A, B}` 多约束语法（spec：`Of T As {Control, New}`，约束列表）；类型参数带多约束时成员查找在约束集上取并（交集语义先例）；CLR 无交/并类型且元数据无法表示；`TypeOf` 只作用于引用类型与接口（值类型并集收窄失效）；2018.12.19 主线对模式匹配 and/or/not 模式 "still uncertain"；2017.10.18 主线讨论过 `Dim a As {"contact"}` 的 `{...}` JSON 字面量类型；2018.05.30 主线把 #304 Select TypeOf 并入模式匹配。
- **Probably**：交/并类型的成员查找实现可复用类型参数多约束的既有路径（编译器内部已存在"在多个接口上查找成员"的逻辑）；`Object` 槽 + `DirectCast` 展开的降级对 PEVerify 无碍。
- **Suspect**：Anthony 未意识到交集语法即约束列表语法的推广（proposal 全文未提约束先例）；并集类型在真实代码里的需求频次；"同时满足两接口"组合爆炸场景在业务代码中的占比。
- **OPEN QUESTIONS**：① `{...}` 与 JSON 字面量类型（2017-10-18 会议讨论）的语法碰撞谁让路；② 并集 `Or` 与交集逗号混写（`{A Or B, C}`）的优先级与括号规则；③ 值类型并集的收窄机制；④ 交集变量进表达式树的降级；⑤ 并集在 `Option Strict Off` 下是否开放晚期绑定。
- **TODO**：把"编译器内部合取表示"立项为流分析引擎的共享部件；为交集写局部变量限定下的最小原型（`Object` 槽 + `DirectCast` 展开 + 成员查找复用类型参数路径）；收集"多接口组合样板"的量化数据。

### RESOLUTION:

1. **分拆提案**。`proposal-intersection-union-types.md` 捆绑了两个成熟度完全不同的特性，必须拆开处理：交集与并集。
2. **交集类型：Consider（限定局部变量）**。`{A, B}` 语法是 VB 多约束列表的推广，成员查找先例（类型参数多约束）已在编译器内，降级路径（`Object` 槽 + `DirectCast` 展开）可行且无反射、无 PEVerify 问题。**范围限定**：只允许出现在局部变量声明与流分析合取状态（PROPOSAL E）；字段、属性、参数、返回类型、泛型实参一律禁止，直到有 CLR 表示或合成类型方案（Currently out of consideration）。交集中两个类类型为编译错误；空/单元素交集无意义。
3. **编译器内部合取表示：必须做，且与流分析引擎共用**。上一场会议挂账的 `{Mammal, ICanFly}` 合取状态用交集类型符号表示；实现顺序上，**内部表示先行**（服务流分析），**用户语法随后**（服务即席声明）。两件事分开排期。
4. **并集类型：Table**。并集的价值几乎全部外包给收窄机器（TypeOf 流分析 / ShapeOf 模式匹配），而这些机器尚未落地（模式匹配还在 "still uncertain" 阶段）。`Or` 语法、值类型并集、Option Strict 分叉、与 `Any`/`Object` 的决策表均未定义。**并集并入模式匹配工作项**，与 #304 的处理方式一致（"Will consider part of pattern matching"）。不在模式匹配前独立实现。
5. **拒绝 PROPOSAL D 作为唯一方案**——内部表示必须做，但我们保留"声明即契约"的用户语法作为后续目标，不因"更简单"而永久砍掉交集的用户价值。

### Implication:

- 将 proposal 拆为两份：`proposal-intersection-types.md`（限定局部变量）与 `proposal-union-types.md`（并入模式匹配路线图，状态 Table）。
- 与流分析团队对表：把合取状态表示落为共享的合成类型符号，先做内部表示。
- 撰写交集 speclet：语法文法（类型位置 `{...}` 与约束列表位置的上下文区分）、成员查找规则（复用类型参数多约束路径）、代码生成降级（`Object` 槽 + `DirectCast` 展开）、局部变量限定与 ByRef/lambda/表达式树规则、`{A, B}` 与 `{B, A}` 等价性、类类型限制。
- 补一份与主线语法的碰撞分析：`{...}` 在类型位置的既有使用者（约束列表）与潜在使用者（JSON 字面量类型）的语法预算。
- 未决问题移交至 OPEN QUESTIONS。

### 状态

- **LDM 状态：交集 Consider（限定局部变量，内部表示先行）；并集 Table（并入模式匹配工作项）。**
- **三态判定：Consider** — 交集部分有 VB 基因支撑（约束列表先例）且解决上一场挂账，值得进原型；并集部分在模式匹配落地前不启动，避免为边缘场景预支语法。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-intersection-union-types.md` — 引入即席交集类型 `{A, B}` 与即席并集类型 `{A Or B}`。
- 来源：Anthony 原文章节 14「Type-System Enhancements」（`..\AnthonyDesign_wordpress.txt` L2389–2393），隐含 18.26「Type Predicates」（`{String, Not Null}`）。
- 配方目标：让"同时满足多接口 / 满足其一"的约束直接写在声明处，保留静态类型信息，消除为组合预定义命名接口的样板。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰、示例可演示（`{IDisposable, ICloneable}` 免强转调用两接口）；但并集的关键子效果（收窄后使用）依赖未落地的模式匹配，在孤立提案中无法兑现；证据止于书面。 | 已提供 / 已检查 | 并集效果依赖外部机器；交集价值被 CLR 钉死在局部变量；无量化数据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。交集语法恰好复用了 VB 既有约束列表（`Of T As {A, B}`），VB 化程度高；但并集 `Or` 语法是外来概念（结构类型/判别联合）的直接拼装，未做 VB 化改造；打包了交集+并集两个无关成熟度特性。 | 已检查 | 作者未点明约束列表血缘；并集与 `Any`/`Object` 概念重叠 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与 Anthony 原文逐字一致；但无文法（BNF）、无兼容性分析、无 Option Strict 分叉；4 个未决问题（≥4 关键点）被轻描淡写；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）。 | 已检查 | 缺文法/兼容性/交互分析；未决问题低估；占位符 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（加速多接口样板消除）与水（差异化 vs C#）正；风（与约束列表/JSON 字面量共享 `{...}` 的一致性风险）与暗（并集在模式匹配前的空转、与主线"无"的状态）负；文档对 CLR 局部变量限定这一最大限制只字未提。 | 已检查（待定） | 未识别 CLR 局部变量限定；未识别 JSON 字面量类型语法碰撞；并集依赖外部机器未权衡 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源指向 section 14 正确；但**关键血缘未标注**——交集语法是 VB 约束列表的推广（继承 VB 基因）完全没提；并集借鉴结构类型/判别联合（F#/TypeScript 传统）未声明；18.26 的 `{String, Not Null}` 把空值谓词混入交集，未说明这是可空性标注的隐含依赖（NRT 未定型）。 | 已检查 | 未声明 VB 约束血缘；未声明结构类型借鉴；混入 NRT 依赖未说明 |

### 设计原则对照

- **与 VB 基因：部分一致，部分偏离。** 交集与约束列表语法同源（原则 #2、#9 加分）；并集违反"不引入第二种做事方式"（原则 #3）、在 C# 无先例下缺少跟随依据（原则 #4）、消费场景依赖未落地模式匹配（原则 #6）。
- **与主线关系：Anthony 独立延伸**（2.3 对照表原判定）。交集借力主线已有语法（约束列表）与主线待办（流分析合取表示），方向与主线共享实现面；并集与主线模式匹配工作项（#304 已并入，standalone 无计划）重叠。
- **破坏性变更：无直接**（类型位置 `{...}` 此前是语法错误）；间接风险为共享 `{...}` 语法时对约束列表解析路径的污染、以及与 JSON 字面量类型语法（2017-10-18 讨论过）的碰撞。

### 总评

- **达成程度：部分达成**——交集概念成立且 VB 化程度高（约束列表血缘未被作者识别但真实存在）；并集概念外强中干，价值几乎全部外包给未落地的模式匹配。
- **LDM 三态建议：Consider**（交集限定局部变量，内部表示先行；并集 Table，并入模式匹配工作项）。
- **主要问题**：① CLR 无法在元数据表示交/并类型，特性被钉死在局部变量，proposal 未识别；② 并集价值依赖未落地的模式匹配收窄机器；③ 未识别交集语法 = VB 约束列表语法推广这一最大论据；④ `{...}` 与 JSON 字面量类型语法碰撞未分析；⑤ 无文法、无兼容性、无 Option Strict 分叉、状态占位。

### 返工建议

- **拆分**：proposal 拆为交集（限定局部变量）与并集（并入模式匹配路线图）两份。
- **补充章节**：语法文法（类型位置与约束列表位置的上下文区分、BNF）；兼容性分析（约束列表路径不受污染、`{...}` 语法预算冲突）；局部变量限定与跨方法边界限制专节；Option Strict 分叉；值类型并集讨论。
- **补充证据**：最小原型（`Object` 槽 + `DirectCast` 展开 + 复用类型参数多约束成员查找路径）；"多接口组合样板"频次的量化数据；与流分析引擎共享合取表示的技术方案。
- **未决问题处理**：优先级规则（`{A Or B, C}`）、JSON 字面量类型碰撞、值类型并集收窄、表达式树降级、并集 Option Strict 行为——并集相关的全部移交模式匹配工作项；交集保留"类类型限制、`{A,B}`/`{B,A}` 等价性、ByRef/lambda 降级"三项自行定夺。
- **设计探索**：把"编译器内部合取表示"作为流分析引擎的共享部件先行立项（PROPOSAL D 的工程内核 + PROPOSAL A 的用户语法分两期）；研究交集类型作为 NRT 之后 `{T, Not Null}` 类类型谓词载体的前景（Suspect，依赖 NRT 修订）。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang`（dotnet/csharplang main 分支镜像）核实；方向概要取自索引 `..\..\csharplang-index.md`（T8/M4 与本提案直接相关）。除索引第四节已核实的 6 段原文外，本附录新增引用均对镜像内原文逐字核实，标注来源文件路径。无法核实项见文末 OPEN QUESTIONS。

### 相关 C# 现实方向

本提案撞上的不是 C# 的空白地带，而是 **C# 15 的主线工作**。索引 T8 把 unions/closed hierarchies/discriminated unions 列为 2025–2026 LDM 主线，C# 15 Kickoff 明确表态（`meetings\2025\LDM-2025-08-18.md`）：

> "We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."

具体现实方向如下：

**1. C# 15 Unions——nominal、声明式、有 CLR 元数据表达（`proposals\unions.md`，LDM Approved）**

C# 的 union 是一组 case type 的封闭集合，以**具名类型**声明，且**能在元数据中表达**。动机原文（逐字，`proposals\unions.md`）：

> "Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive."

关键机制：`System.Runtime.CompilerServices.UnionAttribute` 标记 union 类型；case types 由 union creation member（单参构造器 / 静态 `Create` 工厂）的参数类型决定；union behaviors = union 转换（每个 case type 隐式转 union）+ union 匹配（模式匹配"解包"`Value`）+ union 穷尽性 + union 可空性。`union Pet(Cat, Dog);` 声明语法降级为带单个 `object? Value` 引用的 struct（逐字，`proposals\unions.md`）：

> "They declare a struct which uses a single object reference for storing its `Value`, which means: *Boxing*: Any value types among their case types will be boxed on entry. *Compactness*: Union values only contain a single field."

同文档明言 union 是"类型并集"而非判别联合（逐字，`proposals\unions.md`）：

> "The proposed unions in C# are unions of *types* and not 'discriminated' or 'tagged'. 'Discriminated unions' can be expressed in terms of 'type unions' by using fresh type declarations as case types."

**2. Closed hierarchies——穷尽性的另一载体（`proposals\closed-hierarchies.md` + `meetings\2026\LDM-2026-04-20.md`）**

`closed` 修饰符把"可派生集"限在同一程序集，使 switch 穷尽性可用（逐字，`proposals\closed-hierarchies.md`）：

> "Allow a class to be declared `closed`. This prevents directly derived classes from being declared in a different assembly:"

对互操作最相关的是其元数据降级（逐字，`proposals\closed-hierarchies.md` Lowering）：

> "Closed classes are generated with an `IsClosedType` attribute, to allow them to be recognized by a consuming compiler."
> "Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes."

LDM-2026-04-20 继续夯实设计定位（逐字，`meetings\2026\LDM-2026-04-20.md`）：

> "Our guiding principle for `closed` is that it is an exhaustiveness feature, not merely a way of blocking outside inheritance."

**3. Standard unions——BCL 的 `Union<T1, T2>`（`meetings\2025\LDM-2025-07-30.md`）**

LDM 采纳在 BCL 提供标准泛型 union（类似 `Action`/`Func`）。注意其**顺序不归一**，与 VB 提案的 `{A,B}`/`{B,A}` 等价性主张形成直接对照（逐字，`meetings\2025\LDM-2025-07-30.md`）：

> "There is a small concern that it won't be intuitive that these don't unify across different orders of the case types; e.g., `Union<string, bool>` vs `Union<bool, string>`."

**4. C# 无交集类型（已核实）**

在 `csharplang` 全库 Grep `intersection`，无任何交集类型 feature 提案（命中仅为 NRT spec、overload-resolution-priority、private-protected、param-nullchecking 等无关上下文）。C# 表达"A 且 B"只有泛型约束 `where T : IDisposable, ICloneable` 与 type pattern `and` 组合两条路。即席交集 `{A, B}` 在 C# 生态**无对应物**——交集是纯 VB 领地。

**5. 工作组替代设计——匿名 union 的元数据编码先例（`meetings\working-groups\discriminated-unions\TypeUnions.md` / `Runtime Type Unions.md`）**

两份未进 Approved 的工作组文档给出两条匿名/即席 union 的元数据路线，与 VB 的 `{A Or B}` 直接可比：

- TypeUnions.md（Ad Hoc unions，champion #8928）：擦除为 `object` + 自定义属性编码。逐字引用（`meetings\working-groups\discriminated-unions\TypeUnions.md`）：
  > "The type of the ad hoc union is encoded in metadata using custom attributes."（示例 `void M([AdHocUnion([typeof(A), typeof(B)])] object x);`）
  > "Ad hoc unions with the same member types (regardless of order) are understood by the compiler to be the same type."——顺序等价性正是 VB 提案对交集要求的 `{A,B}`/`{B,A}` 等价那条规则。
- Runtime Type Unions.md：runtime 特判 `System.Union<T1, T2>` 抽象泛型类，仅描述性用于元数据，运行时仍是对象引用。逐字引用（`meetings\working-groups\discriminated-unions\Runtime Type Unions.md`）：
  > "The runtime is aware of a special union type that is used only descriptively in metadata to describe the union, but is actually represented at runtime as a simple object reference."

### 现实 vs 提案（兼容 / 冲突 / 需桥接 / 脱节）

| 维度 | 判定 | 理由 |
|---|---|---|
| CLR 元数据表达 | **冲突（需校准）** | 会议正文 Q&A "A vs E" 断言"CLR 没有交/并类型的表示，C# 也没有"。本附录核实：此断言对**交集**成立、对 **nominal union 已不成立**——C# 15 的 `[Union]` + struct + `Value` 是一套可进元数据的表达，closed hierarchies 也用 `IsClosedType` + `CompilerFeatureRequired` 编码。RESOLUTION #2"直到有 CLR 表示或合成类型方案"的前提需更新：C# 侧已有"合成类型方案"先例。 |
| 并集类型 `{A Or B}` | **需桥接** | C# Approved 设计**放弃匿名并集**，只做具名 `union` 与 BCL 标准 `Union<...>`；匿名即席并集只存在于未批准的工作组文档（擦除为 `object` + `[AdHocUnion]`）。VB 若坚持匿名 `{A Or B}`，与 C# 可互操作的形态应是"具名 union 或 `System.Union<A, B>`"，需一层语法/元数据映射。 |
| 交集类型 `{A, B}` | **脱节（VB 特色）** | C# 无交集类型，CLR 也无。交集是纯 VB/ModVB 领地：无 C# 元数据或 API 需要对齐，但也无先例可借；`Object` 槽 + `DirectCast` 仍是唯一实现路径。互操作评估中性偏正——不与 C# 冲突。 |
| 穷尽性语义 | **兼容且 C# 领先** | 会议判定并集"需收窄机器、模式匹配落地前价值为零"。C# 15 的 union/closed hierarchies 正是把"穷尽性 switch"当核心卖点，且与**早已成熟的模式匹配**共存。方向完全同向，但 C# 已把"并集 + 穷尽性"整机做出来——Table 判定被现实印证而非推翻。 |
| 值类型并集 | **可借鉴** | 会议把 `{Integer Or String}` 列为开放问题（`TypeOf` 不作用于值类型）。C# union 基本模式对值类型 case 走**装箱**（单 `object? Value`），另有一套 `HasValue`/`TryGetValue` non-boxing 访问模式（LDM-2025-07-30 采纳，`meetings\2025\LDM-2025-07-30.md`）。VB 并集进模式匹配路线图时可直接借鉴这套"装箱 + 非装箱访问对"方案。 |
| 元数据识别 | **需桥接（最硬）** | VB/VBScript.NET 若消费 C# 15 生态，编译器必须识别 `UnionAttribute`、`IUnion`（`object? Value { get; }`）、`IsClosedTypeAttribute`、`CompilerFeatureRequired`。`IUnion` 的价值原文（逐字，`meetings\2025\LDM-2025-07-30.md`）："This allows code to dynamically test if something is of a union type, and if so, get at its contained value." 这与决策文件 M4、以及与 unsafe-evolution 的 VB 章节（索引第四节）同构：VB 必须认识新元数据才能正确校验跨语言调用。 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：union 是静态类型，天然 AOT/trimming 友好（索引 T5：类型系统承担反射职责）。VBScript.NET 应把"并集 = 静态 union"作默认，`Any`/晚期绑定留给脚本显式 opt-in——与决策文件 M2 的"默认安全、按需动态"双模路线一致。
2. **source-gen 桥**：C# 的 union 转换是"调用 creation member"（`Pet pet = dog;` 降级为 `new Pet(dog)`）。VBScript.NET 脚本侧消费 C# union 时，可由 source generator 为每个 case type 生成强类型创建/访问器（`TryGetCat` 等），把动态分发落在编译期，避开运行时反射。
3. **识别新元数据**：把 `UnionAttribute` / `IUnion` / `IsClosedTypeAttribute` / `CompilerFeatureRequiredAttribute` 列入 .vbx 编译器"需认识的新元数据"清单。尤其 `[CompilerFeatureRequired("ClosedClasses")]`——不认识它的编译器会被 C# 侧禁止派生 closed 类，VB 侧必须同样拒绝，否则跨语言语义漂移。
4. **匿名并集的互操作映射**：若保留 `{A Or B}` 语法，建议规范规定其跨程序集边界时自动降级为 BCL `System.Union<A, B>`（标准 unions）或要求显式具名，否则脚本产物与 C#/F# 程序集无法交换 union 值。
5. **穷尽性对接**：VB 并集若落地，穷尽性语义应对标 C# 的"处理全部 case type 即穷尽、可空时须含 null 分支"（`proposals\unions.md` Nullability 一节）。照抄比自创便宜，且跨语言行为一致。

### 对既有 RESOLUTION / 三态判定的影响

- **并集 Table 判定不变，理由被 C# 现实强化**：C# 15 证明"并集 + 穷尽性 + 模式匹配"是一个整体，VB 在模式匹配前独立做并集是重复建设——与 RESOLUTION #4（并入模式匹配工作项）完全一致。C# 路线图可作为并集实现蓝本。
- **RESOLUTION #2 前提需更新**："直到有 CLR 表示或合成类型方案"——C# 已提供两套可参考的合成类型方案（`[Union]` + struct；工作组 `[AdHocUnion]` 属性擦除）。交集仍限局部变量，但未来若给交集做合成类型，`[AdHocUnion]` 属性编码是现成先例。
- **交集 Consider 判定不变**：C# 无交集类型，无新证据改变"限定局部变量"的结论。

### 引用纪律与未核实项

- 上引全部 C# 原文均为逐字引用，来源为 `..\..\csharplang`：`proposals\unions.md`、`proposals\closed-hierarchies.md`、`meetings\2025\LDM-2025-07-30.md`、`meetings\2025\LDM-2025-08-18.md`、`meetings\2026\LDM-2026-04-20.md`、`meetings\working-groups\discriminated-unions\TypeUnions.md`、`meetings\working-groups\discriminated-unions\Runtime Type Unions.md`。
- **OPEN QUESTIONS**：
  - `[AdHocUnion]` 属性编码的具体形状未定（TypeUnions.md 自注 "The details of this attribute are not yet specified."），且该设计未进 Approved——本附录引作"先例"而非"现实"。
  - C# 15 unions 的最终发布形态与 preview 时间无承诺（LDM-2025-08-18："no release timeframe or guarantee is given at this time"）。
  - `IUnion` 的泛型版已被移除、非泛型版采纳（LDM-2025-07-30），但最终命名/命名空间未定。
  - BCL 标准 `Union<T1, T2>` 的具体形状由 .NET 库团队最终裁定（LDM-2025-07-30："the .NET library team has final say in the shape of these types"）。
