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

- `Compilers\VisualBasic\Portable\Errors\Errors.vb:1203`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces = 32134`）、`:1813`（`ERR_ExpressionTreeContainsAbstractStaticMemberAccess = 37340`）、`:1815`（`ERR_NextAvailable` 由 37340 推进为 37341）。
- `Compilers\VisualBasic\Portable\Errors\ErrorFacts.vb:923`（32134 入「非报错」集）、`:1561`（37340 入报错集）。
- `Compilers\VisualBasic\Portable\VBResources.resx:2748`、`:5491` —— 两条新消息文案。

- 改动形状：新文件 + `Friend` 面增量 + 一处由 `Protected Overridable` 扩参的收集入口（`CollectProbableExtensionMethodsInSingleBinder`）。新增类型位于 Friend 面（`Friend Module ReducedExtensionMemberReducer`、`Friend NotInheritable Class ReducedExtensionOperatorSymbol`）；`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` 本次零动——证据：`git show --name-only e307d0f` 对 `publicapi` 零命中，且 `PublicAPI.Unshipped.txt` 最后一次改动仍是 shebang 提交 `04831f6`。本提交亦不触及 `Compilers\Core\`：`PEModule.HasExtensionAttribute`（`Compilers\Core\Portable\MetadataReader\PEModule.cs:1031`）与 `PEModule.HasExtensionMarkerAttribute`（同文件 `:1061`）为既有共享钩子，本提交只调用。
- 折抵：主体是**消费 C# 侧已存在的元数据**（C# 14 `<G>$` 分组类型 / `<M>$` 标记类型 / `System.Runtime.CompilerServices.ExtensionMarkerAttribute`，C# 11 `static abstract` 接口成员），不改 VB 自身可声明语义——`IsExtensionGroupingType` 基类恒 False（`NamedTypeSymbol.vb:286`），`PENamedTypeSymbol.vb:956` 为 `Compilers\` 内唯一 override，故源码符号不会被识别为扩展分组类型。本地形态分叉四处，3-way 时可察觉：① `T.X` 由无条件 BC32098 改为接口约束有效集查找（`Binder_Expressions.vb:2920-2977`）；② 发射层新增 `constrained.` + `call` / `ldftn`，`CallKind.ConstrainedCall` 为 fork 新枚举成员；③ 错误码 32134 / 37340 与 `ERR_NextAvailable` 推进落在与上游共享的 `Errors.vb` 编号带；④ `Binder.CollectProbableExtensionMethodsInSingleBinder` 这一既有 `Protected Overridable` 签名被扩参。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`Binder.CollectProbableExtensionMethodsInSingleBinder`（签名）、`Binder.CollectExtensionMembersFromBinders`、`Binder.ReportDiagnosticsIfObsoleteOrNotSupported`（新增可选参）、`MemberLookup.LookupForExtensionMethods`（新增 `ByRef` 参）、`MemberLookup.MergeExtensionPropertiesIfNecessary`、`OverloadResolution.CollectUserDefinedOperators`（新增可选参 `binder`）、`NamedTypeSymbol` 的 `IsExtensionGroupingType`·`AppendProbableExtensionMembers`·`GetExtensionMembers`·`AddExtensionMemberLookupSymbolsInfo`·`IsNameableExtensionMember`、`NamespaceSymbol` 的 `GetExtensionMembers`·`AddExtensionMember`·`AddExtensionMemberLookupSymbolsInfo`、`MethodSymbol.IsExtensionMember`、`PropertySymbol.IsExtensionMember`、`PENamedTypeSymbol` 的 `IsExtensionGroupingType`·`IsExtensionMarkerType`·`TryGetExtensionMarkerMethod`·`GetExtensionReceiverType`、`PEMethodSymbol.IsExtensionMember`·`IsStaticAbstractInterfaceMember`、`CallKind.ConstrainedCall`、`Binder_Expressions` 的 TypeParameter 成员访问分支与 `IsStaticAbstractInterfaceMember`、`TypeSymbolExtensions.CanContainUserDefinedOperators`、`Errors.vb` 的 32134·37340·`ERR_NextAvailable`。上游若日后以其它形状实现 C# 14 扩展成员消费或 C# 11 SAIM 经类型参数消费（例如上游 VB 自补约束接口查找、`constrained.` 发射或 `T.X` 诊断改写），按上游形状对齐并回退本 fork 改法；新增文件 `ReducedExtensionMemberSymbols.vb` 为本地归约层，不与上游路径冲突。
- 对应设计：`proposals\proposal-consume-csharp-extension-and-interface-shared.md`；`meetings\meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION：只消费、零新语法，状态 Active、D4 P1 判入）；`spec\spec-consume-csharp-extension-members.md`、`spec\spec-consume-interface-shared-members.md`；实施样本 `tasks\consume-csharp-extension-and-interface-shared\`（含 `verification-checklist.md`）。测试锁定 `Compilers\VisualBasicSemanticTest\Semantics\ExtensionMemberConsumptionTests.vb`、`Compilers\VisualBasicSemanticTest\Semantics\InterfaceSharedMemberConsumptionTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMemberConsumptionSymbolTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\StaticAbstractMembersInInterfacesTests.vb`。

### 2.20 脚本顶层崩溃族一次收口（issue 04–11 对应单元 F04–F11，修改）

改动主体：`tasks\script-top-level-crashes\`（八条 issue 的逐条收口；判定表、逐单元蓝图与验收网见该文件夹四件套）。**其中 `Symbols\Source\` 的五个文件（`SourceMethodSymbol.vb` / `SourceMemberContainerTypeSymbol.vb` / `SynthesizedEventAccessorSymbol.vb` / `SourceWithEventsBackingFieldSymbol.vb` / `SynthesizedWithEventsAccessorSymbol.vb`）、`Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb` 与 13 份 `xlf` 为本条目首次在册**。

- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMethodSymbol.vb`（**新增在册**）—— F04：早期解码进门条件由三重改四重，补 `Me.IsShared`（`:1500-1503`），使紧随其后的 `Debug.Assert(Me.IsShared)`（`:1505`）不可达；完整解码序列补 `ElseIf Not Me.IsShared Then` 分支（`:1634-1635`，报新码 BC37005），落在 `ParameterCount = 0` 分支（`:1631-1632`）之后、`Else`（`:1637`，内含 `:1638` 的断言）之前。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`（**新增在册**）—— F05：`TypeKind = Submission` 分支按 `isShared` 分叉——共享时走同文件的 `EnsureCtor`（`:2729`，无参 `SynthesizedConstructorSymbol`，`:2774-2805`），实例时保持 `New SynthesizedSubmissionConstructorSymbol(...)`（`:2738`）原样。F11：顶层语句收集点的 `Case SyntaxKind.LabelStatement … Exit Select` 丢弃分支删除（**移除上游 `' TODO (tomat): should be added to the initializers` 注释**），顶层标签语句改由既有的 `Case Else`（`:2621-2633`）与其它顶层可执行语句同款收集进实例初始化器序列。
- `Compilers\VisualBasic\Portable\Symbols\Source\SynthesizedEventAccessorSymbol.vb:495`、`Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:66`、`Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93`（**均新增在册**）—— F07：三处 `Debug.Assert(Not …IsImplicitlyDeclared)` 收窄为 `Debug.Assert(Not …IsImplicitClass)`。**登记理由**：三处断言均为**上游行为**（三个文件的最近一次提交都是 `e814cb1 add base compiler`，断言随上游树进入、未被本地改过；改动仅为谓词收窄），本地改动属「修上游在脚本/提交类（`IsScriptClass`）下的可达缺陷」；断言不是控制流（其后 `AddSynthesizedAttribute` 无条件执行），Release 下断言本就被编译掉——**HEAD Release 构建实测**：顶层 `Event` exit 0（`EVENT-OK`），Debug 同形状 exit 35 ⇒ 该收窄零行为变化。
- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 已在册）—— F08 新锚点：`:2264-2273` 的脚本类分支里，**隐式**引用先查 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2235-2255`），命中共享上下文则落 `:2275-2281` 的 BC30369；显式引用路径（`:2266`）逐字保持。F06 新锚点：`IsInSharedInitializerContext()`（`:4737-4741`）与 `BindAwait` 调用结构里的新支（`:4753-4754`，报新码 BC37341）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Initializers.vb`（**新增在册**；本文件原先只在「二·补」的 `7a0111e` 行被记为未登）—— F09：`ProcessedFieldOrPropertyInitializers` 增「本路绑定期报过 error」可选入参（`:19-55`，`:51` 签名 / `:54` 合成 `HasAnyErrors`），`Empty` 单例语义不变。F10：顶层语句绑定后补跑只查 `Await` 位置的 walk（`:140` 调用点 → `:238-249` 的 `CheckAwaitInTryHandler`）。
- `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb`（§2.4 / §2.19 已在册）—— F10 新锚点：`CheckOnErrorAndAwaitWalker` 增 `onlyCheckAwaitInTryHandler`（`:469` 字段 / `:472` 构造参，**默认 `False` ⇒ `BindMethodBlock` 路径零改动**）与 `VisitBlockOnlyCheckAwaitInTryHandler` 入口（`:523-537`），只保留 BC36943 分支（`:625-636`）。
- `Compilers\VisualBasic\Portable\Compilation\MethodCompiler.vb`（**新增在册**）—— F09：初始化器的静态桶与实例桶各自 `AddRangeAndFree` 进本桶**独立诊断袋**（`:614` / `:623`，绑定调用在 `:609` / `:618`），并把该袋的 error 状态传入 `ProcessedFieldOrPropertyInitializers`；**`declarationErrors`、`:1273` 的逐方法袋与 `:1288` 的发射门表达式形状均不变**（不新增门项）；诊断集合与顺序不变（`AddRangeAndFree` 保序）。
- `Compilers\VisualBasic\Portable\Errors\Errors.vb`、`Errors\ErrorFacts.vb`、`VBResources.resx`、`xlf\VBResources.*.xlf`（13 份；**13 份 xlf 为本条目首次在册**）—— F04 新码 `ERR_ExtensionMethodNotShared = 37005`（`Errors.vb:1636`；占用官方空带 `37004`（`:1634`）与 `37050`（`:1637`）之间的**起始槽位**，该带现余 37006–37049 为空）；F06 新码 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`），`ERR_NextAvailable` 由 37341 推进为 37342（`:1818`）。两枚码各走完 `Errors.vb` → `ErrorFacts.vb`（`IsBuildOnlyDiagnostic` 的「非 build-only」清单）→ `VBResources.resx` → 13 份 xlf 全链（每份 `+2` 个 `trans-unit`、`+10/-0` 行，`<target state="new">`）。
- 测试（新增 / 增量，非上游路径）：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMethods\ExtensionMethodTests.vb`、`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedInitializerTests.vb` · `SubmissionTopLevelLabelTests.vb` · `SubmissionEventMemberTests.vb` · `InitializerDiagnosticGatingTests.vb`、`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`，以及 `scripts\verify-vb-compiler-tests.ps1` 的各门期望值同步。
- 改动形状：行为改动集中在 `Symbols\Source\`（构造器分叉 + 顶层语句收集 + 三处断言收窄）、`Binding\`（四条判据 / 一处补跑检查）、`Compilation\MethodCompiler.vb`（初始化器桶的诊断袋归属）。**零公共面**：`ERRID` 是 `Friend Enum`，两枚新码不进 `PublicAPI.*.txt`（实测 `git status` / `git diff --stat` 对 `*PublicAPI*` 零输出）；`Compilers\Core\Portable\CodeGen\` 零改动（崩溃栈顶的 `ILBuilder` / `BasicBlock` 不修，改法是「不让坏形状活到发射期」）。
- 折抵：F09 只让**已报出的**初始化器 error 参与既有发射门（无初始化器 error 的编译逐字节不变）；F10 复用既有 walker 且开关默认关（方法体路径不变）；F04 / F06 / F08 只**新增**诊断；F05 / F07 / F11 只把「编译期零诊断却进程级失败」的形状修成正常——无一条改变合法既有代码的语义。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`SourceMethodSymbol` 的扩展方法早期 / 完整解码序列、`SourceMemberContainerTypeSymbol` 的 submission 构造器分叉与顶层语句收集点（含被移除的 `tomat` TODO）、三处 `AddSynthesizedAttributes` 的断言谓词、`Binder_Expressions` 的 `CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext` 与 `BindAwait` / `IsInSharedInitializerContext`、`Binder_Initializers` 的 `ProcessedFieldOrPropertyInitializers` 与顶层语句绑定尾、`Binder_Statements` 的 `CheckOnErrorAndAwaitWalker`、`MethodCompiler` 的初始化器绑定与 `:1288` 发射门、`Errors.vb` 的 37005 / 37341 / `ERR_NextAvailable`。上游若日后以其它形状实现同类脚本诊断、顶层标签收集或初始化器发射门，按上游形状对齐并回退本 fork 改法。
- 对应设计：`tasks\script-top-level-crashes\`（`README.md` 判定表与分批 / `design-detailed.md` 逐单元蓝图与 pass 条件 / `design-overview.md` / `test-plan.md`）；测试锁定见上一段的四个编译器测试文件与 `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`。

### 2.21 脚本顶层崩溃族第二轮收口（U1–U7 / U9 / U12，修改 + 新增）

改动主体：`tasks\script-top-level-crashes-2\`（逐单元收口第一轮显式排除的第四处断言与实施期发现的预存在崩溃；判定表、逐单元蓝图与验收网见该文件夹四件套）。**其中 `Symbols\Source\SourceMemberMethodSymbol.vb`（§2.20 只登了同目录的 `SourceMethodSymbol.vb`，两者不是同一文件）与 `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb` 为本条目首次在册**；`Errors\Errors.vb` / `Errors\ErrorFacts.vb` / `VBResources.resx` / 13 份 `xlf` 已在 §2.19 / §2.20 在册，本条目只补新增点。

编译器产品源文件（8 个）：

- `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 / §2.20 已在册；本次 `+2 −1`）—— **U4**：`BindMyBaseExpression` 的**错误路径**（`CanAccessMyBase` 为假，`:2372-2376`）在构造 `BoundMyBaseReference` 时把「取不到基类型」折成 `ErrorTypeSymbol.UnknownResultType`（`:2375`），与同文件 `BindMeExpression`（`:2351`）/ `BindMyClassExpression`（`:2387`）已有的兜底写法一致。**3-way 注意**：只动错误路径；正常路径（`:2379`）一字不动，普通 `Module` / `Structure` / 有基类的 `Class` 三种对照的诊断与产物不变。
- `Compilers\VisualBasic\Portable\Binding\Binder_Initializers.vb`（§2.20 已在册；本次 `+66 −0`）—— **U5**：给顶层语句补跑「分支不得离开 `Finally`」判据。调用点 `:144`（紧随第一轮 `:134-140` 的 `CheckAwaitInTryHandler` 调用点），新方法 `CheckBranchOutOfTopLevelFinally`（`:264`）与便宜前置门 `ContainsFinallyBlock`（`:301`，递归 `ChildNodes` 找「子树里含 `FinallyBlock` 的 `TryBlockSyntax`」）。**3-way 注意**：① 前置门必须按**该语句的语法子树**判，**不能**按顶层语句的语法种类判——`Finally` 常嵌在 `For` / `While` / `Using` / `Catch` 里，按种类判会整批漏掉；② 只取 `ERR_BranchOutOfFinally`，其余流分析诊断（可达性 / 未赋值 / 未使用）本次**不**引入（那是 issue 17 的另一半）；③ 调用点选在「刚绑完该条语句」处，保证 binder 与语句树的归属一致。
- `Compilers\VisualBasic\Portable\Errors\Errors.vb`（§2.19 / §2.20 已在册；本次 `+3 −1`）—— 两枚新码（见下「两枚新诊断码」）。
- `Compilers\VisualBasic\Portable\Errors\ErrorFacts.vb`（§2.20 已在册；本次 `+2 −0`）—— 两条 id 加进 `IsBuildOnlyDiagnostic` 的「非 build-only」清单（`:1563` / `:1564`）。**必须同步**，否则 `DiagnosticTests.TestIsBuildOnlyDiagnostic` 抛 `NotImplementedException`、Semantic 门整体挂掉。
- `Compilers\VisualBasic\Portable\Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb`（**新增在册**；本次 `+7 −8`）—— **U2 + U12**：`If` 判据由 `receiver Is Nothing OrElse receiver.IsMeReference` 扩为 `… OrElse receiver.Kind = BoundKind.PreviousSubmissionReference`（`:27`，让自定义事件走整棵调用树降级这条路），并在 `Else` 分支的 `IsWindowsRuntimeEvent` 之前对接收者显式降级 `receiver = VisitExpressionNode(receiver)`（`:37`）；同时**删掉原 `#If DEBUG` 断言块**（`receiver.Kind = BoundKind.FieldAccess`，在自定义事件上没有后备字段 ⇒ 前提不成立）。保留分支的 `Debug.Assert` 现于 `:33`。**3-way 注意**：本差异是「接收者形状」的 VB 脚本语义知识，落在 VB 降级层；共享 `Compilers\Core\Portable\CodeGen\` **零改动**（`EmitExpression.vb:206-209` 的兜底分支不动）。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`（§2.20 已在册；本次 `+28 −6`）—— **U1**：新增私有 `AddMethodMember`（`:2696`），`AddMember` 的两个方法分支（`SubNewStatement` 与一般 `Sub`/`Function`，原 `:2572` / `:2586`）由「`CreateMethodMember` 后直接 `AddMember`」改为经它中转；**脚本类**的**实例**构造器在此报新码且**不加入成员表**（共享构造器不受影响）。门的判据是 `Me.IsScriptClass AndAlso methodSymbol.MethodKind = MethodKind.Constructor`（`:2706`）：`IsScriptClass`（`:1295-1300`）为 `DeclarationKind.Script OrElse DeclarationKind.Submission`，恰好是两类脚本类，且与 `DeclarationKind.ImplicitClass` 互斥（`IsImplicitClass` 于 `:1302-1306`），游离语句的隐含类不被卷入；同文件 `:2786`（合成初始化器与入口点）本就以 `IsScriptClass` 为判据。**只判 `TypeKind.Submission` 会漏掉非提交脚本类**：那类脚本类由生成的 `<Main>` 实例化（`Symbols\Source\SynthesizedEntryPointSymbol.vb:258-281` 的 `GetScriptConstructor()` + `Dim script As New Script()`），漏判时同一崩溃原样存在（实测：`IsSubmission=False`、`TypeKind=Class`、`IsScriptClass=True`，`GetDiagnostics` 与 `Emit` 均抛 `InvalidCastException`：`SourceMemberMethodSymbol` → `SynthesizedConstructorBase`）。**不加入成员表是必须的**：只要两个 `.ctor` 同时在场，`NamedTypeSymbol.GetScriptConstructor`（`Symbols\NamedTypeSymbol.vb:697-700` 的 `InstanceConstructors.Single()`）就抛；改用该判据后，非提交脚本类的空参用户构造器不再让 `EnsureCtor`（`:2796-2827`）提前返回，仍合成唯一的实例构造器。**3-way 注意**：① 本改动与 §2.20 的 F05（同文件 `AddDefaultConstructorIfNeeded`，`:2744-2762` 的提交构造器按 `isShared` 分叉）是同一文件的两处，评审时一起看。② **这道门是 fork 新增、上游无对应实现**：上游继承的是崩溃本身，C# 与 VB 同形（读码实证，未运行 C# 复现）—— C# 的 `IsScriptClass` 与 VB 逐字相同（`Compilers\CSharp\Portable\Symbols\Source\SourceMemberContainerSymbol.cs:1022-1029`），同样在 `IsScriptClass` 为真时合成初始化器与入口点（`:5721-5727`，对应 VB 的 `SourceMemberContainerTypeSymbol.vb:2786`），入口点工厂按 `compilation.IsSubmission` 分流到 `ScriptEntryPoint`（`Symbols\Synthesized\SynthesizedEntryPointSymbol.cs:27-44` / `:482`），其 `CreateBody` 调 `_containingType.GetScriptConstructor()`（`:516`），C# 的实现同为 `(SynthesizedInstanceConstructor)InstanceConstructors.Single()`（`Compilers\CSharp\Portable\Symbols\NamedTypeSymbol.cs:590-593`），且 C# 侧没有该形状的诊断（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:2099` 中 submission 相关只有 `ERR_ScriptsAndSubmissionsCannotHaveRequiredMembers = 9045`）；C# 里顶层成员不能声明实例构造器，该形状在 C# 里不可拼写。**合并时不得把这道门当「本地无谓改动」精简，也不得按上游形状覆盖**。
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberMethodSymbol.vb`（**新增在册**；本次 `+15 −3`）—— **U3**：`BindSingleHandlesClause` 的 `Select Case ContainingType.TypeKind` 合法分支并入 `TypeKind.Submission`（`:778`，与 `Class` / `Module` 同列）。**U9**：`isFromBase` 分支的 `DirectCast(Me.ContainingType, SourceNamedTypeSymbol)` 换成 `TryCast` + 判 `Nothing` 则报 `BC37343` 并 `Return Nothing`（语句 `:707-711`，`GetOrAddWithEventsOverride` 现于 `:713`，判据注释块 `:702-712`）。**3-way 注意**：`DirectCast` 是本 fork 相对上游的形状差异点（上游对提交类根本走不到这个分支）；该分支原为上游代码，本 fork 只把「必然崩溃」改成「报诊断」。
- `Compilers\VisualBasic\Portable\VBResources.resx`（§2.19 / §2.20 已在册；本次 `+6 −0`）—— 本次**两枚新码**的英文文案（`ERR_SubmissionCannotDeclareInstanceConstructor` / `ERR_WithEventsVariableNotInContainingType`），无其它条目改动。

资源 / 本地化（13 份，§2.20 已在册；本次每份 `+46 −0`）：

- `Compilers\VisualBasic\Portable\xlf\VBResources.{cs,de,es,fr,it,ja,ko,pl,pt-BR,ru,tr,zh-Hans,zh-Hant}.xlf` —— 每份净增 **9 条 `trans-unit`**（`<target state="new">`、`target` 与 `source` 逐字等于 resx `<value>`）：本次两枚新码各 1 条，另 7 条是 **U6a 补齐的既有失同步条目**（`ERR_ExpressionTreeContainsAbstractStaticMemberAccess`、`ERR_LoadDirectiveOnlyAllowedInScripts`、`ERR_PPLoadFollowsToken`、`ERR_PPReferenceFollowsToken`、`ERR_ShebangDirectiveOnlyAllowedInScripts`、`ERR_ShebangDirectiveNotOnFirstLine`、`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces` —— 这 7 条的 resx 侧本来就有，此前只有 13 份 xlf 缺条目，`Directory.Build.props:4-5` 的 `ErrorOnOutOfDateXlf=false` / `UpdateXlfOnBuild=false` 让任何构建门都不报）。**既有条目逐字节未改**（比对时两侧 EOL 已归一）。**3-way 注意**：13 份 xlf 的既有条目顺序本就与 resx 顺序有倒挂，新条目按**该文件里的本地邻居**定位插入，不按 resx 顺序重排；上游若整体重生成 xlf，按上游结果接受即可（新条目已带 `state="new"`）。

测试与门脚本（非上游路径）：

- `Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`（`+496 −0`，新增 **20 条** `<Fact>`/`<Theory>`：W1 的 U4 4 条 + W2 的 U1/U5 12 条 + U1 非提交脚本类 4 条）。后 4 条走**非提交脚本编译**（`VisualBasicCompilation.Create` + `TestOptions.Script` + `WithScriptClassName`），与既有 `CreateSubmission` 用例分属两条构造路径；Semantic 门实测总数随之由 `5822/5718/104/0` 变为 `5826/5722/104/0`，`scripts\verify-vb-compiler-tests.ps1` 的 Semantic 期望值同步为该值。
- `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`（`+415 −8`，该类的用例数 **25 → 44**，新增 **19 条**：W1 的 U2/U3/U4 7 条 + W2 的 U1/U5 8 条 + U9 的 4 条，其中 `SameSubmissionHandles_StillDelivers` 是控制锁）。
- `Scripting\VisualBasicTest\ScriptModeConformanceTests.vb`（新增，716 行，内含 `ScriptModeDeclarationConformanceTests` 22 条）/ `ScriptModeStatementConformanceTests.vb`（新增，675 行，41 条）/ `ScriptModeSubmissionConformanceTests.vb`（新增，491 行，28 条）—— **U7** 的脚本模式符合性矩阵（**89 条**），U12 又在其上补 2 条自定义事件成功臂 ⇒ 三个类现共 **91 条**（断言臂 `AssertRuns` / `AssertReports` / `AssertEmits` / `AssertReplSession`，全部内存 API、无副作用）。
- **`Scripting` 程序集合计**：用例数 **344 → 454**（收口实测 `-automated`：`TestCasesToRun = 454`、454 passed / 0 failed）。344 由两路互证得出——收口实测 454 减去「唯一被改的既有文件 +19 与三个新文件 +91」，与 W1 验证者独立跑出的基线 344 一致。
- `scripts\verify-vb-compiler-tests.ps1`（`+1 −1`）—— **只改数字**：Semantic 门期望值 `5806/5702/104/0` → `5826/5722/104/0`（其余六门与基线逐字一致，本任务实测零增量）。判据未改。

文档（`InternalDevDocs\`）：

- `issues\README.md` + **17 份** issue 正文——新增 9 份（12–20，其中 19 / 20 为本次两个非计划单元的补登记），另 8 份既有正文改「状态 / 锚点 / 计数」（04–11 的 8 份）。
- `spec\spec-scripting-dialect.md`（`+19 −6`）—— 顶层 `GoTo` 条目的 Decision 由「本规范不保证」改为**受本规范保证**（含新增语义示例与「重复标签报 BC30094」一句）；`Handles` 条目改写为「提交类不合成 hookup 构造器（承载 hookup 的实例构造器就是编译器已合成的提交构造器）+ 跨提交 / 宿主对象的变量报 `BC37343`」；诊断分层那句由「其余限制在绑定期报出」改为「解析之后、绑定期与包含类型的成员构建期报出」（`BC37342` 报在成员构建期，见上）；`BC37342` 的条件列改述两类脚本类（提交类由宿主实例化、非提交脚本类由生成的入口点实例化，都只有编译器合成的那一个实例构造器槽位），C# 对照列收紧为「C# 无法在提交类上声明实例构造器、且在声明了实例构造器时不合成提交构造器」；Testing 段的 Restriction tests 补入 `BC37341` / `BC37342` / `BC37343` 三枚码与各自不受影响的同提交形式。
- `tasks\script-top-level-crashes-2\`（新增，四件套 4 份）。
- 本文件。

**两枚新诊断码与官方错误码带的落点**：

- **`BC37342` `ERR_SubmissionCannotDeclareInstanceConstructor`**（`Errors.vb:1817`）—— 脚本类不支持声明**实例**构造器（提交类的实例由宿主创建、非提交脚本类的实例由生成的入口点创建，构造器都由编译器合成），初始化代码放顶层语句。触发面：提交类与非提交脚本类的顶层 `Sub New()` / `Sub New(x)`（`Protected` / `Private` / `Public` 同判定）；`Shared Sub New()` 与脚本类内嵌套普通类的构造器不受影响。
- **`BC37343` `ERR_WithEventsVariableNotInContainingType`**（`Errors.vb:1818`）—— `Handles` 引用的 `WithEvents` 变量来自**上一提交或宿主对象**（提交类只是可见它、并未声明它）。触发面：跨提交 / 宿主对象的 `WithEvents` 变量上的 `Handles` 子句。
- **落点**：两枚码都落在 fork 原有增长带的**前沿尾部**，紧接第一轮的 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`）之后连续占用 **37342 / 37343**，`ERR_NextAvailable` 随之由 37342 推进为 **37344**（`:1820`）。与 §2.19 的 `ERR_ExpressionTreeContainsAbstractStaticMemberAccess = 37340`（`:1815`）是同一模式。注意 `Errors.vb:1601-1606` 的「Fork error-numbering policy」注释约束的是**官方间隙**型新增（36959 / 36967 / 37002 …，两侧都有既有编号）；尾部前沿连续占用是第二类，两种都在本 fork 既有实践中。
- **零公共面**：`ERRID` 是 `Friend Enum`，两枚码**不进** `PublicAPI.*.txt`（本任务实测 `git status` 对 `*PublicAPI*` 零输出）。
- **未来撞号风险（3-way 要点）**：上游自己的下一个 37xxx 错误由上游 `ERR_NextAvailable` 决定；若上游在同一带内新增错误，本 fork 的 37340–37343 需要按上游形状对齐（与 §2.20 登记的 37005 / 37341 同一处置）。

- 改动形状：行为改动集中在 `Binding\`（U4 一处兜底 + U5 一处判据补齐）、`Symbols\Source\`（U1 的成员表闸 + U3 的枚举扩项 + U9 的 `TryCast` 判据）、`Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb`（U2 / U12 的接收者判据与降级）；资源侧是两枚新码 + 13 份 xlf 的失同步补齐。**零公共面**：`ERRID` 是 `Friend Enum`；`Compilers\Core\Portable\CodeGen\` **零改动**（`CodeGen\EmitExpression.vb:206-209` 的 `Throw ExceptionUtilities.UnexpectedValue` 不修，改法是「不让坏形状活到发射期」）。
- 折抵：U2 / U3 / U4 / U12 只让**本族形状**从「崩」变「正常跑」（普通编译上下文对照零行为变化）；U1 与 U9 只**新增**诊断（用户可见行为由「进程终止」变「一条编译错误」）；U5 只**新增**一条既有诊断（`BC30101`）在顶层语句路径上的报点；资源侧只补 13 份 xlf 的缺失条目。无一条改变合法既有代码的语义。
- 合并前评估义务：合并前读本条目对下列符号做 3-way 评审——`Binder_Expressions.BindMyBaseExpression` 错误路径、`Binder_Initializers` 的顶层语句绑定尾（`CheckBranchOutOfTopLevelFinally` / `ContainsFinallyBlock`）、`SourceMemberContainerTypeSymbol.AddMethodMember`（判据 `IsScriptClass`，覆盖提交类与非提交脚本类；与 §2.20 的 F05 提交构造器分叉同文件、一起看）、`SourceMemberMethodSymbol.BindSingleHandlesClause` 的 `Select Case` 与 `isFromBase` 分支、`LocalRewriter_RaiseEvent.VisitRaiseEventStatement` 的接收者判据（含被删除的 `#If DEBUG` 断言块）、`Errors.vb` 的 37342 / 37343 / `ERR_NextAvailable`。上游若日后以其它形状实现同类脚本诊断、`Handles` 宿主枚举或事件降级，按上游形状对齐并回退本 fork 改法。
- 对应设计：`tasks\script-top-level-crashes-2\`（`README.md` 判定表与非范围 / `design-overview.md` 总体设计与全称主张剪枝 / `design-detailed.md` 逐单元蓝图与 pass 条件 / `test-plan.md` L1–L4 矩阵与无副作用纪律）；判定与依据的会议 / 提案出处见 `issues\issue-cross-submission-handles-clause-crash.md`（19）。

## 二·补、已知欠账：尚未登记的修改面（2026-09-11 清点）

> **先读口径与限制**：本节的判定方法是「**文件面筛 + diff 复核**」，**未**逐个读 diff 判定每个改动点是否恰好落在既有 §2.x 条目的语义面内。因此「未登记」是**文件面级**结论，完整度标 `Suspect`——**补登记时必须逐 diff 复核心态，不得直接采信本表**。
> 另注意：既有条目的锚点常为**行区间**（如 §2.1 只锁 `VisualBasicCommandLineParser.vb:61-63`、`:1522-1523`），**文件被某条登记 ≠ 该文件的所有改动点都被登记**。
> 清点范围：2026-07-27（上游基准日期）之后触及 `Compilers\` 的 22 个提交。
> **本节条目不计入修改面、未经 3-way 评审**；合并前须先按「三、合并步骤」第 3 步补登记，再逐条评审。

| 提交 | 日期 | `Compilers\` 规模 | 内容 | 判定 |
|---|---|---|---|---|
| `8377403` | 2026-08-23 | 28 文件 +6955/−1 | 新增 `Compilers\Core\MSBuildTask\` **整棵树**（.cs/.resx/.targets）+ `vbc.csproj` | **未登记** |
| `b60dcd3` | 2026-08-15 | 30 文件 +503/−63 | `#load` 语法节点全链（`Syntax.xml` 新节点 / `SyntaxKind` / `Scanner\KeywordTable` / `Scanner` / `CompilationUnitSyntax.vb` / `SyntaxFactory` / `SyntaxFacts` / `SyntaxNodeFactories` / `InternalSyntax\SyntaxToken` 等） | **未登记**（§2.5 仅两句泛述、**无文件级锚点**；旁证：fork 自有 `ERR_LoadDirectiveOnlyAllowedInScripts = 36967`，`Errors.vb:1606`） |
| `3dfb3bc` | 2026-09-02 | 27 文件 +1524/−155 | ref readonly / `[In]` / `RequiresLocation` modreq 消费（`Binder_WithBlock.vb` / `BoundExpressionExtensions.vb` / `WithExpressionRewriter.vb` / `Symbol.vb` / `Wrapped*` / `Substituted*` / `RetargetingMethod|PropertySymbol.vb` / `CustomModifierUtils.vb` / `SymbolDisplayVisitor.Members.vb` / `LocalRewriter_Call|With.vb` 等） | **未登记**（§2.3 只含 byref-like / ref struct，**不含 ref readonly**） |
| `5803347` | 2026-08-14 | 35 文件 +2165/−87 | consume ref struct 全面 | **部分登记**（§2.3 登了 6 个文件；另约 24 个产品文件未登，含 `Binder_Conversions` / `Binder_Invocation` / `Binder_Lambda` / `Binder_Utils` / `Binder_AnonymousTypes` / `Binder_Delegates` / `Binder_Expressions` / `Binder_Statements` / `Semantics\Conversions` / `SymbolDisplayVisitor.Types` / `PEMethod|Property|Event|FieldSymbol` / `ObsoleteAttributeHelpers` / `Retargeting|Substituted|WrappedNamedTypeSymbol` / `TypeSymbolExtensions` / `SourceMemberField|MethodSymbol` / `SourceMethod|PropertySymbol` / `LambdaRewriter.Analysis`） |
| `80eff5f` | 2026-08-16 | 8 文件 +127/−83 | 错误码重排（新增 `ERR_PPReferenceFollowsToken = 36959` 占官方间隙；`ERR_PPLoadFollowsToken` 36984→**37002**；`ERR_NextAvailable` 编号策略注释）+ `Binder_Delegates.vb` 归约扩展方法免 BC31393 例外 + `MergedNamespaceSymbol.vb` aliased-assembly 全局命名空间过滤 / `DeclarationsAccessibleWithoutAlias` + `VisualBasicCommandLineParser.vb` `/sdkpath`·`/nosdkpath`·`System.Private.CoreLib` 直引（`:134`/`:135`/`:690`/`:698`/`:704`/`:705`/`:1387`/`:1624`） | **未登记**（`Errors.vb` 现同载 4 批编号改动，仅 2 批在账） |
| `0cba0a1` | 2026-08-29 | 2 文件 +20/−1 | `/check` 编译不运行（`VisualBasicCommandLineParser.vb:98`/`:544`/`:1563`） | **未登记**；**内含一处 public API 增量未记录**（见下「已核实的两条」） |
| `28a0f95` | 2026-09-05 | 3 文件 +64/−7 | net472 分支叠入编译器包（`Core\MSBuildTask\Microsoft.Build.Tasks.CodeAnalysis.csproj`、`VisualBasic\vbc\AnyCpu\vbc.csproj`、`vbc\App.config`） | **未登记**（§2.7 只列 `Interactive\vbi`、`Scripting\Core`、`Workspaces\...`、`Samples`、`Installer`） |
| `b2d5d7f` | 2026-08-26 | 1 文件 +18 | 脚本模式放行 `/optimize`、`/optimize+`、`/optimize-`（`VisualBasicCommandLineParser.vb:526`、`:535`；`:833` 是上游既有同名分支） | **未登记** |
| `693e4a5` | 2026-08-01 | 11 文件 +91/−14 | WIP 移植 C# interactive 特性 | **部分登记**（`Binding\BinderBuilder.vb` / `BinderFactory.vb` / `TopLevelCodeBinder.vb` / `Lowering\SynthesizedSubmissionFields.vb` 等未登） |
| `7a0111e` | 2026-08-08 | 4 文件 +144/−4 | top-level 修正 | **部分登记**（§2.6 无文件级锚点；`Analysis\InitializerRewriter.vb` / `Symbols\AssemblySymbol.vb` 未登；`Binding\Binder_Initializers.vb` 的**文件面**已由 §2.20 补登 ⇒ 该文件不再是未登文件，7a0111e 对它的具体改动点随 §2.20 的 3-way 评审一并核） |
| `6b6e38c` | 2026-08-08 | 3 文件 +22/−2 | 返回值与样例修正 | **部分登记** |
| `bee85b3` | 2026-08-02 | 3 文件 +371/−6 | port tests；产品面仅 `SymbolDisplay\ObjectDisplay.vb`（REPL 显示助手） | **未登记（小面）** |

**不计为欠账**（理由在列）：`e814cb1`（1896 文件，建立 fork 裁剪树本体；「一、上游基准」已声明本 fork `Compilers\` 是修剪树 ≠ 上游镜像）、`7566bda` / `0c3de87` / `f1ff2f1` / `bdb7735` / `5727a3f`（产品面仅 `.vbproj` / `AssemblyInfo.vb` / 无产品面）、`ad1824f`（§2.4 已登记）、`e307d0f`（**已补 = §2.19**）、`2f94ea7`（**已覆盖 = §2.9**）。

### 已核实的两条（本表之外，main 直接核对）

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
