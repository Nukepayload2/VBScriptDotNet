# 脚本顶层 `Try`/`Catch`/`Finally`/`SyncLock` 里的 `Await`：`BC36943` 判据未生效（Catch/Finally → 编译器 `NullReferenceException`；SyncLock → 静默产出运行期坏产物）

* 状态：**Fixed**（48d8edb）
* 发现日期：2026-09-13（同日扩展触发面：补入第三子形状 `SyncLock`）
* 发现场景：调查「`.vbx` 顶层变量做成 locals」的可行性时顺带撞见

## 症状

`.vbx` 脚本顶层写含 `Await` 的 `Try`/`Catch`/`Finally`：

```vbx
Imports System.Threading.Tasks
Try
    Await Task.Delay(1)
    Console.WriteLine("TRY")
Catch ex As Exception
    Await Task.Delay(1)
    Console.WriteLine("CATCH")
Finally
    Await Task.Delay(1)
    Console.WriteLine("FINALLY")
End Try
```

实测（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`，自报 `2.0.0-Beta+5816a5c`）：

```
System.NullReferenceException: Object reference not set to an instance of an object.
  + Microsoft.CodeAnalysis.CodeGen.ILBuilder.BlockedBranchDestinationSlow(...) — CodeGen\ILBuilder.cs : 413
  + ... MarkReachableFromBranch : 353 / MarkReachableFrom : 337 / MarkReachableBlocks : 276
  + ... RealizeBlocks : 870 / Realize : 207
  + Microsoft.CodeAnalysis.VisualBasic.CodeGen.CodeGenerator.GenerateImpl() — VisualBasic\Portable\CodeGen\CodeGenerator.vb : 172
  ...
The script has error. See the output for more information.
EXITCODE=3
```

**输出里没有任何编译诊断**——不是「报了错再崩」，是**直接崩**。

## 第三子形状：`SyncLock`（不崩，改为编译通过 + 运行期 `SynchronizationLockException`）

触发面比本文初版登记的 `Try`/`Catch`/`Finally` 宽：**`BC36943` 这条判据本身就同时点名 `SyncLock`**（`Errors.vb:1572` 的 `ERR_BadAwaitInTryHandler`，消息逐字为「不能在『Catch』语句、『Finally』语句或『SyncLock』语句中使用『Await』」；既有用例覆盖 `SyncLock` 头表达式 `Compilers\VisualBasicSemanticTest\Semantics\AsyncAwait.vb:4003-4007` 与块体 `:4017-4022`），而**脚本顶层这条判据同样不生效**。子形状划分：一 = `Catch`、二 = `Finally`（见上节），**三 = `SyncLock`**。

### 探针（**已运行**，2026-09-13 本机复测）

```vbx
Imports System.Threading.Tasks
Dim gate As New Object
SyncLock gate
    Await Task.Delay(1)
    Console.WriteLine("IN-LOCK")
End SyncLock
Console.WriteLine("AFTER")
```

实测（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`；本次构建自报 `2.0.0-Beta+3724b98e…`，与本文件其余痕迹引用的 `+5816a5c` 非同一构建）：

```
IN-LOCK
System.Threading.SynchronizationLockException: Object synchronization method was called from an unsynchronized block of code.
  + System.Threading.Monitor.Exit_Slowpath(System.Threading.Monitor.LeaveHelperAction, Object)
  + Submission#0.VB$StateMachine_1_<Initialize>.MoveNext() — <探针>.vbx : 5
  ...
The script has error. See the output for more information.
EXITCODE=24
```

⇒ **编译期零诊断**（不是「报了错再崩」，也不是本页上节那种编译器崩溃），脚本正常启动、`Await` 之后的受保护代码照常执行（`IN-LOCK` 已打印），**错误推迟到退出 `SyncLock` 块**时才以运行期异常爆出。

### 与 Catch/Finally 那一支同源：同一条判据未生效（**已检查**）

- 判据落点：`VB\Binding\Binder_Statements.vb:602-610`（`CheckOnErrorAndAwaitWalker.VisitAwaitOperator` 见 `_isInCatchFinallyOrSyncLock` 为真 ⇒ 报 `ERR_BadAwaitInTryHandler`）；置位点：`Catch`/`Finally` 在 `:533-539`、**`SyncLock` 在 `:582-589`**、`Using` 在 `:593-600`。
- 该 walker 只在 `BindMethodBlock`（`:291`，调用点 `:330`）里跑，而**脚本顶层语句不在 `<Initialize>` 的方法体里**：`SynthesizedInteractiveInitializerMethod.vb:135-142` 的 `GetBoundMethodBody` 返回的是只含 `BoundLabelStatement(ExitLabel)` 的**空壳块**，顶层语句走的是初始化器绑定路径（诊断袋问题见 `issue-initializer-diagnostic-does-not-gate-emit.md`）。**已检查（源码）+ 已运行（三种子形状都不报 BC36943）。**

⇒ 与 Catch/Finally 一支**同源**（同一条 `BC36943` 判据 + 同一个「walker 看不到顶层语句」的缺口），**差别只在后果**：Catch/Finally 形状留下悬空分支目标 ⇒ 发射期 NRE；`SyncLock` 的形状发射得出来 ⇒ 缺陷被推到运行期。

### 为什么「编译通过」而不是崩（**推测**）

`SyncLock` 体在 IL 上是**隐式的 `Try`…`Finally`（`Monitor.Enter` / `Monitor.Exit`）**，`Await` 落在该隐式 `Try` 块内 ⇒ 属合法的 `Await` 位置，异步重写器照常把 `<Initialize>` 改成状态机。于是 `Await` 的续体在**别的线程**上恢复执行，块尾对该线程调用 `Monitor.Exit` ⇒ `SynchronizationLockException`（栈顶 `Monitor.Exit_Slowpath`，宿主为 `<Initialize>` 状态机）。这也正是语言要禁掉 `SyncLock` 内 `Await` 的理由。

### 后果比 Catch/Finally 更隐蔽（这是本子形状单列的理由）

1. **编译期没有任何提示**：Catch/Finally 至少当场崩（容易被发现），这里 `vbi script.vbx` 照常编译、照常启动，坏产物只在运行到该语句时才暴露（**实锤**：本节探针）。
2. **锁保护期被绕过**：`Await` 之后的受保护语句在**不持锁**的线程上继续执行（探针里 `IN-LOCK` 在被保护区内照常打印，之后才在块尾抛异常）⇒ 互斥语义已被破坏；原线程仍持锁（**推测**：由 `Monitor.Exit` 抛「调用线程未持有锁」反推）。生产里这种「不报错、偶尔坏数据」比崩溃更难定位。

## 预期行为

应报 **`BC36943`**。该诊断确实存在，且在同一形状写成普通异步方法时正常工作（见下）。

三个子形状（`Catch` / `Finally` / **`SyncLock`**）的预期一致：**一条 `BC36943`、不产生可运行产物**。判据既有、**无需新码**；修复落在「让脚本顶层语句也过 `CheckOnErrorAndAwaitWalker`」这一处（蓝图见 `../tasks/script-top-level-crashes/design-detailed.md` §F10），验收须**显式覆盖 `SyncLock` 子形状**——它今天不崩，最容易在「只看崩不崩」的验收里漏网。

## 判别性对照（**已运行**）

同一形状放进**普通异步方法**：

```vbx
Imports System.Threading.Tasks
Class C
    Async Function F1() As Task
        Try
            Await Task.Delay(1)
        Catch ex As Exception
            Await Task.Delay(1)
        Finally
            Await Task.Delay(1)
        End Try
    End Function
End Class
Console.WriteLine("COMPILED")
```

实测：

```
error BC36943: 「不能在『Catch』语句、『Finally』语句或『SyncLock』语句中使用『Await』。」(×2，指向 Catch 与 Finally 里那两处)
EXIT=1
```

⇒ **普通异步方法正常报 BC36943、不崩**；**脚本顶层一条都不报**（`Catch`/`Finally` 崩，`SyncLock` 静默产出坏产物）。**脚本模式特有**。（`SyncLock` 子形状的同类对照同码：普通异步方法里报 BC36943，见 `AsyncAwait.vb:4003-4007` / `:4017-4022`。）

## 根因（**已检查**：判据未生效的确切位置）

`BC36943` 的判据在脚本顶层那条路上没有生效，含 `Await` 的 `Try`/`Catch`/`Finally`/`SyncLock` 因而**活到了发射期**（Catch/Finally 撞共享 `CodeGen.ILBuilder` 的空引用）或**活到了运行期**（`SyncLock`，见上）。

判据本身住在 `CheckOnErrorAndAwaitWalker`（`VB\Binding\Binder_Statements.vb:602-610`），它**只在 `BindMethodBlock` 里被调用**（`:291` 定义、`:330` 调用点）；而脚本顶层语句不在 `<Initialize>` 的方法体里——`SynthesizedInteractiveInitializerMethod.vb:135-142` 的 `GetBoundMethodBody` 返回只含 `BoundLabelStatement(ExitLabel)` 的**空壳块**，顶层语句是经初始化器路径绑定的。⇒ **walker 看不到顶层语句，判据不可能触发**。宿主 `<Initialize>` 仍是普通异步方法（`MethodKind.Ordinary`、`IsShared=False`、`IsAsync=True`，见 `SynthesizedInteractiveInitializerMethod.vb:51-55`/`:105-109`），所以问题不在异步判定，而在**「被检查的体」与「实际发射的体」不是同一块**。

**仍未查**：为什么 Catch/Finally 形状发射到 `ILBuilder.BlockedBranchDestinationSlow` 空引用、而 `SyncLock` 形状能发射出「能跑但坏」的 IL（上节给了**推测**：`SyncLock` 的 `Await` 落在隐式 `Try` 块内属合法位置）。

## 相关

- 同批发现的姊妹问题：`issue-script-top-level-goto-await-crash.md`（issue 11）。**注意根因不同源**——该文的实测已把根因订正为「`SourceMemberContainerTypeSymbol.vb:2613-2615` 丢弃顶层 `LabelStatement`」，与本条的「walker 看不到顶层语句」是两处独立缺口（详见该文）。
- 该形状的「本该报错」性质与 `issue-script-shared-field-await-initializer-crash.md`（issue 06）同族——都是「VB 缺一条 `Await` 位置诊断 → 从报错变崩」。
- 修复蓝图与验收：`../tasks/script-top-level-crashes/`（§F10）。**验收必须含 `SyncLock` 子形状**（该任务的 `test-plan.md` 已列 F10-L2-3 / F10-L3-2 覆盖它，并记「对照：未修复时编译通过但运行抛 `SynchronizationLockException`」）。
