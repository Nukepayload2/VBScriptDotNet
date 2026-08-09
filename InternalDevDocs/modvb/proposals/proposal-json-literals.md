# JSON Literals / JSON 字面量与目标类型创建

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

JSON 字面量用 `{ ... }` 表示，配合目标类型（target-typed）推断，可以从 JSON 直接创建目标类型的对象，例如用 JSON 初始化内存数据库 `InMemoryDbContext`。JSON 字面量还能与 `&=` 复合赋值配合，在不物化对象的情况下把内容写给 writer（如 `JsonWriter`/序列化器）。

## Motivation
[motivation]: #motivation

初始化测试数据、为服务构造参数、序列化输出等场景中，JSON 是最直观的数据形态。当前 VB 需要手写集合初始化器或调用 API 逐字段填充，样板代码冗长。JSON 字面量让数据以 JSON 形态书写，由编译器根据目标类型完成对象创建；同时提供面向 writer 的流式写法，避免为仅写入场景分配中间对象。

## Detailed design
[design]: #detailed-design

目标类型对象创建。原文示例（第 5 章）：

```vb
' Target-typed object creation from JSON.

' Intialize test data.
Let dbContext As InMemoryDbContext =
    {
      "employees": [
        {"firstName":"John", "lastName":"Doe"},
        {"firstName":"Anna", "lastName":"Smith"},
        {"firstName":"Peter", "lastName":"Jones"}
      ]
    }
    
Let service = New HrService(dbContext)

Let results = Await service.FindEmployeesByNameAsync("John Doe")
    
Assert.Equal(1, results.Count)
```

要点：

- `{ ... }` 是一个 JSON 对象字面量，可以嵌套数组 `[ ... ]` 与对象。
- `Let dbContext As InMemoryDbContext = { ... }`：根据目标类型 `InMemoryDbContext` 把 JSON 转成对象（`"employees"` 数组对应其集合属性）。

面向 writer 的 `&=` 写法。原文示例：

```vb
' Pattern to support &= for writers w/o realizing/allocating
' objects (e.g. JsonWriter/Serialization).
For Each e In Employees
    response &= {
                  "firstName": e.FirstName,
                  "lastName": e.LastName,
                  "emailAddress": e.EmailAddress
                }
Next
```

- `response &= { ... }`：把 JSON 字面量直接写给 writer/序列化目标，而不物化为中间对象，适用于流式输出大量数据的场景。

## Drawbacks
[drawbacks]: #drawbacks

- 目标类型创建需要为每个目标类型定义"JSON 到对象"的映射规则（属性名匹配、大小写、类型转换），边界情况多。
- JSON 字面量的语法与集合初始化器、匿名类型初始化器都以 `{ }` 出现，解析存在歧义风险。
- `&=` 对 writer 的语义与普通字符串 `&=` 差异较大，可能造成阅读负担。

## Alternatives
[alternatives]: #alternatives

- 保持现状：手写对象初始化器，或先用 `JObject`/`JsonNode` 解析再映射。
- 仅提供反序列化 API 糖衣（如 `JsonSerializer.Deserialize`）而不引入 JSON 字面量语法。
- 提供 JSON 字面量但要求显式类型转换（非目标类型推断）。

## Unresolved questions
[unresolved]: #unresolved-questions

- JSON 属性名到目标类型成员的映射规则（大小写不敏感？命名转换？缺失/多余属性如何处理）未在原文说明。
- 目标类型必须为对象类型；JSON 字面量是否也支持创建数组、原语类型的默认值场景未展示。
- `response &= { ... }` 中 `response` 的确切类型（writer 接口、具体序列化器）以及 `&=` 重载机制未定义。
- 原文没有展示 JSON 字面量用于属性值表达式的更多场景（如嵌套 `e.FirstName` 的求值时机）。
