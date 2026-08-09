# Abbreviated Properties & Events / 简写属性与事件

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

压缩属性（`Property`）与事件（`Event`）声明中的样板：计算型只读属性可直接用 `Return` 表达式或裸表达式；访问器可省略 `End Get`/`End Set`；`Set(value)` 可省略形参类型；自定义事件的 `AddHandler`/`RemoveHandler`/`RaiseEvent` 访问器同样支持简写。此外允许用 `#If` 预处理指令为属性（特性）做条件选择。原文该节无独立标题，仅标注"New since summary published"。

## Motivation
[motivation]: #motivation

- 计算只读属性在 VB 中通常需要 5 行（`Property` + `Get` + `Return` + `End Get` + `End Property`），绝大多数只是"算一个表达式"，应简化为 2 行。
- 属性/事件的访问器块边界 `End Get`/`End Set` 等对读者是噪音，在访问器体很短时可以省略。
- 跨平台代码中常用 `#If` 在不同平台选择不同特性（如 `Serializable` vs `DataContract`），应允许特性本身被预处理条件包裹。

## Detailed design
[design]: #detailed-design

### 计算只读属性：直接 `Return` 表达式

原来的 5 行缩为 2 行——`Return` 后面的表达式即整个 `Get` 访问器：

```vb
' Computed `ReadOnly` Properties.
' Now 2 lines instead of 5.
ReadOnly Property DiscountedPrice As Decimal
    Return Price - (Price * DiscountRate)
```

### 备选形态：`Get` 后直接跟表达式

另一种写法是 `Get` 后直接书写表达式（不写 `Return`）。原文用注释标注疑问 "`Return` or `Get`?"，即这两种形态孰优尚未定案：

```vb
' `Return` or `Get`?
ReadOnly Property DiscountedPrice As Decimal
    Get Price - (Price * DiscountRate)
```

### 省略访问器 `End` 语句

`Get`/`Set` 访问器可以省略各自的 `End Get`/`End Set`，块结束由下一个访问器或 `End Property` 界定。`Set` 形参写作 `Set(value)`，省略类型标注：

```vb
' Accessor `End` Statements can be omitted.
Property Age As Integer
    Get
        Return _Age
        
    Set(value)
        If value < 0 Then Throw
            
        _Age = value
End Property
```

### 自定义事件访问器简写

自定义事件（`AddHandler`/`RemoveHandler`/`RaiseEvent`）的访问器同样省略 `End`，且 `RaiseEvent` 直接以参数调用后备委托：

```vb
Event AgeChanged As EventHandler
    AddHandler(value)
        _AgeChanged += value
        
    RemoveHandler(value)
        _AgeChanged -= value
    
    RaiseEvent(sender, e)
        _AgeChanged?(sender, e)
    
End Event
```

（示例中 `_AgeChanged?(sender, e)` 是对后备委托的空条件调用。）

### 预处理条件特性

允许特性列表被 `#If ... #Else ... #End If` 包裹，按编译常量选择不同特性：

```vb
' Pre-processing conditional attributes.
#if PLATFORM = "NetFX" Then
    <Serializable>
#Else
    <DataContract>
#End If
Class Message
    ...
End Class
```

示例中的 `...` 为原文对类体的省略。

## Drawbacks
[drawbacks]: #drawbacks

- 省略 `End Get`/`End Set` 后，块边界改为"下一个访问器 / `End Property`"，对解析器与人类读者都需要重新学习界定规则，多访问器属性更容易读错。
- `Get` 后直接跟表达式与"常规 `Get` 块"两种形态并存，增加语法歧义与工具链复杂度。
- `RaiseEvent` 只接受参数、不再有独立的块体，事件的错误处理（如线程切换）样板可能无处安放。

## Alternatives
[alternatives]: #alternatives

- 保持现状：接受属性/事件的样板代码，不做简写。
- 只保留"`Return` 表达式"一种计算属性形态，避免 `Get` 裸表达式形态（原文已对该取舍打上问号）。
- 特性条件选择改用 `Conditional` 特性或类型替代方案，而非预处理指令。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确标注 "`Return` or `Get`?"：计算只读属性的首行关键字到底用 `Return` 还是 `Get`，尚未定案。
- 省略 `End Get`/`End Set` 后，访问器边界的确切规则（尤其 `Set` 无 `End Set` 时如何与后续成员区分）未在原文给出。
- `Set(value)` 省略类型后如何推断 `value` 的类型——与"杂项修复"中的"访问器 value 参数类型推断"直接相关。
- 自定义事件 `AddHandler`/`RemoveHandler`/`RaiseEvent` 简写后，"`Custom` 关键字可省略"（见"杂项修复"一节）是否随之成立，原文未在本文说明。
- `#If PLATFORM = "NetFX" Then` 对特性的条件选择，与"语义预处理（`##If TYPE_EXISTS` 等）"的边界如何划分。
