# 交集 / 并集类型 / Intersection and Union Types

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入**即席交集类型**与**即席并集类型**。交集类型用花括号逗号列表书写 `{IDisposable, ICloneable}`，表示"同时满足两个接口"的对象；并集类型用 `Or` 书写 `{IDisposable Or ICloneable}`，表示"两个类型之一"。两者都作为类型系统增强，无需预先定义命名类型。

## Motivation
[motivation]: #motivation

当某个值必须同时满足多个接口（如既要能释放又要能克隆），或只能满足其中之一时，现有 VB 没有轻量表达方式，只能定义命名接口去合成契约，或退回 `Object` 丢失类型信息。即席交集/并集类型让约束直接写在声明处，既保留了静态类型信息，又避免了为每个组合预定义接口的样板。

期望的结果：`Let i As {IDisposable, ICloneable}` 得到的 `i` 可安全调用两个接口的成员；`Let u As {IDisposable Or ICloneable}` 得到的 `u` 需经模式匹配或类型测试才能使用。

## Detailed design
[design]: #detailed-design

### 即席交集类型

用逗号分隔的花括号列表表示交集：

```vb
' Ad-hoc intersection types.
Let i As {IDisposable, ICloneable} = ...
```

`i` 的类型是 `IDisposable` 与 `ICloneable` 的交集：任何赋值必须同时实现两个接口；对 `i` 可无转型地调用 `i.Dispose()` 与 `i.Clone()`。

### 即席并集类型

用 `Or` 分隔表示并集：

```vb
' Ad-hoc union types.
Let u As {IDisposable Or ICloneable} = ...
```

`u` 的类型是 `IDisposable` 与 `ICloneable` 的并集：赋值可来自任一接口实现；使用前需要通过类型测试（如 `TypeOf`/`If TypeOf u Is IDisposable`）收窄到具体成员。

## Drawbacks
[drawbacks]: #drawbacks

- 交集/并集类型加大类型推断与重载解析的复杂度，编译器需维护复合类型形态。
- 并集类型的成员访问受限（只能访问两者的公共成员），易让初学者困惑。
- 与现有 `Object`、泛型约束、接口继承的相互作用需要大量规范细节。

## Alternatives
[alternatives]: #alternatives

- 只做交集类型（使用场景更明确），并集留给模式匹配与 `Any` 伪类型。
- 仅靠定义命名接口或使用泛型约束表达组合，不引入即席类型。
- 把"交集"表达为泛型 `Of T As {IDisposable, ICloneable}` 约束的隐式合成，不新增声明语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- 并集类型的收窄方式（`TypeOf`/`Select Case`/模式匹配）是否全部适用。
- 交集/并集是否可以包含类类型或泛型实例，而不限于接口。
- 两个类型在编译器内部如何表示（合成类型符号、还是折叠为公共成员集）。
- 交集/并集类型的相等性与转换规则（`{A, B}` 与 `{B, A}` 是否等价）。
