# 任务：脚本顶层 `Dim` 推断撞循环时报诊断而非静默退 `Object`（script-top-level-recursive-dim-inference）——任务计划

> **状态：已收口（F01–F05 全做完；产品码净增删 9/0，新增 28 格；main 亲跑七门全绿 Semantic 5914/5810/104、L2 769/0、档 2 宿主四格对账）。待作者提交 ⇒ commit 号不预填。** 缺陷登记：`..\..\issues\issue-script-top-level-recursive-dim-inference-silent.md`（issue 33，**人的登记，非 agent 输入**）。流水账：`tmp\vortex-logs\script-top-level-recursive-dim-inference\`（`01-investigator-rd-f01` / `03-implementer-rd-f02` / `05-implementer-rd-f03` / `06-main-f04-f05`）。上游账本：`..\..\upstream-merge.md` §2.25(g)（**同文件改写而非新增**）。队列台账：`..\csharp-script-parity-sweep\README.md` **#11**。
>
> **子 agent 禁读 `InternalDevDocs\issues\`、禁改 `InternalDevDocs\tasks\`**：本文件已把症状、两侧读数与判据复述齐全；要改计划报 main，由 main 落笔。

- **一句话**：脚本顶层无 `As` 的 `Dim`，其初始化器**直接或间接引用自身**时，本 fork **静默退成 `Object`、编译期零诊断**（运行期读到 `Nothing`），而 C# 同形状**报错**。要补的是"报不报"，不是改推断本身。
- **来历**：issue 32（顶层 `Dim` 沿用 `Option Infer`）落地后剩下的缺口。**不是 32 的回归**——改前顶层 `Dim` 恒 `Object`，同形状同样零诊断。

## 一、两侧实测读数（已运行，main 亲跑；这就是判据的全部事实基础）

同一棵树成文件（`.vbx` / `.csx`）才是本形状；逐条 REPL 提交各自成树，永远形不成循环。

| 形状（同树两行） | C# `csi 5.10.0-1.26380.3` | VB Debug `vbi`（18:44 构建 ＞ 15:57 源改动） |
|---|---|---|
| `Dim a = b` ／ `Dim b = a`（互指） | `(1,5): error CS7019: 无法推理"a"类型，因为其初始值设定项直接或间接地引用定义。`（exit 1） | **零编译诊断**；运行期 `NullReferenceException` 栈顶 `<Initialize>` |
| `Dim a = a`（自指） | 同一条 `CS7019` | **零编译诊断**；运行期 NRE |
| `Dim a = 5` 之后 `a.Length`（对照） | `(2,28) error CS1061："int"未包含"Length"的定义` | `BC30456："Length"不是"Integer"的成员` ⇒ **已对齐，非本任务范围** |
| `Dim a = b` ／ `Dim b = 5`（非循环前向，对照） | 运行输出 `Int32 val=0` | 运行输出 `Int32 val=0` ⇒ **已对齐，非本任务范围** |

读数判读：若真推断成值类型，`a.GetType()` 会打出 `Int32` 而非 NRE；若是错误类型，编译期就会报错。"零诊断 + NRE"合起来定住的是**静默退 `Object`**。

**普通 VB 不受影响、且循环在普通上下文根本形不成**：方法体内 `Dim a = b`（`b` 未声明）实测 **`BC32000`「局部变量"b"在声明之前不能被引用」**，`Option Explicit` 开／关两档同判 ⇒ 与 Explicit 无关，是作用域报点。分叉点正落在"顶层 `Dim` 提升为脚本类字段、成员查找不看声明顺序"这一脚本特有机制上。

## 二、根因与落点

- **VB 现状**：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb` 的重入守卫 `_computingScriptFieldType`（声明 `:24`、读 `:118`、置位/复位 `:142`/`:170`）命中时 `Return Nothing` ⇒ 退回旧的 `Object` 兜底，**不报任何东西**；且试绑初始化器时诊断被丢进 `BindingDiagnosticBag.Discarded`（`:146`）。
- **C# 对偶**：`Compilers\CSharp\Portable\Symbols\Source\SourceMemberFieldSymbol.cs` 的 `fieldsBeingBound.ContainsReference(this)` 分支报 `ERR_RecursivelyTypedVariable`（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:1165` ＝ 7019），随后落 `CreateErrorType("var")`。
- **VB 已有现成报点**：`ERR_CircularInference1` ＝ **BC30980**，文案 `Type of '{0}' cannot be inferred from an expression containing '{0}'.`，唯一活报点 `Binder_Expressions.vb:3154`（服务局部推断）。

### 落点裁定（main 按 D7「(c) 的处置」自动裁，不回给人）

- **选：甲** —— 守卫命中即报既有码 **BC30980**，零新码、不动 `VBResources.resx`、不占新 BC3xxxx。
- **否：乙**（新增码或改写文案以覆盖互指） —— 需要走提案占新码，或动上游同名文件 `VBResources.resx`（`upstream-merge.md` 口径下要记账并增加合并冲突面）；而 C# 侧对"直接／间接"两种循环**也只用一个 CS7019**，说明要对齐的是"报不报"这一语义强度，不是措辞精度。
- **明示接受的代价**：互指形状的文案偏松（`a` 的初始化器里并不含 `a`）。记在本文件与 issue 33，不静默。
- **D7 三问**：①VB 有现成表达（BC30980）②判据在本仓树内（`ErrorCode.cs:1165` + `SourceMemberFieldSymbol.cs`）且两侧均已实跑 ③不落在 VB 专有概念（循环推断与 `Handles`/`Module` 无关）⇒ 可移植，自动裁。

## 三、可行性（RD-F01 已证，2026-09-24；main 已独立复核锚点）

甲的成立前提＝"字段类型计算期报出的诊断能到达用户、位置正确"。F01 四问答案（档位逐条标）：

1. **出口＝通（档 2 宿主实跑）**。旁证探针 `Dim x As NoSuchType` 写在脚本顶层、**该字段全文无人引用**，仍报 `BC30002: 未定义类型"NoSuchType"。`，波浪线经字节级测量落在 `NoSuchType` 本身（`leading_spaces=9 tilde_count=10`，非标识符、非整行）；引用两次只报一条（不重报）、两个坏字段各报各行。结构侧对齐：`SourceMemberFieldSymbol.vb:79-92` 的 `Type` getter 用同一个 bag 走 `ComputeType → GetDeclaredType`，落库端 `SourceModuleSymbol.vb:819-846` → `:1019-1032` 写进 `sourceFile.DeclarationDiagnostics`（即 `Compilation.GetDiagnostics()` 的面）。
2. **BC30980 不被过滤（档 3 审查）**。`ErrorFacts.vb:550` 属于 `IsBuildOnlyDiagnostic`（`:13`）的**第二个 Case**（`:32` 起 → `:1566 Return False`；第一个 Case `:19-27` 才 `Return True`）⇒ BC30980 被显式分类为**非 BuildOnly**，`SemanticModel.GetDiagnostics()` 会吐出、IDE 面不隐藏。`WRN_NextAvailable = 42600 > 30980`，前置早退分支不适用。全仓除生成物外仅 5 个文件提到该 id，无抑制名单点它名。
3. **实参要换（档 3 + 档 2 旁证）**。`Binder_Expressions.vb:3154` 现传 `localSymbol As LocalSymbol`，`ReportDiagnostic` 通路本身与实参类型无关（`Binder.vb:935 ParamArray args As Object()`）。**但字段符号进 VB 文案会渲染成 `Private a As Integer`**（旁证 `q3b`：`BC30260 … 已在此 type 中声明为"Private a As Integer"`；根因 `Symbol.vb:873 ToString()` ＝ `VisualBasicErrorMessageFormat`）⇒ 直接传 `Me` 会掉出 `Type of 'Private a As Object' cannot be inferred…`。**结论见下方实现约束①**。
4. **既有钉桩＝命中 0 处（档 3，普查 187 文件 / 6 条检索式，留档 `tmp\probes-cyc\rd-f01\filelist.txt`、`scriptfiles.txt`）**。没有任何用例把"脚本顶层自指／互指 ⇒ 零诊断"当期望值，所以无回收义务（RD-D 因此只剩"过时 prose"一项，见 test-plan §四）。不许动的近邻钉：`VariableTypeInference.vb:20`（普通体内自指 ⇒ 已钉 `BC30980`，波浪线在第二个 `i`）、`同文件:52`（普通体内互指 ⇒ **只有** `BC32000`，即 RD-C/C2 的对偶锚点）、`BindingErrorTests.vb:11557/11577/11609/11634/11657/11682`（该文件 `TestOptions.Script` 出现 0 次 ⇒ 全非脚本）。

### 由 (1)(2) 反证死的两条实现约束（F02 必守）

- **① 报点只能落在 `TryComputeScriptFieldType` 的守卫那一支**，不许包到 `ComputeType` 外层：`SourceModuleSymbol.vb:827-844` 里，若 `_lazyType` 已被链上更内层的 frame 存好，外层那个非空 bag 分支**整块跳过、诊断静默丢失**。⇒ 甲选的位置没有第二个可选点。
- **② 实参传 `Me.Name`（String），不传符号**，使文案与局部那侧逐字同式（`Type of 'a' cannot be inferred from an expression containing 'a'.`）。传符号不崩但掉价，且会让 F03 的 `.WithArguments("a")` 因文案不同而红。
- **③ 一个「连通循环分量」只产出一条诊断**，落在该分量中**第一个被计算类型**的那个字段上（不一定是名字排在最前或书写最前的那个——RD-F03 的 `A2b` 实测到"报点跟随先被绑定的字段"）。旗标是"每个字段符号一份"，链上最内层 frame 命中一次。两个互不相干的循环 ⇒ 两条（`X3`）。与 C# 实测（互指只有一条 `CS7019`，指 `a`）一致 ⇒ **不是缺陷**。test-plan 的 A2/A4 已按此改成"先自 dump 读数再定桩"。
- **④ `Option Infer Off` 天然不命中守卫**（`:140` 在置位之前就 `Return Nothing`）⇒ test-plan B2 由构造保住，不需要额外旗标。

### F01 顺带挖出的现状事实（比 §一 记的更宽）

循环形状若**全文无人引用**，今天是 `exit=0`、**零字节输出**（`q1d-cycle-nowrite.out` / `q1e-self-nowrite.out`）——不必跑到 NRE 就完全静默。修复后这一格必须报错（test-plan **A6**）。

## 四、范围内

| # | 单元 | 说明 |
|---|---|---|
| F01 | 取证（只读 + 探针，**不改产品码、不构建**） | ①上述可行性三问的实测答案；②`ErrorFacts.vb:550` 清单性质；③BC30980 的 `{0}` 传参类型与在字段符号上是否可用；④grep 既有绿色用例是否把"顶层自指／互指 ⇒ 零诊断"钉成期望值（含 `Compilers\VisualBasicSemanticTest\**`、`Scripting\VisualBasicTest\**`），列出**文件:行** |
| F02 | 实现（产品码） | `SourceMemberFieldSymbol.vb` 守卫命中 ⇒ 报 BC30980；诊断走类型计算期出口而非 `Discarded`；**判据单一来源**（若同一"是否循环"判据要在两处用，抽一个谓词，不许留两份相似特例） |
| F03 | 单元测试 | 见 `test-plan.md`：RD-A 正格（断具体码 + 位置）、RD-B 反例锁、RD-C 非脚本免疫、回收 F01④ 找到的桩 |
| F04 | 回归（**由 main 跑**） | 七门 + `Scripting\VisualBasicTest` `-automated` 直跑 + 重建 Debug 宿主后跑 RD-H 两格（`tmp\probes-cyc\p1-cycle.vbx` / `p6-self.vbx` 转报错） |
| F05 | 账本（main 落笔） | issue 33 转 Fixed（commit 不预填）；`upstream-merge.md` 登记（`SourceMemberFieldSymbol.vb` 是上游同名文件）；队列 #11；`spec\spec-scripting-dialect.md` 顶层 `Dim` 一节补循环形状（**先英文正本、后 zh-CN**） |

### 非范围

- 不改 32 已落地的推断主体（非循环形状一律保持现状，含 `Int32`/`0` 那格前向引用语义）。
- 不引入新诊断码、不改 `VBResources.resx`／xlf 文案。
- 不动普通方法体的局部推断与 `BC32000`。
- 不碰 `Parser\**`、`OverloadResolution.vb`、`Binder_Initializers.vb`（其它已收口任务的面）。
- 不建提交、不 `git add`（暂存区归作者；工作树现处"多数条目已暂存"是既成事实，别去动它）。

## 五、非脚本面免疫论证（硬约束，F02 的 Storage 字段必须复述并自证）

本仓规矩：改在"脚本与普通编译共用"的代码里，必须写出开关条件并论证普通 VB 走的分支逐字不变（只说"全量测试绿了"不算论证）。

- 本任务的门：**`ContainingType.IsScriptClass`**（定义在 `SourceMemberContainerTypeSymbol.vb:1295-1300` ＝ `DeclarationKind.Script OrElse DeclarationKind.Submission`；基类 `NamedTypeSymbol.vb:682-686` 恒 `False`）。新增报点**只能**落在 `TryComputeScriptFieldType` 内部，而该函数的全部入口已被 32 的 `IsScriptClass` 门 + 七条形状闸包住。
- ⇒ 普通类／模块的字段（语法上必须写 `As`）与错误恢复路径**不可能到达**新报点；`Return Nothing` 退 `Object` 的旧行为在"命中守卫"之外的一切路径上保持不变。
- 验证义务：RD-C 至少一格直断"非脚本编译里同形状仍按普通 VB 报 `BC32000`／普通字段一字不变"。

## 六、Resonance facet（新代码要贴合的近邻样本，逐条有当前证据）

- **必选·错误处理**：诊断在符号层用 `diagBag.Add(...)` 或 `ReportDiagnostic`，位置取 `Syntax`/`GetLocation()`，照 `Binder_Expressions.vb:3154` 的既有 BC30980 报点写法（同码同措辞来源）。
- **必选·架构**：不改 32 的返回契约（`TryComputeScriptFieldType` 仍以 `Nothing` 表示"退回旧路"），新行为只在守卫那一支加分支 ⇒ 保持"推断失败＝退旧路"这一既有不变式，避免调用方 `ComputeType` 出现第三种返回。
- **必选·测试**：断具体诊断码＋位置，不用"编译失败"当断言；正格必须配同容器／同形状的反例锁（本仓 D7 口径连带的正向对照要求）。
- **优先·命名**：新谓词若需要，命名沿用项目词（`IsScriptClass`／`TryCompute...`），不发明近义名。
- **避免**：不留两份"是否循环"的特例判断；不为让互指文案更准而顺手改 resx（已被 §二 否掉）。
- **反复摩擦（来自本仓流水账，安全摘要）**：①行号锚在跨文档引用里易腐，改前重新核对；②诊断正文按机内 ANSI 码页输出，`iconv -f GBK` 会静默吞行 ⇒ 用 CP936；③Git Bash 把 `/xxx` 选项改写成路径 ⇒ `MSYS_NO_PATHCONV=1`；④子 agent 跑全量回归曾两次烧掉 4,900 万/4,400 万 token 且没留日志 ⇒ 只跑定向过滤、长命令重定向到文件读尾巴。

## 七、风险与停止上报

- **已消解（原最大风险）**："类型计算期的诊断出口到不到达用户"——§三 (1) 已用旁证实跑判**通**，且 §三 反证死约束①把报点位置唯一化。
- **仍活**：
  - **一条循环只出一条诊断**可能被验证者当成漏报而误打回 ⇒ 先读 §三 约束③ 与 `README.md` §一 的 C# 读数（互指也只有一条 `CS7019`），A2/A4 已改成自 dump 定桩，不许预设条数。
  - **过时 prose 钉**（F01 发现，属 32 的残留、不属本任务实现面）：`Scripting\VisualBasicTest\ScriptModeApiSurfaceConformanceTests.vb:224-227` 仍写"顶层 `Dim x = ...` 不推断类型"，与 `ScriptModeTopLevelInferenceTests.vb:13-18` 的改判矛盾。**归 main 在 F05 收口时改，实现者/验证者不要动**（避免与本任务的 diff 混在一起）。
  - 构建面独占（并发构建会 `BC2012` 锁 dll）：F02/F03 期间不派第二个动构建面的 agent；agent 只跑定向 `--filter`，全量归 main。
