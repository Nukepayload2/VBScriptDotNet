# 扩展属性 / Extension Properties

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入**扩展属性**：把现有"扩展方法"的机制推广到属性，用 `<Extension>` 特性在 `Module` 中声明 `ReadOnly Property SecondOrDefault(Of T)(list As List(Of T)) As T`，使属性调用可以以"接收者.属性"形式用于任意列表。Anthony 注明扩展属性**可以是泛型**。

## Motivation
[motivation]: #motivation

现在只能为既有类型写扩展方法（`Function`/`Sub`），不能写扩展属性。对"派生视图/便捷取值"类需求（如取第二个元素、取长度、取默认值），扩展方法往往要写成 `list.SecondOrDefault()`，而扩展属性可写成 `list.SecondOrDefault` 这种更声明式的读取形态，让接收者上的只读取值更自然。

期望的结果：在 `MyExtensions` 模块中声明泛型扩展属性，即可在任何 `List(Of T)` 上直接读取 `list.SecondOrDefault`。

## Detailed design
[design]: #detailed-design

在 `Module` 中用 `<Extension>` 特性声明只读扩展属性，接收者作为首个参数（`list`）：

```vb
' Extension Properties.
Module MyExtensions

    ' Note: Can be generic.
    <Extension>
    ReadOnly Property SecondOrDefault(Of T)(list As List(Of T)) As T
        Return If(list.Count > 1, list(1), Nothing)

End Module
```

- `<Extension>` 特性标记该属性为扩展属性；
- `(Of T)` 泛型参数使属性可适用于任意元素类型的 `List(Of T)`；
- 首个参数 `list As List(Of T)` 是被扩展的接收者；
- `ReadOnly` 表明只读：读取 `list.SecondOrDefault` 等价于调用 `If(list.Count > 1, list(1), Nothing)`——元素多于 1 个时返回第二个元素，否则返回 `Nothing`。

调用形态（Anthony 未给出显式调用示例，语义与扩展方法一致）：

```vb
Let second = someList.SecondOrDefault
```

## Drawbacks
[drawbacks]: #drawbacks

- 扩展属性与现有扩展方法的绑定规则重叠，需明确"属性 vs 同名方法"的优先级与歧义处理。
- 只读/可写扩展属性组合时，setter 形态（`list.X = value`）的语法与实现规则需单独设计。
- 泛型扩展属性的类型推断（接收者类型 → 泛型参数）复杂度上升。

## Alternatives
[alternatives]: #alternatives

- 只做扩展方法，属性读取继续用方法形式（`list.SecondOrDefault()`），不扩展语法。
- 允许可写扩展属性（带 setter），而非只读。
- 用 `Default` 属性或其他机制模拟"接收者上的便捷读取"。

## Unresolved questions
[unresolved]: #unresolved-questions

- 是否支持可写（带 `Set`）扩展属性，Anthony 示例仅给出 `ReadOnly`。
- 扩展属性是否可叠加（链式调用 `a.b.c`）并参与 `With`/`For Each` 等语句。
- 泛型扩展属性与目标类型推断（target-typing）的相互作用。
- 扩展属性在 C# 侧的互操作形态（C# 未暴露扩展属性，如何消费/生成）。
