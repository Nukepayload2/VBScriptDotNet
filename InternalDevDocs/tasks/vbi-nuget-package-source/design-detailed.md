# 详细设计：脚本目录链的 `NuGet.config` 生效 + 源指纹接线

> 任务：`vbi-nuget-package-source`（F2 交付物）。事实基准见 `README.md`「共享源码事实」。改动形状按 **C1–C8** 编号，每个 C 给"文件 + 函数 + 行号 + 改动形状"；**G1–G5**（§九）是必须同时满足的实现约束，违反任一条即还原链路失效。
>
> 文件行号锚点以**改动前**工作树（`2f75d52`+）为基准；带「现状」列的表描述改动前的形状。

## 〇、布局不变量（本设计的核心）

```
<cacheRoot>\<key>\
    global.json                       ← 保持现状（其解析不由本布局决定，见 C2.1）
    <镜像根相对层级>\NuGet.config       ← 新增：从脚本目录链原样复制的目录级配置（可能多份）
    <镜像根相对层级>\project.vbproj     ← 生成工程（相对镜像根的深度 = 脚本目录相对镜像根的深度）
    <镜像根相对层级>\obj\…              ← 由 dotnet restore 产出
```

- **镜像根** = 脚本目录链上**最靠上**的目录级 `NuGet.config` 的父目录；链上没有配置时镜像根 = 脚本所在目录 ⇒ 工程回落到 `<key>\project.vbproj`（**与改动前完全一致**）。
- **为什么镜像而不是合并**：`<clear/>`、`disabledPackageSources`、`packageSourceCredentials`、环境变量展开、机器/用户级合并全部继续由 NuGet 计算；本设计只搬运文件、不解释其内容。这是"私有源凭据必须在支持范围内"的直接要求。
- **为什么不只镜像最近的一份**：NuGet 的配置链以**工程文件所在目录**为根向上合并，镜像层级必须与真实脚本目录逐层一致。只搬最近一份会让外层配置的源与凭据静默消失，并使同一棵树在 `dotnet restore` 与 vbi 下取到不同的源集合。
- **为什么不写 `<RestoreSources>`**：会丢凭据（`packageSourceCredentials` 不在工程属性里），且需要解析 `dotnet nuget list source` 的文本输出（该命令**无 json**，见 README「共享源码事实」）。

## 一、C1 配置收集（新增纯函数）

**新增文件**：`Scripting\VisualBasic\Hosting\NuGetSourceConfig.vb`。

```vb
Friend NotInheritable Class SourceConfigFile
    Friend ReadOnly Property MirrorRelativePath As String   ' 相对镜像根/缓存键目录，如 "sub\NuGet.config"
    Friend ReadOnly Property Content As Byte()              ' 原样字节，不做编码解释
End Class

Friend NotInheritable Class NuGetSourceConfigResult
    Friend ReadOnly Property Files As ImmutableArray(Of SourceConfigFile)   ' 按 MirrorRelativePath 排序
    Friend ReadOnly Property MirrorRoot As String                           ' 见规则 3
    Friend ReadOnly Property FailedPath As String                           ' 读取失败的首个路径（否则 Nothing）
    Friend ReadOnly Property FailureReason As String
    Friend ReadOnly Property HasFailure As Boolean
End Class

''' 文件系统 seam：生产用 PhysicalSourceConfigFileSystem.Instance，测试注入内存实现（对齐 IRestoreRunner 风格）
Friend Interface ISourceConfigFileSystem
    Function DirectoryExists(path As String) As Boolean
    Function EnumerateFiles(directory As String) As IEnumerable(Of String)
    Function ReadAllBytes(path As String) As Byte()
End Interface

Friend NotInheritable Class NuGetSourceConfig
    Friend Shared Function Collect(scriptFilePath As String, fileSystem As ISourceConfigFileSystem) As NuGetSourceConfigResult
    Friend Shared Function ProjectRelativePath(scriptFilePath As String, mirrorRoot As String) As String
    Friend Shared Function Fingerprint(files As ImmutableArray(Of SourceConfigFile)) As String
End Class
```

**规则（逐条可测）**：

1. 文件名匹配 `NuGet.config`，`OrdinalIgnoreCase`；**通过枚举目录**（`EnumerateFiles`）而非探测单个拼写，否则 Linux 的区分大小写文件系统上 `nuget.config` 会漏检。每级至多取 1 个文件（同级多个拼写时按 `String.CompareOrdinal` 取首个，确定性）。
2. 从脚本目录向上逐级查找，直到盘符根（或路径不再变化）；已越过根的判据是 `Path.GetDirectoryName` 返回空或不再变化。
3. `MirrorRoot` = 最靠上的那个命中文件的父目录；空集合 ⇒ `MirrorRoot` = 脚本目录。它由 `Collect` 从**绝对命中路径**算出并随结果返回（见 G3）。
4. `MirrorRelativePath` = 命中文件所在目录相对 `MirrorRoot` 的路径 + **规范文件名** `NuGet.config`；`ProjectRelativePath` = 脚本目录相对 `MirrorRoot` 的路径 + `project.vbproj`（`MirrorRoot` = 脚本目录时即 `project.vbproj`）。相对路径用**词法实现**（`TryGetRelativePath`：分隔符边界判定 + `C:\` 盘根保留），因为 `Path.GetRelativePath` 不在 netstandard2.0。
5. `Fingerprint` = 对每个文件按 `MirrorRelativePath`（**小写归一 + `\0` + 文件字节**）拼接、**在函数内按 `MirrorRelativePath` 排序后**取 SHA-256，hex 编码。**只吃相对路径与字节**，不吃绝对路径/时间戳/机器名 ⇒ 跨机器可复现，且"同集内容变"必然换 key。排序在函数内做（不改调用方数组顺序语义），保证「枚举顺序颠倒 ⇒ 同指纹」。
6. 空集合 ⇒ `Fingerprint` = `""`（与改动前生产恒空串逐字相同 ⇒ 回归零变化）。
7. `scriptFilePath` 为空/`Nothing`、非绝对且无法 `Path.GetFullPath`、或目录不存在 ⇒ 空集合（见 C4）；**不抛异常**。命令行给出的脚本路径已是绝对路径（`CommandLineParser.ParseFileArgument` → `FileUtilities.ResolveRelativePath`，`CommandLineParser.cs:1151-1181` / `FileUtilities.cs:96-105`），唯一非绝对来源是 stdin 脚本 `"-"`（`VisualBasicCommandLineParser.vb:486`）。
8. 某个配置文件存在但读取失败（权限/占用/`ReadAllBytes` 返回 `Nothing`）⇒ 不抛，返回带 `FailedPath`/`FailureReason` 的结果，由 C5 转成诊断。

## 二、C2 镜像落盘（`NuGetRestoreRunner.vb`）

| 位置 | 改动形状 |
| --- | --- |
| `RestoreRequest` ctor | 增可选参 `mirrorFiles As ImmutableArray(Of SourceConfigFile) = Nothing`、`projectRelativePath As String = Nothing`；`IsDefault` 归一为空数组；相对路径非法（`IsPathRooted` 或含 `..`）**抛 `ArgumentException`**，保证镜像与工程都不越出缓存键目录 |
| `ProjectFilePath` | `Path.Combine(_cacheDirectory, _projectRelativePath)`，`projectRelativePath` 缺省为 `project.vbproj` ⇒ 旧调用形状行为不变 |
| 新增属性 | `MirrorFiles`、**`ProjectDirectory`**（= 工程所在目录，`obj` 的落点，见 G1） |
| `DotNetRestoreRunner.RestoreAsync` | 在写工程之前调用 `WriteMirrorFiles(...)`（新增 `Friend Shared`，见下）；工程/`global.json` 的落盘抽成 `WriteProjectFiles(...)`（**先建工程父目录**，见 G5）；其余（跑 restore、读 assets、`NuGetRestoreCache.Trim`）不变 |
| `ProjectDirectory` 用于 `obj` | `project.nuget.cache` / `project.assets.json` 由 `Path.Combine(request.ProjectDirectory, "obj", …)` 取，**不再是 `CacheDirectory`**（见 G1） |
| 新增 `Friend Shared Function WriteMirrorFiles(cacheDirectory, files, ByRef failurePath, ByRef failureReason) As Boolean` | 逐个文件：父目录 `CreateDirectory` → 已存在则跳过（write-once）→ `File.WriteAllBytes`；失败**不抛**，回填首个失败路径与原因并返回 `False`。抽成独立函数是为了让"镜像落盘 + 只写一次"能在**不 spawn 进程**的前提下直接测（`NuGetSourceConfigTests` L4-8/L4-9） |

- `WriteOnce` 语义**保持不变**：镜像文件与工程都只在 key 目录首次创建；因源已进 key（C3），"配置变了"必然换 key 目录。
- 镜像/工程写入失败 ⇒ 走 C5 新诊断，不落 `DotNetMissingExitCode`：镜像失败由 `WriteMirrorFiles` 的 `False` 转成 `RestoreOutcome.ConfigFailurePath`（退出码 `NuGetRestoreDiagnostics.ConfigFailureExitCode`），工程/global.json 写入失败仍是既有的兜底路径。

### C2.1 镜像后 NuGet 的实际落点

工程在 `<cache>\<mirrorRel>\project.vbproj` 时，NuGet 把 `obj` 写在**工程目录下**（即 `<cache>\<mirrorRel>\obj\`），因此读到 `obj` 的代码必须跟着工程目录走（见 G1）。

`global.json` 则由 **`dotnet` 进程的 CWD** 解析，不是工程目录：**[已运行]** 工程同目录放 `{"sdk":{"version":"99.0.100","rollForward":"disable"}}`，CWD 在别处时 `dotnet restore <工程>` 成功，CWD = 工程目录时同一命令失败（找不到 SDK 99.0.100）。故 `<cache>\global.json` 在改动前后同样不被生效，本设计不改变该行为（见 §十一 第 1 条）。

## 三、C3 缓存键接线（`NuGetRestoreCoordinator.vb`）

| 位置 | 现状（改动前） | 改动形状 |
| --- | --- | --- |
| `:88` | `Private ReadOnly _sourceFingerprint As String` | 改名/改语义为 `_sourceFingerprintOverride As String`（`Nothing` = 自动） |
| `:103` | `Optional sourceFingerprint As String = ""` | 默认值改为 `Nothing`；`:111` 改为 `_sourceFingerprintOverride = sourceFingerprint`（**保留显式注入能力给测试**：传 `""` 即钉死空串） |
| ctor | — | 另增可选参 `sourceFileSystem As ISourceConfigFileSystem = Nothing`（缺省 `PhysicalSourceConfigFileSystem.Instance`），供 C1 与 C5 的读取失败用例注入 |
| `PrepareCompilationAsync:124`（函数）/ `:137`（调用点） | — | 把 `filePath` 传下去：`EnsureRestoredAsync(scan.ValidRequests, filePath, cancellationToken)`（该处是唯一调用点，且 `filePath` 已在 `:124` 手中 ⇒ 增参，不存字段） |
| `:361-379` | 无收集步骤；`ComputeKey(…, _sourceFingerprint, …)`（`:374-379`）直接吃字段 | 在 `ShouldRestore` 门（`:353`）之后、SDK 判定（`:366-370`）之前收集：`Dim sourceConfig = NuGetSourceConfig.Collect(filePath, _sourceFileSystem)`；`HasFailure` ⇒ 立即返回 C5 诊断。随后 `Dim sourceFingerprint = If(_sourceFingerprintOverride, NuGetSourceConfig.Fingerprint(sourceConfig.Files))`，作为 `ComputeKey` 的入参 |
| `:381` | `GetCacheDirectory(cacheRoot, key)` | 不变（key 已含源指纹） |
| `:390` | `New RestoreRequest(cacheDirectory, projectXml, globalJson)` | 增参：`sourceConfig.Files`、`NuGetSourceConfig.ProjectRelativePath(filePath, sourceConfig.MirrorRoot)` |
| `:393` 之后 | 失败分支 | 先判 `outcome.ConfigFailurePath IsNot Nothing` ⇒ C5 诊断（不落 `ExitCodeToDiagnostic`），再走既有失败分支 |

- **向后兼容**：不传 `sourceFingerprint` 的生产装配（`VisualBasicScript.vb:172-179`）从"恒空串"变为"按目录链自动计算"；无目录级配置时自动值同样是 `""` ⇒ 与改动前逐字相同。
- **成本位置**：`EnsureRestoredAsync` 先过 `NuGetRestorePolicy.ShouldRestore`（`:353`）才走到 `:374` ⇒ 收集与指纹只在**真的要还原**时计算；同集重复提交（策略跳过）零额外开销。

### C3.1 key 的现有算法与已实测复现

`NuGetRestoreCache.ComputeKey`（`NuGetRestoreCache.vb:39-71`）的 descriptor 形状：

```
<canonicalKey1>;<canonicalKey2>;…|<hostTFM 长形式>|<rid>|<sdkVersion>|<sourceFingerprint>|net48|net
key = SHA-256(UTF-8(descriptor)) 的 hex
```

**[已运行] 反推验证**：本机真实缓存目录名

```
microsoft.data.sqlite,8.0.1;|.NETCoreApp,Version=v10.0|win-x64|10.0.401||net
  → 643d9e5be49b2ba3d1a2c4a9fa68c386ee6e07a521d9dc1e684998604f4c9ca1   （= 真实目录名，逐字节吻合）
```

要点（实施者勿漏）：包列表**每个条目后都跟 `;`**（含最后一个），其后才是第一个 `|`；`||` 即 `sourceFingerprint` 的空槽；`hostTFM` 是**长形式** `.NETCoreApp,Version=v10.0`（`NuGetPackageSession.vb:60-73` `FrameworkNameForRestore`）；`rid` 来自 `RuntimeInformation.RuntimeIdentifier`（`NuGetRestoreCoordinator.vb:560-564`）；`sdkVersion` 来自 `NuGetSdkResolver.SelectSdkVersion`（最高已安装，`NuGetRestoreRunner.vb:131-158`）；`net48` 分支产出字面量 `"net48"`，否则 `"net"`。

**本设计接入后**：`sourceFingerprint` 槽位不再恒空 —— 装的是**镜像配置集合的指纹**（C1 第 5 条），即 `SHA-256(按相对路径排序的 (相对路径小写 + \0 + 文件字节))` 的 hex。于是 key = f(包集, 长形式 TFM, rid, sdk, **配置集合指纹**, net48)；该槽位永远只装**定长 hex**，不装原始文件文本 ⇒ 无分隔符注入问题。

## 四、C4 空脚本路径规则（v1）

- `filePath` 为 `Nothing`/`""`（纯 REPL 输入）⇒ 空集合 + 空指纹 + `projectRelativePath = "project.vbproj"` ⇒ **行为与改动前一致**。
- `#load` 链上文件的所在目录**不**参与（Unresolved 登记）。
- 说明：REPL 的 `filePath` 取 `ScriptOptions.FilePath`，即 `GetScriptOptions`（`CommandLineRunner.cs:183`）里的 `New ScriptOptions(filePath:=scriptPathOpt ?? "", …)`（`:198`，`scriptPathOpt` 出自 `:139`）；`#load` 不改变它。

## 五、C5 诊断（新增一条）

- 新增诊断（`NuGetRestoreDiagnostics.vb`）：**VBI1012「NuGet source configuration could not be used」**，`messageFormat = "The NuGet configuration file '{0}' could not be used: {1}"`，严重度 **Error**，消息含**首个失败路径**与原因；工厂 `CreateConfigFailureDiagnostic(path, reason, location)`。沿用该文件既有的**内联 `DiagnosticDescriptor`** 风格（VBI1006–VBI1011 同族），不走 resx——本诊断是宿主本地诊断，与该族一致。
- 另有哨兵退出码 `ConfigFailureExitCode = Integer.MinValue + 1`（区别于 `DotNetMissingExitCode`），由镜像写入失败的 `RestoreOutcome` 携带。**注意**：配置失败靠 `outcome.ConfigFailurePath` 判别，绝不进 `ExitCodeToDiagnostic` 的翻译表，以免被误报成「SDK 缺失」或通用还原失败。
- 锚点：有 `filePath` 时锚该脚本文件首行（`CreateFileAnchorLocation` 用 `Location.Create(filePath, New TextSpan(0,0), 零长行位置)`，不读文件）；无 `filePath` 时 `Location.None`。
- 既有 NU1101/NU1301 → `ExitCodeToDiagnostic`（`NuGetRestoreDiagnostics.vb:100` 单请求重载 / `:134` 数组重载，调用点 `NuGetRestoreCoordinator.vb:399`）路径**不变**；本设计不新增还原失败消息族。
- **可见性收益**：配置生效时，"取源取自该配置"的外部证据取 **dgspec 的 `sources` 段**（`test-plan.md` 人工清单 G1 的判据）。注意当前实现的失败诊断只输出固定文案（VBI1007–VBI1009），源 URL 不进入诊断（见 §十一 第 6 条），故不能以诊断文案里的 URL 作判据。

## 六、C6 迁移与回收

- 布局变化后，既有 key 目录（工程在 key 根）不再被新写入命中；作为孤儿由既有 LRU 回收：`NuGetRestoreCache.Trim`（`:83-128`，`StaleAfter = 30d`、`MaxCacheDirectories = 256`、`TrimToDirectories = 128`）。**不做主动迁移**。
- `cacheRoot` 概念不变（`%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore`），`GetCacheDirectory` 不变。

## 七、C7 文档更新

1. `InternalDevDocs\tasks\vbi-nuget-reference\design-detailed.md:230`（"还原源指纹 | 仅测试注入…源指纹真正接线留待未来"）⇒ 该行内容改为**已接线**并指向本任务（指纹由 `NuGetSourceConfig.Fingerprint` 接入）。
2. `README.md` / `README.zh-CN.md` 的「Script files (.vbx)」一节各补一行：脚本旁的 `NuGet.config` 对 `#R "nuget:"` 还原生效（中英同步，结构与既有 bullet 一致）。
3. `NuGetRestoreCache.ComputeKey` / `NuGetRestoreRunner.RestoreRequest` 的 XML 文档注释同步（`sourceFingerprint` 不再是"生产恒空串"）。

## 八、C8 测试面

见 `test-plan.md`。要点：收集/镜像/指纹三层纯函数 + 假 runner 的宿主断言 + `ComputeKey` 敏感性；**不 spawn 进程、不网络**。

既有断言面不受 `RestoreRequest` 增参影响：`NuGetRestoreEngineTests` 的工程生成与字节稳定用例只吃**生成文本**（`NuGetRestoreEngineTests.vb:154,161,166-172`），`FakeRestoreRunner.LastRequest` 仅被 `ProjectXml` 断言使用（`NuGetRestoreCoordinatorTests.vb:520-521`）。

## 九、实现约束（G1–G5）

本设计只有在下列约束全部满足时才成立；每条给出违反后的表现，便于实施与验收对照。

| # | 约束（规格要求） | 违反后果 |
| --- | --- | --- |
| **G1** | 读 `project.nuget.cache` / `project.assets.json` 必须从 `RestoreRequest.ProjectDirectory` 派生 `obj` 路径，不用 `CacheDirectory` | 工程落在镜像深度时 `NuGetCacheWritten` 恒为 False ⇒ 每次还原都被判失败，报 VBI1009（exit code 0）——功能整体不可用 |
| **G2** | `Collect` 必须把"配置文件不可读"作为结果的一部分返回（`FailedPath`/`FailureReason`/`HasFailure`），不抛异常；镜像**写入**失败另由 `RestoreOutcome.ConfigFailurePath` 承载 | 配置读取失败要么没有诊断通道（C5 落空），要么以异常逸出（无法锚到脚本，且破坏纯函数纪律） |
| **G3** | 镜像根必须由 `Collect` 从**绝对命中路径**算出并随结果返回；`ProjectRelativePath(scriptFilePath, mirrorRoot)` 的第二个入参是已算好的镜像根 | 入参只有相对路径时推不出"脚本目录到镜像根的距离" ⇒ 镜像链不完整（外层配置及其凭据丢失） |
| **G4** | 镜像根取链上**最靠上**的目录级 `NuGet.config` 的父目录，不是"最靠近脚本"的那份 | 只镜像最内层配置 ⇒ 外层源、凭据与 `<clear/>` 之外的语义全丢，且与 `dotnet restore` 的有效源不一致 |
| **G5** | 写工程与 `global.json` 前必须 `Directory.CreateDirectory(request.ProjectDirectory)`（`WriteOnce` 不创建目录） | 写工程抛 `DirectoryNotFoundException`，被 `RestoreAsync` 的兜底 `Catch` 吞为 `DotNetMissingExitCode` ⇒ 报 **VBI1006「找不到 SDK」**，症状与病因完全错位 |

## 十、改动文件清单

| 文件 | 类型 |
| --- | --- |
| `Scripting\VisualBasic\Hosting\NuGetSourceConfig.vb` | **新增**（`SourceConfigFile` / `NuGetSourceConfigResult` / `ISourceConfigFileSystem` / `PhysicalSourceConfigFileSystem` / `NuGetSourceConfig`） |
| `Scripting\VisualBasic\Hosting\NuGetRestoreRunner.vb` | 改（`RestoreRequest` + `RestoreOutcome` + `WriteMirrorFiles` + `WriteProjectFiles` + `obj` 改由 `ProjectDirectory` 派生） |
| `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb` | 改（收集 → 指纹 → key；`filePath` 增参；`sourceFileSystem` seam；两个 C5 分支；`CreateFileAnchorLocation`） |
| `Scripting\VisualBasic\Hosting\NuGetRestoreDiagnostics.vb` | 改（VBI1012 + `ConfigFailureExitCode` + 工厂） |
| `Scripting\VisualBasic\Hosting\NuGetRestoreCache.vb` | 改（仅 XML 文档注释：key 成分补源指纹） |
| `Scripting\VisualBasicTest\NuGetSourceConfigTests.vb` | **新增**（L1 / L3 / L4 三层用例，清单见 `test-plan.md`） |
| `InternalDevDocs\tasks\vbi-nuget-reference\design-detailed.md:230` | 文档（源指纹行 ⇒ 已接线） |
| `README.md` / `README.zh-CN.md` | 文档（「Script files (.vbx)」各补一行） |
| 本任务的 `README.md` / `design-overview.md` / `test-plan.md` | 文档（交叉一致） |

## 十一、Unresolved（不在本设计范围）

1. **`global.json` 不随工程落点生效**（见 C2.1）：它由 `dotnet` 进程 CWD 解析，`<key>\global.json` 因而不被读取。要让它生效，需给 `ProcessStartInfo` 显式设 `WorkingDirectory` 或另择放置位置——前者改变宿主 CWD 语义，影响脚本相对路径与 `#load` 解析，须单独评估。
2. `#load` 链上文件的所在目录是否参与配置链（v1 只认主脚本 `filePath`；REPL 用 `ScriptOptions.FilePath`）。
3. 命令行显式开关族（`/packagesource:` / `/configfile:` / `/packages:`）与目录链的优先级。
4. 全局包目录（`NUGET_PACKAGES` / `<RestorePackagesPath>`）是否应由 vbi 接管。
5. 同级多个 `NuGet.config` 拼写（`NuGet.config` 与 `nuget.config` 并存）时 NuGet 自身的取舍未核实；本设计按 `String.CompareOrdinal` 取首个并以规范拼写镜像，属确定性折中。
6. **还原失败详情落在 stdout**（与本次改动无关的既有现象）：`dotnet restore` 失败时错误文本走 stdout，而 `DotNetRestoreRunner` 只把 `stderr` 交给翻译表（`--list-sdks` 除外）⇒ `ExitCodeToDiagnostic` 针对 `NU1101`/`NU1301` 的映射不触发，诊断退化为 VBI1009 且摘要为空。**[已运行]** 缺源还原失败时 vbi 报 VBI1009 且消息摘要为空；改动前的 vbi 表现相同，故属既有问题。建议独立任务让运行器把 stdout 也纳入错误文本。
7. 镜像树的祖先目录（缓存根之上一路到盘符根）若存在 `NuGet.config`，该文件会进入镜像侧的还原、却不属于脚本的真实配置链，也不进指纹。**[已运行]** 本机该路径上 0 个此类文件。NuGet 没有"停止向上查找"的指令，故只能检测（并入指纹或报诊断），不能结构性消除。

## 十二、备选方案与取舍

### 十二·一 备选：透传 `--configfile`，省掉源指纹（成立的内核）

源指纹存在的**唯一**理由是：配置文件被**复制**进 write-once 的 key 目录，过期副本一旦写入就永不更新。若改为把配置**按绝对路径现场读取**交给还原（`dotnet restore --configfile <path>`），key 目录里就没有会被固化的过期输入，指纹即无必要——正确性由 NuGet 自己的 no-op 机制兜住：`project.nuget.cache` 的 `dgSpecHash` 覆盖 dgspec，而 dgspec 含 `"sources"` 段（README「共享源码事实」实测），配置改动 ⇒ dgspec 变 ⇒ 真还原。同理"脚本旁没有 `NuGet.config` 就不透传"保持改动前的行为。

### 十二·二 实测：`--configfile` 替换整条配置链

**[已运行]** 有效源（dgspec 判读；工程在 `proj\`，被透传的配置在 `cfg\`，CWD 在两者之外）：

| 运行 | 条件 | dgspec 里的有效源 |
| --- | --- | --- |
| A 基线 | 无 `--configfile`；`proj\NuGet.config` = `<clear/>` + proj-local | `proj-local.invalid` |
| B1 | `--configfile cfg\NoClear.config`（**无** `<clear/>`）；`proj\NuGet.config` 同时存在 | **仅** `passed-cfg.invalid` |
| B2 | `--configfile`；**无**工程目录配置 | **仅** `passed-cfg.invalid`（`api.nuget.org` 与用户/机器级源**全部消失**） |

- B2 ⇒ 环境级源被丢弃：`--configfile` 不是"替换 user 槽"，而是"这就是全部配置"。
- B1 ⇒ 工程目录链被忽略（proj-local 没进有效源）。
- 凭据与源同在一个配置文件，故用户级 `packageSourceCredentials` 按同一机制失效（本机用户级配置无凭据条目，未能直接观测；由"该文件不被读取"推定）。

### 十二·三 不采用它的理由

1. **丢失配置链**：单文件无法表达 `repo\NuGet.config` + `repo\sub\NuGet.config` 的链；必须二选一，另一份的源/凭据/`<clear/>` 静默消失（B1 证明链不再参与）。而"镜像层级"方案的全部意义就是把整条链原样交给 NuGet。
2. **丢失环境级合并**：与 `design-overview.md` §三 的「`<clear/>` / `disabledPackageSources` / 凭据 / 环境变量展开 / 机器级合并全部仍由 NuGet 自己算」相反（B2）。
3. **行为断崖**：有配置的脚本失去 `nuget.org` 兜底，无配置的脚本保留——同一目录两种世界，且与"无配置场景逐字不变"的承诺形成不对称。
4. **仍丢失 per-config 隔离**：与下一备选同样，同包集 + 不同配置会共享一个 key 目录，多进程并发时会互相刷新/复用对方的配置上下文。

### 十二·四 备选：保留镜像但每次刷新，去掉指纹

保留镜像、**每次还原都刷新镜像文件**（镜像文件不做 write-once）并**删掉指纹**：零行为变化地删掉指纹——`WriteOnce` 才是逼出指纹的那个选择——代价是放弃「同一 key ⟺ 同一组还原输入」这一不变量与 per-config 隔离。

### 十二·五 取舍

本设计采用**镜像链 + 源指纹**：用"一份缓存目录对应一组确定的还原输入"换取全链保真、环境级源与凭据不受影响，以及并发下的配置隔离。两个备选的代价见 §十二·二 与 §十二·四。
