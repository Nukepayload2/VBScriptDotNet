# Method-Level Imports / 方法级 `Imports`

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

方法级 `Imports` 允许把 `Imports` 语句放在方法体内，将**命名空间、`Shared` 类型成员以及 XML/JSON 命名空间**的引入范围缩小到当前方法，从而在不污染文件级作用域的前提下按需简化方法内部的代码。

## Motivation
[motivation]: #motivation

文件顶部的 `Imports` 作用域是整个文件，会带来命名冲突与 IntelliSense 噪音；有些别名或命名空间只在某个方法里用到，放文件级属于"过度暴露"。方法级 `Imports` 把引入范围收窄到方法内，方法内可直接以简名使用（如直接调 `Clear()`、`WriteLine(...)` 而不是 `Console.Clear()`、`Console.WriteLine(...)`），文件其余部分不受影响。

## Detailed design
[design]: #detailed-design

在 `Sub` 内部使用 `Imports`。原文示例（第 8 章）：

```vb
' Namespaces, Shared type members, and XML/JSON namespaces can
' be imported at the method level.
Sub WriteHelp()
    Imports System.Console
    Imports <xmlns:xs="http://www.w3.org/2001/XMLSchema">

    Clear()    
    WriteLine("...")
    WriteLine("...")

End Sub
```

- `Imports System.Console`：引入命名空间 `System.Console`（其成员为 `Shared`）——引入作用域仅限 `WriteHelp` 方法。
- `Imports <xmlns:xs="http://www.w3.org/2001/XMLSchema">`：以 XML 命名空间声明语法（`<xmlns:xs="...">`）引入一个 XML/JSON 命名空间前缀，供方法内的 XML 字面量使用。
- 之后 `Clear()`、`WriteLine("...")` 直接以无前缀简名调用，方法内等价于 `Console.Clear()`、`Console.WriteLine(...)`；文件其余部分仍按原规则解析。

## Drawbacks
[drawbacks]: #drawbacks

- 同一名字在文件级与方法级被不同 `Imports` 影响，作用域规则更复杂，代码审阅者需留意"这个简名来自哪一级"。
- 方法内频繁的 `Imports` 可能使方法头变得冗长，反而降低可读性。
- 编译器需要在每个方法作用域维护独立的导入集合，绑定与错误报告的成本上升。

## Alternatives
[alternatives]: #alternatives

- 保持现状：全部 `Imports` 置于文件顶部；代价是作用域过宽、别名冲突。
- 用 `With ... End With` 或别名 `Alias` 模拟方法内简名，但无法覆盖 XML 命名空间等场景。
- 引入块作用域 `Imports`（如 `Imports ... In <method>`）显式声明范围，而非默认以方法为界。

## Unresolved questions
[unresolved]: #unresolved-questions

- 方法级 `Imports` 是否可放在方法体内任意位置，还是必须位于语句块最前？
- 是否只允许 `Imports`，还是也允许方法级 `Imports Alias` 与 `Imports ... = ...` 别名形式？
- 方法级导入与文件级导入冲突时的优先级与遮蔽规则（方法级覆盖文件级，还是报错）原文未说明。
- XML/JSON 命名空间导入在方法级的具体解析范围（方法内所有 XML 字面量生效）原文仅给出一处示例。
