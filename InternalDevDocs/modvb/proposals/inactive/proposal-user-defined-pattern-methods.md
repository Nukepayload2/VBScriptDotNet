# User-Defined Pattern Methods / 用户定义模式方法

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

用户定义模式方法允许把任意一个 **带 `Out` 参数且返回 `Boolean`** 的函数当作模式使用：`If ShapeOf stream Is hasAnotherChar(c) Then`。模式函数通过 `Out` 参数向调用点输出匹配到的子值，并以布尔返回值表达匹配成功与否，从而让模式匹配不再局限于内建类型模式。

## Motivation
[motivation]: #motivation

内建的类型模式只能判断"是不是某类型"。真实世界里的匹配往往需要带条件：例如"流中是否还有下一个字符，并且把它取出来"。若只能靠内建模式，程序员得先判类型、再手动调用方法、再判断结果，逻辑被拆散在多个语句里。用户定义模式方法把"匹配判定 + 结果提取"封装进一个普通函数，让 `If`/`Select Case` 条件一次完成判定与取值，可复用、可组合。

## Detailed design
[design]: #detailed-design

一个模式方法就是一个普通函数：以 `Out` 参数承载匹配结果，返回 `Boolean` 表达是否命中。原文示例（第 7 章）：

```vb
' User-defined pattern methods.
Let hasAnotherChar =
      Function(reader As TextStream, Out ch As Char) As Boolean
          If reader.EndOfFile() Then
              Return False, ch:=Nothing
          Else
              Return True, ch:=reader.PeekChar(0)
          End If
      End Function

If ShapeOf stream Is hasAnotherChar(c) AndAlso c <> Nothing Then
    buffer.Append(c)
    stream.EatNextChar()
End If
```

- `Let hasAnotherChar = Function(reader As TextStream, Out ch As Char) As Boolean ...`：模式函数有两个要素——`Out ch As Char` 参数（提取结果）与 `As Boolean` 返回值（命中与否）。
- 函数体内用 `Return False, ch:=Nothing` / `Return True, ch:=reader.PeekChar(0)` 同时返回判定与提取值（`Return` 的 `Out` 赋值语法见第 8 章 `Out` 实参/形参建议）。
- 调用点 `If ShapeOf stream Is hasAnotherChar(c) Then`：把 `stream` 交给模式函数判定。若函数返回 `True`，则变量 `c` 被隐式声明并填入 `Out` 参数的值；`AndAlso c <> Nothing` 继续参与条件。
- 分支体内 `buffer.Append(c)`、`stream.EatNextChar()` 直接使用匹配到的 `c`。

匹配成功时 `c` 由编译器隐式声明并赋值；匹配失败时 `c` 不参与后续逻辑，避免空引用警告。

## Drawbacks
[drawbacks]: #drawbacks

- 模式函数 `As Boolean` + `Out` 的组合约束较宽松，编译器难以静态证明其"只读输入、只在成功时写输出"的性质，可能允许写出有副作用的模式。
- `ShapeOf x Is SomePattern(args)` 与普通函数调用的区分需要靠上下文判定，存在一定解析复杂度。
- 多个模式方法的 `Out` 变量绑定规则（失败时是否赋值、类型推断）需要明确，否则易与 `ByRef` 语义混淆。

## Alternatives
[alternatives]: #alternatives

- 不引入模式方法，仅靠内建类型模式与 `If/ElseIf` 手动编写判定代码。
- 采用专用 `Pattern` 关键字或特性标记模式方法，让编译器严格校验其签名，而非"返回 `Boolean` 即可"。
- 让模式方法只允许带一个 `Out` 参数，简化绑定规则；多值匹配由命名模式（解构语法）建议承担。

## Unresolved questions
[unresolved]: #unresolved-questions

- 模式方法能否通过 `Imports`（如 `Imports SyntaxPatterns`，见命名模式建议）批量引入到作用域，还是需要显式别名？
- 匹配失败时 `Out` 变量是否总是被赋 `Nothing`/默认值（本示例中 `Return False, ch:=Nothing` 是显式写法），编译器是否保证该行为？
- 模式方法之间能否组合（如"且/或"连接）、是否支持重载与泛型参数，原文未展示。
