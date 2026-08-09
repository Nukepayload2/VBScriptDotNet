# 生成式编译器脚本 / Generative compiler scripting

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 对此仅有一句判断："我相当确定这是个坏主意，但总有一天会去冒险"（I'm pretty sure this is a bad idea. But at some point I'll venture into that dragon's lair.）。原文未给出任何动机、语法或设计，本建议如实记录这一表态。

## Motivation
[motivation]: #motivation

原文未展开。可合理推断，"生成式编译器脚本"是指让开发者以脚本参与、驱动或改写编译过程的某种机制；但 Anthony 自己认为这是坏主意，尚未给出任何使用场景或期望结果。

## Detailed design
[design]: #detailed-design

原文（18.18）仅一句话，无任何语法、语义或代码示例，故无可摘录内容。所有设计均未开始。

## Drawbacks
[drawbacks]: #drawbacks

- 让用户脚本在编译器内部运行，意味着把编译期可信边界交给第三方代码，安全与稳定性风险高。
- 可能侵蚀编译器既有的确定性、缓存与并行构建模型。

## Alternatives
[alternatives]: #alternatives

- 不实现该功能（Anthony 自己认为这是坏主意）。
- 用源生成器 / 分析器在受控的进程中实现同等能力，相对更安全。

## Unresolved questions
[unresolved]: #unresolved-questions

- "生成式编译器脚本"具体指什么：改写 IR？注入代码？自定义编译流水线？
- 脚本的运行时机、宿主与权限模型？
- 与源生成器、语义预处理（`proposal-semantic-preprocessing.md`）的关系？
- 原文全部留白；Anthony 表示"总有一天会去冒险"，短期内无进展。
