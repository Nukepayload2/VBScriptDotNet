# Module Enhancements / 模块增强（泛型、嵌套、`StandardModule`）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

模块增强赋予 `Module` 更现代的形态：模块可以是**泛型**的（`Module Utils(Of T)`），也可以**嵌套**在其他模块内（`Module Utils ... Module Strings`）；同时默认**不再把成员提升（hoist）到包含命名空间**，经典的行为由 `<StandardModule>` 特性显式回退。

## Motivation
[motivation]: #motivation

传统 VB 模块把成员隐式提升到包含命名空间，这让静态工具类很好用，但也带来两个问题：一是命名空间被大量工具成员"污染"，名字冲突与 IntelliSense 噪音明显；二是模块无法泛型化、无法嵌套，组织能力弱。让模块默认保持私有作用域、可泛型、可嵌套，再以 `<StandardModule>` 特性按需开启经典提升行为，既能获得现代的组织方式，又保留了经典语法的兼容路径。

## Detailed design
[design]: #detailed-design

泛型与嵌套模块。原文示例（第 8 章）：

```vb
' Modules may be generic, nested, and won't hoist members
' into containing namespace by default.
Module Utils(Of T)
    ...
    Module Strings
        ...
```

- `Module Utils(Of T)`：模块带类型参数 `T`，可以针对不同类型提供泛型工具。
- `Module Strings` 嵌套在 `Utils` 内：模块支持嵌套组织。
- 注释同时指出关键行为变更：**默认不再把成员提升到包含命名空间**——模块成员只能通过模块名限定访问。

经典行为回退。原文示例：

```vb
' Classic behavior can be opted into with attribute.
<StandardModule>
Module IOHelpers
    ...
```

- `<StandardModule> Module IOHelpers`：给模块加 `<StandardModule>` 特性后，成员重新被提升到包含命名空间，恢复经典行为。`IOHelpers` 中的辅助方法可直接以无前缀名字调用。

## Drawbacks
[drawbacks]: #drawbacks

- 默认行为（不提升）与经典模块行为（提升）相反，老代码迁移时需要逐模块加 `<StandardModule>`，迁移成本与出错面不小。
- 泛型模块提升成员时类型参数无法被调用点推断，限制与困惑并存。
- 嵌套模块的可访问性规则（Public/Friend/Private）与提升之间的交互需要额外规范。

## Alternatives
[alternatives]: #alternatives

- 保持现状：模块始终提升成员、不可泛型、不可嵌套；代价是命名空间污染问题长期存在。
- 反向设计：默认提升，用特性关闭提升；但会让绝大多数老代码无改动迁移，而新代码更少受益。
- 用 `Shared Class` + `Friend` 类型替代模块的泛型/嵌套需求，不修改模块本身。

## Unresolved questions
[unresolved]: #unresolved-questions

- 泛型模块的成员是否可被提升？提升后的类型参数如何限定？
- `<StandardModule>` 特性是从 `Microsoft.VisualBasic` 既有特性继承，还是新增特性？属性是否可配置（如只提升部分成员）？
- 嵌套模块成员提升到哪一级（最外层命名空间还是父模块）？原文未说明。
