# Embedded VB Parsing Mode / 嵌入 VB 解析模式

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`<?vb?>` 块内的文本按 VB 语法解析，而不是当作普通文本，从而允许在生成 HTML/XML 的模板中直接书写 VB 语句（`If`、`For Each`、插值字符串等），无需 `<%= %>` 之类的转义标记。对用作语句的 XML 表达式，在函数与属性体内提供隐式 `Return`/`Yield`。

## Motivation
[motivation]: #motivation

传统 Web 模板（ASP、经典 ASP.NET Web Forms）需要在 HTML 与代码之间用 `<%= %>`、`<% %>` 等转义序列来回切换解析模式，代码片段支离破碎、难以阅读和重构，也得不到 VB 编译器的检查与执行支持。嵌入 VB 解析模式让开发者编写一个"纯 VB"的模板：HTML 标签只是 XML 字面量的一部分，而其中的文本、控制流都是真正的 VB 代码。

## Detailed design
[design]: #detailed-design

`<?vb?>` 之后的文档整体按 VB 解析。HTML/XML 标签作为 XML 字面量出现，元素文本中的 VB 表达式（如插值字符串）直接求值；`If ... End If`、`For Each ... Next` 等 VB 语句可以直接控制 XML 元素的输出。

原文示例（第 4 章）：

```vb
<?vb?>
<html>
  <head><title>document.Title</title></head>
  <body>
  <h1>$"{document.Title} >> {document.LastUpdated}"</h1>
  
  If includeDisclaimer Then
      <div>
          ...
      </div>
  End If
  
  For Each p In document.Paragraphs
      <p>p.Text</p>
  Next
  </body>
</html>
```

要点：

- `<title>document.Title</title>`：元素文本中的 `document.Title` 是 VB 表达式，求值后输出。
- `<h1>$"{document.Title} >> {document.LastUpdated}"</h1>`：用插值字符串把多个值拼进文本。
- `If ... Then ... End If` 直接决定一段 XML 字面量是否输出。
- `For Each p In document.Paragraphs` 循环逐项生成 `<p>p.Text</p>`。

对"XML 表达式语句"（以语句形式出现的 XML 字面量），在函数与属性体内提供隐式 `Return` 或 `Yield`。原文示例：

```vb
Function GetResponse() As <ResponseMessage>
    <ResponseMessage>
        ...
    </ResponseMessage>
End Function
```

`GetResponse` 的返回类型是 XML Schema 类型 `<ResponseMessage>`，函数体内末尾的 XML 表达式语句隐式作为返回值。

## Drawbacks
[drawbacks]: #drawbacks

- 解析器需要在 VB 语法与 XML 文本之间动态切换，词法与语法处理的复杂度和出错风险上升。
- 隐式 `Return`/`Yield` 语义可能让开发者困惑：哪一条 XML 语句会"成为"返回值，多条时如何处理。
- 模板中隐藏的控制逻辑增多，调试与错误定位更困难。

## Alternatives
[alternatives]: #alternatives

- 维持现状：继续用 `<%= %>` 转义或命令式字符串拼接生成 HTML。
- 把模板编译为可检查、可执行、可翻译为 JavaScript 的完整 VB 代码树（原文以 "Not Shown" 提及）。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文标注 "Not Shown: Full-Fidelity VB Code-Trees to support inspection, execution, and translation of embedded VB code to target DSLs such as JavaScript."——支持检查、执行、以及把嵌入 VB 代码翻译到 JavaScript 等目标 DSL 的完整保真 VB 代码树尚未设计。
- 原文标注 "Not Shown: Implicit self-application inside `InitializeComponent` methods."——`InitializeComponent` 方法内的隐式自应用（self-application）尚未设计。
- 原文标注 "Not Shown: Builder-pattern to support streaming writes of XML documents."——支持 XML 文档流式写入的 Builder 模式尚未设计。
- 隐式 `Return` 与隐式 `Yield` 的精确选择规则（何时采用哪一个）未明确。
