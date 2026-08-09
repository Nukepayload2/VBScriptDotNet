# Wildcard Lambda Expressions / 通配符 Lambda 表达式

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

为单参数"成员访问式"Lambda 提供通配符语法：用 `*.Member` 取代 `Function(x) x.Member`。`*` 表示"那个唯一的 lambda 参数"，编译器自动把 `*.Member` 编译为 `Function(param) param.Member`，支持连续成员访问（如 `*.Profile.Avatar`）。

## Motivation
[motivation]: #motivation

在 EF Core 配置（`Property`/`HasMany`/`WithOne`/`HasForeignKey`）、导航属性加载（`Include`）以及数据绑定等场景中，大量出现"取出参数某个成员"的极简 Lambda。手写 `Function(b) b.Url` 属于纯样板：参数名、lambda 关键字、括号都是噪音，与所表达的含义（"取 `.Url`"）不成比例。

## Detailed design
[design]: #detailed-design

### EF Core 配置

原生 VB（Vanilla VB）写法：

```vb
' Vanilla VB
modelBuilder.Entity(Of Blog) _
            .Property(Function(b) b.Url) _
            .IsRequired()
            
modelBuilder.Entity(Of Blog) _
            .HasMany(Function(e) e.Posts) _
            .WithOne(Function(e) e.Blog) _
            .HasForeignKey(Function(e) e.ContainingBlogId) _
            .IsRequired()
```

ModVB 写法，用 `*.Url` 等取代整套 lambda：

```vb
' ModVB
modelBuilder.Entity(Of Blog) _
            .Property(*.Url) _
            .IsRequired()
            
modelBuilder.Entity(Of Blog) _
            .HasMany(*.Posts) _
            .WithOne(*.Blog) _
            .HasForeignKey(*.ContainingBlogId) _
            .IsRequired()
```

### 嵌套成员访问

`*` 后跟成员访问链，等价于深层投影：

```vb
Let users = context.Users.Include(*.Profile.Avatar).ToList()
```

### 标记属性中的绑定表达式

在标记（XAML 风格）属性中亦可使用，绑定到外层数据的成员：

```xml
<TextBox Text={*.PrimaryContact.FirstName}/>
```

## Drawbacks
[drawbacks]: #drawbacks

- 通配符只能表达"单参数 + 纯成员访问链"；需要参数名参与（如引用两次同一参数）、调用方法、索引器等场景仍须退回完整 lambda。
- `*` 同时是乘法运算符，虽然在"成员访问表达式起始处"与二元乘法语境可消歧，仍需在解析器与编辑器（高亮、重构）中额外处理。
- 过度使用可能降低可读性：读者需要知道 `*` 代表"唯一的参数"这一约定。

## Alternatives
[alternatives]: #alternatives

- 不做此功能：继续手写 `Function(b) b.Url`，样板代码保留。
- 用命名参数代替通配符（如 `it.Url` 或 `$0.Url`），但 `*` 更简短、与"全部/通配"的直觉更贴合。
- 仅在常见 API（`Include`/`Property`）上做语法糖，而非通用的 lambda 通配，可缩小解析器改动面。

## Unresolved questions
[unresolved]: #unresolved-questions

- `*` 除了属性/字段访问外，是否允许方法调用、索引器、带参数的形式（原文示例均为纯成员访问）。
- `*.PrimaryContact.FirstName` 中 `*` 的类型推断来源（目标类型化，还是参数类型需另行给出）。
- `<TextBox Text={*.PrimaryContact.FirstName}/>` 中的 `{...}` 嵌入语法应归属本建议还是"嵌入 VB 解析模式 / XAML 字面量"等其他建议。
- `*` 与乘法运算符在特定上下文（如 `*` 后紧跟 `(`）的解析消歧细节。
