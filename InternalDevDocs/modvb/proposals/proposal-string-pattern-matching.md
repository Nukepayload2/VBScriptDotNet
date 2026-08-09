# String Pattern Matching / 字符串模式匹配（插值逆运算）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

字符串模式匹配是插值字符串的逆运算：用 `ShapeOf` 把字符串与一个插值模式比对，模式中的 `{message}` 等占位符从被匹配字符串中抽取对应片段并绑定到变量。常与 `Select Case` 组合，实现命令解析等按前缀匹配分发的逻辑。

## Motivation
[motivation]: #motivation

解析命令、路由、日志、配置字符串时，当前需要先 `StartsWith`/`Split`/正则表达式再手工拆分字段，样板多且易错。字符串模式匹配让"期望的形状"直接以插值字符串书写，编译器负责比对并抽取变量，代码意图更清晰。

## Detailed design
[design]: #detailed-design

在 `Select Case ShapeOf command` 中，`Case` 使用插值字符串作为模式。原文示例（第 6 章）：

```vb
' String pattern matching (inverse of interpolation).
Select Case ShapeOf command
    Case $"/echo {message}"
        
        ? message
        
    Case $"/beep {frequency As Integer} {duration As Integer}"
        
        Console.Beep(frequency, duration \ 1000)
        
    Case "/clear"
        
        Console.Clear()

    Case Else
        ? "Unrecognized command: " & command

End Select
```

要点：

- `Case $"/echo {message}"`：匹配以 `/echo ` 开头的字符串，并把剩余部分绑定到变量 `message`。
- `Case $"/beep {frequency As Integer} {duration As Integer}"`：占位符可带类型标注 `As Integer`，被抽取的文本片段按该类型转换后再绑定；不匹配（如无法转换）则该分支不命中。
- `Case "/clear"`：普通字符串字面量仍是精确匹配。
- `Case Else` 兜底，处理无法匹配的命令。
- `? message` 即输出 `message`；`duration \ 1000` 为整数除法（把毫秒换算成秒再传给 `Console.Beep`）。

## Drawbacks
[drawbacks]: #drawbacks

- 插值字符串天然有歧义：`{message}` 之后的文本与 `message` 的内容如何切分、贪婪/非贪婪匹配规则需要精确定义。
- 带类型标注的占位符失败（转换失败）时静默跳过分支，可能掩盖错误。
- 与正则表达式相比表达能力有限（无法做字符类、量词、分组等）。

## Alternatives
[alternatives]: #alternatives

- 保持现状：`StartsWith` + `Split` + `Convert`，或直接使用正则表达式。
- 复用第 7 章通用 `ShapeOf` 模式机制，把插值字符串模式定义为命名/自定义模式的一种形态。
- 引入独立的正则字符串模式语法，而非复用插值字符串。

## Unresolved questions
[unresolved]: #unresolved-questions

- 模式中占位符的匹配边界：默认匹配到何处（直到下一个字面量片段、行尾、空格）？原文未定义。
- 带类型标注占位符的转换失败策略（跳过分支 vs. 抛异常）未明确。
- 是否支持 `$` 之外的转义（如字面 `{`/`}`）以及同一占位符多次出现，未在原文展示。
- 原文没有涉及字符串模式的性能（应避免为每条命令做完整回退匹配），待设计。
