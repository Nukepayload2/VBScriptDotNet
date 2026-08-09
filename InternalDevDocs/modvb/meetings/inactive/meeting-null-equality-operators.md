# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周第五场语言设计会议——前四场分别落定了 `Null` 字面量（Table）、可空性流分析（引擎先行）、`TypeOf ... Is` 类型流分析（共享引擎）与 `Do Nothing`（待定）。本建议 `?=` / `?<>` 是 Null 安全家族（proposal 57–60）里语义最"抄 SQL"的一张牌。这场会议的独特之处在于：**我们把建议的 Motivation 前提拿去对了一遍编译器源码，发现它是对的**——VB 的可空值类型相等在今天确实走三值逻辑。于是整场讨论从"前提是否成立"转向了"前提成立之后，这个语法值不值得为它付费"。

## Agenda

* [Proposal: 二值逻辑相等运算符（`?=`、`?<>`）](#proposal-二值逻辑相等运算符)

## Proposal: 二值逻辑相等运算符

_Related: [2026-08-08 Null 字面量会议](meeting-null-literal.md)（`Null` 字面量 Table，本家族前哨）· [2026-08-08 可空性流分析会议](meeting-nullability-flow-analysis.md)（`IsNot Nothing` 守卫的共享引擎）· [vbldm-notes-2018.02.28](../../vblang/meetings/2018/vbldm-notes-2018.02.28.md)（可空值类型相等 "quirks in a good way"）· [vbldm-notes-2018.02.07](../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（NRT 推迟）· [LDM-2014-02-17](../../vblang/meetings/2014/LDM-2014-02-17.md)（`Nothing` 双重语义三条原话）· AnthonyDesign section 12「Null and Nothing」（L1797–1810）· ModVB `proposal-null-equality-operators.md` · `proposal-null-coalescing.md`（空指示符 `?`，与 `?=` 共享符号）_

### 场景与缺口

建议要解决的是一个具体的、可复现的别扭：对可空值类型（NVT），VB 的 `=` / `<>` 是三值逻辑——任一操作数无值时，比较结果不是 `True`/`False`，而是 `Boolean?` 的"未知"（`Nothing`）。两个都无值的可空变量比较，落在 `If` 里走的是 **False 分支**。对数据库式数据（"值可能缺失"是常态）这不符合直觉：SQL 语义里 `null` 与 `null` 视为相等。

```vb
Dim a As Integer? = Nothing
Dim b As Integer? = Nothing

' 今天：三值逻辑。a、b 皆无值 ⇒ a = b 得到 Boolean? 的 Nothing（未知），
' 在 If 中落入 False 分支：
If a = b Then
    ' 不执行。
End If
```

We 把这句话当成会议的第一件事去核实，而不是接受建议的断言。因为"VB 的 `=` 对 NVT 是不是真的三值"这个前提，决定整个特性有没有存在的必要。结论：**前提成立，且我们找到了编译器里的落点**——`Binder_Operators.vb` 在绑定比较运算符时，对 lifted（可空提升）的 `=`/`<>`，把结果类型取为 `Boolean?`：

```vb
' Binder_Operators.vb（约 L377）
operatorResultType = GetNullableTypeForBinaryOperator(leftType, rightType, booleanType)
```

即：两个 `Integer?` 比较的结果类型是 `Nullable(Of Boolean)`。这就坐实了"任一操作数无值 ⇒ 结果为 Nothing"。

进一步的现场证据让缺口更具体，也更有意思。**同一个"空"在不同拼写下行为不一致**：

```vb
' ① 字面量对字面量：Nothing = Nothing 落进"双方皆 Nothing ⇒ 取 Integer 默认值 0"
'    （ConvertNothingLiterals 对 Equals 的 both-Nothing 分支默认取 Int32），
'    于是变成 0 = 0 ⇒ True。
Dim literalCase As Boolean = (Nothing = Nothing)   ' True

' ② 变量对变量：两个无值可空变量 ⇒ Boolean? Nothing ⇒ If 中 False。
If a = b Then
    ' 不执行。
End If

' ③ 变量对字面量：编译器会警告（WRN_EqualToLiteralNothing / WRN_NotEqualToLiteralNothing）
'    ——"拿可空值与 Nothing 字面量比较"，因为此处的语义最容易被误解。
If a = Nothing Then   ' 警告：可空值与 Nothing 字面量比较；结果仍是 Boolean? Nothing。
End If

' ④ 正确的 HasValue 测试是 Is：a Is Nothing ⇒ a 无值时 True。
If a Is Nothing Then
    ' 执行。
End If
```

同一种"缺失"，四种拼写、四种结果。这条"quirk 的动物园"正是 2014-02-17 记录里"Nothing 是什么意思"三条原话在 NVT 上的回响，也正好踩中 2018.02.28 记录里那句判断——该次讨论 C# tuple 相等性（[csharplang#967](https://github.com/dotnet/csharplang/pull/967)）时，We 明确说过可空值类型相等在 VB 里"与 C# 不同，且这种不同是好的"：

> "Since the equality operator more different than Equals. It is not a reference equals, for example. Nullable value types also works some place in VB where it doesn't in C#."

> "This is likely to need different deep thought than C# (more than a port) to find VB behavior (quirks in a good way)."

We 在会议开场把这两句贴在墙上，作为本建议的**对照面**：`?=` 恰恰是"more than a port"的反面——它把 T-SQL 的 `IS [NOT] DISTINCT FROM` 直接端进 VB，把 2018 年我们珍视的 quirk 修掉。缺口是真实的；但"修掉 quirk"是否就是对的修复方向，是另一件事。

### 候选方案

**PROPOSAL A — 引入 `?=` / `?<>` 新运算符（Anthony 方案）。** 按建议原文落地，语义等价 T-SQL：

```vb
' True if both null, or both not null and equal. Otherwise, false.
If left ?= right Then
    ...
End If

' True if exactly one is null, or neither is null and left <> right.
' False if both null, or both not null and equal.
If left ?<> right Then
    ...
End If
```

两个运算符是严格的互补对：`left ?<> right ≡ Not (left ?= right)`。结果为普通 `Boolean`，不再是 `Boolean?`。建议明确其与 `=`/`<>` 的三值逻辑区分开。

**PROPOSAL B — 不动语言，用库/助手方法。** 泛型场景其实 BCL 早已给了答案：`EqualityComparer(Of T).Default.Equals(x, y)` 对任意 `T` 给出"两个 null 相等、一个 null 不等、非 null 按 `IEquatable(Of T)`/`Equals` 比较"的二值语义，且编译器不需要任何新符号。NVT 场景可用 `If(x Is Nothing, y Is Nothing, x.Equals(y))` 手写。配合 analyzer 提示，能覆盖提议的大部分价值，语言表面积为零。

**PROPOSAL C — 扩展现有 `=` / `<>` 语义（把三值改二值）。** 建议的 Alternatives 第 1 条，立即出局：这是对存量代码的静默语义改写，与"我们几乎从不做破坏性变更"直接冲突，且把 2018 年确认的 quirk 抹掉。仅作记录，不作考虑。

**PROPOSAL D — 只对 NVT 生效的窄化版。** 把 `?=` 限定在"今天会产出 `Boolean?` 的位置"（即至少一个操作数是 NVT），引用类型/字符串一律不给。范围小、语义单一，但语法冲突问题（见拷问 1）不因此消失，且"任意类型是否可用"的疑问被建议留在未决区——我们被迫替它做决定。

**PROPOSAL E — 改语法面，不引入新运算符。** 用 `Is`/`IsNot` 组合、或一个命名函数（如 `NullEquals(a, b)`），或复用 `x Is Nothing` 流分析让守卫惯用法更便宜，而不是新增 `?=` 符号。这是对"符号预算"最谨慎的一条路。

### 权衡：Q&A

- **前提核实之后：A 的核心新增量到底在哪？** 我们逐类推演。对 **NVT**：`x ?= y` 的"两者皆 null ⇒ True"确实绕开了三值逻辑的未知态——这是真实新增量。对 **String**：`=` 已经是空安全的值相等（`Nothing = Nothing` ⇒ True），`?=` 完全冗余。对 **引用类型（Object）**：`=` 在 Option Strict Off 下晚期绑定、Option Strict On 下报错，而 `Is` 才是引用相等；`?=` 的"非 null 且相等"若指**值相等**（走 `Equals`），则与 VB `=` 的引用语义分道扬镳；若指**引用相等**，则 `?=` ≡ `Is`，冗余。对 **泛型 T**：`x = y` 根本不编译，`?=` 是唯一能写的地方——但 `EqualityComparer(Of T).Default` 早已覆盖。**结论：新增量集中在 NVT 一个角落，其余场景要么冗余、要么语义未定义。**
- **A vs B：新运算符 vs `EqualityComparer` 助手。** 这是整场最尖锐的对比。`EqualityComparer(Of T).Default.Equals` 在 BCL 里存在了二十年，行为与 `?=` 的语义逐条对得上（双 null 相等、单 null 不等、非 null 用 `Equals`）。它唯一缺的是"语法糖"和"显式文档化意图"。We 问了一个难堪的问题：如果这个 helper 一直存在而开发者没有大规模使用它，为什么我们要相信一个 `?=` 符号会改变习惯？没有数据支持这个判断。`Probably`：真正的痛点不是缺一个运算符，而是**没人知道该用哪个工具**——那正是分析器/文档的职责，不是新语法的职责。
- **A vs E：`?=` 值不值得花 `?` 这个符号？** 见拷问 1。`?` 在 VB 里已经承担四种职责（NVT 类型修饰、`?.`/`?(`/`?[` 空条件访问、计划中的空指示符、`Integer? = y` 的既有拼写）。再叠加两个二元运算符，是把最常用的符号之一推向歧义悬崖。**没有充分理由，不值得。**
- **D 的窄化能不能救 A？** 不能。窄化解决语义范围，不解决词法冲突；而且"仅 NVT"会让 `?=` 与 `Integer?` 类型修饰符**共享同一个符号、出现在同一条语句**（`Dim x As Integer? = y` 与 `x ?= y`），词法上下文敏感性问题反而更尖锐。
- **C 为什么被一句话否决。** "We will almost never make breaking changes." 三值逻辑改二值，等于把 `If x = y Then`（x 无值时今天走 False）改成走 True——存量行为整体翻转，不可接受。建议把它列为 Alternative 是对的，但它连被讨论的资格都只够记录。
- **"quirks in a good way"张力。** 2018.02.28 的立场是：VB 的可空相等与 C# 不同是**资产**，值得"deep thought"而非"port"。`?=` 用 T-SQL 的模型替换 VB 的模型，是这份资产的直接出清。We 不完全统一：有人认为 quirk 是历史事故该修，有人认为它是 VB 的 SQL 直觉、动了反而陌生。**分歧被记录，但没有达成共识——这本身就是不引入新语法的理由。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

这是整场最重的技术关卡。`?` 在 VB 中已经是**过载最重的符号之一**：

- **既有：NVT 类型修饰符** `Integer?`，且 `Dim x As Integer? = 5` 是**存量合法代码**。词法序列是 `Integer` `?` `=`。若词法器把 `?` + `=` 贪婪合并成 `?=` 单 token，**这条存量代码直接破裂**。必须在"类型上下文（`?` 归属类型）"与"表达式上下文（`?` 起新运算符）"之间做上下文敏感切分——Roslyn 词法器是上下文无关的，这要落到 parser/binder 层，错误恢复与 IDE 悬空 token 全部受影响。
- **既有：空条件访问** `x?.M`、`x?(i)`、`x?[i]`——`?` 后跟 `.`/`(`/`[`。`?=` 是 `?` 后跟 `=`/`<>`，与它们不直接冲突，但证明 `?` 已被占用。
- **计划中：空指示符 `?`**（`proposal-null-coalescing.md`）——`True?`、`lookup("key")?`（后置）、`String?`（类型）。一旦落地，`a? = b`（`?` 后空格再 `=`）与 `a ?= b`（紧贴）将成为同一串字符的两种解析。符号预算到这里基本耗尽。
- **`?<>` 的组合**：`?` + `<>` 需要合成第三个含义的 token。

We 的结论：**`?=` 的语义可以讨论，但它的语法正撞在 VB 最忙的符号上。** 一个需要"上下文相关词法"的新运算符，等于把歧义税摊给编译器和每一位读代码的人。

#### 2. 角案例与边界语义

逐条过建议没定义的角落：

- **链式与混用**：`a ?= b ?= c` 怎么结合？`a = b ?= c`（`=` 与 `?=` 同优先级混用）？建议未给优先级/结合性。若按关系运算符左结合，`a ?= b ?= c` 是 `(a ?= b) ?= c`——拿 Boolean 再和一个操作数比，多半是错。`Probably`：应该**禁止链式**，或定义成语法错误。
- **`Boolean?` 操作数**：三值逻辑的另一个战场是 `And`/`Or`/`AndAlso`/`OrElse`（spec §逻辑运算符：`And`/`Or` lifted 到 `Boolean?` 时明确定义三值；`AndAlso`/`OrElse` 首操作数为 null 时结果恒为 null）。`a ?= b` 若一方是 `Boolean?`，结果二值 Boolean——但紧接着的 `AndAlso`/`OrElse` 又把三值拉回来，两套逻辑在同一条表达式里打架。
- **`Nothing` 字面量**：`x ?= Nothing` 应等价于 `x Is Nothing`（HasValue 测试）还是"与无值可空比较"？建议未说。若前者，则 `?=` 吃掉 `Is` 的地盘；若后者，语义与 ③ 的警告案例纠缠。
- **枚举可空**：`Enum?` 比较走底层整数相等，`?=` 是否同样处理？
- **用户重载 `=`**：自定义类型可以重载 `=`/`<>`。`?=` 若 desugar 成 `EqualityComparer(Of T).Default.Equals`，**用户重载被绕过**（该 helper 走 `IEquatable(Of T)`/`Equals`，不走 `=` 运算符）——这是无声的语义陷阱。若 desugar 成 `x.Equals(y)`，同理。`?=` 必须书面声明它**不参与**用户重载，否则与 `=` 的"等于"措辞形成虚假一致性。
- **匿名类型 / tuple**：匿名类型有 `Equals`，tuple 在 .NET 也有。`?=` 是否对它们放开？2018.02.28 讨论的 tuple 相等正是"需要 deep thought"的领域，`?=` 一刀切进去会踩同样的坑。

#### 3. 作用域与绑定

`?=` 需要一个全新的 SyntaxKind、BoundBinaryOperator 分支（或降级为重写后的 helper 调用）与语义模型符号。若走 helper 降级，`GetOperation`/IOperation 面要暴露什么？是 `BinaryOperatorKind` 还是 `Invocation`？这对 analyzer 与 IDE 可见性有实际影响。若走原生绑定，则 binder 里要为"null 检查 + 相等"两步合成一个节点，语义模型里没有对应符号可指向。**两条路都留下"运算符到底是谁"的尾巴，建议原文完全没碰。**

#### 4. 与既有特性的交互

- **`Option Compare`**：`=` 对 String 受 `Option Compare`（binary/text）影响。`?=` 若对 String 也走"相等"，是否继承 Option Compare？若继承，则它并不纯粹是"空安全"运算符；若不继承，又多一条分叉。建议未提。
- **`Option Strict`**：On 下 `Object` 操作数在 `=` 处是错误（BC30452）；Off 下是晚期绑定。`?=` 对 `Object` 操作数必须**由编译期定义**（不能晚期绑定），否则两条路径行为不一致。且 `?=` 若接受任意 `Object`，等于把晚期绑定世界的一个角固定成早期绑定——改变异常时机。
- **`IsTrue`/`IsFalse`**：`?=` 的结果是普通 Boolean，不涉及。但 `?=` 出现在 `If` 的条件位置时，`isOperandOfConditionalBranch` 的既有优化路径（如 `forceToBooleanType`）要不要为新运算符开门？需要绑定器分支。
- **`Select Case` / 查询表达式**：`Select Case x ?= y` 无意义（Case 匹配不是这样用）；查询的 `Where(Function(x) x ?= y)` 若降级为 helper 调用，表达式树可容纳（方法调用合法）——这条倒是通的。
- **流分析**：`If x ?= Nothing Then` 之后，`x` 应被流分析收窄为"无值"还是"非空"？`?=` 的结果不携带任何一侧的 null 状态，而 `Is`/`IsNot` 会。这意味着 `?=` 与刚定的共享流分析引擎**不产生交互事实**——又一个"新特性不贡献流状态"的角落。

#### 5. Breaking change 与兼容性

新增 token 本身不破坏存量表达式——**前提是词法切分做对**。真正的风险在 `?`：任何把 `?=` 吞掉的贪心词法都会击穿 `Integer? = y`、`String? = value` 等存量声明。此外，`?=` 的落地会触发 IDE/analyzer 对 `?` 的补全与颜色化改动（"输入 `?` 会弹什么"），影响面比一个二元运算符大。**兼容性分析必须写成"存量 `?` 用法矩阵 + 词法回归测试"，建议里没有。**

#### 6. Option Strict / 编译选项分叉

见拷问 4。两条路径必须一致：`?=` 在 On/Off 下都不得晚期绑定、不得改变既有成功绑定的代码。这条可以满足（编译期定义），但前提是"Object 操作数也编译期定义"——而 VB 的哲学里 `Object` 运算恰恰留给运行期。**在 Object 上定义编译期语义，本身就是偏离。**

#### 7. IDE / IntelliSense 影响

新运算符符号的着色、补全、语法错误恢复；`?` 弹窗候选在 `?=` 落地后要新增一类；把 `If(a Is Nothing, b Is Nothing, a.Equals(b))` 重构为 `a ?= b` 的 quick action。这些在原型里都要验证。**We 的通用立场（上一场 TypeOf 会议已声明）：不做进规范等于没设计。**

#### 8. 数据 / 普遍性

缺口有**编译器源码佐证**（lifted 比较返回 `Boolean?`）和 2014 三条原话，但**没有使用频率数据**：`x = Nothing` 的警告（WRN_EqualToLiteralNothing）在真实代码库的触发率？数据库取值场景中"双 null 比较"的真实占比？开发者是否在大量手写 `If(a Is Nothing, b Is Nothing, ...)`？这些我们一条数据都没有。`Suspect`：NVT 在业务代码里本就比引用类型少，`?=` 的战场又进一步缩到"NVT × 双 null"——普遍性存疑。

#### 9. 更简替代

这是本建议最致命的对手：

- **`EqualityComparer(Of T).Default.Equals(a, b)`**——语义逐条吻合，泛型可用，零语言成本。
- **`If(a Is Nothing, b Is Nothing, a.Equals(b))`**——NVT/引用都可读。
- **`a Is Nothing AndAlso b Is Nothing OrElse a = b`**——建议 Alternatives 自己列的手工组合。
- **analyzer + 诊断**：在"可空值 == 可空值"或"可空值 == Nothing 字面量"处给出更聪明的提示与修复，把"该用哪个工具"教给用户。
- **共享流分析引擎**：`If x IsNot Nothing Then` 之后的收窄（已定 Active）让 null 处理变得更便宜，从需求端削弱 `?=`。

We 认为：**用语言功能解决"没人知道该用哪个工具"的问题，是把教学问题错误地诊断为语法问题。**

#### 10. 成本 / 优先级

词法上下文敏感切分 + 新语法节点 + 绑定/降级 + 语义模型 + IDE 全链路 + 与 `?` 家族（空指示符、NRT）的消歧矩阵——这是一个**中等偏上**的特性，且必须排在 `?` 符号定局（空指示符是否落地）之后，否则白做。优先级在可预见窗口内不高，尤其当 `EqualityComparer` 已在 BCL 里。

#### 11. 运行时 / CLR 硬约束

无。`?=` 是纯编译期概念，降级为 null 检查 + 方法调用（`Equals`/helper），不触 PEVerify、不触 CLR 存储规则。表达式树经方法调用面可容纳。CLR 层不构成障碍——**这也意味着它是"可以随时用库实现"的一类功能，语言不必抢。**

#### 12. 值不值得做：价值 × 成本 × 风险

- **价值**：真实但窄。NVT 双 null 比较的别扭是编译源码坐实的，`x = Nothing` 警告证明用户会踩。但新增量收敛到一个角落，且 `EqualityComparer` 已覆盖。
- **成本**：中。语法面（`?` 冲突）是最大的单一成本。
- **风险**：中高。符号过载、与 2018 quirk 张力、参考语义未定义、家族（空指示符/NRT）未定导致接口可能返工。

### VB 基因对照

- **原则 3「不引入第二种做事方式」**：本建议的最重砝码。比较这件事，VB 已有 `=`、`<>`、`Is`、`IsNot`、`Like`，BCL 有 `Equals`/`EqualityComparer`。`?=` 是又一个"等于"，且语义与 `=` 只差一个"双 null"角落。**没有第二种做事方式，就没有 `?=` 的生存空间。**
- **原则 5「读起来像英语、对新手友好」**：`?=` 是三个符号的拼接，不读作英语；对首次接触的开发者，"问号等于"是什么直觉？与 `= "quirk"` 的既有教义并存，教学负担不降反升。
- **原则 7「避免隐蔽的控制流/语义变化」**：`?=` 不改变存量，但 `?` 词法扩展若失手就是隐蔽破裂；`?=` 对用户重载 `=` 的绕过是另一类隐蔽语义。双份警惕。
- **原则 9「消除常见样板」**：部分是——`x ?= y` 比 `If(x Is Nothing, y Is Nothing, x.Equals(y))` 短。但样板最重的其实是泛型，而那里 `EqualityComparer` 已经是单行。**样板消除的边际收益有限。**
- **原则 4「默认跟随 C#，除非有充分理由」**：C# 没有 `?=`（它的 `==` 对 NVT 本就是二值，不需要）。本建议的模板是 **T-SQL**，不是 C#。跟随 SQL 不是坏方向，但它把"关系数据库语义"硬编码进语言通用运算符，与"默认跟随 C#"的既定路线无锚。
- **原则 10「冗长只在有用时是美德」**：`x Is Nothing AndAlso y Is Nothing OrElse x.Equals(y)` 是冗长，但它的冗长恰好**逐字说清了语义**；`?=` 的简短掩盖了"等于"究竟指什么。
- **2.3 主线对照表**：`Null 安全全家桶` 一行写明——主线对 null 条件 AddHandler 已 No Plans，Anthony 大范围铺开，"主线保守，Anthony 激进"。本建议属于 **Anthony 独立激进延伸**，且携带一个与主线直接冲突的隐含立场：2018.02.28 把可空相等 quirk 当作资产，`?=` 把它当负债。

### 诚实分层

- **事实**：VB 的 lifted `=`/`<>` 对 NVT 返回 `Boolean?`（`Binder_Operators.vb` L377 `GetNullableTypeForBinaryOperator(leftType, rightType, booleanType)`）；`Nothing = Nothing`（双字面量）经 `ConvertNothingLiterals` 落为 Int32 默认值 `0 = 0` ⇒ True；`x = Nothing`（NVT 变量对字面量）触发 `WRN_EqualToLiteralNothing`/`WRN_NotEqualToLiteralNothing` 警告；`x Is Nothing` 是可空值的 HasValue 测试（`Is`/`IsNot` 绑定对 nullable 目标限定 Nothing 字面量，Binder_Operators.vb L115–122）；spec §逻辑运算符把三值逻辑明确限定在 `Boolean?` 的 `And`/`Or`/`AndAlso`/`OrElse`（expressions.md L2569–2573、L2704），§关系运算符声明"All of the relational operators result in a Boolean value"（L2392，指非提升形式）；2018.02.28 记录对可空相等 quirk 的立场原话见上文；2014-02-17 记录有 `Nothing` 双重语义三条原话与 `Nothing?`/`Nuffink` 档案；Anthony section 12（L1797–1810）为 `?=`/`?<>` 的唯一出处，未给优先级/结合性/适用范围。
- **Probably**：`x = Nothing` 的真实语义是"提升相等 + 未知态"，而不是 HasValue 测试（HasValue 测试是 `Is`）——但这条依赖我方的绑定代码推演，建议以最小运行例复测确认；`?=` 若落地必然要求禁止链式。
- **Suspect**：建议 Motivation 把"数据库取值"当作主要场景，但 `DBNull.Value` 是 `Object` 且**不是** `Nothing`——`?=` 对 `DBNull` 的帮助需要单独定义，建议未区分（与 Null 字面量会议的四概念警告同源）；"开发者大规模需要双 null 比较"无数据支撑；引用类型场景 `?=` 的"相等"到底指值相等还是引用相等，建议未定义。
- **OPEN QUESTIONS**：① lifted `=` 的"双 null ⇒ Boolean? Nothing"是否在运行时（非常量折叠）成立，需最小运行例复核；② `x ?= Nothing` 语义是否绑定 `Is`；③ `?=` 对用户重载 `=` 的类型是否**明确不参与**用户运算符；④ 与空指示符 `?`（null-coalescing 建议）的消歧规则如何写；⑤ `Option Compare` 是否继承。
- **TODO**：把 lifted 相等的最小运行例写进编译测试；统计 `WRN_EqualToLiteralNothing` 在真实代码库的触发率；跟踪空指示符 `?` 的会议结论（它先定局，`?=` 才有语法基础）。

### RESOLUTION:

本建议标记为 **Table**。语义前提经核实成立，但语法与价值不成立：

1. **不引入 `?=` / `?<>` 运算符。** 三点理由：① `?` 符号预算已耗尽（NVT 类型修饰、空条件访问、计划中的空指示符），`?=` 需要上下文敏感词法，是存量 `Integer? = y` 的破裂风险；② 语义在引用类型上未定义（值相等 vs 引用相等），且必然绕过用户重载的 `=`——新运算符自带语义陷阱；③ 比较这件事已有 `=`/`<>`/`Is`/`IsNot`/`EqualityComparer` 足够多的工具，`?=` 是"第二种做事方式"的教科书案例。
2. **"二值可空相等"这个需求本身是真实的**，但它应该走库/分析器路径，不是语言路径：`EqualityComparer(Of T).Default.Equals(a, b)` 已覆盖泛型；对 NVT，分析器在 `x = y` / `x = Nothing` 处给出指向 `Is`/`Equals` 的更聪明诊断。**这是 80% 的价值，零语法成本。**
3. **2018.02.28 的 quirk 立场维持**：可空值类型相等在 VB 里与 C# 不同，是 "quirks in a good way"，需要 "different deep thought than C# (more than a port)"。`?=` 是 T-SQL 移植，方向与这条直觉相反。如果未来要动 quirk，先做 `x = Nothing` 警告的体验改进，而不是给开发者一个新符号去绕过它。
4. **不批准任何"扩展现有 `=` 为二值"的变体（PROPOSAL C）**：存量破坏，永不考虑。
5. **与空指示符 `?`（null-coalescing 建议）解耦**：`?=` 的语法命运挂在 `?` 符号的定局上；即使未来家族重提，也必须在 `?` 用途清点之后。

**Implication:**
- 建议状态标注为 `LDM Table`；在 proposal 头部加一行"LDM 2026-08-08: Table，理由见会议纪要"。
- 把"二值可空相等"拆为两个非语言工作项：① analyzer 诊断（`x = Nothing` → 建议 `Is`；`x = y` 双 NVT → 建议 `EqualityComparer`/显式 `If` 组合）；② 文档澄清 `=`/`<>`/`Is`/`Nothing` 的差异矩阵。
- 记录对 2018.02.28 "quirks in a good way" 的维持立场，供 Null 安全家族其他建议（null-coalescing、null-safe-behaviors）参考。
- 建议作者如需重提：补齐语法文法（含 `?` 词法冲突矩阵）、引用类型语义二选一、泛型与用户重载交互、数据支撑，再谈。

**Verdict: Table**（`?=`/`?<>` 语法不引入；"二值可空相等"需求转库/分析器路径）。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-null-equality-operators.md` — 引入 `?=` / `?<>` 两个二值逻辑相等运算符（等价 T-SQL `IS [NOT] DISTINCT FROM`），两个 null 视为相等，结果恒为 `Boolean`。
- 来源：Anthony 原文章节 12「Null and Nothing」（L1797–1810，`?=`/`?<>` 两个注释块）；语义模型引自 T-SQL。
- 配方目标：让"可能为 null 的操作数"比较遵循二值逻辑，摆脱 VB `=`/`<>` 对 NVT 的三值逻辑（任一操作数 null ⇒ 结果 `Nothing`）。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 前提**经编译器源码核实为真**（lifted `=` 返回 `Boolean?`），缺口可演示；但示例止于书面、状态栏为占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`），无原型/运行；关键子效果（引用类型语义、泛型、`DBNull` 场景）缺失或未定义 | 已提供 / 已检查 | 效果未达"已运行"；核心语义（"非 null 且相等"的"相等"指什么）未定型 → 效果证据封顶 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。语法是 T-SQL/C# 风格的 `?=`，未做 VB 化改造；`?` 符号与既有 NVT 修饰符、空条件访问、计划空指示符过载，违反原则 3（第二种做事方式）；同时捆绑了"绕过用户重载 `=`"的未定义语义。唯一 VB 血缘是"承认三值逻辑 quirk 并想修掉它"——方向与 2018 直觉相反 | 已检查 | 照搬外部模型；符号过载；语义未绑定 VB 既有比较体系 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文一致、互补对（`?=`/`?<>`）定义正确；但无文法/优先级/结合性，未定义适用范围（仅 NVT vs 任意类型）、引用类型语义、泛型、用户重载、`Option Compare`/`Option Strict` 交互；未决问题 3 个但都是关键设计点且无定夺方式 | 已检查 | 缺文法与兼容性章节；未决问题低估；状态栏占位 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。风（演化一致性）受损：`?` 符号多义化、与空指示符/NRT 家族未消歧；暗风险突出：词法上下文敏感性威胁存量 `Integer? = y`；与 2018 年 quirk 直觉冲突。光（差异化 DX）与雷（消除样板）有小幅正向，但被符号过载与语义未定抵消 | 已检查（待定） | 语法破裂风险无对冲设计；实际影响须"已采纳"后判定 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。T-SQL `IS [NOT] DISTINCT FROM` 来源标注准确；但未声明借鉴 C# nullable 语义、未点明与 BCL `EqualityComparer(Of T).Default` 的实质重合（这是最接近的既有实现）；`?` 符号来源混杂（既有 NVT 修饰 + 计划空指示符）；`DBNull` 场景混淆（DBNull ≠ Nothing） | 已检查 | 关键成分（EqualityComparer 重合）未标注；符号来源混杂 |

### 设计原则对照

- **与 VB 基因：偏离为主**。违反原则 3（第二种做事方式——比较已有 `=`/`<>`/`Is`/`IsNot`/`EqualityComparer`）；`?=` 不读作英语（原则 5 弱）；原则 7（隐蔽语义）双份风险（词法破裂 + 绕过用户重载）；原则 9（消除样板）仅在 NVT 双 null 角落成立，边际收益被 `EqualityComparer` 单行覆盖。
- **与主线关系：Anthony 独立激进延伸**（2.3 对照表 Null 安全全家桶：主线保守、Anthony 激进），且与主线直觉**冲突**——2018.02.28 把可空相等 quirk 当作资产（"quirks in a good way"、"different deep thought than C# (more than a port)"），`?=` 是 T-SQL 移植，方向相反。模板语言是 T-SQL 而非 C#，与"默认跟随 C#"无锚。
- **破坏性变更：间接有**。新增 token 本身非破坏，但 `?` 词法扩展若实现不严谨会击穿存量 `Integer? = y` 等声明；建议无兼容性矩阵、无 langversion 门控设计。

### 总评

- **达成程度：部分达成**。缺口真实且前提被编译器源码证实（三值逻辑存在、`Nothing` 四种拼写不一致）；但方案主体（新语法符号）与基因冲突、语义在引用类型/泛型/用户重载上未定义、与 2018 quirk 直觉相反、被既有 BCL helper 实质覆盖。
- **LDM 三态建议：Table**。`?=`/`?<>` 不引入；"二值可空相等"需求转 analyzer/文档/BCL 路径。若未来重提，须先满足返工清单。
- **主要问题**：① `?` 符号过载与词法上下文敏感性（存量 `Integer? = y` 破裂风险）；② 引用类型"相等"语义未定义，且必然绕过用户重载 `=`；③ 与 2018.02.28 "quirks in a good way" 直觉冲突，是 T-SQL 移植而非 VB 化；④ `EqualityComparer(Of T).Default` 已覆盖核心价值，无数据证明新符号改变习惯；⑤ 证据止于"已提供/已检查"（占位状态、无原型、无数据）。

### 返工建议

- **补充章节**：文法与优先级（BNF、结合性、链式禁令）、语义定义专节（引用类型 = 值相等或引用相等二选一；泛型 T；用户重载 `=` 是否参与；`Option Compare`/`Option Strict` 两路径；`DBNull` 明确排除）、`?` 符号冲突矩阵（`Integer?`、`?.`、空指示符、`?(`/`?[`）、兼容性分析（存量 `?` 用法回归 + langversion 门控）。
- **补充证据**：最小原型（验证 lifted `=` 双 null 的运行时行为、`?` 词法切分的上下文敏感性）；`WRN_EqualToLiteralNothing` 触发率与数据库代码占比的量化数据；`EqualityComparer` 未被采用原因的调研。
- **未决问题处理**：三个 OPEN QUESTIONS 逐个给定夺方式（`x ?= Nothing` 绑定 `Is`；`?=` 明确不参与用户运算符；`Option Compare` 继承现状）；`?` 家族消歧规则须等空指示符会议定局后再议。
- **设计探索**：若不引入符号，探索 analyzer 诊断提案（`x = Nothing` → `Is Nothing`；双 NVT 比较 → 显式 `If` 组合/`EqualityComparer`），以及把 `x = Nothing` 警告文案与修复动作做对是否能覆盖需求。

---

## 附录：C# 生态与互操作考量

> 本附录核实 C# 侧对该主题的「现实方向」，对照本提案（`?=`/`?<>`）与既有 RESOLUTION。背景地图见 `..\..\csharplang-index.md`（T1–T8）；以下 C# 引文均在 `..\..\csharplang` 逐字核实。
> **诚实声明**：本提案是 VB 内部语法提案，C# 侧没有 `?=` 的对应物——这恰恰是本附录最有信息量的结论，而非缺点。C# 对「二值可空相等」的答案早就给出，且不在运算符层面。

### 相关 C# 现实方向

**R1 — C# 的「双 null 相等」早已由 lifted `==` 解决，结果恒为 `bool`。**
C# 对可空值类型（`int?`）的 `==`/`!=` 是**二值**逻辑：双 null ⇒ `true`、单 null ⇒ `false`、双非 null ⇒ 值比较，结果类型是普通 `bool`。仓库内最接近逐字的证据来自 tuple-equality 提案对 nullable 相等给出的公式：

> "In the nullable case, additional checks for `temp1.HasValue` and `temp2.HasValue` are used. For instance, `nullableT1 == nullableT2` evaluates as `temp1.HasValue == temp2.HasValue ? (temp1.HasValue ? ... : true) : false`."
> → `proposals\csharp-7.3\tuple-equality.md`（L9）

代入 `HasValue` 全 false ⇒ `false == false` 为 true ⇒ 取 `(temp1.HasValue ? ... : true)` 的 else ⇒ `true`。即 **C# 端两个无值 `int?` 比较为 `true`**。同文件还强调结果恒为普通 `bool`：

> "The tuple comparison always ends up returning a `bool`."
> → `proposals\csharp-7.3\tuple-equality.md`（L11）

同文件给出 NVT 的 null 测试语法：

> "If you have a `struct S` without `operator==`, the `(S?)x == null` comparison is allowed, and it is interpreted as `((S?).x).HasValue`."
> → `proposals\csharp-7.3\tuple-equality.md`（L69）

对照本提案缺口（见「场景与缺口」）：VB 的 lifted `=` 返回 `Boolean?`（三值），C# 的 lifted `==` 返回 `bool`（二值）。**「二值可空相等」在 C# 是语言默认，不是需要申请的特性。** 这正是 RESOLUTION 原则 4 那句「C# 没有 `?=`（它的 `==` 对 NVT 本就是二值，不需要）」的仓库内佐证。

**R2 — C# 的 null 测试惯用法是 `is null` / `is not null`（C# 8/9），刻意与 `==` 解耦。**
C# 8 把 `e is null` 作为常量模式引入，语义上走 `object.Equals`，动机是**不调用用户重载的 `==`**：

> "The pattern *c* is considered matching the converted input value *e* if `object.Equals(c, e)` would return `true`."
> → `proposals\csharp-8.0\patterns.md`（Constant Pattern，L131）
>
> "We expect to see `e is null` as the most common way to test for `null` in newly written code, as it cannot invoke a user-defined `operator==`."
> → `proposals\csharp-8.0\patterns.md`（Constant Pattern，L133）

C# 9 的 `not` 组合子给出正向写法：

> "More readable than the current idiom `e is object`, this pattern clearly expresses that one is checking for a non-null value."
> → `proposals\csharp-9.0\patterns3.md`（Pattern Combinators，L105）

语义上 `is null` 走 `object.Equals(null, e)`（对引用类型即引用相等），不参与 `==` 重载——这与 VB `Is`/`IsNot Nothing` 的「参考/身份比较、不参与 `=`」**同构**。提案的 `x ?= Nothing` 二义性（绑定 `Is` 还是「与无值可空比较」，见「深度追问：拷问 2」）在 C# 不存在：**null 测试是 `is null`，相等比较是 `==`，两条路从 C# 8 起被刻意分开。** 另注：C# 的类型模式允许 `int? x = 3; if (x is int v)` 测试可空值类型的非空取值（`proposals\csharp-8.0\patterns.md` L112–119）——与 VB 侧 `TypeOf ... Is` 类型流分析（上一场会议）是同一轴线的两套实现。

**R3 — C# NRT 流分析让 `is null`/`is not null` 参与收窄。**
C# 8 NRT 的流分析原话：

> "A flow analysis tracks nullable reference variables. Where the analysis deems that they would not be null (e.g. after a check or an assignment), their value will be considered a non-null reference."
> → `proposals\csharp-8.0\nullable-reference-types.md`（Checking of nullable references，L20）

`is null`/`is not null` 正是这类「check」——`if (worker is null) throw ...` 之后 `worker` 被收窄为非空（同文件 L77–107 的 null-guard 示例）。这与 VB 共享流分析引擎在 `IsNot Nothing` 之后的收窄（2026-08-08 可空性流分析会议，已定 Active）**同向**。而 `?=` 的结果不携带任何一侧的 null 状态（拷问 4）——它在 C# 生态里既不对齐 `is null`（测试、收窄），也不对齐 `==`（比较、二值），是两套惯用法之外的孤儿。

**R4 — C# NRT 是逐项目/逐行 opt-in 的，且以 attribute 形式进元数据。**

> "Both contexts can be specified at the project level (outside of C# source code), or anywhere within a source file via `#nullable` pre-processor directives. If no project level settings are provided the default is for both contexts to be *disabled*."
> → `proposals\csharp-8.0\nullable-reference-types-specification.md`（Nullable contexts，L117）

元数据侧：仓库内可见 `NullableAttribute` 的编码示例（`.custom instance void NullableAttribute::.ctor(uint8)`，→ `proposals\csharp-14.0\extensions.md` L735）；精确编码规则由 Roslyn 实现维护（见下 OPEN QUESTIONS）。

### 现实 vs 提案

| 本提案 / RESOLUTION 的关注点 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| "二值可空相等"（双 null ⇒ True） | lifted `==` 对 NVT 恒为二值 `bool`（R1） | **兼容** | C# 在运算符层面就解决了；RESOLUTION 原则 4 的断言有仓库内公式佐证。C# 不需要 `?=`，VBScript.NET 也不需要 |
| "不引新运算符，走 `EqualityComparer`/分析器" | C# 同样没引 `?=`，靠 `==` + `is null` + 流分析覆盖 | **兼容** | 两语言独立收敛到「不加运算符」；`EqualityComparer(Of T).Default` 与 C# 的 `==`/BCL 语义一致，AOT/source-gen 下无碍（索引 T5/T6） |
| `Is`/`IsNot Nothing` 作为 null 测试 | `is null`/`is not null` 是 C# 主流 null 测试（R2），参与流分析（R3） | **兼容** | VB `Is`/`IsNot` 与 C# `is null`/`is not null` 同构（引用语义、不参与 `=` 重载） |
| VB 三值 `=` 与 C# 二值 `==` 的分叉 | C# 无三值概念 | **冲突（已承认）** | 2018.02.28 立场刻意保留三值 quirk（"quirks in a good way"）；是语言选择而非互操作缺陷，跨语言边界需文档化、不可静默对齐 |
| `?=` 的引用类型「相等」值/引用二义性 | C# 用 `==`（值/重载）vs `is null`（引用）分开两条路 | **需桥接** | 提案没接上 C# 的分界；VBScript.NET 应沿用 `Is`（引用）与 `=`/`EqualityComparer`（值）的分界 |
| `?=` 的模板 T-SQL `IS [NOT] DISTINCT FROM` | C# 生态无对应物 | **脱节** | T-SQL 不是 C#/CLR 的建模来源；C# 解决 null 相等靠 lifted 运算符，与 SQL 无关 |
| 流分析交互（拷问 4：`?=` 不携带 null 状态） | C# `is null`/`is not null` 是流分析可识别的收窄点（R3） | **脱节** | `?=` 在 C# 无对应；VB 的收窄由 `Is`/`IsNot` 承担，方向与 C# 一致 |

**一句话判定**：本提案与 C# interop 的直接接触面很薄，但**间接接触面是决定性的**——C# 现实（R1–R3）从独立侧支持了 RESOLUTION 的核心结论（不加运算符、`Is`/`EqualityComparer`/流分析是正确工具），并揭示提案与 C# 唯一的真实摩擦不是「该不该二值」，而是 **VB 三值 `=` 与 C# 二值 `==` 的既有分叉**——这个分叉被 LDM 刻意保留，无需 `?=` 去桥，只需在互操作边界文档化。

### 对 VBScript.NET 的适应建议

1. **默认安全基线 = `Is`/`IsNot Nothing`**：null 测试固定走引用语义（对齐 C# `is null`/`is not null`），共享流分析引擎在 `IsNot Nothing` 之后收窄——这是「默认安全、按需动态」里安全的那一半，且与 C# 惯用法一一对应。
2. **按需二值 = analyzer + `EqualityComparer(Of T).Default`**：把「双 null 相等」的 NVT 场景引导到 BCL helper（RESOLUTION 工作项 ①）。`EqualityComparer` 在 AOT/trimming/source-gen 下无反射负担（索引 T5/T6），是 C# 生态同样认可的答案。
3. **识别 C# NRT 元数据**：.vbx 编译器应读取 `NullableAttribute`/`NullableContextAttribute`（精确编码见 OPEN QUESTIONS），以正确理解 C# 库的 nullable 签名，使流分析与警告跨语言对齐。只「读」不改写 VB 语义。
4. **文档化三值/二值分叉**：`.vbx` 与 C# 共享 `Nullable<T>` 数据时，「双 null 比较」结果不同（VB `Boolean? Nothing` / C# `true`）。互操作指南应写明：.vbx 内部走 `Is` 组合，跨 C# 边界走 `EqualityComparer`，保证两边读同一数据结论一致。
5. **守住 `?` 符号预算**：C# 的 `?` 同样过载（`int?`、`?.`、`??`、三元 `?:`），C# 的选择是不加 `?=`。VBScript.NET 沿用同一纪律——这是两个语言独立收敛到的同一结论，不应以「C# 兼容」为理由重开 `?=`。

### 对既有 RESOLUTION / 三态判定的影响

**无实质冲击，三态维持 Table。** 本附录从 C# 侧独立验证了 RESOLUTION 三条主干：① C# 的 lifted `==` 早已是二值，`?=` 针对的缺口在 C# 不存在（R1）；② C# 的 null 测试惯用法与 VB `Is`/`IsNot` 同构，「不新增运算符」是两语言共同选择（R2）；③ 流分析是 null 语义的载体，`?=` 不贡献流状态，走 `Is`/`IsNot` 才是与 C# 一致的路径（R3）。

**新增提示一条**：未来若重提「二值可空相等」，不应以「与 C# 对齐」为理由——C# 本就不需要 `?=`，且 C# 侧没有「补丁运算符」先例。重提的唯一正当理由是「VB 开发者想要 T-SQL 语义」，那是一个产品定位问题，不是互操作问题。

### 引用核对

**逐字引用来源**（均在本仓库 `..\..\csharplang` 逐字核实）：

| 引用 | 来源 |
|---|---|
| "In the nullable case, additional checks for `temp1.HasValue` and `temp2.HasValue` are used. For instance, `nullableT1 == nullableT2` evaluates as `temp1.HasValue == temp2.HasValue ? (temp1.HasValue ? ... : true) : false`." | `proposals\csharp-7.3\tuple-equality.md` |
| "The tuple comparison always ends up returning a `bool`." | `proposals\csharp-7.3\tuple-equality.md` |
| "If you have a `struct S` without `operator==`, the `(S?)x == null` comparison is allowed, and it is interpreted as `((S?).x).HasValue`." | `proposals\csharp-7.3\tuple-equality.md` |
| "The pattern *c* is considered matching the converted input value *e* if `object.Equals(c, e)` would return `true`." | `proposals\csharp-8.0\patterns.md` |
| "We expect to see `e is null` as the most common way to test for `null` in newly written code, as it cannot invoke a user-defined `operator==`." | `proposals\csharp-8.0\patterns.md` |
| "More readable than the current idiom `e is object`, this pattern clearly expresses that one is checking for a non-null value." | `proposals\csharp-9.0\patterns3.md` |
| "A flow analysis tracks nullable reference variables. Where the analysis deems that they would not be null (e.g. after a check or an assignment), their value will be considered a non-null reference." | `proposals\csharp-8.0\nullable-reference-types.md` |
| "Both contexts can be specified at the project level (outside of C# source code), or anywhere within a source file via `#nullable` pre-processor directives. If no project level settings are provided the default is for both contexts to be *disabled*." | `proposals\csharp-8.0\nullable-reference-types-specification.md` |

**未使用索引第四节 6 段已核实原文的说明**：native-integers / function-pointers / blittable / span-safety / ref-struct-interfaces / unsafe-evolution-VB 六段与本提案（空相等运算符）主题关系弱，不硬凑。unsafe-evolution 的 VB 表态（"We do not need to add support to Visual Basic for *requires-unsafe* members…"，索引 T8 已核实）与本提案无直接关联，仅作为「C# 现实如何看待 VB」的背景存在。

**OPEN QUESTIONS**：
1. C# lifted `==` 的标准措辞（§11.4.8 Lifted operators）位于 `dotnet/csharpstandard`，本镜像 `spec\expressions.md` 仅链接索引、无正文；本附录以 `tuple-equality.md` 公式为仓库内证据，标准逐字文本**未核实**。
2. `NullableAttribute`/`NullableContextAttribute` 的精确元数据编码由 Roslyn 实现维护，不在 csharplang 仓库；本仓库仅见 `extensions.md` L735 的示例行，编码细节**待核实**。
3. C# 端是否有 LDM 记录明确讨论「`is null` 的引用相等 vs `==` 重载」的取舍，本附录只在 `patterns.md` L133 找到动机句（"cannot invoke a user-defined operator=="），更完整的 LDM 讨论**未检索到**。
