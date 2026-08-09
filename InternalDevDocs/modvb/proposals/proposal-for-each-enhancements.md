# `For Each` 增强 / For Each Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `For Each`：支持元组解构迭代（`For Each key, value In dictionary`）、`Exit For`/`Continue For` 指定显式变量、内联查询理解（`Where` 过滤），并支持 `Await Each` 以消费 `IAsyncEnumerable`。

## Motivation
[motivation]: #motivation

- 迭代字典时想同时拿到键和值，现在只能 `For Each kvp In dictionary` 再 `kvp.Key`/`kvp.Value`；
- 嵌套 `For Each` 中无法直接退出/继续指定层；
- 简单过滤必须写成完整 `For Each ... If ... Then` 或引入 LINQ 查询，样板多；
- 异步流（`IAsyncEnumerable`）尚无循环语法支持。

## Detailed design
[design]: #detailed-design

### 元组解构

```vb
' Tuple deconstruction.
For Each key, value In dictionary
    ...
```

`For Each key, value In dictionary` 每轮迭代把 `KeyValuePair` 解构为 `key`、`value` 两个迭代变量。

### `Exit For` / `Continue For` 显式变量

```vb
' Explicit variable in `Exit For` and `Continue For` statements.
Continue For child

Exit For parent
```

与 3.4 `For` 的增强一致：`Continue For child` 继续名为 `child` 的 `For Each` 循环，`Exit For parent` 退出名为 `parent` 的循环。

### 查询理解（内联过滤）

```vb
' Query comprehensions.
For Each ch In str Where ch -> Char.IsDigit()
    ...
```

`For Each ch In str Where ch -> Char.IsDigit()` 在迭代头直接写 `Where` 过滤（`->` 为管道/筛选表达式），只对 `str` 中数字字符 `ch` 执行循环体。Anthony 曾给出早期原型演示（见文末链接）。

### `Await Each` 与 `IAsyncEnumerable`

```vb
' IAsyncEnumerable support.
Await Each item In sequence
    ...
Next
```

`Await Each item In sequence` 遍历异步序列 `sequence`（`IAsyncEnumerable(Of T)`），每轮 `await` 取下一个元素。这是异步迭代器的消费端语法。

> **Past Demo** (Early Prototype)：Anthony 对查询运算符内置 `For Each` 有早期原型演示：[Query Operators in `For Each`](https://www.youtube.com/watch?v=ynBHLuicCYs)。

## Drawbacks
[drawbacks]: #drawbacks

- 查询理解内联在循环头，与 LINQ 查询语法功能重叠，可能让"何时用哪个"变得含糊。
- `Await Each` 引入异步迭代专用语法，与 `For Each ... In` 的差异需清晰文档化（不能在 `Next` 前省略 `Await`）。
- 元组解构迭代对自定义迭代器的形状要求需明确（依赖 `Deconstruct`）。

## Alternatives
[alternatives]: #alternatives

- 过滤仍用 `If ... Then Continue For` 或 LINQ `Where`；代价是更长的循环体。
- 异步迭代继续依赖 `await foreach` 式的现有/其它语法；`Await Each` 是 VB 惯用的措辞。
- 解构可用 `For Each (key, value) In dictionary` 括号形式，与 `Dim` 解构统一。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Where` 查询理解是否仅支持 `Where`，还是扩展到 `Order By`/`Select` 等更多查询运算符。
- `Await Each` 与现有 `For Each` 在代码格式（是否可省略 `Next` 行）与错误处理上的细节。
- `For Each key, value In dictionary` 对非 `KeyValuePair`、自定义解构类型的适用规则。
