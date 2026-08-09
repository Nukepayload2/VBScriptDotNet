# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本场回到异步——但与 `meeting-agile-async.md` 那次不同，这次我们审的是一份 **inactive** 建议（Anthony 原文第 18.15 章，原文只有一段话）。审 inactive 建议的职责不一样：不是"要不要做"，而是"这份笔记够不够格被激活、激活需要什么信号、以及它和我们已经裁过的兄弟建议是什么关系"。

开场先摆两条与本场直接相关的既有裁定：
- **`meeting-agile-async.md`**（2018-05-30 判例同场回顾）：中间 `Await` 省略 Reject；`Agile Async`（隐式 `ConfigureAwait(False)` 的声明级修饰符）Table——动机真实、机制不可指定、复活信号两条。
- **`proposal-require-await-call.md`**：强制 `Await`、fire-and-forget 必须显式 `Call`，尚未定案，但与"让遗忘可配置"方向相反。

另注一条主线事实：Anthony D. Green 曾在 **2018-03-21 作为来宾出席主线 LDM**（`vbldm-notes-2018.03.21.md`："Anthony D. Green attended as a guest!"），并演示了属性驱动编译期改写（Smart Attributes），"Overall impression was positive"。这解释了 ModVB 与主线的真实关系：Anthony 是来过房间的客座设计者，他的沙盒延伸值得认真对待，但没有自动进入语言的特权。

## Agenda

* [Proposal: ConfigureAwait Options / Fire-and-Forget Exception Handling](#proposal-configureawait-options--fire-and-forget-exception-handling)

## Proposal: ConfigureAwait Options / Fire-and-Forget Exception Handling

_Related: [vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #167 – Support for Return? construct](https://github.com/dotnet/vblang/issues/167)；[vblang #282 – INotifyPropertyChanged / Smart Attributes 设计手记](https://github.com/dotnet/vblang/issues/282)；ModVB：`proposal-require-await-call.md`、`proposal-async-sub.md`、`proposal-agile-async.md`、`meeting-agile-async.md`_

### 场景与缺口

建议原文只有一段引用（Anthony 第 18.15 章，逐字）：

> Void-returning async methods are my white whale. I must find more ways to limit their spread. Additionally when "forgetting" a Task-returning async method I need to be sure folks can express/configure the various patterns to unobserved exceptions.

We started from 这一段，但它实际上**塞了三个缺口**，标题甚至承诺了第四个（"New ConfigureAwait options"）却没展开：

1. **缺口一——白鲸**：void 返回异步方法（`Async Sub`）蔓延，Anthony 想"找到更多办法限制其扩散"。
2. **缺口二——遗忘的 Task**：当调用方"忘记" `Await` 一个返回 `Task` 的异步方法时，未观测异常（unobserved exceptions）的处理模式需要能被"表达/配置"。
3. **缺口三——ConfigureAwait 选项**：标题写的是 "New ConfigureAwait options"，正文对这一半一个字都没说。`Probably`：它与第 11 章的 `Agile`（隐式 `ConfigureAwait(False)`）是同一头鲸，标题把两章的念头记在了一起。

本场先做的第一件事是把两条**异常通道**拆开，因为它们不是一回事，混写会污染任何后续设计：

- **通道一——`Async Sub` 的未处理异常**。spec（`statements.md` §Async Sub，逐字）："If `SynchronizationContext.Current` is `Nothing` at the start of the invocation, then any unhandled exceptions from an Async Sub will be posted to the Threadpool."；"If `SynchronizationContext.Current` is not `Nothing` at the start of the invocation, then `OperationStarted()` is invoked on that context before the start of the method and `OperationCompleted()` after the end. Additionally, any unhandled exceptions will be posted to be rethrown on the synchronization context."。也就是说 void 异步异常**不会静默消失**——它被投递到 `SynchronizationContext` 或线程池，由应用环境（`Application.ThreadException` 之类）接住。
- **通道二——被遗忘 `Task` 的未观测异常**。这是 .NET 运行时的 `TaskScheduler.UnobservedTaskException` 事件负责的全局模式。`Probably`（平台常识，非仓库内材料）：.NET Framework 4.0 里未观测异常在 finalizer 上会崩进程；4.5 起默认吞掉、仅触发事件；.NET Core/5+ 维持触发事件。仓库内 `..\..\..\vblang` 没有任何语言级讨论。

两条通道的**故障语义完全不同**：通道一是"抛给环境"，通道二是"在任务对象里躺着，等一个全局事件"。任何把两者混为一个"未观测异常配置"的设计，都会在 UI 事件处理器（通道一）和后台任务（通道二）上给出错误的默认行为。

### 候选方案

**PROPOSAL A — ConfigureAwait 选项语法化。** 把"await 后不捕获同步上下文"做成语言可配置的东西（方法级修饰符 / 项目级开关 / 逐 await 点的简写）。这是缺口三的直读，也是第 11 章 `Agile` 的另一张脸。

**PROPOSAL B — 未观测异常处理模式的表达/配置。** 给"忘掉的 `Task` 调用"一个语言级挂点，表达该调用异常时的处理策略（记日志、静默、上报全局处理器、交给调用方自定义回调）。

**PROPOSAL C — 借 `require-await-call`：遗忘变成编译错误。** fire-and-forget 的唯一合法路径是显式 `Call`。这从**根上消灭缺口二**——不是配置未观测异常，而是让"未观测"这件事必须被显式点名。

**PROPOSAL D — 维持现状 + 平台/生态。** `TaskScheduler.UnobservedTaskException` 提供全局模式；analyzer（CA2007 一类）在编译期提醒"别忘 `ConfigureAwait` / 别忘 await"；语言不加任何表面。

**PROPOSAL E — 借 `async-sub` 的默认异步返回类型：缩小白鲸面积。** `Async Sub` 默认生成 `Task`/`ValueTask`，void 只在显式请求（或事件处理器这种无处可返）时存在。白鲸从"默认形态"变成"显式角落"。

**PROPOSAL F — 什么都不做，保持 inactive。** 把白鲸的边界与通道划分写进文档，把可执行的子问题归还给兄弟建议。

### 权衡：Q&A

- **白鲸能被打败吗？不能消灭，只能缩小。** 关键事实：VB 的事件处理器是 `Sub`，签名不允许返回 `Task`；`Async Sub` 是异步事件处理器的唯一合法形态。`Probably`（C# 常识，非仓库内材料）：C# 面对同一个约束，用的是 `async void`。所以"void 返回异步方法"有一个**不可归零的残留面积**——事件处理器的面积。"limit their spread" 是正确措辞，但终点不是零，而是"void 只存在于无法返回 Task 的上下文"。

- **A vs 已 Table 的 `Agile`：同一头鲸。** `meeting-agile-async.md` 已经裁过：`Agile Async`（隐式 `ConfigureAwait(False)`）Table，因为 (i) `ConfigureAwait` 是 **awaited 值上的方法**，不是编译器开关，自定义 awaiter 没有它，编译器没有干净的"本点不捕获"开关；(ii) spec（`expressions.md`）写明续体是 awaiter 中介的，`SynchronizationContext` 的捕获发生在 `TaskAwaiter.OnCompleted` 内部（spec 的示例 awaiter 正是 `Dim sc = SynchronizationContext.Current` 后 `sc.Post`），且 resumption delegate "first restores `System.Threading.Thread.CurrentThread.ExecutionContext`"——任何"清空上下文再恢复"都会与 ExecutionContext 流纠缠；(iii) 命名零辨识度。A 若要复活，必须解决**同一个机制问题**，而本建议连问题都没提。**没有新证据，沿用 Table。**

- **A 的"选项"到底是什么？** 我们枚举了四种读法：方法级隐式 `False`（= `Agile`）；项目级捕获默认开关（撞 `async-sub` 的"可配置默认"概念，但那是返回类型不是捕获行为）；逐 await 点简写（`ConfigureAwait(False)` 已经够短）；捕获策略配置。四种读法都没有自然的 VB 语法挂点，且全部与"`ConfigureAwait` 是库 API"相撞。**We don't think 有一个能独立于库与运行时演进的读法。**

- **B 的语法挂点在哪？这是最尖锐的一问。** VB 语句不接受属性/修饰符——属性只能用于声明。要给"一个忘掉的调用"挂 handler，语言需要一个**新的语句形式**或**新的修饰符**，而今天的语法树里没有这个位置：

  ```vb
  ' 假设的“给忘掉的调用挂 handler”语法（本场自造，仅用于评估，不是原文）。
  ' 属性只能用于声明，不能修饰语句 —— 第一行就不合法：
  <OnUnobserved(AddressOf LogIt)>
  SaveAsync()
  ```

  不做新语句形式，剩下的唯一表达方式是**包装函数**——而包装函数已经是库代码能写的东西了，不需要语言。

- **B vs 平台：第二种做事方式。** `TaskScheduler.UnobservedTaskException` 已经提供一个全局模式。语言再加一个 per-call 配置，是同一件事的第二条路（设计原则 #3）。且 per-call 策略和全局事件同时存在时，谁优先、合并还是覆盖，设计里没人定义。

- **B vs C：一个让遗忘不可能，一个让遗忘可配置——方向相反。** 这是本场最重的一锤。同组的 `require-await-call` 主张**默认禁止遗忘**（未 await 的异步调用报错，fire-and-forget 必须 `Call`）；B 主张**让遗忘可配置**（给忘掉的调用一个 handler）。两者不能同时进入设计。而判例站在 C 一边：2018-05-30 我们审 #167 `Return?` 的原话（逐字）：**"We think this is a bad idea. Control flow would be altered by a very subtle character. It's not the same meaning as other uses as ? (any alteration in control flow)"**（Labels: LDM Reviewed: No Plans）。把"遗忘"做成一个可配置的角落，等于把 bug 仪式化——设计原则清单里"避免隐蔽的控制流/语义变化"的红线正对着它。

- **B 与"不 breaking 就没有效果"的矛盾。** 未观测异常的默认行为是运行时定的（吞掉 + 事件）。语言若只加"可配置"而不改默认，价值为零；若改默认（比如改成默认上报），就是**改变既有代码运行时语义**——主线的红线上写了"我们几乎绝不做 breaking change"（`vbldm-notes-2018.12.19.md`："No breaking changes."）。B 在"无效果"与"breaking"之间没有第三态。

- **C 的 `Call` 重载问题（留给 require-await-call，本场只记录边界）。** `Call` 的历史含义是"以语句形式调用"（VB6 遗产）；叠上"fire-and-forget 唯一合法路径"是语义重载，容易让读者以为 `Call` 本身改变异常行为。这个歧义属于 require-await-call 自己的设计，不归本建议。

- **E 能吸收多少白鲸？** 大部分。`Async Sub` 默认返回 `Task`/`ValueTask` 后，void 面积缩到"显式请求"的角落；event handler 的残留面积是可接受的、可解释的。白鲸的真正答案可能不是"消灭 void"，而是"让 void 成为显式选择 + 让遗忘成为错误"（E + C 的组合）。

- **ValueTask 陷阱。** 遗忘 `ValueTask` 比遗忘 `Task` 更糟——它只能 await 一次，且同步完成路径根本没有可观察的任务对象：

  ```vb
  ' 遗忘 ValueTask：若同步完成，连“未观测任务异常”通道都不存在。
  Dim vt As ValueTask = MaybeCompleteAsync()
  ' 遗忘 vt —— 同步路径下异常原地消失，事件都触发不了。
  ```

  `Suspect`：原文只提 `Task`，这是对的；但任何向 `ValueTask`（含 `async-sub` 默认异步类型）的扩展都必须把"未观测异常配置"显式排除，否则会发明一个对 `ValueTask` 不健全的机制。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

无语法提案——这是本建议唯一干净的地方：它**没有凭空发明语法**（评价标准里"凭空虚构原文没有的语法"是最重的品质红线，它避开了）。反过来说，正因为它连一个候选文法都没有，它没有资格进入设计。若未来设计 B，任何语句级 handler 语法都会撞上"语句不接受属性/修饰符"的现状；若设计 A，`Agile`/`Async` 的上下文关键字保留规则（spec：`Await` 仅在含 `Async` 修饰符的立即闭合方法/lambda 内保留）要对新关键字做全套兼容分析。

#### 2. 角案例与边界语义

- **事件处理器**：`Async Sub` 的不可归零面积（见 Q&A）。任何"限制 void 蔓延"的规则必须给 `Handles` 子句开显式豁免，否则会打断 WinForms/WPF 的核心编程模型。
- **同步完成的 Task**：`Task.FromResult` 或已完成的 Task 被遗忘——没有挂起点，异常不会发生；配置面覆盖不到也无需覆盖。
- **已附加 continuation 的 Task**：`SaveAsync().ContinueWith(...)` 不算遗忘（异常被 continuation 观察）；"遗忘"的精确判定（调用点无 await、无赋值、无 continuation）是 require-await-call 的活，不是本建议的。
- **`Let t As Task = SaveAsync()`**：赋给变量不算遗忘（建议原文在 require-await-call 的边界里承认 "Conversions to task objects still allowed"）。
- **ValueTask**：见 Q&A，必须排除。

#### 3. 作用域与绑定

若 B 的 handler 绑到**方法**（attribute），同一方法的多个调用点共享一个策略，无法 per-call 表达；若绑到**调用点**，需要一个不存在的语句级语法节点。若 A 是项目级开关，绑定到编译单元——那它已经不是语言特性而是编译器选项，而选项的爆炸（捕获行为、默认返回类型、名称后缀）正是 `async-sub` 建议自己在 Drawbacks 里承认的复杂度来源。

#### 4. 与既有特性的交互

- **`Async Sub` 语义**（spec 逐字，见"场景与缺口"）：void 异步异常会投递到 SynchronizationContext/ThreadPool。任何"配置未观测异常"的设计如果覆盖到 `Async Sub`，会与这条既有行为重叠甚至冲突。
- **#37 Await in Catch/Finally**：主线已批准在案（2014-02-17："*Approved. Aligns with C# vNext feature* We'll leave the design work to C# LDM, and do exactly the same."；2017-08-09："The feature is approved in principle but needs its priority driven by other platform changes"；2018-02-28："If we drop the difficult scenarios and provide support and disallow Await and these jumps to happen together, this is doable."）。这是主线 async 的唯一活跃前线，本建议不应抢它的顺序。
- **`SyncLock` / `Catch` / `Finally` / 查询表达式内禁 `Await`**（spec）：若走 require-await-call 的强制方向，这些区域内的"未 await 调用"判定也要一起定义。
- **`#Ignore Warning` / 警告策略**：若强制 `Await` 降级为警告，`#Ignore Warning` 是既有的逃生门——这比 B 的"per-call handler 语法"便宜得多。

#### 5. Breaking change 与兼容性

核心矛盾见 Q&A：**B/A 要么不改默认（无效果），要么改默认（breaking）**。改未观测异常默认行为 = 改变既有代码运行时语义，主线判例几乎零容忍。require-await-call（C）是重型 breaking（大量现有代码未 await），但它至少有清晰的迁移路径（报错→`Call`/`Await` 显式化）与 `langversion` 门控空间；B 连迁移路径都没有。

#### 6. Option Strict / 编译选项分叉

spec（`expressions.md` 逐字）："If its type is `Object` then all processing is deferred until run-time."——宽松模式下 `obj.SaveAsync()` 的返回类型是 `Object`，静态检查**看不穿**它是不是 Task。"忘没忘 await"的判定在 Option Strict Off 下必然放宽，两条路径行为分叉。任何强制方向（C）都要为宽松模式设计"无法静态判定"的处理；B 若依赖类型信息配置 handler，同样分叉。

#### 7. IDE / IntelliSense

未 await 调用的警告提示（若走警告路线）、`Call` 的语义标注、断点/步进对 fire-and-forget 的显示（调用点不挂起，调试器应标注"已触发，不等待"）。B 的 per-call handler 需要新的代码透镜或错误文案。这些都需要原型，而本建议没有任何 IDE 设计。

#### 8. 数据 / 普遍性

无数据。2018-05-30 主线原话（逐字）："We believe the majority of Visual Basic customers (there are hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now." 在"数十万安静客户"的业务代码里，遗忘 Task 的频率没有量化。`Suspect`：业务代码里的 fire-and-forget 大多是 UI 事件触发，异常走的是 `Async Sub` 通道（通道一），不是被遗忘 Task 的通道（通道二）——通道二的场景可能比 Anthony 设想的窄。`ConfigureAwait(False)` 样板在库代码里高频（这是 `Agile` 的动机，真实），但那是另一个问题。

#### 9. 更简替代

- `TaskScheduler.UnobservedTaskException`：全局模式，零语言表面。
- **CA2007 一类 analyzer**：编译期提醒"别忘 `ConfigureAwait`"、别忘 await——C# 生态的默认路径。
- `async-sub` 默认返回类型（E）：白鲸的主要缩小手段。
- `require-await-call`（C）：让遗忘不可能，而非让遗忘可配置。
- 包装函数（`Sub ForgetAsync(task As Task, handler As Action(Of Exception))`）：B 想要的能力，库代码已经能写。

全部比语言语法更简单、更不破坏。

#### 10. 复杂度 / 成本 / 优先级

语言表面对一个运行时/平台关切 = 高成本、低价值。主线 async 的焦点是 #37（且 2017-08-09 明确"priority driven by other platform changes"）；ModVB 的异步优先级应花在 `async-sub`（E）与 `require-await-call`（C）上——它们有明确语法、有使用者、有判例支撑。本建议的原创部分（A/B）既无语法也无机制，排不进任何序列。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题（本建议尚未生成任何 IL）。但"编译器级不捕获同步上下文"没有 IL/CLR 锚点（与 `Agile` 相同）；未观测异常配置纯框架侧。`ValueTask` 的"只能 await 一次"是运行时硬约束，任何配置机制必须绕开它。

#### 12. 值不值得做

- **A（ConfigureAwait 选项）**：价值（免样板）真实但窄；机制不可指定；与已 Table 的 `Agile` 重叠。**不值得现在做。**
- **B（未观测异常配置）**：价值（防 bug）× 成本（新语句形式或修饰符）× 风险（方向与 require-await-call 相反、无效果/breaking 两难）。**不值得。**
- **白鲸观察（缺口一）**：有价值，但解决方案在 E + C，不在本建议。**把观察留下，把方案归还。**

### VB 基因对照

- **消除常见样板（原则 #9）**：`ConfigureAwait(False)` 样板真实，但 `meeting-agile-async.md` 已裁定走 `async-sub` 项目级默认 + analyzer，而非语言修饰符。本建议没有给出推翻那个裁定的新证据。
- **不引入"第二种做事方式"（原则 #3）**：B 是"第二种未观测异常处理方式"（`TaskScheduler.UnobservedTaskException` 已存在）。直接踩线。
- **默认跟随 C#（原则 #4）**：2018-12-19 主线原话（逐字）："Where we need to make a decision, we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages (many programmers work in C# and VB.NET)." C# 没有语言级 ConfigureAwait 选项，也没有语言级未观测异常配置——VB 无充分理由偏离。
- **不为边缘场景加特性（原则 #6）**：未观测异常配置是库维护的边缘场景，不是 85% 业务代码的高频痛点。
- **避免隐蔽的控制流/语义变化（原则 #7）**：A 的隐式 `ConfigureAwait` 是隐蔽语义（`Agile` 判例）；B 把"遗忘"仪式化。
- **保持 VB-like / 读起来像英语（原则 #2、#5）**：无语法，无从评价——但白鲸动机本身是**真实 VB 关切**：`Async Sub` 是 VB 特有表面（VB 6 事件驱动遗产的直接后代），C# 用 `async void` 面对同一约束。这一半是"继承 VB 基因"的。
- **与主线关系（对照表 2.3）**：不在主线对照表内，属 **Anthony 独立延伸**。主线 async 焦点是 #37（等待平台）；主线对"遗忘/未观测异常"无语言级记录（仓库内 grep `ConfigureAwait`、`unobserved` 无 LDM 命中）。与 `require-await-call` **方向相反**；与 `async-sub`（E）**协作**；与已 Table 的 `Agile` **重叠**。

### RESOLUTION:

1. **保持 inactive（Table）。** 本建议是一个 note-to-self，不是设计：无语法、无机制、无数据。不激活。
2. **拆分。** 与 `meeting-agile-async.md` 的捆绑教训相同：ConfigureAwait 选项与未观测异常处理是两个独立主题，捆绑评估会互相掩盖。拆分后各自对号入座。
3. **ConfigureAwait 选项（A）：并入 `Agile` 的 Table 状态。** 共享 `meeting-agile-async.md` 的复活信号（见下）。没有新证据，不单独立项。
4. **未观测异常处理（B）：语言层面不做。** 平台已有 `TaskScheduler.UnobservedTaskException`；生态已有 analyzer；若语言要介入，方向是 **require-await-call（让遗忘不可能）**，不是让遗忘可配置。B 与 C 方向互斥，且 #167 `Return?` 判例站在 C 一边。
5. **白鲸（void 异步蔓延）：可缩小，不可消灭。** 缩小手段 = `async-sub` 默认返回类型（E）+ `require-await-call`（C）；event handler 的残留面积是不可避免的、可接受的。把这条边界写进文档，作为对"void 异步"的正式立场。
6. **认可本建议自己的 Alternatives 分解。** 它写的三条（维持现状 / 强制 Await + `Call` / 默认异步类型）正是正确的处置路径，以它替代本建议的原创内容。
7. **记录通道划分。** 未观测异常（被遗忘 Task）≠ void 异步异常（`Async Sub` 投递到 SynchronizationContext/ThreadPool）。任何未来 async 设计不得混写两条通道。

### Implication:

- 将 `proposal-configureawait-options.md` 标注为 **LDM No Plans（Table）**，头部注明与 `meeting-agile-async.md`（`Agile` 重叠）、`proposal-require-await-call.md`（方向相反）、`proposal-async-sub.md`（白鲸方案）的关系。
- 给 `async-sub` 团队发对表请求：确认"默认异步返回类型"是白鲸缩小的主要载体；明确 `Handles` 事件处理器强制 `Sub` 的残留面积是否豁免。
- 给 `require-await-call` 团队记录边界：若采纳强制 `Await`，`Call` 的 fire-and-forget 语义必须在**该建议内**明确未观测异常策略（谁接、接几次、是否绕过 `TaskScheduler.UnobservedTaskException`），不在本建议设计。
- 起草一份"两条异常通道"备忘（`Async Sub` 通道 vs 被遗忘 Task 通道），供 `async-iterator` / `async-event` / `async-sub` 团队参考。
- 跟进主线 #37（Await in Catch/Finally）与 #167（`Return?`）作为本类问题的判例库；跟进 #282（Anthony 的 Smart Attributes 演示）作为 ModVB 关系的事实锚点。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：C# LDM 或 .NET 运行时是否出现过语言级 ConfigureAwait / 未观测异常配置的讨论，仓库内无记录（grep `ConfigureAwait`、`unobserved` 在 `..\..\..\vblang\meetings/` 零命中）。`Suspect`：C# 生态默认路径是 analyzer（CA2007 一类）与 `TaskScheduler.UnobservedTaskException`，但此条来自生态常识，需外部核实。
- `OPEN QUESTIONS`：`TaskScheduler.UnobservedTaskException` 在 .NET Framework 4.0（finalizer 崩进程）与 4.5+（默认吞掉）之间的准确行为差异，平台常识，仓库外材料。`Probably`。
- `OPEN QUESTIONS`：Option Strict Off 下"未 await 调用"能否静态判定（`Object` 型操作数延迟到运行时），若 `require-await-call` 落地，宽松模式的放宽规则待定。
- `TODO`：量化 VBScript.NET 目标代码中"被遗忘 Task"与"`Async Sub` 事件处理器"的占比，为白鲸立场与 require-await-call 优先级补数据。
- `Follow-up`：若 C# / .NET 出现 first-class 未观测异常策略 API（例如每应用配置），带回 VB 重审本建议的 Table 状态——但预期结论是"跟随平台，不发明语法"。

### 状态

- **LDM 状态：LDM No Plans（Table）。**
- **三态判定：Table（整体）**——本建议的原创部分（A/B）不具备设计条件，且有更优的既有路径（E + C + 平台事件）；白鲸观察保留为设计立场。**值得记住，不值得现在做。**

#### 激活所需信号（inactive 建议的明确清单）

1. **`require-await-call` 被采纳**：届时 `Call` 需要未观测异常策略——那是在 require-await-call 内设计，不是在本建议；本建议的角色变成"确认 `Call` 的异常语义与 `TaskScheduler.UnobservedTaskException` 的关系"。
2. **编译器级通用"不捕获"机制出现**（与 `Agile` 共享的复活信号）：例如 Roslyn 出现统一的"awaiter 无上下文恢复"开关，或明确限定 Task/ValueTask 家族并给出 `ConfigureAwait` 缺失时的错误策略。在此之前，A 不可实现。
3. **C# 或 .NET 出现语言级/first-class 动向**：VB 跟随而非发明（原则 #4）。
4. **数据证明业务代码中遗忘 Task 高频**：这会把优先级导向 require-await-call（防遗忘），而不是本建议（配遗忘）。

---

## 附录：特性评价

# 建议评价报告：proposal-configureawait-options.md

## 评价对象

- 建议：proposal-configureawait-options.md — `ConfigureAwait` 选项与未观测异常处理（fire-and-forget）
- 来源：Anthony 原文第 18.15 章 "New ConfigureAwait options/fire-and-forget exception handling"（`..\..\AnthonyDesign_wordpress.txt` L3042–3046，仅一段话，无语法无示例）
- 配方目标：① 限制 void 返回异步方法（`Async Sub`）蔓延；② 让"遗忘" `Task` 返回异步方法时的未观测异常处理模式可表达/配置；③ 标题承诺的"新 ConfigureAwait 选项"（正文未展开）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。两个目标都停在口头：白鲸无量化、未观测异常配置无语法、无示例可演示；标题一半（ConfigureAwait 选项）正文零展开；≥4 个关键未决点按规则封顶效果 | 已检查（书面，无原型） | 核心语法未定型 → 效果未显现；无任何可编译示例 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。捆绑"ConfigureAwait 选项"与"未观测异常配置"两个独立主题；无 VB 化设计；唯有一半动机（白鲸/`Async Sub`）继承真实 VB 基因（VB6 事件驱动遗产），但未转化为任何设计 | 已检查 | 捆绑违背职责单一；无语法即无 VB 化可言；与已 Table 的 `Agile` 重叠未声明 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、诚实标注"原文未给出语法"（未凭空虚构语法，避开了最重红线）；但 Detailed design 整节无设计、无示例、无 BNF；未决问题 4 个关键设计点如实列出但把整个设计责任推回原文；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | Detailed design 空转；无兼容性章节；未提及与 #167/`Agile`/require-await-call 的判例关系 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。若激活：B/A 要么不改默认（无效果）要么改默认（breaking），无第三态；与同组 require-await-call 方向相反 = 一致性断裂；与 `Agile` 重叠 = 重复立项风险；暗风险无对冲设计 | 已检查（预测待定） | 无效果/breaking 两难未识别；方向冲突未声明；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源标了 Anthony 18.15（准确）；但未声明 ConfigureAwait 部分与第 11 章 `Agile`（同仓库已有、已 Table）是同一头鲸；未声明未观测异常机制借自 .NET 运行时（`TaskScheduler.UnobservedTaskException`）；Alternatives 正确列了 sibling 但未说明"此建议的价值已被它们吸收" | 已检查 | 跨章节/跨建议关联缺失；对 .NET 平台机制的借用在正文未提，只在 Alternatives 隐含 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离**——白鲸动机继承真实 VB 基因（`Async Sub` 是 VB 特有表面，C# 用 `async void` 面对同一约束）；但方案无 VB 化形态；B 违反原则 #3（第二种未观测异常处理方式）；A 若以隐式 `ConfigureAwait` 形态出现，违反原则 #7（隐蔽语义，`Agile`/#167 判例）；原则 #4（默认跟随 C#）——C# 无语言级动向，VB 无偏离理由。
- **与主线关系：Anthony 独立延伸**——不在 2.3 对照表内；主线 async 焦点是 #37（已批准在案，等待平台），对 ConfigureAwait/未观测异常无语言级记录（仓库内 grep 零命中，`Suspect`）；与 `require-await-call` 方向相反、与 `async-sub` 协作、与已 Table 的 `Agile` 重叠。
- **破坏性变更：潜在有（若激活）**——改变未观测异常默认行为即改变既有代码运行时语义；建议文档未分析且无 migration 路径。维持 inactive 则无破坏。

## 总评

- **达成程度：未达成**——作为设计，它是一段 note-to-self，无语法、无机制、无数据、无判例关联。作为"设计立场"，它的白鲸观察与通道划分是有价值的，但价值经由 E + C + 平台事件落地，不经由本建议落地。
- **LDM 三态建议：Table（保持 inactive）**——原创部分（A/B）不具备设计条件；有价值的子部分已被 `async-sub`（E）、`require-await-call`（C）、`TaskScheduler.UnobservedTaskException` 拥有。复活信号见正文"激活所需信号"四条。
- **主要问题**：① 无语法无设计，仅一段引用；② 捆绑两个独立主题（ConfigureAwait 选项 / 未观测异常配置）；③ B 与 require-await-call 方向相反，且踩 #167 判例；④ B/A 陷于"无效果 vs breaking"两难；⑤ ConfigureAwait 部分与已 Table 的 `Agile` 重叠且机制不可指定；⑥ 未做通道划分（`Async Sub` 通道 vs 被遗忘 Task 通道）。

## 返工建议

- **补充章节（若想激活）**：先拆分声明——ConfigureAwait 选项与未观测异常配置各自独立成建议；各补 Detailed design（文法或 API 形态）、兼容性分析（默认行为变更的逐例清单）、Option Strict 分叉（`Object` 操作数）、ValueTask 排除分析。
- **补充证据**：VBScript.NET 目标代码中"被遗忘 Task"占比数据；`ConfigureAwait(False)` 样板占比（与 `Agile` 复活评估共享）；可编译示例（当前为零）。
- **未决问题处理**：B 的方向取舍移交 `require-await-call`（防遗忘 vs 配遗忘二选一）；A 的机制问题移交 `meeting-agile-async` 的 `Agile` 复活信号；`Call` 的未观测异常语义在 require-await-call 内定案。
- **设计探索**：确认"event handler 强制 `Sub`"的残留面积后，白鲸立场的正式文档化（E + C + 通道划分）；若 C#/.NET 出现 first-class 未观测异常策略 API，建立"跟随平台而非发明语法"的复审框架（复用本建议的激活信号 3）。

---

## 附录：C# 生态与互操作考量

> 本附录在「附录：特性评价」之后追加，只记录 C#/CLR/.NET 生态对本提案主题的现实走向与互操作含义，不改写正文判定。对应关系说明：本提案（ConfigureAwait 选项 / 未观测异常配置 = async 的同步上下文配置）在 C# 侧的对应面是 **async 演化**，而索引 `..\..\..\csharplang-index.md` 的 T1–T8 以 interop 主线为主、**未单列 async 演化**（T8 的 C# 15/16 未来方向未覆盖 async2）；因此本附录基于对 `..\..\..\csharplang` 的直接 Grep/Read 核实，逐条标注出处。

### 相关 C# 现实方向

C# 把「ConfigureAwait / 同步上下文捕获」长期列为 async 的头号问题，但**从未落地语言级语法方案**。三份相隔数年的 LDM 记录构成完整证据链：

1. **LDM-2019-07-17**（Triage）首次明确"值得做、留待未来主版本"（逐字）：
   > "ConfigureAwait and related issues have been brought up many times. We think it's worth addressing, and should look at possible designs."
   > "Let's look it for a future major release."
   → `meetings\2019\LDM-2019-07-17.md`（Proposals for ConfigureAwait with context）

2. **LDM-2020-11-11** 把 ConfigureAwait 明确为 async 头号问题，并在两处表达"语言语法解决不了"：
   - 审 `await` 后缀操作符（#4076）时（逐字）：
   > "Chainability of `await` expressions isn't the largest issue on our minds with `async` code today: that honor goes to `ConfigureAwait`, which this does not solve."
   > "Rejected. We do like the space of improving `await`, but we don't think this is the way."
   → `meetings\2020\LDM-2020-11-11.md`（Add `await` as a dotted postfix operator）
   - 审 AsyncMethodBuilder（#1407）时（逐字）：
   > "Can this solve `ConfigureAwait`? We don't think so: this controls the method builder, not the meaning of `await`s inside the method, so while it could potentially change whether a method call returns a task that synchronizes to the thread context by default, it could only do that for methods defined in your assembly, which would just lead to confusing behavior."
   → `meetings\2020\LDM-2020-11-11.md`（AsyncMethodBuilder）

3. **LDM-2024-04-01（Async2 / runtime-handled-tasks）** 是最近一次（2024）对 async 的大规模前瞻，方向从「语言语法」转向「程序集级配置 + 运行时开关」，且把 ConfigureAwait 描述为可能成为**迫使程序集级配置方案**的 tipping point（逐字）：
   > "Another topic we discussed briefly was `ConfigureAwait`. This issue may end up being the tipping point that forces an assembly-wide configuration solution, as we don't want to force developers to realize the `Task` return by calling `ConfigureAwait`."
   → `meetings\2024\LDM-2024-04-01.md`（Async improvements (Async2)）
   同一场还讨论了触发方式（逐字）："the compiler can simply look for a `RuntimeFeature` flag, and turn on the new strategy if it's available."——配置点是**平台/运行时**，不是语言表面。

已落地/在案的 C# 侧机制：
- **AsyncMethodBuilder（C# 10）**：允许 per-method 覆盖 async 方法 builder 类型——"Allow per-method override of the async method builder to use."（`proposals\csharp-10.0\async-method-builders.md`，Summary）。但如 LDM-2020-11-11 裁定，它**不能**解决 ConfigureAwait（控制的是 builder，不是 await 含义）。
- **task-like 类型机制**（逐字）："A _task type_ is a `class` or `struct` with an associated _builder type_ identified with `System.Runtime.CompilerServices.AsyncMethodBuilderAttribute`."（`proposals\csharp-7.0\task-types.md`）——这是 async 状态机/awaiter 的 CLR 锚点，与 VB 的 `Async Sub`/`Async Function` 生成逻辑共享同一底座。
- **async void（C#）**：与 VB `Async Sub` 面对同一"事件处理器无处返回 Task"约束；C# 无消灭计划。LDM-2024-04-01 的 Quote of the Day 都拿它开涮："He definitely awaited an `async void`"（`meetings\2024\LDM-2024-04-01.md`）。

生态默认路径（无语言表面）：**库级 `ConfigureAwait(False)` 约定 + analyzer（CA2007 一类）**。`ConfigureAwait` 作为 awaited 值上的方法返回 `ConfiguredTaskAwaitable`，这一"把 await 推迟到返回值"的形态在 C# 可空分析里也被点名（逐字）："There are a lot of methods that defer awaiting a `Task` or `Task`-like type until after the returned thing. For example, `ConfigureAwait`, `ValueTask.AsTask`, `Task.WhenAll`, user libraries to add awaiters to `ValueTuple`s of tasks, etc."（`meetings\2023\LDM-2023-01-18.md`）。

### 现实 vs 提案

| 提案要素 | C# 现实 | 关系 | 理由 |
|---|---|---|---|
| A：ConfigureAwait 选项语法化 | C# 7 年未给语法；await 后缀被拒（2020）；async2 走程序集级配置 + runtime flag（2024） | **不冲突，但不背书语言语法** | C# 的现实答案接近 A 的「项目级开关」读法，但放在平台/运行时，不是语言表面 → 支持既有 Table（跟随平台而非发明语法） |
| B：未观测异常配置 | C# 无语言级未观测异常配置；只有 `TaskScheduler.UnobservedTaskException` + analyzer | **脱节** | C# 现实没有 B 想要的挂点；原则 #4（默认跟随 C#）意味着 VB 也不应发明 per-call handler 语法 |
| C：require-await-call（强制 Await） | C# 无"强制 await"语言特性，只有 analyzer 警示未 await 调用（VSTHRD/CA2012 一类） | **兼容** | C# 生态同向（警示遗忘），把"遗忘=错误"做成语言级是 VB 更强的承诺；不被 C# 现实反对 |
| E：Async Sub 默认返回 Task/ValueTask | C# `async void` 残留面积与 `Async Sub` 对称，无消灭计划 | **兼容** | C# 现实既不构成消灭 void 的理由，也不反对缩小 void 面积 |
| ValueTask 陷阱 | async2 恰好重视 ValueTask（避免 materialize Task） | **需桥接** | 若 .vbx 走 E 默认 ValueTask，需与 async2 的运行时状态机/`IValueTaskSource` 语义对齐，且必须显式排除"未观测异常配置"对 ValueTask 的适用 |

要点：**本提案与 C# 现实最"同向"的部分是动机（ConfigureAwait 样板是真实痛点，C# 自己也认），最"脱节"的部分是解决方案形态（语言语法）**。C# 的走向是从"每次 `ConfigureAwait` 调用"走向"程序集级/运行时配置"——这与 `meeting-agile-async.md` 裁定的"项目级默认 + analyzer"方向一致，而**不是**本建议 A 的任何 per-await/方法级语法读法。

### 对 VBScript.NET 的适应建议

1. **把激活信号 3 的形态从"等 C# 发明语法"改为"等平台给出配置出口"**。C# 现实（async2 / 程序集级配置）表明语言级 ConfigureAwait 选项不会出现；.vbx 应等 C#/.NET 的程序集级配置出口（attribute on assembly / runtime flags）落地后，映射为 .vbx 项目选项或模块特性——而非独立发明 VB 语法。
2. **识别新元数据（必须桥接）**：
   - `AsyncMethodBuilderAttribute`（C# 10，method-level）：VB 编译器需能读取 method-level builder 覆盖，跨语言调用时才不会错配 async 状态机 builder；若 .vbx 让 `Async Sub` 默认返回 Task/ValueTask（E），也要处理显式 `[AsyncMethodBuilder]`。
   - `RuntimeFeature` 标志 / runtime-handled-tasks：C# 编译器按运行时特性标志切换生成策略（async vs async2）；VB 编译器若生成自己的状态机形状（AsyncSub/AsyncTask state machine），需决定"跟随运行时新机制"还是"显式锁定旧生成策略"，否则跨程序集互操作时状态机契约不一致。
3. **analyzer 桥（source-gen 桥）**：C# 生态默认路径是 CA2007 一类编译期提醒——VBScript.NET 把「别忘 ConfigureAwait / 别忘 await」做成内置诊断（.vbx 的 script 编译管线可直接挂 Roslyn analyzer），比语言表面便宜，且与本场结论一致。
4. **继承库级约定，无需语言支持**：`ConfigureAwait(False)` 在 VB 里就是普通方法调用（`ConfiguredTaskAwaitable` 类型）；关键是 Option Strict 下类型可见、Option Strict Off 下（`Object` 操作数，spec `expressions.md`）静态看不穿——与正文第 6 节的分叉分析一致。
5. **E 与 async2 性能方向同向**：C# async2 的主要收益来自"避免 materialize Task"（大部分 async 调用栈其实没有真正的挂起点）；.vbx 若让 `Async Sub` 默认返回 `ValueTask`，正好与这一性能路线对齐，可提前作为 .vbx 的默认异步类型。

### 对既有 RESOLUTION / 三态判定的影响

- **三态 Table 判定保持，且被 C# 2024 现实强化**：C# 从 2019 到 2024 数次讨论 ConfigureAwait 仍未给语言语法，方向转向平台/运行时配置。这印证了正文"机制不可指定"与"跟随平台而非发明语法"的判定。
- **修正原 OPEN QUESTION 的一半**：正文 OPEN QUESTIONS 写"C# LDM 或 .NET 运行时是否出现过语言级 ConfigureAwait / 未观测异常配置的讨论，仓库内无记录（grep vblang 零命中）"。该 grep 只查了 `..\..\..\vblang`；在 `..\..\..\csharplang` 里 **C# LDM 对 ConfigureAwait 的讨论记录充分**（2019-07-17、2020-11-11、2024-04-01 三处直接讨论）。修正结论：C# LDM **多次讨论 ConfigureAwait 但未落地语言方案**；未观测异常配置 C# 侧**无语言级讨论**（与原 meeting 结论一致）。
- **激活信号 3 的形态修正**：从"若 C# 或 .NET 出现语言级/first-class 动向"精确化为"若 C#/.NET 出现程序集级/runtime 配置出口"——触发后 VB 是**映射**（项目选项/模块特性）而非发明语法。
- **其余 RESOLUTION 条目不受影响**：拆分（1/2）、B 不做（4）、白鲸立场（5）、通道划分（7）均与 C# 现实兼容或同向；C# 现实不构成推翻任何一条的理由。

### 引用纪律 / OPEN QUESTIONS

已逐字核实并标注出处的原文：
- "ConfigureAwait and related issues have been brought up many times. We think it's worth addressing, and should look at possible designs." → `meetings\2019\LDM-2019-07-17.md`
- "Let's look it for a future major release." → `meetings\2019\LDM-2019-07-17.md`
- "Chainability of `await` expressions isn't the largest issue on our minds with `async` code today: that honor goes to `ConfigureAwait`, which this does not solve." → `meetings\2020\LDM-2020-11-11.md`
- "Rejected. We do like the space of improving `await`, but we don't think this is the way." → `meetings\2020\LDM-2020-11-11.md`
- "Can this solve `ConfigureAwait`? We don't think so: this controls the method builder, not the meaning of `await`s inside the method…" → `meetings\2020\LDM-2020-11-11.md`
- "Another topic we discussed briefly was `ConfigureAwait`. This issue may end up being the tipping point that forces an assembly-wide configuration solution, as we don't want to force developers to realize the `Task` return by calling `ConfigureAwait`." → `meetings\2024\LDM-2024-04-01.md`
- "the compiler can simply look for a `RuntimeFeature` flag, and turn on the new strategy if it's available." → `meetings\2024\LDM-2024-04-01.md`
- "Allow per-method override of the async method builder to use." → `proposals\csharp-10.0\async-method-builders.md`
- "A _task type_ is a `class` or `struct` with an associated _builder type_ identified with `System.Runtime.CompilerServices.AsyncMethodBuilderAttribute`." → `proposals\csharp-7.0\task-types.md`
- "There are a lot of methods that defer awaiting a `Task` or `Task`-like type until after the returned thing. For example, `ConfigureAwait`, `ValueTask.AsTask`, `Task.WhenAll`…" → `meetings\2023\LDM-2023-01-18.md`

OPEN QUESTIONS / Suspect：
- **Suspect**：库级 `ConfigureAwait(False)` 约定与 Stephen Toub 的 ConfigureAwait FAQ 属 dotnet/runtime 生态，csharplang 内无正文（仅 LDM-2024-04-01 隐式引用 "we don't want to force developers to realize the `Task` return by calling `ConfigureAwait`"）；约定本身未在本仓库核实。
- **OPEN QUESTIONS**：async2 / runtime-handled-tasks 是否落地、何时落地未定（`meetings\2024\LDM-2024-04-01.md` 引用的 runtimelab 文档是实验分支，需外部核实）。
- **Suspect**：CA2007 / VSTHRD / CA2012 等 analyzer 名称属 dotnet/roslyn-analyzers 生态，csharplang 无正文（生态常识）。
- **OPEN QUESTIONS**：C# 是否有针对「未观测异常配置」（B 对应）的未记录讨论——csharplang 内未发现语言级正文，与原 meeting 结论一致；dotnet/runtime 侧（`TaskScheduler.UnobservedTaskException` 演化）需外部核实。
