# `Throw` 异常类型推断 / Throw Exception Type Inference

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议让裸 `Throw`（不带异常对象）能够根据条件上下文推断要抛出的异常类型，从而把常见的参数校验/状态校验从显式 `Throw New ArgumentNullException(...)` 简化为 `If ... Then Throw`。

## Motivation
[motivation]: #motivation

参数校验与前置状态校验是极高频的样板代码，每次都写 `Throw New ArgumentNullException(NameOf(name))` 冗长且占行。若能根据条件形态推断异常类型，可以让最常见的校验一行完成，且错误信息可由编译器生成。

## Detailed design
[design]: #detailed-design

条件 `If ... Then Throw` 中，编译器根据条件的形态推断异常类型并抛出适当异常（消息可自动生成或取自被检查的表达式）：

```vb
' Exception type inference.

' Throws ArgumentNullException.
If name Is Null Then Throw

' Throws ArgumentOutOfRangeException.
If count < 0 Then Throw
    
' Throws InvalidOperationException.
If IsClosed Then Throw
```

- `If name Is Null Then Throw`：检查引用是否为 `Null`，推断抛出 `ArgumentNullException`（针对 `name`）。
- `If count < 0 Then Throw`：检查数值下界，推断抛出 `ArgumentOutOfRangeException`（针对 `count`）。
- `If IsClosed Then Throw`：检查布尔状态，推断抛出 `InvalidOperationException`。

## Drawbacks
[drawbacks]: #drawbacks

- 从条件形态推断异常类型是隐式魔法，读者需了解映射规则（`Is Null`→`ArgumentNullException`、`< 0`→`ArgumentOutOfRangeException`、布尔→`InvalidOperationException`）。
- 不同程序员对同一条件的预期异常可能不同（例如 `IsClosed` 也可能是 `ObjectDisposedException`），推断结果未必符合意图。
- 推断错误的消息内容无法表达丰富的说明文字。

## Alternatives
[alternatives]: #alternatives

- 继续显式 `Throw New ...Exception(...)`，语义最清楚但冗长。
- 提供具名快捷方式（如 `Throw ArgumentNullException`），不依赖条件推断，但失去上下文自动填充。
- 让 `Throw` 支持可选异常类型/消息参数（`Throw ArgumentNullException(name)`），与推断并用。

## Unresolved questions
[unresolved]: #unresolved-questions

- 条件到异常类型的完整映射表（如 `Is Nothing`、`= Nothing`、`<= 0`、`Length = 0`、字符串 `IsNullOrEmpty` 等形态）。
- 生成的消息内容、是否包含 `NameOf` 对应的参数名。
- 与"守卫语句"其它建议（如 `Guarded Let`、`Case Else`）如何配合。
