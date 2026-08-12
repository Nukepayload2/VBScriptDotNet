# 测试计划：REPL 表达式开头 ? 可选（optional-question-prefix）

> 状态：测试计划（F7）。依据链：`../../proposals/proposal-optional-question-prefix.md`（Active）→ `../../meetings/meeting-optional-question-prefix.md`（RESOLUTION #1-#5）→ `design-overview.md`（F1）→ `design-detailed.md`（F2，§7 测试矩阵）→ 本测试计划。
> 本计划把 F2 §7 的 19 条用例升级为**分层测试设计**：参考 C# 对应特性（`AllowsAnyExpression` / interactive 表达式语句）的**四层测试强度**，并按 **VB 语法特性**（标签、比较、`=`、字符串 `&`、方法组、成员访问、晚绑定、续行、`:` 分隔、`Option Strict` 矩阵、`.vbx` 代价）综合设计。
> 实现阶段由实施者照本计划补测试，验证者按「无副作用纪律」与「行为对照表」核对。

## 0. 目标与验收

- **目标**：在 C# 测试强度（解析树形断言 → 语义诊断断言 → 编译 API `HasSubmissionResult` 矩阵 → REPL 全输出断言）之上，覆盖 VB 语法特性特有的全部边界，保证：
  1. 自动打印（值引用）正确；
  2. 合法语句（赋值 / 带副作用调用 / 无括号 Sub 调用）**不改义**；
  3. `?` 显式前缀兼容；`? x = 5` 按比较处理；
  4. 仅**末尾**表达式产生结果（非末尾仍报错）；
  5. Option Strict On/Off **无分叉**；
  6. `.vbx` 脚本模式静默 no-op、退出码正交；
  7. `Regular` 编译**零影响**。
- **验收**：四层矩阵全绿；既有测试（`CompilationAPITests.vb:2644` 等）按 §6 更新；无副作用纪律满足（§8）。

## 1. 参考的 C# 测试强度（四层）

C# 对同一特性（interactive 表达式语句 / `AllowsAnyExpression`，`Compilers\CSharp\Portable\Syntax\ExpressionStatementSyntax.cs:19`；binder `Binder_Statements.cs:649` 跳过 `IsValidStatementExpression` 检查）的测试是**分层叠加**的。本计划逐层对齐：

| 层 | C# 测试锚点 | 强度特征 | VB 落点 |
|---|---|---|---|
| L1 解析层 | `ScriptParsingTests.cs` `Multiplication_Interactive_NoSemicolon` / `_Semicolon`（`Compilers\CSharp\Test\Syntax\Parsing\`） | 逐节点树形断言（`N()`/`M()`，含 missing token），同源文本在 Script/Regular 下分别断言 | `Compilers\VisualBasicSyntaxTest\Parser\`（`ParseAndVerify` / `VerifySyntaxKinds`，`ParserTestUtilities.vb:291`） |
| L2 语义层 | `ScriptSemanticsTests.cs` `ArithmeticOperators_MultiplicationExpression`：`i* i`（交互式，无分号）**无诊断**，`i* i;`（普通语句）**CS0201** | `CreateSubmission` + 精确 `VerifyDiagnostics`/`VerifyEmitDiagnostics`，同一表达式两种模式对照 | `Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`（`CreateSubmission`，`BasicTestBase.vb:437`） |
| L3 编译 API | `CompilationAPITests.cs:2432` `HasSubmissionResult()`：逐条断言各提交的真/假（`"1"`→True、`"1;"`→False、`null`→True、`void goo(){}`→False、`WriteLine()`→False） | 编译对象 API 断言，不跑宿主 | `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb:2639`（**已有 TODO，§6 翻转**） |
| L4 REPL/命令行 | C# csi 端到端（`CommandLineTests.cs` 交互/重定向测试） | 全输出断言（logo + 提示符 + 值 + 颜色标记） | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`（`CreateRunner(input:=...)` + `TestConsoleIO`） |

> 强度原则：**同一输入在「交互 Script / 普通 Regular」两种模式下必须分别断言**（C# 的 `i* i` 无分号 vs `i* i;` 就是这种对照）；**同一声明（如 `Sub MySub`）既要验证打印面也要验证不打印面**（方法组消歧正反两面）。

## 2. VB 语法特性综合维度

C# 用「缺分号」表达「末尾表达式」，VB **无分号**，靠「绑定结果形态（`BoundKind`）+ 裸表达式语句解析」。VB 特有的语法特性构成测试维度（每维 → 对应矩阵节）：

| 维度 | VB 特性 | 关键用例来源（design-detailed §4/§7） |
|---|---|---|
| V1 裸数值/字面量起始 | `1 + 2` 撞标签路径（`ParseLabel`，`ParseStatement.vb:1573`）、字符串/布尔/`Nothing` 起始 | §4 其余行；§7.1 #3/#4 |
| V2 标签 vs 表达式 | `1:` 标签保留，`1 + 2` 表达式语句 | §2.1(a) |
| V3 裸标识符三类 | 变量（`before`）/ 属性（`Now`）/ 方法组（`MySub`/`MyFunc`）解析同形，绑定层消歧 | §3.3 表；§7.1 #1/#2/#7 |
| V4 比较 `=` | `? x = 5` 是**比较**打印 False；`x = 5` 是**赋值**不打印 | §2.1(b)；§8「? x = 5」已定案 |
| V5 成员访问形态 | `DateTime.Now` 保留 `X()` 调用形状（BC30545 抑制）；`obj.MySub` 无括号成员 Sub 调用不打印 | §2.1(b) 底线；§3.4 |
| V6 晚绑定 | `obj.Prop`（Object 接收者）绑定为调用 → 不打印；Strict Off 下天然正确 | §4 LateMemberAccess/LateInvocation 行；§8 |
| V7 续行与 `:` 分隔 | `.` 续行末尾表达式打印；`1 + 2 : x = 5` 非末尾 BC31003；`DateTime.Now : x = 5` 非末尾 BC30545 | §7.1 补充；§8 多行续行 |
| V8 字符串 `&` | `"a" & "b"` 打印 `"ab"`（VB 连接符，区别于 C# `+`） | §2.2；§4 其余行 |
| V9 Option Strict | On/Off 对值引用/成员访问/晚绑定一视同仁（无分叉） | §4 表尾；§8 |
| V10 `.vbx` 脚本模式 | 裸表达式从报错变静默 no-op；退出码只由 `Return` 决定 | §6；§7.2 |
| V11 显式 `?` 兼容 | `? Now` 不变；`? (` 走 PrintStatement（`CanStartConsequenceExpression` 只认 `.`/`!`） | §7.1 #8；P-009 |
| V12 畸形输入 | `Now +` 仍报语法错误（缺失右操作数，码由现状 BC30800 类偏移到「缺失表达式」BC3xxxx） | §7.1 #9；spec 测试事实 |

## 3. L1 解析层测试（`Compilers\VisualBasicSyntaxTest\Parser\`）

> 装置：`Parse(source, options:=TestOptions.Script)`（`ParserTestUtilities.vb:101-110`）+ `ParseAndVerify`（错误码断言）/ `VerifySyntaxKinds`（`ParserTestUtilities.vb:291` 树形断言）。**纯内存，无副作用。**

| # | 用例 | 输入（Script 顶层） | 断言 | 对应 C# 强度 |
|---|---|---|---|---|
| P1 | 裸数值表达式 | `1 + 2` | `ExpressionStatement(AddExpression)`，无 BC30801；`VerifySyntaxKinds` 含 `ExpressionStatement`/`AddExpression` | C# `Multiplication_Interactive_NoSemicolon` 树形 |
| P2 | 标签保留 | `1:` | 仍是 `LabelStatement`，无表达式语句 | C# Script 顶层 `goto Label:` 对照 |
| P3 | 字符串连接 | `"a" & "b"` | `ExpressionStatement(ConcatenateExpression)`，无诊断 | C# 表达式语句树形 |
| P4 | 布尔/`Nothing` 起始 | `True` / `Nothing` | `ExpressionStatement`，无诊断（现状 Case Else → `ReportUnrecognizedStatementError`） | C# 表达式语句树形 |
| P5 | 裸标识符 | `Now` / `before` / `MySub` | `ExpressionStatement(IdentifierName)`，**不是** `InvocationExpression`（不包 `X()`） | C# 裸表达式树形（无分号） |
| P6 | 比较被误包 | `x > 5` | `ExpressionStatement(RelationalExpression)`，无 BC30800（现状 `x()` 误包 + `> 5` 残留） | C# `i* i` 解析对照 |
| P7 | 成员访问保形状 | `DateTime.Now` / `obj.MySub` | 仍是 `InvocationExpression`（`X()` 形状），**不是**裸表达式（§2.1(b) 底线） | C# 成员访问表达式语句 |
| P8 | 显式 `?` 兼容 | `? Now` / `? (` | 仍是 `PrintStatement`（`ParseStatement.vb:1858`），无变化 | C# 无分号对照 |
| P9 | `? x = 5` | `? x = 5` | `PrintStatement` 表达式为 `RelationalExpression`（`=` 作比较，`ParseExpression.vb:134`） | — |
| P10 | 赋值不变量 | `x = 5` | `SimpleAssignmentStatement`，**不是**表达式语句 | C# `i = 1` |
| P11 | 无括号 Sub 调用 | `Call MySub` | `CallStatement` | C# `WriteLine()` |
| P12 | 多行续行 | `1 + 2 _`（换行） | `ExpressionStatement`，无诊断 | C# Script 续行 |
| P13 | 复合赋值不变量 | `x += 1` / `x &= "s"` / `x ^= 2` | `AddAssignmentStatement` / `ConcatenateAssignmentStatement` / `ExponentiateAssignmentStatement`（`MakeAssignmentStatement` 按操作符映射，`ParseStatement.vb:1166/:1190/:1181`；`IsAssignmentStatementOperatorToken` 含 `+=`/`&=`/`^=`，`Syntax.xml.Main.Generated.vb:44551`；三者均为 `AssignmentStatementSyntax` 子类，**不是**表达式语句） | C# `i += 1` |
| P14 | `Mid` 赋值不变量 | `Mid(s, 1, 2) = "ab"` | `MidAssignmentStatement`（上下文关键字消歧先于 `ParseAssignmentOrInvocationStatement`，`Parser.vb:1109-1116`），不受新分支影响 | — |
| P15 | `? .` / `? !` 分发 | `? .Foo` / `? !bar` | **非** PrintStatement——`. ` / `!` 触发 `CanStartConsequenceExpression`（`ParseExpression.vb:512-513`）→ 走成员访问/三元分发（`Parser.vb:1235`） | — |

## 4. L2 语义层测试（`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`）

> 装置：`CreateSubmission(source, parseOptions:=TestOptions.Script)`（`BasicTestBase.vb:437`）+ `AssertTheseDiagnostics` / `VerifyDiagnostics`。**纯编译 API，无副作用。**

| # | 用例 | 提交 | 断言 | 对应 C# 强度 |
|---|---|---|---|---|
| S1 | 裸属性值引用 | `Now` | 无诊断（现状 BC30545 抑制） | C# `i* i` 无诊断 |
| S2 | 裸局部变量 | 先 `Dim before = Now`（多行提交）再 `before` | 无诊断，绑定为 `BoundLocal` | C# `CreateSubmission` 链（`previous:`） |
| S3 | 成员访问属性 | `DateTime.Now` | 无诊断（末尾 BC30545 抑制） | C# 成员访问表达式 |
| S4 | 算术/比较/字符串 | `1 + 2` / `x > 5` / `"a" & "b"` | 无诊断 | C# `i* i` 无诊断 |
| S5 | 方法组不打印 | 先 `Sub MySub()` 再 `MySub`；先 `Function MyFunc()` 再 `MyFunc` | 无诊断（方法组→无参调用重分类，Sub 不打印 / Function 现状打印） | C# `WriteLine` 语句合法 |
| S6 | 带副作用调用 | `Console.WriteLine("hi")` | 无诊断（`BoundCall` Void，不改义） | C# `WriteLine();` 合法 |
| S7 | 赋值不改义 | `x = 5` | 无诊断（赋值语句，不打印） | C# `int i = 5;` |
| S8 | 非末尾裸表达式 | `1 + 2 : x = 5` | **BC31003** `ERR_UnexpectedExpressionStatement`（非末尾仍报错） | C# 无分号对照 |
| S9 | 非末尾成员访问 | `DateTime.Now : x = 5` | **BC30545** 不抑制（Invocation 分支非末尾） | — |
| S10 | 晚绑定 | `Dim o As Object = New X() : o.Prop` | 无诊断（`BoundLateMemberAccess`，不打印）；对照 Strict Off 下 `o.Prop` | C# dynamic 对照 |
| S11 | 空脚本/声明 | `Sub Goo() End Sub` / `Imports System` / `Dim i As Integer` | 无诊断、不打印 | C# `void goo(){}` 合法 |
| S12 | 条件访问 | `a?.Prop` | 无诊断（递归按 `WhenNotNull` 子形态） | — |

> **BoundKind 回归断言（RESOLUTION #5 / §4 回归步骤 2）**：对 S1/S2/S5 追加临时断言记录 `boundExpression.Kind`（`BoundPropertyAccess` / `BoundLocal` / `BoundMethodGroup→BoundCall`），对照 §4 判定表逐行核对；表外新形态并入 `BoundKind` 分支而非累加错误码。

## 5. L3 编译 API 测试（`Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb` `HasSubmissionResult`）

> 对齐 C# `CompilationAPITests.cs:2432`。`HasSubmissionResult`（`VisualBasicCompilation.vb:816-868`）现有分支：PrintStatement 恒 True / ExpressionStatement、CallStatement 非 Void 即 True / ReturnStatement 有表达式非 Void。

| # | 提交 | 现状 | 实现后 | 说明 |
|---|---|---|---|---|
| C1 | `1` | **False**（`:2644`） | **True** | **§6 翻转**；注释 `? should be optional`（`:2645`）删除 |
| C2 | `Now` / `DateTime.Now` | False | True | 裸属性/成员访问值引用（成员访问保 `X()` 形状、末尾 BC30545 抑制） |
| C3 | `1 + 2` / `"a" & "b"` / `True` | False | True | 求值表达式 |
| C4 | `?1` | True（`:2642`） | True | 不变（回归） |
| C5 | `Dim i As Integer` | False（`:2657`） | False | 声明不产生结果 |
| C6 | `System.Console.WriteLine()` | False（`:2658`） | False | Void 调用不打印 |
| C7 | `?System.Console.WriteLine()` | True（`:2659`） | True | 显式 `?` 恒 True（即使 Void） |
| C8 | `Sub Goo() End Sub` | False（`:2654`） | False | 声明 |
| C9 | `Imports System` | False（`:2656`） | False | 指令 |
| C10 | `System.Console.ReadLine()` | True（`:2660`） | True | 非 Void 调用现状已打印，不变 |
| C11 | `x = 5` | — | False | 赋值不改义（新增断言） |
| C12 | `Return 42` | — | True | Return 非 Void（回归，`InitializerRewriter` 路径） |

## 6. 既有测试更新清单

| 文件:行号 | 现状 | 更新 | 原因 |
|---|---|---|---|
| `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb:2644-2646` | `Assert.False(CreateSubmission("1"...).HasSubmissionResult())` + TODO「? should be optional」（:2645） | **翻转为 `Assert.True`**，删 TODO 注释 | 实现后裸 `1` 是末尾表达式 → 打印 |
| `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` | 现有 REPL/脚本测试（:93-285） | 新增用例（§7.1/§7.2），**不修改**既有退出码断言（:239-285 行为不变，§6 正交） | 绑定层改动不碰 Return/PrintStatement 语义 |
| `.vbx` 依赖「裸表达式报错」的外部断言 | — | 更新为「不报错、不设退出码」（§9 迁移影响） | `.vbx` 错误面变化（已接受代价） |

## 7. L4 REPL 与脚本测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

### 7.1 REPL 交互（`CreateRunner(input:=...)` + `TestConsoleIO`，纯内存）

> 全输出断言参考 `TestPrint`（`CommandLineRunnerTests.vb:93-103`）：`AssertEqualToleratingWhitespaceDifferences(s_logoAndHelpPrompt + "..." + runner.Console.Out.ToString())`，含 `>` 提示符与 `«Red»`/`«Gray»` 颜色标记。

| # | 用例 | 输入 | 断言 |
|---|---|---|---|
| R1 | 裸属性打印 | `Now` | 输出含日期值行，无 `«Red»` 错误块 |
| R2 | 裸变量打印 | 多行：`Dim before = Now` 再 `before` | `before` 输出日期值（`BoundLocal` → 打印） |
| R3 | 裸算术打印 | `1 + 2` | 输出 `3` |
| R4 | 裸比较打印 | `x = 1` 定义后 `x > 5`；及 `? x = 5` | 输出 `False`；`? x = 5` 也输出 `False`（比较） |
| R5 | 字符串连接 | `"a" & "b"` | 输出 `"ab"`（ObjectFormatter 带引号） |
| R6 | 赋值不打印 | `x = 5` | 无输出（仅 `>` 提示符） |
| R21 | `Mid`/`ReDim`/`With` 不改义 | `Mid(s, 1, 2) = "ab"`（先定义 s）；`ReDim a(2)`（先定义 a）；`With obj` 块 | 均不打印、合法（overview §4「`ReDim`、`With` 等不改义」；`Mid` 由 `Parser.vb:1109-1116` 消歧保住） |
| R7 | 副作用调用不打印 | `Console.WriteLine("hi")` | 无值输出（WriteLine 自身副作用照常） |
| R8 | 无括号 Sub 不打印 | 多行定义 `Sub MySub()` 后 `MySub` | `MySub` 不打印（方法组 → 调用语句） |
| R9 | 显式 `?` 回归 | `? Now` | 输出与现状一致 |
| R10 | `? x = 5` 比较 | `? x = 5` | 输出 `False`（`=` 比较） |
| R11 | 畸形输入仍报错 | `Now +` | 仍报语法错误（BC3xxxx「缺失表达式」类），不打印不吞错 |
| R12 | 非末尾裸表达式 | `1 + 2 : x = 5` | 报 BC31003，无打印 |
| R13 | 非末尾成员访问 | `DateTime.Now : x = 5` | 报 BC30545（不抑制） |
| R14 | 多行续行末尾 | `1 + 2 _` + 续行（整段提交） | 打印 3（与单行一致） |
| R15 | 晚绑定不打印 | `Dim o As Object = New X() : o.Prop` | 无输出（`LateMemberAccess` 保持调用语义） |
| R16 | `? (` 分发 | `? (1 + 2)` | 打印 3（`? (` 走 PrintStatement，P-009） |
| R17 | 交互状态保持 | `before = 1` 后再敲 `before`（同会话连续提交） | 打印 1（`ContinueWith` 独立树，`IsFinalStatementOfSubmission` 天然正确） |
| R20 | 复合赋值不改义 | `x += 1` / `x &= "s"` | 不打印（`IsAssignmentStatementOperatorToken` 截获，overview §4「合法语句不改义」） |
| R18 | 成员访问属性打印 | `DateTime.Now` | 输出日期值行，无 `«Red»` 错误块（保持 `X()` 形状 → 末尾 BC30545 抑制 → 打印；对应 F2 §7.1 #11 `TestBareQualifiedPropertyPrints`） |
| R19 | 双变量赋值 vs 比较 | 两个**相互独立**的测试场景：① 定义 `a = 1 : b = 2` 后提交 `a = b`；② 定义 `a = 1 : b = 2` 后提交 `? a = b`（或 `?a=b` 无空格，同 token 流、同义） | ① `a = b` 赋值不打印（b 覆盖 a，值变 2）；② `? a = b` 比较打印 **False**（`=` 在 `?` 表达式上下文是二元比较，`ParseExpression.vb:134`；a=1、b=2 未变）——两场景各自独立断言，避免同会话顺序提交使 ② 得 True |

### 7.2 脚本模式 `.vbx`（`CreateIsolatedTempDirectory` 既有装置 + 退出码断言）

| # | 用例 | 文件内容 | 断言 |
|---|---|---|---|
| V1 | 裸表达式静默 no-op | `1 + 2` | 退出码 0、无输出 |
| V2 | 裸属性静默 no-op | `Now` | 退出码 0、无输出 |
| V3 | 显式 `?` 不设退出码（回归） | `? 21` | 退出码 0、无输出（`:239-248` 不变） |
| V4 | `Return` 设退出码（回归） | `Return 21` | 退出码 21（`:264-273` 不变） |
| V5 | 裸 `Return` 退出码 0（回归） | `Return` | 退出码 0（`:276-285` 不变） |
| V6 | 嵌套 Sub 内裸表达式仍报错 | `Sub S() : 1 + 2 : End Sub` | 报原错误（`IsTopLevelScript` 的 `BlockKind` 门控排除嵌套方法） |
| V7 | 末尾 `? New Guid()` 不设退出码（回归） | `? New System.Guid()` | 退出码 0（`:251-261` 不变） |
| V8 | 顶层 Await 回归 | `Await Task.FromResult(13)` | 退出码 0（`:321-331` 不变） |

## 8. 无副作用纪律

- **REPL 用例**：`CreateRunner(input:=...)` + `TestConsoleIO`（`CommandLineRunnerTests.vb:66-91`）——内存 `StringReader`/`StringWriter`，无网络、无进程、无注册表、无文件写（`TestPrint` 等既有用例同款）。
- **脚本用例**：`.vbx` 文件写仅走既有 `CreateIsolatedTempDirectory` 装置（`:41-45`），系统临时目录自清理。
- **L1/L2/L3**：纯编译 API / `Parse` + 树形断言，不跑宿主、不 emit 到磁盘（`CompileAndVerify` 在 `CompilerTestHarness.vb:215-223` 已注释为内存 `MemoryStream` 门控，不加载动态程序集）。
- **新增用例若写不了**：按项目约定，不写就测不了的部分在实现期向用户说明并询问（不静默跳过）。
- 不以 `DateTime.Now` 精确值断言（时间漂移）；断言「输出为日期值行」而非具体时刻（R1/R2 用正则或非空行断言）。

## 9. 迁移与回归影响

- **`.vbx` 错误面变化**：依赖「裸表达式报错」的既有 `.vbx` 脚本/CI 断言更新为「不报错、不设退出码」（§6 更新清单）。
- **语义模型树形状变化**：`1 + 2`/`x > 5`/`before`/`Now` 变 `ExpressionStatement(裸表达式)`；`DateTime.Now`/`obj.MySub` 保留 `X()` 调用形状——L1 断言须分别验证两种形状（P5 vs P7）。
- **`Regular` 零影响**：L1 P1/P6 的 Regular 对照（`TestOptions.Regular`）：`1 + 2` 仍 BC30801、`? 1` 仍 BC31003、`Now`（Sub 内）仍 BC30545、`MySub`（Sub 内）仍合法——证明 `IsTopLevelScript`/`IsScriptInitializer` 双门控不污染 Regular 路径。

## 10. 与 F2 §7 矩阵的关系

本计划是 F2 §7（19 条用例）的**分层扩展**：

| F2 §7 | 本计划扩展 |
|---|---|
| §7.1 11 条 REPL | 拆为 L4 R1-R21，补 `? x = 5`（R4/R10）、`? (`（R16）、晚绑定（R15）、多行续行（R14）、交互状态保持（R17）、双变量赋值 vs 比较（R19）、复合赋值（R20）、`Mid`/`ReDim`/`With` 不改义（R21）；#11 `DateTime.Now` → R18 |
| §7.2 4 条脚本 | 补 V6 嵌套方法仍报错、V8 Await 回归 |
| §7.3 4 条 Regular | 并入 §9「Regular 零影响」对照（`1 + 2` 仍 BC30801、`? 1` 仍 BC31003、`Now`（Sub 内）仍 BC30545、`MySub`（Sub 内）仍合法） |
| —（新增） | L2 语义层 S1-S12（含 `CreateSubmission` 链与 BoundKind 回归断言）、L3 `HasSubmissionResult` 矩阵 C1-C12、§6 既有测试更新清单 |

> 测试命名建议：沿用 F2 §7.1 的 `TestBareXxx` 风格（`TestBarePropertyAccessPrints`、`TestBareVariablePrints`、`TestBareArithmeticExpressionPrints`…）；语义层沿用 C# `ArithmeticOperators_MultiplicationExpression` 的 `<Features>_<Mode>` 命名风格（如 `BareExpression_InteractiveNoDiagnostics` / `BareExpression_RegularLabelError`）。

## 11. 实现期发现与修订（2026-08-11，测试全绿后记录）

> 实现（F9-F12）按本计划执行后，三类根因经修复轮解决，此处记录与计划的差异与依据，供回归与后续维护参考。

### 11.1 解析层两处分发点（F9 缺口，已修）

顶层脚本数值字面量经 `ParseDeclarationStatementInternal` 分发（`Parser.vb:773-778` 的 `IntegerLiteralToken` 分支），**不是** `ParseStatementInMethodBodyCore`（:1104-1111）。F9 最初只改了方法体处，P1/P12/S4a/S8/C1/C3/R3/R12/R14/V1 共 9 例失败，实现期在 :773 补 `IsTopLevelScript` 分支后全部转绿。**本计划 P1/P12 的预期不变**，仅实现落点需记住两处。

### 11.2 方法组回归（HasSubmissionResult 方法组感知，已修）

裸 `MySub` 在 REPL 打印 `Nothing` 而非无输出。根因：`HasSubmissionResult` 用 `GetTypeInfo(方法组)` 返回包含类型（脚本类 `Script`，非 Void）→ True；F10 绑定层重分类（BoundCall Void）对该路径不可见。修正：`HasSubmissionResult` ExpressionStatement 分支改为「最高 bound 节点为 `BoundCall` → 按 `Method.ReturnType` 判定（Sub→False / Function→True），否则回退 `GetTypeInfo`」（`VisualBasicCompilation.vb:846-861`）。这使本计划 S5/R8 的「Sub 不打印 / Function 打印」断言成立。**偏离 design §1/§5 零改动清单**，已在设计文档标注。

### 11.3 晚绑定行为与计划预期不符（既有行为，测试如实记录）

`Dim o As Object = ... : o.Prop`（Object 接收者）作脚本末尾语句报 **BC30491**（`ERR_VoidValue`），而非计划 §4 的「无诊断、不打印」。实证为**既有行为**（F9/F10 前同样如此；Regular `Sub` 内 `o.Prop` 仅 BC42104 警告），根因是脚本结果回传路径把带 Call access kind 的 `BoundLateInvocation` 当值回传。S10 改名为 `LateBoundMemberAccess_ReportsVoidValue`（断言 BC30491）、R15 断言 `«Red»` 错误块 + BC30491 + 无值打印，均附注释。

### 11.4 输入层偏离（断言意图不变，代码注释已标）

| 位置 | 计划输入 | 实际输入 | 原因 |
|---|---|---|---|
| S2 | 多行提交链 `Dim before = Now` → `before` | 单提交 `Dim before = Now : before` | 单提交内 `before` 才是 BoundLocal（跨提交为字段）；语义相同 |
| S4b/R6/R17/R19/R20 | 裸 `x` / `x = 5` / `before = 1` / `a = 1 : b = 2` / `x += 1` | 先 `Dim` 声明再使用 | 交互 OptionExplicit 默认 On，未声明变量 BC30451 |
| S6/S9 | `Console.WriteLine`/`DateTime.Now` | 加 `ScriptCompilationOptions()` 全局导入 | `CreateSubmission` 无默认导入 |
| S10/R15 | `New X()`（X 未定义） | `New Object()`/`New StringBuilder()` | X 未定义必 BC30002；改真实类型覆盖「Object 接收者」意图 |
| S12 | `a?.Prop` | `Dim a As New StringBuilder()` 后 `a?.Length` | `a?.Prop` 无类型可绑定 |
| R7 | 期望输出含 `hi` | 期望输出不含 `hi` | WriteLine 副作用走真实控制台，不进 TestConsoleIO |
| R11 | `Now +` | `Now x` | `Now +` 现为不完整提交（REPL 等待续行不报错）；`Now x` 触发 BC30800 覆盖「畸形输入仍报错」意图 |
| R14 | 宽松 Contains("3") | 精确行断言 | 宽松断言可能假阳性 |

### 11.5 既有测试翻转完成

`CompilationAPITests.vb:2644` `Assert.False(CreateSubmission("1"...))` → **`Assert.True`**，:2645 TODO「? should be optional」已删除；C4-C10 既有断言未动。V3-V5/V7/V8 既有退出码用例未重复创建（回归通过）。
