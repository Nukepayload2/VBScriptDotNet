# Lookahead and Backtracking in String Patterns / 字符串模式的前瞻与回溯

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。在设计字符串模式匹配时，考虑"前瞻 / 回溯"（lookahead / backtracking）能力：避免朴素实现分配大量无谓的中间垃圾字符串，支持需要回溯的常见模式，并允许内层模式以 lazy/greedy 方式影响外层模式的分解。原文仅列出问题，未给出具体语法。

## Motivation
[motivation]: #motivation

按现状，模式匹配从**外到内、从左到右**地求值。原文指出这会带来几个问题：

- 字符串模式匹配的朴素实现会分配大量**无谓的中间垃圾字符串**。
- 某些**常见模式需要回溯**。
- **内层模式无法影响外层模式的分解**，也无法表达 lazy（懒惰）/ greedy（贪婪）的匹配方式。

此外，原文强调：设计字符串模式匹配时，应当考虑该特性如何应用到**文本窗口 / 流**（text window/stream）或 **span** 之上。

## Detailed design
[design]: #detailed-design

原文未给出任何语法示例，仅陈述了上述问题与设计要求。设计意图：

1. **减少中间分配**：模式匹配应尽量基于切片/跨度而非反复拼接出新字符串。
2. **回溯支持**：允许常见模式（例如分隔符后跟固定结尾、嵌套定界符）在匹配失败后回退重试。
3. **内层模式影响外层分解**：内层模式的匹配结果与贪婪程度（lazy/greedy）应当能反过来决定外层如何切分。
4. **面向流与 span**：匹配不应只针对整块 `String`，还应能作用于文本窗口 / 流以及 `Span`。

由于原文没有提供示例，以上均为设计意图的记录；最终语义、匹配方向与回溯规则需要后续设计确定。

## Drawbacks
[drawbacks]: #drawbacks

- 回溯与 lazy/greedy 语义显著增加实现的复杂度，容易引入指数级匹配的陷阱。
- 面向流 / span 的匹配要求模式匹配不依赖一次性载入整个字符串，会限制模式语言的能力。

## Alternatives
[alternatives]: #alternatives

- 保持"从外到内、从左到右"的简单求值，接受中间字符串分配与无法回溯的局限。
- 让用户显式使用 `Regex` 处理需要回溯的复杂场景，字符串模式匹配只覆盖简单、确定性的分解。
- 在模式语言中引入显式的量词/贪婪标记，而不是在运行时自动回溯。

## Unresolved questions
[unresolved]: #unresolved-questions

- 回溯的范围与代价上限如何界定，如何避免性能退化？
- lazy/greedy 的语义如何用语法表达？原文未给语法。
- 如何让内层模式的分解反过来影响外层？机制未定。
- 模式如何作用于文本窗口 / 流 / span？原文仅提出应"考虑"，未给结论。
