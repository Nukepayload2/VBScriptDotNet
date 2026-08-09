# Partial 成员 / Partial Members

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议把 `Partial` 修饰符的应用范围从类型扩展为**任意类型成员**，且不受访问修饰符限制。一个成员可在多个文件中分部声明，编译器合并所有 partial 元数据——包括特性（attributes）、文档注释、`Handles`、`Implements`、`Overrides` 子句。作为配套简化，`Partial Sub` 不再要求 `End Sub` 结束符。

## Motivation
[motivation]: #motivation

在类型（`Partial Class`）级别之外，开发者常常需要在另一文件中给已有成员附加元数据或子句：为某个方法补特性、补文档注释、补 `Handles` 事件、补 `Implements` 接口、补 `Overrides` 覆写。现在只能改写原成员或绕道代码生成。`Partial` 成员让这些补充可以就近放在生成文件中，减少对原文件的侵入。

期望的结果：代码生成工具（如源码生成器）能在分部文件中为已有成员补充元数据，而无需重写整个成员；手写成员仍保持完整可读。

## Detailed design
[design]: #detailed-design

`Partial` 现在可以应用到任意类型成员，无论其访问修饰符如何。所有 partial 元数据被合并。适用于在另一个文件中为既有成员添加特性、文档注释、`Handles`、`Implements`、`Overrides` 子句：

```vb
' Handwritten.vb
Partial Sub OnModelLoaded()
    ' 主体写在手写文件中。
End Sub
```

```vb
' ToolGenerated.vb
' 生成文件只补充元数据，不重复主体。
Partial Sub OnModelLoaded()
    ' 此处可放特性/注释；若为 Partial 函数也可以在此声明实现？
End Sub
```

### 省略 `End Sub`

原文示例指出，`Partial Sub` 不再要求 `End Sub`：

```vb
' `End Sub` no longer required for `Partial` subs.
Partial Sub OnModelLoaded()
' End Sub
```

对于仅用于附加元数据、不重复实现的分部成员，可以只写声明行而省略结束语句，编译器据此知道该成员没有主体、无需补全。

### 元数据合并

以下元数据可在多个分部声明间合并：

- 特性（`<Attribute>`）；
- 文档注释（XML/`'''` 注释）；
- `Handles` 子句；
- `Implements` 子句；
- `Overrides` 子句。

合并规则与现有 `Partial Class` 的特性合并一致：同一成员的各分部声明可各自携带不同的特性/子句，最终合成一个完整成员。

## Drawbacks
[drawbacks]: #drawbacks

- 成员级分部的语义比类型级 partial 更微妙：谁提供"主体"、谁只提供"元数据"需要明确判定，可能产生两套声明各带主体的歧义。
- 省略 `End Sub` 依赖 `Partial` 前缀做语法判别，误用（漏写 `End Sub` 的普通 Sub）会造成解析歧义。
- 特性、`Overrides` 等在分部声明间重复出现时的合并冲突（如两边都声明了同名 `Implements`）需要额外规则。

## Alternatives
[alternatives]: #alternatives

- 不扩展 `Partial` 到成员，仅靠源码生成器"注入"元数据（内部重写，用户在 IDE 中不可见、不可改）。
- 仅允许补充"元数据类"子句（特性、注释、`Implements`），不允许补充 `Handles`/`Overrides` 等会影响实现查找的子句。
- 保留 `End Sub` 要求，改用空体（`Partial Sub OnModelLoaded()` + 空体）表达"无主体"。

## Unresolved questions
[unresolved]: #unresolved-questions

- 两个分部声明都给出主体的判定规则（编译错误？还是最后一个生效？）。
- `Partial` 属性/事件/字段是否也支持，还是仅限方法（`Sub`/`Function`）。
- 省略 `End Sub` 时，若 `Partial` 拼写/位置错误，解析器如何报错才能避免误导。
- 生成文件中的分部声明是否仍要求声明在与手写文件同名的类型上（与现有 partial 类型规则一致）。
