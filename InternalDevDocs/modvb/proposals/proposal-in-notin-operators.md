# `In` / `NotIn` 运算符 / In and NotIn Operators

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

新增二元运算符 `In` 与 `NotIn`：`input In bannedWords` 编译期映射为 `bannedWords.Contains(input)`，可作用于任意集合；`NotIn` 为其取反。二者可与范围表达式配合做区间判定，如 `actScore NotIn 1 To 36`。

## Motivation
[motivation]: #motivation

- "判断一个值是否在某个集合里"目前要写 `bannedWords.Contains(input)` 或 `Array.IndexOf(...) >= 0`，可读性差；
- `If input In bannedWords Then ...` 的写法接近自然语言，也符合 VB 中 `For Each x In coll`、`Select Case` 里 `Case x In range` 已有的 `In` 语义；
- 与范围表达式结合后，`NotIn 1 To 36` 能直接表达"不在某个区间内"，常见于输入校验。

## Detailed design
[design]: #detailed-design

### 映射 `Contains`

```vb
' In and NotIn operators map to `Contains`.
If input In bannedWords Then Throw
```

`input In bannedWords` 编译期改写为 `bannedWords.Contains(input)`；为真时抛出异常。`NotIn` 是其取反（`Not bannedWords.Contains(input)`）。

### 与范围表达式配合

```vb
' Works with range expressions.
If actScore NotIn 1 To 36 Then Throw
```

`actScore NotIn 1 To 36` 表示 `actScore` 不在 1 到 36 的区间内（如 ACT 考试分数合法性校验），由 `In`/`NotIn` 与范围表达式协作实现。

## Drawbacks
[drawbacks]: #drawbacks

- `In` 已在多处使用（`For Each ... In`、`Case In`、XML 字典 `!`/`In` 相关），再作二元运算符需要消除语法歧义。
- `Contains` 对 `HashSet`、`List`、数组等不同类型的存在性判断复杂度不同，语言层需明确仅语义映射、性能交给类型自身。
- `NotIn` 需要新增关键字（或与 `Not` 组合解析），会影响现有代码的 `Not` 用法。

## Alternatives
[alternatives]: #alternatives

- 继续写 `Contains`/`IndexOf`，或 `Array.Exists`，不新增运算符；
- 仅提供 `In`，`NotIn` 用 `Not (x In coll)` 表达；
- 只允许 `In`/`NotIn` 用于 `If`/`Select Case` 条件，不作为通用表达式。

## Unresolved questions
[unresolved]: #unresolved-questions

- 左侧操作数支持哪些类型（值类型、字符串、可空）以及集合元素类型不匹配时的转换规则。
- `In` 是否会参与短路求值或使用哈希查找优化，还是无条件映射为 `Contains`。
- `NotIn` 是否作为独立关键字引入，还是用 `Not` + `In` 组合解析。
- 与 `Select Case` 中既有 `Case x In range` 的关系与一致性。
- 右操作数为字符串时，`In` 是否映射为 `String.Contains`、`IndexOf` 还是正则。
