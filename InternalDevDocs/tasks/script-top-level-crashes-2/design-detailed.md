# 逐单元改动蓝图（script-top-level-crashes-2）

> 引用一律回读被引用的那一行原文后再写。每个单元的 pass 条件独立可判。

## U4 · 顶层显式 `MyBase` 不再撞非空断言（先做，改动面最小）

**现状**：`Binder_Expressions.vb:2369-2379`。错误路径构造 `BoundMyBaseReference` 时取 `Me.ContainingType.BaseTypeNoUseSiteDiagnostics`，而提交类该值为 `Nothing`（§二 C4 实锤）。

**改法**：错误路径的类型形参沿用同一文件 `BindMeExpression` 已有的兜底写法（`:2351` 的 `If(Me.ContainingType, ErrorTypeSymbol.UnknownResultType)`），把「取不到基类型」折成 `ErrorTypeSymbol.UnknownResultType`。正常路径（`:2379`）不动。

**pass 条件**：
1. `vbi.exe` 跑 `tmp\probes\lt3\mybase-script-expr.vbx` / `mybase-script-in-sub.vbx` / `mybase-script-in-shared-sub.vbx`：exit `1`，输出含 `BC36966`，**没有** `Process terminated`。
2. 普通 `Module` / `Structure` 的对照诊断不变（BC32001 / BC30044）。
3. 普通类的 `MyBase`（继承基类）仍 exit 0 且输出 `B`。
4. 单元测试：语义层最小用例（`VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb` 同族）断言诊断 id 与「不抛异常」。

## U3 · `Handles` 子句在提交类上生效

**现状**：`SourceMemberMethodSymbol.vb:773-784` 的 `Select Case ContainingType.TypeKind` 无 `Submission` 分支 ⇒ `Throw ExceptionUtilities.UnexpectedValue`。

**改法**：把 `TypeKind.Submission` 并入 `Case TypeKind.Class, TypeKind.Module`。

**依据**：提交类**是**类容器（`NamedTypeSymbol.vb:691-695`）；Hookup 宿主的选取（`SourceMemberMethodSymbol.vb:790-807`）只用到「实例构造器列表非空」与「共享构造器」，对提交类同样成立。**实锤**：可行性补丁下实例与 `Shared` 两形状均 exit 0，且 handler 真的被调用（输出 `H`）。

**pass 条件**：
1. `handles-script-instance` / `handles-script-shared` 两个探针 exit 0 且输出 `H`。
2. 普通类/模块的 `Handles` 对照不变（`handles-ordinary` exit 0）。
3. 单元测试：`ScriptTopLevelCrashTests.vb` 同族用例，覆盖「实例 `WithEvents` + `Handles` 投递计数」与「`Shared WithEvents` + `Handles`」，断言的是**投递真的发生**（计数 = 1），不只是编译通过。

## U2 · 脚本类实例事件的 `RaiseEvent` 降级

**现状**：`Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:22-100`。脚本类的非限定成员引用经 `Binder_Expressions.vb:2570-2576` → `TryBindInteractiveReceiver`（`:2616-2618`）解析为 `BoundPreviousSubmissionReference`，而 `Else` 分支（现 `:32` 起）原有的 `#If DEBUG` 断言块假定事件字段的接收者是 `Me`（该断言块已按下方改法删除）。

**改法（已实施）**：删掉「接收者已经是最终形态」的断言（原 `:32-39` 的 `#If DEBUG` 块，现已删除），在其位置**降级接收者**——`receiver = VisitExpressionNode(receiver)`（现于 `:37`），落在 `node.EventSymbol.IsWindowsRuntimeEvent` 分支之前。理由：`Else` 分支之后的逻辑（临时量、空检查、把调用接收者换成临时量）与接收者形状无关；不降级会让 `BoundPreviousSubmissionReference` 穿透到 `EmitExpression.vb:209` 的 `Case Else`（**实锤**：可行性补丁下 exit `2148734499`，栈顶 `Unexpected value 'PreviousSubmissionReference'`）。降级写法与 `VisitPreviousSubmissionReference`（`LocalRewriter_PreviousSubmissionReference.vb:13-23`）一致，也与同方法 `If` 分支（现 `:27-30`）走整棵调用树的做法一致。

**pass 条件**：
1. `raise-script-instance-event-in-sub` / `raise-script-instance-event-in-lambda` exit 0。
2. **投递可观测**：实例事件的 handler 计数 = 1（不是「编译通过」）。
3. `Shared Event` + 顶层 `Shared Sub` 的 raise 路径行为不变（`raise-top-level-with-shared` 仍输出 `H`）。
4. 普通类里 raise 自己的实例事件不变（`raise-ordinary` exit 0）。
5. 单元测试：既有 `TopLevelSharedEvent_RaiseReachesTheHandler` 的实例版；并**移除** `TopLevelEvent_AccessorsRun` 的 XML 注释里「实例事件的 raise 路径不可达」那段缺口说明（该缺口由本单元关闭）。

## U1 · 提交类里声明实例构造器 → 新诊断

**现状**：`SourceMemberContainerTypeSymbol.vb:2744-2762` 的提交类实例分支**无条件**合成 `SynthesizedSubmissionConstructorSymbol`，不管用户是否已声明实例构造器 ⇒ 成员表出现两个 `.ctor`。

**两种症状，取决于构造器在源文件中的位置**（穷尽扫描实测）：
- **症状 A** — 构造器是首个记号（或只被注释/空行前导）⇒ `Debug.Assert(comparison <> 0)`（`LexicalOrderSymbolComparer.vb:43`，经 `GetMembers()` `SourceMemberContainerTypeSymbol.vb:3179-3187`），exit `2148734499`。
- **症状 B** — 构造器之前有**任何**东西（语句 / `Imports` / 类声明 / `Enum`）⇒ `InvalidOperationException: Sequence contains more than one element`，栈顶 `NamedTypeSymbol.vb:699` 的 `.Single()`，经 `MethodCompiler.vb:565`，exit `2148734217`。

**访问修饰符不改变判定**：`Protected` / `Private` / `Public Sub New()` 与裸 `Sub New()` 同症状；`Shared Sub New()` 不受影响（exit 0），`Shared Sub New(x)` 另有既有诊断。⇒ 判据按 `MethodKind = MethodKind.Constructor` 取，不看修饰符。**两种症状都要在 pass 条件里验证消失。**

**判「报错」的理由**见 `design-overview.md` §3（三条实锤不变量）。

**改法（落点已定位）**：成员收集入口是 `SourceMemberContainerTypeSymbol.AddMember(memberSyntax, …)`（`:2536`），它对 `SyntaxKind.SubNewStatement` / `SyntaxKind.ConstructorBlock` 走 `:2574-2589`（或 `:2559-2572`）后调 `CreateMethodMember` 再调 `AddMethodMember`。在这一步加判据：

> 当 `Me.IsScriptClass` 且刚创建的成员 `MethodKind = MethodKind.Constructor`（**实例**构造器；`Shared` 构造器不受影响，探针 `ctor-top-level-shared-sub-new` exit 0）时：**报新诊断**，并**不把该成员加入成员表**。

判据用 `IsScriptClass`（`SourceMemberContainerTypeSymbol.vb:1295-1300` = `DeclarationKind.Script OrElse DeclarationKind.Submission`）而非 `TypeKind.Submission`：覆盖提交类与**非提交脚本类**两类——后者由生成的 `<Main>`（`Symbols\Source\SynthesizedEntryPointSymbol.vb:258-281`）实例化，只判 `TypeKind.Submission` 时同一崩溃原样存在（`InvalidCastException`：`SourceMemberMethodSymbol` → `SynthesizedConstructorBase`）。`IsScriptClass` 与 `DeclarationKind.ImplicitClass` 互斥（`IsImplicitClass` 于 `:1302-1306`），普通文件里游离语句的隐含类没有脚本入口点，不被卷入；同文件 `:2786` 的初始化器与入口点合成本就以 `IsScriptClass` 为判据。

**不加入成员表是必须的**，不是简化：只要两个 `.ctor` 同时在场，`NamedTypeSymbol.GetScriptConstructor`（`Symbols\NamedTypeSymbol.vb:697-700` 的 `InstanceConstructors.Single()`）就抛 `InvalidCastException`（**实锤**：可行性补丁下 exit `2147500034`，栈顶即此行）。报错之后不再发射，被拒声明的缺席不产生二次症状。

**新增诊断**（走完整注册链）：
- `Errors.vb:1817` 的 `ERR_SubmissionCannotDeclareInstanceConstructor` 取新码 **37342**，紧邻 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`），同属脚本专属带；`ERR_NextAvailable` 现为 `37344`（`:1820`，`:1818` 的 `37343` 归 `ERR_WithEventsVariableNotInContainingType`）。
- `ErrorFacts.IsBuildOnlyDiagnostic` 的 `Return False` 清单**必须**同步加该 id（否则 `DiagnosticTests.TestIsBuildOnlyDiagnostic` 抛 `NotImplementedException`，Semantic 门整体挂掉）。
- `VBResources.resx` 英文产品文案一条，13 份 xlf 同步（按 U6 的口径）。
- 文案要点：脚本类不支持声明实例构造器，**因为脚本实例由生成的入口点创建、其构造器由编译器合成**（提交类的宿主实例化与之一致）；初始化代码放顶层语句。产品文案（`VBResources.resx:4731` 与 13 份 xlf 的同一 `trans-unit`）即按此表述，对两类脚本类都成立。

**pass 条件**：
1. **症状 A 与症状 B 都要消失**：`tmp\probes\sweep\b3_more\ctor-*-first.vbx`（构造器在首位）与 `ctor-*-after-stmt.vbx`（构造器在语句之后）两类都要 exit 1、输出含新码、**没有** `Process terminated`，也没有 `Sequence contains more than one element`。
2. 三种访问修饰符（`Protected` / `Private` / `Public`）与带参形状同判定。
3. `ctor-top-level-shared-sub-new` 行为不变（exit 0）；`Shared Sub New(x)` 的既有诊断不变。
4. 普通类声明构造器行为不变（`ctor-ordinary` exit 0）。
5. 新码**不**出现在 `PublicAPI.*.txt`（`ERRID` 是 `Friend Enum`）。
6. 单元测试：空参 / 带参 / 带访问修饰符 / 语句之后 / `Shared` 对照五条，落在 `ScriptTopLevelCrashTests.vb` 同族；判别性载体是「撤销判据后该用例必失败」。
7. **非提交脚本类同样受判**：`VisualBasicCompilation.Create` + `TestOptions.Script` + `WithScriptClassName("Script")` 下，空参 / 带参 / 语句之后的 `Sub New()` 三条各报唯一 `BC37342`，且 `GetDiagnostics()` 与 `Emit` 都不再抛（发射不成功）；同一编译里嵌套普通类的 `Sub New()` 零诊断（判别性对照）。落在 `ScriptSemanticsTests.vb:901-941`。

## U5 · 顶层 `Finally` 里的分支不得离开该块 → `BC30101`

**现状**：`ERR_BranchOutOfFinally = 30101`（`Errors.vb:166`）的唯一**报点**是流分析 `ControlFlowPass.VisitFinallyBlock`（`Analysis\FlowAnalysis\ControlFlowPass.vb:152-181`，`:167` 的 `errId = …`）。顶层语句的宿主 `<Initialize>` 方法体是空壳、初始化器不做流分析（`Compilation\MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?'`），判据从不执行 ⇒ 分支活到发射期并产出 JIT 拒绝的方法。

**形状面比「`GoTo`」宽得多**（穷尽扫描实测，全部 exit `2148734266`）：离开顶层 `Finally` 的**任何分支**都中招——`GoTo`、`Return`（含带值）、`Exit For` / `Exit While` / `Exit Do` / `Exit Select`、`Continue For`；嵌套 `Finally`、`Finally` 落在 `Using` / `Catch` 内、多 `Catch` 的 `Try`、`Await` 之后的 `Finally`、`#Load` 进来的文件里的 `Finally`，全部同症状。**只修 `GoTo` 会漏掉 `Return`/`Exit`/`Continue`**——判据必须按「块内 pending 分支」整体取，不能按语句种类枚举。

**边界（实锤）**：同一个跳出形状放进**顶层 lambda** 就会被正常报 `BC30101`（exit 1）——缺口严格限于**顶层语句路径**。反向（跳**进**保护块）由绑定层另行报错（`goto-into-finally` / `goto-into-catch` / `goto-into-using` / `goto-into-loop-from-outside` 等全部 exit 1），不属本单元。

**改法**：复用第一轮已建立的「逐顶层语句补判据」模式（`Binding\Binder_Initializers.vb:134-140` 的调用点 + `:242-252` 的 `CheckAwaitInTryHandler`），为顶层语句补跑该判据。约束：

- **只对该顶层语句的语法子树里含 `Finally` 块的那些运行**（子树扫描，判法见下方「落点细节」），把成本与影响面限制在有嫌疑的形状上。**不能**按顶层语句本身的语法种类判。
- 判据来源是 `ControlFlowPass`，它会把结果写进一个诊断袋；**只取 `ERR_BranchOutOfFinally`**，其余（可达性、未赋值、未使用等）本任务**不**引入——那是「顶层语句缺流分析」的另一半，另立 issue（§账本与规范义务）。
- 诊断位置与普通上下文一致：`GoTo` 时是 `GoToStatementSyntax.Label`（`ControlFlowPass.vb:169-173`）。

**落点细节（已查证，供实施者直接用）**：
- 调用入口可照抄同文件 `CheckAwaitInTryHandler` 的写法（`Binder_Initializers.vb:242-252`）：把该条顶层语句包进一个合成的 `BoundBlock`，再交给判据。
- 判据 API：`ControlFlowPass.Analyze(info As FlowAnalysisInfo, diagnostics As DiagnosticBag, suppressConstantExpressionsSupport As Boolean)`（`Analysis\FlowAnalysis\ControlFlowPass.vb:49`）；`FlowAnalysisInfo` 的构造为 `(compilation, method, block)`（`Analysis\FlowAnalysis\FlowAnalysisPass.vb:28`）。`method` 用 `binder` 手上那个合成初始化器方法（`SynthesizedInteractiveInitializerMethod`）即可；`compilation` 取 `binder.Compilation`。
- **不要**用 `FlowAnalysisPass.Analyze`——它同时跑 `ControlFlowPass` 与 `DataFlowPass`（`FlowAnalysisPass.vb:28-29`），后者会带来本任务不想要的诊断。
- 只取 `ERR_BranchOutOfFinally`：给 `ControlFlowPass` 一只**一次性诊断袋**，只把该 id 的条目转写进 `BindingDiagnosticBag`，其余丢弃。
- 便宜的前置门：**必须按该顶层语句的语法子树判**（递归 `ChildNodes` 找「含 `FinallyBlock` 的 `TryBlockSyntax`」），**不能**按顶层语句本身的语法种类判——`Finally` 常常嵌在 `For`/`While`/`Using`/`Catch` 里面，那时顶层语句是 `ForBlock`/`UsingBlock`/不带 `Finally` 的 `TryBlock`，按种类判会整批漏掉（实测漏 8 个形状）。

**待解风险（实施者必须先证伪）**：把 CFG 通过在一句孤立语句上运行时，块内 `GoTo` 指向块外标签会留下未解决的 pending 分支。需确认该情形既不产生 `BC30101`（误报），也不触发断言（可用 `top-level-goto-forward-across-decl`、`goto-out-of-try-top`、`jumpout-catch-script` 等既有形状回归）。**回退方案**：改为把该树全部顶层语句合成一个块后再跑一次，判据与位置不变。

**pass 条件**：
1. `jumpout-finally-script` / `jumpout-nested-finally-script`：exit 1，输出含 `BC30101`，位置与普通对照组同一行同一列。
2. `tmp\probes\run_probes4.py` 的 `BLOCKS` 11 个块跳转形状里其余 10 个行为**不变**（exit 0）。
3. `flow-unreachable-code` / `flow-goto-forward-across-decl` / `top-level-goto-into-lambda` 等既有顶层跳转形状行为不变（不得引入新诊断）。
4. 全仓既有测试零新增基线变更（尤其 Semantic / Emit 门的既有脚本用例）。
5. 单元测试：`ScriptTopLevelCrashTests.vb` 一条 `TopLevelGoToOutOfFinally_IsReported`，断言 `BC30101` + 「不再产出可运行产物」。

## U6 · 资源同步

### U6a · 13 份 xlf

**现状**：每份 xlf 各缺 **7** 条 `trans-unit`（自算集合差，**实锤**）。`Directory.Build.props:4` 的 `ErrorOnOutOfDateXlf=false` 与 `:5` 的 `UpdateXlfOnBuild=false` 让任何构建门都不报。

**改法**：按缺失 id 补齐。新条目取 `<target state="new">`，`target` 与 `source` 逐字等于 resx 的 `<value>`，`<note />` 与相邻条目同形；**插入位置按该文件里的本地邻居定位，不按 resx 顺序重排**（`cs.xlf` 实测有 31 处与 resx 顺序倒挂）。**先审 resx 英文文案再同步 13 语言**。

**实测形状**：13 份各补 **7** 条 `trans-unit`（同一批 7 个 id），每份 `+35` 行、零删除，落在 3 个连续块；13 份结果均为 `units=1813 / missing=0 / extra=0`。

**pass 条件**：集合差归零；13 份 xlf 的既有条目零改动（比对前把两边 EOL 归一，见 P-113）。

### U6b · `issues\README.md` 状态列

issue 10 / 11 改标 `Fixed`，commit 号按实际提交填（**不得预填、不得推测**）。

### U6c · `spec\spec-scripting-dialect.md`

顶层 `GoTo` 的 Decision 与顶层标签收集后的行为不一致。**跑 spec 阶段**（`subagents/spec-writer.md`），不在实现单元里顺手改。

## U7 · 薄弱面测试补强

**目的**：把「脚本模式崩溃」从「逐个探针发现」升级为「一张可回归的矩阵」。

**做法**：在 `Scripting\VisualBasicTest\` 增加**无副作用**的脚本模式符合性矩阵（内存 `VisualBasicScript` / `CommandLineRunner`，不落盘、不起进程）。每个用例的断言形态统一为：**编译要么成功、要么给出诊断——不得抛异常/终止进程**；能跑的再断言运行结果。

**覆盖面（VB 特有语法 × 顶层位置）**，逐格建用例，缺格即为下一轮的修复线索：

| 维度 | 取值 |
|---|---|
| 声明 | `Dim`（含 `As New`、数组上界、`ReadOnly`）、`Const`、`Property`（auto/手写/`Default`/带参/`ReadOnly`/`Shared`）、`Event`（字段式/自定义/`Shared`）、`Sub`/`Function`（实例/`Shared`/`Async`/`Iterator`/`Overloads`）、`Operator`（一元/二元/转换/`Widening`/`Narrowing`）、`Declare`、嵌套类型（`Class`/`Module`/`Structure`/`Interface`/`Enum`/`Delegate`）、`WithEvents` |
| 修饰与属性 | `Shared`、`Overloads`、`Partial`、`<Extension>`、`<Obsolete>`、`<DllImport>`、`<Conditional>` |
| 语句 | `Await`（各块内）、`GoTo`/标签（各块进出）、`On Error`/`Resume`、`Yield`、`Exit *`、`Return`、`Using`、`SyncLock`、`With`、`Select Case`、`For`/`For Each`/`While`/`Do`、`AddHandler`/`RemoveHandler`/`RaiseEvent` |
| 引用 | 隐式/显式 `Me`、`MyBase`、`MyClass`、上一提交引用、`Handles` 子句 |
| 选项 | `Option Strict`/`Infer`/`Explicit`/`Compare` 各档 |

**输出的去向**：矩阵里任何一格出现 `2148734499` / `2148734266` / 未处理异常 ⇒ **当场立为新单元**，按 `progress` 排进 Vortex 循环修完再继续铺矩阵。

**本任务的已知空白（矩阵的起点，来自穷尽扫描的「未覆盖」清单）**：
- **REPL / 多提交路径**——扫描的 390 个探针全部是单文件 `.vbx`，**没有覆盖**交互式会话与提交链。U9（跨提交 `Handles`）正出在这里，说明这是当前最肥的一块。矩阵必须补：跨提交的字段/导入/可见性/`WithEvents`/`Handles`/`AddHandler` 引用、每提交一个类型的链、`#Load` 与提交链叠加。
- **Release / `/optimize+` 发射**——现有探针都是 Debug 构建，而 `IteratorAndAsyncCaptureWalker` 会按优化档分支。
- **interop 的其余形状**：`ComClass`、`ComImport`、`PreserveSig`、`MarshalAs`、`Declare … Alias/Auto/Ansi/Unicode`、PIA/`NoPia`。
- **三层以上嵌套 lambda**（`Async` × `Iterator` × `Await` × `Yield` 交错）；`End` / `Stop`（不可进程内跑，需另想办法或登记为不可测）。

**参照**：C# 脚本模式测试（`{{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs`、`CommandLineRunnerTests.cs`、`InteractiveSessionTests.cs`）作为**用例形态**的蓝本；行为判据仍以本仓 spec 与普通编译上下文实测为准。

## 账本与规范义务

| # | 义务 | 落点 |
|---|---|---|
| 1 | 五族各建一份 `issues\issue-*.md` 并在 `issues\README.md` 表里登记为 **12–16**（现有清单到 11 为止）：触发面 / 根因（`文件:行号`）+ 预期行为 + **修复后的新增行为变化**小节（C1 与 C5 会新增诊断，必须写明） | `design-detailed.md` 本表即为清单 |
| 2 | 「顶层语句缺流分析」的其余后果建档——**只限实测成立的那一条**：顶层语句里的**块内局部变量**拿不到 `BC42104`。`BC42105` 不受影响、不可达代码警告在全编译器里整体不产生、块外顶层 `Dim` 是字段本就不参与（三条均为实测排除，见 README §一 非范围第 3 条） | `issues\issue-top-level-statements-skip-flow-analysis.md`（已建，编号 17） |
| 3 | 本任务变更面登记 | `upstream-merge.md` |
| 4 | `spec-scripting-dialect.md` 顶层 `GoTo` 条目修订 | spec 阶段 |
| 5 | **空壳方法体**这一共同机制（第一轮根 A + 本任务 C5）在 `design-overview` 层留一处索引，便于后续再补判据时找到同一入口 | 第一轮 `tasks\script-top-level-crashes\design-overview.md` 的根 A 节 |
