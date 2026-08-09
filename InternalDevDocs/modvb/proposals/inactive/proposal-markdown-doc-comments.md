# Markdown Documentation Comment Syntax / Markdown 文档注释语法

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

让 `'''` 文档注释支持 Markdown 语法：用 `# Parameters`、`# Returns`、`# Exceptions`、`# Remarks`、`# Examples` 等小节标题组织内容，用 `@` 前缀引用参数与成员，用反引号书写代码跨度，用围栏（fenced code blocks）内嵌 VB 代码示例。取代现有冗长的 XML 标签式文档注释。

## Motivation
[motivation]: #motivation

VB 现有的 `'''` 文档注释使用 XML 标签（`<param>`、`<returns>`、`<exception>`、`<remarks>`、`<example>` 等），需要层层嵌套、逐行标注，阅读与书写都显笨重。Markdown 是开发者熟悉且对人类友好的书写格式，同时仍可机器化地映射回文档生成管线。

## Detailed design
[design]: #detailed-design

在 `'''` 注释内：

- 小节标题用 Markdown 的 `#`（如 `# Parameters`、`# Returns`、`# Exceptions`、`# Remarks`、`# Examples`），子标题用 `##`。
- 参数与成员用 `@` 前缀引用：`@m`、`@b` 引用形参，`@Double.PositiveInfinity`、`@ArgumentOutOfRangeException` 引用类型/成员，`@Double.PositiveInfinity` 等以点连接。`@` 引用可出现在列表项中。
- 代码跨度用反引号包裹（如 `` `Func(Of Double, Double)` ``）。
- `# Examples` 下用 `##` 子标题区分不同用例，用围栏块内嵌 VB 代码示例。

完整示例（对斜截式直线 `F` 函数的三段式文档）：

```vb
'''
''' Returns a function from a description of a line in slope-intercept form.
'''
''' # Parameters
''' - @m: The slope of the line. Must not be @Double.PositiveInfinity,
'''       @Double.NegativeInfinity, or @Double.NaN.
''' - @b: The y-intercept of the line. Must not be @Double.PositiveInfinity,
'''       @Double.NegativeInfinity, or @Double.NaN.
'''
''' # Returns
''' An instance of `Func(Of Double, Double)` delegate type which
''' returns the y-coordinate given an x-coordinate.
'''
''' # Exceptions
''' - @ArgumentOutOfRangeException: Either @m or @b is
'''     @Double.PositiveInfinity, @Double.NegativeInfinity, or @Double.NaN.
'''
''' # Remarks
''' A line can be of one of 3 forms:
''' 1. Horizontal,
''' 2. Vertical, or
''' 3. Diagonal.
'''
''' This API only supports forms 1 and 3.
'''
''' # Examples
''' ## Normal usage
''' ``` vb.net
''' Let y = F(3 / 2, -5)
''' Graph(y, 0 To 100)
''' ```
'''
''' ## Horizontal line
''' ``` vb.net
''' Let y = F(0, -5)
''' Graph(y, 0 To 100)
''' ```
Function F(m As Double, b As Double) As Func(Of Double, Double)
    If Double.IsInfinity(m) OrElse Double.IsNaN(m) Then
        Throw New ArgumentOutOfRangeException(NameOf m)
    ElseIf Double.IsInfinity(b) OrElse Double.IsNaN(b) Then
        Throw New ArgumentOutOfRangeException(NameOf b)
    End If
    
    Return Function(x) (m * x) + b
End Function
```

可见 Markdown 文档把"参数约束、返回值、异常、说明、示例"组织成可读的分层结构，且示例用围栏代码块直接展示用法。

## Drawbacks
[drawbacks]: #drawbacks

- 现有工具链与第三方文档生成器（Sandcastle、VS 的 XML 文档、编辑器 IntelliSense）均依赖 XML 注释结构，Markdown 语法需在生成 XML 文档文件时做映射，存在兼容与信息损失风险。
- `@` 前缀在注释内会与普通文本（如邮箱、类型名前的 `@`）竞争，需要消歧约定。
- 围栏块、小节标题的解析会显著增加文档注释解析器的复杂度。

## Alternatives
[alternatives]: #alternatives

- 保持 XML 文档注释：向后兼容、工具链成熟，但书写体验差。
- 对 XML 注释做"轻量增强"（仅新增几个标签），而不引入完整的 Markdown 小节体系。
- 允许两种形态共存（既有 XML 又新增 Markdown 小节），用标记区分，但会增加解析与约定负担。

## Unresolved questions
[unresolved]: #unresolved-questions

- `@` 引用与 XML 中 `<param name="..."/>`/cref 的对应规则（尤其 `@m` 是参数名、`@ArgumentOutOfRangeException` 是异常类型），编译器如何校验其正确性。
- 围栏代码块语言标注 `vb.net` 是否保留、是否映射到其他标注（如 `vb`）。
- Markdown 注释如何导出为可供 IntelliSense 消费的 XML 文档；旧 XML 注释是否需要迁移工具。
- 小节标题（`# Parameters` 等）是否允许自定义标题或仅限固定集合。
