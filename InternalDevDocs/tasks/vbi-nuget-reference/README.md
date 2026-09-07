# 任务：`.vbx` / 交互窗口 NuGet 包引用（`#R "nuget:包名[, 版本]"`）——任务计划

本文件夹是 `proposal-vbi-nuget-reference` 的任务计划存储（Vortex 代办拆分 + 概要/详细设计）。本任务是「让 `.vbx` 脚本与 REPL 用 `#R "nuget:…"` 从 NuGet 拉包」的**实现计划**：引擎走 dotnet CLI 临时工程还原（C1）、语法 fork `TryParsePackageReference` 为逗号 + 大小写不敏感、接线以**共享 Core 单 `#R`→N 引用**（R4，U9 spike 已实证定稿）为架构基座，宿主保留 restore 驱动 + 会话缓存 + 领域诊断映射。

- **一句话**：给 `.vbx`/REPL 补一条从 NuGet 拉包的通道——`#R "nuget:包名, 版本"` 经宿主在两提交之间用 `dotnet restore` 还原到内容寻址缓存，编译期由 resolver 把单条 `#R` 展开成 N 个程序集引用（源内自含闭包），运行时注册托管资产 + net10 native 探测 seam；无 nuget 引用全链路零行为变化。
- **依据链**（唯一权威 = 会议 RESOLUTION 1–9）：
  `../../proposals/proposal-vbi-nuget-reference.md`（Active/Proposed；正文多处被 RESOLUTION supersede，见 `design-detailed.md` §I）→ `../../meetings/meeting-vbi-nuget-reference.md`（2026-09-06，RESOLUTION 1–9 + 后续工作项；**U9 spike 实证通过 → 接线 R4 定稿、非回退态**）→ `../../decisions.md`（M5 脚本侧；D4 不直接命中 P1 两档）→ 归属与调度（本任务不入 `../p1-immediate.md`～`../p4-backlog.md` 语法档、独立跟踪，见下「归属与调度」节）→ 上游合并账本 `../../upstream-merge.md`（R4 merge 义务：新增 ReferenceManager 类别）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`，改动蓝图 A–I + 自动裁决规则 + pass 条件）、测试计划（`test-plan.md`，L1–L4 分层 + 门控 V-G2 分离）、本 README（Vortex 代办拆分表 + accepted 门 + 归属与调度）。
- **调度方式**：Vortex 涡流（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度，串行交替、不可催促。流水账：`<项目根>/tmp/vortex-logs/`。

## 范围与非范围

**范围内（v1）**：

- 共享 Core 单 `#R`→N 引用（R4/U9 实证架构）的**收口**：主资产约定文档标注、`DirectiveReferences` 语义注记、`upstream-merge.md` 新增 ReferenceManager 类别、U9 两场景改造为正式单测。
- 语法 fork：`TryParsePackageReference` 逗号 + `OrdinalIgnoreCase` 前缀 + 段 Trim + 版本可省（解析层）；共享层改动标注「本 fork 有意偏离上游」。
- 接线：宿主（VB 脚本宿主层）持具体 `NuGetPackageResolver` 子类 + 会话；Scripting Core 留注入位（`CreateCurrentPlatformResolver`/`CommandLineRunner` 工厂加可选 packageResolver，默认 null）；IVT 走既有 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`。
- 宿主驱动环 + 诊断锚定（R6）：文件脚本与 REPL 预扫描 `#R "nuget:"`、累积会话包集合、两提交间 async `dotnet restore`、还原/NU/SDK/版本/近失配错误映射为锚 `#R` 行的领域诊断。
- 会话临时工程 + 内容寻址缓存（key/写一次字节稳定/`global.json`/LRU/restore 触发策略/退出码 0 唯一有效信号/NU1101·网络清晰转译）；net48 临时工程带 `Microsoft.NETFramework.ReferenceAssemblies`（先例 `vbi.vbproj:55-59`）。
- 运行时：`ScriptBuilder.cs:142-151` 注册 N 引用（既有机制确认覆盖 N）；net10 native = Scripting loader seam（`ResolvingUnmanagedDll` + 宿主供 `runtimes/<rid>/native` 目录清单）。
- 零回归：无 nuget `.vbx` 行为逐字节不变测试 + 现有单结果 resolver 全量不回归。
- 文档勘误 U7：两旧提案相关行加勘误指针（不改冻结正文）。

**非范围（显式）**：

- **U6-A**：编译模式（`.vb` 批编译 / `/r:nuget:`）不启用——宿主驱动环只在脚本/REPL 入口启用，编译模式导去 `dotnet add + .vbproj`。即便注入点共用工厂，编译路径不注入 nuget resolver、不触发还原（显式决定，见 `design-detailed.md` §D）。
- **U5 native 分层**：net10 sqlite 类（本通道唯一 native 验收类）经 loader seam 支持；WindowsAppSDK/WinUI 属宿主分发自管、**不经 nuget 通道、本特性范围外**；net48 native 探测范围外（走宿主能力诊断，只对 sqlite 类成立）。
- **U4-C**：省略版本的生态对齐（对齐 .NET Interactive 真语义）不纳入 v1；v1 = U4-A 版本必填。
- 不做「源内单主资产 seam + 宿主注入其余」半吊子形态（R4 否决）。
- 不 alter 旧 singular public API（`CSharpCompilation.GetDirectiveReference` 原样）。

## U9 spike 实验记录融入（R4 实证依据，非回退态）

U9 spike 是 R4 的硬闸门，已在任务计划前完成并**通过**。实验物都在仓库内 `tmp/`（实施者不得删，改造完成后由实施者/验证者决定归档或清理）：

| 物 | 位置 | 内容 |
|---|---|---|
| Core 改动底稿 | `tmp/u9-change.patch` | `CommonReferenceManager.Resolution.cs` 最小改法 diff（返数组去 throw + 循环逐条 push N 引用 + N 个相同 location + map 单值 = 首项主资产），已应用于工作树（`git status` 见该文件 M） |
| 实证 harness | `tmp/u9harness/Program.cs` | 双场景：U9-i 跨提交继承（`#R "nuget:X"` 展开 [主 dll, 闭包 dll]，提交 2 源码直用闭包类型零错）；U9-ii 位置锚定（坏 dll 与 `#R "nuget:X"` 混排，metadata 诊断锚 `#R` 行） |
| 实证 fixture | `tmp/u9-fixtures/` | `Main`/`Closure` 两个 SDK 生成的 fixture 工程（`PkgMain.MainType` 引用 `PkgClosure.ClosureType`） |
| 坏 dll 阴性物 | `tmp/u9-bad/bad-fake.dll` | 非托管垃圾字节 |
| 运行日志 | `tmp/u9harness/run-final.log`（改后 PASS）、`run-original.log`（原版 `NotSupportedException`，阴性对照）、`tmp/u9-build*.log` | 改后两场景全 PASS；原版两场景都抛 `NotSupportedException: Specified method is not supported` |

**结果结论**：(i) 单 `#R` 解析 N>1 后 REPL 跨提交继承成立（N 引用进 `ExplicitReferences`/`DirectiveReferences`，提交 2 可见闭包类型）；(ii) 位置对齐可测（坏 dll 读失败 `BC31519` 锚 `#R "nuget:X"` 行，即第 0 行，非 `Location.None`）；(iii) 阴性对照抛 `NotSupportedException`，证明改动敏感（不是「本来就能跑」）。→ 残余风险中「ExplicitReferences 实证缺口」「对齐去重回归」「主资产排序稳定性」已被 spike 收敛为可测项，「共享 Core diverg」转 merge 账本义务，「`DirectiveReferences` 膨胀」转文档标注。

**进实现后如何改造成正式单测**（`design-detailed.md` §A2）：U9 harness 依赖 `tmp/u9-fixtures` 的磁盘 dll + 自建控制台工程，不符合「无副作用单测」纪律。正式测试改为 **in-memory**：两个 fixture 程序集用 `VisualBasicCompilation.Create` + `Emit` 到 `MemoryStream`，经 `MetadataReference.CreateFromStream` 交给自定义多结果 resolver；坏 dll 改为垃圾 `MemoryStream`。位置锚定场景用**两行不同位置的 `#R "nuget:…"`**（各行展开含坏资产）证明逐指令锚定，替代磁盘混排（代码路径不分指令来源，性质等价）。落点：`Scripting\VisualBasicTest\` 新文件，见 §A2 pass 条件。

## 归属与调度（本计划裁决，无待作者决策点）

**不入 `../p1-immediate.md`～`../p4-backlog.md` 任一档位**——那四档是 **ModVB 语法提案清单**（`tasks\README.md` 明示；条目均带 `proposal-*.md` ↔ `meeting-*.md` 路径），本特性是 **M5 脚本/宿主工具链契约、非 VB 语言特性、无对应 ModVB 语法提案**，写进任何语法档都是越文体。同类先例一致：`distribute-compiler-nuget-package-and-dotnet-tool`、`net472-desktop-branch`、`vbi-script-diag-mode`、`script-optimization-level` 也都不在 p1–p4，各自以 `proposals\README.md` Active 行 + `tasks\<同名>\` 文件夹独立跟踪（shebang 能进 P1 是因为它是编译器指令 = 语法面 D4①，不构成先例）。

- **登记**：`proposals\README.md` #13 已 Active（`../../proposals/proposal-vbi-nuget-reference.md`）；本 task 文件夹即实现跟踪载体。
- **调度（实施排序，由实现规划裁决）**：R4/U9 已实证定稿、计划 accepted 后即可排入实施，不依赖任何语法档位闸门。会议裁「D4 不直接命中 P1 两档、优先级由实现规划结合路线图定」在此落实为：不占语法档位、作为 M5 工具链独立推进。

## Vortex 代办拆分表（供实施者/验证者无人值守串行）

> 编号 = 功能拆分标识，**非执行顺序**；执行顺序见各条目「前置」。每项 pass 条件可自动判（实施者做完 → 验证者按 pass 条件核对 → 打回/通过）。状态初始 `todo`。所有实现改动以 `design-detailed.md` A–I 为唯一底稿，不得偏离；涉及共享层/共享 Core 的改动必须同步登记 `../../upstream-merge.md`。
>
> **执行范围**：本表 = **无人值守串行**项（实施者/验证者交替，pass 全自动判）。含真实 `dotnet restore`、`%LOCALAPPDATA%` 写入与跨平台（`.so`/`.dylib`）的验收（V-G2 sqlite 端到端）**不在本表内**，见下方「门控集成验收」节。
>
> **测试执行规约**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**禁用 `dotnet test`**（EXIT 0 但静默不跑）；验证者先 `dotnet build`，再直跑 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或加 `-method <FQN>`（单测）。见 `design-detailed.md` §0 + memory `vb-scripting-test-runner`。

| # | 功能（design-detailed 章节） | pass 条件（全部满足才算过） | 前置 | 状态 |
|---|---|---|---|---|
| V-A1 | Core-N 收口：工作树 diff 与 `tmp/u9-change.patch` 形状核对 + 主资产约定文档标注 + `DirectiveReferences`/`ReferenceDirectiveMap` 语义注记 + `../../upstream-merge.md` 新增 ReferenceManager 类别（§A1） | diff 恰为 patch 形状（只动 `CommonReferenceManager.Resolution.cs` 两处，无顺手改动）；`:841-842` 与 `ResolveReferenceDirective` doc 有主资产约定标注（单值 map = 主资产、N 语义经 `ExplicitReferences` 走新增路径）；`Compilation.cs:738`/`State.cs:241`/`CSharpCompilation.cs:1316`/`PublicAPI.Shipped.txt` 零 diff（语义注记只落 `Resolution.cs` 改动面 + 账本，不落零改文件）；账本新增类别含文件锚点 + diverg 说明 + 合并前评估义务 | 无 | done |
| V-A2 | Core-N 正式单测：U9 两场景 in-memory 改造（§A2） | 新增测试文件含两测试：(1) 跨提交继承——`#R "nuget:X"` 展开 [主, 闭包]，`ContinueWith` 提交 2 源码直用闭包类型零编译错；(2) 位置锚定——两行不同位置 `#R "nuget:…"` 各含坏资产，metadata 诊断各锚自己 `#R` 行非 `Location.None`；纯内存（无文件写/无进程 spawn/无网络）；原版行为对照由 V-Z 全量跑兜底 | V-A1 | done |
| V-B | 语法 fork `TryParsePackageReference`（§B） | 解析改逗号 + `OrdinalIgnoreCase` 前缀 + 整体/段 Trim + 版本可省（parse 层）+ 近失配返 false；签名不变、调用点 `RuntimeMetadataReferenceResolver.cs:146` 零改动；文件 doc + 新测试显式标注「本 fork 有意偏离上游：`Ordinal`→`OrdinalIgnoreCase` + 逗号，防 merge 误回滚」；新 `NuGetPackageResolverTests.vb` 锁定：逗号拆分/大小写不敏感/各段 Trim/近失配拒绝/省略版本 | 无 | done |
| V-C | resolver 注入 seam（§C） | `CreateCurrentPlatformResolver`/`CommandLineRunner.GetMetadataReferenceResolver` 加可选 `NuGetPackageResolver`（默认 null → 现有调用零行为变化）；VB 宿主（`Scripting\VisualBasic\Hosting\`）新增具体子类 + 会话骨架并经既有 IVT（`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`）挂入；编译路径（`Vbi.Compile.vb:165-167`）不注入（U6-A） | V-B | done |
| V-D | 宿主驱动环 + 诊断锚定（§D） | `CommandLineRunner`（共享 fork）加**可空协调 seam**（默认 null → 零行为变化），在文件脚本路径（`RunInteractiveCoreAsync` 读码后编译前）与 REPL（`RunInteractiveLoopAsync` 初提交 + 每 REPL 提交编译前）调用宿主协调器预扫描/还原；还原/NU/SDK/版本/近失配/宿主能力错误按 R6 决策表映射为**锚 `#R` 行**的领域诊断；编译模式确认不触发（含 `/r:nuget:` 无还原、注释/测试锁定 U6-A）；无 nuget 指令时协调器不被调用 | V-B, V-C | done |
| V-E | 临时工程 + 缓存（§E） | key 元组按 §E 决策表实现（精确包集规范排序 + 宿主镜像元组 + 还原源 + 框架引用镜像）；目录 `%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>\`；临时工程文件写一次字节稳定；`global.json`（`rollForward: latestMajor`）只在出现 nuget 引用时写；restore 触发策略按 §E 表（同集 skip / 集合变化必调 / 跨进程总调）；唯一有效信号 = 自己那次 restore 退出码 0 + NuGet 自身 `obj/project.nuget.cache`；NU1101/网络 → §E 转译文案；net48 临时工程带 `Microsoft.NETFramework.ReferenceAssemblies`（先例 `vbi.vbproj:55-59`）；LRU 清理按 §E 常量；key 不含脚本源码 | V-D | done |
| V-G | 运行时注册核对 + net10 native seam（§G） | `ScriptBuilder.cs:142-151` 对 N 引用注册路径核对（现机制已按 bound ref 逐个注册，确认覆盖 N，若有缺口补）；`CoreAssemblyLoaderImpl` LoadContext 加 unmanaged seam（`ResolvingUnmanagedDll`/`LoadUnmanagedDll` override + 宿主供 native 探测根目录清单，内建机制在 loader、目录策略在宿主）；空 native 根集 = 现状零回归；缺 `runtimes/<rid>/native` 资产 → 带已搜目录的清晰诊断；net48：还原产物含 native 资产 → 经 R6 预扫描报宿主能力诊断（非 restore 错误）；native seam 属 net10-only、语言中立、登记 upstream-merge | V-D, V-E | done |
| V-H1 | 零回归一：无 nuget `.vbx` 行为逐字节不变（§H） | 新增测试：无 nuget `.vbx`（含普通 `#R` 本机 dll + 打印）经内存宿主跑，stdout 与 golden 逐字节相等；无 nuget 提交：seam 被咨询但无 restore/SDK 路径尝试（stub 仅在实际请求 nuget 时抛）；无 nuget 时全链路零报错 | V-D 完成（coordinator 可注入） | done |
| V-H2 | 零回归二：现有单结果 resolver 全量不回归（§H） | `Scripting\VisualBasicTest` 全量（`CommandLineRunnerTests`/`InteractiveSessionTests`/`ScriptTests`/…）绿；既有引用解析相关测试（含 N>1 不抛新增断言）无回归 | V-A2 起各阶段 | done |
| V-I | 文档勘误 U7 + supersede 表核对 + §F 产品文档明示（§I） | `proposal-vbscript-lsp.md:121`、`proposal-distribute…md:112` 相关行加勘误指针（冻结正文不动），指向本任务 + meeting RESOLUTION；`design-detailed.md` §I supersede 表与提案正文逐项核对一致；§F「与官方 `@`/`#:` 语法差异」产品文档明示文本随勘误同批产出（落 V-G2 的 `.vbx` sample 注释 + 提案勘误指针批注内）；两份勘误指针 + supersede 表 + §F 明示文本由验证者复核 | V-B..V-G 代码面收口后 | done |
| V-Z | 全量收口 gate（§Z） | `Compilers\Core\Portable`（net10 至少）与 `Scripting` 构建绿；V-A2/V-H1/V-H2 + 新增测试全绿（`Scripting\VisualBasicTest` 直跑 `-automated`，见上「测试执行规约」）；`upstream-merge.md` 账本含 ReferenceManager 类别 + 共享层新 seam（若加了 public 面则含 PublicAPI 增量核对）；U9 改造后 `tmp/u9-*` 处置决定执行；门控集成验收（V-G2）已转作者/QA 清单、V-Z 不阻塞其上；无待作者决策点（归属与调度已裁决，见「归属与调度」节）；无遗留 Unresolved（对照 meeting 后续工作项清单逐项） | V-A…V-I 全过 | done |

## 门控集成验收（非无人值守串行；V-Z 收口后由作者/QA 执行）

> 下列项需真实 `dotnet restore`（网络 + `%LOCALAPPDATA%` 写）或跨平台物理机，**不可自动判**，故不在上方 Vortex 表内。每项给出触发条件、执行者、通过判定。执行者做完后回写 `tmp/vortex-logs/` 并浮出结果。

| # | 内容（design-detailed 章节） | 触发 | 执行者 | 通过判定 |
|---|---|---|---|---|
| V-G2 | sqlite 端到端验收（§G3；兼作 §F 的 `.vbx` demo：逗号 + pin 版本）+ 托管传递依赖真实还原 | V-Z 收口后 | 作者/QA | net10 宿主 `.vbx` 用 `#R "nuget:Microsoft.Data.Sqlite, 8.0.x"` 建内存库完成一次 `SELECT` 输出正确；**另引一带托管传递依赖的包（非单 dll 无依赖，见 test-plan G2-5）直用主包 + 依赖类型均零错**；跨平台 dll 命名（`.dll`/`.so`/`.dylib`）/RID 回退/缺目录诊断按 §G2.3-4 清单过；net48 宿主跑同脚本 → 收宿主能力诊断而非崩溃；sample 注释含 §F「与官方 `#:`/`@` 语法差异」产品文档明示文本（V-I 随勘误同批产出） |

> **V-G2 执行须知（V-Z 收口时登记）**：sqlite 阳性用例跑前，须在宿主运行时把会话 `NuGetPackageSession.NativeRootDirectories`（restore 成功后由协调器写入）经 `InteractiveAssemblyLoader.AddNativeProbeRoot` 逐目录推入 loader（loader seam 已落地：`AssemblyLoaderImpl.cs` internal virtual + `CoreAssemblyLoaderImpl.LoadUnmanagedDll` net10 override + `NativeLibraryProbe` 纯候选表）。**现宿主装配侧（`VisualBasicScript.vb` `RunInteractiveAsync`）尚无该调用点**——握手留 V-G2：G2-1 的「native `e_sqlite3` 经 loader 探测根找到」与 G2-2 阴性对照（不带根集 → `DllNotFoundException`）都以正确执行此推入为前提。实现形态待作者/QA 定（可在脚本运行前把 loader 实例经 seam 传给协调器，或由 vbi 宿主在创建会话后调用）。`NuGetPackageSession`/协调器已是 `Friend`、`AddNativeProbeRoot` 为 internal（IVT 可达），无需新增 public 面。

## Accepted 门（计划被批准进入实施的条件）

验证者（或作者）核对以下全部成立，计划才算 accepted，之后才允许按上表无人值守串行实施：

1. **决议吸收完整**：RESOLUTION 1–9 + 后续工作项每项在本计划有落点或显式非范围（U6-A/U4-C/WindowsAppSDK 宿主自管范围外/net48 范围外各有明示）；无越权承诺（不 alter `GetDirectiveReference`、不引第二种做法、不进编译模式）。
2. **源码事实真实**：`design-detailed.md` A–I 每条 `文件:行号` 经验证者 Read/Grep 复核与源码一致（含当前已应用的 Core-N 工作树状态）。
3. **U9 融入到位**：README §U9 与 `design-detailed.md` §A2 的结论/位置/改造路径与 `tmp/u9-*` 实验记录一致。
4. **决策点收敛**：无待作者决策点（归属与调度已裁决：不入 p1–p4 语法档、独立跟踪，见「归属与调度」节）。
5. **pass 条件可判**：Vortex 表每项 pass 条件为客观可自动判（实施/验证无需问人）；无「实现时再定」的悬空设计。
6. **测试计划齐备**：`test-plan.md`（第 4 件交付物）存在，L1–L4 分层矩阵与 Vortex 各 V-* 测试面（V-A2/V-B/§D 诊断/V-H1/V-H2）一一对应；无副作用单测与门控集成（V-G2）边界与 `design-detailed.md` §0/§G3 一致。
7. **文档纪律**：正文简体中文；引用仓库相对路径；无本机绝对路径/用户名/机器特定状态；无过程日志文体（WhiteSand 契约）。

## 关键设计决策摘要（源自 RESOLUTION，实现期不得翻转）

1. **接线（R4/U9）**：共享 Core 单 `#R`→N，`boundReferences[0]` = 主资产；不 alter 旧 API；N 语义走 `ExplicitReferences`（`:845-853`）全量跨提交继承；`upstream-merge.md` 登记 ReferenceManager 类别。
2. **引擎（R2）**：C1 dotnet CLI 临时工程 restore，读 `project.assets.json` compile/runtime 资产；镜像 = 运行宿主 TFM + windows 平台版本 + RID + 解析后 SDK + 还原源 + 框架引用；SDK 门控只在出现 nuget 引用时查。
3. **语法（R3）**：逗号 + 大小写不敏感前缀；近失配显式诊断（宿主预扫描）；共享层标注有意偏离上游。
4. **归属（R7/U1-B）**：引擎/会话落宿主（VB 脚本宿主层），共享 Core 留 seam + 注入位；U1-A 否决。
5. **诊断（R6）**：v1 可读诊断全在宿主预扫描；纯 seam 只给通用锚定；Core-N 增量收益 = metadata 子类诊断锚 `#R` 行。
6. **各 U（R8）**：U2-A（跨进程总调）、U3-B（浮动版本保留请求 spec 纯 dotnet 语义）、U4-A（v1 版本必填）、U5（net10 native loader seam、sqlite 类唯一验收、WindowsAppSDK/WinUI 宿主自管范围外 + 误写范围外诊断、net48 范围外 + 能力诊断限 sqlite 类）、U6-A（仅脚本/REPL）、U7-A（勘误指针）、U8-A（宿主 TFM 镜像）。
