# Visual Basic Language Design Meeting
August 9, 2026

本次会议是 `proposal-optional-question-prefix` 的专用 1:1 会议。先导会议 `meeting-vb-repl-parity-with-csharp-repl.md`（2026-08-09）已把它与 avalonia-ise-repl-ui 一起盘点并三态判定为 **Active**；本次不重复产品盘点和 csharplang 全景，而是回到源码把「`?` 可选」的机制核实清楚，把「实现落点」「诊断族枚举」「边界」三个问题谈深，细化实现指引，维持 Active 判定不变。

## Agenda

* [Proposal: REPL 表达式开头问号可选](#proposal-repl-表达式开头问号可选)

## Proposal: REPL 表达式开头问号可选

_Related: `../proposals/proposal-optional-question-prefix.md`；先导 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`；C# 对照：`csi` 敲表达式即打印（LDM-2020-04-15 interactive 设置）；`../proposals/vbscript-1.2/`（REPL 能力版本归档）_

### 场景与缺口

`? Now` 打印值；`Now` 报 **BC30545**（属性访问必须分配给属性或使用属性值）。对交互求值场景这是噪音错误。C# REPL（`csi`）直接输入 `DateTime.Now` 即求值打印。这是 VB REPL 与 C# REPL 最显眼的交互体验差距（先导会议焦点之一）。

### 现状机制（源码核实）——本会议新增价值所在

本次会议不重复先导会议的方案盘点，而是回到源码，把「`?` 可选的机制地基」逐行核实清楚。按「解析层 → 绑定层 → 打印路径」三节陈述，每条标注 `文件:行号`。已核实事实如下，直接作为提案 Detailed design 的源码实证。

#### 解析层（`Compilers\VisualBasic\Portable\Parser\`）

- 语句起始 `?`（QuestionToken）的分发：`Parser.vb:1233`——若其后是 `.` 或 `!`（`CanStartConsequenceExpression`，`ParseExpression.vb:512` 只认 Dot/Exclamation/OpenParen）→ 走三元/成员访问；否则 → `ParsePrintStatement`（`ParseStatement.vb:1858`）→ `PrintStatementSyntax`。Regular（非交互）源码里 PrintStatement 报 `ERR_UnexpectedExpressionStatement`（BC31003）。
- 裸标识符 `Now` 走 `ParseAssignmentOrInvocationStatement`（`ParseStatement.vb:1087`）：无赋值符 → `MakeInvocationExpression(Now)` 把 `Now` 包成 `Now()`（无参调用，`ParseStatement.vb:1153`）。
- 数字字面量 `1 + 2` 在语句首：`Parser.vb:1092` 对 `IntegerLiteralToken` 先判 `IsFirstStatementOnLine` → 会走标签解析（ParseLabel），`1` 被当标签；或落入一般表达式误解析。**因此「值被丢弃」类失败的诊断码不统一、跨解析层与绑定层，需实现时实证枚举。**

#### 绑定层（`Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb`）

- `BindExpressionStatement`（`:2608`）：Invocation/ConditionalAccess → `BindInvocationExpressionAsStatement` → `ReclassifyInvocationExpressionAsStatement`（`:2719`）——若绑定的其实是属性访问 → 报 `ERR_PropertyAccessIgnored` = **BC30545**（`Compilers\VisualBasic\Portable\Errors\Errors.vb:424`，消息「Property access must assign to the property or use its value」）；Await → 按语句绑定；其余 → `BindRValue`（无诊断）。
- `BindPrintStatement`（`:2704`）：`BindRValue(expression)` → `BoundExpressionStatement`——与裸表达式语句同构，但**没有**「值被丢弃」类诊断。

#### 打印路径（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` + `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb`）

- REPL 交互循环 `RunInteractiveLoopAsync`（`CommandLineRunner.cs:224`）→ `BuildAndRunAsync`（`:296`）：`Compile` → `diagnostics.HasAnyErrors()` 短路 → 运行 → `HasReturnValue()` 为真 → `globals.Print(state.ReturnValue)`（`:313-315`）。
- `HasSubmissionResult`（`VisualBasicCompilation.vb:816`）：末尾语句为 PrintStatement → **恒 True**（即使 Void）；为 ExpressionStatement/CallStatement/ReturnStatement → 类型非 Void 即 True。
- **关键观察**：成功的非 Void 表达式语句**本就会**被打印——`Now` 之所以报错，纯粹是解析/绑定层的「值被丢弃」诊断在 `HasAnyErrors()` 处短路，根本没走到打印。所以「让 `?` 可选」的实质是：把这些失败按「写了 `?`」的语义绑定为 RValue（`BindRValue`），走 PrintStatement 的既有打印路径。

#### 行为对照表（现状，作为提案表格的源码实证）

| 提交 | 解析 | 绑定/诊断 | 结果 |
|------|------|-----------|------|
| `? Now` | PrintStatement | BindRValue 无诊断；HasSubmissionResult 恒 True | 打印 |
| `Now` | 包成 `Now()` | 属性访问作语句 → **BC30545** | 报错 |
| `1 + 2` | 字面量语句首 → 标签/表达式误解析 | 解析层错误（码待实证） | 报错 |
| `x = 5` | 赋值 | 合法 | 不打印 |
| `Console.WriteLine("hi")` | 带副作用调用 | 合法 | 不打印 |
| `x > 5` | 比较表达式 → 误解析 | 解析层错误（码待实证） | 报错 |

### 候选方案

**PROPOSAL A — 全量自动打印。** 凡表达式语句一律按 `?` 语义打印。最对齐 csi，但带副作用调用也被打印、静默行为面最大。**否决。**

**PROPOSAL B — 仅优化报错文案。** 保留强制 `?`，把 BC30545 换成友好提示。零语义风险，解决不了直觉落差。**否决。**

**PROPOSAL C — 值被丢弃才自动打印（采用，提案方案）。** 仅当提交是「表达式作为语句 + 值被丢弃类诊断」时，按 `?` 语义求值打印；本身合法的语句（赋值、带副作用调用）行为不变。

**PROPOSAL D — 仅抑制 BC30545 类绑定层诊断（本会议新增）。** 对交互式提交末尾表达式抑制「值被丢弃」错误，放行到 HasSubmissionResult 打印。只覆盖绑定层（BC30545），覆盖不了解析层（标签误解析）；且语义模型里树仍是 `Now()` 调用形状（影响 IntelliSense/错误恢复）。作为编译器层方案的绑定层部分，单独不足——覆盖不了解析层（标签误解析），且语义模型里树仍是 `Now()` 调用形状。

### 权衡：Q&A

- **对齐 csi vs 方言最小差异**（先导会议已谈，简述）：csi 敲表达式即打印是 interactive 专属行为（LDM-2020-04-15）。VB 的 `?` 是交互方言的显式打印标记；C 方案保留显式 `?` 兼容，只把"原本就报错的裸表达式"变成打印——既对齐 csi 直觉，又不让任何合法语句改义。
- **静默语义风险**：写错的表达式静默打印 vs 报错。C 方案把行为变化钉在「错误→打印」，交互场景下「打印出值」本身就是反馈；A 方案的静默面不可接受。
- **与退出码正交（脚本模式实证）**：`Scripting\VisualBasicTest\CommandLineRunnerTests.vb:239-273` 显示脚本文件末尾 `? 21`、`? New System.Guid()` 均**不设退出码**，`Return 21` 才设退出码 21。自动打印只在交互式 REPL 生效，脚本模式不启用。
- **实现落点：编译器层（跟随 C# REPL 的设定）**。C# REPL 专门在 interactive 方言的编译器语义里允许末尾表达式产生结果（LDM-2020-04-15），VB REPL 跟随：Script kind 下把提交的末尾表达式绑定为 RValue（抑制「值被丢弃」类诊断）、解析层把语句首裸表达式解析为表达式语句；宿主层零改动（`HasSubmissionResult` + `globals.Print` 现有路径自动打印）。宿主层「重试为 `? <raw>`」降级为 fallback（见深度追问 2）。
- **诊断族两层不统一**：需要实证枚举。

### 深度追问：LDM 拷问清单

1. **「值被丢弃」诊断族是两层**：绑定层（BC30545，属性访问作语句）+ 解析层（裸表达式被误解析，如 `1 + 2` 撞标签路径）。提案原以为主要是 BC30545，源码核实显示解析层还有别的码——诊断族必须实证枚举，不能只盯 BC30545。
2. **实现落点：编译器层，跟随 C# REPL 的设定（推荐方案）**。C# REPL 专门做了这种处理——interactive 方言的编译器语义允许末尾表达式产生结果（LDM-2020-04-15「For the scripting dialect this is mostly for producing a result in an interactive setting」），宿主只读返回值打印。VB REPL 跟随同一设定，落在编译器层、宿主层零改动：
   - **绑定层**：Script kind 提交的**末尾表达式语句**复用 `BindPrintStatement` 的 `BindRValue` 路径绑定为 RValue，抑制「值被丢弃」类诊断（BC30545 绑定层、解析层标签/误解析）。`HasSubmissionResult`（非 Void ExpressionStatement 已返回 True）自动让 REPL 打印。
   - **解析层**：Script kind 语句首的裸表达式（`1 + 2` 撞 `ParseLabel`、`x > 5` 被 `MakeInvocationExpression` 误包）解析为表达式语句；需区分「裸数值+冒号 = 标签」与「裸表达式 = 表达式语句」，并保住 `MySub`（VB 无括号 Sub 调用是合法语句，不得自动打印）。
   - **仅末尾产生结果**：与 C# 一致，只有提交的**末尾**表达式产生结果；多行提交中间行的裸表达式仍报错。`HasSubmissionResult` 已用 `root.Members.LastOrDefault()` 判末尾，规则照搬，binder 线程「是否末尾语句」上下文即可。
   - **代价（已接受）**：Script kind 无法区分 REPL 提交与 .vbx 脚本文件（`SourceCodeKind.Interactive` 已废弃，两者同为 Script），`.vbx` 里的裸表达式从「报错」变「静默合法（no-op，不设退出码）」——C# 对 csx 接受了同样的事（LDM-2020-04-15「we are ok with this remaining distance」），本会议跟随接受。
   - 宿主层「重试为 `? <raw>`」降级为 fallback：仅当解析层裸表达式歧义风险过大、或未来需要让 .vbx 里的裸表达式仍报错时再启用。
3. **与 HasSubmissionResult 的互动**：编译器层绑定为 PrintStatement 语义后，HasSubmissionResult 恒 True（即使 Void），统一走 `globals.Print`；不依赖「非 Void 才打印」的 ExpressionStatement 分支。
4. **与退出码正交**：编译器层规则作用于 Script kind；宿主侧 REPL 打印返回值、脚本模式（RunScript）丢弃返回值不设退出码。脚本文件末尾表达式不设退出码的已修复语义不受影响。
5. **Option Strict 分叉**：编译器层绑定路径与显式 `? Now` 完全一致（BindRValue），无新分叉；但宽松模式（Option Strict Off）下部分表达式可能不报「值被丢弃」类错误而应排除出自动打印，需在行为表核对（标 `Suspect`）。
6. **边界情况**：属性访问（`Now`、`DateTime.Now`）、无参方法调用（`MySub` 不带括号——VB 传统合法调用，不得自动打印）、算术（`1 + 2`）、比较（`x > 5`）、字符串（`"a" & "b"`）、多行续行末尾表达式、`?` 与三元 `? .` / `? !` 的区分。
7. **测试纪律**：无副作用单测（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb` 等）；新增用例：`Now` 打印、`1 + 2` 打印 3、`x = 5` 不打印、`Console.WriteLine("hi")` 不打印、脚本模式不自动打印、真正的语法错误（`Now +`）仍报原错误。

### RESOLUTION:

1. **维持 Active（采纳 C 方案方向）**：REPL 表达式开头 `?` 可选——提交是「表达式作为语句 + 值被丢弃类诊断」时，按 `?` 语义求值打印。
2. **显式 `?` 保留**、**合法语句不改义**（与先导会议一致）。
3. **与退出码正交**：交互式专属，脚本模式不启用。
4. **实现落点：编译器层，跟随 C# REPL 的设定**——Script kind 提交的**末尾表达式**绑定为 RValue（抑制「值被丢弃」类诊断）、解析层把语句首裸表达式解析为表达式语句、仅末尾产生结果；`HasSubmissionResult` + `globals.Print` 现有打印路径自动生效，宿主层零改动。`.vbx` 错误面变化（裸表达式从报错变静默 no-op）随 C# 对 csx 的先例接受。宿主层「重试为 `? <raw>`」降级为 fallback。
5. **诊断族实证枚举为 TODO**：BC30545（绑定层）+ 解析层码；编译器层下触发条件天然收窄到「提交末尾的表达式语句」，首版实现时逐项枚举并回归。

### Implication:

- spec 合并时把「编译器层：Script kind 末尾表达式绑定为 RValue + 解析层裸表达式语句」写进 Detailed design；含行为对照表；仅末尾表达式产生结果；宿主层保持「REPL 打印、脚本丢弃」现状。
- 最小原型：先绑定层抑制「值被丢弃」诊断（Script kind 末尾表达式）验证 `Now` 打印；再加解析层裸表达式语句验证 `1 + 2` 打印 3、`MySub` 不自动打印、`.vbx` 裸表达式静默 no-op。
- 补测试矩阵（无副作用），含 `Regular` 编译零影响证明（`SourceCodeKind` 门控）。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`1 + 2` 在语句首的确切解析路径（标签 vs 表达式语句）——需实证。
- `OPEN QUESTIONS`：宽松模式（Option Strict Off）下哪些表达式不再报「值被丢弃」类错误而应排除出自动打印。
- `OPEN QUESTIONS`：`.vbx` 脚本文件里的裸表达式从报错变静默 no-op——已按 C# csx 先例接受；若未来要恢复脚本侧报错，需引入 REPL/脚本区分开关。
- `TODO`：诊断族清单（BC30545 + 解析层）逐项枚举并回归。
- `TODO`：解析层裸表达式语句与标签/`Call`/`Mid` 的消歧实现细节（`1:` 标签、`MySub` 无括号调用）。
- `TODO`：binder 线程「是否提交末尾语句」上下文（复用 `HasSubmissionResult` 的 `root.Members.LastOrDefault()` 判定）。
- `Follow-up`：与 Avalonia GUI（`proposal-avalonia-ise-repl-ui`）的打印/输出窗格整合，复用同一条打印路径。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——对齐 csi 交互体验、纯增量（不改任何合法代码语义）、实现落点已核实（编译器层，跟随 C# REPL 设定）、风险低。提案归 active 根目录。

---

## 附录：C# 生态与互操作考量

先引先导会议已核实的引文（精简引用并标注来源，不重新编造）：

- **LDM-2020-04-15**（`csharplang\meetings\2020\LDM-2020-04-15.md`）：「unlike script we still don't allow expressions at the end. For the scripting dialect this is mostly for producing a result in an interactive setting.」+ Decision「We are ok with this remaining distance, and would prefer not to have a notion of 'expression at the end produces result' in C#.」→ 对应 VBScript.NET 的退出码语义与「末尾表达式不设退出码」。
- **LDM-2020-02-26 / LDM-2019-09-11**：C#「第三种方言」担忧 → VBScript.NET 作为产品自带 REPL 本来就是交互方言，要有意识地保持与普通 VB 的最小差异。
- **LDM-2020-01-22**：submission 状态保持 → Imports 累积/globals 资产。

**新角度（本附录重点）**：**csi 敲表达式即打印 = interactive 专属行为**（LDM-2020-04-15 承认只留 interactive 用）；C# 无对应语法可冲突。VB 的 `?` 是显式打印标记。**本次定案：跟随 C# REPL 的编译器层设定**——把「末尾表达式产生结果」作为 Script 方言的编译器语义（C# 就是这么做的：interactive 方言允许末尾表达式，宿主只读返回值打印），而不是宿主文本重写；`?` 显式前缀保留兼容，脚本方言从「必须显式 `?`」变为「末尾表达式隐式出结果」。**不引入第三种方言**——这是交互方言既有方向（PrintStatement）在编译器语义上的扩展，与 C# 对 csx 的处理同构。

**对既有 RESOLUTION/三态判定的影响**：不变（维持 Active）；RESOLUTION #4 由「宿主层重试」改为「编译器层跟随 C# REPL 设定」，宿主层重试降级为 fallback。附带接受 `.vbx` 错误面变化（裸表达式从报错变静默 no-op，随 C# csx 先例）。

**适应建议**：实现复用 `?` 的打印路径（`ObjectFormatter`/`PrintOptions`/`globals.Print` 单一路径），不另起打印实现。
