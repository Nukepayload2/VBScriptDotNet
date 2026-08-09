# 名称解析优化 / Smarter Name Resolution

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议改进**名称解析**：当 `Imports` 的命名空间（如 `Microsoft.Extensions.Logging`）内含与目标类同名（`Console`）的命名空间，导致其遮蔽 `System.Console` 类并造成 BC30456 错误时，编译器应更"智能"地解析，避免需要显式 `Imports System` 才能使用 `Console.WriteLine`。

## Motivation
[motivation]: #motivation

实际项目里常同时 `Imports` 多个命名空间，其中一个命名空间内嵌了与常用类同名的**子命名空间**。例如 `Microsoft.Extensions.Logging.Console` 是命名空间，而 `System.Console` 是类——两者同名 `Console`。现有名称解析按"命名空间优先"的规则把 `Console` 绑定到子命名空间，于是 `Console.WriteLine` 报错 BC30456："'WriteLine' is not a member of 'Microsoft.Extensions.Logging.Console'"。用户被迫显式 `Imports System` 来消除遮蔽，体验差且难以排查。

期望的结果：编译器在无法继续沿命名空间解析成员时，回退考虑同名类（`System.Console`），让 `Console.WriteLine("Hello World!")` 无需额外 `Imports` 即可工作。

## Detailed design
[design]: #detailed-design

```vb
' Fix: Smarter name resolution to avoid `Console` namespace
' hiding `System.Console` class, necessitating an explicit
' `Imports System`
Imports Microsoft.Extensions.Logging

' error BC30456: 'WriteLine' is not a member of 'Microsoft.Extensions.Logging.Console'
Console.WriteLine("Hello World!")
'       ~~~~~~~~~
```

当前行为：`Imports Microsoft.Extensions.Logging` 使 `Console` 首先绑定到命名空间 `Microsoft.Extensions.Logging.Console`（其中没有成员 `WriteLine`），于是产生 BC30456 错误，用户需补 `Imports System` 才能把 `Console` 绑定到类。

建议行为：名称解析在做成员访问（`.WriteLine`）时，若命中的是"仅命名空间"（无成员可继续解析），则回退到其他同名候选中的类型（`System.Console` 类），从而无需显式 `Imports System`。Anthony 原文仅给出问题与方向，未给出具体算法；本建议的落脚点是**更聪明的名称解析**以缓解命名空间遮蔽。

## Drawbacks
[drawbacks]: #drawbacks

- 回退解析可能改变既有代码的绑定结果，出现"过去报错、现在通过"或相反的隐蔽行为变化。
- 多个同名类型候选并存时（如 `System.Console` 与第三方 `Foo.Console` 类），回退顺序需定义，可能仍产生歧义。
- 与 `Imports` 别名（`Imports C = System.Console`）优先级的关系需明确。

## Alternatives
[alternatives]: #alternatives

- 保持现状，用户在遮蔽时自行 `Imports System` 或使用全限定名（`System.Console`）。
- 让 IDE 提供诊断/快速修复（建议补 `Imports System`），不改编译器解析规则——更保守。
- 引入"显式消除歧义"语法（如 `Console.Console.WriteLine`）而非智能回退。

## Unresolved questions
[unresolved]: #unresolved-questions

- 回退规则的确切触发条件（仅当命中的命名空间无法继续解析成员时，还是先按类型优先）。
- 若存在多个同名类型候选，选择规则（按 `Imports` 顺序？按导入深度？）。
- 回退是否也应适用于函数/属性（如 `String`、`Integer` 等类型被同名命名空间遮蔽的情形）。
- 性能与增量编译下名称解析缓存的影响。
