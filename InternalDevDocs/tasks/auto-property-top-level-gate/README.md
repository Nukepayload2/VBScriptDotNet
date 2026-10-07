# 任务：顶层自动实现属性后的语句被解析层拒绝（auto-property-top-level-gate）——任务计划

> **状态：取证完成、判据已冻结**（AG-F01 于 2026-09-22 交付，日志 `tmp\vortex-logs\auto-property-top-level-gate\01-investigator-ag-f01.md`；§二 判据已按实测回填，无 `TBD`）。**AG-F02 实施待构建面腾空**（与在办任务 `script-class-explicit-me-scope` 的 F03 全量回归排他）。流水账在 `tmp\vortex-logs\auto-property-top-level-gate\`。
> 缺陷登记：`..\..\issues\issue-script-auto-property-initializer-blocks-statement.md`（issue 30）。**子 agent 禁止阅读 `issues\`**——症状、对照与预期已在 `test-plan.md` 与本文件复述自足。

- **一句话**：脚本顶层写自动实现属性（`ReadOnly Property P As Integer = 5`）之后，**同树内**紧跟的第一条实质语句若以**标识符**（调用/赋值/裸表达式）或 `AddHandler`/`RemoveHandler` 开头，被解析层报 **BC30188「应为声明」**；该形状应当合法，且属性的初始化赋值必须已经按源码序进入初始化器。
- **已实锤的现状（改动前发布版宿主 `2.0.0-Beta+c15a959`；探针 `O1`–`O5` 与 `P1`–`P15`）**：
  | 形状 | 实测 |
  |---|---|
  | 自动属性（带或**不带**初始化器、`ReadOnly` 与否、有无 `<Extension>`）+ 紧接以标识符开头的语句 | ❌ BC30188（报在语句行，`Parser.vb:819`）⇒ **与 `=` 初始化器无关**（`P2`/`P1`/`P12`） |
  | 自动属性 + `AddHandler c.DoWork, Sub(s,e) …` | ❌ BC30188 **+** BC30205（**第二个报点** `DeclarationContext.vb:147`） |
  | 同上，中间加空行；或同行用 `:` 分隔 | ❌ BC30188（`O2`/`P14`）⇒ 窗口粒度是「每条实质语句」，与空白/换行无关 |
  | 自动属性 + 关键字开头的语句（`If … End If`） | ✅ 输出 `if-ok 5`（`P6`）⇒ 不被本窗口拒 |
  | 自动属性 + 再一条 `Dim` 声明 + 语句 | ✅ 输出 `6`（`O3`）⇒ **属性初始化赋值确实已按源码序进入初始化器**，坏的只是"后随语句"这一步 |
  | 自动属性初始化器含 `Await` + 声明 + 语句 | ✅ 输出 `6`（`P10`）⇒ `Await` 已被接受且按序生效，不属 BC37341/BC36937 族 |
  | 完整属性（带 `Get`）+ 语句；顶层字段带初始化器 + 语句 | ✅ 各输出 `5`（`O4`/`O5`）⇒ 块属性与字段都不留跨语句窗口 |
  | **脚本里 `Class` 体内**自动属性 + 语句；`Namespace` 内同形状 | ❌ BC30188（`P9`；`P15` 另带 BC36965）⇒ **修复后必须仍是错**（反例锁） |
  | **REPL 逐条提交**（属性一条、语句一条） | ✅ 输出 `5` ⇒ 提交路径本就正常，修复不得改坏（反例锁） |
- **根因（实锤）**：自动属性走**惰性关块**（`PropertyBlockContext` 在下一条条目到来时才由 `IsPropertyBlock` 认出「这是自动属性」并关块），而 BC30188 在 `Parse()` 阶段就按「当前仍在声明语境」下判词——**判词比关块早一步**（`Parser.vb:443-490` 先 `_context.Parse()` 后 `LinkSyntax`；`DeclarationContext.vb:136` 无条件 push；`Parser.vb:812` 字面比较 `BlockKind = CompilationUnit`）。关块与收集层都是对的 ⇒ 修复只动**语境判据**。
- **作者裁定的语义方向（本任务的判据）**：`属性初始化的赋值应该编译到初始化里面`——即顶层自动属性与顶层 `Dim` 同构（后备字段的初始化赋值进脚本初始化器、按源码序执行），其后的顶层语句是普通的下一条语句。**不许**用「把属性挪到文件末尾」或「要求用户插一条声明」来绕。
- **设计问题一律先查 C# 脚本模式**：AG-F01 已交付 §五 的对照（结论：C# 侧不存在「上一条声明把下一条语句判成非法」的机制，且 CS8803 的顺序规则**只对非脚本**生效 ⇒ 本条是纯缺陷、向 C# 收敛，不构成有意分叉）。

---

## 一、范围与非范围

### 范围内
| # | 单元 | 说明 |
|---|---|---|
| F01 | 根因取证 **（已完成 2026-09-22）** | 解析层为什么在该形状后进入「等待声明」状态：BC30188（真名 `ERR_ExpectedDeclaration`，`Errors\Errors.vb:222`）的全部报点、自动属性在编译单元上下文里的续行路径、为什么插入一条声明就恢复、C# 脚本模式对照。**产出：`tmp\vortex-logs\auto-property-top-level-gate\01-investigator-ag-f01.md`** |
| F02 | 修复 | 让「顶层自动属性 + 后随顶层语句」合法，属性初始化赋值进初始化器且**按源码序**在语句之前生效。落点由 §二 判据 5 冻结 |
| F03 | 单元测试 | `Scripting\VisualBasicTest`（宿主/脚本面）与 `Compilers\VisualBasic*Test`（解析/语义面）双侧补测；顺序生效要有**取具体值**的断言（不是只跑不验）；另含判据 4 末条要求的两个未测 arm 真值用例、判据 6 的增量重解析用例 |
| F04 | 回归 | 七门 gate + `Scripting\VisualBasicTest` 全量 `-automated` + 宿主探针复跑（含 issue 30 的 `O1`–`O5` 变体） |
| F05 | 账本 | `upstream-merge.md` 入账（若落在上游同名文件）、issue 30 状态转 Fixed、`spec\` 侧如需补一句顺序口径由 main 落笔 |

### 非范围
- **不动** `Binder_Expressions.vb` 的显式关键字判据（在办任务 `script-class-explicit-me-scope` 的面，避免同文件冲突）。
- **不涉及** issue 29（脚本类基类型判据与 C# 不同形）——该条已按 C# 实测改判，见 `tasks\submission-object-member-lookup\`，与本任务无因果关系。
- **不改**「顶层成员声明与可执行语句可交错」这一方言既有性质（`spec\spec-scripting-dialect.md:64`）；本任务只是让一个被误拒的形状通过。
- **不做解析器平坦化重构**（不照抄 C# 的 `ParseMemberDeclarationOrStatement` 顶层分发）：C# 声明以 `;`/`{}` 自定界、VB 以 `End X` 定界 ⇒ 自动属性必须投机开块，改结构属方言级重写，风险远超本任务。
- **不碰收集/发射层**：`SourceMemberContainerTypeSymbol.vb:2600-2607`（字段）/:2617-2629（全局语句）/:2633-2650（`CreateProperty` 取初始化器）与 C# 侧 `SourceMemberContainerSymbol.cs:5826`/:6093/:5900-5926 已对等，坏的只有解析期语境判定。
- 不引入新诊断码、不改 resx/xlf。

## 二、判据（F02 的实现边界，已按 AG-F01 实测冻结）

1. 顶层自动实现属性之后**同树内**紧跟的、以**标识符**开头的语句（调用 / 赋值 / 裸表达式）**零诊断**，且该语句读到属性后备字段的初值（**值断言**）。触发与 `ReadOnly`、与 `=`/`As New` 初始化器、与特性/修饰符**均无关**（`P1`/`P2`/`P12`）⇒ 判据不得写成「带初始化器才合法」，也不得以「插一条声明」为通过条件。
2. 顺序语义与顶层 `Dim x = 5` 一致：初始化赋值在源码序的下一条语句之前完成；跨 `#Load` 多树时按树序（加载树在前）。`Await` 初始化器同构（`P10` 实测已按序生效，不属 BC37341/BC36937 族）。
3. 不放宽到「任何非声明 token 都当语句」：普通编译（`SourceCodeKind.Regular`）里类体出现游离语句仍报各自普通诊断，**BC30188 在非脚本面不得缩小**；**脚本里 `Class` / `Namespace` 体内**的同形状（`P9`/`P15`）**修复后必须仍是错**。
4. **同窗第二报点必修**：以 `AddHandler` / `RemoveHandler` 开头的后继语句（`P13`：BC30188 + BC30205）与判据 1 同因同修。`RaiseEvent`（`Parser.vb:841`）与 IntegerLiteralToken 臂（`:774`）**未实测** ⇒ F02 须先写用例量出真值，再决定套用同一判据或显式记为「本 issue 不收、另立 issue」，**不得默认沉默**。
5. **修复落点（候选 A：判据化，不做结构重写）**
   - `Parser\BlockContexts\BlockContext.vb`（`BlockKind` :175、`Parse` :288 附近）新增可下钻判据，例如 `Friend Overridable ReadOnly Property AcceptsScriptTopLevelStatement As Boolean`，基类默认 `Parser.IsScript AndAlso BlockKind = SyntaxKind.CompilationUnit`；`PropertyBlockContext` 覆盖为 `Not IsPropertyBlock AndAlso PrevBlock IsNot Nothing AndAlso PrevBlock.AcceptsScriptTopLevelStatement`（`IsPropertyBlock` 已存在于 `PropertyBlockContext.vb:25-29`）。
   - `Parser.vb:85-90`（`IsTopLevelScript`）与 **:812** 的字面 `BlockKind` 比较改走该判据 ⇒ `:819` 的 BC30188 不再对本形状开火；`:829`/`:835` 同批改走（判据 4）。
   - **禁动**：`Parser.vb:843`/`:846`（`Get`/`Set`）——自动属性之后出现 `Get`/`Set` 本就该把它定形成**块属性**，且既有断言 `Scripting\VisualBasicTest\ScriptModeParserArmConformanceTests.vb:85-97`（裸顶层 `Get` → BC30188）、`:101-113`（裸顶层 `Set` → BC30188+BC30205）与 `ScriptModeApiSurfaceConformanceTests.vb:172-182`（把 BC30188 当"坏语法"样本）锁着这条口径。
   - **禁动**：`Parser.vb:875`（specifier 声明报点，本形状下不可达）；`DeclarationContext.vb:234-242`（候选 B 已否定——把 BC30188 移到语境层会改普通编译的错号与 span，正面撞判据 3）。候选 C（解析属性时投机定形 auto/block）同样否定：`IsPropertyBlock` 正是为「只有下一条才可知」而设，提前定形会破坏缺 `End Property` 的错误恢复。
6. **全量与增量必须一致**：同一形状在全量解析与编辑后增量重解析下给出相同诊断。`PropertyBlockContext.TryLinkSyntax`（:85-107，非访问器一律 `Crumble` 且 `newContext = Me`）与 `DeclarationContext.TryLinkSyntax`（:352-354 `NotUsed`）走的是另一套判定，此条目前**无实跑证据** ⇒ F03 必须以用例证实，不得当作既成事实。
7. **与 C# 无需分叉**：C# 侧不存在「上一条声明把下一条语句判成非法」的机制，且其顶层顺序规则 CS8803 明确**只在非脚本**生效（`Compilers\CSharp\Portable\Parser\LanguageParser.cs:776-793`，判定点 :789-792；`IsScript` 见 `Parser\SyntaxParser.cs:224-227`；码名 `Errors\ErrorCode.cs:1818`）⇒ 本修复是**向 C# 收敛**，不新增分叉，也不需要写进 `spec` 的分叉清单。

## 三、批次与串行约束
F01（只读，禁构建）**已完成** → main 冻结判据 **已完成（§二）** → **F02+F03（同一次重建面，当前排队）** → F04 → F05。与在办任务 `script-class-explicit-me-scope` **不得并发跑构建/测试**（同一批 dll，本任务 pitfalls `P-003` 会 BC2012 锁文件）：该任务的 F03 全量回归跑完前，本任务只允许只读取证与探针（探针用现成发布版宿主，不重建）。**同文件连带**：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:1257-1269`（那条任务为绕本缺陷把自动属性放成末条）归 F03 回收加强，须排在该任务 F03 回归之后以避免同文件冲突。

## 四、验证档位
F01 档 2（现成宿主跑 16 个探针 + REPL 提交）+ 档 3（源码审查）；F02/F03 档 1（自动化断言）；F04 档 1 + 档 2（重建发布版宿主后复跑探针）；F05 档 3。低档不得冒充高档。**C# 侧全部为档 3 读码**（仓内无 C# 脚本宿主可跑）。

## 五、C# 对照义务（设计问题的第一手资料）——**AG-F01 已交付**
1. **交错规则**：C# 脚本模式对顺序**零约束**——CS8803 的判定点显式带 `if (!IsScript)`（`Compilers\CSharp\Portable\Parser\LanguageParser.cs:776-793`，:789-792；`IsScript` = `Options.Kind == SourceCodeKind.Script`，`Parser\SyntaxParser.cs:224-227`；码名 `Errors\ErrorCode.cs:1818`）。⇒ 顺序的裁定权在**语言模式**这一维，本修复同样由 `IsScript` 门住。
2. **收集层对偶**：C# 把字段初始化器（`SourceMemberContainerSymbol.cs:5826`）、自动属性后备字段初始化器（:5900-5926，其中 :5908-5916 还收 `var`/模式变量）与全局语句（:6093）混进**同一条 `instanceInitializers`**，绑定在同一个循环同一个顺序（`Binder\Binder_Initializers.cs:190-240`），发射走 `Lowering\InitializerRewriter.cs:29-77`；脚本类成员来源 `Declarations\DeclarationTreeBuilder.cs:270-300`/:322-356。VB 侧同构（`SourceMemberContainerTypeSymbol.vb:2600-2607`/:2617-2629/:2633-2650）⇒ **已一致，本任务不动**。
3. **结论**：C# 侧**不存在**「上一条声明把下一条语句判成非法」的机制（顶层每条目自洽分发，`LanguageParser.cs:727-729` → :2590-2615），因此本条**无可依据的 C# 先例、也无需有意分叉**——它是本仓解析层的孤立缺陷，修复方向即向 C# 收敛。逐行表见 `tmp\vortex-logs\auto-property-top-level-gate\01-investigator-ag-f01.md` §T4。

## 六、风险与停止上报
- 解析层改动的回归面大（`Syntax` 门 4000+ 用例）：任何放宽都必须由「编译单元 + 脚本类 + 后随可执行语句」这一精确条件门住，不得全局改状态机。
- 若取证显示需要动 `ParseStateMachine` / `ParseToken` 的核心分支 ⇒ 停手上报复核，不硬改。
- 与在办任务的同文件冲突（`SourceMemberContainerTypeSymbol.vb` 若两边都要动）由 main 排先后，不并行。
- **复核结论（针对 AG-F01 提出的「候选 A 需复核」）**：**批准候选 A**，理由＝改动面被三重判据门住（`IsScript` / `Not IsPropertyBlock` / 外层须为脚本编译单元），不触碰 scanner、`ParseToken`、增量链接核心（`TryLinkSyntax`/`LinkResult`），且不新增诊断码。**两点附加条件**：① 判据必须是**语境属性的下钻查询**，不许在 `Parser.vb` 里就地写 `BlockKind = PropertyBlock OrElse …` 这类形状枚举（那会把下一次惰性关块变成新洞）；② 增量重解析一致性（§二 判据 6）从「猜测」升级为**必须实跑证实**，证不出来就停下上报，不得当作既成结论入账。
- **回归对账口径**（F04）：BC30188 既有用例绝大多数属 Regular/声明语境，按 `IsScript` 门住后**不应有任何一条变动**——此判断目前是**审查推断**，必须由实跑证实。清单（AG-F01 给出，逐门对账而非只看总数）：`Compilers\VisualBasicSyntaxTest\Parser\ParseErrorTests.vb:540, :1329, :1345-1354, :4515`、`ParseDeclarationTests.vb:536-572`、`ParseAttributes.vb:174`、`ParseXml.vb:4533, :4562`、`Parser\Scanner\ScannerTests.vb:2069, :2090`、`ParseStatements.vb:3420, :3422`；`Compilers\VisualBasicEmitTest\ErrorHandling.vb:387`、`PDB\PDBExternalSourceDirectiveTests.vb:629, :657`；`VisualBasicSymbolTest\...\WinMdEventTest.vb:1153-1211`、`Binding\BindingErrorTests.vb:23624-23625`。脚本面同族必须复跑：`ScriptModeParserArmConformanceTests.vb:85-97`/:101-113、`ScriptModeApiSurfaceConformanceTests.vb:172-182`。
