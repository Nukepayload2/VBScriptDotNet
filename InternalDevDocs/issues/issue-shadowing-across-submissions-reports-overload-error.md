# 跨提交同名重声明报 `BC30521`「重载决策失败」：容器把两个提交的同名成员当重载集，`Shadows` 无效

- **状态**：**In Progress**（2026-09-22 按 `../decisions.md` **D7** 完成取证并**自动裁决为「可移植、改」**，不再"交用户裁决"：目标形状＝跨提交同名 `Function`/`Property` 重声明**合法、最新定义者独占、零诊断**。取证日志 `tmp\vortex-logs\csharp-script-parity-sweep\01-dig-sweep-26-18.md`；裁定与两处更正见下节「D7 裁定」；实施另行立项 `tasks\submission-member-redeclaration-tiebreak\`，本文件不改产品码）
- **发现日期**：2026-09-16
- **发现场景**：`../tasks/script-mode-coverage-parity/` 的 U9（脚本 API 面剩余缺口）。触发点是该单元的**格 1**：
  C# 基线 `TestBranchingSubscripts`（`{{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs:452`）在两个分支链里
  **重声明同名成员并期望新声明遮蔽旧声明、算出 25**；VB 侧同形状报 `BC30521`。登记依据同任务 `README.md`
  **§八 义务 1**（实施期新发现的缺陷同样按此登记）与 `design-detailed.md` §U9 的 **pass 条件 5**
  （「若某格暴露产品缺陷 ⇒ **停手**，按 §U1 的流程单独收口（建 issue → 判定 → 修复 + 用例），**不混在补测单元里改产品码**」）——
  U9 首轮**未停手**（把它当「已登记差异」钉住了），本 issue 即补做停手收口，**本轮不改产品码**。
- **影响面**：**脚本容器**（提交链）的成员绑定 —— `Script.ContinueWith` / `ScriptState.ContinueWithAsync` /
  `vbi` 的 REPL 每一个提交。**普通 VB 编译不受影响**（同一形状给 `BC40003` 警告、正常编译执行，读数见下表 `A6`/`A7`）。
- **严重度**：中。不崩、不静默产出错误值——它**报诊断**（属作者判定原则的两条合法出口之一），真正的缺陷是
  ① **用户没有出路**：唯一能表达「我要遮蔽」的 `Shadows` 修饰符（`A2`）在这条路径上不被咨询、无效；
  ② **严重度被容器升级**：同一个遮蔽关系写在普通编译里只是 `BC40003` **警告**且照常算出 25（`A6`），
  跨提交就变成**硬错**（`A1`）。⇒ 一类**合法形状在脚本容器里写不出来**。
  （**诊断文案不是「完全不指向病灶」**：它把两个候选的**全名**一起列出（`Submission#1.M` / `Submission#0.M`），
  读者能看出冲突跨提交；见「性质判定」第 1 条的收紧。）

## 触发面

**自己的实测读数**（本轮复跑；探针 `tmp\u9-fix\probe\Issue26.cs`，读数 `tmp\u9-fix\i26.txt`——均在
`<项目根>` 的 `tmp\` 下，该目录已 git-ignored，不入库）。下表标签（`A1`/`A2`/`A3`/`A6`/`A7`/`D4`/`D4b`/`D3s`）
**逐字取自探针输出**，与 `i26.txt` 同行标签一一对应：

| # | 输入形状 | 读数 | 三态 |
|---|---|---|---|
| A1 | 脚本容器，两个提交，第二个重声明 `Function M(x As Integer)`（**无 `Shadows`**），调用 `? M(5)` | `[Error:BC30521]` | **实锤** |
| A2 | 同上但写 **`Shadows Function M(...)`** | `[Error:BC30521]`（**与 A1 逐字同结果**） | **实锤** |
| A3 | 对照：第二个提交声明**不同名**成员 `M2` 并调用 `? M2(5)` | `[]`（零诊断） | **实锤** |
| A6 | **普通编译**：`Derived Inherits Base`，`Derived` 里 `Public Function M(...)` 遮蔽基类成员，调用 `(New Derived()).M(5)` | `[Warning:BC40003]`，**`M(5) = 25`**（能编译、能执行） | **实锤** |
| A7 | 同上加 `Shadows` | `[]`（**零诊断**），`M(5) = 25` | **实锤** |
| D4 | 顶层**字段**跨提交重声明（`Dim f As Integer = 1` 然后 `Dim f As Integer = 2`） | `[]` | **实锤** |
| D4b | 同上加 `Shadows` | `[]` | **实锤** |
| D3s | **脚本容器、同一提交内**两个真类 `Derived Inherits Base` 同形状 | `[Warning:BC40003]` | **实锤** |

`A1` 的完整诊断消息（逐字，含两个候选）：

```
重载决策失败，因为没有可访问的“M”最适合这些参数:
 “Public Function Submission#1.M(x As Integer) As Integer”: 不是最适合。
 “Public Function Submission#0.M(x As Integer) As Integer”: 不是最适合。
```

诊断的**位置在调用行**（`? M(5)`），不在两个 `Function M` 的声明行上。

**对照的意义**：`A6` 与 `A1` 是**同一个遮蔽关系**，写在普通 VB 里只是警告且算出 **25**，拆到两个提交里就变成
**硬错**；`A3` 证明「第二个提交加成员」本身没问题（换名就行）；`D4`/`D4b` 证明**字段**跨提交重声明**不报**——
⇒ 触发面是「**成员是方法**（可重载的 kind）」这一具体形状，不是「跨提交重声明」的泛面。`D3s` 证明**容器本身**
（不管是不是跨提交）不引入这个码。

## 根因

**读码（自己读，双锚）**：`Compilers\VisualBasic\Portable\Binding\Binder_Lookup.vb` 的
**`LookupInSubmissions`（声明于 `:858`）** 是提交类（`TypeKind.Submission`）成员查找的实现；
它的分派点在**同文件 `:583-584`**（`Select Case type.TypeKind` 的 `Case TypeKind.Submission` →
`LookupInSubmissions(...)`）。该函数沿 `PreviousSubmission` 链**逐提交**查找，并把每一提交命中的同名成员
**并入同一个 lookup result**：

```
:889-890   ' always overload (ignore Overloads modifier):
           result.MergeOverloadedOrPrioritized(submissionSymbols, checkIfCurrentHasOverloads:=False)
:911-912   ' always overload (ignore Overloads modifier):
           result.MergeOverloadedOrPrioritized(submissionSymbols, checkIfCurrentHasOverloads:=False)
:916       submission = submission.PreviousSubmission
```

1. **合并语义**：`:890` 的注释逐字是 `always overload (ignore Overloads modifier)` —— 两个提交的同名方法
   进**同一个重载集**。`Submission#1.M` 与 `Submission#0.M` 之间**没有继承关系**（提交类的 `BaseType` 是
   `Nothing`——`Compilers\VisualBasic\Portable\Symbols\Source\ImplicitNamedTypeSymbol.vb` 的 **`MakeDeclaredBase`**
   （声明于 `:51`）在 **`:59`** 逐字 `Return If(Me.TypeKind = TypeKind.Submission, Nothing, baseType)`；
   提交类之间的链接靠的是 `PreviousSubmission` 这条**独立于继承的**链），但在查找结果里它们是**平级候选**。
2. **`Shadows` 不被咨询**：这条路径**没有任何地方读 `Shadows` / `Overloads` 修饰符**（`:890` 的注释自己写明
   「ignore Overloads modifier」）⇒ `A2` 与 `A1` 逐字同结果。
3. **报码点**：重载集有两个平级候选且都不「最适合」，于是走标准的重载决策失败报码
   `ERR_NoMostSpecificOverload2 = 30521`（`Compilers\VisualBasic\Portable\Errors\Errors.vb:412`），
   报在**调用点**（`? M(5)`）。⇒ 诊断文本描述的是「调用点的实参选择失败」，而病灶在「两个提交的同名成员
   被放进了同一个重载集」。
4. **为什么字段不中招**：字段不是 `IsOverloadable`（`:893` 的 `If Not first.IsOverloadable Then Exit Do`、
   `:899` 的 `lookingForOverloadsOfKind = first.Kind`），字段命中后重载链**当轮就退出**⇒ 遮蔽按「新提交覆盖」
   生效（`D4`/`D4b` 零诊断）。

**与语言侧的同形状对照**：普通 VB 的「派生类成员遮蔽基类同名成员」由**继承链**处理，判据是
`WRN_MustOverloadBase4 = 40003`（`Compilers\VisualBasic\Portable\Errors\Errors.vb:1825`）
——**只是警告**，语义明确（新成员胜出），并且**用 `Shadows` 可以把这条警告也消掉**（`A7` 零诊断）。
⇒ 语言层面「同名成员遮蔽」是这个语言**已经有明确定义**的关系，`BC30521` 在语言侧**只**用于真正的重载歧义。

## 性质判定

**这不是 VB 语言的既有规则，是脚本容器引入的容器行为。**

判据（全部实测）：

1. **语言侧有对应规则**：同形状普通编译给 `BC40003` 警告 + `M(5)=25`（`A6`），语言层面遮蔽关系是**已定义**的。
2. **容器改变了诊断的严重度与身份**：从「警告 + 能编译」变成「硬错 + 编译失败」（`A1` vs `A6`）。
3. **容器把「遮蔽」错当「重载」**：跨提交的两个 `M` 是**两个无继承关系的类**的成员，容器把它们放进同一个
   重载集（根因第 1 条）；这正是 `BC30521` 的判据被触发的直接原因。
4. **用户无出路**：语言给用户表达「我要遮蔽」的唯一写法（`Shadows`）在这条路径上**不被咨询**（`A2`）。

**按作者判定原则（逐字「崩编译器是 bug。要么让它别崩、正常跑；要么报诊断说『脚本不支持这样用』」）它落哪条？**
它**报的是诊断**——属两条合法出口之一。**判定（收紧后，两条）**：

1. **文案部分指向病灶 —— 不是「完全不指向」**：`A1` 的完整诊断消息把两个候选的**全名**一起列出
   （「`Public Function Submission#1.M(x As Integer) As Integer`」/「`Public Function Submission#0.M(x As Integer) As Integer`」），
   读者据此**能看出**冲突横跨两个提交；消息报在**调用行**、说的是实参选择失败，与真实机制（两个提交的同名成员
   被并进同一重载集）**不矛盾**。⇒ 与 `#24`（`issue-string-script-factory-drops-file-encoding.md`）的
   `BC37236`「源文本没有编码」同类——那条被判**文案指向准确**（它逐字描述了内部真实状态）。
   本条的文案同样**没有说假话**，故不宜判成「诊断指向错误病灶」。
2. **真正的缺陷是「无出路」+「严重度升级」**（两条都是实测读数）：
   - **无出路**：语言里能给用户表达「我要遮蔽」的唯一写法 `Shadows` 在这条路径上**不被咨询**（`A2` 与 `A1`
     逐字同结果）⇒ 该形状**没有合法写法**；
   - **严重度升级**：同一遮蔽关系在普通编译里只是 `BC40003` **警告**且照常算出 `M(5)=25`（`A6`），
     在脚本容器里升级为**硬错、编译失败**（`A1`）。

⇒ **判定：它报了诊断（合法出口之一），但 ① 用户无出路、② 严重度被容器升级** ⇒ 判**产品缺陷**
（**弱形态**：不崩、不产错值，但一类合法形状在容器里写不出来）。

**与「已登记差异」的关系**：U9 首轮把它记成「与 C# 基线的真实差异」并**钉住**（用例
`ScriptModeApiSurfaceConformanceTests.vb:97` 的 `BranchingSubscripts_RedeclaringTheSameMember_ReportsBC30521`），
按「以 C# 为设计蓝本（`../decisions.md` **D5**）」的口径这是**不够**的：D5 说的是**落地细节以 C# / csi 为蓝本**，
不是「凡与 C# 不同就记为差异了事」。本 issue 把它**从「差异」升级为「登记在册的缺陷」**，理由见上。
**钉住的那条用例保留**（它是修复的报警线：修复后行为一变，该用例即红）。

## D7 裁定（2026-09-22 只读取证批次；日志 `tmp\vortex-logs\csharp-script-parity-sweep\01-dig-sweep-26-18.md`）

**先说结论**：判**「可移植、改」**，按 D7 自动裁，不再交人工。目标形状＝**跨提交同名 `Method`/`Property` 重声明合法、最新定义者独占、零诊断**。

### 本仓树内的 C# 判据（档 3 读码；D7 前置纪律要求先量到树内锚点）

1. **决策层有兜底择一**（这就是 VB 缺的那一环）：`Compilers\CSharp\Portable\Binder\Semantics\OverloadResolution\OverloadResolution.cs:2504-2523`，注释逐字 *"Otherwise: Position in interactive submission chain. The last definition wins."*，按 `GetSubmissionSlotIndex()`（共享设施 `Compilers\Core\Portable\Compilation\Compilation.cs:524-534`）比大小直接判胜，**不产生歧义诊断**。
2. **查找层只对 Method/Indexer 继续合并**：`Binder\Binder_Lookup.cs:435-441`（`result.MergeEqual(...)` 后 `if (!IsMethodOrIndexer(firstSymbol)) break;`，`IsMethodOrIndexer` 定义 `:1390-1393`）⇒ field/property 就地 break、最近提交独占。
3. **提交类无继承**：`Symbols\Source\ImplicitNamedTypeSymbol.cs:56-64` ⇒ 提交链上既无 CS0108 隐藏警告，也无 `new` 修饰符的用武之地。
4. VB 侧对应面：查找层与 C# 同构（`Binder_Lookup.vb:893 If Not first.IsOverloadable Then Exit Do` ↔ C# `:438`），差别只在判据宽窄——`SymbolExtensions.vb:136-154` 的 `IsOverloadable` **把 Property 也算可重载**（C# 不含）；决策层 `Semantics\OverloadResolution.vb` 全文 **`Submission` 命中 0 次** ⇒ 无择一 ⇒ `Binder_Invocation.vb:1811-1869 ReportUnspecificProcedures` 报 BC30521。

### 对本文件既有内容的四处更正

1. **缺陷面按符号 kind 收窄**：`D4`/`D4b` 与本轮探针都表明 **`Dim`（field）跨提交重声明本已正常**（最新者胜、零诊断、甚至可跨类型改型：`Dim y As Integer = 7` ⇒ `Dim y As String = "hello"` ⇒ `?y` 得 `hello`）。真凶只有 **`Method` + `Property`**。⇒ 本 issue 标题下的「容器把两个提交的同名成员当重载集」须读成「把同名 **可重载** 成员当重载集」。
2. **对等目标不是「降级成 `BC40003`」**：`BC40003 = WRN_MustOverloadBase4`（`Errors\Errors.vb:1825`，报点 `OverrideHidingHelper.vb:441`）是**继承**遮蔽警告，而提交类无基类型 ⇒ 提交链上两侧都**不可能**报它。"与 C# 对等"＝**无诊断**。「严重度被容器升级」作为**事实**仍然成立（普通编译只是警告），但它不是验收值。
3. **方向 A 的落点表述被更正**：原文写「让 `LookupInSubmissions` 停止向上回溯」——**不采纳**。C# 是「**照旧合并候选** ＋ **决策层择一**」，若在查找层截断会砍掉真实的跨提交重载（异签名两形各自命中，本轮实测 `100`/`200`）。⇒ 采纳落点＝`OverloadResolution.vb` `CombineCandidates`（`:4419-4436` 之后，与 C# 同一 tie-break 链位置）新增「两候选分属不同 submission ⇒ slot 大者胜」。
4. **方向 B 作废**：不新增诊断码。理由＝C# 侧无对应分叉需求，且「合法形状应能写」已被决策层择一解决；按 D6，beta 期也不得用「保留重载语义」作挡箭牌。

`Shadows` 修饰符：**保持不参与、也不报错**（A2 的现状不变）。给它加「必须写/写了才生效」的要求属无 C# 依据的 VB 侧自造语义；若日后要让 `Shadows` 在提交链参与，那是 VB 专有设计，须另走 `proposals\`，不属 D7 自动裁范围。

原「两个候选方向」一节按上述保留为**历史决策资料**（其中①「可见性保留多少」的担忧由落点更正自动消解：只在**同签名候选之间**择一，前序提交的其他成员照常可见）。

### 仍属推测 / 实施时必须先证实

| 项 | 态 |
|---|---|
| 「按此修好后 VB 零诊断并取最新定义（`M(5)=25`）」 | **推测**（形状由 C# 判据推得，未实跑）⇒ 实施第一步就是把它做成单测 |
| 新规则必须只作用于**不同** submission 之间（`DeclaringCompilation` 不同且 slot 不等），同提交内同签名重复仍保留现有诊断 | 设计约束，未验证 |
| `GetSubmissionSlotIndex()` 懒分配、对无代码提交返回同一值（`:526-534` 还带 `TODO (tomat): remove recursion`）、以及 VB 侧 `Debug.Assert(slotIndex >= 0)` 类假设（`SynthesizedSubmissionConstructorSymbol.vb:74`） | 风险面，须在实现时量负值/相等路径 |
| C# 侧行为**未实跑**（本仓 `Scripting\` 下无 C# 测试资产、`CSharpTestBase` 无定义）⇒ 全部档 3 | 档位限制 |

**账本义务（D7 测试回收）**：`Scripting\VisualBasicTest\ScriptModeApiSurfaceConformanceTests.vb:89-110` 现在把 BC30521 钉成期望值（注释还写着 "with and without an explicit `Shadows`"）——修复后按新语义改写为**断 25 且零诊断**，并补两条正向对照：异签名跨提交重载各自命中、field 面不受影响。



## 两个候选方向（**历史资料**：写于 C# 判据取证之前，已被上节裁定取代）

- **方向 A（按 D5 对齐语言侧语义：新提交的同名成员遮蔽前序提交的同名成员）**：让 `LookupInSubmissions` 在
  找到**当前提交自己的**同名成员后**停止向上回溯**（或把前序提交的命中降为「仅在没有当前命中的候选时使用」），
  即把跨提交的同名关系按**遮蔽**而不是**重载**处理——与语言侧 `BC40003` 的语义对齐。落点集中在
  `Binder_Lookup.vb:880-917` 的回溯循环；**但要一并决定**：
  ① 前序提交成员的**可见性**保留多少（`ScriptState.Variables` 与「后一提交读得到前一提交的成员」是脚本容器的
  核心能力，不能一起关掉）；
  ② `D4`/`D4b` 的**字段**路径已有行为（新提交覆盖）要不要与之统一表述；
  ③ C# 基线 `ST:452` 期望值 25 要不要作为验收值。
  **代价**：这是**语义变更**（可观察行为变），落 `../decisions.md` **D4** 的提案闸门内。
- **方向 B（保留重载语义，只把诊断换成能指向病灶的新码）**：跨提交同名成员仍按重载处理，但**在回溯阶段**
  检出「当前提交与前序提交各有同名可重载成员」时，改报一个新诊断（按 `#19` / `#23` 的先例新增码的形态），
  文案说明「两个提交里分别声明了 `M`；容器把它们视作重载集，请改名或用 `Shadows`（若 `Shadows` 支持的话）」。
  落点同在 `Binder_Lookup.vb:880-917`。**注意这个方向要求先解决 `A2`**：诊断若建议用户写 `Shadows` 而
  `Shadows` 无效，会把用户绕回同一个错误——所以方向 B **至少要**让 `Shadows` 生效（那就带上了方向 A 的一部分）。
  **代价**：新增诊断码（`Errors.vb` + `VBResources.resx` + 13 份 `xlf`），落 `../decisions.md` **D4** 闸门内。

**两个方向都不在本补测任务实施**（`design-detailed.md` §U9 pass 条件 5 明确「不混在补测单元里改产品码」；
且方向 A 触及本仓 `README.md` **§一 非范围**的「不为『C# 有、VB 没有』的语言特性造 VB 实现」边界）。

## 三态标注

| 断言 | 三态 | 依据 |
|---|---|---|
| `BC30521` 是脚本容器引入、非 VB 语言规则 | **实锤** | `A6`（普通编译 `BC40003` + `M(5)=25`）与 `A1`（脚本容器 `BC30521`）是**同一遮蔽关系**的两种结果，均本轮实跑 |
| 根因是 `LookupInSubmissions` 沿 `PreviousSubmission` 逐提交 `MergeOverloadedOrPrioritized` | **实锤（读码）** | `Binder_Lookup.vb:858`（声明行）/`:583-584`（分派）/`:889-890`、`:911-912`（`always overload` 注释与合并调用）/`:916`（回溯） |
| `Shadows` 在该路径上不被咨询 | **实锤** | `A2` 与 `A1` 逐字同结果；读码：回溯循环内无任何 `Shadows`/`Overloads` 读取 |
| 字段路径不受影响（遮蔽生效） | **实锤** | `D4`/`D4b` 零诊断；读码 `:893`/`:899` 的 `IsOverloadable` 退出 |
| 「用户无出路 + 严重度升级」是**需要修**的理由 | **实锤** | 无出路：`A2` 与 `A1` 逐字同结果（读数）；严重度升级：`A6` 只是 `BC40003` 警告且 `M(5)=25`、`A1` 是硬错（读数）。**判定**的取舍部分（是否值得为它改产品）为**推测**，**交用户裁决** |
| 诊断文案**部分**指向病灶（列出两个候选全名 ⇒ 不是「完全不指向」） | **实锤** | `A1` 的完整消息逐字含 `Submission#1.M` 与 `Submission#0.M` 两个候选（读数表下引文）；与 `#24` 的 `BC37236` 先例同类 |
| 方向 A/B 的代价估算 | **推测** | 两条路线都只做了落点定位（`Binder_Lookup.vb:880-917`），未做实现试验；方向 A 的①可见性取舍**需要先做隔离实验**才知影响面 |

## 相关

- 钉住该差异的用例（修复的报警线）：`Scripting\VisualBasicTest\ScriptModeApiSurfaceConformanceTests.vb:97`
  （方法声明行：`BranchingSubscripts_RedeclaringTheSameMember_ReportsBC30521`）；用**不同名**成员达成隔离的正向格在
  同文件 `:69`（方法声明行：`BranchingSubscripts_TwoChainsFromOneState_AreIsolated`）。
- C# 基线：`{{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs:452`（`TestBranchingSubscripts`，期望 25）。
- 决策与义务：`../decisions.md` **D4**（提案优先级与闸门）、**D5**（基础功能以 C# / csi 为设计蓝本）；
  `../tasks/script-mode-coverage-parity/design-detailed.md` §U9 **pass 条件 5**；
  `../tasks/script-mode-coverage-parity/README.md` **§八 义务 1**（登记）、**义务 4**（变更面登记）。
- 同类形态的先例（新增诊断码的登记与收口方式）：`#19`（`issue-cross-submission-handles-clause-crash.md`，
  跨提交 `Handles` 判「报错」并新增 `BC37343`）、`#23`（`issue-script-parser-no-options-ended-gate.md`，
  解析器合法输入撞断言 ⇒ 停手上报 / 交用户裁决）。

## 后续（停手上报 / 交用户裁决）

- 本 issue 只登记与取证：**未改任何产品源码**，不预填修复 commit。
- 若用户选方向 A 或 B：按 `../tasks/script-mode-coverage-parity/README.md` **§八 义务 4** 登记变更面；
  方向 A 的新行为需**改** `ScriptModeApiSurfaceConformanceTests.vb:97` 那条钉住用例（改成断言 25 与零诊断），
  方向 B 需为**新码**补回归用例并同步 `VBResources.resx` ↔ 13 份 `xlf`。
