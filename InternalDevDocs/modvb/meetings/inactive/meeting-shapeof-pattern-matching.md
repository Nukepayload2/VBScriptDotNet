# Visual Basic Language Design Meeting
August 8, 2026

## Agenda
* [ModVB Proposal — ShapeOf Pattern Matching](#modvb-proposal--shapeof-pattern-matching)

## ModVB Proposal — ShapeOf Pattern Matching

We are reviewing the ModVB proposal family for possible inclusion in VBScript.NET. Today we took up `ShapeOf` pattern matching, which proposes an operator that matches a value by its runtime type and binds a strongly-typed variable in each branch. We treat this as one slice of the pattern-matching thread we have long wanted — in 2018.12.19 we wrote that pattern matching is "the thing we are most excited about after C# interop issues" — but we are evaluating the *concrete grammar* this proposal puts forward, not just the wish. It was a creative meeting: we went in liking the scenario, and came out liking only a part of the syntax.

_Note on honesty: this note is a review of a ModVB proposal (`proposal-shapeof-pattern-matching.md`, source: Anthony D. Green 第 7 章 "General Pattern Matching"). Statements are layered as 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`. We record rationale so we can return later and see why we did things the way we did._

---

### 场景与缺口

Today, asking "what runtime type is this, and give me a strongly-typed view of it" takes two statements per branch:

```vb
Class USPhoneNumber
End Class
Class InternationalPhoneNumber
End Class
Class DomesticContact
    Sub New(pn As USPhoneNumber)
    End Sub
End Class
Class GlobalContact
    Sub New(pn As InternationalPhoneNumber)
    End Sub
End Class

' Status quo: TypeOf ... Is returns Boolean only; each branch needs its own cast.
Function MakeContact(input As Object) As Object
    If TypeOf input Is USPhoneNumber Then
        Dim pn As USPhoneNumber = DirectCast(input, USPhoneNumber)
        Return New DomesticContact(pn)
    ElseIf TypeOf input Is InternationalPhoneNumber Then
        Dim pn As InternationalPhoneNumber = DirectCast(input, InternationalPhoneNumber)
        Return New GlobalContact(pn)
    Else
        Throw New NotSupportedException()
    End If
End Function
```

There is a clear gap here. `TypeOf ... Is` is a boolean probe, not a dispatch construct; it is also — we have said before — **not an exact-type test but "whether a cast can occur"** (事实，2018.05.30，Issue 305 讨论），which has already caused user confusion. Multi-branch type dispatch degrades into an `If/ElseIf` ladder with repeated casts, and each cast is a place to get the type wrong. The 2017.10.18 principle fits exactly: the *ceremony of inspecting* an object obscures the structure of the data. `Select Case` is the idiomatic VB dispatch statement, but today it only does value equality, ranges and `Is` comparisons — not type dispatch.

We also noted that this matters *more* for the VBScript.NET target than for stock VB: VBScript's heritage is runtime dispatch (`TypeName`, `IsObject`, late binding), and a dispatch-by-runtime-type construct is squarely in that world. `Probably` the strongest scenario data we have is the VBScript dispatch idiom itself, not the (absent) telemetry in the proposal.

---

### 候选方案

We considered three shapes the feature could take.

**PROPOSAL A — 独立 `ShapeOf` 操作符（建议原文，Anthony 第 7 章）**

```vb
' Explicit and implicit `ShapeOf` operators.
' Source: Anthony 第 7 章（原文逐字）。
Select Case ShapeOf input
  Case pn As USPhoneNumber
    Return New DomesticContact(pn)

  Case pn As InternationalPhoneNumber
    Return New GlobalContact(pn)

End Select
```

`Select Case ShapeOf input` 显式交给 `ShapeOf` 做形状（类型）匹配；`Case pn As USPhoneNumber` 声明变量 `pn` 并指定类型，运行时类型命中则绑定并进入分支。同一建议隐含支持表达式形态（原文未展开，但第 7 章上下文有 `If ShapeOf str Is ip As IPEndpoint Then ...`）：

```vb
' PROPOSAL A 的表达式形态 —— 原文第 7 章出现过 `If ShapeOf str Is ip As IPEndpoint`。
If ShapeOf input Is pn As USPhoneNumber Then
    Return New DomesticContact(pn)
End If
```

**PROPOSAL B — 复用 `Select Case`，`Case ... As Type` 作为新子句形态，不引入操作符**

```vb
' PROPOSAL B：`Select Case` 本身不变；`Case pn As USPhoneNumber` 是新增的 Case 子句形态。
' 无关键字。与 2014 预览（`Case b As Button`，事实）和 2018 文法示例（`Case x As String`，事实）一脉相承。
Select Case input
  Case pn As USPhoneNumber
    Return New DomesticContact(pn)

  Case pn As InternationalPhoneNumber
    Return New GlobalContact(pn)

  Case Else
    Throw New NotSupportedException()

End Select
```

**PROPOSAL C — 主线 2018 方向：通用模式匹配 + `Matches` 关键字**

```vb
' PROPOSAL C：vblang 主线 2018.12.19 的方向。`Matches` 是已提议关键字，Case 内仍无关键字。
If input Matches pn As USPhoneNumber Then
    Return New DomesticContact(pn)
End If

Select Case input
  Case pn As USPhoneNumber
    Return New DomesticContact(pn)
End Select
```

`ShapeOf`（若保留）只作为该机制在 `Select Case` 内的一种读法，不与主线文法分叉。

---

### 权衡（LDM 追问清单）

We worked through the twelve-question checklist. The decisive questions were 1, 2, 4, 5 and 9.

**Q1. 语法/文法歧义：`Case pn As USPhoneNumber` 是模式还是声明？**

Both — that is the design intent, and that is precisely the parse question. `Case` 子句今天只接受表达式（等值、范围、`Is` 比较、逗号组合）。`pn As USPhoneNumber` 现在必然是语法错误（`As` 不是表达式操作符），所以解析上我们是在**新增**一种子句形态，不是在歧义化旧形态。我们仍然要决定：是否允许与等值子句逗号合并？

```vb
Case 3, pn As USPhoneNumber     ' Design1（2014）：允许；Design2：禁止，保护未来逗号模式。
Case pn As USPhoneNumber, qn As InternationalPhoneNumber   ' 一个 Case 两个声明 —— 应报错。
```

2014-02-17 的 Design1/Design2 分歧从未收口。We lean toward **Design2's protective stance**（逗号留给未来的组合模式），但代价真实——VB 没有 fall-through、素爱用逗号合并 Case，`Case 3, As String` 这类写法我们喜欢。We are not resolving this today; it belongs to the family grammar.

`If ShapeOf x Is T` 的 `Is` 则是另一类问题：`Is` 右侧现在必须接类型/模式，而 `Is` 已有引用相等的含义。解析需要远视，且把"模式"塞进引用相等操作符的右侧——正是我们 2018.12.19 明确担心过的歧义（"We think `Is` will have ambiguity issues with the existing use for reference equality"）。最后，建议称 `As 类型` "隐含 ShapeOf 语义"——这个隐式触发边界太松。`As T` 在哪些语境是模式、哪些是声明，必须逐语境显式列出（`Case` 子句内、`Matches` 右侧），而不是"隐含生效"。`If input As T Then` 不该成立。

**Q2. 角案例/边界语义**

- **接口**：`Case o As IContact` — 参考转换 isinst，语义清楚，`Probably` 支持。
- **封闭泛型**：`Case xs As IEnumerable(Of Customer)` — isinst 支持，`Probably` 支持。
- **开放泛型**：`Case xs As IEnumerable(Of T)` — **2014-02-17 判例：现行 CLR 无反射不可行，"不会要一个依赖反射的语言特性"，排除。** We stand by that.
- **可空值类型**：`Case n As Integer?` — 主语是 `Integer` 时是否命中？装箱/拆箱语义未定义。`OPEN QUESTION`。
- **值类型**：`Case n As Integer` — 装箱 isinst，可做，但"强类型变量 `n`"在值类型场景下是副本，语义需写清。
- **`Nothing` 主语**：`TypeOf Nothing Is X` 为 False，Case 模式应同样不命中（`Probably`），需显式规范。
- **`Case Else`**：保持现状。Select Case 今天没有穷尽性要求；2018.12.19 我们明确过"今天没有穷尽性，引入即破坏"。不引入。
- **分支顺序**：最具体优先、多分支命中取第一个——与 Select Case 既有行为一致（事实），但类型模式加上继承后"被前面更泛分支遮蔽"是真实陷阱。We think a shadowing warning belongs in the IDE/analyzer, not in the language. `Probably` an analyzer, not a compile error.

**Q3. 作用域与绑定**

`pn` 的生命周期 = 所在 Case 子句（2014 Design1："The scope of the variable declaration is just that case clause, and follows the same principles as other blocks which define variables like ForEach"）。If 形态若存在，主线 2018 文法注释把引入变量的作用域定为所在块、definite assignment 只在命中路径。semantic model：`pn` 是分支局部的新变量，重名分支各自独立作用域，按局部变量惯例遮蔽外层同名变量（`Probably`，需写清）。本建议只写了 `pn As T`（绑定形态），未写 `As T`（只判型不绑定）——2018 文法两个都有，`OPEN QUESTION`。

**Q4. 与既有特性交互**

- **`TypeOf ... Is` 并存**：单分支布尔检查场景，Case 模式仍然多一个绑定能力，不构成完全重叠；但 `If ShapeOf x Is T` 与 `TypeOf x Is T` 就是"第二种做事方式"（设计原则 3，门槛极高）。`Probably` 这是我们对 PROPOSAL A 表达式形态的核心反对。
- **`Select Case` 既有行为**：范围、`Is` 比较、逗号组合全部不变；新增子句形态不得影响既有解析。
- **`Option Strict`**：On 下类型模式是运行时 isinst，宽松下 Object 主语同样走运行时——两条路径行为应一致（`Suspect`：需验证，VBScript.NET 默认宽松，这条很重要）。
- **late binding**：运行时分发是 VBScript 血统的主场，`Probably` 这是本建议最大的加分项。

**Q5. Breaking change**

`Case pn As T` 对现有合法代码零破坏（`As` 非表达式操作符，旧形态不可能合法）。真正要盯的是**新关键字**：`ShapeOf`（以及主线的 `Matches`）都会把原来合法的标识符用法保留字化——任何新关键字都有 identifier-collision 破坏风险。这是对"操作符/关键字方案"的一记实打实的减分，也是我们倾向 B（无关键字）的又一理由。`If ShapeOf x Is T` 的 `Is` 重载是非破坏的，但加重认知负担（Q1）。

**Q6. Option Strict 分叉** — 见 Q4。规则：`Case ... As Type` 是运行时检查，不受 Option Strict On/Off 影响（`Suspect`，需在宽松路径实测）。

**Q7. IDE/IntelliSense 影响**

分支内 `pn` 应显示为强类型局部变量，重命名/查找引用需进入分支作用域；调试器在 Case 断点处应能求值 `pn`。Roslyn 语法模型需新增 Case 子句节点类型。若做遮蔽警告，需要配套 IDE 支持。成本中等，可预期。

**Q8. 数据/普遍性**

主线 2018.12.19 的共识即数据（"we all want to do pattern matching"），VBScript 分发惯用是场景支撑；但本建议自身未提供任何量化数据或用户请求——证据等级止于"已提供/已检查"。We note the proposal does not cite the mainline 2018 `Matches` decision at all, which is the closest precedent on the table.

**Q9. 更简替代：`If TypeOf ... ElseIf` 是否够用？**

正确性上等价。2018.05.30 我们处理 Issue 305（"In" 对类型列表）时曾以"还得额外强转"为由否决过类似思路——Case 模式恰恰**省掉强转**，这是它胜过 if 链的唯一且充分理由。单分支场景我们仍推荐 `TypeOf ... Is`。所以替代方案存在但付出仪式感；我们认可 Case 形态的价值，不认可操作符形态的价值。

**Q10. 成本/优先级**

无关键字 `Case pn As Type`：解析成本中等（Case 子句新增形态 + 作用域管理），与家族文法共享，恰是主线 Phase 1（声明模式）。独立 `ShapeOf` 操作符 + `Is` 重载：高成本的语法面扩张，边际收益低。`Probably` 成本结论：B 值得，A 的操作符部分不值。

**Q11. 运行时/CLR 硬约束**

isinst/castclass 足够，无 PEVerify 问题；值类型走装箱；开放泛型不可行（Q2，2014 判例）；**零反射依赖是硬红线**（事实，2014 判例）。

**Q12. 值不值得做**

逐条打分（价值 × 成本 × 风险）：Case 声明模式 价值 9 / 成本 4 / 风险 2 → 值得；`ShapeOf` 独立操作符 价值 4 / 成本 7 / 风险 8（`Is` 重载 + 新关键字 + 与主线分叉）→ 现在不值得。合起来一句话：**我们要这个能力，不要这个操作符。**

---

### RESOLUTION

1. **场景成立，方向正确。** 按运行时类型做多分支分发是真实高频痛点，消除 `TypeOf ... Is` + `DirectCast` 样板符合我们 2018.12.19 的判断。`Case pn As USPhoneNumber` 的声明模式与我们 2014 预览（`Case b As Button`）和 2018 文法示例（`Case x As String`）一脉相承——**we like it**。

2. **我们不接受把 `ShapeOf` 作为独立操作符引入语言。**
   - `Select Case ShapeOf input` 里的 `ShapeOf` 是多余仪式——2018.12.19 我们已表态 "an additional keyword is not required in all `Case` cases"；
   - `If ShapeOf x Is T` 与 `TypeOf x Is T` 重叠（后者已有流分析，见 `proposal-typeof-flow-analysis.md`），并给 `Is` 增加与引用相等并列的新含义——2018.12.19 我们明确担心过的歧义；
   - 需要绑定的表达式语境，我们按主线用 `Matches`（`If input Matches pn As USPhoneNumber`），不用 `Is` 右侧塞模式。

3. **`ShapeOf` 名称保留给"形状匹配"家族**（解构 `Case (latitude, longitude)`、JSON 模式、用户定义模式方法、命名模式），不作为本建议的独立操作符名。在 `Select Case` 内我们倾向**无关键字**的 `Case pn As Type`。

4. **本建议作为家族 Phase 1 继续推进，但当前文档太薄。** 必须先与 `proposal-select-case-enhancements.md`、`proposal-named-patterns.md`、`proposal-user-defined-pattern-methods.md`、`proposal-json-pattern-matching.md` 归并出统一文法，并补齐下述边界，再回到 Active。

### Implication

- Case 声明模式并入家族 Phase 1（与主线 2018 阶段划分一致：声明模式 → 递归模式 → 之后再看 and/or/not）。
- `ShapeOf` 独立操作符进入 Table，不进入 VBScript.NET 1.0 语法面。
- 家族统一文法需收口的历史悬案（2014 起）：`Case 3, pn As T` 的逗号规则（Design1/Design2）、绑定形态 `pn As T` vs `Dim pn As T`（2018 文法与示例互相矛盾，note 自认 "probably needs a bit more work"）、`Is` 的消歧规则。
- `TODO`（供提案返工）：文法/BNF、`When` 子句、接口/封闭泛型/可空/Nothing、definite assignment 与作用域规范、Option Strict 双路径验证、兼容性分析、semantic model/IDE 影响、主线 2018 `Matches` 对比表。

---

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — `Case pn As T` 零破坏；新关键字（`ShapeOf`/`Matches`）有 identifier-collision 破坏风险，是给操作符方案减分。
2. **保持 VB-like** — `Case pn As Type` 是地道 VB（Select Case 惯用法 + `As Type` 声明式读法）；`If ShapeOf x Is T` 不是。
3. **不引入"第二种做事方式"** — `If ShapeOf x Is T` 与 `TypeOf x Is T` 重复，违反。
4. **默认跟随 C#，除非有充分理由** — C# 用 `is T x` 模式；主线 VB 选 `Matches`/Case 形态；`ShapeOf` 操作符额外再开一条道，偏离无充分理由。
5. **读起来像英语、对新手友好** — "Select Case input / Case pn As USPhoneNumber" 可读；"If ShapeOf x Is pn As T" 拗口。
6. **不为边缘场景加特性** — 类型分发不是边缘，是高频；本建议通过。
7. **避免隐蔽控制流/语义变化** — Case 模式的绑定是显式意图，不是细微字符改语义；`Is` 重载则接近隐蔽语义变化，是反对点。
8. **不与既有语法冲突** — `Is` 重载是冲突面；`As` 无冲突。
9. **消除常见样板** — 这正是本建议的唯一且充分理由。
10. **冗长只在有用时是美德** — `ShapeOf` 操作符是无用冗长；无关键字形态更优。

**主线对照（评价标准 2.3 表）**：`Select Case TypeOf` / 模式匹配在主线 = "最期待、分阶段"，ModVB = `ShapeOf`+`Matches`，关系标为"一致（`Matches` 即 Anthony 提出）"。本次会议细化：**Case 声明模式 = 主线一致**；**`ShapeOf` 独立操作符 = Anthony 独立延伸、与主线分叉**（主线选 `Matches` 且认为 Case 场景无需新关键字）。`ShapeOf` 命名还与家族"形状匹配"（JSON/解构）含义冲突——名不副实。

**Breaking change 结论**：对现有合法代码无破坏；有标识符级的新关键字风险与 `Is` 认知负担，集中于操作符方案。

---

### 三态判定

- **本建议（整体）**：`Table` — 方向正确、载体语法未定、文档太薄，且必须归并进家族文法。
- **分解后**：
  - `Case pn As Type` 声明模式 → **Active**（家族 Phase 1，经返工后）；
  - `ShapeOf` 独立操作符 / `Select Case ShapeOf input` 关键字 → **Table**；
  - `If ShapeOf x Is T` 表达式形态 → 倾向 **Reject**（改用主线 `Matches`），但留到家族文法讨论 `Is` 消歧后最终定夺。

**后续动作**：① 通知提案作者返工并归并家族文法；② `TODO` 清单交给家族统一语法起草；③ 在 `proposal-select-case-enhancements.md` 上开一个"Case 子句形态"分支讨论 Design1/Design2 收口。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-shapeof-pattern-matching.md`（`ShapeOf` 模式匹配）
- 来源：Anthony D. Green 原文第 7 章 "General Pattern Matching"
- 配方目标：让按运行时类型的多分支分发以 `Select Case` 表达，每分支同时完成类型匹配与强类型变量绑定，消除 `TypeOf ... Is` + `DirectCast` 样板。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 3/5 | 核心场景（类型分发 + 绑定）真实、示例可演示（示例能编译）；但证据止于书面、无原型；关键子场景（泛型/接口/可空/`When`）缺失——主效果显现、关键子效果缺失。锚点≈"只覆盖部分场景"。 | 已检查（已提供，未运行） | 无原型；操作符语法未定型；无量化数据 |
| 特性 | 4/5 | `Case pn As Type` 完全延续 VB 基因（Select Case 惯用法 + `As` 声明式），与 2014 预览/2018 文法和谐；但 `ShapeOf` 操作符与 `If ShapeOf x Is T` 带外来味，且与 `TypeOf ... Is` 重叠。锚点≈"主体延续 VB 基因，个别措辞轻微外来味"。 | 已检查 | `Is` 重载；与主线 `Matches` 分叉 |
| 品质 | 3/5 | 六章节齐全、示例来自原文且一致、未决问题 3 个且诚实；但无文法/BNF、无角案例小节、无兼容性/`Option Strict`/semantic model 分析，关键边界含糊（"原文未展开"×3）。锚点≈"缺某一章节或在关键处边界含糊"。状态栏为占位链接（项目惯例，不计重）。 | 已检查 | 无 Grammar 节；边界未定义；未标注主线 2018 `Matches` 先例 |
| 属性 | 3/5 | 对水（盘活 Select Case 分发惯用）、光（VBScript 差异化）正向；暗风险：与主线冲突风险（第二种做事方式）、`Is` 重载、命名与家族"形状匹配"含义冲突、新关键字标识符破坏——文档未充分权衡。锚点≈"有得有失，文档未充分权衡"。 | 已检查（待定，预测性） | 主线冲突未识别；命名冲突；破坏风险未分析 |
| 炼金成分 | 4/5 | 来源标注准确（原文第 7 章逐字示例）；Alternatives 提及 C# 式入口；但未标注 vblang 主线 2018.12.19 `Matches` 决策这一最直接先例，且与家族四份提案的交叉血缘未说明。锚点≈"主要成分标注正确，个别来源略含糊"。 | 已检查 | 主线先例未标注；家族血缘未交叉标注 |

### 设计原则对照
- **与 VB 基因**：主体一致（`Case pn As Type` 声明式 + Select Case 惯用法 + 样板消除）；偏离点 = 独立 `ShapeOf` 操作符与 `If ShapeOf x Is T` 的 `Is` 新含义（违背原则 3"第二种做事方式"、原则 7"隐蔽语义变化"、原则 10"无用冗长"）。
- **与主线关系**：Case 声明模式 = **主线一致**（2014 预览即有此形态，2018 主线把 `Select TypeOf` 并入模式匹配）；`ShapeOf` 操作符 = **Anthony 独立延伸、与主线分叉**（主线选 `Matches`，且认为 Case 场景无需新关键字）。
- **破坏性变更**：对现有合法代码无；有标识符级新关键字风险与 `Is` 认知负担（集中于操作符方案）。

### 总评
- **达成程度**：**部分达成** — 方向（按类型分发 + 绑定）成立且被验证为高频；载体语法（操作符 vs Case 子句 vs `Matches`）未定；边界与家族归并未做。
- **LDM 三态建议**：整体 **Table**；分解后 `Case pn As Type` → Active（家族 Phase 1），`ShapeOf` 操作符 → Table，`If ShapeOf x Is T` 表达式形态 → 倾向 Reject（改用 `Matches`）。
- **主要问题**：① 语法载体悬而未决；② 边界未定义（接口/封闭泛型/可空/`Nothing`/`When`/definite assignment/`Option Strict`）；③ 与家族四份提案语法未统一；④ 无原型与兼容性分析，未标注主线先例。

### 返工建议
- **补充章节**：`Grammar/BNF` 一节（Case 子句新增形态 + `Is` 消歧）；`Edge cases` 一节（接口/封闭泛型/可空/值类型/`Nothing`/分支顺序遮蔽/多命中取第一个）；`When` 子句设计（主线 2018 明确要，本建议完全缺席）；作用域与 definite assignment 规范（Case 子句级 + If 块级）；`Option Strict` 双路径验证；兼容性分析（新关键字标识符破坏）；semantic model / IDE 影响。
- **补充证据**：主线 2018.12.19 `Matches` 决策对比表；家族统一文法草案（与 `proposal-select-case-enhancements.md`、`proposal-named-patterns.md`、`proposal-user-defined-pattern-methods.md`、`proposal-json-pattern-matching.md` 交叉标注）；原型分支与运行结果（状态栏链接）。
- **未决问题处理**：把现有 3 个未决问题升级为规范小节；新增：`Is` 重载消歧规则、`ShapeOf` 命名边界（类型模式 vs 形状匹配家族）、逗号合并 Design1/Design2 收口、绑定形态 `pn As T` vs `Dim pn As T`。

---

## 附录：C# 生态与互操作考量

> 本附录是**追加的考量**，不改写原 meeting 正文。依据 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引）评估本提案在 C#/CLR/.NET 生态现实中的位置。C# 是 CLR 新特性与 .NET 生态的**主要推动者**，VBScript.NET（.vbx）必须能适应这些现实变更；Anthony 主张 VB 保留特色，而 VB LDM 主线曾取"做 C# 变体"、Anthony 离职后仅做兼容——本附录给出「C# 现实方向 vs 本提案响应」的对应。凡引用 C# 原文均逐字准确并标注来源文件；无法核实的标 **Suspect** 或 **OPEN QUESTIONS**。

### 相关 C# 现实方向（索引 T8/M4：unions 与封闭层级）

本提案（按运行时类型做多分支分发 + 每分支强类型变量绑定）在 C# 生态中对应**两条并行主线**。

**主线一：类型模式（declaration pattern）——已落地十年的常态能力。** C# 7 的 `is T x` 与本提案的 `Case pn As Type` 语义同构：运行时类型测试 + 命中时绑定强类型变量。C# 原文：

> "The declaration pattern is useful for performing run-time type tests of reference types, and replaces the idiom"
> ```csharp
> var v = expr as Type;
> ```
> → `proposals\csharp-7.0\pattern-matching.md`

**主线二：unions / closed hierarchies——C# 15 进行中的类型系统扩张（本提案最相关的现实方向）。** C# 15 主线正把「一组封闭类型」做成类型系统头等概念，服务方向是**穷尽性模式匹配（exhaustiveness）**，而不是运行时 isinst 本身。C# 原文：

> "Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive."
> → `proposals\unions.md`（Motivation）

> "Many class types are not intended to be extended by anyone but their authors, but the language provides no way to express that intent, let alone guard against it happening. For consumers of the class this means that no set of derived classes will be considered to "exhaust" the base class, and a switch expression needs to include a catch-all case to avoid warnings."
> → `proposals\closed-hierarchies.md`（Motivation）

> "Our guiding principle for `closed` is that it is an exhaustiveness feature, not merely a way of blocking outside inheritance."
> → `meetings\2026\LDM-2026-04-20.md`

> 时间线：C# 15 Kickoff 上 LDM 对 unions 的表态——"We'll be continuing design work here and are hopeful that C# 15 will at least have preview versions of features in this area."
> → `meetings\2025\LDM-2025-08-18.md`

与本提案最相关的洞察：C# 的穷尽性**不是匹配点的语言特性，而是声明点的类型属性**——类型先声明自己是 closed/union（`public closed class C` / `public union Pet(Cat, Dog)`），编译器据此在 switch 处推导穷尽性。决策文件 M4 的浓缩判断：「C# 15 正做 unions/closed hierarchies/discriminated unions，AOT 驱动『类型系统承担更多职责』」。

### 现实 vs 提案：兼容 / 冲突 / 需桥接

- **能力层：兼容。** `Case pn As Type` 与 C# `is T x` 语义同构（运行时类型测试 + 变量绑定）；C# 从 7.0 到 15 的持续投入（类型模式 → unions/closed hierarchies）证明该能力是长期主线而非一次性想法。本提案「场景成立」的判定被 C# 现实**强化**。
- **方向层：部分张力（需桥接）。** C# 走向是「声明期封闭 → 编译期穷尽」；本提案（及 VB `Select Case` 一贯行为）是「开放运行时分发 + 无穷尽性」——meeting 已明确"今天没有穷尽性，引入即破坏"。二者本身不冲突（C# 的穷尽性只在消费 closed/union 类型时由编译器保证），但存在一个真实的**互操作正确性缺口**：当 .vbx 消费 C# 15 closed 类时，若 VB 编译器不认识 closedness 元数据，就可能允许从该类派生，从而**制造 C# 消费者假设不存在的派生类型**、破坏 C# 侧穷尽性不变量。这是**必须桥接**的点。
- **零反射：一致。** meeting Q11 把「零反射依赖」立为硬红线；C# unions/closed hierarchies 的穷尽性同样靠元数据 + 编译器分析实现、不依赖反射——两条路线在此汇合。

### 对 VBScript.NET 的适应建议

1. **默认安全/按需动态：默认保持开放运行时分发。** `Case pn As Type` 照常以 isinst 实现；VBScript 血统的运行时分发（`TypeName`/`IsObject`/晚期绑定）正是本提案主场，不需要为迎合 C# 方向牺牲。
2. **识别新元数据（必须）。** C# closed 类在元数据中以 `IsClosedType` attribute 标注、构造函数标 `[CompilerFeatureRequired("ClosedClasses")]`（union 类推）。.vbx 编译器至少需要：
   - 把「派生自 closed 类」识别为错误——C# 原文明确这是防**其他语言/编译器**派生的机制（见下）；
   - 可选：当 `Select Case` 主语类型被识别为 closed/union 时，以 **analyzer 提示**缺失分支（而非语言级穷尽性），与 meeting「遮蔽警告属于 IDE/analyzer」的立场一致。

   > "Closed classes are generated with an `IsClosedType` attribute, to allow them to be recognized by a consuming compiler."
   > → `proposals\closed-hierarchies.md`（Lowering）

   > "Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes."
   > → `proposals\closed-hierarchies.md`（Lowering）

3. **source-gen 桥（可选）。** 若未来 .vbx 想要 C# 式编译期穷尽，应走 analyzer/source-gen 而非改语言——与 meeting「穷尽性不引入语言」及家族 Phase 1 范围一致。
4. **不硬凑。** 本提案与索引 T2/T3（Span/ref/unsafe 低层主线）及 T5/T6（AOT/source-gen）关系弱——类型分发不涉及低层内存或生成代码，无直接冲突，无需为此调整。

### 对既有 RESOLUTION/三态判定的影响

C# 生态考量**不改变**原判定，反而**强化**了三点：

1. **`Case pn As Type` 声明模式 → Active 被强化**：C# 类型模式十年主线 + C# 15 unions 均证明「按运行时类型分发并绑定」是长期主流能力方向，本提案方向正确性再获外部证据。
2. **`ShapeOf` 独立操作符 → Table / `If ShapeOf x Is T` 表达式形态 → 倾向 Reject 被强化**：C# 的穷尽性来自**声明期 closedness** 而非匹配点的新关键字，与 meeting「无关键字 Case 形态优于操作符」的结论同构——匹配点仪式（关键字）在 C# 现实中同样不被需要。
3. **新增一项未决义务（原 RESOLUTION 未覆盖）**：.vbx 编译器必须能识别 `IsClosedType` / `[CompilerFeatureRequired("ClosedClasses"|"Union")]` 元数据并阻止派生 closed 类型，否则与 C# 15 生态互操作时会产生违反穷尽性不变量的非法程序。此项应进入家族 Phase 1 的 `TODO` / 兼容性清单。

### OPEN QUESTIONS

- C# union 类型具体依赖哪些编译器识别接口（"union interfaces" / "union member provider"）及其元数据表示，索引未深挖、本附录未核实——需在 `proposals\unions.md` 深挖。
- `Case` 模式消费 C# union 类型时，是否需识别其 "non-boxing access pattern"（`HasValue`/`TryGetValue`）以获得高效模式匹配——本附录未核实。
- closed/union 元数据对既有 VB 编译器（非 .vbx）的破坏面（新增 attribute 是否影响旧编译器对程序集的读取）——本附录未核实，**Suspect**。
