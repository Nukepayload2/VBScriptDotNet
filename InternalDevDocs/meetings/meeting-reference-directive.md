# Visual Basic Language Design Meeting
September 10, 2026

议题是 `proposal-reference-directive`——把 `.vbx` 与交互窗口**源码面唯一的程序集引用机制** `#R "字符串"` 钉成唯一事实基线：它是编译期指令 trivia（`ReferenceDirectiveTriviaSyntax`，`SyntaxKind.ReferenceDirectiveTrivia = 750`），只在 `SourceCodeKind.Script` 下合法，且必须出现在编译单元首个 token 之前；每棵树的 `#R` 在**声明表构建期**收集，并作为「引用集合是否可能变化」的粗粒度脏标记参与增量重绑；操作数的**字符串解释**不由 VB 语言规定，而由宿主注入的 resolver 决定；一条 `#R` 可展开为 N 个引用（fork 改动），N 的完整集经前序提交继承；`<host>` / `<implicit>` 两处引用别名的语义依赖 fork 的 VB「尊重元数据引用别名」行为。这是一份记录型提案（`Prototype: Complete`、`Implementation: Complete`），记录的是随 1.2 发布、并在共享 Core 上补完单 `#R`→N 的既有机制——所以这场会议从一开始就不是「要不要做」，而是「这份事实基线准不准、全不全」。

我们两条独立路径各读了一遍——一条沿 VB 基因问「这像不像 VB、有没有给普通 VB 开第二种做事方式」，一条沿 C# 生态问「它与 C# `#r`、与共享 `Compilers\Core` 合不合」。定性很快收敛：**`#R` 不是 VB 语言特性，是「编译器语法 + 宿主契约」的组合**。VB 语言规范从卷首到卷尾没有一条源层程序集引用语法——`preprocessing-directives.md:3` 把指令族限定为条件编译、外部源与区域三类；`source-files-and-namespaces.md:27-29` 的 `Start` 文法只有 `OptionStatement* ImportsStatement* AttributesStatement* NamespaceMemberDeclaration*`，没有指令位；`Imports` 明文「只让名字可用、不在全局命名空间声明任何东西」，且作用域不含其它 `Imports` 与其它源文件（`source-files-and-namespaces.md:253`）。程序集引用整体是「编译环境」的事。`#R` 以 trivia 形态待在语言之外、在普通编译里直接报错，**没有给普通 VB 增加第二种做事方式**——这是它最保守也最正确的落位。

但把 §1–§9 的锚点翻完之后，我们发现这份「唯一事实基线」在**两处把设计代价说轻了**（别名的「隐藏即不可达」、`IsFilePath` 的形状启发式）、在**一处把共享公共面的行为说反了**（N 完整集的观察点）、在**一处把 C# 的引用张冠李戴**（位置约束的来源）。前者才是真正的会议内容：别名只由宿主与工具链注入，可一旦挂上，VB 源码里就**没有任何语法能到达**被隐藏的类型；解析顺序靠字符串形状启发式分派，同一条 `#R` 在不同机器上解析到不同程序集，而且**没有任何诊断**。公共面那处是硬错误——它落在 C#/VB 共享的 `Compilation` 公共 API 上，两条独立路径各自都撞到了它。本纪要按「先复核、再权衡、后裁决」记下这段推理旅程。

## Agenda

* [Proposal: `#R` 引用指令 / Reference Directive](#proposal-r-引用指令--reference-directive)

## Proposal: `#R` 引用指令 / Reference Directive

_Related: [`../proposals/proposal-reference-directive.md`](../proposals/proposal-reference-directive.md)；邻域 `../proposals/proposal-vbi-nuget-reference.md`、`../proposals/proposal-vbi-project-reference.md`（同为 `#R` 字符串操作数的宿主解释扩展）；同族头部指令 `../proposals/proposal-shebang-directive.md` / `../spec/spec-shebang-directive.md`；方言背景 `../meetings/meeting-scripting-dialect.md`；`../decisions.md` M5 / D2 / D4；共享层台账 `../upstream-merge.md` 2.9_

> **来源标注**：正文引用的源码与规范文本均逐字核对（`文件:行号`）。提案 §1–§9 的锚点逐一复核，绝大多数属实；复核中发现一处事实性表述不准（§5 的 N 完整集观察点）、一处引用张冠李戴（Motivation 与「与 C# 的对照」表里的 C# 位置约束）、一处范围写窄（§6 别名只以脚本立论），另有一条提案与两路意见都没碰到的归属问题（BC36959）。提案是冻结输入，本纪要对它的修正只写进 RESOLUTION。

### 场景与缺口

`.vbx` 与 REPL 的**源码面**只有这一条引用指令：本地 dll、TPA 裸名、`nuget:` 包、`project:` 工程引用最终都落到同一条 `#R` 操作数上（启动面另有 `@vbi.rsp` 的 `/r:`）。它随 1.2 发布，而归档里只有一句「`#R`：引用程序集」（`proposals\vbx-1.2-beta\proposal-repl-directives.md:22`；`proposals\README.md:74` 的「指令系统（`#R`、`#help`、…）」只是登记它属于哪一批）——语法位置、收集机制、宿主契约、别名语义、跨提交继承这些**已被依赖的契约**都没有落文字。底座的缺口是真的：`nuget:` 与 `project:` 两份 Active 提案都已明确自己是「`#R` 字符串操作数的宿主解释」的相邻扩展，底座没有自己的事实基线，三份文档就会各自描述解析顺序与继承语义。

我们认同这份「记录型」定位——它不是发明，而是把已落地并已被依赖的机制说清楚。定性上没有任何悬念，会议的重心因此全部落在**事实准确性**与**规范表述的精确度**上。

### 翻源码：§1–§9 锚点复核

**§1 语法面逐条属实。** 节点定义在 `Syntax\Syntax.xml:9544-9550`（`ReferenceDirectiveTriviaSyntax` 派生 `DirectiveTriviaSyntax`，子节点 `ReferenceKeyword` 与 `File`），枚举值 750（`Syntax\SyntaxKind.vb:3044`）；解析入口 `Parser\ParseConditional.vb:82-83` 派发到 `ParseReferenceDirective`（`:454-472`），`Not IsScript` 挂 `ERR_ReferenceDirectiveOnlyAllowedInScripts`（`:462-463`；`Errors\Errors.vb:1597` = 36964），`isFollowingToken` 挂 `ERR_PPReferenceFollowsToken`（`:464-466`；`Errors.vb:1591` = 36959），操作数经 `VerifyExpectedToken(StringLiteralToken)`（`:468-469`）。位置判据逐字：「A directive is "following a token" if the leading trivia it belongs to does not start the tree … Mirrors C# #load/#r.」（`Scanner\Directives.vb:41-43`），而语法树 API 把这条钉死为「`#r` directives are always on the first token of the compilation unit.」（`Syntax\CompilationUnitSyntax.vb:24-28`）。

**§2 收集与脏标记属实。** 声明表构建期过滤「含诊断或 `ValueText` 为空」后逐条构造 `ReferenceDirective(File.ValueText, New SourceLocation(directiveNode))`（`Declarations\DeclarationTreeBuilder.vb:101-113`）；收集只在非 Regular 树发生（`:174`、`:198`），Regular 分支固定空（`:201`）；结果挂在 `RootSingleNamespaceDeclaration.ReferenceDirectives`（`:14-18`、`:26-37`），由 `DeclarationTable` 合并（`Declarations\DeclarationTable.vb:216-224`）。脏标记 `HasReferenceDirectives = Options.Kind = Script AndAlso …Count > 0`（`Syntax\VisualBasicSyntaxTree.vb:85-91`），在增删/替换树时累积（`Compilation\VisualBasicCompilation.vb:1060`、`:1132`、`:1140`），最终落到 `reuseReferenceManager:=Not referenceDirectivesChanged`（`:571`）；`ReplaceSyntaxTree` 留有「比较新旧 `#r` 以复用」的上游 TODO（`:1178-1180`）。两个读口 `ReferenceDirectiveMap`（`Friend`，`:1375-1379`）与 `ReferenceDirectives`（`Friend`，`:1428-1432`）与提案描述一致。

**§3 宿主契约二选一属实。** `Arguments.IsScriptRunner` 为真给完整命令行 resolver，否则包 `ExistingReferencesResolver`（`Compilers\Core\Portable\CommandLine\CommonCompiler.cs:225-233`，类注释逐字「When scripts are included into a project we don't want #r's to reference other assemblies than those specified explicitly in the project references.」，`CommonCompiler.ExistingReferencesResolver.cs:17-21`、`:44-48`）；VB 命令行接线在 `VisualBasicCompiler.vb:146-147` 与 `:170`。

**§4 解析顺序逐条属实。** `RuntimeMetadataReferenceResolver.ResolveReference`（`:145-198`）的 `nuget:` 前缀分支（`:147-155`，`PackageResolver == null` 时不进后续分支、落到 `:197` 的 `return Empty`）→ `IsFilePath` 路径分支（`:156-175`，无分隔符先按文件名查 TPA，再 `PathResolver.ResolvePath`）→ 非路径分支（`:176-195`，GAC → 显示名 + TPA）；`ResolveMissingAssembly` 是另一条顺序 GAC → TPA → 请求方引用所在目录（`:100-138`，`:98` 声明 `ResolveMissingAssemblies => true`）；`<implicit>` 别名属性在 `:27-29`。基目录与搜索路径经 `CommandLineRunner.cs:212-223` 装配。

**§5 属实，但「N 完整集只经 `ExplicitReferences` 流动」这句不准确**——这是本次复核最实质的一处修正，见「权衡三」。

**§6 的机制属实，但立论范围写窄了**——见「权衡一」。

**§7 术语钉死正确。** `Script.InheritOptions` 明确清空 references 与 imports（`Scripting\Core\Script.cs:133-139`，注释「don't inherit references or imports, they have already been applied」），跨提交引用继承走 `CommonReferenceManager.Resolution.cs:853-858` 的全量追加——与 `spec-scripting-dialect.md` 的「引用是继承、`Imports` 是累积」完全一致。

**§8 样例与 §5 的测试属实。** `Samples\WpfCpuCoreInformation.vbx:1-3` 三条本机 dll 裸名；`Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb:109-142` 锁定「单 `#R "nuget:X"` 展开 [主资产, 闭包] + 闭包类型只在第二条提交被使用仍可绑定」，`:144-200` 锁定「两条 `#R` 各自的坏资产把 BC31519 锚到自己的行」。两个测试全程内存程序集，无磁盘写/进程/网络。另记一笔：含目录分隔符的路径形态其实**有测试锚点**——`ScriptTests.vb:721-726` 用绝对路径 `#R "…\Scripting.VisualBasicTest.dll"` 与 `#!`、`#Load` 组合，提案 §4 说「无样例锚点」时指的是样例不是测试。

**一条提案与两路意见都没碰到的归属问题（新发现，`Suspect`）。** 提案 §1 与「与 C# 的对照」把 BC36959 当作上游 VB 既有实现，但本仓有两条 fork 自述与它张力：`Errors.vb:1589-1591` 的注释逐字「Fork occupies an official intermediate gap. …」紧贴在 `ERR_PPReferenceFollowsToken = 36959` 之上，`:1601-1605` 的「Fork error-numbering policy」又把 `36959` 列为 fork 占位空隙的**首个例子**（与确定属于 fork 的 `36967`、`37002` 并列）；13 份 VB xlf 里没有 `ERR_PPReferenceFollowsToken` 的 trans-unit，而同族的 `ERR_ReferenceDirectiveOnlyAllowedInScripts` 在（`VBResources.de.xlf:7772-7776`，ja / zh-Hans / zh-Hant 三份为 `:7773-7777`）。若 36959 确为 fork 新增，则「本 fork 未改 `#R` 语法面」这句需要加限定。本仓无上游基线可 diff（`Compilers\VisualBasic\Test\` 已裁剪，`.git` 历史里基座是单次导入），故标 `Suspect`，收口动作见 OPEN QUESTIONS。

**U7 与 Unresolved 纪律两笔收尾。** 粗粒度脏标记不是缺陷，而是有意的保守策略——引用集合只要**可能**变了就重建引用管理器，代价是 IDE 里每次编辑脚本都重建引用绑定，上游留下的「比较新旧 `#r`」TODO（`VisualBasicCompilation.vb:1178-1180`）尚未实现；spec 应把它写成「正确性优先、已知性能边界」，不是待修项。另记一条交付纪律：这份提案的 7 条 Unresolved 都是「已实现能力的边界未覆盖」而非设计未定，spec 定稿时必须逐条落进正文 / Alternatives / Considerations，不得以 Unresolved 形态随 spec 发布（`spec\README.md:113`——完成态特性的未决节只写 `None.`）。

### 权衡一：别名是设计层最大的缺口——两路独立命中

这是全场唯一一处「提案写成效果、我们读成代价」的地方。机制本身我们核清了：fork 的 VB 在合并全局命名空间时按引用是否**无别名**过滤——

```
' Metadata imported from aliased assemblies is not visible at the source level unless it is
' exposed through the global alias.  This mirrors C# (extern aliases) and lets /nostdlib on
' .NET Core reference the real core library (System.Private.CoreLib) without leaking its
' entire type surface into the global namespace.
If referenceManager.DeclarationsAccessibleWithoutAlias(i) Then
```
—— `Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:107-120`；判据在共享层 `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs:721-725`（`aliases.Length = 0 OrElse aliases.IndexOf(GlobalAlias) >= 0`）。

**C# 有逃生口，VB 没有。** 同一判据的 C# 侧消费点在 `Compilers\CSharp\Portable\Binder\ImportChain.cs:148`，而 C# 还有 `extern alias` 语法可以把被挡住的命名空间捞回来；VB 全树 `Syntax\SyntaxKind.vb` 里没有任何 `ExternAlias` 类节点，命令行解析器里 grep `alias` 只有一处（`:1629-1633` 给 `System.Private.CoreLib` 挂内部别名）。也就是说：**一旦某引用被挂别名，VB 源码里没有任何语法能到达它——隐藏是绝对的。** 而 VB 对「名字解析不到」的既有先例恰好是**发警告而非沉默**：「If an import alias points to a type or namespace which cannot be resolved by these rules, then the import statement is ignored (and the compiler gives a warning).」（`vblang\spec\source-files-and-namespaces.md:358`）。别名是整片命名空间静默消失，与这条先例反向。

**提案 §6 与 U4 只以脚本立论，漏了 Regular 编译的 `/nostdlib` 路径。** `VisualBasicCommandLineParser.vb:1626-1633` 在 `/nostdlib` 下取 `System.Private.CoreLib.dll` 并给它挂 `RoslynCoreLibrary` 别名，注释逐字说明了理由：「Keep it out of the global namespace (via a non-global alias): SPC defines the System namespace, and letting it merge into the global namespace would change the /nostdlib "type not defined" error set」。**过滤在那里是功能必需，不是脚本专属。** 所以别名行为的消费者不止 `Scripting`：`Script.cs:237-239` 的 `<host>` 与 `RuntimeMetadataReferenceResolver.cs:27-29` 的 `<implicit>` 是两处宿主侧来源，而 VB 命令行自身是第三处——这条抬高了「合并上游时该行为被回退」的代价等级（预测，待定）：受影响的不只是脚本的裸名可见性，普通 VB 编译 `/nostdlib` 的错误集也会变。

**缓解成立，且我们找到仓内自证。** 全仓 `WithAliases(` 的生产路径只有 `Script.cs:239` 与 `RuntimeMetadataReferenceResolver.cs:29`（其余是 Core 的 API 定义与测试），别名**只能由宿主/工具链注入**，源码产生不了；对脚本而言隐藏正是意图本身。更重要的是，两路意见都把「上游 VB 忽略别名、本 fork 有意尊重」标成了 `Suspect`（本仓无上游基线），而我们在 fork 自己的符号测试树里找到了锁定与自述：

```
' This fork respects aliases on metadata references (matching C# extern aliases): the aliased
' lib type is hidden from the global namespace, so 'Task' binds to mscorlib's type and no
' ambiguity is reported. (Upstream VB ignored aliases and reported BC30560.)
```
—— `Compilers\VisualBasicSymbolTest\SymbolsTests\AssemblyAndNamespaceTests.vb:559-561`（注释逐字），断言在 `:562-564`（`WellKnownTypesAndAliases`，测试体 `:531-565`）；同文件 `:504-527` 的 `SpecialTypesAndAliases` 另锁定「被别名的 corlib 仍提供 `System.Object` 特殊类型」。**这把 provenance 从「无据」升级为「仓内自证」**——该分叉是 fork 有意的，且有一条回归测试钉着。顺带记一笔目录事实：本 fork 的**符号**测试树并未裁剪（`Compilers\VisualBasicSymbolTest\` 是活的测试工程，含 111 个 `.vb` 测试文件），与「编译器测试树已裁剪」的既有叙事要分清目录。

**裁决：设计本体采纳，但 U4 必须扩范围并补三条事实**（无逃生口、`/nostdlib` 同样是功能语义、别名语义的规范表述），否则 spec 会把「隐藏了什么」写清却把「隐藏即不可达」漏掉。

### 权衡二：`IsFilePath` 的形状启发式——最不 VB-like 的一块

解析顺序的三段分派由一个字符串形状判据决定：

```
string? extension = FileNameUtilities.GetExtension(assemblyDisplayNameOrPath);
return string.Equals(extension, ".dll", …) || string.Equals(extension, ".exe", …)
    || assemblyDisplayNameOrPath.IndexOf(DirectorySeparatorChar) != -1
    || assemblyDisplayNameOrPath.IndexOf(AltDirectorySeparatorChar) != -1;
```
—— `Compilers\Core\Portable\FileSystem\PathUtilities.cs:518-527`

后果我们逐条验过：`#R "System.Text.RegularExpressions"` 的「扩展名」是 `.RegularExpressions`，**走非路径分支**（GAC → 显示名 + TPA）；写成 `#R "System.Text.RegularExpressions.dll"` 则走路径分支且**先按文件名查 TPA 再回落相对路径**；含分隔符的路径跳过 TPA 文件名查找。同一程序集，加不加 `.dll` 走**不同解析链**，而**没有任何提示**。更关键的是可移植性——同一个 `#R "Microsoft.WinUI"` 在不同机器/不同宿主下解析结果可能不同（提案 Drawbacks 自己承认），`.vbx` 因此不是自含的，而 `Samples\CLAUDE.md` 的自我描述恰恰是「No project files or compilation required」。

VB 的基因是「显式、可读、可诊断」（`vblang\spec\introduction.md:3` 逐字「Wherever possible, meaningful words or phrases are used instead of abbreviations, acronyms, or special characters.」），这里三项全缺。我们**不主张改行为**——`#R` 已随 1.2 发布，且与 csi 的 `#r` 同源，改拼写或改语义是更大的破坏。要求落在规范上：U5 把精确判据与三分支完整顺序写成条文（不用举例代替规则），U6 把 `ResolveReference` 与 `ResolveMissingAssembly` 两条顺序**分别**钉死并说明「`#R` 走前者、被引用程序集的依赖走后者」，文档给**规范形**（跨机可复现的脚本应写相对路径或 TPA 裸名，不依赖 GAC/TPA 的偶然命中）。这是「不静默失败」原则在解析面的兑现。

### 权衡三：N 展开——公共面的观察点被提案说反了

提案 §5 与 U3 的口径是「N 的完整集只经 `ExplicitReferences` 流动，`ReferenceDirectiveMap` 只存主资产」。前半句**不准确**，我们逐行走了一遍共享 Core：

- 每条展开出来的引用都进 `referencesBuilder`，**并**为每条追加同一个 `#r` 的 location——两数组等长同序（`CommonReferenceManager.Resolution.cs:833-839`）；
- 于是凡 `referenceIndex < referenceDirectiveCount` 的引用都进 `uniqueDirectiveReferences`（`:226-238`、`:270-275`），落到 `boundReferenceDirectives`（`:396-403`）；
- 该值经 `CommonReferenceManager.State.cs:435`（internal 属性 `:250-257`）暴露为**公共** `Compilation.DirectiveReferences`（抽象声明 `Compilation\Compilation.cs:726-728`，注释「Unique metadata references specified via #r directive in the source code of this compilation.」；VB 覆写 `VisualBasicCompilation.vb:1369-1373`；已登 `Compilers\Core\Portable\PublicAPI.Shipped.txt:32`，VB 覆写登 `Compilers\VisualBasic\Portable\PublicAPI.Shipped.txt:4528`），并进而进入公共 `Compilation.References`（`Compilation.cs:744-758`，`ExternalReferences` + `DirectiveReferences`）。

所以事实是：**N 的完整闭包同时可从公共 `Compilation.DirectiveReferences` / `Compilation.References` 观察到**，而不只是从 internal 的 `ExplicitReferences`。`ReferenceDirectiveMap` 保持单值（只存 `boundReferences[0]`，主资产）是对的，但它不是唯一的观察点。这条对 C# 生态的意义不是「破坏性」——C# 侧只有 resolver 返 >1 才激活，本 fork 无 C# 脚本产品路径（`Scripting\` 下只有 `Core` 与 `VisualBasic`），且 C# 测试无一处断言旧的 `NotSupportedException`（`Compilers\CSharp\Test\Symbol\Compilation\ReferenceManagerTests.cs` grep 零命中）——而是「公共 API 的语义面变了」：注释说「specified via #r directive」，而闭包里的程序集**并不是**源码里 `#r` 写的。以 `compilation.References` 重建引用集的外部工具（C#/VB 共享面）会多出闭包程序集，spec 与 `upstream-merge.md` 2.9 都要写清。

复核还带出一条去重交互，提案的 U2 只覆盖了 `#r`↔`#r` 一半：`references` 数组是「directive 引用在前、外部引用在后」（`:850-858`），而登记循环**倒序**（`:247`），因此外部引用先注册；闭包程序集若已在外部引用集里，轮到它时被 `TryGetValue` 命中而 `continue`（`:257-266`），既不留 directive location（`location = Location.None`），也不进 `DirectiveReferences`。也就是说「闭包是否出现在公共面」取决于它是否已被别处引用过——spec 必须说明这条交互。

一条正向实锤提案没引：运行时加载不漏闭包——`Scripting\Core\ScriptBuilder.cs:142-151` 遍历 `compilation.GetBoundReferenceManager().GetReferencedAssemblies()` 逐个 `RegisterDependency`，N 的每个引用都会被注册。这条支撑「闭包可跨提交运行」的结论，值得补进 spec 的证据链。

### 权衡四：C# 对照表的位置约束引错了对象

提案在「与 C# 的对照」表里把 C# 侧的位置约束引到 `csharplang\proposals\csharp-14.0\ignored-directives.md:59`。那条的主语是 **ignored directives**（`#!` / `#:`），不是 `#r`——C# `#r` 的位置检查在 `Compilers\CSharp\Portable\Parser\DirectiveParser.cs:499-517` 的 `isFollowingToken` 分支（`:507-510`），与 `#:` 是两条代码路径，而且规则并不等价：ignored directive 多一条「须在任何 `#if` 之前」的约束（`ignored-directives.md:61-62`；实现见 `DirectiveParser.cs:711-714` 的 `_context.SeenAnyIfDirectives`），C# `#r` **没有**这条（`ParseReferenceDirective` 全文无该检查），VB 的 `#R` 也没有——两语言在这一点上同侧。作为 spec 底座，这类引错会让读者推出错误规则。

三项对齐倒是实锤，值得写进对照表：模式门控 BC36964 ↔ CS7011（`CSharp\Portable\Errors\ErrorCode.cs:1157`）、位置约束 BC36959 ↔ CS7009（`:1155`）、空操作数过滤**逐字同构**（VB `DeclarationTreeBuilder.vb:103` 的 `Not d.File.ContainsDiagnostics AndAlso Not String.IsNullOrEmpty(d.File.ValueText)` 与 C# `CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:305-308` 同式）。

另有一条门控对照提案没写：C# 的 `#:` / `#!` 只在 `FileBasedProgram` 解析选项下合法（`DirectiveParser.cs:697-704` 的 `ERR_PPIgnoredNeedsFileBasedProgram`），而 VB 全树 grep `FileBasedProgram` 零命中——**VB 没有 file-based program 这一模式**，`.vbx` 的对照物是 C# scripting（`.csx`），不是 `dotnet run file.cs`。`Samples\CLAUDE.md` 已经写了「This `#R "nuget:"` script form is distinct from the official file-based `#:package id@version` syntax; the two are not interchangeable.」，spec 应与之一致。

### 权衡五：U1 空串、disabled 区、头部指令分类

**U1 的两路意见其实互补，不冲突。** 当前实现把「含诊断或 `ValueText` 为空」的 directive 直接过滤掉（`DeclarationTreeBuilder.vb:102-103`），所以 `#R ""` 是**静默丢弃**。一路指出这与产品已定的「不静默失败」原则冲突；另一路指出 C# 逐字同构——**C# 也静默丢弃**。合起来看，收口方向是明确的：把它写成「**有意行为，镜像 C# 编译器**」（`#R ""` = 无操作），并显式声明这不是 bug；若要加诊断，那是一次**偏离 C# 的独立决定**，必须单列理由。两种写法都能闭合 U1，但 spec 必须二选一写明，不能留成开放问题。

**disabled 区内的 `#R` 有现成条文可对齐，且仓内机制支持。** VB 的扫描器在条件编译为假时走 `SkipConditionalCompilationSection`（`Scanner\Directives.vb:552-643`），只有 `#End If` / `#End` / `#ElseIf` / `#Else` 终止跳过（`:577-582`），其余整段（含 `#R`）收进 `DisabledTextTrivia`（`:638-639`）——**disabled 区内的 `#R` 不产生指令节点、不报诊断**，与 `spec\spec-shebang-directive.md:100-102` 对 `#!` 的条文逐点同形。spec 补一条对齐即可。

**头部指令该立类。** `preprocessing-directives.md:9-18` 的 `CCStatement` 让条件编译指令可出现在任意逻辑行序列中（自由位置），`#Region` / `#ExternalSource` 也只受「不跨方法体、尊重块结构」约束；`#R` 的「须在首个 token 之前」在 VB 规范里是一类**新东西**。`#!` 已经开了先例（`spec-shebang-directive.md:40-50` 的「Position on the first line」），`#Load` 同族（`CompilationUnitSyntax.vb:37-40` 同样只从首 token 取）。但两族的阶梯不一致：`#!` 要求位置 0（连 BOM 都不行），`#R` / `#Load` 只要求「首个 token 之前」（前面可以有注释与其它指令）——这个差异**有理由**（OS 要求 shebang 字面第一行），但 spec 化时必须显式写出「本方言存在自由位置指令与头部指令两类，后者的位置判据分别是 X/Y/Z」，否则读者会从 VB 规范的指令模型推出错误预期。

**拼写 `#R` 与 `introduction.md:3` 有张力，但不改。** 同族的 `#Const` / `#If` / `#Region` / `#ExternalSource` 都是词，`#R` 是单字母缩写。三条理由让它保持唯一拼写：它随 1.2 发布，改拼写是对已发布脚本的破坏，而兼容性政策要求收益「clear and overwhelming」（`introduction.md:21`）；它与 csi 的 `#r` 同源，工具链心智一致；若加 `#Reference` 之类别名，那才是真正的「第二种做事方式」（`vblang\meetings\2018\vbldm-notes-2018.06.13.md:34`：「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」）。它在语言之外，缩写规范的约束力本就弱于语言表面区。

最后一条最容易混淆、spec 必须明说：**`#R` 不导入命名空间**（`source-files-and-namespaces.md:253`），`#R "X"` 之后还要 `Imports`——§8 的样例同时给了两者，这个演示是必要的，规范要把它变成文字。

### VB 基因对照 / C# 生态对照

- **落位正确，且规范可证。** `#R` 不在 VB 语言规范里，也不在「扩展表面区」的尺子下——它没有给普通 VB 增加任何写法。Regular 编译里的 `#R` 本来就是错误（`ParseConditional.vb:462-463`），BC36964 只是把它明确化；`HasReferenceDirectives` 又要求 `SourceCodeKind.Script`（`VisualBasicSyntaxTree.vb:89`），Regular 树永不判脏。零回归面干净。
- **`#R` 与 `Imports` 不是重复，但必须写清分工。** `Imports` 只引名字、不引程序集（`:253`）；`#R` 只引程序集、不引名字。两者是「程序集面」与「名字面」的互补，`#R "X"` 不会让 X 的命名空间可见——这是新手上手最容易错的一步，spec 应给一句话。
- **两处与「可诊断」基因反向，规范必须补偿。** 别名的绝对隐藏（权衡一）与 `IsFilePath` 的形状启发式（权衡二）都不是「文档没写」，而是设计本身的代价。前者在 VB 无逃生口，后者连诊断都没有。写透它们（而不是写成「效果」），这份提案才算合格。
- **宽松/晚绑定基因无摩擦。** `#R` 不触碰绑定语义，只决定引用面；对 `Option Strict Off` 的晚绑定脚本无冲突，对早绑定强类型消费库是主流用法。
- **C# 生态面整体低风险。** `#R` ↔ C# `#r` 同源同形，模式门控、位置约束、空操作数过滤三项对齐；N 展开落在 C#/VB 共享的 `CommonReferenceManager` 上，但只在该 resolver 返 >1 时激活，属休眠区（C# 测试无断言旧 `NotSupportedException`，本 fork 无 C# 脚本产品路径），代价是公共观察点的语义面变了（权衡三）——这是**潜在 interop 注记**，不是现行回归。别名把 VB 拉向 C# 的 extern-alias 语义，是兼容性收益；不对称之处在 VB 侧没有语法层面的逃生口。M1–M8 里只有 M5 相关：`#R` 本身不新增 AOT/trimming 摩擦（脚本提交本就被 `spec-scripting-dialect.md` 的 Considerations 排除在 AOT 之外），spec 引一句边界即可。
- **记录卫生要补。** 别名改动目前不在 `upstream-merge.md` 的本地修改面清单里（`upstream-merge.md` 2.9 只记 N 展开，且只写了「N 完整集经 `ExplicitReferences`」），`#R` 相关错误码也没有台账条目；`spec-scripting-dialect.md:231-239` 的脚本诊断表只列了 BC36964 与 BC36967，**没有** BC36959 与 BC37002——而 `Errors.vb:1601-1605` 的 fork 错误码策略注释恰恰以 36959 为例。这三处要一起收口。

### RESOLUTION:

1. **定性**：`#R` 是**编译器语法 + 宿主/工具链契约**的组合，不是 VB 语言特性；提案作为「已实现机制的唯一事实基线」成立，机制随 1.2 发布、单 `#R`→N 已登账 → 证据等级**已采纳**。定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`。映射 `decisions.md` **M5**（脚本运行时）；D4 不直接命中 P1 两档（既不是「C# 已照顾到的、非底层内存机制相关用例」，也不是「C# interop 用例」），优先级由实现规划结合路线图定。§1–§9 锚点逐一复核属实（`Syntax.xml` 节点定义一处按提案锚点采信，见 Follow-up 的 `Suspect`；本 RESOLUTION 第 2–5、10 条为对提案表述的修正口径）。
2. **U4 扩范围并补三条事实（本 RESOLUTION 为该表述的 supersede 口径）**：① 规范表述要写清「哪些别名存在、各隐藏什么、与 `global` 别名的关系」（`Script.cs:237-239` 的 `<host>`、`RuntimeMetadataReferenceResolver.cs:27-29` 的 `<implicit>`、`CommonReferenceManager.State.cs:721-725` 的判据）；② **VB 没有 `extern alias` 对应物**——被别名挡住的引用在 VB 源码中**不可达**，隐藏是绝对的（C# 有逃生口，`ImportChain.cs:148` 是同一判据的 C# 侧）；③ **该过滤在 `/nostdlib` 的常规编译里同样是功能语义**，不只脚本（`VisualBasicCommandLineParser.vb:1626-1633`）。缺 ②③，U4 不闭合。VB 对「名字解析不到」的既有先例是发警告（`source-files-and-namespaces.md:358`），spec 应正面说明别名为何是例外。
3. **别名 provenance 收口（本 RESOLUTION 为该表述的 supersede 口径）**：`Suspect` 升级为**仓内自证**——fork 自己的符号测试 `Compilers\VisualBasicSymbolTest\SymbolsTests\AssemblyAndNamespaceTests.vb:531-565`（注释 `:559-561` 逐字「This fork respects aliases on metadata references (matching C# extern aliases)…(Upstream VB ignored aliases and reported BC30560.)」）既锁定该行为又自述了与上游的差异；同文件 `:504-527` 另锁定「被别名的 corlib 仍提供 `System.Object`」。提案 §6 末句「这是 fork 基座带入的上游 2026 行为」的来源主张仍无锚点，**改为行为陈述或补 `upstream-merge.md` 条目**。Drawback 措辞升级：该行为若被回退，受影响的不只是脚本裸名可见性，普通 VB 编译 `/nostdlib` 的错误集也会变。
4. **§5 / U3 的观察点修正（本 RESOLUTION 为该表述的 supersede 口径）**：「N 完整集只经 `ExplicitReferences` 流动」改为——闭包同时可从**公共** `Compilation.DirectiveReferences`（`Compilation.cs:726-728`；VB 覆写 `VisualBasicCompilation.vb:1369-1373`；`Compilers\Core\Portable\PublicAPI.Shipped.txt:32`，VB 覆写登 `Compilers\VisualBasic\Portable\PublicAPI.Shipped.txt:4528`）与公共 `Compilation.References`（`Compilation.cs:744-758`）观察到；`ReferenceDirectiveMap` 仅主资产（`Friend` 面）；`ExplicitReferences` 是跨提交继承通道。`upstream-merge.md` 2.9 补记「公共观察点：`DirectiveReferences` 计数由 1/directive 变 N/directive，仅 N>1 激活」，并在公共 API 文档标注闭包程序集并非源码 `#r` 直接书写。
5. **U2 扩一条去重交互**：闭包引用与外部/前序引用共用同一去重键（`CommonReferenceManager.Resolution.cs:236`、`:257-266`），倒序遍历（`:247`）使外部引用先注册——闭包若已在外部引用集里会被去重，其 location 为 `Location.None` 且**不进** `DirectiveReferences`（`:271-275`）。spec 说明「闭包是否出现在公共面取决于它是否已被别处引用过」。
6. **U5 / U6 从「写清楚」升级为「钉死 + 规范形」**：`IsFilePath` 的精确判据逐字写（扩展名 `.dll`/`.exe` 或含目录分隔符，`PathUtilities.cs:518-527`，并说明「扩展名」指最后一个点后段，故 `.RegularExpressions` 不是 `.dll`）；三分支完整顺序成文，不用举例代替；`ResolveReference`（`:145-198`，`#R` 走这条）与 `ResolveMissingAssembly`（`:100-138`，被引用程序集的依赖走这条）**分别**固定，说明二者是两条不同契约、**不统一**；文档给跨机可复现的推荐写法（相对路径或 TPA 裸名）。
7. **U1 收口为「镜像 C# 编译器」的有意行为**：空操作数静默丢弃在 C# 侧逐字同构（`CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:305-308` vs VB `DeclarationTreeBuilder.vb:101-113`），spec 写成「`#R ""` = 无操作，与 C# 编译器一致」，与产品「不静默失败」原则的张力以「有意镜像」显式化；若要改为诊断，须作为**偏离 C# 的独立决定**单列理由与门控。
8. **补 disabled 区条文**：disabled conditional region 内的 `#R` 不产生指令节点、不报诊断（`Scanner\Directives.vb:552-643`，终止集 `:577-582`，跳过文本收进 `DisabledTextTrivia` `:638-639`），对齐 `spec\spec-shebang-directive.md:100-102` 的 `#!` 先例。
9. **spec 立「头部指令」类**：显式区分自由位置指令（条件编译，`preprocessing-directives.md:9-18`）与头部指令（`#R` / `#Load` / `#!`），给出各自位置判据及差异理由（`#!` 位置 0，`#R`/`#Load` 首个 token 之前）；并明写「`#R` 不导入命名空间，`Imports` 才导入」（`source-files-and-namespaces.md:253`）。`#R` 拼写保持唯一，不为缩写道歉。
10. **C# 对照表修正（本 RESOLUTION 为该表述的 supersede 口径）**：C# 侧位置约束改引 `DirectiveParser.cs:499-517`（不引 `ignored-directives.md:59`，那是 `#:`）；补一句「ignored directive 另有『须在任何 `#if` 之前』（`ignored-directives.md:61-62`、`DirectiveParser.cs:711-714`），**C# `#r` 与 VB `#R` 均无此约束**」；补 `FileBasedProgram` 门控对照（`DirectiveParser.cs:697-704`，VB 全树零命中）——`.vbx` 的对照物是 C# scripting（`.csx`），`#R "nuget:"` 与 `#:package id@version` 不可互换（与 `Samples\CLAUDE.md` 一致）。
11. **U3 进 spec 正文而非 Considerations**：`ReferenceDirectiveMap` 是「每条 directive 的主资产」而非「本提交的完整引用集」，闭包只能经公共 `DirectiveReferences` / `References` 或 `ExplicitReferences` 观察。
12. **U7 记录为有意的保守策略**：`HasReferenceDirectives` 只回答「这棵树里有没有 `#R`」（`VisualBasicSyntaxTree.vb:85-91`），因此任何含 `#R` 的树被替换都会让引用管理器不复用（`VisualBasicCompilation.vb:571`）；上游 TODO 在 `VisualBasicCompilation.vb:1178-1180`。spec 写成「正确性优先的保守策略、已知性能边界」，不是缺陷。
13. **记录卫生三件**：① 别名改动入 `upstream-merge.md`（现 §二未列）；② `#R` 相关错误码（BC36964 / BC36959，以及 `#Load` 的 BC36967 / BC37002）入台账，对齐 `Errors.vb:1601-1605` 的 fork 错误码策略；③ `spec-scripting-dialect.md:231-239` 的脚本诊断表补 BC36959 与 BC37002。
14. **补证据链与边界**：§5 补引运行时加载实锤（`ScriptBuilder.cs:142-151` 遍历 bound reference manager 注册依赖，N 的每个引用都会 `RegisterDependency`）；spec 引一句 AOT/trimming 边界（`#R` 不新增摩擦，脚本提交不参与 AOT，M5）。
15. **Unresolved 纪律**：7 条 Unresolved 在 spec 定稿时全部落进正文 / Alternatives / Considerations，不得以 Unresolved 形态随 spec 发布（`spec\README.md:113`）。

### Implication:

- **定性面**：本提案是**记录型**——把 `#R` 的既有机制钉成 spec 基线。会议结论是「机制成立、基线需补」，不是「方案待定」；提案正文不改，修正全部以本 RESOLUTION 为准。
- **规范面**：spec 新增/加强五块——别名语义（含无逃生口与 `/nostdlib` 消费者）、解析顺序的精确判据与规范形、N 展开的公共观察点与去重交互、头部指令分类与 disabled 区条文、空操作数「镜像 C#」的有意行为。
- **回归面**：别名行为已有符号测试锁定（`Compilers\VisualBasicSymbolTest\SymbolsTests\AssemblyAndNamespaceTests.vb:531-565`）；但 `#R` 的语言面（位置 / 模式门控 / 声明表收集 / 脏标记）在 VB 侧**没有诊断级测试**——`Scripting\VisualBasicTest` 的 5 个测试文件用到 `#R`，覆盖的是 nuget 展开、路径形态与零回归，不是 BC36964 / BC36959 本身。spec 化时补诊断测试。
- **互操作面**：N 展开使公共 `Compilation.DirectiveReferences` / `References` 携带闭包（共享面，休眠区）；`upstream-merge.md` 2.9 与公共 API 文档同步补记，防合并时被上游静默改回。
- **记录面**：别名改动与 `#R` 错误码入台账；脚本诊断表补两枚码；BC36959 的归属待与基准 commit diff 确认。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTION`（`Suspect`）：**BC36959 的归属**——`Errors.vb:1589-1591` 与 `:1601-1605` 两处 fork 注释把 36959 列为 fork 占位空隙，13 份 VB xlf 无该 trans-unit；与提案「本 fork 未改 `#R` 语法面」张力。收口动作：与 `upstream-merge.md` 的基准 commit `0e401fcf` 做 diff；若确为 fork 新增，`upstream-merge.md` 补条目、提案 §1 的限定同步进 spec。
- `OPEN QUESTION`：U1 的最终取舍——「镜像 C# 编译器（空串 = 无操作）」与产品「不静默失败」原则之间的取舍；若改为诊断，需给出偏离 C# 的理由与 `langversion` 门控形态。
- `OPEN QUESTION`：同一脚本在两个入口（源码面 `#R` 与启动面 `@vbi.rsp` 的 `/r:`）下引用面可能不同——spec 是否要求两入口的引用集合一致，或明示差异并给诊断。
- `OPEN QUESTION`：`ReferenceDirectiveMap` 的键在 `FilePath` 为空时（内存脚本 / REPL 提交）的语义——去重键是 `(Location.SourceTree.FilePath, File)`（`:815`、`:847`），无路径时键退化，spec 需给键语义。
- `TODO`：把别名改动与 `#R` 错误码记入 `upstream-merge.md`；`spec-scripting-dialect.md:231-239` 诊断表补 BC36959 / BC37002。
- `TODO`：补 `#R` 语言面的诊断测试（BC36964 模式门控、BC36959 位置、`#R ""` 空操作数、disabled 区内 `#R`）。
- `Follow-up`：上游若日后以其它形状真实现单 `#r`→N（`// TODO: implement`），按上游形状对齐并回退本 fork 改法（`upstream-merge.md` 2.9 的合并前评估义务）。
- `Suspect`：`Syntax.xml:9544-9550` 之外的语法面锚点（`ParseConditional.vb:82-83` 的分派表、`Errors.vb:1591` 的码值）逐行核过；`Syntax.xml` 的节点定义只按提案锚点采信，未见矛盾。

### 状态

- **LDM 状态：Active**。
- **三态判定：Active**——机制是 Roslyn scripting 既有实现的复用与 fork 完成，方向无悬念（两路独立复核一致：VB 基因「附带条件支持 / 置信度高」、C# 生态「附带条件支持 / 置信度中」）；提案 §1–§9 锚点逐一复核，绝大多数属实，机制随 1.2 在册（**已采纳**）。会议裁定的修正集中在：§6 / U4 别名范围扩写（无逃生口 + `/nostdlib` 消费者 + 语义表述）、§5 / U3 公共观察点订正、U2 去重交互扩条、U5/U6 升级为「钉死 + 规范形」、U1 收口为「镜像 C#」的有意行为、补 disabled 区与头部指令分类、C# 对照表改引并补门控、记录卫生三件——全部为 spec 化可交付的收口，不构成设计阻塞。**剩余未定项**（BC36959 归属、U1 最终取舍、双入口引用面一致性、`ReferenceDirectiveMap` 键语义）均已带证据锚与收口动作。下一阶段是 **spec 化**（`InternalDevDocs\spec\spec-reference-directive.md`）——本单元是记录型，能力已在册、无实现任务；RESOLUTION 中的实施项（诊断测试、台账登记、诊断表补码）另立跟踪。
- **独立五维评审为不入库工作材料**（git-ignored），仅供主持人/规划内部使用，不混入官方会议记录。
