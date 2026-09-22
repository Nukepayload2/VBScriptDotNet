# 只读 ByRef 返回

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-ref-readonly.md)

## 概述
[summary]: #summary

本规范定义 Visual Basic 语言如何**消费**那些以*只读*引用（readonly reference）返回值的成员——即 C# 的 `ref readonly` 返回。当一个被引用的成员（如 `ReadOnlySpan(Of T).Item` 或 `ReadOnlySpan(Of T).GetPinnableReference()`）通过 `ref readonly` 签名返回值时，Visual Basic 把结果绑定为该元素类型的一个值，通过编译器既有的自动解引用（automatic dereference）来读取它，并强制一种只读的写入规则：任何会经由所返回的引用进行存储的写入，要么在编译期被拒绝，要么被降级为一次拷贝，从而只读内存绝不会遭到写入。

此特性不引入任何声明语法。Visual Basic 不能声明按引用返回的成员——无论可变还是只读；它只从元数据消费此类成员。只读规则锚定在被导入成员的元数据签名上——按引用返回上带有一个必需的 `modreq([In])` 自定义修饰符（custom modifier）——并作为只读标志承载在符号上，经由语义模型（semantic model）暴露。

## 动机
[motivation]: #motivation

现代 .NET 使用 `ref readonly` 返回来以不拷贝的方式暴露数据的高性能只读视图。典范例子是 `ReadOnlySpan(Of T)`：

- `ReadOnlySpan(Of T).Item`——索引器——返回 `ref readonly T`。
- `ReadOnlySpan(Of T).GetPinnableReference()` 返回 `ref readonly T`。
- `ReadOnlySpan(Of T).Enumerator.Current` 返回 `ref readonly T`。

ByRef-Like Type Safety 中所规定的 byref 类似支持已经使 `Span(Of T)` 和 `ReadOnlySpan(Of T)` 在 Visual Basic 中可用：构造、`Length`、`Slice` 及其他普通成员能够绑定并运行。索引器是该类型的核心用法，而它此前被拒绝，原因是 `ref readonly` 返回的元数据签名带有一个必需的自定义修饰符 `modreq([In])`，而 Visual Basic 的元数据导入器（importer）会拒绝除 `IsExternalInit` 以外的每一个必需自定义修饰符。

这一缺口被直接观察到：

```vbnet
Imports System

Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Console.WriteLine(s.Length)                ' Okay: outputs 3
' Console.WriteLine(s(0))                  ' Error BC30643: the property is of an unsupported type
' Dim p As Integer = s.GetPinnableReference() ' Error BC30657: the method has an unsupported return type
```

以*可变*引用返回的成员——纯 `ref`，不带 `modreq([In])`——本就完全可消费：读取、通过它赋值、作为 `ByRef` 实参传递，以及类型推断都能工作。缺口恰恰是 `modreq([In])` 形式，即 C# 自 C# 7.2 起用来编码 `ref readonly` 返回的同一机制（[readonly-ref]）。

## 详细设计
[design]: #detailed-design

### 在元数据中的表示

一个返回 `ref readonly T` 的 C# 成员，其发射的返回签名为 `CMOD_REQD([In]) BYREF T`：返回按引用进行，并在 `BYREF` 标记*之前*带有一个必需的 `System.Runtime.InteropServices.InAttribute` 自定义修饰符。C# 编译器自 C# 7.2 起就使用这一编码（[readonly-ref]）。Visual Basic 把 `BYREF` 读作按引用返回，把元素类型 `T` 读作返回类型；那个必需的 `InAttribute` 修饰符就是只读性的标记。

### 元数据导入

Visual Basic 的元数据导入器历史上会拒绝签名带有必需自定义修饰符的任何成员，仅对 init-only setter 所用的 `IsExternalInit` 修饰符有一个狭窄例外。`ref readonly` 返回上的 `modreq([In])` 落入了这一拒绝范围，携带它的成员被报告为不支持：属性或索引器报为 BC30643，方法报为 BC30657。

本规范将 `System.Runtime.InteropServices.InAttribute` 加入导入器视作无害的必需修饰符集合。这一豁免由修饰符的*特性标识（attribute identity）*来识别——即元数据类型 `System.Runtime.InteropServices.InAttribute`——而非仅凭该修饰符是必需的这一事实。没有其他必需修饰符被豁免；导入器关于「已知无害集合之外的必需自定义修饰符即不支持」的规则被保留。特别地，`System.Runtime.InteropServices.OutAttribute` **未**被豁免：C# 仅允许 `Out` 出现在函数指针参数上，而 Visual Basic 不消费函数指针。

这一豁免适用于两条路径：

- **返回路径。** 返回签名带有 `modreq([In])` 的成员被导入为按引用返回，而非被拒绝。
- **参数路径。** 签名带有 `modreq([In])` 的参数——即 C# 为虚、抽象和委托成员的 `in` 参数所发射的形式——被导入而非被拒绝。普通的、非虚的 `in` 参数只在参数行上带有一个 `[In]` 特性而不带签名修饰符；签名修饰符形式出现在虚签名和委托签名上，而这一豁免使那些成员能够从 Visual Basic 被调用。

**决策**：在返回和参数两条路径上一律豁免必需的 `InAttribute` 修饰符，而非只在返回路径上豁免。两条路径共享同一个元数据用处检查；仅返回路径的豁免将需要把一条路径判别符贯穿到共享检查中。一律豁免与 C# 编译器一致，后者自身的元数据导入器在属性签名上已经把必需的 `InAttribute` 修饰符视作无害，而在参数路径上豁免不产生任何代价，因为 `In` 从不指示可写性。

`ref readonly` 返回总是按引用返回。如果元数据把一个必需的 `InAttribute` 修饰符放在一个*并非*按引用的返回上，则该签名不一致；这样的元数据被当作不支持处理，与 C# 编译器针对只读按引用返回的一致性契约相仿。

### 绑定类型与自动解引用

当一个成员按引用返回时，Visual Basic 把该表达式绑定到**元素类型**，而非绑定到一个按引用类型。按引用性作为被绑定表达式的一个属性来承载——该表达式是一个左值（l-value）——而被绑定类型是元素类型。读取该值由代码生成器既有的自动解引用来处理：当一个按引用返回的调用或属性访问被用作值时，编译器发射一次间接加载。这一机制早于本特性，无需改动。

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Console.WriteLine(s(0))                    ' Okay: reads through the ref readonly return, outputs 10
```

在导入豁免之后，索引器能够绑定：被绑定表达式具有类型 `Integer`，且是一个引用为只读的左值。

### 只读标志

语言在符号上跟踪只读性，与按引用性分开。在方法符号和属性符号上定义一个新属性 `ReturnsByRefReadOnly`：

- 当成员按引用返回且返回签名带有一个必需的 `InAttribute` 修饰符时，它为 `True`。
- 对每一个其他成员它为 `False`，包括每一个在 Visual Basic 源中声明的成员——只读按引用返回没有声明语法，因此没有源成员能设置它。

该标志通过语义模型暴露：一个 `ReturnsByRefReadOnly` 为 `True` 的成员，通过相应的符号 API 报告 `RefKind.RefReadOnly` 和 `ReturnsByRefReadonly = True`。这一暴露是分析器和工具所依赖的表面：它们从符号模型读取真正的只读语义，而非从显示的文本推断。

**决策**：在符号上并通过语义模型暴露只读标志。只读状态是关于被导入元数据的一项语义事实——通过它写入会破坏只读内存——而把它从语义模型中隐藏会使本特性对分析器和 IDE 工具不可见。下文所述的显示分层不会削弱这一暴露。

### 写入语义

`ref readonly` 返回是一个**只读左值**：通过它读取是合法的且会自动解引用，但其背后的引用绝不可被写入，因为它所指向的内存可能确实是只读的。语言按接收者的种类对每一次写入进行分类，结果为两种：拒绝编译，或对一个副本进行操作。没有写入路径会通过只读引用进行存储。

这一写入纪律是本特性的一项硬性验收标准：每一条写入路径要么以 BC30068 被拒绝，要么被降级为一次拷贝，从而在该语句之后只读位置上的值保持不变。

#### 直接赋值、复合赋值与 Mid

直接赋值给一个只读左值会在编译期以 **BC30068**（`ERR_LValueRequired`）被拒绝，其消息指出该表达式是一个值，因此不能是赋值的目标。

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
s(0) = 42                                  ' Error BC30068: cannot assign to a readonly byref return
```

该检查在代码生成之前应用于赋值目标。它涵盖每一种会经由目标进行存储的赋值形式：

- **直接赋值**，`s(0) = 42`。
- **复合赋值**，`s(0) += 5`，它展开为一次经由目标的读-改-写。
- **`Mid` 赋值**，`Mid(s(0), 1) = "x"`，其目标是该只读左值。

三者都经由同一个目标调整路径，因此单一检查覆盖了整个经由引用存储的表面。这一拒绝不适用于编译器生成的 `ByRef` 实参写回；如下文所述，对于只读源这些写回会被抑制。

**决策**：复用 BC30068，而非引入新的错误码。BC30068 是 Visual Basic 针对无法赋值的、值形态目标既有的诊断；它是编译器针对只读赋值目标这一族已经产生的诊断，并且与下文所述的显示约定一致——该约定向用户把一个只读左值呈现为一个值。针对 `ReadOnly` 变量的诊断（BC30064，其消息点名一个 `'ReadOnly' variable`）不被使用：这里的目标是一个按引用返回的属性或方法结果，而非一个 `ReadOnly` 变量，那种措辞将不准确。

注意，C# 以 CS8331 拒绝同一操作（[readonly-ref]）；Visual Basic 的值形态诊断及其对 `ByRef` 实参的基于拷贝的处理（下文）是同一条规则在 Visual Basic 中的对应物，以该语言的赋值约定来表达。

#### ByRef 实参

当一个只读左值作为 `ByRef` 实参传递时，编译器把该值**拷贝**进一个临时变量，按引用传递该临时变量，并**丢弃写回**：调用之后，只读位置保持不变。

```vbnet
Sub M(ByRef v As Integer)
    v = 99
End Sub

Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim x As Integer = s(0)                    ' Okay: x = 10
M(s(0))                                    ' Okay: passes a copy; the callee mutates only the copy
Console.WriteLine(s(0))                    ' Okay: still 10; the write-back was discarded
```

这不是一条新规则。Visual Basic 的写出复制（copy-out）约定仅当实参能够接受写入时才写回一个 `ByRef` 实参；传递一个字面量、一个常量，或任何其他的无归属值，本就能编译并静默丢弃被调方的写入。一个只读左值在语义上等价于一个常量位置：**写回被丢弃，就如同按引用给被调方传了一个常量一样。** 被调方确实会执行它的赋值，但它们改的是临时副本，而只读内存从不被写入。

**决策**：接受被丢弃的写回而不发出警告，与对按引用传递的字面量和常量的处理保持一致。这是与 C# 的一处分歧——C# 在 `ref readonly` 返回被用作 `ref` 或 `out` 实参时报告一个错误；Visual Basic 的写出复制文化宁取拷贝而不取拒绝，且该分歧仅限于 Visual Basic 源表面——元数据契约与跨语言互操作保持不变。在被调方写入其副本时发出一个未来警告仍是一个可选方案，但不属于本特性。

#### With 语句与成员链

`With` 语句会捕获它的接收者，从而成员访问只求值接收者一次。如果接收者是一个只读左值，则该捕获**按值进行**：把引用存入一个临时变量会让一个 `With` 块内的写入经由存储进入只读内存。该值捕获服务于成员**读取**——`With o.S(0) : Dim y = .X : End With` 通过被捕获的值读取 `.X`。块内的一次成员**写入**，与一次其基接收者为只读左值的链式赋值是同一种成员写入，它以 BC30068 被拒绝；它不在被捕获的副本上操作，因为静默写入一个随后被丢弃的副本将无法与一次成功的存储区分开来，从而掩盖写入的丢失。

```vbnet
' Suppose o.S(0) returns ref readonly Row, where Row is a mutable structure with a field X.
With o.S(0)                                ' Captures a copy of the element for reads
    Dim y = .X                             ' Okay: reads the captured value
    .X = 5                                 ' Error BC30068: the receiver o.S(0) is a readonly byref return
End With
```

一次直接写出的成员写入——不使用 `With`——其基接收者为只读左值的，以同一诊断被拒绝，因为任何写入都绝不可通过只读引用进行存储：

```vbnet
o.S(0).X = 5                               ' Error BC30068: the base o.S(0) is a readonly byref return
```

**决策**：一个 `With` 块内其接收者为只读左值的成员写入，以 BC30068 被拒绝，与链式赋值形式 `o.S(0).X = 5` 一致。值捕获仅为服务成员读取而保留；它从不是一次成员写入的目标。

用于**读取**的成员链不受影响：`o.S(0).Y` 通过自动解引用读取并给出字段值。

#### 类型推断

`Dim x = s(0)` 推断出被绑定类型——元素类型——并通过引用读取，产生一个值副本。按引用性在绑定时被剥离（被绑定类型是元素类型），因此推断自然地产生一个值：

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim x = s(0)                               ' Okay: x is Integer, a copy of s(0)
x = 42                                     ' Okay: x is a local variable
```

### 显示

针对只读按引用返回的 `ReadOnly` 标记**仅**在纯调试和内部诊断显示格式中被合成。在一个请求引用修饰符但并非调试格式的显示格式中，只读按引用返回被呈现为 `ByRef`——不比一个可变按引用返回更具限制。在 IDE 用于快速信息（quick info）的、不请求引用修饰符的格式中，该返回按值被呈现为元素类型，既没有 `ByRef` 也没有 `ReadOnly` 标记。

这是有意为之。Visual Basic 没有只读按引用返回的源语法，且值形态的用法是常见情形——读取会自动解引用——因此给用户可见的显示加上标注将是噪声。这一显示约定与绑定模型（被绑定类型是元素类型）以及直接赋值诊断（把目标呈现为一个值）一致。

显示的两项性质得到保证：

- 被合成的标记（在显示之处）使用词序 `ByRef ReadOnly`——`ByRef` 在 `ReadOnly` 之前。对于一个显示样式会呈现只读描述符的仅有 getter 的属性，描述符 `ReadOnly` 出现在 `ByRef` 之前，且被合成的标记被抑制，因此显示绝不加倍为 `ReadOnly ByRef ReadOnly`。
- 一个*byref 类似类型*——`ByRef Like Structure`，在 ByRef-Like Type Safety 中规定——的显示保持不变。那个标记标注的是一个必须一眼可识别的**类型**，而一个只读按引用**返回**是作为值来使用的。

工具和分析器从语义模型读取 `RefKind` 和 `ReturnsByRefReadonly`；它们不从显示的文本推断只读性。

### For Each 遍历 byref 类似枚举数

一个 byref 类似枚举数（enumerator），如 `ReadOnlySpan(Of T).Enumerator`，把 `Current` 暴露为一个 `ref readonly` 属性。`For Each` 语句的模式查找要求一个可读的、能够以无参数调用的 `Current` 属性；它并不排除按引用返回的属性。一旦导入豁免使 `Current` 可读，`For Each` 展开就通过自动解引用读取它，因此对 `ReadOnlySpan(Of T)` 的枚举得以工作：

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim sum As Integer = 0
For Each n As Integer In s                 ' Okay: Current is read through the ref readonly return
    sum += n
Next
```

byref 类似枚举数的规则在 ByRef-Like Type Safety 中规定；本规范与之保持一致。

## 健全性
[soundness]: #soundness

激发本特性的不变式是：一次写入绝不可通过只读引用进行存储。该不变式在编译期通过每一次写入的三种互斥结果来强制：

1. **拒绝。** 一次直接经由只读引用存储的写入——直接赋值、复合赋值、`Mid` 赋值，或一次其基接收者为只读左值的成员写入——以 BC30068 被拒绝。
2. **拷贝而不写回。** 一个作为只读左值的 `ByRef` 实参经由一个临时变量按值传递；写回被丢弃，因此被调方只改动它自己的副本。
3. **为读取而拷贝。** 一个作为只读左值的 `With` 接收者被按值捕获为一个副本以服务成员读取；对这样一个接收者的成员写入被拒绝（第 1 项），而不在副本上执行。一个作为只读左值的类型推断值被推断为一个值副本，而它是可自由写入的。

语言中的每一条写入路径都落入这三类之一，且每一类都在一个能够获得目标只读状态的点被检查。由于只读状态来自被导入的元数据签名——按引用返回上的一个必需 `InAttribute` 修饰符——并且在符号上暴露，这些检查在普通编译和脚本提交之间是统一的，二者共享同一个编译器。

反过来的性质——没有任何原本有效的程序变为无效——成立，因为本特性是增量式的。在导入豁免之前，一个返回 `ref readonly` 的成员完全不可用：读取、传递和赋值都被作为不支持的成员被拒绝。豁免之后，读取、按值传递和基于拷贝的用法能够编译，而此前针对整个成员被拒绝的两种行为——通过只读引用进行存储和使用为 `ref`/`out` 实参——仍被拒绝或被降级为拷贝。没有任何原本能编译且行为正确的程序改变其含义。

## 缺点
[drawbacks]: #drawbacks

- **只读跟踪表面。** 只读标志必须承载在符号上，并在每一个赋值和实参绑定点被查阅。一处遗漏的接线将允许一次写入经由只读引用存储——对只读内存的静默破坏。这是本特性风险最高的单一侧面，也是写入矩阵（直接、复合、`Mid`、`ByRef`、`With`、成员链、推断）作为验收标准的原因。

- **静默丢弃对比报错。** 把一个只读左值传给一个 `ByRef` 参数会编译并静默丢弃被调方的写入。这与针对字面量和常量的既有行为相仿，但它更为微妙：被调方确实执行它的赋值，只有写回被丢失。假定被调方写入会生效的调用者可能会感到意外。这一行为对只读源是正确的——被调方不应写入一个只读引用——并且它与该语言的写出复制文化一致，但一个未来警告可能提升清晰度。

- **与 C# 的模型差异。** C# 以错误拒绝同样的经由写入，并断然拒绝把只读按引用值用作 `ref`/`out` 实参。Visual Basic 则进行拷贝。这一分歧仅限于源表面，不改变元数据或跨语言互操作，但两种语言通过不同机制来强制只读不变式。

## 替代方案
[alternatives]: #alternatives

- **把 `ref readonly` 当作可变 `ref`。** 豁免 `InAttribute` 修饰符而不跟踪只读性，会使 `s(0) = 42` 编译并经由只读引用存储。对于确实只读的内存，这会在运行时静默破坏数据。此方案被拒绝：它违反了「一次写入绝不可通过只读引用存储」的不变式。

- **移植完整的 C# ref 安全模型。** 语言可以采用 `scoped`、`UnscopedRef` 和 ref 逃逸分析。这超出了该语言的定位——它消费 byref 类似和只读按引用成员而不声明它们——并且本特性只需要两个状态——可读与不可写——而不需要一个生命周期系统。成本高，收益仅限于该语言所没有的场景。

- **拒绝而不拷贝。** 语言可以在一个只读左值被作为 `ByRef` 实参传递时报告错误，与 C# 一致。这被拒绝：Visual Basic 的写出复制约定按拷贝传递每一个无归属的值，而一个只读左值就是一个无可写归属的值。拷贝行为与针对字面量和常量的既有处理一致。

- **豁免每一个必需的自定义修饰符。** 豁免所有必需修饰符会接受那些签名使用不相关的必需修饰符来表达其他契约的成员，静默导入编译器并不理解的成员。这一豁免被刻意限定于 `InAttribute`，以特性标识识别。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 只读参数识别

Visual Basic 把每一个按引用参数——`in`、`ref` 和 `ref readonly` 一律——映射为 `RefKind.Ref`；它目前不区分一个只读参数与一个可写参数。因此，传给一个 `in` 参数的只读左值，像任何其他 `ByRef` 实参一样被拷贝，写回被丢弃。这总是正确的：只读内存从不被写入。识别一个只读参数并直接传递引用而不拷贝，是一个可能的未来优化；它不属于本特性，因为拷贝是安全的，而该优化需要读取 `[IsReadOnly]`、`[Out]` 和 `[RequiresLocation]` 元数据并把写入跟踪扩展到参数上。

### 版本控制与引用

本特性只读取元数据；它改变哪些成员能编译，而不改变 Visual Basic 如何发射元数据。一个把返回从 `ref` 改为 `ref readonly` 的库，或向一个虚或委托签名添加 `in` 的库，会改变哪些 Visual Basic 程序能够针对它编译。从一个按引用返回上移除 `InAttribute`——使其变为可变——会把一个此前只读的目标变为一个可写的目标。这些是库一侧普通的引用版本控制考量。

### 跨语言对应

`ref readonly` 返回的 C# 对应物在 [readonly-ref] 中规定，该提案是 C# 7.2 的一部分。C# 12 引入了 `ref readonly` *参数*（[ref-readonly-parameters]）；本特性关乎返回，其元数据编码自 C# 7.2 起保持稳定。C# 编译器自身的元数据导入器把必需的 `InAttribute` 修饰符视作无害，本特性使 Visual Basic 与之对齐。

## 测试
[testing]: #testing

此特性由无副作用的内存编译测试来验证：它们不进行网络访问、文件写入、进程启动或注册表访问。

- **导入测试。** 带 `ref readonly` 返回的成员——索引器、`GetPinnableReference`，以及 byref 类似枚举数的 `Current`——被导入且可读；签名使用其他必需修饰符的成员仍为不支持。
- **写入矩阵测试。** 对只读左值的直接赋值、复合赋值和 `Mid` 赋值报告 BC30068；`With` 块内的成员写入，以及基接收者为只读左值的成员链，以 BC30068 被拒绝，而通过此类接收者的读取由值捕获来服务；`ByRef` 实参拷贝该值并丢弃写回（被调方只改动其副本）；类型推断产生一个值副本。该矩阵涵盖可变和只读的 `ByRef` 接收者、基为只读左值的成员链、表达式 lambda，以及编译器的草稿重写路径。
- **符号测试。** 被导入的只读按引用成员报告 `RefKind.RefReadOnly` 和 `ReturnsByRefReadonly = True`；Visual Basic 源成员始终报告 `False`。显示格式仅在纯调试格式中合成 `ByRef ReadOnly`，其词序针对 `ReadOnly` 属性描述符锁定，且一个 byref 类似类型仍显示为 `ByRef Like Structure`。
- **互操作测试。** 对声明 `in` 参数的虚和委托成员的调用，在参数路径豁免之后能够编译；对 `ReadOnlySpan(Of T)` 的 `For Each` 通过只读的 `Current` 进行枚举。

## 相关条目
[related]: #related-items

- [Readonly references](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/readonly-ref.md)
- [Ref readonly parameters](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-12.0/ref-readonly-parameters.md)
- ByRef-Like Type Safety
- [Ref Struct Interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md)
- [Span safety](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md)
- [Restricted types in the Visual Basic specification](https://github.com/dotnet/vblang/blob/main/spec/types.md)

[readonly-ref]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/readonly-ref.md
[ref-readonly-parameters]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-12.0/ref-readonly-parameters.md
