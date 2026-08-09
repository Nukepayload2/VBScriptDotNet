# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。我们今天回到语句层——审阅 Anthony 第 3.8 节提出的裸 `Throw` 异常类型推断。这次讨论比预期的要短，不是因为内容少，而是因为当我们把镜头的焦距拉远，看到整张条件形态→异常类型的映射表之后，会议很快收敛到"哪些部分是魔法、哪些部分是我们可以容忍的魔法"这一个问题上。

## Agenda

* [Proposal: Throw 异常类型推断（Throw Exception Type Inference）](#proposal-throw-异常类型推断)

## Proposal: Throw 异常类型推断

_Related: ModVB：`Null` 字面量建议、`Guarded Let`（inactive）、`Case Else` 变量（inactive）、类型流分析（守卫语句含 `Throw`）；主线：vblang #197 Inferred `Set` Parameter Type；vblang spec `statements.md` 的 `ThrowStatement` 文法_

### 场景与缺口

The proposal starts from an observation that's hard to argue with：参数校验与前置状态校验是极高频的样板。今天最朴素的写法要求显式写出异常类型、`NameOf`、有时还要消息：

```vb
Sub Validate(name As String, count As Integer)
    If name Is Nothing Then Throw New ArgumentNullException(NameOf(name))
    If count < 0 Then Throw New ArgumentOutOfRangeException(NameOf(count))
End Sub
```

建议想把最常见的三种形态压成一行，异常类型与参数名都由编译器"根据条件的形态"推断：

```vb
Sub Validate(name As String, count As Integer, IsClosed As Boolean)
    If name Is Nothing Then Throw   ' ArgumentNullException，针对 name
    If count < 0 Then Throw         ' ArgumentOutOfRangeException，针对 count
    If IsClosed Then Throw          ' InvalidOperationException
End Sub
```

We agree the syntax surface is genuinely VB 味儿：`If ... Then Throw` 完全由既有关键字拼成，读起来像英语，零新关键字。`Anthony` 原文甚至在**不止一处**假设这个特性存在——递归 lambda 里的 `If n < 0 Then Throw`、属性 setter 里的 `If value < 0 Then Throw`、`In`/`NotIn` 运算符示例里的 `If input In bannedWords Then Throw`、Smart Attributes 里的 `If value = Guid.Empty Then Throw`。它在整套设计里是"被反复调用的基础设施"，不是一次性玩具。这一点我们记下了：要么让它成立，要么那些示例全部退化为无效代码。

但"场景是真的"不等于"方案是对的"。我们把方案拆开后逐条过，下面是从中提炼出的候选。

### 候选方案

**PROPOSAL A — 完整条件形态 → 异常映射。** 按建议原文实现：编译器维护一张"条件形态 → 异常类型"映射表（`Is Nothing`→`ArgumentNullException`、`< 0`→`ArgumentOutOfRangeException`、布尔→`InvalidOperationException`），并尽可能自动生成参数名与消息。

**PROPOSAL B — 窄种子：仅 `Is Nothing` → `ArgumentNullException`。** 只认一种形态：对单个简单变量/参数做 `Is Nothing` 测试，推断 `ArgumentNullException`，`paramName` 取该变量的声明名。没有映射表，只有一条规则。

**PROPOSAL C — 不做推断，依赖 BCL 的 `ThrowIfNull` 家族与显式 `Throw New`。** `.NET 6` 起 BCL 已提供 `ArgumentNullException.ThrowIfNull(param)`、`ArgumentOutOfRangeException.ThrowIfNegative(value, paramName)`、`ArgumentException.ThrowIfNullOrEmpty(str)` 等静态方法，显式、参数名正确、消息由 BCL 本地化。`Probably`：这些 API 是真实存在的（一般知识，非本仓库可核实）。

**PROPOSAL D — 具名快捷方式 `Throw ArgumentNullException(name)`，无推断。** 去掉 `New`、类型显式、参数名显式；条件仍由 `If` 承担。不解决消息问题，但也没有映射魔法。

### 权衡：Q&A

- **A 的映射表站得住吗？** 站不住。同一个条件形态可以被合理地读成多种异常：`IsClosed` 既像 `InvalidOperationException` 又像 `ObjectDisposedException`；`count < 0` 可以是 `ArgumentOutOfRangeException`、`OverflowException` 或 `ArgumentException`；`String.IsNullOrEmpty(s)` 是 `ArgumentNullException` 还是 `ArgumentException`？——空字符串并不是 null。不同程序员对同一条件的意图分布是我们无法在语言层面赌的。**结论：A 的地盘整体放弃。**
- **A 的"哪个操作数有罪"问题。** `If a < b Then Throw`——culprit 是 `a` 还是 `b`？`If GetCount() < 0 Then Throw`——`paramName` 对一个函数调用毫无意义。映射表连"把谁的名字放进异常"都答不齐，遑论消息。这进一步压死 A。
- **B 为什么反而可以谈？** 因为 `Is Nothing` 形态的歧义度最低：被测试的表达式就是 culprit，异常类型几乎唯一（`ArgumentNullException`），`paramName` 就是变量名。它没有映射表，只有一条规则，读者不需要查表。`Not all of us are happy with` 即使这一条——见下文"更简替代"——但它是四个方案里唯一没有语法魔法与语义魔法的。
- **B vs C：`If name Is Nothing Then Throw` 比 `ArgumentNullException.ThrowIfNull(name)` 好在哪？** 我们坦诚地说：好在一个句子更像英语，仅此而已。C 是显式的、消息正确、零新语法、零新语义。B 的收益是"少打 20 个字符 + 更像英语"，代价是让 `Throw` 多出一个依赖上下文的含义。这个交换不显然划算。**这是本场最尖锐的一问，B 因此被压到 Table。**
- **D 值不值得独立做？** D 去掉 `New` 是新的语法面，`Throw SomeType(args)` 与既有的"`Throw` 表达式"形态需要在文法层面区分"裸类型名 + 调用"与"表达式"。它保留了显式性、丢了推断价值，收益不如 C，复杂度却接近新语法。**结论：D 不做。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`If cond Then Throw` 在今天**已经能解析**。规范（`..\..\vblang\spec\statements.md`）给出的文法逐字是：

```
ThrowStatement
    : 'Throw' Expression? StatementTerminator
    ;
```

`Expression?` 可省略，所以裸 `Throw` 在单行 `If`、块 `If` 里都能过语法关。真正的变化全在 binder。但这里有一处**必须先钉死的文法语义**：规范同样逐字写明——

> A `Throw` statement may omit the expression within a catch block of a `Try` statement, as long as there is no intervening finally block. In that case, the statement rethrows the exception currently being handled within the catch block.

也就是说，**裸 `Throw` 在 `Catch` 里已有定义（rethrow）**，规范甚至用 `If x = 0 Then Throw` 做示例。任何推断方案都不能改动这一点。于是"裸 `Throw` 的含义"变成上下文相关：

```vb
Try
    Throw New Exception()
Catch
    If x = 0 Then Throw    ' 今天：rethrow。绝不能变成"推断"。
End Try

If name Is Nothing Then Throw   ' 今天：编译错误。建议把它变成"推断"。
```

规则只能是"**推断只发生在裸 `Throw` 当前是错误的地方**（`Catch` 且无夹层 `Finally` 之外）"。这条规则可表述、可实现，但它意味着同一段文本在不同嵌套语境下含义不同——这正是我们最警惕的"隐蔽语义变化"，只是幸运的是兼容性安全。

#### 2. 角案例与边界语义

**双重求值。** `If count < 0 Then Throw` 若落地为 `ArgumentOutOfRangeException(NameOf(count), count)`，`count` 被求值两次。若 `count` 是带副作用的属性，行为与显式写法不同：

```vb
Property Count As Integer
    Get
        Console.WriteLine("getter called")   ' 会打两遍？
        Return _count
    End Get
End Property

If Count < 0 Then Throw
```

编译器要么先把 culprit 存入临时变量（改变求值顺序的可见度），要么接受双重求值（运行时行为差异），要么只传 `paramName` 不带 `actualValue`（消息变弱）。三条路都有味道。`Probably`：`ArgumentOutOfRangeException` 的 `(paramName, actualValue, message)` 重载是真实存在的。

**布尔状态的映射不可用。** `If IsClosed Then Throw` 若要映射到 `ObjectDisposedException`，该异常的构造函数需要 `objectName`——编译器无法有意义地合成一个被处置对象的名字。而映射到 `InvalidOperationException` 又是赌意图。**结论：布尔形态根本不该进映射表。**

**复合条件。** `If name Is Nothing OrElse name.Length = 0 Then Throw`——一条语句两个条件、两种异常候选。任何"单条件→单异常"的规则都覆盖不了，而 A 的映射表对此没有答案。

**`TypeOf` 守卫。** 类型流分析建议把 `Throw` 列为守卫语句（`If TypeOf x IsNot T Then Return` 同样适用 `Throw`）。若本建议成立，`If TypeOf manager IsNot IVsTextManager Then Throw` 会撞上"`TypeOf ... IsNot` 形态无映射"——必须保持显式 `Throw New ...`。两建议要在这类交叉点上互相声明，不能让读者猜。

**`Throw` 单句不成行。** 裸 `Throw` 独立成句（不在 `If` 内）没有条件可推断，仍是错误。这没问题，但说明"推断"严格绑定在 `If ... Then Throw` 形态上，形态面很窄。

#### 3. 作用域与绑定

推断构造出的异常表达式绑定到 BCL 类型（`System.ArgumentNullException` 等），语义模型里应能看到一条"合成的 `Throw New`"对应的完整构造。`paramName` 若取变量名，它绑定到**词法名**而非符号——与 `NameOf` 的语义一致（重命名安全）。若取 `Me.SomeProperty` 这类成员名，则绑定到成员符号。v1（若做）应只对简单变量/参数生效，避免"property 还是 backing field"的纠缠——与类型流分析的 v1 取舍一致。

#### 4. 与既有特性的交互

- **rethrow（最重要的交互）**：见第 1 条。`Catch` 内的裸 `Throw` 语义必须原样保留，推断规则严格限定于"当前为错误"的语境。
- **`Null` 字面量建议（依赖陷阱）**：建议原文的旗舰示例是 `If name Is Null Then Throw`，用的是 ModVB 的 `Null` 字面量，不是 VB 的 `Nothing`。而主线在 2014 年就拒过 `Null` 字面量——`LDM-2014-02-17.md` 记录逐字为 "* Rejected on 2014-01-06. It would have had parity with C# 'inference is aware of null' feature. *"。**这意味着建议的主示例依赖一个独立且被主线拒绝过的特性。** 若想落地，`Is Nothing` 形态必须等价可用，并明确与 `Null` 建议解耦。
- **守卫语句 / 类型流分析**：见第 2 条交叉点。
- **`Guarded Let`（inactive）**：守卫式 `Let v = e Else Throw` 若允许 `Throw` 作控制流语句，同样面临"抛什么"的问题；但它的 null 触发条件与 B 的 `Is Nothing` 形态同构，理论上可共用一条规则。`Suspect`：两建议都未定稿，合并不在近期。
- **`Case Else` 变量（inactive）**：`Case Else other : Throw New UnexpectedValueException(other)` 是显式抛，不需要推断，也不与本文冲突。
- **CallerInfo / 表达式树**：无交互。表达式树里不能有裸 `Throw`，推断也不应扩展到表达式。

#### 5. Breaking change 与兼容性

这是本建议最幸运也最需要精确的地方。因为裸 `Throw` 在 `Catch` 之外今天**是编译错误**，把它变成"推断抛出"不会破坏任何既有代码——错误变成特性是最安全的兼容形态。但唯一且致命的例外是 `Catch` 内：那里裸 `Throw` 是 rethrow，任何"把所有 `If ... Then Throw` 都推断"的天真实现都会静默改变既有 rethrow 的运行时行为。**规则必须上下文相关，且必须写进 spec 并配测试。** 建议文档完全没提这一点。

#### 6. Option Strict / 编译选项分叉

`If name Is Nothing Then Throw` 在 `Option Strict Off` 下同样合法（`Is Nothing` 测试不依赖静态类型）。推断规则在两路径下应产生**相同**的异常类型与参数名——`Option Strict` 不改变运行时抛错。这是硬要求，实现上无分叉理由。

#### 7. IDE / IntelliSense

补全无新增内容（没有新成员）。值得做的是：
- InfoTip/错误文案：在裸 `Throw` 处提示"将推断为 `ArgumentNullException(NameOf(name))`"，把魔法显式化。
- 快速操作：一键展开为显式 `Throw New ...`；反向（若采纳 B）一键折叠。
- 重构：把 `If name Is Nothing Then Throw New ArgumentNullException(NameOf(name))` 折叠成推断形式——但反过来想，这个重构的存在本身就是"显式信息在丢失"的证据。

#### 8. 数据 / 普遍性

参数校验是真实的高频样板，这一点没有争议。但建议没有给出任何量化数据——没有"xx% 的 `If ... Then Throw New ...` 属于这三种形态"的统计。而 `Implicit-default-optional` 这类成功建议通常有 85% 式占比背书。我们 `Suspect`：`Is Nothing`→`ArgumentNullException` 可能在参数校验里占比很高，但 `IsClosed`→`InvalidOperationException` 这类布尔形态占比低且意图分散。没有数据，普遍性只算"感觉得到"。

#### 9. 更简替代

这是压倒性的一问。`.NET 6` 的 `ArgumentNullException.ThrowIfNull(param)` 一族已经覆盖了 A/B 想解决的最热门情形：

```vb
' 显式、参数名正确、消息由 BCL 本地化、零新语法、零新语义：
ArgumentNullException.ThrowIfNull(name)
ArgumentOutOfRangeException.ThrowIfNegative(count, NameOf(count))

' 与推断形式对比：
If name Is Nothing Then Throw
If count < 0 Then Throw
```

前者甚至更短、更确定。推断形式的唯一优势是"像英语"。**一个纯语言特性，对手是 BCL 的三个静态方法，而且 BCL 方案没有新语法。** 按照"更简单替代"标准，这是我们近年来见过的最强竞争者之一。Analyzer 也能做"把 `If ... Then Throw New ...` 建议改成 `ThrowIfNull`"的脚手架，但同样只能当向导，不能替代语言特性。

#### 10. 成本 / 优先级

若只做 B（一条规则），编译器成本很小：在 binder 对"`If` 内裸 `Throw`（且当前为错误语境）"识别 `Is Nothing` 形态并合成构造即可。但成本小不等于该做——收益同样小。若做 A，映射表、消息本地化、双重求值治理、复合条件、culprit 判定全部要填，成本陡增而价值不增（A 已被否决）。优先级上：与类型流分析、可空性流分析、模式匹配相比，这远不是头条。若做，也只做 B，且排在其它流分析之后。

#### 11. 运行时 / CLR 硬约束

无硬约束。推断只是把一句"当前报错"的代码合法化为一条 `newobj` + `throw`，不触达 CLR 存储规则，PEVerify 无碍。真正的约束在 BCL 一侧：被选中的异常构造函数必须在目标框架上存在且能由编译器有意义地传参——这正是 `ObjectDisposedException` 出局的原因。

#### 12. 值不值得做

逐维打分。**价值**：样板消除是真的，但 BCL `ThrowIfNull` 已抹平大半，剩余价值是"英语可读性"，小。**成本**：B 很小，A 很大。**风险**：`Catch` 内 rethrow 必须钉死（可控），`Null` 字面量依赖必须解除（可控），但"同一文本在不同语境含义不同"的长期认知成本不可控。结论：**值得做只限 B，且证据不足——B 本身也被压到 Table。** 若只做 A 而拒绝收缩，我们直接建议不做。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：`If name Is Nothing Then Throw` 是全文案唯一的光——一个从未写过 VB 的人也能读出意思。
- **消除常见样板（原则 #9）**：方向正确，但见第 9 条——BCL 已解决 80%，语言特性的增量有限。
- **不引入"第二种做事方式"（原则 #3）**：**扣分最重的一项**。今天抛异常只有一条路（`Throw New ...`）。推断给出第二条路，且这条路的语义是"查表猜"。`LDM-2018.02.07` 对 `Option Infer` 的判断我们感同身受：推断若只是把编译器手头已有的信息填上（如 #197 的 `Set` 参数类型，Approved-in-Principle，因为"we already have the type of the property in hand"），那是填充；推断若要求读者查映射表，那是魔法。
- **避免隐蔽的控制流/语义变化（原则 #7）**：**再次扣分**。`If x Then Throw` 在 `Catch` 内外的含义不同，正是这一类。
- **默认跟随 C#（原则 #4）**：C# 没有条件形态推断（C# 有 `throw` 表达式与 `ThrowIfNull`，但没有"从条件猜异常"）。主线的推断先例——2014 年 `#15 Type inference for constructors of generic types` *Approved. Aligns with C# vNext feature*——恰恰说明主线认可的是"跟随 C# 的推断"，而不是"背离 C# 的魔法"。
- **与主线关系（对照表 2.3）**：Anthony 独立延伸，主线无对应工作；所依赖的 `Null` 字面量是主线 2014 年拒绝的激进项。方向上属于"主线保守、Anthony 激进"一栏。

### RESOLUTION:

1. **否决 PROPOSAL A（完整条件形态→异常映射）**：映射表是隐藏魔法，同一形态可被合理读成多种异常，culprit 判定、双重求值、复合条件均无解。它同时违反原则 #3 与 #7，且没有 C# 或 VB 历史先例背书。
2. **布尔形态与复合条件不进任何映射**：`If IsClosed Then Throw` 这类形态的意图分布无法在语言层面赌；`ObjectDisposedException` 因构造函数无法由编译器有意义地合成而天然出局。
3. **PROPOSAL B（`Is Nothing` → `ArgumentNullException` 窄种子）压到 Table**：它是四个方案里唯一零映射表、歧义度最低的种子，但收益薄于 BCL `ThrowIfNull`，须先补数据与设计再议。
4. **rethrow 红线**：任何形态的推断都**不得**触碰 `Catch` 内裸 `Throw` 的既有 rethrow 语义（规范 `statements.md` 明文）。推断只允许发生在裸 `Throw` 当前是编译错误的语境。若 B 复活，这条必须写进 spec 并配测试。
5. **与 `Null` 字面量建议解耦**：`Is Nothing` 形态必须与 `Is Null` 等价可用；本特性不得把旗舰示例押在主线已拒、ModVB 未定的 `Null` 字面量上。
6. **不引入 `Throw SomeType(args)` 快捷语法（PROPOSAL D）**：新语法面、收益不如 BCL、与既有 `Throw` 表达式形态需文法区分。
7. **与类型流分析在 `TypeOf` 守卫处划界**：`If TypeOf x IsNot T Then Throw` 保持显式，本建议不提供该形态的映射。

### Implication:

- 若 B 复活，先写一份窄 speclet：一条规则、只认简单变量/参数、`paramName`=词法名、`Catch` 上下文规则、求值一次（不传 `actualValue` 或先存 temp）、消息由 BCL 参数化构造生成（不新开编译器本地化面）。
- 与 BCL `ThrowIfNull` 做一次并排对比：同一校验用三种写法（显式 / `ThrowIfNull` / 推断），量化行数、字符数与可读性差异，作为 Table 复活的门票。
- 补数据：真实代码库中 `If ... Then Throw New ...` 的形态分布统计（`Is Nothing` 占比、`< 0` 占比、布尔形态占比）。
- 把"裸 `Throw` 语法已合法、语义当前为错误"这一事实写进对外的设计备忘，避免后续特性把同一语法面占掉。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：若 B 复活，`If name Is Nothing Then Throw` 的 `ArgumentNullException` 消息是否含变量名——两条路：BCL 参数化构造自动生成（推荐，本地化免费），或编译器自写消息（需新开本地化面，否决倾向）。
- `OPEN QUESTIONS`：B 是否允许 `Me` 前缀（`If Me._x Is Nothing Then Throw`）与字段，还是只允许参数/局部变量（倾向后者，与类型流分析 v1 一致）。
- `TODO`：量化 `Is Nothing`→`ArgumentNullException` 在参数校验中的真实占比。
- `Follow-up`：与 `Guarded Let`（inactive）对齐"null 触发 → 抛什么"的共用规则，仅当后者复活时。
- `Suspect`：`String.IsNullOrEmpty(s)` 的映射归属（`ArgumentNullException` vs `ArgumentException`）在原文未定；即使未来扩展映射表，我们倾向**不扩展**。

### 状态

- **LDM 状态：LDM Rejected（完整映射）/ LDM Considering（`Is Nothing` 窄种子，Table）**。
- **三态判定：Table** — 完整推断方案 Reject；窄种子 B 有讨论价值但证据与收益均不足，待 BCL 对比与数据补齐后再议。

---

## 附录：特性评价

# 建议评价报告：proposal-throw-inference.md

## 评价对象

- 建议：proposal-throw-inference.md — `Throw` 异常类型推断
- 来源：Anthony 原文第 3.8 节 "Throw"（`..\AnthonyDesign_wordpress.txt` L1009–1022），示例逐字一致；`If n < 0 Then Throw`（L225）、`If value < 0 Then Throw`（L458）、`If input In bannedWords Then Throw`（L1633）、`If value = Guid.Empty Then Throw`（L2547）表明该特性是 Anthony 全文反复假设的基础设施
- 配方目标：`If ... Then Throw` 依据条件形态推断异常类型，把参数校验/状态校验从显式 `Throw New ...Exception(...)` 简化为一行

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 3（只覆盖部分场景，主效果显现但关键子效果缺失）以下：动机明确、示例可操作，但核心映射表全部悬置在未决问题里，主示例 `Is Null` 依赖被主线拒绝的 `Null` 字面量，且主力场景已被 BCL `ThrowIfNull` 覆盖——改进可衡量性差 | 已提供 / 已检查 | 无原型封顶；无量化数据；`IsClosed`→`InvalidOperationException` 的意图赌注成立与否未论证 |
| 特性 | 2/5 | 锚点 2（外来特性直接照搬未 VB 化 / 多个强无关能力捆绑）偏下：语法表面是 VB 原生成分（`If...Then`+`Throw`），但语义是查表猜异常，违反原则 #7；引入"第二种抛异常方式"违反 #3；偏离 C# 默认 #4；无 VB6/主线血缘（VB6 是 `Err.Raise` 显式编号） | 已检查 | 表面与语义分离——"读起来像 VB" 与 "语义是魔法" 同时成立，评价取其重 |
| 品质 | 3/5 | 锚点 3（缺某一章节或关键处边界含糊；未决问题被轻描淡写）：六章节齐全、示例与 Anthony 3.8 逐字一致、3 个未决问题具体（健康区间），但核心设计（映射表）整体搁进未决；无 breaking-change/兼容性分析，且对 rethrow（裸 `Throw` 在 `Catch` 内已定义）这一最关键的文法交互只字未提 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）为红旗；映射表不完整等于详细设计未定型 |
| 属性 | 2/5 | 锚点 3 以下：光（消样板）与雷（提速）有正向，但暗风险突出——运行期抛错类型猜错、依赖主线已拒的 `Null` 字面量、语义上下文相关（`Catch` 内外含义不同）、与 C# 同质化方向上反而更特立独行；文档未权衡 | 已检查（预测待定） | 风=背离 C# 且无充分理由；暗=意图误判直接变成运行期错误类型；实际影响须"已采纳"后定 |
| 炼金成分 | 2/5 | 锚点 3（部分来源未标注；标注与影响有偏差）：语法来源（Anthony 3.8）标注准确，但未标注 `Null` 字面量依赖（主线 2014 拒绝项）、未声明对 C# 默认的偏离、未点明 BCL `ThrowIfNull` 这一竞争成分；成分混杂（推断 + `Null` 字面量 + 消息生成）未拆分说明 | 已检查 | 主成分可溯源，但关键依赖与竞争成分未标注，影响预估与实际偏差 |

## 设计原则对照

- **与 VB 基因：偏离**（读起来像英语 #5 与消样板 #9 达标，但违反 #3"不引入第二种做事方式"、#7"避免隐蔽语义变化"、#4"默认跟随 C#"；`Catch` 内外语义上下文相关是隐蔽变化的直接实例）。
- **与主线关系：Anthony 独立延伸**，主线无对应工作项；所依赖的 `Null` 字面量为主线 2014 年拒绝（`LDM-2014-02-17.md` "* Rejected on 2014-01-06 ... *"）。与类型流分析（守卫含 `Throw`）、`Guarded Let`、`In/NotIn` 运算符交叉但均未协调。
- **破坏性变更：潜在有**——`Catch` 内 `If x Then Throw` 的 rethrow 语义若被天真推断覆盖即破坏既有行为；文档未分析。正确限定（推断只在"当前为错误"的语境）后兼容性安全。

## 总评

- **达成程度：未达成**——概念（消样板）真实，但核心机制（条件形态→异常映射）是隐藏魔法，主力场景已被 BCL `ThrowIfNull` 覆盖，旗舰示例依赖独立且被主线拒绝的 `Null` 字面量，文档对 rethrow 这一最关键交互毫无分析。
- **LDM 三态建议：Table**（完整映射 Reject；`Is Nothing`→`ArgumentNullException` 窄种子保留讨论价值，待数据与 BCL 对比补齐）。
- **主要问题**：① 映射表的歧义（同形态多异常）与 culprit 判定无解；② `IsClosed` 等布尔形态的意图赌注；③ `Catch` 内 rethrow 语义未分析（最关键）；④ `Null` 字面量依赖未声明且该特性被主线拒绝；⑤ 未量化与 BCL `ThrowIfNull` 的差距。

## 返工建议

- **补充章节**：Breaking change / 兼容性（`Catch` 内 rethrow 的语境规则、`langversion` 门控）；文法说明（`ThrowStatement : 'Throw' Expression? StatementTerminator` 与 binder 变化的分界）；求值顺序（双重求值治理）；消息本地化策略（推荐 BCL 参数化构造，不新开本地化面）；Option Strict 分叉（要求两路径一致）。
- **补充证据**：形态分布数据（`Is Nothing` / `< 0` / 布尔占参数校验比例）；与 `ArgumentNullException.ThrowIfNull` / `ThrowIfNegative` 的并排对比；最小原型（仅 `Is Nothing` 形态）。
- **未决问题处理**：映射表要么大幅收缩为单条规则（B），要么整体放弃（A）；`String.IsNullOrEmpty`、复合条件、`TypeOf` 守卫一律明确不映射；`Is Nothing` 与 `Is Null` 解耦并明确等价。
- **设计探索**：与 `Guarded Let` 共用"null 触发→抛什么"规则（仅当其复活）；把"裸 `Throw` 语法已合法、语义当前为错误"写成设计备忘，保护该语法面不被其它特性占用。

---

## 附录：C# 生态与互操作考量

> **范围说明**：本提案（`Throw` 异常类型推断）是**语句层样板糖**，不触达 C# interop 主线（Span/ref/unsafe/COM/AOT/表达式树，见索引 T2/T3/T5）。与 C# 生态最实质的交汇点只有两处：**C# 7 `throw` 表达式**（把 `throw` 送进表达式位置的先例）与 **C# 已拒的参数 null 校验 `!!`**（与本提案 B 同构的"参数校验语法化"）。以下围绕这两条路展开，其余方向如实说明为"弱相关"，不硬凑。

### 相关 C# 现实方向

**1. C# 7 `throw` 表达式——C# 对"throw 能出现在哪"的唯一扩展。** `proposals\csharp-7.0\throw-expression.md` 把 `throw` 扩展进表达式，类型规则逐字是：

> A *throw_expression* has no type.
> A *throw_expression* is convertible to every type by an implicit conversion.

且限定上下文（原文逐字）："A *throw expression* is permitted in only the following syntactic contexts: As the second or third operand of a ternary conditional operator `?:`; As the second operand of a null coalescing operator `??`; As the body of an expression-bodied lambda or method."（→ `proposals\csharp-7.0\throw-expression.md`）。流程分析规则逐字（同文件）："For every variable *v*, *v* is definitely assigned after *throw_expression*." LDM 定稿结论逐字（→ `meetings\2016\LDM-2016-10-18.md`）："They are allowed as expression bodies, as the second operand of `??`, and as the second and third operand of `?:`. They are not allowed in `&&` and `||`, and cannot be parenthesized. We think this is a fine place to land." **关键：C# 让 `throw` 进表达式，但异常类型永远显式——从未做"从条件猜异常"。**

**2. C# 参数 null 校验 `!!`——本提案最直接的 C# 对应物，已拒。** `proposals\rejected\param-nullchecking.md`（champion issue #2145）动机逐字："Given that NRT doesn't affect code execution developers still must add `if (arg is null) throw` boiler plate code even in projects which are fully `null` clean." 其翻译目标与本提案 B 完全同构：

```csharp
if (name is null) {
    throw new ArgumentNullException(nameof(name));
}
```

即"`is null` → `ArgumentNullException(paramName)`"，与 VB 的 `If name Is Nothing Then Throw` → `ArgumentNullException(NameOf(name))` 是同一张靶。该特性最终被移除，结论逐字（→ `meetings\2022\LDM-2022-04-13.md`）："Parameter null checking is removed from C# 11." 移除理由包括 LDT 自身分裂与社区反馈非大比例正面（同文件逐字）："We do see a majority of positive reactions to the feature, but it isn't the large majority we normally like to see for C# features, particularly ones that are as broadly applicable as this feature will be, and even the LDT is split on the feature itself."

**3. C# 侧的"更简替代"正是 BCL `ThrowIfNull`。** `!!` 提案原文逐字允许用 BCL 助手实现（→ `proposals\rejected\param-nullchecking.md`）："the implementation may use helper methods to perform the null check a la the [ArgumentNullException.ThrowIfNull](…)"；更早的 triage 已把"做成方法调用而非新语法"列为候选（→ `meetings\2019\LDM-2019-09-16.md`）："We could potentially use this to implement 'Null parameter checking' as a method call, instead of new syntax. Thus, consider for 9.0." 这与本场把 PROPOSAL C（BCL `ThrowIfNull` 家族）视为最强竞争者完全同向。

**4. AOT/trimming + source-gen 背景（索引 T5/T6）。** C# 生态正把"样板/反射/动态"压向"编译期生成 + 类型系统承担职责"。对参数校验，C# 的落点不是语言语法，而是**带元数据属性的 BCL 静态方法** + 静态分析——这决定了下文"需桥接"的具体内容。

### 现实 vs 提案

- **兼容（CLR 异常类型层）**：无论走 VB 推断（`Throw New ArgumentNullException(NameOf(name))`）、BCL（`ArgumentNullException.ThrowIfNull(name)`）还是 C# 显式（`throw new ArgumentNullException(nameof(name))`），抛出的都是同一 CLR 类型、同一 `paramName`、同一构造参数形状。跨语言调用者 catch 行为一致；无新 IL、无新元数据（本场第 11 条"运行时/CLR 硬约束：无"成立）。
- **需桥接 ① throw 表达式语义对齐**：VB 只做语句层推断，则与 C# throw 表达式**无冲突**；但若 VB 未来把 `Throw` 送进表达式（如 `x ?? Throw`），应逐条照抄 C# 规则——「no type」+「convertible to every type」（让 `??` 另一侧定类型）、限定上下文（`?:`/`??`/表达式体，排除 `&&`/`||`）、求值结果为 `null` 时抛 `System.NullReferenceException`（→ `proposals\csharp-7.0\throw-expression.md`）。注意 C# LDM 在 `!!` 语法讨论中评价过 `string s ?? throw` 形态（→ `meetings\2022\LDM-2022-04-13.md` 逐字）："this feels pretty intuitive, as `?? throw` is already legal expression syntax (provided there's an exception type on the other side)"——即 C# 生态只接受"throw 表达式 + **已有**异常类型"，从不接受"推断异常类型"。
- **需桥接 ② BCL throw helpers 的元数据识别**：本场把 `Throw` 列为守卫语句（类型流分析）。若 VBScript.NET 想让走 BCL 路线的用户得到同等流收益，编译器必须**认识 `ArgumentNullException.ThrowIfNull` 等 BCL 方法上的 `DoesNotReturnIf` 类属性**并据此做流分析，否则"推断 Throw"与"BCL 调用"两条路线的流行为分叉。这与决策文件 M8 的通用桥接点（VB 编译器必须识别新元数据属性）同型。`Suspect`：这些 BCL 属性属 dotnet/runtime，本仓库（csharplang）无可核实正文（见文末 OPEN QUESTIONS）。
- **冲突：无硬冲突；有一个认知张力**。C# 的 `!!` 被移除后，C# 生态对"参数校验样板"没有语言级捷径；VB 若推出 `If name Is Nothing Then Throw` 推断，会形成"VB 有、C# 没有"的语法糖差。方向尚可容忍——C# unsafe-evolution 已逐字明确表态 VB 不必镜像 C# 特性（→ `proposals\unsafe-evolution.md`，VB 小节）："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."——反向同样成立，VB 可保有自己的语法面。但 C# 的移除理由（LDT 分裂、社区反馈非大比例正面、语法"shouty"）对本提案部分适用：C# 对"样板语法化"整体审慎，VB 更应保守，与本场结论方向一致。
- **脱节（如实说明）**：除上述两条路外，本提案与 C#/CLR/.NET 互操作主线（Span/ref/unsafe/COM/AOT/表达式树）无实质关系。它不产生新元数据、不改变内存/存储规则、不进表达式树（本场第 4 条）。若只做 B（一条规则），其 C# 生态足迹几乎为零——这正是它"冲突少但先例也少"的原因。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：本特性属"默认安全"面（合成的是强类型 `ArgumentNullException`，非晚期绑定），与 AOT/trimming 不冲突；不应把它与 `Any`/晚期绑定（决策文件 M2）混在同一"动态"通道。
- **与 BCL throw helpers 双轨共存**：C# 的先例是"BCL 方法 + 静态分析"而非语法。VB 若采纳 B，需与 BCL 路线**行为等价**（同异常类型、同 paramName、消息走 BCL 参数化构造、不传 `actualValue` 以规避双重求值），并让编译器**识别 BCL 方法的 `DoesNotReturnIf` 类元数据**，使两条路线在流分析上等价（需桥接 ②）。这比"推断语法"本身更优先。C# 侧对"求值/实现差异"的容忍可作参照（→ `proposals\rejected\param-nullchecking.md` 逐字）："This could result in observable differences between different compliant implementations, such as whether calls to helper methods are present above the call to the method with the null-checked parameter in the exception stack trace."
- **`Is Nothing` 范围是 C# 先例认可的窄面**：`!!` 只做 `is null` 参考相等测试，不调用 `==`（→ `proposals\rejected\param-nullchecking.md` 逐字）："The check will be specifically for reference equality to `null`, it does not invoke `==` or any user defined operators."——这与 B 的 `Is Nothing` 形态同构，从构造上规避了双重求值与运算符重载问题。B 若复活，应守住"只对简单变量/参数做 `Is Nothing`"。
- **throw 表达式留白**：现阶段不做表达式层 `Throw`；若未来做，照 C# throw-expression 规则集逐条对齐（类型规则/上下文/`null`→NRE），避免"VB 表达式 Throw"与"C# 表达式 throw"语义漂移。
- **source-gen 桥（索引 T6）**：C# 用 source generator 把反射/样板移向编译期。对 VB，`If ... Then Throw` 的展开/折叠可做成 **IDE 快速操作 + analyzer 脚手架**（本场第 7 条已列）——但这只是向导，不能替代语言特性。C# 的教训是：这类样板最好留给 BCL + 静态分析，而非语法糖（`!!` 移除的直接原因之一）。

### 对既有 RESOLUTION / 三态判定的影响

- **无变化，反而加强**。C# 把与本提案 B 同构的"`is null` → `ArgumentNullException` 语法化"在 C# 11 移除，理由含 LDT 分裂与社区反馈非大比例正面（→ `meetings\2022\LDM-2022-04-13.md`）。这是"样板语法化需极高证据门槛"的直接先例，支撑本场 A 否决、B 压 Table 的取向。
- **A（映射表）再遭否定**：C# 对参数校验连"显式异常类型的语法糖"都放弃，做"猜异常"的 A 在 C# 生态方向上无任何先例；`?? throw` 讨论只走到"已有异常类型"为止（→ `meetings\2022\LDM-2022-04-13.md`）。
- **B 若复活，可用 `!!` 作负面对照**：C# 的候选语法（`!!`/`!`/`notnull`/`?? throw`/`checked`）各有反对理由（→ `meetings\2022\LDM-2022-04-13.md`）；VB 的 `If...Then Throw` 是语句形态、无 C# 对应语法包袱，但也无 C# 背书——B 的论证只能靠"数据 + BCL 对比"自己挣，不能借 C#。
- **对 rethrow 红线的 C# 佐证**：C# 从未让裸 `throw;` 有 rethrow 之外的第二种含义——裸 `throw` 在 C# 里始终是"仅 rethrow"。本场第 1/4 条给 VB 裸 `Throw` 增加"推断"含义，是 C# 刻意回避的上下文相关语义；RESOLUTION 第 4 条（rethrow 红线）在 C# 视角下是**必须且充分**的护栏。

### 引用纪律与未决问题

- 本附录引用的 C# 原文均已在 `..\..\csharplang` 逐字核实，出处随文标注；可逐字引用原文清单：
  - `proposals\csharp-7.0\throw-expression.md`：类型规则两句、"convertible to every type"、`null`→`NullReferenceException`、上下文三条、流程分析"definitely assigned after *throw_expression*"。
  - `meetings\2016\LDM-2016-10-18.md`："They are allowed as expression bodies, as the second operand of `??`, and as the second and third operand of `?:`. … fine place to land."
  - `proposals\rejected\param-nullchecking.md`：动机句、`if (name is null) { throw new ArgumentNullException(nameof(name)); }`、"a la the ArgumentNullException.ThrowIfNull"、"reference equality to `null`"句、可观测实现差异句。
  - `meetings\2019\LDM-2019-09-16.md`："We could potentially use this to implement 'Null parameter checking' as a method call, instead of new syntax."
  - `meetings\2022\LDM-2022-04-06.md`："around 9 out of 10 preconditions are null checking"；"the bulk of validation code by far is argument null checks"。
  - `meetings\2022\LDM-2022-04-13.md`："Parameter null checking is removed from C# 11."；"pulling the feature is the right move"；"`?? throw` is already legal expression syntax"。
  - `proposals\unsafe-evolution.md`（VB 小节）："We do not need to add support to Visual Basic for *requires-unsafe* members …"（索引第四节已核实）。
- `OPEN QUESTIONS`：
  - `ArgumentNullException.ThrowIfNull` / `ArgumentOutOfRangeException.ThrowIfNegative` / `ArgumentException.ThrowIfNullOrEmpty` 的**方法签名与 `DoesNotReturnIf` 类属性**属 dotnet/runtime，本仓库（csharplang）无正文可核实（meeting 正文已标 `Probably`）。建议后续在 dotnet/runtime 核实后再把"需桥接 ②"细化成具体属性清单。
  - C# `!!` 被拒的完整原因树：除本附录引用外，`meetings\2022\LDM-2022-04-06.md` / `LDM-2022-04-13.md` 还有针对 NRT 运行时语义、contracts 路线等的长讨论，本附录只取与 VB 相关的论断，未穷尽。
  - `proposals\rejected\param-nullchecking.md` 是否可视为 C# 官方对"参数校验样板语法化"的最终定论：文件在 `rejected\`，但 `meetings\2020\LDM-2020-09-28.md` 曾有 "We have an implementation of this mostly ready. … Let's get it in." 的乐观记录——文档状态与最终移除的时序关系未在本仓库内追溯，待考。
