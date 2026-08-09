# 脚本与解释执行 / Scripting and Interpreted Code

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。原文对"脚本与解释执行"仅有一句回应："Of course."（当然。）。Anthony 表示赞同这个方向，但没有任何设计与细节。本建议如实记录这一"一句话态度"。

## Motivation
[motivation]: #motivation

原文全文只有一句肯定的应答，没有给出任何动机、场景或期望结果。可推断的方向是把 VB 作为脚本语言解释执行（与"顶级代码 / 沉浸式文件"的思路一致），但这一切都停留在推测层面。

## Detailed design
[design]: #detailed-design

原文没有提供任何语法、API 或设计。以下是原文唯一的可用文本，以 VB 注释形式保留：

```vb
' 原文对"脚本与解释执行"的全部回应：
' Of course.
```

除此之外没有更多可引用内容。由于没有可依据的设计，本建议无法给出任何语法细节。

## Drawbacks
[drawbacks]: #drawbacks

- 没有设计，无从谈起代价；若当真做解释执行，性能、与编译模式的二义性等都需要评估。
- 解释执行与现有"编译 → 运行"的心智模型相冲突。

## Alternatives
[alternatives]: #alternatives

- 不做此功能：保持 VB 仅作为编译型语言。
- 依赖"顶级代码 / 沉浸式文件"等相邻建议，提供接近脚本的体验而不引入解释器。

## Unresolved questions
[unresolved]: #unresolved-questions

- 脚本化的运行形态（REPL、解释器，还是编译为可执行脚本）完全未定。
- 与现有编译管道、引用、NuGet 依赖如何交互。
- 原文除一句 "Of course." 外没有任何信息，本建议实质处于空状态。
