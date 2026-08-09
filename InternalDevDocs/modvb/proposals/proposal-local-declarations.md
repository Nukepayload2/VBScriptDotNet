# 局部变量声明增强 / Local Variable Declarations

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强局部变量声明：引入 `Let` 声明关键字（Anthony 提出的新声明关键字，代替 `Dim`）并支持元组解构；同时修复 `As New` 对数组与匿名类型的支持，使 `As New` 在一条语句中即可完成带边界/初始化器的数组声明与匿名类型创建。

## Motivation
[motivation]: #motivation

当前 VB 的局部变量声明存在几处常见痛点：

- 一次从方法返回值中取多个变量时，只能逐一声明再赋值，不能直接解构；
- `As New` 语法无法用于数组（带边界与初始化器）或匿名类型，程序员必须把"声明"与"创建"拆成多步；
- 匿名类型只能通过 `New With {}` 表达式创建，缺少声明式写法。

本建议希望让局部声明更短、更直观，减少样板代码。

## Detailed design
[design]: #detailed-design

### 元组解构

`Let` 允许一次声明并解构多个变量：

```vb
' Tuple deconstruction.
Let suit, rank = GetCard()
```

`GetCard()` 返回二元组 `(suit, rank)`。编译器为 `suit`、`rank` 分别声明局部变量并赋值元组对应元素（与现有 `Dim (suit, rank) = GetCard()` 解构语法语义一致，但写法更简洁）。

### 修复 `As New` 数组

`As New` 现在可用于数组类型，可在同一语句中给出边界与初始化器：

```vb
' Fix: `As New` for arrays and anonymous types.
Dim buffer As New Byte(0 To 1023) {}
```

`buffer` 被声明为 `Byte` 数组，下标范围 `0 To 1023`，并用空集合初始化器 `{}` 完成初始化。此前的限制是 `As New` 只能用于带 `New` 构造函数的对象类型，数组必须在类型名之外单独 `New Byte(0 To 1023) {}`。

### `As New With` 匿名类型

`Let` 结合 `As New With` 声明匿名类型变量：

```vb
Let foreignKey As New With {emailAddress, userId}
```

以 `emailAddress`、`userId` 为属性名与初始值创建匿名类型实例，并声明局部变量 `foreignKey`。

## Drawbacks
[drawbacks]: #drawbacks

- 引入新声明关键字 `Let` 与现有 `Dim`、`Const` 并存，增加语言学习面；VB 历史上 `Let` 曾用于赋值语句（`Let x = 1`），需要妥善处理关键字兼容。
- `As New` 对数组与对对象类型的初始化语义不同，需在规范中明确区分"数组边界/元素初始化"与"构造函数调用"。
- 元组解构中变量名与类型推断规则需要与现有 `Dim ... = (a, b)` 解构保持一致，避免两套语义分叉。

## Alternatives
[alternatives]: #alternatives

- 不引入 `Let`，仅在 `Dim` 上扩展元组解构与 `As New` 数组/匿名类型；`Let` 更多是书写体验上的简化。
- 数组创建继续沿用独立表达式 `New Byte(0 To 1023) {}`，不扩展到 `As New` 形式。
- 匿名类型仍只通过 `New With {}` 表达式创建，不新增声明式写法。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Let` 与 `Dim` 的差异范围（仅换关键字，还是包含额外的类型推断/变量绑定规则）。
- `As New` 数组同时给出显式边界与集合初始化器时的求值顺序与长度校验语义。
- `Let foreignKey As New With {emailAddress, userId}` 中裸标识符 `emailAddress`、`userId` 如何被推断为匿名类型属性（按名字取值，还是按类型推断）。
