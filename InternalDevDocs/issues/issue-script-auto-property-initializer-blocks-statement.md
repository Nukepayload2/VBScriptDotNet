# [BUG] 顶层自动实现属性之后的下一条顶层语句被解析层报 BC30188「应为声明」——与初始化器无关，取决于该语句的首 token

**状态**：**Fixed**（已验证，commit 待作者提交后补；2026-09-22 登记；`tasks\auto-property-top-level-gate\`：AG-F01 取证 → 判据冻结 → 实现 + 28 条解析面 / 4 条绑定面新用例。**档 1**：七门 Failed 全 0（Syntax 4098/4095、Semantic 5847/5743，其余五门数字逐字不变）、`Scripting\VisualBasicTest` 直跑 **745/0**。**档 2（重建发布版宿主后逐格对账）**：`O1`/`O2`/`P1`/`P2`/`P7`/`P8`/`P11`/`P14` 由 BC30188 变零诊断并取到值；`P13` 的 BC30188+BC30205 消失（只剩探针自身缺 `Imports System.ComponentModel` 的 BC30002，属探针缺陷）；`P12` 转成 `BC30662`；`P9`（脚本内 `Class` 体）与 `P15`（`Namespace` 内）**仍是错** ⇒ 反例锁成立。账本已登记 `upstream-merge.md` §2.25(b)。**未提交 ⇒ commit 号不预填**。标题里的"带初始化器"是登记时误判，正文已按实测更正为"与初始化器无关"。
**语义裁定（作者，2026-09-22）**：属性初始化的赋值应当编译进初始化器（与顶层 `Dim` 同构、按源码序生效）——`O3` 实测已表明确实如此，故本条坏的不是顺序语义而是解析层拒绝了「后随的顶层语句」。设计问题以 C# 脚本模式的现行做法为第一手对照。
**取证更正（AG-F01）**：触发面不是「自动实现属性 **+ 有 `=` 初始化器** + 后随可执行语句」——**该判读已被实测推翻**：无初始化器的 `ReadOnly Property P As Integer` 同样触发（`P2`）。真正的判别量是**后随语句的第一个 token 类别**。
**证据等级**：**已运行 + 已实锤**（档 2：发布版宿主 `2.0.0-Beta+c15a959` 下 16 个探针逐条读数；档 3：`Compilers\VisualBasic\Portable\Parser\**` 与 `Compilers\CSharp\Portable\**` 逐跳文件:行）。日志 `tmp\vortex-logs\auto-property-top-level-gate\01-investigator-ag-f01.md`
**严重度**：中（合法脚本形状被拒；不崩；插一条声明即可绕过，故易被误判为「写法问题」）
**影响面**：脚本模式（`.vbx` 文件执行）顶层代码里，自动实现属性**之后同树内**的第一条实质语句；**REPL 逐条提交不受影响**（`ReadOnly Property PR As Integer = 5` 一条 + `Console.WriteLine(PR)` 一条 ⇒ 实测输出 `5`，因每条提交各自成树、属性那条由 EOF 关块）。普通编译无对应形状（类体里本就不能放可执行语句）

## 症状

```vb
Imports System

ReadOnly Property P As Integer = 5
Console.WriteLine(P)
```

```text
O1-autoinit-then-statement.vbx(4) : error BC30188: 应为声明。
Console.WriteLine(P)
~~~~~~~
```

诊断报在**下一条语句**上、说的是「应为声明」，而书写者并没有写下任何非声明内容——错位与病灶不同处。

## 判别性对照（全部已运行；初版探针 `O1`–`O5` 在 `tmp\spec-check-me\`，取证探针 `P1`–`P15` 在 `tmp\probe-auto-property-gate\`）

| 探针 | 形状（后随条目） | 实测 | 结论 |
|---|---|---|---|
| `O1` | 自动属性带初始化器 + 紧接一条顶层语句 | ❌ BC30188（报在语句行） | 基线复现 |
| `O2` | 同上，中间**加空行** | ❌ BC30188 | 空行是 trivia，不产生条目 ⇒ 与空白无关 |
| `O3` | 自动属性 + 再一条**声明**（`Dim q As Integer = 1`）+ 语句 | ✅ 输出 `6` | **顺序语义本就正确**，坏的只有解析这一步 |
| `O4` | **完整属性**（带 `Get`）+ 语句 | ✅ 输出 `5` | 块属性由显式 `End Property` 定界 ⇒ 无残留语境 |
| `O5` | 顶层**字段**带初始化器（`Dim fld As Integer = 5`）+ 语句 | ✅ 输出 `5` | 字段走声明 arm，不需编译单元语境 |
| `P1` | `Property P As Integer = 5`（**非** `ReadOnly`）+ 语句 | ❌ BC30188 | `ReadOnly` 非必需 |
| `P2` | `ReadOnly Property P As Integer`（**无**初始化器）+ 语句 | ❌ BC30188 | **初始化器与触发无关**（推翻初版判读） |
| `P6` | 自动属性 + `If True Then … End If` | ✅ 输出 `if-ok 5` | 关键字开头的块语句合法 |
| `P7` | 自动属性 + 赋值语句 `x = P + 1` | ❌ BC30188（span=`x`） | 同族：首 token 是标识符 |
| `P8` | **两条**自动属性 + 语句 | ❌ BC30188 | 每条自动属性各自留一个窗口 |
| `P9` | **脚本里的 `Class` 体内**：自动属性 + 语句 | ❌ BC30188 | 修复后**必须仍是错**（普通类体语义，反例锁） |
| `P10` | 自动属性初始化器含 `Await` + 声明 + 语句 | ✅ 输出 `6` | `Await` 在顶层自动属性初始化器里已被接受且按源码序生效，不属 BC37341/BC36937 族 |
| `P11` | 同 `P10` 但**紧接**语句 | ❌ BC30188 | 解析错掩盖了该形状的语义面 ⇒ 不能据 `P11` 判 `Await` 有问题 |
| `P12` | `<Extension> ReadOnly Property P As Integer = 5` + 语句 | ❌ BC30188 | 特性/修饰符不影响这道门 |
| `P13` | 自动属性 + `AddHandler c.DoWork, Sub(s,e) …` | ❌ BC30188 **+** BC30205 | 同一窗口的**第二个报点**（访问器语境 arm） |
| `P14` | `ReadOnly Property P As Integer = 5 : Console.WriteLine(P)`（同行冒号分隔） | ❌ BC30188 | 窗口粒度是「每条语句」，与换行无关 |
| `P15` | 脚本里 `Namespace` 内：自动属性 + 语句 | BC36965 + BC30188 | `Namespace` 在脚本里本就被禁；BC30188 同窗 |
| REPL | 属性一条提交、语句一条提交 | ✅ 输出 `5` | 逐条提交正常 ⇒ 反例锁，修不得这条路径 |
| `P4b`/`P4d`（对照） | `?5` 单条 / `?P` + 语句（**无**自动属性） | `BC31003` / 无打印 | 证「`?` 打印语句在 .vbx 末条本就不打印」是**独立既有行为**，与本缺陷无关 |

**触发条件定稿**：顶层自动实现属性（与 `ReadOnly`、与 `=`/`As New` 初始化器、与特性/修饰符**均无关**）之后，**同一棵树内**紧跟的下一条实质语句，若其首 token 是

- **标识符**（调用、赋值、裸表达式等）→ `Parser\Parser.vb:819` BC30188；或
- **`AddHandler` / `RemoveHandler`**（`P13` 实证）→ `Parser\BlockContexts\DeclarationContext.vb:147` BC30188（并附带 BC30205）

就被拒。首 token 是其它语句关键字（`If` / `For` / `While` / `?` …）或下一条本身是声明（`Dim` / `Property` / `Sub` …）则正常。**未测**：`RaiseEvent`、行号标签形状（见 §未复现）。

## 根因（已实锤，档 3 逐跳 + 档 2 行为反证）

自动属性走的是**惰性关块**（要等下一条条目才能确定它是 auto 还是 block），而 BC30188 在 `Parse()` 阶段就按「当前仍在声明语境」下判词——**判词比关块早一步**。

1. 驱动循环 `Parser\Parser.vb:443-490`：每轮 = `_context.Parse()`（:484）→ `LinkSyntax`（:486）→ `ResyncAndProcessStatementTerminator`（:487）。**先解析、后换语境**。
2. `ReadOnly Property P As Integer = 5` 解析成 `PropertyStatement` 后，`DeclarationContext.ProcessSyntax`（`DeclarationContext.vb:111-136`）在 :136 **无条件 push 一个 `PropertyBlockContext`**，只把 `isPropertyBlock` 传成 `False`（:118-134：仅 `Default`/`Iterator` 算块属性）。
3. 下一条条目的 `Parse()` 因此落在 `PropertyBlockContext`（继承 `DeclarationContext`）上：`DeclarationContext.vb:20-22` → `Parser.ParseDeclarationStatement`（`Parser.vb:633`）→ `ParseDeclarationStatementInternal`（:660）→ `Case IdentifierToken`（:784）→ 因 `Context.BlockKind = PropertyBlock ≠ CompilationUnit`（**:812**）落到 :819 报 BC30188。**错误在解析期就产生，此时还没走到任何能关闭自动属性块的代码。**
4. 关块与收集层**本来就是对的**：`PropertyBlockContext.ProcessSyntax` 的 `Case Else`（`PropertyBlockContext.vb:65-79`）→ `EndBlock(Nothing)`（:70）→ `PropertyBlockContext.EndBlock`（:109-142）按 `IsPropertyBlock`（:25-29）认出「自动属性」⇒ 不建 `PropertyBlock`、不补 missing `End Property`，把 `PropertyStatement` 直接 `Add` 给外层（:139-141）再把**当前这条**转交外层（:79）；外层 `CompilationUnitContext.ProcessSyntax`（`CompilationUnitContext.vb:65-72`）在 `IsScript` 下走 `TryProcessExecutableStatement`（`BlockContext.vb:428-538`，Case Else :530-536）⇒ 语句被正确收进编译单元体。这正是 `O3` / `P6` / `P10` 能取到正确值的原因。
5. 为什么插一条 `Dim` 就恢复：`Dim` 命中声明关键字 arm（`Parser.vb:697-720`，`DimKeyword` 在 :714），**该 arm 不要求 `BlockKind = CompilationUnit`**，故在 `PropertyBlockContext` 里照样合法解析；解析完才走第 4 步关块，于是下一条真语句已在 `CompilationUnitContext` 下。
6. 为什么 `?` / `If` 开头的语句能过：它们不在 :667-863 的声明 arm 名单里，落到 `Case Else`（:860-862）→ `ParseStatementInMethodBodyInternal()` → 第 4 步关块 → 被当普通顶层语句收下。
7. 为什么只有自动属性会留这种跨条目窗口：其它块（`Class`/`Module`/`Sub`/`Function`/`Get`/`Set`/`Event`/`Namespace`…）都由显式 `End X` 经 `BlockContext.LinkSyntax`（`BlockContext.vb:358-399`）在**解析下一条之前**关闭（审查结论；`O4` 为旁证）。
8. `P13` 命中第 3 个报点的原因：`Parser.vb:829` / `:835` 的 `AddHandler`/`RemoveHandler` script 守卫同样要求 `BlockKind = CompilationUnit`，不满足则掉进访问器 arm → `DeclarationContext.vb:147`。

BC30188（`ERR_ExpectedDeclaration`，定义 `Errors\Errors.vb:222`）在 VB 产品源码里的**全部**报点共 3 处：上表所引 `Parser.vb:819`、`Parser.vb:875`（标识符后紧跟 specifier 声明时挂到被吃掉的前导标识符上，与本形状无关且在此不可达）、`DeclarationContext.vb:147`。取取证过程中的一条方法论教训：按直觉符号名 `ERR_DeclarationExpected` grep 得 0 命中，必须先拿错误码数字反查 `Errors.vb`（见 pitfalls `P-006`）。

## C# 脚本模式对照（档 3 读码，全部本仓文件:行）

| 维度 | VB 现状 | C# 做法 | 裁定 |
|---|---|---|---|
| 顶层条目分发 | 逐条驱动但**语境跨条目残留**（`Parser.vb:443-490` + `PropertyBlockContext.vb:25-29/65-79/109-142`） | **平坦、无残留**：每个条目独立调 `ParseMemberDeclarationOrStatement(CompilationUnit)`（`Parser\LanguageParser.cs:727-729` → :2590-2615），成员 vs 语句在同一次分发内决定（:2775-2803） | **不照做**（C# 声明以 `;`/`{}` 自定界，VB 以 `End X` 定界 ⇒ 自动属性必须投机开块；平坦化是方言级重写，风险远超本任务）。只把 VB 残留语境的判据开准 |
| 声明与语句能否交错 | 方言上允许（`spec\spec-scripting-dialect.md:64`），但解析层有本缺陷窗口 | **脚本模式对顺序零约束**：CS8803 只在 `!IsScript` 才报（`Parser\LanguageParser.cs:776-793`，判定点 :789-792；`IsScript` 定义 `Parser\SyntaxParser.cs:224-227`；码名 `Errors\ErrorCode.cs:1818`） | **照做（方向一致）**：顺序的裁定权放在**语言模式**这一维，不是「上一条是不是声明」；本修复同样由 `IsScript` 门住，非脚本面不得缩小 |
| 顶层字段/自动属性初始化器与全局语句 | `SourceMemberContainerTypeSymbol.vb:2600-2607`（字段）、:2617-2629（全局语句）、:2633-2650（`CreateProperty` 取 `Initializer`/`AsNewClause`）混进同一 `instanceInitializers` | `SourceMemberContainerSymbol.cs:5826`（字段）、:6093（全局语句）、:5900-5926（自动属性后备字段，:5908-5916 还收 `var`/模式变量）；绑定同一循环同一顺序 `Binder\Binder_Initializers.cs:190-240`，发射 `Lowering\InitializerRewriter.cs:29-77` | **照做（已一致）**：收集/发射层两边已对等，本任务不动 |
| 有没有「上一条声明把下一条语句判成非法」的机制 | 有（本缺陷） | **没有**：每条目自洽，且该状态机对 `IsScript` 关闭 | **无 C# 先例可依 ⇒ 本条是纯缺陷、向 C# 收敛**，不构成分叉 |

诚实标注：**无 C# 脚本宿主**可跑（仓内只有 `vbi`），C# 全部结论为档 3 读码；「C# 脚本顶层可声明自动属性且其初始化与语句按源码序混排」由上述读码推得，未实跑。

## 修复方向（AG-F02 的实施边界）

**采纳候选 A（判据化，不做结构重写）**：

- 在 `Parser\BlockContexts\BlockContext.vb`（`BlockKind` :175、`Parse` :288 附近）新增可下钻的语境判据，例如 `Friend Overridable ReadOnly Property AcceptsScriptTopLevelStatement As Boolean`，基类默认 `Parser.IsScript AndAlso BlockKind = SyntaxKind.CompilationUnit`；`PropertyBlockContext`（`PropertyBlockContext.vb:25-29` 已有 `IsPropertyBlock`）覆盖为 `Not IsPropertyBlock AndAlso PrevBlock IsNot Nothing AndAlso PrevBlock.AcceptsScriptTopLevelStatement`。
- `Parser.vb:85-90`（`IsTopLevelScript`）与 **:812** 的字面 `BlockKind` 比较改走该判据 ⇒ `:819` 的 BC30188 不再对本形状开火。
- **同批必修**：`Parser.vb:829` / `:835`（`AddHandler` / `RemoveHandler`，`P13` 实证同窗）改走同一判据，否则该族留洞。
- **显式不动**：`Parser.vb:843` / `:846`（`Get` / `Set`）——自动属性之后出现 `Get`/`Set` **本就该**把它定形成块属性，且既有断言 `Scripting\VisualBasicTest\ScriptModeParserArmConformanceTests.vb:85-97`（顶层裸 `Get` → BC30188）与 `:101-113`（顶层裸 `Set` → BC30188+BC30205）锁着这条口径；`ScriptModeApiSurfaceConformanceTests.vb:172-182` 亦把 BC30188 当"坏语法"样本。**不得顺手放开**。
- **同批补测再定**：`Parser.vb:774`（IntegerLiteralToken 臂）与 `:841`（`RaiseEvent`）是否同窗**未实测**，AG-F02 须先为这两个形状写用例量出真值，再决定套用判据或记为「本 issue 不收」——不许默认沉默。
- 判据自带三重边界：`Parser.IsScript`（Regular 编译零变化）、`Not IsPropertyBlock`（一旦见过 `Get`/`Set` 即永久关闭放行）、外层必须是脚本编译单元（`Class`/`Namespace` 体内的自动属性不放行 ⇒ `P9`/`P15` 仍是错）。

**否定候选 B**（把 BC30188 从解析期移到语境层 `DeclarationContext.vb:234-242`）：会改普通编译的错号与 span，正面撞反例锁 `P9`，回归面最大。
**否定候选 C**（解析属性时投机定形 auto/block）：`IsPropertyBlock`（`PropertyBlockContext.vb:25-29`）正是为「只有下一条才可知」而设，提前定形会破坏缺 `End Property` 的错误恢复。

**必须新增的锁**：全量与增量解析在同一形状上必须给出相同诊断。`PropertyBlockContext.TryLinkSyntax`（:85-107，非访问器一律 `Crumble` 且 `newContext = Me`）与 `DeclarationContext.TryLinkSyntax`（:352-354 `NotUsed`）走的是另一套判定 ⇒ AG-F02 须补一条「编辑后增量重解析」用例。此条目前**无实跑证据**，属实施时必须证实项而非既成事实。

## 连带义务（跨任务）

- `Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:1257-1269`（在办任务 `script-class-explicit-me-scope` 的 A3 用例）为绕开本缺陷**刻意把自动属性放成提交末条**，注释 :1258-1260 写明原因。修复后须回收加强（其后追一条语句），并与该任务协调先后以避免同文件冲突。
- 本 issue 与 `tasks\script-class-explicit-me-scope\` **无因果关系**：那任务是绑定层判据，本条是解析层；登记时点是在其 F01 写测试过程中被撞见，对照用的是**改动前**的发布版宿主。

## 未复现 / 未查

- `RaiseEvent` 开头的后继语句、`Parser.vb:774`（IntegerLiteralToken 臂）——**未测**，按上节要求由 AG-F02 量真值。
- `P12` 的 `<Extension>` 在修复后会不会转而报「扩展成员须声明在模块里」——该语义判词被解析错挡在后面，未观察；修复后需重跑并如实记录新诊断。
- `Dim arr(1) As Integer = New Integer(){…}` 形状本身即 BC30672（显式界限数组不许显式初始化），探针自误，不属本缺陷（pitfalls `P-007`）。
- 多行自动属性（`Property` 换行写 `= 5`）未测。
- `#Load` 多树下该窗口的树序未测（属反例锁 R3，由实施批次覆盖）。
