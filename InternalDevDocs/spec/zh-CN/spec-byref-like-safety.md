# Byref 类似类型安全

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-byref-like-safety.md)

## 概述
[summary]: #summary

本规范定义 Visual Basic 语言如何识别 **byref 类似类型（byref-like）**——必须被限制在执行栈上的值类型，例如 `Span(Of T)` 与 `ReadOnlySpan(Of T)`——以及它如何强制执行使这些值不落入托管堆的安全规则。本规范还定义语言如何消费 C# 的 `allows ref struct` 反约束（anti-constraint）：byref 类似类型可被接受为带有该反约束的泛型参数的类型实参，该能力被传播到重写或实现声明成员的 Visual Basic 源类型参数上，并且这些类型参数被当作 byref 类似类型来验证和使用。

本规范涵盖两项能力：

1. **byref 类似类型识别与受限类型（restricted type）强制。** 当某个类型的定义带有 `System.Runtime.CompilerServices.IsByRefLikeAttribute` 时，该类型即为 byref 类似类型。就现有的受限类型分析而言，byref 类似类型属于受限类型，编译器会拒绝每一个会将 byref 类似值移入托管堆的使用。byref 类似类型在元数据中为兼容旧编译器而携带的 `Obsolete` 标记会被抑制。

2. **对 `allows ref struct` 反约束的消费。** 当被引用元数据中的某个泛型参数携带 `System.Reflection.GenericParameterAttributes.AllowByRefLike`（0x0020）时，语言允许将 byref 类似类型作为该参数的类型实参，把该能力传播到重写或实现声明成员的 Visual Basic 源类型参数上，并在重写或实现成员的整个主体内对这些类型参数应用 byref 类似规则。

第二项能力的 C# 对应物在 [Ref Struct Interfaces][ref-struct-interfaces] 提案中规定；本规范描述的是同一元数据契约在 Visual Basic 一侧的内容。

## 动机
[motivation]: #motivation

现代 .NET 依赖仅栈（stack-only）值类型来构建高性能 API。`Span(Of T)`、`ReadOnlySpan(Of T)`、`Utf8String` 以及许多其他类型被声明为 C# `ref struct` 类型，绝不可出现在托管堆上。C# 13 还额外允许这类类型通过 `allows ref struct` 反约束参与泛型抽象，从而使 `System.Collections.Generic.IEnumerable(Of T)` 和用户定义的接口这类类型能够以 byref 类似实参进行实例化。

Visual Basic 没有声明 byref 类似类型的语法。它在生态系统中的角色是*消费*它们：在方法主体中使用 `Span(Of T)`，按值传递 byref 类似值，以及实现接受这些值的泛型接口。历史上编译器并不识别 byref 类似类型：

- `ITypeSymbol.IsRefLikeType` 被硬编码为 `False`。
- 受限类型分析只识别三个特殊类型 `TypedReference`、`ArgIterator` 和 `RuntimeArgumentHandle`。
- 编译器不会抑制 byref 类似类型所携带的元数据 `Obsolete` 标记。

后果有两方面。直接使用 byref 类似类型的代码会以过时错误被拒绝，尽管该使用本身在其他方面是安全的。绕过该错误的代码——或者在三个特殊类型分析未覆盖的上下文中使用 byref 类似类型的代码——能够编译并产生在运行时失败的无效 IL，通常抛出 `InvalidProgramException`。把这些误用转换为编译期错误是一种纠正，而非回退：没有任何原本能正确编译且行为正确的程序会改变其含义。

由于交互窗口与普通编译共享同一套语言实现，这些规则必须在两种模式下完全一致。同一个编译器既接受 `.vb` 项目也接受脚本提交（submission）；安全规则不得在二者之间产生分歧。

## 详细设计
[design]: #detailed-design

### byref 类似类型识别

#### `IsRefLikeType` 与 `IsRestrictedType`

当且仅当某个类型的定义携带 `System.Runtime.CompilerServices.IsByRefLikeAttribute` 时，该类型才是 **byref 类似类型**。byref 类似类型是在其他语言中声明的——实际上作为 C# `ref struct` 类型；Visual Basic 从不从源产生 byref 类似类型。因此语言不为 byref 类似类型引入声明修饰符，也不引入 `scoped` 或 `UnscopedRef` 标注。byref 类似的逃逸与生命周期行为完全由编译器的内部规则来表达。

符号属性 `IsRefLikeType` 被定义为读取 `IsByRefLikeAttribute` 的存在与否；它不再被硬编码为 `False`。`IsRestrictedType()` 谓词此前只识别三个特殊受限类型 `TypedReference`、`ArgIterator` 和 `RuntimeArgumentHandle`，现予以扩展，使得一个类型为受限类型当且仅当它是 byref 类似的或属于那三个特殊类型之一。由于现有的受限类型检查点（checkpoint）——字段、数组、转换、lambda、匿名类型等——全部以该谓词来表达，此扩展将它们应用于每一个 byref 类似类型而不引入新的检查点站点。

注意，遗留的特殊类型尽管不是 byref 类似的，仍然是受限的。`TypedReference`、`ArgIterator` 和 `RuntimeArgumentHandle` 是其托管表示不宜装箱的值类型，因此针对它们的现有限制被原样保留。

#### 抑制 byref 类似的过时标记

byref 类似类型在元数据中携带一个 `Obsolete` 特性，其目的在于阻止不理解仅栈规则的编译器使用它们；[span-safety] 描述了这一约定。一个理解 byref 类似类型的编译器会忽略这一特定形式的 `Obsolete`。因此，当某个类型是 byref 类似的且不携带其他过时数据时，编译器会过滤该类型上的元数据 `Obsolete` 特性。这使得 `Span(Of Integer)` 能够在 Visual Basic 源中被直接使用和绑定：

```vbnet
Imports System

Dim buffer As New Span(Of Integer)(New Integer(9) {})
buffer(0) = 42                              ' Okay: element access is by reference, no boxing
```

#### 受限类型检查

以下检查点会拒绝每一个会将 byref 类似类型的值移入托管堆或以其他方式违反仅栈不变式的使用。

| 错误码 | 诊断 | 条件 |
|---|---|---|
| BC31393 | `ERR_RestrictedAccess` | 在一个 byref 类似的接收者上调用继承自 `Object` 或 `ValueType` 的实例成员，这会装箱该接收者 |
| BC31394 | `ERR_RestrictedConversion1` | 一个到 `Object` 或 `ValueType` 的转换，它会装箱该值 |
| BC31396 | `ERR_RestrictedType1` | 用作 `Nullable(Of T)` 的类型实参、字段、数组元素、数组返回值、`ByRef` 参数、匿名类型、委托或转换目标，或用作其参数未携带 `allows ref struct` 反约束的泛型类型实参 |
| BC32061 | `ERR_ConstraintIsRestrictedType1` | 将受限类型或特殊类型用作类型约束 |
| BC36598 | `ERR_CannotLiftRestrictedTypeQuery` | 一个会捕获或装箱 byref 类似值的 LINQ 查询 |
| BC36640 | `ERR_CannotLiftRestrictedTypeLambda` | 一个会在其闭包中捕获 byref 类似值的 lambda |
| BC37052 | `ERR_CannotLiftRestrictedTypeResumable1` | 一个其状态机会捕获 byref 类似值的异步或迭代器方法 |

字段、数组元素和装箱限制与 C# 的 [span-safety] 规则一致：byref 类似类型不能成为除另一个 byref 类似类型以外的任何类型的字段，不能成为数组元素，也不能被转换为非 byref 类似的类型。

```vbnet
Class C
    Private _field As Span(Of Integer)     ' Error BC31396: cannot be a field
    Private _arr() As Span(Of Integer)     ' Error BC31396: cannot be an array element

    Function GetSpan() As Span(Of Integer) ' Okay: a byref-like value may be returned by value
    End Function

    Sub Pass(ByRef s As Span(Of Integer))  ' Error BC31396: cannot be a ByRef parameter
    End Sub
End Class

Async Function ProcessAsync(s As Span(Of Integer)) As Task   ' Error BC36932: cannot be an async parameter
    Await Task.Delay(1)
End Function

Sub Use(s As Span(Of Integer))
    Dim f = Function() s.Length            ' Error BC36640: a lambda cannot capture a byref-like value
End Sub
```

注意，byref 类似类型的标量按值返回是合法的；限制适用于数组返回和 `ByRef` 参数。异步和迭代器参数会在声明处被拒绝（BC36932），与对具体 byref 类似参数的处理保持一致。

#### 显示

IDE 会为 byref 类似类型显示修饰符 `ByRef Like Structure`——例如 `ByRef Like Structure Span(Of T)`。该修饰符仅用于呈现。它不是语言语法的一部分，也无法在源代码中书写，正如 `ByRef Function` 只是一种显示约定而非一种声明形式。

### 脚本与 REPL 约束

交互窗口中的提交被编译为脚本类。一个顶层声明会成为脚本类的一个字段；提交的末尾表达式会被转换为提交返回类型 `Object`；并且脚本初始化方法总是一个异步方法。这三点各自与 byref 类似规则相互作用，产生三个交互窗口专属的约束：

- **顶层变量。** 对 byref 类似类型的顶层声明是一个错误（BC31396），因为该声明会成为脚本类的一个字段，而 byref 类似类型不能是字段。这与 C# 在其脚本宿主中针对 byref 类似类型的顶层变量所应用的规则相同、锚定的语义也相同。

- **提交结果。** 当一个提交的结果——末尾表达式，或由 `?` 命令打印的值——具有 byref 类似类型时，到提交返回类型 `Object` 的隐式转换会装箱该值，因此是一个错误（BC31394）。不提供任何特殊的打印行为：该值绑定在栈上且无法被持久化，所以打印一个无法被引用的类型的名称没有任何交互价值。

- **跨提交 Await。** 由于脚本初始化方法始终是异步的，一个使 byref 类似值留在作用域内的顶层 `Await` 会被异步状态机捕获，是一个错误（BC37052）。

以下用法在交互窗口中仍然可用：方法主体内的 byref 类似局部变量、`ByVal` 值参数，以及对泛型参数携带 `allows ref struct` 反约束的成员的消费。诊断不因模式而参数化：交互窗口报告的受限类型错误与普通编译相同，因此脚本模式不需要新的诊断文本。

### `allows ref struct` 反约束的消费

#### 在元数据中的表示

C# 13 允许一个泛型参数通过声明 `where T : allows ref struct` 来接受 byref 类似类型实参。该能力在元数据中编码为 `System.Reflection.GenericParameterAttributes.AllowByRefLike`（0x0020）。目标运行时是否支持此特性，可通过检查 `System.Runtime.CompilerServices.RuntimeFeature.ByRefLikeGenerics` 字段是否存在来确定，该字段在 .NET 8 及更高版本可用（[byref-like-generics]）。此特性不依赖 `RefSafetyRulesAttribute`，后者是为 ref 安全规则做版本控制的独立机制。

#### 类型实参

当相应的泛型参数携带 `allows ref struct` 反约束时，语言允许将 byref 类似类型作为类型实参。当该参数未携带反约束时，适用现有的限制，该使用以 BC31396 被拒绝。三个遗留的受限类型总是被拒绝，无论该参数是否携带反约束。

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
End Interface

Public Class C(Of T)
End Class

Dim x As IThing(Of Span(Of Integer))       ' Okay: the parameter allows byref-like type arguments
Dim y As C(Of Span(Of Integer))            ' Error BC31396: the parameter has no such capability
```

#### 重写与接口实现

Visual Basic 没有声明 `allows ref struct` 反约束的语法。对于 Visual Basic 源类型参数，该能力的唯一来源是它所重写或实现的成员的元数据。编译器在两个方向上传播该能力：

- **方法类型参数。** 方法的某个类型参数的 `AllowsRefLikeType` 属性不再总为 `False`。当该方法重写了基方法时，该属性取自被重写方法的对应类型参数。当该方法显式实现某个接口成员时，该属性取自该接口成员的对应类型参数。该属性采用惰性计算，并在解析接口实现期间受重入保护。

- **类型类型参数。** 对于一个泛型类型的类型参数，该能力由该类型的传递接口集合计算得出：当该集合中的某个接口 `I(Of ...)` 在某个序号上的参数携带反约束、并且对应的类型实参就是容器自身的类型参数时，该序号即为有能力的。此计算是惰性的并被缓存。该机制为 Visual Basic 所特有：C# 通过类型声明上显式的 `allows ref struct` 语法获得同一能力，而 Visual Basic 没有这种语法。

传播之后，重写或实现泛型参数携带反约束的成员能够成功编译；本应报告的约束不匹配错误不再产生，且源类型参数会报告该能力。在重写或实现成员主体的全程，该类型参数被当作 byref 类似类型来验证和使用。

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
    Sub Use(value As T)
End Interface

Public Class Impl(Of T)
    Implements IThing(Of T)

    Public Sub Use(value As T) Implements IThing(Of T).Use
    End Sub
End Class
```

当一个类型参数参与多个接口的实现时，该能力是这些接口上的并集：只要传递集合中的任一接口使某个序号有能力，该序号即为有能力的。若一个成员必须同时实现两个对应参数不一致的接口成员，现有的约束一致性检查会报告该不匹配。

#### 方法主体规则

适用于具体 byref 类似值的规则也适用于携带该能力的类型参数。定义一个新谓词 `IsRefLikeOrAllowsRefLikeType()`：

```vbnet
IsRefLikeType OrElse SpecialType.IsRestrictedType() OrElse (TypeParameter AndAlso AllowsRefLikeType)
```

对于不是类型参数的类型，该谓词恰好等于 `IsRestrictedType()`，因此它是遗留谓词的严格超集，不会改变现有代码的行为。该谓词驱动字段、数组、`ByRef`、异步捕获、lambda、匿名类型、委托、属性类型、返回数组和数组字面量这些检查点。从有能力类型参数到 `Object` 的转换会被拒绝——转换分类器不返回任何转换——并在用处报告一般的转换错误。

```vbnet
Public Class Impl(Of T)
    Implements IThing(Of T)

    Private _field As T                    ' Error BC31396: cannot be a field
    Private _arr() As T                    ' Error BC31396: cannot be an array element

    Public Sub Use(value As T) Implements IThing(Of T).Use
        Dim local As T = value             ' Okay: stack local
        Dim boxed As Object = value        ' Error BC30311: a capable type parameter cannot be boxed
    End Sub
End Class
```

注意，携带该能力的类型参数对每一次替换都受这些限制约束，包括对非 byref 类似类型的替换。因此 `Impl(Of Integer)` 同样受 `T` 上的字段限制约束。这与反约束的 C# 语义一致：一个声明为 `allows ref struct` 的类型参数不能用于 byref 类似替换会违反的上下文，无论实际类型实参是什么。

#### BC31393 检查点

有两个途径会装箱 byref 类似的接收者。二者都以 BC31393（`ERR_RestrictedAccess`）被拒绝，其消息指出该类型是受限类型，不能用于访问继承自 `Object` 或 `ValueType` 的成员：

- **直接调用。** 在一个 byref 类似的接收者上调用一个继承自 `Object` 或 `ValueType` 且未被该接收者重写的实例成员——`GetHashCode`、`ToString`、`GetType` 或 `Equals`——是一个隐式装箱操作，会被拒绝。该检查既适用于有 byref 类似能力的类型参数，也适用于具体的 byref 类似类型。重写不受影响：当接收者重写了该成员时，该成员绑定到接收者自身的类型，检查不适用。这样的成员仍然可能是过时的，例如 `Span(Of T).GetHashCode`。

- **AddressOf 与委托创建。** 从 byref 类似接收者的实例方法创建委托，需要把接收者装箱到委托目标中，因为委托必须携带对接收者的托管引用。对于 byref 类似接收者的每一个实例成员——无论是继承的成员还是在接收者上声明的成员——都以 BC31393 被拒绝。若没有该检查，发射的代码会装箱接收者并在运行时抛出 `InvalidProgramException`。Shared 方法没有接收者，不受影响。非 byref 类似的接收者不受影响：对于一个普通的值类型，把接收者装箱到委托目标中是合法的。

```vbnet
Dim r As New R()
r.GetHashCode()                            ' Error BC31393: member inherited from Object
Dim d As Func(Of String) = AddressOf r.M   ' Error BC31393: delegate creation must box the receiver
r.Length                                   ' Okay: own member, no boxing
```

这两个途径互斥——直接调用通过调用绑定器绑定，而 `AddressOf` 表达式通过委托创建路径绑定——因此一次使用恰好报告一条诊断。

#### 代码生成

代码生成无需改动。通过泛型接收者的调用始终发射为 `constrained.` callvirt，因此通过有能力类型参数调用接口成员从不装箱接收者。这是发射器的既有行为。

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
    Sub Use(value As T)
End Interface

Public Sub Invoke(Of T As IThing(Of T))(value As T)
    value.Use()                            ' Okay: emitted as constrained. callvirt
End Sub
```

#### 边界与限制

byref 类似类型和有能力的类型参数可用以下用法：`ByVal` 标量参数、按值返回、栈局部变量，以及通过泛型接收者的约束调用（constrained call）。以下用法被拒绝：`ByRef` 参数、字段、数组元素和数组参数、装箱转换、异步和迭代器参数（BC36932）及捕获（BC37052）、lambda 捕获（BC36640）、匿名类型字段，以及属性类型。

以下是 Visual Basic 固有的限制，与本特性无关：

- **隐式接口实现。** Visual Basic 仅通过显式的 `Implements` 子句支持接口实现。一个按名称与接口成员匹配但没有 `Implements` 子句的方法并不满足该接口（BC30149）。这适用于每一个接口成员，而非专门针对其参数携带反约束的成员，也不是本特性引入的限制。

- **默认接口方法。** 实现本规范的编译器不支持默认接口方法。因此，通过有能力的类型参数调用默认接口成员这一场景不可达。

- **Byref 枚举数（enumerator）。** Visual Basic 没有按引用返回的属性声明语法，但它从元数据消费这类属性。一个通过 `ref readonly` 签名（元数据 `modreq(In)`）按引用返回值的 `Current` 属性，会被导入并通过编译器的自动解引用来读取，因此标准的 byref 类似枚举数——例如 `ReadOnlySpan(Of T).Enumerator.Current`——可以用于 `For Each` 语句。一个返回可变引用（纯 `ref`，不带 `modreq(In)`）的 `Current` 本就可读，且保持可读。Visual Basic 仍然不能在源中编写这样的枚举数（没有为按引用返回的成员提供声明语法），且 `ref readonly` 的 `Current` 是只读的：编译器会拒绝通过它进行的赋值，与针对 ref-readonly 值的其他写入规则一致。

- **逃逸分析（escape analysis）。** C# 的 `scoped` 与 ref 逃逸分析是 C# 特有的机制，不属于本规范。

## 健全性
[soundness]: #soundness

激发本规范中每一条规则的不变式是：byref 类似类型的值绝不可出现在托管堆上。每一个受限类型检查都是对一个会将 byref 类似值放到堆上的操作的编译期拒绝：

1. 类的字段和静态字段不能具有 byref 类似类型，因为字段存储将位于堆上。
2. 数组元素不能具有 byref 类似类型，因为元素存储将位于堆上。
3. byref 类似值不能被转换为 `Object` 或 `ValueType`，因为装箱会在堆上分配。
4. byref 类似类型不能被用作类型实参，除非对应参数携带 `allows ref struct` 反约束，因为该替换否则可能把该值嵌入一个堆分配的表示中。
5. byref 类似值不能被 lambda 闭包、匿名类型、LINQ 查询闭包或异步或迭代器状态机捕获，因为这些都会把该值提升到一个堆分配的帧中。
6. byref 类似类型不能是 `ByRef` 参数或数组返回的元素类型，因为这些会允许该值逃逸出其栈帧。

`allows ref struct` 反约束在不削弱不变式的前提下推广了规则 (4)。携带反约束的类型参数被当作潜在 byref 类似的：每一条适用于具体 byref 类似值的规则都适用于此类类型参数的值，且 byref 类似类型可以被替换给它。这一放松仅限于规则 (4)，它是健全的，因为被替换的类型在泛型成员主体全程受完整的 byref 类似规则集约束。对该类型参数的任何使用都无法把该值移入堆，因为编译器把对该参数的每一次使用都当作该参数是 byref 类似的一样来检查。

反过来的性质——没有任何原本有效的程序变为无效——成立如下。对于每一个现有程序，相关类型要么是一个普通类型参数，对其而言该能力为 `False` 且新谓词与遗留谓词一致；要么是一个具体类型，对其而言新谓词与遗留谓词一致，除非该类型是 byref 类似的。在本特性之前，处于如今被拒绝上下文中的具体 byref 类似类型会在运行时产生无效 IL，因此新的编译期错误是对有效行为的纠正而非回退。未使用 byref 类似类型的程序不会观察到任何行为变化。

## 缺点
[drawbacks]: #drawbacks

- **行为变化。** 在如今被拒绝的上下文中使用过 byref 类似类型的程序，在本特性之前能编译并在运行时失败。它们现在改为在编译期失败。这是一种纠正，但依赖了（损坏的）运行时行为的项目必须迁移。元数据 `Obsolete` 抑制也改变了编译器接受哪些程序：此前被当作过时而被拒绝的代码如今被接受，并受受限类型检查约束。

- **与 C# 的模型差异。** Visual Basic 不声明 byref 类似类型，也不暴露 `scoped` 或 `UnscopedRef`。byref 类似的逃逸与生命周期行为完全由编译器内部规则表达。这对消费者而言是足够的——C# 签名所承载的信息被尊重——但 Visual Basic 无法编写新的源级契约，例如 C# 用标注所表达的「此参数不逃逸」。

- **对具体替换的过度限制。** 一个有能力的类型参数对每一次替换都受限制，包括非 byref 类似的替换。因此 `Impl(Of Integer)` 即使 `Integer` 不是 byref 类似的，也受 `T` 上的字段限制约束。这与 C# 一致，也是反约束保持健全所必需的，但它比逐实例化的规则更为保守。

## 替代方案
[alternatives]: #alternatives

- **保持分析器为外部。** byref 类似检查可以留在由项目引用的独立分析器中。这会让交互窗口不受保护，让编译器缺乏内部强制，并且不抑制元数据过时标记。只要未引用该分析器，装箱的 byref 类似值就会继续产生无效 IL。

- **仅限制交互窗口。** 可以在不把受限类型分析扩展到普通编译的情况下加入 REPL 专属的约束。这会在共享同一个编译器的两种模式之间割裂语义，并放弃该特性的互操作价值——后者恰恰存在于消费 `allows ref struct` 成员的普通方法主体中。

- **移植完整的 C# ref 安全模型。** 语言可以采用 `scoped`、`UnscopedRef` 以及 byref 类似类型的声明语法。这超出了该语言的定位：Visual Basic 不声明 byref 类似类型，因此 `scoped` 将没有可标注的对象，而且对它也没有编写场景。成本高，收益仅限于 Visual Basic 所没有的场景。

- **自动应用反约束。** 语言可以自动把每一个无约束的泛型参数视为有能力的，类比于 C# 中自动应用 `allows ref struct` 的提案。这因与那里给出的相同理由而被拒绝：它使代码在一个目标框架上能编译而在另一个上失败，且没有任何语法指示；对某个接口的细微改动（例如添加一个默认成员）会远距离地改变现有实例化的行为。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 运行时支持

此特性依赖若干运行时与库支持：

- `System.Runtime.CompilerServices.IsByRefLikeAttribute`，它标记 byref 类似类型且必须被编译器识别。它可在基类库中获得。
- `System.Reflection.GenericParameterAttributes.AllowByRefLike`（0x0020），它在元数据中编码泛型参数上的反约束。
- `System.Runtime.CompilerServices.RuntimeFeature.ByRefLikeGenerics`，它指示运行时支持 byref 类似泛型实参。该字段在 .NET 8 及更高版本可用（[byref-like-generics]）；该 API 在 [dotnet/runtime#98070][rt-pr-98070] 中添加。
- `System.Reflection.Metadata` 中对读取该标志的支持，以及运行时中对 `constrained.` 调用机制的支持，后者业已存在。

此特性不要求 `RefSafetyRulesAttribute`；该特性对 ref 安全规则做版本控制，与反约束无关。

### API 版本控制

向一个现有的泛型参数添加 `allows ref struct` 反约束不是一个破坏源的更改：它扩展了可被替换的类型集合，且每一个新的替换都在用处针对 byref 类似规则进行检查。相反，移除该反约束始终是破坏性的，既破坏源也破坏二进制：现有对 byref 类似类型的替换将不再能编译。

API 作者应避免向某个抽象或虚成员的参数添加反约束（如果该成员日后可能获得一个无法为 byref 类似替换编写的实现）。例如，向一个接口添加默认接口方法会影响 C# 中的 byref 类似实现方；在不支持默认接口方法的 Visual Basic 中，向接口添加任何成员都要求实现方提供显式实现。

由于 Visual Basic 从元数据消费反约束且没有对应语法，版本控制的表面完全在 C# 一侧：一个添加或移除反约束的库会改变哪些 Visual Basic 程序能够针对它编译。

## 测试
[testing]: #testing

此特性由无副作用的内存编译测试来验证：它们不进行网络访问、文件写入、进程启动或注册表访问。

- **语义测试。** 一个正反用例矩阵针对具体 byref 类似类型和有能力的类型参数验证受限类型规则：字段、数组元素和数组参数、`ByRef` 参数、装箱转换、异步和迭代器方法与参数、lambda 捕获、匿名类型、属性类型、数组字面量，以及约束接口调用。该矩阵包含针对遗留受限类型（`TypedReference`、`ArgIterator`、`RuntimeArgumentHandle`）以及普通类型参数的回归锚点，以确认扩展后的谓词不会改变非 byref 类似代码的行为。对约束调用路径和被拒绝的委托创建路径验证所发射的 IL。

- **符号测试。** 针对能力传播的测试：重写一个参数携带反约束的 C# 泛型方法能够编译且源类型参数报告该能力；显式实现这样的接口成员能够编译并报告该能力；并且覆盖嵌入与运行时能力场景。

- **脚本测试。** 针对交互窗口的测试验证三个顶层约束——顶层变量、提交结果，以及跨提交的 `Await`——以及方法主体局部变量和 `ByVal` 参数的可用性。

- **外部覆盖。** 独立 Visual Basic byref 类似分析器的场景清单，以及 C# [RefStructInterfacesTests][ref-struct-interfaces-tests] 的消费点，被逐点对应到本地用例上。分歧被记录而非隐藏：对一个 byref 类似可处置对象使用 `Using` 语句报告基于模式的错误而非装箱错误；byref 类似类型的标量按值返回是合法的；C# 特有的场景——默认接口方法与逃逸分析——被标记为不适用并陈述上述理由。对 byref 类似枚举数使用 For Each 被实际验证而非标记为不适用：其 `Current` 是一个 ref readonly 属性，按 Byref 枚举数一节所述消费。

## 相关条目
[related]: #related-items

- [Ref Struct Interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md)
- [Span safety](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md)
- [Byref-like generics design](https://github.com/dotnet/runtime/blob/main/docs/design/features/byreflike-generics.md)
- [Restricted types in the Visual Basic specification](https://github.com/dotnet/vblang/blob/main/spec/types.md)
- [RefStructInterfacesTests](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Test/Symbol/SymbolTests/RefStructInterfacesTests.cs)

[ref-struct-interfaces]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md
[span-safety]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md
[byref-like-generics]: https://github.com/dotnet/runtime/blob/main/docs/design/features/byreflike-generics.md
[vblang-types]: https://github.com/dotnet/vblang/blob/main/spec/types.md
[ref-struct-interfaces-tests]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Test/Symbol/SymbolTests/RefStructInterfacesTests.cs
[rt-pr-98070]: https://github.com/dotnet/runtime/pull/98070
