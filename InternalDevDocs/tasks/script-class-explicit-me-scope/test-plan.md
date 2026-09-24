# 验收矩阵（script-class-explicit-me-scope）

判据与范围见 `README.md`；本表是验证者的逐项核对清单。**每格都要给档位与证据三态**；档 3（审查）不得冒充档 1（已运行）。层次：**L1** = 编译器语义测试（内存编译）；**L2** = `Scripting\VisualBasicTest` 脚本面用例（宿主 API / `CommandLineRunner`，内存或隔离临时目录）；**L3** = 重建发布版 `vbi` 后复跑 `tmp\spec-check-me\` 探针（集成观察，非必过门槛，判别力来源）。

## 一、F01 判据形状（L1 必过；改前现状列在最后一格供判别）

| # | 形状 | 期望 | 层次 |
|---|---|---|---|
| A1 | 顶层语句 `Console.WriteLine(Me.counter)` | **BC36966**（消息文案与新范围一致） | L1 · L3（探针 A） |
| A2 | 顶层字段初始化器 `Dim x As Integer = Me.counter` | **BC36966**（报警线：现成谓词在此为 `False`） | L1 · L3（探针 L） |
| A3 | 顶层属性初始化器 / `ReadOnly Property` 带 `= Me.counter` | **BC36966** | L1 |
| A4 | 写在顶层语句中的 lambda 体内 `Me.counter` | **BC36966**（报警线：判据不得按词法位置写） | L1 · L3（探针 J） |
| A5 | 顶层 lambda 内的查询表达式（`From … Select Me.x`） | **BC36966** | L1 |
| B1 | 顶层实例 `Function` 体内 `Return Me.counter`，**无**遮蔽 | 零诊断，取到字段 | L1 · L3（探针 B/D2） |
| B2 | 同 B1 但体内有同名局部（`Dim counter = 5`） | 零诊断，且 `Me.counter` 取字段值、`counter` 取局部值（**遮蔽逃生口恢复**） | L1 · L3（探针 C/M 形状） |
| B3 | 顶层实例 `Sub` / `Property` 访问器体内 `Me.字段`、`MyClass.字段` | 零诊断 | L1 |
| B4 | 写在顶层 `Sub` 体内的 lambda 中 `Me.counter` | 零诊断（与普通方法里的 lambda 同形） | L1 · L3（探针 I） |
| C1 | 顶层 `Shared Function` 体内显式 `Me.字段` | **BC30043**（`ERR_UseOfKeywordNotInInstanceMethod1`），**不得**是 BC36966 | L1 · L3（探针 E:24 / 对照 G1） |
| C2 | 顶层 `Shared Sub` 体内隐式读实例字段 | **BC30369**（issue 08 既有行为，锁住不回退） | L1 |
| C3 | 顶层 `Shared Function` 体内 `MyBase` / `MyClass` | **BC30043** | L1 · L3（探针 E） |
| D1 | 顶层实例 `Function` 体内 `MyBase.ToString()` | 零诊断并绑定到 `System.Object` 的实现；运行输出为该实例的类型全名（形如 `Submission#0`） | L1 · L3（对照 K 已实测普通类形状） |
| D2 | 嵌套普通类（脚本里声明的 `Class`）体内三关键字 | **完全不变**（合法，`MyBase` 亦合法） | L1 · L3（C / K / M） |
| D3 | 普通 `Module` 里 `MyBase`、`Structure` 里 `MyBase` | BC32001 / BC30044 **不变**（issue 15 的对照，锁住） | L1 |
| D4 | 非脚本编译（`SourceCodeKind.Regular`）里任意形状 | 零变化：`IsScriptClass` 分支不得被普通类命中 | L1 |

**反例锁（不得顺手改的东西）**：`ImplicitNamedTypeSymbol.vb:59` 返回值不变；`ERR_BadAwaitInSharedInitializer`（BC37341）仍覆盖脚本类任何字段/属性初始化器；顶层 lambda 里的 `Return 42` 仍**零诊断**（本任务不修，若顺手改坏 A4/B4 的对称性要报出）。

## 二、F02 既有断言同步（L1 / L2）

| # | 站点 | 期望改动 |
|---|---|---|
| E1 | `ScriptSemanticsTests.vb:588`（顶层 `Sub` 体内 `Me.sx`） | 由「单条 BC36966」改为**合法并取到字段**，或拆分保留顶层语句格 |
| E2 | `ScriptSemanticsTests.vb:713`、`:724`（体内 `MyBase`，实例与 `Shared`） | `:713` 转合法（`Object` 基）；`:724` 换码 **BC30043** |
| E3 | `CodeGenScriptTests.vb:94-95`、`:115-116` | 顶层语句格保持 BC36966；体内格按新判据（合法 / BC30043）改 |
| E4 | `ScriptModeStatementConformanceTests.vb:487-492`、`:497-502` + 注释 `:483-484` | 两格现用形状是 `Return Me.ToString()` / `Return MyClass.ToString()`——判据放开后它们**不会**变零诊断，而是变 **BC30456**（P-006：提交类符号层无基类型，继承自 `Object` 的成员查不到，另立 issue 跟踪）。故本格是**换形状**：体内两格改用 `Me.字段` / `MyClass.字段`（如 F01 的 B1/B3），期望零诊断；旧形状不再留作 BC36966 断言。注释按新范围改写 |
| E5 | `ScriptModeParserArmConformanceTests.vb:167-174` 注释 | 断言本身不动（顶层语句 + 顶层字段初始化器两格仍 BC36966）；注释去掉「脚本类内一律拒绝」的旧范围叙述 |
| E6 | 新增覆盖 | A2 / A4 / B2 / B4 / C1 / D1 各一条，落在 `Scripting\VisualBasicTest\` 或编译器测试树（就近即可，不新建工程） |

**F01 收口时的红清单（验证者实跑，共 7 条，F02 必须清零）**：L1 五条 = `ScriptSemanticsTests.vb:588`、`:713`、`:724` 与 `CodeGenScriptTests.vb` 的 `MeKeyword`、`MyBaseAndMyClassKeyword`；L2 两条 = `ScriptModeStatementConformanceTests.vb:487`、`:497`（按 E4 换形状后转绿）。F03 跑 gate 时的期望：这 7 条全部消失，且不出现清单外的新红。

## 三、F03 回归面（全绿才算通过）

| # | 手段 | 通过条件 |
|---|---|---|
| G1 | `scripts\verify-vb-compiler-tests.ps1` | 七门（Phase2 / Syntax / Symbol / Semantic / IOperation / Emit / CommandLine）**Failed = 0**。**基线以该脚本内记录的数字为权威**（不是更早任务文档里的旧数），对账规则 = 脚本基线 + 本任务新增用例数：F01 实测 Semantic 总 5843 = 脚本基线 5826 + 本步 17 个 Fact ✔ 一致；F02/F03 新增用例后按同一规则重算 |
| G2 | `Scripting\VisualBasicTest` 程序集直跑 `-automated`（MTP，`dotnet test` 静默不跑） | Failed = 0；`*CrashTests*` 子集全绿 |
| G3 | 重建 `Interactive\vbi` 发布版后复跑 `tmp\spec-check-me\` 16 探针 | 逐格与 §一 期望一致；输出与 `run-results-2026-09-22.txt`（改前基线）**逐格比对**，差异必须全部落在 §一 允许的形状上，出现意外差异即打回 |

## 四、F04 账本与文档（档 3，已检查）

| # | 义务 | 通过条件 |
|---|---|---|
| H1 | `upstream-merge.md:280` 条目 | 该条不再写「**显式**引用路径逐字保持」；改写为「判据由容纳类型收窄为顶层脚本代码 + `Shared` 落普通判据 + `MyBase` 走绑定路径取 `Object`」，并指向本 issue 与本任务 |
| H2 | `issues\README.md` 行 28 与 issue 28 正文 | 状态转 **Fixed**，注明 commit（**不得预填/推测**，未提交前记「已验证，commit 待作者提交后补」） |
| H3 | issue 28 §修复后会新增的行为变化与须补的回归用例 | 6 行逐项落账：每行给出对应用例名或文件:行；未做到的显式标出 |
| H4 | 本任务文件夹 | 状态行改写为「已实施并验证收口」，§一 各非范围项若被触碰须说明理由 |
| H5 | **F02 改名打断的引用账** | F02 把三条测试方法按新语义改名（`TopLevelExplicitMe_ReportsKeywordNotAllowedInScript` → `TopLevelExplicitMeInMethodBody_NoDiagnostics`、`TopLevelMyBaseInInstanceMethod_…` → `…_BindsToSystemObject`、`TopLevelMyBaseInSharedMethod_…` → `…_ReportsUseOfKeywordNotInInstanceMethod`）。按「文件:行 + 方法名」记的引用因此失效，已知命中 `tasks\script-mode-coverage-parity\test-plan.md:450`、`:582` 与 `issues\issue-script-class-explicit-me-in-member-bodies.md:121`；须逐处重指到新名（并保持行号有效），并由 main 落笔（`InternalDevDocs\` 不归子 agent 改） |

## 五、终局判据（main 审计口径）

1. §一 每格都有档位与三态一致的记录；A2 / A4 / C1 三条报警线**必须有运行证据**。
2. G1 + G2 全绿，且 G3 的逐格差异可解释。
3. H1–H4 全部落账，`spec\` 与 `issues\` 正文无本任务擅自改写（判据已在规范里，实现只负责兑现）。
4. 任一 gate 失败无法归因于本改动 ⇒ 停手上报，不放宽判据换取绿灯。
