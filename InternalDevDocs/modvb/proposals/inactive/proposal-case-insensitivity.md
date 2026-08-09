# Case-insensitive Collation Integration / 大小写不敏感与排序规则集成

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。探索如何更优雅地把"大小写不敏感 / 排序规则"（collation）接入 VB 内置的字符串比较构造，使它们能与现有 .NET API（如 `ComparisonType`、`StringComparer`）自然集成。原文仅给出了调查起点与设计方向，未提供任何具体语法。

## Motivation
[motivation]: #motivation

VB 有许多内置构造会参与字符串比较：

- 二元表达式中的比较运算符 `=`、`<>`、`<`、`>`、`<=`、`>=`。
- `Select Case` 块中的同一批运算符。

许多应用受益于大小写不敏感的字符串比较，但这意味着**不去用**内置的字符串支持——而内置字符串支持是 BASIC 自远古以来的标志（hallmark）之一。

`Option Compare Text` 虽可用，但它有**意外的 null 处理**，且只影响比较运算符、`Select Case` 以及 VB 运行时库中的某些 API（其底层实现基本是 `<CallerOptionCompareSettingAttribute>`）。它**完全没有覆盖 `Distinct` 查询操作符**。于是用户不得不放弃这些语法糖，改去调用 `Equals` 并传入 `ComparisonType` 或 `StringComparer`。

在 String 与 JSON 模式匹配加入后，这个问题会变得更糟。原文希望得到一个能与现有 .NET API 集成的优雅方案，并列出了若干可作为调查起点的设计方向，但明确表示"需要更多考量"（needs more consideration）。

## Detailed design
[design]: #detailed-design

原文未给出任何具体语法，仅列出了需要覆盖的现状与相关构造。摘录自原文的相关 VB 文本如下：

```vb
' 内置参与字符串比较的构造（现状）
If a = b OrElse a < b OrElse a >= b Then
    ...
End If

Select Case name
    Case "admin"
        ...
    Case Else
        ...
End Select

' Option Compare Text 影响上述比较运算符与 Select Case
Option Compare Text

' 当前为了大小写不敏感而放弃语法糖的替代写法
If a.Equals(b, StringComparison.OrdinalIgnoreCase) Then
    ...
End If

' Distinct 不受 Option Compare Text 覆盖
Dim unique = (From n In names Select n).Distinct()
```

设计意图：让 VB 的字符串比较构造能够声明或感知大小写不敏感 / 排序规则设置，并统一接入现有 .NET 的比较 API（`ComparisonType`、`StringComparer`），从而避免"为了一点不敏感比较就退回 `Equals` + 比较器参数"的繁琐。调查起点包括：

- `<CallerOptionCompareSettingAttribute>`：VB 运行时库如何把 `Option Compare` 设置传递给被调 API。
- 如何让 `Distinct` 等查询操作符也感知该设置。
- 如何让 String 与 JSON 模式匹配中的比较遵循同样的规则。

## Drawbacks
[drawbacks]: #drawbacks

- "排序规则"是全局性的状态，与局部、显式的比较器冲突时会产生令人困惑的行为。
- `Option Compare Text` 已存在，任何新方案必须与它的历史语义（含其"意外的 null 处理"）兼容或明确定义取代关系。
- 隐式影响大量构造（运算符、`Select Case`、查询操作符、模式匹配）会显著扩大实现面与测试面。

## Alternatives
[alternatives]: #alternatives

- 维持现状：继续让用户显式调用 `Equals` + `ComparisonType`/`StringComparer`，代价是放弃 BASIC 风格的内置字符串比较语法糖。
- 仅扩展现有 `Option Compare Text` 的覆盖范围（例如补上 `Distinct` 与模式匹配），而非引入新机制。
- 引入显式的比较上下文对象，在特定代码块内局部切换排序规则。

## Unresolved questions
[unresolved]: #unresolved-questions

- 新方案应作为新的语法/上下文，还是增强 `Option Compare Text`？原文未定。
- 如何覆盖 `Distinct` 及未来的 String/JSON 模式匹配？机制待定。
- `Option Compare Text` 现有的 null 处理行为是否需要修正，如何修正？
- 原文标注 "it needs more consideration"——尚未形成设计草案。
