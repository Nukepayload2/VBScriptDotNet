# Named Pattern Inputs / 命名模式的输入参数

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。某些设想的命名模式会受益于额外的"输入"（例如 `Regex("\d+", n)`），但目前命名模式的"实参"列表是**只输出**的。本文记录这一调查方向，原文未给出结论或语法。

## Motivation
[motivation]: #motivation

在命名模式（见命名模式建议）的设计中，模式以"带 `Out` 参数、返回 `Boolean` 的函数"为模型，其"实参"列表用于把子值绑定到调用方的变量——即只承载**输出**。但有些设想的模式除了输出外还需要**输入**，例如正则表达式模式需要接收正则字符串，而匹配出的值才是输出。原文示例：

> Some hypothetical patterns would benefit from additional "inputs" (e.g. `Regex("\d+", n)`) but currently the "argument" list for a named pattern is output only. This should be investigated.

即：`Regex("\d+", n)` 中，`"\d+"` 是输入（正则表达式），`n` 是输出（匹配结果），但当前的实参列表只支持输出，因此应加以研究。

## Detailed design
[design]: #detailed-design

原文仅给出设想的调用形态，未给出确定语法。该形态摘录自原文：

```vb
' 设想的模式调用：同时包含输入（正则字符串）与输出（n）
Case Regex("\d+", n)
```

设计意图：扩展命名模式的"实参"列表，使其除输出（绑定 `Out` 参数）外也能承载**输入**。需要研究：

- 如何区分实参列表中的输入与输出（按位置约定、按关键字、还是按形参的可变性标记？）。
- 输入如何在模式函数中被引用（作为普通函数形参传递）。
- 对现有命名模式语法与"解构语法与工厂语法互为镜像"原则的影响。

## Drawbacks
[drawbacks]: #drawbacks

- 实参列表同时承载输入与输出会削弱"解构语法镜像工厂语法"的对称性，让模式调用变得更像普通函数调用。
- 输入/输出的区分规则若不直观，会引入新的认知负担。

## Alternatives
[alternatives]: #alternatives

- 保持实参列表只输出，输入改由模式函数的普通形参在别处传递（或通过闭包/上下文捕获）。
- 用关键字显式标注输入参数（如 `In`），与既有 `Out` 形成对称。
- 不为命名模式加入输入，需要输入的场景改用传统函数调用。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文仅标注 "This should be investigated"，未给出方向结论。
- 输入与输出在同一实参列表中的区分语法未定。
- 输入如何参与模式相等/编译期分析（例如常量输入能否特判），未定。
