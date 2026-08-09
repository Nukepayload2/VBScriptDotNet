# ShapeOf Pattern Matching / `ShapeOf` 模式匹配

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`ShapeOf` 是一种按**类型（shape）**进行模式匹配的操作符：用 `Select Case ShapeOf input` 声明被匹配值，再以 `Case pn As USPhoneNumber` 形式的类型模式逐一匹配。`ShapeOf` 既可显式书写（`Select Case ShapeOf input`），也可在类型模式语境中隐式生效。

## Motivation
[motivation]: #motivation

经典的 `TypeOf ... Is` 检查只能返回布尔值，做多分支类型分发时要写一长串 `If/ElseIf`，并且每个分支都需要单独的强转。用 `ShapeOf` 可以把"按类型分发"表达成 `Select Case`，每个 `Case` 同时完成类型匹配与变量绑定（如 `pn As USPhoneNumber` 直接得到强类型变量），代码更短、可读性更高，也不容易在强转上出错。

## Detailed design
[design]: #detailed-design

显式与隐式 `ShapeOf` 操作符。原文示例（第 7 章）：

```vb
' Explicit and implicit `ShapeOf` operators.
Select Case ShapeOf input
  Case pn As USPhoneNumber
    Return New DomesticContact(pn)
  
  Case pn As InternationalPhoneNumber 
    Return New GlobalContact(pn)
    
End Select
```

- `Select Case ShapeOf input`：把 `input` 交给 `ShapeOf` 做形状（类型）匹配，这是**显式**使用 `ShapeOf`。
- `Case pn As USPhoneNumber`：每个 `Case` 声明一个变量 `pn` 并指定其类型；若 `input` 运行时类型与该类型匹配，则绑定 `pn` 并进入该分支。`As 类型` 的模式语法本身隐含了 `ShapeOf` 语义，因此可以认为是**隐式**使用 `ShapeOf`。
- 分支内 `pn` 已是强类型（如 `USPhoneNumber`），可直接传给构造函数 `New DomesticContact(pn)`，无需再写强转。

`Case` 分支自上而下尝试匹配，命中的第一个分支生效，与 `Select Case` 既有行为一致。

## Drawbacks
[drawbacks]: #drawbacks

- `ShapeOf` 与已有的 `TypeOf ... Is` 语法并存，可能让新用户困惑"什么时候用哪个"。
- 类型模式只做"是/不是该类型"的完全匹配，不能表达部分字段、模式组合等更细粒度的形状约束（这部分由其他模式建议补充）。
- 若类型层次复杂，分支顺序会影响匹配结果，需要按"最具体优先"排列。

## Alternatives
[alternatives]: #alternatives

- 保持现状：用 `If TypeOf x Is USPhoneNumber Then ... ElseIf ...` 逐分支判断，分支内再 `DirectCast`。
- 复用 `Select Case` 现有的 `TypeOf` 分支（第 3 章 `proposal-select-case-enhancements.md`），把类型匹配作为 `Case TypeOf ... Is ...` 语法，而不引入独立的 `ShapeOf` 操作符。
- 引入更通用的模式匹配机制（如 C# 式 `Is 类型 变量`），由 `ShapeOf` 作为其 VB 化入口。

## Unresolved questions
[unresolved]: #unresolved-questions

- `ShapeOf` 的匹配范围：是否包含接口、泛型开放类型、可空类型的匹配？原文未展开。
- 隐式 `ShapeOf` 的触发边界：哪些语境下 `As 类型` 会被视为模式而非普通声明？原文仅展示了 `Select Case` 中的用法。
- 多个 `Case` 同时命中时的语义（取第一个）是否需要显式规范说明，或提供 `Case Else` 之外的去重/警告机制。
