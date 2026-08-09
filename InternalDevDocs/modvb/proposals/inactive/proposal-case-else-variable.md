# `Case Else` 变量 / Case Else Variable

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。允许 `Case Else` 后紧跟一个变量名，把 `Select Case` 中未匹配到的表达式值绑定到该变量，并且该变量只在未匹配分支内可见，方便在"意外值"场景下使用（如 `Throw New UnexpectedValueException(other)`）。

## Motivation
[motivation]: #motivation

作者（18.3）的出发点是"Expect the unexpected?"（准备好迎接意外？）。对于枚举等类型，穷举完所有预期 `Case` 后，剩下的分支往往只需要知道"表达式值是什么"，以抛出包含该值的异常或记录日志。若 `Case Else` 能直接把未匹配的表达式值交给一个变量，就不必在外层先用中间变量保存表达式值，同时该变量作用域可以限制在 `Case Else` 分支内，不会污染其余分支。

## Detailed design
[design]: #detailed-design

原文示例：

```vb
' Expect the unexpected?
' (Great for enums!)
Select Case expression
    Case value1
        ...

    Case value2
        ...

    Case Else other
        ' Only needed a variable if the value is unexpected.
        ' This keeps the scope limited to this case.
        Throw New UnexpectedValueException(other)
        
End Select
```

- `Case Else other`：在 `Else` 关键字后引入变量 `other`，其值为未匹配上的 `expression` 的值。
- 变量仅在未匹配分支内需要，因此把作用域限制在本 `Case` 内（注释明确说明 "Only needed a variable if the value is unexpected. This keeps the scope limited to this case."）。
- 典型用途是对枚举做穷举式 `Select Case` 时，在兜底分支直接抛出携带意外值的异常。

## Drawbacks
[drawbacks]: #drawbacks

- `Case Else` 原本不接受任何表达式，新增变量位置会改变该关键字的语法形态，需要区分"无变量"与"带变量"两种写法。
- 该语法只服务于"取未匹配值"这一窄场景，收益有限；若引入，可能与既有的 `Case` 变量解构能力重叠。
- 作用域限定规则（只在 `Case Else` 内可见）需要额外的语义规定，增加编译器复杂度。

## Alternatives
[alternatives]: #alternatives

- 维持现状：在 `Select Case` 之外用中间变量保存表达式值，`Case Else` 分支再引用它。
- 由流分析推导：`Case Else` 内部对原表达式求值，不引入新变量语法。
- 依赖更通用的解构 / 模式匹配能力从 `Case` 变量中取得值，而非专门为 `Case Else` 加变量。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Case Else` 变量位置的语法与命名（`Case Else other` 中的 `other`）是否合适、能否与其余 `Case` 分支统一，未定。
- 变量类型如何推导（是表达式的静态类型，还是收窄后的类型）未在原文说明。
- 作用域"仅限本 Case"的具体边界（`Case Else` 内嵌套语句、`Case Else` 分支结束后）如何界定，未定。
