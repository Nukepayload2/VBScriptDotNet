# 脚本顶层方法带 `Handles` 子句 → 断言终止 `Unexpected value 'Submission'`

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-13
* 发现场景：崩溃形状穷尽扫描（`tmp\probes\sweep\`，390 探针）

## 触发面

顶层 `WithEvents` 字段 + 带 `Handles` 子句的顶层方法（实例与 `Shared` 两种）：

```vbx
Class Hook
    Event E As EventHandler
    Sub Fire()
        RaiseEvent E(Me, EventArgs.Empty)
    End Sub
End Class
Dim WithEvents hooked As New Hook
Sub OnIt(s As Object, a As EventArgs) Handles hooked.E
    Console.WriteLine("H")
End Sub
hooked.Fire()
```

## 症状与实测退出码

断言终止，`EXITCODE=2148734499`，栈顶逐字为 `Unexpected value 'Submission' of type 'Microsoft.CodeAnalysis.TypeKind'`——由 `ExceptionUtilities.UnexpectedValue` 抛出，**零编译诊断**。

## 根因

**`Handles` 的宿主类型枚举里没有 `Submission`。**

`Symbols\Source\SourceMemberMethodSymbol.vb:773-784` 的 `Select Case ContainingType.TypeKind` 只列了 `Class` / `Module` 等分支，`Submission` 落 `Case Else` 的 `Throw ExceptionUtilities.UnexpectedValue(ContainingType.TypeKind)`（`:782-783`）。

提交类**是**类容器（`Symbols\NamedTypeSymbol.vb:691-695` 的 `IsSubmissionClass` = `TypeKind = TypeKind.Submission`），而 `Handles` 的 Hookup 宿主选取（同文件 `:790-807`）只用到「实例构造器列表非空」与「共享构造器」两件事，对提交类同样成立：实例事件取 `ContainingType.InstanceConstructors(0)`（`:800-805`），共享事件取 `SharedConstructors(0)`（`:797`）。⇒ 判据只是漏列了一个枚举值。

## 预期行为

普通上下文里同一个形状**合法且真的投递**：`vbc.exe` 编译 exit `0`、运行输出 `H`（实测 `tmp\probes\u6bc\handles-ordinary.vb`）。⇒ 判定**修好**，不新增诊断。

## 修复方向

把 `TypeKind.Submission` 并入合法分支（`SourceMemberMethodSymbol.vb:778`）。改动面一处，普通类/模块的 `Handles` 行为不动。

## 修复后的新增行为变化

**同一次提交内的形状：无新增诊断，无产物形状变化**——该形状从「断言终止」变为「正常编译并投递事件」。实测（当前编译器）：顶层**实例** `WithEvents` + 顶层实例 `Handles` → exit `0`，handler 计数为 1（输出 `H`）。普通类/模块的 `Handles` 对照不变（exit `0`）。**`Shared` 变体的投递取决于挂钩宿主何时执行**——它落在提交类的共享构造器上，见下节与 `issue-submission-shared-constructor-not-run.md`（18）。

回归用例：`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb` 的同族用例，覆盖「实例 `WithEvents` + `Handles` 投递计数」与「`Shared WithEvents` + `Handles`」，断言的是**投递真的发生**（计数 = 1），不只是编译通过。

## 边界：`WithEvents` 变量不是本提交声明的

`Handles` 子句合法性的前提是「第一个标识符必须是**包含类型里**的实例或共享变量」（`vblang\spec\type-members.md:843` 逐字：「The first identifier must be an instance or shared variable in the containing type … otherwise, a compile-time error occurs」）。提交链没有继承，于是：

- **变量在本提交里声明**（本节形状）⇒ 子句受支持，hookup 由该变量的**合成 setter** 承担；
- **变量来自上一提交或宿主对象**（`Handles hooked.E` 而 `hooked` 声明在别处）⇒ 提交类只是**可见**它、并未声明它，判据不满足 ⇒ 报 **`BC37343`**（`Errors.vb:1818` 的 `ERR_WithEventsVariableNotInContainingType`，判据落在 `SourceMemberMethodSymbol.vb:707-711` 的 `TryCast(Me.ContainingType, SourceNamedTypeSymbol)` 失败分支）。

实测：跨提交形状报 `BC37343`、位置锚在 `Handles` 的**容器标识符**上、不再抛 `InvalidCastException`（测试 `Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb:844` / `:867` / `:888`）。**这是该形状的行为变化**：此前它直接终止编译器进程。

## 相关

- 同为「顶层形状落到合成提交类上撞不变量」的姊妹问题：`issue-submission-instance-constructor-crash.md`（12）、`issue-top-level-raise-event-instance-event-crash.md`（13）、`issue-top-level-mybase-assert.md`（15）。
- 同一次提交里的形状允许**共享**事件吗：允许（`Shared Event` + `Shared Sub … Handles` 编译通过），但该形状的 hookup 由共享 `WithEvents` 变量的属性 setter 承担（`SourceMemberMethodSymbol.vb:792-793`），而调用它的是共享字段初始化器 ⇒ 依赖提交类的共享构造器执行，而该构造器**不由提交构造或顶层语句触发、只由共享字段的读 / 写触发** ⇒ 见 `issue-submission-shared-constructor-not-run.md`（18）。
