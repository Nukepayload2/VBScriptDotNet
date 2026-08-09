# Visual Basic Language Design Meeting

August 8, 2026

ModVB 沙盒系列会议。本周话题是 `Array(Of T)` 数组伪类型——Anthony 原文第 14 章 "Type-System Enhancements" 里的三行示例。它把两件事捆在了一起：给数组一种"泛型式"类型拼写，以及引入"按大小"而非"按上界"的数组实例化。我们把这两件事拆开审了——其中一件我们觉得**没什么可做的**，另一件则藏着一个**差一的语义陷阱**。拆完之后，建议整体作为一份提案不成立，但拆出来的半件值得带回 Consider。

## Agenda

* [Proposal: 数组伪类型 `Array(Of T)`](#proposal-数组伪类型-arrayof-t)

## Proposal: 数组伪类型 `Array(Of T)`

_Related: [vblang #135/#136 – Late-binding without `Option Strict Off` / `Dynamic` pseudo-type](https://github.com/dotnet/vblang/issues/135)（伪类型家族前例，2017-08-23）；[vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)（2017-10-18，集合字面量嵌套歧义）；ModVB：`Any` 伪类型、`Let` 声明、交/并类型、typeless-declarations、annotated-types_

### 场景与缺口

We started by separating the two halves the proposal never separates.

**第一半：类型拼写。** 建议声称"现有 VB 用 `T()`（或 `T(n)`）书写数组类型，语法在嵌套数组、泛型类型参数位置等处不够统一、不易组合"。我们当场验证了这条动机，结论是**它大体不成立**。今天 `List(Of Byte())` 就是合法类型，`Integer()()` 就是交错数组，泛型参数位置早就吃数组类型；`(Integer, Integer)` 元组类型更已确立"在 `As` 子句写新类型语法"的前例（2016-05-06 会议："Can I use this syntax in any 'As' clause where a type could appear today?"——"Yes."）。真正不好读的是**深层嵌套**：`Byte()()()` 与 `Array(Of Array(Of Array(Of Byte)))` 相比，泛型式确实顺眼——但这是可读性/IDE 渲染问题，不是组合能力缺口。

```vb
' 今天已经能组合：
Dim items As List(Of Byte()) = New List(Of Byte())()
Dim jagged As Integer()() = {New Integer() {1, 2}, New Integer() {3}}
Dim tupleish As (Integer, Integer) = (0, 0)   ' 2016-05-06 已定：As 子句可写新类型语法
```

**第二半：按大小实例化。** 这是真缺口。`New Byte(0 To 1023) {}` 强迫作者心算"上界 = 长度 − 1"，`New Byte(1023)` 又是上界语义（1024 个元素）。主线 2018-02-28 讨论 C# Range 时亲口承认过这个痛："We believe that there is non-trivial amounts of VB code that are imprecise about the length of arrays - Dimming with the length instead of 1 minus the length."。We think 这个场景对 VBScript.NET 更重：迁移代码里 `ReDim` 声明与 buffer 分配是家常便饭，而"我想要 1024 字节"不该要求作者写出 1023。

但我们立刻注意到两个必须讲清的"基因"问题：

1. **"按大小"不是 VB6/VBScript 血统。** VBScript 的 `Dim arr(1023)` 同样是**上界**语义（1024 个元素）。所以 `New Array(Of T)(size)` 的"size = 元素个数"既不是 VB6 继承，也不是 VBScript 继承——它是 Anthony 的**原创**。把它当成"直观的继承"来接受，是基因错认。
2. **"伪类型"三个字在这个建议上不成立。** 上个月 `Any` 伪类型会议定下的家族基调是：伪类型 = 编译器合成的、无直接 CLR 身份的类型，擦除为 `Object` + 注解。`Array(Of T)` 不是——它一一映射到真实 CLR 数组类型 `T[]`，不需要任何擦除或注解。它是"类型语法糖"，不是"伪类型"。这个归属错位如果不处理，会把 `Any`/annotated-types/`Array(Of T)` 三个会议带向四分五裂的机制。

### 候选方案

**PROPOSAL A — 完整伪类型（原文原样）。** `Array(Of T)` 作数组类型拼写，与 `T()` 并存；`New Array(Of T)(1024)` 按大小实例化；`Let vector As Array(Of Byte) = {}`、`Let jagged As Array(Of Array(Of Integer)) = {({})}`。

**PROPOSAL B — 纯语法糖。** `Array(Of T)` 完全等价于 `T()`，语义上不独立；编译器只做拼写归一化。按大小实例化单独决定，`New Array(Of T)(size)` 仍需定义。

**PROPOSAL C — 只要按大小实例化，不碰类型拼写。** 保留 `T()`；按大小实例化用一种**在表面形状上与按上界截然区分**的语法（不与 `New T(n)` 撞形），或先评估运行时 API + 分析器路径。

**PROPOSAL D — 什么都不做。** `T()` + `New Byte(0 To 1023)` + `ReDim` + `Array.CreateInstance`。主线 2018-02-28 的立场是 "If the API support is good enough, don't do the language work in VB" 与 "Wait to see what C# does and whether we need syntax."。

**PROPOSAL E — 目标类型化 `{}` + 分析器。** 强化目标类型集合字面量（`Dim b As Byte() = {}` 今天已可用），外加一个 off-by-one 分析器；零新语法。

### 权衡：Q&A

- **A vs B：两套拼写同一类型，站得住吗？** 站不住。`T()` 与 `Array(Of T)` 若并存，编译器必须在语义模型、重载决议、元数据写入、IDE 各处做两拼写归一化——纯成本，无类型系统收益。若说 `Array(Of T)` 是"独立类型形态"，那语言里就有了两个数组类型——胡说。**A 只有在被解释成 B（纯糖）时才自洽**，但 B 又不解释按大小实例化。建议自己 Unresolved #1 问了"等价语法糖还是独立形态"，却没有给出哪怕一个倾向。
- **命名：`Array` 是双面人。** `System.Array` 是非泛型真实类型，`Dim x As Array` 今天合法。引入 `Array(Of T)` 后，`Array` 单独出现时仍是 `System.Array`，`Array(Of T)` 却是糖——同一个词根两种身份。而 `New Array(Of Byte)(1024)` 看起来像"构造一个泛型类实例"，其实产生的是 CLR 数组——形状在说谎。
- **`New Byte(1024)` vs `New Array(Of Byte)(1024)`：差一的陷阱。** 这是全场最尖锐的一击。前者按上界 = **1025** 个元素；后者按大小 = **1024** 个元素。同一副括号，差一个元素，表面形状几乎相同：

  ```vb
  Dim a As Byte() = New Byte(1024) {}          ' a.Length = 1025
  Let b = New Array(Of Byte)(1024)             ' b.Length = 1024
  ```

  这正是设计原则所警惕的"隐蔽的语义变化"（`Return?` 被拒的同类理由）：**细微字符差异改变语义**。作者在同一个文件里混用两种写法，就是一个静默的 off-by-one 炸弹。We think 这不是风格问题，是 A 形态的**否决项**。
- **by-size 的 VBScript 共振是幻觉吗？** 名字上共振，语义上不共振。VBScript 的 `Array(1,2,3)` 是**按元素列表**构造数组的内建函数，不是"按大小"；VBScript 的 `Dim arr(1023)` 又是**上界**语义。所以 `New Array(Of Byte)(1024)` 借用了一个 VBScript 函数名，表达的却是 VBScript 里不存在的"按大小"概念——继承标注与实际语义**双错位**。
- **C vs D：语言语法还是运行时 API？** 2018-02-28 给出的决策模板是 "If the API support is good enough, don't do the language work in VB."。`Array.CreateInstance(GetType(Byte), 1024)` 能用但啰嗦；`Enumerable.Range(0, 1024).ToArray()` 有分配浪费；`Array.Empty(Of T)` 只覆盖空数组。一个 `VBRuntime` 帮助函数 + off-by-one 分析器能覆盖大部分场景。by-size 值不值得**语法**，取决于它够不够普遍——我们没有数据，`Probably` 它够普遍（buffer/队列/池分配在业务代码里高频），但必须先量。
- **B vs E：类型拼写本身值得吗？** `Dim vector As Byte() = {}` 今天就能写。`Array(Of T)` 唯一的新增是深层嵌套可读性——那是 IDE 渲染和格式化器的问题，不是语言功能。**为可读性造一个和 `T()` 并存的第二拼写，代价（原则 #3 违背 + 归一化成本）远大于收益。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Array(Of T)` 词法上无歧义（`Of` 是关键字），但**类型文法内部**的括号纠缠没有写进 spec：`Array(Of Byte)(1024)` 的 `(1024)` 是构造参数还是类型后缀？多维若按 Unresolved #2 的 `Array(Of Byte, Byte)` 走，与泛型多类型参数的惯用形态撞车（泛型 `Of T1, T2` 是**类型参数**，`Array(Of Byte, Byte)` 若是**维数**，同一个形状两套语义）。`Array(Of Byte)(,)` 作秩后缀又与构造调用不可区分。交错 `Array(Of Array(Of Integer))` 的嵌套括号层次也没有文法。`{({})}` 则直接踩中主线 2017-10-18 #101 遗留问题——"The problem is the nested case. How do we know the List type? Do you restate the type or have a separate list syntax."——建议没有给出任何解析规则。

#### 2. 角案例与边界语义

- **空 `{}` 目标类型化**：`Let vector As Array(Of Byte) = {}` 依赖"集合字面量先无类型、目标定型后重分类"的既有模型（2016-05-06："Initially type-less, but reclassified as values after target-typing/type-inference. Analogous to array literals."）。可做，但 `Dim x = {}` 无目标类型时在 Option Strict Off 下是 `Object()`、Strict On 下报错（2015-01-14 记录过无主导类型的行为分叉）——两条路径必须一致。
- **`{({})}` 交错嵌套**：`{({})}` 表示"单元素 = 一个空 `Integer()` 数组"。`Probably` 今天在 `Dim x As Integer()() = {({})}` 里就能编译，但嵌套集合字面量的匹配规则（内层何时算元素、何时算分组）恰是 2017-10-18 未决的问题；建议把它当既定事实使用，没有文法。
- **`New Array(Of Byte)(0)` 与负值**：0 合法（空数组）；负值运行时抛 `OverflowException`——按上界的 `New Byte(-1)` 同样抛。行为对得上，但要写进 spec。
- **泛型类型参数**：`Function F(Of T)() As Array(Of T)`——CLR 允许 `T[]`，糖化后无碍。数组协变（`Array(Of String)` → `Array(Of Object)`）是 CLR 属性，糖化后天然保留。
- **ByRef / 传参**：数组本就按引用传递、`ReDim` 可重定大小。糖化后 `ReDim a(1024)` 仍是**上界**——两种实例化语义的分裂会**蔓延进 `ReDim`**，作者更困惑。

#### 3. 作用域与绑定

语义模型里 `Array(Of Byte)` 绑定到什么符号？必须是"数组类型节点"，与 `Byte()` 归一化到同一个 `GetTypeInfo` 结果（`Byte[]`），不能是 `System.Array` 也不是任何泛型实例。若按 B 做纯糖，编译器得在**所有**消费处做拼写归一化（semantic model、重载决议、元数据、IDE 签名渲染）；若按 A 做独立形态，则必须解释"两个数组类型怎么转换"。建议两种都没答。

#### 4. 与既有特性的交互

- **`{}` 集合初始化器**：`Let vector As Array(Of Byte) = {}` 的目标定型与 2014-02-17 "Look at how array literals work..." 的主导类型算法是同一套；`{1, 2, Null}` → `Integer?` 这类既有规则不受影响。
- **`ReDim Preserve`**：`ReDim Preserve a(1023)` 今天按上界。by-size 实例化若落地，`ReDim` 与 `New` 的语义会不一致——要么 `ReDim` 也学 by-size，要么把"两种实例化"的坑埋进 `ReDim`。我们倾向**都不做**，除非先解决差一问题。
- **`ParamArray`**：2014-02-17 已批准 `ParamArray x As IEnumerable(Of Integer)`；`ParamArray x As Array(Of Integer)`（糖）理论上等价于 `ParamArray x As Integer()`——但 `ParamArray` 的数组身份是**特殊的**（编译器展开调用点），糖化后必须证明两种写法在调用点展开上逐字一致。
- **Option Strict Off 晚期绑定**：宽松模式下数组可以是 `Object`。`Array(Of T)` 是编译期类型糖，不引入动态语义——与 `Any` 不同。这是好事，但也再次证明它**不是伪类型家族成员**。
- **元组前例**：2016-05-06 为元组开了"As 子句新类型语法"的口子，但元组是**新的类型身份**；`Array(Of T)` 没有新身份。前例不适用。

#### 5. Breaking change 与兼容性

`Array(Of T)` 今天对绝大多数代码是纯增量——`System.Array` 非泛型，`Array(Of ...)` 现在绑定不到任何东西。但**有真实的破坏面**：

```vb
Class Array(Of T)            ' 用户自定义泛型 Array 今天合法
End Class
' 引入伪类型后：Dim x As Array(Of Integer) 从"绑定用户类型"变成"绑定伪类型"——语义翻转。
```

同样，`New Array(Of Byte)(1024)` 若用户正好有 `Class Array(Of Byte)`，今天构造用户类型，明天构造数组。罕见但真实。**必须配 `langversion` 门控与警告策略**，建议完全没有兼容性章节——弱提案红旗清单命中。

#### 6. Option Strict / 编译选项分叉

`Let vector As Array(Of Byte) = {}` 的目标定型在 Strict On/Off 下行为必须一致（空集合字面量无目标类型时：On 报错、Off 得 `Object()`，2015-01-14 有记录）。by-size 实例化是编译期解析，不引入晚期绑定，两路径天然一致。唯一要防的是：`Option Infer Off` 时 `Let buffer = New Array(Of Byte)(1024)` 推断成什么——必须显式规则。

#### 7. IDE / IntelliSense

两种拼写并存时，InfoTip 显示 `Array(Of Byte)` 还是 `Byte()`？签名帮助、快速信息、`{({})}` 的格式化与补全，都需要指定一个规范拼写。不做进规范等于没设计——建议只字未提。

#### 8. 数据 / 普遍性

by-size 场景（buffer/队列/池分配）`Probably` 高频，但没有量化数据。类型拼写场景的"组合性缺口"**已被我们证伪**（`List(Of Byte())` 合法），剩余价值是深层嵌套可读性。对 VBScript.NET：**数组是核心资产**，但 VBScript 自己的惯用法（`Dim arr(n)`、`ReDim Preserve`）都是上界语义——迁移代码不需要 by-size，需要的是**不踩差一坑**。

#### 9. 更简替代

- `T()` + `New Byte(0 To 1023)`：现状，`Probably` 已覆盖 90%。
- `ReDim` / `ReDim Preserve`：VBScript 迁移的原生路径。
- `Array.CreateInstance` / `Array.Empty(Of T)` / 运行时帮助函数：按大小、按类型创建，2018-02-28 的 "If the API support is good enough, don't do the language work in VB" 模板适用。
- off-by-one 分析器：诊断"作者把长度当上界"的真实错误，零语法。
- **`Dim buffer(1023) As Byte` 改读法**：不改语言，只改教育/文档。`Probably` 对多数业务开发者够用。
- 目标类型化 `{}`：`Dim b As Byte() = {}` 今天可用。

**更简替代 = 运行时 API + 分析器**，语言语法是"最后一公里"。

#### 10. 复杂度 / 成本 / 优先级

类型拼写半件：解析、绑定、归一化都便宜，但**归一化面广**（semantic model/重载/元数据/IDE 都要碰），性价比低。by-size 半件：编译器改动极小（`newarr` + init），真正成本在**语法形状选择**与兼容性分析。对 VBScript.NET：优先级**排在流分析、模式匹配、`Any` 之后**——数组今天能用，这是 DX 打磨不是能力缺口。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍。`Array(Of T)` 糖化为既有 `T[]`；by-size 实例化发 `newarr`；多维走 `newobj instance void T[0...,0...]::.ctor(int32,...)`。**不需要**任何擦除/注解属性——再次证明它不是 `Any` 式伪类型。真正的硬约束只有"多维按大小"的 `newobj` 参数表与 `ReDim` 的一致性。

#### 12. 值不值得做

- **价值**：by-size——高（消除差一心算、贴近"我要 N 个元素"的直觉），但它是 **Anthony 原创**而非 VBScript 继承；类型拼写——低（`T()` 已组合，剩余可读性收益归 IDE）。
- **成本**：类型拼写中（归一化面广）；by-size 低（编译器改动小，但语法决策成本高）。
- **风险**：差一陷阱（原则 #7 直接命中）、`Array` 双面身份、用户泛型 `Array` 遮蔽、家族归属错位。
- **综合**：**拆件后一半值得 Consider，一半 Table**。整体作为一份提案，Reject。

### VB 基因对照

- **消除常见样板（原则 #9）**：by-size 命中——`New Byte(0 To 1023) {}` 的 `0 To` 仪式被消掉。
- **不引入"第二种做事方式"（原则 #3）**：类型拼写硬违背——数组已有 `T()`，`Array(Of T)` 是第二套。这是全场最重的原则罚项。
- **避免隐蔽语义变化（原则 #7）**：`New Byte(1024)` 与 `New Array(Of Byte)(1024)` 同形差一——直接命中，**否决 A 的实例化形态**。
- **读起来像英语、对新手友好（原则 #5）**：`New Array(Of Byte)(1024)` 读作"1024 字节的新数组"，确实比 `New Byte(0 To 1023) {}` 顺；这是 by-size 真优点。
- **保持 VB-like（原则 #2）**：`T()` 是 VB6/VBA 的正统数组拼写；`Array(Of T)` 是泛型式——半外来，且 `Array` 名借用 VBScript **函数**名（按元素列表，非按大小）造成血缘错认。
- **不与既有语法冲突（原则 #8）**：`Array` 标识符双面身份 + `Of` 泛型语法被"类型语法糖"借用，双重风险。
- **与主线关系（对照表 2.3）**：2.3 表中无数组伪类型——属 **Anthony 独立延伸**。主线没有数组伪类型工作；最近的数组相邻讨论是 2018-02-28（Range 对 VB 的启示：数组长度不精确、等 C#、API 优先）与 2016-05-06（元组类型语法前例）、2017-10-18（集合字面量嵌套歧义）。与 `Any` 伪类型、annotated-types（擦除表示）必须对表——本建议**不擦除**，归属错位。

### RESOLUTION:

1. **拆件。** `Array(Of T)` 的类型拼写与 `New Array(Of T)(size)` 的按大小实例化是两件事，捆绑本身是设计缺陷。任何后续工作都不得再混做。
2. **类型拼写 `Array(Of T)`：Table（倾向 Reject）。** 组合性动机不成立——`List(Of Byte())` 今天合法；两套拼写违背原则 #3；`Array` 与 `System.Array` 双面冲突；且它不是"伪类型"家族意义上的伪类型。剩余可读性收益归 IDE 渲染与格式化器，不归语言。
3. **按大小实例化：值得做，但形状必须改。** `New Array(Of T)(size)` 与 `New T(size)` 差一，是原则 #7 否决的隐蔽语义变化。若做，必须选一种**在表面形状上与按上界截然区分**的形式，或先走运行时 API + off-by-one 分析器路径（2018-02-28 模板："If the API support is good enough, don't do the language work in VB."）。启动一个小型数据采样：真实代码里"长度 vs 上界"混写/差一的占比。
4. **伪类型家族章程对表。** 回复 `Any` 伪类型会议的 Implication 对齐请求：`Array(Of T)` **不擦除为 `Object` + 注解**，映射到真实 CLR 类型——它不属于"伪类型"族，应单列"类型语法糖"类别或干脆不建类别。家族章程必须写明什么算伪类型，避免 `Any`/annotated-types/`Array(Of T)` 三套机制各说各话。
5. **`{({})}` 交错嵌套不作规范依据。** 在集合字面量嵌套规则（2017-10-18 #101 遗留问题）定稿前，建议的交错示例只是设想，不是语法。
6. **兼容性分析是进入 Consider 的前置条件。** 逐条列证用户泛型 `Array` 遮蔽、`langversion` 门控、警告策略、`Option Infer Off` 推断规则、`ReDim` 一致性。
7. **状态：核心概念 Consider，`Array(Of T)` 形态 Table，整体 Reject。** 见下。

### Implication:

- 将本建议标注为 **LDM Rejected（整体）/ 按大小实例化 = LDM Considering**；在建议头部注明拆件结论、差一陷阱、家族归属结论。
- 回复 `Any` 伪类型会议：确认 `Array(Of T)` 不属伪类型族，提议家族章程增加"类型语法糖 vs 伪类型"的判据。
- 起草一份按大小实例化的 speclet 草稿：候选形状（区别于 `New T(n)` 的语法、或运行时 API + 分析器）、`ReDim` 一致性、`langversion` 门控、兼容性列证。
- 为 VBScript.NET 启动 off-by-one 采样：统计迁移代码中 `New T(n)`/`ReDim` 混写与长度心算错误的占比。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**主线的 `Array(Of T)`/按大小实例化状态无法核实**。仓库内无数组伪类型提案，2018-02-28 的立场是 "Wait to see what C# does and whether we need syntax." 与 "If the API support is good enough, don't do the language work in VB."，此后我们没有找到主线决议。请勿将本会议结论当作主线裁定；`Suspect`：也许后续会话讨论过，需外部核实。
- `OPEN QUESTIONS`：C# 集合表达式（`[a, b, c]`）作为类型拼写与按大小构造的竞争路径，本会议不裁定（默认跟随 C# 原则下待定）。
- `OPEN QUESTIONS`：多维 by-size（`New Array(Of Byte, Byte)(1024, 1024)`？）的语法与 `newobj` 参数表是否值得——VBScript 迁移代码里多维数组高频。
- `OPEN QUESTIONS`：`ParamArray x As Array(Of T)` 糖化后与 `ParamArray x As T()` 在调用点展开上是否逐字一致。
- `TODO`：按大小实例化的 speclet 草稿与形状候选对照。
- `TODO`：真实代码库 off-by-one / 长度-上界混用采样。
- `Follow-up`：跟踪 C# 集合表达式与 2018-02-28 之后主线的 Range/数组工作；若主线出现决议，重审本建议。

### 状态

- **LDM 状态：LDM Rejected（整体）**；按大小实例化核心 = **LDM Considering**；`Array(Of T)` 类型拼写 = **LDM No Plans**。
- **三态判定：Table** — 拆件后，by-size 是真实且有主线佐证（2018-02-28）的缺口，值得以**新形状**进入 Consider；`Array(Of T)` 类型拼写冗余、违背原则 #3、家族归属错位，整体不采纳。**不是背书，是带着拆件清单的再审议。**

---

## 附录：特性评价

# 建议评价报告：proposal-array-pseudotype.md

## 评价对象

- 建议：proposal-array-pseudotype.md — 数组伪类型 `Array(Of T)`（类型拼写 + 按大小实例化）
- 来源：Anthony 原文第 14 章 "Type-System Enhancements"（`..\AnthonyDesign_wordpress.txt` L2395–2400；与交/并类型、日期字面量同章）。原文自带问号（"Array pseudo-type?"），建议把问号抹成了既定特性
- 配方目标：用 `Array(Of T)` 统一数组类型拼写（嵌套/泛型位置易组合），`New Array(Of T)(size)` 按大小实例化

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。by-size 的改进可演示（1024 元素免心算）；但类型拼写半件的核心动机（组合性缺口）**证伪**——`List(Of Byte())` 今天合法，`T()` 已组合；`{({})}` 嵌套依赖未决文法；无原型；Unresolved ≥4 → 效果证据封顶 | 已检查（书面，无原型） | 组合性动机不实；`New Byte(1024)` vs `New Array(Of Byte)(1024)` 差一未识别；无任何运行证据 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Array(Of T)` 是泛型式（`Of` 是 VB 泛型关键字，算 VB 化）但**两个无关能力捆绑**（类型拼写 + 实例化语义）未加论证；`Array` 名借用 VBScript `Array()` 函数名但语义不同（按元素列表 vs 按大小）；by-size 是 Anthony 原创非 VB6/VBScript 继承 | 已检查 | 两特性捆绑；VBScript 血缘错认；原则 #3（第二种做事方式）违背 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、4 个 Unresolved 具体诚实（如实列出是加分）；但 Detailed design 仅三组示例，无文法、无 `New Array(Of T)(size)` 的语义定义（只有一句注释）、无多维、无兼容性章节；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；Drawbacks/Alternatives 过薄 | 已检查 | 无 BNF/spec 改动描述；无兼容性/breaking-change 章节；`{({})}` 文法悬空；状态行占位 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/水微正向（by-size 免样板、数组是 VBScript.NET 核心资产）；风受损（两套数组拼写、家族术语错位、与 `T()` 归一化成本）；暗风险（差一陷阱、用户泛型 `Array` 遮蔽）文档未识别 | 已检查（预测待定） | 差一语义陷阱是最重暗风险，未权衡；与伪类型家族章程未对齐；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源章节定位准确（第 14 章）；但未声明借鉴 C# 泛型风格的成分；未声明 `Array` 名与 VBScript `Array()` 函数的血缘错认；"按大小"原创性未标注；与 any-pseudotype/annotated-types 的家族互引缺失（上会议 Implication 明确要求的对表未做） | 已检查 | VBScript 血缘错认；C# 成分未标；家族互引缺失；无闭源杂质 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离**——#9（消除样板，by-size）与 #5（读起来像英语，`New Array(Of Byte)(1024)`）命中；#3（不引入第二种做事方式）硬违背（两套数组拼写）；#7（隐蔽语义变化）直接命中（同形差一）；#8（不与既有语法冲突）双重风险（`Array` 双面、`Of` 泛型语法借用）。
- **与主线关系：Anthony 独立延伸**——主线无数组伪类型工作；2.3 对照表无此条目。相邻主线讨论：2018-02-28 Range 对 VB 的启示（"non-trivial amounts of VB code that are imprecise about the length of arrays"、等 C#、API 优先）、2016-05-06 元组"As 子句新类型语法"前例、2017-10-18 集合字面量嵌套歧义（#101）。与 `Any` 伪类型家族章程（擦除为 `Object` + 注解）**冲突**——`Array(Of T)` 映射真实 CLR 类型，不是伪类型。
- **破坏性变更：潜在有**——用户泛型 `Class Array(Of T)` 遮蔽（`Array(Of T)` 从绑定用户类型翻转为伪类型）；`New Array(Of T)(size)` 重绑定。`Array(Of T)` 对绝大多数代码是纯增量，但建议未做兼容性分析。

## 总评

- **达成程度：部分达成**——by-size 缺口真实（主线 2018-02-28 佐证），但类型拼写半件动机不实，两件捆绑、差一陷阱与家族归属错位均未处理。
- **LDM 三态建议：Table（整体）**；按大小实例化核心 **Consider**（须换与按上界截然区分的形状，或走运行时 API + 分析器）；`Array(Of T)` 类型拼写 **Table/Reject**（与 `T()` 冗余、违背原则 #3、非伪类型）。对 VBScript.NET：by-size 值得做但优先级排在流分析、模式匹配、`Any` 之后。
- **主要问题**：① 组合性动机证伪（`List(Of Byte())` 已合法）；② 差一语义陷阱（`New Byte(1024)` vs `New Array(Of Byte)(1024)`）是 A 形态否决项；③ 两件无关特性捆绑；④ 伪类型家族归属错位；⑤ 无兼容性分析（用户泛型 `Array` 遮蔽）。

## 返工建议

- **拆件**：拆成两份独立建议——(1) 按大小实例化（新形状：与按上界表面形状截然区分，或运行时 API + off-by-one 分析器评估）；(2) 若仍要类型拼写，作为纯语法糖写清归一化规则与"类型语法糖 vs 伪类型"的家族判据。
- **补充章节**：文法（BNF：`Array(Of T)` 嵌套/秩/构造调用区分、`{({})}` 嵌套匹配规则）；多维与 `ParamArray` 交互；兼容性分析（用户泛型 `Array` 遮蔽、`langversion` 门控、警告策略）；Option Strict/Infer 分叉（空 `{}` 目标定型、`Option Infer Off` 推断）；IDE 渲染规范拼写。
- **补充证据**：可编译示例（删除 `{({})}` 悬空文法；给出 `List(Of Byte())` 已可组合的对照）；VBScript 迁移代码 buffer 分配/`ReDim` 混写与 off-by-one 占比数据；`New Byte(1024)` vs `New Array(Of Byte)(1024)` 差一的实测对照。
- **未决问题处理**：by-size 形状定案前不进入实现；`Array` 双面身份与 `System.Array` 的关系写死；与 `Any` 伪类型会议对表家族章程（什么算伪类型）；`ReDim` 一致性单独立项。
- **设计探索**：运行时 API + 分析器路径（`Array.CreateInstance`/帮助函数 + off-by-one 诊断）作为语言语法之前的低风险替代；深层嵌套可读性归 IDE 渲染/格式化器的可行性（让 `T()` 显示为 `Array(Of T)`，不动语言）。

---

## 附录：C# 生态与互操作考量

> 关系评估：本提案主题（数组类型拼写 `Array(Of T)` + 按大小实例化）与 C# 的关联**中等偏上、但集中在一处**——它不碰 C# 的 unsafe/COM/PInvoke 低层面，真正的互操作触点只有两个：(1) 数组的 CLR 类型身份 `T[]` 是跨语言共享的一等类型；(2) C# 12 集合表达式与 `new T[n]` 正在改写「数组如何被构造」的生态习惯。C# 没有 `Array<T>` 拼写、没有上界语义、没有按大小字面量——因此本提案的**两半在 C# 现实里命运不同**：类型拼写半件在 C# 侧零对应、零需求；按大小半件恰好与 C# 的 `new T[n]`（按长度）同构，是**向 C# 收敛**而非背离。以下基于 `..\..\csharplang`（dotnet/csharplang 官方镜像）核实。

### 相关 C# 现实方向

**1. C# 数组类型语法 20 年未动，没有「泛型式」数组拼写。** C# 自 1.0 起数组类型就是 `T[]` / `T[,]` / `T[][]`，`System.Array` 是非泛型基类（C# 标准 §16.2 Array types；正文已迁 `dotnet/csharpstandard`，本镜像 `spec\arrays.md` 仅为链接索引）。C# 处理「数组构造的啰嗦」不靠改类型拼写，而靠元素列表字面量——collection expressions 的 Summary 原文：「Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values.」→ `proposals\csharp-12.0\collection-expressions.md`；其 Motivation 点名数组现状：「Arrays, which require either `new Type[]` or `new[]` before the `{ ... }` values.」→ 同上。**结论：C# 生态里不存在 `Array(Of T)` 的对应物**；「给数组造第二拼写」没有任何 C# 侧需求或先例可引。

**2. 按长度（by-length）实例化是 C# 基线，C# 从未有过上界（upper-bound）语义。** `new T[n]` 产生 `Length = n` 的数组（C# 标准 §16.3 Array creation；本镜像 `spec\arrays.md` §16.3 链接指向 `dotnet/csharpstandard`，正文不在镜像内，故不逐字引用）。本提案 by-size 的直觉——`New Array(Of Byte)(1024)` 想要 1024 个元素——**逐字同构于 C# `new byte[1024]`**。反过来，VB/VBScript 的 `New Byte(1024)`/`Dim arr(1024)` 按上界 = 1025 元素，在 C# 生态里**不存在对应写法**。所以「长度 vs 上界」的心算陷阱是 VB 家族独有的历史包袱，C# 没有任何动机去解决它。这是本附录最重要的发现：**by-size 半件不仅不冲突，反而是让 VB 语义向 C# 看齐**；冲突完全发生在 VB 家族内部（`New Byte(1024)` 上界 = 1025）。

**3. Collection expressions（C# 12）：按元素列表构造，恰好与 VBScript `Array(1,2,3)` 同构。** 集合表达式是**按元素列表**构造（`[e1, ..c2, e2]`，`..` 为展开），目标定型到数组/span/集合类型。其 Conversions 规定隐式转换的目标首位即数组：「An implicit *collection expression conversion* exists from a collection expression to the following types:」其下首条为「A single dimensional *array type* `T[]`, in which case the *element type* is `T`」→ `proposals\csharp-12.0\collection-expressions.md`。这与 VBScript 内建 `Array(1,2,3)` 函数（按元素列表）**血缘一致**，而与会话主角的 by-size **无关**——C# 不需要按大小字面量，因为 `new T[n]` 已覆盖。C# LDM 对集合表达式设计哲学的定调原文：「It fits the declarative motto of saying the "what", not the "how".」→ `meetings\2023\LDM-2023-08-09.md`（Conclusion）。另注意：**C# 的自然类型问题同样悬而未决**——LDM-2023-08-09 明确称其为「the (currently postponed) question about natural types for collection expressions - what do you get when you use `var`?」→ 同上。这与本会话第 6 节记录的 `Dim x = {}` 无目标类型分叉（Strict On 报错 / Off 得 `Object()`，2015-01-14）**平行**：两边都还没给「无目标类型的集合字面量」定自然类型。

**4. `params` collections（C# 13）：`params` 从「只能是数组」扩到集合类型。** 原文：「In C# 12 language added support for creating instances of collection types beyond just arrays.」→ `proposals\csharp-13.0\params-collections.md`；Motivation：「Today `params` parameter must be an array type.」→ 同上。对本会话 `ParamArray x As Array(Of Integer)` 的糖化追问：C# 的数组型 `params` 仍是 `params T[]`，C# 13 只是把 `params` 扩到 `ReadOnlySpan<T>`/`IEnumerable<T>` 等；版本史条目原文：「`params` collections: extends `params` support to collection types (`void M(params ReadOnlySpan<int> s)`).」→ `Language-Version-History.md`。**C# 13 没有因此改变「数组型参数集合的类型身份」**——仍是 `T[]`，与本会话「糖化后必须证明两种写法在调用点展开上逐字一致」的未决问题不产生新答案。

**5. Inline arrays（C# 12）：固定大小缓冲的官方答案，是另一条路。** 原文：「Provide a general-purpose and safe mechanism for declaring inline arrays within C# classes, structs, and interfaces.」→ `proposals\csharp-12.0\inline-arrays.md`。`[InlineArray(N)]` 是**结构体内置固定缓冲**（替代 unsafe fixed buffers），与本提案的「堆上按需数组 by-size」是**不同机制**——前者声明类型布局，后者是实例化语法。但对 VBScript.NET 的 buffer 场景（会话第 8 节）两者都值得列进方案对照。

**6. First-class span types（C# 14）：数组被吸收进 Span 生态。** 原文：「We introduce first-class support for `Span<T>` and `ReadOnlySpan<T>` in the language, including new implicit conversion types and consider them in more places, allowing more natural programming with these integral types.」→ `proposals\csharp-14.0\first-class-span-types.md`；其转换定义：「An implicit span conversion permits `array_types`, `System.Span<T>`, `System.ReadOnlySpan<T>`, and `string` to be converted between each other as follows:」→ 同上。`Array(Of T)` 若糖化就是 `T[]`，**天然进入这张转换网络**——但前提是 VB 编译器认识这些转换与 `ref struct`（见下）。

### 现实 vs 提案

| 提案半件 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| 类型拼写 `Array(Of T)` | C# 数组类型 `T[]` 20 年未动；collection expressions 只解决「值构造」不碰类型拼写 | **脱节**（无对应、无 C# 需求） | C# 无 `Array<T>` 先例；两套拼写的归一化成本是 VB 独担；会议 Table/Reject 与 C# 现实一致 |
| 按大小实例化 `New Array(Of T)(size)` | C# `new T[n]` 一直是按长度（`Length = n`） | **兼容（收敛）** | by-size 直觉 = C# `new T[n]`；落地即向 C# 语义看齐。但「形状必须与按上界截然区分」的约束依旧成立——那是 VB 家族内部差一，不是 C# 造成的 |
| 集合字面量 `{}` / `{({})}` 竞争路径 | C# 12 collection expressions `[e1, ..e2]`（按元素列表） | **需桥接** | C# 用元素列表字面量解决「构造啰嗦」；VB 已有 `{}` + VBScript `Array(1,2,3)` 内建。VBScript.NET 的 `{}` 应参考 C# 12 目标定型模型；自然类型问题两边都未决（见上 #3） |
| 伪类型家族归属（不擦除） | C# 无伪类型概念；`T[]` 是 CLR 一等类型 | **兼容** | 会议「`Array(Of T)` 不是伪类型」与 C# 现实完全一致——任何糖都必须归一化到 `T[]` 才能在元数据与 C# 互通（即会议点名的「归一化成本」） |
| `ParamArray x As Array(Of T)` | C# 13 `params` collections 扩到集合，但数组型仍 `params T[]` | **需桥接（待定）** | 调用点展开逐字一致问题仍 OPEN（会议 OPEN QUESTIONS 保持）；C# 13 未提供新答案 |
| 深层嵌套可读性 | C# 也无专门语法，靠 IDE/格式化器 | **脱节**（不冲突） | 与 C# 无关，是渲染问题，会议归 IDE 正确 |

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：by-size 是编译期解析（`newarr` 直发），不引入晚期绑定，天然走「默认安全」。但差一陷阱是**静默正确性炸弹**——无论 by-size 是否落地，都应先上 off-by-one 分析器（会议 RESOLUTION #3 已列）。对 VBScript.NET 迁移代码，高频风险不是「缺 by-size」，而是**既有的「把长度当上界」混写**（`New Byte(1024)` 与 C# `new byte[1024]` 差一个元素——同一份脚本若与 C# 库互算 buffer 长度，必出静默越界）。
- **source-gen 桥**：C# 方向是编译期生成替代运行时反射（interceptors、`[LibraryImport]`；索引 T5/T6）。VBScript.NET 的数组创建应走 `newarr`/`newobj` 直发，**避免 `Array.CreateInstance` 反射路径**（AOT 不友好）；C# 12 允许编译器把 `[1, 2, 3]` 直接烤进程序集数据（`collection-expressions.md` §翻译允许 elide 指令序列），VB 的 `{}` 字面量可参照同一策略。
- **识别新元数据**（决策文件 M8 的「必须桥接」面，在本提案的具体化）：
  - `[InlineArray(N)]`（C# 12）：VB 编译器需能**消费** inline-array 结构体（固定缓冲）作为数组/span 来源，否则 C# 侧 fixed-buffer 类型在 VB 里不可用。
  - `[CollectionBuilder(...)]`（C# 12）：若 VB 集合字面量要目标定型到带 builder 的类型（如 `ImmutableArray<T>`），需识别该属性。
  - RefSafetyRules / span 转换（C# 11/14）：VB 数组 `T[]` 参与 C# 14 隐式 span 转换的前提是 VB 认识这些规则；但 **VB 当前不能声明 `ref struct`**，span-typed 返回进 VB 侧仍是缺口（与决策文件 M7 同源）。
  - 通用 unsafe 元数据：unsafe-evolution 对 VB 的表态「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（VB 小节）——与数组无直接关系，但 VBScript.NET 需认识 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute` 才能校验「调用 C# requires-unsafe 成员」的安全性（决策文件 M8）。
- **数组 → Span 互操作**：`Array(Of T)` 若糖化到 `T[]`，跨语言传参天然可用（C# 14 隐式 span 转换）。但 .vbx 侧不能声明 `ref struct`，接收 span 只能显式转换或只读视图——这是 C# 生态对 VB 数组最大的「单行道」压力，应单独立项评估。

### 对既有 RESOLUTION / 三态判定的影响

- **无实质翻转，且 C# 现实强化了三条结论**：
  1. **类型拼写 Table/Reject 不变**——C# 20 年无 `Array<T>` 方向，外部需求为零；「等 C# 再看」没有任何可等的对象。
  2. **by-size Consider 不变，且多一个收敛证据**——C# `new T[n]` 即按长度，by-size 落地等于让 VB 语义向 C# 看齐；但「形状必须与按上界截然区分」的约束依旧（家族内部差一），且新形状不得与 C# 侧 `new T[n]` 之外的东西撞形（跨语言勿再制造「同形不同义」）。
  3. **「等 C# 再说」（2018-02-28 模板）已有部分答案**——C# 12 做了 collection expressions（元素列表），`new T[n]`（按长度）一直存在；**C# 不会**解决上界语义（它从未有）。所以 VB 要么自己行动（新形状 by-size 或 API+分析器），要么继续 `New Byte(0 To 1023)`；此路已无可等。
- **伪类型家族归属结论强化**：C# 无伪类型；`[CollectionBuilder]`/inline-array 先例证明「类型糖 + 无新 CLR 身份」是 .NET 主流的受支持模式——「`Array(Of T)` 不属伪类型族、应单列类型语法糖」的 Implication 与 C# 生态的互操作现实吻合。

### 引用纪律与未核实项

本附录引用的 C# 原文均已在 `..\..\csharplang` **逐字核实**（来源路径随文标注）：

- 「Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values.」→ `proposals\csharp-12.0\collection-expressions.md`
- 「Arrays, which require either `new Type[]` or `new[]` before the `{ ... }` values.」→ `proposals\csharp-12.0\collection-expressions.md`
- 「An implicit *collection expression conversion* exists from a collection expression to the following types:」→ `proposals\csharp-12.0\collection-expressions.md`
- 「In C# 12 language added support for creating instances of collection types beyond just arrays.」→ `proposals\csharp-13.0\params-collections.md`
- 「Today `params` parameter must be an array type.」→ `proposals\csharp-13.0\params-collections.md`
- 「Provide a general-purpose and safe mechanism for declaring inline arrays within C# classes, structs, and interfaces.」→ `proposals\csharp-12.0\inline-arrays.md`
- 「We introduce first-class support for `Span<T>` and `ReadOnlySpan<T>` in the language, including new implicit conversion types and consider them in more places, allowing more natural programming with these integral types.」→ `proposals\csharp-14.0\first-class-span-types.md`
- 「An implicit span conversion permits `array_types`, `System.Span<T>`, `System.ReadOnlySpan<T>`, and `string` to be converted between each other as follows:」→ `proposals\csharp-14.0\first-class-span-types.md`
- 「It fits the declarative motto of saying the "what", not the "how".」→ `meetings\2023\LDM-2023-08-09.md`
- 「the (currently postponed) question about natural types for collection expressions - what do you get when you use `var`?」→ `meetings\2023\LDM-2023-08-09.md`
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`
- 版本史条目：C# 12「Collection expressions: provides a uniform and efficient way of creating collections using collection-like types (`List<int> list = [1, 2, 3];`)」、C# 13「`params` collections: extends `params` support to collection types (`void M(params ReadOnlySpan<int> s)`).」、C# 14「First-class `Span` types: streamlines usage of `Span`-based APIs by improving type inference and overload resolution.」→ `Language-Version-History.md`

- **OPEN QUESTIONS / Suspect**：
  - **`new T[n]` → `Length = n` 的上界差一事实**：C# 标准正文已迁 `dotnet/csharpstandard`（本镜像 `spec\arrays.md` §16.3 仅链接索引），故此处**未逐字引用**，仅作 C# 基线语义陈述；如需逐字，须到 `dotnet/csharpstandard` 核实。
  - **collection expressions 的最终自然类型规则**：LDM-2023-08-09 时仍 postponed；截至本附录撰写，未在镜像内找到后续定案——若后续定稿，应回填本附录第 3 节并与 2015-01-14 的 `Dim x = {}` 分叉对表。
  - **VB 侧 `{}` 集合字面量与 C# collection expressions 的目标定型差异**：两者文法不同（`{}` vs `[]`），C# 的 spread/`..`/builder 细节是否值得移植进 VB，本附录不裁定——保持会议「默认跟随 C#」的待定立场。
  - **`[InlineArray]` 与 `[CollectionBuilder]` 属性在 VB 侧消费的具体规则**：C# 侧规范已核，但 VB 编译器如何映射到数组/字面量属本评估项目的开放项。
