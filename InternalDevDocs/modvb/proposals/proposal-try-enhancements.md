# `Try` 增强 / Try Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `Try` 块：允许在 `Catch`/`Finally` 块内使用 `Await`；支持在 `Try` 头中声明块级局部资源（`Try resource1 = ..., resource2 As Type`）；并允许 `Catch`/`Finally` 附加到任意块（如 `For Each ... Finally ... Next`）。

## Motivation
[motivation]: #motivation

- 异步清理/日志需要在 `Catch`、`Finally` 中 `Await`，当前 VB 不允许在 `Catch`/`Finally` 里出现 `Await`；
- 需要跨 `Try`/`Catch`/`Finally` 可见的资源声明，现在只能声明在 `Try` 之外（作用域泄漏到函数其余部分）；
- `Catch`/`Finally` 只能跟随 `Try`，无法直接在 `For Each` 等循环上做"块结束时的统一处理"。

## Detailed design
[design]: #detailed-design

### `Catch`/`Finally` 中的 `Await`

```vb
' `Await` in `Catch` and `Finally` blocks.
Try
    ...
Catch ex As Exception
    Await logger.LogAsync(ex)
    
Finally
    Await resource.DisposeAsync()
End Try
```

`Catch` 内 `Await logger.LogAsync(ex)` 异步记录异常；`Finally` 内 `Await resource.DisposeAsync()` 异步释放资源。与 `Async` 方法及 `AsyncIterator` 增强配合。

### `Try` 头的块级局部声明

```vb
' Local declarations scoped to entire `Try`/`Catch`/`Finally` block.
Try resource1 = GetResource(),
    resource2 As ResourceHandle
    
    resource1.Open()
    resource2 = resource1.GetChildResourceHandle()
    
Catch ex As Exception
    resource2?.Revert()
    resource1?.Revert()
    
Finally
    resource2?.Dispose()
    resource1?.Dispose()
End Try
```

`Try` 头以逗号声明块级局部变量：`resource1`（由 `GetResource()` 推断类型）、`resource2 As ResourceHandle`。这些变量在 `Try`、`Catch`、`Finally` 中均可见，且作用域限于该 `Try` 块内（不泄漏到函数体其余部分）。`Catch`/`Finally` 中配合 `?.` 空安全调用做清理。

### `Catch`/`Finally` 可用于任意块

```vb
' `Catch` and/or `Finally` in any block.
For Each p In Process.GetProcesses() Where p.Name = "chrome.exe"
    ...
    
Finally
    p.Kill()
Next
```

`Catch`/`Finally` 不再局限于 `Try`，可以附加到 `For Each` 等任意块：上例在 `For Each` 循环结束后统一 `p.Kill()` 清理。

## Drawbacks
[drawbacks]: #drawbacks

- 把资源声明写进 `Try` 头（而不是声明式 `Using`），与 3.10 `Using` 建议职责重叠，需明确推荐用法。
- `Catch`/`Finally` 附加到任意块改变了异常模型边界，需要定义异常是从块内传播到 `Catch` 还是只捕获本块异常。
- `Catch`/`Finally` 中 `Await` 需要整个方法/块是异步上下文，与同步方法冲突时的诊断规则需明确。

## Alternatives
[alternatives]: #alternatives

- 资源声明改用 `Using`（见 3.10），`Try` 头仅保留声明便利。
- 块尾清理继续用 `Finally` + `Try` 包裹循环，代价是额外缩进一层。
- `Catch`/`Finally` 附加任意块可只支持 `Finally`（清理），不支持 `Catch`（错误处理）以减少语义复杂。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Try` 头声明与 `Using` 头声明、`Do` 头声明的语法统一（逗号分隔、类型推断规则）。
- `Catch` 附加到非 `Try` 块时，异常匹配与 `End Try`/`Next` 终止符的书写规则。
- `Await` 出现在 `Catch`/`Finally` 对 `Async` 方法签名与错误传播的影响。
