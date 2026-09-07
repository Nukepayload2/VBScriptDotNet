# 测试计划：`.vbx` / 交互窗口 NuGet 包引用（`#R "nuget:包名[, 版本]"`）

> 状态：测试计划。依据链：`../../proposals/proposal-vbi-nuget-reference.md` → `../../meetings/meeting-vbi-nuget-reference.md`（RESOLUTION 1–9；U9 spike 已过 → R4 定稿）→ `design-overview.md` → `design-detailed.md`（改动蓝图 A–I + 自动裁决规则 + pass 条件）→ 本测试计划。
> 测试宿：`Scripting\VisualBasicTest\`（MTP 项目：`dotnet test` 静默不跑，须直接跑程序集 `-automated`，见 memory `vb-scripting-test-runner`）；共享 Core 收口回归另走编译器测试（`Compilers\Core\Portable` 构建面）。
> 无副作用纪律（CLAUDE.md）：单测禁止网络 / 文件写入 / 进程启动 / 注册表写入。真实 `dotnet restore`、`%LOCALAPPDATA%` 写入、跨平台（`.so`/`.dylib`）native 实跑属**门控集成验收**（README「门控集成验收」节 V-G2），**不在无副作用单测内**。
> 可注入 seam（design-detailed §C2/§D/§E 已裁决）：`hostCapability`（net48 注入）、`RestoreRunner` fake（假退出码 + 假 assets）、协调 seam `INuGetRestoreCoordinator`——使 net48/NU/SDK/触发策略分支在 net10 单测可达，无需真实进程。

## 1. 测试分层矩阵

| 层 | 适用性 | 说明 |
|----|--------|------|
| L1 解析 | ✅ | 语法 fork `TryParsePackageReference`（§B）——逗号/大小写不敏感/Trim/省略版本/近失配拒绝 |
| L2 语义 | ✅ | 共享 Core 单 `#R`→N 展开（§A2）——跨提交继承 + 位置锚定，U9 两场景 in-memory 改造 |
| L3 API/接线 | ✅ | resolver 注入 seam 默认 null 零行为（§C）+ host 能力/restore runner 注入（§C2/§E） |
| L4 宿主/REPL | ✅ | 宿主驱动环 + 诊断锚定（§D 决策表）+ 零回归（§H）——MTP 宿主测试 |

> 与 diag-mode 任务不同，本任务改动面贯穿 L1（解析）→ L4（宿主环），四层全适用；无副作用约束下 L4 用内存 `TestConsoleIO`/`StringReader`，脚本用例需临时 `.vbx` 时复用 `CreateIsolatedTempDirectory`（`CommandLineRunnerTests.vb` 既有基建）。

## 2. L1 解析（`NuGetPackageResolverTests.vb`，新增）

> 落点 `Scripting\VisualBasicTest\NuGetPackageResolverTests.vb`。`TryParsePackageReference` 为 `internal`，`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:68` 已授 IVT 给 `Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests` → 可直接调。锁定 §B 五类用例 + doc「本 fork 有意偏离上游」标注。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| P1 | 逗号拆分 | `"nuget:Newtonsoft.Json, 13.0.3"` | `(Newtonsoft.Json, 13.0.3)` | 纯内存 |
| P2 | 大小写不敏感前缀 | `"NuGet:…"`/`"NUGET:…"` | True；name/version 不受前缀大小写影响 | 纯内存 |
| P3 | 各段 Trim | `"nuget: Newtonsoft.Json , 13.0.3 "` | `(Newtonsoft.Json, 13.0.3)` | 纯内存 |
| P4 | 近失配拒绝 | `"nugt:X, 1.0"`/`"nuget ：X"`/`"nuget:"`/`"nuget:, 1.0"`/`"nuget: , 1.0"` | 全 False | 纯内存 |
| P5 | 省略版本 | `"nuget:Newtonsoft.Json"` | `(Newtonsoft.Json, String.Empty)` | 纯内存 |

## 3. L2 语义（`NuGetReferenceDirectiveNTests.vb`，新增）

> 落点 `Scripting\VisualBasicTest\NuGetReferenceDirectiveNTests.vb`。U9 两场景（`tmp/u9harness`）in-memory 改造：两个 fixture 程序集 `VisualBasicCompilation.Create` + `Emit` 到 `MemoryStream` + `MetadataReference.CreateFromStream`，交自定义多结果 resolver（仿 `tmp\u9harness\Program.cs` 的 `MultiResolver`）。**不做磁盘写、不 spawn、不网络**。

| # | 用例 | 构造 | 断言 | 无副作用 |
|---|------|------|------|---------|
| S1 | 跨提交继承（U9-i） | `#R "nuget:X"` → resolver 返 [主资产, 闭包]；提交 1 直用 `PkgMain.MainType` → `ContinueWith` 提交 2 直用 `PkgClosure.ClosureType` | 提交 1 零错；提交 2 零错（闭包类型可见）——N 经 `ExplicitReferences` 跨提交继承 | 纯内存 |
| S2 | 位置锚定（U9-ii） | 两行不同位置的 `#R "nuget:…"` 各展开「合法主资产 + 垃圾 `MemoryStream`」+ 混一行普通 `#R` | 每个垃圾资产 metadata 读失败诊断各锚自己 `#R` 行（行号正确、非 `Location.None`、不串行） | 纯内存 |

> 裁决注（§A2）：垃圾流无 FilePath → 用诊断 Id（`BC31519`）/消息关键字 + 行号断言，不断言路径字符串；in-memory 引用集补 `GetType(Object).Assembly` 流 + `ScriptMetadataResolver.Default` 委托；闭包只在提交 2 被源码引用（保 U9-i 语义）。原版行为对照由 V-Z 全量跑兜底。

## 4. L3 API / 接线（注入 seam 零行为）

> 目标：证明所有新增 seam 默认 null / 空根集 = 现状零行为变化；注入路径可测。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| L3-1 | `CreateCurrentPlatformResolver` 默认（无 packageResolver） | 现有调用形状 | 现有引用解析行为与改动前一致（H2 全量兜底） | 纯内存 |
| L3-2 | `CommandLineRunner.GetMetadataReferenceResolver` 默认 | 无 packageResolver 参数 | 现有调用方零改动（编译路径 U6-A 不注入） | 纯内存 |
| L3-3 | host 能力注入 | 会话 ctor 注入 net48 | §D net48 诊断分支可达（单测注入即覆盖，无需真实 net48 宿主） | 纯内存 |
| L3-4 | restore runner fake | fake 返退出码 ≠ 0 / 假 stderr | `ExitCodeToDiagnostic` 转译正确（NU1101/网络/SDK 各行） | 纯内存 |
| L3-5 | `ShouldRestore` 纯函数 | 本进程同集 / 集合变化 / 跨进程重跑同 key | 决策 = skip / 必调 / 总调（§E3 表） | 纯内存 |
| L3-6 | `ReadAssets` 纯函数 | 假 assets.json 文本 | compile/runtime/native 根解析正确；`ref/` vs `lib/` 取 NuGet 已选 compile 目标 | 纯内存 |
| L3-7 | 临时工程生成器 | key + hostCapability=net48 | 产物含 `Microsoft.NETFramework.ReferenceAssemblies`（§E pass，不落盘断言） | 纯内存 |

## 5. L4 宿主 / 诊断 / 零回归（MTP 宿主测试）

> L4 决策表（§D）每行一例 + 零回归两例。宿主测试沿 `CommandLineRunnerTests.vb` 既有基建（`CreateRunner`/`TestConsoleIO`/`CreateIsolatedTempDirectory`）。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| D1 | 版本缺失（U4-A） | 提交含 `#R "nuget:X"`（无版本）经协调器预扫描 | 锚 `#R` 行「请指定版本」；该指令不进包集合；restore 不触发 | 纯内存 |
| D2 | 前缀格式错误 | `#R "nuget ：X"` | 锚 `#R` 行「前缀格式错误」 | 纯内存 |
| D3 | 空包名 | `#R "nuget:"` | 锚 `#R` 行「缺少包名」 | 纯内存 |
| D4 | 疑似拼错 | `#R "nugt:X, 1.0"` | 锚 `#R` 行「前缀疑似拼错」 | 纯内存 |
| D5 | SDK 缺失 | fake runner / SDK 门控注入「无 SDK」 | 锚 `#R` 行「需要 .NET SDK」；无 nuget 时不查 SDK | 纯内存 |
| D6 | net48 native 能力 | hostCapability=net48 + fake runner 产 native 资产 | 锚 `#R` 行宿主能力诊断（非 restore 错误） | 纯内存 |
| D6b | 宿主平台包范围外（§G2.6） | 预扫描喂 `#R "nuget:Microsoft.WindowsAppSDK, 2.2.0"` | 锚 `#R` 行**范围外诊断**（指去宿主 `#R "Microsoft.WinUI"` + WinUI 宿主）；不进包集合；restore 不触发 | 纯内存 |
| D7 | NU1101 | fake runner 退出码 ≠ 0 + stderr 含 NU1101 | 锚 `#R` 行「找不到包」转译 | 纯内存 |
| D8 | 网络失败 | fake runner 网络错误 | 锚 `#R` 行「无法连接 NuGet 源」 | 纯内存 |
| D9 | U6-A 负测试 | 编译模式 `/r:nuget:X` | 不触发还原、不报 nuget 专属诊断 | 纯内存 |
| Z1 | 零回归一（无 nuget `.vbx`） | 普通 `#R` 本机 dll + 打印 + 顶层语句，经内存宿主跑 | stdout 与 golden 逐字节相等；协调 seam 被咨询但无 restore/SDK 路径尝试（stub 仅在实际请求 nuget 时抛） | 纯内存 |
| Z2 | 零回归二（N>1 休眠） | 既有 0/1 单结果 resolver 全量用例 | 无回归（`Scripting\VisualBasicTest` 全量绿） | 纯内存 |

> 注：D1–D8 与 D6b 的诊断位置锚定（`Location.GetLineSpan` 行号 = 该 `#R` 行）对每个用例都要断言；真实 `dotnet restore` / `%LOCALAPPDATA%` 只在门控 V-G2 跑，不经 D1–D9。

## 6. 测试执行规约（本仓特有）

- `Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**不可用 `dotnet test`**（EXIT 0 但静默不跑）。
- 验证一律：先 `dotnet build`，再直接跑程序集 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或加 `-method <FullyQualifiedName>`（单测过滤）。
- 共享 Core `Compilers\Core\Portable` 构建面（net10）随 V-Z 构建 gate 覆盖。
- 门控集成验收（V-G2 sqlite 端到端 + 跨平台 + net48 宿主实跑）由作者/QA 在 V-Z 收口后手动执行（README「门控集成验收」节），测试计划列出步骤与预期如下节。

## 7. 门控集成验收（作者/QA，非无人值守串行）

| # | 步骤 | 预期 |
|---|------|------|
| G2-1 | net10 宿主 `.vbx`：`#R "nuget:Microsoft.Data.Sqlite, 8.0.x"` → `SqliteConnection("Data Source=:memory:")` 建库建表插值 → 一次 `SELECT` | 返回值正确（managed 闭包 + native `e_sqlite3` 经 loader 探测根找到） |
| G2-2 | 不带 native 探测根集重跑 | `DllNotFoundException`（阴性对照，证明根集必需） |
| G2-3 | 跨平台 dll 命名/RID 回退/缺目录诊断 | 按 §G2.3-4 清单人工过（`.dll`/`.so`/`.dylib`；缺目录给清晰诊断） |
| G2-4 | net48 宿主跑同脚本 | 收宿主能力诊断而非崩溃 |
| G2-5 | **托管传递依赖闭包真实还原**：net10 宿主 `.vbx` 引一个**带托管传递依赖**的包（非 Newtonsoft 单 dll 无依赖）——如引主包后用其传递依赖类型（主包类型 + 依赖库类型各至少一处） | 还原闭包 N>1：assets compile 段含主包 + 传递依赖；脚本直用主包与传递依赖类型均零编译错、运行正确（实证「单 `#R`→N」在**真实 restore** 下的托管闭包） |

> 注：G2-5 专补「间接依赖」缺口——S1（in-memory 闭包）已证 Core-N 机制，G2-1 sqlite 传递偏 native；G2-5 用**托管传递依赖**的真实包钉「restore 闭包 → 编译引用集 → 运行」全链。具体包名/版本由实施者选**有托管依赖的稳定 pin 版**（如 `Microsoft.Extensions.*` 一族取有依赖者），预期在 §7 验收时记录。

## 8. 既有测试影响核查

| 受影响面 | 影响 | 处理 |
|---------|------|------|
| `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs`（U9 patch 已应用） | 单 `#R`→N 展开：对既有 0/1 输入休眠（`:887` 对 0/1 返原数组） | V-A1 形状核对 + L2 S1/S2 新测 + V-H2 既有引用解析测试回归 |
| `NuGetPackageResolver.cs` / `RuntimeMetadataReferenceResolver.cs`（§B/§C） | parse 签名不变、调用点 `RuntimeMetadataReferenceResolver.cs:146` 零改动；`CreateCurrentPlatformResolver` 加默认 null 可选参 | 现有调用方零改动；V-H2 全量回归 |
| `CommandLineRunner.cs`（fork 已有本地改动 §2.5/2.6） | 加 `INuGetRestoreCoordinator?` 可选 ctor 参（默认 null）+ seam 三调用点 | 默认 null 零行为；Z1 协调器 stub 证明无 nuget 不触发 |
| 共享层新 seam（`AddNativeProbeRoot` internal 等） | 不新增 public 面、不触 `PublicAPI.*.txt` | V-Z Public API 零增量核对 |

## 9. 判定通过标准

- **plan 阶段标准**：分层矩阵完整、无副作用纪律满足、MTP 直跑规约明确、观察点/回归面列出、门控集成分离（V-G2）。
- **实现阶段标准**：L1 P1–P5 + L2 S1–S2 + L3 L3-1..7 + L4 D1–D9/D6b + Z1–Z2 全绿；`Scripting\VisualBasicTest` **全量 0 失败**（直接跑程序集 `-automated`）；`Compilers\Core\Portable`（net10）构建绿；门控集成 V-G2（G2-1..G2-5）由作者/QA 收口执行。
- 无副作用纪律（Grep 抽查）：无副作用单测文件不含网络/文件写/进程 spawn/注册表（`RestoreRunner` fake 是注入替身，不是真 spawn）。
