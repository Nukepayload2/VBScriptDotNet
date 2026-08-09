# XAML Literals / XAML 字面量

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

XAML 字面量允许在 VB 源码中直接内联一段 XAML 声明，以 `<?xaml?>` 作为起始标记。编译器把该段声明解析为结构化的 XAML 元素树，用于创建 UI 对象（按钮、控件、页面等），从而把"声明式 UI 描述"与承载它的 VB 代码放在同一处。

## Motivation
[motivation]: #motivation

当前在 VB 中创建 UI（例如 WPF / Xamarin.Forms 控件）需要：

- 把 XAML 放到独立的 `.xaml` 文件，通过 `InitializeComponent` 在运行时加载；
- 或在代码中以命令式方式逐个创建控件、设置属性、订阅事件。

两种做法都割裂了"声明式 UI"与"承载它的代码"之间的对应关系，样板代码多、可读性差。XAML 字面量让 XAML 成为 VB 中的一等表达式，属性绑定与事件处理器可以直接引用 VB 成员（如方法名 `OnButtonClicked`），获得编译期校验的潜力。

## Detailed design
[design]: #detailed-design

使用 `<?xaml?>` 处理指令风格的标记开始一段 XAML 字面量。原文示例（第 4 章）：

```vb
' XAML Literals.
Let button = <?xaml?>
             <Button Content="{Binding Command.Name}"
                     Clicked="OnButtonClicked"/>
```

设计要点：

- `<?xaml?>` 是字面量的起始标记，其后是符合 XML/XAML 语法的元素声明。
- 字面量整体是一个表达式，可用于 `Let` 声明、赋值、实参等位置。
- `Content` 属性使用 `{Binding Command.Name}` 标记扩展（Markup Extension）绑定到命令名；
- `Clicked` 事件属性直接指定 VB 方法名 `OnButtonClicked`，编译器应生成事件订阅。
- 编译器需要把这段 XAML 翻译为目标 UI 框架（WPF、Xamarin.Forms 等）的构造与绑定/事件注册调用。

## Drawbacks
[drawbacks]: #drawbacks

- 编译器需要理解 XAML 语法以及目标框架的语义（命名空间、绑定、事件、资源），编译器的复杂度与维护成本显著上升。
- 与现有"XAML 独立文件 + 代码分离"的工具链（可视化设计器、热重载、XAML 编译器/BAML）存在重叠甚至冲突。
- XAML 的丰富特性（资源字典、样式、数据模板、附加属性、x:Name / x:Key 等）在字面量形态下难以完整表达。

## Alternatives
[alternatives]: #alternatives

- 保持现状：继续使用独立 `.xaml` 文件与 `InitializeComponent`，或纯命令式创建控件。
- 仅通过代码生成器（source generator）把 XAML 文件转换为 VB 代码，而不新增语言语法。
- 复用第 4 章的 XML 字面量机制，把 XAML 当作 XML 字面量的一种特化（类似 `<?vb?>` 的反向嵌入），共享同一套解析基础。

## Unresolved questions
[unresolved]: #unresolved-questions

- 绑定路径（`{Binding ...}`）与事件处理器是编译期解析校验还是运行时解析？
- 是否支持完整 XAML 特性（资源、样式、模板、附加属性、x:Name / x:Key 等）？
- 与现有 XAML 工具链（可视化设计器、热重载、XAML 编译器）如何集成？
- 原文仅给出这一个示例，命名空间导入、多根元素、编译产物形态等细节均未展示，待设计。
