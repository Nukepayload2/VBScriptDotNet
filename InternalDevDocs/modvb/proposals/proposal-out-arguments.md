# Out Arguments / `Out` 实参与形参

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`Out` 提供两类能力：其一，**实参侧**——调用点用 `Out value` 标记某实参只用于输出，从而让 `Try*` 这类方法可以"边调用边隐式声明变量"（`If cache.TryGetValue(name, Out value) Then`）；其二，**形参侧**——用 `Out root As Integer` 声明只写参数，作为"惯用多返回值"（尤其模式方法）的载体，配合 `Return True, root:=1` 命名实参赋值。同时提供显式 `ByRef` 实参语法（`ReportErrors(ByRef diagnostics)`）用于需要清晰性与更强检查的场景。

## Motivation
[motivation]: #motivation

- 以 `TryGetValue` 为代表的 `Try*` 方法需要预先声明变量再传入，既啰嗦又会引发"变量可能未赋值"的空引用类警告。
- VB 缺少惯用的"多返回值"表达：要么返回元组/对象，要么用 `ByRef` 但读写皆可、检查宽松，容易产生副作用与歧义。
- 模式方法（第 7 章）天然需要"成功判定 + 提取值"的组合，`Out` 是它的基石。
- 调用点显式写 `ByRef` 能让意图更清晰，并获得更严格的检查与消歧。

## Detailed design
[design]: #detailed-design

实参侧 `Out`：调用点标记输出实参，允许变量推断与隐式声明。原文示例（第 8 章）：

```vb
' `Out` arguments avoid null-reference warnings, and
' allow for inference and implicit declaration of variables.
If cache.TryGetValue(name, Out value) Then
    Return value
End If
```

- `If cache.TryGetValue(name, Out value) Then`：`Out value` 表示该实参只用于接收输出。`value` 无需事先声明，编译器根据形参类型推断并在调用处隐式声明；命中分支 `Return value` 直接使用。
- 由于 `Out` 保证变量会被赋值，可避免"可能未初始化"的空引用警告。

显式 `ByRef` 实参语法：用于清晰、严格检查与需要时消歧。原文示例：

```vb
' Explicit `ByRef` argument syntax for clarity, stricter checking,
' and disambiguation when needed.
ReportErrors(ByRef diagnostics)
```

- `ReportErrors(ByRef diagnostics)`：在调用点显式写出 `ByRef`，明确该实参是引用传递。

形参侧 `Out`：用于惯用多返回值（例如模式方法）。原文示例：

```vb
' `Out` parameters for idiomatic multiple returns (e.g. pattern methods).
Function IsPerfectSquare(number As Integer, Out root As Integer) As Boolean
    
    Select Case number
        Case 1
            ' Simplified single-statement return with `Out` assigns.
            Return True, root:=1
            
        Case 4
            Return True, root:=2
            
        Case 9        
            Return True, root:=3

        Case Else
            Let intRoot As Integer = Math.Sqrt(number)

            If (intRoot * intRoot) = number Then
                Return True, root:=intRoot
            Else
                Return False, root:=Nothing
            End If
    End Select
End Function    
```

- `Function IsPerfectSquare(number As Integer, Out root As Integer) As Boolean`：`root` 是 `Out` 形参——调用方传入的变量只被写入、不被读取。
- `Return True, root:=1` / `Return True, root:=2` / `Return True, root:=3`：`Return` 通过**命名实参**给 `Out` 参数赋值，形成"返回值 + 命名 `Out` 赋值"的单语句多返回写法。
- `Let intRoot As Integer = Math.Sqrt(number)`：`Let` 为 Anthony 提出的新声明关键字（代替 `Dim`），此处作普通局部声明。
- 失败路径 `Return False, root:=Nothing` 显式把 `root` 置空，保证调用方看到的 `Out` 变量总是已赋值。

## Drawbacks
[drawbacks]: #drawbacks

- `Out` 与既有 `ByRef`、`Optional` 并存，形参修饰符的选择更多，语言学习面扩大。
- `Return True, root:=1` 这种"返回值 + 命名 `Out` 赋值"的新写法与现有 `Return expr` 语义不同，需要在规范中明确求值顺序与未赋值情况的处理。
- 调用点 `Out value` 的隐式声明依赖形参类型推断，若形参是泛型或重载，推断歧义可能增加。

## Alternatives
[alternatives]: #alternatives

- 继续用 `ByRef` + 先声明变量：保留现状，代价是样板代码与空引用警告。
- 用元组 / 返回对象表达多返回值：无需新语法，但改变了方法签名形状，`Try*` 风格不适用。
- 只引入调用点 `Out value`，不引入形参侧 `Out`；或反之。本建议二者同属一个正交设计。

## Unresolved questions
[unresolved]: #unresolved-questions

- 形参 `Out` 与 `ByRef`、`Optional` 的组合规则（如 `Optional Out` 是否允许、默认值语义）。
- 调用点隐式声明的变量作用域：`If ... Out value Then` 失败分支中 `value` 是否可见、是否保证已赋值。
- `Return True, root:=1` 中若省略命名 `Out` 赋值，编译器是报错还是隐式赋 `Nothing`。
