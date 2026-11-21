# 测试计划：可嵌入脚本宿主包

> 依据：`../../meetings/meeting-embeddable-scripting-host.md` RESOLUTION R1–R17、本任务采用的四层矩阵（L1 解析 / L2 语义 / L3 API / L4 CLI 对等）。本任务**不改编译器语义**，因此 L1/L2 只保留「不适用」论证与回归确认，主力在 L3 API 与 L4 CLI 对等，另加 L0 打包层。

## 1. 无副作用纪律（硬约束）

单测禁止：网络请求、启动进程、写仓库文件、注册表写入。具体到本任务：

- NuGet 相关一律用**假实现**（`INuGetRestoreService` / `INuGetRestoreRunner` / `INuGetPackageResolver` 的测试替身），不 spawn `dotnet`、不联网；真实 restore 只在 F11 冒烟（人工、仓库外临时工程）验证。
- `#Load` 用内存 `SourceReferenceResolver` 替身（现有 `Scripting\VisualBasicTest` 已有同类 harness），不落盘。
- 输出用 `StringWriter`；不写 `Console`（`ConsoleScriptOutput` 的测试只断言 writer 选择，不真的写控制台）。例外：L4 对等与 F11 冒烟属人工/集成验证，允许 `UseConsoleOutput()` + `Console.SetOut` 捕获。
- 取消/重入测试用 `TaskCompletionSource` 与 `Interlocked` 计数，不用 `Thread.Sleep` 做时序断言。

## 2. 分层矩阵

| 层 | 内容 | 本任务覆盖 |
|---|---|---|
| L0 构建/打包 | nupkg 资产布局、TFM、polyfill、buildTransitive 检测 | 新增（F9/F11） |
| L1 解析 | VB 语法/指令解析 | **不适用**：`Compilers\VisualBasic\Portable\` 零改动；唯一的解析是 rsp 行解析，归 L3 单测 |
| L2 语义 | 绑定/类型/提交语义 | **不适用**：同上；脚本方言语义由既有 `Scripting\VisualBasicTest` 回归覆盖 |
| L3 API | facade 公共契约、DI、runner、restore 适配、错误模型 | 主力（F6–F10） |
| L4 CLI 对等 | `vbi` 行为不回归 + 同一脚本两路径输出一致 | F5/F10/F12 |

## 3. L3 用例

### 3.1 默认集与选项（T-DEF）

| ID | 用例 | 断言 |
|---|---|---|
| T-DEF-1 | 解析嵌入 `vbi.coreclr.rsp` | `/r:` 与 `/imports:` 集合与仓库文件逐项相等（`OrdinalIgnoreCase`） |
| T-DEF-2 | `AddVBScripting` 后默认 options | `Imports` 含 `System`、`System.IO`、`System.Collections.Generic`、`System.Console`、`System.Diagnostics`、`System.Dynamic`、`System.Linq`、`System.Text`、`System.Threading.Tasks`；`References` 含 `System.Runtime`、`Microsoft.VisualBasic`、`System.Private.Xml.Linq` |
| T-DEF-3 | 用户先加 `Imports System.Text.Json` | 默认集不覆盖用户项，重复项去重 |
| T-DEF-4 | `UseDesktopReferences=True` | net472：追加 `System.Drawing`、`System.Windows.Forms`；net10：忽略该选项并记一条 `ILogger` Warning |
| T-DEF-5 | `GlobalsType` 不派生 `VBScriptGlobals` | `VBScriptHostException`，消息含修复建议 |

### 3.2 runner 与结果（T-RUN）

| ID | 用例 | 断言 |
|---|---|---|
| T-RUN-1 | `RunCodeAsync("Print(1)")` | `ExitCode=0`；`Output` 含 `1`；无诊断 |
| T-RUN-2 | `RunCodeAsync("Return 42")` | `ExitCode=42`、`ReturnValue=42` |
| T-RUN-3 | 编译错误 | `ExitCode=1`；`Diagnostics` 非空且 id 以 `BC` 开头；不抛异常 |
| T-RUN-4 | 运行期异常 | `Exception` 非空；`ExitCode = HResult`；不抛 |
| T-RUN-5 | `CompileCodeAsync` 含警告 | 返回诊断含 warning；未执行（`Output` 为空、`ReturnValue` 为 `Nothing`） |
| T-RUN-6 | `EvaluateAsync(Of Integer)("1+1")` | 返回 `2` |
| T-RUN-7 | 宿主已停止后调用 runner | `VBScriptHostException`（不静默执行） |
| T-RUN-8 | 同一 runner 并发两次 `RunCodeAsync` | 恰一次 `VBScriptBusyException`；另一次正常完成 |
| T-RUN-9 | 取消令牌在脚本运行中触发 | 抛 `OperationCanceledException`（一次性入口钉死为异常；session 走 faulted，见 T-SES-8） |
| T-RUN-10 | 自定义 `VBScriptGlobals` 子类的 `Print` 绑定 | 脚本内 `Print("x")` 命中用户重写 |

### 3.3 DI 与覆盖（T-DI）

| ID | 用例 | 断言 |
|---|---|---|
| T-DI-1 | 用户先注册自定义 `IScriptOutput` | 生效（默认不覆盖） |
| T-DI-2 | 用户后注册自定义 `IScriptOutput` | 最后注册者胜（官方 DI 语义） |
| T-DI-3 | 只替换 `INuGetRestoreService`、不换 resolver | 构造 runner 时 `VBScriptHostException` |
| T-DI-4 | `AddVBScriptNuGetRestore()` 后替换三者 | 无异常；三层均命中自定义实现 |
| T-DI-5 | `IVBScriptRunner` 生命周期 | 两次 `GetRequiredService` 得到不同实例 |
| T-DI-6 | `UseConsoleOutput()` | `IScriptOutput` 为 `ConsoleScriptOutput` |

### 3.4 NuGet seam（T-NUG，全部假实现）

| ID | 用例 | 断言 |
|---|---|---|
| T-NUG-1 | 假 restore 返回一条 `#R` 锚定诊断 | 编译不继续；结果含该诊断；`ExitCode=1` |
| T-NUG-2 | 假 restore 返回空 | 编译继续；`INuGetPackageResolver` 未被调用（无 nuget 引用时） |
| T-NUG-3 | `#R "nuget:Pkg, 1.0"` + 假 resolver 返回路径 | 编译成功（用假路径指向已加载程序集） |
| T-NUG-4 | restore 上下文字段 | `TargetFramework`/`RID`/`PlatformVersion`/`SdkVersion`/`SourceFingerprint` 均非空（实现期按可得性断言） |
| T-NUG-5 | 默认实现首次触发 | 经 `ILogger` 记录一条 Information（用 `FakeLogger` 断言） |
| T-NUG-6 | 只替换公共 `INuGetRestoreRunner` | 默认 restore 服务的还原执行改走自定义 runner（桥接层生效，不是摆设） |
| T-NUG-7 | `SourceFingerprint` 的唯一来源 | 值取自 `VBScriptOptions.RestoreContext.SourceFingerprint`，且经 `NuGetRestoreRequest` 透传给自定义 runner |

### 3.5 静态门面（T-STA）

| ID | 用例 | 断言 |
|---|---|---|
| T-STA-1 | `VBScript.RunAsync("Return 7")` | `ExitCode=7` |
| T-STA-2 | `VBScript.CompileAsync("Dim x =")` | 诊断非空、`ExitCode=1`，不抛 |

### 3.6 会话（T-SES）

被测对象：`IVBScriptSessionFactory.CreateSession(options)` 返回的 `IVBScriptSession`；回显断言只针对 `VBScriptSubmissionResult.PrintedValue`，不针对 `IScriptOutput`。

| ID | 用例 | 断言 |
|---|---|---|
| T-SES-1 | 提交 `1+1` | `PrintedValue=2`；`IScriptOutput` 未被写（库不回显） |
| T-SES-2 | 两次提交 `Dim x = 1` / `x + 1` | 第二次 `PrintedValue=2`；`Variables` 含 `x` |
| T-SES-3 | `Dim x = 1` / `Dim x = 2` / `x` | `Variables` 按名去重为一条 `x`，值 2 |
| T-SES-4 | 编译错误后提交合法代码 | 会话未死；后续可继续；options 未被错误提交刷新 |
| T-SES-5 | 运行异常（`Throw New InvalidOperationException("boom")`；`1/0` 在本方言是 Infinity 或常量折叠错误，不能用）后提交 | 会话未死；`Exception` 非空；后续可继续 |
| T-SES-6 | `IsCompleteSubmission` | `1+1` → `Complete`；`If True Then` → `Incomplete`；`Dim z =` → `Incomplete`；`Dim x = )` → `Invalid`（判定算法见 G7b 第 4 条） |
| T-SES-7 | `Reset` 后 | `Variables` 为空；初始 imports/refs 恢复；globals 同一实例；旧变量不可见 |
| T-SES-8 | 取消进行中的提交 | 结果 `IsFaulted=True`；后续 `SubmitAsync` 抛自有异常；`Reset` 后可用 |
| T-SES-9 | `DisposeAsync` 两次 | 幂等；`IsFaulted=True`；之后提交抛自有异常 |
| T-SES-10 | 两个 session 并发 / 同一 session 并发 | 前者各自变量值独立、loader 实例不同；后者第二次提交抛 `VBScriptBusyException` |
| T-SES-11 | 会话内 `#Load` 相对路径 | 以 `WorkingDirectory` 为基，不被重置为进程 CWD |
| T-SES-12 | 每提交 `#R "nuget:"` | 假 restore 每提交一次；imports 不重复累积 |
| T-SES-13 | 提交 `Imports System.Text` 后再提交 `Dim sb = New StringBuilder()` | 第二次成功（跨提交 `Imports` replay） |
| T-SES-14 | `Dim x = 1` / `Dim X = 2` / `x` | 大小写不敏感去重为一条，值 2；`VBScriptVariable.IsReadOnly` 正确；globals 成员不出现在 `Variables` |
| T-SES-15 | 宿主 `ApplicationStopping` 触发后提交 | 抛自有异常，不静默继续 |
| T-SES-16 | `DisposeAsync` 时有在飞提交 | 在飞提交被取消，`DisposeAsync` 返回后不再接受提交 |

### 3.7 公共 API 面（T-API）

| ID | 用例 | 断言 |
|---|---|---|
| T-API-1 | 公共 API 快照（反射遍历 facade 导出类型） | 所有公共方法/属性/枚举签名不含 `Microsoft.CodeAnalysis.*`；`IVBScriptRunner`/`VBScript`/`IVBScriptSession` 带 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` |


## 4. L4 对等与回归

| ID | 用例 | 断言 |
|---|---|---|
| T-PAR-1 | 同一 `.vbx`（`Print(...)` + `#Load` + `Return 3`）经 runner 与 `vbi` | 输出文本一致、退出码一致；**单测层已自动化**（`HostingParityTests`：进程内 `CommandLineRunner` vs `IVBScriptRunner`，逐字比 stdout + 退出码，六格），发布前另有 §6 人工 `vbi` 进程对照 |
| T-PAR-2 | `Samples\FileArgs.vbx` | `Args` 传递与 `vbi` 一致；**只能人工执行**（§6）：该样例读进程级 `Environment.GetCommandLineArgs` 且 `Console.ReadKey` 阻塞，进程内不可能与 `vbi` 进程等价；单测层用等价临时脚本断言「脚本可见的 `Args` 通道」（`HostingParityTests`） |
| T-PAR-3 | `vbi` 现有 Scripting 测试 | `Scripting\VisualBasicTest -automated` 全绿（F5 等价门） |
| T-PAR-4 | `#R "nuget:"` 语义（真实包） | 不进单测；F11 冒烟执行，记录输出 |

## 5. L0 打包层

| ID | 用例 | 断言 |
|---|---|---|
| T-PKG-1 | `dotnet pack`（net10） | nupkg 含 `lib/net10.0/` 五个程序集 + 两侧 satellite；无 `Microsoft.CodeAnalysis.CSharp.dll` |
| T-PKG-2 | `dotnet pack`（net472） | `lib/net472/` 含 8 个 polyfill + facade + 四个程序集；nuspec 另列 `Microsoft.Bcl.AsyncInterfaces` 依赖 |
| T-PKG-3 | nuspec 依赖 | net10 组 6 个 `Microsoft.Extensions.*`；net472 组另列 `Microsoft.Bcl.AsyncInterfaces`（共 7 个）；无 `Microsoft.CodeAnalysis*` 依赖 |
| T-PKG-4 | `DevelopmentDependency` | nuspec 无 `<developmentDependency>true</developmentDependency>`；依赖项无强制 `PrivateAssets=all` 属性 |
| T-PKG-5 | 引用官方 Roslyn 包的测试工程（直接 + 传递各一例） | 两者均构建失败；错误消息含包名与关闭属性 `VBScriptAllowRoslynPackages` |
| T-PKG-6 | 设 `VBScriptAllowRoslynPackages=true` | 构建通过（检测被显式关闭） |
| T-PKG-7 | README 入包 | `PackageReadmeFile` 存在且首段含「运行时库，非构建工具」 |

## 6. 发布前硬门（人工）

1. **net472 冒烟**：仓库外临时 net472 控制台工程引用 nupkg → `Console.WriteLine`、`#Load`、托管包的 `#R "nuget:…"`（如 `Newtonsoft.Json`）成功；native 包（如 `Microsoft.Data.Sqlite`）按宿主能力给出 `VBI1010` 诊断、`ExitCode=1`，属**预期拒绝**，不计失败；记录到 `manual-verification-checklist.md`。
2. **net10 冒烟**：同一工程 net10.0 → 同上。
3. **默认集核对**：把 runner 的默认 `References/Imports` 打印出来与 `vbi.coreclr.rsp` 人工对照（单测已覆盖，人工二次确认）。
4. **身份检测实测**：测试工程加 `Microsoft.CodeAnalysis.CSharp` 包引用（直接），再建一例只引一个传递依赖它的包 → 两者都构建失败；加关闭属性 → 通过。
5. **T-PAR-1/2 对等**：同一 `.vbx`（`Print(...)` + `#Load` + `Return 3`）与 `Samples\FileArgs.vbx`，分别经 `vbi` 与宿主 runner（显式 `UseConsoleOutput()`，必要时 `Console.SetOut` 捕获）执行，比较输出文本与退出码。

## 7. 闸门与命令

- G1（单测）：`Scripting\VisualBasicTest` 既有套件 + 新增 facade 用例，全绿。
- G2（`vbi` 回归）：`Scripting\VisualBasicTest -automated` 全绿；抽样 `.vbx` 输出对照。
- G3（打包）：T-PKG 全绿；nupkg 人工解包核对目录树。
- G4（七门）：`scripts\verify-vb-compiler-tests.ps1`；判据为七门 Failed 全 0。2026-11-18 实测七门全绿：Phase2 143/143/0/0、Syntax 4106/4103/3/0、Symbol 3407/3383/24/0、Semantic 5919/5815/104/0、IOperation 1574/1566/8/0、Emit 4389/4286/103/0、CommandLine 483/476/7/0。Semantic/Emit 两门的并行池 flake 属上游来源，已用串行集合隔离（issue 35 §七）。
- G5（冒烟）：第 6 节五条完成并归档。

## 8. 测试基础设施改动

- 在 `Scripting\VisualBasicTest\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.vbproj` 增加对 facade 工程的 `ProjectReference`。
- 新增测试文件：`HostingDefaultsTests.vb`、`HostingRegistrationTests.vb`、`HostingRunnerTests.vb`、`HostingNuGetTests.vb`、`HostingSessionTests.vb`、`HostingStaticFacadeTests.vb`、`HostingApiSurfaceTests.vb`、`HostingParityTests.vb`；共用替身 `HostingTestDoubles.vb`。nupkg 布局核对放在人工清单（不在单测里跑 `pack`）。
- 假实现：`FakeScriptOutput`、`FakeNuGetRestoreService`、`FakeNuGetPackageResolver`、`FakeRestoreRunner`、`FakeLogger(Of T)`；复用现有 `TestConsoleIO` 与内存 source resolver。
- 测试写法沿用现有 `Scripting\VisualBasicTest`：xunit.v3 的 `<Fact>` / `Imports Xunit`（`...UnitTests.vbproj:23`、`InteractiveSessionTests.vb:10-15`），不新增测试框架依赖。

## 9. 共鸣 facet（测试习惯）

> 样本取自本仓库现有测试与兄弟任务（2026-11-16 已检查）。

| 样本 | facet | 强度 | 证据锚点 | 本计划应用 |
|---|---|---|---|---|
| `Scripting\VisualBasicTest` | xunit.v3 `<Fact>`；exe harness；`-automated` 直跑程序集 | 必选 | `...UnitTests.vbproj:23`、`InteractiveSessionTests.vb:10-15` | §8 写法与 §7 G2 |
| `Scripting\VisualBasicTest` | 假 seam 注入避免进程（测试注入 fake 返回固定结果） | 必选 | `NuGetRestoreRunner.vb:186-189` 的注释 | T-NUG 全部假实现 |
| `Scripting\VisualBasicTest\Helpers` | 内存 `TestConsoleIO`、内存 source resolver | 优先 | 既有 helper | §1 无副作用纪律、§3.2 |
| `tasks\distribute-...manual-verification-checklist.md` | 有副作用的验证独立成文，逐项勾选 | 优先 | 该文件 | 本目录 `manual-verification-checklist.md` |
| `InteractiveSessionTests.vb` | 跨提交字段、`Imports` 累积、提交链的既有用例模板 | 必选 | `Scripting\VisualBasicTest\InteractiveSessionTests.vb:14-33` | T-SES-2/3/7 |
| 既有测试断言风格 | 断言具体值/具体诊断码，不用「不抛异常」代替行为 | 必选 | `InteractiveSessionTests.vb:21` 等 | §3 各用例断言列 |

**必要偏离**：真实 `#R "nuget:"` 不进单测——理由：无副作用纪律禁止联网/进程；验证：F11 手工冒烟 + T-NUG 假实现覆盖接线。

## 10. 生长验收与剪枝

- **测试 → 验收目标映射**：T-DEF-1→R4 默认集；T-RUN-2→R6 结果语义；T-DI-3→R10 成组替换校验；T-PKG-5/6→R11 身份检测；T-PKG-1/2→R12/R13 打包；T-PAR-3→R15 单管线等价；T-RUN-8→R3 重入守卫。 会话组 T-SES-1/2/6/7→R17；T-SES-8/9→R7/R17；T-SES-10→R3。

- **剪掉的测试**：真实 NuGet 还原（触发：F11 或 CI 允许进程）；`#R "project:"`（触发：该能力发布）；嵌入 REPL（触发：v2 范围）。 `RunInteractiveAsync` 便利层（v1 不做，触发：终端式嵌入需求）。

- **鉴别力说明**：T-RUN-8 用并发抢占断言（不是「跑两次」）；T-DEF-1 逐项比对集合（不是「包含 System」）；T-PKG-5 断言构建失败且错误消息含关闭属性（不是「能构建」）；T-RUN-3 断言诊断 id 以 `BC` 开头（不是「有诊断」）。 T-SES-1 同时断言「`PrintedValue` 有值」与「`IScriptOutput` 未写」（不是只看回显）；T-SES-7 断言旧变量不可见（不是只看新变量存在）；T-SES-3 断言去重后的条数。

- **停止条件**：无法无副作用覆盖的项转 `manual-verification-checklist.md` 并注明；不静默跳过；单测出现需联网/进程才能过 → 停，改设计。
