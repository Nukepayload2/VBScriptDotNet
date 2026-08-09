# Guarded `Let`（守卫式声明） / Guarded Let

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。为 `Let` 声明引入守卫式分支：变量初始化值是否为空决定执行指定的控制流语句（`Return`/`Throw`/`Continue` 等）或一段 `Then`/`Else` 代码块，从而在声明处直接完成空值检查，消除重复的空判断样板代码。

## Motivation
[motivation]: #motivation

作者（18.2）先是听到一位 F# 开发者抱怨函数式表达式缺少提前返回（early return），随后尝试把一段典型的服务获取代码改写成 ModVB。最初的版本反复做"取服务 → 判空 → 提前返回"，单调且重复：

```vb
Function GetIVsTextView() As IVsTextView
    
    Let textManager = GetService(Of SVsTextManager)
    If TypeOf textManager IsNot IVsTextManager Then Return Null
        
    Let rdt = GetService(Of SVsRunningDocumentTable)
    If TypeOf rdt IsNot IVsRunningDocumentTable Then Return Null
        
    Let hresult = FindAndLockDocument(rdt, Out docData As IntPtr)
    If Not ErrorHandler.Succeed(hresult) Then Return Null
    If docData = IntPtr.Zero Then Return Null
        
    Let textBuffer = Marshal.GetObjectForIUnknown(docData)
    If textBuffer Is Null Then Return Null
        
    hresult = GetActiveView(textManager, 1, textBuffer, Out textView)
    If Not ErrorHandler.Succeeded(hresult) Then Return Null
        
    Return textView

End Function
```

这段代码"压抑地单调"（oppressively monotonous），作者尝试过元组、可空性、`Out` 变量等任何能打破这一无聊模式的想法。他并不喜欢其他语言（如 Swift）的 `if let`/`guard let` 语法，因为读起来不自然；他意识到自己其实认同这个概念、只是不喜欢那种写法，于是提出下面的语法以消除大量重复。

## Detailed design
[design]: #detailed-design

### 单语句守卫：`Else <控制流语句>`

声明并初始化一个变量（或一组变量）；若初始化后的值为空，则执行指定的控制流语句（如 `Return`、`Throw`、`Exit`、`Continue` 等）：

```vb
' Declares and initializes a variable (or variables).
' If the value of the variable after is null, executes
' the specified control flow statement.
' (e.g. `Return`, `Throw`, `Exit`, `Continue`, etc)
Let variable = expression Else <Control Flow Statement>

' e.g.
Let v = e Else Continue For
```

### 单语句守卫：`Then <控制流语句>`

若变量不为空，则执行指定的控制流语句：

```vb
' Executes specified control flow statement if variable
' is not null.
Let variable = expression Then <Control Flow Statement>

' e.g.
Let v = dictionary.TryGetValue("key") Then Return v
```

### 块形式：`Then ... End Let`

若变量被初始化为非空值，则执行块内语句；变量在 `End Let` 之后失去作用域：

```vb
' Executes statements if variable is initialized to
' non-null value. Variable loses scope after `End Let`.
Let variable = expression Then
    ...
End Let
```

### `Then` 与 `Else` 组合

```vb
' `Then` and `Else`.
' e.g.
Let v As T = TryCast e Then
    ...
Else
    ...
End Let
```

### 条件 `Set`

作者还设想守卫式写法是否适用于 `Set` 赋值：

```vb
' And what about conditional 'Set'?
Let list As List(Of Object)
Set list = lists.TryGetValue("key") Else
    list = New List(Of Object)
    lists.Add("key", list)
End Set
list.Add(value)
```

## Drawbacks
[drawbacks]: #drawbacks

- 同一 `Let` 关键字被赋予"守卫分支"语义，与普通声明的含义混在一起，可能降低可读性。
- `Then`/`Else` 单语句形式与块形式的规则叠加后语法面较宽，需要仔细界定作用域，学习成本不低。
- 作者自己对块形式中变量作用域的疑问（见 Unresolved questions）说明该设计仍未收敛，存在"看起来不错、边界难定"的风险。

## Alternatives
[alternatives]: #alternatives

- 保留现有的 `Let` + `If ... Then Return Null` 样板，不引入守卫语法。
- 采用其他语言的 `if let`/`guard let` 风格（作者明确表示不喜欢这种读法）。
- 借助元组、可空性分析与 `Out` 变量组合来消除样板，而不改动声明语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文对块形式的 `Else` 写法的疑问（原样摘录）：

  > `' Not sure about this:`（指下面这段——变量 `list` 在 `End Let` 之后仍处于作用域）
  > `' But `list` shouldn't be in scope here.`（但 `list` 在这里本不该在作用域内）
  > `' Should it have been `End Else`?`（这里应该是 `End Else` 吗？）
  > `' This logic should maybe be in a TryGetValueOrCreate?`（这段逻辑或许应该放进一个 `TryGetValueOrCreate`？）

  ```vb
  Let list = lists.TryGetValue("key") Else
      list = New List(Of Object)
      lists.Add("key", list)
  End Let
  list.Add(value)
  ```

- 条件 `Set` 的形式是否成立、`End Set` 与 `End Let` 的对应关系如何界定未定（原文"Not sure about this"）。
- 该构造如何与"Result Pattern"（结果模式）配合使用尚不清楚；作者提到尤其期待 `Case` 类能让这类类型的定义变得更简单，但仅停留在兴趣层面，未给出设计。
- 守卫语句的作用域规则（变量在 `End Let` 后是否存活）是最大待定点。
