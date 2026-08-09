# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论资源管理语句的增强：`Using` 与 `SyncLock`。我们同时把上一场 `Try` 增强（proposal-try-enhancements）的讨论结果带到了桌上——两建议共享同一片"块尾处理"实现面，且 `Using` 建议里的 `Finally Await DisposeAsync()` 依赖 `Try` 建议的 Await-in-Catch/Finally。这意味着我们无法孤立地评判 `Using`，必须先厘清它与 `Try` 的职责边界。

## Agenda

* [Proposal: Using / SyncLock 增强](#proposal-using--synclock-增强)

## Proposal: Using / SyncLock 增强

_Related: [vblang #37 – Await in Catch and Finally](https://github.com/dotnet/vblang/issues/37)；vblang 2017.08.09 LDM（Await in Catch/Finally 决策）；ModVB：`proposal-try-enhancements.md`、`proposal-implicit-interface-implementation.md`_

### 场景与缺口

We started from three gaps, all real but with very different strengths.

第一，**资源获取的错误处理与异步释放**。今天 `Using` 块内无法捕获资源获取异常，也无法在释放路径上 `Await`；想做到就必须在外层再包一个 `Try`。这与主线 2017.08.09 的决策直接相关——那次会议把 Await-in-Catch/Finally 标记为"原则上通过，但优先级应由平台侧的 `IAsyncDisposable/Async Using` 驱动"（"needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"），并在 2018.02.28 就 #37 落到"Let's do it!"。`Try` 增强已把 Await 带入 `Catch`/`Finally`，但 `Using` 的**编译器生成释放**仍然无法被用户改写为异步或错误处理。这是本建议最强的一块：它是真实的、可演示的、与主线同向的缺口。

第二，**一次取多个资源**。`Using one, two, three = GetTriplet()` 的愿望今天只能靠嵌套 `Using` 表达。这块价值真实但频率中低——真实代码里"一次拿三个可释放资源"并不多见，且嵌套 `Using` 只是冗长，不是做不到。

第三，**结构识别 `Dispose` 与 `SyncLock` 灵活性**。`Using` 只认 `IDisposable.Dispose`，很多类型有自定义释放方法却用不上；`SyncLock` 只认 `Monitor.Enter/Exit`。这块动机最弱——它把两个方向截然不同的诉求捆在一起，且 `SyncLock` 部分在建议里几乎只有一句转述（"Basically the same stuff as Using"）。

### 候选方案

**PROPOSAL A — 整体包。** 按建议原文一次推进四件事：① `Using` 内 `Catch`/`Finally`；② `Using` 头元组解构；③ 按方法名识别 `Dispose`（甚至扩展方法）；④ `SyncLock` 同款增强 + `Monitor` 之外的其他同步方法。

**PROPOSAL B — 拆分推进。** 四件事各自独立论证：`Using` 的 `Catch`/`Finally` 文法并入 `Try` 增强的通用规则（"Catch/Finally on any block"），`Using` 只保留其特有交互（自动释放与显式 `Finally` 的关系）；解构头单独立项；按名识别 `Dispose` 与 `SyncLock` 扩展分别评估。

**PROPOSAL C — 只做同步释放路径。** 采纳①+②，拒绝③和④。理由：`IDisposable` 是契约不是名字；`SyncLock` 的保证依赖 `Monitor` 的 CLR 语义。

**PROPOSAL D — 语法选项（解构头）。**
- **D1（括号）**：`Using (one, two, three) = GetTriplet()` —— 与 `Dim (a, b) = pair`、`For Each (a, b) In seq` 一致。
- **D2（无括号，原文形式）**：`Using one, two, three = GetTriplet()` —— 建议原文写法。
- **D3（不做）**：维持嵌套 `Using`。

**PROPOSAL E — 释放能力判定（按名 `Dispose`）。**
- **E1**：结构识别**实例** `Dispose` 方法（对齐 `For Each` 认 `GetEnumerator`）。
- **E2**：E1 + **扩展方法** `Dispose`。
- **E3**：不支持，坚持 `IDisposable` 接口。

**PROPOSAL F — `SyncLock` 的其他同步方法。**
- **F1**：模式识别 acquire/release 对（`Enter/Exit`、`Wait/Release`、`EnterReadLock/ExitReadLock`…）。
- **F2**：特性标注（如 `<SyncLockEntry("Acquire")>` / 成对标注）。
- **F3**：内置枚举若干同步原语（`Monitor` 之外加 `SemaphoreSlim`、`SpinLock`…）。
- **F4**：不做。`SyncLock` 保持 `Monitor`-only。

### 权衡：Q&A

- **A vs B：为什么拒绝"整体包"？** 四个子特性相关性弱：①解决"错误处理/异步"，②解决"多资源样板"，③解决"释放入口识别"，④是另一片完全不同的同步语义。捆绑推进违反我们"不引入第二种做事方式"的底线，且任一子特性单独被否都会拖累其余。We are confident this must be split.

- **`Using` 的 `Catch`/`Finally` 是不是 `Try` 增强"任意块 Catch/Finally"的特例？** 语法上是。`Try` 增强已提出 `Catch`/`Finally` 可附加到任意块（`For Each ... Finally ... Next`），若该规则落地，`Using` 获得 `Catch`/`Finally` 是免费的。但 `Using` 有一个通用规则不知道的交互：**编译器生成的自动释放**。显式 `Finally` 与隐藏 `Finally` 会撞在一起。所以：**文法划给通用规则，`Using` 特有语义在本建议内定**。这不是两套机制，而是同一机制的块特例。

- **显式 `Finally` 中的 `Await reader.DisposeAsync()` 会不会双重释放？** 会——`File.OpenText` 返回 `StreamReader`，它同时实现 `IDisposable` 与 `IAsyncDisposable`；若编译器仍生成 `If reader IsNot Nothing Then reader.Dispose()`，则 `Dispose()` 与 `DisposeAsync()` 都被调用，这正是 .NET 文档明令避免的"同步+异步双释放"。我们权衡了三条规则：
  - **R1（自动释放保留）**：显式 `Finally` 只是附加清理，用户在 `Finally` 里手动释放触发警告。缺点：建议原文的招牌示例会撞上自己的警告；双释放依旧发生（只是被警告）。
  - **R2（显式 `Finally` 抑制自动释放）**：任何带显式 `Finally` 的 `Using`，编译器不再生成自动释放；若资源实现 `IDisposable` 而 `Finally` 里未释放，给警告。缺点：`Using` 的"自动释放保证"被削弱，用户忘了写就静默泄漏（警告缓解）。优点：彻底消灭双重释放；支持只实现 `IAsyncDisposable`（不实现 `IDisposable`）的类型——这类类型编译器今天根本无法自动释放，只能靠 `Finally`。
  - **R3（自动改用 `DisposeAsync`）**：类型有 `DisposeAsync` 且上下文可 `Await` 时，编译器自动 `Await reader.DisposeAsync()`。这就是 `Await Using` 的雏形，是主线方向（见下）。
  - **结论：R2 作为过渡，R3 作为主线方向。** 两者不冲突：R3 落地后，R2 的手写场景大部分自然消失。`Not all of us are happy with` R2 对自动释放保证的削弱——这是把"编译器负责"让渡给"用户负责"的退步，但警告把它约束在可审计范围内。

- **解构头要不要括号？** 要。VB 的解构先例全部带括号（`Dim (a, b) = pair`、`For Each (a, b) In seq`）。`Using one, two, three = GetTriplet()` 读起来像"三个赋值共用一个源"，且与既有多资源形式 `Using a = e1, b = e2`（每项独立 `=`）在视觉上难以区分。括号形式无歧义、与先例一致。**We `Suspect` 原文无括号形式是笔误或未深思。**

- **解构元素必须都可释放吗？释放顺序？** 必须。`Using` 的意义就是释放；元素类型非 `IDisposable`（也不满足按名规则）⇒ 编译错误。释放顺序 LIFO，与多资源 `Using a, b` 的嵌套 `Try/Finally` 一致。异常交互：第一个 `Dispose` 抛异常时，其余元素仍应被释放（嵌套保护），与既有多资源行为相同。元素为 `Nothing` 时跳过。

- **按名识别 `Dispose`：E1、E2、还是 E3？** 我们把它拆成两问。
  - **实例方法（E1）**：有 VB 血统。`For Each` 不要求 `IEnumerable`，任何带公开 `GetEnumerator` 方法的类型都能迭代——这是从 VB6/VBA 延续的结构性识别先例。对 `Using` 做同样的事，语法上说得通。风险：`Dispose` 名字太普通，任意含 `Dispose` 方法的类型都会被 `Using` 接受，而 `IDisposable` 的契约语义（幂等、`Dispose` 后 `ObjectDisposedException`、`SuppressFinalize`）是名字匹配不携带的。
  - **扩展方法（E2）**：明确排除。扩展方法解析依赖 `Imports`，编译器无法可靠发现；且扩展 `Dispose` 常常是 transform 而非 release（比如把数据刷到别处），语义更不可靠。原文的 "maybe even an extension method?" 我们回答 **No**。
  - **E3 作为默认底线**：接口路径优先。两条路径并存（接口 + 名字）本身就是原则 #3 的张力；我们接受 E1 但要求它**与隐式接口实现建议共享机制**——2014 年主线在 #37 隐式接口实现里已经为"按名匹配接口成员"立过教训：原则是"给基类加接口实现不应是破坏性变更"（"adding interface-implementation to a base case should not be a breaking change"），按名匹配会踩同一条线（基类新增 `Dispose` 后，派生类的 `Using` 行为改变）。
  - **结论：E1 值得 Consider，不在这轮落地。** 等隐式接口实现的机制与警告策略定稿后，作为它的下游特性一起设计。

- **`SyncLock` 的 "same stuff as `Using`" 具体指什么？** 我们不懂，而且**危险的成分居多**。解构在 `SyncLock` 里无意义——锁的是目标整体，不是它的部分。显式 `Catch`/`Finally` 是危险的：`SyncLock` 已经在隐藏 `Finally` 里调用 `Monitor.Exit`，用户再写 `Finally Monitor.Exit(lockObject)` 会在第二次 `Exit` 抛 `SynchronizationLockException`。`SyncLock` 的释放保证是编译器给的，不需要用户接管——这与 `Using` 的情况**相反**。**结论：`SyncLock` 不采用 `Using` 的 `Catch`/`Finally`/解构。** We `Suspect` "Basically the same stuff as Using" 是把两个块做过度的类比。

- **`SyncLock` 支持其他同步方法：F1、F2、F3、F4？** `Monitor` 的特殊保证并不通用：它可重入、线程关联、编译器保证 `Exit` 在任何路径（含异常）执行。`SemaphoreSlim` 可跨线程、可计数；`SpinLock` 用 `ByRef lockTaken`、不可重入；`ReaderWriterLockSlim` 有两个释放方法（读/写）。F1 模式识别不可靠——`Enter`/`Exit` 名字太普通。F2 特性标注是干净的机制，但引入新属性与编译期约定，成本高、无主线先例。F3 范围太窄，且每种原语的 acquire/release 形态差异大。更深一层：真实需求很可能是**异步锁**（`Await semaphore.WaitAsync()`），而 `SyncLock` 的同步 `Enter` 模型表达不了异步获取——那是另一套特性（见 agile-async 组），不该塞进 `SyncLock`。**结论：F4（Reject）；异步锁需求 Table，另立特性。**

- **与主线 `Async Using` 的关系？** 主线 2017.08.09 已把 Await-in-Catch/Finally 的优先级绑定到 `IAsyncDisposable/Async Using`，即主线押注的是**编译器自动异步释放**（`await using` 的 VB 形态），而本建议用手写 `Finally Await DisposeAsync()`。方向**偏离**。按我们"默认跟随 C#，除非有充分理由"的默认立场，`Await Using` 才是与 C# `IAsyncDisposable` 对齐的正路；手写 `Finally` 只能作为过渡，不能作为终点。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Using` 头的新形式 `Using (one, two, three) = expr` 是全新文法分支：括号目标表 + 单一 `=`。它与既有 `Using a, b`（每项独立声明、各自带 `As` 或 `=`）可明确区分——括号出现即解构。原文无括号形式 `Using one, two, three = GetTriplet()` 需要"多个裸标识符 + 单一 `=` ⇒ 解构"的上下文判定，虽然现文法下它是错误（不会破坏既有代码），但我们拒绝引入这套不一致的判定。`Catch`/`Finally` 后缀复用 `Try` 块的文法，终止符仍是 `End Using`；`Catch`/`Finally` 子句后跟随 `End Using` 即可，无歧义。

#### 2. 角案例与边界语义

**求值位置（Catch 是否覆盖资源获取）。** 若 `Catch ex As IOException` 要捕获 `File.OpenText` 的失败（建议的动机正是"读取配置失败"），资源获取表达式必须**移入** `Try`。今天的 `Using` 求值在隐式 `Try` 之外。这带来一个实现要点：`reader` 必须预置为 `Nothing` 再在 `Try` 内赋值，否则 `Catch`/`Finally` 引用未赋值变量——编译器需生成 `Dim reader = Nothing; Try: reader = expr; …`。Catch 中引用 `reader` 时若求值失败，是未赋值状态，需 definite-assignment 诊断。

**双重释放 / 未释放。** R2 下显式 `Finally` 抑制自动释放；资源实现 `IDisposable` 而 `Finally` 未释放 ⇒ 警告。建议原文招牌示例在 R2 下干净编译：`Finally` 里 `Await DisposeAsync()` 即视为已释放，无警告、无双释放。

**解构的边界。** 元素为 `Nothing` ⇒ 跳过释放。元素非可释放类型 ⇒ 编译错误（不是警告——静默不释放违背 `Using` 初衷）。解构源是元组时走内建 `Deconstruct`；是自定义类型时走其 `Deconstruct` 方法（与 `Dim (a, b) =`、`For Each (a, b) In` 共享实现）。`Using (a As X, b As Y) = pair` 显式类型标注应允许（对齐 `Dim` 解构）。

**`Await` 与 `SyncLock`。** `Await` 在 `SyncLock` 体内本身就与 `Monitor` 的线程关联冲突；这是为什么 `SyncLock` 扩展的动机（异步锁）必须另立特性，而不是给 `SyncLock` 加方法。

#### 3. 作用域与绑定

解构变量作用域为整个 `Using` 块（含 `Catch`/`Finally`）。语义模型里，`Using (log, trace) = OpenLogAndTrace()` 的 `log`、`trace` 绑定为普通局部变量（元组元素按值拷贝），`GetTypeInfo` 返回各自的元素类型；IDE 补全在块内显示二者。Catch 中引用 `reader`：若求值失败未赋值，报 definite-assignment 错误。按名 `Dispose` 的绑定：`Using` 头绑定到实例 `Dispose` 方法符号，与接口路径的差异应体现在警告与文档而非绑定本身。

#### 4. 与既有特性的交互

- **多资源 `Using`**：现有嵌套 `Try/Finally` 模型直接扩展为"解构元素也是资源"，LIFO 语义不变。
- **`Try` 增强**：`Catch`/`Finally` 文法共享；`Try` 头资源声明与 `Using` 头声明职责重叠——若 `Try` 头已能声明资源，`Using` 剩下的独特价值只有"自动释放"（R2 又把它削弱了）。**这是本建议最大的结构性问题**：`Using` 的存续理由需要重新论证，或明确 `Using` = 声明式自动释放、`Try` = 显式控制清理，二者分工。
- **隐式接口实现**：按名 `Dispose` 与按名匹配接口成员共享机制（2014 #37 教训）。
- **可空性流分析**：`Try` 增强示例用 `resource2?.Dispose()` 做清理；`Using` 的自动释放生成 `If r IsNot Nothing Then r.Dispose()`，与 `?.` 语义一致。
- **Await in `Finally`**：依赖 `Try` 增强 / 主线 #37；VB 特有的"Catch 内 `GoTo` 到 `Try` 内"跳转与 Await 状态机不能共存（主线 2018.02.28 已把"禁止 Await 与跳转共存"作为放行前提）。
- **ByRef**：解构变量是普通变量，可作 `ByRef` 实参；但作为解构来源的元组元素按值拷贝，改写不会回写元组。

#### 5. Breaking change 与兼容性

语法全部新增，现编译代码零变化：`Using (…) = expr` 括号形式、`Catch`/`Finally` 后缀、按名释放都是新文法分支；`Using a, b`（多 `=`）不受影响。真正的风险是**新代码静默错误**：按名 `Dispose` 释放了语义不符的类型；R2 下用户忘了在 `Finally` 写释放。两者都靠警告对冲，并配 `langversion` 门控。唯一微妙处是"求值位置移入 `Try`"——它改变异常可见位置，但没有既有代码在 `Using` 里声明 `Catch`，所以无重编译变化。

#### 6. Option Strict / 编译选项分叉

严格模式：编译期确定释放路径（接口或名字）。宽松模式：`Using r = obj` 中 `obj` 是 `Object` 时，编译期无法确定释放能力——运行期 `TypeOf r Is IDisposable` 检查？还是不释放？必须一条规则，两路径一致。我们倾向：宽松模式下 Object 资源**运行期检查**，不可释放则静默跳过（与 `?.` 的空安全精神一致）；但这会在宽松路径引入运行期行为差异，需要写进 spec 并接受 `Suspect` 待原型验证。

#### 7. IDE / IntelliSense

解构变量在 `Catch`/`Finally` 的可见性与补全；按名 `Dispose` 的"此类型将被 `Using` 释放"提示（区别于接口路径）；R2 的"未释放"与"双重释放"警告的 squiggle 与快速修复（"在 `Finally` 添加 `Dispose`"）；Catch 中未赋值引用的诊断。这些都必须在原型中验证——不做进规范等于没设计。

#### 8. 数据 / 普遍性

错误处理 + 异步释放是真实高频（配置读取、连接获取失败）——这是本建议最强的普遍性证据，但它与 `Try` 增强重叠。多资源解构中低频（"一次拿多个可释放资源"在业务代码里不常见）。按名 `Dispose` 需要数据：真实世界里"含 `Dispose` 方法但未实现 `IDisposable`"的类型占比是多少？我们手上没有。`SyncLock` 其他同步方法：VB 业务用户群对低级同步原语的需求低频。**数据不足，子特性必须独立评估。**

#### 9. 更简替代

- `Using` + `Catch`/`Finally` ⇒ `Try` 增强的 `Try` 头资源声明 + 通用 `Catch`/`Finally`（已覆盖大部分）。
- 异步释放 ⇒ `Await Using`（主线方向，与 C# `IAsyncDisposable` 对齐）。
- 多资源 ⇒ 现有嵌套 `Using`；或 C#8 风格 using declaration 的 VB 化。
- 按名 `Dispose` ⇒ analyzer 提示 + 手动扩展方法；或隐式接口实现。
- `SyncLock` 扩展 ⇒ 显式 `Monitor`/`SemaphoreSlim` 的 `Try/Finally`；或 analyzer。
- 结论：**每个子特性都有现成的或更简的替代**，这进一步说明"整体包"不该通过；只有解构头与 `Await Using` 值得语言级投入。

#### 10. 成本 / 优先级

解构头实现成本低（复用 `For Each`/`Dim` 解构实现）。`Catch`/`Finally` 成本中（与 `Try` 增强共享，主要新工作是求值位置移动 + 释放抑制规则）。按名 `Dispose` 成本中低（绑定改动），风险中高（契约语义）。`SyncLock` 扩展成本高（多种 acquire/release 形态）且价值低。优先级：**解构头 > `Await Using`（主线对齐）> 按名 `Dispose` > `SyncLock` 扩展。**

#### 11. 运行时 / CLR 硬约束

无新 IL。`Using`/`SyncLock` 都是语法糖，展开为 `Try/Finally` + `Dispose`/`Monitor` 调用；解构展开为 `ValueTuple`/`Deconstruct` 调用。Await-in-`Finally` 的状态机已由 `Try` 增强承担。PEVerify 无碍。唯一注意：R2 抑制自动释放后，释放路径从编译器保证转为用户代码——这是语言语义变化，不是 CLR 约束。

#### 12. 值不值得做

整体包：不值得。四个子特性相关性弱、多数有更简替代、数据不足、`SyncLock` 侧未设计。拆分后逐项：**解构头值得**（小而清晰、消样板、复用既有实现）；**`Catch`/`Finally` 随 `Try` 增强走**（`Using` 只是受益块）；**按名 `Dispose` 值得 Consider**（有 VB 结构性先例，但契约风险与隐式接口实现纠缠）；**`SyncLock` 扩展不值得**（无数据、语义混乱、异步锁另有出路）。

### VB 基因对照

- **消除常见样板（原则 #9）**：解构头正中靶心；把错误处理外包给 `Try` 增强也是消样板。
- **不引入"第二种做事方式"（原则 #3）**：本建议最大的扣分项。"接口路径 + 名字路径"双轨释放、`Using` 与 `Try` 职责重叠、`SyncLock` 与 `Monitor` 外的原语——全部在扩展表面积。拆分并收敛才能回到这条原则之内。
- **避免隐蔽的控制流/语义变化（原则 #7）**：`SyncLock` 显式 `Finally` 导致双重 `Exit`（运行时错误）是最典型的"隐蔽变化"；R2 的释放让渡、按名 `Dispose` 的静默释放同属此类，靠警告与文档对冲。
- **读起来像英语、对新手友好（原则 #5）**：`Using (log, trace) = OpenLogAndTrace()` 可读；`Using one, two, three = GetTriplet()` 不可读——这是 D1 胜出的理由。
- **永不破坏现有代码（原则 #1）**：语法全新增，零破坏；风险在"新代码静默错误"。
- **与主线关系（对照表 2.3）**：主线在资源管理上只有 Await-in-Catch/Finally（#37）与 `IAsyncDisposable/Async Using` 的决策，且方向是**编译器自动异步释放**。本建议的"手写 `Finally` 异步释放"是 Anthony 独立延伸且**偏离主线方向**；解构头、按名 `Dispose`、`SyncLock` 扩展主线未涉足（vblang proposals 目录无对应提案），属 Anthony 独立延伸。对 ModVB 沙盒可接受，但 `Await Using` 必须列为对齐项。
- **继承 VB6**：`For Each` 认 `GetEnumerator` 的结构性识别是 VB6/VBA 延续的基因，也是按名 `Dispose`（E1）唯一说得通的根据——它不来自 C#，也不来自 `IDisposable` 契约。

### RESOLUTION:

1. **整体包不采纳，拆分为四个独立子特性分别处置。**
2. **`Using` 内 `Catch`/`Finally`：文法并入 `Try` 增强的通用规则（"Catch/Finally on any block"），`Using` 只是受益块之一。** 本建议只规定 `Using` 特有语义：**显式 `Finally` 存在时抑制编译器自动释放（R2）**；资源实现 `IDisposable` 而 `Finally` 未释放 ⇒ 警告；只实现 `IAsyncDisposable` 的类型是合法目标，由用户在 `Finally` 释放。
3. **解构头：采纳，必须加括号** `Using (one, two, three) = GetTriplet()`，与 `Dim (a, b) =`、`For Each (a, b) In` 一致；支持 `(one As Type, two As Type)` 显式类型；元素必须可释放（否则编译错误）；释放 LIFO；元素为 `Nothing` 跳过。**原文无括号形式（`Using one, two, three = GetTriplet()`）不采纳。**
4. **按名识别 `Dispose`：Consider，不在这轮落地。** v1 形态支持结构识别**实例** `Dispose` 方法（E1），明确**排除扩展方法**（E2）；与隐式接口实现建议共享"按名匹配"机制并协调规则；对 `IDisposable` 契约语义的偏离给警告。
5. **`Await Using` 列为主线对齐项**（与 C# `await using`/`IAsyncDisposable` 对齐，呼应 2017.08.09 主线决策）；本建议的手写 `Finally` 仅为过渡。
6. **`SyncLock`：拒绝 "same stuff as `Using`"（解构无意义、显式 `Catch`/`Finally` 双重 `Exit`）。** 其他同步方法（F1/F2/F3）均 Reject；异步锁需求 Table，另立特性（与 agile-async 组对表）。

### Implication:

- 与 `Try` 增强团队对表：把"任意块 `Catch`/`Finally`"的文法文本放入 `Try` 增强；本建议只留 `Using` 特有的释放抑制规则与警告。
- 写最小原型：解构头 + 显式 `Finally` 抑制自动释放 + 求值位置移入 `Try`（预置 `Nothing`）；验证语义模型（解构变量、未赋值诊断）与 IDE 补全。
- 与隐式接口实现团队对表：确定按名 `Dispose` 与按名接口匹配是否共用同一机制；warning 文案与 `langversion` 门控。
- 起草 speclet：`Using (targetList) = expr` 文法、求值位置、释放顺序与异常交互、Option Strict Off 的 Object 资源规则。
- 评估主线 `Await Using` 的可行性，决定手写 `Finally` 是否长期保留。
- 未决问题移交 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：Catch 覆盖资源获取表达式求值后，`Using` 与 `Try` 在 definite-assignment 上的差异是否可接受（Catch 中引用未赋值 `reader`）。
- `OPEN QUESTIONS`：R2 的精确触发条件——任何显式 `Finally` 都抑制？还是仅当 `Finally` 释放了资源时抑制？当前取"任何 `Finally` 抑制 + 未释放警告"，但需要原型确认警告不误报。
- `OPEN QUESTIONS`：Option Strict Off 下 Object 资源的释放能力判定（运行期检查 vs 不释放）——`Suspect` 运行期检查，待验证。
- `OPEN QUESTIONS`：`Await Using` 与解构头的组合语义（`Await Using (a, b) = pair`）。
- `TODO`：量化"一次获取多个可释放资源"与"含 `Dispose` 方法但未实现 `IDisposable`"的真实占比，为数据/普遍性补证据。
- `Follow-up`：与 agile-async 组对表异步锁需求（`SemaphoreSlim.WaitAsync` 是否进入 `Await Using` 或独立特性）。

### 状态

- **LDM 状态：拆分推进**——解构头与 `Catch`/`Finally`（随 `Try` 增强）先行；按名 `Dispose` 待隐式接口实现；`SyncLock` 扩展关闭。
- **三态判定：整体 Table（拆分重组）**——子项：解构头 **Active**；`Catch`/`Finally` **Active（并入 `Try` 增强）**；按名 `Dispose` **Consider**；`SyncLock` 扩展 **Reject**（异步锁需求 Table）。

---

## 附录：特性评价

# 建议评价报告：proposal-using-synclock-enhancements.md

## 评价对象

- 建议：proposal-using-synclock-enhancements.md — `Using` / `SyncLock` 增强（块内 `Catch`/`Finally`、头解构、按名 `Dispose`、`SyncLock` 扩展）
- 来源：Anthony 原文第 3.10 章 "Using" 与 3.11 章 "SyncLock"（`..\AnthonyDesign_wordpress.txt` L1066–1095；3.9 "Try" 为同源兄弟章节）；无 vblang 主线对应提案（vblang proposals 目录无 Using/SyncLock 提案）
- 配方目标：`Using` 块内捕获资源获取异常、异步释放、一次取多资源、按名识别 `Dispose`；`SyncLock` 获得同款增强并支持其他同步方法

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。`Using` 侧（错误处理、异步释放、解构）目标明确、示例可操作；`SyncLock` 侧仅一句转述（"Basically the same stuff"），改进不可衡量、无示例 | 已检查 | 招牌示例（`Finally Await reader.DisposeAsync()`）含双重释放瑕疵，需 R2/R3 规则才能成立；无原型（状态行占位链接）封顶；未决问题 4 个关键点 ⇒ 效果证据封顶 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。解构头消样板（原则 #9）、按名 `Dispose` 继承 `For Each` 结构性先例（VB6 基因）；但四个弱相关子特性捆绑（违反原则 #3），`SyncLock` 扩展外来味 | 已检查 | "第二种做事方式"（接口/名字双轨、`Using`/`Try` 重叠）未自行识别；`SyncLock` 扩展与 Monitor 契约冲突未分析 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致；但 `SyncLock` 的 Detailed design 只有转述、边界完全含糊；解构头语法（无括号）与 `Dim`/`For Each` 先例不一致却未注明 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；无文法（BNF）；未决问题 4 个虽如实列出（1–3 健康区间之外） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（与 `Try` 增强重叠需协调）、水（解构头差异化亮点）、光（复用 `For Each` 解构与 `Try` 文法）；暗风险（双重释放、按名契约、`SyncLock` 双重 `Exit`）在 Drawbacks 部分列出但未充分权衡 | 已检查（预测待定） | 风=与 `Try` 增强重叠可能造成两套机制；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源=Anthony 原文 3.10/3.11 标注正确；但未标注与主线 `IAsyncDisposable/Async Using`（2017.08.09 决策）的偏离关系；未标注 `For Each` 结构性先例（VB 基因继承）；`SyncLock` 转述掩盖 Monitor 契约冲突 | 已检查 | 与 C# `await using` 的关系（主线对齐项）完全未提；无杂质，但来源谱系不完整 |

## 设计原则对照

- **与 VB 基因：部分一致**——解构头消样板（#9）、`For Each` 结构性先例（#5 可读性、VB6 继承）；**部分偏离**——捆绑四个弱相关子特性（#3）、`SyncLock` 显式 `Finally` 属隐蔽语义变化（#7）、按名 `Dispose` 的静默释放（#7）。
- **与主线关系：Anthony 独立延伸**——主线在资源管理上只有 Await-in-Catch/Finally（#37）与 `IAsyncDisposable/Async Using` 决策；本建议的异步释放走"手写 `Finally`"路线，**与主线"编译器自动异步释放"方向偏离**；解构头、按名 `Dispose`、`SyncLock` 扩展主线未涉足。与 `proposal-try-enhancements.md` 重叠；与 `proposal-implicit-interface-implementation.md` 在按名匹配上交互；与 agile-async 组（异步锁）交互。
- **破坏性变更：无**——语法全新增，零重编译变化；风险在"新代码静默错误"（按名释放错误类型、R2 忘写释放），靠警告 + `langversion` 门控对冲。求值位置移入 `Try` 是语义设计点而非既有行为变化。

## 总评

- **达成程度：部分达成**——`Using` 侧（`Catch`/`Finally`、解构头）概念与价值成立，但需拆分、需与 `Try` 增强协调、需补语义规则；`SyncLock` 侧未达成（无设计、与 Monitor 契约冲突）。
- **LDM 三态建议：整体 Table（拆分重组）**——子项：解构头 **Active**（小而清晰、复用既有实现）；`Catch`/`Finally` **Active（并入 `Try` 增强）**；按名 `Dispose` **Consider**（结构性先例成立，但契约风险与隐式接口实现纠缠，暂缓）；`SyncLock` 扩展 **Reject**（无数据、语义混乱、异步锁另有出路）。
- **主要问题**：① 捆绑四个弱相关子特性，违反原则 #3；② `SyncLock` 侧完全未设计，"same stuff"是过度类推；③ 双重释放语义未定义，招牌示例自身有瑕疵；④ 解构头原文语法与 `Dim`/`For Each` 先例不一致；⑤ 与 `Try` 增强重叠未处理，`Using` 存续理由需重新论证；⑥ 异步释放偏离主线 `Await Using` 方向。

## 返工建议

- **拆分提案**：解构头、`Catch`/`Finally`、按名 `Dispose`、`SyncLock` 扩展各自独立成文，不再捆绑。
- **补充章节**：文法（BNF）——`Using (targetList) = expr`、`Using ... Catch/Finally ... End Using`；Compatibility——求值位置移入 `Try`、R2 释放抑制、`langversion` 门控与警告策略；Option Strict 分叉（Object 资源运行期检查）。
- **补充语义**：显式 `Finally` 抑制自动释放的精确触发条件；Catch 覆盖求值的 definite-assignment；释放顺序与异常交互（LIFO + 嵌套保护）。
- **补充证据**：多资源解构、非 `IDisposable` 含 `Dispose` 类型占比的量化数据；最小原型（解构头 + R2 + 求值移入 `Try`）。
- **未决问题处理**：扩展方法（E2）= 明确排除；`SyncLock` 其他同步方法 = Reject，异步锁需求移交 agile-async 组；`Await Using` = 列为主线对齐项，手写 `Finally` 仅过渡；按名 `Dispose` = 待隐式接口实现定稿后重启。
- **设计探索**：`Await Using (a, b) = pair` 组合语义；`Using` 与 `Try` 头资源声明的分工宣言（声明式自动释放 vs 显式控制清理）。

---

## 附录：C# 生态与互操作考量

> 本附录对照 dotnet/csharplang 官方仓库的 C# 现实方向，评估 `Using`/`SyncLock` 增强提案的兼容性、冲突点与 VBScript.NET 适应建议。C# 原文逐字引用并标注来源文件；未能逐字核实的表述标注 **Suspect**。索引依据：`..\..\csharplang-index.md`（T2/T6/T8，M1/M5）。

### 一、相关 C# 现实方向

**1. C# 8「Enhanced using」：using declaration + pattern-based using**

C# 8 把 `using` 从"块内自动释放"扩展出两个能力：`using` 声明（`using var f = ...`，作用域到块尾、按声明逆序释放）与 **pattern-based using**。后者的原文定义：

> "The language will add the notion of a disposable pattern for `ref struct` types: that is a `ref struct` which has an accessible `Dispose` instance method. Types which fit the disposable pattern can participate in a `using` statement or declaration without being required to implement `IDisposable`."
→ `proposals\csharp-8.0\using.md`

形状约束原文（对本提案 E1/E2 直接相关）：

> "In order to fit the disposable pattern the `Dispose` method must be an accessible instance member, parameterless and have a `void` return type. It cannot be an extension method."
→ `proposals\csharp-8.0\using.md`

动机原文（消样板，对应本提案解构头）：
> "The `using` declaration removes much of the ceremony here and gets C# on par with other languages that include resource management blocks."
→ `proposals\csharp-8.0\using.md`

**注意**：C# 把该 pattern 限定在 `ref struct`（C# 8 的 ref struct 无法实现接口），普通类型仍走 `IDisposable`。C# **未**对任意类型开放"按名 Dispose"。

**2. C# 8 async streams：`await using` / `IAsyncDisposable`**

`IAsyncDisposable` 与 `await using` 于 C# 8 落地（`proposals\csharp-8.0\async-streams.md`；`proposals\csharp-8.0\async-using.md` 为占位说明）。这就是本提案 RESOLUTION 5 指向的 "Await Using" 主线在 C# 的对应物。对"双重释放"最权威的 C# 原文（R1/R2/R3 争论的直接外部参照）：

> "types may implement both `IDisposable` and `IAsyncDisposable`, and if they do, it's similarly acceptable to invoke `Dispose` and then `DisposeAsync` or vice versa, but only the first should be meaningful and subsequent invocations of either should be a nop. As such, if a type does implement both, consumers are encouraged to call once and only once the more relevant method based on the context, `Dispose` in synchronous contexts and `DisposeAsync` in asynchronous ones."
→ `proposals\csharp-8.0\async-streams.md`（IAsyncDisposable 章节）

这正面支持 R2/R3："只调用最相关的那一个"正是 R2（显式 `Finally` 抑制自动释放）与 R3（自动改 `DisposeAsync`）共享的语义内核。

**3. C# 13 lock 语句改进：`System.Threading.Lock` 特判**

背景是 .NET 9 引入新锁原语：
> ".NET 9 is introducing a new `System.Threading.Lock` type as a better alternative to existing monitor-based locking."
→ `proposals\csharp-13.0\lock-object.md`（Motivation）

关键语义：对 `System.Threading.Lock` 类型的 `lock (x)` 精确等价于 `using (x.EnterScope())`：
> "A `lock` statement of the form `lock (x) { ... }` … where `x` is an expression of type `System.Threading.Lock`, is precisely equivalent to: `using (x.EnterScope()) { ... }`"
→ `proposals\csharp-13.0\lock-object.md`（Detailed design）

**C# 拒绝了通用 lock pattern**，只特判一个具体类型：
> "Given that we are investigating allowing `ref struct`s into generics, and that the runtime doesn't plan on changing anything but `System.Threading.Lock` for C# 13/.NET 9, we think we should just special case the new type for now, and look at a broader pattern later when we have more use cases."
→ `meetings\2023\LDM-2023-12-04.md`

结论："We generally accept the changes for special casing how `System.Threading.Lock` interacts with the `lock` keyword, but we will not adopt the full pattern at this time." → `meetings\2023\LDM-2023-12-04.md`

lock 与 async 的交界（与本提案"异步锁另立特性"呼应）：
> "We could allow the new `lock` in `async` methods where `await` is not used inside the `lock`. Currently, since `lock` is lowered to `using` with a `ref struct` as the resource, this results in a compile-time error."
→ `proposals\csharp-13.0\lock-object.md`（Alternatives）

C# 对 `lock` 用户直觉的语义扩张：
> "Instead of "`lock` calls a specific API", we think the general user intuition is "`lock` enters a mutual exclusion zone and helps keep me safe from races", which is a much broader intuition."
→ `meetings\2023\LDM-2023-12-04.md`

**4. C# 对 VB 的总体姿态（unsafe-evolution）**

C# 15 unsafe-evolution 明确不为 VB 扩展低层能力：
> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
→ `proposals\unsafe-evolution.md`（VB 小节）

含义：C# 的投入集中在本提案主题之外的"低层安全"（span/ref/unsafe/union）。资源管理（using/lock）在 C# 属"已成熟 + 小幅演进"地带（using declaration、`await using`、`Lock` 特判），C# 现实方向**不排斥** VB 在此地带的差异化，但也**不替 VB 保留位置**——互操作全靠 VBScript.NET 去识别 C# 侧新元数据。

### 二、现实 vs 提案

| 本提案子特性 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| `Using` 内 `Catch`/`Finally`（并入 `Try` 增强） | C# 无"using 上挂 catch/finally"；异步释放用 `await using` 编译器生成 | **脱节（无直接对应，不冲突）** | C# 把同一需求用"编译器自动异步释放"解决，而非给 using 加 catch/finally；与会议"R3 为主线"一致 |
| 解构头 `Using (a, b, c) = ...` | C# 8 using 语句本身支持多 declarator（`using (var a=..., b=...)`），无"解构为多 using 局部"形式 | **脱节（VB 特有语法）** | 底层需求（一次多资源）C# 用既有多资源 using 满足；VB 解构头是差异化语法，不与 C# 冲突 |
| 按名 `Dispose`：E1 实例方法 | C# 8 pattern-based using：ref struct 的实例 `Dispose` 方法、**排除扩展方法** | **兼容（C# 先例）** | C# 已确立"实例方法、无参、void、非扩展方法"的形状约束，与会议 E1/E2 完全一致；但 C# 限定 ref struct，VB 泛化到任意类型需自带"基类新增 Dispose"防破坏护栏 |
| 按名 `Dispose`：E2 扩展方法 | C# 明确 "It cannot be an extension method." | **兼容（C# 直接否定）** | 与会议 E2=No 逐字同向 |
| `SyncLock` "same stuff as `Using`"（解构/`Catch`/`Finally`） | C# lock 只特判 `System.Threading.Lock`，`lock(x)` = `using(x.EnterScope())` | **冲突（语义分歧）+ 需桥接** | C# 13 表明 `lock` 语义在扩张（"enters a mutual exclusion zone"），与 VB SyncLock=Monitor-only 既有语义分歧加大；SyncLock 对 `Lock` 对象执行 Monitor 锁会成为 C# 特意加 warning 的 footgun |
| `SyncLock` 其他同步方法 F1/F2/F3 | C# 拒绝通用 lock pattern（LDM-2023-12-04），只特判一型 | **兼容（同向拒绝通用模式）** | C# 与会议都拒绝"通用模式识别"；C# 走"特判具体类型"，会议走"全 Reject + 异步锁另立"，方向一致但粒度不同 |
| `Await Using`（主线对齐项） | C# 8 `await using` + `IAsyncDisposable` 双释放指引 | **兼容（完全同向）** | 会议 RESOLUTION 5 与 C# async-streams 完全一致 |

### 三、对 VBScript.NET 的适应建议

1. **`SyncLock` 特判或告警 `System.Threading.Lock`（最高优先级桥接项）**：.NET 9+ 生态把 `Lock` 作为 Monitor 替代品推广。VBScript.NET 若 `SyncLock` 保持 Monitor-only，`SyncLock lockObj` 会静默走 Monitor（错误语义）；C# 已为 upcast 到 `object` 的 `Lock` 加 warning。建议：要么镜像 C# 13 特判 `Lock`（`SyncLock x` → 识别 `Lock.EnterScope()` pattern），要么至少在 `SyncLock` 目标是 `System.Threading.Lock` 时给 warning（"此对象是 `Lock`，`SyncLock` 会使用 Monitor 语义"）。即便 F1/F2/F3 保持 Reject，这一条特判值得考虑——它不引入"第二种做事方式"，只是让既有关键字认识生态标准新类型。
2. **`Await Using` 必须识别 `IAsyncDisposable` 元数据**：编译器在 `Using` 头检测 `IAsyncDisposable` 时驱动 `Await DisposeAsync()`；同时按 C# 指引避免双释放（调用一次、只调用最相关者）。R3 落地的先决条件是 VB 编译器认识该接口。
3. **按名 `Dispose`（E1）形状对齐 C# 8**：实例方法、无参、`void` 返回、非扩展方法——与 C# pattern 逐条对齐，降低跨语言语义分歧。泛化到任意类型时，用 `langversion` 门控 + warning 对冲"基类新增 `Dispose`"破坏面（会议已定）。
4. **default-safe / on-demand dynamic 定位**：资源管理属"默认安全"地带，与 C# 无冲突；但 .vbx 脚本解释模式下的释放判定（Option Strict Off 的 Object 资源）应保持会议拟定的"运行期 `TypeOf` 检查 + 静默跳过"，避免把脚本层动态引入编译产物的确定性。
5. **source-gen 桥 / 新元数据识别清单**：编译器需识别 `IAsyncDisposable`、`System.Threading.Lock`（及其 `EnterScope`）、`ref struct Scope`（`allows ref struct` 相关元数据，见索引 T2/M4）。这是"认识 C# 生态新类型"的最小集合，与 unsafe-evolution 的 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`（决策文件 M8 必须桥接点）同级。

### 四、对既有 RESOLUTION / 三态判定的影响

- RESOLUTION 2（R2 显式 `Finally` 抑制自动释放）：**不变**。C# async-streams 的双释放指引直接支持"只调用一次最相关方法"；R2 作为过渡、R3 作为主线与 C# 一致。
- RESOLUTION 4（按名 `Dispose` = Consider，E1 实例、排除 E2）：**获得 C# 先例支持，维持 Consider**。C# 8 pattern-based using 的形状约束（含排除扩展方法）与会议结论逐字同向；唯一新信息是 C# 限定 ref struct，VB 泛化版本的防破坏护栏需在原型中验证。
- RESOLUTION 6（`SyncLock` 拒绝 + 异步锁 Table）：**部分受影响**。F1/F2/F3 的 Reject 与 C# 拒绝通用 pattern 同向，维持。但"SyncLock 保持 Monitor-only"在 .NET 9+ 的 `System.Threading.Lock` 面前出现新 footgun，建议在三态中为 `SyncLock` 增加一个子项：**特判 `Lock`（Consider）或加 warning（Recommended）**，异步锁需求仍 Table（与 C# lock-in-async 的未决领域同向）。
- 三态判定整体：**维持"拆分推进"**；`SyncLock` 侧由"全 Reject"微调为"Monitor-only 语义保留 + `Lock` 特判/告警作为互操作桥"。

### 五、引用纪律

- 全部 C# 原文已逐字核对，来源如下：
  - "The language will add the notion of a disposable pattern for `ref struct` types…" → `proposals\csharp-8.0\using.md`
  - "In order to fit the disposable pattern the `Dispose` method must be an accessible instance member, parameterless and have a `void` return type. It cannot be an extension method." → `proposals\csharp-8.0\using.md`
  - "The `using` declaration removes much of the ceremony here and gets C# on par with other languages that include resource management blocks." → `proposals\csharp-8.0\using.md`
  - "types may implement both `IDisposable` and `IAsyncDisposable`…" → `proposals\csharp-8.0\async-streams.md`
  - ".NET 9 is introducing a new `System.Threading.Lock` type…" → `proposals\csharp-13.0\lock-object.md`
  - "…is precisely equivalent to: `using (x.EnterScope()) { ... }`" → `proposals\csharp-13.0\lock-object.md`
  - "Given that we are investigating allowing `ref struct`s into generics…" → `meetings\2023\LDM-2023-12-04.md`
  - "Instead of "`lock` calls a specific API"…" → `meetings\2023\LDM-2023-12-04.md`
  - "We do not need to add support to Visual Basic for *requires-unsafe* members…" → `proposals\unsafe-evolution.md`（VB 小节）
- **OPEN QUESTIONS**：
  - `System.Threading.Lock` 在 dotnet/runtime 侧的最终 API 形状（`EnterScope` 返回 `ref struct Scope`）以 csharplang 提案为准；实际 BCL 签名需在运行时镜像核实（本库为 csharplang 镜像，无 runtime 源码）。
  - LDM-2023-05-01 笔记写作 "new `System.Lock` type"（`meetings\2023\LDM-2023-05-01.md`），与提案的 `System.Threading.Lock` 命名不一致——**Suspect** 为笔记笔误，引用时以提案命名 `System.Threading.Lock` 为准。
  - 通用 lock pattern 的"未来（ref struct 入泛型后）"形态未定型；若落地，VBScript.NET 需重新评估 F1 类能力。
