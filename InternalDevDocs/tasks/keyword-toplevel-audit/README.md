# 关键字 × 脚本顶层容器 审计

## 1. 任务与范围

脚本方言与普通编译的差别不在关键字，而在**容器**：普通 VB 里语句只能写在方法体内，脚本里可以直接写在顶层，
顶层声明还会被映射成脚本类的成员。容器一变，同一个关键字可能落到不同的 parser 分支、不同的 binder 门、
不同的诊断，甚至被静默读成别的东西。

本审计逐个关键字回答三个问题：

1. 该关键字在顶层**实际**表现如何（本机实测，不是读代码推断）；
2. 这个表现**有没有设计依据**（`spec-scripting-dialect.md` / parser 分支 / binder 门），即「合理照顾」还是「漏了」；
3. 这个表现**有没有测试**（指向真实 `<Fact>`/`<Theory>` 方法声明行）。

范围：

| 项 | 口径 |
|---|---|
| 关键字全集 | **201** 个 = 154 个保留词 + 47 个上下文关键字，来源 `SyntaxFacts.GetKeywordKinds`（`Compilers/VisualBasic/Portable/Syntax/SyntaxKindFacts.vb:25`）与 `SyntaxFacts.IsReservedKeyword` / `IsContextualKeyword`（同文件 `:14-26`），逐项由 `SyntaxFacts.GetText` 取文本 |
| 容器 | 脚本顶层（语句、字段/自动属性/共享字段初始化器、顶层 `Sub`/`Function`/`Property` 体内、顶层 lambda） |
| 路径 | `vbx`（一次提交，等同 `vbi file.vbx`）与 `submissions`（提交链第 N 次提交，等同 REPL 一行） |
| 不在范围 | 非关键字的语法构造（`script-mode-coverage-parity` 的 240 行 ledger 已按 spec 小节覆盖）；工具链与语言服务器 |

## 2. 方法与口径

### 2.1 探针

探针宿主是 `Microsoft.CodeAnalysis.VisualBasic.Scripting` 的 `VisualBasicScript`，与
`Scripting/VisualBasicTest/ScriptModeConformanceTests.vb` 的 `AssertRuns` / `RunChain`（`:65` / `:220`）同构：
`vbx` 路径 = `Create(source)`，`submissions` 路径 = `Create("Dim marker = 1").ContinueWith(source)`，宿主导入取自
`Interactive/vbi/vbi.coreclr.rsp:39-50`。每个探针记录前端诊断、语法树顶层节点、emit 结果，以及关键字文本在树里的
**出现形态**（`tokenKind<parent<grandparent`）。`IdentifierToken<IdentifierName<InvocationExpression` 是
「关键字被读成方法调用」的指纹。

每种关键字跑 14 种形态：`top-bare`（`{0}`）、`top-bare-next`（`{0}` + 下一行 `Dim z = 2`）、`top-arg`（`{0} 1`）、
`top-paren`（`{0}(1)`）、`top-init`（`Dim v = {0} 1`）、`top-init-paren`、`top-auto-prop-init`、
`top-shared-init`，以及对照组 `sub-body-bare`、`sub-body-arg`、`func-init`、`prop-get`、`prop-get-bare`、
`lambda-body`。对照组回答「同一行写进顶层方法体是什么行为」，与顶层那一列的差就是「容器是否被特别照顾」。
机械形态之外，每个关键字另补一条自己的典型写法（合计 201 × 14 × 2 机械格 + 约 700 条自定探针）。

### 2.2 判定词（闭集）

| 判定 | 含义 |
|---|---|
| `合理` | 行为与设计意图一致且有依据可引；两种情况行为不同但各有依据也算合理 |
| `缺陷` | 关键字被静默读成标识符/方法调用/别的构造；诊断码与关键字语义无关（parser 恢复码冒充关键字诊断）；应报未报或指错位置；两路径/两容器不一致且无依据 |
| `缺口` | 行为看着合理但没有测试覆盖（按检索判定） |
| `待定` | 证据不足，须写明缺哪条证据 |

上下文关键字被当标识符使用是语言本来的设计（`SyntaxKindFacts.vb:22-26`：`Yield` `From` `Where` `Async` 等
18 个词在普通代码里就是合法标识符），只有**顶层形态下的静默换义**才算缺陷。本审计因此没有把「裸写上下文关键字
得到 `BC30451`」批量记成问题；`anomalies.md` 里那类「通用恢复码」候选逐条复核后基本属于正常语义。

### 2.3 两条路径

201 个关键字 × 14 种形态 × 2 条路径共 5628 格机械读数，**零 DIFF**；各组自定探针共约 700 条同样零 DIFF。
端到端用 `vbi.exe` 抽核了 vbx（`vbi file.vbx`）与 REPL（stdin）两条路径的关键样本（`Yield 1`、`On Error Resume Next`），
读数与进程内宿主一致。所以下文不区分路径，只标注容器。

## 3. 结论摘要

| 判定 | 数量 |
|---|---|
| 合理 | **176** |
| 缺口 | **20** |
| 缺陷 | **5**（`Yield`、`Async`、`Iterator`、`Custom`、`RaiseEvent`） |
| 待定 | **0** |

审计后五处缺陷**全部修掉**（`Yield`、`Async`/`Iterator`/`Custom` 两类根因，以及顶层 `RaiseEvent`），修复与复验见 §7。

### 3.1 容器级接线：五处

脚本容器对关键字的**专门照顾**只发生在这些地方，其余关键字全部沿用方法体或声明路径：

| 关键字 | 容器级处理 | 位置 |
|---|---|---|
| `Await` | 编译单元上下文**直接声明自己是 async 方法体**，于是 `Await` 走语句分支 | `Parser/BlockContexts/CompilationUnitContext.vb:32-36`；分支 `Parser.vb:803-804`、`:1162-1164` |
| `End` | 顶层 `End` 走方法体路径（stop-or-end），不再走分组结束路径 | `Parser.vb:829-838` |
| `AddHandler` / `RemoveHandler` / `RaiseEvent` | 顶层按语句解析，不按事件访问器解析（`RaiseEvent` 是审计后补的） | `Parser.vb:843-855`、`:899-905` |
| `Yield` | 审计时**没有**对应接线；修复后由 `BlockContext.IsScriptTopLevelStatement` 认领顶层语句链 | `BlockContext.vb`、`CompilationUnitContext.vb`，分支 `Parser.vb:1180-1185`（修复见 §7） |

`Await` 与 `Yield` 是同一类上下文关键字，差别曾经在容器级钩子：`BlockContext.vb:138-142` 把 async 判断声明为
`Overridable`，`CompilationUnitContext` 才有机会改写；`Yield` 依赖的
`IsWithinIteratorMethodOrLambdaOrProperty`（`BlockContext.vb:150-154`）是**不可重写**的只读属性，
编译单元上下文无法声明自己是迭代器，顶层 `Yield` 因此落回标识符路径。修复沿用了 async 那条路的形状：
新增一条可重写、由上下文链推导的「正在解析脚本顶层语句」判断，而不是把编译单元硬说成迭代器。

### 3.2 已确认「合理」的容器级行为

| 关键字 | 实测 | 设计依据 |
|---|---|---|
| `End` | 顶层 `End` `CLEAN` 且 emit 成功；顶层 `End 1` → `BC30678`；顶层方法体内同；游离 `End If` → `BC30087`、`End Sub` → `BC30429` | `spec-scripting-dialect.md:200-212`，`Parser.vb:829-838`，测试 `ScriptModeStatementConformanceTests.vb:395` |
| `Await` | 顶层 `Await 1` → `BC36930`（按 await 语句绑定而非调用），`Dim v = Await Task.FromResult(3)` 干净 | `spec-scripting-dialect.md:179-187`，`CompilationUnitContext.vb:32-36` |
| `AddHandler` / `RemoveHandler` | 顶层按语句解析；带类型探针两路径 `CLEAN` | `spec-scripting-dialect.md:189-198`，`Parser.vb:843-855` |
| `On Error` / `Resume` | 顶层 → `BC36956`（`ERR_ResumablesCannotContainOnError`，`Errors.vb:1586`，消息逐字含 `'On Error' and 'Resume'`）；顶层方法体内 `CLEAN` | 由「顶层语句是 async 初始化器的方法体」派生，`spec-scripting-dialect.md:143-148`、`:438`；测试 `ScriptModeStatementConformanceTests.vb:224` |
| `Namespace` / `Me` / `MyBase` / `MyClass` | `BC36965` / `BC36966`；`Shared` 成员先报 `BC30043`；嵌套类型内 `CLEAN` | `spec-scripting-dialect.md:283-300`、`:74` |
| 顶层声明四形态（`Dim` / `Sub`+`Function` / 嵌套类型 / 可执行语句） | 与 spec 的映射逐条一致；顶层 auto-property 与其后语句解析稳定 | `spec-scripting-dialect.md:56-74` |
| 块语句 9 个（`If` `For` `While` `Do` `Select` `Try` `Using` `SyncLock` `With`） | 顶层典型写法两路径 `CLEAN` | `Parser.vb:877-879` 走方法体路径 |

唯一的欠账是措辞：`On Error` 一族在限制表（`:283-295`）里没有条目，只在 Testing 清单（`:438`）有一句
「unsupported-statement 诊断」，而实际码是 resumable 门的 `BC36956`。

## 4. 缺陷清单

> 复现均为最小源码；「实测」为探针读数，`vbx` 与 `submissions` 两条路径读数一致（`Yield`、`On Error` 另经 `vbi` 端到端复核）。

### D1 · 顶层 `Yield` 被读成方法调用，`BC36966` 成为不可达代码（issue 40）

| 项 | 内容 |
|---|---|
| 症状 | 顶层 `Yield 1` 不给「关键字在此处无效」，而是按**方法调用**解析并报「参数必须加括号」 |
| 复现 | `Yield 1`（顶层，脚本） |
| 实测 | `BC30800`（`ERR_ObsoleteArgumentsNeedParens`，`Errors.vb:630`）另有 emit 期 `BC30451`（`'Yield' is not declared`）；语法树形态 `IdentifierToken<IdentifierName<InvocationExpression<ExpressionStatement`，树里没有 `YieldStatement`；`vbi` 的 vbx 路径与 REPL 路径同样报 `(1) : error BC30800` |
| 期望 | `BC36966`（`ERR_KeywordNotAllowedInScript`），依据 `spec-scripting-dialect.md:286`（限制表）与 `:330`（「A `Yield` in top-level script code is rejected」） |
| 成因 | parser 的 yield 分支要求 `Context.IsWithinIteratorMethodOrLambdaOrProperty`（`Parser.vb:1166-1168`），该判断在编译单元上下文**不可重写**（`BlockContext.vb:150-154`），顶层永远为假，`Yield` 落回 `ParseAssignmentOrInvocationStatement`（`Parser.vb:1173`） |
| 连带 | binder 侧的 `BC36966` 门已成死代码：`Binder_Statements.vb:5248-5256` 只在收到 `YieldStatementSyntax` 时报，顶层 parser 不产该节点 |
| 是否已被测试固化 | **是**。`ScriptModeStatementConformanceTests.vb:81-86` 断言 `"Yield 1"` → `BC30800`，其文档注释写的是「顶层不行，因为宿主的初始化器不是迭代器」——注释说设计，断言钉误解析。覆盖 ledger `tasks/script-mode-coverage-parity/test-plan.md:394` 把它记成「顶层裸 `Yield` 报 BC30800」，与实测也不完全一致（裸 `Yield` 实测 `BC30451`，`Yield 1` 才是 `BC30800`） |
| 修法方向 | 让顶层进入 yield 分支（`IsTopLevelScript` 是现成判据，`Parser.vb:85-92`），把拒绝交给已有的 binder 门；需同时照顾 `Yield = 3`（`Dim Yield` 后赋值）、`Yield(1)`（真调用）两种把 `Yield` 当标识符的合法形态 |
| 状态 | **已修复**（§7）。`Yield 1` / `Yield(1)` 现在报 `BC36966`；字段赋值 `Dim Yield = 2` 后 `Yield = 3` 仍 `CLEAN` |

顶层 `Yield` 的四种写法，用于界定缺陷边界：

| 形态 | 实测 | 说明 |
|---|---|---|
| 顶层 `Iterator Function It() … Yield 1` | `CLEAN`，`YieldKeyword<YieldStatement<FunctionBlock` | 正常 |
| 顶层 `Dim f = Iterator Function() … Yield 1` | `CLEAN`，`YieldKeyword<YieldStatement<MultiLineFunctionLambdaExpression` | 正常 |
| 顶层 `Sub S() … Yield 1` | `BC30800`，形态为调用 | 普通 VB 的上下文关键字规则，不算脚本缺陷 |
| 顶层裸 `Yield 1` | `BC30800`，形态为调用 | **脚本缺陷**：设计要求 `BC36966` |

### D2 · 顶层把 `Async` / `Iterator` / `Custom` 当修饰符起始，赋值语句被读成声明（issue 40）

| 项 | 内容 |
|---|---|
| 症状 | 用上下文关键字当变量名时，**同一行赋值**在顶层报「需要声明」，在方法体内正常 |
| 复现 | `Dim Async = 2` 换行 `Async = 3`（`Iterator` / `Custom` 同形） |
| 实测 | 顶层 `BC30195`（`Expected one of 'Dim', 'Const', 'Public', …`），形态为两个 `FieldDeclaration`（第二行被当声明）；顶层 `Sub S()` 内同两行 `CLEAN`，形态 `IdentifierName<SimpleAssignmentStatement<SubBlock`；只读不赋值（`Console.WriteLine(Async)`）顶层正常 |
| 成因 | 顶层标识符分派对三者**没有前瞻**：`Custom` 直接走 `ParseCustomEventDefinition`（`Parser.vb:796-797`），`Async`/`Iterator` 直接走 `ParseSpecifierDeclaration`（`Parser.vb:801-802`）；方法体分派有前瞻——`Custom` 要求下一 token 是 `Event`（`Parser.vb:1151-1152`），`Async`/`Iterator` 要求下一 token 能起始修饰符声明（`Parser.vb:1154-1160`） |
| 判定 | 缺陷（容器不一致；上下文关键字当标识符是语言允许的，顶层却把它读成修饰符）；测试零覆盖 |
| 修法方向 | 顶层三支补齐与方法体同形的 `PeekToken(1)` 判断 |
| 状态 | **已修复**（§7）。`Async = 2` / `Iterator = 2` / `Custom = 2` / `Type = 2` 现在都是普通赋值；`Async Function`、`Iterator Function`、`Custom Event`、`Type Foo` 的原有读法不变 |

D1 与 D2 是同一类问题的两个面：顶层 statement-start 分派比方法体少一层前瞻或上下文判断，
`Yield` 因此丢了关键字身份，三兄弟因此多出关键字身份。

### D3 · 顶层 `RaiseEvent` 被读成访问器声明，报「需要声明」（issue 41）

| 项 | 内容 |
|---|---|
| 症状 | 脚本自己在顶层声明 `Event E()`，紧接着 `RaiseEvent E()` 却按 Custom Event 的访问器声明解析 |
| 实测 | 顶层 `BC30188`（Declaration expected）+ `BC30205`，形态 `RaiseEventKeyword<RaiseEventAccessorStatement`；同一语句写在顶层 `Sub` 或嵌套 `Class` 内 `CLEAN`（`RaiseEventStatement`） |
| 期望 | 拒绝本身有设计：`spec-scripting-dialect.md:438` 说顶层 `RaiseEvent` 报「unsupported-statement 诊断」；测试 `ScriptModeStatementConformanceTests.vb:375` 把 `BC30188` 钉住 |
| 问题 | ① 设计表（`:283-295`）没有这一条，只有测试清单一句话；② 实际报的是访问器分支的恢复码，不是为该关键字准备的诊断；③ parser 对同族的 `AddHandler`/`RemoveHandler` 有 `IsTopLevelScript` 守卫（`Parser.vb:843-855`），`RaiseEvent` 没有（`Parser.vb:857-858`），三支分叉无依据 |
| 判定 | 诊断错位 + 同族分叉（拒绝可保留，但需在规范里写清，或按 `AddHandler` 的模式加守卫再裁定顶层是否允许 `RaiseEvent`） |
| 状态 | **已修复**（§7）：按 `AddHandler`/`RemoveHandler` 的模式加守卫，顶层 `RaiseEvent` 成为普通语句；`spec` 的 `AddHandler` 一节与 Testing 清单同步改写，两个钉旧行为的测试改写为运行断言 |

## 5. 观察（未计缺陷）

| 项 | 读数 | 说明 |
|---|---|---|
| `Type` 的两容器差异 | 声明位 `BC30802`（`ERR_ObsoleteStructureNotType`，`Errors.vb:632`）、方法体 `BC30109`（`ERR_ClassNotExpression1`，`Errors.vb:172`） | 普通编译（`SourceCodeKind.Regular`）逐字复现同一差异：声明位有专用臂（`Parser.vb:798-800`），方法体分派没有。属语言既有的位置分层；**但** `Type = 2` 这样的字段赋值在顶层曾被这条专用臂误吞（§4 D2 一并修复），修复后只有「`Type` + 名字」才走过时语句提示 |
| `EndIf` / `Wend` / `Gosub` / `Variant` | 顶层与方法体同码（`BC30087+BC30826` / `BC30090+BC30809` / `BC30814` / `BC30804`） | 都是为「已过时」诊断保留的真关键字（`KeywordTable.vb:219-223`），各有专属臂（`Parser.vb:1008`、`:1240`、`:798`）；`EndIf` 另有指令位变体（`ParseConditional.vb:59`） |
| `BC36966` 的英文消息 | 消息写死 `… in top-level script code`，但同一码也落在顶层成员体（spec `:438` 要求如此） | 措辞与作用域不符，纯文案问题 |
| `NameOf(Me)` | `BC37244`，`BC36966` 被遮蔽 | `NameOf` 实参名解析先于关键字门（`ParseExpression.vb:690-706`）；两码都是拒绝 |
| `Me 1` / `Me(1)` 语句位 | `BC30800` / `BC30454` | 语句位先走 assignment-or-invocation（`Parser.vb:1175-1179`），两容器同样拒绝 |
| 注释唯一的脚本 | 前端零诊断但进程内 `Emit` 返回失败且零诊断；`vbi` 端到端 `exit=0` | 宿主差异（`vbi` 的执行路径不复现），不记缺陷；`REM` 进 trivia 正确 |
| `BC31003` 的覆盖 | `Scripting/VisualBasicTest/*.vb` 与 `Test/` 全树 0 条断言 | 它是「顶层裸表达式不在末尾」的唯一分界规则，影响 11 个保留词算子；属既有 ledger 之外的跨组缺口 |
| `#Disable` / `#Enable` 缺 `Warning`、`#ExternalChecksum` 缺实参 | 诊断锚在**下一行行首** | 普通编译同形，fork 未引入 |

## 6. 缺口清单（20 个）

按检索判定：该关键字的顶层形态在 `Scripting/VisualBasicTest` 里找不到对应用例。

| 关键字 | 缺口形态 |
|---|---|
| `True` `False` `Nothing` | 「裸字面量作为脚本结果」的末句路径无用例（`BC31003` 侧也只有数字表达式用例） |
| `And` `Or` `OrElse` `Xor` | 顶层逻辑/位运算符表达式零用例（`Xor` 全测试树零命中） |
| `All` `Ascending` | `Into All`、`Order By … Ascending` 实测 `CLEAN` 但无测试 |
| `ElseIf` | 只有 `#ElseIf` 指令有覆盖，语句形零测试 |
| `When` | `Catch … When` 顶层零测试 |
| `Mid` | `Mid(...) = ...` 赋值语句形零测试（现只有函数形） |
| `Assembly` | 顶层 `<Assembly:…>` 实测 `CLEAN` 但零测试 |
| `Binary` | 显式 `Option Compare Binary` 零用例（只有默认值被钉住） |
| `ExternalChecksum` | 脚本顶层零用例 |
| `Type` | 顶层 `BC30802` 零用例（遗留拼写组只覆盖了 `EndIf`/`Wend`/`Gosub`） |
| `ByVal` `WriteOnly` `NotInheritable` `Out` | 脚本源码里零出现，因而无任何用例（`Out` 指 `Interface I(Of Out T)`） |

另有形态级缺口（关键字本身判合理，不重复计数）：`#EndIf` 指令位无脚本用例；`#R`/`#Load` 在**普通编译**下的
`BC36964`/`BC36967` 全仓零用例；`IsNot`、`Key` 只有 REPL 侧用例；`IsFalse`/`IsTrue` 只有声明被覆盖，
使用侧（`If f Then`）无断言；`xml:` 前缀属性、`Me` 在顶层 `Property` 体内无正例。

## 7. 修复记录（审计后）

修复的是五处「明确缺陷」：`Yield` 丢了关键字身份、四个上下文关键字多出关键字身份、顶层 `RaiseEvent` 被读成访问器声明。

### 7.1 改动

| 文件 | 改动 |
|---|---|
| `Compilers/VisualBasic/Portable/Parser/BlockContexts/BlockContext.vb` | 新增 `IsScriptTopLevelStatement`：沿上下文链推导「正在解析脚本顶层语句」，遇 `Sub`/`Function`/`Property`/访问器/lambda 体返回 False |
| `Compilers/VisualBasic/Portable/Parser/BlockContexts/CompilationUnitContext.vb` | 覆写该判断为 `Parser.IsScript`，与已有的 `IsWithinAsyncMethodOrLambda` 同形 |
| `Compilers/VisualBasic/Portable/Parser/Parser.vb` | ① yield 分支增加顶层条件；② `StartsAssignmentOrMemberAccess` 判定「下一 token 让前导名字成为赋值目标/成员访问」；③ 顶层 `Custom`/`Type`/`Async`/`Iterator` 四处分支补 `PeekToken(1)` 前瞻——与方法体分派同形；④ `RaiseEventKeyword` 补上与 `AddHandler`/`RemoveHandler` 同形的 `IsTopLevelScript` 守卫 |
| `InternalDevDocs/spec/spec-scripting-dialect.md` | `AddHandler` 一节扩为 `AddHandler`/`RemoveHandler`/`RaiseEvent`，Testing 清单里「顶层 `RaiseEvent` 报 unsupported 诊断」改成「顶层 `RaiseEvent` 运行已挂接的处理器」 |
| `Scripting/VisualBasicTest/ScriptModeStatementConformanceTests.vb` | `TopLevelYieldWithoutIterator_IsReported` 改断言 `BC36966` 并加嵌套块形态；新增 `TopLevelYieldNameAndIterator_Conform`；`TopLevelRaiseEventStatement_IsReported` 改写为 `TopLevelRaiseEventStatement_Conforms`（挂 handler 后 `hits = 1`） |
| `Scripting/VisualBasicTest/ScriptModeParserArmConformanceTests.vb` | 新增 T061 两格：`TopLevelContextualKeywordAssignment_Conforms`、`TopLevelContextualKeywordDeclaration_StillReads` |
| `Scripting/VisualBasicTest/ScriptTests.vb` | `TestTopLevelRaiseEventStatementReportsUnsupportedDiagnostic` 改写为 `TestTopLevelRaiseEventStatement`：脚本自声明的事件可 raise 并投递；宿主对象的事件仍被拒（`BC31132`） |
| `Scripting/VisualBasicTest/SubmissionSharedHandlesHookupTests.vb` | 只改注释：顶层 `RaiseEvent` 已不是非法语句，但只能 raise 本类声明的事件 |

顶层变量仍是脚本类的**字段**：`Dim Yield = 2` / `Yield += 3`、`Dim Async = 2` / `Async = 2` 都按普通字段赋值处理，
判据是「下一 token 是否能起始声明」，不是把名字一律读成关键字。

### 7.2 复验读数

| 项 | 修复前 | 修复后 |
|---|---|---|
| 顶层 `Yield 1` | `BC30800`，形态 `IdentifierToken<IdentifierName<InvocationExpression` | `BC36966`，形态 `YieldKeyword<YieldStatement` |
| 顶层 `Yield(1)` | `BC30451` | `BC36966` |
| 顶层 `If True Then / Yield 1 / End If` | `BC30800` | `BC36966`（嵌套块同样认领） |
| 顶层 `Dim Yield = 2` / `Yield = 3` | `CLEAN` | `CLEAN`（字段赋值保住） |
| 顶层 `Iterator Function` / 迭代器 lambda / 嵌套类迭代器 | `CLEAN` | `CLEAN` |
| 顶层 `Sub S()` 内 `Yield 1` | `BC30800` | `BC30800`（普通 VB 语义，不动） |
| 顶层 `Async = 2` / `Iterator = 2` / `Custom = 2` / `Type = 2` | `BC30195` / `BC30195` / `BC30195` / `BC30802` | 均 `CLEAN`，形态 `SimpleAssignmentStatement` |
| 顶层 `Async Function` / `Iterator Function` / `Custom Event` / `Type Foo` | `CLEAN` / `CLEAN` / `CLEAN` / `BC30802+BC30678` | 不变 |
| 顶层 `Event E` + `AddHandler` + `RaiseEvent E()` | `BC30188`+`BC30205`，形态 `RaiseEventAccessorStatement` | `CLEAN`，形态 `RaiseEventStatement`，handler 投递（`hits = 1`，vbx 与 REPL 均验） |
| 顶层 raise 宿主对象的事件 | `BC30188`+`BC30205`（解析期） | `BC31132`（绑定期：`RaiseEvent` 只能 raise 本类事件） |
| `Custom Event` 块内的 `RaiseEvent` 访问器 | `CLEAN` | `CLEAN`（`RaiseEventAccessorStatement`，守卫不进入） |

回归面：

- **全量 battery**（201 关键字 × 14 形态 × 2 路径 = 5628 格）逐格比对两轮：第一轮（`Yield`+上下文关键字）**40 格**变化，
  第二轮（`RaiseEvent`）**8 格**变化，全部落在对应关键字，无崩溃、无其他关键字受影响。
- **脚本单测**：`Scripting/VisualBasicTest` 全量 **814** 个用例，失败 **3** 个，全部为改动前基线同样失败的
  环境相关用例（`PrintOptionsTests.ValidMemberDisplayFormat`、`ScriptModeObjectFormatterTests.DebuggerDisplay`、
  `ScriptModeObjectFormatterTests.StackTrace_Dynamic`；已用 HEAD 源码重建编译器复跑确认）。
- 新增/改写的 6 个用例全绿；`vbi` Release 端到端（vbx 与 REPL）验过 `Yield 1`、`Dim` 字段名 `Yield`、顶层 `RaiseEvent`。

已知边界：顶层裸 `Yield`（无操作数）先报语法 `BC30201`（`Expression expected`），绑定期的 `BC36966` 落在 emit 阶段；
`Yield` 作为**表达式**位置（`Dim v = Yield(1)`、`Console.WriteLine(Yield)`）不受影响。

### 7.3 Debug 构建下的既存断言（上游来源，本 fork 不修；issue 42）

`RaiseEvent` 在**任何 resumable 方法**（`Async`/`Iterator` 方法体）里都会让 **Debug 构建的编译器**在
`StateMachineRewriter.StateMachineMethodToClassRewriter.vb:264` 撞上 `Debug.Assert(local.SynthesizedKind.IsLongLived())`。
复现不依赖顶层：普通 `Async Function` 或 `Iterator Function` 体内 `RaiseEvent` 同样触发（用 Debug `vbi.exe` 实测），
说明它是既存问题；`RaiseEvent` 的 lowering 用 `SynthesizedLocalKind.LoweringTemp`（`LocalRewriter_RaiseEvent.vb:54`），
这一行与参考的上游 Roslyn 完全相同，所以不是本 fork 引入的。

Release 构建不触发（`Debug.Assert` 在 Release 被条件编译掉），语义也正确：Release `vbi` 跑顶层 `RaiseEvent` 脚本打印正常，
单测里 handler 投递计数为 1。顶层 `RaiseEvent` 之所以现在会碰上它，是因为脚本顶层语句是 async 初始化器的方法体——
这正是「顶层 = async 方法体」的必然结果，与 `Async Function` 体内的同一语句同因。

## 8. 顺带发现：覆盖 ledger 的指针漂移

`InternalDevDocs/tasks/script-mode-coverage-parity/test-plan.md` 的 240 行 ledger 有三类失效指针（本审计未改动该文件）：

1. `:394`（`Yield` 行）：把「顶层裸 `Yield` 报 BC30800」当结论，实测裸 `Yield` 是 `BC30451`；同一行引用的方法行号是旧值。
   本次修复后该行结论进一步过期——现为 `BC36966`。
2. 行号漂移（最大 110 行）：`Resume` 行 `:387`、`Error` 行 `:385`、`RaiseEvent` 行 `:365` 指向的行已不落在方法声明行上。
   其中 `RaiseEvent` 行记的「顶层报 `BC30188`」在本次修复后也过期——现为可运行的语句。
3. `:533`（Comments 行）声称脚本源码里没有 `REM` 注释（grep 0 命中），现已有两行 `REM`。

另有 6 处方法指针失效记录在 G1 组报告（`Me`/`MyClass` 行 `:582`、`MyBase` 行 `:581`、`Instance Expressions` 行
`:450`、`Cast Expressions` 行 `:475`、转换函数行 `:596`、`Global`/XML 轴行 `:594`/`:597`）。

## 9. 逐关键字台账

见 `ledger.md`：201 个关键字 × 14 种形态的实测矩阵，附分组、判定、设计依据与测试指针。
判定列保留**审计时**的状态；`Yield`/`Async`/`Iterator`/`Custom` 四行的「缺陷」已由 §7 修掉。
分组口径：G1 类型/转换/字面量/实例表达式 49 个、G2 语句 45 个、G3 声明/成员/修饰符 55 个、
G4 运算符/表达式/查询 30 个、G5 预处理指令/`Option` 子词/遗留拼写 22 个。



