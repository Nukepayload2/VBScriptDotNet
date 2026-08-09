# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天审查 `proposal-select-case-enhancements.md`——一份把五种互不相同的匹配方式（`TypeOf` 类型匹配、`ShapeOf` 形状匹配、`Is`/`IsNot` 恒等匹配、`Is Like` 通配符匹配、`In`/`NotIn` 集合成员匹配）一次性塞进 `Select Case` 的打包建议。我们把它当作模式匹配家族的一条入口来读：Select Case 一直是主线反复回到的句子（2014 预览、2018.05.30 Issue 304、2018.12.19 家族文法），而本建议恰好踩在这条线上。这是一场把"一个打包建议"拆成"五个独立裁定"的会议。

_诚实分层说明：本纪要逐句标注 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`。所有引用的 vblang 主线会议决定与 issue 编号均逐字核对自 `..\..\vblang\meetings\` 原始文件；找不到直接对应材料处，依据 vblang 设计原则与评价标准独立论证并显式标注。_

## Agenda

* [ModVB Proposal — Select Case 增强（TypeOf / ShapeOf / Is / Like / In）](#modvb-proposal--select-case-增强)

## ModVB Proposal — Select Case 增强

_来源：Anthony D. Green 原文第 3.3 节 "Select Case"（`..\AnthonyDesign_wordpress.txt` L845–881）。本建议提出五种新的 `Case` 子句形态，让 `Select Case` 从"等值/范围/比较分发"扩展为"统一的分派语句"。_

### 场景与缺口

`Select Case` 是 VB 惯用的分派语句，但今天只接受等值表达式、范围（`1 To 10`）与 `Is` 比较（`Case Is > 5`）。按运行时类型、按对象恒等、按集合成员、按通配符分派，都得退化成 `If ... ElseIf` 链。现状：

```vb
' 状态：按运行时类型分派 = If 链 + 每分支强转，重复且易错。
Function Describe(shape As Object) As String
    If TypeOf shape Is Rect Then
        Dim r As Rect = DirectCast(shape, Rect)
        Return $"Rectangle {r.X}x{r.Y}"
    ElseIf TypeOf shape Is Circle Then
        Dim c As Circle = DirectCast(shape, Circle)
        Return $"Circle r={c.Radius}"
    Else
        Return "Unknown"
    End If
End Function
```

There is a real gap here。2017.10.18 我们记下过模式引入原则："the principle for introducing a pattern is when the ceremony of _inspecting_ and object(-graph) obscures the structure of the data"——类型分发正是这条原则的靶心。VBScript.NET 的立场让这个缺口更尖锐：VBScript 血统就是运行时分发（`TypeName`、`IsObject`、late binding），一个按运行时形态分派的语句天然属于这个世界。

但 We 立刻注意到：**本建议把五个异质机制打包成一份文档**。类型匹配（继承 `TypeOf` 流分析，已裁决）、形状匹配（继承 `ShapeOf` 操作符，已裁决 Table）、恒等匹配（`Is`/`IsNot`）、通配符（`Like`）、集合成员（`In`/`NotIn`）——它们的血统、成本与风险完全不同，不能一份文档一起判。评价标准 4.2 红旗清单里有一条"一份提案混杂多个独立特性，边界模糊"，本建议命中。于是我们把会议拆成五场小型裁定。

### 候选方案

**PROPOSAL A — 建议原文五合一（Anthony 3.3 逐字）**

```vb
' `Select Case` on type.
Select Case TypeOf obj
    Case AppDomain
        ...

' `Select Case` on shape.
Select Case ShapeOf coordinate
    Case (latitude, longitude)
        ...

' `Select Case` on identity.
Select Case sender
    Case Is CloseButton
        ...

    Case IsNot MainForm
        ...

' `Select Case` with `Like`.
Select Case str
    Case Is Like "?*@?*.?*"
        ...

    Case IsNot Like "#.#.#.#"
        ...

' `Select Case` on collection membership.
Select Case str
    Case In bannedWords
        ...

    Case NotIn bannedWords
        ...
```

五个机制各自独立，互不组合；除 `TypeOf` 与 `Like` 外，其余都引入新的 Case 子句形态或新的关键字组合。

**PROPOSAL B — 并入模式匹配家族统一文法（主线 2018.12.19 方向）**

不给 `Select Case` 开五条新道，而是让它接入家族文法——2018.12.19 我们已给出 Case 内的 Pattern 文法草案：

```vb
' 家族文法（2018.12.19 草案摘录，事实）：
'   Pattern ('When' BooleanExpression)?
'   | 'As' TypeName                     ' Type check pattern
'   | 'Dim' Identifier ('As' TypeName)? ' Variable pattern
'   | 'Is'? ComparisonOperator Expression ' Comparison pattern
'   | 'Like' StringExpression           ' Like pattern
'   | Expression 'To' Expression        ' Range pattern
'   | Expression                        ' Expression pattern

' B 的写法：声明模式 / 类型分派
Select Case shape
    Case r As Rect : Return r.X * r.Y
    Case c As Circle : Return Math.PI * c.Radius * c.Radius
    Case Else : Return 0
End Select

' B 的写法：Like 模式（无 `Is` 前缀）
Select Case str
    Case Like "?*@?*.?*"
        ...

    Case Else
        ...
End Select
```

**PROPOSAL C — 对这个打包建议"什么都不做"（现状 + 库调用）**

```vb
' 恒等：`Case CloseButton` 已等价（引用类型默认引用恒等）。
Select Case sender
    Case CloseButton : ...

    Case Else : ...
End Select

' 集合成员：`.Contains` 已足够短。
If bannedWords.Contains(str) Then ...
```

### 权衡：LDM 追问清单

We 按评价标准第五部分的十二条拷问清单逐条过。五个机制各自过，决定性问题集中在 1（歧义）、4（与既有特性交互）、5（breaking change）、8（数据）、9（更简替代）。

#### 1. 语法 / 文法歧义

**`Case AppDomain`（裸类型名）是类型还是表达式？** 家族文法里类型检查是 `'As' TypeName`，表达式模式是 `Expression`。`Case AppDomain` 今天是个错误（类型名不能单独作表达式）——所以"新增类型模式"本身非破坏。但它不消歧：作用域里若有个变量叫 `AppDomain`，`Case AppDomain` 是恒等匹配还是类型匹配？解析器必须做上下文相关的类型/值二元判定。这正是我们此前把 `Select Case TypeOf` 划给模式匹配的原因之一（`proposal-typeof-flow-analysis.md` 决议 #5，事实）：家族文法用 `Case x As T` / `Case As T` 显式区分，"裸类型名"不进入语言。

**`Case Is CloseButton` 与 `Case Is > 5` 抢 `Is` 的解析。** 今天的 `Case Is` 后面必须跟比较操作符（`<`、`>`, `<=`, `>=`, `<>`, `=`），`Case Is 5` 不合法。本建议把 `Is` 扩展成"后面跟任意表达式 = 引用恒等"，与既有比较模式同享 `Is` 前缀。2018.12.19 我们已就 `Is` 表态（事实）："We think `Is` will have ambiguity issues with the existing use for reference equality." 这条担忧在 `If` 形态下最尖锐，在 Case 形态下是"同一个 token 两种含义"——可解析，但认知负担实打实。`Case IsNot MainForm` 则引入 `IsNot` 作为 Case 前缀，与 `Case Is Not ...`（`Is` + 表达式 `Not ...`）在空白层面的亲缘关系也值得警惕。

**`Case Is Like "..."` 的 `Is` 前缀冗余。** 家族文法已有 `| 'Like' StringExpression // Like pattern`——`Case Like "?*@?*.?*"` 就是通配符模式，不需要 `Is`。`Is Like` 的 `Is` 不承载任何语义（`Like` 不是比较操作符，`Case Is Like` 也无法按比较模式解析），纯粹是缀词。

**`Case In bannedWords` 与既有 `In` 的冲突面。** `In` 已是关键字（`For Each x In coll`）。`Case In ...` 是它在 Case 子句首位的新位置，非破坏但扩大语义面；`NotIn` 则是**新关键字**（identifier-collision 破坏风险，见 Q5）。`Probably`：`Case NotIn bannedWords` 中 `NotIn` 也可按 `Not` + `In` 组合解析，但那样 `Not` 的优先级与短路语义需要专门规定——又是一层文法面。

**组合爆炸。** 建议的未决问题 Q1 问"`Case Is NotIn` 之类的 `Is`/`IsNot` × `In`/`NotIn` × `Like` 组合规则是否都应支持"。We 认为这正是家族文法的存在理由：模式经由文法组合（`When`、未来的 and/or/not），而不是给 Case 前缀做笛卡尔积。每加一个前缀维度，解析与文档负担就翻一倍。这是对 PROPOSAL A 最实质的反对。

#### 2. 角案例与边界语义

**`IsNot` 负子句在 first-match 语义下语义怪异。** `Select Case` 是自上而下取第一个命中的分支。`Case IsNot MainForm` 匹配"任何不是 MainForm 的 sender"——包括前面所有分支都没盖住的东西。它实质是"取反的兜底分支"，与分派语句的穷尽/列举本性相悖。2018.12.19 我们对否定模式的态度是（事实）："We are not ready for a negation pattern, but think that would work better than a `DoesntMatch` keyword." `Case IsNot MainForm` 与 `Case IsNot Like "#.#.#.#"` 都是否定模式换了种拼写。`Probably`：如果将来做否定，应当在模式层（`Not` 模式）而不是 Case 前缀层。

**`Like` 与 Option Compare 的交互。** `Like` 的行为受 `Option Compare Binary/Text` 影响（Binary 区分大小写，Text 不区分）。VBScript 血统里 `Like` 也随 `Option Compare`。`Case Like "?*@?*.?*"` 的命中集合因此依赖编译选项——这是真实的语义分叉，必须在 spec 里写清，且宽松/严格两路径必须一致。`Suspect`：VBScript.NET 若默认 `Option Compare Text`，邮箱地址模式的 `?*@?*.?*` 会大小写不敏感匹配，作者可能没意识到。

**`In` 的集合语义。** `Case In bannedWords` 映射 `bannedWords.Contains(str)`。对 `String()`（数组）需要 `Enumerable.Contains`（依赖 `System.Linq` 导入与扩展方法解析）；对 `HashSet` 是 O(1)；对 `IEnumerable(Of String)` 是 O(n)。2018.05.30 我们处理 Issue 305 时已就"`In` 对普通列表"裁过（事实）："This does not seem to have much value - is not significantly more expressive even if shorter - than .Contains against the list. Not moving forward for this reason." `.Contains` 已经够短——`Case In bannedWords` 省掉的字数不构成"第二种做事方式"的理由。泛型集合、数组、查找表的适用类型集合问题（本建议未决问题 Q3）进一步证明这份设计没做完。

**`Nothing` 与值类型。** `Case In` 对 `Nothing` 主语：`Nothing In arr` → `arr.Contains(Nothing)`，语义由集合决定，尚可；`Case Is Nothing`（若采纳恒等形态）与既有 `Case Nothing` 重复。类型模式的 `Nothing` 主语（`TypeOf Nothing Is X` 为 False）在 `proposal-shapeof-pattern-matching.md` 已列为 `Probably` 不命中，本建议未涉及但应继承。

#### 3. 作用域与绑定

`Case AppDomain` 若作为类型模式，绑定到类型符号还是表达式符号？语义模型里 `Case` 子句的 `GetSymbolInfo` 返回什么？这正是裸类型名方案的死穴（Q1）。家族文法的 `Case x As T` 在 2014-02-17 已定作用域（事实）："The scope of the variable declaration is just that case clause, and follows the same principles as other blocks which define variables like ForEach." `In` 的映射是 `Contains` 的调用点：语义模型应报告"重写为 `bannedWords.Contains(str)`"，IDE 的 Find All References 要能跟到重写点——这类"编译期重写"我们已见过（`In` 运算符建议、属性降级），可做但必须写进 spec。

#### 4. 与既有特性的交互

**与家族文法重叠是整场最大的交互问题。** `TypeOf` Case → 已裁决划归模式匹配（`proposal-typeof-flow-analysis.md` 决议 #5，与 #304 一致）；`ShapeOf` 操作符 → 已裁决 Table（`proposal-shapeof-pattern-matching.md`）；元组解构 `Case (latitude, longitude)` → 家族 Phase 2 递归模式（2018.12.19 分阶段："Declaration pattern → Recursive patterns (including tuple patterns)"，事实）；`Like` → 家族文法已有 `'Like' StringExpression`。五样里三样被家族接管，两样（`Is`/`IsNot`、`In`/`NotIn`）与主线判例冲突。**独立实现 = 为同一件事建两套机制。**

**`Case Is > 5` 既有比较模式不变。** 新增 `Case Is <expr>`（恒等）不得影响 `Case Is <compOp> <expr>` 的解析。2018.12.19 对逗号已有裁决（事实）："Our resolution was for the comma to remain a special feature of `Case`, not a part of the pattern syntax." 组合子句 `Case 1 To 10, 47` 保持现状——若 `Case In bannedWords, 5` 允许，逗号规则要与家族文法对表（Design1/Design2 悬案，2014 起）。

**守卫依赖。** 建议 Drawbacks 说"仍需要依赖守卫式 `Case`"——但守卫（`When`）既不在建议里也不在今天的 VB 里（stock VB 无 `Case ... When`）。2018.12.19 我们明确 "We like `When`."。也就是说本建议依赖一个它没设计的机制，而该机制属于家族文法。这是"建议不完整"的直接证据。

**late binding / Option Strict Off。** VBScript.NET 默认宽松。`Case In bannedWords` 在宽松下若 `bannedWords` 是 `Object`，`Contains` 走 late binding；`Like` 始终走运行时 helper。宽松路径必须与严格路径行为一致（`Probably`：需在原型里验证，别让 `In` 在宽松下变成晚期绑定调用而严格下变成 `Enumerable.Contains`——那是隐蔽的语义分裂）。

#### 5. Breaking change 与兼容性

对现有合法代码**无直接破坏**：`Case Is CloseButton`、`Case In ...`、`Case Like ...` 今天都是语法错误，新增形态非破坏。真正要盯的是**新关键字**：`NotIn`（以及家族外的 `ShapeOf`，已在 shapeof 会议记为 identifier-collision 风险）会把原来合法的标识符用法保留字化。`In`、`IsNot`、`Like` 是既有关键字复用，风险在语义面而非词法面。任何把 `Case NotIn bannedWords` 写进现存代码库的用户，其"含 `NotIn` 标识符"的代码会被破坏——我们几乎从不做破坏性变更（设计原则 1，事实）。

#### 6. Option Strict / 编译选项分叉

`Like` 受 `Option Compare` 影响（Q2）；`In` 的 `Contains` 解析受 `Option Strict` 影响（Q4 late binding）；类型匹配是运行时 isinst，不受 `Option Strict` 影响（`Probably`，需实测）。三条路径必须逐一列证。本建议对这三条分叉全部沉默。

#### 7. IDE / IntelliSense

新增五种 Case 子句形态意味着：Case 子句的补全需要区分"表达式起始 vs 模式关键字起始"（`Case <Tab>` 应提示 `Like`、`In`、`Is`、类型名？）；`Case Is Like` 与 `Case Is >` 的 `Is` 后补全不同；`Case In` 后补全集合表达式；`IsNot`/`NotIn` 的语法高亮。IDE 成本随关键字组合数增长——又一个反对前缀组合爆炸的理由。`Probably`：家族文法的单一 Case 子句节点类型（Roslyn 需新增节点）是这些 IDE 特性的统一承载面，比五条散装形态便宜得多。

#### 8. 数据 / 普遍性

按运行时类型分发是真实高频（`proposal-typeof-flow-analysis.md` 已论证守卫惯用法普遍；2018.12.19 我们写 "We all want to do pattern matching, it's the thing we are most excited about after C# interop issues"，事实）。**恒等匹配与集合成员匹配的普遍性存疑**：`Case Is CloseButton` 能用 `Case CloseButton` 表达；`bannedWords.Contains(str)` 已经是一行。2018.05.30 我们判 `In` "Not moving forward for this reason"。通配符匹配 `Case Like` 有真实场景（邮箱/IP 校验、命令路由），且 2018.12.19 文法里已有 `Like` pattern——这个是五样里除类型外最站得住脚的。本建议未提供任何量化数据或用户请求——证据等级止于"已提供/已检查"。

#### 9. 更简替代

- 恒等：`Case CloseButton`（`=` 语义，引用类型默认引用恒等）——已经存在，显式 `Is` 是零增益。
- 集合：`bannedWords.Contains(str)`——主线已裁"不比 `.Contains` 更有表现力"。
- 通配符：`If str Like "?*@?*.?*" Then ...`——正确性等价，但进入 `Select Case` 家族后 `Case Like` 有真实增量（多分支分发 + 家族一致性）。
- 类型：`Case r As Rect` 声明模式——家族 Phase 1，已裁决 Active。

#### 10. 成本 / 优先级

家族 Phase 1（声明模式 `Case x As T` + `When`）已覆盖最高价值子集（类型分发 + 绑定 + 守卫），成本中等、与主线共享。`Case Like` 是文法里已存在的一条产线，落地成本低。`In`/`NotIn` 需要关键字管理 + `Contains` 重写 + 泛型集合角案例，价值已被判否。`Is`/`IsNot` 需要 `Is` 消歧 + 负子句语义，价值可忽略。**优先级结论：做家族 Phase 1 + `Like`，其余不值得。** "值得做但太难"的热情不抵消成本——我们不为 `In` 的热忱破例。

#### 11. 运行时 / CLR 硬约束

无新 CLR 约束。类型匹配走 isinst/castclass（PEVerify 无碍）；`Like` 走 `Microsoft.VisualBasic.CompilerServices.LikeOperator` helper（既有）；`In` 映射库调用。开放泛型类型模式（`Case xs As IEnumerable(Of T)`）沿用 2014-02-17 判例（事实）："this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection. Therefore this scenario is out of consideration." 零反射依赖是硬红线。

#### 12. 值不值得做

逐个子项打分（价值 × 成本 × 风险）：
- 类型分发（家族声明模式）：价值 9 / 成本 4 / 风险 2 → 值得，家族 Phase 1 已接走。
- `Case Like` 通配符：价值 6 / 成本 2 / 风险 1 → 值得，并入家族文法。
- 元组解构 `Case (lat, lon)`：价值 6 / 成本 5 / 风险 2 → 值得但属 Phase 2 递归模式，现在 Table。
- `Case Is`/`IsNot` 恒等：价值 2 / 成本 4 / 风险 5（`Is` 歧义 + 负子句怪异）→ 不值得。
- `Case In`/`NotIn`：价值 3 / 成本 5 / 风险 5（关键字 + 泛型角案例 + 主线判例）→ 不值得。

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — Case 新形态非破坏；`NotIn`/`ShapeOf` 新关键字有 identifier-collision 风险，是减分项。
2. **保持 VB-like** — `Case Like "?*@?*.?*"` 是地道 VB（`Like` 自 VB6/VBScript 血统）；`Case In bannedWords` 读起来像英语但引入第二种做事方式；`Case IsNot MainForm` 不像 VB 的分派句。
3. **不引入"第二种做事方式"** — 恒等 `Case Is` 与既有 `Case <expr>` 重复；`In` 与 `.Contains` 重复（主线 2018.05.30 判例）；违反。
4. **默认跟随 C#，除非有充分理由** — 主线已选家族文法（`Matches` + Case Pattern）；本建议五条散装形态是"另开五条道"，偏离无充分理由。
5. **读起来像英语、对新手友好** — `Case Is Like "..."` 冗长且 `Is` 无意义；`Case Like "..."` 更干净。
6. **不为边缘场景加特性** — 类型分发不是边缘；恒等/集合成员是边缘。
7. **避免隐蔽控制流/语义变化** — `Case IsNot MainForm` 的 first-match 语义变化不透明；`In` 在宽松下 late-bind 与严格下 `Contains` 的潜在分裂是隐蔽语义变化。
8. **不与既有语法冲突** — `Is` 与比较模式冲突面；`In` 与 `For Each In` 冲突面；`Like` 无冲突。
9. **消除常见样板** — 类型分发命中；恒等/集合成员样板本就不多。
10. **冗长只在有用时是美德** — `Is Like` 的 `Is`、`Case Is CloseButton` 的 `Is` 都是无用冗长。

**主线对照（评价标准 2.3 表）**：`Select Case TypeOf` / 模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`，关系标"一致（`Matches` 即 Anthony 提出）"。本次会议细化：**类型分派 + `Like` = 主线一致**（家族文法两阶段）；**`ShapeOf` 操作符、`Is`/`IsNot`、`In`/`NotIn` = Anthony 独立延伸、与主线判例冲突或分叉**（`In` 已被 #305 判否、否定模式主线"未准备好"、`Is` 歧义主线已预警）。

### RESOLUTION:

1. **打包建议不成立，逐项裁定。** 五种机制血统、成本、风险各异，不能一份文档一起判。本建议作为家族入口的**方向**正确，作为**五合一语法**不成立。

2. **类型匹配（`Select Case TypeOf obj / Case AppDomain`）→ 划归模式匹配家族。** 重申 `proposal-typeof-flow-analysis.md` 决议 #5 与主线 #304（"Will consider part of pattern matching"，事实）：经 `Case x As T` 声明模式表达；`Case AppDomain` 裸类型名的"类型 vs 表达式"歧义不进入语言。

3. **`ShapeOf` 操作符 → Table（家族裁定重申）。** `Select Case ShapeOf coordinate` 需要已裁决 Table 的操作符。元组解构 `Case (latitude, longitude)` 属家族 Phase 2 递归模式，现在 Table。

4. **`Case Is`/`IsNot` 恒等 → Table（`Is`）/ 倾向 Reject（`IsNot`）。** `Case CloseButton` 已等价表达引用恒等；`Is` 加重我们 2018.12.19 预警过的歧义；`Case IsNot` 负子句在 first-match 语义下语义怪异，且我们"未准备好否定模式"（2018.12.19）。`Is` 的消歧规则留给家族比较模式文法再议。

5. **`Like` → 并入家族文法，但用 `Case Like "..."` 形态。** 主线 2018.12.19 文法已有 `| 'Like' StringExpression // Like pattern`。`Is Like` 的 `Is` 前缀冗余，弃用；`IsNot Like` 是否定模式，2018.12.19 未准备好，Table。`Like` 与 `Option Compare` 的交互必须写进 spec。

6. **`Case In`/`NotIn` → Reject。** 遵循 2018.05.30 Issue 305 判例（"is not significantly more expressive even if shorter - than .Contains against the list. Not moving forward for this reason"，事实）。`NotIn` 新关键字风险 + 泛型集合角案例未解决，价值不足以破例。`In` 运算符的通用形式在 `proposal-in-notin-operators.md` 单独评估，本建议的 Case 形态不受理。

7. **本建议作为聚合文档 → Table。** 重写为家族文法对齐的 speclet：以 `Case` 子句形态总表的形式并入家族文法讨论（`As`/`Dim` 模式、`Like` 模式、比较/范围/表达式模式、`When`），而不是五条散装语法。

### Implication:

- 家族 Phase 1（声明模式 + `When`）与 `Case Like` 模式进入 Active；类型分发 + 通配符分发的价值由家族交付。
- `ShapeOf` 操作符、`Case Is`/`IsNot`、`Case In`/`NotIn` 不进入 VBScript.NET 1.0 语法面。
- `TODO`（供提案返工）：把五合一拆成家族文法对照表；`Like` 的 `Option Compare` 语义小节；`Is` 消歧规则（比较模式 vs 恒等模式）；`When` 子句设计（本建议缺失却依赖它）；`NotIn` 关键字兼容性分析；与 `proposal-in-notin-operators.md`、`proposal-shapeof-pattern-matching.md`、`proposal-named-patterns.md`、`proposal-string-pattern-matching.md` 交叉标注血缘。
- 逗号组合规则（Design1/Design2 悬案）与 2018.12.19"逗号是 Case 专属特性"的裁决，留给家族统一文法收口。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Case Like` 的贪婪边界——`"a*b" Like "a*b*c"` 的 `*` 匹配是否回溯；需与既有 `Like` 操作符逐字一致（`Probably` 应一致，因复用同一 helper）。
- `OPEN QUESTIONS`：`Like` 模式与 `Option Compare Text` 的交互在 VBScript.NET 默认设置下的行为，待原型实测。
- `OPEN QUESTIONS`：`Case Is <expr>` 恒等形态与家族比较模式的 `Is` 消歧规则，归家族文法统一讨论。
- `TODO`：为"恒等匹配普遍性"补数据（若 `Case Is` 形态要复活，需要真实代码里"必须引用恒等而非 `=`"的场景证据）。
- `Follow-up`：与 `proposal-in-notin-operators.md` 对表——那里把 `In` 作为通用运算符推进，本建议把 `In` 塞进 Case；两份文档必须明确 `In` 的归属（运算符层 or Case 子句层），否则又是两套机制。

### 状态

- **LDM 状态：** 本建议（整体）`Table`；分解后：类型分派 + `Case Like` → `Active`（家族 Phase 1，经重写）；元组解构 → `Table`（Phase 2）；`Case Is` → `Table`；`Case IsNot` → 倾向 `Reject`；`Case In`/`NotIn` → `Reject`（主线判例）。
- **三态判定：Table（聚合文档）** — 方向是家族入口，载体语法不成立，须重写为家族文法 speclet 后再回 Active。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-select-case-enhancements.md`（`Select Case` 增强：TypeOf/ShapeOf/Is/Like/In 分支）
- 来源：Anthony D. Green 原文第 3.3 节 "Select Case"（`..\AnthonyDesign_wordpress.txt` L845–881，逐字示例）
- 配方目标：把"按运行时类型分派 / 对象引用恒等分派 / 集合成员分派"等场景并入 `Select Case`，让它成为统一的分派语句

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|------|----------|----------|
| 效果 | 3/5 | 锚点≈"只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 的真实缺口（`Select Case` 只做等值 → 分发退化为 If 链）成立、示例可演示；但五样里三样被家族接管、两样与主线判例冲突，且无一有原型/运行证实——"统一分派语句"的宣称未达成，`In` 子效果被 2018.05.30 判例直接消退。 | 已提供 / 已检查 | 无原型；最高价值子集（类型分发）重复家族工作；效果宣称过宽 |
| 特性 | 3/5 | 锚点≈"明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Like` 与 `TypeOf` 延续 VB 血统（原则 9 样板消除命中）；但 `In`/`NotIn`、`IsNot` 负子句带外来味，五种无关机制捆绑进一份文档（红旗清单"一份提案混杂多个独立特性"），且恒等/集合成员与既有语法重复（原则 3）。 | 已检查 | 捆绑五个异质机制；`Is` 与比较模式抢 token；负子句语义不 VB |
| 品质 | 3/5 | 锚点≈"缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、Drawbacks 诚实、未决问题 4 个且具体（Q1–Q4）；但无文法/BNF、无角案例小节、无 Option Strict/兼容性/semantic model 分析，且**未标注主线判例**（2018.12.19 已有 `Like` 模式、2018.05.30 已否 `In`）——最直接先例全部缺席。状态行占位链接（项目惯例，不计重）。 | 已检查 | 缺 Grammar 节；未决问题≥4 个 → 效果证据封顶；主线先例未标注 |
| 属性 | 3/5 | 锚点≈"有得有失——某维度受益、某维度受损，文档未充分权衡"。对水（盘活 `Select Case` 分发惯用）、光（VBScript 运行时分发差异化）正向；暗风险：与家族文法/主线判例冲突（`In` 已否、否定未准备好、`Is` 歧义已预警）、`NotIn` 新关键字、`Like` 的 Option Compare 分叉——文档对这些只字未提。 | 已检查（预测待定） | 主线冲突未识别；关键字破坏未分析；Option Compare 分叉缺失 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注；标注与影响有偏差"。来源标注准确（Anthony 3.3 逐字）；但未标注继承 VB6/VBScript 血统的 `Like`（光/水盘活资产）、未标注主线 2018.12.19 文法中已存在的 `Like` pattern 与比较 pattern、未标注 2018.05.30 `In` 判例——最直接的材料血缘全部缺失；`IsNot Like` 与主线"未准备好否定模式"的张力未说明。 | 已检查 | 主线判例未标注；`Like` 血统未标注；否定模式的冲突未说明 |

### 设计原则对照

- **与 VB 基因**：主体偏离——`Like`/`TypeOf` 两样延续 VB 血统，`In`/`IsNot`/`ShapeOf` 操作符与"不引入第二种做事方式"（原则 3）、"避免隐蔽语义变化"（原则 7）、"冗长只在有用时是美德"（原则 10）冲突。
- **与主线关系**：类型分派 + `Case Like` = **主线一致**（2018.12.19 家族文法两阶段）；`ShapeOf` 操作符、`Is`/`IsNot`、`In`/`NotIn` = **Anthony 独立延伸、与主线判例冲突或分叉**（`In` 已否于 #305、否定模式主线未准备好、`Is` 歧义主线已预警）。
- **破坏性变更**：对现有合法代码无直接破坏（新 Case 形态今天都是语法错误）；有标识符级新关键字风险（`NotIn`、`ShapeOf`）与 `Is` 认知负担；`Like` 的 `Option Compare` 分叉属行为分叉而非破坏。

### 总评

- **达成程度**：**部分达成（且作为聚合文档不成立）** — "Select Case 成为统一分派语句"的方向正确，被家族文法接管的子集（类型分派、`Like`）真实有价值；但五合一语法载体不成立，`In`/`IsNot` 与主线判例正面冲突。
- **LDM 三态建议**：整体 **Table**（重写为家族文法 speclet 后回 Active）；分解后：类型分派 + `Case Like` → Active；元组解构 → Table（Phase 2）；`Case Is` → Table；`Case IsNot` → 倾向 Reject；`Case In`/`NotIn` → Reject。
- **主要问题**：① 五个异质机制捆绑、边界模糊；② 未标注最直接主线判例（`In` 已否、`Like` pattern 已存在、否定未准备好）；③ `Case AppDomain` 裸类型名的"类型 vs 表达式"歧义未解；④ 依赖 `When` 守卫却不设计它；⑤ 无文法/角案例/Option Strict/兼容性分析。

### 返工建议

- **补充章节**：重写为家族文法对照表（把五种形态逐条映射到 2018.12.19 Pattern 文法）；`Grammar/BNF`；`Edge cases`（`Nothing`、值类型、泛型集合、`Option Compare` 下的 `Like`、first-match 下的负子句）；`When` 子句设计；`Option Strict`/`Option Compare` 双路径验证；兼容性分析（`NotIn` 新关键字）；semantic model / IDE 影响。
- **补充证据**：`In` 判例与 `Like` pattern 的引用（2018.05.30 / 2018.12.19 逐字）；家族统一文法草案；原型分支与运行结果（状态栏占位链接）；恒等匹配的普遍性数据（若要复活 `Case Is`）。
- **未决问题处理**：Q1（`Is`/`IsNot` × `In`/`NotIn` × `Like` 组合）→ 答案是"归家族文法组合，不做前缀笛卡尔积"；Q2（TypeOf 收窄交互）→ 已由 `proposal-typeof-flow-analysis.md` 决议；Q3（`In` 的适用类型集合）→ 在 `proposal-in-notin-operators.md` 单独评估；Q4（ShapeOf 语法统一）→ 由家族文法收口。
- **设计探索**：与 `proposal-in-notin-operators.md` 明确 `In` 的归属层（运算符 vs Case 子句）；与 `proposal-string-pattern-matching.md` 对表 `Like` 与插值逆运算模式的关系。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）与 `..\..\csharplang-index.md`（T4/T7/T8、M4）。本提案主题（`Select Case` 增强：类型分派、比较/关系模式、模式家族文法）在 C# 侧的对应主线是 **switch 表达式与模式匹配（C# 8/9，已完成）→ 穷尽性类型分派（C# 15 unions / closed hierarchies，开发中）**。本附录不重新判定提案（判定见正文 RESOLUTION 与《附录：特性评价》），只给出「C# 现实方向 vs 本提案响应」的对应、兼容/冲突判断与互操作要点。
>
> 引用纪律：以下 C# 原文均逐字核对自 `..\..\csharplang` 下列文件；来源以 `→ proposals\csharp-8.0\xxx.md` / `→ meetings\2025\LDM-xxxx.md` 标注。无法核实项列入本附录末尾 **OPEN QUESTIONS**。

### 一、相关 C# 现实方向

**(1) C# 8 模式匹配与 switch 表达式（2019，已完成）**

C# 8 把「按形状分派」做成语言特性。`patterns.md` 的 Summary 开宗明义（→ `proposals\csharp-8.0\patterns.md`）：

> "Pattern matching extensions for C# enable many of the benefits of algebraic data types and pattern matching from functional languages, but in a way that smoothly integrates with the feel of the underlying language."

模式被定义为可递归组合的语法单位（→ `proposals\csharp-8.0\patterns.md`，Patterns 节）：

> "Patterns are used in the *is_pattern* operator, in a *switch_statement*, and in a *switch_expression* to express the shape of data against which incoming data  (which we call the input value) is to be compared. Patterns may be recursive so that parts of the data may be matched against sub-patterns."

类型分派以**声明模式**（declaration pattern，`case int x`）表达，其运行期语义与 VB `TypeOf ... Is` + `DirectCast` 同构（isinst + 非 null 测试；→ `proposals\csharp-8.0\patterns.md`，Declaration Pattern 节）：

> "The runtime semantic of this expression is that it tests the runtime type of the left-hand *relational_expression* operand against the *type* in the pattern.  If it is of that runtime type (or some subtype) and not `null`, the result of the `is operator` is `true`."

switch 表达式（C# 8）引入**穷尽性警告**（→ `proposals\csharp-8.0\patterns.md`，Switch Expression 节）：

> "A switch expression is said to be *exhaustive* if some arm of the switch expression handles every value of its input.  The compiler shall produce a warning if a switch expression is not *exhaustive*."

与本提案直接相关的歧义判例：C# 8 文档化了 `is` 右侧「类型 vs 常量」的歧义，并采用「先按类型绑定，失败再按表达式」解决（→ `proposals\csharp-8.0\patterns.md`，Is Expression 节注记）——这正是本会议 `Case AppDomain` 裸类型名 Q1 歧义在 C# 侧的对应处理：

> "There is technically an ambiguity between *type* in an `is-expression` and *constant_pattern*, either of which might be a valid parse of a qualified identifier. We try to bind it as a type for compatibility with previous versions of the language; only if that fails do we resolve it as we do an expression in other contexts, to the first thing found (which must be either a constant or a type). This ambiguity is only present on the right-hand-side of an `is` expression."

**(2) C# 9 模式组合子与关系模式（2020，已完成）**

C# 9 在模式层加入组合（`and`/`or`/`not`）与关系模式（`<`/`<=`/`>`/`>=`）（→ `proposals\csharp-9.0\patterns3.md`）：

> "Pattern *combinators* permit matching both of two different patterns using `and` (this can be extended to any number of patterns by the repeated use of `and`), either of two different patterns using `or` (ditto), or the *negation* of a pattern using `not`."

关系模式（→ `proposals\csharp-9.0\patterns3.md`，Relational Patterns 节）：

> "Relational patterns permit the programmer to express that an input value must satisfy a relational constraint when compared to a constant value"

其示例 `age switch { < 0 => ..., < 2 => ..., _ => ... }` 正是 VB `Case Is > 5` / `Case 1 To 10` 在 C# 侧的等价形态。否定由 `not` 组合子在**模式层**表达（`e is not null`），且组合子可出现在一切模式位置——包括 switch 语句的 case 标签（→ `proposals\csharp-9.0\patterns3.md`，Pattern Combinators 节）：

> "Like all patterns, these combinators can be used in any context in which a pattern is expected, including nested patterns, the *is-pattern-expression*, the *switch-expression*, and the pattern of a switch statement's case label."

**(3) C# 15 unions / closed hierarchies：穷尽性类型分派（开发中）**

C# 15 把「类型分派 + 穷尽性」提升为一等特性。`LDM-2025-08-18.md`（C# 15 Kickoff）确认 unions 为 C# 15 主线（→ `meetings\2025\LDM-2025-08-18.md`，Unions 节）：

> "We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."

- `closed` 类使「派生类集合封闭」，消费方 switch 可据此证穷尽（→ `proposals\closed-hierarchies.md`，Summary）：

> "Since all derived classes are declared in the closed class' assembly, a consuming `switch` expression that covers all of them can be concluded to "exhaust" the closed class - it does not need to provide a default case to avoid warnings."

- union 类型同理（→ `proposals\unions.md`，Union exhaustiveness 节）：

> "A union type is assumed to be "exhausted" by its case types. This means that a `switch` expression is exhaustive if it handles all of a union's case types"

且 union 的类型分派经由类型模式语义（→ `proposals\unions.md`，Union matching 节）："The is-type operator applied to a union type has the same meaning as a type pattern applied to the union type."——对 `Select Case TypeOf` 判定类型分派的运行期语义是直接的参照。

- 互操作关键：这些能力依赖**新元数据**——`IsClosedTypeAttribute` 与 `[CompilerFeatureRequired("ClosedClasses")]`（→ `proposals\closed-hierarchies.md`，Lowering 节）：

> "Closed classes are generated with an `IsClosedType` attribute, to allow them to be recognized by a consuming compiler."
> "Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes."

**(4) VB 安全模型的现实：unsafe-evolution**

C# 15 候选 `proposals\unsafe-evolution.md` 对 VB 有明确表态（→ `proposals\unsafe-evolution.md`，VB 节）：

> "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."

对本提案无直接语义影响，但提示：VB 编译器必须**识别 C# 侧新增的元数据属性**（见第三节第 3 条）。

### 二、现实 vs 提案

| 本提案形态 | C# 现实对应 | 判定 | 理由 |
|---|---|---|---|
| `Case x As T`（家族声明模式，类型分派） | C# 8 declaration pattern；C# 15 closed hierarchies 穷尽 | **兼容 / 同向** | 运行期语义一致（isinst + 非 null，零反射）；C# 已实现，VB 家族 Phase 1 与之对齐，且 C# 15 给出穷尽性演进方向 |
| `Case AppDomain`（裸类型名） | C# 8 文档化的 `is` 类型/常量歧义，采「先按类型绑定」 | **兼容（避坑参考）** | 本会议 Q1 的「类型 vs 表达式」歧义，C# 用 try-type-first 解决并写进 spec；VB 家族文法用 `As T` 显式声明，方向一致 |
| 元组解构 `Case (lat, lon)` | C# 8 positional / recursive pattern（`(a, b)` 解构） | **兼容 / 同向** | 本会议已判属家族 Phase 2；C# 8 已有参照实现 |
| `Case Is > 5` / `Case 1 To 10`（比较/范围） | C# 9 relational patterns（switch 表达式内 `< 0` 等） | **兼容 / 同向** | 同一语义；C# 把关系比较泛化到任意模式位置并可与 `and`/`or` 组合 |
| `Case Is` / `Case IsNot`（恒等 + 否定子句） | C# 无专用恒等模式；否定由 `not` 组合子在模式层表达 | **冲突 / 需桥接** | C# 的架构选择是「否定/组合放模式层，不放 Case 前缀」——与本会议 2018.12.19「否定应在模式层」的裁决一致；若 VB 将来要否定，应仿 `not` 组合子而非 `Case IsNot` |
| `Case Like "..."`（通配符） | C# 无 `Like`；生态用 `Regex` / 字符串 API | **脱节（VB 特色）** | 无 C# 对应物，互操作无冲突，也无先例可借；贪婪边界与 `Option Compare` 分叉须在 spec 内自证（正文 OPEN QUESTIONS） |
| `Case In` / `NotIn`（集合成员） | C# 无对应；`.Contains` | **冲突** | 与本会议 #305 判例一致；C# 侧亦无此形态，缺乏生态先例 |

### 三、对 VBScript.NET 的适应建议

1. **家族文法优先，且对齐 C# 模式文法。** C# 9 已给出成熟的模式组合文法（parenthesized、`and`/`or`/`not`、嵌套、任意模式位置）。VB 家族文法（`As`/`Dim`/`Like`/比较/范围/`When`）若在组合与嵌套规则上与 C# 对齐，则 C#↔VB 同一代码库内模式语义一致、心智迁移成本最低。**建议**：family speclet 直接参考 `proposals\csharp-9.0\patterns3.md` 的文法组织与子sumption 诊断（不可达分支、重叠分支）。
2. **`Case x As T` 声明模式沿用 C# 8 运行期语义。** isinst + cast + 非 null，零反射——与本会议硬红线一致，也是 C# Declaration Pattern 的参照实现，跨语言行为可对齐。
3. **识别新元数据以消费 C# 15 类型。** C# 15 后 `closed` 类 / union 类型以 `IsClosedTypeAttribute`、`UnionAttribute`、`[CompilerFeatureRequired("ClosedClasses")]` 进入元数据。VBScript.NET 若要在 `Select Case`/家族模式中正确判定「类型分派是否穷尽」或「能否派生于 closed 类」，VB 编译器必须**识别这些属性**（决策文件 M4/M8 同一结论）。建议列入 `.vbx` 元数据识别清单。
4. **否定模式照抄 `not` 组合子，不做 `Case IsNot` 前缀。** C# 9 的 `not` 已在模式层解决否定，并落入统一的 subsumption/穷尽诊断；本会议倾向 Reject `Case IsNot`，C# 经验支持该裁决，并给出替代出口（模式层 `Not` 模式，若将来做）。
5. **`Like` 保持 VB 特色，但语义自证。** C# 无 `Like`，无互操作冲突；但贪婪边界（`*` 是否回溯）、`Option Compare Binary/Text` 分叉（正文 OPEN QUESTIONS）必须在 spec 内闭合，不能依赖「以后抄 C#」。
6. **默认安全 / 按需动态。** `Case In` 等依赖 `Contains` 的形态在 `Option Strict Off` 下会变 late-bound 调用，与 C# 的 AOT/类型系统方向相悖（索引 T7）。若 `.vbx` 愿景含 NativeAOT，`Select Case` 新形态应默认走强类型解析，late-bound 仅作显式兼容层。

### 四、对既有 RESOLUTION / 三态判定的影响

C# 现实**整体确认**正文 RESOLUTION，无推翻项：

- **RESOLUTION #2**（类型分派→家族声明模式）：C# 8 declaration pattern + C# 15 closed hierarchies 穷尽性，双重背书。无变更。
- **RESOLUTION #4**（`Case Is` Table / `Case IsNot` 倾向 Reject）：C# 无专用恒等模式；`not` 组合子证明「否定应在模式层」。**确认**。若 `Case Is` 将来复活，`Is` 消歧可参考 C# 8「try type first」的文档化歧义处理。
- **RESOLUTION #5**（`Case Like` 并入家族、`Is Like` 弃用）：C# 无 `Like`，无干扰；`IsNot Like` 维持 Table 与 C# `not` 模式层方向一致。**确认**。
- **RESOLUTION #6**（`Case In`/`NotIn` Reject）：C# 无此形态。**确认**。
- **RESOLUTION #7**（聚合文档→家族 speclet）：C# 9 组合文法可作 speclet 的**参照实现**，降低重写成本。**确认**，并建议在 speclet 中显式标注与 `patterns3.md` 的对应关系。
- **三态判定（整体 Table）不变。**

### 引用纪律与本附录未决问题

本附录所引 C# 原文均逐字核对自 `..\..\csharplang`：
- `proposals\csharp-8.0\patterns.md`（Summary / Is Expression / Declaration Pattern / Switch Expression 节）
- `proposals\csharp-9.0\patterns3.md`（Relational Patterns / Pattern Combinators 节）
- `proposals\closed-hierarchies.md`（Summary / Lowering 节）
- `proposals\unions.md`（Union matching / Union exhaustiveness 节）
- `proposals\unsafe-evolution.md`（VB 节）
- `meetings\2025\LDM-2025-08-18.md`（Unions / Switch statement-expression improvements 节）

- `OPEN QUESTIONS`：C# 对 switch 语句（非表达式）的改进仅有 Kickoff 一句 "Block bodies for switch expressions and improvements to the switch statement have been on our minds for a while."（→ `meetings\2025\LDM-2025-08-18.md`），无设计正文，本附录不做进一步推断。
- `OPEN QUESTIONS`：C# `dynamic` 表达式作 switch 输入 / 模式输入时的精确静态与运行期语义，本库未深挖（索引 T7 指出动态被 AOT/trimming 视为负担）；不影响本提案结论，但影响「`Case In` 宽松路径」的对照表述。
