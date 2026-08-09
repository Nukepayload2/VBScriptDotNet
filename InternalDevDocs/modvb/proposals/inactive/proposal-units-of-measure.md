# 度量单位 / Units of Measure

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。受 F# 度量单位（units of measure）启发，Anthony 喜欢这种"带单位"的感觉，但不确信这是显而易见的必需特性。他的多次练习目前只得出一个无争议的特性：**泛型运算符（Generic Operators）**；另有一个非必需的语法糖 `Weight(in lbs)`，可清理一些使用上的粗糙边缘。

## Motivation
[motivation]: #motivation

F# 诞生时带来很多酷想法。Anthony 喜欢度量单位的感觉，但不确定它是 no-brainer。他做了多次练习，考察两种可能：(1) VB 用户如何在不依赖语言支持的情况下，仅用库实现一个度量单位系统；(2) 若要让它可用，最小需要添加哪些语言特性。期望结果是让"创建和使用这类系统"的体验可接受。他将继续调研（"I will continue to investigate"）。

## Detailed design
[design]: #detailed-design

原文（18.19）无完整 VB 代码示例，唯一代码片段是语法糖：

```vb
Weight(in lbs)
```

要点：

- **泛型运算符（Generic Operators）**：目前唯一无争议的结论。让运算符可作用于泛型类型参数，是构建库级度量单位系统（如 `Weight` 参与数值运算）的基石。
- **`Weight(in lbs)` 语法糖**：可选、非必要，用于清理使用上的粗糙边缘（单位标注的简洁写法）。该写法不改变语义，只是让用户写出更整洁的单位声明。
- Anthony 认为探索这个空间"绝对值得一次专门的、深入的帖子"（a dedicated and detailed in-depth posting），即本文档只是占位，深入讨论待后续。

## Drawbacks
[drawbacks]: #drawbacks

- Anthony 明确表示不确定这是 no-brainer；引入语言级单位系统可能增加学习与实现成本。
- 单位运算的隐式转换、单位消去（如米/秒）、与现有数值类型交互等都可能是复杂面。
- 语法糖 `Weight(in lbs)` 非必要，若引入可能带来新的解析规则与关键字负担。

## Alternatives
[alternatives]: #alternatives

- 库级实现：完全不引入语言支持，由库作者在泛型上模拟（Anthony 已在练习此路线）。
- 仅引入泛型运算符这一个最小特性，单位系统完全靠库构建。
- 把单位作为"数值变量上的注释"而非真实类型跟踪（见 18.20，`proposal-annotated-types.md`）。

## Unresolved questions
[unresolved]: #unresolved-questions

- 度量单位应建模为真实类型，还是如 18.20 所问"作为数值变量上的注释而非真实类型"？
- 泛型运算符的确切语法与约束（哪些运算符可泛型化）？
- `Weight(in lbs)` 的语义：`in` 后的单位如何解析、如何参与运算与转换？
- 原文未给出任何类型、运算符或转换的设计细节，Anthony 表示将继续调研。
