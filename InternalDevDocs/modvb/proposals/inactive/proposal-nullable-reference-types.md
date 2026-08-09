# "Nullable Reference Types" / 可空引用类型

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。可空引用类型（NRT）在 C# 中口碑不一（mixed reviews），VB 用户也对其在 VB 中如何落地感到困惑与担忧。Anthony 明确以"用户讨厌该特性、将其关闭或建议他人关闭"作为失败判据，必须重新设计以吸收这些观察。本文记录该迭代中的设计方向，原文未给出语法。

## Motivation
[motivation]: #motivation

原文（18.16）：

> Nullable Reference Types has received mix reviews in C#. And VB users have expressed confusion and concern about how it would be done in VB. I consider it a failure if users hate the new feature turn it off or advise others to do so. I must refine the design to consider these observations.

即：可空引用类型在 C# 中评价两极；VB 用户对它在 VB 中如何落地表达了困惑与担忧。若用户讨厌新特性、将其关闭或建议他人也关闭，那么该特性就是失败的。必须重新设计，把上述观察纳入考量。

此外，本节开头所在章节（第 12 章"Null and Nothing"）的头部 Note 也明确说明：收到反馈称 NRT 特性设计（本应是该节的亮点）需要**进一步迭代**以降低困惑与烦扰，Anthony 当时正在处理中。

## Detailed design
[design]: #detailed-design

原文未给出任何语法或示例。设计意图：为 VB 重新设计可空引用类型，目标是**不让用户困惑、不让用户反感、不推动用户关闭特性**。需要与第 12 章 Null 与 Nothing 的相关设计协同：

- `Null` 字面量与可空推断（Null 字面量建议）。
- 空安全行为（`?.` 在语句中的应用，空安全行为建议）。
- 可空性流分析（可空性流分析建议）。

由于原文正在迭代，本节未给出任何具体语法，全部设计细节待定。

## Drawbacks
[drawbacks]: #drawbacks

- C# 的前车之鉴表明：NRT 容易带来大量警告噪音与迁移负担，若处理不当用户会直接关闭特性。
- 与既有 `Nothing`、晚期绑定、`Any` 伪类型等 VB 语义交织，设计空间复杂。

## Alternatives
[alternatives]: #alternatives

- 沿用 C# 的 NRT 方案（被原文否决——C# 口碑不佳，VB 用户困惑）。
- 完全不引入 NRT，保持 VB 现有的"引用类型即可空"的模型（当前状态）。
- 以更温和的方式（如只在显式标注处警告、默认关闭、基于 `Null` 字面量）重新设计，避免 C# 式的默认开启与大量噪音。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确该设计"正在进一步迭代"，未给出任何语法或语义结论。
- 如何让 VB 用户不困惑（默认行为、警告策略、迁移体验）未定。
- 与 `Nothing`、空安全行为、可空流分析等建议如何衔接，未定。
- 如何避免"用户关掉特性/劝退他人"的失败结局，未定。
