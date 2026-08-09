# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这次我们回到异常处理面：Anthony 的 3.9 `Try` 一章一口气给了三件事——`Catch`/`Finally` 里能 `Await`、`Try` 头的块级资源声明、以及把 `Catch`/`Finally` 挂到任意块上。进会议室之前我们就有强烈预感：这三件事不是同一个特性，主线对其中第一件早有决议（vblang #37，2018 年就 "Let's do it!"），而第三件在语义上几乎没有站得住的地基。会议结果证实了这个预感——我们花了大半场在拆包，而不是背书。

## Agenda

* [Proposal: `Try` 增强（Catch/Finally 中 `Await`、块级声明、任意块附加）](#proposal-try-增强)

## Proposal: `Try` 增强

_Related: [vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #117 – Block-scoped Option Statements](https://github.com/dotnet/vblang/issues/117)；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；ModVB：`Using`/`SyncLock` 增强（3.10）、`Async Iterator`、局部变量声明、`Do` 循环头声明_

### 场景与缺口

We started from the observation that this proposal is three independent features wearing one cover page. 把它们拆开看，缺口各不相同：

1. **异步清理与日志**。今天 VB 的 `Catch` 和 `Finally` 里不能 `Await`。这意味着异常之后你想异步记录日志、或者用 `DisposeAsync` 异步释放资源，全都做不了。这不是 ModVB 的发明——主线 vblang #37 的原文就说过：

   > "In VB you can't Await in a Catch block. This makes it pretty much impossible to do some things after an exception that we assume user's would like to do."

   主线 2017.08.09 的决议是 "The feature is approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"，2018.02.28 更直接地落在 "Let's do it!"。**这一件是主线已经批准在案的事。**

2. **块级资源声明的可见性**。要在一个 `Try` 块里用资源，今天只能把声明放在 `Try` 之外，作用域于是泄漏到整个函数体：

   ```vb
   ' 今天：资源声明泄漏到函数体其余部分。
   Dim resource1 As ResourceHandle = GetResource()
   Try
       resource1.Open()
       ...
   Catch ex As Exception
       resource1?.Revert()
   End Try
   ```

   缺口是真实的（作用域泄漏、名字污染函数尾部），但它的解法与 `Using` 头、`Do` 头声明高度重叠——We 把它归入"声明头家族"的统一问题，而不是 `Try` 特有问题。

3. **"块结束时的统一处理"**。`Catch`/`Finally` 今天只能跟 `Try`，无法直接挂到 `For Each` 等循环上。建议用下面这个例子主张需要：

   ```vb
   For Each p In Process.GetProcesses() Where p.Name = "chrome.exe"
       ...
   Finally
       p.Kill()
   Next
   ```

   We stared at this example for a long time. **它不演示任何新能力。** `p.Kill()` 写在 `Next` 之后与写在 `Finally` 里逐字节等价——VB 的 `For Each` 循环变量在 `Next` 之后依然在作用域内（这是 VB 与 C# 的著名差异）。更糟的是，如果按"整块结束后清理"理解，这个例子只杀掉了循环的**最后一个** `p`，而不是所有匹配的 chrome 进程——它连自己想表达的清理语义都没写对。这一件的地基最虚。

### 候选方案

**PROPOSAL A — 全量照单。** 三件一起落：`Await` in `Catch`/`Finally` + `Try` 头声明 + `Catch`/`Finally` 任意块。按建议原文逐字实现。

**PROPOSAL B — 只做第一件，其余划走。** `Await` in `Catch`/`Finally` 独立成案，完全对齐主线 vblang #37 的决议史（approved-in-principle → "Let's do it!"），并落实主线 2018.02.28 定下的"窄化场景"：含 `Await` 的 `Try` 语句内禁用从 `Catch` 跳入 `Try` 的 `GoTo`（以及 `On Error` 跨区）。`Try` 头声明划给"声明头家族"统一工作项（`Using`/`Do` 头，ModVB 3.10 与 `proposal-do-enhancements`）；任意块 `Catch`/`Finally` 直接 Reject。

**PROPOSAL C — 最小切口。** 在第一件之外，`Finally` 对任意块只支持"清理"（不支持 `Catch` 错误处理），且要求给出一个 `Finally` 等价物**确实做不到**的真实场景才考虑；`Try` 头声明要求用 `Dim`/`Using` 头统一文法（逗号分隔、类型推断复用既有声明规则），否则不做。

### 权衡：Q&A

- **这是一份提案还是三份？** 建议的 `Summary` 把三件并排陈列，`Unresolved questions` 也是三件各留一条。We 一致认为这是"一份提案混杂多个独立特性，边界模糊"——评价标准的红旗之一。三件的受益者、成本、语义风险完全不同：第一件对异步业务代码是刚需，第二件是声明家族的重排，第三件是异常模型边界的扩展。**结论：拆包，不复评合订本。**
- **A vs B：任意块 `Catch`/`Finally` 真的只是"语法糖"吗？** 表面看 `For Each ... Finally ... Next` 省掉了 `Try` 包裹循环的一层缩进。但"省一层缩进"是"优化"，不是"特性"（主线一贯态度："Treat it as an optimization, not a feature"）。真正要回答的是异常边界：`Finally` 是"每迭代一次跑一次"还是"整个块结束后跑一次"？异常从循环体传播时它跑不跑？`Exit For`/`Continue For`/`GoTo` 与它如何交互？建议的 Drawbacks 自己承认"需要定义异常是从块内传播到 `Catch` 还是只捕获本块异常"，却一个字都没有定义。**没有异常边界定义的 `Finally` 是 `Finally` 的名字、`End` 的语义。**
- **B vs C：第一件的"窄化场景"牺牲了什么？** 主线 2018.02.28 的原话是：<br>"It is legal in VB (but not C#) to jump from inside a Catch to inside the Try. Await in catch in C# was implemented without support for this (obviously), and we aren't quite sure how to do it because nested Try/Catch create some interesting possibilities."<br>"If we drop the difficult scenarios and provide support and disallow Await and these jumps to happen together, this is doable."<br>VB 有 C# 没有的重试惯用法（`Catch ... When retryCount < 3` 里 `GoTo retry` 跳回 `Try` 内标签，2017.08.09 的原始示例）。在异步状态机里保留这种跳转，是把"跨 await 的异常状态"再叠加"跨 await 的控制转移"，成本陡增。**结论：在含 `Await` 的 `Try` 语句内禁用这类跳转，作为新增限制接受。** 关键论证：含 `Await` in `Catch`/`Finally` 的代码今天根本无法编译，所以这条限制**只作用于新代码**——这是纯增量，没有破坏性。C# 从不允许从 `Catch` 跳进 `Try`，所以这个限制恰好把异步重写区域拉回与 C# 一致，符合"默认跟随 C#，除非有充分理由"。
- **B vs C：`Try` 头声明为什么不能独立立项？** 因为它的语法形态与 `Dim` 逗号声明、`Using` 头、`Do` 头（ModVB `proposal-do-enhancements`）是同一个家族的变体。建议自己也在 Unresolved questions 里写着："`Try` 头声明与 `Using` 头声明、`Do` 头声明的语法统一（逗号分隔、类型推断规则）"。单独把 `Try` 头做出来，等于给声明家族再添一个不一致的分支；与 `Using`/`Do` 头统一后，`Try` 头只是多一个可挂声明头的块。**结论：并入统一工作项，`Consider`；统一工作项被拒则本件单独价值不足，`Table`。**
- **`Await` in `Finally` 与 `Async Using` 谁先谁后？** 主线 2017.08.09 明确把本特性的优先级绑定到 `IAsyncDisposable/Async Using`。反过来说，ModVB 的 `Using`/`SyncLock` 增强（3.10）里那个 `Using reader = File.OpenText(...) ... Finally Await reader.DisposeAsync() End Using` 示例，**没有本特性就编译不过**——两件是相互依赖的。We 的排序：`Await` in `Catch`/`Finally` 是基础件，`Async Using` 的异步释放路径降级为"编译器把 `DisposeAsync` 包成 `Finally` + `Await`"，这正好是本特性在资源场景的特例。先做基础件。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Await` in `Catch`/`Finally` 无文法歧义——`Await` 是既有表达式关键字，`Catch`/`Finally` 块本来就是语句块，只是今天禁止 `Await` 出现在其中（编译错误）。把它从"禁"改"允"是纯放松。

`Try` 头声明有真正的文法判定问题。今天的 VB 中 `Try` 必须独占一行（块头行终止，无隐式行继续），所以 `Try resource1 = GetResource()` 今天**必然报错**——不存在向后兼容冲突。但向前看，解析器需要一个规则来区分"头声明列表"与"正文首条语句"：

```vb
Try resource1 = GetResource(),          ' 头声明：逗号后隐式行继续。
    resource2 As ResourceHandle
    resource1.Open()                    ' 正文从新行开始。
```

判定规则候选：`Try` 所在物理行若还有内容，则按"声明列表"解析，复用 `Dim` 的 declarator 文法（`identifier [As Type] [= expr]`），以行终止符结束头；`Try` 单独成行则是经典形态。由于 `Try` 头今天非法，解析器不需要兼容任何旧写法。**角案例：** 有人今天误写 `Try x = f()`（非法）期望它成为正文——它从未编译过，不存在"期望"。

`Catch`/`Finally` 挂任意块：`Catch` 与 `Finally` 都是 VB 保留字，今天出现在 `For Each` 体内必然报错，文法上无歧义；但语义上需要回答"这是块的一部分还是异常构造"，见第 2 问。

#### 2. 角案例与边界语义

**`Await` in `Catch`/`Finally`：异常掩盖（CLR 强制的语义，不是选择）。** 一个 `Finally` 只能向调用方传播一个异常。`Await resource.DisposeAsync()` 若故障，它抛出的异常会**取代**原始异常向外出（原始异常丢失）。`Catch` 里 `Await logger.LogAsync(ex)` 故障同理——掩盖原始异常。这是 CLR 的单异常约束，C# 对 `await` in `catch`/`finally` 就是直接掩盖，We 认为 VB 应遵循（默认跟随 C#）。**值得记入文档，但不值得为它加 AggregateException 包装**——那会引入第二套传播语义。

**`Catch ... When` 过滤器里不能 `Await`。** 异常过滤器由运行时同步执行，VB 与 C# 都禁止在 `When` 条件中 `Await`。这不是本建议能解除的限制——是运行时约束。需要在 spec 里显式声明。

**重抛的保持。** `Catch ex As Exception` 内先 `Await logger.LogAsync(ex)` 再裸 `Throw`（重抛原始异常）：状态机必须把异常对象跨 await 保存，等待完成后重新抛出同一对象（保留栈与 HResult）。这是状态机重写里最容易写错的点，必须进 spec 并配原型验证。

**头声明的 definite assignment（渐进构造）。** 语义：头变量在进入 `Try` 前全部默认初始化（引用类型为 `Nothing`），然后按从左到右顺序求值初始化器；若第 N 个初始化器抛异常，前 N-1 个变量已赋值、其余保持默认值，异常进入 `Catch`。这样 `Catch` 里的 `?.` 清理永远安全：

```vb
Try first = GetA(), second As Handle
    first.Open()
    second = first.GetChildHandle()
Catch ex As Exception
    ' 若 GetA() 抛异常：first = Nothing，second = Nothing。
    ' 若 first.GetChildHandle()（在正文）抛异常：first 已赋值、second 未赋值。
    first?.Rollback()
    second?.Rollback()
End Try
```

**头声明初始化器里能不能 `Await`？** `Try r = Await GetResourceAsync(), ...` ——`Await` 在头中既不在 `Catch` 也不在 `Finally`，按普通 `Dim r = Await ...` 规则应合法。但头中 `Await` 使整个 `Try` 语句成为异步重写单元，与"含 `Await` 的 `Catch`/`Finally`"的状态机是同一套机制。`Probably` 合法，需与状态机 spec 一同定稿。

**任意块 `Finally` 的边界语义不可解。** 对 `For Each ... Finally ... Next`，"每次迭代跑一次"、"整块跑一次"、"异常时跑一次"是三种互斥语义；`Exit For`、`Continue For`、`GoTo`、嵌套 `Try` 都会改变边界。建议没有定义，We 也无法在不发明新语义的前提下替它定义——这正是 Reject 的理由。

#### 3. 作用域与绑定

头变量作用域 = 整个 `Try`/`Catch`/`Finally` 语句，与 `Using` 头的资源作用域、以及主线模式匹配纪要里 "LDM: The scope of introduced variables is the surrounding scope"（`If` 引入变量）"LDM thinks introduced variables scope to the While block"（`While` 引入变量）的家族判例一致。语义模型返回局部变量符号；`Catch ex` 的 `ex` 仍是 `Catch` 块局部的只读变量，与头变量不冲突。

**与同名外层变量的遮蔽。** `Try r = f()` 之后函数尾部又声明 `Dim r As ...`——VB 的块级声明遮蔽规则需明确。`Probably`：沿用 `Using` 头变量的规则（`Using` 资源名遮蔽外层名，块结束后外层名恢复），但要在 spec 里写明，以免与"同名变量在 `End Try` 后不可见"的直觉冲突。

#### 4. 与既有特性的交互

- **`GoTo` from `Catch` into `Try`（重试惯用法）**：VB 独有，C# 没有。含 `Await` in `Catch`/`Finally` 的 `Try` 语句内禁用该跳转（见 Q&A）。**重试与异步不可兼得**——这是主线 2018.02.28 "disallow Await and these jumps to happen together" 的原文决议，We 采纳。
- **`On Error`（VB6 遗产）**：2018.02.21 讨论过："In general the work is similar to C#. But VB specific things, GoTo and OnError may make it more difficult." 以及 "GoTo and OnError seem unlikely in "modern" code that would use Async. Perhaps we could narrow the scenarios to simplify implementation." `On Error GoTo`/`On Error Resume Next` 与异步状态机的错误路由互斥。**规则：`On Error` 不得跨越含 `Await` 的 `Try` 区域**；同样只作用于新代码，无破坏性。
- **`Using` / `Async Using`**：`Using ... Finally Await reader.DisposeAsync()`（3.10）依赖本特性；本特性的资源清理场景是 `Async Using` 的基础。两案共享同一个 `Finally`+`Await` 降级。
- **`Async Iterator`（ModVB）**：异步迭代器体内 `Try`/`Finally` + `Await` 需要同一套状态机机制；本特性是异步迭代器错误处理的地基。
- **`Throw`**：裸 `Throw` 重抛跨 await 的保持，见第 2 问。
- **Late binding / Option Strict Off**：`Await` 操作数需要 `GetAwaiter`；宽松模式下的晚期绑定 `Await` 是否合法（`Suspect`：现行 VB 是否允许晚期绑定 `Await`，需核实）——若现行规则允许，`Catch`/`Finally` 中应沿用同一规则，不因位置而收紧。

#### 5. Breaking change 与兼容性

三件**全部是新增语法，对既有合法代码零破坏**：

- `Await` in `Catch`/`Finally`：今天编译错误 → 改为合法。删掉一条编译错误，不改变任何已成功编译代码的行为。
- `Try` 头声明：今天 `Try x = f()` 非法 → 合法。
- 任意块 `Catch`/`Finally`：今天在 `For Each` 体内出现 `Catch`/`Finally` 保留字必然报错 → 合法。

唯一需要谨慎的是**新增限制**：禁用"含 `Await` 的 `Try` 内从 `Catch` 跳入 `Try`"，以及 `On Error` 跨区。因为含 `Await` in `Catch`/`Finally` 的代码今天不存在，这些限制触碰不到任何存量代码。We 反复核对了这一点——**这是本特性可以放心做加法而不动减法的关键。** 建议文档没有写这段兼容性分析，是重大缺失（评价标准红旗：无兼容性/breaking change 分析）。

**隐性语义风险（非 breaking，但需要文档化）**：异常掩盖（见第 2 问）、重抛保持、重试惯用法在异步 `Catch` 中的不可用。这些是"新代码接受的新语义"，不是"旧代码行为变化"。

#### 6. Option Strict / 编译选项分叉

头声明必须复用 `Dim` 的声明规则，两条路径行为一致：

- `Option Strict On` + `Option Infer On`：`Try resource1 = GetResource()` 推断类型，合法。
- `Option Strict On` + `Option Infer Off`：`resource1 = GetResource()` 无 `As` → 必须报错（与 `Dim resource1 = ...` 一致）。
- `Option Strict Off`：无 `As` 默认 `Object`（晚期绑定语义，与 `Dim` 一致）。

`Await` in `Catch`/`Finally` 与 Option Strict 无交互（`Await` 的类型要求与位置无关）。两条路径行为必须一致——这是评价清单的第 6 项，建议文档未覆盖。

#### 7. IDE / IntelliSense 影响

- 头声明：`Catch`/`Finally` 内的补全要显示头变量；重命名要跨 `Try`/`Catch`/`Finally` 同步；InfoTip 显示"在 `Try` 头声明"。无新难点，与 `Using` 头变量一致。
- `Await` in `Catch`/`Finally`：**状态机重写区域的调试体验**是主要风险。C# 的经验是 async 方法里 `try`/`finally` 的单步与异常定位本来就难；VB 增加 `Catch` 内 await 后，断点、异常辅助、调用栈的 async 帧都要在重写区域上工作。Edit-and-Continue 对 async 方法已有额外限制，新增区域会进一步收紧。这些必须在原型里验证，**不写进 spec 等于没设计**。

#### 8. 数据 / 普遍性

第一件的数据是实的：vblang #37 是被 champion 的 issue，2018.02.28 纪要原文说它让"some things after an exception that we assume user's would like to do" 完全不可做。自 2018 年以来平台现实又补了一块证据——`IAsyncDisposable`/`DisposeAsync` 已在 .NET Core 3.0 落地，异步释放成为主流 API 形状。异步日志（`await log.ErrorAsync(ex)`）与异步资源释放（`DisposeAsync`）是两大真实驱动。

第二件没有量化数据。"作用域泄漏"是真实的痛，但痛感是温和的（同名冲突、尾部污染），真实代码里多数人用 `Using` 或接受泄漏。`Suspect`：价值真实但强度不足以独立立项。

第三件没有数据，且唯一示例是坏的（见场景与缺口）。We 不认为"省一层缩进"构成普遍性。

#### 9. 更简替代

- **异步资源释放**：`Async Using`（`IAsyncDisposable`）是平台原生的答案，2017.08.09 主线已把本特性优先级绑定给它。但 `Async Using` 的 `DisposeAsync` 降级本身就是 `Finally`+`Await`——所以两者不是替代关系，是层级关系。
- **异步日志**：helper 方法（`TryLogAndSwallowAsync(ex)`）或 analyzer（警告"catch 里调用了异步日志方法却没 await"）可部分替代，但都是把语言应该承担的状态机责任踢回给用户。
- **头声明**：`Dim` 放 `Try` 前（作用域泄漏）或 `Using` 块（不同语义）——现状，样板可接受。
- **任意块清理**：尾随语句或 `Try` 包裹循环。建议自己的 Alternatives 就写了"块尾清理继续用 `Finally` + `Try` 包裹循环，代价是额外缩进一层"——**代价只有一层缩进**，这直接否定了本件独立成案的合理性。

#### 10. 复杂度 / 成本 / 优先级

- **`Await` in `Catch`/`Finally`**：成本在状态机重写。VB 的异步重写器已经能处理同步区域内的 `Try`/`Catch`/`Finally`；新增的是"异常状态跨 await 保存/恢复"。主线评估是"drop the difficult scenarios 后可做"（2018.02.28）。成本中高但有界；优先级高（异步已是主流）。
- **头声明**：解析器 + 绑定器成本低；但若不做统一，第三套声明形态的长期集成成本高。优先级取决于"声明头家族"工作项。
- **任意块 `Catch`/`Finally`**：语义成本高（每个块类型都要定义异常边界，与 `Exit`/`Continue`/`GoTo`/嵌套 `Try` 组合爆炸），价值低。不做。

#### 11. 运行时 / CLR 硬约束

- **异常掩盖**：`Finally`/`Catch` 只能传播一个异常，CLR 强制——遵循 C#。
- **过滤器同步**：`Catch ... When` 内无 `Await`，运行时强制。
- **无新 IL**：状态机复用既有 async 机制；`Try` 头声明、任意块构造不产生新 IL。PEVerify 无碍。
- **Edit-and-Continue**：async 区域扩充会进一步收紧 EnC 支持面。`Probably` 是兼容性代价，需在原型确认。

#### 12. 值不值得做

| 件 | 价值 | 成本 | 风险 | 判定 |
|----|------|------|------|------|
| `Await` in `Catch`/`Finally` | 高（异步清理/日志，主线已批准） | 中高（状态机，有界） | 低（纯增量 + 窄化限制只作用于新代码） | **值得，Active** |
| `Try` 头声明 | 中（作用域泄漏，温和） | 中（若不统一则高） | 低 | **并入统一工作项，Consider** |
| 任意块 `Catch`/`Finally` | 低（示例不演示新能力） | 高（每块异常边界） | 高（语义漂移、隐蔽控制流） | **Reject** |

### VB 基因对照

- **消除常见样板（原则 #9）**：第一件正中靶心——`Catch` 里 `Await logger.LogAsync(ex)` 消除了"用 `Task.Run` 包同步日志"或"把日志调用拆到外层 helper"的样板。第二件是样板消除（免 `Dim` 前移），第三件只省一层缩进。
- **默认跟随 C#（原则 #4）**：第一件完全对齐——C# 6 起 `await` in `catch`/`finally` 合法，我们跟随，并把异步重写区域的控制流限制拉回与 C# 一致。第二、三件 C# 没有对应物，属 Anthony 独立延伸。
- **不引入"第二种做事方式"（原则 #3）**：第三件违反最重——它给"错误处理"新增了 `Try` 之外的入口，给"块尾清理"新增了 `Finally` 之外的形态，这是"第二种做事方式"的教科书案例。第二件也引入第三种声明形态（`Dim`、`Using` 头、`Try` 头），靠并入声明家族统一来化解。
- **避免隐蔽的控制流/语义变化（原则 #7）**：第三件的"异常边界未定义"正是隐蔽控制流的形态——`Finally` 关键字暗示保证，但语义悬空。这是 `Return?` 被拒的同类理由。第一件的异常掩盖是**文档化的**语义，不隐蔽。
- **不为边缘场景加特性（原则 #6）**：第三件为"省一层缩进"这种边缘动机加一个改变异常模型的构造，违反。
- **读起来像英语、对新手友好（原则 #5）**：`Try r = GetResource()` 头声明读起来自然，且与 `Using r = ...`、`For Each r In ...` 同族——这是它唯一站得住的"VB 味儿"论点。
- **与主线关系（对照表 2.3）**：第一件**主线一致**——vblang #37 从 approved-in-principle 到 "Let's do it!"，ModVB 只是把主线已经批准的事落地。第二件是 Anthony 独立延伸但方向与声明头家族一致。第三件是 Anthony 独立延伸且**与主线纪律冲突**（主线从未把错误处理构造挂到非 `Try` 块上——`Suspect`：我们手头的主线纪要里找不到任何"`Catch`/`Finally` 附加任意块"的讨论，这与"`Try` 是异常处理的唯一入口"的既有形态冲突）。根本张力在此例中：主线"默认跟随 C#"，Anthony"故意不跟 C#/F#"——但对第一件两者恰好一致，因为 C# 早已实现。

### RESOLUTION:

1. **拆包**：`proposal-try-enhancements.md` 是三件独立特性的合订本，不复评。三件按下列结论分别处理。
2. **`Await` in `Catch`/`Finally` = Active**：采纳主线 vblang #37 决议（approved-in-principle，2018.02.28 "Let's do it!"）。落实"窄化场景"：含 `Await` 的 `Try` 语句内禁用从 `Catch` 跳入 `Try` 的 `GoTo`，`On Error` 不得跨该区域。该限制只作用于新代码，无破坏性。
3. **异常传播语义遵循 C# 与 CLR**：`Finally`/`Catch` 中 `Await` 故障时异常直接传播（掩盖原始异常）；`Catch ... When` 过滤器内不允许 `Await`（运行时同步约束）；裸 `Throw` 重抛需跨 await 保持原异常对象。
4. **`Try` 头声明 = Consider，并入"声明头家族"统一工作项**：与 `Using` 头（3.10）、`Do` 头（`proposal-do-enhancements`）、`Dim` 逗号声明统一文法；头变量进入块前默认初始化，初始化器抛异常时其余变量保持默认值（渐进构造清理安全）；Option Strict/Infer 分叉复用 `Dim` 规则。统一工作项不启动则本件 Table。
5. **任意块 `Catch`/`Finally` = Reject**：异常边界语义未定义且无法低成本定义；`For Each ... Finally ... Next` 示例是尾随语句的等价物、不演示新能力。`Catch` 附加非 `Try` 块（异常匹配与终止符规则）是本件最不可行的部分。若未来出现真实场景，仅重审 `Finally`（清理）一种形态，`Catch` 不进入。
6. **`Await` in `Finally` 是 `Async Using` 的基础件**：`Using` 3.10 的 `Finally Await reader.DisposeAsync()` 依赖本特性；两者共享 `Finally`+`Await` 降级，先做基础件。

### Implication:

- 将 `Await` in `Catch`/`Finally` 拆为独立提案，引用 vblang #37 决议史（2017.08.09 / 2018.02.21 / 2018.02.28 三段原文），并起草状态机重写 speclet。
- 起草 speclet 必须覆盖：GoTo/OnError 限制区域的精确定义（文法级）、异常跨 await 的保存与重抛、异常掩盖文档化、`When` 过滤器禁止 `Await`、头声明 definite assignment 与 Option Strict/Infer 分叉。
- 与 `Async Iterator`、`Using`/`SyncLock` 增强对表：确认三案共享同一状态机/降级路径，不造三套。
- 与"声明头家族"工作项（`Using`/`Do` 头 + 局部变量声明）对表：头声明统一文法，`Try` 头作为可挂声明头的块加入。
- 撰写最小原型：`Catch`/`Finally` 中 `Await` 的状态机重写，验证调试器单步、异常辅助、重抛保持。
- 补一份 Compatibility 分析：三件均为新增语法（零破坏）+ 新增限制仅作用于新代码，逐条列证；异常掩盖与重试不可兼得写入文档。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Try` 头声明初始化器中的 `Await` 是否合法（`Probably` 合法，随状态机 spec 定稿）。
- `OPEN QUESTIONS`：头变量与同名外层变量的遮蔽规则（`Probably` 沿用 `Using` 头变量规则，待 spec）。
- `OPEN QUESTIONS`：跨 `Await` 的裸 `Throw` 重抛在重入（`Catch` 内多次 await 后 `Throw`）时的精确 IL 形状。
- `OPEN QUESTIONS`：`Await` 在 `Catch`/`Finally` 对 `Async` 方法签名的影响——建议原文列为未决，但主线 2018.02.28 已给出答案方向（状态机重写），此处仅为确认，不再视为开放。
- `TODO`：量化"在 `Catch`/`Finally` 中需要异步清理/日志"的真实占比，为普遍性补证据。
- `TODO`：为任意块 `Finally` 找一个"尾随语句/`Try` 包裹确实做不到"的真实场景；找不到则维持 Reject。
- `Follow-up`：确认现行 VB 是否允许晚期绑定 `Await`；若允许，`Catch`/`Finally` 中沿用同一规则。

### 状态

- **LDM 状态：Active（仅 `Await` in `Catch`/`Finally`）**；`Try` 头声明随声明头家族工作项为 Consider；任意块 `Catch`/`Finally` 为 Reject（standalone）。
- **三态判定**：第一件 Active（价值真实、主线已批准、范围可窄化、零破坏）；第二件 Consider（并入统一工作项）；第三件 Reject（示例不成立、异常边界不可低成本定义）。

---

## 附录：特性评价

# 建议评价报告：proposal-try-enhancements.md

## 评价对象

- 建议：proposal-try-enhancements.md — `Try` 增强（`Catch`/`Finally` 中 `Await`、块级声明、任意块 `Catch`/`Finally`）
- 来源：Anthony 原文 3.9 `Try`（`..\AnthonyDesign_wordpress.txt` L1026–1062）；3.10 `Using`（L1066–1087）共享 `Catch`/`Finally` 语法；`Await` in `Catch`/`Finally` 与主线 vblang #37 同源
- 配方目标：异步清理/日志（`Await` in `Catch`/`Finally`）、块级资源声明（`Try` 头）、任意块统一处理（`Catch`/`Finally` 挂非 `Try` 块）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。第一件动机真实且与主线 #37 一致（异步日志/释放是实场景）；第二件目标明确但价值温和；第三件的示例（`For Each ... Finally p.Kill()`）不演示新能力——`p.Kill()` 写在 `Next` 后逐字节等价，且只杀最后一个 `p`。三件捆绑稀释"目标改进"。无原型/运行证据 | 已检查 | 无原型封顶 3；任意块示例语义错误；未量化用户诉求；`Await` 部分对主线决议史的依赖未引用 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。第一件是 C# 6 既有特性（`await` in `catch`/`finally`）的 VB 移植，且保留 VB 基因（限制区外的 `GoTo` 重试仍合法）；第二件继承 VB 头声明家族（`For Each`/`Using`/`Do`）；第三件是 Anthony 独立延伸且改变异常模型边界——与"`Try` 是唯一异常构造"的 VB 基因冲突 | 已检查 | 三件捆绑含"打包次要无关能力"；第三件对原则 #3（第二种做事方式）、#7（隐蔽控制流）双重违反，文档未识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全但 Detailed design 浅（无文法、无角案例、无 Option Strict 分叉、无 breaking-change 分析）；Drawbacks 自认"异常边界需定义"却未定义；未决问题 3 个且具体（1–3 健康区间），但"`Await` 对 `Async` 方法签名与错误传播的影响"被列为未决而主线 2018.02.28 已有答案方向 | 已检查 | 无文法/规范描述；无兼容性章节；三件未拆包使边界模糊；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。第一件对雷/水正向（解锁异步清理/日志、提速迭代、盘活 `IAsyncDisposable` 资产）；第三件对暗明显（异常边界未定义、语义漂移、隐蔽控制流风险）；第二件与 `Using`/`Do` 头职责重叠的权衡仅一句带过 | 已检查（预测待定） | 任意块 `Catch` 的异常传播边界是暗风险主源；与 `Async Using` 的优先级/层级关系未分析；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。标注了与 3.10 `Using` 的交叉（Drawbacks）；但第一件借鉴 C# 未声明（C# 6 起合法，主线 #37 已批准也仅写在 Drawbacks 之外的角落）；继承 VB 的 `GoTo`/`OnError` 基因未点明；`For Each ... Where` 示例依赖兄弟建议的查询语法（`proposal-for-each-enhancements`/`proposal-query-comprehensions`）未说明 | 已检查 | 借鉴 C# 与主线决议史未标注；成分（三件）影响混杂；无杂质但来源可核性差 |

## 设计原则对照

- **与 VB 基因：部分一致**。第一件对齐原则 #4（跟随 C#）、#9（消除样板），且窄化限制拉回与 C# 一致；第二件对齐头声明家族（#8 不与既有语法冲突、#5 读起来像英语），但引入第三种声明形态（#3 张力）；第三件偏离——#3（第二种错误处理/清理方式）、#6（为省缩进的边缘场景）、#7（异常边界未定义的隐蔽控制流）。
- **与主线关系：主线一致 / Anthony 独立延伸 / 与主线冲突并存**。第一件主线一致（vblang #37：approved-in-principle → "Let's do it!"）；第二件 Anthony 独立延伸，方向与声明头家族一致；第三件 Anthony 独立延伸且与主线"`Try` 为唯一异常构造"的纪律冲突。
- **破坏性变更：无**。三件语法在现行 VB 下均非法，全部为新增语法；GoTo/OnError 限制仅作用于含 `Await` in `Catch`/`Finally` 的新代码，不触碰存量代码。语义风险（异常掩盖、重试不可兼得）为新代码接受的新语义，须文档化。建议文档未做此分析。

## 总评

- **达成程度：部分达成**——仅第一件价值成立且主线已有共识；第二件价值真实但需统一工作项；第三件示例不成立。
- **LDM 三态建议**：`Await` in `Catch`/`Finally` = **Active**（拆独立提案，窄化场景落地）；`Try` 头声明 = **Consider**（并入声明头家族统一工作项，单独价值不足）；任意块 `Catch`/`Finally` = **Reject**。
- **主要问题**：① 三件捆绑、边界模糊（评价标准红旗）；② 任意块部分异常边界未定义且示例不演示新能力；③ 头声明与 `Using`/`Do` 头职责重叠未解决；④ 借鉴 C# 与主线 #37 决议史未标注；⑤ 无文法、无兼容性分析、无原型、状态行占位链接。

## 返工建议

- **拆包**：按三部分拆分提案。`Await` in `Catch`/`Finally` 独立成案并引用 vblang #37 决议史；头声明并入"声明头家族"统一工作项；任意块部分单列 Reject 记录（不删除，留档"为何不做"）。
- **补充章节**：文法（`Try` 头声明列表与 `Dim` 逗号声明对齐；`Try` 单独成行与头声明的行终止判定）；breaking-change/语义章节（GoTo/OnError 限制区、异常掩盖、裸 `Throw` 跨 await 保持、`When` 过滤器禁 `Await`、头变量 definite assignment、Option Strict/Infer 分叉、`langversion` 门控与警告策略）。
- **补充证据**：引用主线三段决议原文（2017.08.09 "approved in principle ... IAsyncDisposable/Async Using"；2018.02.21 "GoTo and OnError seem unlikely ... narrow the scenarios"；2018.02.28 "disallow Await and these jumps to happen together" 与 "Let's do it!"）；`Await` in `Catch`/`Finally` 最小状态机原型；真实代码占比数据。
- **未决问题处理**：异常传播遵循 C#（直接掩盖）；`When` 过滤器禁止 `Await`；头声明统一到 `Using`/`Do` 头工作项后由该工作项定稿；`For Each ... Finally` 需要一个"尾随语句确实做不到"的真实场景，否则维持 Reject。
- **设计探索**：与 `Async Iterator`、`Using`/`SyncLock` 增强共享状态机/降级路径的接口契约；调试器单步与 Edit-and-Continue 在重写区域内的验证。

---

## 附录：C# 生态与互操作考量

### 相关 C# 现实方向

**异常处理面的 C# 现状：C# 6 已全部落地，是本提案第一件的直接蓝本。** `Language-Version-History.md` 的 C# 6 特性清单里，`Exception filters` 与 `Await in catch/finally blocks` 并列：

> - [Exception filters](https://docs.microsoft.com/dotnet/csharp/language-reference/keywords/when)
> - Await in catch/finally blocks

→ `Language-Version-History.md`（C# 6 节）

异常过滤器（`catch (...) when (...)`）的运行时约束在 C# LDM 早期就写死——过滤器只能含表达式、同步求值，不能执行语句：

> "Exception filters can only contain expressions – if they need to execute statements, they need to do it in a helper function."

→ `meetings\2013\LDM-2013-10-07.md`

C# 侧对 VB 缺 `await` in `catch`/`finally` 早有感知。2015 年 C# LDM 主题纪要在「Async」节里原话：

> "Also, await in catch and finally probably didn't make it into VB 14. We should add those the next time around."

→ `meetings\2015\LDM-2015-01-21.md`

**资源声明的 C# 现实：C# 8 用 using declaration 把「隐式作用域资源」做成主流。** `proposals\csharp-8.0\using.md`（champion issue #114）定义了两件事：
- **using declaration**（`using var f = ...`）：作用域延伸到所在块尾、逆序释放、对 `goto` 等控制流无额外限制、隐式只读。
- **pattern-based using**：`ref struct` 只要有可访问的 `Dispose` 实例方法即可参与 `using`，不必实现 `IDisposable`。

原文逐字：

> "The lifetime of a `using` local will extend to the end of the scope in which it is declared. The `using` locals will then be disposed in the reverse order in which they are declared."
>
> "There are no restrictions around `goto`, or any other control flow construct in the face of a `using` declaration. Instead the code acts just as it would for the equivalent `using` statement"
>
> "A local declared in a `using` local declaration will be implicitly read-only."
>
> "The local type must be implicitly convertible to `IDisposable` or fulfill the `using` pattern."
>
> "The language will add the notion of a disposable pattern for `ref struct` types: that is a `ref struct` which has an accessible `Dispose` instance method."

→ `proposals\csharp-8.0\using.md`

异步一侧，C# 8 的 async using / `IAsyncDisposable` 同期定型（`proposals\csharp-8.0\async-using.md` 是占位符，champion issue #43；接口形状 `public interface IAsyncDisposable { ValueTask DisposeAsync(); }` 见 `meetings\2018\LDM-2018-10-03.md`）。LDM-2019-01-16 又为 `IAsyncDisposable` 与 `IDisposable` 定下「pattern → interface → extension」的检测顺序，且 `await foreach` 的 pattern-based disposal 用静态检查、不考虑扩展方法（→ `meetings\2019\LDM-2019-01-16.md`）。

**异常/异步边界的 C# 未来方向：async 状态机钩子。** LDM-2021-03-01 讨论了「async method exception filter」（issue #4485）——动机是 async 状态机先于用户代码解栈、丢失原始栈迹（Roslyn 自身为此在 async 上下文散布 try/catch 来上报堆转储）：

> "...the state machine can catch exceptions and unwind the exception stack before user-code can catch the exception, which leads to bug reports and heap dumps that are missing the actual stack trace where an exception is thrown."

→ `meetings\2021\LDM-2021-03-01.md`（结论：方向被喜欢、需细化，尚无决议）

**AOT/trimming 压力下的 VB 位置。** unsafe-evolution 对 VB 的明确表态（索引第四节已核实，此处照录）：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."

→ `proposals\unsafe-evolution.md`（「VB」节）

### 现实 vs 提案

| 件 | C# 对应现实 | 判定 | 理由 |
|---|---|---|---|
| `Await` in `Catch`/`Finally` | C# 6 已落地（`Await in catch/finally blocks`）；C# LDM 2015 点名 VB 该补 | **兼容** | VB 在追赶 C# 6 已批准的现实；窄化限制（禁 `Catch`→`Try` 跳转、`On Error` 跨区）恰好把异步重写区域拉回与 C# 一致的形态 |
| `Try` 头声明 | C# 无 try 头声明；最近对应物是 C# 8 using declaration（隐式作用域、块尾逆序释放、goto 无限制） | **需桥接** | 若 .vbx 做声明头家族，必须消费 C# 8 的两个元数据现实：`IAsyncDisposable` 与 pattern-based `Dispose`（ref struct）；VB 当前无 ref struct，跨语言消费需桥 |
| 任意块 `Catch`/`Finally` | C# 无任何先例；`try` 是唯一异常构造 | **脱节** | C# 现实方向没有把异常构造挂到任意块的动向；「省一层缩进」类语法糖被 LDM 纪律挡下（出处见下）。C# 现实**强化** Reject |

**VB `Catch ... When` vs C# `when`：同源、同一 CLR 机制，差异在过滤器之外的跳转。** 两者都走 CLR 的 filter clause——同步求值、只含表达式、不可 `Await`。这不是语言差异，是运行时硬约束，所以「过滤器内禁 `Await`」两侧一致、跨语言调用同样成立（呼应正文第 2 问）。真正的 VB 差异在过滤器**命中之后**：VB 允许从 `Catch` 内 `GoTo` 回 `Try` 内标签（vblang 2017.08.09 的 `Catch ex As Exception When retryCount < 3` + `GoTo retry` 原例，→ `..\..\vblang\meetings\2017\vbldm-notes-2017.08.09.md`），C# 从不允许——这正是主线 2018.02.28 "It is legal in VB (but not C#) to jump from inside a Catch to inside the Try" 所指。本提案把含 `Await` 的区域禁用该跳转，等于把异步侧拉回与 C# 完全一致，仅保留非异步区内的 VB 重试惯用法（按需动态）。

**关于正文引用的「主线一贯态度：Treat it as an optimization, not a feature」。** 附录补充出处核实：这句话的逐字原文出自 VB LDM（非 csharplang），上下文是 2018.03.21 关于 `CInt(Fix())` 优化的讨论：

> "Treat it as an optimization, not a feature to keep it narrow and get it done."

→ `..\..\vblang\meetings\2018\vbldm-notes-2018.03.21.md`

正文用它指代"主线纪律"方向不错，但严格溯源它是 vblang 而非 csharplang 的原话；csharplang 侧相近的立场是 LDM-2017-12-04（Span `foreach` 优化）的 "Definitely allow it as an optimization"（→ `meetings\2017\LDM-2017-12-04.md`，主题为 Span 遍历优化，非异常处理）。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：异步重写区域的 `GoTo`/`On Error` 限制是「默认安全」的一部分，.vbx 的 async 状态机必须实现同款限制（与 C# 对齐）；非异步区内的 `Catch ... When` + `GoTo retry` 重试惯用法按需保留（VB 特色）。与决策文件 M2（Any/晚期绑定）的"默认安全、按需动态"双模路线同构。
- **识别新元数据**：`System.IAsyncDisposable`（.NET Core 3.0+）、pattern-based `Dispose`（ref struct 实例方法）、以及 unsafe-evolution 的 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`（决策文件 M8 已列为必须桥接点）。.vbx 的 `Using`/`Async Using`（3.10）与未来声明头必须实现 C# 8 定下的「pattern → interface → extension」三级检测顺序，否则无法消费 C# 的 ref struct disposable 类型与 `WithCancellation`/`ConfigureAwait` 包装类型（→ `meetings\2019\LDM-2019-01-16.md`）。
- **source-gen 桥**：`Await` in `Catch`/`Finally` 的状态机必须与 C# 生成的 `AsyncTaskMethodBuilder`/`AsyncStateMachineAttribute` 形状互通，保证 C# 侧 `await` .vbx 方法、.vbx 侧 `await` C# 方法语义一致。异常掩盖（单异常传播）两侧遵循同一 CLR 约束，文档化即可，不引入第二套传播语义。
- **解释执行模式**：若 VBScript.NET 走 interpreted 路径，`Catch ... When` 必须实现为 CLR filter 语义（对原始异常对象同步求值、false 则继续向外匹配），不能降级成 try/catch 包装——否则与 C# 编译产物的互操作在过滤器求值时机上漂移。
- **AOT/trimming 张力**：`Catch` 内 `Await logger.LogAsync(ex)` 依赖反射式异常处理与分析工具，与 NativeAOT 的静态推理有摩擦；保留"脚本层允许动态、编译产物走类型化"的双模路线（决策文件 M5/M8）。

### 对既有 RESOLUTION/三态判定的影响

C# 生态现实**不推翻、反而加固**本提案的三态判定：

- **Active（`Await` in `Catch`/`Finally`）**：C# 6 早已实现、C# LDM 2015 就点名 VB 该补——方向正确。窄化限制使 VB 异步侧与 C# 对齐，是互操作友好而非倒退。
- **Consider（`Try` 头声明）**：C# 8 using declaration 证明"隐式作用域资源声明"是主流方向，对第二件的价值判断**小幅上行**；但仍须并入声明头家族统一文法，不改 Consider。
- **Reject（任意块 `Catch`/`Finally`）**：C# 无先例、无方向、无生态压力，**加固** Reject。

唯一新增风险：.vbx 若要消费 C# 的 ref struct pattern-based using 类型，需先具备 ref struct 支持（当前 VB 基本没有）——这是桥接工作项，不阻断本提案。

### OPEN QUESTIONS

- **async exception filter（C# issue #4485）若未来落地，是否改变"async 状态机先于用户代码解栈"的现状，进而影响 `Await in Catch` 的调试/遥测价值？** `Suspect`：csharplang 目前仅有 LDM-2021-03-01"方向被喜欢、需细化"的表态，无决议。
- **pattern-based using 是否应扩展出 VB 侧语义**（VB `Using` 是否也接受 ref struct `Dispose` 模式）？属声明头家族工作项，未决。
- **VB `Catch ... When` 过滤器在解释执行模式下是否逐字节对齐 CLR filter clause**？需 .vbx 运行时原型确认。
