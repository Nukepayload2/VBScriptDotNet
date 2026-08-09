# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本系列在过 Anthony 原文第 11 章异步编程增强——上周刚定下 Async Sub 的返回类型与默认异步类型配置（#51），本周依次讨论 Agile Async 与 Await 省略（#52）、强制 Await 与 `Call` 语句（#53）、`Await Each` 与 `Async Iterator`（#54）、`Async Event`（#55）。今天的这份建议是四份里最有张力的一份：它想给"不等待的异步调用"套上编译期约束，而这恰好撞上我们最保守的两条底线——破坏性变更与"默认跟随 C#"。会开得比预想的长，因为我们需要先回答一个更基本的问题：**"强制"到底该由编译器承担，还是由工具承担。**

## Agenda

* [Proposal: 强制 Await 与 Call 语句（Required Await and Call Statement）](#proposal-强制-await-与-call-语句)

## Proposal: 强制 Await 与 Call 语句（Required Await and Call Statement）

_Related: ModVB #51 `Async Sub`、#52 `Agile Async`、#54 `Await Each` / `Async Iterator`、#55 `Async Event`、inactive #89 `ConfigureAwait Options`；主线 [vblang #37 – Await in Catch/Finally](https://github.com/dotnet/vblang/issues/37)；Anthony 原文第 11 章 "Async Programming Enhancements"_

### 场景与缺口

异步方法的返回任务一旦被丢弃，异常会被静默吞掉，执行时序也难以追踪。这确实是真实的 bug 类，而且 Anthony 自己在第 18.15 节把话说得很重：

> "Void-returning async methods are my white whale. I must find more ways to limit their spread. Additionally when 'forgetting' a Task-returning async method I need to be sure folks can express/configure the various patterns to unobserved exceptions."

建议的形态是三条规则，逐字来自原文：

```vb
' 规则 1：把异步调用"转换"为任务对象并赋值——仍然允许（调用被显式接住）。
Let t As Task = MkDirAsync("...")

' 规则 2：直接调用、不等待也不赋值——不允许。
MkDirAsync() ' <- Not allowed.

' 规则 3：显式声明"不等待"——fire-and-forget 的唯一合法写法。
Call FireAndForgetAsync()
```

We see the intended improvement clearly: 让"故意不等待"有一个名字，让"意外不等待"变成错误。We like the direction of "把隐蔽行为变成显式行为"——这正是我们反复强调的"避免隐蔽语义变化"的正面用法。但紧接着 We 注意到的第一件事是：**这整件事在 mainline 已经发生过一次，而 mainline 的答案不是错误，也不是新语法。**

### 候选方案

**PROPOSAL A — 编译错误（原文立场）。** 未 Await、未赋值的 awaitable 返回调用一律编译错误；`Call` 是唯一放行通道。语法如原文三条规则所示。

**PROPOSAL B — 警告而非错误。** 未等待的异步调用产生编译警告（对齐 C# 的 `CS4014` 思路），配合 ModVB #10 的 `#Ignore Warning` 指令按需抑制；`Call` 作为显式放行，是否"抑制警告"待定。

**PROPOSAL C — 只声明 `Call` 语义、不动既有调用的合法性。** 仅把 `Call <async-call>` 在规范层面定义为"明确不等待"，`MkDirAsync()` 作为语句保持今天的状态（不加错误、不加警告）。零破坏，代码形状不变，语义变诚实。

**PROPOSAL D — 不改语言，交给官方分析器。** 维持语言现状，用 Roslyn 分析器提示未等待的异步调用；`Call` 被分析器识别为"已声明故意"。这是 mainline 处理此类"值得管但值得动语言吗"问题的默认路径。

### 权衡：Q&A

- **A vs B：严重级别该是错误还是警告？** 错误是纯粹的破坏性变更——今天合法编译的代码，重编译后变成错误。主线 2018 年 6 月把态度写得很死："We will almost never make breaking changes to Visual Basic (often resulting in _Rejected_ label)"（2018.06.13）。而警告也不是免费的：C# 的可空引用类型恰恰因为"For other than a greenfield project, this results in a wall of compiler warnings, the user then works through these to resolve"（2018.02.07）被推迟。We 没有任何数据说明 VB 现有代码库中有多少未等待的异步调用；把一墙新警告压到"数十万安静客户"身上，违背我们对自己用户群的认知——"hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now"（2018.05.30）。
- **C 的核心主张：`Call` 是"语义藏进既有关键字"还是"给既有惯用法命名"？** `Call FireAndForgetAsync()` 今天就能编译，做的正是"调用并丢弃返回值"这件事。所以本建议并没有引入新的代码形态——它只是给一种早已存在的写法盖上"故意的"印章。这是它最强的论据（零新语法、零破坏、读起来自然），也是它最弱的论据：`Call` 的历史含义是"以语句形式调用过程"（VB6 遗产），把它升级为"我发誓不等待"是给同一个关键字叠加一层读者看不见的语义——与 `Return?` 被拒所警示的"细微字符改变语义"处于同一风险谱系。
- **B vs D：警告进编译器，还是进分析器？** 编译器警告的优势是"无法被跳过"（除非 `#Ignore Warning`）；分析器的优势是零语言破坏、可配置严重级别、可渐进发布。对一个无法量化普遍性的场景，We 倾向于后者。C# 的 `CS4014` 本身是编译器警告，这提示"未等待"完全可以在编译器内实现——但把严重级别从警告升到错误，就跨过了我们"几乎不做破坏"的线。
- **A 最致命的一点：规则被 `_` 轻松绕过。** 规则 1 允许"转换为任务对象并赋值"，那 `Let _ As Task = MkDirAsync()` 呢？`_` 是合法标识符，这行与 `Let t As Task = ...` 形状完全相同，只是意图是丢弃。除非把 `_` 特殊对待（新规矩），否则规则 2 形同虚设；特殊对待 `_` 又与 C# 的 `_ = GetTask()` 丢弃惯用法分叉。We 追问之下，"强制"的完整性要么破产、要么引入新规则——两头都难看。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Call FireAndForgetAsync()` 在文法上没有歧义——`Call` 早已是语句关键字。歧义在语义层：**`Call` 的含义现在依赖被调用者的返回类型**。对同步 `Sub`，`Call` 保持"调用语句"；对 `Task` 返回调用，`Call` 变成"声明不等待"。同一个关键字、同一段源码，语义随目标方法签名漂移——这是我们最不喜欢的那种语境相关。需要文法进一步定义：`Call` 与 `Await` 能否叠加（`Call (Await MkDirAsync())` 是什么？"不等待地等待"自相矛盾）；`Call` 用在返回非 awaitable 的 `Function` 上是否仍合法（今天合法，规则下应保留）。

#### 2. 角案例与边界语义

**丢弃变量是规则的天然破口。**

```vb
' 规则 1 的漏网：与"接住"形状相同，意图却是丢弃。
Let _ As Task = MkDirAsync()    ' 若不禁止 _，规则 2 形同虚设。
```

**语句合法性被重载解析劫持。** 返回类型不同但参数不同的重载在 .NET 里完全合法：

```vb
Function QueueWork(urgent As Boolean) As Task
Function QueueWork(label As String) As Integer

QueueWork("init")   ' 只匹配 String 重载 → 返回 Integer → 作为语句合法（忽略返回值是 VB 传统）。
QueueWork(True)     ' 只匹配 Boolean 重载 → 返回 Task → 未等待 → 规则 2 报错。
```

同一语法形状（`QueueWork(...)`），合法与否完全取决于重载解析落到谁头上——而重载解析又可能被"让语句变合法"的压力影响（编译器会不会为了不报错而偏袒返回 `Integer` 的重载？）。这正是"隐蔽的绑定变化"，必须写进 spec，否则就是又一个 `Return?`。

**实参位置的异步调用。** `Task.WhenAll(MkDirAsync(), BackupAsync())` 中两个调用被 `WhenAll` 接住，规则不适用——但"实参即接住"的边界要明文写清，否则调用方会困惑为什么 `WhenAll` 里不用 `Call`。

**`ValueTask` 的"转换"其实不存在。** 规则的措辞是"Conversions to task objects still allowed"，但 .NET 没有 `ValueTask` 到 `Task` 的隐式转换；`Let t As Task = MkDirAsync("...")` 只在 `MkDirAsync` 静态返回 `Task`（或其子类）时成立。`Suspect`：原文的"conversion"措辞暗示"捕获任意 awaitable"，而 `ValueTask` 场景编译不过。规则应统一为"捕获到 awaitable 类型变量"，而不是"转换到 Task"。

**语句位置忽略返回值的传统。** VB 一直允许 `StringBuilder.Append(x)` 作为语句丢弃返回的 `StringBuilder`。新规则会制造一个不对称：任何返回值都可以丢，唯独 awaitable 不行。这个不对称有充分理由（awaitable 携带异常），但它是 VB "宽松调用模型"里的一个新区分，值得显式记录而不是默认成立。

#### 3. 作用域与绑定

`Call FireAndForgetAsync()` 与 `FireAndForgetAsync()` 绑定到同一个方法符号——"声明不等待"是语句的属性，不是新符号。语义模型里 `Call` 语句的 `GetSymbolInfo` 应与普通调用一致；需要新增的是语句级标记（如"该调用被显式放行"），供分析器与 IDE 读取。`Let t As Task = MkDirAsync()` 绑定到 `MkDirAsync` + 到 `Task` 的引用转换（若返回 `Task(Of T)` 是向上转型）。

#### 4. 与既有特性的交互

- **与 #52（Agile Async 的 Await 省略）目标相抵。** #52 让 `Await obj.MAsync().NAsync().P` 省略中间 Await，理由是"中间异步点读者不需要逐个看到"；#53 强制"每个异步调用都要被 Await"，理由是"异步边界必须可见"。两份建议在同一家族里朝相反方向拉——要么承认省略隐含"已被等待"（那 #53 的强制就只剩仪式意义），要么强制每个点可见（那 #52 的省略就失去意义）。We 必须二选一，不能两个都收。`Probably`：Await 省略是语法糖（语义上中间的 await 仍然发生），所以 #53 的"强制等待"在省略链上自动满足——但"可见性"诉求被打脸。
- **与 #51（Async Sub 返回类型）叠加后仪式感翻倍。** 若 `Async Sub Flush() As Task` 成为新常态，那么调用方每次 `Flush()` 都必须 `Await` 或 `Call`——把仪式感推给调用方。而传统 void `Async Sub` 本身就是 fire-and-forget（异常走同步上下文，比 `UnobservedTaskException` 更隐蔽），限制它的蔓延（Anthony 的"白鲸"）理应优先于强制 Await；次序反了。
- **与 #89（ConfigureAwait Options / 未观测异常）耦合。** `Call` 只是语法——它声明"不等待"，但被丢弃任务的异常怎么办？今天的答案是 .NET 的 `TaskScheduler.UnobservedTaskException`。Anthony 在 18.15 想要的是"表达/配置未观测异常的处理模式"，那是 #89 的地盘。若 #89 不做，`Call` 语义就只是"放弃返回值"，仍完整但悬空；若一起做，两者必须对表，否则 `Call` 的用户不知道异常落在哪。**本建议不能单独吞异常，也不能声称解决未观测异常问题——那是 #89 的承诺。**
- **`#Ignore Warning`（#10）**：若走 B 路径，需要 `#Ignore Warning` 成熟；若走 A 路径，错误能否被抑制也需要定义。
- **late binding**：`obj.RunAsync()` 在 Option Strict Off 下返回 `Object`，静态不可判定，规则天然不适用——见第 6 条。

#### 5. Breaking change 与兼容性

这是整场最尖锐的追问。建议的正文只字未提兼容性，而 A 方案是教科书级的破坏性变更：

- 今天编译通过、运行正确的代码（未等待异步调用）→ 重编译报错。无 `langversion` 门控，无迁移路径。
- "We will almost never make breaking changes to Visual Basic"（2018.06.13）——A 没有任何极端边界情形的借口可以钻。
- B 的警告墙体验已被 2018.02.07 判定为推迟理由。
- C、D 零破坏：既有代码一行不改、一个警告不加。

We 一致认为，没有迁移数据、没有 `langversion` 门控、没有严重级别设计的强制规则，不可能越过这条线。

#### 6. Option Strict / 编译选项分叉

规则只作用于"静态已知返回 awaitable"的调用。Option Strict On 下所有调用都是早期绑定，规则统一适用（Object 上的调用本来就是错误）；Option Strict Off 下晚期绑定调用返回 `Object`，规则无法判定——同一行源码 `obj.RunAsync()` 在两条路径下的合法性不同。这是明确的"两条路径行为不一致"分叉。更麻烦的是：宽松模式下被丢弃的异步异常恰恰最多（代码风格更自由），而规则在宽松模式下最不管用——**最需要它的地方它够不着**。规则必须写明"静态返回类型可判定为 awaitable 才适用"。

#### 7. IDE / IntelliSense

`Call` 的 InfoTip 要传达"不等待，异常未观测"吗？还是维持"调用语句"？`Call` 后补全什么？新错误/警告文案需要设计（如"此异步调用未被等待；用 `Await` 等待，或用 `Call` 显式声明不等待"）。语义模型的语句标记需要新 API 供 IDE 与快速修复（"加 Await" / "包上 Call"）使用。这些都不是阻塞项，但"不做进规范等于没设计"。

#### 8. 数据 / 普遍性

未等待异步调用导致的 bug 真实存在，但**发生率无数据**。C# 选择了警告而非错误，而且主线明确表示"We'll postpone this until we understand the uptake in C#"（2018.02.07，讨论可空引用类型时说的，措辞本身适用于任何"给现有代码制造警告墙"的特性）。对"数十万安静客户"，升级体验是头等考量。`Suspect`：这是真实的 DX 增益，但作为语言特性没有普遍性数据支撑；作为分析器规则则不需要这个数据就能渐进验证。

#### 9. 更简替代

- **分析器（D）**：mainline 默认路径。零破坏、严重级别可配、可渐进、可回滚。C# 生态证明"未等待调用提示"作为分析器/编译器警告完全够用。
- **仅 `Call` 语义（C）**：零破坏，代码形状不变，语义更诚实。价值先以"文档 + 分析器放行"形式兑现。
- **`Let _ = MkDirAsync()` 惯用法**：C# 的 `_ = GetTask()` 是抑制 `CS4014` 的标准做法；VB 不需要新语法就有等价的"显式接住再丢弃"，只是没有"声明故意"的语义层。

#### 10. 成本 / 优先级

错误/警告检查本身的实现成本低（仿照 C# 的未等待调用检测）。真正成本在：`Call` 语义重定义的规范工作、与 #89 的对表、Option Strict 分叉的处理、以及最贵的——破坏性变更的迁移成本（A）或警告墙的社区成本（B）。优先级上，We 认为应排在 #51、#52 之后；在 #89 未定之前，本建议的异常语义悬空，不应提前定案。

#### 11. 运行时 / CLR 硬约束

无。纯编译期检查，不改变 IL 生成，不触达 CLR 存储规则，PEVerify 无碍。`Call` 的"不等待"语义若被赋予"异常吞掉"的含义，需要的是 .NET 层的策略（`UnobservedTaskException`），那是库层/运行时的事，委托给 C#/BCL 讨论（2018.06.13："C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries"）。

#### 12. 值不值得做

价值（防丢异常）真实但可被分析器覆盖；成本（A 破坏性 / B 警告墙）高；风险（与 #52 目标相抵、与 #89 耦合、`Call` 语义混淆、`_` 破口）中高。逐条打分下来：

- A：价值 2，成本 5，风险 5 → **不值得**。
- B：价值 3，成本 3，风险 3 → **可再议，但缺数据**。
- C：价值 3，成本 1，风险 2 → **值得，但作为语言变更仍欠火候**。
- D：价值 4，成本 1，风险 1 → **值得，立刻做**。

### VB 基因对照

- **永不破坏（原则 #1）**：A、B 违反；C、D 符合。这是决定性的一条。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 对未等待调用给的是警告（`Probably`：`CS4014`，属 C# 编译器行为，非本仓库文件可核实），不是错误。A 把严重级别升到错误，没有提供偏离 C# 的"充分理由"——"VB 用户更容易漏 Await"恰恰意味着破坏面更大。
- **不引入"第二种做事方式"（原则 #3）**：`Call` 重定义处于边界——关键字已存在，是给既有关键字加第二种含义；Alternatives 里的新 `FireAndForget` 关键字则明确违反。mainline 的原话是："our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"（2018.06.13）。
- **避免隐蔽语义变化（原则 #7）**：`Call` 从"调用"变成"故意不等待"，语义藏进既有关键字、随被调用者返回类型漂移——与 `Return?` 被拒的同类理由。
- **消除常见样板（原则 #9）**：本建议是**反向**的——它给现有写法强制加仪式。与 #51 叠加后仪式感加倍。
- **冗长只在有用时是美德（原则 #10）**：这是本建议最亮的点——`Call` 的显式性在"故意不等待"场景确实有用，把 fire-and-forget 变成可搜索、可审查的写法。
- **与主线关系（对照表 2.3）**：主线在异步上的动作（#37 Await in Catch/Finally）是"Approved. Aligns with C# vNext feature"、"We'll leave the design work to C# LDM, and do exactly the same"（2014.02.17）——即异步特性**跟随 C#、委托 C# LDM**。本建议在 C# 只给警告的地方升格为强制，方向与主线"异步跟随 C#"的基调冲突；对"工具层提示"的部分则主线一致。属于 Anthony 独立延伸，且是向激进方向的延伸。

### RESOLUTION:

1. **拒绝 PROPOSAL A（编译错误）**。破坏性变更（违反原则 #1）、与 C# 警告路线背离且无充分理由（原则 #4）、`Call` 语义重载（原则 #7）、`_` 破口使强制不完整、与 #52 Await 省略目标相抵。按 mainline 的标号惯例，A 应标 **LDM Rejected**。
2. **PROPOSAL B（警告）暂不采纳，留作后续（Table）**。警告墙体验与 2018.02.07 的推迟先例冲突；仅当两个信号到位后重审：(a) 真实代码库中未等待异步调用的占比数据；(b) `#Ignore Warning`（#10）落地成熟。在此之前，宁可让分析器承担。
3. **PROPOSAL C（仅声明 `Call` 语义）在语言层不单独落地（Table）**。We like 它的零破坏与"显式不等待"的诚实性（原则 #10），但"语义藏进既有关键字、随返回类型漂移"与原则 #7 的张力、以及和 #52 的冲突，使它不能作为孤立的语言变更推进。它的价值先以文档 + 分析器形式兑现；若未来 #89 给出异常处理策略，`Call` 的规范含义再回来复评。
4. **PROPOSAL D（官方分析器）→ Active**。提供一个提示"异步调用未被等待"的官方分析器；`Call <async-call>` 被分析器识别为"已声明故意"（放行）；快速修复提供"添加 Await"与"包装为 Call"两种操作。零破坏、可配置严重级别、可渐进。这是 mainline 对"值得管但不动语言"问题的默认答案。
5. **本建议不吞异常、不承诺未观测异常策略**。`Call` 只声明"不等待"；被丢弃任务的异常走 .NET 既有机制（`TaskScheduler.UnobservedTaskException`）；表达/配置处理模式留给 #89。两建议不互相阻塞。
6. **Option Strict 分叉**：规则（无论走 B 还是 D）只作用于静态返回类型可判定为 awaitable 的调用；晚期绑定调用不受影响，两条路径的差异写进规范。
7. **`Let _ = MkDirAsync()` 继续合法**，不做特殊禁止——目标不是防绕行，而是"给故意不等待一个名字"；`_` 丢弃是与 C# 对齐的既有惯用法。

### Implication:

- 与 #89 团队对表：确认 `Call` 只声明"不等待"、异常策略归 #89；记录"Call 语义 vs ConfigureAwait 选项"的边界。
- 与 #52 团队对表：解决"Await 省略 vs 强制可见"的家族内部矛盾；至少写一份并存的语义说明，避免未来两份 spec 互相打架。
- 起草分析器规格（PROPOSAL D）：检测未等待的 awaitable 返回调用；识别 `Call`、`Let _ = ...`、实参接住等放行形态；快速修复设计；严重级别默认设为"建议（hidden/suggestion）"。
- 记录 2018.02.07 / 2018.06.13 的决策作为评审基准，防止未来有人把 A 抬回来而不先过数据关。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Call` 规范语义的最终载体——若未来要把"声明不等待"写进语言，`Call` 是否是正确的词？还是应等 #89 定稿后一并设计？（`Probably`：`Call` 的历史含义重叠使它是次优选择；但引入新关键字违反原则 #3。）
- `OPEN QUESTIONS`：若 B（警告）重审，严重级别与 `#Ignore Warning` 的交互规则；与 C# `CS4014` 的文案/行为对齐到什么程度。
- `OPEN QUESTIONS`：重载解析"为合法而偏袒非 awaitable 重载"是否会发生，需原型验证。
- `TODO`：量化真实代码库中未等待异步调用的占比（为 B/D 补证据）。
- `TODO`：与 #51 对表——`Async Sub ... As Task` 后，调用方仪式感的数据/体验评估。
- `Follow-up`：把 C 的"显式不等待"写成语言规范外的样式指南条目，先以文档形式沉淀。

### 状态

- **LDM 状态**：分析器路径（D）为 **LDM In Process**；语言变更（A 错误 / B 警告 / C 仅 `Call` 语义）为 **LDM Considering / Table**；A 为 **LDM Rejected**。
- **三态判定**：作为**语言特性**，Reject 强制部分、Table 其余；作为**工具特性**，Active（官方分析器）。

---

## 附录：特性评价

# 建议评价报告：proposal-require-await-call.md

## 评价对象

- 建议：proposal-require-await-call.md — 强制 Await 与 `Call` 语句
- 来源：Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1755–1762）；关联 18.15 "ConfigureAwait options/fire-and-forget exception handling"（L3042–3046）
- 配方目标：把"不等待的异步调用"变成显式行为——默认禁止，`Call` 明确放行；防止异步结果被意外丢弃

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（防丢异常）且示例可演示，但严重级别未定（错误 vs 警告），目标改进无法完整演示；A 效果只对强制路径成立，而该路径是破坏性变更 | 已检查 | 无原型；"Not allowed" 严重级别未定（最大设计点进了未决）；无发生率数据；规则被 `_` 丢弃绕过，效果完整性存疑 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。强制 Await 是外部严格异步纪律（C# 只给警告）升格而来，未做 VB 化；同一建议捆绑"强制检查 + `Call` 重定义 + 赋值放行"多个关切；`Call` 与历史含义重叠（建议自认） | 已检查 | 违反原则 #1/#4/#9；与 #52 Await 省略目标相抵；`Call` 语义随返回类型漂移（原则 #7 风险） |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题具体诚实（1–3 健康区间）；但 Drawbacks 薄、无 Compatibility/breaking-change 章节、无 Option Strict 分叉、状态行占位链接 | 已检查 | 严重级别这一核心设计点被推进未决而正文未讨论；`_` 破口未识别；与 #89 的耦合未提 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。A/B 破坏兼容（暗）且与主线"几乎不做破坏"、"异步跟随 C#"方向断裂（风）；光（正确性）正向但可被分析器覆盖；雷（迭代速度）无增益 | 已检查（预测待定） | 若走 D（分析器）路径属性显著回升，但那是方案选择而非建议本身；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 11 章；未声明 `Call` 继承 VB6/VB 历史语义；借鉴 C# 警告路径未提；18.15"白鲸/未观测异常"这一真实动机未关联到 #89 | 已检查 | "Conversions to task objects"措辞与 .NET 事实不符（`ValueTask`→`Task` 无隐式转换）；来源标注缺章节号；无杂质 |

## 设计原则对照

- **与 VB 基因：偏离**——强制路径违反"永不破坏"（#1）、"默认跟随 C#"（#4）、"消除样板"（#9），`Call` 重定义触及"避免隐蔽语义变化"（#7）；唯一一致点是"显式放行有助理解"（#10）。
- **与主线关系：主线冲突 + 部分主线一致**——强制错误与主线"异步跟随 C# / 委托 C# LDM"（#37、2014.02.17）的基调冲突；警告路径与 2018.02.07"警告墙"推迟先例冲突；分析器路径（D）主线一致。属 Anthony 独立延伸，向激进方向。
- **破坏性变更：A 有（错误级，教科书式）；B 有（重编译后警告墙）；C 无；D 无。**

## 总评

- **达成程度：未达成（作为语言特性）**——概念价值（防丢异常、显式化 fire-and-forget）成立，但强制机制与 VB 基因和主线方向冲突；效果证据止于书面且核心设计点（严重级别）悬空。
- **LDM 三态建议：Reject（强制错误部分）/ Table（警告与 `Call` 语义）/ Active（官方分析器配套）**——真实价值以分析器 + 文档形式落地，语言层不动。
- **主要问题**：① 严重级别未定，而"错误"正是破坏性来源；② `Call` 语义重定义与历史含义重叠且随返回类型漂移；③ `_` 丢弃变量使"强制"不完整；④ 与 #52 Await 省略目标相抵、与 #89 异常策略耦合而未协调；⑤ 无兼容性/breaking-change 分析与 Option Strict 分叉。

## 返工建议

- **补充章节**：Compatibility / breaking-change（A/B 的迁移路径、`langversion` 门控、警告策略）；Option Strict 分叉（静态可判定 vs 晚期绑定）；与 #52、#89 的交互（各自单独成节）；`Call` 语义精确定义（同步 Sub / 非 awaitable Function / awaitable Function / 与 `Await` 叠加 四分支）。
- **补充证据**：真实代码库未等待异步调用占比数据；若走警告路径的最小原型（检测器 + `Call` 放行 + 快速修复）；`ValueTask` 捕获场景的编译验证。
- **未决问题处理**：严重级别 = 警告（对齐 C# `CS4014`，`Probably`），错误级直接放弃；`Let _ = ...` 继续合法、不特殊禁止；异常处理策略移交 #89，本建议明确声明不吞异常。
- **设计探索**：家族一致性——先定"异步边界可见性"的立场（与 #52 对表），再回来定 `Call` 语义；把"显式不等待"先写成样式指南/文档沉淀，为未来语言层决策留接口。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像）核实，补充本提案在 C#/CLR/.NET 生态中的对应走向。与既有「附录：特性评价」不同，本附录只做「C# 现实方向 vs 本提案响应」的对照与桥接，**不改变正文 RESOLUTION**。凡引用 C# 原文均逐字一致并标注来源文件；无法在仓库核实的标 **Suspect** 或列入 **OPEN QUESTIONS**。

### 相关 C# 现实方向

**R1 —「忘了 await」在 C# 是编译器警告 + 丢弃惯用法，不是语言强制错误。**
C# 对「未等待的异步调用」的态度从未升到语言错误。主线处理 `Task` 返回调用未等待的方式是编译器警告 `CS4014`（Roslyn 编译器诊断，**本仓库无正文**，见引用纪律），其标准放行写法是把返回值显式丢弃到 `_`：

```csharp
_ = GetTaskAsync(); // 显式接住再丢弃，抑制 CS4014
```

这与本提案 RESOLUTION #7 的 `Let _ = MkDirAsync()` 继续合法是同一个惯用法——C# 用「显式赋值给 `_`」作为「我知道我在丢」的印章，而不是发明新语法。关键对照：本提案想把印章换成 `Call`，而 C# 的印章恰好是「接住再丢」的 `_ =`——正是本提案规则 1 保留、规则 2 想绕过的路径（见正文「`_` 破口」）。C# 不觉得这是破口，因为 C# 的目标不是「强制」，而是「给丢弃一个显式名字」。

**R2 — C# 生态已把「调用是否被立即 await」当作分析层关注的静态属性，但仍用警告/分析，而非语言强制。**
nullability-improvements 工作组讨论 `[MemberNotNull]` 等后置条件在 async 方法上的语义时，明确提出让后置条件只在调用被「立即 await」时才生效：

> "such that the postconditions are only applied if the call is *immediately awaited*."

→ `meetings\working-groups\nullability-improvements\NI-2022-11-01.md`

同文件还点出被丢弃 Task 的异常语义：「when an async method throws an exception, it is packaged up into a Task and returned to the caller. It is not thrown until the task is awaited.」（→ 同文件）。这证明 C# 类型系统/分析正在把「awaited or not」当成一个有意义的静态事实——但落实手段是编译器警告与可空分析，从未变成「不 await 就编译失败」的语言规则。这给本提案的三态判定提供生态级旁证：**C# 认为「未等待」值得分析层认真对待，但拒绝语言层强制。**

**R3 — awaitable 在 C# 里是一个「模式」而非一个类型：task-like types + `[AsyncMethodBuilder]`。**
C# 7.0 的 task-types 提案把 `async` 扩展到任何匹配「task 类型模式」的类型，而不只是 `Task`/`Task<T>`：

> "Extend `async` to support _task types_ that match a specific pattern, in addition to the well known types `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>`."

→ `proposals\csharp-7.0\task-types.md`

判定「awaitable」靠 `GetAwaiter()` + awaiter 类型，判定「task 类型」靠 `AsyncMethodBuilderAttribute` 关联的 builder。C# 10 进一步允许 per-method 覆盖 builder：

> "Allow per-method override of the async method builder to use."

→ `proposals\csharp-10.0\async-method-builders.md`

对本提案最直接的冲击是正文 `Suspect` 那句：原文「Conversions to task objects still allowed」的措辞与 .NET 事实不符——`ValueTask` 到 `Task` 没有隐式转换，而 awaitable 是一个开放模式（任何有 `GetAwaiter` 的类型都行，含 `ConfiguredTaskAwaitable`）。若规则要按「捕获到变量即放行」写，正确判据是「捕获到 *task-like / awaitable* 类型变量」，而不是「转换到 Task」。

**R4 — ConfigureAwait 的 scope/assembly 级方案，C# 明确留给未来；这正是 #89 的地盘。**
async-streams 提案在讨论 `await foreach` 的 `ConfigureAwait` 时，明确否掉了「为整个作用域/程序集统一配置」的念头：

> "(If we can come up with some way to support a scope- or assembly-level `ConfigureAwait` solution, then this won't be necessary.)"

→ `proposals\csharp-8.0\async-streams.md`

即 **C# 现实里不存在 #89（ConfigureAwait Options / 默认 ConfigureAwait）要的那种「一次配置、全局生效」的开关**。ModVB #89 若做，是 C# 现实之外的 VB 独立延伸。这反过来印证正文第 4 条：`Call` 不能声明「异常怎么处理」——那条路 C# 也没走，留给库层（`TaskScheduler.UnobservedTaskException`）与未来的语言实验。

**R5 — fire-and-forget 是 C# 公认的真实模式；C# 的回应是「承认 + 命名 + 等待入口」，不是消灭它。**
C# 7.1 async Main 讨论里，LDM 明确把 `async void` 与「fire-and-forget」挂钩，并因此拒绝让 `void` Main 变成 async：

> "that makes it *look* like an `async void` method, which is fire-and-forget, whereas we actually want program execution to wait for the main method to finish."

→ `meetings\2017\LDM-2017-02-28.md`

C# 对「故意不等待」没有发明「声明不等待」的关键字；它给的是 `_ =` 丢弃、`async void`（明示 fire-and-forget）和 awaitable 入口（`Task Main` 隐式 `.GetAwaiter().GetResult()`，同文件）。本提案的 `Call` 若想当「显式声明不等待」的唯一合法通道，在 C# 里没有对应物——最接近的 `_ =` 恰恰是本提案规则 1 保留、规则 2 想堵的路径。

**R6 — C# 对「警告墙」的先例是 NRT：警告可做，但必须 opt-in、必须当破坏性变更对待。**
C# 8 NRT 提案正文把「新警告」明确列为需要 opt-in 的破坏性变更：

> "Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism."
> "So nullable warnings also need to be optional"

→ `proposals\csharp-8.0\nullable-reference-types.md`

这与正文 2018.02.07（VB LDM）「警告墙」推迟先例是同一逻辑，且给出 C# 侧的操作结论：**警告可以上，但要么 opt-in（`#nullable enable`），要么有迁移工具；直接把新警告压到存量代码上是不可接受的**。对本提案 B（警告）路径，C# 的先例意味着必须回答「VB 的 opt-in 开关是什么」（`#Ignore Warning`？langversion？）——正是 RESOLUTION #2 的 (a)(b) 两个信号。

**R7 — C# 侧对 VB 的定位：C# 定调、VB 跟随；VB 不在 scope 时 C# 明说「VB 不需要」。**
LDM-2015-01-21（该场 Anthony D. Green 在列）开篇交代 C#/VB 设计联动：

> "There will not be a separate Visual Basic design meeting during this initial period, as many of the overall decisions are likely to apply to both and need to happen in concert."

→ `meetings\2015\LDM-2015-01-21.md`

unsafe-evolution 的 VB 小节更直白：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."

→ `proposals\unsafe-evolution.md`

两条合起来就是正文「与主线关系」引用的 2014.02.17 异步跟随 C# 的 C# 侧镜像：**C# LDM 决定大方向时默认覆盖 VB；VB 若想走 C# 没走的路（如本提案的强制 Await、`Call` 重定义、#89 的全局 ConfigureAwait），就注定是「VB 独有」且要自行承担与 C# 生态分叉的成本。** 好在异步这一块分叉成本低：不改 IL、不进 CLR 元数据（正文第 11 条），纯编译期/分析期。

### 现实 vs 提案

| 提案元素 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| A：编译错误强制 | `CS4014` 是警告；NRT 先例要求 opt-in；C# 从未用语言错误强制 await | **冲突** | C# 把「未等待」当可诊断/可分析问题，不是语法非法；错误级在 C# 生态无对应物 |
| B：编译器警告 | C# 有 `CS4014` 警告先例；NRT 证实「新警告 = 需 opt-in 的破坏性变更」 | **需桥接（有条件兼容）** | 方向与 C# 一致，但 C# 先例要求 opt-in/迁移工具；VB 需定义自己的开关（`#Ignore Warning`/langversion） |
| C：仅 `Call` 语义 | C# 无「显式声明不等待」语法；最接近的 `_ =` 丢弃是「接住再丢」而非新关键字 | **需桥接** | `Call` 若要被 C# 侧工具理解，需在语义模型暴露「已声明故意」标记；与 C# 的 `_ =` 语义互通 |
| D：官方分析器 | C# 生态证明「未等待提示」作为分析器/警告完全够用（`CS4014` + analyzers + NI postconditions 均走分析层） | **兼容** | 与 C# 现状同构；零破坏、可配严重级别；是 C# 处理此类问题的默认路径 |
| 规则判据「Conversions to task objects」 | awaitable/task-like 是模式不是类型；`ValueTask`→`Task` 无隐式转换 | **冲突（需修正）** | 佐证正文 `Suspect`：应写「捕获到 awaitable/task-like 类型变量」 |
| `Call` 与 #89（异常策略） | C# 无全局 ConfigureAwait；scope/assembly 级方案明确留给未来 | **脱节（VB 独立延伸）** | #89 若做是 C# 现实之外的 VB 独有特性，不影响互操作；`Call` 不能承诺异常语义 |
| `Let _ = MkDirAsync()` 合法（RESOLUTION #7） | C# `_ = GetTaskAsync()` 是官方认可的 `CS4014` 抑制惯用法 | **兼容** | 与 C# 对齐；本附录给 C# 佐证 |

### 对 VBScript.NET 的适应建议

- **默认走分析器（D），与 C# 生态对齐**：官方分析器 + `Call` 放行 + 快速修复，严重级别默认「建议」。这使 .vbx 脚本的「忘了 await」检查与 C# 的 `CS4014`/analyzers 体验同构，跨语言团队不用学两套纪律。
- **规则判据按「task-like/awaitable 模式」写，不按「Task 类型」写**：VBScript.NET 编译器/分析器要识别 `AsyncMethodBuilderAttribute`（task-like 类型的关联 builder）与 `GetAwaiter` 模式，才能正确判定「静态返回类型可判定为 awaitable」（RESOLUTION #6）。这正好复用 C# task-types / async-method-builders 已定的元数据语义，无需发明新元数据。
- **识别 C# 10 的 per-method `[AsyncMethodBuilder]`**：C# 10 允许方法级覆盖 builder（R3）；VB 模块消费这类 C# 方法时，其返回类型仍是 task-like 模式，分析器按模式判定即可、不受 builder 覆盖影响——但编译器要认识该属性，避免把自定义 task-like 类型误判为非 awaitable。
- **`Call` 的语义模型标记**：若走 D，`Call <async-call>` 需要在语义模型暴露「该调用被显式放行」的语句级标记（正文第 3 条），供分析器、IDE、快速修复读取——这与 C# 对 `_ =` 丢弃的识别（编译器在表达式级知道这是丢弃）对齐，只是 VB 落在语句级。
- **Option Strict 分叉保持**（RESOLUTION #6）：晚期绑定调用返回 `Object`，规则不适用。C# 无对应物（C# 无晚期绑定），这是 VB 独有的安全面，写进规范即可。
- **#89 的 ConfigureAwait 默认值若做，是 VB 独有延伸**：C# 明确把 scope/assembly 级 ConfigureAwait 留给未来，.vbx 做了也不破坏与 C# 库的互操作（`ConfigureAwait` 是 BCL API），但要在文档标注「VB 独有、C# 无对应」，避免跨语言代码库误以为 C# 也有。

### 对既有 RESOLUTION/三态判定的影响

- **不推翻任何 RESOLUTION 条目**。C# 现实整体支持「语言层 Reject/Table、工具层 Active」的判定：C# 对「未等待」的所有投入（`CS4014` 警告、NI postconditions「immediately awaited」、`_ =` 丢弃）都在分析层，没有一条走语言强制。
- **RESOLUTION #7 获得 C# 佐证**：`Let _ = MkDirAsync()` 继续合法 = C# `_ = GetTaskAsync()` 的 VB 形态；C# 官方不把它当「强制规则的破口」，因为 C# 的目标本就不是防绕行。
- **正文 `Suspect`（「Conversions to task objects」措辞）获得生态级确认**：task-like 是模式（task-types.md）、per-method builder 可覆盖（async-method-builders.md）、`ValueTask` 无到 `Task` 的隐式转换——规则应改写为「捕获到 awaitable 类型变量」。
- **OPEN QUESTION（B 的严重级别 / `#Ignore Warning` 交互）与 C# NRT 先例对表**：C# 的结论是「新警告必须 opt-in 或带迁移工具」；VB 若重审 B，应先定义 opt-in 开关，再谈严重级别。

### 引用纪律与核实状态

本附录全部 C# 原文均逐字取自 `..\..\csharplang`，已人工核实，标注来源路径：

- 「…when an async method throws an exception, it is packaged up into a Task and returned to the caller. It is not thrown until the task is awaited…」「such that the postconditions are only applied if the call is *immediately awaited*.」→ `meetings\working-groups\nullability-improvements\NI-2022-11-01.md`
- 「Extend `async` to support _task types_ that match a specific pattern, in addition to the well known types `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>`.」→ `proposals\csharp-7.0\task-types.md`
- 「Allow per-method override of the async method builder to use.」→ `proposals\csharp-10.0\async-method-builders.md`
- 「(If we can come up with some way to support a scope- or assembly-level `ConfigureAwait` solution, then this won't be necessary.)」→ `proposals\csharp-8.0\async-streams.md`
- 「Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism.」「So nullable warnings also need to be optional」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「that makes it *look* like an `async void` method, which is fire-and-forget, whereas we actually want program execution to wait for the main method to finish.」→ `meetings\2017\LDM-2017-02-28.md`
- 「There will not be a separate Visual Basic design meeting during this initial period, as many of the overall decisions are likely to apply to both and need to happen in concert.」→ `meetings\2015\LDM-2015-01-21.md`
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`

- **Suspect / OPEN QUESTIONS**：`CS4014` 的精确警告文案与 ID 属 Roslyn 编译器诊断，**本仓库（dotnet/csharplang）无正文**，正文 2018.02.07 处已标注 `Probably`；本附录沿用该判定，仅在生态事实层面引用「C# 用编译器警告处理未等待调用」，具体文案需查 learn.microsoft.com / roslyn 仓库核实。
- **OPEN QUESTIONS**：`#89` scope/assembly 级 ConfigureAwait 与 C# async-streams 的「future」方案之间是否值得正式对表（含配置格式/默认值语义）；`Call` 语句级标记在 Roslyn VB 语义模型里的 API 形状尚未设计。
