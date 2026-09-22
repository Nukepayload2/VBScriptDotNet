# 消费 C# 接口共享成员

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-interface-shared-members.md)

## 概述
[summary]: #summary

本规范定义 Visual Basic 语言如何消费 C# 11 的静态抽象接口成员（static abstract interface member）——即直接声明在接口上的共享抽象成员。语言将 `T.Member`、`T.Property = value`、`AddressOf T.Member`、`NameOf(T.Member)` 以及运算符绑定到以声明接口为约束的类型参数上，并将其发射为约束调用（constrained call），从而让运行时按实际类型参数进行分派。

本规范只涉及消费。Visual Basic 没有声明这些成员的语法：直接声明在接口上的 `Shared` 成员并非由该语言声明。这一边界被明确划定，且本规范不改变它。

其 C# 对应物在面向 C# 11 的 [static abstracts in interfaces][static-abstracts] 提案中规定。本规范描述同一元数据契约在 Visual Basic 一侧的内容。对 C# 14 扩展成员的消费属于另一份独立的规范。

## 动机
[motivation]: #motivation

.NET 泛型数学（generic math）——`INumber(Of T)` 及构建于其上的各接口——是用静态抽象接口成员实现的。Visual Basic 程序可以使用某个具体类型的静态成员（`MyNum.Zero`），也可以把该类型传给 C# 泛型方法，但它无法自己编写泛型算法：`T.Zero` 会被拒绝，因为类型参数不能被用作限定符。编写此类算法的能力，正是本规范所弥合的缺口。

由于交互窗口与普通编译共享同一份语言实现，消费规则在两种模式下必须完全一致。

## 详细设计
[design]: #detailed-design

本特性不引入任何新语法。它把语言已经在使用的机制——通过类型名访问共享成员——扩展到类型参数限定符上。

本节通篇的示例引用 `ExternLib` 所声明的若干接口：带 `static abstract T Zero { get; }` 与 `static abstract T Add(T a, T b)` 的 `IHasZero(Of T)`、带 `static abstract T Current { get; set; }` 的 `IConfig(Of T)`，以及带 `static abstract T operator +(T, T)` 的 `IAddable(Of T)`。

### 在元数据中的表示

静态抽象接口成员是直接声明在接口上的共享抽象成员——即 C# 中的 `static abstract`。在元数据中，它是一个静态抽象方法，或一个静态抽象属性的访问器。静态虚接口成员（C# 中的 `static virtual`）是共享的但非抽象的：它带有默认实现。

语言将某个**静态抽象接口成员**识别为既为抽象、且其包含类型为接口的共享成员。该识别对方法与属性访问器一视同仁。

### 经由约束类型参数的绑定

`T.Member`（其中 `T` 是类型参数）绑定到 `T` 的**有效接口集**（effective interface set）的共享成员上——即 `T` 的接口约束，加上这些接口所继承的全部接口。该绑定被限定于静态抽象接口成员。

```vbnet
Imports ExternLib

Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero                  ' Okay: static abstract property getter
    For Each item In items
        result = T.Add(result, item)          ' Okay: static abstract method
    Next
    Return result
End Function
```

对于其他一切情形，类型参数不能被用作限定符这一既有规则仍然有效：一次经由类型参数的成员访问，若未能解析到有效接口集中的某个静态抽象接口成员，则报告 BC32098。这涵盖无约束的类型参数、仅约束到某个类的类型参数、有效接口集中没有任何接口声明的名字，以及并非抽象的共享成员。

```vbnet
Function Use(Of T)(x As T) As Object
    Return T.Zero                             ' Error BC32098: type parameters cannot be used as qualifiers
End Function

Function Use(Of T As MyBaseClass)(x As T) As Object
    Return T.Zero                             ' Error BC32098: the class constraint contributes no interface members
End Function
```

**决策**：经由类型参数的绑定被限定于静态抽象成员。静态虚成员——共享但非抽象——不经由类型参数绑定，并报告 BC32098。这比 C# 更窄，因为 C# 也会经由类型参数分派静态虚成员；本规范刻意把新增表面积限制在抽象形式上，其运行时分派不依赖任何默认实现。

属性 setter 与 getter 对称：

```vbnet
Imports ExternLib

Sub Configure(Of T As IConfig(Of T))(value As T)
    T.Current = value                         ' Okay: static abstract property setter
End Sub
```

### 运行时支持

消费静态抽象接口成员要求目标运行时支持这些成员。是否支持由 `System.Runtime.CompilerServices.RuntimeFeature.VirtualStaticsInInterfaces` 的存在与否决定。当成员来自某个被引用的模块、而目标运行时未提供该特性成员时，该用法报告 BC32134。

```vbnet
' Target runtime without static abstract interface member support:
Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero                  ' Error BC32134: the target runtime does not support static abstract members in interfaces
End Function
```

### 代码生成

经由类型参数对静态抽象接口成员的调用，被发射为 `constrained.` 后接 `call`，并以该类型参数作为受约束类型。它不被发射为 `callvirt`，接收者也不会被丢弃：该约束告诉运行时去针对实际类型参数解析成员。这与 C# 对同一构造的发射方式一致。

```vbnet
Imports ExternLib

Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero
    For Each item In items
        result = T.Add(result, item)
    Next
    Return result
End Function
```

此方法体内部对 `get_Zero` 与 `Add` 的调用被发射为 `constrained. !!T` + `call`。

### 运算符

操作数为类型参数的运算符，解析到有效接口集上声明的某个静态抽象运算符。编译器把有效接口集的共享运算符与从操作数类型本身收集而来的运算符候选一并收集，交由既有的运算符解析机制从中择优。一次成功的解析被发射为 `constrained.` + `call`；所发射的 IL 与 C# 的发射逐字节相同。

```vbnet
Imports ExternLib

Function AddAll(Of T As IAddable(Of T))(a As T, b As T) As T
    Return a + b                              ' Okay: static abstract operator through type parameters
End Function
```

对于并非类型参数的接口类型操作数，不会征询静态抽象运算符；静态虚运算符则根本不作为候选被收集。两种情形下都报告常规的运算符解析失败（`Operator '...' is not defined`）。

### 委托创建与 nameof

`AddressOf T.Member` 创建一个目标为静态抽象成员的委托。该委托创建被发射为 `constrained.` + `ldftn`，且类型表达式接收者被保留以供发射。此委托对类型参数闭合，而非对某个实例闭合。

```vbnet
Imports ExternLib

Function GetCombiner(Of T As IHasZero(Of T))() As Func(Of T, T, T)
    Return AddressOf T.Add                    ' Okay: delegate creation; emitted as constrained. + ldftn
End Function
```

`NameOf(T.Member)` 被接受并返回成员名。`NameOf` 上下文抑制了直接访问本应报告的那些错误，因为 `NameOf` 只需要符号，从不求值该次访问。

```vbnet
Imports ExternLib

Function GetMemberName(Of T As IHasZero(Of T))() As String
    Return NameOf(T.Add)                      ' Okay: nameof
End Function
```

### 表达式树

静态抽象接口成员无法在表达式树（expression tree）中表示。当某个引用了它的 lambda 被转换为表达式树时——无论是经由方法调用、属性访问，还是 lambda 内部的一个 `AddressOf`——表达式树重写器都报告 BC37340。这与 C# 的 CS8927 对齐。

```vbnet
Imports ExternLib
Imports System.Linq.Expressions

Class Test
    Shared Sub Make(Of T As IHasZero(Of T))()
        Dim f = CType(Function() AddressOf T.Add, Expression(Of Func(Of Func(Of T, T, T))))   ' Error BC37340
    End Sub

    Shared Sub Make(Of T As IConfig(Of T))(value As T)
        T.Current = value                     ' Okay: ordinary assignment, no diagnostic
        Dim s = CType(Sub() T.Current = value, Expression(Of Action))   ' Error BC37340
    End Sub
End Class
```

常规的——非表达式树的——`AddressOf` 或赋值不报告诊断，因为委托创建与赋值这两条路径都携带类型参数接收者，并直接发射受约束形式。

### 经由接口名的直接访问

经由接口名直接访问某个静态抽象或静态虚接口成员是错误。这样的成员只能经由受约束的类型参数抵达。设想一个非泛型的 C# 接口 `I1`，声明了 `static abstract void M01();` 与 `static abstract int Zero { get; }`：

```vbnet
Class Test
    Shared Sub Goo()
        I1.M01()                              ' Error BC37314: a shared abstract or virtual interface member cannot be accessed
        Dim z As Integer = I1.Zero            ' Error BC37314
    End Sub
End Class
```

这与 C# 的 CS8926 对齐。对 `NameOf` 实参不报告此错误，因为 `NameOf` 并不访问成员；当成员经由类型参数抵达时也不报告此错误。

### 边界

本规范不为这些成员提供语言的声明语法。直接声明在接口上的 `Shared` 成员在声明处仍被拒绝：方法报 BC30270，属性报 BC30273。

```vbnet
Interface I1
    Shared Sub M1()                           ' Error BC30270: 'Shared' is not valid on an interface method declaration
End Interface

Interface I2
    Shared Property P1 As Integer             ' Error BC30273: 'Shared' is not valid on an interface property declaration
End Interface
```

声明这些成员仍是一个未来的议题，独立于本规范。

## 缺点
[drawbacks]: #drawbacks

- **就静态虚成员而言与 C# 不对称。** C# 经由类型参数分派静态虚成员；本规范只绑定静态抽象成员，并对静态虚形式报告 BC32098。C# 能接受的代码被 Visual Basic 拒绝。这种不对称是对表面积的刻意收窄，而非对 C# 的照搬建模。

- **运行时支持要求。** 对静态抽象接口成员的用法要求目标运行时提供 `RuntimeFeature.VirtualStaticsInInterfaces`。针对缺少该特性的目标运行时，消费此类成员的程序会编译失败，而不是产出不合法的 IL。因此，使用该特性的库会限制其使用者所能针对的运行时。

## 替代方案
[alternatives]: #alternatives

- **像 C# 那样经由类型参数绑定静态虚成员。** 此方案曾纳入考虑并被搁置。分派一个静态虚成员依赖运行时选取实际类型的实现，而本规范把新增表面积限定在分派有明确定义的抽象形式上。扩展到静态虚成员是一项向前兼容的未来变更。

- **暴露声明这些成员的语法。** 在 Visual Basic 源码中声明接口共享成员是一个独立的、更庞大的特性，会给语言增添语法。消费侧的价值可以独立成立，而声明侧被刻意排除在范围之外。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 运行时支持

静态抽象接口成员需要的运行时支持并非在所有目标上都存在。该特性以 `System.Runtime.CompilerServices.RuntimeFeature.VirtualStaticsInInterfaces` 作为消费的门控，因此针对缺少该特性的目标运行时，消费此类成员的程序会编译失败，而不是产出不合法的 IL。

### API 版本化

由于 Visual Basic 只消费这些成员，版本化表面积完全在 C# 一侧。某个库新增一个静态抽象接口成员，会改变哪些 Visual Basic 程序能够针对它编译：一个此前报告错误的用法如今会绑定。某个库移除一个成员，会把一个此前合法的用法变成错误。这两个方向都不会影响一个不提及该成员的程序的含义。

### 表达式树与 `NameOf`

`NameOf` 需要一个可解析的符号，但不得要求该访问可求值。本规范对 `NameOf` 实参抑制直接访问错误，并接受 `NameOf(T.Member)`。相比之下，表达式树的限制关乎树的表示：静态抽象成员没有对应的树节点形式，因此在被转换为表达式树的 lambda 内部的任何引用都是错误，包括一个在普通代码中原本合法的 `NameOf` 里的引用。

### 与 C# 的对齐

代码生成与 C# 对齐：经由类型参数的静态抽象成员被发射为 `constrained.` + `call`，委托创建被发射为 `constrained.` + `ldftn`，运算符的所发射 IL 逐字节相同。错误码 BC37314 与 BC37340 分别对应 C# 的 CS8926 与 CS8927。

## 相关条目
[related]: #related-items

- [Static abstract members in interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/static-abstracts-in-interfaces.md) —— 面向 C# 11 的静态抽象接口成员提案
- [RuntimeFeature.VirtualStaticsInInterfaces](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimefeature.virtualstaticsininterfaces) —— 对静态抽象接口成员消费进行门控的运行时能力成员

[static-abstracts]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/static-abstracts-in-interfaces.md
[runtime-feature]: https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimefeature.virtualstaticsininterfaces
