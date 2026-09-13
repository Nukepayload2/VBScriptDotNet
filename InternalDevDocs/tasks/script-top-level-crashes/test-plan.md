# 测试计划：脚本顶层崩溃族一次收口（script-top-level-crashes）

> 状态：测试计划。依据链：`README.md`（判定表 / 分批 / 实测清单）→ `design-overview.md`（总体设计 / 回归风险）→ `design-detailed.md`（§F04–§F11 逐单元 pass 条件）→ 本文件。
> 上游依据：`..\..\issues\` 八份 issue（04–11）的「预期行为」节；`..\..\spec\spec-scripting-dialect.md` 的 `Testing` 节（`:351-361`，无副作用口径的既有规范表述）。失败点缓存 `<项目根>/tmp/vortex-logs/top-level-implicit-shared/pitfalls.md`（P-001…P-038）。
> **测试宿**：编译器侧 `Compilers\VisualBasicSemanticTest` / `VisualBasicSymbolTest` / `VisualBasicEmitTest`（走七门 gate）；宿主侧 `Scripting\VisualBasicTest`（**MTP 项目：`dotnet test` 静默不跑**，须直跑程序集 `-automated`，见 memory `vb-scripting-test-runner`）。
> **无副作用纪律**（CLAUDE.md）：单测禁网络请求 / 文件写入 / 进程启动 / 注册表写入。本任务**全部用例为内存 I/O**——编译器侧 `CreateCompilation(...).GetDiagnostics()` + `Emit(New MemoryStream(), ...)`；宿主侧 `VisualBasicScript.Create/Compile/RunAsync` + 内存 `StringReader`/`StringWriter`（`Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb:5-10`）。

---

## 1. 测试分层矩阵（按单元标注适用性）

| 层 | 落点 | 适用单元 | 说明 |
|---|---|---|---|
| **L1 解析** | `Compilers\VisualBasicSyntaxTest` | **无新增用例（N/A）** | 本任务**无解析器改动**（`Parser\` 零 diff）；F11 改的是**声明表收集**（`SourceMemberContainerTypeSymbol.vb`）而不是解析，F04 改的是**属性解码**（`SourceMethodSymbol.vb`）而不是属性语法。L1 仅作全量回归面（顶层标签语句与 `<Extension>` 属性语法各有既有用例兜底） |
| **L2 语义/声明** | `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb`（诊断体例，用 `ERRID` 名断言）、`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMethods\ExtensionMethodTests.vb`（脚本模式扩展方法体例）、`Compilers\VisualBasicEmitTest`（发射/不发射） | **F04 / F05 / F06 / F07 / F08 / F09 / F10 / F11 全部** | 诊断 id + 位置 + 符号形状 + 「是否发射」都在这一层判；`Event`/`WithEvents` 的断言终止在本层即被判别（**未修复时测试进程被终止**，见 §7 注 1） |
| **L3 API** | `Scripting\VisualBasicTest\*.vb`（`VisualBasicScript.Create/Compile/RunAsync/ContinueWith`） | **全部** | 宿主可见症状的**主战场**：issue 05 的 `TypeLoadException` 在 `Scripting\Core\ScriptBuilder.cs:189-196` 才暴露、issue 09/10/11 的进程终止在发射期、issue 08 的 `InvalidProgramException` 在运行期——只有 L3 能端到端判定「不再崩且行为正确」 |
| **L4 宿主/REPL** | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` 构造体例（`:91-116`；**按 §5 的构造限定自建，不沿用其 `CreateRunner`**）+ `TestConsoleIO` | **F04 / F05 / F06 / F08 / F09 / F10 / F11**（F07 可选） | 端到端：REPL 提交链不崩会话、`.vbx` 退出码从 `3/24/34/35/58` 变为 `0`（修好）或 `1`（报错）+ 诊断文本 |

> **层间分工口径**：L2 判「诊断与符号形状」，L3 判「宿主不崩 + 结果正确」，L4 判「退出码与会话连续性」。**同一形状至少两层**（L2 + L3），涉及退出码语义的再加 L4。

---

## 2. L1 解析层

**本任务无新增 L1 用例。** 理由（已检查）：`design-detailed.md` §变更面汇总 的改动文件清单里没有任何 `VB\Parser\` 或 `VB\Syntax\` 文件；F11 的标签语句在**解析期已经是合法语法节点**（`SyntaxKind.LabelStatement`，`VB\Binding\Binder_Statements.vb:59-60` 已有绑定分派），F04 的 `<Extension>` 属性语法亦不变。

**回归锚点（不新增，只需全量通过）**：`Compilers\VisualBasicSyntaxTest` 整门（含顶层标签语句与属性语法的既有用例）。

---

## 3. L2 语义/声明层（用例表）

> 命名：`<单元>-L2-<序号>`。每条断言必须给**诊断 id**（`ERRID` 名，非 `BC` 字符串）与**位置**（`Location.GetLineSpan()` 或 `GetLocation()`）或**符号形状**；「零诊断」断言用 `VerifyDiagnostics()`（无参数）。

### F04（扩展方法必须 `Shared`）

| # | 用例 | 输入（脚本/提交模式） | 断言 | 无副作用 |
|---|---|---|---|---|
| F04-L2-1 | 漏 `Shared` | `<Extension>` + `Function Twice(s As String)` | 报**新码**；位置 = `<Extension>` 属性语法；零其它诊断 | 内存编译 |
| F04-L2-2 | 正向（对照） | `<Extension> Shared Function Twice(s As String)` | `VerifyDiagnostics()` 零诊断（既有 `ScriptExtensionMethods` 同形） | 内存编译 |
| F04-L2-3 | 嵌套容器（回归） | `Class C` 内 `<Extension> Function F(o As Object)` | BC36551（`:1627-1628` 分支，**次序不变**） | 内存编译 |
| F04-L2-4 | 标准模块（回归） | `Module M` + `<Extension> Function F(o As Object)`（实例/共享各一试） | 模块成员隐式 `Shared` ⇒ 合法，零诊断 | 内存编译 |
| F04-L2-5 | 无参（回归） | `<Extension> Shared Function F()` | BC36552（`:1630-1631`，**次序不变**） | 内存编译 |
| F04-L2-6 | 首参 Optional（回归） | `<Extension> Shared Function F(Optional o As Object = Nothing)` | BC36548（`:1638-1639`） | 内存编译 |
| F04-L2-7 | 普通类（回归） | 普通 `Class C`（非脚本）内 `<Extension> Shared Function F(o As Object)` | BC36551（普通类不满足 `AllowsExtensionMethods()`）；**新分支不可达** | 内存编译 |

### F05（共享初始化器落进无参共享构造器）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F05-L2-1 | 共享字段 | 提交模式 `Shared sx As Integer = 5` | 零诊断；该 `TypeKind.Submission` 类型的 `MethodKind.SharedConstructor` 成员**唯一**且 `Parameters.IsEmpty` | 内存编译 |
| F05-L2-2 | 共享 `ReadOnly` | `Shared ReadOnly sx As Integer = 5` | 同 F05-L2-1 | 内存编译 |
| F05-L2-3 | 数组上界（无 `=`） | `Shared Dim arr(2) As Integer` | 同 F05-L2-1（触发条件是「静态桶有条目」，不是「写了 `=`」） | 内存编译 |
| F05-L2-4 | **同文件 `.cctor` 合并**（`design-detailed.md` §F05 的 plan 前置实证） | `Const d As Date = #1/1/2020#` + `Shared x As Integer = 5` | 共享构造器**唯一**；不抛 `TypeLoadException`；两条初始化都在其体内（以 L3 的取值断言收口） | 内存编译 |
| F05-L2-5 | 实例形状（回归） | `Dim iy As Integer = 7` | 零诊断；**不产生**共享构造器；`<Initialize>` 路径不变 | 内存编译 |

### F06（共享初始化器里的 `Await` → 新码）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F06-L2-1 | 共享字段 | `Shared Dim x = Await Task.FromResult(1)` | 报**新码**；位置 = `Await` 关键字 | 内存编译 |
| F06-L2-2 | 共享 `ReadOnly` 字段 | `Shared ReadOnly x As Integer = Await Task.FromResult(1)` | 同 F06-L2-1 | 内存编译 |
| F06-L2-3 | 共享**属性** | `Shared ReadOnly Property P As Integer = Await Task.FromResult(7)` | 同 F06-L2-1（判据覆盖 `PropertySymbol`，**不照抄 C# 的 backing-field 形状**） | 内存编译 |
| F06-L2-4 | 共享数组上界路径 | （按 `design-detailed.md` §待定项 U2 判定是否可表达；不可表达则记录为**不可达**并附证据） | 二选一：报新码，或显式记录不可达 | 内存编译 |
| F06-L2-5 | 实例字段（回归） | `Dim y = Await Task.FromResult(2)` | 零诊断（`Binder_Expressions.vb:4726-4727` 的原承诺） | 内存编译 |
| F06-L2-6 | 实例属性（回归） | `ReadOnly Property P As Integer = Await Task.FromResult(7)` | 零诊断 | 内存编译 |
| F06-L2-7 | 嵌套类型（划界，回归） | `Class C` 内 `Shared s As Integer = Await Task.FromResult(9)` | 仍 BC36937（属 F09 面，**不得**被 F06 的新判据截胡） | 内存编译 |

### F07（顶层 `Event` / `WithEvents`）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F07-L2-1 | 实例 `Event` | `Event E As EventHandler` | `VerifyDiagnostics()` 零诊断（**本用例在未修复时终止测试进程**，见 §7 注 1） | 内存编译 |
| F07-L2-2 | `Shared Event` | `Shared Event E As EventHandler` | 同 F07-L2-1 | 内存编译 |
| F07-L2-3 | 实例 `WithEvents` | `Class Raiser` + `WithEvents r As New Raiser` | 同 F07-L2-1 | 内存编译 |
| F07-L2-4 | `Shared WithEvents` | 同 + `Shared WithEvents r As New Raiser` | 同 F07-L2-1 | 内存编译 |
| F07-L2-5 | 嵌套类型（回归） | 嵌套类里 `Public Event E` / `Public WithEvents r` | 零诊断（既有行为，q34/q35 形状） | 内存编译 |
| F07-L2-6 | 真隐式类（回归） | 非脚本编译里的隐式类容器（`ImplicitClass`） | 原断言语义**保住**（断言仍生效；以「未引入新诊断/未改发射」为可判形式） | 内存编译 |

### F08（共享成员的隐式 `Me` → BC30369）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F08-L2-1 | 共享方法体读实例字段 | `Dim sx As Integer = 5` + `Shared Sub S()` 内读 `sx` | BC30369，指向 `sx` | 内存编译 |
| F08-L2-2 | 共享初始化器调实例方法 | `Function F() As Integer` + `Shared Dim y = F()` | BC30369，指向 `F()`（**依赖 F05 已落地**才可见） | 内存编译 |
| F08-L2-3 | **回归锁：实例方法体** | 实例方法体隐式读顶层 `Dim` | 零诊断（`spec:243`） | 内存编译 |
| F08-L2-4 | **回归锁：顶层语句** | 顶层语句隐式读顶层 `Dim` | 零诊断（`<Initialize>` 非共享） | 内存编译 |
| F08-L2-5 | 显式 `Me`（回归） | 脚本类里 `Me.sx` | BC36966（`ERR_KeywordNotAllowedInScript`，**不变**） | 内存编译 |
| F08-L2-6 | 共享属性初始化器 | `Shared ReadOnly Property P As Integer = F()`（F 实例方法） | BC30369（`design-detailed.md` §待定项 U1；不命中则须补 Property 分支判据） | 内存编译 |
| F08-L2-7 | 普通类（回归） | 普通类里两个形状 | BC30369（既有行为不动） | 内存编译 |

### F09（初始化器诊断 gate 发射）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F09-L2-1 | 嵌套类实例字段 | `Class C` + `Dim s As Integer = Await Task.FromResult(9)`（v1） | BC36937 报出；`Emit(New MemoryStream(), ...)` **返回失败且不抛/不终止**（发射被跳过） | 内存 emit |
| F09-L2-2 | 嵌套类共享字段（v2） | 同 + `Shared` | 同 F09-L2-1 | 内存 emit |
| F09-L2-3 | 嵌套类实例属性（v3） | `Property p As Integer = Await Task.FromResult(9)` | 同 F09-L2-1 | 内存 emit |
| F09-L2-4 | 对照 v4 | `Public Shared s As Integer = Await 5` | BC36937 + BC36930 都报（既有行为不变） | 内存编译 |
| F09-L2-5 | 对照 v5 | 同 v2 再叠一个重复成员 | BC30260 + BC36937 都报（既有行为不变） | 内存编译 |
| F09-L2-6 | **warning 不 gate** | 初始化器里有 warning 无 error 的形状（如未使用局部/隐式转换警告） | 零 error；`Emit` **成功** | 内存 emit |
| F09-L2-7 | 无初始化器错误的编译（回归） | 干净脚本 | `Emit` 成功；产物与基线一致（全量兜底） | 内存 emit |
| F09-L2-8 | **语句种类：顶层语句**（与字段/属性初始化器共用入口、实例桶、诊断袋） | 提交模式顶层 `SyncLock <非引用类型>`（BC30582，**bag-only**：`Binder_Statements.vb:4802-4818` 不置 hasErrors）＋ 干净语句 ＋ 干净嵌套类 | 恰好 1 条 error = BC30582；`Emit` 返回失败且**不抛** | 内存 emit |
| F09-L2-9 | 顶层语句错误 ＋ 同桶内 codegen 必炸形状（判别性：修前该桶不被 gate ⇒ 发射期崩） | 同上 ＋ `GoTo skip` / `skip:`（issue 11 形状，自身零诊断） | 恰好 1 条 BC30582；**不抛**（该桶整体不发射） | 内存 emit |
| F09-L2-10 | 顶层语句**内部嵌套块**里报出的 bag-only 错误（位置维度） | `If True Then` / `SyncLock 5` / `End SyncLock` / `End If` ＋ 干净语句 | 恰好 1 条 BC30582 | 内存 emit |
| F09-L2-11 | 语句种类：顶层**裸表达式语句**（**非末条**才报错） | 提交模式 `1 + 2` ＋ 干净语句（末条是提交返回值 ⇒ 单条 `1 + 2` **不报错**，实测 `Emit` 成功） | 恰好 1 条 BC31003；`Emit` 返回失败且**不抛** | 内存 emit |
| F09-L2-12 | 干净顶层语句不劣化（回归） | 干净顶层语句（含裸 `Await` 语句、`If`、字段）＋ 干净嵌套类 | 零 error；`Emit` **成功** | 内存 emit |
| F09-L2-13 | **两桶交叉**：实例桶有错、静态桶干净且**非空** | `Class C` + `Dim s As Integer = Await …` + `Shared t As Integer = 42` | `Emit` 失败；恰好 1 条 BC36937（干净桶不吞错也不加错） | 内存 emit |
| F09-L2-14 | **两桶交叉**：静态桶有错、实例桶干净且**非空**（镜像） | 同 F09-L2-13 把 `Shared` 与实例对调 | 同 F09-L2-13 | 内存 emit |

### F10（顶层 `Catch`/`Finally`/`SyncLock` 内的 `Await` → BC36943）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F10-L2-1 | `Catch` 内 | 顶层 `Try` / `Catch` 内 `Await Task.Delay(1)`（g6） | BC36943；位置 = `Await` 关键字 | 内存编译 |
| F10-L2-2 | `Finally` 内 | 顶层 `Finally` 内 `Await`（g15） | BC36943 | 内存编译 |
| F10-L2-3 | `SyncLock` 内 | 顶层 `SyncLock <对象>` 内 `Await`（g14） | BC36943（**这是 F10 最重要的新覆盖**：今天不崩而是静默产出坏 IL） | 内存编译 |
| F10-L2-4 | `Try` 体内（对照） | 顶层 `Try` 体内 `Await`（g5） | 零诊断 | 内存编译 |
| F10-L2-5 | `Using` 内（对照） | 顶层 `Using` 内 `Await`（g10） | 零诊断（`VisitUsingStatement` 不置 catch/finally/synclock 标志） | 内存编译 |
| F10-L2-6 | 普通 `Async Function`（回归） | `Async Function` 的 `Catch`/`Finally` 内 `Await`（g8） | BC36943 ×2（既有行为逐字不变） | 内存编译 |
| F10-L2-7 | **On Error（回归锁）** | 顶层 `On Error Resume Next` 与 `Try` 混用 | 既有诊断（`ERR_TryAndOnErrorDoNotMix` / unsupported-statement）**不变** ⇒ 证明开关未把 On Error 报告带进顶层 | 内存编译 |
| F10-L2-8 | lambda 内（对照） | 顶层语句里 `Dim f = Task.Run(Async Function() Await Task.FromResult(1))` | **不报** BC36943（walker 的 `VisitLambda` 不下钻） | 内存编译 |
| F10-L2-9 | 多树提交（`design-detailed.md` §待定项 U3） | 主文件 + `#Load` 的被载文件各含一个 `Catch` 内 `Await` | 两条诊断、各锚定到肇事树的行号；**不重复报** | 内存编译（内存 `SourceReferenceResolver`，不落盘） |

### F11（顶层标签入实例初始化器序列）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F11-L2-1 | 前向 `GoTo`（无 `Await`，g1） | `GoTo skip` + `skip:` 标签 | 零诊断；`Emit(New MemoryStream(), ...)` **成功且不抛/不终止** | 内存 emit |
| F11-L2-2 | 前向 `GoTo` + `Await`（g2） | 标签后接 `Await Task.Delay(1)` | 同 F11-L2-1 | 内存 emit |
| F11-L2-3 | 反向 `GoTo` 循环（g11） | `top:` 标签 + `If i < 3 Then GoTo top` | 同 F11-L2-1 | 内存 emit |
| F11-L2-4 | 标签未使用（回归） | 顶层孤立标签（无 `GoTo`） | 零诊断；`Emit` 成功 | 内存 emit |
| F11-L2-5 | 数字行号标签（`design-detailed.md` §待定项 U4） | 顶层 `10:` + `GoTo 10` | 行为与既有诊断不变；若产生差异须打回重裁 | 内存 emit |
| F11-L2-6 | 多标签（回归） | 顶层两个标签 + 两个 `GoTo` | `Emit` 成功；跳转目标不串（以 L3 输出断言收口） | 内存 emit |

---

## 4. L3 API 层（宿主可见症状的主战场）

> 落点 `Scripting\VisualBasicTest\`（新增测试文件，命名 `ScriptTopLevelCrashTests.vb`；体例参 `ScriptTests.vb:16-70` / `InteractiveSessionTests.vb:14-47`）。全部走内存；控制台输出用 `TestConsoleIO`（`Helpers\TestConsoleIO.vb:5-10`）。

| # | 用例 | 构造 | 断言 | 无副作用 |
|---|---|---|---|---|
| F05-L3-1 | 共享字段正常跑 | `VisualBasicScript.RunAsync("Shared sx As Integer = 5" & vbCrLf & "Console.WriteLine(""OK "" & sx)")` | **不抛**（尤其**不抛** `TypeLoadException`）；`io.Out` 含 `OK 5` | 纯内存 |
| F05-L3-2 | 同提交的两个共享初始化器（同文件 `.cctor` 合并的运行期收口） | `Shared d As Date = #1/1/2020#` 与 `Shared x As Integer = 5` 同提交 | 不抛；`x` 与 `d.Year` 取值正确 | 纯内存 |
| F05-L3-3 | 实例形状（回归） | `Dim iy As Integer = 7` | 零诊断、取值正确 | 纯内存 |
| F06-L3-1 | 共享 `Await` 初始化器 → 报错不崩 | `Shared Dim x = Await Task.FromResult(1)` | `script.Compile()` 返回**新码**；`RunAsync` 抛 `CompilationErrorException` 且其 `Diagnostics` 含新码；**进程不终止** | 纯内存 |
| F07-L3-1 | `Event` 语义 | 提交类顶层 `Event E As EventHandler` + 顶层 `AddHandler` + `RaiseEvent`（经嵌套类的 `Raise` 方法） | 零诊断；handler 被调用（计数 = 1） | 纯内存 |
| F07-L3-2 | `WithEvents` 钩子 | 顶层 `WithEvents r As New Raiser` + handler 计数 | 零诊断；`r.Raise()` 后计数 = 1 | 纯内存 |
| F08-L3-1 | 共享方法体读实例字段 | `Dim sx As Integer = 5` + `Shared Sub S()` 读 `sx` | `Compile()` 返回 BC30369；**不再**抛 `InvalidProgramException`；**不产生可运行产物** | 纯内存 |
| F09-L3-1 | 嵌套类 `Await` 初始化器 | `Class C` + `Dim s As Integer = Await Task.FromResult(9)` | `Compile()` 返回 BC36937；**不崩**（对照：未修复时 exit 35） | 纯内存 |
| F09-L3-2 | 顶层语句错误（语句种类维度的 L3 对应物） | `SyncLock <非引用类型>` ＋ 干净语句 ＋ 干净嵌套类 | `Compile()` 返回 BC30582；`RunAsync` 抛 `CompilationErrorException` 且其 `Diagnostics` 含 BC30582；**不崩** | 纯内存 |
| F10-L3-1 | 顶层 `Catch` 内 `Await` | g6 形状 | `Compile()` 返回 BC36943；**不崩** | 纯内存 |
| F10-L3-2 | 顶层 `SyncLock` 内 `Await` | g14 形状 | `Compile()` 返回 BC36943；**不崩**（对照：未修复时编译通过但运行抛 `SynchronizationLockException`） | 纯内存 |
| F11-L3-1 | 顶层 `GoTo` 跳转生效 | g2 形状 + 输出标记 | 零诊断；`io.Out` 含 `A` 与 `C`、**不含** `B`；零异常 | 纯内存 |
| F11-L3-2 | 反向循环 | g11 形状 + `Console.WriteLine("LOOPED " & i)` | 零诊断；输出 `LOOPED 3` | 纯内存 |
| F04-L3-1 | 漏 `Shared` 的扩展方法 | `<Extension> Function Twice(s As String)` | `Compile()` 返回新码；**不终止进程** | 纯内存 |

> **注（L3 的判别力）**：L3 断言是**唯一**能证明 issue 05 修复的层——`TypeLoadException` 由宿主在 `ScriptBuilder.GetEntryPointRuntimeMethod`（`Scripting\Core\ScriptBuilder.cs:189-196`）抛出，编译器侧看不到。反过来，L3 对 F04/F06/F08/F10 的「报错」判定只能证明「诊断出现且不崩」，**不能**替代 L2 的诊断 id / 位置断言。

---

## 5. L4 宿主 / REPL 层

> 落点同新增测试文件（`CommandLineRunner` + `TestConsoleIO`；构造体例参 `CommandLineRunnerTests.vb:91-116`）。输入串以 `vbCrLf` 分隔提交。
> **L4 的构造限定（无副作用）**：L4 用例**自建** `BuildPaths`，`tempDir` 指向**既有**目录（`AppContext.BaseDirectory`），**不得**调用 `CommandLineRunnerTests.vb:42-46` 的 `CreateIsolatedTempDirectory()`——它按既有行为以 `Directory.CreateDirectory` 建 `TestTemp\<guid>` 目录，属写盘。产品侧同形构造见 `Scripting\VisualBasic\VisualBasicScript.vb:151-155`（`tempDir` 用既有目录，不建目录）。
> **注意 pitfalls P-017**：REPL **逐行成提交**（每行一个 `Submission#N`）；跨提交读共享/实例状态时不要用 `GetTypes()(0)` 取类型，须沿 `BaseType` 链取。

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|---|---|---|---|
| F05-L4-1 | REPL 共享状态 | 提交 1 `Shared sx As Integer = 5` → 提交 2 `? sx` | 提交 1 **不抛** `TypeLoadException`；`io.Out` 含 `5`；会话继续 | 纯内存 |
| F09-L4-1 | 嵌套类 `Await` 初始化器（REPL） | 一条提交里的 `Class C` + `Await` 初始化器 | `io.Error` 含 `BC36937`；进程退出码 = 1（**不是** 35）；下一条提交仍可执行 | 纯内存 |
| F09-L4-2 | 顶层语句错误（REPL，语句种类维度） | 一条提交 `SyncLock <非引用类型>` → 下一条提交 `? 1 + 2` | `io.Error` 含 `BC30582`；会话继续（`io.Out` 含 `3`） | 纯内存 |
| F10-L4-1 | 顶层 `Catch`/`SyncLock` 内 `Await`（REPL） | 两种形状各一条提交 | `io.Error` 含 `BC36943`；退出码 = 1（**不是** 3 / 24）；会话继续 | 纯内存 |
| F11-L4-1 | 顶层 `GoTo`（REPL 多行） | 一条提交内多行含标签与 `GoTo` | 零诊断；`io.Out` 输出顺序正确；退出码 = 0 | 纯内存 |
| F04-L4-1 | `.vbx` 脚本：漏 `Shared` 的扩展方法 | 文件脚本路径（内存 resolver，不落盘）或 REPL 单提交 | `io.Error` 含新诊断文本；退出码 = 1（**不是** 35） | 纯内存 |
| F07-L4-1 | `.vbx` 脚本：顶层 `Event` | 文件脚本 | 零诊断；`io.Out` 含 `EVENT-OK`；退出码 = 0（对照 g12 的 35） | 纯内存 |

---

## 6. VB 特性维度（覆盖面自检）

| 维度 | 覆盖 | 用例 |
|---|---|---|
| 脚本 vs 提交 vs 普通脚本类 | 提交（`.vbx` / REPL）为主；非提交脚本类（`DeclarationKind.Script`）**明确不覆盖**（会议 R7 OPEN：非提交脚本类今天走 `EnsureCtor`，形状正确，本任务不动它 ⇒ 以全量回归兜底） | F05-L2-1–4、F09-L2-1–3 |
| 共享性 | 实例 / `Shared` / `Shared ReadOnly` 三态 | F05-L2-1–3、F06-L2-1–2、F07-L2-1–4、F08-L2-1/3/4/6 |
| 成员种类 | 字段 / 属性 / `Event` / `WithEvents` / 方法 / 扩展方法 | F06-L2-3、F07-L2-1–4、F08-L2-1–2、F04-L2-1 |
| 语句种类 | 字段/属性初始化器 与 **顶层语句**（块语句 / 裸表达式语句 / 语句内嵌套块 / `GoTo`+标签）；F09 的 gate 两种都要覆盖 | F09-L2-1–3、F09-L2-8–14、F09-L3-2 |
| 容器种类 | 脚本类本身 / 嵌套类（普通类）/ 标准模块 / 普通类 | F07-L2-5、F08-L2-7、F04-L2-3/4/7、F09-L2-1–3 |
| `Await` 位置 | 初始化器 / `Try` 体 / `Catch` / `Finally` / `SyncLock` / `Using` / lambda / 普通 async 方法 | F06-L2-1–3、F10-L2-1–6/8 |
| 控制流 | 前向 `GoTo` / 反向 `GoTo` / 多标签 / 孤立标签 / 数字行号标签 | F11-L2-1–6 |
| 提交链 | 单提交 / 两提交（跨提交读共享状态） | F05-L4-1 |
| 多树（`#Load`） | 累积树下的诊断锚定 | F10-L2-9 |
| 编译选项 | 脚本默认（`Option Strict Off` / `Infer On` / `Explicit On`）为主；**不新增** `Option Strict On` 变体（与本族判据无关） | 全部 L2/L3 用例的默认环境 |
| 退出码语义 | `0` 正常 / `1` 报错（**不再出现 3 / 24 / 34 / 35 / 58**） | F05-L4-1、F09-L4-1、F10-L4-1、F11-L4-1、F04-L4-1、F07-L4-1 |

---

## 7. 无副作用纪律（逐条核对）

1. **网络**：零。本任务无 NuGet / `#R "nuget:"` / HTTP 路径（`NuGet*` 测试与本任务无关）。
2. **文件写入**：零。编译器侧 `Emit` 目标为 `New MemoryStream()`；F10-L2-9 的 `#Load` 用**内存** `SourceReferenceResolver`（体例见 `ScriptTests.vb:525-569`，不落盘）。既有 `ScriptTests.vb:54-55` 的 `File.ReadAllBytes(GetType(Strings).Assembly.Location)` 是**读**，不在禁令内。**L4 的写盘风险由 §5 的构造限定消解**：既有 `CommandLineRunnerTests.vb:91-116` 的 `CreateRunner` 会在构造路径上无条件调用 `:42-46` 的 `CreateIsolatedTempDirectory()`（写盘）⇒ 新增 L4 用例**不得沿用该 `CreateRunner`**，须按 §5 自建；§8 第 4 条按此抽查。
3. **进程启动**：零。**本任务的 `.vbx` 探针（`README.md` §六 g1–g15）只作设计阶段的取证，绝不写成验收用例**（那会 `Process.Start`）。验收一律走 L2/L3/L4 的内存路径。
4. **注册表**：零。
5. **抽查方式**：W-GATE 对新测试文件做 grep 抽查：不含 `File.Write` / `Directory.Create` / `CreateIsolatedTempDirectory` / `Process.Start` / `HttpClient` / `Registry` / `WebClient`。grep 只覆盖新增文件，故 L4 的构造路径另须**逐例核对**（§8 第 4 条）——`CreateIsolatedTempDirectory` 一旦出现在新增文件内即判不过。

> **注 1（断言终止 ≠ 测试失败，判别时必须区分）**：F07 / F09 / F11 在**未修复**时会让测试进程被 `Debug.Assert` 终止或抛 NRE（表现是整轮测试中断，而不是某条 `Failed`）。验证者核对时：
> - **预期（修复后）**：`TestCasesToRun` 与基线一致，`Failed = 0`。
> - **未修复**：进程中断（无汇总输出）或某条 `Failed`。
> 两者都算「未达标」，但归因不同（前者是断言/崩溃，后者是断言不成立），**不得**把「进程没崩」当成「用例通过」的唯一判据。

---

## 8. 全量回归口径与判定标准

### plan 阶段标准（本文件）

- 分层矩阵完整（L1 N/A 有理由、L2/L3/L4 适用）；用例编号可数（`F04`–`F11` × `L2/L3/L4`，共 **82 条**：L2 61 / L3 14 / L4 7）；每例有可判断言点（诊断 id / 位置 / 符号形状 / 输出 / 退出码）。
- 无副作用纪律明确（§7 五条）。
- 全量回归口径明确（下述两档）。

### 实现阶段标准（W-GATE）

1. **`Scripting\VisualBasicTest` 直跑程序集全量 0 失败**：
   `dotnet build Scripting\VisualBasicTest\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.vbproj` →
   `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`
   （**禁用 `dotnet test`**；类级/单测过滤的 FQN 须完整，缺段或拼错**静默 0 跑**，须核对 `discovery-complete` 的 `TestCasesToRun > 0`）。基线 = 实施开始时的当前全量通过数（**实施期读取，不凭记忆**；体例参 `..\imports-accumulation-diagnostics\README.md` 的口径），新增用例后应等于「基线 + 新增数」且 `Failed = 0`。
2. **七门 gate 与基线一致或按新增用例递增**：`powershell -File scripts\verify-vb-compiler-tests.ps1`（Phase2 / Syntax / Symbol / Semantic / IOperation / Emit / CommandLine；基线数字以 `scripts\verify-vb-compiler-tests.ps1:7-15` 的**当前值**为准，实施期读取）。本任务**有**编译器改动 ⇒ Symbol / Semantic / Emit 三门预期按新增用例递增；Syntax / Phase2 / IOperation / CommandLine 预期不变（无解析器与命令行改动）。
3. **零越权核对**：`git diff --stat` 仅含 `design-detailed.md` §变更面汇总 白名单内的编译器文件 + 新增测试文件 + `Errors.vb`/`ErrorFacts.vb`/`VBResources.resx`/13 `xlf`；**`Compilers\Core\Portable\CodeGen\` 零 diff**；`PublicAPI.*.txt` 零增量；`spec\` / `meetings\` / `proposals\` / `issues\` 零 diff。
4. **无副作用抽查**：§7 第 5 条的 grep 全空；并**逐例核对 L4 用例的 `BuildPaths` 构造**——`tempDir` 为既有目录、无 `CreateIsolatedTempDirectory()` 调用（grep 只覆盖新增文件，跨文件的既有基建写盘由这条构造核对兜住）。
5. **规范闭环接线核对**：`design-detailed.md` §账本与规范义务 的 7 条 spec 义务与 4 条 issue 义务**各有落点已登记**（本任务不执行文本改动，只核对义务不丢项；文本改动由 spec/issue 阶段承担）。
6. **无遗留 Unresolved**：`design-detailed.md` §待定项 U1–U5 各给出结论（实测或显式记录不可达），不得以「已知后续项」收尾。

---

## 9. 既有测试影响核查

| 受影响面 | 影响 | 处理 |
|---|---|---|
| `Compilers\VisualBasicSymbolTest\...\ExtensionMethodTests.vb` 的 `ScriptExtensionMethods`（`:2425-2440`）/ `InteractiveExtensionMethods`（`:2442-2478`） | F04 改同一段解码序列 | **零改动必须通过**（F04-L2-2 等价）；注意 `ScriptExtensionMethods` 标着 `ConditionalFact(NoUsedAssembliesValidation)`，部分配置下跳过 ⇒ **不得**把「它通过」当成 F04 的鉴别力证据（**测试鉴别力须单独论证**，见 `manifest.md` 的「测试鉴别力」口径） |
| `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb` | F06 / F08 / F10 改判据 | 既有 BC30369 / BC36937 / BC36943 用例**零改动通过** |
| `Compilers\VisualBasicEmitTest`（Emit 门） | F09 / F11 改「是否发射」 | 全门通过；**若既有用例的期望从「发射」变「不发射」须逐个说明理由**（预期不会发生：F09 只在新增 error 时 gate） |
| `Compilers\VisualBasicCommandLineTest` | 无命令行改动 | 全门通过 |
| `Scripting\VisualBasicTest` 既有脚本用例（`ScriptTests.vb` 的顶层 `Await` 用例 `:363-372`/`:374-384`/`:386-394`、`InteractiveSessionTests.vb`） | F05 / F09 / F10 / F11 改脚本顶层行为 | **零改动必须通过**（尤其 `TestTopLevelAwaitInStatement` / `TestTopLevelBareAwaitStatement`：它们证明「顶层 `Await` 本身合法」，是 F10 不能误伤的正向锚点） |
| `ScriptTests.vb:299-309` 的 `TestTopLevelGoToLabelStatementCompiles`（`<Fact>` 于 `:299`；`:301-306` 建脚本、`:308` 断言 `GetCompilation().GetDiagnostics()` 无 error） | F11 改顶层标签语句的收集 | **零改动必须通过**；它是 F11 形状（顶层 `GoTo` + 标签，探针 g1 形状）的既有正向锚点，独立锁死 F11 的「零诊断」半 |
| `upstream-merge.md` | 本任务改动面含**未在册**文件 | §账本与规范义务 逐条补登记（W-GATE 第 5 条核对） |
| `spec-scripting-dialect.md` | F11 使 `:266-268` 的 Decision 失效；F04/F06 新增诊断族行 | 义务登记（本任务不改文本） |
