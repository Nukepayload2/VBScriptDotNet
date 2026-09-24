# 验收矩阵（submission-member-redeclaration-tiebreak）

判据与范围见 `README.md`。**子 agent 禁读 `..\..\issues\`**。每格标档位与三态；档 3 不得冒充档 1。

## 〇、基线事实（改动前发布版宿主 `2.0.0-Beta+c15a959` / `f3c84d3`，档 2 已运行；探针在 `tmp\probe-sweep\`）

| 探针 | 形状 | 读数 |
|---|---|---|
| `p8-dim-redeclare` | `Dim x = 1` ⇒ `Dim x = 2` ⇒ `?x` | ✅ `2`，零诊断 ⇒ **field 面本已正常**，本任务不动它 |
| `p8b-fn-redeclare` | `Function M(x)` 两版 ⇒ `?M(5)` | ❌ `BC30521`（候选 `Submission#1.M` / `Submission#0.M`） |
| `p8c-prop` | `Property P As Integer = 3` 两版 ⇒ `?P` | ❌ `BC30521` |
| `p8c-overloads` | `M(Integer)` 与 `M(String)` ⇒ `?M(5)` / `?M("a")` | ✅ `100` / `200` ⇒ **真实跨提交重载必须继续可用** |
| `p8c-shadows` | `Dim w` ⇒ `Shadows Dim w` ⇒ `?w` | ✅ `2`，零诊断（`Shadows` 写了也无效果） |
| `p8c-shadows-fn` | `Function M` ⇒ `Shadows Function M` ⇒ `?M(5)` | ❌ 仍 `BC30521` |
| `p8b` 改型 | `Dim y As Integer = 7` ⇒ `Dim y As String = "hello"` ⇒ `?y` / `?y.Length` | ✅ `hello` / `5` |

`BC40003`（`WRN_MustOverloadBase4`，`Errors\Errors.vb:1825`，报点 `OverrideHidingHelper.vb:441`）是**继承**遮蔽警告；提交类符号层无基类型 ⇒ 提交链上不可能出现它 ⇒ **不得把它当验收值**。

## 一、F01/F02 修复与测试（档 1）

| # | 形状 | 期望 | 备注 |
|---|---|---|---|
| G1 | 两版同名同签名 `Function M(x As Integer)`（`x + x` / `x * x`），在第二版之后 `? M(5)` | **零诊断**，值 `25` | C# 基线 `TestBranchingSubscripts`（`{{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs:452`）期望值即 `25` |
| G2 | 同 G1 的 `Property P`（两版不同实现体） | **零诊断**，读到最新版的值 | 判据 2 |
| G3 | 三版链（`M1`→`M2`→`M3`，返回值各不同） | 零诊断，取**最新** | 链长 >2 时 slot 比较仍成立 |
| G4 | 跨 `#Load` 多树后重声明同名 `Function` | 零诊断且取链上最新 | 与 `IsScriptClass`/多树产法两轴都要跑 |
| G5 | **反例**：同一次提交内写两个同签名 `Function M` | 仍报**现有**诊断（不得被新规则放过） | 判据 4 |
| G6 | `M(Integer)` 与 `M(String)` 分属两次提交 | 各自命中（`100`/`200`） | 判据 3，防查找层截断式修法 |
| G7 | 宿主对象成员与提交成员同名（`HostObjectType` 有/无两轴） | 现有可见性/优先级**不变** | 新规则只在两候选都属 `Submission` 时生效 |
| R1 | `Dim`（field）跨提交重声明 | 行为逐字不变（`2`、改型 `hello`） | 反例锁 |
| R2 | `Shadows` / `Overloads` 写在提交成员上 | 不参与、不新增诊断（与现状同） | 反例锁 |
| R3 | 普通编译（非脚本）里的重载歧义与遮蔽 | 逐字不变（`BC30521` 真歧义、`BC40003` 遮蔽警告各自仍在原位） | 反例锁；`OverloadResolution.vb` 是全编译器共用面 |
| R4 | `Overrides`/接口实现查找（`OverrideHidingHelper`） | 不新增诊断、不新增合法形状 | 新规则不碰继承路径 |

## 二、F03 回归
1. `scripts\verify-vb-compiler-tests.ps1` 七门 Failed=0；**基线以脚本内记录为权威**（Semantic 现为 5843/5739/104/0），本任务新增用例会抬高数字 ⇒ 逐门报实测四元组交 main，**不要自己改脚本基线**。
2. `Scripting\VisualBasicTest` 程序集直跑带 `-automated`（MTP 工程，`dotnet test` 会静默跑 0 个）；失败先用 `TestMethodUniqueID` 反查用例名再判归因。
3. 重建发布版宿主后复跑 `tmp\probe-sweep\p8*`：`p8b`/`p8c-prop`/`p8c-shadows-fn` 由 `BC30521` 变**零诊断并取最新值**；`p8`/`p8c-overloads`/`p8c-shadows`/改型格读数**逐格不变**。

## 三、终局判据
1. G1–G7 有值断言；R1–R4 有档 1 证据；两条既有钉（`ScriptModeApiSurfaceConformanceTests.vb:89-110` 的 BC30521 用例）按 D7 回收改断 `25` + 零诊断，**不得直接删除**。
2. 三态表里"修好后零诊断取最新"从**推测**升级为**实锤**（取证批次只到档 2/档 3）。
3. `upstream-merge.md` 登记 `OverloadResolution.vb` 的函数级分歧（新增 tie-break 规则）与本任务/issue 指针。
