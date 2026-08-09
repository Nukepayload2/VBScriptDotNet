# VB 惯用 JSON 序列化器 / VB Idiomatic Custom JSON Serializers

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。作者（18.8）指出，"ref struct" 最常见的声音来自用 `Utf8JsonReader` 编写自定义 JSON 序列化器；但假定"VB 中写反序列化器的惯用方式就是 C# 的方式"本身值得商榷。若 VB 能提供一种自然的抽象，就能推迟、缓解或绕开（delay, mitigate, or side-step）这个问题。此条紧随上一节（更健壮的映射）而来，是纯讨论，没有给出具体语法。

## Motivation
[motivation]: #motivation

原文说明（引述）：

> This goes with the previous example. The only scenario I consistently hear for "ref structs" is custom JSON serializers with `Utf8JsonReader`. Setting aside the general issue, this assumes that the idiomatic way to write a deserializer in VB is the way it's done in C#. I could delay, mitigate, or side-step the issue if VB had a natural abstraction for this.

即：人们最常为 "ref struct" 辩护的场景是用 `Utf8JsonReader` 写自定义 JSON 序列化器；暂且抛开 ref struct 的一般性问题不谈，这一动机默认了"VB 写反序列化器的方式应该照搬 C#"的假设。如果 VB 能提供一种适合自身的自然抽象，就能推迟、缓解或绕开这个问题。

## Detailed design
[design]: #detailed-design

原文没有给出任何语法示例，也没有具体的抽象设计。此条目仅记录方向性判断：VB 需要一个"自然的抽象"来编写自定义 JSON 序列化/反序列化，从而不必照抄 C# 中以 `Utf8JsonReader` + "ref struct" 为中心的写法。具体形态（关键字、类型、字面量能力）完全留白，与 `proposal-robust-mapping.md` 的映射能力是否同一套机制也未说明。

## Drawbacks
[drawbacks]: #drawbacks

- 缺乏具体语法与示例，无法评估"自然抽象"的形态与实现成本。
- 若最终仍需要与 .NET 的 `Utf8JsonReader`/"ref struct" 互操作，绕开它可能带来性能或兼容性代价。
- 与 JSON 字面量、`ShapeOf` 模式匹配等未定稿特性耦合，依赖链较长。

## Alternatives
[alternatives]: #alternatives

- 维持现状：直接照 C# 的方式使用 `Utf8JsonReader`，不引入专门抽象。
- 让 JSON 字面量/`ShapeOf` 模式匹配承担序列化/反序列化的惯用路径（见 `proposal-json-literals.md`、`proposal-json-pattern-matching.md`、`proposal-robust-mapping.md`），减少对自定义 writer/reader 的依赖。
- 在运行时库层面提供 VB 风格的序列化辅助，把"自然抽象"放到底层而非语法层。

## Unresolved questions
[unresolved]: #unresolved-questions

- VB "自然的抽象"具体是什么形态（语法、类型、库 API），原文完全没有给出，未定。
- 该抽象如何与 "ref struct"/`Utf8JsonReader` 互操作，或是否打算完全绕开，未定。
- 与上一节（更健壮的映射）是否共用同一套机制，未定。
