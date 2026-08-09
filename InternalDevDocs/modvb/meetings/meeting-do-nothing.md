# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周议程只有一项：`Do Nothing` 语句——一个声称"不是玩笑"的提议。我们把它放在空安全（null-coalescing / null-safe-behaviors / null-equality）同一批里审，因为它出自原文第 12 章 "Null and Nothing" 的同一段摘录；但正如我们即将看到的，"与空安全同批"更多是摘录的巧合，而不是语义上的亲戚。

## Agenda

* [Proposal: Do Nothing 语句](#proposal-do-nothing-语句)

## Proposal: Do Nothing 语句

_Related: [vblang #337 – Pattern Matching（"Probably add a discard identifier and a discard pattern"）](https://github.com/dotnet/vblang/issues/337)；主线 2014-02-17 会议（`Nothing`/`Null` 字面量讨论、"We're proud not to do anything"）；ModVB 同批建议：`proposal-null-coalescing.md`、`proposal-null-safe-behaviors.md`、`proposal-null-equality-operators.md`_

### 场景与缺口

建议原文想解决的问题是：某些语法位置"要求至少一条语句"，但实现时可能暂时留白——特性（Attribute）构造器、空分支、调试期占位。今天的做法是写无意义的 `Console.WriteLine`，或 `If True Then End If` 之类的拐弯写法。于是提议引入一条显式的空操作语句：

```vb
' `Do Nothing` statement (not a joke).
' https://anthonydgreen.net/2026/04/01/this-feature-intentionally-left-blank/
Do Nothing
```

We started by trying to pin down what the actual gap is, and immediately hit a fork that the proposal does not separate:

- **缺口一（文法缺口）**：真的存在"语法上必须有一条语句、但语义上无需任何动作"的位置吗？这是"占位"的强硬版本——`Do Nothing` 是语法必需的填充物。
- **缺口二（意图缺口）**：即使语法上允许留空，作者也想**明确声明**"此处有意不做任何事"。这是软性版本——`Do Nothing` 是"可执行的注释"。

两个缺口的论证负担完全不同。缺口一要求我们指认一个今天必须塞假语句的位置；缺口二要求我们证明"注释 + 空体"不够用。The proposal slides between the two, and each has problems.

先说缺口一。建议点名的例子是"特性构造器占位"，但这是**事实错误**：VB 的方法体（含构造器）今天就可以是空体，编译通过：

```vb
Public NotInheritable Class FutureAttribute
    Inherits Attribute

    ' 空构造器体完全合法 —— 这里并不需要任何占位语句。
    Sub New()
    End Sub
End Class
```

VB 在命名实参方面对特性本来就有限制（主线 2017.08.09：*"Because in VB named argument syntax does not correspond to constructor arguments but field/property initialization this is not permitted in attribute."*），但那是特性**用法**的语法限制，与"构造器体留白"无关。`Sub New() / End Sub` 空体合法，是这个语言几十年的常态。

真正"要求至少一条语句"的位置，是**块语句**（`If` / `While` / `For` / `Do` / `Select Case` 分支 / `Catch`）与单行 `If` 的语句体。那里的问题是：**空语句列表今天是否合法？** We were not able to settle this from the notes — 这是一个 15 分钟就能在 Roslyn 里验证的实证问题，却恰好是整个提议的承重墙。详见 OPEN QUESTIONS。

### 候选方案

**PROPOSAL A — 引入 `Do Nothing` 语句（按原文）。** 新关键字短语，文法上需要为 `Do` + `Nothing` 造一条产生式；语义上不产生任何 IL，不影响 definite assignment。

**PROPOSAL B — 允许空语句列表（不新增关键字）。** 既然缺口是"块语句要求至少一条语句"，直接把文法放宽，让 `If ... Then / End If` 允许零条语句。这是建议原文 Alternatives 里的第一条。

**PROPOSAL C — 注释 + 分析器（零语言表面）。** 以 `' TODO` / `' Intentionally left blank` 作为团队约定，用 analyzer/代码风格规则奖励或惩罚空块。不碰文法，不碰绑定。

**PROPOSAL D — 复用既有语句表达留白。** 例如 `Exit Sub`、`Continue Do`、或一条无害赋值。**被否定**：`Exit`/`Continue` 不是空操作——它们改变控制流，放进"留白"位置会让留白变成有语义的语句；无害赋值（如 `x = x`）能编译但引入噪音，且要求手里有一个变量。

**PROPOSAL E — 丢弃式下划线语句（`_`）。** 借鉴主线模式匹配里 *"Probably add a discard identifier and a discard pattern"*（#337）的思路，把丢弃从模式推广到语句。**被否定**：`_` 在 VB 里已是**行继续符**——`Dim x = 1 + _` 下一行接续；把 `_` 同时当作语句会与既有的续行语法正面冲突。This is exactly the kind of collision principle #8 warns about.

### 权衡：Q&A

- **B 看起来是"文法缺口"的自然解，为什么我们没点头？** 因为 B 把"空块"变成合法，等于让 `If x Then / End If` **静默跳过**——这正是设计原则 #7 深恶痛绝的"隐蔽语义变化"：分支体为空，但读代码的人看不出这是有意为之还是删漏了。建议原文自己的 Drawbacks 也承认这一点（"与 `If`/`Select` 等分支中的'空分支'语义可能冲突"）。A 恰好相反：`Do Nothing` 把"此处空"写进了代码，可被 grep、可在调试器里看到。**在"防静默空分支"这一点上，A 优于 B。**
- **A vs C：`Do Nothing` 比注释多给了什么？** 注释会被搬走、会被删除而不报错、不会随分支一起移动；`Do Nothing` 是语句，会跟着重构走、在调试单步时出现、可以被 grep、可以被代码分析识别为"有意占位"。但这些都是**真实的、微小的**增益，且没有量化——没有数据说"业务代码里有百分之多少的注释占位被误删过"。
- **文法/解析：`Do Nothing` 会不会有歧义？** `Do` 是保留字（循环开头），`Nothing` 是保留字（字面量）。两者都不是标识符，现有代码里 `Do Nothing` 两个词不可能构成合法语法——`Do` 后面只允许换行或 `While`/`Until`。因此为 `Do Nothing` 造一条产生式是**非破坏性**的：

  ```vb
  Do Nothing          ' 新语句：空操作。
  Do While Running    ' 既有循环：`Do` + While 子句。
      Work()
  Loop
  ```

  解析器只需在 `Do` 后前瞻一个 token：是 `Nothing` 就进新产生式，否则走循环。`Nothing` 作为表达式语句本身不合法（`Nothing` 单独一行是错误），所以也不会出现"块里的 `Nothing` 表达式"需要区分。`Probably`：文法工作量极小，但 binder 要新增一个语句节点类型。
- **UQ #3：要不要强制空分支必须写 `Do Nothing`？** **绝不。** 让今天能编译的空分支变成错误，是彻底的破坏性变更（原则 #1；主线 2018.06.13 原话："We will almost never make breaking changes to Visual Basic (often resulting in _Rejected_ label)"），而且是把"留白"从可选项升级为义务——这与 A 想解决的"少写拐弯代码"背道而驰。
- **为什么它出现在"Null and Nothing"一章？** 摘录里它夹在 `??` 空合并与 `?` 空指示符之间（原文第 12 章，`..\AnthonyDesign_wordpress.txt` L1815–1817）。但 `Do Nothing` 与 `Nothing` 字面量、与空安全的**运行时** no-op（`proposal-null-safe-behaviors.md` 的 `?.` 空传播）都不同轴：后者是"对象为空时跳过动作"，前者是"作者明说没有任何动作"。放在这一章更像摘录排版，而不是设计归类。这也是整个建议"动机来源"交代不清的一个信号。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Do Nothing` 用两个保留字，需要专门产生式。歧义面很小：`Do` 只与 `While`/`Until`/换行搭配，`Nothing` 只作表达式字面量；二者组合当前必为语法错误，故新产生式不与任何既有文法重叠。单行 `If` 里能否写 `If x Then Do Nothing`？`Probably` 可以——它是一条普通语句，单行 `If` 接受任何语句列表。块内、`Catch` 内、`Using` 内同理。

#### 2. 角案例 / 边界语义

`Do Nothing` 的语义是"无操作"，所以它不应影响：
- **definite assignment**：`Dim x As Integer` 之后一条 `Do Nothing` 不改变 x 的赋值状态——它必须严格中性；
- **控制流**：它不得是 `Exit`/`Continue` 那样的"早退"语句（这是它与 `Exit Sub` 的本质区别）；
- **异步/迭代器**：`Async Sub` 或 `Iterator` 里一条 `Do Nothing` 应正常编译——它只是 nop，不需要 `Async` 上下文；
- **空分支**：`Select Case` 的分支里写 `Do Nothing` 应与写注释等价，但可 grep。

`Probably` 一个值得记录的角案例：`Do Nothing` 作为**方法体唯一语句**时，方法返回类型为 `Sub` 时无返回值问题；若误用于 `Function` 体（`Function F() As Integer : Do Nothing : End Function`），编译器仍应照常报"未返回所有路径的值"——`Do Nothing` 不得被当作隐式 `Return Nothing`。这一点必须在 spec 里写明，否则它就成了"掩盖未实现函数"的暗道。

#### 3. 作用域与绑定

`Do Nothing` 不绑定任何符号。语义模型里它对应一个新的语句节点，`GetSymbolInfo` 无符号、无类型；IDE 的 quick-info 显示"不执行任何操作"。没有作用域引入，没有 shadowing 参与。它不声明变量，所以与"绑定到 property 还是 backing field"这类问题无关。

#### 4. 与既有特性的交互

- **`Exit` / `Continue` / `Return`**：必须明确 `Do Nothing` 是唯一不改变控制流的"占位"语句。开发者若在循环里需要"跳过本次"会写 `Continue Do`，那不是留白，二者不可混用。
- **错误处理**：`On Error Resume Next` / `Try` 内放 `Do Nothing` 无特殊交互——它不吞异常、不改变错误状态。
- **`#Disable Warning`**：`Do Nothing` 应与 `#Disable Warning` 正交；建议本身甚至可以考虑配一个"空块/空体"分析器，让 `Do Nothing` 成为该分析的显式逃生阀。
- **与空安全同批建议**：`?.` 空传播产生的是**运行时** no-op（不调用、不枚举），`Do Nothing` 是**编译期**占位。两者不应共享文法或绑定代码；唯一共同点是"no-op"这个词。

#### 5. Breaking change 与兼容性

我们逐字检查了"当前是否存在合法代码包含 `Do Nothing`"：

- `Do` 后跟 `Nothing`：`Do` 需要换行或 `While`/`Until`，`Nothing` 两者都不是 → 当前必错；
- `Nothing` 是保留字，不能作变量名、标签名、成员名 → 不存在"现有代码恰好用 `Nothing` 作循环条件"的可能；
- 单行语句里 `Do Nothing` 同样无处安放。

**结论：`Do Nothing` 在任何位置当前都是语法错误，加入该语句零破坏。** 这是我们唯一能给出的、干净利落的 compat 结论——比同批其它建议（`?=`、`??` 都触及运算符重载）要清爽得多。代价是：它清爽是因为它根本没碰既有语义，而这恰恰说明它是"边缘中的边缘"。

#### 6. Option Strict / 编译选项分叉

无类型参与，无 late binding，`Option Strict On/Off` 两条路径行为完全一致——`Do Nothing` 没有表达式，Option Infer、Option Compare 均不触及。这一项是我们能直接判定的少数几项之一。

#### 7. IDE / IntelliSense

补全：`Do` 输入时补全列表会多出 `Do Nothing`（与 `Do While`/`Do Until` 并列）——需要设计排序，避免让 `Do Nothing` 成为误选率最高的项。调试：单步进会停在该行（nop 行），断点可设。代码分析：新语句应被"空体警告"类规则识别为有意占位。`Suspect`：这些 IDE 工作虽小，但原文零提及。

#### 8. 数据 / 普遍性

这是本建议最弱的一环。Motivation 没有一个真实 use case 是站得住的（特性构造器示例是错的）；没有占比数据；同批的 null-coalescing 至少有"数据库字段/字典查询"这类可想象的高频场景，`Do Nothing` 连可想象的场景都只能靠"调试期占位"——而调试期占位最长寿的形态恰恰是 `' TODO` 注释。`Suspect`：真实需求频次远低于同批其它建议。

#### 9. 更简替代

- 注释（`' TODO`）：零成本，团队约定即可——这是 C（PROPOSAL C）的地盘；
- 空体 + analyzer：如果空块本身合法，用一个分析器规则奖励"带注释的空块"、惩罚"裸空块"，比语言特性更可配置、可撤销；
- `If True Then End If`：今天的拐弯写法，丑陋但工作——它恰好证明"缺口一"不存在硬性文法障碍（这个写法能编译），从而把问题完全推向"意图缺口"。

#### 10. 复杂度 / 成本 / 优先级

实现成本低（文法 + 一个语句节点 + 无 IL），但机会成本是我们真正在意的：每加一个"第二种做事方式"，都在稀释"简单/低仪式"的核心定位（原则 #3）。优先级上，它排在同批空安全建议之后——那批至少触及真实运行时的 null 痛点，`Do Nothing` 不改变任何程序行为。

#### 11. 运行时 / CLR 硬约束

无 IL、无类型、无 PEVerify 影响。`Do Nothing` 编译后等价于不生成任何指令（`Probably` 连 nop 都不需要发——VB 方法体本可以没有语句）。不触达 CLR 存储规则，无表达式树参与。

#### 12. 值不值得做

- **价值**：低——消除的样板（`Console.WriteLine` 占位）本身就是反模式，正确做法是别写占位；
- **成本**：低——文法 + 绑定，无 IL；
- **风险**：近乎零——非破坏、无歧义。

Value is low, cost is low, risk is near zero. That is the profile of a feature that is *safe but not worth the surface*. 主线 2014 年对自动属性初始化四条提案的结论用在这里异常贴切：*"We're proud not to do anything. None of the proposals buy that much, and none are that special."*

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：这是 A 唯一的正面基因。`Do Nothing` 是英语短语，比 C# 的裸分号 `;` 可读得多——它把 C# 的"空语句"做了一次彻底的 VB 化（主线 2018.12.19 反复强调的 *"Where we need to make a decision, we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages"* 在此反而是反着用：C# 有 `;`，我们如果做，就做比 `;` 更像 VB 的东西）。
- **不引入"第二种做事方式"（原则 #3）**：这是 A 最大的扣分项。表达"什么都不做"今天已有两条路：注释、空体；加上 `Do Nothing` 就是第三条。三条路并存是教科书式的表面扩张。
- **不为边缘场景加特性（原则 #6）**：正中靶心。边缘中的边缘。
- **避免隐蔽的控制流/语义变化（原则 #7）**：`Do Nothing` 是反例式的安全——它绝不改变控制流，与 `Return?` 之流正好相反。这一点我们给它满分。
- **永不破坏（原则 #1）**：非破坏性，已逐字验证。
- **与主线关系（对照表 2.3）**：**Anthony 独立延伸**。主线没有任何"空语句"工作（VB 刻意没有 C# 的 `;`）；最近的邻居是模式匹配的丢弃（#337 *"Probably add a discard identifier and a discard pattern"*），但那是"表达式中不要这个值"，与"语句里没有任何动作"不同轴。同批建议里，null-safe-behaviors 的运行时 no-op 是真正的近亲，但它是语义特性，`Do Nothing` 是文法/意图特性。

### RESOLUTION:

1. **分开两个缺口**：`Do Nothing` 试图同时解决"文法需要语句"与"意图需要声明"，但**文法缺口不成立**——建议点名的特性构造器空体本就合法；真正要求语句的块位置是否允许空列表，是待验证的实证问题（OPEN QUESTIONS ①），不构成对 A 的支持。
2. **不采纳 PROPOSAL B（允许空语句列表）**：空块合法化会产生"静默跳过"的隐蔽语义（原则 #7），比 A 更糟。
3. **不采纳 PROPOSAL D / E**：`Exit`/`Continue` 非空操作；`_` 与行继续符冲突（原则 #8）。
4. **不强制**空分支必须写 `Do Nothing`（UQ #3）：那是破坏性变更（原则 #1），且违背本建议"减少拐弯"的初衷。
5. **价值×成本×风险**：价值低、成本低、风险近零——这是一个"安全但不值表面"的特性。引用我们自己在 2014-02-17 的结论：**We're proud not to do anything.**
6. **TABLE，绑定到明确信号**：等到 (i) 那篇 "This feature intentionally left blank" 文章原文被核实（`Suspect`：URL 日期 2026-04-01，且摘录未含其内容，无法判断是设计论文还是愚人节玩笑）；(ii) 有人指认一个今天必须塞假语句、且注释与 analyzer 都解决不了的真实位置；(iii) 出现需求频次证据。

### Implication:

- 回退状态：`proposal-do-nothing.md` 标记为 Table，不进入原型管线。
- 把"空语句列表在 Roslyn 中是否合法"作为一条独立的技术备忘交编译器团队验证——它影响的不止这一个建议（B 方案在任何未来讨论里都会回来）。
- 若将来要重审，先要那篇文章，再要一个真实位置，再要数据。三条都齐之前不回到议程。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS` ①：**当前 Roslyn VB 编译器对块语句的空语句列表是报错还是放行？**（`If x Then / End If`、`Select Case` 空分支、`While` 空体。）整个"缺口一"悬于此。We `Suspect` 空列表在多数块位置被拒绝（BC30201 一类"Expression expected"），但建议原文的示例位置（构造器体）恰恰是空体合法的位置——两者矛盾说明作者自己没试过。此为最高优先级验证项。
- `OPEN QUESTIONS` ②："This feature intentionally left blank" 文章内容与动机（`anthonydgreen.net/2026/04/01/`，`Suspect`：4 月 1 日日期 + "not a joke" 注释，真实性待核）。
- `OPEN QUESTIONS` ③：若做 `Do Nothing`，`Function` 体内它是否构成"未返回所有路径"的既得事实，还是应静默视为 `Return Nothing`？`Probably` 前者。
- `TODO`：验证属性构造器空体示例（我们断言合法，未在本会议现场编译验证）。
- `TODO`：为"注释占位 vs 语句占位"的误删率/误移率寻找任何公开数据（`Probably` 不存在，找不到就据实写"无数据"）。
- `Follow-up`：与 `proposal-null-safe-behaviors.md` 的术语区分——"运行时 no-op"与"编译期占位"不得共用文档词汇，避免读者把 `?.` 空传播误认为 `Do Nothing` 的运行时语义。

### 状态

- **LDM 状态：Table**（不进入 Active/Prototype；绑定到文章、真实位置、数据三个信号）。
- **三态判定：Table** — 安全、非破坏、实现便宜，但价值不足以支持语言表面扩张；在三个信号出现前，"什么都不做"是对"Do Nothing"的最诚实回答。

---

## 附录：特性评价

# 建议评价报告：proposal-do-nothing.md

## 评价对象

- 建议：proposal-do-nothing.md — `Do Nothing` 空操作语句
- 来源：Anthony 原文第 12 章 "Null and Nothing"（`..\AnthonyDesign_wordpress.txt` L1815–1817），仅一行示例 `Do Nothing` + 注释 "(not a joke)" + 链接文章 URL
- 配方目标：在"需要语句但暂无实现"的位置提供显式空操作占位（建议原文点名：特性构造器、空分支、调试期占位）

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。示例（`Do Nothing`）只自证"它什么都不做"，不能演示"填掉了今天写不了的什么"；点名的特性构造器场景是事实错误（空构造器合法），故声称的缺口不成立；无原型、无数据 | 已检查 | 改进不可衡量；核心场景证伪；与空安全同批的其它建议（`??`/`?=`）至少动机可想象，本建议连可想象的硬缺口都没有 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。把 C# 的 `;` 空语句 VB 化为英语短语 `Do Nothing`（原则 #5 加分），但它是表达"什么都不做"的第三条路（原则 #3 扣分），且把"文法占位"与"意图文档"两个无关能力打包在一个关键字短语里 | 已检查 | 读起来像英语是唯一正面基因；"第二种做事方式"是其核心问题；与 `Nothing` 字面量主题无关却挂在第 12 章 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、3 个未决问题具体（1–3 健康区间），但：动机含事实错误；Detailed design 仅 3 行 + 外部链接；链接文章内容未在摘录体现却作为设计依据；"任意语句位置 vs 仅限特定上下文"的核心边界未定；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 承重墙（"文法缺口"）未验证；作者引用的文章自己都没读过似的（原文称"内容未在摘录中体现"）；缺 Compat/文法分析（本建议其实是少数能给出干净 compat 结论的，反而没写） |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。暗：Drawbacks 自认"诱使无意义占位掩盖缺失逻辑"，且无任何对冲设计；风：与注释/空体形成三条并存写法，演化一致性受损；雷/水/光无明显受益（不加速迭代、不差异化、不盘活资产） | 已检查（预测待定） | "掩盖缺失逻辑"是语言设计师最怕的暗面——一个语言级占位符会制度化"留白"；无对冲；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源可追溯至原文第 12 章（正确），但：借鉴 C# 空语句 `;` 未声明；链接文章（2026-04-01，`Suspect` 愚人节日期）内容不可核验却作为核心依据；动机场景（特性构造器占位）是转述者的引申而非原文摘录——原文只是一行 `Do Nothing` | 已检查 | 材料单一到可疑（一行示例撑起整份建议）；"not a joke" 声明反而让玩笑属性成为待证问题；混入"构造器占位"这一未经原文支持的杂质场景 |

## 设计原则对照

- **与 VB 基因：部分一致，核心偏离**——`Do Nothing` 读起来像英语（原则 #5）、非破坏（原则 #1）、无反模式的控制流（原则 #7）符合基因；但"不引入第二种做事方式"（原则 #3）与"不为边缘场景加特性"（原则 #6）两条硬门槛没过。净偏离大于净符合。
- **与主线关系：Anthony 独立延伸**——主线无空语句工作（VB 刻意无 C# `;`）；最近概念邻居是模式匹配丢弃（#337，*"Probably add a discard identifier and a discard pattern"*），不同轴。与同批 `proposal-null-safe-behaviors.md`（运行时 no-op）语义上最接近但机制完全不同。
- **破坏性变更：无**——`Do Nothing` 两词组合在当前任何上下文都是语法错误，加入该语句零破坏。这是本建议唯一一项"教科书级"的正面结论。

## 总评

- **达成程度：未达成**——声称的缺口（需要语句但语法不放行）未证立；点名的示例位置（特性构造器）事实错误；设计停留在"一行示例 + 一个链接"。
- **LDM 三态建议：Table**——安全、非破坏、实现便宜，但价值低且违背"不引入第二种做事方式"；绑定到三个信号（文章核实、真实文法缺口、频次数据）后再议，在此之前不进入原型管线。
- **主要问题**：① 动机示例（特性构造器占位）事实错误，承重的"文法缺口"未验证；② 设计依据是一篇内容未核验的外部文章（且日期可疑）；③ 与注释/空体形成三条并存写法，违反原则 #3；④ 无数据、无原型、无真实 use case；⑤ 唯一干净的优势（非破坏）恰恰因为它没碰任何既有语义，侧面印证其边缘性。

## 返工建议

- **补充章节**：Compatibility/文法分析（本建议其实是少数能干净给出"非破坏"结论的，写出来反而是加分项）；"任意语句位置 vs 特定上下文"的边界决定；`Function` 体内 `Do Nothing` 与"未返回所有路径"的关系。
- **补充证据**：① 先在 Roslyn 验证"块语句空列表是否合法"（一票否决级证据）；② 找到或指认一个真实存在的、必须塞假语句的位置；③ 那篇 "intentionally left blank" 文章的原文与作者意图；④ 任何关于占位注释误用率的公开数据，找不到就据实写"无数据"。
- **未决问题处理**：UQ #3（强制写 `Do Nothing`）可直接关闭——破坏性变更，永不采纳；UQ #1（链接文章）与 UQ #2（位置范围）转入 OPEN QUESTIONS 并绑定重审信号。
- **设计探索**：若重审，先回答"它是文法特性还是意图特性"二选一——作为意图特性，它要和 analyzer 生态竞争（C 方案），必须证明 analyzer 不够用；作为文法特性，它必须先证明空块在目标位置非法。两条路都不通时，答案就是我们今天的答案。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的 C# 生态考量，不改写正文。评估对象与正文一致：`Do Nothing` 空操作语句（及其所在 "Null and Nothing" 摘录批次）。所有 C# 引用均逐字取自 ..\..\csharplang 镜像并标路径；无法核实的标注 **Suspect** 或 **OPEN QUESTIONS**。

### 相关 C# 现实方向

本提案主题在 C#/CLR/.NET 生态中的对应物，几乎不落低层互操作主线（Span/指针/COM/AOT），而是落在两个成熟的语言文法点上：**C# 空语句 `;`** 与 **null-conditional 家族（C# 6 `?.`/`?[` → C# 14 null-conditional assignment）**。索引对 null-conditional 无专属 T 条目（最近邻是 T7 dynamic 边缘化、T8 未来方向），以下原文均在 ..\..\csharplang 直接核实。

**C# 空语句 `;`（`Do Nothing` 的直接对应物）**：
- C# 一直有独立的空语句：单独一个 `;` 就是一条"无操作"语句，可用于任何需要语句的位置；空块 `{}` 同样合法（statement lists 可为空）。规范章节入口：`spec\statements.md` → §12.3 Blocks / §12.3.2 Statement lists / §12.4 The empty statement（链接索引；正文已迁 dotnet/csharpstandard，镜像内无正文）。
- C# 从未觉得需要 `DoNothing` 关键字——空语句就是显式的 do-nothing，且编译器对"可疑空语句"照常给警告。镜像内逐字证据（C# 8 using declarations 讨论）：「...`using (expr);` which compiles successfully with a warning about an empty statement.」→ `proposals\anonymous-using-declarations.md`
- C# LDM 从未把空语句/do-nothing 当议程：本镜像 LDM notes 按 "empty statement" / "empty block" / "do nothing" 检索无议程命中——它是"尘埃落定的文法"，不是 C# 的活跃方向。

**null-conditional 家族（同批 `proposal-null-safe-behaviors.md` 的 `?.` 对应物）**：
- C# 6 引入 `?.` / `?[`，Language-Version-History 记录为「Null propagator (null-conditional operator, succinct null checking)」→ `Language-Version-History.md`（C# 6 列表）。语义即"接收者判空、为空则短路并求值为 null"（spec §11.7.7 Null-conditional member access / §11.7.11 Null-conditional element access，链接见 `proposals\csharp-14.0\null-conditional-assignment.md` 的 Specification 引用；标准正文在 dotnet/csharpstandard）。
- C# 14 把 `?.` 从读侧扩到写侧（null-conditional assignment）：「Permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression.」→ `proposals\csharp-14.0\null-conditional-assignment.md`（Summary）；lowering 逐字：「`P?.A = B` is equivalent to `if (P is not null) P.A = B;`, except that `P` is only evaluated once.」→ 同上（Specification）；限制：「Conditional access expressions are still not lvalues, and it's still not allowed to e.g. take a `ref` to them.」→ 同上（Detailed design）；`++`/`--` 明确不允许（LDM 结论逐字：「We decided to disallow these operators.」→ `meetings\2024\LDM-2024-10-28.md`）。
- **与 NRT 的互动**（C# 8）：`?.` 的结果态恒为 maybe-null。逐字：「A `null_conditional_expression` has the null state "maybe null".」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`

### 现实 vs 提案

- **`Do Nothing` ↔ C# 空语句：概念兼容、价值被 C# 先例进一步证伪。** C# 用 `;` 解决"需要一条语句但无事可做"，证明"显式无操作语句"有效、非破坏、长期稳定；但 C# 从 1.0 到 15 从未把它升级成关键字/特性，恰是正文 RESOLUTION 第 5 条"安全但不值表面"的现实注脚。C# 空语句还说明：**缺口一在 C# 侧根本不是缺口**（`;` 处处可用）；VB 若照搬最贴切的形态是 PROPOSAL B（允许空语句列表），而会议已因原则 #7（静默空分支）拒绝。
- **一个值得记录的"可见性"文化差异（非互操作约束）**：C# 容忍 `if (x) { }` 与 `if (x) ;`，是因为 `{}`/`;` 都是**可见**的显式符号；VB 的 `Then ... End If` 空体在两者之间没有任何可见 token，才会被读成"删漏了"。C# 的"可见空"不能简单移植成 VB 的"不可见空"——这不推翻 PROPOSAL B 的否决，反而解释了 C# 与 VB 对空体容忍度差异的根源，也支持正文"VB 化后必须是短语而非标点"的立场。
- **`?.` 家族 ↔ C# 6/C# 14：直接同向、有成熟蓝本。** 同批 `proposal-null-safe-behaviors.md` 的 `?.` 空传播正是 C# 6 的既有语义（接收者一次求值 + 短路 + 求值为 null）；NRT 的 "maybe null" 规则是 .vbx 做可空流分析必须复刻的一条。C# 14 null-conditional assignment 为同批 RESOLUTION 第 4 条（空条件赋值 Consider）提供了现成的 lowering 与限制清单。
- **一处过时事实修正（跨会议提示）**：`meeting-null-safe-behaviors.md` 正文称 "`obj?.Member = value` 在 C# 是编译错误"——这在 C# 13 及以前为真；**C# 14 null-conditional assignment 已把它变合法**（`proposals\csharp-14.0\` + `Language-Version-History.md` C# 14 条目）。同批会议写判词时基于的 C# 现实已变。
- **互操作表面：几乎为零。** `Do Nothing` 编译不产生 IL；`?.` 只是普通分支 lowering。两者都不触达 CLR 存储规则、不反射、不涉 AOT/trimming 压力，与索引 T2–T6 低层主线无冲突，与 T7 仅在对 VBScript 脚本层定位上间接相关。

### 对 VBScript.NET 的适应建议

- **`Do Nothing`（若做）：零元数据、零 IL，无新增互操作面。** 重审时参考 C# 空语句"保持轻量"的教训——C# 用标点、VB 用短语，纯属文法/意图差异；不要让它触达运行时、不要给它任何 CLR 语义。
- **`?.` 家族（同批）**：以 C# 6 语义为 reference lowering（接收者一次求值、短路、求值为 null）；空条件赋值对齐 C# 14 的成形设计（`if (P is not null) P.A = B;` 且 P 只求值一次、复合赋值全允许、不可作 lvalue/ref、无 `++`/`--`），使 .vbx 与 C# 的观测语义逐位一致，跨语言调用不打架。
- **识别 NRT 元数据**：.vbx 若推进可空性流分析（`proposal-nullability-flow-analysis.md`），必须读取 C# 的 nullability 属性标注（`NullableAttribute`/`NullableContextAttribute`，属 dotnet/runtime 实现），并复刻 "`?.` 结果恒为 maybe-null" 规则，否则消费 C# 库时空态不一致。
- **AOT 友好**：`?.` 是纯编译期 lowering、无反射，AOT/trimming 安全——与决策文件 M2（Any/dynamic 与 AOT 的张力）无关，可放心进入脚本编译产物。
- **术语纪律**：会议 Follow-up 已要求区分"运行时 no-op"（`?.`）与"编译期占位"（`Do Nothing`）。C# 侧同样严格区分（`?.` 是表达式运算符、`;` 是语句），本附录确认这条纪律有 C# 生态背书，.vbx 文档不应让二者共用词汇。

### 对既有 RESOLUTION/三态判定的影响

- **`Do Nothing` 三态判定不变：Table。** C# 现实既不推翻也不加分：空语句证明概念合法、非破坏（支持正文"非破坏是唯一干净结论"），但 C# 从不升级空语句为关键字，佐证"价值低、表面不值得"（RESOLUTION 第 5 条）。C# 侧没有任何力量要求 VB 做 `Do Nothing`。
- **PROPOSAL B 的否决被 C# 文化差异解释而非推翻。** C# 容忍"可见空"（`{}`/`;`），VB 的"不可见空"（`Then/End If` 之间）触发原则 #7——C# 先例不构成对 B 的支持，反而划清了"为何 VB 不能照搬 C#"。
- **对同批 null-safe-behaviors 的跨会议影响**：C# 14 的落地**强化**该会议 RESOLUTION 第 4 条（空条件赋值 Consider）——现在有 reference lowering 与限制清单可用；但该会议正文一处 C# 事实已过时（见上），建议加注更新。
- **证据等级提示**：正文引主线 2018.12.19 "follow C# unless compelling reason"（vblang 源，csharplang 镜像无对应）；C# 侧空语句属 §12.4 既定文法、无近期 LDM 议题，说明这是"已尘埃落定的文法"而非 C# 活跃方向——VB 无需追赶任何 C# 动态。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「Null propagator (null-conditional operator, succinct null checking)」→ `Language-Version-History.md`（C# 6 列表）
- 「Permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression.」→ `proposals\csharp-14.0\null-conditional-assignment.md`（Summary）
- 「`P?.A = B` is equivalent to `if (P is not null) P.A = B;`, except that `P` is only evaluated once.」→ 同上（Specification）
- 「The right side of the assignment is only evaluated when the receiver of the conditional access is non-null.」→ 同上（Detailed design）
- 「Conditional access expressions are still not lvalues, and it's still not allowed to e.g. take a `ref` to them.」→ 同上（Detailed design）
- 「A `null_conditional_expression` has the null state "maybe null".」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`
- 「...`using (expr);` which compiles successfully with a warning about an empty statement.」→ `proposals\anonymous-using-declarations.md`
- 「We decided to disallow these operators.」→ `meetings\2024\LDM-2024-10-28.md`
- 「...permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression (`a?.b = c`).」→ `Language-Version-History.md`（C# 14 列表）

### OPEN QUESTIONS / Suspect

- **Suspect（外部标准正文）**：C# 空语句 §12.4 与 `?.` §11.7.7/§11.7.11 的**正文文本**位于 dotnet/csharpstandard（csharplang 镜像外）。本附录仅核实了镜像内 `spec\statements.md` 的链接索引与 C# 14 提案对 §11.7.7/§11.7.11 的引用，**未逐字引用标准正文**；`;` 的"无操作"语义与空块合法是既有语言事实，如需逐字原文以 dotnet/csharpstandard 为准。
- **OPEN QUESTION**：C# 14 null-conditional assignment 的**确切发布状态**（提案归档于 `proposals\csharp-14.0\`、Language-Version-History 列于 C# 14，随 .NET 10 同步的发布细节本镜像未记录）。
- **OPEN QUESTION**：NRT 标注的属性名（`NullableAttribute`/`NullableContextAttribute`）属 dotnet/runtime 实现，speclet 仅写 "attributes"、未点名。
- **关系弱申明**：`Do Nothing` 与索引低层 interop 主线（T2 Span/ref、T3 指针/函数指针、T4 COM、T5 AOT/trimming、T6 source-gen）无直接冲突、无互操作表面；与 T7（dynamic 边缘化）仅在对 VBScript 脚本层定位上间接相关。本附录因此聚焦两个真正的 C# 对应物：空语句 `;`（`Do Nothing`）与 `?.`/`?[` 家族（同批空安全）。
