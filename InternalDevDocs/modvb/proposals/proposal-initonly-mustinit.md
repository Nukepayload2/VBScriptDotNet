# `InitOnly` / `MustInit` 属性 / Init-Only and Required Properties

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入两种属性修饰符：**`InitOnly`**——属性只在初始化器/对象构造期间可写，之后只读（对应 C# 的 init-only）；**`MustInit`**——属性为"必填"（required，对应 C# 的 required），对象创建时必须赋值。两者常配合 `With {}` 对象初始化器使用。

## Motivation
[motivation]: #motivation

不可变/初始化一次的对象在 .NET 生态中广泛使用，但 VB 缺少"构造后可读、仅初始化期可写"的属性表达；同时"必填成员"（不填就编译报错）能防止漏初始化。`InitOnly` 与 `MustInit` 分别解决"只读但可在初始化器赋值"与"强制赋值"两个痛点。

期望的结果：

- `InitOnly Property Id As Guid` 只能在对象初始化器或构造函数中赋值，之后读取正常、写入编译报错；
- `MustInit Property Name As String` 表示必填，`New Person With {.Name = "Anthony", .Age = 41}` 缺任一项即报错；
- 配合 `With {}` 完成声明式初始化。

## Detailed design
[design]: #detailed-design

### `InitOnly` 属性

`InitOnly` 属性带显式 `Get`/`Set`，`Set` 中可加校验：

```vb
' `InitOnly` properties.
Class Account
    
    ReadOnly _Id As Guid
    
    InitOnly Property Id As Guid
        Get
            Return _Id

        Set(value)
            If value = Guid.Empty Then Throw
                
            _Id = value
    End Property
End Class

? New Account With {.Id = Guid.NewGuid()}
```

`Id` 被声明为 `InitOnly`：只能在对象初始化器 `With {.Id = Guid.NewGuid()}`（或构造函数）中赋值，`Set` 内校验 `Guid.Empty` 时抛异常；构造完成后对 `Id` 的写入属于编译错误。

### `MustInit` 属性

`MustInit` 表示必填成员，声明时可带默认值：

```vb
' `MustInit` (required in C#) properties.
Class Person
    MustInit Property Name As String = ""
    MustInit Property Age As Integer
End Class

? New Person With {.Name = "Anthony", .Age = 41}
```

`Name` 与 `Age` 均为 `MustInit`：`New Person With {.Name = "Anthony", .Age = 41}` 必须同时提供两者，否则编译错误。Anthony 注明这对应 C# 的 required（C# 侧称 required，本建议沿用 `MustInit` 命名）。

### 分段（piecewise）初始化疑问

```vb
' Piecewise initialation allowed before using other members?
Let p = New Person
p.Name = "Anthony"
p.Age = 41
? p.ToString()
```

Anthony 以问号标注：创建后逐条赋值（`p.Name = ...`、`p.Age = ...`）再使用其他成员，是否被允许作为满足 `MustInit` 的方式，尚未定稿（见 Unresolved questions）。

## Drawbacks
[drawbacks]: #drawbacks

- `InitOnly`/`MustInit` 与现有 `ReadOnly`、构造函数的赋值规则叠加，错误信息与代码路径更复杂。
- `MustInit` 的"必填"依赖对象初始化器/构造检查，反序列化、反射等场景容易绕过。
- 与 `With {}` 初始化器之外的赋值场景（构造函数内、工厂方法内）边界需精细定义。

## Alternatives
[alternatives]: #alternatives

- 只用 `ReadOnly` 字段 + 构造函数参数强制初始化，不引入新修饰符（样板更多）。
- 只做 `InitOnly`（对应 C# init），`MustInit` 用现有 `Option Strict` 之外的运行时校验代替。
- 把"必填"做成分析器诊断而非编译错误，降低语言改动面。

## Unresolved questions
[unresolved]: #unresolved-questions

- 分段初始化：创建后先逐条赋值再使用其他成员，是否满足 `MustInit`（Anthony 原文以 `?` 标注）。
- `InitOnly` 是否可在构造函数内部（非初始化器）赋值，赋值的"初始化期"精确边界。
- `MustInit` 与泛型、继承（派生类是否必须重填）的相互作用。
- `MustInit` 与默认值（`= ""`）同时存在时的语义（有默认值是否仍必填）。
