# 局部函数 / Local Functions

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**提案内容**：给 VB 引入**局部函数（local functions）**——在方法体（以及其它块）内直接用既有的 `Sub` / `Function` 声明写**具名**嵌套过程，可捕获外层局部变量与 `Me`，可直接递归与互递归，调用**不经委托**。

**一句话的定性**：局部函数与 lambda 的差别是**声明 vs 初始化**——lambda 是**表达式**（求值产生委托值，必须先有存放它的变量或目标类型），局部函数是**声明**（名字在块内直接可用，与任何变量无关）。ModVB 会议把这句话写得很清楚：「本地函数是**声明**而非**初始化**：没有声明顺序问题、没有 Nothing 委托陷阱、直接调用无需经委托装箱、调试器支持更好」（**实锤**：`..\modvb\meetings\meeting-recursive-lambda-inference.md:60` 逐字）。

**边界一（与递归 Lambda 提案的关系）**：本提案**不取代** `..\modvb\proposals\proposal-recursive-lambda-inference.md` 的「显式签名自引用递归 Lambda」；ModVB 会议已把两者裁决为**同一个工作项的两个阶段**：「本地函数是终点，递归 Lambda 是『不建语法就能先到 80%』的过渡。We think 它们应作为**同一个工作项的两阶段**，而不是互斥方案」（**实锤**：`meeting-recursive-lambda-inference.md:60` 逐字），RESOLUTION 6 逐字收口：「**本地函数工作项**：互递归与递归的完整解指向本地函数（#195 的意图）；本特性是零语法过渡阶段。两者归同一工作项，避免两套互递归语义。」（**实锤**：同文 RESOLUTION 6）。该会议给出的 VBScript.NET 优先级排序逐字是「**本地函数 > 显式签名自引用递归 Lambda > 互递归**」（**实锤**：同文 `:203`）。

**边界二（与 `proposal-local-declarations.md` 划清界限）**：`..\modvb\proposals\proposal-local-declarations.md` 改的是**局部变量声明**——引入 `Let` 关键字、元组解构、`As New` 数组/匿名类型（**实锤**：该文 Summary 逐条）。本提案改的是**局部过程声明**，不引入任何新的局部**变量**声明形式，也不依赖 `Let`（`Dim` 写法一并适用）。两者共享「方法体内的声明区」这一片文本，但轴不同：那条是「变量怎么写」，本条是「过程能不能在里面声明」。

**边界三（与 `proposal-top-level-code.md` 的关系）**：Anthony 的顶级代码提案问的是「顶层成员被放进什么宿主容器」，其 Unresolved 第一条逐字承认这一点未决：「顶层成员被放入什么宿主容器（**模块还是类**）、命名空间如何推断，**原文未说明**」（**实锤**：`..\modvb\proposals\proposal-top-level-code.md` Unresolved questions 第一条）。本提案**不回答**该问题——它假定容器问题已由既有的脚本方言模型解决，只补「方法体内能不能声明具名过程」这半格。

**边界四（与 `proposal-vbx-top-level-locals.md` 的关系）**：本提案**不是**那条提案的变体，也不为它背书。那条提案要把 `.vbx` 顶层变量从字段改成 `<Initialize>` 的局部，它的**阻塞项 T2** 正是「顶层 `Sub`/`Function` 今天靠成员身份访问顶层变量，locals 化之后怎么办」（**实锤**：`proposal-vbx-top-level-locals.md` §3 T2 与 Unresolved 1）。局部函数是 T2 的**候选载体之一**，但「要不要用」「怎么用」是那条提案的裁决点（见 Unresolved 9）。本提案的成立**不依赖**它。

**边界五（作用域，已裁）**：局部函数的作用域是**整个块**，**允许在定义点之前调用**（含递归与互递归）——作者定案，见 §3。这与 VB 局部**变量**的「声明点起可见」（`BC32000`）**有意分叉**：同一块内并存两条规则，是本决定的已知代价（Drawbacks）。

## Motivation
[motivation]: #motivation

### M1. 本提案的来历：一条调查撞出来的缺失零件（**实锤**）

它是在调查「`.vbx` 顶层变量做成 locals」时撞出来的。那条方向的关键一问是「顶层 `Sub`/`Function` 怎么办」，调查给出的结论是**两条路都是墙**，其中一条是：

> **路 B（方法不再是成员）**：VB **没有局部函数**，C# 那条「顶层语句 + 局部函数」的落地形状在 VB 里**不存在**。（**实锤**：`tmp\investigations\vbx-top-level-locals\submission-wall.md` W1）

该调查同时给出了局部函数缺失的三处机制证据（**实锤**：同文 Q4 路 B「关键锚点」）：

- `LocalFunctionStatementSyntax` 在 VB 全树**零命中**；`Compilers\VisualBasic\Portable` 里 `LocalFunction` 的**唯一**命中是 `Analysis\FlowAnalysis\VisualBasicDataFlowAnalysis.vb:289-293` 的 `UsedLocalFunctions`，它**硬编码返回 `ImmutableArray(Of IMethodSymbol).Empty`**——这是一个 **C# 侧有、VB 侧空置的桩**。
- 方法体内写 `Sub`/`Function` 被解析器路由到**声明解析**：`Parser.vb:1233-1247` 的 `SubKeyword, FunctionKeyword` 落在 `ParseDeclarationStatementInternal()` 那一支，注释逐字 `' This used to return a BadStatement with ERRID_InvInsideEndsProc. Just delegate to ParseDeclarationStatement and let the context add the error`。
- 实测 `.vbx` 内嵌 `Function` ⇒ `BC30289` + `BC30026` + `BC30429`，`exit 1`。

**本提案本轮独立复现了该实测**（**实锤**，探针 `tmp\extprobe\` 下，已删净）：`.vbx` 里在 `Sub Outer()` 体内写 `Sub Inner()` ⇒ `BC30026`（`:2` `'应为 "End Sub"'`——外层方法块被判定在嵌套 `Sub` 处收尾）+ `BC30289`（`:3` 语句不能出现在方法体内）+ `BC30429`（`:6` `End Sub` 前无匹配 `Sub`），`exit 1`。

### M2. 独立价值：本提案不是任何东西的附庸（**实锤**）

M1 只解释了它是**怎么被发现的**。它的价值与那条调查是否复活无关，有四面：

1. **递归 / 互递归的正统解。** ModVB 会议的 PROPOSAL C 逐字：「引入嵌套具名 `Function` / `Sub` 声明，天然支持递归与互递归，变量声明顺序与明确赋值规则**完全不动**。这是主线自己说过的方向。」（**实锤**：`meeting-recursive-lambda-inference.md:51`）。会议在 `:60` 进一步给出「为什么本地函数优于递归 Lambda」的四条：无声明顺序问题、无 Nothing 委托陷阱、不经委托装箱、调试器支持更好。**互递归那半**尤其只有局部函数能干净给出——递归 Lambda 的互递归在 ModVB 会议被 **Table**（RESOLUTION 4），因为「Lambda 未被提前调用」不可静态证明；局部函数把「调用」直接绑到符号（不是绑到变量里的委托值），这个前提问题**根本不出现**。
2. **主线自己说过的方向，且仓内有文字遗痕。** 2017 年 VB 主线否决 `Static` 属性变量（vblang #195）时逐字写：「**Rejected**. We don't see why this one narrow use case for members nested within other members get's special treatment. If we want to enable nesting we'd look at local functions and types as well.」（**实锤**：`..\vblang\meetings\2017\vbldm-notes-2017.11.15.md:16` 逐字）。ModVB 会议对这句的定性是「本地函数 = 主线 2017 会议（#195）表达过意图但无成稿」（**实锤**：`meeting-recursive-lambda-inference.md:170`）。
3. **C# 7 起已有，且是 C# 的递归 / 互递归正统解。** 本仓的 csharplang 镜像里**有**该提案原文（**实锤**：`InternalDevDocs\csharplang\proposals\csharp-7.0\local-functions.md`，本地镜像文件在册，非仅经 ModVB 会议转引）。其开篇逐字：「We extend C# to support the declaration of functions in block scope. Local functions may use (capture) variables from the enclosing scope.」（**实锤**：同文件 `:5` 逐字）。按 `..\decisions.md` **D5**（基础功能落地以 C#/csi 为设计蓝本），这是「C# 已照顾到」的用例。
4. **今天的 VB 用户没有等价物。** 递归只能走「具名私有方法 + `AddressOf`」或「先 `Dim f As Func(...)` 再赋值 Lambda」两语句惯用法；块内的短小助手只能写成 lambda 赋给委托局部变量，代价是**一次委托分配 + 显式委托类型**，且**不能写 `Static`**（`Binder_Statements.vb:1004-1005` → `ERR_StaticInLambda = 36672`，见 §5）。ModVB 会议对这条的定性逐字：「**`AddressOf` 具名方法**：VBScript 遗产里的递归正是『具名函数 + Call』；VBScript.NET 的递归正统应该是具名/本地函数，而非 Lambda。这支持 PROPOSAL C。」（**实锤**：`meeting-recursive-lambda-inference.md:147`）。

### M3. 本提案的代价性质（**待会议裁量**）

本提案**新增一条语言面**，直撞 VB 基因的原则 #3（不引入「第二种做事方式」）。它必须靠三点辩护：**声明 vs 初始化**是**形态差异**（不是口味差异）、**免委托**是可观察的机制收益、**递归/互递归**是现状下的能力缺口。D5 的落地约束仍适用：须说明「为什么 VB 必须分叉」——见 §3（作用域规则与 C# 的取舍）与 §5（扩展方法 / `Handles` 的排除）。

## Detailed design
[design]: #detailed-design

> **证据等级标注**：阶梯为 未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑。本节源码锚点均在工作树逐行复核（**已检查**）；标「实锤」的另有实测或规范逐字。**本阶段未改编译器、未加单元测试**，故不出现「已采纳」「有结果支撑」。
>
> **断言三态**：实锤 / 推测 / 猜测，逐条标注（未标注的按**猜测**处理）。

### 1. 现状基线（**实锤**，供设计对照）

| # | 事实 | 锚点 | 等级 |
|---|---|---|---|
| B1 | `LocalFunctionStatementSyntax` 在 VB 全树**零命中**；`LocalFunction` 在 `Compilers\VisualBasic\Portable` 源码**唯一**命中是 `UsedLocalFunctions`，硬编码 `Empty` | `Analysis\FlowAnalysis\VisualBasicDataFlowAnalysis.vb:289-293` | **实锤**（`grep` + 源码逐行） |
| B2 | 方法体内写 `Sub`/`Function` 被路由到**声明解析**（`ParseDeclarationStatementInternal`） | `Parser\Parser.vb:1233-1247`（含逐字注释） | **实锤**（源码逐行） |
| B3 | 实测 `.vbx` 内嵌具名 `Sub` ⇒ `BC30026` + `BC30289` + `BC30429`，`exit 1`（外层方法块在嵌套 `Sub` 处收尾，**不是**产出嵌套块） | 本轮实测（探针已删净）；同形结论见 `tmp\investigations\vbx-top-level-locals\submission-wall.md` W1 | **实锤**（已运行） |
| B4 | `MethodBlockSyntax` 的继承链是 `MethodBlockSyntax` : `MethodBlockBaseSyntax` : `DeclarationStatementSyntax` : `StatementSyntax` ⇒ 方法**块本身就是一个语句节点** | `Syntax\Syntax.xml:1208`、`:1232`、`:126` | **实锤**（源码逐行） |
| B5 | VB 的局部变量**从声明点起可见**；在声明点之前引用报 `BC32000`（`ERR_UseOfLocalBeforeDeclaration1`），判定与报错都在同一处 | `Errors\Errors.vb:1078`（= 32000）；`Binding\Binder_Expressions.vb:3104-3124`（`node.SpanStart < localSymbol.IdentifierToken.SpanStart` 的 span 比较 + `UseBeforeDeclarationResultType`） | **实锤**（源码逐行） |
| B6 | VB 的闭包捕获机制是 `LambdaRewriter`：分析捕获集 → 为每个作用域建 `LambdaFrame`（**编译器生成的类**）→ 把被捕获局部提升为它的字段 → 访问改写为 `FramePointer` | `Lowering\LambdaRewriter\LambdaRewriter.vb:15-53`（整段文档注释逐字描述该流水线） | **实锤**（源码逐行） |
| B7 | `LambdaFrame` **恒为类**，没有值类型帧的分支 | `LambdaRewriter\LambdaFrame.vb:214-218`（`TypeKind` 恒 `TypeKind.Class`）；`LambdaRewriter\` 目录 `IsValueType` / `TypeKind.Struct` / `isStruct` **零命中**（`grep`） | **实锤**（源码逐行 + `grep`） |
| B8 | 与之对照，**C# 的闭包环境可以是结构体**：`SynthesizedClosureEnvironment` 按分析结果选 `TypeKind.Struct` / `TypeKind.Class` | `Compilers\CSharp\Portable\Lowering\ClosureConversion\SynthesizedClosureEnvironment.cs:56` 逐字 `TypeKind = isStruct ? TypeKind.Struct : TypeKind.Class;`；`ClosureConversion.cs:397`（`env.IsStruct`） | **实锤**（源码逐行） |
| B9 | `Me` 在 lambda 体内被改写为帧指针：`VisitMeReference` → `FramePointer`；共享方法里出现 `Me` 是错误路径（原样返回） | `LambdaRewriter\LambdaRewriter.vb:687-699`；`MyBase`/`MyClass` 同族在 `:701-709` | **实锤**（源码逐行） |
| B10 | `Static` 局部**在 lambda 里被禁止** | `Binding\Binder_Statements.vb:998-1008`（`Me.IsInLambda` ⇒ `ERR_StaticInLambda`）；`Errors.vb:1464`（= 36672） | **实锤**（源码逐行） |
| B11 | `Static` 局部的实现是**外层类型上的合成字段**（值字段 + `$Init` 标志字段），其 `IsShared` 取自**包含成员**的 `IsShared` | `Symbols\Source\SynthesizedStaticLocalBackingField.vb:26-44`（`:31` 用 `implicitlyDefinedBy.ContainingType`、`:37` 用 `implicitlyDefinedBy.ContainingSymbol.IsShared`、`:40` `Debug.Assert(implicitlyDefinedBy.IsStatic)`） | **实锤**（源码逐行） |
| B12 | **扩展方法只能声明在命名空间层的类型上**：`MightContainExtensionMethods` 要求 `_containingSymbol.Kind = SymbolKind.Namespace` 且容器 `AllowsExtensionMethods()`；`AllowsExtensionMethods` 只认 `TypeKind.Module` 或脚本类 | `SourceMemberContainerTypeSymbol.vb:3336-3348`；`Symbols\NamedTypeSymbolExtensions.vb:108-111`；诊断 `ERR_ExtensionOnlyAllowedOnModuleSubOrFunction = 36550`（`Errors.vb:1327`） | **实锤**（源码逐行） |
| B13 | `Handles` 是**成员方法**的语法：子句绑定走 `SourceMemberMethodSymbol`，且 `Handles` 要求同类型上存在 `WithEvents` 变量 | `Symbols\Source\SourceMemberMethodSymbol.vb:581-600`；`ERR_NoWithEventsVarOnHandlesList = 30506`（`Errors.vb:403`） | **实锤**（源码逐行） |
| B14 | 今天 `.vbx` / REPL 的**顶层**成员方法之间调用与文本序无关（实测：先调 `Greet()`、后声明 `Sub Greet()` ⇒ 输出 `"hello"` / `"done"`，`exit 0`） | 本轮实测（探针已删净）；规范侧同向 `spec\spec-scripting-dialect.md:60` 逐字 `top-level methods can call one another directly` | **实锤**（已运行） |
| B15 | VB 的闭包降级**本来就带「拷贝构造分析」**，且**类帧是该语义的载体**：`seenBackBranches` 驱动决策、`symbolsCapturedWithoutCopyCtor` 作例外表；拷贝构造与否写进帧的类型（类构造器二选一）；`For Each` 控制变量提升硬断言必须拷贝构造；构造器延迟注册以待捕获集齐 | `LambdaRewriter.vb:233-248`（含 `:233-234` 逐字注释）、`:260-306`（`:298` 传参、`:269-271` 断言、`:305-306` 注释）；`LambdaFrame.vb:61-65` | **实锤**（源码逐行；运行未跑） |

### 2. 语法形态与承载位置

**形态（提案）**：块内写既有的 `Sub` / `Function` 声明，不加新关键字、不加新修饰符（v1）：

```vbnet
Sub Outer()
    Dim total As Integer = 0

    Sub Add(n As Integer)          ' 局部函数：具名、块内可见
        total += n                 ' 捕获外层局部变量 total
    End Sub

    Function Twice(n As Integer) As Integer   ' 返回类型显式（见 §6）
        Return n * 2
    End Function

    Add(Twice(21))
    Console.WriteLine(total)       ' 42
End Sub
```

**关键取舍：需要新语法节点吗？** 两条实锤把它压成一个实现选择，而不是设计问题：

- 解析器**已经**把块内的 `Sub` / `Function` 认成声明语句（B2）——但它在外层块上下文里**没有落点**，实测结果是外层方法块被提前收尾（B3），而不是产出嵌套块。
- `MethodBlockSyntax` **本身就是一个 `StatementSyntax`**（B4）——即 VB 的「方法块」在语法树上与 `If`/`For` 同属语句层，**句法上没有理由不能出现在语句列表里**。

⇒ **推测**：v1 可以**复用** `MethodStatementSyntax` + `MethodBlockSyntax`，只改**块上下文的接纳规则**（让方法体块接纳方法块作为语句），而**不新造** `LocalFunctionStatementSyntax`。这与 D5 的「延伸现有机制」取向一致，也与 C# 不同（C# 走的是**独立 node kind** `SyntaxKind.LocalFunctionStatement`）。**代价未核实**：块上下文、`BoundBlock` 语句列表、语义模型访问者、`BlockContext` 的 `End Sub` 配对，任何一处都可能把这条推测推翻——列入 Unresolved 1。

### 3. 作用域与「先定义后使用」——**已裁为「甲」**（整块可见、允许前向调用）

**决定（作者定案，2026-09-13）**：「局部函数这个**允许递归也允许定义在下面使用在上面**。」

⇒ **局部函数的作用域是整个块，允许在定义点之前调用。** 递归与互递归因此都成立。**断言三态：实锤**（作者决定，非本提案推断）；`(待会议确认)`。

这是**唯一一个 VB 必须自己作答、且答案会与 C# 分叉的设计点**——决定的结果是**在此点上跟 C#**，而不是跟 VB 的局部变量。

**C# 的答案（逐字，与本决定同向）**：局部函数的作用域是**整个块**，可以在定义点之前调用——

> "Local functions may be called from a lexical point before its definition. Local function declaration statements do not cause a warning when they are not reachable."
> —— **实锤**：`InternalDevDocs\csharplang\proposals\csharp-7.0\local-functions.md`（开篇段，逐字；同句经 `meeting-recursive-lambda-inference.md:263` 转引）

**VB 的既有裁法（逐字，本决定有意偏离它）**：局部**变量**的作用域**从声明点开始**，之前引用报 `BC32000`——判定就是一处 span 比较（B5）。这不是历史包袱而是**活跃的规范**：`proposal-top-level-implicit-shared` 的聚焦复会已裁出「VB 对『引用点早于声明』的既有裁法是 **BC32000**（与 `Option Explicit` 无关）」（**实锤**：`..\meetings\inactive\meeting-top-level-implicit-shared.md`，该裁定写在 `proposals\README.md` #22 条目内）。**⇒ 本决定之后，同一个块内并存两条作用域规则：局部函数整块可见（本节），局部变量声明点起可见（B5，不动）。** 这是本决定的**已知代价**，见 Drawbacks。

**采纳「甲」的理由**（三条，按证据强度排）：

1. **「乙」直接消灭互递归**，而互递归是本提案存在的根本理由（`meeting-recursive-lambda-inference.md:51` 逐字；会议把递归 Lambda 的互递归 **Table** 掉，正是因为它给不出干净解）。选「乙」等于把本提案的价值砍到「比两语句惯用法少写一行」——与作者「允许递归也允许互递归」的意图正面冲突。
2. **不跟 C# 反而制造新的不一致**：VB 的**成员方法**之间调用与文本序无关（B14，`exit 0` 实测；`spec-scripting-dialect.md:60` 逐字 `top-level methods can call one another directly`）。若块内的具名过程**不能**前向调用，同一个语言里「具名过程」的可见性就按**位置**（成员 vs 块内）分成两套——这比「局部函数整块可见」与「局部变量声明点可见」的分裂更难解释。
3. **重绑定风险面有现成对冲**，不需要新造规则：ModVB 会议为同一片实现面（放宽「声明前引用」）已裁决「**启用型**重绑定防护：放宽只作用于『当前绑定失败的引用』；先前成功绑定到外层成员的名字，绑定原样保留」（**实锤**：`meeting-recursive-lambda-inference.md` RESOLUTION 5）。局部函数照此办理即可：**只有在块内该名字当前不绑定到任何外层成员时，整块可见性才生效**。

**必须明说的残余代价**：本决定引入一个**与 C# 同构、但 VB 前所无**的风险面——同一段源码在「先引用后声明」位置上，今天绑定到外层成员、加特性后绑定到局部函数。这正是设计原则 #7（避免隐蔽语义变化）所警惕的（`Return?` 被拒的同类理由）。**「启用型」防护是本决定的配套条件，缺它不可接受**（同会议对递归 Lambda 的原话立场）；该防护的确切形态仍待会议裁定，见 Unresolved 3。

**被否的两个候选**（乙 / 丙）连同各自代价已移入 Alternatives **F** / **G** 记录，不作删除。

### 4. 捕获与 `Me` / 实例上下文

**捕获机制：复用既有的 lambda 闭包流水线**（**推测**：机制同形是实锤，落地是否无障碍未验证）。

VB 的闭包捕获只有一条实现路径（B6）：`LambdaRewriter` 分析捕获集 → 建 `LambdaFrame`（生成的类）→ 被捕获局部成为它的字段 → 体被搬进该帧类的一个生成方法。局部函数需要的三件事与 lambda **逐条相同**：

| 局部函数需要 | lambda 的既有机制 | 锚点 |
|---|---|---|
| 哪些外层局部被引用 | `Analysis` / 捕获集 | `LambdaRewriter.vb:22-29` |
| 把它们放到哪里 | `LambdaFrame` + `LambdaCapturedVariable` | `LambdaRewriter.vb:26-29`；`LambdaFrame.vb:29-30` |
| 怎么访问 | `FramePointer` 改写 | `LambdaRewriter.vb:385-409` |
| `Me` 怎么办 | `VisitMeReference` → `FramePointer` | B9 |

**与 lambda 的唯一机制差异**：lambda 的最终产物是**委托创建表达式**（`LambdaRewriter.vb:37-39`：`Lambda expressions are turned into delegate creation expressions`）；局部函数的**直接调用**应当发射为对合成方法的**直接调用**（C# 侧明文：`A call to a local function is emitted as `call` rather than `callvirt`，regardless of whether the local function is `static`.」——**实锤**：`InternalDevDocs\csharplang\proposals\csharp-8.0\static-local-functions.md` Detailed design 逐字）。**只有把局部函数转成委托时**（`AddressOf LocalFn` / 赋值到 `Func(...)`）才落回 lambda 那条委托创建路径。

**`Me` 与实例上下文**：

- 在**实例方法**里声明的局部函数可以引用 `Me` 与外层实例成员——实现上就是 B9 的 `FramePointer` 改写，与 lambda 同形。
- 在**共享方法**里声明的局部函数**没有** `Me`——`LambdaRewriter` 的 `VisitMeReference` 对 `_topLevelMethod.IsShared` 直接原样返回（`:692-696`，注释逐字 `this can happen only in a case of errors`），即该路径**不是**设计语义而只是错误兜底。⇒ 局部函数要沿用同一约束：共享上下文里无隐式接收者，写 `Me` 走既有的「共享上下文不得引用实例成员」诊断（**实锤**：VB 既有 `BC30469` 族；本提案不新增规则）。
- **不新增**「局部函数是不是 `Shared`」这个问题：局部函数不是类型成员，不参与 `Shared` / 实例二元划分（与 B12/B13 同理）。

### 5. 与既有特性的交互

**① 与 `Static` 局部（任务点名）。** 今天是三条并行规则（B10）：结构体方法里禁止、**lambda 里禁止**、泛型方法里禁止。

- **本提案的 v1 建议：局部函数体内仍禁止 `Static` 局部**，沿用 B10 的同一条判据，只是把 `Me.IsInLambda` 扩成「处于 lambda 或局部函数体内」。理由：`Static` 局部的实现要求一个**稳定的外层类型**来挂合成字段（B11：`implicitlyDefinedBy.ContainingType` / `ContainingSymbol.IsShared`）；局部函数虽然**有**稳定的包含类型（局部函数最终是包含类上的合成方法，见 §2/C# 同形），但在**捕获帧**存在时，"per-instance 持久"的语义与帧的生命期要重新对齐——这一格未验证，v1 不宜顺手放开。**列入 Unresolved 4。**
- **注意术语陷阱（实锤）**：C# 用一个 `static` 修饰符表达两件不同的事——「不捕获」（C# 8 static local functions）与「局部变量持久化」（VB 的 `Static` 局部）。VB 的 `Static` 关键字已被后者占用（B10/B11），**不能**直接借来表示「不捕获」；若将来要「不捕获」变体，需要另选关键字或措辞。**列入 Unresolved 5。**

**② 与 `Imports`。** 局部函数体内**不能**写 `Imports`——这不是新规则：今天在方法体内写 `Imports` 同样被路由到声明解析（`Parser.vb:1229-1231` 的 `ImportsKeyword` 就在 B2 那张表里）并报 `BC30289`。局部函数沿用「块内不得声明 `Imports`」，与 lambda 一致，**零新增**。

**③ 与扩展方法（实锤，定义上排除）。** 局部函数**不可能**是扩展方法：收集侧要求声明容器 `_containingSymbol.Kind = SymbolKind.Namespace`（B12），局部函数**不在任何类型内**；诊断侧 `<Extension>` 只允许在 `Module` / 脚本类的 `Sub`/`Function` 上（B12）。⇒ 建议：局部函数声明带 `<Extension>` 时给一条**明确诊断**，而不是静默失效——具体诊断取「复用 `ERR_ExtensionOnlyAllowedOnModuleSubOrFunction`（36550）」还是新造，**列入 Unresolved 6**。这与 `proposal-script-extension-methods.md`（Active #19）已确立的「通道要显式，不要静默」基调一致。

**④ 与 `Handles`（实锤，同理排除）。** `Handles` 是**成员方法**的语法，其绑定要求同类型上存在 `WithEvents` 变量（B13）。局部函数不是成员 ⇒ 不可带 `Handles` 子句。⇒ 同样建议明确诊断。**另注**：`spec\spec-scripting-dialect.md:348` 已述「提交类里 `Handles` 今天不支持（不报诊断、编译不完成）」，即该区域本身就有一笔既有缺陷，本提案**不试图**在那里开出新面。

**⑤ 与 `Async` / `Iterator` / 泛型 / 重载。** C# 的语法文法允许 `async` 与 `unsafe` 修饰局部函数（**实锤**：`proposals\csharp-7.0\local-functions.md` 的 `local-function-modifiers : (async | unsafe)`），C# 9 起还允许**特性**与 `extern`（**实锤**：`proposals\csharp-9.0\local-function-attributes.md` 逐字 `Local function declarations are now permitted to have attributes`）。VB 侧对应关系：

- VB 的 `Async` / `Iterator` 是方法修饰符，局部函数作为**合成方法**天然可参与状态机重写；但 v1 是否开放属范围问题。
- **特性**：VB 的 lambda **参数**明确**禁止**特性（`ERR_LambdasCannotHaveAttributes = 36634`，`Errors.vb:1417`，报错点在 `UnboundLambdaParameterSymbol.vb:80`；lambda 返回类型另有 `ERR_AttributeOnLambdaReturnType = 36677`）；局部函数跟 C# 还是跟 VB 的 lambda，需裁。
- **重载**：C# 允许局部函数重载，VB 的方法重载靠 `Overloads` / 签名区分；局部函数是否支持重载需裁。
- 以上四项**统一列入 Unresolved 7**，v1 建议**全部收窄为不支持**（最小面），逐条给诊断。

### 6. 与泛型推断、明确赋值的关系（任务点名）

**ModVB 会议 A/B/C/D 对照里 C 的理由，逐字**：「引入嵌套具名 `Function` / `Sub` 声明，天然支持递归与互递归，变量声明顺序与明确赋值规则**完全不动**。」（**实锤**：`meeting-recursive-lambda-inference.md:51`）

这条理由是**可锚定的**：局部函数不触碰 B5 那处 span 比较，也不触碰 `UseBeforeDeclarationResultType`——因为递归经**符号**（局部函数名）而非经**变量里的委托值**，**没有「变量尚未赋值」这个中间状态**。这正是会议在 `:60` 说的「没有 Nothing 委托陷阱」。

**但有两个连带面必须写清**：

1. **若选「甲」（整块可见，§3），上述「完全不动」只对变量成立，对函数名不成立**——「定义点之前调用」需要把函数名前置到块顶，那是对**名字查找顺序**的改动（不是对明确赋值规则的改动）。会议对「宽松放宽」的批评之一正是「放宽声明顺序触及语言地基」；局部函数把这一刀缩到**只切函数名、不切变量**，比递归 Lambda 那条路窄得多，但**不是零**。
2. **`Function` 的返回类型必须显式**（v1 建议）。依据是 C# 自己的修订注记，逐字：「The new definite assignment rules are incompatible with inferring the return type of a local function, so we'll likely be removing support for inferring the return type.」（**实锤**：`InternalDevDocs\csharplang\proposals\csharp-7.0\local-functions.md` 逐字，**本地镜像原文**；ModVB 会议在 `meeting-recursive-lambda-inference.md:265-266` 转引过同文件的两段）。**注意 VB 与 C# 的关键差异**：VB 里 `Function F()` 缺 `As` 子句**不是**「推断」而是**返回 `Object`**——所以 VB 的这条规则要么写成「要求显式 `As` 子句」，要么接受 `Object`；两者语义不同，**列入 Unresolved 8**。
3. **泛型局部函数**：C# 文法允许局部函数带**自己的**类型参数列表（`type-parameter-list?`）与约束子句（**实锤**：`proposals\csharp-7.0\local-functions.md` 文法逐字）。VB 侧照搬需要处理「局部函数被转成委托时泛型参数怎么办」——**未核实**，v1 建议收窄为不支持（与 §5 ⑤ 同批）。

### 7. 意图示例（**机制待验证**，仅供读者看清方向）

**递归**（对比今天的两语句 lambda 惯用法；两段都在**方法体内**）：

```vbnet
Sub Demo()
    ' 今天：必须先把委托变量声明出来，Lambda 体里才能引用自己
    Dim factorial As Func(Of Integer, Integer) =
        Function(n As Integer) As Integer
            Return If(n = 0, 1, n * factorial(n - 1))
        End Function

    ' 本提案：声明即可自引用，无需中间变量，调用不经委托
    Function Fact(n As Integer) As Integer
        Return If(n = 0, 1, n * Fact(n - 1))
    End Function

    Console.WriteLine(factorial(5))     ' 120（今天）
    Console.WriteLine(Fact(5))          ' 120（本提案）
End Sub
```

**互递归**（**本提案相对递归 Lambda 的独有增量**；ModVB 会议把递归 Lambda 的互递归 Table 掉，见 RESOLUTION 4）：

```vbnet
Sub Demo()
    Function IsEven(n As Integer) As Boolean
        If n = 0 Then Return True
        Return IsOdd(n - 1)          ' 前向引用：§3 已裁为「甲」，成立
    End Function

    Function IsOdd(n As Integer) As Boolean
        If n = 0 Then Return False
        Return IsEven(n - 1)         ' 后向引用：三种候选都成立
    End Function

    Console.WriteLine(IsEven(10))    ' True
End Sub
```

**捕获（免委托）**：

```vbnet
Sub Process()
    Dim hits As Integer = 0
    Dim log As New List(Of String)

    Sub Record(text As String)
        hits += 1                    ' 捕获 hits 与 log，但 Record 自身不是委托值
        log.Add(text)
    End Sub

    Record("a")
    Record("b")
    Console.WriteLine(hits)          ' 2
End Sub
```

**与现状的机制差异**（**推测**，未跑）：`Record` 的直接调用应当发射为**直接方法调用**，捕获的 `hits` / `log` 走帧字段——但在 VB **今天**帧**恒为类**（B7），所以「无 GC 压力」这句在 VB 侧**不成立**（见 Drawbacks）。

### 8. 规范与实现影响面

**`spec\spec-scripting-dialect.md` 的顶层四形式映射表不受影响。** 该表（表头 `:13`，逐行 `:15` / `:16` / `:17` / `:18`——照 `pitfalls.md` P-018 的行首单元格引用纪律，不按表号范围引）的四行讲的是**编译单元的顶层形式**；局部函数是**方法体内**的声明，不在表内。⇒ 本提案**不**推翻该表，也**不**需要为它加分叉行。这是与 `proposal-vbx-top-level-locals.md` 的关键区别：那条提案要改 `:15`/`:16`/`:58`/`:60` 整整一列，本提案只**增写**一节。

**必改/必增（`.vbx` 与普通 VB 同等）**：

| 对象 | 动作 | 依据 |
|---|---|---|
| `..\vblang\spec\statements.md` | **新增**「Local functions」一节（语法、作用域、明确赋值、`Me`） | 该文件今天**无**任何 local function 文本（**实锤**：`tmp\investigations\vbx-top-level-locals\submission-wall.md` Q4 路 B「规范侧同向」） |
| `VisualBasicDataFlowAnalysis.vb:289-293` | `UsedLocalFunctions` 从**硬编码 `Empty`** 改为**真实实现** | B1（该桩存在即可证明数据流 API 已预留该面） |
| `Parser.vb:1233-1247` 一带的块上下文 | 让方法体块**接纳**方法块语句（或按 Unresolved 1 另立 node kind） | B2 + B3 + B4 |
| `Binder_Expressions.vb:3104-3124`（B5） | **不改**——「甲」（§3，已裁）下**变量**规则原样保留；局部**函数名**的整块可见性走**独立的**查找路径，**不**触碰这处 span 比较 | `meeting-recursive-lambda-inference.md:51` 逐字 |
| `Lowering\LambdaRewriter\` | 扩展为「lambda + 局部函数」共用捕获分析；新增**直接调用**形态（`call` 而非委托创建）；帧仍用**类**（结构体帧＝独立工作项，见 Unresolved 12） | B6 + B9 + B15 + C# 侧逐字 |

**明确不做**：不改 `TypeKind` / `DeclarationKind` / 脚本类容器模型；不改顶层 `Dim` 的存储类别（那是 `proposal-vbx-top-level-locals.md` 的轴）；不改 `#R` / `#Load` / 提交链。

## Drawbacks
[drawbacks]: #drawbacks

- **C# 的「零 GC 压力」论据在 VB 今天不成立（实锤）；但作者判定这是可实现项，不是「不可跟」。** C# 原文逐字：「Unless you convert a local function to a delegate, capturing is done into frames that are value types. That means you don't get any GC pressure from using local functions with capturing.」（**实锤**：`proposals\csharp-7.0\local-functions.md` 逐字；经 `meeting-recursive-lambda-inference.md:267` 转引）。而 VB 的 `LambdaFrame` **恒为类**（B7），C# 的 `SynthesizedClosureEnvironment` 才按 `isStruct` 分叉（B8）——**这是现状，不是结论**。作者判定（2026-09-13）：「这个优化 VB 可以跟，这不会产生 behavioral change，只是优化了某个语法的内部实现。」⇒ 本条的定性从「VB 拿不到该收益」改写为「**现状如此，属可实现项**」：目标是**无可观察行为变化**，代价全在实现侧（**下一行给出定价**）。本提案的**基础**机制收益仍应表述为**免一次委托分配 + 名字直接可绑定**（这两条不需要额外交付）；结构体帧是**追加的优化项**，不与本提案的 v1 面绑定。
- **【定价】「结构体帧」不是一行小改：它要把帧的表示换成值类型，并保住现有拷贝构造语义逐字不变。** VB 的闭包降级**本来就带一套「拷贝构造分析」**，而且**类帧正是这套语义的载体**——`LambdaRewriter.vb:233-234` 逐字：

  ```vb
  ' There is a simple test to determine whether to do copy-construction:
  ' If method contains a backward branch, then all closures should attempt copy-construction.
  ```

  机制链（**实锤**，源码逐行；本阶段**未跑**）：`seenBackBranches` 驱动整个决策（`LambdaRewriter.vb:240` `Dim copyConstructor = _analysis.seenBackBranches`）；逐符号过例外表 `_analysis.symbolsCapturedWithoutCopyCtor`（`:238-239` 注释 + `:298`）；建帧时把「要不要拷贝构造」写进**帧本身的类型**（`:293-300` 把 `copyConstructor AndAlso Not _analysis.symbolsCapturedWithoutCopyCtor.Contains(captured)` 传给 `LambdaFrame`，后者据此在 `SynthesizedLambdaConstructor` 与 `SynthesizedLambdaCopyConstructor` 之间选，`LambdaFrame.vb:61-65`）；`For Each` 控制变量的提升**硬断言**必须走拷贝构造（`:269-271`）；构造器**延迟注册**，等的正是「全部被捕获局部到齐后才能生成拷贝构造器」（`:305-306` 注释逐字 `we need them to generate copy constructor, if needed`）。

  ⇒ 定价是**两件事**，不是一件：① 把帧的**表示**换成值类型（C# 侧蓝本 `SynthesizedClosureEnvironment.cs:56`）；② 在值类型表示下**保持上述语义逐字不变**——尤其是「**跨回边（back branch）时旧帧的可观察身份**」与「例外表 `symbolsCapturedWithoutCopyCtor` 的成员**仍不得**被拷贝构造」这两条。**难点不在 ① 而在 ②：证明它没有可观察行为变化**——值类型拷贝语义与类引用语义在「闭包跨迭代共享」一类形状上分叉，须**逐形状验证**（本阶段**未验证**，标**推测**）。⇒ 作为**独立工作项**跟踪，不并入本提案 v1 面（Unresolved 12）。
- **「第二种做事方式」风险（原则 #3）。** 块内的具名过程与「lambda 赋给委托局部变量」功能重叠；且顶层 `Sub`/`Function`（成员）与块内 `Sub`/`Function`（局部）**拼写完全相同、可见性规则不同**。辩护只能靠「声明 vs 初始化」的形态差异 + 免委托 + 递归。**此条与兼容性无关，`decisions.md` D6 管不到**（D6 只解掉「兼容性」一条否决理由）。
- **作用域规则的两套并存（§3，已裁为「甲」）。** 局部函数整块可见、局部变量声明点起可见（B5）——同一块内两条规则。且引入**名字重绑定**风险面，必须靠「启用型」防护对冲（RESOLUTION 5），没有它本提案不可接受。
- **实现面大，且 VB 侧零存量。** C# 有完整的 `LocalFunctionSymbol` / `LocalFunctionOrSourceMemberMethodSymbol` / `MakeLocalFunctionName`（**实锤**：`Compilers\CSharp\Portable\Symbols\Source\LocalFunctionSymbol.cs:17`；`Symbols\Synthesized\GeneratedNames.cs:118-125`），而 VB 侧 `LocalFunctionStatementSyntax` **零命中**（B1）。按 D5，C# 可作蓝本，但**移植量级**（parser 块上下文 / binder / 语义模型 / 数据流 / Lowering / IDE / EnC）本阶段**未定价**。
- **`Me` 捕获使「局部函数」不是纯函数。** 实例方法里的局部函数隐式捕获 `Me`（B9 同形），「移到别处」「变成静态」都不平凡；这与用户对「局部小助手」的直觉有落差。
- **未决项多。** §5/§6 的收窄清单（`Static` / 特性 / 泛型 / 重载 / `Async` / 诊断）逐条都还开着，见 Unresolved 4–8。**这些不定，Detailed design 不算完成。**

## Alternatives
[alternatives]: #alternatives

### A. 维持现状（**当前基线**）

- 内容：递归用「具名私有方法 + `AddressOf`」或「先 `Dim f As Func(...)` 再赋值 Lambda」；块内助手写成 lambda 赋给委托局部变量。
- 收益：零机制工作；VB 局部名字规则（B5）不动；与 C# 的差异面不扩大。
- 代价：递归必须两语句；**互递归**在 VB 侧只能靠「先声明两个委托变量」（**推测**：vanilla 是否已能编译未核实——ModVB 会议把这列为「原型必须钉死的第一件事」）；每次块内助手都要付一次委托分配；lambda 里不能写 `Static`（B10）。
- **注**：这是本提案**必须打败的对照**。

### B. 只做「显式签名自引用递归 Lambda」（`proposal-recursive-lambda-inference.md`，ModVB RESOLUTION 3）

- 内容：零新语法，`Let factorial = Function(n As Integer) As Integer … End Function` 自引用。
- 收益：递归样板少一行；不动声明系统。
- 代价：**互递归被 Table**（RESOLUTION 4）；仍要付委托；不解决「块内具名过程」这个形态缺口。
- **与本提案的关系**：会议已裁为**同一工作项的两阶段**（RESOLUTION 6），**不是竞争者**。但**若只允许落一个**，本提案的优先级更高（`:203` 逐字「本地函数 > 显式签名自引用递归 Lambda > 互递归」）。

### C. 只把 `UsedLocalFunctions` 桩补上，不改语法

- 内容：把 `VisualBasicDataFlowAnalysis.vb:289-293` 的空实现换成真实现。
- **为什么不构成替代**：该桩在**没有局部函数语法**时**无输入可分析**（B1：`LocalFunctionStatementSyntax` 零命中）——补它等于给一条永远不走的路径写实现。**登记为反例**，供读者排除「先补桩、后补语法」的幻想。

### D. 窄化版：不允许捕获、不允许前向调用（最保守的局部函数）

- 内容：只允许**不捕获外层状态**、且**定义点之后**才可调用的局部函数（≈ C# 8 的 `static` 局部函数形态，但连前向调用也不要）。
- 收益：完全不动 B5 的明确赋值与声明顺序；`Me` 捕获问题消失。
- 代价：**互递归与递归的价值大半消失**（递归仍可，互递归不行）；价值退化为「块内具名过程」，比 lambda 优势有限。
- 登记为**保守回退路线**：若会议判定 §3 的「甲」不可接受，这是唯一仍能落地的收窄形态。

### E. 顶层 `Sub`/`Function` locals 化（`proposal-vbx-top-level-locals.md` 的路 B）

- 内容：把 `.vbx` 的顶层过程降成 `<Initialize>` 的局部函数。
- **与本提案的关系：消费者，不是替代品。** 它**依赖**本提案（或某种等价载体）才能表达；本提案**不替**它决定顶层过程的成员资格（Summary 边界四）。

### F. 作用域取「乙」——定义点起可见（**§3 已否，存档**）

- 内容：局部函数作用域从**定义点**起，之前调用报 `BC32000` 同族错误（与 B5 的局部变量规则**完全一致**）。
- 收益：**保住局部名字规则的单一性**——同一块内只有一条作用域规则；`BC32000` 的判定点（`Binder_Expressions.vb:3104-3124` 的 span 比较）原样复用，零新增绑定机制。
- 代价：**互递归不成立**（互递归必有一方要前向引用）；自引用递归虽成立（定义点之后），但本提案的价值退化为「比两语句惯用法少写一行」。与 `meeting-recursive-lambda-inference.md:51` 逐字「天然支持递归与**互递归**」的 PROPOSAL C 定位正面冲突，也与作者「允许递归也允许定义在下面使用在上面」的意图冲突。
- **否决理由**：把本提案的头号理由（互递归）删掉，收益/代价不成比例。

### G. 作用域取「丙」——函数名前置、变量不动（**§3 已否，存档**）

- 内容：把块内**局部函数名**前置到块顶（声明束），局部**变量**维持声明点起可见。
- 收益：表面上是「两全」——递归与互递归都成立，且变量规则不被牵动。
- 代价：与「甲」**同风险**（都引入整块可见性与名字重绑定面），只是把「分裂」从「函数 vs 变量」挪到「函数名前置 vs 变量不前置」这一刀上；而且它**多引入一个概念**（"声明束"的两遍绑定），对 IDE 增量绑定与 EnC 的影响面比甲更大（`meeting-recursive-lambda-inference.md` 第 7 条已把「声明束的两遍绑定」标为 IDE/EnC 风险项）。
- **否决理由**：与甲担同样的风险，却多一层机制与 IDE 代价；**甲是更窄的实现**。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **【阻塞】语法承载方式**（§2）。复用 `MethodStatementSyntax` + `MethodBlockSyntax` 只改块上下文接纳规则（**推测**，见 §2），还是照 C# 新造 `LocalFunctionStatementSyntax` 等价节点？前者省一层语义模型改动、后者与 C# 同形。**未验证**：块上下文 / `BoundBlock` 语句列表 / 访问者 / `End Sub` 配对任一处都可能推翻复用方案。
2. **【已定，待会议确认】作用域与「先定义后使用」**（§3）。**作者已定案为「甲」**（整块可见、允许前向调用，2026-09-13），乙 / 丙 已否并存档于 Alternatives F / G。⇒ 本项**不再是开放取舍**，只剩「会议确认」这一道程序；**确认前不改实现**。
3. **「启用型」重绑定防护在本提案的确切形态**。§3 的「甲」把它作为**配套条件**（缺它不可接受）。照 `meeting-recursive-lambda-inference.md` RESOLUTION 5 逐字办，还是需要为「声明 vs 初始化」的差异改写？与 TypeOf 会议的双轨试探共写 speclet 是否仍然成立？
4. **局部函数体内是否允许 `Static` 局部**（§5 ①）。沿用 B10 的禁止（v1 建议），还是放开？放开时 `SynthesizedStaticLocalBackingField` 的 `ContainingType` / `IsShared`（B11）取什么？
5. **「不捕获」变体的关键字**（§5 ①）。C# 用 `static`；VB 的 `Static` 已被「局部变量持久化」占用（B10/B11）。用 `Shared`？还是 v1 不做？
6. **`<Extension>` / `Handles` 落在局部函数上的诊断**（§5 ③④）。复用现有诊断码还是新造？`Handles` 一侧还要与 `spec\spec-scripting-dialect.md:348` 的既有缺陷（提交类 `Handles` 不报诊断、编译不完成）划清边界。
7. **`Async` / `Iterator` / 特性 / 重载 / 泛型局部函数**（§5 ⑤、§6 第 3 条）。v1 建议**全部收窄为不支持**并逐条给诊断——但这是范围决定，需会议确认，且「收窄」本身要给出一致的诊断族。
8. **`Function` 缺 `As` 子句的语义**（§6 第 2 条）。VB 里那是**返回 `Object`**，不是推断。⇒ 局部函数是要求显式 `As` 子句（照 C# 对推断的收窄方向），还是接受 `Object`？两者用户可见后果不同。
9. **与 `proposal-vbx-top-level-locals.md` 的接口**（Summary 边界四）。若那条提案复活，局部函数是否就是它 T2 的载体？**本提案不作承诺**——顶层过程的成员资格是那条提案的裁决点；本提案只保证「方法体内可以有具名过程」这个更窄的命题。
10. **C# 有、VB 空置的其它桩是否成体系**。本轮只确认了 `UsedLocalFunctions`（B1）这一处。`grep` 范围限于 `Compilers\VisualBasic\Portable` 的 `LocalFunction` 关键字，**未**系统性排查「C# 有语义、VB 空实现」的接口成员——若成体系，本提案的施工面要加上这些桩的清单。
11. **IDE / EnC 影响面**。C# 的局部函数在 EnC 下有额外规则；VB 侧零存量，**未评估**。
12. **【独立工作项，不并入 v1】结构体帧（零 GC 压力）**（Drawbacks 定价条）。作者判定**可以跟**（纯内部实现优化、预期无可观察行为变化）。但「换成值类型」必须**保住现有拷贝构造语义逐字不变**（B15：`seenBackBranches` 驱动的帧类型二选一 + `symbolsCapturedWithoutCopyCtor` 例外表 + `For Each` 控制变量断言 + 构造器延迟注册）。⇒ 未定项是：**② 的证明**——值类型帧在「跨回边 / 跨迭代共享」一类形状上是否**真的**无可观察行为变化？需要逐形状验证（**未验证**），并说明与 `LambdaFrame.vb:61-65` 的构造器二选一机制如何映射到值类型。**本项不阻塞本提案 v1**（v1 可以继续用类帧，只是拿不到零 GC 压力）。

## 全称主张自检

> 纪律：全文每一个全称主张（无 / 都 / 任何 / 唯一 / 不可能 / 零命中）逐条给 `文件:行号` 或实测；举不出证据即降级。

| # | 主张 | 证据 | 判定 |
|---|---|---|---|
| 1 | `LocalFunctionStatementSyntax` 在 VB 全树**零命中**；`LocalFunction` 在 VB 源码**唯一**命中是 `UsedLocalFunctions`（硬编码 `Empty`） | `grep`（排除 `bin`/`obj`）；`VisualBasicDataFlowAnalysis.vb:289-293` | **实锤**（`grep` + 源码逐行；范围限 `Compilers\VisualBasic`） |
| 2 | 块内 `Sub`/`Function` **被**路由到声明解析 | `Parser.vb:1233-1247`（含逐字注释） | **实锤**（源码逐行） |
| 3 | 实测块内具名 `Sub` ⇒ `BC30026` + `BC30289` + `BC30429`，`exit 1`；外层方法块在嵌套 `Sub` 处收尾，**不是**产出嵌套块 | 本轮实测（`.vbx`，探针已删净） | **实锤**（已运行） |
| 4 | `MethodBlockSyntax` 继承链的根是 `StatementSyntax`（方法块本身就是语句） | `Syntax.xml:1208`、`:1232`、`:126` | **实锤**（源码逐行） |
| 5 | VB 局部变量**从声明点起**可见；之前引用报 `BC32000` | `Errors.vb:1078`；`Binder_Expressions.vb:3104-3124` | **实锤**（源码逐行） |
| 6 | VB 的闭包帧 `LambdaFrame` **恒为类**，**零**值类型分支 | `LambdaFrame.vb:214-218`；`Lowering\LambdaRewriter\` 目录 `IsValueType`/`TypeKind.Struct`/`isStruct` 零命中 | **实锤**（源码逐行 + `grep`；范围限 `LambdaRewriter` 子目录，**未**穷举整个 `Lowering`） |
| 7 | C# 的闭包环境**可以**是结构体 | `SynthesizedClosureEnvironment.cs:56`；`ClosureConversion.cs:397` | **实锤**（源码逐行） |
| 8 | 扩展方法**只能**声明在命名空间层的类型上（`Module` / 脚本类） | `SourceMemberContainerTypeSymbol.vb:3336-3348`；`NamedTypeSymbolExtensions.vb:108-111`；`Errors.vb:1327` | **实锤**（源码逐行） |
| 9 | `Handles` **只能**落在成员方法上（要求同类型 `WithEvents` 变量） | `SourceMemberMethodSymbol.vb:581-600`；`Errors.vb:403` | **实锤**（源码逐行） |
| 10 | `Static` 局部**在 lambda 里被禁止** | `Binder_Statements.vb:998-1008`；`Errors.vb:1464` | **实锤**（源码逐行） |
| 11 | C# 局部函数**可以**在定义点之前调用；VB **不能**前向引用局部变量 | C# 提案逐字（本地镜像在册）；B5 | **实锤**（文本在册 + 源码逐行） |
| 12 | C# 的「捕获进值类型帧、无 GC 压力」句为**原文逐字** | `proposals\csharp-7.0\local-functions.md`（本地镜像逐字） | **实锤**（文本在册） |
| 13 | ModVB 会议 PROPOSAL C 与其理由为**原文逐字** | `meeting-recursive-lambda-inference.md:51` | **实锤**（文本在册） |
| 14 | 主线 2017 会议**指向** local functions | `vblang\meetings\2017\vbldm-notes-2017.11.15.md:16` | **实锤**（文本在册） |
| 15 | 今天 `.vbx` **顶层**成员方法之间的调用与文本序无关 | 本轮实测（探针已删净，`exit 0`）；`spec-scripting-dialect.md:60` | **实锤**（已运行）；「**块内**过程也应如此」是**推测**，不作前提 |
| 16 | C# 局部函数的直接调用发射为 `call` 而非 `callvirt` | `proposals\csharp-8.0\static-local-functions.md` Detailed design 逐字 | **实锤**（文本在册） |
| 17 | VB 闭包降级**本身就带**拷贝构造分析，**类帧是它的载体**（`seenBackBranches` 驱动 + `symbolsCapturedWithoutCopyCtor` 例外表 + 帧类型二选一 + `For Each` 控制变量断言 + 构造器延迟注册） | `LambdaRewriter.vb:233-248`、`:260-306`；`LambdaFrame.vb:61-65` | **实锤**（源码逐行；**运行未跑**） |
| 18 | 作用域取「甲」（整块可见、允许前向调用）为**作者定案** | 作者决定（2026-09-13）；与 C# 提案逐字同向 | **实锤**（决定，非推断）；`(待会议确认)` |

**降级为「推测」的项**（正文均已标注）：§2 的「复用 `MethodBlockSyntax` 即可，不必新造节点」；§4 的「捕获可原样复用 `LambdaRewriter` 流水线」；§7 的「直接调用发射为直接方法调用」；Unresolved 10 的「桩是否成体系」；**结构体帧「无可观察行为变化」这一目标**（Drawbacks 定价条 ②，Unresolved 12）。

**未复现**：本阶段**未改编译器、未加单元测试、未跑原型**；一切「实现可行/成本量级」判断均为**推测**。`.vbx` 侧实测仅两项（B3 与 B14 的探针），其余运行期行为未跑。

## 相关文档

- `..\modvb\meetings\meeting-recursive-lambda-inference.md` — **本提案的核心来源**：PROPOSAL C（`:51`）、A/B/C/D 对照（`:47-53`）、本地函数优于递归 Lambda 的四条理由（`:60`）、RESOLUTION 3/4/5/6（`:176-179`）、三态与优先级（`:203`）、C# 原文三段转引（`:262-267`）。
- `..\modvb\proposals\proposal-recursive-lambda-inference.md` — 同一工作项的**过渡阶段**提案；其 Alternatives 逐字列过「引入独立的 `Function` / 本地函数语法，天然支持递归与互递归，而非放宽变量声明规则」。
- `..\modvb\proposals\proposal-local-declarations.md` — **相邻但不同**：局部**变量**声明增强（`Let` / 元组 / `As New`），见 Summary 边界二。
- `..\modvb\proposals\proposal-top-level-code.md` — Anthony 的顶级代码提案；其 Unresolved 第一条（容器是模块还是类）**本提案不回答**。
- `..\csharplang\proposals\csharp-7.0\local-functions.md` — **C# 侧原文，本地镜像在册**（非仅经 ModVB 会议转引）；另见 `..\csharplang\proposals\csharp-8.0\static-local-functions.md`（不捕获变体）、`..\csharplang\proposals\csharp-9.0\local-function-attributes.md`（特性 / `extern`）。
- `..\vblang\meetings\2017\vbldm-notes-2017.11.15.md:16` — 主线对局部函数的意图文字（#195 否决理由逐字；本提案在 `..\vblang\` 文本里 grep 到的最直接一处，**范围限已 grep 的目录**）。
- `proposal-vbx-top-level-locals.md`（Active #23） — **消费者**提案，其阻塞项 T2 与本提案相关但裁决点不同（Summary 边界四、Unresolved 9）。
- `spec\spec-scripting-dialect.md` — 顶层四形式映射表（表头 `:13`，四行 `:15`–`:18`）**不受本提案影响**；`:48`（扩展方法承载）、`:60`（顶层方法直接互调）、`:348`（提交类 `Handles` 的既有缺陷）。
- `decisions.md` **D5**（以 C#/csi 为设计蓝本、须说明分叉理由）与 **D6**（兼容性只对 GA 成立，且**只**解掉兼容性一条）。**按节号引用，不按行号**（D6 正文的引用纪律）。
- 工作材料（`tmp\`，不入库）：`tmp\investigations\vbx-top-level-locals\submission-wall.md`（W1 与本提案来历）；`tmp\vortex-logs\top-level-implicit-shared\pitfalls.md`（P-021 已把「VB 有没有局部函数」登记为必查项）。
