# 重复声明处理与 `Appendable` 方法 / Handling duplicate declarations & "Appendable" methods

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。处理源生成器作者在声明式编程中遇到的名称冲突与重复定义问题。Anthony 个人不喜欢把来自多个文件的语句拼接进一个方法，也不希望两个 VB 脚本文件被简单首尾相接执行。他提到"源生成器版的多播委托"（multicast delegate）类比，认为它可能影响其各种声明式编程方案之间某些场景的解决方式。

## Motivation
[motivation]: #motivation

源生成器的编写（与性能）会因试图避免名称冲突、重复定义（或确保不存在重复）而受到负面影响。Anthony 曾在不同层面思考过处理该问题的方法。他不希望两个 VB 脚本文件被简单地连续执行，也不希望终端开发者过于费心思考 partial 文件会被如何组合、组合顺序如何。期望的结果是：在声明式编程场景下，多个来源能以"追加"（Appendable）的方式共同贡献到同一目标，而无需人为处理拼接与顺序。

## Detailed design
[design]: #detailed-design

原文（18.17）未给出任何 VB 代码示例，只描述了概念方向，故本建议不虚构语法，如实记录原文要点：

- **避免拼接**：Anthony 明确表示不喜欢把跨多文件的语句拼接进一个方法。这意味着"把 N 个 partial 文件的内容串成一个方法体"的设计被否决。
- **组合顺序**：不希望终端开发者思考 partial 文件组合的顺序；文件不应只是"背靠背"运行。
- **多播委托类比**：一个"源生成器版多播委托"的类比，可能影响他的各种声明式编程方法（如智能属性、替换修饰符、语义预处理等）之间如何处理某些场景——即"多个来源各自贡献、共同作用于同一目标"的模型，而非顺序拼接。
- **各层处理**：原文提到"thought through some ways to handle this at various layers"，即在多个抽象层（语言层、生成器层、元数据层等）分别处理重复声明。

## Drawbacks
[drawbacks]: #drawbacks

- 若引入"多播委托"式的追加语义，多个来源共同贡献时仍可能存在冲突或非确定性，需要明确定义合并规则。
- 抽象层级越多，语言与工具的实现复杂度越高。
- 概念尚未收敛，没有一个具体方案，难以评估代价。

## Alternatives
[alternatives]: #alternatives

- 维持现状：源生成器作者自行处理名称冲突与去重（原文明确提到这会伤害编写体验与性能）。
- 限制生成器产出：生成器只产出独立成员，禁止把多文件语句拼接成一个方法。
- 由 `Partial` 成员等现有机制覆盖（见 `proposal-partial-members.md`），而不是引入新的"追加"语义。

## Unresolved questions
[unresolved]: #unresolved-questions

- "Appendable" 的确切语义是什么？是方法级、成员级还是模块级？
- 多播委托类比如何映射到源生成器输出：多个"处理器"如何注册、合并、排序？
- 重复定义与名称冲突应由编译器报错、源生成器规避，还是由用户显式解决？
- 原文未给出任何语法或 API 设计，全部待定。
