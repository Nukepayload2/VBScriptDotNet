# 概要设计：`.vbx` / 交互窗口 NuGet 包引用（vbi-nuget-reference）

> 状态：概要设计。依据链：`../../meetings/meeting-vbi-nuget-reference.md`（RESOLUTION 1–9，唯一权威；U9 spike 已过 → R4 定稿）→ `../../proposals/proposal-vbi-nuget-reference.md`（正文被 RESOLUTION supersede 处见 `design-detailed.md` §I）→ `../../decisions.md`（M5 / D4）→ `README.md`（Vortex 拆分 + U9 记录）。
> 本设计画全局架构图景与数据流，为 `design-detailed.md` A–I 提供落点与边界；不展开到代码级。源码事实以任务 `README.md` + `design-detailed.md` 锚点为准，引用以仓库相对 `文件:行号` 给出。

## 1. 背景与目标

**目标（一句话）**：让 `.vbx` 脚本与 REPL 用 `#R "nuget:包名, 版本"` 从 NuGet 拉包——宿主在两提交之间用 `dotnet restore` 还原到内容寻址缓存，编译期单条 `#R` 展开成 N 个程序集引用（源内自含闭包），运行时注册托管资产并在 net10 上提供 native 探测；无 nuget 引用全链路零行为变化。

**现状缺口（实证）**：

1. `.vbx` 引用面只覆盖本机 dll 与运行时平台程序集（`Samples\WpfCpuCoreInformation.vbx:1-3`），无包生态通道。
2. `NuGetPackageResolver`（`Scripting\Core\Hosting\Resolvers\NuGetPackageResolver.cs:13-51`）是上游 2015 年遗留的**空转抽象缝**：fork 全树无子类、`ResolveNuGetPackage` 无实现；`RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver` 硬编码 `packageResolver: null`（`:59`）。
3. 共享编译器 Core 的**单解析闸门**：`CommonReferenceManager.Resolution.cs` 原 `ResolveReferenceDirective` 对单条 `#R` 解析结果 >1 直接 `NotSupportedException`（`// TODO: implement`）——带传递依赖或 `lib/<tfm>/` 多 dll 的包在编译期即崩，不是可读诊断。
4. 两旧提案文档断言「`#R nuget:` 已实现」与代码不符（`proposal-vbscript-lsp.md:121`、`proposal-distribute…md:112`）。

**根因定性（RESOLUTION R1）**：NuGet 引用语义是 `#R` 字符串操作数的**宿主解释**，是**宿主/工具链契约**、不是 VB 语言特性——语言层「扩展表面区高门槛」不适用，VB 用户侧一致性（大小写不敏感、不静默失败、不引第二种做法、不破坏无 nuget 既有脚本）适用。归 `decisions.md` **M5**（脚本 + 现代 .NET 双模路线的工具链对齐）。

**状态**：R4 经 U9 spike 实证定稿（非回退态）；本任务为实现计划。D4 不直接命中 P1 两档；归属已裁决 = **不入 p1–p4 语法档、独立跟踪**（见 `README.md`「归属与调度」节）。

## 2. RESOLUTION 吸收映射表（概要）

| 决议 | 内容 | 落在 |
|---|---|---|
| R1 | 定性：宿主/工具链契约；进产品 spec 不进 `vblang\spec\`；M5；D4 不直接命中 P1 | §1、`README.md`「归属与调度」 |
| R2 | 引擎 C1；镜像 = 运行宿主 TFM；SDK 门控；无 nuget 零行为 + 回归测试 | §3（引擎）、§4（缓存）；`design-detailed.md` §E/§H |
| R3 | 语法逗号 + `OrdinalIgnoreCase` 前缀；近失配显式诊断；共享层标注有意偏离 | §3（语法）；`design-detailed.md` §B/§D |
| R4 | 接线 = 扩展共享 Core 单 `#R`→N（补上游 TODO）；不 alter 旧 API；N 位置相同；merge 账本新增类别 | §3（接线）、§5；`design-detailed.md` §A |
| R5 | U9 spike 硬闸门——**已通过**；通过 → R4 定稿；不过 → 回退宿主拦截（只许干净剥行保行号 + 全量注入） | `README.md` §U9；§5 |
| R6 | 诊断锚定契约：可读诊断全在宿主预扫描；resolver 无诊断出参；Core-N 增量收益 = metadata 子类诊断锚 `#R` 行 | §5；`design-detailed.md` §D |
| R7 | U1-B：引擎/会话落宿主；共享 Core 留 seam + 注入位（IVT 锚 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`）；U1-A 否决 | §4；`design-detailed.md` §C |
| R8 | U2-A 跨进程总调；U3-B 浮动版本纯 dotnet 语义；U4-A 版本必填；U5 native 分层；U6-A 仅脚本/REPL；U7-A 勘误指针；U8-A 宿主 TFM 镜像 | §4/§5；`design-detailed.md` §D/§E/§F/§G/§I |
| R9 | Drawback 降级：fork 的 C# scripting 同文件生效 → 潜在 interop 注记 | `design-detailed.md` §B |

## 3. 总体架构（五层图景）

```
                .vbx 脚本 / REPL 提交（#R "nuget:包名, 版本" 与 #R "path" 混排）
                                  │
        ┌─────────────────────────▼──────────────────────────┐
        │ L5 宿主驱动环（宿主 = VB 脚本宿主层，U1-B）            │
        │  预扫描 #R nuget → 会话包集合 → 集合 delta 判定       │
        │  → 调 dotnet restore（C1 引擎）→ 写会话缓存 →        │
        │   还原/NU/SDK/版本/近失配 → 锚 #R 行的领域诊断         │
        └───────┬──────────────────────────────┬─────────────┘
                │ resolver 已注入（L4）          │ 原生探测根清单（runtimes/<rid>/native）
        ┌───────▼──────────────────────────────▼─────────────┐
        │ L4 接线层（Scripting\Core，共享 seam + 注入位）        │
        │  NuGetPackageResolver(抽象)←具体子类(宿主, 会话缓存前端)│
        │  RuntimeMetadataReferenceResolver.ResolveReference     │
        │    └ nuget 分支: 命中且 PackageResolver≠null → 路径数组 │
        └───────┬─────────────────────────────────────────────┘
                │ ImmutableArray<PortableExecutableReference>（N 个）
        ┌───────▼─────────────────────────────────────────────┐
        │ L3 共享编译器 Core ReferenceManager（R4：单 #R→N）      │
        │  ResolveReferenceDirective 返 N；循环 push N 引用      │
        │  + N 个相同 location；map 单值 = 首项主资产             │
        │  （不 alter CSharpCompilation.GetDirectiveReference）  │
        └───────┬─────────────────────────────────────────────┘
                │ N 引用（ExplicitReferences 全量跨提交继承）
        ┌───────▼─────────────────────────────────────────────┐
        │ L2 运行时注册 / native seam（Scripting\Core）           │
        │  ScriptBuilder 注册 bound ref 路径 → InteractiveAssemblyLoader │
        │  net10: CoreAssemblyLoaderImpl LoadContext + ResolvingUnmanagedDll │
        │  net48: 无 ALC/native（宿主能力诊断导去 .NET 宿主）       │
        └──────────────────────────────────────────────────────┘
                                │
                         执行提交 / 程序集
```

**分层职责与归属**：

- **L1 引擎（C1，宿主侧）**：内容寻址临时还原工程 + `dotnet restore`（只 restore 不 build），读 `obj/project.assets.json` 的 compile/runtime 资产。镜像「当前运行的这一个 vbi 宿主」的 TFM / windows 平台版本 / RID / 解析后 SDK / 还原源 / 框架引用。临时工程按 key 写一次字节稳定 → dgspec 稳定 → NuGet no-op（E7）。**E7 术语**（定义出处 `proposal-vbi-nuget-reference.md:64`，外部取证）：现代 NuGet（7.9/SDK 10.0.4xx）no-op restore 会**校验包文件在盘**（`VerifyRestoreOutput`/`Sha512Exists`），清全局缓存后自动重下、不会静默漏检——vbi 不自担资产存在校验，把该责任留给 NuGet。
- **L2 运行时**：托管资产路径经 `ScriptBuilder.cs:142-151` 注册进 `InteractiveAssemblyLoader`；net10 native 经 loader seam（机制在 loader、目录清单策略在宿主）。
- **L3 共享编译器 Core**：单 `#R`→N 引用展开 + 位置对齐（U9 实证）。这是**本 fork 共享编译器 Core 首条本地 diverg**，按 R4 以「不 alter 旧 API + 产品 VB-only + 休眠区（仅脚本编译激活）」管理并登记 `upstream-merge.md`。
- **L4 接线层**：抽象缝 `NuGetPackageResolver` + 注入位（`CreateCurrentPlatformResolver`/`CommandLineRunner` 工厂）留在 `Scripting\Core`；具体子类 + 会话在宿主（VB 脚本宿主层）。resolver 同步当会话缓存前端（无 await 锚点，`ResolveReference` 同步契约）。
- **L5 宿主驱动环**：异步 restore 只能放宿主环（编译器绑定是同步链）；宿主预扫描持 `#R` location → 领域诊断锚定（R6）。共享 `CommandLineRunner`（fork 已有本地改动）加**可空协调 seam**让宿主环在两提交间介入，默认 null 零行为变化。

## 4. 数据流（两场景）

### 场景 A：文件脚本 `vbi app.vbx`

1. `Vbi.vb:52-73` → `VisualBasicScript.RunInteractiveAsync`（`VisualBasicScript.vb:150-170`）建 `VisualBasicInteractiveCompiler` + `CommandLineRunner`（`CommandLineRunner.cs:31`），宿主构造会话/协调器并注入。
2. `CommandLineRunner.RunInteractiveAsync`（`:59`）→ `RunInteractiveCoreAsync`（`:80`）读码（`:118-128`）→ **宿主协调器预扫描**（读码后、编译前）：发现 `#R "nuget:…"` → 算包集合 → 与上次 exit-0 集比较。
3. 集合有 delta → `dotnet restore`（临时工程 key 命中缓存则 NuGet no-op）；退出码 ≠ 0 → 转 NU/SDK/网络诊断锚 `#R` 行、编译中止；退出码 0 → 读 assets 写会话缓存（compile/runtime 路径 + native 根目录清单）。
4. `GetScriptOptions`（`:134`）造 resolver（携带会话）→ `RunScriptAsync`（`:157`）编译：resolver 把每条 nuget `#R` 解析成 N 引用 → 共享 Core 展开 N + N 个相同 location；`ScriptBuilder` 注册路径 → 运行。
5. 运行期：托管闭包从注册路径解析；native（若 net10 + 包带 `runtimes/<rid>/native`）经 loader native probe 根找到。

### 场景 B：REPL 交互

1. 首提交同 A 步 2-3（`RunInteractiveLoopAsync` 初脚本分支 `:247-251`）。
2. 每 REPL 提交（`:290-308`）→ 协调器预扫描该提交 → 集合 delta → restore / skip → 再 `BuildAndRunAsync`（`:312`）编译。`UpdateOptions`（`:338-358`）保留 resolver（WithRelativePathResolver 拷贝不丢 PackageResolver），会话跨提交持续。
3. 跨提交继承：提交 1 的 `#R "nuget:X"` 展开的 N 引用进 `ExplicitReferences`（`Resolution.cs:848-853`）→ 提交 2 直接可用闭包类型（U9-i 实证）。

## 5. 关键机制与边界

- **单 `#R`→N（R4）**：改共享 Core 两处；map 单值 = 首项主资产（供旧 singular API 与去重）；N 语义走 `ExplicitReferences` 新增路径全量继承；默认 resolver 全返 0/1 → 改动纯休眠。
- **诊断契约（R6）**：resolver 接口无诊断出参 → v1 可读原因全在宿主预扫描；纯 seam 空回只给通用 `ERR_MetadataFileNotFound`（inline `#R` 锚 `#R` 行 `Resolution.cs:823`；ScriptOptions 未解析路径落 `Location.None` `Script.cs:285`）。Core-N 的增量收益：还原出的坏 dll 由 resolver 返回时读失败锚 `#R` 行。
- **native 分层（U5）**：net10 sqlite 类（`runtimes/<rid>/native` 单 dll + 托管包装）v1 可用（本通道唯一 native 验收类）；WindowsAppSDK/WinUI 是宿主分发自管能力、**本通道范围外**（不经 nuget restore，脚本裸名 `#R "Microsoft.WinUI"` 走宿主 TPA）；net48 范围外（还原产物含 native 资产 → 宿主能力诊断「请在 .NET 宿主运行」，只对 sqlite 类成立）。
- **零回归**：无 nuget 引用全链路零行为 + golden 测试；N>1 对既有输入纯加性。
- **代价**：SDK 前提 + 集合变化 spawn 一次 restore + 共享编译器 Core 首条 diverg（账本管理）+ Scripting/Core 共享 seam（fork 既有面）。

## 6. 范围与边界重申

- 不做：编译模式（`.vb`/`/r:nuget:`，U6-A 导去工程）；WindowsAppSDK/WinUI（宿主分发自管，不经 nuget 通道）；net48 native；U4-C 生态对齐；源内单主资产 seam 半吊子形态。
- 保持：`CSharpCompilation.GetDirectiveReference` 原样；无 nuget `.vbx` 逐字节不变；单结果 resolver 全量不回归。
- 详细设计见 `design-detailed.md`（A–I，每条含 `文件:行号` + 改动形状 + 裁决规则 + pass 条件）。
