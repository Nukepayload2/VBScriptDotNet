# `#Load` 源文件加载指令 / Load Directive

* [x] Proposed
* [x] Prototype: Complete（fork 新增编译器指令 trivia：`Syntax.xml:9552-9558` 的 `LoadDirectiveTriviaSyntax` + `ParseConditional.vb:474-492` 的 `ParseLoadDirective`，镜像上游 `#R` 模板；宿主多树展开 `VisualBasicScriptCompiler.vb:73-111`；登账 `upstream-merge.md` 2.5）
* [x] Implementation: Complete（随 2.0 beta 落地，`spec\README.md:36`；从文本内联改为编译器 trivia + 多树提交的修复记录在 `issues\issue-vbx-load-span-shift.md`；NuGet 预扫描复用同点已登账 `upstream-merge.md` 2.16；测试面在各节内联注明）
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`#Load "file.vbx"` 把另一个脚本文件的**源码**并入当前提交。它在语法上是**编译期指令 trivia**（`LoadDirectiveTriviaSyntax`，`SyntaxKind.LoadDirectiveTrivia = 751`；关键字 `SyntaxKind.LoadKeyword = 793`），只在 `SourceCodeKind.Script` 下合法，且必须出现在编译单元首个 token 之前；但它的**操作数解释与展开全部是宿主行为**——宿主（`Scripting\VisualBasic\VisualBasicScriptCompiler.CollectLoadTrees`）用 `ScriptOptions.SourceResolver` 把字符串解析成真实文件路径、逐文件解析为**独立语法树**（`FilePath` = 真实路径）、深度优先递归处理嵌套 `#Load`，再把「加载树在前、主树在后」的多棵语法树交给 `VisualBasicCompilation.CreateScriptCompilation`。与 `#R` 的关键差异是：`#Load` **不进 declaration table**，不是引用机制，它的变化**不触发 reference manager 重建**。

加载树与主树同属**一个 script class**、同属**一个提交**：所有树的顶层语句进**同一个 script initializer** 并按树序（加载树先）执行；`Imports` 属同一提交、跨提交时由宿主重放整条链（是**累积**，不是继承）；`Return` 在任意树都返回整个提交，typed 提交下成为 `.vbx` 退出码；诊断落在各自真实文件的真实行号上，主文件 `#Load` 之后的 span 一字不差。环检测只覆盖**当前递归栈**（`activeLoads`），因此**菱形加载不去重**；缺失文件与环共用 `ERR_FileNotFound`（BC2001）且锚在 `#Load` 的字符串 token 上；展开失败由宿主**抛 `CompilationErrorException`** 而非产出诊断。

本机制不是 fork 发明：`#R` 是上游 Roslyn scripting 的既有实现，`#Load` 在 2.0 由 fork 按同一模板移植（C# scripting 的 `#load` 与之同源）。本提案的职责是把这套已实现的语义钉成唯一事实基线，为 spec 化提供共同底座。

## Motivation
[motivation]: #motivation

- **`#Load` 是脚本「多文件组织」的唯一机制。** `.vbx` 与 REPL 没有工程文件、没有 `Imports` 之外的包含机制，把一段逻辑拆成多个文件只能靠 `#Load`。它决定了「加载文件的成员能不能被主文件调用」「两个文件的顶层语句谁先跑」「加载文件的诊断落在哪个文件」这三类用户每天都会撞到的问题，但产品归档里只有一行记录（`spec\README.md:36`「已移植 C# interactive 的 **`#Load`** 指令」）。
- **它是 2.0 的修复产物，修复过程本身就是规范。** 1.2/早期 2.0 的 `#Load` 是**脚本层纯文本内联**：把被加载文件的文本拼进主文件再解析，导致主文件 `#Load` 之后所有 span 下移 N−1 行（N = 被加载文件行数），被加载文件内的诊断连文件与行号都不对。修复为「编译器指令 trivia + 多树提交」后零漂移（`issues\issue-vbx-load-span-shift.md:39-43`、`:77-97`）。这条修复带来的一串结构性后果（多树 script class、`SyntaxReferences.First()`、binder 根按所在树）必须写清楚，否则后续设计会误以为「脚本提交只能有一棵树」。
- **它与 `#R` 常被混说，术语必须钉死。** 两者都是脚本头部的编译器指令 trivia，但 `#R` 进声明表、参与引用脏标记、由 resolver 变成程序集；`#Load` 不进声明表、不碰引用管理器、由宿主变成语法树。把 `#Load` 描述成「另一种引用」会直接推错实现方向（Alternatives 已列此反例）。
- **它与 NuGet 预扫描共用同一个展开点。** `#Load` 的嵌套 `#R "nuget:…"` 曾经从不进预扫描，导致编译绑定期 BC2017；2.16 让宿主预扫描复用编译器的同一 `CollectLoadTrees`（`upstream-merge.md` 2.16、`NuGetRestoreCoordinator.vb:226`）。这是「展开逻辑单点、编译器与宿主不得各写一份」的规范级理由。
- **C# 侧同构但有关键分叉。** C# 的 `#load` 同样是编译器级指令 trivia + 独立树（`SyntaxAndDeclarationManager.cs:204-289`），但 C# **按路径去重**（`:236`）而 VB fork **按递归栈判环**——菱形加载在 C# 只加载一次、在 VB 加载两次。这条分叉必须写清楚（Unresolved 1）。

## Detailed design
[design]: #detailed-design

> 证据等级标注：本节每个关键设计点后标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑）。锚点均在本 fork 源码中逐条复核，故为**已检查**；已随 2.0 beta 落地或已由修复记录在册的为**已采纳**。本阶段未运行测试，故不出现「已运行」/「有结果支撑」；测试存在性在各节内联注明（集中在 §5 与 §7）。

### 1. 语法：`#Load "字符串"` 是编译期指令 trivia

指令节点由 `Syntax.xml` 定义：`LoadDirectiveTriviaSyntax` 派生自 `DirectiveTriviaSyntax`，子节点只有 `LoadKeyword` 与 `File`（类型 `StringLiteralToken`）（`Syntax\Syntax.xml:9552-9558`）——**没有 `EndOfDirectiveToken`**，行尾终结符由扫描器在指令之外消费（`Scanner\Directives.vb:77` 的 `ConsumeStatementTerminatorAfterDirective`）。这一点与 C# 的 `LoadDirectiveTriviaSyntax`（含 `EndOfDirectiveToken`，`Compilers\CSharp\Portable\Syntax\Syntax.xml:5208-5209`）不同，是本 fork 跟随 `#R` 的 VB 形状。枚举值 `LoadDirectiveTrivia = 751`（`Syntax\SyntaxKind.vb:3048`）、`LoadKeyword = 793`（`:3216`），均为**追加**，不位移既有值（`issue-vbx-load-span-shift.md:83`）。

解析派发在条件编译指令处：`#` 之后是标识符时按 `PossibleKeywordKind` 分派，`SyntaxKind.LoadKeyword` → `ParseLoadDirective(hashToken, isFollowingToken)`（`Parser\ParseConditional.vb:85-86`）。`ParseConditionalCompilationStatement` 的签名带 `isFollowingToken` 参数（`:22`），由扫描器在扫描该 token 的 leading trivia 时判定并传入（`Scanner\Directives.vb:43`、`:76`）。

`ParseLoadDirective`（`ParseConditional.vb:474-492`）的行为，与 `ParseReferenceDirective`（`:454-472`）逐行同构：

- `Load` 先被扫成 `IdentifierToken`，再经 `_scanner.MakeKeyword(identifier)` 转成 `LoadKeyword`（`:478-480`）。VB 标识符/关键字匹配大小写不敏感，故 `#load` 与 `#Load` 同义。
- **模式门控（与位置门控互斥）**：`Not IsScript` 时给关键字挂 `ERR_LoadDirectiveOnlyAllowedInScripts`（`:482-483`；`Errors\Errors.vb:1606` = **BC36967**，消息「#Load is only allowed in scripts」，`VBResources.resx:4397-4399`）。
- **位置门控**：`ElseIf isFollowingToken` 时挂 `ERR_PPLoadFollowsToken`（`:484-485`；`Errors\Errors.vb:1632` = **BC37002**，消息「Cannot use #Load after first token in file」，`VBResources.resx:4400-4402`）。判据是「该指令所属 trivia 不属于树的起始」——`_directiveIsFollowingToken = _leadingTriviaStartOffset > 0`（`Scanner\Directives.vb:41-43`）。两个门是 `If/ElseIf`，同一关键字上**只挂一枚**诊断。
- **操作数必须是字符串字面量**：`VerifyExpectedToken(SyntaxKind.StringLiteralToken, file)`（`:488-489`）。缺字面量时 `VerifyExpectedToken` 走 `HandleUnexpectedToken`（`Parser\ParseVerify.vb:160-176`），`StringLiteralToken` 映射到 `ERR_ExpectedStringLiteral`（`ParseVerify.vb:132-133`；`Errors\Errors.vb:244` = **BC30217**）。

位置约束在语法树 API 上被固化：`CompilationUnitSyntax.GetLoadDirectives` 只从编译单元的**首个 token** 的 leading trivia 取指令（`Syntax\CompilationUnitSyntax.vb:33-41`，注释「#Load directives are always on the first token of the compilation unit」；泛型 `GetDirectives(Of T)` 只遍历 token 的 `LeadingTrivia`，`Compilers\Core\Portable\Syntax\SyntaxNodeOrToken.cs:849-867`）。因此 `#Load` 与 `#R`、`#!` 一样，是脚本文件头部机制，不能出现在语句之后。

**条件编译区域**：`#Load` 位于**被禁用**的 `#If` 分支内时，其文本被扫描器归入 disabled text trivia（`Scanner\Directives.vb:552-643` 的 `SkipConditionalCompilationSection` → `GetDisabledTextAt`），不成为 `LoadDirectiveTriviaSyntax` 节点，故 `GetLoadDirectives` 不会返回它、宿主也不会展开它。VB 的指令 trivia 没有 `IsActive` 概念（`Syntax.xml` 中 `IsActive` 出现 0 次；C# 为 21 处），因此这一点**不靠 IsActive 标记，而靠「禁用区不产指令节点」实现**。

**证据等级：已检查。** 错误码与消息文案均逐条核实；条件编译一条为静态推断（未运行验证，见 Unresolved 7）。`LoadDirectiveTrivia` / `LoadKeyword` 的注册点：`Scanner\KeywordTable.vb:137`、`Syntax\SyntaxKindFacts.vb:24/72/280/364/540/811/847`、`Syntax\SyntaxFacts.vb:605`、`Parser\Parser.vb:5760`、`Syntax\InternalSyntax\SyntaxNodeExtensions.vb:399/434`、`Syntax\InternalSyntax\SyntaxToken.vb:400`、`Syntax\SyntaxNodeFactories.vb:663`、`Syntax\InternalSyntax\SyntaxNodeFactories.vb:349`、`Syntax\SyntaxNormalizer.vb:379/1220-1223`。

### 2. 不进声明表：与 `#R` 的关键差异

`#Load` 与 `#R` 共用「编译器指令 trivia + 首个 token 之前」的语法形状，但在**编译器消费路径上完全不同**：

| 维度 | `#R` | `#Load` |
|---|---|---|
| 进 declaration table | 是（`DeclarationTreeBuilder.vb:101-113`、`:198`） | **否**（无任何调用点） |
| 影响引用脏标记 | 是（`VisualBasicSyntaxTree.vb:85-91` 的 `HasReferenceDirectives`） | **否** |
| 编译器侧产物 | `ReferenceDirective` → resolver → `MetadataReference` | 无（编译器只把它当 trivia 保留） |
| 展开者 | 编译器 Core 的引用管理器 + 宿主 resolver | **宿主**（`CollectLoadTrees`） |
| 展开结果 | 程序集引用（N 个） | 语法树（N 棵） |

证据：

- `DeclarationTreeBuilder` 只为 `#R` 构造 `ReferenceDirective`（`Declarations\DeclarationTreeBuilder.vb:101-113`），且在**非 Regular 树**分支才调用（`:198`），Regular 分支固定 `ImmutableArray(Of ReferenceDirective).Empty`（`:201`）。全仓 `GetLoadDirectives` 的 VB 调用点只有两处：宿主 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:86` 与测试 `Scripting\VisualBasicTest\ScriptTests.vb:737`——**编译器内部零调用**。
- `VisualBasicSyntaxTree.HasReferenceDirectives` 判据是 `Options.Kind = Script AndAlso GetCompilationUnitRoot().GetReferenceDirectives().Count > 0`（`Syntax\VisualBasicSyntaxTree.vb:85-91`）——只问 `#R`，不问 `#Load`。
- 引用脏标记因此只由 `#R` 累积：`AddSyntaxTreeToDeclarationMapAndTable` 的 `referenceDirectivesChanged = referenceDirectivesChanged OrElse tree.HasReferenceDirectives`（`Compilation\VisualBasicCompilation.vb:1060`）、`RemoveSyntaxTreeFromDeclarationMapAndTable` 同式（`:1132`）、`RemoveAllSyntaxTrees` 以 `_declarationTable.ReferenceDirectives.Any()` 收尾（`:1140`），最终决定 `UpdateSyntaxTrees(... reuseReferenceManager:=Not referenceDirectivesChanged)`（`:571`）。

**结论**：`#Load` 指令**本身**不进声明表、不产生引用脏标记，故「增删一条 `#Load`」不触发 reference manager 重建。这是有意的——`#Load` 只改变「有哪些语法树」，而语法树的增删本身走 `AddSyntaxTrees`/`RemoveSyntaxTrees` 的声明表与 `SyntaxTreeOrdinalMap` 更新路径，与引用集合无关。**注意边界**：这说的是 `#Load` 指令本身；被加载**树内部**若含 `#R`，该树经 `AddSyntaxTrees` 加入时其 `HasReferenceDirectives` 照常为真、照常置脏（`VisualBasicCompilation.vb:1060`），引用管理器仍会重建。

**证据等级：已检查。**

### 3. 宿主展开：`CollectLoadTrees` 的完整语义

展开的唯一点是 `VisualBasicScriptCompiler.CollectLoadTrees`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:73-111`），签名把 `activeLoads`（环检测集合）与 `loadedTrees`（输出累加器）显式传出，以便宿主预扫描复用（§6）：

| 语义 | 实现 | 锚点 |
|---|---|---|
| 取本树指令 | `root.GetLoadDirectives()`（只取首个 token 的 trivia） | `:86` |
| 空操作数 | `String.IsNullOrEmpty(path)` → **静默 `Continue For`**（不报错） | `:88-90` |
| 相对路径基准 | `baseFilePath = If(String.IsNullOrEmpty(tree.FilePath), Nothing, tree.FilePath)`，再 `resolver.ResolveReference(path, baseFilePath)`——**按引用树的 `FilePath` 解析** | `:92-93` |
| 缺失 / 环 | `resolvedPath Is Nothing OrElse Not activeLoads.Add(resolvedPath)` → 返回 `ERR_FileNotFound`（BC2001，`Errors.vb:37`），`WithLocation(directive.File.GetLocation())`——**锚在 `#Load` 的字符串 token 上** | `:94-95` |
| 读取与建树 | `resolver.ReadText(resolvedPath)` → `SyntaxFactory.ParseSyntaxTree(loadedText, parseOptions, resolvedPath)`——**独立树、`FilePath` = 真实路径、沿用同一 parse options** | `:98-99` |
| 深度优先 | 先递归子树（`:102`），子诊断原样上抛（`:103-105`），再 `activeLoads.Remove`（`:106`）并 `loadedTrees.Add`（`:107`）——**父树在自己之后加入，故执行序为「最深者先」** | `:101-107` |
| 环检测范围 | `activeLoads` 只含**当前递归栈**（入栈 `:94`、出栈 `:106`） | `:94`、`:106` |
| 主路径预置 | `CreateSubmission` 先把主树规范化路径放进 `activeLoads`（`OrdinalIgnoreCase`），使「`#Load` 自己」也判环 | `:188-194` |
| 大小写 | `activeLoads` 用 `StringComparer.OrdinalIgnoreCase` | `:188` |

**菱形加载不去重（已检查）**：`activeLoads.Remove(resolvedPath)` 在子树完成后执行（`:106`），因此同一路径被两条不同分支各 `#Load` 一次时，第二次 `activeLoads.Add` 成功、文件被**再次解析成一棵新树**加入 `loadedTrees`。这与 C# 的路径去重（见 Alternatives 的「与 C# 的对照」）分叉，后果是该文件顶层语句执行两次、其成员声明成为 script class 的**两个部分**（可能触发重复定义诊断）。无测试覆盖（Unresolved 1）。

**失败不是诊断而是异常**：`CreateSubmission` 拿到非空 `loadDiagnostic` 后调 `ThrowLoadDirectiveError`（`:195-198`），后者抛 `CompilationErrorException(diagnostic.GetMessage(), ImmutableArray.Create(diagnostic))`（`:62-64`）。因此缺失文件/环在**编译期**表现为异常，由宿主捕获（`CommandLineRunner.cs` 的 `RunScriptAsync` catch 后 `ReportDiagnostics` 并返回 `CommonCompiler.Failed`，`:259-263`）。

**`SourceResolver` 无 null 守卫**：`:85` 取 `options.SourceResolver` 后 `:93` 直接调用 `resolver.ResolveReference`。`ScriptOptions.Default` 用的是 `SourceFileResolver.Default`（`Scripting\Core\ScriptOptions.cs:31`），构造函数的 `Debug.Assert(sourceResolver != null)`（`:162`）只在 Debug 生效，故 API 调用方显式传 null 时，只要脚本含 `#Load` 就会 NRE（无 `#Load` 时循环体不执行、不触发）。对照：宿主预扫描在同一位置有显式守卫（`NuGetRestoreCoordinator.vb:209-213`）。见 Unresolved 4。

**调用点与树序**：`CreateSubmission` 先解析主树（`:183`），再 `CollectLoadTrees` 填充 `trees`（`:195`），最后 `trees.Add(tree)` 把**主树追加到末尾**（`:199`）——加载树在前、主树在后，与旧文本内联的执行序一致（`:185-186` 注释）。这份树列表直接进 `VisualBasicCompilation.CreateScriptCompilation`（`:208-232`）。

**证据等级：已检查。**

### 4. 多树编译：提交可由多棵树组成

`#Load` 的展开结果是一份**多树提交**，这要求编译器侧五处从「单树假设」放开：

- **`CreateScriptCompilation` 双载**：单树转发（`Compilation\VisualBasicCompilation.vb:345-362`）→ 多树重载（`:368-389`，`syntaxTrees As IEnumerable(Of SyntaxTree)`），后者统一走 `Create(..., isSubmission:=True)`。单树载是纯转发，旧调用方零改动。
- **`HasSubmissionResult` 只看最后一棵树**：`Dim tree = SyntaxTrees.LastOrDefault()`（`:839-851`，注释「The result of a submission is determined by its top-level script file, which is the last tree」）。因为主树被追加在末尾，「提交结果」= 主文件的结果，`#Load` 的文件不能靠末尾表达式改结果。
- **`AddSyntaxTrees` 不再限单树**：删除了原先的 `If IsSubmission AndAlso declMap.Count > 1 Then Throw New ArgumentException(VBResources.SubmissionCanHaveAtMostOneSyntaxTree, ...)`（提交 `b60dcd3` 的 diff，位于 `:993-1046` 区间内）；通读该区间可见**没有**替换成等价的单树/脚本树守卫，保留的是「有根节点」「非编译器特殊树」「树不重复」三条既有校验（`:1024-1034`）。
- **`SourceMemberContainerTypeSymbol` 的 `Single()` → `First()`**：提交构造函数（`Symbols\Source\SourceMemberContainerTypeSymbol.vb:2731-2732`）与脚本初始化器（同文件 `:2762-2763`）都用 `SyntaxReferences.First()`，注释明写「a submission may span multiple script trees (#Load)」。`SyntaxReferences` 是各 part 的语法引用，顺序由声明表按树序排序决定（§5）。
- **跨树 binder 根**：`TopLevelCodeBinder` 的构造函数新增 `root As SyntaxNode` 参数（`Binding\TopLevelCodeBinder.vb:21-25`，注释「The root is the tree that contains the top-level code being bound; a script class may span multiple trees」），三处调用改为传**所在树**的编译单元根：`BinderFactory.vb:181`（`node.SyntaxTree.GetRoot()`）、`BinderBuilder.vb:437`（`declarationSyntax.SyntaxTree.GetRoot()`）、`Binder_Initializers.vb:123`（`syntaxTree.GetRoot()`）。这是「同一 script class 的多棵树各自有独立 binder 根」的落点，也是语义模型能对每棵树正常工作的前提。
- **编译器级语义测试**：`ScriptSemanticsTests.Errors_02`（`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:119-146`）由「两棵脚本树放进常规编译必抛 `InvalidOperationException`」改为「两棵树的符号各自正常解析」（`b60dcd3` diff）。

**证据等级：已检查。** 多树提交能力已随 2.0 落地 → **已采纳**。

### 5. 语义后果

**（a）顶层语句进同一个 script initializer，按树序执行。** script class 的字段/语句初始化器按 **part** 分组收集：`SourceNamedTypeSymbol` 遍历 `SyntaxReferences`，每个 part 的初始化器追加到 `membersBuilder`（`Symbols\Source\SourceNamedTypeSymbol.vb:186-225`）；`SyntaxReferences` 的顺序由 `DeclarationTable.CalculateMergedRoot` 按树序排序决定（`Declarations\DeclarationTable.vb:171-175` 的 `RootNamespaceLocationComparer` → `VisualBasicCompilation.CompareSourceLocations` → `LexicalSortKey.Compare`，后者**先比 `TreeOrdinal` 再比 `Position`**，`Compilers\CSharp\Portable\Symbols\LexicalSortKey.cs:98-119`）。绑定端 `Binder_Initializers.BindFieldAndPropertyInitializers` 按 `initializers(i)` 的外层顺序遍历（`Binding\Binder_Initializers.vb:100-132`），每个 part 复用同一 binder 根（`:103-104` 注释「All sibling initializers share the same parent node and tree」、`:118-127`）。因宿主把加载树放在前、主树放在后（§3），**加载树的顶层语句先执行**。

**（b）`Return` 在任意树都返回整个提交，typed 提交下成为 `.vbx` 退出码。** 顶层 `Return` 位于 script initializer 体内，`Binder.BindReturnStatement` 的禁令只拦「处于顶层脚本代码但不是初始化器」的上下文（`Binding\Binder_Statements.vb:5090-5093`），故任意树的顶层 `Return` 合法。末尾表达式的结果规则只对 **Object 提交**生效（`Binding\Binder_Initializers.vb:211-224`，判据 `:219` 的 `submissionReturnType.IsObjectType()`）。`.vbx` 走 typed 提交：`CommandLineRunner.RunScriptAsync` 用 `Script.CreateInitialScript<int>(...)`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:243`）并直接返回 `(await script.RunAsync(...)).ReturnValue`（`:256`），即 `Return 17` → 退出码 17。直接测试：`ScriptTests.TestReturnValueInLoadedFile`（`Scripting\VisualBasicTest\ScriptTests.vb:224-238`，被加载文件内容就是 `Return 17`，断言 `ReturnValue = 17`）；退出码语义的脚本级测试 `CommandLineRunnerTests.TestReturnStatementSetsExitCodeInScriptFile`（`:448-458`）与 `TestBareReturnStatementExitCodeIsZero`（`:460-470`）。

**（c）加载树的 `Imports` 属同一提交，参与跨提交 replay（累积，不是继承）。** 加载树与主树在同一 compilation 里，因此其 `CompilationUnitSyntax.Imports` 与该提交的 `GlobalImports` 一起被宿主的 `AddPreviousSubmissionImports` 收集并重放（`VisualBasicScriptCompiler.vb:134-162`，遍历 `previousSubmission.SyntaxTrees` 的每个 `root.Imports`，`:150-161`），去重用 `OrdinalIgnoreCase`（`:115`）。术语纪律：**引用沿提交链继承、`Imports` 由宿主跨提交累积**（`Script.InheritOptions` 明确不继承 imports/references，`Scripting\Core\Script.cs:133-139`）。测试：`ImportsAccumulationFailureTests.ReplayPath_LoadTreeImports_AcceptedForm_DoNotThrow`（`Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb:276-290`）断言「加载树的 `Imports System.Text` 使提交可编译，且 `ContinueWith` 不抛」；其 `RejectedForm` 姊妹测试（`:293-302`）断言坏 clause 被提交门挡住。

**（d）诊断指向真实文件/行、零 span 漂移。** 因为每棵加载树是独立 `SyntaxTree` 且 `FilePath` 为真实路径（§3），诊断天然落在各自文件的真实位置。直接测试：`ScriptTests.TestLoadDirectiveDoesNotShiftDiagnosticSpan`（`:525-548`，主文件第 3 行的 `Print(undefinedVar)` 报 BC30451 于 `main.vbx:3`，而非旧内联行为的 `3+5−1=7`）、`ScriptTests.TestLoadedFileDiagnosticsUseRealFileAndLine`（`:550-569`，加载文件内的错误报 `loaded.vbx:3`）。缺失文件与环的锚点测试：`TestMissingLoadDirectiveFileReportsAtLoadLine`（`:572-587`，锚 `main.vbx:1`）、`TestLoadDirectiveCycleReportsAtLoadLine`（`:605-623`，环在 `mid.vbx` 的 `#Load` 行检出，锚 `mid.vbx:1`）。

**（e）加载树共享提交的引用。** 所有树用同一份 `references`（`CreateSubmission` 只算一次，`:171-176`），故加载文件可直接用 `#R` 引入的类型；测试 `ScriptTests.TestLoadedFileSeesMetadataReferences`（`:626-637`）。

**证据等级：已检查。**（a）（b）（c）为 2.0 已落地能力 → **已采纳**；（b）（d）有直接测试锁定。

### 6. 与 NuGet 预扫描共用展开点

`CollectLoadTrees` 由 `Private Shared LoadReferencedTrees` 提为 **`Friend Shared`** 的单一展开点，唯一目的是让宿主预扫描不能与编译器漂移（`:66-72` 的 XML doc、`upstream-merge.md` 2.16）。宿主侧调用点是 `NuGetRestoreCoordinator.ScanSubmission`：解析主树 → 调 `VisualBasicScriptCompiler.CollectLoadTrees(tree, parseOptions, options, activeLoads, loadedTrees)`（`Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb:226`）→ 主树与所有可达树逐棵扫 `#R` 分类。展开失败（缺文件/环）**不阻塞预扫描**——`CollectLoadTrees` 的返回值被忽略，留给编译器在编译期报 `ERR_FileNotFound`（`:224-226` 注释）。

与 §3 的差异：预扫描在调用前有 `resolver Is Nothing` 的显式守卫（`:209-213`，注释「No source resolver: nothing to expand #Load against, so scan only the submitted tree」），此时只扫提交文本、不展开。这是 `CollectLoadTrees` 唯一被守卫的调用方，编译器侧没有。

**证据等级：已检查。** 修复已登账 `upstream-merge.md` 2.16 → **已采纳**。

### 7. 可运行示例

`loaded.vbx`（被加载文件）：

```vbnet
' loaded.vbx —— 独立语法树，FilePath = 真实路径（诊断因此落在本文件真实行号）
Imports System.Text                          ' 属同一提交；跨提交时由宿主 replay（§5c）

Dim greeting As String = "hello"             ' script class 的字段，主文件可见
Dim sb As New StringBuilder("loaded")        ' 字段初始化器按树序先于主文件顶层语句

Function LoadedValue() As Integer            ' script class 的成员，主文件可直接调用
    Return 42
End Function

Print("loaded.vbx: " & sb.ToString())        ' 顶层语句：进同一 script initializer，按树序先执行
```

`main.vbx`（主文件）：

```vbnet
' main.vbx —— vbi main.vbx
#Load "loaded.vbx"                           ' 编译器指令 trivia；操作数由宿主解析为真实文件
Print("main.vbx: " & LoadedValue() & " " & greeting)
Return 0                                     ' typed 提交：只有显式 Return 才是退出码（§5b）
```

运行 `vbi main.vbx`，输出：

```text
loaded.vbx: loaded
main.vbx: 42 hello
```

退出码 0。示例覆盖 §1（指令 trivia）、§3（独立树、相对路径按主树 `FilePath` 解析）、§5a（加载树顶层语句先执行）、§5b（typed 提交的 `Return`）、§5c（加载树的 `Imports` 属同一提交）。

REPL 里同一机制逐条提交（`#Load` 在交互模式可用，且是 `#help` 明列的脚本指令之一）。下例的 `loaded.vbx` 是**另一份最小文件**（只含 `Function LoadedValue() As Integer`，无顶层语句），故输出只有 `42`：

```text
> #Load "loaded.vbx"
> ? LoadedValue()
42
```

REPL `#help` 的指令清单把 `#Load` 与 `#R` 并列（`Scripting\Core\ScriptingResources.resx:180-183`，「`#Load         Load specified script file and execute it, e.g. #Load "myScript.vbx".`」）——这是「`#Load` 是脚本/REPL 的一等指令、不是内部技巧」的产品级记录。

**证据等级：已检查。** 等价测试用例存在（未运行）：`CommandLineRunnerTests.TestLoadDirectiveInScriptFile`（`:407-421`，脚本文件路径，退出码 0）、`TestLoadDirectiveInInteractive`（`:388-405`，REPL 路径）、`ScriptTests.TestNestedLoadDirective`（`:590-602`，嵌套 + 返回值 8）、`TestLoadDirectiveAtFirstTokenOfLoadedFileIsLegal`（`:681-694`，被加载文件首行 `#Load` 合法）。

### 8. 与其它单元的分工

本提案只写 **`#Load` 指令本体语义**：语法与两处门控（§1）、不进声明表（§2）、宿主展开（§3）、多树编译（§4）、语义后果（§5）、与预扫描的共用（§6）。以下不在本文重复：

- **`#R`** —— `proposals\proposal-reference-directive.md`（Active）；`#R "nuget:…"` / `#R "project:…"` 前缀各归邻域提案。
- **`#!` shebang** —— `proposals\proposal-shebang-directive.md` / `spec\spec-shebang-directive.md`。
- **脚本声明/提交模型**（script class、`PreviousScriptCompilation`、跨提交可见性、`Imports` 累积的宿主机制）—— `proposals\proposal-scripting-dialect.md`（本提案 §5a/§5c 只写 `#Load` 引入的差异面）。
- **1.2 指令系统全表**（`#help`、`/version`、`@vbi.rsp` 等）—— `proposals\vbx-1.2-beta\proposal-repl-directives.md`；该档明确 `#Load` **不属于 1.2**（`:30`）。
- **NuGet restore 驱动环与诊断锚定** —— `proposals\proposal-vbi-nuget-reference.md`（本提案 §6 只写「共用展开点」这一事实）。

## Drawbacks
[drawbacks]: #drawbacks

- **菱形加载会重复加载同一文件。** 环检测只看当前递归栈（§3），两条分支加载同一文件时该文件被解析两次、顶层语句执行两次、成员成为 script class 的两个 part（可能触发重复定义诊断）。C# 按路径去重（见 Alternatives 的「与 C# 的对照」），VB 的行为是分叉而非等价。
- **缺失与环共用同一错误码与消息。** 两者都报 `ERR_FileNotFound`（BC2001，「File not found」），用户看到「文件找不到」但真实原因可能是循环引用（§3）。诊断可读性有损。
- **展开失败是异常而非诊断。** `CompilationErrorException`（§3）让 `Script.Compile()` 的调用方必须 try/catch；`/check` 路径下缺失文件不会像普通编译错误那样只产诊断列表。
- **`#Load` 的路径解析依赖宿主环境。** 相对路径以**引用树的 `FilePath`** 为基准（§3），因此主文件的路径决定所有嵌套 `#Load` 的解析结果；脚本从别的目录启动或移动位置，解析结果会变。
- **`#Load` 不受引用脏标记保护。** 由于它不进声明表（§2），`#Load` 集合变化不会让 reference manager 重建——这在多树语义下是正确且必要的，但也意味着「换一份被加载文件、引用集合跟着变」这类场景完全由调用方重新构造 compilation 承担。
- **脚本方言差异面。** 加载树与主树共享一个 script class，意味着「哪个文件的顶层 `Dim` 是字段」「`Return` 在哪个文件生效」都取决于宿主给出的树序，而不是文件内的书写位置——这与普通 VB 的「一个文件一个编译单元」直觉不同。

## Alternatives
[alternatives]: #alternatives

- **保留 1.2 的文本内联（`ExpandLoadDirectives`）。** 未采用：它整体漂移主文件 span、让被加载文件的诊断连文件与行号都不对（`issue-vbx-load-span-shift.md:39-43`）。与 `proposal-shebang-directive.md` 对「宿主剥行」的否决同构。
- **让 `#Load` 进声明表、当作「源文件引用」处理。** 未采用：`#Load` 的产物是语法树而非程序集引用，走声明表会把「有哪些树」和「有哪些引用」两条正交的状态耦合成一个脏标记（§2）；多树提交的树集合由宿主在 `CreateScriptCompilation` 时给定，是更简单的模型。
- **在宿主侧去重菱形加载（按解析后路径）。** 未采用（当前实现）：会与「同一文件在不同相对路径下被加载」产生歧义，且环检测已经覆盖了真正的无限递归。是否应改成 C# 的路径去重见 Unresolved 1。
- **把缺失文件与环分成两个诊断码。** 未采用（当前实现）：两者都源自「`resolver.ResolveReference` 之后这个路径不可用」，共用 BC2001 使实现最简；分码需要额外状态。见 Unresolved 2。
- **把 `CollectLoadTrees` 放进编译器（让编译器自己展开 `#Load`）。** 未采用：`SourceReferenceResolver` 在编译器选项里存在（`CreateSubmission` 把 `sourceReferenceResolver:=SourceFileResolver.Default` 写进编译选项，`VisualBasicScriptCompiler.vb:226`），但「哪些树参与提交」是宿主决定的事（C# 同样把 `#load` 展开放在 `SyntaxAndDeclarationManager` 这一编译对象侧、由 `SourceReferenceResolver` 驱动）；把树集合的构造权交给宿主，才能让 REPL/脚本/LSP 各按自己的文件系统抽象工作。
- **用 `#Load` 代替 `#R` 做程序集引用。** 不成立：`#Load` 加载的是源文件并并入同一编译，不引入程序集引用（`proposal-reference-directive.md:195`）。

### 与 C# 的对照

C# 的 `#load` 与本 fork 的 `#Load` **同源同层**（编译器指令 trivia + 独立树），但在「谁去重、怎么判环」上分叉：

| 维度 | C# `#load` | VB（本 fork） |
|---|---|---|
| 指令节点 | `LoadDirectiveTriviaSyntax`（含 `EndOfDirectiveToken`，`CSharp\Portable\Syntax\Syntax.xml:5197-5209`） | `LoadDirectiveTriviaSyntax`（无 `EndOfDirectiveToken`，`Syntax.xml:9552-9558`） |
| 解析门控 | `DirectiveParser.cs:519-537`：`Regular` → `ERR_LoadDirectiveOnlyAllowedInScripts = 8097`；`isFollowingToken` → `ERR_PPLoadFollowsToken` | `ParseConditional.vb:482-485`：同两门，BC36967 / BC37002 |
| 展开点 | `SyntaxAndDeclarationManager.cs:204-289`（编译对象侧） | 宿主 `VisualBasicScriptCompiler.CollectLoadTrees`（`:73-111`） |
| 缺文件 | `ERR_NoSourceFile`（`ErrorCode.cs:685` = 1504），锚 `fileToken.GetLocation()`，附「Could not find file」（`SyntaxAndDeclarationManager.cs:229-234`） | `ERR_FileNotFound`（BC2001），锚 `directive.File.GetLocation()`（`:94-95`） |
| 重复/环 | **按路径去重**：`!loadedSyntaxTreeMapBuilder.ContainsKey(resolvedFilePath)`（`:236`），已在映射中则「don't attempt to load it again」（else 分支 `:270-275`，注释在 `:272-273`）——菱形去重、环自然终止 | **按递归栈判环**：`activeLoads`（`:94`、`:106`）——菱形**不去重**、环报 BC2001 |
| resolver 为空 | 产 `ERR_SourceFileReferencesNotSupported` 诊断（`:220-227`） | 无守卫，含 `#Load` 时 NRE（Unresolved 4） |
| 加载树 parse options | 沿用外部树（`tree.Options`，`:241-244`） | 沿用同一 `parseOptions`（`:99`） |

**关于 csharplang 规范**：`InternalDevDocs\csharplang\` 镜像内**未找到** `#load` 的语义规范（`grep -rl "#load"` 仅命中 2 处会议纪要旁及：`meetings\2021\LDM-2021-03-15.md:65-66` 讨论 global using 与 `#load` 的相互作用、`meetings\2021\LDM-2021-05-12.md:76-79` 把 `#load` 列为「文件结构 vs 工程结构」待调和项）。`#load` 的权威定义在**编译器实现**（上表）与 C# scripting 文档，不在 csharplang 提案库——此处如实记录「未找到」，不代以推测。

## Unresolved questions
[unresolved]: #unresolved-questions

以下均为**已实现能力在 spec 化时尚未覆盖的边界**，不是设计未定：

1. **菱形加载是否应去重（与 C# 分歧、无测试）。** 当前实现只在**当前递归栈**内判环（`VisualBasicScriptCompiler.vb:94`、`:106`），两条分支加载同一路径会重复加载并重复执行顶层语句；C# 按路径去重（`SyntaxAndDeclarationManager.cs:236`、`:270-275`）。spec 需裁定：VB 是保持「栈内判环 + 菱形重复」还是改成路径去重？改则需说明「同一路径不同大小写/不同相对写法」的规范化口径（`activeLoads` 已用 `OrdinalIgnoreCase`，`:188`）。当前**无任何测试**覆盖菱形场景。
2. **缺失文件与环是否应分码分消息。** 两者共用 `ERR_FileNotFound`（BC2001）且消息是「File not found」（§3、§Drawbacks）。spec 需明确这是有意简化（可修）还是应新增诊断区分「循环 `#Load`」——C# 侧两者也不同码（缺文件 `ERR_NoSourceFile`，环则根本不报错）。
3. **「加载树先于主树执行」缺直接测试。** 树序由宿主 `trees.Add(tree)` 的位置决定（`VisualBasicScriptCompiler.vb:199`）+ 声明表按 `TreeOrdinal` 排序（`Declarations\DeclarationTable.vb:171-175`、`Compilers\CSharp\Portable\Symbols\LexicalSortKey.cs:98-119`），静态链路完整；但现有测试（`TestReturnValueInLoadedFile`、`TestNestedLoadDirective`）都只断言**返回值**，没有「加载树顶层语句先于主树顶层语句」的顺序断言（例如两个文件各 `Print` 一行后比对输出顺序）。spec 需给出顺序的规范表述并补一条直接测试。
4. **`SourceResolver` 为 null 时 `#Load` 的行为。** `CollectLoadTrees` 取 `options.SourceResolver` 后直接调用，无 null 守卫（`VisualBasicScriptCompiler.vb:85`、`:93`）；`ScriptOptions` 构造函数只有 `Debug.Assert(sourceResolver != null)`（`Scripting\Core\ScriptOptions.cs:162`），Release 下不拦。对照 `NuGetRestoreCoordinator.vb:209-213` 有显式守卫。spec 需明确：是「`SourceResolver` 为 null 时 `#Load` 应产诊断（镜像 C# 的 `ERR_SourceFileReferencesNotSupported`）」还是「API 契约要求非 null、违者 NRE」。
5. **公开 API 台账是否缺 `#Load` 条目（`Suspect`）。** `LoadDirectiveTriviaSyntax` 在生成代码里是 `Public NotInheritable`（`Generated\Syntax.xml.Syntax.Generated.vb:37968`）、`CompilationUnitSyntax.GetLoadDirectives` 是 `Public`（`Syntax\CompilationUnitSyntax.vb:33`）、`SyntaxKind.LoadKeyword` / `LoadDirectiveTrivia` 是公开枚举值；但 `Compilers\VisualBasic\Portable\PublicAPI.Shipped.txt` 与 `PublicAPI.Unshipped.txt` 中 `LoadDirectiveTrivia` / `LoadKeyword` / `GetLoadDirectives` **零命中**（对照同批 `#!` 的条目已进 Unshipped、`#R` 的条目在 Shipped）。若 Public API 分析器启用，构建会报 RS0016。**未跑构建验证**，故标 `Suspect`；spec/实现收口时需确认并补齐。
6. **VB 编译器级语法测试缺位。** `#Load` 的三条语法行为——BC36967 模式门控、BC37002 位置门控、BC30217 缺操作数——在 `Compilers\VisualBasicSyntaxTest` / `VisualBasicSemanticTest` / `VisualBasicTest` 中**无任何测试**（`Compilers\VisualBasic*Test` 的测试源码对 `LoadDirective` / `GetLoadDirectives` **零命中**；`SyntaxFactsTest.vb:119` 只把 `"load"` 登记为 contextual keyword）。注意 C# 侧**有**编译器级测试（`Compilers\CSharp\Test\Symbol\Compilation\LoadDirectiveTests.cs`、`PreprocessorTests.cs:4425` 的 `GetLoadDirectives`），差距在 VB 侧而非全仓。现有覆盖全在宿主层 `Scripting\VisualBasicTest`（`TestLoadDirectiveAfterFirstTokenReportsDiagnostic`、`TestLoadDirectiveAtFirstTokenHasNoFollowsTokenDiagnostic` 等）。spec 需明确这些语法门控的编译器级回归面归属。
7. **禁用条件编译区内的 `#Load` 语义未运行验证。** 静态看，禁用区文本被归入 disabled text trivia（`Scanner\Directives.vb:552-643`），不成为指令节点、故不被展开（§1）；但 VB 指令 trivia 无 `IsActive`（`Syntax.xml` 零命中，C# 21 处），「启用区内的 `#Load`」与「禁用区内的 `#Load`」在 API 上不可区分。spec 需明确条件编译与 `#Load` 的相互作用，并补一条运行验证。
8. **`#Load` 与 `#R` 在文件头部的相对顺序是否有约束。** 两者都只取首个 token 的 leading trivia（`CompilationUnitSyntax.vb:24-28`、`:37-41`），当前无「`#R` 必须在 `#Load` 之前/之后」的规则，展开顺序也与声明顺序无关（`#R` 走引用管理器、`#Load` 走宿主树序）。spec 需明确这是有意无约束，还是需要规定顺序语义。

## 相关文档

- `issues\issue-vbx-load-span-shift.md` — 文本内联 → 编译器 trivia + 多树提交的修复记录（本提案 §3/§4/§5d 的来源）
- `upstream-merge.md` 2.5 / 2.16 — `#Load` 宿主侧改动与 `CollectLoadTrees` 共用点的本地修改面台账
- `proposals\proposal-reference-directive.md` — `#R` 引用指令本体（同层指令 trivia 的对照；`#Load` 与 `#R` 的差异见本提案 §2）
- `proposals\proposal-scripting-dialect.md` — 脚本方言的声明与提交模型（script class、提交链、`Imports` 累积机制的背景）
- `proposals\proposal-shebang-directive.md` / `spec\spec-shebang-directive.md` — `#!` 指令（同属脚本文件头部机制；其 §4 的指令三分表 `#Load` 行**应以本文为规范出处**——该档当前引用的锚点已过时，见本提案 §3 与 `upstream-merge.md` 2.16）
- `proposals\vbx-1.2-beta\proposal-repl-directives.md:30` — 明确 `#Load` 属 2.0、不属 1.2
- `spec\README.md:36` — 2.0 beta 能力归档中 `#Load` 的一句记录（本文的前身）
- `proposals\proposal-vbi-nuget-reference.md` — NuGet restore 驱动环（本提案 §6 的邻域）
- `Compilers\CSharp\Portable\Compilation\SyntaxAndDeclarationManager.cs` / `Parser\DirectiveParser.cs` — C# `#load` 的编译器级实现（Alternatives 对照基准）
- `csharplang\meetings\2021\LDM-2021-03-15.md` / `LDM-2021-05-12.md` — csharplang 镜像内仅有的两处 `#load` 旁及（无语义规范，见 Alternatives）
