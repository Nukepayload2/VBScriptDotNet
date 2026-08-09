# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周议程很小但牵动一段旧账：提案想新增单行 `#Ignore Warning` 指令，而它恰恰命中了主线 2014 年已经拍板过的话题。我们花了相当篇幅确认当年的决定、核对引文，然后讨论「重开已决事项」需要什么样的新证据。

## Agenda

* [Proposal: #Ignore Warning 指令](#proposal-ignore-warning-指令)

## Proposal: #Ignore Warning 指令

_Related: [vblang 2014-04-16 – #Warning Disable](`../../vblang/meetings/2014/LDM-2014-04-16.md`)；[vblang 2014-07-01 – #Warning Disable revisited](`../../vblang/meetings/2014/LDM-2014-07-01.md`)；[vblang 2018-02-07 – 可空引用类型警告](`../../vblang/meetings/2018/vbldm-notes-2018.02.07.md`)；ModVB：`proposal-semantic-preprocessing.md`_

### 场景与缺口

We started from a concrete, familiar irritation。抑制警告今天需要一对 toggle 指令，把要写的代码夹在中间：

```vb
' 今天：两行式。
#Disable Warning BC42356 ' No `Awaits`; don't care.
Async Function SayHiAsync() As Task
#Enable Warning BC42356

    Console.WriteLine("Hello, World!")

End Function
```

提案（以及 Anthony 原文第 2.6 节）主张：很多场景只是「这一小段我不 care 这个警告」，两行式样板多、且有一个真实脚枪——**忘了 `#Enable` 会静默吞掉整段后续代码的警告**：

```vb
' 脚枪：忘记配对的 #Enable。
#Disable Warning BC42356
Async Function FirstAsync() As Task
    Console.WriteLine("Hello, World!")
End Function

Async Function SecondAsync() As Task   ' ← 本不该被抑制的警告也被吞了。
    Console.WriteLine("Hello, World!")
End Function
```

它提议一行式自界定指令：

```vb
' ModVB 建议：一行式。
#Ignore Warning BC42356 ' No `Awaits`; don't care.
Async Function SayHiAsync() As Task

    Console.WriteLine("Hello, World!")

End Function
```

但我们必须先把背景摆清楚：**这不是一张白纸**。主线在 2014 年 4 月 16 日和 7 月 1 日两次专门讨论过 `#Disable Warning`，当时的 ALTERNATIVE SYNTAXES CONSIDERED 里就有一模一样的候选：

```vb
#pragma disable <id>
#pragma enable <id>

#Disable <id>  ' sounds like you're disabling code, not warnings
#Enable <id>

#Ignore <id>
#End Ignore  ' block structure is worse than toggles

#Disable Warning <id>
#Enable Warning <id>
```

主线当时明确否决了 `#Ignore / #End Ignore` 块式（批注原话：`' block structure is worse than toggles`），并且**单独否决了「下一行抑制」**，原话：

> "Q. Should we add a feature which suppresses the warning merely for the next line, rather than for the rest of the file? -- This isn't feasible in C# where lines don't really exist semantically. It is feasible for VB. Disadvantage: would confusing for LINQ, and not refactoring-safe. ... Conclusion: No we won't add this next-line-suppression feature."

而「toggle vs block」的讨论里也把话挑明了：

> "Well, with blocks you can't do overlapping regions. And you can't easily disable warnings for an entire method but then re-enable them for a small critical region inside ... We believe overall that toggles are nicer. They have a lot of engineering, thought and experience behind them."

所以本建议实际是**重开一项 2014 年已决事项**，且提案正文对这段历史只字未提。我们带着「重开需要什么新证据」的问题进入候选方案。

### 候选方案

**PROPOSAL A — 成员级自界定**（`#Ignore Warning BCxxxxx` = 抑制「当前成员」内的指定警告）。放在 `Async Function` 声明前，抑制面正好覆盖整个函数体——与「这个函数我不 care」的意图最贴合。代价：需要明确的成员边界；在 ModVB 顶层语句 / immersive-file 场景（`proposal-top-level-code.md`）成员边界不存在，需要退路；成员体内的指令与 `#Enable` 的交互需定义。

**PROPOSAL B — 下一语句**（`#Ignore Warning BCxxxxx` = 只抑制紧随其后的语句/声明）。语义最紧、最小惊讶；但这就是 2014 被拒的「next-line suppression」，当时否决理由为「confusing for LINQ, and not refactoring-safe」，且对成员体中间某行需每行贴一条指令。

**PROPOSAL C — 不做新指令，修工具链**。新增「不平衡 `#Disable Warning`（缺配对 `#Enable`）」诊断，并让 lightbulb 的「Suppress in Source」一键插入配对两行。直接命中提案声称的脚枪，零语言表面积。`Suspect`：新诊断若做成编译器警告会有 breaking 风险（TreatWarningsAsErrors），故应以分析器形态先行。

**PROPOSAL D — 维持现状 + `SuppressMessageAttribute`**。成员级/程序集级抑制已经存在，但做不到「文件内某段」精确控制，且污染方法签名、需要 GlobalSuppressions 维护。作为对照保留，不构成替代。

**PROPOSAL E — 粘性单行**（`#Ignore Warning` = 打开抑制，直到下一个指令/文件尾，无需配对关闭）。等于给 `#Disable Warning` 换个名字并取消显式关闭——把「忘记关闭」变成默认行为，比现状更糟，快速否决。

### 权衡：Q&A

- **为什么 2014 的否决仍然成立？** toggle 的两个王牌：① 可重叠区域（`#Disable` 包住整方法、内部 `#Enable` 局部放行）；② 显式关闭使「抑制到哪里」一目了然。提案的「覆盖范围由指令自身界定，不易误扩大」成立的前提是 scope 有定义，而提案的 Unresolved questions 第一个就承认「确切作用范围原文未说明」——**范围未定，自我界定的说法不成立**。
- **这算「第二种做事方式」吗？** 算。2014 之后抑制警告的机制是 `#Disable Warning` / `#Enable Warning`；再引入 `#Ignore Warning`，同一件事两条语法路径。设计原则 #3 的门槛极高。2014 年团队对 `#pragma` 本身的使用率都没把握（原话："Do people really use #pragma much in C# today? Hard to say."），为一个使用率未明的既有功能再开第二条路，证据强度不足。
- **「忘记 `#Enable`」是语言缺陷还是工具缺陷？** We think 它是工具缺陷。2014 的 For-the-future 清单里本就有「lightbulb suppression 统一生成 `#pragma` / `#Disable` 标识符」和「灰显未使用 id」的计划；补一条「缺配对 `#Enable`」诊断（PROPOSAL C）即可命中同一痛点，不需要改语法。
- **sandbox 语境有没有改变天平？** VBScript.NET scripting / immersive-file 没有成员边界，`#Disable Warning` 的「区域」在单文件脚本里可能吞掉整份脚本——这是唯一对我们有意义的新证据。但提案没有提供 scripting 场景的实地体验数据，也没有为 scope 给出 scripting 语境下的定义。`Suspect`：这是「等待信号」而非「已有信号」。
- **与 sibling 建议的耦合该不该算加分？** `proposal-semantic-preprocessing.md` 在 `##If ... ##Else` 示例里直接写了 `#Ignore Warning BC40008`（抑制旧方法调用的 BC40008）。但「另一份建议假设它存在」不是设计理由；若本指令被否，改写成两行式即可。We treat 这为反向责任：**若我们放行 A 或 B，必须同时把该建议的示例改成新语法；若拒绝，必须告知该建议作者改写。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

指令文法本身无歧义：`#Ignore` + `Warning` + 错误号列表，与既有指令 `#Disable Warning` / `#Enable Warning` 同构。但 `#Ignore` 这个关键词的可扩展含义（忽略错误？忽略一段代码？）需要在文法层面锁死为「仅允许后接 `Warning`」。另有真实的视觉约束：VB 预处理指令必须左对齐（2014 原话 "Preprocessor directives look ugly because they're always left-aligned"，该问题至今未解决），`#Ignore Warning` 出现在成员体中间时必须贴列 0，破坏缩进节奏——这是比 C# 更差的嵌入体验。

#### 2. 角案例与边界语义

**scope 三读对同一代码给出不同抑制面。** 放在成员体外时，「下一行」与「整个成员」恰好同结果；放在成员体内时立刻分歧：

```vb
Sub M()
#Ignore Warning BC40008        ' 左对齐约束下必须贴列 0，视觉断裂。
    LegacySave()               ' 「下一语句」：只抑制此行。
    LegacySave()               ' 「整个成员」：此行也被抑制。
End Sub
```

**指令放类级、成员边界外**时，「整个成员」scope 自然止于 `End Function`，后续成员不受影响——这是本设计唯一的「自我界定」形态：

```vb
Class Foo
#Ignore Warning BC42356
    Async Function One() As Task
        Return Task.CompletedTask    ' 无 Await ⇒ 若未抑制会报 BC42356。
    End Function

    Async Function Two() As Task     ' 「整个成员」scope 下：未被抑制，警告仍在。
        Return Task.CompletedTask
    End Function
End Class
```

**与 `#Enable` 的优先级未定义。** toggle 区域先被 `#Enable` 关闭、内部再来一条 `#Ignore`——谁赢？提案未答：

```vb
#Disable Warning BC40008
Sub M()
#Enable Warning BC40008        ' 关闭 toggle 区域。
#Ignore Warning BC40008        ' 若「整个成员」scope：重新打开抑制直到 End Sub ⇒ 与显式 #Enable 矛盾。
    LegacySave()
End Sub
```

**粘性读法（E）的连锁问题**：若 `#Ignore` 是无配对开启，范围直到下一个指令/文件尾，则「忘记显式关闭」重新成为默认行为——提案消灭的脚枪以另一种形态回来。`Probably`：原文「覆盖范围由指令自身界定」的直觉其实最接近 A（成员级），而非 B 或 E；但原文没有说出口。

#### 3. 作用域与绑定

指令的语义实现是给某段「警告抑制范围」（suppression range）挂一组错误号。`#Ignore Warning` 若按 A 实现，需要知道「当前成员的文本跨度」——在 Roslyn VB 语法树里这是可得的（`End Function` / `End Sub` / `End Class` 界定），但在顶层语句 / immersive-file 场景，成员边界退化为「整个文件」，A 与 E 合流。`#Disable` / `#Enable` 的 suppression range 是显式 span，天然免疫此问题。语义模型层面无新符号；诊断过滤发生在 Compilation 层，与 `#Disable` 共用同一管道。

#### 4. 与既有特性的交互

- **`#Disable Warning` / `#Enable Warning`**：核心交互（见角案例）。优先级规则、嵌套、重叠全未定义——提案自己承认。
- **`#If` / `#End If` 条件编译**：被编译掉的代码不产生警告，无交互。但 `##If`（语义预处理）的分支里需要抑制时（sibling 建议的用例），新指令与 toggle 都要能用。
- **`/nowarn` 与 `/warnaserror`**：`#Disable Warning` 在警告被升级为错误时仍能抑制（现有行为）；`#Ignore Warning` 必须保持一致，否则会出现「`#Ignore` 压不住 WarnAsError」的惊喜。提案未提。
- **BC2026**：2014 决定「非存在警告静默，不再报 BC2026」，且「disable/enable 非存在警告就什么都不做」。`#Ignore` 若引入，必须继承同一语义，否则与既有指令行为分叉。
- **可空性警告（若 VBScript.NET 采纳 `proposal-nullable-reference-types.md`）**：可空分析会带来「一堵警告墙」（2018-02-07 原话 "a wall of compiler warnings"）。那时抑制需求会上升——但更宽松的抑制通道恰好与团队对可空特性「we'll postpone this until we understand the uptake」的审慎互为张力。

#### 5. Breaking change 与兼容性

新指令本身是纯增量，不破坏旧代码。但注意两个方向：① 若 PROPOSAL C 的新诊断做成**编译器警告**，会改变既有构建的警告集（TreatWarningsAsErrors 用户直接爆红）——所以 C 必须以分析器形态进入；② 若 A 被接受，「成员级」scope 会让代码移动（重排成员顺序、提取方法）时抑制面静默漂移——2014 对 next-line 的 "not refactoring-safe" 担忧部分适用于 A。兼容性章节提案完全没有。

#### 6. Option Strict / 编译选项分叉

`#Ignore` 本身与 Option Strict 正交，严格/宽松两路径都应一致。但注意：许多宽松路径才有的警告（如 BC42016 隐式转换）在 Strict On 下不出现，「抑制一个永不出现的警告」= 继承「非存在警告静默」语义，无行为分叉。真正的分叉点仍是「WarnAsError 下是否可压」，见 4。

#### 7. IDE / IntelliSense 影响

- lightbulb 的「Suppress in Source」多出一条「单行抑制」选项——2014 已经评估过这个 DX 优势（原话：抑制下一条警告时 "it only has to insert a single line ... rather than having to insert `#ignore` before and `#restore` after"），并仍选择否决。We 不再因这个已知优势重开。
- 2014 的 stretch goal「灰显未使用 id」对 `#Ignore` 同样适用；若接受新指令，灰显与对应 code fix 要各写一份。
- `#Ignore Warning` 嵌入成员体时的左对齐约束（见 1）会让格式化/重构工具增加一类「不允许缩进」的特殊行——IDE 需要新的错误文案与格式化规则。

#### 8. 数据 / 普遍性

提案无任何量化数据。既没有「忘记 `#Enable` 的频率」，也没有「需要成员级单行抑制的场景占比」。2014 年对既有 `#pragma` 使用率都只能说 "Hard to say"；为它开第二条路，普遍性证据必须更强，而这里只有一条。`Suspect`：真实痛点存在（脚枪），但它是工具侧可解的痛点，不是语言缺口。

#### 9. 更简替代

- **PROPOSAL C**（分析器诊断 + lightbulb 一键两行）——最直接地解决脚枪，零语言表面。
- 手动修好代码（VB 传统，见下）——对 BC42356 这类「Async 无 Await」警告尤其成立：删掉 `Async`、真正 `Await`、或返回 Task，警告自然消失。
- `SuppressMessageAttribute` / GlobalSuppressions——成员级与程序集级已有答案。
- 结论：**在「更简替代」这一条，提案不占优。**

#### 10. 成本 / 优先级

实现成本低（一个指令关键词 + 一条 suppression-range 逻辑），但**设计成本不低**：scope 三读、与 toggle 的优先级、WarnAsError 交互、顶层语句场景的退路，每一条都是 spec 级工作。价值是「少数抑制者的轻微 DX 增益」。价值 × 成本 × 风险三个维度看，都不支持现在做。优先级应排在所有「新能力」建议之后——这是对既有能力的微调，不是新能力。

#### 11. 运行时 / CLR 硬约束

无。纯编译期 + IDE 诊断过滤，不触达 CLR、不涉 PEVerify、无表达式树问题。这也是我们敢把它放上议程的原因——任何否决都只能来自设计层面，不是可行性层面。

#### 12. 值不值得做

价值：小（工具可解的脚枪 + 轻微样板）。成本：中（spec 层面积远大于实现）。风险：中（重开已决、第二种做事方式、scope 漂移）。**不值得作为语言特性做。** 但值得作为**信号**记录：若 VBScript.NET scripting 落地后 `#Disable/#Enable` 确实痛苦，带着 scope 定义与实地数据回来。

### VB 基因对照

- **不引入「第二种做事方式」（原则 #3）**：直接冲突。这是本建议最大的基因问题。抑制警告已有 `#Disable Warning` / `#Enable Warning`，2014 在考虑过 block 与 next-line 后选择 toggle。重开此局，等同要求「第二种做事方式」的高门槛被一个未定义 scope 的建议越过。
- **消除常见样板（原则 #9）**：动机成立，但样板是两行不是二十行，且工具可消。这一条不足以压倒 #3。
- **读起来像英语、对新手友好（原则 #5）**：`#Ignore Warning BC42356` 确实读得通——「忽略这个警告」。加分项，但不足以翻盘。
- **避免隐蔽的语义变化（原则 #7）**：scope 未定义本身就是隐蔽语义风险；「下一行 vs 整个成员 vs 粘性」三读，同一行代码三种抑制面。这与 `Return?` 被拒的直觉同源——细微书写差异改变语义。
- **「修复而非抑制」的 VB 传统**：2014 原话 "The tradition in VB and its rich history of quick-fixes is that you resolve warnings by FIXING YOUR CODE, e.g. by doing whatever the quickfix says, adding an explicit cast, adding a return statement, ..."。加宽抑制通道，与这条传统背道。2014 已经为抑制开过一次口子；我们不应再开宽。
- **与主线关系（对照表 2.3）**：主线在 `#Disable Warning` 上已是「已决」状态；本建议属 Anthony 独立延伸，且**与主线决定冲突**（重开已决，非「方向一致更激进」）。sibling `proposal-semantic-preprocessing.md` 依赖本语法，需同步。

### RESOLUTION:

1. **不新增 `#Ignore Warning` 指令，三态定为 Table**。2014-04-16 / 2014-07-01 已就「block 式 vs toggle 式」和「next-line 抑制」作出决定；提案没有携带足以推翻的新证据。原则 #3（不引入第二种做事方式）对未定义 scope 的增量不破例。
2. **脚枪归工具链**：以**分析器**形态（.NET Analyzers 包，非编译器警告，避免 TreatWarningsAsErrors 破坏）提供「不平衡 `#Disable Warning`（缺配对 `#Enable`）」诊断；lightbulb「Suppress in Source」一键插入配对两行。这是对提案动机的正面回应。
3. **唯一有意义的续判信号**：VBScript.NET scripting / immersive-file 场景落地后，若 `#Disable/#Enable` 在单文件脚本里确实痛苦（整文件被吞、无成员边界），带「精确定义的 scope + 与 toggle 的优先级规则 + refactoring 安全论证 + 实地数据」回来重开。**在收到该信号前不讨论**。
4. **未决问题中可对齐的部分直接继承 2014 决定**：多错误号用逗号分隔支持；错误号按 VB 标识符规则解析、大小写不敏感；不支持纯数字（须 `BCxxxxx`）；非存在警告静默（BC2026 不再发出）；**不支持通配**（通配会破坏「灰显未使用 id」与显式性原则）。
5. **与 sibling 对表**：告知 `proposal-semantic-preprocessing.md` 作者，其示例中的 `#Ignore Warning BC40008` 改写为 `#Disable Warning BC40008` / `#Enable Warning BC40008` 两行式（本指令不进语言）。

`Not all of us are happy with` Table——有人主张直接 Reject（「重开已决 + 原则 #3 双杀，没有 Table 的理由」），也有人认为成员级自界定（A）在 scripting 语境值得一次受控实验。We did not find the evidence strong enough either way to justify anything beyond Table。

### Implication:

- 起草「不平衡 `#Disable Warning`」分析器建议（进 .NET Analyzers 包），评估误报率与 lightbulb 集成。
- 更新 `proposal-semantic-preprocessing.md`：`#Ignore Warning` 示例改为两行式。
- 在 OPEN QUESTIONS 记录 VBScript.NET scripting 对 `#Disable/#Enable` 的实地体验数据需求（唯一续判信号）。
- 本建议回写标注：重开 2014 已决事项，需在提案正文补「Precedent」一节。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：VBScript.NET scripting 落地后，`#Disable Warning` / `#Enable Warning` 在单文件脚本中的真实体验（范围吞并、无成员边界）——这是 Table → Active/Reject 的唯一触发器。
- `OPEN QUESTIONS`：若未来重开，A（成员级）与 B（下一语句）的取舍必须附 refactoring 安全论证；2014 对 B 已有否决，A 需证明「成员边界」在 immersive-file 下仍成立。
- `TODO`：量化「忘记 `#Enable`」脚枪的频率（GitHub 公开仓库搜索 `#Disable Warning` 无配对 `#Enable`），为工具侧诊断提供证据。
- `Follow-up`：与 `proposal-semantic-preprocessing.md` 的同步——该建议的示例改两行式后需重新走一遍自身评价。
- `Follow-up`：2014 For-the-future 清单里的「灰显未使用 id」「`/nowarn` / `/warnaserror` 支持自定义诊断」仍未完成，与本次讨论共享实现面，可在分析器工作中一并推进。

### 状态

- **LDM 状态：Table**——重开已决事项的证据不足；动机真实但归工具链可解；唯一续判信号是 scripting 语境数据。
- **三态判定：Table**——不否决到 Reject 是因为 scripting 语境确有未知；不放到 Active 是因为新指令的 scope 语义与设计原则双双不达标。

---

## 附录：特性评价

# 建议评价报告：proposal-ignore-warning-directive.md

## 评价对象

- 建议：proposal-ignore-warning-directive.md — `#Ignore Warning` 单行抑制指令
- 来源：Anthony 原文第 2.6 节「`#Ignore Warning` Directive」（`..\AnthonyDesign_wordpress.txt` L542–562；Before/After 示例逐字来自该节）
- 配方目标：一行式替代「`#Disable Warning` + `#Enable Warning`」两行成对写法，消除样板与「忘记 `#Enable`」脚枪，自界定覆盖范围

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3：「只覆盖部分场景；主效果显现但关键子效果缺失/消退」。Motivation 的目标改进（一行式）明确、示例可操作，但核心承诺「覆盖范围由指令自身界定，不易误扩大」因 scope 未定义而不成立；无原型/运行证实 | 已检查 | 关键子效果（自我界定）缺失；「忘记 #Enable」痛点真实但属于工具侧；主效果仅在 scope=A 读法下显现 |
| 特性 | 2/5 | 锚点 2：「外来特性直接照搬未 VB 化；或多个强无关能力捆绑」。语法措辞（`#Ignore Warning`）读起来像英语是唯一 VB 加分；但核心是「第二种做事方式」，直接违背原则 #3，且重开 2014 已决事项 | 已检查 | 与既有 `#Disable Warning`/`#Enable Warning` 功能重复；2014 已显式否决 block 式 `#Ignore/#End Ignore` 与 next-line 抑制，提案未引先例 |
| 品质 | 3/5 | 锚点 3：「缺某一章节或在关键处边界含糊；未决问题被轻描淡写」。六章节齐全、示例与 Anthony 原文一致、3 个未决问题具体（1–3 健康区间），但**最核心的设计点（scope）整体未定义**，且对「重开已决」这一前提完全沉默 | 已检查 | 无文法/BNF；无 Compatibility / breaking-change 章节；未引 2014 会议先例；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 2/5 | 锚点 2：「某关键维度受损且无应对（明显破坏兼容/一致性断裂）」。风=与主线已决方向断裂（重开已决）；暗=第二种机制的设计债与 scope 漂移风险，文档未识别；雷（实现快）是唯一亮色 | 已检查（预测待定） | 与 2014 决定的一致性断裂未提及；sibling 建议的耦合未识别；无对冲设计 |
| 炼金成分 | 3/5 | 锚点 3：「部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差」。材料=Anthony 2.6 节（正文未标注章节号）；实质是重开 2014 主线已决事项（未声明）；「next-line 抑制」的 2014 否决论证与 C# `#pragma` 语境均未援引 | 已检查 | 未标注「与主线 2014 决定冲突」；未声明该设计曾在主线 ALTERNATIVE SYNTAXES CONSIDERED 中被否；与 sibling `proposal-semantic-preprocessing.md` 的依赖关系未标注 |

## 设计原则对照

- **与 VB 基因：偏离**——原则 #3（不引入第二种做事方式）直接冲突；原则 #7（避免隐蔽语义变化）因 scope 三读而不达标；「修复而非抑制」的传统被加宽抑制通道削弱。仅语法可读性（原则 #5）与消除两行样板（原则 #9）勉强支持。
- **与主线关系：与主线冲突**——主线 2014-04-16 / 07-01 在考虑过 `#Ignore/#End Ignore` 块式与 next-line 抑制后，明确选择 `#Disable Warning`/`#Enable Warning` toggle；本建议是 Anthony 独立延伸但**重开已决、与主线决定方向相反**，不是「更激进的同方向」。与 `proposal-semantic-preprocessing.md`（依赖本语法）耦合。
- **破坏性变更：新指令本身无**（纯增量）；但若配套「不平衡 #Disable」做成编译器警告则有（TreatWarningsAsErrors 用户）。提案未做任何兼容性分析。

## 总评

- **达成程度：部分达成**——动机（脚枪）真实、语法读起来通顺，但核心语义（scope）未定义、重开已决事项、违反「一种做事方式」，价值主张被工具链替代方案（PROPOSAL C）整体覆盖。
- **LDM 三态建议：Table**——不 Reject 的理由：VBScript.NET scripting / immersive-file 语境下「无成员边界 + 单文件脚本」可能让 toggle 真的难用，这是未知数；不 Active 的理由：2014 已决 + 原则 #3 + scope 未定。
- **主要问题**：① scope 三读（下一行/整个成员/粘性）未定义，自我界定的核心承诺不成立；② 未引 2014-04-16/07-01 先例，而该先例几乎逐条命中本提案；③ 违反原则 #3 的证据强度不足（无数据、无 scripting 实地体验）；④ 与 `#Disable`/`#Enable`、WarnAsError、`##If` 的交互全未定义；⑤ sibling `proposal-semantic-preprocessing.md` 依赖本语法，需同步改写。

## 返工建议

- **补充章节**：Precedent（2014-04-16 / 07-01 全文引证 + 逐条回应）；Compatibility / breaking-change；文法（BNF：`#Ignore` 仅允许后接 `Warning`）。
- **补充证据**：VBScript.NET scripting 语境下 `#Disable/#Enable` 的实地体验数据（唯一续判信号）；「忘记 #Enable」脚枪的量化（公开仓库搜索）；若坚持放行，需最小原型验证 suppression-range 管道与「成员边界」在 immersive-file 下的表现。
- **未决问题处理**：scope 必须三选一并给出理由（建议选 A 成员级 + 显式退路）；多 ID/通配——继承 2014 决定（逗号分隔、标识符规则、大小写不敏感、禁纯数字、非存在静默、禁通配）；与 toggle 的优先级需给出表格式规则。
- **设计探索**：替代路径——「不平衡 #Disable Warning」分析器 + lightbulb 一键两行（PROPOSAL C）作为对动机的正面回应；若仍要新指令，论证为何工具侧诊断不够，并给出 scripting 语境专属的 scope 定义。

---

## 附录：C# 生态与互操作考量

> 本附录是追加的 C# 生态考量，不改写正文。评估对象与正文一致：`#Ignore Warning` 单行抑制指令（以及 RESOLUTION 的 Table 判定）。所有 C# 引用均逐字取自 csharplang 镜像并标路径；无法核实的标注 **Suspect** 或 **OPEN QUESTIONS**。索引（`..\..\csharplang-index.md`）对「警告抑制」无专门小节（T8 是 C# 15/16 方向，不覆盖本主题），故本附录直接到 csharplang 深挖 `#pragma warning` / NRT 抑制 / `[SuppressMessage]` 相关原文。

### 相关 C# 现实方向

本提案主题（警告抑制指令）在 C# 生态的对应物**不是低层互操作主线**，而是三件事：**`#pragma warning` 区域制 toggle 的长期成熟**、**2014 年把 pragma 诊断标识从数字扩展到标识符（analyzer 时代）**、**NRT 时代（C# 8）把「可空上下文」做成与抑制通道正交的独立 context bit**。三件事合成一句话：C# 20 年只有**一条**源内抑制机制，其余靠选项与工具补足。

1. **`#pragma warning disable/restore` 是唯一源内抑制机制，词法区域制 toggle。**
   - C# 从未提供「下一行」或「成员级」抑制：pragma 从指令生效到 `restore` / 文件尾，是词法区域，不是行制也不是成员制。语法顺序在 2018 年仍被专门勘正：「The proposal lists the syntax for configuring a diagnostic as `#pragma warning CS4321 restore` ... but it is actually `#pragma warning restore CS4321`」→ `meetings\2018\LDM-2018-10-29.md`——注意动词在**前**（`disable`/`restore`），与 VB `#Disable Warning`/`#Enable Warning` 的动词在前结构同构。
   - 生态把它当作标准出口：lock-object 提案写「the usual warning suppression means (`#pragma warning disable`)」→ `proposals\csharp-13.0\lock-object.md`；`[Experimental]` 的抑制同样走 pragma：「It is also possible to suppress the diagnostic by usual means, such as an explicit compiler option or `#pragma`.」示例 `#pragma warning disable DiagID` → `proposals\csharp-12.0\experimental-attribute.md`。
   - `Suspect`：确切引入版本（普遍说法 C# 2.0）本镜像无独立 proposal/spec 正文（spec 目录是链接索引，正文已迁 dotnet/csharpstandard），本附录不声称具体版本。

2. **诊断标识符化（2014 年，analyzer 时代的设计决策）。** C# LDT 在 2014-07-09 决定把 pragma 的诊断标识从数字扩展到标识符，理由是自定义诊断即将到来：「Now that custom diagnostics are on their way, we want to allow users to turn these on and off from source code, just as we do with the compiler's own diagnostics today. To allow this, we need to extend the model of how a diagnostic is identified: today a number is used, but that is not a scalable model when multiple diagnostic providers are involved.」示例同时给出 `#pragma warning disable AsyncCoreSet`（标识符）与 `#pragma warning disable CS1234`（数字）→ `meetings\2014\LDM-2014-07-09.md`。时间上恰与 VB 主线 2014-04-16 / 07-01 讨论 `#Disable Warning` 同期，两个团队在同一个夏天处理同一主题。

3. **NRT 的「独立 context bit」决策（2018–2019）。** C# 面对可空引用类型的「警告墙」时，没有开第二条抑制通道，而是把「可空上下文」做成独立开关（`#nullable enable/disable/restore`），与 `#pragma warning ...` 正交：
   - 2018-10-03 描述区域制模型：「The idea is to control nullable annotations and warnings as separate `#`-prefixed compiler directives that apply lexically to all source code until undone by another directive:」→ `meetings\2018\LDM-2018-10-03.md`。
   - 2019-05-13 的取舍：源内既有警告只能被抑制、不能被 enable——「We observed that, in source, existing warnings cannot be enabled, they can only be suppressed.」；随后把「可空警告上下文」定为独立位：「We're going to pursue the second design, based on "nullable warning context" bit.」并**拿掉** pragma 的 `nullable` 组：「We tentatively decided to remove support for the `nullable` group from the `#pragma warning ...` directive.」→ `meetings\2019\LDM-2019-05-13.md`。NRT spec 的过渡语法 `#pragma warning disable nullable` 最终未保留 → `proposals\csharp-9.0\nullable-reference-types-specification.md`。

4. **`[SuppressMessage]` 属 analyzer 生态，非 C# 语言特性。** 本镜像 grep 0 命中；它（`System.Diagnostics.CodeAnalysis.SuppressMessageAttribute`）源自 FxCop / .NET Analyzers 工具链，支持成员/程序集级抑制 + GlobalSuppressions，但做不到「文件内某段精确控制」——与正文 PROPOSAL D 的定位一致。具体规范在 dotnet/roslyn-analyzers，不在本库（见 OPEN QUESTIONS）。

### 现实 vs 提案

- **方向核心：兼容。** VB `#Disable Warning` / `#Enable Warning` 与 C# `#pragma warning disable` / `restore` 同构：配对 toggle、词法区域、区域可重叠。RESOLUTION 第 1 条「不新增指令、维持 toggle」正是 C# 唯一源内机制的形态——C# 从未为「下一行」或「成员级」开第二条路。
- **本提案的「自界定」主张在 C# 无对应、且被反证。** 正文引用的 2014 VB 判断「next-line 在 C# 里 lines don't really exist semantically」在 C# 侧成立（LDM-2018-10-03 明确 pragma「apply lexically to all source code until undone by another directive」）。PROPOSAL A（成员级）与 B（下一语句）都没有 C# 先例——它们恰是 C# 明确不做的东西。
- **「第二种做事方式」在 C# 侧也无先例。** 生态把 pragma 称作「the *usual* warning suppression means」，正说明没有第二机制；`[Experimental]`、lock 警告都只走 pragma。breaking-change-warnings 提案甚至设想用**工具**检查「explicit `#pragma warning disable` directives ... 'from the past'」而非加语法 → `proposals\breaking-change-warnings.md`——与正文 PROPOSAL C 的工具侧路线同构。
- **NRT 的「解耦」决策与本提案方向相反、但可参照。** 面对警告墙，C# 的解法是**上下文位正交化 + 保持单抑制通道**（LDM-2019-05-13 独立 flag，不保留 pragma `nullable` 组）；本提案「为抑制需求再开一条指令」是另一方向。这条 C# 现实直接支持 RESOLUTION 3 的「唯一续判信号」——抑制需求上升时，先看 context bit 能否吸收，再谈新指令。
- **`[SuppressMessage]` 定位一致**：成员/程序集级已有答案，但「文件内某段精确控制」空白——本提案的痛点（范围）在 C# 生态同样存在，只是 C# 从未用新指令去填，而是接受 toggle + 工具。
- **低层 interop：关系弱，无直接冲突。** 纯编译期 + IDE 诊断过滤，不触达 CLR/PEVerify/表达式树（正文第 11 条已讲清）；与索引 T2/T3/T4/T5/T6 无交集。真正接触点是 analyzer 生态互操作与 NRT 元数据（见下）。

### 对 VBScript.NET 的适应建议

- **保持单抑制通道 + 工具补足（RESOLUTION 2 的 C# 佐证）**：`.vbx` 的 `#Disable/#Enable` 与 C# pragma 同构即可；「不平衡 `#Disable`」以分析器形态补足，正对应 C# 生态「工具检查显式 pragma」（breaking-change-warnings.md）的既有思路。不要为「一行式」引入第二机制。
- **诊断标识符化对齐 C# 2014**：C# pragma 支持 `CS1234` 数字 + 标识符双模式（analyzer 诊断，见 `#pragma warning disable DiagID`）。`.vbx` 若要让 .NET Analyzer 包可逐条抑制，应同样支持「BCxxxxx + 标识符」双模式解析——RESOLUTION 4 的「按 VB 标识符规则解析、大小写不敏感」可自然承接 analyzer 诊断标识符，与 C# 的「usual means」链路一致。
- **NRT 若进 VBScript.NET：参照 C# 的 context bit 正交设计**。若采纳 `proposal-nullable-reference-types.md`，可空上下文应做成独立上下文位（`#nullable`-式），与 `#Disable Warning` 正交，而不是靠加宽抑制通道应付「警告墙」。LDM-2019-05-13 的独立 flag 结论是直接模板。
- **IDE 对齐：lightbulb 肌肉记忆跨语言一致**。C# lightbulb「Suppress in Source」插入 `#pragma warning disable <id>` + `#pragma warning restore <id>` 两行；`.vbx` 应插入 `#Disable Warning BCxxxxx` / `#Enable Warning BCxxxxx` 两行——保持「抑制即两行」的一致心智，降低 C#/VB 迁移者困惑。
- **WarnAsError 一致性**（正文第 4 条）：C# 侧 pragma 能压住 `/warnaserror` 升级的错误（编译器选项层语义）。`.vbx` 不引入新指令则无需新实现；工具侧分析器诊断不得触碰编译期警告集（RESOLUTION 2 已用「分析器形态」规避 TreatWarningsAsErrors 破坏），与 C# 的「pragma 管编译警告、analyzer 管自身诊断」分层一致。

### 对既有 RESOLUTION/三态判定的影响

- **不改变判定，仍为 Table。** C# 现实（唯一 pragma 机制 + 工具补足 + NRT context bit 正交）**强化** RESOLUTION 1「不新增指令」：C# 20 年没有为 member-scope/next-line 抑制开过口子，本提案的「自界定」主张无 C# 参照、反被 C# 词法区域制反证。
- **强化 RESOLUTION 2（工具侧）**：C# 生态已设想「工具检查显式 pragma」（breaking-change-warnings.md），与「不平衡 `#Disable` 分析器诊断」同构——工具侧路线有生态先例。
- **为 RESOLUTION 4 提供扩展方向**：C# 2014 的「数字 + 标识符」双模式 pragma 是 `.vbx` analyzer 互操作成熟先例；RESOLUTION 4 的标识符解析规则可承接 analyzer 诊断。
- **为 RESOLUTION 3（唯一续判信号）增加一条 C# 参照**：C# 在 NRT 时专门重新审视「抑制通道 × 上下文位」并选择正交（LDM-2019-05-13）。这提示：若 `.vbx` 先落地 NRT，抑制需求会先被 context bit 吸收；scripting 续判信号应同时记录 NRT 语境下的抑制需求，避免把「context bit 可吸收的需求」误当成「需要新指令的信号」。
- **两处对齐声明（建议，非阻断）**：RESOLUTION 4 的「不支持通配」与「非存在警告静默」与 C# pragma 行为同向（C# 按诊断 ID 逐条抑制、对未知 ID 的 pragma 不报错），可作为「与 C# 现实一致」的对齐声明补进提案正文。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「Now that custom diagnostics are on their way, we want to allow users to turn these on and off from source code, just as we do with the compiler's own diagnostics today. To allow this, we need to extend the model of how a diagnostic is identified: today a number is used, but that is not a scalable model when multiple diagnostic providers are involved.」→ `meetings\2014\LDM-2014-07-09.md`（Pragma warning directives）；附例 `#pragma warning disable AsyncCoreSet`、`#pragma warning disable CS1234`
- 「The proposal lists the syntax for configuring a diagnostic as `#pragma warning CS4321 restore` ... but it is actually `#pragma warning restore CS4321`」→ `meetings\2018\LDM-2018-10-29.md`
- 「The idea is to control nullable annotations and warnings as separate `#`-prefixed compiler directives that apply lexically to all source code until undone by another directive:」→ `meetings\2018\LDM-2018-10-03.md`
- 「We observed that, in source, existing warnings cannot be enabled, they can only be suppressed.」→ `meetings\2019\LDM-2019-05-13.md`
- 「Individual warnings continue to be able to be individually disabled at the project-level with `/NoWarn` and disabled or restored in source with `#pragma warning ...`.」→ `meetings\2019\LDM-2019-05-13.md`
- 「We're going to pursue the second design, based on "nullable warning context" bit.」→ `meetings\2019\LDM-2019-05-13.md`
- 「We tentatively decided to remove support for the `nullable` group from the `#pragma warning ...` directive.」→ `meetings\2019\LDM-2019-05-13.md`
- 「`#pragma warning` directives are expanded to allow changing the nullable warning context:」+ 示例 `#pragma warning disable nullable` → `proposals\csharp-9.0\nullable-reference-types-specification.md`
- 「the usual warning suppression means (`#pragma warning disable`)」→ `proposals\csharp-13.0\lock-object.md`
- 「It is also possible to suppress the diagnostic by usual means, such as an explicit compiler option or `#pragma`.」+ 示例 `#pragma warning disable DiagID` → `proposals\csharp-12.0\experimental-attribute.md`
- 「For instance it could look for explicit `#pragma warning disable` directives for breaking change warnings "from the past", or try to "check one last time" on explicit language version upgrade gestures in the tool.」→ `proposals\breaking-change-warnings.md`

### OPEN QUESTIONS / Suspect

- **Suspect**：`#pragma warning` 的**确切引入版本**（普遍说法 C# 2.0）。csharplang 镜像无独立 proposal/spec 正文（spec 目录为链接索引，正文在 dotnet/csharpstandard），本附录未声称具体版本，仅表述为「长期唯一机制」。
- **OPEN QUESTION**：`SuppressMessageAttribute`（`System.Diagnostics.CodeAnalysis`）的来源在 dotnet/roslyn-analyzers 生态，csharplang 镜像无正文（grep 0 命中）；其「成员/程序集级抑制 + GlobalSuppressions」语义来自分析器工具链而非 C# 语言规范。`.vbx` 若对齐，需以 dotnet/roslyn-analyzers 为准。
- **关系弱申明**：本提案与 C# 低层 interop 主线（T2 Span/ref、T3 指针/函数指针、T4 COM、T5 AOT/trimming、T6 source-gen）无直接冲突；与 T7（dynamic 边缘化）无交集。真正接触点是「analyzer 生态互操作」与「NRT 元数据」（若 `.vbx` 采纳 NRT）。
