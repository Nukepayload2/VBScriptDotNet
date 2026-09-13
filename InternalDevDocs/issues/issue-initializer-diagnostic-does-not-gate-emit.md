# [BUG] 初始化器里的「`Await` 不在 async 上下文」诊断报了但不阻止发射 → 发射期断言终止（与 `Shared` 无关）

**状态**：**Fixed**（已验证，commit 待作者提交后补；2026-09-12 登记，2026-09-13 修复验证通过——见下「修复落地」节）
**证据等级**：**已运行**（Debug `2.0.0-Beta+5816a5c`，本机实测，含「有诊断 / 无诊断」两组对照）
**严重度**：高（用户可达、Debug 终止进程；诊断**被算出**却不生效，`/check` 之类的「只看诊断」路线会看到错误、直接执行却崩）
**影响面**：**嵌套类型**（普通类，非脚本类）里字段/属性初始化器中的 `Await`——即任何 `TypeKind` 下走「初始化器 → 非 async 方法」这条路的绑定诊断；`Shared` 是否参与**无关**（本 issue 的触发形状一个 `Shared` 都不需要）

## 症状

提交（`.vbx` / REPL）里定义一个**嵌套类**，其字段或属性的初始化器含 `Await`：

```vb
' probe v1
Imports System.Threading.Tasks
Class C
    Dim s As Integer = Await Task.FromResult(9)
End Class
Console.WriteLine("NESTED-INSTANCE-FIELD " & (New C).s)
```

**运行结果是 `Process terminated` + 断言（`EXITCODE=35`），并且一个诊断都不打印**：

```text
Process terminated.
Assertion failed.
Unexpected value 'AwaitOperator' of type 'Microsoft.CodeAnalysis.VisualBasic.BoundKind'
   at …CodeGen.CodeGenerator.EmitExpressionCore(…) …EmitExpression.vb:line 209
   at …CodeGenerator.EmitDirectCastExpression(…) …EmitConversion.vb:line 322
   at …CodeGenerator.EmitAssignmentExpression(…) …EmitExpression.vb:line 1819
```

## 根因（源码核实）：绑定期诊断写进全局袋，而发射门只看逐方法袋

1. **诊断算得出、也确实报出。** 嵌套类不是脚本类，`IsInAsyncContext()`（`Binding\Binder_Expressions.vb:4720-4728`）对它的字段返回 False ⇒ `BindAwait` 走 `:4742-4743` 报 **BC36937**（`ERR_BadAwaitNotInAsyncMethodOrLambda = 36937`，`Errors\Errors.vb:1566`）。
2. **但初始化器的绑定诊断写进的是全局 `_diagnostics`，不是逐方法袋。** `Compilation\MethodCompiler.vb:599-607` 把 `_diagnostics` 传给 `Binder.BindFieldAndPropertyInitializers`（静态桶 `:599-602`、实例桶 `:604-607`）——初始化器的绑定诊断因此落进 `_diagnostics`。
3. **逐方法的发射门组成的袋是一个空袋。** `MethodCompiler.vb:1256-1257`「In order to avoid generating code for methods with errors, we create a diagnostic bag just for this method」→ `BindingDiagnosticBag.GetInstance(_diagnostics)`；而 `Binding\BindingDiagnosticBag.vb:50-52` 的 `GetInstance(template)` **只复制两个布尔**（`AccumulatesDiagnostics` / `AccumulatesDependencies`），**不复制已有诊断**。
4. **发射门的三项判据都不含那条诊断**：`MethodCompiler.vb:1272` 的 `hasErrors = _hasDeclarationErrors OrElse diagsForCurrentMethod.HasAnyErrors() OrElse processedInitializers.HasAnyErrors OrElse block.HasErrors`——
   - `diagsForCurrentMethod` 是第 3 步那个空袋；
   - `processedInitializers.HasAnyErrors` 来自 **bound 节点的错误标志**（`Binder_Initializers.vb:52`），而 `Binder_Expressions.vb:4742-4744` **只 `ReportDiagnostic`、不标错**（`BoundAwaitOperator` 的 `hasErrors` 只来自操作数与 awaiter）；
   - `block.HasErrors` 与该初始化器无关。
5. 于是 `:1315` 的 `If DoLoweringPhase AndAlso Not hasErrors Then` 照常进入 `LowerAndEmitMethod`：`Rewriter.LowerBodyOrInitializer`（`:1515`）里 `LocalRewriter.VisitAwaitOperator` **原样保留**节点（`Lowering\LocalRewriter\LocalRewriter.vb:799-830`，注释「Await operator expression will be rewritten in AsyncRewriter」），而 `AsyncRewriter.GetAsyncMethodKind` 对非 async 的宿主方法直接返回 `None`（`Lowering\AsyncRewriter\AsyncRewriter.vb:364-368`）⇒ 节点活到 `EmitExpression.vb:206-209` 的 `Case Else` ⇒ 断言终止。

**判别性实证（同一形状，三种结局）**：

| 探针 | 形状 | 结果 |
|---|---|---|
| v1 | 嵌套类**实例**字段 `= Await Task.FromResult(9)` | ❌ exit 35，**零诊断** |
| v2 | 嵌套类**共享**字段 同上 | ❌ exit 35，**零诊断** |
| v3 | 嵌套类**实例属性** 同上 | ❌ exit 35，**零诊断** |
| v4 | 嵌套类共享字段 `= Await 5`（**不可 await**） | ✅ exit 1，**BC36937 + BC36930 都打印** |
| v5 | 嵌套类共享字段 `= Await Task.FromResult(9)` **外加一个重复成员声明** | ✅ exit 1，**BC30260 + BC36937 都打印** |

⇒ v4/v5 证明**这条诊断存在于编译结果里**（v5 尤其干净：同一次编译既打印 BC36937 又打印 BC30260）；v1–v3 证明**只有它自己时报不出来、且拦不住发射**。v4 之所以不崩，是 `Await 5` 让操作数带错（BC36930）⇒ bound 节点带错 ⇒ `processedInitializers.HasAnyErrors` 为真 ⇒ 第 4 步的门生效。**这把根因锁死在「诊断落在哪个袋 + 是否标错」上。**

**证据等级：已检查（源码逐行）+ 已运行（5 个探针）。**

## 预期行为

编译应当**停下来报 BC36937**（`error`，不产生可执行产物）：`vbi script.vbx` 应像 v4/v5 一样打印诊断并退出，而不是终止进程。

## 修复落地（**已落地并通过验证**，2026-09-13）

**取候选 B**（让发射门看得见初始化器诊断），落地在两个文件、共两处：

| # | 落点 | 实际改动 |
|---|---|---|
| ① | `VB\Binding\Binder_Initializers.vb:51-55` | `ProcessedFieldOrPropertyInitializers` 的构造函数增加 `Optional bindingReportedErrors As Boolean = False` 入参（`:45-49` 的 `Empty` 单例语义不变），`HasAnyErrors = bindingReportedErrors OrElse boundInitializers.Any(Function(i) i.HasErrors)`；`:25-29` 的文档注释同步改写为「该标志由拥有本路诊断的调用方传入」 |
| ② | `VB\Compilation\MethodCompiler.vb:599-623` | 静态桶与实例桶**各自**取 `BindingDiagnosticBag.GetInstance(_diagnostics)` 作为本桶独立袋，传给 `BindFieldAndPropertyInitializers`，绑完立即 `_diagnostics.AddRangeAndFree(袋)` 合并回共享袋，并把该袋的 `HasAnyErrors()` 作为 ① 的入参。两桶**各自独立判定**，顺序（静态在前）与诊断相对顺序不变 |

**为什么是 B 而不是 A/C**：A（给 bound 节点标错）只覆盖 `Await` 一条诊断，本 issue 自陈的「派生问题（还有哪些初始化器绑定期诊断落进同一个洞）未清点」会留成遗留问题；C（`GetInstance(template)` 复制已有诊断）有 100+ 调用点、语义影响面未清点，风险最大。B 复用逐方法发射门已有的 `processedInitializers.HasAnyErrors` **一项**，不新增门判据、也不动 `_hasDeclarationErrors`。**注意不要改用「在共享袋上取前后 error 增量」**：`_diagnostics` 是编译级共享袋且 `ConcurrentBuild` 默认开，别的成员先报错会让增量口径吞掉本路新产生的错（欠 gate）。

**验证证据**（**已运行**）：

- 新增用例 **19 条 L2**（`Compilers\VisualBasicEmitTest\Emit\InitializerDiagnosticGatingTests.vb`，`<Fact>` 计数 19）+ **4 条宿主用例**（`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`：嵌套类 `Await` 初始化器改为报诊断不终止、顶层语句错误改为报诊断不在 codegen 崩、REPL 两种形状报错后会话继续）。
- **宿主全量 323 通过 / 0 失败**。
- **七门 gate 全绿**（`scripts\verify-vb-compiler-tests.ps1`）。
- **「修复前必失败」已独立复现**：把上述两处改动临时还原后跑新增用例，负载形状重新回到原症状（断言终止 / 崩），随后恢复改动再跑转绿 ⇒ 用例确实锁住本缺陷，而不是同义反复。
- 不变量核对：诊断集合不变（v1–v3 的 BC36937 照旧报出，本修法只改「是否发射」）；warnings 不 gate（`HasAnyErrors()` 只算 `error` 严重度）；无初始化器 error 的编译零行为变化（全量回归兜底）。

**commit 待作者提交后补**（本条登记时尚未提交，故不写 commit 号）。

## 修复方向（候选；**已裁：B**，落地情况见上节）

- **A. 让报错同时把 bound 节点标错**：`Binder_Expressions.vb:4742-4744` 在 `ReportDiagnostic` 之外让 `BoundAwaitOperator` 带 `hasErrors:=True`（或在 `BindAwait` 返回前标记）。改动点最小，且与既有「有错的初始化器不发射」路径（`Binder_Initializers.vb:52` → `processedInitializers.HasAnyErrors`）自然衔接。**未取**：只覆盖 `Await` 一条诊断，末条「派生问题」会留成遗留问题。
- **B. 让发射门看得见初始化器诊断**：`MethodCompiler.vb:599-607` 绑定初始化器后，把「这次绑定给 `_diagnostics` 新增了 error」传播到逐方法的 `hasErrors`（例如记录绑定前后的 error 计数差，或把初始化器诊断也写进该方法的袋）。改动面小，但要注意 `_diagnostics` 是跨方法共享的（顺序敏感）。**已取**——实际落地的形态是「两桶各自独立袋 + `HasAnyErrors()` 入参」，**不采用**「共享袋前后计数差」口径（并发编译下会欠 gate）；理由是 B 复用发射门已有的一项判据，一处改动覆盖全部同类情形（详见上节）。
- **C. 让 `GetInstance(template)` 复制已有诊断**（`Binding\BindingDiagnosticBag.vb:50-52`）：一处改动覆盖全部同类情形，但 `GetInstance` 被 100+ 处调用、语义影响面**未清点**（**推测**风险最大），不建议作第一选择。**未取。**
- **派生问题（推测，未穷举）**：只要某条绑定期诊断是经 `BindFieldAndPropertyInitializers` 直写全局袋的，就可能有同样表现。本 issue 只锁 `Await` 这一条；其余同类（若有）须另行清点。**修复后**：B 的口径是「本桶绑定期是否报过 error」这一**类别判据**，不逐条枚举诊断 ⇒ 同类诊断一并被覆盖；但仍**未做**「还有哪些初始化器绑定期诊断」的正面清点（该项仍开放，属**推测**面）。

## 与在办提案的关系

- `InternalDevDocs\proposals\proposal-submission-shared-members.md`——本 issue 由该提案的专项调查发现（该提案的 issue 06 只覆盖「顶层 `Shared` 字段」，调查实测发现**嵌套类型**里字段、属性、共享字段三种形状同样崩，且**与 `Shared` 无关**）。
- `InternalDevDocs\issues\issue-script-shared-field-await-initializer-crash.md`（issue 06）——**登记面偏窄**：只写了「脚本顶层 `Shared` 字段」。正确的划分是：
  - 顶层 `Shared` 字段 ⇒ 「`IsInAsyncContext` 漏查 `IsShared`」**无诊断**（issue 06 的根因，**仍 Open**）**且**「诊断不 gate 发射」**也不 gate 发射**（本 issue 的根因，两缺陷叠加）；
  - 嵌套类型 ⇒ 诊断**报得出**（BC36937）但**不 gate 发射** ⇒ **本 issue**（**「不 gate 发射」这一半已修**，`BC36937` 现在会拦下发射）。
- `InternalDevDocs\issues\issue-submission-shared-member-implicit-me.md`（issue 08）——同一「共享成员未铺开的边界」母题，但链路不同（那条在接收者解析/隐式 `Me`，本条在初始化器诊断与发射门）。

## 未复现 / 未查

- **Release 构建**下的表现（仅跑 Debug；`Debug.Assert` 在 Release 编译掉，是否变成 NRE 或静默产生错误产物，**未测**）。**修复后**该分歧对本形状已无关紧要：error 在**发射前**就 gate 掉，不再依赖 `Debug.Assert` 是否被编译掉；但其它"只断言不报错"的形状仍可能出现 Release/Debug 分歧（**推测**，未清点）。
- 非提交的 `SourceCodeKind.Script` 编译（`ScriptClassName` 选项路径）下的同形状（**未跑**）。
- 除 `Await` 之外还有哪些「初始化器绑定期诊断」落进同一个洞（**未清点**，见修复方向末条）。
- 探针已跑并清除（`tmp\extprobe\` 恢复为空）。
