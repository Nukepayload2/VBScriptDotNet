# Visual Basic 脚本方言

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-scripting-dialect.md)

## 概述
[summary]: #summary

本规范定义 Visual Basic 脚本方言（scripting dialect）的**声明与提交模型**——脚本文件与交互窗口（interactive window）所使用的方言。该方言没有自己专属的声明语法。在 `SourceCodeKind.Script` 之下，编译器合成一个**脚本类（script class）**，并把四种顶层（top-level）声明形式映射为其成员：

| 顶层形式 | 映射为 |
|---|---|
| `Dim x = …` | 脚本类的一个**字段** |
| `Sub` / `Function` | 脚本类的一个成员——一个**实例成员**，若声明为 `Shared` 则是一个 **`Shared` 成员** |
| `Class` / `Module` / `Structure` / `Interface` / `Enum` / `Delegate` | 脚本类的一个**嵌套类型** |
| 一条可执行语句 | 脚本类**实例初始化器**的一项条目 |

脚本类是合成的，从不在源码中书写。它的名称是 `ScriptClassName` 编译选项，默认值为 `Script`；在一次提交（submission）中，宿主把它替换为 `Submission#N`，这个名字无法在源码中拼写，因为 `#` 是一个类型字符。

每次提交都是一次独立的编译。提交通过 `PreviousScriptCompilation` 串成链，而一次提交中的名字查找会搜索此前每一次提交的脚本类以及宿主对象类型。状态之所以能跨提交保留，是因为顶层 `Dim` 是字段而非局部变量。入口点是作为一个异步初始化器 `<Initialize>` 合成的，它返回 `Task(Of T)`；非提交的脚本编译用一个同步的 `<Main>` 包装它，而一次提交用一个 `<Factory>` 包装它——它是一个 `Private Shared Function`，其返回类型就是初始化器的返回类型。

顶层 `Await` 与顶层 `AddHandler` / `RemoveHandler` 不需要语言扩展：初始化器就是一个普通的 Async 方法，顶层语句就是它的方法体，因此适用的就是普通规则。

声明与状态模型的 C# 对应物是 **C# 脚本方言**（`.csx`，共享的 `Script<T>` API）；文件执行结果的对应物是 **C# 顶层语句**；交互打印行为的对应物是 **C# interactive**。凡下文出现 C# 比较之处，本规范都会指明它指的是这三个侧面中的哪一个。

## 动机
[motivation]: #motivation

普通 Visual Basic 要求代码位于 `Module` 或 `Class` 之内。脚本或交互会话（interactive session）要求的恰恰相反：在顶层键入的声明必须立即可用，并且在下一次提交中仍须可用。C# 语言设计记录直接说明了这一要求——"Submission system allows state preservation across evaluations"（[LDM-2020-01-22][ldm-2020-01-22]）——并把脚本场景与简单程序、顶层函数并列。Visual Basic 设计记录把同一场景记为议题 [#102](https://github.com/dotnet/vblang/issues/102)，"Support Top-Level Statements in a Single Entry-Point File"，其中选定的方向是调和标准方言与脚本方言，而不是让它们逐渐分离（[vbldm-2017-12-06][vbldm-2017-12-06]）。

本规范就是 Visual Basic 侧的这种调和。该方言没有为任何事物增加第二种声明方式：

- 四种顶层形式保留其普通的拼写与含义；改变的只是它们的容器。`Dim` 本来就是一个变量成员的合法修饰符（[type-members][vblang-type-members]），而局部变量被定义为等同于 "declared in the same way" 的实例变量（[statements][vblang-statements]）。因此顶层 `Dim` 与成员声明是同一种构造，而不是新构造。
- 编译环境被允许提供入口点："The compilation environment may also create an entry point method if one does not exist"（[source-files-and-namespaces][vblang-source-files-and-namespaces]）。
- 顶层 `Await` 不是对任何规则的放宽。它是 Async 方法内部的 `Await`，因为顶层语句就是合成的异步初始化器的方法体。

有两个属性把这一方言同普通语言区别开来，必须明确陈述而不能靠推断。第一，`Imports` 语句跨提交累积，这是对 `Imports` 语句的作用域 "does not include other source files" 这一规则的明确偏离（[source-files-and-namespaces][vblang-source-files-and-namespaces]）。第二，脚本方言默认选取宽松的编译选项——`Option Strict Off` 搭配 `Option Infer On` 与 `Option Explicit On`——这正是该语言宽松类型、晚绑定（late binding）这一历史血统的具体承载。

## 详细设计
[design]: #detailed-design

### 脚本类

#### 生成与命名

当一棵语法树的源代码种类（source-code kind）不是 `Regular` 时，声明表构造器不为该编译单元创建隐式类；它创建一个**脚本类**。声明种类对于提交是 `Submission`，对于其他任何脚本编译都是 `Script`；这一差别是有意义的，并在本规范中一路使用（见[脚本类与提交类](#script-classes-and-submission-classes)）。修饰符是固定的：`Friend`、`Partial` 与 `NotInheritable`。`Shared` 是成员修饰符而不是类型修饰符，因此类没有 `Shared` 形式。脚本类是一个普通的类而不是标准模块：标准模块的成员隐式为 `Shared`，且模块永不能被实例化（[types][vblang-types]）；而脚本类是被实例化的——由提交的 `<Factory>` 实例化一次，由脚本文件的 `<Main>` 实例化一次——并且它的顶层成员除非声明为 `Shared`，否则都是实例成员。不过，脚本类是可以声明扩展方法的两种类型之一，另一种是标准模块：顶层 `Shared Function` 上的 `<Extension>` 按普通方式声明一个扩展方法，而同一特性用在嵌套类型的成员上会被 BC36551 拒绝。

类的名称是 `ScriptClassName` 编译选项的值，其默认值为 `Script`。该名称可以带点；每个前置成分成为一个包裹该类的命名空间。这个名称是编译选项而不是源码声明：`Script` 是一个普通标识符，可以从源码中引用，而提交的名称是 `Submission#N`，无法在源码中拼写。

**决策**：提交把选项值替换为 `Submission#N`，以使提交链在元数据与诊断（diagnostic）输出中可区分。替换由宿主执行，宿主通过同一个选项传入生成的名称。不是提交的脚本编译原样使用选项值。

#### 声明模型

脚本编译的每一个顶层成员都成为脚本类的一个成员。这四种形式上的映射是穷尽的。

**顶层 `Dim` 是字段。** 对于块上下文为编译单元的变量声明，解析器发射一棵 `FieldDeclaration`，与它在类体内所做的完全一致。该字段创建在脚本类上，因此它具有实例状态并跨提交存活。它不是局部变量，其作用域也不局限于某个方法体。

**没有 `As` 子句的顶层 `Dim` 由 `Option Infer` 定类型，与局部变量一致。** 当声明没有 `As` 子句时，字段的静态类型是在同样的 `Option Infer` / `Option Strict` 设置下、由初始化表达式推断出的类型——与把同样的语句放进普通方法体的局部变量所得到的类型一致——而不是 `Object`。于是 `Dim x = 5` 得到一个 `Integer` 字段，`Dim s = "hi"` 得到一个 `String` 字段，随后的 `x.Length` 是早绑定引用（在 `Integer` 上是编译期错误，而不是只有运行期才失败的晚期绑定调用）。推断受同样的选项支配：`Option Infer Off` 时字段仍为 `Object`（与从前一致），显式 `As` 子句原样使用，而普通（非脚本）类的字段不受影响，因为其语法本就要求写类型。推断出的类型也跨提交保持——某次提交里声明的字段，在其后的提交被读取时仍按其推断类型已知。

顶层 `Dim` 的初始化器回头引用该字段自身时——直接引用（`Dim a = a`），或经由一串这样的声明绕回它（写了 `Dim a = b` 又写 `Dim b = a`）——类型无从推断，编译器报 `BC30980`（`Type of '{0}' cannot be inferred from an expression containing '{0}'.`），也就是局部变量推断那条路径已经在用的诊断，而不是悄悄把字段定成 `Object`。一条报告覆盖一个相互引用的循环，报在该循环里第一个被计算类型的那个成员上；两个互不相干的循环产出两条报告。同一个程序在 C# 脚本方言里就是这个形状。要留意这条以 `Option Infer On` 为前提：把它关掉就没有任何东西在被推断，那些声明仍走上文所说的静默 `Object` 退路。

什么算类型错误，刻意跟随 Visual Basic 自身的局部变量语义，而不是 C# 交互式 `var`。C# 无条件拒绝 `var b = 1; b = "abc";`（`CS0029`）。Visual Basic 不这样：在 `Option Strict Off`（默认）下，`Integer` 可以经隐式窄化转换接受一个 `String`，所以 `Dim x = 5` 后 `x = "abc"` 编译通过、只有运行期才失败——与同样两句写在方法体里一模一样；在 `Option Strict On` 下该赋值就是普通的 `BC30512`。脚本方言不引入独立的赋值规则，因为那会悄悄覆盖同一份源文件的全局 `Option Strict` 语义。

**顶层 `Sub` 与 `Function` 是实例成员，除非声明为 `Shared`。** 顶层方法在脚本类上创建，规则与在类体中声明的方法相同。实例方法是持有顶层字段的那个类型的成员，所以它的方法体可以直接读写顶层 `Dim` 变量，顶层方法之间也可以直接互相调用。`Shared` 方法没有隐式接收者（implicit receiver），也没有这种对实例状态的访问。

**顶层类型是嵌套类型。** 顶层的 `Class`、`Module`、`Structure`、`Interface`、`Enum` 或 `Delegate` 声明成为脚本类的一个嵌套类型。

**顶层可执行语句构成实例初始化器。** 每条可执行语句被记录为初始化器的一项条目。合成的初始化器方法体是字段初始化器与全局语句按源码顺序构成的序列，其后跟退出标签。顶层语句不在脚本类的构造函数中运行；它们在初始化器方法中运行。

**共享字段的初始化是惰性的，且与主体没有先后保证。** 声明为 `Shared` 的顶层 `Dim` 是脚本类的一个共享字段，而不是实例初始化器的一项条目。它的初始化器在该脚本类的共享构造函数中运行，而公共语言运行时是在第一次触碰到该类任一共享成员时才触发共享构造函数——未必在第一条顶层语句执行之前。因此本方言不承诺"共享字段在主体开始时已初始化完毕"：依赖某先后顺序的代码，必须显式触碰该字段（或某个共享方法）来建立这一顺序。这不是脚本独有的设定——普通的 Visual Basic 类有同样的 before-field-init 行为，C# 脚本的 `static` 字段也受同一条运行时规则支配——所以这里既不脱离普通 Visual Basic，也不脱离 C# 脚本方言。

**作用域。** 顶层声明的作用域是该编译单元的顶层代码以及链中其后的各次提交。它不延伸到嵌套类型的方法体之内：在嵌套类型内部没有指向脚本类实例的隐式接收者，显式的 `Me` 指的是嵌套类型自身的实例，而在一次提交中脚本类的名称根本无法书写。

下面这个脚本文件演练了全部四种形式：

```vbnet
Imports System.Threading.Tasks

Class Raiser                                  ' a nested type of the script class
    Event SomethingHappened As EventHandler
    Sub Raise()
        RaiseEvent SomethingHappened(Me, EventArgs.Empty)
    End Sub
End Class

Dim counter As Integer = 0                    ' a field of the script class
Dim raiser As New Raiser

Function AddOne(value As Integer) As Integer  ' an instance member of the script class
    Return value + 1
End Function

AddHandler raiser.SomethingHappened,          ' an executable statement: part of the initializer
    Sub(sender As Object, e As EventArgs) counter += 1

Dim value = Await Task.FromResult(13)         ' top-level Await in the async initializer
Console.WriteLine(AddOne(value))              ' 14
raiser.Raise()
Console.WriteLine(counter)                    ' 1
```

#### 脚本类与提交类 <a id="script-classes-and-submission-classes"></a>

脚本类二择其一，且这一差别是可观察的。严格意义的**脚本类**具有 `DeclarationKind.Script` 与 `TypeKind.Class`；**提交类**具有 `DeclarationKind.Submission` 与 `TypeKind.Submission`。两者都报告 `IsScriptClass`。有三个行为按此种类分派，并在下面分别规定：

- 跨提交可见性只对提交类存在；
- 合成的入口点对于脚本类是 `<Main>`，对于提交类是 `<Factory>`；
- `WithEvents` 挂钩构造函数会为非提交的脚本类合成（这遵循普通类的路径），不会为提交类合成。

**决策**：保留而非抹平这一种类区分，因为非提交的脚本编译是一次带有合成容器的普通编译，而提交除此之外还是保留状态之链上的一环。把提交的语义当作一般的脚本语义来呈现将是不正确的。

### 提交

#### 提交链

提交是这样一次编译：其脚本编译信息携带着对上一次提交的引用、结果类型以及宿主对象类型。当宿主不指定结果类型时，默认为 `System.Object`。一次提交可由多于一棵语法树构成；提交结果由最后一棵树决定。

这条链是**会话作用域的**：只要宿主进程持有它就存活，进程退出时即被丢弃。这条链**没有上界**。**决策**：不对链的长度设置任何限制，因为任何限制都会使「状态能跨求值保留」变得不可预测；一个链环的代价是实现细节，本规范对此不作承诺。

#### 跨提交可见性

在当前编译中失败的名字查找，会依次搜索此前每一次提交的脚本类——从最新到最旧——然后搜索宿主对象类型。跨提交的**成员查找**只覆盖链上各脚本类的成员与宿主对象的成员；它不搜索此前某次提交所声明的任何其他内容。`Imports` 的累积是一个独立的机制，在[跨提交的 `Imports`](#imports-across-submissions)一节下描述。

```text
> Imports System.Text
> Dim sb = New StringBuilder("abc")
> Console.WriteLine(sb.Length)
3
```

`sb` 是第一次提交的脚本类上的一个字段；第二次提交把它视作某次先前提交的脚本类的一个成员。该查找覆盖先前某个脚本类的每一个成员，包括顶层类型声明所产生的嵌套类型，因此在一次提交中声明的类型可以在其后某次提交中构造和使用。

**决策**：跨提交查找限于脚本类成员与宿主对象成员。扩大会让一次提交的私有实现细节暴露给无关的后续代码。

#### 状态保留

每次提交的脚本类携带该提交的状态。对某次先前提交成员的引用，绑定为对相应先前提交实例的引用，宿主把先前提交实例的数组传给新提交的脚本类构造函数。正是这一机制使上文的 `sb` 在两次提交中引用同一个对象。该机制的形状——构造函数参数、逐次提交的合成字段、宿主对象字段的名称——是实现细节，不属于本规范的契约。

### 入口点

#### 合成的初始化器

每个脚本类都得到一个名为 `<Initialize>` 的合成初始化器。它是 `Friend`，它是**异步的**，其返回类型为 `Task(Of T)`，其中 `T` 是提交的结果类型，未指定结果类型时为 `System.Object`。它的方法体是上文所述的实例初始化器序列，其后跟一条返回语句。

初始化器就是顶层 `Await` 得以成立的原因：顶层语句是它的方法体，所以它们在一个 Async 方法中执行，`Await` 受 Async 方法的普通规则支配。既没有脚本专属的 `Await` 语法，也没有对 Async 规则的放宽。在声明而未带 `Async` 的顶层 `Sub` 或 `Function` 的方法体内，Async 上下文就是该方法自身的上下文：`Await` 不处于 Async 上下文，不会被解析为 Await 语句，并被适用于非 Async 方法的普通规则拒绝。

#### 脚本结果

提交所产生的值称为**脚本结果（script result）**。它由合成的初始化器的返回语句承载，而不是由 `Function Main` 承载。产生它的规则取决于结果类型：

- **Object 提交。** 当结果类型是 `System.Object` 时，提交的最后一个表达式的值就是脚本结果。最后一个表达式被隐式转换为结果类型，而一个不是顶层代码末条语句的裸表达式是错误（BC31003）。
- **带类型提交。** 当结果类型是任何其他类型时，只有带表达式的显式 `Return` 语句才产生脚本结果。末尾表达式被绑定后丢弃；它不会被转换为结果类型。

**决策**：带类型提交遵循 `Function Main` 入口点的 `Return` 语义，而不是 C# 脚本方言的末尾表达式语义。这使文件执行结果与 C# 顶层语句对齐——在后者中 `return n` 是结果的唯一来源——并有意与共享的 `Script<T>` 脚本 API 分离，在后者中末尾表达式无条件被转换为结果类型。这一分歧是语言的属性而非 API 的属性：同一个 `Script<T>.ContinueWith` 调用在 C# 中由末尾表达式产生结果，在 Visual Basic 中则要求显式的 `Return`。

初始化器中不带表达式的 `Return` 语句产生结果类型的默认值：

| 结果类型 | 裸 `Return` 的值 |
|---|---|
| 具有 `Bad` 与 `Nothing` 之外的常量值判别式、且不包括 `System.String` 的类型 | 该类型的默认常量——`Integer` 为 `0`，`Boolean` 为 `False` |
| 其他每种类型，包括 `System.String`、`Object` 与引用类型 | `Nothing` |

明确点出 `System.String`，是因为它是既有常量值判别式又是引用类型的类型；该规则对它走 `Nothing` 分支。

#### `<Main>` 与 `<Factory>`

脚本类的合成入口点按该类的种类选取。

非提交的脚本编译得到 `<Main>`，一个不带参数的 `Private Shared Sub <Main>()`。它的方法体创建脚本类的一个实例并同步等待 `<Initialize>`：取得所返回任务的 awaiter，并对其调用 `GetResult`。当脚本文件作为脚本编译被编译时产生 `<Main>`。作为脚本执行的 `.vbx` 文件是一次提交，因此走 `<Factory>`；这两条路径的区分依据是编译是否是一次提交，而不是文件扩展名。

提交得到 `<Factory>`，一个 `Private Shared Function <Factory>(submissionArray As Object())`，其返回类型是**初始化器的返回类型**——即 `Task(Of T)`，不是 `T`。它的方法体从提交数组创建提交类实例，并返回对其调用 `<Initialize>` 的结果。工厂方法体与工厂返回类型是一致的：工厂返回的正是初始化器返回的东西。宿主把工厂作为 `Func(Of Object(), Task(Of T))` 类型的委托来消费。

**决策**：提交暴露工厂而不是一个同步的 `Sub` 入口点，因为宿主必须自己 Await 初始化器，并且必须提供先前各提交的状态；非提交的脚本编译没有先前状态，因而承受得起阻塞。

**契约地位。** `<Initialize>`、`<Main>`、`<Factory>` 与 `Submission#N` 是编译器生成的名称。它们不是公共契约，也不能从源代码中引用；点名它们的程序无法通过编译。不过，它们在 C# 脚本实现中拼写完全相同，因此识别生成脚本入口点的工具——反编译器、调试器、脚本宿主——可以依赖这一拼写跨语言保持稳定。`ScriptClassName` 选项不同：它的值是编译选项的一部分，而默认值 `Script` 是源代码可以使用的普通标识符。

### 顶层 `Await`

顶层 `Await` 可用，是因为顶层语句是异步初始化器的方法体，别无其他原因。模型中有三处使它成立，而这三处都是普通的：

1. 在脚本中，解析器把编译单元当作位于某个 Async 方法或 lambda 之内，所以顶层的 `Await` 被解析为 Await 语句而不是表达式错误。
2. 绑定器把脚本类的字段或属性初始化器当作 Async 上下文，所以字段初始化器中的 `Await` 被接受。
3. 初始化器是异步的，所以该 await 在运行时与编译期同样合法。

C# 顶层语句的设计走了不同的路线：合成入口点的签名在 `Task`、`void` 与 `int` 之间切换，取决于顶层代码是否使用 `await` 与 `return`。Visual Basic 则把初始化器固定为 `Task(Of T)`，并在 `<Main>` 中执行同步等待，因此 Async 层面是统一的。

### 顶层 `AddHandler` 与 `RemoveHandler`

在脚本的顶层，`AddHandler` 与 `RemoveHandler` 被解析为语句，而不是事件访问器声明。因此它们是普通的可执行语句，与其他顶层语句一起成为实例初始化器的条目。这是在脚本的编译单元顶层所作的一种解析抉择；在类体内部，同样的关键字仍然开始一个访问器声明。

```vbnet
Dim raiser As New Raiser
AddHandler raiser.SomethingHappened, AddressOf OnSomething   ' an ordinary statement
```

`WithEvents` 字段所执行的事件挂钩是一个独立的机制，按脚本类的种类分派，如[脚本类与提交类](#script-classes-and-submission-classes)一节所述。

### 跨提交的 `Imports` <a id="imports-across-submissions"></a>

#### 对 `Imports` 作用域规则的明确偏离

语言规范规定，`Imports` 语句的作用域 "specifically does not include other `Imports` statements, nor does it include other source files"（[source-files-and-namespaces][vblang-source-files-and-namespaces]）。每次提交是一个源文件。然而在脚本方言中，一次提交的 imports 在链上其后每一次提交中仍然有效：写一次 `Imports System.Text` 就让 `StringBuilder` 在会话剩余部分可用。

**决策**：这一偏离是有意的，且限于脚本方言。它的理由是提交模型的状态保留要求；没有它，最常见的交互动作——导入一个命名空间一次——连一次提交都撑不过。普通编译不受影响：其 `Imports` 作用域仍是文件局部的。

该机制在宿主侧而非编译器侧。编译器只消费 `GlobalImports` 编译选项；宿主通过收集整条链的 imports 为每次提交计算出该选项。

#### 规范化

累积的子句按固定顺序收集：先是当前提交的宿主提供的 imports，然后是从最旧到最新的每一次先前提交的子句。收集既取先前提交的编译选项，也取其语法树的 `Imports` 子句。文本相同的子句在收集期间以不区分大小写的比较被移除，这与 Visual Basic 标识符的大小写不敏感相称。余下的子句随后作为项目级 imports 被绑定，从而适用普通的导入规则：

- **成员导入**（`Imports System.Text`）按绑定后的符号去重。解析为某个已存在导入的子句被忽略；项目级导入不报告重复导入诊断。`Global` 限定符在绑定时被解析，因此 `Imports Global.System.Text` 与 `Imports System.Text` 表示同一个导入并被去重。
- **别名导入**（`Imports R = System.Text`）按别名名称去重，比较时不区分大小写。别名名称相同而目标不同的两个子句报告 BC30572，"Alias '{0}' is already declared"；较早的子句胜出。项目级导入诊断不携带源位置，因此落败的子句在消息中以其文本识别，而不是以位置识别。
- **XML 命名空间导入**（`Imports <xmlns:db="…">`）按前缀去重。定义同一前缀的两个子句报告 BC30573，"XML namespace prefix '{0}' is already declared"；较早的子句胜出。与别名导入一样，该诊断不携带源位置，并在其消息中给出子句文本。这是一个 XML 命名空间 "can only be defined once for a particular set of imports" 这条规则的累积形式（[source-files-and-namespaces][vblang-source-files-and-namespaces]）。
- **由当前提交遮蔽。** 写在当前提交语法树中的子句在嵌套于持有累积子句的那个绑定器之内的一个绑定器中被绑定，所以在当前提交的树中声明的别名或 XML 前缀会遮蔽从早先某次提交累积而来的同名别名或前缀，且不产生诊断。通过当前提交的选项提供的 imports 不是文件级的：它们加入累积的项目级集合，因此与早前某次提交的子句相冲突时会被报告而不是被遮蔽。成员导入没有遮蔽行为；它们只是累积。

**决策**：两个累积子句之间的冲突作为诊断报告，而不是静默解决，并且较早的子句胜出。累积子句与写在当前提交语法树中的子句之间的冲突，按有利于当前提交的方向解决，因为当前提交的作者看得见它可以改它。

**失败。** 累积子句的解析失败作为诊断报告，不作为异常向上传播。**决策**：累积子句是不可信输入——它写于早先某次提交，由宿主重放——所以累积路径把畸形子句当作诊断条件而不是致命错误。

### 编译选项

脚本方言默认选取它的编译选项：

| 选项 | 值 | 效果 |
|---|---|---|
| `Option Strict` | `Off` | 允许宽松转换与晚绑定；该语言晚绑定、宽松类型的行为可用。 |
| `Option Infer` | `On` | `Dim x = …` 推断一个类型，而不是把变量定为 `Object`。 |
| `Option Explicit` | `On` | 每个变量使用前必须声明。 |

**决策**：这些默认值是该方言特质的具体承载。`Option Strict Off` 是启用晚绑定的开关；`Option Infer On` 使推断在顶层声明中可用；`Option Explicit On` 保留了使顶层 `Dim` 有意义的声明要求。正是这一组合让脚本可以写 `Dim value = Await Task.FromResult(13)`，并在下一条语句中把 `value` 当作 `Integer` 使用。

### 脚本专属的限制与诊断

脚本方言引入一小族诊断。它们是有层次的：`Namespace` 在解析期间报告；其余的限制在解析之后、绑定期以及容纳类型的成员构建期报告。

| 诊断码 | 诊断 | 条件 | C# 对应物 |
|---|---|---|---|
| BC36965 | `ERR_NamespaceNotAllowedInScript` | 脚本中的 `Namespace` 声明。声明表仍会处理该声明，以便其后的错误被报告得更准确。 | CS7021，名称与含义相同 |
| BC36966 | `ERR_KeywordNotAllowedInScript` | 脚本类**任何位置**的**显式** `Me`、`MyBase` 或 `MyClass`——既包括顶层脚本代码（顶层语句、顶层字段与属性的初始化器，以及在二者之中书写的任何 lambda 或查询表达式），也包括脚本类所声明成员的成员体；不在脚本初始化器之内的顶层脚本代码中的 `Return`；顶层脚本代码中的 `Yield`。隐式的 `Me` 引用是允许的。在 `Shared` 成员中，先报告的是普通的共享上下文诊断（显式关键字为 BC30043，隐式引用为 BC30369）。 | CS0027（`this`）与 CS1512（`base`），在脚本类里任何位置都被拒绝，成员体也算；C# 同样是先判静态上下文、后判脚本门；`Yield` 对应 CS7020 |
| BC31003 | `ERR_UnexpectedExpressionStatement` | 不是编译单元顶层代码末条语句的裸表达式语句。 | 相类似的通用诊断是 CS0201；C# 脚本方言对非末尾表达式没有单独的诊断 |
| BC30545 | `ERR_PropertyAccessIgnored` | 用作语句的属性访问或晚绑定属性组。对于编译单元顶层代码的末条语句，该诊断被抑制。 | 无 |
| BC36964 | `ERR_ReferenceDirectiveOnlyAllowedInScripts` | 普通编译中的 `#R`。 | CS7011 |
| BC36967 | `ERR_LoadDirectiveOnlyAllowedInScripts` | 普通编译中的 `#Load`。 | CS8097 |
| BC37341 | `ERR_BadAwaitInSharedInitializer` | 共享字段或属性初始化器中的 `Await`。共享初始化器在共享构造函数中运行，而共享构造函数是同步的；实例初始化器才是异步的那个，所以在其中允许 `Await`。该诊断只在脚本类中才可达，因为只有当字段或属性的容纳类型是脚本类时它才被当作 Async 上下文。 | CS8100，条件相同；其消息说的是 "static script variable initializer" |
| BC37342 | `ERR_SubmissionCannotDeclareInstanceConstructor` | 在脚本类中声明的实例构造函数。两类脚本类都只有一个实例构造函数槽位：提交类由宿主实例化，非提交的脚本类由编译器生成的入口点实例化，各自调用编译器合成的那个构造函数。声明的构造函数不能占据那个槽位，所以没有任何东西调用它。共享构造函数不受影响。 | 无；这种形状无法在 C# 中写出——脚本类的成员不能是编译器生成类的构造函数——因此不需要为它设诊断 |
| BC37343 | `ERR_WithEventsVariableNotInContainingType` | 指向某个 `WithEvents` 变量的 `Handles` 子句，而容纳它的提交类并不声明该变量，因为该变量是从链上早先某次提交或从宿主对象到达它的。 | 无；C# 没有 `Handles` 子句 |
| BC42367 | `WRN_MainIgnored` | 因为该编译含有脚本类，`Main` 入口点被忽略：全局代码才是入口点。作为警告报告，而非静默忽略。 | CS7022，名称与含义相同 |

上述指令诊断只是模式门控；指令自身的语义另行规定，`#!` shebang 指令亦然。

**`Me` 限制。** 显式的 `Me`、`MyBase` 或 `MyClass` 在**脚本类内的任何位置**都被 BC36966 拒绝：既包括顶层脚本代码——顶层语句、顶层字段或属性的初始化器，以及在二者之中书写的任何 lambda 或查询表达式——也包括脚本类所声明成员的成员体，包括写在某个这样的成员体内部的 lambda。在有实例可用之处，隐式引用都被允许，所以顶层实例方法可以不加限定地调用另一个顶层方法并读取顶层字段。在 `Shared` 成员里，先命中的是普通的共享上下文诊断——显式关键字为 BC30043、隐式引用为 BC30369——而不是脚本专属那条，因为共享上下文的问题在脚本门之前就被判定。`MyBase` 即使在别处被允许的地方也没有基类成员可解析：脚本类没有基类型。这是刻意的，并且在**这一事实上**与 C# 脚本方言相同——后者的脚本类同样报告无基类型，正是为了不让它继承 `ToString`、`GetHashCode` 这类成员；两个方言在关键字本身上一致：C# 在脚本类里任何位置都拒绝显式的 `this` 与 `base`（CS0027 与 CS1512），成员体也算，并且同样先判静态上下文、后判脚本门。

**决策**（作者裁定，2026-09-23）：该禁令覆盖整个脚本类，因为这就是 C# 脚本方言实际做的事。本项目早先一度把禁令收窄到顶层代码，理由是成员体就是普通类代码、而 `Me.` 是唯一能取到被局部遮蔽的脚本字段的手段；对 C# 的实测显示那里是相反的策略，于是选择对等而放弃那份便利——在方法体里用局部遮蔽了顶层字段的脚本作者，改的是局部的名字。两条诊断的先后也保持 C# 的形状：`Shared` 成员先报共享上下文诊断。

```vbnet
Dim counter As Integer = 0

Function NextCounter() As Integer
    Dim counter As Integer = 5
    Return counter                ' Okay: the local shadows the field
End Function

Function ReadCounter() As Integer
    Return counter                ' Okay: implicit reference to the field
End Function

Function ReadCounterQualified() As Integer
    Return Me.counter             ' Error BC36966: You cannot use 'Me' in top-level script code
End Function

Shared Function ReadCounterShared() As Integer
    Return Me.counter             ' Error BC30043: 'Me' is valid only within an instance method
End Function

Console.WriteLine(counter)        ' Okay: top-level code reaches the field without qualification
Console.WriteLine(Me.counter)     ' Error BC36966: You cannot use 'Me' in top-level script code
```

对从 C# 脚本方言迁移来的代码，两个方言都不让成员体显式命名接收者：`this.X` 在 C# 脚本类里被拒绝，`Me.X` 在 Visual Basic 脚本类里也被拒绝，因此同一状态在两边都通过不加限定的引用到达，迁移只是这一类的书写改动。

**`Return` 限制。** `Return` 语句只有在位于顶层脚本代码且不在脚本初始化器之中时，才被 BC36966 拒绝。顶层语句中的 `Return` 是合法的——它就是初始化器的返回语句，它产生脚本结果。这个条件与 `Me` 的条件并不相同：顶层 `Sub` 或 `Function` 方法体内的 `Return` 就是该方法的普通返回，嵌套类型方法中的 `Return` 同样是普通返回。

**`Yield` 限制。** 顶层脚本代码中的 `Yield` 被拒绝。脚本初始化器不是迭代器，顶层语句也没有迭代器上下文。

#### 顶层标签与 `GoTo`

顶层的 `LabelStatement` 是一条可执行语句，而每条顶层可执行语句都被记录为初始化器的一项条目。因此顶层标签为初始化器方法体贡献一条语句，顶层 `GoTo` 亦然。标签绑定就是普通绑定：顶层代码的绑定器收集整个编译单元的标签，所以 `GoTo` 绑定到一个顶层标签，不报告诊断。该跳转在运行时生效——向前跳的 `GoTo` 跳过它所越过的语句，向后跳的 `GoTo` 回到带标签的语句。

```vbnet
Console.WriteLine("A")
GoTo done
Console.WriteLine("not printed")   ' Okay: the branch above skips this statement
done:
Console.WriteLine("B")
```

**决策**：顶层 `GoTo` 与其标签受本规范保证的覆盖，其语义即方法体中普通 `GoTo` 与标签的语义。这两种构造与普通对应物的差别仅在于容器，即上文所述的合成初始化器。

标签名称在编译单元内必须唯一。重复的顶层标签在较后的声明上以普通的重复标签诊断 BC30094 报告。

## 健全性
[soundness]: #soundness

该模型是一种容器化，不是一套新的声明系统。每个顶层形式都保留其普通拼写与普通含义；唯一的改变是声明所落入的容器，而容器是合成的而非书写的。这四种映射对编译单元所能包含的声明是穷尽的，因此没有声明会无家可归。

方言有意脱离普通语言之处在本规范中是明确的。`Imports` 作用域偏离限于脚本方言，其存在是为了让提交模型可用；普通编译保留文件局部作用域。编译选项默认值改变的是脚本的类型行为，不是任何构造的含义。

上表所列限制中的大多数，限于那些在顶层脚本代码中没有普通语言含义的构造：`Namespace` 声明没有可供嵌套于其中的东西，顶层语句中的 `Yield` 没有迭代器。显式 `Me` 限制是唯一一条按**容器**而非按构造划定的限制：该关键字在脚本类内一路被拒绝，包括它自己所声明成员的成员体，因为它本会命名的实例是合成的、由宿主创建的，其类型的名称无法书写。脚本类的成员体因此在这一点上不能与写在普通类里的同一个体互换，而读者需要知道的界是代码落在哪个类里——不是落在同一个类内部的哪一边。隐式引用——脚本本来就是那样写的——在所有位置都保持普通含义。一个既可作为脚本又可作为普通代码合法存在的程序，在两种场合含义相同；差别在于声明的位置、合成的入口点、带类型提交的结果规则，以及三个关键字在脚本类内被拒绝。

## 缺点
[drawbacks]: #drawbacks

- **每次提交都是一次完整的编译。** 后续提交不会增量复用上一次编译；每次查找都要走一遍链，会话的代价随其长度增长。该模型以编译代价换取状态保留。
- **合成容器使诊断变得含混。** 一个顶层声明落入一个 `Friend NotInheritable` 的合成类，其名称（`Submission#N`）在源码中不可书写。类型名称与堆栈帧不如手写的模块那样易认，顶层代码中的错误也不如类体中的错误那样有熟悉的上下文。
- **方言不同于普通语言。** 顶层 `Return` 是合法的，`Namespace` 不是，显式 `Me` 在脚本类内任何位置都不合法，裸表达式在提交末尾是合法的。每一处差别都提高代码在脚本与普通代码之间迁移的成本，这正是 C# 设计记录在警告脚本方言可能成为语言的 "a third dialect" 时所提出的同一关切（[LDM-2020-02-26][ldm-2020-02-26]）。
- **`Imports` 的累积在宿主侧。** 宿主必须收集并重新施加链上各子句，并且必须随着新子句形式的加入与编译器的导入语义保持同步。这是宿主与语言之间一种长期存在的耦合。
- **显式关键字禁令放弃了遮蔽逃生口。** 被局部遮蔽的脚本字段无法从该成员体内按名字限定访问，于是作者改局部的名字。保留禁令的理由是 C# 脚本方言在成员体里同样拒绝该关键字；代价是脚本类的成员体在这一点上不能与普通类里的同一个体互换。

## 替代方案
[alternatives]: #alternatives

- **要求显式的 `Module` 包装。** 这是普通语言的形式。它放弃了「在顶层键入的声明立即可用」这一性质，而这正是脚本场景的全部意义。
- **在宿主中改写源文本，把顶层包进一个 `Module`。** 已否决：它会使每条诊断的行号偏移插入的行数，把解析器与绑定器的抉择在宿主中重做一遍，并对编辑器和语言服务器隐藏真实的树。
- **为顶层 `Await` 增加语法。** 没有必要。初始化器已经是一个 Async 方法，所以 `Await` 受普通 Async 规则支配；新形式会为已有的构造造出第二种表达方式。
- **让末尾表达式产生带类型提交的结果。** 已否决。它会与 `Function Main` 入口点的 `Return` 语义冲突，并会偏离 C# 顶层语句——在后者中末尾表达式不是结果的来源。
- **在编译器中累积 `Imports`。** 已否决。宿主已经拥有这条链和逐次提交的导入选项；让编译器守住单一职责——消费 `GlobalImports`——可避免第二种编译器内部的累积导入概念。
- **给提交一个像脚本文件那样的同步 `Sub` 入口点。** 已否决。宿主必须提供先前各提交的状态，并且必须自己 Await 初始化器；一个阻塞式入口点会迫使宿主在它负责调度的任务上阻塞。

### C# 基线

Visual Basic 与 C# 在三个彼此独立的地方对同样的问题给出不同的回答，这些回答不得混为一谈。

- **声明与状态模型** —— C# **脚本方言**（`.csx`，共享的 `Script<T>` API）。它是最接近的对应物：它也合成一个提交类，也跨提交保留顶层状态，也把提交串成链。本规范关于声明、可见性与状态的模型，就是该模型的 Visual Basic 对应物。
- **文件执行结果** —— C# **顶层语句**。在那里，顶层代码在语义上是 `static async Task Main`，显式的 `return n` 是结果的来源。上文的带类型提交规则遵循这一侧面。
- **交互打印** —— C# **interactive**（`csi`），在那里末尾表达式会被打印。打印行为另行规定，不影响结果规则。

下表把声明与状态模型同 C# 脚本方言比较，并在二者不同之处指出差别。比较是对称的：每一行都说明该构造在两种语言中是什么。

| 维度 | C# 脚本方言（`.csx`） | Visual Basic 脚本方言 |
|---|---|---|
| 顶层变量 | 提交类的字段，跨提交保留 | 脚本类的字段，跨提交保留 |
| 顶层方法 | 提交类的实例成员，若声明为 `static` 则是 `static` 成员 | 脚本类的实例成员，若声明为 `Shared` 则是 `Shared` 成员 |
| 访问先前提交的状态 | 全程隐式接收者：顶层代码与顶层成员都不接受*显式*关键字——在脚本类里 `this` 是 CS0027、`base` 是 CS1512 | `Me.X` 与不加限定的引用都能到达它，但显式关键字在书写处一律被拒绝（BC36966），因此承载这次访问的是不加限定的引用 |
| 入口点 | 由宿主消费的提交工厂 | 提交用 `<Factory>`，脚本文件用 `<Main>` |
| 末尾表达式 | 无条件转换为提交的结果类型 | 仅 Object 提交才转换 |
| 顶层 `Imports` | 跨提交累积 | 跨提交累积（对 `Imports` 作用域规则的明确偏离） |

关于末尾表达式的那一行是相对 C# 脚本方言的有意分歧：结果规则改为遵循 C# 顶层语句。关于访问的那一行是对等而非分叉：两个方言都在脚本类内一路拒绝显式关键字，而 Visual Basic 比 C# 多拒绝一个关键字（`MyClass`，C# 没有对应拼写）——并且两边的共享上下文诊断都排在脚本诊断之前。这些记录的一早版本对 C# 给出过相反的范围，理由是"成员体就是普通代码"；对 C# 交互编译器的实测推翻了它——在那里 `void test() { this.ToString(); }` 报 CS0027——方言因此跟随实测而不是跟随直觉。`Imports` 那一行是相对普通 Visual Basic `Imports` 作用域规则的分歧，而不是相对 C# 的分歧；C# 脚本方言以同样的方式累积 imports。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 提前编译与裁剪

提交是脚本运行时的构造：它被编译为一个内存中的程序集，动态加载，并针对以对象数组传入的状态执行。它在提前编译（ahead-of-time compilation）与裁剪（trimming）的范围之外。提前编译路径只覆盖普通的编译产物；`.vbx` 提交永远不会是提前编译产品的一部分。

### 工具：累积的 `Imports` 不在语法树中

一个会话累积的 imports 承载在每次提交的**编译选项**之中，而不在其语法树中。一次提交自身语法树的 `Imports` 子句，恰好就是作者在该提交中写下的子句；从早先各提交累积来的子句不会被复制到树中。一个想要回答「本次提交生效的 imports 有哪些」的工具——编辑器的补全列表、重构、语言服务器——必须读取编译选项而不是树。只读树会静默遗漏每一个累积的导入。

### 关于提交结果的两个不同问题

两个彼此不同的查询不得混为一谈。产生脚本结果的规则是上文所述的结果类型规则；它是绑定与改写的属性，并以结果类型来表达。另一个独立的查询回答一次提交是否有供宿主打印的结果——这是宿主在决定是否打印任何东西之前所问的问题。该查询检查最后一棵树的末条语句的形状，不考虑结果类型，因此对带类型提交它可能给出与结果规则不同的答案。这两个问题目的不同：一个定义语言，另一个驱动宿主的打印抉择。

### 提交末尾的晚绑定属性访问

在 `Option Strict Off` 之下，`Object` 接收者上末尾的晚绑定成员访问绑定为一次调用而不是一个值，因此它报告的是丢弃取值的错误，而不是被打印。这是既有行为，不是结果规则的一部分。

### 方言的边界

以下几项是实现的必然结果，此处明确陈述而不留作隐含：

- **顶层标签与 `GoTo`。** 如上所规定，顶层标签与顶层 `GoTo` 都是初始化器条目，跳转在运行时生效。这两条语句不被赋予自己专属的位置：它们像任何其他顶层语句一样，在初始化器执行到它们之处执行，且标签名称在编译单元内必须唯一。
- **提交类中的 `WithEvents`。** `WithEvents` 挂钩构造函数的合成按脚本类的种类分派。非提交的脚本类遵循普通的类路径；提交类不合成挂钩构造函数，因为挂钩构造函数本该是的那个实例构造函数，已经作为提交构造函数为它合成了。当 `Handles` 子句所指的 `WithEvents` 变量与该子句声明在同一次提交中时，该子句受支持：挂钩由该变量的合成 setter 执行，而该 setter 由提交自身声明。一个从链上先前某次提交或从宿主对象到达提交类的变量，则以 BC37343 报告——提交类并不声明它，因此 `Handles` 子句的第一个标识符 "must be an instance or shared variable in the containing type that specifies the `WithEvents` modifier or the `MyBase` or `MyClass` or `Me` keyword" 这一要求未得到满足（[type-members][vblang-type-members]），发生编译错误。
- **累积子句之间的别名与 XML 前缀冲突。** 上文的规则定义哪个子句胜出、报告哪个诊断。项目级导入诊断不携带源位置，所以落败的子句只以消息中的文本识别；它无法用行号定位，写在早先某次提交中的落败子句也不例外。

## 测试
[testing]: #testing

该方言由无副作用的内存编译与执行测试来验证：它们不进行网络访问、不在隔离临时目录之外写文件、不启动进程、不访问注册表。脚本文件与交互用例通过脚本宿主运行并捕获控制台；编译器级用例从脚本模式解析选项在内存中编译。

- **声明模型与会话测试。** 一次提交中声明的顶层 `Dim` 在下一次提交中可读。一次提交中声明的顶层 `Function`、`Class`、`Module` 与 `Delegate`，每一个都可在其后某次提交中使用：该类型的实例用对象初始化器构造，取该函数的地址，调用该模块的成员。在一次提交中写下的导入在下一份中生效，并且不取代通过宿主选项提供的导入。
- **入口点与结果测试。** 带类型脚本忽略末尾表达式，带类型脚本的裸 `Return` 给出默认值；显式的顶层 `Return` 值是结果，并优先于末尾表达式；顶层可执行语句按源码顺序运行。在文件执行中，末尾表达式与 `?` 语句都不设置退出码，显式的 `Return` 设置退出码，裸 `Return` 给出零。
- **Async 与事件测试。** 脚本文件中的顶层 `Await`、裸 `Await` 语句，以及其值作为结果的 `Await` 都能编译并运行。带 lambda 的顶层 `AddHandler`、其事件处理程序声明在先前某次提交中的顶层 `AddHandler`，以及顶层 `RemoveHandler` 都能编译并生效。
- **限制测试。** 在脚本模式下，`Namespace` 声明报告 BC36965，而显式的 `Me`、`MyBase` 或 `MyClass` 在脚本类内一路报告 BC36966：在顶层代码中，包括写在其中的 lambda 与顶层字段或属性初始化器，也同样在顶层实例 `Function` 或 `Property` 的成员体内。本方言在允许一侧所要求的覆盖，是去掉限定符的同一位置——从顶层实例成员体内不加限定地引用一个顶层字段或方法，它编译通过并读到值——以及同一个显式关键字写在 `Shared` 成员里，在那里改报共享上下文的 BC30043。顶层 `GoTo` 与其标签编译无误。顶层 `On Error Resume Next` 与顶层 `RaiseEvent` 报告各自的不支持语句诊断。共享字段或属性初始化器中的 `Await` 报告 BC37341；在脚本类中声明的实例构造函数报告 BC37342，而共享构造函数以及脚本类所嵌套类的构造函数不受影响；其 `WithEvents` 变量从早先某次提交或从宿主对象到达提交类的 `Handles` 子句报告 BC37343，而针对同一次提交中声明的变量的子句得以绑定并运行。
- **语句形式测试。** 不是顶层代码末条语句的裸表达式报告 BC31003，同一位置上的成员访问报告 BC30545；在文件执行中两种形式都被接受并被忽略。嵌套 `Sub` 内部的裸表达式仍然报告它的普通诊断，`Object` 接收者上末尾的晚绑定成员访问不被打印。
- **会话隔离测试。** 失败的提交不改变会话的状态。
- **容器测试。** 写在一次提交顶层的某个构造，与写在脚本所声明类型的方法体中的同一个构造，是分开的用例，因为脚本所声明的类型是脚本类的嵌套类型，它的方法体没有指向脚本类实例的隐式接收者。普通编译测试无法产生的嵌套形式，在脚本源码中被断言：针对同一声明类型的 `WithEvents` 变量的 `Handles` 子句完成挂钩并投递；声明类型的共享构造函数在该类型首次使用之前运行一次；声明类型的实例、共享与 `ReadOnly` 字段执行其 `=` 与数组大小初始化器，其自动实现属性带与不带初始化器地执行各自的初始化器，其成员常量可从顶层代码读取；声明类型的 `Custom Event` 在 `AddHandler` 与 `RemoveHandler` 驱动下三个访问器全部运行；声明类型中 override 里的 `MyBase` 调用基类成员；声明类型的重载得以解析；以及 `IIf`、`Choose` 与 `Switch` 可从其中某个声明类型的方法里调用。容器敏感的声明规则也在拒绝一侧被断言：`<Extension>` 用在脚本所声明类的成员上报告 BC36551；脚本所声明模块中的一个 `<Extension>` 方法不会为该模块中写下的调用被收集，报告 BC30456；`Static` 局部变量在 lambda 中报告 BC36672、在脚本所声明 `Structure` 的方法中报告 BC31400、在泛型方法体中报告 BC32068、在 Async 方法体中报告 BC36955；`?` 打印语句没有方法体形式，在那里报告 BC31003。
- **语法族测试。** 脚本所能承载的 Visual Basic 语法，在该构造所能占据的容器中被断言：顶层的注释、带与不带 `Option Strict On` 的类型字符、字符文本、`#Const`、条件编译、`#ExternalSource`、`#Region` 与警告指令；带括号的运算符、一元运算符、算术运算符、`Like`、`TypeOf...Is` 与移位运算符，以及字典成员访问；基于 `From`、`Join`、`Let`、`Select`、`Distinct`、`Where`、分区、`Order By`、`Group By`、`Aggregate` 与 `Group Join` 运算符的查询表达式；XML 文本及其元素、文档、处理指令、注释与 CDATA 形式、其嵌入表达式、其命名空间及其成员访问轴；字符串内插、古旧与废弃的语句形式、空语句、`Error` 语句与 `Global` 限定符；`#` 指令错误；在接纳它们的方法体中的 `Static` 局部变量；在脚本所声明的方法体中用 `On Error` 与 `Err` 对象进行非结构化错误处理；以及 `Microsoft.VisualBasic` 运行时函数 `CallByName`、`LBound`、`UBound`、条件函数与字符串函数。有两个表达式臂在两种容器中都作断言：`NameOf`，作用于一个类型、一个成员以及提交自身声明的一个实体；以及条件访问的空传播形式，取 `?.`、`?!` 与 `?(` 三种结果形式，每种都读两次——一次在活的接收者上，它固定住结果表达式；一次在 `Nothing` 接收者上，它固定住传播。

## 相关条目
[related]: #related-items

- [C# 9.0 top-level statements](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/top-level-statements.md) —— 文件执行模型的 C# 对应物
- [LDM-2020-01-22](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md) —— 脚本与交互场景，提交状态保留
- [LDM-2020-02-26](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md) —— 脚本方言成为第三种方言的风险
- [LDM-2020-03-09](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-03-09.md) —— 顶层语句被当作位于一个异步入口点之内
- [LDM-2020-04-15](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md) —— 末尾表达式在语言本体中不是结果
- [Visual Basic design notes, 2017-12-06](https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.12.06.md) —— 议题 #102，单入口点文件中的顶层语句
- [Source Files and Namespaces](https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md) —— `Imports` 作用域规则与入口点规则
- [Statements](https://github.com/dotnet/vblang/blob/main/spec/statements.md) —— 局部声明与 `Return` 语句规则
- [Type Members](https://github.com/dotnet/vblang/blob/main/spec/type-members.md) —— `Dim` 作为变量成员的修饰符、`WithEvents`、扩展方法
- [Types](https://github.com/dotnet/vblang/blob/main/spec/types.md) —— 标准模块：隐式 `Shared` 成员，永不可实例化
- [Script<T>](https://github.com/dotnet/roslyn/blob/main/src/Scripting/Core/Script.cs) —— 共享的脚本 API

[ldm-2020-01-22]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md
[ldm-2020-02-26]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md
[vbldm-2017-12-06]: https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.12.06.md
[vblang-source-files-and-namespaces]: https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md
[vblang-statements]: https://github.com/dotnet/vblang/blob/main/spec/statements.md
[vblang-type-members]: https://github.com/dotnet/vblang/blob/main/spec/type-members.md
[vblang-types]: https://github.com/dotnet/vblang/blob/main/spec/types.md
