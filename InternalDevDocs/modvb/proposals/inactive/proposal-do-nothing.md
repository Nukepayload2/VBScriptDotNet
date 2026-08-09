# Do Nothing 语句 / Do Nothing Statement

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入 `Do Nothing` 空操作语句，作为显式的"什么都不做"占位，用于需要语句位置但暂无实现或意图留白的场合（例如特性构造器占位）。

## Motivation
[motivation]: #motivation

许多语法位置要求"至少一条语句"，但实现时可能暂时留白：特性（Attribute）构造器、空分支、调试期间的占位等。现有做法要么写无意义的 `Console.WriteLine`，要么用 `If True Then End If` 之类的拐弯写法。`Do Nothing` 提供直白的"空操作"表达。原文注释引用了一篇题为 "This feature intentionally left blank" 的链接文章，暗示该语句的正当性。

## Detailed design
[design]: #detailed-design

```vb
' `Do Nothing` statement (not a joke).
' https://anthonydgreen.net/2026/04/01/this-feature-intentionally-left-blank/
Do Nothing
```

`Do Nothing` 是一个空操作语句：执行它不做任何事，用于在语法上需要语句、但语义上无需任何动作的位置（如特性构造器占位）。原文注释 "not a joke" 与链接文章表明作者是认真的设计，而非玩笑。

## Drawbacks
[drawbacks]: #drawbacks

- 提供"空语句"可能诱使开发者用无意义占位代替真正实现，掩盖缺失的逻辑。
- 与 VB 现有结构（`Exit Sub`、过程体本身可为空）相比，`Do Nothing` 的额外价值有限。
- 与 `If`/`Select` 等分支中的"空分支"语义可能冲突。

## Alternatives
[alternatives]: #alternatives

- 允许空语句体（允许空块），不新增关键字。
- 使用注释（`' TODO`）作为占位，不引入运行时语义。
- 复用现有 `Continue`/`Exit` 或其它 no-op 形式表达留白。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文引用的链接文章内容未在摘录中体现，"intentionally left blank" 的完整动机与适用场景待补充。
- `Do Nothing` 是否允许在任意语句位置使用，还是仅限特定上下文（如特性构造器），未明确。
- 是否应要求空分支必须显式写 `Do Nothing`（即禁止空块），未定。
