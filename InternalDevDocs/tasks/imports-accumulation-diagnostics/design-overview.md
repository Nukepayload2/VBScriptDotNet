# 概要设计：`Imports` 跨提交累积的解析失败降级为诊断

> 状态：概要设计。依据链：`../../spec/spec-scripting-dialect.md`（`:213` 规范要求，**唯一验收口径**）→ `../../meetings/meeting-scripting-dialect.md`（RESOLUTION R5 + TODO `:171`）→ `../../proposals/proposal-scripting-dialect.md`（`§6` `:98-104`，正文冻结）→ `../../decisions.md`（M2/M5）→ `README.md`（Vortex 拆分 + 共享源码事实）。
> 本设计画全局图景与数据流，为 `design-detailed.md` 提供落点与边界；源码事实以 `README.md`「共享源码事实」+ `design-detailed.md` 锚点为准，引用以仓库相对 `文件:行号` 给出。

## 1. 背景与目标

**目标（一句话）**：宿主的 `Imports` 跨提交累积路径在遇到无法解析的累积子句时，报一条锚定在肇事子句位置的诊断并继续，而不是抛 `ArgumentException` 终止宿主/会话；无坏子句时全链路零行为变化。

**规范要求（spec 原文，`:213`）**：

> **Failure.** A parse failure of an accumulated clause is reported as a diagnostic and is not propagated as an exception; the failure is reported at the position of the clause that caused it. **Decision**: an accumulated clause is untrusted input — it was written in an earlier submission and is replayed by the host — so the accumulation path treats a malformed clause as a diagnostic condition rather than as a fatal error.

拆成两条可判要求：

- **R-诊断不抛**：解析失败的累积子句**必须**产出诊断，**不得**以异常形式向宿主调用方传播（现状是 `ArgumentException`）。
- **R-位置**：该诊断**落在肇事子句的位置**。

**现状缺口（实证）**：

1. 宿主累积路径把整条链的子句文本汇总后一次性交给 `GlobalImport.Parse(importNames)`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:119`），该重载在任一条子句解析失败时 `Throw New ArgumentException(...)`（`Compilers\VisualBasic\Portable\GlobalImport.vb:77-86`，throw 在 `:83`）。宿主侧无 try/catch。
2. 后果分级：`CreateSubmission` 抛出的非 `CompilationErrorException` 异常不被 `Script.Compile` 捕获（`Scripting\Core\Script.cs:332-346` 只 catch `CompilationErrorException`）→ 从 `CommandLineRunner` 的 REPL 循环（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:296-365`，循环内无 catch）逸出 → **一次拼错的 `Imports` 子句可终止整个 REPL 会话**。文件脚本路径由 `:264-268` 的兜底 `Catch Exception` 打印异常并返回 `e.HResult`，同样是「宿主级异常」而非诊断。
3. 触发面精确：`GetGlobalImportsForCompilation` 只收集**前序提交**的语法树子句（`:150-161`）与前序提交的 `Options.GlobalImports` 文本（`:146-148`）以及**本提交的 `script.Options.Imports`**（`:117`）；本提交语法树里的 `Imports` 语句按文件级导入正常绑定，不进累积路径。所以坏子句的典型来源是「更早某条提交里写错的 `Imports` 语句」，它在本提交被**重放**时炸掉。
4. 编译器侧确实已有「不抛、回吐诊断」的重载（`:103-108`），但产出的诊断**一律 `NoLocation`**（`GlobalImport.MapDiagnostic` `:111-130` 两分支都返回 `NoLocation.Singleton`），肇事子句身份只体现在消息文本（`ImportDiagnosticInfo.vb:21-24`）。**所以位置只能由宿主补**——这是本设计的核心判断。

**根因定性**：累积子句是**不可信输入**（spec `:213` 的 Decision 原文），累积路径是**宿主侧机制**（`VisualBasicScriptCompiler.GetGlobalImportsForCompilation`），因此修复面在宿主；编译器侧无需改动（且 throwing 重载是 public API 并有既有断言钉住，见 README「共享源码事实」）。

## 2. 分层图（Layering）

| 层 | 职责 | 本任务落点 | 禁止事项 |
|---|---|---|---|
| 决策 | 诊断码 / 位置 / 去重 / 通道四项裁决 | 本设计 §4 + `design-detailed.md` §F1-0–§F1-5 | 不在实施期改判 |
| 流程 | 逐子句解析 → 降级 → 锚定 → 汇总 → 抛出 | `design-detailed.md` §F1-1–§F1-4 | 不把「能编译」当「诊断已报」 |
| 执行 | 改宿主累积路径 + 新增测试 | Vortex F1.1–F1.4 | 不碰 `Scripting\Core`/`Compilers\**` |
| 验证 | 按 pass 条件核对 + 全量回归 | `test-plan.md` §2–§7、Vortex F1.5 | 验证者不顺手改代码 |
| 触发 | 何时进累积路径（只在有前序提交/有 options.Imports 时） | 既有 `GetGlobalImportsForCompilation` 调用点 `:206` | 不在无子句时构造诊断/空跑解析 |
| 状态 | 诊断一次性（深度 1） | `design-detailed.md` §F1-3 | 不做跨进程/跨会话状态 |
| 停止规则 | 实施-验证循环收敛条件 | `README.md` Vortex 表 + Accepted 门 | 不无限循环 |

## 3. 方案骨架（数据流）

```
CreateSubmission(script)                                   [VisualBasicScriptCompiler.vb:164]
  └─ GetGlobalImportsForCompilation(script)                [:206 → :113-120]
       ├─ 收集本提交 options.Imports                        [:117]  → 子句条目 (text, Location.None, depth=0)
       ├─ 收集前序提交（递归，最老先收）                      [:139]  → 子句条目 (text, clause.GetLocation(), depth+1)
       │    ├─ previousSubmission.Options.GlobalImports     [:146-148] → 合成树位置（见 §5 边界）
       │    └─ 各语法树 CompilationUnitSyntax.Imports        [:150-161] → 真实源码位置
       ├─ 逐条：去重（OrdinalIgnoreCase 文本，先到胜）         [:115/:122-132]  ← 语义不变
       ├─ 逐条：GlobalImport.Parse({text}, diags)             [非抛重载 :103-108]
       │    ├─ 无 Error → 收进累积结果
       │    └─ 有 Error → 丢弃子句 + 诊断 WithLocation(条目位置) 记入待报集
       ├─ 待报集仅保留 depth=1 的条目（深度 ≥2 静默丢弃）       [本设计 §F1-3]
       └─ 待报集非空 → Throw New CompilationErrorException(msg, diags)   [先例 :62-64]
                              │
                              ├─ Script.Compile() 捕获转诊断        [Script.cs:332-346]
                              │     ├─ REPL：DisplayDiagnostics → 跳过本提交、会话继续  [CommandLineRunner.cs:370-375]
                              │     └─ 文件脚本：RunAsync 抛出 → ReportDiagnostics → Failed  [CommandLineRunner.cs:259-263]
                              └─ 无坏子句 → 返回值形状与今天完全一致（零行为变化）
```

**关键不变量**：

- 无坏子句 ⇒ 收集结果与今天逐条等价（同一批 `GlobalImport` 对象、同一去重语义、同一顺序）。
- 有坏子句 ⇒ 该子句**不进** `GlobalImports`（否则编译器侧会再抛一次），其余子句照常生效（降级 = 部分可用）。
- 诊断只在**抛出的那一次** `CreateSubmission` 里产生；`compilation.GetDiagnostics()` **看不到**它（它在 `Builder.Build` 阶段就被抛出），这是通道选择的直接后果，测试必须据此写法（`test-plan.md` §5 注）。

## 4. 四项待定项的裁决（概要）

| # | 待定项 | 裁决 | 一句话理由 |
|---|---|---|---|
| 1 | 诊断码：复用还是新增 | **复用** `OptionsValidator.ParseImports` 产出的原始 `Imports` 子句语法错误诊断（id + 文案原样） | 零新码、零 xlf 同步；语义就是「这条 `Imports` 子句写错了」，与用户写文件级 `Imports` 时看到的码一致；新码还要走 13 语言 xlf 纪律 |
| 2 | 诊断位置：怎么映射到当前编译 | **宿主补位置**：`diag.WithLocation(子句所在语法树的位置)`；无源码来源回退 `Location.None` | 编译器侧 `MapDiagnostic` 恒 `NoLocation`（`:111-130`），位置信息只存在于宿主手里的子句语法节点上 |
| 3 | 多提交链去重 | **深度 1 才报**；深度 ≥2 静默丢弃 | 坏子句所在提交不可改（REPL 提交不可变），若每次都报会刷屏；若永久报错则会话被「钉死」。深度 1 报一次 = 用户看到、会话可继续 |
| 4 | net48/Desktop 分支 | **同源同路径**，无需分支处理 | `Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:6` 双 TFM（`netstandard2.0;net10.0`）单源编译，`VisualBasicScriptCompiler.vb` 与 `Compilers\VisualBasic\Portable\GlobalImport.vb`/`OptionsValidator.vb` 全树无 `#If` 条件编译；Desktop 测试工程复用同一实现 |

## 5. 与 spec 的关系（实现补齐后一致）

- spec `§Imports across submissions / Normalization`（`:204-209`）描述的三条规范化（成员导入按符号去重、别名按名去重 + BC30572 先到胜、XML 前缀 BC30573 先到胜、当前提交遮蔽）**已由编译器实现**，宿主只负责把文本喂进 `Options.GlobalImports`（`SourceModuleSymbol.vb:381-400`）。本任务**不动**这条路径，理由见 `design-detailed.md` §F1-0。
- spec `§Failure`（`:213`）是本任务唯一要补的规范缺口：实现补齐后，「解析失败 → 诊断 + 位置」成立。
- **边界（与 spec 措辞的差异，已记录）**：spec 在 `§Boundaries of the dialect`（`:349`）说规范化冲突的「诊断落在失败的那条子句的位置」，但实现里 **project-level import 的诊断按设计一律 `NoLocation`**（`SourceModuleSymbol.vb:460-465` 注释明文），子句身份由消息文本承载（`ImportDiagnosticInfo.vb:21-24`）。本任务**不**去改这条既有设计（会动共享编译器树 + 破 `SymbolErrorTests.vb:6106-6125` 的断言形状）；本任务只保证**解析失败**这一类的诊断带真实位置。此差异已浮出，供后续 spec 措辞收敛。

## 6. 影响面

| 面 | 影响 | 说明 |
|---|---|---|
| 宿主行为（无坏子句） | **零变化** | 逐子句解析 vs 一次性解析：结果集合与顺序一致（去重键、先到胜不变），额外成本 = 每个子句一次合成树解析（链长通常个位数） |
| 宿主行为（有坏子句） | **行为改变（本任务目的）** | 从「抛 `ArgumentException` 可能打死 REPL」变为「报一条带位置的诊断 + 该提交不执行 + 会话继续」 |
| 编译器 / 共享层 | **零 diff** | 不动 `Compilers\**`、不动 `Scripting\Core`；`PublicAPI.*.txt` 零增量 |
| 上游合并 | **无新增义务** | 无共享树改动，`upstream-merge.md` 无需新增类别（实现者需在收口时确认这一点） |
| 既有测试 | **需零回归** | `Imports_CrossSubmission` / `Imports_DoNotReplaceInheritedOptionsImports`（`InteractiveSessionTests.vb:24-47`）必须零改动通过；`Scripting\VisualBasicTest` 全量绿 |
| 性能 | 可忽略 | 累积子句数 = 会话链上出现过的 `Imports` 数；每子句一次 `ParseText`（既有实现已是「拼一条大文本解析一次」，逐子句化不改变量级） |

## 7. 考虑过的替代方案（概要，细节见 `design-detailed.md` §F1-5）

- **A（采用）**：宿主逐子句解析 + 丢弃坏子句 + `CompilationErrorException` 上报锚定诊断。
- **B（否决）**：新增宿主诊断 sink seam（非阻塞上报）。否决理由：需要新的 Friend/public 面与跨程序集装配，且「坏子句不进累积集」的语义已经用诊断表达；seam 只会让同一事实有两个出口。
- **C（否决）**：改编译器 throwing 重载为不抛。否决理由：public API 语义变更 + 既有 `Assert.Throws` 断言（`UsedAssembliesTests.vb:4283`、`ParseXml.vb:4504`）+ 共享树 diverg 需 merge 账本，而宿主侧一行调用即可达成同等效果。
- **D（否决）**：把坏子句文本原样塞进当前提交的 `GlobalImports` 让编译器去报。否决理由：`GlobalImports` 只接受已解析的 `GlobalImport` 对象（构造需要 `ImportsClauseSyntax`），且编译器侧仍会经 `MapDiagnostic` 丢掉位置。

## 8. 验证策略

- **L1–L4 四层矩阵**（`test-plan.md` §2–§5）：逐子句解析判定 / 跨提交会话语义 / API 与宿主通道 / REPL 端到端，全部内存 I/O、无副作用。
- **全量回归**（`test-plan.md` §7）：`Scripting\VisualBasicTest` 直跑程序集 `-automated` 全量 0 失败 + 七门 gate 与基线一致。
- **零越权核对**：`git diff --stat` 只出现 `Scripting\VisualBasic\` 与新增测试文件。

## 9. 风险

| 风险 | 等级 | 处理 |
|---|---|---|
| 逐子句解析改变 `GlobalImport` 对象的语法树归属（每个子句一棵合成树 vs 今天共用一棵） | 低 | 该差异只影响 `MapDiagnostic` 内部的 span 换算，而 project-level 诊断恒 `NoLocation`（`:111-130`）；L1/L2 用例断言「无坏子句时编译结果与诊断集不变」 |
| `CompilationErrorException` 让**复现坏子句的那一条提交**不执行 | 中（有意为之） | 这是「报诊断」的唯一通道（§F1-4）；用 R-用例钉住「只影响一次、会话继续」；备选方案 B 已记录 |
| 深度 ≥2 静默丢弃会让「用户没看到那一次诊断」后无提示 | 中 | 规范只要求「报」，未要求「每次报」；本条作为**已记录边界**写入 `design-detailed.md` §F1-3，并由 R-用例断言「第二次起不重复报」 |
| 宿主选项来源（`script.Options.Imports`）无源码位置 | 低 | 回退 `Location.None`，消息文本仍含子句文本；R-用例覆盖 |
| 会议 OPEN QUESTION「同别名不同目标」的期望行为未定 | 无 | 与本项正交（那是**合法**子句的规范化冲突，本项处理**不可解析**子句），仍留会议跟踪 |

## 10. 关卡决策

- **需求关卡：通过**——目标、范围、非目标、验收边界可复述；F2 显式移交。
- **概要设计关卡：通过**——方案方向（宿主侧逐子句降级）、边界（零编译器改动）、影响面、替代方案齐备。
- **详细设计关卡：见 `design-detailed.md` §11**。
