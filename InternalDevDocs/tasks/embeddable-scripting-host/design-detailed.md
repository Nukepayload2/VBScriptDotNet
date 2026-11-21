# 详细设计：可嵌入脚本宿主包

> 依据：`../../meetings/meeting-embeddable-scripting-host.md` RESOLUTION R1–R17 与 `design-overview.md`。本文逐条给出**改动文件 + 改动形状 + 源码锚点**，实施者可直接照做。锚点为 2026-11-18 重核读数（F5–F15 落地后）；漂移以符号名与语义为准。

## G1. facade 工程（新建）

`Scripting\Hosting\Nukepayload2.VBScriptDotNet.Hosting.vbproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net472</TargetFrameworks>
    <RootNamespace></RootNamespace>
    <SignAssembly>true</SignAssembly>
    <PublicSign>true</PublicSign>
    <AssemblyOriginatorKeyFile>..\..\Compilers\MicrosoftSharedPublicKey.snk</AssemblyOriginatorKeyFile>
    <IsTrimmable>false</IsTrimmable>
    <VersionPrefix>2.0.0</VersionPrefix>
    <VersionSuffix>RC3</VersionSuffix>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj"
                      SetTargetFramework="TargetFramework=net10.0"
                      Condition="'$(TargetFramework)' == 'net10.0'" />
    <ProjectReference Include="..\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj"
                      SetTargetFramework="TargetFramework=netstandard2.0"
                      Condition="'$(TargetFramework)' == 'net472'" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="..\..\Interactive\vbi\vbi.coreclr.rsp"
                      LogicalName="Nukepayload2.VBScriptDotNet.Hosting.vbi.coreclr.rsp" />
  </ItemGroup>
  <ItemGroup>
    <PublicAPI Include="PublicAPI.Shipped.txt" />
    <PublicAPI Include="PublicAPI.Unshipped.txt" />
  </ItemGroup>
</Project>
```

要点：

- **签名**：与 `vbi` 同法用共享密钥 public-sign（`Directory.Build.props:24-26` 的同一把 key，文件 `Compilers\MicrosoftSharedPublicKey.snk`），以便 `InternalsVisibleTo` 能匹配强名。
- **net472 分支**：`ProjectReference` 的 `SetTargetFramework` 让 net472 外层引用脚本库的 `netstandard2.0` 构建；编译器程序集同理由传递引用带入。
- **不引 `Microsoft.CodeAnalysis.*` 包**：编译器程序集在打包时铺 `lib/`，不写成 NuGet 依赖。
- **嵌入 rsp**：默认集随包走，避免运行期读仓库文件。
- **文件头与台账**：新源文件沿用仓库许可头（`Scripting\VisualBasicTest\InteractiveSessionTests.vb:1-3` 同款注释）；公共 API 记入 `PublicAPI.Shipped.txt` / `Unshipped.txt`（同 `Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:32-35` 的 `<PublicAPI>` 惯例）。

facade 源文件（全部 VB，`RootNamespace` 为空，命名空间显式写全）：

| 文件 | 内容 |
|---|---|
| `Hosting\VBScriptingServiceCollectionExtensions.vb` | `AddVBScripting`（两个重载）、`AddVBScriptNuGetRestore`、`UseConsoleOutput` |
| `Hosting\VBScriptOptions.vb` | 选项（不含 Friend 槽；restore 会话由 DI 单例承载，loader 由 `ScriptRunRequest` 携带） |
| `Hosting\VBScriptRunner.vb` | `Friend NotInheritable Class VBScriptRunner Implements IVBScriptRunner` |
| `Hosting\IVBScriptRunner.vb` | 公共接口 |
| `Hosting\VBScriptResult.vb` / `VBScriptDiagnostic.vb` / `VBScriptSource.vb` | 结果 DTO |
| `Hosting\VBScriptGlobals.vb` | `Print` / `Args` 基类 |
| `Hosting\IScriptOutput.vb` + `NullScriptOutput.vb` / `ConsoleScriptOutput.vb` / `LoggerScriptOutput.vb` / `CapturingScriptOutput.vb` | 输出路由（`CapturingScriptOutput` internal，一次性入口捕获 `Print`） |
| `Hosting\NuGet\INuGetRestoreService.vb` / `INuGetRestoreRunner.vb` / `INuGetPackageResolver.vb` / `NuGetRestoreContext.vb` | 三层 seam + 上下文 |
| `Hosting\NuGet\DotnetCliRestoreService.vb` / `DotNetRestoreRunnerAdapter.vb` / `SessionNuGetPackageResolver.vb` | 默认实现（内部委派 Friend 类型） |
| `Hosting\Exceptions\*.vb` | `VBScriptHostException` / `VBScriptBusyException` / `VBScriptScriptNotFoundException` |
| `Scripting\VBScript.vb` | 静态门面（`Nukepayload2.VBScriptDotNet.Scripting`） |
| `Hosting\VBScriptDefaults.vb` | 内嵌 rsp 解析器 |
| `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` | 公共 API 台账（与 `Scripting\VisualBasic` 既有惯例一致） |

`InternalsVisibleTo`：只加在 `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj`（`:20`），公共 scripting 程序集（`Scripting\Core`）不加——facade 不需要它的内部类型。

```xml
<InternalsVisibleTo Include="Nukepayload2.VBScriptDotNet.Hosting" />
```

`Directory.Build.props:34-38` 的 `ItemDefinitionGroup` 会为签名工程自动带上共享公钥，与 facade 的 public-sign 匹配。

## G2. 抽 `ScriptRunnerCore`（等价重构，F5）

**新文件** `Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb`（`Friend`，VB；命名空间 `Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting`）：

> 位置：引擎在 **VB scripting**，`Scripting\Core` 相对 HEAD 零 diff（本任务未在该程序集新增 seam/IVT）。理由是公共 scripting 程序集是其它 Roslyn 使用者也会加载的共享基础设施，本仓产品功能不得承载其中；VB scripting 已持有 VB 脚本编译器且经 IVT 能看到编译器内部，足够实现同一条管线。因此 `CommandLineRunner` **不再修改**：vbi 在 `VisualBasicScript.RunInteractiveAsync` 分流，文件脚本/`/check` 走 VB 引擎（`ScriptCommandLineHost` 适配），交互 REPL 仍由 vendored `CommandLineRunner` 承担（只调用、不修改）。facade 经 VB scripting 的 `InternalsVisibleTo` 访问引擎与其 Friend 适配器。

```vb
Friend NotInheritable Class ScriptRunRequest
    Public Property Compiler As VisualBasicScriptCompiler      ' 引擎只在 VB scripting 内使用
    Public Property Source As SourceText
    Public Property FilePath As String
    Public Property ScriptOptions As ScriptOptions
    Public Property Globals As Object                          ' 必须非空；vbi 传 CommandLineScriptGlobals
    Public Property AssemblyLoader As InteractiveAssemblyLoader ' 每次执行/每 session 独立
    Public Property CompileOnly As Boolean
    Public Property BaseDirectory As String                     ' #Load / 相对引用的基目录（vbi 传编译器基目录，宿主传 WorkingDirectory）
    Public Property RestoreCoordinator As IScriptRestoreCoordinator              ' 可空；VB scripting 内的宿主缝
    Public Property RestoreDiagnosticsReporter As Func(Of ImmutableArray(Of Diagnostic), Boolean) ' 可空；预扫描诊断按原顺序打印的落点
End Class

Friend NotInheritable Class ScriptRunOutcome
    Public Property ExitCode As Integer
    Public Property Diagnostics As ImmutableArray(Of Diagnostic)
    Public Property RestoreDiagnostics As ImmutableArray(Of Diagnostic)   ' reporter 为 Nothing 时作为数据返回
    Public Property ReturnValue As Object
    Public Property Exception As Exception                      ' 运行期异常（vbi 用于打印并取 HResult）
End Class

Friend NotInheritable Class ScriptRunnerCore
    Public Shared ReadOnly Instance As ScriptRunnerCore
    Public Function RunAsync(request, ct) As Task(Of ScriptRunOutcome)   ' 建会话 → 一次提交 → 映射 → dispose
    Public Function CreateSession(request) As ScriptSessionCore(Of Object)
    Public Function CreateSession(Of TReturn)(request) As ScriptSessionCore(Of TReturn)
End Class
```

`RunAsync` 不含自己的执行编排：它只校验请求、建 `ScriptSessionCore(Of Integer)`、`SubmitAsync` 一次、把 `ScriptSubmissionOutcome` 映射成 `ScriptRunOutcome`、`Finally` 释放。预扫描、`Compile`、`RunAsync`/`RunFromAsync`、异常捕获只在 `ScriptSessionCore.SubmitAsync` 一处。

`ScriptSessionCore(Of TReturn)` 才是唯一编排者；`SubmitAsync` 的步骤（逐条对应旧 `CommandLineRunner.RunScriptAsync` 与预扫描）：

1. `request.RestoreCoordinator` 非空 → `PrepareCompilationAsync(code, options.FilePath, options, ct)`；有错误时经 `RestoreDiagnosticsReporter` 在原位置打印并阻塞提交、不杀会话（`Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb` 的提交路径；旧实现在 `CommandLineRunner.cs:154-164`、`:230-236`，现按符号名定位，行号以工作树为准）。
2. `CreateInitialScript(Of TReturn)`（首个提交，vbi 用 `Integer`、会话用 `Object`）或 `ContinueWith`（后续提交）→ `Compile(ct)` 取诊断（`ScriptRunnerCore.vb` 的提交路径；按符号名定位）。
3. 编译有错误 → diagnostics + `ExitCode = 1`，**不刷新 options**（同上，即旧 `CompilationErrorException` 分支的语义）。
4. `CompileOnly` → 直接返回诊断退出码，不推进状态、不刷新 options（`/check`；同上）。
5. 否则 `RunAsync(globals, catchException:=…)`/`RunFromAsync`：异常进 `Exception` 不抛；取消置 faulted 并外抛（同上）。
6. 成功后刷新 options：先 `RemoveImportsAndReferences`（`CommandLineHelpers.cs:15`）再按 `Previous` 链 replay imports（`VisualBasicScriptCompiler.vb` 的 `AddPreviousSubmissionImports`，由 `Private` 改为 `Friend`；**不**在公共 scripting 程序集新增虚方法 seam），基目录取 `request.BaseDirectory` 而非进程 CWD。

`ScriptSubmissionOutcome`：`PrintedValue` 由 `HasReturnValue`（`Script.cs:307-310`）决定，仅当「成功且无异常」时填；`IsFaulted` 只在取消后为真（运行异常后可继续，R17②；R17⑧ 的最终粒度见 F13）。

**改** `Scripting\VisualBasic\VisualBasicScript.vb` 的 `RunInteractiveAsync`：构造完 `VisualBasicInteractiveCompiler` 后按 `Arguments.Check OrElse Not Arguments.InteractiveMode` 分流——文件脚本/`/check` 组 `ScriptRunRequest` → `ScriptRunnerCore.Instance.RunAsync` → 打印诊断/异常 → 返回退出码（`Scripting\VisualBasic\Hosting\CommandLine\ScriptCommandLineHost.vb` 承担旧 `RunInteractiveCoreAsync` 的前半段：显示开关、读源、`ScriptOptions`、参数诊断）；其余仍 `New CommandLineRunner(...)` 调 `RunInteractiveAsync()`。**不改** `Scripting\Core`。

**验收**：`Scripting\VisualBasicTest -automated` 全绿（实测 814/0，改前改后不变）；`vbi` 对 `Samples\FileArgs.vbx`、`Samples\SqliteNuGetDemo.vbx`、`#Load` + `Return 3`、`/check` 的 stdout 与退出码逐字节一致（F5 门，已实跑）。

**已知残差（F5 实测，接受）**：运行期异常经 `catchException` 在 `ScriptExecutionState` 内捕获，异常不再穿过 `Script(Of T).RunSubmissionsAsync` 的 await 边界，故 `Exception.StackTrace` 少两条宿主 async 帧（消息、脚本帧、`HResult`、退出码不变）。这是「真实搬移 + 捕获式异常」的固有结果，不写格式补偿。

**移交 F7**：编译/执行器构造阶段由非用户代码抛出的非 `OperationCanceledException` 异常，旧 `catch(Exception)` 会格式化并返回 `HResult`，新路径会外抛；CLI 有效输入下不可达，facade 的 runner 必须在入口统一映射为 `VBScriptResult.Exception` + 退出码。

## G3. 选项与默认集

`VBScriptOptions`（公共）：

```vb
Public Property Imports As IList(Of String)          ' 初始为空 → 由默认集填充
Public Property References As IList(Of String)       ' 同上
Public Property WorkingDirectory As String
Public Property ScriptArguments As IReadOnlyList(Of String)
Public Property FileEncoding As Encoding
Public Property EmitDebugInformation As Boolean = True
Public Property OptimizationLevel As VBScriptOptimizationLevel = VBScriptOptimizationLevel.Debug
Public Property Globals As Object                    ' 与 GlobalsType 二选一
Public Property GlobalsType As Type
Public Property UseDesktopReferences As Boolean
Public ReadOnly Property RestoreContext As NuGetRestoreContext   ' 由 facade 填
```

默认集（`VBScriptDefaults`）：

- 读嵌入资源 `vbi.coreclr.rsp`；逐行匹配 `^/r:(.+)$` → `References`、`^/imports:(.+)$` → `Imports`；忽略 `/nostdlib`、`/vbruntime-` 与空行/注释。
- 首次 `AddVBScripting` 时把默认集填进 `VBScriptOptions`（若用户已加则追加去重，`OrdinalIgnoreCase`）。
- `UseDesktopReferences=True` 时追加 `System.Drawing`、`System.Windows.Forms`（来源 `vbi.desktop.rsp`，仅 net472 生效）。
- 单测：解析结果与 `Interactive\vbi\vbi.coreclr.rsp` 的 `/r:`/`/imports:` 行集合逐项相等。
- `UseDesktopReferences` 只在 net472 生效；net10 忽略并记一条 `ILogger` Warning（对应 T-DEF-4）。
- 空白项（`""`、空白字符串、`Nothing`）**不被**默认集合并步骤静默剔除：合并只做「追加默认集 + 大小写不敏感去重」，空白项留给 `VBScriptOptionsValidator` 报配置错误（F7 收口）。编译器投影（`VBScriptHostInternals.CreateScriptOptions`）仍会过滤空白项——否则静态门面（无 validator）拿到的空 import 会让编译器抛 `ArgumentException`；过滤掉即与 F6 行为一致。
- `Globals`（实例）与 `GlobalsType`（类型）二选一：会话**只接受实例**（R17① 的同一个实例跨提交），一次性入口可用类型。

## G4. runner 实现

`VBScriptRunner`（`Friend`，transient）：

1. 构造 `ScriptOptions`：按 `CommandLineRunner.cs:170-197` 的形状——`filePath`、`references`（经 `RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver` 解析，工厂同 `:199-210`）、`namespaces`（options.Imports）、`metadataResolver`、`sourceResolver`（`CommonCompiler.LoggingSourceFileResolver` 同形，`:212-215`）、`emitDebugInformation`、`optimizationLevel`、`parseOptions`（`VisualBasicParseOptions`，`Option Strict Off` 与脚本方言一致）。
2. 默认 globals：未给 `Globals` 时构造 `VBScriptGlobals`（输出走 `IScriptOutput.Out`）；`GlobalsType` 必须派生自 `VBScriptGlobals`，否则抛 `VBScriptHostException`。
3. 每次调用新建 `InteractiveAssemblyLoader`（实现 `IDisposable`），传入 `ScriptRunRequest`，`Finally` 释放；不与其它调用或 session 共享（R3）。
4. 调用 `ScriptRunnerCore.Instance.RunAsync`（带 `RestoreCoordinator` 适配器）。 无状态路径内部即「一次提交的 session」，不另起一套执行逻辑。
5. 映射 `ScriptRunOutcome` → `VBScriptResult`：诊断转 `VBScriptDiagnostic`；`Exception` 透传；`Output` 取本次 `CapturingScriptOutput` 的缓冲文本（runner 回填，core 不感知输出）；session 不缓冲，`Print` 直接写配置的 `IScriptOutput`。
6. 重入守卫：`Interlocked.CompareExchange` 抢占实例字段；占用时抛 `VBScriptBusyException`，`Finally` 释放。
7. 取消：`CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)`。

`CompileFileAsync`/`CompileCodeAsync` 走 `CompileOnly=True`；`EvaluateAsync(Of T)` 用 object 返回型会话（`CreateSession(Of Object)`，裸表达式才产出返回值，见 G7）。

实现注（与上文形状一致，补充实现细节）：

- 五个入口最终是 `ThrowIfHostStopping` → `Interlocked.CompareExchange` 重入守卫 → `CreateLinkedTokenSource(ct, lifetime.ApplicationStopping)` → 每次调用 `New InteractiveAssemblyLoader()`（`Finally` 释放）→ `ScriptRunnerCore.Instance.RunAsync`（或 `ScriptSessionCore(Of Object)` 的 `EvaluateAsync` 共享实现）→ `linked.Token.ThrowIfCancellationRequested()` → 映射结果。运行期被取消时，结果让位于 `OperationCanceledException`（一次性入口钉死为异常，T-RUN-9）。
- 一次性入口的输出：`TeeScriptOutput`（`Friend`）把本次 `Print` 同时写进 `CapturingScriptOutput`（→ `VBScriptResult.Output`）与容器里注册的 `IScriptOutput`（默认 `NullScriptOutput`，故默认不写 Console；`UseConsoleOutput()` 才写）。宿主自带的 `Globals` 实例仍自持输出，runner 不覆盖。
- `GlobalsType` 必须派生 `VBScriptGlobals` 且**类型可见性为 public**：提交程序集是独立动态程序集，`Friend` 的 globals 类型会在生成构造函数处抛 `TypeAccessException`（作为脚本失败数据返回）。这一条是 F7 实施新发现，写进 G8。
- `UseDesktopReferences` 的 net10 Warning 落在 runner 构造函数（每次解析 runner 一条），消息含选项名与替代做法。
- 结果映射：`ExitCode` 仍由原始 `Return` 值计算（vbi 契约：无 `Return` ⇒ 0，否则取 `(int)ReturnValue`），**不变**；`VBScriptResult.ReturnValue` 取 `ScriptSubmissionOutcome.PrintedValue`（按 `HasReturnValue` 规则：无 `Return` ⇒ `Nothing`，`Return 0` ⇒ boxed 0）。静态门面与 DI runner 共用这条映射，两边一致。

## G5. DI 注册与校验

`AddVBScripting` 的形状（两重载共用 `Configure` 实现）：

```vb
' 基础注册
services.AddOptions(Of VBScriptOptions)()                       ' PostConfigure 追加默认集
services.TryAddSingleton(Of IScriptOutput, NullScriptOutput)()
services.TryAddTransient(Of IVBScriptRunner, VBScriptRunner)()
services.TryAddEnumerable(ServiceDescriptor.Singleton(Of IValidateOptions(Of VBScriptOptions), VBScriptOptionsValidator)())
' NuGet 三层 + 成组注册（AddVBScriptNuGetRestore）
services.TryAddSingleton(Of NuGetPackageSession)()
services.TryAddSingleton(Of INuGetRestoreRunner, DotNetRestoreRunnerAdapter)()
services.TryAddSingleton(Of INuGetPackageResolver, SessionNuGetPackageResolver)()
services.TryAddSingleton(Of INuGetRestoreService, DotnetCliRestoreService)()
' F13（会话 API）
services.TryAddSingleton(Of IVBScriptSessionFactory, VBScriptSessionFactory)()
```

- 返回类型：`AddVBScripting(this IServiceCollection, …)` 返回 `IServiceCollection`，`AddVBScripting(this IHostApplicationBuilder, …)` 返回该 builder（支持链式，R2）。
- `IHostApplicationBuilder` 重载额外 `builder.Configuration.GetSection("VBScript")` 绑定并 `ValidateOnStart`。
- F7 实际注册：`AddOptions(Of VBScriptOptions)`（`PostConfigure` 里追加默认集，故宿主项永远在前）＋ `TryAddSingleton(Of IScriptOutput, NullScriptOutput)` ＋ `TryAddTransient(Of IVBScriptRunner, VBScriptRunner)` ＋ `TryAddEnumerable(IValidateOptions(Of VBScriptOptions))`；**NuGet 注册（`NuGetPackageSession` ＋ 三层契约）由 `AddVBScriptNuGetRestore()` 成组注册**，F7 的 runner 把 `RestoreCoordinator` 传 `Nothing`。
- 依赖：`IHostApplicationLifetime` 与 `ILogger(Of VBScriptRunner)` 由宿主提供（facade 不引全量 Hosting/Logging 包）；裸 `IServiceCollection` 场景需宿主自行注册 lifetime 与 logging，runner 在宿主停止后抛 `VBScriptHostException`。
- `AddVBScriptNuGetRestore`：显式注册三层默认（`Replace` 语义）；成组是否已注册由服务类型比较判定（不靠标记位），`VBScriptOptions` 上不留「已注册」标志。
- 校验：`VBScriptOptionsValidator` 检查 `GlobalsType` 派生、`WorkingDirectory` 存在、`Imports/References` 不含空项。
- 启动一致性：`VBScriptRunner` 构造时比较 `INuGetRestoreService` 与 `INuGetPackageResolver` 的实际类型；「自定义 restore + 默认 resolver」抛 `VBScriptHostException`（提示改用 `AddVBScriptNuGetRestore` 或同时替换 resolver）。（F8）
- `UseConsoleOutput()`：`Replace(ServiceDescriptor.Singleton(Of IScriptOutput, ConsoleScriptOutput)())`。

## G6. NuGet 三层 seam 与适配

真实链路：**公共 restore 服务编排扫描/缓存/诊断 → 内部 coordinator → 公共 runner 执行还原 → 公共 resolver 做编译期解析**。三层各自可替换，换掉任一层都真的生效。

| 公共契约 | 默认实现 | 内部委派 |
|---|---|---|
| `INuGetRestoreService.PrepareCompilationAsync(VBScriptSource, VBScriptOptions, ct)` | `DotnetCliRestoreService` | 构造 `NuGetRestoreCoordinator(session, runner:=NuGetRestoreRunnerBridge, sourceFingerprint:=context.SourceFingerprint, loader:=…)`，调其 `PrepareCompilationAsync`（`NuGetRestoreCoordinator.vb:34` 实现 `INuGetRestoreCoordinator`）；coordinator 只做扫描/编排/缓存键/诊断，真正还原交给桥 |
| `INuGetRestoreRunner.RestoreAsync(NuGetRestoreRequest, ct)` / `GetInstalledSdkVersionsAsync(ct)` | `DotNetRestoreRunnerAdapter` | 反向映射公共请求到内部 `RestoreRequest`（含 `NuGetRestoreConfigFile` 镜像、包名/版本、`SourceFingerprint`）并调 `DotNetRestoreRunner`（`NuGetRestoreRunner.vb:287`，`RestoreAsync` 在 `:311`） |
| `INuGetPackageResolver.Resolve(name, version)` | `SessionNuGetPackageResolver` | 读 `NuGetPackageSession` 缓存（同 `NuGetPackageResolverImpl.vb:15` 的语义） |

- **桥**：`Friend NuGetRestoreRunnerBridge` 实现内部 `IRestoreRunner`，把 `RestoreAsync`/`GetInstalledSdkVersionsAsync` 全部委托公共 `INuGetRestoreRunner`。默认服务不再 `new DotNetRestoreRunner`，所以「只换公共 runner」会真正改变还原执行（T-NUG-6）。
- **loader 能力接口**：公共 `INuGetRestoreService` 签名不带 loader（R10 契约），而 coordinator 的 `_loader` 用于运行期 `RegisterDependency`/native probe root。facade 用 `Friend INuGetRestoreServiceWithLoader` 能力接口：默认实现实现它，适配器检测到就走带 loader 的重载、否则回退公共方法。公共契约零 Roslyn 类型、零 loader。
- `NuGetPackageSession` 保持 `Friend`，由 DI 注册为单例；facade 经 IVT 使用。
- **执行期上下文**： `NuGetRestoreContext` 字段来源——`TargetFramework` 取程序集 `TargetFrameworkAttribute`；`RuntimeIdentifier` 取 `RuntimeInformation.RuntimeIdentifier`；`PlatformVersion` 取平台版本 API；`SdkVersion` 由默认实现首次真实还原时经公共 `GetInstalledSdkVersionsAsync` 探测（拿不到给空串）；`SourceFingerprint` 是 `NuGet.config` 路径/存在性的确定性摘要，**唯一来源**是 `VBScriptOptions.RestoreContext.SourceFingerprint`——coordinator 不再按脚本目录链另推导，且该值经公共 `NuGetRestoreRequest.SourceFingerprint` 透传给自定义 runner（T-NUG-7）。
- **公共 DTO**：`NuGetRestoreConfigFile`（镜像的 NuGet.config）、请求上的 `MirrorFiles`/`SourceFingerprint` 是为「默认语义可复现 + 自定义 runner 可观测」而公开的最小面。
- 默认实现首次触发时 `Logger.LogInformation` 一条（含上下文）。
- 诊断映射：内部 `Diagnostic` → `VBScriptDiagnostic`（Id/严重度/消息/文件/行列）；严重度映射到自有 `VBScriptDiagnosticSeverity`，公共签名不出现 `DiagnosticSeverity`。restore 诊断并入 `VBScriptResult.Diagnostics` 并按 Id+Severity+Message+File+Line+Column 去重。

## G7. 静态 `VBScript` 门面

```vb
Public NotInheritable Class VBScript
    Public Shared Async Function RunAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional ct As CancellationToken = Nothing) As Task(Of VBScriptResult)
    Public Shared Async Function EvaluateAsync(Of T)(code As String, Optional options As VBScriptOptions = Nothing, Optional globals As Object = Nothing, Optional ct As CancellationToken = Nothing) As Task(Of T)
    Public Shared Async Function CompileAsync(code As String, Optional options As VBScriptOptions = Nothing, Optional ct As CancellationToken = Nothing) As Task(Of VBScriptResult)
End Class
```

实现：内部复用 `ScriptRunnerCore` 的轻薄包装（不建 DI 容器）；`RunAsync` 用 `VBScriptGlobals` 默认实例。文档标注「一次性/测试用；应用集成走 DI 的 `IVBScriptRunner`」。 三个公共方法均标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`。

失败通道：`RunAsync`/`CompileAsync` 返回数据（`VBScriptResult`）；`EvaluateAsync(Of T)` 没有结果对象承载失败，故——编译错误抛 `VBScriptScriptException`（`Diagnostics` 为诊断列表，空时为空列表而非 null）；运行期异常**原样重抛**（保留 HResult、消息与原始栈，不包装）；`T` 转换失败抛 `VBScriptHostException`（`InnerException` 保留转换异常）。`T` 为值类型且脚本无返回值时返回 `default(T)`。实现上 `EvaluateAsync` 用 `CreateSession(Of Object)`（与 vbi REPL 同形），**不**把 `T` 当作脚本返回类型——本方言的裸表达式只在 object 返回型提交里产出返回值（T-RUN-6）。

## G7b. 会话 API（F13）

公共类型（`Nukepayload2.VBScriptDotNet.Hosting`）：

| 类型 | 形状 |
|---|---|
| `IVBScriptSessionFactory` | `CreateSession(options As VBScriptOptions) As IVBScriptSession` |
| `IVBScriptSession` | `SubmitAsync(code, ct)` / `IsCompleteSubmission(code)` / `Variables` / `GetVariable(name)` / `Reset()` / `DisposeAsync()` |
| `VBScriptSubmissionResult` | `Diagnostics`、`ReturnValue`、`PrintedValue`、`Exception`、`IsFaulted`（仅取消后为真；运行异常为假，见第 6 条） |
| `VBScriptVariable` | `Name`、`TypeName`、`IsReadOnly`、`Value`（只读快照） |
| `VBScriptSubmissionState` | `Complete` / `Incomplete` / `Invalid` |

语义（对应 R17）：

1. 同一 `VBScriptGlobals` 实例跨提交；`Reset` 保留。
2. 编译错误不杀会话且不刷新 options；运行异常置 `Exception` 后继续（引擎的 `ScriptSessionCore.SubmitAsync`，步骤同 G2；旧行为锚点 `CommandLineRunner.cs:368-374`、`:376-379`）。
3. `PrintedValue` 不写 `IScriptOutput`；宿主决定是否回显。
4. `IsCompleteSubmission` 三态：`Complete`（可提交）、`Incomplete`（缺 `End If`/`End Region` 等续行结构，需继续输入）、`Invalid`（解析诊断含非续行错误，如 `Dim x = )`）。判定算法：解析该字符串 → 无诊断且 `IsCompleteSubmission` 真 ⇒ `Complete`；有诊断但属「缺结束」构造 ⇒ `Incomplete`；无诊断但编译器判 `IsCompleteSubmission=False`（缺 `End If`/`End Region` 一类）⇒ `Incomplete`；其余诊断 ⇒ `Invalid`。`Dim z =` 钉死为 `Incomplete`（与 vbi 实跑一致）。不暴露 ParseOptions。
5. `Variables` 反射 `ScriptState.Variables`（`ScriptState.cs:61-72`、`:94-117`、`:119-133`），按名去重（末次胜），名字比较走 `IdentifierComparer`（VB 为大小写不敏感，`VisualBasicScriptCompiler.vb:58-62`），排除 globals。
6. **faulted 口径（R17② 与 R17⑧ 的 tie-break）**：R17② 要求「运行异常置 `Exception` 后**继续**」并明确引用既有 REPL 行为（`CommandLineRunner.cs:368-392`），R17⑧ 字面写「取消或异常后 faulted」，二者冲突。裁定按更具体、有既有实现依据的一条落地：
   - **取消 ⇒ faulted**：`SubmitAsync` 返回 `IsFaulted = True` 的结果（不向调用方抛 `OperationCanceledException`；一次性入口仍钉死为异常，T-RUN-9），之后只允许 `Reset`/`DisposeAsync`，其余提交抛 `VBScriptHostException`；
   - **运行异常 ⇒ 会话继续**：结果里 `Exception` 非空、`IsFaulted = False`，下一次提交照常工作。
   依据：② 明确引用既有 REPL 行为且描述更具体；⑧ 的「异常」按②收窄为「取消」。**若 LDM 另有裁决再改**（同步落 `design-overview` §4.3/§8）。
7. loader 与守卫归 session；session 不进 DI 容器（R3）。`Reset`/`DisposeAsync` 只清会话状态并换用新 loader（清掉加载器自己的注册表），**不承诺卸载已加载的程序集**：脚本程序集能否回收取决于宿主选择的 `AssemblyLoadContext`，库不直接控制动态卸载（与 TFM 无关）。
8. 公共入口（`IVBScriptRunner`、`IVBScriptSessionFactory`、`IVBScriptSession`、`VBScript`）统一标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`；`Variables` 的反射路径同样覆盖（R14）。实施注明：`RequiresUnreferencedCodeAttribute` 的 `AttributeUsage` 不含 `Property`（标在属性本体报 `BC30662`），VB 接口属性也没有访问器声明可挂特性（接口里写 `Property … Get`/`Set` 访问器报 `BC30662`/`BC30603`）；因此**接口属性**（`Variables`）一侧无法标注，接口**方法**可以（`IVBScriptSession.vb:44-45`、`:65-66` 已有标注）。`Variables` 的标注落到实现 getter —— `VBScriptSession.Variables` 用完整属性语法把两个特性挂在 `Get` 上（net10 条件编译，net472 无这两个特性），T-API-1 的会话标注测试改为断言实现 getter 上的特性。


## G8. 输出、globals、异常

- `IScriptOutput`：`Out`、`Error` 两个 `TextWriter`；`NullScriptOutput` 用 `TextWriter.Null`；`LoggerScriptOutput` 按行 `LogInformation`/`LogError`；`ConsoleScriptOutput` 用 `Console.Out/Error`。一次性 runner 用 `TeeScriptOutput` 把 `Print` 同时写进「本次捕获」和容器里的 `IScriptOutput`：默认 `Null` 所以不碰 Console，`Result.Output` 仍拿得到文本。
- `VBScriptGlobals`：`Args As IList(Of String)`、`Print(value As Object)`（格式化同 `CommandLineScriptGlobals`，内部用 `VisualBasicObjectFormatter.Instance`）。自定义子类必须 **Public**（提交程序集是独立动态程序集，`Friend` 类型会在生成构造函数处 `TypeAccessException`）；`GlobalsType` 走 `CreateGlobalsInstance`，优先用 `Sub New(IScriptOutput)`，否则要求公共无参构造。
- **异常层次（二选一取了方案①）**：
  - 公共基类 `VBScriptException : Exception`，`VBScriptHostException : VBScriptException`，`VBScriptScriptException : VBScriptException`；`VBScriptBusyException`、`VBScriptScriptNotFoundException` 仍是 `VBScriptHostException` 派生。
  - 理由：原先 `VBScriptScriptException : VBScriptHostException` 会让 `Catch VBScriptHostException` 连同「脚本编译失败」一起吞掉，违反 R7 的「脚本失败＝数据 / 宿主误用＝异常」二分；直接用 `Exception` 作父类（方案②）又会丢失「本库异常」的单根捕获能力，且类型层次上脚本失败与宿主误用毫无关系。方案①同时满足两点：想抓宿主误用只抓 `VBScriptHostException`，想抓本库一切只抓 `VBScriptException`。
  - 公共面：`VBScriptException` 三构造入 `PublicAPI.Unshipped.txt`；`VBScriptScriptException` 的基类改为 `VBScriptException`（新类型、未发布，不构成破坏性变更）。

## G9. 打包工程

`Installer\Scripting\Nukepayload2.Compilers.VBScriptDotNet.Scripting.Package.vbproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net472</TargetFrameworks>
    <IsPackable>true</IsPackable>
    <PackageId>Nukepayload2.Compilers.VBScriptDotNet.Scripting</PackageId>
    <Version>2.0.0-RC3</Version>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <DevelopmentDependency>false</DevelopmentDependency>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);_GetFiles</TargetsForTfmSpecificContentInPackage>
    <IsTrimmable>false</IsTrimmable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Scripting\Hosting\Nukepayload2.VBScriptDotNet.Hosting.vbproj"
                      PrivateAssets="All" ReferenceOutputAssembly="false"
                      SetTargetFramework="TargetFramework=$(TargetFramework)" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="buildTransitive\**\*.*" PackagePath="buildTransitive\" />
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>
  <Target Name="_GetFiles" DependsOnTargets="Build">
    <!-- net10：facade + 4 个 net10 编译器/脚本程序集 + 卫星资源 → lib/net10.0 -->
    <!-- net472：facade + 4 个 netstandard2.0 程序集 + 8 个 polyfill → lib/net472 -->
  </Target>
</Project>
```

文件来源（net10）：

- `Scripting\Hosting\bin\$(Configuration)\net10.0\Nukepayload2.VBScriptDotNet.Hosting.dll`
- `Compilers\Core\Portable\bin\$(Configuration)\net10.0\Microsoft.CodeAnalysis.dll`
- `Compilers\VisualBasic\Portable\bin\$(Configuration)\net10.0\Microsoft.CodeAnalysis.VisualBasic.dll`
- `Scripting\Core\bin\$(Configuration)\net10.0\Microsoft.CodeAnalysis.Scripting.dll`
- `Scripting\VisualBasic\bin\$(Configuration)\net10.0\Microsoft.CodeAnalysis.VisualBasic.Scripting.dll`
- 两侧 `**\*.resources.dll`

net472：facade `bin\$(Configuration)\net472\…`；四个程序集取 `netstandard2.0` 输出；polyfill 从 facade 的 net472 输出按名取（F9 实际形状，等价于 `PackageReference` + `GeneratePathProperty`：验证者删掉 facade 与打包工程的 bin/obj 后从零 pack，得到逐项相同的 128 项树；缺文件时 NuGet 以 `NU5019` 硬失败，不会静默漏铺）。`Microsoft.Bcl.AsyncInterfaces` 由打包工程 net472 TFM 的直接 `PackageReference` 进 nuspec（ProjectReference 的传递依赖不进 nuspec），它**不计入** `lib/` 的八个 polyfill 清单。

`dotnet pack` 必须先构建：`_GetFiles` 依赖同 Configuration 的 facade 输出，`pack --no-build` 会硬失败（`NETSDK1085`），这是预期行为，发布流程固定为「先 build 再 pack」。

## G10. 身份冲突检测（buildTransitive）

`Installer\Scripting\buildTransitive\Nukepayload2.Compilers.VBScriptDotNet.Scripting.targets`：

```xml
<Project>
  <Target Name="_VBScriptDotNetCheckRoslynConflict"
          AfterTargets="ResolvePackageDependenciesForBuild"
          Condition="'$(VBScriptAllowRoslynPackages)' != 'true'">
    <ItemGroup>
      <_VBScriptConflictingPackage Include="@(PackageDependencies)"
          Condition="$([System.Text.RegularExpressions.Regex]::IsMatch('%(Identity)','^Microsoft\.CodeAnalysis(\.|$)')) And !$([System.Text.RegularExpressions.Regex]::IsMatch('%(Identity)','\.Analyzers(\.|$)'))" />
    </ItemGroup>
    <Error Condition="'@(_VBScriptConflictingPackage)' != ''"
           Text="Nukepayload2.Compilers.VBScriptDotNet.Scripting 自带 fork 的 Microsoft.CodeAnalysis 程序集，与官方 Roslyn 包在同一进程中身份冲突（同名同公钥）。检测到（含传递依赖）：@(_VBScriptConflictingPackage, ', ')。请移除；确需共存时设 VBScriptAllowRoslynPackages=true 并自行隔离加载上下文。" />
  </Target>
</Project>
```

要点：

- 检测对象是 **`@(PackageDependencies)`（`ResolvePackageDependenciesForBuild` 的输出，含传递闭包）**，不是 `@(PackageReference)`（只含直接引用）；这是 R11「含传递依赖与插件」的落点。
- 错误消息用英文：示例里的中文只是设计稿措辞，实际消息含包名、冲突包列表与关闭属性 `VBScriptAllowRoslynPackages` 三项要素。
- 排除 `.Analyzers` 系列（分析器包与运行时程序集不冲突）。
- 旧式非 SDK 工程没有 `PackageDependencies` 时，回退扫描 `@(PackageReference)`，并在 README 标注覆盖面差异。
- `PackageDescription` 与 `README.md` 同步写明 process-wide exclusive。
## G11. 方案与文档

- `VBInteractive.sln`：登记 facade 工程与打包工程（同 Toolset 打包工程的位置惯例）。
- 新包 `README.md`：首段「运行时库，非构建工具」；安装示例；`Host.CreateApplicationBuilder` + `AddVBScripting`；替换 `INuGetRestoreService` 示例；`#R "nuget:"` 默认行为；不支持并存的说明；`#Load`/并发/程序集卸载由宿主选择的 `AssemblyLoadContext` 决定（库不承诺卸载）。
- `proposal`/`meeting` 不改；任务 README 的 F 状态在 Vortex 闭环后更新。

## G12. 锚点复核

F15 撤销 Core 落点后已重核：`Scripting\Core` 相对 HEAD 零 diff（`git diff HEAD -- Scripting/Core` 为空），引擎与 vbi 适配器在 VB scripting。`CommandLineRunner.cs` 恢复 HEAD（文件脚本预扫描与 `RunScriptAsync` 仍是上游形状，但 vbi 不再走它执行文件脚本）：`RunInteractiveCoreAsync`、`GetScriptOptions`、metadata/source resolver、REPL 预扫描 `RestoreNuGetReferencesAsync`（`:230`）、`BuildAndRunAsync`、`UpdateOptions` 均按 HEAD。`Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb`：`ScriptRunRequest`、`RunAsync`、`ScriptSessionCore(Of TReturn)`、提交内预扫描、`CreateInitialScript`（按符号名 + 语义；行号以工作树为准）；`Scripting\VisualBasic\Hosting\CommandLine\ScriptCommandLineHost.vb`：vbi 文件脚本/`check` 适配；`VisualBasicScript.vb`：分流入口。`VisualBasicScriptCompiler.vb:117`（`CollectLoadTrees`）、`:222`（`CreateSubmission`）；`INuGetRestoreCoordinator.cs:18-34`；`NuGetPackageResolver.cs:12-51`；`NuGetRestoreCoordinator.vb:34`、`:134`/`:154`；`NuGetRestoreRunner.vb:233-246`、`:287`；`NuGetPackageResolverImpl.vb:15`；`PublicAPI.Shipped.txt:6-11`；`CommandLineScriptGlobals.cs:22-61`；`vbi.coreclr.rsp:1-50`；`vbi.desktop.rsp`（`/r:System.Drawing`、`/r:System.Windows.Forms`）；`vbi.vbproj:7`；`Directory.Build.props:24-26`。 会话相关：`ScriptState.cs:61-72`、`:83-91`、`:94-117`、`:119-133`；`ScriptVariable.cs:34-71`；`Script.cs:307-310`；`CommandLineHelpers.cs:15`；`VisualBasicScriptCompiler.vb:64-71`、`:171-199`。`CommandLineRunner.cs` 的 `RunScriptAsync`/预扫描锚点此后按符号名定位，行号以工作树为准。

### R16 记录项落实

- 包体积口径：本任务包 `Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg` = 10,155,746 字节（net472 TFM 探测缺陷修复后重打，128 条目）；`Nukepayload2.Compilers.VBScriptDotNet.2.0.0-RC3.nupkg` 的 12,245,623 字节（约 11.68 MiB）是工具链包，不属本任务口径。
- `vbi.vbproj:7` 的 `net48` 只是 netfx 先例，net472 需 F11 冒烟；不得把 net48 当 net472 证据。
- `Scripting\VisualBasic\...Scripting.vbproj:1-12` 的「无 IsPackable」是 grep 缺失断言（全仓 `IsPackable` 仅两处：Toolset 打包工程与 `Microsoft.CodeAnalysis.VisualBasic.vbproj`）。
## 残余风险

> 第一条（trim/AOT 标注）已修复；第二条（net10 loader 回收）**已撤销**：脚本程序集能否卸载由宿主选择的 `AssemblyLoadContext` 决定，库不直接控制动态卸载。

- **`IVBScriptSession.Variables` 的 trim/AOT 标注（已修复）**：`RequiresUnreferencedCodeAttribute` 的 `AttributeUsage` 不含 `Property`（属性本体 `BC30662`），VB 接口属性也没有访问器声明（`BC30603`），所以接口一侧仍是唯一例外；标注改落到实现 getter：`VBScriptSession.Variables` 用完整属性语法，把两个特性挂在 `Get` 上（`#If NET`；net472 分支不标注）。实测：net10/net472 facade 均 0 警告 0 错误；`HostingApiSurfaceTests.T_API_1_TrimAndAotAnnotationsCoverTheSessionApi` 断言实现 getter 两个特性齐全。影响：直接遍历接口 `Variables` 的宿主仍拿不到分析器警告；包 `IsTrimmable=false`，v1 不支持裁剪/AOT。
- **程序集回收（撤销，不再承诺）**：此前的「net10 可回收」实现（`CoreAssemblyLoaderImpl` 的 collectible 上下文 + `Dispose` 逐个 `Unload` + 清空 loader 表、`HostingLoaderRecyclingTests` 的 T-LOAD-1..3）已随 `Scripting\Core` 恢复 HEAD 一并撤销，测试文件删除。现行口径：`Reset`/`DisposeAsync` 只清会话状态与 loader 注册，不承诺释放已加载的程序集；能否回收取决于宿主把脚本加载进哪个 `AssemblyLoadContext`（可回收上下文才可能卸载）。库不提供、也不声称提供该保证，文档（包 README、design-overview §7/§11）同步为 ALC 决定论。

## 共鸣 facet（设计贴合）

> 样本取自本仓库最近邻实现（2026-11-16 已检查）。facet 与会议 RESOLUTION 冲突时以 RESOLUTION 为准。

| 样本 | facet | 强度 | 证据锚点 | 本设计应用 |
|---|---|---|---|---|
| `tasks\distribute-compiler-nuget-package-and-dotnet-tool\` | 任务目录结构：README（依据链 + Vortex 表 + 共享事实 + 决策 + 风险）+ 三设计文档 + 手工清单 | 必选 | 该目录 `README.md`、`design-overview.md`、`test-plan.md` | 本目录同构（README / overview / detailed / test-plan / checklist） |
| `tasks\net472-desktop-branch\` | 工作材料写 `<项目根>/tmp/…`，官方文档不硬链 | 优先 | `tasks\net472-desktop-branch\README.md:5` | README 依据链改 `<项目根>/tmp/...` 写法 |
| `Installer\Toolset\...Package.vbproj` | 打包工程形状：`IsPackable` + `PackageId` + `IncludeBuildOutput=false` + `_GetFiles` + `TargetsForTfmSpecificContentInPackage` | 必选 | `...Package.vbproj:5-14`、`:39-68` | G9 同构；`DevelopmentDependency` 取 `false`（与 R13 冲突时以 RESOLUTION 为准） |
| `Scripting\VisualBasic\...Scripting.vbproj` | 产品面工程版本线 `2.0.0`+`RC3`；公共 API 台账 | 必选 | `:8-9`、`:32-35` | G1 补 `VersionPrefix/Suffix` 与 `PublicAPI.*` |
| `Scripting\VisualBasicTest\...UnitTests.vbproj` + `InteractiveSessionTests.vb` | xunit.v3 `<Fact>`；源文件 .NET Foundation 许可头 | 必选 | `...UnitTests.vbproj:23`、`InteractiveSessionTests.vb:1-15` | test-plan §8 与 G1「文件头与台账」 |
| `Installer\Toolset\build\...props` | 只有编译器重定向才需要 build props | 优先 | `...props:1-13` | 本包不需要编译器重定向 props，只带 `buildTransitive` 检测 |

**必要偏离**：① `DevelopmentDependency` 不照抄 Toolset——理由：运行时包不是开发依赖；验证：T-PKG-4。② 包 ID 与命名空间不同形——理由：包 ID 维持家族一致、命名空间自有；验证：包 README 与 nuspec 断言。

## 生长剪枝与验收

- **接触抑制梯**：新包需要存在（应用内集成的唯一路径）→ 复用官方 Hosting 的容器/配置/日志 → 最小形状 = 一个门面程序集 + 一个 core 抽离 → 才创建 facade 工程、打包工程、`buildTransitive` 检测三处新资产。

- **剪枝清单**（多长出来的部分 → 换回什么 → 原因）：

  - 自造 `VBScriptHostBuilder`/容器 → 用 `IHostApplicationBuilder`（宿主已提供）。
  - `IScriptGlobalsFactory` 与 `GlobalsType` 双旋钮 → 合并为 `Globals`/`GlobalsType` 二选一（低仪式）。
  - `/check` 做成公共开关 → 改 `CompileAsync`（库 API 不长大成命令行）。
  - `ISourceTreeProvider` → v1 用真实文件系统（触发条件：出现内存/虚拟文件系统宿主且 `SourceFileResolver` 不够）。
  - `netstandard2.0` 资产 → 缓期（触发条件：net8 宿主需求 + ns2.0 功能面实测通过）。
  - 嵌入 REPL → 删除（交互归 `vbi`）。
  - `RunInteractiveAsync` 便利层 → 移出 v1（与既有公开方法同名不同义、UI 策略、阻塞读；触发条件：出现终端式嵌入需求且命名冲突已解决）。

- **豁免区（不剪）**：官方 Hosting 装配、`#R "nuget:"` 默认可用、契约面零 Microsoft 类型、程序集身份检测、net472 冒烟门。

- **降级/缓期项**：`#R "project:"`（上限：等其成为产品能力；触发：`proposal-vbi-project-reference.md` 实现完成）；包 ID 误标风险（上限：观察一个发布周期；触发：出现一次误用报告或搜索评估失败）。

- **验收与停止**：验收 = F1–F15 的 pass 标准；停止条件见任务 README「停止条件」。
