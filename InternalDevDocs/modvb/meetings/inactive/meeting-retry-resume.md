# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周这份建议来自 Anthony 第 18 章"实验性想法"（`inactive/`），但它和上一份 `Try` 增强一样，**标题看起来像一个语法提案，实际上是一条论题**——作者没有提出任何 `Retry`/`Resume` 关键字，只抛出一个弃用论题："如果 `Try` 能覆盖 `Resume Next` 和 `Goto` 重试这两类场景，非结构化错误处理（`On Error` 家族）就可以被弃用。" 我们走进会议室时带着一个强烈的预感：这条论题的论据部分被 spec 和主线历史验证过，部分站不住；而"弃用"本身不是语言特性，是工具链工作。会议结果证实了这个预感——我们花了整场在拆论点，而不是背书语法。

## Agenda

* [Proposal: `Retry`/`Resume`（重试与恢复——弃用非结构化错误处理的论题）](#proposal-retryresume-重试与恢复弃用非结构化错误处理的论题)

## Proposal: `Retry`/`Resume`（重试与恢复——弃用非结构化错误处理的论题）

_Related: [vblang #37 – Champion "Await in Catch and Finally"](https://github.com/dotnet/vblang/issues/37)；[vblang #48 – `Exit For j`](https://github.com/dotnet/vblang/issues/48)；主线纪要 `2017.08.09`（`GoTo retry` 重试惯用法出处）、`2018.02.21` / `2018.02.28`（GoTo/OnError 与 `Await` 的张力）；ModVB：`proposal-try-enhancements.md`（`Await` in `Catch`/`Finally`，Active）、`proposal-typeof-flow-analysis.md`（`TypeOf` 流分析，Active）、`proposal-do-nothing.md`；Anthony 原文 §18.6（`..\..\AnthonyDesign_wordpress.txt` L2869–2902）_

### 场景与缺口

We started from Anthony 的原话，它是整份建议唯一的事实主张：

> "The only scenarios where I can see using `On Error` today is for `Resume Next` or retrying with `Goto`. If we can hit that with `Try` that kind of unstructured error handling can be deprecated."

We 把这句话拆成三个可检验的主张：

**主张一：`On Error` 的使用只剩两类场景。** 这条与 spec 对不上。VB.NET 规范（`..\..\..\vblang/spec/statements.md` §Unstructured Exception-Handling Statements）明确定义非结构化错误处理是**三个**语句、**四 + 三**种形态：

> "Unstructured exception handling is implemented using three statements: the `Error` statement, the `On Error` statement, and the `Resume` statement."

其中 `On Error` 有四种写法（`GoTo -1` / `GoTo 0` / `GoTo LabelName` / `Resume Next`），`Resume` 有三种写法（`Resume` / `Resume Next` / `Resume LabelName`）。Anthony 的两类枚举**漏掉了**：`On Error GoTo Label` 的"独立处理器块"形态、`Resume`（返回出错语句）形态、`Resume Label`、`On Error GoTo 0` / `GoTo -1`，以及整个 `Err` 对象。这不是咬文嚼字——见主张二与 `Resume` 残留区。

**主张二：`Try` 能覆盖这两类场景。** 重试——**成立**，而且主线早就见过同一个例子（2017.08.09 纪要讨论 `Await` in `Catch` 时，示例逐字就是 `Dim retryCount = 0 / Try / retry: / Catch ex As Exception When retryCount < 3 / retryCount += 1 / GoTo retry / End Try`，见下）。`Resume Next`——**只部分成立**：空 `Try/Catch` 序列是"更糟"的机械翻译（Anthony 自己承认 "This is worse but likely uncommon"），且**`Resume`（非 Next）在结构化异常处理里根本没有对应物**。

**主张三：因此可以弃用 `On Error`。** 这是**跳步**。弃用是一个破坏性变更 + 迁移工程，不是语言特性；"`Try` 覆盖了两个场景"不自动等于"可以弃用"。We 尤其在意：VBScript（VBScript.NET 的直系祖先）**没有 `Try/Catch`，`On Error Resume Next` 是它唯一的错误处理**。对目标语言来说，`Resume Next` 不是"uncommon"，恰恰是遗产代码的主流。这个视角在建议原文里完全缺席。

### 候选方案

**PROPOSAL A — Anthony 原文（零新语法）：增强 `Try` 覆盖重试与 `Resume Next`，然后弃用 `On Error`。**

```vb
' 重试惯用法（Anthony §18.6 原样；与主线 2017.08.09 示例逐字同构——大小写不同，VB 不区分）。
Let retryCount = 0
Try
    Retry:
    DoWork()
Catch ex As Exception When retryCount < 3
    retryCount += 1
    GoTo Retry
End Try
```

`Resume Next` 的空 `Try/Catch` 模拟：

```vb
' Anthony 的空 Try/Catch 序列（原样，作者自评 "This is worse but likely uncommon"）。
Try
    ' Step 1
Catch
End Try

Try
    ' Step 2
Catch
End Try

Try
    ' Step 3
Catch
End Try
```

**PROPOSAL B — 新结构化 `Retry` 关键字（假想，仅作反例）。** 在 `Catch` 内写 `Retry` 重新执行 `Try` 块：

```vb
' 假想的 `Retry` 关键字（PROPOSAL B）——本场不采纳，仅作反例。
Try
    DoWork()
Catch ex As Exception When attempt < 3
    attempt += 1
    Retry
End Try
```

**PROPOSAL C — 结构化重试惯用法（`For` + `Try/Catch` + `Catch ... When`），零新语法。** 这是本场我们发现、并认为优于 A 的重试写法：

```vb
' 结构化重试：零 Goto，第三次失败时 When 为假，异常自然向外传播。
For attempt As Integer = 1 To 3
    Try
        DoWork()
        Exit For
    Catch ex As Exception When attempt < 3
        ' 吞掉本次异常，进入下一轮尝试。
    End Try
Next
```

**PROPOSAL D — 库级重试（如 Polly `RetryPolicy`），语言不动。**

**PROPOSAL E — 弃用路线图作为工具链工作项。** 编译器默认不碰存量 `On Error`；新代码教学/文档/模板停教 `On Error`；用一个 analyzer + code fix 把 `On Error Resume Next` 迁移到 `Try/Catch`。语言特性面为零。

### 权衡：Q&A

- **A vs C：`GoTo Retry` 真的 "isn't that bad" 吗？** Anthony 自评如此，We 不同意。第一，`Goto` 本身就是主线想减少的代码味道——2017.12.06 纪要原话："The real motivator for this is that `Goto` is a code-smell and people want a less smelly solution to this problem."（那是 `Exit For j` 的动机，但同一句判断适用于这里）。第二，C 的 `For`+`Try`+`When` 把"重试 N 次、第 N+1 次失败向外传播"的语义**写进结构**，不需要读 `Catch` 体才知道它在重试；Anthony 自己都承认要"数"逻辑散落在 `Catch` 与标签之间。第三，也是最硬的：**`GoTo` 从 `Catch` 跳回 `Try` 与 `Await` in `Catch` 互斥**（见下），C 形态是唯一同时兼容异步的答案。**结论：重试不需要 A 的 Goto，更不需要 B 的关键字，C 已经存在。**
- **A 的空 `Try/Catch` 能"干净地"模拟 `Resume Next` 吗？** 不能。三个层面的问题：(1) **粒度**——`Resume Next` 是**过程级模式**（spec：整个方法建立一个单一处理器，跟踪"最近的异常处理位置"），错误发生在任何一条语句后都自动续到下一句；空 `Try/Catch` 必须**手工**包住每一条可疑语句，你必须在写代码时就知道哪里会出错——这恰恰是 `Resume Next` 想让作者不必知道的东西。(2) **吞没一切**——空 `Catch` 吞 `OutOfMemoryException`、`StackOverflowException` 等致命异常，`Resume Next` 也吞，所以**吞没语义等价**；但空 `Catch` 把"静默吞没"变成代码里看得见的形态，立刻触发"catch a more specific exception"一类的分析器告警。等价地糟糕，但**可见性不同**。(3) **它不演示新能力**——与上一场 `Try` 增强里 `For Each ... Finally p.Kill()` 的判定同构：这些空块今天就能写，只是没人愿意写。
- **主张一为什么错？`Resume`（非 Next）的残留区。** 结构化 EH 无法"在出错的那条语句处恢复执行"——CLR 的结构化异常处理没有"resume at faulting instruction"；VB 编译器对 `On Error` 家族的实现是"整方法一个 catch-all + 语句边界重入"（spec 原话）。`Resume` 回到出错语句、`Resume Label` 回到标签，这两者**没有 `Try/Catch` 等价物**：

```vb
' `Resume`：返回"出错的那条语句"重新执行——结构化 Try/Catch 无对应物。
Sub MigrateWithResume()
    On Error GoTo Handler
    DeleteTempFile()            ' 若此处失败……
    Exit Sub

Handler:
    CleanupPartialState()
    Resume                      ' ……重新执行 DeleteTempFile()。
End Sub
```

  `Resume` 不能靠"循环块"模拟——重跑整个块 ≠ 重跑出错的那一条语句（属性 getter 会重新求值，块内其它语句会重复执行）。这是主张二/主张三共同的地基裂缝。We 把它记为 `Resume` **残留区**：只要它存在，`On Error` 就无法整体弃用。
- **B：为什么我们连反例都不想认真设计？** 三条硬理由。(1) **保留字成本**——`Resume` 已经是保留字；`Retry` 目前**不是**保留字（spec 文法里无 `Retry` 关键字），引入 `Retry` 会破坏所有用 `Retry` 作标识符的存量代码，而且**恰好破坏当前重试惯用法的标签名**——`Retry:` 标签在 `Retry` 成为保留字后就是非法标识符。(2) **第二种做事方式**——VB 已经有 `Try`、有 `On Error`、有 `GoTo`，再加第三种重试构造是原则 #3 的教科书案例。(3) **零需求信号**——没有任何 issue、没有任何数据请求它；Anthony 自己都没提。(4) **异步不兼容**——见下。
- **A/B vs 2018.02.28 的异步决议。** 这是本场与主线的接缝。主线 2018.02.21 讨论 `Await` in `Catch` 时原话："In general the work is similar to C#. But VB specific things, GoTo and OnError may make it more difficult." 以及 "GoTo and OnError seem unlikely in "modern" code that would use Async. Perhaps we could narrow the scenarios to simplify implementation." 一周后 2018.02.28 落定：

  > "It is legal in VB (but not C#) to jump from inside a Catch to inside the Try. Await in catch in C# was implemented without support for this (obviously), and we aren't quite sure how to do it because nested Try/Catch create some interesting possibilities."
  > "If we drop the difficult scenarios and provide support and disallow Await and these jumps to happen together, this is doable."
  > "Let's do it!"

  换句话说：**`GoTo` 从 `Catch` 跳回 `Try` 的重试惯用法，在含 `Await` 的 `Try` 内被主线决议明令禁用**。我们上一场（`Try` 增强）已采纳这条限制。所以 B 若存在，其异步形态要么禁止 `Retry`（自废一半武功），要么重新打开那个"我们不太确定怎么做"的嵌套状态机问题。**结论：任何重试语法都必须在异步下给出定义，而主线已经证明同步重试不需要新语法（C）。**
- **E vs 编译器弃用警告：为什么不直接对存量 `On Error` 发警告？** 主线原则 #1（2018.06.13 纪要原话）："We will almost never make breaking changes to Visual Basic (often resulting in _Rejected_ label)"。对存量 `On Error` 发警告，等于宣布千万级 VB6/VBScript 迁移代码"过时"，而其中相当一部分（`Resume`、`On Error GoTo Label`）**没有迁移路径**——警告只会制造恐慌。`On Error` 与 async/iterator/lambda 的互斥是 spec 强制的自然边界（见下），不需要编译器额外"弃用"。**结论：E 用 analyzer 只在用户主动迁移时介入，编译器闭嘴。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

建议原文零新语法，文法问题全部落在假想 B 上。`Resume` 已是保留字（不能当标识符用）；`Retry` 不是（引入即破坏 `Retry` 标识符）。若 `Retry` 做成**上下文关键字**，则 `Catch` 块里的裸 `Retry` 语句与"调用名为 `Retry` 的方法"产生歧义——`Retry(x)` 是关键字加参数还是方法调用？VB 的保留字策略历来是"全保留"而非"上下文"（`Resume` 就是全保留），上下文关键字的先例少而脆弱。We 不打算为一条无需求的反例设计文法，此问留档即可。

#### 2. 角案例与边界语义

**A 的 `GoTo Retry` 重试：`Finally` 每轮重跑。** 若 `Try` 带 `Finally`，每次 `GoTo Retry` 重入都会再跑一遍 `Finally`——清理执行 N+1 次。语义上正确（每次尝试都是全新的一次 `Try`），但必须文档化；C 的 `For`+`Try` 形态同样如此，无差异。

**`Catch` 体内抛新异常。** `retryCount += 1` 不会抛，但若 `Catch` 里写日志调用并抛新异常，该异常从 `Catch` 直接向外传播，**不会**重入 `Try`——`GoTo` 在异常前不会被到达。这是"重试只重试 `Try` 体，不重试 `Catch`"的天然边界，A 与 C 一致。

**重试上限的 off-by-one。** A 与 C 语义相同：第 1..N 次失败被 `When` 放行重试，第 N+1 次失败 `When` 为假，异常向外传播。A 中 `retryCount` 在 `Catch` 体内递增、过滤器在**下一次异常进入时**看到新值——顺序正确。**代价**：两种写法都要手工维护计数器；C 把计数器写进 `For` 头，少一个变量。这是 C 胜 A 的一个具体点。

**空 `Catch` 吞致命异常。** 见 Q&A。`Resume Next` 与空 `Try/Catch` 在"吞什么"上等价，但空 `Catch` 在代码审查里显形。Anthony 说 `Resume Next` 场景 "likely uncommon"——We 反驳：在 VBScript 遗产里它是唯一形态；在 VB.NET 里无数据（`Suspect`）。

**`On Error GoTo 0` / `GoTo -1`。** spec：`GoTo -1` 把最近的异常重置为 `Nothing`，`GoTo 0` 把处理器位置重置为 `Nothing`。这两条是"关闭当前处理器"的运行时操作，`Try/Catch` 没有对应物——但它们的迁移是平凡的（删掉即可），不是残留区。

**`SyncLock` / `Using` 的单语句处理。** spec 规定 `SyncLock`/`Using` 隐含结构化 EH，对 `Resume`/`Resume Next` 视作单语句：`Resume` 回块首、`Resume Next` 到块尾。空 `Try/Catch` 逐语句包裹的粒度与它不同——再一次证明机械翻译不成立。

#### 3. 作用域与绑定

建议零新绑定。A 的 `Retry:` 是普通标签（绑定为标签符号）；`GoTo` 从 `Catch` 跳入 `Try` 体是**同一 `Try` 语句内部**的跳转——spec 说 "It is not allowed to `GoTo` into a `Try`, `Using`, `SyncLock`, `With`, `For` or `For Each` block" 指的是从**该语句外部**跳入；`Catch` 是 `Try` 语句的一部分，Catch→Try 体跳转合法（2018.02.28 也确认 "It is legal in VB ... to jump from inside a Catch to inside the Try"）。这两条不矛盾，We 把它们并排记录，因为"spec 禁止 GoTo 进 Try"常被误读为连重试惯用法一起禁。`retryCount`/`attempt` 是普通局部变量，无新作用域规则。

#### 4. 与既有特性的交互

- **`Await` in `Catch`/`Finally`（姊妹建议，Active）**：`GoTo`-from-Catch-to-Try 在含 `Await` 的 `Try` 内被 2018.02.28 决议禁用。重试惯用法的异步形态**只有 C 可用**。
- **`On Error` 与 async/iterator/lambda（spec 强制互斥）**：spec 原话 "Unstructured error handling statements are not allowed in iterator or async methods."；`On Error GoTo LabelName` 不能用于含 lambda 或查询表达式的方法；多行 lambda 体内 "`On Error` and `Resume` statements are not allowed, although `Try` statements are allowed."。**这意味着主线 2018.02.21 的假设 "GoTo and OnError seem unlikely in "modern" code that would use Async" 不是猜测，是规范强制**——异步/迭代器/lambda 代码里 `On Error` 根本进不去。这是弃用叙事的**自然边界**，但反过来说，弃用叙事**不能**指望"异步化会自然淘汰 `On Error`"——VBScript 迁移代码大量是同步遗留。
- **晚期绑定 / `Option Strict Off`**：`Resume Next` + `Err.Number` 的"特征探测"模式几乎全是晚期绑定（`On Error Resume Next / result = customer.Name / If Err.Number = 0`）。结构化替代是 `TypeOf`/`TryCast`——而这正是我们 `TypeOf` 流分析（Active）消灭的样板。**两条工作项在这里汇合**：`Resume Next` 探测模式的迁移路径依赖 `TypeOf`/`TryCast` 收窄的落地。
- **`GoTo` / `Exit For j`（主线 #48，Approved-in-Principle）**：2017.12.06 明确 `Goto` 是 code-smell、`Exit For j` 是为了"less smelly"。重试惯用法是少数**正当**的 `GoTo` 用途之一——主线 2017.12.06 也说 "We use `Goto` all over the scanners and parsers ... `Goto` seems the most appropriate way of expressing that control flow"。We 承认它在同步重试里正当，但这不构成"为它造语法"的理由（C 已零语法覆盖）。
- **`Using` / `SyncLock`**：见第 2 问的单语句处理。

#### 5. Breaking change 与兼容性

建议原文零语法 → 编译层面**零破坏**。真正的破坏性分析在"弃用"这一步：

- **弃用 `On Error`（警告或报错）**：破坏 VB6/VBScript 迁移代码，且 `Resume` 残留区无迁移路径。破坏性**极大**，与主线 #1 冲突。最多只能接受 analyzer 主动迁移。
- **B 的 `Retry` 关键字**：`Retry` 目前非保留字，引入即破坏用 `Retry` 作变量/方法/标签名的代码，**并且破坏当前重试惯用法自己的 `Retry:` 标签**。这是建议原文完全没有分析的一类破坏。
- **`Resume` 复用**：`Resume` 已有语义（返回出错语句），给它赋新义是语义破坏，不做。

**结论**：这份建议真正的 breaking-change 面不在"做什么"，在"弃用什么"。文档没写这一段，是重大缺失（评价标准红旗：无兼容性/breaking change 分析）。

#### 6. Option Strict / 编译选项分叉

无语法分叉（A/C 都是现有语法）。真正分叉的是**迁移策略**：`Option Strict On` 禁晚期绑定，所以 `Resume Next` 探测模式在 Strict On 代码里**本来就几乎不存在**——特征探测在 Strict On 下必须用 `TypeOf`/`TryCast`（`If TypeOf customer Is IHasName Then ...`）。因此弃用叙事可以按 Option Strict 分层：Strict On 的 `On Error` 存量（多为 `GoTo` 处理器块与 `Resume`）与晚期绑定无关，仍在；Strict Off 的 `Resume Next` 探测存量随晚期绑定一起被 TypeOf/TryCast 迁移。**行为一致性要求**：A/C 重试写法在两条路径下行为一致（整数计数器，无晚期绑定），无额外要求。

#### 7. IDE / IntelliSense 影响

- **空 `Catch` 已是分析器目标**：机械迁移把 `Resume Next` 变成空 `Catch`，会触发现有告警——这是"机械翻译 ≠ 推荐迁移"的 IDE 侧证据。
- **code fix 是真正价值**：`On Error Resume Next` → `Try/Catch` 的 code fix 需要处理 `Err` 对象映射（`Err.Number`/`Err.Description` → 异常属性）、探测模式的 `If Err.Number = 0` 改写、`On Error GoTo Label` 处理器块的重排。这是工具链工作项，不是语言工作项。
- **调试器**：`Resume Next` 与空 `Catch` 都会让异常在用户眼中"消失"；first-chance exception 设置是唯一的可见性窗口。无新设计。

#### 8. 数据 / 普遍性

- **`On Error` 使用率：零数据。** Anthony 的 "The only scenarios where I can see..." 是一个语言设计者的个人抽样，不是统计。`Suspect`：VB.NET 真实代码库里 `On Error Resume Next` 与 `On Error GoTo` 的比例、`Resume`（非 Next）的占比，我们都不知道。
- **VBScript 遗产：`Resume Next` 是主流。** VBScript 没有 `Try/Catch`，`On Error Resume Next` + `Err.Number` 是它唯一的错误处理。对 VBScript.NET 而言，这条建议的"uncommon"前提**反向成立**。这是普遍性追问里最重的一锤。
- **主线从未 champion 弃用 `On Error`**：vblang 提案目录里没有任何弃用提案；主线只在 async 语境碰过它。方向是 Anthony 独立延伸。

#### 9. 更简替代

- **重试**：C（`For`+`Try`+`When`）是现有语法；D（Polly）是库级替代。两者都比 B 简单，且 B 与异步互斥。**A 的 `GoTo` 形态是三者里最差的**——虽然正当，但把重试语义埋在标签与 `Catch` 之间。
- **`Resume Next` 探测**：`TypeOf`/`TryCast`/接口测试（Strict On）；空 `Try/Catch`（Strict Off 但没有更好答案时的过渡）。后者靠 analyzer 劝退。
- **弃用本身**："什么都不做"是**真**可行的选项——保留 `On Error`，停止教它。主线 2014.02.17 的 RESOLUTION 原话 "We're proud not to do anything." 的语气在这里适用：语言不需要为弃用一个正在被 spec 边界自然孤立的遗留构造做任何事。
- **B 没有替代品**——因为它没有需求。

#### 10. 复杂度 / 成本 / 优先级

- **B（`Retry` 关键字）**：成本高（保留字 + 新控制流 + 异步状态机交互），价值低（无需求、C 已覆盖同步、异步被决议封死）。**Reject。**
- **A 的 `GoTo` 惯用法**：零成本（已存在），但作为"推荐形态"价值低于 C。文档化即可。
- **E（工具链）**：成本中（analyzer + code fix + 文档），价值对 VBScript.NET 真实（遗产迁移），但不是语言特性——进工具链 backlog，不进 LDM Active。
- **优先级**：低于所有 Active 特性（`Await` in `Catch`/`Finally`、`TypeOf` 流分析）。本条目**不产生任何编译器工作**。

#### 11. 运行时 / CLR 硬约束

- **`Resume` 无 IL 形态**：CLR 结构化 EH 没有 "resume at faulting instruction"；VB 编译器用"整方法单一 catch-all + 语句边界重入"模拟（spec）。这是 `Resume` 残留区的根因，也是主张三的硬性否决项。
- **`Resume Next` 同理**：它也是语句边界重入，只是目标改为下一条语句；空 `Try/Catch` 的"吞掉并继续"是语义最接近的结构化近似，但粒度不同（见第 2 问）。
- **无新 IL**：A/C 都是现有语法，不产生新 IL，PEVerify 无碍。
- **表达式树 / lambda**：spec 已禁 `On Error`/`Resume` 于多行 lambda，无需新处理。

#### 12. 值不值得做

| 方案 | 价值 | 成本 | 风险 | 判定 |
|------|------|------|------|------|
| A（零语法 + 弃用论题） | 中（弃用是清洁目标） | 低（零语法） | 高（弃用本身破坏性） | **论题成立一半，弃用不成立** |
| B（`Retry`/`Resume` 关键字） | 低（无需求） | 高（保留字 + 控制流 + 异步） | 高（破坏 `Retry` 标识符、与 async 决议冲突） | **Reject** |
| C（`For`+`Try`+`When` 重试） | 中高（真实惯用法） | 零 | 零 | **已存在，采纳为推荐形态** |
| E（工具链弃用路线图） | 中（遗产迁移） | 中 | 低 | **转工具链，语言面 Table** |

**总体：不值得为语法做任何事；值得为迁移做工具链，前提是有数据。**

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：弃用 `On Error` 直接违反；警告也不行（`Resume` 残留区无迁移路径）。E 的 analyzer 主动迁移是唯一不违反的形态。
- **保持 VB-like（原则 #2）**：`On Error Resume Next` 是 VB6 遗产，本身"像 VB"；但现代 VB 的形态是结构化 `Try`。B 的 `Retry`/`Resume` 关键字既不像 C#（没有），也不像 VB（没有先例）。
- **不引入"第二种做事方式"（原则 #3）**：B 直接违反——重试第三种写法（`GoTo`、`For`+`Try`、关键字）。主线 2018.06.13 的原话把这条的门槛钉得很高："Our observation is that the vast majority of Visual Basic users are not asking for us for new features so our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea." A/C 零新语法，不违反。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 从未有非结构化 EH，弃用方向与 C# 一致；C# 也没有 `Retry` 关键字，不造语法跟随 C#。**这条完全对齐。**
- **读起来像英语、对新手友好（原则 #5）**：诚实说，`Resume Next` 和 `Retry` 读起来都像英语——这是它们残留的"VB 味儿"论据。但"读起来像英语"的服务对象是**理解**：`For attempt As Integer = 1 To 3 / Try / DoWork()` 比 `Catch ... When retryCount < 3 / retryCount += 1 / GoTo Retry` 更好懂，因为结构自解释。
- **不为边缘场景加特性（原则 #6）**：`On Error` 存量在缩小（`Suspect`，无数据）、`Resume` 是边缘中的边缘。B 为边缘加特性，违反。
- **避免隐蔽的控制流/语义变化（原则 #7）**：`Resume Next` **本身**就是隐蔽控制流的教科书案例——错误后静默跳到下一条。**弃用它符合原则，但绝不可用新语法复制它。** 这是本建议最讽刺也最重要的一条：A 的方向（用结构化的 `Try` 取代非结构化）是对的，B 的方向（造新语法）是错的，因为任何 `Retry`/`Resume` 新语法都在**复刻**要消灭的隐蔽控制流。
- **不与既有语法冲突（原则 #8）**：`Resume` 已是保留字；`Retry` 若保留破坏标识符（含 `Retry:` 标签本身）。
- **消除常见样板（原则 #9）**：重试样板（计数器 + `GoTo`）被 C 以零语法消除；探测样板（`On Error Resume Next` + `Err.Number`）被 `TypeOf`/`TryCast`（Active）消除。**样板消除不靠新语法，靠既有结构。**
- **冗长只在有用时是美德（原则 #10）**：`Try/Catch` 冗长但清晰；`Resume Next` 简洁但隐蔽。VB 的判据向来是"帮助理解时才保留冗长"。
- **与主线关系（对照表 2.3）**：主线**无**弃用 `On Error` 提案（对照表无此条目），本建议是 **Anthony 独立延伸**。方向与主线 2018.02.21 的假设一致（"GoTo and OnError seem unlikely in "modern" code that would use Async"，且已被 spec 强制为事实），与主线 2018.02.28 的 `Await` 决议互补（重试惯用法的异步边界由主线钉死）。**不冲突，但也不被主线 champion。**

### RESOLUTION:

1. **三态：Table（维持 inactive）。** 作为**语法提案**：B（`Retry`/`Resume` 关键字）明确 **Reject**——无需求、保留字成本、与 2018.02.28 异步决议冲突、复刻要消灭的隐蔽控制流。作为**论题**：方向有价值（非结构化错误处理应该退场），但不是语言特性。
2. **采纳 PROPOSAL C 为推荐重试形态**：`For` + `Try/Catch` + `Catch ... When`，零新语法、零 Goto、与异步状态机相容。A 的 `GoTo Retry` 惯用法承认其合法且有时正当（2017.08.09 示例、spec 允许 Catch→Try 跳转），但**不作为新代码推荐**；`Retry:` 标签名保留为普通标签。
3. **`Resume` 残留区（返回出错语句）无结构化等价**，是主张三（可以弃用）的**否决项**。`Resume` / `Resume Label` 保持 `On Error` 专属；弃用范围显式排除残留区。
4. **弃用落地走 PROPOSAL E（工具链），编译器默认闭嘴**：不对存量 `On Error` 发警告（原则 #1 + 残留区无迁移路径）；analyzer + code fix 只在用户主动迁移时介入。新代码教学/文档/模板停教 `On Error`。
5. **`Resume Next` 迁移是场景相关的，不可机械翻译为空 `Try/Catch`**：特征探测（`Err.Number = 0`）→ `TypeOf`/`TryCast`（与 `TypeOf` 流分析 Active 工作项汇合）；"批量忽略失败"→ 定向 `Catch`（按异常类型）；无更优答案的过渡才用空 `Catch` 并接受分析器告警。
6. **记录自然边界**：`On Error`/`Resume` 与 async/iterator/lambda 的互斥是 spec 强制（"Unstructured error handling statements are not allowed in iterator or async methods."）；弃用叙事不得依赖"异步化会自然淘汰 `On Error`"——VBScript 迁移代码大量是同步遗留。
7. **与 VBScript 遗产的关系**：`On Error Resume Next` 是经典 VBScript 唯一的错误处理。VBScript.NET 的价值主张是**引入 `Try/Catch`**（这条在 VB 里已存在），不是立刻弃用遗产。

### Implication:

- 将本条目的三个成分**拆开记录**：C（结构化重试惯用法）并入建议文档作为"推荐形态"；E（弃用路线图）转工具链工作项；B（关键字）留档为 Reject 记录。
- 起草一篇 spec note：非结构化 EH 表面盘点（`Error` / `On Error` 四形态 / `Resume` 三形态 / `Err` 对象）、async/iterator/lambda 互斥、`SyncLock`/`Using` 单语句处理、GoTo 进块限制（区分"从语句外跳入"与"Catch→Try 体内跳转"）。
- 与 `TypeOf` 流分析（Active）对表：`Resume Next` 特征探测的迁移路径依赖 `TypeOf`/`TryCast` 收窄；写一份"探测模式迁移对照表"。
- 与 `Try` 增强（Active）对表：`Await` in `Catch`/`Finally` 落地后，重试惯用法的异步边界按 2018.02.28 决议落实（含 `Await` 的 `Try` 内禁 `GoTo`-from-Catch-to-Try）。
- 收集数据：VBScript.NET 目标代码库 `On Error` 使用率、`Resume Next` 与 `GoTo` 处理器块的比例、`Resume` 残留区规模、`Err` 探测模式占比。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Resume`（返回出错语句）残留区的真实规模——有多少存量代码依赖"重跑出错语句"而非"继续下一条"。这决定弃用路线图能否覆盖 90% 存量。
- `OPEN QUESTIONS`：空 `Catch` 作为 `Resume Next` 过渡形态的接受度——是否需要某种"catch-all 且继续"的专门形态（如 `Catch ... Continue`）？We 倾向不造，但没有数据反对。
- `OPEN QUESTIONS`：VBScript 遗产里 `On Error Resume Next` + `Err` 探测模式能否被 `TypeOf`/`TryCast` **机械**覆盖——晚期绑定存在的场景不能，需要逐条判定。
- `TODO`：量化 `On Error` 使用率（VBScript.NET 目标代码库 + VB6 迁移代码），验证或推翻"只有两类场景"的前提。
- `TODO`：analyzer + code fix 原型（`On Error Resume Next` → `Try/Catch`，含 `Err` 对象映射），测量迁移的机械性比率。
- `Follow-up`：确认主线 2018.02.21 的假设在 VBScript.NET 的适用性——We 判断：对 async 成立（spec 强制），对 VBScript 同步迁移代码不成立。

### 状态

- **LDM 状态：inactive（维持搁置）**；语法半边（`Retry`/`Resume` 关键字）Reject；弃用论题 Table，落地路径转工具链（E）；结构化重试惯用法（C）采纳为推荐形态（零语言工作）。
- **三态判定：Table**——动机真实（非结构化错误处理应当退场）但被三条硬约束否决为"语言特性"：① `Resume` 残留区无结构化等价；② 弃用违反原则 #1 且 VBScript 遗产是存量主流；③ 任何新关键字与 2018.02.28 异步决议冲突。**激活所需信号**：① `On Error` 使用率数据（验证前提）；② `Resume Next` 迁移的完整设计（含 `Err` 对象与探测模式）且有 analyzer 原型；③ `Resume` 残留区规模评估（若趋近于零，弃用论题复活）；④ 文档/模板停教 `On Error` 的落地计划。

---

## 附录：特性评价

# 建议评价报告：proposal-retry-resume.md

## 评价对象

- 建议：proposal-retry-resume.md — `Retry`/`Resume`（重试与恢复）
- 来源：Anthony 原文 §18.6 "Retry/Resume"（`..\..\AnthonyDesign_wordpress.txt` L2869–2902；引述 "The only scenarios where I can see using `On Error` today is for `Resume Next` or retrying with `Goto`. If we can hit that with `Try` that kind of unstructured error handling can be deprecated." 逐字出自该章）
- 配方目标：以（增强后的）`Try` 覆盖 `Resume Next` 与 `Goto` 重试，从而弃用 `On Error` 家族非结构化错误处理；**原文未提出任何新语法**
- 状态定位：`inactive/` 实验性论题（讨论性条目），非正式草案

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。主效果（`Try` 覆盖重试）可演示且与主线 2017.08.09 示例逐字同构；关键子效果（`Resume Next` 覆盖）作者自认"worse"（空 `Try/Catch` 序列），且 `Resume`（返回出错语句）**完全未覆盖**——论题只成立一半。无原型/运行证据 | 已检查 | 前提（"只剩两类场景"）无数据支撑且与 spec 三语句/四形态表面不符；`Resume` 残留区未识别；弃用可衡量性未定义 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。零新语法（无交付、也无杂质）；论题延续 VB 的 `On Error` 遗产基因、方向与主线"modern code 不用 OnError"假设一致；但条目标题 `Retry`/`Resume` 暗示不存在的语法（概念命名错位），且假想 B 若实现则违反原则 #3（第二种错误处理方式）与 #7（复刻隐蔽控制流） | 已检查 | 命名与内容错位（标题像语法提案、正文是论题）；未继承/声明主线 2017.08.09 同构示例的出处 |
| 品质 | 4/5 | 锚点 4："结构完整、边界基本清晰，仅个别细节不精确"。六章节齐全；Summary 诚实标注"实验性/无新语法提案"；原文引述逐字准确；Drawbacks/Alternatives/Unresolved 三节真实（非模板骨架）；未决问题 4 个且具体（依评价标准不扣品质、封顶效果） | 已检查 | 缺兼容性/breaking-change 分析（弃用本身即破坏）；`Resume`（非 Next）、`On Error GoTo 0/-1`、`Err` 对象表面未盘点；未引用 spec 的 async/iterator/lambda 互斥事实；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/光轻度正向（保住 `On Error` 兼容资产、`Try` 覆盖重试盘活结构化路径）；暗风险突出且未对冲（弃用破坏 VB6/VBScript 迁移资产、`Resume` 残留区无迁移路径、假想 `Retry` 关键字破坏标识符）；VBScript 遗产视角（`Resume Next` 是唯一错误处理）完全缺席 | 已检查（预测待定） | 弃用的破坏面未权衡；与 `TypeOf` 流分析 / `Try` 增强的迁移汇合未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料来源（§18.6）标注准确、引述忠实；但重试示例与主线 2017.08.09 示例同构却未标注（继承主线 LDM 材料未声明）；spec 硬约束（非结构化 EH 禁于 async/iterator/lambda）未引用；VBScript 遗产这一关键成分未分析 | 已检查 | 主线 LDM 历史（2017.08.09 / 2018.02.21 / 2018.02.28）未纳入；成分影响（`Resume` 残留区）与"可以弃用"的结论偏差未处理 |

## 设计原则对照

- **与 VB 基因：部分一致**。方向一致于原则 #4（跟随 C#——C# 无非结构化 EH）、#6（不为边缘场景加特性）、#7（弃用隐蔽控制流）；偏离于原则 #1（弃用即破坏）、#2（新关键字不像 VB）；原则 #3（第二种做事方式）与 #8（`Resume` 已保留字 / `Retry` 非保留字）对假想 B 构成否决。原则 #9（消除样板）由既有结构（C 的 `For`+`Try`、`TypeOf`/`TryCast`）达成，无需语法。
- **与主线关系：Anthony 独立延伸，与主线无冲突但也不被 champion**。主线无弃用 `On Error` 提案（对照表 2.3 无此条目）；主线仅在 async 语境接触 `On Error`/`GoTo`（2018.02.21 "GoTo and OnError seem unlikely in "modern" code that would use Async"；2018.02.28 禁 `Await` 与 Catch→Try 跳转共存）。本建议方向与主线假设一致，且重试惯用法的异步边界由主线决议钉死。
- **破坏性变更：零（编译层面）**——建议无新语法。**有（论题层面）**——"弃用 `On Error`"本身是破坏性变更，且 `Resume` 残留区无迁移路径；假想 `Retry` 关键字会破坏 `Retry` 标识符及当前惯用法的 `Retry:` 标签。建议文档未做任何兼容性分析。

## 总评

- **达成程度：部分达成（作为论题）**——前提（"只剩两类场景"）与 spec 表面不符且无数据；主效果（重试可被 `Try` 覆盖）成立，关键子效果（`Resume Next` 干净覆盖）不成立，`Resume` 残留区完全未识别；结论（可以弃用）是跳步。作为讨论条目诚实、忠实、健康；作为特性**刻意不是特性**。
- **LDM 三态建议：Table（维持 inactive）**——语法半边（`Retry`/`Resume` 关键字）Reject；弃用论题 Table，落地路径转工具链（analyzer + code fix + 文档）；结构化重试惯用法（C）采纳为推荐形态（零语言工作）。面向 VBScript.NET 的优先级：**低**，排在 `Await` in `Catch`/`Finally` 与 `TypeOf` 流分析之后——VBScript.NET 的价值主张是引入 `Try/Catch`，不是立刻弃用遗产。
- **主要问题**：① 前提与 spec 不符（三语句/四形态/`Resume` 残留区）；② 弃用的破坏性零分析；③ 未识别 VBScript 遗产视角（`Resume Next` 是唯一错误处理）；④ 未对接主线 LDM 历史（2017.08.09 同构示例、2018.02.28 异步决议）；⑤ 假想 `Retry` 关键字的保留字破坏（`Retry` 非保留字、`Retry:` 标签）未分析。

## 返工建议

- **拆包**：按三个成分拆分——(a) 结构化重试惯用法文档（C 形态，零语法，采纳为推荐）；(b) `On Error` 弃用路线图（转工具链工作项）；(c) `Retry`/`Resume` 关键字（Reject 留档，不删除）。
- **补充章节**：非结构化 EH 表面盘点（引用 spec `statements.md`：`Error`/`On Error` 四形态/`Resume` 三形态/`Err` 对象、async/iterator/lambda 互斥、`SyncLock`/`Using` 单语句处理、GoTo 进块限制）；Compatibility/breaking-change（弃用警告策略、`Retry` 保留字风险、`Resume` 残留区）。
- **补充证据**：`On Error` 使用率数据（验证或推翻前提）；`Resume Next` → `Try/Catch` 迁移的机械性比率；`Resume` 残留区规模；analyzer + code fix 原型（含 `Err` 对象映射）。
- **未决问题处理**：`Resume` 残留区显式留档为弃用范围排除项；特征探测迁移依赖 `TypeOf`/`TryCast`（与 `TypeOf` 流分析 Active 对表）；空 `Catch` 作为过渡形态的接受度待数据。
- **设计探索**：`GoTo Retry`（A）与 `For`+`Try`+`When`（C）在可读性上的对照测试；`Resume Next` 探测模式的迁移对照表（`Err.Number` 探测 → `TypeOf`/`TryCast` 判定）；与主线 2018.02.28 决议的异步边界一致性确认。

---

## 附录：C# 生态与互操作考量

> 本附录评估本提案（`Retry`/`Resume`，错误重试与恢复）在 C#/CLR/.NET 生态的对应走向。**结论先行**：本提案与 C# 生态的接缝几乎全部落在「重试怎么做」这一个问题上，而 C# 生态的答案恰好是**库与循环，不是语言**——与本场决议（不造关键字、采纳结构化惯用法）同向。`Resume` 残留区与 `On Error` 家族是 VB/VBScript 独有遗产，在 csharplang 全库无任何对应物，不构成跨语言互操作问题。本附录**不改变**既有 RESOLUTION 与三态判定，仅在末尾追加一条桥接提示。

### 相关 C# 现实方向

**(1) C# 没有重试语句；重试是循环 + 库模式，不是语言特性。** 在 `proposals` 与 `meetings` 全库检索 `retry`：proposals 侧仅命中字符串插值两处（`proposals\csharp-10.0\improved-interpolated-strings.md`、`proposals\interpolated-string-handler-argument-value.md`，语义是"重试构造函数解析步骤"，与异常重试无关）；meetings 侧**零命中**。C# 的 `try/catch/finally`（自 C# 1.0）从未有过重试子句；重试在 .NET 生态由**库**承担（Polly、Microsoft.Extensions.Resilience 等）。`Polly` 一词在 csharplang 仓库内的全部命中是一只名为 "Polly" 的鹦鹉（`meetings\working-groups\discriminated-unions\TypeUnions.md`）——**重试库完全不在 C# 语言设计视野内**，语言把重试留给库。

**(2) C# 6 已提供异常过滤器 `catch ... when`，它是 C# 侧重试惯用法的承载语法。** `Language-Version-History.md`（C# 6 节）逐字列出两条相关特性：

- 「Exception filters」→ `Language-Version-History.md`
- 「Await in catch/finally blocks」→ `Language-Version-History.md`

即 C# 早在 2015 年就落地"过滤式 catch"与"catch/finally 内 await"。本提案的 PROPOSAL C（`For` + `Try/Catch` + `Catch ... When`）在 C# 侧就是 `for` + `try/catch when`——**跨语言 1:1 同构**。

**(3) 异常过滤器仍是 C# 的活面：C# 15 unsafe-evolution 正在重新审视它。** `proposals\unsafe-evolution.md` 专设 "Catch filters" 一节，原文逐字：

> "The `when` filter of a `catch` clause is an expression, not a statement body."

其语境：过滤器是表达式而非语句体，`unsafe` 块只能包围语句，无法只包过滤器表达式；当过滤器调用的方法成为 *requires-unsafe* 后，需要 `unsafe` 表达式才能内联。该节示例逐字：

```cs
try
{
    await DoWork(); // 'await' here prevents wrapping the whole try/catch in 'unsafe'
}
catch (Exception e) when (NowUnsafeCall(e))
{
}
```

对本提案的意义：VB 的 `Catch ... When` 与 C# 的 `catch ... when` 语法对齐，但 C# 正在给"过滤器表达式里能调用什么"加新的安全维度（requires-unsafe），VB 侧必须能识别对应元数据（见适应建议 4）。

**(4) C# 没有非结构化错误处理，也从未讨论过 `Resume`/`On Error`。** csharplang 全库检索 `On Error Resume`/`On Error GoTo` 零命中。CLR 结构化 EH 没有 "resume at faulting instruction"（本场第 11 节），C# 亦无任何语句可"回到出错语句重跑"。跨语言对照事实——「It is legal in VB (but not C#) to jump from inside a Catch to inside the Try.」——出自 **VB LDM 笔记**（`..\..\..\vblang\meetings\2018\vbldm-notes-2018.02.28.md`，逐字，本场正文已引用；注意 csharplang 侧同名 `meetings\2018\LDM-2018-02-28.md` 内容是可空引用类型，与此无关）。结论：`Resume` 残留区与 `GoTo`-from-Catch 惯用法都是 VB 独有能力，C# 侧既无等价物，也无互操作需求。

**(5) 晚期绑定/动态在 C# 生态边缘化，压迫 `Err` 探测模式。** 索引 T7：`dynamic`（C# 4）与表达式树长期无大演进，C# 靠类型系统与 source-gen 取代动态；索引 T5：AOT/trimming 让"类型系统承担更多职责"。`On Error Resume Next` + `Err.Number = 0` 特征探测几乎全是晚期绑定（本场第 4 节），C# 生态侧没有"更动态"的答案，只有"更静态"的答案（`TypeOf`/`TryCast`、source-gen）。C# 侧已核实的 VB 定位原文：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（「VB」小节，逐字）

C# 对 VB 的策略是"不要求 VB 跟进 C# 的安全上下文模型"——与"默认安全、按需动态"的双模路线同向。

### 现实 vs 提案

| 提案成分 | C# 生态对应 | 关系 | 理由 |
|---|---|---|---|
| B（`Retry`/`Resume` 关键字） | 无对应；重试=循环+库 | **脱节** | C# 从未提案/讨论重试语句（检索零命中）；生态答案（Polly 等）明确是库职责。本场 Reject 与 C# 生态同向。 |
| A（`GoTo`-from-Catch 重试） | C# 禁 Catch→Try 跳转；等价物是循环+`when` | **需桥接（文档层）** | A 是 VB 独有能力，C# 开发者读到无参照系；文档宜给 VB/C# 对照，不推荐新代码用 A。 |
| C（`For`+`Try/Catch`+`Catch ... When`） | `for` + `try/catch when`（C# 6） | **兼容** | 逐字同构、跨语言 1:1 可表达；C# 6 异常过滤器是承载语法。 |
| D（库级重试，Polly） | 就是 C# 生态的默认答案 | **兼容（本场佐证）** | C# 语言不碰重试，交给 Polly/Resilience 库；与"语言不动"结论一致。 |
| E（弃用路线图=工具链） | Roslyn analyzer 生态（编译器外驱动迁移） | **兼容** | C# 侧迁移类工作同样是 analyzer/code-fix（如 .NET analyzers），不在语言特性里做弃用。 |
| `Resume` 残留区（返回出错语句） | 无对应、无 IL 形态 | **脱节（无互操作维度）** | 纯 VB 遗产，编译器内部语句边界重入，不产出新元数据；C# 侧无从消费也无从提供。 |
| `On Error Resume Next`+`Err.Number` 探测 | 无对应；C# 生态走向"更静态" | **需桥接** | 依赖晚期绑定，与 AOT/trimming（T5）、动态边缘化（T7）冲突；迁移 `TypeOf`/`TryCast` 与 C# 生态同向。 |

**净判断**：语法半边（B）与 C# 生态**完全脱节**——C# 用库做重试，本场也因无需求/保留字/异步冲突 Reject B，两方向汇合。惯用法半边（C/D/E）与 C# 生态**兼容或同向**。真正的桥接只有两件：① 文档给 VB/C# 双语重试对照；② VB 编译器识别 C# 侧新元数据（requires-unsafe）。`Resume` 残留区与 `On Error` 家族是 VB 独有，**不构成跨语言互操作问题**——从 C# 视角看，连"互操作阻力"都不存在，纯属 VB 内部遗产问题；这反过来强化本场"残留区否决弃用"的判定（这是 VB 自己的债务，C# 帮不上忙）。

### 对 VBScript.NET 的适应建议

1. **重试推荐形态（C）跨语言同构，直接进双语示例。** VB `For attempt As Integer = 1 To 3 / Try / DoWork() / Exit For / Catch ex As Exception When attempt < 3 / End Try / Next` 配 C# 对照 `for (int attempt = 1; attempt <= 3; attempt++) { try { DoWork(); break; } catch (Exception ex) when (attempt < 3) { } }`。两语言共享同一 CLR EH，重试语义跨语言一致。
2. **默认安全、按需动态。** `On Error Resume Next` 探测模式是晚期绑定重灾区；VBScript.NET 默认 `Option Strict On` + `TypeOf`/`TryCast` 迁移，把 `On Error Resume Next` 探测限定为兼容层/脚本传统模式显式 opt-in。与 C# 生态"动态边缘化、AOT 优先"（T5/T7）同向。
3. **source-gen 桥。** resilience/重试包装（超时、退避、重试上限）做成编译期 source generator 生成，而非运行时反射/DynamicMethod——对齐 C# T6（编译期生成替代运行时动态），为未来 NativeAOT 留出口。
4. **识别新元数据。** C# 15 unsafe-evolution 落地后，`catch ... when` 过滤器若调用 *requires-unsafe* 成员需 `unsafe` 表达式；VB 无 `unsafe` 上下文（C# 明确不为 VB 添加，见上文 VB 引文），因此 VB 编译器**必须认识** `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`，正确判定"从 VB 过滤器调用 C# requires-unsafe 成员"的安全性——决策文件 M8 已标记的桥接点，在 `Catch ... When` 语境下同样生效。

### 对既有 RESOLUTION / 三态判定的影响

**无改变，且获得 C# 生态的独立佐证。**

- 三态 **Table** 不受影响——C# 生态"重试是库+循环"的现实，说明 B 的 Reject 不是 VB 保守，而是整个 .NET 语言的共同选择。
- RESOLUTION ②（采纳 C）与 C# 6 异常过滤器同构，跨语言成立性增强。
- RESOLUTION ③（`Resume` 残留区否决弃用）是纯 VB 内部问题，C# 无此物、无互操作维度；否决项仍成立。
- RESOLUTION ④（E 转工具链）与 Roslyn analyzer 生态实践一致。
- **追加一条桥接提示（非判定改变）**：VBScript.NET 支持 `Catch ... When` 后，把"识别 requires-unsafe 元数据"纳入编译器工作项（适应建议 4），否则跨语言消费 C# 15 时代库会校验失准。

### 引用纪律

- 「The `when` filter of a `catch` clause is an expression, not a statement body.」→ `proposals\unsafe-evolution.md`（"Catch filters" 节，逐字）。其示例 `try { await DoWork(); ... } catch (Exception e) when (NowUnsafeCall(e))` 同节逐字。
- 「Exception filters」「Await in catch/finally blocks」→ `Language-Version-History.md`（C# 6 节，逐字条目）。
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节，逐字；源自索引四、已核实）。
- 「It is legal in VB (but not C#) to jump from inside a Catch to inside the Try.」→ `..\..\..\vblang\meetings\2018\vbldm-notes-2018.02.28.md`（**VB LDM** 笔记，逐字；本场正文已引用）。注意勿与 csharplang 侧同名 `meetings\2018\LDM-2018-02-28.md`（内容是可空引用类型）混淆。

**OPEN QUESTIONS**：
- "C# 从未提案重试语句"是**负向证据**（proposals+meetings 检索零命中），未覆盖 csharplang 的 issue 与 dotnet/runtime 提案；若未来出现重试 issue，本附录"脱节"判断需复核。
- Polly / Microsoft.Extensions.Resilience 的具体 API（重试次数、退避、熔断）属 dotnet/resilience 生态，csharplang 库内无正文，本附录不深挖；VBScript.NET 若做"库级重试默认绑定"需另行调研。
- unsafe-evolution 最终状态未定（C# 15 候选、experimental），requires-unsafe 元数据确切形态（属性名/程序集级开关）落地前，"识别新元数据"的具体清单为 **Suspect**。
