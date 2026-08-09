# Bit Enum / `Bit` 位枚举

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

`Bit Enum` 为标志（`<Flags>`）枚举提供专用语法：成员**自动按 1、2、4、8 … 初始化**；用逗号分隔成员即可构成掩码（`Odds = First, Third`）；`Bit 4 To Bit 7` 定义一段命名数值位段；并配合 `&B_0111_1010` 二进制字面量、`flags(Bits ...)` 读取与 `flags(Bit ...) = True` 写入的简化检查/设置语法。

## Motivation
[motivation]: #motivation

手写 `Flags` 枚举必须自己数 1、2、4、8，掩码还得写 `First Or Third`，既容易出错又难读；读取/设置单比特要写 `(flags And MyFlags.Third) = MyFlags.Third` 之类的手动位运算，30 年经验的程序员也会觉得繁琐。`Bit Enum` 让位枚举的声明、掩码、位段、读取与设置都有专门语法，声明正确性由编译器保证，使用处一目了然。

## Detailed design
[design]: #detailed-design

`Bit Enum` 声明、自动初始化与掩码。原文示例（第 8 章）：

```vb
' Bit modifier for <Flags> Enums.
Bit Enum MyFlags As Byte
    None
    
    ' Correct initialization by default
    First  ' = 1
    Second ' = 2
    Third  ' = 4
    Fourth ' = 8
    
    ' Simpler masks.
    Odds = First, Third
    Evens = Second, Fourth
    All = Odds, Evens

    ' Named numeric sections.    
    Kind = Bit 4 To Bit 7
End Enum
```

- `Bit Enum MyFlags As Byte`：声明一个以 `Byte` 为底层类型的位枚举。
- 连续成员 `First`/`Second`/`Third`/`Fourth` **默认自动按位初始化**（= 1、2、4、8），无需手写数值。
- `Odds = First, Third` / `Evens = Second, Fourth` / `All = Odds, Evens`：用逗号列成员即可构成**掩码**（等价于 `First Or Third`）。
- `Kind = Bit 4 To Bit 7`：`Bit 4 To Bit 7` 定义一段**命名数值位段**——占据第 4 到第 7 位的一段连续位，命名为 `Kind`。

二进制字面量与简化检查/设置。原文示例：

```vb
Let flags As MyFlags = &B_0111_1010

' Simpler checking.
? flags(Bits MyFlags.First)                  ' False.
? flags(Bits MyFlags.Second)                 ' True.

' Simpler settting.
flags(Bit MyFlags.Third) = True

' I have been coding for 30 years and don't need to
' do manual bit operations to prove myself.
? flags(Bit MyFlags.Second, MyFlags.Fourth) ' True.

? flags(Bit MyFlags.Kind)                   ' 7
```

- `Let flags As MyFlags = &B_0111_1010`：`&B_0111_1010` 是二进制字面量（下划线为数字分隔符），直接初始化位枚举变量。
- 读取（检查）：`? flags(Bits MyFlags.First)` 以 `Bits MyFlags.XXX` 作索引，返回该位是否置位（`False`）；`flags(Bits MyFlags.Second)` 返回 `True`。`?` 是 VB 的打印简写（`Print`）。
- 写入（设置）：`flags(Bit MyFlags.Third) = True` 以 `Bit` 标记位并赋值，实现"置位/复位"。
- 一次检查多个位：`flags(Bit MyFlags.Second, MyFlags.Fourth)` 判定 `Second` 与 `Fourth` 同时置位（结果为 `True`）。
- 位段取值：`flags(Bit MyFlags.Kind)` 读出 `Kind` 位段的值（`7`）。

## Drawbacks
[drawbacks]: #drawbacks

- 新增 `Bit`/`Bits` 上下文关键字与枚举的 `Bit ... To Bit ...` 位段语法，语言面扩大，与普通 `Enum` 并存需明确区分。
- `flags(Bit MyFlags.Third) = True` 改变了索引器的读取/写入语义，可能让读者误以为是数组索引。
- 掩码、位段的求值规则（位段 `Bit 4 To Bit 7` 的起止含不含端点）需要精确规范。

## Alternatives
[alternatives]: #alternatives

- 保持现状：手写 `1, 2, 4, 8` 与 `Or` 掩码、手动位运算检查/设置。
- 仅自动初始化位值、不引入 `Bit`/`Bits` 索引语法；检查/设置仍用现有位运算。
- 用 `Flag(0)`、`Flag(4)` 这类函数式写法替代 `Bit n To Bit m` 位段语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Bit 4 To Bit 7` 的语义是包含 `4` 与 `7` 两端点（四比特段）还是半开区间，需明确。
- `flags(Bit MyFlags.Kind)` 位段读值返回 `7`：是对齐到最低位（`0b0111`）还是原样 4–7 位的值，需要规范。
- `None` 未显式赋值时是否恒为 0；掩码 `All = Odds, Evens` 的传递性求值（由掩码再组合掩码）规则原文未细化。
- `Bits` 与 `Bit` 两种索引标记的语义差异（前者仅读、后者可写）及是否可混用，原文未明确。
