# `With` 增强 / With Enhancements

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议增强 `With` 块：支持命名 `With` 变量（`With parameter = expr`），引入 `.Me` 伪成员以引用 `With` 目标本身，并允许对 `With` 目标成员使用复合赋值（如 `&=`）。

## Motivation
[motivation]: #motivation

- `With` 块只能在块内用 `.Member` 访问目标成员，无法直接引用目标对象本身（例如要把它传入其它方法），也无名可寻，嵌套 `With` 只能靠临时变量；
- 对 `With` 目标做复合赋值（如 `StringBuilder` 的 `&=` 追加）需要先 `With` 再在块外写 `builder &= ...`，样板多。

## Detailed design
[design]: #detailed-design

### 命名 `With` 变量

```vb
' Named `With` variable.
With parameter = command.CreateParameter()
    .ParameterName = "@id"
    .DbType = DbType.Guid
    .Value = id
    
    command.Parameters.Add(parameter)
End With
```

`With parameter = command.CreateParameter()` 给 `With` 目标起名 `parameter`。块内既可用 `.Member` 访问其成员，也可直接用 `parameter` 引用目标对象本身（上例传入 `command.Parameters.Add(parameter)`）。

### `.Me` 伪成员

```vb
' `.Me` pseudo-member.
With command.CreateParameter()
    .ParameterName = "@id"
    .DbType = DbType.Guid
    .Value = id
    
    command.Parameters.Add(.Me)
End With
```

`.Me` 作为伪成员在 `With` 块内指代目标对象自身，无需命名即可把它传出去（上例 `command.Parameters.Add(.Me)`）。

### 复合赋值运算符

```vb
' Compound-assignment operators.
With builder
    &= item.Header
    &= vbCrLf
    &= item.Description
    &= vbCrLf
End With
```

`With` 块内允许对目标成员的复合赋值：`&= item.Header` 等价于 `builder = builder & item.Header`（若 `builder` 是 `StringBuilder` 则为追加）。每次 `&=` 都作用于同一个 `With` 目标。

## Drawbacks
[drawbacks]: #drawbacks

- `.Me` 伪成员在嵌套 `With` 中指向哪一层目标需明确定义，且与类内 `Me` 关键字的拼写冲突可能引起混淆。
- 命名 `With` 变量与局部变量的作用域、遮蔽规则需规范化。
- 复合赋值到成员若 Get/Set 有副作用，求值顺序（是否每轮重取目标引用）需明确。

## Alternatives
[alternatives]: #alternatives

- 不使用 `With`，直接为参数创建局部变量再逐字段赋值；代价是丢失 `.Member` 缩写。
- `.Me` 也可以用命名 `With` 替代（每个 `With` 都必须命名），但 `.Me` 对匿名 `With` 更轻量。
- 复合赋值只在块外写 `builder &= ...`，不引入块内 `&=`。

## Unresolved questions
[unresolved]: #unresolved-questions

- `.Me` 在嵌套 `With` 块中是否总是引用最内层目标，还是最近命名目标。
- 命名 `With` 变量可否再次被赋值（其值可否重绑定到新对象）。
- `With` 内 `&=` 对 `StringBuilder` 是调用 `Append` 重载还是生成 `builder = builder & ...` 展开，需按目标类型确定。
