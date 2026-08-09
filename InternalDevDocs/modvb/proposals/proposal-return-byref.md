# `Return` 赋值给 `ByRef`/`Out` 参数 / Return Assigning to ByRef/Out Parameters

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议让 `Return` 语句在返回函数值的同时，可一并给 `ByRef`/`Out` 参数赋值，用一条语句完成"返回结果 + 写回输出参数"。

## Motivation
[motivation]: #motivation

许多函数在返回结果的同时要回填输出参数（如状态码、结果对象）。现行写法要在 `Return` 之前单独写多条对 `ByRef`/`Out` 参数的赋值，最后再 `Return`；若返回值依赖这些赋值的结果，顺序敏感且易错。本建议把"写回输出参数"并入 `Return` 语句，使一次返回即完成全部输出。

## Detailed design
[design]: #detailed-design

```vb
' `Return` statement may now also assign to `ByRef`/`Out` parameters.
Return True, value:=result
```

`Return True, value:=result` 做了两件事：以 `True` 作为函数返回值，并把 `result` 赋给名为 `value` 的 `ByRef`/`Out` 参数（具名实参形式）。逗号分隔的多个赋值项分别匹配各 `ByRef`/`Out` 形参。

## Drawbacks
[drawbacks]: #drawbacks

- `Return` 原本只表达"返回"，如今夹带赋值副作用，读者可能忽略被同时写回的参数。
- 需要定义 `Return` 中赋值项对参数名的解析、可选参数、以及部分形参未赋值的校验规则。
- 与现有"仅 `Return`"写法并存时，两种风格需在规范中统一。

## Alternatives
[alternatives]: #alternatives

- 保持现状：先在方法体内对 `ByRef`/`Out` 参数赋值，再单独 `Return`；代价是多行样板。
- 用 `Out` 实参/形参建议（见 8 节）配合元组返回替代多输出。
- 让 `Return` 支持 `(returnValue, byRef1, byRef2)` 元组式一次性输出。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Return` 中赋值项的具名语法（`value:=result`）与位置语法是否都支持。
- 与 `ByRef`/`Out` 形参建议（`proposal-out-arguments.md`）的语法统一。
- 是否允许 `Return` 只赋值输出参数而不返回函数值（`Sub`/`Async Sub` 场景）。
