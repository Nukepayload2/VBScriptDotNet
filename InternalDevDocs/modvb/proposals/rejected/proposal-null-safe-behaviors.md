# 空安全行为 / Null-Safe Behaviors

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

把 `?.`（null 条件访问 / 空传播）从表达式扩展到多种语句与运算符，使对象为 null 时相关操作表现为 no-op（空操作）：空集合不枚举、空任务不等待、空目标不赋值、空接收者不注册/注销事件、空接收者返回 null 委托等。

## Motivation
[motivation]: #motivation

现代代码中大量调用链可能命中 null 接收者。若每个 null 接收者都在各语句中抛 `NullReferenceException`，需要开发者手工用 `If ... IsNot Nothing` 包裹。目标是把 `?.` 的"空即跳过"语义统一应用到循环、Await、赋值、事件注册、委托创建等语句形态，消除样板判空。

## Detailed design
[design]: #detailed-design

### 空集合不枚举

```vb
' Doesn't enumerate if collection is null.
For Each item In collection?
For Each item in obj?.Member
```

- `For Each item In collection?`：集合为 null 时不枚举（空循环）。
- `For Each item In obj?.Member`：`obj` 为 null 时取成员结果为 null，同样不枚举。

### 空任务不等待 / 空接收者不调用异步成员

```vb
' Won't wait a null task/throw exception.
Await someTask?
Await obj?.MemberAsync()

' Same as `If(obj Is Null, Null, Await obj.MemberAsync())`
Let result = Await obj?.MemberAsync()
```

- `Await someTask?`：任务为 null 时不等待、不抛异常。
- `Await obj?.MemberAsync()` 与 `Let result = Await obj?.MemberAsync()`：`obj` 为 null 时结果为空，等价于 `If(obj Is Null, Null, Await obj.MemberAsync())`。

### 空目标不赋值

```vb
' Won't assign to member of null object.
obj?.Member = value
```

`obj?.Member = value`：`obj` 为 null 时不执行赋值。

### 空接收者不注册/注销事件

```vb
' Won't register/unregister from event on null object.
AddHandler obj?.E, handler
RemoveHandler obj?.E, handler
```

`AddHandler obj?.E, handler` / `RemoveHandler obj?.E, handler`：`obj` 为 null 时不在事件上注册/注销处理器。

### 空接收者返回 null 委托

```vb
' Returns a null delegate if receiver is null.
Let d As Action? = AddressOf obj?.Method
```

`AddressOf obj?.Method`：`obj` 为 null 时返回一个 null 委托（此处 `d` 声明为 `Action?`）。

## Drawbacks
[drawbacks]: #drawbacks

- 同一 `?.` 在不同语句中行为各异（跳过循环、跳过赋值、返回 null 委托等），规则分散、记忆成本高。
- 静默跳过可能掩盖逻辑错误：空集合不枚举、空目标不赋值，出错时无从提示。
- `Await obj?.MemberAsync()` 的表达式类型（可空）会向上传播，波及调用处的类型推断。

## Alternatives
[alternatives]: #alternatives

- 维持现状：由开发者用 `If ... IsNot Nothing` 或 null 条件调用（`?.`）逐处处理。
- 仅扩展 `?.` 到表达式求值，不扩展到语句（For Each / AddHandler / 赋值左侧）。
- 通过流分析 + 警告（而非静默跳过）提示可能的 null 访问。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确标注疑问："Should querying a null collection evaluate to null or empty?"（查询 null 集合时，结果是 null 还是空序列？），待定。
- `Await obj?.MemberAsync()` 的类型是否是可空任务/可空结果，以及该可空性如何沿调用链传播，未完全确定。
- 空安全语句与 `RemoveHandler`、复合赋值（如 `obj?.Member &= x`）等组合行为的完整性，原文仅列示了上述示例，未穷举。
