# 接口实现委托给字段/属性 / Interface Implementation Delegation

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议让接口实现可以**委托给字段或属性**：声明 `Private WholeProvider As ContractAProvider Implements IContractA` 后，`IContractA` 的全部成员转发到字段 `WholeProvider` 上的 `ContractAProvider` 实例。这以"组合优于继承"（Composition Over Inheritance）的方式实现接口，支持整体委托与部分委托（混合自行实现）。

## Motivation
[motivation]: #motivation

类需要复用某个提供者（Provider）已实现的接口契约时，传统做法是手动为每个接口成员写转发方法。接口实现委托让"字段 `Implements` 接口"直接表达转发，大幅减少样板。部分委托还能混合：把某接口的大多数成员交给提供者，个别成员由类自己实现。

期望的结果：`MyObject` 同时 `Implements IContractA, IContractB`，`IContractA` 全部委托给 `WholeProvider`，`IContractB` 的 `X` 委托给 `PartialProvider` 而 `Y` 由类自行实现。

## Detailed design
[design]: #detailed-design

### 提供者类型

被委托方是实现接口的普通类：

```vb
' Interface implementation delegation to fields/properties.
' (Composition Over Inheritance FTW!)

Interface IContractA
    Sub M()
End Interface

Interface IContractB
    Sub X()
        
    Sub Y()
End Interface

Class ContractAProvider
    Implements IContractA
    
    ...
End Class

Class ContractBProvider
    Implements IContractB
    
    ...
End Class
```

### 整体委托

`MyObject` 用字段声明式地把 `IContractA` 整个委托给 `WholeProvider`：

```vb
Class MyObject
    Implements IContractA, IContractB
    
    ' Whole composition.
    Private WholeProvider As ContractAProvider Implements IContractA
```

`Implements IContractA` 出现在字段声明上：对 `IContractA.M` 的调用转发到 `WholeProvider.M`。

### 部分委托（混合自行实现）

`IContractB` 的 `X` 委托给 `PartialProvider`，而 `Y` 由 `MyObject` 自己实现：

```vb
    ' Partial composition.
    Private PartialProvider As ContractBProvider Implements IContractB
    
    Private Sub IContractB_Y() Implements IContractB.Y
        ...
    End Sub
End Class
```

`PartialProvider` 以 `Implements IContractB` 声明，会优先满足接口中它实现的部分（`X`）；`MyObject` 再用显式 `Implements IContractB.Y` 的 `Private Sub IContractB_Y` 补上自己负责的成员 `Y`。整体/部分委托的边界由编译器按"接口成员是否已由被委托字段覆盖"判定。

## Drawbacks
[drawbacks]: #drawbacks

- "字段同时是接口实现"打破"字段只是数据"的直觉，读代码时需理解隐式转发。
- 部分委托时哪个成员归字段、哪个归自身，判定规则复杂且易产生歧义。
- 与显式 `Implements`、隐式接口实现（见 `proposal-implicit-interface-implementation.md`）并存时，接口成员的归属解析重叠，需统一规则。
- 委托字段为 `Private`，其上的接口实现对外暴露时可见性语义需澄清。

## Alternatives
[alternatives]: #alternatives

- 手动为每个接口成员写转发方法（现状），不引入委托语法。
- 仅支持整体委托（`Implements` 后接全部接口成员转发），不支持部分混合。
- 用属性（而非字段）作为委托载体，支持运行时动态切换实现。

## Unresolved questions
[unresolved]: #unresolved-questions

- 委托字段是否允许 `Shared`、是否允许属性（Anthony 标题含"fields/properties"，正文只给出字段示例）。
- 被委托类型未实现接口的全部成员时（部分委托），未覆盖成员是否自动要求宿主实现。
- 转发是否保留接口成员的默认参数、重载与泛型形态。
- 接口被委托后，显式 `Implements` 与隐式实现之间的优先级。
