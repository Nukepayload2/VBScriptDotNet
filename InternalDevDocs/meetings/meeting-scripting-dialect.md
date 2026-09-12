# Visual Basic Language Design Meeting
September 9, 2026

议题是 `proposal-scripting-dialect`——把 `.vbx` 与交互窗口的**声明与提交模型**写成 spec 化的唯一事实基线：`SourceCodeKind.Script` 下顶层 `Dim` 是 script class 的**字段**、顶层 `Sub`/`Function` 是它的**实例成员**、顶层 `Class` 是它的**嵌套类型**、顶层可执行语句是它的**实例初始化器**（在 `<Initialize>` 方法体里按源码顺序展开）；script class 由编译器合成，提交模式下名字被宿主替换为 `Submission#N`，每条提交是一个独立 compilation 并沿 `PreviousScriptCompilation` 链串联。这是一份记录型提案（`Prototype: Complete`、`Implementation: Complete`），记录的是随 1.2 发布、在 2.0 修复完整的既有方言，不是待决的新语法——所以这场会议从一开始就不是「要不要做」，而是「这份事实基线准不准、全不全」。

我们两条独立路径各读了一遍——一条沿 VB 基因问「这像不像 VB、有没有开第二种做事方式」，一条沿 C# 生态问「它与 csx、C# 顶层语句、共享 `Script<T>` 合不合」。定性很快收敛：**模型本身站得住**。它没有发明语法，只是把 VB 既有的声明形式放进一个编译器合成的容器里；而「宿主可以合成入口点」是规范明文允许的（`source-files-and-namespaces.md:53`：「The compilation environment may also create an entry point method if one does not exist.」）。顶层 `Await` 也不是语法放宽——`<Initialize>` 本身就是 `Async Function`（`SynthesizedInteractiveInitializerMethod.vb:51-55`），顶层语句在它体内，`Await` 只是普通规则在普通 async 上下文里的正常用法。这一点提案定性正确，我们逐行核过。

但把 §1–§8 的锚点翻完之后，我们发现这份「唯一事实基线」在三处把方言差异面说小了、在一处把签名写错了。前者才是真正的会议内容：显式 `Me` 系禁令的实际范围、`Imports` 跨提交累积对规范作用域规则的偏离、脚本编译选项默认值承载的晚绑定基因——三块承重墙提案未写或一语带过。签名那处则是硬错误，两条独立路径各自都撞到了它。此外还有一个更结构性的问题：提案在四个承重点上交替用 **C# 顶层语句**与 **C# 脚本方言（csx / `Script<T>`）**做基准，而这两者对本案的关键问题给出的答案正好相反。本纪要按「先复核、再权衡、后裁决」记下这段推理旅程。

## Agenda

* [Proposal: 脚本方言的声明与提交模型](#proposal-脚本方言的声明与提交模型)

## Proposal: 脚本方言的声明与提交模型

_Related: [`../proposals/proposal-scripting-dialect.md`](../proposals/proposal-scripting-dialect.md)；前置盘点 `../meetings/meeting-byref-like-repl-safety.md`（顶层 `Dim` = 字段、末尾表达式装箱、`<Initialize>` 恒 async）；方言边界出处 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`；`../decisions.md` M2（宽松/晚绑定基因）/ M5（脚本运行时）/ D2（AOT 桥）/ D4；产品 spec `../spec/README.md`（1.2 损坏与 2.0 修复记录）；共享层台账 `../upstream-merge.md` 2.2 / 2.6_

> **来源标注**：正文引用的源码与规范文本均逐字核对（`文件:行号`）。提案 §1–§8 的锚点逐一复核，绝大多数属实；复核中发现并修正一处事实失实（§4 `<Factory>` 签名）与若干处表述遗漏（见正文）。提案是冻结输入，本纪要对它的修正只写进 RESOLUTION。

### 场景与缺口

脚本与交互要求免包装：传统 VB 要求代码位于 `Module`/`Class` 内，而 REPL 与 `.vbx` 的体验是「敲下声明即可用、下一条提交还能用」。C# 顶层语句的三场景讨论把「简单程序」「顶层函数」「脚本/交互」并列，并写明「Submission system allows state preservation across evaluations」（`csharplang\meetings\2020\LDM-2020-01-22.md:21`），最终优先落地 (1) 与 (3)（同文件 `:36`）。`.vbx` 与 `vbi` REPL 就是 VB 侧的第 (3) 场景。

缺口陈述与提案一致：免包装随 1.2 已发布（微软商店），但顶层 `Await`、顶层 `AddHandler`/`RemoveHandler`、`Imports` 交互模式在 1.2 为损坏状态（`spec\README.md:15`），三者均在 2.0 修复在册（`spec\README.md:23-27`）。本提案记录的是修复后的完整方言，另含一处相对上游的收窄（typed 提交的末尾表达式不作结果，`spec\README.md:27`）。这份「修复后完整方言」的定位我们认同——它不是发明，而是把已落地的机制说清楚，为 spec 化提供唯一基线。

### 翻源码：§1–§8 锚点复核

**§1–§3 属实，且落点都在既有机制上。** 声明表构建期由 `CreateScriptClass` 合成 script class（`Declarations\DeclarationTreeBuilder.vb:131-162`）：kind 由 `_isSubmission` 决定（`:140`），修饰符固定 `Friend Or Partial Or NotInheritable`（`:143`），`_scriptClassName.Split("."c)` 逐级包 namespace（`:136`、`:151-159`），`declFlags = None`（`:192`，注释逐字「Script class is not static and contains no extensions.」）。非 Regular 树走专门分支（`:175-201`），`Namespace` 进 `childrenBuilder` 仅为改善错误报告（`:182-188`），其余进 `scriptChildren`（`:187`）。顶层 `Dim` → 字段、顶层方法 → 实例成员、顶层可执行语句 → 实例初始化器（`SourceMemberContainerTypeSymbol.vb:2549-2557`、`:2559-2593`、`:2625-2637`），`BindingTopLevelScriptCode` 的判据是「成员是脚本构造函数/初始化器，或类型是 script class」（`Binder.vb:428-443`）。提交链、跨提交可见性、状态保持的运行时机制（`SynthesizedSubmissionFields` + `BoundPreviousSubmissionReference`）也逐条属实。

在 `DeclarationKind` 与 `TypeKind` 的对应上我们核清了一件事：`DeclarationKind.Script` 落 `TypeKind.Class`、`DeclarationKind.Submission` 落 `TypeKind.Submission`（`SourceMemberContainerTypeSymbol.vb:156-160`）。这不是记账式的细节——它解释了为什么跨提交可见性、`<Factory>` 与 `<Main>` 的选择、`WithEvents` 挂钩三者都按 `TypeKind` 分叉：非提交脚本类是普通 Class，走普通 Class 路径；只有提交类才是那条特殊路径。关于提交链本身，我们把它的一句话语义定下来：链是**会话级存活**（进程退出即弃），**无上限是有意的**——任何上限都会让「会话状态保持」变得不可预期；`SynthesizedSubmissionFields` 每提交加一个字段的开销属实现细节，spec 不必承诺。

**§4 有一处事实失实：`<Factory>` 的签名被写成了 `As T`。** 提案原文是 `Private Shared Function <Factory>(submissionArray As Object()) As T`，而实际返回类型是**初始化器的返回类型**，即 `Task(Of T)`。三处互证：

- `Symbols\Source\SynthesizedEntryPointSymbol.vb:27`：`Return New SubmissionEntryPoint(containingType, initializerMethod.ReturnType, submissionArrayType)`——工厂的返回类型直接取初始化器的 `ReturnType`；
- 同文件 `:375-382` 的工厂体是 `Return submission.<Initialize>()`，返回的正是初始化器的 `Task(Of T)`；
- `Scripting\Core\ScriptBuilder.cs:163` 把入口点变成 `Func(Of Object(), Task(Of T))` 的委托——若工厂真返回 `T`，这里根本对不上。

`As T` 只出现在同文件 `:336` 的**源码注释**里（`' Private Shared Function <Factory>(submissionArray As Object()) As T`）——它是上游注释的简化写法，被当成签名抄进了提案。这处错误恰好落在提案最想钉死的那句「固定 `Task(Of T)` 初始化器」上，属记录型提案的事实准确性缺陷。我们把 `IsScriptClass` 的引用也一并订正：它在基类 `NamedTypeSymbol.vb:682-686` 恒为 `False`，真正的覆盖点是 `SourceMemberContainerTypeSymbol.vb:1295-1300`（`kind = DeclarationKind.Script OrElse kind = DeclarationKind.Submission`）。

**§5–§6 属实，但两处语义后果提案没写。** 顶层 `AddHandler`/`RemoveHandler` 按语句解析（`Parser\Parser.vb:828-838`），`WithEvents` 字段的事件挂钩在提交类上是显式 `TODO`（`SourceMemberContainerTypeSymbol.vb:2804-2807`）。`Imports` 跨提交累积确为宿主侧机制（`VisualBasicScriptCompiler.vb:113-120`、`:134-162`），去重键是 `clause.ToString()` + `OrdinalIgnoreCase`（`:115`），经 `GlobalImport.Parse` 转 `GlobalImport` 列表（`:119`）。这两处的语义后果分别见下文「权衡二」与「权衡三」。

**§7 的诊断族复核属实，但清单漏了两条。** `ERR_NamespaceNotAllowedInScript`(36965) 解析期报（`Parser.vb:1677-1679`、`Errors.vb:1598`）、`ERR_KeywordNotAllowedInScript`(36966) 覆盖显式 `Me` 系/`Return`/`Yield`（`Binder_Expressions.vb:2263-2269`、`Binder_Statements.vb:5090-5093`、`:5229-5230`、`Errors.vb:1599`）、`ERR_UnexpectedExpressionStatement`(31003) 非末尾裸表达式、`ERR_PropertyAccessIgnored`(30545) 仅提交末尾抑制——调用点全部核实。我们另找到两条应在清单里的：

- **脚本里写顶层 `Sub Main()` 会被忽略并报警告 BC42367**，不执行：`VisualBasicCompilation.vb:1667-1672` 注释逐字「Global code is the entry point, ignore all other Mains.」，消息见 `VBResources.resx:4370-4371`「The entry point of the program is global script code; ignoring '{0}' entry point.」。这是 VB 对「顶层代码存在时自写 `Main` 被忽略」的既有、**非静默**回答，恰是 VBScript 迁移者最容易踩的一条。
- **顶层标签 / `GoTo` 没有归属**：`SourceMemberContainerTypeSymbol.vb:2613-2615` 的 `Case SyntaxKind.LabelStatement` 只有一句 `' TODO (tomat): should be added to the initializers`——标签**不进初始化器**。这是 U6「完整清单」必须回答的一条。

**§8 的两处门属实，但「提交结果」在实现里其实是两个判据。** 收窄的两处门确实都在：绑定期 `Binder_Initializers.vb:211-224`（判据 `:219` `submissionReturnType.IsObjectType()`）、重写期 `InitializerRewriter.vb:205-223`（判据 `:208-209`），两处都带着说明这是有意收窄的注释（`:217-218`、`:206-207`）。但 `VisualBasicCompilation.HasSubmissionResult`（`:839-900`）**只看最后一棵树的末语句形状**（`PrintStatement` / `ExpressionStatement` / `CallStatement` / `ReturnStatement`），**不看提交返回类型**。对 typed 提交，末语句写 `1 + 2` 时前者为 `True`、Object 判据不给结果——两者可给出相反答案。当前不构成活 bug（REPL 用 Object 提交，二者一致；`.vbx` 不查前者），但 spec 若把二者都叫「提交结果」，读者会以为它们是同一件事。

顺带记一处实现细节：C# 的重写期判据用 `method.DeclaringCompilation.IsSubmissionSyntaxTree(initializer.SyntaxTree)` 排除 `#load` 进来的树（`Compilers\CSharp\Portable\Lowering\InitializerRewriter.cs:44-47`），而 VB fork 用的是「是最后一个初始化器且是全局语句初始化器」（`InitializerRewriter.vb:210-211`）——VB 侧全树 grep 无 `IsSubmissionSyntaxTree`。因加载树先于主树入列（`VisualBasicScriptCompiler.vb:187-199`），两者效果一致；记下来是因为 spec 抄判据时不能照 C# 的措辞写。

### 权衡一：显式 `Me` 系禁令的范围——本模型唯一的「第二种做事方式」

提案 §7 把这条写成「脚本类内**显式**使用 `Me` / `MyBase` / `MyClass`（隐式引用放行）」。逐行读源码，判据是**containing type 是不是 script class**，与「是否顶层语句」无关：

```
' Any executable statement in a script class can access Me/MyClass/MyBase implicitly but not explicitly.
' No code in a script class is shared.
Dim containingType = Me.ContainingType
If containingType IsNot Nothing AndAlso containingType.IsScriptClass Then
    If implicitReference Then Return True
    Else errorId = ERRID.ERR_KeywordNotAllowedInScript : Return False
```
—— `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:2260-2270`

因此下面这段**写在顶层 `Function` 体内**的代码会报 BC36966：

```vbnet
Dim counter As Integer = 0
Function NextCounter() As Integer
    Dim counter As Integer = 5      ' 局部遮蔽字段
    Return counter                  ' 只能拿到 5；写 Me.counter 报 BC36966
End Function
```

这与 §2 自述的「顶层 `Sub`/`Function` 是同一类型的实例成员」在读者心智里直接冲突：VB 实例方法里 `Me` 是唯一能穿过局部遮蔽拿到字段的写法，而这里被剥夺了——**同一段方法体，写在脚本顶层与写在 `Class` 里行为不同**。这正是原版 LDM 说的「a second way to do things」（`vblang\meetings\2018\vbldm-notes-2018.06.13.md:34`：「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」）。我们列了两个方向：

- **收窄**：把判据从「containing type 是 script class」改为「containing member 是脚本初始化器 / 脚本构造函数」，即与 `BindingTopLevelScriptCode` 的方法分支对齐（`Binder.vb:432-435`）。顶层 `Sub`/`Function` 的方法体于是回归普通实例方法语义，`Me.x` 的遮蔽逃生口恢复；顶层语句仍禁显式 `Me`（那里 `Me` 指向脚本作者不该关心的合成实例，禁得有理）。
- **写透**：保留现行为，但 spec 必须明说这条限制覆盖顶层 `Sub`/`Function` 的整个方法体，并给理由与替代写法。

我们倾向收窄——它把「实例成员」这个词还给顶层方法，也让方言差异面回到提案自称的「声明落点 + 入口点合成」两件结构性事实上。一条支撑事实：本 fork 的编译器测试树已裁剪（`Compilers\VisualBasic\` 下的 `*Test` 目录为空），树内没有任何断言该禁令的测试，收窄不会与既有断言冲突（反过来说也没有回归网兜底，需补新测试）。**无论取哪条，现在这版提案的措辞都不够**：读者读到「脚本类内显式使用 `Me`」不会想到它落在自己写的每个顶层函数体里。

### 权衡二：`Imports` 跨提交累积是对规范作用域规则的显式偏离

提案 §6 把「`Imports` 跨提交累积」如实定性为宿主侧机制，这点诚实。但它的语义后果是**规范级偏离**，提案没点出来：

> `vblang\spec\source-files-and-namespaces.md:253`
> 「`Imports` statements make names available in a source file, but do not declare anything in the global namespace's declaration space. The scope of the names imported by an `Imports` statement extends over the namespace member declarations contained in the source file. The scope of an `Imports` statement specifically does not include other `Imports` statements, nor does it include other source files.」

每条提交是一棵语法树（一个「source file」），累积让提交 1 的 `Imports` 作用于提交 2——正面违反上述作用域规则。对 REPL 而言这是**必要**的（否则「敲下 `Imports System.Text`，下一条还能用」就没了，正是 `LDM-2020-01-22.md:21` 的 state preservation）。所以立场是**接受这个偏离，但必须写成偏离**：spec 要写「脚本/交互方言中，`Imports` 的作用域跨提交累积；这是对 `source-files-and-namespaces.md:253` 的方言级偏离，理由是提交链的状态保持」。

规范化规则还要补三类（U4 只提到别名与 `Global` 前缀）：**同别名不同目标**（提交 1 写 `Imports R = System.Text`、提交 2 写 `Imports R = System.IO`，两串文本不同 → 两条都进 `GlobalImport.Parse` → 后一条提交出现重名别名，期望行为未定义）；**XML namespace 前缀重定义**（`clause.ToString()` 也会收集 XML 子句，而规范说「An XML namespace, including the default namespace, can only be defined once for a particular set of imports.」`source-files-and-namespaces.md:552`——普通 VB 多文件场景按文件各自定义不会撞，累积后会撞）；**`Global` 前缀**在累积后是否仍按全局命名空间解析。另有一处健壮性隐患：`GlobalImport.Parse(IEnumerable(Of String))` 遇到无法解析的文本会 **throw `ArgumentException`**（`GlobalImport.vb:77-86`），而输入来自前序提交的 `clause.ToString()`——前序提交若含语法错误的 `Imports` 子句，宿主会抛异常而非报诊断，spec 应要求「累积失败降级为诊断」。

### 权衡三：晚绑定基因的载体是编译选项，不是顶层免包装

这是提案一字未提、我们却认为最该补的一块。「VBScript 基因」不是靠顶层免包装体现的，而是靠脚本编译选项的默认值：

```vbnet
New VisualBasicCompilationOptions(
    outputKind:=OutputKind.DynamicallyLinkedLibrary,
    ...
    optionStrict:=OptionStrict.Off,
    optionInfer:=True,
    optionExplicit:=True,
    ...)
```
—— `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:212-220`

`Option Strict Off` 就是晚绑定的开关（`source-files-and-namespaces.md:141-147` 列出 strict 语义下被禁的东西：窄化转换、晚绑定、`Object` 上的运算、省略 `As` 且无法推断）。脚本默认关掉 strict，正是 VBScript「宽松」基因的落点；`Option Infer On` 让 `Dim sb = New StringBuilder("abc")` 有类型；`Option Explicit On` 又守住「先声明再用」的底线。这三项合起来才是这个方言的性格，也解释了 §9 的示例为什么能在顶层直接 `Dim value = Await Task.FromResult(13)`。spec 应增加「Compilation options」一节，把三个默认值与理由写清——这是与「普通 VB 项目」差异面最大、也最需要向读者交代的一处。

### 权衡四：末尾表达式收窄——拿哪一个 C# 当基准

§8 的收窄本身我们认同：对齐 `Function Main` 的 `Return` 语义，也与 C# 顶层语句拒绝「末尾表达式产生结果」进入语言本体的立场同向（`LDM-2020-04-15.md:42`：「we would prefer not to have a notion of "expression at the end produces result" in C#」）。但提案的**理由**引错了对象：C# 那边还有一套共享脚本引擎，它的行为与收窄相反，而且是无条件的。

- C# 侧：`Compilers\CSharp\Portable\Binder\Binder_Initializers.cs:254-264` 在 `binder.Compilation.IsSubmission` 时把末尾表达式转成 `scriptInitializer.ResultType`，**没有** `IsObjectType()` 这类门；`Compilers\CSharp\Portable\Lowering\InitializerRewriter.cs:34-35` 的判据只是 `hasSubmissionResultType = (object)submissionResultType != null`，同样没有对象类型门。
- VB fork 侧：两处门都在（`Binder_Initializers.vb:219`、`InitializerRewriter.vb:208-209`）。

也就是说，同一个共享公共 API `Script<T>.ContinueWith<TResult>`（`Scripting\Core\Script.cs:114`）下，C# 末尾写 `42` 就是结果 42，VB fork 必须写 `Return 42`、末尾写 `42` 得 0——同一 API、同一段共享 host 代码，语义按语言分叉。**这是设计取舍，不是缺陷**，但表述必须改：不是「与 C# 同向」，而是「**与 C# 顶层语句同向、与 C# 脚本方言（共享 `Script<T>` API）有意分叉**」。这条分叉落在共享编译器路径（上游 VB 是照 C# 平行实现写的），必须进 `upstream-merge.md` 台账（现 2.6 只记了「`Return` 按 `Function Main` 语义处理」）与公共 API 文档，否则合并时容易被上游静默改回。

更深一层，提案在四个承重点上交替引用 C# 顶层语句与 C# 脚本方言，而两者对本案关键问题答案相反：顶层变量（局部 vs 字段）、末尾表达式（不作结果 vs 转成提交返回类型）、入口签名（按 `await`/`return` 切换 vs 固定 `Task(Of T)`）、顶层 byref-like 变量（合法 vs 非法）。C# 设计组当年正是在同一张桌子上区分这两个面（`csharplang\meetings\2020\LDM-2020-02-26.md:46-48`：「Many of these features mirror what we already have in CSX... since the semantics of this design have subtle differences from CSX this would effectively create a third dialect of C#.」）。一份要当唯一事实基线的记录型提案更应该说清自己站在哪一边。**我们要求在 Alternatives 的对照表上方加一段「基准声明」**：VB 脚本的**声明/状态模型**对标 csx，**文件执行的退出码语义**对标 C# 顶层语句，**REPL 的打印语义**对标 csi；每处 C# 引用旁标注是哪一面。

### 诊断族与 C# 对应物 / AOT 边界 / tooling 后果

C# 的脚本专属错误码只有四枚（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:1157,1166-1167,1332`）：`ERR_ReferenceDirectiveOnlyAllowedInScripts`(CS7011)、`ERR_YieldNotAllowedInScript`(CS7020)、`ERR_NamespaceNotAllowedInScript`(CS7021)、`ERR_LoadDirectiveOnlyAllowedInScripts`(CS8097)。对照下来：BC36965 与 CS7021 **同名同义**（命名惯例对齐）；BC36966 覆盖 `Me`/`MyBase`/`MyClass`/`Return`/`Yield`，C# **没有**对应物（csx 里 `this` 就是提交实例、顶层 `return` 就是提交结果），是 VB 独有的方言面；BC31003/BC30545 是通用诊断复用（与 C# 复用 CS0201 同构）。我们另外注意到 C# 侧有 `WRN_MainIgnored = 7022`（`ErrorCode.cs:1168`）——与 VB 的 BC42367 同名同义，所以补进清单的那条警告在 C# 有直接对应物。spec 给完整禁止清单时应逐条标注 C# 对应物或「无对应」。另记一笔规则细节：脚本初始化器内的裸 `Return` 补默认值并不是「值类型取默认、否则 `Nothing`」这么简单——`System.String` 与 `Bad`/`Nothing` 判别值一起走 `Nothing` 分支（`InitializerRewriter.vb:230-237`、`Binder_Statements.vb:5164-5172`），spec 抄这条规则时要带上 `String` 这个特例。这两枚新错误码（BC36965/BC36966）与 typed 提交收窄一样落在共享编译器树，目前都还没进 `upstream-merge.md` 账本——新增 VB 错误码不入账，将来与上游 VB 撞号时无据可查，属维护卫生但必须补。

AOT 边界提案通篇没提，但它是 C#/.NET 生态对「脚本」最现实的一条约束：本方言属脚本运行时（内存 emit、动态程序集加载、`Object()` 状态数组、`Submission#N` 合成类型、反射式构造），天然落在 AOT/trimming 之外；`decisions.md` D2 的 AOT 路线只覆盖普通 VB 编译产物。spec 应写一句「`.vbx` 提交不参与 AOT/trimming」，否则读者会以为它可进 AOT 产物。

Tooling 后果也值得记一笔：累积后的 `Imports` 只出现在**编译选项**（`Compilation.Options.GlobalImports`）里，**不出现在提交的语法树**（`root.Imports` 只有本提交自己的）。任何基于语法树判断「当前会话有哪些 Imports」的编辑器/工具都会漏掉累积项，必须读 `Compilation.Options.GlobalImports`。

### VB 基因对照 / C# 生态对照

- **script class 是既有声明语义的容器化，不是第二套声明方式。** 四条落点映射逐条核过，没有一条引入新语法：`Dim`/`Sub`/`Function`/`Class` 的拼写与语义不变，变的只是「写在哪个容器里」；容器是编译器合成物，源码看不见名字。更硬的一层是规范依据——`Dim` 本来就是类型级变量成员的合法修饰符（`type-members.md:1780-1787` 的 `VariableModifier` 含 `'Dim'`），而「局部与成员声明方式相同」也是规范明文（`statements.md:329`：「Local variables and local constants are equivalent to instance variables and constants scoped to the method and are declared in the same way.」）。spec 引上这两条，「顶层 `Dim` 是字段」就从方言怪癖变成同一修饰符在合成容器里的自然结果。
- **`Await`/`Return` 的普通规则定性正确。** 顶层 `Await` 位于 `IsAsync = True` 的 `<Initialize>` 内；顶层 `Return` 也合法，因为规范说「A `Return` statement with an expression is only allowed in a regular method that is a function, or in an async method that is a function with return type `Task(Of T)`」（`statements.md:1814`），而 `<Initialize>` 正是后者。**命名建议**：spec 不要把这套规则叫「`Function Main` 语义」——脚本里没有 `Function Main`，只有合成的 `<Initialize>`；对脚本作者应说「顶层显式 `Return value` 决定进程退出码」，用「脚本结果（script result）」这个中立词。
- **Motivation 缺 vblang 自家立场。** 提案引的全是 csharplang，但 VB 侧自己有记录，且立场更贴近本题：`vblang\meetings\2017\vbldm-notes-2017.12.06.md:80-85` 记 #102「Support Top-Level Statements in a Single Entry-Point File」——原版团队关心的正是「脚本方言与标准方言语义不同会让文档/教程教两个语言」，方向是**调和（reconcile）**：「Let's try to reconcile the standard and scripting dialects in the new year.」，**Decisions: Deferred to January 2018**。这不是说提案错了（产品有 REPL，方言客观存在），而是说 Motivation/Alternatives 应正面回答同语言自家团队的调和诉求——只引 C# 的「第三种方言」担忧不够。
- **对照表要改对称。** 提案把 VB 的「顶层 `Sub`/`Function` 是 script class 的成员、同提交内可直接调用」当作对 C#「局部函数，类内不可访问」的优势项。这个对照不对称：VB 侧「可直接调用」只对**顶层代码**成立；嵌套类型的方法里没有指向当前 script class 实例的引用（`Me` 指向嵌套类型自己的实例，显式写 `Me` 又被禁），提交模式下类型名 `Submission#N` 含 `#`（VB 类型字符）不可书写，非提交下 `New Script()` 只得到另一个全新实例。**推论（推测 = 待定，基于已检查事实）**：顶层 `Class` 的方法里同样无法访问当前脚本实例的顶层 `Function`/`Dim`——C# 那条「类内不可访问」的对照 VB 也成立，只是原因不同。spec 应明写「顶层声明的作用域 = 顶层代码 + 后续提交，不含嵌套类型内部」。
- **C# 生态面：合成名逐字一致是加分项。** `<Initialize>`、`<Main>`/`<Factory>`、`Submission#N` 在本 fork 里与 C# **逐字相同**（C# 锚点 `Symbols\Synthesized\SynthesizedInteractiveInitializerMethod.cs:19`、`Symbols\Synthesized\SynthesizedEntryPointSymbol.cs:22-23`；`Submission#N` 与默认类名 `Script` 走共享 `Scripting\Core\ScriptBuilder.cs:73` 与 `Compilers\Core\Portable\Symbols\WellKnownMemberNames.cs:67`）。识别这些名字的工具（反编译器、调试器符号化、脚本宿主）跨语言通用。跨提交可见性、提交链、宿主侧 `Imports` 重放也与 C# 脚本引擎同源同形，不引入新元数据、新类型形状或新反射约定。**这是本案对 C# 生态最友好的一处。**
- **方言可搬运性的一点成本。** csx 里 `this.X` 是访问前序提交状态的正常写法，而 BC36966 禁掉脚本类里的显式 `Me` 系（与「权衡一」同源）。这不是 C# interop 缺陷（C# 侧不受影响），只是 csx → vbx 搬运时要改写，与「第三种方言」担忧同源，spec 在方言边界段提一句即可。

### RESOLUTION:

1. **定性**：本模型是 Roslyn scripting 既有机制的复用与 fork 完成，不是新语法；提案作为「已实现方言的事实基线」成立。三处修复（顶层 `Await`、顶层 `AddHandler`/`RemoveHandler`、`Imports` 跨提交累积）与一处收窄（typed 提交末尾表达式）均已随 2.0 在册 → 证据等级**已采纳**。定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`。映射 `decisions.md` **M2**（宽松/晚绑定基因）与 **M5**（脚本运行时）；D4 不直接命中 P1 两档——既不是「C# 已照顾到的、非底层内存机制相关用例」，也不是「C# interop 用例」（如 consume ref struct）；优先级由实现规划结合路线图定。
2. **`<Factory>` 签名订正（本 RESOLUTION 为该表述的 supersede 口径）**：`As T` → **`As Task(Of T)`**。锚点：`SynthesizedEntryPointSymbol.vb:27`（返回类型 = `initializerMethod.ReturnType`）、`:375-382`（体为 `Return submission.<Initialize>()`）、`Scripting\Core\ScriptBuilder.cs:163`（`Func(Of Object(), Task(Of T))`）。`As T` 是上游源码注释 `:336` 的误写被当签名抄录。§4 应补一句「工厂体与返回类型一致」。同时把 `IsScriptClass` 的引用从基类（`NamedTypeSymbol.vb:682-686` 恒为 `False`）改到实际覆盖点（`SourceMemberContainerTypeSymbol.vb:1295-1300`）。
3. **显式 `Me`/`MyBase`/`MyClass` 禁令的范围（本 RESOLUTION 为该表述的 supersede 口径）**：判据是「containing type 是 script class」（`Binder_Expressions.vb:2260-2270`），与是否顶层语句无关——顶层 `Sub`/`Function` 的方法体同样被禁，这与 §2「顶层方法是实例成员」冲突并拿掉了 `Me.x` 的遮蔽逃生口。**裁决：判据收窄到脚本初始化器与脚本构造函数**（与 `Binder.vb:432-435` 的方法分支对齐），顶层成员体回归普通实例方法语义；顶层语句仍禁显式 `Me`（那里 `Me` 指向脚本作者不该关心的合成实例）。落地为 2.0 之后的实施项（编译器测试树已裁剪、树内无断言该禁令的测试，需补新测试）；在收窄落地前，spec 若描述现状必须写明「禁令覆盖顶层成员体的整个方法体」。
4. **顶层 `Dim` 的规范依据写进 spec**：引 `type-members.md:1780-1787`（`VariableModifier` 含 `'Dim'`）与 `statements.md:329`（局部与成员「declared in the same way」），使「顶层 `Dim` 是字段」呈现为同一修饰符在合成容器里的自然结果，而不是方言怪癖。
5. **`Imports` 跨提交累积写成对 `source-files-and-namespaces.md:253` 的显式方言偏离**，理由 = 提交链的状态保持。规范化补齐三类：**同别名不同目标**、**XML namespace 前缀重定义**（`source-files-and-namespaces.md:552`）、**`Global` 前缀**；并把「累积失败降级为诊断」写进 spec——`GlobalImport.Parse` 在 `:77-86` 遇不可解析文本会 `throw ArgumentException`，输入来自前序提交 `clause.ToString()`，含语法错误子句会抛而非报诊断。U4 据此收口。
6. **补「脚本编译选项默认」一节**：`Option Strict Off` + `Option Infer On` + `Option Explicit On`（`VisualBasicScriptCompiler.vb:218-220`）——VBScript 宽松/晚绑定基因的实际载体，也是与普通 VB 项目差异面最大的一处。
7. **§8 收窄的基准与理由改写（本 RESOLUTION 为该表述的 supersede 口径）**：收窄本身成立（对齐 `Function Main` 的 `Return` 语义与 C# 顶层语句的 `return n`），但「与 C# 判断同向」必须改写为「**与 C# 顶层语句同向、与 C# 脚本方言（共享 `Script<T>` API）有意分叉**」，证据是 C# 侧的无条件转换（`Binder_Initializers.cs:254-264`、`Lowering\InitializerRewriter.cs:34-35`）与 VB fork 的两处门（`Binder_Initializers.vb:219`、`InitializerRewriter.vb:208-209`）。该分叉落在共享编译器路径，**记入 `upstream-merge.md` 2.6**（现只记「`Return` 按 `Function Main` 语义处理」）并在公共 API 文档标注「`Script<T>` 同名 API 在 C# 与 VB 下『什么产生结果』不同」。
8. **加「C# 基准声明」段**：声明/状态模型 ↔ csx（提交类字段、跨提交可见性）、文件执行退出码 ↔ C# 顶层语句、REPL 打印 ↔ csi；每处 C# 引用旁标注是哪一面。修 §4「C# 入口签名切换」的对照口径（那是顶层语句的性质，不是脚本面的性质）。
9. **U2 定稿**：`<Initialize>`/`<Main>`/`<Factory>`/`Submission#N` 不是公开契约、源码不可按名引用（镜像 `top-level-statements.md:83-87` 的措辞），但**与 C# 实现逐字一致**，工具可依赖其稳定性。`scriptClassName` 不同：它是编译选项（`VisualBasicCompilationOptions.vb:72`），默认名 `Script` 可被源码按名引用；提交模式下被宿主替换为 `Submission#N`（`#` 不可书写）。
10. **U3 定稿**：`DeclarationKind.Script → TypeKind.Class`、`DeclarationKind.Submission → TypeKind.Submission`（`SourceMemberContainerTypeSymbol.vb:156-160`）；跨提交可见性、`<Factory>` vs `<Main>`、`WithEvents` 挂钩三者均按 TypeKind 分叉，spec 必须分开写，不得把提交语义写成脚本模式通例。
11. **U5 定稿：`WithEvents`/`Handles` 不得静默**。提交类无挂钩（`SourceMemberContainerTypeSymbol.vb:2804-2807` 的 `'TODO: anything to do here?`），非提交脚本类走普通 Class 路径（`:2808`）。规范对 `WithEvents` 的定义是挂钩**必须发生**的语义（`type-members.md:1978`：「The `WithEvents` modifier causes the variable to be renamed with a leading underscore and replaced with a property of the same name that does the event hookup.」），缺失即 `Handles` 子句静默不生效——违反 VB「不静默失败」底线。裁决：**实现挂钩或给显式诊断，二选一，不接受静默**。
12. **U6 诊断族补全并逐条标注 C# 对应物**：补 `WRN_MainIgnored`（BC42367，`VisualBasicCompilation.vb:1667-1672`、`VBResources.resx:4370-4371`；C# 有同名 `WRN_MainIgnored = 7022`，`ErrorCode.cs:1168`）与顶层标签/`GoTo`（`SourceMemberContainerTypeSymbol.vb:2613-2615` 的 TODO，标签不进初始化器）。清单逐条标注：`Namespace` ↔ CS7021（同名同义）、`Yield` ↔ CS7020、非脚本 `#R`/`#Load` ↔ CS7011/CS8097、`Me`/`MyBase`/`MyClass` 与「初始化器之外的 `Return`」标「C# 无对应」（csx 允许 `this` 与顶层 `return`）；BC31003/BC30545 标为通用诊断复用。
13. **U1 定稿**：提交链是**会话级存活**（进程退出即弃）、**无上限是有意的**（任何上限都会让「会话状态保持」不可预期）；`SynthesizedSubmissionFields` 每提交一字段的开销属实现细节，spec 不承诺。
14. **补 AOT 边界一句**（D2/M5）：`.vbx` 提交属脚本运行时，**不参与 AOT/trimming**；AOT 路线只覆盖普通 VB 编译产物。
15. **补 tooling 后果**（§6）：累积的 `Imports` 只在 `Compilation.Options.GlobalImports`，不在提交语法树；基于语法树判断会话 `Imports` 的编辑器/工具必须改读编译选项。
16. **Motivation 补 vblang 自家立场**（`vbldm-notes-2017.12.06.md:80-85`，#102 Deferred to January 2018，方向是 reconcile），并正面回答同语言自家团队的调和诉求。
17. **措辞与对称性收尾**：§8「`Function Main` 语义」改为「脚本结果（script result）」，并说明它由合成初始化器承载；§「与 C# 顶层语句的对照」表改对称 + spec 明写「顶层声明的作用域 = 顶层代码 + 后续提交，不含嵌套类型内部」；裸 `Return` 补默认值的措辞带上 `System.String` 特例（`InitializerRewriter.vb:230-237`、`Binder_Statements.vb:5164-5172`）；区分 `HasSubmissionResult`（宿主打印决策，只在 Object 提交下有语义）与 Object 判据（脚本结果的产生规则）；**重写期判据的 spec 措辞不得照抄 C# 的 `IsSubmissionSyntaxTree`**（VB 侧无此 API，fork 用「最后一个初始化器 + 全局语句初始化器」，`InitializerRewriter.vb:210-211`）；**方言边界段补一句 csx → vbx 的搬运成本**——csx 里 `this.X` 是访问前序提交状态的正常写法，搬到 vbx 要改写（`Binder_Expressions.vb:2263-2269`）。
18. **记录卫生**：typed 提交收窄与 BC36965/BC36966 记入 `upstream-merge.md`（新增 VB 错误码不入账本，未来与上游 VB 撞号时无据可查）。

### Implication:

- **定性面**：本提案是**记录型**——它把 Roslyn scripting 既有机制在本 fork 的完整形态钉成 spec 基线。会议结论是「机制成立、基线需补」，不是「方案待定」；提案正文不改，修正全部以本 RESOLUTION 为准。
- **规范面**：spec 新增/加强四块——`Imports` 跨提交累积的方言偏离声明与规范化规则、脚本编译选项默认值、诊断族完整清单（含 C# 对应物标注）、基准声明（csx / C# 顶层语句 / csi 三分）。
- **语义面**：显式 `Me` 系禁令收窄到脚本初始化器/构造函数，顶层成员体回归实例方法语义（实施项 + 新测试）；`WithEvents`/`Handles` 在脚本类里「实现或显式诊断」，不留静默。
- **互操作面**：末尾表达式收窄明确为「与 C# 顶层语句同向、与共享 `Script<T>` 有意分叉」——进 `upstream-merge.md` 与公共 API 文档，防合并时被上游静默改回；合成名与 C# 逐字一致是既有加分项，工具可依赖。
- **回归面**：收窄判据与新增诊断均需新测试；编译器测试树已裁剪，回归网需在本 fork 重建。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTION`：显式 `Me` 收窄判据的精确边界——`MyBase`/`MyClass` 在顶层成员体里放行到什么程度（`MyBase` 在 `Friend NotInheritable` 的 script class 上解析到 `Object`，语义无害但需 spec 明说）。
- `OPEN QUESTION`：`Imports` 累积的「同别名不同目标」期望行为——后提交覆盖、前提交获胜、还是显式诊断；XML namespace 前缀重定义同理。
- `TODO`：`Imports` 累积失败降级为诊断（当前 `GlobalImport.Parse` 会抛 `ArgumentException`）。
- `TODO`：`WithEvents`/`Handles` 在提交类上二选一（实现挂钩 / 显式诊断），并补非提交脚本类走普通 Class 路径的分叉测试。
- `TODO`：把 typed 提交收窄、BC36965/BC36966 记入 `upstream-merge.md`；在公共 API 文档标注 `Script<T>` 的语言相关结果语义。
- `Suspect`：§8 称「上游把末尾表达式的值当作提交结果」——本仓库无上游 VB 基线可 diff（`Compilers\VisualBasic\Portable` 已是 fork 树），C# 平行实现的无条件转换（`Binder_Initializers.cs:254-264`）与产品记录（`spec\README.md:27`）足以支撑该判断；若要升级为实锤，需与 `upstream-merge.md:10` 的基准 commit `0e401fcf` 做 diff。
- `Suspect`：顶层 `Sub`/`Function` 体内 `Await` 的保留字状态——脚本顶层没有可见的 `Async` 修饰符，编译器靠 `Parser.IsScript` 把整棵编译单元视作 async 上下文（`CompilationUnitContext.vb:32-36`）；进入非 async 的顶层成员体后是否回到 `unreserved elsewhere`（`expressions.md:4923`）未运行验证。
- `Follow-up`：`Option Strict Off` 下晚绑定属性访问作为提交末尾语句走 BC30491（`spec-optional-question-prefix.md:103` 记为 pre-existing behavior）——REPL「敲表达式即打印」在晚绑定属性上并不完整，与本提案 §8 的 Object 判据相邻，spec 的 Considerations 应点明边界。
- `Follow-up`：`HasSubmissionResult` 与 Object 判据是否同源（spec 说明，或让前者也过 Object 判据）。

### 状态

- **LDM 状态：Active**。
- **三态判定：Active**——模型是 Roslyn scripting 既有机制的复用与 fork 完成，方向无悬念（两路独立复核一致：VB 基因「附带条件支持/置信度中」、C# 生态「附带条件支持/置信度高」）；提案 §1–§8 锚点逐一复核，绝大多数属实，三处修复与一处收窄均已在册（**已采纳**）。会议裁定的修正集中在：§4 `<Factory>` 签名失实（`As T` → `Task(Of T)`）、§7 `Me` 系禁令范围收窄、§6 `Imports` 累积写成方言偏离声明、补脚本编译选项默认值、§8 基准与理由改写、诊断族补两条并标注 C# 对应物、AOT 边界与 tooling 后果——全部为 spec 化可交付的收口，不构成设计阻塞。**剩余未定项**（`Imports` 规范化边界、`Me` 收窄的精确边界、`WithEvents` 二选一、上游基线 diff）均已带备选与证据锚，由 spec 与另立跟踪的实施项分别收口。下一阶段是 **spec 化**（`InternalDevDocs\spec\spec-scripting-dialect.md`）——本单元是记录型，能力已在册、无实现任务；RESOLUTION 中的实施项（`Me` 判据收窄、`Imports` 累积失败降级诊断、`WithEvents`/`Handles` 二选一）另立跟踪，不在本单元 spec 范围内。
- **独立五维评审为不入库工作材料**（git-ignored），仅供主持人/规划内部使用，不混入官方会议记录。
