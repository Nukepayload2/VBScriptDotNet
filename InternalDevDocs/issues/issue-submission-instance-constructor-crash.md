# 脚本顶层声明实例构造器 → 断言终止 / `Sequence contains more than one element`（脚本类构造器是编译器不变量）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-13
* 发现场景：崩溃形状穷尽扫描（`tmp\probes\sweep\`，390 探针）

## 触发面

脚本（`.vbx`）顶层声明**实例**构造器。空参、带参、`Protected` / `Private` / `Public` 与裸声明**同判定**；`Shared Sub New()` 不受影响。

```vbx
Sub New()
End Sub
```

```vbx
Sub New(x As Integer)
End Sub
```

```vbx
Protected Sub New()
End Sub
```

## 症状与实测退出码

**两种症状，按构造器在源文件里的位置分裂**（扫描批次逐条实测，`tmp\probes\sweep\_table.md`）：

- **症状 A —— 构造器是首个成员**（可被注释、空行前导；`Imports` 不算）：断言终止 `Debug.Assert(comparison <> 0)`（`Compilers\VisualBasic\Portable\Compilation\LexicalOrderSymbolComparer.vb:43`），`EXITCODE=2148734499`。实测覆盖 `ctor-plain-first` / `ctor-arg-first` / `ctor-protected-first` / `ctor-private-sub-new` / `ctor-protected-sub-new` / `ctor-plain-after-blank` / `ctor-plain-after-comment` / `ctor-protected-after-blank` / `ctor-protected-after-comment`。
- **症状 B —— 构造器之前有任何一个成员**（语句 / `Imports` / 类声明 / `Enum`）：`System.InvalidOperationException: Sequence contains more than one element`，栈顶是 `Symbols\NamedTypeSymbol.vb:699` 的 `InstanceConstructors.Single()`，`EXITCODE=2148734217`。实测覆盖 `ctor-plain-after-stmt` / `ctor-arg-after-stmt` / `ctor-private-after-stmt` / `ctor-protected-after-stmt` / `ctor-plain-after-imports` / `class-then-toplevel-ctor`。

两者都**零编译诊断**直接崩。对照组：`ctor-shared-sub-new` exit `0`。

## 根因

**提交类的实例分支无条件合成构造器，于是成员表里出现两个 `.ctor`。**

- `Symbols\Source\SourceMemberContainerTypeSymbol.vb:2744-2762`：`AddDefaultConstructorIfNeeded` 的 `TypeKind = TypeKind.Submission` 实例分支**无条件**创建 `SynthesizedSubmissionConstructorSymbol` 并加进成员表（`:2761`），不看用户是否已经声明了实例构造器。
- `Symbols\NamedTypeSymbol.vb:697-700`：`GetScriptConstructor` = `DirectCast(InstanceConstructors.Single(), SynthesizedConstructorBase)` —— 它要求**全类型恰好一个**实例构造器，且必须是合成的那个。
- `Symbols\Source\SynthesizedEntryPointSymbol.vb:343-344`：`<Factory>` 取 `GetScriptConstructor()` 后 `Debug.Assert(ctor.ParameterCount = 1)`；同文件 `:258-259` 对 `<Main>` 断言 `= 0`。`:336-339` 的注释即 `<Factory>` 体本身：`Dim submission As New Submission#N(submissionArray)` / `Return submission.<Initialize>()`。

**用户声明的构造器没有落点**（三条不变量，均为实锤）：

1. `.Single()` 要求唯一实例构造器（`NamedTypeSymbol.vb:697-700`）。让用户构造器留在成员表里则该行抛 `InvalidCastException`（可行性补丁实测 exit `2147500034`，栈顶即此行）。
2. 宿主自己构造提交类（`SynthesizedEntryPointSymbol.vb:336-339` / `:343-344`），用户构造器没有调用点。
3. 提交数组的状态恢复（`SynthesizedSubmissionConstructorSymbol`）挂在合成构造器上；换成用户构造器，提交数组永不赋值 ⇒ 即便放行也只是把崩溃从编译期挪到运行期。

## 预期行为

普通上下文里同一个形状**合法**：`Class C : Sub New() : End Sub : End Class` + `Dim c As New C` 用 `vbc.exe` 编译 exit `0`（实测 `tmp\probes\u6bc\ctor-ordinary.vb`），构造器正常执行。所以这不是「用户写错了」。

但提交类**不允许**声明实例构造器：提交实例由宿主创建。⇒ 本形状判**报错**（判定原则的第二条出路：「报诊断说脚本不支持这样用」）。

## 修复方向

在成员收集入口加判据：`Me.IsScriptClass` 且刚创建的成员 `MethodKind = MethodKind.Constructor`（**实例**构造器；`Shared` 构造器不受影响）时，报新诊断并**不把该成员加入成员表**。`IsScriptClass` 覆盖**两类**脚本类（提交类与 `DeclarationKind.Script` 的非提交脚本类），见下「修复的漏网面」。

不加入成员表是必须的而非简化——只要两个 `.ctor` 同时在场，`GetScriptConstructor` 的 `.Single()` 就会抛（见上）。

落点：两个方法分支（`SourceMemberContainerTypeSymbol.vb:2572` / `:2589`）改为调用私有 `AddMethodMember`（`:2696-2712`），判据在 `:2706-2709`。

## 修复后的新增行为变化

**新增诊断 `BC37342`**（此前零诊断、直接崩）：

```
error BC37342: An instance constructor cannot be declared in a script class because
the script instance is created by the generated entry point and its constructor is
compiler-synthesized. Put initialization code in top-level statements.
```

- 诊断 id：`Errors.vb:1817` 的 `ERR_SubmissionCannotDeclareInstanceConstructor = 37342`，紧邻脚本专属带 `ERR_BadAwaitInSharedInitializer = 37341`（`:1816`）。`ERR_NextAvailable` 现为 `37344`（`:1820`）——`:1818` 的 `37343` 已归 `ERR_WithEventsVariableNotInContainingType`（跨提交 / 宿主对象的 `WithEvents` 变量上的 `Handles` 子句，判据见 `SourceMemberMethodSymbol.vb:707-711`）。
- `ErrorFacts.IsBuildOnlyDiagnostic` 的 `Return False` 清单含该 id（`:1563`）——不加会让 `DiagnosticTests.TestIsBuildOnlyDiagnostic` 抛 `NotImplementedException`。
- `VBResources.resx:4730-4732` 英文文案一条，13 份 `VBResources.*.xlf` 各一条。
- 位置锚在**整条声明**上（`Sub New()` / `Protected Sub New()` / `Sub New(x As Integer)`）。

实测（当前编译器，`vbi.exe /check` 与直跑）：

| 形状 | 症状 | 当前行为 |
|---|---|---|
| 构造器在首位 | `2148734499` 断言终止 | exit 1，唯一诊断 `BC37342` |
| 构造器在语句之后 | `2148734217` `Sequence contains more than one element` | exit 1，唯一诊断 `BC37342` |
| `Shared Sub New()` | exit 0 | exit 0（不变） |

回归用例：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:775-821`（空参 / 带参 / 三种访问修饰符 / 语句之后）与 `:901-941`（非提交脚本类四条，见下），`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb` 三条（案例 `:678` / `:696` / `:724`，宿主面）。

## 修复的漏网面：非提交脚本类

**实测**（非提交脚本编译：`VisualBasicCompilation.Create("probe", {Parse(code, options:=TestOptions.Script)}, {MscorlibRef_v4_0_30316_17626, MsvbRef_v4_0_30319_17929}, New VisualBasicCompilationOptions(OutputKind.ConsoleApplication).WithScriptClassName("Script"))`）：

```
IsSubmission=False
ScriptClass=Script TypeKind=Class IsScriptClass=True
GetDiagnostics THREW System.InvalidCastException: Unable to cast object of type
  'SourceMemberMethodSymbol' to type 'SynthesizedConstructorBase'.
Emit THREW 同一 InvalidCastException
```

同族扫描（同一非提交脚本编译下逐个形状，实测）：**只有实例构造器一族崩**——`Sub New()` / `Sub New(x)` / 语句之后的 `Sub New()` 三个形状都是上面这条；`Shared Sub New()` 正常（`diags=[] emit=True`）；`RaiseEvent`、自定义 `Custom Event`、`Handles`、显式 `MyBase`、顶层 `Finally` 跳出都不崩（报诊断或正常）。

**根因是门写窄，不是另一条缺陷**：判据原先写成 `Me.TypeKind = TypeKind.Submission`，漏掉 `DeclarationKind.Script` 的非提交脚本类。这类脚本类由生成的入口点实例化——`Symbols\Source\SynthesizedEntryPointSymbol.vb:258-281` 的 `<Main>` 取 `GetScriptConstructor()` 并 `Dim script As New Script()`——与提交类同样是「单一实例构造器槽位」的关系。

**症状与提交类不同源**：非提交脚本类的槽位由 `EnsureCtor` 合成，而用户声明的空参构造器已在成员表里时 `EnsureCtor`（`SourceMemberContainerTypeSymbol.vb:2809-2810`）提前返回、不再合成 ⇒ `InstanceConstructors.Single()` 成功、随后的 `DirectCast(..., SynthesizedConstructorBase)` 抛 `InvalidCastException`；提交类则是 `.Single()` 先抛（本例的两种症状）。两者同源于同一条不变量：入口点只会调用编译器合成的那个构造器。

**改法**：门放宽为 `Me.IsScriptClass AndAlso methodSymbol.MethodKind = MethodKind.Constructor`（`SourceMemberContainerTypeSymbol.vb:2706`）。`IsScriptClass`（`:1295-1300`）为 `DeclarationKind.Script OrElse DeclarationKind.Submission`，恰好是这两类脚本类，且与 `DeclarationKind.ImplicitClass` 互斥（`IsImplicitClass` 于 `:1302-1306`）——普通文件里游离语句的隐含类没有脚本入口点，**不能**被卷入；同文件 `:2786`（合成初始化器与入口点）本来就以 `IsScriptClass` 为判据。

**回归用例**（`ScriptSemanticsTests.vb:901-941`）：`ScriptClassParameterlessInstanceConstructor_ReportsSubmissionCannotDeclareInstanceConstructor` / `ScriptClassInstanceConstructorWithParameters_…` / `ScriptClassInstanceConstructorAfterStatement_…` 三条断言唯一诊断 `BC37342` 落在声明上，并显式跑 `GetDiagnostics()` 与 `Emit` 断言不再抛、发射不成功；判别性对照 `ScriptClassNestedClassInstanceConstructor_NoDiagnostics` —— 同一非提交脚本编译里嵌套普通类声明 `Sub New()` 零诊断。

## 相关

- 判定原则与三条不变量的完整论证：`../tasks/script-top-level-crashes-2/design-overview.md` §3。
- 同为「顶层形状落到合成提交类上撞不变量」的姊妹问题：`issue-top-level-raise-event-instance-event-crash.md`（13）、`issue-top-level-handles-clause-crash.md`（14）、`issue-top-level-mybase-assert.md`（15）。
