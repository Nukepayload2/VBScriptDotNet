# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天审查的是一份 **inactive** 建议——`Case Else` 变量（`proposal-case-else-variable.md`，Anthony 第 18 章实验性想法）。第 18 章是 Anthony 明确标注"还需要时间酝酿"的一批想法，我们的任务不是背书它，而是认真决定：**它值得激活，还是保持搁置？**

本建议紧贴模式匹配家族的地盘。上一场 Select Case 会议（`meeting-select-case-enhancements.md`）把五种 Case 子句形态拆成逐项裁定，把类型分派与 `Case Like` 划给家族文法；`ShapeOf` 会议（`meeting-shapeof-pattern-matching.md`）把声明模式 `Case x As T` 并入家族 Phase 1。`Case Else` 变量恰好落在同一片实现面上——所以今天的讨论绕不开 2018.12.19 的家族文法。

_诚实分层说明：本纪要逐句标注 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。所有引用的 vblang 主线会议决定与 issue 编号均逐字核对自 `..\..\..\vblang\meetings/` 原始文件；C# `case var x` 属 C# 语言常识，不来自 vblang 文件。找不到直接对应材料处，依据 vblang 设计原则与评价标准独立论证并显式标注。_

## Agenda

* [ModVB Proposal — `Case Else` 变量（Case Else Variable）](#modvb-proposal--case-else-变量)

## ModVB Proposal — `Case Else` 变量

_来源：Anthony D. Green 原文第 18.3 节 "Case Else variable"（`..\..\AnthonyDesign_wordpress.txt` L2823–2841，inactive / 实验性）。原文没有任何散文，只有一段代码示例与两行注释——整份建议是在这段代码上由我们补齐的。_

### 场景与缺口

建议要解决的场景：对枚举做穷举式 `Select Case`，所有预期 `Case` 都列完后，兜底分支需要"拿到未匹配到的表达式值"，以便抛出携带该值的异常或记日志：

```vb
' 建议原文形态（逐字，占位符未补全）：
' ' Expect the unexpected?
' ' (Great for enums!)
Select Case expression
    Case value1
        ...

    Case value2
        ...

    Case Else other
        ' Only needed a variable if the value is unexpected.
        ' This keeps the scope limited to this case.
        Throw New UnexpectedValueException(other)
        
End Select
```

注意两件事（事实，来自原文）：其一，主语是占位符 `expression`，`value1`/`value2`/`UnexpectedValueException` 都是占位符——`UnexpectedValueException` 不是 .NET 既有类型，示例本身**不能编译**；其二，**原注释自己点出了两个卖点**："Only needed a variable if the value is unexpected"（只在意外值分支才需要变量）与 "This keeps the scope limited to this case"（作用域限定在本分支）。

There is a real but narrow gap here。`Select Case` 的主语只求值一次（事实，VB 规范），`Case Else` 分支里若直接引用原表达式会**二次求值**——对属性 getter / 函数调用，这既浪费又有副作用。要拿到"已求值的那一次结果"，今天只能在外面先存一个中间变量。本建议想在 `Case Else` 内直接声明一个变量来承接这个值。

### 候选方案

**PROPOSAL A — 按建议原文落地（`Case Else other` 简写）。** 在 `Case Else` 关键字后直接跟一个标识符作变量绑定：

```vb
Select Case ParseHeader(stream)          ' 非平凡主语
    Case 0 : ...
    Case 1 : ...
    Case Else other
        Throw New UnexpectedValueException(other)
End Select
```

卖点：一行省掉外层 `Dim value = ...`，且变量作用域天然限在本分支。

**PROPOSAL B — 并入模式匹配家族文法（`Case Else Dim other` 变量模式）。** 2018.12.19 家族文法已有一条现成的产线（事实）：

```
Pattern
    | 'Dim' Identifier ('As' TypeName)?      // Variable pattern -- introduces a new variable in child scope; as TypeName or Object
```

`Case Else` 若允许接收一个"无类型检查的变量模式"，就是 `Case Else Dim other`——与 A 同一件事，但拼写统一进家族，不单独发明 Case Else 子句形态：

```vb
Select Case ParseHeader(stream)
    Case 0, 1 : ...
    Case Else Dim other
        Throw New UnexpectedValueException(other)
End Select
```

**PROPOSAL C — 维持现状（外层中间变量 / 直接引用主语）。** 什么都不做。分两种情形：

```vb
' C1：主语是局部变量时，Else 分支直接引用它——零新语法。
Select Case status
    Case FileReadStatus.Ok : ...
    Case FileReadStatus.NotFound : ...
    Case Else
        Throw New UnexpectedValueException(status)   ' status 就在手边
End Select

' C2：主语是非平凡表达式时，用一行中间变量承接（只求值一次）。
Dim value = ParseHeader(stream)
Select Case value
    Case 0, 1 : ...
    Case Else
        Throw New UnexpectedValueException(value)
End Select
```

### 权衡：LDM 追问清单

We 按评价标准第五部分的十二条拷问清单逐条过。本建议形态极简，但决定性问题恰好是最重的几个：**它与家族文法重叠、它的卖点在常见场景下并不成立、它的拼写有歧义。**

#### 1. 语法 / 文法歧义

`Case Else` 今天不接受任何东西——`Case Else` 后跟裸标识符（如 `other`）今天是语法错误（裸表达式不是语句，VB 也无此位置的行标签）。所以新增 `Case Else <identifier>` 在词法层面**非破坏**。但歧义在家族语境里：

**裸标识符在家族文法是"表达式模式"，不是绑定。** 2018.12.19 文法（事实）：

```
| Expression                             // Expression pattern -- value/reference equality test against Expression
```

`Case SomeConstant` 在家族里是与 `SomeConstant` 做等值测试。若 `Case Else other` 的 `other` 变成"绑定"，同一个裸标识符在 `Case` 位置是等值、在 `Else` 位置是声明——**两种位置两种含义**，正是家族文法用 `Dim`/`As` 显式区分的那类歧义。这直接质疑 A 的拼写。`Probably`：A 若要成立，必须证明"Else 位置裸标识符 = 绑定"不会与未来家族文法的"Else 位置表达式模式"打架；而后者目前没有含义（Else 不比较），所以今天能消歧，但这是给未来埋雷。

**discard 占位。** 2018.12.19 我们写过 "Probably add a discard identifier and a discard pattern"（事实）。若家族引入 discard，`Case Else _`（不绑定的兜底）与 `Case Else other`（绑定的兜底）是同一位置的两个形态，拼写面要一起定，不能先占 `other` 再补 `_`。

**`:` 内联语句。** `Case Else other : DoSomething()`——`other` 绑定后接内联语句，解析器要区分 `other` 是绑定还是 `:` 前的语句。由于裸标识符不是合法语句，向前看一个 token 即可消歧（`Probably` 可行），但这是新的语法负担。

#### 2. 角案例与边界语义

**主语是局部变量时，卖点消失。** 这是整场对 A 最实质的反对。建议的核心卖点（"避免二次求值 + 免中间变量"）只在主语是**非平凡表达式**时才成立；而最常见的情形——对枚举穷举分发——主语几乎总是局部变量或参数，`Case Else` 分支直接引用它即可，不需要任何新变量。把 Anthony 自己注释的 "Great for enums!" 展开成真实代码，就会看到 `status` 就在手边。**"作用域限定在本分支"的好处，在主语为局部变量时同样不存在**——`status` 本来就在外层作用域。作用域收益只有在主语是非平凡表达式、且你不想为此污染外层时才有意义。

**`Nothing` 与值类型。** `Select Case obj / Case Else other` 且 `obj` 为 `Nothing`：`other` = `Nothing`，语义自洽。枚举/结构体：`other` 是值拷贝。无特殊边界（`Probably`）。

**definite assignment。** `other` 由绑定保证赋值，分支内可安全使用；分支外不可见。这与家族文法变量模式的 child scope 一致（2018.12.19，事实）。

**多个 `Case Else`。** 今天 VB 允许几个 `Case Else` 子句？若允许多个，各自声明的变量作用域与遮蔽规则需要专门规定。本建议完全没提。`OPEN QUESTIONS`。

**`Case Else` 后空分支。** `Case Else other` 但分支无语句——绑定无意义但合法；需决定是否报"变量已声明但未使用"。

#### 3. 作用域与绑定

变量作用域 = 所在 Case 子句块。2014-02-17 Design1 先例（事实）："The scope of the variable declaration is just that case clause, and follows the same principles as other blocks which define variables like ForEach." 建议注释的 "This keeps the scope limited to this case" 与 Design1 逐字同义——A 的作用域主张是既有先例的直白复用，不是新设计。

语义模型：`other` 是一个新的局部符号，类型 = 主语表达式的静态类型（建议未决问题 Q2）。绑定到什么符号？一个由编译器合成的局部，类似 ForEach 迭代变量。`Probably`：semantic model 返回新局部符号；若与外层同名变量，走 VB 的隐式遮蔽（嵌套 `Dim x` 允许）还是报错，未定——`OPEN QUESTIONS`。

#### 4. 与既有特性的交互

**与家族文法重叠是整场最大的交互问题。** `Case Else` 变量是家族"变量模式"（`Dim Identifier`）在兜底子句的一个特例。上一场 Select Case 会议（事实，我们自己的前场决议）已把类型分派与 `Case Like` 划给家族；`ShapeOf` 会议把声明模式并入家族 Phase 1。**A 若独立落地，等于在家族文法之外给 Case 子句面再开一条形态——为同一件事建两套机制**（设计原则 3）。这不是"建议错了"，而是"建议的载体错了"。

**`Case Else When`（2014 先例）。** 2014-02-17 社区已提出 `Case Else When (flags And (CharFlags.Complex Or CharFlags.IdentOnly)) <> 0` 这类形态（事实，AdamSpeight2008 的 `With`/`When` 建议）。若家族文法带回 `When`（2018.12.19 明确 "We like `When`."，事实），`Case Else Dim other When other <> 0` 是自然的组合。A 的简写与 `When` 组合时，`Case Else other When ...` 的 `other` 在 `When` 里也要可用——又是一层解析与绑定面。

**守卫 / Guarded `Let`。** 本建议的姊妹建议 `inactive/proposal-guarded-let.md`（18.1/18.2）在 `Let ... Else <控制流>` 里做"失败兜底"，与"Else 兜底拿值"是同一家族想法的两个方向。两份建议必须对表，否则"Else 分支拿值"会出现两种语法。

**`TypeOf` 流分析。** 无直接交互——本特性不涉及类型测试，无收窄。但共享 Case 子句面；家族统一文法落地时必须一次说清"Case 子句能带哪些东西"。

**逗号（Design1/Design2 悬案）。** 2018.12.19 决议（事实）："Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax." `Case Else` 是兜底，逗号组合对 Else 无意义（Else 无条件命中）；但家族文法决定"Case Else 是否接受模式"时，要顺带定"Else 内是否允许逗号"。`Probably`：Else 不允许逗号，但需显式写进 spec。

#### 5. Breaking change 与兼容性

对现有合法代码**无直接破坏**（事实）：`Case Else` 后跟裸标识符今天就是语法错误，新增形态非破坏。真正要盯的：若 A 采用裸标识符，且未来家族文法把 `Case Else` 位置开放给表达式模式/其他模式，先到先占的 A 语法可能把家族困住——这正是 2014-04-23 那个 "Urgent question"（事实）："do we need to make changes to the CURRENT design of primary constructors and Select Case so that we don't block off a future world of pattern-matching?" A 现在做，就是在重演 2014 年的教训：为今天的便利堵死未来的文法面。

#### 6. Option Strict / 编译选项分叉

两路径行为一致（`Probably`）：`other` 类型 = 主语静态类型，严格/宽松下相同；宽松下主语为 `Object` 时 `other` 为 `Object`，分支内晚期绑定行为与直接引用主语一致。本建议对分叉完全沉默——虽无实质分叉，但 spec 必须列证。

#### 7. IDE / IntelliSense

`other` 应在分支内补全、`GetSymbolInfo` 返回新局部符号、InfoTip 显示"绑定自 Select Case 主语"；分支外补全不得出现 `other`。裸标识符形态下，`Case Else` 后的补全要区分"变量绑定"与"内联语句"两种起始——又是一个反对 A 的 IDE 成本。`Probably`：家族文法的单一 Case 子句节点类型是这些 IDE 特性的统一承载面，比 A 的散装形态便宜。

#### 8. 数据 / 普遍性

"对未匹配值抛异常"是真实场景（枚举解析、命令分发、状态机），但**没有量化数据**，且最常见形态（主语 = 局部变量）下卖点不成立。2018.12.19 我们写过（事实）："We need some compelling cases for non-matches. The most compelling case for patterns is TypeCheck/assignment, and that doesn't seem to make sense for non-matches."——这句是对"非命中分支要什么"的精确警告：`Case Else` 拿值，正是"non-matches 需要什么"的最小版本，而我们已经承认非命中分支的用例缺乏说服力。证据等级止于"已提供/已检查"。

#### 9. 更简替代

- **外层中间变量**（C1/C2）：一行，语义完全相同，零新语法。对"穷举枚举抛异常"，这行中间变量是最小仪式。
- **直接引用主语局部**（C1）：主语是局部变量时，什么都不用加。
- **家族变量模式**（B）：`Case Else Dim other`，一旦家族 Phase 1 落地，几乎是零成本。
- **Analyzer / 重构**：提示"此 `Select Case` 穷举不完整"、提示"Else 分支可引用主语"——不改变语言，也能覆盖一部分价值。2017.10.18 我们认可过模式引入原则（事实）："I therefore proposal the principle for introducing a pattern is when the ceremony of _inspecting_ and object(-graph) obscures the structure of the data." 本场景的"仪式"是一行中间变量——不够格。

#### 10. 成本 / 优先级

B 形态的成本 ≈ 0（家族文法一条现成产线 + 决定 `Case Else` 接受变量模式）。A 形态的成本 = 新的 Case Else 子句形态 + 消歧 + 作用域/语义模型/IDE——**为省一行中间变量付一个语法形态的钱**。价值（价值 3 / 成本 6 standalone / 风险 3）不值得。优先级：排在家族 Phase 1 之后，且只作为家族的一个子决定，不作为独立特性。

#### 11. 运行时 / CLR 硬约束

无。纯编译期绑定，无反射、无 PEVerify 问题、不触达 CLR 存储规则。泛型开放类型的顾虑不适用（2014-02-17 判例 "this is impossible in the current CLR without reflection" 针对的是类型模式；本特性不涉及类型测试）。

#### 12. 值不值得做

逐项打分（价值 × 成本 × 风险）：价值 3（窄场景、卖点只在非平凡主语成立）/ 成本 6 standalone（新子句形态 + 歧义 + IDE）/ 风险 3（家族文法堵路 + 裸标识符双义）。**不值得 standalone。** 但作为家族"变量模式进 `Case Else`"的子决定，价值/成本比翻转——那时值得重新评估。热情不抵消可行性：我们对"在 Else 里拿值"这个直觉有同感，对"A 独立落地"的载体没同感。

### VB 基因对照

按设计原则 10 条逐条过（事实依据为评价标准第二部分）：

1. **永不破坏现有代码** — `Case Else <标识符>` 今天即语法错误，非破坏。✓
2. **保持 VB-like** — `Case Else other` 读起来像英语（"否则，other"）；B 的 `Case Else Dim other` 更显式更 VB（`Dim` 是 VB 声明关键字）。A 的简写不带外来味，但也没 VB 化到 `Dim`。±
3. **不引入"第二种做事方式"** — 家族变量模式落地后，A 就是第二种方式。今天虽无第一种方式，但方向已定，A 抢跑。✗
4. **默认跟随 C#，除非有充分理由** — C# 有 `case var x`（var 模式，命中即绑定主语，可作兜底臂，C# 语言常识）。主线家族文法对变量模式的态度是"分阶段、先声明模式"（2018.12.19 阶段划分，事实）；本特性是 C# var 模式的 Else 特例，应随家族走，而非 VB 自创拼写。±
5. **读起来像英语、对新手友好** — A 简短；B 的 `Dim` 对新用户更诚实。±
6. **不为边缘场景加特性** — 主语为非平凡表达式 + 想要 Else 内拿值，是边缘中的边缘。✗
7. **避免隐蔽控制流/语义变化** — 无隐蔽控制流；绑定是显式的。✓
8. **不与既有语法冲突** — A 与家族"裸标识符 = 表达式模式"有潜在冲突面。✗
9. **消除常见样板** — 样板是一行中间变量，不是高频痛点。±
10. **冗长只在有用时是美德** — `Dim` 在这里是有用的冗长（消歧）。±

**主线对照（评价标准 2.3 表）**：`Select Case TypeOf` / 模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`，关系标"一致（`Matches` 即 Anthony 提出）"。本建议不在主线任何工作项里；它是 **Anthony 独立延伸，且与主线家族文法的载体冲突**——不是目标冲突（目标一致：在兜底拿值），是载体冲突（独立子句形态 vs 家族变量模式）。

### RESOLUTION:

1. **目标成立，载体不成立。** "在 `Case Else` 兜底分支拿到未匹配值"是合理的语言直觉，C# `case var x` 有先例；但以独立子句形态 `Case Else other` 落地，是与家族文法抢地盘，且卖点只在主语为非平凡表达式时成立。

2. **本特性 = 家族"变量模式"在 `Case Else` 的子决定，不是独立特性。** 2018.12.19 文法已有 `| 'Dim' Identifier ('As' TypeName)? // Variable pattern`（事实）。家族 Phase 1 落地时，决定 `Case Else` 是否接受变量模式；拼写倾向 `Case Else Dim other`（显式、无歧义），`Case Else other` 裸标识符简写**不进入语言**——它撞家族"裸标识符 = 表达式模式"的双义。

3. **搁置（Table）。** 与家族文法绑定：家族 Phase 1（声明模式 + `When`）落地前，本建议不单独激活；落地后作为家族文法的一部分重新评估。

4. **拒绝"为省一行中间变量付一个语法形态"。** 主语为局部变量时直接引用主语（C1）；主语为非平凡表达式时用一行中间变量（C2）——现状已覆盖卖点的绝大多数。

5. **不引入穷尽性检查。** 本建议与穷尽性无关；2018.12.19（事实）"we don't have this today and introducing it is backwards breaking"——穷尽性若将来做，是另一个特性，别与本建议绑在一起。

6. **与 `When` 组合形态（`Case Else Dim other When ...`）留给家族文法。** 2014 `Case Else When` 先例 + 2018.12.19 "We like `When`."（事实）——组合规则在家族统一文法里一次定清。

7. **与姊妹建议对表。** 与 `inactive/proposal-guarded-let.md`（18.1/18.2，"Else 兜底拿值"的另一个方向）明确边界；与 `proposal-exclusive-for-upper-bound.md`（18.4）同为第 18 章实验项，一并搁置，不因本会议单独激活 18 章任何一项。

### Implication:

- 本建议维持 **inactive**；状态标 `LDM No Plans`（沿用 2018.05.30 的标签惯例，事实）。不关闭——家族文法落地后回访。
- `TODO`（供提案返工）：补充与家族文法的对照表（`Case Else Dim other` vs `Case Else other` vs C# `case var x`）；把 `UnexpectedValueException` 等占位符换成可编译示例；补 Option Strict 双路径验证与兼容性分析；回答"多个 `Case Else`"问题。
- `TODO`：给家族统一文法起草工作项开一条"`Case Else` 是否接受模式"的待决清单（含逗号规则、`When` 组合、`Dim` vs 裸标识符拼写、discard `_`）。
- 与 `meeting-shapeof-pattern-matching.md`、`meeting-select-case-enhancements.md` 的决议保持同一套 Case 子句面术语。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：今天 VB 允许多个 `Case Else` 子句吗？若允许，各变量模式的作用域与遮蔽如何规定。
- `OPEN QUESTIONS`：`Case Else Dim other` 与外层同名变量：隐式遮蔽（嵌套 `Dim` 规则）还是报错？semantic model 返回哪个符号。
- `OPEN QUESTIONS`：`Case Else` 接受模式后，逗号是否被禁止（`Probably` 禁止，需显式写进 spec）。
- `OPEN QUESTIONS`：C# `case var x` 的普及度数据——若 C# 开发者几乎不用它，我们为什么需要 Else 绑定？（数据 / 普遍性的待证项。）
- `TODO`：量化"主语为非平凡表达式 + 想在兜底拿值"的真实代码占比。
- `Follow-up`：与 guarded-let 会议对表，明确"Else 兜底拿值"两种语法的边界。

### 状态

- **LDM 状态：`LDM No Plans`（保持 inactive / Table）**——不是 Reject：目标合理、家族落地后近零成本可复活；是"现在不做，载体未定"。
- **三态判定：Table** — 价值窄、standalone 成本与歧义高、与家族文法载体冲突；激活所需信号见下。

**激活所需信号**：① 家族 Phase 1（声明模式 + `When`）已落地，且统一文法决定 `Case Else` 接受模式；② 出现真实代码证据：主语为非平凡表达式、外层中间变量被明确视为不可接受的场景（如代码库中此类 `Select Case` 有可量化占比）；③ 有原型证实 `Case Else Dim other` 的语义模型与 IDE 行为符合预期。三个信号齐备前，本建议保持搁置。

---

## 附录：特性评价

# 建议评价报告：proposal-case-else-variable.md

## 评价对象

- 建议：proposal-case-else-variable.md — `Case Else` 变量（Case Else 分支内绑定未匹配值）
- 来源：Anthony D. Green 原文第 18.3 节 "Case Else variable"（`..\..\AnthonyDesign_wordpress.txt` L2823–2841）。原文仅一段代码示例 + 两行注释，无散文；建议文档在此之上补齐了六章节。
- 配方目标：让 `Case Else` 把未匹配到的 `Select Case` 主语值绑定到分支内变量，省去外层中间变量并限定作用域，便于"意外值"场景抛异常/记日志。

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点≈"只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 的目标改进（避免二次求值、免外层中间变量、作用域限定）清晰、示例可演示；但卖点只在主语为非平凡表达式时成立——最常见形态（主语 = 局部变量）下直接引用主语即可，主效果消退。无原型/运行。 | 已提供 / 已检查 | 无原型封顶 3；"作用域限定"卖点在主语为局部变量时不成立；示例含占位符不能编译 |
| 特性 | 3/5 | 锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Case Else` 是 VB 血统（原则 9 少量命中）；概念 = C# `case var x`（var 模式）的 Else 特例，未 VB 化到 `Dim`，且裸标识符拼写与家族"表达式模式"双义。 | 已检查 | 借鉴 C# 未标注；裸标识符与家族文法冲突面未识别；A 拼写未 VB 化 |
| 品质 | 3/5 | 锚点≈"缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、Drawbacks/Alternatives/Unresolved 诚实（3 个未决问题具体）；但无文法/BNF、无角案例、无兼容性/breaking 分析、无 Option Strict 双路径、无 semantic model/IDE 分析，且未标注最直接先例（家族文法变量模式、C# var 模式、2014 `Case Else When`）。 | 已检查 | 缺 Grammar/Edge cases/Compatibility 节；主线先例全部缺席；示例不可编译 |
| 属性 | 3/5 | 锚点≈"有得有失——某维度受益、某维度受损，文档未充分权衡"。对水（盘活 `Case Else` 兜底惯用）与光（VBScript 宽松分发差异化）微正；对风（演化一致性）受损——独立子句形态与家族文法载体冲突，重演 2014-04-23 "block off a future world" 教训；暗风险低（非破坏）。文档对家族冲突只字未提。 | 已检查（预测待定） | 家族文法冲突未识别；"为省一行付一个语法形态"的成本未权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。来源标注部分准确（Motivation 引"作者（18.3）"）；但未标注继承 VB6/VBScript 血统的 `Case Else`、未标注借鉴 C# `case var x`（var 模式）、未标注家族文法 2018.12.19 变量模式这条现成产线——最直接材料血缘全部缺失。 | 已检查 | C# 借鉴未标注；家族文法血缘未标注；与 guarded-let（同章）的血缘未说明 |

## 设计原则对照

- **与 VB 基因**：主体中性偏温和——`Case Else` 兜底拿值不违反可读性（原则 5），非破坏（原则 1），无隐蔽控制流（原则 7）；但违反"不为边缘场景加特性"（原则 6）、与家族变量模式构成"第二种做事方式"风险（原则 3），且简写拼写未 VB 化到 `Dim`。
- **与主线关系**：**Anthony 独立延伸、与主线载体冲突**——目标（Else 拿值）与主线模式匹配一致（`Select Case TypeOf`/模式匹配 = 主线"最期待"领域，2.3 表），但载体（独立 Case Else 子句形态）与主线家族文法（2018.12.19 变量模式）冲突。应作为家族"变量模式进 `Case Else`"的子决定，而非独立特性。
- **破坏性变更**：无（`Case Else <标识符>` 今天即语法错误）；有未来文法面占用风险（裸标识符双义、堵家族"Case Else 接受模式"的形态选择）。

## 总评

- **达成程度：部分达成（且载体不成立）** — "在兜底分支拿未匹配值"的概念成立且有 C# 先例；但 A 的独立语法载体不成立，卖点只在窄场景成立，证据止于书面。
- **LDM 三态建议：Table（保持 inactive / `LDM No Plans`）** — 并入家族文法（`Case Else Dim other`）后随 Phase 1 重新评估；不作为独立特性激活。
- **主要问题**：① 卖点只在主语为非平凡表达式时成立（最常见形态直接引用主语即可）；② 独立子句形态与家族文法载体冲突（原则 3）；③ 裸标识符简写与家族"裸标识符 = 表达式模式"双义；④ 借鉴 C# `case var x` 与 2014 `Case Else When` 先例未标注；⑤ 无文法/角案例/兼容性/IDE 分析，示例不可编译。

## 返工建议

- **补充章节**：Grammar/BNF（`Case Else` 是否接受模式；`Dim other` vs `other` 拼写；逗号规则）；Edge cases（`Nothing`、值类型、多 `Case Else`、空分支、内联语句 `:`、`When` 组合、discard `_`）；Compatibility（家族文法占用、裸标识符双义）；Option Strict 双路径；semantic model/IDE。
- **补充证据**：C# `case var x` 的使用普及度与场景；家族文法 2018.12.19 变量模式逐字引用；"主语非平凡表达式 + 兜底拿值"的真实代码占比；原型分支与运行结果（状态栏占位链接）。
- **未决问题处理**：Q1（语法/命名）→ 归家族文法定拼写（倾向 `Dim other`）；Q2（变量类型）→ 主语静态类型，两路径一致；Q3（作用域边界）→ Design1 先例（"just that case clause"），需补多 `Case Else` 情形。
- **设计探索**：与 `inactive/proposal-guarded-let.md` 对表"Else 兜底拿值"两种方向；与 `meeting-shapeof-pattern-matching.md`、`meeting-select-case-enhancements.md` 共用 Case 子句面术语，一次定清"Case 子句能带哪些东西"。

---

## 附录：C# 生态与互操作考量

> 本附录由 meeting agent 追加，独立于正文章节；只做「C# 现实方向 vs 本提案响应」的对应，不改动正文 RESOLUTION / Implication / 评价报告。所有 C# 原文均逐字核对自 `..\..\..\csharplang\`（dotnet/csharplang 官方仓库镜像），来源以 `→ 路径` 标注；无法在本仓库逐字核实的项标 **Suspect / OPEN QUESTIONS**。C# 语义常识（如 `case var x` 的用法、`default:` 内重写表达式会重新求值）按正文纪律与 C# 文件引用区分标注。

### 相关 C# 现实方向

本提案主题（在兜底分支把未匹配到的 `Select Case` 主语绑定为分支内局部变量）在 C# 的对应物是 **switch 语句 / switch 表达式的模式绑定**（C# 7/8 已落地、稳定），以及 **C# 15 的 unions / closed hierarchies**（索引 T8，模式匹配的穷尽性走向）。

**C# switch 语句没有「else / default 绑定变量」的形态。** `pattern-matching.md`（C# 7.0）的 switch_label 文法（事实，逐字）：

```
switch_label
    : 'case' complex_pattern case_guard? ':'
    | 'case' constant_expression case_guard? ':'
    | 'default' ':'
    ;
```

`default` 标签是**裸的**——没有 designation、没有绑定。`Case Else other` 在 C# 侧没有语法对应。

**C# 的「兜底且绑定」由 var 模式充当，且刻意要求"最后 + 不能有 default"。** `pattern-matching.md`（C# 7.0）var 模式原文（事实，逐字）：

> An expression *e* matches a *var_pattern* always. In other words, a match to a *var pattern* always succeeds. If the *simple_designation* is an identifier, then at runtime the value of *e* is bound to a newly introduced local variable. The type of the local variable is the static type of *e*.

`meetings\2015\LDM-2015-01-28.md` 的讨论逐字印证了它在 switch 里的"Else 绑定"角色与约束（事实，逐字，原文即注释形态）：

```
case var x: // would have to be last and there'd have to not be a default:
```

——即 C# 把"兜底拿值"实现为**一个总是命中的模式臂**（`case var x:`），而非给 `default` 扩展绑定变量；两者互斥，`case var x` 必须最后。

**C# 模式绑定变量的作用域 = case 块**（事实，逐字，`proposals\csharp-7.0\pattern-matching.md`）：

> If the pattern is a case label, then the scope of the variable is the *case block*.

**definite assignment 有「单 label 块」条件**（事实，逐字，`proposals\csharp-7.0\pattern-matching.md`）：

> A pattern variable declared in a *switch_label* is definitely assigned in its case block if and only if that case block contains precisely one *switch_label*.

**C# switch 表达式把「非穷尽」当作编译器警告 + 运行期异常，而非用户兜底**（事实，逐字，`proposals\csharp-8.0\patterns.md`）：

> The compiler shall produce a warning if a switch expression is not *exhaustive*.

> If there is no such *switch_expression_arm*, the *switch_expression* throws an instance of the exception `System.Runtime.CompilerServices.SwitchExpressionException`.

**C# 15 unions 正在让「穷尽」成为类型系统事实**（索引 T8；`proposals\unions.md` 原文，逐字）：

> Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive.

> *Union exhaustiveness*: Switch expressions over union values are exhaustive when all case types have been matched, without need for a fallback case.

**discard 模式**是 C# 的「不绑定兜底」`_`（事实，逐字，`proposals\csharp-8.0\patterns.md`）：

> An expression *e* matches the pattern `_` always. In other words, every expression matches the discard pattern.

### 现实 vs 提案

| 维度 | C# 现实 | 本提案响应 | 判定 |
|---|---|---|---|
| 「兜底拿值」概念 | `case var x:`（var 模式总命中、绑定主语静态类型） | `Case Else other` / `Case Else Dim other` | **兼容**：同一概念；C# 证实"兜底臂绑定值"成立 |
| 作用域 | 模式变量作用域 = case 块 | Design1 先例 "just that case clause" | **兼容**：逐字同义（正文 3 节已指出） |
| 语法载体 | 复用模式文法（var 模式），**不**扩展 default | A 独立子句形态 vs B 家族变量模式 | **倾向 B**：C# 的取舍正是"用模式臂表达、不动 else"，佐证 A 载体不成立 |
| 兜底与穷尽 | 走向穷尽性：unions 让 switch 表达式免 fallback；非穷尽抛异常 | 本提案与穷尽性无关；RESOLUTION 第 5 点"不引入穷尽性检查" | **脱节**：C# 把注意力放在"编译器推理有没有兜底"，本提案放在"给兜底加变量"——方向相反但不冲突 |
| 多 label / definite assignment | 多 label 共享 block 时模式变量**不** definite assigned | 多 `Case Else` 语义 = `OPEN QUESTIONS` | **参照**：C# 规则可作 VB 规则蓝本 |
| 绑定类型 | var 模式 = 主语静态类型 | `other` 类型 = 主语静态类型 | **兼容**：两语言一致 |
| discard / 不绑定兜底 | `default:` 或 `case _:`（discard 模式） | 家族文法的 discard 占位（正文 1 节 "Probably add a discard identifier"） | **兼容**：同一拼写面，需一起定 |

**冲突点（弱）**：C# 侧不存在 `default` / `else` 绑定变量的形态——本提案 A 若落地，等于发明 C# 在 2015 年 LDM 里**讨论过但刻意不做**的文法面（C# 的取舍是"用模式臂表达，不扩展 default"）。这是文法面哲学的错位，不是语义冲突。

**需桥接（元数据层）**：C# 15 unions / closed hierarchies 落地后，VB 编译器须识别其新元数据（CompilerFeatureRequired 驱动的特性标记；决策文件 M4 已提）。若 C# union 已穷尽，VB 的 `Case Else` 在跨语言消费该 union 类型时是"不可达 / 冗余"——VB 侧穷尽性与 C# union 的接口如何对齐，是家族 Phase 1 之后要定的桥接点。

**脱节点**：本提案是纯编译期绑定，无反射、无 marshaling——与 C# 的 AOT / trimming / source-gen 主线（索引 T5/T6）**无摩擦**；它的互操作风险集中在"家族文法与 C# 模式文法的概念对齐"，不在运行时。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：本特性天然"默认安全"（纯编译期局部绑定，无晚期绑定、无反射），与 C# / AOT 方向一致——这是它在互操作维度上的加分项（正文 11 节已确认无 CLR 硬约束）。不必为它引入动态路径。
2. **source-gen 桥：不必要**。本特性不产生运行时反射 / marshaling，source generator 在这里无用武之地。真正需要 source-gen 桥的是 Any / 晚期绑定（决策文件 M2/M5），不是 Else 绑定。
3. **识别新元数据**：若 C# unions / closed hierarchies 落地，VB 编译器需识别其元数据，并在"跨语言消费 C# union"时给出穷尽性提示——`Case Else` 对已穷尽 union 报"不可达 / 冗余"，或对缺兜底提供诊断。
4. **拼写对齐 C# 先例**：VBScript.NET 若做"Else 拿值"，优先 `Dim other`（家族变量模式，显式、消歧），与 C# `case var x` 的概念对齐；裸标识符 `Case Else other` 会与 C# 学习路径上"模式臂绑定"形成两套心智模型，且撞家族"裸标识符 = 表达式模式"双义。
5. **穷尽性作为独立议题**：C# 在 switch 表达式已做穷尽性警告、unions 让穷尽性成为类型系统事实——VB 是否跟进穷尽性检查，应作为家族 Phase 1 后的独立议题，不绑定本建议。

### 对既有 RESOLUTION / 三态判定的影响

- **不改变三态判定（Table）**。附录进一步**佐证** RESOLUTION 1/2：C# 早在 2015 年就讨论过 `case var x` 这个"Else 绑定"，并明确它必须最后、不能有 default、以模式臂而非 else 扩展落地——C# 用"复用模式文法"解决"兜底拿值"，正是本会议结论"载体应走家族变量模式（B）而非独立子句形态（A）"的 C# 侧镜像。
- **佐证 OPEN QUESTION 处理方向**：C# "多 label 共享 block 时模式变量不 definite assigned" 可为"多个 `Case Else`"提供蓝本——VB 若允许多个 `Case Else` 各自绑定，可仿 C#：绑定变量只在单 label 分支 definite assigned；多 label 共享 block 时按"至少一个绑定已赋值"或"不 definite assigned"显式规定。
- **RESOLUTION 第 5 点（不引入穷尽性）在 C# 侧正被扭转**：C# 8 起 switch 表达式已产生穷尽性警告（CS8509），unions 把穷尽性变成类型系统事实。本附录提示：家族 Phase 1 讨论"`Case Else` 是否接受模式"时，顺带把"VB 是否引入穷尽性诊断"列入待决清单——但它与本建议的 Table 状态无关。

### 引用纪律说明 / OPEN QUESTIONS

- 本附录引用均逐字核对：`→ proposals\csharp-7.0\pattern-matching.md`（var 模式 / switch_label 文法 / 作用域 / definite assignment）、`→ proposals\csharp-8.0\patterns.md`（discard / exhaustive 警告 / SwitchExpressionException）、`→ meetings\2015\LDM-2015-01-28.md`（`case var x: // would have to be last...`）、`→ proposals\unions.md`（T8 穷尽性原文）。索引 T8 的表述（C# 15 = unsafe evolution + unions + closed hierarchies）未在本附录直接引用原文，仅作背景。
- `OPEN QUESTIONS`：C# switch 语句在 `default:` 块内再次书写 switch 表达式是否被明确保证"重新求值"（即拿不到已求值的那一次）——csharplang 无逐字说明（spec 正文在 dotnet/csharpstandard，未在本仓库核实）；正文"主语只求值一次"的 VB 侧事实成立，C# 侧对应表述待补源。
- `OPEN QUESTIONS`（沿用正文）：C# `case var x` 的使用普及度数据——本附录无法从仓库核实，仍是"数据 / 普遍性"的待证项。
- `Suspect`：无（本附录所有 C# 原文均已逐字核实）。
