# 并行扩展 / Parallel Extensions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。作者在 18.5 中仅提出一个方向性观察：ModVB 已经集成了异步（asynchrony）但尚未集成并行（parallelism），值得将来思考，而现阶段 `Parallel.For` 已经足够。该条没有给出任何具体语法。

## Motivation
[motivation]: #motivation

原文说明（引述）：

> We've integrated asynchrony but not parallelism. Worth thinking about eventually. For now `Parallel.For` is enough.

即：语言层面已经吸收了异步支持，但并行没有对应待遇。作者认为最终值得思考并行的语言集成，但当下用 .NET 现成的 `Parallel.For` 就能满足需要，因此只作为"需要更多酝酿时间"的实验性条目列出，属于 awareness/scouting 层面的关注，而非已认可的设计。

## Detailed design
[design]: #detailed-design

原文没有给出任何示例代码或语法提案。此条目只是记录"将来值得思考并行语言集成"这一关注点；在未有进一步设计前，推荐的实践仍是使用 BCL 的 `Parallel.For` 等现成 API。

## Drawbacks
[drawbacks]: #drawbacks

- 缺乏具体语法与动机用例，难以评估并行语言集成能否带来显著收益。
- 若贸然为循环引入并行语义，会与同步 `For` 的既有心智模型冲突，还可能带来并发副作用与可重现性问题。
- .NET 的 TPL/`Parallel.For` 已覆盖大多数并行场景，语言层面重复造轮子收益有限。

## Alternatives
[alternatives]: #alternatives

- 保持现状：继续用 `Parallel.For` / TPL 完成并行需求，语言不介入。
- 仅在查询（PLINQ）或异步查询（async queries）层面提供并行能力，而不是在语句层面（参见 inactive 中 `proposal-plinq-async-queries.md`）。
- 待异步与并行在语言中稳定后再统一设计，避免与既有异步模型叠加出复杂交互。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文未提供任何语法示例，并行扩展的具体形式（语句、关键字、还是仅作用于查询表达式）完全未定。
- 并行与既有异步（asynchrony）支持如何协调，未在原文讨论。
- "将来值得思考"的时间点与触发条件，未定。
