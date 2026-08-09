# Patterns as Data / 模式作为数据

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。组合（composition）是管理复杂度的关键，但目前还没有办法把模式当作**数据**传递或组合。修复这一缺口可能打开一些使用场景。原文仅提出这一观察，未给出语法。

## Motivation
[motivation]: #motivation

原文（18.13）：

> Composition is essential to managing complexity but as-is there isn’t yet a way to pass patterns around as data or compose them. Fixing this could open some scenarios.

即：组合对管理复杂度至关重要，但按现状尚无办法把模式作为数据传递或组合它们；修复这一点可能打开一些使用场景。

## Detailed design
[design]: #detailed-design

原文未给出任何语法或示例。设计意图：让模式成为可传递、可组合的一等数据。需要研究的方向包括：

- 模式能否存入变量、作为参数传给函数/过程，或在运行时动态构建。
- 模式之间如何组合（逻辑与、逻辑或、取反、嵌套复用）。
- 组合后的模式如何参与 `Select Case`/`If` 的匹配，以及与命名模式、用户定义模式方法等机制的关系。

由于原文没有示例，以上仅为设计意图记录；"模式作为数据"的具体形态完全待定。

## Drawbacks
[drawbacks]: #drawbacks

- 把模式变成运行时数据会模糊"语法层面的匹配"与"数据驱动的匹配"的边界，增加语言复杂度。
- 运行时动态构建模式可能让编译器无法做既有的静态分析（如穷尽性检查、模式优化）。

## Alternatives
[alternatives]: #alternatives

- 保持模式只出现在语法匹配位置，组合靠 `Case` 顺序与嵌套表达（当前状态）。
- 只允许编译期可组合的模式（如命名模式的嵌套），不把模式开放为任意运行时数据。

## Unresolved questions
[unresolved]: #unresolved-questions

- "模式作为数据"的具体语法与类型未定。
- 模式数据的创建方式（字面量？对象？委托？）未定。
- 组合模式的穷尽性、可达性与性能分析如何保持，未定。
- 原文仅陈述可能性（"could open some scenarios"），未承诺任何设计。
