# 验收矩阵（string-script-factory-file-encoding）

判据见 `README.md` §一。**子 agent 禁读 `..\..\issues\`**。每格标三态；全量回归归 main。

## 一、格子（档 1，`Scripting\VisualBasicTest` 直跑 `-automated`）

| # | 形状 | 期望 | 反证它的方式 |
|---|---|---|---|
| C1 | `VisualBasicScript.Create(Of T)(code, options.WithFilePath(p).WithFileEncoding(enc).WithEmitDebugInformation(True))` → `Compile()` | **零 `BC37236`**，且 `emitResult.Success` | 去掉工厂里传编码那一处 ⇒ 必红（这是修复的报警线） |
| C2 | 同一 `options` 走 `Create(Of T)(stream, …)` | 与 C1 **同结果** | 两重载并排断言，防"只修一侧" |
| C3 | 同一字符串分别经两重载得到 `SourceText`：`Encoding` 与 `CheckedPath` | 逐字段相等（不是"都非空"） | 若字符串侧重载漏传编码，等式即破 |
| C4 | **反例锁**：不传 `WithFileEncoding` 的同一形状 | 仍报**逐字同一条** `BC37236`（不得顺手放宽那道门） | 若修复把编码改成"缺省 UTF-8"，本格转红 ⇒ 说明越界 |
| C5 | `ContinueWith(String)` 上的同形状 | 与 C1 同结果（同一实现） | 若存在第二处未改的 `SourceText.From`，本格转红 |
| C6 | 既有两条把该分歧钉成期望值的用例（`ScriptModeApiSurfaceConformanceTests.vb` 的 U7 格 2 / 格 8） | 按 D7 回收：改断"零 `BC37236` + 发射成功"，**不得整条删除** | 回收后若仍断旧期望，等于把缺陷钉住 |

## 二、账本核对（档 3）
1. `git diff` 只出现 `Scripting\VisualBasic\VisualBasicScript.vb`（+ 测试文件），**公共签名不变**、`PublicAPI.*.txt` 不在改动列表内。
2. `upstream-merge.md` 有新登记（该文件属上游同名文件 ⇒ 记函数级分歧 + 本任务/issue 指针）。
3. main 侧：七门全量四元组、`Scripting\VisualBasicTest` 全量 `-automated` 计数（基线 745/0，本任务新增用例后 Total 增加、Failed=0）。
