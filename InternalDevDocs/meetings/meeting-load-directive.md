# Visual Basic Language Design Meeting
September 10, 2026

议题是 `proposal-load-directive`——把 `.vbx` 与 REPL 的**多文件组织唯一机制** `#Load "字符串"` 钉成唯一事实基线。它是一份记录型提案（`Prototype: Complete`、`Implementation: Complete`），记录的是 2.0 beta 已落地、并在 2.16 与 NuGet 预扫描共用同一展开点的机制：`#Load` 在语法上是编译期指令 trivia（`LoadDirectiveTriviaSyntax`，`SyntaxKind.LoadDirectiveTrivia = 751`；关键字 `LoadKeyword = 793`），只在 `SourceCodeKind.Script` 下合法，且必须出现在编译单元首个 token 之前；但操作数的解释与展开全部是**宿主行为**——`VisualBasicScriptCompiler.CollectLoadTrees` 用 `ScriptOptions.SourceResolver` 把字符串解析成真实路径、逐文件解析为独立语法树、深度优先递归嵌套 `#Load`，再把「加载树在前、主树在后」的多棵语法树交给 `CreateScriptCompilation`。它不进声明表、不碰引用脏标记，与 `#R` 的「编译器语法 + 引用管理器」路径正交。

我们两条独立路径各读了一遍——一条沿 VB 基因问「这像不像 VB、有没有给普通 VB 开第二种做事方式」，一条沿 C# 生态问「它与 C# `#load`、与共享 `Compilers\` 合不合」。定性很快收敛：**骨架可以原样采纳**。`#Load` 与 `#R` 同形同门（`Syntax\Syntax.xml:9552-9558` vs `:9544-9550`，都只有「关键字 + 字符串」两个子节点、都没有 `EndOfDirectiveToken`），多树 script class 骑的是 VB 既有的 **Partial 类型**机制（`vblang\spec\types.md:1127` 逐字「the declaration of the type may be spread across multiple partial declarations within the program」），脚本层没有工程文件、源码面唯一的包含机制就是它，没有给普通 VB 增加第二种做事方式。语法、门控、树序、`Return` 语义都不动。

会议的实际内容落在复核撞出的几处：**提案有两处把设计说得比实际更糟**（失败通道、`/check` 契约），**一条路径把失败后果推得更糟**（「REPL 会崩会话」），**三处论证方向被源码推翻**（环分码的成本、菱形去重的理由、展开点归属），**两处事实缺口被两条路径独立命中**（公开 API 台账、`SourceResolver` 无守卫）。我们按「先复核、再权衡、后裁决」记下这段推理旅程；提案是冻结输入，对它的修正只写进 RESOLUTION。

## Agenda

* [Proposal: `#Load` 源文件加载指令 / Load Directive](#proposal-load-源文件加载指令--load-directive)

## Proposal: `#Load` 源文件加载指令 / Load Directive

_Related: [`../proposals/proposal-load-directive.md`](../proposals/proposal-load-directive.md)；同层头部指令 `../proposals/proposal-reference-directive.md`（`#R` 本体，Active）与 `../proposals/proposal-shebang-directive.md` / `../spec/spec-shebang-directive.md`（`#!`）；方言与提交模型 `../proposals/proposal-scripting-dialect.md` / `../meetings/meeting-scripting-dialect.md`；共用展开点邻域 `../proposals/proposal-vbi-nuget-reference.md`；修复记录 `../issues/issue-vbx-load-span-shift.md`；共享层台账 `../upstream-merge.md` 2.5 / 2.16；`../decisions.md` M5 / D4_

> **来源标注**：正文引用的源码与规范文本均逐字核对（`文件:行号`）。提案 §1–§8 的锚点逐一复核，绝大多数属实；复核中发现两处事实性表述不准（Drawbacks 的失败通道两条）、一处锚点指错文件（§5a 的 `LexicalSortKey`）、一处措辞会误导（§5c 的 `Imports` 边界）、一处论证方向被 C# 实现推翻（Alternatives 的展开点归属），另有两条缺口被两条路径独立命中（U5 公开 API 台账、U4 `SourceResolver` 守卫）。提案是冻结输入，对它的修正只写进 RESOLUTION。

### 场景与缺口

`.vbx` 与 REPL 没有工程文件，把一段逻辑拆成多个文件**只能靠 `#Load`**；它决定「加载文件的成员能不能被主文件调用」「两个文件的顶层语句谁先跑」「加载文件的诊断落在哪个文件」这三类脚本作者每天都会撞到的问题。而归档里只有一行记录（`spec\README.md:36`「已移植 C# interactive 的 **`#Load`** 指令」）。更关键的是，它是 2.0 的**修复产物**：1.2/早期 2.0 的 `#Load` 是脚本层纯文本内联，把被加载文件的文本拼进主文件再解析，导致主文件 `#Load` 之后所有 span 下移 N−1 行、被加载文件内的诊断连文件与行号都不对；改成「编译器指令 trivia + 多树提交」后零漂移（`issues\issue-vbx-load-span-shift.md:39-43`、`:77-97`）。这条修复带来的一串结构性后果（多树 script class、`SyntaxReferences.First()`、binder 根按所在树）必须写清楚，否则后续设计会误以为「脚本提交只能有一棵树」。

我们认同这份「记录型」定位——它不是发明，而是把已落地并已被依赖的机制说清楚。定性上没有悬念，会议的重心因此全部落在**事实准确性**与**边界收口**上。

### 翻源码：§1–§8 锚点复核

**§1 语法面逐条属实。** 两个指令节点同构：`LoadDirectiveTriviaSyntax` 派生自 `DirectiveTriviaSyntax`，子节点只有 `LoadKeyword` 与 `File`（`StringLiteralToken`），**没有 `EndOfDirectiveToken`**（`Syntax\Syntax.xml:9552-9558` vs `:9544-9550`）——行尾终结符由扫描器在指令之外消费。枚举值 `LoadDirectiveTrivia = 751`（`Syntax\SyntaxKind.vb:3048`）、`LoadKeyword = 793`（`:3216`），与同批 `ShebangDirectiveTrivia = 752`（`:3052`）一样是**追加**，不位移既有值。解析派发在条件编译指令处（`Parser\ParseConditional.vb:85-86`），`ParseLoadDirective`（`:474-492`）与 `ParseReferenceDirective`（`:454-472`）逐行同构：`Not IsScript` 挂 `ERR_LoadDirectiveOnlyAllowedInScripts`（`:482-483`；`Errors\Errors.vb:1606` = 36967），`ElseIf isFollowingToken` 挂 `ERR_PPLoadFollowsToken`（`:484-485`；`Errors.vb:1632` = 37002）——两个门是 `If/ElseIf`，同一关键字上只挂一枚；消息逐字（`VBResources.resx:4398`「#Load is only allowed in scripts」、`:4401`「Cannot use #Load after first token in file」）。位置判据是 `_directiveIsFollowingToken = _leadingTriviaStartOffset > 0`（`Scanner\Directives.vb:41-43`），注释逐字「Mirrors C# #load/#r.」；语法树 API 把它固化为「只从编译单元首个 token 的 leading trivia 取」（`Syntax\CompilationUnitSyntax.vb:33-41`，`#R` 的同款在 `:20-28`）。操作数必须是字符串字面量（`:488-489`），缺字面量走 BC30217。这里还牵出一条 spec 立类的诉求：VB 原版规范的指令族是**自由位置**的——条件编译指令可出现在任意逻辑行序列中（`vblang\spec\preprocessing-directives.md:9-18` 的 `CCStart : CCStatement*`，`LogicalLine` 与条件编译指令并列）——而 `#Load` 与 `#R` 的「首个 token 之前」、`#!` 的「位置 0」是 VB 规范里的一类新东西。spec 应把「头部指令」立成一类，将三者判据与差异理由写在一起（`#!` 位置 0 是 OS 要求，`#R`/`#Load` 只需首个 token 之前），不要各写各的。

**§2 不进声明表属实，且我们补了一条编译器侧的自证。** `DeclarationTreeBuilder` 只为 `#R` 构造 `ReferenceDirective`（`Declarations\DeclarationTreeBuilder.vb:101-113`），且只在非 Regular 树分支调用（`:198`），Regular 分支固定空（`:201`）；`HasReferenceDirectives` 只问 `#R`（`Syntax\VisualBasicSyntaxTree.vb:85-91`）。提案说「全仓 `GetLoadDirectives` 的 VB 调用点只有宿主 `VisualBasicScriptCompiler.vb:86` 与测试 `ScriptTests.vb:737`」——我们核了，属实。顺着这条我们把范围扩到整个 `Compilers\`：`CollectLoadTrees` / `LoadDirectiveMap` / `TryGetLoadedSyntaxTree` 在 VB 编译器中**零命中**，这三个名字只出现在 C# 侧（`Compilers\CSharp\Portable\Compilation\SyntaxAndDeclarationManager.cs`、`SyntaxAndDeclarationManager.LazyState.cs`）。**VB 编译器没有任何 `#Load` 状态**，这条是 §2 结论的最硬证据。

**§3 宿主展开逐条属实，但三条失败路径提案没写完。** `CollectLoadTrees`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:73-111`）的每一行都与提案表一致：空操作数静默 `Continue For`（`:88-90`）、相对路径按引用树的 `FilePath` 解析（`:92-93`）、缺失/环共用 `ERR_FileNotFound` 并锚在 `directive.File.GetLocation()`（`:94-95`）、`ReadText` + 独立建树（`:98-99`）、深度优先且父树后加入（`:101-107`）、`OrdinalIgnoreCase`（`:188`）、主路径预置（`:189-194`）、主树追加末尾（`:199`）。**我们复核中确认了三处提案未写的失败面**：`options.SourceResolver` 在 `:85` 取出后 `:93` 直接调用、`:190` 又裸调 `NormalizePath`，**全程无 null 守卫**（对照：同仓的预扫描 `NuGetRestoreCoordinator.vb:209-213` 有显式守卫，注释「No source resolver: nothing to expand #Load against, so scan only the submitted tree」）；`resolver.ReadText`（`:98`）**无 try/catch**；失败由 `ThrowLoadDirectiveError` 抛 `CompilationErrorException`（`:62-64`、`:196-198`）。这三条的后果各不相同，见「权衡二」。

**§4 多树编译属实，且顺手订正了一处文档与代码不符。** `CreateScriptCompilation` 单树转发 + 多树重载（`Compilation\VisualBasicCompilation.vb:345-362`、`:368-389`）；`HasSubmissionResult` 只看最后一棵树（`:839-851`，注释逐字「The result of a submission is determined by its top-level script file, which is the last tree.」）；`SourceMemberContainerTypeSymbol` 的 `SyntaxReferences.First()` 两处注释都写着「a submission may span multiple script trees (#Load)」（`Symbols\Source\SourceMemberContainerTypeSymbol.vb:2731-2732`、`:2762-2763`）。`AddSyntaxTrees` 区间（`:993-1046`）内确实只有三条既有校验——有根节点（`:1024-1026`）、非编译器特殊树（`:1028-1030`）、树不重复（`:1032-1034`），**没有**脚本树守卫。因此 `issue-vbx-load-span-shift.md:89` 那句「改守『仅脚本树』」与代码不符；提案 §4 的描述比修复记录准确。

**§5 语义后果属实，但树序锚点指了 C# 文件。** 树序链完整：`:199` 追加序 → `Declarations\DeclarationTable.vb:171-175` 按树序排序（注释逐字「Sort the root namespace declarations to match the order of SyntaxTrees.」）→ `LexicalSortKey.Compare` 先比 `TreeOrdinal` 再比 `Position`。提案把这条判据引到 `Compilers\CSharp\Portable\Symbols\LexicalSortKey.cs:98-119`——**VB 编译走的是 `Compilers\VisualBasic\Portable\Symbols\LexicalSortKey.vb:154-178`**（我们读了，同样先 `TreeOrdinal` 后 `Position`）。提案声明「锚点均在本 fork 源码中逐条复核」，这一条没做到。`Return` 语义（`Binding\Binder_Statements.vb:5090-5093` 的禁令只拦非初始化器的顶层脚本代码）与末尾表达式只对 Object 提交生效（`Binding\Binder_Initializers.vb:211-224`，判据 `:219` `IsObjectType()`）均属实，注释逐字「typed submissions (vbx exit code) follow Function Main semantics: Return only.」。

**§6–§8 属实。** 共用展开点由 `Private Shared LoadReferencedTrees` 提为 `Friend Shared`，XML doc 逐字「also used by the NuGet host pre-scan so its walk of loaded trees cannot drift from the compiler's」（`:66-72`）；预扫描调用点与守卫（`NuGetRestoreCoordinator.vb:224-226`、`:209-213`）。示例与 `#help` 清单属实（`Scripting\Core\ScriptingResources.resx:182` 逐字「`#Load         Load specified script file and execute it, e.g. #Load "myScript.vbx".`」）。测试面我们逐条点过：span 零漂移（`ScriptTests.vb:525-548`）、加载文件诊断真实文件/行（`:550-569`）、缺失文件锚 `#Load` 行（`:571-587`）、嵌套（`:589-602`）、环锚 `mid.vbx:1`（`:604-623`）、跨树引用（`:625-637`）。

### 权衡一：公开 API 台账与死资源——两条路径独立命中

这是两条独立路径各自撞到的同一处，而且从 `Suspect` 升级为**实锤缺项**。`Compilers\VisualBasic\Portable\PublicAPI.Shipped.txt` 与 `PublicAPI.Unshipped.txt` 对 `LoadDirectiveTrivia` / `LoadKeyword` / `GetLoadDirectives` **零命中**；而这三个符号确实是公开面（`Generated\Syntax.xml.Syntax.Generated.vb:37968` 的 `Public NotInheritable Class LoadDirectiveTriviaSyntax`、`CompilationUnitSyntax.vb:33` 的 `Public Function GetLoadDirectives()`、`SyntaxKind.vb:3048`/`:3216` 的公开枚举值）。对照同批：`#R` 的 `GetReferenceDirectives` 与整组 `ReferenceDirectiveTriviaSyntax` 在 Shipped（`:412`、`:1794-1801`、`:2996`），fork 自己新增的 `#!` 整组在 Unshipped（`:1-18`）。按本仓既定做法（fork 新增 → `PublicAPI.Unshipped.txt`），`#Load` 的三组条目是**漏登**的。至于「构建会不会因 RS0016 失败」，我们没有跑构建，标 `Suspect`。

顺带清出一条死资源：单树守卫删除后，`SubmissionCanHaveAtMostOneSyntaxTree` 只剩 `VBResources.resx:129` 与 13 份 xlf 的 trans-unit（`VBResources.*.xlf:589-590`），全仓生产代码**零引用**（grep 只命中 resx / xlf / 提案自身）。

记录卫生还有两处要一起收口：`upstream-merge.md` 2.5 把 `#Load` 记成「脚本宿主 `Scripting\Core\`」，而展开点在 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb`、预扫描在 `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb`；`issue-vbx-load-span-shift.md:89` 的「改守『仅脚本树』」与 `VisualBasicCompilation.vb:1024-1034` 不符。另记一笔：`proposals\README.md` 的 Active 表（现 17 项）**没有登记本提案**。

### 权衡二：失败通道——提案把设计说得比实际更糟，两种读法里只有一种成立

提案 Drawbacks 写「`CompilationErrorException` 让 `Script.Compile()` 的调用方必须 try/catch」「`/check` 路径下缺失文件不会像普通编译错误那样只产诊断列表」。我们对失败通道有两种读法：一种顺着「`BuildAndRunAsync` 的 `newScript.Compile()` 无 try/catch」推断异常会一路逃到进程级兜底、**把 REPL 会话打崩**；另一种认为 `Compile()` 内部就把异常转成了诊断。回源一读，**后一种成立，两条 Drawback 都不成立**。

准确形状是：`ThrowLoadDirectiveError`（`:62-64`）在 `CreateSubmission` 里抛出（`:196-198`），而 `Script.Compile()` 就是 `CommonCompile`（`Scripting\Core\Script.cs:231-232`），后者的 `try` 覆盖 `GetPrecedingExecutors` / `GetExecutor`（即 `GetCompilation()` → `CreateSubmission`），并显式 `catch (CompilationErrorException e)` 转成诊断返回（`:332-346`）。于是三条宿主路径都拿到诊断而非异常：REPL 的 `BuildAndRunAsync` 先 `Compile()`（`CommandLineRunner.cs:370`），有错误就 `return (state, options)`（`:372-375`）——**丢本次提交、会话继续**；`/check` 走同一个 `Compile()`（`:245-252`），退出码契约 0/1 在 `#Load` 缺失/环场景下照样成立（`spec-vbi-script-diag-mode.md:72-77`）；文件脚本路径不先 `Compile()`，`RunAsync` 才抛，由 `:259-263` 捕获并 `ReportDiagnostics` → `Failed`。也就是说「REPL 打错一个文件名会结束会话」这条推断**被 `Script.cs:332-346` 直接推翻**。

残余的结构差异只有一条，且是 C# 生态视角下才看得见的：**失败提交连 `Compilation` 对象都不存在**（异常发生在 `CreateSubmission` 内），C# 则有一个带诊断的编译对象可供查询（`SyntaxAndDeclarationManager.cs:282` 构造 `LoadDirective`、`CSharpCompilation.cs:3135-3138` 在 `GetDiagnostics` 时汇出）。对 CLI/REPL 无影响，对分析类工具是可见差异。

真正该收口的是提案漏写的第三条：`resolver.ReadText`（`:98`）**无 try/catch**，而 C# 在同一位置把读失败包成诊断（`SyntaxAndDeclarationManager.cs:238-268`，`catch (Exception e) → CommonCompiler.ToFileReadDiagnostics`，官方测试 `LoadDirectiveTests.FileThatCannotBeDecoded` 断言二进制文件报 CS2015）。一个「存在但不可读/不可解码」的 `#Load` 目标会让原生异常逃出 `Script.Compile()`——这才是「VB 比 C# 更脆」的实打实差异，修复面很小。

### 权衡三：环与缺失分码、菱形去重——两处成本极低，两处理由不成立

**环与缺失共用 BC2001 是事实，而消息是假的。** `:94` 把两种条件压进一个 `OrElse`，`:95` 报 `ERR_FileNotFound`（`Errors.vb:37` = 2001），消息逐字是 `file '{0}' could not be found`（`VBResources.resx:165`）。循环引用时文件明明存在——在 `a.vbx` 里看到的是「找不到 `b.vbx`」，而 `b.vbx` 就在旁边。提案 Alternatives 说「分码需要额外状态」——**这句不成立**：两个条件已经在同一个 `OrElse` 的两侧，拆开就是两个分支，零额外状态。C# 侧两者也不同码（缺文件 `ERR_NoSourceFile`；环根本不报错）。

**菱形加载不去重，而重复成员按 VB 自己的规则是硬错误。** `:106` 的 `activeLoads.Remove` 在子树完成后执行，于是同一路径经两条兄弟分支各 `#Load` 一次时，第二次 `Add` 成功、文件被**再次解析成一棵新树**加入 `loadedTrees`（`:107`）——顶层语句执行两次，其成员成为 script class 的**两个部分**。按 `vblang\spec\general-concepts.md:13` 逐字「it is invalid for declarations to introduce identically named entities of the same kind into the same declaration context」，这是硬错误，且错误消息会指向被加载文件内部，看不出根因是「我的两个文件都 `#Load` 了同一个 helper」。C# 按**解析后路径**去重：`!loadedSyntaxTreeMapBuilder.ContainsKey(resolvedFilePath)`（`SyntaxAndDeclarationManager.cs:236`），命中则走 else 分支（`:270-275`，注释逐字「The path resolved, but we've seen this file before, so don't attempt to load it again.」），**环因此静默终止**——C# 官方测试 `LoadDirectiveTests.Cycles`（`:152-193`）对自环与三节点环都断言树数正确且 `VerifyDiagnostics()` 为空。提案 Alternatives 说去重「会与『同一文件在不同相对路径下被加载』产生歧义」——**这句也站不住**：去重发生在 `ResolveReference` 之后（C# 同样），比较的是解析后的路径，不是源码里书写的字符串；而 `activeLoads` 已经在用 `OrdinalIgnoreCase`（`:188`），做同一件事的成本极低。

这里有一条提案与两条路径都同意的边界：**改去重是行为变更**。旧文本内联实现下菱形同样重复执行，所以按路径去重会改变既有 `.vbx` 的行为，必须走行为变更流程、并先做产品侧影响面统计。但无论裁定如何，**spec 必须把这条分叉写进 C# 对照表，并补一条菱形测试**——我们复核 `Scripting\VisualBasicTest`，`Diamond`/`twice`/`Duplicate` 的命中项全部属于 `#R`、NuGet 包与 `Imports`，`#Load` 菱形**零覆盖**，与提案 Unresolved 1 一致。

### 权衡四：展开点与 `Imports` 边界——C# 实现推翻提案的论证方向

**展开点的归属被提案说反了方向。** 提案 Alternatives 说「C# 同样把 `#load` 展开放在 `SyntaxAndDeclarationManager` 这一**编译对象侧**、由 `SourceReferenceResolver` 驱动」，并用它论证「把树集合的构造权交给宿主，才能让 REPL/脚本/LSP 各按自己的文件系统抽象工作」。前半句是事实，后半句不成立：`CSharpCompilation` 持有 `SyntaxAndDeclarationManager` 字段（`CSharpCompilation.cs:121`），在构造时以 `options.SourceReferenceResolver` 创建（`:516`）——**C# 恰恰是在编译器里展开、同时尊重宿主抽象**，靠的就是把 resolver 作为编译选项注入。而 fork 的 `CreateSubmission` 本来就把 `sourceReferenceResolver:=SourceFileResolver.Default` 写进了编译选项（`VisualBasicScriptCompiler.vb:226`）。所以「交给宿主才能让各宿主按自己的文件系统抽象工作」这个理由，在 C# 的实现里站不住。

代价是实在的：一个只调 `VisualBasicCompilation.CreateScriptCompilation` 的消费方（IDE / LSP / 测试）拿到的编译里，`#Load` **是惰性的**——被加载文件的成员不存在，且没有任何诊断说明「你没展开」；C# 的 `#load` 展开还参与编译对象的增量状态（`SyntaxAndDeclarationManager.cs:304-466` 的 `RemoveSyntaxTrees`/`ReplaceSyntaxTree` 会连带移除/更新加载树，`LoadDirectiveTests.FileThatCannotBeDecoded` 正是用换树验证「重新展开、重新报诊断」），VB 侧没有对应物，任何编辑都要重跑 `CollectLoadTrees` 并重建整个 compilation。我们**不主张 v1 改**（改它等于重做 2.0 的落地形态）；但 spec 必须把「展开是宿主义务、未展开的 `#Load` 无语义」写成显式契约，否则未来会有人把它当 bug 修一半。另记一条工具链摩擦：唯一展开点是 `Friend Shared`（`:73`），第三方工具无法复用它，只能自己重写环检测与树序规则——这与 §6「展开逻辑单点、不得各写一份」的理由只对 fork 内调用点成立。

**`Imports` 边界：提案的措辞会误导。** 提案 §5c 写「加载树的 `Imports` 属同一提交，参与跨提交 replay（累积，不是继承）」。事实是两句话：**同一提交内 per-file**——`vblang\spec\source-files-and-namespaces.md:253` 逐字「The scope of an `Imports` statement specifically does not include other `Imports` statements, nor does it include other source files.」，所以 `loaded.vbx` 的 `Imports` 只在 `loaded.vbx` 内生效，`main.vbx` 看不到；**跨提交累积**——它被 `AddPreviousSubmissionImports` 收集进**下一次提交**的 `GlobalImports`（`VisualBasicScriptCompiler.vb:134-162`，遍历 `previousSubmission.SyntaxTrees` 的每个 `root.Imports`），从下一次提交起对一切生效。提案的措辞读者很容易读成「同一提交内共享」，而事实相反。它引用的测试（`ImportsAccumulationFailureTests.vb:276-290`）也证明不了那句话：加载文件里的 `Imports System.Text` 在其自身代码中**未被使用**，断言只覆盖「可编译 + 下一次 `ContinueWith` 不抛」。

### 权衡五：C# 对照表缺了最结构化的一行，空操作数分叉不能照抄姊妹 spec

**C# 对照表缺了「引用管理器复用」这一行，而它才是「编译器级 vs 宿主级」的核心。** C# 的 `CSharpSyntaxTree` 定义了 `HasReferenceOrLoadDirectives`（`Compilers\CSharp\Portable\Syntax\CSharpSyntaxTree.cs:140-154`）：

```
if (Options.Kind == SourceCodeKind.Script)
{
    var compilationUnitRoot = GetCompilationUnitRoot();
    return compilationUnitRoot.HasReferenceDirectives || compilationUnitRoot.HasLoadDirectives;
}
```

它被用在 `CSharpCompilation.cs:971`、`:1037`、`:1113` 的 `reuseReferenceManager &= !tree.HasReferenceOrLoadDirectives;`——**C# 把 `#load` 当作可能影响引用集合的指令**（被加载树可带 `#r`），因此放弃 reference manager 复用。VB 的脏标记只看 `#R`（`VisualBasicSyntaxTree.vb:85-91`），`#Load` 完全无编译期状态。这条不是缺陷（提案 §2 已正确划界：被加载树自身的 `#R` 照常置脏），但它是跨语言工具最容易踩空的差异，必须进对照表。

**空操作数：C# 报错，VB 静默。** C# 只在 `path == null`（缺字面量）时 `continue`（`SyntaxAndDeclarationManager.cs:207-213`）；空字符串不是 null，于是走 resolver → `ResolveReference("")` 返回 null → `ERR_NoSourceFile`。官方测试逐字钉死：`#load ""` → `error CS1504: Source file '' could not be opened -- Could not find file.`（`LoadDirectiveTests.cs:19-29`）。VB 是 `String.IsNullOrEmpty(path)` → 静默 `Continue For`（`:88-90`）。这条要特别标出来，因为姊妹 spec 把 `#R ""` 的 no-op 写成「**it mirrors the C# compiler**」——该措辞对 `#R` 成立（C# 声明表构建器有同款过滤：`CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:305-308` 的 `!d.File.ContainsDiagnostics && !string.IsNullOrEmpty(d.File.ValueText)`），对 `#Load` **不成立**。`#Load` 的 spec 若沿用同一句，就写错了。

**其余对照行**：模式门控、位置门控、缺操作数、parse options（C# 沿用外部树的 `tree.Options`，`:243`）逐条对齐；树序与 C# 同构（`AppendAllSyntaxTrees` 先 `AppendAllLoadedSyntaxTrees`（`:170`）、再把自己追加到末尾（`:181`）、加载树分支递归（`:249-261`）），当前对照表里没有「树序」行，值得补一条以消除「VB 自作主张定序」的误解。`resolver` 为空：C# 报 `ERR_SourceFileReferencesNotSupported`（`:217-223`；`LoadDirectiveTests.NoSourceReferenceResolver` 断言 CS8099），VB 是 NRE——`ScriptOptions.SourceResolver` 是 `Scripting\Core` 的语言无关 API，文档明写「to be used to resolve source of scripts referenced via #load directive」（`Scripting\Core\ScriptOptions.cs:97`），同一个 API 面、同一输入、不同失败模式，是最典型的 interop 摩擦。大小写（VB 经 `MakeKeyword` 大小写不敏感）与 `IsActive`（VB 指令 trivia 无此概念，靠「禁用区不产节点」实现）都是继承自 `#R` 的既有形状，不是 `#Load` 引入的新摩擦，对照表可各加一行并标注。关于 csharplang 规范：镜像内确实**未找到** `#load` 的语义规范（全库仅 `meetings\2021\LDM-2021-03-15.md:65-66`、`LDM-2021-05-12.md:76-79` 两处旁及），提案如实记录「未找到」而不是替它编一个，这一点我们认可。

### VB 基因对照 / C# 生态对照

- **落位正确。** `#Load` 是脚本多文件组织的唯一机制，`#R` 是程序集面、两者不可互替；提案 Alternatives 已把「用 `#Load` 代替 `#R`」「让 `#Load` 进声明表」明确否掉，理由成立。VB 对扩展表面区的门槛很高（`vblang\meetings\2018\vbldm-notes-2018.06.13.md:34` 逐字「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」），`#Load` 在这条尺子下是干净的。
- **多树 script class 是延申既有机制，不是新容器。** script class 本身是 `Partial`（`spec\spec-scripting-dialect.md:48`），多 part 合并是 VB 既有语义（`vblang\spec\types.md:1127`），`SyntaxReferences.First()` 就是「取第一个 part」的普通写法。
- **两处与「不静默失败」基因反向，规范必须补偿。** 环与缺失共用 BC2001 说假话；菱形加载静默地产生同一指令的两种后果。二者都不是「文档没写」，是设计本身的代价，且修法成本极低。失败形态本身的收口方向（转诊断、给 REPL 保护）在 `#R` 已有先例（缺库是 BC2017 进 `Compilation.GetDiagnostics()`），同族指令不应有两种失败形态。
- **宽松/晚绑定基因无摩擦。** `#Load` 不碰绑定语义；各树内的 `Option` 语句仍按 VB「per file」规则各自生效（`vblang\meetings\2018\vbldm-notes-2018.02.07.md:38` 逐字「Options are set per file, not per class」）。
- **破坏既有代码审计干净，但三处收口遗留。** `#Load` 在 Regular 编译里本来就是错误（`ParseConditional.vb:88-89` 的 `ParseBadDirective`），BC36967 只是把它明确化；`Load` 作为普通标识符不受影响（`LoadKeyword` 只在 `#` 之后经 `PossibleKeywordKind` 分派，`Parser.vb:1907-1915` 带 `Case Else → Return False`）。遗留三处：PublicAPI 缺项、死资源、`AddSyntaxTrees` 脚本树守卫缺失（提案如实记录了但没进 Drawbacks/Unresolved）。
- **C# 生态面整体低风险，但跨语言移植有一个真坑。** 对照表补齐后，模式门控/位置门控/树序/parse options 全部对齐；唯一的语义分叉是菱形——同一份 `.csx` 内容（C# 里合法且只执行一次）搬到 `.vbx` 变成执行两次 + 可能重复定义。M1–M8 里只有 M5 相关：`#Load` 不新增 AOT/trimming 摩擦（脚本提交本就被 `spec-scripting-dialect.md` 的 Considerations 排除在 AOT 之外）。D4 判入 P1 档①（C# scripting 已覆盖、非底层内存机制）；但能力已落地，P1 在此不改变结论。

### RESOLUTION:

1. **定性**：`#Load` 是「**编译器语法 + 宿主展开契约**」的组合，不是 VB 语言特性；提案作为「已实现机制的唯一事实基线」成立，机制随 2.0 beta 落地并在 2.16 与 NuGet 预扫描共用展开点 → 证据等级**已采纳**。语法、门控、树序、`Return` 语义**不改**（两路独立复核一致：VB 基因「附带条件支持 / 置信度高」、C# 生态「附带条件支持 / 置信度中」）。§1–§8 锚点逐一复核属实，修正以本 RESOLUTION 第 2–15 条为准。定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`；映射 `decisions.md` **M5**。
2. **U5 从 `Suspect` 升级为实锤缺项（本 RESOLUTION 为该表述的 supersede 口径）**：`LoadDirectiveTriviaSyntax` / `LoadKeyword` / `GetLoadDirectives` 三组公开 API 漏登 `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`（零命中；对照 `#R` 在 Shipped `:412`/`:1794-1801`/`:2996`，`#!` 整组在 Unshipped `:1-18`）。按 fork 既定做法补进 `PublicAPI.Unshipped.txt`（与 `#!` 同路径）。「补登后构建是否仍报 RS0016」**未跑构建 → `Suspect`**。
3. **死资源收口**：`SubmissionCanHaveAtMostOneSyntaxTree` 单树守卫删除后无生产引用（`VBResources.resx:129` + 13 份 xlf `:589-590`），删除或归档，spec 定稿前清零（对齐「实现阶段不留遗留问题」）。
4. **失败通道表述订正（本 RESOLUTION 为提案 Drawbacks 两条的 supersede 口径）**：① `Script.Compile()` **不抛**——它把 `CompilationErrorException` 转成诊断返回（`Script.cs:231-232`、`:332-346`），抛的是 `RunAsync`（`CommandLineRunner.cs:259-263` 捕获）；② `/check` 走同一 `Compile()`（`:245-252`），缺失/环场景下退出码 0/1 契约照样成立（`spec-vbi-script-diag-mode.md:72-77`）；③ REPL 先 `Compile()` 再 `RunAsync`（`:370`、`:378`），有错误即丢本次提交、**会话继续**——「打错文件名会结束会话」不成立。准确的残余差异只写一条：**失败提交不存在 `Compilation` 对象**（C# 有带诊断的编译对象可查，`CSharpCompilation.cs:3135-3138`）。
5. **`ReadText` 读失败转诊断**：`CollectLoadTrees` 的 `resolver.ReadText`（`VisualBasicScriptCompiler.vb:98`）无捕获，按 C# 形状（`SyntaxAndDeclarationManager.cs:238-268` 的 `ToFileReadDiagnostics`）包成诊断，锚 `directive.File.GetLocation()`；补一条「存在但不可解码」的测试（对齐 C# `FileThatCannotBeDecoded` 期望 CS2015 的形状）。
6. **`SourceResolver` 为 null 产诊断（本 RESOLUTION 为该表述的 supersede 口径）**：`VisualBasicScriptCompiler.vb:85`、`:93`、`:190` 三处裸调无守卫（对照 `NuGetRestoreCoordinator.vb:209-213` 有守卫，`ScriptOptions.cs:162` 只有 `Debug.Assert`），spec 定为**产诊断**（镜像 C# 的 `ERR_SourceFileReferencesNotSupported`，`SyntaxAndDeclarationManager.cs:217-223`），不采用「API 契约要求非 null、违者 NRE」；同时守 `:190` 的 `NormalizePath`。
7. **U2 环与缺失分码（本 RESOLUTION 为该表述的 supersede 口径）**：`:94` 的 `OrElse` 拆成两个分支，环报新码/新消息（含 `#Load` 语境，如「cyclic #Load reference」），保留锚在 `#Load` 字符串 token 上。提案 Alternatives「分码需要额外状态」**不成立**（条件已在 `OrElse` 两侧，零额外状态），该理由作废。
8. **U1 菱形去重（本 RESOLUTION 为该表述的 supersede 口径）**：**倾向按解析后路径去重**（对齐 `SyntaxAndDeclarationManager.cs:236`、`:270-275`，复用已在位的 `OrdinalIgnoreCase`，环检测从「栈」退化为「已见集合」是净简化）；但这是**行为变更**（旧文本内联同样重复），必须走行为变更流程并先做产品侧影响面统计。**最低可接受结果**：spec 显式写出「VB 不去重、C# 去重」分叉并进 C# 对照表，补菱形 / 环 / 大小写三组测试（当前 `#Load` 菱形零覆盖）。提案 Alternatives「会与不同相对路径产生歧义」**不成立**（比较在 `ResolveReference` 之后），该理由作废。
9. **C# 对照表补齐（本 RESOLUTION 为该表述的 supersede 口径）**：补六行——引用管理器复用（`CSharpSyntaxTree.cs:140-154` 的 `HasReferenceOrLoadDirectives` 用于 `CSharpCompilation.cs:971`/`:1037`/`:1113`）、空操作数（C# `#load ""` = CS1504，`LoadDirectiveTests.cs:19-29`；VB 静默 `:88-90`）、读取失败通道（C# 转诊断 `:238-268`；VB 无 try/catch `:98`）、`resolver` 为空（C# CS8099 `:217-223`；VB NRE）、树序（C# `:170`/`:181`/`:249-261` 同构）、大小写与 `IsActive`（继承 `#R` 形状，非 `#Load` 特有）。`#Load` 的 spec **不得**沿用姊妹 spec 对 `#R ""` 的「mirrors the C# compiler」措辞——该措辞对 `#R` 成立（C# `DeclarationTreeBuilder.cs:305-308` 同款过滤），对 `#Load` 不成立。
10. **`Imports` 边界钉死（本 RESOLUTION 为提案 §5c 的 supersede 口径）**：按「**同提交内 per-file**（`source-files-and-namespaces.md:253`）、**跨提交累积**（`AddPreviousSubmissionImports`，`VisualBasicScriptCompiler.vb:134-162`；对齐 `spec-scripting-dialect.md` 的有意偏差）」两句话写成条文，并补一条直接测试（加载文件 `Imports` 引入的名字在其自身可用、在主文件不可用、在下一次提交可用）；现有测试（`ImportsAccumulationFailureTests.vb:276-290`）证据强度不足以支撑原表述。
11. **spec 立「头部指令」类并写明显式契约**：承接 `#R` 的同一诉求，把 `#!`（位置 0）/ `#R`+`#Load`（首个 token 之前）的判据与差异理由写在一起；并显式写出「**展开是宿主义务，未展开的 `#Load` 无语义**」——只调 `CreateScriptCompilation` 的消费方得到的是惰性指令（C# 在编译对象内展开，`CSharpCompilation.cs:121`、`:516`；VB 在宿主，`CollectLoadTrees`）。
12. **补三条 spec 交付物**：树序直接测试（U3，两文件各 `Print` 一行比对输出序，现有测试只断言返回值）、编译器级语法门控测试（U6，BC36967 / BC37002 / BC30217 目前只有宿主层覆盖，C# 侧有 `LoadDirectiveTests.cs`）、禁用区内的 `#Load` 运行验证（U7，静态结论认同：禁用文本不产指令节点，`Scanner\Directives.vb:552-643`；对齐 `spec-shebang-directive.md` 的 `#!` 条文）。U8（`#R` 与 `#Load` 头部相对顺序）写明「**有意无约束**」。
13. **证据锚点订正（本 RESOLUTION 为该表述的 supersede 口径）**：§5a 的 `LexicalSortKey` 改指 `Compilers\VisualBasic\Portable\Symbols\LexicalSortKey.vb:154-178`（现指 C# 文件）；`issue-vbx-load-span-shift.md:89`「改守『仅脚本树』」与 `VisualBasicCompilation.vb:1024-1034` 不符，按代码订正；`upstream-merge.md` 2.5 与 `proposal-reference-directive.md:42` 的 `#Load` 位置订正为 `Scripting\VisualBasic\`（现记 `Scripting\Core\`）；`proposal-shebang-directive.md:96-104` 的 `#Load` 行锚点已过时（`LoadReferencedTrees @ :56` / `ParseConditional.vb:471` → 现为 `CollectLoadTrees @ VisualBasicScriptCompiler.vb:73` / `ParseLoadDirective @ ParseConditional.vb:474`）。
14. **记录卫生**：`proposals\README.md` 的 Active 表补登本提案、`meetings\README.md` 补登本条纪要；`#Load` 相关错误码（BC36967 / BC37002 / BC2001）与宿主侧改动入 `upstream-merge.md`；`Scripting\VisualBasicTest` 补一条 `/check` + 缺失 `#Load` 用例（当前零覆盖，纯内存无副作用）。
15. **Unresolved 纪律**：8 条 Unresolved 在 spec 定稿时全部落进正文 / Alternatives / Considerations，不得以 Unresolved 形态随 spec 发布（`spec\README.md:114`——已完成特性此节只写 `None.`）。

### Implication:

- **定性面**：本提案是**记录型**——把 `#Load` 的既有机制钉成 spec 基线。会议结论是「骨架成立、边界需收口」，不是「方案待定」；提案正文不改，修正全部以本 RESOLUTION 为准。
- **规范面**：spec 新增/加强五块——头部指令分类与「展开是宿主义务」的显式契约、`Imports` 边界的 per-file / 跨提交两句话、C# 对照表六行（含引用管理器复用与空操作数分叉）、失败通道的准确表述（`Compile()` 转诊断 + 无 `Compilation` 对象的残余差异）、环与菱形的诊断与去重口径。
- **实现面**：三处小改（`ReadText` 转诊断、`SourceResolver` 守卫产诊断、环与缺失分码）+ 一处行为变更待裁定（菱形去重）；PublicAPI 补登与死资源清理是构建门与卫生项。
- **回归面**：现有宿主层测试覆盖 span 零漂移、诊断锚点、嵌套、环、跨树引用；缺的是树序顺序断言、菱形/大小写、编译器级语法门控、禁用区、`/check` + 缺失 `#Load`——全部为纯内存测试。
- **互操作面**：菱形分叉是跨语言移植的真坑（C# 合法且只执行一次 → VB 执行两次）；`HasReferenceOrLoadDirectives` 是 C# 把 `#load` 当引用集合信号的证据，补进对照表防跨语言工具误判。
- **记录面**：`proposals\README.md` 登记、`upstream-merge.md` 2.5 位置订正、`issue-vbx-load-span-shift.md:89` 与 `#!` 档锚点订正。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTION`（待定）：**菱形去重是否落地**——按路径去重对齐 C#，但属行为变更；需产品侧统计「是否有真实 `.vbx` 脚本依赖重复执行」，再走行为变更流程裁定。
- `OPEN QUESTION`：**环的新诊断码位**——新增 VB 错误码还是复用 BC2001 换消息；C# 侧两者不同码（缺文件 `ERR_NoSourceFile`，环不报错），分码不算背离。
- `OPEN QUESTION`（`Suspect`）：**`ScriptOptions.SourceResolver` 与编译选项 `sourceReferenceResolver` 不一致**——`CreateSubmission` 硬编码 `SourceFileResolver.Default`（`VisualBasicScriptCompiler.vb:226`），而展开用 `script.Options.SourceResolver`（`:85`）；调用方 `WithSourceResolver(custom)` 后 `compilation.Options.SourceReferenceResolver` 仍是默认值。C# host 是否透传，本仓无源码可对照。
- `OPEN QUESTION`（`Suspect`）：**补登 PublicAPI 后构建是否仍需其它改动**（RS0016 是否真会报）——未跑构建。
- `OPEN QUESTION`：**第三方工具可及性**——唯一展开点 `CollectLoadTrees` 是 `Friend Shared`，外部工具只能自己重写环检测与树序规则；是否为工具链开公开入口属更大决策。
- `TODO`：补 `PublicAPI.Unshipped.txt` 三组条目；清理 `SubmissionCanHaveAtMostOneSyntaxTree` 资源与 13 份 xlf；`ReadText` 转诊断；`SourceResolver` 守卫；环与缺失分码；补测试（树序 / 菱形 / 大小写 / 编译器级门控 / 禁用区 / `/check` + 缺失 `#Load`）。
- `TODO`：锚点与台账订正（`LexicalSortKey.vb:154-178`、`issue-vbx-load-span-shift.md:89`、`upstream-merge.md` 2.5、`proposal-reference-directive.md:42`、`proposal-shebang-directive.md:96-104`）；`proposals\README.md` 登记本提案、`meetings\README.md` 登记本条纪要。
- `Follow-up`：上游若日后在编译对象内真实现 `#load` 展开（C# 形状），评估是否跟随其形状并回退「宿主单点展开」的取舍（`upstream-merge.md` 的合并前评估义务）。

### 状态

- **LDM 状态：Active**。
- **三态判定：Active**——机制是 2.0 beta 已落地并已被依赖（**已采纳**），两路独立复核一致（VB 基因「附带条件支持 / 置信度高」、C# 生态「附带条件支持 / 置信度中」）；提案 §1–§8 锚点逐一复核，绝大多数属实。会议裁定的修正集中在：失败通道表述订正（`Compile()` 转诊断、`/check` 契约成立、REPL 不崩会话）、环与缺失分码、菱形去重口径与测试、`ReadText` / `SourceResolver` 两处守卫、C# 对照表六行、`Imports` 边界、头部指令类与「展开是宿主义务」、PublicAPI 与死资源、锚点与台账订正——全部为 spec 化可交付的收口，不构成设计阻塞。**剩余未定项**（菱形去重落地、环的新码位、resolver 与编译选项一致性、工具链入口）均已带证据锚与收口动作。下一阶段是 **spec 化**（`InternalDevDocs\spec\spec-load-directive.md`）——本单元是记录型，能力已在册；RESOLUTION 中的实现项（两处守卫、分码、测试、台账）另立跟踪。
- **supersede 口径清单**：标注「supersede 口径」的条目为 R2（U5 表述）、R4（Drawbacks 的失败通道两条）、R6（`SourceResolver` 的 NRE 备选）、R7（「分码需要额外状态」的理由）、R8（「去重会歧义」的理由）、R9（C# 对照表）、R10（§5c 的 `Imports` 措辞）、R13（锚点订正）——提案正文不改，这些表述以本条纪要为准。
- **独立五维评审为不入库工作材料**（git-ignored），仅供主持人/规划内部使用，不混入官方会议记录。
