# 强制 Await 与 Call 语句 / Required Await and Call Statement

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

规定异步调用必须被 `Await` 等待或赋给任务对象；若要显式不等待（fire-and-forget），必须使用 `Call` 语句，防止异步调用结果被意外丢弃。

## Motivation
[motivation]: #motivation

异步方法若不等待其返回的任务，异常将被静默丢失，且执行时序难以追踪。为消除"意外漏掉 Await"这类 bug，语言应把"不等待的异步调用"变成显式行为：默认禁止，需要时用 `Call` 明确表达。

## Detailed design
[design]: #detailed-design

```vb
' "Conversions" to task objects still allowed.
Let t As Task = MkDirAsync("...")

' Async calls must be awaited or task objects assigned.
MkDirAsync() ' <- Not allowed.

' Explicit syntax (required) for not awaiting.
Call FireAndForgetAsync()
```

- `Let t As Task = MkDirAsync("...")`：把异步调用"转换"为任务对象并赋给 `Task` 变量，仍然允许（调用被显式接住）。
- `MkDirAsync()`：直接调用、不等待也不赋值，不允许（原文标注 `' <- Not allowed.`）。
- `Call FireAndForgetAsync()`：显式声明"不等待"，用于 fire-and-forget 场景，是唯一允许忽略返回任务的写法。

## Drawbacks
[drawbacks]: #drawbacks

- 破坏性变更：大量现有代码中异步方法被忽略调用（未 Await），迁移成本高。
- 需要编译器在"调用被忽略"与"调用返回任务"之间插入强制检查，报错规则需覆盖重载、晚期绑定等边界。
- `Call` 表达"不等待"在语义上不够显式（与现有 `Call` 仅表示"以语句形式调用"的历史含义重叠）。

## Alternatives
[alternatives]: #alternatives

- 仅产生警告（warning）而非错误，配合 `#Ignore Warning` 抑制。
- 引入单独的 `FireAndForget` 关键字或 `Let _ = MkDirAsync()` 赋值表达不等待。
- 维持现状，依赖分析器（analyzer）提示未等待的异步调用。

## Unresolved questions
[unresolved]: #unresolved-questions

- 直接调用不等待时是编译错误还是警告，原文仅标注 "Not allowed."，未明确严重级别。
- `Call` 的 fire-and-forget 语义细节：异常是否被吞掉？是否需要配置未观测异常（unobserved exception）的处理策略。
- 与"转换到任务对象仍被允许"（`Let t As Task = MkDirAsync("...")`）规则的界限——哪些调用形式合法，待定。
