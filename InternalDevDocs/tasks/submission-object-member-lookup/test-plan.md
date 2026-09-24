# 验收矩阵（submission-object-member-lookup）

契约与范围见 `README.md`。**子 agent 禁读 `..\..\issues\`**。每格标档位与三态。**重要**：本机发布版宿主是**改动前**二进制，`Me` 在其上仍被 BC36966 拒 ⇒ 行为格只能用 L1/L2 测试（对**工作树**编译器）取证，不得用现成宿主的探针读数冒充本任务的效果。

## 〇、基线事实（已运行，工作树编译器 + F01/F02 用例）

| 形状 | 现状 | 证据 |
|---|---|---|
| 体内 `MyBase.ToString()` | ✅ 零诊断，输出 `Submission#0` | `ScriptSemanticsTests` 的 `ExplicitMyBaseInScriptFunctionBody_RunsAndPrintsTheScriptClassName`（L2） |
| 体内 `Me.字段` / `MyClass.字段` | ✅ 零诊断 | 同任务 B1/B3 与 L2 `ScriptModeStatementConformanceTests` 换形状后的两格 |
| 体内 `Me.ToString()` / `MyClass.ToString()` | ❌ BC30456 | F02 实施记录 + F01 验证者复核（临时用例实测后删除） |

## 一、S-F01 取证交付（档 3 + 档 1 阅读，禁构建禁编辑）

| # | 交付 | 通过条件 |
|---|---|---|
| T1 | `BaseType` / `BaseTypeNoUseSiteDiagnostics` / `MakeDeclaredBase` 在 `Compilers\VisualBasic\Portable\**` 的消费点清单 | 逐条文件:行 + 「是否依赖提交类基类型为 `Nothing`」判定；计数与抽样复核一致，不得只给"看起来几处" |
| T2 | `Me` / `MyClass` 接收者的成员查找路径（从 `BindMeExpression` 到成员解析）与它在提交类上的失败点 | 逐跳文件:行；明确指出是 `BaseType Is Nothing` 造成还是另有门 |
| T3 | **C# 对照**（README §四 三项） | 表格「VB 现状 / C# 做法 / 采用形态 / 分叉理由」，引 C# 文件:行 |
| T4 | 候选修法清单（至少含：甲=符号层给脚本类 `Object` 基；乙=`Me`/`MyClass` 查找特例；丙=其它你发现的更对等形态），每条给改动面、回归风险、与 `MyBase` 既有兜底能否收敛成一处 | 三态标注；未跑的一律标推测；**不得**自行实施 |
| T5 | 对 §五 那条旧约束（禁改 `:59`）的裁定建议：维持 / 撤销 / 附条件撤销 | 依据 T1 清点与 T3 对照，给结论与理由 |

## 二、S-F02/S-F03 修复与测试（档 1）

| # | 形状（提交类实例成员体内） | 期望 | 层次 |
|---|---|---|---|
| K1 | `Return Me.ToString()` | 零诊断；解析到 `Object.ToString`；运行输出实例的类型全名（`Submission#0` / `Script`） | L1 + L2 |
| K2 | `Return Me.GetType().Name` | 零诊断；运行输出容器类型名 | L1 + L2 |
| K3 | `Return Me.GetHashCode()`、`Return Me.Equals(other)` | 零诊断，绑定到 `Object` 成员 | L1 |
| K4 | 不限定调用 `ToString()` / `GetType()`（隐式 `Me`） | 与 K1/K2 同（普通类合法即此处合法） | L1 |
| K5 | `MyClass.ToString()` / `MyClass.GetType()` | 零诊断 | L1 |
| K6 | `Object` 的共享成员以 `Me.ReferenceEquals(...)` 形状访问 | 按普通类给的同一结论（合法或同一条普通诊断），不得新造诊断 | L1 |
| K7 | 上一任务的规避形状回收：`ScriptModeStatementConformanceTests` 体内两格增补 `ToString` 正例（原形状因本缺陷被迫换成 `Me.字段`） | 增补后该类全绿 | L2 |
| R1 | 反例锁：普通编译（`Regular`）与脚本内声明的普通类 | 行为零变化（既有相关用例全绿） | L1 |
| R2 | 反例锁：`Overrides` / `NotInheritable` / 接口实现查找 / `Inherits` 相关诊断 | 不新增诊断、不新合法形状；T1 清单点名的消费点各有一条锁 | L1 |
| R3 | 反例锁：`MyBase` 现有兜底不回退，且（若采纳"收敛成一处"）`MyBase` 三格期望值不变 | 既有 `ExplicitMyBaseIn*` 用例逐条仍绿 | L1 + L2 |
| R4 | 反例锁：显式关键字判据（顶层禁 / 体内放 / `Shared` 落 BC30043）不回退 | 上一任务 A1–A5/B1–B4/C1–C3 全绿 | L1 |

## 三、S-F04 回归（构建面独占后跑）

| # | 手段 | 通过条件 |
|---|---|---|
| Q1 | `scripts\verify-vb-compiler-tests.ps1` 七门 | Failed = 0；基线以脚本内记录为权威，Total = 基线 + 本任务新增用例数（逐门列数） |
| Q2 | L2 全量直跑 `-automated` | Failed = 0；偶发失败按 P 条规矩给二择一归因，不得用"重跑不失败"放过 |
| Q3 | **重建发布版宿主**后跑 K1–K5 形状 | 每条有实际读数；`Me.ToString()` 输出类型全名。这是本任务第一次能拿宿主级证据的地方 |

## 四、S-F05 账本（档 3）
L1 `issues\README.md` 行 29 与 issue 29 → **Fixed**（commit 不预填），根因从"候选"升级为实锤并回填文件:行；L2 `upstream-merge.md` 入账（含 §五 若撤销旧约束的决定）；L3 若与 C# 存在有意分叉，`spec\` 侧口径由 main 落笔并保持中英对等。

## 五、终局判据
1. K1–K6 与 R1–R4 全部有档 1 证据；K7 完成"规避形状"的回收。
2. Q1–Q3 全绿，Q3 有宿主级读数。
3. T1–T5 已回填 `README.md` §三/§五，契约里不留"未定形态"；与 C# 的每一处差异要么消除、要么登记为有意分叉。
