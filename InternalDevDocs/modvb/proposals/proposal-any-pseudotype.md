# `Any` 伪类型 / Any Pseudotype

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

从 VB6/VBA 回归（或重新引入）`Any` 伪类型，用于动态分发（晚期绑定）替代 `Object`；`Object` 始终保持编译期成员检查。另支持单表达式内的临时晚期绑定写法 `someObject(As Any).X.Y().Z`。

## Motivation
[motivation]: #motivation

- 现在用 `Object` 声明做动态对象（如 `ExpandoObject`）时，成员访问会被当作编译期检查，编译器不知道成员存在从而报错，只能借助 `CallByName`/反射；
- `Any` 作为显式"动态类型"标记，让 `obj.CreateTime = Date.Now` 这类自由赋值合法化，贴近 VB6/VBA 习惯；
- 对只需要某一处晚期绑定的表达式，`(As Any)` 提供局部、按表达式控制的动态绑定，避免把整个变量都变成动态。

## Detailed design
[design]: #detailed-design

### `Any` 动态分发

```vb
' New (or back from VB6/VBA) `Any` pseudotype for
' dynamic dispatch instead of `Object`.
' `Object` always compile-time checked.
Let obj As Any = New ExpandoObject
obj.CreateTime = Date.Now
obj.Id = 1
obj.Description = "[Untitled]"
```

`obj As Any = New ExpandoObject` 后，对 `obj` 的任何成员读写（`CreateTime`、`Id`、`Description`）都走动态分发，运行时解析到 `ExpandoObject` 的成员，编译期不校验成员是否存在。对照地，`Object` 始终做编译期检查。

### 单表达式晚期绑定

```vb
' Late-bind for a single expression if you want.
? someObject(As Any).X.Y().Z
```

`someObject(As Any)` 对单个表达式强制晚期绑定：即使 `someObject` 的静态类型是 `Object`，`.X.Y().Z` 链也按动态解析，无需把整个变量类型改为 `Any`。

## Drawbacks
[drawbacks]: #drawbacks

- `Any` 与 `Object` 并存会造成"到底哪个是动态"的困惑，API 边界处类型不稳定。
- 动态分发牺牲编译期检查与 IntelliSense，`Any` 使用面扩大后错误会推迟到运行时。
- `(As Any)` 后置转换语法与现有的 `(As Type)` 后置转换（另有专项建议）要区分，避免混淆。

## Alternatives
[alternatives]: #alternatives

- 维持 `Object` + 显式 `CallByName`/反射/IDynamicMetaObjectProvider 手动绑定；
- 复用 `DynamicObject`/`ExpandoObject` 与 `Option Strict Off`，不新增关键字；
- 只提供 `(As Any)` 单表达式形式，不引入 `Any` 声明类型。

## Unresolved questions
[unresolved]: #unresolved-questions

- `Any` 与 `Object` 之间的隐式转换、可空性、`Is`/`TypeOf` 行为是否与 `Object` 一致。
- `(As Any)` 语法与 `proposal-postfix-casting` 的后置转换 `(As Type)` 是否共用解析规则。
- 动态分发底层走 DLR 还是反射；性能与 `Option Strict`/`Option Explicit` 的交互。
- `Any` 成员写回（`obj.Id = 1`）的赋值兼容性检查规则。
