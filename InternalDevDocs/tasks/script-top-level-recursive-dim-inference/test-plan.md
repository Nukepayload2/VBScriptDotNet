# 测试计划：脚本顶层 `Dim` 推断撞循环（script-top-level-recursive-dim-inference）

> 判据与读数在 `README.md`，本文件只列格子。断言一律**断具体诊断码 + 位置**，不得用"编译失败"或"通过"当唯一断言（README §六 facet：测试）。
> 落点＝甲（复用 `ERR_CircularInference1` ＝ BC30980）。若 F01 判通道不通而停手，本文件全部格子作废并由 main 改写。

## 一、RD-A 正格（新增，档 1 · 已运行）

放置建议：`Compilers\VisualBasicSemanticTest\Semantics\ScriptTopLevelDimInferenceTests.vb`（32 已建该类，同容器便于对照）。脚本类用 `CreateSubmission(..., parseOptions:=TestOptions.Script)`。

> **计数与位置一律"先自 dump 实测、再按读数定桩"，不得预设。** RD-F01 的结构推演：一条连通循环链只会命中一次重入守卫（最内层那个 frame 被退回 `Object` 时命中），因此**一个循环分量只产出一条** BC30980，且报在被退回 `Object` 的那个符号上——这与 `README.md` §一 记的 C# 读数一致（互指只有一条 `CS7019 (1,5)`，指 `a`，`b` 不报）。故 A2/A4 原写的"各一条／三条"是**格子预期偏严**，已改成自 dump 定桩。

| 格 | 源串（同树多行） | 期望 |
|---|---|---|
| A1 | `Dim a = a` | 单条 `BC30980`；**先跑一次实际诊断集合再写断言**（条数、行、列、波浪线跨度全部取自读数）。文案实参＝**裸名**（见 README §二 实现约束②），与 `VariableTypeInference.vb:40` 的局部形状同式：`Type of 'a' cannot be inferred from an expression containing 'a'.` |
| A2 | `Dim a = b` ＋ `Dim b = a` | **自 dump 定桩**：读数应为"恰好一条、报在链中被退回 `Object` 的那个符号所在行"，不许零条；同时与 C# 侧一条 `CS7019` 对账并记进日志 |
| A3 | `Dim a = b` ＋ `Dim b = a` ＋ `Console.Write(a)` | 除 A2 的读数外**不得**级联出 `BC30451`／`BC30512`／`BC30456` 一类次生错（若出现，说明类型被算成错误类型而非"报完仍退 `Object`"，打回） |
| A4 | 间接三跳：`Dim a = b` ＋ `Dim b = c` ＋ `Dim c = a` | **自 dump 定桩**（预期一条，位置在被打回的那个符号）；硬要求只有两条：不死循环／不栈溢出，且**非零**诊断 |
| A5 | `Option Infer On` ＋ `Dim a = If(True, a, 1)`（自指经表达式） | 一条 `BC30980`（同样先自 dump）；对照同形状放进普通方法体（见 C2） |
| **A6** | **循环且全文无人引用**：`Dim a = b` ＋ `Dim b = a`（无 `Console`、无 `GetType`）；以及 `Dim a = a` 单条 | 必须报错。RD-F01 实测现状＝**完全静默**（`exit=0`、零字节输出，见 `tmp\probes-cyc\rd-f01\q1d-cycle-nowrite.out` / `q1e-self-nowrite.out`），比"要运行到 NRE 才看得见"更宽；旁证 `q1a-unref.vbx` 已证**无引用的字段照样能报出类型计算期诊断**（`BC30002` 落在 `NoSuchType` 上）⇒ 本格不许以"没人用就不报"为口径放过 |

**必须保持为"零诊断"的近邻格（不得被本任务改严）**：`Dim a = b` ＋ `Dim b = 5` ⇒ `a` 为 `Integer`、运行值 `0`（README §一对照行，两侧已实跑同形）。

## 二、RD-B 反例锁（既有行为不许变，档 1）

| 格 | 形状 | 期望＝现状 |
|---|---|---|
| B1 | `Dim x As Integer = 5`（显式 `As`） | 零诊断（32 的 T4 同形状，不重复建，指过去即可） |
| B2 | `Option Infer Off` ＋ `Dim a = a` | **仍零诊断**、退 `Object`（推断关着时不该有"推断循环"诊断）——若 F02 让这条变红，属过度收严，打回 |
| B3 | `Dim a = 5` 之后 `a.Length` | `BC30456`（32 的 T1，锁住"主体已对齐"那格不被破坏） |
| B4 | `Dim a = Nothing` | 零诊断、`Object`（32 §一的 `Nothing` 字面量退路） |

## 三、RD-C 非脚本面免疫（硬约束的验证义务，档 1）

| 格 | 形状 | 期望 |
|---|---|---|
| C1 | `Regular` 编译里普通类字段 `Private f As Integer`（含 `f` 自引用初值需 `As`） | 诊断集合与改前**逐字相同**（新报点不可达） |
| C2 | `Regular` 编译里方法体 `Dim a = b` ＋ `Dim b = 1` | `BC32000`（实测两档 `Option Explicit` 同判 ⇒ 各建一格），**不得**出现 `BC30980` 顶掉它 |
| C3 | 脚本里用户自写的 `Class`／`Module` 内字段与方法体 | 同 C1／C2（脚本容器内的普通类型不是 `IsScriptClass`，锚点 `SourceMemberContainerTypeSymbol.vb:1295-1300`） |

## 四、RD-D 回收义务（F01④ 的产出转成格子）

- D1：F01 grep 出的每一处"把顶层自指／互指钉成零诊断"的既有断言（含 doc-comment／README 里的 prose 钉），逐条列 `文件:行` → 按新判据改断 `BC30980`，**不许 revert 产品码来翻绿**（本仓 D7 测试回收线）。
- D2：`upstream-merge.md` 若在册条目里记过"顶层 `Dim` 循环静默退 `Object`"类口径，一并改写。

## 五、RD-H 宿主档 2（main 跑，F04）

重建 Debug 宿主后复跑留档探针，逐格对账：

| 探针 | 改前读数（已实跑） | 期望改后 |
|---|---|---|
| `tmp\probes-cyc\p1-cycle.vbx`（互指） | 零编译诊断、运行期 NRE | 编译期**一条** `BC30980`（一个连通循环只命中一次守卫，报在被退回 `Object` 的那个符号上；README §三 约束③，RD-F02 实测＝一条，与 C# 侧只有一条 `CS7019` 同形），不产出、不运行 |
| `tmp\probes-cyc\p6-self.vbx`（自指） | 零编译诊断、运行期 NRE | 一条 `BC30980` |
| `tmp\probes-cyc\p5-fwd.vbx`（非循环前向） | `type=Int32 val=0` | **逐字不变**（这是"没改严"的哨兵格） |
| `tmp\probes-cyc\p3-infer-check.vbx` | `BC30456` | 逐字不变 |

**RD-F02 实测带出的口径（F03 别误判）**：`Option Strict On` 下的循环形状是 `BC30980` ＋ **既有的** `BC30209`（"Strict On 要求所有变量声明带 `As`"）；`BC30209` 的条数改前改后**相同**（RD-F02 读数 4 → 4）⇒ 它是 32 那条早退路径的既有报点，**不算** A3 说的次生级联。

宿主读数三坑（README §六"反复摩擦"）：`MSYS_NO_PATHCONV=1`、诊断按 **CP936** 解码、循环形状必须同树成文件。

## 六、回归门（main，F04）

- 七门 Failed 全 0；总数只允许按本任务新增格子数增加（`Semantic` 门基线以 main 跑前 `git diff HEAD --numstat` 实测为准，不抄文档数字）。
- `Scripting\VisualBasicTest` 直跑 `-automated`（不能用 `dotnet test`），读最后一行 JSON 的 `"TestsFailed":0`。
- 已知偶发：L2 `ScriptModeArgsTests` 的文化泄漏类红（`..\..\..\tmp\HANDOFF.md` §4.5，本机文档；已用串行集合消解）——若再红，验证者必须给"与本次改动无关的证据"或"由本次改动引入"二者之一的判定，**"复跑即绿"不是结论**。

## 七、验证者档位声明要求

每格结论标：所用档位（档 1 已运行 ／ 档 2 宿主实跑 ／ 档 3 审查）＋ 证据三态（实锤／推测／猜测）。档 3 结论不得顶档 1 交付。落不进任何档的格 ⇒ 报 main「测不了」，不许静默跳过或硬撑低档。
