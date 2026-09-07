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
- `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:345-368` — `CreateScriptCompilation`；`:712`/`:723` 引用复用；`:816-868` `HasSubmissionResult`（脚本末尾表达式判定，支撑 REPL 打印与退出码）。

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

### 2.7 新增（相对上游的新文件/项目）

- `Interactive\vbi\` — vbi 交互解释器宿主。
- `Scripting\Core\`（common scripting fork）、`Scripting\VisualBasic\` — 脚本运行时。
- `Workspaces\SharedUtilitiesAndExtensions\Compiler\` — CompilerExtensions 局部移植（.shproj）。
- `Samples\`、`Installer\` — 样例与分发。

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

## 三、合并步骤

1. **拉取上游**：`git -C {{Roslyn}} fetch origin release/stable`，记录新 commit 到「一、上游基准」。
2. **Public API 对账**：`git -C {{Roslyn}} diff <base> <new> -- src/Compilers/VisualBasic/Portable/PublicAPI.Shipped.txt`，与本 fork 比对——**编译器 public API 面不得破坏**（长期约束，见 `proposals\proposal-vbscript-lsp.md`「补层与构建机制」）。
3. **逐区合并修改面**：对「二、本地修改面清单」逐条 3-way 评审（上游 base → 上游 new → 本地）。修改面外文件可直接跟随上游，不必逐个评审。
4. **构建与测试**：跑 `scripts\verify-vb-compiler-tests.ps1` 七门 gate（Compiler/Syntax/Symbol/Semantic/IOperation/Emit/CommandLine）；`Scripting\VisualBasicTest` 直接跑程序集 `-automated`（dotnet test 静默不跑）。
5. **文档同步**：合并后更新 `spec\README.md` 版本历史、`compilers-index.md` 版本快照、本账本「一、上游基准」。

## 四、合并纪律

- 本地修改保持「**新增为主、尽量不侵入既有 public API**」；`PublicAPI.Shipped.txt` 把关。
- 修改面外文件不本地化；需要本地化时先在「二、本地修改面清单」登记再改。
- 无法核实的上游变更标注 `Suspect` / `OPEN QUESTIONS`，不写死。
- 合并后任何测试 gate 不绿即视为未完成，不得静默跳过。

## 五、引用互链

- `compilers-index.md`「版本快照」节 → 上游基准见本文件。
- `decisions.md` D1/D2/D4 → 修改面语义依据。
- `spec\README.md` 版本历史 → 产品能力随版本归档。
