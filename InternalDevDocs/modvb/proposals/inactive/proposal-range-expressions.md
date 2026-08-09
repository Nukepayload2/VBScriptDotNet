# 范围表达式 / Range Expressions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入范围表达式字面量 `1 To 10 Step 2`。范围表达式可以作为一等表达式赋值给变量、直接用于 `For Each` 迭代，也可以作为参数传给方法（如 `Rnd(1 To 100)`）。

## Motivation
[motivation]: #motivation

- 当前要产生一个等差整数序列只能写 `Enumerable.Range`、循环或数组字面量，样板多且不直观；
- `For Each n In 0 To 60` 这种写法在 VB 中非常自然，范围表达式可以让它直接从语言层面成立，无需依赖返回 `IEnumerable` 的辅助函数；
- 很多 API 需要传入"区间/范围"（如 `Rnd(1 To 100)` 表示 1..100 的随机数），范围表达式作为参数可提升可读性。

## Detailed design
[design]: #detailed-design

### 基本语法

```vb
' Range expressions.
Let odds = 1 To 10 Step 2
```

`1 To 10 Step 2` 表示从 1 到 10、步长为 2 的序列。`To` 与 `Step` 均为现有 VB 关键字，`Let` 是 Anthony 提出的新声明关键字（此处与 `Dim` 等价）。`Step` 可省略，省略时步长为 1。

### 用于 `For Each`

```vb
For Each n In 0 To 60
    ...
```

范围表达式可直接作为 `For Each` 的迭代源，`n` 依次取 0..60 的每个整数，省去 `Enumerable.Range(0, 61)` 之类写法。

### 作为参数

```vb
Let guess = Rnd(1 To 100)
```

`1 To 100` 作为实参传给 `Rnd`，表示在 1 到 100 之间取值。范围表达式在此作为"区间"语义传递，而非集合。

## Drawbacks
[drawbacks]: #drawbacks

- `To` / `Step` 在 VB 中已有多种含义（`For` 循环头、数组下标、`Select Case` 区间），再作为范围表达式字面量会加重歧义解析负担。
- 范围表达式的类型需要明确：默认应是惰性的整数序列，还是立即物化（数组）？这影响内存与性能。
- 与既有 `arr(1 To 10)` 数组切片语法、`x To y` 区间比较在视觉上容易混淆。

## Alternatives
[alternatives]: #alternatives

- 继续使用 `Enumerable.Range(start, count)` 或 `For` 循环，代价是样板与可读性；
- 只把范围表达式限制为 `For Each` 循环内的特殊语法，不提供一般表达式形式；
- 复用数组字面量 `{1, 2, 3, ...}` 展开，但无法表达大范围或步长。

## Unresolved questions
[unresolved]: #unresolved-questions

- 范围表达式是否只支持整数类型，还是也允许 `Decimal` / `Double` / `Date` 等类型。
- 范围是惰性序列（`IEnumerable`）还是预先物化，`Step` 支持负数吗。
- 空范围（`start > end` 且正步长）的行为：返回空序列还是编译期/运行期错误。
- 与 `For n = 1 To 10` 头部语法的关系：范围表达式是否可直接复用到 `For` 头部。
- 作为参数（如 `Rnd(1 To 100)`）时范围表达式的具体语义是否与集合上下文一致。
