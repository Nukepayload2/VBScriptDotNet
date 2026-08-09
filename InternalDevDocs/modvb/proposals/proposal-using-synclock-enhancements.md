# `Using` / `SyncLock` 增强 / Using & SyncLock Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `Using` 块：允许在 `Using` 内使用 `Catch`/`Finally`、支持 `Using` 头的元组解构、并允许按方法名识别 `Dispose`（包括可能是扩展方法）；`SyncLock` 获得与 `Using` 类似的增强，并可能支持 `Monitor.Enter/Exit` 之外的同步方法。

## Motivation
[motivation]: #motivation

- `Using` 块中想要捕获资源获取异常或异步释放资源（`DisposeAsync`）时，现在必须在外层再包一个 `Try`；
- `Using one, two, three = GetTriplet()` 一次取多个资源的愿望无法满足；
- 目前 `Using` 只认 `IDisposable.Dispose` 方法，很多类型有自定义/扩展的释放方法却无法用于 `Using`；
- `SyncLock` 只能用于 `Monitor.Enter/Exit`，缺乏灵活性。

## Detailed design
[design]: #detailed-design

### `Using` 块内的 `Catch` / `Finally`

```vb
' `Catch` and/or `Finally` in `Using` block.
Using reader = File.OpenText(filename)
    ...

Catch ex As IOException
    logger.Log("Failed to read configuration.")

Finally
    Await reader.DisposeAsync()

End Using
```

`Using` 块可直接跟随 `Catch`/`Finally`：`Catch ex As IOException` 捕获读取配置失败；`Finally` 中 `Await reader.DisposeAsync()` 异步释放（与 3.9 `Await` 增强配合）。

### 元组解构

```vb
' Tuple deconstruction.
Using one, two, three = GetTriplet()
    ...
```

`Using` 头一次解构三个资源变量 `one`、`two`、`three`（来自 `GetTriplet()`），三者均在块内可见并在结束时释放。

### 按名称识别 `Dispose`

```vb
' Recognize `Dispose` method by name (maybe even an extension method?).
```

`Using` 不要求目标实现 `IDisposable`，而是按方法名（`Dispose`）识别释放入口，甚至可以是扩展方法。原文对此标注为存疑（"maybe even an extension method?"）。

### `SyncLock` 类似增强

原文仅述：

> Basically the same stuff as `Using`. Maybe some enhancements for additional synchronization methods other than `Monitor.Enter/Exit`.

`SyncLock` 获得与 `Using` 相同的增强（块内 `Catch`/`Finally`、元组解构），并可能支持 `Monitor.Enter/Exit` 之外的其他同步方法。

## Drawbacks
[drawbacks]: #drawbacks

- 按名称识别 `Dispose` 会削弱类型安全：任何含 `Dispose` 方法的类型都可用于 `Using`，可能与 `IDisposable` 契约（`Dispose` 后 `ObjectDisposedException`）语义不完全一致。
- `Using` 内嵌 `Catch`/`Finally` 与 `Try` 的职责边界变得模糊。
- 为 `SyncLock` 引入其他同步机制（自旋锁、读写锁等）增加语义与性能契约的复杂度。

## Alternatives
[alternatives]: #alternatives

- 资源获取错误继续用外层 `Try ... Using ... End Try` 包裹，不把 `Catch` 并入 `Using`。
- 多资源 `Using` 保持嵌套（`Using one`/`Using two`/`Using three`），不引入头解构。
- `Dispose` 识别坚持 `IDisposable` 接口，自定义释放用扩展方法辅助。

## Unresolved questions
[unresolved]: #unresolved-questions

- "按方法名识别 `Dispose`"是否真支持扩展方法（原文以 "maybe ... ?" 标注，未定）。
- `SyncLock` 除 `Monitor.Enter/Exit` 外还要支持哪些同步方法（原文用 "Maybe" 标注，未定）。
- `Using` 元组解构中各资源释放的顺序（逆序）与异常交互。
- `DisposeAsync` 与按名识别 `Dispose` 的协调（如何选择同步/异步释放路径）。
