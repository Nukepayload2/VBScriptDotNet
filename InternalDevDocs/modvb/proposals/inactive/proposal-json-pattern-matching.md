# JSON Pattern Matching / JSON 模式匹配

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

JSON 模式匹配让 `ShapeOf expr Is { ... }` 可以用 JSON 形状描述匹配条件：被匹配对象需满足给定的字段与取值，例如 `ShapeOf newEmployee Is { "firstName": "Jack", ... }`。此外可以用 `{"http://.../schema"}` 形式声明 JSON Schema 类型，并配合 `!` 操作符访问其成员。

## Motivation
[motivation]: #motivation

断言接口返回值、校验数据结构时，用 JSON 形状直接描述期望，比逐一断言字段更直观。配合 JSON Schema，可以给匿名/动态数据附加类型信息，使 `employee!emergencyContact` 这类成员访问获得 IntelliSense 与编译期分析支持。

## Detailed design
[design]: #detailed-design

JSON 形状模式。原文示例（第 5 章）：

```vb
Let newEmployee = Await service.AddEmployeeAsync("Jack", "Sparrow")

' Copied base-line from postman.
Assert.IsTrue(ShapeOf newEmployee Is {
                "firstName": "Jack",
                "lastName": "Sparrow",
                "emailAddress": "jack.sparrow@company.com"
              })
```

- `ShapeOf newEmployee Is { ... }`：用 JSON 对象字面量作为模式，逐个匹配字段名与字段值（此处与 `AddEmployeeAsync` 返回的对象内容比对）。

JSON Schema 类型与 `!` 成员访问。原文示例：

```vb
' JSON schema types for IntelliSense/analyzers.
Let employee As {"http://.../employee"} = ...
? employee!emergencyContact
```

- `{"http://.../employee"}`：以 Schema 的 URI 作为类型标注，为 `employee` 附加 JSON Schema 描述。
- `employee!emergencyContact`：`!` 操作符按 Schema 访问成员（此处是 `emergencyContact` 字段），供 IntelliSense 与分析器使用。

## Drawbacks
[drawbacks]: #drawbacks

- JSON 模式只按字段名/字面量值匹配，无法表达"任意类型"、"可选字段"等组合语义，表达力有限。
- 需要引入 Schema 拉取/解析机制（网络或本地资源），与编译器的离线编译模型存在张力。
- `!` 访问符与现有 `!` 字典访问的语义需要区分，可能造成混淆。

## Alternatives
[alternatives]: #alternatives

- 保持现状：用 `Assert.Equal` 逐字段断言，或对 `JObject` 手动取值。
- 复用通用的 `ShapeOf` 模式机制（第 7 章），把 JSON 模式定义为其语法糖，而非单独实现。
- 用强类型包装类替代 Schema 类型，把 `!` 变成普通成员访问。

## Unresolved questions
[unresolved]: #unresolved-questions

- JSON 模式中字段值的匹配语义：是否区分大小写？是否支持非字符串值、嵌套对象/数组模式？未在原文展示。
- Schema URI 的解析方式（本地嵌入、包引用、还是编译期远程获取）与缓存策略未定义。
- `employee!emergencyContact` 中 `!` 的完整语义（缺字段时行为、类型推断）未明确。
- 原文未展示 JSON 模式与第 7 章通用 `ShapeOf` 模式之间的层级关系。
