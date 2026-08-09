# `#Ignore Warning` Directive / `#Ignore Warning` 指令

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

新增单行预处理指令 `#Ignore Warning BCxxxxx`，用于关闭紧随其后的一段代码中指定的编译器警告（按错误号）。取代当前"`#Disable Warning` + `#Enable Warning`"两行成对使用的写法，只需一行且更贴近"忽略某处警告"的意图。

## Motivation
[motivation]: #motivation

- 现有的 `#Disable Warning BC42356` 与 `#Enable Warning BC42356` 必须成对出现，把要写的代码夹在中间，样板多、易漏（忘了 `#Enable` 会静默吞掉整段后续代码的警告）。
- 很多场景只是"这一小段我不关心这个警告"（例如刻意不写 `Await`），一行式 `#Ignore Warning` 语义更明确、侵入面更小。

## Detailed design
[design]: #detailed-design

**Before**（现状：两行式）：

```vb
' Before.
#Disable Warning BC42356 ' No `Awaits`; don't care.
Async Function SayHiAsync() As Task
#Enable Warning BC42356

    Console.WriteLine("Hello, World!")

End Function
```

**After**（ModVB：单行式）：

```vb
' After.
#Ignore Warning BC42356 ' No `Awaits`; don't care.
Async Function SayHiAsync() As Task

    Console.WriteLine("Hello, World!")

End Function
```

`#Ignore Warning BC42356` 紧跟注释 `' No `Awaits`; don't care.`，意图即"忽略接下来的代码中 BC42356 警告（此函数无 `Await`，我们不在乎）"。相比两行式，它去掉了 `#Enable` 收尾，覆盖范围由指令自身界定，不易误扩大。

## Drawbacks
[drawbacks]: #drawbacks

- 需明确定义 `#Ignore Warning` 的作用范围（下一行？下一条语句？整个成员？），范围界定模糊会误导使用者。
- 如果被忽略警告所在的代码本身有语法错误，可能因警告被吞而掩盖真实问题。
- 与 `#Disable`/`#Enable` 并存时，两种语义叠加的交互规则需要清晰定义。

## Alternatives
[alternatives]: #alternatives

- 保持 `#Disable`/`#Enable` 两行式：向后兼容，但样板多。
- 使用 `SuppressMessageAttribute`：把警告抑制提升为特性，但会污染方法签名且不能按"文件内某段"精确控制。
- 仅允许 `#Ignore Warning` 用于"当前行或当前语句"，让范围规则最简单。

## Unresolved questions
[unresolved]: #unresolved-questions

- `#Ignore Warning` 的确切作用范围（一行 / 一个语句 / 一个成员）原文未说明。
- 指令是否支持多个错误号（如 `#Ignore Warning BC1234, BC5678`）或通配。
- 与既有 `#Disable Warning`/`#Enable Warning` 的交互与优先级。
