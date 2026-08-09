# 新查询理解 / New Query Comprehensions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

在 `For Each` 循环内新增 `Include` 查询理解：遍历主集合时以声明式方式一次性加载关联导航数据（EF 式 `Include`）。原文标注 "Not Shown" 的 `Skip Until`、`Take Until`、`Skip Last`、`Take Last`、`Left Join`、`Right Join` 一并列出，但语法待定。

## Motivation
[motivation]: #motivation

- ORM（如 Entity Framework）里加载关联数据目前要用 `.Include(...)`/`.Include(...).ThenInclude(...)` 的链式调用，或单独触发延迟加载；
- 把关联声明放进 `For Each` 循环体内，让"遍历主数据 + 需要哪些关联"在同一处表达，意图更集中、可读性更好；
- 原文把 `Include` 归入一组 "New Query comprehensions"，暗示这一类新查询理解会统一规划。

## Detailed design
[design]: #detailed-design

### `Include`

```vb
' New Query comprehensions:
' Shown: Include
For Each blog In context.Blogs
 Include post In blog.Posts,
         post.Author.Photo,
         blog.Owner.Photo
    ...
Next
```

`Include post In blog.Posts, post.Author.Photo, blog.Owner.Photo` 声明：遍历 `blog` 时一并加载 `blog.Posts` 集合、每篇 `post` 的作者照片 `post.Author.Photo`，以及 `blog` 所有者的照片 `blog.Owner.Photo`。多个关联用逗号分隔，嵌套导航路径由点号连接，与 EF 的 `Include`/`ThenInclude` 语义对应。

### 未展示的查询理解

原文明确列出以下项，但标注 "Not Shown"，未给出任何代码：

- `Skip Until`
- `Take Until`
- `Skip Last`
- `Take Last`
- `Left Join`
- `Right Join`

这些项在本文档只登记意图，具体语法见 Unresolved questions。

## Drawbacks
[drawbacks]: #drawbacks

- 把查询理解写进 `For Each` 会模糊"循环"与"查询"的边界，`Include` 后的副作用、执行时机需要规定。
- `Include` 在循环体内每个迭代都出现时，是否导致重复加载/重复查询，需有明确的执行语义。
- 与现有查询表达式语法并存，会新增一套理解关键词。

## Alternatives
[alternatives]: #alternatives

- 维持 EF 链式 `Include`/`ThenInclude` 与延迟加载，不新增循环内理解；
- 用查询表达式子句（如 `Include` 作为 `From` 后子句）而非循环内语句；
- 只做 `Include`，把 Skip/Take 系列继续留给 `Skip`/`Take` 标准运算符。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文标注 "Not Shown"：`Skip Until`、`Take Until`、`Skip Last`、`Take Last`、`Left Join`、`Right Join` 的语法与语义全部待定，本文档不含其代码示例。
- `Include` 是否只允许出现在 `For Each` 循环内，还是也可用于查询表达式。
- 多个 `Include`（逗号分隔）的执行顺序与去重规则。
- 嵌套导航 `post.Author.Photo` 是否等价于 EF 的 `ThenInclude`，是否允许更深层路径。
