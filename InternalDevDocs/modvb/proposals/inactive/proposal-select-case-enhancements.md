# `Select Case` 增强 / Select Case Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议为 `Select Case` 扩展多种匹配方式：`TypeOf` 类型匹配、`ShapeOf` 形状匹配、`Is`/`IsNot` 恒等匹配、`Is Like` 通配符匹配，以及 `In`/`NotIn` 集合成员匹配。

## Motivation
[motivation]: #motivation

目前 VB 的 `Select Case` 只支持对取值表达式做等值匹配，导致"按运行时类型分派""按对象引用相同分派""按集合成员分派"等常见场景必须退化为 `If` 链或 `If ... ElseIf ...`，冗长且易错。本建议让 `Select Case` 成为统一的分派语句。

## Detailed design
[design]: #detailed-design

### 按类型匹配

```vb
' `Select Case` on type.
Select Case TypeOf obj
    Case AppDomain
        ...
```

`TypeOf obj` 取得 `obj` 的运行时类型，`Case AppDomain` 匹配该类型（配合类型流分析，匹配分支内 `obj` 可收窄为 `AppDomain`）。

### 按形状匹配

```vb
' `Select Case` on shape.
Select Case ShapeOf coordinate
    Case (latitude, longitude)
        ...
```

`ShapeOf coordinate` 对 `coordinate` 做形状（结构）匹配，`Case (latitude, longitude)` 解构出两个元素。

### 按恒等匹配

```vb
' `Select Case` on identity.
Select Case sender
    Case Is CloseButton
        ...
        
    Case IsNot MainForm
        ...
```

`Case Is CloseButton` 用引用恒等（`Is`）匹配 `sender` 是否为 `CloseButton` 这一特定对象；`Case IsNot MainForm` 表示"不是 `MainForm`"。

### `Like` 通配符匹配

```vb
' `Select Case` with `Like`.
Select Case str
    Case Is Like "?*@?*.?*"
        ...

    Case IsNot Like "#.#.#.#"
        ...
```

`Case Is Like "?*@?*.?*"` 对 `str` 做 `Like` 通配符匹配（邮箱地址模式）；`Case IsNot Like "#.#.#.#"` 表示不匹配 IP 地址模式。

### 集合成员匹配

```vb
' `Select Case` on collection membership.
Select Case str
    Case In bannedWords
        ...

    Case NotIn bannedWords
        ...
```

`Case In bannedWords` 判断 `str` 是否属于 `bannedWords` 集合（映射为 `Contains`）；`Case NotIn bannedWords` 表示不属于该集合。

## Drawbacks
[drawbacks]: #drawbacks

- `Select Case` 一次只能测试一个主题表达式，类型/形状/恒等/集合这几种匹配无法在同一语句内组合出复杂谓词，仍需要依赖守卫式 `Case`。
- 关键字量增大（`TypeOf`、`ShapeOf`、`In`/`NotIn`、`Is`/`IsNot` 与 `Like` 的组合），解析与文档负担上升。
- 类型匹配与现有 `TypeOf ... Is` 表达式并存，需明确 `Case TypeOf obj` 中 `Case AppDomain` 与 `Case TypeOf obj Is AppDomain` 两种写法的关系。

## Alternatives
[alternatives]: #alternatives

- 保持 `Select Case` 只做等值匹配，类型/恒等/集合分派继续用 `If ... ElseIf`；代价是冗长。
- 用布尔守卫 `Case <boolExpr>` 表达以上所有场景；代价是失去各模式专有的解构与收窄能力。
- 把恒等匹配推广为通用 `Case Is ...` 谓词语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Case Is NotIn bannedWords` 之类 `Is`/`IsNot` 与 `In`/`NotIn`、`Like` 的组合规则是否都应支持。
- `TypeOf` 匹配分支内的类型收窄与现有类型流分析（`TypeOf ... Is`）的交互。
- `In`/`NotIn` 对泛型集合、`IEnumerable`、数组、查找表的适用类型集合。
- `ShapeOf` 与第 7 章通用模式匹配（`ShapeOf` 模式匹配建议）的语法统一问题。
