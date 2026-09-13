# Visual Basic Language Design Meeting
September 13, 2026

议题是 `proposal-top-level-implicit-shared`——**脚本/提交类的顶层成员默认按 `Shared` 处理**，把 `spec\spec-scripting-dialect.md:60` 的「顶层 `Sub`/`Function` 默认是实例成员」这半句反过来。这不是一条新的语法，而是一次**声明语义的默认值翻转**；它第二次来到我们面前（上一轮会议在「容器机制上是 class／心智是 module」的错配里给同一方向留了 `Table`，见 `meeting-script-extension-methods.md`），所以我们这一次没有从「要不要少写一个字」开始，而是从「翻开源码看这个字翻转之后，`spec` 里哪几句会失守」开始。

两条独立路径各读了一遍——一条沿 VB 基因问「`Shared` 在脚本类里是不是同一个意思、`Module` 的隐式共享凭什么可以搬过来、方言自己承诺过不动的那一轴动了没有」；一条沿 C# interop 问「这件事在 C# 里怎么分的、宿主契约与元数据面会不会失配、`D5` 的同形性读数是正是负」。两条路径在**事实基线**上独立收敛：提案的锚点密度是这批材料里最高的一档，我们逐条复核的承重行**没有一处判据不成立**；分歧也不在「能不能做」（`drawbacks` 与两路都同意机制可行），而在**同一句话**上——两条路径各自独立撞上、结论相同：**提案 Summary 用来支撑整个方向的「在出货面上没有可观察差异」不成立，而 §6 用来论证它的机制句把方向写反了。** 一份提案的 Summary 建立在一个反向前提上，这件事必须先被摆平，之后才轮到定价。

会议的实际内容落在复核撞出的几处：**§6 的机制句与提案 §3 自己声明的依赖（甲）相反**（共享初始化器本来就在 `.cctor` 里，默认共享是把每一个顶层 `Dim` 的初始化器**搬出** `<Initialize>` 的源码序，不是搬进去）；**`spec:64` 的「in source order」会因为这次搬迁而失守，且在 `.vbx` 单次运行内就能看见**；**`spec:35`/`:273`/`:277` 三句自陈不变式与这次翻转直接冲突**（与兼容性无关，D6 管不到）；**§4 的宿主对象修法实际是四处改动、不是两处**（`Binder_Expressions.vb:2612` 的一个条件同时把守着前序提交与宿主对象两个分支，降级侧两处 `Debug.Assert(Not _topMethod.IsShared)` 挡着）；**§2「B 比 ①a 少一道风险」那根唯一的支柱被源码削弱**（容器与属性语法在 `SourceMethodSymbol.vb:82` 是同一个作用域里的两个变量）；**`Module` 这个类比在「冗余 `Shared`」这一点上给出的是相反答案**（VB 对隐式共享容器的既有处理是把 `Shared` 判为错误）；以及**提案援引的经典 VBScript 前提，其自己的调查材料已经写明「无仓内锚点、不建议作为论据」**。另有一批正向发现是复核中新增的（提交类的元数据可见性放宽恰好把宿主 API 的发现路径保住、`<host-object>` 在反射面上确实是 `Public`），见「我们翻源码时……」一节。

> **来源标注**：正文引用的源码与规范文本均逐行复核（`文件:行号`）；提案 Detailed design 的承重锚点**逐一复核，无一处承重行或承重判据不成立**（规范 `:16`/`:48`/`:58`/`:60`/`:64`/`:66`/`:90-91`/`:131`/`:137`/`:163`/`:165`/`:169`/`:176`/`:234`/`:243`/`:311`、字段与属性的初始化器分桶、提交构造器分叉、入口点合成条件、绑定门槛、扩展方法两谓词、受限类型判据、发射层读写两侧、宿主契约与公开 API；逐项见「我们翻源码时……」，不复述为审计表）。复核中钉清更正了提案的四类表述问题（`spec:137`/`:165` 引行错、`EmitAddress.vb` 的 `HasHome` 分支数、§8 的 C# 可见性归属、§6 的机制方向），并在两处对**两条路径各自给出的结论本身**做了补充订正（§8 的 `Public` 不是「错」而是「理由错、观察对」；§2 的 ①a 顺序论据在源码层被削弱但注入点差异仍成立）。提案是冻结输入，对它的修正只写进 RESOLUTION。

> **本轮的实测**（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`，自报 `2.0.0-Beta+5816a5c`；探针写在系统临时目录、用后已删净，未写入仓库跟踪目录）：用脚本对自身提交类型做反射枚举其字段——`Submission#0` 上 `Dim x = 5` / `Dim y As Integer = 7` 产出的字段是 `pub=True|static=False|initonly=False`，合成的 `<host-object>` 与 `<Submission#0>` 是 `pub=True|initonly=True`（两条路径均 `EXIT=0`）。这一条把提案 §5「宿主 API 零改动」的**现状前提**从「沿提案自述」升级为已运行，同时也把 §8 那句 C# 事实的**真正机制**钉了出来（见下）。

## Agenda

* [Proposal: 脚本/提交类顶层成员默认 `Shared` / Implicitly Shared Top-Level Members](#proposal-脚本提交类顶层成员默认-shared--implicitly-shared-top-level-members)

## Proposal: 脚本/提交类顶层成员默认 `Shared` / Implicitly Shared Top-Level Members

_Related: [`../../proposals/inactive/proposal-top-level-implicit-shared.md`](../../proposals/inactive/proposal-top-level-implicit-shared.md)；被改写的语义 `../../spec/spec-scripting-dialect.md`（`:16` / `:48` / `:58` / `:60` / `:64` / `:66` 等）；前置依赖 `../../proposals/proposal-submission-shared-members.md` 与 `../meeting-submission-shared-members.md`（甲 + 乙 已裁定采纳、丙 否决）；同片区域的姊妹裁决 `../meeting-script-extension-methods.md`（方向 B「顶层成员默认共享」维持 Table）与 `../../proposals/proposal-script-extension-methods.md`；缺陷登记 `../../issues/issue-script-top-level-extension-method-crash.md`（issue 04）、`../../issues/issue-submission-shared-field-initializer-typeload.md`（issue 05）、`../../issues/issue-script-shared-field-await-initializer-crash.md`（issue 06）、`../../issues/issue-submission-implicit-type-member-asserts.md`（issue 07）、`../../issues/issue-submission-shared-member-implicit-me.md`（issue 08）、`../../issues/issue-initializer-diagnostic-does-not-gate-emit.md`（issue 09）；byref-like 既有定案 `../../spec/spec-byref-like-safety.md`（`:107` / `:237`）与 `../meeting-byref-like-repl-safety.md`（`:91`）；决策 `../../decisions.md` **D5**（基础功能以 C#/csi 为蓝本、须说明分叉理由）与 **D6**（兼容性约束只对 GA 成立，且**只**解掉兼容性一条）；上游合并面 `../../upstream-merge.md`_

### 场景与缺口

提案要消掉的那个别扭是真的：今天要写一个顶层扩展方法，必须手写 `Shared`，而「顶层的东西天然是共享的」是很多脚本作者的第一直觉；实现上顶层代码装在一个类里，成员默认实例。我们复核了这条路的**现状形状**，它比「别扭」更重一层：`SourceMethodSymbol.vb:1504` 与另一处同形的 `:1634` 是两处 `Debug.Assert(Me.IsShared)`，而**两处的守卫都不查 `IsShared`**（`:1500-1502` 只看 `MethodKind`、`AllowsExtensionMethods()`、`ParameterCount`；`:1627`/`:1630` 同族）。也就是说，一个不带 `Shared` 的顶层 `<Extension>` 成员走到的不是「写起来啰嗦」，而是**一条断言终止的路**——按提案自己的登记，这是 issue 04，一条**诊断缺口**。这一点改变了整场讨论的量级：收益侧不是「省一个关键字」，而是「让一条会崩的路变成一条报错的路」。

缺口还有一个更口语的形状，我们也认同：`spec:48` 把 `<Extension>` 只允许出现在标准模块或脚本类上（`NamedTypeSymbolExtensions.vb:108-111` 的 `TypeKind = Module OrElse IsScriptClass`），而标准模块里写 `Shared` 是**错误**（见下），脚本类里却**必须**写。同一个概念在两个容器里给出相反的仪式要求，这个不对称确实存在。

问题在**用什么去消它**。提案选的是把整个方言的默认声明语义翻一次；我们沿着源码把这次翻转的每一格都走了一遍，得出的结论是：**它买到的与它花掉的，不是一个量级。**

### 我们翻源码时，几处锚点改变了讨论的形状

**规范面：提案的引行全部命中，只有一处挂错了句子。** 我们逐行读了 `spec-scripting-dialect.md`：`:16`（映射表 `Sub`/`Function` 行的「an **instance member**, or a **`Shared` member** if declared `Shared`」）、`:48`（「a script class is instantiated — once by a submission's `<Factory>`, once by a script file's `<Main>` — and its top-level members are instance members unless they are declared `Shared`」）、`:58`（「so it has **instance state**」）、`:60`（「**Top-level `Sub` and `Function` are instance members unless declared `Shared`.**」）、`:64`（「the sequence of field initializers and global statements **in source order**」）、`:66`（「It does not extend into the body of a nested type」）、`:90-91`（`Dim value = Await Task.FromResult(13)` 的例子）、`:176`、`:234`/`:243`（BC36966）、`:311`——全部逐字命中，提案 §10 的「不动」清单（`:131`/`:137`/`:163`/`:165`/`:234`/`:243`）也逐条对得上。唯一一处偏差在 §1 末尾的注：提案写「`spec:137`/`:165` 已声明这两者的**形状**是实现细节、非契约」，但这两行本身没有这句话——那句话的原文在 **`:131`**（「The shape of the mechanism — the constructor parameter, the synthesized per-submission fields, and the name of the host object field — **is an implementation detail and is not part of the contract of this specification.**」）与 **`:169`**（Contract status 段：「They are **not a public contract** and cannot be referenced from source code」）。**事实判断是对的，引行错了**；既然这是一份锚点密度极高的提案，这类都能顺手纠准。

**§6 的机制句把方向写反了——而它正是 Summary 的承重句。** 提案 §6 逐字：「默认共享之下，共享字段的初始化时机从 `.cctor`（一次）变成 `<Initialize>`（每次运行）——**而那正是实例字段今天的行为**。」我们按源码走了一遍，答案是相反的：

- 初始化器的分桶**只看 `IsShared`、不看用户写没写关键字**：`SourceMemberFieldSymbol.vb:628-632`（数组隐式初始化器）与 `:665-673`（`= …` 形态，`:669` 注释逐字「const fields are implicitly shared and get into this list.」），属性的同名分桶在 `SourceMemberContainerTypeSymbol.vb:2669-2681`。
- 静态桶交给 `AddDefaultConstructorIfNeeded(membersBuilder, True, StaticInitializers, diagnostics)`（`SourceMemberContainerTypeSymbol.vb:2505`），提交类分支在 `:2726-2737` 造一个 `isShared:=True` 的构造器符号（`:2735`）。
- 它的 `MethodKind` 由 `SynthesizedConstructorBase.vb:192` 给出：`Return If(m_isShared, MethodKind.SharedConstructor, MethodKind.Constructor)`；而 `MethodSymbol.vb:517-521` 的 `IsScriptConstructor` 判据是 `MethodKind = MethodKind.Constructor AndAlso ContainingType.IsScriptClass` ——**共享构造器 `IsScriptConstructor` 恒假**。于是 `MethodCompiler.vb:1481-1487`（`If method.MethodKind = Constructor OrElse SharedConstructor Then If method.IsScriptConstructor Then body = block Else body = BuildConstructorBody(…)`）把静态桶交给 `BuildConstructorBody` 注入方法体，而 `:695-698` 的分派也把 `MethodKind.SharedConstructor` 指向 `processedStaticInitializers`。

⇒ **今天共享字段的初始化器就已经在 `.cctor` 里**（issue 05 之所以是 exit 34，正是因为那个 `.cctor` 被造出了形参——见 `meeting-submission-shared-members.md` 的甲）。默认共享要做的事，是把**每一个**带初始化器的顶层 `Dim` 从 `<Initialize>` 的源码序里**搬出去**、搬进 `.cctor`。提案 §6 说的方向（`.cctor` → `<Initialize>`）是上一场会议**明确否决掉的候选「丙」**的机制，提案自己也没有把它列为依赖。同一份文件里 §3 声明依赖甲、§5 与 Drawbacks 都按 `.cctor` 语义写（「重跑**不回到初值**」），只有 §6 按 `<Initialize>` 语义写——**两者不可能同时成立**。

**`spec:64` 的顺序承诺会失守，而且这是 `.vbx` 单次运行就能看见的。** 这是上面那条的直接推论，我们把链条走到了底：今天顶层字段初始化器与顶层语句**同住实例桶、按源码位置排序**——`SourceMemberContainerTypeSymbol.vb:1571` 的注释逐字「initializers should be added in syntax order」，`:1572-1573` 两条 `Debug.Assert`；顶层可执行语句在 `:2626-2633` 追加到**同一个** `instanceInitializers`；`MethodCompiler.vb:599-607` 用**同一个** `scriptInitializer` 绑两条桶，`:1488-1490` 由 `BuildScriptInitializerBody` 把实例桶写进 `<Initialize>` 的方法体。这正是 `spec:64` 逐字承诺的「the sequence of field initializers and global statements **in source order**」。默认共享之后，那一行 `Dim x = 5` 的初始化器离开这条序列、进 `.cctor`；而 `.cctor` 必然在 `<Initialize>` 被调用**之前**跑完——`SynthesizedEntryPointSymbol.vb:340-389` 的 `<Factory>` 体是「`New Submission#N(…)`（`:360-372`）→ `submission.<Initialize>()`（`:374-382`）」，类型初始化先于实例创建。于是脚本里一行**写在声明之前**的 `Console.WriteLine(x)`，会从读到默认值变成读到初始值。提案 §6 列的三个场景（`.vbx` 每次新进程 / REPL 后产生的提交不重跑前序 / `#Load` 合成一个提交）**论证的都是「初始化器不重跑」，没有一条论证「同一次运行内的执行时机不变」**——前半句我们同意，它是真的、也有用；从它推出「所以在出货面上没有可观察差异」这一步不成立。

我们的证据等级不含糊：机制是**已检查（源码逐行）**，今天的顺序基线是**已检查（源码 + 上一场会议的同类代理实测）**；**「提案形态下的端到端输出」是「待定」**——没有人在默认共享实现上跑过这个形状，提案也没有。也就是说：「零差异」这个**全称主张**目前拿不出证据，而它有一条方向明确的源码级反例。按纪律，全称主张举不出证据即降级。

**这次翻转动的正是方言自陈「不动」的那一轴。** `spec-scripting-dialect.md` 的 Soundness 一节是这份 spec 对自己性质的承诺，三句逐字：`:35`「The four top-level forms keep their ordinary spelling **and meaning**; **only their container changes**.」；`:273`「The model is **a containerization, not a new declaration system**. Every top-level form keeps its ordinary spelling **and its ordinary meaning**; the only change is the container the declaration lands in.」；`:277`「A program that is legal both as a script and as ordinary code **has the same meaning in both**; the differences are the placement of declarations, the synthesized entry point, and the result rule for typed submissions.」提案 §1 自己也承认「默认共享**只改成员的默认共享性这一轴**」——而「成员的默认共享性」正是这三句里的 meaning。原版 spec 把这件事写成了定义句：`vblang\spec\type-members.md:1825`「A variable declared **with** the `Shared` modifier is a *shared variable*.」、`:1866`「A variable declared **without** the `Shared` modifier is called an *instance variable*.」；`:551` 同族还写明「It is invalid to refer to `Me`, `MyClass`, or `MyBase` in a shared method.」**这一条与兼容性无关**（不涉及任何既有代码会不会坏），所以 D6 对它没有作用；它是一份 spec 与它自己不变式之间的矛盾，落在 D6 明文保留的「规范的可表达性 / 与既有不变式冲突」一类里。`spec:284` 恰好记着 C# LDM 的那句警告——a scripting dialect can become "a third dialect" of the language（`[LDM-2020-02-26]`）——这句警告就落在这把刀上。

**`Module` 这个类比只借到了一半，而且另一半给出的是相反答案。** 提案的 Motivation 用模块做心智支点。我们复核了 VB 对「成员一律隐式共享」的容器的既有处理：`types.md:689` 逐字「A *standard module* is a type whose members are **implicitly `Shared`**…**Standard modules may never be instantiated.**」；`type-members.md:551` 末句逐字「Methods defined in standard modules and interfaces **may not specify `Shared`**, because they are implicitly `Shared` already.」；落实到实现是 `DeclarationModifiers.vb:55`（及 `Binder_Utils.vb:1605`）的 `InvalidInModule = [Protected] Or [Shared] Or [Default] Or …`，字段侧由 `SourceMemberFieldSymbol.vb:453-468` 报 `ERR_ModuleCantUseVariableSpecifier1`。⇒ **VB 对「冗余的 `Shared`」的既有答案是报错，不是静默接受**。而 `spec:48` 已明文写「A script class is an ordinary class and **not a standard module**: … whereas a script class is **instantiated**」。所以模块能隐式共享，前提是**它不可实例化、没有实例状态可失去、成员作用域落在命名空间里**；脚本类这三条一条都没有（它被实例化、有宿主对象与前序提交状态、容器名 `Submission#N` 在源码里拼不出来）。提案 §Summary 的边界句「`Shared` 仍可写（写了也是共享，不是错误）」**静默地选了与模块先例相反的那一支，并且把它当作「不是错误」一笔带过**——而这个选择一旦落进规范，`spec:16`/`:48` 改完之后「`Shared` 在脚本类里是什么意思」将无法从规范回答。我们要求：要么照模块先例给「脚本类顶层的 `Shared` 是冗余修饰符」的诊断，要么显式写明允许并给出理由——**不能在规范里留空**。

**Motivation 的唯一 VB 特有理由，其自己的调查材料已经把它标成猜测。** 提案 Motivation 的第一句是「经典 VBScript 只有全局变量与过程，**没有类成员这一层**；顶层『东西天然共享』是 VB 用户的历史直觉」。我们翻到本项目的工作材料 `tmp\meetings\script-extension-methods\investigation-instance-vs-shared.md` §3.1，它逐字写着：「「经典 VBScript 的顶层就是共享」这一类比，本文**找不到仓内锚点**：VBScript 语言没有 `Shared` 关键字，也没有标准模块。→ 该类比**标猜测**（无锚点），**不建议作为论据**。」——**从「猜测、不建议作为论据」到 Motivation 的第一句话，中间没有任何新证据。** 与之相对，同一条 VBScript 基因里有一条**方向相反**且有锚点的：经典 VBScript 的唯一执行模型是语句自上而下，它没有「字段初始化器」这种东西；而这次翻转把 `Dim x = 5` 变成一条「在脚本开始前就执行完」的声明——这正是上面 `spec:64` 那条。⇒ 提案取了 VBScript 基因的一半，切掉了另一半。**VB 特有理由目前是猜测级的**；按 D5 的口径，这不是「充分性待裁」，是「这条理由目前没有证据支撑」。

**§4 的宿主对象修法，改动面比提案给的大一倍。** 提案 §4 说要把 `<host-object>` 字段共享化、并「必须同时放开 `isReadOnly:=True`」——后半句对（`Lowering\SynthesizedSubmissionFields.vb:57` 逐字 `accessibility:=Accessibility.Private, isReadOnly:=True, isShared:=False`，两者同行）。但 `Binder_Expressions.vb:2611-2612` 的那个条件**不能整体放开**，它同时把守着两个分支：前序提交（`:2613-2614` → `BoundPreviousSubmissionReference`）与宿主对象（`:2616-2625` → `BoundHostObjectMemberReference`）。降级侧两处都要求引用点所在成员**非共享**：`LocalRewriter_PreviousSubmissionReference.vb:16` 逐字 `Debug.Assert(Not _topMethod.IsShared)`、`LocalRewriter_HostObjectMemberReference.vb:15` 逐字 `Debug.Assert(Not _topMethod.IsShared)`，而两者都造 `New BoundMeReference(...)` + `BoundFieldAccess(Me.<字段>)`（`:21-22` 与 `:19-20`）。⇒ 这条修法是**四处**：(a) 字段共享性、(b) `isReadOnly`、(c) 把 `:2612` 拆成「宿主对象不看共享性」+「前序提交仍要求非共享」两条判据、(d) 把宿主对象那一支的降级改成**无接收者的静态字段访问**（`EmitExpression.vb:2196` 的 `If field.IsShared Then Stsfld Else Stfld` 那条已在别处被证明可行的路）。这恰好是 Unresolved 1 悬着的那一格，成本翻倍对规划有直接价值。顺带确认提案 §4「编译期 BC30469、不是崩」成立：宿主对象是实例只读字段，在共享成员体里被 `:2612` 拦住即报 BC30469。

**`spec:66` 的反转是单向的放宽，但放宽的正是最该守住的那一边。** 默认共享之后，嵌套类型里的代码会**不限定名**读到顶层成员（`:2612` 的把守对共享成员放行）。这不算失控：BC36966 仍拒显式 `Me`/`MyBase`/`MyClass`，而 `Binder_Expressions.vb:2263` 只查 `IsScriptClass`、不查共享性，所以嵌套类型**写不出**容器限定名。但 `spec:66` 存在的理由是「嵌套类型里没有指向脚本类实例的隐式接收者」，反转后两者通过**类型级静态字段**重新接通：一个嵌套类型的实例，在没有任何接收者的情况下读写脚本顶层的**可变**状态；而这状态在源码里**没有名字可写**（`spec:50`/`:66` 的 `Submission#N`；提交形态下 `Script` 名字会报 BC30451）。模块也有这个性质，但模块的补偿是「不可实例化 + 作用域落在命名空间」——脚本类两条都没有。**结果是一个本来能靠限定名说清楚的共享状态，变成一个只能隐式碰到的共享状态**；在 VB 的语境里这是可读性的净损失。若会议仍要走这个方向，`spec:66` 的反转不能只写成「作用域放宽」，必须同时写清**为什么嵌套类型可以隐式碰到一个它无法命名的容器的状态**——这条叙事今天不存在。

**§2 那根「B 比 ①a 省」的支柱，被源码削弱了。** 提案 §2 与 Alternatives C 的论据是：①a（给带 `<Extension>` 的顶层成员隐式 `Shared`）的判据**必须**落在属性识别处，而属性识别晚于修饰符判定。两处引用都命中——`Binder_Utils.vb:85` 的 `MapKeywordToFlag(syntax As SyntaxToken)` 确实只收 token；`SourceMemberMethodSymbol.vb:85-89` 的属性识别确实在符号构造期，`:86` 也确实在问 `containingType.AllowsExtensionMethods()`。但「**必须**」这一步不成立：`SourceMethodSymbol.vb:77-83` 的 `CreateRegularMethod(container As SourceMemberContainerTypeSymbol, syntax As MethodStatementSyntax, binder, diagBag)` 里，`:82` 第一句就是 `DecodeMethodModifiers(syntax.Modifiers, container, binder, diagBag)`，`:83` 紧接着算 `flags`——**`syntax.AttributeLists` 与 `container` 在 `:82` 是同一个作用域里的两个变量**，而 `flags` 是在 `MyBase.New(containingMethodType, flags, …)`（`SourceMemberMethodSymbol.vb:77`）之前本地算出来的。⇒ ①a 不是「信息拿不到」，而是「判据写在哪一层的工程选择」。B 的注入点与影响面差异仍然成立（容器确实是 `DecodeMethodModifiers` 的入参，见 `SourceMethodSymbol.vb:417-420`），但「少一道风险」那句话只剩不下支柱。**这一条不改变 B 与 ①a 的最终取舍**，但它拿掉的是提案用来给 B 加分的唯一理由。

**两处正向发现（复核新增，它们对提案有利）。** 其一，§7 的 ref struct 空结果**成立**：VB 的受限类型判据 `SourceMemberFieldSymbol.vb:140-145` 只看类型（`:142` `If varType.IsRefLikeOrAllowsRefLikeTypeOrArrayType(restrictedType) Then`，判据里没有 `IsShared`），C# 侧反而更严（`SourceMemberFieldSymbol.cs:67` 的 `this.IsStatic` 是短路第一项）。既有定案依据的是「**字段**」而非「实例字段」：`spec-byref-like-safety.md:107` 逐字「because the declaration would become a field of the script class and byref-like types cannot be fields」、`:237`「Fields of classes and **static fields** cannot have a byref-like type」。默认共享对顶层 ref struct **既不变严也不变松**——这是一条真正的零代价轴，记在正账。其二，**§5「宿主 API 零改动」成立，而且理由比提案写的更硬**：提案 §8 的说法是「`readonly int x = 5;` 在脚本里字段是 `Public, InitOnly`」，我们把这句话拆成两层看——**声明**上的默认可见性两边都是 `Private`（C# 见 `SourceMemberFieldSymbol.cs:211-212` 的 `isInterface ? Public : Private`；VB 见 `SourceMemberFieldSymbol.vb:436` 的 `If(container.IsValueType, Accessibility.Public, Accessibility.Private)`），但**发射的元数据可见性**两边都会对提交类放宽：`Symbol.vb:97-102`（VB）与 `Symbol.cs:256-265`（C#）逐字写「We need to relax visibility of members in interactive submissions since they might be emitted into multiple assemblies」，判据是 `ContainingType.TypeKind = TypeKind.Submission`。所以我们实测的那份反射输出里，`Submission#0` 的 `x`/`y` 与合成的 `<host-object>`/`<Submission#0>` **全部是 `IsPublic=true`**。⇒ 提案 §8 那句话**观察对、归属错**（两条路径都把 `Public` 判为写错，这里要把口径补成两层；见 RESOLUTION 7）。而它给 §5 的结论是加分的：放宽的判据是 `TypeKind.Submission`，而本提案**明说不改容器种类**，所以共享化之后顶层字段仍在提交类里、元数据可见性仍是 `Public`，`ScriptState.cs:106-113` 的 `DeclaredFields` + `field.IsPublic` 过滤器照旧找得到它，`ScriptVariable.cs:55`/`:70` 的 `_field.GetValue(_instance)` / `SetValue(_instance, value)` 对静态字段会忽略实例参数 ⇒ 公开 API 一行不改这条**成立**。这是提案 §5 的正面证据，只是它的机制不是提案写的那一个。

> **顺带记录一条本次观察到的、与方向无关的机制事实**：`Symbol.vb:93-116` 的字段级放宽是按 `TypeKind.Submission` 分叉的，而 `spec:98-102` 说明非提交脚本类是 `DeclarationKind.Script` + `TypeKind.Class`。也就是说两种 kind 在**元数据可见性**上本就有一处可观察差异（提交类成员一律放宽为 `Public`，非提交脚本类不）。这与提案 Unresolved 5 是同一片区域，我们把它作为该 Unresolved 必须显式裁定的又一条理由。

### 候选逐一权衡

**A（维持现状，顶层 = 实例）：我们不把它当作「什么都不做」。** 提案给它记的代价是「顶层扩展方法**永久**必须手写 `Shared`」——这条我们按上面复核改写了：缺 `Shared` 的现状不是「啰嗦」，是 `SourceMethodSymbol.vb:1504`/`:1634` 两处断言终止（issue 04）。把它当成「缺一条诊断」而不是「缺一个默认值」，A 的代价就从「永久仪式」缩成**一条诊断的形状设计**。这是本次讨论里最关键的一次重新定价。

**B（本提案，顶层默认共享）：我们维持 Table。** 把它的优点记全：机制可行（`meeting-submission-shared-members` 已复核丙的三段同向，甲 + 默认共享只是把同样的分桶判据普遍化）；宿主 API 零改动（本轮的反射实测支持）；入口点与跨提交机制不动（`spec:169` 的 `<Initialize>`/`<Main>`/`<Factory>` 拼写不变）；ref struct 一轴是空结果。但四类理由压过来，且**全部落在 D6 明文保留的四类里**（我们不使用兼容性）：

1. **与 spec 自陈不变式冲突**（规范可表达性）：`spec:35`/`:273`/`:277` 三句承诺「只改容器、不改 spelling 与 meaning」，而这次翻转改的正是 meaning。要保留方向就必须把 Soundness 一节改写成**显式偏离**（照 `Imports` 累积偏离的既有做法：写明「这是刻意的、限于方言、理由是 X」），而不是只把 `:60` 那半句反过来。
2. **§6 的机制方向写反**（机制收益与代价）：共享初始化器本来就在 `.cctor`，默认共享是把顶层 `Dim` 的初始化器**搬出**源码序。这条不澄清，会议就是在「零差异」这个错误前提上定价。
3. **收益与代价不成比例**：收益是顶层扩展方法少写一个 `Shared`（且如上所述，A + 诊断能以更低成本买到同一份用户可见价值）；代价是一次声明语义翻转、`spec:64`/`:66` 两句明文承诺的改写、`.cctor` 成为最普通一行 `Dim x = 5` 的必经之路、宿主对象类型级化（并发/重入互相覆盖，而兜住它的凭据只有「出货面上看不到」，那是一份对宿主行为的假设清单）、以及被 05/06 硬阻塞（提案自认）。
4. **与 D5 的同形性反向**：C# 保留了共享/实例这条轴，`static` 是显式 opt-in 且有用户可见后果（静态上下文拿不到脚本状态）；C# 的静态初始化器构造器与提交构造器是**两个类**（D5 已证实例 1），`MethodCompiler.cs:547-550` 逐字把静态桶只交给 `MethodKind.StaticConstructor`。本提案把这条轴整个删掉、且**不提供任何反向拼法**（写了 `Shared` 仍是共享）——这是**能力删除**，不是默认翻转。`Shared` 在顶层随之退化为同义反复。

**C（①a，只对带 `<Extension>` 的顶层成员隐式 `Shared`）：比 B 窄，但它的「顺序风险」被削弱之后，它反而是成本账更清楚的那一个。** 它买到与 B 相同的直接收益（顶层扩展方法不必写 `Shared`），对缺口 ①②③④ 零收益，也不动声明语义。我们不再用「唯一的实现风险是顺序」来描述它：判据写在哪一层是工程选择（见上），但它**不触碰** `spec:35`/`:273`/`:277` 这条线，这是它与 B 的实质分野。

**D（容器种类改 `Module`，即 ③）：已两次否决，本提案也不采用。** 我们复核了提案与被否方向的分界：`SourceMemberContainerTypeSymbol.vb:147-180` 的 `Select Case` 一个字不动、`Binder_Lookup.vb:583-584` 的跨提交入口不动、`MethodCompiler.vb:564` 的 `IsScriptClass` 分支不动——分界做得干净，**不要拿 ③ 的否决理由来拒本方向**。按姊妹会议的复会裁决，容器保持 `TypeKind.Submission`。

**真正要比较的是 B 与「A + issue 04 诊断 + 甲 + 乙」。** 后者买到本方向 90% 的用户可见价值（错误变响、能力完整），不动一行声明语义，不加一次全局翻转，不欠 `spec:35`/`:273`/`:277` 一笔账。我们把它写进 RESOLUTION 作为推荐路线。

### VB 基因对照

- **保持 VB-like**：`vblang\meetings\2018\vbldm-notes-2018.06.13.md:33` 逐字「We strongly believe that Visual Basic has a stance - a way of doing things. We will strive to maintain consistency with things being "VB-like"」；`:34` 逐字「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」。**这两句与兼容性无关，D6 管不到它们。** 一个「看不见名字的类型级可变状态」、一个在脚本里退化为同义反复的修饰符，都不在这把尺子的舒适区里。
- **`Shared` 承载的语义被取消**：`type-members.md:551` 的「It is invalid to refer to `Me`, `MyClass`, or `MyBase` in a shared method.」在脚本里就是「写 `Shared` = 你放弃了宿主对象、放弃了实例状态」。默认共享之后，脚本作者**再没有任何顶层写法**（除非嵌套一个 `Class`）能声明一个访问宿主对象的顶层 `Sub`；提案的修法是把宿主对象也变成类型级一份——那是**用第二个语义收窄去补第一个**。`Shared` 不是变成另一个意思（那是上一场会议否决丙的核心理由），而是变成一句恒真的废话，同时把「能碰实例状态」这个**能力**从作者手里拿走。
- **与 VBScript 基因的关系**：如上一节，唯一被援引的那半条基因无锚点、且其另一半（语句自上而下）与本次翻转相反。
- **与主线关系**：主线对扩展表面积设高门槛（`evaluation-standard.md` §2.3 的「主线保守」），而对脚本方言没有任何成文计划要求翻转默认共享性——本方向是产品侧的独立延伸，不在主线议程上。

### C# 生态对照

先记结论：**这一提案在 C# interop 面上是安全的**，这是它真正的优点。元数据契约与宿主契约不动：`spec:169` 把 `<Initialize>`/`<Main>`/`<Factory>` 的拼写声明为「spelled identically in the C# scripting implementation, so tooling … can rely on the spelling being stable across languages」，提案 §1 明说不碰它们，我们复核 `SourceMemberContainerTypeSymbol.vb:2761` 与 `SynthesizedInteractiveInitializerMethod.vb:51-55`/`:87-91` 确认现状即 C# 同形；跨提交查找按 `TypeKind.Submission` 分派（`Binder_Lookup.vb:583-584`），静态/实例的分叉发生在拿到符号之后的接收者合成（`Binder_Expressions.vb:2612`），所以顶层成员全变静态后跨提交这一面只会更简单。本树 `Scripting\` 下只有 `Core`/`VisualBasic`/`VisualBasicTest`/`VisualBasicTest.Desktop`（已列目录确认），没有 C# 宿主，「C# 侧消费 VB 脚本类」不是本树的活动场景。

但 interop 面有两条必须定价的后果，它们不是「可观察/不可观察」，是**形状选择**：

- **本提案删掉了共享/实例这条轴，C# 保留它。** C# 的顶层语句模式下顶层变量是**局部**（`SimpleProgramBinder.cs:26-39` 的 `BuildLocals` 把 `GlobalStatementSyntax` 逐个收进 `locals`），脚本模式下是实例成员、`static` 才是显式 opt-in。本提案反向设默认且**无 opt-out**：永久失去「声明一个顶层实例成员」的能力，`Shared` 退化为同义反复。这也意味着 §4 的宿主对象共享化**不是选项而是被迫的后果**——顶层 `Sub` 现在是共享成员，`Print`/`Args` 必须从共享上下文可达。
- **宿主对象的形状必须过一次 D5 论证，而现在没有。** C# 在同一位置是「实例 `readonly` 字段 + 真构造器赋值」（与 VB 现状同形：`SynthesizedSubmissionFields.vb:57` + `SynthesizedSubmissionConstructorSymbol.vb:84-97`）。VB 若改成「静态 + 非只读」，须写明为什么不追平 C#，以及类型级一份在「同一 `Script` 重复/并发运行」下的可观察后果。**这是唯一一条会打到 interop 语义（宿主模型）的改动**——而它恰好又是 `spec:361`「A failed submission does not change the state of the session」在宿主对象这一格上的重验点。

再记一条 C# 侧的对位事实，它让「分叉」的代价更清楚：C# 对**共享**初始化器里的 `Await` 是**报错**（CS8100），而 VB 今天选择崩溃；上一场会议已裁定往「从崩改成报错」走（乙），这与 C# 同向。本提案不改变这条线。

## RESOLUTION

1. **方向「顶层成员默认共享」维持 `Table`；本提案不采纳为当前方向。** 我们不用兼容性作为理由（D6 明文只解掉兼容性一条，而本方向今天也没有「在用语义」会被破坏）。四类理由**全部落在 D6 明文保留的范围**里：(a) **与 spec 自陈不变式冲突**（`spec:35`/`:273`/`:277`「只改容器、不改 spelling 与 meaning」；规范可表达性）；(b) **Summary 的承重主张不成立**——「在出货面上没有可观察差异」举不出证据且有一条方向明确的源码级反例（`spec:64` 的源码序在该字段上失守，同一次 `.vbx` 运行内可见），(c) **机制收益与代价不成比例**（收益＝少写一个 `Shared`；代价＝声明语义翻转 + `spec:64`/`:66` 改写 + `.cctor` 成为最普通一行 `Dim` 的必经之路 + 宿主对象类型级化 + 被 05/06 阻塞）；(d) **与 D5 的同形性反向**（C# 保留共享/实例轴、`static` 是 opt-in；本提案是无 opt-out 的能力删除）。
2. **必须先完成的三条（本方向若复活，缺一不得上会）**：
   - **(必须 1) 重写 §6 的机制句**：共享字段的初始化器**今天就在 `.cctor`**（`SourceMemberFieldSymbol.vb:628-632`/`:665-673` → `SourceMemberContainerTypeSymbol.vb:2505` → `:2726-2737`；`SynthesizedConstructorBase.vb:192` + `MethodSymbol.vb:517-521` 使共享构造器 `IsScriptConstructor` 恒假 → `MethodCompiler.vb:1481-1487` 注入 `.cctor` 方法体）。默认共享的真实形状是「**每一个**顶层 `Dim` 的初始化器从 `<Initialize>` 的源码序搬进 `.cctor`」，方向与 §6 相反；同时删掉 Drawbacks 第三段里的「改道」措辞（那是已被 `meeting-submission-shared-members` RESOLUTION 3 否决的候选「丙」的机制）。
   - **(必须 2) 降级 Summary 的「零差异」并补进 Drawbacks**：`spec:64` 的顺序承诺在该字段上失守，机制**已检查**、基线**已检查**（含上一场会议的同类代理实测）；只保留「提案形态的端到端输出未跑」这一句为「待定」。§6 的三个场景**一个都不要删**（它们证明「初始化器不重跑」，是对的、有用的），删的是从它们推出「所以没有差异」这一步。
   - **(必须 3) 把 §4 的宿主对象修法按四处定价**：字段共享性 + 放开 `isReadOnly` + 把 `Binder_Expressions.vb:2612` 拆成「宿主对象不看共享性」与「前序提交仍要求非共享」两条 + 把宿主对象那一支的降级改成无接收者的静态字段访问（`LocalRewriter_HostObjectMemberReference.vb:15`/`:19-20` 与 `LocalRewriter_PreviousSubmissionReference.vb:16` 两处 `Debug.Assert(Not _topMethod.IsShared)` 与 `BoundMeReference` 依附都要处理）。并给 Unresolved 1 补一条后果：宿主对象类型级化后，`spec:361` 的会话隔离在该格上需重验。
3. **应当完成的两条**：
   - **(应当 1) Motivation 的经典 VBScript 类比必须补锚点或降级为猜测**：本项目自己的调查材料已写明该类比「找不到仓内锚点……不建议作为论据」，而它是 D5 分叉理由的唯一支柱；同时补上方向相反的那半条基因（VBScript 的执行模型是语句自上而下，`Dim x = 5` 的执行时机变化与该基因相反）。
   - **(应当 2) §2 与 Alternatives C 的「①a 唯一的实现风险是顺序」收窄为「判据写在哪一层的工程选择」**：依据 `SourceMethodSymbol.vb:77-83`（`syntax` 与 `container` 同在 `:82`）与 `SourceMemberMethodSymbol.vb:85-86`（`binder` 与 `containingType` 同在符号构造期）。B 与 ①a 的注入点/影响面差异仍成立，但不再依赖一个被源码削弱的顺序风险。
4. **推荐路线（本方向不采纳时的替代，我们建议按它走）**：**维持实例基线 + issue 04 补诊断 + 甲 + 乙**。
   - **issue 04 升为独立立项、修法取「诊断而非断言」**：顶层不带 `Shared` 的 `<Extension>` 成员今天走到 `SourceMethodSymbol.vb:1504`/`:1634` 两处 `Debug.Assert(Me.IsShared)`（守卫均不查 `IsShared`），目标定为「给一条可定位的诊断」。这买到「顶层扩展方法必须手写 `Shared`」这条抱怨的**全部用户可见价值**——那本来是一条诊断缺口，不是一条默认值缺口。
   - **甲 + 乙 按 `meeting-submission-shared-members` 的裁决落地**（甲＝按 `isShared` 在 `:2726-2737` 分叉出独立的静态构造器符号；乙＝移植 CS8100 语义、给共享字段/属性初始化器里的 `Await` 报新码），仍是本提案的前置，且独立成立。
5. **若日后复活本方向，规范措辞的两条硬要求**：(a) `spec:66` 的反转**不能**只写「作用域放宽」，必须新写一条理由说明「嵌套类型为什么可以隐式碰到一个它无法命名的容器的状态」（脚本类**不是**模块，`spec:48` 明文）；(b) `spec:64` 的「in source order」要明文限定为「实例初始化器与顶层语句」，共享初始化器的时机（类型级、先于首次引用）单独写一句。
6. **边界裁定（与是否采纳无关，现在就要钉）**：
   - **注入点必须写死 `DeclarationKind.Submission` 还是 `IsScriptClass`**：非提交脚本类（`DeclarationKind.Script`）今天健康，而分桶点（`SourceMemberFieldSymbol.vb:628-632`/`:665-673`、`SourceMemberContainerTypeSymbol.vb:2669-2681`）**不含容器种类门**；且元数据可见性放宽（`Symbol.vb:97-102`）按 `TypeKind.Submission` 分叉，两种 kind 本就有可观察差异。要求给「非提交脚本类 + 顶层 `Shared` 字段带初始化器」一条独立验证用例。
   - **冗余 `Shared` 要显式裁一次**：照模块先例（`DeclarationModifiers.vb:55`、`Binder_Utils.vb:1605`、`SourceMemberFieldSymbol.vb:453-468` 的 `ERR_ModuleCantUseVariableSpecifier1`）报诊断，还是明文允许；写进规范影响面。
7. **口径订正（写进纪要，不改提案）**：
   - 提案 §8「`readonly int x = 5;` 在脚本里字段是 `Public, InitOnly`」应拆成两层：**声明**上的默认可见性两边都是 `Private`（C# `SourceMemberFieldSymbol.cs:211-212`；VB `SourceMemberFieldSymbol.vb:436`），**发射**的元数据可见性对 `TypeKind.Submission` 放宽为 `Public`（`Symbol.cs:256-265`、`Symbol.vb:97-102`）。本轮反射实测（`EXIT=0`）显示 `Submission#0` 的 `Dim x`/`Dim y` 与 `<host-object>` 均为 `IsPublic=true`。⇒ 「观察对、归属错」。
   - 提案 §1 末注的 `spec:137`/`:165` 应改为 `:131`/`:169`（事实判断不变）。
   - 提案 §8 与全称主张 #11 的 `EmitAddress.vb:261-284` 的 `HasHome` 是 **5** 个分支（const / 非只读 / 容器不符 / 共享 / 否则），不是「6 档判据」。
   - 引行口径以提案为准的两处（与 `ruling-instance-vs-shared.md` 的行号口径不同）：`SourceMemberFieldSymbol.vb:140-145` 与 `ScriptTests.vb:136-149`——**提案两处都对**（`ScriptTests.vb:135-148` 是那份工作材料的一行偏移；本轮逐行复核 `:141` 写值断言、`:144` 重跑回初值断言、`:147` 续跑断言三条俱在）。
8. **正面记录（保留为提案资产，与方向取舍无关）**：§7 的 ref struct 空结果（`SourceMemberFieldSymbol.vb:140-145` 无 `IsShared` 分支；C# `SourceMemberFieldSymbol.cs:67` 含 `this.IsStatic` 故静态更严；`spec-byref-like-safety.md:107`/`:237` 的依据是「字段」而非「实例字段」）、§9 的两条模式分野（`SimpleProgramBinder.cs:26-39`；`SimpleProgram` 在 VB 全树零命中）、§8 的只读无硬墙与它主动作出的自纠（`HasHome` 只管要地址的形状，`EmitExpression.vb:2193-2203` 的 `EmitFieldStore` 不查 `HasHome`）、§12 的测试策略（必须避开 `Object` 接收者盲区——`Binder_Lookup.vb:1176-1181` 的 `Not container.IsObjectType()` 加上 `VisualBasicScriptCompiler.vb:218` 写死的 `optionStrict:=OptionStrict.Off`，会让「找不到成员」静默降级为晚绑定；以及把已知未通过校验的形状用 `Verification.FailsPEVerify` 显式锁住）。这些我们复核成立，plan 阶段可独立使用。
9. **登记面**：本方向维持 `Table` 期间**不改** `spec` 与 `upstream-merge.md`；issue 04 升为独立立项时按 `proposal-submission-shared-members.md` §7b 的方式补登记，并注意 `upstream-merge.md`「二·补、已知欠账」节在合并前须逐 diff 复核。提案相对 `upstream-merge.md` 的改动面若日后复活，须补编译器侧文件级锚点。

## Implication

落定之后，`spec-scripting-dialect.md` 的 Soundness 一节保住了它对自己的承诺：方言仍然只改容器、不改 spelling 与 meaning，`Imports` 累积仍是**唯一**一条明文写下的偏离。`Shared` 在脚本类里继续承载它在本语言其余部分承载的那句语义（类型级、先于首次引用；`type-members.md:1269`/`:2021`），而「顶层扩展方法要写 `Shared`」这条抱怨，我们把它从「一个要用全局默认值去换的设计取舍」降级为「一条要补的诊断」。

我们放弃的是「让顶层省掉一个 `Shared`」这个便利。我们清楚这次放弃的边界：不是「不可行」（机制可行，两路都同意），不是「怕 breaking change」（D6 让那条论据不成立，而且本方向今天也没有在用语义可破坏），而是因为**它要求方言把自己承诺过不动的那一轴动掉**，而这个代价买到的东西（少写一个字）与它花掉的东西（语言概念的单一性、`spec:64` 的顺序承诺、`spec:66` 的作用域隔离、宿主对象的实例性、`ReadOnly` 与 `Shared` 组合的完整性）不成比例。复活条件写在 RESOLUTION 2 与 5 里；如果将来产品侧真的需要这一格，请带着完成后的三条必须项回来。

对相邻单元的影响：issue 04 与 issue 05/06/07/08/09 各自独立、分别立项，不要把多条并进同一份设计；`spec:66`/`spec:64` 的措辞改动若发生，按 RESOLUTION 5 的两条硬要求走。

## OPEN QUESTIONS / TODO / Follow-up

* **OPEN**：`spec:35`/`:273`/`:277` 与本次翻转的关系要不要正式写成一条**显式偏离**（照 `Imports` 累积偏离的写法）？本轮只在 RESOLUTION 1(a) 记下冲突，未定偏离措辞。
* **OPEN**：冗余 `Shared` 的裁决（报诊断 vs 明文允许）——对应 RESOLUTION 6 的第二条。模块先例给的是报错；若选报错，本方向的用户可见面会再缩一分。
* **OPEN**：非提交脚本类（`DeclarationKind.Script`）要不要随方向一起改——对应 RESOLUTION 6 的第一条；本轮新增一条依据（元数据可见性放宽按 `TypeKind.Submission` 分叉，两种 kind 在元数据面上本已可区分）。
* **OPEN**：宿主对象的并发/重入语义（提案 Unresolved 1/6）——类型级一份之后的长会话、异步提交、同进程多脚本可见性边界；以及 `spec:361` 会话隔离在该格上的重验。本轮把「出货面上看不到」判定为**一份对宿主行为的假设清单**，不是结论。
* **TODO**：issue 04 的诊断形状（错误码归属、是否复用共享上下文引用实例成员的既有码 BC30469 = `ERR_ObjectReferenceNotSupplied`、消息是否点名「顶层扩展方法需要 `Shared`」）——升为独立立项后的第一件事。
* **TODO**：把本轮的反射实测（`Submission#0` 字段可见性）与其机制锚点（`Symbol.vb:97-102`/`Symbol.cs:256-265`）补进提案 §5/§8 的口径更正记录，plan 阶段以纪要为准。
* **TODO**：`ScriptTests.vb:136-149` 的「重跑回初值」三条断言在甲 落地后应逐字节不变（甲 只动共享路径）——请列为一条回归验证用例；这正是 `meeting-submission-shared-members` OPEN 里要求的同一条。
* **Follow-up**：若日后复活本方向，「提案形态的端到端输出」必须实跑（顶层 `Dim x = 5` 的初始化器落在 `.cctor` 还是 `<Initialize>` 的 IL 证据 + 声明前读它的输出），把本纪要与提案的「待定」一并升为实锤或推翻。
* **Suspect**：经典 VBScript 是否有类成员层（`Class … End Class` / `Me` / `Class_Initialize`）——本仓无锚点，不作为论据；本轮同样不把它当实锤用。

## 会后实测补证（会后追加，2026-09-13）

> 本节由作者指示追加，只**补充证据**：不改动上方任何 RESOLUTION、判定与叙事。测量工具与上文同：Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（自报 `2.0.0-Beta+5816a5c`）；探针写在系统临时目录、用后已删净，未写入仓库跟踪目录。csi 一行由作者在本机实跑。

| # | 形态 | 实测 | 读者 |
|---|---|---|---|
| 1 | 分支体内的 `Dim`（`If True Then : Dim x = 5 : End If`），在块外读 `x` | `error BC30451: 'x' 未声明`，`EXIT=1` | **分支/循环体内部声明的 `Dim` 是局部**，不参与字段分桶 ⇒ 不受本方向影响 |
| 2 | 顶层 `Console.WriteLine("BEFORE=" & x)` **写在** `Dim x As Integer = 5` **之前** | `BEFORE=0` / `AFTER=5`，`EXIT=0` | `spec:64` 的源码序在今天成立 |
| 3 | 守卫 `If True Then : Console.WriteLine("GUARD HIT") : Return : End If` 之后接顶层 `Dim x = Probe()`（`Probe` 打印 `INIT RAN`） | 只输出 `GUARD HIT`；**`INIT RAN` 未输出**，`EXIT=0` | **今天守卫拦得住后面的顶层初始化器**——这正是源码序承诺在「提前退出」上的兑现 |
| 4 | 同形状但把声明写成 `Shared Dim x = Probe()` | `System.TypeLoadException: Could not load type 'Submission#0'`，`EXIT=34`；栈逐字到 `Scripting\Core\ScriptBuilder.cs:194` | **提案形态今天无法测量**——不是「没实现默认共享」，而是连手写 `Shared` 都不通（issue 05）。这是 RESOLUTION 1(c)「被 05/06 硬阻塞」的现场证据 |
| 5 | csi：`Console.WriteLine(a);static int a = 5;` | `5` | **csx 的 `static` 顶层字段初始化器先于一切顶层语句**（作者实跑，csi `5.10.0-1.26380.3`） |

**我们从中读出的四件事**：

其一，**测量 3 与测量 5 合起来，把 RESOLUTION 1(b) 的理由从「读到的值不同」推到了「该不该执行」这一层**。测量 3 说明今天的源码序承诺在守卫上是有兑现的：`Return` 一走出去，后面那行 `Dim` 的初始化器**根本没执行**。测量 5 说明同一个形状一旦落进 `.cctor`，初始化器**先于守卫执行**。两者机制同源已在上文逐点核过（`SourceMemberContainerSymbol.cs:6093` ↔ `SourceMemberContainerTypeSymbol.vb:2633`；静态桶在两边都是 `MethodCompiler` 里静态构造器的唯一分支）。⇒ 本方向若落地，**守卫将不再能保护写在它下方的有副作用的初始化**：用法提示、dry-run 这类「先拦后做」的写法，会变成「拦之前已经做了」。这一点是**推测**——由测量 1/2/3/5 与机制同源推出，**没有在提案形态上跑过**（测量 4 说明了为什么跑不了）。

其二，**源序承诺的失守面在结构里比在直线代码里宽**。直线代码里「先读后声明」一眼可见、少有人写；而 `If` / `Select` / 循环里，声明落在结构之后、读取落在结构之内，是自然的写法，且**结构可以提前退出、循环可以根本不进或多次进**——声明在何种情况下算「已经发生」，不再由阅读顺序给出。

其三，**测量 1 把这条论据的范围收窄了一格**：写在分支/循环体**内部**的 `Dim` 是局部，不参与字段分桶，因此本方向不触碰它们。受影响的确切形状只有一种：**顶层字段被结构内部的语句读到，而声明的文本位置在结构之后**。

其四，**测量 4 是一条独立于方向取舍的现场记录**：本方向今天的不可测，不是缺默认共享这一层机制，而是缺「甲」——顶层共享字段连写都写不出来。这把 RESOLUTION 1(c) 的排序（先甲+乙、再谈方向）从论证变成了实测。

## 状态

* **LDM 状态：Table**。方向「脚本/提交类顶层成员默认共享」维持 `Table`（第二次到会，且本次带完整提案）；提案作为方向的完整论证**不通过**——被推翻的是它的**核心论证与方向定价**，不是它的**事实基线**。三条必须项 + 两条应当项见 RESOLUTION 2/3；复活条件见 RESOLUTION 1/2/5。
* **三态判定**：
  * `proposal-top-level-implicit-shared` = **Table**（方向维持 Table；提案需按 RESOLUTION 2 的三条必须项返工后方可重新上会）
  * **推荐替代路线**（RESOLUTION 4）：维持实例基线（**Active**，现状）+ issue 04 补诊断（**Consider**，升独立立项）+ 甲/乙（**Active**，`meeting-submission-shared-members` 已裁定）+ ①a 维持 **Table**（若 A+诊断 路线不被接受，它是次优选择）
  * **正面资产**（RESOLUTION 8）：§7 ref struct 空结果 / §9 两模式分野 / §8 只读无硬墙与自纠 / §12 测试策略 = **Active**（可独立用于 plan）
* **证据等级**：承重锚点**已检查**（逐行复核，无一处承重行或承重判据不成立）；规范不变式冲突、§6 方向反转、`spec:64` 失守、`spec:66` 反转、宿主对象四处改动面、①a 信息可得性 = **已检查（源码逐行）**；宿主 API 现状（提交类字段元数据可见性、反射枚举）= **已运行**（本轮探针，`EXIT=0`，探针已删净、未入库）；**提案形态的端到端行为 = 未运行（待定）**（本阶段未改编译器）；Release 行为 = 未跑（全部只跑 Debug `5816a5c`）；凡引 C# 侧测试均止于「读到断言文本」（本树无 C# 运行环境）。**会后追加的实测补证见上节**：源码序基线（`BEFORE=0`/`AFTER=5`）、守卫拦截顶层初始化器、分支体内 `Dim` 是局部、提案形态因 issue 05 不可测 = **已运行**；csx `static` 顶层字段 = **作者本机实跑**。

独立五维评审为不入库工作材料（git-ignored），不随本纪要入库。
