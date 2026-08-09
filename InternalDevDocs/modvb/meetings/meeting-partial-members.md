# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。可空性流分析与 ShapeOf 模式匹配之后，我们回到 Anthony 原文第 13 章"声明式编程"家族：Smart Attributes（13.1）、Replacement Modifiers（13.2）、Partial 成员（13.3）、语义预处理（13.4）。四份建议里本建议最薄——Anthony 原文 13.3 只有十行。但正因为薄，它**没有写出来的东西**比写出来的多：一场讨论下来我们最大的收获是，`Partial` 方法的元数据合并**大部分已经在语言规范里了**。这一事实改变了整场辩论的方向。

## Agenda

* [Proposal: Partial 成员（Partial Members）](#proposal-partial-成员partial-members)

## Proposal: Partial 成员（Partial Members）

_Related: vblang 2014-02-17 LDM #20 – Partial modules and interfaces（"partial methods ... will just drop out"）；vblang 2014-02-10 LDM – `Partial Private Sub New` 的 Roslyn breaking change；[vblang #107 – Replaceable Members](https://github.com/dotnet/vblang/issues/107)；[vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；ModVB：`proposal-replacement-modifiers.md`（13.2）、`proposal-smart-attributes.md`（13.1）、`proposal-semantic-preprocessing.md`（13.4）_

### 场景与缺口

建议声称的缺口：`Partial` 只作用于类型级，开发者无法在另一个文件中给既有成员补元数据——特性、文档注释、`Handles`、`Implements`、`Overrides` 子句。现在只能改写原成员或绕道代码生成。期望源码生成器能在分部文件中补充元数据，而无需重写整个成员。

开场我们就做了一件 LDM 应该做的事：**回读规范**。`..\..\vblang\spec/type-members.md` §Partial Methods 白纸黑字写着：

> "Attributes on the partial method and the method that supplies its body are merged, as are any attributes on the methods' parameters. Similarly, the list of events that the methods handle is merged."

并且给出了 `Handles` 合并的实例——声明部分 `Handles Me.E1`、实现部分 `Handles Me.E2`，注释明言 "Handles both E1 and E2"。也就是说，**`Private Sub` 场景下的特性合并与 `Handles` 合并早已是语言现状**。本建议的核心卖点"编译器合并所有 partial 元数据"对方法而言大半不是新东西。

那么真正的缺口收窄为四件事：

1. **访问修饰符**：规范要求 "The partial method declaration must be declared as `Private`"。非 `Private` 成员的元数据合并确实做不到。
2. **`Function`**：规范要求 "must always be a subroutine"（只能是 `Sub`）。带返回值的 `Partial Function` 今天不可声明。
3. **非方法成员**：属性、事件、字段的 `Partial` 语义——规范完全沉默，纯属空白。
4. **`Implements` / `Overrides` 放宽**：规范明说 "Partial methods cannot themselves implement interface methods"；而 `Overrides` 受到 "same declaration modifiers" 约束。

我们 **`Suspect`** Anthony 13.3 的十行笔记没有对照规范就当成新建议递了上来；也可能他默认读者知道现状。无论哪种，建议都必须基于真实增量改写，否则是在给已经存在的东西颁发许可证。

### 候选方案

**PROPOSAL A — 建议原文全量。** `Partial` 应用到任意类型成员、任意访问修饰符；`Partial Sub` 不再要求 `End Sub`（空声明即"无主体"）；合并特性/文档注释/`Handles`/`Implements`/`Overrides`。

**PROPOSAL B — 保守最小增量。** 保留今天的语法区分（声明部分 = `Partial` + 空体 + `End Sub`；实现部分 = 非 `Partial` + 主体）。只扩展两点：声明部分可携带特性与文档注释（`Private Sub` 下规范已允许，补明文与访问修饰符放开）；访问修饰符放开到 `Private` 以外、允许 `Function`，配套规则**"非 `Private` 或 `Function` 的分部成员必须恰有一个实现，不得 drop-out"**。不合并 `Handles`/`Implements`/`Overrides`（现状部分保留、放宽部分后置），不扩展到非方法成员。

**PROPOSAL C — 元数据全谱。** 在 B 基础上，允许声明部分合并 `Implements` 与 `Overrides`（放宽规范的 "same declaration modifiers" 与 "cannot themselves implement interface methods"），并扩展到属性/事件/字段。这是 Anthony 13.3 的完整主张。

**PROPOSAL D — 不做，委托生成器。** 主线 2017-11-15 LDM 已决定"生成器优先"：`INotifyPropertyChanged` 是 source generator 的 poster child，`Replaces` w/ source generators 会覆盖多数场景。本建议的窄缺口（非 `Private` 成员元数据合并）先记录为需求，不做语言改动，等生成器生态自行消化。

### 权衡：Q&A

- **A vs B：`End Sub` 省略值不值？** 不值，且危险。见下方"深度追问 1"与"5"——它同时违反原则 #3（引入第二种结束方式）与 #7（隐蔽语义变化），收益只是少打一行。**结论：拒绝。** 保留 `End Sub` 空体作为"无主体"的唯一表达。
- **B vs C：`Handles`/`Implements`/`Overrides` 合并不是 Anthony 想要的核心吗？** 是，但规范里 `Handles` 合并**已经存在**（C1 示例），不用等 C；`Implements` 声明部分今天被明令禁止，`Overrides` 受 "same declaration modifiers" 约束，放开这两处是**绑定层**改动——谁有主体、谁映射接口、基类方法是否存在，都需要编译器在合并前就搞清楚的语义。我们不把绑定层设计欠账混进"元数据合并"的最小交付。**结论：`Handles` 保持现状；`Implements`/`Overrides` 放宽与属性/事件/字段 partial 一并 Table。**
- **A 的"两套声明各带主体"歧义怎么破？** 规范已经给了答案：声明部分与实现部分用 `Partial` 关键字的**有无**区分，且 "Only one method can supply a body"。A 让两个分部都能标 `Partial`，等于**抹掉了这个语法标记**——两个空体分部摆在面前，编译器无法判断哪个是"空实现"哪个是"元数据声明"。这是 A 与 C 共有的设计洞。**结论：保留"声明部分带 `Partial`、实现部分不带"的区分。**
- **B 的"非 `Private` 不得 drop-out"是不是照搬 C# 9？** 是同一个方向，但锚点不同。C# 9 放宽了 partial method 的访问性并规定非 private 必须有实现；我们采用相同规则，理由是 **VB 自己的 drop-out 设计**：规范说 partial method "have no cost if they are not used"，调用被忽略、实参不求值——这个效率故事只对 `Private Sub` 成立，因为调用者都在类型内部。一个 `Public` 方法不能凭空消失，外部调用者的二进制会断。所以规则必须收敛为：`Private Sub` 保留 drop-out（现状），其余必须恰一实现。
- **D 够吗？** 不够，但它的分量被低估了。2017-11-15 我们明确说过 "We need to know the status of source generators ... before deciding"，并判 `INotifyPropertyChanged` 归生成器。生成器能生成"整个成员"并带属性，也能 emit `partial void OnX()` 让手写方实现——真正做不到的只有**给手写成员本身标注属性**，而且今天连 `Private Sub` 都已被 partial 方法覆盖。所以本建议的真实增量（非 `Private` + `Function` + 非方法成员）里，只有前两项是生成器做不了的。**结论：D 作为整体否决，但其论证要求 B 不得把生成器场景的既有路径抢走。**
- **建议原文自身的自洽性？** 差。Handwritten.vb 声称"主体写在手写文件中"但示例代码只有空 `End Sub`，没有一行主体；ToolGenerated.vb 的注释"若为 Partial 函数也可以在此声明实现？"是一句悬而未答的疑问，不该出现在 Detailed design 里。我们把这两处记入品质扣分。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`End Sub` 省略是歧义与隐蔽语义的来源。规范今天的文法（`type-members.md` §Constructors 之后的成员文法，partial 声明要求 `'End' 'Sub'`）把 `Partial Sub Foo()` 定为一个完整声明。若删掉 `End Sub`：

```vb
' A 方案下，这两行是"一个声明 + 下一成员"还是"一个错误的主体"？
Partial Sub Foo()
Partial Sub Bar()
```

解析器要靠前瞻区分"声明完毕"与"以下是主体"；更糟的是静默情形：

```vb
Partial Sub Foo()
' 忘了写 End Sub，也忘了写主体 —— 编译通过，方法 drop-out，无人知晓。
```

作者以为写了一个方法，编译器认为写了一个空声明。这是原则 #7 的教科书案例。保留 `End Sub`（空体）后问题消失——"无主体"有且仅有一种写法，`Partial` 的有无仍标记声明/实现角色。

#### 2. 角案例与边界语义

- **双主体**：规范已定 "Only one method can supply a body"。两个分部都带语句 → 编译错误（现状保持）。
- **零主体**：`Private Sub` → drop-out（现状，规范明言 "the call is ignored"）；非 `Private` / `Function` → 编译错误（B 的配套规则）。
- **空实现歧义**（B 要避免的洞）：今天"空体 + `Partial`"是声明部分，"空体 + 非 `Partial`"是合法空实现。一旦允许实现部分也标 `Partial`，这个判别消失——这是 A/C 必须引入"空体即无主体"规则的原因，而我们拒绝付出这个代价。
- **`Partial Function` 的声明部分**：声明部分无主体就无法提供返回值，所以它只能是元数据载体，返回值由实现部分承担。若零实现 → 错误（不能 drop-out，调用者依赖返回值）。
- **属性/事件/字段**：字段没有"主体"概念，"分部字段"语义未知；属性有 accessor，"分部属性"是合并 accessor 还是合并元数据？事件有 `Handles`/`RaiseEvent`，与 `WithEvents` 交互。**全部 Table**。

#### 3. 作用域与绑定

合并后语义模型应返回**单一成员符号**，属性来自所有分部合并；`Me.OnModelLoaded()` 绑定到合并成员。`Implements` 放宽要求类型已声明接口——partial type 合并后跨文件成立，但成员级 `Implements` 的绑定必须等类型合并完成后进行，这是一条新规则（今天声明部分被禁，无需处理）。文档注释的展示策略（拼接 vs 首个非空）与属性合并顺序都需要定义——**属性顺序**规范没有写，`Probably`：按分部文件的出现顺序，需查现有实现先例。

#### 4. 与既有特性的交互

- **drop-out 效率契约**：规范强调 partial method "have no cost if they are not used"——实参不求值。这是 `Private Sub` 的专属卖点；扩展到非 `Private`/`Function` 时契约失效，必须换成"恰一实现"。
- **`Handles` 合并已存在**（C1 示例）；`Private Sub` drop-out 时 `Handles` 静默失效——这是现状，不是本建议新增。
- **`Overrides` 与 "same declaration modifiers"**：规范要求实现部分与声明部分修饰符相同。今天想合并 `Overrides`，两个分部都得写 `Overrides`。放宽此约束是绑定层变更 → Table。
- **`Partial` ∩ `Replaceable`**（13.2）：一个成员同时 `Partial` 与 `Replaceable`？元数据合并与"工具替换实现"叠床架屋，未定义。
- **CallerInfo / 表达式树 / 扩展方法**：声明部分无主体，不涉及。

#### 5. Breaking change 与兼容性

- **`End Sub` 省略**：不破坏旧代码（只是放宽文法），但制造新的静默失败面。拒绝后此风险消失。
- **访问修饰符放开**：新代码风险。若允许 `Public Partial Sub` 且无实现 → 我们规定为错误；若实现部分被删除，非 `Private` 分部不能再静默消失——这是与现状不同的**新保证**，但也是新的错误面（之前 private 是静默的）。
- **`same declaration modifiers` 与规范自相矛盾**：规范第 1169 行要求实现部分与声明部分 "same declaration modifiers"，但同节示例 a.vb/b.vb 里声明部分是 `Private Partial Sub`、实现部分是 `Public Sub`。我们 `Suspect` 规范内部不一致——这正是"访问修饰符放开"到底放宽到什么程度的未解之谜，须实现者裁定。
- **attribute 合并顺序**：合并已存在，但顺序（`GetCustomAttributes` 的返回序）规范未定；若本建议重新定义顺序，属破坏性变更。`Probably`：沿用现有实现行为，不重新定义。

#### 6. Option Strict / 编译选项分叉

无实质分叉。本特性与类型推导、晚期绑定无关；`Handles` 的事件绑定不受 Option Strict 影响。严格/宽松两路径行为一致。唯一注意点：宽松路径下文档注释与特性合并同样生效，无差异。

#### 7. IDE / IntelliSense 影响

- 无 `End Sub` 的声明在编辑器如何折叠与着色；`Partial` 字形。
- Go to Definition：从声明部分跳实现部分，还是合并成员？
- 文档注释合并的展示；特性冲突（非 `AllowMultiple` 两分部都有）的诊断文案。
- 断点：声明部分无语句，断点落在实现部分。这些不进规范等于没设计。

#### 8. 数据 / 普遍性

跨文件给**非 `Private`** 成员补元数据的场景，受益者是源码生成器作者——而主线 2017-11-15 的判词是 `INotifyPropertyChanged` 归生成器特性，`Replaces` w/ source generators 会覆盖多数场景。业务用户（"hundreds of thousands of quiet customers"）的刚性需求频次没有数据。`Suspect`：真实但窄，且一半已被现状 partial 方法覆盖。优先级低。

#### 9. 更简替代

- **现有 partial method**：`Private Sub` 下特性/`Handles` 合并已可用——"生成器标注手写 Private Sub"今天就能做。
- **生成器 emit `partial void OnX()`，手写方实现**：C#/VB 已有惯例，覆盖生成器声明、手写实现的场景。
- **analyzer + code fix**：给既有成员加属性（用户触发），非编译期。
- **真正无法替代的缺口**：非 `Private` / `Function` 成员 + 非方法成员。前两者窄而可定义；后者语义未定。

#### 10. 成本 / 优先级

语法面小，但 `Implements`/`Overrides` 放宽与非方法成员触及绑定层，成本中-高。若只做 B 的"访问修饰符 + `Function` 放开 + 恰一实现规则"，成本中低。优先级：应并入 13.2（`Replaceable`/`Replaces`）+ 13.4（`##If MEMBER_EXISTS`）的"生成器协作"工作项统一评估，而不是单独抢发。

#### 11. 运行时 / CLR 硬约束

无。合并是编译期概念，IL 仍是单一成员；drop-out 是既有行为。不触达 CLR 存储规则，PEVerify 无碍。

#### 12. 值不值得做

价值（非 `Private`/`Function` 元数据合并）低-中且真实；成本（若收敛到 B）中低；风险（drop-out 扩展、`End Sub` 歧义、`same declaration modifiers` 语义不清）中。**值得做——但只做 B，且以"保留 `End Sub` + 非 `Private`/`Function` 必须恰一实现"为前提。** 全量 A 或 C 我们不建议做。

### VB 基因对照

- **保持 VB-like（原则 #2）**：复用 `Partial` 关键字不加新词，方向 VB；但去掉 `End Sub` 让 Sub 有了两种结束方式，不 VB。
- **不引入"第二种做事方式"（原则 #3）**：`End Sub` 省略直接违反；而"声明部分带 `Partial`、实现部分不带"恰恰是今天**唯一**消解主体歧义的方式，必须保留。
- **避免隐蔽语义变化（原则 #7）**：`End Sub` 省略 + drop-out 扩展到非 `Private`，双重违反。收敛规则（非 `Private` 必须恰一实现）是对冲。
- **消除常见样板（原则 #9）**：本建议不消除样板，而是新增一种"生成器与手写成员协作"的机制——不在该原则射程内。
- **冗长只在有用时是美德（原则 #10）**：`End Sub` 是成员边界的显式终结符，属于"有用的冗长"。
- **与主线关系（对照表 2.3）**：`Partial` 成员在主线无 active 工作，属 **Anthony 独立延伸**；但它直接踩在主线**既有决定**上——2014-02-17 LDM #20 批准 partial module/interface 时写过 "Partial methods inside will be allowed; they will just drop out. We can't think of any design gotchas."——十年后我们在这里看到了 gotchas。同时与 2017-11-15 的"生成器优先"战略存在张力，本建议不得绕过该战略。
- **一个值得记录的巧合（`Probably`）**：2014-02-10 LDM 里调查 Strict Module 的 "Language designer Anthony D. Green" 与 ModVB 建议的作者 Anthony 高度可能是同一人——他当年就经由 Đonny 的 `Partial Private Sub New` 触及 partial 的边界（Roslyn 按设计禁掉了 partial 构造函数）。这解释了为什么 13.3 会把 `Partial` 往更激进的方向推：这是作者与这个关键字长达十余年的私人关系。

### RESOLUTION:

1. **拒绝 `End Sub` 不再要求。** 保留 `End Sub` 空体作为"无主体"的唯一表达。此条同时违反原则 #3 与 #7，收益为零。
2. **确认现状并重新定位建议。** 特性合并、参数特性合并、`Handles` 合并在 `Private Sub` 场景下**已是语言现状**（`spec/type-members.md` §Partial Methods）。建议必须基于真实增量改写：非 `Private` 访问修饰符、`Function`、非方法成员、`Implements`/`Overrides` 放宽。
3. **原则上采纳 B 的核心：** partial 方法扩展到 `Function` 与非 `Private` 访问修饰符，配套规则**"非 `Private` 或 `Function` 的分部成员必须恰有一个实现，不 drop-out"**。`Private Sub` 保留 drop-out（现状）。方向与 C# 9 一致，但锚点是 VB 自己的 drop-out 设计与规范，不照搬。
4. **保留语法区分：** 声明部分 = `Partial` + 空体 + `End Sub`；实现部分 = 非 `Partial` + 主体。这是消解"谁有主体"歧义的机制，不因本建议废除。
5. **`Implements` 声明部分放宽、`Overrides` "same declaration modifiers" 放宽、属性/事件/字段 `Partial` ⇒ Table。** 都是绑定层改动，须先出绑定设计。
6. **定位：并入"生成器协作"工作项。** 与 13.2（`Replaceable`/`Replaces`）、13.4（`##If MEMBER_EXISTS`）统一评估；2017-11-15 的生成器优先战略是本建议的约束，不是背景板。

### Implication:

- 撰写最小原型：`Partial Function` / 非 `Private` partial + 恰一实现规则 + 声明部分特性/文档注释合并；验证语义模型、`End Sub` 文法改动面与 IDE 补全。
- 起草 speclet：文法改动（partial 声明允许非 `Private`、`Function`）、drop-out 收敛规则、"same declaration modifiers" 的重新定义、attribute 合并顺序（先查现有实现）、`langversion` 门控。
- 与 13.2/13.4 团队对表；决定是否并入"生成器协作"包。
- 补一份 Compatibility 分析：drop-out 保证变化、attribute 顺序、`same declaration modifiers` 放宽逐条列证。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：attribute 合并顺序与重复特性（非 `AllowMultiple` 两分部都有）冲突规则——查现有实现先例后定。
- `OPEN QUESTIONS`：文档注释合并策略（拼接 vs 首个非空 vs 各自保留）。
- `OPEN QUESTIONS`："same declaration modifiers" 与规范自身示例（`Private Partial Sub` + `Public Sub`）矛盾，访问修饰符放开到底放宽到什么程度，待实现者裁定。
- `OPEN QUESTIONS`：非方法成员 `Partial` 的定义（字段无主体、属性 accessor、事件 `Handles`）。
- `OPEN QUESTIONS`：`Partial` ∩ `Replaceable`（13.2）的组合语义。
- `TODO`：访谈生成器作者，量化"标注非 `Private` 手写成员"的真实需求；`Probably` 为低频。
- `TODO`：复查 2017-11-15 之后主线对 source generator 的后续决定。
- `Follow-up`：C# 9 partial method 扩展（任意访问性 + 必须实现）与 VB 的兼容性对照表。

### 状态

- **LDM 状态：Considering（整体）**；B 的"访问修饰符/`Function` 放开 + 恰一实现规则"获原则上采纳（可 Active）；`End Sub` 省略 Reject；`Implements`/`Overrides` 放宽与非方法成员 Table。
- **三态判定：Consider** — 真实增量窄而可定义，但建议原文须返工；先落地 B 的核心子集，其余 Table。

---

## 附录：特性评价

# 建议评价报告：proposal-partial-members.md

## 评价对象

- 建议：proposal-partial-members.md — `Partial` 成员（`Partial` 修饰符扩展到任意类型成员，合并元数据；配套"`End Sub` 不再要求"）
- 来源：Anthony 原文 13.3 "`Partial` members"（`..\AnthonyDesign_wordpress.txt` L2294–2308，仅十行）；相关素材 18.17（L3066，"I personally dislike stitching together statements from across multiple files into one method."）
- 配方目标：源码生成器能在分部文件中为既有成员补充特性/文档注释/`Handles`/`Implements`/`Overrides`，减少对原文件侵入

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。声称的缺口大部分不是缺口——`Private Sub` 下特性/`Handles` 合并已是规范现状（`type-members.md` L1169）；示例依赖被拒的 `End Sub` 省略语法（当前文法不编译）；真实增量（非 `Private`/`Function`）建议未识别为增量 | 已检查 | 无原型；示例与正文冲突（Handwritten.vb 说"主体在手写文件"却无主体语句）；"现在只能改写原成员或绕道代码生成"对 `Private Sub` 不成立 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。复用 `Partial` 关键字属 VB 基因（#2），但捆绑了 `End Sub` 省略（#3 第二种方式）、访问修饰符放开（#7 隐蔽语义变化）、`Implements`/`Overrides`/非方法成员等多项强无关能力 | 已检查 | 访问修饰符放开的方向与 C# 9 一致但未附"非 private 必须实现"规则；未识别现状已覆盖的部分 |
| 品质 | 2/5 | 锚点 2："多处章节缺失/顺序混乱；自相矛盾；示例与正文冲突；来源可疑"。六章节齐全但 Detailed design 近空（两段代码 + 一句注）；示例自相矛盾；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；未引用规范 §Partial Methods——最大的品质缺陷 | 已检查 | 核心事实前提（"合并是新增"）与规范矛盾；4 个未决问题全是核心设计点（效果封顶）；ToolGenerated.vb 注释是悬而未答的疑问而非设计 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风损：`End Sub` 省略制造第二种写法、drop-out 扩展破坏一致性；暗：静默 drop-out、与生成器战略重叠；对雷/水/光增益小 | 已检查（预测待定） | 未对冲 drop-out 扩展风险；与 2017-11-15 生成器优先战略的张力未识别 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。材料=Anthony 13.3 十行（未标章节号）；将规范已存在的合并当作新主张（影响预估不符）；借鉴 C# 9 访问性放宽未标注；未承认 2017-11-15 战略锚点 | 已检查 | 未对照 `spec/type-members.md` §Partial Methods 即行文；无虚构材料，但事实前提错位 |

## 设计原则对照

- **与 VB 基因：偏离为主**。`Partial` 关键字复用（#2）是唯一正分；`End Sub` 省略违反 #3 与 #7；本特性不落在 #9（样板消除）射程内；`End Sub` 属于"有用的冗长"（#10），应保留。
- **与主线关系：Anthony 独立延伸**，但直接踩在主线既有决定上——2014-02-17 #20 批准的 "partial methods ... will just drop out"（当年 "We can't think of any design gotchas"，如今有）；与 2017-11-15 生成器优先战略张力；与规范 §Partial Methods 直接交互。主线无 active 的 partial-member 工作。
- **破坏性变更：潜在有**。`End Sub` 省略（文法放宽但新静默失败面）、drop-out 扩展到非 `Private`（编译产物变化）、attribute 合并顺序若重新定义（反射顺序变化）。建议未做兼容性分析。

## 总评

- **达成程度：未达成**（作为完整建议）。真实增量（非 `Private`/`Function` 元数据合并）窄而可定义，但建议未识别现状、捆绑了应拒的子特性、未做绑定层设计。
- **LDM 三态建议：Consider**。B 的"访问修饰符/`Function` 放开 + 恰一实现规则"子集可 Active；`End Sub` 省略 Reject；`Implements`/`Overrides` 放宽与非方法成员 Table。
- **主要问题**：① 未对照规范即声称"合并元数据"为新增（事实前提错误）；② `End Sub` 省略 = 隐蔽语义变化 + 文法歧义，必须拒绝；③ 访问修饰符放开未配套"非 `Private` 必须恰一实现"规则；④ "same declaration modifiers" 与规范示例自相矛盾，未定夺；⑤ 与生成器战略（2017-11-15）的关系未识别。

## 返工建议

- **补充章节**：对照 `spec/type-members.md` §Partial Methods 重写 Detailed design，明确真实增量；文法/BFN 变更（声明部分允许非 `Private`/`Function`、`End Sub` 保留）；drop-out 收敛规则；attribute 合并顺序；Compatibility/breaking-change 分析；与 13.2/13.4 及 2017-11-15 战略的关系。
- **补充证据**：最小原型（`Partial Function` / 非 `Private` + 恰一实现规则 + 声明部分特性/文档注释合并）；生成器作者访谈量化需求；C# 9 partial method 扩展对照表。
- **未决问题处理**：双主体 → 错误（规范已定）；零主体 → `Private Sub` 保留 drop-out、其余报错；`End Sub` 保留（空体）；`Implements`/`Overrides`/非方法成员 → Table 并移交绑定层设计；`Partial` ∩ `Replaceable` 语义与 13.2 对齐后定。

---

## 附录：C# 生态与互操作考量

> 主题定位：本提案（Partial 成员增强）与 C# 的 **partial 成员家族演进**直接同源——C# 9 扩展 partial methods、C# 13 partial properties/indexers、C# 14 partial events/constructors。这是本批 ModVB 提案中与 C# 关系**最强**的一类：C# 把「partial = 生成器协作的声明/实现分离」当作连续 5 个版本的主线在推进，而本提案想为 VB 做同一件事，还夹带了 C# 没有的 `Handles` 合并与 `End Sub` 空体锚点。索引第四节 6 段已核实原文与本主题无直接对应；索引对该主题也无专节（属 T8「2025–2026 LDM 主线」的相邻条带），故本附录直接到 `..\..\csharplang` 对 partial 家族三份提案与两次 2025 LDM 逐字核实。所有引用见「五、引用纪律」。

### 一、相关 C# 现实方向

**P1 — partial 成员是 C# 连续多版本的「生成器协作」主线（C# 3 → 9 → 13 → 14）。**
- C# 3 原始 partial methods 受三重限制：必须 `void`、不能有 `out`、隐式 `private`，且调用点可整体擦除（drop-out）。动机是设计器生成代码的钩子（逐字）："The original motivation for this feature was source generation in the form of designer generated code." → `proposals\csharp-9.0\extending-partial-methods.md`（Motivation）。
- C# 9 把限制全部移除（逐字）："This proposal aims to remove all restrictions around the signatures of `partial` methods in C#. The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods." → `proposals\csharp-9.0\extending-partial-methods.md`（Summary）。`Language-Version-History.md` C# 9.0 条目压缩为一句（逐字）："partial methods can have any accessibility, return a type other than `void` and use `out` parameters, but must be implemented."
- C# 13 把 partial 扩到属性与索引器（逐字）："Partial properties: allows splitting a property into multiple parts using the `partial` modifier." → `Language-Version-History.md`（C# 13.0 条目）。
- C# 14 扩到事件与构造函数（逐字）："Partial events and constructors: allows the partial modifier on events and constructors to separate declaration and implementation parts." → `Language-Version-History.md`（C# 14.0 条目）。2025-01-22 LDM 把它推进 Working Set（逐字）："We now have those use cases, and we agree that they're sufficient motivation and validation to move forward with." → `meetings\2025\LDM-2025-01-22.md`；Conclusion："Approved, moved into Working Set"。
- 含义：C# 侧这一家族**没有停**，每代往更多成员类型铺；生成器协作是唯一反复申明的动机。本提案（VB 侧 partial 增强）与 C# 的演进完全落在同一战略象限。

**P2 — drop-out 的元数据语义：无实现的 partial 方法不出现在元数据。**
- 逐字："Given they can be erased `private` is the only possible accessibility because the member can't be exposed in assembly metadata." → `proposals\csharp-9.0\extending-partial-methods.md`（Motivation）。
- 这正是正文 C1「方法不存在于元数据 if 无实现」的 C# 一侧确认。VB 与 C# 在「擦除 → 元数据缺席」上**行为一致**：双方都只在编译期擦除调用点，都不向 IL 输出该成员。

**P3 — partial 是纯源级概念；元数据里只有「合并后的普通成员」。**
- 逐字："Only the defining declaration of a partial property participates in lookup, similar to how only the defining declaration of a partial method participates in overload resolution." → `proposals\csharp-13.0\partial-properties.md`（Defining and implementing declarations）。索引器节注释直接给出合并产物的元数据形态（逐字）："results in a merged member emitted to metadata:"。
- 属性合并（逐字）："Similar to partial methods, the attributes in the resulting property are the combined attributes of the parts are concatenated in an unspecified order, and duplicates are not removed." → `proposals\csharp-13.0\partial-properties.md`（Attribute merging）。
- 文档注释合并（逐字）："When doc comments are present on both parts, all the doc comments on the definition part are dropped, and only the doc comments on the implementation part are used." → `proposals\csharp-13.0\partial-properties.md`（Documentation comments）。
- 推论（`Probably`）：partial 本身不进元数据——三份提案均未给 partial 成员定义任何「partial 标记」特性或 `CompilerFeatureRequired` 门控；消费者在元数据里看到的只是普通方法/属性/事件。因此 **.vbx 读取 C# 13/14 产出的 partial 成员无需新元数据识别**；需要处理的只有「合并后的属性顺序/重复」与「doc 以实现部分为准」这两条约定。

**P4 — 角色判定靠「单声明可自明」的语法约定——与正文 RESOLUTION #1/#4 同一哲学。**
- 逐字："It is useful for the compiler to be able to look at a single declaration in isolation and know whether it is a defining or an implementing declaration." → `proposals\csharp-13.0\partial-properties.md`（Remarks）。
- C# 用「访问器全为分号体 = 定义声明」做这个自明标记，并因此禁止 partial auto-property；VB 用 `End Sub` 空体。两边都拒绝「靠两份声明互相推断」的歧义形态——正文拒绝 `End Sub` 省略（RESOLUTION #1）与 C# 的选择是同构的。

**P5 — 接口实现 / 修饰符匹配 / CallerInfo：C# 的既有规则面。**
- 方法级：C# 9 允许 partial 方法参与 `overrides` 与（隐式）接口实现（逐字）："This explicitly allows for `partial` methods to participate in `overrides` and `interface` implementations:" → `proposals\csharp-9.0\extending-partial-methods.md`（Detailed Design）。
- 属性/事件级：C# 13/14 **禁止显式接口实现**（逐字）"A partial property cannot explicitly implement interface properties." → `proposals\csharp-13.0\partial-properties.md`；"Cannot explicitly implement an interface member." → `proposals\csharp-14.0\partial-events-and-constructors.md`（Detailed design）。
- 修饰符匹配：C# 9 与 VB 规范同要求（逐字）"partial declarations and definition signatures must match on all method and parameter modifiers. The only aspects which can differ are parameter names and attribute lists" → `proposals\csharp-9.0\extending-partial-methods.md`（Detailed Design）。
- CallerInfo：C# 为跨部分重复 caller-info 属性立了专门规则（标准修订文）——同一 caller-info 属性不得同时出现在定义与实现部分；仅实现部分出现的被忽略。VB 声明部分今天无主体，看似不涉及，但一旦允许声明部分带属性，就必须补这条规则。

### 二、现实 vs 提案

| 本提案元素 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| RESOLUTION 3 · B 核心（非 `Private` + `Function` + 恰一实现） | C# 9：任意访问性 + 非 void + out，显式访问性必须配定义 | **兼容** | 同一方向、同一「外部可见成员不得凭空消失」论证。正文已锚定「VB drop-out 设计」，不照搬；方向确认无冲突。 |
| drop-out 保留于 `Private Sub` | C# 9：drop-out 仅限**无显式访问性**的 `partial void M();`；显式 `private` 也须有定义 | **需桥接（边界分歧）** | C# 把 drop-out 挂在「无访问性修饰符」上，VB 挂在 `Private` 上。VB 规范要求声明部分必须 `Private` → 全部 VB partial 方法都有显式访问性，按 C# 规则本都不得 drop-out；VB 保留 `Private Sub` drop-out 比 C# 更宽容。跨语言读者会踩到不同的「消失边界」，需写进兼容性对照表（正文 Follow-up 已有此 TODO）。 |
| RESOLUTION 4 · 保留 `End Sub` 空体 / 单声明自明 | C# 13：分号体 = 定义声明（P4） | **兼容（强印证）** | C# 为同一问题选了同一答案；正文 RESOLUTION #1/#4 可从 C# 侧获得独立佐证。 |
| RESOLUTION 5 · `Implements`/`Overrides` 放宽（Table） | 方法级有先例（P5，C# 9 允许参与 override/接口实现）；属性/事件级 C# 13/14 禁显式接口实现 | **方法同向、非方法冲突** | 方法级放宽有 C# 9 背书；但 VB `Implements` 子句是显式风格，对属性/事件而言 Anthony 的「声明部分带 `Implements`」目标与 C# 13/14 的禁令正相反——Table 时应把「隐式满足接口（同 C# 9）可行、显式 `Implements` 无 C# 先例且被禁」写明。 |
| RESOLUTION 5 · 属性/事件/字段 partial（Table） | C# 13/14 已 shipped 属性 + 事件/构造函数语义 | **需桥接（先例现成）** | VB 不必从零设计：定义/实现双声明、属性合并、doc 合并、签名匹配、CallerInfo 规则均可借用。字段 C# 明确不推进（逐字）："We could also go even further in permitting partial declarations of constructors, operators, fields, and so on, but it's unclear if the design burden of these is justified" → `proposals\csharp-13.0\partial-properties.md`（Open Issues）——与正文对「分部字段」的犹豫一致。 |
| OPEN QUESTIONS · 文档注释合并策略 | C#：定义部分 doc 全弃、实现部分生效（P3） | **可采纳先例** | .vbx 若采用 C# 约定，与 C# 库的 doc 行为一致；但「拼接/各自保留」直觉与 C# 不同，须显式选边并写清理由。 |
| OPEN QUESTIONS · attribute 合并顺序 / 非 `AllowMultiple` 冲突 | C#：拼接、顺序未定、不去重（P3） | **可采纳先例（冲突未解）** | 顺序未定两边一致；非 `AllowMultiple` 重复时 C# 只定「不去重」、未裁定反射冲突 → 该 OPEN QUESTION 在 C# 侧同样开放，以「不去重」为基线即可。 |
| `Handles` 合并（现状） | C# 无 `Handles`；事件只有 add/remove | **VB 特色，无冲突** | C# 14 partial events 是 non-field-like（逐字）："It does not have any backing storage or accessors generated by the compiler." → `proposals\csharp-14.0\partial-events-and-constructors.md`——与 VB `WithEvents`/`RaiseEvent`/`Handles` 语义无对应，属 VB 独有面，桥接点是「事件 add/remove 双声明 + VB 事件绑定子句并存」。 |

**脱节提醒（如实说明）**：决策文件的通用适应建议（「默认安全/按需动态」「识别新元数据」「AOT/trimming 张力」）在本提案上**大部分不适用**——partial 是纯编译期概念，不进 IL、不依赖反射、与 AOT 无摩擦；真正适用的只有「source-gen 桥」与「元数据合并约定」两条。本附录不硬凑低层互操作主线。

### 三、对 VBScript.NET 的适应建议

1. **drop-out 边界对齐（默认安全）**：`.vbx` 脚本层应把「无实现的 `Private Sub` drop-out」当作编译期擦除契约来宣传——脚本反射器/宿主若枚举程序集成员，看不到被擦除的方法（与 C# 一致）。若脚本需要反射到全部成员，应引导用「非 `Private`/`Function` 必须恰一实现」的形态（B 核心），而不是放宽 drop-out。
2. **source-gen 桥**：RESOLUTION #6 已把本提案并入「生成器协作」工作项。C# 侧三份提案的动机全是 source generator（P1）——.vbx 的生成器应能 emit「`Partial Function` / 非 `Private` 声明 + 实现」，编译器把两部分合并为单一元数据成员（P3 的 merged member）。C# 9 的 `[RegexGenerated]` 式「声明即生成契约」是可直接复刻的形态。
3. **识别新元数据：本提案不需要，但有一件事要处理**。partial 不进元数据（P3 推论）→ .vbx 读 C# 13/14 程序集无需认 partial 标记；但**合并后的属性顺序**（C# 为「拼接、顺序未定」）与 **doc 注释「实现部分生效」** 是 .vbx 若实现相同合并时必须对齐的两条约定，否则 .vbx 与 C# 产出的 `GetCustomAttributes()` 序 / XML doc 内容会不一致。
4. **事件互操作**：C# 14 non-field-like partial events（无编译器生成后备存储、仅 `+=`/`-=`）与 VB `WithEvents`/`Handles`/`RaiseEvent` 语义无对应。若 .vbx 未来采用 C# 14 语义，必须同时定义「VB 事件绑定子句（`Handles`、`WithEvents`）与 partial 声明部分的关系」——这是正文 Table 的非方法成员条目里**最需要桥接**的一项。
5. **接口内 partial 成员的地雷**：C# 2025-04-07 LDM 承认接口内 partial 成员长期有「隐式 virtual/public」怪癖，且只修属性/事件、不修方法（逐字）："While we're fine with what is effectively a bug fix for partial properties, which have not been out for long, we're not of the opinion that changing the behavior for partial methods in interfaces is worth the effort or risk potential." → `meetings\2025\LDM-2025-04-07.md`。VB 若允许 partial 成员进接口，应**先定 virtual/public 默认语义**，避免重蹈 C# 已承认的坑。

### 四、对既有 RESOLUTION / 三态判定的影响

- **无推翻，有强化与修正。**
- **强化 RESOLUTION #1/#4**（保留 `End Sub`、单声明自明）：C# 13 的「分号体 = 定义声明」与 Remarks 的「单声明自明」论证（P4）为同一决策提供了独立佐证——这不是 VB 保守主义，而是两个语言都选了「显式角色标记」。
- **强化 RESOLUTION #3**（B 核心）：C# 9 的方向完全同向；但兼容性表必须补一行「drop-out 边界分歧」（C# 挂在无访问性修饰符上，VB 挂在 `Private` 上）——这是正文 Follow-up「C# 9 对照表」的实质内容，正文当时只写了「方向一致」。
- **修正 RESOLUTION #5 的 Table 定位**：属性/事件 partial 不再是「纯空白」，C# 13/14 已给出可借用的完整语义（定义/实现双声明、属性合并、doc 合并、签名匹配、CallerInfo 规则）。Table 的「绑定层设计」成本被 C# 前置还清了一部分；但 **`Implements` 显式声明部分对非方法成员与 C# 13/14 禁令冲突**，须在 Table 条目里明确。
- **三态判定（Consider）不变**，但附录给出一条前置条件：若 .vbx 的目标是「与 C# 库源码级互认 partial 成员」，属性/事件 partial 的方向应优先参照 C# 13/14（含其「禁止显式接口实现」），而不是 Anthony 原案的「全谱合并」。

### 五、引用纪律（逐字核实表）

本附录引用的 C# 原文均经 `..\..\csharplang` 逐字 Grep 核实：

| 原文（逐字） | 来源 |
|---|---|
| "The original motivation for this feature was source generation in the form of designer generated code." | `proposals\csharp-9.0\extending-partial-methods.md`（Motivation） |
| "This proposal aims to remove all restrictions around the signatures of `partial` methods in C#. The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods." | `proposals\csharp-9.0\extending-partial-methods.md`（Summary） |
| "Given they can be erased `private` is the only possible accessibility because the member can't be exposed in assembly metadata." | `proposals\csharp-9.0\extending-partial-methods.md`（Motivation） |
| "When a `partial` method has an explicit accessibility modifier the language will require that the declaration has a matching definition even when the accessibility is `private`" | `proposals\csharp-9.0\extending-partial-methods.md`（Detailed Design） |
| "This explicitly allows for `partial` methods to participate in `overrides` and `interface` implementations:" | `proposals\csharp-9.0\extending-partial-methods.md`（Detailed Design） |
| "partial declarations and definition signatures must match on all method and parameter modifiers. The only aspects which can differ are parameter names and attribute lists" | `proposals\csharp-9.0\extending-partial-methods.md`（Detailed Design） |
| "partial methods can have any accessibility, return a type other than `void` and use `out` parameters, but must be implemented." | `Language-Version-History.md`（C# 9.0 条目） |
| "Partial properties: allows splitting a property into multiple parts using the `partial` modifier." | `Language-Version-History.md`（C# 13.0 条目） |
| "Partial events and constructors: allows the partial modifier on events and constructors to separate declaration and implementation parts." | `Language-Version-History.md`（C# 14.0 条目） |
| "A partial property must have one *defining declaration* and one *implementing declaration*." | `proposals\csharp-13.0\partial-properties.md` |
| "Only the defining declaration of a partial property participates in lookup, similar to how only the defining declaration of a partial method participates in overload resolution." | `proposals\csharp-13.0\partial-properties.md` |
| "It is useful for the compiler to be able to look at a single declaration in isolation and know whether it is a defining or an implementing declaration." | `proposals\csharp-13.0\partial-properties.md`（Remarks） |
| "A partial property cannot explicitly implement interface properties." | `proposals\csharp-13.0\partial-properties.md` |
| "Similar to partial methods, the attributes in the resulting property are the combined attributes of the parts are concatenated in an unspecified order, and duplicates are not removed." | `proposals\csharp-13.0\partial-properties.md`（Attribute merging） |
| "When doc comments are present on both parts, all the doc comments on the definition part are dropped, and only the doc comments on the implementation part are used." | `proposals\csharp-13.0\partial-properties.md`（Documentation comments） |
| "We could also go even further in permitting partial declarations of constructors, operators, fields, and so on, but it's unclear if the design burden of these is justified" | `proposals\csharp-13.0\partial-properties.md`（Open Issues） |
| "Allow the `partial` modifier on events and constructors to separate declaration and implementation parts" | `proposals\csharp-14.0\partial-events-and-constructors.md`（Summary） |
| "It does not have any backing storage or accessors generated by the compiler." | `proposals\csharp-14.0\partial-events-and-constructors.md`（Detailed design） |
| "Cannot explicitly implement an interface member." | `proposals\csharp-14.0\partial-events-and-constructors.md`（Detailed design） |
| "We now have those use cases, and we agree that they're sufficient motivation and validation to move forward with." | `meetings\2025\LDM-2025-01-22.md`（Partial events and constructors） |
| "Approved, moved into Working Set" | `meetings\2025\LDM-2025-01-22.md`（Conclusion） |
| "While we're fine with what is effectively a bug fix for partial properties, which have not been out for long, we're not of the opinion that changing the behavior for partial methods in interfaces is worth the effort or risk potential." | `meetings\2025\LDM-2025-04-07.md` |

### OPEN QUESTIONS / Suspect

- `OPEN QUESTIONS`：非 `AllowMultiple` 重复属性在合并后的反射行为——C# 只定「不去重」（P3），未裁定冲突语义；正文同类 OPEN QUESTION 以 C# 为基线，但两边都未闭合。
- `Probably`：partial 不进元数据（P3 推论）——三份提案均无「partial 标记」特性，但无逐字句直接断言「不发射任何标记」；待 .vbx 消费真实 C# 13/14 程序集验证。
- `Suspect`：正文「CallerInfo … 不涉及」一句——C# 13 已为跨部分 caller-info 属性立规则（仅实现部分出现则被忽略），VB 一旦允许声明部分带属性就必须补规则；正文那句只对「今天声明部分无属性」成立。
- `OPEN QUESTIONS`：接口内 partial 成员的 virtual/public 默认语义——C# 2025-04-07 只决定「不修方法行为」，未定新语义；VB 若支持接口内 partial 成员需自定。
