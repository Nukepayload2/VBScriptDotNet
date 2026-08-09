# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这场是组 13（异步系）的第三场——`agile-async` 与 `require-await-call` 处理的是"异步调用怎么发出"，本建议处理"异步序列怎么消费、怎么生产"。上一场 `for-each-enh` 已经裁定 `Await Each` 为 `Table`，并把它的落地依赖登记到本建议——所以本场有一半的工作是回应那个依赖：生产者侧 `Async Iterator Function` 是否成立，消费端是否随之解禁。

先摆一个态度：这是一份**骨架级**建议。两行语法、三段 Drawbacks、三段 Alternatives、三个未决问题——比 `for-each-enh` 的四件套还要薄。但主题本身不薄：`IAsyncEnumerable` 是 .NET Core 3.0 之后流式数据的标准形态，C# 8 已经为此造了一整套 `await foreach` + `async` 迭代器语法。我们讨论的价值判断是：这个方向 VB 该不该跟、跟多深、以什么措辞跟。

## Agenda

* [Proposal - Await Each 与 Async Iterator](#proposal---await-each-与-async-iterator)

## Proposal - Await Each 与 Async Iterator

_Related: [vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；主线 2017-08-09（`Await in Catch/Finally`：异步特性优先级由 `IAsyncDisposable/Async Using` 等平台变化驱动）；主线 2018-02-21 / 2018-02-28（Await in Catch：VB 的 GoTo/OnError 使异步状态机复杂）；主线 2014-02-17（`Iterator Function` + `Yield` 落进 VB 的记录）；ModVB：`proposal-for-each-enhancements.md`（`Await Each` 裁定为 Table 依赖本建议）、`proposal-agile-async.md`、`proposal-require-await-call.md`、`proposal-async-sub.md`、`proposal-async-event.md`、`proposal-using-synclock-enhancements.md`_

### 场景与缺口

We opened with the gap, and we do not dispute that it is real.

流式异步数据（分页、事件流、大文件、聊天、长连接）在 .NET Core 3.0+ 里以 `IAsyncEnumerable(Of T)` 为标准形态。今天 VB 消费它的方式只有两条：

```vb
' 路径一：一次性物化，失去流式。
For Each chunk In Await stream.FetchChunksAsync(ct).ToListAsync()
    ...
Next

' 路径二：手写状态机 —— 任何人都不想写第二遍。
Dim enumerator = stream.FetchChunksAsync(ct).GetAsyncEnumerator(ct)
Try
    While Await enumerator.MoveNextAsync()
        Dim chunk = enumerator.Current
        ...
    End While
Finally
    Await enumerator.DisposeAsync()
End Try
```

路径一把"流式"整个丢了——`IAsyncEnumerable` 之所以存在就是为了不把全部数据物化进内存；路径二是 `For Each` 在异步世界里的缺席。生产侧更痛：要造一个 `IAsyncEnumerable`，今天只能手写 `IAsyncEnumerator` 的状态机，或借助 `System.Linq.Async` 的运算符组合——没有"把一段 `Await`/`Yield` 混合体变成异步序列"的声明式写法。

C# 8 已经为这两个缺口造了一整组语法：消费端 `await foreach`、生产端 `async` 迭代器（`yield return` 与 `await` 混用）。方向明确，问题只在 VB 怎么落。We think the honest question is not *whether* but *how*——以及 ModVB 这份骨架建议够不够格成为落地的起点。

### 候选方案

**PROPOSAL A — 原文全案：`Await Each` 消费 + `Async Iterator Function` 生产（对称双端）。**

```vb
' Support for consuming IAsyncEnumerable.
Await Each chunk In stream.FetchChunksAsync(ct)
    Await HandleChunkAsync(chunk)
Next

' Support for constructing IAsyncEnumerable.
Async Iterator Function FetchChunksAsync(source As Stream, ct As CancellationToken) As IAsyncEnumerable(Of Byte())
    Dim buffer(4095) As Byte
    Do
        Dim n = Await source.ReadAsync(buffer, ct)
        If n = 0 Then Exit Do
        Yield buffer
    Loop
End Function
```

（原文把 `stream` 写作 `steam`，我们判定为笔误，下同——见 OPEN QUESTIONS。）

**PROPOSAL B — 消费端改为 `Await For Each` / `For Each Await` 的修饰语形态，不引入 `Await Each` 新字面。**

```vb
For Each Await chunk In stream.FetchChunksAsync(ct)
    ...
Next
```

把 `Await` 作为 `For Each` 子句的修饰语，而不是新建语句头。复用 `For Each` 既有文法，parser 面小。

**PROPOSAL C — 消费端一次性物化（Alternative 原文第一条）：`For Each chunk In Await seq.ToListAsync()`。** 语义正确、零新语法，但丢流式、丢逐块。

**PROPOSAL D — 依赖 `System.Linq.Async`，不新增语法（Alternative 原文第二条）。** 消费端 `ForEachAsync`、`Select` 等运算符组合可用；生产端 `System.Reactive`/`Ix` 也有帮助。但 `ForEachAsync` 拿不到循环体内逐元素的 `Await` + `Exit For` 控制流——回调模型表达不了 `Exit`/`Continue`。

**PROPOSAL E — 仅消费端先行（`Await Each` 单独落地）。** 消费别人（C# 生态）生产的序列，自己暂不生产。`for-each-enh` 会议已指出其问题：没有生产者，消费端消费谁？但它并非完全无意义——`IAsyncEnumerable` 的生产者今天大量来自 C# 与库。

**PROPOSAL F — 仅生产端先行（`Async Iterator Function` 单独落地）。** 让 VB 能声明异步迭代器，消费端暂用路径一/路径二。生产者是更硬的一半（状态机），但单独落地的价值被消费端的别扭打折扣。

**PROPOSAL G — 返回类型分叉：原文 `As IAsyncEnumerable`（非泛型）vs 修正为 `As IAsyncEnumerable(Of T)`（泛型）。** 这是本建议最具体的一个"待定"。见 Q7。

**PROPOSAL H — 什么都不做 / 维持 Table。** 等 C# 生态把 `IAsyncEnumerable` 用法固化、等 ModVB 其余异步系建议（agile-async、require-await-call）定型再回访。

### 权衡：Q&A

**Q1：`Await Each` 是 VB 该有的措辞吗？**

读起来像英语："await each chunk in the stream"——这正是原则 #5（读起来像英语）要的形态。`Await` 是 VB 既有的表达式关键字，`Each` 是 `For Each` 的关键字，两者组合在 VB 语义里天然成立。对比 C# 的 `await foreach`（`foreach` 在前、`await` 作修饰），VB 的 `Await Each` 是"以异步关键字开头"——与 `Await stream.ReadAsync()` 的语序一致，符合 VB 的英文语序偏好。**我们喜欢这个措辞，反对 PROPOSAL B 的 `For Each Await`**：把 `Await` 塞进子句修饰位置，读起来是 "for each await chunk"，英文语序别扭，且 `For Each Await x` 与 `For Each x In Await seq`（物化形态）在视觉上过于接近，正是原则 #7（细微字符改变语义）要防的混淆面。`Suspect`：vblang 主线对 `IAsyncEnumerable` 没有任何讨论（我们对 `..\..\vblang\meetings/` 全量检索 `IAsyncEnumerable` 零命中），`await foreach` 与 `async` 迭代器是 C# 8 的外部队标，措辞决策只能靠 VB 自身审美，无法从主线引证。

**Q2：`Await Each` 有没有 `Next`？有——原文第 3.5 章就写了。**

本建议的未决问题问"是否带 `Next` 步进"，但 Anthony 原文第 3.5 章（For Each）L945–948 写得清清楚楚：

```vb
Await Each item In sequence
    ...
Next
```

`Next` 在 `For Each` 里是循环终止符、不是步进；`Await Each` 对称地以 `Next` 终止。所以"带不带 `Next`"不是未决问题——原文已答。真正未决的是 `Next` 能否带变量名（`Next item`，与 `For Each` 的 `Next x` 对称）以及 `Exit For`/`Continue For` 是否作用于 `Await Each` 循环。我们倾向：`Exit For`/`Continue For` 应当成立——`Await Each` 是 For 家族成员，`for-each-enh` 的命名循环工作（`Exit For parent`）天然应该覆盖它。`Probably`。

**Q3：提前退出 / 异常时的 `DisposeAsync` 谁来调用？**

`IAsyncEnumerator` 要求消费方在提前终止时调用 `DisposeAsync`。C# `await foreach` 的行为是：正常走完、`break`、异常都确保 `DisposeAsync`。VB 的 `Await Each` 必须规定同一语义——循环体 `Exit For`、循环体抛异常、循环外 `GoTo` 跳出（VB 有 `GoTo`，2018-02-21 主线明确说异步状态机里 GoTo/OnError 是麻烦点）都要触发 dispose。这是 spec 的硬任务，不是可选细节。C# 侧 `await foreach` 的 dispose 语义为外部事实（`Suspect`：无法在本仓库核实，但这是流式资源回收的公共常识）。

**Q4：取消（cancellation）怎么传？**

原文示例把 `cancellationToken` 传给方法（`FetchChunksAsync(ct)`）——这是"序列内建取消"。C# 8 还有枚举级取消：`foreach await (x in seq.WithCancellation(ct))`，即用 `WithCancellation` 扩展把 token 绑到枚举器上。VB 若只支持序列内建取消，则"消费方想在中途取消"就只能靠 `Exit For` 手动退出（不抛 `OperationCanceledException`），或要求生产者把 token 接住。我们 `Suspect`：VB 需要至少一种取消路径，最佳是 `WithCancellation` 式扩展 + 语言支持，与 `ConfigureAwait` 问题（Q6）一并定。

**Q5：`Async Iterator Function` 的 `Async` + `Iterator` 修饰符组合，状态机怎么办？**

这是全案最硬的一问。VB 已有独立的 `Async Function`（await 状态机）与 `Iterator Function`（yield 状态机，2014-02-17 记录 `Private Iterator Function Helper(min%, max%) As IEnumerable(Of Integer)` + `Yield i` 已落地）。`Async Iterator` 是**两套状态机叠乘**：同一函数体里既有 `Await` 挂起点、又有 `Yield` 产出点，编译器要生成一个同时处理两类挂起的组合状态机（实现 `IAsyncEnumerable(Of T)`，`MoveNextAsync` 返回 `ValueTask(Of Boolean)`）。C# 8 为此付出了真实编译器工程量；VB 编译器要复刻。2018-02-28 主线在讨论 Await in Catch 时说过："Await in try catch is implemented by rewriting the code to use a complex state machine."——异步状态机在 VB 本就复杂，叠上 yield 是加倍的复杂度。**这不是语法问题，是编译器工程量问题**，本建议对此只字未提。`Probably`：ModVB 若要落地，这是需要单独设计文档 + 原型验证的部分。

**Q6：`ConfigureAwait` / Agile 交互。**

`IAsyncEnumerator.MoveNextAsync` 和 `DisposeAsync` 返回 `ValueTask`，await 它们同样有"是否捕获同步上下文"的问题。`agile-async` 建议（组 13 姊妹）主张 `Agile Async` 方法不捕获同步上下文。`Await Each` 循环在 `Async` 方法内运行时，每次 `MoveNextAsync` 的恢复点是否捕获上下文？我们倾向：**默认与普通 `Await` 一致（捕获），由 agile-async 的机制统一放宽**——不在这里另设规则，避免两套 ConfigureAwait 语义。`Probably`。

**Q7：返回类型——`As IAsyncEnumerable`（非泛型）错没错？**

错。.NET BCL 里**没有**非泛型 `IAsyncEnumerable` 接口——只有 `IAsyncEnumerable(Of T)`（`IAsyncEnumerator(Of T)` 同理）。原文写 `As IAsyncEnumerable` 无法编译；本建议的未决问题自己意识到了（"实际应为 `IAsyncEnumerable(Of T)`，待定"），但没有去核。这不是"待定"而是"已知错误"。正确形态是泛型，元素类型由 `Yield` 表达式推断并与 `As` 子句相互校验。同时注意：`IAsyncEnumerable(Of T)` 元素类型**不能**像普通 `Async Function` 那样由返回类型推断——`Async Iterator` 的返回类型必须显式，这与 C# 8 一致。`Probably`：C# 8 的 async 迭代器同样要求显式 `IAsyncEnumerable<T>`。

**Q8：与 require-await-call 的交互——`Async Iterator Function` 返回的不是 `Task`，它算"被 Await"吗？**

`require-await-call` 规定"异步调用必须被 Await 或赋给任务对象，否则用 `Call`"。但 `Async Iterator Function` 返回 `IAsyncEnumerable(Of T)`，不是 awaitable——对它的"消费"是 `Await Each` 而非 `Await`。一个 `Async Iterator Function` 被调用却不用 `Await Each` 消费，算不算"漏 await"？我们倾向：**不算错误，但应诊断**——`Dim e = FetchChunksAsync(ct)` 拿到一个无人消费的枚举器，与拿到一个无人等待的 Task 同样可疑。这条要与 require-await-call 的规则面合并考虑，不能各自为政。`Probably`。

**Q9：`Await` 在 `SyncLock`/`Using`/`Catch` 内的限制对 `Await Each` 怎么作用？**

VB 现行规则：`Await` 不能出现在 `SyncLock` 体内、`Using` 体的某些位置受限、`Catch` 块内不可用（2017-08-09：Approved-in-Principle 但优先级由平台驱动）。`Await Each` 循环体里写 `Await`，继承同一套限制。而 `Async Iterator Function` 的生产者体里，`Yield` 出现在 `Using`/`SyncLock`/`Catch` 内怎么办？C# 8 禁止在 `catch`/`finally` 里 `yield`（`Suspect`，外部事实）。VB 若允许，就碰 2018-02-21 主线警示的 GoTo/OnError 状态机问题。**本建议对此无任何规定**——这是 spec 级缺口。

**Q10：破坏性变更？**

零。`Await Each` 今天在非异步上下文里 `Await` 作标识符、`Each` 是关键字，`Await Each item In sequence` 是语法错误；`Async Iterator Function` 中 `Async` 与 `Iterator` 修饰符不能同用，今天也是错误。错误变程序，无既有代码破坏——这是本建议最干净的一张牌。唯一需要小心的：`Await Each` 若被 parse 为 `Await <expr>` 的某种失败路径，错误信息要足够清晰。

**Q11：Option Strict / Option Infer 分叉。**

`Await Each chunk In seq` 中 `chunk` 的类型由 `seq` 的 `IAsyncEnumerable(Of T)` 元素类型决定。`Option Infer On`：`chunk` 推断为 `T`；`Option Infer Off` + `Option Strict Off`：`chunk` 为 `Object`（宽松路径，元素晚期绑定访问）。两条路径的类型推断必须逐位一致——本建议对两路径只字未提。`Option Strict On` 下 `Await Each` 非异步上下文立即报错。这条要求与 `for-each-enh` 的 `Await Each` 要求一致。

**Q12：值不值得现在做？**

价值：流式异步消费/生产是 .NET 3.0+ 的真实高频场景，VB 缺席是 parity gap；措辞极 VB。成本：组合状态机是重型编译器工作（Q5）；取消/dispose/`Yield` 位置限制等 spec 面全是空白；原型（状态行 `Prototype: Complete` 指向占位链接）未提供。风险：低（零破坏），但**范围未定义**——这份骨架建议把"消费 + 生产 + 状态机 + 取消 + dispose"一整座山打包，没有分阶段路径。对照主线 2017-08-09 对 Await in Catch 的裁定——"needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"——主线把异步特性绑在平台演变上；ModVB 若要在 VBScript.NET 落地，平台（`IAsyncEnumerable` 需 netstandard2.1+ / .NET Core 3.0+）是否就绪必须先确认。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

- **`Await Each` vs `Await <expr>`**：`Await` 是异步方法内的上下文关键字；`Each` 是 `For Each` 的关键字。`Await Each item In seq` 与 `Await SomeExpr` 在文法上如何区分？答案：`Each` 不是合法的表达式起始符（它是关键字），所以 `Await` 后紧跟 `Each` 只有一个读法——语句头。parse 规则：在 async 上下文里，`Await` 之后遇 `Each` 关键字 → `Await Each` 语句。需写进 BNF。
- **`Async Iterator Function`**：`Async` 与 `Iterator` 是既有的方法修饰符，二者组合是**新**修饰符组合。`Async Iterator Sub` 是否合法？——不合法：迭代器必须有返回值，`Async Iterator Sub` 是错误。文法要区分 `Async Iterator Function`（合法）与 `Async Iterator Sub`（错误）。
- **无 body 区分**：`Await Each` 的循环体以 `Next` 终止，无缩进依赖；单行冒号形态 `Await Each x In seq : Await F(x) : Next` 的优先级需在文法里明确（与 `For Each` 的冒号形态一致）。
- **`Await Each` 循环变量**：`chunk` 是循环局部变量，作用域 = 循环体（含 `Next` 之前）。与 `For Each` 控制变量同规则（只读、不可赋值）。

#### 2. 角案例与边界语义

- **空序列**：`Await Each` 循环体一次不执行，无特例。
- **提前退出**：`Exit For` 触发 `DisposeAsync`（Q3）；`Continue For` 触发下一次 `MoveNextAsync`，不 dispose。
- **循环体抛异常**：`DisposeAsync` 必须被调用（`Try`/`Finally` 结构）。若 `DisposeAsync` 自身抛异常——两个异常如何聚合？C# 侧有 `AggregateException` 处理经验（`Suspect` 外部事实）。VB 需定义。
- **`Await` 在循环体内**：循环体是 async 方法的一部分，`Await` 可用；但循环变量捕获进 lambda 时逐迭代副本行为需与 `For Each` 一致（`for-each-enh` 已列为 spec 待办）。
- **`Async Iterator Function` 零 `Yield`**：合法的"空序列"迭代器（与 `Iterator Function` 零 `Yield` 一致）。
- **`Async Iterator Function` 内 `Await` 后的 `Yield`**：状态机在 `Await` 挂起点之后产出元素——这是组合状态机的核心路径，编译器必须支持"挂起后再产出"。
- **取消**：Q4——序列内建取消（token 传进方法）与枚举级取消（`WithCancellation`）两路。VB v1 至少支持序列内建；`WithCancellation` 式扩展建议随 ConfigureAwait 一并定。
- **`Yield` 在 `Using`/`SyncLock`/`Catch` 内**：Q9——C# 8 禁止在 catch/finally 里 yield；VB 是否沿用，或利用 VB 的 GoTo 能力放宽（伴随 2018-02-21 的状态机复杂度代价）。spec 待办。

#### 3. 作用域与绑定

- `Await Each` 的 `chunk` 绑定到 `IAsyncEnumerable(Of T)` 的元素类型 `T`（泛型）或 `Object`（宽松路径）。语义模型返回 `LocalSymbol`；作用域 = 循环。
- `Async Iterator Function` 的 `Yield expr` 表达式必须可转换到声明的 `T`（显式返回类型）；`Yield` 之后 `chunk` 变量在下一轮重新赋值（循环变量语义）。
- `Async Iterator Function` 是**方法声明**，与 `For Each` 的循环变量无绑定冲突。

#### 4. 与既有特性的交互

- **`For Each`**：`Await Each` 是 `For Each` 的异步孪生——共享 `Next` 终止、共享 `Exit For`/`Continue For` 语义、共享循环变量只读规则。但**不是** `For Each` 的选项开关——`Await` 显式出现在语句头，不改变 `For Each` 语义（原则 #7）。
- **`Async` / `Iterator` 修饰符**：`Async Iterator` 是二者组合，`Async` 与 `Iterator` 各自的既有限制（async 不能有 `ByRef` 参数、iterator 的元素类型规则）在组合下都要继续成立。
- **`Exit For j` / `Continue For`（for-each-enh 命名循环）**：`Await Each` 循环应可作为命名目标（`Exit For outer`）。for-each-enh 已批准命名循环，`Await Each` 必须与其一致。
- **`Using` / `SyncLock`**：`Await Each` 循环体继承 `Await` 在这些块内的现行限制；`Async Iterator` 生产者的 `Yield` 在块内的位置限制（Q9）需另定。
- **`Async Sub`（组 13 姊妹）**：`Async Iterator Function ... As IAsyncEnumerable(Of T)` 与 `Async Sub ... As Task` 的返回契约不同——前者返回序列、后者返回 awaitable。二者是不同特征，不应混淆。
- **query / LINQ**：`Await Each` 不引入查询理解；流式 LINQ（`System.Linq.Async`）作为库层面补充（PROPOSAL D），与语言语法互补不冲突。

#### 5. Breaking change 与兼容性

零破坏（Q10）——两处新语法今天都是错误。风险面只有一条：若 `Await Each` 落地而 `IAsyncEnumerable(Of T)` 元素类型推断与 `Option Infer Off` 的宽松路径行为不一致，会制造"同样源码两种编译器两种行为"的分叉。其余兼容问题（`Next` 带变量名、`Exit For` 命名）都是 error→program。需要 `langversion` 门控与诊断文案设计。

#### 6. Option Strict / 编译选项分叉

- `Option Strict On` + `Option Infer On`：`chunk` 推断为 `T`；`Await Each` 仅限 `Async` 方法内。
- `Option Strict On` + `Option Infer Off`：`chunk` 需要显式类型声明？`For Each` 的宽松形态在此如何对应，需对齐 `for-each-enh` 的裁定。
- `Option Strict Off`：`chunk` 为 `Object`，元素晚期绑定访问；`Await Each` 在非 `Async` 方法内是否仍报错？——应报错，`Await` 的上下文要求与 Strict 无关。
- 两路径的 `DisposeAsync` / 取消语义完全一致（运行时行为不因编译选项改变）。

#### 7. IDE / IntelliSense 影响

- `Await Each` 的 `chunk` 补全与 InfoTip：`chunk` 显示 `T`（从 `IAsyncEnumerable(Of T)` 推导）。
- `Async Iterator Function` 的语法高亮、`Yield` 点标记、`Async`/`Iterator` 双修饰符的修饰符补全。
- 调试：组合状态机的"Step Into"体验——C# 8 的 async 迭代器在调试器里有专门的展示（`Suspect` 外部事实），VB 若不做，开发者会在状态机里迷路。
- 错误文案：非 `Async` 上下文里的 `Await Each`、"`Yield` 出现在 catch 内"、`IAsyncEnumerable` 泛型参数缺失等，全部要新设计。

#### 8. 数据 / 普遍性

流式异步是 .NET Core 3.0+ 的真实高频模式（分页、事件流、大文件），`IAsyncEnumerable` 是库与框架的标准返回形态（ASP.NET Core、EF Core、SignalR 都产它）。但——**没有 VB 侧的量化数据**，对照 Implicit-default-optional 的 85% 标准，这里一条统计都没有。主线对 `IAsyncEnumerable` 零讨论（Q1 `Suspect`）。C# 8 的采纳是外部信号，不是 VB 数据。We are not saying the pain is fake; we are saying we cannot measure it from this repo.

#### 9. 更简替代

- `Await stream.FetchChunksAsync(ct).ToListAsync()`（PROPOSAL C）：语义正确、零语法，但丢流式——对真流式场景不是替代，是劣化。
- `System.Linq.Async`（PROPOSAL D）：消费端回调式可用，但表达不了 `Exit For`/`Continue For`/循环体 `Await` 控制流；生产端只能靠手写状态机。是补充，不是替代。
- 手写状态机：现状，无人想维护。
- 结论：**没有"更简"的替代**——这正是需要语言语法的证据。但"需要"不等于"现在就要"：成本（状态机）巨大，替代虽然劣化但存在。

#### 10. 复杂度 / 成本 / 优先级

- 消费端 `Await Each`：中量。parser（`Await` + `Each` 判定）+ 绑定（元素类型）+ 降级（`For Each` 模式检测的异步版：`GetAsyncEnumerator`/`MoveNextAsync`/`DisposeAsync`）+ dispose 语义。**不依赖**组合状态机（消费的是别人生产的序列）。
- 生产端 `Async Iterator Function`：**大量**。组合状态机（await 挂起 × yield 产出）是 Roslyn 里最重的代码生成之一；还要处理 `Yield` 位置限制、取消、`ValueTask` 生命周期。**这是全案的成本主体**。
- 优先级：**生产端先行（PROPOSAL F）、消费端随行（PROPOSAL E 反向）**，或双端同落（PROPOSAL A）。单独落消费端（E）可立即消费 C# 生态的序列、成本中量——这是"短输赢"；单独落生产端（F）价值高但成本重。诚实排法：E 先行验证文法与 dispose，F 随后啃状态机，A 是终态。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题。`IAsyncEnumerable(Of T)` / `IAsyncEnumerator(Of T)` / `ValueTask(Of Boolean)` 都是既有 BCL 类型；组合状态机实现既有接口，不触达 CLR 存储规则。平台要求：`IAsyncEnumerable` 需要 netstandard2.1 / .NET Core 3.0+——**VBScript.NET 的目标平台若低于此，全案不成立**。这与主线 2017-08-09"由平台变化驱动优先级"的裁定同构。

#### 12. 值不值得做

- 价值：高（流式异步是真实高频场景，VB 缺席是 parity gap；措辞极 VB）。
- 成本：中-高（消费端中量、生产端大量组合状态机）。
- 风险：低（零破坏），但**范围未定义**——骨架建议把整座山打包，无分阶段、无取消/dispose spec、无原型。
- **结论：值得做，但不是现在、不是这份骨架。** 方向成立、措辞成立，落地需要先啃状态机原型与 spec 空白。若坚持骨架原样推进，我们会建议 Table——那等于让一座山在没有图纸的情况下开工。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）**——两处新语法今天都是语法错误，error→program，通过。这是本建议最干净的一张牌。
- **原则 2（保持 VB-like）**——`Await Each item In sequence ... Next` 读起来像英语，`Await` 前置与 VB 异步语序一致；`Async Iterator Function` 是 `Async Function` 与 `Iterator Function` 的直接组合，语法血缘纯正。
- **原则 3（不引入第二种做事方式）**——唯一的撞墙风险：`Await Each` 与物化形态 `For Each x In Await seq.ToListAsync()` 并存，是否"第二种写法"？我们判定**不是**：二者语义根本不同（流式 vs 物化），不是同一件事的两种写法，是不同能力。`System.Linq.Async` 是库层补充，与语法互补。这条我们比 for-each-enh 的 `Where` 案更放心——`Where` 与 LINQ 重叠是"同一过滤两种写法"，`Await Each` 与物化是"两种数据语义"。
- **原则 4（默认跟随 C#，除非有充分理由）**——方向与 C# 8 `await foreach`/async 迭代器一致，措辞 VB 化（`Await` 前置、`Next` 终止）。这是"跟随但 VB 化"的教科书案例。
- **原则 5（读起来像英语、对新手友好）**——`Await Each chunk In stream` 无需解释。满分。
- **原则 6（不为边缘场景加特性）**——流式异步不是边缘；但缺 VB 侧数据（Q8），是扣分点。
- **原则 7（避免隐蔽控制流/语义变化）**——`Await` 显式出现在语句头，异步语义可见，无细微字符变义。PROPOSAL B（`For Each Await`）被拒正是这条原则。通过。
- **原则 8（不与既有语法冲突）**——`Await` + `Each` 组合无既有冲突（`Each` 非合法表达式起始）；`Async Iterator` 修饰符组合无冲突（今天非法）。通过。
- **原则 9（消除常见样板）**——手写 `IAsyncEnumerator` 状态机、`ToListAsync` 物化绕行，都是高频样板。全中。
- **原则 10（冗长只在有用时是美德）**——`Await Each`、`Async Iterator` 都是显式关键字，在"让意图可见"处保留。符合。

对照主线表（2.3）：**本建议在对照表无对应行**——`Await Each` / `Async Iterator` 不是主线任何已批准/讨论项。最接近的主线上下文是异步平台演变：2017-08-09 对 Await in Catch 的裁定（优先级由 `IAsyncDisposable/Async Using` 驱动）、2018-02-21/28 对异步状态机复杂度的反复警示。**这是 Anthony 独立延伸**，方向与 C# 8 一致，措辞继承 VB 的 `For Each`/`Async`/`Iterator` 基因。与组 13 姊妹（agile-async、require-await-call、async-sub）的交互面在 Q6/Q8 已列出。

### RESOLUTION:

**RESOLUTION:** 方向成立，措辞成立，落地未就绪。消费端从 `Table` 解禁为 `Consider`，生产端维持 `Consider`，全案等待两件事：组合状态机原型 + spec 空白填充。

1. **`Await Each`（消费端）— `Consider`（解禁，先于生产端落地）。** 措辞极 VB（Q1），`Next` 终止（Q2，原文已答），dispose/取消/ConfigureAwait 语义按 Q3/Q4/Q6 定。落地前置：parser + 绑定 + 降级（`GetAsyncEnumerator`/`MoveNextAsync`/`DisposeAsync` 模式检测）+ dispose 语义的 speclet + 最小原型。它**不依赖**组合状态机——消费 C# 生态已有序列即可跑。`for-each-enh` 的 Table 裁定在本场被本决定更新：解禁的**前提**是消费端原型证明模式检测与 dispose 语义可做。
2. **`Async Iterator Function`（生产端）— `Consider`（维持，成本主体）。** 返回类型必须为 `IAsyncEnumerable(Of T)`（Q7，非泛型不存在）；`Async`+`Iterator` 组合状态机是全案成本主体（Q5），需单独设计文档 + 原型，并处理 `Yield` 位置限制（Q9）、取消、`ValueTask` 生命周期。**在组合状态机原型出来之前，生产端不升 Active。**
3. **PROPOSAL B（`For Each Await`）— 否决。** 语序别扭、与物化形态视觉混淆（原则 #7）。
4. **PROPOSAL C / D（物化 / LINQ 库）— 不采纳为替代，保留为库层补充。** 语义不同的能力，不是同一件事的两种写法（原则 #3 判定）。
5. **PROPOSAL G（返回类型）— 定为泛型 `IAsyncEnumerable(Of T)`。** 非泛型是已知错误，不是"待定"。
6. **与组 13 姊妹对表**：`Await Each` 的 `ConfigureAwait` 默认行为随 `agile-async` 统一；`Async Iterator Function` 返回序列不被 `Await` 消费的"漏用"诊断并入 `require-await-call` 规则面（Q8）；与 `async-sub` 的返回契约区分（Q4 交互）。
7. **平台前置**：`IAsyncEnumerable` 需 netstandard2.1+ / .NET Core 3.0+，先确认 VBScript.NET 目标平台（与主线 2017-08-09"由平台驱动优先级"同构）。

**Implication:**

- 返工建议原文：本建议是骨架，需重写为双端分离的两份设计（或一份明确分节）；补文法（BNF）、Option Strict 双路径、`DisposeAsync`/取消/`Yield` 位置限制 spec、`steam`→`stream` 笔误更正、`IAsyncEnumerable(Of T)` 返回类型更正、状态行占位链接替换为真实原型分支。
- 消费端最小原型：parser（`Await` + `Each`）+ 绑定（元素类型）+ 降级（异步 `For Each` 模式检测）+ `Exit For` 触发 `DisposeAsync`；验证语义模型与 IDE 补全。
- 生产端原型：`Async Iterator Function` 组合状态机，验证 `Await` 后 `Yield`、`Yield` 后 `Await`、`Using`/`Catch` 内位置限制、`ValueTask(Of Boolean)` 生命周期。
- 与 for-each-enh 对表：`Await Each` 循环作为命名循环目标（`Exit For outer`）；循环变量 lambda 捕获逐迭代副本行为共用 spec 待办。
- 与 agile-async / require-await-call 对表：ConfigureAwait 默认行为、未消费枚举器诊断。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 文档整体 `Consider`；其中 `Await Each`（消费端）`Consider`（解禁，先于生产端）、`Async Iterator Function`（生产端）`Consider`（成本主体，等原型）、PROPOSAL B 否决留档、PROPOSAL C/D 保留为库层补充。

### OPEN QUESTIONS / TODO / Follow-up

- `Suspect`：`IAsyncEnumerable` 在 vblang 主线零讨论（全量检索零命中），C# 8 `await foreach`/async 迭代器的具体行为（dispose 语义、`WithCancellation`、`ConfigureAwait`、catch/finally 内 yield 禁止、调试器展示）为外部事实，无法在本仓库核实——引用时需标注外部队标。
- `OPEN QUESTIONS`：`Await Each` 的枚举级取消——`WithCancellation` 式扩展是否进 v1，还是 v1 仅序列内建取消。
- `OPEN QUESTIONS`：`Await Each` 循环体内 `Await` 与 `SyncLock`/`Using` 内 `Await` 限制的交互是否与普通 `Await` 完全一致。
- `OPEN QUESTIONS`：`Async Iterator Function` 内 `Yield` 在 `Catch`/`Finally` 内的合法性——沿用 C# 8 的禁止，还是借 VB 的 GoTo 能力放宽（伴随状态机复杂度）。
- `OPEN QUESTIONS`：`Async Iterator Function` 的 `DisposeAsync` 抛异常与循环体异常并发时的聚合语义。
- `OPEN QUESTIONS`：`Option Infer Off` + `Option Strict Off` 下 `Await Each` 循环变量的显式类型要求，与 `for-each-enh` 裁定对齐。
- `TODO`：量化 VB 生态 `IAsyncEnumerable` 消费/生产场景的真实占比（对照 85% 统计标准）。
- `TODO`：确认 VBScript.NET 目标平台对 `IAsyncEnumerable`（netstandard2.1+）的支持。
- `Follow-up`：与 require-await-call 合并"未消费枚举器"诊断规则面；与 agile-async 合并 ConfigureAwait 默认行为。

### 状态

- **LDM 状态：`Consider`**；消费端 `Await Each` 从 Table 解禁为 Consider（等消费端原型）、生产端 `Async Iterator Function` Consider（等组合状态机原型）。
- **三态判定：Consider** — 方向与措辞成立、零破坏、是真实 parity gap；但骨架建议 + 组合状态机成本使 Active 无从谈起，Table 又埋没真实需求。Consider 是最诚实的落点。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-async-iterator.md` — `Await Each` 消费 + `Async Iterator Function` 生产 `IAsyncEnumerable`。
- 来源：Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1764–1774，`Await Each`/`Async Iterator Function` 双示例）与第 3.5 章 "For Each"（L945–948，`Await Each item In sequence ... Next` 的 `Next` 终止形态）。
- 配方目标：为 `IAsyncEnumerable` 提供对称的消费（`Await Each`）与生产（`Async Iterator Function`）语法，消除手写状态机与物化绕行。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 一句话点名 `IAsyncEnumerable` parity gap，但无受益者画像、无量化数据；示例不可编译（`As IAsyncEnumerable` 非泛型在 BCL 不存在、`steam` 笔误）；无原型（状态行 `Prototype: Complete` 指向占位链接） | 已提供 | 无运行证据封顶 2；消费/生产两端各自价值未拆开评估 |
| 特性 | **3/5** | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Await Each` 措辞极 VB（英文语序、`Next` 终止、继承 `For Each` 基因）；`Async Iterator Function` 是 `Async`+`Iterator` 组合；方向借自 C# 8 `await foreach`/async 迭代器（未标注） | 已检查 | 消费端与生产端两个成本/价值画像不同的特征打包在 one-size；C# 平行物未声明 |
| 品质 | **2/5** | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突"。六章节模板齐全、3 个未决问题具体诚实（1–3 健康区间）；但：无文法（BNF）、无 Compatibility/breaking-change、无 Option Strict 分叉、无边界/交互覆盖；示例无法编译（非泛型返回类型）；`steam` 笔误自认却未更正；状态行占位链接；"`Next` 是否带步进"其实是原文已答的问题却被列为未决 | 已检查 | 红旗：示例与 BCL 事实冲突（非泛型 `IAsyncEnumerable` 不存在）；关键边界（dispose/取消/yield 位置）完全空白 |
| 属性 | **3/5** | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。对 VBScript.NET：流式异步强化核心场景（水/光正向）、消样板加速迭代（雷正向）；但组合状态机成本巨大（雷/暗）、消费端先行的范围未定义（风=一致性）、依赖平台 `IAsyncEnumerable`（暗）、文档对成本/平台/范围零权衡 | 已检查（预测待定） | 成本主体（组合状态机）未识别；平台依赖未确认；实际影响须"已采纳"后定 |
| 炼金成分 | **3/5** | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。主要材料可辨认：Anthony 原文第 11 章 + 第 3.5 章；继承 VB `Iterator`/`Yield`（2014-02-17 落地）与 `Async` 基因；**借鉴 C# 8 `await foreach`/async 迭代器未标注**；与组 13 姊妹（agile-async/require-await-call/async-sub）的交互未声明 | 已检查 | C# 平行物未标注；跨建议依赖未交叉引用；`steam` 笔误/非泛型返回类型的来源问题未甄别 |

### 设计原则对照

- **与 VB 基因**：部分一致。措辞极 VB（原则 #5 满分、原则 #9 消样板全中）、零破坏（原则 #1 通过）、显式异步无隐蔽语义（原则 #7 通过）；"第二种做事方式"判定为不同能力而非重叠（原则 #3 可辩护）；方向跟随 C# 8 但 VB 化（原则 #4 教科书案例）。缺口：无 Option Strict 双路径（原则 #6 证据）、无边界覆盖。
- **与主线关系**：**Anthony 独立延伸**——对照表 2.3 无对应行；vblang 主线对 `IAsyncEnumerable` 零讨论。方向与 C# 8 一致；措辞继承 VB `For Each`/`Async`/`Iterator`。与组 13（agile-async/require-await-call/async-sub）、for-each-enh（`Await Each` Table 依赖本建议）交叉。主线唯一相关上下文是异步平台演变（2017-08-09"由 `IAsyncDisposable/Async Using` 驱动"、2018-02-21/28 状态机复杂度警示）。
- **破坏性变更**：无——两处新语法今天均为语法错误，错误变程序。风险面：Option Infer Off 宽松路径与 Strict 路径的行为分叉；`Async Iterator Function` 返回序列不被消费的"漏用"诊断归属。

### 总评

- **达成程度**：**部分达成**。方向与措辞成立（`Await Each` 是极 VB 的消费语法、`Async Iterator Function` 是基因纯正的生产语法）、零破坏、是真实 parity gap；但建议是骨架——无文法、无 Option Strict、无 dispose/取消/`Yield` 位置边界、示例无法编译、成本主体（组合状态机）未识别、无原型。
- **LDM 三态建议**：**Consider**。消费端 `Await Each` 从 Table 解禁为 Consider（消费端原型证明模式检测与 dispose 语义后可 Active）；生产端 `Async Iterator Function` Consider（组合状态机原型出来前不升 Active）；全案范围需重写为双端分离设计。
- **主要问题**：(1) 骨架建议把消费+生产+状态机+取消+dispose 一整座山打包，无分阶段路径；(2) 示例无法编译（非泛型 `IAsyncEnumerable`、`steam` 笔误），且未决问题把"原文已答的 `Next`"列为未决；(3) 成本主体（组合状态机）与平台依赖（netstandard2.1+）未识别；(4) 无文法、无 Compatibility、无 Option Strict 分叉、无 dispose/取消 spec；(5) C# 8 平行物未标注。

### 返工建议

- **范围切分**：消费端（`Await Each`）与生产端（`Async Iterator Function`）拆为两份文档或显式分节；各自独立文法、独立成本、独立原型路径。消费端标注"可先于生产端落地、消费 C# 生态序列"。
- **补充章节**：文法（BNF：`Await Each` 语句与 `Await <expr>` 的区分、`Async Iterator Function` 修饰符组合与 `Sub` 禁止、`Next` 终止与可选变量名）；Compatibility/breaking-change（error→program、`langversion` 门控）；`Option Strict On/Off` + `Option Infer On/Off` 双路径验证示例；`DisposeAsync`（`Exit For`/异常/`GoTo` 触发）、取消（内建 + `WithCancellation` 是否进 v1）、`Yield` 位置限制（`Using`/`SyncLock`/`Catch`）spec。
- **补充证据**：消费端最小原型（parser + 绑定 + 降级 + dispose 语义 + 语义模型 + IDE 补全）；生产端组合状态机原型（`Await`↔`Yield` 混合、`ValueTask(Of Boolean)` 生命周期、调试器展示）；VB 生态 `IAsyncEnumerable` 场景占比数据；VBScript.NET 目标平台对 netstandard2.1+ 的确认。
- **未决问题处理**：`steam`→`stream` 更正；`IAsyncEnumerable` 定为泛型 `IAsyncEnumerable(Of T)`（非泛型 BCL 不存在，非"待定"）；`Next` 终止确认为原文已答（L945–948），转向 `Next item` 变量名与 `Exit For`/`Continue For` 命名循环的 spec 细节；`Await Each` 与 `For Each` 的循环变量 lambda 捕获、`Option Infer Off` 显式类型要求并入 for-each-enh 的共享待办。

---

## 附录：C# 生态与互操作考量

本提案（`Await Each` 消费 + `Async Iterator Function` 生产）在 C# 侧有**已完成 feature**的直接对应：**C# 8 async streams**（`IAsyncEnumerable<T>`、`await foreach`、async 迭代器）。它不是"C# 未来方向"，而是 2018–2019 已落地、现为 .NET 主流流式范式的既有事实。以下原文均在 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像）核实。

### 一、相关 C# 现实方向

**1. 平台 / BCL 基础。** C# 8 推动 `IAsyncEnumerable<T>`/`IAsyncEnumerator<T>`/`IAsyncDisposable` 进入核心库（netstandard2.1 / .NET Core 3.0+）：`GetAsyncEnumerator` 带可选 `CancellationToken`、`MoveNextAsync` 返回 `ValueTask<bool>`、`Current` 协变、`IAsyncEnumerator<T>` 继承 `IAsyncDisposable`。提案原文接口：

> ```csharp
> public interface IAsyncEnumerable<out T>
> {
>     IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
> }
> public interface IAsyncEnumerator<out T> : IAsyncDisposable
> {
>     ValueTask<bool> MoveNextAsync();
>     T Current { get; }
> }
> ```
> — → `proposals\csharp-8.0\async-streams.md`（「IAsyncEnumerable / IAsyncEnumerator」）

这与本提案 Q7 的判断一致（BCL 只有泛型 `IAsyncEnumerator(Of T)`），且是 RESOLUTION 第 7 条"平台前置 netstandard2.1+"的源头——**这个平台底线本身就是 C# 8 推出来的**（索引 T1：C# 是 CLR 新特性与 .NET 生态的主要推动者）。

**2. 消费端语法：`await foreach`，await 前置。** C# LDM 在 `foreach await` / `foreach async` / `async foreach` 三个候选里选了 `await foreach`，理由是显式 await 与既有惯例一致：

> "In the past we've followed the pattern that there's always an explicit `await` in the code if we're awaiting. There's some debate on what sounds best."
> **Conclusion**: "`await foreach (...)` and `await using (...)`."
> — → `meetings\2018\LDM-2018-10-15.md`（「Syntax of `foreach await`/`using await`」）

这与本提案 Q1 的措辞判断（`Await Each` 优于 `For Each Await`）**互证**——两门语言都落在"显式 await、await 在前"；C# 侧 `foreach await`（≈ 本提案 PROPOSAL B 的 `For Each Await`）同样被否决。

**3. 生产端语法：async 迭代器。** `async` 修饰 + 出现 `yield` 即判定为 async iterator，返回类型**必须显式**为 `IAsyncEnumerable<T>` 或 `IAsyncEnumerator<T>`：

> "The existing language support for iterators infers the iterator nature of the method based on whether it contains any `yield`s. The same will be true for async iterators. Such async iterators will be demarcated and differentiated from synchronous iterators via adding `async` to the signature, and must then also have either `IAsyncEnumerable<T>` or `IAsyncEnumerator<T>` as its return type."
> — → `proposals\csharp-8.0\async-streams.md`（「Async Iterators / Syntax」）

LDM 更早定调的组合本质：

> "Same as current iterators, except with an `async` keyword, and IAsyncEnumerable/tor return type. Needs to feel exactly like putting iterators and async methods together."
> — → `meetings\2017\LDM-2017-08-30.md`（「iterators」）

"putting iterators and async methods together" 正是本提案 Q5 说的"两套状态机叠乘"——C# 自己也把 async iterator 定义为 iterator 与 async 的直组合，状态机工程量是 C# 的真实成本。注意：C# 允许返回 `IAsyncEnumerator<T>`（不一定是 `IAsyncEnumerable<T>`），本提案 Q7 只考虑了 `IAsyncEnumerable(Of T)`——这是 C# 多出来的一个自由度，VB 可决定是否跟进。

**4. 取消：双路 + `[EnumeratorCancellation]`。** C# 8 最终采用"序列内建 + 枚举级 `WithCancellation`"双路，用属性把两路统一：

> "We find that a reasonable compromise to support both scenarios in a way that is convenient for both producers and consumers of async-streams is to use a specially annotated parameter in the async-iterator method. The `[EnumeratorCancellation]` attribute is used for this purpose. Placing this attribute on a parameter tells the compiler that if a token is passed to the `GetAsyncEnumerator` method, that token should be used instead of the value originally passed for the parameter."
> — → `proposals\csharp-8.0\async-streams.md`（「Cancellation」）

两路消费场景（同节原文）："`await foreach (var i in GetData(token)) ...` where the consumer calls the async-iterator method"；"`await foreach (var i in givenIAsyncEnumerable.WithCancellation(token)) ...` where the consumer deals with a given `IAsyncEnumerable` instance."。LDM 决策同步确认（`GetAsyncEnumerator(CancellationToken token = default)` + 提供 `WithCancellation` 扩展方法）：→ `meetings\2018\LDM-2018-11-28.md`（「Cancellation of async streams」）。这验证了本提案 Q4 的 `Suspect`——C# 不是二选一，而是双路并存。

**5. 模式化消费（pattern-based）。** `await foreach` 同时支持接口与 duck-typing pattern，且 pattern 绑定显式传 `default(CancellationToken)`：

> "`foreach` will be augmented to support `IAsyncEnumerable<T>` in addition to its existing support for `IEnumerable<T>`. And it will support the equivalent of `IAsyncEnumerable<T>` as a pattern if the relevant members are exposed publicly, falling back to using the interface directly if not, in order to enable struct-based extensions that avoid allocating as well as using alternative awaitables as the return type of `MoveNextAsync` and `DisposeAsync`."
> — → `proposals\csharp-8.0\async-streams.md`（「foreach」）

> "We will attempt to bind the call `e.GetAsyncEnumerable(default(CancellationToken))` and if it succeeds and has a conforming return type, the pattern binds successfully."
> — → `meetings\2018\LDM-2018-12-12.md`（「Pattern binding」）

本提案 RESOLUTION 第 1 条的降级设计（`GetAsyncEnumerator`/`MoveNextAsync`/`DisposeAsync` 模式检测）与 C# 同构。注意 C# 的 pattern 签名把 `CancellationToken` 参数纳入——VB 若要互操作 C# 的 struct enumerator（零分配路径），模式签名必须同样考虑 token 参数。

**6. `await foreach` 禁止 dynamic。** C# LDM 明确否决对 dynamic 的异步枚举，理由是非泛型 `IAsyncEnumerable` 不存在：

> "Block it. For synchronous foreach we resort to the nongeneric `IEnumerable`, but there is no nongeneric `IAsyncEnumerable`, and there won't be."
> — → `meetings\2018\LDM-2018-05-21.md`（「foreach await over dynamic」）

这是 Q7"非泛型不存在"的又一权威佐证，并对本提案 Q11 的 `Option Strict Off` 宽松路径构成压力（见下节）。

**7. yield 位置限制：C# 长期禁止、2024 仍未放开。** 本提案 Q9 标 `Suspect` 的"C# 8 禁止在 catch/finally 里 yield"——仓库内核实为**属实**（sync iterator 的长期规则，async iterator 继承），且 2024 年 C# 才考虑对 *sync* iterator 放宽、尚无结论：

> "The proposal is to allow `yield` inside `try`/`catch` blocks in iterators, which is a longstanding user request. There are challenges, both technically and in matching programmer expectations. These particularly arise during `dispose`."
> "No conclusion was reached. We'll reconsider this in a future meeting after offline work and discussions."
> — → `meetings\2024\LDM-2024-10-28.md`（「`yield` in `try` / `catch`」）

**8. LINQ：语言层不做，推荐社区 Ix。** C# 8 明确不为 `IAsyncEnumerable<T>` 复制 ~600 个 LINQ 重载，而是推荐社区库：

> "But Ix already has an implementation of many of these, and there doesn't seem to be a great reason to duplicate that work; we should instead help the community improve Ix and recommend it for when developers want to use LINQ with `IAsyncEnumerable<T>`."
> — → `proposals\csharp-8.0\async-streams.md`（「LINQ」）

本提案 PROPOSAL D（依赖 `System.Linq.Async`）与 C# 官方立场一致。

### 二、现实 vs 提案

| 维度 | C# 现实（来源） | 本提案立场 | 判定 | 理由 |
|---|---|---|---|---|
| 消费端措辞 | `await foreach`，await 前置，否决 `foreach await`（LDM-2018-10-15） | `Await Each`，await 前置，否决 `For Each Await`（Q1） | **兼容（互证）** | 两语言同落"显式 await、await 在前"；C# 否决的 `foreach await` 正是 PROPOSAL B 的对应物 |
| 生产端形态 | async iterator = `async` + `yield`，显式 `IAsyncEnumerable<T>`/`IAsyncEnumerator<T>`（async-streams.md；LDM-2017-08-30） | `Async Iterator Function`，显式 `IAsyncEnumerable(Of T)`（Q7） | **兼容** | 组合状态机、显式返回类型同源；C# 多允许 `IAsyncEnumerator<T>` 返回，VB 可跟进 |
| 模式化消费 | pattern + interface 双轨，绑定 `GetAsyncEnumerator(default(CancellationToken))`（LDM-2018-12-12） | 降级 = 模式检测（RESOLUTION 1） | **兼容（有细节）** | 同构；但 VB 模式签名需考虑 token 参数，否则消费不了 C# struct enumerator |
| 取消 | 双路：内建 + `WithCancellation`/`[EnumeratorCancellation]`（async-streams.md；LDM-2018-11-28） | 双路倾向（Q4，OPEN QUESTION） | **需桥接** | VB 需识别 `[EnumeratorCancellation]` 元数据或定义等价机制；Q4 可参照 C# 已定方案收口 |
| 动态/晚期绑定 | 禁止 `await foreach` over dynamic；"no nongeneric `IAsyncEnumerable`"（LDM-2018-05-21） | `Option Strict Off` 下 chunk 为 `Object`（Q11） | **冲突/需决策** | C# 无运行时类型可枚举而整体禁止；VB 若要晚期绑定 `Await Each` 需自造反射 adapter，或对齐 C# 禁止 |
| yield 位置 | 长期禁 catch/finally 内 yield；2024 sync 未决（LDM-2024-10-28） | Q9 借 GoTo 放宽（OPEN QUESTION） | **脱节** | 放宽与 C# 长期惯例相反且状态机成本更高；建议 v1 沿用禁止 |
| ConfigureAwait | enumerable 包装（`enumerable.ConfigureAwait(false)`），非语句内 per-await（async-streams.md「ConfigureAwait」） | 随 agile-async 统一（Q6） | **兼容** | 都非 per-await 语法；VB 若走包装需与 agile-async 机制对齐 |
| LINQ 支持 | 语言层不做，推荐 Ix（async-streams.md「LINQ」） | PROPOSAL D 库层补充 | **兼容** | 与 C# 官方立场一致 |

### 三、对 VBScript.NET 的适应建议

1. **默认安全、按需动态。** `Await Each`/`Async Iterator Function` 的**默认路径**编译为与 C# 同构的状态机（实现 `IAsyncEnumerable(Of T)`、`MoveNextAsync` 返回 `ValueTask(Of Boolean)`），零反射、AOT/trimming 友好——符合索引 T5/T6"C# 用编译期产物替代运行时动态"的方向。晚期绑定路径（`Option Strict Off` 的 Object 序列）要么对齐 C# 禁止动态异步枚举（安全默认），要么做成**显式 opt-in** 的反射 adapter（如 `seq.AsAsyncEnumerable(Of T)`），不隐式反射——与决策文件 M2/M5"脚本层允许动态、编译产物走类型化"的双模路线一致。
2. **跨语言消费是最大卖点。** VB 消费 C# 生产的 `IAsyncEnumerable(Of T)` 在 BCL 层零障碍（同一接口）；关键在**降级模式检测要覆盖 C# 的结构化枚举器**（struct enumerator + pattern-based），即模式签名与 C# 一致地绑定 `GetAsyncEnumerator(default(CancellationToken))`。否则 C# 生态里大量 struct-based 流式类型在 VB 端会被降级为接口装箱，损失 C# 特意争取的零分配路径。这也加强 RESOLUTION 第 1 条"消费端先行"——C# 生态已大量生产序列，VB 消费端先行可立即互操作。
3. **source-gen 桥。** 异步迭代器状态机本身是编译器产物，不需要额外 source generator；但 VBScript.NET 的脚本编译管线（决策文件 M5：编译到受管程序集 + source-gen 桥）应保证生成的状态机类型与 C# source-gen 生成的 `IAsyncEnumerable` 类型在模式检测层互通。
4. **识别新元数据。** VB 编译器必须认识 `[EnumeratorCancellation]`（取消绑定，async-streams.md「Cancellation」）、`IAsyncDisposable`/`ValueTask` 的 awaitable/disposable 模式（async-streams.md 接口）。参照决策文件 M8：C# 新特性往往带新特性标志/新 BCL 契约，VB 不认识就无法跨语言正确校验。
5. **平台前置。** `IAsyncEnumerable` 需 netstandard2.1 / .NET Core 3.0+（RESOLUTION 第 7 条）——这正是 C# 8 推出来的平台底线；VBScript.NET 若目标平台低于此，全案连同跨语言消费都不可用，需先确认。

### 四、对既有 RESOLUTION / 三态判定的影响

**基本无影响——三态维持 `Consider`；但本附录把三处 `Suspect` 外部事实变为仓库内可核实，并把一个 OPEN QUESTION 收口：**

- **Q7（非泛型 `IAsyncEnumerable` 不存在）**：升级为**已核实**。双证："there is no nongeneric `IAsyncEnumerable`, and there won't be."（LDM-2018-05-21）+ "Block it."（同一会议对 dynamic 异步枚举的否决）。非泛型是已知错误，RESOLUTION 第 5 条维持。
- **Q1（措辞）**：得到外部旁证。C# LDM 选 `await foreach` 而非 `foreach await`（LDM-2018-10-15，"there's always an explicit `await`"），与 `Await Each` 的 await 前置同构；PROPOSAL B 在 C# 侧有被否决的对应物。PROPOSAL B 否决维持。
- **Q4（取消）**：OPEN QUESTION 可参照 C# 已定方案收口——C# 是"序列内建 + `WithCancellation`/`[EnumeratorCancellation]`"双路（LDM-2018-11-28 + async-streams.md），不是二选一。VB v1 至少应支持 `[EnumeratorCancellation]` 识别，否则 VB 与 C# 生产者互操作时取消语义不对称。
- **Q9（yield 位置）**：`Suspect` 升级为**已核实**（sync 长期禁止、2024 未决）。"借 GoTo 放宽"是脱节方向，建议 v1 沿用 C# 的禁止。
- **新增互操作要求**（建议并入返工清单）：`Async Iterator Function` 的参数应支持 `[EnumeratorCancellation]` 或等价的 VB 机制，否则 VB 生产的序列在 C# 消费端用 `WithCancellation` 时无法覆盖内建 token——跨语言取消语义不完整。此点在原 RESOLUTION/OPEN QUESTIONS 中缺失。

### 五、引用清单（逐字已核实）

- "C# has support for iterator methods and async methods, but no support for a method that is both an iterator and an async method. We should rectify this by allowing for `await` to be used in a new form of `async` iterator…" → `proposals\csharp-8.0\async-streams.md`（Summary）
- "The existing language support for iterators infers the iterator nature of the method based on whether it contains any `yield`s. The same will be true for async iterators." → `proposals\csharp-8.0\async-streams.md`（「Async Iterators / Syntax」）
- "We find that a reasonable compromise… is to use a specially annotated parameter in the async-iterator method. The `[EnumeratorCancellation]` attribute is used for this purpose." → `proposals\csharp-8.0\async-streams.md`（「Cancellation」）
- "there's always an explicit `await` in the code if we're awaiting." → `meetings\2018\LDM-2018-10-15.md`
- "there is no nongeneric `IAsyncEnumerable`, and there won't be." → `meetings\2018\LDM-2018-05-21.md`
- "Needs to feel exactly like putting iterators and async methods together." → `meetings\2017\LDM-2017-08-30.md`
- "which is a longstanding user request. There are challenges, both technically and in matching programmer expectations. These particularly arise during `dispose`." → `meetings\2024\LDM-2024-10-28.md`
- "But Ix already has an implementation of many of these, and there doesn't seem to be a great reason to duplicate that work; we should instead help the community improve Ix…" → `proposals\csharp-8.0\async-streams.md`（「LINQ」）

**OPEN QUESTIONS（本附录未进一步核实、留给后续）**：C# async iterator 生成的 IL 状态机特性的具体形态（`[AsyncIteratorStateMachine]` 在 csharplang 仓库无正文，属 Roslyn 编译器事实）；C# `ConfiguredAsyncEnumerable<T>` 等包装类型未来是否会进入 ref struct 领域（与 C# 13 ref struct interfaces / 索引 T2 的潜在连接）；`System.Linq.Async` 的运行时形态（属 dotnet/runtime 生态，本库无正文）。
