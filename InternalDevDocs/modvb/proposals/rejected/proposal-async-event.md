# Async 事件 / Async Events

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

允许把事件声明为异步事件：`Async Event E(sender As Object, e As EventArgs)`，使事件处理器可以异步执行。原文以疑问句形式提出，属探索性建议。

## Motivation
[motivation]: #motivation

UI 与 I/O 场景中，事件处理器经常需要执行异步工作。当前 VB 事件处理器要么写成 `Async Sub`（事件本身的签名仍是普通委托），要么手动等待 `Task`，缺少"事件本身就是异步"的一等表达。

## Detailed design
[design]: #detailed-design

```vb
' `Async` events?
Async Event E(sender As Object, e As EventArgs)
```

`Async Event E(sender As Object, e As EventArgs)` 声明一个异步事件。原文仅给出了这一行示例，并在注释中以 "`Async` events?" 的疑问形式标注，表明该设计点尚未成形。

## Drawbacks
[drawbacks]: #drawbacks

- 异步事件的语义（`RaiseEvent` 是否等待所有处理器完成后返回、多个处理器如何并发/串行、异常如何聚合）均需全新规定，复杂度高。
- 事件委托类型（如 `AsyncEventHandler`）与现有 `EventHandler` 的互操作需要设计。
- 事件多为"广播式"，等待异步处理器会改变调用方时序，在捕获同步上下文时可能引入死锁风险。

## Alternatives
[alternatives]: #alternatives

- 维持现状：用 `Async Sub` 编写事件处理器（处理程序内部异步），事件本身仍是普通委托。
- 引入专门的 `AsyncEventHandler` 委托类型，不新增 `Async Event` 语法。
- 将异步事件建模为 `Func(Of Object, EventArgs, Task)` 类型的普通属性。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文以 "`Async` events?" 的疑问形式提出，未给出完整语义，整体处于未定稿状态。
- `RaiseEvent` 的等待/异常聚合语义、`AddHandler`/`RemoveHandler` 的委托类型、以及与现有同步事件处理器的兼容性，均未确定。
- 异步事件与 `Agile Async`（不捕获同步上下文）是否配合使用，待定。
