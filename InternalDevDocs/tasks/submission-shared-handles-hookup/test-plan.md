# 验收矩阵（submission-shared-handles-hookup）

判据与范围见 `README.md`。**子 agent 禁读 `..\..\issues\`**。每格标档位与三态。

## 〇、基线事实（既有实锤 + 本轮取证）

| 形状 | 现状 | 证据 |
|---|---|---|
| 顶层 `Shared Event` + `Shared Sub … Handles`（无共享字段初始化器），随后 raise | exit `0`、**stdout 为空**、零诊断（挂钩丢失） | `tmp\probes\u6bc\handles-shared.vbx`（issue 记录为实锤；本轮 4 条 REPL 探针因逐行提交切开块而**未复现**，勿改用 REPL 试） |
| 同上，但在 raise 前读一次那个共享字段 | 侧写与 `H` 一起出现（`.cctor` 被触发、挂钩投递） | `tmp\probes\u10\v1-touch-field-first.vbx`、`v2-probe-touched.vbx` |
| 同上，`RuntimeHelpers.RunClassConstructor(t.TypeHandle)` 强制初始化 | 立刻投递 | `tmp\probes\u10\w4-touch-self-type-via-reflection.vbx` ⇒ 挂钩体本身无恙 |
| `Shared b As Integer = 42` ⇒ `?b` | ✅ `42`（`.cctor` 存在且首次访问触发） | 取证 `tmp\probe-sweep\p9b-shared-field.txt` |
| `Shared Function F()`（体内打印）+ `Shared a As Integer = F()` + 主语句 | 只打印主语句 ⇒ 初始化器**惰性**（与 C# 同形，A 半边不修） | `tmp\probe-sweep\p9a-shared-init.txt` |
| 普通编译同形状（`Holder.Go()` 内 raise，`Main` 调它） | ✅ 投递 `H` | `tmp\probes\u10\m4-holder-go.vb` ⇒ 脚本特有，非 CLR 差异 |

## 一、F01/F02/F03 修复与测试（档 1）

| # | 形状 | 期望 | 备注 |
|---|---|---|---|
| T1 | **真值先行**：只有 shared `Handles`、无共享字段初始化器 | 实测记录：`SharedConstructors` 是否为空 / 越界 / 静默丢弃；`GetHandles` 的返回与诊断集合 | F01 的产出，决定 F02 的写法；不得沿用"推测=静默丢弃" |
| T2 | 同 T1 形状，修复后 | 事件被 raise 时处理器**恰好投递一次**（计数断言，非"有输出即过"） | 判据 |
| T3 | 普通类同形状（非脚本） | 计数同为 1，逐字不变 | 正向对照（D7 口径连带要求） |
| T4 | 修复前后都不 `RunClassConstructor`、也不读共享字段 | 修复后仍**不提前**执行共享字段初始化器（`beforefieldinit` 惰性口径不变），只保证挂钩已登记 | 锁 A 半边不被改坏 |
| T5 | 共享字段初始化器 + 共享 `Handles` 共存 | 两者都生效且顺序可断（初始化器先于挂钩可用） | 同一次 `EnsureCtor` 复用 |
| T6 | 每次 `ContinueWith` 新建实例的提交链 | 处理器**不重复**挂钩（计数仍 1） | 否掉"挂进脚本初始化器"次选的根据，必须实测成立 |
| T7 | `.cctor` 体内接收者 | 不得出现 `Me`；`RaiseEvent`/`AddHandler` 的 receiver 解析与 IL 有实测证据（或复核不出来即停手） | README §三.1 |
| R1 | 非提交脚本类（`DeclarationKind.Script`）的 `Handles` | 行为不变 | 反例锁 |
| R2 | `WithEvents` 形状（宿主是属性 setter，不是 `.cctor`） | 行为不变（本任务不放宽它） | 反例锁；issue 正文的"三处更正"第 1 条 |
| R3 | `BC37343`（`Handles` 的 `WithEvents` 变量来自早先提交或宿主对象） | 仍报该诊断，不新增合法形状 | 反例锁 |

## 二、F04 / F05
1. `spec`（main 落笔）：`Shared` 初始化时机口径 + `zh-CN` 同步 + 结构对等计数（当前基线 412/412、`##`=10、`###`=14、`####`=12、fences=10、表格行=35、`vbnet` 块逐字一致）。
2. 七门 gate Failed=0（基线以 `scripts\verify-vb-compiler-tests.ps1` 内记录为权威，新增用例逐门报四元组）；`Scripting\VisualBasicTest` 直跑 `-automated`；重建发布版宿主复跑 `tmp\probes\u6bc\`、`tmp\probes\u10\`、`tmp\probe-sweep\p9*` 全部形状。
3. 账本：issue 18 状态按 A/B 两半分别写（A = 与 C# 同形不修；B = Fixed，commit 不预填）；`upstream-merge.md` 登记两处上游同名文件的函数级分歧。

## 三、终局判据
1. T1 的三态从**推测**升级为**实锤**；T2–T7 有值断言；R1–R3 有档 1 证据。
2. T4/T6 证明"没顺手改 A 半边、没重复挂钩"——这两格是本任务最重要的反例锁，缺任一条不得收口。
