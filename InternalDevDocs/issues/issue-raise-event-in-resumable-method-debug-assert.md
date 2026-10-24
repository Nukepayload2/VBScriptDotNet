# `RaiseEvent` 在 resumable 方法体内触发状态机断言的 Debug 构建失败（上游共享代码）

* 状态：**🔒 上游来源缺陷，本 fork 不修**（命中 `decisions.md` **D7 例外 (b)**：缺陷在上游共享代码里且上游亦未修 ⇒ 目标 `dotnet/roslyn`，改它只增合并冲突面；先例＝问题单 25 / 35）
* 发现日期：2026-10-23
* 发现场景：关键字 × 顶层容器审计复验 `RaiseEvent` 端到端时撞上（`tasks\keyword-toplevel-audit\` §7.3）

## 触发面

`RaiseEvent` 写在**任何 resumable 方法体**里 —— `Async` 方法、`Iterator` 方法，或者脚本顶层（顶层语句是异步初始化器的方法体）。
事件是**字段式事件**（`Event E As EventHandler`），接收者走字段访问那条降级路径：

```vbnet
Class K
    Event E As System.EventHandler
    Async Function F() As Task
        RaiseEvent E(Nothing, System.EventArgs.Empty)
    End Function
End Class
```

`Iterator Function` 里同形同样触发；非 async 的普通 `Sub` 里不触发。**与脚本无关**：上面这段是脚本内声明的普通类，
不经过任何脚本特有的绑定路径；把它写进普通 `.vb` 工程，只要编译器本身是 Debug 构建，同样会走到同一条断言。

## 症状与实测

**Debug 构建的编译器**在发射阶段 `FailFast`：

```
Process terminated.
Assertion failed.
KIND=LoweringTemp NAME= TYPE=System.EventHandler
   at Microsoft.CodeAnalysis.VisualBasic.StateMachineRewriter`1.StateMachineMethodToClassRewriter.PossibleStateMachineScope(...)
      in ...\Lowering\StateMachineRewriter\StateMachineRewriter.StateMachineMethodToClassRewriter.vb:line 264
   at ...StateMachineMethodToClassRewriter.VisitBlock(...) line 244
```

（`KIND=` 前缀来自一次临时诊断输出，用来确认被 hoist 的是哪个局部；该输出已还原，未入库。）

**Release 构建完全正常**：同一形状 `.vbx` 在发布版 `vbi` 下 `exit 0`，handler 真的被调用；顶层 `RaiseEvent` 脚本同样 `exit 0` 并输出期望值。
`Debug.Assert` 在 Release 被条件编译掉，所以产品（随包分发的是 Release 编译器）不受影响。

## 根因

raise 的降级自己造了一个**短生命期**临时变量，而状态机重写器要求被 hoist 的局部必须是「长生命期」种类：

- `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:54`：`New SynthesizedLocal(..., SynthesizedLocalKind.LoweringTemp)` ——
  为「先取事件字段、判空、再 invoke」造的临时量（`:43-94` 的 `BoundBlock` 把它登记为块内局部）。
- `Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb`：hoist 谓词是「除 `ConditionalBranchDiscriminator` 之外的合成本地一律 hoist」
  （Debug 构建下更宽：再把用户局部/参数全量纳入），该临时量因此进 `_hoistedVariables`。
- `StateMachineRewriter.StateMachineMethodToClassRewriter.vb:264`：`Debug.Assert(local.SynthesizedKind.IsLongLived())` ——
  只要被 hoist 的局部不是长生命期种类就终止。`LoweringTemp` 不在 `IsLongLived()` 集合里。

## 上游对照（代码事实 ✔，运行结论 ✎）

| 文件 | 与参考上游 Roslyn（`{{Roslyn}}`）的差异 |
|---|---|
| `StateMachineRewriter.StateMachineMethodToClassRewriter.vb` | **逐字相同**（676/676 行，`Compare-Object` 0 差异）——断言本身相同 |
| `StateMachineRewriter.vb` / `StateMachineTypeSymbol.vb` / `SynthesizedContainer.vb` / `SynthesizedStateMachineMethod.vb` / `StateMachineFieldSymbol.vb` | 0 差异 |
| `LocalRewriter_RaiseEvent.vb` | 仅「接收者处理」不同（fork 支持 submission 的 previous-submission reference，并 `VisitExpressionNode(receiver)`）；**造 temp 的那一行与上游相同** |
| `IteratorAndAsyncCaptureWalker.vb` | fork 的差异是 ref-like（ByRef-like）检查与其诊断；上游 hoist 谓词**更宽**（不含 ref-like 排除） |

⇒ **✎ 推测（未实跑上游）**：上游的 hoist 集合是 fork 的超集、断言逐字相同，因此上游 Debug 构建大概率同样会终止。
把它变成实锤需要：用 `{{Roslyn}}` 的 Debug 配置构建 `Microsoft.CodeAnalysis.VisualBasic`，以该编译器跑上面那段 `Async Function`，
观察是否 `Assertion failed`。本 fork 不做这一步，因为不影响本仓的处置结论（见下）。

## 为什么本 fork 不修

1. 断言与触发路径都在**上游共享代码**里（`StateMachineRewriter` 文件逐字相同；`LocalRewriter_RaiseEvent.vb` 的 temp 行相同），
   命中 `decisions.md` D7 例外 (b)：目标 `dotnet/roslyn`，本 fork 改它只增加上游合并冲突面。先例＝问题单 25（`CommonTypeNameFormatter` 丢命名空间）、问题单 35（并发绑定断言）。
2. 两个候选修法都要求偏离上游：
   - 把 rise 的 temp 换成长生命期种类（`SynthesizedLocalKind.LongLived`）——这会让状态机为它生成调试作用域，emit 形状随之上游分叉；
   - 把断言收窄成「只对真正跨 await 的局部断言」——等于在本 fork 修上游的断言，且同样改到逐字相同的文件。
3. 产品面影响为零：随包发布的是 Release 编译器（`Debug.Assert` 条件编译掉），语义实测正确。受影响的只有本仓的 Debug 构建（例如 Debug 版 `vbi`）。

## 影响面与已核事实

- 触发条件：**Debug 构建的编译器** + `RaiseEvent` 位于 resumable 方法体内 + 字段式事件；`Custom Event` 走的是另一条（`If`）分支，不在本条内。
- 产品路径无影响：Release 编译 ── 已运行：脚本内普通类 `Async Function` / `Iterator Function` 内 raise、脚本顶层 raise 三个形状均 `exit 0` 且投递正确。
- 上游测试面大概率没覆盖到这个组合（◇ 抽查）：上游测试树 58 个文件含 `RaiseEvent`，其中 15 个也含 `Async`/`Iterator`，抽查其中 5 个未发现「async/iterator 方法体内 raise」的用例
  ⇒ 这条断言即便在上游存在，也不会被上游 CI 在 Debug 下抓到。

## 与本仓既有 issue 的边界

- 问题单 13 / 20 是 `LocalRewriter_RaiseEvent.vb` 里的**另两条**断言（`receiver.Kind = BoundKind.FieldAccess`）与接收者未降级，均已 **Fixed**；本条是状态机重写器对 hoist 生命期的断言。
- 问题单 41（顶层 `RaiseEvent` 被读成访问器）修好之后，顶层 raise 会走到本条描述的 resumable 路径；因此**Debug 版 `vbi`** 跑顶层 raise 脚本会终止。
  Release 不受影响，这也是本条不阻塞 41 落地的原因。

## 若上游要修（建议方向，供 issue 转发用）

- 首选：把 `LocalRewriter_RaiseEvent.vb` 的临时量改为长生命期种类，并在状态机场景下验证 emit 与调试作用域；
- 或：让 `PossibleStateMachineScope` 只对被判定需要跨挂起的局部断言（当前实现对 Debug 构建的全量 hoist 策略过敏）。

## 相关

- `tasks\keyword-toplevel-audit\README.md` §7.3（本条的现场记录与观测步骤）。
- 问题单 13、20（raise 的另两条断言，已修）、41（顶层 raise 的语句化）、25 / 35（D7 例外 (b) 的先例）。
