# 链式三元表达式 / Simplified "Chained" Ternary Expressions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 提出一种简化的"链式"三元表达式，把多个条件/值对写进一个表达式里，可视为 `Select Case` 的"轻量表达式版"（`Case` expression lite）。它区别于经典的三参数 `If()`，允许在一个表达式里表达多分支取值。

## Motivation
[motivation]: #motivation

写闰年判断这类"多条件取值"时，现有的 `If()` 只有两个分支；多分支要么嵌套 `If()`，要么退化成 `Select Case` 加赋值语句。Anthony 想用单个表达式直接表达多个条件分支，让"取哪个值"紧邻其条件。

## Detailed design
[design]: #detailed-design

原文示例（忽略 HTML 高亮标签后的纯 VB 文本）：

```vb
' Something like this.
? If(year Mod 400 = 0,
       True,
     year Mod 4 = 0 AndAlso
     year Mod 100 <> 0,
       True,
     Else,
       False)
```

结构解读（配中文注释）：

```vb
' ? 前缀区分"链式三元"与经典的三参数 If()。
? If(条件1, 值1,
     条件2, 值2,
     Else, 兜底值)
```

- 前导的 `?` 表示这是新的链式形式（区别于已有的三参数 `If()`）；
- 之后是交替出现的"条件, 值"对，逐条求值，命中即取值；
- 结尾用 `Else` 关键字作为兜底分支（对应 `Select Case` 的 `Case Else`）。

## Drawbacks
[drawbacks]: #drawbacks

- 前导 `?` 与空合并/可空相关语法可能混淆（`?` 在语言中已有多种含义）。
- 分支很多时可读性未必优于 `Select Case` 语句，可能鼓励写出很长的表达式。
- 与经典三参数 `If()` 并存，会带来教学与阅读上的二义性负担。

## Alternatives
[alternatives]: #alternatives

- 嵌套经典 `If()`：可行，但括号层层嵌套难以阅读。
- 用 `Select Case` 语句加赋值：最直白，但无法作为表达式嵌入。
- 交给模式匹配 / `Select Case` 表达式（另一份建议），而非引入新的 `? If()` 形态。

## Unresolved questions
[unresolved]: #unresolved-questions

- 前导 `?` 是否必要（能否直接扩展 `If()` 的参数数量）。
- `Else` 在这里是关键字还是普通标识符，大小写与转义如何处理。
- 条件是否必须穷尽（没有 `Else` 时取什么值）。
- 条件中能否使用 `AndAlso`/`OrElse` 等运算符，原文仅示例了一个 `AndAlso`。
- 表达式类型如何由各分支值推断（公共类型规则）。
