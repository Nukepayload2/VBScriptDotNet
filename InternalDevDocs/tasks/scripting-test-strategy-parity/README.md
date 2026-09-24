# 任务：脚本测试策略对齐 C# script mode（scripting-test-strategy-parity）——任务计划

> **状态：SP-F01 已交付（两轮，第二轮改写了第一轮的口径）；SP-F05 已交付（2026-09-24 main：给 `..\script-mode-coverage-parity\README.md` 的「负向算覆盖」裁定补上限定——负向格必须点名同容器正向对照，否则记 `缺口`）⇒ 待 SP-F02/SP-F03 派工**（构建面被 `auto-property-top-level-gate` 占用）。流水账 `tmp\vortex-logs\scripting-test-strategy-parity\`（`01-investigator-sp-f01.md` 含 §B 第二轮复跑）。
>
> **取证结论（与起点预期的差）**：
> 1. **配对密度不是缺口**。第二轮按"构造族级"重算：C# `5/6`、VB(HEAD) `5/6`、VB L4 宿主面 `20/21`；第一轮的"方法粒度"数（C# 67% / VB L2 81% / VB L4 30%）口径不同，以第二轮为准，两套数都留在日志里备算。
> 2. **真缺口是缺一根轴**：**构造 × 位置**。同一构造在"顶层语句 / 成员体 / 初始化器 / lambda / Shared 体"各位置的行为分别成格——这一轴两侧现树都是 **0/3**（C# 也没有），所以这不是"向 C# 对齐"能解决的，得自己立判据。
> 3. 缺陷存活期的实测：**关键字族当时 0 配对**，且有 **6 处把错误行为钉成正例**（HEAD `ScriptSemanticsTests.vb:588/:595`、`CodeGenScriptTests.vb:78/94-95`、`99/115-116`、L4 `:487/:497`）⇒ 修复只会把这些绿格弄红，测试集当时**零发现力**（三条缺陷 0/3）。issue 30 的精确形状全树 0 格，且 `ScriptSemanticsTests.vb:1258-1260` 至今写着"为绕本缺陷把属性放末条"。
> 4. **正格断值率落后**：C# 的正对照常钉 `expectedOutput` / 精确 IL / `ToTestDisplayString`；VB 多数正格只到"无诊断"。SP-F03 要抬。
> 5. **有意分叉（策略层，三条）**：① 本 fork **没有** C# 脚本测试工程（`class CSharpTestBase` 全树 0 定义、`Scripting\` 下无 C# 资产、`CSharpScript.cs` 在树外）⇒ C# 侧永远是**档 3 只读 oracle**，报数必须带档位，"跑一遍 C# 对照"不可执行；② `Scripting\VisualBasicTest`（L4 宿主面）在 C# 侧无对应物 ⇒ 是 VB 净增，C# 义务只到编译器层为止；③ 两侧 `[Theory]`/`<Theory>` 计数都是 **0**，`ScriptTestFixtures.cs` 是**宿主类型 fixture**（B/C/B2/I + 自引用 `HostRef`，10 个消费点、2 个文件）而非表驱动 ⇒ 不把"表驱动化"当对齐动作，也不引入。
> 6. **前提修正**：任务提示原把 `ScriptTestFixtures` 当"表驱动装置候选"，读码证伪。VB 侧对偶：L2/L3 `Compilers\Test\Utilities\VisualBasic\BasicTestBase.vb:437-452` 的 `CreateSubmission` 形参齐备（101 处 / 8 文件在用），但 `hostObjectType` **调用 0 次**、树内无 fixture 类型族；另有 4 份私有 `CreateSubmissionCompilation` 复制。⇒ SP-F02 的装置项是"补 fixture 类型族 + host 翻转 pair"，不是"造表驱动"。

## 三·补、post-fix 复核（2026-09-24，main 逐格打开源码核；判据②口径）
上表 §取证结论 3 的"22 格 / ≈40 外推"与 §取证结论 6 的"host 调用 0 次"是**修复前**（HEAD 早期）的读数，队列本轮逐条修复后已大面积过期。本轮抽样**逐格打开**（非粗 grep）核 `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb` 的脚本特有负向格与其同容器正向对照：

| 负向格（脚本特有拒绝） | 同容器正向对照（跑通并断具体值） | 三态 |
|---|---|---|
| `TopLevelYieldWithoutIterator_IsReported` BC30800 | `ReleaseOptimizedIteratorAndAsync_Conform`（`Iterator Function` + `Yield`，:426）| 亲验 ✔ |
| `TopLevelOnErrorStatement_IsReported` BC36956 | `OnErrorInsideMethod_Conforms`（成员体内 `On Error`，断 `"ERRORS-HANDLED"`，:202）| 亲验 ✔ |
| `TopLevelResumeStatement_IsReported` / `…ResumeNext…` BC36956 | 同上 `OnErrorInsideMethod_Conforms`（注释逐字点名）| 亲验 ✔ |
| `TopLevelAwaitInFinally_IsReported` BC36943 | 紧邻 `Await`-in-`Try` 正格（:55-60，断 `5`）| 亲验 ✔ |
| `TopLevelGoToIntoLoop_IsReported` BC30757 | 相邻 `GoTo` 合法跳转正格（:183，断 `"6/using/2"`）| 亲验 ✔ |

⇒ **该 L2 符合性套件的负向格本就按判据①配了同容器正向对照**（作者/历轮实施者已守此纪律），非"只钉诊断"。叠加：host-object 覆盖由队列 #22（构造/嵌套泛型 host）补上，`hostObjectType`"0 次"读数过期；本轮 B/E/F/H 每条修复自带正/反对照。**结论**：SP-F03 的"22–40 格待补"premise 在当前树**大面积不成立**，不宜作为固定工作量再追。**残余 J**＝(a) SP-F05 已把"负向须挂同容器正向对照"成文（本文件 §判据 1 + `..\script-mode-coverage-parity\README.md` SP-F05 裁定）⇒ 后续新格按此自审即可；(b) SP-F02「fixture 类型族 + host 翻转 pair」是**装置 ergonomics**（让"某容器漏测"一眼可见），非缺陷、可择期做，**不阻塞队列清零**。若将来某次新增违反判据①，再按 ledger 检索法逐格补，无需现在跑全量 372 格普查。

## 四、判据
1. 每个"脚本容器内被拒绝"的规范格，测试里必须同时存在：负向格（钉诊断 ID + 位置）+ 正向对照格（同形状在合法容器里跑通并**取具体值**）。**正向对照必须写在脚本自身的合法容器里**（提交类成员体、脚本所声明类型的方法体等）——**普通编译（`SourceCodeKind.Regular`）的对照不充分**：issue 29 那条当时的唯一"跑通"格正是普通编译对照，它证明形状合法却从未触达脚本容器的绑定路径。
2. 装置层：C# 侧的容器参数化做法若确实更强，VB 侧对偶后要能一眼看出"某容器漏测" —— 即新增容器维度时旧用例自动被带走跑一遍。**新增维度按 §取证结论 2 定为「构造 × 位置」两轴**，不是照抄 C#。
3. 覆盖率口径修订后，受影响的既有格子数要有**实数**（多少格缺正向对照），不得只给定性说法。第二轮实数：ledger 372 覆盖格中"只有负向证据" **22 格**（解析到方法体口径；文本粗口径 32，外推 ≈40），检索命令与命中数在日志。
4. 补的测试必须能在对应缺陷未修时**失败**（红灯可复现），修好后转绿；否则记为无效用例。**注**：issue 28 已于 2026-09-23 按 C# 改判回退 ⇒ 该族格子的"修好"方向反转，正向语义格要改写成**预期被拒 + 消息指向真原因**的形状（D7 测试回收），不得留下"成员体放行"的正例。

- **作者裁定（2026-09-22）**：「有这种 bug 说明测试没覆盖到位」——找出测试**策略**与 C# script mode 的差异点，预期策略与 C# 侧类似，并补测试。
- **一句话**：问题不在用例条数，而在**负向用例被计为覆盖**这一条口径（`..\script-mode-coverage-parity\README.md` 「判据②补充裁定 · 负向（断言诊断）用例**算** `已覆盖`」），使得"钉住一条诊断"的格子看起来已覆盖，而该诊断背后的**正向语义**从来没被测过。

## 一、这轮的三条缺陷为什么全都"已覆盖"（起点证据，已核对）

| 缺陷 | 当时锁住它的用例 | 为什么没暴露 |
|---|---|---|
| 成员体内显式 `Me` / `MyClass` / `MyBase` 被过宽拒绝（issue 28） | `ScriptSemanticsTests.vb:588` 等断言 BC36966 | 用例钉的正是**错误的行为**（把过宽禁令当规范），按裁定它算"已覆盖" |
| 提交类体内取 `Object` 继承成员报 BC30456（issue 29） | 无——被上一条挡住，从未可达 | 负向用例把通往该代码路径的路全堵了，正向面**零用例** |
| 顶层自动属性后随语句被解析层拒（issue 30） | 容器测试里有"自动属性带/不带初始化器"格 | 那些格是**非交互脚本类**或普通类形状，且不含"后随语句"这一维 |

⇒ 共同点：**没有任何一格要求"被拒的形状在普通类里是合法的"这一对照**，也没有要求"禁令放开后正向语义成立"。

## 二、与 C# 侧的对照义务（SP-F01 的核心问题，不得凭印象答）

树内 C# 脚本测试资产已知：`Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs`、**`ScriptTestFixtures.cs`**、`Syntax\Parsing\ScriptParsingTests.cs`、`Emit\CodeGen\CodeGenScriptTests.cs`。要回答：
1. C# 用什么**装置**组织脚本测试（fixture / 参数化 / 容器枚举），VB 侧有没有对偶物。
2. C# 的负向用例（如 CS7021/CS7020/CS8097 与 `this`/`base` 在脚本里的处理）是否**成对**配正向对照；VB 侧是否普遍缺这一对。
3. C# 的 `ScriptTestFixtures` 具体覆盖哪些"成员体内语义"形状（例如方法体内 `this.x`、`base.x`）；VB 侧对应格子现状如何。
4. 由此给出策略差异清单：**装置 / 配对规则 / 容器参数化 / 断言粒度（是否取值）** 四个维度各写「C# 做法 / VB 现状 / 对齐动作」。

## 三、范围与非范围
- 范围内：SP-F01 只读对照；SP-F02 策略落地（只动测试树，必要时新增/改造 VB 侧测试装置）；SP-F03 补测试（含把三条已注册缺陷的**正向语义**钉住）；SP-F04 回归；SP-F05 口径修订（给 coverage-parity 的"负向算覆盖"裁定补限定：**负向格必须点名正向对照**，否则记 `缺口`）。
- 非范围：不改产品源码（缺陷各归其任务：`script-class-explicit-me-scope`、`submission-object-member-lookup`、`auto-property-top-level-gate`）；不改 `Scripting\Core` 宿主行为；不重跑历史矩阵全量（只改口径并抽样复核受影响格数）。

## 四、判据
1. 每个"脚本容器内被拒绝"的规范格，测试里必须同时存在：负向格（钉诊断 ID + 位置）+ 正向对照格（同形状在普通类/合法容器里跑通并**取具体值**）。
2. 装置层：C# 侧的容器参数化做法若确实更强，VB 侧对偶后要能一眼看出"某容器漏测" —— 即新增容器维度时旧用例自动被带走跑一遍。
3. 覆盖率口径修订后，受影响的既有格子数要有**实数**（多少格缺正向对照），不得只给定性说法。
4. 补的测试必须能在对应缺陷未修时**失败**（红灯可复现），修好后转绿；否则记为无效用例。

## 五、并发
三个 agent 在跑（两任务只读取证 + 一任务验证者构建中）⇒ SP-F01 **只读**，禁编辑禁构建；SP-F02 起需独占构建面，排在最前面两个任务的构建面腾空之后。
