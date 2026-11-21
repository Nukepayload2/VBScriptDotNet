# 可嵌入的脚本宿主包 / Embeddable Scripting Host Package

> **状态来源**：本提案根据用户 2026-11-16 的定案撰写——目标场景是「用户在自己的 app 里集成 VBScript.NET，不引入 CLI」；API 允许模仿 Roslyn Scripting 的形状，但**命名空间不自称 Microsoft**；`#R "nuget:"` 行为默认与 `vbi` 一致且**可被用户整体替换**；`#R "project:"` **不进 v1**（当前未发布）；装配流程**对齐官方 .NET Hosting**（`Microsoft.Extensions.Hosting` / `IHostApplicationBuilder`），除非证明不适用。**一个包，脚本与宿主分属不同命名空间，程序集怎么切不限**（用户定案）。Roslyn 官方已把脚本 API 边缘化（见 Motivation §2），因此本提案**不承诺** Roslyn Scripting API 兼容性。

> **RESOLUTION Active**：见 `../meetings/meeting-embeddable-scripting-host.md`（2026-11-16，双老登评审；契约面/依赖/生命周期按 R1–R17 修正）。

* [x] Proposed
* [x] Prototype: Complete（任务包 `../tasks/embeddable-scripting-host/` F1–F15 done，未 commit）
* [x] Implementation: Complete（同一任务包；运行时包已打包验证，未 commit）
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

交付一个可分发的**运行时包**（暂名 `Nukepayload2.Compilers.VBScriptDotNet.Scripting`），让任意 .NET 应用在**进程内**托管 `.vbx` 脚本与 `.vb` 片段。装配完全走官方 Hosting 扩展：宿主用 `Host.CreateApplicationBuilder(args)` 建 `IHostApplicationBuilder`，本包提供 `builder.AddVBScripting(...)` 与 `IServiceCollection` 扩展，脚本执行通过容器里的 `IVBScriptRunner` 取得；配置、生命周期、日志、DI 全部复用 `Microsoft.Extensions.*`，不自造容器、不自造 host。

包内同时容纳两层公共命名空间——`Nukepayload2.VBScriptDotNet.Hosting`（宿主装配与服务契约）与 `Nukepayload2.VBScriptDotNet.Scripting`（脚本对象层）——以及 fork 的编译器与 Scripting 程序集；程序集边界不承载产品语义，用户按命名空间理解 API。

`vbi` 现有的执行管线（`.vbx`、`/check`、`#Load` 多树、`Print`/`Args`、`Return` 退出码）与 **NuGet 还原管线**都收敛为容器里**可替换的服务**：默认实现与 `vbi` 行为一致；NuGet 一段是三层契约（还原协调、还原执行、包解析），宿主按官方 Hosting 的习惯（`Replace` / `AddSingleton`）在 `Build()` 之前接进自己的实现；只换还原服务而保留默认解析层会在 runner 构造时被拒绝（见 §3）。

## Motivation
[motivation]: #motivation

### 1. 嵌入场景今天无路可走（已检查）

用户的应用要跑 `.vbx`，只有两条路：源码/项目引用自己拼宿主，或在进程里 spawn `vbi`。两条都不满足「作为库集成、可配置、可替换」。

现有执行管线在代码里是齐全的，但**公共出口只到「跑一段代码」为止**：

| 能力 | 现状锚点 | 公共可达性 |
|---|---|---|
| 创建/运行脚本对象 | `Scripting\VisualBasic\VisualBasicScript.vb:25-144` | 公开（`VisualBasicScript.Create/RunAsync/EvaluateAsync`） |
| `.vbx` 文件执行、`Return` → 退出码 | `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:238-269` | `CommandLineRunner` 是 `internal sealed`（`:24`） |
| 控制台 I/O | `Scripting\Core\Hosting\CommandLine\ConsoleIO.cs:13` | `internal class` |
| 交互入口 | `VisualBasicScript.vb:146-182` | 公开，但硬绑 `ConsoleIO.Default`（`:157`）与 vbi 的 rsp（`:150`），等价于 CLI 本体 |
| 脚本编译器 / `#Load` 展开 | `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:18`（`Friend`）、`:117`（`CollectLoadTrees`） | 不可达 |

### 2. Roslyn 官方已弃这条路由，兼容包袱不存在（已检查）

`proposal-vbi-nuget-reference.md:30-41` 记录了调查期线上取证：.NET Interactive/Polyglot 2026-04 弃用并点名 **file-based apps** 为继任；csx 被官方排除；csi 无新 REPL、进 CLI 未排期；VB 无 file-based 继任（"can opt in later"）。同文 `:43-45` 判断「上游把 Scripting 边缘化后，fork 该层的 merge 冲突成本趋零」。

立场：**形状可以借，契约不必背**。脚本对象层照 Roslyn Scripting 的用法习惯（`RunAsync` / `EvaluateAsync` / `CompileAsync`）设计，但放在自己的命名空间，并明确不保证与 `Microsoft.CodeAnalysis.Scripting` 源码兼容。

### 3. 需要包，但今天的包不可发布（已检查）

| 事实 | 锚点 |
|---|---|
| Scripting/Core 声明 `IsPackable`，但 `PackageId` 是上游名字 `Microsoft.CodeAnalysis.Scripting.Common` | `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:13-14` |
| VB Scripting 项目没有 `IsPackable` | `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:1-12` |
| 两库都经 `ProjectReference` 直接引用 fork 编译器 → 直接打包会把依赖解析成上游 `Microsoft.CodeAnalysis` 包 | 同文件 `:28-29`、`Scripting\Core\...csproj:79` |
| 版本线不一致：VB Scripting 写 `2.0.0` + `RC3`，Scripting/Core 继承 `5.9.0` | `...VisualBasic.Scripting.vbproj:8-9`；`Directory.Build.props:6` |

### 4. NuGet 行为已经是「缝」，只是没有公共出口（已检查）

`vbi` 的 NuGet 支持被拆成两个 internal seam，且已经通过 `CommandLineRunner` 构造参数注入：

| Seam | 锚点 | 语义 |
|---|---|---|
| `INuGetRestoreCoordinator` | `Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs:12-33` | 编译前预扫描、还原、返回锚在 `#R` 上的阻塞诊断 |
| `NuGetPackageResolver`（抽象） | `Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:12-51` | 同步解析前端（`nuget:` 前缀、逗号语法） |
| 默认实现 | `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb:34`、`NuGetPackageResolverImpl.vb:15`、会话缓存 `NuGetPackageSession.vb` | 临时工程 + `dotnet restore` + 读 assets |

「默认同 `vbi`、可替换」不需要新机制，只需要把这两条缝提升为公共契约并接进 DI。

### 5. 用户定案（2026-11-16）

- 场景：应用内集成，**不引入 CLI**。
- 装配：**对齐官方 .NET Hosting 扩展**（`Microsoft.Extensions.Hosting`），不为「少一个依赖」另造装配模型；确实不适用时才偏离，并须写出理由。
- 打包：**一个包**；脚本层与宿主层用**不同命名空间**区分；程序集切分随意。
- 目标框架：**`net10.0` + `net472`**（与仓库既有 Toolset 包/产品线的双目标约定一致）。
- 并发：**不承诺线程安全**——与 `vbi` 的单会话、顺序执行一致，文档写明。
- NuGet：默认行为与 `vbi` 相同（联网 + `NuGet.config` 源，与 `dotnet restore` 默认一致），**用户可替换**；离线/禁用由成对替换还原服务与包解析两层实现，不另造开关。
- `#R "project:"` **不进 v1**（用户定案 2026-11-16）：它当前只是提案，等它成为产品能力后再评估（见 §5）。

## Detailed design
[design]: #detailed-design

### 1. 包、命名空间与依赖

| 项 | 设计 |
|---|---|
| 包 ID | `Nukepayload2.Compilers.VBScriptDotNet.Scripting`（已定案） |
| 程序集 | 不承载产品语义：门面程序集（可 1~2 个）+ fork 的 `Microsoft.CodeAnalysis.dll` / `Microsoft.CodeAnalysis.VisualBasic.dll` / `Microsoft.CodeAnalysis.Scripting.dll` / `Microsoft.CodeAnalysis.VisualBasic.Scripting.dll` |
| 公共命名空间 | `Nukepayload2.VBScriptDotNet.Hosting`（装配扩展、服务契约、`VBScriptOptions`、结果/诊断/会话类型）、`Nukepayload2.VBScriptDotNet.Scripting`（无容器的一次性门面 `VBScript`） |
| TFM | `net10.0` + `net472`（已定案；net472 走 netstandard2.0 编译器/脚本库 + `Microsoft.Extensions.Hosting.Abstractions` 的 `net462`/`netstandard2.0` 资产，本地缓存 `10.0.0` 已核对） |
| 托管依赖 | 只有 Abstractions：`Hosting.Abstractions`、`DependencyInjection.Abstractions`、`Options`、`Options.ConfigurationExtensions`、`Logging.Abstractions`、`Configuration.Abstractions`（均 10.0.0）。容器、配置、日志与 `IHostApplicationLifetime` 由宿主提供；不引 `Microsoft.Extensions.Hosting` 本体，不引第三方容器 |
| 编译器依赖 | 直接铺 `lib/net10.0` 与 `lib/net472`（net472 用 netstandard2.0 编译器程序集，自包含）。已定案：Toolset 包只有 `tasks/`、无 `lib/` 资产，依赖它无法给应用提供编译期引用 |
| 版本 | 与编译器包、CLI 包同轨（已定案：三包同为 `2.0.0-RCx`；内部编译器程序集仍 5.9.0） |

> 边界：`Microsoft.CodeAnalysis.*` 程序集仍随包分发，其类型在进程内必然可见；「命名空间不自称 Microsoft」约束的是**我们承诺与文档化的契约**（`Nukepayload2.VBScriptDotNet.*`），不是把上游类型藏起来（做不到，见 Drawbacks 3）。`Nukepayload2` 根保证命名空间无碰撞；`DotNet` 是产品名「VBScript.NET」的标准扁平化（`VBScriptNET` 更丑；单用 `VBScript` 会与经典 WSH VBScript 相混）。**包 ID 是独立决定，不必与命名空间同形**。

### 2. 装配：走官方 Hosting，不自造 host

宿主侧只加两组扩展方法：`AddVBScripting(this IHostApplicationBuilder, Action<VBScriptOptions>?)` 与 `AddVBScripting(this IServiceCollection, Action<VBScriptOptions>?)`。前者顺带绑定 `VBScript` 配置节；后者用于已经持有 `IServiceCollection` 的宿主（ASP.NET Core、Worker、测试）。

```vb
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.DependencyInjection.Extensions
Imports Microsoft.Extensions.Hosting
Imports Nukepayload2.VBScriptDotNet.Hosting

Dim builder = Host.CreateApplicationBuilder(args)

' 官方 Hosting 习惯：先注册自己的服务，再让本包补齐缺省
builder.Services.AddSingleton(Of IMyApi)(New MyApi())

builder.AddVBScripting(Sub(options)
                           options.Imports.Add("System")
                           options.GlobalsType = GetType(MyGlobals)
                       End Sub)

' 需要自定义 NuGet 行为时：成组取默认三段，再成对替换服务层与解析层
' （只换 INuGetRestoreService 而保留默认解析层，会在 runner 构造时抛异常）
builder.Services.AddVBScriptNuGetRestore()
builder.Services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService, MyOfflineRestoreService)())
builder.Services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver, MyPackageResolver)())

Using host = builder.Build()
    Await host.StartAsync()

    Dim runner = host.Services.GetRequiredService(Of IVBScriptRunner)()
    Dim result = Await runner.RunFileAsync("task.vbx", {"a", "b"})
    Console.WriteLine($"exit={result.ExitCode}")

    Await host.StopAsync()
End Using
```

约定：

- **注册**：`AddVBScripting` 对每个默认实现用 `TryAddSingleton` / `TryAddTransient`（options、`IScriptOutput`、`IVBScriptRunner`、`IVBScriptSessionFactory`、NuGet 三层），已有注册优先；后注册者在解析时胜出（官方 DI 的最后注册规则），也可用 `services.Replace(...)` 显式替换。NuGet 三层必须成组替换：换服务层时同时换解析层，或先 `AddVBScriptNuGetRestore()` 再只换解析层——只换服务层会被 runner 的构造校验拒绝（见 §3）。
- **配置**：`VBScriptOptions` 走 `IOptions(Of VBScriptOptions)`；`AddVBScripting(IHostApplicationBuilder)` 自动绑定 `VBScript` 节，也可在回调里直接赋值。
- **生命周期**：`IVBScriptRunner` 注册为 **transient**，一次解析服务一个工作单元，没有「随 `IHost` 启停」的语义；`IVBScriptSessionFactory` 是单例，它创建的 session 自持生命周期（窗口/面板，不进容器）。`host.StartAsync()` 之后即可执行脚本；宿主进入 `ApplicationStopping` 后拒绝启动新调用（抛 `VBScriptHostException`）。`IHostedService` 只在需要「启动即跑某脚本」的宿主里由用户自建，本包不强制。
- **日志**：宿主级诊断走 `ILogger(Of VBScriptRunner)`（实现类型作分类）；脚本里的 `Print` 与编译诊断走 `IScriptOutput`——默认 `NullScriptOutput` 不碰 stdout/stderr，要写控制台须显式 `UseConsoleOutput()`（换成 `ConsoleScriptOutput`），也可注册 `LoggerScriptOutput` 转到日志。
- **异步**：`RunFileAsync`/`RunCodeAsync`/`EvaluateAsync` 全部带 `CancellationToken`；`IVBScriptRunner` 不承诺线程安全——同一个 runner 的上一次调用未完成时再次调用抛 `VBScriptBusyException`，不会静默串行化；并发场景让每个 worker 解析自己的 runner（每次执行各持独立程序集加载器）。

**明确不自造的部分**：不定义自己的 `IHost`/`IHostBuilder`/容器、不复制 `Microsoft.Extensions.Hosting` 的启动流程；只在官方接口上做扩展。

### 3. 服务契约（替换点）

| 契约 | 默认实现 | 对应 `vbi` 行为 |
|---|---|---|
| `IVBScriptRunner` | `VBScriptRunner`（internal） | `.vbx` / `/check` / 片段执行、结果与退出码 |
| `IVBScriptSessionFactory` | `VBScriptSessionFactory`（internal） | 有状态提交链：会话 / `immediate window` 语义，不进容器 |
| `INuGetRestoreService` | `DotnetCliRestoreService`（internal） | 预扫描 `#R "nuget:"`（含 `#Load` 树）→ 内容寻址临时工程 → `dotnet restore` → 读 assets → 写会话缓存 |
| `INuGetRestoreRunner` | `DotNetRestoreRunnerAdapter`（internal） | 执行一次还原（spawn `dotnet restore`、读 assets），不解释诊断、不缓存 |
| `INuGetPackageResolver` | `SessionNuGetPackageResolver`（internal） | 编译期同步读取会话缓存（`nuget:` 前缀、逗号语法） |
| `IScriptOutput` | `NullScriptOutput`（`UseConsoleOutput()` 换成 `ConsoleScriptOutput`；另有 `LoggerScriptOutput`） | `ConsoleIO` 的 `Out`/`Error` 行为，需显式开启 |

globals 不是工厂契约：`VBScriptGlobals` 是公共基类（`Print`/`Args`），由 `VBScriptOptions.GlobalsType` 指定实现类型（R8）。

### 4. 默认 NuGet 行为 = `vbi`（不改语义）

沿用 `proposal-vbi-nuget-reference.md` 的 C1 引擎与「同步编译器 / 异步宿主环」约束：

1. 编译一个提交前，`IVBScriptRunner` 调用 `INuGetRestoreService.PrepareCompilationAsync(source, options, ct)`（`source` 是 `VBScriptSource`：文本 + 路径）；后者用与编译器同一个展开点扫描 `#Load` 可达树（`VisualBasicScriptCompiler.vb:117`，现为 `Friend`；v1 沿用 core、不为文件系统抽象开口，触发条件见「已定案」）。
2. 还原集合有增量 → 生成内容寻址临时工程 → `dotnet restore` → 读 `project.assets.json`，把 compile/runtime 资产写入 `INuGetPackageResolver` 的会话缓存与 `InteractiveAssemblyLoader`。
3. 编译期 `nuget:` 引用经 `INuGetPackageResolver` 同步命中；未命中时触发一次「还原 → 重编」（与 `vbi` 同）。
4. 还原失败产出锚在 `#R` 行的诊断，交 `IScriptOutput`。

**替换示例（离线源 / 自建缓存 / 直接禁用）**：

```vb
Imports System.Collections.Immutable
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.DependencyInjection.Extensions
Imports Nukepayload2.VBScriptDotNet.Hosting

NotInheritable Class MyRestoreService
    Implements INuGetRestoreService

    Public Function PrepareCompilationAsync(
        source As VBScriptSource,
        options As VBScriptOptions,
        ct As CancellationToken) _
        As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreService.PrepareCompilationAsync

        ' 自建私有源 / 本地缓存 / 直接返回空表示不提供 NuGet
        Return Task.FromResult(ImmutableArray(Of VBScriptDiagnostic).Empty)
    End Function
End Class

' 解析层必须与上面的服务层配对：读你的服务写下的状态
builder.Services.AddVBScriptNuGetRestore()
builder.Services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService, MyRestoreService)())
builder.Services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver, MyPackageResolver)())
```

**只换还原服务（保留默认解析层）会报错**：还原服务与包解析是同一契约的两半——服务写下包状态，解析层读它。只替换 `INuGetRestoreService` 而保留默认 `INuGetPackageResolver`，`VBScriptRunner` 在**构造时**抛 `VBScriptHostException`，消息里给出修复方向：要么只换解析层（先 `AddVBScriptNuGetRestore()` 取默认组，再替换 `INuGetPackageResolver`），要么服务层与解析层成对替换。校验发生在 runner 构造，不等到第一次 `#R "nuget:"` 才暴露。

### 5. 引用面现状与 v1 范围（调查结果，已检查）

| 引用形式 | 现状 | 证据 |
|---|---|---|
| `#R "路径/裸名"` | 已发布 | `README.md:41-47`、`README.zh-CN.md:41` |
| `#R "nuget:包, 版本"` | **已发布**（当前 `vbi` 支持，含 `NuGet.config` 源与凭据） | `README.md:21`、`Samples\CLAUDE.md:50-60`、`Samples\SqliteNuGetDemo.vbx:1-12`、`NuGetRestoreCoordinator.vb` + `NuGetPackageResolverImpl.vb` |
| `#R "project:<工程>"` | **未发布，且 v1 不含**（作者定案）：仅提案，Prototype / Implementation 均为 Not Started | `proposal-vbi-project-reference.md:8-9`（状态行）；全仓代码检索无 `project:` 前缀实现 |

因此「宿主默认行为 = `vbi`」在 v1 自然等价于「`nuget:` 可用且可替换」；`project:` **不进 v1**（用户定案 2026-11-16），等它成为产品能力后再评估。

### 6. 单一实现纪律：`vbi` 与嵌入共用一条执行管线

落点在 VB scripting 侧，不在 `Scripting\Core`：`Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb`（`ScriptRunnerCore` / `ScriptSessionCore(Of TReturn)`，唯一的编译、运行与退出码编排，输入是 `ScriptRunRequest`——源、路径、globals、loader、`CompileOnly`、基目录与 restore seam）与 `Scripting\VisualBasic\Hosting\CommandLine\ScriptCommandLineHost.vb`（`vbi` 的 `.vbx` / `/check` 适配器，`VisualBasicScript.RunInteractiveAsync` 在脚本执行形状下走它）。`Scripting\Core` 保持 HEAD 零改动：`CommandLineRunner` 原样保留，在 `vbi` 的调用路径上只承担交互 REPL。宿主门面经 VB scripting 的 `InternalsVisibleTo` 用同一 core，`vbi` 的 CLI 行为不变。

理由：`#Load` 展开、`#R` 预扫描、退出码语义、诊断汇报分散时，两套实现必然漂移（`proposal-load-directive.md:32` 已把「展开逻辑单点、编译器与宿主不得各写一份」写成规范级理由）。

### 7. 不做什么

- 不做 `Microsoft.CodeAnalysis.Scripting` 的源码/二进制兼容承诺。
- 不自造 Host/容器/配置系统（官方 Hosting 已覆盖）。
- 不在 v1 提供嵌入 REPL（交互仍归 `vbi`）。
- 不提供 C# scripting 包。
- 不保证与上游 Roslyn NuGet 包在同一进程共存（Drawbacks 2）。

## Drawbacks
[drawbacks]: #drawbacks

1. **依赖官方 Hosting 的 Abstractions 层**。包从「纯编译器」变成「带 Hosting 契约依赖的运行时」。这是用户定案的取舍；代价是引入 6 个 `Microsoft.Extensions.*` 包（`Hosting.Abstractions`、`DependencyInjection.Abstractions`、`Options`、`Options.ConfigurationExtensions`、`Logging.Abstractions`、`Configuration.Abstractions`），好处是与现代 app 的装配习惯零摩擦，且不再维护自造容器。
2. **程序集身份冲突**。包内 `Microsoft.CodeAnalysis*.dll` 与上游 NuGet 同名同公钥（`Directory.Build.props:24-26` 的 public-sign 设定）。默认 `AssemblyLoadContext` 按简单名绑定，同一进程里同名程序集只能加载一份 → 引了本包的应用不能再引上游 Roslyn 包（否则类型身份/版本绑定冲突）。必须在 README 写死这条。
3. **「命名空间不自称 Microsoft」只能约束契约面**。上游类型随程序集分发、必然可见；彻底隐藏需改名 fork（程序集名 + 命名空间 + IVT + 序列化形状），成本与 merge 面都不可接受。
4. **维护面转移**。Roslyn 已不再投入 Scripting（Motivation 2），这条管线以后由本仓独自维护；好处是 merge 冲突低，坏处是没有上游修复。
5. **NuGet 默认实现有副作用**。spawn `dotnet`、写临时工程、联网、依赖 SDK 版本。默认行为与 `dotnet restore` 一致（联网）；运行在受限环境（服务端沙箱、CI、无 SDK 机器）时，宿主须成对替换 `INuGetRestoreService` 与 `INuGetPackageResolver` 来关闭或改源——代价是要自己实现这两层契约，而包不另造开关。
6. **并发与生命周期**。长驻宿主反复跑脚本会累积内存：脚本程序集能否卸载由宿主选择的 `AssemblyLoadContext` 决定，库不承诺回收，`Reset` / `DisposeAsync` 只清会话状态并换新加载器（`proposal-blazor-wasm-repl.md:149` 也只要「需内存压测」，无实测结论）。`IVBScriptRunner` 注册为 transient 且不承诺线程安全：同一 runner 并发调用抛 `VBScriptBusyException`，并发场景由宿主为每个 worker 各解析一个 runner。
7. **包体积**。编译器两个程序集为主；本包 Release 产物 `Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg` = 10,155,746 字节（128 条目，任务包 F9 打包实测）；同源 Toolset 包 `Nukepayload2.Compilers.VBScriptDotNet.2.0.0-RC3.nupkg` = 12,245,623 字节（约 11.68 MiB），只作量级参照，不是本包口径。

## Alternatives
[alternatives]: #alternatives

- **A. 只发程序集、不给宿主 API**（把 `VisualBasicScript.RunAsync` + README 配方当产品）。成本最低，但 `.vbx` 文件执行、`Print`/`Args`、退出码、NuGet 全要用户自己拼，且契约不稳。否决为主方案，保留为「v0 应急」。
- **B. 自造 Host/容器**（如自定义 `VBScriptHostBuilder` + 私有 DI）。零官方依赖，但要重造注册/生命周期/配置/覆盖规则，且与用户既有 Host 无法组合。**用户已否决**。
- **C. 只做 `IServiceCollection` 扩展、不提供 `IHostApplicationBuilder` 重载**。可行但少了配置绑定与「`builder.AddVBScripting()` 一行接入」的体验；作为子集保留（不是开放问题：设计上两个重载都提供）。
- **D. CLI 子进程**。用户明确排除；进程边界还会让 globals/返回值传递变复杂。
- **E. 改名 fork Roslyn Scripting（程序集 + 命名空间全换）**。能满足「命名空间无 Microsoft」，但 IVT、程序集身份、公共形状、与上游 merge 全线受影响。否决。
- **F. 不做（维持源码引用）**。用户场景无法满足。

## Unresolved questions
[unresolved]: #unresolved-questions

> 收录标准：每条必须是**当前仍要裁决、且现有证据不足以下结论**的问题。已被作者定案、或证据已能闭合的，写入本节末尾「已定案」记录，不占编号。

当前没有未决项。

### 已定案（记录，不进 Unresolved）

| 决定 | 依据 |
|---|---|
| 脚本层 v1 API 面 = 两个入口：无状态 `VBScript.RunAsync` / `EvaluateAsync` / `CompileAsync`，与 `IVBScriptSessionFactory.CreateSession(options)` → `IVBScriptSession` 的会话链；不给 `ContinueWith` / `ScriptState` 文档承诺 | RESOLUTION R6；公共 API 里没有 `ScriptState`，也没有 `ContinueWith` 入口 |
| 包 ID `Nukepayload2.Compilers.VBScriptDotNet.Scripting` | 与 Toolset/CLI 家族前缀一致；包内自带编译器，`Compilers` 不算误标 |
| 命名空间 `Nukepayload2.VBScriptDotNet.Hosting` / `.Scripting` | `Nukepayload2` 根无碰撞；`DotNet` 是「VBScript.NET」的标准扁平化；单用 `VBScript` 会与经典 WSH VBScript 相混（作者评估） |
| 一个包；脚本层与宿主层分命名空间；程序集切分随意 | 作者 2026-11-16 定案 |
| 装配走官方 `Microsoft.Extensions.Hosting`，不自造 host/容器 | 作者 2026-11-16 定案 |
| 编译器程序集随包自包含（铺 `lib/net10.0` + `lib/net472`） | Toolset 包只有 `tasks/`、无 `lib/` 资产，依赖它无法提供编译期引用 |
| 目标框架 `net10.0` + `net472` | 仓库既有 Toolset 包/产品线的双目标约定；net472 由 netstandard2.0 编译器/脚本库覆盖（`vbi` 已有 net48 宿主使用同一 Scripting 栈，`Interactive\vbi\vbi.vbproj:7`），`Microsoft.Extensions.Hosting.Abstractions` 也提供 `net462`/`netstandard2.0` 资产（本地缓存 `10.0.0` 已核对） |
| 不承诺线程安全 | 与 `vbi` 的单会话、顺序执行一致；同一 runner 并发调用抛 `VBScriptBusyException`，并发场景每个 worker 各解析一个 runner |
| NuGet 默认 = `dotnet restore` 默认（联网 + `NuGet.config` 源） | 与 CLI 默认行为一致；离线/禁用由成对替换还原服务与包解析两层实现，不另造开关 |
| 三包版本同轨 `2.0.0-RCx`（内部编译器程序集仍 5.9.0） | 现有约定：Toolset 包与 CLI 包均为 `2.0.0-RC3`，VB Scripting 项目亦写 `2.0.0`+`RC3` |
| `#R "project:"` 不进 v1 | 作者 2026-11-16 定案；该能力当前仅提案（`proposal-vbi-project-reference.md:7-10` 三项 `Not Started`），v1 引用面 = `nuget:`（已发布）+ 路径/裸名 `#R` |
| v1 不为 `#Load` 开 `ISourceTreeProvider` 抽象 | YAGNI：v1 宿主从真实文件系统加载；触发升级条件 = 出现内存脚本/虚拟文件系统宿主（Blazor、测试替身）且 `SourceFileResolver` 不再够用 |
| `vbi` 行为一致性作为 v1 验收门（不是开放问题） | 已定「宿主与 CLI 同语义」；逐项矩阵属测试计划（`test-plan.md`）内容，在 plan 阶段落 |
| 嵌入 REPL 不进 v1 | 交互仍归 `vbi`；宿主包只提供非交互执行 |

## 相关文档
[related]: #related

- `proposal-distribute-compiler-nuget-package-and-dotnet-tool.md` — Toolset 编译器包与 CLI 包的分发布局（本提案是其第三条通道）。
- `proposal-vbi-nuget-reference.md` — `#R "nuget:"` 的引擎/接线/证据基线（本提案复用其 C1 引擎与 seam 设计）。
- `proposal-vbi-project-reference.md` — `#R "project:"` 的宿主驱动模型（当前未实现；v1 不含，见 §5）。
- `proposal-blazor-wasm-repl.md` — 宿主缝与受限环境清单（引用程序集注入、`Assembly.Location` 假设、程序集不可卸载）。
- `proposal-load-directive.md` — `#Load` 的展开点单一纪律（本文 §6 的依据）。
