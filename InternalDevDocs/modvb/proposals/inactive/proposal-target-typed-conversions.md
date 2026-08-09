# 目标类型化转换 / Target-Typed Conversions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。为"喜欢把类型写在变量上"的人提供目标类型化转换：转换表达式（`DirectCast`、`TryCast` 等）省略类型实参时，由声明变量的目标类型来推断转换目标类型，例如 `Let i As Integer = DirectCast obj`。

## Motivation
[motivation]: #motivation

作者在原文 18.1 中提出，一部分开发者倾向于在变量声明处写类型（`Let x As T`），那么转换的目标类型已经有明确声明，转换表达式再重复写一遍类型就显得冗余且不一致。若让编译器根据声明的目标类型推导转换目标，可以写出更简洁、自说明的代码，并把类型信息集中到变量声明一处。

## Detailed design
[design]: #detailed-design

原文给出的核心示例：

```vb
' For those of us who prefer variable types on the variable.

Let obj As Object = 1S

Let i As Integer = DirectCast obj

Let v As T = Trycast expression
```

- `Let obj As Object = 1S`：字面量 `1S` 依目标类型 `Object` 参与目标类型化转换。
- `Let i As Integer = DirectCast obj`：`DirectCast` 不带类型实参，目标类型由变量声明 `As Integer` 提供。
- `Let v As T = Trycast expression`：`Trycast`（尝试转换）同样省略类型实参，由 `As T` 推断。

即在"类型在变量上"的声明风格下，转换关键字后面的类型实参可以被省略，编译器用变量的声明类型作为转换的目标类型。

## Drawbacks
[drawbacks]: #drawbacks

- 省略转换目标类型后，同一表达式在不同上下文（变量声明、实参、属性赋值等）中的语义可能不同，可读性与可搜索性下降。
- 若目标类型并不唯一或不可从上下文确定，会引入新的歧义诊断，增加学习成本。
- 与既有的 `CType`/`DirectCast`/`TryCast` 必须显式写类型参数的既有约定冲突，可能造成风格分裂。

## Alternatives
[alternatives]: #alternatives

- 继续要求 `DirectCast`/`TryCast` 显式写出类型实参，不改动转换语法。
- 仅对"变量上带类型声明"的形式启用省略，其他上下文（如实参）不启用，缩小影响面。
- 完全依赖类型推断的普通赋值（`Let obj = 1S`），不做目标类型化转换，类型靠推断而非声明。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文在示例末尾明确标注：`' I think this will cause fist fights.`（"我认为这会引发争论/打架"），针对的是 `M(CType(x))` 这种把 `CType` 放到实参位置、依赖参数类型作目标的用法，是否允许、语义如何确定尚未决定。
- 转换关键字省略类型实参时，允许出现在哪些上下文（仅变量声明，还是也包括实参、属性赋值等）未定。
- `Trycast` 与 `DirectCast` 的命名/大小写（原文写作 `Trycast`）是否为最终拼写，未定。
