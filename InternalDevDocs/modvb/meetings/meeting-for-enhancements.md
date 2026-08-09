# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次讨论 `For` 增强。我们一进会议室就发现这份建议的尴尬处境：它把三件事捆在一起，而其中两件是主线早在 2017 年就 Approved-in-Principle 的东西，第三件又恰好踩在 VB 自家 spec 里明文记录的不对称上。所以这场会的真正工作不是"要不要做"，而是"拆不拆、拆了怎么各自定边界"。

## Agenda

* [Proposal: `For` 增强（多计数器 / `Exit For`・`Continue For` 显式变量 / 按迭代捕获修复）](#proposal-for-增强)

## Proposal: `For` 增强

_Related: [vblang #48 – Multiple `For` or `For Each` Control Variables Per Statement](https://github.com/dotnet/vblang/issues/48)；[vblang #186 – `Exit For j` Statement to Break Out of Nested `For` and `For Each` Loops](https://github.com/dotnet/vblang/issues/186)；[vblang #104 – Extend `For Each` Statement with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)；ModVB：`proposal-for-each-enhancements.md`（共享 `Exit For`/`Continue For` 显式变量语法面）；vblang spec `statements.md` §For...Next Statements / §For Each...Next Statements / §Branch Statements_

### 场景与缺口

We opened with three observations, and immediately noted they are **three different problems** with three different risk profiles sharing one statement surface:

1. **多计数器**：遍历三维数组要写三层缩进的 `For`，视觉笨重。Anthony 原文章节 3.4 `For` 给出扁平写法：

```vb
' Multiple nested counter variables and ranges.
For z = 0 To maxDepth,
    y = 0 To maxHeight,
    x = 0 To maxWidth

    ....

Next x, y, z
```

2. **`Exit For` / `Continue For` 显式变量**：嵌套循环里想退出/继续指定层，今天只能退最内层，或求助于 `GoTo`——而主线会议原文就承认「`Goto` is a code-smell and people want a less smelly solution to this problem」。

```vb
Continue For y   ' 跳过并继续"名为 y"的那一层循环的下一轮

Exit For z       ' 退出"名为 z"的那一层循环
```

3. **按迭代捕获修复**：`For`（数值型）的迭代变量被 lambda 捕获时，警告 BC42324 与循环结束后的 `IndexOutOfRangeException`。而这里有一个**spec 明文记录的不对称**：`For Each` 在 VB 11.0 已改为按迭代复制变量，`For` 却没有。We were immediately struck by the fact that the proposal did not cite either the mainline approvals (#48/#186) or VB's own spec note — both of which are the strongest evidence in the room.

### 候选方案

因为三件事风险/价值差异很大，我们把"是否捆绑"本身作为一个方案来比，再给每件各自的备选。

**元问题 —— 捆绑还是拆分：**

- **PROPOSAL A — 按建议原样整体推进。** 三项绑在同一份提案、同一个状态机、同一轮评审。
- **PROPOSAL B — 拆成三份独立提案**：B1 多计数器 `For`；B2 `Exit For z` / `Continue For y` 显式变量；B3 `For` 迭代变量按迭代捕获修复。
- **PROPOSAL C — 只取主线已 Approved-in-Principle 的两项**（B1+B2），把 B3（破坏性变更）表掉。

**B1 多计数器 `For` 的语法备选：**

- **B1a — 逗号表头 `For z = ..., y = ..., x = ...`（建议原文）。** 纯语法糖，desugar 为嵌套循环。
- **B1b — 复用 Range（#25）+ `For Each`。** `For Each x In 1 To maxWidth` 式。主线 #25（Range `1 To 10 Step 2` Expressions）当时判为 "Speclet needed"，设计方向是从 `For` 往下长。但多计数器是"一条语句多个并行范围"，Range 表达的是"单条范围"，形态不同。
- **B1c — 维持嵌套现状（什么都不做）。** 能力上等价，只损失可读性。

**B2 `Exit For z` / `Continue For y` 的语法备选：**

- **B2a — 显式变量名定位（建议原文，主线 #186 的形态）。**
- **B2b — Java 式标签循环（`Exit For outer`）。** 主线讨论过，未否决也未采纳。
- **B2c — 逗号列表 `Exit For x, y` / `Continue For x, y`。** 主线讨论过：对 `Exit` 而言"Seems reasonable"，对 `Continue` 则被明确拒绝。
- **B2d — 维持现状（`GoTo` + 标签，或布尔标志 + 每层 `Exit For`）。**

**B3 捕获修复的语义备选：**

- **B3a — 每轮迭代生成新的捕获副本（建议原文）。** 细节未定：是"只给闭包做快照"还是"C# 5 式每轮新存储"。
- **B3b — 保持共享变量语义，仅抑制/移除警告 BC42324。** 治标不治本。
- **B3c — Analyzer/IDE 修复建议**：提示在循环内声明局部变量再捕获（当前规避手段）。编译器自动做更友好。
- **B3d — 现状。**

### 权衡：Q&A

**A vs B vs C：该不该捆绑？** 不该。三件里两件是纯增量语法糖（B1、B2），一件是破坏性行为变更（B3）。把它们绑在同一个"Proposed"状态行下，意味着 B3 的破坏风险会拖住 B1/B2 的落地，而 B1/B2 的"纯糖"光环又会掩盖 B3 的兼容性代价。评价框架把"一份提案混杂多个独立特性"列为红旗，We agree。**结论：B。**

**B1a vs B1b vs B1c：多计数器值不值得新语法？** 主线 #48 已经替我们回答过一次。We quoted the 2017 meeting verbatim: 「None of us could think of a good reason why this doesn't already work. Allowing the `For Each` case is virtually required by #104.」并且 #48 当时的理由与 VB 基因高度一致：`Imports`、`Dim`、`Next`、`From`、`Let`、`Case` 都能逗号并列，`For` 是"同一类东西能逗号合并"这一长串先例里缺失的一块。B1b（Range 复用）不能覆盖多计数器形态——那是单范围。B1c 是现状，能力等价，但"扁平比嵌套易读"是真实的 DX 诉求。**结论：B1a，且必须连同 `For Each` 一起做**——#48 的批准本来就覆盖 `For` 与 `For Each` 两者，Anthony 原文 3.5 `For Each` 一节也写明了同一语法面。

**B2a vs B2b vs B2c vs B2d：变量名还是标签还是列表？** 这里主线 #186 的权衡几乎可以直接搬进来：

- **关于标签（B2b）**：主线原话是「Maybe, but doing the proposal doesn't preclude us doing what Java does later. And if your control variables are well named, e.g. `row` and `column` then requiring the programmer to label the entire loop might very well be redundant and people would complain about that.」我们同意：变量名通常比标签更短、更贴近数据；标签循环留给未来。
- **关于逗号列表（B2c）**：主线对 `Exit For x, y` 说"Seems reasonable"，但对 `Continue For x, y` 说了 **No**：「One isn't continuing the inner loop _then_ continuing the outer one; one is simply skipping to the next iteration of the outer loop.」We think this is exactly right and worth enforcing as a hard rule：`Exit For` 可以带列表（一次退出多层），`Continue For` 只能带**单个**变量。
- **关于可读性疑虑**：主线会议也诚实记录过一个反方观点——「going from a likely more descriptive label name to a likely brief control variable name this could actually be a less readable construct in many cases」，且编译器自己的扫描器/解析器大量使用 `GoTo` 并不觉得羞愧。但我们认为这是对"变量名是否起得好"的担忧，不是对机制本身的否决；而且 VB 已有 `Exit Do` / `Exit While` / `Exit Select` 指定块、`Next` 多变量闭合多层的双重先例。**结论：B2a，`Exit For` 允许列表、`Continue For` 仅单变量。**

**B3a vs B3b vs B3c vs B3d：捕获修复怎么做？** 这是全场争得最久的一项。先摆事实——spec `statements.md` §For...Next 有一句我们逐字引用：

> "Note that a new copy of the loop control variable is *not* created on each iteration of the loop block. In this respect, the `For` statement differs from `For Each`."

而 §For Each...Next 的 Note 记录 `For Each` 已在 VB 11.0 修掉：

> "Up to Visual Basic 10.0, this produced a warning at compile-time and printed "3" three times. … As of Visual Basic 11.0, it prints "1, 2, 3". That is because each lambda captures a different variable "x"."

所以 B3 不是新发明，是**补完 VB 自家 11.0 修了一半的事**。B3b（只删警告）等于把一个已知 footgun 的报警器拆掉——不可接受。B3c（analyzer 建议手动局部变量）是现状的文档化，编译器自动做更友好。**方向选定 B3a。** 但 B3a 里还藏着一个大坑，见下方追问 #2 与 #5：建议原文写「与 C# 的 `for` 循环变量捕获语义一致」，这句话对 VB 的 `For` 而言**不成立或至少远未成立**，因为 VB 的计数器可以预存在、循环后的终值可被依赖、循环体可以修改计数器、计数器可以被 ByRef 传参——这些 C# `for` 全都没有。B3a 必须先选定语义模型。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

- **多计数器**：今天 `ForStatement` 的 grammar 是 `'For' LoopControlVariable Equals Expression 'To' Expression ( 'Step' Expression )?`，头部没有逗号。新 grammar 为 `LoopControlVariable Equals Expression 'To' Expression ( 'Step' Expression )? ( Comma LoopControlVariable Equals Expression 'To' Expression ( 'Step' Expression )? )*`。因为今天 `For` 头部不允许逗号，新引入不造成歧义。但要决定**多计数器是否也延伸到 `For Each`**——`For Each x, y In coll`（元组解构，Anthony 3.5）与 `For a = 1 To 2, b = 1 To 2` 形状不同，parser 可凭是否有 `In` 区分，不歧义，但必须写进同一份 grammar 讨论。
- **`Exit For z` / `Continue For y`**：spec 的 `ExitKind : 'Do' | 'For' | 'While' | 'Select' | 'Sub' | 'Function' | 'Property' | 'Try'` 与 `ContinueKind : 'Do' | 'For' | 'While'` 今天都不带操作数。改为 `'Exit' 'For' IdentifierList?`、`'Continue' 'For' Identifier?`。无歧义，但 `Exit For`（无变量）必须保持"最内层"语义，否则破坏现有代码。
- **`Next x, y, z`**：spec 已经允许 `NextExpressionList : Expression ( Comma Expression )*`（我们读到了），语法零改动——这条先例是 B1a 的最硬支撑。

#### 2. 角案例 / 边界语义

**多计数器的 desugar 顺序。** 建议原文的未决问题 Q1 正中要害。若 B1a 是纯语法糖，desugar 成：

```vb
For z = 0 To maxDepth
    For y = 0 To maxHeight
        For x = 0 To maxWidth
            ...
        Next x
    Next y
Next z
```

那么所有语义自动继承嵌套：`Next x, y, z` 的执行顺序是"内层先增，x 耗尽则 y 增并**重新初始化 x**，y 耗尽则 z 增并重新初始化 y 与 x"。**关键细节**：嵌套语义里 `For x = 0 To maxWidth` 每次 y 迭代都会重新求值上界 `maxWidth`（spec：三个表达式只在循环开始时求值——每个循环各求一次）。纯 desugar 会让扁平写法的上界求值次数与嵌套完全一致（外层每轮重新求值内层上界），这可能与直觉相悖，但至少可预测、可文档化。若我们想"全部上界只求一次"，那就是**非 desugar** 的新运行时语义——We strongly prefer the former（"Treat it as an optimization, not a feature" 的姊妹句：**treat it as sugar, not a new runtime**）。

**`Exit For` / `Continue For` 无变量时。** 保持"最内层"。`Exit For`（无变量）在扁平多计数器体内 = 退出 x 层；`Continue For`（无变量）= 继续 x 层。与嵌套 desugar 完全一致。

**多计数器体内 `Exit For z` / `Continue For y`。** 与 B2 组合：`Continue For y` 跳过剩余体，直接到 y 层的 `Next`——即 y 增、**x 重新初始化**再进入下一轮。这在嵌套 desugar 下也自动成立（`Continue For y` 是作用于 y 循环的既有 #186 特性）。两个特性天然正交、共享一份实现面。

**预存在计数器 + 循环后终值。** 这是 B3 最尖锐的角案例。VB 允许 `Dim i` 后 `For i = ...`，且大量代码依赖循环结束后的 `i`（未命中时等于终值，命中时等于命中索引）：

```vb
Dim i As Integer
For i = 0 To arr.Length - 1
    If arr(i) = target Then Exit For
Next
' 依赖 i 的终值：arr.Length 表示未找到；否则是命中索引。
```

若 B3a 采用"每轮新存储"，必须规定**循环结束时把当前值写回预存在的 `i`**。这要求写回时机的精确 spec（正常终止、`Exit For`、`Continue For`、异常？——异常不应写回，因为变量在异常路径上本就该保持异常前值，但"哪一刻的值"要定）。

**循环体修改计数器。** VB 允许 `For i = 0 To 100 : If cond Then i += 10 : Next` 跳过一段。这依赖"体内修改作用于下一轮"——如果 B3a 采用 For Each 式"整块体引用新副本"，这个惯用法会碎掉。C# 5 的修法是**每轮新存储但值流经**（体内修改仍影响本轮步进与下一轮初值）。We think VB 必须复刻后者，绝不能把 `For` 变成 `For Each` 那种"迭代变量每轮从源重取"的模型——数值 `For` 的计数器就是循环状态的载体。

**`ByRef` 传计数器。** `For i = 0 To 10 : TryAdvance(i) : Next`——被调方改写计数器。若每轮新存储，被调方改写的是本轮副本，写回与否要定。`Probably`：跟随"体内修改作用于本轮存储，步进读取本轮存储"的模型，ByRef 修改自然作用于本轮副本并被步进消费——但**预存在计数器在循环中被 ByRef 修改后，循环外是否可见**必须与写回规则一致。这个点建议原文完全没提。

**`Step` 负值与捕获。** 负步进不影响捕获语义（捕获的是每轮入口值），但要写清"每轮入口值"的定义（步进前？测试后？）。`Probably`：定义为"本轮体执行开始时的计数器值"，与 For Each 的"Current 赋值后"对齐。

#### 3. 作用域与绑定

- **多计数器作用域**：扁平形式下 z、y、x 同时在整个体内可见——与嵌套形式下"z 对一切可见、y 对 z 内可见、x 对最内可见"一致（三者都在体内可见）。spec §For...Next 有句现成约束：「A loop control variable cannot be used by another enclosing `For...Next` statement.」扁平形式把它变成"同一语句内不得重名"，自然延续。
- **`Exit For z` 的绑定**：`z` 应绑定到**围困此语句的、控制变量名为 `z` 的 `For` 语句**，而不是普通变量引用。语义模型里 `Exit For z` 的 `z` 返回什么符号？We think：返回该 `For` 语句的计数器符号（便于 IDE 跳转/重命名），但其**绑定身份**是"循环定位"，不参与值读取。若体内 `Dim z` 遮蔽（如 `For i = ... : Dim z = 1 : Exit For z`），必须报错，否则无法解析。建议原文未决问题 Q2 的"同层同名"其实被 spec 1299 消解了一大半——但**预存在计数器 + 循环内局部遮蔽**的交互要补规则。

#### 4. 与既有特性的交互

- **Lambda / 闭包捕获**：B3 的正题。B1/B2 不影响。
- **Async**：`For` 体内 `Await`。每轮新存储在异步重入时自然存活（提升为闭包字段），语义干净。
- **`On Error GoTo` / 非结构化异常处理**：`Exit For` 在 `Resume`/`Resume Next` 语境下的定位不变；多计数器 desugar 不引入新的运行时块，`Resume` 语义不受影响（`Probably`，需验证）。
- **LINQ 查询**：`For` 计数器不参与查询，无交互。
- **`For Each` 的并行面**：B1/B2 必须与 `proposal-for-each-enhancements.md` 共享 `Exit For`/`Continue For` 显式变量的规范与实现，否则会造出两套"显式变量"语义。

#### 5. Breaking change 与兼容性

- **B1/B2：纯增量。** 新语法不改变任何既有代码的行为。`Exit For`（无变量）语义不变。**零破坏。**
- **B3：破坏性。** spec 明文记录 `For` 今天**不**按迭代复制。改成按迭代捕获，则"共享变量捕获"的既有代码（正是 BC42324 警告的对象）行为改变：lambda 看到的从"终值"变成"各自轮次值"。这是**行为变更**，不只是警告移除。但它有一个极硬的先例：`For Each` 在 VB 11.0 就这么干过（spec 记录了从 "3 3 3" 到 "1 2 3" 的变化），当时没有 langversion 门控，直接改 + 移除警告。We think `For` 应复刻这个先例：直接改、移除警告、在迁移说明中记录。`Probably`：对预存在计数器与循环后终值的保护（写回）可以进一步减小可观察破坏面，但**绝不能承诺零破坏**。
- **编译时间/符号**：B3 每轮新存储需要额外的闭包局部变量，编译产物变大但常规；不触达 IL 合法性问题。

#### 6. Option Strict / 编译选项分叉

- 多计数器每计数器的类型**独立**推断（各取自身 bounds/step 的 widest type，spec §For...Next 的类型规则逐计数器适用）。`Object` 计数器（`Option Strict Off`）各自在运行时推断，与今天单计数器一致。
- 捕获修复与 Option Strict 无关（两条路径同改）。
- `Option Explicit` / 隐式局部：`For i = ...` 在无 `Dim` 时走"隐式局部声明"路径（method-scope）。多计数器每个 `i`、`y`、`z` 都走同一规则；`Exit For z` 的绑定在"隐式局部"下也能找到该计数器。**两条路径行为必须一致。**

#### 7. IDE / IntelliSense

- `Next x, y, z` 补全：语法已存在，Roslyn 模型已支持多变量 `Next`——零新工作。
- 多计数器 `For` 头部的多行排版与缩进（建议原例把 `y =`、`x =` 换行缩进），IDE 的格式器要认识这种 continuation。
- `Exit For`/`Continue For` 后输入时，补全应建议**围困 For 的控制变量名**（含预存在计数器），对不围困的变量名不提示。IDE 语义：`Exit For z` 的 `z` 上 InfoTip 显示"循环 z"而非变量值。
- B3 后 lambda 内 `i` 的 Go to definition 指向"每轮副本"——符号显示策略要设计（指向 `For` 语句更诚实）。

#### 8. 数据 / 普遍性

- **B2（Exit/Continue 显式变量）**：最强。嵌套循环退出/继续是高频需求，`GoTo` 的坏名声是真实动机。无量化数据，但 `Probably` 高。
- **B3（捕获修复）**：BC42324 是 VB 著名的警告之一，社区大量手写 `Dim snapshot = i` 规避——普遍性有侧面证据。
- **B1（多计数器）**：最弱。三维以上数组/张量遍历在业务代码里不常见。`Suspect`：这份建议把 B1 放在摘要第一位，但它的数据/普遍性证据恰恰最少。若无数据支撑，B1 应排 B2、B3 之后。

#### 9. 更简替代

- 多计数器：嵌套 `For` 能力等价，只是不扁平；Range（#25）+ `For Each` 是另一条更长、更重的路。B1a 作为纯糖是最简的。
- Exit/Continue：`GoTo` 臭名昭著；布尔标志 + 逐层 `Exit For` 是更啰嗦的等价物。B2a 无更简替代。
- 捕获：手动 `Dim snapshot = i` 是唯一现状替代，编译器自动做严格更优（零样板、不遗忘）。

#### 10. 复杂度 / 成本 / 优先级

- B1：parser + binder + desugar + spec。作为纯糖，运行时零风险。成本中。
- B2：binder 绑定 + spec。名称解析是唯一难处。成本中。
- B3：lowering + spec。**建议原文低估的部分**——"与 C# 一致"掩盖了预存在计数器写回、体内修改模型、ByRef、循环后终值四个 VB 特有约束。成本中高。
- 优先级：B2（价值高、风险低）> B3（价值高、破坏中、需 spec）> B1（价值中、纯糖、证据最弱）。三者共享 `For` 表面，可同工作流但**分 spec 分状态机**。

#### 11. 运行时 / CLR 硬约束

无。B1 是纯 desugar，B2 是控制流定位（编译期），B3 只是多几个闭包局部。无新 IL、不触 PEVerify、不触达存储规则。

#### 12. 值不值得做

- B2：价值高 × 成本中 × 风险低 → **做**。
- B1：价值中 × 成本中 × 风险低（纯糖） → **做**，但需数据与 For Each 扩展。
- B3：价值高（修 footgun、补自家 11.0 半截工程、与 C# 5 对齐）× 成本中高 × 风险破坏（有 For Each 先例对冲）→ **做**，但先补 spec。
- **捆绑整体推进：不值**——风险混杂、状态互相拖累。We were quite firm on this.

### VB 基因对照

- **保持 VB-like（原则 #2）**：B1a 的逗号并列完全落在 VB 基因上——`Imports`、`Dim`、`Next`、`From`、`Let`、`Case` 都是"同类构造可逗号合并"，主线 #48 白纸黑字列过这份先例清单。
- **消除常见样板（原则 #9）**：B1 压扁三层缩进、B3 消灭手写快照，都正中靶心。
- **读起来像英语、对新手友好（原则 #5）**：`For z = 0 To maxDepth, y = 0 To maxHeight, x = 0 To maxWidth` 一个眼神读完。
- **避免隐蔽的控制流/语义变化（原则 #7）**：B2 的 `Exit For z` 是显式控制流，不隐蔽；B3 是隐蔽语义变化的**反方向**——它把隐蔽的共享捕获变成可预期的按轮捕获，但代价是行为变更，必须按 `For Each` VB 11 先例处理并写迁移说明。
- **不引入"第二种做事方式"（原则 #3）**：这是 B1 唯一的扣分点——扁平形式是嵌套形式的第二种写法。但主线已批准，且作为纯糖"第二种写法"的代价被"可读性增益"抵消。We recorded the tension but did not let it block.
- **默认跟随 C#（原则 #4）**：B3 与 C# 5 的 `for` 一致是加分项——**但 VB 的 `For` 比 C# 的 `for` 多出预存在计数器、循环后终值、体内修改、ByRef 四个能力，不能直接抄**。"跟随 C#"在 B3 上要翻译成"跟随 C# 的精神，实现 VB 自己的语义模型"。
- **与主线关系（对照表 2.3）**：该行原文是「多 `For` 变量 / `Exit For j` | Approved-in-Principle | 直接采用 | 一致」——本建议直接采用主线已批准的形态，**与主线一致**。捕获修复是主线未接管、Anthony 独立延伸但方向与 VB 自家 11.0 工程一致。`Let` 替换 `Dim` 是 Anthony 全文独立的方言，本建议示例沿用，与本特性无关，不评审。

### RESOLUTION:

1. **拆分，不捆绑**。本建议把三件不同风险/价值的事捆在一起，整体推进。拆成三份：`for-multi-counter`（B1）、`for-exit-continue-targeting`（B2）、`for-per-iteration-capture`（B3），各自独立状态机。
2. **B2（`Exit For z` / `Continue For y`）：Active。** 采纳主线 #186 的 Approved-in-Principle。`Exit For` 允许逗号列表（一次退出多层），`Continue For` 仅允许**单个**变量——拒绝 `Continue For x, y`，理由照抄主线："one is simply skipping to the next iteration of the outer loop." 无变量形式保持"最内层"语义。标签循环（Java 式）**Table**，不排除未来。
3. **B1（多计数器 `For`）：Active，含 `For Each` 扩展。** 采纳主线 #48 的 Approved-in-Principle。纯语法糖，desugar 为嵌套循环，不自造运行时语义。`For` 与 `For Each` 同时做（#48 明确 `For Each` 由 #104 驱动；Anthony 3.5 原文也覆盖）。`Next x, y, z` 沿用既有 `NextExpressionList` grammar。
4. **B3（按迭代捕获修复）：原则采纳，但 spec 未就绪前不升 Active。** 目标是补完 spec 明文记录的 `For`/`For Each` 不对称（`For Each` 已于 VB 11.0 修复）。语义模型必须为 VB 定制：预存在计数器写回、循环后终值保持、体内修改与步进读同一轮存储、ByRef 交互。**"与 C# 一致"的表述不成立**——那是目标不是方案。破坏性变更按 `For Each` VB 11 先例接受：直接改 + 移除警告 BC42324 + 迁移说明，不设 langversion 门控（`Probably`，待与版本团队确认）。
5. **`Exit For`/`Continue For` 显式变量的规范与实现，与 `proposal-for-each-enhancements.md` 共享**一份 spec、一份实现，两块语法面，避免两套"显式变量"语义。

### Implication:

- 起草 `for-multi-counter` speclet：desugar 规则（`Next` 增量顺序、`Step`、上界求值时机与嵌套一致性、`For Each` 扩展、作用域与同名规则、Option Strict 两路径）。
- 起草 `for-exit-continue-targeting` speclet：`Exit For z`/`Continue For y` 的名称绑定（绑定到围困 For 的控制变量符号；循环内遮蔽报错；预存在计数器；`Exit For` 列表 vs `Continue For` 单变量）。
- 起草 `for-per-iteration-capture` speclet：语义模型（每轮新存储、值流经）、预存在计数器写回时机、循环后终值保证、ByRef 交互、破坏清单与迁移说明。
- 为 B1 补数据：三维+数组遍历在真实代码库的占比；为 B3 收集 BC42324 触发率。
- 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）替换为真实原型分支。
- 与 for-each-enh 团队对表，确定共享规范的文档所有权。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：多计数器 desugar 后，内层上界随外层每轮重新求值（与嵌套一致）——扁平写法用户可能预期"全部上界只求一次"；是否在文档中用"纯糖"表述明确该行为。
- `OPEN QUESTIONS`：`Exit For z` 在循环体内 `Dim z` 遮蔽时的绑定规则（建议：绑定围困 For 的控制变量，遮蔽者报错）。
- `OPEN QUESTIONS`：B3 对 `Step` 负值、`i -= 1` 型修改、`ByRef` 写回与"异常路径不写回"的精确规则。
- `OPEN QUESTIONS`：B3 破坏性变更是否设 `langversion` 门控或按 `For Each` VB 11 直接改——待版本团队裁定。
- `TODO`：量化 B1/B3 的普遍性数据。
- `Follow-up`：与 for-each-enh 共享规范后，`Continue For child`/`Exit For parent`（For Each 侧）与本文 B2 的绑定规则必须逐字一致。

### 状态

- **LDM 状态：拆分中。** B2 → Active；B1 → Active（含 `For Each`）；B3 → 原则采纳，spec 就绪后 Active。
- **三态判定：整体 Consider**（捆绑状态不可直接推进）；拆分后 B1/B2 **Active**、B3 **Consider→Active（spec 补全后）**。B1 若拿不到普遍性数据，降为 Consider。

---

## 附录：特性评价

# 建议评价报告：proposal-for-enhancements.md

## 评价对象

- 建议：proposal-for-enhancements.md — `For` 增强（多计数器 / `Exit For`・`Continue For` 显式变量 / 按迭代捕获修复）
- 来源：Anthony 原文第 3.4 节 `For`（`..\AnthonyDesign_wordpress.txt` L885–923；多计数器、`Continue For y`/`Exit For z`、捕获修复示例逐字来自该节）；捕获修复自称"与 C# 的 `for` 循环变量捕获语义一致"
- 配方目标：压平多层嵌套 `For` 样板、支持 `Exit For`/`Continue For` 显式指定层、修复 `For` 迭代变量的 lambda 按迭代捕获问题（警告 BC42324 + `IndexOutOfRangeException`）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。三项目标清晰、示例可操作，但：捕获修复的关键子效果（预存在计数器写回、循环后终值、体内修改、ByRef）未定义，示例止步于"理想情形"；多计数器的 `Next` 增量顺序留给未决问题，主效果边界未闭合 | 已检查 | 无原型封顶 3；"与 C# 一致"掩盖 VB 特有语义缺口；B1 普遍性证据最弱却放摘要第一位 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"。逗号并列是 VB 基因（`Imports`/`Dim`/`Next`/`From`/`Let`/`Case` 先例）；捕获修复是补 VB 自家 11.0 半截工程、非外来。轻微扣分：`For` 头部逗号是全新形状 | 已检查 | 三项能力捆绑在同一"特性"下，边界模糊；`Let` 方言沿用未注明 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题具体（健康区间），但**缺 Compatibility/breaking-change 章节**（B3 是破坏性变更却只字未提兼容策略）；状态行占位链接；捕获修复语义模型未展开；与 for-each-enh 的重叠未提 | 已检查 | 无兼容分析是硬伤；Drawbacks 三行浅尝辄止；Alternative 与主线 #48/#186 零交互 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/光受益（提速样板、盘活 `For` 惯用法）；暗风险（B3 破坏、名称解析歧义）存在但文档未识别；风（与主线 #48/#186 的 Approved-in-Principle 关系）完全未提，演化一致性论证缺失 | 已检查（预测待定） | 三项捆绑使"整体风险"被最弱项拖累；破坏性变更无应对设计 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。Anthony 3.4 来源准确（示例逐字）；"借鉴 C#"有标注但**不准确**（VB `For` 与 C# `for` 结构不同，直接抄不成立）；**主线 #48/#186 已 Approved-in-Principle 这一最硬的来源未引用**；VB 11.0 `For Each` 先例未引用 | 已检查 | 忽略主线先例 = 材料成分标注不全；"C# 一致"标签与 VB 实际语义有偏差 |

## 设计原则对照

- **与 VB 基因：主体一致**——逗号并列（#2/#3 家族先例）、消除样板（#9）、可读（#5）。唯一张力在原则 #7（隐蔽语义变化）：B3 是行为变更，但方向是"把隐蔽变可预期"，且有 `For Each` VB 11 先例。原则 #3（不引入第二种做事方式）对 B1 有轻微张力，主线已批准作为抵消。
- **与主线关系：主线一致**——B1/B2 直接采用主线 #48/#186 的 Approved-in-Principle 形态（对照表 2.3 该行：「多 `For` 变量 / `Exit For j` | Approved-in-Principle | 直接采用 | 一致」）。B3 是主线未接管、Anthony 独立延伸但与 VB 自家 11.0 工程方向一致。与 `proposal-for-each-enhancements.md` 共享语法面，需一份规范。
- **破坏性变更：有**——B3 改变 `For` 迭代变量的 lambda 捕获语义（spec §For...Next 明文记录今天不按迭代复制）。建议文档未分析，更未给出 `For Each` VB 11 先例作为对冲。B1/B2 纯增量，零破坏。

## 总评

- **达成程度：部分达成**——B1/B2 概念与主线批准完全对上，价值真实；B3 方向正确但语义模型未定型、兼容性未分析；整体被"捆绑 + 无兼容章节 + 未引用主线"拖累。
- **LDM 三态建议：拆分后**——B2（Exit/Continue 显式变量）**Active**；B1（多计数器 `For`，含 `For Each`）**Active**；B3（按迭代捕获）**Consider→Active（spec 补全后）**。整体按现状：**Consider**。
- **主要问题**：① 三项捆绑，风险混杂；② B3 破坏性变更无兼容分析、语义模型照抄 C# 不成立（预存在计数器/循环后终值/体内修改/ByRef 未处理）；③ 未引用主线 #48/#186 的 Approved-in-Principle 与 VB 11.0 `For Each` 先例；④ 与 for-each-enh 的重叠未识别。

## 返工建议

- **补充章节**：Compatibility / breaking-change（B3 按 `For Each` VB 11 先例的策略、`langversion` 门控裁定、迁移说明）；BNF 或 spec 章节修改（`ForStatement` grammar 扩展、`ExitKind`/`ContinueKind` 带操作数）。
- **补充证据**：拆分后的三份提案各自独立状态机；B1 的三维遍历占比数据；B3 的 BC42324 触发率；状态行真实原型链接。
- **未决问题处理**：`Next x, y, z` 增量顺序 = 明确"纯糖 desugar，语义与嵌套一致"；同名计数器解析 = 借 spec 1299（围困 For 不得重名）+ 遮蔽报错；`Step`/`To` 组合 = 每计数器独立类型推断与独立校验。
- **设计探索**：B3 语义模型选型（快照 vs C# 式每轮新存储）及预存在计数器写回；`Exit For` 列表 vs `Continue For` 单变量的 grammar 与语义（照抄主线 #186 讨论）；与 for-each-enh 共享 `Exit For`/`Continue For` 显式变量规范后的绑定规则逐字对齐表。

---

## 附录：C# 生态与互操作考量

> 本附录把本次「`For` 增强」提案放到 dotnet/csharplang 现实走向下对照。资料来源：`..\..\csharplang`（官方仓库镜像，main 分支）的浓缩索引（`..\..\csharplang-index.md`）与原文核实。逐字引用均标注来源文件路径；无法在本库核实处标 **OPEN QUESTIONS**。

### 相关 C# 现实方向

**C# 的 `for` / `foreach` 现状。** C# 的 `for` 计数器是**单变量、整条循环共享**——初值只在进入循环时求值一次，此后所有迭代、所有 lambda 捕获都指向同一个变量。这是 C# 社区著名的闭包 footgun；C# 5 只修了 `foreach`，`for` 至今未修。本库两条原文互为印证：

> "In fact we took a slight breaking change in C# 5.0 in order to make that the case for the loop variable in `foreach`, because the alternative didn't make sense to people, and tripped them up when they were capturing the variable in lambdas, etc."

→ `meetings\2013\LDM-2013-12-16.md`

> "The only exception is if it occurs in the initializer of a for loop, because that part is evaluated only on entry to the loop."

→ `meetings\2013\LDM-2013-12-16.md`（同一段：`foreach` 改了，`for` 初值仍是"进入时求值一次"的单变量）

> "foreach loop was changed to generates a new loop variable rather than closing over the same variable every time"

→ `Language-Version-History.md`（C# 5 一节）

C# 对循环的低层投入方向是 **ref 化**：C# 7.3 加 *ref for loops* / *ref foreach loops*，方向是"按引用访问元素"，与本提案的"按迭代捕获"无关：

> "In C# 7.3, we added support for *ref for loops* and *ref foreach loops*."

→ `proposals\csharp-7.3\ref-loops.md`

**Range/Index（C# 8）——切片，不是迭代。** C# 8 的 `^`/`..` 只用于**索引与切片**，不用于数值迭代：

> "This feature is about delivering two new operators that allow constructing `System.Index` and `System.Range` objects, and using them to index/slice collections at runtime."

→ `proposals\csharp-8.0\ranges.md`（Summary）

早期 LDM 确实讨论过 `foreach` 遍历 Range，且留下了逐字记录：
- `meetings\2018\LDM-2018-01-18.md`「Enumerability」："`Range` should implement `IEnumerable<T>`, and needs to support the enumerable pattern of having a struct enumerator type."；语言问答里 "And support for `foreach` naturally falls out: `foreach (var x in 3..5) { ... }`."
- `meetings\2018\LDM-2018-01-22.md`："In foreach loops over ranges, if you use constant end points, again intuition seems to suggest inclusiveness:"（后接 `foreach (var x in 1..100) { ... }` 示例）

但最终落地的 feature 范围锁定在 index/slice，`Range` 并未成为可 `foreach` 的迭代源。C# 9 的 extension `GetEnumerator` 提案 Drawbacks 明确把它列为"不该被 foreach"的反例：

> "Every change adds additional complexity to the language, and this potentially allows things that weren't designed to be `foreach`ed to be `foreach`ed, like `Range`."

→ `proposals\csharp-9.0\extension-getenumerator.md`（Drawbacks）

**C# 生态的"数值迭代"答案。** 由于 Range 不可枚举、`foreach (var i in 0..10)` 不成立，.NET 世界里"从 0 数到 N"的惯用法是 `for (int i = 0; i < N; i++)` 或 LINQ 的 `Enumerable.Range(...)`。C# 另给了"内联固定集合遍历"（集合表达式直接进 `foreach`）：

```cs
foreach (bool b in [true, false]) { ... }
```

→ `proposals\immediately-enumerated-collection-expressions.md`（`foreach (var item in [1, 2, 3])` 语义降级，编译器可 "inlining" 枚举；注意该提案**未归档进 `proposals\csharp-13.0\`**，是否随某版本 ship 见 OPEN QUESTIONS）

**多计数器。** C# 的 `for` 语法本就允许一条语句内声明多个计数器（共享一个条件、各自步进），例如 `for (int i = 0, j = n - 1; i < j; i++, j--)`。这是"同一条循环、同生共死"的多计数器；与 VB B1 的"多条独立范围、desugar 成嵌套循环"是两种不同的语义形状。（C# for-statement 逐字 grammar 在 dotnet/csharpstandard §12.9.4，本库 `spec\statements.md` 只有链接索引，未逐字核实。）

**扩展 `GetEnumerator`（C# 9 / C# 14 extensions）。** C# 9 起 `foreach` 可识别扩展方法形式的 `GetEnumerator`；C# 14 extensions 进一步把 `GetEnumerator`/`GetAsyncEnumerator` 列为参与 `foreach` 的模式成员，而 `MoveNext`/`Current`/`Dispose` 不参与：

> "Allow `foreach` loops to recognize an extension method `GetEnumerator` method that otherwise satisfies the foreach pattern, and loop over the expression when it would otherwise be an error."

→ `proposals\csharp-9.0\extension-getenumerator.md`（Summary）

> "This includes: `GetEnumerator`/`GetAsyncEnumerator` in `foreach`"　／　"This excludes: `MoveNext`/`MoveNextAsync` in `foreach`"

→ `proposals\csharp-14.0\extensions.md`

### 现实 vs 提案

**B1 多计数器 `For`——脱节 + 需桥接（轻），兼容。** C# 没有"多条并行范围"的语言形式；C# 的多计数器是共享条件的单循环。两者能力不重叠，B1 作为纯糖不依赖任何 C# 侧配合。真正的桥接点在**语义命名**：VB 主线 #25 的 "Range 表达式"（`1 To 10 Step 2`）是**迭代范围**，而 C# 8 的 `System.Range`（`1..10`）是**切片范围**——同名异物。VBScript.NET 在文档与 IDE 里必须明确区分这两个 "Range"，否则消费 .NET 集合时会把切片当迭代。

**B2 `Exit For z` / `Continue For y`——脱节（VB 特色区），无冲突。** C# 只有最内层 `break`/`continue`，无带标签的 break/continue（Java 式标签循环在 C# 不存在，`goto` 是唯一 escape hatch，与 VB 现状同病）。B2 是纯编译期控制流定位，不产生 IL 差异，无桥接需求。与 C# 的唯一交集是动机：C# 社区同样抱怨深层退出要靠 `goto`，但 C# LDT 至今没有动作——VB 可在此处领先。

**B3 按迭代捕获修复——方向兼容，但"与 C# 一致"的参照物不存在。** 会议正文已判「"与 C# 一致"不成立」；本附录从 C# 生态侧给出更硬的证据：**C# 自己的 `for` 计数器至今仍是共享捕获**（C# 5 只修了 `foreach`，见上节引文）。也就是说，B3 想做的"按迭代捕获"在 C# 里连 `for` 都没做——VB 若做成，将**领先于 C#**，而不是"与 C# 一致"。这从另一面支持会议结论：B3 没有现成模型可抄，必须自建语义模型。附带观察：C# 对 `for` 共享捕获**无警告**（静默 footgun），VB 有 BC42324 警告——VB 在诊断面上比 C# 诚实，修掉后更应保留一条可观测的迁移路径。

### 对 VBScript.NET 的适应建议

- **认识 `System.Range`/`System.Index` 元数据。** .NET 生态对 `^`/`..` 采用极广（现代 C# 库普遍 `arr[1..]`、`list[^1]`）。VBScript.NET 的 `For`/`For Each` 与数组/切片索引在消费 .NET 集合时，必须能识别 Range/Index 及 C# 8 的 Countable/`Slice` 模式（`proposals\csharp-8.0\ranges.md`），否则 `arr(1..^1)` 类调用无法映射。B1 的扁平多计数器与 C# 的 `..` 是**互补**而非竞争：前者管"数到 N"，后者管"切一段"。
- **`For Each` 参与扩展 `GetEnumerator` 查找。** C# 9（及 C# 14 extensions）允许 `GetEnumerator` 由扩展方法提供；VB `For Each` 的 binder 必须参与同样的扩展方法查找，否则无法遍历"只能靠扩展枚举器"的 C# 类型。这一点与 `proposal-for-each-enhancements.md` 共享实现面时一并考虑。
- **编译期纯糖，AOT 友好。** B1 纯 desugar、B2 控制流定位、B3 闭包局部——全部无运行时反射，与 C# 主线的 NativeAOT/trimming 方向天然兼容（ranges.md 的 IL 表示原文 "These two operators will be lowered to regular indexer/method calls" 是同一哲学）。若 .vbx 走 interpreted 脚本模式，这些特性的降级规则必须与编译模式**逐字一致**（脚本层也要有按迭代捕获）。
- **默认安全。** B3 修掉共享捕获 footgun，是把"隐蔽的闭包陷阱"变成"可预期的按轮捕获"，与 C# "把信息放回类型系统/编译期"（索引 T5/T6）同向；不引入 dynamic/反射，不增加 AOT 摩擦。

### 对既有 RESOLUTION / 三态判定的影响

- 不改变三态判定（B2/B1 Active、B3 原则采纳）。本附录为 **RESOLUTION #4**（"「与 C# 一致」的表述不成立——那是目标不是方案"）补充生态侧证据：C# 5 只修 `foreach`、C# `for` 至今共享捕获，所以该声称连"参照物"都不存在。
- RESOLUTION #3（B1 纯糖 desugar、不造运行时）与 C# 处理 `^`/`..` 的方式一致：ranges.md Alternatives 原文 "The new operators (`^` and `..`) are syntactic sugar." 可作旁证。
- B2 与 for-each-enh 共享的绑定规则不受 C# 影响，维持现状。

### 引用纪律与未核实点

**逐字引用清单（均已核实，源文件为相对 `..\..\csharplang` 的路径）：**

1. "In fact we took a slight breaking change in C# 5.0 … capturing the variable in lambdas, etc." → `meetings\2013\LDM-2013-12-16.md`
2. "The only exception is if it occurs in the initializer of a for loop, because that part is evaluated only on entry to the loop." → `meetings\2013\LDM-2013-12-16.md`
3. "foreach loop was changed to generates a new loop variable rather than closing over the same variable every time" → `Language-Version-History.md`
4. "This feature is about delivering two new operators that allow constructing `System.Index` and `System.Range` objects, and using them to index/slice collections at runtime." → `proposals\csharp-8.0\ranges.md`
5. "The new operators (`^` and `..`) are syntactic sugar." → `proposals\csharp-8.0\ranges.md`（Alternatives）
6. "Every change adds additional complexity to the language, and this potentially allows things that weren't designed to be `foreach`ed to be `foreach`ed, like `Range`." → `proposals\csharp-9.0\extension-getenumerator.md`
7. "Allow `foreach` loops to recognize an extension method `GetEnumerator` method that otherwise satisfies the foreach pattern, and loop over the expression when it would otherwise be an error." → `proposals\csharp-9.0\extension-getenumerator.md`
8. "This includes: `GetEnumerator`/`GetAsyncEnumerator` in `foreach`" ／ "This excludes: `MoveNext`/`MoveNextAsync` in `foreach`" → `proposals\csharp-14.0\extensions.md`
9. "In C# 7.3, we added support for *ref for loops* and *ref foreach loops*." → `proposals\csharp-7.3\ref-loops.md`
10. "`Range` should implement `IEnumerable<T>`, and needs to support the enumerable pattern of having a struct enumerator type." → `meetings\2018\LDM-2018-01-18.md`
11. "In foreach loops over ranges, if you use constant end points, again intuition seems to suggest inclusiveness:" → `meetings\2018\LDM-2018-01-22.md`

**OPEN QUESTIONS / Suspect：**

- `System.Range` 运行时成员表（是否含 `GetEnumerator` 实例方法）属 dotnet/runtime，本库无正文；本文据 extension-getenumerator Drawbacks 推其"无枚举器"，严格核实需查 dotnet/runtime。
- C# `for` 语句多计数器 grammar 的逐字规范正文在 dotnet/csharpstandard §12.9.4，本库 `spec\statements.md` 只有链接索引。
- C# 是否在追踪 `for` 共享捕获的修复 issue（dotnet/csharplang / roslyn 议题）——issues 不在本镜像内，未核实。
- `proposals\immediately-enumerated-collection-expressions.md` 存在但**未归档进 `proposals\csharp-13.0\`**，Language-Version-History 也未单独列出——是否已随某版本 ship 为 **Suspect**，以 release notes 为准。
