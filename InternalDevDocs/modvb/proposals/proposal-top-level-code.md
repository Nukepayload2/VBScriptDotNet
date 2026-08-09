# Immersive Files ("Top-Level Code") / 顶级代码（沉浸式文件）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

允许在模块（`Module`）与类（`Class`）之外直接编写可执行语句与成员声明，整个源文件即构成一个完整程序。Anthony 称这类文件为"沉浸式文件（Immersive Files）"，即通俗意义上的"顶级代码（Top-Level Code）"。编译器隐式地把文件中的代码放入某个宿主容器，使用者无需先理解 `Module`/`Class`/`Main` 等样板概念。

## Motivation
[motivation]: #motivation

- 大幅降低初学者的入门门槛：第一个程序从"类 + `Sub Main` + `End Sub` + `End Class`"的样板缩成一行 `Console.WriteLine`。
- 适用于脚本、小工具、演示程序等一次性代码，写起来像 QBasic 时代的体验。
- 与网页（`.vbxhtml`）、WinForms 事件处理器等场景结合，让"整个文件就是一个程序/页面"成为可能。

## Detailed design
[design]: #detailed-design

文件最外层不再强制要求 `Module` 或 `Class`，可直接书写：

- 可执行语句（调用、赋值、循环等）；
- `Imports` 语句；
- 成员声明（`Function`、`Sub` 等）；
- 顶层变量声明（此处使用 Anthony 提出的新声明关键字 `Let` 代替 `Dim`）。

**FirstProgram.vb** —— 整个程序只有一行：

```vb
Console.WriteLine("Hello, World!")
```

**SecondProgram.vb** —— 混合 `Imports`、函数与顶层语句，构成完整程序：

```vb
' This is an entire program.
Imports System.Console

Function Prompt(message As String) As String
    WriteLine(message)
    Return ReadLine()
End Function

Let name = Prompt("What is your name?")

WriteLine($"Hello, {name}.")
```

**Starfield.vb** —— 顶层语句直接使用循环：

```vb
buffer.Clear(Color.Black)

' Draw 10 yellow 5x5 squares randomly across a 600x400 screen.
For i = 1 To 10
    buffer.FillRectangle(Brushes.Yellow, Rnd() * 600, Rnd() * 400, 5, 5)
Next
```

**Index.vbxhtml** —— 沉浸式网页文件。`.vbxhtml` 是 VB 与 HTML 混合的源文件，HTML 中通过 `<{ ... }>` 嵌入 VB 表达式：

```xml
<?xml version="1.0" encoding="UTF-8"?>
<html>
  <head>
    <title>Welcome to my site!</title>
  </head>
  <body>
    <h1><{ ViewData!Message }></h1>
    <h2>This is a header.</h2>
    <p>Today is <{ Date.Today.ToString("MMM d, yyyy") }>.</p>
    
    <a href="/">Home</a>
    <a href="/home/about">About</a>
    <a href="/home/contact">Contact</a>
  </body>
</html>
```

**Form1.vb** —— 沉浸式文件同样支持 WinForms 场景，顶层直接声明带 `Handles` 的事件处理器（不再需要 `Class Form1` 外壳）：

```vb
Sub Button1_Click() Handles Button1.Click
    MsgBox("Hi")
End Sub

Sub Button2_Click() Handles Button2.Click
    MsgBox("Bye")
End Sub
```

原文还列出了一批早期原型演示（"Past Demos"）的视频链接，包括基础语法、笔记本（Notebooks）、QBasic 式游戏循环、网页、Web 控件/组件、Web API、XAML/Xamarin.Forms，说明该方向此前已有原型验证，但未附代码。

## Drawbacks
[drawbacks]: #drawbacks

- 顶层成员会被编译器隐式放置进某个容器（模块/类），其可见性、命名空间归属、与显式声明的容器的交互规则都需要明确定义，否则会产生混淆。
- 多个沉浸式文件之间如何互相引用、是否有隐式全局状态，需要设计一致的作用域模型。
- 现有工程中"文件即 `Module` 或 `Class`"的心智模型会被打破，迁移旧代码到新形态时需要清晰指引。

## Alternatives
[alternatives]: #alternatives

- 不做此功能：保持现状，要求每个程序都必须包裹在 `Module`/`Class` 中，靠模板代码生成 `Sub Main`。这是现状的代价——样板代码始终存在，入门门槛无法降低。
- 编译器隐式生成一个容纳顶层代码的模块（而非类），类似当前 VB 编译器对无 `Main` 项目的处理方式，可作为实现层的默认宿主。

## Unresolved questions
[unresolved]: #unresolved-questions

- 顶层成员被放入什么宿主容器（模块还是类）、命名空间如何推断，原文未说明。
- 多个沉浸式文件之间的引用与可见性规则（是否等价于同一隐式模块）。
- `.vbxhtml` 中 `<{ ... }>` 的嵌入表达式语法属于本建议还是"嵌入 VB 解析模式"等其他建议的范畴，原文未明确切分。
- `Form1.vb` 示例中的事件处理器如何与 WinForms 设计器字段（`WithEvents`/`Handles` 目标）关联。
- 原文标注的 Past Demos 均为早期原型视频，没有代码，其能力边界（如笔记本、游戏循环）未在文档中展开。
