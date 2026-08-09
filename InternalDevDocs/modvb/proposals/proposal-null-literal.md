# Null 字面量与可空推断 / Null Literal and Nullable Inference

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入统一的 `Null` 字面量，同时表示空引用与空值（可空值类型）；`Null` 永不表示值类型的 `CType(Nothing, T)`。在条件表达式等场景中使用 `Null` 会强制把推断类型提升为可空类型（如 `DateTime?`）。

## Motivation
[motivation]: #motivation

VB 现有的 `Nothing` 既可表示空引用又可表示值类型的默认值，语义含糊；把"引用为空"与"值类型默认值"混为一谈也常引发困扰。`Null` 提供一个与 `Nothing` 区别开的"真·空"字面量：引用与可空值类型的统一空值，并在类型推断时强制可空。

## Detailed design
[design]: #detailed-design

### `Null` 字面量

```vb
' `Null` literal for null-references and values.
' Never means CType(Nothing, T) for value types.
? Null
```

`Null` 是空引用与空值的统一字面量；它永不表示值类型的 `CType(Nothing, T)`。示例首行的 `?` 是原文即时窗口（Immediate Window）的求值提示符。

### 强制可空类型推断

```vb
' Forces nullable type inference.
' `endDate` is typed as `DateTime?` not `Date`.
Let endDate = If(HasFinished, EndDatePicker.Value, Null)
```

`If(HasFinished, EndDatePicker.Value, Null)` 因第三操作数为 `Null`，把 `endDate` 推断为 `DateTime?` 而非 `Date`，从而允许该变量持有"无日期"的状态。

## Drawbacks
[drawbacks]: #drawbacks

- 与 `Nothing` 并存引入两个"空"字面量，概念区分需要学习成本，历史代码迁移时易混淆。
- 强制可空推断会改变现有 `If()` 等表达式的推断结果，属于破坏性变更（如 `Date` → `DateTime?`）。
- `Null` 与可空引用类型（NRT）特性耦合，而 NRT 本身仍在迭代中（见 Unresolved questions）。

## Alternatives
[alternatives]: #alternatives

- 直接复用现有 `Nothing`，仅在推断算法上改进。
- 用显式类型注释（`Let endDate As DateTime? = ...`）表达可空，不引入 `Null`。
- 以 `Nullable(Of T)` / `?` 作为唯一的可空表达，不新增字面量。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确标注：section 12 的亮点 "Nullable Reference Types"（可空引用类型）设计需要进一步迭代以降低困惑与恼人程度，作者正在修订；`Null` 与 NRT 的关系因此未定。
- `Null` 与现有 `Nothing` 的并存关系（`Nothing` 是否保留、是否弃用、两者在可空值类型上是否等同）未确定。
- `Null` 在参数默认值、`Case` 匹配、`Select` 等场景的语义未在原文详述。
