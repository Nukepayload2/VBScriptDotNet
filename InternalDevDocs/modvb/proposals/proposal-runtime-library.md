# VB 运行时库改进 / VB Runtime Library Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议改善 **VB 运行时库**（"VB runtime"）本身，而非新增语法。内容包括：运行时方法（尤其是内建转换）的性能、更友好/更有信息量的转换异常、更友好/更有信息量的晚期绑定（late-binding）异常、晚期绑定下对更多早绑定语言语义（如 target-typing）的支持、面向替代平台/目标等更好的一体化组织、对二进制字面量语法（及分隔符）在转换中的 bug 修复，以及 `My` 助手的现代化。

> 注：本项以运行时为主，非纯语法建议。

## Motivation
[motivation]: #motivation

VB 的语言体验很大程度取决于运行时库：内建转换（`CInt`/`CDate`/`Val` 等）、晚期绑定、`My` 助手都是"语言即运行时"的一部分。Anthony 在第 17 章列举了"需要改进的 VB 运行时问题"，指出性能与异常质量、晚期绑定语义一致性、跨平台整合、转换 bug、`My` 现代化都是长期诉求。

期望的结果：

- 转换与常见运行时路径更快；
- 转换失败/晚期绑定失败时，异常能给出有用的上下文（而不是笼统的运行时异常）；
- 晚期绑定尽可能复用早绑定的语义（如 target-typing）；
- 运行时在替代平台/目标上有更好的组织与可用性；
- 二进制字面量在转换中的 bug 被修复；
- `My` 助手（如 `Async` 方法）跟上现代 .NET。

## Detailed design
[design]: #detailed-design

Anthony 原文第 17 章以要点列表给出方向，未附代码示例；以下按原文要点逐项说明。涉及的关键代码路径可用现有 VB 语法举例（非新语法）：

```vb
' 内建转换：性能与异常质量改善的对象
Let n = CInt("123")          ' 转换异常应更"有用/有信息量"
Let d = CDate("1/1/2026")

' 晚期绑定：希望更多早绑定语义（如 target-typing）可用
Dim obj As Object = GetLateBound()
obj.Clone()                  ' 晚期绑定异常应给出上下文
```

原文要点清单：

1. **运行时方法性能，尤其是内建转换**（intrinsic conversions）：`CInt`/`CDate`/`CLng` 等转换路径的热点优化。
2. **转换异常更友好**：转换失败时提供有用、有信息量的异常（指出输入、期望类型等）。
3. **晚期绑定异常更友好**：晚期绑定操作失败时提供有用、有信息量的异常。
4. **晚期绑定下更多早绑定语义支持**：例如 target-typing（目标类型化），让晚期绑定结果尽量符合早绑定行为。
5. **面向替代平台/目标等更好的分解（factoring）**：运行时在替代平台/目标上有更合理的分层与复用。
6. **Bug 修复**：例如转换中对二进制字面量语法（以及可能的数字分隔符）的支持。
7. **`My` 助手现代化**：如 `Async` 方法（Anthony 注明"可能已经完成"）。
8. **附加功能**（Additional functionality）：运行时按需补齐缺失能力。

## Drawbacks
[drawbacks]: #drawbacks

- 运行时改动（尤其异常消息与行为变化）可能破坏依赖现有异常文本/行为的既有代码与测试。
- 性能优化与可读性/可移植性之间需权衡，且需要大量基准与回归保护。
- 非语法改动，收益分散、不易被当作"语言新特性"获得关注与资源。

## Alternatives
[alternatives]: #alternatives

- 不做运行时改动，靠调用方自行捕获并包装异常、自行做性能优化（样板多、体验不一致）。
- 把晚期绑定语义/转换性能问题转交给 .NET 运行时（CoreCLR）层面解决，不动 VB 运行时。
- 用分析器（诊断 + 快速修复）模拟部分改进（如提示改用 `TryParse`），不做运行时行为变更。

## Unresolved questions
[unresolved]: #unresolved-questions

- 内建转换性能的目标基准（哪类转换、多大规模优化）。
- 异常消息/类型的兼容性策略（是否提供新的异常子类型 vs 修改现有消息）。
- 晚期绑定"更多早绑定语义"的具体清单与边界（target-typing 之外还包括哪些）。
- 二进制字面量转换 bug 的确切场景与是否同时支持数字分隔符（Anthony 以问号标注分隔符）。
- `My` 现代化中 `Async` 是否已完成（Anthony 注明"might be done already"）。
