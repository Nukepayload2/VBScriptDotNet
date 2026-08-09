# 后置空合并运算符与空指示符 / Post-fix Null-Coalescing Operator and Null Indicator

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入后置空合并运算符 `??`（左操作数为 null 时返回右操作数）与空指示符 `?`：`?` 可标注可空值（推断 `Boolean?` 等）、声明可空引用类型（`String?`），并可附加在表达式后文档化"可能为 null"从而启用更严格的错误检查。

## Motivation
[motivation]: #motivation

获取"可能为空"的数据（如数据库字段、字典查询、外部 API）后，需要简洁的空合并与空标注。后置 `??` 让 `Label1.Text = account.Description ?? "n/a"` 直接内联兜底值；空指示符 `?` 则把"该值可为 null"的意图写进类型与表达式，供编译器进行更严格的可空性检查。原文指出 section 12 的亮点"可空引用类型（NRT）"仍需迭代（见 Unresolved questions）。

## Detailed design
[design]: #detailed-design

### 后置空合并运算符 `??`

```vb
' Post-fix null-coalescing operator.
Label1.Text = account.Description ?? "n/a".
```

`account.Description ?? "n/a"`：当 `account.Description` 为 null 时取 `"n/a"`，否则取 `account.Description`。（原文行尾多出一个句点，疑为笔误。）

### 空指示符 `?`：值可空推断

```vb
' Null indicator operator `?` for values.
Let flag = True? ' Infers `Boolean?` type.
```

`True?` 在 `True` 后附加空指示符 `?`，使 `flag` 推断为 `Boolean?` 类型。

### 空指示符 `?`：可空引用类型

```vb
' Null indicator `?` for optional reference types.
Property Description As String?
```

`Property Description As String?` 把引用类型 `String` 声明为可空，表达"该属性可以为 null"。

### 空指示符 `?`：文档化"可能为 null"并启用严格检查

```vb
' Documents value "may be null", turns on
' stricter error checking.
Let result = lookup("key")?
```

`lookup("key")?`：在表达式后附加 `?`，向编译器文档化该值"可能为 null"，并开启更严格的可空性错误检查。

## Drawbacks
[drawbacks]: #drawbacks

- `??` 与 `?` 两个符号在不同位置承担多种含义（空合并、可空类型标注、可空推断、可空性文档化），符号过载，可读性存疑。
- 可空引用类型（`String?`）与 NRT 特性耦合，而 NRT 本身尚未定稿，规则可能反复。
- 后置 `?` 与三元条件/`If()` 等现有语法在解析上存在歧义风险。

## Alternatives
[alternatives]: #alternatives

- 用现有 `If(account.Description IsNot Nothing, account.Description, "n/a")` 或空合并函数替代 `??`。
- 用 `Nullable(Of T)` 泛型（如 `Nullable(Of String)`）替代 `?` 语法。
- 空指示符 `?` 仅用于可空类型标注，不扩展到表达式后的可空性文档化。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确标注：section 12 的亮点 "Nullable Reference Types" 设计需要进一步迭代以降低困惑与恼人程度，作者正在修订，故 `String?` 等可空引用类型的最终规则未定。
- `lookup("key")?` 触发的"更严格错误检查"具体规则（哪些误用报错、哪些仅提示）未定义。
- 后置 `??` 与 `?` 空指示符在解析（如 `a? ?? b`、`a ?? b?`）中的歧义处理未在原文说明。
- 空合并与可空值类型（`DateTime?` 等）的交互、链式合并的优先级未详述。
