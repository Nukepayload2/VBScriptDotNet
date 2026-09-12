# 提交中的 `WithEvents` / `Handles` 支持 / WithEvents and Handles in Submissions

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本提案定义**提交（submission / `.vbx` / REPL 提交链）里的 `WithEvents` 字段与 `Handles` 子句应当如何工作**。语言侧不引入新语法——`WithEvents` 与 `Handles` 都是既有 VB 语法，脚本模式下也已经能解析（`Sub H0(...) Handles Me.Tick` 在 `.vbx` 顶层与 REPL 提交里都能走到绑定期）；缺失的是**语义与实现路径**。今天的实际状态比「静默失效」更糟：本提案实测的四条提交类输入路径中，四条里有三条在 Release 构建下也让进程或会话终止——(a)(b) 未处理 `InvalidOperationException`（退出码 9）、(c) 未处理 `InvalidCastException`（退出码 1 且 REPL 会话终止）；第四条 (d) 是 Debug 断言误伤、Release 下正常。Debug 构建下四条路径也都终止（(a)(b)(c)(d) 均为退出码 35；其中 (c) 先撞上 Debug 断言 `:66`，看不到 `:702` 的 `DirectCast`）。而**提交类里的嵌套类型完全正常**。本提案把这四条路径拆到 `文件:行号`，按 `decisions.md` **D5** 的强制要求给出 C# / csi 侧对照与「VB 特有理由」排查结论，并把候选方向、成本/收益与待裁决点列全，交 LDM 会议定夺。

本项**不是 bug 修复**：即使现状表现为崩溃，把它修成「能用」需要新增跨提交语义——**挂钩写入由哪一次提交承载**（这是本提案认定的核心设计轴，见 Detailed design §2.6）、关键字容器的挂载宿主选择、同名 `WithEvents` 的替换规则、旧订阅的摘除规则——这些都没有既有语义可循。

## Motivation
[motivation]: #motivation

- **`WithEvents` / `Handles` 在脚本里不是冷门组合。** 它是 VB 用户表达「把某个对象的某个事件绑到这个方法」的声明式写法，`Handles` 更是 VB 相对 C# 的招牌语法。`.vbx` 与 REPL 面向的是 VB6/VBA 迁移用户，这批人写 `WithEvents` 的默认动作是声明式而不是命令式 `AddHandler`。今天他们得到的是**编译器崩溃**，不是错误信息。
- **现状违反两条已生效的纪律。** `meeting-scripting-dialect.md` RESOLUTION **R11** 裁决「`WithEvents`/`Handles` 不得静默，实现挂钩或给显式诊断，二选一」（`:150`），spec 也已在方言边界段落把该行为划到保证之外（`spec\spec-scripting-dialect.md:348`）。但 spec 的措辞（「a submission class synthesizes no hookup constructors」）描述的是 `AddWithEventsHookupConstructorsIfNeeded` 的 TODO 分支，不是用户实际遭遇的行为——用户遭遇的是异常终止。
- **它是 `spec-scripting-dialect` 的最后一个未闭合结构面。** 该 spec 已把声明模型、提交链、入口点、结果规则、`#Load` 多树、`Imports` 累积都钉成基线；唯独事件挂钩一句带过，且那一句对「非提交脚本类」的描述与注入层实现不符（见 Detailed design §4 子情形 (vi) 与 §7）。
- **`decisions.md` D5 要求基础功能的落地细节以 C# / csi 为蓝本，并说明「为什么 VB 必须分叉」。** 事件挂钩正好落在 D5 的点名范围（构造器、初始化器、类型落点）。本提案按 D5 的要求把对照做到底，结论是：分叉**解释得了**（`WithEvents`/`Handles` 是 C# 没有的语言概念），但**解释不了当前的崩溃**——上游 Roslyn 自己把它登记为 dotnet/roslyn#14073（`ImplicitNamedTypeSymbol.vb:217`）。缺口的性质是「移植不完整」，这也是它必须走完整流程、而不是当 bug 顺手改的原因：补齐它要新定义规则，不是恢复既有行为。

## Detailed design
[design]: #detailed-design

> **证据等级标注**：本节每个关键结论后标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑）。断言三态按实锤 / 推测 / 猜测标注，未标注者按**猜测**处理。锚点均在本 fork 源码逐条复核 → **已检查**；运行结论来自 `.vbx` 文件执行与 REPL 会话两种宿主路径 → **已运行**。**本阶段的证据边界（含未复现项）集中列在 §8，引用本节的运行结论时必须连带读该节。**
>
> **运行环境（两个构建，必须区分）**：Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（版本串 `2.0.0-Beta+5816a5c`，即当前 HEAD，**带 `Debug.Assert`**）；Release `Interactive\vbi\bin\Release\net10.0\vbi.exe`（版本串 `2.0.0-Beta+e307d0f`，2026-08-23 构建，早于当前工作树若干提交）。凡「Debug-only 断言」处均注明；凡两构建行为不同处均并列给出。

### 1. 现状事实基线：三条崩溃路径 + 一条 Debug 断言

`AddWithEventsHookupConstructorsIfNeeded` 的提交分支确实什么都不做：

```vb
2804        Private Sub AddWithEventsHookupConstructorsIfNeeded(members As MembersAndInitializersBuilder, diagBag As BindingDiagnosticBag)
2805            If TypeKind = TypeKind.Submission Then
2806                'TODO: anything to do here?
2807
2808            ElseIf TypeKind = TypeKind.Class OrElse TypeKind = TypeKind.Module Then
```

—— `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2804-2808`（调用点 `:2509`，紧随两个 `AddDefaultConstructorIfNeeded` 之后）。

**但这个 TODO 分支对本主题而言是死代码**：**提交类（`TypeKind.Submission`）里**任何含可解析事件的 `Handles` 子句，都会在更早的绑定期抛异常，走不到这里（**范围限定为提交类**：嵌套类型不在此列，见下表的 (e) 行——该行有三重实测，是范围限定反例、不计入路径数）。四条实测输入路径如下（三条崩溃路径 (a)(b)(c) + 一条 Debug 断言误伤 (d)，**已运行**）：

| # | 输入 | Debug 构建 | Release 构建 | 崩溃点 |
|---|---|---|---|---|
| (a) | `.vbx` 顶层：`WithEvents src As New Ticker` + `Sub H1(...) Handles src.Tick` | 进程终止，退出码 35（断言） | 未处理 `InvalidOperationException`，退出码 9 | `SourceMemberMethodSymbol.vb:771` |
| (b) | `.vbx` 顶层：`Event Tick(...)` + `Sub H0(...) Handles Me.Tick` | 进程终止，退出码 35 | 未处理 `InvalidOperationException`，退出码 9 | 同上 `:771` |
| (c) | REPL：前序提交声明 `WithEvents src`，本提交 `Sub H2(...) Handles src.Tick` | 进程终止，退出码 35（终止于 `SourceWithEventsBackingFieldSymbol.vb:66` 断言，**不是** `:702`） | 未处理 `InvalidCastException`，退出码 1，**会话终止**（尾随提交不再执行） | `SourceMemberMethodSymbol.vb:702`（Release） |
| (d) | `.vbx` 顶层：仅 `WithEvents src As New Ticker`（无 `Handles`） | 进程终止，退出码 35（`Not Me.ContainingType.IsImplicitlyDeclared`） | **正常编译并运行，退出码 0** | `SourceWithEventsBackingFieldSymbol.vb:66` |
| (e) | `.vbx`：**提交类里的嵌套类型**（`Class Host` 内 `WithEvents` + `Handles`） | **正常工作**（打印 `nested H1 fired`，退出码 0） | 正常工作，退出码 0 | — |

崩溃点原文与根因：

```vb
762            Select Case ContainingType.TypeKind
763                Case TypeKind.Interface, TypeKind.Structure, TypeKind.Enum, TypeKind.Delegate
764                    ' Handles clause is invalid in this context. 
765                    Return Nothing
766
767                Case TypeKind.Class, TypeKind.Module
768                    ' Valid context
769
770                Case Else
771                    Throw ExceptionUtilities.UnexpectedValue(ContainingType.TypeKind)
772            End Select
```

—— `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberMethodSymbol.vb:762-772`。`TypeKind.Submission` 落进 `Case Else`；异常由 `Throw ExceptionUtilities.UnexpectedValue(...)` 抛出，`ExceptionUtilities.UnexpectedValue` 本身只是 `Debug.Assert(false, ...)` 后返回 `InvalidOperationException`（`Dependencies\Contracts\ExceptionUtilities.cs:18-26`）——**Release 下断言被编译掉，异常照抛**，所以这不是「Debug 才崩」。异常在 `SourceModuleSymbol.GetAllDeclarationErrors` 的 Task 里逸出（`SourceModuleSymbol.vb:703`），随后穿到脚本宿主。**两条路径的逸出栈不同，必须分开读**（下列帧行号取自较旧的 Release `e307d0f` 构建，与当期树行号不同，以该二进制自身 PDB 为准）：

- **`.vbx` 文件路径（(a)/(b)，退出码 9）**：`SourceModuleSymbol.vb:703 → Compilation.Emit → ScriptBuilder.Emit:179 → Script.RunAsync:469/:443 → CommandLineRunner.RunScriptAsync:209`。该树 `:209` 逐字是 `return (await script.RunAsync(globals, cancellationToken)).ReturnValue;`（当期树同一定义在 `:238`、同一调用在 `:256`）。**栈中不经 `CommonCompiler`。**
- **REPL 路径（(c)，退出码 1）**：**不含** `RunAsync` 帧，经 `CommonCompile` / `Compile` / `BuildAndRunAsync`，含 `CommandLineRunner.RunInteractiveCoreAsync:146` 与 `Vbi.OnStartupAsync:62`。

**且异常并非「未处理」**：`.vbx` 路径由 `RunScriptAsync` 的 `catch (Exception e) { DisplayException(e); return e.HResult; }` 接住，退出码 9 = `COR_E_INVALIDOPERATION`（`0x80131509` 低字节）；REPL 路径由 `Vbi.OnStartupAsync` 兜底 `catch` 返回 1；Debug 下 FailFast 退出码 35 = `0x80131623` 低字节。三条码对应三条不同路径。**所以崩溃不会转成编译诊断，用户看到的是「脚本出错」。**（**已运行**：会议阶段对 Release `e307d0f` 二进制复跑 + 逐帧比对）

**路径 (c) 是另一处、更早的崩溃**，与 (a)/(b) 无关：

```vb
700                ' if was found in one of bases, need to override it
701                If isFromBase Then
702                    witheventsPropertyInCurrentClass = DirectCast(Me.ContainingType, SourceNamedTypeSymbol).GetOrAddWithEventsOverride(witheventsProperty)
703                Else
704                    witheventsPropertyInCurrentClass = witheventsProperty
705                End If
```

—— `SourceMemberMethodSymbol.vb:700-705`；`isFromBase` 的判据在 `:665`（`Not TypeSymbol.Equals(witheventsProperty.ContainingType, Me.ContainingType, TypeCompareKind.ConsiderEverything)`）。脚本类 / 提交类的实际符号类型是 **`ImplicitNamedTypeSymbol`**，它直接派生自 `SourceMemberContainerTypeSymbol`、**不经过** `SourceNamedTypeSymbol`——`DeclarationKind.ImplicitClass` / `Script` / `Submission` 三种声明都构造 `ImplicitNamedTypeSymbol`（`SourceMemberContainerTypeSymbol.vb:233-237`），其余构造 `SourceNamedTypeSymbol`（`:240`）；源头另见 `VisualBasicCompilation.vb:2021-2022`。因此**在提交类顶层方法里**（此时 `Me.ContainingType` 就是那个 `ImplicitNamedTypeSymbol`）`isFromBase = True` 时这个 `DirectCast` 必然 `InvalidCastException`——实测抛在 `:702`（嵌套类型内的同名情形不在此列：其 `ContainingType` 是 `SourceNamedTypeSymbol`）。这也正是设计层「跨提交 `WithEvents` 需要造覆盖属性」想走的那条路：对提交类，覆盖属性无处可造（`MakeDeclaredBase` 对 `Submission` 返回 `Nothing`，`ImplicitNamedTypeSymbol.vb:51-60`）。

**路径 (d) 的意义**：Debug 的断言 `Not Me.ContainingType.IsImplicitlyDeclared`（`SourceWithEventsBackingFieldSymbol.vb:66`）在脚本类上必然为假——`ImplicitNamedTypeSymbol.IsImplicitlyDeclared` 就是 `IsImplicitClass OrElse IsScriptClass`（`ImplicitNamedTypeSymbol.vb:33-37`，`IsScriptClass` 定义在 `SourceMemberContainerTypeSymbol.vb:1295-1300`）。Release 下断言被编译掉，`AddSynthesizedAttributes` 正常追加 `CompilerGenerated` / `DebuggerBrowsable(Never)` / `AccessedThroughProperty` 三个特性（`:68-77`），产物可用。**所以这条断言是误伤，不是缺口**（**已运行**：两构建行为相反）。

**(e) 行（范围限定反例）的意义**：嵌套类型的 `ContainingType.TypeKind` 是 `Class`，`Handles` 走 `Case TypeKind.Class` 正常通过（`:767`）；它的构造器也不是脚本构造器（`IsScriptConstructor` = `MethodKind.Constructor AndAlso ContainingType.IsScriptClass`，`Symbols\MethodSymbol.vb:517-521`，而 `ContainingType` 是嵌套类而非脚本类），所以挂钩注入没有被跳过。**结论：既有资料「提交类不支持、嵌套也不支持」的二分与事实相反——提交类里可用的恰恰是嵌套类型。**

### 2. 既有机制盘点：哪些是现成的，缺的到底是哪一层

`WithEvents` 与 `Handles` 的完整机制由四块组成。其中 **(2.1)(2.2) 两块与类型种类无关且已经写好**；(2.3) 的宿主选择按类型种类分流；(2.4) 对脚本类显式跳过。

**(2.1) `WithEvents` 字段就地展开成「属性 + 后备字段 + 两个访问器」。** 声明 `WithEvents x As New Raiser` 时，成员登记不进字段符号，而是：

```vb
643                        Dim propertySymbol = SourcePropertySymbol.CreateWithEvents(container,
...
653                        container.AddMember(propertySymbol, binder, members, omitFurtherDiagnostics)
654                        container.AddMember(propertySymbol.GetMethod, binder, members, omitDiagnostics:=False)
655                        container.AddMember(propertySymbol.SetMethod, binder, members, omitDiagnostics:=False)
656                        container.AddMember(propertySymbol.AssociatedField, binder, members, omitDiagnostics:=False)
```

—— `Symbols\Source\SourceMemberFieldSymbol.vb:643-656`；属性侧 `SourcePropertySymbol.CreateWithEvents`（`Symbols\Source\SourcePropertySymbol.vb:231-289`，访问器合成在 `:271-283`；后备字段恒 `Private`，`SourceWithEventsBackingFieldSymbol.vb:29-32`）。这段对脚本类**照常执行**——路径 (d) 能走到 `SourceWithEventsBackingFieldSymbol` 就是证据。**已运行（间接）。**

**(2.2) `WithEvents` 容器的挂/摘钩在属性 `Set` 访问器体内，与构造器无关。** `SynthesizedPropertyAccessorBase` 在 `Set` 的合成体里分四段：认领本类中 `hookupMethod` 指向该访问器的 `HandledEvents`（`:143-196`，`:164` 判等）、摘掉旧值上的处理者（`:201-271`，`:249` 产 `BoundRemoveHandlerStatement`，`:257-268` 以 `old IsNot Nothing` 守卫）、赋值（`:273-300`）、对非空新值挂上处理者（`:302-345`，`:329` 产 `BoundAddHandlerStatement`）。

```vb
143                If propertySymbol.IsWithEvents Then
...
164                                    If handledEvent.hookupMethod = accessor Then
...
201                ' need to unhook old handlers before setting a new event source
202                If eventsToHookup IsNot Nothing Then
```

—— `Binding\SyntheticBoundTrees\SynthesizedPropertyAccessorBase.vb:143-196`、`:201-271`、`:273-300`、`:302-345`。**该文件内 `TypeKind` 零命中（已 grep）**，即这一块是完整的挂 + 摘实现，且不区分「类型是 Class 还是 Submission」。**已检查。**

运行时直证（嵌套类型，Debug 构建，退出码 0）：把 `WithEvents src` 换成新对象后再对**旧对象**触发事件，处理者不再执行；对新对象触发则执行——**已运行**：

```vbnet
Class Host
    Public WithEvents src As New Ticker With {.Name = "A"}
    Public Old As Ticker
    Sub H1(sender As Object, e As EventArgs) Handles src.Tick
        Console.WriteLine("H1 fired")
    End Sub
    Public Sub Swap()
        Old = src
        src = New Ticker With {.Name = "B"}
    End Sub
End Class
' h.Swap() 之后：h.Old.RaiseIt() 无输出；h.src.RaiseIt() 打印 "H1 fired"
```

**(2.3) 关键字容器（`Handles Me.E` / `MyClass.E` / `MyBase.E`）与 `WithEvents` 容器走两条不同的宿主方法选择路径。**

```vb
780            If handlesKind = HandledEventKind.WithEvents Then
781                hookupMethod = witheventsPropertyInCurrentClass.SetMethod
782            Else
...
793                    hookupMethod = instanceCtors(0)
```

—— `SourceMemberMethodSymbol.vb:780-795`。`WithEvents` 容器 → `Set` 访问器（即 (2.2)，**不需要构造器**）；关键字容器 → 实例构造器（`:793`）。**已检查。**

**(2.4) 关键字容器的挂钩注入在构造器体组装里，而脚本构造器体被显式跳过。**

```vb
1480            Dim body As BoundBlock
1481            If method.MethodKind = MethodKind.Constructor OrElse method.MethodKind = MethodKind.SharedConstructor Then
1482                If method.IsScriptConstructor Then
1483                    body = block
1484                Else
1486                    body = InitializerRewriter.BuildConstructorBody(compilationState, method, constructorInitializerOpt, processedInitializers, block)
1487                End If
1488            ElseIf method.IsScriptInitializer Then
1490                body = InitializerRewriter.BuildScriptInitializerBody(DirectCast(method, SynthesizedInteractiveInitializerMethod), processedInitializers, block)
```

—— `Compilation\MethodCompiler.vb:1480-1492`。挂钩循环本体在 `Analysis\InitializerRewriter.vb:86-142`（`:111` 以 `handledEvent.hookupMethod.MethodKind = constructorMethod.MethodKind` 认领，`:135` 产 `BoundAddHandlerStatement`）。脚本初始化器体 `BuildScriptInitializerBody`（`InitializerRewriter.vb:174-186`）**只**拼「初始化器语句 + 顶层语句」，没有挂钩循环。**已检查。**

**(2.5) 脚本类的字段/属性初始化器进 `<Initialize>`，不进构造器。** 顶层 `Dim x As New Raiser` 是实例初始化器（`SourceMemberContainerTypeSymbol.vb:2626-2633` 把顶层可执行语句登记为 `FieldOrPropertyInitializer`），它们在 `MethodCompiler.vb:1488-1490` 被拼进 `SynthesizedInteractiveInitializerMethod`（`<Initialize>`，`MethodKind.Ordinary`，`SynthesizedInteractiveInitializerMethod.vb:105-107`）。提交构造器的体则在 `MethodCompiler.vb:1553-1560` 由三段落拼成：基构造器调用 + 提交数组初始化（`:1535-1537`） + 剩余体。`<Initialize>` 由入口点体内调用——`<Main>` 体经 `SynthesizedEntryPointSymbol.vb:286-292` 调 `script.<Initialize>()`（体组装在 `:251-292`），提交入口点体在 `:377` 调 `Return submission.<Initialize>()`（体组装在 `:340-382`）——**每次提交实例化只执行一次**。**已检查。**

**(2.6) 挂钩写入发生在哪一次提交——候选 E 的成立前提。**

**这是本提案认定的核心设计轴**，(2.1)(2.2) 的「现成」只在特定前提下成立，必须先把前提说清。

挂/摘钩语句活在 `Set` 访问器体里，因此它们**只在属性被赋值时执行**（§2.2 的四段结构）。而对提交类，顶层 `WithEvents x As New R` 的赋值就是**该提交自己的实例初始化器**：登记在 `SourceMemberContainerTypeSymbol.vb:2626-2633`，组装进该提交自己的 `<Initialize>`（`InitializerRewriter.vb:174-186`），只在 `MethodCompiler.vb:1488-1490` 那次编译里成形。**已检查。**

由此得到一条硬约束：**后一提交无法让前序提交的 `<Initialize>` 再执行一次**（`<Initialize>` 由入口点在提交实例化时调用一次，§2.5）。前序提交的 `Set` 访问器体在编译前序提交时就已固化，它不认识后一提交才写的 `Handles` 子句。因此：

> **候选 E（复用 `WithEvents` 容器的既有挂载机制）与 P-A1 的成立条件 = `WithEvents` 的赋值初始化器与 `Handles` 子句落在同一次提交里。** 只有此时，「编译期随该提交一起合成的 `Set` 访问器」与「运行期同一次 `<Initialize>` 里的赋值」才配对，挂钩写入才有宿主。

跨提交时三条可能路径都是**新规则**，不是既有机制的复用。三条路径里「挂钩是否真的发生」都是**推测**（本轮无运行证据——见 §8）；但它们各自依赖的局部事实有独立证据： (β) 的属性遮蔽是实锤（§5.1），(α) 的「前序实例可达」已运行证实（§5.2 #1/#2）：

- **(α) 前序提交声明 `WithEvents`，后一提交写 `Handles x.E`**：属性属主是前序提交，其 `Set` 访问器已固化。后一提交既不能重跑前序 `<Initialize>`，也不能让前序 `Set` 认得新 `Handles`。⇒ 挂钩代码必须写进**后一提交自己的某个方法**（`<Initialize>` 或提交构造器），接收方是前序实例（§5.2 已运行证实该接收方可达）。这正是子情形 (iii)。
- **(β) 后一提交重新声明同名 `WithEvents`**：属性遮蔽（§5.1），新属性/新对象/新访问器，`Set` 在后一提交的 `<Initialize>` 里被触发、挂钩发生——但挂的是**新对象**；前序对象上的旧订阅不受影响（§5.2）。
- **(γ) 靠提交构造器承载**：只解决关键字容器（`Handles Me.E`），对 `WithEvents` 容器无用（§2.3 的宿主选择不经过构造器）。

**(2.7) 缺口定位。** 综合上：缺的是两层门——**(i) 绑定层 `Handles` 的 `TypeKind` 闸只认 `Class`/`Module`（`SourceMemberMethodSymbol.vb:762-772`）**；**(ii) 注入层 `MethodCompiler.vb:1482` 让所有脚本构造器跳过挂载体（`IsScriptClass` 对 `DeclarationKind.Script` 与 `DeclarationKind.Submission` 同时为真，`SourceMemberContainerTypeSymbol.vb:1295-1300`）**。前者是三条崩溃的直接原因；后者只对关键字容器构成问题（**推测**：由 (2.2)(2.3) 的宿主选择推出）。**推测**：若只放开 (i)，子情形 (ii)（`WithEvents` 容器，且 `WithEvents` 与 `Handles` 同处本次提交，§2.6）会因为 (2.2) 是完整的而直接可用；关键字容器路径仍不生效——因为挂载体被 (ii) 跳过。该推论**未运行验证**，需 prototype 阶段实证。

### 3. C# / csi 对照（`decisions.md` D5 强制项）

**先看语言面：C# 的语法层与绑定层没有 `WithEvents` / `Handles` 概念。** 证据（`grep` 于 `Compilers\CSharp\Portable\` 的 `.cs` 源文件，已排除 `bin`/`obj`）：`Syntax\SyntaxKind.cs` 中 `Handles` / `WithEvents` **零命中**；整个 `Portable\` 树里 `WithEvents` **只有 1 处**，是公开 API 适配层的恒假实现：

```csharp
        bool IPropertySymbol.IsWithEvents
        {
            get { return false; }
        }
```

—— `Compilers\CSharp\Portable\Symbols\PublicModel\PropertySymbol.cs:89-92`。`Handles` 在 C# 侧只出现在英文注释文本里（如 `Lowering\LocalRewriter\LocalRewriter_CollectionExpression.cs:246` 的 "Handles types …"），不是语言构造。

**再看 `event` 声明本身：两侧是同构的，不是分叉点。**

| 维度 | C# | VB（本 fork） |
|---|---|---|
| 字段式事件符号 | `SourceFieldLikeEventSymbol`，合成 add/remove 访问器（`Symbols\Synthesized\SynthesizedEventAccessorSymbol.cs:24-33`、`:140-159`） | `SourceEventSymbol` + `SynthesizedEventAccessorSymbol.vb:113-120` |
| 访问器体的语义 | `Delegate.Combine` / `Delegate.Remove`（`Compiler\MethodBodySynthesizer.cs:346-408`，注释 `:336-343`） | 同式（`SynthesizedEventAccessorSymbol.vb:258-265` 注释、`:268-429` 实现） |
| 事件在脚本/提交类上的落点 | 顶层成员 → 脚本类（`Declarations\DeclarationTreeBuilder.cs:296`、`:322-361`） | 顶层成员 → 脚本类（`Declarations\DeclarationTreeBuilder.vb:139-149`，`:143` 修饰符 `Friend Or Partial Or NotInheritable`） |

**关键对照：C# 有没有「hookup 构造器」这个概念？没有。C# 的提交构造器里没有任何事件代码。**

```csharp
    internal sealed class SynthesizedSubmissionConstructor : SynthesizedInstanceConstructor
    {
...
            _parameters = ImmutableArray.Create<ParameterSymbol>(
                SynthesizedParameterSymbol.Create(this, TypeWithAnnotations.Create(submissionArrayType), 0, RefKind.None, "submissionArray"));
```

—— `Compilers\CSharp\Portable\Symbols\Synthesized\SynthesizedSubmissionConstructor.cs:12-30`（与 VB 的 `SynthesizedSubmissionConstructorSymbol.vb:19-38` 逐字同构）。它的体由 `MethodBodySynthesizer.ConstructScriptConstructorBody` 组装（`Compiler\MethodCompiler.cs:1269-1271`），与 VB 的 `MakeSubmissionInitialization`（`MethodCompiler.vb:1535-1537`）对应；**该方法的实现体内没有任何事件/委托相关代码（已 grep）**。C# 脚本初始化器的重写同样只有「初始化器 + 末尾表达式」两件事（`Lowering\InitializerRewriter.cs:32-77`），脚本构造器的体是空块（`Compiler\MethodCompiler.cs:1010-1014`）。

**C# 在提交链里对事件订阅做了什么？什么也没做——因为它的订阅是用户语句，不是编译器合成的。**

C# 里让一个订阅生效必须由用户写代码：`E += handler;`（脚本顶层就是一条 global statement，随该提交的初始化器执行），或者手写 `add` / `remove` 访问器体。两者都是用户源码，编译器不合成任何 hookup。因此 C# 的「每次新提交实例重新订阅」是**用户代码自带的**，C# 编译器既不需要、也没有对应的机制。

**两侧各自的机制结论（D5 要求的「VB 特有理由」排查）：**

1. **`WithEvents` 自动挂钩、`Handles` 子句、`MyBase` 事件映射是 VB 独有语言概念。** 证据：C# 侧零命中（见上）。这一半分叉**解释得了**——C# 没有「声明式订阅」，所以它不需要 hookup 构造器；VB 有「声明式订阅」，所以它必须有一个**合成位置**来放挂钩代码。
2. **但「这个合成位置在提交类上不存在」解释不了。** 上游 Roslyn 自己把这件事登记为跟踪项，不是有意设计：

```vb
213        Friend Overrides Function GetSynthesizedWithEventsOverrides() As IEnumerable(Of PropertySymbol)
214            ' All infrastructure for proper WithEvents handling is in SourceNamedTypeSymbol, 
215            ' but this type derives directly from SourceMemberContainerTypeSymbol, which is a base class of 
216            ' SourceNamedTypeSymbol.
217            ' Tracked by https://github.com/dotnet/roslyn/issues/14073.
218            Return SpecializedCollections.EmptyEnumerable(Of PropertySymbol)()
219        End Function
```

—— `Symbols\Source\ImplicitNamedTypeSymbol.vb:213-219`。这是**上游**对一个**上游已知缺口**的显式短路；注释直接点明根因就是本提案 §1 路径 (c) 的那个 `DirectCast` 前提（脚本类不派生自 `SourceNamedTypeSymbol`）。`SourceMemberContainerTypeSymbol.vb:2806` 的 `'TODO: anything to do here?` 同属上游遗留；fork 未改过这两处（`SourceNamedTypeSymbol` 侧的覆盖机制在 `SourceNamedTypeSymbol.vb:2738-2752`、`:2761-2770`（`GetSynthesizedWithEventsOverrides`）与 `:2772-2782`（`EnsureAllHandlesAreBound`），抽象声明在 `NamedTypeSymbol.vb:1219`）。
3. **结论**：分叉的性质是**「VB 模型需要 C# 没有的机制」+「VB 自己的这条机制在脚本类上没接完」**。前者不算缺口；后者算缺口，且是移植不完整（D5 的判定口径）。因此本项按新特性立项、走 proposal → LDM，而不是当 bug 修。
4. **C# 有无同类诊断可对照？** 没有。C# 的脚本诊断族是 `ERR_ReferenceDirectiveOnlyAllowedInScripts`(7011) / `ERR_YieldNotAllowedInScript`(7020) / `ERR_NamespaceNotAllowedInScript`(7021) / `ERR_LoadDirectiveOnlyAllowedInScripts`(8097)（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:1157/1166/1167/1332`，`grep "InScript"` 的全部命中），没有任何与声明式事件挂钩相关的门。VB 侧对应的脚本族是 `ERR_NamespaceNotAllowedInScript`(BC36965) / `ERR_KeywordNotAllowedInScript`(BC36966)（`Compilers\VisualBasic\Portable\Errors\Errors.vb:1598-1599`），同样无关。**这意味着候选 C（报诊断）没有现成的 C# 对应物可抄，只能新定义。**

### 4. 目标设计：子情形矩阵与落地路径

**子情形矩阵（决定方案粒度）：**

| # | 子情形 | 今天（构建无关） | 目标语义 | 所需改动层 |
|---|---|---|---|---|
| (i) | 提交类顶层 `Handles Me.E` / `MyClass.E`（关键字容器） | 崩溃 `:771` | 挂钩进脚本类的合成宿主 | 绑定宿主选择 + 注入点 |
| (ii) | 提交类顶层 `WithEvents x As New R` + `Handles x.E`（`WithEvents` 容器），**两者同处本次提交** | 崩溃 `:771` | 复用 `Set` 访问器挂载（§2.2） | 只放行 TypeKind 闸；**前提见 §2.6** |
| (iii) | **跨提交**：`x` 声明于前序提交，本提交写 `Handles x.E` | 崩溃 `:702` | 待定（见 Unresolved 3）：前序 `Set` 访问器不可复用（§2.6），挂钩写入必须落在本提交自己的方法里 | 接收方合成 + 挂钩注入宿主 |
| (iv) | 嵌套类型内的 `WithEvents` + `Handles` | **已可用**（已运行） | 不变 | 无 |
| (v) | 顶层 `WithEvents` 仅声明（无 `Handles`） | Release 可用；Debug 断言误伤 | 可用 | 放宽 `SourceWithEventsBackingFieldSymbol.vb:66` 断言 |
| (vi) | 非提交脚本类（`DeclarationKind.Script` → `TypeKind.Class`，`SourceMemberContainerTypeSymbol.vb:156-157`） | **符号层走 Class 分支，注入层被 `MethodCompiler.vb:1482` 跳过** | 与 (i) 同 | 与 (i) 同 |

子情形 (vi) 是既有资料的第二处转述差异：`meeting-scripting-dialect.md:150` 与 `spec:348` 都说「非提交脚本类走普通 Class 路径」。这在**符号合成层**（`SourceMemberContainerTypeSymbol.vb:2808`）成立，在**挂钩注入层**不成立——`IsScriptConstructor` 对 `DeclarationKind.Script` 的合成构造器同样为真（`MethodSymbol.vb:517-521` + `SourceMemberContainerTypeSymbol.vb:1295-1300`），于是 `MethodCompiler.vb:1482` 同样让它 `body = block`。**已检查（代码路径）；运行后果未实证（§8 第 2 项）。**

**落地路径（推荐顺序，取舍留 LDM）：**

**P-A1（最小完整实现）：只放行 `WithEvents` 容器路径，即子情形 (ii) + (v)，且语义上限定在「`WithEvents` 与 `Handles` 同处一次提交」（§2.6）。** 在该前提下改动面最小且**不新增任何合成符号**——挂/摘钩完全由既有的 `Set` 访问器承担（§2.2），脚本类顶层 `Dim x As New R` 的赋值走同一提交的 `<Initialize>`（§2.5），`Set` 访问器被触发，挂钩发生。需要动的只有两处门：`SourceMemberMethodSymbol.vb:762-772` 把 `TypeKind.Submission` 与 `Class`/`Module` 同列（或为脚本类单列分支），以及 `SourceWithEventsBackingFieldSymbol.vb:66` 的断言。实现面（1 个方法 + 1 个断言）、影响面（只扩宽承认的 `TypeKind`，普通类零改动）、测试面（提交用例 + 嵌套回归网 `VisualBasicEmitTest\CodeGen\CodeGenWithEvents.vb`）都在可控范围。**收益**：`WithEvents`（`As New` 初始化器形式）+ `Handles` 这个最贴近 VB6/VBA 习惯的组合在**单次提交内**可用。**已检查 + 部分已运行**：§2.2 的挂/摘语义与「`As New` 初始化器触发 `Handles` 挂钩」已在嵌套类型上跑通；提交类上的同一链路因 (i) 未放行，**未运行验证**（§8 第 1 项）。

**P-A2：补关键字容器，即子情形 (i) + (vi)。** 需要为脚本类指定一个「挂载宿主方法」。两个候选位置各有代价：

- **`<Initialize>`（`SynthesizedInteractiveInitializerMethod`）**：与字段初始化器同体（§2.5），时序自然；但它是 `MethodKind.Ordinary`（`:105-107`），而挂钩循环靠 `handledEvent.hookupMethod.MethodKind = constructorMethod.MethodKind` 认领（`InitializerRewriter.vb:111`），**不会匹配**，必须在 `BuildScriptInitializerBody`（`:174-186`）里新开一段挂钩循环。挂钩与初始化器的先后（镜像 `BuildConstructorBody` 是「挂钩先于初始化器」，`InitializerRewriter.vb:86-142` 的挂钩段在初始化器语句之前）需要裁定——脚本顶层 `Dim` 的处理者可能读到后面才初始化的字段。
- **提交构造器**：与普通类的「构造器承载」一致，注入点已存在（`MethodCompiler.vb:1553-1560` 的三段式组装），但脚本构造器的形参是 `submissionArray`（`SynthesizedSubmissionConstructorSymbol.vb:19-38`），且它的体先跑基构造器调用与提交数组初始化。往这里插挂钩会让「`<Initialize>` 才承载实例状态」的现有分工变浑。

**A2 的 `MyBase` 边界**：提交类无基类型（`ImplicitNamedTypeSymbol.vb:51-60` 对 `Submission` 返回 `Nothing`，`SourceNamedTypeSymbol.vb:1431-1436` 同结论），`Handles MyBase.E` 语义为空，需要单独裁定（Unresolved 6）。

**P-A3（最贵，可延后）：子情形 (iii) 跨提交 `WithEvents`。** 今天的 `isFromBase` 分支要造覆盖属性（`SourceMemberMethodSymbol.vb:700-705` → `SourceNamedTypeSymbol.GetOrAddWithEventsOverride`，`SourceNamedTypeSymbol.vb:2738-2752`），但脚本类不是 `SourceNamedTypeSymbol` 且 `GetSynthesizedWithEventsOverrides` 对脚本类返回空（`ImplicitNamedTypeSymbol.vb:213-219`）。§2.6 进一步说明：**就算覆盖机制修好，前序提交的 `Set` 访问器也无法承载后写的 `Handles`**，所以这条路径必须自带挂钩注入宿主，不能靠「复用 Set 访问器」。要裁定的两件事：挂钩写进后一提交的哪个方法（`<Initialize>` 还是提交构造器），以及接收方如何合成（§5.2 已运行证实前序实例可达）。**已检查；引入成本最高。**

### 5. 替换与摘钩：同名 `WithEvents` 在新提交里能否摘掉旧处理器

这是用户明确关心的问题，本节给出可判定的部分与不可判定的部分。

**(5.1) 「同名」可判定，但判定结果不是「替换」。** 跨提交成员查找 `LookupInSubmissions` 从当前提交沿 `PreviousSubmission` 链回溯（`Binding\Binder_Lookup.vb:858-926`）：命中的成员若**不可重载**则 `Exit Do`（`:892-895`，新提交遮蔽旧的）；若**可重载**则继续收集同 kind 成员合并为重载集（`:897-913`）。而 `WithEvents` 属性的可重载性被显式排除：

```vb
136        Public Function IsOverloadable(symbol As Symbol) As Boolean
137            Dim kind = symbol.Kind
138            If kind = SymbolKind.Method Then
139                Return True
...
144            ' uncommon case - WithEvents property do not overload. They behave like fields when OHI is concerned.
145            Return DirectCast(symbol, PropertySymbol).IsOverloadable
...
152        Public Function IsOverloadable(propertySymbol As PropertySymbol) As Boolean
153            Return Not propertySymbol.IsWithEvents
```

—— `Symbols\SymbolExtensions.vb:136-146`（symbol 版，`:138-139` 方法恒可重载）、`:151-154`（property 版）。**所以：新提交再声明一个同名 `WithEvents`，在绑定层是「像字段一样遮蔽旧的」，不是重载合并**（**实锤**：代码路径 `:136-154` + `Binder_Lookup.vb:892-895`，且 REPL 实测跨提交重名不报诊断）。但**处理者方法是方法，方法可重载**（`:138-139`），所以「新提交写了同名处理者」**不构成替换判据**——绑定层不提供这条信息，必须作为**新规则**显式定义（**实锤**：`:138-139` + `Binder_Lookup.vb:897-913` 的重载合并分支）。

同一提交内不允许重名（`WithEvents src` 写两次 → BC30260 `ERR_MultiplyDefinedType3`，`Errors.vb:273`；实测于 `.vbx`，**已运行**），跨提交允许（REPL 实测两次声明同一名字无诊断，**已运行**）——这是「新提交 = 遮蔽」的直接运行证据。

**(5.2) 摘除：机制链路已跑通，但没有代码会把「重新声明」当作替换信号。**

`Handles` 编译为 `AddHandler`（`BoundAddHandlerStatement`，`InitializerRewriter.vb:135`），是订阅叠加，不是赋值；要摘必须显式 `RemoveHandler`，而 `RemoveHandler` 需要**等值委托**（CLR `Delegate` 按 `(target, method)` 比较）。跨提交场景下这条链的每一环都已跑通：

1. **前序提交实例可达。** 跨提交成员访问被绑定为 `BoundPreviousSubmissionReference`：

```vb
2612            If currentType.TypeKind = TYPEKIND.Submission AndAlso Not currentMember.IsShared Then
2613                If memberDeclaringType.TypeKind = TYPEKIND.Submission Then
2614                    Return New BoundPreviousSubmissionReference(syntax, currentType, memberDeclaringType)
```

—— `Binding\Binder_Expressions.vb:2611-2630`（`TryBindInteractiveReceiver`，隐式 `Me` 的插入点在 `:2560-2572`），降级为对 `SynthesizedSubmissionFields` 字段的读取（`Lowering\LocalRewriter\LocalRewriter_PreviousSubmissionReference.vb:14-25`；该字段是普通强引用字段，`Lowering\SynthesizedSubmissionFields.vb:22-34`、`:64`，故前序实例在会话链存活期间被强引用）。**已检查。**

2. **新提交能读到前序提交的 `WithEvents` 值。** REPL 实测（Release 构建，退出码 0）：sub1 写 `WithEvents src As New Ticker`，sub2 写 `? src Is Nothing` → `False`、`? src.GetType().Name` → `"Ticker"`。**已运行。**

3. **跨提交摘除。** 本轮用固定输入探针测了六组。结论：摘除成败由**一个变量**决定——处理者到事件的委托转换**是否需要 relax stub**；订阅来自 `Handles` 自动挂载还是命令式 `AddHandler` **不改变结果**（两组逐项相同）：

   | 探针 | 订阅来源 | 处理者 → 事件的转换 | 首次 raise | 摘除后 | 重挂后 |
   |---|---|---|---|---|---|
   | pA1 | `Handles` 自动挂载（嵌套 `Class Host`） | 直接兼容 | 触发 | **摘掉（无输出）** | **1 次** |
   | pA3 | `Handles` 自动挂载（事件声明为无参） | 直接兼容 | 触发 | **摘掉** | **1 次** |
   | pA2 | `Handles` 自动挂载 | 需 relax stub | 触发 | **摘不掉** | **2 次** |
   | pB1 | 命令式 `AddHandler`（跨提交） | 直接兼容 | 触发 | **摘掉** | **1 次** |
   | pB2 | 命令式 `AddHandler`（跨提交） | 需 relax stub | 触发 | **摘不掉** | **2 次** |
   | pA4 | `Handles` 自动挂载，**同类同提交** | 需 relax stub | 触发 | **摘不掉** | — |

   - **直接兼容组的输入（pA1/pA3，已运行，Debug 构建，退出码 0）**：提交 1 声明 `Class Ticker`（`Public Event Tick(sender As Object, e As EventArgs)` + `Public Sub RaiseIt()`）与嵌套 `Class Host`（`Public WithEvents src As New Ticker` + `Public Sub H1(sender As Object, e As EventArgs) Handles src.Tick`）；提交 2 `Dim h As New Host`（Host 的构造器运行 `src = New Ticker` 初始化器 → **`Set` 访问器执行 → `Handles` 自动挂钩发生**）；提交 3 `h.src.RaiseIt()` → 打印 `H1 fired`；提交 4 `RemoveHandler h.src.Tick, AddressOf h.H1`——处理者签名与事件签名**逐参相同**，转换不需要 relax stub（无 BC42328）；提交 5 `h.src.RaiseIt()` → **无输出**；提交 6 `AddHandler h.src.Tick, AddressOf h.H1`；提交 7 raise → 打印 **1 次**（不是 2 次）。pA3 把事件换成无参 `Event Tick()`、处理者换成 `Sub H1()`（`RemoveHandler` 行同式，仍直接兼容），结果逐项相同。
   - **需 relax stub 的对照输入（pA2/pB2，已运行）**：其余不变，只把处理者写成零参 `Sub H1()`、事件仍带 `(sender As Object, e As EventArgs)`；`RemoveHandler ..., AddressOf h.H1` 报 **BC42328**（「需要宽松转换」）且**摘除失败**——raise 仍打印，重挂后再 raise 打印 **2 次**。pB2（命令式 `AddHandler src.Tick, AddressOf H1`，处理者零参）数值逐项相同。**这就是限制 2 的运行侧证据**：relax 转换产生的委托目标是 `BuildDelegateRelaxationLambda` 合成的 lambda，用户侧无法重建等值委托。
   - **旁证（pA4，已运行）**：处理者零参、`RemoveHandler src.Tick, AddressOf H1` 写在**同一个类、同一次提交**内，同样**摘不掉**。⇒ 该限制不是「跨提交」造成的，而是 relax stub 本身不等值。

   直接兼容组还说明：`AddressOf H1` 的接收方被自动重绑到 **H1 所在提交的实例**上（`BoundPreviousSubmissionReference`），因此跨提交新建的委托与原先的委托目标相同、`Delegate.Remove` 成功。**已运行。**

   **未覆盖的形状**：`Handles` 子句写在**提交类顶层**时产生的订阅的跨提交摘除——结构上无法实测，因为顶层 `Handles` 必崩（§1 (a)/(b)），放行 `TypeKind` 闸之前产生不出这种订阅。外部复验（上一轮）用命令式、零参处理者（BC42328 宽松转换）测得**摘除失败**（该轮探针序列为 `AddHandler → raise → RemoveHandler → raise`，两次 raise 均触发、共 2 次触发，**无重挂相位**），与上表**需 relax stub** 两行的「摘除后摘不掉」同一口径，因此两轮数值的差异**已由「转换是否需要 relax stub」消解**（消解矩阵即上表），不是结论冲突；另记入 Unresolved 4。§8 第 1 项给出完整的未复现清单。

**结论（用户问题的三态回答）：**

- **「能否判定替换」→ 实锤：不能按既有语义判定，必须新定义规则。** 可用的原子判据只有两个：`WithEvents` 属性符号的同一性（新提交同名 → 遮蔽，可判定）与处理者方法名（大小写不敏感，可判定）；把这两者组合成「同名 = 替换」是一条**新规则**，不是既有语义的推论（`SymbolExtensions.vb:136-146`、`:151-154`、`Binder_Lookup.vb:892-895`、`:897-913`）。
- **「旧处理器能否被摘掉」→ 实锤：机制链路的每一环都已跑通（限定为「签名直接兼容、无 relax stub」的订阅；来源为 `Handles` 自动挂载与命令式 `AddHandler` 两种，均已运行，见上文矩阵），因此「判定替换 → 摘旧挂新」是一个可实现规则，不是空想。** 但必须连带三条限定：① **没有任何代码会把「新提交同名」当作替换信号并自动摘**，摘除必须作为新规则显式实现；② 实测覆盖的是**签名直接兼容**的订阅，**需 relax stub 的订阅摘不掉**（限制 2），且**未覆盖**顶层 `Handles`（提交类）产生的订阅（§8 第 1 项）；③ 规则作用域受下面第三条限制。
- **「旧处理器被闭包捕获会怎样」→ 实锤：`Handles` 语法面内不存在闭包订阅。** 判据是语法形状：`HandlesClauseItemSyntax` 的全部子节点只有 `EventContainer`（关键字容器或 `WithEvents` 容器）、`DotToken`、`EventMember`（**限 `IdentifierName`，只能是简单标识符**）（`Syntax\Syntax.xml:2032-2051`）——子句里没有任何委托/表达式位置，处理者恒为**带着该子句的那个命名方法本身**（合成式见 `SourceMemberMethodSymbol.vb:818-824` 的 `ImmutableArray.Create(Of MethodSymbol)(handlingMethod)`）。所以订阅委托的目标是「某提交实例 + 命名方法」，**在无需 relax stub 时可重建**。两条真实限制必须写进规则：
  1. 命令式 `AddHandler x.E, Sub() ...` 的委托目标是闭包实例，新提交无法重建等值委托 ⇒ **不在摘除规则范围内**（`Handles` 语法面覆盖不到它）。
  2. **签名需要 relax 的 `Handles` 订阅也不可重建。** `Handles` 允许零参处理方法（`Binder_Delegates.vb:334`，`isForAddressOf:=Not isForHandles`），这类转换需要 stub（`Semantics\Conversions.vb:4309-4316` 的 `AllArgumentsIgnored` 在需 stub 集合内），于是 `ReclassifyAddressOf` 在 `Binder_Delegates.vb:1048-1052` 判 stub、`:1058-1065` 调 `BuildDelegateRelaxationLambda`（`:1062` 传 `isZeroArgumentKnownToBeUsed`）造一个**合成 lambda**，委托目标不再是用户可写的命名方法。运行侧证实这类 `Handles` 可用（嵌套类型 `Sub H0()` 处理 `Event Tick(x As Integer)` → 打印 `H0 relaxed fired`，Debug 构建退出码 0，**已运行**），但**用户侧无法重建其委托**。⇒ 摘除规则的作用域必须限定在「签名直接兼容（无 relax stub）的 `Handles` 订阅」，或者由编译器自己合成等值合成方法（可行性**未验证**，见 Unresolved 4）。

### 6. 可运行示例

**今天能跑的例子（作为机制锚点，Debug 构建均退出码 0）：**

```vbnet
' nested.vbx —— 提交里的嵌套类型：WithEvents + Handles 今天就走得通
Imports System

Class Ticker
    Public Event Tick(sender As Object, e As EventArgs)
    Public Sub RaiseIt()
        RaiseEvent Tick(Me, EventArgs.Empty)
    End Sub
End Class

Class Host
    WithEvents src As New Ticker
    Sub H1(sender As Object, e As EventArgs) Handles src.Tick
        Console.WriteLine("nested H1 fired")          ' 输出：nested H1 fired
    End Sub
    Public Sub Go()
        src.RaiseIt()
    End Sub
End Class

Dim h As New Host
h.Go()
```

**目标行为（P-A1 落地后应可运行；语法本身今天已能解析——它正是让编译器崩溃的那段输入）：**

```vbnet
' top-level.vbx —— 目标：顶层 WithEvents 与 Handles 在提交里生效
Imports System

Class Ticker
    Public Event Tick(sender As Object, e As EventArgs)
    Public Sub RaiseIt()
        RaiseEvent Tick(Me, EventArgs.Empty)
    End Sub
End Class

WithEvents src As New Ticker                    ' 提交类的字段 → 展开为 _src + 同名属性（§2.1）

Sub H1(sender As Object, e As EventArgs) Handles src.Tick
    Console.WriteLine("H1 fired")               ' 目标：raise 时执行（挂钩经 §2.2 的 Set 访问器合成）
End Sub

src.RaiseIt()                                   ' 目标输出：H1 fired；退出码 0
Return 0
```

这段输入在两个构建下的实际结果见 §1 的 (a) 行：Debug 进程终止（退出码 35），Release 未处理异常（退出码 9）。**示例同时充当回归用例的骨架**：断言「处理者被调用一次」＋「`Return` 值即退出码」＋「嵌套类型用例零回归」。

### 7. 与其它单元的分工

- **`WithEvents` / `Handles` 在普通类与嵌套类型上的既有语义** —— 不在本文；回归网是 `Compilers\VisualBasicEmitTest\CodeGen\CodeGenWithEvents.vb`。
- **脚本类的声明模型、提交链、`<Initialize>`/`<Main>`/`<Factory>` 合成、跨提交可见性** —— `proposals\proposal-scripting-dialect.md`（本文只写事件挂钩这一面）。**特别地，`<Initialize>` 的调用时机与「每提交实例化」模型**属该提案的领域，本文 §2.6 只引用其结论。
- **顶层 `AddHandler` / `RemoveHandler` 作为可执行语句** —— 已由 `spec\spec-scripting-dialect.md:182-196` 规定，是**命令式**订阅面，与本文的声明式 `Handles` 面互补且不重叠。
- **`#Load` 多树提交** —— `proposals\proposal-load-directive.md`（本文 §2.6 与 Unresolved 5 只写它与挂钩写入宿主的相互作用面）。
- **spec 的方言边界段订正** —— `spec\spec-scripting-dialect.md:348` 现文称「A non-submission script class follows the ordinary class path」，该句在符号合成层成立、在挂钩注入层不成立（§4 (vi)）；且「a submission class synthesizes no hookup constructors」未描述用户实际遭遇的崩溃。spec 订正归 `spec\` 侧，本文只记事实与锚点。

### 8. 本阶段的证据边界（未复现项）

以下四项**尚未取得复现证据**（§5.2 #3 的摘除矩阵不在其列——那六组固定探针已跑通，其**未覆盖的形状**即本节第 1 项），引用本提案相应结论时不得当作已验证事实：

1. **`Handles` 自动挂载订阅的跨提交摘除（提交类顶层形态），以及 P-A1 在提交类上的同提交形态**：§5.2 #3 的摘除矩阵已跑通，但用的是**嵌套类型**里的 `Handles` 自动挂载；**提交类顶层** `Handles` 产生的订阅无法用于该实验——顶层 `Handles` 必崩（§1 (a)/(b)），在 `TypeKind` 闸放行之前产生不出这种订阅。因此 §4 P-A1 描述的「提交类顶层 `WithEvents` 容器 + 同提交 `Handles`」链路**只有代码路径证据**，其运行后果（挂钩是否真的发生、`Set` 访问器是否在生成体时已看到 `HandledEvents`）**未验证**。
2. **非提交脚本类（子情形 (vi)）的运行后果**：本 fork 的脚本宿主（`vbi` 文件执行与 REPL）都走 `CreateScriptCompilation(..., isSubmission:=True)`（`VisualBasicScriptCompiler.vb:208` → `VisualBasicCompilation.vb:368-389`）。理论上另一条路是「Script 解析选项 + 普通 `Create`（`isSubmission:=False`）」（`vbc` 侧确有 `SourceCodeKind.Script` 解析选项的构造点，`VisualBasicCompiler.vb:96`），但**本轮与外部复验都未找到可用的 CLI 开关**产出该形态，因此「其合成构造器被 `MethodCompiler.vb:1482` 跳过」只有代码路径证据，无运行证据。
3. **relax stub 合成方法是否跨编译稳定**（把 relax 情形纳入 Unresolved 4 的摘除规则的前提）：同一 `(处理者方法, 事件)` 在不同提交里调用 `BuildDelegateRelaxationLambda`（`Binder_Delegates.vb:1058`）是否落到同一方法定义、从而产生等值委托——**未验证**。
4. **`IsWithEvents` 对 VB 消费方的语义**：只在 C# 适配层核到恒 `false`（`PublicModel\PropertySymbol.cs:89-92`）。VB 侧另有真实实现——源符号读标志位（`SourcePropertySymbol.vb:743-747`）、覆盖属性恒真（`SynthesizedOverridingWitheventsProperty.vb:69-73`）、元数据侧由 `PENamedTypeSymbol.vb:717` 置位后经 `PEPropertySymbol.vb:257-278` 读出。**这些只做了代码核对，未做跨语言/跨程序集消费方实测。**

## Drawbacks
[drawbacks]: #drawbacks

- **补齐挂钩会改变「今天能过」的脚本的运行时行为。** 顶层 `WithEvents` 单独声明在 Release 下今天能编译能跑（§1 (d)）；补上挂钩后它会开始真正订阅事件。这不是回归（今天的行为是缺陷），但确实改变了既有产物的可观察行为，spec 需明确这是修错而非语义变更。
- **自动摘钩规则与 VB `Handles` 的既有语义直接冲突。** `Handles` 在普通 VB 里允许多个处理者订阅同一事件、且从不相互摘除；「新提交同名 ⇒ 摘旧的」是一条**只为脚本方言存在**的规则，会让「同名」在两个语境下含义不同（§5.1）。这是本提案最大的语义代价。
- **摘除规则的作用域不完整。** 需要 relax stub 的 `Handles` 与命令式 `AddHandler`/lambda 都在规则之外（§5.2），用户会看到「有的能替换、有的不能」，规则可解释但需要学习成本。
- **「同一次提交」这个前提本身是用户可见的约束。** P-A1 只在 `WithEvents` 与 `Handles` 同处一次提交时成立（§2.6）。跨提交必须另付实现成本，而在补齐之前，用户会在 REPL 里遇到「先声明 `WithEvents`、下一条提交再写 `Handles` 不行」的落差。
- **改共享编译器树。** 三条落地路径都动 `Compilers\VisualBasic\Portable`（绑定宿主选择、注入点、`TypeKind` 闸），需登记 `upstream-merge.md`；若新增合成符号还会触及 `PublicAPI.*.txt`。
- **`<Initialize>` 承载挂钩会改变脚本初始化器的体形状。** 序列点、调试信息、以及「挂钩 vs 字段初始化器」的先后都要重新定义（§4 P-A2），而这些是脚本调试体验的既有基础。
- **跨提交挂钩把「提交只追加状态」变成「提交可修改前序状态」。** 摘钩本质上是让新提交去改前序提交对象上的订阅列表，这与提交链「每一步只新增自己的状态」的现有直觉不符，工具（LSP/补全）难以静态推断。

## Alternatives
[alternatives]: #alternatives

**A. 完整实现（提交类也合成 hookup 构造器 + 访问器）。**
要回答的是「hookup 构造器在**每提交实例化**的模型下什么时候跑」：提交类只有合成的 `Sub New(submissionArray As Object())`（`SynthesizedSubmissionConstructorSymbol.vb:19-38`），它的体由 `MethodCompiler.vb:1553-1560` 三段拼成（基构造器调用 + 提交数组初始化 + 剩余体），实例字段初始化器则完全不在构造器里、而在 `<Initialize>`（`:1488-1490`）。所以「照搬普通类的 hookup 构造器」在本模型下无处安放——`WithEvents` 容器的挂载根本不需要构造器（§2.2），关键字容器需要一个新宿主。
- **成本**：绑定宿主选择 + 注入点 + `TypeKind` 闸，共 3 处共享树改动；`MyBase` 语义需单独裁定。**若要把跨提交（(iii)）也纳入，还要加接收方合成与「后一提交自带挂钩注入宿主」**（§2.6）。
- **收益**：关键字容器与 `WithEvents` 容器都可用（跨提交部分需额外工作）。
- **证据等级**：已检查（全部锚点已复核）；`MethodCompiler.vb:1482` 的跳过后果为**推测**（未 prototype）。
- **判断**：**必要但可分期**——其中 `WithEvents` 容器部分（P-A1）在「同提交」前提下成本极低且复用既有完整机制，应独立拆出先行。

**B. 部分实现：只支持顶层（脚本类自身）的 `WithEvents`，不支持嵌套类型里的。**
**方向与事实相反，必须先纠正**：今天可用的恰恰是**嵌套类型**（§1 (e)，已运行），顶层不可用。因此本候选的实际内容应改写为「**最小可用子集**」：只做 P-A1（顶层 `WithEvents` 容器路径，前提为同提交 + 放宽误伤断言），关键字容器与跨提交暂缓。
- **成本**：2 处改动（`TypeKind` 闸、断言），不改 `MethodCompiler` / `InitializerRewriter`。
- **收益**：最贴近 VB6/VBA 习惯的写法在单次提交内可用；零新合成符号；普通类与嵌套类型零回归。
- **证据等级**：已检查 + 部分已运行（§2.2 的挂/摘语义与 `As New` 触发挂钩已在嵌套类型上跑通；提交类上放行 `TypeKind` 闸后的实际后果未运行）。
- **判断**：**性价比最高的第一步**；未覆盖的是 `Handles Me.E`、跨提交、以及提交类上的任何运行验证。

**C. 明确报诊断：不支持就报一条清楚的编译错误，替代当前的崩溃。**
- **选码**：既有族**不可直接复用**——`ERR_HandlesSyntaxInClass`(BC31412) 的文案是「'Handles' in classes must specify a 'WithEvents' variable, 'MyBase', 'MyClass' or 'Me' qualified with a single identifier.」（`VBResources.resx:2358-2361`）、`ERR_HandlesSyntaxInModule`(BC31418) 是「'Handles' in modules must specify a 'WithEvents' variable qualified with a single identifier.」（`:2367-2370`），两者的语义都是「限定形式不对」，不是「此处不支持」。`ERR_NoWithEventsVarOnHandlesList`(BC30506，`Errors.vb:403`) 同理。⇒ 需新增码，触发 VB 资源 + xlf 13 语言同步义务。
- **覆盖范围**：必须覆盖四条输入路径 / 三个崩溃点（(a)(b) 的 `:771` 闸、(c) 的 `:702` `DirectCast`、(d) 的 `SourceWithEventsBackingFieldSymbol.vb:66` 断言），否则「不静默」只补了一部分。
- **对照 C#**：无同类诊断可抄（§3 结论 4），只能新定义。
- **成本**：1 处判定 + 选码/文案/xlf；不动 `MethodCompiler`/`InitializerRewriter`。
- **收益**：把进程终止换成可读错误；但用户拿到的是「不支持」而不是「能用」。
- **代价**：若日后补挂钩，诊断要撤（迁移成本）。
- **证据等级**：已检查。

**D. 维持现状 + 文档化边界。**
当前 spec 已按这条写（`spec\spec-scripting-dialect.md:348` 把该行为划出保证范围）。**不可取**，两条理由：① 现状不是「静默失效」而是**进程终止**，把崩溃写进「边界」不成立——Release 下未处理 `InvalidOperationException`（退出码 9）没有任何用户体验可言，Debug 下断言终止更直接；② 与 `meeting-scripting-dialect.md:150` R11 的「不接受静默」裁决冲突，也与「不留遗留问题」的收口纪律冲突。列出此项是为了让会议有明确的否决记录。

**E. 复用 `WithEvents` 容器的既有挂载机制，不新建 hookup 机制（本提案倾向的路径）。**
`WithEvents` 容器的挂/摘钩已经完整实现于属性 `Set` 访问器（`SynthesizedPropertyAccessorBase.vb:143-196`、`:201-271`、`:273-300`、`:302-345`），且与类型种类无关（该文件 `TypeKind` 零命中）；脚本类顶层 `Dim x As New R` 的赋值进 `<Initialize>` 作为初始化器语句，`Set` 被触发（§2.5）。
- **重算后的定价（按 §2.6）**：该机制**只能覆盖「`WithEvents` 的赋值初始化器与 `Handles` 子句同处一次提交」这一形态**——因为挂/摘钩语句活在 `Set` 访问器体里，只在该属性被赋值时执行，而前序提交的 `<Initialize>` 不可能被后一提交重跑，前序提交的 `Set` 访问器也不认识后写的 `Handles`。这一前提把 E 的覆盖面明确限定为子情形 (ii)，**跨提交 (iii) 必须另配挂钩注入宿主**（多一份改动面 + 一份新规则）。在此前提下，E 的改动面仍是 2 处门（`TypeKind` 闸 + 断言），零新合成符号。
- **收益（限定后）**：单次提交内的 `WithEvents`（`As New`）+ `Handles` 组合可用。
- **边界**：关键字容器 (i) 与跨提交 (iii) 都不覆盖。
- **证据等级**：已检查 + 部分已运行（§2.2 挂/摘语义、`As New` 触发挂钩已在嵌套类型上跑通；提交类上未运行，见 §8 第 1 项）。
- **判断**：仍是**成本最低的第一步**，但它的「零新机制」性质只在同提交形态下成立；跨提交的部分不能算在 E 的成本里。

### 与 C# 的对照小结（承接 §3）

| 维度 | C# / csx | VB（本 fork） | 是否分叉 |
|---|---|---|---|
| 声明式事件订阅语法 | 无 | `WithEvents` + `Handles` | **是**（VB 独有，解释得了） |
| 字段式 `event` 的形状 | 后备字段 + 合成 add/remove（`SynthesizedEventAccessorSymbol.cs:24-33`） | 同构（`SynthesizedEventAccessorSymbol.vb:113-120`） | 否 |
| 订阅如何发生 | 用户语句 `E += h;` 或手写 `add`/`remove` 访问器体（`InitializerRewriter.cs:32-77` 无事件代码） | 编译器合成 `AddHandler` | **是**（机制差异的根源） |
| hookup 构造器 | **不存在** | 存在（`AddWithEventsHookupConstructorsIfNeeded`） | **是**（VB 需要，C# 不需要） |
| 提交构造器 | 只给 `submissionArray` 形参（`SynthesizedSubmissionConstructor.cs:12-30`） | 同构（`SynthesizedSubmissionConstructorSymbol.vb:19-38`） | 否 |
| 提交初始化器里的事件代码 | 无（`InitializerRewriter.cs:32-77`） | 无（`InitializerRewriter.vb:174-186`） | 否 |
| 脚本/提交类上的挂钩 | 无此概念 | 上游显式短路，登记为 dotnet/roslyn#14073（`ImplicitNamedTypeSymbol.vb:213-219`） | **缺口**（未解释的分叉） |

## Unresolved questions
[unresolved]: #unresolved-questions

1. **挂钩写入由哪一次提交承载（本提案的核心未决）。** §2.6 已证：`Set` 访问器只在属性被赋值时执行，而后一提交无法让前序提交的 `<Initialize>` 再执行一次。三条候选：(a) 限定语义，**要求 `WithEvents` 的赋值初始化器与 `Handles` 子句同处一次提交**（接受「跨提交不行」并把界写到 spec）；(b) 让挂钩代码写进**当前提交**的合成方法（`<Initialize>` 或提交构造器），接收方用 `BoundPreviousSubmissionReference` 指向前序实例（代价 = 新注入宿主 + 接收方合成，且与 (i) 一样要选宿主）；(c) 只做关键字容器、放弃 `WithEvents` 容器。「靠 `<Initialize>` 重入」不是候选——`<Initialize>` 由入口点在提交实例化时调用一次（`SynthesizedEntryPointSymbol.vb:377`、`:286-292`），语义上不存在重入。**需要什么证据**：先 prototype 放行 `TypeKind` 闸，实测同提交形态是否真的挂钩（§8 第 1 项），再据此给 (a)/(b) 定价。
2. **关键字容器的挂载宿主（子情形 (i)/(vi)）：`<Initialize>` 还是提交构造器？** 两者各有代价（§4 P-A2）：前者与字段初始化器同体但 `MethodKind.Ordinary`，与挂钩循环的 `MethodKind` 认领条件（`InitializerRewriter.vb:111`）不匹配，必须新写挂钩循环；后者复用既有的三段式构造器体组装（`MethodCompiler.vb:1553-1560`）但与「`<Initialize>` 承载实例状态」的现有分工冲突。**还需一并裁定挂钩与字段初始化器的先后**（普通类的既有顺序是挂钩段在初始化器语句之前，`InitializerRewriter.vb:86-142`）。
3. **跨提交 `WithEvents`（子情形 (iii)）的目标语义。** 两条候选：(a) 绕过覆盖机制，把挂钩直接挂到**前序提交实例**的 `WithEvents` 属性值上（`BindSingleHandlesClause` 的 `isFromBase` 分支对脚本类改走 `BoundPreviousSubmissionReference` 接收方）；(b) 让脚本类也参与覆盖机制（需先解掉 `ImplicitNamedTypeSymbol.vb:213-219` 的短路，并让 `ImplicitNamedTypeSymbol` 具备 `SourceNamedTypeSymbol` 的覆盖能力）。**注意**：§2.6 表明 (a)(b) 都还要额外回答「挂钩代码写进后一提交的哪个方法」——修好覆盖机制并不自动给出挂钩宿主。**需要什么证据**：(a) 需 prototype 验证合成接收方在 `Private` 属性上的可访问性检查是否放行（跨提交读前序顶层 `WithEvents` 已运行证实可行，但那是**值读取**，不是**挂钩合成**）。
4. **摘除规则的作用域与合成方法的重建。** 规则必须限定在**签名直接兼容（无 relax stub）**的订阅（§5.2 限制 2）；若要把 relax 情形也纳入，必须保证编译器对同一 `(处理者方法, 事件)` 每次合成出的 relaxation lambda 落到**同一个方法定义**（否则委托不等值，`RemoveHandler` 无效）。**未验证**（§8 第 3 项）。另需裁定「同名」是否覆盖**同名但签名不同**（重载）与「新提交重复声明 `WithEvents` 同名变量」两种情形。**两轮实测的数值差异已消解**：§5.2 #3 的固定探针矩阵显示自变量是**转换是否需要 relax stub**，与订阅来源（`Handles` 自动挂载 / 命令式 `AddHandler`）无关——`Handles` 组与命令式组逐项相同（pA1↔pB1 = 摘除生效、重挂后 1 次；pA2↔pB2 = 摘除失败、重挂后 2 次）；外部复验的**摘除失败**（其序列为 `AddHandler → raise → RemoveHandler → raise`，两次 raise 均触发、无重挂相位）对应其中的 **relax** 组，直接兼容组则为「摘除生效、重挂后 1 次」，两组结论一致而非冲突。
5. **`Suspect`/猜测：多树提交（`#Load`）下 `WithEvents` 与 `Handles` 分处两棵树时的行为。** 多树提交仍是**同一个 script class**（`VisualBasicCompilation.vb:366-389` 的 `CreateScriptCompilation` 多树重载；`SourceMemberContainerTypeSymbol.vb:2731-2732`、`:2762-2763` 的注释「a submission may span multiple script trees (#Load)」），因此 `:665` 的 `isFromBase` 判据（`ContainingType` 同一性）应当仍为 `False`、走同提交路径，挂钩写入的宿主也应当是同一个 `<Initialize>`（各树初始化器按树序进同一体）。**以上为猜测**：未运行验证，也未核对 `#Load` 情形下 `Set` 访问器与 `<Initialize>` 的组装顺序（加载树在前、主树在后，`proposal-load-directive.md` §5a）是否会产生「挂钩早于 `WithEvents` 赋值」之类的次序问题。
6. **`Handles MyBase.E` 在提交类上的语义。** 提交类无基类型（`ImplicitNamedTypeSymbol.vb:51-60`、`SourceNamedTypeSymbol.vb:1431-1436`），该子句语义为空。三选一：报错 / 报诊断 / 与 `Me` 等价。C# 无对应物可参照。
7. **诊断码选址与 xlf 义务（若采候选 C）。** 既有 `Handles` 诊断族语义不匹配（`VBResources.resx:2358-2370`），需新增码 → 走 VB 资源 + 13 语言同步；同时要决定诊断覆盖范围是否包含非提交脚本类（子情形 (vi)）与 Debug 断言点。
8. **`SourceWithEventsBackingFieldSymbol.vb:66` 的断言该怎么处理。** 对脚本类它必然为假（`ImplicitNamedTypeSymbol.vb:33-37`），Release 下被编译掉且产物正确（§1 (d) 已运行）。是放宽为 `Not Me.ContainingType.IsImplicitlyDeclared OrElse Me.ContainingType.IsScriptClass`，还是让脚本类的 `WithEvents` 后备字段不走这条断言，需与本主题的实现一起定，否则在断言存在的期间，Debug 构建下顶层 `WithEvents`（连带 P-A1 的任何用例）都测不了。该断言在 Debug 下还会**遮蔽 (c) 的跨提交崩溃**：跨提交用例的前序提交含顶层 `WithEvents`，Debug 实测终止于 `:66` 断言（退出码 35）而非 `:702`（§1 表 (c) 行），因此「断言存在期间测不了顶层 `WithEvents`」也包括 (c) 族。
9. **子情形 (vi)（非提交脚本类）是否一并覆盖。** 该形态的构造入口是「Script 解析选项 + `Create(..., isSubmission:=False)`」（`VisualBasicCompiler.vb:96` 是解析侧的构造点），与本 fork 的脚本宿主（`vbi` 文件执行与 REPL 都走 `CreateScriptCompilation(..., isSubmission:=True)`，`VisualBasicScriptCompiler.vb:208` → `VisualBasicCompilation.vb:368-389`）不是同一条路；本轮与外部复验都**未复现**该形态（§8 第 2 项）。若不一并覆盖，`spec:348` 与 `meeting:150` 中「非提交脚本类走普通 Class 路径」的表述必须订正为「符号层成立、注入层不成立」。
10. **`WithEvents` 容器的挂载时机与 `Nothing` 保护。** `Set` 访问器路径只在**属性被赋值**时挂钩（§2.2）；`WithEvents x As Raiser`（无初始化器、也无后续赋值）在普通 VB 里同样不挂钩。需确认这是有意语义还是缺口，并在 spec 里明确脚本侧的同一规则——这一条与 §2.6 的「同提交」前提叠加后，会决定用户能写出的最小可用形态。
11. **`Suspect`：本主题在 `upstream-merge.md` 无台账条目。** 现有账本未记录 `SourceMemberContainerTypeSymbol.vb:2806` 的 TODO 与 `ImplicitNamedTypeSymbol.vb:213-219` 的上游短路是否属于 fork 改动面。**未与 `upstream-merge.md:10` 的基准 commit 做 diff**，故标 `Suspect`；落实实现前需确认这两处在基准 commit 中的原状。

## 相关文档

- `InternalDevDocs\meetings\meeting-scripting-dialect.md:150`（RESOLUTION R11：「`WithEvents`/`Handles` 不得静默，实现挂钩或给显式诊断，二选一」）、`:172`（对应 TODO）
- `InternalDevDocs\spec\spec-scripting-dialect.md:348`（方言边界段「`WithEvents` in a submission class」；本文 §4 (vi) 与 §7 指出其两处与实现不符）、`:182-196`（顶层 `AddHandler`/`RemoveHandler` 作为可执行语句）
- `InternalDevDocs\proposals\proposal-scripting-dialect.md`（脚本声明与提交模型，本文的父邻域；`<Initialize>` 的调用时机与每提交实例化模型属该提案）
- `InternalDevDocs\proposals\proposal-load-directive.md`（多树提交，本文 Unresolved 5 的邻域）
- `InternalDevDocs\decisions.md` D5（基础功能以 C# / csi 为蓝本，须给「VB 特有的理由」排查结论；本文 §3 为该要求在本主题上的落实）
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb`、`ImplicitNamedTypeSymbol.vb`、`SourceMemberMethodSymbol.vb`、`SourcePropertySymbol.vb`、`SourceWithEventsBackingFieldSymbol.vb`（VB 侧符号层）
- `Compilers\VisualBasic\Portable\Compilation\MethodCompiler.vb`、`Analysis\InitializerRewriter.vb`、`Binding\SyntheticBoundTrees\SynthesizedPropertyAccessorBase.vb`、`Symbols\Source\SynthesizedEntryPointSymbol.vb`（挂钩注入层与提交实例化）
- `Compilers\CSharp\Portable\Symbols\Synthesized\SynthesizedSubmissionConstructor.cs`、`Symbols\Synthesized\SynthesizedEventAccessorSymbol.cs`、`Compiler\MethodBodySynthesizer.cs`、`Lowering\InitializerRewriter.cs`（C# 对照基准）
- `Compilers\VisualBasicEmitTest\CodeGen\CodeGenWithEvents.vb`（普通类 `WithEvents`/`Handles` 回归网）
- `InternalDevDocs\spec\spec-scripting-dialect.md:344` 的边界条目与 `InternalDevDocs\spec\README.md`（方言能力的归档面）
