# 查询增强 / Query Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

增强 LINQ 查询语法：`From` 子句支持元组解构（`From x, y In ...`）；`Select` 子句支持聚合函数写法（`Select FirstOrDefault()`），减少对 `Aggregate` 子句的依赖；`Select` 结果支持目标类型化（`Select AddressOf obj.M` 定型为 `Action`）；并修复 BC36606 错误在 `Select n.ToString()` 这类场景下的误报。

## Motivation
[motivation]: #motivation

- 查询 `EnumerateCoordinates()` 返回的坐标对时，`From x, y In ...` 直接解构出两个变量，免去额外的 `Select` 投影；
- 取第一个匹配项目前需要先 `Select` 再在外面包 `.FirstOrDefault()`，或动用笨重的 `Aggregate` 子句；`Select FirstOrDefault()` 更直观；
- `Select AddressOf obj.M` 希望在目标类型明确时（如 `IEnumerable(Of Action)`）自动定型为委托，省去 `CType` 或 `New Action(...)`；
- BC36606 在范围变量名与 `Object` 类成员名冲突（如 `n` 与 `n.ToString()`）时误报，需要停止。

## Detailed design
[design]: #detailed-design

### 元组解构

```vb
' Tuple deconstruction.
? From
    x, y In EnumerateCoordinates()
  Order By
    y Descending
```

`x, y In EnumerateCoordinates()` 把返回的坐标对（元组/二元组）解构成 `x`、`y` 两个范围变量，后续 `Order By y Descending` 可以直接引用解构出的 `y`。

### 聚合函数

```vb
' Smarter syntax for aggregate functions.
' Less need for Aggregate clause.
Let firstMatch = From   c In db.Contacts
                 Where  c.Id = contactId
                 Select FirstOrDefault()
```

`Select FirstOrDefault()` 作为聚合写法，等价于原查询再取第一个匹配项，`firstMatch` 是 `Contact`（或 `Contact?`），无需在查询外加 `.FirstOrDefault()`，也减少对 `Aggregate` 子句的需求。

### 目标类型化 `Select`

```vb
' Target-typing into `Select` clause.
Let actions As IEnumerable(Of Action) =
      From   obj In objects
      Select AddressOf obj.M ' <- Typed to `Action`.
```

`actions` 声明为 `IEnumerable(Of Action)`，`Select AddressOf obj.M` 依据目标类型把方法组定型为 `Action` 委托，无需显式转换。

### BC36606 修复

```vb
' Fix: Stop reporting this error in cases like this:
' BC36606: Range variable name cannot match the name of
' a member of the 'Object' class.
Let strings = From n In 1 To 5
              Select n.ToString()
'                      ~~~~~~~~
```

当范围变量名恰好与 `Object` 类成员名相同（此处 `n` 与 `ToString`）并用作 `n.ToString()` 这类调用时，不应再报告 BC36606。`n` 在查询内是正常的使用，错误检查应当更精确。

## Drawbacks
[drawbacks]: #drawbacks

- 聚合函数写进 `Select` 会与"`Select` 应只做投影"的直觉冲突，且当多个聚合共存时需要新的分组/标记语法。
- 元组解构与既有的 `From` 单项形式并存会带来两套语法分支，解析与诊断更复杂。
- 目标类型化 `Select` 依赖外层声明的类型，查询表达式内部无法独立判断错误。

## Alternatives
[alternatives]: #alternatives

- 维持现状：聚合用 `.FirstOrDefault()` 或 `Aggregate` 子句，解构先写 `Select` 投影，委托转换显式 `CType`；
- 只修 BC36606，不引入其他查询语法增强；
- 元组解构复用 `For Each` 解构（`For Each (x, y) In ...`）的既有语法，而不改 `From`。

## Unresolved questions
[unresolved]: #unresolved-questions

- 聚合函数（`FirstOrDefault`、`Count`、`Sum` 等）的完整集合与返回类型（可空与否）待定。
- `Select FirstOrDefault()` 是否可与其他 `Select` 投影/`Where` 组合，多个聚合时如何书写。
- 目标类型化在 `Select` 之外（如 `Where`）是否同样适用。
- BC36606 修复的精确判定条件（哪些成员名冲突场景仍应报错）需进一步定义。
