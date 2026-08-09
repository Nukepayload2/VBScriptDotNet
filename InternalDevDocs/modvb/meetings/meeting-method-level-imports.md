# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本建议是上次 TypeOf 流分析会议的续篇之一——两者共享对"作用域如何影响绑定"的追问；但本次的议题更老：它是主线 `#117`（Method-scoped `Option` and `Imports` statements）的 **Imports 半途**，主线 2017 年讨论过后把它挂起，2018 年只把 `Option` 部分重新过了一遍。今天我们把这条被主线搁下的线索捡起来审一遍。

## Agenda

* [Proposal: 方法级 `Imports`（Method-Level Imports）](#proposal-方法级-imports)

## Proposal: 方法级 `Imports`

_Related: [vblang #117 – Method-scoped `Option` and `Imports` statements](https://github.com/dotnet/vblang/issues/117)；[vblang #255 – Localised Compiler Options](https://github.com/dotnet/vblang/issues/255)；[vblang #135 – Late-binding without `Option Strict Off`](https://github.com/dotnet/vblang/issues/135)（#117 隶属的场景）；ModVB：`proposal-module-enhancements.md`、`proposal-json-literals.md`_

### 场景与缺口

We started from a claim we have heard repeated in various forms: 文件顶部的 `Imports` 作用域是整个文件，别名与命名空间一旦引入就"过度暴露"——既带来命名冲突，也制造 IntelliSense 噪音。有些引入只在某个方法里用到，文件其余部分完全不需要它们。原建议（Anthony 第 8 章）提出的解法是把 `Imports` 放进方法体：

```vb
' Namespaces, Shared type members, and XML/JSON namespaces can
' be imported at the method level.
Sub WriteHelp()
    Imports System.Console
    Imports <xmlns:xs="http://www.w3.org/2001/XMLSchema">

    Clear()
    WriteLine("...")
    WriteLine("...")

End Sub
```

Here's the important framing we kept in front of us: `Imports System.Console` 在今天**文件级就合法**——VB 的 `Imports` 语句本就可以引入类型/模块以带入其 `Shared` 成员，`WriteLine`、`Clear` 在文件级导入后即可无前缀调用。所以原建议不是发明新能力，而是把**既有能力的生效范围收窄到一个方法**。这个"范围收窄"是它全部价值的所在，也是它全部设计负担的所在。

这段示例紧接在第 8 章 Module 增强（泛型/嵌套/默认不提升成员）之后。We think 这不是排版巧合：如果模块默认不再把成员提升进命名空间，方法级 `Imports` 就是"按方法把模块成员请进作用域"的最自然通道。这条互补关系建议原文一字未提，却是我们在房间里最重要的发现。

### 候选方案

**PROPOSAL A — 方法级 `Imports`（按原文第 8 章）。** `Imports` 可出现在方法体内，引入命名空间、`Shared` 类型成员、XML/JSON 命名空间；作用域为整个方法；方法内以简名直接使用。原文示例 `WriteHelp` 即此形态。

**PROPOSAL B — 块级 `Imports`（`Option`/`End Option` 块风格）。** 不默认以方法为界，而是显式声明作用域块：

```vb
Sub WriteHelp()
    Option
        Imports System.Console
        Imports <xmlns:xs="http://www.w3.org/2001/XMLSchema">
        Clear()
        WriteLine("...")
    End Option
    Console.WriteLine("...")   ' 块外恢复原解析
End Sub
```

这与主线 2017 年笔记里 "`Option`/`End Option` block. Blocked-scoped support types and namespaces in with block" 的记录同源，是 #117 当年讨论中浮现过的形态。

**PROPOSAL C — 方法级"类型/模块导入 + 别名"（窄口径）。** 只允许方法级 `Imports SomeModule` 与 `Imports Alias = System.Console`，明确**不**支持方法级整命名空间导入，也不承担 XML/JSON 命名空间。口径最小、语义最清晰，直击 Anthony 自己的真实用例（见下）。

**PROPOSAL D — 什么都不做。** 保持现状：文件级 `Imports` + 方法内全限定名。代价是长文件里"只为两个方法服务"的导入仍然整文件生效，冲突与噪音问题长期存在。

### 权衡：Q&A

- **A 的术语有问题。** 建议的 Detailed design 写"`Imports System.Console`：引入命名空间 `System.Console`（其成员为 `Shared`）"——这是**类型**，不是命名空间；而 Summary 又正确地写着"`Shared` 类型成员"。同一份文档自相矛盾。We think 这不是笔误级别的小事：**"导入命名空间"与"导入类型并带入其 Shared 成员"是两条不同的解析路径**，别名、遮蔽、歧义规则都不同。先把话说准，才谈得上设计。`Probably`：方法级 `Imports` 应镜像文件级 `Imports` 的全部形态——命名空间、类型/模块（带入 Shared 成员）、`Alias =` 别名、XML 命名空间声明——而不是原建议里那个含混的"命名空间（其成员为 Shared）"。

- **A vs B：以方法为界还是以显式块为界？** 方法为界省一个 `End` 关键字，符合"低仪式感"；但方法的形状不稳定——方法可以被 `With` 块、`Using` 块、`For` 切成更小的区域，把导入钉在"整个方法"上等于同时管到作者不想管的地方。显式块更精确，但多一层嵌套、多一对关键字。2018 年主线评估 #117 时对块级 `Option` 的总体态度是"a convenience feature"，且"any code that needs looser Options could be refactored into another method and placed in a Partial class"——同样的反驳对 `Imports` 成立：**需要更窄作用域，本可以把这段代码拆成一个单独的方法**。我们不打算给"方法拆分"这种既有组织手段再叠一块语法。**结论：若要做，以方法为界（A 的口径）比显式块（B）更 VB；B 留档。**

- **A vs C：命名空间导入真的需要方法级吗？** 我们对方法级"整命名空间导入"保持怀疑——它把文件级导入的毛病（宽泛、噪音）原样搬进方法，只是搬小了一点。而"导入一个类型/模块"恰恰相反：类型导入天然窄（只带入一个类型的成员），且与 Module 增强的"默认不提升"互补。Anthony 自己的真实用例也指向类型导入而不是命名空间导入——第 7 章的 `ConstructorCall` 函数里：

```vb
Function ConstructorCall(
           node As ExecutableStatementSyntax,
           Out kind As VBSyntaxKind,
           Out arguments As SeparatedSyntaxList(Of ArgumentSyntax)
         ) As Boolean
    ' Bring shared pattern functions into scope.
    Imports SyntaxPatterns

    Select Case ShapeOf node
        Case ExpressionStatement(
               InvocationExpression(
                 MemberAccess(MeExpression(), IdentifierName("New")),
                 argList
               )
             )
            Return True, kind:=VBSyntaxKind.MeExpression,
                         arguments:=argList.Arguments
        ...
    End Select
End Function
```

这是整场讨论里**唯一的真实代码实证**：一个模式匹配辅助模块，按方法引入作用域。它演示的正是"类型/模块导入"，而不是命名空间导入。We think 这暗示 C 才是价值重心；A 把 XML/JSON 命名空间一起打包进来，属于"打包了次要无关能力"。

- **"Do we really want `Imports`? Seems kinda crazy."** 这是主线 2017.08.23 笔记里对 Method-scoped `Option` and `Imports` 的第一反应，we want 把这句话完整地摆上台面，而不是假装它不存在。我们的重读：这句话的"kinda crazy"指的是"在一个方法里引入一套新的名称绑定来源"，会让读者在追踪简名出处时多问一层"这个 `WriteLine` 来自哪一级"。文件级导入 + 全限定名的双轨已经能解决一切——方法级导入是第三条路。**这是原则 #3（不引入"第二种做事方式"）的正面撞击**，任何辩护都必须先过这一关。另一边，2017 年同场也记了 "Could be useful, should break it out into another proposals." 和 "Between this and a `Dynamic` type, I like this better."——主线的态度是"有可能有用，但不是头条"。我们大体认同。

- **C# 对照（`using static`）。** 2014.02.10 笔记在讨论静态类/静态导入时记过一句：VB 侧对"必须限定成员访问、除非显式全部导入"的能力标注为 "NOT POSSIBLE, BUT DESIRABLE"，而 C#/Roslyn 当时正引入 "static usings"。但**C# 的方法级 `using static` 至今不存在**——C# 的 `using` 指令（含 `using static`）只允许在命名空间/编译单元级。所以这不是"跟随 C#"，而是**超出 C#**：我们在一段 C# 自己都没有的语法空间里做设计。`Suspect`：超出 C# 本身不是否决理由（VB 在表达式/XML 等领域本就超出 C#），但"跟随 C#"这条默认策略不再提供掩护，论证负担全在我们自己身上。

- **冲突与遮蔽：从 2014.10.08 的讨论借一条规则。** 导入引入重名时的行为，主线有真实先例：2014.10.08 笔记记录了两个模块都提供 `Sin` 的情况——"VB makes this invocation of Sin an ambiguity. But when you do the equivalent in C# using 'static using', it gathers together all candidates from all usings, and then performs overload resolution on them." 当时主线的结论是保留 VB 的歧义报错，并仅在 meta-import 的特定场景改成"合并候选交给重载解析"（"This is the proposal that we picked"，小破坏，需改 spec 4.3.2）。**我们的立场：方法级导入不发明新的冲突规则，原样复用现有导入规则**——同级导入重名仍报歧义；方法级与文件级同名时，方法级（内层）遮蔽文件级（外层），与 VB 的嵌套作用域直觉一致。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Imports` 今天是**编译单元级构造**：只允许出现在文件顶部、任何 `Namespace`/`Type` 声明之前。把它放进方法体，意味着文法必须允许 `ImportsStatement` 出现在语句列表里。语法本身无歧义——`Imports` 是保留关键字，语句形态与文件级一致。真正的歧义在**位置语义**：若允许出现在方法体任意处，那么"方法体最前面出现的 `Imports`"与"写在两行代码之间的 `Imports`"各自管到哪？我们倾向镜像文件级约束：**方法级 `Imports` 必须位于方法体的语句最前**（所有可执行语句之前），这样"整个方法生效、位置与文本顺序无关"成立，读者不需要逆向追踪"这行导入从哪一行开始起作用"。

#### 2. 角案例与边界语义

- **局部变量遮蔽。** 方法内 `Dim WriteLine As String` 与导入的 `Console.WriteLine` 冲突——局部变量/参数必须遮蔽导入（VB 既有作用域规则：内层遮蔽外层）。这条必须写死，否则导入会悄悄改变既有代码的绑定（见 Breaking change）。
- **泛型方法 / 迭代器 / `Async`。** 方法级导入对 `Sub`/`Function` 一视同仁；`Iterator`/`Async` 只是降级状态机，导入集合是编译期概念，不受影响。`Probably`：无交互。
- **XML 命名空间声明。** `Imports <xmlns:xs="...">` 在方法级生效于方法内的 XML 字面量——这是对既有 XML 命名空间导入的直接收窄，语义自洽。但"JSON 命名空间"没有对应物：主线 JSON 字面量建议（#101 讨论、ModVB `proposal-json-literals.md`）里没有"JSON 命名空间导入"这一构造。**`Suspect`：原建议的"XML/JSON 命名空间"里，JSON 一半无据可依**，除非 JSON 字面量建议先定义 JSON 命名空间导入，否则本建议不应承担。
- **别名形式。** `Imports C = System.Console` 在方法级是否允许？文件级允许，方法级没有理由不允许；且别名是方法级最可能被高频使用的形态（单方法内避免全限定）。**RESOLUTION 倾向：允许，且等号形式与文件级一致。**
- **Option Strict Off / late binding。** 方法级导入的对象是类型/命名空间，与晚期绑定无交互——宽松模式下 `WriteLine("...")` 本来就走 `Object` 晚期绑定与否取决于是否存在静态解析；方法级导入只影响早期绑定解析路径。与文件级导入行为一致即可，无分叉（见下）。

#### 3. 作用域与绑定

语义模型里，方法级导入后的简名绑定到导入符号——与文件级导入的绑定目标相同，只是 `GetSemanticModel` 在"方法内"与"方法外"返回不同结果。IDE 补全必须在方法内显示导入成员、方法外不显示。绑定目标（property vs backing field 之类的问题）不在此特性内——导入只把名称引入作用域，不改变既有解析逻辑。

#### 4. 与既有特性的交互

- **Module 增强（第 8 章相邻建议）。** 若模块默认不再提升成员，方法级 `Imports` 是带回模块成员的通道——两者是**同一包**的两个半场。本建议未提及这个依赖；若独立落地而模块仍提升成员，方法级导入的一部分价值（导入模块）被文件级自动提升架空；若模块先落地不提升，则没有方法级导入，模块成员只能全限定访问。**We think 这两个建议必须在同一个设计周期里对表。**
- **`With` 块。** 2017.08.23 笔记里 "Blocked-scoped support types and namespaces in with block" 把它与 `With` 块并置——`With` 是 VB 里"块级成员作用域"的既有例子（`With obj` 内可裸访 `obj` 成员）。方法级导入与 `With` 的语义模型不同（`With` 绑定对象表达式，导入绑定名称），但"把作用域收窄到一个块"的审美是同源的。这是本特性最"VB"的部分。
- **名称解析建议（`proposal-name-resolution.md`）。** 第 16 章 Anthony 提过 "Smarter name resolution to avoid `Console` namespace hiding `System.Console` class"——命名空间 `Console` 遮蔽类型 `System.Console` 的既有痛点。方法级导入**收窄**了这种遮蔽的半径（只在方法内生效），但不能治愈它。两建议是正交的：一个管"导入的半径"，一个管"同名遮蔽的排序"。

#### 5. Breaking change 与兼容性

本建议是**纯增量**：文件级不允许方法内 `Imports`，今天所有合法代码在采纳后仍合法、绑定不变。唯一需要防护的点是"导入引入的简名遮蔽既有局部解析"——已被第 2 条的局部变量遮蔽规则封死。零破坏是本建议最大的优点，也是它与 TypeOf 流分析（那次有 `Shadows` 重解析风险）的本质区别。**结论：无 breaking change，需在 spec 里把遮蔽规则逐条列证。**

#### 6. Option Strict / 编译选项分叉

导入解析不依赖 Option Strict 的开关——严格/宽松两条路径下，方法级导入引入的名称与文件级导入行为一致；宽松模式下不改变既有晚期绑定选择。`Probably`：无分叉。但 `Option Compare` 有一点相关性：导入名称的**大小写解析**在 `Option Compare Text` 下更宽松，方法级导入沿用同一规则即可，不新开。

#### 7. IDE / IntelliSense

动机的一半是"减少噪音"，所以 IDE 体验是验收标准的一半。需要验证：方法内补全显示导入成员；方法外不显示；`Imports` 在方法内作为语句的着色/缩进；"组织 Imports"代码修复能否把文件级导入**移进**唯一使用它的方法（这是本特性最可能的 IDE 入口，也可能是它最大的真实价值——不是手写，而是重构）。这些都要在原型里验证。

#### 8. 数据 / 普遍性

诚实地说：**我们只有一条真实代码证据**——Anthony 第 7 章 `Imports SyntaxPatterns`。除此之外，"方法内重复全限定名"的占比没有量化数据；"文件级导入带来的冲突与噪音"是常见抱怨，但"方法级导入能解决其中多少"同样没有数据。`Probably`：业务代码（"hundreds of thousands of quiet customers"）中，单方法专用命名空间/类型的频度不高——多数方法的导入需求与文件级导入高度重合。本特性的普遍性弱于 Module 增强，更弱于 TypeOf 收窄。

#### 9. 更简替代

- 文件级导入 + 方法内全限定：现状，零新语法，代价是长文件噪音。
- 拆方法：2018 年主线对 #117 的既有建议——"any code that needs looser Options could be refactored into another method"——对导入同样成立。
- Analyzer / 重构：**"把文件级导入移进唯一使用处"完全可以先做成一个代码修复**，让用户手动受益，再决定是否语言化。这条我们格外看重：它把决策推迟到有数据时。

#### 10. 成本 / 优先级

编译器侧成本中等偏低：`ImportsStatement` 进语句文法、方法级独立导入集合、绑定层按作用域查导入表。Roslyn 的 `Imports` 目前挂在 compilation/type 级，方法级需要把导入集合下放到方法——不是重写，但触达绑定管线。IDE 侧成本集中在补全与"组织 Imports"。价值与成本的匹配，取决于第 8 条的数据缺口能不能补上。

#### 11. 运行时 / CLR 硬约束

无。导入是纯编译期名称解析，不生成任何 IL、不触达 CLR 存储规则、与 PEVerify 无关。

#### 12. 值不值得做

价值（收窄作用域、减少噪音）真实但**窄**；成本（编译器中）可控；风险（零破坏）最低。逐条打分：**价值 3/5、成本可控、风险最低**。缺的是数据与原型。We're not prepared to reject it——零破坏的特性我们不轻易丢；我们也尚未准备好全力推进——语义未定、价值未量化。

### VB 基因对照

- **消除常见样板（原则 #9）**：方向上一致，但"常见"存疑——样板（重复全限定名）是否存在、多常见，没有数据。这是本特性与 TypeOf 收窄（高频守卫惯用法）最大的区别：**证据等级差一档。**
- **不引入"第二种做事方式"（原则 #3）**：本特性的最大扣分项。文件级导入 + 全限定已覆盖场景，方法级导入是第三条路径。主线 2017 年 "Seems kinda crazy" 的直觉就在这里。
- **保持 VB-like（原则 #2）**：`Imports` 是纯正 VB 词汇；把作用域收窄到方法符合 VB 的块作用域传统（`With`、`Using`、`For`）。这一半是满分的。
- **永不破坏现有代码（原则 #1）**：满分——纯增量，遮蔽规则写死即零破坏。
- **避免隐蔽语义变化（原则 #7）**：无——导入是显式声明，不改变任何既有绑定。
- **冗长只在有用时是美德（原则 #10）**：方法内简名 `Clear()` 比 `System.Console.Clear()` 短而可读——正是这条原则的正向应用。
- **与主线关系（对照表 2.3）**：这是**主线议题的独立延伸**——#117 主线讨论过（2017.08.23），2018.02.07 把它缩为 Block-scoped `Option` 并"Leaving these two issues open for now"，`Imports` 半途从未单独成型。ModVB 把它独立写成完整建议，属于"Anthony 独立延伸但源自主线议题"。与 Module 增强（同章）、JSON 字面量（"JSON 命名空间"）必须对表。

### RESOLUTION:

1. **概念成立，形态待定**：方法级 `Imports` 值得继续设计，但**不是 Active**——它是 #117 的 Imports 半途（主线 2017 讨论、2018 搁置），价值真实但窄、数据缺失、语义未定。**三态：Consider。**
2. **先纠术语**：原建议"引入命名空间 `System.Console`（其成员为 `Shared`）"是类型/命名空间的概念混淆。方法级 `Imports` 应镜像文件级 `Imports` 的完整形态：命名空间、类型/模块（带入 `Shared` 成员）、`Imports Alias =` 别名、XML 命名空间声明。
3. **作用域以方法为界**（PROPOSAL A 口径），不做显式块（PROPOSAL B 留档）：省一对关键字、低仪式感、与文件级 `Imports` 的"整个作用域生效"直觉一致。位置规则：**方法级 `Imports` 必须位于方法体语句最前**，镜像文件级"`Imports` 先于一切声明"的约束。
4. **价值重心在"类型/模块导入"**（PROPOSAL C 的口径）：方法级整命名空间导入与文件级导入同样宽泛，兴趣最低；类型/模块导入窄、与 Module 增强互补、有 Anthony 真实代码实证。XML 命名空间导入随之支持；**"JSON 命名空间"不成立，划给 JSON 字面量建议，本建议不承担**。
5. **冲突规则不发明、复用既有**：方法级遮蔽文件级（内层胜）；局部变量/参数遮蔽导入；同级导入重名沿用现有歧义报错（对照 2014.10.08 记录），不引入 C# static-using 式"合并候选交重载解析"。
6. **与 Module 增强同一设计周期对表**：若模块默认不提升成员，方法级 `Imports` 是带回模块成员的通道；两个建议必须一起设计，否则互相架空。

### Implication:

- 修订建议文档：重写 Detailed design（文法、导入对象清单、位置规则、遮蔽优先级）；新增 Compatibility 章节（纯增量声明 + 遮蔽规则逐条列证）；移除/移出"JSON 命名空间"。
- 写一个最小原型：方法级 `Imports` 类型/模块 + 别名 + 冲突测试；验证语义模型（方法内/外绑定差异）与 IDE 补全。
- 补数据：真实代码库中"文件级导入仅被单一方法使用"与"方法内重复全限定名"的占比；为普遍性提供证据。
- 先做一个"组织 Imports"代码修复原型：把文件级导入移进唯一使用处。**如果重构比手写更高频，我们可能只需要重构，不需要语言特性。**
- 与 Module 增强、JSON 字面量两个建议对表，确认依赖关系。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：方法级导入与 `Option Compare` 大小写解析、泛型方法、`Iterator`/`Async` 降级状态机的逐项行为清单，待 speclet 补全。
- `OPEN QUESTIONS`：若"整命名空间导入"最终被排除，方法级 `Imports` 的命名空间形式是否仍保留（`Imports System.Xml` 只作窄范围副本）——we lean 排除，留待数据。
- `TODO`：量化"文件级导入仅被单方法使用"的真实占比，为 PROPOSAL C vs A 提供依据。
- `TODO`：实现"组织 Imports 代码修复"原型，验证"重构即特性"假设。
- `Follow-up`：与 Module 增强建议合并一页设计说明（互补依赖）；与 JSON 字面量建议确认"JSON 命名空间"归属。

### 状态

- **LDM 状态：LDM Considering**（#117 的 Imports 半途在 ModVB 沙盒内重新激活，主线状态不变）。
- **三态判定：Consider** — 概念有价值、零破坏、极 VB；但价值未量化、核心语义（位置/别名/冲突）未定、且是最可被"代码修复 + 全限定名"替代的候选。补上数据与原型、与 Module 增强对表后，可转 Active；否则维持 Consider。

---

## 附录：特性评价

# 建议评价报告：proposal-method-level-imports.md

## 评价对象

- 建议：proposal-method-level-imports.md — 方法级 `Imports`
- 来源：Anthony 原文第 8 章 "General Modernization and Evolution II (Declarations)"（`..\AnthonyDesign_wordpress.txt` L1412 章首；方法级 Imports 示例 L1463–1473，紧接 Module 增强 L1451–1461）；另有第 7 章 L1354 的真实用例 `Imports SyntaxPatterns`（"Bring shared pattern functions into scope."）
- 配方目标：把 `Imports` 的生效范围收窄到单个方法，减少文件级导入的命名冲突与 IntelliSense 噪音
- 血缘：主线 #117（Method-scoped `Option` and `Imports`，2017.08.23 讨论、2018.02.07 只重过 `Option` 部分）的 Imports 半途

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。动机（噪音/冲突）真实、示例可操作（文件级本已合法，收窄后可演示）；但位置/别名/冲突优先级三个核心语义未定，效果边界不完整；"JSON 命名空间"声称无法演示 | 已检查 | 无原型封顶；核心语法（作用域规则）未定型 → 按评价标准效果最多 3–4；实证仅一条（`Imports SyntaxPatterns`） |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Imports` 是纯正 VB 词汇、方法作用域符合 VB 块传统（`With`/`Using`）；但打包了命名空间/类型与 Shared 成员/XML/JSON 命名空间三类能力；"第二种做事方式"张力（原则 #3）未处理 | 已检查 | 方法级整命名空间导入与文件级导入同病（宽泛）；JSON 部分无基因可承；最亮处是类型/模块导入与 Module 增强的互补，建议未点明 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、4 个未决问题具体诚实（如实列出是加分）；但 Detailed design 只有一处示例，无文法/位置/别名/冲突规则；术语自相矛盾（Summary"`Shared` 类型成员" vs Detailed design"命名空间 `System.Console`（其成员为 `Shared`）"）；状态行占位链接；无 Compatibility 章节 | 已检查 | 术语混淆是硬伤（类型 vs 命名空间）；≥4 个关键未决点使效果封顶；占位链接违反强提案清单 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光/雷受益（盘活 `Imports` 资产、编辑提速）；风受损——主线 #117 已缩为 Option-only 并搁置，ModVB 复活 Imports 半途属演化节奏断裂；暗风险小（纯增量），但与 Module 增强的耦合未权衡 | 已检查（预测待定） | 风维度与主线节奏不一致未自行识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源"第 8 章"标注正确；但未点明血缘是主线 #117（2017.08.23 讨论过的同一议题）；未对照 C# "static using"（2014.02.10 已有对照记录）与主线"合并候选"讨论（2014.10.08）；"JSON 命名空间"无主线依据 | 已检查 | 血缘与对照材料缺失；JSON 成分疑似混入无关能力；无杂质（未借闭源） |

## 设计原则对照

- **与 VB 基因：主体一致**（`Imports` 纯正词汇、方法作用域符合 VB 块传统、零破坏=原则 #1、消除样板=原则 #9、冗长只在有用时是美德=原则 #10）；**偏离于原则 #3**（不引入"第二种做事方式"——文件级导入 + 全限定已覆盖场景）与轻微偏离原则 #5 的可读性要求（简名更可读，但"简名来自哪一级"的认知负担上升）。
- **与主线关系：Anthony 独立延伸，但源自主线议题**——#117 的 Imports 半途（2017.08.23 讨论、"should break it out into another proposals"，2018.02.07 仅重过 `Option` 部分并搁置）。与 Module 增强（同章、互补依赖）；与 JSON 字面量（"JSON 命名空间"归属）；与名称解析建议（遮蔽排序，正交）。
- **破坏性变更：无**——纯增量新语法；需在 spec 里写死"局部变量/参数遮蔽导入、方法级遮蔽文件级"以防未来语义漂移。

## 总评

- **达成程度：部分达成**——概念（收窄导入作用域）成立且零破坏；术语、作用域规则、价值数据均未到位。
- **LDM 三态建议：Consider**——不 Reject（零破坏、极 VB、与 Module 增强互补）；不 Active（价值未量化、语义未定、主线已搁置、是最可被代码修复替代的候选）。
- **主要问题**：① 术语混淆（类型导入 vs 命名空间导入）必须先厘清，这是设计的地基；② 位置/别名/冲突优先级三个核心语义未定；③ 与 Module 增强的互补依赖未分析（第 8 章两者相邻，若模块默认不提升，方法级 Imports 是带回模块成员的通道）；④ "JSON 命名空间"无据可依；⑤ 无 Compatibility/交互章节、无原型、实证仅一条。

## 返工建议

- **补充章节**：文法（`ImportsStatement` 在方法语句列表中的位置与约束）；导入对象清单（命名空间/类型与模块 Shared 成员/别名/XML 命名空间）及各自文法与解析路径；遮蔽与冲突优先级（方法级 > 文件级、局部变量/参数 > 导入、同级歧义沿用现有规则）；与 Module 增强、`With` 块、`Option Compare` 的交互；Compatibility（纯增量声明 + 遮蔽规则逐条列证）；把"JSON 命名空间"移出至 JSON 字面量建议。
- **补充证据**：最小原型（方法级 `Imports` 类型/模块 + 别名 + 冲突测试；语义模型方法内/外绑定差异；IDE 补全）；"组织 Imports"代码修复原型；真实代码库中"文件级导入仅被单方法使用"与"方法内重复全限定名"的占比数据。
- **未决问题处理**：位置 = 方法体语句最前（镜像文件级）；别名 = 允许（`Imports Alias =` 等号形式）；冲突 = 方法级遮蔽文件级、局部变量遮蔽导入、同级歧义沿用现有规则；XML 命名空间 = 方法内 XML 字面量生效；JSON = 划给 JSON 字面量建议。
- **设计探索**：与 Module 增强合并一页互补设计（默认不提升 + 方法级导入）；对照 C# 缺少方法级 `using static` 的"超出 C#"论证；评估"仅重构/代码修复、不语言化"作为最小可行路径。

---

## 附录：C# 生态与互操作考量

_范围声明：本提案（方法级 `Imports`）是**纯编译期名称解析**——不生成 IL、不触达 CLR 存储规则、与 PEVerify 无关（见正文「运行时 / CLR 硬约束」）。因此它与 C#/CLR/.NET 的互操作面**不在运行时/元数据层**，而在**语言设计的「作用域语义」层**。本附录据此聚焦 C# 的导入/作用域方向与本提案的对应关系，不硬凑元数据互操作话题。索引依据：`..\..\csharplang-index.md`（下称「索引」），版本对照见其头部（C# 6 起）。_

### 相关 C# 现实方向

1. **C# 的导入指令只在编译单元/命名空间体级，从未下放到方法。** C# 的 `using` 指令（`using Namespace;`、`using Alias = Type;`、`using static Type;`）从 C# 1 至今都只出现在编译单元级或命名空间体级。`GlobalUsingDirective.md` 的编译单元文法把 `using_directive*` 与 `namespace_member_declaration*` 并置，且对 `extern_alias_directive` 作用域的既有条文写明其覆盖「*using_directive*s … of its immediately containing compilation unit or namespace body」——导入指令的合法位置就是这两个层级。C# 6 引入 `using static`（`Language-Version-History.md` C# 6 节条目「Import of static type members into namespace」，系特性清单条目，非提案正文可引）。**方向是往「更大作用域」走**：C# 10 `global using` 把导入上探到整个程序——原文逐字：「The effect of adding a *global_using_directive* to a program can be thought of as the effect of adding a similar *using_directive* that resolves to the same target namespace or type to every compilation unit of the program.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`。即便 `global using` 本身也仍被钉在编译单元级：原文逐字：「The *global_using_directive*s are allowed only on the Compilation Unit level (cannot be used inside a *namespace_declaration*).」→ 同文件。

2. **术语陷阱必须挑明：C# 8 的 "using declaration" 是资源管理，不是名称导入。** C# 8 `using var f = ...`（using declaration）的作用域是「声明所在块」，但它管的是 `IDisposable` 生命周期，与「把名字带进作用域」无关。原文逐字：「The `using` declaration removes much of the ceremony here and gets C# on par with other languages that include resource management blocks.」→ `proposals\csharp-8.0\using.md`（Motivation）。因此「C# 8 using declarations 是作用域内 using 别名/静态导入」的联想**不成立**——它是资源释放。真正可比的 C# 构件是 `using` 指令（导入名称）与 `using` 语句/声明（资源释放），两者在 C# 里是**同名不同物**的两条语法线；本提案属于前者，与后者无任何共享语法。

3. **C# 对「更细粒度可见性」的答案是文件级，不是方法级。** C# 11 file-local types 把类型可见性收窄到单个文件。原文逐字：「Permit a `file` modifier on top-level type declarations. The type only exists in the file where it is declared.」→ `proposals\csharp-11.0\file-local-types.md`（Summary）。其动机直接来自 source generators（原文逐字：「Our primary motivation is from source generators.」→ 同文件，Motivation）——C# 解决「名称碰撞面」的最小粒度是**文件**，不是方法。另注意 file-local 类型与 `global using static` 互斥（原文逐字：「It is a compile-time error to use a file-local type in a `global using static` directive」→ 同文件，`global using static` 节）——C# 的两种「收窄」与「放广」机制互相拒绝。

4. **C# 14 extensions 继续在指令级进化 `using static`。** extensions 提案把 `using static` 扩到 extension block：原文逐字：「A **using_static_directive** makes members of extension blocks in the type declaration available for extension access.」→ `proposals\csharp-14.0\extensions.md`。方向仍是「指令级导入、越导越多」，不是「下放到方法」。

**综合**：C# 现实方向的导入作用域只有两档——编译单元/命名空间体（`using` 指令）与整个程序（`global using`）；可见性粒度下探停在「文件」（file-local types）；唯一「块级 using」是资源释放（using declaration，C# 8）。**没有任何 C# 先例把名称导入作用域收窄到方法。** 这与索引 T1/T8 的总体判断一致：C# 的 scope 演化在「更大」与「文件」两个方向用力，方法级作用域不在其问题空间内。

### 现实 vs 提案

| 维度 | 判定 | 理由 |
|---|---|---|
| 方法级作用域 | **脱节（超出 C#）** | C# 导入指令从未下放到方法；粒度下探止于文件级。本提案是在 C# 自己没有的语法空间里做设计。正文 C# 对照的「`Suspect`：超出 C#」经逐字核实**成立且更强**——不是「C# 恰好没有」，而是 C# 的作用域方向在反方向（更大/文件级）。 |
| 术语可比性 | **需桥接（概念澄清）** | 任务提示把本提案与「C# 8 using declarations（作用域内 using 别名/静态导入）」类比——**误读**。C# 8 using declaration 是资源释放（`using var`），不是导入。可比的 C# 构件是 `using` 指令（编译单元/命名空间级）与 `global using`（程序级）。本附录已逐字核实并纠正；正文重述时应改用「using 指令」表述，避免读者误把 `using var` 当导入先例。 |
| 作用域方向 | **冲突（方向相反，非同一 feature 竞争）** | C# 10 `global using` 把导入作用域扩大到整个程序；本提案把导入收窄到方法。方向相反，但两者不抢同一场景——C# 的答案仍是「文件级导入 + 全限定名」，本提案是第三条路，正是正文「原则 #3」张力的来源。**无兼容压力，也无借鉴价值**（C# 无法告诉我们方法级怎么做）。 |
| 编译期纯解析 | **兼容** | 与 C# `using` 指令同性质：纯编译期名称解析，零 IL、零 CLR 元数据、零反射。对 AOT/trimming 无感，落在索引 T7（dynamic 边缘化、靠静态推理）的同侧——本提案的「收窄作用域」方向天然增强可静态推理性。 |
| 歧义/冲突语义 | **需桥接（跨语言行为差异）** | 正文 RESOLUTION #5 拒绝 C# static-using 式「合并候选交重载解析」（源自 2014.10.08 主线记录），保留 VB 的歧义报错。跨语言后果：同一段代码在 VB（方法级导入）与 C#（编译单元级 static using）下，同名调用的处理一个报歧义、一个合并解析。注意 C# 内部也不是统一行为——`global using` 对多来源同名 simple_name 的处理是报歧义（原文逐字：「…references to that name as a *simple_name* are considered ambiguous.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`）。即「合并候选」是 C# 编译单元级 `using static` 特有的行为，VB 选歧义报错并非特立独行。对从 C# 迁移的读者这是认知断层，文档需点明。 |
| 名称遮蔽 | **兼容** | 内层遮蔽外层（方法级 > 文件级、局部变量/参数 > 导入）与 C# 嵌套作用域遮蔽直觉一致。原文逐字：「Just like regular members, names introduced by *global_using_alias_directive*s are hidden by similarly named members in nested scopes.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`。 |

### 对 VBScript.NET 的适应建议

- **零运行时改造，改动集中在 Roslyn VB 绑定管线**：方法级导入不需要 .vbx 识别任何新 CLR 元数据；真正的工作是把导入集合从 compilation/type 级下放到方法级（正文「成本 / 优先级」已列）。这是修改版 VB 编译器**独有**的钩子，C# 侧无对应物——共享的「组织 Imports」/导入查找基础设施需 VB 侧分叉，无法复用 C# 的编译单元级实现。
- **source-gen 桥**：C# file-local types 的动机正是 source generator 的名称碰撞（「Our primary motivation is from source generators.」）。方法级导入是 VB 侧收窄「生成代码命名碰撞面」的**对称手段**——生成的脚本方法可把辅助模块 `Imports` 进方法内，不外泄到文件级。两者互补而非竞争：C# 在文件级、VB 在方法级解决同一类噪音。
- **默认安全 / 按需动态**：本提案走早期绑定路径，与 `Option Strict` 宽松模式的晚期绑定无交互（正文第 6 条），天然落在「默认安全」一侧。若 VBScript.NET 按决策文件 M2/M8 走「默认安全、按需动态」双模路线，方法级导入属于静态面，与 NativeAOT 兼容——是少数与 C# 现实**零摩擦**的 VB 特色提案。
- **消费 C# 14 扩展成员时需补设计点**：C# 14 让 `using static` 携带 extension block 成员。若 .vbx 未来消费 C# 14 扩展成员，方法级 `Imports` 的类型/模块导入应能把扩展成员同样带进方法作用域——但不在本建议当前口径内。**OPEN QUESTIONS**。
- **IDE/重构是最大落地面**：正文第 7 条的「把文件级导入移进唯一使用的方法」代码修复是纯 VB 侧重构；C# 的 Organize Usings / Remove Unnecessary Usings 没有「移进方法」这个目标位置，需要 .vbx 的 VB 专用 analyzer/code fix。

### 对既有 RESOLUTION/三态判定的影响

- **三态判定不变：Consider。** C# 现实既不提供升 Active 的推力（C# 无对应先例、无生态拉力、scope 方向相反），也不构成 Reject 的理由（零破坏、纯编译期、非同一 feature 竞争）。
- **RESOLUTION #5 获外部佐证**：C# 并非一致采用「合并候选」——`global using` 规范对多来源同名 simple_name 明文报歧义。VB 保留歧义报错与 C# 的 global using 路径同构，只有编译单元级 `using static` 走合并。正文引 2014.10.08 主线的「合并候选」记录在本库无正文可核实，标 **OPEN QUESTIONS**（见下）。
- **对正文 C# 对照的修正（不改写正文，仅记录）**：正文「C# 8 using declarations 作用域内别名/静态导入」的联想不准确——C# 8 using declaration 是资源释放。这**不影响**正文核心结论（「C# 的方法级 `using static` 至今不存在」仍成立，且比正文说的更彻底：C# 连方法级导入的念头都没有），但重述时应改为「using 指令（编译单元/命名空间级）」。
- **RESOLUTION #3（以方法为界）无 C# 先例可借，也无需改动**：C# 8 using declaration 的块作用域只管资源生命周期，`global using` 管整个程序，都不提供方法级名称导入的参照。方法边界仍是纯 VB 选择，论证负担在「为什么是方法不是块/文件」，C# 无法回答，只能靠正文的「低仪式感 + 与文件级直觉一致」。

### 引用清单（逐字核实，本附录实际引用的 C# 原文）

- 「The effect of adding a *global_using_directive* to a program can be thought of as the effect of adding a similar *using_directive* that resolves to the same target namespace or type to every compilation unit of the program.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`
- 「The *global_using_directive*s are allowed only on the Compilation Unit level (cannot be used inside a *namespace_declaration*).」→ `proposals\csharp-10.0\GlobalUsingDirective.md`
- 「The `using` declaration removes much of the ceremony here and gets C# on par with other languages that include resource management blocks.」→ `proposals\csharp-8.0\using.md`（Motivation）
- 「Permit a `file` modifier on top-level type declarations. The type only exists in the file where it is declared.」→ `proposals\csharp-11.0\file-local-types.md`（Summary）
- 「Our primary motivation is from source generators.」→ `proposals\csharp-11.0\file-local-types.md`（Motivation）
- 「It is a compile-time error to use a file-local type in a `global using static` directive」→ `proposals\csharp-11.0\file-local-types.md`（`global using static` 节）
- 「A **using_static_directive** makes members of extension blocks in the type declaration available for extension access.」→ `proposals\csharp-14.0\extensions.md`
- 「Just like regular members, names introduced by *global_using_alias_directive*s are hidden by similarly named members in nested scopes.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`
- 「…references to that name as a *simple_name* are considered ambiguous.」→ `proposals\csharp-10.0\GlobalUsingDirective.md`（Global Using namespace directives 节）
- 「Import of static type members into namespace」→ `Language-Version-History.md`（C# 6 节特性条目，链接标题，非提案正文）

### OPEN QUESTIONS

- C# 编译单元级 `using static` 的「合并候选交给重载解析」行为：正文引 2014.10.08 主线记录转述，本库（csharplang）无对应提案正文可逐字核实，仅作会议记录引用。
- 方法级 `Imports` 是否/如何承载 C# 14 扩展成员（extension block 经 `using static` 进入作用域）的互操作，若 .vbx 决定消费扩展成员。
- C# `using` 指令的**基线**作用域条文（「仅编译单元/命名空间体」）正文在 dotnet/csharpstandard（`spec` 目录仅为链接索引，不在本镜像内）；本附录据 `GlobalUsingDirective.md` 文法与 extern_alias 条文间接核实，若需逐字条文请查 csharpstandard §14 命名空间章节。
