# `For` 增强 / For Loop Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `For` 循环：支持一条语句内声明多个计数器变量与各自范围，`Exit For`/`Continue For` 可用显式变量指定要退出的嵌套层；同时修复 `For` 迭代变量在 lambda 中按迭代捕获的问题（警告 BC42324 及 `IndexOutOfRangeException`）。

## Motivation
[motivation]: #motivation

- 多层嵌套的索引循环（如遍历三维数组）必须为每一维写一条 `For` 并各自缩进，视觉上笨重；
- 嵌套循环中 `Continue For`/`Exit For` 只能作用于最内层，无法直接跳到/退出指定层；
- 在 lambda 中捕获 `For` 迭代变量时，VB 报告警告 BC42324，且因迭代变量被延迟读取，会在循环结束后取到末值，导致 `IndexOutOfRangeException`。

## Detailed design
[design]: #detailed-design

### 多计数器与多范围

```vb
' Multiple nested counter variables and ranges.
For z = 0 To maxDepth,
    y = 0 To maxHeight,
    x = 0 To maxWidth
    
    ....   
    
Next x, y, z
```

一条 `For` 语句以逗号并列多个计数器声明，每个计数器有自己的下界/上界（可配 `Step`）。`Next x, y, z` 顺序结束各层循环。它等价于嵌套的 `For z ... : For y ... : For x ...`，但更扁平、可读。

### `Exit For` / `Continue For` 的显式变量

```vb
' Explicit variable in `Exit For` and `Continue For` statements.
Continue For y

Exit For z
```

`Continue For y` 跳过并继续**名为 `y`** 的那一层循环的下一轮；`Exit For z` 退出**名为 `z`** 的那一层循环。未指定变量时保持现行语义（作用于最内层）。

### 迭代变量按迭代捕获修复

```vb
' Fix: Capture `For` variable per iteration to avoid
' reporting warning BC42324 AND throwing 
' 'IndexOutOfRangeException' in cases like this:
Let arr = {"Peaches", "Pears", "Plums"}
Let elementPrintActions = New List(Of Action)

For i = 0 To arr.Length - 1
    ' Warning BC42324: Using the iteration variable in
    ' a lambda expression may have unexpected results.
    ' Instead, create a local variable within the loop
    ' and assign it the value of the iteration variable.
    elementPrintActions.Add(Sub() Console.WriteLine(arr(i)))
    '                                                   ~
Next

For Each printAction In elementPrintActions
    printAction()
Next
```

当前实现中 `Sub() Console.WriteLine(arr(i))` 捕获的是同一个变量 `i`，循环结束后 `i == arr.Length`，调用所有 `printAction()` 都会因访问 `arr(arr.Length)` 抛 `IndexOutOfRangeException`，且编译器给出警告 BC42324。修复方案是让 `For` 迭代变量在**每一轮迭代**都生成新的捕获副本（与 C# 的 `for` 循环变量捕获语义一致），使每个 lambda 记住各自那一轮的 `i`。

## Drawbacks
[drawbacks]: #drawbacks

- 多计数器 `For` 的迭代顺序（内层 x 最快）与现有嵌套写法一致但需文档明确，避免与直觉冲突。
- `Continue For y`/`Exit For z` 依赖循环变量名，若循环体内部变量与计数器重名需有明确的名称解析优先级。
- 迭代变量按迭代捕获会改变变量生命周期语义，属行为变更，可能影响依赖旧捕获行为的既有代码。

## Alternatives
[alternatives]: #alternatives

- 多计数器 `For` 也可用元组范围（`For Each ... In`）表达，但本建议保持 `For` 的计数语义。
- 显式层跳转可改用带标签的 `Exit For`/`Continue For`（如 `Exit For outer`），而不仅限于变量名。
- 捕获修复也可要求程序员手工声明循环内局部变量（现状的规避方法），但编译器可自动完成更友好。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Next x, y, z` 各计数器在 `Next` 中被递增的顺序与省略部分 `Next`（如 `Next` 不带任何变量）时的语义。
- `Continue For y`/`Exit For z` 若同层存在多个同名计数器（不同 `For` 语句）时的解析规则。
- 多计数器 `For` 与 `Step`、`To` 上界的组合以及各计数器独立校验的语义。
