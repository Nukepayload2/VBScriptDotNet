# Blazor WebAssembly 纯前端 REPL 可行性 / Blazor WASM REPL Feasibility

* [x] Proposed
* [ ] Prototype: Not Started
* [ ] Implementation: Not Started
* [ ] Specification: Not Started

> 状态说明：本提案为**新提案（Proposed）**，三态判定待 LDM 会议评估；按 `proposals/README.md` 的 active 目录规则暂列根目录。
>
> 证据标记用仓库三态：**实锤**（已运行或有结果支撑）、**推测**（已检查后推断，未运行验证）、**猜测**（无证据，须附「补证动作」）。本提案的 PoC 读数取 **2026-11-15 工作树**；临时探针工程未入库，临时补丁已回滚。

## Related
[related]: #related

- **相邻 UI 路线**：[`proposal-avalonia-ise-repl-ui.md`](proposal-avalonia-ise-repl-ui.md)（桌面 ISE 布局）、[`proposal-vscode-extension-ise-repl-ui.md`](proposal-vscode-extension-ise-repl-ui.md)（VS Code 扩展）。本提案补的是**浏览器交付**这条独立路线，不替代前两者。
- **被本提案触碰的既有能力**：[`proposal-vbi-nuget-reference.md`](proposal-vbi-nuget-reference.md)（`#R "nuget:"` 依赖 `dotnet restore` 进程）、[`proposal-vbi-project-reference.md`](proposal-vbi-project-reference.md)（`#R "project:"` 依赖 `dotnet build` 进程）、[`proposal-reference-directive.md`](proposal-reference-directive.md)（`#R` 的宿主 resolver 契约）、[`proposal-load-directive.md`](proposal-load-directive.md)（`#Load` 的文件解析）。
- **外部依据**：Native AOT 的限制（[Microsoft Learn: Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)）；Blazor WebAssembly AOT 与解释器（[Microsoft Learn: Blazor WebAssembly build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0)）。

## Summary
[summary]: #summary

本提案记录「把 `vbi` REPL 做成 Blazor 纯前端网页」的可行性调查，并给出边界结论。

结论分三档：

1. **纯前端完整替代 `vbi`：No-Go。** 在保持现有功能语义的前提下，NuGet 还原、真实文件系统、`Process`、COM/P/Invoke、WinForms/WPF/WinUI、`.NET Framework` 宿主与 `End` 等能力与浏览器沙箱直接冲突。
2. **纯前端受限 Playground：Go，但有明确范围。** PoC 与第 7 节复测已证明：在 .NET 10 Blazor WebAssembly 的 **解释器模式**下，Roslyn VB 编译、`Assembly.Load(byte[])`、`Reflection.Emit`、`AssemblyLoadContext` 路径、状态延续与 `Console` 捕获都能跑通。代价是需要宿主注入参考程序集、虚拟文件系统，并放弃上列桌面能力。
3. **要完整 Web 化：走 Blazor Server，或 WASM UI + 服务端执行内核。** 这两条都不是纯前端，但能复用现有 `CommandLineRunner` 与桌面侧进程能力。

本提案建议：**若“纯前端”是硬要求，只批准受限 Playground；若目标是功能等价，优先 Blazor Server。** 不把 Native AOT 作为 REPL 的默认部署模式：官方限制明确“无动态加载”，实测 `Assembly.Load(byte[])` 在 Native AOT 下抛 `PlatformNotSupportedException`；Blazor WASM AOT 是另一条运行时路径，未实测，列为 blocking unresolved。

## Motivation
[motivation]: #motivation

### 为什么要调查

- **分发与触达**：`vbi` 当前是控制台程序，商店版又套 WinUI3 启动器；Web 版可以做到零安装、可分享链接、跨设备打开，对“试一段 VB 代码”的场景有明显价值。
- **已有两条 UI 路线**：Avalonia 走桌面 ISE，VS Code 扩展走编辑器内 REPL；浏览器是第三条独立路线，需要单独判断，不能拿前两者的结论替代。
- **技术假设需要校正**：常见说法是“浏览器不能动态加载程序集，所以 Roslyn REPL 不可能”。本次 PoC 证明该说法对 **.NET 10 WASM 解释器模式不成立**；但反过来，**Native AOT 确实不支持动态加载与运行时生成**。两个结论必须分开记录，否则会导出相反的决策。

### 本次要回答的问题

1. 现有 `VisualBasicScript` / `CommandLineRunner` / fork 编译器能否在 Blazor WASM 中复用？
2. `#R`、`#Load`、NuGet、`End`、桌面互操作在纯前端各自剩下什么？
3. AOT 是否是可用部署模式？
4. 如果做，最小可用范围与验收标准是什么？

### 期望结果

得到可执行的 Go/No-Go：纯前端只做「VBScript.NET Playground」，完整功能另选服务端路线；同时把 AOT 限制与需改造的宿主缝写清楚，供后续 plan 阶段直接使用。

## Detailed design
[design]: #detailed-design

### 1. 调查方法与 PoC

PoC 使用 **.NET 10 Blazor WebAssembly 独立应用**（`blazorwasm` 模板），直接引用本仓库的 `Scripting/VisualBasic/Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj`，在浏览器中执行以下验证。工程为仓库外临时工程，已删除；临时补丁未保留。

| 验证项 | 读数 | 证据 |
|---|---|---|
| `RuntimeFeature.IsDynamicCodeSupported` | `True` | 实锤：浏览器内实测 |
| `RuntimeFeature.IsDynamicCodeCompiled` | `False`（Mono 解释器） | 实锤：浏览器内实测 |
| `Assembly.Load(byte[])` + 反射调用测试程序集 | 成功，返回 `42` | 实锤：已运行 |
| `Reflection.Emit` 动态生成并执行 | 成功，返回 `42` | 实锤：已运行 |
| Roslyn 编译 VB 源码 → `Assembly.Load` → 入口点 | 成功 | 实锤：已运行 |
| `VisualBasicScript.RunAsync("1 + 2")` | 返回 `3`；约 222 ms | 实锤：已运行，单次观测，非基准 |
| `Dim x = 41` → `x + 1` 跨提交 | 返回 `42`；约 90 ms | 实锤：已运行 |
| `Console.SetOut` 捕获脚本输出 | 成功捕获 `hello-console` | 实锤：已运行 |
| `#R "本地.dll"` | `BC2017` 找不到库 | 实锤：已运行 |
| `#Load "本地.vbx"` | 找不到文件 | 实锤：已运行 |
| 执行 `End` | 页面失去响应；evaluate/screenshot 超时 | 实锤：已运行 |
| 发布目录总体积 | 约 65.8 MB（含 `.br`/`.gz`/`.map` 冗余）；`_framework` 约 35 MB | 实锤：已运行 |

#### 1.1 为跑通 PoC 临时改动的三处文件系统假设

当前代码不能直接在浏览器跑通，根因不是 Roslyn 编译器本身，而是**静态宿主假设 `Assembly.Location` 非空**：

1. [`Script.cs`](../../Scripting/Core/Script.cs#L256-L257) 用 `typeof(object).Assembly` 的 `Location` 创建 corlib 引用。浏览器中 `Location` 为空；底层 [`MetadataReference.cs`](../../Compilers/Core/Portable/MetadataReference/MetadataReference.cs#L354-L373) 在空路径上直接抛异常并尝试打开文件。
2. [`VisualBasicScriptCompiler.vb`](../../Scripting/VisualBasic/VisualBasicScriptCompiler.vb#L24) 用 `CreateFromAssemblyInternal(GetType(Strings).Assembly)` 创建 VB 运行时引用，同样依赖 `Location`。
3. [`VisualBasicScriptCompiler.vb`](../../Scripting/VisualBasic/VisualBasicScriptCompiler.vb#L36-L46) 的 XML LINQ 引用通过 `Assembly.Location` 推导 `System.Private.Xml.Linq.dll` 路径。

PoC 的做法是：**由宿主以字节形式注入 corlib / VB 运行时 / XML LINQ 参考程序集**，避免任何 `Assembly.Location` 与 `CreateFromFile`。这只是可行性验证，不是生产补丁；生产方案必须把引用来源做成宿主接口，而不是删除引用。

#### 1.2 参考程序集必须单独部署

.NET 10 Blazor 的 `_framework` 资产是 WebCIL `.wasm`，不是可直接交给 `MetadataReference.CreateFromFile` 的 `.dll`。PoC 首次尝试 `_framework/System.Console.dll` 返回 404；改用 **net10.0 参考程序集包**（实测 167 个 DLL，未压缩约 5.77 MB），通过 `MetadataReference.CreateFromImage` 注入后才编译成功。

这条是纯前端方案的实际成本：**参考程序集包要和 Roslyn 一起部署，不能指望浏览器里的运行时程序集直接当编译引用。**

### 2. AOT 约束（本次调查的关键修正）

#### 2.1 Native AOT 的官方限制

Microsoft 的 Native AOT 限制页明确列出：

- **No dynamic loading, for example, `Assembly.LoadFile`.**
- **No runtime code generation, for example, `System.Reflection.Emit`.**

来源：[Native AOT deployment — Limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)（推测：官方页面已核对，非运行验证；页面最后更新 2026-11-05）。

#### 2.2 实测：Native AOT 下 `Assembly.Load(byte[])` 抛 `PlatformNotSupportedException`

用 .NET 10 控制台程序设置 `<PublishAot>true</PublishAot>`，`dotnet publish -c Release -r win-x64` 后运行：

```csharp
using System;
using System.Reflection;

try
{
    var asm = Assembly.Load(new byte[] { 0x4D, 0x5A });
    Console.WriteLine("LOADED " + asm.FullName);
}
catch (Exception ex)
{
    Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
}
```

输出：

```text
System.PlatformNotSupportedException: Operation is not supported on this platform.
```

这里传入的是不完整 PE（只有 `MZ` 头），但异常发生在平台能力检查阶段，早于 PE 解析；因此它证明的是 **API 在该运行时不受支持**，不是“坏镜像”。（实锤：已运行）

**结论：REPL 的「运行时 emit IL → `Assembly.Load` → 执行」模型与 Native AOT 不兼容。** 若产品要求 Native AOT 作为默认部署，纯前端 REPL 在本提案范围内为 No-Go；服务端 Native AOT 同样不能承载这个模型。

#### 2.3 Blazor WASM AOT 与 Native AOT 不是同一条路径

Blazor WebAssembly 的 `<RunAOTCompilation>true</RunAOTCompilation>` 走的是 **Mono/WebAssembly AOT**，不是 Native AOT。官方文档说明了它与解释器模式的差异（**实锤**：文档已核对），但**没有**像 Native AOT 限制页那样逐条列出“动态加载不支持”（**推测**：基于文档缺项，非运行验证）。

因此本提案分开记录：

- **解释器模式（本次 PoC）**：`Assembly.Load` / `Reflection.Emit` 已运行通过，是唯一已验证的纯前端基线。
- **Blazor WASM AOT**：未实测；不能把 Native AOT 的结论直接外推，也不能假设可用。**在完成专项 spike 之前，不把 AOT 作为可选部署承诺。**
- **Native AOT**：官方限制 + 实测都表明不可用；不作为 REPL 部署模式。

这条也是本提案对“AOT 是否可行”的正式回答：**默认解释器；AOT 列为阻断性未决项，不进入实现承诺。**

### 3. 纯前端功能边界

| 能力 | 现有实现 | 纯前端判断 |
|---|---|---|
| 宿主入口 | [`Vbi.vb`](../../Interactive/vbi/Vbi.vb#L22-L44) 走 WinForms/WPF 初始化；[`vbi.vbproj`](../../Interactive/vbi/vbi.vbproj#L7-L26) 目标为 `net10.0-windows;net10.0;net48` | 需新建纯 `net10.0` Blazor 宿主；桌面入口不复用 |
| REPL 循环 | [`CommandLineRunner.cs`](../../Scripting/Core/Hosting/CommandLine/CommandLineRunner.cs#L271-L366) 同步 `ReadLine()`；[`ConsoleIO.cs`](../../Scripting/Core/Hosting/CommandLine/ConsoleIO.cs#L15-L33) 绑定静态 `Console` | 必须改异步输入队列 + 虚拟终端；逻辑可复用，I/O 不可直接复用 |
| 编译与引用 | Roslyn 从文件路径/GAC/运行时目录解析引用 | 可复用编译器；引用必须改为宿主注入镜像（见 1.1/1.2） |
| 动态执行 | [`ScriptBuilder.cs`](../../Scripting/Core/ScriptBuilder.cs#L160) emit 到内存；[`CoreAssemblyLoaderImpl.cs`](../../Scripting/Core/Hosting/AssemblyLoader/CoreAssemblyLoaderImpl.cs#L48-L60) 经 ALC 加载 | PoC 已验证解释器模式可用；长会话程序集不可卸载，需内存压测 |
| NuGet `#R "nuget:"` | [`NuGetRestoreRunner.vb`](../../Scripting/VisualBasic/Hosting/NuGetRestoreRunner.vb#L311-L366) 的 `RestoreAsync` 调 `RunDotNet("restore ...")`（`:330`；`Process.Start` 在其下游的 `RunDotNet` 内，`:427`），并写临时工程、镜像 `NuGet.config`、读 `project.assets.json` | 当前实现不可直接使用；纯前端只能重写为浏览器端 NuGet 客户端（有限包、无 MSBuild targets、无原生资产），或接服务端代理 |
| `#Load` / 本地 `#R` | 基于 `SourceFileResolver` / 文件路径 | 只能虚拟文件系统（上传、OPFS、内存）或 URL；`C:\path\file.vbx` 这类本地路径语义不再成立 |
| `End` | [`LocalRewriter.vb`](../../Compilers/VisualBasic/Portable/Lowering/LocalRewriter/LocalRewriter.vb#L865-L878) 降级到 `ProjectData.EndApp` | 实测页面失去响应；需改 lowering 或替换运行时行为，不能沿用现有语义 |
| COM / P/Invoke / WinForms / WPF / WinUI / `.NET Framework` | 由 Windows 宿主提供 | Blazor WASM 目标不包含这些能力；不是“实现难”，是运行环境不存在 |
| `Process` / 注册表 / 真实多线程 | 现有 vbi 可直接使用 | 浏览器无进程/注册表；线程受 WASM 运行时限制，需逐项验证 |
| 体积与启动 | Roslyn VB 编译器 wasm 约 6.16 MB（压缩约 1.4 MB）；`Microsoft.CodeAnalysis` 约 3.17 MB（压缩约 0.9 MB） | 首次下载明显重于普通网页；解释执行更慢；需设体积预算与加载体验 |

### 4. 候选方案

| 方案 | 纯前端 | 功能覆盖 | 改动量（猜测：粗估，未核算） | 适用 |
|---|---|---|---|---|
| **A. Blazor Server 包现有 REPL** | 否 | 高：可复用 `CommandLineRunner`、NuGet/进程能力 | 中 | 内部工具、快速完整 Web 化 |
| **B. Blazor WASM 受限 Playground** | 是 | 中：语言核心、状态延续、格式化输出、上传脚本、虚拟 `#Load` | 大 | 公开演示、学习、轻量脚本 |
| **C. WASM UI + 服务端执行内核** | 否 | 高：执行/还原放服务端 | 大 | 产品级完整功能 |
| **D. 桌面 vbi + Web 终端/远程会话** | 否 | 高 | 小到中 | 过渡方案 |

### 5. 推荐路线

#### 5.1 如果“纯前端”是硬要求：只做 B，明确边界

**范围内：**

- 多行提交、顶层代码、`Imports` 累积、`?` / 自动打印、VB 格式化。
- 单会话状态延续（`ScriptState` + `ContinueWith`）。
- `Console` 输出捕获；不保证 `Console.ReadLine` 等交互式控制台 API。
- 上传 `.vbx`；虚拟 `#Load`（上传/内存文件系统）。
- 上传或 URL 方式 `#R`（`MetadataReference.CreateFromImage` + `Assembly.Load`）。
- 明确“不支持”的能力清单与“下载桌面版 vbi”入口。

**范围外：**

- `#R "nuget:"` 完整还原（可后续用服务端代理或有限客户端替代）。
- 本地路径 `#Load` / `#R`、`Process`、COM、P/Invoke、WinForms/WPF/WinUI、`.NET Framework` 宿主。
- `End` 的进程退出语义（在 Web 端改为“结束会话”或明确禁用，需单独设计）。
- Native AOT；Blazor WASM AOT 在 spike 通过前不承诺。

**建议结构：**

```text
Blazor UI（编辑器 + 输出面板 + 会话列表）
  └─ BrowserReplSession
       ├─ IReplHost：异步输入/输出、颜色、取消
       ├─ IReferenceProvider：参考程序集镜像（ref pack / 上传）
       ├─ IVirtualFileSystem：#Load、脚本路径、工作目录
       ├─ Roslyn VB 编译（解释器模式）
       ├─ InteractiveAssemblyLoader / ALC
       └─ Console 适配器 + 会话存储（内存 / IndexedDB）
```

实现要点：

1. 抽 `IReplHost`，替换 `ConsoleIO.Default`；把同步 `ReadLine` 改为 `ValueTask<string?>` 输入队列。
2. 抽 `IReferenceProvider`；禁止在 `Scripting` 核心继续假设 `Assembly.Location`。
3. 抽 `IVirtualFileSystem`；`#Load` / `#R` 的路径解析走虚拟根。
4. 参考程序集包独立部署；对 Roslyn 关闭裁剪或显式 root，发布后必须跑 smoke test。
5. 编译/执行放 Web Worker，避免长编译冻结 UI；Worker 方案需单独验证内存与启动成本。
6. 纯前端不执行服务端代码；对公开部署设置 CSP、无敏感凭据、资源限额（脚本仍可发起网络请求，不能只靠“浏览器沙箱”）。

#### 5.2 如果目标是完整功能：A 或 C，不叫“纯前端”

- **A（推荐先做）**：Blazor Server 作为 UI，服务端复用 [`CommandLineRunner.cs`](../../Scripting/Core/Hosting/CommandLine/CommandLineRunner.cs#L271-L366)；把 `ConsoleIO` 换成 SignalR 异步 I/O；NuGet/`Process`/文件能力留在服务端。
- **C（产品级）**：WASM UI + 服务端执行 API；可保留离线只读/受限 Playground 作为降级。
- 服务端执行任意脚本必须容器隔离、资源限额、网络限制、会话超时与取消；不能把“能跑 REPL”直接等同于“可公开托管”。

### 6. 验收标准（若进入 plan）

1. `dotnet publish -c Release` 的 Blazor WASM 产物能启动，且 **不启用 AOT**；发布后运行 `VisualBasicScript.RunAsync("1+2")` 返回 `3`。
2. 至少 10 个脚本场景：顶层代码、`Imports`、`?`、多行、异常诊断、`Await`、`Console` 捕获、Unicode、状态延续、上传 `.vbx`。
3. 编译错误能在 UI 中显示诊断码与位置；不因异常终止整个 Blazor 会话。
4. `#Load` 虚拟文件与 `#R` 上传 DLL 至少各 1 条正例、1 条负例。
5. 明确 `End` 在 Web 端的行为（拒绝/结束会话），并有测试。
6. 长会话（建议 1000 次提交）内存有上限与观测数据；程序集不可卸载的风险有记录。
7. 文档列出“纯前端不支持”的能力，不把 `vbi` 等价作为宣传口径。
8. 若产品要求 AOT：先完成 Blazor WASM AOT spike；Native AOT 已判定不适用，不进入验收。

### 7. 交付形态复测（实锤）

用交付形态（`Nukepayload2.Compilers.VBScriptDotNet.Scripting` 的 facade，而非 §1 的 `Microsoft.CodeAnalysis.VisualBasic.Scripting` 门面）复测，确认 §1.1 的三处 `Assembly.Location` 假设是否仍然阻断。

**环境**：`blazorwasm` 模板、`net10.0`、Debug（解释器模式）、`dotnet run`，`WebAssemblyHostBuilder` + `AddVBScripting(IServiceCollection 重载)`。示例工程为一次性验证工程，未入库。

| 验证项 | 读数 |
|---|---|
| `VBScript.RunAsync("Print(1)")` | 失败：`TypeInitializationException` → `NotSupportedException`「无法对不含位置的程序集创建元数据引用」（`VisualBasicScriptCompiler.vb:24`） |
| DI `IVBScriptSessionFactory.CreateSession` + `SubmitAsync("1+1")` | 同上，会话建不起来 |
| `typeof(Object).Assembly.Location` / facade 程序集 `Location` | 均为空串；`TRUSTED_PLATFORM_ASSEMBLIES` = null |
| `Assembly.Load(byte[])` / `AssemblyLoadContext.Default.LoadFromStream` | **均成功**装载 4096 B PE，反射调用返回 42 ⇒ 执行期不是死路 |
| `<WasmEnableWebcil>` 默认值时 `_framework` | 207 个 `.wasm` / 0 个 `.dll` |
| 设 `<WasmEnableWebcil>false</WasmEnableWebcil>` 后 | 206 个 `.dll` / 1 个 `.wasm` ⇒ 可拿到真 PE image |

**结论（推测）**：与 §1.1 一致——阻断点在编译期引用解析（`Location` 为空、TPA 为空、默认引用集是简单名），不在执行期；修法与 §1.2 相同（宿主以镜像形式注入参考程序集，或从 `_framework` 取 PE）。据此，当前实现下纯前端 Blazor WebAssembly 尚不具备可用性，不建议进入实现；是否列入支持范围待 LDM 会议评估，裁决按 `decisions.md` 的引用纪律（按节号）落账，当前无对应条目。

包 README 的「已知限制」按限制说明记载该阻断（编译期无法解析引用、执行期可用）；支持与否的裁决不在本提案或包文档中落定。

若将来立项，最小改动面：

1. `VisualBasicScriptCompiler.vb:24` 与 `:36-46` 的两处静态引用改为可注入或可缺失。
2. 提供宿主注入 `MetadataReference`（按镜像）的公共入口，替代 TPA 解析器。
3. 宿主侧按 `blazor.boot.json` 映射 `_framework` 资产并关 WebCIL，或随包部署 net10 参考程序集。
4. 评估解释器下的编译耗时与内存。

## Drawbacks
[drawbacks]: #drawbacks

- **功能缩水是结构性的，不是实现细节**：NuGet、COM、P/Invoke、桌面 UI、`.NET Framework`、`End` 都无法在纯前端保持原语义。用户看到的将是“另一个产品”，不是“网页版 vbi”。
- **两套宿主、两套测试面**：Blazor 宿主 + 桌面宿主需要同时维护 I/O、引用、文件系统、错误展示；共享核心越深，宿主缝越多。
- **体积与启动成本**：Roslyn + 参考程序集包 + Blazor 运行时使首次加载明显重于普通网页；移动端内存与低端设备体验需要单独评估。
- **解释器性能**：PoC 的 222 ms/90 ms 只是简单表达式；复杂脚本、循环、反射型代码会明显慢于桌面 JIT。
- **AOT 天花板**：Native AOT 明确不可用；Blazor WASM AOT 未验证。若未来要求 AOT，当前“运行时 emit + load”架构需要重做。
- **安全面**：公开 Playground 执行任意代码，脚本可以发起网络请求或消耗 CPU；浏览器沙箱只能挡住 OS 访问，不能替代产品级滥用防护。
- **维护成本**：参考程序集版本必须与运行时绑定；.NET 升级会同时影响 Roslyn、ref pack、WASM 运行时与 Blazor。

## Alternatives
[alternatives]: #alternatives

- **Blazor Server**：功能覆盖最高、改动相对小；不是纯前端，需要服务端与沙箱。适合内部工具或可信用户。
- **WASM UI + 服务端执行内核**：保留 Web UI 体验，执行留在服务端；适合产品级完整功能，但仍有服务端成本。
- **Avalonia ISE / VS Code 扩展**：已有提案，分别覆盖桌面与编辑器内体验；不解决“零安装网页分享”，但功能与实现风险更可控。
- **桌面 vbi + Web 终端/远程会话**：最小改动获得远程访问；不是 Blazor 纯前端，但可作为过渡。
- **只做静态编译/诊断，不做执行**：浏览器里只跑 Roslyn 编译与错误提示，不加载执行用户程序集；可完全避开 AOT/动态加载问题，但不再是 REPL。
- **暂不做**：保留桌面 REPL，把资源投入 NuGet/桌面体验等现有痛点。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **AOT 是否是产品硬要求？** 若是，纯前端 REPL 在本提案证据下应直接 No-Go；若否，解释器模式是唯一基线。
2. **Blazor WASM AOT 是否支持 `Assembly.Load`/动态 IL？** 未实测。需要 `RunAOTCompilation=true` 的发布版 spike，读取 `RuntimeFeature.IsDynamicCodeSupported` 并实际执行一次 emit→load。Native AOT 已实测不支持，不能替代该 spike。
3. **NuGet 策略**：纯前端放弃、接服务端代理，还是重写有限客户端？三者的功能承诺与工作量差异很大。
4. **`End` 语义**：Web 端拒绝、结束会话、还是改 lowering/运行时？需要单独提案或明确不支持。
5. **参考程序集打包**：独立 ref pack、嵌入程序集、还是按需下载？体积、缓存、版本绑定与裁剪策略待定。
6. **Web Worker**：编译/执行是否放 Worker？收益是 UI 不冻结，代价是第二套运行时、内存与启动成本。
7. **长会话内存**：`InteractiveAssemblyLoader` 的程序集不可卸载；1000 次提交后的内存曲线与上限未测。
8. **安全模型**：公开 Playground 是否允许网络请求？是否需要登录、配额、内容审查或 iframe 隔离？
9. **浏览器范围**：桌面 Chrome/Edge 优先，还是同时承诺移动端？移动端 WASM 内存与键盘交互未评估。
10. **LDM 定位**：本提案是 active 根目录的 Proposed；三态判定、优先级（D4）与是否进入 plan 待会议评估。
