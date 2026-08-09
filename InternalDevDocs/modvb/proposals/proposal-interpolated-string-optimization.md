# Interpolated String Optimization / 插值字符串优化与 StringBuilder &=

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

优化插值字符串的求值：多个 `$"..."` 通过 `&` 拼接时，编译器应合并为对 `String.Format` 的**一次**调用；不包含插值的字符串则完全不调用 `String.Format`。同时扩展 `&=`，使其对 `StringBuilder` 也生效（等同追加写入），无论拼接对象是字符串变量还是 `StringBuilder`。

## Motivation
[motivation]: #motivation

插值字符串被编译成 `String.Format` 调用，若一个逻辑输出被拆成多个相邻的 `$"..."` 再用 `&` 拼接，会产生多次格式化与多次分配。日志、异常消息等高频路径中，这种冗余分配会造成可测的性能损耗。让编译器合并相邻插值片段、并让 `StringBuilder &=` 直接追加，能减少中间字符串分配。

## Detailed design
[design]: #detailed-design

相邻插值字符串合并。原文示例（第 6 章）：

```vb
' This should only call String.Format once.
? $"Greetings, {firstName}. " &
  $"The current time is {Date.Now}, " &
  $"on {Date.Today.DayOfWeek}."
```

- 三个通过 `&` 连续拼接的插值字符串应合并为一次 `String.Format` 调用，而非三次。

无插值时跳过格式化。原文示例：

```vb
' This shouldn't call String.Format at all.
Throw New ArgumentNullException(
            description, 
            $"Argument '{NameOf(description)}' may not be null."
          )
```

- 该插值字符串只包含常量文本与 `NameOf(description)`（编译期常量），不包含运行时插值，因此**不应**调用 `String.Format`，可以直接用常量字符串。

`StringBuilder` 的 `&=`。原文示例：

```vb
' `&=` should work whether concatenating a string variable or a StringBuilder.
Let builder As StringBuilder = ...
builder &= "Line" & vbCrLf
```

- `builder &= "Line" & vbCrLf` 对 `StringBuilder` 生效，语义为追加（等效于 `builder.Append(...)`），与字符串变量的 `&=`（拼接再赋值）区分。

## Drawbacks
[drawbacks]: #drawbacks

- 合并规则依赖相邻插值片段的精确识别，边界情况（换行、注释、条件求值顺序）可能造成合并后语义变化。
- `&=` 对 `StringBuilder` 与对字符串变量语义不同，是类型相关的运算符重载，可能增加阅读负担。
- 依赖 `NameOf` 等编译期常量判断"是否含运行时插值"的分析器需要维护。

## Alternatives
[alternatives]: #alternatives

- 保持现状：`String.Format` 与 `&=` 各自按常规语义工作，靠开发者手写 `StringBuilder.Append`。
- 把优化完全交给运行时（如 `String.Format` 内部的常量折叠），不改变编译器行为。
- 引入显式的 `StringBuilder` 字面量/追加语法，而非复用 `&=`。

## Unresolved questions
[unresolved]: #unresolved-questions

- 合并的判定边界：非相邻（中间有其他语句/表达式）的插值片段是否也合并？原文未明确。
- "无插值时不调用 `String.Format`"的判定是否扩展到所有编译期可折叠的插值（如 `{1 + 2}`）？原文仅以 `NameOf` 为例。
- `StringBuilder &=` 的右侧是任意字符串表达式还是仅限常量/字面量？未定义。
- 原文未展示多个插值片段与 `StringBuilder` 场景的组合规则。
