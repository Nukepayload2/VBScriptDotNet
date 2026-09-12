# 脚本方言的声明与提交模型 / Scripting Dialect: Declarations and Submissions

* [x] Proposed
* [x] Prototype: Complete（Roslyn scripting 既有机制；随 2.0 beta 在 fork 内落地）
* [x] Implementation: Complete（2.0 beta 修复记录在册，`spec\README.md:23-27`；测试面见 §10）
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本提案记录 VBScript.NET 脚本/交互方言的**声明与提交模型**：`SourceCodeKind.Script` 下，顶层 `Dim` 是 script class 的**字段**、顶层 `Sub`/`Function` 是 script class 的**成员**、顶层 `Class`/`Module` 是 script class 的**嵌套类型**、顶层可执行语句是 script class 的**实例初始化器**（在 `<Initialize>` 方法体里按源码顺序展开）；script class 由编译器合成，名字取自 `VisualBasicCompilationOptions.ScriptClassName`（默认 `Script`），提交（submission）模式下由宿主改为 `Submission#N`。每条提交是一个独立 compilation，经 `PreviousScriptCompilation` 链串联，跨提交可见的只有链上前序 script class 的成员与宿主对象成员。入口点由编译器合成（`<Initialize>` / `<Main>` / `<Factory>`），脚本初始化器本身是 `Async Function` 返回 `Task(Of T)`，入口同步等待——这是顶层 `Await` 可用的**根因**，不是语法放宽。

本模型不是 fork 发明：它是 Roslyn scripting 的既有机制（`SourceCodeKind.Script` + `ImplicitNamedTypeSymbol` + 合成入口点），C# 侧的顶层语句（`Program.Main`）与 `.csx` 方言共用同一套设计；本 fork 在此机制上完成了三处修复（顶层 `Await`、顶层 `AddHandler`/`RemoveHandler`、`Imports` 跨提交累积）与一处相对上游的收窄（typed 提交的末尾表达式不作结果）。本提案的职责是把这个已实现的方言**说清楚**，为 spec 化提供唯一的事实基线。

## Motivation
[motivation]: #motivation

- **脚本与交互要求免包装。** 传统 VB 要求代码位于 `Module`/`Class` 内。C# 顶层语句的三场景讨论把「简单程序」「顶层函数」「脚本/交互」并列，并明确「**Submission system allows state preservation across evaluations**」（`csharplang\meetings\2020\LDM-2020-01-22.md:15-23`），最终优先落地 (1) 与 (3)（同文件 `:36-41`）。`.vbx` 与 `vbi` REPL 就是 VB 侧的第 (3) 场景，脚本方言的声明模型必须支撑「敲下声明即可用、下一条提交还能用」。
- **1.2 已发布，但三处损坏。** 顶层免包装随 1.2 beta 发布（微软商店，`proposals\README.md:73`），但顶层 `Await`、顶层 `AddHandler`/`RemoveHandler`、`Imports` 交互模式在 1.2 为损坏状态（`spec\README.md:15`、`meetings\meeting-vb-repl-parity-with-csharp-repl.md:18`）。三者均已在 2.0 beta 修复（`spec\README.md:23-27`）——**本提案记录的是修复后的完整方言**。
- **顶层 `Await` 常被误读为语法放宽。** 它其实不需要任何语法改动：脚本初始化器本身就是 `Async Function`，顶层语句在它体内，`Await` 只是普通 VB 规则在普通 async 上下文里的正常用法（证据见 §4）。把这一点写清楚，是防止后续设计在「脚本方言是否要特殊语法」上走弯路的前提。
- **需要钉死 fork 相对上游的收窄。** 上游把「末尾表达式的值」当作提交结果（REPL 打印用），本 fork 收窄为**只有 Object 提交**把末尾表达式当结果，typed 提交（`.vbx` 退出码）必须显式 `Return`（`spec\README.md:27`、`Binder_Initializers.vb:219`、`InitializerRewriter.vb:208-211`）。这是与 C# `Program.Main` 语义对齐的判断，且与 C# LDM 拒绝「末尾表达式产生结果」进入语言本体的立场同向（`csharplang\meetings\2020\LDM-2020-04-15.md:36-42`）。

## Detailed design
[design]: #detailed-design

> 证据等级标注：本节每个关键设计点后标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑）。锚点均在本 fork 源码中逐条复核，故为**已检查**；已随版本发布或已由 2.0 修复记录在册的为**已采纳**。本阶段未运行测试，故不出现「已运行」/「有结果支撑」；相关测试用例存在性在 §10 注明。

### 1. script class 的生成与命名

`SourceCodeKind.Script` 的语法树在声明表构建阶段**不生成隐式类，而生成 script class**。`DeclarationTreeBuilder.CreateScriptClass`（`Declarations\DeclarationTreeBuilder.vb:131-162`）以 `CompilationUnit` 节点自身为语法引用，构造一个 `SingleTypeDeclaration`：

- `kind` = `DeclarationKind.Submission`（提交）或 `DeclarationKind.Script`（非提交的脚本编译），由 `_isSubmission` 决定（`:140`）；
- 修饰符固定为 `Friend Or Partial Or NotInheritable`（`:143`）——script class 不是 static、不含扩展成员（`:192-193`）；
- 名字取自 `_scriptClassName`，按 `.` 拆分成嵌套 namespace（`:136`、`:151-159`），因此 `ScriptClassName` 可以是 `A.B.C` 形式的点分名。

名字的默认值来自选项：`VisualBasicCompilationOptions` 构造函数的 `scriptClassName As String = WellKnownMemberNames.DefaultScriptClassName`（`VisualBasicCompilationOptions.vb:72`），而 `WellKnownMemberNames.DefaultScriptClassName = "Script"`（`Compilers\Core\Portable\Symbols\WellKnownMemberNames.cs:67`）。选项值经 `VisualBasicCompilation.ForTree`（`Compilation\VisualBasicCompilation.vb:1063-1065`）传入声明表构建器；非法 CLR 类型名在选项校验时报 `ERR_InvalidSwitchValue`（`VisualBasicCompilationOptions.vb:1050-1051`）。

**提交模式下名字被宿主替换。** `ScriptBuilder.GenerateSubmissionId` 生成 `Submission#N` 作为每次提交的 `scriptClassName`（`Scripting\Core\ScriptBuilder.cs:68-75`），宿主把它传给 `VisualBasicCompilationOptions`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:215`）。因此 REPL/`.vbx` 里实际可见的 script class 名是 `Submission#1`、`Submission#2`……而 `Script` 是选项默认值（普通脚本编译路径，如 `VisualBasicCommandLineParser.vb:1505` 的 `/i` 与脚本模式编译）。

script class 在符号层是 `ImplicitNamedTypeSymbol`：`NamedTypeSymbol.IsScriptClass`（`Symbols\NamedTypeSymbol.vb:682-686`）、`IsSubmissionClass => TypeKind = TypeKind.Submission`（`:691-695`）；`GetScriptConstructor` / `GetScriptInitializer` / `GetScriptEntryPoint` 三个访问器把合成成员定位出来（`:697-711`）。`VisualBasicCompilation.ScriptClass` 是惰性绑定的（`VisualBasicCompilation.vb:477`、`:2002-2012`、`:2021-2023`）。

**证据等级：已检查。**

### 2. 顶层声明到 script class 的映射

非 Regular 的语法树走专门分支（`DeclarationTreeBuilder.vb:175-201`）：遍历 `node.Members`，`DeclarationKind.Namespace` 的声明照常进入编译单元的子节点（**仅为改善错误报告**，见 `:184-186`，诊断在解析期给出，见 §7），**其余全部进 `scriptChildren`**（`:179-196`），成为 script class 的成员声明。

四种顶层形态的落点：

| 顶层形态 | 语法/绑定落点 | 符号落点 |
|---|---|---|
| `Dim x = …` | 编译单元（`SyntaxKind.CompilationUnit`）下 `ParseVarDeclStatement` 置 `isFieldDeclaration = True` → `FieldDeclarationSyntax`（`Parser\Parser.vb:2093-2111`） | `AddMember` 的 `FieldDeclaration` 分支 → `SourceMemberFieldSymbol.Create`（`SourceMemberContainerTypeSymbol.vb:2549-2557`） |
| `Sub` / `Function` / `Declare` / `Operator` | 方法块或方法语句节点 | `AddMember` 的方法分支 → `CreateMethodMember`（`:2559-2593`） |
| `Class` / `Module` / `Structure` / `Interface` / `Enum` / `Delegate` | 类型块节点 | 作为 `SingleTypeDeclaration` 进 `scriptChildren`，成为 script class 的**嵌套类型**（`DeclarationTreeBuilder.vb:179-196`） |
| 可执行语句 | `ExecutableStatementSyntax` / `EmptyStatement` | `AddMember` 的 `Case Else` 分支：`BindingTopLevelScriptCode` 为真时登记为**实例初始化器**（`:2625-2637`） |

关键区分：顶层 `Dim` 是**字段**而非局部变量。字段符号由 `SourceMemberFieldSymbol.Create` 建在 script class 上（`:2549-2557`），顶层 `Sub`/`Function` 是**同一类型的实例成员**（`:2559-2593`）——同类型实例成员之间可直接引用，故顶层方法体内可访问顶层 `Dim`；字段又是实例状态，故跨提交存活（`InteractiveSessionTests.Fields`，`Scripting\VisualBasicTest\InteractiveSessionTests.vb:14-22`）。`Binder.BindingTopLevelScriptCode` 精确定义了「什么算顶层脚本代码」：包含成员是脚本构造函数或脚本初始化器（全局语句），或包含类型是 script class（脚本变量初始化器）（`Binding\Binder.vb:428-443`）。

顶层语句的执行顺序由 `InitializerRewriter.BuildScriptInitializerBody` 保证：`<Initialize>` 的方法体 = 重写后的初始化器序列（字段初始化器与全局语句**按源码顺序**）+ 原始块语句 + 结果 `Return`（`Analysis\InitializerRewriter.vb:174-186`）。注意 script class 的**构造函数**体是空的（`MethodCompiler.vb:1481-1487` 的 `method.IsScriptConstructor → body = block`），普通类的实例初始化器才注入构造函数（同文件 `:1486`）——这是「脚本初始化器」与「构造函数」在脚本类里分工不同的地方。

**证据等级：已检查。** 顶层免包装能力本身已随 1.2 发布（`proposals\vbx-1.2-beta\proposal-top-level-code.md`、`proposals\README.md:73`）→ **已采纳**。

### 3. 提交链与跨提交可见性

提交信息由 `VisualBasicScriptCompilationInfo` 承载：本类型持有 `PreviousScriptCompilation`，返回类型与 globals 类型由基类 `ScriptCompilationInfo` 以 `ReturnTypeOpt`/`GlobalsType` 持有（`Compilation\VisualBasicScriptCompilationInfo.vb:7-31`；基类见 `Compilers\Core\Portable\Compilation\ScriptCompilationInfo.cs:11-18`）。`VisualBasicCompilation` 在 `isSubmission` 时构造它（`VisualBasicCompilation.vb:479-484`），并暴露 `PreviousSubmission`（`:833-837`）。`CreateScriptCompilation` 有两个重载：单树（`:345-362`）与多树（`:368-389`，一个提交可由多棵树组成，提交结果由**最后一棵树**决定——`HasSubmissionResult` 的注释与实现见 `:839-858`）。

**跨提交可见性只覆盖两类成员**，由名字查找直接实现：

- **前序提交的 script class 成员。** `LookupInSubmissions`（`Binding\Binder_Lookup.vb:858-926`）从当前 compilation 起，沿 `PreviousSubmission` 链逐级对 `submission.ScriptClass` 做 `LookupWithoutInheritance`，直到没有可行成员为止（`:874-917`）；查不到再落到宿主对象类型（`:919-926`）。符号级 `LookupSymbolsInfo` 同构（`:2030-2051`）。绑定接收方时，`TryBindInteractiveReceiver` 对 `TypeKind.Submission` 的声明类型生成 `BoundPreviousSubmissionReference`，对宿主对象成员生成 `BoundHostObjectMemberReference`（`Binding\Binder_Expressions.vb:2611-2630`，调用点 `:2567-2572`）。
- **宿主对象成员。** globals 类型由 `CompilationOptions`/`ScriptCompilationInfo` 携带（`VisualBasicCompilation.vb:927-936` 的 `CommonScriptGlobalsType`/`GetHostObjectTypeSymbol`）。

**状态保持的运行时机制。** 每个提交的 script class 各有一个合成构造函数，接收 `submissionArray As Object()`（`SynthesizedSubmissionConstructorSymbol.vb:19-38`）；`SynthesizedSubmissionFields` 为链上每个前序提交各合成一个字段，并为宿主对象合成 `<host-object>` 字段（`Lowering\SynthesizedSubmissionFields.vb:15-21`、`:50-62`、`:64-70`）；降级时 `BoundPreviousSubmissionReference` 变成对这些字段的访问（`Lowering\LocalRewriter\LocalRewriter_PreviousSubmissionReference.vb:13-23`），构造函数体里由 `MakeSubmissionInitialization` 填充（`SynthesizedSubmissionConstructorSymbol.vb:55-59`，调用点 `MethodCompiler.vb:1536`）。隐式解析的引用（宿主解析出的程序集）也沿链继承，避免重复解析出不同符号（`Symbols\ReferenceManager.vb:325-328`）；匿名类型模板同样要求前序提交先物化（`MethodCompiler.vb:224-229`）。

宿主侧用 `Script.Previous` 串链：`ContinueWith` 以当前脚本为 `previousOpt` 构造新脚本（`Scripting\Core\Script.cs:100-118`、`:64`），且**不继承引用与 Imports**（`InheritOptions`，`:133-139`）——引用由编译链继承，Imports 由 §6 的宿主侧机制补回。

**证据等级：已检查。**

### 4. 入口点合成与顶层 `Await` 的根因

script class 的合成成员（`SourceMemberContainerTypeSymbol.vb:2758-2768`）：脚本构造函数由 `EnsureCtor` 建（`:2758`；提交类走 `:2726-2737` 的 `SynthesizedSubmissionConstructorSymbol`），合成初始化器与入口点由 `:2761-2768` 加入成员表——即 `SynthesizedInteractiveInitializerMethod` 与 `SynthesizedEntryPointSymbol`。

- **`<Initialize>`**（`SynthesizedInteractiveInitializerMethod.InitializerName`，`:12`）：`IsAsync = True`（`:51-55`），返回类型 `Task(Of T)`——`CalculateReturnType` 取 `ScriptCompilationInfo.ReturnTypeOpt`，无则用 `System.Object`，最终 `returnType = Task_T.Construct(resultType)`（`:148-172`）。
- **`<Main>`**（`SynthesizedEntryPointSymbol.MainName`，`:15`）：非提交脚本编译的入口，`Private Shared Sub <Main>()`，体为 `Dim script As New Script() : script.<Initialize>().GetAwaiter().GetResult()`（`:219-309`，注释在 `:247-250`、生成在 `:269-308`）。
- **`<Factory>`**（`FactoryName`，`:16`）：提交的入口，`Private Shared Function <Factory>(submissionArray As Object()) As T`，体为 `Dim submission As New Submission#N(submissionArray) : Return submission.<Initialize>()`（`:312-389`）。`GetScriptEntryPoint` 按 `TypeKind` 选名字（`NamedTypeSymbol.vb:707-711`）。编译期 `MethodCompiler` 识别这三个合成成员并跳过常规成员处理（`MethodCompiler.vb:559-570`、`:665-674`），`<Initialize>` 的方法体由 `InitializerRewriter.BuildScriptInitializerBody` 组装（`:1488-1490`）。

**顶层 `Await` 的根因。** 解析层把编译单元视为「async 方法或 lambda 内部」：`CompilationUnitContext.IsWithinAsyncMethodOrLambda => Parser.IsScript`（`Parser\BlockContexts\CompilationUnitContext.vb:32-36`），于是 `Await` 在顶层被解析为 `ParseAwaitStatement`（`Parser\Parser.vb:798-802`）；绑定层 `IsInAsyncContext` 对脚本类的字段/属性初始化器返回真（`Binding\Binder_Expressions.vb:4720-4727`），`BindAwait` 因此不报「Await 不在 async 上下文」（`:4740-4744`）。由于顶层语句确实位于 `IsAsync = True` 的 `<Initialize>` 体内，`Await` 是**普通 VB 规则**在普通 async 方法里的正常用法；无需为脚本方言新增语法。C# 侧的选择不同：顶层语句的签名按是否使用 `await` 在 `Task`/`void`/`int` 之间切换（`csharplang\proposals\csharp-9.0\top-level-statements.md:99-105`，`csharplang\meetings\2020\LDM-2020-04-15.md:66-74`），VB 则固定合成 `Task(Of T)` 初始化器 + 同步 `<Main>`。

**证据等级：已检查。** 顶层 `Await` 修复记录在册（`spec\README.md:24`）→ **已采纳**。

### 5. 顶层 `AddHandler` / `RemoveHandler`

脚本顶层出现的 `AddHandler` / `RemoveHandler` 按**语句**解析，而非事件访问器声明：`Parser.vb:828-838` 在 `IsScript AndAlso Context.BlockKind = SyntaxKind.CompilationUnit` 时直接走 `ParseStatementInMethodBodyInternal()`，否则才按属性/事件访问器处理。因此 `AddHandler t.E, Sub() …` 是普通可执行语句，进入 §2 的实例初始化器路径。

`AddHandler` 语句本身的绑定不依赖脚本特性；但 `WithEvents` 字段的事件挂钩走 `AddWithEventsHookupConstructorsIfNeeded`，该方法对 `TypeKind.Submission` 目前是显式 `TODO`（`SourceMemberContainerTypeSymbol.vb:2804-2807`）——这是本模型的一条已知边界（见 Unresolved questions）。

**证据等级：已检查。** 2.0 修复记录在册（`spec\README.md:25`）→ **已采纳**。

### 6. `Imports` 跨提交累积（宿主侧机制）

`Imports` 跨提交累积**不是编译器机制，而是宿主侧机制**。`Script.InheritOptions` 明确不继承 Imports（`Scripting\Core\Script.cs:133-139`，注释 "they have already been applied"），而 VB 宿主在构造每条提交的编译选项时把整条链的 Imports 重新汇总：

`VisualBasicScriptCompiler.GetGlobalImportsForCompilation`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:113-120`）合并「本提交 `script.Options.Imports`」与「链上前序提交的 Imports」，去重后用 `GlobalImport.Parse` 转成 `GlobalImport` 列表，作为 `globalImports` 选项传给 `CreateScriptCompilation`（`:206`、`:216`）。`AddPreviousSubmissionImports`（`:134-162`）递归走 `script.Previous`，对每个前序提交收集两处来源：编译选项里的 `GlobalImports`（`:146-148`）与各语法树 `CompilationUnitSyntax.Imports` 的 clause 文本（`:150-161`）；去重键为 `StringComparer.OrdinalIgnoreCase`（`:115`），符合 VB 标识符大小写不敏感。编译器侧只消费 `Options.GlobalImports`，没有任何 submission 专属分支。

**证据等级：已检查。** 2.0 修复记录在册（`spec\README.md:26`）→ **已采纳**；测试 `InteractiveSessionTests.Imports_CrossSubmission`（`:24-33`）与 `Imports_DoNotReplaceInheritedOptionsImports`（`:35-47`）锁定该行为。

### 7. 脚本专属限制与诊断族

脚本模式引入一组专属诊断。已实现的成员如下（错误码与调用点均已核实）：

| 诊断 | 码 | 触发条件与调用点 |
|---|---|---|
| `ERR_NamespaceNotAllowedInScript` | 36965 | 脚本里写 `Namespace`：解析期直接给 `namespace` 关键字加错（`Parser\Parser.vb:1677-1679`；`Errors.vb:1598`）。声明表仍按 Namespace 处理以改善后续错误报告（`DeclarationTreeBuilder.vb:184-186`） |
| `ERR_KeywordNotAllowedInScript` | 36966 | 脚本类内**显式**使用 `Me` / `MyBase` / `MyClass`（隐式引用放行）：`Binding\Binder_Expressions.vb:2263-2269`（`Errors.vb:1599`） |
| 同上（`Return`） | 36966 | 判据 `BindingTopLevelScriptCode AndAlso Not IsScriptInitializer`：`Binder_Statements.vb:5090-5093`。顶层语句位于 `<Initialize>` 内，故顶层 `Return 42` 合法（它就是初始化器的返回）；该分支拦的是脚本类里「处于顶层脚本代码但不是初始化器」的上下文 |
| 同上（`Yield`） | 36966 | 顶层脚本代码里的 `Yield`：`Binder_Statements.vb:5229-5230` |
| `ERR_UnexpectedExpressionStatement` | 31003 | **非末尾**裸表达式语句：`Binder_Statements.vb:2652-2673`（判定用 `IsFinalStatementOfSubmission`，非末尾才报） |
| `ERR_PropertyAccessIgnored` | 30545 | 属性访问/晚绑定属性组被当语句用；**仅提交末尾语句抑制**：`Binder_Statements.vb:2802`、`:2816-2820`，抑制开关由 `:2655-2663` 传入 |
| 裸 `Return` 补默认值 | — | 脚本初始化器内的裸 `Return` 补类型默认值（值类型 `ConstantValue.Default`，否则 `Nothing`）：`Binder_Statements.vb:5162-5173`；`InitializerRewriter` 侧对「无末尾表达式」同样补默认值（`InitializerRewriter.vb:225-242`） |

诊断族分两层：`Namespace` 在**解析期**报，`Return`/`Yield`/显式 `Me` 系与末尾表达式规则在**绑定期**报。两者的共同判据都是「是否处于脚本类顶层脚本代码」，判据实现见 `Binder.BindingTopLevelScriptCode`（`Binder.vb:428-443`）与 `BinderFactory` 的 `NodeUsage.TopLevelExecutableStatement`（`Binding\BinderFactory.vb:179-181`）、`NodeUsage.ScriptCompilationUnit`（`:172-177`，注意其中仍有上游遗留的 TODO 注释）。

**证据等级：已检查。**

### 8. 末尾表达式：只有 Object 提交把它当结果

脚本初始化器的结果类型由提交的 `ReturnTypeOpt` 决定：`.vbx` 文件执行用 `CreateInitialScript<int>`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:243`，退出码由 `RunAsync(...).ReturnValue` 取回，`:256`），REPL 用 `CreateInitialScript<object>`（`:291`、`:357`）。

本 fork 相对上游的**收窄**：末尾表达式只有在结果类型是 `System.Object` 时才成为提交结果。两处实现一致：

- 绑定期：`BindGlobalStatement` 仅当 `submissionReturnType.IsObjectType()` 时把末尾表达式隐式转换为结果类型（`Binding\Binder_Initializers.vb:211-224`，判据在 `:219`）；
- 重写期：`RewriteInitializersAsStatements` 仅当 `submissionResultType.IsObjectType()` 且是最后一个初始化器时把末尾表达式语句提升为结果（`Analysis\InitializerRewriter.vb:205-223`，判据在 `:208-211`）；无结果时补默认值（`:225-242`）。

两处门的判据都是「结果类型是 `System.Object`」（`IsObjectType()` 扩展定义于 `Symbols\TypeSymbolExtensions.vb:339-342`），本提案称之为**Object 提交判据**。typed 提交（`ReturnType = Integer`）因此遵循 `Function Main` 语义：**只有显式 `Return` 产生结果**。REPL 打印路径的编译侧查询是 `VisualBasicCompilation.HasSubmissionResult`（`VisualBasicCompilation.vb:839-900`），本 fork 在其中加入了方法组感知（`:869-881`、`:902-919`），并经 `Script.HasReturnValue()` 暴露（`Scripting\Core\Script.cs:306-310`）。

这与 C# 的判断同向：C# 明确拒绝「末尾表达式产生结果」进入语言本体，只把它留给交互方言（`csharplang\meetings\2020\LDM-2020-04-15.md:36-42`）。**退出码的数值语义不在本提案范围内**（属后续单元）；本提案只声明「返回类型承载结果、typed 提交必须显式 `Return`」这一结构事实。

**证据等级：已检查。** 收窄记录在册（`spec\README.md:27`）→ **已采纳**。

### 9. 可运行示例

以下 `.vbx` 脚本同时覆盖 §2、§4、§5、§8（`.vbx` 由 `vbi main.vbx` 执行；`Print` 是宿主对象成员，`CommandLineScriptGlobals.Print`，`Scripting\Core\Hosting\CommandLine\CommandLineScriptGlobals.cs:37`）：

```vbnet
' main.vbx —— 顶层声明直接书写，无需 Module 包装
Imports System.Threading.Tasks

Public Class TestEvents               ' script class 的嵌套类型
    Public Event SomethingHappened As EventHandler
    Public Sub Raise()
        RaiseEvent SomethingHappened(Me, EventArgs.Empty)
    End Sub
End Class

Public Function AddOne(value As Integer) As Integer   ' script class 的成员
    Return value + 1
End Function

Dim counter As Integer = 0            ' script class 的字段（不是局部变量）
Dim t As New TestEvents               ' 字段初始化器与全局语句按源码顺序在 <Initialize> 里执行

AddHandler t.SomethingHappened, Sub() counter += 1    ' 顶层 AddHandler 按语句解析

Dim value = Await Task.FromResult(13)                 ' 顶层 Await：初始化器本身是 Async Function
Print(AddOne(value))                                  ' 14
t.Raise()
Print(counter)                                        ' 1

Return 42                                             ' 显式 Return 承载结果（typed 提交）
```

REPL 里同一条模型表现为跨提交可见性（§3、§6）：

```text
> Imports System.Text
> Dim sb = New StringBuilder("abc")     ' script class 的字段，存活到后续提交
> Console.WriteLine(sb.Length)
3
```

**证据等级：已检查。** 等价测试用例存在（未运行，见 §10）：顶层 `Await`（`CommandLineRunnerTests.vb:505-516`、`:518-529`）、顶层 `AddHandler`（`:531-551`）、跨提交字段与声明（`InteractiveSessionTests.vb:14-22`、`:68-98`）、跨提交 `Imports`（`:24-33`）、显式 `Return` 承载结果（`TestReturnStatementSetsExitCodeInScriptFile`，`:448-458`；`TestBareReturnStatementExitCodeIsZero`，`:460-470`）、末尾表达式不承载结果（`:423-433`）。

### 10. 测试面（`Scripting\VisualBasicTest`，无副作用）

本方言的回归面已在既有测试类中建立（本阶段只读、未运行）：

- `InteractiveSessionTests.vb`：`Fields`（顶层 `Dim` 跨提交存活）、`Imports_CrossSubmission`、`Imports_DoNotReplaceInheritedOptionsImports`、`PreviousSubmissions_Declarations`（顶层 `Function`/`Class`/`Module`/`Delegate` 跨提交可见）、匿名类型跨提交。
- `CommandLineRunnerTests.vb`：`TestTopLevelAwaitInScriptFile`、`TestBareAwaitStatementInScriptFile`、`TestTopLevelAddHandlerInScriptFile`、`TestReturnStatementSetsExitCodeInScriptFile`、`TestBareReturnStatementExitCodeIsZero`、`TestQuestionDirectiveInScriptFileDoesNotSetExitCode`。
- `ScriptTests.vb`：`TestRunScriptWithSpecifiedReturnType`、`TestRunScriptWithTypedReturnType` 等 typed/Object 提交的返回类型断言。

### 11. 与其它单元的分工

本提案只覆盖**声明与提交模型**。以下属其它单元或已有 spec，不在本文重复：`#R` / `#Load` / `#!` 指令语义（单元 2、3 与 `spec\spec-shebang-directive.md`）；REPL `?` 前缀与自动打印（`spec\spec-optional-question-prefix.md`）；脚本 globals 的成员清单（`proposals\vbx-1.2-beta\proposal-script-globals.md`）。退出码的数值语义不在本提案范围内（范围声明见 §8）。

## Drawbacks
[drawbacks]: #drawbacks

- **每条提交是一次完整编译。** 提交链上每次 `ContinueWith` 都新建 compilation 并沿链查找（§3），无增量编译；长会话的编译时间与内存随链长增长，且 `SynthesizedSubmissionFields` 为每个前序提交各加一个字段（`SynthesizedSubmissionFields.vb:19-21`）。
- **合成类型使错误定位与调试模糊。** 顶层声明落在 `Friend NotInheritable` 的合成 script class 里（`DeclarationTreeBuilder.vb:143`），栈帧与类型名（`Submission#N`）对用户不友好；顶层代码的错误上下文比单文件编译更难读。
- **方言与普通 VB 的差异面。** 脚本里 `Return` 在顶层合法、`Namespace` 非法、显式 `Me` 非法、裸表达式在末尾合法——这些都是普通 VB 里不成立或不存在的规则。C# 设计组对「第三种方言」的担忧（`csharplang\meetings\2020\LDM-2020-02-26.md:46-56`）同样适用于 VB：差异面越大，脚本代码与普通代码互搬的成本越高。
- **宿主侧 `Imports` 累积的维护成本。** 累积靠遍历前序提交的语法树与选项（`VisualBasicScriptCompiler.vb:134-162`），宿主必须与编译器的 Imports 语义保持同步（例如新的 imports clause 形态），这是一处需要长期维护的耦合。

## Alternatives
[alternatives]: #alternatives

- **不做免包装，要求显式 `Module` 包装。** 传统 VB 形态；脚本与 REPL 的「敲下即可用」体验无从谈起。1.2 起已采用免包装（`proposals\vbx-1.2-beta\proposal-top-level-code.md`）。
- **宿主重写文本，把顶层代码包进 `Module`。** 会整体漂移诊断行号、让宿主重复实现解析与绑定判定，且编辑器/工具看不到同一棵树。与 `proposal-shebang-directive.md` 对「宿主剥行」的否决同构。
- **为顶层 `Await` 放宽语法。** 不需要：`<Initialize>` 本身就是 `Async Function`（§4），`Await` 是普通规则。C# 走的是另一条路（按是否使用 `await` 切换入口签名，`LDM-2020-04-15.md:66-74`），VB 采用固定 `Task(Of T)` 初始化器 + 同步等待，语义更统一。
- **让末尾表达式在 typed 提交也当结果。** 未采用。上游语义下这会与「`Function Main` 由 `Return` 决定结果」冲突，也与 C# 拒绝「末尾表达式产生结果」进入本体的判断相左（`LDM-2020-04-15.md:36-42`）。本 fork 的收窄是 §8 的定案。
- **把 `Imports` 累积做进编译器（提交链在编译器内维护 Imports 状态）。** 未采用：宿主已有 `script.Previous` 链与 `ScriptOptions.Imports`，在宿主侧汇总只需重放选项，编译器侧保持「只消费 `Options.GlobalImports`」的单一职责。

### 与 C# 顶层语句的对照

C# 的顶层语句把语句序列语义化为 `Program` 类里的 `static async Task Main(string[] args)`（`csharplang\proposals\csharp-9.0\top-level-statements.md:10-22`、`:67-81`），顶层局部变量/函数「在作用域内但不可从类内访问」（`:185-203`，`LDM-2020-03-09.md:58-63`）。VB 的 script class 模型在**结构上同源**（免包装 + 合成入口点），但两处关键差异是 VB 的方言边界：

| 维度 | C# 顶层语句 | VB 脚本方言 |
|---|---|---|
| 顶层 `Dim` | 生成的 `Main` 里的**局部变量**（语义化定义 `top-level-statements.md:67-81`；问题提出见 `LDM-2020-01-22.md:25-32`） | script class 的**字段**（§2），因此跨提交存活 |
| 顶层 `Sub`/`Function` | 局部函数，类内不可访问 | script class 的**成员**，同提交内可直接调用 |
| 入口签名 | 按 `await`/`return` 在 4 种签名间切换（`top-level-statements.md:99-105`） | 固定 `Async Function <Initialize>() As Task(Of T)` + 同步 `<Main>`/`<Factory>`（§4） |
| 末尾表达式 | 不作结果（`LDM-2020-04-15.md:36-42`） | 仅 Object 提交作结果（§8），typed 提交必须显式 `Return` |

这张表就是「VB 脚本方言 = Roslyn scripting 的提交模型 + VB 的声明落点」的具体含义，也是上文各条取舍的共同背景。

### 方言边界原则

**有意识地保持方言与普通 VB 语义的最小差异**是本产品对 REPL/脚本方言的既定态度。本提案与该原则一致——`?` 是打印标记、末尾表达式不参与退出码、自动打印仅限交互式；脚本方言的差异被限制在「声明落点」与「入口点合成」这两件结构性事实上，而不是在表达式/语句语义上另开一套规则。

## Unresolved questions
[unresolved]: #unresolved-questions

以下均为**已实现能力在 spec 化时尚未覆盖的边界**，不是设计未定：

1. **提交链无界增长没有语义约束。** `PreviousScriptCompilation` 链与 `SynthesizedSubmissionFields` 的字段数都随会话长度线性增长（`SynthesizedSubmissionFields.vb:19-21`、`:64-70`），目前没有链长上限、也没有「链上某条提交何时可被回收」的规范表述。spec 需说明链的生命周期语义（会话级存活）与是否存在建议上限。
2. **`<Main>` / `<Factory>` 名字的契约地位。** `SynthesizedEntryPointSymbol.MainName = "<Main>"`、`FactoryName = "<Factory>"`（`:15-16`）是编译器内部名，源码不可按名引用；C# 顶层语句提案明确「the actual name used by the compiler is implementation dependent and the method cannot be referenced by name from source code」（`top-level-statements.md:83-87`）。spec 需明确 VB 是否同样把这两个名字排除出公开契约，以及 `scriptClassName` 可被源码按名引用的边界。
3. **`DeclarationKind.Script` 与 `DeclarationKind.Submission` 的可见性表述。** 两者共用一个 script class 模型（`DeclarationTreeBuilder.vb:140`），但 `TryBindInteractiveReceiver` 只对 `TypeKind.Submission` 生成前序提交接收方（`Binder_Expressions.vb:2612`），入口点名字按 `TypeKind` 二选一（`NamedTypeSymbol.vb:707-711`），且非提交路径要求 `previousSubmission`/`returnType`/`globalsType` 全为 `Nothing`（`VisualBasicCompilation.vb:482-484` 的断言）。spec 需分别给出两种 kind 下「跨提交可见性」的确切含义，避免把提交语义误述为脚本模式通例。
4. **`Imports` 累积的规范化边界。** 宿主把每个 imports clause 用 `clause.ToString()` 转字符串后 `GlobalImport.Parse`（`VisualBasicScriptCompiler.vb:156-159`、`:119`），去重用 `OrdinalIgnoreCase`（`:115`）。别名（`Imports X = Y`）、`Global` 前缀、重复但写法不同的 clause 在 spec 中的规范化形式尚未定义。
5. **脚本类里 `WithEvents` 的语义。** 顶层 `AddHandler` 已按语句支持（§5），但 `WithEvents` 字段的事件挂钩在提交类上是显式 `TODO`（`SourceMemberContainerTypeSymbol.vb:2804-2807`）。spec 需说明脚本类是否支持 `WithEvents`，若不支持应给出何种诊断。
6. **脚本限制诊断族的完整性。** `ERR_KeywordNotAllowedInScript` 目前只有 `Return`、`Yield`、显式 `Me`/`MyBase`/`MyClass` 三处调用点（§7）；顶层 `On Error Resume Next`、顶层 `RaiseEvent` 走的是另一组解析/绑定诊断（`meetings\meeting-vb-repl-parity-with-csharp-repl.md:19` 记录为 BC30024/BC30188/BC30205/BC36956）。spec 需给出脚本顶层禁止语法的**完整清单**与各诊断的归属层，而不是只描述已实现的三处。

## 相关文档

- `csharplang\proposals\csharp-9.0\top-level-statements.md` — C# 顶层语句语义化为 `Program.Main`（本提案 Alternatives 的对照基准）
- `csharplang\meetings\2020\LDM-2020-01-22.md` — 三场景与 submission 状态保持
- `csharplang\meetings\2020\LDM-2020-02-26.md` — 「第三种方言」担忧
- `csharplang\meetings\2020\LDM-2020-04-15.md` — 末尾表达式不进语言本体；`await` 触发不同入口签名
- `csharplang\meetings\2020\LDM-2020-03-09.md` — 顶层语句视作位于 async `Main` 内
- `proposals\vbx-1.2-beta\proposal-top-level-code.md` — 1.2 顶层免包装归档（本提案的前身记录）
- `proposals\vbx-1.2-beta\proposal-script-globals.md` — 脚本 globals（宿主对象成员）
- `proposals\vbx-1.2-beta\proposal-repl-interactive-session.md` — REPL 交互会话
- `proposals\proposal-optional-question-prefix.md` / `spec\spec-optional-question-prefix.md` — 裸表达式打印（与本提案 §8 的「结果」判据正交）
- `proposals\proposal-shebang-directive.md` — `#!` 指令（同属脚本文件头部机制）
- `meetings\meeting-vb-repl-parity-with-csharp-repl.md` — REPL 与 C# REPL 对齐会议（方言边界原则的出处，RESOLUTION 第 3 条）
- `upstream-merge.md` 2.2 / 2.6 — 脚本编译链与顶层代码/脚本提交语义的本地修改面台账
