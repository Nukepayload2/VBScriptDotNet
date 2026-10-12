# 测试计划：脚本目录链的 `NuGet.config` 生效 + 源指纹接线

> 任务：`vbi-nuget-package-source`（F3 交付物）。分层沿 `../vbi-nuget-reference/test-plan.md` 的 L1/L3/L4 口径；**无副作用纪律**为硬约束。
>
> 全量用例落地于 `Scripting\VisualBasicTest\NuGetSourceConfigTests.vb`；下表编号与该文件的用例一一对应。运行摘要与真机验证记录见 `README.md`「验证记录」。

## 〇、纪律（违反即打回）

- **不 spawn 进程**：还原由假 `IRestoreRunner`（返回固定 `RestoreOutcome`）注入；本任务不测 `DotNetRestoreRunner` 的进程行为（真还原见 G 人工清单）。
- **不网络**：所有源 URL 用不可达域名（如 `https://contoso.invalid/v3/index.json`）或纯字符串，绝不实际请求。
- **临时文件**：一律 `CreateIsolatedTempDirectory`（对齐 `CommandLineRunnerTests`），用后由既有清理回收；不在仓库跟踪目录写文件。
- **纯函数优先**：收集/镜像布局/指纹的用例只喂内存数据。

## 一、L1 纯函数（`NuGetSourceConfig`）

| # | 用例 | 输入 | 判据 |
| --- | --- | --- | --- |
| L1-1 | 空链 | 脚本目录无任何 `NuGet.config` | `Collect` 空；`MirrorRoot` = 脚本目录；`ProjectRelativePath` = `project.vbproj`；`Fingerprint` = `""` |
| L1-2 | 单层命中 | 脚本目录直接有 `NuGet.config` | 1 个 `SourceConfigFile`；`MirrorRelativePath` = `NuGet.config`；工程相对路径 = `project.vbproj` |
| L1-3 | 多层命中 | `<root>\NuGet.config` + `<root>\sub\NuGet.config`，脚本在 `<root>\sub\app` | 2 个文件；`MirrorRoot` = `<root>`；相对路径分别为 `NuGet.config`、`sub\NuGet.config`；工程相对路径 = `sub\app\project.vbproj` |
| L1-4 | 大小写不敏感 | 目录里是 `nuget.config` | 命中（`OrdinalIgnoreCase`） |
| L1-5 | 指纹敏感性 | 同路径、字节不同 | 指纹不同 |
| L1-6 | 指纹稳定性 | 同集合、枚举顺序颠倒 / 绝对路径不同 | 指纹相同（排序 + 只吃相对路径与字节） |
| L1-7 | 空集合指纹 | 空集合 | `""`（与改动前生产完全一致，回归判据） |
| L1-8 | 空/无效脚本路径 | `Nothing` / `""` / 不存在的目录 | 空集合、无异常 |
| L1-9 | 根边界 | 脚本在盘符根目录 | 向上遍历终止、不抛异常 |
| L1-10 | 深层嵌套 | 脚本路径深 8 层且每层都有 `NuGet.config` | 全收集、相对路径正确（长路径场景） |
| L1-11 | 读取失败 | 注入 `ReadAllBytes` 抛异常 | 不抛；`HasFailure`/`FailedPath`/`FailureReason` 就位（C5 的输入） |

> L1-4 的判据在实现中收紧了：除"命中 `nuget.config`"外，还断言 `MirrorRelativePath` 是**规范拼写** `NuGet.config`——因为 seam 由"探测单个拼写"改为"枚举目录"（Linux 区分大小写，见 `design-detailed.md` §一 规则 1），镜像必须写 NuGet 自己会找的拼写。

## 二、L1 纯函数（`NuGetRestoreCache.ComputeKey`）

| # | 用例 | 判据 |
| --- | --- | --- |
| L1-12 | 同包集 + 不同 `sourceFingerprint` | **key 不同**（本任务的核心不变量） |
| L1-13 | 同包集 + 同 `sourceFingerprint` | key 相同（与既有用例一致，回归） |
| L1-14 | `sourceFingerprint = ""` | key 与**改动前**逐字相同：用 **[已运行] 实测复现**的真实缓存目录名反推值钉死 — `ComputeKey({"microsoft.data.sqlite,8.0.1"}, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.401", "", False)` 必须仍是 `643d9e5b…4c9ca1`（`design-detailed.md` §C3.1 的复现）。比"记录某台机器的 key"更强：它是常量输入下对描述符格式（含每包后的 `;` 与空指纹的 `||` 槽）的冻结 |

## 三、L3 API（`RestoreRequest` 形状）

| # | 用例 | 判据 |
| --- | --- | --- |
| L3-1 | 未传镜像/相对路径（旧调用形状） | `ProjectFilePath` = `<cache>\project.vbproj`、`ProjectDirectory` = `<cache>`（**向后兼容**） |
| L3-2 | 传镜像集合 + 相对路径 | `ProjectFilePath` = `<cache>\<mirrorRel>\project.vbproj`；`ProjectDirectory` 随之为工程目录（`obj` 落点，见 `design-detailed.md` §九 约束 G1）；`MirrorFiles` 原样可读 |
| L3-3 | 越界相对路径 | 绝对路径或含 `..` ⇒ `ArgumentException`（镜像/工程绝不越出缓存键目录） |

## 四、L4 宿主（假 runner，`NuGetRestoreCoordinatorTests` 风格）

| # | 用例 | 判据 |
| --- | --- | --- |
| L4-1 | 无目录级配置 | 假 runner 收到的 `RestoreRequest.ProjectFilePath` 在 key 根；`MirrorFiles` 空；**且本次 key 叶名 = 空指纹算出的 key**（与 L1-14 同口径的回归） |
| L4-2 | 脚本旁有配置 | `MirrorFiles` 含该文件且内容一致；工程路径落在镜像深度（与脚本目录的相对深度一致） |
| L4-3 | 配置内容变更 ⇒ 二次调用 | 两次 key 不同 ⇒ 两次 `RestoreRequest.CacheDirectory` 不同（**不会被 `WriteOnce` 复用**）；内容改回 ⇒ 回到原 key（指纹是内容的函数，不是历史的函数） |
| L4-4 | 空 `filePath`（REPL 提交） | `MirrorFiles` 空、指纹空；key 叶名 = 空指纹的 key（与 L4-1 同形状） |
| L4-5 | 显式注入 `sourceFingerprint` | 覆盖自动计算（测试注入能力保留，既有用例不破） |
| L4-6 | 配置读取失败（注入 `readAllBytes` 抛） | 产出 **VBI1012**，消息含路径与原因，锚在脚本文件；**不 spawn 还原**（`RestoreCalls = 0`）、不误报 NU 系诊断 |
| L4-7 | 镜像**写入**失败（假 runner 回填 `ConfigFailurePath`） | 产出 VBI1012，**不落** `ExitCodeToDiagnostic`（不被误报成 SDK 缺失/通用失败） |
| L4-8 | 镜像落盘 + 只写一次（直接调 `DotNetRestoreRunner.WriteMirrorFiles` 到隔离临时目录，**不 spawn 进程**） | 文件出现在 `<cache>\<mirrorRel>` 的相对层级；已存在的内容**不被重写**（E7 write-once 前提） |
| L4-9 | 镜像写入失败不抛（用"同名文件占位目录"制造确定性失败） | 返回 `False` + 首个失败路径 + 非空原因 |
| L4-10 | 工程与 `global.json` 落盘到镜像深度（直接调 `DotNetRestoreRunner.WriteProjectFiles`，不 spawn 进程） | 断言工程父目录**事前不存在**，调用后 `<cache>\app\project.vbproj` 与 `<cache>\global.json` 就位。**回归用例**：工程父目录未创建会被兜底 Catch 吞成「找不到 SDK」（`design-detailed.md` §九 约束 G5），L4-10 钉死这一处 |

> **L4 落盘判据的覆盖方式**：假 runner 不写盘，故"镜像文件就位 + write-once"与"工程落在镜像深度"由直接调用 `DotNetRestoreRunner.WriteMirrorFiles` / `WriteProjectFiles`（写隔离临时目录，无进程）的用例覆盖；"NuGet 是否真的按镜像链取源"需真还原，仍属 **G 人工清单**。

## 五、回归面（必须零改动通过）

- `NuGetRestoreCoordinatorTests`、`NuGetRestoreEngineTests`、`NoNuGetZeroRegressionTests`、`NuGetReferenceDirectiveNTests`、`NuGetRuntimeHandshakeTests`、`NuGetMissingNativeAssetsTests`、`NuGetPackageResolverTests`。
- 既有"无 NuGet 引用时协调器零介入"用例（`NoNuGetZeroRegressionTests`）必须仍绿（本设计不新增任何在无 `#R "nuget:"` 时的行为）。

## 六、验收 gate

1. 上表新增用例全绿 — **[已运行] 25/25 通过**（`NuGetSourceConfigTests`，xunit v3 console runner `-class "*NuGetSourceConfigTests"`）。
2. `Scripting\VisualBasicTest` 全量 0 失败 — **[已运行] Total 811 / Errors 0 / Failed 0 / Skipped 0**（810 那轮的日志在 `tmp\vortex-logs\f6-full-suite.txt`，43.6s）。
3. 手工 G 清单 — **部分完成，见下**（G1/G2/无配置对照 已跑通；G3 凭据 / G4 两层嵌套 / 与改动前 `dgSpecHash` 逐字对照 未跑）。

## 七、G 人工清单（真还原，不进自动测试）

> 运行器为 xunit v3 console（非 MTP console）：`-class` / `-method` / `-namespace` / `-filter`；`--filter-class` 等 `--` 前缀选项不存在。
>
> 手工用例的条件：自建私有源（`dotnet pack` 出 `VbiSourceConfigProbe 1.0.0`，放进 `repo\feed\`，**从未发布到 nuget.org**）+ `repo\NuGet.config`（`<clear/>` + 该本地源）+ 脚本 `repo\app\probe.vbx`，在 CWD 不在 `repo` 的情况下运行 `vbi.exe repo\app\probe.vbx`。**[已运行]** 结果与原始观测见 `README.md`「验证记录」。

| # | 场景 | 判据 | 状态 |
| --- | --- | --- | --- |
| G1 | 脚本旁的 `NuGet.config` 是否真的决定源 | 正向：本地私有源里的包能还原并用起来（`pong`，exit 0）；反向（移走配置 + 清空全局包缓存 + 删旧 key 目录）：同一脚本失败（exit 1）。**加一条硬证据**：缓存里那份 `project.vbproj.nuget.dgspec.json` 的 `restore.sources` **只有配置里那个本地源**（`nuget.org` 被 `<clear/>` 清掉） | **done** |
| G2 | 同集换源/去源 ⇒ key 变化 | 有配置与无配置两次运行落在**两个不同 key 目录**（有配置那个含镜像 `NuGet.config`，无配置那个没有） | **done**（同集「内容变更 ⇒ 新 key」的自动面见 L4-3） |
| G3 | 私有源 + `packageSourceCredentials`（真实凭据） | 还原成功 ⇒ 凭据随镜像文件生效 | **未跑**（需真实凭据源；设计上凭据随文件原样复制，未做任何凭据语义处理） |
| G4 | 深层嵌套：`repo\NuGet.config` + `repo\sub\NuGet.config`，脚本在 `repo\sub\app\` | 两层源都参与；缓存内出现 `NuGet.config` 与 `sub\NuGet.config`，工程在 `sub\app\` | **未跑**（自动面 L1-3/L1-10 已覆盖布局与相对路径） |
| G5 | 与改动前对照（无任何目录级配置） | 工程仍在 key 根、**无镜像文件**、dgspec 源 = 用户/机器级三条 ⇒ 与改动前同形（真机已确认前三条；`dgSpecHash` 的**逐字**对照未做，但自动面 L1-14 已用实测复现的真实 key 钉死空指纹路径） | **部分 done** |
| G6 | ~~`global.json` 镜像深度~~ **已由实测结案** | 实测证明 `global.json` 由 `dotnet` 进程 CWD（非工程目录）解析 ⇒ 改动前后**同样不生效**，无回归、无需验证（事实见 `design-detailed.md` C2.1 与 §十一） | **N/A** |

## 八、证据留存

- 自动用例：xunit v3 console 运行摘要（Total/Errors/Failed/Skipped）。
- 人工 G：`tmp\vortex-logs\...` 下的原始输出（含 `dgspec` 中 `sources` 段落摘录，作为"源真的换了"的实证）。
