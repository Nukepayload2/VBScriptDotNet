# Pipeline Operator / 管道运算符 `->`

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

管道运算符 `->` 把"嵌套函数调用"改写为从左到右的数据流：`First(x) -> Second() -> Third()` 等价于 `Third(Second(First(x)))`。前一个表达式的返回值自动成为下一个调用的接收者/参数，让调用链按执行顺序自然书写。

## Motivation
[motivation]: #motivation

多层嵌套调用 `Third(Second(First(x)))` 读起来要从最内层往外读，难以把握数据流动方向，也容易配对括号出错。管道写法让数据沿 `->` 从左到右流动，符合"先发生在前、后发生在后"的直觉，链条越长收益越明显；也更便于中途加/删处理步骤。

## Detailed design
[design]: #detailed-design

管道运算符 `->` 取代嵌套调用。原文示例（第 8 章）：

```vb
' Pipeline `->` operator.
' Instead of:
Return Third(Second(First(x)))
' Write this:
Return First(x) -> Second() -> Third()
```

- `First(x) -> Second() -> Third()`：`First(x)` 的结果传入 `Second`，`Second` 的结果传入 `Third`。
- 语义等价于嵌套写法 `Third(Second(First(x)))`，但顺序从左到右，链上每一段的输出直接成为下一段的输入。
- `Second()`、`Third()` 在管道中作为接收者使用，接收前一段的输出作为其参数（此处省略了显式实参，由管道自动传递）。

## Drawbacks
[drawbacks]: #drawbacks

- `->` 与现有运算符集（如 `>`、`>=`、字典 `!`、Lambda `=>` 类符号）视觉接近，可能被误读。
- 管道中 `Second()` 的"接收者即实参"规则需要明确：省略实参的调用如何拿到前段结果，含多参数时的传递方式。
- 管道与 LINQ 查询、链式方法调用（`.` 链）并存时，风格选择增加，团队内易分歧。

## Alternatives
[alternatives]: #alternatives

- 保持嵌套调用写法：`Return Third(Second(First(x)))`，无需新语法。
- 用中间变量逐步赋值展开，牺牲简洁换取显式。
- 借鉴现有语言：把管道作为通用运算符实现（F#/Elm 式 `|>`），或仅作为方法链的语法糖（仅支持 `-> Member(...)`）。

## Unresolved questions
[unresolved]: #unresolved-questions

- 管道中接收调用的参数传递规则：`First(x) -> Second()` 是把结果作为 `Second` 的第一个实参，还是仅在 `Second` 无参时成立？多实参如何表达？原文示例未展示 `Second()` 的参数形态。
- 管道结果是否总是 `Return` 的表达式，还是可出现在任意表达式位置（如赋值、实参）。
- 与第 8 章其他增强（如 `Return` 命名 `Out` 赋值）组合时的优先级与结合方向。
