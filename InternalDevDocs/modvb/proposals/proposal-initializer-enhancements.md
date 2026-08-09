# 初始化器增强 / Initializer Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

增强对象/集合初始化器：集合元素无匹配 `Add` 时调用 `New`；嵌套成员与嵌套集合初始化器（`.AcceptButton With {...}`、`.Choices From {...}`）；`With` 与 `From` 组合；`With` 内字典访问 `!Key`；以及查询式集合初始化器 `New Hashset(Of Integer) From n In 0 To 9`。

## Motivation
[motivation]: #motivation

- 构造复杂对象（如 `List(Of DateOnly)`、`MessageBoxFrom`、`Dictionary`）目前需要多条语句逐个 `Add` 或赋值，样板多；
- 集合初始化器里写 `{2026, 1, 1}` 无法直接表达"构造一个 `DateOnly`"，若没有匹配的 `Add` 重载就报错；
- 字典/带索引器对象无法用现成初始化器简洁填充；查询式集合初始化器希望直接用范围/查询生成集合内容。

## Detailed design
[design]: #detailed-design

### 无匹配 `Add` 时嵌套元素调用 `New`

```vb
' Nested initializers call `New` when no matching `Add`.
Let usFederalHolidays =
      New List(Of DateOnly) From {
            {2026, 1, 1},
            {2026, 1, 19},
            {2026, 2, 16},
            {2026, 5, 25},
            {2026, 6, 19},
            {2026, 7, 3},
            {2026, 9, 7},
            {2026, 10, 12},
            {2026, 11, 11},
            {2026, 11, 26},
            {2026, 12, 24},
            {2026, 12, 25}
          }
```

`List(Of DateOnly)` 没有 `Add(Integer, Integer, Integer)` 重载，此时 `{2026, 1, 1}` 会尝试调用 `New DateOnly(2026, 1, 1)` 构造元素后再 `Add`，从而一行声明一整份节假日列表。

### 嵌套成员与集合初始化器

```vb
' Nested member and collection initializers.
? New MessageBoxFrom With {
        .Caption = "Select one",
        .AcceptButton With {
           .Text = "Proceed"
         },
        .Choices From {
           "Option 1",
           "Option 2",
           "Option 3"
         }
      }
```

`.AcceptButton With { .Text = "Proceed" }` 对成员 `AcceptButton` 再做一次对象初始化（嵌套 `With`）；`.Choices From { ... }` 对 `Choices` 集合成员用 `From` 填入三个选项。不同成员可按其类型分别选择 `With` 或 `From`。

### 组合 `With` 与 `From`

```vb
' Combine `With` and `From`.
? New List(Of Object) With { .Capacity = 8 } From { ... }
```

同一个初始化器里先 `With { .Capacity = 8 }` 设置集合自身属性，再 `From { ... }` 填入元素，两者可串联组合。

### `With` 内字典访问

```vb
' Dictionary access in `With`
? New Dictionary(Of String, Integer) With {
        !One   = 1,
        !Two   = 2,
        !Three = 3
      }
```

`!Key = value` 在初始化器里等价于按键写入：`!One = 1` 即 `dict("One") = 1`（`With` 目标自身的索引器/字典访问），用感叹号字典访问语法填充 `Dictionary(Of String, Integer)`。

### 查询式集合初始化器

```vb
' Query collection initializers.
Let digits = New Hashset(Of Integer) From n In 0 To 9
```

`New Hashset(Of Integer) From n In 0 To 9` 以查询式写法把范围 `0 To 9` 的每个值 `n` 填入 `HashSet`，等价于把一次查询结果直接作为集合初始化内容。

## Drawbacks
[drawbacks]: #drawbacks

- "无匹配 `Add` 就调用 `New`"的启发式会引入隐式构造，报错信息可能变含糊（到底是没匹配 `Add` 还是 `New` 失败）。
- `With`/`From`/`!` 在初始化器内并存的语法面扩大，解析与文档负担增加。
- 嵌套初始化器深嵌套时，错误定位与可读性下降。

## Alternatives
[alternatives]: #alternatives

- 维持现状：逐条 `Add` / 逐字段赋值，或先用 `Enumerable.Range(...).Select(...).ToHashSet()` 等链式调用；
- 只支持 `With` 组合，不引入 `From` 与 `!` 字典访问；
- 无匹配 `Add` 时直接报错，要求用户显式写 `New DateOnly(...)`。

## Unresolved questions
[unresolved]: #unresolved-questions

- "调用 `New`"的匹配规则：按参数个数、类型还是可选参数重载选择，是否允许多个候选。
- `!Key = value` 是否仅限 `Dictionary(Of String, T)`，还是任何带索引器/`Item` 的类型；`!` 命名规则与 XML 字典访问是否冲突。
- `From n In 0 To 9` 查询式集合初始化器支持哪些查询子句（`Where`、`Select`、`Order By` 等）。
- `With` + `From` 组合时的求值顺序：先 `With` 还是先 `From`。
- 嵌套 `With` 的目标成员为 `Nothing` 时是否自动 `New`。
