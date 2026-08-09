# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-versioning-tools.md`（版本化工具 / `Imports Wpf`）。按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期"，且"It is perfectly fine for work to happen on inactive or rejected proposals, and for them to be resurrected later."（proposals/README）。所以今天会议的真正问题是：**这个想法值得激活、继续搁置，还是把它拆开、留下能用的部分？**

我们开场读到的是一份很特别的一页建议：它没有示例代码，没有 Detailed design，没有原型链接——原文自己承认"原文未提供示例代码，只有一组设问"。我们把它当作**Anthony 第 18 章 18.27 的一页设问**来审，而不是一份成型的设计。它一口气问了五个彼此独立的问题，而"拆包"成了我们这场会的第一项工作。

## Agenda

* [Proposal: 版本化工具 / More Versioning Tools（`Imports Wpf`）](#proposal-版本化工具--more-versioning-tools)

## Proposal: 版本化工具 / More Versioning Tools

_Related: [vblang #117 – Method-scoped `Option` and `Imports` statements](https://github.com/dotnet/vblang/issues/117)；[vblang #255 – Localised Compiler Options](https://github.com/dotnet/vblang/issues/255)；2014-02-10 会议（Module 成员提升与"命名空间污染"）；2014-02-17 会议 #30（Extern alias for assemblies）；2014-10-01 会议（`InternalImplementationOnly` → "analyzer over language"）；2014-10-08 会议（meta-import 与合并候选）；2017-08-23 会议（Method-scoped `Imports`）；2017-12-06 会议（`Imports` 逗号多导入先例）；2018-02-21 会议（Upgrade Project 与接口演化）；ModVB：`meeting-name-resolution`、`meeting-module-enhancements`、`meeting-method-level-imports`_

### 场景与缺口

We started from the exact words of the proposal's source——Anthony 第 18 章 18.27（`..\..\AnthonyDesign_wordpress.txt`）：

> "We have `TypeForwardedToAttribute` to move a type to a different assembly. What about moving it to a different namespace? What about renaming types or methods? Could I design some kind of "virtual" or "meta" namespace concept that let's authors provide cleaner experiences, e.g. `Imports Wpf` instead of the 5 or 6 namespaces you actually need. Is there a way to provide a cleaner `System` namespace for new developers? StringBuilder should be there, _AppDomain probably shouldn't. What would tooling look like on upgraded projects, looking for help, docs, etc."

我们承认这段话里的**痛点是真的**：

- **库演化**。一个公开发行的库要移动类型、重命名方法，而既有客户不重编译、直接换 DLL 也不能崩。今天唯一的标准机制是 `TypeForwardedToAttribute`（`System.Runtime.CompilerServices`），它把类型的**身份**重定向到**另一个程序集**，由运行时类型加载器消费。跨命名空间移动、类型/方法重命名，在元数据层都没有对应物。
- **命名空间膨胀**。WPF 的真实体验：一个窗口文件常常要写 5–8 行 `Imports`，而且这些命名空间之间还会互相制造歧义。主线的旧笔记里就有现成的例子（2014-02-17，原句照录）：

  ```
  Threading.Thread.Sleep(1000)
  ' In Console and Winforms this is okay, but in WPF aps it is ambiguous
  ' between System.Threading and System.Windows.Threading
  ```

  一个今天的 WPF 窗口文件，`Imports` 清单通常长这样（能编译，就是我们讨论的现状）：

  ```vb
  Imports System.Windows
  Imports System.Windows.Controls
  Imports System.Windows.Controls.Primitives
  Imports System.Windows.Data
  Imports System.Windows.Documents
  Imports System.Windows.Media
  Imports System.Windows.Media.Imaging
  Imports System.Windows.Shapes

  Public NotInheritable Class MainWindow
      Private _label As New Label With {.Content = "Hello"}

      Public Sub OnClick(sender As Object, e As RoutedEventArgs)
          Dim brush As New SolidColorBrush(Colors.Red)
          _label.Background = brush
      End Sub
  End Class
  ```

  `Label` 来自 `System.Windows.Controls`，`RoutedEventArgs` 来自 `System.Windows`，`SolidColorBrush`/`Colors` 来自 `System.Windows.Media`——同一个方法的三行代码横跨三个命名空间，这就是 `Imports Wpf` 想折叠的体验。而主线的 WPF 歧义实录恰好来自同一片地方：`Threading.Thread.Sleep` 在 WPF 里要在 `System.Threading` 与 `System.Windows.Threading` 之间抉择。

- **新开发者门槛**。`System` 命名空间对新手不友好：`StringBuilder`（实际在 `System.Text`）不在这，`_AppDomain`（COM 互操作接口）反而在。这不是"错觉"——是真实的 API 表面问题。
- **升级工具化**。项目升级后，帮助、文档、查找引用怎么跟上重命名与移动。

但我们也立刻注意到一件事：**这段话是一连串问号**。`What about...?` / `Could I design...?` / `Is there a way...?` / `What would tooling look like...?`。它不是一份设计，是一张**问题清单**。我们在房间里反复回到的框架是：把五个独立问题捆成一份建议，恰恰踩中了我们自己评价标准里的红旗——"一份提案混杂多个独立特性，边界模糊"（4.2）。我们这场会的第一项决议性工作是**拆包**。

### 候选方案

先拆包。五个子建议各自的本质归属天差地别：

| 子建议 | 一句话 | 本质归属 |
|--------|--------|----------|
| (a) 跨命名空间类型移动 | `TypeForwardedToAttribute` 的扩展 | 运行时/CLR 元数据面 |
| (b) 类型/方法重命名保持二进制兼容 | 在元数据里给旧名做别名 | 运行时/CLR 元数据面 |
| (c) 虚拟/元命名空间（`Imports Wpf`） | 一个导入单元代替 5–6 个命名空间 | **语言面**（唯一） |
| (d) 更干净的 `System` | 把 `StringBuilder` 挪进来、`_AppDomain` 挪出去 | BCL/API 表面 |
| (e) 升级工具化 | 重命名/移动后找帮助、文档 | 工具/路线图 |

对拆包后的 (c)（唯一有语言表面）、以及整份建议的去留，我们列出候选：

**PROPOSAL A — 元命名空间作为编译期导入单元（语言特性）。** `Imports Wpf` 是一个新的可导入单元，编译期展开为若干真实命名空间。这是建议原文的设想。需要新文法、新的绑定规则、语义模型里的新符号种类。

**PROPOSAL B — 元命名空间作为工具层展开（非语言特性）。** IDE 提供"组导入"：`Imports Wpf` 是一个智能片段/代码修复，键入后**展开成真实的 6 条 `Imports`**。零语言面、零绑定间接层、零破坏；所有价值都在编辑体验里。

**PROPOSAL C — 库侧标注 + 编译器识别（轻语言面）。** 库作者用某个属性声明"命名空间组"，编译器读取并允许 `Imports Wpf` 绑定到该组。语言面最小（只加一个属性 + 一条导入规则），但把"组"的定义权交给库作者，带来版本漂移与信任问题（见深度追问 #1/#3）。

**PROPOSAL D — 元数据层重定向扩展（跨命名空间移动/重命名）。** 向运行时/C# LDM 提议扩展类型转发或新增"成员别名"元数据。VB 侧不发明自己的机制，只做消费端评估。

**PROPOSAL E — 什么都不做，保持 inactive，拆出工具化。** 现状机制（`TypeForwardedToAttribute`、门面类型、`Obsolete` shim）+ 独立的工具化路线（升级助手、组导入展开）。最符合主线的"缩小范围、分阶段落地"与"对扩展设高门槛"。

这三条候选在我们桌上的形状（均为**我们房间里画的草图**，非建议原文，亦非任何既有语法——只用于把讨论钉在具体形态上）：

```vb
' PROPOSAL A：语言导入单元——Imports Wpf 是新的可导入对象，编译期展开。
Imports Wpf

' PROPOSAL B：工具层展开——同一个词，IDE 把它还原成真实的 Imports 清单。
' 用户敲下 Imports Wpf（或触发代码修复），得到的是下面这一串可见的 6 行：
Imports System.Windows, System.Windows.Controls, System.Windows.Data,
        System.Windows.Documents, System.Windows.Media, System.Windows.Shapes

' PROPOSAL C：库作者用属性声明"命名空间组"，编译器读取。
<NamespaceGroup("Wpf",
    "System.Windows",
    "System.Windows.Controls",
    "System.Windows.Data",
    "System.Windows.Media",
    "System.Windows.Shapes")>
Public NotInheritable Class WpfGroup
End Class
```

`Probably`：B 的形状最接近"今天就能做、用户立刻受益、且没有黑盒"；A 与 C 的差别只在于"组"的定义权在消费者（A，编译期内置）还是库作者（C，属性声明）。

### 权衡：Q&A

- **拆包是不是过度解读？** 不是。五个子建议的实现面、归属、风险完全不相交：(a)(b) 依赖 CLR 元数据；(d) 依赖 BCL 团队；(e) 依赖 IDE/文档团队；只有 (c) 需要改编译器和语言。把 (a)(b)(d)(e) 的成败绑在 (c) 的辩论上，对谁都不公平。主线对这类情况有直接先例——2017-08-23 讨论 Method-scoped `Option` and `Imports` 时记过一句 "Could be useful, should break it out into another proposals."，我们照做。

- **(a)(b) 到底是不是语言问题？** 不是。`TypeForwardedToAttribute` 已经存在，跨命名空间移动在 CLR 里**没有**机制：元数据里每个类型只有**一个**全限定名，旧客户编译产物里的 `TypeRef` 仍指向旧名，同一程序集内没有转发通道。**唯一**不破坏二进制兼容的做法是库作者保留一个旧名门面类型：

  ```vb
  ' 现状 (a)：移动类型时保留旧名门面——只对非 NotInheritable 的引用类型有效。
  Namespace MyLib.Old
      Public Class Widget
          Inherits MyLib.New.Widget   ' 旧名是门面，转发到新位置
      End Class
  End Namespace
  ```

  门面模式对 `NotInheritable` 类、值类型、静态成员、继承身份统统失效——这是**库写作纪律**，不是语言能修的。重命名同理：元数据每成员单名，旧成员必须留作 shim：

  ```vb
  ' 现状 (b)：重命名方法时保留旧名 shim + Obsolete——库作者今天就在这么做。
  Public Class Widget
      <Obsolete("Use Compute instead.")>
      Public Function Calculate(x As Integer) As Integer
          Return Compute(x)
      End Function

      Public Function Compute(x As Integer) As Integer
          Return x * 2
      End Function
  End Class
  ```

  主线对这类"想给库作者加机制"的请求有过一次非常清晰的裁定——2014-10-01，当时为了"让接口可以在不破坏实现者的情况下加成员"，考虑新增一个 `InternalImplementationOnly` 属性，决议是 "Workaround #7 is a better option than adding this proposal to the language."（workaround #7 = 一个随 NuGet 包分发的 Roslyn analyzer，警告非法实现者）。**同一句裁决我们今天原样适用：analyzer 与库纪律，胜过语言特性。** 且评价标准 1.5 明确"CLR/库层变更直接委托 C# LDM"——我们跟随。

- **(c) 元命名空间，值不值语言的代价？** 这是我们今天唯一真正辩论的部分。支持面：痛点真实（WPF 5–8 行 `Imports`、2014-02-17 的歧义实录），且 `Imports Wpf` 读起来极像 VB——一个简短的、低仪式的词。反对面有三层：
  - **第一层，价值与"少写几行"同量级。** VB 今天已经支持 `Imports A, B, C` 一行多导入——2017-12-06 原句："VB already has a strong precedent for multiple consecutive statements/constructs of the same kind being combinable into a comma-separated list: `Imports`, `Dim`, `Next`, `From`, `Let`, `Case`."。也就是说，今天就能这样写：

    ```vb
    ' 今天合法的 VB：一行多导入。组想省掉的"仪式"，一半已经不存在了。
    Imports System.Windows, System.Windows.Controls, System.Windows.Data,
            System.Windows.Media, System.Windows.Shapes
    ```

    所以 `Imports Wpf` 的净收益是把"6 条 `Imports`"缩成"1 条"——省的不是仪式，是一行。这与我们愿意为语言付的价码不匹配。
  - **第二层，"第二种做事方式"。** 2018-06-13 原句："our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"。元命名空间引入一种**新的可导入单元**，与命名空间导入、类型导入、别名导入（`Imports Alias = `）并列。这违反原则 #3。
  - **第三层，绑定间接层。** 建议自己的 Drawbacks 承认："虚拟 / 元命名空间"会在编译期解析与元数据之间引入间接层。`Button` 到底来自哪个命名空间？——今天你能在 `Imports` 清单里看到；有了 `Imports Wpf`，你看到的是一个黑盒。这正是原则 #7（避免隐蔽的语义变化）警惕的东西。
  - 所以我们不把 (c) 的语言化当成"这是不是好主意"来问，而问"它能不能被工具覆盖"。**能。** PROPOSAL B 用今天的 IDE 机制就能做：组导入展开成真实 `Imports`，用户得到的是**可见**的 6 行而不是一个黑盒。工具化把"黑盒"变成"展开"，反而更诚实。

- **PROPOSAL C（库侧属性标注）会不会更好？** 表面诱人：库作者声明组，消费者只需 `Imports Wpf`。但把"组"的定义权交给库作者带来三个问题：**版本漂移**——库新增一个命名空间，`Imports Wpf` 是否自动包含？消费者编译结果随之漂移；**信任**——一个属性就能改变消费端的名称解析，等于给第三方一个隐形的编译器插件；**语义模型**——`Imports Wpf` 绑定到什么符号？一个既非类型也非命名空间的第三方声明的组。C# 的 `global using`、`static using` 都没有这种"库作者可定义的导入聚合单元"，我们没有可借鉴的稳定设计。**结论：C 留档，不推进。**

- **(d) 更干净的 `System` 呢？** 这不是语言能做的事。`System` 命名空间由框架定义，语言没有重定义它的权限。把 `StringBuilder` 从 `System.Text` 挪进 `System`，是把整个框架的 API 表面翻一遍——**本质破坏**，而 2018-06-13 原句 "We will almost never make breaking changes to Visual Basic" 说的是语言，但精神同样适用于 BCL 表面。它的价值（新手友好）真实存在，但主线的既有答案是另一个方向：**`My` 命名空间**——VB 编译器生成的、面向常用任务的精选命名空间，它已经回答了"给新开发者一个精选入口"的需求，而**没有**动 `System` 一个字节：

  ```vb
  ' VB 既有的"精选命名空间"先例：My 是编译器生成的、按任务组织的入口。
  My.Computer.FileSystem.ReadAllText(path)   ' 不用记 System.IO 的路径
  My.Application.Info.Version                 ' 不用记 System.Reflection 的路径
  ' 建议想要的"干净 System"，My 已经在做；再往下就要动 BCL 表面了。
  ```

  2014-02-10 讨论过反方向的问题——Đonny 抱怨 Module 成员"pollute"命名空间（"static class does not 'pollute' namespaces with it's members"），那次收敛成了 `Strict Module`。**"精选 System"与"去污染"是两个方向，但都不是语言特性。**

- **(e) 升级工具化，值不值得认真做？** 值得，但它是**路线图项**不是语言特性。主线 2018-02-21 的 Upgrade Project 讨论原句："The customer problem: To change the version of VB a project is targeting, you have to edit the .vbproj file. Ick. Extra Ick because VB programmers put high value on simplicity/low ceremony."——项目升级对 VB 团队是真问题，且已经列在计划里。重命名/移动之后的"找帮助、找文档"是同一类工具化问题：搜索引擎/文档索引/`Obsolete` 消息里的指引。这些不需要 LDM 决定，需要的是工程投入。

- **接口演化呢？** 这是 (a)(b) 的现代变体，也是我们房间里讨论最多、最不能忽视的部分。2018-02-21 记录过 C# 的 default interface methods："A compelling scenario for this involves allowing interfaces to change over time without breaking all implementors. The implication is that people will then evolve interfaces, and if VB can't handle this, it will have a serious problem."——接口演化如果做成了，**库作者就不再需要**"跨命名空间移动/重命名不破坏二进制兼容"这类工具，因为接口可以直接长出新成员。这是一个**运行时+两门语言**共同推进的领域，VB 只做消费端。我们的立场：库演化问题的**正确出路在接口演化与工具化**，不在给 VB 发明元数据重定向。

- **"什么都不做"是不是消极？** 不。我们在这个沙盒里已经有过一次"We're proud not to do anything"的同款会议（postfix-casting 会议对 target-typed 的态度）。今天对这份建议，我们不是"什么都不做"，而是**把能做且值得做的部分（工具化、接口演化对表、升级路线图）指派出去，把不能做的部分（元数据重定向、重写 BCL）明确标注为不属于语言**。拆包本身就是建设性的动作。

### 深度追问：LDM 拷问清单

以下逐条主要针对唯一有语言面的 (c) 元命名空间；对 (a)(b)(d)(e) 只做归属性追问。

#### 1. 语法 / 文法歧义

`Imports Wpf` 的歧义在**绑定目标**而不在词法：今天 `Imports X` 的 `X` 必须是真实命名空间或类型，若 `Wpf` 不是，编译报错。引入"组"之后，`Imports Wpf` 有四种可能：真实命名空间 `Wpf`、类型 `Wpf`、别名 `Wpf`、命名空间组 `Wpf`。若一个程序集里真有一个叫 `Wpf` 的命名空间，组与它同名怎么办？——组遮蔽还是歧义？PROPOSAL C（库属性标注）还要回答"组声明出现在哪个程序集、以什么名字进入消费端的绑定表"。今天 `Imports Alias = X.Y` 是等号形式，`Imports Wpf` 是无等号形式；组是无等号形式，与"导入真实命名空间"的词法**完全相同**——所以这个歧义不是"再想细一点"能消的，它是文法层面的：**导入目标集合里新增了一个既非命名空间也非类型的成员**。语法本身无歧义，语义建模有。

#### 2. 角案例与边界语义

- **组内碰撞**：组展开后是多个命名空间，其中两个都定义了同名简名怎么办？VB 的默认规则是歧义报错（2014-10-08 原句："VB makes this invocation of Sin an ambiguity."）。而同一场会议对 **meta-import** 场景又选了另一条路："Allow at lookup, i.e. merge them all, and leave it to overload resolution. (This is the proposal that we picked.)"。**元命名空间恰恰是"meta-import"的现代形态**——2014 年那个词就是为"一个导入带入多个来源"造的。两条先例方向相反，我们把这个冲突原样写进 OPEN QUESTIONS，留给原型。
- **组内子命名空间**：`Imports Wpf` 引入 `System.Windows.Controls`，那 `Imports Wpf.Controls.Primitives`（对组内命名空间继续做成员访问）成立吗？组没有"成员"——它展开后才谈得上。文法层面必须回答 `Wpf.` 之后跟什么。
- **嵌套组**：组能包含另一个组吗？组的成员是命名空间还是也可以是组？（若 PROPOSAL C，库作者几乎一定会想聚合。）
- **版本漂移**：库作者在组声明之后新增命名空间，已编译的消费者要不要自动获得？——`Probably` 不能：编译期"自动包含新成员"会让同一份 `Imports Wpf` 在不同编译时刻解析到不同集合，是可观测的语义漂移。这与 `Imports` 的静态性相悖。
- **空组 / 删组**：库删掉组声明的命名空间，消费者编译报错的位置在 `Imports Wpf` 还是在组内成员的使用处？报错体验要设计。

#### 3. 作用域与绑定

`Imports Wpf` 在语义模型里返回什么符号？现有 `Imports` 绑定到 `INamespaceSymbol` 或 `ITypeSymbol`。组是一个新的符号种类（`NamespaceGroupSymbol`？）——它不是 CLR 概念，纯编译期存在。`Wpf.` 的成员查找、`Global.Wpf` 是否可达、`nameof` 对组的处理——2014-10-15 对别名的裁定是 "If 'I' identified an alias... then the result of nameof is still 'I'"，组的 `nameof` 语义需要同样明确。IDE 补全在 `Imports Wpf` 之后显示什么、组是否参与 `Go To Definition`，都要定义。**这整套语义建模的工程量，是对 PROPOSAL A/C 最现实的冷水**：它不小。

#### 4. 与既有特性的交互

- **`Imports Alias = `**：组与别名是不同构造（别名单命名空间，组多命名空间），但词法相邻，交互规则（组能否被别名、别名能否指向组）必须定。
- **XML 命名空间导入**：`Imports <xmlns:...>` 与组正交，但组内 XML 字面量解析不受影响——`Probably` 无交互。
- **Module 成员提升**：组内命名空间若含模块，成员提升照常发生——组的"范围"等于其成员的"范围之和"，需要确认不产生新的提升面。
- **`name-resolution` 建议（ModVB）**：第 16 章 Anthony 提过命名空间 `Console` 遮蔽类型 `System.Console`。组把多个命名空间折叠成一个名字，**放大**了这种遮蔽的半径——这是组让"隐蔽语义变化"更隐蔽的具体机制，与 `meeting-name-resolution` 必须对表。
- **方法级 `Imports`（`meeting-method-level-imports`）**：若方法级 `Imports` 存在，组能否在方法级导入？组是"第二种做事方式"，方法级导入是"范围收窄"，两者叠加会扩大表面积——我们倾向**组不与方法级导入叠加**，组只允许文件级。

#### 5. Breaking change 与兼容性

若 `Imports Wpf` 是纯加法（今天 `Imports Wpf` 在无真实 `Wpf` 命名空间时编译失败），本身不破坏。但两个破坏点真实存在：**(i)** 组引入的简名可能与既有 `Imports`/项目导入重名，产生**新的**歧义报错——已编译通过的代码在重编译后报错；**(ii)** 若真实存在 `Wpf` 命名空间，组的遮蔽改变既有绑定。而 (a)(b)(d) 的破坏性是**本质的**——移动类型、重命名、重排 BCL 表面，没有不破坏的路径。这一条把 (a)(b)(d) 与 2018-06-13 的"几乎永不做破坏性变更"直接对冲。

#### 6. Option Strict / 编译选项分叉

组导入是名称解析层概念，严格/宽松两条路径行为一致——导入不产生转换、不触发晚期绑定。`Probably` 无分叉。但注意：`Option Compare Text` 下组名的**大小写匹配**沿用现有导入规则即可，不新开。

#### 7. IDE / IntelliSense

如果组做成了语言特性，IDE 必须支持：`Imports Wpf` 的着色、组展开的显示（"包含 6 个命名空间"）、`Wpf.` 的成员补全、`Go To Definition`、`Organize Imports` 与组的关系。而如果只做 PROPOSAL B（工具层展开），IDE 工作是**相反方向**——把组**还原成可见的 `Imports` 清单**。我们注意到：B 的 IDE 工作量远小于 A 的（展开比建模简单），却给用户更多透明。这让我们更倾向 B。

#### 8. 数据 / 普遍性

诚实地说：**零数据**。建议没有给任何占比、任何案例、任何请求来源。WPF 多命名空间导入是真实体验，但"它困扰了多少人、花掉多少时间、是否已被工具/模板解决"没有数字；类型移动/重命名破坏的频率更没有数字。2018-02-21 主线在讨论接口演化时也承认没有数据。按我们的证据阶梯，效果维度封顶在"已提供"。

#### 9. 更简替代

这是本建议最受冲击的一条：

- **`Imports A, B, C` 一行多导入**（2017-12-06 先例）——组 80% 的价值（少写几行）已被覆盖，代价为零。
- **IDE 组导入展开（PROPOSAL B）**——`Imports Wpf` 作为智能片段展开为真实 6 行，覆盖剩余价值且保持透明。
- **项目模板 / 文档**——新开发者被 "cleaner System" 的目标，用模板预导入 + 文档指引就能覆盖。
- **analyzer（2014-10-01 先例）**——重命名/移动的引导工作交给 analyzer，不碰语言。
- **接口演化（2018-02.21 对表）**——库演化的根治法，是运行时+C# 的工作，VB 只消费。

#### 10. 复杂度 / 成本 / 优先级

PROPOSAL A 的编译器成本中等（新符号种类 + 绑定规则 + 语义模型），但**语义建模没有先例可抄**——C# 的 `global using`/`using static` 都不是"聚合命名空间"。PROPOSAL C 成本更小但要定义"第三方属性影响消费端名称解析"的信任模型，**这是设计里最危险的部分**。优先级上，与模式匹配、可空性流分析、接口演化相比，这只是一个"少写一行"的诉求——排不上号。

#### 11. 运行时 / CLR 硬约束

组本身零 IL、零运行时影响（纯编译期）。但 (a) 跨命名空间移动触达类型加载器的转发逻辑（CLR 层）；(b) 重命名触达元数据存储规则（每成员单名）。这两条是硬约束：**CLR 不给，语言就不给**。VB 无权单独改变元数据布局，只能消费运行时提供的机制。default interface methods 是"运行时已经在给"的方向。

#### 12. 值不值得做

逐条打分（对 (c) 语言化）：**价值 2/5**（少写一行、且可被工具覆盖）、**成本 3/5**（语义建模无先例）、**风险 3/5**（碰撞、遮蔽、信任模型）。净值偏负。对整份建议（作为 inactive 文件）：**不激活，但拆包留档**。动机真实，方向明确，只是没有一件属于"现在要做的语言特性"。

### VB 基因对照

- **消除常见样板（原则 #9）**：方向一致——`Imports Wpf` 的吸引力全在"少写样板"。但"常见"存疑：WPF 多命名空间导入是真实痛点，却可能已被模板/片段缓解，无数据。
- **不引入"第二种做事方式"（原则 #3）**：**最大扣分项**。组是继命名空间导入、类型/模块导入、别名导入、XML 命名空间导入之后的**第五种导入对象**。2018-06-13 的"second way to do things"门槛，组正面撞上。
- **永不破坏现有代码（原则 #1）**：(a)(b)(d) 是本质破坏，(c) 有隐蔽重绑定风险。整份建议对"We will almost never make breaking changes"构成群体性挑战。
- **保持 VB-like（原则 #2）**：`Imports` 是纯正 VB 词汇，但"虚拟/元命名空间"不是 VB 概念——它更像 F# 的 `AutoOpen` 聚合或 C# 没有的东西。`Suspect`：组把"导入可见性"变成"导入黑盒"，不符合 VB 的透明直觉。
- **避免隐蔽语义变化（原则 #7）**：(c) 让"这个 `Button` 来自哪个命名空间"不可见；(a)(b) 本质是元数据上的别名与重定向，"出错时很难调试"（建议 Drawbacks 自己的话）。
- **跟随 C#（原则 #4）与 CLR 层委托（评价标准 1.5）**：跨命名空间移动/重命名委托 C# LDM 与运行时；接口演化（default interface methods）是我们要跟的方向。
- **冗长只在有用时是美德（原则 #10）**：6 行 `Imports` 在"追踪名称来源"这件事上是有用的冗长——组把它压缩掉，恰恰损失了这层信息。这与方法级 `Imports` 会议里"简名来自哪一级"的认知负担是同一件事的放大版。
- **与主线关系（对照表 2.3）**：主线对照表里**没有**对应条目——这是纯粹的 **Anthony 独立延伸**。最接近的主线工作是：2018-02-21 Upgrade Project（项目升级工具化）与 default interface methods（接口演化），两者都已被主线/运行时接管。`My` 命名空间是主线给"精选命名空间"的既有答案。与 ModVB 的 `name-resolution`、`module-enhancements`、`method-level-imports` 共享同一片"导入与命名空间"设计面。

### RESOLUTION:

1. **整体判定：Table（保持 inactive，不 Reject）。** 这份建议是一张**问题清单**，不是设计——五个独立子建议无一成型，全部证据止于书面（状态行仅 `[x] Proposed`，原型/实现/规范链接为占位符）。我们拒绝把它当特性激活；也拒绝彻底否决，因为动机真实、且 (c) 有清晰的可激活路径。
2. **拆包为五个独立议题，各自定责：**
   - (a) **跨命名空间类型移动** → 委托运行时/CLR + C# LDM（默认跟随 C# + CLR 层委托原则）；VB 不发明自己的元数据重定向。现状记录：门面类型模式（对 `NotInheritable`/值类型/静态成员失效）。
   - (b) **类型/方法重命名保持二进制兼容** → 语言无机制可加（CLR 元数据单名约束）；现状 = 旧成员 shim + `Obsolete`；引导工作交给 analyzer（2014-10-01 先例："Workaround #7 is a better option than adding this proposal to the language."）。
   - (c) **虚拟/元命名空间（`Imports Wpf`）** → 唯一有语言面的子建议，**今日不语言化**；先走工具化（PROPOSAL B），语言化（PROPOSAL A/C）挂在"激活所需信号"上。
   - (d) **更干净的 `System`** → BCL/文档/模板议题，非语言特性；`My` 是 VB 既有的"精选命名空间"先例，方向上不重排 BCL 表面。
   - (e) **升级工具化** → 与主线 2018-02-21 Upgrade Project 工作合并，作为路线图项。
3. **(c) 元命名空间的具体立场：**
   - 承认痛点真实（2014-02-17 WPF 歧义实录 + WPF 5–8 行 `Imports`）。
   - 但价值与"少写一行"同量级（`Imports A, B, C` 已覆盖 2017-12-06 先例），且违背 2018-06-13 的"second way to do things"门槛、引入绑定黑盒。
   - **先做 IDE 组导入展开**（PROPOSAL B）：`Imports Wpf` 作为代码片段/代码修复，展开成真实 `Imports` 清单。零语言面、零破坏、保持可见性。
   - 碰撞规则不预设立场：2014-10-08 同场记录了 VB 默认歧义报错与 meta-import "merge them all"（PROPOSAL3，被选中的方案）两条相反先例——**元命名空间正是"meta-import"的现代形态**，规则留给原型。
   - PROPOSAL C（库属性标注）**留档不推进**：版本漂移、信任模型、语义建模三座大山无先例可抄。
4. **激活所需信号（明确列出）：**
   - **信号 1**：原型证明"组导入展开"工具覆盖不了的**残余价值**——若工具做出来后发现用户仍然需要 `Imports Wpf` 是"真的一个导入"，再谈语言化。
   - **信号 2**：量化数据——WPF/多命名空间导入痛点与类型移动/重命名破坏的真实发生率（建议现在零数据）。
   - **信号 3**：运行时/CLR 侧对接口演化（default interface methods）与跨程序集类型转发的进展——VB 只做消费端；若接口演化落地，库作者对 (a)(b) 的需求会大幅萎缩。
   - **信号 4**：语义模型与 IDE 的完整方案——`Imports Wpf` 绑定到什么符号、`Wpf.` 补全显示什么、`Go To Definition` 指向哪。

### Implication:

- 将建议拆为 5 份独立文档；(c) 单独成文并补文法/碰撞/绑定草图（引用 2014-10-08 双先例）。
- 实现"Imports 组展开"IDE 代码修复原型（PROPOSAL B），验证"工具覆盖语言"假设。
- 与 C# LDM / 运行时对表：default interface methods 进度、跨程序集类型转发是否有新机制。
- 与 `meeting-name-resolution`、`meeting-module-enhancements`、`meeting-method-level-imports` 对表：元命名空间与它们共享"导入与命名空间"设计面。
- 补数据：为信号 2 建立量化路径。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：元命名空间碰撞规则——沿用 VB 歧义报错还是 2014-10-08 meta-import 的"合并候选交重载解析"（PROPOSAL3）？两条都是主线先例，方向相反，待原型。
- `OPEN QUESTIONS`：`Imports Wpf` 的语义模型符号（`NamespaceGroupSymbol`？），`Wpf.` 成员访问、`Global.Wpf`、`nameof` 的绑定行为。
- `OPEN QUESTIONS`：版本漂移——库作者组声明后新增命名空间，已编译消费者是否自动获得新成员（`Probably` 不能，与 `Imports` 静态性相悖）。
- `OPEN QUESTIONS`：组内子命名空间访问（`Wpf.Controls.Primitives`？）与嵌套组是否允许。
- `TODO`：量化 WPF/多命名空间导入与类型移动破坏的发生率。
- `TODO`："Imports 组展开"代码修复原型。
- `Follow-up`：与主线 Upgrade Project（2018-02-21）对表；与 C# default interface methods 进度对表；与 `name-resolution` 建议确认遮蔽半径放大问题。

### 状态

- **LDM 状态：LDM Considering**（沙盒内）；建议维持 **inactive**（主线无对应 issue，proposals/inactive 归属不变）。
- **三态判定：Table** — 保持搁置。动机真实但未成型；五个子建议中四个是非语言面（运行时/CLR、BCL、工具），唯一语言面子建议 (c) 的价值可被工具覆盖、且违背"第二种做事方式"与"隐蔽语义变化"两条原则。拆出工具化与对表工作，其余等"激活所需信号"。

---

## 附录：特性评价

# 建议评价报告：proposal-versioning-tools.md

## 评价对象

- 建议：proposal-versioning-tools.md — 版本化工具 / `Imports Wpf`（五个子建议捆绑：跨命名空间移动 / 重命名 / 元命名空间 / 更干净 `System` / 升级工具化）
- 来源：Anthony 原文第 18 章 18.27（`..\..\AnthonyDesign_wordpress.txt` L3346–3350，**一页设问**，无示例代码）
- 配方目标：给库作者更多"不破坏二进制兼容"的版本化能力，并改善新开发者的命名空间体验
- 血缘：主线无对应工作；最接近的是 2018-02-21 Upgrade Project 与 C# default interface methods（接口演化）；`My` 命名空间是主线对"精选命名空间"的既有答案

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 是五个问号，无任何可演示的示例（原文自己承认"未提供示例代码"）；改进不可衡量；无原型（状态行仅 `[x] Proposed`，原型/实现/规范链接均为占位符） | 已提供 | 效果维度被未成型的设计封顶；五个子建议效果各自独立，无法统一验收 |
| 特性 | 2/5 | 锚点 2："或多个强无关能力捆绑"。五个能力归属面完全不同（CLR 元数据 / 语言导入 / BCL 表面 / 工具），捆绑是红旗 4.2"边界模糊"的典型；元命名空间概念既非继承 VB6 也非借鉴 C#，是原创但未 VB 化（绑定黑盒） | 已检查 | 唯一有 VB 基因的部分是 `Imports` 词汇本身；组概念与原则 #2/#3/#7 三面冲突 |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节形式上齐全，但 Detailed design 空置（"原文未提供示例代码"）、Drawbacks/Alternatives 是通用套话、5 个未决问题其实就是整个设计本身；状态行占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）违反强提案清单 | 已检查 | 按评价标准 3.3，未决问题 ≥4 个关键设计点 → 效果封顶；占位链接是红旗 4.2 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。若实现，(a)(b)(d) 本质破坏、与 2018-06-13"几乎永不做破坏性变更"对冲；(c) 引入绑定间接层（暗）；风维度——与主线节奏无关但无对表设计。Drawbacks 诚实列了风险是唯一加分 | 已检查（预测待定） | 暗风险无对冲设计；无原型；实际影响须"已采纳"后定 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。材料来源未标注章节号；`TypeForwardedToAttribute`（CLR/.NET 机制）、`Imports`（VB 词汇）、元命名空间（原创）、`My`（VB 既有）四类成分混在一起未区分；五能力捆绑未说明各自血缘 | 已检查 | 未声明"一页设问"的定位；无杂质（未借闭源）但成分标注残缺 |

## 设计原则对照

- **与 VB 基因：偏离为主**——(c) 违背原则 #3（第二种做事方式）、#7（隐蔽语义变化）；(a)(b)(d) 违背原则 #1（永不破坏）；唯一一致处是 (c) 的"消除样板"方向（原则 #9）与 `Imports` 词汇本身。`My` 是主线对"精选命名空间"的既有、更 VB 的答案。
- **与主线关系：Anthony 独立延伸**——主线对照表 2.3 无对应条目；主线在相关领域的方向是接口演化（default interface methods，C#/运行时主导）与项目升级工具化（2018-02-21），两者都被主线接管。元命名空间与 ModVB 的 `name-resolution`、`module-enhancements`、`method-level-imports` 共享设计面。
- **破坏性变更：有（若实现）**——(a) 跨命名空间移动、(b) 重命名、(d) 重排 BCL 表面均为本质破坏；(c) 有隐蔽重绑定与新增歧义报错风险。建议文档未做兼容性分析。

## 总评

- **达成程度：未达成**——作为"特性"没有成型的设计、无示例、无原型、无数据；作为"问题清单"它诚实地列出了真实痛点。评价标准的红线（捆绑、占位链接、零数据）全部命中。
- **LDM 三态建议：Table**——保持 inactive，不 Reject（动机真实、子建议 (c) 有可激活路径），不 Active（无设计、无数据、四分之三内容不是语言问题）。
- **主要问题**：① 五独立子建议捆绑，边界模糊；② 四分之三是非语言面（CLR/BCL/工具），被误当成语言特性提案；③ (c) 元命名空间价值可被 `Imports A, B, C` 与 IDE 组导入展开覆盖，且引入绑定黑盒；④ 零数据、零原型、状态行占位链接；⑤ 未做兼容性分析。

## 返工建议

- **补充章节/拆分**：拆为 5 份独立文档；(c) 单独成文，补文法（导入目标集合新增"组"）、碰撞规则（2014-10-08 双先例）、语义模型草图（`NamespaceGroupSymbol`）、与既有 `Imports` 形态的交互表；(a)(b) 改写为"委托运行时/C# LDM"的立场说明 + 门面/`Obsolete` shim 现状记录；(d) 改写为 BCL/文档/模板议题（`My` 对照）；(e) 并入升级路线图。
- **补充证据**：WPF/多命名空间导入痛点与类型移动/重命名破坏的发生率数据（信号 2）；"Imports 组展开"IDE 代码修复原型（信号 1）；default interface methods 进度对表（信号 3）。
- **未决问题处理**：碰撞规则 = 留待原型，不预设立场；组内子命名空间/嵌套组 = `Probably` 不支持 v1；版本漂移 = `Probably` 不自动包含新成员；`nameof`/`Global.Wpf` 绑定 = 待语义模型设计。
- **设计探索**：把 (c) 的语言化从"功能"重写为"工具不足的证明"——先做展开工具，用数据决定是否语言化；评估 `My` 扩展作为"精选命名空间"的既有路径；与 C# LDM 对表接口演化如何削弱 (a)(b) 的长期需求。

---

## 附录：C# 生态与互操作考量

> 本附录由 ModVB 评估项目 meeting 追加 agent 补写，聚焦本提案与 dotnet/csharplang 官方生态的对应关系。正文与 RESOLUTION 不改写；本附录只补「C# 现实方向」证据，用于检验正文的三态判定与委托结论。来源目录 `..\..\..\csharplang`（与索引同源）。除注明「索引摘要」外，C# 引文均逐字摘自原文件并标注路径。

### 相关 C# 现实方向

本提案的主题是「版本化工具」——给库作者更多「不破坏二进制兼容」的演化能力。这个主题在 C#/CLR/.NET 生态里有一条**平行但方向相反**的主线：C# 不打算给元数据加"身份重定向"，而是把破坏性变更当作**语言版本治理**问题来管。

**治理框架（索引 T1 摘要，详版见 `Design-Process.md`）。** 语言由 C# LDT 在 Roslyn 中开发；提案需被 LDT 成员 champion、经 LDM 讨论后进 milestones（Working Set / Backlog / Any Time / Likely Never），实现后归档 `proposals/csharp-X.0/`，最终进 ECMA-334（滞后数年）；节奏约一年一个主版本、与 .NET 同步。两句原句照录：

> "It is not uncommon for a feature that is approved in theory to spend years in the backlog and/or working set before being implemented."
> → `Design-Process.md`（Triaged feature）

> "…the ECMA-334 committee is working on catching up as fast as they can, but is several years behind the language implementation."
> → `Design-Process.md`（Implemented feature）

**与"版本化工具"直接对口的 C# 提案：`proposals\breaking-change-warnings.md`（BCW，champion issue #8865）。** 它的方向与本提案 (a)(b) 的"元数据层防破坏"**相反**——不是阻止破坏，而是允许极少量破坏、再用语言版本机制提前警告。Summary 原句照录：

> "Allow very limited breaking changes in C# when this enables significantly simpler feature designs that are easier to learn, understand and use."
> "Retroactively add warnings in previous language versions to help identify and fix user code that would be vulnerable to such breaks upon a language version upgrade."
> → `proposals\breaking-change-warnings.md`（Summary）

BCW 的机制依赖 C# 的 **`LangVersion` 标量**——同一个新编译器编译旧语言版本，并在低版本下对"将来会破坏"的代码先布警告。Detailed design 原句照录（末词 `to compiler` 是原文笔误，非我校改）：

> "When a new language feature is added in C# version `n` that may cause existing code to error or work differently, such code is detected in C# versions `n - 1` and lower, and a warning is yielded with a suggestion for how to fortify the code against the future break."
> "It is customary that newer compilers are used to compile older versions of C#. The compiler that supports C# version `n` will implement these warnings when used to compiler older language versions. [原文如此]"
> → `proposals\breaking-change-warnings.md`（Detailed design）

LDM 2023-03-08 把"编译器按版本分叉绑定"当成最大的实现顾虑（原句照录）：

> "Today, the compiler tries very hard to not change its semantic behavior while binding. When we see a user taking advantage of a language feature in a newer language version than what they are targeting, we simply produce an error and continue binding. This would change the compiler to introduce new warnings in _lower_ language versions, and cause the compiler to potentially bind in different fashions for higher and lower versions."
> → `meetings\2023\LDM-2023-03-08.md`（Limited Breaking Changes in C#）

BCW 后续进展：2023-10-16 被加进 **Working Set**（原句照录："**Conclusion**: Added to the working set."，`meetings\2023\LDM-2023-10-16.md`）；2024-02-07 继续讨论，触及两条与本提案直接相关的治理决策（`meetings\2024\LDM-2024-02-07.md`）：一是 **post-upgrade 事后 opt-in 警告**（原句照录："we'd like to have users be able to opt-in to warnings _after_ an upgrade, in case they did not migrate using whatever garden-path approach we create."），二是**改动或移除 `latest` langversion**（原句照录："Are we ok with changing or removing the `latest` language version?"）。截至本附录核实，BCW 仍在 Working Set、未归档 `proposals/csharp-X.0/`，未落地。

**C# 有没有"元数据重定向"方向？——没有。** 整个 csharplang 仓库对 `TypeForwardedTo` / type forwarding 全库 Grep 零命中。C# 面对"类型/成员身份演化"的答案不在元数据，而在三处：(i) **default interface methods**（C# 8，接口演化，版本史原句照录）：

> "interfaces can now have members with default implementations, as well as static/private/protected/internal members except for state (ie. no fields)."
> → `Language-Version-History.md`（C# 8.0 条目）

(ii) **BCW**（语言版本级破坏治理，上文）；(iii) **库纪律**（`Obsolete` shim、门面类型、analyzer）。

**组导入在 C# 的先例：`global using`（C# 10）。** C# 有 `global using` 与 `using static`（C# 6），但都是"多文件重复导入 / 静态成员导入"，不是"库作者可定义的导入聚合单元"——正文 PROPOSAL C"没有可借鉴的稳定设计"可以从版本史核实：

> "`global using` directives avoid repeating the same `using` directives across many files in your program."
> → `Language-Version-History.md`（C# 10.0 条目）

### 现实 vs 提案

| 提案子项 | C#/CLR/.NET 现实 | 判定 | 理由 |
|---|---|---|---|
| (a) 跨命名空间移动 | 全库无 type forwarding 讨论；CLR 每类型单全限定名 | **兼容（委托成立）** | C# 也不发明元数据重定向；正文 RESOLUTION 2(a) 委托 C# LDM/运行时与事实一致 |
| (b) 类型/方法重命名保二进制兼容 | 同上；CLR 每成员单名 | **兼容（委托成立）** | C# 靠 shim + `Obsolete` + analyzer；无成员别名元数据 |
| (c) 元命名空间 `Imports Wpf` | `global using` 只是"多文件重复导入"，非聚合单元 | **脱节（C# 无参考）** | 纯 VB 原创；"无先例可抄"被版本史证实；C# 侧也不会提供 |
| (d) 更干净的 `System` | BCL 表面；C# 语言无权重排 | **兼容（双方都做不了）** | 非语言特性；`My` 是 VB 既有答案 |
| (e) 升级工具化 | BCW 的 "Upgrading tool" 替代方案讨论过同类设计 | **需桥接** | C# 判定为 "boil the ocean"；VB 小受众可能反而适合 |

**关键判定——BCW 与本提案是"同一个痛点的两条答案"，且 C# 已选了另一条：**

- **BCW（C#，时间维）**："允许极少破坏 + 低版本先警告"，治理标量是 `LangVersion`，**纯编译器侧、不需要 CLR 配合**；
- **本提案 (a)(b)（元数据维）**："给身份做别名，让破坏不发生"，需要 **CLR 改元数据布局**，VB/C# 都无权单方面给。

C# 选 BCW 有现实原因：它在编译器职权范围内（Motivation 原句照录）：

> "We currently restrict new C# language features from causing any breaks (errors or behavior changes) to existing code, occasionally leading to unnatural and unintuitive design choices that make the language harder than necessary to learn and reason about."
> → `proposals\breaking-change-warnings.md`（Motivation）

对照正文 深度追问 #11「CLR 不给，语言就不给」：BCW 恰好是"CLR 不用给"的那条路（编译器能自己走通）；(a)(b) 是"CLR 必须给"的路，C# 至今没拿到、也没在推进。这从证据上**加固**正文"(a)(b) 不是语言问题、委托 C#/CLR"的结论。

**版本漂移对表。** BCW 的 "Missed warnings" Drawback 与 PROPOSAL C 的"组漂移"是**同一类问题**——同一份源码在不同编译时刻解析到不同结果：

> "However, users might upgrade both without a single compile in between, or they may have turned off the warnings and forgot to turn them on again."
> → `proposals\breaking-change-warnings.md`（Drawbacks / Missed warnings）

BCW 承认这个窗口无法用语言堵死、只能靠工具兜底（事后 opt-in 警告、"check one last time" on explicit upgrade gestures）；PROPOSAL C 的"组新增命名空间 → 已编译消费者是否自动获得"因此也不该指望编译器静态保证——正文 `Probably` 不自动包含，与 C# 现实一致。

### 对 VBScript.NET 的适应建议

1. **确立版本治理的"标量"等价物。** BCW 能成立的前提是 C# 有 `LangVersion`——新编译器同时服务旧版本，才能"低版本布警告、高版本改行为"。VB 侧语义由正交的 `Option` 开关（Option Strict 等）主导（正文 深度追问 #6 已触及 Option Strict 分叉），`LangVersion` 在 VB 里不是同等的特性门。`.vbx` 若要复刻 BCW 的"默认安全、按需动态"（决策文件 M2），建议先定义自己的版本维度（如项目级版本属性 + `Option Strict` 分层），让破坏性行为变化在旧模式下先出警告。**这是本提案与 C# 生态最有价值的接合点**：C# 已验证"破坏可以治理"，`.vbx` 不必走"元数据防破坏"的绝路，可以走"警告治理"的熟路——尤其 C# 正在研究"事后 opt-in 警告"与"是否移除 `latest`"（LDM-2024-02-07），`.vbx` 可以观察这些取舍再定自己的治理粒度。
2. **工具化走 source-gen / analyzer 桥（决策文件 M5/M6）。** PROPOSAL B（组导入展开）与 (e) 升级工具化应做成 Roslyn analyzer/code-fix + 增量 generator，而不是运行时机制——与 C# source generators（C# 9/10）方向一致，AOT/trimming 友好。`Imports Wpf` 展开、升级迁移报告都是"编译期生成替代运行时动态"的典型场景。
3. **识别 C# 生态的新元数据（决策文件 M8）。** C# 8 DIM、C# 13 `allows ref struct`/`ref struct` 接口、以及 BCW 若落地可能引入的版本属性——`.vbx` 编译器必须认识这些才能消费现代 C# 库。DIM 尤其关键：它是 (a)(b) 长期需求的正向替代，正文立场"VB 只做消费端"意味着 .vbx 必须把 DIM 的消费做扎实。`field` 关键字已随 C# 14 落地（`Language-Version-History.md` C# 14.0 条目："`field` allows access to the property's backing field without having to declare it."）——它就是 BCW 设想的"极少量破坏"的一个活例子。
4. **引导式升级可以是 VB 的差异化强项。** C# 把"专用升级体验"评为 "boil the ocean"（Alternatives 原句照录）：

   > "This seems like a "boil the ocean" approach, since everyone has to take that route, even though the vast majority of users won't be affected by the breaking change."
   > → `proposals\breaking-change-warnings.md`（Alternatives / Upgrading tool）

   因为 C# 用户基数大、强制所有人走升级通道不值；但 VB/`.vbx` 是小众、高价值的迁移受众（Anthony 主张的 VB 特色），一条**显式的、可绕过的**升级助手反而贴合 VB"低仪式"的价值观（正文 (e) 引用的 2018-02-21 "Ick. Extra Ick…" 同源）。建议把 (e) 升级工具化做成 .vbx 的首发工具，而不是等 IDE 通用能力。

### 对既有 RESOLUTION / 三态判定的影响

- **不改变三态判定（Table）。** 本附录未发现任何"C# 正在做元数据重定向"的证据；恰恰相反，BCW 表明 C# 走的是"接受破坏 + 警告治理"而非"元数据防破坏"。正文结论——(a)(b) 非语言问题、委托 C#/CLR；整体 inactive 不激活——被 C# 现实**印证**而非推翻。
- **激活信号 3 部分得到回答。** 信号 3 问"运行时/CLR 侧对接口演化与类型转发的进展"。本附录补充：DIM 已在 C# 8 落地（版本史可查），BCW 在 Working Set 推进"语言版本级破坏治理"——两者都是 C#/运行时侧进展，且方向都是**让库作者对 (a)(b) 的需求萎缩**。信号 3 的判断（VB 只做消费端、需求会萎缩）方向正确。
- **给 RESOLUTION 2(a)(b) 补一条证据注脚**：委托 C# LDM/运行时的立场，与 BCW"纯编译器侧、不需要 CLR"的取舍形成对照——C# 不碰 (a)(b) 不是没想过，而是那条路要 CLR 改元数据、成本不可比；VB 更不该单方面发明。

### 引用与核实

- 逐字引文出处（本附录全部 C# 引文）：`proposals\breaking-change-warnings.md`（Summary / Motivation / Detailed design / Drawbacks / Alternatives）、`meetings\2023\LDM-2023-03-08.md`、`meetings\2023\LDM-2023-10-16.md`、`meetings\2024\LDM-2024-02-07.md`、`Language-Version-History.md`（C# 8.0 DIM、C# 10.0 global using、C# 14.0 field keyword）、`Design-Process.md`。均为逐字照录；BCW 原文 `to compiler` 笔误已标 `[原文如此]`。
- `OPEN QUESTIONS`：BCW 在 2024-02-07 之后是否有新 LDM/是否已归档——本附录未追 2024 年后半年与 2025–2026 meetings；最终形态（是否改/删 `latest` langversion、post-upgrade opt-in 警告如何落地）未定。
- `OPEN QUESTIONS`：VB/.vbx 是否引入 `LangVersion` 等价标量——VB 主线（只做兼容）与 Anthony 主张（VB 特色）在此拉锯；正文与索引均未给定论。
- `Suspect`：BCW 提案正文的示例写 "introduce [field access in auto-properties] in C# 13"，但 `Language-Version-History.md` 显示 `field` 关键字随 **C# 14** 落地——引用 BCW 原文的版本数字时注意其已过时。
