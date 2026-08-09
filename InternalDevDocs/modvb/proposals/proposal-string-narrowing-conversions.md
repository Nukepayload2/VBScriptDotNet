# String Narrowing Conversions / 字符串窄化转换

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

当目标类型具备 `Parse` 方法时，允许把字符串常量直接窄化转换到该类型（如 `Let nil As Guid = "0000..."`）；当目标类型具备 `TryParse` 方法时，允许用 `ShapeOf ... Is ...` 把字符串按该类型匹配并绑定变量；此外允许把字符串窄化转换到枚举类型（如 `Let kind As PhoneKind = "mobile"`）。

## Motivation
[motivation]: #motivation

从字符串得到 `Guid`、`IPEndpoint`、枚举等值是目前非常常见的操作。当前需要显式调用 `Guid.Parse`、`IPEndpoint.TryParse`、`Enum.Parse` 等，样板代码重复且易错。让编译器识别 `Parse`/`TryParse` 方法并自动用于赋值与匹配，能显著减少转换代码，并让"期望的类型"直接写在声明处。

## Detailed design
[design]: #detailed-design

存在 `Parse` 时的窄化转换。原文示例（第 6 章）：

```vb
' Narrowing conversion operator from String if Parse exists.
Let nil As Guid = "00000000-0000-0000-0000-000000000000"
```

- 目标类型 `Guid` 有 `Parse(String)` 方法，因此字符串常量可窄化转换到 `Guid`，等效于调用 `Guid.Parse`。

存在 `TryParse` 时的模式/形状操作符。原文示例：

```vb
' Pattern/Shape operator from String if TryParse exists.
Let str = "20.0.255.255:23"

If ShapeOf str Is ip As IPEndpoint Then
    ? ip.Port
End If
```

- `ShapeOf str Is ip As IPEndpoint`：因 `IPEndpoint` 具备 `TryParse`，用其尝试解析 `str`；成功则把结果绑定到变量 `ip` 并进入 `Then` 分支（此处输出 `ip.Port`），失败则走 `Else` 或继续。

字符串到枚举的窄化转换。原文示例：

```vb
' Narrowing conversion operator from String to Enum types.
Enum PhoneKind
    Home
    Office
    Mobile
End Enum

Let kind As PhoneKind = "mobile"
```

- `Let kind As PhoneKind = "mobile"`：字符串 `"mobile"` 窄化转换为 `PhoneKind` 枚举成员（对枚举名匹配应不区分大小写，才能命中 `Mobile`）。

## Drawbacks
[drawbacks]: #drawbacks

- 隐式窄化转换失败（`Parse` 抛异常、`TryParse` 返回 False、枚举名不匹配）时的行为需要精确定义，否则会掩盖错误。
- 依赖类型上存在 `Parse`/`TryParse` 方法作为"转换操作符"的规则过于宽松，可能与类型自身预期不符。
- 窄化转换与既有 `CType`/`Convert` 语义并存，容易造成混淆。

## Alternatives
[alternatives]: #alternatives

- 保持现状：显式调用 `Guid.Parse`、`IPEndpoint.TryParse`、`Enum.Parse`。
- 仅提供自定义转换操作符（`Widening`/`Narrowing`）机制，由类型作者显式声明，而非编译器自动探测 `Parse`。
- 把 `Parse`/`TryParse` 识别限定到模式（`ShapeOf`）场景，普通赋值不自动窄化。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Parse` 窄化在失败时抛异常还是回退到 `Nothing`/默认值？原文未定义。
- `TryParse` 与 `Parse` 同时存在时优先级如何（模式用 `TryParse`、赋值用 `Parse`？）未明确。
- 枚举窄化是否支持标志组合（`"Home, Office"`）、数字字符串、以及不区分大小写规则？原文仅展示单个成员名。
- 原文未说明这类窄化是否也适用于函数实参、返回值等其他上下文。
