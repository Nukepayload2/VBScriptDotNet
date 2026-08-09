# 独占上界 `To <` / Exclusive For "Upper" Bound

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**实验性 / 未定稿**。为范围表达式引入独占上界写法 `x To < y`，表示区间 `[x, y)`，从而在遍历数组等场景中免去 `arr.Length - 1` 这类减一写法。该语法对接受独占范围的 API 也可能通用有用。

## Motivation
[motivation]: #motivation

作者（18.4）表示对 `arr.Length - 1` 已经有些厌倦。在 QB/VB6 时代因为有 `UBound` 函数（只对数组有效）而不觉得麻烦；但其他一些 API 接受的是"独占范围"（exclusive ranges），因此这种 `x To < y` 语法可能是普遍有用的，不只是 `For` 循环。

## Detailed design
[design]: #detailed-design

原文示例：

```vb
For i = 0 To < arr.Length
    ...
Next

Console.WriteLine(String.Join(", ", 0 To < 10)
```

- `For i = 0 To < arr.Length`：循环上界为"小于 `arr.Length`"，即 `i` 从 `0` 到 `arr.Length - 1`，可直接遍历数组下标，不再写 `arr.Length - 1`。
- `String.Join(", ", 0 To < 10)`：范围表达式 `0 To < 10` 表示 `0` 到 `9`，可被用作序列（如传入 `String.Join`）。
- 独占上界以 `<` 前缀表达（`To < y`），语义为左闭右开区间 `[x, y)`。

## Drawbacks
[drawbacks]: #drawbacks

- `To <` 中 `<` 与前缀、比较运算符易混淆，解析上需要特别注意（`To` 后紧跟 `<` 时如何与"小于"表达式区分）。
- 独占/闭区间两种上界并存，写循环的人仍可能搞混到底是 `To arr.Length` 还是 `To < arr.Length`，引入新的出错点。
- 只解决"上界减一"这一表象问题，`UBound` 式的既有习惯仍会与之并存。

## Alternatives
[alternatives]: #alternatives

- 维持 `arr.Length - 1` 或提供一个数组专用的 `UBound` 等价物（但 `UBound` 只对数组有效，不通用）。
- 让范围表达式整体统一为半开区间语义，或在 `For` 之外提供独立的范围类型/运算符，把"独占"能力下沉到范围表达式中。
- 不改语法，仅靠库 API 显式传递独占上界。

## Unresolved questions
[unresolved]: #unresolved-questions

- `To <` 写法的解析歧义（`<` 与比较运算符如何区分）未定。
- 独占上界是仅用于 `For`，还是作为通用范围表达式特性（如 `String.Join(", ", 0 To < 10)` 所示）推广到所有范围场景，未定。
- 与既有的 `Step`、范围步进、及 `1 To 10`（第 42 项范围表达式建议）如何组合，未在原文说明。
