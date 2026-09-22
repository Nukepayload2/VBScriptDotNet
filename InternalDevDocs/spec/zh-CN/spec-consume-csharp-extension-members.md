# 消费 C# 扩展成员

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-csharp-extension-members.md)

## 概述
[summary]: #summary

本规范定义 Visual Basic 语言如何消费 C# 14 扩展成员（extension member）——在 C# 扩展块（extension block）中声明的扩展属性与扩展运算符——除语言已经消费的扩展方法之外。一个扩展成员通过其包含类型的形态及其标记属性从元数据中被识别，针对接收者类型被归约，并参与成员访问（`obj.Property`）、赋值（`obj.Property = value`）以及运算符解析（`a + b`）。

本规范只涉及消费。Visual Basic 没有声明这些成员的语法：扩展属性或运算符并非由该语言声明。这一边界被明确划定，且本规范不改变它。

其 C# 对应物在面向 C# 14 的 [extensions][extensions] 提案中规定。本规范描述同一元数据契约在 Visual Basic 一侧的内容。静态抽象接口成员——.NET 泛型数学所依赖的另一族成员——的消费属于另一份独立的规范。

## 动机
[motivation]: #motivation

现代 .NET 库越来越多地随附 Visual Basic 此前无法用其天然语法消费的扩展成员。C# 14 允许扩展块声明属性和运算符，一如方法。扩展方法今天对 Visual Basic 是可用的：一个 C# `[Extension]` 方法被作为 `receiver.Method()` 消费。扩展属性与运算符则不然：`"hello".CharCount` 报告 `CharCount` 不是 `String` 的成员，`a + b` 也不会解析到扩展运算符。调用它们的唯一方式是直接调用底层的访问器或运算符方法，这既冗长又偏离 API 所记录的形态。在 `Option Strict Off` 下，一次失败的扩展运算符查找会降级为晚绑定并在运行期失败，这比编译期错误更糟。

由于交互窗口与普通编译共享同一份语言实现，消费规则在两种模式下必须完全一致。

## 详细设计
[design]: #detailed-design

本特性不引入任何新语法。它把语言已经在使用的机制——扩展成员回退查找（fallback lookup）——扩展到属性与运算符这两种成员种类上。

本节通篇的示例引用一个 C# 库 `ExternLib`。它声明了一个带 `String` 上扩展块的 `[Extension]` 类（一个 `Int32` 属性 `CharCount` 与一个方法 `Shout`）、一个在 `Int32` 上的经典扩展方法 `Twice`、一个在值类型 `Vec` 上带 `operator +` 的扩展块，以及一个接收者为其块自身类型参数的泛型扩展块。

### 在元数据中的表示

一个 C# 14 扩展块以固定形态被发射进元数据：

- 位于命名空间作用域的一个 `[Extension]` 静态类，即**扩展容器**（extension container）。
- 一个名为 `<G>$<content-hash>` 的嵌套**分组类型**（grouping type），其本身标记为 `[Extension]`，承载扩展成员的实现。
- 一个名为 `<M>$<content-hash>` 的嵌套**标记类型**（marker type）：一个 `SpecialName`、`static`、public、派生自 `System.Object`、元数为零、不实现任何接口的类，带有唯一一个静态 `SpecialName` 方法 `<Extension>$(receiver)`，该方法的参数类型即扩展块的接收者类型。
- 扩展成员本身——方法、属性和运算符——作为分组类型的成员，各自标记 `System.Runtime.CompilerServices.ExtensionMarkerAttribute`。该属性唯一的字符串实参是该成员标记类型的元数据名，它把成员绑定到其所属的扩展块。

扩展方法存在于两种元数据形态中。经典形态是位于顶层的一个 `[Extension]` 静态方法，把接收者作为其第一个参数；语言一直消费的就是这一形态。C# 14 中位于扩展块里的扩展方法使用分组类型形态。扩展属性与运算符只出现在分组类型形态中，因此只识别 `[Extension]` 方法的经典扩展方法机制从未见到它们。

### 识别

编译器从元数据中识别以下内容：

- **扩展分组类型**是位于一个 `[Extension]` 容器内部、其本身标记为 `[Extension]` 的嵌套类型。
- **扩展标记类型**是一个嵌套类型，它 `SpecialName`、`static`、public、元数为零、派生自 `System.Object`、不实现任何接口，并带有唯一一个 `<Extension>$(receiver)` 标记方法。
- **扩展成员**是扩展分组类型内部携带 `ExtensionMarkerAttribute` 的方法或属性。

分组类型的**接收者类型**是其嵌套标记类型中标记方法的参数类型。对于非泛型扩展块，接收者类型是具体的；对于泛型扩展块，它是分组类型自身的类型参数。

```vbnet
Imports ExternLib

Dim count As Integer = "hello".CharCount      ' Okay: extension property getter; count = 5
Dim loud As String = "hi".Shout()             ' Okay: C# 14 extension method
Dim twice As Integer = 5.Twice()              ' Okay: classic extension method
Dim sum As Vec = New Vec(1, 2) + New Vec(10, 20)   ' Okay: extension operator; sum = (11, 22)
```

### 收集与查找作用域

扩展成员以与扩展方法相同的作用域规则被收集。绑定器遍历其作用域链，某个经 `Imports` 子句进入作用域的命名空间，会贡献它所声明的各 `[Extension]` 容器的扩展成员。因此，一个扩展成员仅当其包含命名空间在使用点处于作用域内时才参与查找。当其不在作用域内时，成员访问会报告「名字不是接收者的成员」这一常规错误：

```vbnet
' Without 'Imports ExternLib', the extension members are not in scope.
Dim count As Integer = "hello".CharCount      ' Error BC30456: 'CharCount' is not a member of 'String'
```

这与扩展方法的既有行为一致，不引入任何新的作用域规则。

### 归约

扩展成员在被使用之前，先针对实例类型被归约（reduce）。归约暴露出接收者，并且就绑定器而言，使该成员看起来像是接收者的一个普通成员。

- **扩展属性。** 接收者类型取自标记方法。该属性被归约为一个属性，其 `GetMethod` 与 `SetMethod` 是归约后的访问器，其 `ReceiverType` 是接收者类型。归约后的属性以不带其接收者参数的形式呈现，因此 `obj.Property` 与 `obj.Property = value` 经由普通的成员访问与赋值路径绑定。由于归约后的属性是接收者的一个普通成员，它凡普通成员访问参与之处皆参与：诸如 `obj.Inner.Value` 这样的链式访问逐段经由同一查找绑定，而对接收者使用的一条 `With` 语句，其 `.<member>` 访问也经由同一路径绑定。setter 与 getter 对称。
- **扩展运算符。** 接收者是运算符的第一个（左操作数）参数。该运算符保留其全部操作数，并以 `MethodKind.UserDefinedOperator` 呈现，从而流经既有的运算符解析机制。分组类型上的运算符由 C# 发射为一个抛出的桩（throwing stub）；真正的实现是扩展容器上签名相同的顶层非扩展方法，且该方法被优先选为调用目标。
- **扩展方法。** 对应的顶层 `[Extension]` 方法——把接收者作为显式参数接受的「shim」包装方法——被定位并经由经典扩展方法机制归约。既有路径不变。

扩展属性或运算符的归约完全以元数据形态来表述；不涉及任何新语法。

### 泛型扩展块

当接收者类型含有分组类型自身的类型参数时，接收者类型必须在使用该成员之前从实例类型推断得出。归约推断接收者所引用的类型参数，用推断出的实参构造分组类型，校验约束，并在构造后的类型上归约该成员。这正是经典扩展方法归约所用的同一套推断机制，应用于标记方法的「接收者即参数」形态上。

```vbnet
Imports ExternLib

Dim arr As Integer() = New Integer() {1, 2, 3}
Dim n As Integer = arr.TotalCount             ' Okay: receiver T() is inferred with T = Integer
```

### 名字解析与优先级

扩展成员是回退候选。成员查找先解析实例成员；当某个所请求名字的实例成员成功绑定时，不会征询任何扩展成员。当查找找不到实例成员时，才归约并考虑处于作用域内的扩展成员。

```vbnet
Imports ExternLib

' ExternLib declares: extension(string s) { public int Length => s.Length * 1000; }
Dim n As Integer = "hello".Length             ' Okay: binds to String.Length; the extension property is not consulted
```

在各扩展候选之间，来自最近作用域的候选胜出；当两个候选同等优劣时，先被收集者胜出。不产生任何歧义诊断。这与适用于既有扩展方法回退的合并规则相同。

**决策**：扩展成员绝不会被提升到实例成员之上。提升它们会改变那些声明了被某一作用域内扩展成员所遮蔽的实例成员之既有程序的含义；纯粹的回退保留了每一个既有程序的含义。

### Strict 模式与晚绑定

扩展成员只参与早绑定（early-bound）查找。由此得出两个推论：

- **`Object` 接收者。** 一个 `Object` 接收者绝不参与扩展成员查找。在 `Option Strict Off` 下，对 `Object` 接收者的成员访问仍是晚绑定，并在运行期解析；扩展成员不被考虑。这与扩展方法的既有行为一致。
- **扩展运算符。** 扩展运算符在 `Option Strict On` 与 `Option Strict Off` 下都在编译期解析。在 `Option Strict Off` 下，一个此前降级为晚绑定并在运行期失败的运算符，如今当有某个扩展运算符处于作用域内时会解析到它。一次编译期的解析严格优于一次无声的运行期失败。

## 缺点
[drawbacks]: #drawbacks

- **`Option Strict Off` 下的行为变更。** 一个此前经晚绑定编译——并在运行期失败——的运算符表达式，如今当有某个扩展运算符处于作用域内时会在编译期解析到它。这是一处纠正，但一个依赖了晚绑定路径的程序改变了含义。这一新解析仅在匹配的运算符处于作用域内时才发生，因此该变更限于命名了此类运算符的程序。

- **元数据复杂度。** 扩展成员是从一个三部分组成的元数据形态——容器、分组类型、标记类型——被识别的，而非从方法上的单个属性。该识别比经典扩展方法识别更繁琐，而泛型扩展成员的归约复用了类型推断机制，那是扩展方法处理中最错综复杂的部分。

## 替代方案
[alternatives]: #alternatives

- **通过访问器或 `op_` 名字来识别扩展属性与运算符。** 编译器本可以把一个名为 `get_*`、`set_*` 或 `op_*` 的静态方法当作扩展成员来处理，而无须读取分组类型与标记类型的形态。此方案被否决：扩展块的顶层实现方法不带任何可区分的属性，因此一个手写的、名字如此的静态方法会被误认。元数据形态是唯一可靠的信号。

- **要求对扩展容器命名空间的 `Imports` 是显式且绝对的。** 作用域规则本可以被收紧，但语言已经经由绑定器链解析扩展方法，而扩展成员使用同一规则。无须新的作用域机制。

- **暴露声明扩展属性与运算符的语法。** 在 Visual Basic 源码中声明扩展成员是一个独立的、更庞大的特性，会给语言增添语法。消费侧的价值可以独立成立，而声明侧被刻意排除在范围之外。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 运行时支持

扩展成员不需要任何运行时支持：扩展属性、运算符或方法都被编译为一次普通的静态调用。归约完全是绑定期的变换。

### API 版本化

由于 Visual Basic 只消费这些成员，版本化表面积完全在 C# 一侧。某个库新增一个扩展属性或扩展运算符，会改变哪些 Visual Basic 程序能够针对它编译：一个此前报告错误的用法如今会绑定。某个库移除一个，会把一个此前合法的用法变成错误。这两个方向都不会影响一个不提及该成员的程序的含义。

### 与 C# 的对齐

归约与回退语义与 C# 对齐：扩展成员是位于实例成员之下的回退，它们只从处于作用域内的命名空间被收集，且归约后的扩展成员以接收者的一个普通成员的形式呈现给绑定器。实例成员优先级即 C# 规则，原封不动地予以保留。

## 相关条目
[related]: #related-items

- [Extensions](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/extensions.md) —— 引入扩展成员的 C# 14 提案，包含本规范所消费的这一元数据编码

[extensions]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/extensions.md
