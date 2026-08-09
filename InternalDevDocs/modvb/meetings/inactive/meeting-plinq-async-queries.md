# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上一场我们刚把"并行扩展"（§18.5）判定为 Table，并把"查询级并行"的调查权正式移交到本建议名下（`meeting-parallel-extensions.md` RESOLUTION #3）——所以这场会议的第一项职责，是接住那笔移交。第二项职责更大：这份建议（§18.14）一口气点名了六个调查方向——Parallel LINQ、使用 `Await` 的查询、针对 `IAsyncEnumerable` 的查询，以及 CTE、地理空间、No-SQL、时序数据库——但**一个语法、一个示例都没有**。原文只有一句话（`..\..\AnthonyDesign_wordpress.txt` L3038），本建议忠实地把这句话翻译成了登记册。

We 不得不用一整场回答一个前置问题：**这份登记册里，哪些方向值得激活、哪些应该关闭、哪些从来就不该进语言。** 与上一场的"并行扩展"不同——那只是一句观察，这一份至少点名了方向——但方向多不代表设计多。六个方向的成本画像、血缘与既有判例关系完全不同，把它们捆在一份建议里，是让一个未设计的登记册替六个未设计的调查背书。We were not going to do that again.

## Agenda

* [Proposal: PLINQ / 异步查询 / 更多查询运算符](#proposal-plinq--异步查询--更多查询运算符)

## Proposal: PLINQ / 异步查询 / 更多查询运算符

_Related: Anthony 原文 §18.14（`..\..\AnthonyDesign_wordpress.txt` L3034–3038）；ModVB `meeting-parallel-extensions.md`（查询级并行划归本建议）；`meeting-async-iterator.md`（`Await Each` / `Async Iterator Function` = Consider，消费端先于生产端）；`meeting-agile-async.md`（中间 `Await` 省略 = Reject）；`meeting-query-comprehensions.md`（`Include` 循环内理解 = Reject，库/语言边界）；`meeting-query-enhancements.md`（查询运算符四件套，拆件裁定）；主线 2014-02-17（查询子句=翻译为标准运算符）、2014-04-02（await 全表达式上下文）、2017-08-09（异步优先级由平台驱动）、2018-05-30（#167 `Return?` = LDM Reviewed: No Plans；"数十万安静客户"）_

### 场景与缺口

We started from the paragraph. 原文（§18.14）逐字如下：

> Parallel LINQ, queries that use `Await`, queries against `IAsyncEnumerable` are areas to investigate. As query languages and data stores evolve we also have to ensure that the query comprehension syntax can smoothly express common use-cases (e.g. Common-Table Expressions, Geospatial data, No-SQL, Temporal Databases) without needing to fallback to extension methods and lambda expressions.

即：Parallel LINQ、使用 `Await` 的查询、针对 `IAsyncEnumerable` 的查询是值得调查的领域；随着查询语言与数据存储演进，必须确保查询理解语法能平滑表达常见用例（CTE、地理空间、No-SQL、时序数据库），而不必退回扩展方法与 Lambda 表达式。

这句话拆成两层，我们像上一场拆"并行扩展"那样拆：

**观察层——查询理解的子句集是固定的。** VB 查询理解的文法枚举了一组固定子句（`From` / `Where` / `Select` / `Order By` / `Group By` / `Join` / `Group Join` / `Aggregate` / `Distinct` / `Skip` / `Take` 等）。一个子句对应一个标准查询运算符。当场景超出这个集合（异步、并行、领域数据形态），开发者就必须退回扩展方法与 Lambda。观察本身为真——2014-02-17 主线讨论查询子句时，整个方向就是"把查询理解**翻译为标准运算符**"（`From` 子句、`Select` 的 `As` 子句、BC36606 名字冲突修复都走这条翻译链），所以"子句集=运算符集"是查询理解的既定骨架，不是作者的一家之言。

**结论层——"因此语言要覆盖这些场景"。** 这里 We stopped。六个方向里，至少三个（并行、垂直数据形态）的答案不是"加语法"而是"这本来就是库的活"；另外两个（`Await` 查询、`IAsyncEnumerable` 查询）有真实的机制性缺口，但缺口已经被同组的 `async-iterator` 建议部分接管。**"平滑表达"的愿望是真的，"为每个愿望造一个子句"不是答案。** 我们把六个方向拆成三个簇来讨论：**并行簇**、**异步簇**（`Await` 查询 + `IAsyncEnumerable` 查询）、**垂直存储簇**（CTE / 地理空间 / No-SQL / 时序）。

### 候选方案

因为建议没有语法，我们先枚举"如果做，可能长什么样"，再逐个过。

**PROPOSAL A — 全量激活：为六个方向设计查询子句。** 并行子句（`From x In items Parallel` 或 `Parallel From ...`）、`Await` 子句（`Where Await p(x)`）、异步序列子句、CTE 子句、地理空间谓词子句、时序窗口子句。这是字面读法，但也是我们当场就能否掉的读法——它把三个不同簇的设计义务压给一个没有语法的登记册。

**PROPOSAL B — 拆分登记册：六个方向逐个独立处置，该关的关、该并的并、该拒的拒。** 沿用 `query-comprehensions` 会议对七件套、`query-enhancements` 会议对四件套的处理先例。We think this is the only honest reading。

**PROPOSAL C — 异步簇并入 `async-iterator`（`Await Each` 的后续层）；并行簇关闭（库已解决）；垂直存储簇 Reject（库/提供程序领地）。** 本场后半段实质在评估这一方案的每一个刀口。

**PROPOSAL D — 什么都不做，保持 inactive。** 维持扩展方法与 Lambda 表达。这是 Anthony 自己的 Alternatives 第一条。

**PROPOSAL E — 提取一条正向原则：**"查询理解可扩展性"原则——**子句跟随标准运算符；领域数据形态住进提供程序；扩展方法与 Lambda 是设计好的可扩展性边界，不是需要消灭的退路。** 作为备忘输出，不伴随任何语法。

### 权衡：Q&A

**Q1：并行簇——`From x In items.AsParallel() ...` 今天就能编译，语言还要做什么？**

`AsParallel()` 是 .NET 的扩展方法（`ParallelEnumerable.AsParallel`），返回 `ParallelQuery(Of T)`；查询理解把 `From x In src Where ... Select ...` 翻译为 `src.Where(Function(x) ...).Select(...)`，扩展方法解析在 `ParallelQuery(Of T)` 上命中 PLINQ 的运算符重载。也就是说**并行查询在语言层面已经是闭门即通**：

```vb
' 今天就能编译：查询理解天然组合 AsParallel()。Probably（库事实，仓库内未运行验证，见 TODO）。
Let topCities = From c In cities.AsParallel()
                Where c.Population > 1_000_000
                Order By c.Population Descending
                Take 10
```

We 不必争论"查询能否并行"——能。要争论的是"语言是否给并行一个**子句**"。答案在三条判例面前都很清楚：① `Parallel` 关键字撞既有的高频成员访问 `Parallel.For(...)`（上一场已列为 A 形态的破坏面）；② 一个 `Parallel` 子句是 `AsParallel()` 的第二种做事方式（原则 #3）；③ 上一场对 #167 `Return?` 的引文在这里适用的是其精神——用一个词把整个查询从顺序变成无序并发，读者几乎必然沿用顺序心智模型，而 PLINQ 的真实语义（`AggregateException` 聚合异常、`Where` 默认无序、`Order By` 强制有序化）与声明式的阅读直觉并不一致。**并行值得显式，`AsParallel()` 恰好把它放在显式位置。** 结论倾向：**并行簇关闭——不是"值得做但太难"，而是"已经被库解决，语言介入是减分"。**

**Q2：`Await` 查询——"源上 Await"与"谓词上 Await"是两回事，建议一个字都没分。**

原文点名 "queries that use `Await`"，但没有给形态。我们枚举出至少三种完全不同的东西：

```vb
' 形态一：源上 Await —— 数据源本身是异步获取的。
' 今天非法：spec 现行规则（expressions.md §Await Operator，见 ModVB meeting-agile-async.md 的归纳）
'          await 表达式不得出现在查询表达式内。
Let recent = From row In Await GetRecentRowsAsync()
              Where row.IsActive
              Select row

' 形态二：谓词上 Await —— 谓词本身是异步的。
Let flagged = From item In items
              Where Await IsFlaggedAsync(item)

' 形态三：投影上 Await —— 投影是异步的。
Let titles = From doc In docs
             Select Await doc.FetchTitleAsync()
```

形态一有两个硬问题。其一，**延迟执行被打破**：查询理解的源表达式在查询被枚举时才求值（LINQ 的惰性）；`Await` 在 `From` 源里意味着要么源在查询构造时**急切**求值（把惰性查询变成急切副作用，原则 #7 的红线），要么整条查询变成异步迭代器（那正是 `async-iterator` 建议的组合状态机领地——成本主体，上一场已定为"等原型"）。其二，它要求 `Await` 出现在查询表达式内部，而现行 spec 明确禁止——解除这个限制，恰恰撞上 2014-04-02 主线留给我们的那句忠告。形态二、三的障碍更深：`Where`/`Select` 的翻译目标是**同步** lambda（`src.Where(Function(x) p(x))`）；谓词返回 `Task(Of Boolean)` 时，`Enumerable.Where` 不接受。要接住它，要么编译器识别"异步谓词"并改走异步运算符（`System.Linq.Async` 的 `WhereAwait`/`SelectAwait` 一类，`Suspect`：外部 .NET 事实，仓库内不可核实），要么引入异步 lambda 的文法——而后者是全新的、更大的语法面。**建议对这三种形态零区分，是它作为"调查"的诚实之处，也是它作为"提案"的真空之处。**

**Q3：`Where Await` 撞 #167 吗？**

不撞 `Return?` 的原文——`Await` 是显式的，不是"细微字符"。但撞 2014-04-02 主线的精神。我们逐字引用那条记录：

> "And so we allowed await in all expression contexts, even though it's bad practice to use them in confusing places like `x[await t1] += await t2;`"

主线允许 `Await` 出现在一切表达式上下文，**同时明示"把它用在令人困惑的地方是坏实践"**。查询表达式的子句内恰恰是教科书级的"confusing place"：`Where Await p(x)` 把一个挂起点藏进声明式构造，单步调试看到的是一行查询、看不到中间挂起点；异常在"被省略的 await 点"与"显式 await 点"的位次不可区分（这条与 `agile-async` 会议拒绝中间省略时的论证同源）。**结论：显式 `Await` 在查询里的形态，即使语法可做，也在"坏实践"区。**

**Q4：`IAsyncEnumerable` 查询——它是 `async-iterator` 的下一层，不是本建议的独立层。**

上一场我们把 `Await Each`（消费端）解禁为 `Consider`、`Async Iterator Function`（生产端）定为 `Consider`（等组合状态机原型）。本建议的"针对 `IAsyncEnumerable` 的查询"——即对异步序列做 `Where`/`Select`/`Order By`——依赖一个前提：**VB 有消费异步序列的循环**（`Await Each`）。没有消费端，查询异步序列连"在哪儿求值"都没有锚点；有了消费端，还要回答"查询翻译到哪套运算符"（`IEnumerable` 运算符还是 `System.Linq.Async` 的异步运算符）。这是一个真实但**有序依赖**的调查项：排在 `Await Each` 之后。我们不会在消费端落地之前为它单独设计语法。

**Q5：垂直存储簇（CTE / 地理空间 / No-SQL / 时序）——为什么是库领地？**

这簇共享一个特征：**每个方向的语义都由外部数据提供程序定义**。CTE 是 SQL 概念（`WITH ... AS ...`），语言不知道也不该知道某个 SQL 方言怎么翻译 `WITH`；地理空间谓词（`ST_Within`、距离、多边形相交）由空间库（如 NetTopologySuite）定义；No-SQL 的文档查询由文档数据库的提供程序定义；时序的窗口聚合（时间分桶、滑动窗口）由时序库定义。这与 `query-comprehensions` 会议对 `Include` 的裁定完全同构——我们在那份纪要里说得很直白：

> "这个关键字的全部语义都由外部库定义。我们对'库功能长成语言关键字'非常警惕。"

`Include` 被拒，因为它是 EF 的功能披着语言关键字的壳。CTE/地理/No-SQL/时序子句是同一件事的四个变体：语言为提供程序的语义造关键字，等于把第四方的版本演进焊进语言文法。而且 C# 一个都没做（主线"默认跟随 C#"安静地站在库这一边；对 `..\..\..\vblang/meetings/` 检索 `CTE`/`Geospatial`/`Temporal`/`No-SQL`/`PLINQ`/`AsParallel` 均零命中，我们已核实——只有 2018-02-21 "parallel the IL names" 的英文单词用法）。**结论：垂直簇整体 Reject（作为语言子句）。**

**Q6：Anthony 的"无需退回扩展方法与 Lambda"——这句话对不对？**

We think 这句话**一半对、一半错**。对的一半：当一个场景足够通用、值得成为标准运算符时，它就该成为查询子句——这正是 `query-enhancements` 会议裁定的方向（目标类型化 `Select` = Consider、`From` 元组解构 = Consider）。错的一半：把"扩展方法与 Lambda"描述为 fallback 是方向性错误。**扩展方法与 Lambda 是查询理解的可扩展性边界，不是它的失败**——恰恰是它们，让 PLINQ、异步流、EF、空间库都能在不改语言的前提下接入查询。删掉这个边界去换"平滑表达"，等于用语言的表面积去换库的灵活度，原则 #3（不引入第二种做事方式）会直接否决。Anthony 自己第 2.3 章的通配符 lambda（`.Include(*.Profile.Avatar)`）就是这条边界的正面案例——它让扩展方法变得比子句更简洁，而不是需要被消灭。

**Q7：绑定与语义模型——`Where Await` 的 lambda 返回什么？**

形态二若做，`Where Await IsFlaggedAsync(item)` 的翻译目标是异步谓词运算符（接受 `Func(Of T, Task(Of Boolean))`）；语义模型里该子句绑定的符号就不是 `Enumerable.Where`，而是某个异步运算符——补全与签名帮助要为新运算符做一整块新 UI。形态三同理。范围变量（`item`）的绑定倒是清晰（查询作用域），但"子句绑定到什么运算符"这一问，是查询理解第一次需要"两套翻译路径"（同步 / 异步）的开关——编译器要按 `Await` 的出现位置选择翻译表。可做，但是 Roslyn 全栈特性，且建议零设计。

**Q8：Option Strict 分叉——宽松路径下并行与异步都会静默消失。**

`Option Strict Off` 下 `items` 可为 `Object`：`AsParallel()` 扩展方法对 `Object` 不适用，并行路径在宽松模式下**静默不生效**——同样的源码，严格模式并行、宽松模式串行，而查询结果在"元素顺序"上可能不同（PLINQ `Where` 默认无序）。异步形态同理：`Object` 型操作数的 `Await` 按 spec 延迟到运行时（`meeting-agile-async.md` 已述），谓词异步化在宽松路径下退化。**两路径行为必然分叉，除非明文规定这些子句仅限早期绑定。** 建议对两路径只字未提。

**Q9：与上一场 `parallel-extensions` 的移交怎么接？**

上一场 RESOLUTION #3 把查询级并行（PROPOSAL B）正式划归本建议，并注明"避免两份描述同一调查的 inactive 文档"。本场接住移交后给出的结论是：**该调查项在语言层面可以关闭**——`AsParallel()` 已是查询理解的合法源，查询级并行无需新语法；若未来有人 champion 一个 `Parallel` 子句，他必须先过 #167/原则 #3 的闸门（见 Q1）。**移交至此结清。**

### 深度追问：LDM 拷问清单

We 用评价标准第五部分的追问清单逐条过。

#### 1. 语法 / 文法歧义

- **`Await` 在查询内的三种形态**（Q2）各自需要不同的文法判定：`From ... In Await expr` 是"源表达式含 await"（要重定义惰性），`Where Await expr` 是"谓词异步化"（要新运算符或异步 lambda），`Select Await expr` 是"投影异步化"。同一个 `Await` 关键字，三个位置三种语义，parser 要在子句内做位置敏感的上下文判定。建议无 BNF。
- **`Parallel` 子句的摆放**：`Parallel From`（修饰整个查询）与 `From x In src Parallel`（修饰子句）读法不同，且 `Parallel` 是英文常用词、`Parallel.For` 是高频成员访问（上一场已述）——上下文关键字的位置消歧是必须设计而非默认可得的东西。
- 无 body 区分：`Await Each`（`async-iterator` 的语句）与 `Await` 在查询子句里的用法是两个文法位置，不得互相污染——但目前两者都只是名字。

#### 2. 角案例与边界语义

- **惰性 vs 急切**：形态一的 `From row In Await GetRecentRowsAsync()`——源何时求值？查询构造时（急切）还是枚举时（惰性）？急切破坏 LINQ 的延迟执行承诺；惰性需要异步迭代器。这是"`Await` 查询"的第一个语义分岔，建议没问。
- **异步谓词的顺序保证**：LINQ-to-Objects 的谓词按元素顺序执行；异步谓词天然可能交错/并发（若走并行路径）或串行 await（若走顺序路径）。`Where` 在异步序列上保不保序，翻译路径定不了就没法编译任何示例。
- **异常聚合**：PLINQ 抛 `AggregateException`；异步序列的异常发生在 `MoveNextAsync`。声明式查询的 `Try`/`Catch` 边界在两种执行模型下都变了，而查询理解没有异常边界语法。
- **`IAsyncEnumerable` 查询的取消**：`Await Each` 的取消问题（序列内建 vs `WithCancellation`，上一场 OPEN QUESTION）在查询层同样存在——查询中途取消谁调用 `DisposeAsync`？语言无表达。
- **`From x In items.AsParallel()` 的排序**：`Order By` 之后的并行查询是"无序的中间步骤 + 有序的最终输出"，读者从源码读到的顺序直觉与运行时不一致。

#### 3. 作用域与绑定

- 范围变量绑定在异步查询里依然是 `RangeVariableSymbol`，这是查询理解的既定语义，不受影响。
- 受影响的是**子句绑定的运算符**：`Where` 同步绑 `Enumerable.Where`/`Queryable.Where`，异步版要绑异步运算符；语义模型要在同一子句上按上下文返回两套符号。这是语义模型第一次为查询引入"翻译路径开关"。
- `Parallel` 若做子句，`src` 的绑定从 `IEnumerable(Of T)` 变成 `ParallelQuery(Of T)`，`Select` 的返回类型不变但执行模型变了——语义模型怎么把"执行模型"暴露给 IDE，未定义。

#### 4. 与既有特性的交互

- **与 `Await` 的现行限制**：spec 禁止 `Await` 出现在查询表达式内（`meeting-agile-async.md` 归纳）。解除是 error→program，安全；但"哪些子句位置允许"本身就是设计，且与 2014-04-02"全上下文但坏实践"的框架冲突。
- **与 `async-iterator`（`Await Each`）**：`IAsyncEnumerable` 查询是消费端 `Await Each` 的下一层；没有消费端，异步查询没有求值锚点。必须按序依赖。
- **与 `require-await-call`（强制显式 `Await`）**：异步查询的"未消费"形态——一个对 `IAsyncEnumerable` 的查询对象在枚举前是惰性的，不是"被忘记的 Task"；require-await 的诊断语义在查询对象上不成立（查询未被枚举不算漏用）。两条建议需要明确这条边界。
- **与 `agile-async` / `configureawait-options`**：异步查询每次 `MoveNextAsync`/`SelectAwait` 的 `ConfigureAwait` 行为随 `agile-async` 统一（上一场 Q6 已倾向"默认捕获、由 agile 统一放宽"）；`configureawait-options`（§18.15）的"未观测异常"配置同样适用。三条异步建议共享同一片配置面。
- **与 `query-enhancements`（已拆件裁定）**：该建议的 Alternatives 说"通过增强现有查询运算符间接覆盖"——而 `query-enhancements` 会议已裁：④ 大概率已由主线修复、② `Select` 聚合 Reject、①③ Consider。**"增强现有运算符"这条路本身已部分关闭**，本建议引用它时没有对表。
- **与 PLINQ 库语义**：`From x In items.AsParallel()` 组合后，`Where`/`Order By`/`Take` 绑定到 PLINQ 重载——这些是库语义（聚合异常、无序），查询理解不感知。

#### 5. Breaking change 与兼容性

- 现状无语法：零破坏。
- 若激活形态一（解除"`Await` 不得在查询内"）：error→program，无既有合法代码破坏；但"源上 await 使惰性查询急切化"若被采纳是执行时机变化，必须显式排除。
- 若激活 `Parallel` 子句：`Parallel` 上下文关键字的破坏面已由上一场列明（`Dim Parallel As Integer`、`SomeType.Parallel`、`Parallel.For` 成员访问重解析）。
- 异步运算符路径：`Where` 子句在"发现谓词含 `Await`"时改绑异步运算符——**重载解析依赖类型细节**（谓词返回类型是否 awaitable），库作者改一个返回类型就会静默改变既有代码的绑定路径。这是 `agile-async` 拒绝省略时列过的同一类脆弱性。

#### 6. Option Strict / 编译选项分叉

Q8 已述。两条路径必须行为一致：并行与异步子句仅限早期绑定；`Option Strict Off` 下 `Object` 源要么报诊断、要么明文规定"不启用并行/异步翻译"。建议零讨论。

#### 7. IDE / IntelliSense 影响

- 异步子句的补全：`Where` 后输入 `Await` 时，IDE 要切换"异步谓词"上下文；`Select` 的返回类型提示从 `T` 变为 `Task(Of T)` 再变回 `T`（await 后），InfoTip 语义要做两遍。
- 调试：异步查询的单步——中间挂起点、`MoveNextAsync` 的位次、异常堆栈——都需要新的可视化，`async-iterator` 会议的 IDE 追问在这里逐条复发。
- 并行查询的调试是已知痛点（并行栈、断点在哪个线程命中），上一场已述。

#### 8. 数据 / 普遍性

没有数据。主线 2018-05-30 的客户画像我们逐字引用：

> "We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now."

"数十万安静客户"的主力是业务 CRUD，不是 PLINQ 数据科学、不是地理空间、不是时序分析。六个方向里，`IAsyncEnumerable` 查询的普遍性最可辩护（流式异步是 .NET Core 3.0+ 的高频模式，但上一场已承认"没有 VB 侧量化数据"）；`Await` 查询是次高频但机制未定；其余四个（含并行）是明确的利基。`Suspect`：这份登记册服务的对象与主线客户画像错位，且没有一条量化数据可以校准错位的程度。

#### 9. 更简替代

- **并行**：`AsParallel()`——存在、能编译、组合良好，是压倒性替代。语言介入无增量。
- **异步序列**：`System.Linq.Async` 的运算符组合（`Suspect`：外部事实），配合 `Await Each`（Consider）消费；语言语法只做消费端，不做查询翻译。
- **`Await` 查询**：先把源取到（`Let rows = Await GetRecentRowsAsync()`），再对内存序列查询；谓词异步改为先物化异步结果再同步过滤。样板真实但有限，且显式、可调试。
- **垂直存储**：各领域的查询提供程序（EF 的 SQL 翻译、空间库、No-SQL 驱动、时序库）。语言无替代义务。
- Analyzer：提醒"此处查询可并行"或"此谓词可异步化"，比语言语法便宜得多。

#### 10. 复杂度 / 成本 / 优先级

- 并行：零（已解决）。
- `Await` 查询：Roslyn 全栈（文法 + 位置判定 + 两套翻译路径 + 异步运算符契约 + 语义模型开关 + IDE），且核心语义（惰性/急切、保序、异常）未定型。成本最高、机制最空。
- `IAsyncEnumerable` 查询：排在 `Await Each` 之后，消费端原型是前置。成本中-高。
- 垂直存储：语言单方面做不完（需提供程序契约），成本特性级、价值利基。
- 优先级排序：`async-iterator`（上一场 Consider）> `query-enhancements` 的①③（Consider）> 本建议的任何方向。**本建议整体排不进当前序列。**

#### 11. 运行时 / CLR 硬约束

- 无 CLR 障碍：PLINQ、异步运算符、`IAsyncEnumerable` 全是既有 BCL 类型与方法；desugar 到库调用即可，无 PEVerify 问题、无存储规则问题。
- 平台约束：`IAsyncEnumerable` 需 netstandard2.1+ / .NET Core 3.0+（上一场已列为 `async-iterator` 的前置）；异步运算符（`System.Linq.Async`）是第三方/预览包，契约稳定度未定。
- 正因为无 CLR 硬约束，**语言必须介入的理由进一步削弱**——运行时和库已经把机制备好了，缺的只是"要不要给语法"，而给语法的代价是上述全部设计债。

#### 12. 值不值得做

逐簇打分：

- **并行簇**：价值（零增量）× 成本（零）× 风险（`Parallel` 子句的破坏面与语义雷区，上一场已逐条列明）——**不值得，且不需要。** 关闭。
- **`Await` 查询**：价值（源上 await 消除 `Let rows = Await ...` 一行的样板；谓词异步化打开新场景但无数据支撑）× 成本（Roslyn 全栈 + 两套翻译路径）× 风险（#167 精神、2014-04-02"坏实践"、重载解析脆弱、惰性/急切语义未定）——**机制未定、价值未证、风险有先例。Table。**
- **`IAsyncEnumerable` 查询**：价值（真实 parity gap，流式异步）× 成本（中-高）× 风险（低）——**方向真，但依赖 `Await Each`。挂在异步族之后。Table（条件于 async-iterator）。**
- **垂直存储簇**：价值（利基）× 成本（特性级 × 4 个领域）× 风险（Include 判例、提供程序单点依赖、原则 #3）——**Reject（作为语言子句）。**

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **不引入"第二种做事方式"（原则 #3）**——本登记册最重的一刀。`Parallel` 子句是 `AsParallel()` 的第二种写法；`Await` 谓词子句是异步运算符（或 `Let rows = Await ...`）的第二种写法；四个垂直子句是四个提供程序 API 的第二种写法。三重复合违规，`query-comprehensions` 的 `Include` 判例逐字适用。
- **避免隐蔽语义变化（原则 #7）**——`Parallel` 子句把顺序查询变成无序并发（上一场 #167 引文适用其精神）；`Await` 在子句内把挂起点藏进声明式构造（2014-04-02"坏实践"区）。这是两个最强的扣分项。
- **默认跟随 C#（原则 #4）**——C# 没有查询里的 `await`、没有 `Parallel` 查询子句；它的答案同样是把 `AsParallel()` 和 `System.Linq.Async` 留给库。无"充分理由"偏离。
- **读起来像英语（原则 #5）**——`From c In cities.AsParallel() Where c.Population > ...` 读起来顺；`Where Await IsFlaggedAsync(item)` 读起来别扭（"where await..."在英语里是残缺句）。措辞分界明显。
- **不为边缘场景加特性（原则 #6）**——垂直簇与并行在 VB 客户画像里都是利基，且无数据。败。
- **消除常见样板（原则 #9）**——唯一诚实的命中：源上 `Await` 消除 `Let rows = Await GetRecentRowsAsync()` 一行样板。但样板消除的代价是整套两路径翻译，且替代（显式 `Let`）只多一行。得不偿失。
- **永不破坏既有代码（原则 #1）**——现状无语法，零破坏；激活后形态一/异步路径是 error→program，安全但需 `langversion` 门控。通过。
- **与主线关系（对照表 2.3）**——**本建议在对照表无对应行，是 Anthony 独立延伸。** 主线对 `PLINQ`/`AsParallel`/`IAsyncEnumerable`/`CTE`/`Geospatial`/`Temporal` 的检索零命中（已核实，仅 2018-02-21 英文单词用法）。主线对查询理解的既定方向是"翻译为标准运算符"（2014-02-17）；对异步的既定节奏是"等平台"（2017-08-09："The feature is approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`."）；对 await 位置的既定框架是"全上下文允许但明示坏实践"（2014-04-02）。三条主线锚点全部指向同一个方向：**查询/异步的扩展走库与平台，语言不越界。** 与 `async-iterator`（消费端 Consider）、`query-enhancements`（①③ Consider）、`parallel-extensions`（查询级并行移交本建议后本场结清）交叉。

### RESOLUTION:

1. **拆分登记册（采纳 PROPOSAL B 的框架）。** 六个方向不是一份建议，是六份未设计调查的登记表。按 `query-comprehensions` / `query-enhancements` 先例拆开逐个处置，不再作为整体裁定。
2. **并行簇 — 关闭（作为语言调查项），移交结清。** `From x In items.AsParallel() ...` 今天即可编译（`Probably`，见 TODO 运行验证）；查询级并行由库解决，语言介入是减分（原则 #3 第二种方式、#7 隐蔽语义、`Parallel` 关键字撞 `Parallel.For` 成员访问）。接受 `parallel-extensions` 的移交并结清：若未来有人 champion 一个 `Parallel` 查询子句，必须先过 #167 / 原则 #3 / 破坏面三道闸。**无激活信号可开**——这不是"等待"而是"不需要"。
3. **`Await` 查询 — Table（机制未定）。** 三种形态（源上/谓词上/投影上）语义完全不同，建议零区分；源上 Await 破坏延迟执行、谓词/投影 Await 需要两套翻译路径或异步 lambda，均为 Roslyn 全栈成本；且撞 2014-04-02"坏实践"区与 `agile-async` 已述的重载解析脆弱性。**复活信号三条**：(a) 明确 `Await` 在查询中的位置与语义（源=急切 or 惰性异步迭代器；谓词/投影=异步运算符契约）；(b) 异步 lambda 或异步运算符翻译路径的既定文法；(c) 真实使用数据。在此之前，替代方案是显式 `Let rows = Await GetRecentRowsAsync()` 再查询。
4. **`IAsyncEnumerable` 查询 — Table（条件于 `async-iterator`）。** 方向真实（流式异步是 parity gap），但它是 `Await Each`（上一场 Consider）的下一层——没有消费端就没有求值锚点。**复活信号**：`Await Each` 消费端原型落地并升 Active 后，随之为查询理解设计异步运算符翻译路径；与 `require-await-call` 明确"未枚举的查询对象 ≠ 被忘记的 Task"边界。
5. **垂直存储簇（CTE / 地理空间 / No-SQL / 时序）— Reject（作为语言子句）。** 语义全部由外部提供程序定义，是 `Include` 判例的四个变体（"库功能长成语言关键字"）；C# 一个都没做（主线默认跟随）；无数据、利基、成本特性级 × 4。**若未来任何领域数据形态证明需要语言介入，必须先给提供程序契约与使用数据。**
6. **采纳 PROPOSAL E——把"查询理解可扩展性"写为正向原则。** 子句跟随标准运算符；领域数据形态住进提供程序；扩展方法与 Lambda 是设计好的可扩展性边界，不是需要消灭的退路。这一条以备忘形式记录，不伴随任何语法。它同时回填了 Anthony 原句里最有价值的那半句——"ensure that the query comprehension syntax can smoothly express common use-cases"——以原则而非语法的形态落地。
7. **PROPOSAL A（全量激活）与 PROPOSAL D（什么都不做）都作为极端选项留档。** A 是字面读法、当场否决；D 是本场实际走向的锚（保持 inactive），但我们不接受"什么都不做就完事"——因为拆分后有三个子项各有去处（关闭/Table/Reject），不是一体地"不做"。

### Implication:

- 在本建议文档头部标注 **LDM Reviewed: No Plans（保持 inactive，已拆件）**，注明与 `parallel-extensions` 的移交结清、`async-iterator` 的条件依赖、`query-comprehensions` 的库/语言边界判例。
- 把"查询理解可扩展性"原则写入查询家族备忘（与 `query-enhancements` / `query-comprehensions` / `async-iterator` 共享），作为未来任何查询子句提案的准入门槛。
- 与 `async-iterator` 团队对表：`IAsyncEnumerable` 查询翻译路径登记为其消费端原型的后续工作项；与 `require-await-call` 团队对表"未枚举查询对象"的边界。
- 与 `query-enhancements` 团队对表：本建议 Alternatives 引用的"增强现有查询运算符"路径已被该会议部分关闭（② Reject），引用需更新。
- 替换占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）为"无原型"说明。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**`System.Linq.Async` 的运算符集合（`WhereAwait`/`SelectAwait` 等）与 C# 8 的 `await foreach`/异步流细节为外部 .NET 事实**，仓库内无法核实；若未来设计异步翻译路径，需外部核实并在设计中引用。
- `OPEN QUESTIONS`：`Await` 在查询源上的"急切 vs 惰性异步迭代器"语义——若未来激活，必须先定；我们倾向惰性异步迭代器（复用 `async-iterator` 的组合状态机），但不在此定案。
- `OPEN QUESTIONS`：异步查询的保序与异常聚合语义——`Where` 异步谓词在顺序/并发两种执行模型下是否保序，未定。
- `TODO`：编译验证 `From c In cities.AsParallel() Where c.Population > 1_000_000 Order By c.Population Descending Take 10` 在基础编译器下是否通过（并行簇"已解决"论断的 `Probably` → `已运行` 升级）。
- `TODO`：量化 VB 生态中并行/异步序列/`Await` 查询三类场景的真实占比，为异步簇的复活评估补数据。
- `Follow-up`：把垂直存储簇的"提供程序契约 + 使用数据"复活条件写入本建议文档头部；把"查询理解可扩展性"原则写入查询家族备忘。

### 状态

- **LDM 状态：LDM Reviewed: No Plans（保持 inactive，拆件处置）。**
- **三态判定：Table（整体）** — 六项拆开后：并行簇**关闭**（库已解决，移交结清）；`Await` 查询 **Table**（机制未定，复活信号 = 位置语义 + 异步翻译路径 + 数据）；`IAsyncEnumerable` 查询 **Table**（条件于 `Await Each` 落地）；垂直存储簇 **Reject**（作为语言子句，Include 判例）。**激活所需信号因簇而异，但共同前提是：先有语法，再有语法——一份没有语法的登记册，无论名字多响亮，都只是登记册。**

---

## 附录：特性评价

# 建议评价报告：proposal-plinq-async-queries.md

## 评价对象

- 建议：proposal-plinq-async-queries.md — PLINQ / 异步查询 / 更多查询运算符（六个调查方向登记册）
- 来源：Anthony 原文 §18.14（`..\..\AnthonyDesign_wordpress.txt` L3034–3038），全文一句话："Parallel LINQ, queries that use `Await`, queries against `IAsyncEnumerable` are areas to investigate..."；本建议将其翻译为六个方向的登记册，未添加任何语法
- 配方目标：让查询理解语法平滑表达并行、异步、`IAsyncEnumerable` 及 CTE/地理空间/No-SQL/时序等常见用例，无需退回扩展方法与 Lambda

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。六个方向无语法、无验收标准；唯一"示例"（`From x In items.AsParallel()` 等）是概念示意且自标"非完整查询"，其中并行的概念例恰好演示"无需语言改进"而非改进；异步例不可编译（spec 禁止 `Await` 在查询内） | 已提供（仅原文一句观察 + 概念示意） | 无原型、无运行证据，效果证据封顶；并行方向的效果已被库达成（改进为零）；异步方向的效果无示例可演示 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。六个强无关调查方向捆绑（红色红旗）；隐含的特性方向（`Parallel` 子句、`Await` 子句、垂直子句）全部是"库能力的语言壳"，无一做 VB 化改造；无任何 VB 基因增量（对照 `Await Each` 的措辞纯正与本建议的空壳形成反差） | 已检查 | 与原则 #3（第二种做事方式）三重冲突；垂直簇 = `Include` 判例的四个变体；捆绑使"该关闭的"与"该考虑的"共享一个判断 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、诚实标注"原文未给出任何语法示例"（不虚构语法——重要加分）、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design 实质为空（六个方向的语法全部未定）、Drawbacks/Alternatives 是撰写者的推演而非原文、无文法、无 Compatibility、无 Option Strict 分叉 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；未引用决定性事实（`AsParallel()`/`System.Linq.Async` 已存在）；未对表 `query-enhancements`（其 Alternatives 引用的"增强查询运算符"路径已部分关闭） |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风（演化一致性）受损：异步簇与 `async-iterator`（Consider）/`require-await-call`/`agile-async` 的方向交互未定义，垂直簇与 `Include` 判例直接冲突；暗风险：`Parallel` 子句若激活带破坏面（上一场已述）、异步翻译路径重载解析脆弱；文档零权衡 | 已检查（预测待定） | 风/暗双损无应对；对 VBScript.NET 差异化无助益（C# 已把答案留给库）；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。主要材料可辨认：Anthony §18.14（准确引述）；但未标注：PLINQ/`System.Linq.Async`/C# 生态的既有对应（决定性成分）、`parallel-extensions` 的查询级并行移交关系、`query-comprehensions` 的库/语言边界判例、"queries that use Await" 与已 Reject 的中间省略（§11）在翻译路径上的隐性关联 | 已检查 | 来源标注不全；成分（已存在的库方案）与影响（"需要语言语法"）有偏差；无杂质但关键前例大面积欠标注 |

## 设计原则对照

- **与 VB 基因：偏离为主。** 唯一命中是原则 #1（不破坏——现状无语法即零破坏）；原则 #3（第二种做事方式）三重违规（`Parallel`/`Await`/垂直子句），原则 #7（隐蔽语义：`Parallel` 顺序→无序、`Await` 挂起点入声明式构造）与原则 #6（利基 + 无数据）失败；原则 #4（默认跟随 C#）安静支持库层；唯一正向是原则 #9 的极小命中（源上 `Await` 消一行样板），但代价远超价值。
- **与主线关系：Anthony 独立延伸，主线零讨论。** 对照表 2.3 无对应行；vblang 主线对 `PLINQ`/`AsParallel`/`IAsyncEnumerable`/`CTE`/`Geospatial`/`Temporal` 检索零命中（已核实，仅 2018-02-21 英文单词用法）。主线三条锚点（2014-02-17 查询=翻译标准运算符；2014-04-02 await 全上下文但坏实践；2017-08-09 异步=等平台）全部指向"扩展走库与平台"。与 `async-iterator`（消费端 Consider，本建议的异步簇是其后续层）、`query-enhancements`（①③ Consider）、`parallel-extensions`（查询级并行移交本建议后本场结清）交叉。
- **破坏性变更：无（现状，因为无语法）。** 若激活形态一（解除"`Await` 不得在查询内"）为 error→program，安全但需 `langversion` 门控；若激活 `Parallel` 子句，`Parallel` 上下文关键字破坏面（含 `Parallel.For` 成员访问重解析）如上一场所述；异步翻译路径的重载解析脆弱性（库演化改绑定）是间接风险。

## 总评

- **达成程度：未达成（作为提案）。** 它是六项调查的登记册，不是提案：无语法、无示例、无数据、无设计。但区分度高于 `parallel-extensions`——异步簇有机制性价值（虽被 `async-iterator` 部分接管），并行簇"已被库解决"是可核实的正面结论，垂直簇应拒绝。原文最有价值的是那句原则性观察（"smoothly express common use-cases"），它应以原则而非语法的形态落地。
- **LDM 三态建议：Table（整体，拆件处置）。** 并行簇**关闭**（`AsParallel()` 已解决，无激活信号）；`Await` 查询 **Table**（机制未定：位置语义 + 异步翻译路径 + 数据）；`IAsyncEnumerable` 查询 **Table**（条件于 `Await Each` 消费端落地）；垂直存储簇 **Reject**（作为语言子句，Include 判例）。
- **主要问题**：(1) 六项捆绑，血缘/成本/判例关系互相矛盾；(2) 无语法，核心 `Await` 位置与语义完全未定；(3) 决定性事实缺失——`AsParallel()` 与 `System.Linq.Async` 已覆盖大部分，建议未核实；(4) 与 `async-iterator`/`parallel-extensions`/`query-enhancements`/`query-comprehensions` 的重叠与依赖未声明；(5) 无任何量化数据，"数十万安静客户"画像下多数方向是利基。

## 返工建议

- **范围切分**：六项拆开。并行项关闭并标注"已被 `AsParallel()` 解决（编译验证后升级证据级）"；异步两项合并进 `async-iterator` 的后续工作项（`IAsyncEnumerable` 查询翻译路径登记为其消费端原型的后续层）；垂直四项移出语言提案，标注"提供程序领地"，或转为"查询理解可扩展性"原则备忘。
- **补充章节**（若保留异步项）：`Await` 在查询中的位置与语义（源=急切 or 惰性异步迭代器；谓词/投影=异步运算符契约）；文法 BNF（三种形态的解析判定）；两套翻译路径（同步/异步）的开关规则；Option Strict On/Off 双路径行为（并行/异步子句仅限早期绑定）；Compatibility/breaking-change（error→program + `langversion` 门控）。
- **补充证据**：编译验证 `From c In cities.AsParallel() ...`（并行"已解决"由 `Probably` 升 `已运行`）；`System.Linq.Async` 运算符集合的外部核实；VB 生态三类场景（并行/异步序列/`Await` 查询）的占比数据。
- **未决问题处理**：与 `async-iterator` 团队对表 `Await Each` 落地顺序与异步翻译路径归属；与 `require-await-call` 团队明确"未枚举查询对象 ≠ 被忘记的 Task"；把"查询理解可扩展性"原则写入查询家族备忘作为准入门槛；替换占位链接。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\..\csharplang-index.md`（来源 `..\..\..\csharplang`，dotnet/csharplang 官方仓库镜像）补写 C# 生态对照。先说实话：本提案不是 C# 互操作/元数据主题，索引的 T1–T8（Span、指针、COM、AOT、source-gen）大多不直接命中；它命中的是 C# 侧一个更基础的问题——**C# 把「并行 + 异步查询」分别放在哪一层：语言、BCL 还是库？** 幸运的是，C# 文档对这个问题**有白纸黑字的正面回答**，而且与本场 RESOLUTION 高度重合。这是本附录的核心发现，也是它比上一场（parallel-extensions）「关系弱」更强的理由。

### 相关 C# 现实方向

C# 生态对并行/异步查询的答卷是**三分层，且每一层都有文档证据**：

1. **异步流 = 「消费循环进语言 + 接口进 BCL + 查询运算符留给库」。** C# 8 async streams 提案（`proposals\csharp-8.0\async-streams.md`，Summary 节）逐字：

   > "We should rectify this by allowing for `await` to be used in a new form of `async` iterator, one that returns an `IAsyncEnumerable<T>` or `IAsyncEnumerator<T>` rather than an `IEnumerable<T>` or `IEnumerator<T>`, with `IAsyncEnumerable<T>` consumable in a new `await foreach`."

   `IAsyncEnumerable<T>`/`IAsyncEnumerator<T>`/`IAsyncDisposable` 进 core library（`System.Collections.Generic`/`System`），`await foreach` 是语言消费循环，查询运算符不属语言。

2. **LINQ-over-`IAsyncEnumerable` 的运算符面是「库层（Ix），语言明确不重复」。** async-streams.md LINQ 节对异步运算符规模的估算逐字：

   > "That is a staggering number of APIs, with the potential for even more when extension libraries like Interactive Extensions (Ix) are considered. But Ix already has an implementation of many of these, and there doesn't seem to be a great reason to duplicate that work; we should instead help the community improve Ix and recommend it for when developers want to use LINQ with `IAsyncEnumerable<T>`."

   C# 的设计结论是**帮社区改进 Ix（即后来的 `System.Linq.Async`）并推荐它**，而不是在 BCL 或语言里复制异步运算符。这与本场 Q2 把「谓词/投影异步化」的翻译目标指到 `System.Linq.Async`（`Suspect`：外部事实）的方向一致，且给出了 C# 侧为何如此分层的理由（~600 个重载的 API 面，见该节前文）。

3. **C# 明确拒绝「`await` 出现在查询子句里」——本场 Q2/Q3 的结论在 C# 侧有逐字同源。** async-streams.md LINQ 节对查询理解里的 `await` 有两段正面表述：

   > "However, there is no query comprehension syntax that supports using `await` in the clauses"

   > "or to enabling `await` to be used directly in expressions, such as by supporting `async from`. However, it's unlikely a design here would impact the rest of the feature set one way or the other, and this isn't a particularly high-value thing to invest in right now, so the proposal is to do nothing additional here right now."

   注意：C# 把「`await` in query clauses」与「`async from`」都正面评估过，然后选择「现在不做」（do nothing additional here right now）。这不是疏漏，是白纸黑字的取舍——它把本场 `Await` 查询的「坏实践区」（2014-04-02）与「机制未定」（Table）都背书到了 C# 生态侧。

4. **C# 自己扩展查询理解的方式 = 新增标准运算符，不是新关键字；且十多年才动一次。** `proposals\left-right-join-in-query-expressions.md`（champion issue #8947，仍在 `proposals\` 根目录、未归档 csharp-X.0）三处逐字：

   > "It's worth noting that C# query expression support for LINQ hasn't evolved in a long time - this would be the first change in quite a while."

   > "Note that the proposed `join` modifiers do not require any LINQ expression tree changes, as they're represented via existing MethodCallExpression's which reference the new `LeftJoin()` and `RightJoin()` methods. There is thus nothing blocking supporting them from LINQ providers (such as EF Core)."

   > "As noted above, this is the first proposal for evolving C#'s support around LINQ in a long while; there are quite a few other gaps in this area: additional C# query expression clauses (distinct, aggregates, set operations...)"

   关键：C# 查询理解十多年第一次演化，是给既有 `join` 子句加 `left`/`right` 修饰符、翻译到**新的标准运算符方法**（`LeftJoin()`/`RightJoin()`，随 .NET/EF 10 进栈），并刻意声明「零表达式树改动、EF Core 可直接消费」。这就是本场 RESOLUTION 6（PROPOSAL E：子句跟随标准运算符）的 C# 活例。

5. **IQueryable/EF 消费契约的障碍 = C# 自己拒绝了 `async` 进表达式树。** LDM-2015-04-22 Design Review（`meetings\2015\LDM-2015-04-22-Design-Review.md`，Expression Trees 节）逐字：

   > "Expression trees are currently lagging behind the languages in terms of expressiveness. A full scale upgrade seems like an incredibly big investment, and doesn't seem worth the effort. For instance, implementing `dynamic` and `async` faithfully in expression trees would be daunting."

   IQueryable/EF 靠表达式树把查询传给提供程序；C# 判定「`async` 忠实进表达式树 daunting」。这意味着「异步查询」在 EF 这条链路上连 C# 自己都没铺——垂直存储簇「提供程序领地」的判定在 C# 侧有直接证据（索引 T7 亦确认表达式树长期边缘化）。

6. **`await foreach` over dynamic：C# LDM 直接封死。** LDM-2018-05-21（async streams 专题，`meetings\2018\LDM-2018-05-21.md`）逐字：

   > "Block it. For synchronous foreach we resort to the nongeneric `IEnumerable`, but there is no nongeneric `IAsyncEnumerable`, and there won't be."

   与本场 Q8 的 Option Strict 分叉建议（异步路径仅限早期绑定）同向——C# 在语言层就堵死了动态异步枚举。

### 现实 vs 提案

| 本场判定/形态 | C# 现实 | 判定 |
|---|---|---|
| 并行簇 — 关闭（`AsParallel()` 已解决） | PLINQ 是 BCL 库，语言零投入；C# 15/16 路线图无并行（上一场附录已核实） | **兼容** —— 双方都把并行留在库层 |
| `Await` 查询 — Table（机制未定） | async-streams.md 明确评估 `async from` / `await` in clauses 后选择「现在不做」 | **兼容（且是强背书）** —— C# 不仅没做，还正面写过「not a particularly high-value thing to invest in right now」；本场 Table 与 C# 现实完全重合 |
| `IAsyncEnumerable` 查询 — Table（条件于 `Await Each`） | 运算符留给 Ix / `System.Linq.Async`，语言只做 `await foreach` 消费循环 | **兼容 + 需桥接** —— 分层一致；但 VB 需先有消费端 `Await Each`，且要识别 `IAsyncEnumerable`/`ValueTask` 等 BCL 契约与异步迭代器元数据 |
| 垂直存储簇 — Reject（作为语言子句） | C# 查询理解首次演化只做 left/right join（新运算符），CTE/地理/No-SQL/时序零命中；async 进表达式树被拒 | **兼容** —— C# 同样把领域数据形态留给提供程序（EF/空间库/No-SQL/时序库） |
| PROPOSAL E — 查询理解可扩展性原则 | left/right join = 新 join 修饰符翻译到新标准运算符 `LeftJoin()`/`RightJoin()`，零表达式树改动 | **同构/需桥接** —— C# 恰好用本场 PROPOSAL E 的方式演化查询理解；VB 查询翻译表需认识新增标准运算符 |

**总体判定：本提案与 C# interop 关系中等偏强（比 parallel-extensions 强）。** 分歧不在技术互操作而在「语言/库分层」：C# 已在文档层面正面定过「异步查询运算符在库、消费循环在语言、`await` 不进子句、领域形态进提供程序」的答卷，与本场 RESOLUTION 2–6 高度重合。**本场没有一处判定与 C# 现实冲突，四处直接兼容，两处需桥接（元数据识别 + 新标准运算符翻译表）。**

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态：异步查询路径仅限早期绑定，C# 有直接先例。** Q8 的 Option Strict 分叉建议从「推断」升格为「有先例」——C# LDM 对 `await foreach` over dynamic 的回答是逐字的「Block it」（无 nongeneric `IAsyncEnumerable`，且不打算有）。VBScript.NET 对 `IAsyncEnumerable` 的消费与查询翻译应限定 `Option Strict On`/早期绑定；宽松路径下 `Object` 源要么报诊断、要么明文降级为同步。
- **source-gen 桥（索引 T6）：异步翻译路径用编译期 lowering 交付，不引入运行时「两套查询引擎」。** 若未来 `Await Each` 落地后做 `IAsyncEnumerable` 查询翻译，正确形态是「查询表达式 → 既有异步运算符链（`System.Linq.Async` 的 `Where`/`Select` 一类）的编译期 lowering / source-gen」——与 C#「运算符在库、语言只做 lowering」的分层一致，也符合索引 T6「编译期生成替代运行时动态」。零运行时新依赖。
- **识别新元数据：异步侧是重头，并行侧几乎不触发（呼应上一场）。** VB 编译器必须认识 C# 8 后 .NET 的既有异步序列契约：`IAsyncEnumerable<T>`/`IAsyncEnumerator<T>`/`IAsyncDisposable`、`ValueTask<bool>`（`MoveNextAsync`/`DisposeAsync` 返回）、`AsyncIteratorStateMachineAttribute`/`AsyncMethodBuilderAttribute`、`EnumeratorCancellationAttribute`（`WithCancellation` 模式）——这是消费 C# 写的异步序列的硬前提。并行侧（`Parallel`/PLINQ）则无新元数据，决策文件 M8 的 unsafe 元数据桥接要点在异步侧更贴切。
- **查询翻译表对齐 C# 新增标准运算符。** C# 正在把 `LeftJoin()`/`RightJoin()` 随 .NET/EF 10 引进 BCL 并允许查询子句翻译到它们；VBScript.NET 的查询翻译表应把这类「新增标准运算符」视作普通运算符增量而非语法增量——正是本场 RESOLUTION 6 的落地形态，也顺带服务 IQueryable/EF 消费契约（C# 已证明零表达式树改动即可让提供程序消费）。

### 对既有 RESOLUTION / 三态判定的影响

**不推翻任何条目，整体强化，且比上一场多一处「文档级背书」。** C# 侧有白纸黑字的取舍，逐条对表：

- RESOLUTION 2（并行簇关闭）——C# 把并行留在 BCL、语言零投入（上一场附录已核实），本场无新增。
- RESOLUTION 3（`Await` 查询 Table）——**新增最强背书**：C# async-streams 提案明确评估「await in query clauses / async from」并选择「do nothing additional here right now」。C# 都不认为这是高价值投资；VB 的复活信号（位置语义 + 异步翻译路径 + 数据）将需要比 C# 更充分的理由，门槛事实上更高。
- RESOLUTION 4（`IAsyncEnumerable` 查询 Table 条件于 `Await Each`）——C# 把异步查询运算符留给 Ix / `System.Linq.Async`，与「运算符在库层」一致；但 VB 的消费端 `Await Each` 无 C# 对应问题（C# 有 `await foreach`），是 VB 自有的前置，复活信号不变。
- RESOLUTION 5（垂直簇 Reject）——C# 查询理解首次演化只做 left/right join，CTE/地理/No-SQL/时序零命中，且 async 进表达式树被拒——「领域数据形态住进提供程序」在 C# 侧证据完整。
- RESOLUTION 6（PROPOSAL E）——C# 用同构方式演化查询理解，本原则从「ModVB 备忘」升格为「与 C# 实际分层一致的既有方向」，可作为未来查询子句提案的生态侧准入参考。

### 引用纪律与未核实项

- 已核实逐字引用（来源 `..\..\..\csharplang`）：
  - 「We should rectify this by allowing for `await` to be used in a new form of `async` iterator, one that returns an `IAsyncEnumerable<T>` or `IAsyncEnumerator<T>` rather than an `IEnumerable<T>` or `IEnumerator<T>`, with `IAsyncEnumerable<T>` consumable in a new `await foreach`.」→ `proposals\csharp-8.0\async-streams.md`（Summary）。
  - 「That is a staggering number of APIs, with the potential for even more when extension libraries like Interactive Extensions (Ix) are considered. But Ix already has an implementation of many of these, and there doesn't seem to be a great reason to duplicate that work; we should instead help the community improve Ix and recommend it for when developers want to use LINQ with `IAsyncEnumerable<T>`.」→ `proposals\csharp-8.0\async-streams.md`（LINQ 节）。
  - 「However, there is no query comprehension syntax that supports using `await` in the clauses」→ `proposals\csharp-8.0\async-streams.md`（LINQ 节）。
  - 「or to enabling `await` to be used directly in expressions, such as by supporting `async from`. However, it's unlikely a design here would impact the rest of the feature set one way or the other, and this isn't a particularly high-value thing to invest in right now, so the proposal is to do nothing additional here right now.」→ `proposals\csharp-8.0\async-streams.md`（LINQ 节，段末）。
  - 「Expression trees are currently lagging behind the languages in terms of expressiveness. A full scale upgrade seems like an incredibly big investment, and doesn't seem worth the effort. For instance, implementing `dynamic` and `async` faithfully in expression trees would be daunting.」→ `meetings\2015\LDM-2015-04-22-Design-Review.md`（Expression Trees 节）。
  - 「Block it. For synchronous foreach we resort to the nongeneric `IEnumerable`, but there is no nongeneric `IAsyncEnumerable`, and there won't be.」→ `meetings\2018\LDM-2018-05-21.md`（「foreach await over dynamic」节）。
  - 「It's worth noting that C# query expression support for LINQ hasn't evolved in a long time - this would be the first change in quite a while.」→ `proposals\left-right-join-in-query-expressions.md`（Drawbacks）。
  - 「Note that the proposed `join` modifiers do not require any LINQ expression tree changes, as they're represented via existing MethodCallExpression's which reference the new `LeftJoin()` and `RightJoin()` methods. There is thus nothing blocking supporting them from LINQ providers (such as EF Core).」→ `proposals\left-right-join-in-query-expressions.md`（Drawbacks）。
  - 「As noted above, this is the first proposal for evolving C#'s support around LINQ in a long while; there are quite a few other gaps in this area: additional C# query expression clauses (distinct, aggregates, set operations...)」→ `proposals\left-right-join-in-query-expressions.md`（Open questions）。
- 已核实检索性事实（无逐字引用，属计数/位置结论）：`System.Linq.Async` 包名不在 csharplang 仓库正文——async-streams.md 以「Interactive Extensions (Ix)」指代并建议「help the community improve Ix」；left-right-join 提案位于 `proposals\` 根目录（未归档 csharp-X.0），champion issue #8947。
- **Suspect** / **OPEN QUESTIONS**（仓库内不可核实，需外部确认）：
  - `System.Linq.Async` 的具体运算符集合（`WhereAwait`/`SelectAwait` 等）为外部 dotnet 仓库事实，本仓库无正文——与本场 OPEN QUESTION 合并，维持 `Suspect`。
  - left-right-join 提案自称「`LeftJoin()`/`RightJoin()` being introduced into .NET 10」与「first-class left/right join is being introduced into .NET and EF 10」——属跨仓库（dotnet/runtime、dotnet/efcore）事实，本仓库只可核实提案文本、不可核实 API 落地形态。
