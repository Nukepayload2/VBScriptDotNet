# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周这份建议来自 Anthony 第 18 章"实验性想法"（`inactive/`），全文只有一句话（原文 §18.5 "Parallel extensions"）：

> "We've integrated asynchrony but not parallelism. Worth thinking about eventually. For now `Parallel.For` is enough."

所以本次会议的真实议题是：**这个"值得将来思考"的登记条目，是否应该激活，还是继续搁置。** 我们没有语法可审、没有示例可跑、没有动机用例——有的只是一句观察。We 花了整场来回答一个前置问题：**这句话的观察本身，在语言层面成不成立。**

## Agenda

* [Proposal: 并行扩展（Parallel Extensions）](#proposal-并行扩展parallel-extensions)

## Proposal: 并行扩展（Parallel Extensions）

_Related: [vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #167 – Support for Return? construct](https://github.com/dotnet/vblang/issues/167)（#167 决议是本场判例主轴）；ModVB：`proposal-plinq-async-queries.md`（inactive，18.14，并行 LINQ/异步查询调查项）、`proposal-query-enhancements.md`、`proposal-agile-async.md`；Anthony 原文 §18.5（`..\..\AnthonyDesign_wordpress.txt` L2861–2865）_

### 场景与缺口

We started from the claim. 这句话拆成两层：

**观察层——异步与并行在语言里的待遇不对称。** VB 有 `Async`/`Await`：语言级状态机重写、`Await` 表达式、`Async` 方法修饰符。并行没有对应物：`Parallel.For`、`Parallel.ForEach`、PLINQ、`Task.WhenAll` 全是库。观察本身为真，We have no dispute。

**结论层——"因此最终值得语言级集成并行"。** 这里 We stopped. 异步之所以需要语言介入，是因为它**必须重写控制流**：`Await` 之后的代码要搬到状态机里，语言不参与就做不成。并行不需要——`Parallel.For(0, n, Sub(i) ...)` 已经把"并行"这件事封装在库层，循环体仍是普通委托，CLR 原生支持。**这不是"语言忘了并行"，而是"并行没有异步那样非语言不可的理由"。** 观察为真，结论存疑。

### 候选方案

因为建议没有语法，我们只能先枚举"如果要做，它可能长什么样"，再逐一否决。

**PROPOSAL A — 语句级并行循环（`Parallel For` / `Parallel For Each`）。** 在 `For` / `For Each` 前加修饰符，desugar 到 `Parallel.For` / `Parallel.ForEach`：

```vb
' A 的直觉形态（自造，原文无语法）。
Parallel For i = 0 To n - 1
    Process(i)
Next
```

**PROPOSAL B — 查询级并行（PLINQ 进查询理解）。** 让查询表达式天然可并行：`From x In items.AsParallel() ...`，或一个 `Parallel` 查询子句。这是原文 18.14 `proposal-plinq-async-queries.md` 的调查范围，We 认为并行如果真的进语言，最可能的落点就是这里——因为查询子句是声明式的、无副作用的，并行化不破坏既有控制流。

**PROPOSAL C — 并行方法/并行语句块（`Parallel` 修饰方法或块）。** 把一段任意代码丢到线程池并行执行。We rejected this early——它没有任何"循环边界"来界定并行单位，副作用会漏得到处都是，且与既有 `Async` 方法的语义完全正交、互相干扰。

**PROPOSAL D — 什么都不做，保持 inactive。** 维持 `Parallel.For` / TPL / PLINQ 库方案，把本条目继续搁置。这也是 Anthony 自己的倾向（"For now `Parallel.For` is enough"）。

### 权衡：Q&A

- **A vs B：并行该落在语句还是查询？** We think 若并行有价值，它的价值在 B 不在 A。查询理解里的 `Select`/`Where`/`Order By` 是纯函数式的，元素互不依赖，并行化是"免费的加速"——这正是"Treat it as an optimization, not a feature"的姊妹句。而 A 的循环体是任意语句：副作用、共享可变状态、`Exit For`/`Continue For`、`ByRef`——每个都是并行化的语义雷区。**结论：A 是错误层次，B 是唯一有讨论价值的形态，且 B 已经登记在 plinq-async-queries 名下。**

- **A 的语义雷区有多大？** 我们当场给 A 开了一张清单，每一行都足以单独毙掉 A：
  - **`Exit For` / `Continue For`**：`Parallel.ForEach` 的循环体是委托，`Exit For` 无法跨委托边界——要么禁止（循环体里不能提前退出），要么改语义（`Exit For` → `ParallelLoopState.Break()`），而 `Break` 的语义是"尽早停止，不保证已开始项停止"，与 VB 开发者对 `Exit For` 的直觉完全不同。
  - **循环后计数器值**：顺序 `For i = 0 To n - 1` 结束后 `i` 有确定终值；并行下 `i` 是各线程的私有参数，循环后取值无意义。任何依赖循环后 `i` 的既有代码（在并行版里）静默改变行为。
  - **共享可变状态**：`total += items(i)` 在并行下是竞态。
  - **异常语义**：`Parallel.For` 抛 `AggregateException` 聚合，不是第一个异常。Catch 写法要变。
  - **`ByRef` / `Return`**：委托体内 `Return` 是对委托返回，不是对方法返回。
  We 无需展开——清单第一行就够毙掉 A。

- **A 的 `Parallel` 关键字会踩中主线最重的判例。** 2018-05-30 主线审 #167 `Return?` 时的原话，我们逐字引用：

  > "We think this is a bad idea. Control flow would be altered by a very subtle character. It's not the same meaning as other uses as ? (any alteration in control flow)"

  Labels: LDM Reviewed: No Plans。`Return?` 只是**一个字符**改变控制流。`Parallel For` 是**一个词**把整个循环从顺序变成无序并发——变化幅度大得多，而表面上 `Parallel For` 和 `For` 只有一字之差，读者几乎必然沿用顺序心智模型。We 不认为这能过 #167 的闸门。

- **A 撞"第二种做事方式"（原则 #3）。** `Parallel.For` / `Parallel.ForEach` 已经存在于库层且工作良好。语言再给 `Parallel For`，等于为同一件事提供第二种写法，扩展语言表面积，而收益只是少写一层委托括号。C# 没有做，主线没有做，我们找不到理由做。

- **B 已经有人登记了。** 原文 18.14 的 `proposal-plinq-async-queries.md`（inactive）明列 "Parallel LINQ, queries that use `Await`, queries against `IAsyncEnumerable` are areas to investigate"。并行进语言的调查权在它名下。**本建议若要在查询层落地，先要跟那份建议合并或明确划界**，否则我们会造出两份描述同一调查的 inactive 文档。

- **C 为什么没有存活空间？** `Parallel` 修饰任意语句块，等于给"副作用自由散落"开绿灯：块内任何共享写入都是竞态，而语言没有任何机制约束它。`Async` 方法至少用状态机界定了异步边界；`Parallel` 块连边界都没有。We don't see a way to make this safe.

- **"For now `Parallel.For` is enough"——这句是 Anthony 的谦辞还是结论？** We 倾向于后者。异步集成后，并行仍留在库层，不是因为作者忘了，而是因为库已经够用。TPL 覆盖数据并行（`Parallel.For`）、任务并行（`Task.WhenAll`）、查询并行（PLINQ）；语言不介入时，开发者被迫显式写出并发边界，这反而是一种诚实的责任划分。**保持显式 = 保持安全。**

### 深度追问：LDM 拷问清单

We 用评价标准第五部分的追问清单逐条过。

#### 1. 语法 / 文法歧义

建议无语法，所以歧义面为零——但若激活 A，`Parallel` 的歧义立即出现：`Parallel` 是英文常用词，且 `Parallel.For` 已是 .NET 里高频出现的**成员访问表达式**。把 `Parallel` 做成上下文关键字，`Parallel.For(...)` 这种既有代码的解析会首当其冲。`Parallel For` 与 `Parallel.For` 的区分要靠位置/上下文——这正是 #167 类"细微字符变语义"的隐患在词法层的版本。

#### 2. 角案例 / 边界语义

- `Exit For` / `Continue For` 在委托体内非法（见 Q&A）——`Parallel For` 若含它们要么报错要么重定义语义。
- 循环后计数器终值、`Step` 负步进、预存在计数器——并行下全部失去顺序语义。
- `CancellationToken`：`Parallel.For` 支持取消，`Exit For` 若映射到取消，谁能取消、何时取消、取消后循环体执行到什么程度——未定义。
- 嵌套并行 / 并行体内再 `Parallel.For`：线程池饥饿问题，语言要不要管？不管就出事故。

#### 3. 作用域与绑定

A 的循环体被提升为委托：体内引用的局部变量要变成闭包字段。语义模型里 `i` 绑定到什么——循环变量还是委托参数？IDE 的 `i` 上跳转/重命名行为全变。若循环体含 `Exit For`，binder 要报"无法跨并行边界退出"。这些在建议原文中一概没有。

#### 4. 与既有特性的交互

- **与 `Async`/`Await`**：`Parallel For` 体内能否 `Await`？`Parallel.ForEach` 的委托是 `Action(Of T)`，`Await` 在非 `Async` 委托体内非法——所以并行体内无法 await，除非设计成 `Async` 委托（那 `Parallel.For` 不 await 委托返回值，语义又悬空）。异步 × 并行的叠加是这条建议最缺的交互设计。
- **与查询理解**：若走 B，`From x In items.AsParallel()` 今天就能写——`AsParallel()` 是扩展方法，查询理解天然组合。语言层几乎没有新工作，这就是 B 的吸引力。
- **与 `For` 增强家族**：`proposal-for-enhancements.md` 刚把 `Exit For z` / `Continue For y` 定为 Active。若并行 `For` 的 `Exit` 语义另起炉灶，会与这份 Active 建议直接冲突。We 不会让两套 `Exit For` 语义并存。

#### 5. Breaking change 与兼容性

- 不做（D）：零破坏。
- 做 A：`Parallel` 上下文关键字会影响含 `Parallel` 标识符的既有代码（`Dim Parallel As Integer`、`SomeType.Parallel`）；`Parallel For` 若使 `Parallel.For` 重解析，破坏面直接落在高频库调用上。**做 A 几乎必然带破坏。**
- 做 B：`AsParallel()` 扩展方法已是现状，查询层并行零破坏。

#### 6. Option Strict / 编译选项分叉

宽松模式下 `items` 可为 `Object`，`Parallel.ForEach(items, ...)` 的泛型推断会失败或晚期绑定——并行泛型方法与晚期绑定互斥。建议没有讨论两路径，而并行天然依赖强类型（泛型委托推断），`Option Strict Off` 下行为必然分叉。这是 B 也要回答的问题。

#### 7. IDE / IntelliSense 影响

并行循环的调试是已知痛点（并行栈、线程窗口、断点在哪个线程命中）。IDE 需要一个"并行循环"的专门可视化；`Exit For` 在并行体里报什么错误、`Parallel` 关键字高亮如何与成员访问区分，都是新工作。建议原文零 IDE 设计。

#### 8. 数据 / 普遍性

没有数据。主线 2018-05-30 欧洲行结论我们逐字引用：

> "We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now."

"数十万安静客户"的主流是业务 CRUD，不是 CPU 密集并行。"并行扩展"的需求方是谁、需求频率多高、被库方案阻塞在哪——一个数据都没有。`Suspect`：这是面向库作者/高性能计算场景的诉求，与 VB 主线客户画像错位。

#### 9. 更简替代

- 库层 `Parallel.For` / `Parallel.ForEach` / `Parallel LINQ` / `Task.WhenAll`——全部存在，覆盖数据并行、查询并行、任务并行三大场景。
- Analyzer：提醒"此循环体含共享写入，并行化不安全"，比语言语法便宜得多。
- 主线 2018-02-07 对可空引用类型留了一句话，我们认为是本场最贴切的处置范式：

  > "We'll postpone this until we understand the uptake in C#."

  并行语言集成，同样可以等到"C# 生态真的出现语言级并行需求"再动。C# 目前也没有语言级并行循环——它的答案和 VB 一样是库。

#### 10. 复杂度 / 成本 / 优先级

A 的成本是 Roslyn 全栈特性（新文法 + 闭包提升 + `Exit` 语义重定义 + 异常聚合 + IDE 并行可视化），且带破坏面。B 的成本低得多（查询翻译到既有 `AsParallel` 链），但价值是"优化"而非"新能力"。结合优先级：异步家族（`agile-async`、`async-iterator` 等）已在队列里，**并行应排在异步收敛之后**——原文自己都说"Eventually"，那"Eventually"就不该是现在。

#### 11. 运行时 / CLR 硬约束

无 CLR 障碍：`Parallel`、TPL、PLINQ 都已在 CLR 上实现，desugar 到库调用即可，无 PEVerify 问题、无存储规则问题。**正因为无 CLR 约束，反而削弱了"语言必须介入"的理由**——运行时不需要语言帮忙。

#### 12. 值不值得做

- 价值：低（主流客户画像错位；库已覆盖）。
- 成本：A 高（全栈 + 破坏），B 低但价值是优化级。
- 风险：A 高（#167 判例、语义雷区、破坏面），B 低。
- **结论：不值得现在做。** 保持 inactive，等三个信号（见 RESOLUTION）。

### VB 基因对照

- **保持 VB-like（原则 #2）**：`Parallel For` 读起来像英语，表面"像 VB"——但"像"的是壳，语义是彻底的并发重写，与 VB"读起来像英语、对新手友好"的承诺背道而驰。新手读到 `Parallel For` 会以为是 `For` 的并行版本，行为却完全不同。
- **不引入"第二种做事方式"（原则 #3）**：A 直接违反——`Parallel.For` 已经存在。C 更严重（任意块并行无先例）。
- **避免隐蔽的控制流/语义变化（原则 #7）**：本特性最重的败点。#167 的 `Return?` 判例逐字适用，且程度更甚。We were firm on this.
- **默认跟随 C#（原则 #4）**：C# 也没有语言级并行循环，答案同样是库。跟随 C# = 保持库层。
- **消除常见样板（原则 #9）**：A 想消除的是"包一层委托"的样板，但那是库的既定形态，样板成本低。B 的查询层并行才是"消除样板"的正确落点。
- **不为边缘场景加特性（原则 #6）**：并行在 VB 主流客户中是边缘场景，且无数据。败。
- **与主线关系（对照表 2.3）**：对照表**无并行行**——这是 Anthony 独立延伸，主线零讨论（对 `..\..\..\vblang/meetings/` 检索 `Parallel`/`PLINQ`/`AsParallel`/`parallelism`，仅命中 2018-02-21 Private Protected 讨论中"parallel the IL names / parallel naming"的英文单词用法，无任何语言级并行设计讨论；已核实）。主线对异步的处理（2017-08-09 #37 "The feature is approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"）也是"等平台、等需求"，不是主动造语法。

### RESOLUTION:

1. **保持 inactive（Table），不激活。** 本建议无语法、无动机用例、无数据，且我们核实了主线对语言级并行零讨论。三个激活信号缺一不可：
   - **信号一**：一份具体的语法提案，且必须回答 `Parallel` 关键字的词法歧义与 `Exit For`/`Continue For`/异常/共享状态/`Await` 交互——仅登记"值得思考"不构成激活。
   - **信号二**：需求数据——VB 目标代码库中 `Parallel.For`/PLINQ 的实际使用量与受阻场景，证明库方案真的不够。
   - **信号三**：C# 或平台动向——语言级并行在 C# 生态出现真实需求或设计，VB 再按"默认跟随 C#"评估。
2. **PROPOSAL A（语句级 `Parallel For` / `Parallel For Each`）：Reject（就该形态而言）。** 逐条：#167 判例（细微字符改控制流，此处是一个词改整个循环语义）、`Exit For` 无法跨委托、循环后终值/异常/共享状态语义全变、`Parallel` 关键字撞既有成员访问、原则 #3 第二种做法。若未来复活，须先给语义模型，不是先给语法。
3. **PROPOSAL B（查询级并行）：划归 `proposal-plinq-async-queries.md`。** 并行进语言的唯一有讨论价值的形态是查询层；调查权在 18.14 名下。本建议与它合并或明确划界，避免两份重复登记。查询层并行是"优化而非特性"，符合主线判例（2017-08-09 对 #37 的"priority driven by platform"；"Treat it as an optimization, not a feature"）。
4. **PROPOSAL C（并行语句块）：Reject。** 无并行边界、副作用自由散落、无安全机制。
5. **PROPOSAL D（什么都不做）：采纳。** Anthony 自己的 "For now `Parallel.For` is enough" 即是我们现在的结论。保持显式 = 保持安全。

### Implication:

- 在本建议文档头部标注 **LDM Reviewed: No Plans（保持 inactive）**，注明与 #167 判例、`proposal-plinq-async-queries.md` 的关系，以及三个激活信号。
- 把查询级并行的调查权正式移交 `proposal-plinq-async-queries.md`（组 21 队列），本建议不再重复登记。
- 与 `proposal-for-enhancements.md` 对表：确认 `Exit For z`/`Continue For y`（Active）不受本建议影响；本建议的 A 形态若复活，不得另立 `Exit For` 语义。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**主线语言级并行零讨论**——我们对 `..\..\..\vblang/meetings/` 与 `..\..\..\vblang/proposals/` 全量检索 `Parallel`/`PLINQ`/`AsParallel`/`parallelism`，仅命中英文单词用法，无设计讨论。仓库内可核实到此为止；仓库外（C# LDM、.NET 博客）是否存在语言级并行讨论，本仓库无法核实，`Suspect`（需外部核实）。
- `OPEN QUESTIONS`：并行体内 `Await` 的交互（`Parallel.ForEach` 委托非 `Async`，`Await` 非法）——若未来设计 B 或 A，必须回答；本场不定。
- `TODO`：量化 VB 代码库中 `Parallel.For`/PLINQ 的实际使用量与受阻场景，为激活信号二补数据。
- `TODO`：把"并行语言集成的三个激活信号"写入本建议文档头部。
- `Follow-up`：与组 21 的 plinq-async-queries 会议对表，确认查询级并行调查的归属。

### 状态

- **LDM 状态：LDM Reviewed: No Plans（保持 inactive）。**
- **三态判定：Table** — 观察真实但结论不成立：异步需要语言是机制性的，并行不需要。语句级并行 Reject（#167 判例 + 语义雷区 + 第二种做法），查询级并行划归 plinq-async-queries，现状（库层 `Parallel.For`）保持。**激活所需信号：具体语法 + 需求数据 + C# 动向，三者缺一不可。**

---

## 附录：特性评价

# 建议评价报告：proposal-parallel-extensions.md

## 评价对象

- 建议：proposal-parallel-extensions.md — 并行扩展（Parallel Extensions）
- 来源：Anthony 原文 §18.5（`..\..\AnthonyDesign_wordpress.txt` L2861–2865），全文一句话："We've integrated asynchrony but not parallelism. Worth thinking about eventually. For now `Parallel.For` is enough."
- 配方目标：登记"将来值得思考并行语言集成"这一关注点；无语法、无动机用例、无示例

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 1/5 | 锚点 1："目标定义不清"。Motivation 是一个方向性观察，没有定义任何语言能力改进、没有可衡量目标、没有示例可演示——唯一"效果"是"值得思考"，无法验收。观察本身为真，但效果维度的评分对象（语言能力/体验改进）根本不存在 | 已提供（仅原文一句观察） | 无语法、无示例、无验收标准；"集成并行"的形态完全未定义 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化"。唯一隐含的"特性"方向（语句级 `Parallel For`）是照搬库层 `Parallel.For` 的语义，未做任何 VB 化改造；且违反原则 #3（第二种做事方式）与 #7（隐蔽语义变化）。无任何 VB 基因增量 | 已检查 | 无特性内容可评；方向与 VB 基因冲突；`Parallel` 关键字撞既有成员访问 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节模板齐全（Summary/Motivation/Detailed design/Drawbacks/Alternatives/Unresolved），诚实标注"原文无语法"（不虚构——这是加分）；但 Detailed design 实质上为空（"原文没有给出任何示例代码或语法提案"），Drawbacks/Alternatives 是撰写者的推演而非原文内容，边界含糊是必然 | 已检查 | 无语法、无示例、无 BNF；"未决问题 3 个且具体"（健康区间，不扣分）；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。风（演化一致性）受损：若激活语句级并行，与 `For` 增强家族（`Exit For z` Active）直接冲突、与 async 家族交互未定义；暗风险：`Parallel` 关键字破坏面、隐蔽并发语义。文档未权衡这些 | 已检查（预测待定） | 无应对设计；属性影响须"已采纳"后定；对 VBScript.NET 差异化无正向贡献 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。主要成分可辨认：Anthony §18.5（准确引述）、库层 TPL/PLINQ 对应（暗示未明说）；但未标注：借鉴自 C# 生态事实（C# 同样无语言级并行）、`proposal-plinq-async-queries.md` 的重复登记关系（Alternatives 提了，但未声明应合并）、对主线零讨论的核实缺失 | 已检查 | 来源标注不全；未声明与 18.14 调查项的重叠；未点明"默认跟随 C#"对库层的支持 |

## 设计原则对照

- **与 VB 基因：偏离为主。** 唯一命中是原则 #1（不破坏——不做即零破坏）；原则 #2 表面通过（`Parallel For` 像英语）实为误导（语义是并发重写）；原则 #3（第二种做法）与 #7（隐蔽语义，#167 判例逐字适用）直接违反；原则 #6（边缘场景）失败；原则 #4（跟随 C#）支持库层而非语言层。
- **与主线关系：Anthony 独立延伸，主线零讨论。** 对照表 2.3 无并行行；主线对 `Parallel`/PLINQ/AsParallel/parallelism 的检索零命中（已核实，仅 2018-02-21 英文单词用法）。主线对异步的处置（2017-08-09 #37 "approved in principle but needs its priority driven by other platform changes"）也是等平台，不是造语法。与 plinq-async-queries（18.14）在查询级并行上重叠，需划界或合并。
- **破坏性变更：无（现状，因为无语法）**；但若激活 A，`Parallel` 上下文关键字会破坏含 `Parallel` 标识符的既有代码，且 `Parallel.For` 成员访问的解析受影响——破坏面直接落在高频库调用上。

## 总评

- **达成程度：未达成（作为提案）。** 它是一句观察的登记，不是提案：无语法、无动机用例、无数据、无设计。观察本身（异步/并行待遇不对称）为真，但结论（值得语言级集成）不成立——异步需要语言是机制性必然，并行没有这一必然。
- **LDM 三态建议：Table**（保持 inactive，标注 LDM Reviewed: No Plans）。语句级并行（A）Reject；查询级并行（B）划归 plinq-async-queries；什么都不做（D）采纳。
- **主要问题**：(1) 无任何设计内容可评；(2) 语句级并行方向违反 #167 判例与原则 #3/#7；(3) `Parallel` 关键字撞既有成员访问；(4) 与 18.14 plinq-async-queries 重复登记未声明；(5) 无需求数据，"数十万安静客户"画像下并行是边缘场景。

## 返工建议

- **若保持 inactive（推荐）**：文档头部加状态标注（LDM Reviewed: No Plans + 三激活信号）；把查询级并行的调查权正式移交 plinq-async-queries；替换占位链接或说明无原型。
- **若未来激活**：必须先回答——具体语法（含 `Parallel` 词法歧义消解）、`Exit For`/`Continue For`/异常/共享状态/`Await` 的语义模型、`Option Strict On/Off` 两路径行为、兼容性分析（`Parallel` 标识符 + `Parallel.For` 成员访问）、与 for-enhancements 的 `Exit For z` 冲突消解、需求数据与原型。
- **补充证据**：VB 代码库 `Parallel.For`/PLINQ 使用量统计；与 C# 语言级并行动向的对照（外部核实，仓库内不可得）。
- **未决问题处理**：并行体内 `Await` 交互随 plinq-async-queries 一并调查；查询级并行划归 18.14，本建议不再重复设计。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\..\csharplang-index.md`（来源 `..\..\..\csharplang`，dotnet/csharplang 官方仓库镜像）补写 C# 生态对照。核心问题只有一个：**C# 把并行放在哪一层——语言还是 BCL？** 本提案（并行语法扩展，尤其语句级 `Parallel For`）与 C# 现实是兼容、冲突、需桥接还是脱节。先说实话：并行不是 C# 的互操作主题，C# 侧没有「语言推动新 CLR 能力」的机制性理由与之对应——这个「弱关系」本身就是最重要的发现。

### 相关 C# 现实方向

C# 生态对并行的回答是 **BCL，不是语言**：

1. **并行 = 库层，C# 语言侧零投入。** C# 没有 `parallel for` / `parallel foreach` 语言构造；数据并行（`System.Threading.Tasks.Parallel` 的 `Parallel.For`/`Parallel.ForEach`）、查询并行（PLINQ 的 `AsParallel()`）、任务并行（`Task.WhenAll`）全部在 BCL。`Language-Version-History.md` 的逐版本语言特性清单（C# 13 `ref`/`unsafe` in iterators/async、`ref struct` interfaces；C# 14 extensions 等）**无任何并行条目**——对 `..\..\..\csharplang` 全库检索 `Parallel.For`/`PLINQ`/`AsParallel`/`System.Threading.Tasks`，该历史文件零命中。索引 T8 亦确认 C# 15/16 主线是 unsafe evolution、unions/closed hierarchies、extensions，**无语言级并行**。

2. **异步进语言、并行不进语言的机制性理由，在 C# 设计文档中有直接证据。** C# 5 的 `async`/`await` 是语言特性，因为编译器必须把方法重写为状态机；并行没有这种「非语言不可」的控制流重写。`proposals\csharp-7.0\task-types.md`（Execution 节）逐字：

   > "The types above are used by the compiler to generate the code for the state machine of an `async` method."

   这与本场正文「异步需要语言是因为必须重写控制流，并行不需要」的判断同构——C# 侧用同一把尺子量出了「并行留在库层」。

3. **C# 提案库里并行的唯一一次出现，是「库 + lambda」惯用法。** 对 `proposals\` 全量检索，唯一一处并行循环代码在已 reject 的 `proposals\rejected\readonly-locals.md`（readonly locals/parameters 提案），作为多线程竞态场景的示例：

   ```csharp
   readonly long index = ...;
   Parallel.ForEach(data, item => {
       T element = item[index];
       index = 0; // Error: can't assign to readonly locals outside of declaration
   });
   ```

   C# 语言设计者写并行循环时，默认形态就是 BCL 调用 + lambda，而不是语法。这正是本提案 PROPOSAL A 要发明的「语句级语法」的反面证据。

4. **C# LDM 对语言级并行零讨论。** 对 `meetings\` 全量检索 `language-level parallel` / `parallel loops` / `parallelism in the language` / `parallel for` 零命中；`Parallel` 一词在 LDM 中仅以「并行 IL 命名」「并行版本控制」等英文普通用法出现。C# LDM 从未把「语言级并行」列为候选特性——与本场对 `..\..\..\vblang` 检索「主线零讨论」的结论遥相呼应，两边语言设计组织对并行的一致沉默，本身就是「库已够用」的治理信号（索引 T1：特性需 LDT 成员 champion、经 LDM 进 milestone；并行至今无人 champion）。

### 现实 vs 提案

| 本提案形态 | C# 现实 | 判定 |
|---|---|---|
| PROPOSAL A：语句级 `Parallel For` / `Parallel For Each` | C# 无此构造；唯一先例是 BCL `Parallel.For` + lambda（readonly-locals 示例） | **冲突** —— 与 C#「并行留在 BCL」的既定分工相悖；为 RESOLUTION 第 2 条 Reject 提供生态侧独立证据 |
| PROPOSAL B：查询级并行（PLINQ 进查询理解） | 查询并行是 `AsParallel()` 扩展方法 + LINQ 组合，语言零介入 | **脱节** —— 方向不冲突，但 C# 现实恰恰说明查询并行不需要语言：`From x In items.AsParallel()` 今天就能写。B 是「优化而非特性」，C# 生态无动机支持为它造语法 |
| PROPOSAL C：并行方法/块 | C# 无先例；与 async 状态机边界正交 | **冲突** —— 与 RESOLUTION 第 4 条 Reject 一致，C# 侧无可借鉴 |
| PROPOSAL D：保持库层 | C# 现状即此 | **兼容** —— 本场结论与 C# 现实完全重合 |

**总体判定：本提案与 C# interop 关系弱；语句级方向与 C# 现实冲突，查询级方向与 C# 现实脱节（不需要语言介入），保持库层与 C# 现实兼容。** C# 生态把并行放在 BCL（System.Threading.Tasks、PLINQ）而非语言，本提案想把并行放进语言——这是治理/分层上的分歧，不是互操作技术上的分歧。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态：并行必须走强类型路径。** 本场 Q&A 已指出 `Option Strict Off` 下 `Parallel.ForEach(items, ...)` 泛型推断失败或落入晚期绑定。VBScript.NET 的「默认安全」应把并行限定在强类型、`Option Strict On` 路径；`Any`/晚期绑定留给 COM/Office 动态场景，不与并行泛型方法混用——决策文件 M2 的「默认安全、按需动态」边界在此同样适用。
- **source-gen 桥（索引 T6）：查询级并行用编译期翻译交付，而非新语法。** 若未来激活 B，正确实现是「查询表达式 → 既有 `AsParallel()` 链」的编译期 lowering / source-gen，零运行时新依赖——与 C#「编译期生成替代运行时动态」的方向（T6）一致，也与 RESOLUTION 第 3 条（划归 plinq-async-queries）一致。
- **AOT / trimming（索引 T5）：库层并行是 AOT 友好路径。** TPL/PLINQ 是普通 BCL 代码，无反射、无 `DynamicMethod`，NativeAOT/trimming 下天然可裁剪；语言级「动态并行」（如 `Any` 参与并行调度）反而会成为 AOT 障碍。生态压力侧再次支持「并行留在库层」。
- **识别新元数据：本主题几乎不触发。** 并行对应的 `Parallel`/PLINQ 类型是普通 BCL 类型，C# 侧无新增特性/属性（无 `RefSafetyRules`、`RequiresUnsafeAttribute`、`CompilerFeatureRequired` 之类）——决策文件 M8 的「unsafe 元数据桥接」要点在本主题不适用。真正需要识别新元数据的是异步侧（`AsyncMethodBuilder`、`IAsyncEnumerable`），不是并行侧。

### 对既有 RESOLUTION / 三态判定的影响

**不推翻任何条目，整体强化 Table 判定。** C# 生态现实（并行=BCL、LDM 零讨论、15/16 路线图无并行）为既有 RESOLUTION 提供背书：

- RESOLUTION 第 1 条「信号三：C# 或平台动向」——索引 T8 确认 C# 15/16 主线无语言级并行，**信号三当前未触发**，inactive（Table）维持。
- RESOLUTION 第 2 条（A Reject）——C# 自身把并行留在 BCL 且唯一提案用例是库+lambda，是「A 是错误层次」的独立佐证。
- RESOLUTION 第 5 条（D 采纳）——与 C# 现实完全重合，互为印证。
- 一处可补充：信号三可精确化为「C# LDM 将语言级并行 champion 为特性」；但按索引 T1 治理规则，并行缺乏机制性理由（无状态机重写需求），champion 概率低——信号三事实上接近「长期休眠」。

### 引用纪律与未核实项

- 已核实逐字引用：
  - 「The types above are used by the compiler to generate the code for the state machine of an `async` method.」→ `proposals\csharp-7.0\task-types.md`（Execution 节）。
  - `Parallel.ForEach(data, item => {`（示例代码）→ `proposals\rejected\readonly-locals.md`（第 54–60 行上下文）。
- 已核实检索性事实（无逐字引用，属计数结论）：`Language-Version-History.md` 无并行条目；`proposals\` 仅 readonly-locals 一处并行循环代码；`meetings\` 对语言级并行零命中。
- 索引第四节 6 段已核实原文**均不涉及并行**——这本身即证据：C# 语言设计文档对「并行作为语言问题」集体沉默。
- **OPEN QUESTIONS**（仓库内不可核实，需外部确认）：
  - .NET 并行栈（`Parallel`/PLINQ/`Task`）作为 BCL 库的历史出处——如微软研究院「Parallel Extensions」项目演化成 TPL/PLINQ——不在 csharplang 仓库，本仓库无法核实，`Suspect`。
  - C# 5 `async`/`await` 的原始设计动机原文：`proposals\csharp-5.0\` 目录未归档（async 提案早于本仓库提案库），此处仅用 `task-types.md` 的编译器状态机描述作旁证；如需「async 为何必须是语言特性」的原始 LDM 论述，须查 dotnet/csharplang 早期 commit 或外部存档。
