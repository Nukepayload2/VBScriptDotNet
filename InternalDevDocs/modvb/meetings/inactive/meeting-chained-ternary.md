# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天我们审查的是一份 **inactive** 建议——链式三元表达式 `? If(...)`（`proposal-chained-ternary.md`，Anthony 第 18 章实验性想法）。第 18 章是 Anthony 明确标注"还需要时间酝酿"的一批想法，我们上几场会议已经用同一套眼光处理过 `Case Else` 变量（18.3）与 `ShapeOf` 模式匹配（第 7 章）——任务是认真决定：**它值得激活，还是保持搁置？**

开场十分钟我们意识到，这份建议与它所属的家族构成了一场三向对峙。链式三元声称是"`Select Case` 的轻量表达式版"（原文副标题 `Case expression lite?`，问号是 Anthony 自己加的），但它的语法形状其实是 **`If-ElseIf` 的表达式化**，不是 `Select Case` 的表达式化——它测试 N 个**彼此独立的布尔条件**，而不是对**同一个表达式**做多模式分发。这决定了它同时踩在三条我们已经在走或已裁定过的路上：`If()` 的推断（上一场 `conditional-best-common-type`，LUB 已 Table）、`Select Case` 的模式匹配演进（主线 #304 / #337 / ShapeOf）、以及 VBScript 血统里的多分支取值函数 `Switch`/`IIf`。三个方向各自有一个更好的答案，而 `? If()` 恰好都不是。

_诚实分层说明：本纪要逐句标注 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。所有引用的 vblang 主线会议决定与 issue 编号均逐字核对自 `..\..\..\vblang\meetings/` 原始文件；VBScript 内置函数 `Switch`/`IIf` 的精确行为属 VBScript 语言常识，不来自 vblang 文件，按 `Probably` 标注。找不到直接对应材料处，依据 vblang 设计原则与评价标准独立论证并显式标注。_

## Agenda

* [ModVB Proposal — 链式三元表达式（`? If` / Chained Ternary）](#modvb-proposal--链式三元表达式)

## ModVB Proposal — 链式三元表达式

_来源：Anthony D. Green 原文第 18.25 节 "Simplified "Chained" ternary expressions (Case expression lite?)"（`..\..\AnthonyDesign_wordpress.txt` L3286–3297，inactive / 实验性）。原文没有任何散文——只有一个闰年判断的代码示例、一条 "Something like this." 注释、以及副标题里的一个问号。整份建议是我们在这段代码上补齐的。_

### 场景与缺口

建议要解决的场景：写"多条件取值"时，经典 `If()` 只有两个分支；多分支要么嵌套 `If()`，要么退化成 `Select Case` 加赋值语句。Anthony 想用单个表达式直接表达多个条件分支，让"取哪个值"紧邻其条件。原文示例（`..\..\AnthonyDesign_wordpress.txt` L3290–3297）：

```vb
' Something like this.
? If(year Mod 400 = 0,
       True,
     year Mod 4 = 0 AndAlso
     year Mod 100 <> 0,
       True,
     Else,
       False)
```

结构解读：前导 `?` 表示这是新的链式形式；之后是交替出现的"条件, 值"对，逐条求值、命中即取值；结尾用 `Else` 关键字作兜底分支（对应 `Select Case` 的 `Case Else`）。

There is a real but narrow gap here。同一件事今天的两种写法都成立，只是各有代价。嵌套 `If()` 平铺不了多层分支，括号层层右缩进：

```vb
' 今天（可编译）：3 分支 = 两层嵌套，条件与取值被括号隔开。
Function IsLeapYear(year As Integer) As Boolean
    Return If(year Mod 400 = 0,
              True,
              If(year Mod 4 = 0 AndAlso year Mod 100 <> 0,
                 True,
                 False))
End Function
```

`Select Case` 语句能平铺，但无法作为表达式嵌入——在 `Return`、实参、LINQ 投影等表达式位置，`Select Case` 进不去。We 承认这个缺口真实存在：**表达式语境里的多分支取值**在 VB 里没有一等的表达方式。但本次会议的争论点在于：`? If()` 是不是填这个缺口的正确形状。

### 候选方案

**PROPOSAL A — 建议原文形态：`? If(条件, 值, ..., Else, 兜底)`。** 前导 `?` 前缀 + 交替的"条件, 值"对 + 结尾 `Else` 标记。这是建议的原样形态。

**PROPOSAL B — 无 `?` 前缀，直接扩展 `If()` 元数。** 建议的未决问题之一正是"前导 `?` 是否必要（能否直接扩展 `If()` 的参数数量）"。`If(c1, v1, c2, v2, Else, v3)`——用元数区分链式与经典三参形式，省掉 `?`。

**PROPOSAL C — 真正的"`Select Case` 表达式"。** 复用 `Select Case` 文法（`Case`、`Case Else`、范围、`Is` 比较、未来的模式），让整个语句块作为表达式产出值。这是"Case expression lite"这个名字真正指向的东西，也是与主线模式匹配方向（#304 / #337）一致的长线形态。

**PROPOSAL D — 复活 VBScript `Switch` 语义。** `Switch(expr1, value1, expr2, value2, ...)` 本就是"多分支取值"的脚本世界原型；VBScript.NET 若要这个能力，可给 `Switch` 语义补上短路求值与类型推断，而非发明 `? If()`。`Probably`（VBScript 语言常识，不在 vblang 语料内）：VBScript 的 `Switch` 按序求值、取第一个为真表达式对应的值；`IIf(expr, a, b)` 是两分支但**两个分支都被求值**（经典陷阱，VB 的 `If()` 正是为修复这一点而生）。

**PROPOSAL E — 什么都不做。** 维持嵌套 `If()` + `Select Case` 语句；用 analyzer 提示重构。2018.05.30 会议记录的欧洲行结论："there are hundreds of thousands of quiet customers each month primarily want VB to keep doing what it does now"——除非数据说话，否则少动。

### 权衡：Q&A

- **A 的 `?` 前缀是必要还是冗余？** 冗余。元数已经区分了两种形式——3 参是今天的经典三元，4 参以上只可能是链式（今天 4+ 参 `If()` 是编译错误）。`?` 不承载任何语义信息，纯粹是仪式。更糟的是它在语言里已经有太多含义：`?.`（空条件，2014 #54 批准）、注解字符串 `? "..."#Guid`（2017.10.18 讨论过，结论是 "Using attributes would be better than a special tag"）、XML 字面量。给 `?` 再叠一种含义，正中主线对"微妙字符改语义"的否决先例（见下）。
- **A/B 真的能叫 "Case expression lite" 吗？** 不能。`Select Case` 是对**单一主题表达式**做多模式分发，`Case Else` 语义上可以绑定"未匹配到的那次求值结果"（正是 `proposal-case-else-variable.md` 想显式化的东西）；`? If()` 测试的是 **N 个彼此独立的布尔条件**，`Else` 只是一个兜底值，没有任何"被测对象"可以绑定。所以它是 `If-ElseIf` 的表达式化，不是 `Select Case` 的表达式化。Anthony 副标题里的问号，我们的答案是：**不是。**
- **B 与 C 是竞争还是互补？** 竞争，且 C 在表达力上严格支配 B——`Select Case` 有 `Case Is > 5`、范围 `1 To 10`、逗号合并分支、未来的模式；B 的每个条件只能是布尔表达式。B 只赢在一个点：实现小。而"实现小"这个点，前提是结果类型推断不用从零设计——那个前提恰好不成立（见追问 #2、#8）。
- **D 的遗产分量。** VBScript 血统正是 VBScript.NET 的差异化所在。`Switch`/`IIf` 是脚本世界对"多分支取值"的原生回答，它们的坑（`IIf` 全求值）也是 VB 的 `If()` 当年要修复的靶子。`Probably`：与其发明 `? If`，不如把一个**短路求值、类型安全**的 `Switch` 语义作为 VBScript.NET 的特色资产。`Not all of us are happy` 于"复活脚本函数名"的路线——它偏离主线更远——但至少它是诚实的血统，而 `? If()` 两头都不靠。
- **E 的分量。** 对 2 分支，B 与经典 `If()` 完全重复（纯语法重复，零增益）；对 3 分支，嵌套 `If()` 只是"右缩进两层"，可读性账未必输。真正压垮 A/B 的，是结果类型推断的成本被上一场会议刚刚判了 Table（见下）。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**`?` 前缀是整场最重的语法红旗。** VB 里 `?` 已经出现在 `?.`（空条件，2014 #54 批准）与 XML 字面量中；注解字符串想法（2017.10.18 `? "{123e4567-e89b-12d3-a456-426655440000}"#Guid`）被社区以"用 attribute 而非特殊标记"否决。词法器今天遇到 `?` 后跟标识符直接报错——`? If(` 需要新的 token 语境。而主线对"用微妙字符改变含义"有过明确的否决先例，2018.05.30 #167 `Return?`：

> "We think this is a bad idea. Control flow would be altered by a very subtle character. It's not the same meaning as other uses as ? (any alteration in control flow)"

`? If()` 的 `?` 正是同一类问题：一个细微字符，把 `If()` 从"三参求值"切成"链式分派"。我们找不出一个让它免于该先例的辩护。

**元数歧义。** 假设去掉 `?`（B）：3 参 = 经典三元；4 参 = `(c1, v1, c2, v2)`（两条件无兜底）还是 `(c1, v1, Else, v2)`（一条件带兜底）？`Else` 是保留字，`(c1, v1, c2, v2)` 里没有 `Else` 字样，只能是"两条件无兜底"⇒ 引入穷尽性问题。6 参 = `(c1, v1, c2, v2, Else, v3)` 是闰年例。5、7 等奇数参既不能配对又无兜底标记 ⇒ 编译错误。这套四情形文法（3 参 / N 对无兜底 / N 对带兜底 / 奇数参）是**可定义**的，但绝不简单——而这还只是解析层。

**`Else` 作为参数表内的标记。** `Else` 是保留字，不能作普通标识符，所以不存在与用户变量名冲突；但语义模型里它既不是表达式、也不是符号——`If()` 的参数位置第一次出现"非表达式的实参"。IntelliSense、重载解析、表达式分类、pretty-lister 全部需要特殊路径。

#### 2. 角案例与边界语义

**穷尽性（无 `Else` 时全不命中取什么？）。** 建议未决问题之一。三个选项：① 编译期强制要求 `Else`（闰年例已带，但剥夺了"部分匹配 + 运行时兜底"的表达力）；② 无 `Else` 且全不命中 ⇒ 运行时抛异常（C# switch expression 先例）；③ 返回 `Nothing`/默认值。主线对 `Select Case` 的穷尽性态度明确，2018.12.19："If you mean exhaustiveness of cases in `Select Case`, we don't have this today and introducing it is backwards breaking."——表达式版本没有"落空则什么都不做"的出口，**必须**自己定义，而三个选项各有代价。

**短路保证必须写进 spec。** VB 的 `If()` 是编译器内建的特殊形式，只求值被选中的分支（与 C# `?:` 同，且正是为修复 `IIf` 全求值的坑而生）。链式必须继承逐对短路：条件按序求值，命中后只求值该分支值、跳过其余。`Probably`：这条若不写死，读者会照 VBScript `Switch` 的直觉认为所有表达式都被求值——把一个修复过的语义重新打开成坑。

**单分支退化。** 2 分支 + `Else` 的 4 参形式 `If(c1, v1, Else, v2)` 与经典 3 参 `If(c1, v1, v2)` 语义完全相同——纯语法重复。也就是说链式的**最小有效用例是 6 参（三条件）**，而"表达式语境里恰好有三个条件分支"这个场景的频次，没有任何数据。

**值类型分支。** `If(c1, New Integer, c2, New Date, Else, ...)` 的类层次公共祖先到 `System.ValueType`，推断 `ValueType` ⇒ 一切访问装箱，比 `Object` 好不了多少还加装箱罚金。上一场 `conditional-best-common-type` 会议已裁定这类推断问题的算法（LUB）为 Table——这里绕不过去。

**`Nothing` 分支不贡献候选类型。** 2014 #18 的 dominant-type 规则只对裸 `Null` 字面量做 "replace each non-nullable-value-type candidate 'T' with Nullable(Of T)"（例 `Dim x = If(b,5,Null)` ⇒ `Integer?`）；`Nothing` 无此规则。链式多分支里 `Nothing` 行为沿袭 `If()` 现状，不新增能力也不解决旧坑。

**泛型实例化。** `If(c1, New List(Of Integer), c2, New List(Of String), Else, ...)`——公共基只有 `Object`，共享的 `IList` 只有非泛型形态，推断给不出用户要的类型。与上一场 `conditional-best-common-type` 会议列出的泛型问题完全相同，未定义。

#### 3. 作用域与绑定

`Else` 标记无符号可绑——语义模型里它是"非表达式实参"，`GetSymbolInfo` 无从谈起。条件与值位置是普通表达式，无新作用域引入（不像模式变量，不产生收窄作用域）。流分析交互见追问 #4：若采纳 `TypeOf` 流分析（`meeting-typeof-flow-analysis.md` 决议），每个 (条件, 值) 对的**值区域**应按条件收窄被测变量——上一场已定 `If()` 两操作数"真操作数收窄、假操作数保持宽类型"，链式需逐对推广，v1 不承诺。值位置是 rvalue，无 ByRef 议题。

#### 4. 与既有特性的交互

- **`If()` 是内建特殊运算符，不是普通方法。** 扩展元数 = 扩展编译器内建路径；3 参行为必须一字不动（它有 2014 #46 记录的历史包袱，见追问 #5）。
- **第三种取值方式。** VB 已有 `If()`（表达式）与 `Select Case`（语句），再加链式 = 第三种。设计原则 #3（不引入"第二种做事方式"）的门槛极高，这里要跨的是"第三种"。
- **与 `conditional-best-common-type` 决议（上一场）直接冲突。** 该会议裁定：目标类型化 `If()`（PROPOSAL B）= Active（逐字引用 2014 #46："When interpreting If(x,y,z) in a context in which the desired type is known, then interpret both y and z in the context of that type."）；完整 LUB 回退（PROPOSAL A）= Table。链式的多分支结果类型 = 同一推断问题的 N 分支版——**借链式复活上一场刚判 Table 的 LUB，是跨会议一致性义务的正面违反**。
- **与 `TypeOf` 流分析（`meeting-typeof-flow-analysis.md`）**：值区域逐对收窄需定义优先级（先收窄、后目标类型化、再 dominant——沿用该会议与 `conditional-best-common-type` 会议共同要求的裁决链）。
- **与 `target-typed-conversions`（inactive，Anthony 自评"会打架"）**：谁定类型的老问题，链式不解决反而加剧。
- **表达式树**：链式降级为嵌套 `conditional` 节点，LINQ-to-Entities 可行；但降级规则必须写 spec，短路在树中要保持。

#### 5. Breaking change 与兼容性

这是本建议唯一的光亮点：**基本无破坏。** 新语法 `? If(` 是纯新增——`?` 后跟标识符今天报错；扩展元数（B）也是纯新增——4+ 参 `If()` 今天报错。3 参形式只要明确不动，任何旧代码行为不变。无 langversion 门控需求。

但我们不把这当成放行的理由。2014 #46 记录的三参 `If()` 本身就有未竟的破坏性变更包袱（目标类型化，"Note that it will be a breaking change. We decided in LDMs in years past that this would nevertheless be good for the language."）——那件事我们上一场刚批准为 Active。在目标类型化 `If()` 落定之前，给同一个内建运算符再加一重元数语义，等于在未完工的地基上盖第二层。顺序反了。

#### 6. Option Strict / 编译选项分叉

- **Strict Off**：分支值可为 `Object`，结果类型推断走宽松/晚期绑定路径。
- **Strict On**：必须公共类型，无公共类型时报错。
- 两条路径结果不同；`Else` 缺失时两路径的兜底行为需保持一致。上一场对 `If()` LUB 的裁定（"严格受益、宽松受损"）同样适用于这里，但这里连算法都没定。

#### 7. IDE / IntelliSense

可变元数内建运算符的签名帮助展示（"条件, 值, 条件, 值, Else, 值"）；`Else` 标记的着色（关键字 vs 普通标识符）；奇数参错误的文案；重构"嵌套 `If()` ↔ 链式"；诊断"此链式可改用 `Select Case` 表达式"。`?.` 的语法高亮与 `? If` 的词法区分需要确认。这些在原型中验证之前，特性不算设计完——而本建议连原型都没有。

#### 8. 数据 / 普遍性

唯一示例是闰年——教科书玩具。没有任何用户请求、没有 mailing list 讨论、没有真实代码测量。对比 2014 #46（`Fix the ternary If operator`）有三段逐字记录的真实用户抱怨（"I don't know what Nothing means."等）——链式三元一条都没有。2018.05.30 欧洲行的话在这里有分量："hundreds of thousands of quiet customers ... primarily want VB to keep doing what it does now"。`Suspect`：表达式语境多分支取值的真实占比远低于 `Select Case` 语句与嵌套 `If()`，而这两者今天已覆盖绝大多数用例。

#### 9. 更简替代

- **嵌套 `If()`**：今天可用，2 分支零成本、3 分支可接受。代价只是右缩进。
- **`Select Case` 语句 + 赋值**：最 VB、最直白、支持范围/比较/逗号合并，只是进不了表达式位置。
- **扩展元数（B）**：比 A 少一个 `?`，但仍需穷尽性 + 推断 + IDE 三件套。
- **`Select Case` 表达式（C）**：表达力支配 B，与主线模式匹配方向一致，是长线正确形态。
- **VBScript `Switch` 复活（D）**：VBScript.NET 特色资产，短路 + 类型安全。
- **Analyzer / 重构**：可提示"此 If 链可平铺"或"改用 `Select Case`"，但**不能改变表达式能力**——只能当脚手架，替代不了语言特性（与之前所有会议结论一致）。
- 结论：`? If()` 是上述所有方案里**最差**的形态——比 B 多一个 `?`、比 C 少表达力、比 D 少遗产。

#### 10. 成本 / 优先级

- A：lexer（新 `?` token 语境）+ parser（参数表内 `Else` 标记）+ binder（多分支 conditional 降级 + 结果类型算法）+ IDE。中成本。
- B：省 lexer，binder/IDE 同。
- 真正的大头是结果类型算法——与 LUB 同量级，而 LUB 刚被上一场判 Table。为一个"平铺括号"的改进付一套新推断算法 + 新文法 + 新 IDE 路径，成本不成比例。
- 优先级：远低于已 Active 的目标类型化 `If()`、模式匹配、`Select Case` 表达式。

#### 11. 运行时 / CLR 硬约束

无。纯编译期降级为嵌套 conditional；短路用分支指令；表达式树用嵌套 `conditional` 节点；不触达 CLR 存储规则，PEVerify 无碍。值类型 → `ValueType` 的装箱是性能罚金而非正确性障碍。

#### 12. 值不值得做

逐维打分。**价值**：低——薄改进、无数据、单一玩具示例、替代方案众多且多数更优。**成本**：中——新文法 + 推断算法 + IDE，其中推断算法与刚被 Table 的 LUB 同量级。**风险**：中——`?` 语法族冲突（撞 #167 否决先例）、与上一场决议的跨会议断裂、教学负担、与 `Select Case` 表达式抢地盘。综合结论很清晰：**不值得以当前形态做。** 这不是"Fantastic idea, and too hard to do"（2018.02.07 #211 式热情未灭）；这是"价值不足、形态错误、时机未到"三合一。能力留活口，形态否决。

### VB 基因对照

- **永不破坏既有代码（原则 #1）**：通过——新语法、纯新增，是全程唯一的亮点。
- **保持 VB-like（原则 #2）**：违反——`? If(` 前缀没有任何 VB 风味；`Else` 作参数标记前所未见。
- **不引入"第二种做事方式"（原则 #3）**：顶格违反——第三种取值方式（`If()` + `Select Case` + `? If()`）。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 8 已有成熟的 switch expression，本建议不与它对齐，也没给出偏离的"充分理由"——反而用一个更弱的形态抢先占住同一语义位。
- **读起来像英语、对新手友好（原则 #5）**：违反——`?` 前缀没有英语对应，新手无法解释"Why a question mark?"。
- **不为边缘场景加特性（原则 #6）**：违反——无数据、无普遍性，闰年玩具示例撑不起一个语法。
- **避免隐蔽语义变化（原则 #7）**：`? If(` 是新语法不改变既有行为，但它撞上 #167 的否决逻辑——"微妙字符改变含义"。
- **不与既有语法冲突（原则 #8）**：违反——`?` 撞 `?.`、注解字符串、XML。
- **消除常见样板（原则 #9）**：部分通过——平铺嵌套 `If()` 的括号，但增益薄、频次未证。
- **冗长只在有用时是美德（原则 #10）**：违反——`?` 前缀是无用冗长，元数已足够区分。

**与主线关系（对照表 2.3）**：Anthony 独立延伸，且**部分与主线冲突**。主线把 `Select Case` 向模式匹配演进——2018.05.30 #304 逐字："Will consider part of pattern matching."（标签 `Pattern Matching` 与 `LDM Reviewed: No Plans`）；2018.12.19 家族文法（`Matches` 关键字、逗号保持 `Case` 专属）；2014.04.23 的"Urgent question: do we need to make changes to the CURRENT design of primary constructors and Select Case so that we don't block off a future world of pattern-matching?"。`? If()` 以"Case expression lite"之名抢占这个方向的语义位，但形态更差——这是对主线方向的一致性破坏，不是延伸。与 `If()` 推断（#46 / dominant-type / LUB）的关系见追问 #4：上一场刚裁定 LUB=Table，本建议不得借道复活。

### RESOLUTION:

1. **否决 `? If()` 语法形态（PROPOSAL A）。** 三条独立理由叠加：① `?` 前缀冗余（元数已区分）且撞 `.` 空条件、注解字符串、XML——命中 2018.05.30 #167 `Return?` 的否决逻辑（"Control flow would be altered by a very subtle character"）；② `Else` 作参数表内标记无先例、语义模型无法分类；③ 它自称 "Case expression lite" 是误称——它是 `If-ElseIf` 的表达式化（N 个独立布尔条件），不是 `Select Case` 的表达式化（单主题多模式分发）。
2. **"表达式语境的多分支取值"能力 = Table，不否决其价值。** 归属候选：扩展 `If()` 元数（B）/ `Select Case` 表达式（C）/ VBScript `Switch` 语义复活（D）——三者都优于 A，但都不在本次激活。复活必须**更换形态**，`? If()` 不再被考虑。
3. **长期形态偏好 PROPOSAL C（`Select Case` 表达式）。** 它与主线模式匹配方向（#304 / #337 / ShapeOf）一致，表达力支配 B，且能自然继承 `Select Case` 的范围/比较/逗号合并文法。`? If()` 的存在不应阻塞 C 的讨论。
4. **结果类型推断是硬闸门。** 链式（无论 A/B/D）的结果类型 = 多分支 dominant/LUB 问题。上一场 `conditional-best-common-type` 决议：目标类型化 `If()`=Active、LUB 回退=Table。**链式复活必须以该决议落地为前提，不得借道复活已 Table 的 LUB。** 跨会议一致性义务，逐字执行。
5. **若未来走 PROPOSAL B（扩展元数），必须满足**：3 参行为一字不动；穷尽性明示（无 `Else` ⇒ 编译期强制 `Else`，或运行时异常，二选一写入 spec）；短路求值写入 spec（防 VBScript `Switch` 全求值坑复刻）。
6. **不启动任何实现工作。** 建议维持 inactive；标注"形态否决、能力 Table"。

### Implication:

- 在 `proposal-chained-ternary.md` 状态区记录本裁定：`? If` 形态 Rejected，能力 Table，复活需更换形态。
- 起草一份 VBScript `Switch`/`IIf` 语义对比表（`Probably` 层级），作为 D 路线（VBScript.NET 特色 `Switch`）的证据底稿。
- 发起数据调查：真实代码库中"表达式位置多分支取值"的占比，量化 B/C 的验收数据。
- 在 `Select Case` 表达式 / 模式匹配工作项下挂一条 follow-up："表达式形态的多分支取值（`Select Case` 表达式）评估"。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：扩展 `If()` 元数后，4 参无 `Else` 的穷尽性规则（编译期强制 vs 运行时异常 vs `Nothing`）——这是 B 复活的第一道门。
- `OPEN QUESTIONS`：`Else` 若保留为标记，语义模型与 IDE 对"非表达式实参"的分类。
- `OPEN QUESTIONS`：与 VBScript `Switch` 的关系——`Switch` 的全求值行为在 VBScript.NET 里是修复（改短路）还是兼容（保留）？`Probably`：应修复，与 `If()` 修复 `IIf` 同构。
- `TODO`：量化"表达式语境多分支取值"频次；量化"三条件及以上"在其中的占比（链式的最小有效用例）。
- `Follow-up`：`conditional-best-common-type` 的 LUB（A）若复活，链式一并重新评估；`Select Case` 表达式建议启动时，本建议归档为其前史。

### 状态

- **LDM 状态：建议整体 Consider → Table（能力）；`? If()` 形态 LDM Rejected。**
- **三态判定：Table** — 能力（表达式语境多分支取值）真实但窄、可用替代众多；形态 `? If()` 因 `?` 冲突、`Else` 标记、误称三宗罪被否决。复活信号明确（见下），当前不值得激活。

**激活所需信号**（全部满足才重新评估）：① 结果类型算法按 `conditional-best-common-type` 决议落地（目标类型化 Active、LUB Table 状态清晰）；② 出现"`Select Case` 表达式"或"扩展 `If()` 元数"的正规建议并推进；③ 真实代码数据证明"表达式语境三条件及以上取值"的频次；④ 语法形态改用 B/C/D 之一，不再是 `? If()`。

---

## 附录：特性评价

# 建议评价报告：proposal-chained-ternary.md

## 评价对象

- 建议：proposal-chained-ternary.md — 链式三元表达式（`? If(条件, 值, ..., Else, 兜底)`，"`Select Case` 表达式轻量版"）
- 来源：Anthony 原文第 18.25 节（`..\..\AnthonyDesign_wordpress.txt` L3286–3297，inactive / 实验性；原文只有一个闰年代码示例，无任何散文）
- 配方目标：用单个表达式直接表达多个条件分支，让"取哪个值"紧邻其条件；平铺嵌套 `If()` / 免去 `Select Case` 语句

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。示例（闰年）能演示"平铺"这一**表面**改进，但改进只是减少括号嵌套、不可量化；无原型、无数据；多分支取值的场景早被嵌套 `If()` 与 `Select Case` 覆盖 | 已提供（状态行 Prototype/Implementation/Specification 全部为占位链接，无运行证据） | 唯一示例是玩具；"表达式语境"的独有增益无真实用例支撑；5 个未决问题 ≥4 ⇒ 核心语法未定型 = 效果证据封顶 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`?` 前缀 + 参数表内 `Else` 标记是纯外来形态，且撞既有 `?` 语法族（`.`, `#Guid` 注解, XML）；把"穷尽性规则"作为未决负担捆绑进来；自称"Case expression lite"实为 If-ElseIf 表达式化，与 `Select Case` 基因脱节 | 已检查 | 违反 #3/#5/#8（第三种做事方式、非英语可读、语法冲突）；未对齐 C# switch expression 也未给偏离理由 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节结构齐全、示例与原文逐字一致、5 个未决问题诚实具体（≥4 个如实列出是加分，品质不直接扣）；但 Detailed design 是草图级——无文法/元数规则/优先级链；无 Compatibility 分析；无 Option Strict 分叉；状态行占位链接 | 已检查 | 核心语法（`?`、`Else`、元数）停在"结构解读"级，达不到"懂编译器者能实现"；Drawbacks 一句"与三参 If() 并存带来教学负担"未展开 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风（与上一场 conditional-best-common-type 的 LUB=Table 决议断裂、`?` 语法族冲突、与主线 Select Case→模式匹配方向抢地盘）受损且文档无权衡；雷（lexer/parser/binder/IDE 四件套）为薄改进不成比例；光/水未激活——未识别 VBScript `Switch`/`IIf` 资产 | 已检查（预测待定，实际影响须"已采纳"后定） | 破坏性变更虽小，但"一致性断裂"风险被 Drawbacks 完全忽略；对主线的语义位抢占无对冲 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。主来源标注准确（inactive / 实验性 / 第 18 章）；但血统与影响分析缺失：未标与 VBScript `Switch`/`IIf` 的继承关系（对 VBScript.NET 最相关的材料）、未标与 vblang #46 / dominant-type 机制的关系、未提 C# switch expression 的成熟形态对照 | 已检查 | 无杂质但来源欠标注；把"多分支取值"写成新发明，实际是脚本世界与 C# 8 都已覆盖的能力，先例继承关系不透明 |

## 设计原则对照

- **与 VB 基因：部分一致、多处偏离。** 一致：原则 #1（新语法、纯新增、无破坏——全程唯一亮点）、#9（消除嵌套括号样板）。偏离：#2（`? If(` 不像 VB）、#3（第三种取值方式）、#5（`?` 无英语语义）、#6（边缘场景无数据）、#7（撞 #167 "微妙字符"否决先例）、#8（`?` 语法族冲突）、#10（`?` 前缀无用冗长）。
- **与主线关系：Anthony 独立延伸，且部分与主线冲突。** 主线把 `Select Case` 向模式匹配演进（#304 "Will consider part of pattern matching."、2018.12.19 家族文法、ShapeOf）；`? If()` 以更弱形态抢占同一语义位 = 一致性破坏而非延伸。与 `If()` 推断（#46 / dominant-type / LUB）无衔接，反而依赖刚被本系列会议判 Table 的 LUB。
- **破坏性变更：基本无**（新语法、4+ 参 `If()` 今天报错）——这是相对同类建议（如 conditional-best-common-type）最干净的一点；但未做 langversion/警告策略分析，且若走 B 路线需保证 3 参行为一字不动。

## 总评

- **达成程度：未达成（形态否决）。** 底层能力（表达式语境多分支取值）真实但狭窄、可替代；`? If()` 形态因"`?` 冲突 + `Else` 标记无先例 + 'Case expression' 误称"被否决。唯一实质亮点是零破坏。
- **LDM 三态建议：Table（能力）/ Reject（形态）。** 形态 `? If()` 直接否决、留档；能力归属候选为扩展 `If()` 元数（B）、`Select Case` 表达式（C）、VBScript `Switch` 复活（D），均在结果类型算法（LUB/目标类型化）落地与数据证明确认后才有复活可能。
- **主要问题**：① `?` 前缀冗余且与 `?.`/注解字符串/XML 冲突，撞 #167 否决先例；② 结果类型推断依赖已 Table 的 LUB，跨会议一致性断裂；③ "Case expression lite"是误称——实际是 If-ElseIf 表达式化，与主线 Select Case→模式匹配方向抢地盘；④ 无数据、无原型、单一玩具示例；⑤ 未识别 VBScript `Switch`/`IIf` 资产（对 VBScript.NET 最相关的材料）。

## 返工建议

- **补充章节**：文法（参数表 BNF 或 spec 级规则：3 参 / N 对无兜底 / N 对带兜底 / 奇数参四情形）；穷尽性规则；短路求值语义；Compatibility 分析（3 参行为不变声明）；Option Strict 分叉。
- **补充证据**：若走 B，最小原型（扩展元数、3 参不动、短路验证）；"表达式位置多分支取值"与"三条件及以上"的真实代码频次数据；VBScript `Switch`/`IIf` 语义对比表。
- **未决问题处理**：① 前导 `?` 删除（元数已区分）——建议直接移除并改走 B/C/D；② `Else` 标记文法在 B 路线下保留为保留字标记、在 C 路线下并入 `Case Else`；③ 穷尽性——建议选"无 `Else` 时运行时异常"或"编译期强制 `Else`"，写死进 spec；④ 结果类型——以 conditional-best-common-type 决议（B=Active / A=Table）为前置，不在本建议内重新发明；⑤ `AndAlso`/`OrElse` 在条件中本来合法（布尔表达式），无新限制。
- **设计探索**：将能力重写为"`Select Case` 表达式"建议（复用 `Select Case` 文法 + `Case Else` + 模式匹配家族）；或独立撰写"VBScript.NET 特色 `Switch`"建议（短路 + 类型安全 + 穷尽性）作为 D 路线的正式化。

---

## 附录：C# 生态与互操作考量

_范围与诚实说明：本提案是纯表达式层语法特性——正文追问 #11 已裁定「运行时/CLR 硬约束：无」，编译期降级为嵌套 conditional 分支，不触达 CLR 存储规则、不引入新元数据。因此本附录的"互操作"对象不是 CLR 类型/元数据，而是**语言形态与生态方向**：C# 如何填充同一个「表达式语境多分支取值」缺口，以及 VBScript.NET 若复活该能力应如何与 C#/.NET 现实对齐。以下 C# 原文全部逐字核对自 `..\..\..\csharplang` 镜像；镜像内 `spec\` 仅为链接索引、正文已迁至 dotnet/csharpstandard，故规范正文级引文在镜像内无法给出处，按 `Probably` / `OPEN QUESTIONS` 标注。_

### 相关 C# 现实方向

C# 生态对「多条件取一值」的对应走向不是扩展 `?:`，而是**另起炉灶的 switch expression**：

1. **条件运算符 `?:` 保持严格二元，C# 从未扩展元数。** 语法生产式为 `conditional_expression : null_coalescing_expression | null_coalescing_expression '?' expression ':' expression`（→ `proposals\conditional-operator-access-syntax-refinement.md`）。一条件、一真一假两个操作数——不存在"第 4 参、第 6 参"概念。这与本提案 PROPOSAL B（扩展 `If()` 元数）在 C# 无任何先例形成对照。
2. **C# 8 switch expression 是「表达式语境多分支取值」的正统形态。** 逐字（→ `proposals\csharp-8.0\patterns.md`）：
   - 动机定位：「A *switch_expression* is added to support `switch`-like semantics for an expression context.」（line 266）
   - 表达式约束：「The *switch_expression* is not permitted as an *expression_statement*.」（line 293）
   - 结果类型：「The type of the *switch_expression* is the *best common type* (§12.6.3.15) of the expressions appearing to the right of the `=>` tokens of the *switch_expression_arm*s if such a type exists and the expression in every arm of the switch expression can be implicitly converted to that type. In addition, we add a new *switch expression conversion*, which is a predefined implicit conversion from a switch expression to every type `T` for which there exists an implicit conversion from each arm's expression to `T`.」（line 297）
   - 短路/首臂命中求值：「At runtime, the result of the *switch_expression* is the value of the *expression* of the first *switch_expression_arm* for which the expression on the left-hand-side of the *switch_expression* matches the *switch_expression_arm*'s pattern, and for which the *case_guard* of the *switch_expression_arm*, if present, evaluates to `true`. If there is no such *switch_expression_arm*, the *switch_expression* throws an instance of the exception `System.Runtime.CompilerServices.SwitchExpressionException`.」（line 303）
3. **穷尽性裁定：警告 + 运行时抛专用异常，非编译期错误。** 逐字（→ `meetings\2018\LDM-2018-03-28.md`）：「Should non-exhaustiveness be an error or a warning? If warning, what should happen at runtime?」裁定：「For now, it's a warning, and if you get there we throw a new exception type for this.」配套 spec（→ `proposals\csharp-8.0\patterns.md` line 301）：「The compiler shall produce a warning if a switch expression is not *exhaustive*.」
4. **结果类型推断：C# 走「公共类型 + 目标类型化转换」双机制，不用纯 LUB。** C# 9 目标类型化条件表达式逐字（→ `proposals\csharp-9.0\target-typed-conditional-expression.md`）：

   > For a conditional expression `c ? e1 : e2`, when
   > 1. there is no common type for `e1` and `e2`, or
   > 2. for which a common type exists but one of the expressions `e1` or `e2` has no implicit conversion to that type
   >
   > we define a new implicit *conditional expression conversion* that permits an implicit conversion from the conditional expression to any type `T` for which there is a conversion-from-expression from `e1` to `T` and also from `e2` to `T`. It is an error if a conditional expression neither has a common type between `e1` and `e2` nor is subject to a *conditional expression conversion*.

   且该提案承认 `?:` 与 switch expression 的推断并不一致（line 56）：「This approach does have two small downsides. First, it is not quite the same as the switch expression:」——`M(b ? 1 : 2); // calls M(long)` 而 `M(b switch { true => 1, false => 2 }); // calls M(short)`。
5. **`?` 记号在 C# 同样过载，C# 已为之引入解析规则。** 逐字（→ `proposals\conditional-operator-access-syntax-refinement.md`）：「The C# language currently has potential ambiguities when parsing the `?` token followed by `[` or `.`, which can be difficult to interpret both visually and syntactically.」——`a?[b` 可被读作 null-conditional indexing 或条件表达式起点。这印证本提案追问 #1 对 `?` 语法族冲突的担忧：连 C# 都要为 `?` 的多种含义加"空白敏感"解析规则，VBScript.NET 若再给 `?` 叠一种含义，将同时背离 C# 与 VB 的既有 `?` 语义。
6. **一般生态走向（索引 T5/T6/T7/T8）**：AOT/trimming 下类型系统承担更多职责、source-gen 替代运行时动态、dynamic/表达式树边缘化、unsafe-evolution 重塑默认安全模型。链式三元是纯编译期表达式，降级为分支 IL，不触达反射/动态——**AOT 友好**，这是它与 M2（Any/dynamic）路线在生态压力上的本质区别。

### 现实 vs 提案

- **形态（A：`? If()`）——冲突。** C# 既未扩展 `?:` 元数，也未引入带 `?` 前缀的多分支条件；其多分支取值专用形态是 switch expression（无 `?` 前缀、`=>` arm、穷尽性警告）。`? If(` 的 `?` 前缀在 C# 生态无对应物，且 C# 自身正为 `?` 过载头痛。证据指向**维持形态否决**。
- **能力（表达式语境多分支取值）——兼容/同向。** C# 8 switch expression 填充的正是同一缺口（"support `switch`-like semantics for an expression context"）。VBScript.NET 候选 PROPOSAL C（`Select Case` 表达式）与该方向同构；B（扩展元数）在 C# 无先例——C# 没有把 `?:` 变成 N 分支。
- **结果类型推断——部分兼容、需桥接。** C# 的 `?:` 与 switch expression 都保留「best common type 优先 + 目标类型化转换兜底」双机制；VB `conditional-best-common-type` 决议（目标类型化 Active、LUB 回退 Table）与 C# 的「目标类型化转换兜底」一致，但 C# **并未**把 best common type 判 Table——VB 若照搬 C#，第一遍 best common type 与已 Table 的 LUB 的关系必须明示（桥接点，非直接冲突）。
- **穷尽性——直接可借鉴。** 本提案 OPEN QUESTION #1（无 `Else` 兜底）的三选项与 C# 实际裁定一一对应：C# 选「编译期警告 + 运行时抛专用异常」。但映射非 1:1：C# 穷尽性定义是"覆盖输入类型的每个值"（单主题分发），B 路线是"N 个独立布尔条件 + 有无 `Else`"（多主题）——**机制可移植、语义定义不同**。
- **短路求值——兼容。** C# switch expression「第一个匹配 arm 的 expression」（patterns.md line 303）与本提案"逐对短路"要求同构；C# `?:` 历来只求值选中分支。`Probably`（C# 语言常识，镜像内无 spec 正文副本）：`?:` 的"只求值选中分支"与"右结合"语义权威正文在 dotnet/csharpstandard §11.15——见 OPEN QUESTIONS。

### 对 VBScript.NET 的适应建议

- **长线形态照抄 C# 8 switch expression 骨架（PROPOSAL C）**：`Select Case` 表达式应复用 `Case`/`Case Else` 文法 + 穷尽性警告 + 首臂命中即取值（短路）。这是与 C# 生态互操作成本最低的路径——.vbx 脚本与 C# 库共享同一「表达式取值」心智模型，无需新关键字、无 `?` 冲突。
- **结果类型**：以 `conditional-best-common-type` 决议落地为目标类型化优先；若采纳 C# 的 best common type 第一遍，须明确其与已 Table 的 LUB 的关系（桥接点）。C# 的 `M(b ? 1 : 2)` vs `M(b switch {...})` 差异（target-typed-conditional-expression.md line 56–60）是「目标类型化与 best common type 谁优先」的现成测试用例，可作为 .vbx 结果类型规则的验收基准。
- **穷尽性默认建议**：沿用 C# 先例——无 `Else` 时编译期警告 + 运行时抛专用异常（而非编译期强制 `Else`，也非返回 `Nothing`），与 C# switch expression 行为对齐，降低跨语言学习成本。
- **不为 `?` 加第三种含义**：C# 都要为 `?` 歧义引入空白敏感解析规则，VBScript.NET 不应在 `?.`、`? "..."#Guid` 注解、XML 字面量之外再叠链式三元的 `?`。
- **AOT/source-gen 契合**：链式/`Select Case` 表达式是纯编译期结构，AOT/trimming 友好、不引入反射——是 VBScript.NET「默认安全、按需动态」路线的合格成员；**无需** source-gen 桥（无运行时生成），也**无需**识别新元数据（不触达 CLR 存储/特性标志）。

### 对既有 RESOLUTION/三态判定的影响

C# 证据**不推翻既有裁定，反而强化之**：

- **RESOLUTION #1（否决 `? If()` 形态）**：C# 的 `?` 过载（连 C# 都要加解析规则）与严格二元 `?:` 支持否决。
- **RESOLUTION #2/#3（能力 Table、长线偏好 C）**：C# 8 switch expression 是 C 的同构先例；且 C# 从未走 B（扩展元数）路线——强化「C 优于 B」。
- **RESOLUTION #4（结果类型硬闸门）**：C# 9 目标类型化条件表达式印证「目标类型化是正道」；但须明示 C# 保留 best common type 第一遍与 VB「LUB=Table」的桥接关系。
- **OPEN QUESTION #1（穷尽性）**：C# 给出成熟答案（编译期警告 + 运行时 `System.Runtime.CompilerServices.SwitchExpressionException`），可直接作为 B 路线复活时的 spec 蓝本。
- **三态判定不变**：Table（能力）/ Reject（形态）。C# 生态方向不构成复活 `? If()` 的理由；反而为复活候选（C）提供了成熟范本。

### OPEN QUESTIONS

- `?:` 的「只求值选中分支」与「右结合」逐字规范正文位于 dotnet/csharpstandard §11.15（`spec\expressions.md` 仅保留链接 `https://github.com/dotnet/csharpstandard/blob/draft-v6/standard/expressions.md#1115-conditional-operator`），镜像内无正文副本——**未能逐字核实**，本附录按 `Probably`（C# 语言常识）陈述。
- 「switch expression 非穷尽 = 警告 + 运行时 `SwitchExpressionException`」已逐字核实（patterns.md line 301/303 + LDM-2018-03-28）；「switch expression 放宽为 expression_statement」仍是未竟事项（patterns.md line 295 "We are looking at relaxing this in a future revision."）——与本提案"表达式语境"诉求无直接冲突，仅作生态动向记录。
- C# switch expression 的 `best common type`（patterns.md line 297）与 VB `conditional-best-common-type` 会议裁定的 LUB 是否为同一算法，需另行比对 dotnet/csharpstandard §12.6.3.15 与 vblang 决议——`Suspect`，镜像内无法定论。
