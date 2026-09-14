# 任务：脚本顶层崩溃族第二轮收口（script-top-level-crashes-2）——任务计划

> **状态：计划已产出，并已按本计划实施、验证收口**（U1–U7、U9、U12 全部落地并通过独立验证；U10/U11 判为未修复，登记为 `issues\README.md` 第 18 条）。本文件夹是第二轮的**四件套**：本 README（目标 / 判定表 / 分批 / Vortex 代办 / 探针清单 / 共享源码事实）+ `design-overview.md`（总体设计 + 全称主张剪枝自检）+ `design-detailed.md`（逐单元改动蓝图 + pass 条件 + 账本与规范义务）+ `test-plan.md`（L1–L4 矩阵 + 无副作用纪律 + 全量回归口径）。

- **一句话**：把「**在类体里合法、在合成提交类上却崩编译器或产出坏 IL**」的顶层形状逐条判定为「修好」或「报错」，给出可无人值守串行实施的改动蓝图与无副作用验收网。
- **作者给定的判定原则（沿用第一轮，是本计划的唯一判据）**：
  > 崩编译器是 bug。要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」。
  执行形式：**同形状放进普通编译上下文实测**——合法 ⇒ 走「修好」；报错 ⇒ 走「报错」并复用同一条诊断（或按已采纳会议决议的新码）。
- **与第一轮的关系**：第一轮（`tasks\script-top-level-crashes\`，已提交 `48d8edb`）收口了 `issues\` 04–11 八条；本任务收口**第一轮显式排除在范围外**的第四处断言（README §一 非范围第 3 条）与第一轮实施期发现的预存在崩溃。两轮共用同一判定原则与同一证据纪律。
- **依据链**：`..\..\issues\README.md` → 本任务新增 issue 正文（见 §六）→ `..\..\decisions.md` **D5**（基础功能以 C#/csi 为蓝本）→ `..\..\spec\spec-scripting-dialect.md`（脚本声明/提交模型）→ `..\..\compilers-index.md`（源码地图）→ 第一轮 `tasks\script-top-level-crashes\`（判定原则、探针方法、Vortex 流水账格式）。
- **调度方式**：Vortex 涡流（实施者 agent 产出 → 验证者 agent 按 pass 条件核对 → 打回修复 → 通过关闭），main 只调度、串行交替、不可催促。流水账 `<项目根>/tmp/vortex-logs/script-top-level-crashes-2/`。

---

## 一、范围与非范围

### 范围内（五族，全部**已运行实测**复现）

| # | 单元 | 形状（脚本 / 提交模式顶层） | 症状（探针 exit） |
|---|---|---|---|
| C1 | U1 | `Sub New()` / `Sub New(x)` 顶层声明（`Protected`/`Private`/`Public` 同判定） | **两种症状**：构造器在首位 → 断言终止 `comparison <> 0`（`LexicalOrderSymbolComparer.vb:43`）；前面有任何东西 → `InvalidOperationException: Sequence contains more than one element`（`NamedTypeSymbol.vb:699` 的 `.Single()`） |
| C2 | U2 | 顶层实例 `Event` 在**顶层成员**（`Sub` 或 lambda）里 `RaiseEvent` | 断言终止（原 `#If DEBUG` 内的接收者断言，已由 U2 删除；判据现为 `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:27` 的降级分支）；放宽后落到 `EmitExpression.vb:209` 的 `UnexpectedValue` |
| C3 | U3 | 顶层方法带 `Handles` 子句（实例与 `Shared` 两种） | 断言终止 `Unexpected value 'Submission'`（`SourceMemberMethodSymbol.vb:783`） |
| C4 | U4 | 顶层**显式** `MyBase` 表达式（裸表达式 / 实例 `Sub` / `Shared Sub`） | 断言终止 `Field 'type' cannot be null`（`Binder_Expressions.vb:2375`） |
| C5 | U5 | **任何离开顶层 `Finally` 的分支**——`GoTo` / `Return`（含带值）/ `Exit For`·`While`·`Do`·`Select` / `Continue For`；含嵌套 `Finally`、`Finally` 落在 `Using`·`Catch` 内、`Await` 之后的 `Finally`、`#Load` 进来的文件 | 运行期 `InvalidProgramException`（exit `2148734266`），编译期零诊断。**边界**：同形状放进顶层 lambda 会被正常报 `BC30101` |
| — | U6 | **资源失同步**（非崩溃类，作者指令「遇到资源不同步就同步」） | 见 §三 |
| — | U7 | **薄弱面测试补强**（参考 C# 脚本模式测试风格 + VB 特有语法） | 见 `test-plan.md` |

### 非范围（显式）

- **不改容器种类**：脚本/提交类保持 `TypeKind.Submission`（`meeting-script-extension-methods.md` R21 已裁）。
- **不给共享发射层加守卫**：`Compilers\Core\Portable\CodeGen\` 不出现 VB 形状知识。
- **不补「顶层语句没有流分析」的其余后果**：`MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?` 之下，**顶层语句里的块内局部变量**拿不到 `BC42104`（未赋值即用）——判别性实测：同形状放进顶层 `Sub` 或嵌套类方法**都报** `BC42104`，只有顶层语句序列零诊断。这些是**缺警告**（不是崩溃），本任务只在 U5 收口其中会产出坏 IL 的那一条（`BC30101`），其余登记为 `issues\issue-top-level-statements-skip-flow-analysis.md`。
  **不属本缺口的形状（实测排除，勿据此立论）**：① 顶层**块外** `Dim x` 是**字段**，字段有默认值、本就不参与 `BC42104`——普通上下文的同形状字段同样零警告；② 顶层**块内** `Dim` 是**局部变量**（读块外的 `y` 报 `BC30451`）；③ `BC42105`（函数并非所有路径都返回值）**不受影响**，顶层 `Function` 与嵌套类型方法都正常报；④ 不可达代码警告在本编译器里**整体不产生**（`ControlFlowPass.vb:89`/`:100`/`:110` 三处报点全被注释）。
- **不动 `meetings\` / `proposals\`**：本任务是缺陷修复，不引入能力变更；若某单元需要推翻既有 RESOLUTION，该单元**停手上报**，不自行改判。
- **重命名/搬移第一轮任务文件夹**：不改。

---

## 二、判定表（本任务的核心产出）

> 判定依据 = **同形状在普通编译上下文里的实测行为**。普通上下文一律 `vbc.exe` 编译普通程序（`Module Entry / Sub Main`）或内存编译；脚本上下文一律 `vbi.exe <file>.vbx` 直跑。三态按 `manifest.md`「断言状态」标注。

| # | 普通上下文里的同形状 | 普通上下文行为（证据） | 判定 | 依据 |
|---|---|---|---|---|
| **C1** | 普通类里 `Sub New()` / `Sub New(x As Integer)` | **合法**（`vbc` exit 0，`ctor-ordinary`） | **报错** | 提交类的构造器是**编译器不变量**，不是可用成员：`NamedTypeSymbol.GetScriptConstructor` = `DirectCast(InstanceConstructors.Single(), SynthesizedConstructorBase)`（`Symbols\NamedTypeSymbol.vb:697-700`，**实锤**：绕开合成构造器后该行抛 `InvalidCastException`）；`SynthesizedEntryPointSymbol` 对 `<Main>` 断言 `ctor.ParameterCount = 0`（`Symbols\Source\SynthesizedEntryPointSymbol.vb:258-259`）、对 `<Factory>` 断言 `= 1`（`:343-344`），且 `<Factory>` 体逐字是 `Dim submission As New Submission#N(submissionArray) : Return submission.<Initialize>()`（`:337-338`）⇒ **宿主构造提交类，用户构造器无落点**。判据落点见 `design-detailed.md` §U1 |
| **C2** | 普通类的实例方法里 `RaiseEvent` 自己的实例事件 | **合法且工作**（`raise-ordinary` exit 0，handler 计数 1） | **修好** | 断言的前提「事件字段的接收者是 `Me`」在脚本类里**不成立**：脚本类的**非限定**成员引用一律经 `TryBindInteractiveReceiver`（`Binder_Expressions.vb:2570-2576`）解析成 `BoundPreviousSubmissionReference`（`:2616-2618`），**同一次提交内也不例外** ⇒ 接收者是上一提交引用而非 `Me`，未被降级就进发射。断言之后的 `Else` 分支逻辑与接收者形状无关（只要求它是事件字段访问），修法是把接收者**降级**（`design-detailed.md` §U2） |
| **C3** | 普通类里 `Sub OnIt(...) Handles hooked.E`（实例与 `Shared` 各一） | **合法且工作**（`handles-ordinary` exit 0） | **修好** | `BindSingleHandlesClause` 的 `Select Case ContainingType.TypeKind`（`SourceMemberMethodSymbol.vb:773-784`）只列 `Class`/`Module`，`Submission` 落 `Throw UnexpectedValue`。提交类**是**类容器（`NamedTypeSymbol.IsSubmissionClass` = `TypeKind = TypeKind.Submission`，`Symbols\NamedTypeSymbol.vb:691-695`），Hookup 宿主取实例/共享构造器（`:790-807`）对提交类同样成立——**实锤**：把 `Submission` 并入该 case 后两形状均 exit 0 且 handler 真的被调用（探针 `handles-script-instance` / `handles-script-shared` 输出 `H`） |
| **C4** | `Module` 里显式 `MyBase` / `Structure` 里显式 `MyBase` / `Class` 里显式 `MyBase` | Module → **BC32001**（`mybase-ordinary-module` exit 1）；Structure → **BC30044**（`mybase-ordinary-structure` exit 1）；Class → **合法**（`mybase-ordinary-class` exit 0，输出 `B`） | **修好**（按已报诊断收口） | 脚本类**已经**报出 `BC36966`（`ERR_KeywordNotAllowedInScript`，`Errors.vb`）——缺的只是「报完不崩」。`BoundMyBaseReference` 的 `type` 字段断言非空（`Generated\BoundNodes.xml.Generated.vb:6016`），而提交类 `BaseTypeNoUseSiteDiagnostics` 为 `Nothing` ⇒ 错误路径取不到类型。`BindMeExpression` 的同类错误路径早有兜底写法（`Binder_Expressions.vb:2351` 的 `If(Me.ContainingType, ErrorTypeSymbol.UnknownResultType)`），本单元把同一写法补到 `BindMyBaseExpression` |
| **C5** | 普通方法里 `Finally` 块内 `GoTo` 跳到块外 | **报错 BC30101**（`jumpout-finally-ordinary` exit 1） | **报错** | `ERR_BranchOutOfFinally = 30101`（`Errors.vb:166`）唯一**报点**在流分析 `ControlFlowPass.VisitFinallyBlock`（`Analysis\FlowAnalysis\ControlFlowPass.vb:152-181`），而顶层语句的宿主 `<Initialize>` 方法体是空壳、初始化器**不做流分析**（`Compilation\MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?`）⇒ 判据从不执行，坏形状活到发射期。脚本模式下顶层 `Try`/`Catch`/`Using`/`SyncLock`/循环/`Select`/`With`/`If` 的跳出**都正常**（`tmp\probes\run_probes4.py` 的 `BLOCKS` 11 个键，脚本侧 `jumpout-*-script` 11 条探针逐条实测），只有 `Finally` 例外 |

**计数：修好 3 条（C2 / C3 / C4）；报错 2 条（C1 / C5）。**

---

## 三、资源失同步（U6，作者指令「遇到资源不同步就同步」）

| 项 | 现状（证据） | 同步动作 |
|---|---|---|
| `VBResources.resx` ↔ 13 份 `xlf` | 每份 xlf 各缺 **7** 条条目（自算集合差 `resx data@name` − `xlf trans-unit@id`，**实锤**）；`Directory.Build.props:4` 的 `ErrorOnOutOfDateXlf=false` 与 `:5` 的 `UpdateXlfOnBuild=false` 让任何门都不报 | 按原版 VB 编译器 xlf 规范补齐 13 份（新条目取 `<target state="new">`、target/source 与 resx `<value>` 逐字相同、位置与 resx 邻居一致） |
| `issues\README.md` 状态列 | issue 10 / 11 仍标 `Open`，而两者已在 `48d8edb` 修复并验证 | 改标 `Fixed`（commit 号按实际提交填，不预填） |
| `spec\spec-scripting-dialect.md` | 顶层 `GoTo` 的 Decision 仍写「本规范不保证」，而第一轮已让顶层标签参与初始化器序列 | **跑 spec 阶段**修订（`subagents/spec-writer.md`），不在本任务的实现单元里顺手改 |
| `upstream-merge.md` | 本任务改动面未登记 | 新增本任务章节 |

**收口时的落地情况**：四行全部完成——13 份 xlf 各补 7 条（U6a）；`issues\README.md` 的 issue 10 / 11 已改标 `Fixed`（并新增 12–20）；`spec\spec-scripting-dialect.md` 两处已修订（U6c）；`upstream-merge.md` 新增 §2.21。

---

## 四、分批与 Vortex 代办

**同文件改动强制串行。** 触碰面：

| 单元 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb` | `Symbols\Source\SourceMemberMethodSymbol.vb` | `Binding\Binder_Expressions.vb` | `Binding\Binder_Initializers.vb` | `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb` | `Errors.vb` + `ErrorFacts.vb` + resx + 13 xlf |
|---|---|---|---|---|---|---|
| U1 | ● | | | ●（或 U1 专用落点） | | ●（新码） |
| U2 | | | | | ● | |
| U3 | | ● | | | | |
| U4 | | | ● | | | |
| U5 | | | | ● | | |
| U6 | | | | | | ● |
| U7 | | | | | | |

| 批次 | 单元 | 说明 |
|---|---|---|
| W1 | U4 → U3 → U2 | 三条「一行级收窄 + 降级」的独立面，先清掉断言族 |
| W2 | U1 | 需要一个新 ERRID ⇒ 单独一批，注册链（`Errors.vb` / `ErrorFacts.IsBuildOnlyDiagnostic` / resx / xlf）完整走一遍 |
| W3 | U5 | 唯一涉及新判据执行路径的一条，改动面最深 |
| W4 | U6 → U7 | 资源同步与测试补强 |
| W5 | 全量收口 gate | 七门 + Scripting 程序集 + 变更面核对 |

**验收条件（每个单元共同遵守）**：
1. 探针形状在 `vbi.exe` 下不再出现 `2148734499` / `2148734266` 退出码（**修好**类单元另加：形状成功运行且可观测结果正确）。
2. 普通上下文对照组行为**不变**。
3. 单元对应的无副作用单元测试落在 `Scripting\VisualBasicTest\`（REPL/宿主面）或 `Compilers\VisualBasic{Emit,Semantic,Symbol}Test\`（编译器面），且**判别性用例**必须能让「改动被撤销」时失败。
4. 注释与 XML 文档里的 `文件:行号` 锚点回读被引用的那一行文本后再写。

---

## 五、探针清单（无副作用：只写 `tmp\probes\`，不入仓）

| 驱动 | 覆盖 |
|---|---|
| `tmp\probes\run_probes.py` | 第一轮 65 形状总览（脚本模式） |
| `tmp\probes\run_probes2.py` | 形状 + 普通上下文对照（第一版，含命名冲突缺陷） |
| `tmp\probes\run_probes3.py` | **命名冲突修正后的成对探针**（脚本 `vbi.exe` / 普通 `vbc.exe`），本任务 A/B 证据的来源 |
| `tmp\probes\run_probes4.py` | 块边界跳转全矩阵 + 流分析可达性 |
| `tmp\probes\run_probes5.py` | 与 C5 同 CFG 报点的其余形状（`Yield` / `Exit Try` / `On Error` / 嵌套 `Finally`） |
| `tmp\probes\patch_feasibility.py` | 五族的**可行性补丁**（`--revert` 复位）；本计划 §二 的「实锤」结论由它产生 |

> 探针只用于**取证**，不属交付面；`tmp\` 已在 `.gitignore` 内。

---

## 六、共享源码事实（本任务新增锚点，均已回读原文）

| 事实 | 锚点 |
|---|---|
| 提交类构造器是唯一且合成的 | `Symbols\NamedTypeSymbol.vb:697-700`（`InstanceConstructors.Single()`） |
| `<Main>` / `<Factory>` 对构造器形参数的断言 | `Symbols\Source\SynthesizedEntryPointSymbol.vb:258-259` / `:343-344` |
| `<Factory>` 用提交数组构造提交类 | `Symbols\Source\SynthesizedEntryPointSymbol.vb:337-338` |
| 提交类实例分支无条件合成构造器 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb:2744-2762` |
| 普通类分支会复用用户已声明的构造器 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb:2796-2816`（`EnsureCtor` 的前置检查） |
| 脚本类非限定成员引用取上一提交引用 | `Binding\Binder_Expressions.vb:2570-2576` / `:2616-2618` |
| 上一提交引用的降级落点 | `Lowering\LocalRewriter\LocalRewriter_PreviousSubmissionReference.vb:13-23` |
| 事件 raise 的两条降级路径与接收者前提 | `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:22-100` |
| `Handles` 宿主类型的 `Select Case` | `Symbols\Source\SourceMemberMethodSymbol.vb:773-784` |
| Hookup 宿主构造器的选择 | `Symbols\Source\SourceMemberMethodSymbol.vb:790-807` |
| `MyBase` 错误路径 | `Binding\Binder_Expressions.vb:2369-2379` |
| `Me` 错误路径的兜底写法 | `Binding\Binder_Expressions.vb:2346-2355` |
| 顶层语句的判据补齐模式（第一轮 F10 的写法） | `Binding\Binder_Initializers.vb:134-140`（调用点）/ `:242-252`（`CheckAwaitInTryHandler`） |
| 初始化器不做流分析 | `Compilation\MethodCompiler.vb:625` |
| `BC30101` 的唯一报点 | `Analysis\FlowAnalysis\ControlFlowPass.vb:152-181` |

---

## 七、与其他文档的接口

- **issue 正文**：本任务五族各建一份 `issues\issue-*.md`（`design-detailed.md` §账本与规范义务 列出必写小节）。
- **upstream-merge**：变更面登记由 W4 执行。
- **spec**：`spec-scripting-dialect.md` 的两处修订只由 spec 阶段执行（§三）。
- **第一轮任务文件夹**：不改（其「非范围」第 3 条指向本任务的 U2，两处口径以本文件为准）。
