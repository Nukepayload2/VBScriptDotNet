# 更健壮的映射 / More Robust Mapping

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。为对象成员到 JSON（或同类映射）的"自然连接"（natural join）语法考虑两个问题：一是如何用一个 `null` 键显式表示"某个成员是刻意不映射的"，用于压制"忘记序列化某属性"的警告；二是如何用 `ShapeOf ... IsNot { ... }` 反序列化校验来验证 JSON 映射是否完整、缺少字段时抛异常。

## Motivation
[motivation]: #motivation

作者（18.7）先提出两个问题：

> Should there be some "natural join" syntax to map the members of one object to another?
> Should there be a way to indicate that an unmapped member is intentional?

即：是否应有某种"自然连接"语法把一个对象的成员映射到另一个对象？是否应有办法标明"某个未映射的成员是有意的"？后者针对的是编译器可能发出的"你忘了序列化某个属性"的警告——警告不该被整个关闭，而应能被定向地静音。

## Detailed design
[design]: #detailed-design

### 序列化：用 `null` 键显式表示"有意不映射"

```vb
' If there's a warning that you forgot to serialize a property,
' how do you silence it without turning off the warning entirely?
jsonWriter &= {
                "firstName": person.Name.Given,
                "lastName" : person.Name.Surname,
                null       : person.EmailAddress
              }
```

- `jsonWriter &= { ... }`：以对象字面量形式向 writer 追加序列化内容。
- `null : person.EmailAddress`：`null` 作为键表示"把 `person.EmailAddress` 显式排除在映射之外"，用于声明"我不打算序列化这个成员"——这样编译器既不会报警告，也不必把整个警告关闭。

### 反序列化：用 `ShapeOf` 校验映射完整

```vb
' Same question with deserializing.
' Pattern matching into existing variables/properties is
' still an open question, though.
Let address = New Address
If ShapeOf json IsNot {
     "line1": address.Street
     "line2": null,
     "city" : address.City
     "state": address.State
   }
Then
    Throw New DeserializationFailureException()
End If

Return address
```

- `ShapeOf json IsNot { ... }`：对 JSON 做形状（形状模式匹配）检查，把各字段匹配进已存在的变量/属性（如 `"line1": address.Street`），任一段不匹配则整体为 `IsNot` 成立，于是抛出 `DeserializationFailureException`。
- `"line2": null`：同样用 `null` 表示该字段"有意不反序列化到任何成员"。
- 该示例依赖"模式匹配进已有变量/属性"（pattern matching into existing variables/properties），作者注明这一点本身仍是开放问题。

## Drawbacks
[drawbacks]: #drawbacks

- `null` 键同时承担"字面值空"与"有意不映射"两种含义，易混淆。
- 依赖 `ShapeOf` 模式匹配、对象字面量、`jsonWriter &=` 等多项尚未定稿的特性相互叠加，地基不稳。
- "映射进已有变量/属性"的机制本身是开放问题（作者原注），校验示例的实现前提并不牢靠。

## Alternatives
[alternatives]: #alternatives

- 用显式忽略指令/特性（如逐条声明 `Ignore`）替代 `null` 键，不把"不映射"塞进 JSON 字面量语法。
- 不引入自然连接语法，继续手工逐字段序列化/反序列化，警告用更细粒度的方式处理。
- 在对象字面量/`ShapeOf` 之外另设专门的映射与校验构造，避免混合多种未定稿特性。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文两个问题："是否应有自然连接语法映射对象成员？"、"是否应有办法标明未映射成员是有意的？"均无结论，未定。
- 原文注明："Pattern matching into existing variables/properties is still an open question, though."（模式匹配进已有变量/属性仍然是开放问题）——反序列化校验示例依赖此能力，其本身未定。
- `null` 键与真正的 JSON `null` 值、空值语义在对象字面量中的区分，未在原文详述。
