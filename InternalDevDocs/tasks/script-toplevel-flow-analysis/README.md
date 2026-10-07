# 任务：脚本顶层语句参与数据流分析（BC42104「未赋值即用」）（script-toplevel-flow-analysis）

> 队列 `..\csharp-script-parity-sweep\README.md` #7；缺陷登记 `..\..\issues\issue-top-level-statements-skip-flow-analysis.md`（issue 17）。
> **性质**：D7 可移植、判改。**非作者阻塞**——"字段 vs 局部需作者裁定"这一说法已被实测推翻（见 §〇）。
> **状态（2026-09-24 main）**：**已落地**（commit 待作者补）。展开 `EnsureInitializersAnalyzed` 的 `BoundGlobalStatementInitializer→.Statement` + 删 issue-16 ad-hoc `CheckBranchOutOfTopLevelFinally`/`ContainsFinallyBlock`（BC30101 改通用路单源）；`CheckAwaitInTryHandler` 保留。新增 7 格 ⇒ Semantic 5883→5886/5782、七门全绿、L2 769/0。账本 `..\..\upstream-merge.md` §2.25(i)。下方 §〇–§三 保留为取证与判定史（含"逐语句 ControlFlowPass＋放宽 filter＝Actual:[] 死路"的记录）。

## 〇、实测结论（档 1 ✔，先读这段）
- **C# oracle（本仓树内，非 csi）**：`Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs:986` `ERR_UseDefViolation`——顶层 `int a; int b = a;`（提升为 `Program` 字段）**不报** CS0165；`void F(){int c;int d=c;}` 与裸块 `{int e;int f=e;}`（局部）**报**。⇒ 顶层**字段**豁免＝parity（非缺口），**块内局部**该报。
- **顶层字段＝parity，已钉死**：`ScriptTopLevelDefiniteAssignmentTests.vb` 4 格（在树内、全绿）——顶层字段读无 BC42104、成员体未赋局部报 BC42104（证明分析对真局部生效）、跨提交字段读无警告、赋值后无警告。
- **修复机制已验证（临时改后回退）**：在 `Binder_Initializers.vb` `EnsureInitializersAnalyzed` 把 `BoundGlobalStatementInitializer` **展开为 `.Statement`** 再拼哑块 → 顶层**块内**局部（`If`/`For` 里 `Dim s As String`+读）**正确报 BC42104**，7/7 定向绿（含临时加的 3 块内局部格）。
- **唯一耦合（落地前必须先解）**：展开同时让**通用 `ControlFlowPass`** 下钻顶层语句体 → 与绑定期手跑的 `CheckBranchOutOfTopLevelFinally`（issue 16 加）**双报 BC30101**：3 个 `ScriptSemanticsTests` finally 格从 1 诊断变 2（`AssertSingleError` 断言单条⇒红）。且 emit 期分析与 `:292` 的 `ERR_BranchOutOfFinally` 发射门过滤（issue 16 依赖）时序要对齐。

## 一、症状（缩小到块内局部）
顶层**块内**局部未赋值即读 → 零 BC42104（同形放进成员体 / 普通 `Module` 报）。块外顶层 `Dim`＝字段，其零警告是 parity、**不属本缺口**。

## 二、根因（已定位，非推测）
顶层语句绑成 `BoundGlobalStatement`（`BindGlobalStatement`）置于合成 script 初始化器的桩体；`EnsureInitializersAnalyzed` 以 `StaticCast(BoundInitializer→BoundStatement)` 拼块、保留 `BoundGlobalStatementInitializer` 包装，通用 `Analyzer.AnalyzeMethodBody` 不下钻其体（故 issue 10/11/16 才逐条手跑 Await/Finally 检查）。

## 三、落地路径（明确，非开放问题）
1. 采用展开式修复（§〇 已验证），**同时**把 `CheckBranchOutOfTopLevelFinally` 收敛为只由通用 `ControlFlowPass` 供 BC30101（消双报），核对 `:292` 发射门过滤在 emit 期分析下仍生效（issue 16 不回退）。
2. 逐个跑 issue 16 finally 全形状（各分支种类、嵌套、`Using`/多 `Catch`/`#Load`）；若 `CheckAwaitInTryHandler`（BC36943）在通用 pass 下也双报则一并收敛（Semantic 面未见 await 双报，但须 Emit/IOperation/L2 全量确认）。
3. 正/反对照测试：保留已入树的 4 parity 格，重加块内局部格（If/For/Using；未赋→断**恰好一条** BC42104 且位置对，赋值→静默）。
4. 全量回归：七门 + L2 `-automated` + 重建宿主档 2。BC42105 / 不可达代码**不动**（无活报点）。

## 四、范围与非范围
- 范围内：上述落地路径；块内局部参与定义赋值分析。
- 非范围：不改块外顶层 `Dim`＝字段的设计（§2.25(g)/#32）；不改 `Shared` 字段惰性/挂钩（#9）；不动实例字段定义赋值语义；不新增不可达/BC42105 行为。

## 五、片段（FI-*）
| 片段 | 内容 | 验收 |
|---|---|---|
| FI-F01 | 展开 + 收敛 ad-hoc finally（含 await 若需）使 BC30101/BC36943 **单源** | 3 finally 格回 1 诊断、位置对；issue 16/10/11 全形状不回归 |
| FI-F02 | 顶层块内局部 BC42104 正反对照格（未赋→报、赋值→静默）；保留 4 parity 格 | 红灯可复现→绿 |
| FI-F03 | 七门 + L2 + 重建宿主档 2 逐格排雷（脚本块内未赋局部普遍新增 BC42104，含 conformance 套件） | 全绿；基线同步 |
| FI-F04 | 账本：issue 17→Fixed；动 `Binder_Initializers.vb`（上游同名）→ upstream-merge 新 §2.25(x)；spec 中英补"顶层块内局部参与定义赋值分析" | commit 不预填 |

## 六、并发与纪律
- 独占构建面；实施者禁跑全量、禁读 `issues\` 正文、禁 `git add`/`commit`。
- **高回归面**：落地后脚本"块内未赋局部"普遍新增 BC42104，须逐格排雷 + 可能改基线，且触及 issue 16 的 ad-hoc 机制——非单格安全改，故单列专做、勿与它任务混批。

## 七、回退记录（供续做参考，勿重复踩）
临时展开修复 → 定向 7/7 绿（BC42104 对）→ 全量七门暴露 3 个 finally 格双报 BC30101（5886/5779/3）→ 为保持树绿**回退产品码**（`EnsureInitializersAnalyzed` 复原 `StaticCast`，仅留一条 `NOTE` 注释记录此路径与耦合）、移除临时 3 格、基线复原 5883。4 parity 格与 NOTE 注释保留。续做＝按 §三 先收敛 ad-hoc finally，再重落展开 + 3 格 + 全量。
