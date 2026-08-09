# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周议题来自同一片可空性土壤：后置空合并运算符 `??` 与空指示符 `?`。它与上周的 `Null` 字面量会议共享同一批主线参照物（NRT 推迟决定、2014 年 `Null` 拒绝记录），所以不少论证可以复用——但有一个关键差异：`Null` 字面量的竞争对手是 `Nothing`，而本建议的竞争对手是 VB 自己的 `If(a, b)` 二参形式。这让我们把大半场时间花在了「我们是否已经在做这件事」这个问题上。

## Agenda

* [Proposal - 后置空合并运算符与空指示符](#proposal---后置空合并运算符与空指示符)

## Proposal - 后置空合并运算符与空指示符

_Related: [LDM-2014-02-17](../../vblang/meetings/2014/LDM-2014-02-17.md)（#18 `Null` 字面量拒绝、#46 修复三元 `If`、#47 可空转换、#54 `?.` 批准）· [LDM-2014-04-01](../../vblang/meetings/2014/LDM-2014-04-01.md)（`?.` 是 UserVoice 第二高票、`??` 结合性）· [vbldm-notes-2017.08.30](../../vblang/meetings/2017/vbldm-notes-2017.08.30.md)（NRT 未就绪、`!` 冲突）· [vbldm-notes-2018.02.07](../../vblang/meetings/2018/vbldm-notes-2018.02.07.md)（NRT 推迟）· AnthonyDesign section 12「Null and Nothing」/ 18.16「Nullable Reference Types」· ModVB `proposal-null-coalescing.md`_

### Scenario / 场景与缺口

提案描述的场景我们非常熟悉：从数据库字段、字典查询、外部 API 拿到「可能为空」的数据之后，需要一个简洁的内联兜底值。原文的招牌示例：

```vb
' 后置空合并运算符（AnthonyDesign section 12 原样）
Label1.Text = account.Description ?? "n/a".
```

以及空指示符 `?` 的三个用途——值可空推断、可空引用类型声明、表达式后的可空性文档化：

```vb
' 空指示符 `?`：值可空推断
Let flag = True?                ' 推断 Boolean?

' 空指示符 `?`：可空引用类型
Property Description As String?

' 空指示符 `?`：文档化"可能为 null"，开启更严格错误检查
Let result = lookup("key")?
```

（注：`Let` 是 ModVB 自己的声明风格——对照表 2.3「`Let` 替换 `Dim`」为 Anthony 独立延伸，主线无此概念。下文示例沿用原建议的 `Let`，但我们的讨论与声明关键字无关。）

但是——我们第一轮的共同反应不是「好，这正是我们缺的」，而是「VB 不是已经有 `If(a, b)` 了吗」。VB 的二参 `If` 运算符自 VB 2005 起就存在：`If(x, y)` 在 `x` 为 Nothing（对可空值类型为无值）时返回 `y`，否则返回 `x`，且第二个操作数**短路**——仅在第一个操作数为空时才求值。这与提案给 `??` 定义的语义逐字重合：

```vb
' 今天就能写的等价物，零新语法
Label1.Text = If(account.Description, "n/a")

' 组合场景也被覆盖：空条件成员访问（2014-02-17 #54 批准、VB14 已发布）
' 等价写法是 If(customer Is Nothing, Nothing, customer.Name)，并放入临时变量
Dim addr As String = If(customer?.Address, "unknown")
```

We think the gap 不是能力缺口，而是**拼写与发现性**缺口。能力上 VB 早在 C# 的 `??` 之前就有空合并；问题只在于 `If(a, b)` 二参形式不如一个 `??` 符号显眼。这决定了本场辩论的走向。

### 候选方案

**PROPOSAL A — 完整实现（Anthony 方案）。** 同时引入 `??` 中缀运算符与 `?` 空指示符的全部三种用途（值推断、引用类型标注、表达式文档化）。范围与 AnthonyDesign section 12 一致，并隐含依赖同一章节的 `?=` / `?<>` / `?.` / `Await x?` / `For Each x In c?` 家族。

**PROPOSAL B — 只做 `??`，`?` 家族全部不做。** `??` 是纯运算符（语义已由 `If(a, b)` 覆盖，但语法更短）；`?` 指示符依赖未定型的 NRT，单独摘出。

**PROPOSAL C — 只做 `?` 值可空推断（`True?`），`??` 不做。** 理由：`??` 被 `If(a, b)` 完全覆盖，而 `True?` 的「从字面量直接推可空类型」是 `If` 无法表达的新能力（虽然 `Dim x As Boolean? = True` 也可达，见下文）。

**PROPOSAL D — 什么都不做。** 维持 `If(a, b)` 现状；可空引用标注等 NRT 定稿后再议；`lookup("key")?` 的「严格检查」需求用 analyzer/诊断缓解。这是 2014 年之后主线的默认立场。

### 权衡：Q&A

我们先把被否定的思路也留档，供后人回看。

- **A vs B：`?` 家族能解耦吗？** 只能解耦一半。`True?` 值推断**不依赖** NRT——`Boolean?` 就是 `Nullable(Of Boolean)`，是 2014-02-17 #47 之后已稳定的语法。但 `String?` 作为可空引用类型标注**必须**依赖 NRT，而 NRT 的地基主线没打、Anthony 自己还在重画（见第 5 节）。`lookup("key")?` 的「更严格检查」依赖什么更是完全没有定义。**结论：`?` 家族不是一个特性，是三个互相纠缠的提案。**

- **A vs D：`??` 值得做吗？** 这是我们最尖锐的一问，结论见 RESOLUTION。先记录核心论据：`If(a, b)` 与 `??` 在求值语义、短路、可空值类型处理、dominant-type 推断上**没有差异**——两者对 `Nullable(Of T)` 无值、对 `Nothing` 空引用行为完全一致。换句话说，`??` 不是表达新语义，而是给同一事物换第二个拼写。这让我们直接想起 2014 年 `Null` 字面量的命运：它在引用类型上 `Nothing` 完全等价，是纯粹的第二拼写，被拒。`??` 现在对 `If(a, b)` 处于同样的位置。

- **反方论点：发现性。** `If(a, b)` 二参形式存在，但两参重载不如三参形式广为人知。很多开发者不知道 `If(x, y)` 可以当空合并用，于是会去写 `If(x IsNot Nothing, x, y)`（三参形式 + 显式测试）。一个 `??` 符号的发现性确实更好。We think 这是本建议**唯一**能站住的论据。但它没有数据支撑：我们不知道 `If(a, b)` 的真实使用率、不知道用户对 `??` 的请求量。在数据缺席时，引入第二种做事方式违背我们一贯的成本纪律。

- **C 的价值评估：`True?` 有多新？** 说实话，`Dim flag As Boolean? = True` 与 `Let flag = True?` 一样短。`True?` 的真正价值只在**类型推断链**里：`Dim x = If(cond, True?, Nothing)` 之类场景让可空性从字面量开始传播。但这是边缘增益，且 `?` 后缀会立刻撞上 `?.` 的 tokenizer（见深度追问第 1 条）。We `Suspect` 这个子特性是为家族而家族。

- **A 的隐含捆绑：`??` 与 `?` 是两份提案。** 一份提案混杂多个独立特性、边界模糊，是我们的红旗清单第一项。`??` 是运算符（语义已被覆盖），`?` 是类型/表达式标注（依赖 NRT）。把它们捆绑在一起，`??` 的正确性会被 `?` 的未定型拖下水。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**术语矛盾先放上桌。** 提案标题与 Summary 称 `??` 为「后置」（post-fix），但唯一示例 `account.Description ?? "n/a"` 是标准的**中缀**二元运算——左操作数与右操作数分居符号两侧，AnthonyDesign 原文注释也写 "Post-fix null-coalescing operator."。若真意是「中缀」（与 C# 一致），「后置」是术语笔误；若真意是「后置」（`x?` 单目），则示例、描述、Summary 三处互相矛盾。We `Suspect` 这是术语错误——`??` 显然有两个操作数。但一份连自己符号定位都写不清楚的提案，是我们把品质打到低分的直接理由之一。

**`??` 的优先级与结合性完全未定义。** 提案没有给出任何文法。C# 的 `??` 是右结合、优先级低于 `||`，我们 2014 年曾引用过 Eric Lippert 关于 `??` 结合性的博客（LDM-2014-04-01）。但 VB 若要引入，优先级表要独立定义：`a ?? b` 与 `Is Nothing`、`AndAlso`、`If()` 如何排序？`a ?? b Is Nothing` 是 `a ?? (b Is Nothing)` 还是 `(a ?? b) Is Nothing`？提案只字未提。

**后缀 `?` 与 `?.` 的 tokenizer 冲突是本建议最实的语法钉子。** VB 自 2014 年 #54 批准后已发布空条件成员访问 `customer?.Name` 与空条件索引 `customer?(0)`。若再允许表达式后附加后缀 `?`，那么 `customer?.Name` 就有了两种切分：

```vb
customer?.Name    ' (a) 空条件成员访问：若 customer 非空则取 .Name
                  ' (b) (customer?) 后缀可空标注，然后 .Name 普通成员访问
```

按最大匹配原则 tokenizer 会取 `?.` 单 token——即保持 (a)。但用户若想写 (b)「文档化 customer 可能为空，然后访问 .Name」，必须写 `(customer?)?.Name` 之类，语法立刻变丑；而编译器又必须在 (b) 的形态下区分「这是 (a) 还是 (b)」。**加一个后缀 `?`，等于给所有 `?.` 表达式的解析投下阴影。**

**`a? ?? b` 与 `a ?? b?` 的归属。** 提案自己把这两个形态列为未决。我们的追问更狠：如果 `?` 是后缀运算符，`a ?? b?` 中的 `?` 是标 `b` 还是标整个 `a ?? b`？如果是标整个表达式，`??` 与 `?` 的优先级要一起定义；如果是标 `b`，`b?` 的类型是什么？这些不是「待定细节」，是设计的一半。

#### 2. 角案例与边界语义

- **链式合并**：`a ?? b ?? c` 右结合（C# 语义）。VB 等价 `If(a, If(b, c))`——可读性相当，语法长度相当。无增益。
- **与 `?.` 组合**：C# 中 `customer?.Address ?? "unknown"` 是空安全的经典组合。VB 用 `If(customer?.Address, "unknown")` 已有。组合场景被覆盖，是「不引入第二种方式」论据的一部分。
- **可空值类型**：`Dim d As DateTime? = deadline ?? Now`——提案未定义 `??` 与可空值类型的交互（左操作数为无值可空时取右）。`If(deadline, Now)` 已正确处理。再次重合。
- **`True?` 与 `Option Infer`**：`Let flag = True?` 在 `Option Infer Off` 下如何推断？`?` 后缀是给类型推断喂信息，还是独立显式类型？若 `Option Infer Off` 且无 `As` 子句，`Let flag = ...` 本身在 ModVB 语义里是什么类型？提案未答。
- **`lookup("key")?` 的运行时语义**：`?` 后缀**不改变求值结果**（仍返回 `lookup("key")` 的值），只改变编译器的错误检查严格度。这意味着同一表达式在加 `?` 前后运行时行为完全相同——一个**零运行时语义**的运算符。这本身不违法（`NameOf` 也是），但它的「开启更严格检查」到底检查什么，是凭空消失的验收标准。

#### 3. 作用域与绑定

`lookup("key")?` 的「更严格检查」一旦具体化，语义模型要给 `?` 引入一类新的 annotation symbol——它既不改变 `GetSymbolInfo` 的绑定，又要参与 nullability 状态传播。这与可空性流分析共享状态机，但触发方式（后缀开关 vs 整文件 `Option`）完全不同。`True?` 的 `GetTypeInfo` 返回 `Nullable(Of Boolean)`，这没问题；`String?` 属性在语义模型里返回什么——`String` 还是新的 annotated `String`？依赖 NRT 的表示，而 NRT 的表示在 VB 里从未定过。

#### 4. 与既有特性的交互

- **`If(a, b)` 二参形式**：本建议最大的对手，见 Q&A。
- **`?.` / `?()` 空条件**：tokenizer 冲突见第 1 条；`??` 与 `?.` 组合被 `If` 覆盖。
- **`Integer?` 可空值类型语法**：`?` 作为类型后缀**已存在**（`Nullable(Of T)` 的糖）。再给 `?` 加「表达式后缀」与「可空引用标注」两种新身份，同一符号在**类型位置、表达式位置、成员访问位置**承担三种语义，加上 `??` 的第四种（二元合并）——符号过载，这是本建议 Drawbacks 自己承认、我们完全同意的一点。
- **`Is Nothing` 惯用法**：`If(x Is Nothing, y, x)` 是新手写空合并的常见形态。`If(a, b)` 已经消灭它；`??` 进一步消灭它，但代价是消灭 `If` 本身的使用率。
- **NRT**：`String?` 直接挂在未定型 NRT 上（见第 5 节）。
- **`Option Strict` / late binding**：`lookup("key")` 在 `Option Strict Off` 下返回 `Object`，`?` 后缀对 `Object` 的「严格检查」是什么？严格检查概念在宽松模式下没有锚点。

#### 5. Breaking change 与兼容性

直接层面：**无破坏**。`??`、`True?`、`String?`（在非 NRT 语境）、`lookup("key")?` 在今天全部是语法错误或非法类型，全部是新开的口子，旧代码行为不变。

间接层面：**有风险**，且集中在 `?` 后缀。若 `?` 后缀合法化，所有 `?.` 表达式进入「两种切分」的灰色地带。我们无法保证某个在 `Option Strict Off` + 旧 `langversion` 下解析为 `customer?.Name` 的表达式，在新语言版本下仍以同样方式解析。更阴险的是：若未来 NRT 定稿、`String?` 语义落定，**重编译**可能改变既有声明（`Property Description As String` 与 `As String?` 若被宽松对待，代码库的 nullability 状态会漂移）。这是 2018.02.07 Part 2 警告过的：表面标注的传播"won't be as simple as managing attributes"。我们的规则是「几乎从不做破坏性变更」——直接破坏为零，但 `?` 家族的**未来破坏**是不可量化的黑盒。

#### 6. Option Strict / 编译选项分叉

两条路径必须行为一致。`??` 运算符本身无分叉（二元运算符，类型推断与 `If(a, b)` 相同路径）。分叉集中在 `?`：`True?` 的推断依赖 `Option Infer`；`lookup("key")?` 的「严格检查」在 `Option Strict Off` 下没有定义（严格概念的局部化是 `#117` Block-scoped Option 讨论的领域，但那是另一条线）。若两条路径对 `?` 的接受度不一致，会造出「写了 `?` 才能编译」与「写了 `?` 是错误」的两片世界。

#### 7. IDE / IntelliSense

`??` 的 IDE 影响小（补全与 `If(a, b)` 相同）。`?` 家族的 IDE 影响是整条 NRT 链路：波浪线、Quick Actions、InfoTip 显示可空性、`lookup("key")?` 的「严格检查」需要新的错误文案与建议动作。这条链路在 VB 里从未建过（2018.02.07 推迟 NRT 时连 C# 侧都只是原型）。语义模型要为 `?` 引入 annotation 概念，Roslyn 改动覆盖 parser→binder→flow-analysis→IDE 四层——这是与 `Null` 字面量会议一致的结论：地上建筑再好看，地基没打就是没打。

#### 8. 数据 / 普遍性

- 痛点场景（数据库/字典/外部 API 的空值）**普遍**，这点无疑。
- 但「`If(a, b)` 不够好」**没有数据**：没有 `??` 请求量、没有 `If(a, b)` 使用率统计、没有用户抱怨「二参 If 难发现」的证据。
- 迁移视角：VBScript/VBA **没有** `??`，`If` 的三参形式倒是脚本世界的熟面孔——所以「脚本用户期待 `??`」不成立，方向甚至相反。
- C# 的 NRT 采用率 2026 年已被主流接受（对照 2018 年的等待信号），`Probably`——但 C# 采用 NRT 不等于 VB 用户想要 `?` 后缀；VB 侧的接受度数据我们拿不到。

#### 9. 更简替代

- **`If(a, b)`**：现成的、已发布的、语义完全一致的替代。这是「更简替代」清单的终极大奖。
- **Analyzer / 诊断**：把 `If(x IsNot Nothing, x, y)` 的「你不会用二参 `If`」提示做进 analyzer，教会用户现有惯用法——80% 的价值，零语言改动。
- **`New Boolean?` / 显式注解**：`True?` 的价值可用 `Dim flag As Boolean? = True` 达到。
- **什么都不做 + 文档**：把二参 `If` 的发现性当作文档/教程问题而非语言问题处理。

#### 10. 复杂度 / 成本 / 优先级

`??` 单独：成本低（二元运算符 + 优先级表 + 转换规则），但它同时是**零价值增量**（语义已被覆盖）。`?` 家族：成本高（四层管道 + NRT 依赖），且 NRT 无时间表。**高成本 + 零到未知价值**的组合，在任何优先级会议上都不会好看。

#### 11. 运行时 / CLR 硬约束

`??` 的代码生成是既有模式（`dup; brtrue` 短路），无 CLR 约束。`?` 后缀是纯编译期概念，运行时无痕迹。真正的 CLR 层硬骨头是 **NRT 表面标注跨程序集传播**——2018.02.07 Part 2 已明确"at least at the implementation level it won't be as simple as managing attributes"。`??` 不碰它，`String?` 整个坐在它上面。

#### 12. 值不值得做：价值 × 成本 × 风险

- **价值**：`??` 的真实价值 = 拼写长度 − 发现性，被 `If(a, b)` 基本清零。`?` 家族的价值建立在未定型 NRT 上，现值不可评估。
- **成本**：`??` 低；`?` 家族高。
- **风险**：`?` 后缀的 tokenizer 阴影 + NRT 未来的重编译漂移，中等且不可量化。
- **合计**：`??` 是「低价值 × 低成本 × 低风险」——一个不值得做的优化；`?` 家族是「未知价值 × 高成本 × 中风险」——一个不该现在开的工。两者都不进 Active。

### VB 基因对照

我们逐条对照设计原则：

- **原则 3「不引入第二种做事方式」**：**违反**。空合并已有 `If(a, b)`，`??` 对全部场景语义等价，是纯粹的第二拼写。这是否决 `??` 的最重砝码。C# 有 `??` 是因为 C# 没有 `If(a, b)`；VB 的先有惯用法正是原则 4「默认跟随 C#，除非有充分理由」里那个**充分理由**。
- **原则 5「读起来像英语、对新手友好」**：`If(account.Description, "n/a")` 读起来像英语；`account.Description ?? "n/a"` 是符号行话。`If` 胜出。
- **原则 7「避免隐蔽的控制流/语义变化」**：`lookup("key")?` 用一个字符改变错误检查行为而**不改变任何运行时语义**——这是隐蔽变化的变体：编译期行为随一个后缀开关翻转。正是 2014 年拒绝 `Return?` 的同类警惕。
- **原则 8「不与既有语法冲突」**：`?` 后缀与 `?.`、`?()`、`Integer?` 类型语法直接相撞。2017.08.30 我们为 `!` 与字典访问 `dict!key`、单精度类型字符 `Dim radius!` 的冲突发过愁——`?` 复刻了同款烦恼。
- **原则 9「消除常见样板」**：这是本建议唯一沾光的条目，但样板早已被 `If(a, b)` 消灭。`??` 消灭的是「`If(a, b)` 的括号」，不是样板。
- **原则 10「冗长只在有用时是美德」**：`If(a, b)` 的冗长是有用的——它自解释、无歧义、与三参 `If` 同一家族。
- **2.3 主线对照表**：`Null 安全全家桶` 一行写得很清楚——主线对 null 条件 `AddHandler` 已 No Plans、整体保守，Anthony 大范围铺开。本建议属于 **Anthony 独立激进延伸**，且拿不出反驳主线保守立场的新证据。与 `Null` 字面量会议（Table 结论）在同一格子里。

### 诚实分层

- **事实**：VB 已有 `If(a, b)` 二参空合并（短路、覆盖可空值类型）；`?.` 已发布（2014-02-17 #54 批准）；`Integer?` 是既有可空值类型语法；2014-02-17 #18 的 `Null` 字面量被拒（"Rejected on 2014-01-06."）；2017.08.30 NRT "Not ready yet" 且 `!` 有冲突；2018.02.07 NRT "We'll postpone this until we understand the uptake in C#."；Anthony 18.16 自认 NRT "I must refine the design"；proposal 的 `??` 示例是**中缀**而标题称「后置」，且示例行尾带多余句点。
- **Probably**：到 2026 年 C# NRT 已被主流采用，但 VB 用户接受度无数据；`If(a, b)` 二参形式的发现性确实差于 `??`，但「差多少」无量化。
- **Suspect**：提案所称「后置空合并」实为中缀，术语矛盾；`?` 家族三用途可解耦程度；`lookup("key")?`「更严格检查」规则的可行性——一个零运行时语义、只在编译期翻转严格度的后缀运算符，是否真的可设计得用户不困惑。
- **OPEN QUESTIONS**：① `a? ?? b` / `a ?? b?` 的归属（提案自列，我们无法在无文法下作答）；② 后缀 `?` 与 `?.` 的 tokenizer 消歧是否可行（我们倾向不可，除非牺牲后缀形态）；③ `String?` 在修订后 NRT 中的最终语义（Anthony 未交付）；④ `True?` 在 `Option Infer Off` 下的推断。
- **TODO**：为 `If(a, b)` 与 `??` 的「发现性差距」收集数据（用户请求、教程调查）；起草 `?` 后缀 tokenizer 消歧的失败案例集；跟踪 Anthony NRT 修订。

### RESOLUTION:

本建议整体标记为 **Table**，并按四条轨迹分别落定——We think 这份提案的真实价值需要拆开看，捆绑评估只会互相拖累：

1. **`??` 中缀空合并运算符：Reject。** VB 的 `If(a, b)` 二参形式已实现完全相同的语义（短路、可空值类型处理、dominant-type 推断一致），`??` 对全部场景是第二拼写。这与 2014 年拒绝 `Null` 字面量的先例（`Nothing` 已存在）逻辑一致，也符合原则 3 与原则 5。若未来数据证明二参 `If` 的发现性差距大到值得一个运算符，重新打开讨论——但届时替代方案也应包括「改进 `If` 的发现性」这条零语言改动路径。

2. **`?` 可空值推断（`True?`）：Consider（低优先级，独立窄建议）。** 它是家族中唯一不依赖 NRT 的成员，无破坏、可单独落地。但价值低（`Dim flag As Boolean? = True` 已达同效），且后缀形态撞 `?.` tokenizer。作为独立、更窄的提案继续探索，不随家族走。

3. **`?` 可空引用类型标注（`String?`）：Table。** NRT 地基未定——主线 2018 推迟（"We'll postpone this until we understand the uptake in C#."）、2017.08.30 "Not ready yet"、Anthony 自认修订中。与 `Null` 字面量会议结论一致：不把新语法建立在未定型的语义上。

4. **`?` 表达式文档化（`lookup("key")?`）：Reject。** 「更严格检查」的具体规则未定义——哪些误用报错、哪些仅提示、与 `Option Strict` 如何交互，全部空白。规则未定义 = 无法验收；且一个字符翻转编译期严格度，违反原则 7 对隐蔽变化的警惕。

**Implication:**

- 建议头部加一行"LDM 2026-08-08: Table，见会议纪要"；`??` 与 `lookup()?` 标注 Reject，`String?` 标注 Table，`True?` 标注 Consider。
- 拆出独立探索项："二参 `If` 发现性数据调查"（支持 `??` 重估的唯一证据路径）；"`?` 后缀 tokenizer 消歧草案"（决定 `True?` 是否可落地）。
- 更新 2.3 对照表 `Null 安全全家桶` 一行的记录，注明 2026-08-08 评估结论。
- 与同组其他会议对表：`null-equality`（`?=` / `?<>`）、`null-safe-behaviors`（`For Each x In c?`、`Await x?`）共享同一家族，避免重复讨论同一批未定问题。
- 建议作者补文法（优先级/结合性/BNF）、`lookup()?` 检查规则、与 `If(a, b)` 的逐场景等价表。

**Verdict: Table**（`??` Reject / `True?` Consider / `String?` Table / `lookup()?` Reject）。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-null-coalescing.md` — 后置空合并运算符 `??`（左操作数为 null 时返回右操作数）与空指示符 `?`（值可空推断 / 可空引用类型标注 / 表达式可空性文档化）。
- 来源：Anthony 原文章节 12「Null and Nothing」（`..\AnthonyDesign_wordpress.txt` L1812–1827），隐含 18.16「Nullable Reference Types」。
- 配方目标：为「可能为空」的数据提供简洁的内联兜底（`??`）与可空性标注（`?`），供更严格错误检查。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。主效果（内联兜底）真实且示例可演示，但被既有 `If(a, b)` 完全覆盖——改进幅度归零；关键子效果（`String?` 可空引用、`lookup()?` 严格检查）依赖未定型 NRT / 未定义规则，缺失或悬空 | 已检查 | 无原型（状态行占位链接 `PROTOTYPE_OWNER/...`、`pr/1`）；`If(a, b)` 已覆盖主效果未在提案中自省；`lookup()?` 无验收标准 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`??` 是 C# 运算符原样照搬（语义已被 VB 既有 `If` 覆盖，属"第二做事方式"）；`?` 家族捆绑三份独立特性；符号四用途过载。仅"值可空推断"有轻度 VB 化（复用既有 `Integer?` 概念），故不判 1 | 已检查 | 违反原则 3 / 5 / 7 / 8；`??` 未 VB 化且与 `If(a,b)` 冗余；`?` 与 `?.` tokenizer 冲突未分析 |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节齐全但：标题/Summary 称"后置"而示例为**中缀**（自相矛盾）；示例行尾多余句点（原文笔误未处理）；无 BNF/优先级/结合性；未决问题 4 个关键点被轻描淡写且部分可定未定；状态行占位链接 | 已检查 | 术语矛盾；无文法；无兼容性分析；未决问题≥4 关键点（效果封顶 3）；占位符状态 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。得=对齐 C# 直觉、更短拼写（雷/光有限）；失=与主线 NRT 推迟立场断裂、符号过载（风）、`?` 后缀与 `?.` 解析阴影（暗）；`lookup()?` 隐蔽行为变化（暗）。文档未权衡这些维度 | 已检查（预测待定） | 暗风险（tokenizer 阴影、未来重编译漂移）被文档识别但未对冲；与主线断裂无讨论；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源=Anthony section 12 标注准确；但 `??` 是 C# 借鉴未显式声明、NRT 依赖一笔带过（18.16 自认修订中未引）；`?` 家族混入多个无关能力未说明边界；`If(a,b)` 既有替代完全未提 | 已检查 | C# 借鉴未标注；NRT 依赖未展开；与既有 `If` 的关系缺失是最重的成分缺漏 |

### 设计原则对照

- **与 VB 基因：偏离**。违反「不引入第二种做事方式」（原则 3，`??` vs `If(a, b)`）、「读起来像英语」（原则 5，符号 vs 英文函数名）、「避免隐蔽语义变化」（原则 7，`lookup()?` 一个字符翻转严格度）、「不与既有语法冲突」（原则 8，`?` vs `?.` / `Integer?`）。
- **与主线关系：Anthony 独立激进延伸，与主线冲突**（2.3 对照表：`Null 安全全家桶` — 主线保守、Anthony 大范围铺开；主线对 NRT 明确推迟）。
- **破坏性变更：直接无，间接有**。`??`/`True?`/`String?`/`lookup()?` 今天全部是错误，直接破坏为零；但 `?` 后缀使既有 `?.` 表达式进入双切分灰色地带，且 NRT 定稿后重编译可能使 nullability 状态漂移（2018.02.07 Part 2 警示过表面标注传播问题）。

### 总评

- **达成程度：部分达成**——痛点场景真实（获取可能为空的数据需内联兜底），但方案主效果被既有 `If(a, b)` 覆盖；`?` 家族的核心成员依赖未定型 NRT 或规则空白。
- **LDM 三态建议：Table**（`??` Reject / `True?` Consider / `String?` Table / `lookup()?` Reject）。
- **主要问题**：① `??` 是 `If(a, b)` 的第二拼写（原则 3 致命）；② 标题"后置"与中缀示例自相矛盾（术语）；③ `?` 符号四用途过载且与 `?.` tokenizer 冲突；④ NRT 地基未定（主线推迟 + Anthony 自认修订中）；⑤ `lookup()?` 的"严格检查"规则未定义 = 无法验收；⑥ 与 `If(a, b)` 的逐场景等价关系完全未讨论。

### 返工建议

- **补充章节**：文法专节（`??` 的优先级/结合性/BNF；`?` 后缀与 `?.` 的 tokenizer 消歧）；与 `If(a, b)` 的逐场景等价/差异表（短路、可空值类型、dominant-type 推断、组合 `?.`）；Compatibility 分析（既有 `?.` 表达式的重解析、NRT 定稿后的重编译漂移、langversion 门控）；`lookup()?` 的检查规则完整定义。
- **补充证据**：`If(a, b)` 二参形式的使用率与发现性数据（支持 `??` 重估的唯一证据路径）；`??` 请求量统计；最小原型（`??` 或 `True?` 单独）的语义模型验证；术语修正（删除"后置"或改为"中缀"，清理示例行尾句点）。
- **未决问题处理**：拆分为四份独立评估（`??` / `True?` / `String?` / `lookup()?`），各自给三态；把 `String?` 的结论与 `null-literal` 会议及 Anthony NRT 修订绑定；`True?` 与 `?=`（null-equality）协调符号家族。
- **设计探索**：若未来重估 `??`，先探索「改进 `If(a, b)` 发现性」的零语言改动路径（analyzer + 文档 + 补全强化），再谈新运算符。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的 C# 生态考量，不改写正文。评估对象与正文一致：`??` 中缀空合并运算符与 `?` 空指示符家族。所有 C# 引用均逐字取自 csharplang 镜像并标路径；无法核实的标注 **Suspect** 或 **OPEN QUESTIONS**。

### 相关 C# 现实方向

本提案在 C#/CLR/.NET 生态中的对应物，主要不是低层互操作主线（Span/指针/COM/AOT），而是 **`??` 运算符现状**、**C# 8 `??=`** 与 **C# 8 可空引用类型（NRT）**——`??` 在 C# 生态里同时是「求值运算符」与「NRT 流分析运算符」，这正是与 VB `If(a, b)` 对比时最容易漏掉的一维。

**`??` 与 `??=` 的现状**：

- `??` 是**非重载二元运算符**，自 C# 2 随可空值类型引入（Language-Version-History 的 C# 2 条目列有 "Nullable types"；`??` 未在该文件单列，见 OPEN QUESTIONS）。C# 8 的 null-coalescing-assignment 提案确认其非重载本质：「This proposal adds a non-overloadable binary operator to the language that performs this function.」→ `proposals\csharp-8.0\null-coalescing-assignment.md`（Motivation）
- `??=` 在 C# 8 加入，Language-Version-History C# 8.0：「`??=` allows conditionally assigning when the value is null.」→ `Language-Version-History.md`
- C# 8 同时放宽 `??` 左操作数类型要求：「As part of this proposal, we will also loosen the type requirements on `??` to allow an expression whose type is an unconstrained type parameter to be used on the left-hand side.」→ `proposals\csharp-8.0\null-coalescing-assignment.md`（Summary）

**NRT 把 `??` 变成流分析运算符**（本附录最重要的 C# 现实）：

- C# 8 NRT spec 显式给出 `??` 的 null-state 规则：「`E1 ?? E2` has the same null state as `E2`」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（§The null-coalescing operator）——即 C# 中 `maybe-null ?? not-null` 的整个表达式是 **not-null**，`??` 参与可空性流传播。
- C# LDM 记录同向：「…we know that the null-coalescing assignment cannot produce null. This is how the existing null-coalescing operator works. The null-coalescing operator also specifies that, in `x ?? y`, the conversion to a non-nullable version of `x` is preferred if it is available.」→ `meetings\2018\LDM-2018-07-16.md`
- NRT 目标（首节两枚 bullet）：「Allow developers to express whether a variable, parameter or result of a reference type is intended to be null or not.」「Provide warnings when such variables, parameters and results are not used according to that intent.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 元数据（Metadata representation）：「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」→ 同上

**C# 对 `??` 类型推断的保守态度**：

- target-typed `??` 提案（issue #2473）被移入 Likely Never：「…it doesn't actually work well in practice, as `??` has a number of complicated rules around type resolution already and what part should be target-typed is confusing.」→ `meetings\2020\LDM-2020-09-09.md`——C# 自认 `??` 的类型解析已足够复杂、不愿再加深。这与正文第 1 条「`??` 的优先级/结合性/文法未定义」的批评互为镜像：两边都把 `??` 的类型/解析复杂度当成真实负担。

**C# 未来方向：指针空合并（inactive，unsafe 通道）**：

- `proposals\inactive\pointer-null-coalescing.md` 提议把 `??`/`??=` 扩展到指针：「This proposal extends support of a commonly used feature in C# (?? and ??=) to unsafe code and pointer types specifcally [sic].」→ `proposals\inactive\pointer-null-coalescing.md`（Summary）——属 unsafe evolution 支线；同仓库 unsafe-evolution 明确 VB 无此需求：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节）。此方向对 VBScript.NET **无约束**。

### 现实 vs 提案

- **求值语义：兼容**。C# `??` 的求值语义（短路、可空值类型、非重载、结果类型转换）与 VB `If(a, b)` 在「求值」维度重合；RESOLUTION 对 `??` 的 Reject 在 C# 生态下不构成冲突——C# 有 `??` 是因为 C# 没有 `If(a, b)`。C# 用运算符、VB 用函数名，是两条语言各自的基因选择。
- **null-state 维度：需桥接**。正文 RESOLUTION 说「`If(a, b)` 与 `??` 在求值语义、短路、可空值类型处理、dominant-type 推断上没有差异」——这句在**求值**维度成立，但在 **NRT null-state** 维度**不成立**：C# `??` 是流分析一等公民（`E1 ?? E2` has the same null state as `E2`，且结果类型偏向非空），而 VB `If(a, b)` 不参与任何 null-state 追踪（VB 没有 NRT）。若 VBScript.NET 未来推进 `String?`（正文 RESOLUTION 第 3 条 Table 分支），`If(a, b)` 需要新增与 `??` 等价的 null-state 规则（左 maybe-null + 右 not-null → not-null），否则「`If(a, b)` 完全覆盖 `??`」的等价性在可空性维度破功。
- **`??=` 缺口：无 VB 对应**。C# 8 的 `??=`（复合赋值）在 VB 无对应运算符；等价写法 `x = If(x, y)` 语义可达但拼写不同。跨语言习惯差异需文档化，不构成语言级障碍。
- **类型推断的「复杂度」证据方向一致但论据不同**：C# 拒绝 target-typed `??` 的论据是「已复杂、不再加深」；本提案 Reject `??` 的论据是「语义已被覆盖」。两者都认为 `??` 不值得新增复杂度，但 C# 是「保守于既有」、VB 是「无增量价值」。
- **低层 interop：关系弱，无直接冲突**。本提案不涉及 Span/指针/函数指针/COM/AOT 任意主线；C# 把 `??` 扩展到指针是 inactive/unsafe 通道，与 VB 无 unsafe 的现状无关（unsafe-evolution 已对 VB 明确不扩展）。

### 对 VBScript.NET 的适应建议

- **默认安全/按需动态**：把「识别 C# NRT 元数据」作为 .vbx 的默认安全基础设施。C# 把 nullability 以属性形式写入元数据（见 speclet 原文）；.vbx 若要消费 C# 库的可空性（波浪线、跨语言空安全校验），必须识别这些标注，否则 `If(a, b)` 空合并即便语义正确，也无法参与「调用 C# 可空 API」的警告链路。OPEN QUESTION：具体属性名（实现层 `NullableAttribute`/`NullableContextAttribute`）属 dotnet/runtime，speclet 仅写 "attributes"，本镜像未收录。
- **若推进 NRT：给 `If(a, b)` 定义 null-state 规则**，而不是引入 `??`。最省事的对齐是复刻 C# 规则（`If(a, b)` 结果的可空态 = `b` 的可空态，且结果类型偏向非空），一行规则即可让既有 `If` 承担 C# `??` 在流分析里的角色——这是「不引入第二种做事方式」（原则 3）在 NRT 时代的延续。
- **source-gen 桥**：`If(a, b)` 的代码生成与 C# `??` 同构（`dup; brtrue` 短路模式），编译到受管程序集时跨语言互操作无障碍，无需语法桥或 IL 适配。
- **文档映射表**：提供 `??`↔`If(a, b)`、`??=`↔`x = If(x, y)`、`customer?.Address ?? "unknown"`↔`If(customer?.Address, "unknown")` 的映射，帮助 C# 迁移者；脚本侧保持 VBScript 的 `If` 三参习惯，不引入 `??` 符号。
- **识别特征标志历史**：C# 8 曾用 `[NonNullTypes]` 属性作为 NRT opt-in（LDM-2018-07-16 讨论过，后以 `#nullable` 指令实现）；未来 unsafe-evolution 的属性（RequiresUnsafeAttribute/MemorySafetyRulesAttribute）虽不涉本提案，但同属「.vbx 需识别的新元数据」清单（决策文件 M8）。

### 对既有 RESOLUTION/三态判定的影响

- **不改变判定**：仍为 Table（`??` Reject / `True?` Consider / `String?` Table / `lookup()?` Reject）。C# `??` 的求值语义确被 `If(a, b)` 覆盖，Reject 成立。
- **新增第二条「重开 `??`」的证据路径**：正文 RESOLUTION 的重开条件是「发现性数据」。C# 现实给出**另一条**：若未来 `String?`（NRT）被采纳，`If(a, b)` 与 `??` 在 null-state 维度不再等价，届时重开 `??`（或等价地给 `If` 加 null-state 规则）有新的现实依据。这条路径不依赖使用率数据，依赖 NRT 是否落地。
- **强化 `String?` 的 Table 判断**：C# NRT 2026 年已是主流，但它是「`?` 后缀 + 元数据属性 + 全链路 IDE」的整体工程；VB 若跟进必须复刻整条属性管线。附录确认「等 NRT 定型再动」仍是正确策略，且 NRT 一旦落地会连带影响 `??` 的评估（见上条）。
- **`True?`（Consider）不受影响**：`Nullable(Of Boolean)` 与 NRT 无关；C# 生态对该方向无新增约束或先例。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「This proposal adds a non-overloadable binary operator to the language that performs this function.」→ `proposals\csharp-8.0\null-coalescing-assignment.md`（Motivation）
- 「As part of this proposal, we will also loosen the type requirements on `??` to allow an expression whose type is an unconstrained type parameter to be used on the left-hand side.」→ `proposals\csharp-8.0\null-coalescing-assignment.md`（Summary）
- 「`??=` allows conditionally assigning when the value is null.」→ `Language-Version-History.md`（C# 8.0）
- 「`E1 ?? E2` has the same null state as `E2`」→ `proposals\csharp-8.0\nullable-reference-types-specification.md`（§The null-coalescing operator）
- 「…we know that the null-coalescing assignment cannot produce null. This is how the existing null-coalescing operator works. The null-coalescing operator also specifies that, in `x ?? y`, the conversion to a non-nullable version of `x` is preferred if it is available.」→ `meetings\2018\LDM-2018-07-16.md`
- 「Allow developers to express whether a variable, parameter or result of a reference type is intended to be null or not.」「Provide warnings when such variables, parameters and results are not used according to that intent.」→ `proposals\csharp-8.0\nullable-reference-types.md`
- 「Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them.」→ 同上
- 「…it doesn't actually work well in practice, as `??` has a number of complicated rules around type resolution already and what part should be target-typed is confusing.」→ `meetings\2020\LDM-2020-09-09.md`
- 「This proposal extends support of a commonly used feature in C# (?? and ??=) to unsafe code and pointer types specifcally [sic].」→ `proposals\inactive\pointer-null-coalescing.md`（Summary）
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节）

### OPEN QUESTIONS / Suspect

- **OPEN QUESTION**：`??` 的精确引入版本。本附录据 C# 2 的 "Nullable types" 条目（Language-Version-History）与业界通识标注为 C# 2.0（任务提示误记为 C# 6），但该文件未将 `??` 单列，镜像内无逐字原文核实「`??` 随 C# 2 引入」。
- **Suspect**：正文引用的「C# 的 `??` 是右结合、优先级低于 `||`」（出自 LDM-2014-04-01 引 Eric Lippert 博客）——csharplang 的 `spec\expressions.md` 只是指向外部 csharpstandard 的链接索引（§11.14 The null coalescing operator），优先级/结合性正文不在镜像内，本附录未能逐字核实。
- **OPEN QUESTION**：NRT 标注的具体属性名（`NullableAttribute` / `NullableContextAttribute`）来自 dotnet/runtime 实现，csharplang 镜像的 speclet 仅写 "attributes"；`.vbx` 若做元数据识别需以 dotnet/runtime 为准。
- **关系弱申明**：本提案与 C# 低层 interop 主线（T2 Span/ref、T3 指针/函数指针、T4 COM、T5 AOT/trimming、T6 source-gen）**无直接冲突**；真正的对应物是 `??`/`??=` 运算符现状与 NRT null-state 规则。
