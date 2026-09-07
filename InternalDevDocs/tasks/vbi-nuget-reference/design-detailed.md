# 详细设计：`.vbx` / 交互窗口 NuGet 包引用（vbi-nuget-reference）

> 状态：详细设计（改动蓝图 A–I）。依据链：`../../meetings/meeting-vbi-nuget-reference.md`（RESOLUTION 1–9，唯一权威；U9 spike 已过 → R4 定稿）→ `../../proposals/proposal-vbi-nuget-reference.md` → `design-overview.md` → 本设计。
> 本设计把概要落到「可被实现者直接照做、验证者可无人值守核对」的代码级底稿。每条改动给 `文件:行号` + 改动形状 + **自动裁决规则**（当 X 发生做 Y 不做 Z）+ **pass 条件**。
> 源码锚点均为本计划撰写时逐条 Read/Grep 核实的**当前工作树**状态（含 U9 补丁已应用后的 `CommonReferenceManager.Resolution.cs`）。文件相对路径均相对仓库根。

## 0. 核心判定原则（贯穿全文）

- **接线已定稿（R4/U9）**：共享 Core 单 `#R`→N。工作树已应用 U9 补丁（`CommonReferenceManager.Resolution.cs` 为 M 状态）。实现期**不得回退**、不得顺手改其它地方；本设计 §A 是「收口」不是「再发明」。
- **不 alter 旧 public API**：`CSharpCompilation.GetDirectiveReference`（`Compilers\CSharp\Portable\Compilation\CSharpCompilation.cs:1316-1322`）、`PublicAPI.Shipped.txt`、`Compilation.cs:738` 的 `ReferenceDirectiveMap` 形状、`CommonReferenceManager.State.cs:241` 的单值 map——全部原样。
- **resolver 无诊断出参（R6）**：可读原因（版本缺失/SDK 缺失/近失配/还原失败/宿主能力）全在宿主预扫描造；resolver 层只当「会话缓存前端」，未命中返回空数组 → 走通用 `ERR_MetadataFileNotFound`。
- **引擎与会话状态落宿主（U1-B）**：`Scripting\Core` 只留 seam + 注入位；dotnet-shell 还原/临时工程/缓存逻辑放 VB 宿主层，共享 Core 不背。
- **零回归**：无 nuget 引用全链路零行为；所有新增 seam 参数默认 null / 空根集 = 现状。
- **文件路径规约**：下文引用仓库相对路径；新增文件相对仓库根。**测试纪律**：单测无副作用（不网络/不写文件/不 spawn 进程/不注册表）；涉及真实 `dotnet restore` / native 的验收为手工或门控集成项，不进无副作用单测。
- **测试执行规约（本仓特有）**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**不可用 `dotnet test`**（EXIT 0 但静默不跑）；验证一律直跑输出程序集：先 `dotnet build` 该项目，再 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或追加 `-method <FullyQualifiedName>`（单测过滤，如 `...CommandLineRunnerTests.TestX`）。见 memory `vb-scripting-test-runner`。

## 1. 改动清单总览

| # | 文件（相对仓库根） | 改动形状 | 目的（改动面） |
|---|---|---|---|
| A1 | `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs` | 仅文档标注（代码保持 U9 补丁形状） | Core-N 收口（§A1） |
| A1 | `InternalDevDocs\upstream-merge.md` | 新增 2.9 类别 | R4 merge 义务（§A1） |
| A2 | `Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb` | 新增 | U9 两场景正式单测（§A2） |
| B | `Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs` | 修改 `TryParsePackageReference` + doc | 语法 fork（§B） |
| B | `Scripting\VisualBasicTest\NuGetPackageResolverTests.vb` | 新增 | 解析锁定（§B） |
| C | `Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs` | 修改 `CreateCurrentPlatformResolver` 加可选参 | 注入位（§C） |
| C | `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` | 修改 ctor/`GetScriptOptions`/`GetMetadataReferenceResolver`/coordinator seam | 注入 + 宿主环缝（§C/§D） |
| C | `Scripting\VisualBasic\Hosting\`（新增若干 vb 文件） | 新增具体子类 + 会话 + 协调器 + restore 驱动 | U1-B 宿主落点（§C/§D/§E） |
| D | `CommandLineRunner.cs`（同上）+ VB 宿主协调器 | 预扫描/还原环 + R6 诊断 | 宿主驱动环（§D） |
| E | VB 宿主协调器 + 临时工程生成器 | key 缓存 + restore | 缓存/临时工程（§E） |
| G | `Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`（+`InteractiveAssemblyLoader.cs` internal seam） | native probe seam | net10 native（§G） |
| H | `Scripting\VisualBasicTest\NoNuGetZeroRegressionTests.vb` | 新增 | 零回归一（§H） |
| I | `InternalDevDocs\proposals\proposal-vbscript-lsp.md`、`proposal-distribute-…md` | 加勘误指针（正文冻结） | U7（§I） |
| Z | — | 全量验证 | 收口 gate（§Z） |

> 共享层（`Scripting\Core`、`Compilers\Core\Portable\ReferenceManager`）每一处改动都必须同步登记 `InternalDevDocs\upstream-merge.md`（§A1 2.9 类别 + §C/§D/§G 的新 seam/文件条目）。

---

## A. 共享 Core N>1 收口（R4/U9，已实证）

### A1. 工作树核对 + 文档标注 + 账本

**现状（已核实，工作树 = U9 补丁应用态）**：`Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs`

- `:802-843` `GetCompilationReferences` 的 directive 循环：`:820` `var boundReferences = ResolveReferenceDirective(...)`；`:821-825` `IsEmpty` → `:823` 产 `ERR_MetadataFileNotFound`（锚 `referenceDirective.Location`）并 `continue`；`:833-839` `foreach (var boundReference in boundReferences)` 逐条 `referencesBuilder.Add` + `referenceDirectiveLocationsBuilder.Add(referenceDirective.Location)`（N 引用 + N 个相同 location）；`:841-842` `localBoundReferenceDirectives.Add((FilePath, File), boundReferences[0])`（**单值 map = 首项主资产**）。
- `:845-846` external refs 追加在后；`:848-853` 上一条提交的 `ExplicitReferences` 全量追加（跨提交继承通道）。
- `:873-888` `ResolveReferenceDirective` 现返 `ImmutableArray<PortableExecutableReference>`；`:886` 调 `ResolveReference`；`:887` `IsDefaultOrEmpty ? Empty : references`（去 throw）。
- **零改动区**：`Compilation.cs:738`（`ReferenceDirectiveMap` abstract）、`CommonReferenceManager.State.cs:241`（单值 map）、`CSharpCompilation.cs:1316-1322`（`GetDirectiveReference` 从 map 取单值）、`PublicAPI.Shipped.txt:145`、`Compilers\CSharp\` 全树——均不触碰。

**改动形状（文档义务，不动机芯）**：

1. 在 `:841-842` 的注释处把「primary asset」约定写清：`boundReferences[0]` = resolver 返回数组的**首项主资产**（请求的包本身），其余项为该包的依赖闭包；`ReferenceDirectiveMap`/旧 singular API/跨指令去重都以主资产为键值。resolver 契约：**多资产 resolver 必须把主资产放索引 0**（写入 `NuGetPackageResolver` 子类 doc，§C）。
2. `:873-877` 的 doc 注释补一句：N 语义是**新增路径**（经 `ExplicitReferences` 全量继承），旧 `GetDirectiveReference` 语义不变（仍返回主资产单值）；N>1 只在含 reference directive 的脚本编译激活（休眠区），对既有 0/1 输入纯加性。
3. **语义注记（落在改动面，不 diff 其它共享文件）**：主资产/单值语义已由 item 1（`:841-842` 填位注释）与 item 2（`:873-877` doc）覆盖；**不**在 `Compilation.cs:738`/`State.cs:241` 增删任何 doc 注释（这两处与 `CSharpCompilation.cs`/`PublicAPI.*` 保持字节级零 diff）——「单值 map = 主资产、N 完整集经 `ExplicitReferences`」的防误读由改动面注释 + 账本 2.9 承载，不以牺牲零 diff 换注记。

**`InternalDevDocs\upstream-merge.md` 新增类别（2.9）**：登记「共享编译器 Core ReferenceManager：单 `#R`→N（补上游 `// TODO: implement`）」。内容含：文件锚（`Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs` 两处）、改动性质（只动 `ResolveReferenceDirective` 返数组 + 展开/对齐，不 alter 旧 API）、折抵（产品 VB-only、休眠区、可 PR 化）、**合并前义务**（读账本评估同步成本；上游若日后同做按上游形状对齐）。

**裁决规则**：
- 当 diff 检查发现工作树改动超出 `tmp/u9-change.patch` 形状（多改了别的函数/加了公共面）→ **回退到 patch 形状再继续**（不做 Z：不扩大改动面）。
- 当实现需要给 N 语义加公共 API → **不做 Z：不加**；用文档标注 + 内部通道（`ExplicitReferences`）满足；公共面需求走「未来上游同做再对齐」。

**pass 条件**：
- `git diff` 该文件 = patch 形状（两处语义改动 + 注释）；`Compilers\CSharp\`、`PublicAPI.Shipped/Unshipped.txt`、`State.cs`、`Compilation.cs` 零 diff。
- `:841-842`/`:873-877` doc 含主资产约定（Grep 可验）；单值语义注记只落 `Resolution.cs` 改动面 + 账本，不落 `Compilation.cs`/`State.cs`（零 diff 即验证）。
- `InternalDevDocs\upstream-merge.md` 出现 2.9 类别且含锚点 + 合并前评估义务。

### A2. U9 两场景改造为正式单测

**目标**：把 `tmp/u9harness`（磁盘 fixture + 控制台工程）改成 `Scripting\VisualBasicTest` 下的 xunit in-memory 测试，去掉文件写/进程依赖。

**新增文件**：`Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb`（沿用现有 xunit `[Fact]` + `Imports` 风格，参照 `ScriptTests.vb`/`InteractiveSessionTests.vb`）。

**构造（共享 helper，放同文件）**：
- 编译两个 in-memory 程序集：`PkgMain.MainType.Hello() As String`（引用 `PkgClosure.ClosureType`）与 `PkgClosure.ClosureType.Answer() As Integer`，用 `VisualBasicCompilation.Create` + 引用 `GetType(Object).Assembly` → `Emit` 到 `MemoryStream` → `MetadataReference.CreateFromStream`。**不做任何磁盘写**。
- 自定义 `MetadataReferenceResolver`（仿 `tmp\u9harness\Program.cs` 的 `MultiResolver`）：`ResolveReference` 对 `"nuget:X"` 返回主资产 + 闭包（或主资产 + 垃圾流），其余委托 `ScriptMetadataResolver.Default`。

**测试 1 — 跨提交继承**（对应 U9-i）：`#R "nuget:X"` + 提交 1 源码直用 `PkgMain.MainType` → `VisualBasicScript.Create(code1, options).ContinueWith(code2)`，提交 2 源码直用 `PkgClosure.ClosureType`（闭包无源码直用先行）。断言：提交 1 编译零错误；提交 2 编译零错误（闭包类型可见），即 N 引用经 `ExplicitReferences`（`:848-853`）跨提交继承。

**测试 2 — 位置锚定**（对应 U9-ii）：构造脚本含**两行不同位置的 `#R "nuget:…"`**，每行各展开「合法主资产 + 垃圾 MetadataReference」；另外再混一行普通 `#R`（指向 `CreateFromStream` 的合法 PE，模拟普通路径，仍走指令绑定）——验证逐指令锚定。断言：每个垃圾资产的 metadata 读失败诊断**各自锚到自己的 `#R` 行**（`Location != Location.None` 且行号 = 对应 `#R` 行），不串行、不落 `Location.None`。

**裁决规则**：
- 当垃圾流经 `CreateFromStream` 后诊断消息不含路径（流无 FilePath）→ 用诊断 Id（如 `BC31519`）/消息关键字断言 + 行号断言，不断言路径字符串。
- 当 in-memory 引用「必须本机运行时可解析」而 `ResolveMissingAssembly` 补不上（如闭包程序集引 `System.Runtime` 之外类型）→ 引用集补 `GetType(Object).Assembly` 流 + `ScriptMetadataResolver.Default` 委托即可；**不做 Z**：不把 fixture 落盘。
- U9-i 原版「闭包无源码直用」语义必须保留（闭包只在提交 2 被源码引用，不能为了好编译在提交 1 也引用）。

**pass 条件**：
- 两测试全绿；测试文件内无 `File.WriteAllBytes`/`Directory.Create`/`Process.Start`/`HttpClient`（Grep 可验）。
- 测试 2 至少覆盖两个不同行号的 nuget `#R`，各自坏资产诊断行号正确。

---

## B. 语法 fork：`TryParsePackageReference`（R3）

**文件**：`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs`
**现状（已核实）**：`:13` `internal abstract class NuGetPackageResolver`；`:15` `ReferencePrefix = "nuget:"`；`:17-19` doc「Syntax is "nuget:name[/version]"」；`:20-47` `TryParsePackageReference`：`:22` `StartsWith(prefix, StringComparison.Ordinal)`、`:24` `Split('/')`、`:29-46` 缺省版本返 `string.Empty`；`:49` `abstract ImmutableArray<string> ResolveNuGetPackage(string, string)`。

**改动形状（签名不变，调用点 `RuntimeMetadataReferenceResolver.cs:146` 零改动）**：

```
TryParsePackageReference(reference, out name, out version):
1. t = reference.Trim(' ', '\t', '"')            // 整体去首尾空白/引号
2. If Not t.StartsWith(ReferencePrefix, OrdinalIgnoreCase) Then Return False
3. body = t.Substring(ReferencePrefix.Length)     // 去掉前缀（保留其后实际大小写）
4. 按第一个 ',' 拆两段（limit 2）：seg0, seg1
5. name = seg0.Trim(' ', '\t'); name 非空才继续，否则 False（近失配：无包名）
6. version = (seg1 IsNot Nothing) ? seg1.Trim(' ', '\t') : String.Empty   // 可省（parse 层）
7. Return True
```

- **有意偏离上游**：`Ordinal` → `OrdinalIgnoreCase`（前缀大小写不敏感）；斜杠 `/` → 逗号 `,`；段 Trim。文件 doc（`:17-19` 处）改写并显式标注：**「本 fork 有意偏离上游：`Ordinal` → `OrdinalIgnoreCase` + 逗号拆分 + 段 Trim；理由：VB 标识符大小写不敏感（指令内容先例 `vblang\meetings\2014\LDM-2014-04-16.md` 的 `#Disable Warning` id），逗号对齐 .NET Interactive/csx。上游若改回斜杠/仅小写属误回滚。」**
- **近失配在 parse 层返 False**（`nugt:`、`nuget ：`、`nuget:` 无包名、`nuget:, 1.0`），由宿主预扫描（§D）给显式诊断；parse 层不做模糊匹配。

**测试（新增，本 fork 无上游 `NuGetPackageResolverTests`）**：`Scripting\VisualBasicTest\NuGetPackageResolverTests.vb`。`TryParsePackageReference` 是 `internal`，`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:68` 已授 IVT 给 `Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests` → 测试工程可直接调。锁定用例：
1. 逗号拆分：`"nuget:Newtonsoft.Json, 13.0.3"` → (`Newtonsoft.Json`, `13.0.3`)。
2. 大小写不敏感前缀：`"NuGet:Newtonsoft.Json, 13.0.3"`、`"NUGET:…"` → True；name/version 值不受前缀大小写影响。
3. 各段 Trim：`"nuget: Newtonsoft.Json , 13.0.3 "` → 名/版各去空白得 (`Newtonsoft.Json`, `13.0.3`)（U7 前向兼容用例：`#R "nuget: Package, Version"`）。
4. 近失配拒绝：`"nugt:X, 1.0"`、`"nuget ：X"`、`"nuget:"`、`"nuget:, 1.0"`、`"nuget: , 1.0"` 全 False。
5. 省略版本：`"nuget:Newtonsoft.Json"` → (`Newtonsoft.Json`, `String.Empty`)。

**裁决规则**：
- 当输入 `reference` 非 string 起始含引号（防御）→ Trim 已含 `"`，无需其它特殊处理。
- 当包名段含逗号（NuGet id 不允许逗号）→ 首个逗号即分隔，多余逗号留在版本段 → 版本解析失败在 restore 层报（§E），不做 Z：不写「合并逗号」逻辑。

**pass 条件**：五类用例测试全绿；`RuntimeMetadataReferenceResolver.cs:146` 调用点零改动；doc 含「有意偏离上游」标注。

---

## C. resolver 注入 seam（U1-B）

### C1. 共享层注入位

**文件**：`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs`
- `:51-63` `CreateCurrentPlatformResolver`（现 `packageResolver: null` 在 `:59`）→ 加 `NuGetPackageResolver? packageResolver = null` 可选参，透传给内部 ctor（`:65-78` 本已接受 `packageResolver`，`:80-95` ctor 已存 `:38` `PackageResolver` 字段，nuget 分支 `:144-154` 已就绪）。
- **纯加性**：默认 null → 现有调用（`:59` 语义）零变化。

**文件**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`
- `:190-200` `GetMetadataReferenceResolver(CommandLineArguments, TouchedFileLogger)` → 加 `NuGetPackageResolver? packageResolver = null` 可选参，透传 `CreateCurrentPlatformResolver`。
- `:161-188` `GetScriptOptions`（static）→ 加 `NuGetPackageResolver? packageResolver = null` 透传给 `GetMetadataReferenceResolver`；`:179` `metadataResolver:` 用该 resolver。
- `:31-42` ctor → 加可选字段 `_packageResolver`（默认 null）+ 可选 ctor 参；实例方法调 `GetScriptOptions`/`GetMetadataReferenceResolver` 时传 `_packageResolver`（`:134` 调用点）。
- **编译路径不注入（U6-A）**：`Interactive\vbi\Vbi.Compile.vb:165-167` `VbiCompiler.GetCommandLineMetadataReferenceResolver` 调 `CommandLineRunner.GetMetadataReferenceResolver(Arguments, loggerOpt)` —— 该调用不传 packageResolver（默认 null）→ 编译模式不点亮。**不做 Z**：不在该覆写里取会话。

### C2. 宿主（VB 脚本宿主层）具体子类 + 会话

**归属**：`Scripting\VisualBasic\`（程序集 `Microsoft.CodeAnalysis.VisualBasic.Scripting`，经 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:55` IVT 可见 Core internal 的 `NuGetPackageResolver` 抽象类）。具体子类/会话**不**进共享 Core（U1-A 否决）。

**新增文件（建议）**：
- `Scripting\VisualBasic\Hosting\NuGetPackageSession.vb` — 会话：当前已还原包集合（exit-0 集）、compile/runtime 路径缓存、native 根目录清单、宿主镜像元组；`Friend NotInheritable Class NuGetPackageSession`。
- `Scripting\VisualBasic\Hosting\NuGetPackageResolverImpl.vb` — `Friend NotInheritable Class NuGetPackageResolverImpl Inherits NuGetPackageResolver`：`ResolveNuGetPackage(name, version)` **同步只读会话**：key 命中且 version 非空 → 返 compile 路径数组；未命中/version 空 → 返 `ImmutableArray<string>.Empty`。doc 标注「多资产返数组，索引 0 = 主资产」（§A1 契约）。
- `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb` — 实现 §D 协调 seam + §E restore 驱动（spawn `dotnet restore`、读 assets、写会话、LRU）。装配入口：`VisualBasicScript.RunInteractiveAsync`（`Scripting\VisualBasic\VisualBasicScript.vb:150-170`）构造会话 → `CommandLineRunner`（`:163`）时经新 ctor 可选参传入 resolver + coordinator。

**裁决规则**：
- 当宿主需要新的 session 状态（如 net48 native 标记）→ 加会话字段，**不做 Z**：不往共享 resolver/runner 塞状态。
- 当 `Scripting\VisualBasic` 需调 vbi 专有类型（如编译模式判定）→ 不做（本会话与编译模式无关，U6-A）。
- **host 能力（TFM/net48）必须可注入**：§D net48 分支与 §E key 的宿主镜像需在 net10 单测可达。会话/协调器 ctor 收 `hostCapability`（枚举或 TFM 字符串，默认 = 运行时自检），测试注入 net48 → 单测直接覆盖 §D net48 诊断行与 net48 key 分支，无需真实 net48 宿主进程。**不做 Z**：不靠读当前进程 TFM 隐式判定而不可注入。

**pass 条件**：`CreateCurrentPlatformResolver`/`GetMetadataReferenceResolver`/`GetScriptOptions` 加了默认 null 的可选参后，现有调用方零改动且行为不变；编译路径工厂不注入；具体子类 `Friend`、只读会话、`Empty` 未命中；装配在 `VisualBasicScript.RunInteractiveAsync` 完成且默认（无 nuget）路径不 new 额外会话进程；`hostCapability` 可注入（ctor 参或 `Friend` 可写字段）且有 net48 注入用例覆盖 §D/§E net48 分支。

---

## D. 宿主驱动环 + 诊断锚定（R6）

### D1. 协调 seam（共享 runner 的最小介入）

`CommandLineRunner` 是 `internal sealed`（`:24`），且该 fork 已有本地改动（上游合并账本 §2.5/§2.6）。宿主无法子类化 → **共享 runner 加可空协调 seam**（internal，默认 null → 零行为变化）。**定形（本设计裁决，不留「实现时二选一」）**：在 `Scripting\Core\Hosting\CommandLine\` 定义 `internal interface INuGetRestoreCoordinator`，`CommandLineRunner` ctor 加 `INuGetRestoreCoordinator? = null` 可选参；VB 宿主实现该接口（`Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb`），经既有 IVT 可见。选接口不选 `Func`：三调用点 + 会话/loader 多方法承载，接口可被测替身注入（测试工程经 IVT 实现 fake），`Func` 单委托承载不了「restore 前预扫描 + 编译后 loader 根下推」两步。规则如下：

- seam 在**编译前**被调用（`Task<ImmutableArray<Diagnostic>>`，空 = 放行），入参 = 即将编译的 `SourceText code` + 文件路径（REPL 提交或脚本文件）。
- seam 内部（VB 宿主实现）：预扫描 → 包集合 delta → restore（§E）→ 写会话 → 返回（空或锚定诊断）。编译侧不做任何 nuget 语义判断。

**三个调用点（`CommandLineRunner.cs`，均已核实行号）**：

| 调用点 | 位置 | 时机 |
|---|---|---|
| 文件脚本路径 | `:118-128` 读码后、`:157` `RunScriptAsync` 前（`GetScriptOptions` 于 `:134` 早建 resolver 无妨——resolver 惰性读会话，见下） | 编译前 |
| REPL 初提交 | `:247-251` 初脚本 `BuildAndRunAsync` 前 | 编译前 |
| REPL 每提交 | `:290-308` 读完输入/`IsHelpCommand`（`:292-296`）之后、创建 `newScript` 前 | 编译前 |

**关键不变量**：resolver 在 `GetScriptOptions`（`:134`）时建好、持有会话引用；restore 在编译前写入会话；`ResolveReference` 在 `Compile` 时**惰性读会话** → 先后次序安全（先建 resolver 后 restore 也可，只要 restore 在 Compile 前）。REPL 的 resolver 跨提交经 `UpdateOptions`（`:338-358` 的 `WithRelativePathResolver` 拷贝）保留 `PackageResolver`（`:80-95` ctor 链）→ 会话持续。**loader 持久**：`Script.CreateInitialScript`（`Script.cs:54-56`）首次建 `InteractiveAssemblyLoader`，`ContinueWith`（`Script.cs:114-127`）复用 `Builder` → 同一 loader；coordinator 的 loader 经 seam 传入首次 Create（§G）。

### D2. 宿主预扫描 + 领域诊断（R6 决策表）

宿主协调器把「restore/会话错误」与「语法层近失配」映射为**锚 `#R` 行的领域诊断**。预扫描 = 解析提交 → 遍历 `ReferenceDirectiveTriviaSyntax`（VB 产自 `Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472`，值 = directive 的 File 文本）→ 对每个 `#R` 串跑同一 `TryParsePackageReference`（§B）分类。

**决策表（当 X 发生 → 做 Y，不做 Z）**：

| 条件 X | 做 Y | 不做 Z |
|---|---|---|
| parse True 且 version 非空 | 计入包集合；restore 后正常编译 | 不跳过 |
| parse True 且 version 空（U4-A） | 锚 `#R` 行报「请指定版本（如 13.0.3）」，该指令不进集合；其余合法指令照常 | 不隐式取最新版；不进集合 |
| 串 Trim 后 `StartsWith("nuget", OrdinalIgnoreCase)` 但非 `"nuget:"`（如 `nuget ：X`） | 锚 `#R` 行报「前缀格式错误：应为 `nuget:`（大小写不限），`nuget` 后须紧跟冒号」 | 不当作文件路径放行 |
| Trim 后 `StartsWith("nuget:", OrdinalIgnoreCase)` 但 parse False（`nuget:  ` 空名/`nuget:,`） | 锚 `#R` 行报「`nuget:` 后缺少包名」 | 不当作文件路径 |
| 首冒号前字母串（长度 ≤7，大小写不敏感）与 `"nuget"` 编辑距离 ≤1 或互为前缀（如 `nugt:`、`nugett:`） | 锚 `#R` 行报「前缀疑似拼错：应为 `nuget:`」 | 不静默当文件路径 |
| 其余 parse False（普通路径/TPA/GAC 裸名） | 交给 resolver 通用路径（命中即正常，未命中 → 通用 `ERR_MetadataFileNotFound` 锚 `#R` 行） | 不额外诊断 |
| restore 退出码 ≠ 0 / NU 错误 / 网络失败 | 锚相关 `#R` 行报转译诊断（§E 转译表） | 不把原始 NU 堆栈整段贴出；不以「找不到元数据」掩盖 |
| SDK 缺失 / global.json 无匹配 | 锚 `#R` 行报「需要 .NET SDK（可跨主版本，见 §E）」 | 不在无 nuget 时查 SDK |
| 包名命中宿主平台包 denylist（如 `Microsoft.WindowsAppSDK`；WinUI 属宿主分发自管，见 §G2.6） | 锚 `#R` 行报**范围外诊断**「此包由宿主自管，不经 nuget restore——请用 `#R "Microsoft.WinUI"` + WinUI 宿主」 | 不进集合；不 restore；不当普通包 |
| net48 宿主 + 还原产物含 native 资产（§G，sqlite 类） | 锚 `#R` 行报宿主能力诊断「此包含原生资产；net48 宿主不支持 native 探测，请在 .NET (net10) 宿主运行」 | 不报 restore 错误；不尝试 native |

诊断位置来源：预扫描时持有各 `#R` 的 `Location`（directive 语法位置）→ `Diagnostic.Create(descriptor, location, …)`。消息走既有资源/新资源通道：**优先复用**既有诊断文案形状；确需新文案走本 fork 既有资源增补流程（VB 编译器资源 + xlf 同步纪律，见 memory `xlf-localization-must-follow-upstream`）。**v1 最小面**：若新码文案成本高，可用「宿主侧本地化字符串 + 直接输出」（交互宿主本就打印诊断文本）——由实现者选，验证者核对不引入未同步 xlf 的编译器资源。

**U6-A 显式关闭**：编译模式（`.vb` 批编译 / `/r:nuget:`）不创建会话、不注入 resolver、不预扫描、不 restore。锁定方式：`VbiCompileMode.IsCompileInvocation`（`Vbi.Compile.vb:32-59`）命中即走 `VbiCompileMode.Run`（`:61-83`），不经 `VisualBasicScript.RunInteractiveAsync` → 会话/协调器根本未装配。加一个编译模式负测试断言 `/r:nuget:X` 不被解释（见 §D pass）。

**pass 条件**：
- seam 三调用点全部接入且默认 null 时 runner 行为与现在逐字节一致（由 §H 零回归一背书）。
- 决策表每行有对应测试/验收：版本缺失、前缀格式、空包名、疑似拼错、宿主平台包范围外、SDK 缺失、NU 转译各至少一例，断言诊断锚 `#R` 行（`Location.GetLineSpan` 行号 = 该 `#R` 行）。net48 native 能力行经 **hostCapability 注入**（§C2，net10 单测注入 net48 即达）+ fake runner 产出 native 资产到达；NU/SDK/网络经 **fake runner** 返固定退出码到达；宿主平台包范围外行喂 `nuget:Microsoft.WindowsAppSDK` 断言报范围外诊断且不进集合不 restore——均无真实 restore/进程。
- U6-A 负测试：`/r:nuget:…` 编译不触发还原、不报 nuget 专属诊断。

---

## E. 会话临时工程 + 缓存（R2/U2/U3/U4/U8 + E7）

> **E7 术语**（本文件首次出现，定义出处 `proposal-vbi-nuget-reference.md:64`）：现代 NuGet（7.9/SDK 10.0.4xx）no-op restore 会**校验包文件在盘**（`VerifyRestoreOutput`/`Sha512Exists`），清全局缓存后自动重下、不静默漏检——vbi 不自担资产存在校验。§E 依赖该事实实现「跨进程总调 + no-op 廉价」。

### E1. 缓存 key（决策表）

```
key = hash(
  精确包集合：每包 (id 按 OrdinalIgnoreCase 归一化, version) 规范排序后序列化，
  宿主镜像元组：TFM + windows 平台版本 + RID + 解析后 SDK 版本 + 还原源指纹 + 框架引用镜像，
  net48 标志（决定临时工程是否带 Microsoft.NETFramework.ReferenceAssemblies）)
```

| 成分 | 来源（裁决） |
|---|---|
| TFM + windows 平台版本 | **宿主供参**：读运行中 vbi 程序集 `TargetFrameworkAttribute` + 每宿主常量表兜底（U8-A）；不读 `.vbx` 头部声明注释（该注释由外层 launcher 派发进程，引擎不读） |
| RID | 当前进程 RID（`RuntimeInformation.RuntimeIdentifier`；restore 时由 NuGet 按图选，key 记宿主 RID） |
| 解析后 SDK 版本 | `dotnet --list-sdks` 选版本 + `global.json` `rollForward` 结算（见 E3） |
| 还原源指纹 | 用户级/机器级 `nuget.config` 源集合的稳定指纹 |
| 框架引用镜像 | 需要镜像的 FrameworkReference（如 WindowsDesktop/WinUI）清单 |
| 浮动版本（U3-B） | key **含请求 spec**（如 `13.0.*`），不写回具体版、不换 key；dgspec 不变则 NuGet 冻结上次解析 |
| 脚本源码 | **不进 key**（restore 是包集合的纯函数） |
| 共享 | 仅精确相等集合共享；子集/超集不共享（同包在不同图压到不同版本，不拿大图产物喂小图） |

### E2. 缓存目录 + 字节稳定 + 清理

- 根目录：`%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>\`（跨会话常驻）。
- 临时工程文件（`.vbproj` + `global.json` + 必要 props）**按 key 写一次**；已存在则不改写 → dgspec 字节稳定 → NuGet no-op（E7）才成立。工程内容只依赖 key 成分，不含时间/机器指纹。
- `net48` 临时工程须含 `<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" …/>`（先例 `Interactive\vbi\vbi.vbproj:55-59`）。
- **LRU 清理裁决**：每次成功 restore 后顺带清理。常量默认：目录数 > 256 → 按 `Directory.GetLastWriteTimeUtc` 删最旧直到 ≤ 128；单个 key 目录近 30 天未用且非本次会话 → 可删。绝不删「本次 restore 正在写」的 key（restore 是短进程、写完即释放，进程内不做并发协调；跨进程删除竞争由「下次 restore 重生成 + NuGet 自愈」兜底）。
- 工程写盘（`%LOCALAPPDATA%`）属宿主 restore 集成行为，**只在本特性触发时发生**；无 nuget 引用不建任何目录。

### E3. SDK 门控 + restore 触发策略

- **SDK 门控**：只在出现 nuget 引用时查 `dotnet --list-sdks`；临时工程写 `global.json` = `{"sdk":{"version":"<当前 SDK>","rollForward":"latestMajor"}}`（允许跨主版本升）；无匹配 → §D 决策表「SDK 缺失」锚 `#R` 行。无 nuget 引用全链路不启用。
- **restore 触发**（U2-A）：

| 情况 | 行为 |
|---|---|
| 本进程内包集合与上次 exit-0 相同 | skip，零调用 |
| 集合变化（新增/改版） | 必调（非 NuGet 不可算；顺带自愈上次坏档） |
| 跨进程重跑同 key | v1 总调（NuGet no-op 廉价重验包在盘，E7） |

- **唯一有效信号** = 自己那次 restore 的**退出码 0** + NuGet 自身 `obj/project.nuget.cache`。vbi **不做资产状态判断**：不校验 assets.json、不探包存在。
- **转译表**（restore 失败 → §D 诊断）：`NU1101`（找不到包）→ 锚 `#R` 行「找不到包 `<name> <version>`（还原源已含当前源指纹），请核对包名/版本」；网络失败/超时 → 「无法连接 NuGet 源（<源>），请检查网络或离线源」；其余 NU/SDK 错误 → 取首行错误摘要锚 `#R` 行，附 exit code，不整段贴 stderr。

**restore runner 可注入 seam（本设计裁决，防「无法单测」）**：
- 协调器**不直接 spawn `dotnet`**；内部经一个 `RestoreRunner` 抽象（`Friend` delegate `Function(request As RestoreRequest) As RestoreOutcome` 或等价的内部接口）执行。生产实现 spawn `dotnet restore`；测试注入 fake runner（返固定退出码 + 假 `obj/project.nuget.cache`/`project.assets.json`）。
- **决策与转译做纯函数**：`ShouldRestore(prevExit0Set, currentSet)`、`ExitCodeToDiagnostic(exitCode, stderr, #R location)`、`ReadAssets(jsonText)` 均无副作用、可脱离 runner 单测；runner 只负责「跑一次 restore 并回吐原始结果」。
- 注入发生在装配点（§C2 `VisualBasicScript.RunInteractiveAsync` 旁路）；测试可绕过装配直接构造协调器 + fake runner。
- **不做 Z**：不把 fake 逻辑藏进生产 spawn 分支（用 if 判测试态）；也不为了可测把 assets 解析塞进 runner。

### E4. 资产读取

restore exit-0 后读 `<key>\obj\project.assets.json`：`targets` 下按「TFM 精确匹配宿主镜像」取 compile 资产路径（喂 resolver 会话）+ runtime 资产（managed 注册）+ `runtimes/<rid>/native` 目录去重集（喂 §G native 根）。compile 段取**全部依赖闭包**（不是只主包）——这正是 R4「单 `#R` 自含全图」的资产来源；resolver 子类把主资产放索引 0、闭包随后。

> **实现注记（F-A/F-B 后，2026-09-07）**：`NuGetRestoreAssetsReader.ReadAssets(assetsJsonText, host.ShortTargetFramework, rid, validRequests)` 实际以**短 TFM 单体键**（`net10.0`/`net48`，RID-specific target 形如 `net10.0/win-x64`）寻址 `targets` 节——SDK 写出的 `targets` 键是短形，非长形 `.NETCoreApp,Version=v10.0`；`NuGetPackageSession.FrameworkNameForRestore` 只作缓存 key 成分与 net48 ReferenceAssemblies 映射，**不再命名 assets target**。真实 assets 形状三项实测修正（P-001，单测 golden 已镜像 SDK 输出）：`packageFolders` 为**对象形**（键 = 包目录，值 `{}`），非数组（数组形仅作手写输入回退）；`dependencies` 键为**裸包名**（无版本），收集闭包时须先经库表 `FindResolvedIdentity` 还原到 `name/version` 恒等再递归；RID-specific target 把已选 RID 的 native 文件放包条目 **`native` 节**（非 `runtimeTargets`，后者只在 plain target 全 RID 出现），`GetNativeDirectories` 现依次扫 `native`→`runtimeTargets`→`runtime`。

**裁决规则**：
- 当 assets 的 compile 路径含 `ref/` 与 `lib/` 双份 → 取 NuGet 已选中的 compile 目标（`ref/<tfm>` 优先，同 `dotnet` 语义）；运行时注册用 runtime 目标（缺失则 compile lib）。
- 当集合变化但 restore 在无 SDK 机器 → §D「SDK 缺失」；**不做 Z**：不尝试进程内替代还原（C2/增量自解析均否决）。

**pass 条件**：同 key 二次运行不重写工程文件（字节不变，Grep/哈希可验）；「本进程同集 skip / 集合变必调 / 跨进程总调」各有**纯函数 `ShouldRestore` 单测**（feed 不同集合 → 断言调用决策）；NU1101/网络/SDK 缺失各产锚 `#R` 行转译诊断（`ExitCodeToDiagnostic` 纯函数喂假 stderr）；net48 key 产物含 ReferenceAssemblies 引用（临时工程生成器喂 hostCapability=net48 断言含该包）。以上全部无进程 spawn。

---

## F. 语法语义契约（U3-B / U4-A / R3，行为验收）

| 契约 | 规则 | 落点 / pass |
|---|---|---|
| 逗号 | `#R "nuget:name[, version]"`；首个逗号拆两段各 Trim | §B parse 测试；`.vbx` demo 用逗号 |
| 大小写不敏感前缀 | `NuGet:`/`NUGET:` 合法；包名/版本保留原大小写 | §B 测试 2 |
| v1 版本必填（U4-A） | 缺省报「请指定版本」锚 `#R` 行；不隐式「最新解析」 | §D 决策表 + 测试 |
| 浮动版本（U3-B） | `*`/`13.0.*` 是合法 spec；key 含请求 spec、不写回具体版；dgspec 不变 NuGet 冻结；要完全确定可 pin | §E1 key 表 + §E3；.vbx demo 用 pin 版本为主 |
| 近失配 | 只消「大小写」一类失配；近失配（`nugt:`/`nuget ：`/无包名）显式诊断 | §B 测试 4 + §D 决策表 |
| 不引第二种做法 | 不加 `/r:nuget:`（编译模式）外的第二种前缀写法；产品文档明示与官方 file-based `#:package id@version` 的差异 | §D U6-A + 文档 |

**pass 条件**：
- 语法/语义行由 §B 解析测试 + §D 决策表测试锁定（自动可判）。
- `.vbx` demo 行（逗号写法、pin 版本）落 **V-G2 sqlite 验收的 `.vbx` sample**（同一脚本，随 §G3 门控跑），不另开 demo 产物；该 sample 用逗号 + pin 版本。
- 「与官方 `@`/`#:` 语法差异」的**产品文档明示**文本由 **V-I**（文档批）随勘误同批产出（草案写入 §I1 勘误指针批注，同文本在 V-G2 `.vbx` sample 注释复用，README V-I pass 复核）——§F 不自行产出独立产品文档文件。

> **实现注记（V-G2 续跑收口）**：V-G2 sqlite 验收脚本（`scripts/g2-1-sqlite.vbx`，注释已含上方 §F 明示文本）在 F-B 真跑通过后晋升 `Samples/SqliteNuGetDemo.vbx` 作为唯一 demo 产物（不另开第二份），满足「同一脚本不另开 demo 产物」；注释语言按 Samples 产品惯例用英文，同文本语义见 §I1。§F 明示文本中文口径随 V-I 勘误指针（已 done）。

---

## G. 运行时注册 + net10 native（U5）

### G1. 托管运行时注册（确认覆盖 N）

**现状（已核实）**：`Scripting\Core\ScriptBuilder.cs:142-151` 编译后对 `compilation.GetBoundReferenceManager().GetReferencedAssemblies()` **逐条** `RegisterDependency(identity, path)`（path 非 null）。Core-N 下 N 引用全在 bound refs 内 → 该循环天然覆盖 N。**预期零代码改动**；若实测某闭包路径缺注册（其 PE 引用的 FilePath 为 null 等），在会话 resolver 保证 compile 路径都带 FilePath。

**pass**：多资产包运行期闭包类型可解析（V-G2 sqlite 端到端隐含验证 managed 闭包）。

> **实现注记（F-A/F-B 后）**：实测发现 ScriptBuilder 循环只注册**编译期 bound**（脚本直引）程序集；整 app 型脚本（Avalonia）运行期才触达的闭包程序集（基类链 / 惰性依赖）不在其内 → **并非零改动**。落实施：`InteractiveAssemblyLoader` 加 internal `RegisterRuntimePathOverride(compile, runtime)`（`RegisterDependency` 锁内查覆盖表重定向 compile→runtime，修复对齐 `ScriptBuilder.cs:147-148` 上游 TODO「Contract vs RT」）+ internal 观察位 `GetRegisteredDependencyLocations`/`NativeProbeRoots`；`NuGetRestoreAssetsReader.ComputeRuntimePathOverrides`（纯函数，同名唯一且路径不同才覆盖、lib-only 不覆盖、歧义跳过）产 compile(ref)→runtime(lib) 覆盖表；协调器 `PushSessionAssetsToLoader` 把 native 根 + 覆盖表下推 loader，并 `RegisterRuntimeClosure` 把 restore 的整个 runtime(lib) 闭包经 `AssemblyName.GetAssemblyName` + `AssemblyIdentity.TryParseDisplayName` 逐条 `RegisterDependency` 注册（`File.Exists` 门控，单测虚路径跳过）。覆盖由 `NuGetRuntimeHandshakeTests` + V-G2 真实还原端到端锁定（279 全量绿）。

### G2. net10 native loader seam（机制在 loader，目录策略在宿主）

**现状（已核实）**：`Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`：`:15` `internal sealed class CoreAssemblyLoaderImpl`；`:22` in-memory `LoadContext`；`:35` per-path `LoadContext`；`:45-75` 私有 `LoadContext : AssemblyLoadContext`：`:70-71` 只挂 `Resolving +=`（托管），**无 unmanaged 钩子**。`InteractiveAssemblyLoader.cs:29` `public sealed partial`，`:168` `RegisterDependency(AssemblyIdentity, string)`。net48 走 `DesktopAssemblyLoaderImpl.cs:14`（无 ALC，无 `NativeLibrary`）。`Scripting\` 树 Grep `ResolvingUnmanagedDll|NativeLibrary` 零命中。

**改动形状**：
1. `InteractiveAssemblyLoader.cs`（public sealed）加 **internal** seam：`internal void AddNativeProbeRoot(string directory)` / 或 ctor 变体（由协调器在首次 Create 前/运行中调用，把根集下推到 `AssemblyLoaderImpl`）。**不新增 public 面**（IVT 已覆盖 vbi/VB.Scripting）→ 不触碰 `PublicAPI.Unshipped.txt`。若实现者判断确需 public，须走 PublicAPI.Unshipped 增补并登记账本（**默认 internal**）。
2. `CoreAssemblyLoaderImpl.cs`：持根集；每个 `LoadContext`（`:22` 与 `:35` 两构造点）接收根集引用；LoadContext 加 unmanaged seam——推荐 override `LoadUnmanagedDll(string)`：对每个根目录按「原名 → 原名+平台扩展名」探测，命中 `LoadUnmanagedDllFromPath`，未命中 `base.LoadUnmanagedDll(name)`（等价可改用 `ResolvingUnmanagedDll` 事件，二选一，行为须一致）。**空根集 → 与现状逐字节一致**。
3. **平台 dll 命名/搜索序**（跨平台）：候选名 = `name`、`name + ext`、`lib` + name + ext（ext = `.dll`/`.so`/`.dylib` 按当前平台）；每个根目录按上述序试。RID 回退：宿主给的是 restore 已选 `runtimes/<rid>/native` 目录（NuGet 已做 RID 回退），loader 不做 RID 回退逻辑。
4. **缺目录诊断**：会话读 assets 时若包含 native 资产但对应 `runtimes/<rid>/native` 目录不存在 → 在还原后诊断里报「包 <name> 缺少当前 RID（<rid>）的 native 资产」，带已检查的 RID 候选列表（RID 回退属 restore 侧，诊断只是清晰化）。
5. **net48**：不实现 native；还原产物含 native 资产 → §D 决策表「net48 宿主能力诊断」（限定：只对 restore 拉回、探测根可解的 native 资产成立）。
6. **WindowsAppSDK/WinUI 类**：**本通道范围外（宿主分发自管，非「后置」项）**——WinUI 随宿主构建期分发（`Installer\vbicore\vbicore.vbproj:28-40` `UseWinUI` + `PackageReference Microsoft.WindowsAppSDK`；`Interactive\vbi\Vbi.vb:38-45/:75-81` `Application.Start` 引导），脚本裸名 `#R "Microsoft.WinUI"` 走宿主 TPA，不经 nuget restore。本特性对该类无处理义务；误写 `#R "nuget:Microsoft.WindowsAppSDK,…"` → §D 决策表**范围外诊断**指去宿主自带 WinUI。不做 Z：不为 WindowsAppSDK 加专用逻辑。

**分工**：目录清单（策略）= 宿主（从 assets runtime 闭包取 `runtimes/<rid>/native` 去重集，经协调器在编译后运行前调用 `AddNativeProbeRoot`）；unmanaged 解析钩子（机制）= Scripting loader（`CoreAssemblyLoaderImpl`）。宿主拿不到 ALC（私有嵌套）、看不到包 `Assembly` 实例 → 纯宿主挂 resolver 不成立，机制必须在 loader。

> **实现注记（F-A 装配，2026-09-07）**：loader 共享落地为 `CommandLineRunner` ctor 可选参 `InteractiveAssemblyLoader assemblyLoader = null`（默认 null → 维持 `CreateInitialScript` 自建 loader 的现状），三次 `CreateInitialScript`（文件脚本 / REPL 初提交 / REPL 首提交）经 `assemblyLoaderOpt:` 用该实例 → 跨提交同一 loader；`VisualBasicScript.RunInteractiveAsync` 建一个共享 loader 同时传 runner 与 coordinator（`loader:=`）。宿主 push 在协调器 `PushSessionAssetsToLoader`（restore 成功、运行前）一次性完成：native 根 `AddNativeProbeRoot` + 覆盖表 `RegisterRuntimePathOverride` + runtime(lib) 闭包 `RegisterRuntimeClosure`（见 §G1 注记）。

**pass 条件**：空 native 根集下 loader 行为与现状一致（现有 Scripting 测试全绿）；`AddNativeProbeRoot` 为 internal 且不触 `PublicAPI.*.txt`；跨平台候选名探测表有单测（纯函数，输入根目录集 + name → 候选路径，不实际加载）；net48 能力诊断在 §D 测试覆盖。

### G3. sqlite + 托管传递依赖端到端验收（V-G2，作者/QA 门控集成，V-Z 收口后执行）

net10 宿主 `.vbx`：`#R "nuget:Microsoft.Data.Sqlite, 8.0.x"` → `Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:")` 开库建表插值 → 一次 `SELECT` 返回值正确。同时验证：native `e_sqlite3` 经 loader 探测根找到（不带根集会 `DllNotFoundException`——阴性对照）；跨平台命名/RID 回退/缺目录诊断按 §G2.3-4 清单人工过；该 `.vbx` sample 注释随附 §F「与官方 `#:`/`@` 语法差异」产品文档明示文本（V-I 产出）。

**托管传递依赖真实还原（G2-5，补间接依赖缺口）**：sqlite 的传递依赖偏 native，未覆盖「restore 闭包 → 托管 N>1 引用集」真实链；S1 只在 in-memory 证 Core-N 机制。故另引**带托管传递依赖的包**（非 Newtonsoft 单 dll 无依赖；具体选有托管依赖的稳定 pin 版，如 `Microsoft.Extensions.*` 一族），脚本直用主包类型 + 其依赖库类型各至少一处，断言还原闭包 N>1、两类型均零编译错且运行正确。具体包/版本在验收时记录于 test-plan.md §7。

此验收允许真实 `dotnet restore` + `%LOCALAPPDATA%` 写（非无副作用单测范围）。**不在无人值守 Vortex 表内**（README「门控集成验收」节登记，V-Z 收口后由作者/QA 执行）。

> **实现注记（V-G2 续跑收口，2026-09-07）**：本验收已由 Vortex F-B 轮在 net10 宿主真跑执行并关闭（独立复核记录 `tmp/vortex-logs/vbi-nuget-runtime-handshake/5-verifier-fb-gated.md`）：G2-1 sqlite 内存库 e2e `sqlite-ok:forty-two` EXIT 0（无 DllNotFound/TypeLoad）；G2-2 阴性对照不带根集 → `DllNotFoundException` EXIT 36；G2-5 托管传递依赖闭包用 `Microsoft.Extensions.Caching.Memory, 8.0.1`（lib-only，闭包 6 程序集）主包 + 依赖类型各直用零错；补 F-A ref-split 冒烟（单 `#R` `Avalonia.Desktop, 12.1.1` 直用 ref-split 传递依赖 `PixelPoint`/`SKColor`，无 TypeLoad → ref→lib override 真实生效）。真跑暴露三缺陷以修复 + 单测收口（见 §G1/§E4 注记；`Scripting\VisualBasicTest` 全量 279 绿）。**未物理机真证**：G2-3 跨平台命名 / RID 回退 / 缺目录诊断与 G2-4 net48 宿主真跑——由纯函数单测覆盖（`NativeLibraryProbeTests` / `NuGetMissingNativeAssetsTests` / hostCapability=net48 注入）。sqlite 演示脚本已晋升 `Samples/SqliteNuGetDemo.vbx`（原 `scripts/g2-1-sqlite.vbx`，注释含 §F 与官方 `#:`/`@` 差异明示文本 + `SQLitePCL.Batteries_V2.Init()` 显式调用原因）。

> **实现注记（F-D loader 向上版本统一，2026-09-08）**：interactive loader 的依赖解析现支持**向上版本统一**——`InteractiveAssemblyLoader.ResolveBestDefinitionIndex`（两处私有 `FindHighestVersionOrFirstMatchingIdentity` 共享，登记 `upstream-merge.md` 2.15）：请求 identity 无精确（或平台统一）候选时，允许同名 + 文化/公钥/内容类型一致且定义版本 ≥ 引用版本的**最高版本**候选满足（可升级、不降级）；有精确候选时精确优先。默认/无版本冲突路径逐字节不变，`PublicAPI.*` 零增量。跨 assembly 版本 skew 的包组合（如 `FluentAvaloniaUI 3.0.0-preview2` 引用 Avalonia 12.0.0.0 配 12.1.1 包）由此可运行；`Samples/AvaloniaCalculator.vbx` 已加回 FAUI 主题（5 包含 FluentAvaloniaUI），窗口真跑通过（net10 宿主 MainWindowTitle 可见、16s 存活、无错误输出）。用法注意：`FluentAvaloniaTheme` 构造需 Avalonia Application 上下文（ctor 内 `ResolveThemeAndInitializeSystemResources` 查 `Application.Current`），裸控制台脚本构造会 NRE，属用法约束而非版本解析问题——须在 `Application.Initialize` 内 `Styles.Add(New FluentAvaloniaTheme())`（样例即此用法）。

---

## H. 零回归（RESOLUTION 后续工作项测试一/二）

### H1. 无 nuget `.vbx` 行为逐字节不变

新增 `Scripting\VisualBasicTest\NoNuGetZeroRegressionTests.vb`：
- 取一代表性脚本（含普通 `#R` 本机/内存 dll 引用 + 打印 + 顶层语句），经内存宿主（`StringReader`/`StringWriter` 交互宿主或 `VisualBasicScript.RunAsync`）跑，断言 stdout 与 **golden 常量逐字节相等**。
- 协调器 stub：seam 对每提交被咨询（装配 coordinator 后 runner 编译前必调，见 §D1），stub 仅在提交实际请求 nuget 包时抛 → 证明无 nuget 提交：seam 被咨询但无 restore/SDK 路径尝试。
- 普通 `#R` 路径（`ScriptMetadataResolver.Default`）与共享 Core N 展开对 0/1 输入的纯加性由此背书。

### H2. 现有单结果 resolver 全量不回归

`Scripting\VisualBasicTest` 全量（`CommandLineRunnerTests`/`InteractiveSessionTests`/`ScriptTests`/`ScriptOptionsTests`/`VbiCompileModeTests`/…）绿——N>1 对既有 0/1 输入休眠（`Resolution.cs:887` 对 0/1 返原数组，路径与旧版一致）；共享 Core/runner/resolver 的新增参数默认 null/空。

**裁决规则**：若某既有测试失败 → 先判是否由新增默认参数/引用顺序引起；是 → 修正默认值形状保持兼容；否 → 按正常回归排查。**不做 Z**：不为过测试而放宽 N 契约。

**pass 条件**：H1 golden 逐字节相等 + 无 nuget 提交 seam 被咨询但无 restore/SDK 路径尝试（stub 不抛）；H2 全绿；`Scripting\Core`/`Core\Portable` 构建无新公共面（除登记的 seam）。

---

## I. 文档勘误（U7）+ supersede 核对

### I1. 两旧提案勘误指针（不改冻结正文）

| 位置 | 失实断言 | 勘误指针 |
|---|---|---|
| `InternalDevDocs\proposals\proposal-vbscript-lsp.md:121` | 「`#R "nuget: Package, Version"`——产品层已有，LSP 零新增……已实现 `nuget:` 前缀解析」 | 在该行下方追加指针行（不删改原句）：「勘误：'已实现'断言与本仓代码不符（`NuGetPackageResolver` 空转、注入 null）；本特性以 `tasks\vbi-nuget-reference\` + `meetings\meeting-vbi-nuget-reference.md` RESOLUTION 为准。」 |
| `InternalDevDocs\proposals\proposal-distribute-compiler-nuget-package-and-dotnet-tool.md:112` | 「复用 Scripting 层已有 `NuGetPackageResolver`……不重复实现」 | 同上式指针；语法 `nuget: Package, Version` 与逗号 + Trim **前向兼容**（U7 精确化），失实点在「复用已有实现」，不在拼写 |

指针文本须含：指向 `tasks\vbi-nuget-reference\` + meeting 文件名；不含本机路径/用户名。§F「与官方 file-based `#:`/`@` 语法差异」产品文档明示文本草案随勘误批注同批写入（同文本在 V-G2 `.vbx` sample 注释复用；落点与复核见 §F pass / README V-I）。落地时机 = 代码面收口后（Vortex V-I）。

**§F 产品文档明示文本（`#R "nuget:…"` 与官方 file-based `#:`/`@` 差异；V-I 产出，V-G2 `.vbx` sample 注释复用同文本）**：

- 语法：`#R "nuget:包名[, 版本]"`——前缀 `nuget:` 大小写不敏感（`NuGet:`/`NUGET:` 亦合法）；包名与版本以**逗号**分隔，各段首尾空白忽略（Trim）；解析层版本可省，但 **v1 版本必填**，缺省报「请指定版本」（锚 `#R` 行）；支持浮动版本 spec（如 `13.0.*`）。
- 与官方写法差异：官方 file-based programs 的包引用写作 `#:package id@version`（`@` 分隔、无引号前缀）。两种写法**不互通**——`.vbx`/vbi 属 Script/REPL 语义，沿用 `#R "nuget:"` 惯例，不解析 `#:`/`@`；产品文档须明示此差异，避免用户拿 file-based 心智套 `.vbx`。

### I2. 提案正文 supersede 表（实现者照此忽略被 supersede 的提案段落）

| 提案段落 | 被 supersede 为 | 实现者动作 |
|---|---|---|
| `proposal-vbi-nuget-reference.md`「接线（倾向候选 C3）」（Summary 三主张 + §3） | R4：共享 Core 单 `#R`→N（U9 已过） | 不实现 C3「源内 seam + 宿主注入」半吊子；按本设计 §A/§C/§D |
| 同提案 §1「fork 该文件测试改断言」 | 本 fork 无上游 `NuGetPackageResolverTests` → **新增测试** | 按 §B 新增 |
| 同提案 U1（建议 B）IVT 先例 `vbi.vbproj:42-43` | R7 证据锚订正：正锚 = `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58` | 按 §C |
| 同提案 U5（v1 支持 managed、原生留 spike） | R8（U5）：net10 native loader seam 支持、sqlite 类唯一验收、WindowsAppSDK/WinUI 宿主分发自管范围外（非后置）、net48 范围外 | 按 §G |
| 同提案 Drawbacks「解析改动对 fork 的 C# scripting 同文件生效」 | R9：降级为「潜在 interop 注记」（本 fork 无 C# scripting 工程） | 文档按注记口径 |
| 同提案「TFM 镜像（U8）」与「脚本声明 TFM 叠加」表述 | R2：只镜像运行宿主 TFM；不叠加脚本声明 TFM | 按 §E1 |
| `proposal-vbscript-lsp.md:121` / `distribute…:112`「已实现」 | U7-A + 精确化：前向兼容、失实在断言 | 按 §I1 |

**pass 条件**：两旧提案各出现一条勘误指针且原句未改动；supersede 表与 meeting RESOLUTION 逐项一致（验证者对照 R1–R9 复核）；提案正文未被直接改写过（Grep 确认原文仍在）。

---

## Z. 全量收口 gate（Vortex V-Z）

1. 构建：`Compilers\Core\Portable`（net10）+ `Scripting\Core`/`VisualBasic` + `Interactive\vbi` 全绿。
2. 测试：V-A2（Core-N 两测）、V-B 解析测、§D 诊断/U6-A 测（net48/NU/SDK 分支经 hostCapability 注入 + fake runner 到达）、§H1 golden、`Scripting\VisualBasicTest` 全量（V-H2）绿——直跑 `-automated`（§0 测试执行规约），不用 `dotnet test`。
3. 账本：`InternalDevDocs\upstream-merge.md` 含 ReferenceManager 类别（2.9）+ Scripting Core 新 seam/新 VB Hosting 文件登记；共享层无未登记改动。
4. Public API：`PublicAPI.*.txt` 无未登记增量（默认 internal seam → 零增量）。
5. `tmp/u9-*`：正式测试落地后，处置（保留为实证记录 / 归档）由实施者 + 验证者决定并记录到 `tmp/vortex-logs/`。
6. 无副作用纪律：无副作用单测文件不含网络/文件写/进程 spawn/注册表（Grep 抽查）。
7. 门控集成验收（V-G2 sqlite 端到端）已转交作者/QA 清单（README「门控集成验收」节），V-Z 不阻塞其上。
8. 测试计划齐备：`test-plan.md`（第 4 件交付物）在册，其 L1–L4 分层与 V-A2/V-B/§D 诊断/V-H1/V-H2 测试面一一对应（accepted 门 item 6）。
9. 无待作者决策点（归属与调度已裁决：不入 p1–p4 语法档、独立跟踪，见 `README.md`「归属与调度」）；accepted 门核对项逐条可勾。

---

> 本设计为无人值守实施/验证底稿。实施者按 `README.md` Vortex 表逐项做，验证者按各项 pass 条件核对打回。任何「实现期需人拍」的新分歧须记录进 `tmp/vortex-logs/` 并在收口时浮出，不得静默改判。
