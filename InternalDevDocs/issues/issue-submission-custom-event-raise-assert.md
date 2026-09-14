# 脚本提交类自定义 `Custom Event` 在成员体内 `RaiseEvent` → 断言终止（接收者不是事件字段）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-14
* 发现场景：U7 脚本模式符合性矩阵铺「声明 · `Event`（字段式 / 自定义 / `Shared`）」这一格时撞出（最小复现 `tmp\probes\u7\custom-event-raise.vbx`），按约定摘出矩阵、单独立项。

## 触发面

提交类里声明**自定义** `Custom Event`（带访问器块），在成员体内 `RaiseEvent` 它：

```vbx
Private _handlers As System.EventHandler
Custom Event Changed As System.EventHandler
    AddHandler(value As System.EventHandler)
        _handlers = CType(System.Delegate.Combine(_handlers, value), System.EventHandler)
    End AddHandler
    RemoveHandler(value As System.EventHandler)
        _handlers = CType(System.Delegate.Remove(_handlers, value), System.EventHandler)
    End RemoveHandler
    RaiseEvent(sender As Object, e As System.EventArgs)
        If _handlers IsNot Nothing Then _handlers(sender, e)
    End RaiseEvent
End Event
Sub RaiseIt()
    RaiseEvent Changed(Nothing, System.EventArgs.Empty)
End Sub
```

**边界**：只声明 + `AddHandler` 而**不** raise ⇒ 干净通过；把同样的 raise 放进**顶层 lambda** ⇒ 同样崩（走另一条方法体，接收者形状相同）。

## 症状与实测退出码

Debug 构建下断言终止，`EXITCODE=2148734499`，栈顶 `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:33` 的 `Debug.Assert(receiver.Kind = BoundKind.FieldAccess)`（U12 落地前该断言在 `:30`）。宿主侧同形状抛 `Xunit.Sdk.TraceAssertException`（xunit 装了 TraceListener ⇒ 只判该用例失败，不连坐同批）。

## 根因

**`Else` 分支的形状前提是「接收者是事件字段访问」，而自定义事件根本没有后备字段。**

- 自定义事件的 raise 调的是**事件自己声明的 `RaiseEvent` 访问器**，所以调用接收者是**实例引用**、不是事件字段。
- 提交类里非限定成员引用又一律解析成 `BoundPreviousSubmissionReference`（`Binding\Binder_Expressions.vb:2615-2618` 的 `TryBindInteractiveReceiver`）⇒ `Else` 分支的前提**双重不成立**，断言当场终止。插桩实测接收者真实形状：`raiseCallReceiver=PreviousSubmissionReference`。
- 与 issue 13（U2，字段式实例事件）**不是同一个断言**：U2 处理的是「接收者**是**事件字段访问、需把该 receiver 降级」；本条撞的是「**压根不是**字段访问」，即 `Else` 分支本身不适用。

## 预期行为

普通上下文里同一个形状**合法且真的投递**：普通类里 `Custom Event` + 成员体内 `RaiseEvent`，`vbc.exe` 编译 exit `0`、handler 真的被调用（实测 `tmp\probes\u12\custom-event-ordinary.vb`，`COUNT=2`）⇒ 判定**修好**，不新增诊断。

## 修复方向

把 `If` 的判据由「接收者是 `Nothing` 或 `IsMeReference`」**扩宽**到 `OrElse receiver.Kind = BoundKind.PreviousSubmissionReference`（现于 `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:27`），让自定义事件走 `If` 分支（整棵调用树降级）——**普通类里走的正是这条 path**。U2 的 `receiver = VisitExpressionNode(receiver)` 与该分支保留的 `Debug.Assert` 原样不动。

## 修复后的新增行为变化

**无新增诊断，无产物形状变化**——该路径从「断言终止」变为「正常投递」。实测（当前编译器）：脚本侧与普通类侧同为 `COUNT=2`。

回归用例（`Scripting\VisualBasicTest\ScriptModeConformanceTests.vb`）：
- `:427` `TopLevelCustomEventRaise_ReachesTheHandler`——断言返回值 `2`（handler 真被调用两次）；
- `:456` `TopLevelCustomEventRaiseInLambda_ReachesTheHandler`——同一 raise 放进顶层 lambda，断言返回值 `1`。
- 既有 `:399` `TopLevelCustomEvent_AccessorsAndRegistration_Conform` 的 XML 注释里那段「实例事件的 raise 路径不可达、故意不断言」的说明已删除（该缺口由本单元关闭）。

**判别性实测**：把改前 dll（`d9534ebb…`）换进测试 bin 目录的副本 ⇒ 声明符合性矩阵 22 个方法 **20 过 2 挂**，挂的恰是新增这两条，消息逐字为 `TraceAssertException … receiver.Kind = BoundKind.FieldAccess`（`line 30`）。

## 相邻形状（登记，不属本单元）

- **跨提交**自定义事件 raise（前一提交声明事件、后一提交 raise）：在降级前就退回 `BoundBadStatement`（`Binder_Statements.vb:2621-2637` 的 `ERR_CantRaiseBaseEvent = 30029` 分支）——REPL 实测 session exit `0` + `BC30029`、**不崩**（**实锤**）。
- **宿主对象上**的 `Custom Event`：同属该分支，但**无运行证据**（REPL / `vbi.exe` 不传 globals）⇒ 只作**源码推断（推测）**。

## 相关

- 字段式实例事件 raise：`issue-top-level-raise-event-instance-event-crash.md`（13）——同一文件、相邻断言，根因与修法不同。
- 矩阵维度一 #18 这一格由本单元补回（`ScriptModeConformanceTests.vb` 的声明符合性表）。
