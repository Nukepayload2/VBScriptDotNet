# Visual Basic Language Design Meeting
September 11, 2026

议题是 `proposal-with-events-in-submissions`——**提交（submission / `.vbx` / REPL 提交链）里的 `WithEvents` 与 `Handles`**。语言侧不新增语法：两个关键字都是既有 VB 语法，脚本模式下已经能解析到绑定期；缺的是**语义与实现路径**。提案把这层缺口拆成三条崩溃路径加一条 Debug 断言误伤，并把「静默失效」这个说法推翻了一半——今天用户拿到的不是静默，是**进程终止或会话终止**。四条路径落在两个方法上：`SourceMemberMethodSymbol.vb:762-772` 的 `TypeKind` 闸（`TypeKind.Submission` 落进 `Case Else`，`:770-771` 逐字 `Case Else` / `Throw ExceptionUtilities.UnexpectedValue(ContainingType.TypeKind)`），与 `:700-705` 的 `isFromBase` 分支（提交类顶层方法里 `DirectCast(Me.ContainingType, SourceNamedTypeSymbol)` 必然失败，因为提交类的符号类型是 `ImplicitNamedTypeSymbol`，它直接派生 `SourceMemberContainerTypeSymbol` 而不经过 `SourceNamedTypeSymbol`）。

我们把四条路径里的三条（(a)、(c)、(d)）重跑了一遍，并把提案 `:70` 那段逸出栈追到了它的出处——结论是那段栈**确实存在、但被拼接过**：它的尾段与另一条 REPL 栈被合成了一段连续箭头。这正是本次会议最费时的一处，也是若干裁决的地基（见「翻源码」一节第 3 小节）。

我们两条独立路径各读了一遍——一条沿 VB 基因问「`Handles` 在提交模型里到底还算不算它自己」「把同名当替换会不会开第二种做事方式」，一条沿 C# 生态问「这件事在 C# 侧有没有可见面、元数据层能不能表达、共享宿主会不会因此分叉」。两条路径在事实基线上独立收敛，对候选集的定价也基本一致，分歧只在**跨提交 (iii) 的收口方式**与**子情形 (vi) 是否并进同一条门**两处，两处都在下文按证据裁掉了。

> **来源标注**：正文引用的源码与规范文本均逐行核对（`文件:行号`）。提案 Detailed design 的锚点逐条复核（含任务点名者与正文引用者共 30 余条），**全部落在其所述方法/成员体内、无一处指向另一符号**；区间端点有三处不精确（`ExceptionUtilities.cs:18-26` 应作 `:21-28`；`SynthesizedEntryPointSymbol.vb:286-292` 的完整语句跨 `:284-295`、体组装跨 `:251-309`；§3 两处「表述比事实大」），一处机制叙述需订正（「未处理异常」实为被 runner 的兜底 `catch` 抓住），一处栈需拆分（`:70`）。另有一批正向机制发现是复核中新增的（跨提交 `Handles` 的因果链缺环、relax 转换两侧不对称、C# 的 CS9045 范式、`AccessedThroughProperty` 才是跨程序集契约、回归网实在树内），见「翻源码」一节。提案是冻结输入，对它的修正只写进 RESOLUTION。

## Agenda

* [Proposal: 提交中的 `WithEvents` / `Handles` / WithEvents and Handles in Submissions](#proposal-提交中的-withevents--handles--withevents-and-handles-in-submissions)

## Proposal: 提交中的 `WithEvents` / `Handles` / WithEvents and Handles in Submissions

_Related: [`../proposals/proposal-with-events-in-submissions.md`](../proposals/proposal-with-events-in-submissions.md)；父域与姊妹会议 `../proposals/proposal-scripting-dialect.md` / `../meetings/meeting-scripting-dialect.md`（`WithEvents`/`Handles` 的「不得静默」裁决在其 RESOLUTION R11，`meeting-scripting-dialect.md:150`；本单元只写事件挂钩这一面，不重述声明/提交模型）；多树提交 `../proposals/proposal-load-directive.md`；决策 `../decisions.md` D5（基础功能以 C# / csi 为蓝本，须给「VB 特有理由」）；上游跟踪项 dotnet/roslyn#14073；共享层台账 `../upstream-merge.md`_

### 场景与缺口

**`WithEvents` + `Handles` 是这批目标用户的第一直觉写法。** `.vbx` 与 REPL 面向 VB6/VBA 迁移用户，他们表达「把某个对象的事件绑到这个方法」的默认动作是声明式（`WithEvents src As New R` + `Sub H(...) Handles src.Tick`），不是命令式 `AddHandler`。今天这个写法在提交类里得到的不是错误信息，是终止。

**四条输入路径，我们在两个构建上各自复核（已运行）。** 计数口径按提案：三条崩溃 + 一条 Debug 断言误伤；**提交类里的嵌套类型不在此列**（它是范围限定反例，见下）。

| # | 输入 | Debug `5816a5c` | Release `e307d0f` | 崩溃点 |
|---|---|---|---|---|
| (a) | `.vbx` 顶层 `WithEvents src As New Ticker` + `Sub H1(...) Handles src.Tick` | 断言终止，退出码 35 | `InvalidOperationException`，退出码 9 | `SourceMemberMethodSymbol.vb:771` |
| (b) | `.vbx` 顶层 `Event Tick(...)` + `Sub H0(...) Handles Me.Tick` | 同 (a) | 同 (a) | 同上 `:771` |
| (c) | REPL 前序提交声明 `WithEvents src`，本提交 `Sub H2(...) Handles src.Tick` | 断言终止，退出码 35（终止于 `SourceWithEventsBackingFieldSymbol.vb:66`，**不是** `:702`） | `InvalidCastException`，退出码 1，**会话终止** | `SourceMemberMethodSymbol.vb:702` |
| (d) | `.vbx` 仅 `WithEvents src As New Ticker`（无 `Handles`） | 断言终止，退出码 35（`SourceWithEventsBackingFieldSymbol.vb:66`） | **正常编译运行，退出码 0** | — |
| (e) | `.vbx` 提交类里的**嵌套类型**（`Class Host` 内 `WithEvents` + `Handles`） | 正常（退出码 0） | 正常（退出码 0） | — |

**本表的证据边界（引用前必读）**：(a) 与 (d) 两行是**本轮在两个二进制（Debug `5816a5c` / Release `e307d0f`）上重跑复现**的；(c) 行**只在 Release 构建上重跑复现**（退出码 1、`InvalidCastException` 与栈文本均实测），其 Debug 侧终止于 `:66` 断言属提案/验证者记录（**已运行·转引**，本轮未重跑）；(b) 行未重跑，结论由 (a) 的同一崩溃点（`:771`）与提案记录支撑；(e) 行**未重跑**，「嵌套可用」由代码路径支撑（`ContainingType.TypeKind` 是 `Class`，走 `:767` 的 `Case TypeKind.Class`；其构造器也不是脚本构造器，`MethodSymbol.vb:517-521`），运行侧属提案/验证者记录（**已运行·转引**）。四个 Debug 终止的共同来源是 `ExceptionUtilities.UnexpectedValue` 里的 `Debug.Assert(false, output)`（`ExceptionUtilities.cs:24`）——Debug 下断言先杀进程，所以 Debug 构建看不到 Release 的那条逸出栈。

**三个退出码各自有机制，不是「未处理异常」。** 我们在共享宿主里读到并实测复核：`CommandLineRunner.RunScriptAsync` 在当期树（`:264-268`）与较旧树（`e307d0f`，`:218-222`）**都有** `catch (Exception e) { DisplayException(e); return e.HResult; }`。所以 (a)(b) 的 `InvalidOperationException` 是**被 runner 抓住、经 `DisplayException` 打印后 `return e.HResult`** 的——`0x80131509` 的低字节即 `0x09` = 观测到的 **9**（已运行：Release 二进制实测退出码 9，栈文本由 `DisplayException` 打印）。Debug 的退出码 35 同理来自 `Environment.FailFast` 的 `0x80131623` 低字节（已运行：(d) 实测 35）。而 (c) 的退出码 1 来自 `Vbi.OnStartupAsync` 自己的 `Catch ex As Exception … Return 1`（`Interactive/vbi/Vbi.vb:68-72`）——即 REPL 循环里异常**确实无人处理**，逃到宿主入口才被接住。**三个码三条路径，这本身是 (a)(b) 与 (c) 走不同宿主路径的旁证。**

**(d)/(e) 两条的意义是纠偏既有资料。** (d) 说明 `SourceWithEventsBackingFieldSymbol.vb:66` 的 `Debug.Assert(Not Me.ContainingType.IsImplicitlyDeclared)` 在脚本类上必然为假（`ImplicitNamedTypeSymbol.vb:33-37` 的 `IsImplicitlyDeclared = IsImplicitClass OrElse IsScriptClass`），Release 下断言被编译掉、产物正常——**是误伤，不是缺口**。(e) 说明既有「提交类不支持、嵌套也不支持」的二分与事实相反：嵌套类型的 `ContainingType.TypeKind` 是 `Class`，走 `:767` 的 `Case TypeKind.Class` 正常通过，且它的构造器不是脚本构造器（`MethodSymbol.vb:517-521`，`ContainingType` 是嵌套类而非脚本类），挂钩注入因此没被跳过。**提交类里可用的恰恰是嵌套类型。**

### 翻源码：锚点逐一复核

**提案点名的锚点逐条核对，全部落在方法/成员体内，无一处指向另一个符号。** 唯一的偏差是两条区间的端点（见下第 1 条），不影响任何结论。我们逐条 Read/sed 核了 `SourceMemberMethodSymbol.vb:665`/`:700-705`/`:762-772`/`:780-795`/`:818-824`、`ExceptionUtilities.cs`、`SourceMemberContainerTypeSymbol.vb:233-237`/`:240`/`:156-157`/`:2509`/`:2626-2633`/`:2804-2808`/`:1295-1300`、`MethodSymbol.vb:517-521`、`SynthesizedPropertyAccessorBase.vb:143`/`:164`/`:201-271`/`:249`/`:273-300`/`:302-345`/`:329`、`InitializerRewriter.vb:86-142`/`:174-186`、`SynthesizedEntryPointSymbol.vb:286-292`/`:377`、`SynthesizedInteractiveInitializerMethod.vb:105-107`、`SourceWithEventsBackingFieldSymbol.vb:29-32`/`:66`/`:68-77`、`ImplicitNamedTypeSymbol.vb:25-26`/`:33-37`/`:51-60`/`:213-219`、`Binder_Lookup.vb:858-926`、`Binder_Expressions.vb:2560-2572`/`:2611-2630`（`:2630` 确为 `End Function`）、`Conversions.vb:4309-4316`（`AllArgumentsIgnored` 在 `:4315`）、`Binder_Delegates.vb:1048-1052`/`:1058-1065`（`:1062` 传 `isZeroArgumentKnownToBeUsed`）、`SymbolExtensions.vb:136-146`/`:151-154`、`Syntax.xml:2032-2051`、`SourcePropertySymbol.vb:231`/`:271-283`/`:743-747`、`SourceMemberFieldSymbol.vb:643-656`、`SourceNamedTypeSymbol.vb:2738-2752`/`:2761-2770`/`:2772-2782`/`:1431-1436`、`NamedTypeSymbol.vb:1219`、`VisualBasicCompilation.vb:368-389`/`:2021-2022`、`VisualBasicScriptCompiler.vb:208`、`VisualBasicCompiler.vb:96`、`Errors.vb:273`/`:403`/`:1598-1599`、`VBResources.resx:2358-2361`/`:2367-2370`。**`SynthesizedPropertyAccessorBase.vb` 的 `TypeKind` 零命中经 `grep -c` 复现为 `0`**（文件 370 行）——这是「挂/摘钩与类型种类无关、P-A1 零新机制」的支点。

**1）一处机制叙述要订正：不是「未处理异常」。** 提案 §1 表把 (a)(b) 写成「未处理 `InvalidOperationException`（退出码 9）」、把 (c) 写成「未处理 `InvalidCastException`（退出码 1）」。实测口径是：(a)(b) **被 `RunScriptAsync` 的兜底 `catch` 接住**，经 `DisplayException` 打印栈后以 `e.HResult` 为退出码；只有 (c) 在 REPL 循环内无人接住。可观察结果与提案相同，但机制不同——而机制位置恰好是下面第 3 小节判栈归属的依据。另外 `ExceptionUtilities.UnexpectedValue` 的完整区间是 `:21-28`（`Debug.Assert(false, output)` 在 `:24`，`return new InvalidOperationException(output)` 在 `:27`），提案引 `:18-26` 会让人以为整段都在区间内。

**2）路径 (c) 的因果链提案缺了最关键一环（正向发现）。** 提案写「`isFromBase = True` 时 `DirectCast` 必然崩」，结论对，但没答「`isFromBase` 为什么会是 `True`」——在提交类里写 `Handles src.Tick`，`src` 是**前序提交**声明的变量，为什么没报 BC30506（`ERR_NoWithEventsVarOnHandlesList`，`Errors.vb:403`）而是找到了属性？两处锚点补上这条链：`Binder_Lookup.vb:579-584`（`Case TypeKind.Submission` → `LookupInSubmissions`，对提交类成员查找**本身就沿 `PreviousSubmission` 链回溯**）与 `SourceMemberMethodSymbol.vb:860-871`（`FindWithEventsProperty` 就是 `binder.LookupMember(witheventsLookup, containingType, name, 0, options, useSiteInfo)`，`containingType` 是当前提交类）。两者一叠，`Handles src.Tick` 必然查得到那个 `WithEvents` 属性，而它的 `ContainingType` 是 `Submission#N-1` 的 `ImplicitNamedTypeSymbol` ≠ 当前提交类 ⇒ `:665` 判 `isFromBase = True` ⇒ `:702` 崩。**这条链的分量不在实现细节，在定性：绑定层把前序提交的属性当成了「本类型的成员」，而 `type-members.md:843` 的第一条 `Handles` 规则逐字要求「The first identifier must be an instance or shared variable **in the containing type**」。** 也就是说 (c) 不是「功能没写完」，是**绑定层已经违反了自己语言规范的第一条规则**——这条改变了 (iii) 的收口方式（见权衡三）。

**3）`:70` 那段逸出栈的归属：实锤 + 它被拼接过（本次会议最费时的一处）。** 提案 `:70` 段的括注记着这样一段栈：`… SourceModuleSymbol.vb:703 → Compilation.Emit → ScriptBuilder.Emit:179 → Script.RunAsync:469/:443 → CommandLineRunner.RunScriptAsync:209`，并另有两个撰写者自补的帧 `RunInteractiveCoreAsync:146`、`Vbi.OnStartupAsync:62`（未标注所属构建）；main 因 `RunScriptAsync` 全仓只有一处定义、当期树在 `:238` 而栈帧记 `:209`（小于定义行），撤掉了两个自补帧、把剩余栈标为「行号待复验」。我们把 Release `e307d0f` 二进制（`Interactive\vbi\bin\Release\net10.0\vbi.exe`，Aug 23，版本串 `2.0.0-Beta+e307d0f`）拿 (a) 与 (c) 两条输入各跑了一次，**未整理的原始栈文本如下**（已运行）：

- **(a) 文件脚本路径**（退出码 9）：`… BindSingleHandlesClause … :771` → `… SourceModuleSymbol.vb : 694` → `… : 744` → **`GetAllDeclarationErrors … SourceModuleSymbol.vb : 703`** → `SourceAssemblySymbol : 1185` → `VisualBasicCompilation.GetDiagnosticsWithoutFiltering : 2263` → `CompileMethods : 2531` → **`Compilation.Emit`（5 帧）** → **`ScriptBuilder.Emit … ScriptBuilder.cs : 179`** → `ScriptBuilder.Build : 133` → `CreateExecutor : 88` → `Script<T>.GetExecutor … Script.cs : 365` → **`Script<T>.RunAsync(…, Func, …) … Script.cs : 469`** → **`Script<T>.RunAsync(Object, CancellationToken) … Script.cs : 443`** → **`<RunScriptAsync>d__15.MoveNext() … CommandLineRunner.cs : 209`**。**栈到此为止。**
- **(c) REPL 路径**（退出码 1）：`… BindSingleHandlesClause … :702`（`InvalidCastException`）→ 同一段编译器栈 → `ScriptBuilder.Emit : 179` → `Build : 133` → `CreateExecutor : 88` → `GetExecutor : 365` → **`Script<T>.CommonCompile … Script.cs : 338`** → `Script.Compile : 232` → **`BuildAndRunAsync … CommandLineRunner.cs : 298`** → **`RunInteractiveLoopAsync … : 292`** → **`RunInteractiveCoreAsync … : 146`** → `RunInteractiveAsync … : 73` → **`Vbi.OnStartupAsync … Vbi.vb : 62`** → `The script has error. See the output for more information.`

由此四条结论：

- **帧行号归属 = 较旧的 Release `e307d0f` 构建，实锤。** `RunScriptAsync` 在 `e307d0f`/`b2d5d7f`/`7a0111e` 定义在 `:201`，`:209` 逐字是 `return (await script.RunAsync(globals, cancellationToken)).ReturnValue;`；在当期树定义在 `:238`、同一调用在 `:256`，`:209` 落在静态方法 `GetScriptOptions` 体内。**实测把归属从「推测（高）」升为实锤**：Release 二进制打印的正是 `:209`。其余三帧（`SourceModuleSymbol.vb:703`、`ScriptBuilder.Emit:179`、`Script.RunAsync:469/:443`）两棵树逐行同文、**无鉴别力**，但与实测一致；而且它们各自的意义都精确到位——`:703` 是 `curTask.GetAwaiter().GetResult()`（声明错误 Task 的再抛点，正是异常逸出处），`:469` 是 `var currentExecutor = GetExecutor(cancellationToken);`（上一帧 `GetExecutor` 的调用行），`:179` 是 `return compilation.Emit(`。`Compilation.Emit` 是**省略了中间帧**的整理写法（真实链经 `CompileMethods : 2531`）。
- **两个被撤的自补帧不是编造，是另一条栈的真帧。** `RunInteractiveCoreAsync:146` 与 `Vbi.OnStartupAsync:62` **在 (c) 的原始栈里逐字出现**（`CommandLineRunner.cs:line 146` / `Vbi.vb:line 62`）。但它们**与 (a) 栈不同源**：`:146` 在 `e307d0f` 树是 `await RunInteractiveLoopAsync(…)`（**REPL 分支**；`.vbx` 分支的调用行在 `:151`），而在 (a) 栈里同一位置是 `RunScriptAsync:209`。两条路径互斥——(a) 的异常**在 `RunScriptAsync` 内部就被 `catch` 接住**，所以它的栈**结构上不可能**包含 `RunScriptAsync` 以下的任何宿主帧；反过来 (c) 的栈走 `CommonCompile : 338` / `Compile : 232` / `BuildAndRunAsync : 298`，**结构上不可能**包含 `RunAsync:469/:443` 与 `RunScriptAsync`。**⇒ 提案 `:70` 的箭头链是把 (a) 与 (c) 两条真实栈的尾段拼接起来的；它作为单次运行的栈不可能存在。** 撤掉是对的，但正确的处置不是「标待复验」，而是**拆成两条、各自标注栈归属**——按当期树去核永远核不上。
- **一处 `Vbi.vb` 行号提醒（新发现）**：Release 二进制报告 `Vbi.OnStartupAsync` 在 `Vbi.vb:62`，而 `git show e307d0f:Interactive/vbi/Vbi.vb` 里该 `Await` 在 `:57`（`:62` 是 `Return retVal`）。也就是说**该 Release 构建的工作树与 e307d0f 的提交内容并不逐字相同**（`Vbi.vb` 后来的改动当时已在工作树里、稍后才提交）。所以引用这批帧时的口径应是「**以二进制自身报告的 PDB 行号为准**」，不能简单写成「按 `e307d0f` 树读」。
- **提案「栈中不经 `CommonCompiler`」属实**：两条栈都不含 `CommonCompiler` 帧，异常发生在 `ScriptBuilder.Emit` 之下的编译器声明阶段。

**4）relax 转换两侧不对称（修正提案的一处归因）。** 提案 §5.2 把 `Binder_Delegates.vb:334` 的 `isForAddressOf:=Not isForHandles` 作为限制 2 的锚点之一，容易被读成「`Handles` 允许零参处理者 ⇒ 不需要 stub」。实际是：`:334` 管的是**诊断判级**（`Handles` 不当窄化 ⇒ 订阅侧不报 BC42328），**不是免 stub**——零参处理者 ↔ 带参事件的转换是 `MethodConversionKind.AllArgumentsIgnored`，它**在** stub 集合里（`Conversions.vb:4315`），照样走 `BuildDelegateRelaxationLambda`（`:1058-1065`）。规范依据在 `type-members.md:847-848`（逐字：「Unlike an `AddHandler` statement, however, explicit event handlers allow handling an event with a method with no arguments regardless of whether strict semantics are being used or not」）——说的是**不报错**，不是**不需要 stub**。**两侧不对称才是真限制**：订阅侧（`isForHandles:=True`）不报诊断但仍造合成 lambda，摘除侧（`AddressOf`，`isForAddressOf:=True`）直接报 BC42328。提案的结论（这类订阅摘不掉）对，但 spec 里必须写成明文的取舍声明——**用户能写出来的 `Handles` 形态不能被「摘不掉」反过来禁掉**，只能是摘除能力让步。

**5）提案的 C# 对照可以当基准用，两处叙述收口，三处补锚。** C# 侧锚点逐条复核**全部属实**：`PropertySymbol.cs:89-92`（`IPropertySymbol.IsWithEvents` 恒 `false`）、`SynthesizedSubmissionConstructor.cs:12-30`、`SynthesizedEventAccessorSymbol.cs:24-33`/`:140-159`、`MethodCompiler.cs:1010-1014`（脚本构造器体是**空块**）/`:1269-1271`、`MethodBodySynthesizer.cs:336-343`/`:346-408`、`DeclarationTreeBuilder.cs:296`/`:322-361`（`:339` 修饰符 `Internal | Partial | Sealed`）、`InitializerRewriter.cs:32-77`（**通读确认无事件/委托代码**）、`ErrorCode.cs:1157/1166/1167/1332`；`SyntaxKind.cs` 的 `Handles`/`WithEvents` 零命中、全树 `WithEvents` 恰 1 行亦复现。两处收口：① 「`Handles` 只出现在英文注释里」不准确——全树词面命中 47 行，其中含 `Handles` 的**标识符**（`customAttributeHandles`、`GenericParameterHandleCollection` 等）占多数，注释只 6 行（结论不变：无一处是语言构造）；② 「C# 的脚本诊断族是 `grep InScript` 的全部命中」按字面成立，但读者会以为清单穷尽——`ErrorCode.cs` 另有 `ERR_ExpectedSingleScript=7018`（`:1164`）与 `ERR_ScriptsAndSubmissionsCannotHaveRequiredMembers=9045`（`:2099`）。三处补锚见下条。

**6）C# 侧范式与元数据契约（正向发现）。** ① C# 对「脚本/提交顶层不允许某特性」的既有作答范式是**一条诊断、两类脚本类同闸**：`SourceMemberContainerSymbol.cs:3087-3099` 的 `ReportRequiredMembers`，首行 `Debug.Assert(IsSubmissionClass | IsScriptClass)`，命中即报 CS9045。② 跨程序集识别 `WithEvents` 的**真正契约是特性而非 CLR 标志**：`PENamedTypeSymbol.vb:1319` 的 `HasAccessedThroughPropertyAttribute` 收集名字、`:716-717` 再按名字在本类型里找唯一属性并 `SetIsWithEvents(True)`，读出侧 `PEPropertySymbol.vb:257-278`；因此它是**纯 VB→VB 契约**，C# 侧永远看不见（`IsWithEvents` 恒 `false`）——这正好是提案 Drawbacks「LSP/补全难以静态推断」那条的 C# 侧锚点。③ `SourcePropertySymbol.vb:251-254` 把**实例** `WithEvents` 属性强制置为 `Overridable`（IL 里是 virtual），而覆盖机制（`SynthesizedOverridingWitheventsProperty.vb:46-73`）的前提正是「有一个可覆盖的基属性」——这条与下节 P-A3 的否决直接相连。

**7）回归网实在树内（推翻一条口头担忧）。** 提案指向的 `Compilers\VisualBasicEmitTest\CodeGen\CodeGenWithEvents.vb` **存在于本期树**（2127 行，`WithEvents`/`Handles` 词面命中 118 处）。姊妹会议 `meeting-scripting-dialect.md:142` 记的「编译器测试树已裁剪」指的是显式 `Me` 禁令那一条测试，与本回归网不是同一件事——引用时不要混。P-A1 的「普通类与嵌套类型零回归」因此有现成承接点。

### 权衡一：P-A1 是「只做对的那一半」，不是半成品

`WithEvents` 容器的挂/摘钩**本来就不走构造器**：它在属性 `Set` 访问器体内完成（`SourceMemberMethodSymbol.vb:780-781` 让 `WithEvents` 容器选 `SetMethod` 作宿主；`SynthesizedPropertyAccessorBase.vb` 的 `:143-196` 认领本类中 `hookupMethod` 指向该访问器的 `HandledEvents`、`:201-271` 摘旧、`:273-300` 赋值、`:302-345` 挂新）。这段实现**与类型种类无关**（该文件 `TypeKind` 零命中，已复现）。脚本类顶层 `Dim x As New R` 的赋值则是该提交自己的实例初始化器（`SourceMemberContainerTypeSymbol.vb:2626-2633` 把顶层可执行语句登记为 `FieldOrPropertyInitializer`），组装进该提交自己的 `<Initialize>`（`InitializerRewriter.vb:174-186`），由入口点在提交实例化时调用一次（`SynthesizedEntryPointSymbol.vb:377` 的 `Return submission.<Initialize>()`）。

于是 P-A1 的全部改动就是**两处门**：`SourceMemberMethodSymbol.vb:762-772` 把 `TypeKind.Submission` 与 `Class`/`Module` 同列，以及 `SourceWithEventsBackingFieldSymbol.vb:66` 的断言。**零新合成符号、零新语法、零新写法**，普通类与嵌套类型代码路径完全不变（`SynthesizedPropertyAccessorBase` 零改动，已复现）。它覆盖的是子情形 (ii) 与 (v)——正是 VB6/VBA 迁移用户最容易写出的那个组合。

**但 P-A1 有一个前提，我们认为它应当从「用户可见约束」升格为「规范边界」。** 挂/摘钩语句活在 `Set` 访问器体里，因此只在属性被赋值时执行；而**后一提交无法让前序提交的 `<Initialize>` 再执行一次**（该链是三环：提交数组注入 `MethodCompiler.vb:1535-1537` → 新实例构造 `SynthesizedEntryPointSymbol.vb:365-372` → 工厂体内唯一一次 `:377` 的调用，语义上不存在重入）。提案把这写成 Drawbacks 里的一条代价（「用户会在 REPL 里遇到『先声明 `WithEvents`、下一条提交再写 `Handles` 不行』的落差」）。我们不同意这个定位：按 `type-members.md:843`，`Handles` 的第一条规则本来就要求第一个标识符是**本类型内**声明且带 `WithEvents` 的变量；提交链里的前序提交是**另一个类型**（`Submission#1` vs `Submission#2`，各自 `ImplicitNamedTypeSymbol`），而跨提交能查到它、正是因为绑定层沿链回溯（`Binder_Lookup.vb:579-584`）——那是**意外**，不是设计。**所以「同一次提交」不是 P-A1 的成本，是 `Handles` 规则在提交模型里的本来面目；跨提交 `Handles` 是错误输入，不是待支持的能力。** 把这句话写成规范边界，P-A1 就从「只做了一半」变成「只做对的那一半」。

### 权衡二：候选 C 与 P-A1 是补集，不是二选一

提案 Alternatives 把「报诊断」（C）与「复用既有挂载机制」（E）并列成备选。我们不同意这个摆法：E（P-A1）覆盖 (ii)+(v)，C 覆盖 (i)+(iii)+(vi)+(d) 的 Debug 面；**R11 的「实现挂钩或给显式诊断，二选一」（`meeting-scripting-dialect.md:150`）指的是每一处二选一，不是全局二选一**。

不做 C 的后果是实测的，不是推理的：只做 P-A1 之后，用户在 REPL 里写 `Handles src.Tick`（跨提交）拿到的仍然是 `:702` 的 `InvalidCastException`——**退出码 1、整个会话终止、尾随提交不再执行**（已运行复现）。这比今天最坏的行为还坏，因为它杀掉整个会话。诊断选码方面我们认同提案的判断：既有 `Handles` 族语义不匹配（`ERR_HandlesSyntaxInClass`(BC31412) 的文案是「'Handles' in classes must specify a 'WithEvents' variable, 'MyBase', 'MyClass' or 'Me' qualified with a single identifier.」，`VBResources.resx:2358-2361`；`ERR_HandlesSyntaxInModule`(BC31418) 是「'Handles' in modules must specify a 'WithEvents' variable qualified with a single identifier.」，`:2367-2370`；两者的语义都是「限定形式不对」，不是「此处不支持」；`ERR_NoWithEventsVarOnHandlesList`(BC30506，`Errors.vb:403`) 同理），需新增码并走 VB 资源 + 13 语言 xlf + `upstream-merge.md` 入账。**覆盖范围必须含四个入口**，其中 `:66`（Debug 断言）与 `:702`（跨提交 `DirectCast`）必须一并处理，否则 Debug 下连 P-A1 的用例都测不了。

C# 侧在这里能给的是**范式**而非可抄的码：C# 没有与声明式事件挂钩相关的诊断（`grep -i hookup` 全树零命中），但有 CS9045 那条「一条诊断、`IsSubmissionClass || IsScriptClass` 同闸」的既有作答方式。这也支持把子情形 (vi)（非提交脚本类）**并进同一条门**：提案自己已经证出两类脚本类在注入层同病（`MethodCompiler.vb:1482` 的 `IsScriptConstructor` 对 `DeclarationKind.Script` 与 `Submission` 同时为真，`MethodSymbol.vb:517-521` × `SourceMemberContainerTypeSymbol.vb:1295-1300`），只有符号合成层还维持着「非提交走普通 Class 路径」的说法（`SourceMemberContainerTypeSymbol.vb:2808`）——分开写会留下一处长期不一致。按同一条门写，`spec:348` 与 `meeting:150` 的表述一并订正为「符号层成立、注入层不成立」。

### 权衡三：跨提交 (iii) 走诊断，不走覆盖属性

提案把 (iii) 的实现列为最贵的一条（P-A3），理由是覆盖属性无处在提交类上造。我们两条独立路径都认为**这条路不该走**：

- **从 `Handles` 规范看**，`type-members.md:843` 的第一条规则就要求容器变量在**本类型内**；提交链不构成 containing type，而普通 VB 里唯一的跨类型合法形态是继承（基类声明 `WithEvents`）。提交类**没有基类型**（`ImplicitNamedTypeSymbol.vb:51-60` 对 `TypeKind.Submission` 返回 `Nothing`；`SourceNamedTypeSymbol.vb:1431-1436` 同结论），继承那条路根本不存在。
- **从元数据层看，覆盖路线不可表达**：覆盖机制的载体 `SynthesizedOverridingWitheventsProperty`（`SynthesizedSymbols\SynthesizedOverridingWitheventsProperty.vb:46-73`）前提是「有一个可覆盖的基属性」，而两类脚本类**都不可继承**（VB 修饰符 `Friend Or Partial Or NotInheritable`，`DeclarationTreeBuilder.vb:143`；C# 对偶 `Internal | Partial | Sealed`，`DeclarationTreeBuilder.cs:339`）——「后一提交继承前一提交」在两种语言里都不成立（C# 的跨提交同样走字段数组 + `BoundPreviousSubmissionReference`，不是继承）。
- **若日后真要「能用」**，形态只能是**语句级注入**（把 `AddHandler` 写进后一提交自己的方法体）。宿主二选一：`<Initialize>` 与提交构造器。这里 C# 侧有一条输入：`<Initialize>` 的体两侧同构，且**目前两侧都没有语言特定的挂钩段**（C# `InitializerRewriter.cs:32-77` ↔ VB `InitializerRewriter.vb:174-186`，都是「初始化器序列 → 语句」两段），把挂钩段放进去会让这段体在两侧分叉，而 `Await`、末尾表达式、结果打印都压在这段体上；提交构造器两侧本就各含一段特定内容（提交数组初始化，C# `MethodCompiler.cs:1269-1271` ↔ VB `MethodCompiler.vb:1553-1560`），往那里加不新增形状差异。**所以我们倾向提交构造器**——但这属 VB 设计面的决定，且 P-A2 本就要延后，留作 OPEN QUESTION。

**结论：(iii) 先落诊断**（把 `:665` 的 `isFromBase` 对提交类转成「不合法限定」的错误路径，现成的 BC30506 语义最近，若要更准可新增脚本专属码），**P-A3 不做**，语句级注入列 Backlog。收益是省掉接收方合成、挂钩注入宿主与覆盖机制解短路三笔成本。注意一个细节：**提交类没有 `MyBase` 语义**（无基类型），所以 `Handles MyBase.E` 需要单独裁定（报错 / 报诊断 / 与 `Me` 等价，C# 无对应物）。

### 权衡四： 「同名 = 替换并自动摘钩」应当直接否决

提案 §5.1/§5.2 自己承认这是最大的语义代价，但仍把它留成 Unresolved 选项。我们的意见是**直接否决**，理由来自规范原文两条（逐字）：

> `type-members.md:870`：「A single member can handle multiple matching events, and **multiple methods may handle a single event**.」

> `type-members.md:1980`：「The implicit property created by a `WithEvents` declaration takes care of hooking and unhooking the relevant event handlers. **When a value is assigned to the variable**, the property first calls the `remove` method … Next the assignment is made, and the property calls the `add` method …」

规范给出的「自动摘钩」触发点就是一个：**对 `WithEvents` 变量赋值**（`type-members.md:1980` 逐字限定「**When a value is assigned to the variable**」）；多处理者共存是默认语义、规范未给出互摘规则（`:870` 逐字「multiple methods may handle a single event」）。若定义「新提交写同名处理者 ⇒ 摘掉旧的」，就造出了第二个触发点，而且触发条件从「赋值」变成「写一个同名方法」——这两件事在 VB 用户心里毫无关联，正是原版 LDM 划的那条线（`vblang\meetings\2018\vbldm-notes-2018.06.13.md:34`：「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」）。

**而且这个场景的既有语义已经够用**：同名 `WithEvents` 重声明就是**遮蔽**（`SymbolExtensions.vb:144` 的注释逐字「uncommon case - WithEvents property do not overload. They behave like fields when OHI is concerned.」，`:151-154` 的实现 `Return Not propertySymbol.IsWithEvents`；`SynthesizedOverridingWitheventsProperty.vb:75-77` 同调「WithEvents properties shadow by name (similarly to fields).」）——遮蔽是既有语义，不需要新规则；新属性被赋值时挂的是**新对象**，旧对象的旧订阅不受影响；真要摘旧，走的是既有语义里已有的两个入口：给 `WithEvents` 变量赋新值（`Set` 访问器摘旧），或显式 `RemoveHandler`——我们不建议为「同名替换」新增第三个入口。所以 P-A1 的规范措辞应是「**声明式订阅只加不摘；摘除只有两个既有入口（`WithEvents` 变量赋值、显式 `RemoveHandler`）；新提交重新声明同名 `WithEvents` 是遮蔽，不构成对旧订阅的操作**」，并写明规则作用域：`Handles` 语法面内不存在闭包订阅（`HandlesClauseItemSyntax` 的子节点只有 `EventContainer`（关键字容器或 `WithEvents` 容器）、`DotToken`、`EventMember`（限 `IdentifierName`，只能是简单标识符），`Syntax.xml:2032-2051`；处理者恒为带着该子句的命名方法本身，合成式见 `SourceMemberMethodSymbol.vb:818-824`），而**需 relax stub 的订阅不可重建**（见「翻源码」第 4 条），故摘除规则限定在「签名直接兼容」的订阅——这条限制必须对用户讲清「为什么」（`type-members.md:870`/`:1980`：多处理者共存、摘钩只由赋值触发），否则会被读成缺陷。

### VB 基因对照 / C# 生态对照

**VB 基因这一侧给「附带条件支持 / 置信度高（锚点与机制）、中（成本判断）」**，倾向是：收 P-A1 但把「同一次提交」写成规范边界而不是实现妥协；候选 C 必做（它是补集）；否决自动摘钩；(iii) 出诊断；(vi) 不单独立项；断言 `:66` 的放宽方向应当是**重述不变量**（「容器不是 `ImplicitNamedTypeSymbol`，或该类型确有源声明的 `WithEvents` 成员」）而不是往既有断言里塞 `OrElse IsScriptClass`——断言想守的是「`WithEvents` 后备字段只出现在用户可写的类型上」，脚本类打破了这个前提，这是**类别问题不是特例问题**，加 `OrElse` 等于在不变量上凿洞。另外它提醒：这批用户既最容易写出 P-A1 覆盖的形态，也最容易踩 relax stub 那条限制（VB6 时代 `Handles` 写法自由、签名不必逐参对齐），所以「只加不摘」这句必须写给他们看。

**C# 生态这一侧给「附带条件支持 / 置信度高（限 C# 兼容性一维）」**：本主题在 C# 侧**没有可见面**——改动面全部落在 VB 绑定层（一个 `TypeKind` 闸 + 一处断言）与 VB 诊断资源，不新增元数据、不新增公共 API（不触 `PublicAPI.*.txt`）、不改提交类形状，因此对 C# 编译器、分析器、反编译器、工具链不产生可见影响；唯一的对外义务是候选 C 的 VB 诊断资源 + 多语言 xlf + `upstream-merge.md` 入账，C# 侧无义务。它还确认了一件对我们有利的事：`Script<T>` 这条共享宿主 API **不含** WithEvents 语义，所以同一共享 runner 跑 C# 与 VB 脚本不会因本规则出现「同一段代码两种答案」（这与末尾表达式那条不同）。它对候选集的两条输入——宿主选提交构造器、把 (vi) 并进同一条门——已并入上文。

**两条路径在事实基线上独立收敛，在候选定价上只在 (iii) 与 (vi) 两处分歧，两处都按证据裁掉了**：跨提交不影响 C# 生态（既有字段数组机制，不是继承），因此「先出诊断」与「日后语句级注入」都不引入分叉，按 VB 侧的规范论据取诊断；而 (vi) 的「同病」有实现锚点，按 C# 侧的范式取「一条门盖两类」，同时不为其单独立项。

### RESOLUTION:

1. **P-A1 采纳**（子情形 (ii) + (v)）：`SourceMemberMethodSymbol.vb:762-772` 放行提交类（与 `Class`/`Module` 同列，或为脚本类单列分支），并处置 `SourceWithEventsBackingFieldSymbol.vb:66` 的误伤断言。**零新合成符号**，挂/摘钩复用既有 `Set` 访问器机制（`SynthesizedPropertyAccessorBase.vb` 零改动）。
2. **「`WithEvents` 赋值初始化器与 `Handles` 子句必须同处一次提交」升格为规范边界**，不作为实现妥协或 Drawback 陈述。依据：`type-members.md:843` 的容器要求；提交链不构成 containing type。spec 必须写明这条界，并写明跨提交 `Handles` 是错误输入。
3. **候选 C（显式诊断）采纳，且与 P-A1 是补集、不是备选**：覆盖范围含四个入口（`:771` 闸、`:702` `DirectCast`、`:66` Debug 断言面、以及 (i) 关键字容器）。**没有 C 则 P-A1 不单独发布**——否则跨提交 `Handles` 仍以「会话终止」收场（已运行复现）。
4. **「同名 = 替换并自动摘钩」否决。** P-A1 的规范措辞定为「声明式订阅只加不摘；摘除只有两个既有入口（`WithEvents` 变量赋值、显式 `RemoveHandler`）；新提交同名 `WithEvents` 是遮蔽，不构成对旧订阅的操作」。Drawbacks 里「自动摘钩与 `Handles` 既有语义冲突」一条随之消失。
5. **子情形 (iii) 跨提交走诊断；P-A3（覆盖属性路线）否决**，理由双份：规范侧（`type-members.md:843`）+ 元数据侧（提交类无基类型 `ImplicitNamedTypeSymbol.vb:51-60`、两类脚本类都不可继承 `DeclarationTreeBuilder.vb:143` / `DeclarationTreeBuilder.cs:339`，覆盖载体 `SynthesizedOverridingWitheventsProperty.vb:46-73` 前提不成立）。语句级注入列 Backlog，不属本单元。
6. **子情形 (vi) 与 (i) 合成同一条门**（形态照 CS9045 范式 `SourceMemberContainerSymbol.cs:3087-3099` 的 `IsSubmissionClass || IsScriptClass`），但**不为 (vi) 单列立项**（本 fork 宿主产不出该形态，运行后果未实证）。`spec:348` 与 `meeting:150` 的「非提交脚本类走普通 Class 路径」订正为「符号层成立、注入层不成立」。
7. **断言 `SourceWithEventsBackingFieldSymbol.vb:66` 的处置方向取「重述不变量」**，不取「往断言里加 `OrElse IsScriptClass`」。
8. **P-A2（关键字容器 `Handles Me.E` / `MyClass.E`）延后，宿主选择留 OPEN QUESTION**；C# 侧输入记载为「倾向提交构造器」（理由：`<Initialize>` 的体两侧同构且目前两侧都没有语言特定的挂钩段，C# `InitializerRewriter.cs:32-77` ↔ VB `InitializerRewriter.vb:174-186`；提交构造器两侧本就各含提交数组初始化段）。同时须裁定挂钩与字段初始化器的先后（普通类既有顺序是挂钩段在初始化器语句之前，`InitializerRewriter.vb:145-147` 的注释逐字「insert initializers AFTER implicit or explicit call to a base constructor and after Handles hookup if there were any」）。
9. **`Handles MyBase.E` 在提交类上语义为空**（无基类型），三选一（报错 / 报诊断 / 与 `Me` 等价）留 OPEN QUESTION。
10. **摘除规则的规范作用域**：限定在「签名直接兼容（无 relax stub）的 `Handles` 订阅」；**订阅能力不因摘除能力受限**（`type-members.md:847-848` 的宽松形式是有意特性），spec 须写成明文的取舍声明，并写明两侧不对称（订阅侧不报 BC42328、摘除侧报）。
11. **候选 C 的落地义务四项**：诊断覆盖范围、BC 选号、`VBResources.resx` + 13 语言 xlf（按原版 VB 编译器 xlf 规范：新增 trans-unit、不得手改既有单元）、`upstream-merge.md` 入账（对齐 `meeting-scripting-dialect.md:157` RESOLUTION 18 对 BC36965/BC36966 的处置）。C# 侧无义务。
12. **`§1` 的机制叙述订正**（本纪要为该表述的 supersede 口径）：「未处理 `InvalidOperationException`」→「被 `RunScriptAsync` 的兜底 `catch (Exception)` 接住、经 `DisplayException` 打印后 `return e.HResult`」；退出码 9 = `0x80131509` 低字节（已运行实测）；(c) 的退出码 1 来自 `Vbi.OnStartupAsync` 的 `Catch`（`Vbi.vb:68-72`）。
13. **`:70` 的逸出栈拆成两条**（本纪要为该表述的 supersede 口径）：文件脚本路径栈 = `GetAllDeclarationErrors:703 → Emit → ScriptBuilder.Emit:179 → Script<T>.RunAsync:469/:443 → RunScriptAsync:209`；REPL 路径栈 = `… → BuildAndRunAsync:298 → RunInteractiveLoopAsync:292 → RunInteractiveCoreAsync:146 → RunInteractiveAsync:73 → Vbi.OnStartupAsync:62`。**两条均取自较旧的 Release `e307d0f` 构建（已运行复现）**，帧行号以**二进制自身报告的 PDB 行号**为准；当期 Debug 树不可能产出（`RunScriptAsync` 定义在 `:238`、`Script.RunAsync` 调用在 `:256`）。
14. **`Binder_Lookup.vb:579-584` + `SourceMemberMethodSymbol.vb:860-871` 补入提案的因果链**（跨提交 `Handles` 能查到前序属性 ⇒ 绑定层实际违反了 `type-members.md:843`，这是 P-A1 边界的规范论据）。同时补 `§5.2` 的 relax 归因（`Binder_Delegates.vb:334` 是**诊断判级**、不是免 stub；`Conversions.vb:4315` 在 stub 集合内）。
15. **C# 侧两处叙述收口**：「`Handles` 只出现在英文注释里」→「无任何语言构造命中；词面命中的多数是英文注释与含 `Handles` 的标识符名（全树 47 行，注释 6 行）」；「脚本诊断族 = `grep InScript` 全部命中」→ 补 `ERR_ExpectedSingleScript`(7018) 与 `ERR_ScriptsAndSubmissionsCannotHaveRequiredMembers`(9045)，并把 CS9045 记为**范式**参照。另补锚 `PENamedTypeSymbol.vb:1319`（跨程序集契约是 `AccessedThroughPropertyAttribute`）与 `SourcePropertySymbol.vb:251-254`（实例 `WithEvents` 属性恒 `Overridable`）。
16. **删去提案 Motivation 的排序义表述**（「`spec-scripting-dialect` 的最后一个未闭合结构面」「唯独事件挂钩一句带过」）：排序不可证伪，且其支撑「语义条文只 `:348`」已被推翻——`spec-scripting-dialect.md:102` 逐字「`WithEvents` hookup constructors are synthesized for a non-submission script class (which follows the ordinary class path) and are not synthesized for a submission class.」同为同主题语义陈述。改为可证伪表述：「spec 的方言边界段把该行为划到保证之外（`:348`），但该措辞描述的是 `AddWithEventsHookupConstructorsIfNeeded` 的 TODO 分支（`SourceMemberContainerTypeSymbol.vb:2804-2808`），不是用户实际遭遇的终止」。
17. **残留项责任主体订正**：提案 `:307` 的「**本轮**用固定输入探针测了六组」改为「**验证者**用固定输入探针测了六组（撰写者转引）」——探针不是撰写者所跑。其余残留项（`:20` 排序义）由 R16 处置，二者均不阻塞。
18. **回归网确认**：`Compilers\VisualBasicEmitTest\CodeGen\CodeGenWithEvents.vb` 在本期树内（2127 行，`WithEvents`/`Handles` 命中 118 处），可作 P-A1 的「普通类与嵌套类型零回归」承接点；提案正文的「零回归」不是纸面保证。（`meeting-scripting-dialect.md:142` 的「编译器测试树已裁剪」指显式 `Me` 禁令那一条测试，非本回归网。）

### Implication:

- **对应 model**：本条对应 `decisions.md` **D5** 的落实（「基础功能以 C# / csi 为蓝本 + 说明为什么 VB 必须分叉」）。结论是分叉**解释得了**——`WithEvents`/`Handles` 是 C# 没有的语言概念，C# 没有「声明式订阅」所以不需要合成位置；缺口的性质是**移植不完整**（上游自己登记为 dotnet/roslyn#14073，`ImplicitNamedTypeSymbol.vb:213-219` 的注释直接点明根因是本提案 §1 (c) 的 `DirectCast` 前提）。**M1–M8 无直接对应条目**；最近的落点是 M5（脚本运行时）；M8 的「晚绑定 vs AOT」不要拉进来当理由（脚本宿主天然在 AOT 之外，挂钩代码是编译期合成，不用反射也不用 `DynamicMethod`）。D4 不直接命中 P1 两档（既不是「C# 已照顾到的用例」，也不是 C# interop 用例），优先级由 LDM 风险评估定。
- **对 spec 的冲击面**：`spec\spec-scripting-dialect.md` 需订正/新增五块——`:348` 的方言边界条目（符号层 vs 注入层的分立 + 「不支持的形态报诊断」替代现状）、「同一次提交」边界条文（R2）、「声明式订阅只加不摘」与两个摘除入口（R4）、摘除规则的作用域与两侧不对称声明（R10）、`WithEvents` 后备字段不变量与断言处置（R7）。
- **对 `upstream-merge.md` 的冲击面**：三条落地路径都动 `Compilers\VisualBasic\Portable`（绑定宿主选择、注入点、`TypeKind` 闸），须登记；新增 BC 码须入账（R11）。提案 Unresolved 11 指出本主题在账本中无条目、未与基准 commit diff——该疑点在本单元维持 `Suspect`，实现前必须确认 `SourceMemberContainerTypeSymbol.vb:2806` 的 TODO 与 `ImplicitNamedTypeSymbol.vb:213-219` 的上游短路是否属 fork 改动面。
- **行为变更的定性**：路径 (d)（顶层 `WithEvents` 单独声明）在 Release 下今天能编译能跑（退出码 0，已运行），P-A1 落地后它会**开始真正订阅**。这不是回归——`type-members.md:1978-1980` 明文要求挂钩必须发生，今天是缺陷——但 spec 必须点名这是修错而非语义变更，否则就是一次静默的行为变更。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTION`：**关键字容器的挂载宿主**（Unresolved 2）——`<Initialize>` 还是提交构造器？C# 侧输入倾向提交构造器（R8）；同时要裁定挂钩与字段初始化器的先后（普通类既有顺序：挂钩在初始化器之前，`InitializerRewriter.vb:145-147`）。
- `OPEN QUESTION`：**`Handles MyBase.E` 在提交类上的语义**（R9）——报错 / 报诊断 / 与 `Me` 等价。提交类无基类型，C# 无对应物。
- `OPEN QUESTION`：**P-A2 的真实成本**（动注入层对脚本初始化器体形状、序列点、调试信息的影响）；我们的倾向是它比提案估的高，但标**待定**。
- `OPEN QUESTION`：**多树提交（`#Load`）下 `WithEvents` 与 `Handles` 分处两棵树的行为**——多树提交仍是同一个 script class（`SourceMemberContainerTypeSymbol.vb:2731-2732` / `:2762-2763` 注释逐字「a submission may span multiple script trees (#Load)」），故 `:665` 的 `isFromBase` 应仍为 `False`；但 `Set` 访问器与 `<Initialize>` 的组装次序（加载树在前、主树在后）是否会产生「挂钩早于 `WithEvents` 赋值」的次序问题**未验证**（`Suspect`）。
- `OPEN QUESTION`：**`IsWithEvents` 的跨程序集消费**（Unresolved 4 / §8 第 4 项）——只在代码层核到 VB→VB 契约（`PENamedTypeSymbol.vb:1319` → `:716-717`，读出侧 `PEPropertySymbol.vb:257-278`）与 C# 适配层恒 `false`，**未做跨程序集消费方实测**。若日后要动这条契约，必须先补一个「VB 编译的程序集 → 另一编译单元按 `WithEvents` 消费」的实测。
- `OPEN QUESTION`：**relax stub 合成方法是否跨编译稳定**（Unresolved 4 前提，§8 第 3 项）——同一 `(处理者方法, 事件)` 在不同提交里调用 `BuildDelegateRelaxationLambda`（`Binder_Delegates.vb:1058`）是否落到同一方法定义。**未验证**；且即使稳定也不该依赖（依赖合成物的实现细节去定义用户可见语义）。
- `TODO`：R1（两处门）+ R3（诊断覆盖四入口 + 新码）+ R11（xlf / 资源 / 台账）+ R18（回归网接入）；prototype 先放行 `TypeKind` 闸，实测同提交形态是否真的挂钩（`Set` 访问器在生成体时是否已看到 `HandledEvents`）——**这是 P-A1 唯一的运行侧未验证项**（§8 第 1 项）。
- `TODO`：spec 五块（见 Implication）；`upstream-merge.md` 补登与基准 diff（R11 尾巴）。
- `Follow-up`：**(i)/(vi)** 若日后要「能用」而非只有诊断，按 R6 的门形状与 R8 的宿主结论重走一轮；**(iii)** 若语句级注入进入 Backlog 排期，先答「挂钩写进后一提交的哪个方法」与「继承不可行 ⇒ 接收方如何合成」两问。
- `Suspect`：本主题在 `upstream-merge.md` 无台账条目，`SourceMemberContainerTypeSymbol.vb:2806` 的 TODO 与 `ImplicitNamedTypeSymbol.vb:213-219` 的上游短路是否属 fork 改动面未与基准 commit 做 diff。

### 状态

- **LDM 状态：Active**。
- **三态判定：Active（Proposed）**——四条路径的崩溃基线在两个构建上各自复核一致（VB 基因「附带条件支持 / 置信度高（锚点与机制）、中（成本判断）」；C# 生态「附带条件支持 / 置信度高（限 C# 兼容性一维）」），裁决为 **P-A1 采纳、候选 C 采纳（与 P-A1 捆绑）、「同名替换/自动摘钩」否决、P-A3 否决、(iii) 出诊断、(vi) 并入同一条门**。采纳项满足 D4 的 P1 硬约束（不改合法既有代码的正确语义：P-A1 只扩宽承认的 `TypeKind` 并处置误伤断言，C 只新增诊断）；被否决项里「自动摘钩」会改既有行为面、P-A3 已在规范与元数据两侧证不可表达。**剩余未定项**（P-A2 宿主、`MyBase` 语义、`#Load` 次序、`IsWithEvents` 跨程序集契约、relax stub 稳定性、上游基线 diff）均已带证据锚与收口动作。下一阶段是 **plan**（`tasks\with-events-in-submissions\`）——落地顺序建议 **C（止血：先让不支持的形态不再终止进程或会话）→ P-A1（最小可用子集）→ 其余按 Backlog**。
- **supersede 口径清单**：R12（§1 表的「未处理异常」机制叙述）、R13（`:70` 栈的归属与拆分）、R14（§1 (c) 因果链 + §5.2 relax 归因）、R15（§3 两处叙述 + 三处补锚）、R16（Motivation 的排序义表述）、R17（`:307` 的责任主体）——提案正文不改，这些表述以本条纪要为准。
- **独立五维评审为不入库工作材料**（git-ignored），仅供主持人/规划内部使用，不混入官方会议记录。

### 附录：五维评价（特性评价，不进正文）

| 维度 | 得分 | 评价 | 证据等级 | 问题 |
|------|------|------|----------|------|
| 效果 | 4/5 | 缺陷面可演示且可复现（四条路径在两个构建上各自复核，退出码与消息均实测）；P-A1 有明确的收益与低成本改动面（两处门、零新合成符号）；未决问题集中在 (i)/(iii)/(vi) 的**后续**形态而非主线 | 已运行（四条路径 + 栈复现）+ 已检查（源码） | P-A1 在提交类本身的运行后果未验证（同提交形态的挂钩是否真发生，§8 第 1 项） |
| 特性 | 4/5 | 不新增语法、不新增写法；P-A1 完全复用既有 `Set` 访问器机制（零新合成符号）；「只加不摘」与原版 LDM「不开第二种做事方式」的线一致。扣分在提案一度把「同名替换」留成候选（会引入第二种做事方式）与「跨提交也要支持」的定位偏差 | 已检查 | 提案对边界（同提交 vs 跨提交）的定位需要靠规范条文反向纠正 |
| 品质 | 4/5 | 六节模板完整；证据等级逐节标注、断言三态、主动声明证据边界（§8 四项）；Unresolved 11 条具体且带动作；`(e)` 行自发做范围限定反例值得肯定。扣分在三处锚点范围不精确、一处机制叙述（未处理异常）、一处栈被拼接 | 已检查 | `:70` 段把两条不同宿主路径的栈合成一段连续箭头 |
| 属性 | 4/5 | 修的是「进程终止 / 会话终止」这一最坏失败模式，方向与「不静默失败」底线一致（雷/风/火正向）；副作用（(d) 行的行为变更）已自认并给出定性。扣分在「会话终止」面（跨提交）一度被排到最贵的候选里 | 已检查 | 候选集缺推荐，最强的一条（C 是补集）埋在 Alternatives 的并列结构里 |
| 炼金成分 | 4/5 | C# 对照 12/12 锚点属实、可当基准用；规范引用逐字准确；机制链基本完整。扣分在漏掉三处最有力的证据：跨提交 `Handles` 查得到前序属性的两处锚点（`Binder_Lookup.vb:579-584` + `SourceMemberMethodSymbol.vb:860-871`，它把 (iii) 从「待实现」变成「应报错」）、CS9045 的同闸范式、`AccessedThroughPropertyAttribute` 才是跨程序集契约 | 已检查 | 提案的核心论据之一是「移植不完整」，却漏掉了最有力的那条移植证据 |

**总判定**：**基本达成**——事实基线与四条路径刻画达成（可直接当决策基线；任务点名与正文引用的 30 余条锚点逐条核对，全部落在其所述符号体内，仅三处区间端点不精确）；候选定价的方向经两条独立路径复核后收敛，仅「同名替换」与「跨提交」两处被反向修正，且修正只影响文档措辞与后续排期，不影响 P-A1/C 的可交付性。**LDM 三态建议：Active**。**返工建议**：见 RESOLUTION R12–R17（提案冻结，修正只落纪要）。
