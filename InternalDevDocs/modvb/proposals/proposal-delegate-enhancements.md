# Delegate Enhancements / 委托增强与匿名委托类型

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

委托增强让 `+` / `-` 运算符对委托执行**强类型的 `Delegate.Combine` / `Delegate.Remove`**：`handlers += AddressOf Button_Click` 与 `handlers -= AddressOf Button_Click` 可以直接添加/移除事件处理器，无需手写 `[Delegate].Combine/Remove`。同时提供匿名委托类型的显式语法 `<Function(Double, Double) As Double>`，用于声明"匹配该签名的委托"类型。

## Motivation
[motivation]: #motivation

- 管理事件处理器列表时，手动调用 `[Delegate].Combine`/`[Delegate].Remove` 冗长且易错；用 `+=`/`-=` 直观表达"添加/移除"是主流语言的标准写法。
- 需要一个简洁的**委托签名**作为类型，直接嵌入声明：现有匿名委托类型要么缺失，要么需要冗长的命名委托，写起来像样板。

## Detailed design
[design]: #detailed-design

强类型 `Delegate.Combine/Remove`。原文示例（第 8 章）：

```vb
' Strongly typed [Delegate].Combine/Remove with + and -.
Let handlers As EventHandler = Null
handlers += AddressOf Button_Click
handlers -= AddressOf Button_Click
```

- `Let handlers As EventHandler = Null`：`handlers` 初始为 `Null`（Anthony 的空字面量写法，见 `Null` 字面量建议），类型为 `EventHandler`。
- `handlers += AddressOf Button_Click`：把 `Button_Click` 方法（经 `AddressOf` 取函数指针）**强类型合并**进 `handlers`，等价于强类型的 `Delegate.Combine`。
- `handlers -= AddressOf Button_Click`：把该方法从 `handlers` **强类型移除**，等价于强类型的 `Delegate.Remove`。

匿名委托类型。原文示例：

```vb
' Explicit syntax for anonymous delegate types.
Let binOp As <Function(Double, Double) As Double>
```

- `Let binOp As <Function(Double, Double) As Double>`：`<Function(Double, Double) As Double>` 是**匿名委托类型**——表示"接受两个 `Double`、返回 `Double`"的委托签名；`binOp` 是这种签名类型的变量，可用任何匹配该签名的 Lambda/方法赋值。

## Drawbacks
[drawbacks]: #drawbacks

- `+=`/`-=` 在委托上同时有"算术加法（不可用）"与"合并/移除"语义，需编译器区分，教育成本集中在个别语境。
- 匿名委托类型 `<Function(...) As ...>` 用了尖括号，与泛型类型参数 `(Of ...)`、XML 字面量等 VB 已有尖括号场景易混淆。
- `Delegate.Combine` 在事件与普通委托变量上的行为（是否创建新实例）需保持一致，否则隐式行为与显式调用结果不同。

## Alternatives
[alternatives]: #alternatives

- 保留显式 `[Delegate].Combine/Remove` 调用，不引入 `+=`/`-=`。
- 用 `Of` 语法定义匿名委托类型（如 `Delegate(Of Func(Of Double, Double) As Double)`），而非 `<...>`。
- 只增强 `+=`/`-=`，匿名委托类型交给运行时 `Func/Action` 泛型承担。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文明确标注：**Not shown: Relaxed delegates can be removed.**——宽松（relaxed）委托的移除行为未在原文展示，需另行确定。
- `handlers` 为 `Null` 时 `+=`/`-=` 的边界行为（视为空列表合并、还是抛异常）。
- `<Function(Double, Double) As Double>` 中参数是否可命名、是否可带 `Optional`/`ByRef` 修饰；返回类型省略时的规则。
- 匿名委托类型是否允许作为泛型实参、事件类型或方法返回类型，原文未涉及。
