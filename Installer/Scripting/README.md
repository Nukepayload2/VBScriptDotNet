# Nukepayload2.Compilers.VBScriptDotNet.Scripting

这是运行时库，非构建工具。它把 VBScript.NET 的脚本执行管线（`.vbx` 文件语义、`Print`/`Args`、`Return` 退出码、`#Load`、`#R "nuget:"`）作为可嵌入组件提供给普通 .NET 应用；包内确实自带编译器程序集，但那是为了在运行时编译脚本，不会重定向任何项目的 `Vbc` 编译。要让 `dotnet build` / Visual Studio 用本 fork 编译 `.vbproj`，请引用构建工具包 `Nukepayload2.Compilers.VBScriptDotNet`。

包支持 `net10.0` 与 `net472` 两个目标框架，依赖官方 .NET Hosting 的 Abstractions 层，不依赖任何 `Microsoft.CodeAnalysis*` NuGet 包（编译器程序集直接铺在 `lib/` 下）。

## 安装

```xml
<PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet.Scripting" Version="2.0.0-RC3" />
```

只在运行时使用，不要加 `PrivateAssets="all"`：这是运行时库，需要随应用一起发布。

`net472` 目标会在 `lib/net472` 中带上脚本执行所需的 polyfill（`System.Buffers`、`System.Collections.Immutable`、`System.Memory`、`System.Numerics.Vectors`、`System.Reflection.Metadata`、`System.Runtime.CompilerServices.Unsafe`、`System.Text.Encoding.CodePages`、`System.Threading.Tasks.Extensions`），应用不需要再单独引用它们。

## 在宿主中注册

`AddVBScripting` 只使用 `IHostApplicationBuilder` / `IServiceCollection`，容器、配置、日志全部由宿主提供：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nukepayload2.VBScriptDotNet.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddVBScripting(options =>
{
    options.WorkingDirectory = AppContext.BaseDirectory; // #Load 与相对引用的基准目录
});

// 显式选择把脚本 Print 写到控制台；默认输出是 Null，库不会污染 stdout/stderr。
builder.Services.UseConsoleOutput();

using var host = builder.Build();
var runner = host.Services.GetRequiredService<IVBScriptRunner>();

var result = await runner.RunCodeAsync("Print(\"hello from a script\")");
Console.WriteLine($"exit={result.ExitCode} output={result.Output}");
```

没有容器的一次性调用可以用静态门面：

```csharp
using Nukepayload2.VBScriptDotNet.Scripting;

var result = await VBScript.RunAsync("Return 42");
```

`IVBScriptRunner` 是 transient 的，每次解析得到新实例。`VBScriptResult` 同时给出 `ExitCode`、`ReturnValue`、`Diagnostics`、`Output` 与运行期 `Exception`：脚本失败是数据，宿主误用（文件不存在、runner 正忙、宿主停止）才抛异常。

`net472` 的默认 references 列表不含桌面程序集（与 `vbi.coreclr.rsp` 一致）。`options.UseDesktopReferences = true` 会把 `System.Drawing` 与 `System.Windows.Forms` 追加进列表（`net10.0` 上该选项被忽略并记一条 Warning）。注意：在 .NET Framework 上默认的平台元数据解析器仍可能从 GAC 解析到这两个程序集，所以「不开启时类型不可用」并不可观察——该开关的实际作用是让引用列表与 `vbi.desktop.rsp` 保持同形。

## 替换 NuGet 还原

`INuGetRestoreService` 是「提交前预扫描并还原 `#R "nuget:"`」的公共缝，默认实现经 `dotnet restore` 工作。宿主可以整体替换：

```csharp
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nukepayload2.VBScriptDotNet.Hosting;

sealed class MyRestoreService : INuGetRestoreService
{
    public Task<ImmutableArray<VBScriptDiagnostic>> PrepareCompilationAsync(
        VBScriptSource source,
        VBScriptOptions options,
        CancellationToken cancellationToken)
    {
        // 自行解析 source.Text 中的 #R "nuget:"，返回锚定在对应行的阻塞诊断；空数组表示可以继续编译。
        return Task.FromResult(ImmutableArray<VBScriptDiagnostic>.Empty);
    }
}

builder.Services.Replace(ServiceDescriptor.Singleton<INuGetRestoreService, MyRestoreService>());
```

还原执行（`INuGetRestoreRunner`）与包解析（`INuGetPackageResolver`）是另外两层。可以单独替换 `INuGetPackageResolver`；只替换 `INuGetRestoreService` 而保留默认 resolver 不行——runner 构造时会抛 `VBScriptHostException`，因为默认 resolver 读的是默认 service 写下的会话状态。两种可行配置：调用 `AddVBScriptNuGetRestore()` 注册一组默认实现后只替换 resolver；或同时替换 service 与 resolver，让 resolver 读取你的还原实现写下的状态。

## `#R "nuget:"` 的默认行为

- 编译前预扫描脚本里的 `#R "nuget:Package, Version"`，按脚本 `WorkingDirectory` 的目录链收集 `NuGet.config`，在进程外执行 `dotnet restore`，再把还原结果加入本次执行的程序集加载器。
- 还原失败会变成锚定在 `#R` 行的诊断并阻止编译，不会继续编译出一个「找不到类型」的假象。
- 默认实现记录宿主环境（目标框架、RID、平台版本、SDK 版本与源配置指纹），可通过 `VBScriptOptions.RestoreContext` 只读查看；首次触发经 `ILogger` 留一条 Information。
- 还原使用宿主的 `dotnet` 与用户级 NuGet 缓存；库不修改应用的 `NuGet.config`。

## 不能与官方 Roslyn 包并存

包内的 `Microsoft.CodeAnalysis*.dll` 与官方 Roslyn 包**同名且同公钥**。.NET Framework 上这就是同一个程序集身份；.NET Core 默认的 AssemblyLoadContext 按简单名绑定，所以同进程里只会有一份生效——谁先加载谁赢。这是 **process-wide exclusive** 的边界，不是「引用两份、各用各的」。

- 冲突检测在构建期执行，读取的是**包含传递依赖的依赖闭包**，不只看直接 `PackageReference`；插件和测试宿主同样会被检测到。
- 命中 `Microsoft.CodeAnalysis*`（排除 `.Analyzers` 系列分析器包）会直接构建失败，错误消息列出冲突包名与关闭属性 `VBScriptAllowRoslynPackages`。
- 确认自己隔离了加载上下文时才可设 `VBScriptAllowRoslynPackages=true` 关闭检测；否则运行期会出现难以解释的类型身份错乱。
- 非 SDK 工程没有 SDK 的依赖闭包项时，检测回退为扫描直接 `PackageReference`，覆盖面更窄，需要自行检查传递依赖。

## 已知限制

- **`#Load` 不是沙箱**：路径相对 `VBScriptOptions.WorkingDirectory` 解析，脚本可以加载进程有权读取的任意文件；库不做权限隔离。
- **并发**：`IVBScriptRunner` 不承诺线程安全。同一个 runner 上一次调用未完成时再次调用会抛 `VBScriptBusyException`，不会静默串行化。并发场景请让每个 worker 解析自己的 runner；每次执行各持独立程序集加载器。
- **脚本程序集能否卸载由宿主选择的 `AssemblyLoadContext` 决定**：库不直接控制动态卸载。`Reset` / `DisposeAsync` 只清会话状态并换用新的加载器（清掉加载器自己的注册表），**不承诺释放已加载的脚本程序集**；旧程序集能否被 GC 回收，取决于宿主把脚本加载进哪个 `AssemblyLoadContext`（只有可回收上下文才可能卸载），这一点与目标框架无关。长驻宿主若确实需要回收，应在宿主侧安排可回收上下文边界；库不提供、也不声称提供该保证。一次性 `IVBScriptRunner` 调用每次新建加载器并在结束时释放，同样只释放加载器状态，不代表程序集已被卸载。
- **不支持裁剪/AOT**：动态编译与动态加载无法被静态分析，公共入口标注了 `RequiresUnreferencedCode` / `RequiresDynamicCode`，包本身 `IsTrimmable=false`。`PublishTrimmed` / NativeAOT 场景请勿使用。唯一例外是 `IVBScriptSession.Variables` 属性：CLR 的 `RequiresUnreferencedCodeAttribute` 不允许标在属性上，反射读取请走同一接口上已标注的 `GetVariable`。
- **`net472` 仅 Windows**：需要 .NET Framework 4.7.2+。
- **`net472` 上的 `#R "nuget:"` 只支持托管包**：含 native 资产的包（如 `Microsoft.Data.Sqlite`）会得到 `VBI1010` 宿主能力诊断——.NET Framework 宿主无法探测 native 库。需要 native 依赖时请把宿主目标改成 `net10.0`。
- **Blazor WebAssembly（纯前端）暂不支持**：编译期需要宿主注入参考程序集（浏览器里 `Assembly.Location` 为空、`TRUSTED_PLATFORM_ASSEMBLIES` 为 null，且运行时程序集是 WebCIL 而非 PE），当前实现会在 `VisualBasicScriptCompiler` 类型初始化处失败。执行期本身可用（net10 Debug 下 `Assembly.Load(byte[])` 已验证）；纯前端场景请等产品支持或改用 Blazor Server。可行性细节见 `InternalDevDocs\proposals\proposal-blazor-wasm-repl.md`。

## 许可证

MIT
