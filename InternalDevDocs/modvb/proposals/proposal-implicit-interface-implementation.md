# 隐式接口实现与签名放宽 / Implicit Interface Implementation

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入**隐式接口实现**：类只需 `Implements` 某接口，接口成员的实现可由"同名同签名"的公共成员自动满足，无需逐个写 `Implements I.X` 子句。同时放宽显式接口实现的签名匹配，使一个方法（如 `GetEnumerator`）能以协变返回类型同时满足多个接口成员。Anthony 注明这"对代码生成很有用"。

## Motivation
[motivation]: #motivation

显式 `Implements` 子句（`Function Dispose() Implements IDisposable.Dispose`）冗余且易错；而实现多个接口时，若签名天然一致，为每个接口成员重复标注子句很啰嗦。隐式实现让"实现了接口"的类自动满足同名同签名的成员；签名放宽则让 `IEnumerable(Of Student).GetEnumerator` 与 `IEnumerable.GetEnumerator` 这两个返回类型不同的接口成员可由一个方法同时满足。

期望的结果：代码生成器输出更精简、实现类更易维护、泛型接口与非泛型接口的同名成员无需重复方法。

## Detailed design
[design]: #detailed-design

### 隐式接口实现

类 `Implements IDisposable` 后，同名同签名的 `Dispose()` 自动成为其实现，无需 `Implements IDisposable.Dispose`：

```vb
' Implicit interface implementation.
' (Great for code generation!)
Class ResourceHandle
    Implements IDisposable
    
    Sub Dispose() ' Implements IDisposable.Dispose
        ...
    End Sub
End Class
```

`ResourceHandle.Dispose()` 因名字与签名匹配 `IDisposable.Dispose` 而被视为该接口成员的实现。注意 `Dispose` 必须为 `Public`（或至少可访问）才能满足接口契约。

### 显式接口实现的签名放宽

一个方法满足两个接口成员：`IEnumerable(Of Student).GetEnumerator`（返回 `IEnumerator(Of Student)`）与 `IEnumerable.GetEnumerator`（返回 `IEnumerator`）。由于 `IEnumerator(Of Student)` 派生自 `IEnumerator`，一个返回泛型接口的方法即可协变满足两者：

```vb
' Signature relaxation for explicit interface implementation.
Class StudentCollection
    Implements IEnumerable(Of Student)

    ' No need for second method.    
    Function GetEnumerator() As IEnumerator(Of Student)
      Implements IEnumerable(Of Student).GetEnumerator,
                 IEnumerable.GetEnumerator

        ...
    End Function
End Class
```

同一 `Implements` 子句列出两个接口成员，而方法体只有一个——"无需第二个方法"，靠返回类型的协变（`IEnumerator(Of Student)` → `IEnumerator`）完成匹配。

## Drawbacks
[drawbacks]: #drawbacks

- 隐式实现可能误配：类新增的公共成员无意间"抢走"接口成员的实现，行为随成员增删漂移。
- 接口存在两个名字相同、签名不同（重载）的成员时，隐式匹配规则复杂。
- 协变满足多个接口成员后，调用方通过不同接口引用看到的返回类型不一致，调试时可能困惑。

## Alternatives
[alternatives]: #alternatives

- 维持显式 `Implements` 子句不变，仅允许"同名同签名"快速路径（不自动，但提供补全建议）。
- 只做隐式实现，不引入签名放宽（`GetEnumerator` 仍需手写两个方法）。
- 依赖 IDE 代码补全生成 `Implements` 子句，不改变语言语义。

## Unresolved questions
[unresolved]: #unresolved-questions

- 隐式实现是否要求成员为 `Public`，还是 `Private`（配合 `Implements` 关键字）也可。
- 接口有多个同名重载成员时，隐式匹配如何选定实现。
- 签名放宽是否扩展到参数（如可选参数）或名字，还是仅返回类型（协变）。
