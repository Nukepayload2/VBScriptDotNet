# Await Each 与 Async Iterator / Await Each and Async Iterators

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

为 `IAsyncEnumerable` 提供对称的消费与构建语法：用 `Await Each ... In ...` 消费异步序列，用 `Async Iterator Function ... As IAsyncEnumerable` 声明异步迭代器（在 `Await` 与 `Yield` 混合体中逐块产出元素）。

## Motivation
[motivation]: #motivation

随着 `IAsyncEnumerable` 成为流式异步数据的标准形态，VB 需要一等公民语法：消费方要能在异步序列上逐块迭代，生产者要能以 `Async Iterator` 简洁地构造异步序列，避免手写状态机或 `IAsyncEnumerable` 回调样板。

## Detailed design
[design]: #detailed-design

### 消费 `IAsyncEnumerable`

```vb
' Support for consuming IAsyncEnumerable.
Await Each chunk In steam.FetchChunksAsync(cancellationToken)
    ...
```

`Await Each ... In ...` 在异步序列 `steam.FetchChunksAsync(cancellationToken)` 上迭代，循环体内每个 `chunk` 都是异步拉取得到的数据块。（原文把变量名写作 `steam`，疑为 `stream` 的笔误。）

### 构建 `IAsyncEnumerable`

```vb
' Support for constructing IAsyncEnumerable.
Async Iterator Function FetchChunksAsync(...) As IAsyncEnumerable
    Await ...

    Yield ...
    Yield ...
End Function
```

`Async Iterator Function ... As IAsyncEnumerable` 声明一个异步迭代器：函数体中可以 `Await` 异步操作，并用 `Yield` 逐个产出元素，由编译器生成相应的异步迭代器状态机。

## Drawbacks
[drawbacks]: #drawbacks

- `Await Each` 与现有 `For Each`（含 awaitable 迭代）并存的语法区分较微妙。
- 异步迭代器同时引入 `Async` 与 `Iterator` 两套状态机规则，复杂度叠加。
- 取消（cancellation）、提前退出（`Exit For`）时底层迭代器的 Dispose 行为需要额外规定。

## Alternatives
[alternatives]: #alternatives

- 复用现有 `For Each` 配合 `Await`（如 `For Each chunk In Await steam.FetchChunksAsync(...)`）一次性物化序列。
- 依赖 `System.Linq.Async` 的 LINQ 方法，不新增语法。
- 仅消费（`Await Each`）或仅构建（`Async Iterator`）其中之一先行落地。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文 `steam.FetchChunksAsync(cancellationToken)` 疑为 `stream` 的笔误，未更正。
- `Async Iterator` 的返回类型原文写 `As IAsyncEnumerable`（非泛型），实际应为 `IAsyncEnumerable(Of T)`，待定。
- `Await Each` 的完整语句形态（是否带 `Next` 步进、`Exit For`、索引变量）未在原文详述。
