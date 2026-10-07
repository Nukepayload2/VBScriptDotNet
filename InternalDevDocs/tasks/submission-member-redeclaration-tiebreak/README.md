# 任务：跨提交同名 Method/Property 按提交链位置择一（submission-member-redeclaration-tiebreak）——任务计划

> **状态：待派工**（等构建面腾空；现由 `auto-property-top-level-gate` 的接手者占用）。流水账在 `tmp\vortex-logs\submission-member-redeclaration-tiebreak\`。缺陷登记：`..\..\issues\issue-shadowing-across-submissions-reports-overload-error.md`（issue 26，已带「D7 裁定」节）；队列台账 `..\csharp-script-parity-sweep\README.md` **#8**。**子 agent 禁读 `issues\`**——症状、对照与判据在本文件与 `test-plan.md` 内复述自足。

- **一句话**：同一条提交链里重声明**同名同签名**的 `Function`/`Property`，VB 报 `BC30521`「重载决策失败」；与 C# 脚本方言对等的行为是**零诊断且最新定义者独占**。
- **C# 判据（本仓树内，档 3 读码；日志 `tmp\vortex-logs\csharp-script-parity-sweep\01-dig-sweep-26-18.md`）**
  1. **决策层兜底择一**：`Compilers\CSharp\Portable\Binder\Semantics\OverloadResolution\OverloadResolution.cs:2504-2523`，注释逐字 *"Otherwise: Position in interactive submission chain. The last definition wins."*，按 `GetSubmissionSlotIndex()`（共享设施 `Compilers\Core\Portable\Compilation\Compilation.cs:524-534`）比大小直接判胜 ⇒ **无歧义诊断**。
  2. **查找层只对 Method/Indexer 继续合并**：`Binder\Binder_Lookup.cs:435-441`（`result.MergeEqual(...)` 后 `if (!IsMethodOrIndexer(firstSymbol)) break;`，定义 `:1390-1393`）⇒ 属性/字段就地停止、最近提交独占。
  3. VB 现状：查找层与 C# 同构（`Binding\Binder_Lookup.vb:893 If Not first.IsOverloadable Then Exit Do`），差别只在判据宽窄——`Symbols\SymbolExtensions.vb:136-154` 的 `IsOverloadable` **把 Property 也算可重载**；决策层 `Semantics\OverloadResolution.vb` 全文 `Submission` 命中 **0** ⇒ 无人胜出 ⇒ `Binding\Binder_Invocation.vb:1811-1869 ReportUnspecificProcedures` 报 BC30521。

## 一、判据（F01 的实现边界）

1. 跨提交重声明同名同签名 `Function` ⇒ **零诊断**，调用命中**最新**定义（值断言：前一版 `x + x`、后一版 `x * x` ⇒ `M(5)` 得 `25`）。
2. 同形状用在 `Property` 上 ⇒ 与 1 同判。**注**：这是按 C# 的**结果形状**对齐，不是照抄判据——可重载属性是 VB 的语言特性（C# 没有），为抄 `IsMethodOrIndexer` 而砍掉它属反向破坏。
3. **真实跨提交重载不受影响**：异签名（`M(x As Integer)` / `M(s As String)`）仍各自命中（基线实测 `100` / `200`）。
4. **同一次提交内**的同签名重复不得被放过：两候选必须分属**不同** `DeclaringCompilation` 且 slot 不等才择一。
5. 不新增诊断码、不改 resx/xlf；`Shadows` / `Overloads` **不参与也不报错**（现状即如此）。
6. 与 C# 无需分叉条目：唯一差别是第 2 格的 kind 集合，写在本文件即可，不进 `spec` 的分叉清单。

## 二、范围内

| # | 单元 | 说明 |
|---|---|---|
| F01 | 实现 | `Compilers\VisualBasic\Portable\Semantics\OverloadResolution.vb` 的 `CombineCandidates`（`:4419-4436` 之后，与 C# 同一 tie-break 链位置）新增「两候选都属 `TypeKind.Submission` 且分属不同编译 ⇒ `GetSubmissionSlotIndex()` 大者胜」。**否定**查找层截断（改 `Binder_Lookup.vb:890/:912` 会砍掉判据 3） |
| F02 | 单元测试 | `test-plan.md` §一 G1–G7 + 反例锁 R1–R4；宿主面（`Scripting\VisualBasicTest`）与编译器面（`Compilers\VisualBasic*Test`）双侧都要有 |
| F03 | 回归 | 七门 gate + `Scripting\VisualBasicTest` 直跑 `-automated`（MTP，`dotnet test` 静默跑 0 个）+ 重建发布版宿主复跑 `tmp\probe-sweep\p8*` |
| F04 | 账本 | issue 26 转 Fixed（commit 不得预填）；**测试回收**：`Scripting\VisualBasicTest\ScriptModeApiSurfaceConformanceTests.vb:89-110`（把 BC30521 钉成期望值、注释含 "with and without an explicit `Shadows`"）改断 `25` + 零诊断；`upstream-merge.md` 入账（`OverloadResolution.vb` 是上游同名文件） |

### 非范围
- 不动 `Binder_Lookup.vb` 的合并语义与 `always overload` 注释。
- 不碰解析层（`auto-property-top-level-gate`）、不碰 `Binder_Expressions.vb`（`script-class-explicit-keyword-parity-revert`）、不碰脚本类基类型线（`submission-object-member-lookup`）。
- 不实现 `Shadows` 在提交链的语义（VB 专有设计，须另走 `proposals\`）。

## 三、风险与停止上报
1. `GetSubmissionSlotIndex()` **懒分配**、对无代码提交可能返回同一值（`Compilation.cs:526-534` 带 `TODO (tomat): remove recursion`）⇒ 相等路径按判据 4 显式处理；并防 VB 侧 `Debug.Assert(slotIndex >= 0)` 类假设（`Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:74`）。
2. 若 tie-break 需在并发/多树 `#Load` 场景解释 slot 含义，先小范围实跑确认与提交链一致；不一致 ⇒ 停手上报，不猜。
3. 构建面独占：同一时间只有一个 agent 重建/跑测试（BC2012 锁 dll）。

## 四、落点复核（档 3 读码，2026-09-24；收 §2.25(c) 未闭合项②）

- **C# 判据的落点不是"合并候选"，是"比较两个候选"**：那段 "Position in interactive submission chain. The last definition wins." 在 `Compilers\CSharp\Portable\Binder\Semantics\OverloadResolution\OverloadResolution.cs:2506-2523`，位于 `BetterFunctionMember<TMember>`（函数头 `:2136`）内、"more specific parameter type"（`MoreSpecificType`，`:2495`）之后、custom-modifier 计数之前。⇒ C# 是**决策末段**兜底。
- **本 fork 落点在 `CombineCandidates`**（`Compilers\VisualBasic\Portable\Semantics\OverloadResolution.vb:4279-4500`，插入段 `:4438-4476`）＝ VB 的"同名候选合并"步。对 issue 26 的真实缺陷形状（**同名同签名**跨提交）这是 VB 里唯一会下判决的地方，故行为对等成立；`CombineCandidates` 之后两个同名候选若签名不同则各自存活，走 `BetterMethod`/`ApplyTieBreakingRules`（`:1789`）。
- **仍存在的潜在缺口（候选形状已推出，未实测）**：两提交里**同 arity、不同参数类型**且互不更优的一对，例如 `#0: Sub M(a As Integer, b As Long)` 与 `#1: Sub M(a As Long, b As Integer)`、调用 `M(1, 2)`。按上面的读码，**C# 会由末段兜底选较新提交**，而 VB 目前预期仍是 `BC30521` ⇒ 若实测确认，就需在 `ApplyTieBreakingRules`（或其"全部规则都未判出胜者"的末段）**再补一处同源判据**，且必须与 `CombineCandidates` 那段**收敛成一个谓词**（不许留两份相似特例当技术债）。
- **反例义务**（新规则不得越界）：同一提交内的同签名对（G5）、异签名但一方明确更优的对（G6 `100/200`）、非脚本普通类的真歧义（R3 `BC30521`）三格必须逐字不变。
- **SW-TB-F04（下一片段）**：① 用上面那对形状在**当前码**上取读数（档 1 单测即可，别改产品码先）；② 读数确认为 `BC30521` 后，把择一判据提取为 `Private Shared Function TryGetSubmissionSlotWinner(...)` 一处定义、两处调用，并补等值/负槽位断言（§2.25(c) 缺口①）；③ 若读数显示 VB 在该形状上另有规则先行判出胜者 ⇒ 不改码，把结论写回本节并结案该缺口。

## 五、SW-TB-F04 实测结论（推翻 §四的候选缺口）

- 指定形状（`#0 M(Integer, Long)` / `#1 M(Long, Integer)`，调用 `M(1, 2)`）在 VB 读 `BC30521`（两候选各注"不是最适合"，槽位 1/2）；**同形状在 C# 实跑读 `CS0121`**（探针 `tmp\f04-cs-probe\Program.cs`，用本仓 CSharp 编译产物；读数 `tmp\vortex-logs\submission-member-redeclaration-tiebreak\04-implementer-f04.md:33`）⇒ **不是净分歧，§四第三行的猜测被推翻**，不新增第二落点。
- 原因：C# 那段被"参数类型同一性"前置门挡在与 VB `signatureMatch` 相同的范围内（`OverloadResolution.cs:2216-2230`/`:2312`/`:2352` 先返回）。VB 侧这类对被放进不同的 equally-applicable 桶（`:2588+`）⇒ 末段第二落点**永不触发**，实测后撤销。
- 已落地的只有**保形提取**：内联块 → `TryGetSubmissionSlotWinner`（`OverloadResolution.vb:1934-1981`，唯一调用点 `:4495-4497`），配 4 格直断谓词（槽位相等 / 非提交 / 双方序取大者 / 无代码提交不推进槽位）+ 2 格形状锁；该类 9→15 格，Semantic 基线 5862/5758/104。
- 「等槽位候选对经公共查找层不可构造」三条路径见 `04-implementer-f04.md`；其中"元数据载入的提交类型不带 `TypeKind.Submission`"只到**读码档**（未实测），不得当结论引用。

## 六、"槽位相等/不可用的候选对"经公共查找层**不可构造**的结论（SW-TB-F04；§五 的自足版，不依赖 `tmp\` 流水账）

**结论**：无法在公共 API（脚本提交查找层）下造出"两个候选 `TypeKind.Submission` 且 `GetSubmissionSlotIndex()` 相等（或不可用）"的一对，因此 `TryGetSubmissionSlotWinner` 的"槽位相等→无胜者""非提交→无胜者"等守卫分支**不能经端到端形状触发**。这不是漏测——改由**谓词层直断**覆盖（见下"替代方案"）。三条尝试路（读数存 `tmp\vortex-logs\submission-member-redeclaration-tiebreak\04-implementer-f04.md`，本处为结论复述）：

1. **让某次提交不推进槽位**（`Compilation.cs:526-531`，`HasCodeToEmit()=False`）——**已实跑**：`Imports System`-only / 空提交确不推进槽位，`importsOnly.slot == withCode.slot == 1`（槽位等值是真状态）；**但**这种提交不声明任何成员 ⇒ 拿不出候选对。此路已做成断言 `F4_SubmissionWithoutCodeToEmit_KeepsThePredecessorSlot`。
2. **两条独立提交链合进同一次查找**——**已实跑**：两链链头槽位都是 1（`otherChainFirst=1`），但 `CreateScriptCompilation` 只接受**单个** `previousScriptCompilation`，无并行链公共 API，两链**永不同框** ⇒ 端到端构造不出等槽位候选对，只能在谓词层直接喂两个同槽位候选。
3. **取 `DeclaringCompilation` 为 `Nothing` 的元数据提交类型去撞"槽位不可用"**——**仅读码（✎ 未实测）**：`TypeKind.Submission` 只来自 `DeclarationKind.Submission`（`SourceMemberContainerTypeSymbol.vb:157-159` ← `DeclarationTreeBuilder.vb:140`，且需 `Compilation.IsSubmission`）；元数据里的 `Script`/`Submission#N` 类型**不是** `TypeKind.Submission`，会被谓词第一道门（两候选皆须 `Submission`）挡下，走不到槽位比较。**此路要 Emit + 引用两轮装置、收益低于代价，本批未实跑**，故这条只作读码佐证，不作已验证结论。

**替代方案（不算漏测的理由）**：把择一判据提取为 `Friend Shared Function TryGetSubmissionSlotWinner`（`OverloadResolution.vb:1934-1981`），配 IVT（`Microsoft.CodeAnalysis.VisualBasic.vbproj:60`）让语义测试装配**直接喂候选**测守卫——四格直断（等槽位→无胜者 / 非提交→无胜者 / 双方序取大者 / 无代码提交不推进槽位）覆盖了端到端不可达的等值/负值分支。`Friend`（非 `Private`）的可见性选择理由见流水账（IVT 测试可见、对外仍非 API）；若要严格回 `Private` 可用反射替代，本批未取。

**判据状态**：SW-TB-F04 已收口；队列 #8 / issue 26 的"择一规则余下细节"以本节为准（结论自足、不随 `tmp\` 清除而丢失）。第 3 条读码路若将来要升级为已验证，需补一轮 Emit+引用装置实跑。
