# 脚本顶层 `Shared` 字段初始化器含 `Await` → 编译器断言终止

**状态**：Open（2026-09-12 登记）
**证据等级**：**已运行**（Debug `2.0.0-Beta+5816a5c`，本机实测，含非共享对照）

## 症状

`.vbx` 顶层写共享字段并用 `Await` 初始化：

```vb
Imports System.Threading.Tasks

Shared Dim x = Await Task.FromResult(1)
Console.WriteLine(x)
```

Debug 构建**直接终止进程**（`EXITCODE=35`）：

```text
Process terminated.
Assertion failed.
Unexpected value 'AwaitOperator' of type 'Microsoft.CodeAnalysis.VisualBasic.BoundKind'
   at Microsoft.CodeAnalysis.ExceptionUtilities.UnexpectedValue(Object o) … ExceptionUtilities.cs:line 24
   at …CodeGenerator.EmitExpressionCore(…) … EmitExpression.vb:line 209
   at …CodeGenerator.EmitDirectCastExpression(…) … EmitConversion.vb:line 322
   at …CodeGenerator.EmitAssignmentExpression(…) … EmitExpression.vb:line 1819
   at …MethodCompiler.GenerateMethodBody(…) … MethodCompiler.vb:line 1673
```

**对照（已运行）**：同形状但**非共享**的顶层字段

```vb
Dim y = Await Task.FromResult(2)
Console.WriteLine("INSTANCE-OK " & y)
```

正常执行，输出 `INSTANCE-OK 2`、`EXITCODE=0`。⇒ 崩溃是 **`Shared` 特有**。

## 根因

`AwaitOperator` **存活到 codegen**，而 `EmitExpressionCore` 对未列出的 `BoundKind` 落 `Case Else` 抛 `UnexpectedValue`——`Compilers\VisualBasic\Portable\CodeGen\EmitExpression.vb:206-209`（`:209` 为 `Throw ExceptionUtilities.UnexpectedValue(expression.Kind)`；`ExceptionUtilities.UnexpectedValue` 本身是 `Debug.Assert(false, …)` 后返回 `InvalidOperationException`，见 `Dependencies\Contracts\ExceptionUtilities.cs:18-26`，故 Debug 下表现为断言终止）。

`Await` 未被 async 重写消化：**共享字段的初始化器不落在 async 的 `<Initialize>` 里**。这与 `issue-submission-shared-field-initializer-typeload.md`（issue 05）**同族**——两者都出自「共享提交构造器」这条路（`SynthesizedSubmissionConstructorSymbol` 无条件带实例版形参、`MethodKind` 因此报 `SharedConstructor`，根因链见 issue 05 的更正节）。

> **断言状态**：上述「初始化器落入**非 async** 的共享构造器」是**推断**——未逐帧验证共享字段初始化器的合成落点。已实测的是：共享形状崩、非共享形状不崩，且崩在 codegen 拿到 `AwaitOperator` 处。

## C# 对照

**C# 有对应诊断，且正是为这个场景设的**：CS8100 = `ERR_BadAwaitInStaticVariableInitializer`（「静态字段初始化器不许 `await`」），在**绑定期**报错（`Compilers\CSharp\Portable\Binder\Binder_Await.cs:161-166`）。VB 侧**无对应诊断**，于是同一个程序形状从「报错」变成「崩进程」。

## 预期行为

应当**报诊断**（移植 CS8100 的语义，落在绑定期），而不是终止编译器进程。

## 修复方向

1. **绑定期加诊断**：脚本顶层 `Shared` 字段（或更一般地，任何初始化器落入非 async 合成方法的字段）的初始化器含 `Await` → 报错。
2. **与 issue 05 一并评估**：两者同属「共享提交构造器」这条路的缺口。若按 `decisions.md` **D5**（C# 已证实例 1）拆出独立的**静态构造器符号**（C# 的 `SynthesizedStaticConstructor`），本条的落点会更清晰。

## 未复现 / 未查

- Release 构建下的表现（仅跑了 Debug）。
- 共享**属性**初始化器；共享字段初始化器含 `Await` 以外的异步形状（`Await` 藏在 lambda／嵌套表达式内）。
- 非脚本模式下是否可达（顶层 `Shared` 是脚本特有形状）。
- 失败提交的状态隔离差异。

## 相关

- `issue-submission-shared-field-initializer-typeload.md`（issue 05，同族的 `TypeLoadException`）
- `decisions.md` **D5**（C# 已证实例 1：提交构造器应拆成两个符号）
- 复现探针已跑并清除（`tmp\extprobe\` 恢复为空）

## 更正（2026-09-12，两处）

1. **「崩溃是 `Shared` 特有」这一结论只在「顶层」这一行成立，不是这个程序形状的全貌。** 实测（2026-09-12，另一轮探针）：**嵌套类型**（普通类，非脚本类）里字段/属性的初始化器含 `Await` **同样崩**（`EXITCODE=35`、同一 `EmitExpression.vb:209` 抛点），且**与 `Shared` 无关**——实例字段、实例属性、共享字段三种形状全部复现。那条路的根因**不是**本 issue 的「绑定期放行」，而是「诊断 BC36937 报了但不阻止发射」，已登记为 **issue 09**（`issue-initializer-diagnostic-does-not-gate-emit.md`）。本 issue 的「对照」节仍有效（同一份源码在顶层实例/共享两侧的对照），但**不能**推广成「只有 `Shared` 会崩」。
2. **「共享不能 `Await`」的机制描述补一条**：本 issue 只说「`Await` 未被 async 重写消化」，未说明为什么。实际第一道门是 `Lowering\AsyncRewriter\AsyncRewriter.vb:364-368`（`If Not method.IsAsync Then Return AsyncMethodKind.None`）——`.cctor` 的 `IsAsync` 恒 False（`Symbols\SynthesizedSymbols\SynthesizedMethodBase.vb:195-200`），`LocalRewriter` 又刻意原样保留 `AwaitOperator`（`Lowering\LocalRewriter\LocalRewriter.vb:799-830`）。语言规则 BC36950（`SourceMethodSymbol.vb:523-525`）只挡**用户手写**的 `Async Shared Sub New`，**不**是本崩溃的原因 ⇒ 「共享不能 `await`」是**实现缺口**，不是 CLR / 语言规则；完整调查见 `tmp\meetings\script-extension-methods\investigation-shared-initializer-async.md`，候选重排见 `proposals\proposal-submission-shared-members.md`。

3. **「修复方向」节的两条候选已被重排替代（本 issue 的目标不再是「只能报错」）。** 现记为：**主候选 丙**——把共享字段/属性的初始化器**改道并入已有的异步 `<Initialize>`**，此时本场景的 `Await` **合法**（不再需要任何新诊断），且该路线**同时消掉 issue 05**；**甲**（拆出无参共享构造器符号）与 **乙**（移植 CS8100 到绑定期）**只在 丙 被否时才需要**。⇒ 当 丙 被否时，本节原第 1 条（绑定期加诊断）= 乙 仍是唯一能把「崩」变成「报错」的修法；当 丙 被采纳时，本节原第 1、2 条均**不再适用**。**但无论 丙 是否采纳，都修不了面孔 ②**（嵌套类型那条，属 **issue 09** 的「诊断不 gate 发射」，与本 issue 的「绑定期放行」是两个根）。
