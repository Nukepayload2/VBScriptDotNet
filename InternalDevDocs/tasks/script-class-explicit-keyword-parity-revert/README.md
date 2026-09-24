# 任务：脚本类显式关键字恢复 C# 同形（script-class-explicit-keyword-parity-revert）——任务计划

> **状态：待派工（等构建面腾空；现由 `auto-property-top-level-gate` 的 AG-F02b 占用）**。流水账在 `tmp\vortex-logs\script-class-explicit-keyword-parity-revert\`。
>
> **进度（2026-09-23）**：**RV-F01 由 main 完成**（`Binder_Expressions.vb` 净 `+21 −15`，定向实测 15 条红；日志 `01-main-rv-f01.md`）；**RV-F02 已完成**（Semantic 8 格翻成 BC36966 拒绝格并改名、Emit 两格恢复上游每形两条、L2 五格 `AssertRuns`→`AssertReports`，定向实测 Semantic 77/77、Emit 17/17、L2 **745/0**；日志 `02-implementer-rv-f02.md`）。**账本已登记**：`..\..\upstream-merge.md` §2.25（与上游的唯一差＝判定次序）+ `..\..\meetings\meeting-scripting-dialect.md` 权衡一就地更正 + issue 28 转 Not A Bug。**main 侧全量与档 2 也已完成**：七门 Failed 全 0（Syntax 4098/4095、Semantic 5847/5743、Symbol/Emit/IOperation/CommandLine/Phase2 数字逐字不变）、`Scripting\VisualBasicTest` 直跑 745/0；重建发布版宿主后 `R1`–`R6`、`B`、`P` 由"零诊断/取到值"变 **BC36966**，`E@24` 仍是 **BC30043**（共享先判生效），`C`/`G1`/`G2`/`K`/`M`/`N`/`Q` 读数不变 ⇒ 本任务**已验证，待提交**（commit 号不预填）。
> 前因任务：`..\script-class-explicit-me-scope\`（其 F01/F02/F03 已档 1 全绿、未提交）。**本任务把它的判据部分回退**。缺陷登记：`..\..\issues\issue-script-class-explicit-me-in-member-bodies.md`（issue 28，已带「撤销改判」条）。**子 agent 禁读 `issues\`**——判据与形状矩阵在本文件与 `test-plan.md` 内复述自足。

- **裁定来源（作者，2026-09-23）**：`..\..\decisions.md` **D7 冲突裁定**新增第二个实例——**「照 C# 回退」**。C# 在脚本类里**一律拒绝显式 `this`/`base`，成员体也算**（`Compilers\CSharp\Portable\Binder\Binder_Expressions.cs:55-73` 的 `HasThis`，逐字 `return !inTopLevelScriptMember || !isExplicit;`；`BindBase` `:2636-2639` 报 CS1512、显式 `this.X` 报 CS0027。**判据锚点＝这段源码 + 作者实跑**：`Microsoft (R) Visual C# 交互窗口编译器 5.10.0-1.26380.3` 下 `void test() { this.ToString(); }` → `(1,15): error CS0027`；`Compilers\CSharp\Test\Semantic\Semantics\SemanticErrorTests.cs:1365` 的 `this.goo(); // 5` 只作旁证——同段 `:1381` 的 `// OK` 在注释块内，不作断言锚）。⇒ VB 原判据（容纳类型是脚本类 ⇒ 显式三关键字 BC36966）**就是 C# 同形**，不属缺陷。
- **被放弃的收益（作者明示接受）**：顶层 `Dim` 生成的字段被成员体内同名局部遮蔽时，失去 `Me.字段` 这个逃生口（只能给局部改名）。

## 一、目标判据（回退后必须成立的形状）

1. **实例**成员体内（顶层 `Sub` / `Function` / `Property` 的访问器体）与其中的 lambda：显式 `Me.` / `MyClass.` / `MyBase.` → **BC36966**（恢复旧行为）。
2. **顶层脚本代码**（全局语句、顶层字段/属性初始化器、写在这些之中的 lambda）里的显式三关键字 → **BC36966**（不变，与 1 同一判据来源）。
3. **`Shared` 成员体内的显式引用 → BC30043、隐式实例引用 → BC30369**（**这是前任务 F01 已落地的部分，保留不回退**）。依据：C# 的 `HasThis` 把静态检查放在脚本门**之前**（`Binder_Expressions.cs:45-49` ⇒ CS0026/CS1511），脚本门（`:64-73` ⇒ CS0027/CS1512）在其后 ⇒ 与 C# 同形的次序是「先共享诊断、后脚本禁令」。
4. `MyBase` 在脚本类里**不得**被绑定到 `System.Object`：删除前任务在绑定路径加的兜底（`Binder_Expressions.vb` 的 `GetBaseTypeOfScriptClass` 及其在 `BindMyBaseExpression` 成功分支的调用）。
5. 隐式（不加限定的）引用行为**零变化**：顶层实例方法可直接调用另一个顶层方法、读顶层字段（issue 08 的既有口径）。
6. 普通编译（`SourceCodeKind.Regular`）与脚本内声明的普通类**零变化**（`Me.x & MyClass.x & MyBase.ToString()` 合法并照常运行）。

## 二、范围内

| # | 单元 | 说明 |
|---|---|---|
| RV-F01 | 判据回退 | `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`：`CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext` 的 BC36966 条件从 `IsBindingTopLevelScriptCode()` 恢复为「容纳类型是脚本类」，**但共享上下文分支必须仍在脚本门之前命中**（判据 3）；`GetNearestNonLambdaContainingMember` / `IsBindingTopLevelScriptCode` 若因此无消费者则删除，不留死代码；`CanAccessMyBase` 的 `Debug.Assert` 放宽（`... OrElse ContainingType.IsScriptClass`）随兜底一并回退，并核对回退后不会有路径能撞断言 |
| RV-F02 | 断言同步 | 把前任务 F02 为「成员体放行」而改的既有断言**改回**拒绝侧；`test-plan.md` §二的表逐格落 |
| RV-F03 | 单元测试 | 前任务新增的 17 + 23 格里，凡是"成员体合法"的正例全部翻成**负例钉**（BC36966 + 波浪线在关键字上），"Shared 落 BC30043/BC30369"的格子**保留**；每格标三态 |
| RV-F04 | 回归 | 七门 gate（**基线以 `scripts\verify-vb-compiler-tests.ps1` 内记录为权威**，Semantic 门已由 main 更新为 5843/5739/104/0；本任务翻格会改变 Passed/Total，逐门报实测四元组交 main）+ `Scripting\VisualBasicTest` 直跑 `-automated`（MTP，`dotnet test` 静默跑 0 个）+ 重建发布版宿主复跑探针 |
| RV-F05 | 账本 | issue 28 状态转 **「Not A Bug（改判撤销）」**而非 Fixed；`upstream-merge.md:280` 的在册条目改写为「显式路径逐字保持上游判据 + 共享分支次序」；`spec` 措辞由 main 落笔（见 §四） |

## 三、非范围

- **不碰解析器**（`Parser\**`）：另一在办任务 `auto-property-top-level-gate` 的面，同批 dll 不得并发构建。
- **不改** `ImplicitNamedTypeSymbol.vb:51-60` 的返回值（提交类符号层无基类型 = C# 同形，预期行为）。
- **不修** `..\submission-object-member-lookup\`（issue 29）：本回退让它的形状重新不可达（BC36966 在前），那条线只剩「裸 `ToString()` 在提交类里能否解析」与「非提交脚本类比 C# 多继承」两问，**本任务不答**。
- 不新增诊断码、不改 `Errors.vb`/`VBResources.resx`/`xlf\**`（BC36966 的消息逐字含 "in top-level script code"，而回退后它会报在成员体里 ⇒ 这是**已知文案代价**，随 D7 冲突裁定接受，不许为它造新码）。

## 四、spec 侧（main 落笔，agent 不得改 `InternalDevDocs\**`）

`spec\spec-scripting-dialect.md:234`（诊断表行）、`:246-270`（`Me` 限制段 + 代码例 + `**Decision**`）、`:334`/`:339`（C# 对比行与其解释段）、`:381`（Testing 条）需按判据 1–4 回写为「脚本类内一律拒显式三关键字；共享诊断在前」，`zh-CN\` 同步并保结构对等（408/408 行、`##`=10、`###`=14、`####`=12、fences=10、表格行=35、`vbnet` 块逐字一致）。

## 五、串行与停止上报

- 与 `auto-property-top-level-gate`（AG-F02b 在跑，独占构建面）**不得并发构建/测试**（BC2012）。本任务只读取证与探针不在此列。
- 停止上报：若回退导致某条**上游同名测试**（非本 fork 新增）从绿转红，或 `CanAccessMyBase` 断言在回退后被真实路径撞上 ⇒ 停手交 main，不硬改。
