# Key Fields & Properties, and Auto-Constructors / `Key` 字段与属性、自动构造函数

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

引入 `Key` 修饰符。用在类字段上时，编译器自动生成对应的构造函数参数与字段赋值；用在结构体的只读属性上时，编译器自动生成 `Sub New`、`Equals` 与 `GetHashCode`。从而消除大量手写样板代码。

## Motivation
[motivation]: #motivation

- 依赖注入的经典样板是"声明字段 + 写构造函数 + 逐个赋值"，三处信息重复，Anthony 注释"我再也不用敲的代码"即指此。
- 值类型（如货币金额 `Money`）的不可变值对象通常需要手写 `Equals`/`GetHashCode`/构造函数，冗长且易错。`Key` 让编译器一次性生成这些成员。

## Detailed design
[design]: #detailed-design

### 类字段：自动构造函数

在类中用 `Key` 修饰字段，编译器自动生成"同名、同类型"的构造函数参数并完成赋值。多个 `Key` 字段可用逗号并列在一行声明。被注释掉的部分（`''`）就是编译器将要替你生成的代码：

```vb
Class AppointmentBookingService
    
    Private Key CalendarService As ICalendarService,
                PaymentService As IPaymentService,
                EmailService As IEmailService

    ' Look at this code I'm never going to have to type again!
    ''Public Sub New(calendarService As ICalendarService,
    ''               paymentService As IPaymentService,
    ''               emailService As IEmailService)
    ''               
    ''    Me.CalendarService = calendarService
    ''    Me.PaymentService = paymentService
    ''    Me.EmailService = emailService
    ''End Sub

End Class
```

即 `Private Key CalendarService As ICalendarService` 等价于：声明了该字段，同时生成构造函数形参 `calendarService` 并执行 `Me.CalendarService = calendarService`。

### 结构体只读属性：自动 `Sub New` / `Equals` / `GetHashCode`

在结构体的只读属性上标记 `Key`，编译器自动生成 `Sub New(value, currency)`、`Equals` 与 `GetHashCode`：

```vb
Structure Money
    Implements IEquatable(Of Money)
    
    Public Key ReadOnly Property Value As Decimal
    
    Public Key ReadOnly Property Currency As Currency
    
    ' Sub New(value, currency), Equals, and GetHashCode provided by compiler.
    
End Structure
```

此处的 `Implements IEquatable(Of Money)` 与编译器生成的 `Equals` 相互配合，`Money` 作为不可变值对象可直接用于字典键、相等比较等场景。

## Drawbacks
[drawbacks]: #drawbacks

- 构造函数参数名、顺序、可空性由编译器推断，使用者无法精细控制；需要自定义构造函数逻辑（校验、额外参数）时可能与自动生成的 `Sub New` 冲突。
- 隐式生成的 `Equals`/`GetHashCode` 对序列化、反射、动态代理（如 ORM、Mock）工具链的可观察行为可能造成影响。
- `Key` 在 VB 中已是一个关键字（用于匿名类型/查询中的键属性），复用同名关键字可能带来理解与解析上的负担。

## Alternatives
[alternatives]: #alternatives

- 保持手写构造函数与 `Equals`/`GetHashCode`：样板代码继续存在，且值对象相等性容易因成员增减而失配。
- 引入类似主构造函数（primary constructor）的语法，但它与"字段即参数"的现有声明方式风格差异较大。
- 值类型自动生成 `Equals`/`GetHashCode` 也可以只针对"标记了 `Key` 的属性"进行，避免对一切结构体无差别生效。

## Unresolved questions
[unresolved]: #unresolved-questions

- 自动生成的构造函数与使用者显式声明的 `Sub New` 如何共存（互斥？还是仅补充缺失参数？）。
- `Key` 与现有 `Key` 关键字（匿名类型键属性、查询 `Key` 子句）的语法冲突与消歧方式，原文未展开。
- 结构体中 `Key ReadOnly Property` 的组合规则：是否允许多个、是否要求全部只读、字段可否也用于结构体。
- 自动生成构造函数的参数顺序（按字段声明顺序？）与可选参数规则，原文未说明。
- 类中 `Key` 字段是否只支持 `Private`，是否支持 `Get`/`Set` 全属性形式的注入。
