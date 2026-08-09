# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周处理循环语句现代化批次的最后一份：`Do` 循环头声明/赋值。它与同批的 `For` 增强（多计数器）、`For Each` 增强共享"把循环语句头部变得更会说话"这一主题；同时，Anthony 第 3 章的块头声明家族（`With` / `Try` / `Using` / `Do` 的头部分句）让本次讨论必须回答一个家族级问题，而不只是 `Do` 一个问题。

## Agenda

* [Proposal: Do 循环头声明/赋值（Do Loop Header Declarations）](#proposal-do-循环头声明赋值)

## Proposal: Do 循环头声明/赋值

_Related: [vblang #48 – Multiple `For` / `For Each` Control Variables Per Statement](https://github.com/dotnet/vblang/issues/48)；[vblang #186 – `Exit For j`](https://github.com/dotnet/vblang/issues/186)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；ModVB：`proposal-for-enhancements.md`、`proposal-with-enhancements.md`、`proposal-try-enhancements.md`、`proposal-using-synclock-enhancements.md`、`proposal-nullability-flow-analysis.md`、`proposal-null-literal.md`_

### 场景与缺口

We started from a loop shape every VB 程序员都写过，且大多写得不耐烦——逐行/逐块读取：

```vb
' 今天：'读取→处理→再读取'要写成两处读取 + 一次预读。
Dim line As String = Console.ReadLine()
Do While line IsNot Nothing
    ProcessLine(line)
    line = Console.ReadLine()
Loop
```

同一个 `Console.ReadLine()` 出现两次，初始化读取与循环体末尾读取是同一件事的副本。更糟的退路是 `While True` + 循环体末尾读取 + `Exit Do`，样板换成了控制流跳跃。C# 用 `while ((line = reader.ReadLine()) != null)` 一句话写完这个惯用法，VB 做不到——VB 的赋值是语句不是表达式，条件里不允许副作用。

Anthony §3.6 `Do`（`..\AnthonyDesign_wordpress.txt` L963–972）把目标形态写在两行注释与两个示例里，原文逐字为：

```vb
' Variable declaration/assignment in header BEFORE condition is evaluated.
Do line = Console.ReadLine() Until line Is Null
    ...

Do bytesRead = stream.ReadAsync(buffer, 0, buffer.Length) While bytesRead > -1
    ...
```

We see a clear, if modest, parity gap: 循环头"先取再判"的惯用法在 C# 里是母语，在 VB 里是两行样板。建议要做的就是把"赋值在条件求值之前执行、变量在循环体内可用"写成一条 `Do` 语句。

但场景介绍到这里，We 立刻注意到两处需要较真的地方：第一个示例用了 `Null`（Anthony 的 `Null` 字面量建议，主线段在 2014 年已拒绝，见下）；第二个示例的 `bytesRead` 是 `stream.ReadAsync(...)` 的 `Task(Of Integer)`，而 `bytesRead > -1` 要求它是数值——原文示例**按字面不可编译**。这两处都在未决问题里被承认了一半，但讨论必须把它们顶到台面上。

### 候选方案

**PROPOSAL A — 纯赋值子句（Anthony 原文形态）。** 头部分句只允许"对既有变量的赋值"：

```vb
Dim line As String
Do line = Console.ReadLine() Until line Is Nothing
    ProcessLine(line)
Loop
```

`line` 必须在进入循环前已声明（Option Explicit On）；每次迭代先执行赋值，再求值条件。条件读的是**变量 `line`**，不是赋值的返回值。

**PROPOSAL B — 声明 + 赋值子句（`Dim` / `Let`）。** 头部分句允许声明新变量，作用域为整个循环语句：

```vb
Do Dim line = Console.ReadLine() Until line Is Nothing
    ProcessLine(line)
Loop
```

这解决 A 的预声明负担，也解决"循环头引入的变量作用域在哪"的绑定问题——直接对齐 [vblang #337](https://github.com/dotnet/vblang/issues/337) 模式匹配讨论里 LDM 对循环引入变量作用域的既有判断（见下）。ModVB 语境下 `Dim` 的声明形式与 `Let`（Anthony 声明式）等价：

```vb
Do Let line = Console.ReadLine() Until line Is Nothing
    ProcessLine(line)
Loop
```

**PROPOSAL C — C# 式：把赋值变成表达式。** 让条件本身就是赋值表达式，`Do While (line = Console.ReadLine()) IsNot Nothing`。这等于给 VB 引入表达式赋值——改变 `=` 的双重含义（条件里是相等、赋值语境里是赋值），彻底违背 VB"赋值是语句"的基本设定，也与设计原则 #7（避免隐蔽语义变化）正面冲突。We 认为这不值得讨论太久，但把它留档。

**PROPOSAL D — 什么都不做。** 维持两处读取的样板，或鼓励 `While True` + `Exit Do`。代价见场景。

**PROPOSAL E — 纳入块头声明家族统一设计。** Anthony 第 3 章把"块头局部声明/赋值"铺在了同一家族：`With parameter = command.CreateParameter()`（§3.7）、`Try resource1 = GetResource(), resource2 As ResourceHandle`（§3.9）、`Using reader = File.OpenText(filename)` / `Using one, two, three = GetTriplet()`（§3.10）、`Do line = ... Until ...`（§3.6）。这些头部分句共享"作用域恰好为整个块"的规则。若只给 `Do` 做、不给 `Using`/`Try`/`With` 做，语法家族会裂成两半；若一起做，则这是一份家族 speclet 而非一个孤立特性。

### 权衡：Q&A

- **A vs B：预先声明 vs 循环内声明。** A 把"变量从哪来"留给外层——语义简单，但 Option Explicit On/Off 分叉立刻出现（见下）。B 把声明放进头部，作用域与 `For` 控制变量一致（VB spec 中 For 控制变量"scoped to the entire `For` loop"），还顺带解决了 definite assignment：编译器知道 `line` 在每次条件求值前必然被赋值。We 倾向于 B 是更完整的形态，A 是 B 的"已有变量"特例——两者不该是竞争对手，而是同一个文法的两个分支（声明或赋值）。
- **A/B vs C：为什么不做表达式赋值。** 因为 VB 的 `=` 在表达式里是相等比较，赋值是语句级动作。把赋值塞进表达式，等于把 C 家族最招黑的"赋值当条件"直接移植进来，且会破坏 `If x = y Then` 的可读性契约。Anthony 的头部分句恰恰是**VB 化的改造**：保留"赋值是语句"的原则，只是把这条语句挂到循环头上。这是本建议最站得住的地方。
- **A/B vs E：单独做还是家族做。** 这是整场争论最大的一拍。主张家族做：`Using`/`Try`/`With`/`Do` 的头部分句文法应该统一成一条规则（"块头可以挂一组声明/赋值子句"），否则 `Using one, two, three = GetTriplet()` 与 `Do line = ... While ...` 各写各的文法。主张先单独做：VB 主线惯于分阶段落地（2018.12.19 的模式匹配就是分三阶段），且 `Do` 头分句是家族里唯一与**条件**组合的形态（`While`/`Until` 挂在子句后面），文法判定与其余成员不同。**结论：文法先单独立项，但子句的书写形态必须与 Using/Try 一致**，留一份家族 speclet 统一语义。
- **底部循环怎么办。** 是否允许 `Do ... Loop While line = ReadLine()`？2018.12.19 的模式匹配讨论里有一句我们完全同意的话（见下）：变量在声明前使用"quite weird"。底部循环的头部子句如果声明变量，条件在 `Loop` 后才遇到声明，读起来是反的。**结论：头部子句只属于 `Do` 顶测形态**（`Do <子句> While/Until <条件>`），底部形态不引入。
- **`Continue Do` 与头部子句。** `Continue Do` 跳到下一轮迭代；头部子句在下一轮先执行再测条件——语义自然成立，不需要额外规则。`Exit Do` 则直接离开，头部子句不再执行。
- **原文第二个示例为什么不能编译。** `Do bytesRead = stream.ReadAsync(...) While bytesRead > -1`：若 `bytesRead` 未声明，Option Explicit On 下报 BC30451；若声明为 `Integer`，`Integer = Task(Of Integer)` 无转换；若声明为 `Task(Of Integer)`，`Task(Of Integer) > -1` 无运算符。三路皆错。修正形态是在头部子句里显式 `Await`：

```vb
Async Sub ProcessStreamAsync(stream As Stream)
    Dim buffer(1023) As Byte
    Do Let bytesRead = Await stream.ReadAsync(buffer, 0, buffer.Length) While bytesRead > 0
        ProcessChunk(buffer, bytesRead)
    Loop
End Sub
```

`Await` 是普通表达式，出现在头部子句的初始化器里没有新语义。**结论：不隐式 `Await`**——隐式 await 属于 agile-async 工作项（`proposal-agile-async.md`），本建议只做"头部子句允许表达式"，`Await` 按既有规则显式书写。原文示例需要修正。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

今天 `DoTopLoopStatement` 的文法（`..\..\vblang\spec/statements.md` L1213–1217）是：

```antlr
DoTopLoopStatement
    : 'Do' ( WhileOrUntil BooleanExpression )? StatementTerminator
      Block?
      'Loop' StatementTerminator
    ;
```

在 `Do` 之后合法出现的只有 `While`、`Until`、或语句终止符。建议的形态把子句放在 `While`/`Until` **之前**，即：

```antlr
DoTopLoopStatement
    : 'Do' ( (DeclarationOrAssignment (WhileOrUntil BooleanExpression)?)
           | (WhileOrUntil BooleanExpression) )? StatementTerminator
      Block?
      'Loop' StatementTerminator
    ;
```

无歧义：`Do` 后紧跟标识符（或 `Dim`/`Let`）时进入新形态，紧跟 `While`/`Until` 时是既有形态，二者首记号集合不相交。且 `Do <标识符> = ...` 今天就是语法错误——纯增量，零破坏。但有一个文法判定要注意：`Do x = 5 Loop`（无 `While`/`Until` 的裸赋值子句）语义上是"每轮先赋 5 再无条件循环"，毫无意义。We 建议**要求**子句后必须跟 `While` 或 `Until`，否则子句没有存在的理由——这与 2018.12.19 文法里 `DoTopLoopStatement` 的 `( WhileOrUntil ... )?` 可空选择形成对照，但我们这边宁可收窄。

#### 2. 角案例与边界语义

**首轮先赋值。** 子句保证在第一次条件求值前执行——这是与 `Do While`（可零次迭代）的本质区别。`Do line = ReadLine() Until line Is Nothing` 至少执行一次赋值和一次测试。

**循环后置状态。** 正常结束时条件 `line Is Nothing` 为真，故循环后 `line` 可空状态为"是 Nothing"——与 `Do While line IsNot Nothing` 的后置等价（这与 `meeting-typeof-flow-analysis.md` 里 `Do Until TypeOf animal Is T` ≡ `Do While ... IsNot T` 的循环后置收窄是同一套规则）。**但** `Exit Do` 提前退出时循环后 `line` 可能是非空末行——后置状态只在正常终止时成立，需要与 nullability-flow 团队共用"正常终止 vs 提前退出"的路径区分。

```vb
Dim line As String
Do line = Console.ReadLine() Until line Is Nothing
    ProcessLine(line)
Loop
' 正常结束：line Is Nothing。Exit Do 提前退出：line 可能是最后一行非空内容。
```

**赋值目标是属性/字段。** A 形态没有限定赋值目标。`Do Me.Value = NextValue() While Me.Value > 0` 里，条件**重新求值 getter**，而 C# 的 `(value = NextValue()) > 0` 比较的是赋值表达式直接产生的值——C# 不重读变量。若 getter 与 setter 不对称，两种语言的语义在这里分岔。VB 没有"赋值表达式值"可用，重读变量是唯一选择，所以**赋值目标限定为简单局部变量或参数**（排除属性、字段、任意 lvalue）是本特性安全的底线——理由与 typeof-flow 的 B 方案一致：局部变量不可被重求值、不可被 shadow。

**值类型目标。** `Do i = GetNext() While i < 10`，`i` 为 `Integer`：子句赋值、条件比较，与 `For` 的语义正交，无拆箱问题。`Until i = -1` 这类"哨兵值"用法是 `For` 表达不了、`Do` 头正好填补的角落。

**`Continue Do` / `Exit Do` 交错。** 见 Q&A：`Continue Do` 回到子句，`Exit Do` 离开。无新规则。

#### 3. 作用域与绑定

Anthony 原文没有写作用域规则，只写"其作用域覆盖整个循环（含条件与循环体）"。这里我们直接引用 2018.12.19 模式匹配讨论中 LDM 对循环引入变量作用域的判断（`..\..\vblang\meetings/2018/vbldm-notes-2018.12.19.md`），原文逐字：

- 在 `WhileStatement` 文法上方："`// LDM thinks introduced variables scope to the While block`"
- 在 `DoTopLoopStatement` 上方："`// introducing variables with either While or Until could only be used by the When clause, not within the block` / `// LDM thinks probably within the block as well`"

即：**循环条件里引入的变量作用域 = 整个循环块**（含条件与循环体）。这与 Anthony 说的"覆盖整个循环"一致。语义模型里，`Do Dim line = ... Until line Is Nothing` 的 `line` 应在循环语句范围内可见；循环外只有预先声明的变量继续可见。We 采纳"循环块作用域"作为 B 形态的绑定规则，与 For 控制变量一致。

`Probably`：若 B 形态的变量在循环外与同名外层变量冲突，应按最近作用域遮蔽——与 `For` 控制变量的遮蔽规则一致，无特殊处理。

#### 4. 与既有特性的交互

- **循环体局部变量的按迭代复制。** spec（`statements.md` L1165）规定"Each time a loop body is entered, a fresh copy is made of all local variables declared in that body"。头部声明的变量算"在循环体内声明"吗？We 认为**算**——头部声明的变量在每次迭代开始时新建副本，与 `For` 迭代变量按迭代捕获修复（`proposal-for-enhancements.md`）的目标一致，让 lambda 捕获拿到每一轮自己的副本，而非共享末值。
- **lambda 捕获。** 承接上条：若头部变量被 lambda 捕获，按迭代复制避免了 BC42324 一类"捕获迭代变量"警告。这与 For 修复是同一实现面。
- **`GoTo` 进入循环。** spec 规定禁止 `GoTo` 进入含 lambda/LINQ 的循环；头部声明变量后，循环入口处多了一个"必须先赋值才能测条件"的节点，GoTo 穿越它的规则需沿用既有禁止。
- **ByRef / copy-in copy-out。** 条件里的 `line` 是只读求值，不涉及 ByRef。若把头部变量传给调用点后的 ByRef 实参，被调方可改写成更宽值——与 typeof-flow 的 ByRef 作废规则一致：循环内被改写后，后续条件按新值求值，无收窄承诺。
- **`Await` 交互。** 见 Q&A：头部子句允许 `Await` 作为初始化器表达式，但不隐式 await。与 `Async Iterator`（`Await Each`）正交——那是 For Each 的事。
- **流分析交互。** 循环后置状态与 nullability-flow（`IsNot Null` 收窄）和 typeof-flow（类型收窄）共用同一引擎：`Until line Is Nothing` 正常结束后 `line` 可空性收窄为"一定为 Nothing"，`Do Let x = TryCast(...) While x IsNot Nothing` 正常结束后收窄为"一定为 Nothing"。语义上的 `Null` 字面量（见第 8 条）会推高这里的分叉复杂度。

#### 5. Breaking change 与兼容性

零破坏。`Do <标识符> = ...` 今天即语法错误，新文法只扩展合法集合；不引入新关键字（`Dim`、`Let`、`While`、`Until` 均已是记号）。不改变任何既有 `Do While/Until` 的行为。`Until` 不是保留字（spec L1257"`Until` is not a reserved word"）——本建议不触碰该事实。唯一需要 gate 的是 langversion：新文法按版本门控即可，旧版本编译旧行为。

#### 6. Option Strict / Option Explicit 分叉

这是 A 形态最需要摊开的分叉。

- **Option Explicit On**：`Do line = ...` 要求 `line` 已预先声明，否则报 BC30451。声明形式 `Do Dim line = ...` 永远可用。两路径一致。
- **Option Explicit Off**：`Do line = ...` 的裸赋值会**隐式声明** `line`。按 spec（`statements.md` L511），隐式局部变量**类型为 Object、作用域为整个方法**——这与本建议的"循环头声明"语义（循环块作用域）直接打架。若允许隐式声明，`line` 会变成 Object 且方法级可见，类型信息丢失（`line Is Nothing` 仍合法，但 `ProcessLine(line)` 的强类型性没了）。
- **Option Strict Off + late binding**：`Do x = SomeDynamicMethod() While ...` 允许晚期绑定赋值，`x` 为 Object。

**结论：A 形态在 Option Explicit Off 下的隐式声明是杂质。** 我们倾向于：裸赋值要求变量预先声明（Option Explicit On 语义），隐式声明走既有规则、不作为本特性的卖点；B 形态（显式 `Dim`/`Let`）在两条 Option 路径下行为一致。严格/宽松路径的成员可用性必须保持一致——这是 Option Strict 分叉的底线。

#### 7. IDE / IntelliSense 影响

头部子句是循环的一个独立步进单位：调试器应能单独对子句设断点、单步"赋值→条件→体"，watch 面板在条件求值处显示子句刚写入的值。补全与签名帮助须把头部变量纳入循环体内作用域；错误文案需要覆盖"循环头声明的变量在此处尚不可用"（条件里使用子句稍后声明的另一变量时）。这些都要进原型验证。

#### 8. 数据 / 普遍性

逐行/逐块读取循环是真实高频惯用法（`Console.ReadLine`、`TextReader.ReadLine`、`Stream.Read`/`ReadAsync`、`DataReader.Read`），C# 开发者随手就写 `while ((line = r.ReadLine()) != null)`。但"数十万安静客户"里这个模式的占比没有量化数据。`Suspect`：这是真实的 DX 增益，但属于"消除样板"里中等偏小的那类——不像 `TypeOf` 收窄那样每次多态分发都在付费。优先级应排在循环语句现代化的同伴（For 多计数器、For Each 增强）之后或同期。

另注意 `Null` 字面量依赖：原文示例写 `line Is Null`。主线段在 [LDM-2014-02-17](../../vblang/meetings/2014/LDM-2014-02-17.md) 里逐字记录："`# 18. Introduce "Null" literal` / `* Rejected on 2014-01-06. It would have had parity with C# "inference is aware of null" feature. *`"。`Null` 在 ModVB 是独立的 `proposal-null-literal.md`。**本建议的两个示例都应写作 `Is Nothing`**，或明确标注依赖 null-literal；混用两者会让示例无法编译也不可读。

#### 9. 更简替代

- **现状样板**（预读 + 循环体末尾再读）：功能等价、零新语法，但读取出现两次。这是本建议要消灭的东西，它确实存在。
- **`Do ... Loop While` 底部测试**：把读取放循环体末尾，`Do : line = ReadLine() : If line Is Nothing Then Exit Do : ProcessLine(line) : Loop`——仍然要 `Exit Do` 或 `Loop While line IsNot Nothing`，且第一次读取没法在进入前拿到。不构成替代。
- **LINQ 序列化**：`For Each line In ReadLines()` 需要把读取源改造成 `IEnumerable(Of String)`；对 `Console.ReadLine`/`Stream.ReadAsync` 这类非序列 API 不适用。
- **Analyzer / 重构**：可以提示"此读取出现了两次"，但无法让代码更短。不是替代。

#### 10. 复杂度 / 成本 / 优先级

文法扩展小（`DoTopLoopStatement` 加一个可选子句）；绑定/作用域规则中等（循环块作用域、definite assignment、按迭代复制）；与 nullability-flow 的循环后置状态对齐需要协调。整体是"低成本、中价值、零破坏"。若按家族（E）一起做，成本翻倍但价值也翻倍——我们倾向先单独落 `Do`，家族文法随后。

#### 11. 运行时 / CLR 硬约束

无。纯语法与绑定，不产生新 IL 形态；循环由既有分支指令构成，PEVerify 无碍。`Do` 不是表达式树节点，无表达式树限制。子句里的 `Await` 走既有异步状态机重写。

#### 12. 值不值得做

- **价值**：中。消除"读取出现两次"的真实样板，且是 VB 化的（保持赋值是语句），不是照抄 C#。
- **成本**：低–中。语法小、绑定中、无 IL。
- **风险**：低。纯增量零破坏；主要风险是语义分叉（隐式声明、属性目标、隐式 await）——都可以用"限定简单局部变量 + 显式 `Await` + 显式声明"收口。

**值得做——但必须收口范围。** 不做 A 的"无限制赋值目标"，不做隐式声明卖点，不做隐式 await。若按原文逐字实现（含不可编译的 ReadAsync 示例、`Null` 依赖、未定声明语义），我们会建议不做。

### VB 基因对照

- **消除常见样板（原则 #9）**：正面命中。两处读取收成一处，最小语法解决高频痛点。
- **不引入"第二种做事方式"（原则 #3）**：这是本建议最大的扣分项。`Do` 头分句是**新的循环形态**（先赋值→再测→体），与 `Do While`（先测）、`Do...Loop While`（后测）并存为第三种。但它不是与既有语法重复——没有任何既有形态能表达"先取再判且变量在体内可用"，所以是"扩展表面"而非"重复表面"。`Not all of us are happy with` 这个取舍，但 For 循环头本来就有自己的变量子句，Do 头分句是同一模式的延伸，不是突兀的新语法。
- **读起来像英语、对新手友好（原则 #5）**：`Do line = Console.ReadLine() Until line Is Nothing` 读起来是一个句子，顺序就是执行顺序，可读性尚可；比 C# 的 `while ((line = r.ReadLine()) != null)` 更接近英语。
- **避免隐蔽的控制流/语义变化（原则 #7）**：风险点在"条件重读变量"（属性 getter 不对称）与隐式声明——用"限定简单局部变量 + 显式声明"对冲。若让属性/字段进头部，这就是又一个隐蔽语义变化。
- **与主线关系（对照表 2.3）**：主线没有 `Do` 头分句建议。2018.12.19 模式匹配讨论已在探索"`While`/`Until` 条件里引入变量"的语法（`BooleanExpressionOrPattern` + `'Dim' Identifier` 变量模式），并对作用域给出"循环块"判断——本建议的 B 形态与主线**方向一致但语境不同**：主线是类型匹配引入变量，本建议是赋值/声明引入变量；二者应共用"循环引入变量作用域"的规则，但文法不冲突。与 For 多变量（#48，Approved-in-Principle）同属"循环语句现代化"这一主线趋势。总体：**Anthony 独立延伸、方向与主线一致**。
- **破坏性变更**：无。

### RESOLUTION:

1. **骨架采纳**：`Do` 顶测形态允许在 `While`/`Until` **之前**挂一个"赋值或声明"子句，语义 = 每轮先执行子句、再求值条件，变量在条件与循环体内可用。子句**必须**后跟 `While` 或 `Until`（裸子句 + 无条件循环无意义，不收）。
2. **v1 赋值目标限定**：只允许**简单局部变量或参数**，且要求预先声明（Option Explicit On 语义）；排除属性、字段、任意 lvalue、以及 Option Explicit Off 下的隐式声明卖点。理由：局部变量不可重求值、不可 shadow，条件重读与子句写入必然一致。
3. **声明形式纳入 v1**：支持 `Do Dim x = expr While/Until cond`（ModVB 语境 `Dim`/`Let` 等价）。作用域 = **循环块**（对齐 2018.12.19 "introduced variables scope to the While block"）；声明变量按迭代复制，供 lambda 捕获，与 For 捕获修复一致。
4. **不隐式 `Await`**：`Task` 结果需显式 `Await`；原文第二个示例修正为 `Do Let bytesRead = Await stream.ReadAsync(...) While bytesRead > 0`。隐式 await 移交 agile-async 工作项。
5. **底部循环不引入头部子句**：`Do ... Loop While/Until` 保持现状（2018.12.19 #8 iv 一致："We don't usually let variables be used before they are declared, so it would look quite weird to allow this in one case (`DoBottomLoopStatement`)"）。
6. **示例的 `Null` 一律改 `Is Nothing`**，或显式标注依赖 `proposal-null-literal.md`；不混用。
7. **循环后置流状态**：`Until x Is Nothing`（或 `While x IsNot Nothing`）正常结束后的可空收窄成立，与 nullability-flow / typeof-flow 共用"正常终止 vs `Exit Do` 提前退出"的路径区分；`Exit Do` 路径收窄作废。
8. **多子句（逗号分隔）与块头声明家族（`With`/`Try`/`Using`/`Do`）的统一文法** ⇒ 列为后续（`Table`），单独一份家族 speclet。子句书写形态先对齐 Using/Try（`Using one, two, three = GetTriplet()`、`Try resource1 = GetResource(), resource2 As ResourceHandle` 的逗号分隔先例，[vblang #48](https://github.com/dotnet/vblang/issues/48)："strong precedent for multiple consecutive statements ... `Imports`, `Dim`, `Next`, `From`, `Let`, `Case`"）。

### Implication:

- 起草 speclet：`DoTopLoopStatement` 文法扩展、循环块作用域、首轮先赋值、按迭代复制、definite assignment（子句先于条件）、Option Strict/Explicit 分叉表、langversion 门控。
- 修正建议原文：ReadAsync 示例加显式 `Await`；`Null` → `Nothing`；明确 v1 范围（简单局部变量 + 预先声明或 `Dim`/`Let`）；补 BNF 与兼容性章节。
- 与 nullability-flow 团队对表：`Until x Is Nothing` 的循环后置收窄规则、`Exit Do` 路径区分。
- 与家族 speclet（With/Try/Using 头部子句）协调子句文法；确定逗号分隔多子句的统一形态。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：头部声明的变量在"循环外同名遮蔽 + lambda 捕获"组合下的精确规则（`Probably` 与 For 控制变量一致，未定稿）。
- `OPEN QUESTIONS`：若 A 形态（裸赋值）目标变量在循环外作用域更大，循环结束后其值语义即普通赋值语义——明确即可，但需写进 spec。
- `OPEN QUESTIONS`：条件里使用子句**稍后**声明的另一变量（`Do Dim a = f(), b = a ...` 的声明顺序）——是否允许前向引用，待定（`Suspect`：倾向不允许，与"变量不得先于声明使用"一致）。
- `TODO`：量化"读取→处理→再读取"惯用法占比，为数据/普遍性补证据。
- `Follow-up`：与 `For` 迭代捕获修复共用实现面，确认"头部声明按迭代复制"走同一条捕获路径。

### 状态

- **LDM 状态：Consider（限定范围，骨架采纳）**。转 Active 的条件：建议原文修正（示例可编译、`Null` 处理、v1 范围明确）+ 最小原型验证作用域/definite assignment/IDE 步进。
- **三态判定：Consider** — 价值真实、零破坏、VB 化改造站得住；但当前建议原文不成熟（不可编译示例、未决声明语义、家族文法缺失），不足以直接 Active。多子句与家族统一文法为 Table。

---

## 附录：特性评价

# 建议评价报告：proposal-do-enhancements.md

## 评价对象

- 建议：proposal-do-enhancements.md — `Do` 循环头声明/赋值
- 来源：Anthony 原文第 3.6 节 `Do`（`..\AnthonyDesign_wordpress.txt` L963–972；两行示例与注释逐字出自该节）；关联家族 §3.7 `With`（L976）、§3.9 `Try`（L1026）、§3.10 `Using`（L1066）
- 配方目标：把"赋值在条件求值前执行、变量在循环体内可用"的读取循环写成一条 `Do` 语句，消除"读取出现两次"的样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 3–2 之间：目标改进真实（消除重复读取）且 Motivation 说清了场景，但示例不能演示本特性——第二个示例（`Do bytesRead = stream.ReadAsync(...) While bytesRead > -1`）三路皆错（未声明/类型不匹配/`Task` 无 `>` 运算符），第一个示例依赖未采纳的 `Null` 字面量；无原型 | 已提供 | 示例不可编译、`Null` 依赖未标注、证据止于书面且书面即有缺陷 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造"。核心是 C# `while ((line = r.ReadLine()) != null)` 惯用法的 VB 化——保留"赋值是语句"，把赋值挂到循环头；与 Anthony ch.3 块头家族（With/Try/Using）谐和；但引入了第三种循环形态（触碰原则 #3），且 Option Explicit Off 隐式声明、属性目标等细节是未 VB 化的杂质 | 已检查 | 第三种循环形态的合法性未论证；赋值目标限定缺失 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design 示例不能编译、`Null`/`Nothing` 混用、与 3.9/3.10 的关联只在 Drawbacks 提一句、无文法/BNF | 已检查 | 示例不可编译、无文法、家族关联未展开、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 3/5 | 锚点 3："有得有失"。雷/水正向（提速、与 For 修复共用实现面、盘活块头声明家族资产）；暗风险=条件副作用调试复杂度、隐式 await 隐患未被文档识别；风=与家族文法一致性需协调，未充分权衡 | 已检查（预测待定） | 隐式 await 隐患文档未识别；第三种循环形态与主线演化方向的一致性未论证 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。材料=Anthony §3.6 逐字（已标注章节号于 Drawbacks 之外仍需显式）；**未声明借鉴 C#**（条件赋值惯用法是明显来源）；`Null` 字面量依赖（主线段 2014 已拒绝）未标注；继承 VB6/主线 `Do While/Until` 文法未点明 | 已检查 | C# 来源未标注、`Null` 依赖未标注、主线 2018.12.19 循环引入变量作用域讨论未引用 |

## 设计原则对照

- **与 VB 基因：部分一致**——消除样板（#9）正面；可读性（#5）尚可（读起来是一个句子）；但第三种循环形态触碰 #3（第二种做事方式），"条件重读变量"触碰 #7（隐蔽语义变化），需限定简单局部变量对冲。
- **与主线关系：Anthony 独立延伸，方向与主线一致**——主线无 `Do` 头分句建议；2018.12.19 模式匹配已在探索"`While`/`Until` 条件引入变量"并给出循环块作用域判断，B 形态与其共用规则；与 For 多变量（#48 Approved-in-Principle）、`Exit For j`（#186）同属循环语句现代化。`Null` 字面量依赖与主线段 2014 拒绝记录冲突，须标注。
- **破坏性变更：无**——纯增量，`Do <标识符> =` 今天即语法错误；不引入新关键字。

## 总评

- **达成程度：部分达成**——概念（VB 化的条件赋值惯用法）与价值成立；示例正确性、声明 vs 赋值语义、家族文法统一未完成。
- **LDM 三态建议：Consider（限定范围）**——以"简单局部变量 + 预先声明或 `Dim`/`Let` 声明 + 顶测 + 显式 `Await`"为 v1；多子句、家族统一文法、隐式 await 为 Table。
- **主要问题**：① 原文示例不可编译（ReadAsync 未 `Await`）；② 声明 vs 赋值、Option Explicit 分叉未定；③ 与块头声明家族（With/Try/Using）的文法统一缺失；④ `Null` 字面量依赖未标注（主线段已拒绝）。

## 返工建议

- **补充章节**：文法/BNF（`DoTopLoopStatement` 扩展，含"子句必须后跟 `While`/`Until`"的约束）；Compatibility（纯增量论证）；Option Explicit/Strict 分叉表；作用域（循环块）与 definite assignment 规则。
- **补充证据**：修正示例——`Do Let bytesRead = Await stream.ReadAsync(...) While bytesRead > 0`（Async 方法内合法），`Null` → `Nothing` 或标注依赖；最小原型验证首轮先赋值、循环块作用域、按迭代捕获、IDE 单步/断点。
- **未决问题处理**：明确 v1=简单局部变量 + 显式声明；底部循环不引入；不隐式 await；条件前向引用倾向不允许。
- **设计探索**：块头声明家族（With/Try/Using/Do）统一子句文法的家族 speclet；循环后置流状态与 nullability-flow / typeof-flow 对齐（`Until x Is Nothing` 正常结束后的收窄与 `Exit Do` 路径区分）。

---

## 附录：C# 生态与互操作考量

> 本附录依据 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库浓缩索引）。以下 csharplang 文件路径均相对该仓库镜像根 `..\..\csharplang`。

### 相关 C# 现实方向

本提案（`Do` 循环头声明/赋值）的主题落在 C# 的**循环形态**与**「条件中引入变量」的作用域**这一面。对照索引，最相关的不是 T2/T3 低层内存主线，而是三个语法层面的接触点，且均不构成互操作摩擦：

1. **C# 循环形态的现实（T0 基础语法面）**。C# 只有 `while` / `do` / `for` / `foreach` 四种形态，其中**后置条件循环只有 `do-while` 一种**（`do { } while (cond);`），没有 `until` 变体，也没有「循环头赋值/声明子句」这种独立语法位。`for (initializer; condition; iterator)` 的头部是 C# 经典的在条件求值前声明/初始化循环变量、且变量作用域为整个循环的语法位；C# 7 起 `while`/`do` 条件里的 out/pattern 变量也以循环语句为作用域（见下第 3 点）。VB 的 `Do While/Until`、`Loop While/Until`、`For` 家族形态更多、并且 `While`/`Until` 双端可挂——这是 VB 的**语法特色**。循环形态是方法内语法，不跨程序集边界，IL 层面只是分支 + 比较指令，CLR/元数据完全不感知两语言的形态差异（与本会议第 11 节「无新 IL 形态、PEVerify 无碍」一致）。

2. **C# 惯用法对照：赋值表达式**。C# 用「赋值即表达式」写同一个"先取再判"惯用法：`while ((line = r.ReadLine()) != null)`。本提案的 **Proposal C**（照抄该形态、给 VB 引入表达式赋值）被**明确拒绝**，采纳的是 VB 化改造（保留"赋值是语句"，把赋值挂到循环头）。这决定了两语言在这条惯用法上**语法哲学分道，但互操作层面零冲突**——VB 编译器为头部子句产出的 IL 与 C# 的赋值条件循环等价（先赋、再比较、分支），CLR 不感知源码形态。

3. **C# 7 先例：条件中引入变量的作用域（最实质的兼容证据）**。C# 7 pattern matching 提案对模式变量作用域的规定（`proposals\csharp-7.0\pattern-matching.md`，§"Scope of pattern variables"），逐字为：

   > The scope of a variable declared in a pattern is as follows:
   > - If the pattern is a case label, then the scope of the variable is the *case block*.
   > - Otherwise the variable is declared in an *is_pattern* expression, and its scope is based on the construct immediately enclosing the expression containing the *is_pattern* expression as follows: …
   > - If the expression is in an *iteration_statement*, its scope is just that statement.

   2016-12-07 LDM 对循环条件中引入的表达式变量作用域有更直接的记录（`meetings\2016\LDM-2016-12-07-14.md`，§"Do-while loop scope"），逐字为：

   > In the previous meeting we decided that while loops should have narrow scope for expression variables introduced in their condition. We did not explicitly say that the same is the case for do-while, but it is.

   `out var` 同样规定作用域与 pattern variable 一致（`proposals\csharp-7.0\out-var.md`）："The scope will be the same as for a *pattern-variable* introduced via pattern-matching."

   这与本提案 **RESOLUTION 3 的「循环块作用域」判定同向**——C# 早已把「条件中引入的变量」限定为循环语句作用域，本提案的绑定规则有一个成熟的外部先例可对照（2018.12.19 VB LDM 讨论是 VB 侧同一判断，C# 侧是更早的独立佐证）。

4. **T7（dynamic/晚期绑定）接触点**。本会议第 6 节 Option Strict Off 的 `Do x = SomeDynamicMethod() While ...` 走晚期绑定，落在索引 T7「`dynamic` 与表达式树相对边缘化」的面上（`..\..\csharplang-index.md` §一 T7）。但这是 VB 晚期绑定的一般性问题（决策文件 M2/M8），不是本特性特有——头部子句只是又一个允许 Object 赋值的语法位。

### 现实 vs 提案

| 面向 | 判定 | 理由 |
|---|---|---|
| 循环形态本身 | **兼容（互操作脱节）** | 循环形态是方法内语法；CLR/IL 不感知 `Do While` vs `while`、`Loop Until` vs `do-while` 的区别。C# 只有 do-while 一种后置条件循环，VB `Do/Loop` 家族更丰富——**语法特色而非互操作摩擦**。 |
| 赋值表达式惯用法 | **兼容（哲学分道，非冲突）** | C# 用表达式赋值写同一惯用法，VB 拒绝（Proposal C）、用头部子句替代。产出 IL 等价，跨语言调用无碍；不引入新关键字、无新 IL 形态、无新元数据。 |
| 条件中引入变量的作用域 | **兼容** | C# 7 pattern/out 变量与 2016 LDM 已确立「循环语句作用域」；本提案 B 形态的循环块作用域与之同向，互为佐证、无冲突。 |
| Option Strict Off / 晚期绑定 | **需桥接（一般性问题）** | 若 .vbx 走 NativeAOT，`Do x = SomeDynamicMethod() While ...` 的 Object 晚期绑定仍是反射负担（决策文件 M2/M8）。但这是全语言层面「默认安全 / 按需动态」路线问题，与本特性无因果关系。 |

**结论**：本提案与 C# interop 的关系**弱且无害**——无新元数据、无跨程序集表面、无 AOT/trimming 新负担。唯一的"张力"是它给「条件中写赋值副作用」又开了一个语法位，而 C# 的 T7 方向恰恰在收缩动态/反射面；但 VB 的头部子句是**显式语句**（赋值是语句），不是 C# 那种隐式赋值表达式，语义边界更清楚，不触碰 C# 的 `dynamic` 语义。

### 对 VBScript.NET 的适应建议

- **无需元数据识别**：与决策文件 M8 的「VB 必须认识 `RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute`」不同，本特性零新元数据，编译器不需为它识别任何新 CLR 属性或 `CompilerFeatureRequired` 标志（对照 M4 对 unions 的要求）。
- **source-gen / AOT 友好**：头部子句是纯编译期语法，产出普通分支 IL，天然 AOT 友好；在「脚本编译到受管程序集 + source-gen 桥」的默认路线（决策文件 M5）下无额外成本。
- **与 C# 库互操作无摩擦**：.vbx 调用 C# 的 `IEnumerable` / `while` 风格 API、或 C# 消费 .vbx 模块，都不感知循环形态差异。唯一要注意的是 RESOLUTION 2 已把赋值目标限定为简单局部变量/参数——若放开到属性/字段，条件重读 getter 的语义与 C# 赋值表达式值（不重读）不一致，会在跨语言对拍调试时制造困惑；限定即安全。
- **若走解释执行（scripting-interpreted）**：解释器需能表示「子句先于条件」的步进节点（IDE 单步/断点、watch 在条件求值处显示子句刚写入的值，见本会议第 7 节），这与决策文件 M5 的 interpreted 模式属同一实现面，非互操作问题。

### 对既有 RESOLUTION / 三态判定的影响

无实质影响。C# 对照不改变 RESOLUTION 1–8 任何一条：

- RESOLUTION 2（赋值目标限定简单局部变量）与 C# `while` 赋值表达式「比较的是表达式直接产生的值」形成语义对照——VB 侧限定目标正是为避免这种分岔，RESOLUTION 已覆盖，无需修改。
- RESOLUTION 3（声明形式、循环块作用域）获得 C# 7 pattern variable 作用域先例的外部背书（见上），使「循环块作用域」判定更稳。
- 三态判定维持 **Consider（限定范围）**：C# 生态既不催生也不阻碍本特性；它是一个纯 VB 语法特色，互操作中性。C# 侧无对应提案追赶（索引无 loop-form 相关提案），不存在"被 C# 抢先"或"与 C# 撞车"的问题。

### 引用纪律

- 「The scope of a variable declared in a pattern is as follows: … If the expression is in an *iteration_statement*, its scope is just that statement.」→ `proposals\csharp-7.0\pattern-matching.md`（§Scope of pattern variables，L141–153，逐字，已核实）。
- 「In the previous meeting we decided that while loops should have narrow scope for expression variables introduced in their condition. We did not explicitly say that the same is the case for do-while, but it is.」→ `meetings\2016\LDM-2016-12-07-14.md`（§Do-while loop scope，L159–162，逐字，已核实）。
- 「A variable declared this way is called an *out variable*. … The scope will be the same as for a *pattern-variable* introduced via pattern-matching.」→ `proposals\csharp-7.0\out-var.md`（L5/L14，逐字，已核实）。
- C# `while ((line = r.ReadLine()) != null)` 为 C# 基础语法（赋值表达式）的通用惯用法，csharplang 库内无单一出处文档，标注为通用用法、非逐字引用。
- T7/T5/T6/T8 方向描述引自索引 `..\..\csharplang-index.md` §一摘要，非原文逐字。
- **OPEN QUESTIONS**：C# 官方是否考虑过 `do-until` / `loop-until` 后置条件变体——索引未见记录，`do-while` 是唯一后置条件形态（未在 csharplang 库内核实到相反证据）；VB 头部子句在 interpreted 模式下与 C# 对拍调试的具体语义映射待原型验证。
