# 任务：REPL 表达式开头 `?` 可选（optional-question-prefix）设计任务

本文件夹是 `proposal-optional-question-prefix` 的设计任务存储（Vortex 代办列表 + 设计产物）。

- **依据链**：`../..\proposals\proposal-optional-question-prefix.md`（提案，Active）→ `../..\meetings\meeting-optional-question-prefix.md`（LDM 会议，RESOLUTION #1-#5，**Active**）→ `../..\compilers-index.md`（编译器索引）。
- **交付物**：概要设计、详细设计、spec（`../..\spec\`）。三份都要求无副作用测试矩阵。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`，验证通过） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`，验证通过） |
| F3 | spec 文档 | 见下「F3 验收条件」 | **done**（`../../spec/spec-optional-question-prefix.md` + 提案头部更新，验证通过） |
| F4 | 设计修正（BoundKind 触发 / Option Strict 无分叉 / `? x = 5` 定案） | 四份交付物交叉一致、无残留、不误伤 | **done**（验证通过） |
| F5 | 设计修正（**裸变量打印纳入本版本** / 清理拆版措辞） | 四份交付物交叉一致——行为对照表、判定原则、测试矩阵、spec 能力规范四处「裸变量打印」口径一致；无残留拆版措辞（「不在首版行为表/follow-up」已删，fallback 措辞改为「备用，主方案不采用」） | **done**（验证通过，流水账 `9-implementer-f5-bare-variable.md`、`10-verifier-f5-bare-variable.md`） |
| F6 | 一致性审计 + 修复 + 复验 | F4/F5 修正后四份交付物内部一致、无残留（`? (` 分发、判定原则「其余→打印」、§2.3/§3.5 机制、行号、README 决策、拆版措辞 6 项全修净），复验通过 | **done**（审计打回→修复→复验通过，流水账 `11-verifier-f6-consistency.md`、`12-implementer-f6-fix.md`、`13-verifier-f6-recheck.md`） |
| F7 | 测试计划（参考 C# 四层测试强度 + VB 语法特性综合） | 四层矩阵（L1 解析树形 / L2 语义诊断 / L3 `HasSubmissionResult` / L4 REPL 全输出）+ VB 特性维度（标签/比较/`=`/字符串 `&`/方法组/成员访问/晚绑定/续行/`:` 分隔/Option Strict/.vbx 代价）+ 既有测试翻转清单（`CompilationAPITests.vb:2644` 翻 True 删 TODO） | **done**（`test-plan.md`；审计 2 项 → 修复 → 复验；后续按用户追问补 VB 特殊边界：复合赋值 P13、`Mid` 赋值 P14、`? .`/`? !` 分发 P15、双变量赋值 vs 比较 R19、复合赋值不改义 R20、`Mid`/`ReDim`/`With` 不改义 R21——复验一轮打回 P13 复合赋值 kind 归属 → 修复 → 复验 PASS） |
| F8 | 验证 test-plan.md | ①四层锚点真实 ②行为对照一致 ③既有测试翻转核实 ④无副作用纪律 ⑤错误码一致 ⑥无遗漏（审计：缺 `DateTime.Now` REPL 打印用例 → 修复） | **done**（验证者 → 修复 → 复验 PASS） |

> 全流程 2026-08-10/11 经 Vortex 涡流完成：F1→F2→F3→F4→F5→F6 每组实施者+验证者串行交替，全部通过；F7/F8（测试计划）2026-08-11 同样串行交替通过。流水账见 `<项目根>/tmp/vortex-logs/1..13-*`；失败点缓存 `pitfalls.md`（P-001~P-011）。
> F4（核心设计决策修正）：触发判定由「错误码/诊断族枚举」改为「**绑定结果形态（`BoundKind`）**」；Option Strict 定案**无分叉**；`? 变量名 = 表达式` 定案**非开放问题**（`?` 使 `=` 为比较）。流水账 `7-implementer-f4-design-update.md`、`8-verifier-f4-design-update.md`。
> F5（核心范围修正）：**裸变量打印纳入本版本**——`Dim before = Now` 后敲 `before` 自动打印。裸标识符解析为**裸表达式**（不包成 `X()`），绑定层按绑定结果形态消歧（方法组 → 调用语句不打印；值引用 → RValue 打印）。原 F2 §8 误标 follow-up，已升级为本版本行为。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-10）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。

### 判定原则（采纳 C 方案，meeting RESOLUTION #1/#2）

按**绑定结果形态（`BoundKind`）**判定触发：值引用（`Local` / `Field` / `Parameter` / `PropertyAccess` / `LateInvocation`+PropertyGroup）→ 打印；方法组/真正调用（`MethodGroup` → 无参调用重分类 / `Call` / `LateMemberAccess`）→ 保持合法语句不打印。**不枚举错误码**。本身合法的语句（赋值、带副作用调用、无括号 Sub 调用）**不改义**。显式 `?` 保留兼容。**裸变量打印纳入本版本**：`Dim before = Now` 后敲 `before` 自动打印（关键设计决策 7）。

### 现状机制（`Compilers\VisualBasic\Portable\`）

- **解析层**：
  - `?` 分发：`Parser.vb:1233`——后随 `.`/`!` → `ParseAssignmentOrInvocationStatement`；否则（含 `(`）→ `ParsePrintStatement`（`ParseStatement.vb:1858`）。
  - 裸标识符 `Now`：`ParseAssignmentOrInvocationStatement`（`ParseStatement.vb:1087`）无赋值符 → `MakeInvocationExpression`（定义 `:1116`；无参调用创建 `:1153`）包成 `Now()`（无参调用）。（**现状源码**；设计修正见关键设计决策 7：裸标识符改裸表达式，绑定层方法组消歧。）
  - 裸数值 `1 + 2` 语句首：`Parser.vb:1092` `IntegerLiteralToken` + `IsFirstStatementOnLine` → `ParseLabel`（`ParseStatement.vb:1573`），无冒号 → `ERR_ObsoleteLineNumbersAreLabels`（`:1579`）。
  - `x > 5`：identifier 语句首 → `ParseAssignmentOrInvocationStatement` 只 `ParseTerm` 取 `x` → `MakeInvocationExpression` 误包成 `x()`，`> 5` 残留 → 解析层错误。
  - `IsScript` 门控已存在：`Parser.vb:79-83`（`SourceCodeKind.Script`）。
- **绑定层**：
  - `BindExpressionStatement`（`Binder_Statements.vb:2608`）：Invocation/ConditionalAccess → `BindInvocationExpressionAsStatement`（`:2715`）→ `ReclassifyInvocationExpressionAsStatement`（`:2719`）；PropertyAccess → `MakeRValue` + `ERR_PropertyAccessIgnored` = **BC30545**（`Errors.vb:424`）；LateInvocation 里 PropertyGroup 也报 BC30545（`:2742`）；其余 → `BindRValue` 无诊断（`:2624`）。
  - `BindPrintStatement`（`:2704`）= `BindRValue`，无「值被丢弃」诊断——与裸表达式同构。
- **HasSubmissionResult**（`VisualBasicCompilation.vb:816-868`）：末尾 PrintStatement → 恒 True；ExpressionStatement/CallStatement → 非 Void 即 True；ReturnStatement → 有表达式且非 Void。已用 `root.Members.LastOrDefault()`（`:833`）判末尾。
- **`SourceCodeKind.Script` 同时服务 `.vbx` 与交互提交**：`VisualBasicCompiler.vb:96` `scriptParseOptions = parseOptions.WithKind(SourceCodeKind.Script)`。`SourceCodeKind.Interactive` 已 Obsolete。

### 宿主层（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）

- 交互循环 `RunInteractiveLoopAsync`（`:224`）→ `BuildAndRunAsync`（`:296`）：`Compile` → `HasAnyErrors()` 短路（`:300`）→ `RunAsync` → `HasReturnValue()`（`newScript.HasReturnValue()`，`:313`）为真 → `globals.Print(state.ReturnValue)`（`:315`）。**宿主零 `?` 重试逻辑**。
- `.vbx` 脚本 `RunScriptAsync`（`:201`）：`Script.CreateInitialScript<int>` → 直接返回退出码，**不打印**。末尾表达式不设退出码（测试：`CommandLineRunnerTests.vb:239-285`）。

### C# 先例（跟随设定）

- C# `ExpressionStatementSyntax.AllowsAnyExpression`（`Compilers\CSharp\Portable\Syntax\ExpressionStatementSyntax.cs:19`）= **缺分号**（交互式末尾表达式）；binder `BindExpressionStatement`（`Binder_Statements.cs:649`）在 `allowsAnyExpression` 时跳过 `IsValidStatementExpression` 检查（`:655`）。即：**编译器层允许末尾表达式产生结果，宿主只读返回值打印**。VB 跟随同一设定，但 VB 无分号、靠「**绑定结果形态（`BoundKind`）判定 + 裸表达式语句解析**」。

## 关键设计决策（源自 meeting RESOLUTION #4，设计文档必须吸收）

1. **实现落点：编译器层**，宿主层零改动。`HasSubmissionResult` + `globals.Print` 现有路径自动生效。
2. **绑定层**：Script kind 提交的**末尾表达式语句**绑定为 RValue；**按绑定结果形态（`BoundKind`）判定触发**——值引用（`Local`/`Field`/`Parameter`/`PropertyAccess`/`LateInvocation`+PropertyGroup）→ 打印；方法组/真正调用（`MethodGroup` → 无参调用重分类 / `Call` / `LateMemberAccess`）→ 保持语句不打印。**不枚举错误码**。只**末尾**产生结果（复用 `root.Members.LastOrDefault()` 判定）。
3. **解析层**：Script kind 语句首裸表达式（`1 + 2` 撞标签、`x > 5` 被误包）解析为表达式语句；区分「裸数值+冒号 = 标签」与「裸表达式 = 表达式语句」；保住 `MySub`（无括号 Sub 调用是合法语句，不得自动打印）。
4. **代价（已接受）**：`.vbx` 里裸表达式从「报错」变「静默 no-op（不设退出码）」，随 C# csx 先例（LDM-2020-04-15「we are ok with this remaining distance」）。
5. **与退出码正交**：自动打印仅交互式 REPL；脚本模式（`RunScript`）丢弃返回值不设退出码。
6. 宿主层「重试为 `? <raw>`」**降级为 fallback 备用，主方案不采用**。
7. **裸变量打印纳入本版本**：`Dim before = Now` 后敲 `before` **自动打印**（对齐 C# `csi` 敲变量名即打印）。裸标识符解析为**裸表达式**（`IdentifierName`，不包成 `X()`），绑定层按绑定结果形态消歧——`Local`/`Field`/`Parameter`/`PropertyAccess` 值引用 → 打印；方法组（`MySub`/`MyFunc`）→ 无参调用重分类为调用语句（Sub 不打印、Function 现状打印）。原 F2 设计误标 follow-up，已升级为本版本行为。
8. **Option Strict 无分叉（已定案）**：自动打印路径对 On/Off 一视同仁，不存在宽松模式分叉。理由：① 打印路径 `globals.Print`（`CommandLineRunner.cs:315`）不经 CType/隐式转换，`HasSubmissionResult` 只判「非 Void」；② 裸表达式作语句时**没有目标类型上下文**，宽松模式的隐式转换无处发生；③ 值引用路径与 `ReclassifyInvocationExpressionAsStatement`（`Binder_Statements.vb:2719-2755`）均无 `OptionStrict` 门控，`Now` On/Off 都求值为值 → 都打印；晚绑定 `obj.Prop` 绑定为调用 → 无诊断 → 不打印，天然正确。
9. **`? x = 5` 非开放问题（已定案）**：`?` 前缀把整句送进表达式上下文（`ParsePrintStatement` → `ParseExpressionCore`），`? x = 5` 中 `=` 是二元比较运算符（`ParseExpression.vb:134`），打印比较结果 False；想赋值写 `x = 5`（语句首赋值分支 `ParseStatement.vb:1096` 截获），解析层即分开，无共存冲突。

## F1 验收条件（概要设计 pass 标准）

- 覆盖三层落点（解析层 / 绑定层 / 宿主层）与判定原则（绑定结果形态为**值引用**才打印，合法语句不改义，不枚举错误码）。
- 含行为对照表（`? Now` / `Now` / `before`（裸变量）/ `1 + 2` / `x > 5` / `x = 5` / `Console.WriteLine("hi")` / `MySub`）。
- 说明「仅末尾表达式产生结果」与 `HasSubmissionResult` 复用。
- 说明 `.vbx` 代价与退出码正交（脚本模式不启用自动打印）。
- 说明 C# `AllowsAnyExpression` 先例与 VB 跟随设定的差异（无分号）。
- 说明触发判定为**绑定结果形态（`BoundKind`）**（值引用 → 打印；方法组/调用 → 保持语句），**不枚举错误码**；解析层 `1 + 2`/`x > 5` 靠修误解析解决。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号区域 + 改动形状**（新函数/参数/分支），可被实施者直接照做。
- 解析层：区分标签 vs 表达式语句的具体 token 判定；`MySub` 消歧（无括号 Sub 调用不自动打印）的具体办法；`1 + 2` / `x > 5` 的具体解析分支。
- 绑定层：「值被丢弃」诊断抑制的精确落点（BC30545 绑定层、`ReclassifyInvocationExpressionAsStatement`）；**方法组消歧**（裸 `MySub`/`MyFunc` 按调用语句重分类 vs 值引用打印，见 §3.3）；「是否末尾语句」上下文如何线程（复用 `root.Members.LastOrDefault()` 判定）。
- 触发判定与排除清单（按 `BoundKind` 绑定结果形态分行，含 `Local`/`Field`/`Parameter` 值引用行与 `MethodGroup` 行；诊断码仅辅助说明不作判定依据；Option Strict 无分叉）。
- 无副作用测试矩阵（REPL 与脚本模式），含**裸变量用例**（`TestBareVariablePrints`）与 `Regular` 编译零影响证明（`SourceCodeKind` 门控）。
- 边界与迁移影响（`.vbx` 静默 no-op、Option Strict 无分叉）。

## F3 验收条件（spec pass 标准）

- 在 `../..\spec\` 登记「REPL 裸表达式自动打印」为 2.0 能力（与 spec/README 版本历史结构一致，不维护动态现状快照）。
- 更新 `proposal-optional-question-prefix.md` 头部的 `Specification` 进度（Not Started → 指向 spec 文档）。
- 与 proposal/meeting RESOLUTION 一致（Active、C 方案、编译器层落点、`.vbx` 代价、退出码正交）。
