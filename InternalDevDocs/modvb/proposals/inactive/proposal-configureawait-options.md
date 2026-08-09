# ConfigureAwait Options / Fire-and-Forget Exception Handling / ConfigureAwait 选项与"忘了处理"的未观测异常

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。研究新的 `ConfigureAwait` 选项，并进一步限制 void 返回异步方法的蔓延；同时让"忘记"处理 `Task` 返回异步方法时，开发者能够表达/配置各种未观测异常（unobserved exceptions）的处理模式。原文未给出语法。

## Motivation
[motivation]: #motivation

原文（18.15）：

> Void-returning async methods are my white whale. I must find more ways to limit their spread. Additionally when "forgetting" a Task-returning async method I need to be sure folks can express/configure the various patterns to unobserved exceptions.

即：void 返回的异步方法是"我（Anthony）的白鲸（white whale）"，必须找到更多办法限制它们的蔓延。此外，当"忘记"处理一个返回 `Task` 的异步方法时，需要确保开发者能够表达/配置针对**未观测异常**的种种处理模式。

## Detailed design
[design]: #detailed-design

原文未给出任何语法或示例。设计意图分两方面：

1. **限制 void 返回异步方法的蔓延**：继续寻找语法/工具手段，让 `Async Sub`（或配置默认异步返回类型）难以被误用，从而减少"void 返回异步方法"的扩散。
2. **未观测异常的表达与配置**：当调用方"忘记" `Await` 一个返回 `Task` 的异步方法时，需要让开发者能够表达并配置对未观测异常的处理模式（例如记录日志、静默、上报到全局处理器等）。

可参考的既有相关建议：异步返回类型配置（`Async Sub` 建议）、强制 `Await` 与 `Call` 语句（要求 Await 调用建议）。

## Drawbacks
[drawbacks]: #drawbacks

- 增加 `ConfigureAwait` 选项与未观测异常配置会扩大异步模型的配置面，可能让默认行为更难预测。
- 过度约束"忘记 `Await`"的写法可能打断合法的 fire-and-forget 模式。

## Alternatives
[alternatives]: #alternatives

- 维持现状：未观测异常沿用 .NET 全局的 `TaskScheduler.UnobservedTaskException` 等既有机制。
- 通过编译期强制 `Await`（要求 Await 建议）+ 显式 `Call` 放行 fire-and-forget，间接减少未观测异常，而非新增 `ConfigureAwait` 选项。
- 限制 void 返回异步方法改用默认异步返回类型配置（`Async Sub` 建议）达成，而非新增选项。

## Unresolved questions
[unresolved]: #unresolved-questions

- 新的 `ConfigureAwait` 选项具体有哪些、语义如何，原文未定。
- 未观测异常的处理模式如何"表达/配置"（语法或 API），未定。
- 与"强制 Await""`Call` 语句"等建议如何协同，未定。
- 原文未给任何语法或示例。
