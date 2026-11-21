# 概要设计：可嵌入脚本宿主包

> 依据：`../../meetings/meeting-embeddable-scripting-host.md` RESOLUTION R1–R17（唯一权威）。本设计只确定结构与契约，不写逐文件改动（见 `design-detailed.md`）。

## 1. 目标与非目标

**目标**：应用只引一个运行时包，就能在进程内执行 `.vbx` / `.vb` 片段，语义与 `vbi` 对齐（net10 的默认引用/导入集逐项相等），NuGet 还原默认可用且可替换，装配走官方 `Microsoft.Extensions.Hosting`。

**非目标**：语言/编译器语义改动、嵌入 REPL、`#R "project:"`、C# scripting、线程安全承诺、替代 `vbi` CLI。

## 2. 总体架构

```
应用代码
  │  Host.CreateApplicationBuilder → builder.AddVBScripting(...)
  ▼
┌────────────────────────────────────────────────────────────┐
│ Nukepayload2.VBScriptDotNet.Hosting（facade，公共契约）      │
│  · AddVBScripting / VBScriptOptions / IVBScriptRunner        │
│  · VBScriptResult / VBScriptDiagnostic / VBScriptGlobals     │
│  · INuGetRestoreService / INuGetRestoreRunner / Resolver     │
│  · IScriptOutput / 异常类型                                  │
└───────────────┬────────────────────────────────────────────┘
                │ 内部调用（Friend + IVT）
                ▼
┌────────────────────────────────────────────────────────────┐
│ Scripting 既有层（本任务只做等价抽离，不改共享程序集语义）    │
│  · ScriptRunnerCore（VB scripting，内聚 .vbx/编译/退出码/诊断）│
│  · ScriptCommandLineHost（VB scripting，文件脚本/check 适配）  │
│  · CommandLineRunner（上游壳，只承担交互 REPL 路径）           │
│  · VisualBasicScriptCompiler（提交、#Load 展开）              │
│  · NuGetRestoreCoordinator / NuGetPackageResolverImpl        │
│  · InteractiveAssemblyLoader / CommandLineScriptGlobals       │
└───────────────┬────────────────────────────────────────────┘
                ▼
        fork 编译器（Microsoft.CodeAnalysis*，随包分发）
```

两条执行路径共用同一个 core：`vbi` 的文件脚本/`/check`（`ScriptCommandLineHost` 适配）与 `IVBScriptRunner`（库）。这是 R15 的落点——差异只在输入（`CommandLineArguments` vs `VBScriptOptions`）与输出（Console vs `IScriptOutput`），不在执行语义；交互 REPL 仍由 vendored `CommandLineRunner` 承担（只调用、不修改）。`Scripting\Core` 保持上游原样，引擎刻意放在 VB scripting。

## 3. 工程与包布局

| 工程 | 路径（新建） | 产物 |
|---|---|---|
| facade | `Scripting\Hosting\Nukepayload2.VBScriptDotNet.Hosting.vbproj` | `Nukepayload2.VBScriptDotNet.Hosting.dll`（`net10.0` + `net472`） |
| 打包 | `Installer\Scripting\Nukepayload2.Compilers.VBScriptDotNet.Scripting.Package.vbproj` | `Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg` |

nupkg 内容：

```
lib/net10.0/
  Nukepayload2.VBScriptDotNet.Hosting.dll
  Microsoft.CodeAnalysis.dll  Microsoft.CodeAnalysis.VisualBasic.dll
  Microsoft.CodeAnalysis.Scripting.dll  Microsoft.CodeAnalysis.VisualBasic.Scripting.dll
lib/net472/
  Nukepayload2.VBScriptDotNet.Hosting.dll          # net472 构建
  Microsoft.CodeAnalysis*.dll                       # netstandard2.0 构建
  System.Buffers.dll  System.Collections.Immutable.dll  System.Memory.dll
  System.Numerics.Vectors.dll  System.Reflection.Metadata.dll
  System.Runtime.CompilerServices.Unsafe.dll  System.Text.Encoding.CodePages.dll
  System.Threading.Tasks.Extensions.dll
buildTransitive/
  Nukepayload2.Compilers.VBScriptDotNet.Scripting.targets   # 身份冲突检测
README.md
```

依赖：`Microsoft.Extensions.Hosting.Abstractions` 等六个 Abstractions 包（10.0.0）。**不含**任何 `Microsoft.CodeAnalysis*` 包依赖（编译器程序集直接铺在 `lib/`，避免与上游同 ID 冲突）。 net472 另需 `Microsoft.Bcl.AsyncInterfaces`（`Hosting.Abstractions` 的 net462 组传递依赖，见 nuspec），不计入 `lib/` 清单。

## 4. 公共 API 面

### 4.1 命名空间 `Nukepayload2.VBScriptDotNet.Hosting`

| 类型 | 形状 |
|---|---|
| `VBScriptingServiceCollectionExtensions` | `AddVBScripting(this IServiceCollection, Action(Of VBScriptOptions))`；`AddVBScripting(this IHostApplicationBuilder, Action(Of VBScriptOptions))` → 返回 builder；`AddVBScriptNuGetRestore(this IServiceCollection)`；`UseConsoleOutput(this IServiceCollection)` |
| `VBScriptOptions` | `Imports`、`References`、`WorkingDirectory`、`ScriptArguments`、`FileEncoding`、`EmitDebugInformation`、`OptimizationLevel`（自有 `VBScriptOptimizationLevel`）、`Globals`（对象，优先）、`GlobalsType`（Type，仅一次性入口）、`UseDesktopReferences`、`RestoreContext`（只读） |
| `IVBScriptRunner` | `RunFileAsync(path, args, ct)` / `RunCodeAsync(code, ct)` / `CompileFileAsync(path, ct)` / `CompileCodeAsync(code, ct)` / `EvaluateAsync(Of T)(code, ct)` |
| `IVBScriptSessionFactory` | singleton（只 `new`，session 不进容器）；`CreateSession(options As VBScriptOptions) As IVBScriptSession` |
| `IVBScriptSession` | 调用方 `Await Using`；`SubmitAsync` / `IsCompleteSubmission` / `Variables` / `GetVariable` / `Reset` / `DisposeAsync` |
| `VBScriptSubmissionResult` | `Diagnostics`、`ReturnValue`、`PrintedValue`、`Exception`、`IsFaulted`（仅取消后为真；运行异常为假，见 §8） |
| `VBScriptVariable` | 只读快照，不含 globals；`Name`、`TypeName`、`IsReadOnly`、`Value` |
| `VBScriptSubmissionState` | 枚举：`Complete` / `Incomplete` / `Invalid` |
| `VBScriptResult` | `ExitCode`、`ReturnValue`（无 `Return` 时 `Nothing`，`Return 0` 时为 boxed 0）、`Diagnostics`（`ImmutableArray(Of VBScriptDiagnostic)`）、`Output`（`String`，只含 `VBScriptGlobals.Print` 的文本；不捕获 `Console.WriteLine`）、`Exception`（`System.Exception`，运行期异常） |
| `VBScriptDiagnostic` | `Id`、`Severity`（自有 `VBScriptDiagnosticSeverity`）、`Message`、`FilePath`、`Line`、`Column` |
| `VBScriptDiagnosticSeverity` / `VBScriptOptimizationLevel` | 自有枚举：`Error/Warning/Info/Hidden`、`Debug/Release`；实现层映射到 Roslyn 枚举，公共签名不出现 `Microsoft.CodeAnalysis.*` |
| `VBScriptGlobals` | `Print(value)`、`Args`；可继承，`GlobalsType` 必须派生自它；**子类必须 `Public`**（提交程序集是独立动态程序集，非 public 类型在生成代码访问时抛 `TypeAccessException`） |
| `VBScriptSource` | `Text`、`FilePath` |
| `IScriptOutput` | `Out` / `Error`（`TextWriter`）；内置 `NullScriptOutput`、`ConsoleScriptOutput`、`LoggerScriptOutput` |
| `INuGetRestoreService` | `PrepareCompilationAsync(VBScriptSource, VBScriptOptions, ct)` → `Task(Of ImmutableArray(Of VBScriptDiagnostic))` |
| `INuGetRestoreRunner` | `RestoreAsync(NuGetRestoreRequest, ct)` → `Task(Of NuGetRestoreOutcome)`；`GetInstalledSdkVersionsAsync(ct)` → 已安装 SDK 版本（执行器层，默认服务经桥调用它，替换即生效） |
| `INuGetPackageResolver` | `Resolve(packageName, version)` → `ImmutableArray(Of String)`（同步前端） |
| `NuGetRestoreContext` | 只读：`TargetFramework`、`RuntimeIdentifier`、`PlatformVersion`、`SdkVersion`、`SourceFingerprint` |
| 异常 | `VBScriptException`（公共基类，抓本库一切）、`VBScriptHostException`（宿主误用的基类）、`VBScriptBusyException`（重入）、`VBScriptScriptNotFoundException`、`VBScriptScriptException`（仅 `EvaluateAsync` 的编译错误，带 `Diagnostics`；**不**继承 `VBScriptHostException`，见 §8） |

### 4.2 命名空间 `Nukepayload2.VBScriptDotNet.Scripting`

`VBScript` 静态门面：`RunAsync` / `EvaluateAsync(Of T)` / `CompileAsync`，参数为代码 + `VBScriptOptions` + 可选 globals；`RunAsync`/`CompileAsync` 返回 `VBScriptResult`，`EvaluateAsync(Of T)` 返回 `T`（脚本无返回值时 `default(T)`）。失败通道：编译错误抛 `VBScriptScriptException`（带诊断），运行期异常原样重抛，`T` 转换失败抛 `VBScriptHostException`。**不暴露** `Create` / `Script(Of T)` / `ScriptState`。 无状态入口内部复用「一次提交的会话」实现（`EvaluateAsync` 用 object 返回型会话，裸表达式才产出返回值）。

### 4.3 不变量

- 公共签名不出现 `Microsoft.CodeAnalysis.*`（编译单元、诊断、引用、loader 全部在实现层）。 枚举同样走自有类型（`VBScriptOptimizationLevel`/`VBScriptDiagnosticSeverity`），不直接暴露 `OptimizationLevel`/`DiagnosticSeverity`。
- 脚本失败 = `VBScriptResult`（诊断 + 退出码）；宿主误用 = 自有异常。**唯一例外**是 `VBScript.EvaluateAsync(Of T)`——它没有结果对象，编译失败抛 `VBScriptScriptException`（携带 `Diagnostics`），运行期异常原样重抛。
- `/check` 语义由 `CompileAsync` 承载，不是命令行开关。
- 会话失败语义：**取消**后 session 进入 faulted，只允许 `Reset`/`DisposeAsync`；**运行异常不 faulted**（结果携带 `Exception`，会话继续），依据 R17② 对既有 REPL 行为的明确引用，见 §8 与 detailed G7b 第 6 条。`PrintedValue` 只作数据返回，不写 `IScriptOutput`。

## 5. 默认语义与引用解析

- facade 内嵌 `vbi.coreclr.rsp`（构建时 link `Interactive\vbi\vbi.coreclr.rsp`，LogicalName 固定），启动时解析为默认 `References`（`/r:`）与 `Imports`（`/imports:`）；`/nostdlib`、`/vbruntime-` 由编译器选项承担，不进列表。
- net10：默认集 = 该 rsp 逐项；单测比对仓库文件。
- net472：默认集同样取 coreclr 列表（不带 `System.Drawing`/`System.Windows.Forms`）；`UseDesktopReferences = True` 时追加 desktop rsp 的两条引用。
- 引用解析沿用 `RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`（`CommandLineRunner.cs` 的 `GetMetadataReferenceResolver`，`:212`），保证与 `vbi` 同源。
- `#R "nuget:"` 预扫描发生在编译前（文件脚本在 VB scripting 引擎 `Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb` 的提交路径；REPL 仍在 `CommandLineRunner.cs` 的 `RestoreNuGetReferencesAsync`，`:230`；行号以工作树为准），`#Load` 可达树复用 `CollectLoadTrees`（`VisualBasicScriptCompiler.vb:117`）。

## 6. DI 拓扑与覆盖语义

| 服务 | 生命周期 | 默认实现 |
|---|---|---|
| `IVBScriptRunner` | transient | `VBScriptRunner` |
| `IVBScriptSessionFactory` | singleton | `VBScriptSessionFactory`（只 `new`，不入容器） |
| `IScriptOutput` | singleton | `NullScriptOutput`（`UseConsoleOutput` 换 `ConsoleScriptOutput`） |
| `INuGetRestoreService` | singleton | `DotnetCliRestoreService`（编排扫描/缓存/诊断，经 `NuGetRestoreCoordinator`；带 loader 的能力接口 `Friend INuGetRestoreServiceWithLoader`） |
| `INuGetRestoreRunner` | singleton | `DotNetRestoreRunnerAdapter`（默认服务经 `Friend NuGetRestoreRunnerBridge` 调用它，所以替换会真的改还原执行） |
| `INuGetPackageResolver` | singleton | `SessionNuGetPackageResolver` |
| `NuGetPackageSession` | singleton（内部加锁） | 会话缓存 |
| `IOptions(Of VBScriptOptions)` | singleton | 配置绑定 `VBScript` 节 |

覆盖规则：`AddVBScripting` 全部用 `TryAdd*`；用户在之前注册即胜出，之后注册则按官方 DI「最后注册者胜」，或显式 `Replace`。`Build()` 后容器不可变。成组注册 `AddVBScriptNuGetRestore()` 注册三层默认；启动校验发现「只换了一环」（如自定义 `INuGetRestoreService` 但未换 resolver 且未成组注册）时抛配置异常。

三层链路是**串联**而非并列：restore 服务负责扫描/缓存键/诊断 → 内部 `NuGetRestoreCoordinator` → `NuGetRestoreRunnerBridge` → 公共 `INuGetRestoreRunner` 执行还原 → 公共 `INuGetPackageResolver` 供编译期解析。所以「只换公共 runner」会真的改变默认路径的还原执行（T-NUG-6），`SourceFingerprint` 的唯一来源是 `VBScriptOptions.RestoreContext.SourceFingerprint`（不再按脚本目录另推导，且透传给自定义 runner，T-NUG-7）。

`AddVBScripting` 实际注册面：`IOptions(Of VBScriptOptions)`（配置绑定 + `PostConfigure` 追加默认集 + `IValidateOptions` 校验器）、`IScriptOutput`（默认 `NullScriptOutput`）、`IVBScriptRunner`（transient）；NuGet 三项由 `AddVBScriptNuGetRestore()` 成组注册。facade 只引 Abstractions，因此 `IHostApplicationLifetime` 与 `ILogger(Of VBScriptRunner)` 必须由宿主提供——用 `IHostApplicationBuilder` 重载或 `Host.CreateApplicationBuilder` 时天然满足；裸 `IServiceCollection` 场景需自行注册这两项，否则解析 runner 时容器报缺依赖。

## 7. 生命周期与并发

- `IVBScriptRunner` 为 transient：每次调用内部创建「一次提交的会话」，用完即弃；无状态入口与有状态入口共用同一提交实现。
- `IVBScriptSessionFactory` 为单例，只负责 `new`；`IVBScriptSession` **不进 DI 容器**（生命周期是窗口/面板，不是请求），由调用方 `Await Using` 释放。
- **每个一次性执行也各持独立 loader**：`VBScriptRunner` 每次调用 `new InteractiveAssemblyLoader()` 并传入 core，执行结束释放；不与 session 或其它调用共享。
- **一次性执行的输出**：runner 用 `TeeScriptOutput` 把 `Print` 同时写进本次捕获缓冲（→ `VBScriptResult.Output`）和容器里的 `IScriptOutput`（默认 `NullScriptOutput`，即默认不写 Console；`UseConsoleOutput()` 才写）。脚本以 `Console.WriteLine` 直接写进程输出不经此通道，故不会被捕获。
- **每个 session 自持 `InteractiveAssemblyLoader` 与重入守卫**；一次性执行的守卫留在 runner。并发占用抛 `VBScriptBusyException`，不静默损坏。
- `ApplicationStopping` 在**每次提交**链接（不是建 session 时捕获一次）；`DisposeAsync` 幂等并取消在飞提交。
- `Reset` 清状态、恢复初始 options 快照、保留 globals、**新建 loader**；旧 loader 在 `Reset`/`DisposeAsync` 里 `Dispose`，只清掉加载器自己的注册表。**脚本程序集能否卸载取决于宿主选择的 `AssemblyLoadContext`**：库不直接控制动态卸载，不承诺释放已加载的程序集；`Reset`/`DisposeAsync` 只清会话状态与 loader 注册。
- **公共 scripting 程序集不承载本仓功能**：引擎在 `Scripting\VisualBasic\Hosting\`（VB scripting），`Scripting\Core` 保持上游原样；原因是同一进程的用户可能并用其它 Roslyn API，改公共程序集不可接受。

## 8. 错误模型

| 情形 | 通道 |
|---|---|
| 编译错误 / 脚本异常 / `Return` 非零 | `VBScriptResult.ExitCode` + `Diagnostics`（与 `vbi` 的退出码语义一致） |
| `EvaluateAsync(Of T)` 的编译错误（无结果对象可承载） | `VBScriptScriptException`（带 `Diagnostics`） |
| `EvaluateAsync(Of T)` 的运行期异常 | 原样重抛（保留 HResult、消息、脚本栈帧） |
| 脚本文件不存在 | `VBScriptScriptNotFoundException` |
| 同一 runner 并发调用 | `VBScriptBusyException` |
| 同一 session 并发提交 | `VBScriptBusyException` |
| 取消（一次性入口） | `OperationCanceledException`（一次性入口钉死为异常；session 走 faulted 结果） |
| 取消后的 session | 进入 faulted（结果 `IsFaulted=True`），只允许 `Reset`/`DisposeAsync`；后续提交抛 `VBScriptHostException` |
| 运行异常后的 session | **不 faulted**：结果携带 `Exception`，会话继续（R17②，detailed G7b 第 6 条的 tie-break） |
| 宿主已停止（`ApplicationStopping` 已触发） | `VBScriptHostException` |
| 宿主已释放 / 未初始化 | `VBScriptHostException` 派生 |
| restore 配置不一致 | 启动/构造期 `VBScriptHostException`（含修复提示） |

异常层次：公共基类 `VBScriptException`；`VBScriptHostException : VBScriptException`（宿主误用族，`VBScriptBusyException`、`VBScriptScriptNotFoundException` 在其下）；`VBScriptScriptException : VBScriptException`（脚本编译失败，**不**在宿主误用族下，故 `Catch VBScriptHostException` 不会吞掉脚本编译错误）。脚本运行期异常不属于本库异常：它在 `VBScriptResult.Exception` 里原样返回（`EvaluateAsync` 则原样重抛）。

## 9. 程序集身份与 trim

- 包内 `Microsoft.CodeAnalysis*.dll` 与官方同公钥：`buildTransitive` 目标在 `ResolvePackageDependenciesForBuild` 之后读 `@(PackageDependencies)`（SDK 解析出的依赖闭包，含传递依赖），对不产生 `PackageDependencies` 的非 SDK 工程回退到 `@(PackageReference)`（仅直接依赖）；命中 `Microsoft.CodeAnalysis*`（排除 `.Analyzers` 系列）即 `Error`，可用 `VBScriptAllowRoslynPackages=true` 关闭。
- README 与 `PackageDescription` 写「process-wide exclusive，含传递依赖与插件」。
- 公共入口标 `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`；包 `IsTrimmable=false`。

## 10. RESOLUTION 映射

| R | 设计落点 |
|---|---|
| R1 定性 M5 | 本文件 §1、§4（不改语言面） |
| R2 Abstractions-only + 双扩展 | §3 依赖、§4.1 扩展方法 |
| R3 transient + 守卫 + lifetime | §6 生命周期、§7 |
| R4 默认集 | §5 |
| R5 契约零 Microsoft | §4.3、design-detailed G1/G3 |
| R6 两个入口 | §4.1/§4.2；概设 §10 本行；设计 G7/G7b |
| R7 异常模型 | §8 |
| R8 globals | §4.1 `VBScriptGlobals` |
| R9 输出默认 | §4.1 `IScriptOutput`、§6 |
| R10 三层 seam + 组注册 + 上下文 | §4.1、§6、design-detailed G6 |
| R11 身份检测 | §9、design-detailed G10 |
| R12 TFM + polyfill + 冒烟 | §3 包布局、test-plan |
| R13 打包 | §3 |
| R14 trim/AOT | §9 |
| R15 core 抽离 | §2、design-detailed G2 |
| R16 文档修正 | design-detailed 末尾 |
| R17 会话语义 | §4.1 会话类型、§6 factory、§7 生命周期、设计 G7b |

## 11. 代价与风险

- **core 抽离是真实重构**：新建 VB 引擎要以 `VBScriptOptions` 重建 options 路径（旧实现与 `CommandLineArguments` 耦合，见 `CommandLineRunner.cs:140`、`:238-245`）；风险由「先等价、后扩展」两阶段控制。
- **`IVBScriptSession.Variables` 的标注只能落在实现上**：CLR 的 `RequiresUnreferencedCodeAttribute` 目标不含属性，VB 接口属性也没有可挂特性的访问器声明，因此标注挂在实现 getter `VBScriptSession.Variables`；反射入口用同样标注过的 `GetVariable`。包 `IsTrimmable=false`，v1 不支持裁剪/AOT，此差异不影响行为。
- **`Reset` 只清会话状态与 loader 注册，不承诺卸载程序集**：能否回收取决于宿主选择的 `AssemblyLoadContext`，库不直接控制动态卸载；这一点与目标框架无关。
- **facade 依赖 `Friend` 成员**：靠共享密钥签名 + `InternalsVisibleTo`，与 `vbi` 同法；不把内部类型改成 public。
- **net472 闭包与 net10 不同**：8 个 polyfill 必须显式；冒烟是发布硬门。
- **身份冲突**：机制上无解（同公钥同身份），只能构建期拦截 + 文档声明。
- **包 ID 位于工具链家族**：保留但记误标风险，重开条件写在 meeting OPEN QUESTIONS。
