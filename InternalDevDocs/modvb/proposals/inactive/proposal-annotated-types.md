# 注释类型 / 类型变体 / Annotated types / Type flavors

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。让 XML/JSON 等"类型变体"以注释（annotation）的形式标注在声明上，而不是生成新类型：如 `order As <PurchaseOrder>`、`request As {"trade-message"}`，供 IDE 工具据此提供 IntelliSense 与诊断。同一思路也延伸到匿名委托类型（统一到 `Action`/`Func`）、度量单位注释，以及一种更克制、不信任第三方代码进入编译器的"轻量类型提供器"。

## Motivation
[motivation]: #motivation

- VS2008 的 VB 曾提供基于 schema 文件的 XML IntelliSense（编辑器中对 XML 元素/属性给出提示），但在构建 Roslyn 时因时间压力被砍掉；Anthony 一直在琢磨如何优雅地把它找回来。
- 如今 JSON 地位上升，在编辑器里对 JSON 负载做类似 IntelliSense 体验是 no-brainer：给定上下文，当输入 `jsonObject!` 时应能提供补全列表。
- 为了让这种体验成立，需要让 XML/JSON schema 类型能够在代码中表达——哪怕只是作为装饰/注释。
- 更广泛的动机：用一个统一方案服务多种"类型变体"场景（XML schema、JSON schema、匿名委托、度量单位、类型提供器）。

## Detailed design
[design]: #detailed-design

### XML / JSON schema 类型的注释标注

原文示例：

```vb
' Suggestions should be provided after typing `order.<`.
Sub ProcessOrder(order As <PurchaseOrder>)

' Suggestions should be provided after typing `request!`.
Sub ProcessRequest(request As {"trade-message"})
```

要点：

- `order As <PurchaseOrder>`：对 `order` 声明加注 XML schema 类型；输入 `order.` 后应依据 `<PurchaseOrder>` 的 schema 提供补全。
- `request As {"trade-message"}`：对 `request` 声明加注 JSON schema 类型；输入 `request!` 后提供补全。
- 这些标注**不必（且最好不）**实现成它们自己的真实类型，而是对例如 `XElement` 或 `JsonObject` 的一种注释，供工具使用。
- 现有先例：元组类型 `(x As Integer, y As Integer)` 并不生成新类型，而是以注释形式把名字浮现到 `ValueTuple(Of Integer, Integer)` 上。
- C# 用同样手段表示 `dynamic`（运行时仍是 `System.Object`）；ModVB 也会对 `Any` 类型这样做（见 `proposal-any-pseudotype.md`）。

### 匿名委托类型统一到 `Action` / `Func`

"What if"：把简单匿名委托类型与 `Action`/`Func` 统一起来，而不是生成它们自己的匿名类型，例如 `<Function(Object) As Boolean>` 是 `Func(Of Object, Boolean)` 的一种"风味"（flavor）。VB 已经做了一些魔法，在最终转换为具名委托类型时擦除这些类型。这样 VB 用户既能使用 `Action`/`Func`，又能向调用者呈现更有价值的信息：

```vb
' Do you know what the two parameters to the delegate mean?
Function ReplaceNodes(
           nodes As IEnumerable(Of Node),
           replacer As Func(Of Node, Node, Node)
         )
         As Node

' How about now?
Function ReplaceNodes(
           nodes As IEnumerable(Of Node),
           replacer As <Function(original As Node, rewritten As Node) As Node>
         )
         As Node
```

### 度量单位作为注释

另一个 "What if"：单位是否可作为数值变量上的注释被跟踪，而不是作为真实类型？（与 `proposal-units-of-measure.md` 相互引用。）

### 轻量"类型提供器"

Erik Meijer（VB9 的重要影响者）曾告诉 Anthony：当年 XML IntelliSense 的做法本质上就是"type provider"（至少足以据此申请专利）。Anthony 想要的是**不是** F# 那种编译期代码生成/注入（不太信任让第三方代码在编译器内部运行）的方案，而是更克制的形式：开发者可以在代码中对任何类型引用附加任意元数据，工具据此提供补全与诊断。他还没找到直观的语法，但想象中的用法：

```vb
' Annotated with the key for a connection string.
Let db = New SqlDbProxy(For "AdventureWorks")()

' Completions could be provided by an editor extension that
' can connect to the database locally.
For Each p In db.Tables!Products
   Where p!Price(As Decimal) >= 100

' Remember that thing I said about `Default` methods and invocables?
db.StoredProcedures!sp_DeleteOldRecords()

Let api = New RestProxy(For "api.github.com")()

' REST API URL completions.
? api.HttpGet("/repos/...")

' As easy as late-binding but not actually.
' Windows Management Instrumentation.
Let wmi = New WmiProxy(For "\.\root\cimv2")()
wmi.Classes!Win32_Process.Methods!Create(commandLine:="notepad.exe",
                                         returnValue:=Out code As UInt32)
If code = 0 Then
    ...
```

核心场景：让工具提供"类型的安全(er)体验"，同时保留晚期绑定对象模型的轻量足迹，而**不**在运行时付出真正的 Reflection/DLR 机器成本。Anthony 在原文列出了 6 个场景，写完后又想出 2-3 个，认为足以支撑"寻找一个统一方案"。

## Drawbacks
[drawbacks]: #drawbacks

- "类型变体"是注释而非真实类型，会削弱编译期类型检查：编译器层面无法对 `order.<...>` 或 `request!...` 做真正的静态验证，只能靠工具层。
- 统一方案覆盖面很广（XML/JSON schema、委托、单位、类型提供器），设计上极易失控。
- "不信任第三方代码在编译器内运行"的约束，会限制类型提供器的实现深度。

## Alternatives
[alternatives]: #alternatives

- 只为 XML 恢复旧式 schema IntelliSense，不引入通用"注释类型"。
- 仅为 JSON 做独立的 schema 类型（`proposal-json-literals.md` / `proposal-xml-schema-types.md` 方向）。
- 用分析器 / 编辑器扩展读取元数据，不改变语言语法。

## Unresolved questions
[unresolved]: #unresolved-questions

- 注释语法各异：`<PurchaseOrder>`、`{"trade-message"}`、`<Function(...) As ...>` 各自独立，是否存在统一语法？
- `Any` 用 `Object` 表示（与 C# 的 `dynamic` 类似），这一表示的细节（转换、可空性、晚期绑定）如何界定？
- 匿名委托统一到 `Action`/`Func` 后，"风味"信息在 API 边界、重载决议、序列化中如何存活？
- 度量单位作为数值变量的注释，是否与真实类型方案互斥，还是分层？
- 类型提供器"任意元数据"的语法仍未落地（原文明确"haven't landed on an intuitive syntax"）。
- 这些类型变体在泛型、可空性、晚期绑定场景中的交互未定义。
