# 任务：可嵌入脚本宿主包（embeddable-scripting-host）

本文件夹是 `proposal-embeddable-scripting-host`（RESOLUTION **Active**）的设计任务存储：Vortex 代办列表 + 概要/详细设计 + 测试计划。任务目标是把 `vbi` 现有的脚本执行管线（`.vbx`、`/check`、`#Load` 多树、`Print`/`Args`、`Return` 退出码、`#R "nuget:"`）接到**官方 .NET Hosting** 上，交付一个可被应用引用的运行时包 `Nukepayload2.Compilers.VBScriptDotNet.Scripting`。

- **依据链**：`../../proposals/proposal-embeddable-scripting-host.md`（Proposed）→ `../../meetings/meeting-embeddable-scripting-host.md`（RESOLUTION R1–R17，**plan 阶段的唯一权威**；提案正文细节以 RESOLUTION 为准）→ `../../decisions.md`（M5 / D4 / D5 / D7 / D9）→ `../../spec/spec-scripting-dialect.md`、`spec-reference-directive.md`、`spec-load-directive.md`。独立五维评审是不入库的工作材料：`tmp\meetings\embeddable-scripting-host\evaluation.md`。
- **交付物**：运行时包 `Nukepayload2.Compilers.VBScriptDotNet.Scripting` 2.0.0-RC3（`net10.0` + `net472`；Release 产物 10,155,746 B / 128 条目，见 F9 与 `manual-verification-checklist.md`）及其打包工程 `Installer\Scripting\`、宿主 facade 工程 `Scripting\Hosting\`；文档 `README.md`（本文件）、`design-overview.md`、`design-detailed.md`、`test-plan.md`、`manual-verification-checklist.md`（net472 与打包的集成验证）。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替。
- **流水账**：`tmp\vortex-logs\embeddable-scripting-host\`。

## 优先级（D4）

**P1（立即实现）**。理由：三态 Active；属分发/宿主契约类（与 `distribute-compiler-nuget-package-and-dotnet-tool` 同层），不是语言面新特性；缺口真实（公共出口止于 `VisualBasicScript.RunAsync`，包不可发布）；机制已存在（两条 internal seam + 一条执行管线），属延伸而非发明。

## 边界（本任务不做）

- 不做语言/编译器语义改动（`Compilers\VisualBasic\Portable\` 零改动）。
- 不做 REPL 的 UI/命令层（提示符、`#help`、`/loadpath`）；但 **v1 提供会话原语** `IVBScriptSession`（供 immediate window），不做 `RunInteractiveAsync` 便利循环。
- 不做 `#R "project:"`（未发布，v1 不含）。
- 不做 C# scripting / csi。
- 不承诺线程安全；并发由 runner 的重入守卫显式拒绝。
- 不改 `vbi` 的 CLI 行为与输出。
- **不得修改公共 scripting 程序集（`Scripting\Core`）**：VB 功能只在 VB 编译器与 VB scripting 内实现（`Compilers\VisualBasic\*`、`Scripting\VisualBasic\*`、`Interactive\vbi\*` 以及本任务的 facade/packaging/demo 侧）。理由＝同一进程的用户可能并用其它 Roslyn API，改公共程序集不可接受。脚本程序集回收与否取决于宿主选择的 `AssemblyLoadContext`，本任务不实现、不承诺动态卸载。

## Vortex 代办列表

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计（`design-overview.md`） | 覆盖架构分层、公共 API 面、DI 拓扑、默认语义、并发/生命周期、包布局；逐条映射 RESOLUTION R1–R17；`vbi` 等价断言必须带 net10/net472 限定与锚点 | **done**（2026-11-16 初稿；F4 复验 PASS） |
| F2 | 详细设计（`design-detailed.md`） | 每个改动文件给出路径 + 改动形状 + 源码锚点（`文件:行号`）；可被实施者照做；含 facade 工程与打包工程 csproj 形状 | **done**（2026-11-16 初稿；F4 复验 PASS） |
| F3 | 测试计划（`test-plan.md`） | 四层矩阵（L1/L2 说明为何 N/A，L3 API，L4 CLI 对等）+ 无副作用纪律 + 发布前硬门（net472 冒烟、默认集比对） | **done**（2026-11-16 初稿；F4 复验 PASS） |
| F4 | 一致性审计 + 修复 + 复验 | F1–F3 与 meeting RESOLUTION 交叉一致；所有源码锚点逐条复核；不出现「与 vbi 完全同语义」这类未加限定断言 | **done**（2026-11-17 三轮独立审计 PASS，报告 `tmp\meetings\embeddable-scripting-host\f4-audit-3.md`） |
| F5 | 抽 `ScriptRunnerCore`（无行为变化） | `CommandLineRunner` 的脚本执行路径改为调用 core（core 同时提供一次性与会话驱动）；`vbi` 的 `.vbx`/`/check` 输出、退出码、诊断与改动前逐字节一致（现有 Scripting 测试全绿） | **done**（2026-11-17 实现+返工+独立验证 PASS：`ScriptSessionCore(Of TReturn)` 为唯一编排，`RunAsync` = 建会话→一次提交→映射；四例 stdout/退出码逐字节一致、`-automated` 814/0；已知残差＝运行期异常栈少 2 条宿主 async 帧；日志 `tmp\vortex-logs\embeddable-scripting-host\3-verifier-f5.md`） |
| F6 | facade 工程 + 选项 + 默认集 + 静态 `VBScript` | 新工程可编译（net10/net472）；`VBScriptOptions` 默认 references/imports 与 `vbi.coreclr.rsp` 逐项相等（单测）；`VBScript.RunAsync/EvaluateAsync/CompileAsync` 返回自有 `VBScriptResult` | **done**（2026-11-17 实现+返工+复验 PASS：双 TFM 0 警告 0 错误、默认集 34/11 逐项相等、`-automated` 826/0；`EvaluateAsync` 返工为 object 会话 + 转换，编译错误走新增 `VBScriptScriptException`；日志 `7-verifier-f6-recheck.md`） |
| F7 | Hosting 注册 + runner + 结果/异常 + 输出 | `AddVBScripting` 两个重载；Abstractions-only 依赖；runner transient + 重入守卫；`VBScriptResult` 含 ExitCode/ReturnValue/Diagnostics/Output/Exception；默认输出不写 Console；取消令牌链接 `ApplicationStopping` | **done**（2026-11-17 实现+返工+复验 PASS：6 个 Abstractions 包、848→850/0；返工修 `ReturnValue` 无 `Return` 时为 `Nothing`；异常层次定稿 `VBScriptException` 单根；日志 `11-verifier-f7-recheck.md`） |
| F8 | NuGet 三层 seam + 缓存服务 + 成组注册 + 上下文 | 公共 `INuGetRestoreService` / `INuGetRestoreRunner` / `INuGetPackageResolver`；`NuGetPackageSession` 入 DI；`AddVBScriptNuGetRestore()`；只换一环时启动校验报错；restore 上下文暴露；默认实现首次触发经 `ILogger` 留痕 | **done**（2026-11-17 实现+返工+文档复验 PASS：三层串联经 `NuGetRestoreRunnerBridge`，只换公共 runner 实测生效；`RestoreContext.SourceFingerprint` 唯一来源；`-automated` 861/0；日志 `15-verifier-f8-docs.md`） |
| F9 | 打包工程 + 身份检测 + 包 README | `dotnet pack` 产出 nupkg，`lib/net10.0` 与 `lib/net472` 内容符合设计（含 8 个 polyfill）；`buildTransitive` 冲突检测在引用官方 Roslyn 包的测试工程上报错；包 README 首段写明「运行时库，非构建工具」 | **done**（2026-11-18 独立验证 PASS：Debug 10,983,078 B / Release 修复前 10,153,917 B → 修复后 10,155,746 B，128 条目；四例身份检测真实构建 1/1/0/0；nuspec 6/7 组无 Roslyn；`-automated` 862/0；日志 `17-verifier-f9.md`） |
| F10 | L3 单测 + vbi 对等 harness | 新增测试全部无副作用（无网络/无进程/无写盘）；覆盖注册覆盖语义、默认集、结果映射、异常模型、输出路由、restore 组校验、重入守卫；同一 `.vbx` 在 runner 与 `vbi` 下输出/退出码一致（进程内 runner + 现有 harness） | **done**（2026-11-18 独立验证 PASS：`HostingParityTests` 六格逐字对等（进程内 `CommandLineRunner` vs `IVBScriptRunner`）、`HostingApiSurfaceTests` 26 个导出类型反射零 Roslyn 且能拒绝 `ScriptOptions` 变体，`-automated` 872/0；日志 `19-verifier-f10.md`） |
| F11 | net472 打包冒烟（发布硬门） | 打包后 net472 冒烟工程：引用包、`Console.WriteLine`、`#Load`、`#R "nuget:"` 全部可用；记录到 `manual-verification-checklist.md` | **done**（2026-11-18 档 2 独立验证 PASS：net472 与 net10 各自包解析/还原/执行全绿，托管包可用、native 包给 `VBI1010`；修复了 netfx 宿主 TFM 探测缺陷；日志 `22-verifier-f11.md`） |
| F12 | 全量回归 + 文档/索引 | `scripts\verify-vb-compiler-tests.ps1` 七门全绿；`Scripting\VisualBasicTest -automated` 全绿；包 README/索引更新 | **done（2026-11-18，七门全绿，未 commit）**：`Scripting\VisualBasicTest -automated` 892/0/0/0；七门＝Phase2 143/143、Syntax 4106/4103、Symbol 3407/3383、Semantic 5919/5815、IOperation 1574/1566、Emit 4389/4286、CommandLine 483/476（Skipped 依门基线，Failed 全 0）。两类残差已修复（SDK 折叠漂移改语义断言、Syntax 4 条跟上 HEAD 的 parser），证据与处置见下方「F12 回归残差」。锚点清扫、残余风险、交付物段与索引收尾完成 |
| F13 | 会话 API（factory / session / 结果 / 变量 / 三态） | 按 G7b：`IVBScriptSessionFactory.CreateSession`、`IVBScriptSession` 六成员、`VBScriptSubmissionResult`/`VBScriptVariable`/`VBScriptSubmissionState`；无状态入口复用一次提交；契约面零 Microsoft 类型（PublicAPI 快照断言：签名与枚举不含 `Microsoft.CodeAnalysis.*`） | **done**（2026-11-18 档 1 独立验证 PASS：65 项探针 0 失败、会话 16 条用例全绿、契约面零 Roslyn；日志 `24-verifier-f13-f14.md`） |
| F14 | 会话语义与测试 | R17 八条逐条有测试（T-SES-1..16）；取消即 faulted、`Reset` 恢复初始 options、globals 保留、imports 不重复、基目录不被重置 | **done**（2026-11-18 同轮交付：T-SES-1..16 逐条实测，faulted tie-break＝取消 faulted／运行异常继续；`Variables` 的 trim/AOT 标注落到实现 getter——接口属性在 CLR 上不可标注、VB 接口无访问器声明，见 `design-detailed.md` G8） |
| F15 | 撤销 Core 改动，引擎迁回 VB scripting | `git diff HEAD -- Scripting/Core` 为空（`ScriptRunnerCore.cs` 删除、公共层无新增 seam/IVT）；`ScriptRunnerCore`/`ScriptSessionCore(Of TReturn)`/`ScriptCommandLineHost` 在 `Scripting\VisualBasic\Hosting\`；vbi 文件脚本/`/check` 走 VB 引擎、REPL 仍调 vendored `CommandLineRunner`；facade 经 VB scripting IVT；回收测试删除 | **done（2026-11-18，未 commit）**：四例 stdout/退出码逐字节一致（stderr 仅缺 2 条已裁定的宿主 async 帧）；`Scripting\VisualBasicTest -automated` 889/0（892 − 3 条回收测试）；七门全绿；日志 `tmp\vortex-logs\embeddable-scripting-host\34-implementer-core-revert-vb-engine.md` |

## 共享源码事实（所有 Vortex agent 以此为基准）

> 2026-11-18 工作树读数（F5–F15 落地后复核；行号以工作树为准），逐条已检查。路径相对仓库根。F15 已撤销 F5 的 Core 落点：`Scripting\Core` 恢复 HEAD，执行引擎与 vbi 适配器在 VB scripting。

### 执行管线（要被抽出的部分）

- **模式分派（vbi 入口）**：`Scripting\VisualBasic\VisualBasicScript.vb` 的 `RunInteractiveAsync` 构造 `VisualBasicInteractiveCompiler` 后按 `Arguments.Check OrElse Not Arguments.InteractiveMode` 分流：文件脚本/`/check` → `ScriptCommandLineHost` + `ScriptRunnerCore`（`Scripting\VisualBasic\Hosting\`）；交互 → `New CommandLineRunner(...).RunInteractiveAsync()`。`CommandLineRunner.cs` 保持 HEAD（`RunInteractiveCoreAsync`、`GetScriptOptions`、REPL 预扫描）：它现在只承担交互 REPL。
- **ScriptOptions 构造**：`CommandLineRunner.cs` 的 `GetScriptOptions`（`:183`；filePath / references / imports / resolver / debug / optimization / parseOptions）；metadata resolver 工厂 `GetMetadataReferenceResolver`（`:212`，`RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`）；source resolver `GetSourceReferenceResolver`（`:225`）。行号取 2026-11-19 工作树读数，后续以符号名为准。
- **脚本执行适配器（vbi 文件脚本/check）**：`Scripting\VisualBasic\Hosting\CommandLine\ScriptCommandLineHost.vb`——显示开关/读源/`ScriptOptions`/参数诊断，然后 `CommandLineScriptGlobals` → 构造 `ScriptRunRequest` → `ScriptRunnerCore.RunAsync` → 诊断打印与 `/check` 失败映射 → 异常显示 → 返回 `ExitCode`。`Script.CreateInitialScript`、`RunAsync` 取 `ReturnValue`、`CompilationErrorException`/其它异常的映射在 `Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb`（按符号名；行号以工作树为准）。
- **提交创建与 `#Load` 展开**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `CreateSubmission`（`Public Overrides`，`:222`）与 `CollectLoadTrees`（`Friend Shared`，`:117`）。
- **NuGet 预扫描**：文件脚本路径在 `Scripting\VisualBasic\Hosting\ScriptRunnerCore.vb` 的 `ScriptSessionCore(Of TReturn)` 提交路径（调 `IScriptRestoreCoordinator.PrepareCompilationAsync`；VB 侧接口，`NuGetRestoreCoordinator` 同时实现共享 `INuGetRestoreCoordinator`；行号以工作树为准）；REPL 的预扫描留在 `CommandLineRunner.cs:230`（`RestoreNuGetReferencesAsync`）。协调器实现 `Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb:34`（`Friend`；`PrepareCompilationAsync` 重载在 `:134`/`:154`），`#Load` 复用 `CollectLoadTrees`（`:240`）。
- **会话机制（已检查）**：`ScriptState.Variables`/`GetVariable`（`Scripting\Core\ScriptState.cs:61-72`、`:83-91`）、`ScriptVariable` 四成员（`ScriptVariable.cs:34-71`）、`HasReturnValue`（`Script.cs:307-310`，`internal` 且带 `// TODO: remove`）、REPL 的 `BuildAndRunAsync`（`CommandLineRunner.cs:368-392`）与 `UpdateOptions`（`:394-409`）、`RemoveImportsAndReferences`（`:405`）、`IsCompleteSubmission`（`VisualBasicScriptCompiler.vb:64-71`）、跨提交 imports replay（`AddPreviousSubmissionImports`，`Friend`，由 VB 引擎直接调用）。
- **解析前端**：`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:12-51`（`internal abstract`；`nuget:` 前缀 `OrdinalIgnoreCase`、逗号分隔）。
- **可注入还原执行器**：`Scripting\VisualBasic\Hosting\NuGetRestoreRunner.vb:233-246`（`Friend Interface IRestoreRunner`）、`:287`（`DotNetRestoreRunner`）。

### 公开面现状

- `Scripting\VisualBasic\PublicAPI.Shipped.txt:6-11`：只有 `VisualBasicScript.Create/RunAsync/EvaluateAsync`，返回类型是 `Script(Of T)` / `ScriptState(Of T)`。
- `Scripting\Core\Hosting\CommandLine\CommandLineScriptGlobals.cs:22-61`：`public class`，`Args` / `Print` / `PrintOptions`。
- `Scripting\VisualBasic\VisualBasicScript.vb:150`：`RunInteractiveAsync` 公开，但绑 `ConsoleIO.Default`（`:157`）与 rsp 文件名参数（`:150`）。
- `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:13-14`：`IsPackable` 但 `PackageId=Microsoft.CodeAnalysis.Scripting.Common`（上游名，不可发布）。
- `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:1-12`：无 `IsPackable`（grep 缺失断言）；`:28-29` 两个 `ProjectReference`；`:8-9` 版本 `2.0.0`+`RC3`。

### 默认语义来源

- `Interactive\vbi\vbi.coreclr.rsp:1-50`：`/nostdlib`、`/vbruntime-`、34 条 `/r:`、11 条 `/imports:`（`System`、`System.IO`、`System.Collections.Generic`、`System.Console`、`System.Diagnostics`、`System.Dynamic`、`System.Linq`、`System.Text`、`System.Threading.Tasks` 等）。
- `Interactive\vbi\vbi.desktop.rsp`：含 `/r:System.Drawing`、`/r:System.Windows.Forms`（net472 桌面引用面，v1 默认不带）。
- `Interactive\vbi\vbi.vbproj:7`：`net10.0-windows;net10.0;net48`（net48 是 netfx 先例，不是 net472 证据）。
- `Interactive\vbi\bin\Debug\net48` 的 netfx 闭包八个 polyfill：`System.Buffers`、`System.Collections.Immutable`、`System.Memory`、`System.Numerics.Vectors`、`System.Reflection.Metadata`、`System.Runtime.CompilerServices.Unsafe`、`System.Text.Encoding.CodePages`、`System.Threading.Tasks.Extensions`。

### 构建/打包先例

- Toolset 打包工程：`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.vbproj:5-14`（`IsPackable`/`PackageId`/`IncludeBuildOutput=false`/`DevelopmentDependency=true`/`_GetFiles` 加 `TargetsForTfmSpecificContentInPackage`）、`:39-68`（`_GetFiles` 铺 `tasks/`）。
- Toolset build props：`Installer\Toolset\build\Nukepayload2.Compilers.VBScriptDotNet.props:1-13`（只注册 `Vbc`；本任务不需要编译器重定向 props）。
- 签名设定：`Directory.Build.props:24-26`（`Microsoft.CodeAnalysis*` 与 `Roslyn.Test.PdbUtilities` 用共享密钥 public-sign）。

## 关键设计决策（RESOLUTION 摘要，设计文档必须吸收）

1. **依赖只用 Abstractions**：`Hosting.Abstractions` / `DependencyInjection.Abstractions` / `Options` / `Options.ConfigurationExtensions` / `Logging.Abstractions` / `Configuration.Abstractions`（10.0.0）；不引全量 `Microsoft.Extensions.Hosting`。
2. **契约面零 Microsoft 类型**：自有 `VBScriptSource` / `VBScriptDiagnostic` / `VBScriptResult` / 异常类型；Roslyn 类型只留实现层。
3. **脚本层 v1 是两个入口**：无状态（`RunAsync` / `EvaluateAsync` / `CompileAsync`）与有状态（`IVBScriptSessionFactory.CreateSession` → `IVBScriptSession`）；无状态内部复用「一次提交的会话」；`RunInteractiveAsync` 不进 v1。
4. **默认集 = `vbi.coreclr.rsp`**（net10 逐项相等）；net472 桌面引用 opt-in。
5. **runner transient + 重入守卫**；restore 会话缓存单例加锁；每 runner 独立 assembly loader；取消链接 `ApplicationStopping`。
6. **NuGet 三层 seam + 成组注册 + 启动校验 + restore 上下文**。
7. **输出默认不写 Console**（默认 Null / `ILogger` 适配；`UseConsoleOutput()` 显式）。
8. **身份冲突**：README + `PackageDescription` 写 process-wide exclusive；`buildTransitive` 检测 `Microsoft.CodeAnalysis*` 并 error（留关闭属性）。
9. **TFM**：`net10.0` + `net472`；net472 铺 8 个 polyfill；net472 冒烟为发布硬门；`netstandard2.0` 推迟。
10. **打包**：单门面程序集承载公共契约；不继承 `DevelopmentDependency`；包 ID `Nukepayload2.Compilers.VBScriptDotNet.Scripting`（误标风险记 OPEN QUESTION）。
11. **trim/AOT**：入口标 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`；包 `IsTrimmable=false`。
12. **单管线成本**：core 抽离的落点是「以 `VBScriptOptions` 为一等输入重写取 options 路径」，不是去掉 Console 适配器。

## 共鸣 facet 记录（计划期实验）

按 SOP，facet 与剪枝已**内联**到 `design-detailed.md`（「共鸣 facet（设计贴合）」/「生长剪枝与验收」）与 `test-plan.md`（「共鸣 facet（测试习惯）」/「生长验收与剪枝」）。本节只留实验结论：

- 一次约 15 分钟的近邻比对抓出 3 处真实偏离（版本线、公共 API 台账、测试写法/许可头），均为「新代码不像本仓库」类缺陷，已在两份文档内联修复。
- 反面样本：Toolset 的 `DevelopmentDependency=true` 不能照抄（与 RESOLUTION R13 冲突）；兄弟任务里指向不存在的 `meetings/evaluation-*.md` 的链接是坏味道。
- 适用边界：Resonance 改进「贴合度」，不改进决策（该不该做、优先级、契约边界归 LDM 与证据纪律）。

## 风险与缓解

| 风险 | 缓解 |
|---|---|
| 抽 core 改变 `vbi` 行为 | F5 以现有 Scripting 测试 + 字节级输出对照为门；先等价重构再扩接口 |
| facade 需要访问 `Friend` 的宿主管线 | facade 用共享密钥签名并加 `InternalsVisibleTo`（与 `vbi` 同法）；仅访问 `Friend` 成员，不改可见性 |
| net472 闭包遗漏 | 显式列 8 个 polyfill；F11 冒烟硬门 |
| 默认集与 rsp 漂移 | 把 rsp 作为嵌入资源随包分发，单测逐项比对仓库文件 |
| 身份冲突只能靠文档 | `buildTransitive` 构建期 error + README + PackageDescription |
| 包 ID 误标（工具链家族） | README 首段声明运行时定位；OPEN QUESTION 写死重开条件 |
| 会话长驻内存 / 取消残态 | R17 的 faulted 契约 + `Reset` 换 loader（只清状态与 loader 注册）；T-SES-8/10 覆盖。程序集能否卸载由宿主 `AssemblyLoadContext` 决定，库不承诺回收 |
| 并发默认被误用 | transient + 重入守卫抛自有异常；文档给「多 runner + 宿主排队」 |
| 门测试的 SDK 版本未钉死（仓库无 `global.json`） | 2026-11-18 已消除影响面：`FormatPrimitive` 的 Decimal 断言不再钉死折叠后的文本，改断言语义（InvariantCulture 解析回 Decimal 的 round-trip），旧/新 SDK 都成立；仓库仍不设 `global.json`，门基线按实跑刷新 |

## F12 回归残差（2026-11-18 已在同日修复）

**全量（F12 时点，历史）**：`dotnet Scripting\VisualBasicTest\bin\Debug\net10.0\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated` → `TestsTotal=892, TestsFailed=0, TestsNotRun=0, TestsSkipped=0`，退出码 0。该 892 含后来被 F15 删除的 `HostingLoaderRecyclingTests` 3 条（T-LOAD-1..3）；F15 口径为 889。`T-SES-11` 的 `Reset` 后基目录/再次 `#Load` 断言仍在。

**七门实测**（`tmp\vortex-logs\embeddable-scripting-host\27-gates.log`，脚本 `scripts\verify-vb-compiler-tests.ps1` 全跑一遍，退出码 0）：

| 门 | Total/Passed/Skipped/Failed | 退出码 |
|---|---|---|
| Phase2 | 143/143/0/0 | 0 |
| Syntax | 4106/4103/3/0 | 0 |
| Symbol | 3407/3383/24/0 | 0 |
| Semantic | 5919/5815/104/0 | 0 |
| IOperation | 1574/1566/8/0 | 0 |
| Emit | 4389/4286/103/0 | 0 |
| CommandLine | 483/476/7/0 | 0 |

结论：**七门全绿**。门脚本的基线数字无需改动（Phase2/Symbol/Syntax 已回到 2026-10-03 基线 143/143、3407/3383/24/0、4106/4103/3/0），注释里记录了复核日期、两处测试修正，以及 issue 35 的串行隔离；「重跑即绿」不构成处置（issue 35 §七）。

**已在 2026-11-18 修复一：SDK 常量折叠漂移（Phase2 / Symbol）**

- **原因**：`SymbolDisplayTests.FormatPrimitive` 用测试源码里的字面量 `CDec(1.1)` 作为输入，它由 SDK 的 vbc 在编译测试程序集时做常量折叠。SDK 11.0.100 折叠成精确十进制 `1.1000000000000000888178419700`，旧 SDK（10.0.100-rc.1）折叠成 `1.1`；断言钉死文本 `"1.1"`，于是随 SDK 变红。产品侧 `CDec` 对变量/运行期求值不受影响（仓库内树编译器经 vbi `Print(CDec(1.1))` 仍输出 `1.1`）。
- **处置**：改断言而不改产品（同一改动同时落在两份副本 `Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb:4905` 与 `Compilers\VisualBasicTest\SymbolDisplayTests.vb:4915`）：
  1. 保留一条精确文本断言，输入换成 Decimal 字面量 `1.1D`——字面量直接按十进制解析，不经过 Double→Decimal 折叠，文本稳定；
  2. `CDec(1.1)` 两条改成语义断言：输出必须非空、能以 `InvariantCulture` 解析成 `Decimal`、且 round-trip 等于同一表达式在当前 SDK 下折叠出的 `Decimal`（`AssertDecimalRoundTrips`，两份副本各一份）；
  3. 注释写明「折叠/格式化行为可能随 SDK 变化，断言语义而不是文本」。
- **实测前后**（同一台机器、同一 SDK）：修前 Phase2 `143/142/0/1`、Symbol `3407/3382/24/1`，失败 `Expected "1.1" Actual "1.1000000000000000888178419700"`；修后 `143/143/0/0`、`3407/3383/24/0`。

**已在 2026-11-18 修复二：Syntax 4 条期望未跟上 HEAD 的 parser**

两次 parser 提交都是有意为之（`InternalDevDocs\tasks\keyword-toplevel-audit\README.md` §4 D1/D2/D3、§7 有判定与修复记录），旧期望才是陈旧的一方，因此更新期望、不改产品：

| 测试 | 旧期望 | 新期望（实测） | 依据 |
|---|---|---|---|
| `ParseErrorTests.CustomKeywordAlone_IsReadAsIdentifierStatement`（原名 `BC30195ERR_ExpectedSpecifier`） | `BC30195` | `BC30188`（`Custom` 处） | `0c13fb9` 给 `Custom` 补了与方法体同形的 `PeekToken(1) = Event` 前瞻（`Parser.vb:832`）；单独一个 `Custom` 保持标识符身份，模块体内裸标识符语句报 BC30188。同文件 `BC30188ERR_ExpectedDeclaration_1`（`Unicode Sub sub1()`）是同组码的既有证据 |
| `ParseDeclarations.BC31140ERR_InvalidUseOfCustomModifier_AfterTheCustomGuard` | `30195+31140+30429` | `30188`（`Custom`）+ `32009`（`sub goo()` 非行首） | 同上；新增第二格 `Private Custom Sub goo()` 仍产出 `31140`，保住该码的覆盖 |
| `ParseIteratorTests.ParseYieldInScriptingAndInteractive` | 6 | 7（1 条脚本顶层 `Yield (T)` + 6 条迭代器内） | `0c13fb9` 让脚本顶层 `Yield` 进入 yield 分支（`Parser.vb:1215-1221`），由 binder 报 `BC36966`；断言改为「顶层恰好 1 条且是 `Yield (T)`，其余全在迭代器内」 |
| `ScriptTopLevelAfterAutoPropertyTests.G8_RaiseEventAsSuccessor_...` | 两层各 `BC30188`+`BC30205` | 两层都零诊断，末成员是 `RaiseEventStatement` | `5f67241` 给 `RaiseEvent` 补上 `AddHandler`/`RemoveHandler` 同形的 `IsTopLevelScript` 守卫（audit D3「已修复」） |

**已用串行集合隔离（原因见 issue 35 §七）**：同族的并行池 flake 在 `Emit` 与 `Semantic` 两门都偶发过。`Emit` 的 `SubmissionSharedHandlesHookupTests.SharedHookupAcrossSubmissions_TwoRaisesDeliverTwiceAndTheHookupRunsOnce` 在 2026-11-18 首次全门复跑失败一次；`Semantic` 的 `ScriptTopLevelDefiniteAssignmentTests.CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning` 在验证者首跑失败一次。两者都是 `Binder_Conversions.vb:442` 的 `Debug.Fail(argument.Type.IsSameTypeIgnoringAll(targetType))` + `ValidatePooledObjects` 报池泄漏，单测隔离（18/18、5/5）与整门复跑均通过。归因：HEAD 就存在的并行执行 flake（`Compilers` 产品代码未改动），根因是上游 `ReferenceManager` 缓存「查／建／发布」三段不原子。2026-11-18 作者批准后，两个受害类分别进入各自工程的 `DisableParallelization` 串行集合，确定性配方 8 轮 0 红、两门各 5 轮基线不变。trx 分别留档于 `tmp\vortex-logs\embeddable-scripting-host\27-gates-emit-flaky.trx`、`28-gates-semantic-flaky.trx`。

**残余风险收口**：三条候选方案全部按「更新断言」落地，不引入 `global.json`、不改产品；Syntax 的 4 条期望值是同一处置。`Variables` 的 trim 标注落在实现 getter；net10 loader 回收一条已撤销（脚本程序集能否卸载由宿主 `AssemblyLoadContext` 决定），见 `design-detailed.md`「残余风险」。**工作树截至 2026-11-19 未 commit。**

## 停止条件

- 需要改 `Compilers\VisualBasic\Portable\` 才能实现 → 停，回评审。
- 抽 core 无法保持 `vbi` 输出等价 → 停，改设计（不允许两套执行路径）。
- net472 冒烟无法通过且短期无解 → 降级为「v1 仅 net10」，回会议复评（复会，不改提案）。
