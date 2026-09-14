# 脚本跨提交 `Handles` 子句 → `InvalidCastException`（`WithEvents` 变量不是本提交声明的）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-14
* 发现场景：REPL / 提交链（同一轮任务的 390 探针穷尽扫描全是单文件 `.vbx`，未覆盖跨提交路径）

## 触发面

前一提交声明 `WithEvents` 变量，后一提交在顶层方法上写 `Handles` 引用它：

```vbx
' 提交 1
Dim WithEvents hooked As New Widget

' 提交 2
Sub OnIt(s As Object, a As EventArgs) Handles hooked.E
    Console.WriteLine("H")
End Sub
```

最小复现：REPL 逐步提交——`vbi.exe < 输入流`，每条顶层声明各自成一个提交（输入见 `tmp\vortex-logs\script-top-level-crashes-2\u9-repro-input.txt`）。

**同一次提交内**的同形状合法且真的投递（见 `issue-top-level-handles-clause-crash.md`（14））——本 issue 是它的补集语义。

## 症状与实测退出码

`InvalidCastException`：`Unable to cast object of type 'Microsoft.CodeAnalysis.VisualBasic.Symbols.ImplicitNamedTypeSymbol' to type 'Microsoft.CodeAnalysis.VisualBasic.Symbols.SourceNamedTypeSymbol'`，零编译诊断。

抛出点是 `Symbols\Source\SourceMemberMethodSymbol.vb` 的 `isFromBase` 分支；修复前该分支直接写 `DirectCast(Me.ContainingType, SourceNamedTypeSymbol).GetOrAddWithEventsOverride(witheventsProperty)`（HEAD 上位于 `:702`，U3 落地后漂到 `:713`）。

## 根因

**`isFromBase = True` 在提交链里不代表「在基类里」，而覆盖属性机制的前提全部不成立。**

- 提交类的成员查找沿 `PreviousSubmission` 回溯、再落到宿主对象类型（`Binder_Lookup.vb:583-584` / `:870-916` / `:920-926`），于是**前一提交（或宿主对象）声明的 `WithEvents` 变量**被当成继承成员返回 ⇒ `isFromBase = True`。
- 覆盖属性路线要求「有可覆盖的基属性 + 赋值虚派发 + 挂钩体在其所属提交里」，而提交类 `base=<nothing>`（`ImplicitNamedTypeSymbol.vb:59`）。**最小回退实现（`DirectCast` 换 `TryCast` 后退回非覆盖路径）实测编译通过但 handler 计数恒 `0`** ⇒ 静默不投递比报错更糟。
- 因此判据改为：`TryCast(Me.ContainingType, SourceNamedTypeSymbol)` 失败 ⇒ 报新码并 `Return Nothing`（现于 `SourceMemberMethodSymbol.vb:707-711`）。

## 预期行为

**报错**（判定原则第二条出路）。依据是外生决议，非本单元自创：

- `vblang\spec\type-members.md:843` 逐字：「The first identifier must be an instance or shared variable in the containing type that specifies the `WithEvents` modifier or the `MyBase` or `MyClass` or `Me` keyword; otherwise, a compile-time error occurs.」——规范自身就把该情形定义为**编译期错误**；提交链不构成 containing type。
- `meetings\meeting-with-events-in-submissions.md:120`（RESOLUTION **R5**）逐字：「**子情形 (iii) 跨提交走诊断；P-A3（覆盖属性路线）否决**」。
- `proposals\proposal-with-events-in-submissions.md:439`：「既有族**不可直接复用** … ⇒ **需新增码**，触发 VB 资源 + xlf 13 语言同步义务」。

普通 VB 里没有对应形状——`Handles` 只能引用**本类型或基类**的成员，提交链不构成继承 —— 故判定原则里「同形状普通上下文合法就该修好」这条**对它不适用**。

## 修复后的新增行为变化

**新增一枚诊断码 `BC37343`（`ERR_WithEventsVariableNotInContainingType`）**：

- 位置锚在 `Handles` 的**容器标识符**上（实测 `line=0 cols=53..59 spanText='hooked'`）；**不再抛 `InvalidCastException`**。
- REPL 里 `exitCode = 0` 且会话存活（失败提交不终止会话，下一条提交照跑）。
- 同一次提交内的形状（issue 14）不受影响，仍正常投递。

注册链：`Errors.vb:1818`（`ERR_WithEventsVariableNotInContainingType = 37343`；`ERR_NextAvailable` 推进为 `37344`，`:1820`）→ `ErrorFacts.vb:1564`（非 build-only 清单）→ `VBResources.resx:4733`（英文文案）→ 13 份 xlf 同步（每份 `+46 −0`，本任务三枚新码合并计入）。

回归用例（`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`，四条）：
- `:844` `CrossSubmissionHandles_IsReportedInsteadOfTerminatingTheProcess`——诊断 id + 位置（第 0 行）+ 宿主只收到 `CompilationErrorException`；
- `:870` `HostObjectWithEventsHandles_IsReportedInsteadOfTerminatingTheProcess`——宿主对象（`globalsType`）上的 `WithEvents` 变量同判定；
- `:891` `ReplCrossSubmissionHandles_IsReportedAndTheSessionContinues`——REPL 两步提交，诊断出现且会话存活（下一条提交仍执行）；
- `:918` `SameSubmissionHandles_StillDelivers`——**控制锁**：同提交形状仍投递（计数 = 1）。

**判别性实测**：验证者自建精确对照（整树 robocopy 后只撤回本单元那一个 hunk、重编 50 秒）跑真测试类 ⇒ 该类 **44 条中恰 3 条失败 / 41 通过**，失败三条逐字为原 `InvalidCastException`、恰是三个跨提交用例；`SameSubmissionHandles_StillDelivers` 两侧都过。

## 相关

- 同一次提交内的形状：`issue-top-level-handles-clause-crash.md`（14）。
- 同为「顶层形状落到合成提交类上撞不变量」的姊妹问题：`issue-submission-instance-constructor-crash.md`（12）、`issue-top-level-raise-event-instance-event-crash.md`（13）、`issue-top-level-mybase-assert.md`（15）。
- `Handles MyBase.E` 在提交类上的语义是会议 OPEN QUESTION（`meeting-with-events-in-submissions.md` R9），本 issue 不代裁。
