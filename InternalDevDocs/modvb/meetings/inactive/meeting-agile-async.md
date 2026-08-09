# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周回到异步。主线的 `Await` 讨论（#37 Await in Catch/Finally）刚在 2018 年以 "Let's do it!" 收尾，而 ModVB 的异步建议却一口气堆了五份（`async-sub`、`agile-async`、`require-await-call`、`async-iterator`、`async-event`）——它们共享同一片状态机实现面，但方向上互相拉扯。本场只审 `agile-async` 一份，其余在相关处引用。这场讨论比预想的更分裂：**建议原文把两个互不相关的特性绑在了一起**，而其中一个（中间 `Await` 省略）撞上了我们自己在 2018 年立下的牌子。

## Agenda

* [Proposal: Agile Async 与 Await 省略](#proposal-agile-async-与-await-省略)

## Proposal: Agile Async 与 Await 省略

_Related: [vblang #167 – Support for Return? construct](https://github.com/dotnet/vblang/issues/167)；[vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #59 – New conversion operator/syntax](https://github.com/dotnet/vblang/issues/59)；ModVB：`proposal-require-await-call.md`、`proposal-async-sub.md`、`proposal-async-iterator.md`、`proposal-async-event.md`、`proposal-postfix-casting.md`、`proposal-configureawait-options.md`_

### 场景与缺口

We started from Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1716–1777）。这一章**没有散文，只有一段代码块**——11 行示例把六个特性塞在一起：`Async Sub` 返回类型、可配置默认异步类型、`Agile Async`、中间 `Await` 省略、强制 `Await`/`Call`、`Await Each`、`Async Iterator`、`Async Event`。我们先把本建议主张的两个缺口单独拿出来：

**缺口一：库代码的同步上下文样板。** `Async` 方法在 await 后恢复时会经由 awaiter 的 `OnCompleted` 把续体投递回捕获到的 `SynchronizationContext`。对 UI 线程这是特性，对库代码这是开销与死锁来源——所以库作者到处补 `ConfigureAwait(False)`：

```vb
' 今天：库代码里的标准样板。
Public Async Function DownloadTextAsync(url As String) As Task(Of String)
    Dim content = Await s_http.GetStringAsync(url).ConfigureAwait(False)
    Dim json = Await ParseJsonAsync(content).ConfigureAwait(False)
    Return json!value
End Function
```

**缺口二：嵌套 await 不可读。** 多层异步调用的括号嵌套把阅读顺序倒过来：

```vb
' 今天：'(Await (Await obj.MAsync()).NAsync()).P' 从内向外读。
Let result = (Await (Await obj.MAsync()).NAsync()).P
```

We 认可这两个动机都是真的——第一个尤其真实，它是库代码每一天都在付的成本。**但 We don't think 它们是一个特性。** `Agile` 是声明级修饰符，改变整个方法的续体行为；省略是表达式级语义，改变一段链式代码的绑定与求值。两者风险画像完全不同，捆绑评估只会互相掩盖。这是本场第一个结论。

### 候选方案

**PROPOSAL A — 只做 `Agile Async` 修饰符（原文第一段）。** 声明级：`Agile Async Sub FeelFreeToUseThreadPool()`，方法内每个 await 点等价于隐式 `ConfigureAwait(False)`，不捕获/恢复同步上下文。

**PROPOSAL B — 只做中间 `Await` 省略（原文第二段）。** 表达式级：`Await obj.MAsync().NAsync().P` 等价于 `(Await (Await obj.MAsync()).NAsync()).P`，中间的 await 点自动省略。

**PROPOSAL C — 两者捆绑（原文形态）。** A + B 一起进设计。

**PROPOSAL D — 都不做，走生态路线。** 同步上下文问题交给 analyzer/API（C# 生态的默认路径，见下）；嵌套 await 维持显式。

**PROPOSAL E — 项目级默认替代。** 用 `async-sub` 建议里的"可配置默认异步类型"承担"库代码不想捕获上下文"的场景，不引入 `Agile` 关键字（原文第 11 章本就捆绑了这两者）。

### 权衡：Q&A

- **A vs C：捆绑为什么危险？** 两份特性的证据强度、风险来源、复活条件完全不同。捆绑使读者无法单独否定省略而不连坐 `Agile`；也让"省略"藏在"库代码样板"这个更正当的动机后面过关。**We 拒绝捆绑。** 拆分后分别评估，A 与 B 各自对号入座。

- **A 的语义能定义吗？** 建议原文说 `Agile` "等价于每个 await 点都隐式 `ConfigureAwait(False)`"。这有四个问题。其一，`ConfigureAwait` 是 **awaited 值上的方法**，不是编译器开关；编译器要"隐式调用"它，就必须对每个操作数合成一次调用——这只能作用于有 `ConfigureAwait` 的类型（`Task`/`Task(Of T)`/`ValueTask`/`ValueTask(Of T)`），**自定义 awaiter 没有 `ConfigureAwait`**，此时 `Agile` 是什么语义？报错、回退为捕获、还是发明编译器内部机制？建议未定义。其二，spec（`expressions.md` §Await Operator）写明续体是 awaiter 中介的——编译器对 awaiter 调用 `UnsafeOnCompleted` 或 `OnCompleted`，而 `SynchronizationContext` 的捕获发生在 `TaskAwaiter.OnCompleted` **内部**（spec 的示例 awaiter 正是 `Dim sc = SynchronizationContext.Current` 后 `sc.Post`）；编译器层没有一个干净的"本点不捕获"开关。其三，spec 写明 resumption delegate "first restores `System.Threading.Thread.CurrentThread.ExecutionContext`"，任何"清空同步上下文再恢复"的实现都会与 ExecutionContext 流纠缠。其四，"隐式调用 `ConfigureAwait(False)`"要求操作数类型在绑定期已知且具备该方法——这又把 Option Strict 分叉卷进来了。**结论：`Agile` 目前不可指定。** 这本身就是不进入设计的充分理由。

- **A vs E：`Agile` 有没有独立于项目级配置的生存空间？** 存疑。库代码"不想捕获"的需求，其实可以在调用侧用 `ConfigureAwait(False)`、在项目侧用默认配置（E）表达；`Agile` 作为第三种做法，恰好踩中设计原则 #3（不引入"第二种做事方式"）——而且它引入的是**第二种异步方法**（`Async` 与 `Agile Async` 并存，行为分裂），比普通语法重复更严重。`Suspect`：如果 E 落地，"库代码样板"的一半价值就被吸走了。

- **A 的命名。** `Agile` 是英文里极常用的词（敏捷开发）。把它做成修饰符关键字，等于把每个含 `Agile` 标识符的现有代码都置于重解释风险下——即使做成上下文关键字（像 `Async` 那样，spec：`Await` 只在 `Async` 方法内保留、他处不保留），`Agile` 的辨识度也太低：`Agile Async Function` 读起来像"敏捷的异步函数"，语义零信息。**We don't like the name。**

- **B vs 设计原则 #7（隐蔽语义变化）。** 这是整场最重的一锤。2018-05-30 我们审 #167 `Return?` 时的原话：**"We think this is a bad idea. Control flow would be altered by a very subtle character. It's not the same meaning as other uses as ? (any alteration in control flow)"**（Labels: LDM Reviewed: No Plans）。`Return?` 被拒，因为它用**一个细微字符**改变控制流。中间 `Await` 省略是同一件事的**表达式级放大版**：一行链式调用里的异步边界全部隐形，读者（与调试器）无法从源码看出哪些调用是同步、哪些会挂起、异常在哪一点被捕获。`Return?` 是一个字符，省略是一条链——我们找不到理由对后者网开一面。

- **B vs Task 成员调用的链式惯用法。** 省略规则必须回答"哪些中间调用被隐式 await"。而异步代码里到处都是返回 awaitable 的调用，同时 `Task` 上又有 `ContinueWith`、`ConfigureAwait`、`Result`、`Wait` 等成员。两者冲突是致命的：

```vb
' 今天合法且高频：先调 Task 上的组合/配置方法，再 await。
Let text = Await client.GetStringAsync(url).ConfigureAwait(False)

' B 的规则（"返回 awaitable 的中间调用都隐式 await"）会把它重解为：
'   Await client.GetStringAsync(url) 得到 String，再在 String 上找
'   ConfigureAwait —— 不存在，编译错误。
' 一个今天能编译、且语义正确的惯用法，在新规则下要么报错、要么改绑。

' 同理：ContinueWith。
Let json = Await client.PostAsync(url, body).ContinueWith(AddressOf ReadJson)
```

这不是角案例，这是现有异步代码的主体形态。省略规则要么把这些调用重解释，要么为它们开例外，而"开例外"又反过来使省略规则的判定不再是"返回 awaitable 就省略"。**We see no consistent rule.**

- **B 的 `.Result` 死锁陷阱。** `Await obj.MAsync().Result` 今天表示 `Await (obj.MAsync().Result)`——先同步取 `Result`（在 UI 线程上就是死锁源头），再 await。省略规则下它变成"await 后取结果上的 `Result`"。同一个源码，两种语义；其中一种还是教科书级的死锁写法。读者无法从写法分辨。

- **B vs 重载解析的脆弱性。** 省略判定依赖"链上每个成员的返回类型是否 awaitable"。库作者加一个重载、把 `Task` 换成 `ValueTask`、给结果类型加一个同名成员——都会静默改变"哪些点被 await"。也就是说，**库的演化会改变既有代码的求值边界**。We 对这类"依赖类型细节的语义漂移"零容忍。

- **B vs require-await-call（同组 sibling）。** `proposal-require-await-call.md` 主张**强制显式 `Await`**（未 await 的异步调用直接报错，fire-and-forget 必须 `Call`）。本建议主张**自动省略 `Await`**。同一场 ModVB 设计里，两个方向背道而驰——一个加仪式，一个删仪式。两者不能同时进入设计；在决定取舍前，任何一个都只是半成品。建议原文自己在 Unresolved #4 里引用了对方的关键句（"Conversions to task objects still allowed" / `Let t As Task = MkDirAsync("...")`），却没有定义两者的边界。

- **B 的价值演示建立在已 Table 的特性上。** 原文第二个示例 `? Await client.HttpGetAsync("...")(As JsonObject)!requestId(As Guid)` 依赖两件事：一是 `(As Type)` 后置转换——我们在 `meeting-postfix-casting.md` 已裁定 **Table**；二是行首的 `?` 是即时窗口（Immediate Window）提示符，**不是合法 VB**。也就是说，这条最能打的示例由"未采纳的语法 + 非法语法"组成。`Probably`：拆掉这两块之后，省略剩余的独立价值只剩括号消除。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Await` 的保留规则本身清晰（spec：`Await` 仅在含 `Async` 修饰符的立即闭合方法/lambda 内保留，且出现在 `Async` 之后；他处不保留）。歧义不在词法，在**绑定**：省略规则要求"知道链上各成员的返回类型"才能决定 await 边界，而返回类型要等绑定、绑定又要等 await 边界——先有鸡还是先有蛋。spec 的 awaiter 规则（操作数必须 awaitable 或 `Object`）对"整条链"不再成立，因为 `.P`（尾成员）不是 awaitable。**省略规则无法在解析期确定，只能在绑定后回填，这会让语义模型出现"先绑定后决定边界、再重绑定"的两遍依赖。**

#### 2. 角案例与边界语义

- **尾成员是什么？** `Await obj.MAsync().NAsync().P` 里 `.P` 不被 await（展开式把它放在最外层 `Await` 之外）。但如果 `.P` 是返回 `Task` 的属性呢？"最后一个 awaitable"是 `NAsync()` 还是 `.P`？省略规则必须给出与 `Await X.Y().Z` 同形的判定，而建议只在注释里给了一个例子。
- **非 awaitable 结果链。** `Await obj.MAsync().P`（`MAsync` 返回 `Task`，`P` 是结果上的普通属性）：显式 `Await` 的目标是"链上最后一个 awaitable 调用"——若规则要求显式 `Await` 的目标本身 awaitable，则 `(Await obj.MAsync()).P` 里被 await 的是 `MAsync()`，语义成立；但读者写 `Await obj.MAsync().P` 时以为 await 的是整条链。**写法与语义之间的映射完全靠猜测。**
- **`Await obj?.MAsync()`。** 建议原文自己标注 **"Not shown: `Await obj?.MAsync()` actually working."**——`?.` 与省略的组合在原文中根本没展示可用。空条件调用 + 隐式 await：`obj` 为空时整条链跳过，`obj` 非空时 `MAsync()` 被 await？这个交互未定义，而 `?.` 在异步代码里极常见。
- **异常传播边界。** 建议的 Drawbacks 自己承认"自动省略中间 await 可能影响异常传播与异步状态机的划分"。省略把多个 await 点折叠进一条表达式，异常发生在"被省略的 await 点"与发生在"显式 await 点"，在堆栈与状态机位次上不再可区分。`Suspect`：调试体验会明显劣化——单步只看到一行链式调用，中间挂起点对调试器不可见。
- **值类型 / 结构体。** spec 明确 `Async`/`Iterator` 方法内可变结构体会隐式操作副本。`Agile` 若改变状态机生成，必须与这条既有规则对齐——未定义。

#### 3. 作用域与绑定

语义模型里，`Await obj.MAsync().NAsync().P` 的 `Await` 绑定到什么？一个折叠的异步链没有单一的"被 await 的表达式"节点；`GetTypeInfo` 应返回什么——`P` 的类型？`NAsync()` 的结果类型？每个省略点的符号都必须存在（否则 IDE 无法导航），但 AST 里没有这些节点。`Probably`：省略必须引入"隐式 await 节点"或等价物，等于给编译器加一种新的合成表达式。绑定成本显著。

#### 4. 与既有特性的交互

- **与 `Await` 的限制域。** spec：await 表达式不得出现在 `Catch`/`Finally`/`SyncLock` 体内、查询表达式内。省略若把这些限制扩展到"隐式 await 点"，意味着 `Await obj.MAsync().NAsync()` 在 `SyncLock` 体内会因"中间点不该 await"而报错——这条边界没人设计过。
- **与后置转换 `(As Type)`。** 价值示例依赖它，而它是 Table（见 `meeting-postfix-casting.md`，其中 `Await` 优先级问题也是 OPEN QUESTION）。**两条未定特性互相引用，等于未定平方。**
- **与 Option Strict Off / 晚绑定。** spec：await 操作数为 `Object` 时"deferred until runtime"（运行时调 `GetAwaiter`/`IsCompleted`/`TryCast` 到 `ICriticalNotifyCompletion`）。宽松模式下 `obj.MAsync()` 是 `Object`，省略规则无从判定它是否"返回 awaitable"——**严格/宽松两条路径的省略行为必然分叉**，除非明文规定宽松模式不省略。
- **与 async-iterator / async-event。** `IAsyncEnumerable` 的 `Await Each` 与 `Async Event` 各自的状态机都含 await 点；`Agile` 若覆盖它们，`Agile Async Iterator`、`Agile Async Event` 的语义是什么？建议 Unresolved #2 承认"未定义"。
- **与 lambda / 闭包。** 省略只在"立即闭合方法为 `Async`"时有意义；lambda 内的 `Await` 省略与外围异步方法的状态机划分如何交互，未定义。

#### 5. Breaking change 与兼容性

`Agile` 是新修饰符，若做成上下文关键字，不破坏既有代码。但**省略是重解释既有合法代码**：任何"整条链可 await、且中间成员同时存在于 awaited 类型与 `Task` 类型"的现有代码，在新规则下绑定改变或报错（见 Q&A 的 `ConfigureAwait`/`ContinueWith` 例）。这与 `Return?` 的拒绝理由同源：**同源码重编译，行为/绑定变。** 这是设计原则列表里最重的一条红线。

#### 6. Option Strict / 编译选项分叉

如上：`Object` 型操作数把省略判定推迟到运行时，严格路径（类型已知）与宽松路径（类型为 `Object`）的省略行为无法一致。任何要求"两路径行为一致"的写法都在这条上失效。

#### 7. IDE / IntelliSense

省略使"这个成员访问会不会挂起"对补全、签名帮助、调试步进全部隐形。需要新的可视化（awaiter 边界提示）、新的错误文案（"此处的中间调用被隐式 await"）、重构"提取局部变量"必须把省略点物化成显式 `Await`。原型验证成本高，而建议没有任何 IDE 设计。

#### 8. 数据 / 普遍性

"库代码要补 `ConfigureAwait(False)`"是真实的，但没有量化数据；"嵌套 await 括号难读"同样没有频率证据。`Suspect`：`ConfigureAwait` 样板在库代码里高频，在"数十万安静客户"的业务代码里低频（业务代码大多在 UI 线程，捕获是想要的）；省略的价值则完全落在低频的深链场景。C# 生态对这个问题的默认答案是 **analyzer 与 API 纪律**（如 CA2007 一类"不要直接 await Task 而不调 `ConfigureAwait`"的规则）——此条来自 C# 生态常识，仓库内 `..\..\vblang` 的会议记录里没有语言级讨论，`Suspect`（需外部核实）。如果 C# 用工具解决，VB 默认跟随（原则 #4）就指向工具而非语言。

#### 9. 更简替代

- `ConfigureAwait(False)` 逐点标注：现状，样板但确定。
- **analyzer**：提醒"此处 await 会捕获上下文"，或强制 `ConfigureAwait(False)`——C# 生态路径，零语言表面。
- **项目级默认**（E，借 `async-sub` 的可配置默认异步类型）：库代码在项目层面表达"不捕获"，不引入关键字。
- **`Async` 状态机的既有开关**：主线 2017-08-09 对 #37 的决议是 "The feature is approved in principle but needs its priority driven by other platform changes such as `IAsyncDisposable/Async Using`"——主线处理异步特性的节奏是**等平台**，不是造新修饰符。
- 嵌套括号的替代：中间变量（`Let step1 = Await obj.MAsync()` 后继续），与"提取方法"重构。显式且可调试。

#### 10. 复杂度 / 成本 / 优先级

`Agile` 需要新的状态机生成模式（不捕获续体），且要处理自定义 awaiter 的缺失——编译器级改动。省略需要绑定回填、合成节点、重载解析规则、IDE 可视化——是 Roslyn 全栈特性。两者的实现成本都不低于主线已批准而尚未落地的 #37（"If we drop the difficult scenarios and provide support and disallow Await and these jumps to happen together, this is doable"——#37 靠**砍场景**才可行；本建议连核心规则都未成形）。价值未量化，风险高，优先级排不进当前序列。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 问题（省略最终生成的是显式 await 等价 IL；`Agile` 若实现为"隐式 `ConfigureAwait(False)`"也只是对既有方法的调用）。但"编译器级不捕获同步上下文"没有一个现成的 IL/CLR 机制可以锚定——它要么退化为"对 `ConfigureAwait(False)` 的语法糖"（受限于 Task 家族），要么需要新的运行时约定。这是实现硬约束，不是装饰。

#### 12. 值不值得做

- **省略**：价值（括号少一层）× 成本（Roslyn 全栈）× 风险（重解释既有代码、重载解析脆弱、隐蔽语义）——**不值得，且是 `Return?` 的明确先例**。
- **`Agile`**：价值真实但窄（库代码样板）；成本高；机制未定义；生态已有替代路径（analyzer / 项目级默认）。**方向可以记住，现在不做。**

### VB 基因对照

- **消除常见样板（原则 #9）**：两半都冲着它去，但都在"以样板换确定性"的地方翻了车——省略把显式 await 的确定性抹掉了。
- **避免隐蔽语义变化（原则 #7）**：省略是**直接违反**，2018-05-30 #167 决议逐字适用（见 Q&A）。`Agile` 相对温和，但"第二种异步方法"也制造了行为分叉。
- **不引入"第二种做事方式"（原则 #3）**：`Agile` 踩中；省略则是"同一件事少写几个字但语义不同"，比第二种方式更糟。
- **保持 VB-like / 读起来像英语（原则 #2、#5）**：`Agile Async` 这两个词的组合在 VB 历史与 VB6 传统里无对应物；"agile"是通用英文词，作关键字零辨识度。
- **默认跟随 C#（原则 #4）**：C# 生态用 analyzer/API 解决 `ConfigureAwait` 样板，没有语言级"不捕获"或 await 省略——VB 无充分理由偏离。
- **与主线关系（对照表 2.3）**：本建议**不在主线对照表内**，属 **Anthony 独立延伸**，且与主线对 `Await` 的处理方式（2014-04-02 允许 await 出现在全部表达式上下文、"even though it's bad practice to use them in confusing places like `x[await t1] += await t2;`"——即**允许但明示是坏实践**）方向相反：主线给显式 `Await` 全语境自由，本建议要把它藏起来。与同组 `require-await-call`（强制显式 `Await`）**直接冲突**；与 `postfix-casting`（Table）互相引用未定；与 `async-sub` / `async-iterator` / `async-event` 交互未定义。

### RESOLUTION:

1. **拆分，不捆绑。** 本建议捆绑了 `Agile Async` 与中间 `Await` 省略两个独立特性，捆绑本身被否。两部分独立评估、独立处置。
2. **中间 `Await` 省略：Reject。** 直接适用 2018-05-30 #167 `Return?` 决议（"Control flow would be altered by a very subtle character"）。省略把异步边界隐形、重解释既有合法代码（`ConfigureAwait`/`ContinueWith` 惯用法）、绑定依赖重载解析回填、与 `require-await-call` 的强制显式方向互斥。**不进入设计。**
3. **`Agile Async`：Table。** 动机（库代码同步上下文样板）真实，但机制不可指定（"隐式 `ConfigureAwait(False)`"无法泛化到自定义 awaiter；spec 表明上下文捕获是 awaiter 中介的，编译器层无不捕获开关）、命名零辨识度、引入"第二种异步方法"（原则 #3）、C# 生态默认路径是 analyzer/API（原则 #4）。**复活信号两条**：(a) 出现可在编译器层通用实现的"不捕获"机制，或明确限定 Task/ValueTask 家族并给出 `ConfigureAwait` 缺失时的错误策略；(b) 主线或 C# LDM 出现语言级动向。在此之前，先用 `async-sub` 的项目级默认配置（PROPOSAL E）与 analyzer 承担该场景。
4. **与 sibling 对表是前置条件。** `require-await-call`（强制显式 `Await`）与本建议方向相反，两者不得同时进入设计；取舍须在独立建议中定案。`async-iterator` / `async-event` 的 await 语义与 `Agile` 的交互，各自列为 OPEN QUESTION，本场不定。
5. **若复活 `Agile`**，前置条件：补机制设计（编译器如何实现不捕获、自定义 awaiter 行为）、补文法（BNF）与兼容性分析（`Agile` 作标识符/上下文关键字）、补与 Async Iterator/Event 交互、提供原型验证状态机生成与 IDE 步进。

### Implication:

- 将 `proposal-agile-async.md` 标注为：中间 `Await` 省略 = **LDM Rejected**；`Agile Async` = **LDM No Plans（Table）**，并在文档头部注明与 #167、`require-await-call`、`postfix-casting` 的关系。
- 给 `require-await-call` 团队发对表请求：明确"强制显式 `Await`"与"省略 `Await`"二选一的取舍，防止同一语言里两个方向并存。
- 起草一份"异步表达式链中的 Await 边界"备忘，供 `require-await` / `async-iterator` / `async-event` 团队参考：`Await` 的目标、尾成员判定、`.Result`/`.ConfigureAwait` 类 Task 成员冲突、`?.` 交互。
- 跟进主线 #37（Await in Catch/Finally）与 #167（Return?）作为本类问题的判例库。
- 为复活信号量化"库代码 `ConfigureAwait(False)` 样板"在 VBScript.NET 目标代码中的占比。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**主线对"语言级不捕获同步上下文 / 隐式 await"是否在仓库外的邮件列表或 C# LDM 讨论过，无法核实**。仓库内 `..\..\vblang\meetings/` 无相关记录（grep `ConfigureAwait`、`omitted await`、`implicit await` 仅命中 spec 的 awaiter 示例与 Async Sub 语义，无 LDM 讨论）。`Suspect`：C# 生态的 CA2007 一类 analyzer 是默认路径，但此条来自生态常识，需外部核实。
- `OPEN QUESTIONS`：省略规则在 Option Strict Off 下对 `Object` 型操作数的行为（spec 将 Object 型 await 延迟到运行时）。
- `OPEN QUESTIONS`：`Await obj?.MAsync()` 在"强制显式 Await"方向下是否成立——若 require-await 落地，这决定 `?.` 与异步的合法组合。
- `TODO`：量化 `ConfigureAwait(False)` 在真实库代码中的出现密度，为 `Agile` 复活评估补数据。
- `TODO`：撰写"异步表达式链中的 Await 边界"备忘（见 Implication）。
- `Follow-up`：若 C# 出现 await 省略或语言级 ConfigureAwait 讨论，带回 VB 重审本建议的 Table 状态。

### 状态

- **LDM 状态：中间 `Await` 省略 = LDM Rejected；`Agile Async` = LDM No Plans（Table）**，两状态互相独立。
- **三态判定：Table（整体）** — 两部分各自处置：省略 Reject（#167 先例 + 重解释既有代码 + 与 require-await 互斥），`Agile` Table（动机真实、机制未定义、生态已有替代路径、复活信号明确）。**值得记住，不值得现在做；且必须拆分后单独重新提交。**

---

## 附录：特性评价

# 建议评价报告：proposal-agile-async.md

## 评价对象

- 建议：proposal-agile-async.md — `Agile Async`（不捕获同步上下文）+ 中间 `Await` 省略
- 来源：Anthony 原文第 11 章 "Async Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1741–1753，两段示例逐字来自原文；同章还捆绑 `async-sub`/`require-await-call`/`async-iterator`/`async-event`）
- 配方目标：① `Agile` 修饰符让异步方法 await 后不捕获同步上下文（免 `ConfigureAwait(False)`）；② 链式异步调用省略中间 `Await`，配合后置转换简化链式转换

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。两个目标均无量化数据；`Agile` 的效果（免样板）止于口头，无性能/死锁数据；省略的效果依赖尚未定型的语义，示例二 `? Await client.HttpGetAsync("...")(As JsonObject)...` 含即时窗口 `?`（非合法 VB）且依赖已 Table 的后置转换，不可编译、不可演示 | 已检查（书面，无原型） | 核心语法未定型 → 效果证据封顶；示例不可编译；价值演示建立在未采纳特性上 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`Agile Async` 引入"第二种异步方法"（原则 #3），在 VB/VB6 传统无对应物；省略是全新表达式语义，直接照搬 Anthony 设计未做 VB 化；一份建议捆绑两个无关特性（声明级修饰符 + 表达式级省略） | 已检查 | 捆绑违背职责单一；`Agile` 关键字泛化、零辨识度；省略与"保持 VB-like"（原则 #2）冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、4 个未决问题具体诚实（≥4 个关键点按规则不扣分但封顶效果）；但 Detailed design 仅三个示例、无语义定案，**省略的判定规则是特性核心却整个留在 Unresolved**；无 BNF、无兼容性章节；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；未提及与主线 #167/#37 的关系 | 已检查 | 核心规则未设计；两个特性边界不清；示例不可编译（`?`）；占位链接 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。省略重解释既有合法代码（`ConfigureAwait`/`ContinueWith` 惯用法）、重载解析脆弱（库演化改求值边界）；与同组 `require-await-call` 方向互斥、与 `async-iterator`/`async-event` 交互未定义 = 一致性断裂；暗风险无应对设计 | 已检查（预测待定） | 破坏兼容风险被 Drawbacks 轻描淡写；风=与 sibling 建议方向冲突；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 第 11 章（未标章节号）；**最大偏差**：未说明 `(As Type)` 是独立的已 Table 建议（`postfix-casting`），把依赖写成自己的示例；"Conversions to task objects still allowed" 引自 sibling `require-await-call` 却未声明；未提 C# 生态用 analyzer 而非语言解决 `ConfigureAwait` 样板 | 已检查 | 来源标注不全；跨建议引用未声明；与主线 #167 判例的关联缺失 |

## 设计原则对照

- **与 VB 基因：偏离为主**——省略直接违反原则 #7（隐蔽语义变化，#167 `Return?` 先例逐字适用）；`Agile` 违反原则 #3（第二种异步方法）且命名违反原则 #2（保持 VB-like）；原则 #4（默认跟随 C#）——C# 生态用 analyzer/API 而非语言，无偏离理由；唯一命中原则 #9（消除样板）但以确定性为代价。
- **与主线关系：Anthony 独立延伸，与主线张力**——主线无"语言级不捕获同步上下文/await 省略"记录（仓库内 meetings 无相关讨论，`Suspect`）；主线对 await 的处理（2014-04-02 全语境自由但明示坏实践、2017-08-09 #37 原则上批准）都强调显式 await；与同组 `require-await-call`（强制显式）直接冲突；与 `postfix-casting`（Table）互相引用；与 `async-sub`/`async-iterator`/`async-event` 交互未定义。
- **破坏性变更：有（省略）**——任何"整条链可 await 且中间成员同时存在于 awaited 类型与 `Task` 类型"的现有代码会被重解释或报错；`Agile` 若为上下文关键字则无直接破坏，但 `Agile` 作标识符的兼容需专门分析。

## 总评

- **达成程度：未达成**——两个特性均未达到可设计状态：省略与既定判例（#167）冲突且重解释既有代码；`Agile` 机制不可指定、生态已有替代路径；且两者被无理由捆绑。
- **LDM 三态建议：Table（整体，拆分后单独处置）**——中间 `Await` 省略：**Reject**；`Agile Async`：**Table**（复活信号：通用"不捕获"机制成立，或主线/C# 出现语言级动向；先以 `async-sub` 项目级默认 + analyzer 承担场景）。
- **主要问题**：① 捆绑两个独立特性；② 省略的判定规则是核心却完全未设计，且违反原则 #7（#167 先例）；③ 省略重解释既有合法代码（`ConfigureAwait`/`ContinueWith` 惯用法），与 `require-await-call` 方向互斥；④ `Agile` 的"隐式 `ConfigureAwait(False)`"无法泛化到自定义 awaiter，编译器层无不捕获开关；⑤ 价值演示依赖已 Table 的后置转换与非法 `?` 语法。

## 返工建议

- **补充章节**：拆分声明——两个特性各自独立成建议；省略部分补文法（BNF，含尾成员判定、`?.` 交互、Task 成员冲突例外）、兼容性分析（重解释既有代码的逐例清单）、Option Strict 分叉（Object 型操作数）；`Agile` 部分补机制设计（编译器如何实现不捕获、自定义 awaiter 行为、`ConfigureAwait` 缺失错误策略）、兼容性（`Agile` 作标识符）。
- **补充证据**：可编译示例（替换 `?` 即时窗口示例，去掉对后置转换的依赖）；`ConfigureAwait(False)` 在真实库代码中的占比数据；最小原型验证状态机生成与 IDE 步进。
- **未决问题处理**：`Await obj?.MAsync()` 移交 `require-await-call` 团队（在"强制显式"方向下定义 `?.` 合法组合）；"Conversions to task objects still allowed" 边界移交 `require-await-call`；`Agile` 与 `Async Iterator`/`Async Event` 交互列入各自建议的 OPEN QUESTION。
- **设计探索**：`async-sub` 项目级默认异步类型（PROPOSAL E）能否吸收"库代码不想捕获"场景的完整需求；与 analyzer（CA2007 一类）的职责划分；若 C# 出现语言级动向，带回的评估框架（复用本建议的复活信号）。

---

## 附录：C# 生态与互操作考量

> 本附录核对 C#/CLR/.NET 生态对本提案主题的现实方向。C# 是 CLR 新特性与 .NET 生态的主要推动者（索引 T1），本提案两个半部——「async 状态机/性能」与「`Await` 显式语义」——在 C# 都有直接对应物。来源：`..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）；C# 原文**逐字**引用并标注路径；无法核实处标 **Suspect** / **OPEN QUESTIONS**。

### 相关 C# 现实方向

**1. C# 的 async 性能优化走「builder 覆写 / 运行时状态机」路线，不是「不捕获同步上下文」的新修饰符。**
- C# 10 `AsyncMethodBuilder` 方法级覆写（`proposals\csharp-10.0\async-method-builders.md`）：允许逐方法替换状态机 builder（如 `PoolingAsyncValueTaskMethodBuilder<T>`），动机是 ValueTask 池化、免 GC 分配。原文（Summary）：「Allow per-method override of the async method builder to use.」；原文（Motivation）：「…as long as no more than that number are ever returned to the pool to be pooled at the same time, `async ValueTask<{T}>` methods effectively become free of any GC allocation overhead.」——C# 解决 async 分配/性能的方式是**换 builder + 池化**。
- LDM-2020-11-11 在评审 AsyncMethodBuilder 时**显式否决**「builder 覆写顺带解决 ConfigureAwait」：「Can this solve `ConfigureAwait`? We don't think so: this controls the method builder, not the meaning of `await`s inside the method, so while it could potentially change whether a method call returns a task that synchronizes to the thread context by default, it could only do that for methods defined in your assembly, which would just lead to confusing behavior.」（`meetings\2020\LDM-2020-11-11.md`）——这正是 `Agile` 想做的「编译器级不捕获」的 C# 侧判定：**换 builder 改变不了 await 的语义**。
- LDM-2024-04-01（Async2 / runtime-handled tasks，`meetings\2024\LDM-2024-04-01.md`）：运行时实验把状态机生成移进 runtime，SyncContext/AsyncLocal 行为做成可配置。原文：「This issue may end up being the tipping point that forces an assembly-wide configuration solution, as we don't want to force developers to realize the `Task` return by calling `ConfigureAwait`.」；「One idea that was floated repeatedly was making this configurable; we could opt for behavior-preserving semantics by default, and let users opt-in to the breaking change if they chose to do so.」；「We have very few of these [configuration flags] in C#, intentionally, as we don't want to create language dialects.」——C# 对「库代码不想捕获上下文」的现实答案是**程序集级可配置默认**（≈ 本提案 PROPOSAL E），且对造新开关/方言极度谨慎。
- LDM-2019-07-17：「ConfigureAwait and related issues have been brought up many times. We think it's worth addressing, and should look at possible designs.」（`meetings\2019\LDM-2019-07-17.md`）——ConfigureAwait 是 C# LDM 反复出现、始终在设计视野内的议题。

**2. C# 讨论过「await 链式化」并拒绝；未讨论过「省略/隐式 await」。**
- LDM-2020-11-11 评审 #4076「Add `await` as a dotted postfix operator」：「While we are sympathetic to the desire to make awaits more chainable, and the `.` can be viewed as the pipeline operator of the OO world, we don't think this solves enough to make it worth it. Chainability of `await` expressions isn't the largest issue on our minds with `async` code today: that honor goes to `ConfigureAwait`, which this does not solve.」结论：「Rejected. We do like the space of improving `await`, but we don't think this is the way.」（`meetings\2020\LDM-2020-11-11.md`）
- 本提案的中间 `Await` 省略与 #4076 动机同向（让 await 可链式），但形态更激进：C# 连「把 await 做成**显式后缀操作符**」都拒绝，本提案要**删掉**显式 `Await`。`meetings\` 目录 grep `implicit await` / `omitted await` 零命中——C# 没有「省略 await」的语言级讨论。

**3. C# 对 async 的演进是「放宽限制」+「运行时性能」，`await` 语义保持稳定。**
- C# 13 `ref-unsafe-in-iterators-async`（`proposals\csharp-13.0\ref-unsafe-in-iterators-async.md`）：async/iterator 在无 await/yield 段内可用 ref/ref struct/unsafe。原文（Motivation）：「It is not necessary to disallow `ref`/`ref struct` locals and `unsafe` blocks in async/iterator methods if they are not used across `yield` or `await`, because they do not need to be hoisted.」
- C# 14 候选 `async-method-ref-parameters`（`proposals\async-method-ref-parameters.md`）：把放宽扩展到 async 方法的 ref/in/out/ref-like **参数**，同 await 边界语义。原文（Summary）：「…extending the relaxation of `ref` and `ref struct` variables in `async` methods to method _parameters_ as well, with the same overall semantics.」
- 方向本质：C# 让 async 方法能做更多低层/ref 的事（性能与互操作），但 `Await` 的显式语义、状态机划分、异常边界不变——**与本提案「隐藏 await 边界」方向相反**。

**4. null-conditional await（`await?`）是 C# 候选，保持 `Await` 显式。**
- `proposals\null-conditional-await.md`（Summary）：「Support an expression of the form `await? e`, which awaits `e` if it is non-null, otherwise it results in `null`.」其动机示例正是本提案 OPEN QUESTION 的形态：`await GetX()?.DoSomethingAsync()` 改为 `await? GetX()?.DoSomethingAsync()`。
- 状态：LDM-2022-08-31（triage）：「This is our second-highest upvoted `Any Time` milestone issue, and continues C#'s operator monad over nulls (along with the previous issue). It's on the list.」（`meetings\2022\LDM-2022-08-31.md`）——Any Time 里程碑，未排期。

### 现实 vs 提案

| 本提案半部 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| 中间 `Await` 省略 | #4076 显式后缀 await（Rejected）；无 omitted/implicit await 讨论 | **冲突（同向动机，C# 已拒，且本提案更激进）** | C# 连显式后缀形式都拒绝（「chainability… isn't the largest issue… that honor goes to `ConfigureAwait`」）；省略是重解释既有合法代码（同源码改绑定），比 #4076 的「新增显式后缀操作符」破坏面更大。本场 Reject 获得 C# 判例直接佐证 |
| `Agile Async`（不捕获同步上下文） | LDM-2024-04-01 assembly-wide 可配置默认；LDM-2020-11-11 明确 builder 覆写不能改变 await 语义 | **需桥接（同目标，异手段）** | C# 的「不捕获」答案是**程序集级配置 + 运行时状态机**（≈ 本提案 E），不是逐方法修饰符；C# LDM 已否掉「换 builder 实现不捕获」。C# 动向支持 E 而非 A |
| 状态机性能 / 分配（AsyncMethodBuilder 池化、async2） | builder 覆写 + 运行时状态机 | **兼容（可搭便车）** | C# 在 builder/runtime 层解决分配与恢复性能，与「不捕获上下文」正交；.vbx 状态机生成若跟随 C# emit 约定即可受益 |

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：`Agile` 的「库代码不想捕获上下文」场景，C# 现实方向是**程序集级可配置默认 + analyzer/API 纪律**；.vbx 应跟随该方向（若 C# 落地 SyncContext 默认配置的 attribute / runtime flag，.vbx 库代码直接受益），不发明 `Agile` 修饰符。复活信号 (b)「C# 出现语言级动向」已被 LDM-2024-04-01 部分兑现，但兑现形态是**配置而非修饰符**——指向 E 而非 A。
2. **source-gen / emit 桥**：async 性能方向（AsyncMethodBuilder 池化、async2）主要在 builder/runtime/JIT 层；.vbx 编译器需**识别 `[AsyncMethodBuilder]`、`AsyncStateMachineAttribute`、`PoolingAsyncValueTaskMethodBuilder` 等元数据**，确保 VB async 方法与 C# 生成的状态机/builder 互通；若 .vbx 允许自定义 builder（`AsyncMethodBuilderAttribute` 用于方法），需对齐 C# 10 的规则（builder 的 `Create` / `Start` / `SetResult` / `Task` 协议）。
3. **识别新元数据 / 消费 C# async 方法**：C# 13/14 放宽 async 的 ref/ref-like（含参数）后，C# 库可能暴露带 ref/ref-like 参数的 async 方法；VB 的 ref struct 支持目前是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error，.vbx 至少应正确报错或桥接，而非误解析；与决策文件 M8 的 `RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute` 识别同属「认识新元数据」清单。
4. **`Await?` 跟进评估**：本提案 OPEN QUESTION 的 `Await obj?.MAsync()`，若「强制显式 Await」方向落地，可对标 C# 的 `await?` 候选（保持 `Await` 显式 + null 条件语义），不必走「省略」路线。

### 对既有 RESOLUTION / 三态判定的影响

- **判定不变，证据升级。** RESOLUTION #2（省略 Reject）与 #3（`Agile` Table）维持；C# 侧记录为本场此前缺失的外部佐证补位：
  - 省略：C# #4076 已 Reject await 链式化（动机同向、形态更温和），在 `Return?` 判例之外新增 C# 判例。
  - `Agile`：复活信号 (b)「C# LDM 出现语言级动向」已出现（LDM-2024-04-01），但动向是 **assembly-wide 配置（支持 E）**，不是信号 (a) 的「编译器层通用不捕获机制」。**复活信号收窄：优先跟踪 C# assembly-wide SyncContext 配置的落地，评估 E 而非 A。**
- **OPEN QUESTION 部分关闭。** 本场 OPEN QUESTION「C# LDM 是否讨论过语言级不捕获 / 隐式 await」——**已核实 C# 侧**：讨论过 ConfigureAwait（LDM-2019-07-17、LDM-2020-11-11、LDM-2024-04-01 等）与 await 链式化（#4076，Rejected）；**未讨论过省略/隐式 await**（grep 零命中）。「C# 生态用 CA2007 一类 analyzer」仍为生态常识（CA2007 属 dotnet/roslyn-analyzers，csharplang 仓库无正文），标 **Suspect**；但「C# 不用语言级方案解决 ConfigureAwait」已由 LDM 记录证实。
- **不改变三态。** 整体仍为 Table（拆分处置：省略 Reject / `Agile` Table）；C# 动向未提供支持 `Agile` 修饰符的机制，也未动摇省略的判例基础。

### 引用纪律

- 上文所有 C# 原文均**逐字**引自 `..\..\csharplang` 并标注路径；本附录核实的原文与出处如下，后续 meeting agent 可直接复用：
  - 「Allow per-method override of the async method builder to use.」→ `proposals\csharp-10.0\async-method-builders.md`（Summary）
  - 「…as long as no more than that number are ever returned to the pool to be pooled at the same time, `async ValueTask<{T}>` methods effectively become free of any GC allocation overhead.」→ `proposals\csharp-10.0\async-method-builders.md`（Motivation）
  - 「Can this solve `ConfigureAwait`? We don't think so: this controls the method builder, not the meaning of `await`s inside the method, so while it could potentially change whether a method call returns a task that synchronizes to the thread context by default, it could only do that for methods defined in your assembly, which would just lead to confusing behavior.」→ `meetings\2020\LDM-2020-11-11.md`
  - 「While we are sympathetic to the desire to make awaits more chainable, and the `.` can be viewed as the pipeline operator of the OO world, we don't think this solves enough to make it worth it. Chainability of `await` expressions isn't the largest issue on our minds with `async` code today: that honor goes to `ConfigureAwait`, which this does not solve.」→ `meetings\2020\LDM-2020-11-11.md`（#4076，结论 Rejected）
  - 「This issue may end up being the tipping point that forces an assembly-wide configuration solution, as we don't want to force developers to realize the `Task` return by calling `ConfigureAwait`.」→ `meetings\2024\LDM-2024-04-01.md`
  - 「We have very few of these [configuration flags] in C#, intentionally, as we don't want to create language dialects.」→ `meetings\2024\LDM-2024-04-01.md`
  - 「It is not necessary to disallow `ref`/`ref struct` locals and `unsafe` blocks in async/iterator methods if they are not used across `yield` or `await`, because they do not need to be hoisted.」→ `proposals\csharp-13.0\ref-unsafe-in-iterators-async.md`（Motivation）
  - 「…extending the relaxation of `ref` and `ref struct` variables in `async` methods to method _parameters_ as well, with the same overall semantics.」→ `proposals\async-method-ref-parameters.md`（Summary）
  - 「Support an expression of the form `await? e`, which awaits `e` if it is non-null, otherwise it results in `null`.」→ `proposals\null-conditional-await.md`（Summary）
  - 「This is our second-highest upvoted `Any Time` milestone issue, and continues C#'s operator monad over nulls…」→ `meetings\2022\LDM-2022-08-31.md`

### OPEN QUESTIONS / TODO（本附录）

- `OPEN QUESTIONS`：CA2007 一类 analyzer 作为「C# 生态默认路径」的具体规则文本属 dotnet/roslyn-analyzers，本仓库无正文，未核实。**Suspect**。
- `OPEN QUESTIONS`：`await?` 是否完整覆盖 `obj?.MAsync()` 的空短路形态（提案正文以 `GetX()?.DoSomethingAsync()` 示例，`?.` 链 + `await?` 组合的规范细节未在 LDM 定案）。
- `OPEN QUESTIONS`：async2（runtime-handled tasks）是否会落地、其 SyncContext/AsyncLocal 可配置项的具体形状（attribute or runtime flag）——仍属 dotnet/runtimelab 实验，本仓库仅有 LDM-2024-04-01 的会议记录与 presentation 引用。
- `TODO`：跟踪 C# `await?` 与 assembly-wide ConfigureAwait 配置的状态，作为本场复活信号的客观触发器。
