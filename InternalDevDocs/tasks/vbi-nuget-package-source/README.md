# 任务：让脚本目录链上的 `NuGet.config` 对 `#R "nuget:"` 生效（vbi-nuget-package-source）设计任务

本文件夹是本任务的设计任务存储（Vortex 代办列表 + 设计产物）。本任务**无独立提案**：输入是**用户裁决（2026-10-11）**与既有任务 `../vbi-nuget-reference/` 的设计，其中 `design-detailed.md:230` 已把"源指纹"登记为留待未来的未接线项。

- **依据链**：用户裁决 2026-10-11（档位 = **A + C**：只修"脚本目录链生效"这一缺陷 + 源指纹接线；方案 = **镜像目录层级**；私有源凭据**必须**支持，故不采用 `<RestoreSources>` 注入）→ `../vbi-nuget-reference/design-detailed.md`（§E1 缓存键 / §E2 缓存目录与 LRU / §E3 还原触发 / §E7 no-op 与字节稳定）→ `../vbi-nuget-reference/test-plan.md`（无副作用纪律与验收 gate 沿用）。
- **交付物**：概要设计、详细设计、测试计划。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 一句话目标

把**脚本所在目录链上的 `NuGet.config`** 变成还原的真实输入：`#R "nuget:Name, Ver"` 按脚本目录的配置链取源（含私有源与 `packageSourceCredentials`），并把该配置集合接进还原缓存键，使"同一 key ⟺ 同一组还原输入"成立。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`） |
| F4 | 设计验证 + 一致性 | 三份交付物交叉一致、源码锚点真实、无副作用纪律 | **done**（复核项 R1–R9 见「验证记录」；缺陷 G1–G4 已固化为 `design-detailed.md` §九 约束） |
| F5 | 实现（配置收集 + 镜像落盘 + key 接线 + 空路径规则 + 诊断 + 文档） | 见「实现阶段（F5+）」 | **done**（`NuGetSourceConfig.vb` 新增；`NuGetRestoreRunner` / `NuGetRestoreCoordinator` / `NuGetRestoreDiagnostics` / `NuGetRestoreCache` 改动；两个 TFM `netstandard2.0;net10.0` 零警告零错误） |
| F6 | 测试实现 + 修复轮 | 见「实现阶段（F5+）」 | **done**（`NuGetSourceConfigTests.vb` 25 个用例；**全量 `Scripting\VisualBasicTest` 811 通过 / 0 失败 / 0 错误**，日志 `tmp/vortex-logs/f6-full-suite.txt`、`f7-full-suite.txt`） |
| F7 | 集成验证 | 全量构建、`Scripting\VisualBasicTest` 全绿、手工 G 清单 | **部分 done**：两个 TFM 零警告零错误；全量 **811 通过 / 0 失败**；手工 G 的 **G1/G2/无配置对照 已真机跑通**（自建私有源 + `<clear/>`，dgspec 证明源真的换了），G3 凭据 / G4 两层嵌套 / `dgSpecHash` 逐字对照 未跑 |

> 真机运行还抓到一个静态复核漏掉的缺陷（约束 **G5**：工程父目录未创建 ⇒ 被兜底 Catch 吞成「找不到 SDK」），已修复并加回归用例 L4-10。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> **本节是实现前的基线（2026-10-11，改动前的形状）**。设计目标形状见 `design-detailed.md` §一/§二/§三 与 §十（改动文件清单），其中带「现状」列的表与全部行号锚点均以**改动前**工作树为基准。凡本节点到"生产恒空串""无镜像"的地方，都是**改动前**的状态。

> 标注 **[已运行]** 者为 2026-10-11 本机实测；标注 **[已核实]** 者为源码逐行读取（行号以当日工作树 `2f75d52`+ 为基准，实现前复核）。

### 还原链路（`Scripting\VisualBasic\Hosting\`）

- **[已核实]** `NuGetRestoreRunner.vb:199`：`RunDotNet("restore """ & request.ProjectFilePath & """ --nologo")` —— **没有 `--source` / `--configfile` / `--packages`**，也没有设置工作目录（继承宿主 CWD，但见下条：CWD 不影响配置链）。
- **[已核实]** `NuGetRestoreRunner.vb:229-233` `WriteOnce(path, content)`：文件存在即不重写。这是 §E7「字节稳定 ⇒ NuGet no-op」的前提。
- **[已核实]** `NuGetProjectGenerator.GenerateProjectXml`（`:30-84`）：只产出 `OutputType` / `TargetFramework` / `RuntimeIdentifier` / `RestoreProjectStyle` / `NuGetAudit` + `PackageReference`（net48 时加 ReferenceAssemblies）+ `FrameworkReference`。**没有任何源/配置内容**（无 `<RestoreSources>` / `<RestoreConfigFile>` / `<RestorePackagesPath>`）。
- **[已核实]** `NuGetRestoreCache.ComputeKey`（`:39-45`）签名含 `sourceFingerprint As String`，并在描述符中参与 SHA-256（`:59`）。
- **[已核实]** `NuGetRestoreCoordinator.vb:88` `_sourceFingerprint`；`:103` ctor 可选参 `sourceFingerprint As String = ""`；`:111` 赋值；`:374-379` `ComputeKey(...)` 把 `_sourceFingerprint` 传进去；`:381` `GetCacheDirectory(cacheRoot, key)`；`:390` `New RestoreRequest(cacheDirectory, projectXml, globalJson)`。
- **[已核实]** 生产装配 `Scripting\VisualBasic\VisualBasicScript.vb:172-179` 建 `NuGetRestoreCoordinator(session, loader:=assemblyLoader)` —— **不传 `sourceFingerprint`** ⇒ 恒空串（与 `vbi-nuget-reference/design-detailed.md:230` 记载一致）。
- **[已核实]** seam 已带脚本路径：`Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs:32` `PrepareCompilationAsync(SourceText code, string? filePath, ScriptOptions? options, CancellationToken ct)`；调用点 `CommandLineRunner.cs:234`（文件脚本路径 = 主脚本；REPL 提交为 `""`）。

### 本机真实缓存（`%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>`）

- **[已运行]** 5 个 key（最新 2026-10-07）。key 目录内容 = `global.json` + `project.vbproj` + `obj\{project.assets.json, project.nuget.cache, project.vbproj.nuget.dgspec.json, project.vbproj.nuget.g.props, project.vbproj.nuget.g.targets}`。
- **[已运行]** 生成的 `project.vbproj` 原文（10-07 那份）只含 `RuntimeIdentifier=win-x64` + `<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.1" />`，无任何源配置。
- **[已运行]** 该次还原的 `dgspec` 记录的**实际源**：`C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\`、`%USERPROFILE%\Downloads\Nuget`、`https://api.nuget.org/v3/index.json` —— 全部来自**用户/机器级**配置；`packageFolders` = `%USERPROFILE%\.nuget\packages\`。
- **[已运行]** `project.nuget.cache` 含 `"dgSpecHash": "ayl6BX1ZZko="`、`"success": true`；dgspec **含 `"sources"` 段** ⇒ 有效源集合变化会改变 dgSpecHash，从而让 NuGet **不做 no-op**（环境级源变化可自愈）。

### 配置链的根（本次实测的核心事实）

- **[已运行]** 仓库外干净复现（`%TEMP%\dshtest\proj`）：把 `NuGet.config`（`<clear/>` + 一个假源 `https://project-local.invalid/v3/index.json`）放在**工程文件同目录**，`dotnet restore <工程>`（CWD 在另一个目录）后 dgspec **只记该假源**；删掉该文件后再还原，dgspec 回到用户/机器三条。
- **⇒ 结论：配置链的根是「工程文件所在目录」，不是 CWD。** 而 vbi 的工程在 `%LOCALAPPDATA%` 下 ⇒ **放在 `.vbx` 旁边或仓库根的 `NuGet.config` 对 `#R "nuget:"` 零作用**。
- **[已运行]** 反例校正记录：我最初用"脚本目录（在仓库内）做 CWD"实验，看到的是**仓库根 `NuGet.config`** 的 Arcade 源列表 —— 那不是 CWD 生效，而是**探针工程恰好也在仓库子树内**，链从工程目录向上命中了仓库根的配置。仓库外复现（上一条）才是判据。

### `dotnet nuget` 的可用面（[已运行]）

- `dotnet nuget list source` **无 `--working-directory`**（只看 CWD）。
- `--format` **只接受 `detailed` / `short`**，**没有 json** ⇒ 若走"让 dotnet 算有效源再写进工程"的路线，需解析文本输出（本设计因此**不采用**该路线）。

## 关键设计决策（用户裁决 2026-10-11 + 本设计推导）

1. **档位 A + C（用户裁决）**：本轮只做"脚本目录链生效"这一缺陷修复与源指纹接线；**不加命令行开关**（`/packagesource:` / `/configfile:` / `/packages:` 留后续独立任务）。
2. **镜像目录层级，不自己实现配置合并**：`<clear/>`、`disabledPackageSources`、`packageSourceCredentials`、环境变量展开、机器/用户级合并全部交给 NuGet；我们只把目录级 `NuGet.config` **原样搬到 key 目录内的相同相对层级**，并让生成的工程落在镜像深度。这是"私有源凭据必须支持"（用户裁决）的直接后果——`<RestoreSources>` 注入会丢凭据。
3. **源集合必须进 key**：`sourceFingerprint` 从恒空串改为"镜像配置文件集合的稳定指纹"。理由是不变量"同一 key ⟺ 同一组还原输入"：`WriteOnce` 让 key 目录内的文件**永不重写**，若源不进 key，换源后旧配置会静默生效。
4. **不做配置合并、不改环境级行为**：用户/机器级配置仍由 NuGet 自然加载；本任务不接管 `NUGET_PACKAGES` 全局包目录。
5. **空路径（纯 REPL 输入）v1 沿用改动前的行为**：`filePath` 为空 ⇒ 不镜像、指纹为空。`#load` 链上文件的所在目录**不**参与（v1 边界，登记为 Unresolved）。
6. **不动 E7 的 no-op/字节稳定前提**：靠"源进 key"而非"内容变了就重写文件"来保证正确性。
7. **旧 key 目录按孤儿处理**：布局变化后由既有 LRU（`StaleAfter = 30d`、`MaxCacheDirectories = 256`）回收，不做主动迁移。

## F1 验收条件（概要设计 pass 标准）

- 给出四层触点：**配置收集（纯函数）** → **镜像落盘（`RestoreRequest` / `DotNetRestoreRunner`）** → **缓存键接线（`NuGetRestoreCoordinator`）** → **空路径规则 + 诊断 + 文档**。
- 含行为对照表（无目录级配置 / 脚本旁有配置 / 配置变更后二次运行 / 纯 REPL 输入 / 私有源带凭据）的对象、源集合、key、实际效果。
- 明确"配置链根 = 工程目录"这一前置事实与镜像方案为何成立。
- 明确 v1 边界（不加开关、不合并配置、`#load` 链不参与、不改全局包目录）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号 + 改动形状**（C1 收集纯函数 + C2 `RestoreRequest`/runner 镜像落盘 + C3 key 接线 + C4 空路径规则 + C5 诊断 + C6 迁移/回收 + C7 文档 + C8 测试面），可被实施者直接照做。
- 明确 key 目录内布局（镜像根规则、工程落点、相对层级映射）与旧布局的关系。
- 明确指纹的定义（输入、排序、编码、大小写规则），并说明其对"同集不同源"的判别力。
- 无副作用测试矩阵与边界（相对/绝对脚本路径、大小写不敏感枚举、长路径、Windows 保留名）。

## F3 验收条件（测试计划 pass 标准）

- 三层矩阵：L1 纯函数（收集/镜像布局/指纹/`ComputeKey` 敏感性）；L3 API（`RestoreRequest` 形状）；L4 宿主（假 runner，无进程：镜像文件就位、工程落点、key 随配置变化、空 `filePath` 行为）。
- 无副作用纪律：复用 `CreateIsolatedTempDirectory`；**不 spawn 进程、不网络**（真还原留 G 人工清单）。
- 验收 gate：新增用例全绿 + `Scripting\VisualBasicTest` 全量 0 失败 + 回归面（`NuGetRestoreCoordinatorTests` / `NuGetRestoreEngineTests` / `NoNuGetZeroRegressionTests` 既有用例不破）。

## 实现阶段（F5+）

- **F5 实现**：C1–C7。**done**。验收：脚本旁放 `<clear/>` + 私有源配置后，源取自该配置——**已由 F7 真机 G1 取证**（硬证据 = dgspec 的 `sources` 段只剩该配置的源；诊断文案不含源 URL，故不作判据，见 `design-detailed.md` §十一 第 6 条）；同集换源 ⇒ key 变化 ⇒ 真还原——自动面已证（`NuGetSourceConfigTests` L4-3）；无目录级配置 ⇒ 行为与改动前逐字相同——自动面已证（L1-14 用 `[已运行]` 实测复现的真实 key 钉死 + L4-1/L4-4 断言 key 叶名 = 空指纹的 key）。
- **F6 测试实现**：按 `test-plan.md` 分层，无副作用。**done**：25 个新用例全绿 + 既有 NuGet 用例不破（全量 811 通过）。
- **F7 集成**：全量构建（两个 TFM 零警告）、测试全绿（**811/0/0**）；手工 G：**G1/G2/无配置对照已真机跑通**（G3 凭据、G4 两层嵌套、`dgSpecHash` 逐字对照 未跑）；`vbi-nuget-reference/design-detailed.md:230` 状态**已更新为「已接线」**。

### F4 复核结论（实施前）摘要

**done**：发现 4 处设计缺陷（G1–G4），全部修正并已固化为与设计正文解耦的约束表 `design-detailed.md` §九「实现约束」；逐项复核记录（R1–R9）与真机验证见本文件「验证记录」。其中 G1 不修则功能整体不可用。

### 实现后真机运行追加的缺陷（F4 静态复核未覆盖）

**G5**：工程落到镜像深度后，`WriteOnce` 不会创建工程的父目录（原代码只建 key 根），异常被 `RestoreAsync` 的兜底 `Catch` 吞掉，报成 **VBI1006「找不到 SDK」**——症状与病因完全错位。已修复（`WriteProjectFiles` 先建工程目录）并加回归用例 L4-10，约束已入 `design-detailed.md` §九 G5。**教训**：请求形状测试（L3/L4-1…L4-9）证明不了"文件真的落到那个路径上"，必须有至少一条真机或真落盘的路径验证。

## 验证记录（流水账）

> 本节是过程记录；设计正文不引用本节，设计事实以 `design-detailed.md` 为准。

### F4 复核项逐条结论（R）

复核方式：重读全部源码锚点 + 真机实验 + 与本机真实缓存对照。

| # | 复核项 | 结论 | 事实去向 |
| --- | --- | --- | --- |
| R1 | 工程落到镜像深度后 `global.json` 是否仍被 SDK 找到 | **[已运行] 无回归**：`global.json` 由 `dotnet` 进程 CWD 解析，不是工程目录，故 `<key>\global.json` 在改动前后同样不生效 | `design-detailed.md` C2.1 + §十一 第 1 条 |
| R2 | `filePath` 传递形状 | 唯一调用点 `PrepareCompilationAsync:137`，且 `filePath` 在 `:124` 已在手 ⇒ 增参，不落字段 | §三 表 |
| R3 | `filePath` 是否绝对 | **[已核实] 命令行给出的已是绝对路径**；唯一例外是 stdin 脚本 `"-"`，由 `GetScriptDirectory` 兜底 | §一 规则 7 |
| R4 | 其它构造 `RestoreRequest` 的位置 | **[已运行] 生产仅 `NuGetRestoreCoordinator.vb:390`**；测试面零命中 ⇒ 无同步负担 | §十 |
| R5 | `NuGetRestoreEngineTests` 的工程生成/字节稳定断言 | **[已核实] 只吃生成文本**，不涉及 `ProjectFilePath` ⇒ 零改动 | §八 |
| R6 | 既有假 runner 是否覆盖 `RestoreRequest` 形状 | **[已核实] `LastRequest` 仅被 `ProjectXml` 断言使用** ⇒ 增参不破坏既有用例 | §八 |
| R7 | 大小写不敏感的文件名匹配在 Linux 上是否成立 | **[已核实] `File.Exists` 探测单拼写在 Linux 会漏检** ⇒ seam 改 `EnumerateFiles`，镜像统一写规范拼写 | §一 规则 1/4 |
| R8 | `Path.GetRelativePath` 可用性 | **[已核实] 不在 netstandard2.0** ⇒ 自实现词法相对路径 | §一 规则 4 |
| R9 | 镜像层级加深是否影响 LRU/长路径 | `Trim` 未改；深嵌套由 L1-10（8 层）覆盖 | `test-plan.md` |

### 真机验证运行（`[已运行]` 2026-10-11，非自动面）

用本机自建的私有源做闭环（`%TEMP%\vbi-g`）：

- 源：`dotnet pack` 产出的 `VbiSourceConfigProbe.1.0.0.nupkg` 放进 `repo\feed\`（**从未发布到 nuget.org** 的包）。
- 配置：`repo\NuGet.config` = `<clear/>` + `<add key="localprobe" value="…\repo\feed" />`。
- 脚本：`repo\app\probe.vbx` = `#R "nuget:VbiSourceConfigProbe, 1.0.0"` + `Console.WriteLine(VbiSourceConfigProbe.Probe.Ping())`。
- 运行：`vbi.exe repo\app\probe.vbx`（CWD 在别处，配置链只可能来自脚本目录）。

| 观测 | 结果 |
| --- | --- |
| **正向**（配置在，全局包缓存已清空） | 输出 `pong`，exit 0 ⇒ 包**只能**来自脚本目录配置里那个本地源 |
| **反向**（移走 `NuGet.config` + 清空全局包缓存 + 删掉无配置的旧 key 目录） | `error VBI1009: NuGet restore failed`，exit 1 ⇒ 缺了配置就**确实取不到**该包 |
| **缓存目录布局**（正向那次） | `<key>\NuGet.config`（镜像在根）+ `<key>\app\project.vbproj`（工程落在脚本目录深度）+ `<key>\app\obj\…`（`obj` 随工程） |
| **dgspec 实际源**（正向那次） | `{"…\vbi-g\repo\feed":{}}` — **只有**配置里那个本地源，`nuget.org` 被 `<clear/>` 清掉 ⇒ NuGet 真的按镜像链取源 |
| **无配置对照**（反向那次） | key 变了（空指纹 ⇒ 另一个目录）、工程回到 key 根、**无任何镜像文件**、dgspec 源回到用户/机器级三条（含 `api.nuget.org`）⇒ 无目录级配置时与改动前同形 |
| **G5 曝光** | 修复前该正向用例报 **VBI1006「找不到 SDK」**（真因是工程父目录未建）；修复后同一用例 exit 0 |

**结论**：`#R "nuget:"` 的源确实由「脚本目录链的 `NuGet.config`」决定，且不经过 nuget.org；无配置时行为与改动前一致。未覆盖：`packageSourceCredentials`、两层嵌套源、与改动前的 `dgSpecHash` 逐字对照（状态见 `test-plan.md` §七）。

### 裁决记录

- **2026-10-11**：本轮范围 = 档位 **A + C**（修「脚本目录链生效」+ 源指纹接线），方案 = 镜像目录层级；私有源凭据必须在支持范围内，故不采用 `<RestoreSources>` 注入。
- **2026-10-11**：**保留**「镜像链 + 源指纹」；不采用「透传 `--configfile`」（会替换整条配置链，丢环境级源与凭据）与「镜像每次刷新 + 去掉指纹」（丢 per-config 隔离）。代价论证与实测见 `design-detailed.md` §十二。
- **2026-10-11**：`design-detailed.md` 按 WhiteSand 触媒整备（文体契约 = 提案设计/规范；过程记录移入本节；G1–G5 固化为实现约束）。

## Unresolved（登记，不在本轮解决）

- **`global.json` 实际不生效**（实测，见「验证记录」R1）：SDK 只从 `dotnet` 进程 CWD 找 `global.json`，`<key>\global.json` 是死文件；改动前后一致，无回归。让它真正生效需改 `ProcessStartInfo.WorkingDirectory` 或换放置位置，影响 CWD 语义，留独立任务。
- `#load` 链上文件的所在目录是否参与配置链（v1 只认主脚本 `filePath`）。
- 命令行显式开关族（`/packagesource:` / `/configfile:` / `/packages:`）与目录链的优先级。
- 全局包目录（`NUGET_PACKAGES` / `<RestorePackagesPath>`）是否应由 vbi 接管。
- **还原失败详情可能落在 stdout**（既有现象，真机反向用例观测到）：vbi 报 VBI1009 且摘要为空，说明 `dotnet restore` 的错误文本走 stdout，而运行器只把 stderr 交给翻译表 ⇒ `NU1101`/`NU1301` 的映射可能从不触发（改动前的 vbi 表现完全相同）。建议独立任务修。
- 同级多个 `NuGet.config` 拼写并存时 NuGet 自身的取舍未核实（本实现按 `CompareOrdinal` 取首个）。
