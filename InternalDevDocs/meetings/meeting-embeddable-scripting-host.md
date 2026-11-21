# Visual Basic Language Design Meeting
November 16, 2026

议题是 `proposal-embeddable-scripting-host`——让普通 .NET 应用在自己的进程里托管 `.vbx` 与 `.vb` 片段，而不是把 `vbi` 当子进程调。触发点很直接：今天想「用库集成」的人只有两条路，源码/项目引用自己拼宿主，或 spawn CLI；前者拿不到 `.vbx` 语义与 NuGet，后者就是 CLI 本身。我们翻 `Scripting\` 时确认，执行管线其实已经齐全——脚本模式、`/check`、`#Load` 多树、`Return` 退出码、`#R "nuget:"` 都在——缺的只是公共出口：`CommandLineRunner` 是 `internal sealed`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:24`），`ConsoleIO` 是 `internal class`（`ConsoleIO.cs:13`），`CollectLoadTrees` 是 `Friend`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:18`、`:117`）。所以这不是「补一套能力」的会议，而是「把已有管线接到官方 Hosting 上、并决定哪些东西算契约」的会议；真正有争议的，是契约面与默认语义从哪来。

## Agenda

* [Proposal: 可嵌入的脚本宿主包 / Embeddable Scripting Host Package](#proposal-可嵌入的脚本宿主包)

## Proposal: 可嵌入的脚本宿主包

_Related: [`../proposals/proposal-embeddable-scripting-host.md`](../proposals/proposal-embeddable-scripting-host.md)；近邻 `../proposals/proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（第三条分发通道）、`../proposals/proposal-vbi-nuget-reference.md`（`#R "nuget:"` 引擎与 seam）、`../proposals/proposal-vbi-project-reference.md`（v1 不含）、`../proposals/proposal-load-directive.md`（展开点单点纪律）；`../decisions.md` D4/D5/D7/D9 与 M5_

> **来源标注**：本会议逐条复核了提案的源码锚点（`文件:行号`），三处行号/措辞修正与一处证据口径修正记录在讨论与 RESOLUTION。独立五维评审为不入库工作材料，供规划内部使用，不混入本官方记录。 **复会（2026-11-17）**：delta = 把 `VBScriptSession` 纳入 v1；两路复会意见均为「修改后接受 / 附带条件支持（高置信）」，条件落进 R3/R6/R9/R12/R17。

### 场景与缺口

应用侧的缺口是「库集成」而不是「执行能力」。`Scripting\VisualBasic\PublicAPI.Shipped.txt` 里公开的只有 `VisualBasicScript` 的 `Create/RunAsync/EvaluateAsync`（`:6-11`）与 `VisualBasicObjectFormatter`（`:1-5`）；`VisualBasicScript.vb:150` 的交互入口虽然公开，却把 `ConsoleIO.Default`（`:157`）和 vbi 的 rsp 文件名（`:150` 的参数）写死，等价于 CLI 本体。应用即便绕过它，也拿不到 `.vbx` 文件语义、`Print`/`Args`、退出码与 NuGet 还原。

包这一侧也不成立：`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:13-14` 虽然 `IsPackable`，`PackageId` 却是上游名字 `Microsoft.CodeAnalysis.Scripting.Common`；`Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:28-29` 经 `ProjectReference` 直接引用 fork 编译器，直接打包会把依赖写成上游 `Microsoft.CodeAnalysis` 包；两库版本线一个 5.9.0、一个 2.0.0-RC3（`Directory.Build.props:6`、`...VisualBasic.Scripting.vbproj:8-9`）。

我们按 `decisions.md` M5（脚本/工具链侧）与 D4 判这是分发与宿主契约，不是语言特性；与 `meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` 的定性同层。

### 翻源码：三条承重事实

**第一，可替换的 NuGet 行为早就是缝，不是新设计。** `INuGetRestoreCoordinator.cs:12-33` 的文档注释写明：宿主在提交或文件脚本编译前预扫描、还原、返回锚在 `#R` 上的阻塞诊断；`NuGetPackageResolver.cs:12-51` 是同步解析前端，`nuget:` 前缀用 `OrdinalIgnoreCase`、逗号分隔。两者已经通过 `CommandLineRunner` 的构造参数注入（`CommandLineRunner.cs:30-34`），默认实现 `NuGetRestoreCoordinator.vb:34` / `NuGetPackageResolverImpl.vb:15` 都是 `Friend`。提案把「默认同 vbi、可替换」建立在这两条缝上，方向是对的；我们只是把它从 `Friend` 提到公共契约。

**第二，「默认同 vbi」的默认值不只在 restore 里。** 这一条是提案没写透、我们翻 `vbi` 的响应文件才发现的：脚本的可用性一大半来自 `Interactive\vbi\vbi.coreclr.rsp:1-50` 的 `/nostdlib`、`/vbruntime-`、34 条 `/r:` 与 11 条 `/imports:`。提案的 `VBScriptOptions` 示例只有 `Imports.Add("System")`，没有交代默认引用集/导入集从哪来。后果是可复现的：同一段 `.vbx` 在 `vbi` 里 `Console.WriteLine` 直接可用，嵌进宿主却 `BC30451`——正是 §6 想消灭的「行为随入口而变」。**这一条不补，「宿主与 CLI 同语义」的验收门第一天就会红。**

**第三，单管线的成本不是「去掉 Console」。** 提案 §6 说把与「控制台」无关的部分抽成 core，但 `CommandLineRunner` 与 CLI 参数是缠在一起的：`:140` 从 `CommandLineArguments` 造 `ScriptOptions`、`:241` 取 `Arguments.ScriptArguments`、`:245` 读 `Arguments.Check`、`:249` 调 `_compiler.ReportDiagnostics`。抽 core 实际是「以 `VBScriptOptions` 为一等输入重写取 options 的路径」，是一笔不小的重构，但值得——`#Load` 的 once 语义、`#R` 预扫描、退出码三者共用同一个展开点（`VisualBasicScriptCompiler.vb:117`、`CommandLineRunner.cs:183` 起的 `GetScriptOptions`、`:238-269` 的 `RunScriptAsync`），两套实现必然漂移。

### 候选方案

**PROPOSAL A —— 一个运行时包 + 官方 Hosting 装配 + 自有契约面（采用）。** 包内同时放编译器/脚本程序集与门面；公共命名空间 `Nukepayload2.VBScriptDotNet.Hosting` / `.Scripting`；`AddVBScripting` 只挂官方 `IHostApplicationBuilder`/`IServiceCollection`；执行入口是容器里的 runner；NuGet 还原与解析是可替换服务；脚本层只暴露自有结果类型。

**PROPOSAL B —— 只发程序集、给 README 配方（否决为主方案，保留 v0 应急）。** 成本最低，但 `.vbx` 文件语义、`Print`/`Args`、退出码、NuGet 全要用户自己拼，契约不稳。

**PROPOSAL C —— 自造 Host/容器（否决）。** 与官方 Hosting 重复，且无法与应用程序既有的 Host 组合；C# 生态里这类库的惯例是只提供 `IServiceCollection` 扩展。

**PROPOSAL D —— CLI 子进程（否决）。** 进程边界让 globals/返回值传递复杂化，也无法在同一进程里注入服务。

**PROPOSAL E —— 改名 fork Roslyn Scripting（否决）。** 能满足「命名空间无 Microsoft」，但 IVT、程序集身份、公共形状、上游 merge 全线受影响。我们要的是「形状可借、契约不背」，不是再造一个 Roslyn。

### 权衡：Q&A

- **依赖粒度：引 `Microsoft.Extensions.Hosting` 还是只引 Abstractions？** 提案写的是前者。我们翻官方分层后认为这是库设计错误：`Hosting` 是 app 级 meta-package，会把 Configuration.Json/CommandLine、Logging.Console/Debug 等二十余包拖进库依赖，并强迫宿主统一版本；net472 上还多带 `Microsoft.Bcl.AsyncInterfaces` 与 `System.Threading.Tasks.Extensions`。库应只引 `Hosting.Abstractions`（`IHostApplicationBuilder` 就在其中）、`DependencyInjection.Abstractions`、`Options`、`Logging.Abstractions`、`Configuration.Abstractions`，配置绑定再补 `Options.ConfigurationExtensions`。示例里的 `Host.CreateApplicationBuilder` 属于宿主，不属于库。

- **单例 runner 与「不承诺线程安全」自相矛盾。** 提案把 `IVBScriptRunner` 注册成单例（`:125`）又说线程安全不承诺；在 ASP.NET Core 多请求下这就是静默损坏。DI 惯例是 singleton 必须线程安全，因此要么改成 transient/scoped 的会话对象，要么内部串行化。我们选 **transient runner + 单例 restore 会话缓存（内部加锁） + 每 runner 一个 assembly loader**，并给 runner 加重入守卫：同一个 loader 被并发占用时抛自有异常，不静默。同时删掉「随 `IHost` 启动/停止」的说法——没有 `IHostedService` 就不会初始化；改成把 `IHostApplicationLifetime.ApplicationStopping` 链接进每次执行的取消令牌。

- **契约面不自称 Microsoft：提案的名义与实质。** 提案 §1 说公共契约用 `Nukepayload2.VBScriptDotNet.*`，但替换点的第一个参数是 `SourceText`、返回 `ImmutableArray(Of Diagnostic)`（提案 §3 示例）；脚本层直接返回 `Script(Of T)` / `ScriptState(Of T)`（`PublicAPI.Shipped.txt:6-11`）；`Print`/`Args` 只存在于 `CommandLineScriptGlobals`（`CommandLineScriptGlobals.cs:22-37`）。这与「不承诺兼容」直接冲突，也堵死未来用独立 ALC 隔离的可能。**我们裁决：公共 seam 用自有 DTO（源码文本+路径、自有 `VBScriptDiagnostic`、自有 `VBScriptResult`），Roslyn 类型只留实现层；脚本层 v1 不给 `ContinueWith`/状态链。**

- **脚本层 v1 是两个入口。** 无状态（`VBScript.RunAsync` / `EvaluateAsync` / `CompileAsync`，返回 `VBScriptResult`）与有状态（`IVBScriptSessionFactory.CreateSession(options)` → `IVBScriptSession`，供 UI 驱动的 immediate window）。**无状态入口内部用「一次提交的 session」实现**——imports 剥除、异常映射、打印三处单点，否则两条路径必然漂移（`CommandLineRunner.cs:238-269` 与 `:381-388` 的结果语义本来就不同）。不给 `Script(Of T)`/`ScriptState`/`ScriptOptions`/`CompilationErrorException` 的文档承诺；`RunInteractiveAsync` 便利层不进 v1（与 `VisualBasicScript.vb:146-182` 既有公开方法同名不同义，且把提示符/续行/阻塞读带进执行契约），README 给终端循环的十行配方即可。

- **globals 与输出。** `IScriptGlobalsFactory` 与 `VBScriptOptions.GlobalsType` 是同一件事的两个旋钮，合成一个；提供自有基类 `VBScriptGlobals`（`Print`/`Args`），`GlobalsType` 必须派生自它。`IScriptOutput` 默认不能写 `Console`（库污染 stdout，服务里尤其糟）；默认给 `ILogger` 适配或 Null，`UseConsoleOutput()` 显式开，vbi 等价测试用显式注册。

- **restore 的三层缝与成组替换。** 提案把仓内两档 seam 折成一个 `INuGetRestoreService`，但 `NuGetRestoreRunner.vb:230` 明确生产实现是 `DotNetRestoreRunner`——预扫描协调器与执行器是两级。我们保留三个契约（预扫描/还原、还原执行、解析前端），把 `NuGetPackageSession` 提升为 DI 服务，并提供成组注册 `AddVBScriptNuGetRestore()`；只换还原服务而保留默认解析层时，runner 构造必须报错，而不是让默认 resolver 读一个没人写的缓存。restore 的镜像上下文（宿主 TFM/RID/平台版本/SDK 版本/源指纹）要作为 `VBScriptOptions` 的只读属性暴露，否则「可替换 = 可复现默认」不成立；默认实现首次触发时经 `ILogger` 留痕。

- **程序集身份是全文最硬的一条。** `Directory.Build.props:24-26` 用 Microsoft 共享公钥 public-sign，包内 `Microsoft.CodeAnalysis*.dll` 与官方包同名同公钥。.NET Core 的默认 ALC 按简单名绑定，.NET Framework 上同名同 PKT 就是同一身份——不是「加载两份」，是「谁先加载谁赢」。README 写死不够：传递依赖、插件、测试宿主不会读 README。我们加 `buildTransitive` 冲突检测（检测到 `Microsoft.CodeAnalysis*` 包引用即 error，留显式关闭属性），并把边界写进 `PackageDescription` 与错误消息。

- **net472：可行性一半、闭包没写。** 我们实查本地缓存，`Microsoft.Extensions.Hosting` 10.0.0 的 `lib/` 有 `net462`/`netstandard2.0`/`net8.0`/`net9.0`/`net10.0`，net472 走 `net462` 资产成立；编译器/脚本库的 netstandard2.0 构建在 netfx 有先例，但 `Interactive\vbi\bin\Debug\net48` 的闭包含 `System.Buffers`、`System.Collections.Immutable`、`System.Memory`、`System.Numerics.Vectors`、`System.Reflection.Metadata`、`System.Runtime.CompilerServices.Unsafe`、`System.Text.Encoding.CodePages`、`System.Threading.Tasks.Extensions` 八个 polyfill——提案只写「自包含」，没列这八个。且 `vbi.vbproj:7` 的 net48 只是先例，不是 net472 端到端证据：net472 必须有一次 pack + 冒烟作为硬门。TFM 集合保持 `net10.0 + net472`；`netstandard2.0` 的补充按「net8 宿主需求 + ns2.0 功能面实测」触发，不在 v1。

- **默认引用集：net10 与 net472 不同。** `vbi.coreclr.rsp` 不含桌面程序集，`vbi.desktop.rsp` 含 `/r:System.Drawing` 与 `/r:System.Windows.Forms`。库不该默认背桌面程序集，所以 v1 的「默认同 vbi」验收门只对 **net10** 要求逐项相等；net472 的桌面引用面作为显式 opt-in（`VBScriptOptions.UseDesktopReferences = True`），差异写进文档。这条把「同语义」从口号变成可测断言。

- **包 ID 与门面程序集。** `Nukepayload2.Compilers.*` 目前等于 Toolset 与 CLI 两个工具链包，把运行时库放进这个前缀有误标风险（会被按 `DevelopmentDependency` 消费、降低运行时搜索可发现性）。会议保留 `Nukepayload2.Compilers.VBScriptDotNet.Scripting` 作为 v1 包 ID（家族一致、包内确实自带编译器），但要求：不继承 `DevelopmentDependency`、公共契约固定在一个门面程序集、README 首段写明「运行时库，非构建工具」。该保留项列入 OPEN QUESTIONS，带重开条件。

- **trim/AOT。** 动态编译与动态加载注定不可裁剪；公共入口标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`，包标 `IsTrimmable=false`，否则 C# 用户 `PublishTrimmed` 后在运行期才炸。

- **锚点复核与修正。** 大部分锚点逐行属实；三处需修正：`CollectLoadTrees` 是 `:117`（不是 81-103，`:82-96` 是 `ParseSubmissionForHost`/`IsCompleteSubmissionForHost`，文档注释起于 `:88`）；`CommandLineRunner.RunScriptAsync` 实际到 `:269`（`/check` 在 `:245-252`、返回退出码 `:256`、异常映射 HResult `:259-267`）；Toolset 包 RC3 实测 11.68 MiB（提案写 12.2 MB，是十进制口径，宜统一单位）。另有两处口径修正：`proposal-blazor-wasm-repl.md:149` 原文是「需内存压测」，不是「已实测」；`vbi.vbproj:7` 证明的是 net48，不是 net472。以及一条证据纪律：`...VisualBasic.Scripting.vbproj:1-12` 的「无 IsPackable」是 grep 缺失断言，应注明来源。

### RESOLUTION:

1. **定性**：本提案是宿主/工具链契约（M5），默认值是产品策略而非语言规范；规范只写「resolver 由宿主注入」这一层（`spec-reference-directive.md:11`、`:109-116`），不写 `nuget:` 的默认来源。
2. **装配**：采纳官方 Hosting 形状，但库依赖只用 Abstractions（`Hosting.Abstractions`、`DependencyInjection.Abstractions`、`Options`、`Logging.Abstractions`、`Configuration.Abstractions` + `Options.ConfigurationExtensions`）；提供 `AddVBScripting(this IHostApplicationBuilder, …)` 与 `AddVBScripting(this IServiceCollection, …)` 两个重载，返回 builder 以支持链式；缺省用 `TryAdd*`，覆盖用 `Replace`。
3. **生命周期与并发**：`IVBScriptRunner` 注册为 **transient**；restore 会话缓存为单例并内部加锁；**每次一次性执行与每个 session 各持独立 assembly loader**；一次性执行的守卫留在 runner，**session 自持守卫**（并发占用抛自有异常，不静默）；session 由 `IVBScriptSessionFactory`（单例）创建、不进 DI 容器（生命周期是窗口而非请求）；`IHostApplicationLifetime.ApplicationStopping` 在**每次提交**链接，不在建 session 时捕获一次；删除「runner 随 host 启停」的表述。
4. **默认语义**：`VBScriptOptions` 的默认 references/imports 逐项等于 `vbi.coreclr.rsp`（net10）；net472 默认不带 `System.Drawing`/`System.Windows.Forms`，桌面引用为显式 opt-in。验收测试必须逐项比对 rsp，差异写入文档。
5. **契约面**：公共契约不得出现 `Microsoft.CodeAnalysis.*` 类型。服务 seam 使用自有 DTO（`VBScriptSource`＝文本+路径、`VBScriptDiagnostic`、`VBScriptResult`）；实现层可继续用 Roslyn 类型。
6. **脚本层 v1 是两个入口**：无状态（`VBScript.RunAsync` / `EvaluateAsync` / `CompileAsync`，返回 `VBScriptResult`；`/check` 语义改为 `CompileAsync`）与有状态（`IVBScriptSessionFactory.CreateSession(options)` → `IVBScriptSession`）。**无状态入口内部复用一次提交的会话实现**。不给 `ContinueWith`/`ScriptState`/`ScriptOptions`/`CompilationErrorException` 的文档承诺；`RunInteractiveAsync` 便利层不进 v1（命名冲突 + UI 策略 + 阻塞读）。
7. **异常模型**：脚本失败是数据（诊断+退出码）；宿主误用（文件不存在、runner 被占用、宿主已释放）抛自有异常。
8. **globals**：新增自有基类 `VBScriptGlobals`（`Print`/`Args`），`GlobalsType` 必须派生自它；删除 `IScriptGlobalsFactory`（与 `GlobalsType` 合并为单一旋钮）。
9. **输出**：`IScriptOutput` 默认不写 `Console`（默认 `ILogger` 适配或 Null）；`UseConsoleOutput()` 显式开启；vbi 等价测试用显式 Console 注册。 `PrintedValue` 只作数据返回，不写 `IScriptOutput`；是否回显由宿主决定（双通道会重复且乱序）。
10. **NuGet seam 粒度**：保留三层契约——预扫描/还原协调、还原执行、解析前端；`NuGetPackageSession` 提升为 DI 服务；提供成组注册 `AddVBScriptNuGetRestore()`；runner 构造校验「自定义还原服务 + 默认解析层」这一组合并报错；restore 镜像上下文（TFM/RID/平台/SDK/源指纹）作为 `VBScriptOptions` 只读属性；默认首次触发经 `ILogger` 留痕。
11. **程序集身份**：README 与 `PackageDescription` 明写 process-wide exclusive（含传递依赖与插件）；加 `buildTransitive` 冲突检测（检测到 `Microsoft.CodeAnalysis*` 即 error，留关闭属性）。
12. **TFM 与闭包**：v1 `net10.0` + `net472`；net472 显式铺八个 polyfill（`System.Buffers`、`System.Collections.Immutable`、`System.Memory`、`System.Numerics.Vectors`、`System.Reflection.Metadata`、`System.Runtime.CompilerServices.Unsafe`、`System.Text.Encoding.CodePages`、`System.Threading.Tasks.Extensions`）；net472 必须有一次 pack + 冒烟实测作为发布硬门；`netstandard2.0` 按触发条件推迟。 net472 的 `IAsyncDisposable` 依赖 `Microsoft.Bcl.AsyncInterfaces`（`Hosting.Abstractions` 10.0.0 的 net462 组传递依赖），nuspec 清单须单列，不能只数 `lib/` 里的八个。
13. **打包**：公共契约固定在一个门面程序集；运行时包不继承 `DevelopmentDependency`、不默认 `PrivateAssets=all`；包 ID 保留 `Nukepayload2.Compilers.VBScriptDotNet.Scripting`，README 首段写明「运行时库，非构建工具」。
14. **trim/AOT**：公共入口标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`；包标 `IsTrimmable=false`。
15. **单管线成本重述**：§6 的落点不是「去掉 Console 适配器」，而是「以 `VBScriptOptions` 为一等输入重写取 options 的路径」，`/check` 降为 `VBScriptOptions` 的一种模式；vbi 与 runner 共用同一 core。
16. **文档修正**：`CollectLoadTrees` `:117`；`CommandLineRunner` `:238-269`；Toolset 包体积统一口径（11.68 MiB 或 12,245,623 字节）；`proposal-blazor-wasm-repl.md:149` 改「待压测」；`vbi.vbproj:7` 标 net48 先例；「无 IsPackable」标注为 grep 断言。
17. **会话语义（v1）**：① 同一 globals 实例跨提交复用，`Reset` 不换；② 编译错误不杀会话且**不刷新 options**（只有成功提交才刷新），运行异常置 `Exception` 后继续（`CommandLineRunner.cs:370-384` 的既有行为）；③ 复刻 REPL 的 imports/refs 纪律——每提交 `RemoveImportsAndReferences`（`CommandLineRunner.cs:405`、`CommandLineHelpers.cs:20`），沿 `Previous` 链 replay `AddPreviousSubmissionImports`（`VisualBasicScriptCompiler.vb:192-220`），并保留**初始 options 快照**供 `Reset` 恢复；④ 基目录参数化：vbi 用进程 CWD，session 用 `VBScriptOptions.WorkingDirectory`，不得被 `UpdateOptions:399` 静默重置；⑤ `PrintedValue` = vbi 会写到 stdout 的那一行（`HasReturnValue` 规则，`Script.cs:307-309`），但**不落 `IScriptOutput`**；⑥ 完整判定返回三态 `VBScriptSubmissionState`（Complete/Incomplete/Invalid），不暴露 ParseOptions；⑦ `Variables` 按名去重（末次胜）、DTO 只读 + `IsReadOnly`、名字比较大小写不敏感、不含 globals；⑧ 取消或异常后 session 进入 faulted，只允许 `Reset`/`DisposeAsync`；`DisposeAsync` 幂等且取消在飞提交；宿主停掉后再提交抛自有异常；`Reset` 清状态、恢复初始 options、保留 globals、新建 loader。

### Implication:

- **能力面**：应用可以只引一个包，用官方 Hosting 装配出与 `vbi` 同语义的脚本执行；NuGet 还原默认可用、可换，不必 spawn CLI。
- **语义面**：v1 的「同 vbi」从口号变成可测断言——net10 的 references/imports 与 rsp 逐项相等；net472 的桌面引用面明确为 opt-in，不再声称完全一致。
- **风险面**：程序集身份是同进程共存的硬边界，必须靠构建期检测而不是 README 兜底；并发默认从「单例+不承诺」改为「transient+重入守卫」，把静默损坏变成显式失败。
- **维护面**：脚本层不再把 Roslyn Scripting 的类型写进契约，上游已退场也不会冻结我们的 API；单管线重构的成本被显式记账。
- **会话面**：应用可以做 UI 驱动的 immediate window——`CreateSession` + `SubmitAsync` + `Variables`，而不必自己拼提交链；回显由宿主从 `PrintedValue` 决定，库不抢 `IScriptOutput`。

### OPEN QUESTIONS / TODO / Follow-up

- **包 ID 的误标风险（保留待观察）**：`Nukepayload2.Compilers.*` 目前是工具链家族；若发布后发现被按 `DevelopmentDependency` 消费、或运行时搜索不可发现，切到 `Nukepayload2.VBScriptDotNet.Scripting`。重开条件写死：出现一次实际误用报告或一次包搜索评估失败。
- **net472 端到端**：打包 + 冒烟工程（引用包、跑 `Console.WriteLine`、`#Load`、`#R "nuget:"`）必须在 v1 发布前完成；当前只是资产存在性与先例推断。
- **netstandard2.0 补充**：触发条件=net8 宿主需求出现且 ns2.0 功能面（ALC/native 探测）实测可接受。
- **restore 上下文的具体字段**：TFM/RID/平台版本/SDK 版本/源指纹之外，是否含 `global.json` 与凭据作用域，待实现期确定。
- **成组替换的启动校验形态**：`ValidateOnStart` 还是构造期检查，待实现期定。
- **脚本层结果对象命名**（`VBScriptResult` / `VBScriptDiagnostic`）在实现期与 `spec` 对齐后定稿，避免与常见 DTO 撞名。
- **异常与并发相关的诊断族**：重入守卫的异常类型与消息、restore 首次触发的日志级别，实现期定。
- **会话长驻与取消残态**：取消后 faulted 的具体实现（半提交状态、`Reset` 后可用性）与长驻内存曲线未实测；实现期必须补测试，v1 发布前给结论。
- **`Reset` 新建 loader 的回收效果**：旧 loader 的程序集是否可被回收取决于引用与 GC，标「待定」，实现期用弱引用探针验证。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**。理由：缺口真实（公共出口与可分发包都不存在），机制已存在（两条 internal seam + 一条执行管线），属「把已有能力接到官方 Hosting」的延伸而非发明；与 D4 的分发类优先级同层，与 M5 零新摩擦。两路独立评审均为「附带条件支持」（VB 中高、C# 高），条件已逐条落进 RESOLUTION（依赖粒度、生命周期、契约面、默认引用集、身份检测、net472 闭包）。 **复会（2026-11-17）**维持 Active；条件写进 R3/R6/R9/R12/R17（session 自持 loader 与守卫、无状态入口复用一次提交实现、`PrintedValue` 不落输出、三态完整判定、Variables 去重、取消即 faulted、net472 依赖清单补 `Microsoft.Bcl.AsyncInterfaces`）。
- **进入实现规划**；net472 冒烟与默认集比对是发布前硬门。
