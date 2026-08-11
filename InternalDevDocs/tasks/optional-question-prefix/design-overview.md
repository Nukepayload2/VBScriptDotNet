# 概要设计：REPL 表达式开头 ? 可选

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-optional-question-prefix.md`（Active）→ `../../meetings/meeting-optional-question-prefix.md`（Active，RESOLUTION #1-#5）。
> 本设计吸收会议 RESOLUTION #1-#5，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。

## 1. 背景与目标

**目标（一句话）**：REPL 交互模式下 `Now` 直接求值打印（对齐 C# `csi`），显式 `?` 前缀保留兼容，本身合法的语句（赋值、带副作用调用、无括号 Sub 调用）不改义，脚本模式（`.vbx`）不启用自动打印。

- **现状缺口**：`? Now` 打印；`Now` 报 **BC30545**（属性访问作语句、值被丢弃）。C# REPL 敲 `DateTime.Now` 即打印——VB REPL 与 C# REPL 交互体验差距最显眼。
- **状态**：提案与专用 LDM 会议均为 **Active**；会议采纳 PROPOSAL C（值被丢弃才自动打印），实现落点为**编译器层、跟随 C# REPL 设定**（RESOLUTION #1、#4）。

**RESOLUTION #1-#5 吸收映射**：

| RESOLUTION | 内容 | 落在本设计 |
|-----------|------|-----------|
| #1 | 维持 Active，采纳 C 方案方向 | 第 1、4 节 |
| #2 | 显式 `?` 保留、合法语句不改义 | 第 3、4 节 |
| #3 | 与退出码正交，脚本模式不启用 | 第 6 节 |
| #4 | 编译器层落点，宿主层零改动；`.vbx` 错误面变化随 csx 先例接受；宿主「重试为 `? <raw>`」降级 fallback | 第 2、5、6 节 |
| #5 | 触发形态回归确认（按 BoundKind 绑定结果形态，不枚举错误码） | 第 4、7 节 |

## 2. 总体架构（三层落点）

**核心观察**：成功的非 Void 表达式语句本就会打印——`Now` 之所以报错，纯粹是解析/绑定层的「值被丢弃」诊断在 `CommandLineRunner` 的 `HasAnyErrors()`（`CommandLineRunner.cs:300`）处短路，根本没走到打印。因此「让 `?` 可选」的实质是：把这些原本失败的交稿按「写了 `?`」的语义绑定为 RValue（`BindRValue`），走 `PrintStatement` 的既有打印路径。三层落点如下。

### 2.1 解析层（`Compilers\VisualBasic\Portable\Parser\`）

**现状（源码实证）**：

- `?` 分发在 `Parser.vb:1233`：后随 `.`/`!` → 成员访问/三元（`CanStartConsequenceExpression(qualified:=False)` 只认 `.`/`!`，`ParseExpression.vb:512`）；否则（含 `(`）→ `ParsePrintStatement`（`ParseStatement.vb:1858`）→ `PrintStatementSyntax`。
- 裸标识符 `Now`：`ParseAssignmentOrInvocationStatement`（`ParseStatement.vb:1087`）无赋值符 → `MakeInvocationExpression`（定义 `ParseStatement.vb:1116`；无参调用创建 `:1153`）把 `Now` 包成 `Now()`（无参调用形状）。
- 裸数值 `1 + 2` 语句首：`Parser.vb:1092` 对 `IntegerLiteralToken` 先判 `IsFirstStatementOnLine` → 走标签解析 `ParseLabel`（`ParseStatement.vb:1573`），无冒号 → `ERR_ObsoleteLineNumbersAreLabels`（`:1579`）。
- `x > 5`：identifier 语句首 → `ParseAssignmentOrInvocationStatement` 只 `ParseTerm` 取 `x` → `MakeInvocationExpression` 误包成 `x()`，`> 5` 残留 → 解析层错误。
- `IsScript` 门控已存在：`Parser.vb:79-83`（`SourceCodeKind.Script`）。

**设计落点**：

- Script kind 下，语句首裸表达式解析为**表达式语句**；需区分「裸数值+冒号 = 标签」（`1:` 是标签）与「裸表达式 = 表达式语句」（`1 + 2`）。
- **裸标识符（`before`/`Now`/`MySub`）解析为裸表达式**（`IdentifierName`，不包成 `X()`）——否则 `before` 变 `before()` →「不是方法」错误，到不了值形态判定；方法组 vs 变量/属性的消歧交给绑定层（第 4 节）。
- 保住 `MySub`：无括号 Sub 调用是合法语句，不得自动打印（见第 4 节判定原则）。
- 逐字句消歧细节（标签/`Call`/`Mid`、`?` 与三元 `? .`/`? !`）留给 F2。

### 2.2 绑定层（`Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb`）

**现状（源码实证）**：

- `BindExpressionStatement`（`:2608`）：Invocation/ConditionalAccess → `BindInvocationExpressionAsStatement`（`:2715`）→ `ReclassifyInvocationExpressionAsStatement`（`:2719`）；PropertyAccess → `MakeRValue` + `ERR_PropertyAccessIgnored` = **BC30545**（`Errors.vb:424`）；LateInvocation 里 PropertyGroup 也报 BC30545（`:2742`）；其余 → `BindRValue` 无诊断（`:2624`）。
- `BindPrintStatement`（`:2704`）= `BindRValue(expression)` → `BoundExpressionStatement`，**没有**「值被丢弃」类诊断——与裸表达式语句同构。

**设计落点**：

- Script kind 提交的**末尾表达式语句**绑定为 RValue；按**绑定结果形态（`BoundKind`）**判定触发——**值引用（Local/Field/Parameter/PropertyAccess）→ 打印**，**方法组/真正调用（MethodGroup → 无参调用重分类 / Call）→ 保持语句不打印**。绑定层抑制 `ERR_PropertyAccessIgnored`（BC30545）；解析层标签/误解析（`1 + 2`/`x > 5`）由解析层修复消除，不在绑定层抑制清单内。
- 只**末尾**产生结果：复用 `HasSubmissionResult` 的 `root.Members.LastOrDefault()`（`VisualBasicCompilation.vb:833`）判定「是否提交末尾语句」，binder 线程据此决定是否走 RValue 路径。
- 绑定为 RValue 后语义模型即「按 `?` 语义求值」，不再停留为 `Now()` 调用形状（避免影响 IntelliSense/错误恢复）。

### 2.3 宿主层（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）——零改动

**现状（源码实证）**：

- 交互循环 `RunInteractiveLoopAsync`（`:224`）→ `BuildAndRunAsync`（`:296`）：`Compile` → `HasAnyErrors()` 短路（`:300`）→ `RunAsync` → `HasReturnValue()`（`newScript.HasReturnValue()`，`:313`）为真 → `globals.Print(state.ReturnValue)`（`:315`）。**宿主零 `?` 重试逻辑**。
- `HasSubmissionResult`（`VisualBasicCompilation.vb:816-868`）：末尾 PrintStatement → 恒 True（即使 Void）；ExpressionStatement/CallStatement → 类型非 Void 即 True；ReturnStatement → 有表达式且非 Void。

**设计落点**：

- **宿主层零改动**。`HasSubmissionResult` + `globals.Print` 现有路径自动生效——末尾表达式语句绑定为 RValue（非 Void）后，`HasSubmissionResult` 返回 True，统一走 `globals.Print`。机制细化见 detailed §3.5：复用 ExpressionStatement 非 Void 分支，**不新建 PrintStatementSyntax**（自动打印只针对非 Void 值表达式，无需 PrintStatement「恒 True」分支）。
- 宿主层「重试为 `? <raw>`」**降级为 fallback 备用，主方案不采用**（仅当解析层裸表达式歧义风险过大、或未来需要让 `.vbx` 里的裸表达式仍报错时再启用）。

## 3. 行为对照表

| 提交 | 现状 | 提案后 |
|------|------|--------|
| `? Now` | 打印（PrintStatement → `BindRValue` 无诊断；`HasSubmissionResult` 恒 True） | **打印**（不变，显式 `?` 保留） |
| `Now` | `MakeInvocationExpression` 包成 `Now()` → 属性访问作语句 → **BC30545** 报错 | **自动打印** |
| `before`（先 `Dim before = Now` 或 `before = 1` 定义） | 裸标识符包成 `before()` → 「`before` 不是方法」错误 → 不打印 | **自动打印**（裸标识符 → 裸表达式 → 绑定为 `BoundLocal` 引用 → 非 Void → RValue → 打印） |
| `1 + 2` | 字面量语句首 → 标签/表达式误解析（`ERR_ObsoleteLineNumbersAreLabels`，`ParseStatement.vb:1579`） | **自动打印 3** |
| `x > 5` | 误包成 `x()`，`> 5` 残留 → 解析层错误（`ERR_ObsoleteArgumentsNeedParens`，`ParseStatement.vb:1151`） | **自动打印 False** |
| `x = 5` | 赋值，合法 | **不打印**（合法语句不改义） |
| `Console.WriteLine("hi")` | 带副作用调用，合法 | **不打印**（合法语句不改义） |
| `MySub` | 无括号 Sub 调用，合法（VB 传统） | **不打印**（合法语句，保住） |

## 4. 判定原则

- **自动打印的充要条件**：提交是「表达式作为语句 + 绑定结果为值（排除方法组/真正调用）」——值引用（变量/字段/属性）与求值表达式（如 `1 + 2`/`x > 5`）均打印。本身合法的语句（赋值、带副作用调用、无括号 Sub 调用、`ReDim`、`With` 等）**不改变行为**。行为变化严格限定在「原本就是错误」的提交上，不引入静默语义漂移。
- **触发判定 = 绑定结果形状（`BoundKind`），不枚举错误码**：绑定层按 `BoundKind` 区分——`Local`/`Field`/`Parameter`（变量/字段/参数引用）→ 值是值 → 自动打印；`PropertyAccess` → 值是属性访问 → 自动打印；`MethodGroup` → 方法组 → 无参调用重分类为调用语句（Sub 不打印、Function 现状打印）；`Call` → 真正调用 → 保持合法语句不打印；`LateMemberAccess` → 晚绑定成员访问（Object 接收者）→ 保持调用语义不打印；`LateInvocation` + PropertyGroup → 晚绑定属性 → 打印；**其余（非方法组求值表达式，如 `1 + 2`/`x > 5`）→ `Case Else` `BindRValue` 无诊断 → 打印**。`BoundKind` 与 `PrintStatement` 都是 VB 编译器既有结构，不造新概念。错误码不是稳定契约、长期维护面大，且解析层错误（标签/误包）与绑定层错误（BC30545）本质不同，不塞进同一张清单。解析层 `1 + 2`/`x > 5` 需修误解析（C# 无先例可抄，VB 特有）；绑定层只管「绑定结果形态」。

## 5. C# 先例与跟随设定

- **C# 机制**：`ExpressionStatementSyntax.AllowsAnyExpression`（`Compilers\CSharp\Portable\Syntax\ExpressionStatementSyntax.cs:19`）= **缺分号**（交互式末尾表达式）；binder `BindExpressionStatement`（`Compilers\CSharp\Portable\Binding\Binder_Statements.cs:649`）在 `allowsAnyExpression` 时跳过 `IsValidStatementExpression` 检查（`:655`）。即**编译器层允许末尾表达式产生结果，宿主只读返回值打印**（interactive 专属行为，LDM-2020-04-15）。
- **VB 跟随同一编译器层设定，但机制不同**：C# 的触发信号是**语法缺分号**（`AllowsAnyExpression`）与 `IsStatementExpression` 语法白名单；**VB 无分号**，靠「**绑定结果形态（`BoundKind`）判定 + 裸表达式语句解析**」作为等价信号。两者同构（编译器层允许末尾表达式产生结果、宿主层零改动），但触发条件不同——VB 是「绑定结果为值引用（变量/字段/属性）」的裸表达式。
- **不可盲目照搬 C#**：C# 的 `AllowsAnyExpression`=缺分号信号（VB 无分号）、`IsStatementExpression` 语法白名单（C# 非法表达式在解析层已是表达式语句，VB 不是）——两者位置不同，照搬错位。VB 用 `BoundKind` 判定触发，并复用 `?` 前缀路径（`ParsePrintStatement` → `BindRValue`），不引入 C# 的语法标志位。

## 6. 代价与边界

- **`.vbx` 代价（已接受）**：`SourceCodeKind.Script` 同时服务 `.vbx` 与交互提交（`VisualBasicCompiler.vb:96`，`scriptParseOptions = parseOptions.WithKind(SourceCodeKind.Script)`；`SourceCodeKind.Interactive` 已 Obsolete），无法区分 REPL 提交与 `.vbx` 脚本文件。`.vbx` 里的裸表达式从「报错」变「**静默合法 no-op（不设退出码）**」——随 C# 对 csx 的先例接受（LDM-2020-04-15「we are ok with this remaining distance」）。
- **与退出码正交**：自动打印仅交互式 REPL；脚本模式 `RunScriptAsync`（`CommandLineRunner.cs:201`）直接返回退出码、**不打印**。脚本文件末尾表达式不设退出码的已修复语义（测试：`Scripting\VisualBasicTest\CommandLineRunnerTests.vb:239-285`）不受影响。
- **Option Strict 无分叉（已定案）**：自动打印路径对 On/Off 一视同仁，不存在宽松模式分叉。理由：① 打印路径 `globals.Print(state.ReturnValue)`（`CommandLineRunner.cs:315`）直接打印绑定结果，不经任何 CType/隐式转换，`HasSubmissionResult` 只判「非 Void」；② 裸表达式作语句时**没有目标类型上下文**，宽松模式的隐式转换根本无处发生；③ 值引用路径（裸 `before`/`Now` → `MakeRValue`）与 `ReclassifyInvocationExpressionAsStatement`（`Binder_Statements.vb:2719-2755`，成员访问形态如 `DateTime.Now`）均无 `OptionStrict` 门控，`Now` On/Off 都求值为值 → 都打印，无分叉；晚绑定表达式（`obj.Prop`）绑定为调用 → 无诊断 → 不打印，天然正确。
- **仅末尾产生结果**：与 C# 一致，只有提交的**末尾**表达式产生结果；多行提交中间行的裸表达式仍报错。

## 7. 决策记录（原未决问题，全部已定案）

本节原为「未决问题/风险」，经逐条推敲后**无真正未决项**，全部定案如下：

- **触发判定（已定案）**：绑定结果形态（`BoundKind`），不枚举错误码（详见 §4）。
- **Option Strict（已定案无分叉）**：On/Off 行为一致，无排除清单（详见 §6）。
- **自动打印作用域（已定案，跟随 C#）**：**仅交互式 REPL**。C# 由宿主入口区分（interactive 打印、脚本丢弃）；VB 跟随——交互走 `RunInteractiveLoopAsync` 打印，脚本/`@vbi.rsp`/stdin 走 `RunScriptAsync` 丢弃。不引入额外开关。
- **`.vbx` 区分开关（不引入，与任务无关）**：`.vbx` 裸表达式变静默 no-op 随 csx 先例接受；「未来恢复脚本侧报错」不在本任务范围，不设计开关。
- **打印格式（已定案：唯一方案是复用）**：自动打印走 `?` 同一条路径（`BindRValue → HasSubmissionResult → globals.Print`），复用 `ObjectFormatter`/`PrintOptions`。不存在第二套格式的备选方案。
- **`?` 带变量名（已定案非开放问题）**：`? x = 5` 中 `=` 是比较运算符（`ParseExpression.vb:134`）；赋值写 `x = 5`（语句首赋值分支 `ParseStatement.vb:1096`）。解析层即分开，无共存冲突。
- **多行续行（已定案，见 F2 §8）**：与单行一致——基于整段提交树判末尾，整段以表达式语句收尾且绑定结果形态为值引用则打印。
- **裸变量打印（已定案，纳入本版本）**：`Dim before = Now` 后敲 `before` **自动打印**（对齐 C# `csi` 敲变量名即打印）。裸标识符解析为**裸表达式**（`IdentifierName`，不包成 `X()`），绑定层按绑定结果形态消歧——`Local`/`Field`/`Parameter`/`PropertyAccess` 值引用 → 打印；方法组（`MySub`/`MyFunc`）→ 无参调用重分类为调用语句（Sub 不打印、Function 现状打印）。原 F2 设计误标为 follow-up，已升级为本版本行为。

> 记录：本节经多次迭代（F1 挂起 → F4 定案 → F5 裸变量纳入 → 本次清理），最终**无未决项**。
