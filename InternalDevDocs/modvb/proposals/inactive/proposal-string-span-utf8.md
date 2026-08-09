# String vs Span/ReadOnlySpan(Of Char/Byte) and UTF-8 / String 与 Span 及 UTF-8

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。当字符串数据以 `Span(Of Char)` / `ReadOnlySpan(Of Char)` / `ReadOnlySpan(Of Byte)`（UTF-8）形式出现时，VB 的字符串处理必须依然出色，不能让现代问题给代码库引入丑陋。原文仅有一句话，未提供任何语法。

## Motivation
[motivation]: #motivation

原文原文（18.11）仅此一句：

> String handling in VB must be awesome! We cannot allow modern problems to introduce ugliness into our codebases.

即：VB 的字符串处理必须是出色的；不能让现代问题（指 `Span`、UTF-8 等新数据形态与性能诉求）在 VB 代码库中引入丑陋的代码。

## Detailed design
[design]: #detailed-design

原文未给出任何语法或示例。设计意图：当字符串以 `Span(Of Char)`、`ReadOnlySpan(Of Char)` 或 UTF-8 编码的 `ReadOnlySpan(Of Byte)` 存在时，VB 应当提供与之同样优雅的字符串处理方式，避免开发者被迫写出笨拙的往返转换（例如先把 `Span` 复制成 `String` 才能使用习惯的字符串 API）。标题所列类型为设计覆盖的范围：

- `Span(Of Char)` / `ReadOnlySpan(Of Char)`
- `ReadOnlySpan(Of Byte)`（UTF-8）

具体如何让字符串模式匹配、字符串运算符与 `String` API 覆盖这些形态，原文未给出结论，属于待研究的设计空间。

## Drawbacks
[drawbacks]: #drawbacks

- 让 `String` 之外的类型获得一流的字符串语义会带来大量重载与 API 面扩张，维护成本高。
- `Span` 不能作为字段/闭包捕获，与某些字符串用法天然不兼容，可能造成能力上的落差。

## Alternatives
[alternatives]: #alternatives

- 保持 `String` 为唯一一流字符串类型，`Span`/UTF-8 场景靠显式转换处理（当前状态）。
- 引入统一的字符串抽象或接口，`String`、`Span(Of Char)`、UTF-8 缓冲都实现之。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文仅一句陈述意图，未给出任何设计方向或语法，全部细节待研究。
- 现代问题具体指哪些（UTF-8？非托管互操作？性能热点？）未展开。
- `Span` 与字符串模式匹配、字符串字面量等特性如何协作，未定。
