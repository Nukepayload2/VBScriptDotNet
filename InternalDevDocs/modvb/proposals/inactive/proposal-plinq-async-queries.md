# PLINQ, Async Queries, More Query Operators / PLINQ、异步查询与更多查询运算符

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。调查 Parallel LINQ、使用 `Await` 的查询、针对 `IAsyncEnumerable` 的查询；并确保随着查询语言与数据存储的演进，查询理解语法（query comprehension syntax）能平滑表达常见用例（如公共表表达式、地理空间数据、No-SQL、时序数据库），无需退回扩展方法与 Lambda。原文仅列出调查方向，未给出语法。

## Motivation
[motivation]: #motivation

原文（18.14）：

> Parallel LINQ, queries that use `Await`, queries against `IAsyncEnumerable` are areas to investigate. As query languages and data stores evolve we also have to ensure that the query comprehension syntax can smoothly express common use-cases (e.g. Common-Table Expressions, Geospatial data, No-SQL, Temporal Databases) without needing to fallback to extension methods and lambda expressions.

即：Parallel LINQ、使用 `Await` 的查询、针对 `IAsyncEnumerable` 的查询都是值得调查的领域。随着查询语言与数据存储演进，还必须确保查询理解语法能平滑表达常见用例（例如公共表表达式 CTE、地理空间数据、No-SQL、时序数据库），而不需要退回扩展方法与 Lambda 表达式。

## Detailed design
[design]: #detailed-design

原文未给出任何查询语法示例，仅点名了需要覆盖的方向。摘录自原文的 VB 相关文本仅有关键词与类型：

```vb
' 需要调查的查询形态（原文未给完整示例）
' 1) Parallel LINQ 查询
' 2) 使用 Await 的查询
' 3) 针对 IAsyncEnumerable 的查询
From x In items.AsParallel() ...
From y In Await GetItemsAsync() ...
For Each z Await In stream   ' 概念性：IAsyncEnumerable 消费
```

（上例仅为概念示意，原文未给出完整查询代码。）

设计意图：让查询理解语法能够平滑表达以下场景，而无需退回扩展方法与 Lambda：

- **Parallel LINQ**：并行查询。
- **`Await` 查询**：查询中异步获取数据源/元素。
- **`IAsyncEnumerable` 查询**：对异步序列进行查询。
- **CTE（公共表表达式）**、**地理空间数据**、**No-SQL**、**时序数据库**等新兴存储形态的常用表达。

## Drawbacks
[drawbacks]: #drawbacks

- 查询理解语法覆盖的面越广，语言与编译器（翻译到对应扩展方法）的实现越复杂。
- 为 No-SQL/时序/地理空间等垂直领域扩展语法，可能让通用语法背上少数场景的包袱。

## Alternatives
[alternatives]: #alternatives

- 维持现状：并行/异步/特殊存储场景用扩展方法与 Lambda 表达，查询理解语法只覆盖经典 LINQ。
- 通过增强现有查询运算符（见查询增强建议）间接覆盖，而非新增并行/异步专用语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- 各方向的具体语法未定，原文仅列为 "areas to investigate"。
- `Await` 在查询中的位置与语义（`From` 子句？`Where`/`Select` 中？）未定。
- Parallel LINQ 与查询理解语法的映射方式未定。
- CTE/地理空间/No-SQL/时序数据库各自的表达是否值得进入通用语法，未定。
