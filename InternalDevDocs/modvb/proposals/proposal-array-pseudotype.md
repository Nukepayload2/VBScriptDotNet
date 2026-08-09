# 数组伪类型 / Array Pseudo-Type (`Array(Of T)`)

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入**数组伪类型** `Array(Of T)`：以泛型化、类型化的方式书写数组类型，取代或补充现有的 `T()` 下标语法，使数组类型可以出现在 `Array(Of Array(Of Integer))` 这类嵌套/复合位置，并支持 `New Array(Of Byte)(1024)` 这种**按大小**（而非按边界）实例化数组的写法。

## Motivation
[motivation]: #motivation

现有 VB 用 `T()`（或 `T(n)`）书写数组类型，语法在嵌套数组、泛型类型参数位置等处不够统一、不易组合；数组创建又必须给出上界（`New Byte(0 To 1023) {}`），而非元素个数。`Array(Of T)` 伪类型让数组类型能以一致的类型语法书写，`New Array(Of T)(size)` 让"按长度创建"更直观，并与 `{}` 集合初始化器自然配合。

期望的结果：`Let vector As Array(Of Byte) = {}` 声明字节数组；`Let jagged As Array(Of Array(Of Integer)) = {({})}` 声明交错数组；`Let buffer = New Array(Of Byte)(1024)` 按大小实例化。

## Detailed design
[design]: #detailed-design

### 数组类型声明

用 `Array(Of T)` 书写数组类型：

```vb
' Array pseudo-type?
Let vector As Array(Of Byte) = {}
Let jagged As Array(Of Array(Of Integer)) = {({})}
```

`vector` 是一维 `Byte` 数组，用空集合初始化器 `{}` 初始化；`jagged` 是"整数数组的数组"（交错数组），用 `{({})}` 初始化——内层 `({})` 为单个空整数数组元素。

### 按大小实例化

`New Array(Of T)(size)` 按元素个数创建数组（而非按边界）：

```vb
' Array instantiation by size (rather than bounds).
Let buffer = New Array(Of Byte)(1024)
```

`buffer` 为长度 1024 的 `Byte` 数组，每个元素为默认值（`0`）。Anthony 以问号标注"数组伪类型？"，表示该形态尚未完全定稿。

## Drawbacks
[drawbacks]: #drawbacks

- 伪类型与真实类型（`System.Array`）同名易混淆，需在语义上明确 `Array(Of T)` 是编译器伪类型而非 `System.Array` 泛型。
- 与现有 `T()` 下标数组语法并存会引入两套表达同一类型的写法，规范化成本高。
- `New Array(Of T)(size)` 与 `New T(n)`（按边界创建）语义差异（长度 vs 上界）需反复提醒。

## Alternatives
[alternatives]: #alternatives

- 保持 `T()` 语法不变，仅在泛型参数位置特殊支持数组（如 `Of T() As ...`），不新增伪类型。
- 数组创建继续用 `New Byte(0 To 1023) {}`，不引入按大小的 `New Array(Of T)(size)`。
- 让 `Array(Of T)` 只是 `T()` 的语法糖（完全等价），而不是独立伪类型。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Array(Of T)` 与 `T()` 的关系（等价语法糖，还是独立类型形态）。
- 是否支持多维数组（如 `Array(Of Byte, Byte)` 或 `Byte(,)` 的伪类型对应物）。
- `New Array(Of T)(size)` 的 `size` 是否允许表达式、是否支持多维大小列表。
- 交错数组 `{({})}` 中集合初始化器与 `Array(Of Array(Of Integer))` 的嵌套匹配规则。
