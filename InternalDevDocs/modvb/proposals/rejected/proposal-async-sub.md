# Async Sub 返回类型与默认异步类型配置 / Async Sub Return Types and Configurable Default Async

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

允许 `Async Sub` 显式指定返回的 awaitable 类型（如 `Task`、`ValueTask`），并允许省略返回类型与结果类型，由编译期可配置的默认规则（默认异步类型与名称后缀）补齐，从而消除 `Async Function ... As Task` 的样板写法。

## Motivation
[motivation]: #motivation

现代 VB 中异步方法必须写成 `Async Function ... As Task`，即使没有返回值也必须用 `Function` 伪装，形成原文所述的 "Function As Task" 别扭形式。开发者希望直接用 `Async Sub` 表达"真正的异步 Sub"，并希望轻量场景默认使用 `ValueTask`、公共方法默认使用 `Task`，避免每次都手写返回类型。

## Detailed design
[design]: #detailed-design

### 真正的 `Async Sub` 与显式返回类型

```vb
' New syntax for true "Async Sub"s.
' No more "Function As Task" weirdness.
Async Sub Flush() As Task
    ...
End Sub

' Default awaitable type for lightweight
Async Sub MkDir() As ValueTask
```

- `Async Sub Flush() As Task`：真正的异步 Sub，返回类型显式写为 `Task`。
- `Async Sub MkDir() As ValueTask`：轻量场景的默认 awaitable 类型为 `ValueTask`。

### 可配置的默认异步类型与名称后缀

```vb
' Lightweight Async syntax with configurable default async
' types and name-suffixing. e.g.
' Public methods use Task, Private methods use ValueTask.

' Same as `Async Sub Flush() As Task` (configurable).
' Same as `Function FlushAsync() As Task.
Async Sub Flush()

' Same as `Async Function Pull(...) As Task(Of JsonObject).
' Same as `Function PullAsync(...) As Task(Of JsonObject).
Async Function Pull(...) As JsonObject
```

- `Async Sub Flush()`：省略返回类型，等价于 `Async Sub Flush() As Task`（默认异步类型可配置），也等价于 `Function FlushAsync() As Task`（名称后缀 "Async" 可配置）。
- `Async Function Pull(...) As JsonObject`：声明的结果类型为 `JsonObject`，等价于 `Async Function Pull(...) As Task(Of JsonObject)`，也等价于 `Function PullAsync(...) As Task(Of JsonObject)`。
- 配置示例见原文注释：公共方法默认用 `Task`，私有方法默认用 `ValueTask`；默认异步类型与名称后缀均可配置。

## Drawbacks
[drawbacks]: #drawbacks

- 隐式补齐返回类型与 "Async" 后缀依赖全局/项目配置，方法签名在阅读时不直观，需靠配置猜测实际生成的名字。
- `Async Function ... As JsonObject` 改变了现有 `As Task(Of JsonObject)` 的书写习惯，易与"直接返回 `JsonObject` 本体"混淆。
- 同一功能（生成 `Task`/`ValueTask` 与 `Async` 后缀）存在多套配置开关，增加编译器复杂度。

## Alternatives
[alternatives]: #alternatives

- 维持现状，继续写 `Async Function Flush() As Task`，不引入隐式类型。
- 只引入显式 `Async Sub ... As Task/ValueTask`，不提供省略返回类型的默认配置。
- 用代码分析器/源生成器（而非编译器配置）自动补全 `Async` 后缀。

## Unresolved questions
[unresolved]: #unresolved-questions

- 默认异步类型与名称后缀的配置机制如何表达？原文仅举例"Public methods use Task, Private methods use ValueTask"，未给出具体配置语法。
- 省略返回类型时，`Async Sub` 与 `Async Function` 各自默认生成的 `Task`/`ValueTask(Of T)` 规则是否联动，未明确。
- 显式 `As Task` 与省略后配置出的 `Task` 在元数据与重载解析上是否完全等价，待定。
