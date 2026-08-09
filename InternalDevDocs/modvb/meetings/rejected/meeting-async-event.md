# Visual Basic Language Design Meeting

August 8, 2026

ModVB 沙盒系列会议。本周我们回到第 11 章 Async Programming Enhancements 的末位条目——上周我们刚审过同章的 `Async Sub` 返回类型与默认异步类型配置（见 `meeting-async-sub.md`），判定整体 Reject、显式 `Async Sub ... As Task` 停在 Table。今天这条比上周那条还薄：整份特性在原文里只有一行，注释还是问号（`' `Async` events?`）。但话题压在我们最不能轻动的一块地基上——事件模型。我们花了大量时间核对规范里 `Event` 与 `RaiseEvent` 到底承诺了什么，结论是：**"异步事件"这个概念在今天的事件模型里找不到一个可以落脚的声明位置**。这句话值得展开说清楚。

## Agenda

* [Proposal: Async Event（异步事件）](#proposal-async-event)

## Proposal: Async Event

_Related: Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1776–1777，全特性仅一行）；[vblang spec – type-members.md "Events"](https://github.com/dotnet/vblang/blob/master/spec/type-members.md)；[vblang spec – statements.md "RaiseEvent Statement"](https://github.com/dotnet/vblang/blob/master/spec/statements.md)；[vblang #303 – Null conditional operators for add/removeHandler](https://github.com/dotnet/vblang/issues/303)；[vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #167 – Support for Return? construct](https://github.com/dotnet/vblang/issues/167)；主线会议：2014.02.17、2014.04.01、2017.05.19、2017.08.09、2018.02.21、2018.02.28、2018.05.30、2018.12.19_

### 场景与缺口

We started from a scenario that is real and that the proposal's Motivation names only in passing. 事件处理器经常需要异步工作，今天的第一公民写法是 `Async Sub`：

```vb
' 今天：事件处理器做异步工作，写成 Async Sub（void、fire-and-forget）。
' 事件本身的委托类型仍是普通 Sub(sender, e)，没有任何"事件是异步的"一说。
Private Async Sub LoadButton_Click(sender As Object, e As EventArgs) Handles LoadButton.Click
    Await LoadAsync()
End Sub
```

这条路径在"没人需要观察完成"时完全够用。缺口在于**raises 这一侧**：声明方想把事件当作"通知点"，且要在所有异步处理器完成之后才继续。`RaiseEvent` 今天同步调用多播链、立即返回——处理器是 `Async Sub` 时，谁也不会等它：

```vb
Class Document
    Event Saved As EventHandler

    Public Async Function SaveAsync() As Task
        Await FlushToDiskAsync()
        RaiseEvent Saved(Me, EventArgs.Empty)
        ' 若 Saved 的处理器是 Async Sub（void），SaveAsync 在这里立即返回，
        ' 处理器可能还在执行。调用方无法可靠地认为"保存已通知完毕"。
    End Function
End Class
```

UI 场景里这个缺口有真身。WinForms 的 `FormClosing`：处理器在第一次 `Await` 处就把控制权还给了窗体，窗体继续关闭流程；等到续体恢复再设置 `e.Cancel` 已经太迟：

```vb
Private Async Sub Form_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
    Await FlushDirtyStateAsync()      ' 第一次 Await 处返回，关闭流程继续
    If HasUnsavedChanges Then
        e.Cancel = True               ' 太迟了——此刻无人再读 e.Cancel
    End If
End Sub
```

We 承认缺口真实。但我们同样要诚实指出提议的形态：原文把整份特性压缩成一行，注释还是疑问句（L1777，逐字）：

```vb
' `Async` events?
Async Event E(sender As Object, e As EventArgs)
```

一份特性一句话、问号收尾，等于把"这是什么、谁等待、等待多久、几个处理器、异常怎么办"全部留给会议。我们把大部分时间花在第一个问题上：**一个事件怎么可能是"异步"的？**

### 候选方案

We 把原文的单一语法与三条候选重新拆成五个相互独立的切片：

**PROPOSAL A — `Async Event` 声明语法（原文唯一形态）。** `Async Event E(sender As Object, e As EventArgs)`。让事件声明携带 `Async` 修饰符。未定义 `RaiseEvent` 的等待/异常语义、未定义委托形状、未定义与 `Async Sub` 处理器的关系。

**PROPOSAL B — 引入 `AsyncEventHandler` 委托类型，不新增语法。** `Public Delegate Function AsyncEventHandler(sender As Object, e As EventArgs) As Task`，再 `Event E As AsyncEventHandler`。把"处理器返回 Task"显式化。

**PROPOSAL C — 用 `Func(Of Object, EventArgs, Task)` 类型的普通属性替代事件。** 原文候选。完全退出事件体系（无 `Handles` / `AddHandler` / `RaiseEvent` / 多播 / `WithEvents`），把"异步通知"降格成可调用属性。

**PROPOSAL D — 可等待的 Raise（awaitable raise）。** 我们重新框定的真问题：不是"事件是不是异步"，而是"**raises 能不能等待所有处理器完成**"。语法形态未知（`Await RaiseEvent E(...)`？`RaiseEventAsync E(...)`？`RaiseEvent E(...)` 自动 await？），多播并发与异常聚合语义待定。

**PROPOSAL E — 什么都不做。** 维持 `Async Sub` 处理器；把"等待所有处理器完成"做成库方法（在声明类内建一个异步通知列表）。

### 权衡：Q&A

- **Q：一个事件怎么"异步"？——事件是委托槽，不是执行体。** 事件声明描述的是"谁能挂上来、以什么签名"。异步性只可能挂在**调用**（`RaiseEvent`）上，不可能挂在**声明**上。`Async Sub` 处理器已经让执行异步；声明层加 `Async` 修饰符唯一能承诺的是"`RaiseEvent` 会产生可观察的完成信号"——可 `RaiseEvent` 是语句，语句丢弃值。这个矛盾从第一行就存在，提案没有触碰。

- **Q：委托形状的硬墙——规范禁止事件委托有返回类型。** 这是本次会议最确定、也最要命的一条。`type-members.md` 白纸黑字：

  > "If a delegate type is specified, the delegate type may not have a return type."

  事件要么带参数列表（编译器合成 `XEventHandler` 嵌套 `Sub` 委托），要么 `As` 一个委托类型——两条路都要求**无返回类型**。B 的 `AsyncEventHandler` 返回 `Task`，正面撞墙。所以"异步事件"不是加一个修饰符，而是要**放宽一条规范根基规则**，连带影响接口事件、`MustOverride`/`Overridable` 事件、`Custom Event`、WinRT 事件访问器（规范对 winmd 只允许特定委托形态，`type-members.md`："External tools used to build the winmd will typically allow only certain delegate types such as `System.EventHandler(Of T)`... and will disallow others."）。

- **Q：`RaiseEvent` 的消费模型。** 规范 `statements.md` 说得很硬：

  > "The `RaiseEvent` statement is processed as a call to the `Invoke` method of the event's delegate... If the delegate's value is `Nothing`, no exception is thrown."

  `RaiseEvent` 是语句（`RaiseEventStatement` 文法），同步调用多播链、空链静默——这条"空链静默"是 2014.04.01 会议明确夸过的优点（C# 要写 `OnFired?.Invoke(...)`，注释 "not needed in VB, since this is already built into RaiseEvent statement"）。若委托返回 `Task`，`RaiseEvent` 只有三条路：
  - **fire-and-forget**：与今天 `Async Sub` 无差别，特性白做。
  - **同步阻塞等完**：处理器续体若 post 回当前同步上下文，就是教科书级死锁。
  - **返回 Task 供调用方 await**：`RaiseEvent` 必须从"语句"变成"可 await 的表达式"（`Await RaiseEvent E(...)`）。这与我们上周在 Async Sub 会议上对 "Sub 调用是语句、不是值" 的判定是**同一类文法事实**：语句分类一变，语义模型、分析器、IDE 全跟着变。

- **Q：多播语义一个都没定义。** 事件是广播。多个处理器时：串行 await 还是 `Task.WhenAll`？处理器抛异常时，后续处理器还跑不跑、异常是首错还是 `AggregateException`？处理器返回 `Nothing`（空 Task）怎么办？处理器永不完成（阻塞）怎么办？raise 期间 `RemoveHandler`（`GetInvocationList` 是快照，迭代中移除安全）？处理器内再次 raise 同一事件（重入）？**每个选择都牵动同步上下文与重入风险**，提案一个字没提。

- **Q：与最普遍现实模式的冲突——`Async Sub` 处理器挂不上去。** `type-members.md` 的处理器合法判据：

  > "A handler method `M` is considered a valid event handler for an event `E` if the statement `AddHandler E, AddressOf M` would also be valid."

  若 `Async Event` 的委托返回 `Task`，一个 void 的 `Async Sub` 处理器与它**签名不兼容**。而我们上周刚确认过：`Async Sub` 的最大存量用途恰恰是事件处理器。B 一旦落地，所有现存处理器要么改写成 `Async Function ... As Task`，要么被挡在门外——这是把最普遍的模式变成二等公民。

- **Q：主线信号——事件便利性已被判过 side case。** 2018.05.30 对 #303（null 条件 AddHandler）的裁决：考虑过 WinForms/WPF 场景，"However, even in that context this would be quite rare (generally humans don't do the AddHandlers thing and generally all controls are instantiated.)"，最后 "Really seems like a side case."，标 `LDM Reviewed: No Plans`。主线的事件工作聚焦在 #24 override events（2014.02.17 "Approved in principle, but still needs design work. This is a parity issue for BCL and other frameworks"）与 WinRT 事件行为（2017.05.19 "Follow-up: Verify event behavior makes sense with regard to WinRT events."）。**异步事件在主线纪要里没有任何痕迹——我们检索了 `..\..\vblang\meetings` 全部文件。** `Suspect`：这是 Anthony 的独立探索，不是可量化的用户需求。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Async` 修饰符今天只用于方法（`Async Function` / `Async Sub`）。`EventModifiers` 文法（`type-members.md`）只有 `AccessModifier` / `Shadows` / `Shared`。加 `Async` 是纯新文法，本身无歧义——**真正的歧义在语义**：`Async Event E(sender, e)` 的委托形状是什么？若是无返回 `Sub(sender, e)`，`Async` 没有可消费的信号（处理器已经是 `Async Sub` 了）；若是 `Function(sender, e) As Task`，与"事件委托不得有返回类型"直接冲突。还有修饰符组合：`Async` × `Custom Event`、`Async` × `Shared`、`Async` × `Overridable`/`MustOverride`、`Async` × `Implements` 各自意味着什么？一行语法引出的文法问题超过六处。

#### 2. 角案例与边界语义

- **空链（无处理器）**：`RaiseEvent` 的空链静默是规范保证（statements.md）。异步版空链返回什么？已完成 Task？语义模型的 `GetTypeInfo` 是什么？
- **处理器抛异常**：链继续还是中断？异常如何呈现给 raiser——首错、`AggregateException`、还是照 `Async Sub` 的老路丢给环境？
- **处理器返回 `Nothing`**：空 Task。`Task.WhenAll` 遇 null 抛 `ArgumentNullException`，库方案要防御，语言方案要不要？
- **永不完成 / 阻塞**：可等待的 raise 把调用方永远吊住。
- **重入**：处理器内再 raise 同一事件；raise 期间 `RemoveHandler`。
- **`Shared` / 接口 / `MustOverride` / `Overridable` 事件**：2014 #24 的存量缺口（"VB users are shut out from any library which uses (3) or (5)"）是主线真正的痛点；异步事件在这个残缺的继承模型上再叠一层。
- **WinRT 事件**：访问器签名是 `EventRegistrationToken`（规范 WinRT 一节），与 Task 委托不兼容；2017.05.19 的 follow-up 仍未关闭。

#### 3. 作用域与绑定

语义模型里 `Async Event E` 的 `E` 符号返回什么委托类型？`RaiseEvent` 绑定到哪个 `Invoke`？若 `RaiseEvent` 变得可 await，它在语义模型里被分类为**表达式**还是**语句**？——这条与 Async Sub 会议上 "Sub 调用被分类为 void、不是值" 的判定必须保持自洽，否则同一份语义模型里"调用分类"出现两个标准。

#### 4. 与既有特性的交互

- **`Handles` / `AddHandler` / `AddressOf`**：void 处理器挂不上 Task 委托（见 Q&A）。
- **`Async Sub` 处理器**：最大存量用途，与 B 冲突。
- **`Custom Event`**：`RaiseEvent` 块本身是 Sub 块（`RaiseEventDeclaration` 文法），要返回 Task 吗？`AddHandler` / `RemoveHandler` 访问器签名是否全变？
- **`WithEvents` / 对象初始化器 / `Call`**：与事件挂钩的语法面都要过一遍。
- **2014.02.17 #31 `+=` 与 `AddHandler` 的关系**：主线立场是 "Semantics will be exactly as for C#. However we will not also allow 'event += value' as a synonym for 'AddHandler event, value'."——事件语法的扩展一向要过这条守门。
- **`GoTo` / `OnError`**：2018.02.21 谈 #37 时说 "GoTo and OnError seem unlikely in "modern" code that would use Async."；异步事件若引入可等待的 raise，与 `OnError` 的交互没有任何既有定义。

#### 5. Breaking change 与兼容性

A 是新语法，存量代码无此形态，**零破坏**。但危险在别处：若我们采纳"`RaiseEvent` 自动 await"（R3 的隐式形态），新代码作者以为 fire-and-forget、实际被等待，就是隐蔽的时序变化——与 2018.05.30 判 #167 `Return?` 完全同类（"We think this is a bad idea. Control flow would be altered by a very subtle character."）。零破坏的新语法也可能是一个安静的 footgun。

#### 6. Option Strict / 编译选项分叉

事件委托天然类型化（参数列表或 `As` 委托类型），严格/宽松两条路径下 `AddHandler`/`Handles` 的兼容判定都是确定性的，**不构成主要分叉**。唯一注意：宽松模式下的晚期绑定与 `RaiseEvent`（`RaiseEvent` 绑定 `Me` 上的事件，编译期确定）无关。此轴低影响。

#### 7. IDE / IntelliSense

补全要显示"返回 Task 的处理器签名"；`Handles` 的代码生成要从 `Async Sub` 改成 `Async Function ... As Task`；错误文案要解释"void 处理器不能挂到异步事件"；还需要一个重构（把现有 `Async Sub` 处理器转成 Task 返回）。B/D 任何一版都要为"两种事件世界"维护两套补全与两套错误文案。`Probably`：IDE 成本不低于编译器成本。

#### 8. 数据 / 普遍性

主线 2018.05.30 欧洲之行直言："We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now." 与 "It remains striking how few Visual Basic programmers we're able to hear from." 异步事件没有任何主线请求；与它相邻的 #303（事件便利性）已被判 side case。`FormClosing` 类"raiser 必须等 handler"的场景真实但窄，且库方案可解。`Suspect`：普遍性证据为零，只有个体审美。

#### 9. 更简替代

- **`Async Sub` 处理器**：覆盖"处理器做异步工作"的绝大多数场景，今天就能写。
- **库方法（D 的零语言成本对照）**：不用事件，在声明类内建一个异步通知列表 + `Task.WhenAll`，今天就能写、能编译、能测：

  ```vb
  Class Document
      Private ReadOnly _asyncHandlers As New List(Of Func(Of Object, EventArgs, Task))

      Public Sub RegisterAsyncHandler(handler As Func(Of Object, EventArgs, Task))
          _asyncHandlers.Add(handler)
      End Sub

      Public Async Function NotifyAllAsync() As Task
          Dim pending = _asyncHandlers.
              Select(Function(h) h(Me, EventArgs.Empty)).
              ToArray()
          Await Task.WhenAll(pending)
      End Function
  End Class
  ```

  注意一个反直觉的结论：这个"库方案"**没有用事件**——因为规范禁止事件委托有返回类型，Task 返回的处理器根本挂不进 `Event`。它绕开了语言，代价是失去 `Handles`/`AddHandler`/`RaiseEvent`/多播。这恰恰说明：**"等待异步处理器"与"VB 事件模型"在今天是互斥的**，语言特性要做的是把两者重新接上，而接上的成本是放宽那条规范规则。`Suspect`：`Custom Event` 场景下库方案无法借用 `GetInvocationList`（访问器在用户手上），需另行设计。
- **C# 对照**：C# 没有 `async event`；C# 的事件处理器就是 `async void`，即 VB 的 `Async Sub`。默认跟随 C#（2018.12.19："Where we need to make a decision, we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages"）⇒ 维持现状，没有偏离的 compelling reason。
- **反应式 / 消息总线库**：`IAsyncEvent` 这类模式把"通知+异步"放到库层。超出语言范围，但说明需求侧有成熟替代。

#### 10. 复杂度 / 成本 / 优先级

完整特性 = 文法（`EventModifiers` 加 `Async`）+ 放宽"事件委托不得有返回类型" + `RaiseEvent` 语句分类变更 + 多播并发/异常策略 + 同步上下文与重入分析 + IDE。这是接近一个语言特性满配的成本，收益却窄到库方案能覆盖大半。**优先级排在 #37 之后是硬性的**：若要 raiser 捕获 handler 抛出的异常，raise 处需要 `Await` 在 `Catch` 里——而 #37（Await in Catch/Finally）今天仍未落地（2017.08.09 "approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"；2018.02.28 "Let's do it!"）。特性还依赖主线尚未交付的能力。

#### 11. 运行时 / CLR 硬约束

无运行时障碍。委托返回 `Task` 是普通 IL；`GetInvocationList` / `Task.WhenAll` 全部存在；PEVerify 无碍。**约束全在语言层**：事件委托无返回类型（规范规则）、`RaiseEvent` 是语句（文法）、`Async Sub` 处理器是 void（`Async Sub` 规范：异常 "propagated to the environment in some implementation-specific manner"）。这也说明本特性没有任何运行时理由非做不可——它纯粹是语法与语义的选择题，而选项还没出全。

#### 12. 值不值得做

价值：窄（"raiser 必须等 handler"），且大半可被库方案覆盖。成本：高（近满配）。风险：语义未定义 + 死锁 footgun + 第二个事件世界 + 依赖 #37。**综合：按原文不值得做——不是"好特性但太难"，是"它要做什么都还没定义"。** 与上周 Async Sub 会议同一句话：热情不抵消可行性，但这里连热情的对象都还没成形。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：A 新语法零破坏；但"`RaiseEvent` 自动 await"的诱惑是隐蔽时序变化，触碰 #7 红线（`Return?` 的前车之鉴）。
- **保持 VB-like（原则 #2）**：事件 = 多播委托 + `RaiseEvent` 语句 + `Handles`，是 VB 的骨架。`Async Event E(sender, e)` 读起来像"事件是异步的"——事件不执行，处理器才执行。措辞误导，不像 VB。
- **不引入"第二种做事方式"（原则 #3）**：在同步事件世界之外再造一个异步事件世界：委托形状不同、raise 语义不同、处理器签名不同；而 `Async Sub` 处理器 + 普通事件已覆盖主场景。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 无 `async event`，用 `async void`（即 `Async Sub`）。没有 compelling reason，2018.12.19 原话见上。
- **读起来像英语、对新手友好（原则 #5）**：`Async Event` 把执行体的属性安在声明上，误导读者。
- **不为边缘场景加特性（原则 #6）**：窄场景、零数据；#303 同类已判 side case。
- **消除常见样板（原则 #9）**：样板在 raiser（"等 handler"），库方法可消；特性不消样板，反而给所有现存处理器加"改签名"的新样板。
- **冗长只在有用时是美德（原则 #10）**：`Async Event` 的冗长不携带信息——它不说明 raise 等待还是不等待，信息量为零。

**与主线关系（对照表 2.3）**：**Anthony 独立延伸**（探索性一行），与主线"默认跟随 C#"方向相抵。主线事件工作 = #24 override events（parity、原则批准）+ #303（No Plans、side case）+ WinRT 行为跟进（2017.05.19）；主线 async 精力 = #37 与 `IAsyncDisposable/Async Using`。与同章相邻建议必须联动：`Agile Async`（"Don't capture sync context w/o `ConfigureAwait(False)`"）与"可等待的 raise"是同一枚硬币——raise 捕获上下文则死锁，不捕获则 UI 处理器语义崩。

### RESOLUTION:

1. **否决 A（`Async Event` 声明语法，as written）。** 一行注释、问号结尾，未定义任何消费模型。事件不是执行体，"`Async`" 挂在声明上无意义；委托形状撞上"事件委托不得有返回类型"的规范规则；`RaiseEvent` 是语句、丢弃值，无可等待路径。与 `Return?`（#167，主线 "Control flow would be altered by a very subtle character"）属同一类"细微字符改变语义"的设计，我们不重蹈。
2. **否决 B（`AsyncEventHandler` 委托类型 + 事件）。** 直接违反 `type-members.md` "If a delegate type is specified, the delegate type may not have a return type"；`Async Sub` 处理器（void）无法挂上，现存最大用法集体失效。
3. **否决 C（`Func(Of Object, EventArgs, Task)` 普通属性）。** 那是**退出事件体系**，不是异步事件——失去 `Handles` / `AddHandler` / `RaiseEvent` / 多播 / `WithEvents`。若有"可等待的通知"场景，一个普通方法或库 API 即可，不需要语言。
4. **Table D（可等待的 Raise）。** 这是我们重新框定的真问题——但 v1 不启动。`RaiseEvent` 是语句，需要语句分类变更；多播并发/异常聚合策略未定义；同步上下文死锁与重入需专项设计；handler 异常捕获依赖 #37。**除非出现真实需求数据，且有人把多播语义设计出来，否则停在 Table。**
5. **认可现状 + 库方案。** `Async Sub` 处理器是"事件里做异步工作"的第一公民，今天就是；"等待所有 handler"用类内异步通知列表 + `Await Task.WhenAll`，零语言成本。`Probably`：库方案对 field-like 场景成立；`Custom Event` 需访问器配合，标为 OPEN QUESTION。
6. **与 `Agile Async` 对表。** 若 D 复活，必须规定 raise 时是否捕获同步上下文，并与同章 `Agile Async` 联动。UI 事件处理器在 UI 线程执行是事件体系的基本承诺，异步事件不能悄悄打破它。

### Implication:

- 把本建议在沙盒路线图标为 **Rejected（as written）**；D（可等待 Raise）作为 Table 项记录。
- 为"事件委托不得有返回类型"这条规范规则（`type-members.md`）写一条备注，防止将来任何"Task 返回事件"建议忽略它——它是本次否决的支点。
- 记录库方案（异步通知列表 + `Task.WhenAll`）为候选设计；若 VBScript.NET 脚本宿主出现"raiser 必须等 handler"的真实需求，先评估库方案再谈语言。
- 与 #37 状态联动：任何"raise 处等待并捕获 handler 异常"都依赖 Await in Catch。
- 一行探索性注释被审慎否决的判例，值得留在沙盒记录里——它示范了"最薄的建议也需要最厚的追问"。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：多播 async raise 的并发/异常语义若强行设计，串行 vs `WhenAll` 哪个是 VB 默认？`Probably`：`WhenAll` 更贴"广播"直觉，但异常聚合对 raiser 不可见，需要显式聚合形状。
- `OPEN QUESTIONS`：库方案的 `Custom Event` 场景（`GetInvocationList` 无法直接访问）如何支持。
- `OPEN QUESTIONS`：VBScript.NET 脚本宿主是否存在"raiser 必须等 handler 完成"的刚性需求——需要真实脚本数据，而非一行注释。
- `TODO`：在 2014 #24（override events）推进时，把"事件委托返回类型"一并 review——那才是事件模型真改动发生的地方。
- `Follow-up`：与 `Agile Async` / `require-await` 工作项共享一份"异步事件的同步上下文行为"对照表。

### 状态

- **LDM 状态：Rejected（as written）**；D（可等待 Raise）= Table；A / B / C = Rejected。
- **三态判定：Reject** —— 一行探索性语法未定义任何语义；委托返回类型违反规范规则；多播并发与死锁风险未解；库方案已覆盖主要价值。对 VBScript.NET 的优先级建议见附录（整体 Reject，D 上限 Consider，需需求数据）。

---

## 附录：特性评价

# 建议评价报告：proposal-async-event.md

## 评价对象

- 建议：proposal-async-event.md — `Async Event` 异步事件
- 来源：Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1776–1777；全特性仅一行 `' `Async` events?` / `Async Event E(sender As Object, e As EventArgs)`，探索性、问号收尾）；同章相邻建议 `Agile Async`（L1741–1743）、require-await（L1758–1762）、`Await Each`（L1764–1766）、`Async Iterator`（L1768–1774）为对照
- 配方目标：允许把事件声明为异步事件，使事件处理器可以异步执行并可被等待

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 描述的"处理器异步工作"今天 `Async Sub` 已覆盖；唯一新能力（raiser 等 handler）无示例可演示——`Async Event E(...)` 一行没有任何可消费路径 | 已提供 | 无原型封顶 2；核心语法不能按意图编译或演示；"事件异步"与"handler 异步"概念混写 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。事件"异步"措辞与 VB 事件模型（多播委托 + `RaiseEvent` 语句 + `Handles`）冲突；把执行体属性安在声明上（原则 #2/#5）；创建第二个事件世界（#3） | 已检查 | 未 VB 化；与"默认跟随 C#"（#4）相抵且无 compelling reason |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节模板齐全但 Detailed design 仅一句、以疑问句存在；示例（原文 L1777）今天不合法；3 个未决问题具体诚实（1–3 健康区间），但每个都是致命级未定义（RaiseEvent 语义/委托类型/兼容性）——核心语法未定型，效果证据封顶 | 已检查 | 缺文法/BNF；缺 Compatibility/breaking-change；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；未识别"事件委托不得有返回类型"这条规范墙 |
| 属性 | 2/5 | 锚点 2："某关键维度明显受损且无应对"。暗风险：同步上下文死锁 footgun、第二个事件世界（委托形状分裂）、与 #4（跟随 C#）相抵；依赖 #37 未落地；对雷/水/光无正向贡献 | 已检查（预测待定） | 风/暗维度受损且文档未识别；未识别 WinRT 事件访问器冲突 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 第 11 章一行；未声明继承 VB 既有事件模型（`RaiseEvent` 语句、事件委托无返回类型）——恰是最大交互对象；未声明与 C# `async void`（`Async Sub`）的对照；未声明与同章 `Agile Async`/`require-await` 的联动 | 已检查 | 成分影响与预期有偏差：声称"一等异步事件"，实际撞规范墙且无消费模型 |

## 设计原则对照

- **与 VB 基因：偏离。** 破坏/威胁原则 #2（事件措辞误导）、#3（第二个事件世界）、#4（跟随 C# 无理由）、#6（边缘场景）、#7（可等待 raise 的隐蔽时序变化）；与 #1 兼容（新语法零破坏）但触碰 #5（误导读法）、#10（冗长不携带信息）。唯一沾边的 #9（消除样板）实为给处理器加"改签名"样板。
- **与主线关系：Anthony 独立延伸**（探索性一行），与主线"默认跟随 C#"方向相抵；主线事件工作 = #24 override events（2014.02.17，parity、原则批准）+ #303（2018.05.30，No Plans、side case）+ WinRT 行为跟进（2017.05.19）；主线 async 精力 = #37（Await in Catch/Finally）与 `IAsyncDisposable/Async Using`。与相邻 ModVB 建议 `Agile Async`/`require-await` 必须联动。
- **破坏性变更：无**（新语法）；但"`RaiseEvent` 可等待"对新代码构成隐蔽时序变化风险（原则 #7），若复活需 `langversion` 门控。

## 总评

- **达成程度：未达成。** 动机缺口大部分被 `Async Sub` 覆盖；剩余缺口（raiser 等 handler）窄且库方案可解；核心语法未定义任何语义，直接撞上"事件委托不得有返回类型"的规范墙。
- **LDM 三态建议：Reject（as written）；D（可等待 Raise）= Table；A / B / C = Reject。** 对 VBScript.NET 优先级：**Reject**，D 上限 Consider——仅在脚本宿主出现"必须等待 handler 完成"的真实需求数据、且库方案（异步通知列表 + `Task.WhenAll`）被证伪后，才回访 D。
- **主要问题**：① 事件是委托槽不是执行体，"Async" 挂声明无意义；② 委托返回类型违反规范（`type-members.md` "may not have a return type"），语法+规范级变更；③ `RaiseEvent` 是语句、丢弃值，无可等待路径（与 Async Sub 会议"Sub 调用是语句"同类的文法事实）；④ 多播并发/异常聚合/死锁/重入全未定义；⑤ `Async Sub` 处理器（现存最大用法）挂不上 Task 委托；⑥ 全特性仅一行、问号收尾，未达可评价成熟度。

## 返工建议

- **补充章节**：① 明确"异步事件"的确切语义——谁等待、多播顺序（串行 vs `WhenAll`）、异常聚合、同步上下文策略；② 文法（`EventModifiers` 加 `Async` 的 BNF）与"事件委托不得有返回类型"的放宽方案；③ Compatibility（`Handles`/`Async Sub` 处理器/`Custom Event`/接口与 `MustOverride` 事件/`WithEvents`/WinRT 访问器）；④ 消费模型（`RaiseEvent` 如何被等待，含 `Await RaiseEvent E(...)` 的语句分类论证）。
- **补充证据**：真实代码库中"raiser 必须等 handler"的占比数据；存量事件处理器中 `Async Sub` 的比例；`FormClosing` 类时序缺口的事故样本；最小原型。
- **未决问题处理**：多播默认并发语义（`Probably` WhenAll + 显式聚合形状）；`Custom Event` 库方案；同步上下文策略与 `Agile Async` 联动；依赖 #37（Await in Catch/Finally）的先后关系。
- **设计探索**：库方案（异步通知列表 + `Task.WhenAll`）作为零语言成本对照的完整设计；"事件委托返回类型"规则的放宽与 2014 #24 override events 工作绑定，作为事件模型真改动发生的唯一合法入口。

---

## 附录：C# 生态与互操作考量

> 本附录以 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像）为据，补充正文对"C# 现实"一侧的生态与互操作分析。索引（`..\..\csharplang-index.md`）的 T1–T8 主题与本提案（异步事件）**均无直接映射**——它落在索引覆盖的边缘：C# 侧的相关现实是 **C# 5.0 时代的 async void 事件处理器模型**（早于镜像 proposals 目录的惯例），与 C# 13/15 的 async 演进只共享"async"这个词，不共享事件模型。以下分析基于对镜像的逐字检索核验，弱关联处如实标注。

### 相关 C# 现实方向

**R1 — C# 没有异步事件构造；`async void` 是唯一官方的"事件里做异步"路径。**
- C# 的事件模型是纯多播委托：`+=` / `-=` / `Invoke()`，语言层没有"可等待的 raise"或"Task 返回事件处理器"。事件处理器要异步，唯一官方路径是 `async void`。
- C# LDT 对 `async void` 的定性是 fire-and-forget。LDM-2017-02-28（讨论 async Main 时，逐字）：
  > "However, that makes it *look* like an `async void` method, which is fire-and-forget, whereas we actually want program execution to wait for the main method to finish."
  → `meetings\2017\LDM-2017-02-28.md`
- C# 7.1 async-main 提案明确表达了对 `async void` 的谨慎（逐字）：
  > "There are also concerns around encouraging usage of `async void`."
  → `proposals\csharp-7.1\async-main.md`
- 规范侧：C# 规范对 void-returning async function 单列一节（§14.15.3 "Evaluation of a void-returning async function"），说明它是被规范特判的存在。但镜像 `spec\classes.md` 仅是链接索引，正文在 dotnet/csharpstandard（draft-v6）。"async void 只能用于事件处理器"与"异常经 SynchronizationContext 传播"是公认行为，但**逐字措辞无法从镜像核实**，标 **Suspect**。
- **镜像全库检索 `IAsyncEventHandler` / `AsyncEventHandler` / `async event` 零命中** → C# LDT 从未讨论过异步事件。本提案的 `Async Event` 在 C# 侧没有任何讨论基础（此为可复现的检索事实）。

**R2 — C# 的 async 演进不触碰事件模型。**
- C# 13 `ref-unsafe-in-iterators-async`（逐字）："Allow `ref`/`ref struct` locals and `unsafe` blocks in iterators and async methods provided they are used in code segments without any `yield` or `await`." 这是放宽 **async 方法体内**的栈/指针使用，与事件声明、`RaiseEvent` 无关。→ `proposals\csharp-13.0\ref-unsafe-in-iterators-async.md`
- 排队中的 `async-method-ref-parameters`：async 方法参数允许 `ref`/`in`/`out`/ref-like，同样与事件模型无关。其"已知安全 async method builder"白名单包含 `System.Runtime.CompilerServices.AsyncVoidMethodBuilder`，说明 async void 状态机是 C# 元数据中常态存在、编译器/运行时都认得的东西。→ `proposals\async-method-ref-parameters.md`
- **与 D（可等待 raise）最相关的 C# 先例信号**：LDM-2020-11-11 讨论过让 `await` 更可链（"await as a dotted postfix operator"），结论 **Rejected**，理由是同步上下文才是真问题（逐字）：
  > "Chainability of `await` expressions isn't the largest issue on our minds with `async` code today: that honor goes to `ConfigureAwait`, which this does not solve."
  结论行："Rejected. We do like the space of improving `await`, but we don't think this is the way."
  → `meetings\2020\LDM-2020-11-11.md`
  这与正文对 D 的"同步上下文死锁 footgun"担忧（Q&A、RESOLUTION #6）完全同频——C# LDT 独立地得出同一优先级判断。

**R3 — 跨语言事件消费契约：C# 侧没有"可等待事件"契约可对表。**
- C# 消费事件 = 挂 void 处理器、`Invoke` 即 fire。即便某个 C# 库把返回 `Task` 的委托用作事件（C# 事件允许任意委托类型，规范 Events 一节在 dotnet/csharpstandard，镜像仅链接索引），生态里**没有消费方会等待它**——无约定、无 awaitable raise。VB 规范"事件委托不得有返回类型"（`type-members.md`）是 VB 侧规则，不是 C# 规则；但 C# 生态的现实是"Task 返回事件"不存在可互操作语义。这是"谁消费、谁等待"的契约空白，不是语法能力差异。
- .NET BCL 没有标准 `AsyncEventHandler` 委托（`System.EventHandler`/`EventHandler<T>` 是同步标准）。异步回调的成熟形态在库层（如 Blazor 的 `EventCallback`，可携带 `Task` 完成），属 dotnet/runtime / ASP.NET 范围，本镜像无正文——**OPEN QUESTIONS**：BCL/ASP.NET 侧"异步事件回调"的精确清单无法从 csharplang 核实。
- 对 VB 的定位信号：unsafe-evolution 的 VB 小节明确表态（逐字）：
  > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
  → `proposals\unsafe-evolution.md`
  C# 团队对 VB 的预期就是"VB 不必跟进 C# 的底层演进"——异步事件同理：C# 不会为 VB 造一个 async event，VB 也不必为对齐 C# 生态而造。

### 现实 vs 提案

| 提案切片 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| A `Async Event` 声明 | 无对应构造；C# 事件 = 多播委托 + async void 处理器 | **脱节** | C# 侧零讨论、零生态牵引；"事件是异步的"措辞在 C# 事件模型里同样没有对应语义（事件不执行，处理器才执行） |
| B `AsyncEventHandler` 委托 | 无标准委托；Task 返回事件无消费约定 | **冲突** | 撞 VB 规范墙（事件委托不得有返回类型）；且与 C# 事件处理器（void）签名不兼容——跨语言挂载断裂，C# `async void` 处理器挂不上 Task 委托 |
| C `Func(Of Object, EventArgs, Task)` 属性 | 库层异步回调（非事件）正是 C# 生态实际形态 | **兼容但脱节** | 普通委托/属性，C# 侧完全兼容；但退出事件体系，与 C# 事件模式无互操作对象 |
| D 可等待 raise | 无对应；C# 连 await 链式语法都否决，优先处理 ConfigureAwait | **脱节** | C# 无"等待事件"概念；D 是 VB 独立探索，跨语言无消费方（C# 不会等待 VB 的 raise） |
| E 什么都不做（Async Sub + 库方案） | async void = 唯一官方事件异步路径，与 Async Sub 等价 | **兼容** | 与 C# 现实完全同向；库方案（`Func(Of Object, EventArgs, Task)` + `Task.WhenAll`）全是 BCL 类型，跨语言互通 |

整体判定：**无冲突项需要桥接 C#**；唯一"需桥接"的是元数据识别（见下），而非语言语义。

### 对 VBScript.NET 的适应建议

1. **默认安全——跟随 async void 模型**：VBScript.NET 事件处理器保持 `Async Sub`（= C# `async void`）。这是与 C# 互操作成本最低的默认：C# 事件（`EventHandler`）能直接挂 VB `Async Sub` 处理器，反之 VB 事件也能挂 C# `async void` 处理器——两边都是 void 委托，元数据无差异，零桥接。
2. **按需动态——库方案**：脚本宿主若出现"raiser 必须等 handler"的刚性需求，用类内异步通知列表 + `Await Task.WhenAll`（正文已给出完整设计）。它与 C# 完全互通：`Func(Of Object, EventArgs, Task)` 与 `Task.WhenAll` 都是 BCL 类型，C# 侧库无需任何语言协作。
3. **source-gen 桥**：库方案的样板（注册/触发/聚合）可由 VBScript.NET 的 source generator 生成，对齐 C# 生态"编译期生成替代运行时反射"的方向（索引 T5/T6）。"异步通知"对脚本作者表现为声明式，对运行时表现为普通委托列表，零反射、可 AOT/trimming——这是脚本层"按需动态"与"默认安全"双模路线在事件侧的具体落法。
4. **识别新元数据（需桥接的唯一项）**：消费 C# `async void` 处理器时，其 IL 是普通 void 方法 + `AsyncStateMachineAttribute` + `AsyncVoidMethodBuilder` 状态机。VBScript.NET 编译器/调试器需识别 async 状态机元数据，把"void 方法"还原为"异步事件处理器"（诊断、堆栈、调试体验）。`async-method-ref-parameters.md` 的已知安全 builder 白名单（含 `AsyncVoidMethodBuilder`）可作识别基线；这与决策文件 M8 提示的"识别 C# 新元数据属性"是同一类工作。
5. **跨语言事件消费契约的边界**：明确 **"VB `Async Sub` ↔ C# `async void` 事件处理器"是唯一可互操作的事件消费契约**。任何 Task 返回事件处理器都不应作为 VBScript.NET 的事件模型对外暴露——C# 生态无消费方，暴露即制造一个"挂了但没人等"的假契约。

### 对既有 RESOLUTION / 三态判定的影响

无实质改变；C# 现实**强化**正文的 Reject（as written）与 D = Table 判定：

- **无生态牵引**：C# LDT 从未讨论异步事件（镜像零命中）；Anthony 的一行是独立探索，C# 侧无同向力量。
- **官方路径即现状**：C# 官方 async 事件路径 = `async void` = VB `Async Sub`。正文"维持现状 + 库方案"与 C# 现实完全同向，没有偏离 C# 的 compelling reason（`..\..\vblang\meetings\2018\vbldm-notes-2018.12.19.md` 全句："Where we need to make a decision, we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages (many programmers work in C# and VB.NET)."）。
- **同步上下文优先级获 C# LDT 独立印证**：LDM-2020-11-11 否决 await 链式语法、把 `ConfigureAwait` 列为 async 的头号问题，与正文对 D 的同步上下文死锁担忧（RESOLUTION #6）同频。
- **VB 定位印证**：unsafe-evolution 的 VB 表态（R3 所引）显示 C# 团队预期 VB 不跟进底层演进；异步事件同理——C# 不会为 VB 而造，VB 也不该为 C# 生态而造。

### 引用纪律

- **逐字核实（镜像内）**：R1 两条（LDM-2017-02-28、`proposals\csharp-7.1\async-main.md`）；R2 一条（LDM-2020-11-11，含结论行）；R3 一条（`proposals\unsafe-evolution.md` VB 小节）；vblang 2018.12.19 原则句（正文已引，此处补全句）。
- **Suspect**：C# 规范 §14.15.3 关于 async void 事件处理器的精确措辞（镜像 `spec\classes.md` 仅为链接索引，正文在 dotnet/csharpstandard draft-v6，无法逐字核实；"仅用于事件处理器、异常经 SynchronizationContext 传播"为公认语义）。
- **OPEN QUESTIONS**：.NET BCL / ASP.NET 是否（以及哪些框架）提供标准异步事件回调（如 Blazor `EventCallback`）——属 dotnet/runtime 范围，csharplang 镜像无正文可核实。
- **检索记录**：`IAsyncEventHandler`、`AsyncEventHandler`、`async event` 在 `..\..\csharplang` 全库零命中（可复现检索事实）；正文第 9 节"C# 没有 async event"的论断在此获得镜像级证实。
