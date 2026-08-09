# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的建议来自 **inactive** 子目录——`proposal-rust-ownership.md`（任意块作用域 & Rust 式所有权）。它把 Anthony 原文第 18 章末尾三节（18.28、18.29、18.30）合并成一份：任意块作用域一句、"Rust 式所有权"四词、AI 一声叹息。这三节加起来不到五行话，是整份清单里最接近"备忘录"的建议。按 vblang 生命周期，inactive 意味着"有合理前景但目前不排期，且 perfectly fine for work to happen ... for them to be resurrected later"；所以本场问题与重复声明那场相同：**值得激活、保持搁置，还是根本没有足够材料？** 但我们的答案与那一场不同——这一份有三个各自独立的生命体，不能一刀切。

我们开场先把三节原文逐字念了一遍，因为它们几乎就是本场会议的全部输入：

> 18.28 "Just remembered that. People keep asking for this."
> 18.29 "Could we? Of course. Should we? Who knows?"（候选关键字：`Take`, `Borrow`, `Give`, `Lend`）
> 18.30 "Ugh. (sigh) yeah, I know…"

We agreed on one meta-point immediately：这不是一份提案，是**三张便签**。它们的血缘、成本与风险画像完全不同，必须拆开评，不能同生共死。这也是我们处理本场全部争论的第一把剪刀。

## Agenda

* [Proposal: 任意块作用域与 Rust 式所有权（Arbitrary Block Scopes & Rust-style Ownership）](#proposal-任意块作用域与-rust-式所有权)

## Proposal: 任意块作用域与 Rust 式所有权

_Related: [vblang #117 – Block-scoped Option Statements](https://github.com/dotnet/vblang/issues/117)；[vblang #255 – Localised Compiler Options](https://github.com/dotnet/vblang/issues/255)；2018.02.07 LDM（#117、#211 "Fantastic idea, and too hard to do"）；2018.12.19 LDM（模式匹配变量作用域："The scope of introduced variables is the surrounding scope"）；2014.02.17 LDM（声明表达式 Rejected、`Out` 参数作用域、隐式接口 `IDisposable`）；2016.05.06 元组设计笔记（解构必须括号）；ModVB：`proposal-using-synclock-enhancements.md`（3.10 `Using`）、`proposal-return-byref.md`、`proposal-local-declarations.md`（3.1 `Let`）、`proposal-duplicate-declarations.md`（18.17）_

### 场景与缺口

We started by asking, for each of the three fragments: 缺的到底是什么。

**18.28 任意块作用域。** VB 的局部变量作用域规则与 C# 相反：块（`If`、`For`、`While`…）**不构成作用域边界**，块内 `Dim` 的变量在块结束后依然可见、直到过程末尾。这是文档记载、大量程序员踩过的 VB/C# 差异：

```vb
Sub Demo()
    If True Then
        Dim helper As Integer = 42
    End If
    Console.WriteLine(helper)   ' 42 —— 合法。VB 中 If 块不封闭作用域。
End Sub
```

"People keep asking for this" 的痛点就在这里：想要"临时变量用完即失效"的卫生感，今天做不到。但——这是第一刀——**这个差异本身是 VB 的特征（feature），不是缺陷（bug）**。它让"把 `Dim` 往下挪一挪"不需要重排结构，也让初学者少一个"变量在块外不存在"的编译错误。我们拿不到任何"块作用域缺失造成真实事故"的数据（见深度追问 8）。`Suspect`：请求者多为 C# 移植过来的开发者，把 C# 的块作用域当作默认心智模型，而非 VB 用户自发的痛点。

**18.29 Rust 式所有权。** 我们认真问：在 .NET 里"所有权"到底买到了什么。Rust 的所有权解决的是**没有 GC 时的内存安全**；.NET 有 GC，内存安全由运行时承担。于是所有权在 VB 里只能退化为**确定性释放**——而这件事 VB 已经有 `Using`（3.10，本家族已拆解过）。Anthony 自己给了判词："Should we? Who knows?"——作者本人都没有答案，我们作为设计者必须有。更硬的一个事实：`Take` **已经是 VB 的 LINQ 查询关键字**（`From ... Take 10`），Anthony 自己的原文第 2.7 节查询示例就在用 `Take 10`。拿 `Take` 当语句关键字，等于给查询语言和语句语言各安一个 `Take`。

**18.30 AI。** 原文只有一句叹息。我们讨论了很久"这句到底指什么"——`Probably` 指 AI 辅助编程/语言与 LLM 的协作，但**没有任何设计内容可评价**。我们不会对一句叹息做三态判定；它甚至不够格进 Table，只能进 Suspect + OPEN QUESTION。

### 候选方案

**任意块作用域（18.28）：**

**PROPOSAL A — 真块作用域语法。** 引入类似 `Scope ... End Scope` 的块，块内 `Dim` 严格封闭于块。新关键字、新作用域模型、语义模型里 `Dim` 符号的 scope 规则要重写。

**PROPOSAL B — 诊断方案（不引入语法）。** 保留 VB 的作用域语义，但加一条**信息性诊断**："此变量在 `End If` 后被使用"（`IDE`/`Probably` 可选警告）。把"卫生感"从语言强制降到编辑器建议，零破坏、零新语法。

**PROPOSAL C — 什么都不做。** 沿用例行替代：临时变量放进独立 `Function`、或命名约定（`tmp` 前缀）。主线的态度接近这个——2018.02.07 对 #117 说得很明白：`This is a convenience feature.` 且"代码需要更宽松 Options 时 `could be refactored into another method and placed in a Partial class`"。

**Rust 式所有权（18.29）：**

**PROPOSAL X — 完整 Rust 所有权模型。** `Take`/`Borrow`/`Give`/`Lend` 四个关键字 + 编译器借用检查（use-after-take、alias 检测）。这是把 borrow checker 移植到 Roslyn——C# 的可空引用类型（flow analysis 的一小角）都被主线推迟了，所有权是它的**上确界**。

**PROPOSAL Y — 窄版：`IDisposable` 单次消费流分析（诊断，不是语法）。** 编译器/analyzer 跟踪一个 `IDisposable` 是否被 `Using` 消费，发现 double-dispose、use-after-dispose 就报警。不引入任何关键字，是"所有权"里唯一有数据支撑的骨头（CA2000/CA2213 已在 analyzer 层做过）。

**PROPOSAL Z — 什么都不做。** `Using` + analyzer 就是 VB 的"确定性释放"。GC 管内存，`Using` 管资源，析构函数兜底。主线对这类"第二个机制"的门槛极高（原则 #3）。

**AI（18.30）：无方案可言。** 没有设计就没有候选。我们把它整体留给 OPEN QUESTION。

### 权衡：Q&A

- **块作用域：A 还是 B？** 我们先把 A 的最强辩护说了一遍——"变量生命周期短，防止误用"是真实愿望，`Scope` 块读起来也像英语（`Scope ... End Scope`）。但 A 撞上三堵墙：(i) VB 的作用域语义是全语言的既有行为，真块作用域会制造**两种作用域心智**并存（原则 #3）；(ii) 需要一个新关键字（`Scope`），且要重写语义模型里 local symbol 的 scope 规则；(iii) 主线的模式匹配作用域决策给出反例——2018.12.19 LDM 对 `If` 模式变量明确裁定 `The scope of introduced variables is the surrounding scope`，即主线在给语言引入新变量时**主动选择**了"周围作用域"而非"块作用域"。这不是疏忽，是设计取舍。B 用一个零破坏的诊断拿到 A 的大部分价值（把"块外使用"从"合法但可能粗心"变成"有提示"），成本是一行 analyzer。**We lean strongly to B over A。**

- **B 的诊断会不会误报？** 会。VB 里"块内声明、块外使用"完全合法且有正当用法（比如在 `If` 里初始化、块外使用，这是我们的示例 `Demo` 本身）。所以 B 的默认级别必须是**信息性/关闭**（`IDE0005` 式），不能是警告，否则 `Demo` 会被标红。`Probably`：只对"同一个过程里块外没有更早赋值"的特定形态给提示，宁可漏报不可误报。

- **所有权：X 还是 Y？** 我们把 X 逐条审了一遍，没有一条活下来：
  - **`.Take` 关键字冲突**：`Take` 已是查询关键字（事实，见 Anthony 2.7 节 `Take 10`）。`Borrow`/`Give`/`Lend` 是三个全新保留字。为表所有权一次性占用四个字，VB 从没为一个特性花过这么多词汇。
  - **借用检查 vs GC**：借用检查器的前提是"别名即错误"。但 .NET 里引用类型天生可别名、反射/`Unsafe`/COM 互操作可绕过一切静态保证。检查器只能检查"它检查得到的代码"，而绕过的洞是常态——这样的检查器要么误报（别名其实无害），要么被 `Marshal.ReleaseComObject` 等真实需求打穿。
  - **与 `Using` 的关系**：`Using` 已是确定性释放的 VB 惯用法（3.10 家族会议已把它拆解）。所有权会是**第二套资源管理机制**，直接撞原则 #3。
  - **主线信号**：2018.02.07 对可空引用类型说 `We'll postpone this until we understand the uptake in C#`，且 `it may feel like a "not VB" thing as we understand its usage`。可空分析只是"这个引用可能为 null"，都被判"not VB"而推迟；所有权是"这个引用不许别名、用完必须失效"，在"not VB"光谱上走得更远。
  - **Anthony 自己的回答**：`Should we? Who knows?`——作者把球踢回给设计者，而设计者没有拿到任何价值论证。
- **那 Y 呢？** Y 唯一可辩护的骨头是 **double-dispose / use-after-dispose** 检测，但 analyzer（CA2000/CA2213）已经在做。`Probably`：Y 作为语言特性是过度工程，作为 analyzer 增强是合理的小步。我们把它记进 Follow-up，不激活。
- **AI 要不要给一句判词？** 我们一度想用 2018.02.07 对 #211 的判词 `Fantastic idea, and too hard to do` 作结，但立刻意识到这不对——#211 有明确目标（"usage of any installed programming language"），18.30 **连目标都没有**。`Suspect`：对没有内容的片段，任何判词都是编造。**OPEN QUESTION** 处理，见下方。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

块作用域 A 的 `Scope ... End Scope`：`Scope` 不是保留字，引入需登记。更深的歧义在**嵌套与 `Exit`**：`Exit Scope` 合法吗？`Scope` 与 `Using`/`SyncLock`/`With` 嵌套时，`End Using` 与 `End Scope` 的配对如何消歧？所有权 X 的歧义在 `Take` 的上下文判定：`Take resource`（语句）与 `Take 10`（查询子句）共用一个词形，parser 需要语句/查询两个语境各给一个含义——这是"隐蔽语义变化"的教科书形态，正是 `Return?` 被拒的同类理由。

#### 2. 角案例与边界语义

块作用域 A 的角案例几乎全是作用域模型的：`Scope` 内 `Dim x` 与外层同名 `x`（允许遮蔽还是报错？VB 今天**禁止**同名局部变量在嵌套块里遮蔽——这会直接改变既有规则）；`Scope` 内 `Try`/`Catch` 的 definite assignment；`Exit Scope` 与 `Continue` 语义；lambda 捕获 `Scope` 变量。所有权 X 的角案例是借用检查器的标准全家桶：`Borrow` 期间抛异常、`Take` 后走 `Using`、`ByRef` 传参即别名、`Return ByRef`（ModVB 建议）制造别名、闭包捕获。没有一条有答案，因为原文没有设计。

#### 3. 作用域与绑定

语义模型层面，块作用域 A 要回答：`Scope` 内的 `Dim x` 的 `GetTypeInfo` 返回的符号，其 `scope` 是"声明所在块"还是"过程"？今天 VB 是后者（或更精确地说，块不建立新的声明空间，同名遮蔽被禁止）。真块作用域要**引入新的声明空间**，这会改 Roslyn 语义模型的 symbol scope 规则——不是语法糖，是编译器核心改动。所有权 X 要回答："被 `Take` 的变量"是否变成一个不同的符号（Rust 的 move 会让旧绑定死亡）？在 .NET 里这没有运行时对应物，纯静态。`Probably`：X 的符号模型将是一场地震。

#### 4. 与既有特性的交互

- **`Using`/`SyncLock`/`With`**：这三个是 VB 现有的"带语义的块"。`Scope` 若无语义，只是把作用域变严，与 `Using`（作用域 + 释放）形成部分重叠——`Using` 已经是最常见的"块内变量用完即走"的载体。
- **`Return ByRef`**：所有权模型与 ByRef 别名天然冲突；ModVB 的 `return-byref` 建议（已开会拆解）若落地，别名就是一等公民，borrow checker 与之水火不容。
- **Late binding / Option Strict Off**：`Object` 上无法跟踪所有权——变体值可以是任何东西。所有权 X 只能在 Strict 模式下工作（见追问 6）。
- **`Try` 增强 / `Await`**：Anthony 的 3.9 `Try` 建议主张 `Local declarations scoped to entire Try/Catch/Finally block`——这与"任意块作用域"是同一片实现面，但方向相反：`Try` 是**放宽**（跨 Catch/Finally 共享），`Scope` 是**收紧**（封闭）。两个建议在同一编译器区域打架，必须对表。
- **`Let` 作用域**：Anthony 的 guarded-let 想让 `Let` 变量 `End Let` 后失去作用域——那也是"封闭作用域"的诉求，与 18.28 同源。`Suspect`：Anthony 在多处反复表达"想要更严的作用域"，但从未给出统一机制；18.28 可能是这个家族的地基，也可能只是并列的备忘。

#### 5. Breaking change 与兼容性

两份设计都是"新语法"，现编译代码零变化——这是唯一让人安心的地方。但注意：块作用域 A 若允许 `Scope` 内 `Dim x` 与外层 `x` 同名，会**放宽**今天"already declared"的错误（合法化更多代码）；若不允许，则 A 的卫生价值大打折扣。所有权 X 若把"use after take"从合法变成编译错误，则是把**合法代码变非法**——严格说不是"重编译变化"（因为用了新关键字），但会训练出一种"编译通过 = 所有权正确"的错误预期。`Take` 作为上下文关键字对既有查询代码零影响（查询里的 `Take 10` 不受扰动），这是唯一干净的点。

#### 6. Option Strict / 编译选项分叉

所有权 X 在 `Option Strict Off` 下无意义——`Object` 上无法做借用检查。这制造一个两路径分叉：Strict 下 `Take` 有语义，宽松下 `Take` 是装饰还是报错？我们 `Probably` 认为 X 只能活在 Strict 里，而 VBScript.NET 的产品气质恰恰大量依赖宽松/晚期绑定——这让 X 与产品定位正面冲突。块作用域 A 与 Option 正交（作用域与类型严格性无关），但 `Scope` 内晚期绑定变量同样无法被"卫生检查"追踪。

#### 7. IDE / IntelliSense 影响

块作用域 A：区域折叠、变量作用域高亮（引用变灰）、`End Scope` 的配对显示——都是新 UI 面。所有权 X：借用状态徽标（"已 Take"）、use-after-take 的 squiggle、修复建议——borrow checker 的 IDE 面是它实现成本的大头（Rust 的 rust-analyzer 是独立项目规模）。两者都是"不做进 IDE 等于没设计"的规格，而建议一行都没提。

#### 8. 数据 / 普遍性

三块都没有数据。块作用域："People keep asking"——没有次数、没有场景抽样、没有"泄漏作用域造成事故"的案例。主线 #117 的评估是混合的：`Option Strict: Would provide value, possible to do`；`Option Infer: This doesn't really make sense to us`。所有权：没有"double-dispose 是生产事故主因"的证据；`.NET` 的 `Dispose` 模式 + analyzer 已覆盖多数。AI：无内容。**We cannot prioritize on sentiment alone.**

#### 9. 更简替代

- 块作用域 A ⇒ **B 的诊断方案**（零语法）；或拆 `Function`；或命名约定。
- 所有权 X ⇒ **`Using` + analyzer**（CA2000/CA2213 增强）；`Marshal.ReleaseComObject` 的脚本场景用 `Using` 包 COM 对象即可。
- AI ⇒ 无替代可谈。

每条更简替代都指向同一个结论：**主诉病症有一瓶更便宜的药**。这正是评价标准里"更简替代是否更优"的问法——本场答案是更优。

#### 10. 成本 / 优先级

块作用域 A：语法 + 语义模型 + IDE，中低成本，但价值未证、与主线作用域决策反向。所有权 X：borrow checker 是 Roslyn 历史上最大单特性量级之一（可空引用类型主线推迟在先）；价值在 GC 语言里被大幅稀释。优先级：**两者都低于本家族任何一张已激活的牌**。在 VBScript.NET 的产品叙事里，宽松、晚期绑定、脚本卫生（`Using`、`Try`）是主线；`Take`/`Borrow` 是逆叙事。

#### 11. 运行时 / CLR 硬约束

块作用域 A：纯编译期概念，无 CLR 影响。所有权 X：CLR 没有 move 语义，引用类型赋值即复制别名，`unsafe`/反射/COM 可绕过一切静态保证——X 无法在运行时强制，只能静态检查"编译器能看到的那部分"。这与可空分析同性质，但 X 的前提（禁止别名）在 .NET 里**结构上无法成立**。这是 X 的最终判决：不是太贵，而是在该运行时上概念错位。

#### 12. 值不值得做

逐维打分。块作用域 A：价值（消除卫生痛）2/5——真实但无数据、且 B 能拿大头；成本（语言+语义模型+IDE）3/5；风险（两种作用域心智、与主线决策反向）3/5。**不值得以 A 形态做**。所有权 X：价值 1/5（GC 已管内存、`Using` 已管资源、analyzer 已管诊断）；成本 5/5（borrow checker）；风险 5/5（概念错位 + 关键字冲突 + 与宽松模式冲突）。**X 是被高成本和高风险双重否决的典型**。Y 是唯一带正号的部分，但它属于 analyzer，不属于语言。

### VB 基因对照

- **永不破坏现有代码（原则 #1）**：三块都是新语法/新诊断，通过；但 X 把合法代码变非法（use-after-take 从合法到报错）踩到线边。
- **保持 VB-like（原则 #2）**：X 是"not VB"光谱的最远端；`Take`/`Borrow`/`Give`/`Lend` 是机械式外来词，不读起来像英语。
- **不引入"第二种做事方式"（原则 #3）**：X = 第二套资源管理机制；A = 第二套作用域心智。双双撞线。
- **默认跟随 C#（原则 #4）**：C# 没有 Rust 所有权（它用 `using` + `ref struct` + `Span` 表达确定性，用 analyzer 表达纪律）。跟随 C# 即不引入 X。块作用域 C# 有（`{}`），但 VB 从没打算抄 C# 的 `{}` 块。
- **读起来像英语、对新手友好（原则 #5）**：`Scope`/`Using` 尚可；`Take`/`Borrow`/`Lend` 是术语不是英语。B（诊断）最友好——零新词。
- **不为边缘场景加特性（原则 #6）**：三块都无数据支撑普遍性，X 尤其边缘。
- **避免隐蔽的控制流/语义变化（原则 #7）**：`Take` 双语境（语句/查询）是隐蔽变化候选；A 的"同名遮蔽合法化"改变既有规则。X 的 use-after-take 报错是最隐蔽的语义收紧。
- **消除常见样板（原则 #9）**：都不消样板；X 反而**增加**样板（每个资源都要标注所有权动词）。
- **与主线关系（对照表 2.3）**：三块在主线对照表**均无对应行**，属 Anthony 独立延伸。最近的触点是 #117（Block-scoped Option Statements，2018.02.07 留 open）——但那是 Options 的作用域，不是变量的作用域，本建议未引用；以及 2018.12.19 模式匹配作用域决策——主线选择 surrounding scope，与本建议的收紧方向**反向**。这与 2014.02.17 对声明表达式的终局一致：`None of this feels naturally "VB"ish. We're happy if VB sticks merely to Out parameters and implicit declaration of Out arguments.`——VB 对"表达式/块级作用域精细化"的历史态度是**收缩**，不是扩张。

### RESOLUTION:

三张便签，三个裁定，一个整体状态。

1. **整体：保持 inactive（Table）。** 没有一份子建议达到激活门槛。这不是"Fantastic idea, and too hard to do"（那需要目标清晰），而是"**材料不足，无法估值**"。
2. **18.28 任意块作用域：Table；若激活，只考虑诊断形态（PROPOSAL B），否决语法形态（A）。** 理由：VB 的泄漏作用域是既有特征而非缺陷；主线在模式匹配作用域上主动选了 surrounding scope（2018.12.19）；A 引入第二种作用域心智且需新关键字；B 用零破坏诊断拿到 A 的大部分卫生价值。`Scope ... End Scope` 语法本场不进入任何排期。
3. **18.29 Rust 式所有权：Reject 语言特性（X），诊断窄版（Y）移交 analyzer 工作项。** 理由叠加：GC 已承担内存安全（价值稀释）；`Using` 已是确定性释放的 VB 惯用法（第二机制，原则 #3）；`Take` 与 LINQ 查询关键字冲突（事实）；借用检查在可别名、可反射、可 COM 的运行时上结构上不可强制（概念错位）；主线以 "not a VB thing" 推迟了更温和的可空分析（2018.02.07）。`Take`/`Borrow`/`Give`/`Lend` 四个关键字本场不登记为保留字。
4. **18.30 AI：无可判定。** 没有设计内容的片段不进三态机；作为 OPEN QUESTION 记录，要求作者给出"这到底指什么特性"再谈。
5. **跨建议对表**：与 `Try` 增强（3.9，放宽作用域）、`guarded-let`（`End Let` 后失作用域，收紧作用域）对表——Anthony 家族对作用域的收紧/放宽诉求散落多处，18.28 若激活必须与它们统一成一套作用域规则，否则会造出三套互相打架的语义。

**激活所需信号**（任何未来复活必须携带）：

- (a) **量化数据**：块作用域缺失造成真实事故/返工的场景抽样；或 double-dispose/use-after-dispose 在 VBScript.NET 目标库中的实测占比（`Probably`：前者低，后者更低）。
- (b) **语法与符号模型**：`Scope` 块是否引入新声明空间、是否允许与外层同名遮蔽——一句话说清，否则无从评估。
- (c) **对线证据**：证明 `Using` + analyzer + 拆 `Function` 三件套确实覆盖不了目标场景。
- (d) **`Take` 冲突的解法**：若坚持所有权，须先给出 `Take` 作为语句关键字与查询关键字的消歧方案。
- (e) **AI 片段的具体化**：把一句叹息改写成一个可评估的特性描述，或从建议中删除。

### Implication:

- 本建议状态标注更新为 **LDM Considering（Table）**；三块各自的裁定（A=Reject、B=Table/诊断、X=Reject、Y=analyzer、AI=OPEN）记入建议文档头部。
- 把"块外使用变量"的信息性诊断（PROPOSAL B）作为独立 analyzer/IDE 工作项列 TODO——这是本场唯一带正号的可落地产物。
- double-dispose/use-after-dispose 检测增强（CA2000/CA2213 家族）移交 analyzer 工作项，与 Y 合并。
- 与 `Try` 增强、`guarded-let`、`using-synclock-enhancements` 三份建议交叉引用作用域语义，避免多套作用域规则。
- 向建议作者发出 OPEN QUESTION：18.30 的 AI 片段到底指什么特性。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：18.30 的 AI 叹息指什么？AI 辅助编程？语言内嵌 LLM 类型？源生成器与模型协作？无描述则无法评估。
- `OPEN QUESTIONS`：块作用域 A 若复活，"`Scope` 内同名遮蔽"允许还是禁止？允许则改变既有 already-declared 规则，禁止则卫生价值减半。
- `OPEN QUESTIONS`：`Take` 若作为上下文关键字，语句语境与查询语境的消歧边界在哪？是否会被 `From x In y Take 5` 这类混合形态击穿。
- `OPEN QUESTIONS`：`Scope` 与 `Using`/`SyncLock`/`With` 嵌套时 `Exit`/`Continue` 的语义。
- `TODO`：块外使用变量信息性诊断的最小原型（零语法、默认关闭、只对无更早赋值的形态提示）。
- `TODO`：double-dispose/use-after-dispose 检测在 analyzer 层的现状盘点（CA2000/CA2213 覆盖率）。
- `Follow-up`：复查 Anthony 家族中所有"收紧作用域"表述（18.28、guarded-let `End Let`、3.9 `Try` 放宽）是否共享一套统一作用域模型。
- `Follow-up`：跟踪 C# 侧是否有任何"所有权/borrow"式语言的 uptake（对齐 2018.02.07 "We'll postpone this until we understand the uptake in C#"的判例）。

### 状态

- **LDM 状态：建议整体 LDM Considering（Table）**；子裁定：18.28 语法形态 Reject、诊断形态 Table；18.29 语言特性 Reject、analyzer 增强 Table；18.30 无法判定。
- **三态判定：Table（保持 inactive）** — 材料不足、价值未证、成本/风险压倒；唯一正产物（信息性诊断、analyzer 增强）不属于语言，直接移交工具层。

---

## 附录：特性评价

# 建议评价报告：proposal-rust-ownership.md

## 评价对象

- 建议：proposal-rust-ownership.md — 任意块作用域（18.28）+ Rust 式所有权（18.29）+ AI（18.30），三想法合并，均无设计
- 来源：Anthony 原文第 18 章 18.28–18.30（`..\..\AnthonyDesign_wordpress.txt` L3354–3378）：18.28 一句 "Just remembered that. People keep asking for this."；18.29 "Could we? Of course. Should we? Who knows?" + `Take, Borrow, Give, Lend`；18.30 "Ugh. (sigh) yeah, I know…"；`Take` 查询关键字另见原文 2.7 节（L651 `Take 10`）
- 配方目标：让变量作用域可收窄到任意语句组（无语法）；为 `IDisposable` 引入显式所有权表达（无语义）；AI 相关（无内容）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。三个想法均无示例、无语法、无改进度量；作者本人对 18.29 的预期是 "Who knows?"；"People keep asking" 无数据 | 已提供（原文仅三句；状态行 Prototype/Implementation/Specification 均占位链接） | 核心语法未定型 = 效果未显现；无任何可演示产物；AI 段无内容 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。Rust 所有权为外来概念直接照搬（`Take`/`Borrow`/`Lend` 机械式、不英语化）；三块血缘完全无关却被捆绑成一份；块作用域与 VB 泄漏作用域基因反向 | 已检查 | `Take` 与 VB LINQ 查询关键字冲突未识别；未对照 `Using` 这一现有资源管理机制；"not VB"风险全文未提 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节结构完整、**诚实不虚构**（明确标注"原文未给出任何语法"，避免品质最重违规）、未决问题如实列出 | 已检查 | Detailed design 三节全部为空/转述；无文法、无示例、无 Compatibility；状态行占位链接；AI 节无任何内容 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。风=与主线作用域决策（surrounding scope）反向、与 `Using`/`Try`/`Let` 家族作用域语义冲突未识别；暗=所有权"not VB"、宽松模式不兼容、`Take` 关键字冲突；文档无任何对冲 | 已检查（预测性结论，标"待定"；实际影响须"已采纳"后定） | 作为 inactive 存档影响可控，但激活即多处断裂；无应对方案 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。来源标注准确（18.28–18.30 逐字、无虚构材料）；未声明借鉴 C#（实际也未借鉴——C# 用 `using`/`Span`/analyzer 走的是另一条路，本建议完全未提这条对照） | 已检查 | 未标注 `Take` 已是 VB 查询关键字这一同语言内冲突；未标注与 `Using`（3.10）、`guarded-let`、`Try`（3.9）的作用域家族关系；无杂质 |

## 设计原则对照

- **与 VB 基因：偏离为主。** 无语法无法谈 #2（保持 VB-like），但所有权四个词机械外来、不英语化（#5 负分）；X 引入第二套资源机制、A 引入第二种作用域心智（#3 双撞）；X 把合法变非法、`Take` 双语境（#7）；三块均无普遍性数据（#6）；X 增加样板（#9 负分）。唯一亮点是"诚实不虚构"（品质层面，非特性层面）。
- **与主线关系：Anthony 独立延伸。** 主线对照表三块均无对应行；最近触点为 #117（Block-scoped Option Statements，2018.02.07 留 open，但那是 Options 作用域）与 2018.12.19 模式匹配作用域决策（主线选 surrounding scope，方向**反向**）；与 2014.02.17 声明表达式 Rejected 的终局一致（"None of this feels naturally 'VB'ish."）。与 `proposal-using-synclock-enhancements.md`（`Using`）、`proposal-return-byref.md`（别名）、`proposal-local-declarations.md`（`Let`）、inactive/`proposal-guarded-let.md`（`End Let` 作用域）在作用域语义上交互。
- **破坏性变更：无（当前）。** 全为新语法/新关键字，现编译代码零变化；但 X 一旦激活，use-after-take 从合法变非法是语义收紧，需 `langversion` 门控与警告策略。`Take` 上下文关键字对既有查询代码零影响（唯一干净点）。

## 总评

- **达成程度：未达成（作为可落地特性）/ 合格（作为备忘存档）。** 建议诚实转述原文、不虚构语法，作为 inactive 存档合格；作为语言特性，效果、特性、属性三维均不达标，且没有任何一层设计可供评估。
- **LDM 三态建议：Table（保持 inactive）**。子裁定：18.28 语法形态 Reject、诊断形态 Table（信息性诊断列为独立工作项）；18.29 语言特性 Reject、analyzer 增强（double-dispose/use-after-dispose）Table；18.30 无法判定（材料不足）。面向 VBScript.NET 的优先级同为 **Table**——产品叙事（宽松、晚期绑定、脚本卫生）与所有权概念正面冲突。
- **主要问题**：① 三块均无设计、无语法、无示例（"便签"不是"提案"）；② `Take` 与 LINQ 查询关键字冲突（事实，原文自己就在用 `Take 10`）；③ 所有权在 GC + 可别名 + 可反射运行时上概念错位，borrow checker 无法强制；④ 与主线 surrounding-scope 决策及 `Using`/`Let`/`Try` 家族作用域语义冲突未识别；⑤ AI 段无内容，无法判定；⑥ 无任何普遍性数据。

## 返工建议

- **拆分为三份独立建议**：块作用域、所有权、AI 各自成文，血缘与成本画像完全不同。
- **补充章节**：块作用域——具体语法（或明确放弃语法改诊断）、符号模型（是否新声明空间、同名遮蔽）、与 `Using`/`Try`/`Let` 作用域的对表；所有权——`Take` 关键字冲突消歧、与 `Using` 的职责边界、Option Strict Off 行为、analyzer 对比；AI——先定义特性内容。
- **补充证据**："People keep asking"的量化数据；泄漏作用域造成事故的场景抽样；double-dispose/use-after-dispose 在目标库的实测占比。
- **未决问题处理**：`Take` 冲突=先解决再谈激活；18.30=要求作者具体化，否则删除；块作用域同名遮蔽=明确允许/禁止后再评估。
- **设计探索**：把 18.28 重写为"块外使用变量信息性诊断"的零语法方案（本场 PROPOSAL B），这是唯一值得继续的设计方向；所有权仅保留 analyzer 级单次消费检测（Y）。

---

## 附录：C# 生态与互操作考量

> 本附录基于 dotnet/csharplang 浓缩索引（`..\..\..\csharplang-index.md`）并在 `..\..\..\csharplang` 镜像逐字核实原文。三张便签中，与 C# interop 强关联的只有 **18.29（Rust 式所有权）**：C# 从未用「所有权」回应同一批问题，而是走 **ref 安全模型（ref-safe-context）** + **`using` + analyzer** 两条互补路线，且曾在 2015 年正面 Reject 过与 Rust 所有权同构的方案。18.28（块作用域）、18.30（AI）与 C# interop 关联弱，如实简述。

### 相关 C# 现实方向

**（1）C# 对「确定性释放 / 所有权」的正面裁决：Reject（2015）。** C# 在 `meetings\2015\LDM-2015-02-11.md` 讨论过与 Rust 所有权结构同构的「destructible types」（C++-RAII 式：隐式析构、`move` 显式转移所有权、`ref` 借出）。原文逐字：

> "Destructible types offer a sweeping approach to deal with this, that is a complete departure from `using` and `IDisposable` and instead leans more closely on C++-like RAII features." → `meetings\2015\LDM-2015-02-11.md`

> "To prevent multiple disposal, such variables can't just be assigned to others. Instead, ownership must be explicitly transferred (with a `move` keyword)" → 同上

> "Additionally variables can be \"borrowed\" by being passed by `ref` to a method." → 同上

> "Overall, we see how this adds value, but not enough that having these concepts in the face of all C# developers. It doesn't earn back it's -100 points." → 同上（Conclusion）

C# 当时的替代路线与本场裁定同款——`using` + analyzer：

> "Could we take a step back and have analyzers encourage practices that are less error prone? Yes. You just cannot have analyzers enforce correctness. And you cannot have them improve perf by affecting codegen. And they cannot do the heavy lifting for you." → 同上

**（2）C# 对低层内存安全的回答是 ref 安全模型，不是所有权。** 从 C# 7.2 `Span<T>` 起，C# 持续把「栈上安全低层类型」做进类型系统：ref fields + `scoped` + `[UnscopedRef]`（C# 11）、`ref readonly`（C# 12）、ref struct interfaces + `allows ref struct`（C# 13）、first-class Span（C# 14）。该模型约束的是「引用/ref-like 值能逃逸到哪个作用域」，**不是「谁是所有者」**——别名天然自由（GC 兜底），只有逃逸受限。核心机制原文逐字：

> "We associate with each expression at compile-time the concept of what scope that expression is permitted to escape to, \"safe-to-escape\". Similarly, for each lvalue we maintain a concept of what scope a reference to it is permitted to escape to, \"ref-safe-to-escape\". For a given lvalue expression, these may be different." → `proposals\csharp-7.2\span-safety.md`（Overview）

> "The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack." → `proposals\csharp-7.2\span-safety.md`（Introduction）

C# 11 低层改进的目标是「用 C# 类型系统完全定义 `Span<T>`、移除 runtime 特判 `ByReference<T>`」——ref 安全是**语言层**规则，与 GC 并存互补，而非替代 GC：

> "Allow the runtime to fully define `Span<T>` using the C# type system and remove special case type like `ByReference<T>`" → `proposals\csharp-11.0\low-level-struct-improvements.md`（Motivation）

> "These enabled .NET developers to write highly performant code while continuing to leverage the C# language rules for type and memory safety.  It also allowed the creation of fundamental performance types in the .NET libraries like `Span<T>`." → 同上

**（3）unsafe-evolution（C# 15 候选）重定义 `unsafe` =「解引用非受管内存」，并明确 VB 不参与。** 内存安全被划成三段：GC 管受管内存、ref-safe-context 管逃逸、`unsafe` 只留给「非受管内存的解引用」：

> "We update the definition of `unsafe` in C# from referring to locations where pointer types are used, to be locations where memory unmanaged by the runtime is dereferenced." → `proposals\unsafe-evolution.md`（Summary）

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." → `proposals\unsafe-evolution.md`（VB 小节）

**（4）检索事实：** 在 `proposals` 目录对 ownership / borrow checking / borrow checker 全文检索 **零命中**；所有权/借用概念只在 2015 年 destructible types 讨论中出现过并遭 Reject。C# 生态对「所有权」的答案是：不存在。

**（5）18.28 / 18.30：** 块作用域在 C# 自 1.0 起就是默认（`{}`），C# 侧无任何新动向驱动或阻碍 VB 收窄作用域；AI 片段与 C# interop 无对应。两者均无 C# 原文可引，如实留白。

### 现实 vs 提案

- **18.29 所有权（X）vs C# 2015 判例：兼容——而且是互相印证的同一个裁决。** C# 对同一批问题（确定性释放、所有权转移、move/borrow、禁止隐式别名）的终局与本场 RESOLUTION 一字不差地同构：语言特性 Reject、`using` + analyzer 保留。C# 原判词 "It doesn't earn back it's -100 points." 等价于本场「X 是被高成本和高风险双重否决的典型」。**这不是冲突，而是给 RESOLUTION 加分的独立证据**——默认跟随 C#（原则 #4）在此有 C# 自身 11 年前的判例背书。
- **18.29 所有权（X）vs C# ref 安全模型：范式错位 → 脱节（本场追问 11 的 C# 侧实证）。** Rust 所有权前提是「别名即错误」（单一所有者、编译期 move、禁止 alias）；C#/CLR 前提是「别名免费且安全（GC 兜底），只约束栈上 ref-like 值的逃逸」。同一「内存安全」目标，Rust 靠编译期所有权，.NET 靠运行时 GC + 编译期逃逸分析。所有权需要 CLR 提供 move/无别名语义（CLR 没有，引用类型赋值即复制别名，`unsafe`/反射/COM 可绕过一切静态保证）——等于要求运行时改架构。本场「概念错位」判词在 C# 生态一侧得到实证。
- **18.28 块作用域：与 C# 无冲突、无借鉴 → 脱节。** C# 有块作用域但 VB 从未打算抄 `{}`（正文已述）；PROPOSAL B 诊断方案的走向不受 C# 影响。
- **18.30 AI：无 C# interop 对应。** 维持正文 OPEN QUESTION。

### 对 VBScript.NET 的适应建议

- **默认安全、不引入所有权。** C# 2015 判例 + 本场 RESOLUTION 一致：VBScript.NET 以 `Using` + analyzer（CA2000/CA2213 家族）作为「确定性释放」的唯一机制；不登记 `Take`/`Borrow`/`Give`/`Lend`。这与产品「宽松、晚期绑定、脚本卫生」叙事一致，也与 C# 生态「工具层纪律、语言层克制」的走向同向。
- **识别 C# ref 安全新元数据（通用桥接点）。** C# 11+ 模块会带 `[module: RefSafetyRules(11)]`（见 `proposals\csharp-11.0\low-level-struct-improvements.md` 的 RefSafetyRulesAttribute 节）；unsafe-evolution 落地后程序集会带 `MemorySafetyRulesAttribute`，requires-unsafe 成员带 `RequiresUnsafeAttribute`（见 `proposals\unsafe-evolution.md`）。VBScript.NET 若消费 C# 生成的现代低层库（Span/ref struct/ref fields），VB 编译器必须**认识这些元数据**才能正确校验「调用 requires-unsafe 成员」与 ref-like 逃逸边界。这与本提案无直接关系，但属「C# 生态前进 → .vbx 必须适配」的通用义务（决策文件 M8 已点名）。
- **source-gen / analyzer 桥。** C# 用 source generator 把「生成代码」搬到编译期（索引 T6）。本场唯一带正号的产物——PROPOSAL B 的块外使用提示、double-dispose/use-after-dispose 检测——应以 analyzer/source-gen 形式落地，与 C# 生态的「工具层纪律」接轨，而非语言层。这也天然规避了「VB 编译器不认识 C# 新元数据」的大部分暴露面（诊断跑在自家源码上）。

### 对既有 RESOLUTION / 三态判定的影响

- **无影响，且被 C# 侧强化。** 三态判定（Table）与子裁定（X=Reject、Y=analyzer、B=诊断）在 C# 生态中找到了 11 年前的先例（LDM-2015-02-11 destructible types Reject），无需修改正文。
- 新增一条未来复活时须回答的问题（扩展激活信号 (c)/(d) 的对照范围）：**「C# 2015 拒绝 destructible types 之后，C# 生态用 `using` + analyzer + ref-safety 路线解决了同一批问题；为什么这套对 VBScript.NET 不够？」** 若答不出，18.29 的激活门槛进一步抬高。

### OPEN QUESTIONS / Suspect

- `Suspect`：本附录「C# 2015 判例 vs VB 本场裁定的同构性」是**分析性对照**，非 C# 原文。C# 原文只证明「C# 拒绝过所有权模型」，不证明「VB 应跟随」；后者是原则 #4 的价值判断，本场已独立作出。
- `OPEN QUESTIONS`：C# 侧是否有任何所有权/borrow 式语言的 uptake（本场 Follow-up 已列）；`Suspect`：unsafe-evolution 的 `MemorySafetyRulesAttribute` 以 `15` 填充、`RefSafetyRules(11)` 以 `-langversion:11` 触发，均为提案状态，落地形态（属性名/版本号）以最终发布的 runtime 为准。
