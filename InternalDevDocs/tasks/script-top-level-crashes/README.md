# 任务：脚本顶层「本该报错 / 本该正常却崩掉编译器」一次收口（script-top-level-crashes）——任务计划

> **状态：计划已产出，待 author/验证者核对（Accepted 门前）**。本文件夹是本族缺陷的四件套：本 README（目标 / 判定表 / 分批 / Vortex 代办 / 共享源码事实 / 冲突登记）+ `design-overview.md`（总体设计 + 全称主张剪枝自检）+ `design-detailed.md`（逐单元改动蓝图 + pass 条件 + 账本义务）+ `test-plan.md`（L1–L4 矩阵 + 无副作用纪律 + 全量回归口径）。

- **一句话**：把 `issues\` 的 **04–11 八条 Open 缺陷**（脚本顶层成员 / 初始化器 / 语句在**普通编译上下文里合法或报错、在脚本模式下却崩编译器进程或产出坏 IL**）逐条判定为「修好」或「报错」，给出可无人值守串行实施的改动蓝图与无副作用验收网；**八条判定为 4 修好 + 4 报错**。
- **作者给定的判定原则（本计划的唯一判据）**：
  > 崩编译器是 bug。要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」。
  判据的执行形式（本计划把「判定依据必须来自证据」落成可复核的形式）：**同形状放进普通编译上下文（普通 `Async Function` / 普通类 / 嵌套类 / 普通 `Module`）实测是什么行为**——合法 ⇒ 走「修好」；报错 ⇒ 走「报错」并复用同一条诊断。
- **依据链**：
  `..\..\issues\README.md`（问题清单，04–11 的原文与根因链）→ `issues\issue-*.md` 八份 → 已采纳会议 RESOLUTION（`..\..\meetings\meeting-submission-shared-members.md` **R1**（issue 05＝甲）/ **R2**（issue 06＝乙，目标「从崩改成报错」）/ **R5**（甲先乙后、独立验收）/ **R6**（06 的面孔② 归 issue 09）/ **R7**（07/08/09 独立立项）；`..\..\meetings\meeting-script-extension-methods.md` **R2**（issue 04＝候选 B，补「必须 `Shared`」诊断））→ `..\..\decisions.md` **D5**（基础功能以 C#/csi 为蓝本）/ **D6**（兼容性只对 GA 成立）/ **D4**（P1 硬约束）→ `..\..\spec\spec-scripting-dialect.md`（脚本声明/提交模型，本任务的规范落点）→ `..\..\compilers-index.md`（源码地图）。
- **调度方式**：Vortex 涡流（实施者 agent 产出 → 验证者 agent 按 pass 条件核对 → 打回修复 → 通过关闭），main 只调度，串行交替、不可催促。流水账 `<项目根>/tmp/vortex-logs/script-top-level-crashes/`。

---

## 一、范围与非范围

### 范围内（`issues\README.md` 登记的 04–11 共 8 条 Open）

| # | issue 文件 | 形状（顶层/提交类） | 现状症状（已运行实测） |
|---|---|---|---|
| 04 | `issue-script-top-level-extension-method-crash.md` | `<Extension>` 施加于脚本类的**实例**成员（漏写 `Shared`） | Debug 断言终止 `Me.IsShared`（`SourceMethodSymbol.vb:1504`）；Release 落到 codegen |
| 05 | `issue-submission-shared-field-initializer-typeload.md` | 顶层 `Shared` 字段/属性带初始化器（含 `Shared ReadOnly`、含 `Shared Dim arr(2)` 隐式上界） | 编译期零诊断，宿主 `TypeLoadException`（exit 34） |
| 06 | `issue-script-shared-field-await-initializer-crash.md` | 顶层 `Shared` 字段/属性初始化器含 `Await` | 断言终止 `Unexpected value 'AwaitOperator'`（exit 35） |
| 07 | `issue-submission-implicit-type-member-asserts.md` | 提交类顶层 `Event` / `WithEvents` | 断言终止（`SynthesizedEventAccessorSymbol.vb:495` / `SourceWithEventsBackingFieldSymbol.vb:66`，exit 35） |
| 08 | `issue-submission-shared-member-implicit-me.md` | 共享成员体 / 共享初始化器**隐式**引用实例成员 | 编译期零诊断，运行期 `InvalidProgramException`（exit 58）或先撞 issue 05 |
| 09 | `issue-initializer-diagnostic-does-not-gate-emit.md` | **嵌套类型**（普通类）里字段/属性初始化器含 `Await` | 诊断 BC36937 **报了但不 gate 发射** ⇒ 零诊断 + 断言终止（exit 35） |
| 10 | `issue-script-top-level-await-in-try-crash.md` | 顶层 `Catch` / `Finally` / `SyncLock` 里的 `Await` | 编译器 `NullReferenceException`（`ILBuilder` 的块实现期，exit 3），零诊断 |
| 11 | `issue-script-top-level-goto-await-crash.md` | 顶层 `GoTo` 指向顶层标签 | 编译器 `NullReferenceException`（`BasicBlock.ShortenBranches`，exit 3） |

### 非范围（显式）

- **不改容器种类**：脚本/提交类保持 `TypeKind.Submission`（`meeting-script-extension-methods.md` R21 已裁；本任务的任何单元都不得以「换容器」为修法）。
- **不修 issue 07 之外的断言族邻居**：`Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93` 虽属同一断言族，但**本任务一并收口**（它是同族可枚举的第三处，属 F07 的改动面，不留遗留）；除此以外 `Symbols\Source\**` 里其余 `IsImplicitlyDeclared` 消费点不在此列。
- **不改 `Compilers\Core\Portable\CodeGen\`（共享 C#/VB 发射层）**：八条崩溃的栈顶都在共享 `CodeGen.ILBuilder` / `BasicBlock`，修法一律是「不让坏形状活到发射期」，不是给共享发射层加守卫（加守卫会把 VB 的形状知识写进 C# 也走的文件）。
- **不做 `Module` 化 / 顶层成员默认 `Shared`**：`meetings\inactive\meeting-top-level-implicit-shared.md` 方向维持 Table，本任务不触碰。
- **不改 `spec\` / `meetings\` / `proposals\` / `issues\` 文本**：规范与 issue 正文的修订作为**义务**记在 `design-detailed.md` §账本与规范义务，由后续 spec/issue 阶段执行。
- **不碰 issue 06 的「面孔②」判据**（嵌套类型）：由 F09 承接（`meeting-submission-shared-members.md` R6 明写）。

---

## 二、判定表（本任务的核心产出）

> **判定依据 = 同形状在普通编译上下文里的实测行为**。普通上下文一律用**内存编译**或 `vbi.exe` 直跑 `.vbx`（探针见 §六「实测清单」）。三态标注按 `manifest.md`。

| # | 普通上下文里的同形状 | 普通上下文行为（证据） | 判定 | 依据 / 前置决定 |
|---|---|---|---|---|
| **04** | `C#` `.csx` 里 `<Extension>` 非 `static` 成员 → **CS1105**「Extension method must be static」（`issues\issue-script-top-level-extension-method-crash.md` 的已检查锚点 + `ScriptSemanticsTests.cs:1110-1118`）；VB 普通上下文里该前提**不可违反**（`Module` 成员隐式 `Shared`） | **报错**（普通上下文没有可比形状，但 C# 有先例且 VB 缺这一条） | **报错** | **前置决定已存在**：`meeting-script-extension-methods.md` **R2** 采纳候选 B = 「维持『脚本类里只有 `Shared` 成员可作扩展方法』，补齐缺失诊断」，两处同批（早期解码守卫 + 完整解码分支），**诊断必须点名 `Shared`**（对齐 CS1105 句式，不复用 BC36551 的「only in modules」）。本计划不推翻，只做落点细化。 |
| **05** | 普通类 `Public Shared x As Integer = 5` | **合法且工作**：**已运行实锤**——嵌套类同形状（`Public Class C` + `Public Shared sx As Integer = 5` + 读 `C.sx`）exit 0、输出 `NESTEDCLASS-OK 5`（探针 q23，见 `..\..\proposals\proposal-submission-shared-members.md` §4 探针清单；issue 正文的边界表同向） | **修好** | **前置决定已存在**：`meeting-submission-shared-members.md` **R1** 采纳**甲**（`SourceMemberContainerTypeSymbol.vb:2726-2737` 的 `TypeKind = Submission` 分支按 `isShared` 分叉——共享时走 `EnsureCtor`，形态是**两个符号**而非形参分叉）。本计划不推翻。 |
| **06** | 普通类 `Shared x = Await …` | **报错**：**已运行实锤**——嵌套类（非脚本类）`Public Shared s As Integer = Await 5` 正常打印 **BC36937 + BC36930**、exit 1（探针 v4）；机制上 `IsInAsyncContext()`（`Binder_Expressions.vb:4720-4728`）对非脚本类的字段返回 False ⇒ 绑定期报 BC36937。C# 侧是 CS8100（脚本专属静态字段初始化器） | **报错** | **前置决定已存在**：`meeting-submission-shared-members.md` **R2** 采纳**乙**，目标定为「从崩改成报错」，三条件（新脚本专属码 / 覆盖共享**属性** / 落点在**调用结构**）。本计划不推翻。 |
| **07** | 嵌套类里 `Public Event E` / `Public WithEvents r` | **合法且工作**（q34/q35 exit 0；取自 issue 正文的已运行结论） | **修好** | 三处 `Debug.Assert(Not …IsImplicitlyDeclared)` **不是控制流**：断言之后的 `AddSynthesizedAttribute` 无条件执行（`SynthesizedEventAccessorSymbol.vb:495-498`、`SourceWithEventsBackingFieldSymbol.vb:66-77`），Release 下断言编译掉后**照常发射**（`e307d0f` 实测顶层 `Event` exit 0），故把断言收窄到原本意图（`Not …IsImplicitClass`）**零行为变化**。可选路线 B（改 `ImplicitNamedTypeSymbol.IsImplicitlyDeclared`）改动面扩散到全部消费者，**不取**——理由见 `design-detailed.md` §F07。 |
| **08** | 普通类里共享方法体 / 共享初始化器隐式引用实例成员 | **报错 BC30369**（q39 / q37 两条，exit 1） | **报错** | 复用 **BC30369**（`ERR_BadInstanceMemberAccess`，`Errors.vb:323`），**零新码**。根因是 `Binder_Expressions.vb:2260-2261` 的注释前提「No code in a script class is shared」在 fork 里已不成立（`:2263-2265` 对任何隐式 `Me` 一律 `Return True`，永不走到 `:2272-2278`）。 |
| **09** | 嵌套类型初始化器里 `Await`（普通类，非脚本类） | **诊断已报** BC36937，**且应当不发射**——`EmitExpression.vb:207` 的注释逐字「Code gen should not be invoked if there are errors.」；判别性对照 v4/v5 显示「有别的错时门生效」 | **修好**（让已报出的诊断 gate 发射） | **缺陷机制**不是「缺诊断」，而是「诊断只落进编译级共享袋，逐方法发射门看不见本桶报过 error」（登记与逐环锚点见 `..\..\issues\issue-initializer-diagnostic-does-not-gate-emit.md`；相关文件 `MethodCompiler.vb` / `BindingDiagnosticBag.vb`）。修法不改语言语义：错误照报，只是不再崩。 |
| **10** | 普通 `Async Function` 里 `Catch` / `Finally` 内 `Await` | **报错 BC36943**（g8 实测 exit 1、×2；`g10` 证明 `Using` 内 `Await` 合法 ⇒ 家族边界与消息一致） | **报错** | **复用 BC36943**（`ERR_BadAwaitInTryHandler`，`Errors.vb:1572`），**零新码**。根因：BC36943 的检查住在 `BindMethodBlock` 的 `CheckOnErrorAndAwaitWalker`（`Binder_Statements.vb:291` / `:330` / `:602-610`），而脚本 `<Initialize>` 的方法体由 `SynthesizedInteractiveInitializerMethod.GetBoundMethodBody` 返回**空壳**（`Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`，体内只有一个退出标签）⇒ 顶层语句从不经过这道 walker。 |
| **11** | 普通 `Async Function` 里 `GoTo` 跨 `Await` | **合法、编译通过、跳转生效**（g4 实测 exit 0，输出 `A`→`C`） | **修好** | 根因**不是**「异步宿主与普通方法不一致」（**推翻 issue 11 正文的同源假说**，见 §三）：顶层 `LabelStatement` 被**显式丢弃**——`SourceMemberContainerTypeSymbol.vb:2613-2615` 逐字 `Case SyntaxKind.LabelStatement ' TODO (tomat): should be added to the initializers / Exit Select`，而顶层语句（含 `GoTo`）在 `:2625-2637` 照常入 `instanceInitializers` ⇒ `GoTo` 的分支目标永远没有落地块。**判别性实测 g1/g11：无 `Await` 的顶层 `GoTo`（前向与反向各一）同样崩在 `ShortenBranches`** ⇒ `Await` 与本崩溃无关。 |

**计数：修好 4 条（05 / 07 / 09 / 11）；报错 4 条（04 / 06 / 08 / 10）。**

---

## 三、共同根因假说：逐条查证结论（**不是一根，是两根 + 五条各自独立**）

原始假说：「这批崩溃同源——脚本顶层语句住在合成的异步宿主 `<Initialize>` 里，发射前置检查与普通方法不一致，本该报错的活到发射期」。

**查证结论：假说对其中两条成立（09、10），对第三条（11）被实测推翻，对另外五条不成立。**

### 根 A（成立，覆盖 09 + 10）：顶层语句走「初始化器路径」，拿不到「方法体路径」的检查与发射门

三层证据，逐环 `文件:行号` + 实测：

1. **`<Initialize>` 的方法体是空壳**：`Compilers\VisualBasic\Portable\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142` 的 `GetBoundMethodBody` 只返回一个含退出标签的空 `BoundBlock`。⇒ `MethodCompiler.BindAndAnalyzeMethodBody`（`Compilation\MethodCompiler.vb:1807-1848`，`body = method.GetBoundMethodBody(...)` 于 `:1818`）看到的不是顶层代码。
2. **方法体级检查因此整族缺席**：BC36943 与 On Error/Resume 混用、行号标签、`WRN_AsyncLacksAwaits` 全部挂在 `Binder_Statements.vb:291-448` 的 `BindMethodBlock` 上，其中 `CheckOnErrorAndAwaitWalker.VisitBlock(blockBinder, body, ...)` 于 `:330` 被调用、`VisitAwaitOperator` 于 `:602-610` 报 BC36943。空壳体使这一步恒为空转。**实测**：g15（Finally 内 `Await`）、g6（Catch 内 `Await`）、g7（三处）→ exit 3 NRE、零诊断；g8（普通 `Async Function` 同形状）→ exit 1、BC36943。
3. **顶层语句的绑定诊断落进编译级袋，不进发射门**（**缺陷机制**：登记与逐环锚点见 `..\..\issues\issue-initializer-diagnostic-does-not-gate-emit.md`）：顶层语句与字段/属性初始化器同由 `Binder.BindFieldAndPropertyInitializers` 绑定，该绑定报出的诊断只落进编译级共享袋，不构成「本桶报过 error」这一信号；而发射门（`MethodCompiler.vb`）的三项判据里，`diagsForCurrentMethod` 是 `BindingDiagnosticBag.GetInstance(_diagnostics)` 造出的**空袋**（`BindingDiagnosticBag.vb` 的 `GetInstance` **只复制两个布尔**），`processedInitializers.HasAnyErrors` 只反映 **bound 节点错误标志**（`Binding\Binder_Initializers.vb` 的 `ProcessedFieldOrPropertyInitializers.HasAnyErrors`），`block.HasErrors` 是那个空壳 ⇒ 已报出的诊断拦不住发射。**实测**：v1–v3（嵌套类三种形状）exit 35 零诊断 vs v4/v5（同形状+别的错）正常打印 BC36937。

⇒ 09 与 10 是**同一根的两个面**：A-1（检查缺席）与 A-2（诊断不 gate）。**修法必须成对**——只让诊断 gate 发射（F09）不会让 10 出现诊断（walker 压根没跑）；只补 walker（F10）而不让初始化器诊断 gate，则报出的 BC36943 仍然拦不住发射（`_diagnostics` 不参与 `:1288`）。故 F09 是 F10 的**前置**。

### 根 B（成立，只覆盖 11）：顶层语句收集表**漏掉 `LabelStatement`**

- 丢弃点：`SourceMemberContainerTypeSymbol.vb:2613-2615`（含上游作者 `' TODO (tomat): should be added to the initializers` 原注释）。
- 收集点与之相邻：`:2625-2637` 的 `Case Else` 把 `ExecutableStatementSyntax` 入 `instanceInitializers`。
- **推翻「同源于根 A」的判别性实测**：g1（顶层 `GoTo` + 标签，**无任何 `Await`**）exit 3，崩点与 g2 逐字相同（`ILBuilder.BasicBlock.ShortenBranches`，`Compilers\Core\Portable\CodeGen\BasicBlock.cs:325`）；g11（**反向** `GoTo` 循环，无 `Await`）同样 exit 3。g3（顶层 `Try/Catch/Finally`，无 `Await`）exit 0 正常 ⇒「顶层异步宿主」本身不致病。
- ⇒ 11 与 10 **不同源**；issue 11 正文的「与 #10 同源」是**推测**，实测**证伪**（该订正记在 §九 冲突登记）。

### 各自独立（04 / 05 / 06 / 07 / 08 五条）

| # | 独立根因（锚点） | 与根 A/B 的关系 |
|---|---|---|
| 04 | `SourceMethodSymbol.vb:1500-1504` 与 `:1633-1634` 的 `Debug.Assert(Me.IsShared)`——「扩展方法必须 `Shared`」的前提在 `Module` 内不可违反，故 VB 从无用户可见诊断（`NamedTypeSymbolExtensions.vb:108-111` 把 `IsScriptClass` 与 `TypeKind.Module` 并列放行） | 无关（早期/完整属性解码路径） |
| 05 | `SourceMemberContainerTypeSymbol.vb:2726-2737` 的 submission 分支把**共享**初始化器交给**为实例版设计**的 `SynthesizedSubmissionConstructorSymbol`（`:31-38` 无条件带 `submissionArray` 形参）⇒ 带形参的 `.cctor` | 无关（构造器符号生产） |
| 06 | `Binder_Expressions.vb:4720-4728` 的 `IsInAsyncContext()` **不查 `IsShared`**（共享初始化器被当 async 上下文放行），而 `.cctor` 的 `IsAsync` 恒 False（`Symbols\SynthesizedSymbols\SynthesizedMethodBase.vb:195-200`）⇒ `AwaitOperator` 存活到 `CodeGen\EmitExpression.vb:206-209` | 与 08 **同母题**（「脚本类里没有共享代码」这一失效前提的第二个落点，见 08 行） |
| 07 | `ImplicitNamedTypeSymbol.vb:33-37` 让提交/脚本类的 `IsImplicitlyDeclared` 恒 True，撞三处合成成员断言（`SynthesizedEventAccessorSymbol.vb:495`、`SourceWithEventsBackingFieldSymbol.vb:66`、`SynthesizedWithEventsAccessorSymbol.vb:93`） | 无关（类型符号类型判定） |
| 08 | `Binder_Expressions.vb:2260-2270` 以注释「No code in a script class is shared」为前提，对脚本类任何隐式 `Me` 一律 `Return True` | 与 06 同母题（同一条失效前提） |

> **转述纪律**：上表每格的三态 = 锚点行**实锤**（逐行已检查）+ 症状**实锤**（已运行）；「06 与 08 同母题」是**推测**（issue 08 / 06 正文各自的自述，未额外实证）；「Release 下 07 照常发射」为**实锤**但取自**较旧** Release 二进制 `e307d0f`（**参照级**，HEAD Release 未跑）。

---

## 四、分批与实现顺序（**W1/W2/W3 三批**，单元 F04–F11）

分批原则：**同文件串行、跨文件并行**；`Module` 级共享文件（`Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` / 13 份 `xlf`）只允许一个单元在一个实施轮内独占。

| 批 | 单元 | 文件面 | 依赖 | 说明 |
|---|---|---|---|---|
| **W1** | **F09**（issue 09，修好：诊断 gate 发射） | `Compilation\MethodCompiler.vb`（`:599-623` / `:1288`）、`Binding\Binder_Initializers.vb`（文档 + 构造） | 无 | **必须最先**：F06 / F08（初始化器形状）/ F10 都靠「初始化器绑定产生的 error 能拦住发射」 |
| **W2** | **F05**（甲）→ **F11**（标签入序列）→ **F07**（断言收窄） | `Symbols\Source\SourceMemberContainerTypeSymbol.vb`（F05 `:2726-2737`、F11 `:2613-2615`，**同文件 ⇒ 串行**）；F07 三个合成成员文件 | 无（与 W1 并行安全：文件面不重叠） | F05 先于 F11 只因同文件串行的书写顺序（`meeting-submission-shared-members.md` R5 已把「甲先落地」定为顺序） |
| **W3** | **F10**（BC36943 补跑）→ **F08**（BC30369）→ **F06**（乙，新码）→ **F04**（B，新码） | `Binding\Binder_Initializers.vb` + `Binding\Binder_Statements.vb`（F10）；`Binding\Binder_Expressions.vb`（F08 `:2257-2286` 与 F06 `:4740-4744`，**同文件 ⇒ 串行**）；`Symbols\Source\SourceMethodSymbol.vb`（F04）；`Errors.vb` + `ErrorFacts.vb` + `VBResources.resx` + 13 `xlf`（F06 与 F04 各一枚新码，**必须串行相邻落地**） | **F10 ← F09**；**F06 ← F05（甲）+ F09**；**F08 的初始化器形状 ← F09**（方法体形状无依赖）；F04 无依赖 | 两枚新码共享 `Errors.vb` 的编号带与 13 份 xlf ⇒ 由同一实施轮或紧邻轮次完成，避免 rebase 冲突 |

**跨批依赖图（单行）**：`F09 → {F10, F06, F08②}`、`F05 → F06`；F04 / F07 / F11 无前置。

---

## 五、共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-09-13，本代理逐条 Read/Grep 复核行号；工作树 = `with-modified-vbsyntax` 分支，含 F09 的初始化器袋改动）。引用以 `文件:行号` 给出；证据等级按 `manifest.md` 证据阶梯。路径相对仓库根；编译器前缀 `Compilers\VisualBasic\Portable\`（简写 `VB\`）、共享编译器前缀 `Compilers\Core\Portable\`（简写 `Core\`）。失败点缓存 `<项目根>/tmp/vortex-logs/top-level-implicit-shared/pitfalls.md`（P-001…P-038）。

### 根 A 相关

- `VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142` — `GetBoundMethodBody` 返回只含 `BoundLabelStatement(ExitLabel)` 的空壳块；`:51-55` `IsAsync` 恒 True；`:87-91` `IsShared` 恒 False；`:105-109` `MethodKind = Ordinary`。**已检查。**
- `VB\Compilation\MethodCompiler.vb:1807-1848` — `BindAndAnalyzeMethodBody`：`body = method.GetBoundMethodBody(compilationState, diagnostics, methodBodyBinder)`（`:1818`），随后 `Analyzer.AnalyzeMethodBody`（`:1821`）与 `DiagnosticsPass.IssueDiagnostics`（`:1822`）都作用在这个**空壳**上。**已检查。**
- `VB\Binding\Binder_Statements.vb:291-448` — `BindMethodBlock`；`:330` 调 `CheckOnErrorAndAwaitWalker.VisitBlock(blockBinder, body, diagnostics, …)`；`:335-339` 的 `WRN_AsyncLacksAwaits` 判据。**已检查。**
- `VB\Binding\Binder_Statements.vb:454-635` — `CheckOnErrorAndAwaitWalker`（`Private Class`，嵌在 `Partial Friend Class Binder` 内 ⇒ 同一 `Binder` 的其它 partial 文件按 `Private` 可达）；`:516-523` 的 `Visit` 在**非** async 上下文时拒绝下钻到 `BoundExpression`（这是「walker 必须在 async-aware binder 下跑」的硬约束）；`:525-541` `VisitTryStatement`（`Debug.Assert(Not node.WasCompilerGenerated)`，`:526`）；`:579-591` `VisitSyncLockStatement`；`:593-600` `VisitUsingStatement`（**不**置 `_isInCatchFinallyOrSyncLock` ⇒ `Using` 内 `Await` 合法，与 BC36943 消息口径一致）；`:602-610` `VisitAwaitOperator` 报 `ERRID.ERR_BadAwaitInTryHandler`。**已检查。**
- `VB\Binding\Binder_Initializers.vb:102-203` — `BindFieldAndPropertyInitializers` 主循环；`:122-126` 脚本模式下 `parentBinder = New TopLevelCodeBinder(scriptInitializerOpt, syntaxTree.GetRoot(), parentBinder)`（**async-aware**：`TopLevelCodeBinder` 继承 `SubOrFunctionBodyBinder` 且以 `<Initialize>` 为 containing member，见 `VB\Binding\TopLevelCodeBinder.vb:12-25` ⇒ `IsInAsyncContext()` 为 True）；`:131-136` 的 `initializer.FieldsOrProperties.IsDefault` 判定「这是顶层语句（非字段/属性初始化器）」；`:205-229` `BindGlobalStatement` → `Me.BindStatement(statementNode, diagnostics)`。**已检查。**
- `VB\Binding\Binder_Initializers.vb:19-76` — `ProcessedFieldOrPropertyInitializers`；`:22-28` 文档逐字「Indicate the fact that binding of initializers produced a tree with errors or that the binding of the initializers reported an error diagnostic.」；`:54` `HasAnyErrors = bindingReportedErrors OrElse boundInitializers.Any(Function(i) i.HasErrors)`；`:58-75` `EnsureInitializersAnalyzed`（**已存在的「把初始化器拼成一个合成块再走分析」扩展点**，其 `:64-67` 的拼块形状是 F09/F10 的模板）。**已检查。**
- `VB\Compilation\MethodCompiler.vb:599-623` — 两个桶各自 `BindingDiagnosticBag.GetInstance(_diagnostics)` 得到**本桶独立袋**并作 `diagnostics` 实参传给 `Binder.BindFieldAndPropertyInitializers`，随后 `_diagnostics.AddRangeAndFree(袋)` 合并回共享袋（`AddRange` 保序）；`:630` 的 `CreateSharedConstructorsForConstFieldsIfRequired` 与随后的 `CompileMethod` 调用。**已检查。**
- `VB\Compilation\MethodCompiler.vb:1272-1273` — `diagsForCurrentMethod = BindingDiagnosticBag.GetInstance(_diagnostics)`；`:1288` 发射门 `hasErrors = _hasDeclarationErrors OrElse diagsForCurrentMethod.HasAnyErrors() OrElse processedInitializers.HasAnyErrors OrElse block.HasErrors`；`:1331` `If DoLoweringPhase AndAlso Not hasErrors Then`；`:1504-1506` `BuildScriptInitializerBody`（脚本初始化器体的真正拼装点）；`:1554` `LowerAndEmitMethod` 内的二次门 `hasErrors = body.HasErrors OrElse diagsForCurrentMethod.HasAnyErrors …`；`:1558` 早退；`:1642` `Debug.Assert(Not diagnostics.HasAnyErrors)`。**已检查。**
- `VB\Binding\BindingDiagnosticBag.vb:50-52` — `GetInstance(template)` 只复制 `AccumulatesDiagnostics` / `AccumulatesDependencies` 两个布尔，**不复制已有诊断**。**已检查。**
- `VB\Compilation\MethodCompiler.vb:29` / `:68` / `:100` — `_hasDeclarationErrors` 是 `ReadOnly` 字段、构造期赋值；`SetGlobalErrorIfTrue`。**已检查**（⇒ 任何「复用它」的方案都必须改成实例状态或走 `ProcessedFieldOrPropertyInitializers` 通道）。
- `VB\CodeGen\EmitExpression.vb:206-209` — `EmitExpressionCore` 的 `Case Else` 抛 `ExceptionUtilities.UnexpectedValue`；`:207` 注释逐字「Code gen should not be invoked if there are errors.」（F09 的契约依据）。**已检查。**
- `Core\CodeGen\ILBuilder.cs:397-434`（`BlockedBranchDestinationSlow`，崩溃栈 `:413`）与 `Core\CodeGen\BasicBlock.cs:305-346`（`ShortenBranches`，崩溃栈 `:325`）— **共享 C#/VB 发射层，本任务零改动**。**已检查。**

### 根 B 相关

- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2613-2615` — `Case SyntaxKind.LabelStatement` / `' TODO (tomat): should be added to the initializers` / `Exit Select`（**丢弃点**）。**已检查。**
- 同文件 `:2625-2637` — `Case Else`：`memberSyntax.Kind = SyntaxKind.EmptyStatement OrElse TypeOf memberSyntax Is ExecutableStatementSyntax` ⇒ 若 `binder.BindingTopLevelScriptCode` 则 `SourceNamedTypeSymbol.AddInitializer(instanceInitializers, initializer, members.InstanceSyntaxLength)`（**收集点**）。**已检查。**
- `VB\Binding\Binder_Statements.vb:59-60` — `Case SyntaxKind.LabelStatement : Return BindLabelStatement(...)`（⇒ 顶层标签一旦入序列即可正常绑定）；`:926-948` `BindLabelStatement`（`:930-934` 注释逐字「A label symbol will always be found because all labels without syntax errors are put into the label map in the blockbasebinder.」）。**已检查。**
- `VB\Binding\ExecutableCodeBinder.vb:24-74` — 标签由 `LabelVisitor` 扫**整棵语法根**收集（`:50-74` `BuildLabels`）⇒ 顶层标签的 `SourceLabelSymbol` 本来就存在，`GoTo` 因此能绑定成功、**零诊断**（这解释了「为何没有诊断却有分支目标」）。**已检查。**
- `VB\Compilation\MethodCompiler.vb:1497-1509` — `BuildConstructorBody` / `BuildScriptInitializerBody` 分派（`:1504-1506`）。**已检查。**
- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:1565-1585` — `AddInitializer`（`aggregateSyntaxLength` 累加；`:1571-1574` 断言「initializers should be added in syntax order」）；`:1523` vs `:1524` 两桶 span 累加语义不对称（静态桶跳过元数据常量、实例桶无条件）。**已检查**（F11 的 `precedingInitializersLength` 与调试偏移必须沿用这条既有规则）。**参见 pitfalls P-011 / P-013**。

### 产物 / 构造器相关（F05）

- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737` — submission 分支 `If Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(initializers, False)` → `New SynthesizedSubmissionConstructorSymbol(syntaxRef, Me, isShared, binder, diagnostics)`（**isShared 原样透传**）。**已检查。**
- `VB\Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:31-38` / `:40-44` — 无条件造 `submissionArray As Object()` 形参、`Parameters` 直接回吐。**已检查。**
- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2771-2802` — `EnsureCtor`（`:2800` `New SynthesizedConstructorSymbol`，形参恒空）；`:3195-3214` `CreateSharedConstructorsForConstFieldsIfRequired`（**既有同形先例**，由 `MethodCompiler.vb:630-655` 单独编译）。**已检查。**
- `VB\Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb:59-63`（`Name` 取 `.cctor`，承重行 `:61`）、`:190-194`（`MethodKind`，**承重行 `:192`**）。**已检查。**
- `VB\Symbols\MethodSymbol.vb:517-521` — `IsScriptConstructor` 要求 `MethodKind = Constructor`（共享时为 `SharedConstructor` ⇒ 假）。**已检查。**
- `VB\Emit\NamedTypeSymbolAdapter.vb:451-511`（`beforefieldinit`，**VB 主动打该标志**于 `:496-499`）— **参见 pitfalls P-001**：甲 之后共享初始化器仍进 `.cctor`，其时序仍是「首次访问该静态字段之前」，**不是**「`<Initialize>` 之前」。

### 断言族（F07）

- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:233-240` — `DeclarationKind.ImplicitClass / Script / Submission` → `New ImplicitNamedTypeSymbol(...)`，其余 → `SourceNamedTypeSymbol`。**已检查。**
- `VB\Symbols\Source\ImplicitNamedTypeSymbol.vb:25-37` — 类 `ImplicitNamedTypeSymbol` 与 `IsImplicitlyDeclared = IsImplicitClass OrElse IsScriptClass`。**已检查。**
- `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:1295-1300` — `IsScriptClass` = `DeclarationKind.Script OrElse Submission`。**已检查。**
- `VB\Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:61-78` — `AddSynthesizedAttributes`：`MyBase.AddSynthesizedAttributes(...)` → `Debug.Assert(Not Me.ContainingType.IsImplicitlyDeclared)`（`:66`）→ **无条件**加 `CompilerGenerated` / `DebuggerBrowsableNever` / `AccessedThroughProperty`。**已检查**（⇒ 断言**不是控制流**）。
- `VB\Symbols\Source\SynthesizedEventAccessorSymbol.vb:492-506` — 同形：`:495` 断言 → **无条件**加 `CompilerGenerated`。**已检查。**
- `VB\Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93` — 同族第三处（issue 正文列为「未单独实测」，F07 一并收口）。**已检查（断言式已读）。**

### 接收者 / `Await` 判据（F08 / F06）

- `VB\Binding\Binder_Expressions.vb:2235-2255` — `IsMeOrMyBaseOrMyClassInSharedContext()`（`SymbolKind.Method, Property` 分支 `:2244-2246`；`SymbolKind.Field` 分支 `:2248-2251`；兜底 `Return True` `:2254`）。**已检查。**
- `VB\Binding\Binder_Expressions.vb:2257-2286` — `CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext`：`:2260-2261` 注释（「Any executable statement in a script class can access Me/MyClass/MyBase implicitly but not explicitly. / **No code in a script class is shared.**」）、`:2262-2270` 脚本类分支（隐式 ⇒ `Return True`；显式 ⇒ `ERR_KeywordNotAllowedInScript`）、`:2272-2278` BC30369 出口。**已检查。**
- `VB\Binding\Binder_Expressions.vb:4720-4728` — `IsInAsyncContext()`（Method 分支 `:4722-4723`；Field/Property 分支 `:4726-4727` **只看 `IsScriptClass`、不查 `IsShared`**）。**已检查。**
- `VB\Binding\Binder_Expressions.vb:4734-4749` — `BindAwait` 的调用结构（`:4740-4741` IsInQuery、`:4742-4743` `ElseIf Not IsInAsyncContext()` → `GetAwaitInNonAsyncError()`）。**已检查。**（乙 的落点依据：共享字段时 `IsInAsyncContext()` 为真 ⇒ `:4743` 不可达。）

### 扩展方法承载（F04）

- `VB\Symbols\NamedTypeSymbolExtensions.vb:108-111` — `AllowsExtensionMethods(container)` = `TypeKind.Module OrElse IsScriptClass`（**全 VB 侧唯一容器判据**）。**已检查。**
- `VB\Symbols\Source\SourceMethodSymbol.vb:1496-1523` — 早期解码分支：`:1500-1502` 的三重条件（`MethodKind` ∈ {Ordinary, DeclareMethod} ∧ `AllowsExtensionMethods()` ∧ `ParameterCount <> 0`）⇒ `:1504` `Debug.Assert(Me.IsShared)`。**已检查。**
- `VB\Symbols\Source\SourceMethodSymbol.vb:1621-1648` — 完整解码分支序列：`:1624-1625` BC36550、`:1627-1628` BC36551、`:1630-1631` BC36552、`:1633-1634` `Debug.Assert(Me.IsShared)`、`:1636-1647` 首参 Optional/ParamArray/泛型约束（BC36548 / BC36554 / …）。**已检查。**
- `VB\Errors\Errors.vb:1815` — `ERR_NextAvailable = 37341`（F06 的码位带）；`VB\Errors\Errors.vb:1634-1635` 之间（`37004` 之后直接 `37050`）**37005–37049 现为空**（F04 的码位带，见 `meeting-script-extension-methods.md` OPEN QUESTIONS）。**已检查（区间存在性按既有纪要与本任务 grep 复核；实现期须重跑 grep 确认）。**

### 测试基建

- `Scripting\VisualBasicTest\`（**MTP 项目**：`dotnet test` 静默不跑，须直跑程序集 `-automated`，见 memory `vb-scripting-test-runner`）；既有体例 `ScriptTests.vb:16-59`（`VisualBasicScript.Create` / `RunAsync` / `s_defaultOptions`）、`:61-70`、`InteractiveSessionTests.vb:14-47`、`Helpers\TestConsoleIO.vb:5-10`（内存 `StringReader`/`StringWriter`）、`CommandLineRunnerTests.vb:91-116`（L4 构造体例，用法见 `test-plan.md` §5 的构造限定）。**已检查。**
- 编译器侧诊断体例：`Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb`（用 `ERRID` 名断言，非 `BC` 字符串）、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMethods\ExtensionMethodTests.vb:2425-2440`（`ScriptExtensionMethods`，正向用例，标着 `ConditionalFact(NoUsedAssembliesValidation)`）。**已检查。**
- 七门 gate：`scripts\verify-vb-compiler-tests.ps1:7-15`。**已检查。**

---

## 六、实测清单（`vbi.exe` 直跑 `.vbx`，探针不随仓库留存）

> 编译器：Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（自报 `2.0.0-Beta+5816a5c`）；Release 对照：`Interactive\vbi\bin\Release\net10.0\vbi.exe`（自报 `2.0.0-Beta+e307d0f3dd82b3a2c990ff195d598129ec82e447`，**较旧**，只当参照）。探针形状见下表（命令形状：`vbi.exe <probe>.vbx`，记录退出码）。

| 探针 | 形状 | Debug 退出码 | 结论 |
|---|---|---|---|
| g1 | 顶层前向 `GoTo` + 标签，**无 `Await`** | **3** | NRE @ `BasicBlock.cs:325`（`ShortenBranches`）⇒ 11 的崩溃**与 `Await` 无关** |
| g2 | 顶层 `GoTo` + 标签 + `Await`（issue 11 原形状） | **3** | 与 g1 同一崩点 |
| g3 | 顶层 `Try/Catch/Finally`，**无 `Await`** | **0** | 正常（`TRY` / `FINALLY`）⇒ 顶层异步宿主本身不致病 |
| g4 | 普通 `Async Function` 里 `GoTo` 跨 `Await` | **0** | 输出 `A`→`C`（跳转生效）⇒ 普通上下文**合法** |
| g5 | 顶层 `Try` 体内 `Await`（仅 Try） | **0** | 正常 ⇒ `Await` 在 Try 体内合法 |
| g6 | 顶层 `Catch` 内 `Await` | **3** | NRE @ `ILBuilder.cs:413`（`BlockedBranchDestinationSlow`） |
| g7 | 顶层 `Try`/`Catch`/`Finally` 三处 `Await`（issue 10 原形状） | **3** | 同上 |
| g8 | 普通 `Async Function` + `Catch`/`Finally` 内 `Await` | **1** | **BC36943 ×2**（指向 Catch 与 Finally）⇒ 普通上下文**报错** |
| g9 | 顶层 `SyncLock Me` + `Await` | **1** | BC36966（探针写法问题：显式 `Me` 被脚本拒绝） |
| g10 | 顶层 `Using` + `Await` | **0** | 正常 ⇒ `Using` **不在** BC36943 家族（与消息口径一致） |
| g11 | 顶层**反向** `GoTo` 循环，无 `Await` | **3** | 同 g1 ⇒ 前向/反向都命中丢弃的标签 |
| g12 | 顶层 `Event E As EventHandler` | **35** | 断言 `Not ContainingType.IsImplicitlyDeclared`（HEAD 复现 issue 07） |
| g13 | 顶层 `Shared sx As Integer = 5` | **34** | `TypeLoadException: Could not load type 'Submission#0'`（HEAD 复现 issue 05） |
| g14 | 顶层 `SyncLock <局部锁对象>` + `Await` | **24** | **编译通过**，运行期 `SynchronizationLockException` ⇒ 同一条缺失检查的第三种后果（**坏产物**，非崩溃） |
| g15 | 顶层 `Finally` 内 `Await`（仅 Finally） | **3** | NRE @ `BlockedBranchDestinationSlow`（与 issue 10 登记栈一致） |
| g12-R | 顶层 `Event`（**较旧** Release `e307d0f`） | **0** | `EVENT-OK` ⇒ 断言在 Debug 之外不保护任何东西（F07 方向的旁证，**参照级**） |
| g13-R | 顶层 `Shared` 字段（Release） | **34** | 与 Debug 同 |
| g1-R | 顶层 `GoTo` 无 `Await`（Release） | **3** | 与 Debug 同 ⇒ 11 也不是 Debug 特有 |
| g4-R | 普通 async `GoTo`+`Await`（Release） | **0** | `A`→`C` |

**设计期新发现（不在任何 issue 正文里，须在实现期纳入验收）**：**g14**——顶层 `SyncLock` 内的 `Await` 不会崩编译器，而是**编译通过并产出运行期损坏的 IL**（监视器在错误的块里被 `Exit`）。它是 issue 10 触发面的第三个子形状（Catch / Finally / SyncLock），也把 F10 的收益从「不崩」升级为「不再静默产出坏产物」。

**未跑项（如实登记）**：HEAD 的 Release 构建（Release 目录只有较旧的 `e307d0f` 二进制，非 HEAD）；`issues\issue-*.md` 里各条「未复现 / 未查」节列举的其余形状（共享属性初始化器含 `Await`、失败提交的状态隔离、跨提交形状）。

---

## 七、Vortex 代办拆分表（供实施者/验证者无人值守串行）

> 编号 = 单元标识（**非执行顺序**，执行顺序见「前置」列）。每项 pass 条件客观可判（实施者做完 → 验证者按 pass 条件核对 → 打回/通过）。状态初始 `todo`。全部实现改动以 `design-detailed.md` §F04–§F11 为唯一底稿。
>
> **测试执行规约**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**禁用 `dotnet test`**（EXIT 0 但静默不跑）；先 `dotnet build`，再直跑 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）或 `-class <FQN>` / `-method <FQN>`（FQN 须完整到 `命名空间.类.方法`，缺段或拼错会静默 0 跑，须核对 `TestCasesToRun > 0`）。编译器侧走七门 gate。

| # | 功能（design-detailed 章节） | pass 条件（全部满足才算过） | 前置 | 状态 |
|---|---|---|---|---|
| F09 | 初始化器绑定诊断 gate 发射（§F09） | 嵌套类型实例/共享字段与实例属性三种形状的 `Await` 初始化器：**报 BC36937 且不再发射**（进程不再被断言终止）；`v4/v5` 对照组结果不变（诊断照旧打印）；**warnings 不 gate**（`HasAnyErrors` 判定，非 `IsEmpty`）；`processedInitializers.HasAnyErrors` 的三项既有语义不变；**没有初始化器错误的编译零行为变化**（全量回归兜底） | 无 | 已落地 |
| F05 | submission 共享分支改用无参共享构造器符号（甲）（§F05） | `Shared sx As Integer = 5` / `Shared ReadOnly sx As Integer = 5` / `Shared Dim arr(2) As Integer` 三种形状不再 `TypeLoadException`，且**共享初始化器在共享构造器里执行**；`Const d As Date = #…` 与 `Shared x As Integer = 5` **同文件**时两者落到同一个 `.cctor`（`meeting-submission-shared-members.md` R1 的 plan 前置实证项）；**非共享**实例初始化器路径逐字节不变 | 无 | todo |
| F11 | 顶层标签语句纳入实例初始化器序列（§F11） | 顶层 `GoTo`（前向/反向）+ 顶层标签**不再崩**，且跳转**语义生效**（输出与普通 async 方法同形）；顶层 `GoTo` 无 `Await`（g1/g11 形状）同样通过；**顶层数字行号标签**与顶层 `On Error` 的既有诊断不变；`precedingInitializersLength` / 调试偏移的双桶规则不变 | 无（与 F05 同文件 ⇒ 串行） | todo |
| F07 | 三处合成成员断言收窄（§F07） | 顶层 `Event` / `WithEvents`（实例与 `Shared` 各一）不再断言终止，且**发射与运行正确**（事件 raise→handler 计数、`WithEvents` 钩子生效）；嵌套类型同形状不变；未受影响的 `Debug.Assert` 语义（真正的 `ImplicitClass`）仍被保住 | 无 | todo |
| F10 | 脚本顶层补跑 `Catch`/`Finally`/`SyncLock` 内的 `Await` 检查（§F10） | 顶层三处位置各写 `Await` → 报 **BC36943**（含 `Catch`/`Finally`/`SyncLock` 三种），**不崩**；`Try` 体内与 `Using` 内的 `Await` **仍合法**（g5/g10 形状零诊断）；普通 `Async Function` 同形状结果不变（BC36943 ×2）；**On Error / 行号标签诊断不因复用 walker 而改变**（只取 await 检查，见 §F10 裁决） | **F09** | todo |
| F08 | 脚本类共享成员的隐式 `Me` → BC30369（§F08） | 共享方法体读实例字段、共享字段初始化器调实例方法两形状都报 **BC30369**（与普通类同码同形）；**实例成员里的隐式访问仍然合法**（回归锁死，`spec:243`）；显式 `Me` 仍报 BC36966；无新码 | F09（初始化器形状） | todo |
| F06 | 共享字段/属性初始化器里的 `Await` → 新脚本专属码（乙）（§F06） | 共享字段、共享 `ReadOnly` 字段、共享属性、共享数组上界四条子情形全部报**新码**（消息点名「共享/静态初始化器在执行时是同步的共享构造器」）；**实例**初始化器里的 `Await` 仍合法；嵌套类型形状仍走 BC36937（F09 面）；新码走完 `Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` / 13 `xlf` 全链；诊断**位置**落在肇事 `Await` 关键字 | **F05**、**F09** | todo |
| F04 | `<Extension>` 施加于脚本类实例成员 → 新诊断（候选 B）（§F04） | 顶层 `<Extension>` 漏写 `Shared` → 报**新诊断且消息点名 `Shared`**（对齐 CS1105 句式），早期解码与完整解码**两处同批**；`<Extension> Shared Function` 正向用例（`ExtensionMethodTests.ScriptExtensionMethods` 形状）**零改动通过**；嵌套容器仍报 BC36551 / 新码 F，首参 Optional/ParamArray 仍报 BC36548 / BC36554（**序列顺序不变**）；普通 `Module` 侧零行为变化 | 无（与 F06 共享编号带/资源面 ⇒ 串行相邻） | todo |
| W-GATE | 全量收口 gate（`test-plan.md` §8） | `Scripting\VisualBasicTest` 直跑 `-automated` 全量 **0 失败**；七门 gate 逐门数字与基线一致或按新增用例递增；`PublicAPI.*.txt` 零增量；`upstream-merge.md` 按 §账本与规范义务 补登记（F09 的两个文件 `Compilation\MethodCompiler.vb` / `Binding\Binder_Initializers.vb` 是 W1 批的唯一改动面，在 `upstream-merge.md` 中**均未在册**；后者在 `upstream-merge.md:250` 的 `7a0111e` 行被记为「未登」，补登后该行的「部分登记」说明须同步订正）；无遗留 Unresolved | F04–F11 全过 | todo |

> **执行范围**：全部为**无人值守串行**项（pass 条件全自动判，无真实网络 / 进程 / 写盘）。本任务不引入任何需要人工或真实外部环境的验收项。

---

## 八、Accepted 门（计划被批准进入实施的条件）

1. **判定表有证据**：§二 每一条的「普通上下文行为」都有 `文件:行号` 或实测支撑；三态标注齐全（未标注的按猜测处理）。
2. **共同根因查证过**：§三 的「两根 + 五独立」结论可复核——根 A 有 3 环源码 + 4 条实测；根 B 有 2 处源码 + 4 条判别性实测（g1/g11 vs g3/g4）。
3. **前置决定未被推翻**：05＝甲、06＝乙 与两份会议 RESOLUTION 一致（§九 冲突登记里的偏差已显式声明）。
4. **pass 条件可判**：§七 每行的 pass 条件客观、可自动判，无「实现时再定」的悬空项。
5. **测试计划齐备**：`test-plan.md` 的 L1–L4 矩阵与 §七 各行一一对应；无副作用纪律与全量回归口径明确。
6. **零越权承诺**：不改 `Compilers\Core\Portable\CodeGen\`（共享发射层）、不改容器种类、不新增 public API、不动 `spec\`/`meetings\`/`proposals\`/`issues\` 文本。
7. **文档纪律**：正文简体中文；引用仓库相对路径；无本机绝对路径 / 用户名 / 机器特定状态；无过程日志文体。

---

## 九、冲突与偏差登记（如实标出，不掩盖）

> 本节是本计划与上游既有决议的**唯一**偏差清单；除下列各条外，本计划不改变任何既有决定。

1. **「07 / 08 / 09 分别立项」与本任务单一文件夹的偏差**（流程性，不涉及设计）。
   `meeting-submission-shared-members.md` R7 与 Implication 节要求 issue 07 / 08 / 09「**分别立项**，不要把三条并进同一份设计」。本任务按作者指示把这 8 条放进**一个**文件夹。
   **本计划的处置**：只共用**文档容器**，不共用**设计**——F04–F11 各自独立成章、独立验收、独立 pass 条件，无跨单元的共享机制设计（唯一跨单元耦合是 §四 的**依赖顺序**与 **W3 批**的**共享资源文件串行**，二者都是工程约束而非设计合并）。若作者要求严格按会议口径拆分，可把 `design-detailed.md` 的 §F07 / §F08 / §F09 章节原文抽成三个独立任务文件夹，**无需改动内容**。
2. **issue 11 的根因订正**（实证推翻 issue 正文的推测）。
   `issues\issue-script-top-level-goto-await-crash.md` 的「根因方向」写「与 `issue-script-top-level-await-in-try-crash.md` 同源：顶层语句住在合成的异步宿主 `<Initialize>` 里，该宿主在发射前置检查上与普通 `Async Function` 不一致」——**该推测被 g1/g11 实测证伪**（无 `Await` 的顶层 `GoTo` 同样崩；g3 证明顶层 Try 无 `Await` 正常）。真实根因是 `SourceMemberContainerTypeSymbol.vb:2613-2615` 丢弃 `LabelStatement`（含上游 `TODO (tomat)`）。**本计划不改 `issues\` 文本**，订正作为义务记在 `design-detailed.md` §账本与规范义务。
3. **issue 10 的实际触发面比登记宽**（新增实测）。
   登记只写「`Try`/`Catch`/`Finally` 里写 `Await`」。新增实测 g14：顶层 `SyncLock` 内的 `Await` **不崩**，而是编译通过、运行期抛 `SynchronizationLockException`（exit 24）——**坏产物**。F10 的验收必须覆盖 `SyncLock` 这一子形状（`CheckOnErrorAndAwaitWalker.VisitSyncLockStatement` 已把 `SyncLock` 置入 `_isInCatchFinallyOrSyncLock`，`:579-591`），并把它写回 issue 10 的义务清单。
4. **规范文本冲突（`spec-scripting-dialect.md`）**：规范 `:266-268` 把顶层 `GoTo` 的运行时效果写成 "outside the guarantees of this specification"，并有一条 **Decision**。F11 判「修好」在**结论上**与该 Decision 的保守口径相反（变成「有保证」）。
   **依据**：判据是「同形状在普通上下文里合法且可运行」（g4），且该 Decision 的上文正是对 `:2613` 那个上游 TODO 的描述（规范自己写「Because the label statement is not emitted into the initializer body」）。
   **处置**：F11 落地时必须同步改 `spec:266-268`（把 Decision 改写为「顶层 `GoTo` 与普通方法内的 `GoTo` 同义、跳转生效」，并删去「不保证」句）；`spec:347` 的 Boundaries 条目同样要改。**本计划不碰 spec 文件**，改动作为义务写入 `design-detailed.md`。若作者认为该 Decision 必须保留（即维持「不保证」并改为报诊断），则 F11 的判定翻转为「报错」——**这一条是本计划唯一需要作者显式确认的判定**（见 §十 待确认项）。
5. **`spec:56` / `:273` 的「穷尽」主张**与 issue 07 相关：规范说四形式映射穷尽，实测顶层 `Event`/`WithEvents`/`Property` 也被送进脚本类（`DeclarationTreeBuilder.vb:175-198`）。F07 判「修好」（让 `Event`/`WithEvents` 正常工作）⇒ 规范必须把这两形式写进映射表或其邻域。`meeting-submission-shared-members.md` R8(c) 已把这两句的改写「照单接受」。
6. **无其它冲突**：05＝甲、06＝乙、容器种类保持 `TypeKind.Submission`、07/08/09 的边界划分（06 面孔② 归 09）四条均与本计划一致。

---

## 十、待作者显式确认项

1. **F11 的判定**（§九 第 4 条）：维持本计划的「**修好**」（顶层 `GoTo` 跳转生效，需同步放宽 `spec:266-268` 的 Decision），还是改为「**报错**」（对顶层 `GoTo` 指向顶层标签报诊断，保留规范的「不保证」口径）？
   本计划推荐「修好」：同形状在普通上下文里合法**且跳转确实生效**（g4），规范那句 Decision 的上下文是对上游 TODO 的描述，且「修好」只需要 1 处收集点改动，而「报错」要在 VB 里新增一条**别处不存在**的 GoTo 限制（与 `spec:266`「A top-level `GoTo` is an ordinary executable statement」自相矛盾）。
2. **F09 的修法选型**（A 标错节点 / B 让发射门看见 / C `GetInstance` 复制诊断）：本计划取 **B**（见 `design-detailed.md` §F09 修法选型表），仅作备案登记，无需作者决策，除非作者偏好 A 的最小面。

## 状态行

- **计划状态**：四件套已产出；**Accepted 门前**（待 author/验证者核对 §八 七条）。
- **实现状态**：F09 已落地（工作树：`Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb`；新增 `Compilers\VisualBasicEmitTest\Emit\InitializerDiagnosticGatingTests.vb` 与 `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`，`scripts\verify-vb-compiler-tests.ps1` 的 Emit 期望值同步）；§七 其余单元（F04–F08 / F10 / F11）与 W-GATE 未开始（对应行仍 `todo`）。
- **F09 的读取口径**：F09 系 author 直接指令先行落地（未走 §八 的 Accepted 门）⇒ `design-detailed.md` 的 §F09 蓝图、不变量与 pass 条件按**已落地**读并据此核对；§七 其余单元行仍按蓝图读。
- **待确认**：§十 第 1 条（F11 判定方向）。
