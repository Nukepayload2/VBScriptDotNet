# 脚本顶层的关键字分派与方法体不同形：`Yield` 限制不可达、四个上下文关键字赋值被读成声明

* 状态：**Fixed**（`0c13fb9`）
* 发现日期：2026-10-23
* 发现场景：关键字 × 顶层容器全量审计（`tasks\keyword-toplevel-audit\`，201 关键字 × 14 形态 × 2 路径 = 5628 格 + 约 700 条自定探针）

## 触发面

两个形状同源，都是「顶层语句起始位置的同一个 token 被读成了别的东西」。

**A. 顶层 `Yield` 丢了关键字身份**

```vbx
Yield 1
```

**B. 顶层把上下文关键字当声明起始**

```vbx
Dim Async = 1
Async = 2
```

`Iterator` / `Custom` / `Type` 同形（`Dim Iterator = 1` 后 `Iterator = 2`、`Dim Custom = 1` 后 `Custom = 2`、`Dim Type = 1` 后 `Type = 2`）。

## 症状与实测

A：顶层 `Yield 1` 报 **`BC30800`**（`ERR_ObsoleteArgumentsNeedParens`，「参数必须加括号」），语法树形态
`IdentifierToken<IdentifierName<InvocationExpression<ExpressionStatement` —— 被当成对名字 `Yield` 的**方法调用**，
树里没有 `YieldStatement`。`vbi` 的 `.vbx` 路径与 REPL 路径逐字同码（`(1) : error BC30800`）。

B：顶层 `Async = 2` 报 **`BC30195`**（`ERR_ExpectedSpecifier`），形态是两个 `FieldDeclaration` —— 第二行被当成声明；
同两行写进顶层 `Sub` 体内 `CLEAN`，形态 `IdentifierName<SimpleAssignmentStatement<SubBlock`。`Type = 2` 报 **`BC30802`**
（`ERR_ObsoleteStructureNotType`，「`Type` 已改为 `Structure`」）。

判别性对照（同一编译器，只换容器）：`Yield` 写在顶层 `Iterator Function` / 迭代器 lambda 里正常产出 `YieldStatement`；
写在顶层 `Sub` 体内的 `Yield 1` 仍是 `BC30800`（普通 VB 的上下文关键字规则，不是脚本缺陷）。

## 根因

**顶层的语句起始分派比方法体少一层前瞻或上下文判断。**

- A：`Parser.vb:1180-1185` 的 yield 臂要求 `Context.IsWithinIteratorMethodOrLambdaOrProperty`；该判断在
  `BlockContext.vb:150-154` 是**不可重写**的只读属性，编译单元上下文无法声明自己是迭代器 ⇒ 顶层永远为假，`Yield`
  落回 `Parser.vb:1187` 的 assignment-or-invocation。对照 `Await`：**同一类上下文关键字**，容器级钩子存在——
  `BlockContext.vb:138-142` 把 async 判断声明为 `Overridable`，`CompilationUnitContext.vb:32-36` 覆写为 `Parser.IsScript`
  ⇒ 顶层 `Await 1` 正常按 await 语句绑定（实测 `BC36930`，`ERR_BadGetAwaiterMethod1`）。
- A 的连带：binder 侧的 `BC36966` 门（`Binder_Statements.vb:5248-5256`，`Err_KeywordNotAllowedInScript`）只在收到
  `YieldStatementSyntax` 时报，顶层 parser 不产该节点 ⇒ 该门在顶层是**死代码**。设计要求见
  `spec\spec-scripting-dialect.md:286`（限制表）与 `:330`（「A `Yield` in top-level script code is rejected」）。
- B：`Parser.vb:1122-1131` 对 `Custom` / `Type` / `Async` / `Iterator` 四个记号**没有前瞻**（`Custom` 直接走
  `ParseCustomEventDefinition`、`Async`/`Iterator` 直接走 `ParseSpecifierDeclaration`）；方法体分派有前瞻——
  `Parser.vb:1151-1152` 要求 `Custom` 后随 `Event`、`:1154-1160` 要求 `Async`/`Iterator` 后随能起始修饰符声明的 token。

## 预期行为

A：`BC36966`（`ERR_KeywordNotAllowedInScript`），依据 `spec\spec-scripting-dialect.md:286` 与 `:330`。

B：零诊断。顶层变量是脚本类的**字段**（`spec:58`），`<name> = <value>` 落在字段上是普通赋值语句；上下文关键字作标识符是
语言本来的设计（`SyntaxKindFacts.vb:22-26`）。判别锚：`Dim Async = 1` 本身在顶层 `CLEAN`，且 `Console.WriteLine(Async)`
在顶层也 `CLEAN` ⇒ 只有「赋值目标」这一位置坏。

## 修复方向（已落地）

1. **补容器级钩子**，照 async 那条路的形状：`BlockContext.vb` 新增 `IsScriptTopLevelStatement`——沿上下文链推导「正在解析脚本顶层语句」，
   遇 `Sub`/`Function`/`Property`/访问器/lambda 体返回 False；`CompilationUnitContext.vb` 覆写为 `Parser.IsScript`。
   yield 臂条件改为 `IsWithinIteratorMethodOrLambdaOrProperty OrElse Context.IsScriptTopLevelStatement`，拒绝交回既有 binder 门。
2. **保住字段赋值**：新增 `Parser.vb` 的 `StartsAssignmentOrMemberAccess`（`=` 与各复合赋值、`.`、`!`），顶层判定要求
   「后随 token 不是赋值/成员访问目标」⇒ `Dim Yield = 2` 后 `Yield = 3`、`Yield += 1`、`Yield.ToString()` 仍按字段处理。
3. **顶层四处分支补前瞻**：`Custom` 要求后随 `Event`；`Async`/`Iterator` 用与方法体相同的
   `SyntaxFacts.IsSpecifier OrElse CanStartSpecifierDeclaration`；`Type` 只在后随 `IdentifierToken` 时才走「已改为 `Structure`」提示。

## 修复后的新增行为变化

| 形状 | 修复前 | 修复后 |
|---|---|---|
| 顶层 `Yield 1` / `Yield(1)` | `BC30800` / `BC30451` | `BC36966`（形态 `YieldKeyword<YieldStatement`） |
| 顶层 `If True Then` / `Yield 1` / `End If` | `BC30800` | `BC36966`（嵌套块同样认领） |
| 顶层裸 `Yield` | `BC30451` | `BC30201`（缺操作数，前端）；`BC36966` 在 emit 期一并报出 |
| 顶层 `Dim Yield = 2` / `Yield = 3` | `CLEAN` | `CLEAN`（字段赋值保住） |
| 顶层 `Async = 2` / `Iterator = 2` / `Custom = 2` / `Type = 2` | `BC30195` / `BC30195` / `BC30195` / `BC30802` | 均 `CLEAN`，形态 `SimpleAssignmentStatement` |
| 顶层 `Async Function` / `Iterator Function` / `Custom Event` / `Type Foo` | 正常 / 正常 / 正常 / `BC30802+BC30678` | 不变 |

`Yield` 作为**表达式**位置（`Dim v = Yield(1)`、`Console.WriteLine(Yield)`）不受影响；`Yield` 在非迭代器方法体内仍是标识符（普通 VB 语义）。

## 验证（已运行）

- 全量 battery 逐格比对：**40 格**变化，全部落在 `Yield`/`Async`/`Iterator`/`Custom`/`Type`，无崩溃、无其他关键字受影响。
- `Scripting\VisualBasicTest` 全量 **814** 格：失败 3 格，均为改动前基线同样失败的环境相关用例（`PrintOptionsTests.ValidMemberDisplayFormat`、
  `ScriptModeObjectFormatterTests.DebuggerDisplay`、`ScriptModeObjectFormatterTests.StackTrace_Dynamic`——第三项为 PDB 行号差 1）；
  已用 HEAD 源码重建编译器复跑确认为基线红。
- 新增/改写用例：`ScriptModeStatementConformanceTests.TopLevelYieldWithoutIterator_IsReported`（改断 `BC36966` 并加嵌套块形态）、
  `TopLevelYieldNameAndIterator_Conform`（字段赋值 + 两种迭代器宿主）、`ScriptModeParserArmConformanceTests` T061 两格。
- 发布版宿主端到端：`.vbx` 与 REPL 的 `Yield 1` 均由 `BC30800` 转为 `BC36966`；`Dim Yield = 2` / `Yield += 3` 输出 `5`。

## 相关

- 同族：`issue-top-level-raise-event-parsed-as-accessor.md`（41，顶层访问器分派缺守卫）、`issue-script-mode-end-statement-rejected.md`（39，`End` 顶层缺守卫，也是逐字同一处 switch）。
- 审计与台账：`tasks\keyword-toplevel-audit\README.md`（§4 D1/D2、§7 修复记录）、`ledger.md`（201 关键字 × 14 形态实测矩阵）。
- 复验过的既有记录面：`tasks\script-mode-coverage-parity\test-plan.md:394` 的「顶层裸 `Yield` 报 BC30800」结论已过期（裸写实为 `BC30451`，`Yield 1` 才是 `BC30800`；修复后两者都走向 `BC36966`）。

