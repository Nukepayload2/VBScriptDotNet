# `#R` 引用指令 / Reference Directive

* [x] Proposed
* [x] Prototype: Complete（上游 Roslyn VB 既有机制：`ReferenceDirectiveTriviaSyntax` + 声明表收集 + 宿主注入 resolver；本 fork 未改 `#R` 语法面，见 `upstream-merge.md` 二）
* [x] Implementation: Complete（`#R` 随 1.2 发布，`proposals\README.md:74`；单 `#R`→N 的 fork 改动已登账 `upstream-merge.md` 2.9，测试见 `Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb`）
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`#R "字符串"` 是 VBScript.NET 脚本**源码**层唯一的程序集引用机制（REPL 启动面另有 `@vbi.rsp` 的 `/r:`，见 §9）。它在语法上是**编译期指令 trivia**（`ReferenceDirectiveTriviaSyntax`，`SyntaxKind.ReferenceDirectiveTrivia = 750`），只在 `SourceCodeKind.Script` 下合法，且必须出现在编译单元首个 token 之前；每棵语法树的 `#R` 被汇总进 declaration table，并作为「引用可能变化」的脏标记参与增量重绑定（`HasReferenceDirectives` 本身也要求 Script）。操作数的**字符串解释**不由 VB 语言规定，而由宿主注入的 resolver 决定——脚本 runner 用命令行 resolver 解析任意路径/裸名，普通编译只允许命中命令行已给的引用（`ExistingReferencesResolver`）。宿主解析顺序为：`nuget:` 前缀分支 → 按路径解析（含 TPA 裸名优先）→ 非路径则 GAC 与程序集显示名/TPA。一条 `#R` 可以展开为 N 个引用（fork 改动，索引 0 = 主资产、其余 = 依赖闭包），N 的完整集经前序提交的 `ExplicitReferences` 跨提交继承；`<host>` / `<implicit>` 两处引用别名的语义依赖 fork 的 VB「尊重元数据引用别名」行为。`#R "nuget:…"` 与 `#R "project:…"` 两个前缀的语义各由独立提案承载，不在本文范围。

这套机制不是本 fork 的设计：它是 Roslyn scripting 的既有实现（C# scripting 的 `#r` 与之同源），1.2 起随 vbi 发布。本提案的职责是把**前缀之外的指令本体语义**钉成唯一事实基线，为两个前缀提案（nuget / project）与 spec 化提供共同底座。

## Motivation
[motivation]: #motivation

- **`#R` 是所有引用能力共同的底座。** `.vbx` 与 REPL 的**源码面**只有这一条指令（启动面另有 `@vbi.rsp` 的 `/r:`，见 §9）：本地 dll、TPA 裸名、`nuget:` 包、`project:` 工程引用最终都落到同一条 `#R` 操作数上。nuget 与 project 两份 Active 提案都已明确自己是「`#R` 字符串操作数的宿主/工具链解释」的相邻扩展（nuget 侧 `proposals\proposal-vbi-nuget-reference.md:102`；project 侧 `proposals\proposal-vbi-project-reference.md:17`、`:32`）。底座没有自己的事实基线，三份文档就会各自描述解析顺序与继承语义。
- **`#R` 已随 1.2 发布，但只有一句记录。** 1.2 版归档里 `#R` 的记载是「`#R`：引用程序集」一行（`proposals\vbx-1.2-beta\proposal-repl-directives.md:22`）。语法位置、收集机制、宿主契约、别名语义、跨提交继承这些**已被依赖的契约**都没有落文字。
- **fork 在共享 Core 上补了「单 `#R`→N」。** 上游对多引用展开留了 `// TODO: implement` 并直接 `throw new NotSupportedException()`；本 fork 把它实现为多引用展开（`upstream-merge.md` 2.9）。这条改动是 nuget/project 两个提案的继承面（`proposal-vbi-project-reference.md:32`「R4 继承」），必须有自己的规范表述。
- **别名语义与上游 VB 有意分叉。** `Script` 给 host/globals 程序集加 `<host>` 别名（`Scripting\Core\Script.cs:237-239`）、隐式补位引用加 `<implicit>` 别名（`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:27-29`）；二者只有编译器尊重别名才生效，而 fork 的 VB 用 `DeclarationsAccessibleWithoutAlias` 过滤（`Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:107-120`）。这是「脚本里某些类型为什么凭空不可见」的规范级原因。
- **C# 侧的同构与分叉需要对照。** C# 14 的 ignored-directives 提案在 Alternatives 里明写「`#r` is already supported by C# scripting and other existing tooling」（`csharplang\proposals\csharp-14.0\ignored-directives.md:114`），其位置约束（须在首个 token 之前）与 VB 的 `#R` 一致（同文件 `:59`）；而 C# 的包引用走 `#:` 忽略指令、VB 走 `#R "nuget:…"` 前缀——这条分叉需要写清楚。

## Detailed design
[design]: #detailed-design

> 证据等级标注：本节每个关键设计点后标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑）。锚点均在本 fork 源码中逐条复核，故为**已检查**；已随 1.2 发布或已登账的为**已采纳**。本阶段未运行测试，故不出现「已运行」/「有结果支撑」；测试存在性在 §5 注明。

### 1. 语法：`#R "字符串"` 是编译期指令 trivia

指令节点由 `Syntax.xml` 定义：`ReferenceDirectiveTriviaSyntax` 派生自 `DirectiveTriviaSyntax`，子节点为 `ReferenceKeyword` 与 `File`（类型 `StringLiteralToken`）（`Syntax\Syntax.xml:9544-9550`），枚举值 `ReferenceDirectiveTrivia = 750`（`Syntax\SyntaxKind.vb:3044`）。

解析入口在条件编译指令的分派处：`#` 之后是标识符时，按 `PossibleKeywordKind` 分派，`SyntaxKind.ReferenceKeyword` → `ParseReferenceDirective(hashToken, isFollowingToken)`（`Parser\ParseConditional.vb:82-83`）。该函数（`:454-472`）的行为：

- `R` 先被扫成 `IdentifierToken`，再经 `_scanner.MakeKeyword(identifier)` 转成 `ReferenceKeyword`（`:458-460`；`Scanner\TokenFactories.vb:316-325`）。VB 标识符/关键字匹配大小写不敏感，故 `#r` 与 `#R` 同义。
- **仅脚本合法**：`Not IsScript` 时给关键字挂 `ERR_ReferenceDirectiveOnlyAllowedInScripts`（`:462-463`；错误码 `Errors\Errors.vb:1597` = 36964）。
- **必须在首个 token 之前**：`isFollowingToken` 为真时挂 `ERR_PPReferenceFollowsToken`（`:464-466`；`Errors\Errors.vb:1591` = 36959）。该标志的判据是「该指令所属 trivia 不属于树的起始」——`_directiveIsFollowingToken = _leadingTriviaStartOffset > 0`（`Scanner\Directives.vb:41-43`）。
- **操作数必须是字符串字面量**：`VerifyExpectedToken(SyntaxKind.StringLiteralToken, file)`（`:468-469`）。

位置约束在语法树 API 上被固化：`CompilationUnitSyntax.GetReferenceDirectives` 只从编译单元的**首个 token** 的 leading trivia 取指令（`Syntax\CompilationUnitSyntax.vb:20-28`，注释「`#r` directives are always on the first token of the compilation unit」）。因此 `#R` 与 `#Load`、`#!` 一样，是脚本文件头部机制，不能出现在语句之后。

**证据等级：已检查。** `#R` 语法面为上游 Roslyn VB 既有实现，本 fork 的本地修改面清单（`upstream-merge.md` 二）无 `#R` 语法条目——`#!` 是 2.8 新增的兄弟分支，`#Load` 的 fork 改动落在宿主 `Scripting\Core`（2.5）。

### 2. 指令收集：declaration table 与增量重绑脏标记

每棵树的 `#R` 在**声明表构建期**被收集，而不是绑定期扫描语法树：

- `DeclarationTreeBuilder.GetReferenceDirectives`（`Declarations\DeclarationTreeBuilder.vb:101-113`）遍历 `compilationUnit.GetReferenceDirectives(filter)`，过滤条件是「该指令的 `File` 不含诊断且 `ValueText` 非空」（`:102-103`），然后为每条构造 `ReferenceDirective(File.ValueText, New SourceLocation(directiveNode))`（`:108-112`；`ReferenceDirective` 结构见 `Compilers\Core\Portable\MetadataReference\ReferenceDirective.cs:13-26`）。
- 收集**只在非 Regular 树发生**：`VisitCompilationUnit` 按 `_syntaxTree.Options.Kind <> SourceCodeKind.Regular` 分支，Script 分支调 `GetReferenceDirectives(node)`（`:174`、`:198`），Regular 分支固定为 `ImmutableArray(Of ReferenceDirective).Empty`（`:201`）。
- 结果挂在根声明上：`RootSingleNamespaceDeclaration.ReferenceDirectives`（`Declarations\RootSingleNamespaceDeclaration.vb:11-18`、`:26-37`，构造时 `Debug.Assert(Not referenceDirectives.IsDefault)`）。
- `DeclarationTable` 把各根声明的 directives 合并成 `ICollection(Of ReferenceDirective)`（`Declarations\DeclarationTable.vb:216-224`、`:278-281`）。

**脏标记**：`VisualBasicSyntaxTree.HasReferenceDirectives` = `Options.Kind = Script AndAlso GetCompilationUnitRoot().GetReferenceDirectives().Count > 0`（`Syntax\VisualBasicSyntaxTree.vb:85-91`）。编译对象在增删/替换语法树时累积它：

- `AddSyntaxTreeToDeclarationMapAndTable`：`referenceDirectivesChanged = referenceDirectivesChanged OrElse tree.HasReferenceDirectives`（`Compilation\VisualBasicCompilation.vb:1060`）；
- `RemoveSyntaxTreeFromDeclarationMapAndTable`：同式（`:1132`）；
- `RemoveAllSyntaxTrees`：以 `_declarationTable.ReferenceDirectives.Any()` 收尾（`:1140`）；
- 最终 `UpdateSyntaxTrees(... reuseReferenceManager:=Not referenceDirectivesChanged)`（`:571`）——**引用管理器是否复用，取决于本次改动是否可能改变 `#R` 集合**。这是一处粗粒度标记：`ReplaceSyntaxTree` 对任何一棵含 `#R` 的新旧树都判脏，即使两份 `#R` 完全相同（`VisualBasicCompilation.vb:1178-1180` 留有「比较新旧 `#r` 以复用」的 TODO）。

编译对象对外暴露两个读口：`ReferenceDirectives`（`_declarationTable.ReferenceDirectives`，`:1428-1432`）与 `ReferenceDirectiveMap`（按 `(path, content)` 键取元数据引用，`:1375-1379`）。

**证据等级：已检查。**

### 3. 解析语义：宿主注入的 resolver（宿主/工具链契约）

`#R` 的操作数**字符串解释不由 VB 语言定义**，而由 `VisualBasicCompilationOptions.MetadataReferenceResolver` 决定；该 resolver 的装配分两条路，由 `Arguments.IsScriptRunner` 二选一：

- **脚本 runner**：`referenceDirectiveResolver = commandLineReferenceResolver`——即完整命令行 resolver，可以解析任意路径/裸名（`Compilers\Core\Portable\CommandLine\CommonCompiler.cs:225-228`）。
- **普通编译（csc/vbc）**：`referenceDirectiveResolver = New ExistingReferencesResolver(commandLineReferenceResolver, resolved)`，注释写明「when compiling into an assembly (csc/vbc) we only allow #r that match references given on command line」（`:229-233`）。`ExistingReferencesResolver` 先解析，再按程序集标识过滤，只保留命中「构造编译时已给的引用」的结果（`Compilers\Core\Portable\CommandLine\CommonCompiler.ExistingReferencesResolver.cs:44-48`；类注释 `:17-21`）。

VB 命令行的接线在 `VisualBasicCompiler.CreateCompilation`：`ResolveMetadataReferences(diagnostics, touchedFilesLogger, referenceDirectiveResolver)` 取回 out 参数（`CommandLine\VisualBasicCompiler.vb:146-147`），再 `WithMetadataReferenceResolver(referenceDirectiveResolver)` 写进编译选项（`:170`）。

绑定期，Core 的引用管理器对每条 directive 调 resolver：`ResolveReferenceDirective(referenceDirective.File, referenceDirective.Location, compilation)`（`ReferenceManager\CommonReferenceManager.Resolution.cs:820`），实现里取 `basePath = tree.FilePath`（为空则 `null`），调 `MetadataReferenceResolver.ResolveReference(reference, basePath, MetadataReferenceProperties.Assembly.WithRecursiveAliases(true))`（`:892-902`）。注意属性带 `WithRecursiveAliases(true)`——`#R` 引入的引用默认允许递归别名。VB 侧 `Symbols\ReferenceManager.vb:299-313` 接收 `boundReferenceDirectiveMap` / `boundReferenceDirectives` 并交给 `ResolveMetadataReferences`。

**结论**：`#R` 是**编译器语法 + 宿主契约**的组合——语法层只保证「这是脚本头部的一条字符串指令」，字符串怎么变成程序集由宿主说了算。这也解释了为什么 `#R "nuget:…"` 不需要新语法（`proposal-vbi-nuget-reference.md:102`）。

**证据等级：已检查。** `#R` 随 vbi 发布并可用 → **已采纳**。

### 4. 宿主解析顺序

vbi 脚本/编译两路共用的 resolver 是 `RuntimeMetadataReferenceResolver`（`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs`）。`ResolveReference(reference, baseFilePath, properties)`（`:145-198`）的判定顺序是：

1. **`nuget:` 前缀分支**（`:147-155`）：`NuGetPackageResolver.TryParsePackageReference` 命中时——`PackageResolver != null` 则返回包资产数组（多资产即 N 个引用），`PackageResolver == null` 则**直接返回空数组**（`:197` 的 `return Empty`，不进入后面的分支）；只有前缀**不命中**才进入下面的分支。该前缀的语义属邻域提案（`proposal-vbi-nuget-reference.md`）。
2. **按路径解析**（`PathUtilities.IsFilePath(reference)` 为真，`:156-175`）。判据是「扩展名为 `.dll`/`.exe` **或**含目录分隔符」（`Compilers\Core\Portable\FileSystem\PathUtilities.cs:518-527`）——不是「是否含分隔符」。此分支内：
   - 若**不含**目录分隔符且 TPA（`TrustedPlatformAssemblies`）非空，先按文件名（去扩展名）查 TPA（`:158-165`）；
   - 再走 `PathResolver.ResolvePath(reference, baseFilePath)` 相对路径解析（`:167-174`）。`RelativePathResolver.ResolvePath` 依次尝试「相对 `baseFilePath`」「宿主 `BaseDirectory`」「`SearchPaths`」（`Compilers\Core\Portable\FileSystem\RelativePathResolver.cs:36-45` → `FileUtilities.ResolveRelativePath`）。
3. **非路径（程序集名形态）**（`:176-195`）：先 GAC（`:178-185`），再按程序集显示名解析并查 TPA（`:187-194`）。

基目录与搜索路径来自命令行参数：`CommandLineRunner.GetMetadataReferenceResolver` 把 `arguments.ReferencePaths` 与 `arguments.BaseDirectory` 传给 `RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:212-223`）；TPA 表由 `TRUSTED_PLATFORM_ASSEMBLIES` 环境数据构建（`RuntimeMetadataReferenceResolver.cs:203-206`）。

**缺失程序集补解析**走另一条路径：`ResolveMissingAssembly(definition, referenceIdentity)`（`:100-138`，`ResolveMissingAssemblies => True` 见 `:98`）的顺序是 **GAC（仅强名）→ TPA → 请求方引用所在目录**。它与 `ResolveReference` 的顺序不同（GAC 在前、且以请求方目录收尾），两者的顺序差别见 Unresolved 6。

**证据等级：已检查。** 裸名与 `.dll`（无目录分隔符）两种形态均已在样例中实际使用（`Samples\WpfCpuCoreInformation.vbx:1-3`、`Samples\CLAUDE.md` 的 `#R "Microsoft.WinUI"`）→ **已采纳**；含目录分隔符的路径形态由代码支持（§4 第 2 点判据），无样例锚点（与 §8「两种可复现」一致）。

### 5. 一条 `#R` 展开为 N 个引用（fork 改动）

上游对「一条 directive 解析出多个引用」是显式不支持（`references.Length > 1 → throw new NotSupportedException()`，附 `// TODO: implement`；台账 `upstream-merge.md` 2.9，nuget 提案记上游行号 `:883-887`）。本 fork 把它实现为**多引用展开**：

- directive 循环里 `ResolveReferenceDirective` 的返回值改为数组，逐条 push 进 `referencesBuilder`，每条都锚同一个 `#R` 的 location（`ReferenceManager\CommonReferenceManager.Resolution.cs:833-839`）。
- per-`#R` 的 map 保持**单值**：只存 `boundReferences[0]`，即**主资产**（the requested package itself）；后续条目是它的**依赖闭包**（`:841-847` 的注释）。因此 `ReferenceDirectiveMap` 与旧 singular API 的可见面不变，N 的完整集只经 `ExplicitReferences` 流动。
- `ResolveReferenceDirective` 去掉 `NotSupportedException`，返回 `ImmutableArray(Of PortableExecutableReference)`；0/1 输入路径与旧版一致（`IsDefaultOrEmpty → Empty`）（`:878-902`，其中 `:883-891` 的 remark 说明「多引用是新路径、旧 singular 消费者不变、只在 N>1 激活、纯加性」）。

**继承**：N 的完整集在前序提交的 `ExplicitReferences` 里被全量追加（`:853-858`），因此后续提交无需重复 `#R` 即可见到闭包（§7）。

**测试**：`Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb` 锁定两个场景——Test 1（`:109-142`）验证 `#R "nuget:X"` 展开出 [主资产, 闭包]，闭包类型只在**第二条提交**被源码使用仍可绑定；Test 2（`:144-200`）验证两条 `#R` 各自的坏资产把 `BC31519` 锚到**自己的行**（第 0 行 / 第 1 行），且不影响同一脚本里的合法主资产/闭包/普通 `#R`。测试全程内存程序集，无磁盘写/进程/网络。

**证据等级：已检查**（测试文件与实现均已读，本阶段未运行）；fork 改动已登账 `upstream-merge.md` 2.9 → **已采纳**。

### 6. 引用别名：`<host>` 与 `<implicit>`

两处别名都由 Scripting 宿主在构造引用时设置，其**唯一目的都是隐藏类型**：

- **`<host>`**：host/globals 程序集用 `MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("<host>")).WithRecursiveAliases(true)`（`Scripting\Core\Script.cs:237-239`），在 `GetReferencesForCompilation` 中挂到 globals 程序集上（`:259-269`）。注释写明目的是「hide its namespaces and global types behind it」，即让宿主对象类型的命名空间与全局类型不进入脚本的裸名查找。
- **`<implicit>`**：隐式补位引用（`ResolveMissingAssembly` 解析出来的）用 `WithAliases(ImmutableArray.Create("<implicit>"))`（`RuntimeMetadataReferenceResolver.cs:27-29`，经 `CreateResolvedMissingReference` `:140-143` 应用）。

**生效前提是编译器尊重别名。** fork 的 VB 在命名空间合并时用 `referenceManager.DeclarationsAccessibleWithoutAlias(i)` 过滤：只有「无别名」或「别名含 `global`」的引用才把其模块的全局命名空间并入（`Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:107-120`；判据 `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs:721-725`：`aliases.Length = 0 OrElse aliases.IndexOf(GlobalAlias) >= 0`）。该处的注释明写「This mirrors C# (extern aliases) and lets /nostdlib on .NET Core reference the real core library (System.Private.CoreLib) without leaking its entire type surface into the global namespace」（`MergedNamespaceSymbol.vb:107-110`）。原版上游 VB 忽略别名，本 fork 有意尊重（这是 fork 基座带入的上游 2026 行为，`Scripting` 层依赖它）。

**效果**：`<host>` 让 globals 类型的命名空间/全局类型不出现在脚本裸名里；`<implicit>` 让隐式补位的程序集只用于满足程序集引用、其类型不并入全局命名空间（避免与显式引用产生歧义）。这也解释了「为什么某个类型明明被加载了却不能用裸名访问」。

**证据等级：已检查。**

### 7. 跨提交引用继承 vs `Imports` 累积

两条机制常被混说，此处钉死术语：

- **引用是继承。** 编译链在绑定引用时把前序提交的 `ExplicitReferences` 全量追加（`CommonReferenceManager.Resolution.cs:853-858`），并避免对「前序提交已隐式补位过的引用」重复解析（`Symbols\ReferenceManager.vb:325-328`）。N 展开的闭包正是靠这条路径跨提交存活（§5）。
- **`Imports` 是累积，不是继承。** `Script.InheritOptions` 明确把 references 与 imports 都清空（`Scripting\Core\Script.cs:133-139`，注释「don't inherit references or imports, they have already been applied」），`ContinueWith` 默认使用它（`:125-131`）；`Imports` 由宿主在每条提交的编译选项里重放整条链的 imports 补回（机制见 `proposal-scripting-dialect.md` §6 与 `Scripting\VisualBasic\VisualBasicScriptCompiler.GetGlobalImportsForCompilation`）。

因此 spec 里应写「引用沿提交链继承、`Imports` 由宿主跨提交累积」，不得写成「继承 Imports」。

**证据等级：已检查。**

### 8. 可运行示例

`.vbx` 文件（三种操作数形态中可在任意机器复现的两种；`System.Text.RegularExpressions` 在 .NET 共享框架内）：

```vbx
' demo-reference.vbx —— #R 的两种常见操作数形态
#R "System.Text.RegularExpressions"      ' 裸名：GAC 未命中 → 按程序集显示名查 TPA
#R "System.Text.RegularExpressions.dll"  ' 无目录分隔符 + .dll：先按文件名查 TPA，再回落相对路径

Imports System.Text.RegularExpressions

Dim m = Regex.Match("vbi 2.0", "\d+\.\d+")
Console.WriteLine(m.Value)               ' 2.0
```

REPL 里同一机制逐条提交（`#R` 属 1.2 已发布的指令系统）：

```text
> #R "System.Text.RegularExpressions"
> Imports System.Text.RegularExpressions
> Regex.Match("vbi 2.0", "\d+\.\d+").Value
"2.0"
```

`Samples\WpfCpuCoreInformation.vbx:1-3` 是本机 dll（无目录分隔符、经 `IsFilePath` 走路径分支）形态的真实样本：

```vbx
#R "PresentationCore.dll"
#R "PresentationFramework.dll"
#R "WindowsBase.dll"
```

**证据等级：已检查**（样例文件存在；本阶段未执行）。

### 9. 与其它单元的分工

本提案只写**前缀之外的 `#R` 指令本体语义**：语法与位置约束（§1）、收集与脏标记（§2）、宿主 resolver 契约（§3）、宿主解析顺序（§4）、N 展开（§5）、别名（§6）、跨提交继承（§7）。以下不在本文重复：

- **`#R "nuget:…"`** —— `proposals\proposal-vbi-nuget-reference.md`（Active）。
- **`#R "project:…"`** —— `proposals\proposal-vbi-project-reference.md`（Active）。
- **`#Load` / `#!`** —— `#Load` 是源文件加载（宿主侧，`upstream-merge.md` 2.5、2.16）；`#!` 见 `proposals\proposal-shebang-directive.md` 与 `spec\spec-shebang-directive.md`。
- **1.2 指令系统全表**（`#help`、`/version`、`@vbi.rsp` 等）—— `proposals\vbx-1.2-beta\proposal-repl-directives.md`；其中 `@vbi.rsp` 含 `/r:`（启动面引用，同文件 `:25`）。
- **脚本声明/提交模型**（script class、`PreviousScriptCompilation`、跨提交可见性）—— `proposals\proposal-scripting-dialect.md`。

## Drawbacks
[drawbacks]: #drawbacks

- **同一份 `#R` 在两种宿主下语义不同。** 脚本 runner 的 resolver 能解析任意路径/裸名，普通编译的 `ExistingReferencesResolver` 只认命令行已给的引用（§3）。同一段脚本被搬进 `.vbproj` 编译时，`#R` 会失败或静默指向另一份程序集——这是「脚本」与「工程」两条引用面的固有落差，不是可修的 bug。
- **裸名解析依赖运行宿主进程的 TPA/GAC。** 同一个 `#R "Microsoft.WinUI"` 在不同机器/不同宿主下解析结果可能不同（§4）；脚本的可移植性因此受宿主环境约束，而不是脚本自包含。
- **相对路径以脚本文件路径与宿主 `BaseDirectory` 为基准。** 脚本移动位置或从别的目录启动 vbi，`#R ".\libs\X.dll"` 的解析结果会变（§4）。
- **N 展开让「一条 directive 对应几个引用」不再是一一关系。** 单条 `#R` 的多条引用共享一个 location（§5），诊断锚定因此需要按 directive 聚合；`ReferenceDirectiveMap` 只暴露主资产，工具侧看到的引用集与真实编译引用集不等。
- **别名语义依赖 fork 对上游 VB 的分叉。** `<host>` / `<implicit>` 只有编译器尊重别名才生效（§6），而原版上游 VB 忽略别名。合并上游时若该行为被回退，脚本的裸名可见性会整片改变，且症状是「类型凭空出现/消失」而非编译错误。
- **脏标记是粗粒度的。** 任何含 `#R` 的树被替换都会导致引用管理器不复用（§2），IDE 里每次编辑脚本都可能重建引用绑定；上游留下的「比较新旧 `#r`」TODO 尚未实现。

## Alternatives
[alternatives]: #alternatives

- **让宿主剥行、把 `#R` 重写成命令行 `/r:`。** 会整体漂移诊断行号、让宿主重复实现解析与位置判定，且编辑器/工具看不到同一棵树。与 `proposal-shebang-directive.md` 对「宿主剥行」的否决同构；且 `#R` 的「指令即 trivia、随树可查」正是声明表收集（§2）成立的前提。
- **让 `#R` 在普通编译里也解析任意路径/裸名。** 未采用：上游为此专门加了 `ExistingReferencesResolver`（§3），目的是让脚本并入工程时不会偷偷引入工程引用之外的依赖。放开会让「工程引用一致性」失效，属破坏性变化。
- **保留上游的单引用限制（`NotSupportedException`）。** 未采用：nuget 与 project 两个前缀各自都需要单条 `#R` 一次展开为 N 个引用（nuget 侧 `proposal-vbi-nuget-reference.md:3`「扩展共享 Core 支持单 `#R`→N 引用」；project 侧 `proposal-vbi-project-reference.md:15`「共享 Core 单 `#R`→N…让单条 `project:` 自含主产物 + 闭包」）。fork 的多引用展开对 0/1 输入休眠、旧 singular API 不变（§5），因此是纯加性改动。
- **不做别名、让 globals 与隐式补位引用直接并入全局命名空间。** 未采用：`<host>` 的存在是为了让宿主对象成员不被裸名泄漏（`Script.cs:237` 注释），`<implicit>` 是为了让隐式补位的程序集不产生歧义（§6）。去掉别名会改变脚本的名字解析结果。
- **把 `#R` 收集从声明表挪到绑定期。** 未采用：声明表在 `VisualBasicCompilation` 构造期就可判定「引用集合是否可能变化」（§2），这是增量重绑复用引用管理器的唯一判据；挪到绑定期会让每次编译都要重绑引用。
- **用 `#Load` 代替 `#R` 做程序集引用。** 不成立：`#Load` 加载的是**源文件**并并入同一编译，不引入程序集引用；两者是不同机制（`upstream-merge.md` 2.5）。

### 与 C# 的对照

C# scripting 的 `#r` 与 VB 的 `#R` 同源：C# 14 的 ignored-directives 提案在 Alternatives 里把「单一指令包办包引用」列为候选时，明写 `#r` 已由 scripting 支持——原文「For example, `#r` is already supported by C# scripting and other existing tooling.」（`csharplang\proposals\csharp-14.0\ignored-directives.md:114`，位于 `### Other syntax forms` → `#### Single directive`）。该提案最终选择的 `#:` 前缀是**语言忽略、工具消费**的指令，其位置约束与 VB 的 `#R` 一致：「Ignored directives must occur before the first token (§6.4) in the compilation unit, just like `#define`/`#undef` directives.」（同文件 `:59`）。

| 维度 | C# | VB（本 fork） |
|---|---|---|
| 脚本引用指令 | `#r "…"`（C# scripting） | `#R "…"`（`ReferenceDirectiveTriviaSyntax`，§1） |
| 包引用形态 | 语言层 `#:package id@version`（ignored directive，工具消费）；scripting 的 `#r "nuget:…"` 仍存在 | `#R "nuget:包名[, 版本]"` 前缀（`proposal-vbi-nuget-reference.md`） |
| 位置约束 | 须在首个 token 之前（`ignored-directives.md:59`） | 同（§1，`ERR_PPReferenceFollowsToken`） |
| 语言是否解释操作数 | 否（`#r` 由 scripting 宿主解释；`#:` 语言直接忽略） | 否（宿主注入 resolver 解释，§3） |

这张表就是「`#R` 是宿主/工具链契约而非 VB 语言语义」的对照含义：C# 与 VB 都把操作数的解释权交给宿主，差别只在指令拼写与包引用前缀。

## Unresolved questions
[unresolved]: #unresolved-questions

以下均为**已实现能力在 spec 化时尚未覆盖的边界**，不是设计未定：

1. **操作数字符串的文法未 spec 化。** `#R` 的值是 `StringLiteralToken`（§1），但「值文本如何被解释」在 spec 里没有条文：转义序列是否按 VB 字符串字面量规则处理、首尾空白是否 Trim、`#R ""`（空串）是忽略还是报错。当前实现只过滤「含诊断或 `ValueText` 为空」的 directive（`DeclarationTreeBuilder.vb:102-103`），空串因此被**静默丢弃**而非报错——spec 需明确这是有意行为还是应补诊断。
2. **重复 `#R` 的去重键在 `FilePath` 为空时未定义。** 去重键是 `(referenceDirective.Location.SourceTree.FilePath, referenceDirective.File)`（`CommonReferenceManager.Resolution.cs:815`、`:847`）。同一条 `#R` 出现在两份不同路径的脚本里不会被去重；而内存脚本（无 `FilePath`）的键语义、以及同一提交内两条相同 `#R` 是否应只解析一次，spec 均未覆盖。
3. **N>1 时 `ReferenceDirectiveMap` 的契约。** 该 map 只保留主资产（§5），但它是 `Friend` API（`VisualBasicCompilation.vb:1375-1379`），未来 IDE/工具若用它枚举「本提交的引用」会漏掉闭包。spec 需明确：该 map 是「每条 directive 的主资产」而非「本提交的完整引用集」，并说明闭包只能经 `ExplicitReferences` 观察。
4. **`<host>` / `<implicit>` 的语义在规范层没有文字。** 二者的效果（隐藏命名空间/全局类型）目前只由 `Script.cs:237-239`、`RuntimeMetadataReferenceResolver.cs:27-29` 的注释与 `MergedNamespaceSymbol.vb:107-110` 的注释表达。spec 需给出别名语义的规范表述（哪些别名存在、各隐藏什么、与 `global` 别名的关系），否则「类型凭空不可见」无法从规范推导。
5. **裸名/路径的分支判据在 spec 里未固化。** `ResolveReference` 的分支谓词是 `PathUtilities.IsFilePath`（`.dll`/`.exe` 扩展名**或**含目录分隔符，`PathUtilities.cs:518-527`），与直觉的「是否含路径分隔符」不同：`#R "System.Text.RegularExpressions"` 的「扩展名」是 `.RegularExpressions`，走的是非路径分支（GAC → 显示名 → TPA）。spec 需给出精确判据与各分支的完整顺序，而不是举例说明。
6. **两条解析路径的顺序不一致，spec 需分别固定。** `ResolveReference`（`:145-198`）与 `ResolveMissingAssembly`（`:100-138`）的顺序不同：前者在「非路径」分支先 GAC 再 TPA、在「路径」分支先 TPA 再相对路径；后者是 GAC → TPA → **请求方引用所在目录**。`#R` 到底走哪条（`#R` 是 `ResolveReference`，但被引用的程序集的依赖走 `ResolveMissingAssembly`），以及两条顺序是否需要统一，spec 应明确。
7. **`HasReferenceDirectives` 的粗粒度语义。** 该标记只回答「这棵树里有没有 `#R`」（`VisualBasicSyntaxTree.vb:85-91`），因此任何含 `#R` 的树被替换都会让引用管理器不复用（§2，`VisualBasicCompilation.vb:571`）。上游留有「比较新旧 `#r` 是否相同」的 TODO（`:1178-1180`）。spec 需说明这是有意的保守策略（正确性优先）还是应作为已知性能边界记录。

## 相关文档

- `proposals\vbx-1.2-beta\proposal-repl-directives.md` — 1.2 指令系统归档（`#R` 的一句记录，本文的前身）
- `proposals\proposal-vbi-nuget-reference.md` — `#R "nuget:包名[, 版本]"` 前缀（邻域，Active）
- `proposals\proposal-vbi-project-reference.md` — `#R "project:<工程路径>"` 前缀（邻域，Active）
- `proposals\proposal-scripting-dialect.md` — 脚本方言的声明与提交模型（跨提交继承与 `Imports` 累积的背景）
- `proposals\proposal-shebang-directive.md` / `spec\spec-shebang-directive.md` — `#!` 指令（同属脚本文件头部机制）
- `upstream-merge.md` 2.9 — 共享 Core 单 `#R`→N 的本地修改面台账（补上游 `// TODO: implement`）
- `upstream-merge.md` 2.5 / 2.16 — `#Load` 的宿主侧改动（与 `#R` 同层机制的对照）
- `compilers-index.md` — 编译器目录地图（VB-BIND / CORE-SYMBOLS-META 定位用）
- `csharplang\proposals\csharp-14.0\ignored-directives.md` — C# 14 ignored directives（Alternatives 的 `#r` 与位置约束对照）
