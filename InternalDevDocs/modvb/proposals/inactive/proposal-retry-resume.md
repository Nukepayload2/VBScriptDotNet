# `Retry`/`Resume` / Retry/Resume

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。作者（18.6）指出今天仍使用 `On Error` 的场景只剩下 `Resume Next` 和用 `Goto` 重试；若 `Try` 结构能覆盖这两种场景，非结构化错误处理（`On Error`/`Resume Next`/`Goto`）就可以被弃用。原文给出了用 `Try ... Catch ... Goto Retry` 实现重试的示例，属于讨论性条目，无新语法提案。

## Motivation
[motivation]: #motivation

原文说明（引述）：

> The only scenarios where I can see using `On Error` today is for `Resume Next` or retrying with `Goto`. If we can hit that with `Try` that kind of unstructured error handling can be deprecated.

即：今天还会用 `On Error` 的场景只有两类——`Resume Next`，以及用 `Goto` 做重试。如果 `Try` 结构能够覆盖这两类需求，就可以弃用 `On Error` 这类非结构化错误处理。作者并未提议新的 `Retry`/`Resume` 关键字，而是探讨如何用现有（或增强后的）`Try` 达成目的。

## Detailed design
[design]: #detailed-design

用 `Try` + `Catch ... When` + `Goto` 表达重试模式：

```vb
' This isn't that bad.
Let retryCount = 0
Try
    Retry:
    ...
Catch ex As Exception When retryCount < 3
    retryCount += 1
    Goto Retry
End Try
```

- `Let retryCount = 0`：初始化重试计数。
- `Try ... Catch ex As Exception When retryCount < 3 ... Goto Retry`：当捕获到异常且重试次数未满时，递增计数并跳回 `Retry:` 标签重新执行。
- 作者评价这段"还不算太糟"（This isn't that bad）。

原文还对比了用多个 `Try/Catch` 空块模拟 `Resume Next` 的写法，并认为它更差但通常不常见、且有其他更优雅的方式：

```vb
' This is worse but likely uncommon and there are
' other ways to do this elegantly.
Try
    ' Step 1
Catch
End Try

Try
    ' Step 2
Catch
End Try

Try
    ' Step 3
Catch
End Try
```

作者并未提出新的 `Retry`/`Resume` 语法，只是指出若 `Try` 能承担这两类职责，非结构化错误处理即可弃用。

## Drawbacks
[drawbacks]: #drawbacks

- 用 `Catch ... When retryCount < 3` + `Goto` 表达重试，逻辑散落在 `Catch` 与标签之间，可读性一般；作者自己也只评价为"不算太糟"。
- 用多个空 `Try/Catch` 模拟 `Resume Next` 明显笨重，若这是唯一途径则弃用 `On Error` 的收益会被稀释。
- 引入专门的重试/恢复语法（如新的 `Retry` 关键字）会增加语言面，而现有 `Try` 已可部分实现。

## Alternatives
[alternatives]: #alternatives

- 维持 `On Error`/`Resume Next`/`Goto`，不弃用非结构化错误处理。
- 依赖增强后的 `Try`（参见 `proposal-try-enhancements.md`）覆盖重试与恢复场景，之后逐步弃用 `On Error`。
- 提供库级重试辅助（如通用的 Retry 函数），把重试逻辑移出语言构造。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文没有提出新的 `Retry`/`Resume` 关键字语法，条目名仅为主题归纳；是否需要专门的构造完全未定。
- `Resume Next`（"忽略错误继续下一条语句"）能否被 `Try/Catch` 干净地覆盖、空 `Try/Catch` 是否可接受，未定。
- 是否/何时正式弃用 `On Error` 系列语法，未定。
- `Catch ex As Exception When retryCount < 3` 中 `Goto Retry` 与标签的写法是否就是最终形态，未定。
