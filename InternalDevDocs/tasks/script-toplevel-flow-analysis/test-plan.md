# 测试计划：脚本顶层语句数据流分析（script-toplevel-flow-analysis）

档位：**档 1**＝单元测试；**档 2**＝重建宿主探针；**档 3**＝读码。收口判据只用档 1。
**前置**：见 README §〇/§三——顶层**块内局部** use-def 是真·可移植缺口（**非作者裁定**）；展开式修复已验证可接通 BC42104（7/7），落地前须先收敛 issue-16 的 ad-hoc `CheckBranchOutOfTopLevelFinally`（否则双报 BC30101）。4 个 parity 格已在树内（Semantic 5883 全绿）。

## 一、已交付的取证（当前码，产品码零改）
| 形状 | 当前读数 | 三态 |
|---|---|---|
| 顶层 `Dim s As String` 读 `s`（提交类） | 无 BC42104 | 实锤（已跑）|
| 顶层 `Dim i As Integer` 读 `i`（提交类） | 无 | 实锤 |
| 成员体 `Sub` 内 `Dim s` 读 `s` | BC42104 | 实锤 |
| 非提交脚本类顶层 `Dim s` 读 `s` | 无 BC42104 | 实锤 |
| 普通 `Module Main` 内 `Dim s` 读 `s` | BC42104 | 实锤 |

⇒ 结论：BC42104 在"顶层＝字段"下不触发；是否应触发＝设计裁定（见 README §三）。

## 二、待裁定后的新增用例（正/反对照，同容器，正向断具体值）
> 若裁定＝「顶层应报 BC42104」：下列"顶层"格从断无警告改成断 `BC42104`；若裁定＝「维持字段语义」：顶层格作为 **Not A Bug** 锁"无警告 + 说明为何"，成员体/普通格保留为对照。

1. `Toplevel_UnassignedRefRead`（提交类顶层 `Dim s As String` + 读）→ 断裁定后的集合（`{}` 或 `{BC42104}`）。
2. `MemberBody_UnassignedRefRead`（提交类成员 `Sub` 内同形状）→ 断 `BC42104`（对照，锁定局部确实报警）。
3. `OrdinaryCompilation_UnassignedRefRead`（`Module Main` 内）→ 断 `BC42104`（非脚本对照，须不受任何裁定影响）。
4. `Toplevel_ValueTypeRead`（顶层 `Dim i As Integer` 读）→ 断无警告（值类型字段语义，无论裁定都应静默）。
5. `Toplevel_AssignedThenRead`（顶层 `Dim s = "x"` 后读）→ 断无警告（赋值后不报，防误报）。
6. `SharedMemberField_NotAffected`（`Shared` 字段初始化器读未初始化字段）→ 断不因本改动新增 BC42104（保护 §2.25(g)/#9 语义）。
7. 若裁定＝甲（局部混合）：加跨提交用例 `#0 Dim s As String` / `#1 读 s` → 断字段化那条不退化（跨提交仍可访问，且不报局部未赋值）。

## 三、不得顺带做
- 不新增"不可达代码"警告（本编译器无活报点；队列 #7 明令）。
- 不改 §2.25(g) 的 `Option Infer` 字段类型推断；不改 `Shared` 字段惰性/挂钩。

## 四、回归（裁定并实施后）
- 定向：`ScriptSemanticsTests` / 本任务新类。
- 全量：七门（基线按新格数同步）+ L2 `-automated` + 重建宿主档 2 抽样顶层未赋值形状。
- 非脚本面免疫：普通编译对照格（§二 3/4/5）修前＝修后。

## 五、收口
- issue 17 转 Fixed（甲/乙实施）或 Not A Bug（维持字段语义）；账本按 README §五 FI-F05；commit 不预填。
