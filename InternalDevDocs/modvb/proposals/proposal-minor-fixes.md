# Bug Fixes & Other Minutiae / 杂项修复

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

一组互不相关的小修复与语法放宽，修正既有行为或消除不必要的样板：`Async Function Main` 合法化、`Optional` 参数默认值推断、`Optional` 关键字在首个可选参数后可省略、属性/事件访问器 `value` 参数类型推断、`Custom` 关键字在自定义事件声明中可省略、`NameOf` 调用省略括号、`NameOf` 支持开放泛型类型。

## Motivation
[motivation]: #motivation

这些都是长年存在的痛点：`Main` 无法直接 `Await`、`Optional` 参数被迫写默认值、可选参数连串声明时每个都要带 `Optional`、访问器里 `value`/`sender`/`e` 的参数类型只是对已声明类型的重复、`NameOf` 需要括号且不能引用开放泛型。逐一消除可减少样板并修正不合直觉的行为。

## Detailed design
[design]: #detailed-design

### `Async Function Main` 合法化

允许入口点 `Main` 返回 `Task`/`Task(Of Integer)` 并直接 `Await`，取代必须包一层异步辅助方法的绕行：

```vb
Async Function Main(args As String()) As Task(Of Integer)

    Await Console.Out.WriteLineAsync("This is an example.")

    Return 0
End Sub
```

（原文此例以 `End Sub` 收尾，疑为笔误，应为 `End Function`，见 Unresolved questions。）

### `Optional` 参数默认值推断

`Optional` 参数可以不写默认值，编译器按类型推断（如 `String` → `Nothing`/空串策略由设计决定）：

```vb
Function BuyCar(
           make As String,
           model As String,
           year As Integer,
           Optional trim As String ' <-- No default here.
         )
         As Car
```

### `Optional` 关键字在首个可选参数后可省略

原文以问号标注，属候选方向——从第一个可选参数起，后续参数无需重复写 `Optional`：

```vb
Function MakeDateTime(
           year As Integer,
           month As Integer,
           day As Integer,
           Optional
             hour As Integer,
             minute As Integer,
             second As Integer,
             millisecond As Integer,
             microsecond As Integer
         )
         As Date
```

### 访问器 `value` 参数类型推断

属性 `Set` 与自定义事件的访问器形参类型可由所在成员声明推断，无需重复书写。

**Before**（当前：必须写全类型）：

```vb
' Before
Property Name As String
    Get

    End Get
    Set(value As String)

    End Set
End Property

' We shall never know the delegate type of this event.
Custom Event NameChanged As EventHandler(Of EventArgs)
    AddHandler(value As EventHandler(Of EventArgs))

    End AddHandler
    RemoveHandler(value As EventHandler(Of EventArgs))

    End RemoveHandler
    RaiseEvent(sender As Object, e As EventArgs)

    End RaiseEvent
End Event
```

**After**（ModVB：类型省略，由 `Property`/`Event` 声明推断）：

```vb
' After
Property Name As String
    Get

    End Get
    Set(value)

    End Set
End Property

Event NameChanged As EventHandler(Of EventArgs)
    AddHandler(value)

    End AddHandler
    RemoveHandler(value)

    End RemoveHandler
    RaiseEvent(sender, e)

    End RaiseEvent
End Event
```

### `Custom` 关键字在自定义事件声明中不要求

上例同时演示：`Custom Event NameChanged` 在 ModVB 中直接写作 `Event NameChanged`，`Custom` 关键字不再要求。

### `NameOf` 调用省略括号

`NameOf` 可作为无括号的形式使用（原文对 `NameOf` 省略括号与开放泛型两处均标有 "?"，见 Unresolved questions）：

```vb
Public Shared ReadOnly HeaderTextProperty As DependencyProperty =
                         DependencyProperty.Register(
                           NameOf HeaderText, ' <-- No parentheses.
                           GetType(String),
                           GetType(MyControl)
                         )
                                            
Public Property HeaderText As String
    Get
        ...
```

（示例中 `Get` 块内 `...` 为原文省略。）

### `NameOf` 支持开放泛型类型

原生 VB 中 `GetType(Dictionary(Of,))` 合法，但 `NameOf` 只能用封闭泛型；ModVB 允许 `NameOf` 直接引用开放泛型：

```vb
' Vanilla VB
' This is legal:
Shared ReadOnly DictionaryType As Type = GetType(Dictionary(Of,))

' But you're required to write this:
Shared ReadOnly DictionaryName As String = NameOf(Dictionary(Of Object, Object))
```

```vb
' ModVB
' Now this is also legal:
Shared ReadOnly DictionaryName As String = NameOf Dictionary(Of,)
```

## Drawbacks
[drawbacks]: #drawbacks

- 各项改动虽小，但彼此独立，需分别评估对既有代码的破坏面（如 `Optional` 省略关键字可能改变既有参数列表的解析）。
- `NameOf` 省略括号与"开放泛型"叠加后，`NameOf` 的解析规则变得更复杂，可能引入歧义（例如与类型字面量的区分）。
- 访问器 `value` 类型推断依赖于成员声明上下文，报错信息可能变得更间接。

## Alternatives
[alternatives]: #alternatives

- 逐项取舍：每一项都可独立决定做或不做（例如 `NameOf` 省略括号可单独定案，不随其余项）。
- `Optional` 默认值推断也可用显式默认值表达式或 `= Nothing` 表达，保持显式优先。
- 访问器类型推断与本仓库"简写属性与事件"建议中的 `Set(value)` 简写配套演进。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文 `Async Function Main` 示例以 `End Sub` 结尾，应为 `End Function`——疑似原文笔误。
- "`Optional` 关键字在首个可选参数后可省略"原文以 "…optional after first optional parameter?" 标注问号，未定案。
- `NameOf` 省略括号原文以 "Optional parentheses for `NameOf` expressions?" 标注问号；且原文标题写作"Open generic types/methods(?)"，对"开放泛型方法是否同样允许"也存疑。
- `Optional` 参数省略默认值后的实际默认值语义（`String` 取 `Nothing` 还是空串等）未定。
- `Custom` 关键字省略与"简写属性与事件"建议中自定义事件访问器简写的相互作用。
