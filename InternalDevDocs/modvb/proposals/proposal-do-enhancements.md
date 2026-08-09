# `Do` 循环头声明/赋值 / Do Loop Header Declarations

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议允许在 `Do ... While/Until` 的循环头中声明或赋值变量，使该赋值在**条件求值之前**执行，从而让"读取下一项→判断是否继续"的惯用法写进一条循环语句。

## Motivation
[motivation]: #motivation

典型的逐行读取/逐块读取循环需要三处分开的代码：初始化读取、循环体、下一轮读取。例如读取 `Console.ReadLine()` 直到空、或反复 `stream.ReadAsync` 直到返回 `-1`。现有 `Do`/`While` 无法在条件里做"先取再判"且保留变量在循环体内的可用性，往往退化为 `While True`/`Exit` 结构。

## Detailed design
[design]: #detailed-design

循环头中允许"赋值"形式的子句，其作用域覆盖整个循环（含条件与循环体），并且在每次条件求值**之前**执行。

```vb
' Variable declaration/assignment in header BEFORE condition is evaluated.
Do line = Console.ReadLine() Until line Is Null
    ...

Do bytesRead = stream.ReadAsync(buffer, 0, buffer.Length) While bytesRead > -1
    ...
```

- `Do line = Console.ReadLine() Until line Is Null`：每轮先执行 `line = Console.ReadLine()`，再判断 `line Is Null`，满足则结束循环；`line` 在循环体内可用。
- `Do bytesRead = stream.ReadAsync(buffer, 0, buffer.Length) While bytesRead > -1`：每轮先异步读取并赋给 `bytesRead`，`bytesRead > -1` 为真则继续。

语义上等价于"把赋值放到循环体末尾并在 `Until`/`While` 处回跳"，但变量声明/赋值、条件、循环体在一个头部表达，更紧凑。

## Drawbacks
[drawbacks]: #drawbacks

- 循环头带赋值子句降低了可读性，条件里混入副作用，调试（单步、watch）更复杂。
- 若赋值子句是声明（首次使用）与赋值混用，需要定义变量作用域恰好为整个循环的规则。
- 与 3.9 `Try` 头、`Using` 头等"块头局部声明"的语法要统一风格。

## Alternatives
[alternatives]: #alternatives

- 保持现状：`While True` + 循环体末尾读取 + `Exit Do`，代价是样板代码。
- 支持完整的 `Do Declare x = ... While/Until ...` 声明式变体，而不仅是赋值。
- 用 `For`/`For Each` 适配读取循环，但对条件式读取不自然。

## Unresolved questions
[unresolved]: #unresolved-questions

- 头部子句是否应支持"声明 + 赋值"（如 `Do Dim line = Console.ReadLine() ...`），而不仅是赋值已有变量。
- 多子句（如同时声明多个变量）是否允许。
- `bytesRead = stream.ReadAsync(...)` 这类 `Task` 结果在 `While` 条件求值时是否隐式 `Await`（与 3.9 `Await` 增强的交互）。
