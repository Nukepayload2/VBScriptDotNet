# 可空性流分析（Nullability Flow Analysis）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

对可空类型（如 `Animal?`）进行可空性流分析：当变量经过 `If animal IsNot Null Then` 之类的空值守卫之后，编译器在该分支内把变量视为非空，允许直接访问其成员；在未收窄的位置访问可空变量的成员则给出明确的诊断错误。

## Motivation
[motivation]: #motivation

当前（vanilla VB）中，对可空变量直接调用成员通常缺少针对性的诊断，或与传引用类型、结构体（如 `Nullable(Of T)`）的语义混淆。本建议的目标是：

- 在 `If animal IsNot Null Then` 之后的代码中，`animal.Move()` 不再报错。
- 在未收窄的位置对可空类型调用成员时，给出清晰、可读的错误信息。

## Detailed design
[design]: #detailed-design

### 可空类型上调用成员的报错改进

对可空变量直接调用成员时，编译器应给出明确的诊断，而不是含糊或延迟到运行期的错误。错误文案待定：

```vb
Let animal As Animal? = SomeFunction()

' (Actual error text subject to change)
' Error: `Move` is not a member of `Animal?`.
' -OR-
' Error: Cannot call member `Animal.Move` from nullable value.
animal.Move()
```

### IsNot Null 守卫后的成员访问不再报错

一旦经过 `If animal IsNot Null Then` 守卫，分支内的 `animal` 被视为非空：

```vb
If animal IsNot Null Then
    ' No error.
    animal.Move()
End If
```

### 与守卫语句的配合

可空性收窄同样可配合守卫语句（`Return` / `Exit` / `Continue` / `Throw`）使用：守卫之后的位置，变量必然非空，成员访问合法。

## Drawbacks
[drawbacks]: #drawbacks

- VB 的历史语义中 `Nothing` / `Null` 与引用类型、`Nullable(Of T)` 的处理差异较大，流分析需要谨慎对齐，避免破坏既有代码的解析结果。
- 需要对"未收窄即可空访问"给出新错误，可能让一部分存量代码从"运行时失败"变为"编译期失败"，属破坏性变更（breaking change）。
- 编译器需要额外的可空状态跟踪，增加实现与维护成本。

## Alternatives
[alternatives]: #alternatives

- 沿用现有行为：可空变量成员访问交给运行时判定，缺乏编译期提示。
- 引入完整的可空引用类型（nullable reference types）系统，这是一项更大的语言变更（相关建议在 `inactive/proposal-nullable-reference-types.md`）。
- 依赖 `?.` 空条件运算符进行显式防护，而不做流分析（相关建议见 `proposal-null-safe-behaviors.md`）。

## Unresolved questions
[unresolved]: #unresolved-questions

- 错误的最终文案未定，原文标注："(Actual error text subject to change)"。
- `Null` 字面量与 `Nothing` 在流分析中的等价关系未明确（参见 `proposal-null-literal.md`）。
- 可空性流分析跨越方法边界（函数返回可空值）时的传播规则待定。
