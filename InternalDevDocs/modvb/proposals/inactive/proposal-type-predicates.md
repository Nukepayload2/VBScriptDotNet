# 类型谓词（流敏感类型）/ Type Predicates in Flow-Sensitive Typing

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 在写类型推断章节时想到：把"流敏感类型"进一步泛化——如果能够给系统提供一个**命名类型**，它代表"某个谓词返回 true"，那么编译器就会在对应执行路径上跟踪形如 `{Integer, Not Negative}` 的复合类型，或允许写 `count As {Integer, Positive}` 这样的声明。作者明确表示尚未深入思考，仅作为"某个时候值得探索"的方向。

## Motivation
[motivation]: #motivation

- 现有 `TypeOf ... Is` 流分析：检查类型为 `IEnumerable` 的变量的值是否为 `IDisposable`，会让该执行路径上的变量类型变成复合（交集）类型 `{IEnumerable, IDisposable}`。
- nullness 也可以这样处理：变量的类型可以是 `{String, Not Null}`。
- 把上述机制再推进一步，任何"对值成立与否"的谓词都可以成为类型的一部分。

## Detailed design
[design]: #detailed-design

原文给出的说明性伪代码——注意：**原文明确标注 "Pseudo-code, not proposed syntax"，即这不是建议语法**：

```vb
' Pseudo-code, not proposed syntax.
Interface Negative
    Shared Function IsNegative(x As Integer) As Boolean
        Return x < 0
    End Function
End Interface

Interface Positive
    Inherits NonNegative
    
    Shared Function IsPositive(x As Integer) As Boolean
        Return x > 0
    End Function    
End Interface
```

含义：如果我能给所设计的系统一个命名类型，它代表"某个函数返回 true"，那么系统就会开始把这样的信息传导下去，例如变量类型成为 `{Integer, Not Negative}`，或者允许形如 `count As {Integer, Positive}` 的声明。这已经足够让它成为一个"某个时候值得探索"的有趣方向，但作者还没有深入思考。

原文还顺带提及一段未展开的话："跳过一段长故事 …… effect system …… TypeScript …… 面向未来？（future-proof?）……"——说明该想法与效果系统（effect system）、TypeScript 的类型谓词（type predicates）有隐约关联，但没有进一步阐述。

## Drawbacks
[drawbacks]: #drawbacks

- 流敏感类型本身已经很复杂，叠加"任意谓词即类型"会让类型推断、重载解析、可空性交互全面变复杂。
- 示例中 `Positive` 继承未定义的 `NonNegative`，暴露了该想法的粗糙状态（仅用于说明概念）。
- 谓词型接口与普通接口语义混淆：接口通常描述结构契约，而非"值满足某个函数"。

## Alternatives
[alternatives]: #alternatives

- 只依赖现有的 `TypeOf ... Is` / nullness 流分析，不为任意谓词引入命名类型。
- 借助"即席交集类型"建议（`{A, B}`）表达组合，而不扩展"谓词型命名接口"。
- 把谓词表示交给一等函数 / 委托类型，而非接口机制。

## Unresolved questions
[unresolved]: #unresolved-questions

- 如何把"命名类型"与"谓词函数"对应起来（接口上约定方法？），原文未说明。
- `{Integer, Not Negative}` 与 `count As {Integer, Positive}` 的精确语义与语法（原文标注为伪代码，非建议语法）。
- 与效果系统（effect system）、TypeScript 类型谓词的关联具体是什么。
- 示例中的 `NonNegative` 未定义，是否为 `Negative` 的反向补充，未澄清。
- 这类类型如何参与可空性、泛型约束与重载解析。
