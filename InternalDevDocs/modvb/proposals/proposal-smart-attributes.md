# 智能属性 / Smart Attributes (`PropertyHandlerAttribute`)

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入"智能属性"（Smart Attributes）：自定义特性在自动属性与事件的既定位置注入代码，多个特性可按顺序组合出多种行为。特性通过继承 `PropertyHandlerAttribute` 并提供 `OnPropertySet` / `OnPropertyGet` 静态方法声明，编译器把这些方法注入到属性访问器的调用点。此方案旨在"无源码生成器（Source Generator）也能做声明式编程"，以简单性优先。

## Motivation
[motivation]: #motivation

如今要在属性上叠加校验、通知、撤销等横切行为，往往需要手写冗余的访问器代码，或引入源码生成器。Anthony 指出，第 13 章各策略在"最终用户体验、工具作者体验、性能、复杂度"上各有取舍，同一个场景可能用多种方式或组合解决。智能属性优先选择简单：把行为以特性形式声明在属性上，由编译器注入，无需源码生成器即可获得组合式、可复用的属性行为。

期望的结果：开发者只用 `<>` 特性列表即可声明式地组合 `Trim`、`Notify`、`Undoable`、`MaxLength` 等行为，行为本身作为 `PropertyHandlerAttribute` 的派生类独立维护。

## Detailed design
[design]: #detailed-design

### 使用点（Use Sites）

特性以逗号分隔组合在自动属性或事件上，按声明的顺序注入到 `Set`（以及必要时的 `Get`）的既定位置：

```vb
' Custom attributes inject code into auto-
' properties and events at defined points in
' in defined order. Allows for composing
' multiple behaviors declaratively.

' Example use sites:

<Trim, Notify, Undoable, MaxLength(25)>
Property Title As String = "New Listing"

<ThrowOnNull, Notify, Undoable, MaxLength(50)>
Property Description As String = "No description."

<Notify>
Property LastSaved As Date

<Notify, Undoable,
 Regex("^\d{3}-\d{2}-\d{4}$", "Must be of the form '###-##-####'.")>
Property ListingCode As String = "<None>"
    
<Wrapper>
Public Property Rating As Integer = 3

<NotSupported>
Public ReadOnly Property CanRead As Boolean

<IgnoreExceptions>
Event Closing As EventHandler

<RaiseAsync>
Event Saved As EventHandler
```

特性不仅作用于属性，也可作用于事件（如 `IgnoreExceptions`、`RaiseAsync`）。

### 处理器的声明

每个行为是一个继承 `PropertyHandlerAttribute` 的特性，通过 `OnPropertySet` / `OnPropertyGet` 静态方法声明注入逻辑：

```vb
' Example declarations:

Public Class TrimAttribute
    Inherits PropertyHandlerAttribute

    Public Shared Sub OnPropertySet(
                        sender As Object,
                        propertyName As String,
                        ByRef backingField As String,
                        ByRef value As String
                      )

        ' Doesn't set backing field; let's later handlers run.
        value = If(value?.Trim(), "")
    End Sub
End Class
```

`TrimAttribute.OnPropertySet` 不直接写回后备字段，只修改 `value`，让后续处理器继续运行。`sender`、`propertyName`、`backingField`、`value` 为编译器注入的标准参数。

### 可提前终止的处理器（返回 Boolean）

处理器可以返回 `Boolean`，返回 `True` 时 `Set` 提前退出：

```vb
Public Class IdempotentAttribute
    Inherits PropertyHandlerAttribute
    
    ' Causes `Set` to exit early if function returns true.
    Public Shared Function OnPropertySet(Of T)(
                             sender As Object,
                             propertyName As String,
                             ByRef backingField As T,
                             ByRef value As T
                           ) As Boolean

        Return (value ?= backingField)
    End Function
End Class
```

`IdempotentAttribute` 使用二值逻辑相等运算符 `?=`（见 `proposal-null-equality-operators.md`）判断新旧值是否相同，相同则返回 `True`，令 `Set` 提前返回、跳过后续处理器。

### 构造参数复制到调用点

特性构造参数会被复制到生成的调用点，因此处理器所需的额外参数不必保存为字段：

```vb
Public Class AutoRoundAttribute
    Inherits PropertyHandlerAttribute

    Sub New(digits As Integer)
        ' Because this constructor arguments are copied to the generated callsite,
        ' the 'digits' parameter doesn't actually have to be saved anywhere.
        Do Nothing
    End Sub

    Public Shared Sub OnPropertySet(
                        sender As Object,
                        propertyName As String,
                        ByRef backingField As Decimal,
                        ByRef value As Decimal,
                        digits As Integer
                      )

        value = Math.Round(value, digits)
    End Sub
End Class
```

`AutoRound(2)` 的 `digits` 参数直接出现在调用点的 `OnPropertySet(..., digits)` 形参中，无需在特性实例上保存。

### 校验与只读

```vb
Public Class ThrowOnNullAttribute
    Inherits PropertyHandlerAttribute

    Public Shared Sub OnPropertySet(sender As Object,
                                    propertyName As String,
                                    backingField As Object,
                                    value As Object)

        If value Is Nothing Then Throw New ArgumentNullException(propertyName)
    End Sub
End Class

Class NotSupportedAttribute
    Inherits PropertyHandlerAttribute

    Shared Sub OnPropertyGet(Of T)(obj As Object,
                                   propertyName As String,
                                   ByRef backingField As T,
                                   ByRef value As T)

        Throw New NotSupportedException(propertyName)
    End Sub

    Shared Sub OnPropertySet(Of T)(obj As Object,
                                   propertyName As String,
                                   ByRef backingField As T,
                                   ByRef value As T)

        Throw New NotSupportedException(propertyName)
    End Sub
End Class
```

`ThrowOnNullAttribute` 在 `value` 为 `Nothing` 时抛出 `ArgumentNullException`；`NotSupportedAttribute` 同时声明 `OnPropertyGet` 与 `OnPropertySet`，用于"不支持读写"的属性（如 `CanRead`），抛出 `NotSupportedException`。

### 撤销（Undo）

```vb
Public Class UndoableAttribute
    Inherits PropertyHandlerAttribute

    Public Shared Sub OnPropertySet(obj As Object,
                                    propertyName As String,
                                    previousValue As Object,
                                    ByRef newValue As Object)

        UndoRedo.Remember(Sub() CallByName(obj, propertyName, CallType.Set, previousValue))
    End Sub
End Class
```

`UndoableAttribute` 把旧值登记进 `UndoRedo`，以便撤销时用 `CallByName` 恢复。

## Drawbacks
[drawbacks]: #drawbacks

- 特性注入是隐式的控制流，访问器背后的真实行为不再直观可见，调试时可能困惑。
- `OnPropertySet` / `OnPropertyGet` 依赖命名约定与签名形态，编译器对形参类型（如 `ByRef T`）的处理复杂。
- 与源码生成器方案（见 13.2）存在重叠，两者如何取舍与共存仍需反馈与调和。

## Alternatives
[alternatives]: #alternatives

- 不做事先注入，改用源码生成器（13.2 的 `Replaceable`/`Replaces` 方案）生成访问器代码——能力更强但工具与体验更复杂。
- 仅靠手写访问器或现成 MVVM 框架（如 INotifyPropertyChanged 助手类），不引入编译器机制。
- 允许自定义特性声明任意多个注入点（如 `OnPropertyChanged`），而非固定 `Set`/`Get` 两点。

## Unresolved questions
[unresolved]: #unresolved-questions

- 特性在 `Set`/`Get` 中注入的"既定位置与既定顺序"到底如何精确定义（多个处理器的先后、与后备字段写入的相对顺序）。
- 处理器签名如何匹配到调用点（按名字约定 `OnPropertySet`/`OnPropertyGet` 还是按参数类型推断）。
- 事件（`IgnoreExceptions`、`RaiseAsync`）注入的语义与位置。
- 特性参数复制到调用点后，特性实例本身是否可以省略（`Do Nothing` 构造）的做法是否为推荐形态。
- 原文第 1959 行代码中残留的 `#End Region` 是否为笔误，规范需确认。
