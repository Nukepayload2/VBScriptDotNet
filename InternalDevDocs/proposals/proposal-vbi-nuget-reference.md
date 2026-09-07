# vbi 脚本/交互窗口 NuGet 包引用：`#R "nuget:包名[, 版本]"` / NuGet Package References in `.vbx` scripts

> **RESOLUTION Active**：见 `../meetings/meeting-vbi-nuget-reference.md`（2026-09-06）。接线 R4 经复会改判：扩展共享 Core 支持单 `#R`→N 引用（补上游 `CommonReferenceManager.Resolution.cs:883-887` 的 `// TODO: implement`）作为 U9 spike 目标架构（受 spike 硬门控；不 alter 旧 `GetDirectiveReference`、N 语义走新增 API/路径；spike 不过回退宿主拦截）；TFM 镜像复会 2 收窄为只镜像运行宿主 TFM；U5 native 复会 3：net10 支持实测（sqlite 类先行、loader seam `ResolvingUnmanagedDll`），WindowsAppSDK 类 net10 后置，net48 范围外。

* [x] Proposed
* [x] Prototype: In Progress（U9 spike 实证：共享 Core 单 `#R`→N；见 `../meetings/meeting-vbi-nuget-reference.md`）
* [ ] Implementation: [Not Started]
* [ ] Specification: [Not Started]

## Summary
[summary]: #summary

本提案主张：让 `.vbx` 脚本与交互窗口提交用 `#R "nuget:包名[, 版本]"` 引用 NuGet 包。机制主张为——把脚本里出现的 nuget 引用集合翻译成一个**内容寻址的临时还原工程**，交给 **dotnet CLI** 还原（引擎候选 C1，要求机器上有 .NET SDK），读 `obj/project.assets.json` 的 compile/runtime 资产分别喂编译引用与运行时加载；接线主张为——fork `NuGetPackageResolver` 的解析（**逗号 + 大小写不敏感前缀**，VB 语言大小写不敏感，有意偏离 .NET Interactive/上游的仅小写前缀），把 Roslyn 上游 2015 年遗留的 `NuGetPackageResolver` 抽象缝接活成**状态化 seam**：编译器侧保持同步缓存前端，真正的异步还原由**宿主在两提交之间**驱动。

三条主张（均待 LDM 审视，**非已裁决**；候选与成本见 Alternatives / Unresolved）：

- **引擎（倾向候选 C1）**：临时还原工程镜像「当前运行的这一个 vbi 宿主」的 TFM / windows 平台版本 / RID / 已解析 SDK 版本，只 `restore` 不 `build`；不自理用户自定义 framework reference。其余引擎候选见 Alternatives。
- **接线（倾向候选 C3）**：`#R "nuget:…"` 字符串仍进编译器引用解析（fork 后的 `TryParsePackageReference` 识别逗号），resolver 同步读会话缓存；异步还原在宿主环两提交之间执行；编译命中未还原包时触发「还原 → 重编」一次。
- **门控**：只有出现 nuget 引用才要求 SDK；无 nuget 引用的脚本全链路零行为、零报错（零回归目标）。

相关提案（勘误见 Unresolved U7）：`proposal-vbscript-lsp.md`（其 :121「`#R nuget:` 已实现、LSP 零新增」为**与代码不符的断言**）；`proposal-distribute-compiler-nuget-package-and-dotnet-tool.md`（其 :112 语法写成 `#R "nuget: Package, Version"` 并称「复用已有实现」，与实际代码相冲突）。

## Motivation
[motivation]: #motivation

### `.vbx` 引用面现状只覆盖本机/运行时程序集，没有包生态接入

`.vbx` 脚本现在只能 `#R` 本地 dll 与运行时平台程序集——`Samples\WpfCpuCoreInformation.vbx:1-3` 引用 `PresentationCore.dll`/`PresentationFramework.dll`/`WindowsBase.dll` 即走 TPA/本机路径；没有任何「从 NuGet 拉包」的通道。脚本要跑现代库（JSON、HTTP、UI 工具包）就必须补这条通道。

### 官方 C#/VB 脚本路线已弃 Scripting，VB 无 file-based 继任——NuGet 支持只能自补

调查期线上取证（见「证据来源与证据等级」生态对照）：

| 事实 | 来源 |
|------|------|
| .NET Interactive / Polyglot 2026-04 弃用，点名 **file-based apps** 为继任 | dotnet/interactive#4163 |
| csx 被官方排除，官方指去第三方 dotnet-script | dotnet/sdk#49222 |
| csi 无官方新 REPL；dotnet/csi 进 CLI 未排期 | dotnet/roslyn#17666 |
| VB 无 file-based 继任（"can opt in **later**"） | dotnet/sdk#55716 |

官方示范的答案恰是「临时工程 + dotnet/MSBuild restore」（file-based apps：`#:package` → 内存造 `.csproj` 交给 MSBuild）。VB 无继任 → vbi 的 NuGet 支持必须自己补，且**官方示范的形态与本提案 C1 同构**。

### fork 改 Scripting 共享层的冲突成本趋零

上游把 Scripting 边缘化后，`Microsoft.CodeAnalysis.Scripting` 层几乎不再有特性投入 → fork 该层（改解析器/注入位/加子类）与上游基线 merge 的冲突概率低。这解除了「改共享层 = 永久 merge 面」的主要顾虑。

### 修一份失实记录：`NuGetPackageResolver` 是空转抽象缝

`NuGetPackageResolver`（`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:13-51`）是上游遗留的抽象缝：本仓 grep 全树**无任何子类**、`ResolveNuGetPackage` 无实现；`RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver` 硬编码 `packageResolver: null`（`:59`），调用点恒空。但 `proposal-vbscript-lsp.md:121` 声称「`nuget:` 前缀解析已实现、LSP 零新增」，`proposal-distribute…:112` 声称「复用已有 NuGetPackageResolver 不重复实现」——两者都与代码不符。本提案的方向之一是**把这条缝接活**（而不是留一个「看起来能用实则空转」的状态）；两处文档的纠正口径见 U7。

## Detailed design
[design]: #detailed-design

### 0. 机制与现状（设计输入，全部已检查）

| # | 事实 | 锚点 |
|---|------|------|
| E1 | `#R "…"` 的值是 `StringLiteralToken` → `ReferenceDirectiveTriviaSyntax`，仅 Script 源码合法（非脚本报 `ERR_ReferenceDirectiveOnlyAllowedInScripts`） | `Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472` |
| E2 | 脚本/编译**两条** resolver 都由 `CommandLineRunner.GetMetadataReferenceResolver` 构造 → `CreateCurrentPlatformResolver(...)`（不传 `packageResolver`，即 null）；`GetScriptOptions` 把它挂进 `ScriptOptions.MetadataResolver` | `CommandLineRunner.cs:161-180`（resolver 在 :165/:179）、`:190-200`；`RuntimeMetadataReferenceResolver.cs:51-63`（null 在 :59） |
| E3 | 编译路径的 `VbiCompiler` 覆写 `GetCommandLineMetadataReferenceResolver` 复用同一工厂 → 注入该工厂 = 脚本与编译两路同得（U6 相关） | `Interactive\vbi\Vbi.Compile.vb:165-167` |
| E4 | `ResolveReference` 已有 nuget 分支：`TryParsePackageReference` 命中**且 `PackageResolver != null`** 才解析；否则空回 → 下游「找不到元数据」 | `RuntimeMetadataReferenceResolver.cs:144-154` |
| E5 | `NuGetPackageResolver` 抽象无实现；fork 全树无子类；上游引入于 2015-10-04（git log 9005cb34cf1，前期线上取证） | 本仓 `NuGetPackageResolver.cs:13-51`；grep 全树无 `Inherits NuGetPackageResolver` |
| E6 | 运行时把编译绑定的文件引用 `RegisterDependency(identity,path)` 进 `InteractiveAssemblyLoader`；依赖解析先探自身目录再查注册表 | `Scripting\Core\ScriptBuilder.cs:142-151`；`InteractiveAssemblyLoader.cs:168`（`RegisterDependency`）、`:264-345`（依赖解析） |
| E7 | 现代 NuGet(7.9，SDK 10.0.4xx) no-op restore **校验包文件在盘**（`VerifyRestoreOutput`/`Sha512Exists`），清全局缓存后自动重下，不会静默漏检 | NuGet.Client 7.9.0.83 源码（调查期 chrome 取证）；learn.microsoft.com/nuget 官方文档 |

推论：resolver 是唯一咽喉（E2–E4）；运行时加载对「还原出的路径清单」现成友好（E6）；restore 坏档/清缓存由 NuGet 自己兜（E7）；改 Scripting 层成本低（见 Motivation）。

### 1. 语法与解析（候选主张：fork `NuGetPackageResolver` 为逗号 + 大小写不敏感）

对齐 dotnet-interactive csx 逗号设计——先例 `dotnet/interactive（git remote origin）仓库：src\Microsoft.DotNet.Interactive\PackageReference.cs:26-61`：`value.Trim([' ','\t','"'])` → 前缀 `nuget:` → 按首个 `,` 拆两段（limit 2）各 Trim → 名必填、版本可省。**唯一有意偏离：前缀大小写不敏感**（VB 语言大小写不敏感，`NuGet:`/`NUGET:` 均合法）。上游 `NuGetPackageResolver.cs:22` 用 `StringComparison.Ordinal`、.NET Interactive 用大小写敏感的 `StartsWith("nuget:")`（`PackageReference.cs:30`），都是仅小写。

fork 后 `TryParsePackageReference(reference → (name, version))` 规则（静态签名不变，调用点 `RuntimeMetadataReferenceResolver.cs:146` 零改动）：

1. 去首尾 space/tab/引号。
2. 前缀匹配 `"nuget:"` 用 `OrdinalIgnoreCase`。
3. 去掉前缀（保留其后实际大小写），按**第一个 `,`** 拆两段（limit 2），各 Trim。
4. name = 段 1，非空必需。
5. version = 段 2（可省；空/缺省 = 未指定，语义见 U4）。

fork 该文件的测试（上游 `NuGetPackageResolverTests`）改断言大小写不敏感与逗号用例。已知副作用：`NuGetPackageResolver` 位于 C#/VB 共享的 `Scripting\Core`，改逗号后 fork 的 C# scripting 也认逗号、不再认斜杠（Drawbacks）。

### 2. 调用形态与可运行示例

`.vbx` 文件执行：

```vbx
' demo-json.vbx —— 用 #R "nuget:…" 拉包并立即使用
#R "nuget:Newtonsoft.Json, 13.0.3"

Dim doc = Newtonsoft.Json.Linq.JObject.Parse("{""name"":""Ada"",""age"":36}")
System.Console.WriteLine(doc("name"))   ' 输出 Ada
```

交互窗口（REPL）提交：

```
> #R "nuget:Newtonsoft.Json, 13.0.3"
> ? Newtonsoft.Json.Linq.JObject.Parse("{""x"":1}")("x")
1
```

语法层面无需新语法树节点：`#R` 值本就是字符串字面量（E1），新增的是**该字符串的解析语义**（第 1 节）与**还原/加载接线**（第 3–5 节）。

### 3. 架构：编译器同步约束 → 状态化 seam + 宿主驱动环

关键约束（客观事实）：编译器绑定是同步链，`MetadataReferenceResolver.ResolveReference` 无 `await` 锚点（E4）；「下载还原」是异步长操作 → **异步只能放宿主环，resolver 保持同步当会话缓存前端**，不做「字面异步绑定」。

因此本提案主张的接线形态（候选 C3，倾向）：

- **同步前端**：具体 `NuGetPackageResolver` 子类的 `ResolveNuGetPackage(name, version)` **只读会话缓存**，命中返回 compile 资产路径数组；未命中返回空（调用点即回到 E4 的空回路径，表现成常规「找不到元数据」诊断）。
- **宿主驱动环**：REPL/脚本入口在**编译前**扫描当前提交/脚本文件里的 `#R "nuget:…"`（用 fork 后同一 `TryParsePackageReference`），累积「请求包集合」；集合较上次成功还原有增量 → `await dotnet restore` → 成功后把 assets 的 compile/runtime 路径写入会话缓存 → 再编译。还原错误（NU1101 等）抓回挂到对应 `#R` 行。
- **安全网**：若缓存未命中仍进入编译（如首次脚本直跑未预扫描），编译失败后可触发「还原 → 重编」一次；是否保留该安全网属实现细节（v1 建议保留，成本低）。

归属（子类/会话放哪一层）见 U1；跨进程是否跳过还原见 U2。

### 4. 还原工程与缓存（候选：内容寻址、跨脚本按精确集共享）

**缓存 key**（决定哪些脚本共享、哪些不共享；本提案主张）：

```
key = hash(
  精确包集合：按 id(OrdinalIgnoreCase 归一)+version 规范排序，
  宿主镜像元组：TFM + windows 平台版本 + RID，
  global.json rollForward 解析后的实际 SDK 版本，
  还原源指纹（用户级 nuget.config / feed），
  框架引用镜像（WindowsDesktop/WinUI 等——restore 靠它识别「框架已有」而非拉同名包）)
```

- 位置：`%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>\`（常驻，跨会话；LRU/按访问时间上限清理）。
- 临时工程文件**按 key 写一次、此后字节稳定**（不重新生成）→ dgspec 稳定 → NuGet no-op 才成立（E7）。
- **共享仅对精确相等集合**；子集/超集不共享（同一包在不同图会被压到不同版本，拿大图产物喂小图是错的）。脚本源码**不进 key**（restore 是包集合的纯函数）。

**restore 触发策略**（候选主张）：

| 情况 | 行为 |
|---|---|
| 包集合与上次 exit-0 相同（本进程内） | skip，零调用 |
| 集合变化（新增/改版本） | 必调（S+P 图不存在，非 NuGet 不可算；顺带自愈上次半途坏档） |
| 跨进程重跑同 key | v1 **总调**（NuGet no-op 廉价重验包存在，E7；自愈 + revalidate；性能权衡见 U2） |

- **vbi 不做资产状态判断**：不校验 assets.json、不探包存在。唯一有效信号 = 自己那次 restore 的退出码 0 + NuGet 自己的 `obj/project.nuget.cache`（E7）。离线/源缺失 → NU1101 → vbi 转清晰诊断。
- 浮动版本（`*`/`13.0.*`）：key 语义见 U3。

**SDK 门控**：出现 nuget 引用才查 `dotnet --list-sdks`；临时工程目录写 `global.json`（`{"sdk":{"version":"<当前 SDK>","rollForward":"latestMajor"}}`）允许跨主版本升。无匹配 → 在 `#R` 行报「需 .NET SDK ≥ X」。无 nuget 引用 → 全链路不启用（零回归）。上述缓存策略的边界仍需 LDM 审视（哪些入 key、清理策略、总调 vs skip），本提案给的是倾向，非裁决。

### 5. 接线改动点（文件清单，候选归属）

1. `Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs`：`TryParsePackageReference` 改逗号 + 大小写不敏感（第 1 节）。
2. 具体 `NuGetPackageResolver` 子类：`ResolveNuGetPackage(name, version)` **同步读会话缓存**返回 compile 资产路径。归属（Scripting/Core 内 vs 宿主工程经 IVT）见 U1。
3. `CommandLineRunner.GetMetadataReferenceResolver`（`CommandLineRunner.cs:190-200`）与 `RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`（`:51-63`，null 在 `:59`）：加 `packageResolver` 注入位，把会话 resolver 挂到脚本与编译两条路径（E2/E3 同一工厂）。
4. 宿主驱动环（`Interactive\vbi` + `Scripting\VisualBasic` 的 script/REPL 入口）：按第 3 节扫描/还原/喂缓存。
5. 运行时：assets 的 **runtime 闭包**路径喂 `ScriptBuilder.cs:142-151` 注册机制（沿用 `#r path` 同款，E6）；native 资产处理范围见 U5。

### 6. v1 范围与宿主镜像（候选，边界留 U5/U6/U8）

- **目标**：普通 lib 包 + **windows 桌面包**（临时工程按宿主声明 `UseWPF`/`UseWindowsForms`/`-windows` 平台版本）。net48 镜像的临时工程须自带 `Microsoft.NETFramework.ReferenceAssemblies` 包引用（先例 `Interactive\vbi\vbi.vbproj:55-59`；vbi 多目标 `net10.0-windows;net10.0;net48` 见 `:7`）。
- **已知边界**：WindowsAppSDK 类含 `runtimes/<rid>/native` 原生资产的包需加 native 探测根，超出现有 loader 能力（E6 只注册托管程序集 identity→path）——v1 列为已知限制 + spike（U5）。

## Drawbacks
[drawbacks]: #drawbacks

- **SDK 前提**：nuget 功能只在装了匹配 SDK（可跨主版本升，见 SDK 门控）的机器可用；商店交互宿主（终端用户机无 SDK）此功能缺失，只能靠清晰报错。
- **进程启动延迟**：每次集合变化 spawn 一次 `dotnet restore`（百毫秒级）；冷首包含网络。跨进程同 key 也总调（U2 讨论是否可省）。
- **Scripting/Core 多文件 diverg**：本提案 fork 该共享层的解析器/注入位/（视 U1）子类；虽上游已边缘化，仍属与基线字节差，合并时需留意；解析改动对 fork 的 C# scripting **同文件生效**（认逗号、不认斜杠）。
- **共享库带「shell dotnet」逻辑的耦合风险**：若具体子类/会话进 Scripting/Core，所有消费该 fork Scripting/Core 的宿主（含未来 LSP）都背上该行为（U1 权衡）。
- **还原的隐式网络行为**：脚本里出现 `#R "nuget:…"` 即触发潜在下载；对「完全离线/内网」用户是行为变化，需把 NU 错误转成可读诊断。
- **编译模式共享注入的越界风险**：注入点在脚本/编译共用工厂（E3），若不加闸，编译模式 `/r:nuget:` 会顺带点亮——是否 v1 想要需显式决定（U6）。

## Alternatives
[alternatives]: #alternatives

### 引擎层（C1 / C2 / 增量自解析 / Interactive-host / file-based 同款）

| 方案 | 无 SDK | 还原正确性 | 依赖与维护 | seam 生死 | 成本 / 收益 / 风险（留 LDM 裁） |
|---|---|---|---|---|---|
| **C1：dotnet CLI 临时工程还原（本提案倾向）** | ✗ | ★★★（MSBuild 级） | 低 | 活 | 收益：正确性最高、最贴合官方 file-based 示范、临时工程可留作调试现场；成本：SDK 前提、每次集合变化 spawn 进程；风险：需镜像宿主 TFM/SDK/RID（U8）、SDK 缺失路径要清晰报错 |
| **C2：进程内 NuGet.Commands 全量还原** | ✓ | ★★★ | 高 | 可活 | 收益：免 SDK、可进程内 await、无 spawn；成本：需在进程内背 Sdk/FrameworkReference/RID/TFM 项目概念与 `tools/` 依赖闭包，缺 MSBuild 还原 API（未验证）；风险：静态链接 NuGet.Commands 的版本/许可与体积，MSBuild 工程求值（Sdk 解析、props/targets 导入）难以在进程内复刻——C1 用真实 `dotnet restore` 恰好绕开这一整块 |
| **增量 NuGet.Protocol 自解析** | ✓ | ★★ | 中 | 活 | 收益：免 SDK、轻、可增量（只拉缺失包）；成本：自写 TFM 资产选择/依赖归约/框架引用逻辑；风险：自写归约器正确性风险最大，且无法表达 FrameworkReference / 平台化包，v1 就撞 WindowsAppSDK 类包 |
| **.NET Interactive 式 host `AddReferences`（不接线 seam）** | 视引擎 | 视引擎 | 低 | **死** | 收益：实现最自由（host 层直接加引用）；成本/风险：与「不留死代码」冲突、失去编译器 resolver 这个唯一咽喉（E2–E4），LSP 复用点（`RuntimeMetadataReferenceResolver`）丧失；本提案不主张，除非 LDM 决定显式废弃 seam |
| **file-based apps 同款虚拟工程** | ✗ | ★★★ | 低 | 死 | 收益：等于 C1 但纯编译器外、不 fork Scripting；成本/风险：仍要 vbi 宿主当驱动，且把引用解析搬出编译器 → 语义模型/诊断与 vbi 执行不一致；与 C1 相比不省事 |

> 补充：C1 与「file-based 同款」的差别只在「虚拟工程由谁造、resolver 接不接」；本提案主张 C1 的理由是**沿用编译器 resolver 唯一咽喉**、让 LSP/诊断天然一致。该取舍不是已裁决。

### 语法层（逗号 + 大小写不敏感 vs 上游斜杠 vs .NET Interactive 原样）

| 方案 | 收益 | 成本/风险 | 备注 |
|---|---|---|---|
| **逗号 + 大小写不敏感前缀（本提案主张）** | VB 大小写不敏感语言惯例；`#R "NuGet:…"` 不报错；与 csx/dotnet-interactive 逗号设计同构，用户心智迁移顺 | fork 共享解析器、C# 侧也变 | 与本仓已有两提案文档写法（`#R "nuget: Package, Version"`）最接近，勘误面最小（U7） |
| 上游斜杠 `nuget:name/version` 原样保留 | 零 diverg | 与 .NET Interactive/现代 NuGet 书写惯例（逗号）不一致；版本区段语义弱 | 上游原始设计（`NuGetPackageResolver.cs:18`）已随生态落后 |
| 小写敏感 + 逗号（.NET Interactive 原样） | 生态零偏离 | VB 用户写 `NuGet:` 会静默不识别 → 报「找不到元数据」而非清晰前缀错误 | 偏离理由见第 1 节 |

## Unresolved questions
[unresolved]: #unresolved-questions

1. **U1 — seam 实现归属：具体 `NuGetPackageResolver` 子类 + 还原驱动放哪一层。**
   编译器侧 resolver 在共享层 `Scripting\Core`（E2/E4），但「还原 + 会话缓存」是宿主行为。
   - 备选 A：具体子类 + dotnet-shell 还原逻辑进 `Scripting\Core`。收益：未来 LSP 等消费该 fork Scripting 的宿主**零额外接线白拿**；成本/风险：共享核心库背上「shell dotnet / NuGet 缓存目录 / 临时工程」逻辑，纯库消费者被拖累；与上游「实现在宿主」的预留分工（csi/InteractiveHost 均注入 null）偏离更大，diverg 面扩大。
   - 备选 B（建议）：`Scripting/Core` 只保留/复刻上游抽象缝与注入位，**引擎与会话状态落宿主**（`Interactive\vbi` 等经 IVT 注入 resolver）。收益：边界干净、与上游分工一致、共享层不背进程/shell 逻辑；成本：未来 LSP 需自建会话 + 还原状态机（复用点退化为「复用 resolver 装配」）；IVT 先例见 `vbi.vbproj:42-43`。
   - 备选 C：完全不动 `Scripting/Core`，宿主另起 parallel resolver 拦截 `#R`。成本：引用不进编译器 metadata 解析 → 语义模型/诊断与执行不一致，需另补编译器入口，fork 面反而最大。
   - 建议：B。证据锚：上游把解析抽象留在 `Scripting\Core\Hosting\Resolvers`、实现留空由宿主注入（`NuGetPackageResolver.cs:13-51`、`RuntimeMetadataReferenceResolver.cs:59` null）；`vbi.vbproj:42-43` IVT 先例。成本权衡：B 的「LSP 需自建会话」是显性代价，但 A 的共享层耦合是隐性长期代价。

2. **U2 — 跨进程是否省 `dotnet restore` 的进程启动（sentinel-skip）。**
   跨进程重跑同 key 每次都 spawn 一次 `dotnet restore`。
   - 备选 A（建议）：v1 **总调**。依据：E7——NuGet no-op 廉价重验包在盘，重复 spawn 正确性由 NuGet 兜、还顺带自愈坏档/清缓存；成本：每次 vbi 启动带 nuget 引用会多一次 dotnet 进程（百毫秒级，实测数据待定）。
   - 备选 B：sentinel-skip（缓存目录写成功标记，命中即跳过进程启动）。收益：省进程启动；成本/风险：标记需与 NuGet 自身 `obj/project.nuget.cache` 的失效语义对齐，否则包被外部删除/缓存被清时 sentinel 误报成功——等于把 E7 已交给 NuGet 的校验责任拿回来自担；维护成本高。
   - 备选 C：本进程内集合相同 skip（状态已在第 4 节主张）+ 跨进程总调（=A）；仅当延迟实测成为问题再评估 sentinel。
   - 建议：A（性能数据不足前不引入 sentinel）。证据锚：E7；延迟数字为待定预测。

3. **U3 — 浮动版本（`*` / `13.0.*`）的产品语义。**
   - 备选 A：首次还原后把解析出的**具体版本写回临时工程**并换 key。成本：key 分裂、写回破坏「字节稳定」（第 4 节 no-op 前提）、同一 `#R` 隔日可能不同 key，确定性反而差；与 dotnet 语义不符。
   - 备选 B（建议）：**保留请求 spec，纯 dotnet 语义**——key 含请求的浮动 spec；dgspec 不变则 NuGet 冻结上次解析（no-op 不重算），包集合变才整体刷新。收益：最小实现、与 `dotnet add/restore` 一致、缓存稳定；成本：浮动版本在「集合新增包」触发的那次 restore 里可能整体重算（NuGet 行为），用户要完全确定可 pin 版本。
   - 备选 C：禁止浮动版本，缺省/通配报错要求 pin。收益：最确定；成本：最不灵活，与 csx/dotnet 惯例相悖（.NET Interactive 显式把 `*`/`*-*` 当合法版本 spec：`PackageReference.cs:63` `IsPackageVersionSpecified`）。
   - 建议：B。证据锚：`dotnet/interactive（git remote origin）仓库：src\Microsoft.DotNet.Interactive\PackageReference.cs:63`；.NET Interactive 把请求 spec 原样喂 DependencyManager（`PackageRestoreContext.cs:135-137` 的 `Include=…, Version=…`）→ 纯 dotnet/F# 语义，未在 vbi 侧锁版本。

4. **U4 — 省略版本 `#R "nuget:X"` 的语义。**
   - 备选 A（建议）：v1 **版本必填**，缺省报清晰诊断「请指定版本（如 13.0.3）」。收益：v1 面窄、无隐式「最新解析」，与 U3 的解耦干净；脚本要可复现还原，pin 是特性；成本：书写多一串版本号。
   - 备选 B：缺省映射 `Version="*"`（latest stable），对齐 csx `#r:nuget:X` 惯用与 `IsPackageVersionSpecified`。收益：写起来短；成本：引入隐式最新解析，key 含 `*`（不锁版本），与 U3-B 组合需一并定义，确定性差。
   - 备选 C：先**对照 .NET Interactive 真语义**再定——其缺省/空版本行为要落到 `FSharp.Compiler.DependencyManager` 的 nuget provider（`PackageRestoreContext.cs:225-245` 经 `DependencyProvider` 调外部 provider），该外部实现本仓不可读，需 meeting 前补核。收益：语义不凭空造；成本：v1 语义悬空直到补核完成。
   - 建议：A（C 作为若 LDM 想对齐生态时的前置调查项）。证据锚：上游 `NuGetPackageResolver.cs:29-33` 缺省版本返回空串、语义未定义；.NET Interactive 空版本 → `GetPackageManagerLines` 输出 `Version=` 空值行（`PackageRestoreContext.cs:137`），实际解析在外部 DependencyManager（待核）。

5. **U5 — windows 桌面包与原生资产（`runtimes/<rid>/native`）的 v1 范围。**
   - 备选 A（建议）：v1 支持 **managed 资产**（含 windows-desktop 平台化包：TFM/平台版本选择仍由 MSBuild restore 决定）；对含原生资产的包（WindowsAppSDK 等）v1 列为**已知限制** + 报清晰错误，原生探测根留 spike。成本：部分包 v1 不可用；收益：改动面可控，不触碰 native 加载这个深水区。
   - 备选 B：v1 就做 native 探测——还原后扫 assets runtime 闭包的 `runtimes/<rid>/native` 并把目录加进 native 搜索。成本/风险：native 加载（P/Invoke 搜索路径）不在 `InteractiveAssemblyLoader` 能力内（E6 只注册托管 identity→path，`InteractiveAssemblyLoader.cs:168`）；需宿主级 `SetDllDirectory`/`NativeLibrary.Resolving`，且 net48 宿主无 `NativeLibrary` API（vbi 多目标含 net48，`vbi.vbproj:7`），两宿主行为分叉，改动面大。
   - 备选 C：windows 桌面包整体范围外 v1（只普通 lib），平台化托管包也后置。收益最窄；成本：恰是 `.vbx` 在 Windows 上最想要的库不可用，motivation 受损。
   - 建议：A。证据锚：E6 能力边界；`vbi.vbproj:7` 多目标；spike 结果未出（预测性待定）。

6. **U6 — `#R nuget:` 是否同时进编译模式（`/r:nuget:` / `.vb` 批编译）。**
   注入点在脚本/编译共用工厂（E3：`VbiCompiler.GetCommandLineMetadataReferenceResolver` 复用 `CommandLineRunner.GetMetadataReferenceResolver`）→ 两路可能**顺带同时点亮**。
   - 备选 A（建议）：v1 **仅脚本/REPL `#R`**，编译模式不动（即便注入共用工厂，宿主驱动环只在脚本/REPL 入口启用，编译模式不触发还原）。收益：聚焦（本提案能力=脚本引用），`.vb` 走工程/MSBuild 才是正道；成本：`.vb` 单文件拉包无快捷通道。
   - 备选 B：双路（`#R` + `/r:nuget:` 都支持）。收益：编译模式也方便；成本：`/r:` 语义（裸名 TPA/GAC/路径）混入 `nuget:` 前缀需 parser 特判，歧义面大；还原驱动环要进编译路径（vbi 批编译是短进程，缓存预热/会话管理另起一套）。
   - 备选 C：编译模式显式报「请用 dotnet add + .vbproj」导去工程。
   - 建议：A。证据锚：E3 共用工厂说明「技术上顺带可用」是 U6 必须显式决定的原因；范围主张见第 6 节。

7. **U7 — 旧提案失实/过期断言（`proposal-vbscript-lsp.md:121`、`proposal-distribute…:112`）的纠正方式。**
   两处都写成「nuget 引用已实现/直接复用已有实现」，与本仓代码（E5 零实现、E2/E4 注入 null）不符；且两处语法写法是 `#R "nuget: Package, Version"`（前缀后带空格），与上游斜杠解析、本提案逗号解析都不一致。
   - 备选 A（建议）：**不改冻结正文**，本提案经 meeting RESOLUTION 把「纠正」作为本提案的附注/supersede 口径，落地后在两文档相关行加勘误指针。收益：不动已评审文本、不破坏各自会议纪要锚点；成本：短期内两处仍会误导读者，靠本提案指正兜底。
   - 备选 B：直接 patch 两文件正文。收益：就地修正；成本：若两文件正文已被会议纪要引用，patch 需同步改纪要，链式改动面大，与「新决议 supersede 旧文」的归档惯例冲突。
   - 备选 C：单独发一份勘误 note 文件，本提案与两文档都链它。
   - 建议：A（C 为可选补充）。证据锚：`proposal-vbscript-lsp.md:121`、`proposal-distribute…:112` 原文；E5 代码事实。

8. **U8 — windows 平台版本（`net10.0-windows<ver>` 的平台版本号）取值来源。**
   镜像宿主到临时工程需要平台版本（`UseWPF`/`UseWindowsForms` 时由 SDK 映射，但显式 `-windows` 平台版本需确定值）。
   - 备选 A（建议）：读宿主程序集 `TargetFrameworkAttribute` + **每宿主常量表兜底**（宿主带什么 TFM 就镜像什么平台版本）。收益：镜像精确、无外部探测；成本：需按宿主建档解析 TFM 特性里的平台版本字符串。
   - 备选 B：运行时探测 SDK/环境（`dotnet --info`/环境变量）。收益：免建表；成本：spawn/解析开销 + 与「实际跑的宿主 TFM」可能不一致（探测到的是机器默认 SDK，非宿主 TFM）。
   - 备选 C：固定最小版本（如 `-windows7.0`）+ 用户 override 注释。收益：最简单；成本：平台版本低于宿主实际可用 API 面，特性门控可能过严。
   - 建议：A。证据锚：vbi 多目标 TFM 见 `vbi.vbproj:7`；宿主运行时 TFM 由自身 `TargetFrameworkAttribute` 可读（该读取路径待 spike 实证，预测性待定）。

## 证据来源与证据等级

### 源码（产品自身编译器树 / 本仓 Scripting，证据等级=已检查）

- `#R` 语法面：`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472`（`ParseReferenceDirective`：值取 `StringLiteralToken`，仅 Script 合法）。
- resolver 唯一咽喉与注入 null：`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:38`（`PackageResolver` 字段）、`:51-63`（`CreateCurrentPlatformResolver`，null 在 `:59`）、`:144-154`（`ResolveReference` 的 nuget 分支：命中且非 null 才解析）。
- 空转抽象缝：`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:13-51`（抽象类、`nuget:` 前缀 + 斜杠拆分、`ResolveNuGetPackage` 抽象）；本仓 grep 全树**无任何 `Inherits NuGetPackageResolver` / 子类**。
- 脚本/编译共用工厂：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:161-180`（`GetScriptOptions` 造 resolver 并挂 `ScriptOptions.MetadataResolver`，:165/:179）、`:190-200`（`GetMetadataReferenceResolver`）；`Interactive\vbi\Vbi.Compile.vb:165-167`（编译路径覆写复用同一工厂）。
- 运行时加载：`Scripting\Core\ScriptBuilder.cs:142-151`（编译绑定引用 `RegisterDependency` 注册）；`Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs:168`（`RegisterDependency` 签名）、`:264-345`（依赖解析：先自身目录再注册表）。
- vbi 宿主多目标：`Interactive\vbi\vbi.vbproj:7`（`net10.0-windows;net10.0;net48`）、`:55-59`（net48 镜像需 `Microsoft.NETFramework.ReferenceAssemblies`）。
- 相关旧提案原文：`InternalDevDocs\proposals\proposal-vbscript-lsp.md:121`（「`#R nuget:` 已实现、LSP 零新增」）；`InternalDevDocs\proposals\proposal-distribute-compiler-nuget-package-and-dotnet-tool.md:112`（`#R "nuget: Package, Version"` + 「复用已有实现」）。
- 本仓样本 `.vbx` 现状：`Samples\WpfCpuCoreInformation.vbx:1-3`（仅本机 dll 引用）。

### 实证 / 生态对照（dotnet/interactive 仓库内已读 / 调查期线上取证）

- .NET Interactive 逗号解析（仓库内已读，已检查）：`dotnet/interactive（git remote origin）仓库：src\Microsoft.DotNet.Interactive\PackageReference.cs:26-61`（前缀必须小写 `nuget:`、按首个逗号拆两段、版本可省、`*`/`*-*` 视为合法 spec 在 `:63`）。
- .NET Interactive kernel 拦截 `#r`（仓库内已读，已检查）：`dotnet/interactive（git remote origin）仓库：src\Microsoft.DotNet.Interactive.PackageManagement\KernelExtensions.cs:36-61`（`#r` 指令参数进 `PackageReference`）、`:88-104`（`#!nuget-restore` 隐藏指令）。
- .NET Interactive 还原路径（仓库内已读，已检查）：`dotnet/interactive（git remote origin）仓库：src\Microsoft.DotNet.Interactive.PackageManagement\PackageRestoreContext.cs:20`（`restoreTfm = "net10.0"` 硬编码）、`:128-139`（`r`/`i` 行拼装 `Include=…, Version=…`）、`:225-245`（经 `FSharp.Compiler.DependencyManager` 的 `DependencyProvider` 还原，executionTfm 写死 net10.0）。
- 上游 Scripting 弃用/生态路线（调查期线上取证，已检查；本机不可复验 issue 原文）：dotnet/interactive#4163、dotnet/sdk#49222、dotnet/roslyn#17666、dotnet/sdk#55716（VB "later"）。上游 `NuGetPackageResolver` 2015-10-04 引入（git log 9005cb34cf1，前期线上取证；本 fork 文件历史见提交 a5e5286「Add source code of Microsoft.CodeAnalysis.Scripting (VS 17.6)」，非上游历史）。
- NuGet 7.9 no-op restore 校验包在盘（调查期 chrome 取证，已检查）：NuGet.Client 7.9.0.83 `RestoreCommand`/`NoOpRestoreUtilities`（`VerifyRestoreOutput`/`Sha512Exists`）+ learn.microsoft.com/nuget 官方文档——本会话未复验，等级=已检查（外部，非本仓）。

### 预测性判断（证据等级=待定）

- `dotnet restore` spawn 的延迟量级（百毫秒级）与冷首网络时长——未实测（U2）。
- .NET Interactive 缺省/空版本在 `FSharp.Compiler.DependencyManager` nuget provider 的最终行为——外部实现，需 meeting 前补核（U4-C）。
- 宿主 `TargetFrameworkAttribute` 读取平台版本字符串的解析路径——spike 前未实证（U8）。
- 原生资产探测根（native probing）的具体方案——spike 未做（U5）。
- 「fork Scripting 层 diverg 冲突概率低」为推测，依据是上游脚本边缘化的生态事实（Motivation），非本仓可证。
