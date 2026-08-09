# `Default` 方法 / Default Methods

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入 `Default` 方法（可调用对象/仿函数）：以 `Default Sub Invoke(...)` 定义后，实例可直接像函数一样调用（`sp_Users_Initialize()`），同时支持早期与晚期绑定；并配合动态语言式能力——动态加方法（`obj.Multiply = Function(left, right) left * right`）、带名参数传给方法、以及晚期绑定下的 `AddHandler`/`RemoveHandler`/`AddressOf`。

## Motivation
[motivation]: #motivation

- 把"代表一个存储过程/函数"的对象做成可调用对象时，目前需要 `Invoke`/`Call` 之类的显式方法名，无法像函数一样直接 `sp_Users_Initialize()`；
- 动态场景希望运行时给对象补方法（`obj.Multiply = Function(...) ...`），而非预定义全部成员；
- 伪动态 API 需要把实参名等信息原样传给目标方法（`sp_Users_SelectById(id:=Guid.Empty, role:="admin")`），并在晚期绑定下支持事件挂接（`AddHandler` 等）。

## Detailed design
[design]: #detailed-design

### `Default` 方法与可调用对象

```vb
' Default Methods to enable both early- and late-
' bound invocable objects/functors.
Class StoredProcedureInfo
    
    Default Sub Invoke(ParamArray args As Object())
        ...
    End Sub
End Class

Let sp_Users_Initialize As StoredProcedureInfo = ...
sp_Users_Initialize()
```

`Default Sub Invoke(...)` 把一个方法标记为默认调用入口：实例 `sp_Users_Initialize` 直接写 `sp_Users_Initialize()` 即可调用 `Invoke`。早期绑定时（实例类型已知）按 `Default` 方法调用；晚期绑定时（如 `Any`/动态）同样按默认方法分派，从而同时支持两类绑定的可调用对象/仿函数。

### 动态加方法

```vb
' And dynamic language like capabilities:
obj.Multiply = Function(left, right) left * right
? obj.Multiply(3, 5)
```

对动态对象 `obj` 直接赋值一个 Lambda 作为其成员 `Multiply`，随后 `obj.Multiply(3, 5)` 调用该动态方法并得到结果，类似脚本语言的动态成员注入。

### 带名参数传给方法

```vb
' Better support for pseudo-dynamic APIs w/o runtime
' binding.
' Argument list info passed to method w/ names, etc.
sp_Users_SelectById(id:=Guid.Empty, role:="admin")
```

对于伪动态 API（无需运行时绑定），调用 `sp_Users_SelectById` 时把参数名（`id`、`role`）与值一并传给目标方法，支持 `:=` 带名实参，让这类 API 无需依赖运行时反射即可获得参数元数据。

### 晚期绑定下的事件与委托

```vb
' Late-bound support for the following:
AddHandler
RemoveHandler
AddressOf
```

晚期绑定（`Any`/动态）下支持 `AddHandler`、`RemoveHandler`、`AddressOf`，用于动态挂接事件、解绑事件与取方法地址。

## Drawbacks
[drawbacks]: #drawbacks

- `Default` 方法与 VB 已有的 `Default Property`（索引器）共用 `Default` 修饰符，需要消歧（默认属性 vs 默认方法）。
- 可调用对象的 `Invoke` 重载冲突、`ParamArray` 与可选参数组合的解析更复杂。
- 动态加方法与带名参数传递依赖运行时分派，性能与类型安全代价需权衡。

## Alternatives
[alternatives]: #alternatives

- 继续显式调用 `Invoke`/`Call` 方法，不引入可调用对象语法；
- 用 `Callable` 属性/特性标注对象为可调用，而不引入 `Default` 方法；
- 动态能力仅依赖 DLR 与 `ExpandoObject` 既有机制，不新增语言语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文标注 "Not shown": 晚期绑定的 `AddressOf` 修复（Dev10 时代的一个 bug fix），具体问题与方案未展示，待定。
- 原文标注 "Maybe": 晚期绑定是否也支持 `Await`、`For Each`、`Queries`，这三个场景仅列出未定论。
- `Default` 方法是否允许多个 `Default` 重载；`Default Sub` 与 `Default Function`（有返回值）能否并存。
- 可调用对象的泛型 `Invoke`、`ref`/`out` 参数如何表达。
- 动态加方法（`obj.Multiply = Function(...) ...`）是否只对 `ExpandoObject`/DLR 对象生效，还是也支持普通对象。
