# `Set` 赋值语句 / Set Statement

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入显式 `Set` 赋值语句：允许对属性/成员使用复合赋值（如 `+=`），支持逗号分隔的多个赋值目标，并支持一次 `Set` 完成元组解构赋值。

## Motivation
[motivation]: #motivation

VB 对"变量赋值"与"属性赋值"有明确的 `Let`/`Set` 历史区分，但现代 VB 中直接对属性写复合赋值（`obj.Position += acceleration`）会被编译器拒绝——因为复合赋值需要读改写且隐式触发属性 Get/Set，语义上存在歧义。此外一次为多个变量赋值（如全部置 `Null`）目前必须拆成多条语句。

本建议用显式的 `Set` 关键字消除歧义，并一次性支持多重赋值与元组解构赋值。

## Detailed design
[design]: #detailed-design

### 显式 `Set` 复合赋值

```vb
' Explicit `Set` statement.
Set obj.Position += acceleration
```

`Set` 显式声明这是一个赋值语句。`obj.Position += acceleration` 等价于"读取 `obj.Position` 当前值，加上 `acceleration`，写回 `obj.Position`"。由于关键字 `Set` 明示了赋值意图，编译器不再需要对"是复合赋值还是表达式"做猜测。

### 多重赋值

```vb
Set left = Null,
    right = Null
```

一条 `Set` 语句内以逗号分隔多个赋值目标，各目标按顺序赋值。此处把 `left`、`right` 同时置为 `Null`。

### 元组解构赋值

```vb
Set suit, rank = GetNextCard()
```

与声明式解构 `Let suit, rank = GetCard()` 对应，`Set` 用于对**已存在**的变量做解构赋值：把 `GetNextCard()` 返回元组的两元素分别赋给 `suit`、`rank`。

## Drawbacks
[drawbacks]: #drawbacks

- `Set` 关键字在 VB 中已用于属性定义（Property Setter）与历史赋值语句，语句级复用可能造成混淆。
- 复合赋值到属性会对 `obj` 及 Get 结果多次求值，若 Get/Set 有副作用，读改写语义需谨慎定义。
- `Set` 与 `Let` 两个声明/赋值关键字的职责划分（`Let` 声明、`Set` 赋值）需要用户适应。

## Alternatives
[alternatives]: #alternatives

- 不加 `Set`，直接允许 `obj.Position += acceleration` 并让编译器自行处理；风险是向后兼容与歧义。
- 复合赋值仅在局部变量上允许，对属性保持禁止；代价是丢失对属性做增量更新的便利。
- 多重赋值/解构赋值复用于其它现有语法（如 `left = right = Null`），但可读性差。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Set` 语句与现有 `Set` 属性定义、`Let` 声明关键字的名称复用与解析规则。
- `Set obj.Position += acceleration` 对索引属性、多次求值 `obj` 时的精确语义。
- `Set suit, rank = GetNextCard()` 与 `Let suit, rank = GetCard()` 解构赋值/声明的语法关系。
