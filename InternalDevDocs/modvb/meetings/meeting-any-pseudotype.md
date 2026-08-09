# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周话题是 `Any` 伪类型——把主线 2017 年 8 月那场 "Late-binding without `Option Strict Off`"（#135 场景）连同它下面的四个提案（#136 `Dynamic` 伪类型、#137 晚期绑定成员访问表达式、#43 变量级晚期绑定、#106 擦除接口）一起翻了出来。这场讨论像一次补课：本建议几乎是把主线没做完的功课重新做了一遍，而它撞上的问题，也几乎一模一样——只是多了一个我们特有的错位：**`Any` 这个血缘标签，贴错了**。

## Agenda

* [Proposal: `Any` 伪类型 Any Pseudotype](#proposal-any-伪类型-any-pseudotype)

## Proposal: `Any` 伪类型 Any Pseudotype

_Related: [vblang #135 – Late-binding without `Option Strict Off`](https://github.com/dotnet/vblang/issues/135)；[vblang #136 – `Dynamic` pseudo-type](https://github.com/dotnet/vblang/issues/136)；[vblang #137 – Late-bound member-access expressions](https://github.com/dotnet/vblang/issues/137)；[vblang #43 – Variable-scoped late-binding](https://github.com/dotnet/vblang/issues/43)；[vblang #106 – Erased interfaces](https://github.com/dotnet/vblang/issues/106)；ModVB：后置转换 `(As Type)`、无类型声明默认类型、注解类型、交/并类型_

### 场景与缺口

We started from a scenario that any VBScript/VB6 migrant recognizes immediately：今天的 VB 里，`ExpandoObject` 这类动态对象被声明为 `Object` 时，成员访问一律走**编译期检查**，编译器不知道 `CreateTime`、`Id` 存在，于是报错。作者只能退回到 `CallByName`、反射或 `IDynamicMetaObjectProvider` 手动绑定——样板一大片，而且写法跟 VBScript 时代完全不是一回事：

```vb
' 今天：Object 声明 ⇒ 编译期检查 ⇒ 报错，只能走 CallByName。
Dim e As Object = New ExpandoObject
' e.CreateTime = Date.Now   ' BC30456: 'CreateTime' 不是 'Object' 的成员
CallByName(e, "CreateTime", CallType.Set, Date.Now)
```

建议要填的缺口就是：一个显式的"动态类型"标记，让 `e.CreateTime = Date.Now` 这种自由赋值合法化，贴近 VB6/VBA 习惯。对于 VBScript.NET 这个方言，这个缺口是**核心**而不是边缘——VBScript 里一切都是 `Variant`，整段整段的迁移代码就是无类型成员读写的连续体。We think 这个动机是真实的，而且比主线当初评估时的语境更重：主线问的是"数十万安静客户"，我们问的是"整片待迁移的 VBScript 代码库"。

但我们一进场就撞上三个问题，它们决定了整场会议的方向：

1. **血缘标签错误。** 建议声称 `Any` "从 VB6/VBA 回归"。但 VB6/VBA 的动态类型是 `Variant`，不是 `Any`；`Any` 在 VB6 里只是 `Declare` 语句（API 声明）的占位伪类型——`Declare Sub CopyMemory Lib "kernel32" Alias "RtlMoveMemory" (Destination As Any, ...)`——**它从来不是变量类型**。把 `Any` 当作"动态分发类型"来继承，是继承错了对象。`Suspect`：建议作者把 VB6 的 `Variant` 与 `Any` 混为一谈了。
2. **它几乎是主线 #136 的复述，而主线没做完。** 2017-08-23 会议讨论 `Dynamic` 伪类型时的记录只有两句问号："Feels like it could be a light-weight solution." 与 "Q: Is it just a safe alias for `Object` or is it more like C# `dynamic` in that it always late-binds even if a static member is available."——**没有 RESOLUTION**。本建议把同样的想法用 `Any` 的名字重新提出，却既没有回答主线的两个问题，也没有引用它们。
3. **`(As Any)` 单表达式形式与前次会议的决议撞车。** 后置转换会议（meeting-postfix-casting）的 RESOLUTION #3 已向本建议发出对齐请求：`(As Any)` 不得独立定文法，需与 `(As Type)` 共用后缀转换解析规则。本建议在未定义文法的情况下又把它写了一遍。

### 候选方案

**PROPOSAL A — `Any` 声明类型（原文原样）。** `Let obj As Any` 使该变量的所有成员读写都走动态分发，编译期不校验成员；`Object` 始终编译期检查。运行时语义未定（DLR 还是反射）。

**PROPOSAL B — 只做 `(As Any)` 单表达式形式，不引入 `Any` 类型。** 建议自己的 Alternatives #3。`someObject(As Any).X.Y().Z` 对单表达式强制晚期绑定。与前次会议的后置转换 `(As Type)` 共享 `(As ...)` 语法空间。

**PROPOSAL C — 主线形态：`Dynamic` 命名 + 变量级晚期绑定（#43 的路子）。** 接受主线 #136 的 `Dynamic` 命名；把"动态"做成**变量级开关**而不是一个新类型——即 #43 "Variable-scoped late-binding" 的声明形态。运行时仍擦除为 `Object`（与注解类型建议一致）。

**PROPOSAL D — 什么都不做，依赖既有机制。** `Object` + `Option Strict Off`（整文件）、`!` 字典访问、`CallByName`、手动 `IDynamicMetaObjectProvider`；若只想要局部松动，用块级 `Option Strict`——主线 2018-02-07 对块级 Option 中 `Option Strict` 的评估是 "Would provide value, possible to do"。

### 权衡：Q&A

- **A vs D：`Any` 比 `Object` + `Option Strict Off` 多了什么？** `Option Strict Off` 是**整文件**的开关，且它同时放松所有宽松行为（隐式收窄、无类型声明等）。`Any` 是**逐变量**的开关：`Option Strict On` 的文件里可以只让一个变量动态。这确实有独立价值——它把"晚期绑定"从文件级粒度降到变量级粒度。但我们立刻反问：`Any` 和"变量级 `Option Strict Off`"（#43）是同一种东西的两种写法吗？如果是，用类型名伪装一个作用域开关，是隐蔽的语义变化（设计原则 #7 的惩罚对象）；如果不是，设计文档必须说明差异，而它没有。
- **A vs C：命名该听谁的？** 主线把同一个东西叫 `Dynamic`（#136）。本建议叫 `Any`，理由是 VB6 血缘。但如上所述，`Any` 的 VB6 血缘是 `Declare` 占位符，不是动态类型——名字继承的合法性站不住。`Variant` 才是 VB 家族动态类型的真名（VBScript 变量即 `Variant`）。`Probably`：如果 VBScript.NET 要继承血统，`Variant` 或 `Dynamic` 都比 `Any` 诚实；`Any` 只会让新读者以为它与 `Declare As Any` 有关，而它没有任何关系。**We think 命名问题不是小节的争辩**——"VB 味儿"是硬审美判据，一个误导性的继承名会沉淀几十年。
- **B 的价值会不会像 #137 一样蒸发？** 主线 2017-08-23 对"晚期绑定成员访问表达式"（#137）的结论是："! means so much."、"There's a feature here, but it might not add its weight."、"The JSON thing is great. Oh, but it already works with ! so all the value just evaporated."——`!` 字典访问已经吃掉了读场景的价值。`(As Any)` 比 #137 多出的增量是**写回**（`obj.Id = 1`），这是 `!` 做不到的。所以 B 的残余价值真实但窄：**写回型动态成员**。但这恰好是 `CallByName` 已经覆盖的场景，B 必须证明它比 `CallByName` 值得一个新语法。
- **A 的运行时落点是谁？** 这是全场的核心拷问。VB 现有的晚期绑定是**反射系**的（`Microsoft.VisualBasic.CompilerServices.LateBinding` 族；2016-05-06 会议讨论元组转换时还专门问过 "What's the impact on the late-binder?"）。`ExpandoObject` 是 **DLR** 类型。若 `Any` 走反射，对 `ExpandoObject`/`IDynamicMetaObjectProvider` 的支持是残缺的；若走 DLR，等于把 C# 编译器十年积累的 call-site 织入工作搬进 VB 编译器。**没有运行时设计，"伪类型"三个字是不成立的。**
- **`Object` 与 `Any` 的边界谁画？** 建议的 Unresolved #1 问了转换，但没给答案。最尖锐的是泄漏：`Any` 变量赋值给 `Object` 变量后，动态行为被静态化——同一对象换只手就失去动态性。这是隐蔽语义变化，不是细节。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`(As Any)` 与后置转换 `(As Type)` 共享 `(As ...)` 词法入口。`f(As Integer)` 与 `f(As Any)` 词法上可区分（`As` 不是合法实参表达式），但**类型文法内部的括号**同样纠缠：`x(As Any)(0)` 是"转 Any 后索引 0"还是语法错误？`As Any` 若允许 `As Any()`（动态数组？）与 `As Any` 并存，解析规则必须写进 spec。前次会议已决议：**本建议不得独立定文法**。此外，若 `Any` 成为内建类型名，`As Any` 在 `Declare` 语句里的旧含义（若有迁移）与变量声明的含义冲突。

#### 2. 角案例与边界语义

- **写回规则**：`obj.Id = 1` 的赋值兼容性——DLR 在运行时做转换，反射在运行时做 `Coerce`。`obj.Id = "text"` 赋值给一个实际类型为 `Integer` 的动态成员，运行时抛什么、何时抛？
- **ByRef**：`Sub Set(ByRef target As Any, value As Any)`——动态对象的 ByRef 传递在反射系下要 copy-in/copy-out，2016-05-06 已记录过 `obj.LateBoundCall(M())`（`M` 返回 `ByRef`）必须 copy-back 的规则。`Any` 的 ByRef 是否继承？DLR 系的 `CallSite` 与 ByRef 语义完全不同。
- **泛型**：`List(Of Any)` 擦除为 `List(Of Object)` + 注解（与 annotated-types 建议一致——该建议原文就写明 "C# 用同样手段表示 `dynamic`（运行时仍是 `System.Object`）；ModVB 也会对 `Any` 类型这样做"）。但 `Of Any` 的成员访问在 `List` 迭代时是否保持动态？流类型与 `Option Infer` 推断 `Dim x = list(0)` 得到什么？
- **`Async Function` 返回 `Any`**：无类型声明建议（typeless-declarations）已把 `Async Function Fetch(objectId)` 的返回默认定为 `Any`，按 `Task(Of Any)` 处理——`Await` 一个 `Task(Of Any)` 得到动态类型，`Await` 表达式之后的成员访问算谁的？
- **可空性**：`Any` 是否允许 `Any?`？`Nothing` 赋给 `Any` 是 `Object` 语义（2014-02-17 记录过 "Dim x = Nothing infers Object even with Option Strict On"）还是别的？

#### 3. 作用域与绑定

语义模型里 `obj.CreateTime` 返回什么符号？`Any` 变量没有编译期成员表，绑定结果应是"动态调用节点"而非 `ExpandoObject.CreateTime`。`GetTypeInfo` 返回 `Any`；`.X.Y().Z` 链上的每一跳都进动态调用。IDE 补全在 `obj.` 后无成员可显——这是特性的事实，不是缺陷，但必须与"错误进运行时"的后果一起设计。一个必须回答的问题：**动态调用与 `TypeOf` 流分析如何交互**？TypeOf 流分析（meeting-typeof-flow-analysis）v1 只收窄显式局部变量与参数——`If TypeOf a Is ExpandoObject Then a.` 之后，`a` 是 `Any` 还是收窄为 `ExpandoObject`？若收窄，动态被静态化，语义变化；若不收窄，流分析的"零新语法收益"在 `Any` 上失效。我们倾向 v1 明确**排除 `Any`**：流分析只作用于静态类型。

#### 4. 与既有特性的交互

- **Late binding / `Option Strict Off`**：`Option Strict Off` + `Object` 已提供整文件晚期绑定。`Any` 是逐变量版本。两者并存时，"到底哪个是动态"的困惑是建议 Drawbacks 自己承认的。矩阵：`Object` × {Strict On/Off} × `Any` = 三种绑定行为，规则必须写死，否则同文件内行为分裂。
- **`!` 字典访问**：主线 #137 的教训——读场景的价值已被 `!` 吃掉。`Any` 必须论证写回场景的独立价值。
- **`CallByName` / 反射**：现有显式机制。`Any` 是它们的语法甜点还是替代品？若是替代，`CallByName` 的动态名字传参（`CallByName(e, name, ...)`，`name` 是变量）`Any` 表达不了——**`Any` 只能写死成员名，覆盖不了变量成员名**。这是个真实的表达力缺口，`Any` 不能完全替代 `CallByName`。
- **Lambda / 闭包**：动态调用在 lambda 内如何表示？表达式树（`Expression.Dynamic` 是 C# 的 DLR 表示）——VB 编译器若要支持 `Any` 进表达式树，DLR 是唯一路径；反射系无法进表达式树。
- **元组 / 命名注解**：`Any` 作为元组元素、注解类型（annotated-types）的基座——伪类型家族需要统一机制。

#### 5. Breaking change 与兼容性

**潜在破坏，不是纯增量。** `Any` 今天**不是** VB.NET 保留字——`Dim Any As Integer` 合法。引入内建 `Any` 类型后：
- 用户自定义类型/变量/成员名 `Any` 被遮蔽或改绑；
- `Dim x As Any` 从"类型未定义"报错变成"绑定到伪类型"——若用户项目里恰好有 `Class Any`，语义翻转。

`(As Any)` 表达式形式是纯增量（今天是语法错误，`As` 不是合法实参），但声明形式有真实的标识符冲突面。**必须配 `langversion` 门控与警告策略**，并逐条列证。`Suspect`：建议完全没有兼容性章节——这是弱提案红旗清单的命中项。

#### 6. Option Strict / 编译选项分叉

`Any` 的存在让"Option Strict"的含义出现分叉：`Any` 变量在 `Option Strict On` 下仍然动态（这是它的定义），那么 `Option Strict` 的文档语义要改写——"Strict"不再等于"无晚期绑定"，而是"晚期绑定必须显式标注"。这一条可以接受，但**必须写进 Option Strict 的语言规范**。`Option Explicit` 同理：无类型声明默认 `Any`（typeless-declarations）后，`Option Explicit On` 是否仍强制显式声明？VBScript 的 `Option Explicit` 与 VB6 语义必须对齐。两条路径（Strict On/Off）对 `Any` 的行为应**一致**——`Any` 就是"显式标注的动态"，不受 Strict 影响。

#### 7. IDE / IntelliSense

`obj.` 无补全（动态）；`obj.CreateTime = ` 的赋值右侧无类型推断；错误从编译期移到运行期（`MissingMemberException`）；InfoTip 显示 `Any`。`(As Any)` 使链上所有跳都失去补全。这些都能做，但工作量不小（补全抑制、动态调用渲染、错误文案），且**不做进规范等于没设计**——建议只字未提。

#### 8. 数据 / 普遍性

建议没有量化数据。但对 VBScript.NET：**普遍性是压倒性的**——VBScript 一切皆 `Variant`，迁移代码的成员读写全是动态的。这是与主线评估最大的分野：主线 #136 的价值存疑是因为它的客户群（"hundreds of thousands of quiet customers"）静态类型为主；VBScript.NET 的客户群就是动态类型原住民。`Probably`：这是本建议在"普遍性"维度上唯一真正站得住的地方，也是最该补充数据的地方（VBScript 迁移代码中无类型/动态成员访问的占比）。

#### 9. 更简替代

- `Object` + `Option Strict Off`：已有，文件级。
- 块级 `Option Strict`：主线 2018-02-07 评估 "Would provide value, possible to do"——比 `Any` 更小、不新增类型，但也是文件/块级，不是变量级。
- `!`：读场景。
- `CallByName`：写回场景，且支持动态成员名（`Any` 做不到）。
- `IDynamicMetaObjectProvider` 手动绑定：最大控制，最繁琐。
- Analyzer / 重构：能提示"这里可用 `Any`"，不能替代语言特性。
- **`Variant` 回归**（正名后的 A）：如果我们坚持做，这可能是最诚实的形态。

`Probably`：`Any` 的本质是"变量级显式晚期绑定"——这是 #43 的概念，用一个类型名伪装作用域开关。**更简替代 = 变量级 Option Strict 或声明修饰符**，`Any` 类型反而是绕路。

#### 10. 复杂度 / 成本 / 优先级

取决于运行时落点：反射系——绑定改动中等，但 `ExpandoObject`/DLR 类型支持残缺；DLR 系——绑定与表达式树改动大（C# 用了多年才成熟），且 VB 编译器需要新的动态调用基础设施。无论哪条，加文法、语义模型、IDE、规范，都是中大型特性。对于 VBScript.NET：**高价值，中高成本**。若只想要迁移话术，块级 `Option Strict` + `!` + `CallByName` 的组合已经覆盖大部分；`Any` 是"最后一公里"的完整体验。优先级：排在流分析、模式匹配之后。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍。`Any` 不能是真正的 CLR 类型——必须擦除为 `Object` + 注解（`DynamicAttribute` 等价物），与 annotated-types 建议一致。DLR call-site 是合法 IL。反射系的 `LateBinding.InternalLateCall` 族是既有的运行库调用。真正的硬约束是**表达力**：反射系不支持表达式树；DLR 系要求 `System.Linq.Expressions.DynamicExpression` 与 `CallSiteBinder` 在目标运行时可用。`Probably`：VBScript.NET 若跑在完整 .NET 上，两条路都通；若受限运行时，DLR 依赖成问题。

#### 12. 值不值得做

- **价值**：对 VBScript.NET 高（迁移）；对主线低（静态客户群）。写回动态成员是 `!` 之外的独立增量；变量级粒度是 `Option Strict Off` 之外的独立增量。
- **成本**：中高（运行时落点未定，文法未定，IDE 未设计）。
- **风险**：血缘误导、与后置转换文法冲突、标识符冲突破坏面、与 `CallByName` 表达力缺口（动态成员名）。
- **综合**：**值得做——但不是这份建议。** 概念值得进 Consider，形态不成熟。

### VB 基因对照

- **消除常见样板（原则 #9）**：部分命中。消除 `CallByName`/反射样板，但引入"第三/四/五种动态写法"的选择负担。`obj.Id = 1` 对比 `CallByName(e, "Id", CallType.Set, 1)` 确实更 VB。
- **不引入"第二种做事方式"（原则 #3）**：硬违背，重罚项。晚期绑定已有 `Option Strict Off` + `Object`；`Any` 是第二套。`(As Any)` 是第三套。主线 #137 已经用 `!` 证明过"价值可能不够重"。
- **保持 VB-like（原则 #2）**：形式像（`As Any` 读起来像 VB6），血缘假（VB6 的 `Any` 是 Declare 占位符，动态类型是 `Variant`）。"看起来像 VB"与"真的是 VB"是两回事。
- **避免隐蔽语义变化（原则 #7）**：风险项。`Any → Object` 动态静态化、`List(Of Any)` 元素流类型、`Async Function` 返回 `Any` 的 `Await` 结果——都是隐藏陷阱。
- **读起来像英语、对新手友好（原则 #5）**：`Let obj As Any = New ExpandoObject` 零括号、零前缀，新手能读。这是真优点。
- **不与既有语法冲突（原则 #8）**：`(As Any)` 与 `(As Type)` 文法冲突（前次会议决议）；`Any` 标识符与既有代码冲突。双重命中。
- **与主线关系（对照表 2.3）**：2.3 表中无 `Any` 伪类型——它属 **Anthony 独立延伸**，但与主线 **#135 场景及其 #136/#137/#43/#106 提案重叠**，且主线**无决议**（2017-08-23 只记了两句问号）。命名与主线 #136（`Dynamic`）不同。与 typeless-declarations（默认类型 `Any`）、annotated-types（`Any` = `Object` + 注解）、array-pseudotype（伪类型家族）、交/并类型必须对齐，否则"伪类型"机制四分五裂。

### RESOLUTION:

1. **认可动机，不采纳形态。** "逐变量、显式标注的晚期绑定"这个缺口是真实的——主线 #136 把它叫 `Dynamic` 伪类型并说 "Feels like it could be a light-weight solution"，对 VBScript.NET 它更是核心。但本建议作为一份设计，未达到进入实现的门槛。
2. **血缘正名。** VB6/VBA 的动态类型是 `Variant`，不是 `Any`；`Any` 只是 `Declare` 语句的占位伪类型。若目的是继承 VB 家族血统，命名应为 `Variant`（VBScript 的直接祖先）或接受主线 #136 的 `Dynamic`。`Any` 作为"动态分发类型"的名字，**不被采纳**。`Suspect` 原文的继承标注，需作者澄清意图。
3. **运行时落点先于一切。** 在 DLR 与反射两条路之间定案之前，本建议不得自称"设计"。`ExpandoObject`/`IDynamicMetaObjectProvider` 的支持矩阵、表达式树能力、受限运行时依赖，全部由运行时落点决定。`Probably`：VBScript.NET 应选 DLR（否则 `ExpandoObject` 场景残缺），但这是成本决定，不是偏好。
4. **`(As Any)` 单表达式形态 = Table。** 接受后置转换会议的 RESOLUTION #3：`(As Any)` 不得独立定文法，须与 `(As Type)` 共用后缀转换解析规则；且其读场景价值已被 `!` 覆盖（#137 教训），写回场景需先于 `CallByName` 证明独立价值。示例 `? someObject(As Any).X.Y().Z` 里的 `?` 是即时窗口提示符，**不是合法 VB 语句**——与本系列 postfix-casting 相同的错误，必须替换为可编译示例。
5. **表达力缺口必须承认。** `Any` 只能写死成员名，覆盖不了 `CallByName(e, name, ...)` 的**变量成员名**场景。若 `Any` 定位为 `CallByName` 的替代品，它不成立；若定位为补充，Drawbacks 必须说清边界。
6. **兼容性分析是进入 Consider 的前置条件。** 逐条列证 `Any` 标识符冲突（`Dim Any As Integer` 今天合法）、`langversion` 门控、警告策略、`Object ↔ Any` 转换与动态静态化规则、`List(Of Any)`/`Async Function` 返回的流类型。
7. **与 Option Strict / Option Explicit 规范同步。** `Any` 使 "Strict = 无晚期绑定" 的表述失效，必须改写为 "晚期绑定必须显式标注"；`Option Explicit` 与无类型默认 `Any` 的交互（typeless-declarations）须同步定案。
8. **状态：核心概念 Consider，`(As Any)` 形态 Table。** 见下。

### Implication:

- 将本建议标注为 **LDM Considering**；在建议头部注明关联 #135/#136/#137/#43/#106、血缘正名结论、以及"运行时落点未定即不进入设计"的门槛。
- 回复后置转换会议的对齐请求：确认 `(As Any)` 不再独立定文法，等待后缀转换解析规则的共用草案。
- 与 typeless-declarations / annotated-types / array-pseudotype 团队对表，统一"伪类型/注解类型"的擦除表示（`Object` + 注解）与 `Task(Of Any)` 语义。
- 起草一份"动态绑定写法全景"备忘：`Option Strict Off` + `Object`、`!`、`CallByName`、`IDynamicMetaObjectProvider`、块级 `Option Strict`、`Any`/`Variant`/`Dynamic`——作为将来任何"动态类型"提案的前置阅读材料。
- 为 VBScript.NET 启动一个小型采样：统计迁移代码中动态成员访问（读/写）与 `Variant` 用法的占比，为普遍性维度补数据。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**主线 #136（`Dynamic` 伪类型）与 #43（变量级晚期绑定）的最终状态无法核实**。2017-08-23 会议纪要记录了 `Dynamic` 伪类型的讨论与两句问号，但**没有记录 RESOLUTION**，直接转入后续议程；仓库的 proposals 目录中也找不到 #136/#43 的提案正文。请勿将本会议的任何结论当作主线对 #136/#43 的裁定。`Suspect`：也许决议发生在未整理的会话或邮件列表中，需外部核实。
- `OPEN QUESTIONS`：`Any` 与 `Object` 之间是否允许隐式转换？若允许，动态静态化泄漏如何防？若不允许，`Let a As Any = GetObject()` 的迁移成本是什么？
- `OPEN QUESTIONS`：`TypeOf a Is ExpandoObject` 流分析是否收窄 `Any`（我们倾向 v1 排除，但未定案）。
- `OPEN QUESTIONS`：`Any` 作为 `Declare` 占位符类型的旧语义是否保留、是否与动态 `Any` 冲突。
- `TODO`：运行时落点决策记录（DLR vs 反射，含成本估算与 `ExpandoObject` 支持矩阵）。
- `TODO`：VBScript 迁移代码动态访问占比采样。
- `TODO`：`Any` 标识符冲突的兼容性证据（在真实代码库上跑一次 `Dim x As Any` 遮蔽检测）。
- `Follow-up`：跟踪主线 #136/#43/#137 状态；若出现决议，重审本建议的命名与形态。

### 状态

- **LDM 状态：LDM Considering**（核心概念）；`(As Any)` 单表达式形态 = **LDM Considering（Table）**。
- **三态判定：Consider** — 概念真实（尤其对 VBScript.NET）、动机有主线同源佐证，但血缘命名、运行时落点、文法、兼容性四项均为空白；`(As Any)` 形态 Table，绑定到"后置转换文法定稿 + 写回场景数据"两条信号。**不是背书，是带着返工清单的再审议。**

---

## 附录：特性评价

# 建议评价报告：proposal-any-pseudotype.md

## 评价对象

- 建议：proposal-any-pseudotype.md — `Any` 伪类型（动态分发/晚期绑定）
- 来源：Anthony 原文动态编程部分（`..\proposals\README.md` 第 10 组"动态编程增强"第 48 项；同组还有 typeless-declarations、default-methods）；`(As Any)` 与 proposal-postfix-casting 互引
- 配方目标：用 `Any` 伪类型使动态分发显式化，替代 `Object` 的编译期检查；`(As Any)` 提供单表达式晚期绑定

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。声明形式（`Let obj As Any = New ExpandoObject` 后的自由赋值）Motivation 清晰、可操作；但 `(As Any)` 关键子效果**示例不可编译**（`?` 即时窗口提示符）；无原型；运行时落点未定使"动态分发"无从验证；Unresolved ≥4 → 效果证据封顶 | 已检查（书面，无原型） | 核心语法未定型；写回场景无演示；无任何运行证据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。动态伪类型直接继承 C# `dynamic`/主线 #136（未声明）；VB 化改造=换成 VB6 名称 `Any`，但**血缘误标**（VB6 动态类型是 `Variant`，`Any` 是 Declare 占位符）；捆绑 `(As Any)` 子特性；原则 #3（第二种做事方式）违背 | 已检查 | 血缘标签错误；未声明 C# dynamic/主线 #136 来源；两个特性捆绑 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、4 个 Unresolved 具体诚实（如实列出是加分）；但 Detailed design 仅两组示例，无文法、无语义（`(As Any)` 语义未定）、无运行时设计；无兼容性/breaking-change 章节；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；`?` 示例不可编译 | 已检查 | 无 BNF/spec 改动描述；无兼容性章节；示例缺陷；状态行占位 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光/水受益（VBScript 迁移盘活、与 C# 差异化）；风受损（第二/三套动态写法、与 postfix-casting 文法冲突、与 `!`/CallByName 重叠未权衡）；暗风险（`Any` 标识符遮蔽破坏面、动态静态化泄漏）文档未识别 | 已检查（预测待定） | 与 #137"价值蒸发"教训未对齐；兼容性风险未权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。最大偏差：继承 VB6 的标注与实际不符（`Any` vs `Variant`）；未声明 C# `dynamic`/主线 #136 的借鉴；postfix-casting 关系已在 Drawbacks/Unresolved 提及（这是加分）；无闭源杂质 | 已检查 | 血缘标注错误；C#/主线来源未标；与 typeless-declarations 的默认类型共用未指明影响 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离**——#9（消除样板）与 #5（读起来像英语）命中；#3（不引入第二种做事方式）硬违背；#2（保持 VB-like）形式像而血缘假；#7（避免隐蔽语义变化）与 #8（不与既有语法冲突）双双风险。
- **与主线关系：Anthony 独立延伸，与主线 #135/#136/#137/#43/#106 重叠且主线无决议**——2017-08-23 对 `Dynamic` 伪类型只记了两句问号、无 RESOLUTION；#137 已指出 `!` 吃掉大部分价值；仓库内无 #136/#43 提案正文可核实。
- **破坏性变更：潜在有**——`Any` 非保留字，引入内建类型会遮蔽用户标识符（`Dim Any As Integer` 今天合法）；`(As Any)` 表达式为纯增量。建议未做兼容性分析。

## 总评

- **达成程度：部分达成**——概念（逐变量显式晚期绑定）成立且有 VBScript.NET 强相关，但设计文档在命名血缘、运行时落点、文法、兼容性四项均未完成。
- **LDM 三态建议：Consider（核心概念）**；`(As Any)` 单表达式形态 **Table**（绑定到后置转换文法定稿 + 写回场景数据）。对 VBScript.NET：价值高，但必须先返工。
- **主要问题**：① 血缘正名（`Any` vs `Variant` vs `Dynamic`）；② 运行时落点未定（DLR vs 反射）；③ `(As Any)` 与前次会议决议冲突且示例不可编译；④ 无兼容性分析（标识符遮蔽）；⑤ 与 `CallByName` 的表达力缺口（动态成员名）未承认；⑥ 与 typeless-declarations/annotated-types 的擦除表示未对齐。

## 返工建议

- **补充章节**：文法（BNF，`(As Any)` 并入后置转换共用文法；数组/泛型/ByRef/`Await` 形态）、运行时设计（DLR vs 反射决策、call-site/`LateBinding` 选择、`ExpandoObject` 支持矩阵、表达式树）、兼容性分析（`Any` 标识符冲突、`langversion` 门控与警告）、`Object ↔ Any` 转换与动态静态化规则、Option Strict/Explicit 规范同步。
- **补充证据**：可编译示例（删除 `?`；给出 `obj.Id = 1` 写回演示）；VBScript 迁移代码动态访问占比数据；`Any` 遮蔽检测的真实代码库扫描；DLR vs 反射的实测对照。
- **未决问题处理**：血缘正名定案（`Variant`/`Dynamic` 二选一或三者对照表）；`TypeOf` 流分析对 `Any` 的排除规则；`Any` 与 `Declare` 占位符语义的冲突与保留；`Async Function` 返回 `Any` 的 `Await` 结果类型。
- **设计探索**：`Any` = `Object` + `DynamicAttribute` 注解的擦除表示（与 annotated-types 统一）；变量级晚期绑定（#43 形态）作为 `Any` 类型之外的替代语法；动态成员名（`CallByName` 场景）的补齐方案；渐进类型化路径（原型用 `Any`，逐步替换为强类型，含迁移工具与警告策略）。

---

## 附录：C# 生态与互操作考量

> 本附录补充 meeting 正文未展开的 C#/CLR/.NET 生态视角。素材基于 `..\..\csharplang-index.md`（浓缩索引 T4/T5/T7/T8、M2/M8），并对其中关键原文在 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像）逐一 Grep 核实。本提案（`Any` 伪类型 / 动态分发 / 晚期绑定）与 C# `dynamic`（C# 4）、COM/IDispatch 晚期绑定、unsafe-evolution 对 `dynamic` 的质疑直接相关。

### 一、相关 C# 现实方向

本提案直接命中的 C# 现实方向有四条：

**1. C# `dynamic`（C# 4）与晚期绑定的相对边缘化（索引 T7）。**
- `dynamic` 是 C# 4（VS 2010）引入的 Dynamic binding，与 NoPIA（嵌入式互操作类型）同期——`Language-Version-History.md` C# 4 条目逐字列出 "Dynamic binding" 与 "Embedded interop types ("NoPIA")"。
- 此后 `dynamic` 长期无大演进。C# 14 对表达式树的唯一放宽是可选/具名参数：`proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`（Summary 逐字）"Support optional and named arguments in method calls in `Expression` trees"。
- 表达式树与 Span 的互斥被明确记录：`proposals\csharp-14.0\first-class-span-types.md`（Expression trees，逐字）"Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine"。ref struct 无法进表达式树，意味着「解释/动态」通道与 C# 低层类型主线互斥。
- 最尖锐的是 unsafe-evolution 把 `dynamic` 列入「是否应标 unsafe」的开放问题：`proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"，逐字）"`dynamic` (probably should match what BCL decides for reflection APIs)"——即 `dynamic` 因依赖反射系 API，在 AOT/trimming 语境下被质疑安全性。**该问题尚未裁决。**

**2. COM / IDispatch 晚期绑定的语言层投入趋零，转向 source-gen（索引 T4）。**
- 仓库内 COM 讨论浅而少：LDM-2014-09-03 仅把 COM interop 当作 out/ref 哑元参数场景（逐字）"passing dummies to ref or out parameters that you don't need (common in COM interop scenarios)"；LDM-2026-04-20 只在 sealed→接口转换处承认 COM 是「静态推理不完全可靠」的例外（逐字）"that reasoning is not perfectly sound in the presence of COM. We are already comfortable with that existing tradeoff"。
- 现代互操作重心是 AOT 友好的 source generator：P/Invoke 的 `[LibraryImport]`、COM source generators（后者属 dotnet/runtime 生态，不在本库）。语言侧支持点是函数指针调用约定、`UnmanagedCallersOnly`、ref struct/inline array 布局。

**3. AOT / trimming 对「反射型动态」的生态压力（索引 T5）。**
- LDM-2023-07-24（interceptors，逐字）："This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible."
- 方向本质：**类型系统承担更多本来靠反射/动态完成的职责**（unions、closed hierarchies、source-gen、interceptors），以服务 NativeAOT 与 trimming。

**4. unsafe-evolution 重塑「默认安全模型」，且对 VB 明确表态（索引 T8）。**
- C# 15 候选把 `unsafe` 从「出现指针」改为「解引用非托管内存」，指针将更多在非 unsafe 上下文出现，成员级标记 *requires-unsafe*。
- 对 VB 的表态（`proposals\unsafe-evolution.md`「VB」小节，逐字）："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."

### 二、现实 vs 提案

| 维度 | C# 现实方向 | 与本提案关系 | 判定 |
|---|---|---|---|
| `dynamic` 边缘化 | 不演进、表达式树受限、unsafe-evolution 质疑安全性 | `Any` ≈ 用 VB 语法重提 C# 4 `dynamic`（`Object` + `DynamicAttribute` 擦除、DLR/反射 call-site）——若只是复刻，会同样边缘化 | **冲突 + 需桥接** |
| COM/IDispatch 晚期绑定 | 语言层投入少，但 NoPIA + `dynamic` 仍是 COM 互操作出口 | VB 传统强项（Office 自动化、VBScript 迁移）。C# 未消灭晚期绑定，只是不演进 | **兼容** |
| AOT / trimming | 类型系统取代反射/动态职责；interceptors 动机即「反射难 AOT」 | 晚期绑定 = 反射 = NativeAOT 最大障碍 | **冲突最大** |
| unsafe-evolution 默认安全 | C# 默认静态、显式 unsafe；VB 明确不参与 requires-unsafe | `Any` 是「显式标注的动态」，Option Strict On 下仍动态——方向不同但可并行；VB 编译器需认识 requires-unsafe 元数据才能校验跨语言调用 | **需桥接** |
| unions / closed hierarchies（C# 15） | 类型系统表达封闭集合，服务模式匹配与 AOT | 与 `TypeOf` 流分析（v1 倾向排除 `Any`）同向：C# 让穷尽性静态化，`Any` 让成员访问动态化——两股拉力并存 | **兼容**（各管各的管道） |

**脱节提醒**：C# 对「晚期绑定」不消灭、也不前进；本提案对 VBScript.NET 的价值主张（整片 VBScript 迁移库）恰好落在 C# 主动放弃的地带（`dynamic` 边缘化 + COM 语言层投入少）。这是 VB 的**特色空白**，也是 **AOT 压力最大**之处——没有免费午餐。

### 三、对 VBScript.NET 的适应建议

1. **双模路线：默认安全、按需动态。** 与决策文件 M2/M8 一致：`.vbx` 脚本层允许动态（迁移友好），编译产物走类型化。`(As Type)` postfix-casting 是「动态 → 强类型」的单表达式出口；RESOLUTION #4 已把 `(As Any)` 绑定到共用后缀转换文法——动态只出现在显式标注处，精神上与 C#「unsafe 必须显式」对称（机制不同，语义同构）。
2. **运行时落点必须纳入 AOT 判据。** RESOLUTION #3 要求「DLR 与反射先定案」。C# 生态视角补充一条判据：若 VBScript.NET 愿景包含 NativeAOT，DLR call-site / `DynamicExpression` / `CallSiteBinder` 与反射系 `LateBinding` 族**都是** AOT 障碍；除 `ExpandoObject` 支持矩阵外，需再补「每条运行时路线在 NativeAOT/trimming 下的剩余能力」表。若目标只是完整 .NET + Office/COM，DLR 仍是首选（`ExpandoObject` 场景完整），但应默认「编译到受管程序集 + source-gen 桥」，interpreted 模式显式 opt-in（决策文件 M5）。
3. **识别新元数据是互操作前提。** unsafe-evolution 后 C# 成员会标 `RequiresUnsafeAttribute`、模块级 `MemorySafetyRulesAttribute`；C# 15 unions / `allows ref struct` 依赖 `CompilerFeatureRequired` 等特性标志；C# `dynamic` 在元数据里是 `DynamicAttribute`。VB 编译器（及 .vbx）必须认识这些才能正确校验跨语言调用（决策文件 M8 明示为**必须桥接**点）。
4. **命名对齐生态词汇。** RESOLUTION #2 已把名字判给 `Variant`/`Dynamic`。`Dynamic` 与 C# `dynamic` 的 `DynamicAttribute` 擦除、DLR call-site 语义直接对应，对跨语言读者最诚实；`Variant` 强调 VBScript 血统。无论选谁，`Any` 都应放弃——它在 C#/COM 元数据里无对应物，只会造成与 `Declare As Any` 的混乱。
5. **差异化论证对着 C# 讲。** C# `dynamic` 边缘化的教训是「光有动态语法不够，价值要证明」。VBScript.NET 的三张差异化筹码应写进设计文档：① 迁移代码量（`Variant` 原住民）；② 写回动态成员（`!` 做不到）；③ COM/Office 晚期绑定场景。

### 四、对既有 RESOLUTION / 三态判定的影响

- **无推翻，有强化。** C# 生态视角支持 RESOLUTION #2（血缘正名）——`Dynamic` 还附带与 C# `dynamic` 元数据/DLR 语义的对应，命名论证多一条实证；支持 RESOLUTION #3（运行时落点先于一切）——AOT/trimming 压力使运行时落点从「实现细节」升级为「战略决策」。
- **三态判定（Consider / Table）不变**，但补充一个 C# 侧前置条件：运行时落点定案时须一并回答「NativeAOT 愿景下 `Any`/晚期绑定如何处理」——否则 Consider 的核心概念在 AOT 场景是空的。
- **OPEN QUESTIONS 增补**：`TypeOf` 流分析排除 `Any`（meeting 倾向 v1 排除）与 C# unions/closed hierarchies 的「静态穷尽」方向同向，建议作为互操作口径固化，避免动态/静态边界在跨语言调用处漂移。

### 五、引用纪律

本附录引用的 C# 原文均经 `..\..\csharplang` Grep 逐字核实：

| 原文（逐字） | 来源 |
|---|---|
| "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." | `proposals\unsafe-evolution.md`（「VB」小节） |
| "`dynamic` (probably should match what BCL decides for reflection APIs)" | `proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"） |
| "Support optional and named arguments in method calls in `Expression` trees" | `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`（Summary） |
| "Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine" | `proposals\csharp-14.0\first-class-span-types.md`（Expression trees） |
| "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." | `meetings\2023\LDM-2023-07-24.md` |
| "Dynamic binding" / "Embedded interop types ("NoPIA")" | `Language-Version-History.md`（C# 4 条目） |
| "passing dummies to ref or out parameters that you don't need (common in COM interop scenarios)" | `meetings\2014\LDM-2014-09-03.md` |
| "that reasoning is not perfectly sound in the presence of COM. We are already comfortable with that existing tradeoff" | `meetings\2026\LDM-2026-04-20.md` |

### OPEN QUESTIONS（本附录未核实 / 需外部确认）

- **`dynamic` 是否标 unsafe：unsafe-evolution 开放问题未裁决**（"probably should match what BCL decides"），其走向直接影响 VB `Any` 的 AOT 叙事——`Suspect` 任何把「C# 已判 dynamic 死刑」当既定事实的说法。
- COM source generator 的具体语言协作（属 dotnet/runtime 生态，本库无正文）。
- NativeAOT 下 DLR / 表达式树的精确语言层约束（本库无正文）。
- interceptors 最终状态（experimental，可能 pull）。
