# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的话题很短但很硬：`Async Sub` 的返回类型与"默认异步类型配置"。这份建议把我们熟悉的两种冲动——"消除样板"与"让写法更像自然语言"——推到了与语言根基（Sub/Function 之分、声明即事实）相撞的位置。我们用了大量时间核对现行规范里 `Async Sub` 到底承诺了什么，结果是：建议的核心语法在今天的文法下**无法被消费**。这句话值得展开说清楚。

## Agenda

* [Proposal: Async Sub 返回类型与默认异步类型配置](#proposal-async-sub-返回类型与默认异步类型配置)

## Proposal: Async Sub 返回类型与默认异步类型配置

_Related: Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1720–1763）；[vblang spec – statements.md "Async Methods"](https://github.com/dotnet/vblang/blob/master/spec/statements.md)；[vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang 2018.12.19 LDM – Pattern Matching 总评](https://github.com/dotnet/vblang/blob/master/meetings/2018/vbldm-notes-2018.12.19.md)；ModVB：`require-await`、`Agile Async`（同章相邻建议）_

### 场景与缺口

We started from the proposal's opening claim, which is Anthony's, not the mainline's：今天可等待的异步方法必须写成 `Async Function ... As Task`，"即便没有返回值也必须用 `Function` 伪装"，形成原文所称的 "Function As Task" 别扭形式：

```vb
' 今天：可等待的异步方法必须是 Function，且返回 Task / Task(Of T)。
Async Function Flush() As Task
    Await Task.Delay(100)
End Function

' 今天：Async Sub 是 fire-and-forget（void）。规范对它单独定义：
' "If control flow exits through an unhandled exception, then that exception is
'  propagated to the environment in some implementation-specific manner."
' 它不能被 Await，异常无处可等。
Async Sub FlushFireAndForget()
    Await Task.Delay(100)
End Sub
```

建议想要三件事，原文用一组例子包在一起（Anthony 第 11 章 L1720–1739，逐字）：

```vb
' New syntax for true "Async Sub"s.
' No more "Function As Task" weirdness.
Async Sub Flush() As Task
    ...
End Sub

' Default awaitable type for lightweight
Async Sub MkDir() As ValueTask

' Lightweight Async syntax with configurable default async
' types and name-suffixing. e.g.
' Public methods use Task, Private methods use ValueTask.

' Same as `Async Sub Flush() As Task` (configurable).
' Same as `Function FlushAsync() As Task.
Async Sub Flush()

' Same as `Async Function Pull(...) As Task(Of JsonObject).
' Same as `Function PullAsync(...) As Task(Of JsonObject).
Async Function Pull(...) As JsonObject
```

We 先承认这一面：**"让一个无返回值的操作声明成 `Sub`，却又能等待"在直觉上是成立的**，脚本语言里尤甚；`Async Sub ... As Task` 读起来确实比 `Async Function ... As Task` 更贴近"这是一个动作"。但直觉成立不等于设计成立。我们在动笔前把现行文法逐条对了一遍，发现这条路的入口就是堵死的。

### 候选方案

We 把原文的捆绑拆成四个相互独立的切片。原文把它们写成一个特性，我们认为它们是四个特性，各自的成本与风险完全不同：

**PROPOSAL A — 显式 `Async Sub ... As Task/ValueTask`（仅此，不含省略与配置）。** 允许 `Sub` 带 `As` 子句，且仅当带 `Async` 修饰符时、仅限 awaitable 类型。不加默认值、不改名字。

**PROPOSAL B — 结果类型省略：`Async Function Pull(...) As JsonObject` 隐式等价于 `Task(Of JsonObject)`。** 调用者写 `Await Pull()`，但声明读起来像同步返回 `JsonObject`。

**PROPOSAL C — 省略返回类型 + 可配置默认异步类型：裸 `Async Sub Flush()` 按配置生成 `Task`/`ValueTask`（如"Public 方法用 Task，Private 方法用 ValueTask"）。**

**PROPOSAL D — 名称后缀自动生成：`Async Sub Flush()` 等价于 `Function FlushAsync() As Task`——编译器按配置补 "Async" 后缀。**

**PROPOSAL E — 什么都不做。** 维持 `Async Function ... As Task`；把"限制 fire-and-forget 扩散"交给 require-await 工作项（见 Anthony 18.15 的自述，他是真要解决这件事的）。

### 权衡：Q&A

- **A 的核心追问：Sub 调用是语句，还是表达式？** 这是整场最重要的一问。现行规范 `InvocationStatement` 是 `'Call'? InvocationExpression StatementTerminator`，且明确 "Any value resulting from the evaluation of the invocation expression is discarded"；`Await` 则要求"a single expression which must be classified as a value"。一个 `Sub` 调用被分类为 void——不是值。所以即便我们允许 `Async Sub Flush() As Task`，调用端仍是死路：

```vb
' 假设 A 生效：Sub 可以声明 As Task。
Async Sub Flush() As Task
    Await Task.Delay(100)
End Sub

Dim t As Task = Flush()   ' 错误：Sub 调用是语句，不是表达式，不能出现在赋值右侧
Await Flush()             ' 错误：Await 需要"被分类为值"的表达式；Sub 调用被分类为 void
Flush()                   ' 唯一合法的用法：当语句，值被丢弃 —— 与今天的 fire-and-forget 无差别
```

`Sub` 的调用语法在 VB 里从来没有被分类为"值"。要让 `Await Flush()` 成立，必须让"带返回类型的 Sub 调用"出现在表达式位置——那等于把 Sub 变成 Function。**A 不解决这个问题就没有任何可等待的消费者；解决了这个问题就不是 A 了。** `Suspect`：原文从未触碰这条文法现实，`Probably` 是假设了 `As Task` 会自动让调用可作表达式，而这个假设不成立。

- **A vs B：`As JsonObject` 是声明与事实不符。** 规范 `type-members.md` 说得很硬："An async method ... must be either a subroutine, or a function with return type `Task` or `Task(Of T)` for some `T`"。即今天的 `Async Function Pull(...) As JsonObject` 是**编译错误**。B 把一个错误变成"包一层 Task"的新含义。问题不在错误变合法，而在：源码写着 `As JsonObject`，元数据却是 `Task(Of JsonObject)`——**声明类型 ≠ 实际类型**。读代码的人理所当然地以为 `Pull()` 同步返回 `JsonObject`。这正是我们历史上否决 `Return?` 的同一类设计：细微字符改变语义。`Function Pull(...) As JsonObject` 与 `Function Pull(...) As JsonObject` 只差一个 `Async` 修饰符，返回类型就完全不同。

- **B 还有一个真破坏：任务型结果类型被重解释。** `Async Function Read() As MyAwaitable`（`MyAwaitable` 是自定义 awaitable）**今天合法**，语义是"返回 `MyAwaitable`"。B 的"是否包 Task"分类规则必须按 `X` 是否 awaitable 决定：awaitable 就不包、非 awaitable 就包。于是同一个 `As X` 的语义取决于 `X` 的类别，读者要心里跑一遍 awaitable 判定才能读签名；而且任何把自定义 awaitable 当返回类型写的现有代码，语义都会变。这是破坏性变更，不是增量。

- **C 直接改写现有 `Async Sub` 的含义。** 今天的裸 `Async Sub` 有明确定义：void、fire-and-forget，规范单列了它的异常路径与 `SynchronizationContext` 行为（UI 程序里未处理异常回投 UI 线程）。C 把裸 `Async Sub` 变成"按配置返回 Task"。那么**每一行今天合法的 `Async Sub` 重编译后行为都变了**——异常从"回投同步上下文"变成"Task 进入 Faulted"；而且 `Async Sub` 最普遍的真实用途是事件处理器，事件委托要求 void 签名：

```vb
' 今天合法、且是最常见的 Async Sub 用法：
Private Async Sub LoadButton_Click(sender As Object, e As EventArgs) Handles LoadButton.Click
    Await LoadAsync()
End Sub

' 规范：handler 合法的判据是 "AddHandler E, AddressOf M" 合法。
' EventHandler 委托是 void Sub；若裸 Async Sub 默认返回 Task，
' AddressOf 返回类型不匹配 → Handles 绑定失败，重编译即破。
```

We will almost never make breaking changes（2018.12.19 会议重申 "No breaking changes" 为第一条总原则）。C 一次破了不止一处。

- **C 的"配置化"是比 Option Strict 更危险的分叉。** Option Strict 只改变绑定的严格程度，从不改变一个方法的签名。C/D 让**元数据签名随可访问性与项目配置漂移**：同一份源码，项目 A 编出 `Flush() As Task`、项目 B 编出 `Flush() As ValueTask`。把 A 的二进制引用进 B 的源码，签名对不上；配置一改，整库的公开表面跟着变。签名应当是源码的投影，不应当是配置的投影。

```vb
' 同一源码，两个项目配置不同 → 元数据不同。
' 项目 A：Public 方法默认 Task。项目 B：Public 方法默认 ValueTask。
Public Async Sub Flush()
' A 编出：Flush() As System.Threading.Tasks.Task
' B 编出：Flush() As System.Threading.Tasks.ValueTask
' 跨项目引用：签名不匹配。
```

- **D 的名后缀是元数据造假。** 规范 `statements.md` 对命名只说了一句："By convention async methods are named with the suffix 'Async'"——**是约定，不是编译规则**。让编译器把 `Flush` 编成 `FlushAsync`，等于把程序员的选择权没收，并连带破坏 `NameOf(Flush)`（返回源码名还是元数据名？）、反射、晚期绑定（`CallByName(Me, "Flush", ...)` 运行时找不到 `FlushAsync`）、`Overrides`/`Implements`/`Handles`（全部按元数据名绑定）。建议原文连 `NameOf` 都没有提。

- **A 还有一个独立的守旧理由：Sub/Function 之分是 VB 最深层的直觉之一。** "Sub 无返回值、Function 有返回值、Sub 调用是语句"三者相互支撑。允许 `Sub ... As Task` 是对这一直觉的第一次裂缝；`SubSignature` 文法今天**根本没有** `As` 子句（`FunctionSignature` 才有），而且这一改动不只动 `SubDeclaration`，还要动 `MustOverrideSubDeclaration`、`InterfaceSubDeclaration`、Sub lambda 签名——表面远比想象的大。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`SubSignature` 今天没有 `As` 子句——加一个是新文法，本身无歧义。真正的歧义在 B：`Async Function Pull(...) As JsonObject` 里 `As JsonObject` 到底指"返回类型"还是"结果类型"？分类规则依赖 `JsonObject` 是否 awaitable，这是**上下文相关语义**。嵌套更糟：

```vb
' 若 "As X = 把 X 包进 Task(Of X)" 且 "X 是 awaitable 就不包"：
Async Function Read() As Task(Of String)
' Task(Of String) 是 awaitable → 不包 → 返回 Task(Of String)。可读性还好。
Async Function ReadOuter() As Task(Of Task(Of Integer))
' Task(Of Task(Of Integer)) 是 awaitable → 不包。读者能猜对，但为什么猜？因为"返回类型"这类知识藏在 awaitable 分类里。
Async Function Custom() As MyAwaitable
' MyAwaitable 是自定义 awaitable → 不包。但若它哪天不是 awaitable 了呢？同样源码，语义翻转。
```

读者需要知道"`X` 是否 awaitable"才能读懂一个签名——这不是"读起来像英语"，这是"读起来像编译器"。

#### 2. 角案例与边界语义

- **事件处理器**：`Handles` 要求处理器与事件委托签名兼容（"A handler method `M` is considered a valid event handler for an event `E` if the statement `AddHandler E, AddressOf M` would also be valid"）。`Async Sub` 的最大存量用途是事件处理器；C 让它们集体失效。
- **接口与抽象**：`InterfaceSubDeclaration` / `MustOverrideSubDeclaration` 用 `SubSignature`（无 `As`）。A 若要完整，必须同时给接口/抽象方法开口——接口里一个 `Sub M() As Task` 到底是什么？实现方用 `Function` 还是 `Sub` 去实现？
- **`Overrides`**：基类 `MustOverride Sub Flush()` 是 void；派生类 `Async Sub Flush() As Task` 签名不同，不能 `Overrides`。A 只对**新方法**可用，存量继承树一律用不上。
- **可变结构体复制**：规范已规定 async/iterator 方法在结构体上操作的是调用时刻的副本（`s.Mutate()` 不改 `s.x`）。A 不改变这条，但值得确认 `Async Sub ... As Task` 的完成时机与副本生命周期一致。
- **`ByRef` 参数**：规范规定 async 方法 "must have no `ByRef` parameters"——A/B/C/D 都不放宽，这是既定约束，无需重审。
- **`ValueTask` 的坑**：`ValueTask` 只能等待一次、不能存储。默认"轻量场景用 ValueTask"是**性能策略**，而可访问性（Public/Private）不是性能信号。把性能策略编进类型系统，且是按可访问性——这层耦合我们没有看到任何论据。
- **`GoTo` / `OnError`**：主线在 2018-02-21 讨论 Await in Catch/Finally 时说过 "GoTo and OnError seem unlikely in 'modern' code that would use Async"。A 不新增状态机复杂度，不展开。

#### 3. 作用域与绑定

C/D 下语义模型的返回值类型是**配置依赖**的：同一句 `Async Sub Flush()`，`GetTypeInfo` 返回 `Task` 还是 `ValueTask`，取决于项目配置；D 下语义模型里的符号名 `Flush` 与元数据名 `FlushAsync` 分离。Roslyn 的 semantic model、分析器、源生成器全都假设"源码符号 = 元数据符号"。让这个不变量破掉，是编译器级的地基改动，不是特性。

#### 4. 与既有特性的交互

- **事件 / `AddHandler` / `Handles`**：如上，void 委托是硬约束。
- **`NameOf`**：D 下 `NameOf(Flush)` 的结果未定义（源码名还是元数据名？）。建议未提及。
- **晚期绑定 / Option Strict Off**：`obj.Flush()` 在宽松模式下按运行期名查找。D 的改名让宽松模式下先期编译通过、运行期 `MissingMemberException`。
- **`Await` 在 `Catch`/`Finally`**：主线的真痛点（#37，2017-08-09 原则批准、2018-02-28 "Let's do it!"）。这与本建议正交——状态机复杂度在 Function 与 Sub 上是一样的，A/B/C/D 都不碰它。但我们想记录：**主线的 async 精力投在能力缺口（哪里不能 Await）上，而不是投在方法声明的拼写上**。
- **`Call` 语句**：Anthony 在同章写 `Call FireAndForgetAsync()` 作为"显式不等待"的语法。这暗示作者心口有一个 require-await 模型（"Async calls must be awaited or task objects assigned"）。C 与这个模型打架：如果裸 `Async Sub` 已是 Task，那么 `Call Flush()` 到底算"显式不等待"还是算"漏了 Await"？两套规则抢同一语法。
- **`Iterator`**：`Async Iterator` 是相邻建议（FetchChunksAsync 那行）。A 若只对 `Async Sub` 开口，`Async Iterator Sub` 要不要开口？规范把 `Iterator` 与 `Async` 并列处理，这里会出现菱形交互。

#### 5. Breaking change 与兼容性

- **C**：改变现存 `Async Sub` 的语义（void→Task），重编译即变：异常路径从"回投 `SynchronizationContext`"变成"Task Faulted"；事件处理器失效。**不可接受。**
- **B**：对"非任务型返回类型的 async Function"（今天报错）提供新含义——这不是运行时破坏；但对"任务型/自定义 awaitable 返回类型"的 `As X` 触发重解释——**这是破坏**。规则依赖 awaitable 分类，存量自定义 awaitable 代码全部中招。
- **A**：新语法，存量代码无此形态，零破坏。但它是"零破坏的无效特性"（见场景段：无法消费）。
- **D**：元数据改名，重编译即破（`NameOf`、反射、晚期绑定、绑定族）。

#### 6. Option Strict / 编译选项分叉

C/D 不是 Option Strict 那条轴，而是第三条轴：**"配置分叉"**。Option Strict 从不改变签名；C/D 改变签名。这意味着严格/宽松两条路径之外还要处理"配置 A/配置 B"的互操作——两个模块、同一源码、不同配置，`Task` vs `ValueTask` 互换。这个分叉我们无法维护。

#### 7. IDE / IntelliSense

C 下补全与签名帮助要显示"推断出的 Task/ValueTask"，但这是配置依赖的——同一项目内跨文件改配置，IDE 缓存就脏了。D 下重命名重构会与自动后缀打架：用户按 `F2` 把 `Flush` 改名，编译器又给追加 `Async`。`NameOf` 的显示与求值需要一个从未设计过的区分。`Probably`：这一项的成本不低于编译器成本。

#### 8. 数据 / 普遍性

主线可核实的 async 请求集中在 #37（Await in Catch/Finally）——那是"语言在阻塞场景"的实打实缺口。而 "Function As Task weirdness" 在 vblang 二十余份纪要里**没有出现过**：主线没有把 `Async Function ... As Task` 当作痛点。`Suspect`：这是 Anthony 个人审美（他自己也只在第 11 章与 18.15 表达），不是可量化的用户需求。反过来，fire-and-forget 的扩散是 Anthony 亲口承认的白色巨鲸（18.15："Void-returning async methods are my white whale. I must find more ways to limit their spread."）——但那条鲸鱼属于 require-await，不属于改拼写。

#### 9. 更简替代

- **维持 `Async Function ... As Task`**：已工作、无歧义、与 C# `async Task` 对齐。所谓样板只有一个词（`Function` vs `Sub`）。
- **分析器 / 警告**：对 fire-and-forget `Async Sub` 出警告、提示改 `Async Function ... As Task`。零语法、零破坏，覆盖"限制 void async 扩散"的真实动机。
- **约定而非编译规则**：命名后缀已经是规范文本里的约定（"By convention ... suffix 'Async'"）。约定不需要编译器执行。
- **脚本宿主侧**：若 VBScript.NET 的脚本语义真的需要"动作即 Sub"，正确做法是先回答 A 的消费模型，而不是绕过它。

#### 10. 复杂度 / 成本 / 优先级

C/D 要求把"配置默认类型/后缀"作为编译器级输入贯穿语法、绑定、semantic model、IDE、元数据——成本与"给语言加一个编译开关改签名"相当，风险（签名漂移）远大于收益。A 单独看成本不高（文法 + 绑定各一小块），但收益为零（无法消费）。B 的成本是重写 async 返回类型的绑定规则与 overload 规则，收益是省一个 `Task(Of ...)` 外壳，代价是声明谎言。**优先级上，主线 async 的下一步仍是 #37 与 `IAsyncDisposable/Async Using`**（2017-08-09："The feature is approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"）——不是本建议。

#### 11. 运行时 / CLR 硬约束

IL 层不存在"Sub/Function"之分——一个返回值的成员与不返回值的成员在 CLR 里只是返回类型不同。所以 A 的 `Sub ... As Task` 在 IL/PEVerify 层面无新约束，`Task`/`ValueTask` 的状态机也早已有完整支持。**语言层的约束是文法（`SubSignature` 无 `As`）、调用分类（Sub 调用不是值）与 Sub/Function 不变量**——三者全是语言决定，不是运行时决定。这也说明本建议没有任何运行时理由非做不可。

#### 12. 值不值得做

价值（"Function As Task"的观感别扭）：低，且是主观审美，无数据。成本（C/D 是编译器级配置贯穿，B 是绑定重写，A 是文法 + 无法消费）：中到高。风险（C 破坏存量 `Async Sub`，D 破坏绑定族，B 重解释自定义 awaitable）：高。**综合：按原文不值得做。** A 单独看是"零破坏的无效特性"——没有消费者，价值为零。真正的问题（void async 扩散）在别处，且别处已有更好、更便宜的工具。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：C、D 直接违反；B 对自定义 awaitable 违反。
- **保持 VB-like（原则 #2）**：Sub/Function 之分、"Sub 调用是语句"是 VB 的骨骼。`Sub ... As Task` 是第一次对这根骨骼动手。
- **不引入"第二种做事方式"（原则 #3）**：B 让 `Async Function ... As Task(Of JsonObject)` 与 `Async Function ... As JsonObject` 并存，同一能力两套拼写。
- **默认跟随 C#，除非有充分理由（原则 #4）**：2018.12.19 原话——"Where we need to make a decision, we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages"。C# 的可等待无值方法就是 `async Task`，即 VB 的 `Async Function ... As Task`。本建议没有给出偏离的 compelling reason。
- **读起来像英语（原则 #5）**：`Function Pull(...) As JsonObject` 读起来像"同步返回 JsonObject"——是误导，不是可读性。
- **避免隐蔽语义变化（原则 #7）**：B 是教科书级反例；C 把裸 `Async Sub` 静默改义。
- **消除常见样板（原则 #9）**：本建议不是消除样板，是给样板换脸。真样板（fire-and-forget 的扩散）归 require-await。
- **冗长只在有用时是美德（原则 #10）**：显式 `As Task` 恰恰是有用的冗长——它让签名成为事实。

**与主线关系（对照表 2.3）**：**Anthony 独立延伸**，且与主线"默认跟随 C#"方向相抵。主线的 async 路线是 #37（Await in Catch/Finally）与 `IAsyncDisposable/Async Using` 的时序，从不涉及把 `Sub` 变成可等待声明。与相邻 ModVB 建议 `require-await`（Anthony 18.15 的白色巨鲸）目标一致但手段错位：require-await 治扩散，本建议改拼写。

### RESOLUTION:

1. **拆分捆绑。** 原文把四个独立特性（显式 Sub 返回类型 / 结果类型省略 / 默认异步类型配置 / 名称后缀生成）捆成一个建议。We 不会整体采纳任何捆绑。
2. **否决 C（配置化默认异步类型）。** 裸 `Async Sub` 今天有明确语义（void、fire-and-forget、`SynchronizationContext` 回投），规范白纸黑字。改为"默认 Task"是重编译即变的破坏，且事件处理器（`Handles` 要求 void 委托）整体失效。签名不得成为配置的投影。
3. **否决 D（名称后缀生成）。** "By convention async methods are named with the suffix 'Async'"——约定不是编译规则。编译器改写元数据名会破坏 `NameOf`、反射、晚期绑定、`Overrides`/`Implements`/`Handles`。
4. **否决 B（结果类型省略）。** `Function Pull(...) As JsonObject` 声明与事实不符（实际返回 `Task(Of JsonObject)`）；对任务型/自定义 awaitable 返回类型构成重解释破坏；与 `Return?` 属同一类"细微字符改变语义"的设计，我们不会重蹈。
5. **Table A（显式 `Async Sub ... As Task`），附一个必答题。** A 是四个切片里唯一零破坏的，但它面对一条文法死路：Sub 调用是语句，不是值；`Await Flush()` 与 `Dim t As Task = Flush()` 在现行文法下都不合法，唯一合法用法是当语句丢弃值——那正是今天的 fire-and-forget。**除非有人能论证"可等待 Sub"的消费模型**（Sub 调用进表达式位置，等于把 Sub 变 Function），A 没有价值。这条必答题答不上来，A 就停在 Table。
6. **真实动机移交。** Anthony 想消灭的（void async 扩散，他的 white whale）应交给 `require-await` 工作项与警告/分析器；保留 `Async Sub`（void）作为显式 fire-and-forget，不重载它。主线的 async 优先级仍由 #37 与 `IAsyncDisposable/Async Using` 驱动。

### Implication:

- 把 `require-await` / `Call FireAndForgetAsync` 从本建议解耦，移入相邻工作项（Anthony 同章）。
- 为 B 起草一份"声明类型 ≠ 实际类型"的代价文档，作为将来任何"隐式包装返回类型"建议的拒绝模板。
- 若 VBScript.NET 脚本宿主确实存在"Sub 形状的可等待方法"需求，先回答 A 的消费模型；那是一个更大的语言变更（Sub 调用作表达式），且与 C# 对齐原则冲突，需要单独提案。
- 在沙盒路线图上把本建议的状态标为 Rejected（原文），并链接到 `require-await`。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：A 的消费模型——"可等待 Sub"到底靠什么被等待？若答案是"Sub 调用进表达式位置"，请写一份独立的、论及 Sub/Function 不变量与 `NameOf` 影响的提案。
- `OPEN QUESTIONS`：B 的 awaitable 分类规则若强行落地，自定义 awaitable 返回类型的重解释范围如何界定、如何门控。
- `OPEN QUESTIONS`：VBScript.NET 脚本场景是否真的存在"Sub 形状 + 可等待"的刚性需求——需要真实脚本的数据，而非审美。
- `TODO`：统计存量 `Async Sub` 的用途分布（事件处理器 vs 裸 fire-and-forget），为"C 的破坏面"补量化证据。
- `Follow-up`：`require-await` 落地时，与 `Call` 语句、`Async Sub`（void）的语义分工写成一张对照表。

### 状态

- **LDM 状态：Rejected（as written）**；拆分后 A = Table（等待消费模型论证），B / C / D = Rejected。
- **三态判定：Reject** —— 动机是观感而非能力缺口，核心语法在现行文法下无法消费，配置化/改名切片破坏存量语义与元数据稳定性。对 VBScript.NET 的优先级建议见附录（整体上限 Consider，A 为唯一可抢救切片，B/C/D 明确 Reject）。

---

## 附录：特性评价

# 建议评价报告：proposal-async-sub.md

## 评价对象

- 建议：proposal-async-sub.md — `Async Sub` 返回类型与默认异步类型配置
- 来源：Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1720–1763；`Async Sub ... As Task`、`As ValueTask`、裸 `Async Sub`、`Async Function Pull(...) As JsonObject`、`Call FireAndForgetAsync` 全部出自该章）；18.15 "New ConfigureAwait options/fire-and-forget exception handling"（"white whale" 自述）为本建议动机的侧证
- 配方目标：允许 `Async Sub` 显式声明 awaitable 返回类型；允许省略返回类型/结果类型由可配置默认规则补齐，消除 "Function As Task" 样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。目标（消除 "Function As Task" 观感）是审美抱怨，无 use case、无数据、无原型；核心示例 `Async Sub Flush() As Task` 在现行文法下**无法被等待**（Sub 调用是语句、被分类为 void），示例不能按意图演示改进 | 已提供 | 无原型封顶；示例的核心特性没有可消费路径 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。四个独立特性捆绑；`As JsonObject` 隐式包装是隐蔽语义变化（原则 #7 反例）；`Sub As Task` 动摇 Sub/Function 骨骼（原则 #2）；配置化签名与"签名是源码投影"（原则 #5/#10）相悖 | 已检查 | 无 VB 化改造；与"默认跟随 C#"（#4）相抵且无 compelling reason |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节齐全但：未写"可等待消费路径"（Sub 调用是语句这一文法事实通篇未提）；示例注释 "Same as `Function FlushAsync() As Task`" 暗示编译器改名，正文未展开、未论 `NameOf`/反射；状态行占位链接（`PROTOTYPE_OWNER/roslyn/...`、`pr/1`）；未决问题 3 个且具体（诚实区间内），但"显式 As Task 与配置出的 Task 是否等价"恰好暴露 C 的自我矛盾 | 已检查 | 缺 Compatibility/breaking-change 分析；缺文法/BNF；核心语法不可消费却未标注 |
| 属性 | 2/5 | 锚点 2："某关键维度明显受损且无应对"。暗风险突出：C 改变存量 `Async Sub` 语义与事件处理器（原则 #1 破坏）；D 元数据改名破坏绑定族；B 重解释自定义 awaitable；C/D 签名随配置漂移是编译器级暗伤 | 已检查（预测待定） | 风/暗维度明显受损且文档未识别；对雷（迭代速度）几无正向贡献 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 第 11 章（正文有"原文注释"字样但未标注章节号）；**未声明继承 VB 既有 `Async Sub`（fire-and-forget）语义**——而这恰是最大的交互对象与破坏面；未声明与 C# `async void`/`async Task` 的对照关系（实际是对 C# 拼写的偏离）；18.15 的真实动机（限制 void async）未与本建议目标对齐 | 已检查 | 成分影响与预期有偏差：声称消除样板，实际制造签名漂移与声明谎言 |

## 设计原则对照

- **与 VB 基因：偏离。** 破坏原则 #1（C/D/B 重编译即变）、#2（Sub/Function 骨骼）、#7（`As JsonObject` 隐蔽变义）；与 #3（两套拼写并存）、#4（偏离 C# 无 compelling reason）、#5（声明误导读者）冲突。唯一沾边的 #9（消除样板）实为换脸，真样板归 require-await。
- **与主线关系：Anthony 独立延伸，方向与主线相抵。** 主线 async 路线为 #37（Await in Catch/Finally，2017-08-09 原则批准、2018-02-28 "Let's do it!"）与 `IAsyncDisposable/Async Using`，从不涉及 Sub 声明形态；"By convention ... suffix 'Async'"是主线对命名的既有立场（约定，非编译规则）。与相邻 ModVB 建议 `require-await` 目标一致（治 void async 扩散）但手段错位。
- **破坏性变更：有。** C 改变存量 `Async Sub` 语义（含事件处理器）；B 对任务型/自定义 awaitable 返回类型重解释；D 元数据改名。仅 A 零破坏，但零价值。

## 总评

- **达成程度：未达成。** 动机（审美）不足以支撑成本（编译器级配置贯穿）与风险（存量语义破坏 + 签名漂移）；核心语法在现行文法下无法消费，无人回答过"怎么等待一个 Sub"。
- **LDM 三态建议：Reject（原文捆绑）；拆分后 A = Table（等待消费模型论证）、B / C / D = Reject。** 对 VBScript.NET 的优先级上限为 Consider，且仅限 A（显式 `Async Sub ... As Task`），前提是脚本宿主给出真实需求数据并先行解决消费模型；B/C/D 在脚本场景同样成立为 Reject。
- **主要问题**：① 文法不自洽——Sub 调用是语句、不是值，`Async Sub ... As Task` 无法被 Await 或赋值；② C 破坏存量 `Async Sub` 与事件处理器；③ B 声明与实际类型不符，且重解释自定义 awaitable；④ C/D 使元数据签名成为配置的投影，贯穿 semantic model/IDE/分析器；⑤ 动机是观感而非能力缺口，主线的 async 精力在 #37 一类的能力缺口上。

## 返工建议

- **补充章节**：① 消费模型（"可等待 Sub"靠什么被等待——Sub 调用进表达式位置？那需独立提案并论 Sub/Function 不变量与 `NameOf`）；② Compatibility / breaking-change（存量 `Async Sub` 的 `SynchronizationContext` 语义、事件处理器 `Handles`、`Overrides`/`Implements`/接口与抽象、`NameOf`/反射/晚期绑定、自定义 awaitable 重解释、配置分叉互操作）；③ B 的 awaitable 分类文法（BNF）与重解释门控。
- **补充证据**：真实代码库中 "Function As Task" 被抱怨/误用的量化数据；存量 `Async Sub` 用途分布（事件处理器 vs 裸 fire-and-forget）；VBScript.NET 脚本宿主对"Sub 形状可等待方法"的刚性需求。
- **未决问题处理**：显式 `As Task` 与配置 Task 的等价性问题，在否决 C 后自动消解；A 的消费模型未答复前不推进。
- **设计探索**：把"限制 void async 扩散"移交 `require-await`（Anthony 18.15 白色巨鲸的本体），与 `Call` 语句、`Async Sub`（void）的语义分工写成对照表；若脚本场景确需 Sub 形状，探索"脚本宿主翻译层"而非语言文法变更。

---

## 附录：C# 生态与互操作考量

> 主题：Async Sub 的返回类型与"默认异步类型配置"——即 VB 侧 `async void` 的对应物。C# 侧对应走向：async void 的边界、async Task / task-like types、C# 7.1 async Main、C# 9 top-level await、C# 10 AsyncMethodBuilder 覆盖、async 状态机元数据。来源：`..\..\csharplang-index.md`（索引）与 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像，main 分支）。C# 原文均逐字核对。

### 相关 C# 现实方向

1. **async void = fire-and-forget；C# 以"约定 + 分析器"而非文法限制它。** C# 语言本身在文法上允许 `async void` 出现在任何地方（不是编译错误），但 LDT 明确把它定性为 fire-and-forget，并在能避开处避开。LDM 讨论 async Main 时的原话（逐字）：
   > "The temptation is to allow the `async` keyword on existing entry points returning `void` (or `int`?). However, that makes it *look* like an `async void` method, which is fire-and-forget, whereas we actually want program execution to wait for the main method to finish."
   → `meetings\2017\LDM-2017-02-28.md`
   同一提案的 Alternatives 明确表达对鼓励 async void 的顾虑（逐字）：
   > "There are also concerns around encouraging usage of `async void`."
   → `proposals\csharp-7.1\async-main.md`
   "async void 只用于事件处理器"在仓库内**没有文法级表述**——它是社区/运行时文档与分析器（MS docs "Async/Await Best Practices"、Roslyn/VSTHRD 系分析器）的约定，正文不在本镜像。这一点与 VB 规范对 `Async Sub` 的待遇同构：规范只描述异常路径（回投同步上下文），不限制用途。

2. **可等待、无返回值的方法 = `async Task`（或 task-like 对应）。** C# 的规范形态是"返回 Task 的方法"，声明与元数据一致。task-like types（C# 7.0）把该模式形式化（逐字）：
   > "Extend `async` to support _task types_ that match a specific pattern, in addition to the well known types `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>`."
   → `proposals\csharp-7.0\task-types.md`
   一个 task-like 类型带 `[AsyncMethodBuilder(...)]` 关联的 builder 类型 + `GetAwaiter()`（同提案 "Task Type" 节）——这是跨语言 async 的元数据底座。

3. **C# 7.1 async Main：宁可合成包装入口，也不放开 async void。** 官方总结（逐字）：
   > "Allow `await` to be used in an application's Main / entrypoint method by allowing the entrypoint to return `Task` / `Task<int>` and be marked `async`."
   → `proposals\csharp-7.1\async-main.md`
   编译器合成实际入口 `Main(...).GetAwaiter().GetResult()`（Detailed design；LDM 结论 "Allow `Task` and `Task<int>` returning `Main` methods as entry points, invoking as `Main(...).GetAwaiter().GetResult()`" → `meetings\2017\LDM-2017-02-28.md`）。C# 9 top-level statements 延续该决定：顶层 `await` 使合成入口为 `static async Task $Main(string[] args)`，无 await 则为 `static void $Main(...)`（表见 `proposals\csharp-9.0\top-level-statements.md` 的 "Async-operations" × "Return-with-expression" 表）。

4. **C# 10 AsyncMethodBuilder 覆盖：builder 选择是"逐方法显式 opt-in"，不是配置默认。** 官方动机明确否定进程级/全局开关（逐字）：
   > "With the switch at the process level and affecting all `async ValueTask` methods in the process, whether you control them or not, it's too big a hammer."
   → `proposals\csharp-10.0\async-method-builders.md`
   替代做法是给方法贴 `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`——选择留在源码、留在签名旁边，可被二进制引用与 semantic model 看到。与本提案 C 切片的"配置默认"正相反。

5. **async 状态机元数据是跨语言的共享契约。** C# 把 async 方法编为：方法带 `[AsyncStateMachine(typeof(<ExampleAsync>d__29))]` + `[CompilerGenerated]` 状态机 struct + `<>t__builder` 字段 + builder 模式（`Create()` / `Start(ref stateMachine)` / `SetResult` / `SetException` / `Task` 属性），生成代码逐字见 `proposals\csharp-10.0\async-method-builders.md` Execution 段。void 的 builder 是 `AsyncVoidMethodBuilder`，与 `AsyncTaskMethodBuilder`、`AsyncValueTaskMethodBuilder` 一起被列为"known-safe" builder 白名单（`proposals\async-method-ref-parameters.md`，排队中的 C# 提案）。同提案对状态机语义有一句逐字说明，正好呼应本会议对 SynchronizationContext 的关切：
   > "The async state machine builder `Start` methods wrap the initial `MoveNext` call so that exceptions are marshaled to the returned task object and so that the ExecutionContext and SynchronizationContext are captured and restored."
   → `proposals\async-method-ref-parameters.md`
   VB 的 async 方法产出同一形态，这正是 VB/C# 跨语言 `Await` 互通的底座。运行时（CLR/PEVerify）层面没有 Sub/Function 之分——与本会议"运行时 / CLR 硬约束"节结论一致。

6. **C# 的 async 精力投在"哪里不能 Await"，不在方法声明拼写。** C# 13 放宽 async/iterator 内无 await 段的 ref/unsafe（`proposals\csharp-13.0\ref-unsafe-in-iterators-async.md`），排队中的 `proposals\async-method-ref-parameters.md` 再把放宽延伸到参数；`await` in `catch`/`finally` 自 C# 6.0 已支持（本镜像不含 C# 6 spec 正文，见 OPEN QUESTIONS）。对照：VB 主线的 async 优先项正是 vblang #37（Await in Catch/Finally）与 `IAsyncDisposable/Async Using`——C# 早已跨过这条能力线。索引 T8 的 unsafe-evolution 对 VB 的表态（"We do not need to add support to Visual Basic for *requires-unsafe* members..." → `proposals\unsafe-evolution.md`）进一步印证：C# 推进低层能力时会把 VB 排除在门槛外，VB 需要的是识别新元数据而非跟进拼写。

### 现实 vs 提案

- **A（`Async Sub ... As Task`）：IL 同构，拼写相抵——需桥接，而非兼容。** 按第 5 点，A 产出的元数据与 `Async Function ... As Task`（= C# `async Task`）完全同构，运行时零新约束。但 C# 世界不存在"`async void` 且返回 Task"——void 就是 void，可等待无返回值的方法始终是 `Task` 返回的 Function（第 2、3 点）。A 若落地，唯一差别是源码拼写——纯 VB 特色拼写，C# 无对应物，且消费模型（Sub 调用不是值）在 C# 侧同样无解。按"默认跟随 C#"原则 #4，需 compelling reason 才偏离；A 目前只有审美理由。**判定：需桥接（源码层 VB 特色、元数据层与 C# 同构），但消费死路使桥接失效——与 RESOLUTION 第 5 条一致。**
- **B（结果类型省略）：与 C#"声明即事实"冲突。** C# 的返回类型就是声明的类型（第 2、4 点）；`As JsonObject` 隐含 `Task(Of JsonObject)` 在 C# 无任何对应概念，且会被任何 C# 读者读成"同步返回 JsonObject"。**判定：冲突。**
- **C（配置化默认异步类型）：与 C# 10 的显式 opt-in 直接冲突。** C# 逐字否决进程级/全局开关（"too big a hammer"，第 4 点），改用源码旁的逐方法属性；C 把"默认 Task/ValueTask"做成项目配置，正是 C# 否决的那类"同一源码、不同环境、不同产物"分叉。**判定：冲突——C# 的替代做法证明"源码旁显式选择"才是生态接受的形态。**
- **D（名称后缀生成）：与 C# 对命名的立场冲突。** C# 保留 async 后缀为约定（逐字）：
  > "While the async suffix is recommended for Task-returning methods, that's primarily about library functionality"
  → `proposals\csharp-7.1\async-main.md`
  C# 从未让编译器改写用户方法名（top-level statements 的 `$Main` 是编译器生成的入口名，且明确"the method cannot be referenced by name from source code"，非用户源码名改写）。**判定：冲突。**
- **E（什么都不做）+ require-await：与 C# 主路径兼容。** C# 的现状就是"async Task 返回的方法 + 用分析器/约定约束 async void 扩散"（第 1、2、3 点）——E + require-await 正是 C# 的答案。**判定：兼容。**
- **void async 的边界：C# 靠约定，VB 靠规范——方向一致、手段错位。** C# 与 VB 对 void async 的异常语义同构（builder `Start` 包装异常与 SyncContext，第 5 点），但 C# 用分析器把 async void 压缩到事件处理器；VB 未压缩（本提案即尝试用改拼写压缩）。**判定：方向一致（都想限制 void async），手段错位——C# 用分析器，提案用签名。** 与 RESOLUTION 第 6 条一致。
- **动机"Function As Task weirdness"：与 C# 现实脱节。** C# 仓库内从未把"async 方法需返回 Task"当作痛点（async-main 反而把返回 Task 作为解法推广，第 3 点）——与本会议数据点 8（主线二十余份纪要无此抱怨）相互印证。**判定：脱节。**

### 对 VBScript.NET 的适应建议

1. **元数据形态优先对齐。** .vbx 的 async 方法必须产出与 C# 相同的状态机元数据（`[AsyncStateMachine]` + builder 模式，第 5 点）才能跨语言 `Await`。ModVB 编译器需识别：C# 7.0 task-like types 的类型级 `[AsyncMethodBuilder]`、C# 10 的方法级 `[AsyncMethodBuilder]` 覆盖——否则无法消费 C# 库里的自定义 builder（如 pooling ValueTask builder），也无法为 .vbx 自定义 awaitable 产出正确 builder。`AsyncVoidMethodBuilder` 与"known-safe builder 白名单"（第 5 点）是必须识别的另一类元数据（白名单外的 custom builder 与 ref/ref-like 参数互斥，VB 侧虽无 ref 参数，但识别白名单是理解 C# 二进制安全边界的前提）。
2. **"Sub 形状的可等待"走宿主翻译层，不改语言文法。** 若 VBScript.NET 脚本宿主确需"动作即 Sub"，正确做法是宿主把 `Sub Flush()` 翻译为 Task 返回的方法（元数据仍是 Function 形态），而不是在 VB 文法里给 `SubSignature` 开口（RESOLUTION 第 5 条、Implication 第 3 条）。这与 C# 生态的 source-gen 桥方向（索引 T6：编译期生成替代运行时反射）一致——翻译层就是 .vbx 的 source-gen 桥：源码呈现 VB 特色拼写，元数据呈现 C# 可互操作的形状。
3. **默认安全、按需动态：void async 的默认面收窄。** C# 用分析器把 async void 限定到事件处理器（第 1 点）；.vbx 可采纳同类默认——裸 `Async Sub` 仅用于事件处理器或显式 fire-and-forget，其余要求 `Async Function ... As Task`——把"宽松"做成显式 opt-in。这与提案的 require-await 移交同向，也与决策文件 M2/M5 的"默认安全、按需动态"总路线一致。
4. **配置不得漂移签名。** C# 10 把 builder 选择留在源码（逐方法属性，第 4 点）。.vbx 若引入任何 builder/返回类型偏好，应同样落在源码/属性层面而非项目配置——否则与 C# 二进制互操作时签名对不上（RESOLUTION 第 2 条；决策文件 M5 对"解释执行 vs 编译产物"的双模路线同样要求源码投影稳定）。

### 对既有 RESOLUTION / 三态判定的影响

- **无影响，且被 C# 生态证据方向相反地强化。** 逐条对应：C# 从未扩展 async void（支持否决 C 的"裸 Async Sub 改义"）；C# 10 选逐方法显式 opt-in 而非配置默认（支持否决 C 的配置化）；C# 保留后缀为约定（支持否决 D）；C# 声明类型即实际类型（支持否决 B）；C# 用分析器约束 void async（支持"真实动机移交 require-await"）。C# 侧没有一条证据支持"给 Sub 开口"。
- **三态判定维持：Reject（原文捆绑）；A = Table。** 需在 A 的档案补一句：A 的 IL 形态与 C# `async Task` 完全同构（第 5 点），唯一差别是源码拼写与"Sub 调用不是值"的消费死路——这使 A 在 C# 对齐维度上无任何互操作收益，纯属 VB 侧审美。
- **新增一条跨语言提示：** .vbx 在"事件处理器 void async"与"Task 返回方法"之间需要一条清晰边界。这条边界在 C# 靠分析器、在 .vbx 靠 require-await/警告——两边的边界语义（异常回投同步上下文 vs Task 进入 Faulted，第 5 点的 builder `Start` 语义）必须逐字对齐，否则跨语言调试时同一段 void async 的异常行为不一致。

### OPEN QUESTIONS / Suspect

- `OPEN QUESTIONS`：C# 对 `async void` 异常传播的规范正文（"propagated to the current synchronization context" 一类表述）在 dotnet/csharpstandard，不在本镜像（`spec\` 目录仅为链接索引）；本附录未引用其逐字，建议核实后补入。
- `OPEN QUESTIONS`：C# 6.0 `await` in `catch`/`finally` 的官方提案/规范正文不在本镜像（proposals 目录自 C# 7.0 起才完整）；该事实作为既定 C# 能力陈述，未经逐字核对。
- `OPEN QUESTIONS`：C# 社区把 async void 限定为事件处理器的权威表述（MS docs / Roslyn 分析器）不在 csharplang 仓库；本文按"约定而非文法"描述，未逐字引用。
- `Suspect`：第 4 点把 async-method-builders 的 "too big a hammer" 用作 C 切片的对照——该句语境是 ValueTask pooling 的进程级开关，与 C 的"项目配置默认类型"是**类比**而非同物；两者共享的批判是"全局开关影响面过大、不可按方法选择"。引用时保留此差异。
- 索引第四节 6 段已核实原文中与 async 直接相关的无；本附录引用的 C# 原文均出自本附录自核的 `async-main.md`、`async-method-builders.md`、`task-types.md`、`top-level-statements.md`、`LDM-2017-02-28.md`、`async-method-ref-parameters.md`。
