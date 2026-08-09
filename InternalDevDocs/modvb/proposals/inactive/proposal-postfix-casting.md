# 后置转换语法 / Post-fix Casting Syntax

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入后置转换语法 `expr(As Type)`：把类型转换写在表达式之后，使链式转换与成员访问更易读、易写，无需为转换引入中间变量或前置括号。

## Motivation
[motivation]: #motivation

现有 VB 的转换是前置形式（`CType(x, Type)`、`DirectCast(x, Type)`、`CType(x, Type).Member`）。当转换之后还要继续链式访问成员时，前置转换会让表达式被括号层层包裹、阅读顺序与书写顺序相反。后置转换 `x(As Type)` 把类型转换当作"后缀操作"，让表达式从左到右读起来自然。

## Detailed design
[design]: #detailed-design

```vb
' Post-fix casting syntax makes it easier to read
' and write chained conversions and member accesses.
Let rowId = control.Tag(As DataRow).RowId

? jsonObject!receivedDate(As Date)
```

- `Let rowId = control.Tag(As DataRow).RowId`：把 `control.Tag`（通常是 `Object`）后置转换为 `DataRow`，随后立即访问 `.RowId`，链式一气呵成。
- `? jsonObject!receivedDate(As Date)`：从 `jsonObject` 的 `receivedDate` 字段（字典访问 `!`）后置转换为 `Date`。原文中 `?` 是 Immediate Window 的打印/求值提示符，此处保留原样以展示在即时窗口中的用法。

后置转换可作为任意表达式的一等操作，与成员访问 `.`、索引 `!`/`()`、调用等无缝衔接。

## Drawbacks
[drawbacks]: #drawbacks

- `expr(As Type)` 与现有函数调用括号形式在语法上接近，解析器需区分"调用/索引"与"后置转换"（`As` 关键字起了消歧作用）。
- 多种转换既有形式（`CType`、`DirectCast`、`TryCast`）并存，新增后置形式可能造成写法分裂。
- 可读性虽提升，但把类型转换藏进长链中，错误信息定位可能变难。

## Alternatives
[alternatives]: #alternatives

- 继续用 `CType(control.Tag, DataRow).RowId` 前置形式，代价是嵌套括号。
- 引入中间变量（`Let tag As DataRow = CType(control.Tag, DataRow)`），更显式但更冗长。
- 管道运算符 `->`（见 8 节建议）配合转换函数替代后置语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- `(As Type)` 是否还有 `DirectCast`/`TryCast`/空安全变体（如 `x(As? Type)`）。
- 后置转换与泛型推断、可空类型的交互。
- 与现有 `CType`/`DirectCast`/`TryCast` 的转换语义（是否等价于 `DirectCast`）。
