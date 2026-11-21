# 手动验证清单：可嵌入脚本宿主包

> 本清单只覆盖**有副作用、无法进单测**的集成验证（打包、net472、真实 NuGet 还原、身份检测）。全部在仓库外临时工程执行；结果与日期回填。

## 前置

1. `dotnet pack Installer\Scripting\Nukepayload2.Compilers.VBScriptDotNet.Scripting.Package.vbproj -c Release`
2. 产物：`Installer\Scripting\bin\Release\Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg`
3. 临时工程目录：仓库外新建 `%TEMP%\vbs-f11-smoke\`（net10 与 net472 各一份），`NuGet.config` 添加本地源指向上述 `bin\Release`。

## A. net10 冒烟

- [x] 工程引用包（`PrivateAssets` 不设），`Host.CreateApplicationBuilder` + `AddVBScripting`
- [x] `RunCodeAsync("Console.WriteLine(""hi"")")` → 输出 `hi`，`ExitCode=0`
- [x] `.vbx` 文件 + `#Load` 另一个文件 → 两个文件的顶层语句都执行
- [x] `Return 3` → `ExitCode=3`
- [x] `#R "nuget:Microsoft.Data.Sqlite, 8.0.1"` → 还原成功并可用
- [ ] 自定义 `INuGetRestoreService`（返回空）→ 不联网、不 spawn `dotnet`，`#R "nuget:"` 报诊断（F12 未覆盖：需另建仓库外宿主工程并重新打包；同一行为已由 F8 单测的替换服务用例覆盖，见 `HostingNuGetTests`）

## B. net472 冒烟（发布硬门）

- [x] net472 控制台工程引用包 → 编译成功（8 个 polyfill 进入输出）
- [x] 同上能力逐项勾选
  - [x] `Console.WriteLine` → 屏幕输出 `net472-console-ok`，`ExitCode=0`
  - [x] `#Load` 相对 `WorkingDirectory` + `Return 3` → 两行输出都在，`ExitCode=3`
  - [x] `#R "nuget:"` 托管包 → 可用（`Newtonsoft.Json` 查出 `json-ok:42`，`ExitCode=0`）
  - [x] `#R "nuget:"` native 包 → 预期拒绝：宿主能力诊断 `VBI1010`、`ExitCode=1`（首轮曾因还原工程 TFM 非法而整体失败，根因与修复见文末两条 F11 记录）
- [ ] `UseDesktopReferences` 两态
  - [x] `True` → net472 脚本可用 `System.Windows.Forms.Label`（`ExitCode=0`）
  - [ ] `False` → 本机无法复现「不可用」：未加该引用时平台元数据解析器仍能从 GAC 解析桌面程序集（见文末修复记录）
- [x] 记录 net472 输出目录的 `System.*.dll` 清单，与设计 8 个 polyfill 对照

## C. 身份检测

- [x] 临时工程加 `Microsoft.CodeAnalysis.CSharp` 包引用 → 构建失败，错误含包名与 `VBScriptAllowRoslynPackages`
- [x] 加 `<VBScriptAllowRoslynPackages>true</VBScriptAllowRoslynPackages>` → 构建通过（运行期是否冲突不在本项范围）
- [ ] 只经**传递依赖**引入 `Microsoft.CodeAnalysis*` 的包 → 构建失败、错误列出该包（与直接引用同判据；证据见 F9 日志 `17-verifier-f9.md`，本清单不复跑）

## D. 包内容核对

- [x] `lib/net10.0/`：facade + 4 程序集 + satellite；无 CSharp 编译器
- [x] `lib/net472/`：facade + 4 程序集 + 8 polyfill
- [x] nuspec 依赖：net10 六项 `Microsoft.Extensions.*`，net472 另加 `Microsoft.Bcl.AsyncInterfaces`（共七项）；无 `Microsoft.CodeAnalysis*`
- [x] `buildTransitive/` targets 存在；`README.md` 入包且首段为运行时定位

## E. 对等抽样

- [ ] 取一段 `Print(...)` + `#Load` + `Return 3` 的 `.vbx`，与 `Samples\FileArgs.vbx`、`Samples\SqliteNuGetDemo.vbx`：`vbi` 与宿主 runner 的 stdout、退出码一致（F12 只补跑 `vbi` 侧的 `Print` + `Return` 抽查；`Print`+`#Load`+`Return` 的宿主对等由 F10 `HostingParityTests` 六格与 F11 net10 冒烟覆盖；`FileArgs.vbx` 需交互、`SqliteNuGetDemo.vbx` 的 net472 一侧被 B 段缺陷阻塞，未跑）

---

## 2026-11-18 F11 实测记录

**结论**：net10 冒烟全绿；**net472 发布硬门未通过**——`#R "nuget:"` 在 net472 宿主上完全不可用（生成的临时还原工程 TFM 非法 → `NETSDK1013`）。net472 其余三项（`Console.WriteLine`、`#Load`、`Return 3`）与工程构建（8 个 polyfill 进入输出）均通过。按硬边界未改产品代码。

> 本节保留修复前的原始结论与证据；缺陷已修，修复点与修复后复跑见文末「2026-11-18 F11 修复后复跑记录」。

**环境**：Windows 10.0.26200 x64；`dotnet --version` = 11.0.100（同机另有 8.0.428、10.0.100-rc.1）；net10 运行时 10.0.15；.NET Framework 4.8.9363.0（NDP `Release` = 533509）；net472 targeting pack 已安装。

### 1. 打包

```
dotnet pack Installer\Scripting\Nukepayload2.Compilers.VBScriptDotNet.Scripting.Package.vbproj -c Release -o %TEMP%\vbs-f11-smoke\feed --nologo
```

- 退出码 0；产物 `Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg`
- 字节数 10,153,917
- SHA256 `624536FD478795CA34893926B8497D70C5427CE889A77679A839B325FE746050`
- 该包是**修复前**产物；修复后重新打包的字节数与 SHA256 见下一节

### 2. net472 冒烟

工程在 `%TEMP%\vbs-f11-smoke\net472\`：C# 控制台（顺带验证跨语言），`TargetFramework=net472`，`AutoGenerateBindingRedirects=true`，引 `Nukepayload2.Compilers.VBScriptDotNet.Scripting` 2.0.0-RC3（不设 `PrivateAssets`）与 `Microsoft.Extensions.Hosting` 10.0.0（`CreateApplicationBuilder` 在 Hosting 包、不在 Abstractions 内）；`NuGet.config` 先本地 feed 后 nuget.org。程序用 `Host.CreateApplicationBuilder` + `AddVBScripting(options => WorkingDirectory = <base>\scripts)` + `UseConsoleOutput()` 取 `IVBScriptRunner`。构建 0 警告 0 错误（退出码 0）；运行退出码 1（第 4 步失败）。

- 步骤 2 `RunCodeAsync("Console.WriteLine(\"net472-console-ok\")")`：屏幕出现 `net472-console-ok`，`ExitCode=0`，0 诊断。**通过**
- 步骤 3 `RunFileAsync("main.vbx")`，`main.vbx` = `#Load "child.vbx"` + `Print("net472-load-ok")` + `Return 3`，`child.vbx` = `Print("net472-child-ok")`：`Output="net472-child-ok\r\nnet472-load-ok\r\n"`，`ExitCode=3`，0 诊断（`#Load` 相对 `WorkingDirectory` 解析成立）。**通过**
- 步骤 4a `#R "nuget:Microsoft.Data.Sqlite, 8.0.1"`（`sqlite.vbx`，与 `Samples\SqliteNuGetDemo.vbx` 同形）：`ExitCode=1`，诊断 `Error VBI1009: NuGet restore failed (exit code 1): `，锚在 `#R` 行。**失败**
- 步骤 4b `#R "nuget:Newtonsoft.Json, 13.0.3"`（纯托管包，排除 native 因素）：同样 `ExitCode=1`、`VBI1009`。**失败**
- 步骤 5 身份：`Environment.Version=4.0.30319.42000`，`RuntimeInformation.FrameworkDescription=.NET Framework 4.8.9363.0`，核心程序集 `mscorlib 4.0.0.0`，facade `5.9.0.0`；`core-assembly-target-framework=<none>`（mscorlib 没有 `TargetFrameworkAttribute`），冒烟应用与 facade 自身均为 `.NETFramework,Version=v4.7.2`。

输出目录的 `System.*.dll`：设计要求的 8 个 polyfill 全部在（`System.Buffers`、`System.Collections.Immutable`、`System.Memory`、`System.Numerics.Vectors`、`System.Reflection.Metadata`、`System.Runtime.CompilerServices.Unsafe`、`System.Text.Encoding.CodePages`、`System.Threading.Tasks.Extensions`）；另有 4 个由宿主 `Microsoft.Extensions.Hosting` 带入（`System.Diagnostics.DiagnosticSource`、`System.IO.Pipelines`、`System.Text.Encodings.Web`、`System.Text.Json`）。

**缺陷与根因（未修）**：生成的临时还原工程 `%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>\bin\Release\net472\scripts\project.vbproj` 里写的是

```xml
<TargetFramework>.NET Framework 4.8.9363.0</TargetFramework>
```

`dotnet restore` 原文：

```
error NETSDK1013: 未识别 TargetFramework 值“.NET Framework 4.8.9363.0”。可能是因为拼写错误。如果拼写正确，必须显式指定 TargetFrameworkIdentifier 和/或 TargetFrameworkVersion 属性。
```

根因：`Scripting\VisualBasic\Hosting\NuGetPackageSession.vb` 的 `NuGetHostCapability.CreateDefault()` 读 `GetType(Object).Assembly` 的 `TargetFrameworkAttribute`；.NET Framework 的 mscorlib 运行时没有该特性（步骤 5 的 `<none>`），于是回退到 `RuntimeInformation.FrameworkDescription`（`.NET Framework 4.8.9363.0`）。该值既不是 TFM，也不以 `.NETFramework,` 开头，因此 `IsNet48=false`、`ShortTargetFramework` 未被缩短，还原工程拿不到 `net48`，也不会加 `Microsoft.NETFramework.ReferenceAssemblies.net48`。net10 上同一逻辑正常，因为 `System.Private.CoreLib` 带 `.NETCoreApp,Version=v10.0`。

环境对照：同机手工建等价 `net48` 还原工程（`Microsoft.Data.Sqlite` 8.0.1 + `Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3，`RuntimeIdentifier=win`）→ `dotnet restore` 退出码 0。失败来自生成的 TFM，不是环境。

附带观察（不阻断）：还原失败时 `VBI1009` 的消息体为空——还原层只取 stderr，而 MSBuild 的 `NETSDK1013` 写在 stdout。

### 3. net10 冒烟

`%TEMP%\vbs-f11-smoke\net10\`，同一 `Program.cs`、`TargetFramework=net10.0`。构建 0 警告 0 错误；运行退出码 0，五项全 PASS：

- 步骤 2：`net10-console-ok`，`ExitCode=0`，0 诊断
- 步骤 3：`Output="net10-child-ok\r\nnet10-load-ok\r\n"`，`ExitCode=3`，0 诊断
- 步骤 4a：`sqlite-ok:42`，`ExitCode=0`（native `e_sqlite3` 经探测根命中）
- 步骤 4b：`json-ok:42`，`ExitCode=0`
- 步骤 5：`Environment.Version=10.0.15`，`FrameworkDescription=.NET 10.0.15`，核心程序集 `System.Private.CoreLib 10.0.0.0`，restore 上下文 `TargetFramework=.NETCoreApp,Version=v10.0`、`RuntimeIdentifier=win-x64`；facade `5.9.0.0`

### 4. 其余清单项（本轮实测）

- §C 身份检测：直接引 `Microsoft.CodeAnalysis.CSharp` 4.14.0 的临时工程构建失败，错误列出 `Microsoft.CodeAnalysis.Common, Microsoft.CodeAnalysis.CSharp` 并给出 `VBScriptAllowRoslynPackages=true`；加该属性后构建退出码 0。
- §D 包内容：`lib/net10.0/` = facade + 4 程序集 + 13 个 satellite 文化目录（每目录 4 个资源 dll），无 `Microsoft.CodeAnalysis.CSharp.dll`；`lib/net472/` = 同上另加 8 个 polyfill；nuspec net10 组 6 项、net472 组 7 项（含 `Microsoft.Bcl.AsyncInterfaces` 10.0.0），无 `Microsoft.CodeAnalysis*`，无 `developmentDependency`；`buildTransitive/...targets` 与 `README.md` 都在包内，README 首段为「运行时库，非构建工具」。
- §E 对等（部分）：同一 `main.vbx`（`Print` + `#Load` + `Return 3`）经 `vbi`（net10 Release 构建）与宿主 runner：stdout 两侧均为 `"net10-child-ok"` / `"net10-load-ok"`，退出码均 3；vbi 另往 stderr 写一行 host 提示（不属于脚本 stdout）。`Samples\FileArgs.vbx` 需 `Console.ReadKey` 交互未跑；`Samples\SqliteNuGetDemo.vbx` 的 net472 一侧被 B 段缺陷阻塞。

### 5. 未覆盖 / 降级

- net472 `#R "nuget:"`（native 的 `Microsoft.Data.Sqlite`、纯托管的 `Newtonsoft.Json` 两种）均不可用 → B 段硬门阻断；修好还原 TFM 检测后应复跑，并同时验证「native 包得到 VBI1010 宿主能力诊断」与「托管包可用」两条。
- §A 最后一条（自定义 `INuGetRestoreService`）与 §B 的 `UseDesktopReferences` 未在本轮人工跑（前者 F8 单测覆盖；后者需再加一段宿主脚本）。
- §E 的 `FileArgs.vbx`（交互）与 `SqliteNuGetDemo.vbx`（net472）未跑。
- 观察（非本包证据）：`Interactive\vbi` 的 net48 可执行文件跑脚本时在 `VisualBasicScriptCompiler` 静态构造报 `System.Private.Xml.Linq.dll` 缺失；该产物早于 F5–F10，不代表当前源码，未据此下结论。

### 6. 证据位置

仓库外 `%TEMP%\vbs-f11-smoke\`：`pack.log`、`net472-build.log`、`net472-run.log`、`net10-build.log`、`net10-run.log`、`identity-direct-build.log`、`identity-allowed-build.log`、`control-net48-restore.log`、`net472-generated-restore.log`、`parity-vbi.stdout.log`、`parity-vbi.stderr.log`、`vbi48-managed.log`，以及 `feed\`、`net472\`、`net10\`、`control\`、`identity-direct\`、`identity-allowed\`、`parity\`、`parity-net48\` 目录。

---

## 2026-11-18 F11 修复后复跑记录

**结论**：上一节记录的 net472 缺陷已定位并修复，**net472 与 net10 冒烟全部通过**（两个 TFM 的运行退出码均 0），单测 872/0。

### 1. 缺陷与修复点

缺陷：net472 宿主把临时还原工程的 `<TargetFramework>` 写成 `.NET Framework 4.8.9363.0`，`dotnet restore` 报 `NETSDK1013`，`#R "nuget:"` 在 net472 上完全不可用（上一节证据）。

- **宿主 TFM 探测**（`Scripting\VisualBasic\Hosting\NuGetPackageSession.vb`）：不再读核心库（.NET Framework 的 mscorlib 运行期没有 `TargetFrameworkAttribute`），改为按可靠度取「入口程序集 `TargetFrameworkAttribute` → `AppContext.TargetFrameworkName` → `RuntimeInformation.FrameworkDescription` 映射」（`.NET Framework 4.8.9363.0`→`.NETFramework,Version=v4.8`，`.NET 10.0.15`→`.NETCoreApp,Version=v10.0`）。每个分支都返回 `New FrameworkName(...)` 可解析的值。
- **版本感知的 TFM**：`ShortTargetFramework` / `FrameworkNameForRestore` 不再把 .NET Framework 写死 `net48` / `v4.8`；4.7.2 宿主得到 `net472` / `.NETFramework,Version=v4.7.2`。注入短名 `net48` 的测试路径行为不变。
- **参考程序集包随 TFM**（`NuGetProjectGenerator`）：net472 工程引用 `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3（此前恒为 net48 包）。
- **netfx 的 RID**：`GetRuntimeIdentifier()` 在 netstandard2.0/netfx 下改为「OS 族 + 进程架构」（`win-x64`）。实测反证：保留裸 `win` 时 NuGet 选不到 native 资产，native 包会退化成本节稍后要排除的裸运行期失败（`Library e_sqlite3 not found`），而不是 VBI1010。
- **VBI1009 空消息**：失败时把 stderr + stdout 合成诊断文本（去空行、去重复、上限 8000 字符），摘要优先取错误行；MSBuild 的 `NETSDK1013` / `NU1102` 写在 stdout，此前被丢弃。
- **公开快照一致**：`NuGetRestoreContext.TargetFramework`（facade）改用同一套宿主 TFM 探测，不再显示自由格式的描述字符串。

### 2. 打包（修复后）

```
dotnet pack Installer\Scripting\Nukepayload2.Compilers.VBScriptDotNet.Scripting.Package.vbproj -c Release -o %TEMP%\vbs-f11-smoke\feed --nologo
```

- 退出码 0；产物 `Nukepayload2.Compilers.VBScriptDotNet.Scripting.2.0.0-RC3.nupkg`
- 字节数 10,155,746
- SHA256 `6B71DF6B82EC6976B99716EBF5B2326E79F81CCDE02C44D3D154D6E09CD6F9ED`

### 3. net472 复跑（EXIT=0，失败计数 0）

构建 0 警告 0 错误。逐条：

- 步骤 2 `Console.WriteLine` → `net472-console-ok`，`ExitCode=0`，0 诊断
- 步骤 3 `#Load`+`Return 3` → `Output="\"net472-child-ok\"\r\n\"net472-load-ok\"\r\n"`（`Print` 带引号是对象格式化的既有行为），`ExitCode=3`，0 诊断
- 步骤 4a `#R "nuget:Microsoft.Data.Sqlite, 8.0.1"` → `ExitCode=1`，`Error VBI1010: Package 'Microsoft.Data.Sqlite' contains native assets. The .NET Framework (net48) host cannot probe native libraries; ...`（**宿主能力诊断，不是裸失败**）
- 步骤 4b `#R "nuget:Newtonsoft.Json, 13.0.3"` → `json-ok:42`，`ExitCode=0`，0 诊断
- 步骤 6a `UseDesktopReferences=true` → `Print(GetType(System.Windows.Forms.Label).FullName)` 输出 `System.Windows.Forms.Label`，`ExitCode=0`
- 步骤 7 失败诊断（`#R "nuget:Newtonsoft.Json, 99.99.99"`）→ `VBI1009` 带 `<临时工程> : error NU1102: 找不到版本为 (>= 99.99.99) 的包 Newtonsoft.Json`，消息不再为空
- 身份：`Environment.Version=4.0.30319.42000`、`FrameworkDescription=.NET Framework 4.8.9363.0`、核心程序集 `mscorlib 4.0.0.0`、facade `5.9.0.0`；restore 上下文 `TargetFramework=.NETFramework,Version=v4.7.2`、`RuntimeIdentifier=win-x64`
- 临时还原工程：`<TargetFramework>net472</TargetFramework>`、`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`、`Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3；对该工程 `dotnet restore` **退出码 0**

### 4. net10 复跑（EXIT=0，失败计数 0）

构建 0 警告 0 错误；行为与修复前一致：

- 步骤 2 → `net10-console-ok`，`ExitCode=0`
- 步骤 3 → 两行输出，`ExitCode=3`
- 步骤 4a → `sqlite-ok:42`，`ExitCode=0`
- 步骤 4b → `json-ok:42`，`ExitCode=0`
- 步骤 7 → `VBI1009` 带 `NU1102` 原文（stdout 合并生效）
- 身份：`Environment.Version=10.0.15`、`.NET 10.0.15`、`System.Private.CoreLib 10.0.0.0`、restore 上下文 `.NETCoreApp,Version=v10.0` / `win-x64`、facade `5.9.0.0`

### 5. 回归

- `dotnet Scripting\VisualBasicTest\bin\Debug\net10.0\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated` → `TestsTotal=872, TestsFailed=0, TestsNotRun=0, TestsSkipped=0`，退出码 0（修复后跑了两次，均绿），基线未漂移。
- net10 行为不变由上面第 4 节与单测共同证明。

### 6. 未覆盖 / 无法复现

- `UseDesktopReferences=False` 的「桌面类型不可用」在本机无法复现：即使不追加该引用，平台元数据解析器（`RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`）仍能从 GAC 解析 `System.Windows.Forms`，脚本 `ExitCode=0`（已用独立进程复核，排除同进程先行加载的干扰）。正向（`True`）已实测可用。
- `#R "nuget:"` 失败路径的原始症状——修复前是 `VBI1009` 空消息 + `NETSDK1013`，修复后已无法用同一输入复现（TFM 已合法），改用 `NU1102` 失败用例证明 stdout 合并生效。
- §A 最后一条（自定义 `INuGetRestoreService`）与 §E 的交互样例仍未跑（同上一节）。

### 7. 证据位置

仓库外 `%TEMP%\vbs-f11-smoke\`：`pack-final.log`、`net472-build.log`、`net472-run-final.log`、`net10-build.log`、`net10-run-final.log`、`net472-run-desktop-off-fresh.log`、`tests-final.log`、`tests-final2.log`、`net472-generated-restore-final.log`，以及 `feed\`、`net472\`、`net10\`、`parity\`。修复前的 `net472-run.log`、`net472-generated-restore.log`、`control-net48-restore.log` 保留，用于「原错误 → 根因 → 修复后」对照。仓库内改动为产品代码，见实施日志 `21-implementer-f11-fix.md`。

---

## F12 修复前的失败读数（2026-11-18）

七门中 Phase2、Syntax、Symbol 三门为红，Semantic、IOperation、Emit、CommandLine 为绿；两类红的根因（SDK 常量折叠漂移、Syntax 门基线陈旧）与处置，以及修复后的复跑读数，见 `test-plan.md` §7、任务 README「F12 回归残差」与实施日志 `27-implementer-gates-and-recycling.md`，本节不留读数副本。

**`vbi` 抽查**（net10，`vbi.dll` 强制重建后）：脚本 `Print("f12")` + `Return 4`（含 `#Load`）→ stdout `"f12"`、`7`，退出码 4；stdin REPL `Print(6 * 7)` → `42`，退出码 0。stderr 的 `The script has error…` 横幅是既有非零退出路径（`Vbi.vb:69-71`），不是回归引入。

**仍未覆盖的清单项**：

- §A 自定义 `INuGetRestoreService`：未重复搭建仓库外宿主工程；同一替换行为由 F8 单测覆盖（见正文标注）。
- §B `UseDesktopReferences=False`：本机无法复现「不可用」（平台解析器仍从 GAC 解析桌面程序集），已保留原说明。
- §E `Samples\FileArgs.vbx`（交互）与 `SqliteNuGetDemo.vbx`（net472 一侧）：未跑；宿主侧对等由 F10/F11 覆盖。
