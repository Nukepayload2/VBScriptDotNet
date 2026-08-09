# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周第三次语言设计会议——前两次分别落定了 `TypeOf ... Is` 类型流分析的共享引擎与本建议直接依赖的 `Null` 字面量。本建议的可空性流分析与 TypeOf 流分析被我们视为**同一台流分析引擎的两张脸**，所以这场讨论大部分时间在拆一件我们反复遇到的、被建议原文混在一起的两件事实：**机制（流分析）**与**语法面（`Animal?` / `Null`）**。拆开之后，本建议的可落地部分比原文看起来小得多，也安全得多。

## Agenda

* [Proposal: 可空性流分析（Nullability Flow Analysis）](#proposal-可空性流分析)

## Proposal: 可空性流分析

_Related: [2026-08-08 TypeOf 流分析会议](meeting-typeof-flow-analysis.md)（共享引擎）· [2026-08-08 Null 字面量会议](meeting-null-literal.md)（`Null` 字面量 Table）· [vbldm-notes-2018.02.07](../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（NRT 推迟决定）· [vbldm-notes-2017.08.30](../../vblang/meetings/2017/vbldm-notes-2017.08.30.md)（NRT 未就绪）· ModVB `proposal-nullability-flow-analysis.md` · `inactive/proposal-nullable-reference-types.md` · `proposal-null-safe-behaviors.md`_

### 场景与缺口

We started from the proposal's stated gap: 对可空变量直接调用成员，vanilla VB 给不出针对性诊断。建议的期望行为是——

```vb
Let animal As Animal? = SomeFunction()

' (Actual error text subject to change)
' Error: `Move` is not a member of `Animal?`.
' -OR-
' Error: Cannot call member `Animal.Move` from nullable value.
animal.Move()

If animal IsNot Null Then
    ' No error.
    animal.Move()
End If
```

And we like the *shape* of that expectation. 守卫之后直接访问成员、未守卫处给明确诊断——这是"消除样板"与"让错误可读"两个正方向直觉，和上一场 TypeOf 收窄是同一种 DX 冲动。

但这场会议一开始，我们就把一个事实摆在桌上，并确认它没有被建议原文声明：**示例里的 `Animal?` 与 `Null` 在今天的 VB 里都不存在。** `Animal?` 是未定稿的可空引用类型（NRT）语法（`inactive/proposal-nullable-reference-types.md`，Anthony 自己标注"实验性 / 正在修订"）；`Null` 字面量在今天的另一场会议上刚被我们维持 2014 年立场拒绝。建议原文把两件都**没有落地**的语法当作既成事实使用，这是本建议从头到尾要过的最重的一关。

我们把缺口拆成两个可分离的命题，后面全部讨论围绕它们展开：

1. **可空值类型**（`Nullable(Of T)`，`Integer?`）：类型系统**今天**就已编码可空性，`Function() As Integer?` 合法，`x Is Nothing` 是 HasValue 测试。这里做流分析**不需要任何新语法**。
2. **可空引用类型**（`Animal?`，`String?`）：类型系统**今天不区分** `Animal` 与"可空的 Animal"，要做流分析就必须先有 NRT 标注面。这里做流分析**被 NRT 卡死**。

### 候选方案

**PROPOSAL A — 完整 NRT 流分析（C# 式）。** 按建议原文逐字落地：`Animal?` 注解 + 全量流状态（Null / NotNull / MaybeNull）+ 未收窄访问给**错误** + 跨方法边界传播（返回类型标注）。这是"先打地基再盖楼"：必须先落 NRT 类型系统，再谈流分析。

**PROPOSAL B — 仅诊断增量，零新语法。** 不做 `Animal?` 注解面；对既有 `Nothing` 守卫（`If x Is Nothing` / `IsNot Nothing`）做流分析，编译器跟踪局部变量与参数的 null 状态。可空值类型场景（轨道 1）是主体：`If x IsNot Nothing` 之后 `x.Value` 合法，未收窄处访问 `.Value` 给**警告**。可空引用类型场景（轨道 2）仅当未来 NRT 落地时把同一引擎套到注解类型上。

**PROPOSAL C — 引擎先行，语法面后置。** 把 null 状态做成与 TypeOf 收窄、definite assignment **共享**的流分析引擎的第一类状态（上一场会议已定：两建议是同一引擎的两张脸），v1 只在"类型系统已编码可空性"的位置启用（即 `Nullable(Of T)`）。`Animal?` 语法面明确划给 NRT 工作项，本建议不承诺、不占位。

### 权衡：Q&A

- **A vs B：先落 NRT 再谈流分析，还是反着来？** 主线 2018 年对 NRT 的决定至今有效，逐字引用：*"There are probably a significant number of VB (and C#) projects where retrofitting this behavior is not desirable. While these warnings are valuable, it's unclear how popular this feature will be. And it may feel like a 'not VB' thing as we understand its usage. We'll postpone this until we understand the uptake in C#."* 2017.08.30 更直白：*"Not ready yet."* 到今天，C# 的采用率信号大概率已明朗，但**VB 用户要不要它、要它长什么样，Anthony 的 NRT 修订版一个字都还没交付**。让本建议排队等 NRT（= A），等于把一个可独立工作的引擎挂在一个没有交付日期的地基上。**结论：不取 A 的次序。**
- **B vs C：两者其实是一件事。** B 是"只做轨道 1"，C 是"把轨道 1 的引擎做对、同时把轨道 2 的接口留好"。我们用 C 的表述来描述 B 的实现，因为上一场会议已承诺共享引擎；区别只在标题。**结论：v1 取 B ∩ C。**
- **轨道 1 单独做，值不值得？** 这是整场被反复回访的问题。可空值类型在业务代码里不如引用类型普遍，`x.Value` 的 `InvalidOperationException` 确实是一类真实 bug。但单独看，价值中等；当它只是共享引擎的一个状态域时，边际成本很低，而且为轨道 2 排雷。**结论：值得，但不是头条特性。**
- **警告还是错误？** 见 Breaking change 追问。一句话预告：**原文的"诊断错误"不可接受，v1 只能是警告 + opt-in。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

本建议**自身不引入任何新语法**——这是它最干净的地方。它预设的两个语法（`Animal?`、`Null`）都属于别的建议，且都未落地：

- `Animal?` 引用类型标注面 = NRT，未定稿。它还与 `proposal-null-coalescing.md` 的**空指示符 `?`** 撞车——那份建议里 `?` 同时承担"值可空推断"（`True?` → `Boolean?`）、"可空引用类型标注"（`String?`）、"表达式后文档化可能为 null"（`lookup("key")?`）三职。同一符号在三个位置三种含义，且都与本建议的"可空类型"概念相关。**在任何一份建议把 `?` 的语义钉死之前，本建议的 `Animal?` 没有稳定的参照物。**
- `Null` 字面量 = 今天早些时候刚被 Table（字面量 Reject / 可空推断 Consider）。本建议的守卫写法 `IsNot Null` 依赖它。

**一句总结**：语法面是两座别的工地上的楼，本建议的地基（机制）先动工，语法面等邻居。

#### 2. 角案例与边界语义

**Else 分支。** 建议只画了 `IsNot Null` 真分支（非空）。完整的流分析必须处理镜像：`If x Is Nothing Then` 的真分支里 x **必空**，成员访问应立即警告；`Else` 分支里 x 非空，成员访问合法。

```vb
If animal Is Nothing Then
    animal.Move()   ' 必空 ⇒ 确定性警告（即使只做警告，这也是最尖的一类）
Else
    animal.Move()   ' 非空 ⇒ 合法
End If
```

**可空值类型的 `.Value` 与 `HasValue`。** 这是轨道 1 的真正战场，建议原文一个字没提——它把全部示例放在引用类型上。对 `Integer?` 类型 `x`：

```vb
Dim x As Integer? = GetMaybeNumber()

' 今天就能编译，但 x 为空时运行期抛 InvalidOperationException：
Console.WriteLine(x.Value)

If x IsNot Nothing Then
    Console.WriteLine(x.Value)   ' 守卫后：HasValue 为真，不警告
End If
```

注意 `Nullable(Of T)` 的成员语义分叉：`x.ToString()`、`x + 1`、`x = x + 1` 在空值上**不抛**（`ToString()` 返回 `""`，算术传播空值）；只有 `.Value` 与**收窄转换**（`CInt(x)`、`CType(x, Integer)`）在空值上抛。所以轨道 1 的"事实提取"目标不是"成员访问"，而是 `.Value` 与把空值收窄到非空值类型的转换。这与引用类型"任何成员访问都是潜在 NRE"完全不同——再一次说明两轨道是两套规则，只是同一个引擎。

**`If()` 表达式。** 两个操作数区域与 TypeOf 收窄同规则不对称：真操作数继承"非空"，假操作数继承"空或保持 MaybeNull"。二元 `If(x, fallback)` 本身不产生收窄（它是对 x 的取值，不是测试）。

```vb
Let v = If(x IsNot Nothing, x.Value, 0)   ' 真操作数：x 非空，.Value 合法
```

**`AndAlso` / `OrElse`。** 与 TypeOf 收窄同规则：`AndAlso` 右侧与 `Then` 区域继承左侧的正向测试；`OrElse` 右侧继承左侧为假时的状态。只从**裸测试或裸测试链**提取事实，复合布尔（`(a Or b) AndAlso ...`）不产生事实。

**守卫语句。** `If x Is Nothing Then Return`（同样适用 `Exit` / `Continue` / `Throw`）之后继续区 x 非空。这是与 TypeOf 收窄共享的最高价值惯用法，零新语法。

**循环后置条件。** 注意一个与 TypeOf 收窄方向相反、容易做错的例子：

```vb
Dim x As String = ReadLine()
Do While x IsNot Nothing
    x = ReadLine()
Loop
' 循环正常退出时，x 为 Nothing（循环条件为假 ⇒ x Is Nothing）。
' 所以这里 x 是"必空"，不是"非空"。
```

`While x IsNot Nothing` 的正常退出路径把 x 推成 null——这是"负收窄"可表示的一例（不像类型补集，null 状态是类型系统能表达的）。**流分析必须正确区分"循环出口处必空"与 TypeOf 场景的"循环出口处必为 T"**。`Do Until x Is Nothing` 同理。循环体内重赋值使状态每次迭代重置。

**重赋值与 `ByRef`。** `x = GetMaybeNull()` 把状态重置为 MaybeNull；把 x 作为 `ByRef` 实参传给可能改写它的被调方 ⇒ 调用点后状态作废（保守）。

**闭包捕获。** 与 TypeOf 收窄同规则：lambda 可能在收窄区域外延迟执行 ⇒ null 状态**不流入 lambda 体**；被捕获且在 lambda 内赋值的变量，外侧状态作废。

**`TryCast` / `CType` / `DirectCast`。** `TryCast(x, T)` 失败返回 `Nothing` ⇒ 结果**总是 MaybeNull**，即使 x 已收窄为 NotNull。`CType` / `DirectCast` 失败抛异常 ⇒ 结果 NotNull（但要求输入非空才能成功——`DirectCast(x, T)` 在 x 为 MaybeNull 时自身就该被警告）。`TypeOf x Is T` 测试**同时是 null 测试**（x 为 null 时 `TypeOf` 返回 False）⇒ 该测试的真分支应同时把 x 置为"非空 + 类型 T"。这是两场建议共享引擎后必须写进事实提取表的条目。

**字符串与数组。** `Nothing` 与 `""` 是两回事（`String` 成员访问在空串上合法、在 null 上 NRE），流分析只关心后者。数组变量本身可为 null，`arr(0)` 在 `arr Is Nothing` 时 NRE——轨道 2 若落地，数组引用也走 NotNull 分析。

#### 3. 作用域与绑定

对轨道 1（`Nullable(Of T)`），`x` 在守卫后的成员访问绑定到 `Nullable(Of Integer).Value`——绑定对象不变，只是编译器**抑制警告**。对轨道 2（若 NRT 落地），`animal.Move()` 处的语义模型应返回 `Animal.Move`，表达式类型仍是 `Animal`（`Animal?` 与 `Animal` 是同一 CLR 类型，null 状态是编译期元数据）——**这与建议原文的错误文案"`Move` is not a member of `Animal?`"相矛盾**：`Move` 确实是 `Animal` 的成员，C# 对 `string? s; s.Length` 报的也不是"不是成员"，而是 CS8602 "Dereference of a possibly null reference"。原文的错误文案暴露了一个概念错误：它把 `Animal?` 当成一个**包装类型**（像 F# `Option(Of T)`）而非**带注解的类型**。`Probably`：建议的备选文案 "Cannot call member `Animal.Move` from nullable value" 方向正确，但仍应以 C# 的"可能为 null 的解引用"为参考。

#### 4. 与既有特性的交互

- **`?.` 空条件访问**：`animal?.Move()` 永远安全，不产生任何诊断。流分析是 `?.` 的编译期互补物；`proposal-null-safe-behaviors.md` 把 `?.` 扩展到语句（`For Each ... ?`、`Await ... ?`）——若那些落地，它们的"可空"信息与流分析必须同源。
- **可空值类型的相等**：2018.02.28 会议提醒我们，VB 的 `=`/`<>` 对可空值类型用三值逻辑，在某些位置与 C# 行为不同（*"quirks in a good way"*）。流分析对"空值参与比较"不改变任何行为——它只决定 `.Value` 是否被警告。与 `proposal-null-equality-operators.md`（`?=`/`?<>` 二值逻辑）的边界要写清：流分析不判定相等语义，只判定空状态。
- **条件最佳公共类型**（`proposal-conditional-best-common-type.md`）：`If(cond, a, b)` 的推断类型若与 null 状态纠缠（如操作数之一必空），v1 明确"多分支赋值不产生跨分支空状态"——与 TypeOf 会议同一条。
- **definite assignment**：已存在；null 状态分析与其共享流格，不新增边界。
- **晚期绑定 / Option Strict Off**：见第 6 条。

#### 5. Breaking change 与兼容性

这是整场最重的一问，且结论与 TypeOf 会议**符号相反**。TypeOf 收窄的"启用型"规则（只启用"宽类型上绑定会失败"的成员，不改既有成功绑定）在这里**不适用**——因为对引用类型，`animal.Move()` 在 vanilla VB 里**今天就能编译**（运行期才 NRE）。可空流分析的全部效果就是**让原本可编译的代码在未收窄处失败**。这正是 C# NRT 的模型，也正是 2018 年主线顾虑的 *"a wall of compiler warnings ... retrofit this behavior"* 与 *"it may feel like a 'not VB' thing"*。

因此：

- **警告，不是错误。** 原文的 "Error: ..." 若落地，等于把所有依赖运行期 NRE 的存量代码（"hundreds of thousands of quiet customers"）在**重编译时变成编译失败**。C# 用警告 + 按程序集 opt-in，且绿地上全是警告墙、需要逐条消化——我们几乎从不做这种破坏，**除非** opt-in。
- **opt-in 与门控**：必须 `langversion` / 项目开关门控，默认关闭（或默认最低严重度警告）。用户主动打开才看到新诊断。这与 2018.02.07 的 *"The user opts in per assembly"* 一致。
- **轨道 1 也是破坏性变更**（警告级）：`Dim x As Integer? = Nothing : Console.WriteLine(x.Value)` 今天无警告，开启轨道 1 后给警告。影响面窄（可空值类型 + `.Value`/收窄转换），但仍是"重编译后行为变化（警告）"。需要在 spec 里逐条列证。
- **编译期失败 vs 运行期失败的迁移报告**：哪些代码从"运行期 NRE/InvalidOperationException"变成"编译期警告"，必须给一份样例矩阵，不能只说"更好"。

#### 6. Option Strict / 编译选项分叉

两条路径行为必须一致，且**不得把宽松模式下的晚期绑定改为早期绑定**：`animal.Move()` 在 `Option Strict Off` 下对 `Object` 是晚期绑定，流分析不得因为"animal 被收窄为非空"而把它改成早期绑定（那会改变运行期行为与异常时机）。轨道 1 对 `Nullable(Of T)` 无晚期绑定问题（`Nullable(Of T)` 不是 `Object`）。轨道 2 在 `Option Strict Off` 下应整体不启用（用户没要求类型安全，就不该得到类型安全的警告噪音）。

#### 7. IDE / IntelliSense

悬波形（squiggle）、快速操作（quick action：插入守卫 / 建议 `?.` / 建议 `GetValueOrDefault()`）、悬停显示 null 状态（"Animal（可能为 Nothing）"）——全链路都要在原型里验证。错误文案是关键：`Move is not a member of Animal?` 会误导（见第 3 条），且不提供修复建议的诊断是半成品。建议文案与修复器同时设计。

#### 8. 数据 / 普遍性

守卫惯用法（`If x Is Nothing Then Return`）在真实代码里高频——这是与 TypeOf 会议同样的最强证据。但**没有量化数据**：没有 `.Value` 误用导致的 `InvalidOperationException` 崩溃统计、没有用户请求数。轨道 1 的普遍性（可空值类型 + `.Value`）在业务代码里**弱于**引用类型守卫。C# 采用率信号（2018 年的"等信号"）到今天大概率已明朗，但那是 C# 的，不是 VB 的——VB 用户对 NRT/可空分析的接受度我们拿不到数据。We think：在有数据之前，"普遍性"是假设。

#### 9. 更简替代

- **`?.`**：`animal?.Move()` 已存在，覆盖大部分"可能是 null"的成员访问。它不产生诊断、不流分析，但也不需要。流分析的价值在于"**别人给你的**可空值"（函数返回 `Integer?`、API 返回可空）——那些位置 `?.` 无法替你在守卫后去点号。
- **analyzer + 诊断**：`x.Value` 在 MaybeNull 上给提示、`If x Is Nothing Then Return` 后提示可省略守卫——这些**今天就能用 Roslyn analyzer 做**，不碰语言。这是对我们的最强竞争者：若轨道 1 只做"警告"，analyzer 能做到 80%；语言版本的价值在于语义模型 / IDE / 未来 NRT 的**同一套状态**，而不是警告本身。
- **什么都不做**：维持现状，`x.Value` 运行期抛、引用类型 null 运行期 NRE。代价是这些 bug 只有测试/运行期才暴露。

#### 10. 成本 / 优先级

引擎与 TypeOf 收窄共享，边际成本主要是"事实提取表 + 警告管线 + IDE 波浪线"——中等偏低。**真正的成本不在实现，在 NRT 依赖**：若做 A（完整引用类型流分析），实现量接近 C# 可空分析的全程，且建立在未定稿语义上。做 B ∩ C（轨道 1）成本可控、无上游依赖。优先级：**排在 TypeOf 收窄之后**（或与之并行，共享引擎），且必须在 `?` 空指示符（null-coalescing 建议）钉死之前锁住接口，避免语义打架。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍。null 状态是纯编译期概念；对 `Nullable(Of T)` 不改变任何 IL（`.Value` 本来就是 `get_Value()` 调用，只是编译器选择性告警）。轨道 2 若落地，NRT 的**表面标注跨程序集传播**才是硬骨头——2018.02.07 Part 2 已指出 *"at least at the implementation level it won't be as simple as managing attributes"*——那笔账记在 NRT 工作项头上，本建议不承担。

#### 12. 值不值得做

- **轨道 1**：价值中（真实 bug 类，但不普遍）、成本低（共享引擎）、风险低（警告 + 门控）。**值得做。**
- **轨道 2**：价值高（引用类型才是大头）、成本高、风险高（破坏性 + 未定型 NRT）。**在做完 NRT 之前不值得做，做完 NRT 之后是同一引擎的开关。**
- **原文的完整形态（A）**：价值 × 成本 × 风险 不划算，我们明确不为它排队。

### VB 基因对照

逐条对照设计原则：

- **原则 9「消除常见样板」**：正中靶心。守卫惯用法是真实样板，`If x Is Nothing Then Return` 之后能直接 `x.Value` 是"让既有惯用法更聪明"，极 VB。
- **原则 3「不引入第二种做事方式」**：**本建议自身零新语法，得分**。但它预设的两件语法（`Animal?`、`Null`）都是别家建议引入的"第二种方式"——`Null` 尤其（引用类型上与 `Nothing` 完全等价，上一场已否决）。这条原则的扣分记在依赖项上，不记在本建议。
- **原则 4「默认跟随 C#，除非有充分理由」**：可空分析是 C# 先例，本建议默认跟随——但**跟随的方式**（警告 vs 错误、opt-in vs 默认）正是 VB 行使"充分理由偏离"的位置：C# 的警告墙体验（2018 记录直说的 *"it may feel like a 'not VB' thing"*）我们不要原样照搬。
- **原则 7「避免隐蔽的控制流/语义变化」**：本建议把"运行期失败"变成"编译期诊断"，**改变的是失败时机**。这是设计原则最警惕的一类，必须用警告 + 门控对冲。没有这两条，它就是又一个 `Return?`。
- **原则 5「读起来像英语、对新手友好」**：守卫后直接 `x.Value` 不需要解释；但 `Animal?` 的"？类型"语法对新手不直觉（与 null-coalescing 建议的 `?` 过载叠加更甚）。
- **2.3 主线对照表**：`Null 安全全家桶` 一行——主线对"null 条件 AddHandler"已 No Plans，Anthony 大范围铺开，"主线保守，Anthony 激进"。本建议属于这个家族里**最温和、最可编译期化**的一员（没有新语法、只有诊断）；它不改变家族的方向判断，但可以作为家族的"引擎先行"部分先行落地。`Null 字面量` 一行——主线保守、Anthony 激进，上一场已 Table，本建议的 `IsNot Null` 写法随之下架。

### 诚实分层

- **事实**：2018.02.07 主线对 NRT 决定"推迟到观察 C# 采用率"（逐字引用见上）；2017.08.30 "Not ready yet" 且 `!` 与字典访问、单精度类型字符冲突；Anthony NRT 修订版未交付（inactive 建议自认"实验性 / 修订中"）；`Null` 字面量今天已 Table（字面量 Reject）；`Nullable(Of T)` 今天就可编码可空性、`Is Nothing` 是 HasValue 测试、`.Value` 与收窄转换在空值时抛 `InvalidOperationException`；`TypeOf x Is T` 在 x 为 null 时为 False（即类型测试同时是 null 测试）；TypeOf 流分析会议已定共享引擎。
- **Probably**：到 2026 年 C# NRT 已被主流采用（2018 年的"等待信号"已触发）；轨道 1（`Nullable(Of T)` 流分析）在共享引擎上可实现；`IsNot Null` 若被接受则与 `IsNot Nothing` 语义等价——但正因为等价，`Null` 拼写无增量价值。
- **Suspect**：原文错误文案"`Move` is not a member of `Animal?`"把 `Animal?` 当包装类型，与 NRT"注解类型"模型不符（C# 报"possible null reference"而非"不是成员"）；轨道 1（`.Value`/收窄转换警告）在真实 VB 业务代码中的普遍性没有数据；"可空引用类型流分析的高价值"是否值得等 NRT 的长期延迟。
- **OPEN QUESTIONS**：① `Animal?` 语法面归 NRT 后，本建议的轨道 2 是否还需要独立 spec；② 警告的默认严重度与 `langversion` 门控的确切形态；③ `?` 空指示符（null-coalescing 建议）与本建议对"可空"一词的语义共享；④ 轨道 1 的 `.Value` 警告是否也覆盖"收窄转换"（`CInt(x)`），还是 v1 只做 `.Value`。
- **TODO**：跟踪 Anthony NRT 修订版；与 TypeOf 流分析团队对表事实提取（`TypeOf x Is T` 同时产生类型收窄 + 非空）；写"可空值类型 vs 可空引用类型"双轨差异表；为 `.Value` 误用收集崩溃数据。

### RESOLUTION:

1. **机制原则上采纳**：null 状态流分析与 TypeOf 收窄、definite assignment 共用同一流分析引擎；状态域 = `Null / NotNull / MaybeNull` × 类型收窄。v1 范围与 TypeOf 收窄一致：**显式局部变量与参数**；不收窄属性、字段与任意 lvalue；不流入 lambda；重赋值、`ByRef` 实参、闭包内赋值 ⇒ 状态作废。
2. **拆双轨**：
   - **轨道 1（可空值类型 `Nullable(Of T)`）＝ Active（v1）**。零新语法、无 NRT 依赖。事实源是既有 `Is Nothing` / `IsNot Nothing` 守卫与 `AndAlso` / `OrElse` 区域；目标是 `.Value` 与"把 MaybeNull 收窄到非空值类型"的转换的警告。
   - **轨道 2（可空引用类型 `Animal?`）＝ Table**。语法面归 NRT 工作项；NRT 方向明朗后本引擎直接复用。本建议不承诺 `Animal?` 语法、不排队等 NRT。
3. **`Null` 字面量依赖下架**：守卫写 `IsNot Nothing`，不用 `IsNot Null`。`Null` 字面量已 Table，且流分析只需要"此处是否为 null"的概念，`Is Nothing` 已完整提供。
4. **诊断 = 警告，不是错误**。原文的 "Error: ..." 不可接受——那会把存量代码从运行期失败改成编译期失败。C# 用警告 + 按程序集 opt-in，我们跟随，并加 `langversion` / 项目开关门控，**默认关闭**。轨道 1、轨道 2 都是如此。
5. **错误文案**：否决 "`Move` is not a member of `Animal?`"（误导，`Move` 是 `Animal` 的成员）。候选为 C# 式"对可能为 null 的引用解引用"与建议备选 "Cannot call member `Animal.Move` from nullable value"；最终文案与修复器（插入守卫 / 建议 `?.` / 建议 `GetValueOrDefault()`）一起在原型中定。
6. **TypeOf 收窄的"启用型"规则在本建议不适用**：那是"启用成员"，本建议是"抑制诊断"。后者改变失败时机，所以必须警告 + 门控，并在 spec 中给出"运行期失败 → 编译期警告"的迁移矩阵。
7. **`TypeOf x Is T` 同时是 null 测试**：事实提取表中，该测试的真分支同时产出"类型收窄 + NotNull"。这是与 TypeOf 团队共享引擎的第一条合并规则。

### Implication:

- 最小原型：轨道 1（局部变量 + 参数 + `Is Nothing`/`IsNot Nothing` 守卫 + `AndAlso`/`OrElse` 区域 + `.Value` 警告）；验证语义模型 `GetTypeInfo`、IDE 波浪线与快速操作。
- 与 TypeOf 流分析团队对表：共享状态表示、事实提取文法、行为差异表（`IsNot Nothing` 与 `IsNot Mammal` 同一条路径、`TypeOf x Is T` 的复合事实）。
- 起草 speclet：警告策略与 `langversion` 门控、`Nullable(Of T)` 角案例（`.Value`/`HasValue`/`GetValueOrDefault`/收窄转换/`If()` 三值逻辑/循环后置"出口必空"）、迁移矩阵。
- 补一份 Compatibility 分析：轨道 1 对存量 `.Value` 代码的警告影响面；轨道 2 的破坏性预估（留给 NRT）。
- 与 null-coalescing 建议对表：`?` 空指示符的语义必须在轨道 2 动工前钉死。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：轨道 2 是否值得为等 NRT 而长期保留独立建议文件，还是并入 NRT 工作项（`Probably`：并入，本建议只留轨道 1 + 引擎接口）。
- `OPEN QUESTIONS`：警告默认严重度与门控形态（项目开关 vs 仅 `langversion`）；"quiet customers"默认零打扰。
- `OPEN QUESTIONS`：轨道 1 v1 是否覆盖"收窄转换"（`CInt(x)`、`CType(x, T)`），还是只做 `.Value`。
- `TODO`：量化 `.Value` 误用导致的 `InvalidOperationException` 占比，为轨道 1 的普遍性补证据。
- `Follow-up`：`For Each` 枚举器、`Await`、索引器在 null 状态下的规则（`arr(0)`、`list(0)` 若接收者为 MaybeNull）。

### 状态

- **LDM 状态：Active（轨道 1，限定范围）**；轨道 2 与 `Animal?` 语法面随 NRT 走（Table）。
- **三态判定：Active / Table 并存**——机制与轨道 1 立即推进；轨道 2 与 `Null` 拼写下架。原始完整形态（PROPOSAL A）不在 Active 之列。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-nullability-flow-analysis.md` — 可空性流分析：守卫后变量视为非空、未收窄处成员访问给明确诊断。
- 来源：Anthony 原文（`Nullable(Of T)`/可空分析与 NRT 相关的章节，原文未标注章节号）；隐性借鉴 C# NRT 流分析（未声明）；守卫惯用法继承 VB 既有 `Is Nothing`/`IsNot Nothing`（未点明）。
- 配方目标：`If animal IsNot Null` 之后 `animal.Move()` 不再报错；未收窄位置给清晰可读错误。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰、示例可演示，但**示例依赖未落地的 `Animal?` 与 `Null`**——在今天的 VB 里不能编译，效果无法验收；关键子效果（引用类型流分析）被 NRT 卡死；可独立显现的轨道 1（`Nullable(Of T)` `.Value` 警告）建议原文完全没写。证据止于书面（状态行为占位链接）。 | 已检查 | 主效果（引用类型）未显现；可显现部分（值类型）未设计；占位链接 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。机制本身零新语法、消除样板、让既有守卫惯用法更聪明——很 VB；但整体是 C# NRT 流分析的搬移，`Animal?` 语法是外来面，`Null` 拼写是已被否决的第二空字面量，且打包了未定稿 NRT 依赖。 | 已检查 | 外来成分未 VB 化到可落地；打包未定稿依赖 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全，但：无文法/状态机描述；无兼容性/breaking-change 分析；未声明"依赖 NRT 与 `Null` 字面量"这两个前提；边界案例（`Else` 分支、`.Value`/`HasValue`、`AndAlso`/`OrElse`、闭包、`ByRef`、循环后置、Option Strict 分叉）全部缺失；3 个未决问题虽具体但把最关键的前提（NRT）当既成事实。 | 已检查 | 前提未声明；边界过薄；无兼容性分析 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。得=雷（轨道 1 纯增量提速）、水（守卫惯用法的差异化 DX）、光（盘活存量 `Nothing` 守卫）；失=暗（把运行期失败变编译期失败 = 破坏性，未识别）、风（依赖未定型 NRT 与刚被否决的 `Null`，演化一致性断裂）。文档未权衡这些维度。 | 已检查（预测待定） | 破坏性/一致性断裂风险未被文档识别；实际影响须"已采纳"后定 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。完全未标注材料来源：借鉴 C# NRT 未提、继承 VB `Nothing` 守卫惯用法未提、`Animal?` 与 `Null` 两个未落地语法作为既成事实使用而未声明依赖、Anthony 原文未标章节。影响预估（"不再报错/给错误"）以不存在的语法为前提，与可落地能力明显不符。 | 已检查 | 来源零标注；混入 `Null`（已被否决）与 NRT（未定稿）杂质 |

### 设计原则对照

- **与 VB 基因：主体一致，局部偏离**。一致：零新语法（原则 3 得分）、消除样板（#9）、守卫惯用法读起来像英语（#5）。偏离：把"运行期失败"改为"编译期诊断"触碰原则 7（隐蔽语义变化），原文以错误实现且无门控，未自行识别；`Animal?`/`Null` 依赖引入第二种做事方式（#3，记在依赖项）。
- **与主线关系：主线保守，Anthony 激进**（2.3 对照表 `Null 安全全家桶` 一行）。主线 2018 对 NRT 明确"推迟到观察 C# 采用率"，本建议是家族里最温和的一员（纯诊断、无新语法），但引用类型轨道仍踩在主线未落地的 NRT 上；`Null` 拼写与主线 2014 拒绝、上一场维持的立场冲突。
- **破坏性变更：有（警告级）**。轨道 1 使存量 `.Value`/收窄转换代码从无警告变为警告；轨道 2 若按原文做错误则把运行期 NRE 变为编译期失败。文档未分析，须警告 + opt-in 门控 + 迁移矩阵。

### 总评

- **达成程度：部分达成**——机制与轨道 1 的价值成立、可独立落地；但原文把"机制"与"语法面"混为一谈，把未落地的 `Animal?`/`Null` 当既成事实，主效果（引用类型）无法在当前语言中显现。
- **LDM 三态建议：Active（轨道 1，限定范围）/ Table（轨道 2）**——轨道 1（`Nullable(Of T)` 流分析）以"局部变量 + 参数 + 守卫 + `.Value` 警告 + 门控"先行；`Animal?` 语法面随 NRT；`Null` 拼写下架。
- **主要问题**：① 依赖未定型 NRT 与已否决的 `Null` 而不自知（前提未声明）；② "诊断错误"的破坏性未分析，违背"几乎从不破坏"；③ 把"机制"与"语法面"混为一谈，未拆双轨；④ 边界案例（`.Value`/`HasValue`、`Else` 分支、循环出口必空、Option Strict 分叉）完全缺失；⑤ 证据止于书面（占位链接、无原型、无数据）。

### 返工建议

- **补充章节**：兼容性/breaking-change（`langversion` 门控、警告策略、"运行期失败→编译期警告"迁移矩阵）；依赖声明专节（NRT 未定稿、`Null` 已 Table）；状态机/事实提取文法（哪些布尔表达式产生 null 事实、`TypeOf x Is T` 的复合事实）；`Nullable(Of T)` 角案例专节（`.Value`/`HasValue`/`GetValueOrDefault`/收窄转换/`If()` 三值逻辑/循环出口必空）。
- **补充证据**：轨道 1 最小原型（局部变量 + 参数 + 守卫 + `.Value` 警告）；`.Value` 误用崩溃数据；语义模型与 IDE 波浪线验证；编译时间成本。
- **未决问题处理**：明确"守卫用 `IsNot Nothing`、弃 `Null`"；错误文案二选一并附修复器；轨道 1 v1 是否覆盖收窄转换；轨道 2 是否并入 NRT 工作项。
- **设计探索**：与 TypeOf 收窄统一引擎后的状态域合并（Null/NotNull/MaybeNull × 类型收窄，`TypeOf x Is T` 真分支 = 收窄 + NotNull）；`?` 空指示符（null-coalescing 建议）与本建议对"可空"的语义共享契约；轨道 2 的注解传播接口预留给 NRT。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang`（dotnet/csharplang main 镜像）与 `..\..\vblang` 已核实原文；所有 C#/VB 引文逐字一致并标注来源。本提案是 ModVB 提案里与 C# 8 NRT 关系**最直接**的一个——C# 8 的可空引用类型即"流分析 + 警告"，本会议拆出的「机制（流分析）/ 语法面（`Animal?`、`Null`）」二分，在 C# 侧是同一条产品线的两块，而非两件事。

### 相关 C# 现实方向

1. **C# 8 NRT 就是"流分析 + 警告"，不是"错误 + 新类型"**。C# 8 提案对机制的描述：
   > "A flow analysis tracks nullable reference variables. Where the analysis deems that they would not be null (e.g. after a check or an assignment), their value will be considered a non-null reference."
   → `proposals\csharp-8.0\nullable-reference-types.md`（Checking of nullable references）
   诊断形态是警告：*"Otherwise, a warning is given if a nullable reference is dereferenced, or is converted to a non-null type."*（同文件）。与本会议 RESOLUTION #4（警告非错误）同向。

2. **opt-in / 门控是 C# 的第一公民，且默认关闭**。C# 8 提案 Breaking changes 一节：
   > "Non-null warnings are an obvious breaking change on existing code, and should be accompanied with an opt-in mechanism."
   > "So nullable warnings also need to be optional"
   → `proposals\csharp-8.0\nullable-reference-types.md`（Breaking changes）
   C# 9 规格给出粒度控制：项目级 + `#nullable` / `#pragma warning`（`#nullable disable|enable|restore` 及 `warnings`/`annotations` 子目标），并写明 *"If no project level settings are provided the default is for both contexts to be *disabled*."* → `proposals\csharp-9.0\nullable-reference-types-specification.md`（Nullable contexts）。本会议「警告 + `langversion`/项目开关门控、默认关闭」在 C# 有成熟对应物。

3. **`!`（null-forgiving / "damnit"）运算符**。C# 用它显式把表达式标为非空：
   > "A nullable reference can also explicitly be treated as non-null with the postfix `x!` operator (the "damnit" operator), for when flow analysis cannot establish a non-null situation that the developer knows is there."
   → `proposals\csharp-8.0\nullable-reference-types.md`（Checking of nullable references）
   规格化后：*"The postfix `!` operator has no runtime effect - it evaluates to the result of the underlying expression. Its only role is to change the null state of the expression to "not null", and to limit warnings given on its use."* → `proposals\csharp-9.0\nullable-reference-types-specification.md`（The null-forgiving operator）。`!` 的设计（目标类型 / 固有类型 / `default!`）在 LDM 讨论过 → `meetings\2017\LDM-2017-08-16.md`（The null-forgiving operator）。

4. **C# 9 规格化三态流状态，且跟踪范围大于本提案 v1**。C# 的 null state 是 "not null" / "maybe null" / "maybe default"（第三个为类型参数保留）：
   > "Every expression in a given source location has a *null state*, which indicated whether it is believed to potentially evaluate to null. The null state is either "not null", "maybe null", or "maybe default"."
   → `proposals\csharp-9.0\nullable-reference-types-specification.md`（Null state and null tracking）
   跟踪对象含字段与属性，不止局部变量：
   > "For certain expressions denoting variables, fields or properties, the null state is tracked between occurrences, based on assignments to them, tests performed on them and the control flow between them. This is similar to how definite assignment is tracked for variables."
   → 同文件（Null tracking for variables）；其 `tracked_expression` 文法为 `simple_name | this | base | tracked_expression '.' identifier`（标识符为字段或属性）。
   而**调用结果不跟踪**：*"The null state of an `invocation_expression` is not tracked by the compiler."*（同文件，Invocation expressions）——与本会议「流分析的价值在『别人给你的可空值』」判断一致。

5. **元数据：`NullableAttribute`（跨语言识别的核心）**。C# 8 提案：
   > "Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them."
   → `proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation）
   且当时留下开放问题：*"We need to decide if only nullable annotations are included, or there's also some indication of whether non-null was "on" in the assembly."*（同文件）——即逐成员属性之外是否还有程序集级标记。
   实际发射证据：extensions 提案的 IL 片段直接出现 `.custom instance void NullableAttribute::.ctor(uint8) = (...)`（C# 对泛型类型参数发射该属性）→ `proposals\csharp-14.0\extensions.md`。属性数量膨胀后 C# 引入压缩策略，LDM 记录对比 `NullableAttribute` 时提到 *"before we invested in a compression strategy"* → `meetings\2022\LDM-2022-01-24.md`（Required members metadata representation）。

6. **可空值类型：C# 提案自己留白的机会点**。C# 8 提案在 Nullable value types 一节把「对可空值类型做流分析」列为可选项——这正是本提案轨道 1：
   > "Another opportunity is to apply the flow analysis to nullable value types. When they are deemed non-null, we could actually allow using as the non-nullable type in certain ways (e.g. member access). We just have to be careful that the things that you can *already* do on a nullable value type will be preferred, for back compat reasons."
   → `proposals\csharp-8.0\nullable-reference-types.md`（Nullable value types）

7. **先例：VB 不跟进特性，但编译器必须认识 C# 新元数据**。unsafe-evolution 对 VB 的明确表态（本索引第四节已核实）：
   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   → `proposals\unsafe-evolution.md`（VB 小节）
   同样的逻辑适用于 NRT：VB 可以不落 `Animal?` 语法，但 VB 编译器仍须认识 `NullableAttribute`，否则无法正确消费 C# 标注库。

### 现实 vs 提案

| 维度 | C# 现实 | 本提案决议（本会议） | 判定 |
|---|---|---|---|
| 诊断形态 | 警告 + 按程序集 opt-in、默认关闭 | RESOLUTION #4：警告 + `langversion`/项目门控、默认关闭 | **兼容**——几乎是对 C# 原文的重述 |
| 轨道 1（可空值类型） | C# 提案列为"另一机会"，未主打 | Active（v1） | **兼容且差异化**——VB 走 C# 犹豫未落地处 |
| 跟踪范围 | 局部/字段/属性（`tracked_expression` 含 `.identifier`） | v1 只跟踪显式局部变量与参数（RESOLUTION #1） | **需桥接**——跨语言同段逻辑诊断不一致 |
| `!` 抑制语法 | null-forgiving `!` | 无对应物 | **需桥接**——`!` 令牌被 VB 字典访问与单精度类型字符占用 |
| 元数据 | `NullableAttribute`（压缩后）+ 程序集级标记 | 轨道 1 零新元数据；轨道 2 未定 | **需桥接（轨道 2）**——消费 C# 标注库须解码；若落 NRT 须发射兼容属性 |
| 状态模型 | 三态 + "maybe default"（类型参数专用） | Null / NotNull / MaybeNull | **需桥接（轨道 2）**——泛型场景跨语言状态粒度不一致 |
| 调用结果跟踪 | 不跟踪 invocation 结果 | 「别人给你的可空值」是价值点 | **兼容**——同一结论 |
| 破坏性 | C# 自认 breaking，靠 opt-in 对冲 | 轨道 1 也自认警告级破坏 | **兼容** |
| AOT / trimming | null 状态是编译期元数据，非 AOT 负担 | 纯编译期概念，IL 零改动 | **兼容 / 低摩擦**——不像 `Any`/晚期绑定（决策文件 M2/M8 张力） |

**需展开的三处桥**：

- **`!` 的 VB 对应物（令牌冲突）**。VB LDM 2017.08.30 已记录：*"the damnit operator `!` conflicts with both VBs dictionary-access operator `dict!key` and the type character for single-precision floating-point numbers `Dim radius!` so we'll have to resolve that."* → `../../vblang/meetings/2017/vbldm-notes-2017.08.30.md`。轨道 2（若 NRT 落地）需要一个不撞令牌的抑制拼写；本会议未提出候选 → 列入 OPEN QUESTIONS。
- **`NullableAttribute` 消费**。C# 库的 null 标注经 `NullableAttribute` 跨程序集传播（完整的编码/压缩方案与程序集级标记见 ECMA standard §22.5.7，不在本仓库内）；VB（.vbx）编译器即使当前不落 NRT，也应解码这些属性，才能对 C# 标注 API 给出正确诊断。轨道 1 不受此影响——`Nullable(Of T)` 的可空性已编码在 CLR 类型里，**零新元数据**，这是轨道 1 最大的互操作优势。
- **跟踪范围差异**。C# 跟踪字段/属性，同一守卫惯用法在 C# 收窄、在 VB（v1）不收窄；对"VB 作为 C# 库消费者"的场景，诊断差异会暴露给用户。不必在 v1 解决，但要在 spec 里标为已知差异。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：轨道 1 保持"警告 + 默认关闭"，与 C# `#nullable` 默认关闭一致；门控建议做成"项目开关"而非只 `langversion`，以对齐 C# "per assembly" 语义（2018.02.07 VB 记录同词）。`Option Strict Off` 下整体不启用（本会议第 6 条），避免把晚期绑定误改早期绑定。
- **source-gen / analyzer 桥**：C# 生态已示范"诊断可下沉到 analyzer"（NetAnalyzers + nullable 警告是标准组合）；本会议「更简替代」已指出 analyzer 能做到 80%。建议：轨道 1 先用**内置 analyzer + 语义模型同一状态**起步（语言改动最小、可随 Roslyn 发布），把"语言化"留到与 TypeOf 共享引擎一起做，避免两套实现。
- **识别新元数据**：把 `NullableAttribute` 解码纳入共享引擎的"事实源"接口——即使轨道 2 未落地，也能让 VB 消费 C# 标注库时给出正确诊断。这是 unsafe-evolution 先例（VB 不跟特性但识别元数据）的直接复用。
- **`!` 对应物**：轨道 2 立项时单列"抑制运算符令牌选择"工作项；在候选确定前，`?.` / `DirectCast` 是现成变通。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #4 被 C# 原文强化，无需改动**：C# 8 提案明确"warnings must be optional"与按程序集 opt-in，本会议结论与之一致。附录的价值是把"跟随 C# 的充分理由"写成可引用的原文，供 speclet 用。
- **RESOLUTION #1（只跟踪局部与参数）是 v1 的刻意收窄**，但 C# 的 `tracked_expression` 已证明字段/属性跟踪可行且是 C# 常态——建议把"扩展到字段/属性"列为 v1 后的既定方向，而非永远不做。
- **三态 vs C# 三态 + "maybe default"**：轨道 2 若启用，需决定是否采纳 "maybe default"（类型参数专用）。若 VB 共享引擎状态域固定为 Null/NotNull/MaybeNull，则跨语言泛型方法（`T?`）的 null 状态会被"压扁"，与 C# 诊断不一致。轨道 1 不受影响。
- **新增一条 Implication 建议**：把"解码并消费 `NullableAttribute`"列为轨道 2 的前置工作项，同时允许其在轨道 1 阶段作为"消费 C# 标注 API"的独立增强先行落地。

### 引用纪律与 OPEN QUESTIONS

本附录逐字引用的 C#/VB 原文全部来自上述标注文件（`..\..\csharplang` 与 `..\..\vblang` 镜像），未作转述改写。

- **OPEN QUESTIONS**：
  1. VB 的 null-forgiving 对应物拼写（`!` 令牌冲突已知，2017.08.30；无候选）。
  2. Roslyn VB 编译器当前对 `NullableAttribute`（及程序集级 nullable 标记）的实际消费程度——本附录只核实了 C# 发射侧，未核实 VB 消费侧。
  3. C# "maybe default" 状态是否应进入 VB 共享引擎状态域（仅轨道 2 相关）。
  4. C# NRT 在真实 VB 用户群的接受度数据（本会议第 8 条；C# 侧采用率已明朗，VB 侧仍无数据）。
- **Suspect**：C# 9 规格 `as` 运算符一节的 null 状态规则（nonnullable 目标 → "not null"）与 `as` 运行期失败返回 `null` 的语义存在可疑出入，本附录**故意未采用**该处引文。
