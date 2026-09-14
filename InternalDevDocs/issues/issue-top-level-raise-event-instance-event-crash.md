# 脚本顶层实例 `Event` 在顶层成员里 `RaiseEvent` → 断言终止（接收者是上一提交引用而不是 `Me`）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-13
* 发现场景：崩溃形状穷尽扫描（`tmp\probes\sweep\`，390 探针）

## 触发面

脚本顶层声明实例 `Event`，在**顶层成员**（顶层 `Sub`，或顶层 lambda）里 `RaiseEvent` 该事件：

```vbx
Event e As EventHandler
Sub S()
    RaiseEvent e(Nothing, EventArgs.Empty)
End Sub
AddHandler e, Sub(s As Object, a As EventArgs) Console.WriteLine("H")
S()
```

`Shared Event` + 顶层 `Shared Sub` 里的 raise 走另一条路径（实测 exit `0`，handler 投递 `H`，`tmp\probes\u6bc\raise-shared.vbx`），**不受影响**。

## 症状与实测退出码

Debug 构建下断言终止，`EXITCODE=2148734499`。断言的前提是「事件字段的接收者是 `Me`」；把该断言放宽后同一个形状落到共享发射层的兜底分支 `CodeGen\EmitExpression.vb:206-209` 的 `Throw ExceptionUtilities.UnexpectedValue(expression.Kind)`（exit `2148734499`），栈顶逐字为 `Unexpected value 'PreviousSubmissionReference'`（可行性补丁实测）⇒ 出问题的不是事件本身，而是**未被降级的接收者节点**。

## 根因

**脚本类的非限定成员引用解析成上一提交引用，而 raise 那条路径假定接收者已经是最终形态。**

- `Binding\Binder_Expressions.vb:2570-2576`：脚本类（`IsScriptClass`）的非限定成员引用一律先经 `TryBindInteractiveReceiver`；解析不到才退回 `Me`（`:2582`）。
- `Binding\Binder_Expressions.vb:2615-2634`：`TryBindInteractiveReceiver` 对**提交类**（`TypeKind.Submission`）返回 `BoundPreviousSubmissionReference`（`:2618`）——**同一次提交内也不例外**（`memberDeclaringType.TypeKind = Submission` 即命中）。
- 该节点本应由降级器收口：`Lowering\LocalRewriter\LocalRewriter_PreviousSubmissionReference.vb:13-23` 把它降级成 `Me.<上一提交字段>`（`BoundFieldAccess(Me, _previousSubmissionFields.GetOrMakeField(targetType))`）。
- `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:22-37`：`VisitRaiseEventStatement` 起初只在接收者是 `Nothing` 或 `IsMeReference` 时走整棵调用树降级（该判据现于 `:27-30`，本单元修复后已扩入 `PreviousSubmissionReference`）；`Else` 分支（事件字段访问）不自带降级，接收者形状不对时直接穿透到发射层。

## 预期行为

普通上下文里同一个形状**合法且真的投递**：`Module M : Event e As EventHandler ...` + `AddHandler` 后 `RaiseEvent e`，`vbc.exe` 编译 exit `0`、运行输出 `H`（实测 `tmp\probes\u6bc\raise-ordinary.vb`）。⇒ 判定**修好**（合法形状照常工作），不新增诊断。

## 修复方向

在 `Else` 分支开头对接收者调用降级（`receiver = VisitExpressionNode(receiver)`，落在 `IsWindowsRuntimeEvent` 分支之前），与 `VisitPreviousSubmissionReference` 的降级写法一致，也与该方法的 `If` 分支走整棵调用树的做法一致。`Else` 分支之后的逻辑（临时量、空检查、把调用接收者换成临时量）与接收者形状无关。

## 修复后的新增行为变化

**无新增诊断，无产物形状变化**——该路径从「崩」变为「正常投递」。实测（当前编译器）：`Event e ...` + 顶层 `Sub` raise + `AddHandler` → exit `0` 且 handler 真的被调用（输出 `H`）。`Shared Event` + 顶层 `Shared Sub` 的 raise 路径不变。

回归用例：`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb` 的实例事件用例（既有 `TopLevelSharedEvent_RaiseReachesTheHandler` 的实例版），断言的是**投递计数**而不是「编译通过」。

## 相关

- 同为「顶层形状落到合成提交类上撞不变量」的姊妹问题：`issue-submission-instance-constructor-crash.md`（12）、`issue-top-level-handles-clause-crash.md`（14）、`issue-top-level-mybase-assert.md`（15）。
