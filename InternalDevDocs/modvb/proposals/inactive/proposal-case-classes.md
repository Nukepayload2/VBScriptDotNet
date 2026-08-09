# `Case` 类、结构与接口 / Case Classes, Structures, and Interfaces

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 对"判别联合（discriminated unions）"重新框定后提出的语法草图：以 `Case` 作为 `Class`/`Structure`/`Interface` 的前缀，表达封闭的、按形状分发的类型族。原文只附了一张截图说明，**没有给出任何文字形式的语法**，故本建议仅记录概念，不虚构语法。

## Motivation
[motivation]: #motivation

"判别联合"在 dotnet 的"改天看看"清单上（和很多 F# 特性一样）。Anthony 一直在看函数式视频、读文章以理解它们，但大多数时候并不认同（"I mostly don't"）。他看到了 2 个让他"撬开门缝"（cracked the door）的例子，于是为自己重新框定了这个问题，并产出了若干仅供说明的语法草图。他计划"改天再深挖，但要趁早"（I'll dive into this another day, but sooner than later.）。

## Detailed design
[design]: #detailed-design

原文（18.21）**只附了一张截图**（image：584×693）作为语法草图，没有任何文字形式的方法，因此无法摘录 VB 示例代码，也不虚构语法。可确认的概念要点：

- 特性名带 `Case` 前缀：`Case Class`、`Case Structure`、`Case Interface`。
- 这是对"判别联合"的重新框定：不是引入新的泛型化的 sum 类型构造器，而是用 `Case` 修饰已有的 `Class`/`Structure`/`Interface` 声明形式。
- 语法草图以图片形式展示，原文未转录为文本。

## Drawbacks
[drawbacks]: #drawbacks

- 判别联合需要封闭性（编译器知道所有分支），对 `Class` 这种默认开放的类型体系是根本性冲突。
- `Case` 前缀占用关键字 `Case`（`Select Case` 已使用），解析器与向后兼容需要评估。
- 原文仅有图片草图，无语义说明，风险未知。

## Alternatives
[alternatives]: #alternatives

- 不做：Anthony 大部分时候并不认同判别联合（"I mostly don't"）。
- 采用传统的 sum 类型（嵌套类型 + 基于类型的模式匹配）路线，而非 `Case` 前缀修饰。
- 用现有的 `ShapeOf` / 模式匹配增强（`proposal-shapeof-pattern-matching.md`）部分覆盖场景。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Case` 修饰符如何表达封闭性：限定同文件同类型，还是整个程序集？
- `Case Structure` 与 `Case Interface` 的具体语义（结构分支、接口分支）？
- 图片草图中的实际语法形式（Anthony 表示"改天深挖，但要趁早"，未给出文字形式）。
- 与模式匹配、`TypeOf ... Is` 流分析、穷尽性检查如何协同？
