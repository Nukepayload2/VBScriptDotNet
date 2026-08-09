# `Structure` 约束重载 / Overloading on the `Structure` constraint

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。泛型方法无法按约束（`Class` vs `Structure`）重载，但 VB 借助"伪参数 + 约束参与重载决议"这一现成行为，让 `Sub M(Of T As Class)(p As T)` 与 `Sub M(Of T As Structure)(p As T, Optional ignored As Object = Nothing)` 可以共存并按值/引用类型分派。目标是在源码层面消除这个假参数，即使元数据里仍需要它。

## Motivation
[motivation]: #motivation

处理泛型时，有时希望为值类型和引用类型提供不同实现，尤其是涉及任何可空性时（"especially when any kind of nullability is involved"）。但无法按泛型约束重载方法。不过存在一个"cheat"（原文措辞）。期望结果是让这种按值/引用类型分派的写法更整洁，不需要方法作者写出假参数、也不让调用者看到它。

## Detailed design
[design]: #detailed-design

原文示例：

```vb
' (As we all know...) you can't overload on generic constraints.
' This is illegal:
Sub M(Of T As Class)(p As T)
Sub M(Of T As Structure)(p As T)

' But this is not:
Sub M(Of T As Class)(p As T)
Sub M(Of T As Structure)(p As T, Optional ignored As Object = Nothing)

' Because VB considers type parameter constraints in determining
' which overloads are applicable, these lines do what you'd hope:
M("") ' Calls reference type overload.
M(1)  ' Calls value type overload
```

要点：

- 直接按约束重载（两个签名形参相同）非法。
- 给第二个重载加一个可选假参数 `Optional ignored As Object = Nothing` 后合法：VB 在确定哪些重载可应用时**会考虑类型参数约束**，因此 `M("")` 命中引用类型重载，`M(1)` 命中值类型重载。
- 这是一个"待办"（to-do）：考虑如何从源码中移除这个假参数，即使在元数据里仍需要它。

## Drawbacks
[drawbacks]: #drawbacks

- 假参数 `ignored` 暴露在公共 API 签名中，调用者可能误传，作者也必须为每个需要分派的方法写一份"装饰"签名。
- 依赖"约束参与重载决议"这一隐式行为，可读性差、容易被误解为语法错误。

## Alternatives
[alternatives]: #alternatives

- 保持现状（使用假参数 cheat）。
- 真正允许按泛型约束重载（突破 CLR 限制，改动更大）。
- 用不同的方法名（如 `MRef`/`MVal`）或在方法内部用 `If GetType(T).IsValueType` 手动分派。

## Unresolved questions
[unresolved]: #unresolved-questions

- 假参数应如何在源码中隐藏（语法糖），而在元数据中保留？
- 编译器如何自动生成/识别该假参数，重载决议规则需要哪些调整？
- 当 `T` 同时适配 `Class` 与 `Structure` 版本时（如 `T` 无约束），决议规则如何定义？
- 可空性场景（原文提到"especially when any kind of nullability is involved"）下分派的具体行为未展开。
