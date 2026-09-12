# [BUG] 提交类的共享成员不报 BC30369：隐式 `Me` 到实例成员 → 运行期 `InvalidProgramException`

**状态**：Open（2026-09-12 登记）
**证据等级**：**已运行**（Debug `2.0.0-Beta+5816a5c`，本机实测，含普通类对照）
**严重度**：高（用户可达、编译期零诊断、运行期抛 `InvalidProgramException`；共享字段初始化器形状叠加 issue 05）
**影响面**：任何 `TypeKind.Submission` 编译（`.vbx` 脚本执行、vbi REPL 提交）里的**共享方法体**与**共享字段/属性初始化器**；`vbc` 不产生提交编译，不受影响

## 症状

脚本顶层声明一个 `Shared` 方法，方法体里不带限定地读一个**实例**字段：

```vb
' probe A
Dim sx As Integer = 5

Shared Sub S()
    Console.WriteLine("SHARED-READS-INSTANCE " & sx)
End Sub

S()
```

**编译期零诊断**，运行期抛未处理异常（`EXITCODE=58`）：

```text
System.InvalidProgramException: Common Language Runtime detected an invalid program.
  + Submission#0.S()
  + Submission#0.VB$StateMachine_1_<Initialize>.MoveNext() … probe.vbx : 7
  + Microsoft.CodeAnalysis.Scripting.ScriptExecutionState.<RunSubmissionsAsync>… ScriptExecutionState.cs : 114
```

即 `sx` 被当作**隐式 `Me`** 引用绑定了实例字段，发射进一个 **static** 方法体（`Shared Sub S()`）⇒ 非法 IL ⇒ JIT 拒绝。

同形状的**共享字段初始化器**版本（探针 q09）：

```vb
' probe B
Function F() As Integer
    Return 3
End Function
Shared Dim y = F()          ' F 是实例方法，靠隐式 Me 调用
```

**也是零诊断**；它今天先撞上 issue 05 的 `TypeLoadException`（带参 `.cctor` 让类型装不上），所以看不到后续 IL 问题 —— 但这条路径同样没有 BC30369。

## 对照（已运行）

| 形状 | 普通类（嵌套在脚本里） | 提交类顶层 |
|---|---|---|
| 共享方法体读实例字段 | ❌ **BC30369**（exit 1） | ❌ 零诊断 → 运行期 `InvalidProgramException`（exit 58） |
| 共享字段初始化器调实例方法 | ❌ **BC30369**（exit 1） | ❌ 零诊断 → issue 05 的 `TypeLoadException`（exit 34） |

普通类探针（两个形状各一个）：

```vb
Public Class C
    Public x As Integer = 5
    Public Shared Sub S()
        Console.WriteLine("ORDINARY " & x)      ' → error BC30369, 指向 x
    End Sub
End Class
C.S()
```

```vb
Public Class C
    Public Shared x As Integer = F()            ' → error BC30369, 指向 F()
    Public Function F() As Integer
        Return 3
    End Function
End Class
```

BC30369 = `ERR_BadInstanceMemberAccess`（`Compilers\VisualBasic\Portable\Errors\Errors.vb:323`），消息「Cannot refer to an instance member of a class from within a shared method or shared member initializer without an explicit instance of the class.」（`VBResources.resx:919-921`）。

## 根因（源码核实）

**报错点被一个「脚本类里没有共享代码」的提前返回挡掉了。** `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:2257-2270`：

```vb
Private Function CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext(implicitReference As Boolean, <Out()> ByRef errorId As ERRID) As Boolean
    errorId = Nothing

    ' Any executable statement in a script class can access Me/MyClass/MyBase implicitly but not explicitly.
    ' No code in a script class is shared.
    Dim containingType = Me.ContainingType
    If containingType IsNot Nothing AndAlso containingType.IsScriptClass Then
        If implicitReference Then
            Return True                     ' ← 任何隐式 Me 一律放行
        Else
            errorId = ERRID.ERR_KeywordNotAllowedInScript
            Return False
        End If
    End If

    If IsMeOrMyBaseOrMyClassInSharedContext() Then
        errorId = If(implicitReference, ERRID.ERR_BadInstanceMemberAccess, …)   ' ← BC30369，:2272-2278
```

- 脚本类（含提交类）的隐式 `Me` 在 `:2264-2265` **无条件返回 True**，函数在到达 `:2272-2278` 的 BC30369 分支前就退出了。
- 该分支的成立以注释里的假设为前提：「**No code in a script class is shared**」（`:2261`）。**这条假设在本 fork 不成立**——fork 让脚本顶层可以声明 `Shared` 成员（`spec\spec-scripting-dialect.md:16`、`:48`、`:60`），共享成员体里没有 `Me`，隐式引用实例成员必然落到非法 IL。
- **证据等级：已检查（源码逐行）+ 已运行（两个形状 × 两个容器共 4 个探针）。**

**同一处假设的第二个落点（已由 issue 06 记录）**：`Binder_Expressions.vb:4720-4728` 的 `IsInAsyncContext()` 对脚本类的字段/属性只问「是不是脚本类」，也不问 `IsShared` —— 同一个「脚本类里没有共享代码」前提的另一种表现。

## 预期行为

应当报 **BC30369**（`ERR_BadInstanceMemberAccess`），与普通类同形；或明确放开「提交类共享成员可隐式访问实例状态」并给出正确的承载机制（若走后者，需要回答「静态方法体里那个 `Me` 从哪来」）。

现状是**两者皆无**：编译通过、运行期抛 `InvalidProgramException`（共享方法形状）或先撞 issue 05（共享初始化器形状）。

## 修复方向（候选，未拍板）

- **A. 在 `:2263-2270` 的脚本类分支里加共享上下文判据**：把 `If implicitReference Then Return True` 改成「隐式引用时先查 `IsMeOrMyBaseOrMyClassInSharedContext()`，命中则照常落 BC30369」，只对**显式**引用保留 `ERR_KeywordNotAllowedInScript`。改动最小、与普通类对齐；须评估 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2225-2255` 一带）在脚本类上下文里的判据是否需要补 `IsShared`。
- **B. 顺带修 issue 06**：两处都是同一前提，若同批修可让规范只写一条「脚本类里的共享上下文」规则。但两条缺陷的验收面不同（一为应用 BC30369，一为 `Await` 诊断），是否绑成一包要由会议裁。
- 两者都需评估：脚本顶层 `Dim` 的隐式访问（`spec:243` 明文允许「隐式 `Me` 引用」）在**实例**成员里是**正确且要保留**的——回归测试必须同时锁住「实例方法体里隐式访问实例字段仍然合法」与「共享成员体里同样写法报 BC30369」。

## 与在办提案的关系

- `InternalDevDocs\proposals\proposal-submission-shared-members.md`——本 issue 由该提案的「同一族缺口清点」发现（原问为「`Shared` 方法是否健康」，探针给出反例）。该提案因此把「`Shared` 方法健康」限定为「不隐式引用实例状态时健康」，并把本 issue 列为同族第三项。
- `InternalDevDocs\issues\issue-script-shared-field-await-initializer-crash.md`（issue 06）——同一「脚本类里没有共享代码」前提的另一个落点（`IsInAsyncContext`）。
- `InternalDevDocs\issues\issue-submission-shared-field-initializer-typeload.md`（issue 05）——共享初始化器形状的下一步失败（探针 q09 今天停在 issue 05）。
- `InternalDevDocs\issues\issue-script-top-level-extension-method-crash.md`（issue 04）——同属「脚本类里被移植时漏掉的前提」形状（那条是 `Debug.Assert(Me.IsShared)`）。
- `InternalDevDocs\spec\spec-scripting-dialect.md:234`、`:243`、`:258`——规范对「隐式 `Me` 允许 / 显式 `Me` 报 BC36966」的规定；本 issue 是**共享**成员上的第三个情形，规范未覆盖。

## 未复现 / 未查

- Release 构建下的表现（仅跑了 Debug；`InvalidProgramException` 与 `Debug.Assert` 无关，**推测** Release 下同样会抛）。
- 跨提交形状（提交 1 声明实例字段、提交 2 的共享方法读它）。
- 共享**属性** getter 里的隐式 `Me`（应同族，未测）。
- 嵌套类里的共享成员（普通路径，**推测**无此问题——未见相关断言或提前返回）。
- 探针已跑并清除（`tmp\extprobe\` 恢复为空）。
