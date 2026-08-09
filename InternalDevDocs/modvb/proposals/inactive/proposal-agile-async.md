# Agile Async 与 Await 省略 / Agile Async and Await Elision

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入 `Agile Async` 修饰符声明"线程敏捷"的异步方法（await 后不捕获同步上下文，无需 `ConfigureAwait(False)`），并允许在链式异步调用中省略中间的 `Await` 操作，配合后置转换语法简化链式转换。

## Motivation
[motivation]: #motivation

默认的 `Async` 方法在 await 后恢复时会捕获并回到同步上下文，带来上下文切换开销；库代码通常必须手动添加 `ConfigureAwait(False)`。同时，多层异步调用 `(Await (Await obj.MAsync()).NAsync()).P` 嵌套括号难以阅读。目标是用 `Agile` 修饰符与中间 `Await` 的省略让代码更简洁、更明确。

## Detailed design
[design]: #detailed-design

### `Agile Async`（不捕获同步上下文）

```vb
' (Thread) `Agile` `Async` methods.
' Don't capture sync context w/o `ConfigureAwait(False)`.
Agile Async Sub FeelFreeToUseThreadPool()
```

`Agile Async` 方法在 await 后不再捕获/恢复同步上下文，等价于每个 await 点都隐式 `ConfigureAwait(False)`。

### 中间 `Await` 的省略

```vb
' Elision of intermediate `Await` operations.
' Same as `(Await (Await obj.MAsync()).NAsync()).P`
Let result = Await obj.MAsync().NAsync().P
```

`Await obj.MAsync().NAsync().P` 等价于 `(Await (Await obj.MAsync()).NAsync()).P`：只有最外层的 `Await` 显式写出，中间的 await 点自动省略。

### 与后置转换语法的配合

```vb
' Works with new conversion syntax.
' Same as `CType(CType(Await client.HttpGetAsync("..."), JsonObject)("requestId"), As Guid)`
? Await client.HttpGetAsync("...")(As JsonObject)!requestId(As Guid)
```

结合后置转换 `(As Type)`，可把 `Await client.HttpGetAsync("...")` 后置转换为 `JsonObject`，再以 `!requestId` 取字段并后置转换为 `Guid`，消除了多重嵌套的 `CType`。示例首行的 `?` 是原文即时窗口（Immediate Window）的求值提示符。

原文还标注 "Not shown: `Await obj?.MAsync()` actually working."，即 `?.` 与异步调用的组合在原文中尚未展示实际可工作。

## Drawbacks
[drawbacks]: #drawbacks

- 中间 `Await` 省略使表达式中的异步边界不明显，读者可能误以为整条链是同步调用。
- `Agile` 修饰符与现有 `Async` 并存，形成两种"异步方法"，理解成本上升。
- 自动省略中间 await 可能影响异常传播与异步状态机的划分。

## Alternatives
[alternatives]: #alternatives

- 不引入省略，保持显式 `(Await (Await obj.MAsync()).NAsync()).P`。
- 用 `ConfigureAwait(False)` 或项目级默认值替代 `Agile` 关键字。
- 只引入 `Agile` 修饰符，不做中间 `Await` 省略。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文标注 "Not shown: `Await obj?.MAsync()` actually working."：`?.` 与省略 Await 的组合是否真正可用，尚未展示。
- `Agile` 的精确语义（是否等价于每个 await 点隐式 `ConfigureAwait(False)`）及其与 `Async Iterator`、`Async Event` 的交互未定义。
- 中间 Await 省略的判定规则：是否任何返回 awaitable 的中间调用都自动隐式 await？如何避免与普通（非异步）链式调用混淆。
- 原文在 `Let t As Task = MkDirAsync("...")` 处提到 "Conversions to task objects still allowed."，即"转换到任务对象仍被允许"，其与省略语法的关系待定。
