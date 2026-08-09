# 二值逻辑相等运算符 / Two-Valued Logic Equality Operators

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入 `?=` 与 `?<>` 两个二值逻辑相等运算符，用于比较可能为 null 的操作数：两个 null 视为相等。语义等价于 T-SQL 的 `IS [NOT] DISTINCT FROM`，不受 VB `=`/`<>` 三值逻辑的影响。

## Motivation
[motivation]: #motivation

VB 的 `=`/`<>` 对可空值类型（NVT）采用三值逻辑：任一操作数为 null 时比较结果为 `Nothing`（即"未知"），在 `If` 等布尔上下文中不符合直觉。对可空数据（尤其数据库取值）常需要二值语义：两个 null 相等、一个 null 与一个非 null 不等、两个非 null 按值比较。原文明确指出这与 T-SQL 的 `IS [NOT] DISTINCT FROM` 一致。

## Detailed design
[design]: #detailed-design

```vb
' 2-Valued Logic equality operators which treat two
' nulls as equivalent. (i.e. T-SQL `IS [NOT] DISTINCT FROM`).
' NOTE: VB `=`/`<>` uses 3-valued logic for NVTs today.

' True if both null, or both not null and equal.
' Otherwise, false.
If left ?= right Then
    ...

' True if one or the other (but not both) are null,
' or if neither is null and left <> right.
' False if both are null, or both not null and equal.
If left ?<> right Then
    ...
```

- `left ?= right`：两者皆 null，或两者皆非 null 且相等时结果为 `True`；否则为 `False`。
- `left ?<> right`：恰有一者为 null，或两者皆非 null 且 `left <> right` 时为 `True`；两者皆 null 或两者相等时为 `False`。
- 与 VB 现有 `=`/`<>` 的三值逻辑（NVT 参与时为 `Nothing`）明确区分：两个新运算符都是严格的二值逻辑，结果为布尔值。

## Drawbacks
[drawbacks]: #drawbacks

- 新增 `?=`/`?<>` 与现有 `=`/`<>` 并存，带来写法分裂与认知负担。
- 语义与 T-SQL 对齐，但语言内部需要规定 null 传播（null propagation）在这些运算符上不适用。
- 运算符在泛型、匿名类型、接口上的行为需额外规定。

## Alternatives
[alternatives]: #alternatives

- 扩展现有 `=`/`<>` 的语义（破坏性变更，与三值逻辑的历史行为冲突）。
- 用 `Is`/`IsNot` 组合手工实现（如 `left Is Null AndAlso right Is Null`），不新增运算符。
- 依赖可空比较辅助方法（如 null-aware 的 `Equals` 重载）。

## Unresolved questions
[unresolved]: #unresolved-questions

- `?=`/`?<>` 与既有 `=`/`<>`、`Is`/`IsNot` 的优先级与结合性未在原文给出。
- 运算符对可空值类型的比较结果（直接按 `HasValue`/`Value` 二值比较）细节未完全确定。
- 原文未说明这两个运算符是仅用于可空类型，还是任意类型均可使用。
