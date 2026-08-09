# 版本化工具 / More Versioning Tools

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 希望给库作者提供更多"不破坏二进制兼容"的版本化能力：把类型移动到不同的命名空间、重命名类型或方法、设计"虚拟 / 元命名空间"、提供更干净的 `System` 命名空间，以及升级项目时的工具化支持。

## Motivation
[motivation]: #motivation

- 现有 `TypeForwardedToAttribute` 只能把类型转发到**另一个程序集**，不能跨命名空间移动。
- 重命名类型或方法在保持二进制兼容的前提下同样受限。
- 能否设计一种"虚拟 / 元命名空间"概念，让作者提供更清爽的体验，例如 `Imports Wpf` 代替实际需要的 5 到 6 个命名空间。
- 能否为新开发者提供一个更干净的 `System` 命名空间：`StringBuilder` 应该在那里，`_AppDomain` 大概不应该。
- 升级后的项目上，工具化（查找帮助、文档等）会是什么样子。

## Detailed design
[design]: #detailed-design

原文未提供示例代码，只有一组设问。把原文涉及的关键标识符以 VB 形式列出：

```vb
' 原文提及的关键标识符（无示例代码）：
TypeForwardedToAttribute   ' 现有：仅能把类型转发到另一个程序集
Imports Wpf                ' 设想的"虚拟/元命名空间"：替代实际需要的 5-6 个命名空间
StringBuilder              ' 应该出现在"干净"的 System 命名空间中
_AppDomain                 ' 大概不该出现在那里
```

内容要点逐条照录：

1. **跨命名空间移动**：`TypeForwardedToAttribute` 已有，但它只能把类型移动到不同程序集；能否移动到不同命名空间？
2. **重命名**：能否重命名类型或方法而不破坏二进制兼容？
3. **虚拟 / 元命名空间**：能否设计一种概念，让作者提供更干净的体验，例如 `Imports Wpf` 代替 5 或 6 个实际需要的命名空间？
4. **更干净的 `System`**：能否为新开发者提供更干净的 `System` 命名空间？`StringBuilder` 应该在那里，`_AppDomain` 大概不应该。
5. **升级工具化**：升级后的项目上，查找帮助、文档等工具化支持长什么样？

## Drawbacks
[drawbacks]: #drawbacks

- "虚拟 / 元命名空间"会在编译期解析与元数据之间引入间接层，增大复杂度。
- 重命名 / 移动而保持二进制兼容，本质是在元数据上做别名与重定向，出错时很难调试。
- 更干净的 `System` 命名空间会与现有公开 API 表面冲突，迁移代价大。

## Alternatives
[alternatives]: #alternatives

- 只用 `TypeForwardedToAttribute` 继续承担兼容性职责（现状）。
- 通过 NuGet 包版本化加文档迁移流程替代元数据级重命名。
- 靠 IDE 重构工具完成重命名，而不是在运行时 / 元数据层做映射。

## Unresolved questions
[unresolved]: #unresolved-questions

- "移动到不同命名空间"的机制与 `TypeForwardedToAttribute` 的关系。
- 类型 / 方法重命名如何在元数据中表达而不破坏二进制兼容。
- "虚拟 / 元命名空间"的精确语义（聚合多个命名空间为一个导入单元）与 `Imports` 的交互。
- 更干净的 `System` 命名空间如何界定取舍（`StringBuilder` 留、`_AppDomain` 去）。
- 升级项目时"查找帮助、文档"的工具化具体形态。
