# Visual Basic Language Design Meeting
September 6, 2026

议题是 `proposal-vbi-nuget-reference`——让 `.vbx` 脚本与交互窗口用 `#R "nuget:包名[, 版本]"` 引用 NuGet 包。触发点是 `.vbx` 的引用面只覆盖本机 dll 与运行时平台程序集（`Samples\WpfCpuCoreInformation.vbx:1-3` 即只走 TPA/本机路径），没有任何「从 NuGet 拉包」的通道；而官方 C#/VB 脚本路线已弃 Scripting、VB 又无 file-based 继任（`dotnet/sdk#55716` 的 "later"），NuGet 支持只能自己补。提案把机制主张收敛成三件事：引擎（倾向 C1，临时还原工程交 dotnet CLI）、接线（倾向把上游 2015 年遗留的空转抽象缝 `NuGetPackageResolver` 接活成状态化 seam + 宿主驱动环）、语法（fork 该解析器为逗号 + 大小写不敏感前缀）。三条主张都标了「待 LDM 审视」。

我们两条独立路径各复核了一遍——一条沿 VB 语言基因问「这设计像不像 VB」，另一条沿 C# 生态问「这设计与 .NET 工具链合不合」。方向的判断很快就收敛了：**引擎选 C1、语法用逗号，两票都没有悬念；真正要反复权衡的是接线**。沿着提案那句「resolver 是唯一咽喉」把 `#R` 的实际数据流追到共享编译器 Core 的 ReferenceManager 时，我们发现一个提案正文完全没算到的硬约束：**单条 `#R` 指令只接受恰好 1 个解析结果，多一个就直接抛 `NotSupportedException`**。这让我们先在宿主拦截和扩展共享 Core 之间来回权衡，最终把「扩展共享 Core 让单条 `#R` 解析成 N 个引用（补完上游自己认的 `// TODO: implement`）」定为目标架构，但受 spike 硬门控。此外还有两处范围裁量（TFM 镜像、native 依赖）在复核中各自收窄过一次。这是一场「方向早定、接线形态与两条边界在复核中逐步收紧」的会议。

## Agenda

* [Proposal: .vbx 脚本/交互窗口 NuGet 包引用](#proposal-vbx-脚本交互窗口-nuget-包引用)

## Proposal: .vbx 脚本/交互窗口 NuGet 包引用

_Related: [`../proposals/proposal-vbi-nuget-reference.md`](../proposals/proposal-vbi-nuget-reference.md)；勘误对象 `../proposals/proposal-vbscript-lsp.md`、`../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（U7，两处「已实现」断言失实）；`../decisions.md` M5 / D2（脚本 + 现代 .NET 双模路线的工具链对齐）；共享 Core 合并基准 `../upstream-merge.md`_

> **来源标注**：本会议引用的源码与规范文本均逐字核对（`文件:行号`），提案 E1–E6 锚点逐一复核属实。复核中发现并修正了提案的几处表述：U1-B 的 IVT 锚点方向、`Location.None` 的覆盖范围、U7 旧文档语法与本提案的前向兼容性、Drawback「fork 的 C# scripting 同文件生效」的降级、以及 TFM 镜像与 native 范围两处前提（见正文）。

### 场景与缺口

`.vbx` 脚本现在只能 `#R` 本地 dll 与运行时平台程序集。脚本要跑现代库（JSON、HTTP、UI 工具包）就必须补「从 NuGet 拉包」这条通道。官方示范的形态恰好是「临时工程 + restore」：dotnet/interactive 2026-04 弃用、点名 file-based apps 为继任（`dotnet/interactive#4163`），csx 被官方排除（`dotnet/sdk#49222`）、csi 无官方新 REPL（`dotnet/roslyn#17666`）、VB 无 file-based 继任（`dotnet/sdk#55716`，VB "later"）——五项外部事实经独立现网核验属实（2026-09，见「证据等级注记」）。VB 没有继任，所以 vbi 的 NuGet 支持必须自己补，且官方示范的「内存造工程交给 MSBuild/dotnet restore」与本提案 C1 同构。

另一条动机是修一份失实记录：`NuGetPackageResolver`（`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:13-51`）是上游遗留的抽象缝——fork 全树 grep 无任何子类、`ResolveNuGetPackage` 无实现，`RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver` 硬编码 `packageResolver: null`（`:59`）。但 `proposal-vbscript-lsp.md:121` 声称「`#R "nuget: Package, Version"`——产品层已有，LSP 零新增……已实现 `nuget:` 前缀解析」，`proposal-distribute…:112` 声称「复用 Scripting 层已有 `NuGetPackageResolver`……不重复实现」——两处都与代码不符。本提案的方向之一是把这条缝接活（或显式废弃），而不是留一个「看起来能用实则空转」的状态。

### 翻源码：E1–E6 全实，但「唯一咽喉」在共享 Core 撞墙

我们先把提案的证据纪律核了一遍：`ParseConditional.vb:454-472`（`#R` 值取 `StringLiteralToken`、仅 Script 合法、产 `ReferenceDirectiveTrivia`）、`NuGetPackageResolver.cs:13-51`（抽象缝、`:22` `StringComparison.Ordinal`、`:18` doc 注释 `Syntax is "nuget:name[/version]"`、`:29-33` 缺省版本返回空串）、`RuntimeMetadataReferenceResolver.cs:38/:51-63/:59/:144-154`（`PackageResolver` 字段、`CreateCurrentPlatformResolver` null 注入、`ResolveReference` nuget 分支命中且非 null 才解析）、`CommandLineRunner.cs:161-200`（脚本/编译共用工厂）、`Vbi.Compile.vb:165-167`（编译路径覆写复用同一工厂）、`ScriptBuilder.cs:142-151` / `InteractiveAssemblyLoader.cs:168`（运行时注册依赖）、`vbi.vbproj:7/:55-59`（多目标 + net48 ReferenceAssemblies）——全部属实。全树 grep 也确认没有任何 `Inherits NuGetPackageResolver`。到这一步，提案的「resolver 是唯一咽喉」图景是自洽的。

然后我们做了一件提案没做的事：把「咽喉」沿 `#R` 的**编译期**数据流往下追，追到 C#/VB 共享的编译器 Core。`#R` 指令在脚本里不只是给 `ScriptOptions` 的引用通道——脚本编译时它会作为 reference directive 由共享 ReferenceManager 绑定：`VisualBasicCompilation.vb:1428-1430` 从声明表取回树里的 `#R`，`Compilation.cs:733` 暴露 `ReferenceDirectives`，`CommonReferenceManager.Resolution.cs:802-836` 逐条解析。而 `ResolveReferenceDirective`（`:869-890`）对**每一条** `#R` 是这样处理的：

```csharp
var references = compilation.Options.MetadataReferenceResolver.ResolveReference(reference, basePath, ...);
if (references.IsDefaultOrEmpty) return null;
if (references.Length > 1) { // TODO: implement
    throw new NotSupportedException();
}
return references[0];
```

单条 `#R` 的解析结果数组长度**必须恰为 1**，>1 直接 `NotSupportedException`。这不是 Scripting 层，是 `Compilers\Core\Portable\ReferenceManager` 的共享代码——C#/VB 编译器都在用。而脚本编译确实会把 `script.Options.MetadataResolver` 挂进编译选项（`VisualBasicScriptCompiler.vb:213` `metadataReferenceResolver:=script.Options.MetadataResolver`），所以 `#R "nuget:…"` 一定会走这条单解析闸门。

这一下把提案 §3 的表述顶翻了。提案让具体 `NuGetPackageResolver` 子类「返回 compile 资产路径**数组**」——但 NuGet 的 compile 资产是**整个传递闭包**（`project.assets.json` 的 compile 段列全部依赖），任何带传递依赖的包、或单包里 `lib/<tfm>/` 放多个 dll 的包，compile 数组长度都 >1，编译期就会抛 `NotSupportedException`，不是可读诊断。Newtonsoft.Json 13.0.3（无依赖、单 dll）是幸运儿，但不是一般情况。

第二路复核撞到的是同一堵墙的另一面——**诊断形态**。resolver 返回空引用数组时，编译器给什么？inline `#R` 路径空回 → `CommonReferenceManager.Resolution.cs:823` 产 `ERR_MetadataFileNotFound` 且锚定在 `referenceDirective.Location`（即 `#R` 指令行）；但 `ScriptOptions.MetadataReferences` 里的未解析引用（宿主经 `ScriptOptions` 加的引用）→ `Script.cs:282-285` 产同码但 `Location.None`。两条路都是**通用消息、不带原因**——resolver 接口没有诊断出参，无法表达「SDK 缺失」「还原失败」「版本没写」「前缀拼错」。提案 §3 承诺「还原错误（NU1101 等）抓回挂到对应 `#R` 行」，这句只有在**宿主预扫描**（宿主持有每个 `#R` 的语法位置）时才兑现得了；纯 seam 空回只能给通用锚定，给不了领域诊断。

### 定性：这是宿主工具链契约，不是 VB 语言特性

动笔裁决前，我们先定了一件性质上的事。`#R`/`#Load`/`#!` 都不在 VB 语言规范里——`vblang\spec\preprocessing-directives.md` 只覆盖 `#Const/#If/#ExternalSource/#Region/#ExternalChecksum`，没有 reference/load 指令；它们是 Roslyn Scripting 层指令、由 fork 为 `.vbx` 携带。NuGet 引用语义是**该字符串操作数的宿主解释**，不是 VB 语法。所以：

- VB 语言层的「扩展表面区高门槛」（`vbldm-notes-2018.06.13.md` 的 Review process）**不适用**——那条针对语言特性；
- 但 **VB-like 的用户侧原则适用**——大小写不敏感、不静默失败、不引入「第二种做法」、不破坏无 nuget 的既有脚本。这四条恰好是量这份提案的尺子。

归档位置因此也定了：定稿后进产品 spec（`InternalDevDocs\spec\`），**不进 `vblang\spec\`**。与 `decisions.md` 的映射落在 **M5**（runtime-library / scripting ↔ source-gen / AOT）：本提案给 `.vbx` 补 NuGet 通道、服务 M5 脚本侧，且 C1 引擎与 D2（生成 vbproj → SDK）和官方 file-based apps 同族——这是「脚本 + 现代 .NET」双模路线的工具链对齐，不引入 M1–M8 的新方向冲突。

### 候选方案权衡：引擎

**C1（dotnet CLI 临时工程 restore）——采用，两票一致，且我们都给高置信。** 官方 file-based apps 的做法正是「`#:package` → 内存造工程 → 交给 MSBuild/dotnet restore」（`csharplang\proposals\csharp-14.0\ignored-directives.md:16,26`）；本产品自己的 NativeAOT 桥 D2 也是「生成 vbproj → SDK 编译」。C1 与这两者同族，`project.assets.json` 作为资产交换格式与整个 C#/.NET 工具链一致——compile/runtime 资产分组、TFM/平台/RID 选择全由 MSBuild/NuGet 说了算，本提案不需要复刻任何资产选择逻辑。FrameworkReference / WindowsDesktop 平台版本 / Sdk props&targets 求值恰是 NuGet/MSBuild 还原最难的部分，C1 用真实 `dotnet restore` 恰好外包掉。`.vbx` 的「低仪式感」（`Samples\CLAUDE.md` 的 no project files 定位）确实要为此背上 SDK 前提 + spawn + 冷首网络的仪式成本，但这是四个引擎候选中唯一「不发明机器」的选择——VB 的处世之道是「平台已领先的，就让平台领跑、VB 对齐」，C1 符合这个气质。

**C2（进程内 NuGet.Commands 全量还原）——否决。** 全量 restore 依赖 MSBuild 工程求值，进程内复刻是被生态验证过很痛的路线；连 .NET Interactive 都没走进程内全量还原——它经 `FSharp.Compiler.DependencyManager` 的 `DependencyProvider` 调**外部** nuget provider（外部检出 `PackageRestoreContext.cs:225-245`），`:20` restoreTfm 写死。C1 比 .NET Interactive 的 TFM 处理更诚实（镜像宿主真实 TFM，net48 走 ReferenceAssemblies）。

**增量 NuGet.Protocol 自解析——否决。** 自写 TFM 资产选择/依赖归约/框架引用逻辑，正确性风险最大——自写归约无法表达 FrameworkReference/平台版本这类宿主驱动的求值语义，restore 图里任意带这些语义的包（framework 引用类）都会在 v1 撞墙。

**TFM 镜像：只镜像运行宿主。** 我们原以为 `.vbx` 头部的 `' Attribute TargetFramework = "net48"`（`Samples\CLAUDE.md:15`）是引擎内语义、C1 临时工程必须叠加它。翻 `Installer\vbichooser\Program.vb:98-128` 后发现想错了：那是**外层 launcher 的进程派发信号**——MSIX 文件关联时 vbichooser 用 Regex 读该注释，命中 `net4*` 就 `LaunchApp("vbifw\vbi.exe", …)` 派发到 net48 宿主、否则 .NET 宿主（`proposal-runtime-host-selection.md:10,22`，1.2 归档）。vbi 引擎全树（`Interactive\vbi`/`Scripting\*`/编译器）**无人读取该注释**。因此每个 vbi 进程内，宿主 TFM 即脚本的有效 TFM；C1 镜像**只镜像运行宿主**即端到端正确（net48 声明脚本已被派发进 net48 宿主 → net48 还原图）。叠加声明 TFM 在直通路径（dotnet tool / 直接 `vbi.exe` 不经 vbichooser）反而是错的：net10 进程还原 net48 图，restore 结果根本不会被该进程加载。

### 候选方案权衡：语法

**逗号——采用。** 上游斜杠是 2015 年的死代码陈旧设计（`NuGetPackageResolver.cs:18` doc 注释、`:24` 按 `/` 拆分，自引入后从未接线）；.NET Interactive 是逗号（外部检出 `PackageReference.cs:36` 按首个逗号拆两段各 Trim），C# LDM 在 file-based 语境也把 `#r "nuget: Microsoft.CodeAnalysis, 4.14.0"` 列为已知形态（`ignored-directives.md:118`）。逗号与当代 NuGet 书写、csx 心智一致，版本区段语义强。注意 `.vbx` 的天然参照物是 **csx** 不是 file-based apps——`.vbx` 是 Script 语义（`SourceCodeKind.Script`、顶层语句、REPL），所以不跟官方 `#:package id@version`（那是 file-based/project 语义），走 `#r "nuget:"` 惯例是对的；与官方 `@` 语法的差异要在产品文档明示，避免用户拿 file-based 心智套 `.vbx`。

**大小写不敏感前缀——采用，这是本设计里最「VB」的一笔，附两个条件。** VB 标识符大小写不敏感是写进规范的硬基因（`vblang\spec\lexical-grammar.md:284`「Identifiers are case insensitive…」），且在**指令内容层有直接先例**：`vblang\meetings\2014\LDM-2014-04-16.md:66-67` 对 `#Disable Warning` 的 `<id>` 明示「must parse as per the same rules of VB identifiers, and are case-insensitive」，且同段写明 C# 对应物刻意大小写敏感——这正是「VB 在指令内容层面对同一问题有意偏离 C#」的样板。上游仅小写（`NuGetPackageResolver.cs:22` Ordinal、.NET Interactive `PackageReference.cs:30` 大小写敏感 `StartsWith`）对 VB 用户意味着：写 `#R "NuGet:…"` 会得到一个「找不到元数据」的失败——对一个「大小写不敏感是我的语言直觉」的用户，这是最差的一种失败，仅凭这一点大小写不敏感就有充分理由。两个落地条件：其一，大小写不敏感只消「大小写」一类失配，`nugt:`/`nuget ：`/`nuget:` 后无包名等近失配仍需显式诊断，才算真正「不静默失败」（这条是收口不是可选项）；其二，改动落在 C#/VB 共享的 `Scripting\Core`，须在 `NuGetPackageResolver` 的文档注释与测试里显式标注「本 fork 有意偏离上游：`Ordinal` → `OrdinalIgnoreCase` + 逗号」，防后人按上游 merge 时误当 bug 回滚。

### 候选方案权衡：接线——扩展共享 Core，让单条 `#R` 携带一个包的全部引用

这是本会议最实质的架构裁决，我们把完整的推理旅程记下来，让后来者能跟随为什么最后没有停在「宿主拦截」。

**起点：单解析闸门逼出三条出路。** `ResolveReferenceDirective` 只取 `references[0]`、>1 抛（`CommonReferenceManager.Resolution.cs:883-887`）意味着「resolver 返回 compile 资产数组」在多资产/传递依赖包上不成立。我们列了三出路：(1) resolver 只返回 1 个主 compile 资产、宿主注入其余（但首条 `#R` 只进一个程序集时公开 API 暴露依赖类型会「类型在未引用程序集」，宿主还需把闭包塞进引用集，与「resolver 是唯一咽喉」自相矛盾）；(2) 改共享 Core ReferenceManager 允许 N>1；(3) 宿主级拦截——.NET Interactive 就是 kernel 还原后把引用加进会话、编译器不碰 nuget（外部检出 `PackageManagement\KernelExtensions.cs:36-61`），是生态惯例。我们起初倾向出路 3，把 resolver seam 降为「单主资产辅助形态」。

**两条独立复核没有一致收口，反而把分歧点逼得更清楚。** 我们把出路 2 的数据模型从头查了一遍。VB 基因这一路逐点核验后**附带条件支持扩展 Core N>1**：(a) **零回归成立且比预期干净**——生产 resolver 现全部返回 0/1（`RuntimeMetadataReferenceResolver.ResolveReference:144-197` 各分支单路径），测试 resolver 亦全部 0/1；没有任何测试断言「N>1 抛 `NotSupportedException`」——`Resolution.cs:858-863` finally 注释里「tests that (intentionally) cause ResolveReferenceDirective to throw」指的是 `ReferenceManagerTests.cs` 的 resolver `case "throw": throw new TestException()`，是**测试自抛自己的异常验清理路径**，不是验 `>1` 抛异常。把 `:883-887` 的「>1 抛」改成「逐条绑 N」对既有全部输入是**纯加性**。(b) **涟漪能做**——数据不变量是位置数组与 `#R` 区引用数组按下标对齐（`GetCompilationReferences` 每成功一条加 1 引用 `:833` + 1 位置 `:834`），N>1 的正确实现 = 一条 `#R` 展开成 N 条引用 + **N 个相同位置**，混排时诊断仍锚 `#R` 行；别名（`:877` `WithRecursiveAliases(true)`）对整次调用所有 N 引用统一生效、恰是想要的行为；跨指令去重按文件与 `(filePath,file)` 同指令，语义不破。(c) **merge 摩擦有界**——这是补上游自己认的 TODO（`:885`），可整理成 PR 形态，但上游大概率不收（所有出货宿主都选择在宿主层绕开、Roslyn Scripting 已边缘化）；现实预期本地 diverg，但这段只在含 reference directive 的脚本编译时激活、本 fork 又不分发 C# 编译器 → 冲突无产品可见性。(d) **源真值更贴 VB 基因**——「喂给编译器的文本 == 磁盘上的 `.vbx`」，LSP 从源建编译即得同一引用集，「你写的即所编译的」、无隐藏态；宿主拦截要剥源/造平行引用清单，而「seam 单主资产 + 宿主注入其余」是**最差中间态**——从源单独建的编译只看到主资产（错）、宿主会话看到全图（对），同一 `.vbx` 两套真相。诊断形态：领域诊断（NU1101/SDK/版本/前缀）在两条路线下都不变（resolver 无诊断出参），但 **metadata 子类变好**——还原出的坏 dll 在 N>1 下由 resolver 返回、读失败锚 `#R` 行；宿主拦截下宿主注入的引用读失败落 `Location.None`。

C# 兼容这一路**反对扩展 Core N>1、主张维持宿主拦截**，置信度高。它的核心论据是这条闸门不是孤立一行，而是**整个 `#r` 绑定数据模型都编码了 1:1 不变量**：(i) `referenceDirectiveLocations` 与 references 按位对齐、第 2..N 个资产的诊断会错挂；(ii) 单值 `ReferenceDirectiveMap`（`Compilation.cs:738`、`CommonReferenceManager.State.cs:241`，值类型单个 `MetadataReference`）喂给 **C# 公开 API** `CSharpCompilation.GetDirectiveReference`（`CSharpCompilation.cs:1316-1322`，已入 `PublicAPI.Shipped.txt:145`），测试断言 1:1（`ReferenceManagerTests.cs:898-900,1376-1377`、`CompilationAPITests.cs:598-605`）；(iii) 制造共享 Core 首条**永久 diverg**——`upstream-merge.md` 合并账本的本地修改全在 VB 语言层（Parser/Binder/Symbols）+ Scripting 层，没有一条落在 `Compilers\Core\Portable\ReferenceManager`（C#/VB 共享、上游活跃维护），而共享 Core 恰是本产品为消费 C# 13–15 元数据必须紧密跟随上游的层（`decisions.md` M8）。它承认 ≤1 路径零回归成立，也承认「源一致 + LSP 一致」是 Core-N 的真正优点，但结论是用**最大的 diverg 换最小的簿记**不值，推荐保持混合形态（resolver 单主资产 + 宿主经 `ScriptOptions.WithReferences` 注入闭包——`Script.cs:245-296` 的 `AddRange` 通道本就支持 N，不需要动 Core）。

**三块新事实化解了反对路最硬的那根支柱，让我们把天平压向扩展。** (1) **旧 singular public API 无产品消费方**：`CSharpCompilation.GetDirectiveReference` 的树内消费方只有 C# 测试（`CompilationAPITests.cs:598-605`、`ReferenceManagerTests.cs:898-900,1376-1377`），无 Workspaces/Scripting/IDE/产品代码调用——public 面虽 shipped（`PublicAPI.Shipped.txt:145`），但本 fork 内零产品依赖。(2) **本 fork 产品面 VB-only**：分发产物（Toolset 编译器 NuGet + vbi dotnet tool/交互）均仅 VB 面，compile 模式「不含 csc」（`proposal-distribute…`）；fork 虽 vendored C# 源码树，但没有 C# 编译器产品。C# 13–15 消费是「VB 消费 C# 元数据」，不依赖本 fork 改 C# 编译器自身。(3) **API 策略**：**不 alter 旧 singular `GetDirectiveReference`**（连同其测试原样保留 → 零 C# 测试 churn、无 C# 可见 breaking）；如需暴露 N 语义，**新增 N 感知的 API/路径**。

事实 (1)+(3) 化解反对路的支点 (ii)——「public API breaking」不再成立：旧 API 原样保留、测试原样通过，C# 生态可见伤害归零；N 语义走新增路径。事实 (2) 化解支点 (iii) 的一半——共享 Core 的本地 diverg 若只影响 C# 侧（ReferenceManager 绑定主循环），本 fork 无 C# 编译器产品，冲突无产品可见性；剩余一半（VB 编译器跟上游 Core 需紧贴、merge 每次要解 ReferenceManager 冲突）是真实成本，但被「休眠区（仅脚本编译激活）+ 可 PR 化（补上游自己认的 TODO）+ 上游改动频率低」折抵。支点 (i)（位置对齐）是既定成本——VB 侧给出具体修法（每条 `#R` push N 个相同 location），两路都同意要动共享绑定主循环，分歧只在代价定性（VB：边界清晰可测；C#：要重画数据结构）——我们把它定为 spike 的**可测性检查项**而非否决项。

**裁决：方向采用扩展。** **扩展共享 Core 使单条 `#R` 可解析为 N 引用（补上游 TODO）作为 spike 的目标架构**；它保住「源真值 + LSP 复用 resolver」这一产品长期一致性卖点，而混合形态（源内单主资产 seam）被明确否决为「同一 `.vbx` 两套真相」的最差中间态。代价（改共享绑定主循环 + 首条共享 Core 本地 diverg）被「新增 N 感知 API 不 alter 旧 + 产品 VB-only + 休眠区」折抵到有界，转成 spike 门控与 merge 账本义务。宿主角色不消灭：restore 驱动 + 会话缓存写 + 领域诊断映射者（resolver 无诊断通道的事实不变）。**若 spike 不过**（REPL 跨提交继承或位置对齐可测性证伪），回退宿主级拦截——但只允许「干净剥行保行号 + 全量注入」的过渡形态，不许源内单主资产 seam 半吊子。

### 归属与各 Unresolved 裁决

**U1-B（引擎 + 会话状态落宿主、`Scripting/Core` 留抽象缝 + 注入位）——采纳，证据锚订正。** 提案引「IVT 先例 `vbi.vbproj:42-43`」方向反了：那是 vbi **授予** IVT 给测试工程（vbi → 测试），不是「宿主经 IVT 拿 Scripting 内部」。真正支撑 U1-B 的是 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`——共享 Core 授 IVT 给 `csi`/`vbi`/`Microsoft.CodeAnalysis.InteractiveHost` 等，vbi 宿主本就继承 `internal` 的 `NuGetPackageResolver` 抽象类并可从 `RuntimeMetadataReferenceResolver` 的 internal 构造器（`:65-78` 带 `packageResolver` 参数）注入。U1-A（把 dotnet-shell 还原逻辑搬进共享 Core）会让所有消费该 fork Core 的宿主（含未来 LSP）背上 VB 宿主行为，是隐性长期耦合，否决。U1-B 的显性代价「LSP 需自建会话 + 还原状态机」可接受。

**U2（跨进程是否省 restore）——采纳 A，v1 总调。** 正确性留给 NuGet no-op 重验包在盘（外部取证，E7），不自担校验；sentinel-skip 是把 NuGet 已兜的责任拿回来自担，与 `obj/project.nuget.cache` 失效语义对齐的成本高，性能数据不足前不引入。

**U3（浮动版本）——采纳 B，保留请求 spec、纯 dotnet 语义。** key 含请求的浮动 spec、dgspec 不变则 NuGet 冻结上次解析；A 的写回换 key 破坏「字节稳定」no-op 前提、确定性反而差，C 与 csx/dotnet 惯例相悖（.NET Interactive 显式把 `*`/`*-*` 当合法版本 spec，外部检出 `PackageReference.cs:63`）。用户要完全确定可 pin 版本。

**U4（省略版本）——采纳 A，v1 版本必填。** 最保守、最确定性、与 U3 解耦干净；隐式「最新解析」是 VB 会排斥的隐藏魔法（Option Explicit 基因）。缺省报「请指定版本」须经诊断锚定机制（R6）落到 `#R` 行。U4-C（先对照 .NET Interactive 缺省/空版本在 `FSharp.Compiler.DependencyManager` 的真语义再定）保留为「若想对齐生态」时的前置调查项。

**U5（原生资产与 windows 桌面包）——net10 走 Scripting loader seam 支持（sqlite 类 = 本通道唯一 native 验收类 + spike）；WindowsAppSDK/WinUI 判为宿主分发自管、本通道范围外（非「后置」项，见下）；net48 范围外 + 宿主能力诊断。** 我们把 native 分成「是不是只让 P/Invoke 找到 dll」来裁，而不是「有没有 native」。net10 的原生机制现成：脚本程序集加载进 `CoreAssemblyLoaderImpl` 的私有 `LoadContext : AssemblyLoadContext`（`CoreAssemblyLoaderImpl.cs:45`，现只挂托管 `Resolving +=` `:70-71`、无 unmanaged 钩子，全 `Scripting\` 树 grep `ResolvingUnmanagedDll|NativeLibrary` 零命中）；native 支持 = 在该 loader 加**可加性 seam**（`AddNativeProbeRoot`/构造入参 native 探测目录清单）→ `ResolvingUnmanagedDll` override → `LoadUnmanagedDllFromPath`。分工要写精确：**目录清单（策略）= 宿主**（vbi，从 C1 assets runtime 闭包取 `runtimes/<rid>/native` 目录去重集，RID 已由 restore 选好）；**unmanaged 解析钩子（机制）= Scripting loader**——`InteractiveAssemblyLoader` 是 `public sealed`（`InteractiveAssemblyLoader.cs:29`）、ALC 是私有嵌套、宿主拿不到 ALC 也看不到包 `Assembly` 实例，「纯宿主挂 resolver」不成立。此 seam 是 net10-only 共享 Scripting/Core 可加性改动（语言中立、不破坏既有、merge 面低），与 `NuGetPackageResolver` fork / `CommandLineRunner` 钩子同类登记，**不碰共享编译器 Core（接线改动面不变）**。**sqlite 类（Microsoft.Data.Sqlite → SQLitePCLRaw bundle → `runtimes/<rid>/native/e_sqlite3.dll` + 托管包装，单 native dll、无 native-to-native 依赖、无 WinRT/framework）net10 先行**——恰好同时验证传递依赖的 native 闭包，为验收样例。**同一判据把 WindowsAppSDK/WinUI 划出本分类——它甚至不是本通道会 restore 的资产，而是宿主分发自管的能力。** 把「Windows 上最想要的那批 UI 库怎么进 vbi 进程」追到宿主代码层，结论很直接：WinUI 只随商店宿主分发——`Installer\vbicore\vbicore.vbproj:28-40` 构建期 `UseWinUI` + `<PackageReference Microsoft.WindowsAppSDK 2.2.0>`，宿主代码 `Interactive\vbi\Vbi.vb:38-45` 在脚本运行前以 `WinRT.ComWrappersSupport.InitializeComWrappers()` + `Application.Start(AddressOf OnAppInit)` 完成 XAML 激活与 DispatcherQueue 生命周期引导、`:75-81` `OnAppInit` 建 `App`（宿主自己就是一个 WinUI3 应用）；`.vbx` 侧只需裸名 `#R "Microsoft.WinUI"` 走宿主 TPA（`Samples\WinUISimpleWindow.vbx:1`）——全程不经 nuget restore。普通 / dotnet-tool vbi（`Interactive\vbi\vbi.vbproj:7` 多目标 `net10.0-windows;net10.0;net48`、`:23-26` 无 WindowsAppSDK 引用）与 net48 宿主（`Installer\vbifw\vbifw.vbproj:5`）不带 WindowsAppSDK，WinUI 随之不可用——那是**宿主分发差异**，先于本提案、不是本特性要补的缺口。本通道内对这类**无处理义务、无「后置」计划**；`#R "nuget:Microsoft.WindowsAppSDK,…"` 是条不该通的用法——该包价值在 MSBuild targets + Bootstrap/激活/框架语义，C1 只 restore 不 build、loader 探测根也交付不了一个能跑的 WinUI——经 R6 报**范围外诊断**，指去宿主自带 WinUI（裸名 `#R "Microsoft.WinUI"` + WinUI 宿主）。**net48 范围外**：`DesktopAssemblyLoaderImpl` 无 ALC、无 `NativeLibrary`（net48 无此 API）、走 `Assembly.LoadFile`/`Assembly.Load(byte[])`（`DesktopAssemblyLoaderImpl.cs:33-58`）；含 native 资产闭包 + net48 宿主 → 经 R6 预扫描在该 `#R` 行报**宿主能力诊断**「此包含原生资产；net48 宿主不支持 native 探测，请在 .NET (net10) 宿主运行」——不是 restore 错误。此诊断只对 restore 拉回、探测根可解的 native 资产（sqlite 类）成立；WindowsAppSDK 类不经本通道到达，不在「指去 net10」建议内。C 选项（windows 桌面包整体范围外、平台化托管包一并后置）会同时砍掉 restore 真拉回的 windows 桌面包与 sqlite 类 native 数据访问——两者恰是 `.vbx` 在 Windows 上经本通道的主流用法，不取。

**U6（是否同时进编译模式）——强支持 A，v1 仅脚本/REPL `#R`。** `/r:` 裸名语义（TPA/GAC/路径）混入 `nuget:` 前缀 = 引入「第二种做法」+ parser 特判歧义面；vbi 批编译是短进程，无会话可驱动还原。`.vb` 单文件拉包导去 `dotnet add + .vbproj` 才是正道。注意即便注入点共用工厂（技术上顺带点亮），宿主驱动环只在脚本/REPL 入口启用，编译模式不触发还原——这是显式决定不是默认。

**U7（旧文档失实勘误）——采纳 A + 措辞精确化。** 不改两冻结正文；本会议 RESOLUTION 作为 supersede 口径，落地后在两文档相关行加勘误指针。措辞精确化（我们两条独立路径发现的是同一个矛盾）：提案对旧文档写法一处说「与本提案逗号写法最接近」、另一处说「与本提案逗号解析都不一致」——自相矛盾。按逗号 + Trim 规则（去首尾空白 → 去前缀 → 首个逗号拆两段各 Trim），`#R "nuget: Newtonsoft.Json, 13.0.3"` 段 1 Trim 后恰得 `Newtonsoft.Json`，**是前向兼容的**。旧文档真正失实的是「已实现/复用已有实现」这一断言（E5 零实现），不是拼写。勘误指针应指向「已实现」断言，不渲染成语法冲突。

**U8（windows 平台版本来源）——采纳 A，独立成立。** 读宿主 `TargetFrameworkAttribute` + 每宿主常量表兜底，镜像精确、无外部探测；**不叠加「脚本声明 TFM」维度**（该注释由外层 launcher 派发进程、引擎不读，见引擎节）。平台版本来源与「是否叠加脚本声明 TFM」正交：net10.0-windows 宿主读自身 TFM 的平台版本；net10.0/net48 宿主无 `-windows` 平台版本项（net48 走 FrameworkReference/ReferenceAssemblies，`vbi.vbproj:55-59` 先例）。B（运行时探测 `dotnet --info`）可能探到机器默认 SDK、与宿主 TFM 不一致，不取；C（固定最小版本）会特性门控过严，不取。

**测试策略订正**：提案第 1 节说 fork 该文件的测试「改断言」——但本 fork **没有上游 `NuGetPackageResolverTests`**（`Scripting\VisualBasicTest` 无 NuGet 相关测试），所以是**新增测试**不是改断言。新测试须锁定：逗号拆分、大小写不敏感前缀、各段 Trim、近失配拒绝、省略版本。

**Drawback 措辞降级**：提案 Drawbacks 说「解析改动对 fork 的 C# scripting 同文件生效（认逗号、不认斜杠）」——本 fork 的 `Scripting\` 下只有 `Core`/`VisualBasic`/测试，**没有 C# scripting 工程**，无现行 C# 消费方。但 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58` 保留对 `CSharp.Scripting`/`csi`/`InteractiveHost` 的 IVT——共享 Core 从设计上就是 C# 宿主也在用的层。所以这句应降级为「潜在 interop 注记，待定」，不是现行回归；真正现行的是改共享层与上游基线 merge 的 diverg 顾虑，而因上游已边缘化、可接受。

### RESOLUTION:

1. **定性**：`.vbx` 的 NuGet 引用是**宿主/工具链契约**，不是 VB 语言特性——语言层「扩展表面区高门槛」不适用，VB 用户侧一致性（大小写不敏感、不静默失败、不引第二种做法、不破坏无 nuget 的既有脚本）适用。定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`。映射 `decisions.md` **M5** 脚本侧；与 C# 生态同向、不引入 M1–M8 新摩擦。D4 不直接命中 P1 两档，优先级由实现规划结合路线图定。
2. **引擎：采用 C1**（dotnet CLI 内容寻址临时工程 restore，读 `project.assets.json` compile/runtime 资产分别喂编译引用与运行时加载）。C2 / 增量自解析否决（理由留档）。**镜像 = 运行宿主 TFM**：restore 的 target TFM 为**宿主供参**——vbi 传自身进程 TFM；`' Attribute TargetFramework = "net48"` 头部注释由外层 launcher（vbichooser，MSIX 文件关联）在派发进程时读取、引擎不读（`Installer\vbichooser\Program.vb:98-128`），故**不叠加「脚本自选 TFM」维度**；镜像元组 = 宿主供参 TFM + windows 平台版本 + RID + 解析后 SDK 版本 + 还原源指纹 + 框架引用镜像。SDK 门控：出现 nuget 引用才查 SDK、`global.json` rollForward 允许跨主版本升、无匹配在 `#R` 行报清晰错误；**无 nuget 引用全链路零行为零报错，配一条「无 nuget `.vbx` 行为逐字节不变」的回归测试钉死**。
3. **语法：采用**逗号 `nuget:name[, version]` + **大小写不敏感前缀**（fork `TryParsePackageReference`，签名不变）。两条落地义务：**(a)** 近失配前缀（`nugt:`/`nuget ：`/无包名）在宿主预扫描给显式诊断——大小写不敏感只消「大小写」一类失配，近失配诊断是「不静默失败」的收口；**(b)** 共享层改动在 `NuGetPackageResolver` 文档注释 + 测试显式标注「本 fork 有意偏离上游 `Ordinal` → `OrdinalIgnoreCase` + 逗号」，防 merge 误回滚。测试为**新增**（本 fork 无上游 `NuGetPackageResolverTests`），锁定逗号/大小写/Trim/近失配/省略版本。
4. **接线：扩展共享 Core 支持单 `#R` → N 引用（补上游 `CommonReferenceManager.Resolution.cs:883-887` 的 `// TODO: implement`）作为 spike 目标架构**。落地硬约束：
   - **API 面**：**不 alter 旧 singular public API** `CSharpCompilation.GetDirectiveReference`（`CSharpCompilation.cs:1316`，`PublicAPI.Shipped.txt:145`）——连同其测试原样保留，零 C# 测试 churn、无 C# 可见 breaking；如需暴露 N 语义，**新增 N 感知 API/路径**（上游若日后同做，按上游形状对齐）。
   - **位置对齐**：N>1 实现 = 一条 `#R` 展开成 N 条引用 + **N 个相同 location**（改 `CommonReferenceManager.Resolution.cs:833-834` push N），混排 `#R`（nuget + 普通路径）诊断仍锚各自 `#R` 行——spike 验证可测性。
   - **merge 义务**：改动按「完成上游 TODO」形状写（只动 `ResolveReferenceDirective`/`:802-836` 展开与对齐，不顺手改别的），保留可 PR 化；预期上游大概率不收 → 本地长期 diverg，以「产品 VB-only（无 C# 编译器分发）+ 休眠区（`HasReferenceDirectives` 门控、仅脚本编译激活）」折抵，并在 `upstream-merge.md` 合并账本**新增 ReferenceManager 类别**登记，每次合并前读账本评估同步成本。
   - 否决「源内单主资产 seam + 宿主注入其余」半吊子形态（同一 `.vbx` 两套真相——源建的编译只见主资产、宿主会话见全图）。宿主角色保留：restore 驱动 + 会话缓存写 + 领域诊断映射者。
5. **U9 spike（硬闸门）**：验证并落地共享 Core N>1——(i) REPL 跨提交继承（N 引用进 `ExplicitReferences`/`DirectiveReferences` 的实证，`:841-846`）；(ii) 位置对齐可测性（混排诊断锚 `#R` 行）；(iii) C# 旧 singular API 的 N 语义裁决（约定 resolver 返回首项 = 主资产、`GetDirectiveReference` 语义不变，文档标注）与新增 N 感知路径形状；(iv) 与 `upstream-merge.md` 账本对照的同步成本。**spike 不过 → 回退宿主级拦截，但只允许「干净剥行保行号 + 全量注入」过渡形态，禁止源内单主资产 seam。**
6. **诊断锚定契约**：restore/NU 错误与「版本缺失/SDK 缺失/前缀失配」由宿主预扫描持有各 `#R` 的 directive location、再造为锚定该行的领域诊断；resolver 空回只能给通用 `ERR_MetadataFileNotFound`（inline `#R` 锚 `#R` 行 `CommonReferenceManager.Resolution.cs:823`；ScriptOptions 未解析路径落 `Location.None` `Script.cs:285`），**不作 v1 的「可读原因」承诺**——此约束在宿主拦截与 Core-N 两路线下均成立（resolver 无诊断出参）。Core-N 的增量收益在 **metadata 子类**：还原出的坏 dll 由 resolver 返回时读失败锚 `#R` 行，而非宿主注入引用的 `Location.None`。「安全网（缓存未命中重编）」若保留则接受通用锚定消息，否则 v1 去掉安全网强制预扫描——U9 一并定。
7. **归属：采纳 U1-B**（引擎/会话状态落宿主 vbi，经 IVT 注入）。**证据锚订正**：`vbi.vbproj:42-43` 是 vbi → 测试工程 IVT，方向不支撑论点；正锚是 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`（Core 授 IVT 给 `vbi`/`csi`/`InteractiveHost` 等）。U1-A 否决（共享库背 shell-dotnet/缓存逻辑，隐性长期耦合）。
8. **各 U 采纳**：U2-A（v1 跨进程总调、正确性交 NuGet no-op）、U3-B（浮动版本保留请求 spec、纯 dotnet 语义）、U4-A（v1 版本必填、缺省报「请指定版本」经 R6 锚 `#R` 行；U4-C 保留为对齐生态时的前置调查项）、U5（net10 native 经 Scripting loader seam 支持、sqlite 类为唯一验收类；WindowsAppSDK/WinUI 判宿主分发自管、本通道范围外——非「后置」项，遇 `#R "nuget:Microsoft.WindowsAppSDK,…"` 报**范围外诊断**指去宿主自带 WinUI；net48 范围外 + 宿主能力诊断，限定只对 restore 拉回、探测根可解的 sqlite 类原生资产成立）、U6-A（v1 仅脚本/REPL `#R`、编译模式导去工程）、U7-A + 措辞精确化（旧文档 `nuget: Package, Version` 与本提案逗号+Trim **前向兼容**，失实点在「已实现」断言，勘误指针指向断言不指向拼写）、U8-A（宿主 `TargetFrameworkAttribute` + 常量表，独立成立；不复加脚本自选 TFM 维度，见 R2）。
9. **Drawback 措辞降级**：「fork 的 C# scripting 也认逗号」降级为**潜在 interop 注记**（本 fork 无 C# scripting 工程、无现行 C# 消费方；`Scripting.Core.csproj:54-58` 保留 CSharp.Scripting/csi IVT 说明共享层设计上 C# 宿主在用）。现行风险是共享层与上游 merge 的 diverg，因上游边缘化可接受。共享编译器 Core（`ReferenceManager`）的 diverg 另按 R4 merge 义务登记 `upstream-merge.md`。

### Implication:

- **能力面**：`.vbx` 从「只能本机/平台程序集」到「可 `#R "nuget:…"` 拉包」——在 Core-N（U9）落地前，多资产/传递依赖包需靠宿主过渡形态注入闭包；U9 后单 `#R` 自含全图。提案的 demo（Newtonsoft.Json，无依赖单 dll）能跑，不代表一般包能跑——这是本会议最大的范围修正。**原生面分层**：net10 上 sqlite 类（`runtimes/<rid>/native` 单 dll + 托管包装）经 Scripting loader seam 支持——`.vbx` 的数据访问（`Microsoft.Data.Sqlite`/SQLitePCLRaw）这一主流场景 v1 可用。WindowsAppSDK/WinUI 不在本通道对象内：宿主构建期 `PackageReference Microsoft.WindowsAppSDK` + `Application.Start` 引导分发自管（`vbicore.vbproj:28-40`、`Vbi.vb:38-45`），脚本裸名 `#R "Microsoft.WinUI"` 走 TPA——若误写 `#R "nuget:Microsoft.WindowsAppSDK,…"`，经 R6 报**范围外诊断**指去宿主自带 WinUI。net48 范围外（宿主能力诊断指去 .NET 宿主，限定只对 restore 拉回、探测根可解的 sqlite 类原生资产成立）。
- **架构面**：「resolver 是唯一咽喉」对多资产 NuGet 包不成立（编译器单 `#R` → 单引用）；本会议以**补完上游 TODO、扩展共享 Core N>1** 让源内 `#R` 自含（源真值），换取 LSP 复用 resolver 的天然一致；代价是共享编译器 Core 首条本地 diverg，以「不 alter 旧 API + 产品 VB-only + 休眠区 + 账本登记」管理。宿主仍是 restore 驱动 + 领域诊断映射者。
- **生态面**：对 C# 生态零可见 breaking——旧 `GetDirectiveReference` 原样保留（`PublicAPI.Shipped.txt` 不触碰）、C# 测试零 churn；N 语义走新增 API/路径。共享 Core 的改动若上游日后同做可 PR 化，否则为本地 diverg。
- **诊断面**：v1 的可读诊断全部依赖宿主预扫描持 `#R` location；纯 seam 只给通用锚定消息。Core-N 让坏 dll 等 metadata 子类诊断锚 `#R` 行（而非 `Location.None`）。
- **回归面**：无 nuget 引用全链路零行为零报错 + 一条逐字节回归测试；N>1 扩展对既有输入纯加性（现有 resolver 全返回 0/1）——「几乎不做 breaking change」的直接兑现。
- **文档面**：两旧提案（`proposal-vbscript-lsp.md:121`、`proposal-distribute…:112`）的「已实现」断言失实，经本 RESOLUTION supersede；落地后加勘误指针。

### 后续工作项

**下一阶段 = 任务计划（`tasks\<slug>\`）先决（硬闸门，本会议不开实现）：**

- **U9 spike（硬闸门）**：共享 Core N>1 的最小改法——只动 `CommonReferenceManager.Resolution.cs` 两处（`ResolveReferenceDirective` 返数组去 throw；调用循环逐条 push N 引用 + N 个相同 location）；单值 map 以**首项=主资产**约定保留，`State.cs`/`Compilation.cs`/`CSharpCompilation.GetDirectiveReference`/`PublicAPI.Shipped.txt` 均不动；N 引用经 `ExplicitReferences`（`:841-846`）全量跨提交继承（无未使用裁剪）；位置对齐可测（`referenceDirectiveLocations` 仅内部消费）；去重/别名在 N 下语义保持；默认 resolver 返回 0/1 → 纯休眠。实证 spike 用例：跨提交闭包可见性（闭包中无源码直用程序集）+ 混排多资产坏 dll 位置锚定单测。残余风险按序：ExplicitReferences 实证缺口、对齐去重回归、主资产排序稳定性（→ resolver 契约文档）、共享 Core diverg（→ `upstream-merge.md` 账本）、`DirectiveReferences` 膨胀（→ 文档标注）。**通过 → 接线 R4 定稿；不过 → 回退宿主级拦截（仅干净剥行保行号 + 全量注入过渡形态）。**
- **native spike（loader-seam 机制与分工见 U5）**：net10 `ResolvingUnmanagedDll` 探测 seam 落地实测——钩子挂 `CoreAssemblyLoaderImpl.LoadContext`（in-memory + per-path 两处）→ `LoadUnmanagedDllFromPath`，`Microsoft.Data.Sqlite` 端到端（一次 `SELECT`）为验收；dll 名映射/搜索序（含跨平台 `.dll`/`.so`/`.dylib`）；RID 回退；缺 RID 资产清晰诊断（带已搜目录）；assets 字段提取；附加时序。空 native 根集 = 现状（零回归）。
- **诊断锚定机制收口（R6）**：宿主预扫描持 `#R` location 的领域诊断映射。

**进入实现期的要求（进测试计划）：**

- 零回归测试一：无 nuget 引用的既有 `.vbx` 全链路行为逐字节不变。
- 零回归测试二：现有单结果 resolver 全量脚本测试不回归（N>1 纯加性背书）。

**已决约束的记录（非待定）：**

- restore target TFM 作**宿主供参**（vbi 传进程 TFM；未来 LSP 复用会话按 `proposal-vbscript-lsp.md:103` 读注释传 net48，不把「读注释」下放共享引擎）。
- 已知限制：`' Attribute TargetFramework = "net48"` 仅 MSIX 文件关联经 vbichooser 读、选宿主进程；**直接 `vbi`/dotnet tool 静默忽略并跑进程自身 TFM**（先于本提案的宿主选择 UX 缺口，`README.md:19` 对 tool 分发不完全成立）——产品文档须区分两种分发形态。

**证据等级注记 / 延后调查：**

- 外部取证已检查（2026-09 独立现网核验属实）：dotnet/interactive#4163（弃用点名 file-based apps 为 C# 继任）、dotnet/sdk#49222（csx closed/not_planned、指去 dotnet-script）、dotnet/sdk#55716（VB "can opt in later"）、dotnet/roslyn#17666（2017 open、无官方排期）、NuGet 7.9 no-op 校验包在盘（7.9.0.83 `VerifyRestoreOutput`/`Sha512Exists` + MS Learn「Cleaning the global-packages directory」）。
- U4-C（可延后）：`.NET Interactive` 缺省/空版本在 `FSharp.Compiler.DependencyManager` nuget provider 的最终行为——若想对齐生态再做。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——方向（C1 + 逗号 + 大小写不敏感 + U1-B/U2-A/U3-B/U4-A/U5/U6-A/U8-A）两票独立收敛且高置信；证据纪律上乘，提案 E1–E6 + `vbi.vbproj` 锚点逐一复核全部属实，外部取证经独立现网核验属实（2026-09），Unresolved 1–8 每项带备选 + 证据锚 + 建议；VB 基因贴合度高（大小写不敏感有 `LDM-2014-04-16.md:66-67` 指令内容先例、`lexical-grammar.md:284` 标识符先例，「不静默失败/不引第二种做法/零回归」全数守住）。接线裁决在复核中从「宿主拦截为主」改判为「扩展共享 Core 单 `#R` → N（补上游 TODO）作为 spike 目标架构」——两路独立复核分歧（一路附带条件支持、一路反对）经三块新事实（旧 API 仅 C# 测试消费、本 fork VB-only、新增 N 感知 API 不 alter 旧）化解反对路最硬支点后定案，剩余反对转为 spike 检查项 + merge 账本义务；TFM 镜像收窄为只镜像运行宿主 TFM（R2）；native 范围收窄为 net10 loader-seam 支持（sqlite 类唯一验收）+ net48 范围外（U5），WindowsAppSDK/WinUI 判宿主分发自管、本通道范围外（非后置项）。**无待定设计**：剩余未定 = U9 跨提交 / native sqlite 实证 spike 与延后项 U4-C——均为任务计划阶段交付物。**下一阶段是任务计划（`tasks\<slug>\`），其第一道硬闸门是 U9 spike**——接线 R4 以 spike 通过为定稿前提、不过则回退宿主拦截；同批收口诊断锚定机制（R6）与 native spike。**本 RESOLUTION 不开启实现**；实现以 spike 与任务计划通过为准。
