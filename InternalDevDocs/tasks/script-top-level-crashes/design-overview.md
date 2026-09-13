# 概要设计：脚本顶层崩溃族一次收口（script-top-level-crashes）

> 状态：总体设计。依据链：`README.md`（判定表 / 分批 / 共享源码事实 / 冲突登记）→ `design-detailed.md`（逐单元改动蓝图与 pass 条件）→ `test-plan.md`（L1–L4 矩阵）。
> 上游依据：`..\..\issues\` 八份 issue（04–11）→ 已采纳会议 RESOLUTION（`..\..\meetings\meeting-submission-shared-members.md` R1/R2/R5/R6/R7；`..\..\meetings\meeting-script-extension-methods.md` R2）→ `..\..\decisions.md` **D4/D5/D6** → `..\..\spec\spec-scripting-dialect.md`。
> 源码事实以 `README.md` §五「共享源码事实」为基准；本文件只引用其**结论**，未复核过的断言一律标三态。编译器路径前缀统一 `Compilers\VisualBasic\Portable\`（简写 `VB\`）。失败点缓存 `<项目根>/tmp/vortex-logs/top-level-implicit-shared/pitfalls.md`（P-001…P-038）。

---

## 1. 目标

**一句话目标**：让脚本（`.vbx` / REPL 提交）顶层这 8 类形状，**要么正常工作、要么报一条能定位到行的诊断**，任何一条都**不再终止编译器进程、不再静默产出坏 IL**。

**目标的可判形式**（即本设计的验收语言）：对 `issues\README.md` 的 04–11 逐条给出

- **判定**（修好 / 报错）+ **判据**（同形状在**普通编译上下文**里的实测行为）；
- **落点**（`文件:行号`）与**不变量**（哪些既有行为一个字都不许变）；
- **无副作用单元测试断言**（`test-plan.md`）。

**为什么现在做**：这 8 条全部是「用户可达 + 编译期零诊断或诊断无效」的进程级失败——退出码分布为 3（NRE）、34（`TypeLoadException`）、35（断言终止）、58（`InvalidProgramException`）、24（运行期损坏产物）。按作者原则，**崩编译器是 bug**。

## 2. 判定原则（把作者原则落成可复核的判据）

1. **崩 = bug**（无论 Debug 断言、Release NRE、宿主 `TypeLoadException`、还是静默坏 IL）。
2. **判定依据必须来自「同形状在普通编译上下文里的行为」**：合法 ⇒ **修好**；报错 ⇒ **报错**并复用同一条诊断（不新造语义）。
3. **零新码优先**：只有「普通上下文里也**没有**对应诊断、但语义上必须有」的形状（04、06）才引入新码；其余一律复用既有码（08 → BC30369、10 → BC36943）。
4. **不动共享发射层**：崩溃栈顶都在 `Compilers\Core\Portable\CodeGen\`（`ILBuilder` / `BasicBlock`，C# 与 VB 共享），修法一律是「不让坏形状活到发射期」，**不给共享层加 VB 专属守卫**。
5. **上游改动面最小**：能改 `VB\Symbols\Source\` 的一处收集点，就不改 `Binding\` 的通用判据；能收窄一条 `Debug.Assert`，就不改 `IsImplicitlyDeclared` 这种被众消费者依赖的谓词。
6. **兼容性不是否决理由**（D6）：本族涉及的全部形状今天**必崩或必错**（`Shared sx = 5` → 34、共享 `Await` → 35、顶层 `Event` → 35、隐式 `Me` → 58、顶层 `GoTo` → 3），**没有在用语义可破坏**；本设计因此**一次也没有**用「这会改掉既有语义」作为取舍依据，D6 边界外的四条（可行性 / 与 D5 同形性 / 机制收益与代价 / 规范可表达性）逐条论证。

## 3. 现状缺口与共同根因：**两根 + 五条独立**

> 原始假说：「这批崩溃同源（脚本顶层语句住在合成的异步宿主 `<Initialize>` 里，发射前置检查与普通方法不一致）」。逐条查证结论：**假说对 09、10 成立；对 11 被实测推翻；对 04–08 不成立。**（完整证据见 `README.md` §三。）

### 3.1 根 A ——「顶层语句走初始化器路径，拿不到方法体路径的检查与发射门」（覆盖 09 + 10）

三环，全部 `文件:行号` 已检查：

| 环 | 事实 | 锚点 | 实测 |
|---|---|---|---|
| A-1 | `<Initialize>` 的方法体是**空壳**（只含退出标签）⇒ 顶层语句不经过 `BindMethodBlock` | `VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`；`VB\Compilation\MethodCompiler.vb:1818` | g3（无 `Await` 的顶层 Try）exit 0 正常 ⇒ 宿主本身健全 |
| A-2 | 方法体级检查（BC36943 / On Error / 行号标签 / `WRN_AsyncLacksAwaits`）全挂在 `BindMethodBlock` 上，对空壳恒空转 | `VB\Binding\Binder_Statements.vb:291`、`:330`、`:602-610` | g6/g7/g15 → exit 3 零诊断；g8（普通 `Async Function` 同形状）→ exit 1、**BC36943 ×2** |
| A-3 | **缺陷机制**：顶层语句改由 `BindFieldAndPropertyInitializers` 绑定，该绑定报出的诊断只落进编译级共享袋；发射门只看空袋 + bound 节点标志 + 空壳 ⇒ 已报出的诊断拦不住发射 | 文件名级锚点：`VB\Compilation\MethodCompiler.vb`（初始化器绑定的诊断袋与发射门）、`VB\Binding\BindingDiagnosticBag.vb`（`GetInstance` 只复制两个布尔）、`VB\Binding\Binder_Initializers.vb`（`ProcessedFieldOrPropertyInitializers.HasAnyErrors`）；登记与逐环锚点见 `..\..\issues\issue-initializer-diagnostic-does-not-gate-emit.md` | v1–v3（嵌套类三形状）exit 35 零诊断 vs v4/v5（同形状另有错）正常打印 BC36937 |

⇒ 09 是 A-3 的直接后果，10 是 A-2 的直接后果；两者**必须成对修**（只做其一会留下「诊断出现但拦不住发射」或「门生效但压根没诊断」）。

### 3.2 根 B ——「顶层语句收集表漏掉 `LabelStatement`」（只覆盖 11）

- 丢弃点 `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2613-2615`（**含上游作者 `' TODO (tomat): should be added to the initializers`**）；相邻的 `:2625-2637` 照常收集顶层可执行语句。
- 标签符号本身存在（`VB\Binding\ExecutableCodeBinder.vb:50-74` 扫整棵语法根建标签表）⇒ `GoTo` 能绑定、**零诊断**，但分支目标永远没有落地块。
- **判别性实测**：g1（前向 `GoTo`，无 `Await`）与 g11（反向 `GoTo`，无 `Await`）都 exit 3、崩点逐字相同（`Core\CodeGen\BasicBlock.cs:325`）；g4（普通 `Async Function` 同形）exit 0 且**跳转生效**。⇒ 与 10 **不同源**，issue 11 正文的「同源」是**推测**，已被证伪。

### 3.3 各自独立（04 / 05 / 06 / 07 / 08）

| # | 根因（锚点） | 母题 |
|---|---|---|
| 04 | `VB\Symbols\Source\SourceMethodSymbol.vb:1504` 与 `:1634` 的 `Debug.Assert(Me.IsShared)`：容器白名单把 `IsScriptClass` 与 `TypeKind.Module` 并列放行（`VB\Symbols\NamedTypeSymbolExtensions.vb:108-111`），而「扩展方法必须 `Shared`」在 `Module` 内不可违反 ⇒ **VB 从无用户可见诊断** | 移植不完整（C# 有 CS1105） |
| 05 | `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737` 的 submission 分支把共享初始化器交给**为实例版设计**的符号（`VB\Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:31-38` 无条件带形参）⇒ 带参 `.cctor` | 共享初始化器的**落点** |
| 06 | `VB\Binding\Binder_Expressions.vb:4726-4727` 的 `IsInAsyncContext()` 不查 `IsShared`（放过），而 `.cctor` 的 `IsAsync` 恒 False ⇒ `AwaitOperator` 存活到 `VB\CodeGen\EmitExpression.vb:206-209` | 同上（放过的**诊断**面） |
| 07 | `VB\Symbols\Source\ImplicitNamedTypeSymbol.vb:33-37` 让脚本/提交类 `IsImplicitlyDeclared` 恒 True，撞三处合成成员断言 | 「只为普通类写的前提」 |
| 08 | `VB\Binding\Binder_Expressions.vb:2260-2270`：注释前提「No code in a script class is shared」在 fork 里已不成立，隐式 `Me` 一律 `Return True`，永不走到 `:2272-2278` 的 BC30369 | 同上 + 共享成员的接收者解析 |

> 08 与 06 共享同一条失效前提（issue 08 / 06 正文各自自述），但**链路不同**（接收者解析 vs async 判据）⇒ 两个单元，**不绑成一包**（与 `meeting-submission-shared-members.md` R7 的「不要并进同一份设计」一致）。

## 4. 总体设计：逐单元一句话

> 每单元的**改动形状 / 判据 / 不变量 / 陷阱**见 `design-detailed.md`。此处只给设计骨架与**为什么是这个形状**。

| 单元 | 判定 | 一句话设计 | 上游同形依据 |
|---|---|---|---|
| **F04** | 报错 | 在扩展方法解码的两个位置（早期 `:1500-1504` + 完整 `:1624-1648`）补「必须 `Shared`」守卫与新诊断，**消息点名 `Shared`** | C# CS1105（`SourceOrdinaryMethodSymbol.cs:243-245`）；`meeting-script-extension-methods.md` R2 |
| **F05** | 修好 | submission 分支按 `isShared` 分叉：共享时走同文件的 `EnsureCtor`（无参 `SynthesizedConstructorSymbol`），实例时保持现状 | C# 的两符号分立（D5 已证实例 1）；会议 R1 的「甲」 |
| **F06** | 报错 | 在**调用结构**层（`BindAwait` 的 `:4740-4744` 一带）补子情形判据，对共享字段/属性的初始化器报一条**新脚本专属码**；判据必须覆盖 `PropertySymbol`（VB 的属性初始化器 `ContainingMember` 是属性，不能照抄 C# 的 backing-field 形状） | C# CS8100（脚本专属）；会议 R2 的「乙」 |
| **F07** | 修好 | 把三处 `Debug.Assert(Not …IsImplicitlyDeclared)` 收窄为 `Debug.Assert(Not …IsImplicitClass)`——**保留原始意图**（真正的隐式类容器不追加合成特性），把脚本/提交类放行 | 断言不是控制流（`SynthesizedEventAccessorSymbol.vb:495-498` 断言后无条件加特性）；Release 下照常发射（`e307d0f` 实测 exit 0） |
| **F08** | 报错 | 在 `:2262-2270` 的脚本类分支里，把**隐式**引用改走 `IsMeOrMyBaseOrMyClassInSharedContext()`，命中共享上下文则落 BC30369；**只对显式引用保留** `ERR_KeywordNotAllowedInScript`（BC36966） | 普通类同形状报 BC30369（q39/q37 实测） |
| **F09** | 修好 | 让**初始化器绑定产生的 error** 参与逐方法发射门：把 `BindFieldAndPropertyInitializers` 的「产生了 error 诊断」传播成 `ProcessedFieldOrPropertyInitializers.HasAnyErrors`（复用 `:1288` 已有的一项判据，不加新门项） | `VB\CodeGen\EmitExpression.vb:207` 注释逐字「Code gen should not be invoked if there are errors.」 |
| **F10** | 报错 | 在初始化器绑定这条已存在的路上，用**已存在的 async-aware `TopLevelCodeBinder`** 对顶层语句的绑定结果跑一次**只查 `Await` 位置**的 walk，报 **BC36943** | BC36943 本身（`Errors.vb:1572`）；`meeting-submission-shared-members.md` R6 把 06 面孔② 归 09、10 由本单元承接 |
| **F11** | 修好 | 把 `:2613-2615` 的 `Exit Select` 换成与可执行语句同款的收集（`AddInitializer(instanceInitializers, …)`，受 `binder.BindingTopLevelScriptCode` 守卫） | 普通方法里的标签语句是正常语句；`VB\Binding\Binder_Statements.vb:59-60` 已有 `LabelStatement` 的绑定分派 |

**判定计数：修好 4（F05 / F07 / F09 / F11）、报错 4（F04 / F06 / F08 / F10）。**

## 5. 分批与依赖（为什么是这个顺序）

```
W1  F09 ──────────────► F10
              └───────► F06   （F06 还需要 F05 先落地）
              └───────► F08 的「初始化器形状」那一半
W2  F05 ──────────────► F06
    F11、F07（无依赖）
W3  F04（无依赖）
```

- **W1 = F09 最先**：它是三个单元共同的「拦得住发射」前提（A-3）。F09 单独不修任何用户可见形状，所以它**不新增诊断**、只让已报出的诊断生效——回归面最小，适合打头。
- **W2 = F05 / F11 / F07**：文件面与 W1 不重叠，可并行；F05 与 F11 都在 `SourceMemberContainerTypeSymbol.vb`（不同区域）⇒ **同文件串行**。
- **W3 = F10 / F08 / F06 / F04**：诊断族集中落地。`Binder_Expressions.vb` 被 F08 与 F06 共用（`:2257-2286` vs `:4740-4744`）⇒ 串行；`Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` / 13 份 `xlf` 被 F06 与 F04 共用 ⇒ 两枚新码**串行相邻**落地。
- **不存在「同批改」的跨单元合并**：每个单元独立验收、独立 pass 条件；批次只表达**顺序与文件占用**，不表达设计耦合。

## 6. 回归风险面（总）

| 风险 | 单元 | 对冲 |
|---|---|---|
| 共享初始化器改由无参共享构造器承载后，与 `CreateSharedConstructorsForConstFieldsIfRequired` 争同一个 `.cctor` 名 | F05 | `meeting-submission-shared-members.md` R1 的 plan 前置实证：同文件 `Const d As Date = #…` + `Shared x As Integer = 5` 必须落到同一个 `.cctor`（**硬 pass 条件**） |
| `beforefieldinit` 语义误判（以为共享初始化器会先于 `<Initialize>` 跑） | F05 / F06 | pitfalls **P-001**：VB **主动**打该标志（`VB\Emit\NamedTypeSymbolAdapter.vb:496-499`），时序是「首次访问该静态字段之前」；**P-003**：运行时行为必须实测，不许由源码唯一推出 |
| 顶层标签入序列会改动「实例桶 span 累加 / 调试语法偏移」 | F11 | 沿用 `AddInitializer` 既有规则（`SourceMemberContainerTypeSymbol.vb:1565-1585`；`:1523` vs `:1524` 两桶不对称）；**不得**把标签当元数据常量跳过；pitfalls **P-011 / P-013**（桶与方法 `isShared` 必须一致，但 `<Initialize>` 走 `:3243` 的脚本类快路径，`:3276` 的 `Throw` 不可达） |
| 「初始化器有 error 就 gate 发射」波及正常编译 | F09 | 只在**新增 error**（非 warning、非既有 error）时置位；`HasAnyErrors` 语义（error 级）必须核对；无初始化器错误的编译逐字节不变（全量回归 + 七门兜底） |
| 把 On Error / 行号标签诊断一起带进顶层 | F10 | **裁决：只做 `Await` 位置检查**，不复用 `VisitBlock` 的 On Error / `reportedAnError` 分支（见 `design-detailed.md` §F10） |
| 改接收者判据会误伤顶层实例成员的隐式访问（`spec:243` 明文允许） | F08 | 双向锁死：实例成员隐式访问**仍合法**、共享成员同写法**报 BC30369**（`issue-submission-shared-members` 正文已点名的回归项） |
| 新码顺序打乱既有扩展方法诊断序列（BC36550 / BC36551 / BC36552 / BC36548 / BC36554） | F04 | 新分支插在序列**末尾**、并核对「无参 + 非 `Shared`」时先报哪一条（`meeting-script-extension-methods.md` OPEN QUESTIONS 的待裁项，落 `design-detailed.md`） |
| 上游合并成本 | 全部 | 见 §8 账本义务；F07 / F11 / F05 都改**当前未登记**的文件（`Symbols\Source\` 下四处）⇒ 必须补登记 |

## 7. 边界与不做

- **不做**：容器种类更改、顶层成员默认 `Shared`、共享初始化器改道并入 `<Initialize>`（`meeting-submission-shared-members.md` R3 已否决，附复活条件）、给共享初始化器支持 `Await`（同 R3）、`Compilers\Core\Portable\CodeGen\` 的任何改动。
- **不做**：`issue 04` 的姊妹项「嵌套容器承载扩展方法」（BC1109 移植），它属 `meeting-script-extension-methods.md` 的候选 F，边界与 F04 不同（两者落在同一段序列，F04 落地时必须**不阻塞**它）。
- **不做**：把 `spec` / `issues` / `meetings` / `proposals` 文本改掉（列为义务，见 `design-detailed.md` §账本与规范义务）。
- **不做**：REPL 打印行为、退出码语义、`#Load` / `#R` / `Imports` 累积（与本族正交）。

## 8. 账本与合并面（概要）

> 详表见 `design-detailed.md` §账本与规范义务。`..\..\upstream-merge.md` 的现状（已检查）：`Binder_Expressions.vb` 在 §2.19 已于册（文件面级锚点），`Errors.vb` 在 §2.19 / §2.8 已于册，`MethodCompiler.vb` / `Binder_Initializers.vb` / `Symbols\Source\SourceMemberContainerTypeSymbol.vb` / `ImplicitNamedTypeSymbol.vb` / `SynthesizedInteractiveInitializerMethod.vb` / 三个合成成员文件 / `SourceMethodSymbol.vb` / `BasicBlock.cs` **均未在册**。

- **新增码两枚**（F04、F06）必须按「新锚点 + 新码位」登记；F04 走 `Errors.vb:1634-1635` 之间现空的 37005 带（`meeting-script-extension-methods.md` OPEN QUESTIONS 已选 37005 为自然选择），F06 走 `ERR_NextAvailable = 37341`（`meeting-submission-shared-members.md` R9(b)）。**实现期须重跑 grep 复核两带现空。**
- **`Compilers\Core\Portable\CodeGen\` 零改动**（见 §2 第 4 条的承诺）⇒ §二·补 的欠账表**不新增**条目。
- **法务/公开发布面零变化**：全部改动在 `Friend` / `Private` / 方法体内，`PublicAPI.*.txt` 零增量（**文件面级判断**，实现期以 PublicAPI 分析器实测为准）。

## 9. 全称主张剪枝自检（Growth 剪枝自检）

> 规则：全文每条全称主张（无 / 都 / 任何 / 唯一 / 即可 / 不可能 / 全部）逐条挂 `文件:行号` 或实测；**举不出证据的一律降级为「推测」或删除**。

| 全称主张 | 证据 | 状态 |
|---|---|---|
| 「这批崩溃对 09、10 成立为一根（初始化器路径），对 11 被推翻，其余五条各自独立」 | 根 A 三环锚点（`SynthesizedInteractiveInitializerMethod.vb:135-142`、`Binder_Statements.vb:330`/`:602-610`、`MethodCompiler.vb:599-623`/`:1288`）+ 根 B 锚点（`SourceMemberContainerTypeSymbol.vb:2613-2615`）+ 实测 g1/g3/g6/g7/g8/g11/g15 | **实锤** |
| 「顶层语句**从不**经过 `BindMethodBlock`」 | `SynthesizedInteractiveInitializerMethod.vb:135-142` 返回空壳 + `MethodCompiler.vb:1818` 是唯一取体点；无第二条路径（构造器走 `:1497-1503`、脚本初始化器走 `:1504-1506`） | **实锤（源码链闭合）**；「无第二条路径」未穷举全仓调用点 ⇒ **推测（高置信）** |
| 「BC36943 与 On Error / 行号标签检查**全**挂在 `BindMethodBlock` 上」 | `Binder_Statements.vb:330` 是该 walker **唯一**调用点（本任务 `grep` 复核：全 `Compilers\VisualBasic\Portable` 内 `CheckOnErrorAndAwaitWalker` 只有定义 + 该调用） | **实锤**（grep 唯一命中） |
| 「三处 `Not …IsImplicitlyDeclared` 断言是**同族可枚举的全部**」 | issue 07 正文的 grep 结论（命中 6 条、其中 3 条同族）；`meeting-submission-shared-members.md` R9(c) 把计数勘误为 6 条，本任务沿用其清单（`MethodCompiler.vb:1841`、`AnonymousDelegate_TypePublicSymbol.vb:26`、`SourceWithEventsBackingFieldSymbol.vb:66`、`SynthesizedEventAccessorSymbol.vb:495`、`SynthesizedWithEventsAccessorSymbol.vb:93`、`SymbolExtensions.vb:426`） | **已检查**（转述会议 R9(c)，未重跑 grep ⇒ 标**推测（高置信）**，实现期须重跑） |
| 「三处断言**都不是控制流**」 | `SynthesizedEventAccessorSymbol.vb:495-498` 与 `SourceWithEventsBackingFieldSymbol.vb:66-77`：断言之后 `AddSynthesizedAttribute(...)` 无条件执行 | **实锤**（逐行已读）；第三处 `SynthesizedWithEventsAccessorSymbol.vb:93` **未逐行读其后的语句** ⇒ 该处是**推测（高置信）**，实现期须核 |
| 「Release 下断言既不终止也不改变发射结果」 | `e307d0f` 实测：顶层 `Event` exit 0 `EVENT-OK`、顶层 `GoTo` 无 `Await` exit 3、顶层 `Shared` 字段 exit 34、普通 async `GoTo`+`Await` exit 0 | **实锤（参照级，较旧二进制）**；HEAD Release **未跑** ⇒ 「HEAD 同形」（**推测**） |
| 「`GetInstance(template)` **只**复制两个布尔、不复制诊断」 | `VB\Binding\BindingDiagnosticBag.vb:50-52` 逐行 | **实锤** |
| 「`ProcessedFieldOrPropertyInitializers.HasAnyErrors` 的来源**只有两个**：本路绑定期报过的 error（调用方经 `bindingReportedErrors` 入参传入）与 bound 节点的错误标志」 | `VB\Binding\Binder_Initializers.vb:54` `Me.HasAnyErrors = bindingReportedErrors OrElse boundInitializers.Any(Function(i) i.HasErrors)`；`:22-28` 文档逐字「Indicate the fact that binding of initializers produced a tree with errors or that the binding of the initializers reported an error diagnostic. Some diagnostics … are reported without marking the bound tree, so the flag has to be supplied by the caller that owns the diagnostics of this binding」（本任务于工作树 `Binder_Initializers.vb` 逐行复核） | **实锤** |
| 「`Using` 内的 `Await` **不在** BC36943 家族（只有 Catch / Finally / SyncLock）」 | `Binder_Statements.vb:593-600`（`VisitUsingStatement` **不**置 `_isInCatchFinallyOrSyncLock`，只置 `_enclosingSyncLockOrUsing`）与 `:579-591`（`SyncLock` 两者都置）对照 + 实测 g10 exit 0 | **实锤** |
| 「`AllowsExtensionMethods` 是**全 VB 侧唯一**容器判据」 | issue 04 正文的已检查结论（`NamedTypeSymbolExtensions.vb:108-111` + 两处消费点 `SourceMethodSymbol.vb:1627` / `:1501`）；`meeting-script-extension-methods.md` R2 复核为「四条消费点」 | **已检查**（转述，未重跑 grep）⇒ **推测（高置信）** |
| 「新码两枚都用**现有空带**」 | F04：`Errors.vb:1634-1635` 之间 37005–37049 现空（会议 OPEN QUESTIONS + issue 04 的码位策略）；F06：`ERR_NextAvailable = 37341`（`Errors.vb:1815`） | **已检查**（沿用既有会议结论）⇒ 实现期须重跑 grep 复核 ⇒ **推测（高置信）** |
| 「`Compilers\Core\Portable\CodeGen\` 零改动」 | 本设计的**承诺**（不是事实主张），由 W-GATE 的 `git diff --stat` 核对 | **承诺**（验收判据） |
| **降级处理** | ①「全仓无第二条方法体绑定路径」→ 推测（高置信）；②「三处断言族穷尽」→ 推测（高置信，实现期重跑 grep）；③「第三处断言也不是控制流」→ 推测（高置信，实现期读）；④「HEAD Release 与 `e307d0f` 同形」→ 推测；⑤「`AllowsExtensionMethods` 唯一」→ 推测（高置信）；⑥「两码带现空」→ 推测（高置信，实现期重跑 grep） | — |

## 10. 与决策文档的关系

- **D5（以 C# / csi 为蓝本）**：F04 对齐 CS1105、F05 对齐 C# 的两符号分立（D5 **已证实例 1**，本任务把它从「登记在册的分叉」推向「已落地的对齐」）、F06 对齐 CS8100、F07 的落点在 C# 侧无对应物（C# 没有脚本类承载 `Event` 的等价断言族）——**四者都给出了「为什么不照抄 C#」或「照抄哪一半」的说明**（见 `design-detailed.md`）。
- **D6（兼容性只对 GA 成立）**：本族一次也没有用兼容性做论据（见 §2 第 6 条）。
- **D4（P1 硬约束）**：F04 / F06 / F08 / F10 只**新增诊断**，F05 / F07 / F11 只**让今天必崩的形状可用**，F09 只**让已报出的错误生效**——**没有任何单元改变合法既有代码的正确语义**，全部满足 P1 硬约束。
