# 上游 Roslyn 合并账本

本文件记录 VBScriptDotNet 编译器 fork 与上游 dotnet/roslyn 的合并基准与修改面，保证后续低成本跟随上游。**维护者每次合并前先读本文件，合并后更新本文件。**

## 一、上游基准

| 字段 | 值 |
|------|-----|
| 仓库 | `{{Roslyn}}`（本机值见 `portal.local.md`；origin = dotnet/roslyn） |
| 基准 commit | `0e401fcf66cbfd4aeb27a78408ab91cab3a6f207` |
| 基准日期 | 2026-07-27 |
| 基准分支 | `release/stable`（[release/stable] Snap insiders to stable (#84648)） |

> 相关参照：`{{VbLs}}` 的 `roslyn.json` pin 在 `87e31f80d4cc6b9fb236828df8af28a3bd42ee55`，是 vb-ls 的 vendored roslyn 快照，与本 fork 基准不同，仅作参考。
> 注意：上游基准 ≠ 本 fork 的裁剪基线。本 fork 的 `Compilers\` 是**修剪过的 Roslyn 编译器树**（见 `compilers-index.md` 版本快照），非完整上游镜像。

## 二、本地修改面清单

以下文件/区域是 fork 相对上游的本地改动。按「新增 / 修改」分类。证据列给出文件:行号或 commit；修改面外文件可直接跟随上游。

### 2.1 脚本扩展名与交互模式（修改）

- `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:61-63` — `ScriptFileExtension` 改为 `.vbx`。
- 同文件 `:1522-1523` — `\i` 交互模式选项 / 无参启动进入交互式 / `vbi script.vbx` 直接执行。

### 2.2 脚本编译链（修改）

- `Compilers\VisualBasic\Portable\Compilation\VisualBasicScriptCompilationInfo.vb` — `PreviousScriptCompilation` 链。
- `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:345-368` — `CreateScriptCompilation`；`:712`/`:723` 引用复用；`:839` `HasSubmissionResult`（脚本末尾表达式判定，支撑 REPL 打印与退出码）。

### 2.3 byref-like / ref struct 支持（修改，跨 Symbols 与 Analysis）

- `Compilers\VisualBasic\Portable\Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb:96-154` — `IsRefLikeOrAllowsRefLikeType()` 装箱/捕获检查。
- `Compilers\VisualBasic\Portable\Symbols\ConstraintsHelper.vb`、`Symbols\Metadata\PE\PENamedTypeSymbol.vb`、`Symbols\Source\SourceModuleSymbol.vb`、`Symbols\Source\SourceNamedTypeSymbol.vb`、`Symbols\Source\SourceTypeParameterSymbol.vb`、`Symbols\TypeSymbol.vb` — ref struct / allows ref struct 相关符号逻辑。
- 对应设计：`spec\spec-byref-like-safety.md`；规则来源 `{{VBRefStructHelper}}`（BCX 系列错误码）。注意：编译器层面尚未 suppress ref struct obsolete error（见 `decisions.md` D1）。

### 2.4 REPL 裸表达式自动打印（optional-question-prefix，修改）

- `Compilers\VisualBasic\Portable\Parser\ParseStatement.vb:1115-1154` — `ExpressionStatement` / `ParseScriptExpressionStatement` / `MakeInvocationExpression`（裸表达式语句 + 方法组形状）。
- 绑定层方法组消歧与 `IsFinalStatementOfSubmission`：`Compilers\VisualBasic\Portable\Binder\Binder_Statements.vb`（`BindExpressionStatement` / `ReclassifyInvocationExpressionAsStatement`）。
- `VisualBasicCompilation.vb` `HasSubmissionResult` 方法组感知（Sub→False / Function→True）。
- 对应设计：`spec\spec-optional-question-prefix.md`；实施样本 `tasks\optional-question-prefix\`。

### 2.5 #Load 指令（修改）

- 脚本宿主 `Scripting\Core\`（common scripting fork）：`#Load` 指令解析与并入脚本编译。
- 编译器层 Script submission 链：`VisualBasicCompilation.vb` `CreateScriptCompilation`。

### 2.6 顶层代码 / 脚本提交语义（修改）

- `SourceCodeKind.Script` 支撑 `.vbx` 与交互提交；`Return` 按 `Function Main` 语义处理（typed submission → vbx exit code）。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` — `RunScriptAsync` 用 `CreateInitialScript<int>` 直接返回退出码（2.0 修复，见 `spec\README.md` 版本历史）。

### 2.7 新增（相对上游的新文件/项目）——**先区分「上游目录」与「真 fork 新增」**

**判据**：对任一 fork 路径 `X` 取 `git cat-file -e <基准commit>:src/X`（基准 commit 见 §一；上游树路径前缀为 `src\`，本 fork 已把 `src\` 摊平到仓库根）——**rc=0 ⇒ 上游原有**，**rc=128 ⇒ fork 新增**。

**复跑前提（可用性告警；复跑 2026-09-24）**：本 fork 仓**不持有**基准 commit 对象（`git cat-file -t <基准commit>` 报 `could not get object info`）⇒ 上述 `cat-file -e` 判据在本仓内**当前为假阴性通道**：任一路径都返回 128，含本节 (a) 自记 rc=0 的 `src\Scripting\Core\ScriptOptions.cs` ⇒ **不得据其判「fork 新增」**。分类改在同版本的 `{{Roslyn}}` 仓（其 HEAD 即基准 commit）以 `git ls-tree` 核对，实测读数见 §2.24「判据」条。**换机器或取到基准对象后，本节与 §2.24 的分类都须复跑核对。**

**目录级的「新增」是伪分类**：一个目录在上游树内、其内部仍有 fork 新增文件，两者必须分开记。

**（a）上游目录（目录本身在上游树内；fork 只在其内增删文件 ⇒ 合并时必须逐文件比对上游改动）**

- `Scripting\Core\`、`Scripting\VisualBasic\` — **上游目录**，**不是**「本 fork 新增的目录」。
  上游同名文件在册：`src\Scripting\Core\ScriptOptions.cs`、`src\Scripting\VisualBasic\VisualBasicScript.vb`、
  `src\Scripting\VisualBasic\VisualBasicScriptCompiler.vb`、两个 `.vbproj`/`.csproj`（`cat-file -e` 均 **rc=0**）。
  **目录内 fork 新增文件**（`rc=128`，共 16 个）：
  - `Scripting\Core\`：`Hosting\CommandLine\INuGetRestoreCoordinator.cs`（§2.12）、
    `Hosting\AssemblyLoader\NativeLibraryProbe.cs`（§2.13）、`CoreLightup.cs`、`ScriptingResources.Designer.cs`。
  - `Scripting\VisualBasic\`：`Hosting\NuGetJson.vb`、`NuGetMissingNativeAssetsDetector.vb`、
    `NuGetPackageResolverImpl.vb`、`NuGetPackageSession.vb`、`NuGetProjectGenerator.vb`、`NuGetRestoreAssets.vb`、
    `NuGetRestoreCache.vb`、`NuGetRestoreCoordinator.vb`、`NuGetRestoreDiagnostics.vb`、`NuGetRestorePolicy.vb`、
    `NuGetRestoreRunner.vb`（§2.10 / §2.12 / §2.13 / §2.14）、`VBScriptingResources.Designer.vb`。
  - **其中 3 个不是 fork 自己写的**：`CoreLightup.cs` 与两份 `*.Designer.*` 是整目录引入时的**旧快照残留**。
    本 fork 的 `Scripting\` 由 commit `a5e5286`（`Add source code of Microsoft.CodeAnalysis.Scripting (VS 17.6)`）
    整体引入（**早于基准 commit，且此后未随上游刷新** ⇒ 该目录内未被本 fork 改动过的文件停在旧上游版本，
    合并时按**大跨度 3-way** 处理，不能假定「与基准逐字一致」）；`CoreLightup.cs` 上游已于
    `59981258f6d`（`Remove assembly loading light-up from scripting (#74409)`，**经 `merge-base --is-ancestor`
    实锤是本基准 commit 的祖先**）删除；`*.Designer.*` 上游已改为构建期生成（不入库）。⇒ 合并时按上游删/不追踪处理。
    （三态：**实锤**——`cat-file -e` 判据 + 两侧 `git log` 引用；「早于基准且未刷新」为**实锤**，
    由 `git log -- Scripting/Core/ScriptOptions.cs` 只命中 `a5e5286` 得出。）
- `Interactive\vbi\` — **上游目录**（`src\Interactive\vbi\` 在册：`App.config`、`Vbi.vb`、`vbi.coreclr.rsp`、
  `vbi.desktop.rsp`、`vbi.vbproj`，`cat-file -e` 均 rc=0）。**目录内 fork 新增文件**（3 个）：
  `Vbi.Compile.vb`、`app.manifest`、`My Project\PublishProfiles\FolderProfile.pubxml`。
- `Workspaces\SharedUtilitiesAndExtensions\Compiler\` — **上游目录**（`src\Workspaces\SharedUtilitiesAndExtensions\Compiler\`
  在册）。本 fork 只保留其 `Core\` 子树（393 个文件**全部**是上游原有路径，逐条 `cat-file -e` rc=0，
  **零 fork 新增文件**，含 `Core\CompilerExtensions.shproj` / `.projitems` 亦为上游原有）；
  上游同级的 `CSharp\` / `Extensions\` / `VisualBasic\` 被裁剪。⇒ 合并按「裁剪 + 跟随上游」处理，无本地分叉。

**（b）真 fork 新增（上游树内无对应路径，rc=128）**

- `Samples\` — 样例脚本（12 个文件，全部 `rc=128`；上游全树**无**名为 `Samples` 的目录）。
- `Installer\` — 分发工程（`StoreAssets\` / `Toolset\` / `vbichooser\` / `vbicore\` / `vbifw\` /
  `VBInteractive.WindowsDesktop.Installer\`，80 个文件，全部 `rc=128`；`vbicore` / `vbifw` / `vbichooser` /
  `VBInteractive` / `StoreAssets` 五个名字在上游全树 grep **零命中**）。
- 另两个同属真新增的面：`InternalDevDocs\`（上游树内 **rc=128**，全树无此目录）、
  `Scripting\VisualBasicTest\` 下的 fork 新增测试文件（`Scripting\` 的 43 条 `rc=128` 路径中，除上列 16 个
  产品/生成文件外的 27 个测试与 Helpers 文件——该数为 **2026-09-11 清点读数**，现值见 §2.24（b））。

> 合并前读本节时，**目录名在册 ≠ 目录是新增**：上游在册的目录里照样可以有 fork 新增文件，冲突面按**文件**判，不按目录名判（`Scripting\Core\` / `Scripting\VisualBasic\` 即属此类，见 (a)）。

### 2.8 #! shebang 指令（新增）

新增文件：

- `Compilers\VisualBasic\Portable\Syntax\ShebangDirectiveTriviaSyntax.vb` — `Content`/`WithContent` 便利 API（解释器路径作 `StringLiteralToken`）。
- `Compilers\VisualBasic\Portable\Syntax\Syntax.xml` 新节点 `ShebangDirectiveTriviaSyntax`（`DirectiveTriviaSyntax` 派生，child: `ExclamationToken`），3 个 `Generated\Syntax.xml.Syntax/Main/Internal.Generated.vb` 经基线 VBSyntaxGenerator 再生成（零删除、纯新增）。
- `Compilers\VisualBasic\Portable\PublicAPI.Unshipped.txt` — 节点 + visitor + `SyntaxFactory.ShebangDirectiveTrivia` + Content/WithContent 增量（`Shipped.txt` 字节不变）。

修改文件：

- `Compilers\VisualBasic\Portable\Parser\ParseConditional.vb` — 派发 `Case SyntaxKind.ExclamationToken` + `ParseShebangDirective`（`IsScript` 门控 / 位置 0 / 整行消费）+ `ConsumeShebangContentAsTrailingTrivia`（`SkippedTokensTrivia` 尾随）。
- `Compilers\VisualBasic\Portable\Scanner\Directives.vb` — `DirectiveHashPosition`（位置 0 判定）+ `ApplyDirective` shebang 分支。
- `Compilers\VisualBasic\Portable\Errors\Errors.vb`（37003/37004）、`Errors\ErrorFacts.vb`、`VBResources.resx`（消息文案）。
- `Compilers\VisualBasic\Portable\Syntax\SyntaxKind.vb`（`ShebangDirectiveTrivia = 752`）、`Syntax\SyntaxKindFacts.vb`、`Syntax\SyntaxNormalizer.vb`（`VisitShebangDirectiveTrivia`）。
- `Compilers\VisualBasic\Portable\Parser\Parser.vb`（`IsFirstStatementOnLine`）、`Syntax\InternalSyntax\SyntaxNodeExtensions.vb`。

对应设计：`spec\spec-shebang-directive.md`；实施样本 `tasks\shebang-directive\`（proposal/meeting 见 `proposals\proposal-shebang-directive.md` / `meetings\meeting-shebang-directive.md`）。

### 2.9 共享编译器 Core ReferenceManager：单 `#R`→N（修改，补上游 `// TODO: implement`）

- `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs`（两处）——
  - `:833-848` `GetCompilationReferences` 的 directive 循环：`ResolveReferenceDirective` 改返 `ImmutableArray`，逐条 push N 个 bound reference + N 个相同 `#r` location 进 `referencesBuilder`；per-#r map 保持单值 = `boundReferences[0]`（主资产，`primary asset` 约定见 `:841-847` 注释）。
  - `:878-902` `ResolveReferenceDirective`：返 `ImmutableArray<PortableExecutableReference>`，去掉 `references.Length > 1 → throw new NotSupportedException()`；0/1 输入路径与旧版一致（`IsDefaultOrEmpty → Empty`）。
- 改动性质：只动 shared resolver 的展开/对齐；不 alter 旧 singular API（`ReferenceDirectiveMap` 单值 map、`GetDirectiveReference` 语义均不变）；N 完整集经 `ExplicitReferences`（`:853-858` 上一条提交全量追加）跨提交继承；N>1 只在含 reference directive 的脚本编译激活（休眠区），对既有 0/1 输入纯加性。
- 折抵：产品 VB-only（本 fork 无 C# 脚本产品路径）；多引用展开对既有 0/1 输入休眠、零行为变化；改动面小且自洽，可 PR 化（以共享编译器改进形态向上游提 PR 也成立）。
- 合并前评估义务：合并前读本条目评估同步成本（对 `ResolveReferenceDirective` 与 directive 循环做 3-way 评审）；上游若日后以其它形状真实现 `// TODO: implement`，按上游形状对齐并回退本 fork 改法。

### 2.10 Scripting Core NuGet 解析器注入 seam：`packageResolver = null` 可选参 + VB 宿主 Friend 骨架（修改 + 新增）

- `Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs`（修改）—— `CreateCurrentPlatformResolver`（:51-64）签名末位新增可选参 `NuGetPackageResolver? packageResolver = null`（:55），同形透传 internal ctor（:57-63，位置实参 :60）；`ResolveReference` 的 NuGet 分支（:145-155）在 `PackageResolver == null` 时维持原返 `Empty`，不触既有路径。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（修改）—— `_packageResolver` 字段（:30）；ctor（:32-44）末位加 `NuGetPackageResolver packageResolver = null`（:32，赋值 :43）；`GetScriptOptions`（:163-190）与 `GetMetadataReferenceResolver`（:192-203）末位同形可选参（:163 / :192）并下传（:167 → :202 具名 `packageResolver:` 进 `CreateCurrentPlatformResolver`）。
- 新增文件（VB 宿主，Friend 内部骨架）—— `Scripting\VisualBasic\Hosting\NuGetPackageSession.vb`：`NuGetHostCapability`（:17-47，宿主 TFM / net48 判定）+ `NuGetPackageSession`（:53-91，restore 后包→编译资产字典，`TryGetCompilePaths` :80-86）；「索引 0 = 主资产」契约见 :57-58 与 :77-78。`Scripting\VisualBasic\Hosting\NuGetPackageResolverImpl.vb`：`Friend NotInheritable Class NuGetPackageResolverImpl Inherits NuGetPackageResolver`（:15-33）；`ResolveNuGetPackage`（:27-32）只读查会话，key 未命中 / 版本空 → `Empty`。
- 改动形状：全部为默认 `null` 可选参 / Friend 新文件，落 internal/Friend 面，不加公共面（`PublicAPI.*` 零动）；现有调用方不传参即默认 `null`，零改动（`Scripting\VisualBasic\VisualBasicScript.vb:166`、`Scripting\VisualBasicTest\CommandLineRunnerTests.vb:111`）。
- 折抵：纯加性 —— VB 宿主现未在调用点注入（`VisualBasicScript.vb:163-165` 注释预留接线位），默认 `null` 时解析行为与 seam 前一致；VB 宿主经 Core IVT（`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:55`）复用 internal `NuGetPackageResolver` 基类（`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:12`）与 seam，仅 VB 产品路径可达。
- 合并前评估义务：合并前读本条目对 `CreateCurrentPlatformResolver`、`CommandLineRunner` ctor、`GetScriptOptions`、`GetMetadataReferenceResolver` 四处做 3-way 评审；上游若日后以其它形状实现同类注入或改动上述签名，按上游形状对齐并回退本 fork 改法；新增 VB Hosting 文件为本地私有 Friend，不与上游路径冲突。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md`（seam 与「索引 0 = 主资产」契约出处）。

### 2.11 Scripting Core NuGet 包引用语法 fork：`TryParsePackageReference`（修改）

- `Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:23-48` — fork `TryParsePackageReference`（共享层，V-B 语法 fork）：`:26` 前缀匹配 `StartsWith` `Ordinal`→`OrdinalIgnoreCase`、`:34` 分隔 `/`→首个逗号（`IndexOf(',')`）、`:25` 整体 Trim（`' '`/`'\t'`/`'"'`）与 `:38`/`:46` name/version 段 Trim、`:46` 版本可省（无逗号时 `version = string.Empty`）。文件内 doc「本 fork 有意偏离上游」标注于 `:19-21`（doc 块 `:16-22`），与改动同处，防 3-way 合并且标差异可见。
- 改动形状：只动 parse 层，签名 `internal static bool (string reference, out string name, out string version)`（`:23`）不变；近失配（`:27-31` 前缀不匹配、`:39-44` name 段空）返 `False`；调用点 `RuntimeMetadataReferenceResolver.cs:147` 零改动（形参/返回值形状不变，`ResolveReference` 分支逻辑不动）。
- 折抵：VB 标识符大小写不敏感（前缀 `nuget:` 忽略大小写）＋逗号语法对齐 .NET Interactive/csx（`nuget:name, version`）；共享层 diverg 由文件内 doc（`:16-22`）＋本账本双记，任一侧被上游整体覆盖时 merge 可察觉并据「折抵」评估而非静默回滚。
- 合并前评估义务：合并前对本方法做 3-way 评审；上游若日后改回斜杠分隔或仅接受小写前缀，属误回滚，按上游真实意图评估对齐并回退本 fork 改法。
- 备注：本 fork 语法由 `Scripting\VisualBasicTest\NuGetPackageResolverTests.vb` 锁定（doc `:5`「锁定本 fork 对 NuGetPackageResolver.TryParsePackageReference 的语法解析」）；对应设计 `tasks\vbi-nuget-reference\design-detailed.md` §B（语法 fork）。

### 2.12 Scripting Core NuGet restore 协调 seam：`INuGetRestoreCoordinator`（修改 + 新增）

- `Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs`（新增）—— `internal interface INuGetRestoreCoordinator`：`PrepareCompilationAsync(SourceText code, string? filePath, CancellationToken)` → `Task<ImmutableArray<Diagnostic>>`（空 = 放行，非空 = 阻塞诊断，锚 `#R` 行）。编译前 seam，默认不装（null → runner 逐字节不变）。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（修改）—— ctor（`:33`）末位加可选参 `INuGetRestoreCoordinator nuGetRestoreCoordinator = null`（字段 `:31`，赋值 `:45`）；三调用点经 helper `RestoreNuGetReferencesAsync`（`:228-234`）：文件脚本路径（`:148-162`，读码后/编译前；交互 + 非 /check 跳过，由 REPL 初提交覆盖）、REPL 初提交（`:276-292`）、REPL 每提交（`:339-350`，IsHelpCommand 后、建 newScript 前）；默认 null → `Task.FromResult(Empty)`，零行为变化。
- `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb`（新增，VB 宿主 Friend）—— 实现该 seam：预扫描解析提交 → `CompilationUnitSyntax.GetReferenceDirectives()` → 逐 `#R` 串跑 `NuGetPackageResolver.TryParsePackageReference`（共享 parse 层）分类 → 决策表不依赖 restore 的行产锚 `#R` 行宿主诊断（VBI1001 版本缺失 / VBI1002 前缀格式 / VBI1003 空包名 / VBI1004 疑似拼错 / VBI1005 宿主自管包范围外）；restore 驱动行已接上（V-E，非留待）：`EnsureRestoredAsync` 先经 `IRestoreRunner`（生产 `DotNetRestoreRunner` spawn `dotnet restore`，测试注入 fake）查 `dotnet --list-sdks` 选版（SDK 缺失 → VBI1006），再部署字节稳定临时工程跑一次 restore；restore 退出码 ≠ 0 / 无 `project.nuget.cache` → `ExitCodeToDiagnostic` 转译（NU1101 → VBI1007 / 网络 → VBI1008 / 其它 → VBI1009 附 exit code）；exit-0 后读 assets 写会话，net48 含 native 资产 → VBI1010 宿主能力诊断，net10 缺当前 RID native → VBI1011 清晰诊断。
- `Scripting\VisualBasic\VisualBasicScript.vb`（修改）—— `RunInteractiveAsync`（`:150-176`）装配 `NuGetPackageSession` + `NuGetPackageResolverImpl` + `NuGetRestoreCoordinator` 经 ctor 可选参传入 runner（`:163-175`）；仅交互入口（编译模式 `Vbi.Compile.vb` 不经此点）。
- 改动形状：internal 接口 / Friend 实现，默认 null，不加公共面（`PublicAPI.*` 零动）；无 nuget 引用时 coordinator 预扫描短路空返、resolver 不被 consult；宿主诊断走宿主侧字符串直出，不引入编译器资源 / xlf 增量。
- 折抵：纯加性——默认 null 时 `CommandLineRunner` 行为与 seam 前一致；VB 宿主现装配 coordinator 后，无 nuget `.vbx`/REPL 提交仍逐字节不变（预扫描空返），仅 nuget `#R` 路径点亮；共享层 diverg 由本账本 + `design-detailed.md` §D 双记。
- 合并前评估义务：合并前读本条目对 `CommandLineRunner` ctor / 三调用点与 `INuGetRestoreCoordinator` 接口做 3-way 评审；上游若日后以其它形状实现 host 注入或改动上述签名，按上游形状对齐并回退本 fork 改法；新增 VB Hosting 文件为本地私有 Friend，不与上游路径冲突。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §D（宿主驱动环 + 诊断锚定）。

### 2.13 Scripting Core net10 native loader seam：`AddNativeProbeRoot` + `LoadUnmanagedDll`（修改 + 新增）

- `Scripting\Core\Hosting\AssemblyLoader\AssemblyLoaderImpl.cs`（修改）—— 基类加 `internal virtual void AddNativeProbeRoot(string directory) { }`（默认 no-op；net48 Desktop 不探测，net10 Core override）。
- `Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`（修改）—— `public sealed partial` 加 **internal** seam `AddNativeProbeRoot(string)`（null 校验后下推 `_runtimeAssemblyLoader`）。不新增 public 面（IVT 已覆盖 vbi/VB.Scripting）→ `PublicAPI.*.txt` 零动。
- `Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`（修改）—— 持共享 native 根集 `_nativeProbeRoots`（`ImmutableArray<string>`，默认空）；override `AddNativeProbeRoot` 去重追加；每个私有 `LoadContext`（in-memory 与 per-path 两构造点）改收 owner 引用；`LoadContext` 在 `#if NET10_0` 下 override `LoadUnmanagedDll(string)`：对每根目录按「原名 → 原名+平台扩展名 → lib+原名+扩展名」探测，命中 `LoadUnmanagedDllFromPath`，未命中 `base.LoadUnmanagedDll(name)`。空根集 → 逐字节回到现状（无 override 语义）；netstandard2.0/net48 构建不含 override（net10-only）。
- `Scripting\Core\Hosting\AssemblyLoader\NativeLibraryProbe.cs`（新增，internal static）—— 纯候选表 `GetProbeCandidates(roots, name, ext)`（跨平台命名 `.dll`/`.so`/`.dylib`，root 序 → 三候选名序，无 I/O）+ `GetPlatformNativeExtension()`；由 `Scripting\VisualBasicTest\NativeLibraryProbeTests.vb` 锁定（纯函数，不真加载）。
- VB 宿主配套（本地 Friend，不入上游路径）—— `NuGetRestoreDiagnostics.vb` 增 VBI1011 `CreateMissingNativeAssetsDiagnostic`；新增 `NuGetMissingNativeAssetsDetector.vb`（纯检测：请求包带 native 资产但无当前 RID）；`NuGetRestoreCoordinator.vb` restore 成功后（非 net48）接入该检测产锚 `#R` 行诊断。
- 改动形状：internal/Friend 面，零公共面（`PublicAPI.*` 零动）；空根集零回归；目录策略在宿主（host 供 `runtimes/<rid>/native` 去重集），unmanaged 解析机制在 loader。
- 折抵：纯加性——无 nuget 引用/空 native 根集时 loader 行为与 seam 前一致；native seam 仅 net10 激活（`#if NET10_0`），net48 无 ALC 不探测；`LoadUnmanagedDll` override 空根集转发 base 与现状等价。
- 合并前评估义务：合并前读本条目对 `AssemblyLoaderImpl` 基类、`InteractiveAssemblyLoader`（internal seam）、`CoreAssemblyLoaderImpl`（`LoadContext` ctor 签名 + `#if NET10_0` override）与新增 `NativeLibraryProbe.cs` 做 3-way 评审；上游若日后以其它形状实现同款 native 探测或改动上述签名，按上游形状对齐并回退本 fork 改法；VB Hosting 配套为本地私有 Friend，不与上游路径冲突。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §G（net10 native loader seam）。

### 2.14 Scripting Core 运行期资产握手：`RegisterRuntimePathOverride`（ref→lib）+ 共享 loader 装配（修改 + 新增）

- `Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`（修改，public sealed partial）—— 运行期 ref→lib 覆盖 seam：新增 internal `RegisterRuntimePathOverride(string compilePath, string runtimePath)`（把 NuGet compile(ref) 资产映射到 runtime(lib) 资产，宿主 restore 成功后调用）与 `GetRegisteredDependencyLocations(string simpleName)`（internal 观察位，供测试断言覆盖生效）；`public RegisterDependency(AssemblyIdentity, string path)` 在锁内查覆盖表：命中则以 runtime 路径替换 compile 路径再登记，**覆盖表默认空 → 无 nuget / 未握手路径逐字节回到现状**；不新增 public 面（`PublicAPI.*` 零动）。
- `Scripting\Core\Hosting\AssemblyLoader\AssemblyLoaderImpl.cs`（修改）—— 基类加 `internal virtual ImmutableArray<string> NativeProbeRoots => Empty`（观察位；Desktop 恒空）。
- `Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`（修改）—— override `NativeProbeRoots` 返回共享根集 `_nativeProbeRoots`（配合 2.13 的 `AddNativeProbeRoot`/`LoadUnmanagedDll`）。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（修改）—— ctor 末位加可选参 `InteractiveAssemblyLoader assemblyLoader = null`（字段 + 赋值），三处 `Script.CreateInitialScript(..., assemblyLoaderOpt:)`（文件脚本 / REPL 初提交 / REPL 首提交）由原 `null` 改传 `_assemblyLoader`；默认 null 时行为与现一致（`CreateInitialScript` 自建 loader）。
- VB 宿主配套（本地 Friend，不入上游路径）—— `NuGetRestoreAssets.vb` `NuGetRestoreAssetsReader.ComputeRuntimePathOverrides`（纯函数：compile 资产集 × runtime 资产集 → ref→lib 覆盖字典，同名唯一且路径不同才覆盖、lib-only 包不覆盖、同名歧义跳过）；`NuGetRestoreCoordinator.vb` ctor 加可选 `loader`，restore 成功（非 net48-native 早退）后 `PushSessionAssetsToLoader` 把 native 根逐条 `AddNativeProbeRoot` + 覆盖表逐条 `RegisterRuntimePathOverride` 下推 loader，并把 restore 的整个 runtime(lib) 闭包经 `RegisterRuntimeClosure`（`AssemblyName.GetAssemblyName` + `AssemblyIdentity.TryParseDisplayName` + public `RegisterDependency`，`File.Exists` 门控）注册进 loader（F-B 扩展：ScriptBuilder 只注册编译 bound 程序集，整 app 型脚本运行期才触达的闭包程序集由此补全，仍走既有 public `RegisterDependency`，零公共面增量）；`VisualBasicScript.vb` `RunInteractiveAsync` 建一个共享 `InteractiveAssemblyLoader` 同时传给 runner 与 coordinator。
- 改动形状：internal/Friend 面，零公共面（`PublicAPI.*` 零动）；覆盖表/根集默认空 = 现状零回归；目录策略在宿主、机制在 loader（同 2.13）。
- 折抵：纯加性——空覆盖表 / 空根集 / 无 loader 时 loader 与 runner 行为与 seam 前一致；只有宿主在真实 nuget restore 成功后握手才点亮。ref→lib 修复对齐 ScriptBuilder.cs:147-148 上游 TODO（"Contract assembly vs RT assembly path"）。
- 合并前评估义务：合并前读本条目对 `InteractiveAssemblyLoader`（`RegisterDependency` 覆盖重定向 + 新增 internal 方法）、`CommandLineRunner` ctor / 三处 `CreateInitialScript` 调用点、`AssemblyLoaderImpl`/`CoreAssemblyLoaderImpl` 新增 internal 属性做 3-way 评审；上游若日后以其它形状实现同款 runtime 资产选择或改动上述签名，按上游形状对齐并回退本 fork 改法；VB Hosting 配套为本地私有 Friend，不与上游路径冲突。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §G（运行时注册 + net10 native loader seam）。

### 2.15 Scripting Core loader 向上版本统一：`ResolveBestDefinitionIndex`（修改）

- `Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`（修改，public sealed partial）—— 新增 internal static `ResolveBestDefinitionIndex(AssemblyIdentity reference, IReadOnlyList<AssemblyIdentity> definitions)`（版本选择策略单点，供测试直调锁定）+ private `IsUpwardVersionUnification(reference, definition)`；原两处私有 `FindHighestVersionOrFirstMatchingIdentity`（`LoadedAssemblyInfo` 与 `AssemblyIdentityAndLocation` 重载，.cs:557/576 前身）改为把候选 identity 物化数组后委托该内部方法。语义：请求 identity 无**精确（或平台统一）候选**时，允许同名 + 文化/公钥/内容类型一致且定义版本 ≥ 引用版本的**最高版本**候选满足（可升级、不降级）；有精确候选时精确优先。「等价仅版本异」经 public `AssemblyIdentityComparer.Default.Compare == EquivalentIgnoringVersion` 判定，不触碰共享 `DesktopAssemblyIdentityComparer` 全局语义、不新增 public 面（`PublicAPI.*` 零动）。
- 改动形状：internal/Friend 面，零公共面；无版本冲突路径（精确命中 / 弱名 any-version / FX 双向统一）行为与改动前逐字节一致；只有「强名非 FX、无精确候选且定义版本高于引用版本」时点亮升级路径（对齐默认 ALC 的版本向上绑定）。
- 折抵：修复 P-008 用户可见限制——跨 assembly 版本 skew 的包组合（`FluentAvaloniaUI 3.0.0-preview2` 按 Avalonia 12.0.0.0 编译配 Avalonia 12.1.1 包）由「永 FileNotFound、须同 release pin」变为可运行（精确优先、只升不降，引用高于所有定义仍失败）。样例 `Samples/AvaloniaCalculator.vbx` 加回 FAUI 主题（窗口真跑通过：MainWindowTitle 可见、16s 存活、无错误输出、无残留）。
- 合并前评估义务：合并前读本条目对 `InteractiveAssemblyLoader` 两处私有 `FindHighestVersionOrFirstMatchingIdentity` 与新增 internal `ResolveBestDefinitionIndex` 做 3-way 评审；上游若日后以其它形状实现同款版本统一或改动上述方法，按上游形状对齐并回退本 fork 改法。新单测锁定选择策略（`Scripting\VisualBasicTest\NuGetRuntimeHandshakeTests.vb` F-D 节，纯函数零 I/O + 一例真实已加载程序集解析）。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §G（运行时 loader seam）；修复 F-B 记录的 P-008 已知限制（`tmp/vortex-logs/vbi-nuget-runtime-handshake/pitfalls.md` 已追加 P-008 裁决）。

### 2.16 Scripting Core NuGet 协调 seam 增带 ScriptOptions + #Load 预扫描展开（修改）

- `Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs`（修改）—— `PrepareCompilationAsync` 签名由 `(SourceText code, string? filePath, CancellationToken)` 改为 `(SourceText code, string? filePath, ScriptOptions? options, CancellationToken)`（`:32`）；`options` 为编译该提交将用的 `ScriptOptions`，null = 旧形状（只扫提交文本，不展开 `#Load`）。接口仍 internal，零公共面。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（修改）—— 私有 helper `RestoreNuGetReferencesAsync`（`:230`）与三调用点（文件脚本 `:156`、REPL 初提交 `:283`、REPL 每提交 `:344`）均透传当前 `scriptOptions`/`options`；coordinator 为 null 时 helper 短路 `Empty`，行为与 seam 前逐字节一致。
- `Scripting\VisualBasic\VisualBasicScriptCompiler.vb`（修改）—— 原私有 `LoadReferencedTrees`（抛 `CompilationErrorException`）提为 **Friend Shared** `CollectLoadTrees`（`:73-110`）：同一 DFS `#Load` 展开（resolver 解析 + 循环 activeLoads 防护 + depth-first 收集），遇首个不可解析/循环 `#Load` 返回其 file-not-found 诊断而不再内抛，由调用方决定如何浮出；`CreateSubmission`（`:195-197`）收到诊断仍 `ThrowLoadDirectiveError`（行为与改动前一致）。共享单点防编译器与宿主预扫描两份拷贝漂移。
- `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb`（修改）—— `ScanSubmission`（`:175`）在 options 非 null 时以 `options.ParseOptions`/`options.SourceResolver` 解析主树并调 `VisualBasicScriptCompiler.CollectLoadTrees`（`:218`）展开 `#Load`，主树与所有可达树逐棵经 `ScanTreeForNuGetDirectives`（`:232`）扫 `#R` 分类（决策表不变）；被 load 文件的 `#R` 诊断/restore 锚定位到各自树的路径与行。展开失败（缺文件/循环）不阻塞预扫描——留给编译器在编译期报 ERR_FileNotFound。保留 3 参重载（`:136-137`）＝ options 为 Nothing 的旧形状（只扫提交文本），既有调用/测试零改动。
- 改动形状：接口 internal 加参 + VB 宿主 Friend；`PublicAPI.*` 零动；options 为 null / 无 `#Load` / 无 nuget 引用时行为与 seam 前一致（预扫描短路空返）。
- 折抵：修复「`#Load` 嵌套 `#R "nuget:…"` 不 restore → 编译绑定期 BC2017 找不到库」：coordinator 之前只扫提交文本，被 load 文件里的 nuget 指令从没进预扫描；现与编译器共享同一展开逻辑，restore 先于编译完成。单测以 in-memory `SourceReferenceResolver` 覆盖（无磁盘/网络/spawn），真跑（REPL `#load` AvaloniaCalculator / 文件脚本 `#load` nuget helper）均 EXIT 0、无 BC2017。
- 合并前评估义务：合并前读本条目对 `INuGetRestoreCoordinator` 签名、`CommandLineRunner` 三调用点、`VisualBasicScriptCompiler.CollectLoadTrees`（含 `LoadReferencedTrees` 重构）与 `NuGetRestoreCoordinator.ScanSubmission` 做 3-way 评审；上游若日后以其它形状实现 host 注入或改动上述签名/`#Load` 展开，按上游形状对齐并回退本 fork 改法。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §D（宿主驱动环 + 诊断锚定）；修复用户实锤 REPL `#load` 含 nuget 的 `.vbx` 报 BC2017 的 bug（F-E）。

### 2.17 Scripting Core loader NuGet 会话状态替换 seam：`ResetSessionState`（修改）

- `Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`（修改，public sealed partial）—— 新增 **internal** `ResetSessionState()`：清 native 探测根（下推 `_runtimeAssemblyLoader.ResetNativeProbeRoots()`）+ 清 runtime-path override 表（`_runtimePathOverrides` 置空，锁内）；**托管依赖注册（`_dependenciesWithLocationBySimpleName` / `_loadedAssembliesBySimpleName`）不清**——前序 REPL 提交已加载程序集仍需解析。不新增 public 面（`PublicAPI.*` 零动）。
- `Scripting\Core\Hosting\AssemblyLoader\AssemblyLoaderImpl.cs`（修改）—— 基类加 internal virtual `ResetNativeProbeRoots()`（默认 no-op；net48 Desktop 不探测）。
- `Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`（修改）—— override `ResetNativeProbeRoots()` 把共享根集 `_nativeProbeRoots` 置空。
- VB 宿主配套（本地 Friend，不入上游路径）—— `NuGetRestoreCoordinator.PushSessionAssetsToLoader` 每次成功 restore 推送前先 `ResetSessionState()` 再推本次会话根集/覆盖表；结合 R-1 会话累积包集合语义，升降级/移除 native 包后旧根与旧 override 不再残留，空根/空覆盖 = 现状零行为。
- 改动形状：internal/Friend 面，零公共面（`PublicAPI.*` 零动）；空根集/空覆盖表下行为与 seam 前逐字节一致。
- 折抵：REPL 长会话内 loader 的 NuGet 握手状态由「单调累积」改为「每次成功 restore 后替换为本次会话集」；托管依赖保留累积（不能卸载已加载程序集，且前序提交仍需运行）。单测 `NuGetRuntimeHandshakeTests.LoaderResetClearsNativeRootsAndOverridesButKeepsDependencyRegistrations` / `...CoordinatorReplacesLoaderOverridesAcrossVersionUpgrade` 锁定（R-2）。
- 合并前评估义务：合并前读本条目对 `InteractiveAssemblyLoader`（internal `ResetSessionState`）、`AssemblyLoaderImpl`/`CoreAssemblyLoaderImpl`（internal virtual/override `ResetNativeProbeRoots`）做 3-way 评审；上游若日后以其它形状实现同款替换语义或改动上述方法，按上游形状对齐并回退本 fork 改法；VB Hosting 配套为本地私有 Friend，不与上游路径冲突。
- 对应设计：`tasks\vbi-nuget-reference\design-detailed.md` §G（net10 native loader seam / 运行期注册）；修复 VB 老登复查 R-2（REPL 升降级 loader 状态残留，F-G）。

### 2.18 VisualBasic 命令行 `/imports:` 坏子句不入 `GlobalImports`（修改，纯加性守卫）

- `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:1865-1878`（`ParseGlobalImports`）—— `GlobalImport.Parse(importNamespace, importDiagnostics)` 对语法非法的子句返回 `Nothing`（`GlobalImport.vb:68-70` 经 `ElementAtOrDefault` 取空序列默认值），`:1871` 的诊断照旧进 `errors`；`:1872` 由无条件 `globalImports.Add(import)` 改为 `:1874-1876` `If import IsNot Nothing Then globalImports.Add(import)`。
- 改动形状：纯加性守卫——合法子句路径逐字节不变（`import IsNot Nothing` 恒真），只在坏子句时不再把 `Nothing` 放进 `GlobalImports`；`Arguments.Errors` 内容不变，vbc / vbi 编译模式的用户可见输出不变（错误仍由 `Arguments.Errors` 报出、`CommonCompiler.cs:897` 编译前返回 `Failed`）。
- 折抵：消除 `GlobalImports` 的 `Nothing` 毒丸——六个消费点（`VisualBasicCompilationOptions.GetImports` :347、`VisualBasicCompilation` :793、`VisualBasicDeterministicKeyBuilder` :83-84、`SourceModuleSymbol` :336/:381、`SourceNamedTypeSymbol` :1935、`VisualBasicSyntaxHelper` :132）在「错误场景下仍被编译」时不再 NRE；vbi 脚本/REPL 宿主（`CommandLineRunner.cs:140` 的 `GetScriptOptions` 先于 `:142-146` 的 Errors 门）由 NRE 栈恢复为报出 `/imports:` 诊断并返回 `Failed`。无坏值时零行为变化。
- 合并前评估义务：合并前读本条目对 `ParseGlobalImports` 做 3-way 评审；上游若日后改判坏子句形状（改 `GlobalImport.Parse` 契约或在解析期补诊断），按上游形状对齐并回退本 fork 改法。本文件已在 §2.1 登记（`:61-63`、`:1522-1523`），本条目为其新增改动点。
- 对应缺陷：`issues\issue-vbi-imports-switch-nre.md`（方向 A）；测试锁定 `Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb`。

### 2.19 C# 14 扩展成员与 C# 11 接口共享成员消费（新增 + 修改，纯消费侧）

改动主体为提交 `e307d0f`（`Compilers\` 下 37 文件 +3765/−130）。

**Part A — 扩展成员（`<G>$` 分组类型 → 归约符号 → 绑定挂点）**

新增文件：

- `Compilers\VisualBasic\Portable\Symbols\ReducedExtensionMemberSymbols.vb`（669 行）—— `Friend Module ReducedExtensionMemberReducer`（`:29`）：入口 `ReduceExtensionMember`（`:35`，按 `SymbolKind.Property` / `SymbolKind.Method` + `IsExtensionMember` 分派；扩展运算符以 `OverloadResolution.GetOperatorInfo(method.Name).ParamCount <> 0` 识别而非 `MethodKind`）、`FindTopLevelShim`（`:78`，在 `groupingType.ContainingType` 上按同名且 arity == `groupingType.Arity + member.Arity` 定位非扩展的顶层静态 shim）、`GetReceiverType`（`:106`，经 `UnwrapGroupingType`（`:120`）剥 `RetargetingNamedTypeSymbol` 后读 `PENamedTypeSymbol.GetExtensionReceiverType`）、`ReduceExtensionProperty`（`:133`）、`ReduceExtensionOperator`（`:183`，receiver 取首参数，`:225` 仅在 grouping arity 0 时改指顶层 shim）、`ReduceExtensionMethodViaShim`（`:242`，复用既有 `ReducedExtensionMethodSymbol.Create`）、`InferGroupingTypeArguments`（`:267`，合成 `SignatureOnlyMethodSymbol` 走 `TypeArgumentInference.Infer` + `CheckConstraints`）、`ConstructGroupingType`（`:440`）。
- 同文件 `Friend NotInheritable Class ReducedExtensionOperatorSymbol`（`:496`，`Inherits WrappedMethodSymbol`）—— `ReceiverType` 返首操作数（`:516`）、`ReducedFrom` 返 `Nothing`（`:527`，避免被 `MethodSymbol.IsReducedExtensionMethod` 认成 receiver-stripped 归约扩展方法）、`MethodKind` 报 `UserDefinedOperator`（`:545`）、`IsMethodKindBasedOnSyntax` 返 False（`:551`，使 `ValidateOverloadedOperator` 跳过）、`MayBeReducibleExtensionMethod` 返 False（`:557`）。

符号 / 元数据层：

- `Compilers\VisualBasic\Portable\Symbols\MethodSymbol.vb:447`、`Compilers\VisualBasic\Portable\Symbols\PropertySymbol.vb:424` —— 基类 `Friend Overridable ReadOnly Property IsExtensionMember As Boolean`（默认 False）。
- `Compilers\VisualBasic\Portable\Symbols\Metadata\PE\PEMethodSymbol.vb:713`、`Compilers\VisualBasic\Portable\Symbols\Metadata\PE\PEPropertySymbol.vb:619` —— override 为 `_containingType.IsExtensionGroupingType AndAlso _containingType.ContainingPEModule.Module.HasExtensionMarkerAttribute(_handle, markerName)`。
- `Compilers\VisualBasic\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.vb:956`（`IsExtensionGroupingType`，判据 `ContainingType IsNot Nothing AndAlso HasExtensionAttribute(_handle, ignoreCase:=True)`）、`:970`（`IsExtensionMarkerType`）、`:1002`（`TryGetExtensionMarkerMethod`）、`:1039`（`GetExtensionReceiverType`）。
- `Compilers\VisualBasic\Portable\Symbols\NamedTypeSymbol.vb:286`（`IsExtensionGroupingType` 基类恒 False）、`:371`（`IsExtensionMemberSymbol`）、`:387`（`AppendProbableExtensionMembers`）、`:407`（`GetExtensionMembers`）、`:429`（`AddExtensionMemberLookupSymbolsInfo`）、`:450`（`IsNameableExtensionMember`）。
- 命名空间面：`Compilers\VisualBasic\Portable\Symbols\NamespaceSymbol.vb:463`（`AddExtensionMemberLookupSymbolsInfo`）、`:495`（`GetExtensionMembers`）、`:557`（`AddExtensionMember`，供重定向层替换入桶符号）；`Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:589`、`:595`；`Compilers\VisualBasic\Portable\Symbols\Retargeting\RetargetingNamespaceSymbol.vb:259`、`:277`、`:292`；`Compilers\VisualBasic\Portable\Symbols\Retargeting\RetargetingNamedTypeSymbol.vb:212`（`AppendProbableExtensionMembers`，追加项逐个 `RetargetingTranslator.Retarget`）、`:235`（`GetExtensionMembers` 抛 `ExceptionUtilities.Unreachable`）、`:248`。

绑定层（收集 + 挂点）：

- `Compilers\VisualBasic\Portable\Binding\Binder.vb:222` —— `CollectProbableExtensionMethodsInSingleBinder` 增参 `extensionMembers As ArrayBuilder(Of Symbol)`；`:238` 新增 `Friend Sub CollectExtensionMembersFromBinders`（`OverloadResolution` 不在 Binder 层级，经此 Friend 入口走同一条 binder 链）。
- 四个 collector override 同步增参并追加收集：`Compilers\VisualBasic\Portable\Binding\NamedTypeBinder.vb:106`/`:113`、`Compilers\VisualBasic\Portable\Binding\NamespaceBinder.vb:92`/`:99`、`Compilers\VisualBasic\Portable\Binding\ImportedTypesAndNamespacesMembersBinder.vb:141`/`:157`、`Compilers\VisualBasic\Portable\Binding\TypesOfImportedNamespacesMembersBinder.vb:79`/`:94`。
- `Compilers\VisualBasic\Portable\Binding\Binder_Lookup.vb:1183` —— `LookupForExtensionMethods` 增 `ByRef extensionMembers As ArrayBuilder(Of Symbol)`（`:1190`），`:1219` 逐 binder 收集、`:1251` 合并；`:1271` 新增 `MergeExtensionPropertiesIfNecessary`（`:1282` 转调既有 `MergeInternalXmlHelperValueIfNecessary`；扩展属性经 `ReducedExtensionMemberReducer.ReduceExtensionMember` + `CheckViability` 后以 `MergePrioritized` 作低优先级候选并入）；`:83`、`:1353`（`AddLookupSymbolsInfoOfExtensionMethods`）传 `Nothing`。
- `Compilers\VisualBasic\Portable\Binding\Binder_XmlLiterals.vb:1533`（新增 `_receiverType` 字段）、`:1539`（双参 ctor）、`:1571`（`HasExplicitReceiverType`）、`:1579`（`ReceiverType` 优先返显式 receiver）、`:1775`（`CallsiteReducedFromMethod` 经 `FindTopLevelShim`）、`:1979`（`ParameterCount` 不剥首参）、`:1989`（访问器 `Parameters` 传 `skipReceiver`）、`:2062`（`ReducedAccessorParameterSymbol.MakeParameters` 增可选参 `skipReceiver`）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Invocation.vb:994`/`:1000` —— 带显式 receiver 类型的扩展属性把接收者转 r-value（`Conversions.ClassifyConversion` + `PassArgumentByVal`）；其余归约扩展属性沿用 `UpdateReceiverForExtensionMethodOrPropertyGroup`。
- `Compilers\VisualBasic\Portable\Semantics\Operators.vb:2847` —— `CollectUserDefinedOperators` 增可选参 `binder As Binder = Nothing`（`:2857`）；`:2917` 追加 `CollectInterfaceConstraintSharedOperators`（重载定义 `:3000`/`:3021`，经 `CollectSharedOperatorsOnInterface`（`:3061`）扫接口成员；候选面为类型参数接口约束 + 其 `AllInterfaces`）、`:2925`/`:2928` 追加 `CollectExtensionUserDefinedOperators`（定义在 `:2941`，经 `CollectExtensionMembersFromBinders` + `ReduceExtensionMember`，以 `HashSet(Of MethodSymbol)` 去重）；`binder` 透传到 `:3145` 起的 25 处 True/False/一元/二元收集点，转换分支不传（`:2836-2839` 的 `CollectUserDefinedConversionOperators` 调 5 参重载时省 `binder`，配合 `:2924` 的 `binder IsNot Nothing AndAlso opKind = MethodKind.UserDefinedOperator` 门控，故转换运算符不参与扩展运算符收集）。
- `Compilers\VisualBasic\Portable\Symbols\TypeSymbolExtensions.vb:531` —— `CanContainUserDefinedOperators` 对接口约束放行（`constraint.IsInterfaceType() OrElse CanContainUserDefinedOperators(...)`）。
- `Compilers\VisualBasic\Portable\BoundTree\BoundUserDefinedBinaryOperator.vb:48`、`Compilers\VisualBasic\Portable\BoundTree\BoundUserDefinedUnaryOperator.vb:40` —— `Debug.Assert` 放宽为「`MethodKind.UserDefinedOperator`，或 `Ordinary` + `IsShared` + `IsMustOverride` + 接口」（SAIM 运算符在 PE 符号模型落为 `MethodKind.Ordinary`）。

**Part B — C# 11 接口共享成员（SAIM）经约束类型参数消费**

- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:2920-2977` —— `T.X`（`BoundTypeExpression` + `TypeKind.TypeParameter`）分支由原先无条件返 `ERR_TypeParamQualifierDisallowed`（BC32098）改为：逐 `ConstraintTypesWithDefinitionUseSiteDiagnostics` 取接口约束及其 `AllInterfaces`，以 `LookupMember(..., options Or LookupOptions.MustNotBeInstance)` 找同名成员，成员全为 `IsStaticAbstractInterfaceMember` 时经 `BindSymbolAccess` 绑定；`:2959-2961` 运行时门控（`SupportsRuntimeCapability(RuntimeCapability.VirtualStaticsInInterfaces)` 为假且成员来自被引用模块时才报 `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`）；未命中仍返 BC32098（`:2976`）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:3044` —— 新增 `IsStaticAbstractInterfaceMember`（shared + must-override + 接口，覆盖方法/属性访问器形状）。
- `Compilers\VisualBasic\Portable\Binding\Binder.vb:982`/`:994` —— `ReportDiagnosticsIfObsoleteOrNotSupported` 增可选参 `receiverIsTypeParameter`（为 True 时不再报 `ERR_BadAbstractStaticMemberAccess` BC37314）。
- 传该标志的调用点：`Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:1389`/`:1395`（属性 get）、`Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb:1982`/`:1987`（属性 set）、`Compilers\VisualBasic\Portable\Binding\Binder_Invocation.vb:858`/`:882`（方法/属性访问）、`Compilers\VisualBasic\Portable\Binding\Binder_Delegates.vb:323`/`:328`（`AddressOf`）。
- 接收者保留（供发射 `constrained.` 前缀）：`Compilers\VisualBasic\Portable\Binding\Binder_Invocation.vb:896-898`、`Compilers\VisualBasic\Portable\Binding\Binder_Delegates.vb:1002-1010`，均以 `AdjustReceiverTypeOrValue(..., clearIfShared:=False, ...)` 替代清 shared 接收者的重载。
- `Compilers\VisualBasic\Portable\Binding\Binder_Operators.vb:1286`（`GetStaticAbstractOperatorReceiver`，用于 `:607` 二元 / `:1259` 一元结果构造）—— 类型参数操作数上的接口 shared + must-override 运算符补一个 `BoundTypeExpression` 接收者，其余返 `Nothing`。
- `Compilers\VisualBasic\Portable\CodeGen\EmitExpression.vb:996`（新增 `CallKind.ConstrainedCall`）、`:1021-1028`（shared + must-override + 接口 + `TypeParameter` 接收者 → `ConstrainedCall`）、`:1138`（发射 `constrained.` + `call`）、`:487`（`AddressOf` 创建委托时发 `constrained.` + `ldftn`）。
- `Compilers\VisualBasic\Portable\Lowering\UseTwiceRewriter.vb:320` —— 类型参数接收者的 shared 属性访问在 get/set 两次访问都保留接收者。
- 表达式树拒绝：`Compilers\VisualBasic\Portable\Lowering\Diagnostics\DiagnosticsPass_ExpressionLambdas.vb:266`（方法调用）、`:282`（属性访问）、`:290-295`（新增 `VisitDelegateCreationExpression`）、`:304`（`IsStaticAbstractInterfaceMember`），产 `ERR_ExpressionTreeContainsAbstractStaticMemberAccess`。

**附带（错误码资源）**

- `Compilers\VisualBasic\Portable\Errors\Errors.vb:1203`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces = 32134`）、`:1815`（`ERR_ExpressionTreeContainsAbstractStaticMemberAccess = 37340`）、`:1820`（`ERR_NextAvailable`，现值 **37344**＝§2.20 / §2.21 各码占用后的累计前沿）。
- `Compilers\VisualBasic\Portable\Errors\ErrorFacts.vb:923`（32134 入「非报错」集）、`:1561`（37340 入报错集）。
- `Compilers\VisualBasic\Portable\VBResources.resx:2748`、`:5491` —— 两条新消息文案。

- 改动形状：新文件 + `Friend` 面增量 + 一处由 `Protected Overridable` 扩参的收集入口（`CollectProbableExtensionMethodsInSingleBinder`）。新增类型位于 Friend 面（`Friend Module ReducedExtensionMemberReducer`、`Friend NotInheritable Class ReducedExtensionOperatorSymbol`）；`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` 在 `e307d0f` 零动——证据：`git show --name-only e307d0f` 对 `publicapi` 零命中，且 `PublicAPI.Unshipped.txt` 最后一次改动仍是 shebang 提交 `04831f6`。本提交亦不触及 `Compilers\Core\`：`PEModule.HasExtensionAttribute`（`Compilers\Core\Portable\MetadataReader\PEModule.cs:1031`）与 `PEModule.HasExtensionMarkerAttribute`（同文件 `:1061`）为既有共享钩子，本提交只调用。
- 折抵：主体是**消费 C# 侧已存在的元数据**（C# 14 `<G>$` 分组类型 / `<M>$` 标记类型 / `System.Runtime.CompilerServices.ExtensionMarkerAttribute`，C# 11 `static abstract` 接口成员），不改 VB 自身可声明语义——`IsExtensionGroupingType` 基类恒 False（`NamedTypeSymbol.vb:286`），`PENamedTypeSymbol.vb:956` 为 `Compilers\` 内唯一 override，故源码符号不会被识别为扩展分组类型。本地形态分叉四处，3-way 时可察觉：① `T.X` 由无条件 BC32098 改为接口约束有效集查找（`Binder_Expressions.vb:2920-2977`）；② 发射层新增 `constrained.` + `call` / `ldftn`，`CallKind.ConstrainedCall` 为 fork 新枚举成员；③ 错误码 32134 / 37340 与 `ERR_NextAvailable` 推进落在与上游共享的 `Errors.vb` 编号带；④ `Binder.CollectProbableExtensionMethodsInSingleBinder` 这一既有 `Protected Overridable` 签名被扩参。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`Binder.CollectProbableExtensionMethodsInSingleBinder`（签名）、`Binder.CollectExtensionMembersFromBinders`、`Binder.ReportDiagnosticsIfObsoleteOrNotSupported`（新增可选参）、`MemberLookup.LookupForExtensionMethods`（新增 `ByRef` 参）、`MemberLookup.MergeExtensionPropertiesIfNecessary`、`OverloadResolution.CollectUserDefinedOperators`（新增可选参 `binder`）、`NamedTypeSymbol` 的 `IsExtensionGroupingType`·`AppendProbableExtensionMembers`·`GetExtensionMembers`·`AddExtensionMemberLookupSymbolsInfo`·`IsNameableExtensionMember`、`NamespaceSymbol` 的 `GetExtensionMembers`·`AddExtensionMember`·`AddExtensionMemberLookupSymbolsInfo`、`MethodSymbol.IsExtensionMember`、`PropertySymbol.IsExtensionMember`、`PENamedTypeSymbol` 的 `IsExtensionGroupingType`·`IsExtensionMarkerType`·`TryGetExtensionMarkerMethod`·`GetExtensionReceiverType`、`PEMethodSymbol.IsExtensionMember`·`IsStaticAbstractInterfaceMember`、`CallKind.ConstrainedCall`、`Binder_Expressions` 的 TypeParameter 成员访问分支与 `IsStaticAbstractInterfaceMember`、`TypeSymbolExtensions.CanContainUserDefinedOperators`、`Errors.vb` 的 32134·37340·`ERR_NextAvailable`。上游若日后以其它形状实现 C# 14 扩展成员消费或 C# 11 SAIM 经类型参数消费（例如上游 VB 自补约束接口查找、`constrained.` 发射或 `T.X` 诊断改写），按上游形状对齐并回退本 fork 改法；新增文件 `ReducedExtensionMemberSymbols.vb` 为本地归约层，不与上游路径冲突。
- 对应设计：`proposals\proposal-consume-csharp-extension-and-interface-shared.md`；`meetings\meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION：只消费、零新语法，状态 Active、D4 P1 判入）；`spec\spec-consume-csharp-extension-members.md`、`spec\spec-consume-interface-shared-members.md`；实施样本 `tasks\consume-csharp-extension-and-interface-shared\`（含 `verification-checklist.md`）。测试锁定 `Compilers\VisualBasicSemanticTest\Semantics\ExtensionMemberConsumptionTests.vb`、`Compilers\VisualBasicSemanticTest\Semantics\InterfaceSharedMemberConsumptionTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMemberConsumptionSymbolTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\StaticAbstractMembersInInterfacesTests.vb`。

### 2.20 脚本顶层崩溃族 F04–F11（对应 issue 04–11，修改）

改动主体：`tasks\script-top-level-crashes\`（八条 issue 的逐条收口；判定表、逐单元蓝图与验收网见该文件夹四件套）。**其中 `Symbols\Source\` 的五个文件（`SourceMethodSymbol.vb` / `SourceMemberContainerTypeSymbol.vb` / `SynthesizedEventAccessorSymbol.vb` / `SourceWithEventsBackingFieldSymbol.vb` / `SynthesizedWithEventsAccessorSymbol.vb`）、`Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb` 与 13 份 `xlf` 为本条目首次在册**。

- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMethodSymbol.vb`（**新增在册**）—— F04：早期解码进门条件由三重改四重，补 `Me.IsShared`（`:1500-1503`），使紧随其后的 `Debug.Assert(Me.IsShared)`（`:1505`）不可达；完整解码序列补 `ElseIf Not Me.IsShared Then` 分支（`:1634-1635`，报新码 BC37005），落在 `ParameterCount = 0` 分支（`:1631-1632`）之后、`Else`（`:1637`，内含 `:1638` 的断言）之前。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`（**新增在册**；锚点为 **2026-09-24 工作树**读数，同文件的 §2.21 U1 / §2.25(d) 增行均已计入）—— F05：`TypeKind = Submission` 分支（`AddDefaultConstructorIfNeeded`，`:2744-2762`）按 `isShared` 分叉——共享时走同文件的 `EnsureCtor` 调用点（`:2751`；该 Sub 定义 `:2796-2827`，产无参 `SynthesizedConstructorSymbol`），实例时保持 `New SynthesizedSubmissionConstructorSymbol(...)`（`:2760`）原样。F11：顶层语句收集点的 `Case SyntaxKind.LabelStatement … Exit Select` 丢弃分支删除（**移除上游 `' TODO (tomat): should be added to the initializers` 注释**），顶层标签语句改由既有的 `Case Else`（`:2617-2629`）与其它顶层可执行语句同款收集进实例初始化器序列。
- `Compilers\VisualBasic\Portable\Symbols\Source\SynthesizedEventAccessorSymbol.vb:495`、`Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:66`、`Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93`（**均新增在册**）—— F07：三处 `Debug.Assert(Not …IsImplicitlyDeclared)` 收窄为 `Debug.Assert(Not …IsImplicitClass)`。**登记理由**：三处断言均为**上游行为**（三个文件的最近一次提交都是 `e814cb1 add base compiler`，断言随上游树进入、未被本地改过；改动仅为谓词收窄），本地改动属「修上游在脚本/提交类（`IsScriptClass`）下的可达缺陷」；断言不是控制流（其后 `AddSynthesizedAttribute` 无条件执行），Release 下断言本就被编译掉——**HEAD Release 构建实测**：顶层 `Event` exit 0（`EVENT-OK`），Debug 同形状 exit 35 ⇒ 该收窄零行为变化。
- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 已在册）—— F08：`CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext`（`:2257-2297`）内，**隐式**引用查 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2235-2255`）命中共享上下文则落 BC30369 / 显式引用落 BC30043（`:2266-2273`）；脚本类里的**显式**引用仍报 BC36966（`:2278-2281`，禁令范围＝整个脚本类含成员体，与上游一致），脚本类内的隐式引用直接放行（`:2286-2288`），其余走 `IsInsideChainedConstructorCallArguments`（`:2290-2293`）。**与上游的唯一差 = 两条判定的先后**（上游先判脚本禁令 ⇒ 脚本类 `Shared` 成员里的显式关键字报 BC36966；本 fork 先判共享上下文 ⇒ 报 BC30043），理由与证据见 §2.25(a)。F06 锚点：`IsInSharedInitializerContext()`（`:4744-4748`）与 `BindAwait` 调用结构里的新支（`:4760-4761`，报新码 BC37341）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Initializers.vb`（**新增在册**）—— F09：`ProcessedFieldOrPropertyInitializers` 增「本路绑定期报过 error」可选入参（`:19-55`，`:51` 签名 / `:54` 合成 `HasAnyErrors`），`Empty` 单例语义不变。F10：顶层语句绑定后补跑只查 `Await` 位置的 walk（`:140` 调用点 → `:238-249` 的 `CheckAwaitInTryHandler`）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb`（§2.4 / §2.19 已在册）—— F10 新锚点：`CheckOnErrorAndAwaitWalker` 增 `onlyCheckAwaitInTryHandler`（`:469` 字段 / `:472` 构造参，**默认 `False` ⇒ `BindMethodBlock` 路径零改动**）与 `VisitBlockOnlyCheckAwaitInTryHandler` 入口（`:523-537`），只保留 BC36943 分支（`:625-636`）。
- `Compilers\VisualBasic\Portable\Compilation\MethodCompiler.vb`（**新增在册**）—— F09：初始化器的静态桶与实例桶各自 `AddRangeAndFree` 进本桶**独立诊断袋**（`:614` / `:623`，绑定调用在 `:609` / `:618`），并把该袋的 error 状态传入 `ProcessedFieldOrPropertyInitializers`；**`declarationErrors`、`:1273` 的逐方法袋与 `:1288` 的发射门表达式形状均不变**（不新增门项）；诊断集合与顺序不变（`AddRangeAndFree` 保序）。
- `Compilers\VisualBasic\Portable\Errors\Errors.vb`、`Errors\ErrorFacts.vb`、`VBResources.resx`、`xlf\VBResources.*.xlf`（13 份；**13 份 xlf 为本条目首次在册**）—— F04 新码 `ERR_ExtensionMethodNotShared = 37005`（`Errors.vb:1636`；占用官方空带 `37004`（`:1634`）与 `37050`（`:1637`）之间的**起始槽位**，该带现余 37006–37049 为空）；F06 新码 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`）；`ERR_NextAvailable` 现值 **37344**（`:1820`，含 §2.21 的 37342 / 37343）。两枚码各走完 `Errors.vb` → `ErrorFacts.vb`（`IsBuildOnlyDiagnostic` 的「非 build-only」清单）→ `VBResources.resx` → 13 份 xlf 全链（每份 `+2` 个 `trans-unit`、`+10/-0` 行，`<target state="new">`）。
- 测试（新增 / 增量，非上游路径）：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMethods\ExtensionMethodTests.vb`、`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedInitializerTests.vb` · `SubmissionTopLevelLabelTests.vb` · `SubmissionEventMemberTests.vb` · `InitializerDiagnosticGatingTests.vb`、`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`，以及 `scripts\verify-vb-compiler-tests.ps1` 的各门期望值同步。
- 改动形状：行为改动集中在 `Symbols\Source\`（构造器分叉 + 顶层语句收集 + 三处断言收窄）、`Binding\`（四条判据 / 一处补跑检查）、`Compilation\MethodCompiler.vb`（初始化器桶的诊断袋归属）。**零公共面**：`ERRID` 是 `Friend Enum`，两枚新码不进 `PublicAPI.*.txt`（实测 `git status` / `git diff --stat` 对 `*PublicAPI*` 零输出）；`Compilers\Core\Portable\CodeGen\` 零改动（崩溃栈顶的 `ILBuilder` / `BasicBlock` 不修，改法是「不让坏形状活到发射期」）。
- 折抵：F09 只让**已报出的**初始化器 error 参与既有发射门（无初始化器 error 的编译逐字节不变）；F10 复用既有 walker 且开关默认关（方法体路径不变）；F04 / F06 / F08 只**新增**诊断；F05 / F07 / F11 只把「编译期零诊断却进程级失败」的形状修成正常——无一条改变合法既有代码的语义。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`SourceMethodSymbol` 的扩展方法早期 / 完整解码序列、`SourceMemberContainerTypeSymbol` 的 submission 构造器分叉与顶层语句收集点（含被移除的 `tomat` TODO）、三处 `AddSynthesizedAttributes` 的断言谓词、`Binder_Expressions` 的 `CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext` 与 `BindAwait` / `IsInSharedInitializerContext`、`Binder_Initializers` 的 `ProcessedFieldOrPropertyInitializers` 与顶层语句绑定尾、`Binder_Statements` 的 `CheckOnErrorAndAwaitWalker`、`MethodCompiler` 的初始化器绑定与 `:1288` 发射门、`Errors.vb` 的 37005 / 37341 / `ERR_NextAvailable`。上游若日后以其它形状实现同类脚本诊断、顶层标签收集或初始化器发射门，按上游形状对齐并回退本 fork 改法。
- 对应设计：`tasks\script-top-level-crashes\`（`README.md` 判定表与分批 / `design-detailed.md` 逐单元蓝图与 pass 条件 / `design-overview.md` / `test-plan.md`）；测试锁定见上一段的四个编译器测试文件与 `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`。

### 2.21 脚本顶层崩溃族 U1–U7 / U9 / U12（修改 + 新增）

改动主体：`tasks\script-top-level-crashes-2\`（覆盖 §2.20 的 F04–F11 未收的第四处断言，以及实施期发现的预存在崩溃；判定表、逐单元蓝图与验收网见该文件夹四件套）。**首次在册文件**：`Symbols\Source\SourceMemberMethodSymbol.vb`（注意与 §2.20 登记的 `SourceMethodSymbol.vb` 不是同一文件）、`Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb`；`Errors\Errors.vb` / `Errors\ErrorFacts.vb` / `VBResources.resx` / 13 份 `xlf` 已在 §2.19 / §2.20 在册，本条目只列新增点。

编译器产品源文件（8 个）：

- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 / §2.20 已在册）—— **U4**：`BindMyBaseExpression` 的**错误路径**（`CanAccessMyBase` 为假，`:2379-2383`）在构造 `BoundMyBaseReference` 时把「取不到基类型」折成 `ErrorTypeSymbol.UnknownResultType`（`:2382`），与同文件 `BindMeExpression` / `BindMyClassExpression` 已有的兜底写法一致。**3-way 注意**：只动错误路径；正常路径（`:2385`）保持上游取法——**此处不得加「脚本类兜底到 `System.Object`」**（待防死清单见 §2.25(a)），普通 `Module` / `Structure` / 有基类的 `Class` 三种对照的诊断与产物不变。
- `Compilers\VisualBasic\Portable\Binding\Binder_Initializers.vb`（§2.20 已在册；本条目 `+66 −0`）—— **U5**：给顶层语句补跑「分支不得离开 `Finally`」判据。调用点 `:144`（紧随 §2.20 F10 的 `:134-140` `CheckAwaitInTryHandler` 调用点），新方法 `CheckBranchOutOfTopLevelFinally`（`:264`）与便宜前置门 `ContainsFinallyBlock`（`:301`，递归 `ChildNodes` 找「子树里含 `FinallyBlock` 的 `TryBlockSyntax`」）。**3-way 注意**：① 前置门必须按**该语句的语法子树**判，**不能**按顶层语句的语法种类判——`Finally` 常嵌在 `For` / `While` / `Using` / `Catch` 里，按种类判会整批漏掉；② 只取 `ERR_BranchOutOfFinally`，其余流分析诊断（可达性 / 未赋值 / 未使用）本条目**不**引入（那是 issue 17 的另一半）；③ 调用点选在「刚绑完该条语句」处，保证 binder 与语句树的归属一致。
- `Compilers\VisualBasic\Portable\Errors\Errors.vb`（§2.19 / §2.20 已在册；本条目 `+3 −1`）—— 两枚新码（见下「两枚新诊断码」）。
- `Compilers\VisualBasic\Portable\Errors\ErrorFacts.vb`（§2.20 已在册；本条目 `+2 −0`）—— 两条 id 加进 `IsBuildOnlyDiagnostic` 的「非 build-only」清单（`:1563` / `:1564`）。**必须同步**，否则 `DiagnosticTests.TestIsBuildOnlyDiagnostic` 抛 `NotImplementedException`、Semantic 门整体挂掉。
- `Compilers\VisualBasic\Portable\Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb`（**新增在册**；本条目 `+7 −8`）—— **U2 + U12**：`If` 判据由 `receiver Is Nothing OrElse receiver.IsMeReference` 扩为 `… OrElse receiver.Kind = BoundKind.PreviousSubmissionReference`（`:27`，让自定义事件走整棵调用树降级这条路），并在 `Else` 分支的 `IsWindowsRuntimeEvent` 之前对接收者显式降级 `receiver = VisitExpressionNode(receiver)`（`:37`）；同时**删掉原 `#If DEBUG` 断言块**（`receiver.Kind = BoundKind.FieldAccess`，在自定义事件上没有后备字段 ⇒ 前提不成立）。保留分支的 `Debug.Assert` 现于 `:33`。**3-way 注意**：本差异是「接收者形状」的 VB 脚本语义知识，落在 VB 降级层；共享 `Compilers\Core\Portable\CodeGen\` **零改动**（`EmitExpression.vb:206-209` 的兜底分支不动）。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`（§2.20 已在册；本条目 `+28 −6`）—— **U1**：新增私有 `AddMethodMember`（`:2696`），`AddMember` 的两个方法分支（`SubNewStatement` 与一般 `Sub`/`Function`，原 `:2572` / `:2586`）由「`CreateMethodMember` 后直接 `AddMember`」改为经它中转；**脚本类**的**实例**构造器在此报新码且**不加入成员表**（共享构造器不受影响）。门的判据是 `Me.IsScriptClass AndAlso methodSymbol.MethodKind = MethodKind.Constructor`（`:2706`）：`IsScriptClass`（`:1295-1300`）为 `DeclarationKind.Script OrElse DeclarationKind.Submission`，恰好是两类脚本类，且与 `DeclarationKind.ImplicitClass` 互斥（`IsImplicitClass` 于 `:1302-1306`），游离语句的隐含类不被卷入；同文件 `:2786`（合成初始化器与入口点）本就以 `IsScriptClass` 为判据。**只判 `TypeKind.Submission` 会漏掉非提交脚本类**：那类脚本类由生成的 `<Main>` 实例化（`Symbols\Source\SynthesizedEntryPointSymbol.vb:258-281` 的 `GetScriptConstructor()` + `Dim script As New Script()`），漏判时同一崩溃原样存在（实测：`IsSubmission=False`、`TypeKind=Class`、`IsScriptClass=True`，`GetDiagnostics` 与 `Emit` 均抛 `InvalidCastException`：`SourceMemberMethodSymbol` → `SynthesizedConstructorBase`）。**不加入成员表是必须的**：只要两个 `.ctor` 同时在场，`NamedTypeSymbol.GetScriptConstructor`（`Symbols\NamedTypeSymbol.vb:697-700` 的 `InstanceConstructors.Single()`）就抛；改用该判据后，非提交脚本类的空参用户构造器不再让 `EnsureCtor`（`:2796-2827`）提前返回，仍合成唯一的实例构造器。**3-way 注意**：① 本改动与 §2.20 的 F05（同文件 `AddDefaultConstructorIfNeeded`，`:2744-2762` 的提交构造器按 `isShared` 分叉）是同一文件的两处，评审时一起看。② **这道门是 fork 新增、上游无对应实现**：上游继承的是崩溃本身，C# 与 VB 同形（读码实证，未运行 C# 复现）—— C# 的 `IsScriptClass` 与 VB 逐字相同（`Compilers\CSharp\Portable\Symbols\Source\SourceMemberContainerSymbol.cs:1022-1029`），同样在 `IsScriptClass` 为真时合成初始化器与入口点（`:5721-5727`，对应 VB 的 `SourceMemberContainerTypeSymbol.vb:2786`），入口点工厂按 `compilation.IsSubmission` 分流到 `ScriptEntryPoint`（`Symbols\Synthesized\SynthesizedEntryPointSymbol.cs:27-44` / `:482`），其 `CreateBody` 调 `_containingType.GetScriptConstructor()`（`:516`），C# 的实现同为 `(SynthesizedInstanceConstructor)InstanceConstructors.Single()`（`Compilers\CSharp\Portable\Symbols\NamedTypeSymbol.cs:590-593`），且 C# 侧没有该形状的诊断（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:2099` 中 submission 相关只有 `ERR_ScriptsAndSubmissionsCannotHaveRequiredMembers = 9045`）；C# 里顶层成员不能声明实例构造器，该形状在 C# 里不可拼写。**合并时不得把这道门当「本地无谓改动」精简，也不得按上游形状覆盖**。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberMethodSymbol.vb`（**新增在册**；本条目 `+15 −3`）—— **U3**：`BindSingleHandlesClause` 的 `Select Case ContainingType.TypeKind` 合法分支并入 `TypeKind.Submission`（`:778`，与 `Class` / `Module` 同列）。**U9**：`isFromBase` 分支的 `DirectCast(Me.ContainingType, SourceNamedTypeSymbol)` 换成 `TryCast` + 判 `Nothing` 则报 `BC37343` 并 `Return Nothing`（语句 `:707-711`，`GetOrAddWithEventsOverride` 现于 `:713`，判据注释块 `:702-712`）。**3-way 注意**：`DirectCast` 是本 fork 相对上游的形状差异点（上游对提交类根本走不到这个分支）；该分支原为上游代码，本 fork 只把「必然崩溃」改成「报诊断」。
- `Compilers\VisualBasic\Portable\VBResources.resx`（§2.19 / §2.20 已在册；本条目 `+6 −0`）—— 本条目**两枚新码**的英文文案（`ERR_SubmissionCannotDeclareInstanceConstructor` / `ERR_WithEventsVariableNotInContainingType`），无其它条目改动。

资源 / 本地化（13 份，§2.20 已在册；本条目每份 `+46 −0`）：

- `Compilers\VisualBasic\Portable\xlf\VBResources.{cs,de,es,fr,it,ja,ko,pl,pt-BR,ru,tr,zh-Hans,zh-Hant}.xlf` —— 每份净增 **9 条 `trans-unit`**（`<target state="new">`、`target` 与 `source` 逐字等于 resx `<value>`）：本条目两枚新码各 1 条，另 7 条是 **U6a 补齐的既有失同步条目**（`ERR_ExpressionTreeContainsAbstractStaticMemberAccess`、`ERR_LoadDirectiveOnlyAllowedInScripts`、`ERR_PPLoadFollowsToken`、`ERR_PPReferenceFollowsToken`、`ERR_ShebangDirectiveOnlyAllowedInScripts`、`ERR_ShebangDirectiveNotOnFirstLine`、`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces` —— 这 7 条的 resx 侧本来就有，只是 13 份 xlf 缺条目，`Directory.Build.props:4-5` 的 `ErrorOnOutOfDateXlf=false` / `UpdateXlfOnBuild=false` 让任何构建门都不报）。**既有条目逐字节未改**（比对时两侧 EOL 已归一）。**3-way 注意**：13 份 xlf 的既有条目顺序本就与 resx 顺序有倒挂，新条目按**该文件里的本地邻居**定位插入，不按 resx 顺序重排；上游若整体重生成 xlf，按上游结果接受即可（新条目已带 `state="new"`）。

测试与门脚本（非上游路径）：

- `Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`（`+496 −0`，新增 **20 条** `<Fact>`/`<Theory>`：W1 的 U4 4 条 + W2 的 U1/U5 12 条 + U1 非提交脚本类 4 条）。后 4 条走**非提交脚本编译**（`VisualBasicCompilation.Create` + `TestOptions.Script` + `WithScriptClassName`），与既有 `CreateSubmission` 用例分属两条构造路径；Semantic 门实测总数随之由 `5822/5718/104/0` 变为 `5826/5722/104/0`，`scripts\verify-vb-compiler-tests.ps1` 的 Semantic 期望值同步为该值。
- `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`（`+415 −8`，该类的用例数 **25 → 44**，新增 **19 条**：W1 的 U2/U3/U4 7 条 + W2 的 U1/U5 8 条 + U9 的 4 条，其中 `SameSubmissionHandles_StillDelivers` 是控制锁）。
- `Scripting\VisualBasicTest\ScriptModeConformanceTests.vb`（新增，716 行，内含 `ScriptModeDeclarationConformanceTests` 22 条）/ `ScriptModeStatementConformanceTests.vb`（新增，675 行，41 条）/ `ScriptModeSubmissionConformanceTests.vb`（新增，491 行，28 条）—— **U7** 的脚本模式符合性矩阵（**89 条**），U12 又在其上补 2 条自定义事件成功臂 ⇒ 三个类现共 **91 条**（断言臂 `AssertRuns` / `AssertReports` / `AssertEmits` / `AssertReplSession`，全部内存 API、无副作用）。
- **`Scripting` 程序集合计**：用例数 **344 → 454**（收口实测 `-automated`：`TestCasesToRun = 454`、454 passed / 0 failed）。344 由两路互证得出——收口实测 454 减去「唯一被改的既有文件 +19 与三个新文件 +91」，与 W1 验证者独立跑出的基线 344 一致。
- `scripts\verify-vb-compiler-tests.ps1`（`+1 −1`）—— **只改数字**：Semantic 门期望值 `5806/5702/104/0` → `5826/5722/104/0`（其余六门与基线逐字一致，本任务实测零增量）。判据未改。

文档（`InternalDevDocs\`）：

- `issues\README.md` + **17 份** issue 正文——新增 9 份（12–20；其中 19 / 20 即本条目两个非计划单元），另 8 份既有正文改「状态 / 锚点 / 计数」（04–11 的 8 份）。
- `spec\spec-scripting-dialect.md`（`+19 −6`）—— 顶层 `GoTo` 条目的 Decision 由「本规范不保证」改为**受本规范保证**（含新增语义示例与「重复标签报 BC30094」一句）；`Handles` 条目改写为「提交类不合成 hookup 构造器（承载 hookup 的实例构造器就是编译器已合成的提交构造器）+ 跨提交 / 宿主对象的变量报 `BC37343`」；诊断分层那句由「其余限制在绑定期报出」改为「解析之后、绑定期与包含类型的成员构建期报出」（`BC37342` 报在成员构建期，见上）；`BC37342` 的条件列改述两类脚本类（提交类由宿主实例化、非提交脚本类由生成的入口点实例化，都只有编译器合成的那一个实例构造器槽位），C# 对照列收紧为「C# 无法在提交类上声明实例构造器、且在声明了实例构造器时不合成提交构造器」；Testing 段的 Restriction tests 补入 `BC37341` / `BC37342` / `BC37343` 三枚码与各自不受影响的同提交形式。
- `tasks\script-top-level-crashes-2\`（新增，四件套 4 份）。
- 本文件。

**两枚新诊断码与官方错误码带的落点**：

- **`BC37342` `ERR_SubmissionCannotDeclareInstanceConstructor`**（`Errors.vb:1817`）—— 脚本类不支持声明**实例**构造器（提交类的实例由宿主创建、非提交脚本类的实例由生成的入口点创建，构造器都由编译器合成），初始化代码放顶层语句。触发面：提交类与非提交脚本类的顶层 `Sub New()` / `Sub New(x)`（`Protected` / `Private` / `Public` 同判定）；`Shared Sub New()` 与脚本类内嵌套普通类的构造器不受影响。
- **`BC37343` `ERR_WithEventsVariableNotInContainingType`**（`Errors.vb:1818`）—— `Handles` 引用的 `WithEvents` 变量来自**上一提交或宿主对象**（提交类只是可见它、并未声明它）。触发面：跨提交 / 宿主对象的 `WithEvents` 变量上的 `Handles` 子句。
- **落点**：两枚码都落在 fork 原有增长带的**前沿尾部**，紧接 §2.20 F06 的 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`）之后连续占用 **37342 / 37343**，`ERR_NextAvailable` 随之由 37342 推进为 **37344**（`:1820`）。与 §2.19 的 `ERR_ExpressionTreeContainsAbstractStaticMemberAccess = 37340`（`:1815`）是同一模式。注意 `Errors.vb:1601-1606` 的「Fork error-numbering policy」注释约束的是**官方间隙**型新增（36959 / 36967 / 37002 …，两侧都有既有编号）；尾部前沿连续占用是第二类，两种都在本 fork 既有实践中。
- **零公共面**：`ERRID` 是 `Friend Enum`，两枚码**不进** `PublicAPI.*.txt`（本任务实测 `git status` 对 `*PublicAPI*` 零输出）。
- **未来撞号风险（3-way 要点）**：上游自己的下一个 37xxx 错误由上游 `ERR_NextAvailable` 决定；若上游在同一带内新增错误，本 fork 的 37340–37343 需要按上游形状对齐（与 §2.20 登记的 37005 / 37341 同一处置）。

- 改动形状：行为改动集中在 `Binding\`（U4 一处兜底 + U5 一处判据补齐）、`Symbols\Source\`（U1 的成员表闸 + U3 的枚举扩项 + U9 的 `TryCast` 判据）、`Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb`（U2 / U12 的接收者判据与降级）；资源侧是两枚新码 + 13 份 xlf 的失同步补齐。**零公共面**：`ERRID` 是 `Friend Enum`；`Compilers\Core\Portable\CodeGen\` **零改动**（`CodeGen\EmitExpression.vb:206-209` 的 `Throw ExceptionUtilities.UnexpectedValue` 不修，改法是「不让坏形状活到发射期」）。
- 折抵：U2 / U3 / U4 / U12 只让**本族形状**从「崩」变「正常跑」（普通编译上下文对照零行为变化）；U1 与 U9 只**新增**诊断（用户可见行为由「进程终止」变「一条编译错误」）；U5 只**新增**一条既有诊断（`BC30101`）在顶层语句路径上的报点；资源侧只补 13 份 xlf 的缺失条目。无一条改变合法既有代码的语义。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`Binder_Expressions.BindMyBaseExpression` 错误路径、`Binder_Initializers` 的顶层语句绑定尾（`CheckBranchOutOfTopLevelFinally` / `ContainsFinallyBlock`）、`SourceMemberContainerTypeSymbol.AddMethodMember`（判据 `IsScriptClass`，覆盖提交类与非提交脚本类；与 §2.20 的 F05 提交构造器分叉同文件、一起看）、`SourceMemberMethodSymbol.BindSingleHandlesClause` 的 `Select Case` 与 `isFromBase` 分支、`LocalRewriter_RaiseEvent.VisitRaiseEventStatement` 的接收者判据（含被删除的 `#If DEBUG` 断言块）、`Errors.vb` 的 37342 / 37343 / `ERR_NextAvailable`。上游若日后以其它形状实现同类脚本诊断、`Handles` 宿主枚举或事件降级，按上游形状对齐并回退本 fork 改法。
- 对应设计：`tasks\script-top-level-crashes-2\`（`README.md` 判定表与非范围 / `design-overview.md` 总体设计与全称主张剪枝 / `design-detailed.md` 逐单元蓝图与 pass 条件 / `test-plan.md` L1–L4 矩阵与无副作用纪律）；判定与依据的会议 / 提案出处见 `issues\issue-cross-submission-handles-clause-crash.md`（19）。

### 2.22 脚本宿主对象按反射类型解析（修改；修复 issue 22）

`Compilers\` 下两个文件、三处，全部落在 `Friend` 面。

- `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb`（`+14 −4`）—— `GetHostObjectTypeSymbol`（`:933-957`）由「反射 `FullName` 当元数据名用」改为**先 `GetTypeByReflectionType`、失败再落回字符串路径**（镜像 `CSharpCompilation.cs:1873`）；字符串回退（含 `+`→`.` 的嵌套兜底）**保留**于 `:945-952`，`:935` 的 `FullName Is Nothing` 前置检查收窄为该回退的门。**语义变化**：`globalsType` 为构造泛型（顶层或嵌套）/ 数组 / 泛型外层里的嵌套类型时，宿主对象由「不绑定（`BC30451`）」或「断言终止（`EXIT=35`）」变为**正常绑定**。字符串路径能解析的形状（普通类型 / 继承闭合的泛型 / 非泛型外层里的嵌套类型）逐字节不变（实测：`HostObjectBinding_ClosedGenericBaseMembers`、`HostObjectBinding_PrivateClass`、`HostObjectInRootNamespace` 等既有格全绿）。
- `Compilers\VisualBasic\Portable\Symbols\AssemblySymbol.vb`（`+21 −2`）—— `GetTypeByReflectionType` 的嵌套分支（`:741`）：`:765-772` 在递归解析最外层类型前用「本类型为外层携带的实参」把外层泛型定义闭合（`Type.DeclaringType` 对嵌套类型返回的是**外层泛型定义**，是个开放类型，原样递归会撞 `:723` 的 `Debug.Assert(Not type.ContainsGenericParameters)`）；`:774` 实参游标初始化改为 `rootArity`；`ApplyGenericArguments`（`:822`）增 `length = 0` 早退（`:833-837`）。
- **C# 侧不改**（`Compilers\CSharp\Portable\Symbols\AssemblySymbol.cs` 保持上游形状）—— 本 fork 的产品路径是 VB（`Scripting\VisualBasic` / `Interactive\vbi`），C# 脚本侧无产品入口。**这是本条目的一处有意两侧不对称**：同一形状在 C# 上仍断言终止（实测 `cs:782` ← 递归 `cs:830`），在 VB 上正常绑定。
- **零公共面**：三处均为 `Friend`/`Private`（`GetHostObjectTypeSymbol` / `GetTypeByReflectionType` / `ApplyGenericArguments`），`PublicAPI.*.txt` 零动（实测 `git status` 对 `*PublicAPI*` 零输出）。`Compilers\Core\` **零改动**（`IsValidHostObjectType` / `ValidateScriptCompilationParameters` 为既有上游代码，只被消费）。
- 折抵：「崩编译器是 bug」是按作者原则的判定出口，两条症状都属「修错」而非「回归」：`BC30451` 侧改的是**从未绑定**的宿主对象（C# 同格 `InteractiveSessionTests.cs:1545` 本来就通过），断言侧改的是**进程级失败**。无一条改变合法既有代码的语义。
- 合并前评估义务：合并前读本条目对 `VisualBasicCompilation.GetHostObjectTypeSymbol` 与 `AssemblySymbol.GetTypeByReflectionType` / `ApplyGenericArguments` 做 3-way 评审。上游若日后自行给 C# 侧补同款嵌套修复（或对 `GetTypeByReflectionType` 做等价重构），**按上游形状对齐**——届时两语言可重新同形；本 fork **不得**因「C# 那边没修」而回退 VB 侧的修法。
- **有意分歧·不镜像 C# 的 `MissingMetadataTypeSymbol` 兜底（合并时不得补回）**：C# 在反射解析失败时返回 `MissingMetadataTypeSymbol.TopLevel`（`CSharpCompilation.cs:1881-1885`），本 fork 的 VB 侧**有意保持 `Nothing`**——该兜底在 VB 侧无消费点，补回等于引入死代码。理由与逐调用方取证见 `issues\issue-constructed-generic-host-object-not-bound.md` 的「兜底评估：VB 侧不加 `MissingMetadataTypeSymbol`（证据）」节：VB 四个调用方全部不消费错误类型——`Binder_Lookup.vb:921`（成员查找）、`:2046`（补全符号表）、`Lowering\SynthesizedSubmissionFields.vb:56`（`<host-object>` 字段）三处显式过滤 `SymbolKind.ErrorType`，第四处 `Binder_Expressions.TryBindInteractiveReceiver`（`:2620-2629`）以 `TypeSymbol.Equals` 逐个比对宿主类型、错误类型永不命中；⇒ 返回错误类型与返回 `Nothing` 在全部调用点行为一致（C# 侧这份兜底的唯一可见作用是由 `HostObjectModelBinder` 转成 `CS0103`，VB 无对应 binder）。**合并义务**：上游若给 VB 补兜底语义、或给 VB 引入消费错误类型宿主的 binder，落点是 binder 侧的 `SymbolKind.ErrorType` 过滤面，**不在** `GetHostObjectTypeSymbol`；届时按上游形状做 3-way 评审并对齐，回退本 fork 的 `Nothing` 语义。
- 对应缺陷：`issues\issue-constructed-generic-host-object-not-bound.md`（22）；用例锁定 `Scripting\VisualBasicTest\ScriptModeHostObjectConformanceTests.vb`（`HostObjectBinding_PublicGenericClassMembers` 改写 + 6 条新增），`Scripting` 程序集 480 → 486 passed / 0 failed。

### 2.23 元数据引用别名在 VB 侧生效（修改；合并时必须保住）

**本条目登记的既有本地偏差**随 commit `80eff5f` 进入，**不属** §2.22 的改动面（§二·补 的 `80eff5f` 行只给文件面筛结果，本条是该面的规范化描述）。规范化描述在 `spec\spec-reference-directive.md`（别名规则：无别名或含 `global` 的引用并入全局命名空间，带其它别名的引用**不**并入；VB 无 `extern alias` ⇒ 隐藏是绝对的）。

- `Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:101-121`（`ConstituentGlobalNamespaces`）—— 组装合并全局命名空间时，只并入 `referencedAssemblies(i)` 的 `GlobalNamespace` **当** `referenceManager.DeclarationsAccessibleWithoutAlias(i)` 为真（判据在 `:114`）；文件内 `:106-109` 的注释是本 fork 自写的 deviating 标注（「This mirrors C# (extern aliases) …」）。**上游 VB 无这道过滤**（同文件上游形状直接并入每个引用程序集模块的全局命名空间）。
- `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs:721-725` —— `DeclarationsAccessibleWithoutAlias`：`aliases.Length == 0 || aliases.IndexOf(GlobalAlias) >= 0`。**共享层既有 API，本 fork 未改**（C# 侧同款消费者：`CSharpCompilation.cs:1393`、`:1417`、`Binder\ImportChain.cs:148`）；本 fork 的改动只是**让 VB 也消费它**。
- 宿主侧别名来源（`Scripting\Core\`，本地文件）：`Script.cs:237-239` 对宿主对象程序集施加 `<host>` 且 `WithRecursiveAliases(true)`；`Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:29` 对补位程序集施加 `<implicit>`。
- 折抵：本 fork 的 VB 有意尊重元数据引用别名（原版上游 VB 忽略别名）——`Scripting` 的 `<host>` / `<implicit>` 语义依赖此行为（别名程序集里的类型从 VB 源码完全不可达），且 `/nostdlib` 下命令行编译器对真实核心库施加非全局别名时同样依赖它。
- 合并前评估义务：合并前读 `MergedNamespaceSymbol.ConstituentGlobalNamespaces` 与 `spec\spec-reference-directive.md` 做 3-way 评审；**上游若日后给 VB 补上同类别名过滤，按上游形状对齐并回退本 fork 改法；上游若仍然忽略别名，不得把这道过滤当「本地无谓改动」精简掉**（会连带打断 `<host>` / `<implicit>` 的隐藏语义）。同时核 `DeclarationsAccessibleWithoutAlias` 在上游是否仍只有这三个 C# 消费者（若上游给 VB 也接上，本条目随之关闭）。
- 对应规范与测试：`spec\spec-reference-directive.md`；`Scripting\VisualBasicTest\ScriptModeReferenceAliasTests.vb`（本任务 U4 单元）。

### 2.24 脚本模式的测试与文档文件面：上游原有测试文件的增量 + fork 自有测试/文档面（修改 + 新增）

**产品面改动已由 §2.22 在册，本条不重复登记**：本单元触及 `Compilers\` 的改动只有 `6772cc7` 的 `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb` 与 `Symbols\AssemblySymbol.vb` 两处（泛型 `globalsType` 宿主对象一档，即 §2.22 的 `+14 −4` / `+21 −2`）；工作区对 `Compilers\` 零改动（`git diff --name-only HEAD -- Compilers/` 零输出，**实锤**）。本条只登记 `Scripting\VisualBasicTest\` 与 `InternalDevDocs\` 的文件面。这些增量绝大多数落在 **fork 自有路径**（无 3-way 面）；**唯一有 3-way 面的是对上游原有文件的一处增量**。

**（a）上游原有文件（合并时必须保住）**

- `Scripting\VisualBasicTest\ScriptTests.vb`（`+176 −0`，纯追加于文件末尾，`6772cc7`）—— 新增 `#Load` 返回语义族 **7** 条 `<Fact>`（`TestMultipleLoadedFiles_FirstReturnDecides`、`TestLoadedFileReturnPrecedesTheMainTreeReturn` 等；C# 蓝本 `CSharpTest\ScriptTests.cs:643/661/684/707/742/777/808/826`）。**该文件为上游原有**（判据见下）。**3-way 注意**：这些断言锁的是 `#Load` 目标树并入**同一提交**、外层 `Return` 与载入树的 `Return` 在同一条提交方法里竞争（§2.5 的产品面），**不是**上游 C# 的「载入文件独立方法」形状；上游若重写该文件或改 `#Load` 的并入形状，按上游形状对齐并重评这 7 条，**不得**在合并时整体采用上游版本而丢掉这 176 行。该文件自 `c490340` 起被本 fork 反复改动（`git log -- Scripting/VisualBasicTest/ScriptTests.vb` 共 **11** 个提交），合并前须逐段 3-way。

**（b）fork 自有路径（无 3-way 面；登记以正文件面分类）**

- `Scripting\VisualBasicTest\` —— **上游目录**（基准 commit 的 `src\Scripting\VisualBasicTest\` 内含 `.vbproj`、`My Project\launchSettings.json` 与 **5** 个 `.vb`：`ScriptTests.vb` / `InteractiveSessionTests.vb` / `ObjectFormatterTests.vb` / `CommandLineRunnerTests.vb` / `PrintOptionsTests.vb`；**无** `Helpers\` 目录）。**§2.7(b)只在 (b) 里提到该目录**（登记「`Scripting\VisualBasicTest\` 下的 fork 新增测试文件」），**未列入 (a) 的「上游目录 ⇒ 合并时必须逐文件比对上游改动」清单** ⇒ 读 §2.7 时**不得**把该目录整体当 fork 新增。§2.25 新增的 fork 自有文件：**15** 个 `.vb`（`ScriptModeApiSurfaceConformanceTests` / `ScriptModeArgsTests` / `ScriptModeErrorHandlingAndStaticsTests` / `ScriptModeLexicalConformanceTests` / `ScriptModeObjectFormatterTests` 与配套 `ScriptModeObjectFormatterFixtures` / `ScriptModeOperatorConformanceTests` / `ScriptModeParserArmConformanceTests` / `ScriptModePdbTests` / `ScriptModeQueryAndXmlConformanceTests` / `ScriptModeReferenceAliasTests` / `ScriptModeRuntimeFunctionTests` —— 以上 `6772cc7`；`ScriptModeTopLevelInferenceTests` —— `441395c`；`ScriptModeNestedContainerConformanceTests` / `ScriptModeExpressionArmConformanceTests` —— 未跟踪）＋ `Helpers\` **9** 个文件（上游基准树内无该目录）。其中 `ScriptModeReferenceAliasTests.vb` 的规范对应关系已在 §2.23 在册、`ScriptModeHostObjectConformanceTests.vb` 的用例锁定已在 §2.22 在册。
  该目录 fork 自有文件的**现值（2026-09-24 清点，`find` 复算，排除 `obj\` / `bin\`）**：`.vb` **34** 个（该目录非 `Helpers\` 的 `.vb` 共 39 个，减上游原有 5 个）＋ `Helpers\` **9** 个 = **43** 个文件。§2.7(b) 记的「27 个测试与 Helpers 文件」是 **2026-09-11 的清点读数**，与现值差 16（该差值未逐项归因，标 `Suspect`）；两处数字**不互相换算**，合并前复跑判据时**按现值 43 起数**。
- `InternalDevDocs\spec\spec-scripting-dialect.md` —— Testing 节补「Container tests」与「Syntax-family tests」两节（嵌套容器面与语法族面）。`InternalDevDocs\` 全树为 fork 新增（§2.7(b)），无上游对照。
- 说明：`6772cc7` 的 `M` 文件里另有 5 个（`InteractiveSessionReferencesTests.vb` / `ScriptModeHostObjectConformanceTests.vb` / `ScriptModeStatementConformanceTests.vb` / `ScriptModeSubmissionConformanceTests.vb` / `ScriptOptionsTests.vb`）是 **fork 自有文件**的增量（上游基准树内无这些路径），无 3-way 面。

**判据（上游/本地的分类口径，实锤）**：本机 fork 仓**不含**基准 commit 对象（`git cat-file -t 0e401fcf66cbfd4aeb27a78408ab91cab3a6f207` 报 `could not get object info`，**实锤**）⇒ §2.7 的 `cat-file -e` 判据**在 fork 仓内当前复跑不了**：任一路径都返回 128，含 §2.7 自记 rc=0 的 `src\Scripting\Core\ScriptOptions.cs` ⇒ 该通道现为**假阴性**，不得据其判「fork 新增」。本条的（a）/（b）分类改取同版本的 `{{Roslyn}}` 仓 —— **其 HEAD 即基准 commit**（`git log -1` = `0e401fcf66c 2026-07-27`，**实锤**），以该仓的 tracked 树核对上游路径：`git ls-tree -r --name-only HEAD -- src/Scripting/VisualBasicTest` 实测 **7** 条（5 个 `.vb` + `.vbproj` + `My Project\launchSettings.json`，实锤）。**换机器或取到基准对象后，§2.7 与本条的分类都须复跑核对。**

**合并前评估义务**：合并前读本条对 `Scripting\VisualBasicTest\ScriptTests.vb` 做逐段 3-way 评审（上游侧改动与本 fork 的 176 行同处一文件；`6772cc7` 对 `Scripting\VisualBasicTest\` 的净改动为 `+7638 −58`，其中 58 行删除**全部**落在 `ScriptModeHostObjectConformanceTests.vb`（`+278 −58`，该文件的改写已在 §2.22 在册），其余 `M` 文件均为纯增量）；(b) 的路径与上游不重叠，按 fork 自有文件处理。

**对应规范与测试**：`spec\spec-scripting-dialect.md`（方言规范，其 Testing 节含补写的两节）；`tasks\script-mode-coverage-parity\`（`test-plan.md` §C 的语法 ledger 与 §C.3.P / §C.3.Q 两个单元的收口读数）。

### 2.25 脚本类显式关键字判据的净分歧 + 顶层自动属性后的语句窗口（修改；两条未提交收口）

> **状态：全部未提交（2026-09-24 工作树）**，commit 号一律不预填；提交后须回填 commit 号并复跑本节的锚点。对应：`tasks\script-class-explicit-keyword-parity-revert\`（含其前置 `tasks\script-class-explicit-me-scope\` 的改判与回退）、`tasks\auto-property-top-level-gate\`。issue：28（撤销改判）、30。

**（a）`Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 / §2.20 / §2.21 已在册）—— 与上游只差"两条判定的先后"**

- 净分歧：`CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext`（`:2257-2297`）先判 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2266-2273`），后判脚本类显式禁令（`:2278-2281`）。⇒ 脚本类的 `Shared` 成员里显式 `Me` / `MyClass` / `MyBase` 报 **BC30043**（隐式为 BC30369），而**上游此处先报 BC36966**。禁令的**范围**与上游一致：整个脚本类，成员体与其中 lambda 也算。
- 理由（分叉登记，非"顺手改"）：与 C# 脚本方言的判定次序同形——`Compilers\CSharp\Portable\Binder\Binder_Expressions.cs:45-49` 的 `HasThis` 先测容纳成员是否 `IsStatic`（命中则 `inStaticContext = true` 直接返回），`:55-73` 才是脚本类对显式 `this` / `base` 的门；调用方 `:2636-2639` 据此在两条错误码之间二选一。判据＝照 C# 的实际策略定。
- **回退掉的中间形态**（合并时不要被这些字面复活）：曾把 BC36966 收窄到"顶层脚本代码"（辅助函数 `IsBindingTopLevelScriptCode` / `GetNearestNonLambdaContainingMember`，已删）；曾让 `MyBase` 在脚本类里兜底到 `System.Object`（`GetBaseTypeOfScriptClass`，已删；`BindMyBaseExpression` 正常路径 `:2385` 恢复上游取容纳类型基类型）；曾把 `CanAccessMyBase` 的断言放宽为 `IsClassType OrElse IsScriptClass`（`:2302` 区，已恢复为 `Debug.Assert(ContainingType.IsClassType)`，实测无路径撞上）。§2.21 的 U4 错误路径兜底（`:2379-2383`）**保留不动**。
- 3-way 注意：只评审这一个函数的**顺序**；上游若改动该函数体（含新增谓词），须保住"共享判定在前"这一条并同步 `ScriptSemanticsTests.vb` 的 `Shared` 三格与 `ScriptModeStatementConformanceTests.vb` 的体内五格。

**（b）解析器三文件（均为上游同名文件；新增在册）—— 脚本顶层自动属性之后的语句窗口**

- `Parser\BlockContexts\BlockContext.vb`（**新增在册**）—— 新增可下读的语境判据 `Friend Overridable ReadOnly Property AcceptsScriptTopLevelStatement`（基类默认 `Parser.IsScript AndAlso BlockKind = SyntaxKind.CompilationUnit`），位置在 `BlockKind`（`:175` 区）之后。
- `Parser\BlockContexts\PropertyBlockContext.vb`（**新增在册**）—— 覆盖该判据为 `Not IsPropertyBlock AndAlso PrevBlock IsNot Nothing AndAlso PrevBlock.AcceptsScriptTopLevelStatement`（紧接 `IsPropertyBlock`，`:25-29` 区）：自动实现属性的块只是占位，须把"下一条条目算不算语句"让给下层语境回答。
- `Parser\Parser.vb`（**新增在册**；锚点为 **2026-09-24 工作树**读数，区分 Case 标签行与判断行）—— `IsTopLevelScript`（`:85-93`）改走上述判据；标识符臂的判断行 `:819` 改为 `Context.BlockKind = SyntaxKind.CompilationUnit OrElse IsTopLevelScript`（普通编译仍走 `ERR_ExecutableAsDeclaration`）；`AddHandler` 臂（Case 标签 `:835`、判断 `:838`）与 `RemoveHandler` 臂（Case 标签 `:843`、判断 `:844`）改走 `IsTopLevelScript`。**刻意未动**：`:777`（数字字面量臂里既有的 `IsTopLevelScript` 调用）、`:850`（裸 `RaiseEvent` 臂）、`:852` / `:855`（顶层裸 `Get` / `Set` 仍按块属性访问器处理）、`:884`（specifier 报点）、`DeclarationContext.vb:234-242`（`ERR_ExecutableAsDeclaration` 落点）。
- 动机与实测：自动属性走惰性关块，而 BC30188 在 `Parse()` 阶段就按"仍在声明语境"下判词 ⇒ 合法脚本形状被拒（issue 30，触发面与 `=` 初始化器无关，判别量是后继语句首 token）。C# 侧不存在该机制（`Compilers\CSharp\Portable\Parser\LanguageParser.cs:727-729`、`:2590-2615` 每条目自洽分发；顺序规则 CS8803 显式只在 `!IsScript` 生效 `:776-793`）⇒ **向 C# 收敛，不是分叉**。
- 实测面（档 1；读数 2026-09-23，**未经复跑**）：`Syntax` 门 **4098/4095/3/0**（新增 `ScriptTopLevelAfterAutoPropertyTests.vb` 28 条，含 4 条增量重解析锁）、`Semantic` 门 **5847/5743/104/0**（新增 `ScriptTopLevelAutoPropertyTests.vb` 4 条 + `ScriptSemanticsTests.vb` 的 A3 形状回收）、`Scripting\VisualBasicTest` 直跑 **745/0**；Symbol / Emit / IOperation / CommandLine / Phase2 五门数字逐字不变 ⇒ 普通编译的 BC30188 报错面未缩小。
- 3-way 注意：新增的是**虚属性**（不改任何既有签名）；合并时若上游调整 `ParseCompilationUnitCore` 的驱动次序或 `PropertyBlockContext` 的关块时机，须重跑 `ScriptTopLevelAfterAutoPropertyTests` 的 `G9_*` 四条（全量 vs 增量重解析一致性）再判定。

**（c）`Compilers\VisualBasic\Portable\Semantics\OverloadResolution.vb`（新增在册）—— 跨提交同名 `Method`/`Property` 的决策层择一**

- 净分歧：规则链里新增一段"交互链槽位择一"。**当前锚点（提取后）**＝谓词 `TryGetSubmissionSlotWinner`（`OverloadResolution.vb:1934-1981`）+ `CombineCandidates` 内唯一调用点（`:4495-4497`，位置在 `ShadowBasedOnReceiverType` 那组之后；不改上游 `ElseIf` 链，对 `HEAD` 的净规模 `+61 −0`）。生效条件：`signatureMatch` 为真、两候选 `ContainingType.TypeKind` 均为 `Submission`、`DeclaringCompilation` **不同**、且两侧 `GetSubmissionSlotIndex()` **都 ≥ 0 且不相等** ⇒ 槽位大者胜。
- 与 C# 的关系＝**同规则、等生效范围**（不是"同一函数同一位置"）：`Compilers\CSharp\Portable\Binder\Semantics\OverloadResolution\OverloadResolution.cs:2504-2523`（注释原文 "Otherwise: Position in interactive submission chain. The last definition wins."）位于 `BetterFunctionMember`（`:2136`）末段，但被"参数类型同一性"前置门挡在 `allSame` 之后 ⇒ 实际生效范围与 VB 侧的 `signatureMatch` 门一致（下条②有实跑对照）。槽位来自共享层 `Compilers\Core\Portable\Compilation\Compilation.cs:518-534`。**VB 侧原先完全没有 submission 概念**（改前该文件 `Submission` 命中 0）⇒ 本条是"把 C# 的行为补进 VB"，不是恢复上游；上游若重排该规则链，须保住 `TryGetSubmissionSlotWinner` 的**四道守卫**（两候选皆 `Submission`、不同 `DeclaringCompilation`、槽位非负、槽位不等）与"只在 `signatureMatch` 下调用"这一条。
- **两处缺口已收（SW-TB-F04）**：
  - ① 槽位**相等/负值**路径：判据从 `CombineCandidates` 内联块**提取为单一谓词** `Friend Shared Function TryGetSubmissionSlotWinner(leftCandidate, rightCandidate, ByRef leftWins, ByRef rightWins)`（`OverloadResolution.vb:1934-1981`），唯一调用点 `:4495-4497`（仍在 `signatureMatch` 门后，形状＝`If signatureMatch AndAlso TryGet… Then GoTo DeterminedTheWinner`）⇒ 纯提取、行为逐字不变；谓词设为 `Friend` 是为让守卫可被**直断**（IVT 已在 `Microsoft.CodeAnalysis.VisualBasic.vbproj:60`），新增格 `F4_EqualSubmissionSlots_NoWinnerIsReported`、`F4_NonSubmissionCandidates_NoWinnerIsReported`（含"普通编译槽位为负"的实测断言）、`F4_DifferentSubmissionSlots_TheLaterSubmissionWinsInEitherArgumentOrder`、`F4_SubmissionWithoutCodeToEmit_KeepsThePredecessorSlot`。经三条路径实测确认"经公共查找层造出等槽位候选对"**不可构造**（无代码提交不推进槽位却不声明任何成员；两条链无法共享同一 `previousScriptCompilation`；从元数据载入的提交类型不带 `TypeKind.Submission`——最后一条只到读码档），故按替代方案直断谓词。
  - ② **落点等已证**：原判据只到读码的那条疑问（"同 arity、参数类型互不更优"的一对是否 C# 会兜底选较新提交）已被**探针实测**推翻——同形状 C# 读 `CS0121`（证据＝**已运行**：探针 `tmp\f04-cs-probe\Program.cs`，读数 `tmp\vortex-logs\submission-member-redeclaration-tiebreak\04-implementer-f04.md:33`；读数 2026-09-24，**未经复跑**），与 VB 的 `BC30521` 同形。C# 该段确实位于 `BetterFunctionMember`，但它被"参数类型同一性"前置门（`:2216-2230`/`:2312`/`:2352` 先返回）挡在与 VB `signatureMatch` **相同的范围**内 ⇒ 落点差异不影响行为，**不得**再加宽。VB 侧也因此在 `ShadowBasedOnTieBreakingRules` 末段的第二落点**实测永不触发**（这类对被放进不同的 equally-applicable 桶，`:2588+`）⇒ 该落点不实现，不留不可达特例。锁格＝`F4_IncomparableParameterTypesAcrossSubmissions_KeepReportingAmbiguityLikeCSharp`（断 `BC30521` + 调用行位置）、`F4_SameParameterTypesDifferingKindAcrossSubmissions_BindsToTheLatest`（`M(Integer)` / `M(ByRef Integer)` 型别同一 ⇒ 择一取最新，与 C# `in` 例逐字同）。
  - 净分歧因此仍只有两条：**(1) VB 可重载 `Property` ⇒ 择一覆盖面比 C# 宽**；**(2) VB 比 C# 多一道 `slot >= 0` 门**（实测该分支在 `TypeKind.Submission` 前置门下不可达 ⇒ 行为不可观测，保留作防御读码注释，不算分叉）。本条目 Semantic 门 **+6 格**（该类 9→15），`scripts\verify-vb-compiler-tests.ps1` 基线随之 5862/5758/104（读数 2026-09-24，**未经复跑**）。
- 3-way 注意：本条与 §2.24/§2.25(b) 无重叠文件；测试面（`Compilers\VisualBasicSemanticTest\Semantics\ScriptSubmissionMemberTieBreakTests.vb` 决策层 15 格 + `Scripting\VisualBasicTest\ScriptSubmissionMemberRedeclarationTests.vb` 宿主层）属 fork 自有路径，无 3-way 面。

**（d）`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`（§2.20 / §2.21 已在册）—— 提交类的共享 `Handles` 挂钩要有 `.cctor` 可注入**

- 净分歧：`AddWithEventsHookupConstructorsIfNeeded` 的**提交类分支**原为上游留下的空 `'TODO: anything to do here?`（`e814cb1` 带入），现改为：收集 `IsShared` 且带 `Handles` 的源方法、**只认关键字事件容器**（`Me.` / `MyClass.`），且**事件本身也为 shared** 时 `EnsureCtor(members, isShared:=True, ...)`（复用 `48d8edbff` 的**无参**提交类 `.cctor` 路径）。`TypeKind.Class OrElse TypeKind.Module` 分支**一字未动** ⇒ 普通类形状零变化。
- 真值（实测，**未经复跑**）：改前该形状不是"零诊断 + 静默不投递"，而是 **ICE**——`SharedConstructors` 为空、`GetDiagnostics()`/`Emit()` 双双抛 `IndexOutOfRangeException`，栈顶 `Symbols\Source\SourceMemberMethodSymbol.vb:797` 的裸索引 `hookupMethod = Me.ContainingType.SharedConstructors(0)`。⇒ 属"合法输入崩编译器"强形态（作者判定原则第一条）。证据＝**已检查**（probe 读数 2026-09-23）：出处仅 `tmp\vortex-logs\submission-shared-handles-hookup\probe-chain.txt` 一份（栈与 `SHAREDCTORS=0` 行已核对），但该次运行所用二进制未记录 ⇒ 再引此结论前先复现为**已运行**。
- C# 侧无对偶（无 `Handles`/`WithEvents` 概念），故本条**不是** D7 自动裁的产物，落点依"fork 自己开口未接线"选定；次选"把挂钩挂进脚本初始化器"被否（每次 `ContinueWith` 重复挂钩、类型级降实例级），实测格 T6 钉住"两次运行只挂一次"。
- 3-way 注意：上游若实现该 TODO，须比对"何时需要 `.cctor`"的判据（本 fork 用"事件也 shared"），不得退回无条件建 `.cctor`（会给无关提交类造空构造器）。
- **`Scripting\VisualBasicTest` 全量直跑里的 1 红为测试装置偶发**：机制候选＝进程级 UI 文化泄漏（锚点已检查），因果未证 ⇒ 本条目的收口判定不以该「1 红」为依据。机制取证、复现方法与取样分布不在账本维护，见 `..\tmp\HANDOFF.md` §4.5（该偶发红的机制候选说明）与 §5（待办表）——该文件是本机交接文档，**不入仓**。
- **跨提交那一格也已收（issue 31；复跑 ✔）**：上面判据"事件本身也为 shared"仅覆盖**同提交**（事件落在本提交 `members.Members`）。当事件在**更早提交**、处理器在本提交时，绑定层 `SourceMemberMethodSymbol.BindSingleHandlesClause` 沿提交链（`Binder.LookupInSubmissions`）查到该共享事件后**无条件**取 `SharedConstructors(0)`，而收集层因本提交成员字典查不到事件名而 `Continue For`（不建 `.cctor`）⇒ 两阶段成员集不同 ⇒ `IndexOutOfRangeException`（同一强形态）。修法＝收集层提交分支里，事件名查不到时不再直接跳过，而是**幂等** `EnsureCtor(members, isShared:=True, ...)` 后再 `Continue For`；事件根本不存在时绑定层在 `eventSymbol Is Nothing` 处**先于** `SharedConstructors(0)` 早退报诊断（BC30183/BC31407 一类），多造的 `.cctor` 因无人挂入而是惰性的——不吞诊断、不静默丢钩。落点仍**只在 `TypeKind.Submission` 分支**内（`Class`/`Module` 一字未动，非脚本面免疫）。新增 2 格链式提交 Emit 用例（`SubmissionSharedHandlesHookupTests.vb` 的 "cross-submission shared Handles" 区块：正例零诊断 + `SharedConstructors.Length=1`；缺事件反例仍含 Error 诊断）⇒ Emit 门 **4380→4382 / Passed 4277→4279**，七门全绿、L2 **768/0**。取证/取舍史与甲-乙-丙判定见 `issues\issue-cross-submission-shared-handles-ice.md` §七→§八。

**（e）`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`（§2.3 / §2.6 等已在册的上游同名文件）—— 脚本命令行补 `optionsEnded` 门（issue 23，对齐 csi）**

- 净分歧（**当前工作树锚点**）：符号 `Parse` 主循环引入局部 `optionsEnded`（`:172` 声明）；两处读门 `:199`（`Debug.Assert(optionsEnded OrElse Not arg.StartsWith("@", …))`）与 `:203`（`If optionsEnded OrElse Not TryParseOption(…)`）。**唯一写点** `:481` 位于脚本分支 `If IsScriptCommandLineParser Then`（`:476`）…`Else` 内的 `Case "-"`，判据取逐字 `arg = "--"` ⇒ `optionsEnded = True` 后 `Continue For`。`--` 与 `-` 在 VB 侧同码（`Case "-"`），只把**逐字 `--`**摘出置位，单 `-` 的 stdin 形状一字未动。
- 非脚本（vbc）免疫＝**结构可证**：写点 `:481` 词法上落在 `IsScriptCommandLineParser` 块内 ⇒ 编译模式下 `optionsEnded` 全程 `False` ⇒ `optionsEnded OrElse X ≡ X`，`:199`/`:203` 逐字退回改前表达式；上游共享 `FlattenArgs` 的 `--` 识别本身也只在 `scriptArgsOpt != null` 时生效（vbc 连分隔符都收不到）。锁格 `ScriptOptionsEndedTests.VbcDashDashDoesNotEndOptionProcessing` / `VbcResponseFileAfterDashDashIsStillExpanded`（vbc 形状：修前＝修后）。
- 与 C# 的关系＝**向 csi 现行做法收敛**（D7 自动裁，方向 A）：对偶 `Compilers\CSharp\Portable\CommandLine\CSharpCommandLineParser.cs:169`（断言门）/`:174`（选项判定门）/置位点 `:330`（同样在 `if (IsScriptCommandLineParser)` 块内，`:308`）/`if (arg == "-")` 分流 `:315`。修前 VB 合法输入 `["--","@arg1"]` 撞 `:198` 裸断言、`["--","/arg2","script.vbx"]` 报 BC2007 且把源文件槽写成 `-`（C# 把 `/arg2` 当源文件）——真行为分歧。
- **一处刻意未对齐（后续项）**：C# `:314` 的 `if (value != null) break;`（`-` 带值时交给通用处理）**未移植** ⇒ `--:x` 这类畸形在 VB 保持既有形状（`TryParseOption` 给 `name="-"`、`value="x"`）。补它会改变 `-` 带值的既有可用形状（判据明令不动），由 `ScriptOptionsEndedTests.OnlyTheExactSeparatorEndsOptions` 锁住并登记为后续不对齐项。
- 实测面（复跑）：`CommandLine` 门 **483/476/7/0**（新增 `ScriptOptionsEndedTests` 8 格，`scripts\verify-vb-compiler-tests.ps1` 基线随之 475→483）；`Scripting\VisualBasicTest` 直跑 **766/0**（新增 `CommandLineRunnerTests` 内 `TestOptionsEnded*` 两格；**并回收既有格 2** `ScriptModeArgsTests.DoubleDashInteractiveMode_…` 里把"`--` 之后的 option 形状 = BC2007"当 D5 分叉钉桩的第二断言，改断为 C# 同形的"无 BC2007 + `/arg2` 成源文件"）；重建 Debug 宿主后 `vbi -- tmp/a.vbx`（打印脚本输出）、`vbi -- @x`（`BC2001` 找不到文件、**不再撞断言**）、`vbi -`（stdin 脚本仍跑通）三格实跑（档 2）。
- 3-way 注意：只评审主循环这两个 `optionsEnded OrElse` 读门与脚本 `Case "-"` 内 `:481` 的置位；上游若给主循环加/改断言或选项判定，须保住"门只被 `IsScriptCommandLineParser` 内的写点置真"这一非脚本免疫前提，并同步 `ScriptOptionsEndedTests` 的 vbc 两格与 `ScriptModeArgsTests` 格 2 的第二断言。

**（f）`Scripting\VisualBasic\VisualBasicScript.vb`（上游同名文件；新增在册）—— 字符串脚本工厂补传 `ScriptOptions.FileEncoding`（issue 24，对齐 csi）**

- 净分歧（**当前工作树锚点**）：`Create(Of T)(code As String, …)`（`:29`）折 `SourceText` 时改传 `SourceText.From(If(code, String.Empty), options?.FileEncoding)`，与同文件流重载 `:39`（`SourceText.From(code, options?.FileEncoding)`）、共享基类 `Script.cs:117 ContinueWith(String)` 与 C# `CSharpScript.cs:37` 同形。改前不传编码 ⇒ 「字符串 + `WithFilePath` + `WithEmitDebugInformation(True)`」的树无 `Encoding`、命中逐树 debug document 门（`Compilation.cs:2518`）报 `BC37236`（C# 同形发 PDB）。
- **行为保持性（关键）**：`SourceText.From(string, Encoding? = null, …)`（`SourceText.cs:107`）把编码原样存入 `StringText`，`options?.FileEncoding` 为 `Nothing` 时与改前的 `SourceText.From(code)` **逐字等价** ⇒ 未设编码的既有形状（含产品路径 FileEncoding 恒 `Nothing`）零变化。C# 基线 `Pdb_CreateFromString_..._WithoutFileEncoding` 同样以"传 null"落到无编码树并报 encodingless，佐证两侧一致。
- 非脚本免疫＝**平凡成立**：改动仅在 `Scripting` 的公共工厂方法，无任何 `Compilers\` 文件被触碰 ⇒ 普通 VB 编译路径不可能经过此处（七门为结构性无关，仅 `Scripting\VisualBasicTest` 是直接回归面）。
- 测试面（档 1，亲跑 `Scripting\VisualBasicTest` 直跑 **768/0**，基线 766→768）：回收 `ScriptModePdbTests.vb` 中把该分歧钉成期望的格 2（`..._WithEncoding_ReportsBC37236` → `..._ResultInPdbEmitted`，断零 BC37236 + 发 PDB + 帧名指向脚本文件）；另三处字符串 + UTF8 形状的树形子断言（emit-off 两格、inline+encoding 两格）由 `encodingIsPresent:=False` 改 `True`（其"帧无文件信息 / 帧名空路径"的行为结论不变）；反例锁格 1（同形状编码 `Nothing`）保持 `BC37236` 一字未动；新增 C3（字符串与流两工厂在同一 options 下产出的 `SourceText` `Encoding`/文本逐字段相等）与 C5（`ContinueWith(String)` 同样带编码，证明无第二处未改的 `SourceText.From`）。
- 3-way 注意：`VisualBasicScript.vb` 属上游同名文件，合并时若上游改字符串工厂的 `SourceText.From` 调用，须保住传 `options?.FileEncoding` 这一条（与流重载/C#/共享基类对齐），别退回不带编码的旧形。

**（g）`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb`（上游同名文件；新增在册）—— 脚本顶层 `Dim x = <expr>` 沿用 `Option Infer` 的推断类型（issue 32）＋ 推断撞循环时报 `BC30980` 而非静默退 `Object`（issue 33）**

- 净分歧（当前工作树）：`ComputeType` 在既有分派前加一条脚本分支——当 `ContainingType.IsScriptClass AndAlso Not IsConst` 时先试 `TryComputeScriptFieldType`，拿到类型就返回；否则退回上游原样（`GetDeclaredType` / 常量 `GetInferredType`）。`TryComputeScriptFieldType` 只在**极窄条件**下推断：声明无 `As` 子句、无类型字符（`GetTypeCharacter() = None`）、无 `?`/数组维度、初始化器是普通 `EqualsValue`、且 `binder.OptionInfer` 为真；它用 `BindingLocation.FieldType` 的 binder 把初始化器**绑定一次只为取类型**（诊断丢进 `BindingDiagnosticBag.Discarded`），按局部变量 `DecodeVarTypeOrInfer` 同一套 BoundKind 归一（`UnboundLambda`→委托、`ArrayLiteral`/`TupleLiteral`→`InferredType`、否则 `.Type`），`IsNothingLiteral`/错误类型则返回 `Nothing` 退回 `Object`。
- 关键结构事实（为何这样落点）：非 `Const` 字段**没有** `SourceMemberFlags.InferredFieldType`（该标志只在 `:558` 的 `Const` 分支置位）⇒ `HasDeclaredType` 为真 ⇒ `ComputeType` 走 `declaredType`；而 `<Initialize>` 里的字段值绑定（`Binder_Initializers.vb:603`）取 `If(HasDeclaredType, fieldSymbol.Type, Object)` 作目标类型 ⇒ 一旦 `Type` 被推断，值绑定自动按推断类型收敛，与局部同形，无需改值绑定路径。符号层的一次绑定**不影响发射**（只读类型、诊断丢弃），运行期副作用只在 `<Initialize>` 那次绑定发生。
- 非脚本面免疫：整条路径在 `ContainingType.IsScriptClass` 门内 ⇒ 普通类/模块字段（VB 语法本要求写 `As`）与所有非脚本容器逐字走原路径；`Option Infer Off`、显式 `As`、`= Nothing`/错误类型也退回原 `Object`/既有 `BC30209` 行为（对齐 §七 局部真值）。**递归守卫**＝实例字段 `_computingScriptFieldType`（挡 `Dim x = x` 自指与互指）；**该守卫自 issue 33 起不再静默**，先报 `BC30980` 再退 `Object`，详见下方 issue 33 条。
- 与 C# 的关系＝**方向对、判据不同**：C# 也在 `SourceMemberFieldSymbol.cs:530 !IsScriptClass` 脚本分支用 `BindInferredVariableInitializer` 符号层推断字段类型；但**赋值不兼容的报错判据刻意不抄 C#**——C# 一律 `CS0029`，VB 须等同局部（`Option Strict Off` 编译通过、运行期窄化失败；`On` 报 `BC30512`），否则等于覆盖全局 `Option Strict Off`（D7 分叉纪律）。
- 实测面（亲跑）：新增 `ScriptTopLevelDimInferenceTests.vb` 7 格（推断正格 + `Option Infer Off`/显式 `As`/`= Nothing` 反例锁 + `BC30512`/`BC30209` 判据）⇒ Semantic 门 5862→**5869/5765/104**；七门全绿；档 2：`vbi` 里 `Dim x = 1` 后 `x.Length` 由运行期晚期绑定异常转为编译期 `BC30456`（"not a member of Integer"）。**回收既有钉桩**：`ScriptModeTopLevelInferenceTests.vb`（原"顶层 Dim 是 Object"整文件）与 `ScriptModeStatementConformanceTests.vb` 的 `TopLevelInferredField_IsObject_Conforms`/`TopLevelInferredFieldWithStrictOn_IsReported` 按新语义改正格（overload 判据取 "integer"/"sequence"；LINQ 旧 `InvalidCastException` 形状改断具体结果）。
- **issue 33 增行（同一函数、净增 9 行）**：`TryComputeScriptFieldType` 的重入守卫那一支（`:118`–`:129`）由「`Return Nothing` 静默退 `Object`」改为「**先报 `ERRID.ERR_CircularInference1`（BC30980）再** `Return Nothing`」——`diagBag.Add(ERRID.ERR_CircularInference1, Me.Syntax.GetLocation(), Me.Name)`。三条落点是**被机制证死**的、不是风格选择：① 报点只能在最内层那一支——`SourceModuleSymbol.vb:827-844` 在 `_lazyType` 已被内层 frame 存好时会**整块跳过外层非空 bag**，包到 `ComputeType` 层就等于没报；② 实参传 `Me.Name` 而非符号——字段符号进 VB 文案会经 `Symbol.vb:873 ToString()`（`VisualBasicErrorMessageFormat`）渲染成 `Private a As Integer`，与局部那侧（`Binder_Expressions.vb:3154`）不同式；③ 返回契约不变（`Nothing` 仍＝退旧 `Object` 路），故 `Option Infer Off` 在 `:149` 早退、守卫不置位 ⇒ 该档天然零变化，不需额外旗标。
- **issue 33 的 C# 对照与判据**：C# 同形状（`var a = b; var b = a;`，同一棵 `.csx`）由 `csi 5.10.0-1.26380.3` 实跑报 `(1,5) error CS7019`（机制锚 `SourceMemberFieldSymbol.cs` 的 `fieldsBeingBound.ContainsReference(this)` ↔ `ErrorCode.cs:1165`）⇒ 按 D7 三问全「是」判可移植，且**复用既有码不占新码**（例外 (b) 不适用：改动面在本仓 VB 侧）。**粒度已实测对齐**：一条连通循环只出一条、落在该分量中第一个被计算类型的字段上，两个不相干循环出两条——与 C# 只出一条 `CS7019` 同形。
- **issue 33 非脚本面免疫**：新增报点完全位于 `ContainingType.IsScriptClass` 门（`SourceMemberContainerTypeSymbol.vb:1295-1300` ＝ `DeclarationKind.Script OrElse Submission`）之内，且 `BC30980` 经核为**非 BuildOnly**（`ErrorFacts.vb:550` 落在 `IsBuildOnlyDiagnostic` 的 `Return False` 那一支）⇒ 只在脚本类字段上可达。普通上下文另有前置报点：方法体内 `Dim a = b`（`b` 未声明）实测 `BC32000`，`Option Explicit` 两档同判 ⇒ 循环在普通上下文根本形不成。
- **issue 33 实测面（亲跑）**：产品净增删 `9 / 0`（`git diff HEAD --numstat` 实测）；新增 `ScriptTopLevelRecursiveDimInferenceTests.vb` **28 格**（A 正格含"循环且无人引用"哨兵、B 反例锁、C 非脚本面）⇒ **七门全绿**：Semantic 5886→**5914/5810/104/失败 0**（抬基线前实测＝预测逐字相符），余六门数字一字未变；`Scripting\VisualBasicTest` 直跑 **769/0**（总数未变＝本条未新增 L2 格）；档 2 宿主四格对账：`p1-cycle.vbx(2) : error BC30980: 无法从包含“a”的表达式中推断“a”的类型。`（一条、落标识符、名字裸渲染）、`p6-self` 同形、哨兵 `p5` 仍 `type=Int32 val=0`、`p3` 仍 `BC30456`；`Option Strict On` 下循环形状＝`BC30980` ＋**既有**的 `BC30209`（条数 4→4 未变，非次生级联）。计划与流水账：`tasks\script-top-level-recursive-dim-inference\`、`tmp\vortex-logs\script-top-level-recursive-dim-inference\`（RD-F01 取证 → F02 实现 → F03 定桩 → F04/F05 main）。
- 3-way 注意：`SourceMemberFieldSymbol.vb` 属上游同名文件。若上游改动 `ComputeType` 的 `HasDeclaredType`/`GetInferredType` 分派或 `InferredFieldType` 标志置位条件，须保住"脚本分支只在 `IsScriptClass`+`OptionInfer`+无 `As`/无类型字符/无修饰下生效、其余逐字退回"这一非脚本免疫前提；上游若给字段引入原生 `Option Infer`，本 fork 分支应并入上游判据而非并存。**另加一条**：上游若给脚本字段补上循环推断的诊断（C# 侧早已有 `ERR_RecursivelyTypedVariable`），本条 `diagBag.Add` 应并入上游判据、勿并存成双报；改回静默 `Return Nothing` 前须先确认 issue 33 的两侧读数不再成立。

**（h）`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb`（§2.x 已在册的上游同名文件）—— 脚本转发 `ScriptOptions.WarningLevel`（issue 27，对齐 C#）**

- 净分歧（**当前工作树锚点**，2026-09-24）：① 新增公共 `Public Function WithWarningLevel(warningLevel As Integer) As VisualBasicCompilationOptions`（相等返回 `Me`，否则拷贝后置位）——对齐 C# 已发布的 `CSharpCompilationOptions.WithWarningLevel`（`PublicAPI.Shipped.txt:204`）。② 拷贝构造器 `Friend Sub New(other)` 在 `MyClass.New(...)` 之后补 `Me.WarningLevel = other.WarningLevel`：私有构造器把 `warningLevel` 硬编码为 `1`（`:266`，非公共构造形参），故拷贝会抹掉 `With*` 刚设的级别；补这一行使 `With*` 链式可组合（脚本路径 `.WithIgnoreCorLibraryDuplicatedTypes(True).WithWarningLevel(...)` 依赖之）。③ `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `CreateSubmission` 在 `WithIgnoreCorLibraryDuplicatedTypes(True)` 后链 `.WithWarningLevel(script.Options.WarningLevel)`，把用户选项接上编译对象。④ 公共面：`PublicAPI.Unshipped.txt` 追加 `WithWarningLevel` 一行（否则 PublicAPI 分析器报 RS0016）。
- 改前面貌：VB 脚本编译对象的 `Options.WarningLevel` **恒为 1**（`ScriptOptions` 默认 4、`WithWarningLevel(0/3)` 存得住但被丢），而 C# 脚本路径落地（上游 `CSharpScriptCompiler.cs:65` 传 `warningLevel: script.Options.WarningLevel`）⇒ 同一公共 API 两条语言路径行为不同。
- **非脚本面免疫（结构可证）**：普通编译仍走公共构造器 ⇒ 私有构造器仍产 `warningLevel:=1`，`WithWarningLevel` 无既有非脚本调用者；拷贝构造器新增行只把"拷贝是否保留级别"从"恒 1"改成"等于源级别"，而普通路径所有对象 `WarningLevel` 本就为 1 ⇒ 拷贝前后同值、逐字无差（仅经 `WithWarningLevel` 得非 1 的对象其拷贝才保留，非脚本面不产此类对象）。实证：七门里除新格外六门数字一字未动。
- **行为效力口径**：读码确认 `CompilationOptions.WarningLevel` 在 VB 绑定/发射面**无活消费点**（VB 源码仅 `MessageProvider.GetWarningLevel(code)` 按诊断码查严重级别，与本属性无关）⇒ 本条是**纯转发奇偶对齐**（C# 亦然），不改变报哪些诊断；测试只断"转发值抵达编译对象"，不断"诊断随级别增减"。故 issue 27 原「方向 A·低等级隐藏警告重现」的担忧（当时标推测）被实测证伪：七门/L2 零既有格转红。
- 实测面（亲跑 ✔）：新增编译层 `VisualBasicCompilationOptionsTests.WithWarningLevel`（默认 1 / 置位不动源 / 相等 `Same` / `Equals`·`GetHashCode` 纳入 / 拷贝与 `With*` 链式保留 0）⇒ Semantic 门 5869→**5870/5766/104**；**回收既有 D5 分叉钉桩** `ScriptOptionsTests.WarningLevel_DoesNotReachTheCompilationOption` → `WarningLevel_ReachesTheCompilationOption`（0→0 / 3→3 / 默认→4，L2 格数不变），并改写 `ScriptModeApiSurfaceConformanceTests.vb` 文档串里"registered divergence"措辞为"C# parity"；**七门全绿**（GATES exit=0）、L2 `-automated` **769/0**。
- 3-way 注意：`VisualBasicCompilationOptions.vb` 属上游同名文件。若上游日后给公共构造器加 `warningLevel` 形参，`WithWarningLevel` 的"拷贝后赋值"与拷贝构造器的 `Me.WarningLevel = other.WarningLevel` 均可并回构造器传参；合并时若上游改私有构造器 `:266` 的 `warningLevel:=1`，须保住"非脚本默认仍为 1"这一免疫前提。

**（i）`Compilers\VisualBasic\Portable\Binding\Binder_Initializers.vb`（§2.25 已多处在册的上游同名文件）—— 让顶层语句体进入完整方法体流分析（issue 17 顶层块内局部 use-def；并收编 issue 16 的 ad-hoc finally 检查）**

- 净分歧（**当前工作树锚点**，2026-09-24）：`EnsureInitializersAnalyzed`（合成初始化器分析入口）原先以 `StaticCast(BoundInitializer→BoundStatement)` 拼哑块，顶层语句仍被包在 `BoundGlobalStatementInitializer` 里、通用 `Analyzer.AnalyzeMethodBody` 不下钻其体 ⇒ 顶层**块内局部**（`If`/`For`/`Using` 里的 `Dim`）拿不到定义赋值警告 BC42104。改为：**展开** `BoundGlobalStatementInitializer`→`.Statement` 再拼块（普通字段/属性初始化器原样透传），使分析下钻顶层语句体。
- **同时删除**绑定期的 ad-hoc `CheckBranchOutOfTopLevelFinally`（issue 16 为"顶层 Finally 分支产坏 IL"临时加，内部亦跑 `ControlFlowPass.Analyze` 再把诊断**过滤到只剩 `ERR_BranchOutOfFinally`**）及其辅助 `ContainsFinallyBlock`：通用 `Analyzer.AnalyzeMethodBody` 现已是 BC30101 的**单源**，保留 ad-hoc 会与通用路**双报** BC30101（删前实测 3 个 `ScriptSemanticsTests` finally 格 1→2）。`CheckAwaitInTryHandler`（BC36943）**保留不动**——实测通用路不双报 await。
- **非脚本面免疫（可证）**：展开只影响 `BoundGlobalStatementInitializer`，而该 kind **仅**由脚本顶层语句产出；普通类/模块的字段/属性初始化器分支逐字透传、不经此路。删除的 ad-hoc 亦只在脚本顶层路径被调用。实证：七门里除新增格外科门数字一字未动。
- 与 C# 的关系＝**方向 A 对齐**（D7）：C# oracle `ScriptSemanticsTests.ERR_UseDefViolation`（`ScriptSemanticsTests.cs:986`）——顶层 `Program` 字段（`int a;int b=a;`）不报 CS0165、块/方法内局部报；本改动令 VB 顶层块内局部报 BC42104、字段仍豁免，观测同形。
- 实测面（亲跑 ✔）：新增 `ScriptTopLevelDefiniteAssignmentTests.vb` 7 格（顶层字段读无警告＝parity、成员体未赋局部报 BC42104、跨提交字段无警告、赋值后无警告、顶层 If 块内/For 块内未赋局部报 BC42104、赋值块内局部无警告）⇒ Semantic 门 5883→**5886/5782/104**；**七门全绿**（Emit 4382/4279 含 issue 16 的 `InvalidProgramException`/finally 用例，证 BC30101 单源仍抑制坏 IL）、**L2 `-automated` 769/0**（脚本符合性套件无新增红）。
- 3-way 注意：`Binder_Initializers.vb` 属上游同名文件、本条目与 §2.25 其余条目同文件。若上游日后自行让顶层语句进入完整流分析，本展开式改动应并入、勿并存；删除的 `CheckBranchOutOfTopLevelFinally`/`ContainsFinallyBlock` 若被上游以别形重现，须核对不与通用路双报 BC30101。issue 16 的"顶层 Finally 分支报 BC30101、抑制坏 IL"行为由通用路承接（Emit 门用例锁），回归时一并核。

**（j）`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`（**上游同名文件**，新增在册）＋ `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb`（**fork 自有路径**，无 3-way 面）—— `#Load` 改为 once 语义（issue 34）**

> **本条取代 §2.16 对这两处形状的描述。** §2.16 记的是当年那次改动的原样（`CollectLoadTrees` 提为 `Friend Shared`、供 NuGet 预扫描共用），该事实仍然成立；但 §2.16 中的「**循环 `activeLoads` 防护**」「遇首个不可解析**/循环** `#Load` 返回其 file-not-found 诊断」「展开失败（缺文件**/循环**）留给编译器报 `ERR_FileNotFound`」三处**已不再是当前行为**——祖先栈机制连同它对 `ERR_FileNotFound` 的复用已整体删除。读 §2.16 时以本条为准。

- 净分歧（**当前工作树锚点**，2026-09-28）：`CollectLoadTrees`（`VisualBasicScriptCompiler.vb:88`）的第 4 个形参由祖先栈 `activeLoads` 改为**一次编译内已展开集** `expandedFiles`（`:92`），**不再出栈**。展开体拆成两段：`:109-111` 只在 `resolvedPath Is Nothing`（真的解析不到）时返回 `ERR_FileNotFound`＝`BC2001`；`:117-119` 的 `If Not expandedFiles.Add(resolvedPath) Then Continue For` 承担全部重复处理，**零诊断**。两个调用点各自构造集合并**各自预置入口文件规范化路径**（`VisualBasicScriptCompiler.vb:219,223`；`NuGetRestoreCoordinator.vb:219`＋其后的同名预置块），调用点 `:226` / `:230`。
- **与 C# 的关系＝同规则、更严一格**（D9「取意图不取实现」）：C# 侧 `Compilers\CSharp\Portable\Compilation\SyntaxAndDeclarationManager.cs:236` 用 `!loadedSyntaxTreeMapBuilder.ContainsKey(resolvedFilePath)` 判重复、`:270-275` 静默跳过（注释逐字 "we've seen this file before, so don't attempt to load it again"）——**方向同形**。差别在 `:247` 的 `Add` 排在递归**返回之后**、且该表**从不预置入口文件路径** ⇒ `#load` 链绕回入口文件即去重失效。实测（`csi` 5.10.0-1.26380.3，2026-09-28）：同一形状从 `cyc-main.csx` 进入 exit 0、把 `cyc-a.csx` 自身作入口则 `CS0102`＋`CS0229`×2、exit 1 ⇒ **行为随入口而变**，判为 C# 侧缺陷。本 fork 预置入口文件，故在该形状上更严。
- **非脚本面免疫（比"落点在宿主"更硬）**：`GetLoadDirectives()` 在 `Compilers\VisualBasic\` 下**只有定义、零调用点**，`AppendAllSyntaxTrees` 只存在于 C# 侧 ⇒ **VB 编译器产物本身不含任何 `#Load` 展开逻辑**；本条每一行都在 `Scripting\` 宿主侧的 `CreateSubmission` 与 NuGet 预扫描路径内（预扫描不产生编译对象、不是编译路径）。普通编译里 `#Load` 自身先被 `BC36967` 挡掉。实证：七门**逐门数字与基线一字未动**。
- 实测面（亲跑 ✔）：新增/改写 `ScriptTests.vb` 7 格（菱形＋同文件两次＋纯打印库＋每次编译独立＋成环**改写**＋自载新增＋路径拼写 5 种写法）与 `NuGetRestoreCoordinatorTests.vb` 2 格（菱形/自载各带 `#R`，断**诊断条数**而非 `RestoreCalls`——后者对重复扫描不敏感，因 `DeduplicateRequests` 按 id+version 去重）。**七门全绿且逐门与基线相同**（Syntax 4098/4095/3、Semantic 5914/5810/104、Emit 4382/4279/103、CommandLine 483/476/7、Symbol 3407/3383/24、IOperation 1574/1566/8、CodeGen 143/143）；**L2 `-automated` 769→777/0**（+8 ＝ 本批新增用例数，与成环那条"改写不计"对账一致）。**档 2**：重建 `vbi.exe` 后 `probes-cyc2\dz.vbx` 由 exit 1＋`BC30260`+`BC31429` 转 exit 0 且输出与 `csi` 同形状**逐字相同**；`dup-main.vbx` 打印由两遍转一遍；`a.vbx` 成环与 `self.vbx` 自载由 `BC2001`/exit 1 转 exit 0 静默；**`probes-nest\main.vbx` 输出逐字未变**（证明"被加载内容先执行"的既有语义未被触动）。
- **鉴别力经变异法证**（各自命中的正是目标用例，非"随便哪条红了"）：删 NuGet 侧主路径预置 → `MainFileSelfLoadScansMainNuGetDirectiveExactlyOnce` 红；还原祖先栈 → `DiamondLoadScansSharedNuGetLibraryExactlyOnce` 红；集合改 `Shared` → `TestLoadDedupIsPerCompilationNotPerProcess` 等 14 格红；比较器改 `Ordinal` → `TestFileLoadedTwiceRunsItsCodeOnce` 红（报 `BC30260`＋`BC31429`，正是 issue 34 原症状）。
- 3-way 注意：`VisualBasicScriptCompiler.vb` 是**上游同名文件**（`{{Roslyn}}\src\Scripting\VisualBasic\VisualBasicScriptCompiler.vb`），与上游的 `AppendAllSyntaxTrees` 是同一机制的两侧。合并时**只评审 `#Load` 展开这一个函数**：若上游调整了去重表、入口文件预置或静默跳过的判据，须保住三条——(1) 重复**静默**跳过、不报诊断；(2) 入口文件路径**在展开前预置**（否则结果随入口而变）；(3) 深度优先、加载树先于引用者。`NuGetRestoreCoordinator.vb` 上游树内零命中，属 fork 自有，**无 3-way 面**，登记仅为免得将来误判漏登记。

**（k）`Compilers\VisualBasic\Portable\Symbols\ReferenceManager.vb`（**上游同名文件**；新增在册）—— 把「查缓存 / 建符号 / 发布缓存」并成一次持锁的原子步（issue 35、37、38 同族）**

> **状态：⚠ 未提交，且不可交付 —— 全量回归查出 Symbol 门 28 条失败（详见下）。** **候选 R2 已撤回**（插桩已删净、BOM 已补、首行无噪声后手动还原；`ReferenceManager.vb` 现与上游**逐字相同**），配套三格测试已随裁决删除。接手第一件事见 `..\tmp\HANDOFF.md` §5.3（本机文档，**不入仓**）。
>
> **⚠ 处置建议＝reserve（待作者裁决）**：**C# 侧是同一个缺陷**，不是类似缺陷——`Compilers\CSharp\Portable\Symbols\ReferenceManager.cs` 与 VB 那份**逐行同构**（查缓存 `:1017` 持锁 → 建符号 `:428-430` **在 `:311` 锁区间外** → `InitializeNewSymbols :481` → 再入锁 `:486` 发布 `:497`），连注释与方法名都相同。⇒ **单独修 VB ＝ 对上游 C# 形成分叉**，而 `decisions.md` D5 要求「**为什么 VB 必须分叉**」；目前唯一能给的理由是「本 fork 只发布 VB 脚本、C# 侧是死代码」，那是**产品范围**理由而非技术理由。
> **行为实验在本 fork 内无法对称做**（结构性事实）：`Compilers\CSharp\Portable\Scripting\` **不存在**（裁掉了 C# 脚本层、无 csi）⇒ C# 侧没有与 VB 那条失败测试同构的宿主路径。`tmp\cs-side-probe\` 用编译器 API 单独跑的 C# 探针 0 复现，但**其 VB 阳性对照未能命中目标签名**（harness 自身有 `typeArguments` 缺陷）⇒ **该 0 作废，不得引用为"C# 无此缺陷"的证据**。
> **三条了结路径**：① **两侧一起改**（保持与上游一致）＋ 回归两个语言的普通编译对照；② 取得技术性理由并写进 D5；③ **放弃本条、按"上游共享缺陷"登记后交 `dotnet/roslyn`**，fork 不单边修。
> **🚫 阻塞项（亲跑七门；Symbol 门 3407 格里 28 条失败，连跑四次分别 26/28/30/28）**：
> - **7 条 `Debug.Assert allAssemblyData(i).IsLinked = bindingResult(i).AssemblySymbol.IsLinked` 炸在 `:429`**。原因：R2 会**采纳**缓存里已有的符号（`bindingResult(i).AssemblySymbol = cached`），而那个符号可能是**为另一个 `AssemblyData` 建的**、其 `IsLinked` 与当前条目不同 ⇒ 断言失配。**原代码不会踩**：它只在**新建**分支进这一行，`IsLinked` 必然与 `allAssemblyData(i)` 一致。⇒ **采纳已有符号时必须校验／筛选 `IsLinked` 兼容性**（或只在兼容时采纳）。
> - **6 条 `Assert.NotSame() Failure: Values are the same instance`**（`NoPia.LocalTypeSubstitution1`、`Retargeting.NoPia.LocalTypeSubstitution1_2` 等）——这些测试**要求不同实例**（NoPia 局部类型替换按编译各造一份），而本修复让跨编译共享符号 ⇒ **共享范围可能过宽**。
> - **7 条 `UsedAssembliesTests` 引用类型／顺序不符**（期望 `VisualBasicCompilationReference`、实得 `MetadataImageReference`）——同属"该共享的没共享、不该共享的共享了"这一类后果。
> - 另 5 条 `AggregateException` ＋ 3 条零散，签名待归类。
> ⇒ **L2（脚本层）777/0 未变**，故障集中在编译器符号层。**四条配方全绿 ＋ 变异可证只覆盖了脚本／提交形状，没有覆盖普通编译的符号身份语义 —— 这正是缺口所在。** 修法必须带"普通编译对照"重新回归 **Symbol 门**。
>
> **已撤回的候选 R**（留档）：只把「查缓存 / 建 / 发布」并入同一次 `SyncLock`，把发布从 `UpdateSymbolCacheNoLock` 搬进创建点。**原配方上有效**（4/8 → 0/12；变异「发布挪出锁」→ 12/12），**DupTrace 机制级证据**：同一 PE 产生多个符号 `dup-fresh` **16 组**、`dup-chain` **8 组**；带 R 的 `dup-Rchain` **0 组**。**但 R 引入了一条新缺陷**：它把**初始化留在锁外**，而发布进了锁内 ⇒ 锁内出现"**已发布但尚未 `SetReferences`**"的 `PEAssemblySymbol`，另一个编译进得来就捡到它，`ReuseAssemblySymbols`（`Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Binding.cs:820`）读它的 bound references 时炸（`NonMissingModuleSymbol.vb:136` 的 `_moduleReferences IsNot Nothing` 断言），并且 `Compilation.ValidateScriptCompilationParameters`（`Compilation.cs:274`）抛。⇒ 「R 下换了一种红」**不是同族缺陷，是 R 自己引入的**。
>
> **⚠ 承重的是什么（变异 M-B 定性，推翻了"次序"那条红线）**：**M-B＝把发布前移到创建点、但初始化与发布仍在同一把锁内 ⇒ 守门格 0/4 全绿**，与 R2 等价。⇒ **承重的是「从重读缓存到发布的整段都在同一次持锁内」，不是「发布排在初始化之后」这个次序**。R2 之所以仍把发布留在 `UpdateSymbolCacheNoLock`，是**可读性／最小惊讶／与原次序一致**的取舍，**不是**正确性所需。收窄锁区间（M-C＝整份 R）才变红。**这一条已写进本条与 issue 35，别再把因果归到 `WeakList`／issue 37 上。**
>
> **候选 R2（已撤回，留档）**：把**一整段**放进同一次 `SyncLock`（`VisualBasic\...\ReferenceManager.vb:403-501`）——`IsBound` 提前判定（`:405`）→ 重读缓存／采纳／新建（`:418`）→ corLibrary ＋ `SetCorLibrary` → `SetupReferencesForSourceAssembly` → `InitializeNewSymbols`（`:469`）→ **`UpdateSymbolCacheNoLock`（`:474`，发布仍是最后一步、仍在初始化之后）** → `InitializeNoLock`（`:476`）。`UpdateSymbolCacheNoLock`（`:573`）恢复原样（文件类符号仍在那里发布）。读数：该守门格 **6/6 全绿**，插桩 `96 CREATE / 96 PUBLISH`、**同一 PE 多个符号 = 0**（`dup-R2chain.log`）⇒ 既保住「R 消灭重复符号」，又消掉 R 引入的新红。
>
> **`IsBound` 提前判定不是弱化而是加强**：`_isBound` 只在 `InitializeNoLock` 里翻转，而那也在这把锁内（`:476`）。
>
> **根因（亲自读码核实，非转述）**：`AddAvailableSymbols` 持 `SymbolCacheAndReferenceManagerStateGuard` 读缓存后**放锁**，发布要到 `UpdateSymbolCacheNoLock` 才**重新**取锁 ⇒ 「读到空 / 建符号 / 发布」**三步不原子**。已排除：锁是 `static` **进程级**（`CommonReferenceManager.State.cs:34`）；所有 `CachedSymbols` 访问点都在锁内（Core 的 `Binding.cs:583` / `Resolution.cs:331` 只**传递**列表；C# 侧 `ReferenceManager.cs:1014-1017` 持锁枚举、`:634` 的 `Add` 在 `UpdateSymbolCacheNoLock` 内）；**第二个** `WeakList`（`Compilation._retargetingAssemblySymbols`，`Compilation_MetadataCache.cs:33`）虽同样「枚举收尾改写自身」，但它是**实例级**字段、唯一枚举点 `VisualBasic\...\ReferenceManager.vb:999` **在锁内** ⇒ **进程级共享的 `WeakList` 只有 `AssemblyMetadata.CachedSymbols` 一个**。

- 净分歧（**R 已撤回，修复未落地**；以下是 R 的形状，供下一版参照）：`CreateAndSetSourceAssemblyFullBind` 里「创建 `AssemblySymbol`」那个 `For` 循环**整体移入 `SyncLock SymbolCacheAndReferenceManagerStateGuard`**；循环内新建前**重读缓存**，新建后**在同一把锁内发布**（需两个新辅助方法：`AssemblyDataForFile` 上的 `TryGetCachedAssemblySymbol()` 读取 ＋ `PublishCachedAssemblySymbol()` 发布）；`UpdateSymbolCacheNoLock` **不再**发布 `AssemblyDataForFile` 条目，只保留 `AssemblyDataForCompilation` 的 `CacheRetargetingAssemblySymbolNoLock`。**注意**：R 有一处行为变化——`fileData` 用**空条件传播**，元数据条目若不是 `AssemblyDataForFile` 就**跳过发布**，而原码是 `DirectCast(assemblies(i), AssemblyDataForFile)` 会**抛** `InvalidCastException`；下一版须复核这条放宽是想要的。
- **为什么要（机制，读码核实）**：`AddAvailableSymbols` 持锁读缓存后**放锁**，而发布要到 `UpdateSymbolCacheNoLock` 才**重新**取锁 ⇒ 「读到空 / 建符号 / 发布」三步**不原子**。同进程两个编译共用同一份元数据（两个提交、提交与其 previous、或并行测试共用的进程级引用对象）会**各建一个 `PEAssemblySymbol`**。后果是同一类型有两个符号：按 `SpecialType` 相等（`Conversions` 的快路径 `ConversionEasyOut` 只查表就判 Identity），但按 `IsSameTypeIgnoringAll` 不等 ⇒ `Binder_Conversions.vb:442` 的断言炸。**与 C# 的关系**：C# 侧 `Compilers\CSharp\Portable\Symbols\ReferenceManager.cs` 是同一形状、同一非原子性（`:1014-1017` 持锁枚举、`:634` 的 `Add` 在 `UpdateSymbolCacheNoLock` 内）⇒ **本条不是分叉，是把两边共有的缺陷在 VB 侧修掉**；上游若日后修 C# 侧，本条应与之合并、勿并存。
- **行为保持性（须在合并时复核）**：单线程下重读必然返回 `Nothing`（没人抢先发布）⇒ 创建的符号集合与改动前**逐字相同**。
- **性能注记（非阻断）**：R 把**符号创建整段放进进程级 `static` 锁**内，绑定 N 个引用的编译要做 N 次元数据读取且全程持全局锁 ⇒ 并行编译被串行化。正确性换来的代价；上游若日后有更细的方案（双重检查 ＋ 丢弃胜出的新建符号）可换。
- 3-way 注意：本条只评审 `CreateAndSetSourceAssemblyFullBind` 的**创建/发布原子性**与 `AssemblyDataForFile` 的新辅助方法。上游若重构 `ReferenceManager`（换成 `ConcurrentDictionary`、或把 `CachedSymbols` 换成线程安全容器），**本条与 issue 37 可能一并失效**——`WeakList` 自身不线程安全（`Compilers\Core\Portable\InternalUtilities\WeakList.cs:206-217` 枚举**结束时会改写列表**），而 `SymbolCacheAndReferenceManagerStateGuard` 是**进程级 `static`**（`CommonReferenceManager.State.cs:34`）⇒ 若上游去掉这把锁，本条的保护**同时消失**。合并时三处须一起看：本条目 ＋ issue 37 ＋ 上游的容器改动。
- **配套测试**（均**fork 新增、上游树内零命中 ⇒ 无 3-way 面**）。**守门形状是本条最来之不易的判据，务必先读**：
  - **独立提交之间永不相遇**，所以"一堆并发跑独立提交"那种格**没有鉴别力**（实测 ORIGINAL 全绿）。**重复符号本身不炸，炸的是"两个符号在同一个编译里相遇"。**
  - 真正复现的形状＝**两段提交链**（`previous:=`）：后一段**通过前一段的编译**去解析 `Handles Me.Ev`（连带 `System.EventHandler`），**却用自己的 body**——正是 `SubmissionSharedHandlesHookupTests` 的 `SharedHookupAcrossSubmissions_*` 形状。再加上**自建缓存必空的 `AssemblyMetadata`**（`CopyWithoutSharingCachedSymbols()` ＋ `GetReference`，测试可直接调，`InternalsVisibleTo` 含 Emit.UnitTests）＋**专用 `Thread` 由闸门齐放**（`Task.Run` 版在 ORIGINAL 上实测 **0/6 全绿**、无鉴别力）。⇒ **ORIGINAL 4/4 红**，16 个线程全部 `System.InvalidOperationException: argument.Type.IsSameTypeIgnoringAll(targetType)`。
  - **这三格现已全部删除**（作者裁决）：它们测的是**产品从不产生的并发条件**——产品路径不并发（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` 零并发原语，`Script.cs` 只有 `Interlocked` 做惰性一次性初始化）。备份 `tmp\backup\r2\*.deleted`。**Emit 门基线 4388/4285 不变**（那几格本就禁用、未计入）。行 N 的 `SubmissionSharedHandlesHookupTests` **保留**——零线程原语、顺序执行。
- **机制级证据（`tmp\probe\dup-*.log`，保留，比"测试变绿"强）**：同一 PE 产生多个不同符号的组数——无修复 `dup-fresh` **16**、`dup-chain` **8**；带 R 的 `dup-Rchain` **0**；带 R2 的 `dup-R2chain` **0**。三份里 `CREATE 出但从未 PUBLISH` 均为 0。

## 二·补、已知欠账：尚未登记的修改面（2026-09-11 清点）

> **先读口径与限制**：本节的判定方法是「**文件面筛 + diff 复核**」，**未**逐个读 diff 判定每个改动点是否恰好落在既有 §2.x 条目的语义面内。因此「未登记」是**文件面级**结论，完整度标 `Suspect`——**补登记时必须逐 diff 复核心态，不得直接采信本表**。
> 另注意：既有条目的锚点常为**行区间**（如 §2.1 只锁 `VisualBasicCommandLineParser.vb:61-63`、`:1522-1523`），**文件被某条登记 ≠ 该文件的所有改动点都被登记**。
> 清点范围：2026-07-27（上游基准日期）之后触及 `Compilers\` 的 22 个提交。
> **本节条目不计入修改面、未经 3-way 评审**；合并前须先按「三、合并步骤」第 3 步补登记，再逐条评审。

| 提交 | 日期 | `Compilers\` 规模 | 内容 | 判定 |
|---|---|---|---|---|
| `8377403` | 2026-08-23 | 28 文件 +6955/−1 | 新增 `Compilers\Core\MSBuildTask\` **整棵树**（.cs/.resx/.targets）+ `vbc.csproj` | **未登记** |
| `b60dcd3` | 2026-08-15 | 30 文件 +503/−63 | `#load` 语法节点全链（`Syntax.xml` 新节点 / `SyntaxKind` / `Scanner\KeywordTable` / `Scanner` / `CompilationUnitSyntax.vb` / `SyntaxFactory` / `SyntaxFacts` / `SyntaxNodeFactories` / `InternalSyntax\SyntaxToken` 等） | **未登记**（§2.5 仅两句泛述、**无文件级锚点**；旁证：fork 自有 `ERR_LoadDirectiveOnlyAllowedInScripts = 36967`，`Errors.vb:1606`） |
| `3dfb3bc` | 2026-09-02 | 27 文件 +1524/−155 | ref readonly / `[In]` / `RequiresLocation` modreq 消费（`Binder_WithBlock.vb` / `BoundExpressionExtensions.vb` / `WithExpressionRewriter.vb` / `Symbol.vb` / `Wrapped*` / `Substituted*` / `RetargetingMethod·PropertySymbol.vb` / `CustomModifierUtils.vb` / `SymbolDisplayVisitor.Members.vb` / `LocalRewriter_Call·With.vb` 等） | **未登记**（§2.3 只含 byref-like / ref struct，**不含 ref readonly**） |
| `5803347` | 2026-08-14 | 35 文件 +2165/−87 | consume ref struct 全面 | **部分登记**（§2.3 登了 6 个文件；另约 24 个产品文件未登，含 `Binder_Conversions` / `Binder_Invocation` / `Binder_Lambda` / `Binder_Utils` / `Binder_AnonymousTypes` / `Binder_Delegates` / `Binder_Expressions` / `Binder_Statements` / `Semantics\Conversions` / `SymbolDisplayVisitor.Types` / `PEMethod·Property·Event·FieldSymbol` / `ObsoleteAttributeHelpers` / `Retargeting·Substituted·WrappedNamedTypeSymbol` / `TypeSymbolExtensions` / `SourceMemberField·MethodSymbol` / `SourceMethod·PropertySymbol` / `LambdaRewriter.Analysis`） |
| `80eff5f` | 2026-08-16 | 8 文件 +127/−83 | 错误码重排（新增 `ERR_PPReferenceFollowsToken = 36959` 占官方间隙；`ERR_PPLoadFollowsToken` 36984→**37002**；`ERR_NextAvailable` 编号策略注释）+ `Binder_Delegates.vb` 归约扩展方法免 BC31393 例外 + `MergedNamespaceSymbol.vb` aliased-assembly 全局命名空间过滤 / `DeclarationsAccessibleWithoutAlias` + `VisualBasicCommandLineParser.vb` `/sdkpath`·`/nosdkpath`·`System.Private.CoreLib` 直引（`:134`/`:135`/`:690`/`:698`/`:704`/`:705`/`:1387`/`:1624`） | **未登记**（`Errors.vb` 现同载 4 批编号改动，仅 2 批在账） |
| `0cba0a1` | 2026-08-29 | 2 文件 +20/−1 | `/check` 编译不运行（`VisualBasicCommandLineParser.vb:98`/`:544`/`:1563`） | **未登记**；**内含一处 public API 增量未记录**（见下「已核实的两条」） |
| `28a0f95` | 2026-09-05 | 3 文件 +64/−7 | net472 分支叠入编译器包（`Core\MSBuildTask\Microsoft.Build.Tasks.CodeAnalysis.csproj`、`VisualBasic\vbc\AnyCpu\vbc.csproj`、`vbc\App.config`） | **未登记**（§2.7 只列 `Interactive\vbi`、`Scripting\Core`、`Workspaces\...`、`Samples`、`Installer`） |
| `b2d5d7f` | 2026-08-26 | 1 文件 +18 | 脚本模式放行 `/optimize`、`/optimize+`、`/optimize-`（`VisualBasicCommandLineParser.vb:526`、`:535`；`:833` 是上游既有同名分支） | **未登记** |
| `693e4a5` | 2026-08-01 | 11 文件 +91/−14 | WIP 移植 C# interactive 特性 | **部分登记**（`Binding\BinderBuilder.vb` / `BinderFactory.vb` / `TopLevelCodeBinder.vb` / `Lowering\SynthesizedSubmissionFields.vb` 等未登） |
| `7a0111e` | 2026-08-08 | 4 文件 +144/−4 | top-level 修正 | **部分登记**（§2.6 无文件级锚点；`Analysis\InitializerRewriter.vb` / `Symbols\AssemblySymbol.vb` 未登；`Binding\Binder_Initializers.vb` 的**文件面**已由 §2.20 补登 ⇒ 该文件不再是未登文件，7a0111e 对它的具体改动点随 §2.20 的 3-way 评审一并核） |
| `6b6e38c` | 2026-08-08 | 3 文件 +22/−2 | 返回值与样例修正 | **部分登记** |
| `bee85b3` | 2026-08-02 | 3 文件 +371/−6 | port tests；产品面仅 `SymbolDisplay\ObjectDisplay.vb`（REPL 显示助手） | **未登记（小面）** |

**不计为欠账**（理由在列）：`e814cb1`（1896 文件，建立 fork 裁剪树本体；「一、上游基准」已声明本 fork `Compilers\` 是修剪树 ≠ 上游镜像）、`7566bda` / `0c3de87` / `f1ff2f1` / `bdb7735` / `5727a3f`（产品面仅 `.vbproj` / `AssemblyInfo.vb` / 无产品面）、`ad1824f`（§2.4 已登记）、`e307d0f`（**已补 = §2.19**）、`2f94ea7`（**已覆盖 = §2.9**）。

### 已核实的两条（本表之外）

1. **public API 增量漏登**：`0cba0a1` 给 `public abstract class CommandLineArguments` 加了 `public bool Check { get; internal set; }`（`Compilers\Core\Portable\CommandLine\CommandLineArguments.cs:36`），**未记入 `Compilers\Core\Portable\PublicAPI.Unshipped.txt`**——该文件仅 27 行、自 `e814cb1`（2026-07-30 导入基础编译器）起再无改动、`CommandLineArguments` 在其中零命中，而该类型在 `PublicAPI.Shipped.txt:449` 起被跟踪。**本 fork 的构建配置（props/targets/csproj/`Directory.Build.*`）内无任何 `PublicApiAnalyzers` / `RS0016` 接线**，故 §四 所写「`PublicAPI.Shipped.txt` 把关」**当前无执行机制**。（对照：shebang 提交 `04831f6` 是依规把节点写进 `Compilers\VisualBasic\Portable\PublicAPI.Unshipped.txt` 的。）
2. **§2.9 锚点漂移**：`ResolveReferenceDirective` 现定义在 `:892`（条目写 `:878-902`）、directive 循环在 `:820`（写 `:833-848`）、`ExplicitReferences` 在 `:857`（写 `:853-858`）。该条目与 `2f94ea7` 同一提交写入，锚的是当时未定稿的状态；补登记时应一并订正。

**清点中未核实、故不入表**：`ad1824f` 的 `Parser\Parser.vb` 改动点是否即 §2.8 登记的 `IsFirstStatementOnLine`；`b2d5d7f` 在 `:526` 另加 `optimize` 分支的动机（上游 `:833` 已有同名分支）。标 `Suspect`，补登记时先核。

## 三、合并步骤

1. **拉取上游**：`git -C {{Roslyn}} fetch origin release/stable`，记录新 commit 到「一、上游基准」。
2. **Public API 对账**：`git -C {{Roslyn}} diff <base> <new> -- src/Compilers/VisualBasic/Portable/PublicAPI.Shipped.txt`，与本 fork 比对——**编译器 public API 面不得破坏**（长期约束，见 `proposals\proposal-vbscript-lsp.md`「补层与构建机制」）。
3. **逐区合并修改面**：对「二、本地修改面清单」逐条 3-way 评审（上游 base → 上游 new → 本地）。修改面外文件可直接跟随上游，不必逐个评审。**「二·补、已知欠账」的条目须先逐 diff 复核心态、补登记进「二」，再按本条评审**——欠账未清前，本步不算完成。
4. **构建与测试**：跑 `scripts\verify-vb-compiler-tests.ps1` 七门 gate（Compiler/Syntax/Symbol/Semantic/IOperation/Emit/CommandLine）；`Scripting\VisualBasicTest` 直接跑程序集 `-automated`（dotnet test 静默不跑）。
5. **文档同步**：合并后更新 `spec\README.md` 版本历史、`compilers-index.md` 版本快照、本账本「一、上游基准」。

## 四、合并纪律

- 本地修改保持「**新增为主、尽量不侵入既有 public API**」；`PublicAPI.Shipped.txt` 把关。**注意：本 fork 构建内未接 `PublicApiAnalyzers`（无 `RS0016` 接线），该门当前靠人工自觉**——`PublicAPI.Unshipped.txt` 需手写增量；已发现一处漏登（见「二·补、已知欠账」已核实第 1 条）。
- 修改面外文件不本地化；需要本地化时先在「二、本地修改面清单」登记再改。
- 无法核实的上游变更标注 `Suspect` / `OPEN QUESTIONS`，不写死。
- 合并后任何测试 gate 不绿即视为未完成，不得静默跳过。

## 五、引用互链

- `compilers-index.md`「版本快照」节 → 上游基准见本文件。
- `decisions.md` D1/D2/D4 → 修改面语义依据。
- `spec\README.md` 版本历史 → 产品能力随版本归档。
