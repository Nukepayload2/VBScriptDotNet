# XML Schema Types / XML Schema 类型

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

XML Schema 类型允许用 `<geo:Address>` 这样的形式声明变量类型，让编译器把 XSD/XML Schema 信息映射为类型系统的一部分，从而重新启用 XML 的 IntelliSense：对 `address.<Country>.Value` 等成员访问进行静态检查与提示。

## Motivation
[motivation]: #motivation

VB 的 XML 字面量与 XML 轴属性（`.<Country>`）提供了便捷的 XML 访问，但默认情况下成员访问是动态的，没有针对具体 XML 结构的类型信息，也就没有 IntelliSense 与编译期校验。引入 XML Schema 类型后，开发者声明"这个变量是 `<geo:Address>`"，编译器即可依据 Schema 推导该 XML 允许的元素与属性，恢复类似强类型对象那样的代码提示。

## Detailed design
[design]: #detailed-design

用 `<命名空间:类型名>` 的形式作为类型标注。原文示例（第 4 章）：

```vb
' XML Schema "types" to re-enable XML IntelliSense.
Let address As <geo:Address> = ...
? address.<Country>.Value
```

要点：

- `Let address As <geo:Address> = ...`：声明变量 `address`，其类型是 XML Schema 类型 `<geo:Address>`。
- `? address.<Country>.Value`：XML 轴属性 `.<Country>` 依据 Schema 得到类型信息，`.Value` 可取回该元素的值；此时 IntelliSense 应列出 `<geo:Address>` 下允许的子元素。
- 类型标注中的 `geo:` 命名空间前缀应当可以解析到某个 XSD 定义。

## Drawbacks
[drawbacks]: #drawbacks

- 需要把 XML Schema（XSD）作为编译期输入接入类型系统，工作量与复杂度高。
- Schema 允许可选元素、递归、任意内容（`xs:any`）、通配符等，静态类型化可能覆盖不全，退回动态行为。
- 与现有 `XDocument`/`XmlDocument` 动态 API 的兼容关系需要明确定义。

## Alternatives
[alternatives]: #alternatives

- 保持现状：继续使用无类型 XML 字面量，通过分析器插件对 `.Value` 用法做启发式提示。
- 仅做分析器/工具层支持：在 IntelliSense 中根据变量初始化的 XML 结构推断成员，而不引入新的"类型"语法。
- 用代码生成把 XSD 转成强类型包装类，而非语言级类型。

## Unresolved questions
[unresolved]: #unresolved-questions

- Schema 类型如何与变量生命周期结合：赋值、集合、函数参数/返回类型、泛型中是否都允许 `<geo:Address>` 形式？
- 命名空间前缀 `geo:` 到 XSD 的解析规则（引用方式、项目级还是内联）未在原文展示。
- Schema 中可选/重复元素在 IntelliSense 中的表示方式未明确。
- 原文只给出声明与访问两行示例，其余（如对元素赋值的语义）待设计。
