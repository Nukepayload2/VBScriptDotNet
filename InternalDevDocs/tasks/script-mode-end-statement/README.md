# 任务：脚本模式 `End` 语句解禁（script-mode-end-statement）——任务计划

> **状态：已收口（F01–F05 全做完；产品码净增删 +10/−1；新增 14 格＝Syntax 8 / Semantic 5 / Emit 1；宿主两格负向钉改写为正格。七门全绿、`Scripting\VisualBasicTest` 直跑 777/0、档 2 宿主六格对账。待作者提交 ⇒ commit 号不预填。）** 缺陷登记：`..\..\issues\issue-script-mode-end-statement-rejected.md`（issue 39，**人的登记，非 agent 输入**）——已转 **Fixed**。流水账：`tmp\vortex-logs\script-mode-end-statement\`（`01-main-plan` / `02-implementer-f02-f03` / `03-verifier` / `04-main-f04-f05`）。上游账本：`..\..\upstream-merge.md` **§2.25(l)**（两个改动文件都是上游同名文件）。
>
> **子 agent 禁读 `InternalDevDocs\issues\`、禁改 `InternalDevDocs\tasks\`**：本文件已把症状、读数与判据复述齐全；要改计划报 main，由 main 落笔。

- **一句话**：`.vbx` 脚本与 REPL 提交里的 `End` 语句被**两道互相独立的门**拒绝——顶层**裸** `End` 走解析期（`BC30678` `ERR_UnrecognizedEnd`），**块内／方法体内／lambda 体内**的 `End` 走绑定期（`BC30615` `ERR_EndDisallowedInDllProjects`）——两条都到不了运行期（`EXITCODE=1`，且 `End` **之前的语句也不执行**）。预期：`End` 合法，语义＝**立即以退出码 `0` 结束进程**，与脚本内 `System.Environment.Exit(0)` 的现有行为同形。
- **来历**：`End` 是标准 VB 语句（`Compilers\VisualBasic\Portable\Lowering\LocalRewriter\LocalRewriter.vb:865-879` 把 `BoundEndStatement` 下降为 `Microsoft.VisualBasic.CompilerServices.ProjectData.EndApp()` 调用），普通编译里一直可用；脚本容器把它连带挡掉了，属**脚本容器的既有判据失效**，与 issue 28/29/08 同族。

## 一、判据（已运行读数；本任务事实基础的全部）

宿主＝本机 `dotnet tool install` 的 `vbi`；探针源码留档 `tmp\probe-end-stmt\`（`a-end-top.vbx` / `d-end-in-if.vbx` / `c2-end-sub.vbx` / `e-end-in-lambda.vbx` / `x-exit.vbx` / `normal.vb` / `sdkctl\`）。

| 形状（`.vbx` 或 REPL 逐条提交） | 现状读数（已运行） |
|---|---|
| 顶层**裸** `End`（后只跟行终止符／`:`） | `BC30678` `'"End" 语句无效。`，`EXITCODE=1` |
| 顶层 `If`／`For`／`Using`… **块内** `End` | `BC30615` `'在类库项目中不能使用 "End" 语句。`，`EXITCODE=1` |
| **方法体内** / **lambda 体内** `End` | 同上 `BC30615` |
| **判别性对照**：同一份源码只换 `OutputKind`（编译模式 `vbi normal.vb /target:exe`） | `EXITCODE=0`、**零诊断** ⇒ 门是 `OutputKind`，**不是 `End` 本身** |
| 同对照的另一半：`/target:library` | `EXITCODE=1`、`BC30615` ⇒ 该码的**正当用途**，不许删除 |
| 标准 VB 对照（SDK 自带编译器，`OutputType=Exe`，`tmp\probe-end-stmt\sdkctl\`） | 打印 `before`、`RUN_EXITCODE=0`、**无 `after`** ⇒ 语义＝立即结束进程、退出码 0 |
| 脚本内 `System.Environment.Exit(0)`（`x-exit.vbx`） | 打印 `X1`、`EXITCODE=0`、**无 `X2`** ⇒ `End` 生效后应当同形 |

**判据来源**：作者裁定（issue 39 正文「预期行为」）＋ 上表两侧实测。`End` 是 **VB 专有语句**，C# 脚本方言无对应物（`csi` 侧没有可与 `End` 对齐的语法）⇒ D7「可移植性三问」第 ③ 问为**否**，本任务**不**由 C# 实现在树内自动裁决，判据以作者裁定 + 标准 VB 实测为准（D7 例外 (a)/(c) 都不适用：不需要新造语言特性，也不需要两个等价形态之间选落点）。

## 二、根因与两道门（各修一处，缺一不可）

### 门 1：顶层裸 `End` 被当成「块结束语句」（解析期）

- `Compilers\VisualBasic\Portable\Parser\Parser.vb:829-830`，位于 **`ParseDeclarationStatementInternal`**（`:663-875`）的 `EndKeyword` 臂：

  ```vb
  Case SyntaxKind.EndKeyword
      Return ParseGroupEndStatement()
  ```

  无条件按块结束语句解析。`End` 后跟行终止符时 `ParseGroupEndStatement`（`:1756-1778`）算出 `endKind = GetEndStatementKindFromKeyword(nextToken.Kind) = SyntaxKind.None`，于是 `:1769`

  ```vb
  statement = ReportSyntaxError(ParseStopOrEndStatement(), ERRID.ERR_UnrecognizedEnd)
  ```

  ——节点其实**已经**被解析成 `StopOrEndStatementSyntax`，错误是**多挂上去的**。
- **方法体语境的分发器是对的**：`ParseStatementInMethodBodyInternal`（`:929-939`）→ `ParseStatementInMethodBodyCore`（`:941`）的 `EndKeyword` 臂 `:1005-1006` 调 `ParseEndStatement()`（`:1719-1749`），后者用 `CanFollowStatementButIsNotSelectFollowingExpression`（`Parser\ParseScan.vb:84-89`）判别「`End` 后面跟的是语句终止符」⇒ 走 `ParseStopOrEndStatement()`，零诊断。
- ⇒ 缺口＝**顶层脚本没有复用方法体那条判别**。同一 switch 里紧邻的臂早有 `IsTopLevelScript` 守卫：`IdentifierToken` 臂 `:819`、`AddHandler` 臂 `:838`、`RemoveHandler` 臂 `:844`；`EndKeyword` 臂独缺。守卫谓词＝`IsTopLevelScript`（`:85-93`，`Context.AcceptsScriptTopLevelStatement`）。
- **边界（不得越界）**：真正的块结束语句（`End If`/`End Sub`/`End Class`…）**在脚本顶层仍须报既有诊断**——`ParseEndStatement` 内部按下一 token 天然分流到 `ParseGroupEndStatement`（`:1748`），块结束语句的诊断来自 `Parser\DeclarationContext.vb` 的 `RecoverFromMismatchedEnd`（声明 `:359`，`Select Case (stmtKind)` 表 `:367-456`：`EndIfStatement` → `ERR_EndIfNoMatchingIf` `:370`、`EndSubStatement` → `ERR_InvalidEndSub` `:388` …），**该表不动**（`:450-451` 的 `EmptyStatement`/`IncompleteMember` 支只是该表的兜底一格）。

### 门 2：块内／方法体内的 `End` 撞「DLL 项目」门（绑定期）

- `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb:5434-5440`：

  ```vb
  Private Function BindEndStatement(endStatementSyntax As StopOrEndStatementSyntax, diagnostics As BindingDiagnosticBag) As BoundStatement
      If Not Compilation.Options.OutputKind.IsApplication() Then
          ReportDiagnostic(diagnostics, endStatementSyntax, ERRID.ERR_EndDisallowedInDllProjects)
      End If
      Return New BoundEndStatement(endStatementSyntax)
  End Function
  ```

- 脚本提交**写死**了 `OutputKind.DynamicallyLinkedLibrary`：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:237-262` 的 `CreateSubmission` 用 `CreateScriptCompilation`（`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:345-389`），实参 `outputKind:=DynamicallyLinkedLibrary` 在 `:242` ⇒ `IsApplication()` 恒假 ⇒ **`End` 一旦进到绑定层就必报 BC30615**。两个宿主（`vbi` 与商店版）都走同一条 `CreateSubmission`。
- **禁止**无条件删除该门——普通 `OutputKind.DynamicallyLinkedLibrary` 的**类库项目**仍须报 BC30615，那是该诊断的正当用途，且有既有用例钉住（见 §五）。
- **落点（main 裁，D7「(c) 的处置」留痕）**：判据放宽为

  ```vb
  If Not (Compilation.IsSubmission OrElse Compilation.Options.OutputKind.IsApplication()) Then
  ```

  - **选它**：`IsSubmission` 在 VB 编译器树内**只有一个写点**（`VisualBasicCompilation.vb:388`，位于 `CreateScriptCompilation` 内）⇒ 词法上就是「脚本提交」本身，D10 的开关条件可结构可证，且 `.vbx`（`CreateSubmission`）与 REPL 提交走的是同一条路。
  - **否掉 `ContainingType.IsScriptClass`**（另一个候选）：它＝`DeclarationKind.Script OrElse DeclarationKind.Submission`（`Symbols\Source\SourceMemberContainerTypeSymbol.vb:1295-1300`，基类 `Symbols\NamedTypeSymbol.vb:682-686` 恒 `False`），会把**非提交**的 `SourceCodeKind.Script` 编译一起放宽；而本仓正好记着「测试用 `DeclarationKind.Script`、产品出货走 `Submission`，两条不等价」的教训（`decisions.md` D5 实例 4）⇒ 判据只取产品真正走的那条。
  - **明示接受的代价**：REPL 提交里**普通类成员体内**的 `End` 也随之合法（提交整体不是类库）。与「脚本/REPL 里的 `End` 合法」这一裁定同向，不静默。

## 三、非脚本面免疫论证（D10 硬约束；F02 的 Storage 字段必须复述并自证）

两个改动文件都在「脚本／提交特有机制与普通 VB 编译共用」的代码里，故必须写出开关条件，并论证普通 VB 输入走的分支**逐字不变**。

- **门 1 的开关＝`Parser.IsTopLevelScript`**（`Parser.vb:85-93` ＝ `Context.AcceptsScriptTopLevelStatement`）。定义在 `Parser\BlockContexts\BlockContext.vb:207-211`：

  ```vb
  Friend Overridable ReadOnly Property AcceptsScriptTopLevelStatement As Boolean
      Get
          Return Parser.IsScript AndAlso BlockKind = SyntaxKind.CompilationUnit
      End Get
  End Property
  ```

  唯一 override 是 `PropertyBlockContext.vb:45-49`（自动实现属性代持块的透传），**唯一调用点**是 `Parser.vb:91`。`IsScript`（`:79-83`）＝ `_scanner.Options.Kind = SourceCodeKind.Script` ⇒ 普通编译（`SourceCodeKind.Regular`）下该谓词**恒 `False`**，`EndKeyword` 臂结构不可达新分支，走 `ParseGroupEndStatement()` 逐字不变；脚本里**块内**的 `End` 也不经过本臂（那是方法体分发器）。既有 5 处普通编译的 BC30678 解析钉（§五）因此不受影响。
- **门 2 的开关＝`Compilation.IsSubmission`**。全 `Compilers\VisualBasic\Portable\**` 内 `isSubmission:=True` 只有一处（`VisualBasicCompilation.vb:388`，`CreateScriptCompilation` 内）；普通编译走 `:339` 的 `isSubmission:=False`。⇒ `IsSubmission ≡ False` 时新判据**退化为原文** `Not OutputKind.IsApplication()`。
- **验证义务**：反例锁至少覆盖 —— ① 普通 `Class`/`Module` 方法体内的 `End` 仍报 `BC30615`（`/target:library` 正当用途）；② 普通编译顶层裸 `End` 的诊断不变；③ 脚本顶层 `End If`/`End Sub` 类块结束语句诊断不变；④ 脚本内 `Environment.Exit(0)` 行为不变（§一表）。
- **3-way 注意点**：`Parser.vb` 与 `Binder_Statements.vb` 都是上游同名文件 ⇒ 收口时在 `upstream-merge.md` 记「合并上游时必须保住的两个脚本分支」。

## 四、范围内

| # | 单元 | 说明 |
|---|---|---|
| F01 | 取证（只读 + 探针，**不改产品码、不构建**） | ① 全量普查既有钉桩与 prose 钉（grep `BC30678`／`ERR_UnrecognizedEnd`／`BC30615`／`ERR_EndDisallowedInDllProjects`，排除生成物），产出行清单；② 确认脚本顶层**块内** `End` 的分发归属与顶层裸 `End` 的差异（哪条臂、哪个函数）；③ 判 `End` 的**发射层可观测面**（`AssertEmits` 形态与断 IL 的既有用法），定出进程级行为在无副作用纪律下能断到什么程度；④ 确认 `End` 在 `#Load` 进来的树、lambda、`Try` 块内的归属 |
| F02 | 实现（产品码） | `Parser.vb` `EndKeyword` 臂加 `IsTopLevelScript` 守卫（与 `:838` 同形）；`Binder_Statements.vb:5435` 判据放宽为 `Compilation.IsSubmission OrElse OutputKind.IsApplication()`。净增删最小，不留第二处相似特例 |
| F03 | 单元测试 | 见 `test-plan.md`：ES-A 正格（脚本/REPL）、ES-B 反例锁（非脚本面免疫）、ES-C 发射层、ES-D 回收 |
| F04 | 回归（**由 main 跑**） | 七门 gate（`scripts\verify-vb-compiler-tests.ps1`，**同步脚本内基线**）+ `Scripting\VisualBasicTest` 直跑 `-automated` + 重建 Debug 宿主后跑 `tmp\probe-end-stmt\` 六格档 2 对账 |
| F05 | 账本（main 落笔） | issue 39 转 Fixed（commit **不预填**）；`..\..\issues\README.md` 第 39 行状态同步；`upstream-merge.md` 登记两个上游同名文件；`spec\spec-scripting-dialect.md` 新增 `End` 一节（**先英文正本、后 `zh-CN` 译文**）；`..\script-mode-coverage-parity\test-plan.md` 的 `End` 行同步（§五 第 3 条） |

### 非范围

- 不新增诊断码、不改 `VBResources.resx`／xlf 文案；不动 `Stop` 语句（顶层已可发射，`StopOrEndStatementSyntax` 的另一半）。
- 不改 `End` 的**下降语义**（`LocalRewriter.VisitEndStatement` 原样）；不改 `OutputKind` 的任何默认值；不动脚本宿主里 `CreateSubmission` 的 `outputKind:=DynamicallyLinkedLibrary`。
- 不动 `DeclarationContext.vb:451` 的块结束诊断臂；不动普通类库项目的 BC30615（含既有钉桩）。
- 不建提交、不 `git add`（暂存区归作者）。

## 五、须回收 / 须补的既有钉桩

1. **负向钉（必须改写为正格）**：`Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb`
   - `:388-397` `TopLevelEndStatement_IsReported`（钉 `BC30678`）
   - `:399-407` `EndInsideMethod_IsReported`（钉 `BC30615`）
   - `:387-391` 的 XML doc prose（「End cannot be produced by a script at all…」）随改判重写。
   - 同 region 的 `:409-419` `TopLevelStopStatement_EmitsWithoutRunning` **不受影响**，作为「同 region 的邻居不被误改」的哨兵。
2. **保持改判不动的正当钉子（反例锁，不许改）**：`BindingErrorTests.vb`（约 `:9680`）与 `Binder_Statements_Tests.vb`（约 `:3620`／`:3766`）钉的是**普通编译**的 BC30615 ⇒ 这正是不许删门的证据面；F01 负责核实它们的编译形状（`OutputKind`／是否 submission），并在 `test-plan.md` 里点名。
3. **覆盖率台账**：`..\script-mode-coverage-parity\test-plan.md` 的 `End` 语句行（约 `:583`，两栏现按「负向，裁定 5 算覆盖」登记）随用例改写同步更新；同文件 `:675`（R9 行）、`:860`（R9 段）、`:1094`（裁定段）里的「两栏都有负向用例」表述同步改。`:584` 的 `Stop` 行不受影响。**该行的源码锚已腐**（F01 实测）：行内写的 `Parser.vb:996`（声称 `EndKeyword`）今天是 `ForKeyword`（`:994-995`），`Parser.vb:1710` 也不是 `ParseEndStatement`——正确锚是**声明位 `:829-830`／方法体位 `:1005-1006`／`ParseEndStatement :1719`**，更新台账时一并改正。
4. **须补**：脚本顶层 `End` 的正向格（零诊断 + 可发射 + `End` 之后代码的不可达性）；块结束语句顶层诊断不变的正向锁（`End If` 或 `End Sub`）。

## 六、新增行为变化（交付时必须登记）

**有行为变化，必须登记**（本仓规矩：今天静默、修复后改产物形状的，写进 issue 正文与 spec）：

1. 脚本/REPL 里 `End` 从「编译期拒绝」变为「合法且立即结束进程（退出码 `0`）」，`End` 之前的输出已落盘、之后的语句不执行。
2. `End` 由**不产生可运行产物**变为**产生可运行产物**（脚本会在 `End` 处提前结束而不是跑完全文）。
3. **不产生新的诊断码**：两道门是**撤掉**误报，不是换码。

**无副作用纪律下的取证边界**（必须明写）：单元测试进程不能真的跑 `End`（`EndApp()` 会结束测试进程），故进程级行为（退出码 0、`End` 后不输出）由 **F04 的档 2 宿主实测**取证，档 1 只能断到「零诊断 + 发射成功 + IL 里是 `ProjectData.EndApp` 调用」；写不了就测不了的部分**不静默跳过**，在 `test-plan.md` §五 与 F04 读数里各记一次。

## 七、Resonance facet（新代码要贴合的近邻样本，逐条有当前证据）

- **必选·解析层**：守卫写法逐字照 `Parser.vb:838-840`（`If IsTopLevelScript Then` / `Return ParseStatementInMethodBodyInternal()`），不加新谓词、不改 `IsTopLevelScript`。
- **必选·绑定层**：判据仍留在 `BindEndStatement` 内、仍用 `ReportDiagnostic(diagnostics, endStatementSyntax, ERRID.ERR_EndDisallowedInDllProjects)` 原码原位置；只改条件表达式，不动 `BindingDiagnosticBag` 通路。
- **必选·测试**：脚本格用 `ScriptModeConformance`（`Scripting\VisualBasicTest\ScriptModeConformanceTests.vb:35-114`）的既有断言臂，不新造夹具；非脚本格照既有 `AssertTheseDiagnostics` 形态。
- **优先·命名**：不加新谓词；若要引用提交性，用既有 `Compilation.IsSubmission`，不发明近义名。
- **避免**：不留两份「是不是脚本」的判据；不为让两格变绿而放宽断言；不把 `BC30615` 的类库用途一起删掉。
- **反复摩擦（来自本仓流水账，安全摘要）**：①行号锚在跨文档引用里易腐，改前重新核对；②诊断正文按机内 ANSI 码页输出 ⇒ CP936 解码，Git Bash 需 `MSYS_NO_PATHCONV=1`；③子 agent 跑全量回归曾烧掉大量 token 且没留日志 ⇒ 只跑定向 `--filter`，长命令重定向到文件读尾巴，全量归 main。

## 八、风险与停止上报

- **构建面独占**：并发构建会 `BC2012` 锁 dll ⇒ 同一时间只允许一个 agent 动构建面；agent 只跑定向过滤，七门全量与 `-automated` 直跑归 main。
- **两道门只开一道**会造成「顶层裸 `End` 无诊断但块内仍 BC30615」的**半修状态** ⇒ F02 必须一次改两处，F03 的两组正格都要有（顶层裸 + 方法体内）。
- **误判风险**：把 `BC30615` 当「该删的误报」而顺手删门 ⇒ 普通类库用例立刻变红；`test-plan.md` ES-B 就是防这一条的。
- **停手上报条件**：① 若实测发现「标准 VB 的 `End` 在**类库项目**里其实合法」（与 §一 对照读数矛盾），停手，因为那说明门的正当用途本身错了；② 若发射层发现 `ProjectData.EndApp()` 在提交里解析不到（`WellKnownMember` 为 `Nothing`，`LocalRewriter.vb:871` 的分支），停手上报，那意味着需要的是**引用面**改动而不是本任务的判据改动。除这两条外按 D7「(c) 的处置」自行裁并留痕，不回给人。
