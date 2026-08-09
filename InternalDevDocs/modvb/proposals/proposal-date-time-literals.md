# 日期/时间字面量增强 / Date and Time Literal Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `#...#` 日期/时间字面量：在 `Date` 字面量中支持毫秒、支持 `DateTimeKind`（UTC/Local）后缀；并为 `DateOnly`、`TimeOnly`、`TimeSpan`、`DateTimeOffset` 引入内置字面量类型支持（带 `d`、`t`、`s` 等类型后缀）。Anthony 以多个 `?` 标注这些形态仍在探索中。

## Motivation
[motivation]: #motivation

现有 VB 的 `#1/1/2026#` 字面量只能表达无毫秒、固定 Kind 的 `Date`，而 `DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 在 .NET 上已常用，却缺少原生字面量，只能借助 `DateTime.ParseExact` 或显式构造。内置字面量让这些类型的书写更直白、更易读，并降低出错率。

期望的结果：毫秒、Kind 直接写入 `#...#`；`#...#d`/`#...#t`/`#...#s` 分别产生 `DateOnly`/`TimeOnly`/`TimeSpan`；带 `-` 与偏移量的写法产生 `DateTimeOffset`。

## Detailed design
[design]: #detailed-design

### `Date` 字面量中的毫秒与 DateTimeKind

```vb
' Millisecond in `Date` literals.
? #1/1/2026 10:00:00.200#

' DateTimeKind in `Date` literal?
? #1/1/2026 UTC#
? #1/1/2026 Local#
```

`#1/1/2026 10:00:00.200#` 表达毫秒（`.200`）；`#1/1/2026 UTC#` 与 `#1/1/2026 Local#` 指定结果的 `DateTimeKind`（`Utc`/`Local`）。Anthony 以 `?` 标注 Kind 后缀尚未定稿。

### 内置类型字面量：DateOnly / TimeOnly / TimeSpan / DateTimeOffset

```vb
' Built-in type support for: DateOnly, TimeOnly, TimeSpan, DateTimeOffset
? #7/4/2026#d
? #12:00#t
? #1h 35m#s
? #200ms#s
? #4/2/2007 7:23:57 PM - 4/3/2007 2:23:57 AM = -07:00:00#
```

- `#7/4/2026#d`：`DateOnly` 字面量（后缀 `d`）；
- `#12:00#t`：`TimeOnly` 字面量（后缀 `t`）；
- `#1h 35m#s`、`#200ms#s`：`TimeSpan` 字面量（后缀 `s`），以 `h`/`m`/`ms` 单位组合表达时长；
- `#4/2/2007 7:23:57 PM - 4/3/2007 2:23:57 AM = -07:00:00#`：`DateTimeOffset` 字面量，用 `-` 连接起止时刻、`=` 指定偏移量（`-07:00:00`）。

## Drawbacks
[drawbacks]: #drawbacks

- 字面量语法变得拥挤：毫秒、Kind、类型后缀、单位、偏移量的组合规则多，解析与易读性都受影响。
- `#...#d`、`#...#t`、`#...#s` 与字符串/XML 字面量可能产生的歧义需小心界定。
- `DateTimeOffset` 的"起止时刻 + 偏移量"语义（区间而非单点）与常见 `Date` 字面量认知不同。

## Alternatives
[alternatives]: #alternatives

- 不新增类型后缀，仅靠静态工厂（如 `DateTimeOnly.Parse(...)`）书写这些类型的字面量。
- 只在 `Date` 内扩展毫秒与 Kind，`DateOnly`/`TimeOnly`/`TimeSpan`/`DateTimeOffset` 继续用构造器。
- 采用 C# 侧的 `DateOnly.FromDateTime(...)` 等 API 组合，不引入新字面量语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- Anthony 原文明确标注 **Not shown**：内置类型支持是否扩展到 `Half`、`Int128`（`^`/`LongLong`?）、`UInt128`。
- `System.Numerics` 类型（如 `BigInteger`）是否需要特殊处理/内置字面量支持。
- 是否从接口推断运算符（例如从 `IEquatable`、`IComparable` 推断 `=`/`<` 等运算符）。
- Kind 后缀（`UTC`/`Local`）与默认 `DateTimeKind.Unspecified` 的转换/比较规则。
- `TimeSpan` 单位缩写集合（`h`/`m`/`s`/`ms`）的完整语法与是否允许负数。
