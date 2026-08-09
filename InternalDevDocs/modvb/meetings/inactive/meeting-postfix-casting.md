# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论后置转换语法 `expr(As Type)`。我们把主线 2017 年 `As Type`/`CVal` 议题（#59）连同 2014 年"快速整型转换"的决议一并翻了出来——这场讨论比预想的更像一场"补课"：主线路在这个问题上走得比我们早，而它留下的犹豫恰好是本建议全部未决问题的根源。

## Agenda

* [Proposal: 后置转换语法 Post-fix Casting `(As Type)`](#proposal-后置转换语法-post-fix-casting-as-type)

## Proposal: 后置转换语法 Post-fix Casting `(As Type)`

_Related: [vblang #59 – New conversion operator/syntax (`As Type`/`CVal`)](https://github.com/dotnet/vblang/issues/59)；ModVB：`TypeOf ... Is` 流分析、`ShapeOf` 模式匹配、管道运算符 `->`、`(As Any)` 伪类型_

### 场景与缺口

We started from a scenario that is easy to feel and hard to argue with：转换之后还要继续链式访问成员时，前置转换让表达式被括号层层包裹，阅读顺序与书写顺序相反。

```vb
' 今天：读起来是"CType 开括号、control.Tag、逗号、DataRow、闭括号、点 RowId"，
' 类型的书写位置与思维上的"先转后访"顺序正好颠倒。
Let rowId As Integer = CType(control.Tag, DataRow).RowId

' 建议：从左到右，"Tag 转成 DataRow 再取 RowId"。
Let rowId = control.Tag(As DataRow).RowId

' 字典访问后立即转换，脚本风格代码里非常顺手。
Let receivedDate = jsonObject!receivedDate(As Date)
```

Motivation 是干净的：后置转换把类型转换当作"后缀操作"，与成员访问 `.`、字典 `!`、调用链无缝衔接。我们认可这个方向的直觉——**"先发生在前、后发生在后"**与管道运算符 `->`（Anthony 原文第 8 章）是同一个阅读哲学。但一旦进入设计，这场讨论立即撞上主线已经趟过的雷区。`Probably`：这里真正的敌人不是语法本身，而是**"第几种转换写法"**这道坎。

### 候选方案

**PROPOSAL A — 后置转换 `expr(As Type)`（原文原样）。** 按建议原文把 `(As Type)` 作为任意表达式的一等后缀操作。语义未定（建议自己的 Unresolved #3 问"是否等价于 DirectCast"）。示例即上文两行。

**PROPOSAL B — 语义显式化的后置转换家族。** `expr(As Type)` = CType 语义；`expr(As? Type)` = TryCast 语义（失败返回 `Nothing`）；再加关键字变体表达 DirectCast。直接回应主线 #59 留下的问题——"TryCast like behavior – return null? Odd to throw sometimes and return null sometimes."（2017-04-12 会议纪要原文）。把 throw / return-null 的分裂显式化。

**PROPOSAL C — 复用管道运算符 / 方法链，不引入新语法。** `expr -> CType(Of DataRow).RowId`（依赖管道运算符建议），或扩展方法 `expr.Convert(Of DataRow).RowId`。

**PROPOSAL D — 什么都不做，依赖既有工具。** 前置 `CType`/`DirectCast` + 中间变量 + TypeOf 流分析收窄 + 模式匹配 `Case x As T` 模式变量。以主线 2014-03-12 的先例为正当性依据："This is a compiler optimization, pure and simple. It adds no new syntax or concepts or library functions."——那场会议正是在**拒绝** `d As Integer` 一类新语法时说的这句话。

### 权衡：Q&A

- **A vs D：后置转换有没有独立于流分析的生存空间？** 有，但比想象的小。TypeOf 流分析（上次会议已决议）v1 只收窄显式局部变量与参数，**不收窄属性**——`control.Tag` 是属性，流分析救不了它，后置转换在属性链上确有独立价值。但流分析 + 模式变量 `Case x As T` 已覆盖"局部变量链式访问"这块最大的蛋糕。**结论：A 的剩余需求是"属性/字典访问后立即转换"，真实但窄。**
- **A vs B：语义不定的语法能不能教、能不能测？** 不能。`(As Type)` 若不定义与 CType/DirectCast/TryCast 的关系，实现者无从下手、用户无从预期、错误信息无从措辞。B 的显式家族又立刻踩中"不引入第二种做事方式"（设计原则 #3）——CType、DirectCast、TryCast 三种转换语义 + CStr/CInt 等别名已经够多了，再加一整个后置家族是灾难。**We see no attractive middle ground here。** `Probably`：若复活，默认锚定 CType 语义（2017-04-12 记录过 CType 是"the all-around conversion operator supporting the widest set of conversions"），`As?` TryCast 变体以 2014-02-17 的 null-checking conversion operator 先例（rejected "because it's too niche"）为由不进设计。
- **C 可行吗？** 管道运算符自身还在设计初稿（`proposal-pipeline-operator.md` 连参数传递规则都没定），把后置转换建立在它的肩膀上是不成熟套不成熟。扩展方法 `Convert(Of T)` 表达力够，但 `obj.Convert(Of DataRow).RowId` 比 `obj(As DataRow).RowId` 长且绕，没有解决 `!` 后接转换的书写问题。**C 被否定，留档。**
- **主线先例的份量。** 2014-03-12 的"快速整型转换"场景，主线**明确列出并拒绝**了 `DirectCast(d, Integer)`、`(Integer)d`、`VB.FastCast(d)`、`d As Integer` 四种新语法，理由是"compiler optimization, pure and simple"更干净。我们承认这是对"新增转换语法"的一记沉重先例——尽管那场会议针对的是**性能**（少一次 `Math.Round`），本建议针对**可读性**，动机不同，但"用优化/既有机制替代新语法"的取向一致。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`As` 是保留关键字，实参表达式不可能以 `As` 开头，所以 `f(As Integer)` 与 `f(x)` 在词法上可区分——这是建议的 Drawbacks 里已经点到的"`As` 关键字起消歧作用"，我们认可。但真正的歧义不在词法入口，而在**类型文法内部的括号嵌套**：

```vb
' (a) 数组类型：String() 自带一对 ()，与调用/索引括号纠缠。
Let first = names(As String())(0)

' (b) 泛型类型实参：List(Of String) 里的 Of。
Let count = obj(As List(Of String)).Count

' (c) 可空类型：Integer? 的 ? 与空安全调用 ?( 是同一记号、两种语义。
Let dt = row?(As DataRow).Date      ' 是"空安全调用后转换"还是语法错误？

' (d) 方法组误写：obj.Method(As String) 会被解析为"对方法组做转换"。
'     今天的同一写法是非法实参表（As 不是实参），所以不破坏既有代码，
'     但会给想写 obj.Method("x") 的人一个误导性错误。
```

`(a)` 需要括号配对逻辑区分"类型括号"与"索引括号"；`(b)` 可解；`(c)` 是真实的记号冲突——`x?(y)` 作为"空检查后索引/调用"操作符在主线 2014-02-17 有明确讨论记录，`As? Type` 变体又让 `?` 出现在 `As` 之后，与 `Integer?` 的 `?` 语义完全不同。`(d)` 是学习性负担而非歧义，但足以让补全与诊断设计师头疼。

#### 2. 角案例与边界语义

- **双重转换**：`x(As A)(As B)` 无歧义但应禁止还是允许？`x(As A)(0)` 是"转 A 后索引 0"——一旦允许 `(As ...)` 与 `()` 并存于后缀链，括号配对规则必须写进 spec。
- **Await 优先级**：`Await x(As T)` 应解析为 `Await (x(As T))` 还是 `(Await x)(As T)`？VB 的 `Await` 优先级低，但这条必须显式定案，否则异步场景出隐蔽行为。
- **转换目标为类型参数**：`x(As T)` 中 `T` 是类型参数时，CType 语义会退化为 DirectCast——2017-04-12 明确记录过 "Additionally CType will always behave like DirectCast for type parameter conversions."。后置形式必须继承这条退化规则，否则窄类型参数路径与宽类型路径行为分裂。
- **数组转换**：`x(As String())` 若要支持，直接踩中 1(a)。

#### 3. 作用域与绑定

`(As Type)` 在语义模型里应表达为一个**转换节点**——复用现有 `CType` 表达式的绑定与类型推断，而不是新符号。`GetTypeInfo` 返回目标类型 T；`.` 后补全显示 T 的成员。我们建议**不新建 AST 节点种类**，把 `(As Type)` 解析并降级为既有 `CastExpression`（`CType` 的语法特例），复用全部转换规则、late binder 钩子与诊断。这是成本控制的关键决定。

#### 4. 与既有特性的交互

- **ByRef**：`x(As T)` 是 rvalue，不能作 `ByRef` 实参——天然安全，无 copy-in/copy-out 问题。`Probably` 唯一要防的是把 `(As T)` 误用于 `ByRef` 目标时给出清晰诊断。
- **Late binding / Option Strict Off**：这是最尖锐的交互。宽松模式下 `obj.Member` 本来就晚期绑定；一旦 `obj(As T).Member` 出现，`(As T)` 若下略为运行时转换，结果静态类型为 T，`.Member` 会**转为早绑定**——改变运行期行为与异常时机（编译期报错 vs 运行期 `MissingMemberException`）。规则必须明确：后置转换在宽松模式下**是否强制早期绑定**。参考 2014-02-17 的记录 "TryCast bypasses the latebinder"，各转换语义对 late binder 的行为本就不同，后置形式继承哪一条必须写死。
- **Lambda / 闭包**：`(As T)` 是纯表达式转换，无捕获问题；但 `Function(x) x(As Integer)` 中 `x` 的类型由参数推断，转换作用于参数表达式——与 lambda 参数类型推断交互需验证。
- **LINQ 查询**：`From x In list Where CType(x, String).Length > 0` 里前置转换可自由出现；后置 `x(As String).Length` 在查询理解中的范围变量上同样应成立——但这依赖"范围变量可作转换源"的既有规则，无新语义。
- **表达式树**：`(As T)` 若下略为 CType，表达式树经 `Expression.Convert` 可表示；`As?` TryCast 变体对应 `Expression.TypeAs`。`Probably` 可表示，但须在原型中验证，不做进规范等于没设计。

#### 5. Breaking change 与兼容性

**无直接破坏。** `(As ...)` 在任何现有合法代码中都不可能出现——`As` 不是合法实参表达式，现有 `f(As Integer)` 是编译错误。新语法是**纯增量**，不会重解释既有代码。这是本建议在兼容性上唯一干净的地方，我们如实记录。剩下的风险全是**心理/生态层**：新增一种写法会沉淀在文档、教程与代码库里，几十年收不回来——这正是设计原则 #3 惩罚的对象。

#### 6. Option Strict / 编译选项分叉

严格路径：`x(As T)` 走显式转换规则（CType 语义 = 允许运行期转换、user-defined conversion、unboxing 等，见 2017-04-12 的 CType 支持清单）。宽松路径：如上第 4 条，必须定义 `(As T)` 是否介入晚绑定。两条路径行为不一致的代价是"同一个特性两套心智模型"——我们要求**规则统一**：`(As T)` 的转换语义与 `CType(x, T)` 完全一致，不因 Option 开关而分叉。

#### 7. IDE / IntelliSense

- 键入 `x(` 时，补全必须识别"这是转换括号还是调用/索引括号"，不能把 `As` 当作参数提示。
- `(As ` 之后应提供类型名补全（与声明 `As` 后的类型补全共用一套）。
- 错误文案：`obj.Method(As String)`（方法组转换）需要一个不误导的专用诊断。
- InfoTip：`x(As DataRow).RowId` 处的表达式类型应显示 `DataRow`，而不是中间 `Object`。

#### 8. 数据 / 普遍性

建议没有提供任何量化数据。`control.Tag` 是 WinForms 时代的经典样板——真实但老旧；`jsonObject!receivedDate(As Date)` 是脚本风格代码（VBScript 迁移）的真实痛点。两者都真实，但都**不是**"数十万安静客户"的高频热区。`Suspect`：链式转换的可读性收益是真实的，但频率证据缺失，优先级证据不足。

#### 9. 更简替代

- **中间变量**：`Let tag As DataRow = control.Tag` 后 `tag.RowId`——显式、可调试，代价是多一行。建议的 Alternatives 已承认。
- **TypeOf 流分析**：上次会议决议只收窄局部变量/参数，属性链救不了，但已切走最大一块。
- **模式匹配 `Case x As T`**：2018-12-19 主线模式文法里已有 `'As' TypeName` 的 "Type check pattern -- matches when subject is of TypeName"，`Case x As T` 模式变量能同时完成"测试 + 绑定 + 收窄"，覆盖大量"转换后访问"场景。
- **编译器优化**：2014-03-12 的取向——用优化替代语法。
- Analyzer / 重构：可以建议"此处可后置转换"，但不能改变语言表面积。作脚手架可，作替代不可。

#### 10. 复杂度 / 成本 / 优先级

解析器改动中等（后缀转换分支 + 括号配对），绑定与 late binder 改动中等，IDE 工作量偏高（补全、签名帮助、诊断、InfoTip）。无原型、无文法、无 spec。价值是"窄而真实的可读性增益"。综合优先级：**低**。排在流分析、模式匹配、管道运算符之后。

#### 11. 运行时 / CLR 硬约束

无。`(As T)` 下略为 CType/DirectCast 语义时生成既有 IL（`castclass`、`unbox.any`、运行期转换调用），PEVerify 无碍，不触达 CLR 存储规则。性能与手写 `CType` 等价。

#### 12. 值不值得做

- **价值**：真实但窄——消除"前置转换 + 括号 + 链条"样板，从左到右读。不是 85% 高频痛点。
- **成本**：中——语法、绑定、IDE 三处都动。
- **风险**：语义分裂（#59 明确记录的反感）、与主线 2014 先例的取向张力、与流分析/模式匹配的重复。
- **综合**：现在做，不划算。**值得记住，不值得现在做。**

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：本建议唯一满格项。`control.Tag(As DataRow).RowId` 从左到右、零括号嵌套，新手不需要配对括号就能读。我们反复回访这一点，它真实。
- **不引入"第二种做事方式"（原则 #3）**：硬违背，重罚项。VB 已有 `CType`/`DirectCast`/`TryCast` + `CStr`/`CInt` 等别名，后置形式是第 N 种写法。2017-04-12 讨论 `As Type` 时全场对语义分裂的犹豫（"Odd to throw sometimes and return null sometimes"）正是这一条的活证据。
- **保持 VB-like（原则 #2）**：偏离。VB 的转换传统是前缀函数式（`CType(x, T)`），后置 `(As Type)` 在 VB/VB6 历史上无对应物。2014-03-12 拒绝 `d As Integer` 时已经亮过红灯。
- **避免隐蔽语义变化（原则 #7）**：风险项。语义未定 = 把隐蔽变化留给实现者决定；`Await` 优先级、Option Strict 分叉都是隐藏陷阱。
- **消除样板（原则 #9）**：部分命中。消除的是"括号包裹 + 前置"样板，代价是"写法选择"负担。净收益存疑。
- **与主线关系（对照表 2.3）**：`postfix-casting` 不在主线对照表内，属 **Anthony 独立延伸**。它与主线 #59（`As Type`/`CVal`）议题**重叠但主线无决议**（见 OPEN QUESTIONS）；与主线 2014-03-12"编译器优化优先"取向**张力**；与 `TypeOf` 流分析、模式匹配在"免强转链式访问"上**重复**。与 `proposal-any-pseudotype.md` 的 `(As Any)` 必须共用解析规则，否则会造出两套后缀转换语法。

### RESOLUTION:

1. **认可动机，不采纳形态。** 链式转换的可读性问题是真实的，但 `expr(As Type)` 作为独立语法现在不进入设计。
2. **语义不可分裂。** 任何后置转换若落地，必须等价于既有**单一**转换语义，默认锚定 CType（"the all-around conversion operator"，2017-04-12）。`(As? Type)` TryCast 变体不进入设计——以 2014-02-17 null-checking conversion operator 先例（rejected as "too niche"）与 #59 对 throw/return-null 分裂的反感为依据。
3. **状态 = Table。** 绑定到两个信号，任一满足即复活评估：
   - (a) 主线 #59（`As Type`/`CVal`）出现可核实的决议，且其语义与本建议兼容；
   - (b) TypeOf 流分析 + 模式匹配（`Case x As T`）落地后，属性/字典链的残余需求有实测数据支撑。
4. **若复活，前置条件**：提供可编译示例（**去掉即时窗口 `?`**）、补文法（BNF）与括号配对规则、定义 Option Strict 分叉与 Await 优先级、补兼容性分析（确认纯增量）、提供原型验证语义模型与 IDE 补全。
5. **不新建 AST 节点。** `(As Type)` 若实现，解析并降级为既有 `CastExpression`（`CType` 语法特例），复用全部转换规则与诊断。

### Implication:

- 将本建议标注为 **LDM Considering / Table**，并在建议文档头部注明关联 `#59` 与两条复活信号。
- 给 `proposal-any-pseudotype.md` 团队发送对齐请求：`(As Any)` 不得独立定文法，需与本建议共用后缀转换解析规则（当前两者互引而未定义）。
- 与 TypeOf 流分析 / ShapeOf 模式匹配团队对表，确认 `Case x As T` 与 `x(As T)` 的边界划分，避免重复实现。
- 起草一份"转换写法全景"备忘：CType/DirectCast/TryCast/CStr 族 + 后置 + `As?`——作为将来任何"新增转换语法"提案的前置阅读材料。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**主线 #59（`As Type`/`CVal`）的最终状态无法核实**。2017-04-12 会议纪要记录了 CType/DirectCast 语义总结与 Proposal 1–4（`As Type`、magic method fix、扩展 DirectCast、`CVal`），但纪要正文**没有记录 RESOLUTION**，直接转入 "GitHub repo review"。我们无法在仓库内找到 #59 的决议记录——请勿将本会议的任何结论当作主线对 #59 的裁定。`Suspect`：或许决议发生在纪要未整理的会话或邮件列表中，需外部核实。
- `OPEN QUESTIONS`：`(As T)` 在 Option Strict Off 下是否强制早期绑定（与 late binder 的关系）。
- `OPEN QUESTIONS`：`(As Type)` 与空安全调用 `?(` 的记号冲突（`row?(As DataRow)`）的解析规则。
- `TODO`：量化"属性/字典访问后立即转换"在真实代码（含 VBScript 迁移代码）中的占比，为复活评估补数据。
- `TODO`：撰写"转换写法全景"备忘，含 `(As Any)` 的后缀转换共用文法草案。
- `Follow-up`：跟踪主线 #59 状态；若出现决议，重审本建议。

### 状态

- **LDM 状态：LDM Considering（Table）**；复活条件已明确（见 RESOLUTION #3）。
- **三态判定：Table** — 动机真实但形态不成熟：语义分裂、与主线 #59 重叠而无决议、与 2014 "编译器优化优先"先例张力、与流分析/模式匹配重复；且无文法、无原型、无数据。值得记住，不值得现在做。

---

## 附录：特性评价

# 建议评价报告：proposal-postfix-casting.md

## 评价对象

- 建议：proposal-postfix-casting.md — 后置转换语法 `expr(As Type)`
- 来源：Anthony 原文第 3.12 章 "Casting"（`..\AnthonyDesign_wordpress.txt` L1099–1107，两示例逐字来自原文）
- 配方目标：把类型转换写成后缀操作，使链式转换与成员访问从左到右可读，免去前置 `CType` 的括号嵌套

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 目标（链式可读性）清晰，第一示例（`control.Tag(As DataRow).RowId`）可操作；但第二示例 `? jsonObject!receivedDate(As Date)` 中的 `?` 是即时窗口提示符，**不是合法 VB 语句**；无原型；语义未定使效果无从验证 | 已检查（书面，无原型） | 核心语法未定型 → 效果证据封顶；示例不可编译 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。后置 `(As Type)` 在 VB/VB6 无传统（2014-03-12 已拒 `d As Integer`）；用 `As` 关键字做了一点 VB 化，但整体是全新形态；未定语义与 CType/DirectCast/TryCast 三种并存 = 原则 #3 的"第 N 种做事方式" | 已检查 | 语义分裂（#59 明确记录的反感）；"VB 味儿"存疑 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、结构与模板一致、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design 仅两个示例，无文法、无语义、无角案例；语义未定是"关键处边界含糊" | 已检查 | 无 BNF/spec 改动描述；无兼容性章节；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；未提与主线 #59 的关系 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。风=演化一致性断裂（第 N 种转换写法、与 #59 重叠而无决议、与 2014 先例张力）；与流分析/模式匹配重复未权衡；暗风险（late binding 分叉、记号冲突 `?(`）被 Drawbacks 轻描淡写 | 已检查（预测待定） | 无应对设计；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 3.12（未标章节号）；**最大偏差**：未说明 `As Type` 与主线 #59（同名议题，2017 已讨论）的关系，把提案写成全新想法；未声明 Swift/F# 后置 `as?` 类比的借鉴；无闭源杂质 | 已检查 | 来源标注不全；与主线讨论的关联缺失 |

## 设计原则对照

- **与 VB 基因：偏离为主**——原则 #5（读起来像英语）满格，但原则 #2（保持 VB-like）、#3（不引入第二种做事方式）、#7（避免隐蔽语义变化）均受冲击；#9（消除样板）部分命中但引入"写法选择"负担。
- **与主线关系：Anthony 独立延伸，与主线张力**——与主线 #59（`As Type`/`CVal`）议题重叠但主线无可核实决议；与 2014-03-12"编译器优化优先、拒绝 `d As Integer`"先例取向相反；与 TypeOf 流分析 / 模式匹配在"免强转链式访问"上重复。
- **破坏性变更：无（语法纯增量）**——`(As ...)` 在任何现有合法代码中都不可能出现，不重解释既有代码；风险全在生态层（写法沉淀）与语义分裂。

## 总评

- **达成程度：部分达成**——可读性论点成立，但语义未定、语法未设计、无原型、无数据，且与主线三个既有决策/议题纠缠。
- **LDM 三态建议：Table**（有条件复活：主线 #59 决议出现，或流分析/模式匹配落地后残余需求有数据支撑）。
- **主要问题**：① 转换语义未定（CType/DirectCast/TryCast 分裂，直指 #59 未解之题）；② 未与主线 #59 对齐就独立推同名语法；③ 2014-03-12 有拒绝同类新语法先例；④ 无文法、无原型、示例不可编译（`?` 即时窗口）；⑤ 与流分析/模式匹配/`(As Any)` 的重复与文法共用未定。

## 返工建议

- **补充章节**：文法（BNF，含数组/泛型/可空类型的括号配对规则）、转换语义定案（默认 CType，含类型参数退化为 DirectCast 的继承）、Option Strict 分叉与 Await 优先级、兼容性分析（确认纯增量）、IDE 影响、与 #59 / 流分析 / 模式匹配 / `(As Any)` 的关系章节。
- **补充证据**：可编译的链式示例（替换 `?` 即时窗口示例）、最小原型（解析 + 绑定 + IDE 补全）、"属性/字典访问后立即转换"在真实代码的占比数据。
- **未决问题处理**：`(As? Type)` 变体按 2014-02-17 null-checking conversion operator 先例（too niche）搁置；`As` 的消歧能力确认；`row?(As DataRow)` 记号冲突定解析规则。
- **设计探索**：把 `(As Type)` 作为 `CastExpression`（CType 语法特例）下略的 AST 方案；与 `(As Any)` 共用的后缀转换文法草案；复活评估的两条信号量化。

---

## 附录：C# 生态与互操作考量

> 立场声明：C# 是 CLR 新特性与 .NET 生态的主要推动者（索引 T1）；VB LDM 主线已退化为「只做与 C# 兼容」，Anthony 主张 VB 保持特色（决策文件通用背景）。本提案（后置转换 `expr(As Type)`）与 C# 的**模式匹配 / 类型测试**（`is T x`）、**C# 15 unions / closed hierarchies**（索引 T8/M4）、以及 **Any/dynamic 晚期绑定桥**（决策文件 M2）直接相关。本附录只追加「C# 现实方向 vs 本提案响应」的对应与对 VBScript.NET 的适应建议，**不改写正文 RESOLUTION**。引用均来自 `..\..\csharplang` 镜像，标注到文件；无法核实的列入末尾 `OPEN QUESTIONS`。

### 相关 C# 现实方向

1. **C# 的类型测试走模式匹配，不新增 cast 语法。** C# 的强制转换只有前缀式 `(T)x` 与 `x as T` 两种；C# 7 起「运行时类型测试 + 绑定变量」交给模式匹配：
   - 原文：「The type pattern is useful for performing run-time type tests of reference types, and replaces the idiom」`var v = expr as Type; if (v != null) {...}`「with the slightly more concise」`if (expr is Type v) {...}` → `proposals\csharp-8.0\patterns.md`（Type Pattern 节；同段落在 `proposals\csharp-7.0\pattern-matching.md` 的 Declaration Pattern 节，措辞为 "The declaration pattern is useful for..."）。
   - C# 8 起的 switch 表达式把「测试 + 收窄 + 取值」做成**单表达式**（`x switch { DataRow r => r.RowId, _ => ... }`），恰好覆盖本提案主打的那种「窄而真实」的链式场景（见下节对应表）。
2. **C# 15 主线 = 类型系统承载更多职责，服务穷尽模式匹配与 AOT。** unions 与 closed hierarchies 均为 C# 15 开发中方向（索引 T8）：
   - 原文：「Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive.」→ `proposals\unions.md`（Motivation）。
   - 原文：「Many class types are not intended to be extended by anyone but their authors, but the language provides no way to express that intent, let alone guard against it happening.」→ `proposals\closed-hierarchies.md`（Motivation）。
   - 跨语言元数据信号，原文：「Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes.」→ `proposals\closed-hierarchies.md`（Cross-language concerns）。印证决策文件 M4/M8：VB 编译器必须识别这类新特性标志，才能正确消费 C# 15 类型。
3. **dynamic / 晚期绑定边缘化。** C# 4 的 `dynamic` 长期无大演进，且被 AOT/trimming 视为负担（索引 T7）；unsafe-evolution 甚至把 dynamic 列为「应标 unsafe 吗」的候选：
   - 原文（open question，未决议）：「`dynamic` (probably should match what BCL decides for reflection APIs)」→ `proposals\unsafe-evolution.md`（「Should more constructs be `unsafe`?」节）。
4. **VB 在 unsafe 模型上的官方定位（无 unsafe 上下文）。** unsafe-evolution 对 VB 有明确表述（已核实逐字，与索引第四节一致）：
   - 原文：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」节）。
   - 含义：VB 侧没有 C# 的 unsafe / requires-unsafe 上下文分层；C# 指针/解引用成员在新模型下更多出现在「非 unsafe 上下文」，VB 编译器仍需识别其新元数据（RequiresUnsafe / 内存安全属性）才能正确校验调用安全（决策文件 M8）。

### 现实 vs 提案

| 本提案要素 | C# 现实对应 | 判定 |
|---|---|---|
| 后置转换 `expr(As T)`（锚定 CType 语义） | C# 无后缀 cast；对应物是前缀 `(T)x` + 模式匹配 `x is T v` + switch 表达式 | **语法层面脱节，IL/CLR 层面兼容**：C# 用模式匹配解决「类型测试 + 绑定 + 收窄」，不新增 cast 写法；但本提案落到既有 IL（`castclass`/`unbox.any`/运行期转换，正文第 11 条），**无需新 CLR/元数据支持**，纯增量 |
| `expr(As? T)` TryCast 变体（正文已拒） | C# `x as T`（转换失败返回 null，仅引用/装箱转换） | **语义兼容但无差异化**：C# 早已有 `as`；正文以 too niche 拒绝后缀变体，C# 现实不改变该结论 |
| 提案核心场景「属性/字典访问后立即转换并访问成员」（`control.Tag(As DataRow).RowId`） | C# 对应：`control.Tag is DataRow r ? r.RowId : ...` 或 switch 表达式 `control.Tag switch { DataRow r => r.RowId, _ => ... }` | **兼容且 C# 已有表达式级替代**：C# 在「单表达式」层面已能表达同样痛点——强化正文 RESOLUTION #3(b)：残余需求需 VB 侧实测数据才能证明独立价值 |
| `(As T)` 作为 Any/动态值的「类型化出口」（晚绑定→强类型） | C#：`dynamic` 值经 `(T)d` / `as` / 模式匹配转为强类型后，后续成员访问即**早绑定** | **需桥接（VB 特色，最大接合点）**：决策文件 M2 建议的「默认安全、按需动态」路线的关键一环；正文 OPEN QUESTION「宽松模式下是否强制早绑定」可锚定 C# 行为——转强类型 ⇒ 后续访问早绑定 |
| 消费 C# 15 unions / closed hierarchies（VB 侧经类型谓词/流分析/`Case x As T`） | C# 靠 `[CompilerFeatureRequired("ClosedClasses")]` 等元数据 + 模式匹配穷尽性 | **需桥接**：VB 编译器要识别 closed/sealed/union 新元数据，才能理解 C# 类型的穷尽性与可继承性（决策文件 M4/M8） |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态（决策文件 M2 主线）。** `(As T)` 的长期价值不在「链式可读性」（窄场景），而在作为从 Any/晚绑定世界返回强类型的**单表达式类型化出口**。若 VBScript.NET 坚持「脚本层允许动态、编译产物走类型化」双模路线，应把后置转换优先当作该出口的候选语法评估，而非「另一种 CType 写法」。
2. **语义对齐 C#，避免双套心智模型。** 若落地，`(As T)` 走 CType 语义（= `Expression.Convert`，正文第 4 条表达式树一节）；其「转强类型后早绑定」行为与 C# `(T)dynamicValue` 一致。`As?` 若（不推荐地）复活应对齐 `Expression.TypeAs`。如此跨语言行为可预测，且不产生新 IL（正文第 11 条）。
3. **source-gen 桥与 AOT。** 动态/晚绑定与 NativeAOT/trimming 张力最大（决策文件 M5/M8）。把 `(As T)` 视为「此处结束动态、进入可裁剪路径」的显式边界，可成为 AOT 编译器静态化该点的钩子——优于隐式晚绑定。
4. **识别新元数据。** 为消费 C# 15 unions/closed hierarchies，VBScript.NET 的 VB 编译器需识别 `CompilerFeatureRequired`（含 `"ClosedClasses"`）、closed/sealed 语义、以及 unsafe-evolution 引入的内存安全属性（决策文件 M8）。与本提案无直接语法冲突，但决定「VB 能否安全调用新 C# 类型」。
5. **与 `(As Any)` 共用文法（决策文件 M2 / 正文 RESOLUTION #5 延伸）。** 后缀转换解析规则必须与 `(As Any)` 共用；C# 视角无对应物、不受影响。类型名补全复用声明 `As` 的类型补全（正文第 7 条），与 C# `is T` 补全无冲突。

### 对既有 RESOLUTION / 三态判定的影响

- **C# 现实强化「Table」判定，不推翻。** C# 对同一痛点的答案是模式匹配 + switch 表达式（C# 7/8 已落地，C# 15 的 unions/closed hierarchies 继续加码），而非新 cast 语法。正文 RESOLUTION #1（不采纳形态）与 C# 方向一致；RESOLUTION #3(b) 在 C# 视角下依然成立，且 C# 的 switch 表达式就是「单表达式收窄」的现成参照系。
- **潜在第 (c) 条复活信号（本附录不修改 RESOLUTION，仅提示）。** 若后续把后置转换重新定位为「Any/动态值的类型化出口」（而非通用转换写法），该定位在 .vbx 脚本迁移场景下有独立于 C# 的生存价值，且与 C# `dynamic→(T)` 语义可对齐。建议在「转换写法全景」备忘（正文 Implication）中单列此出口价值，作为将来评估的补充信号——是否升格为正式复活条件，留给主线决定。
- **生态侧注脚。** Table 状态与 C# 生态无冲突：本提案不要求任何新 CLR/元数据支持，纯增量（正文第 5、11 条），「现在不做」不构成互操作债务；「若做需与 C# 语义对齐」应在复活时优先满足。

### 引用与核实状态

本附录引用的逐字原文均已核实（源文件位于 `..\..\csharplang`）：

- 「The type pattern is useful for performing run-time type tests of reference types, and replaces the idiom」→ `proposals\csharp-8.0\patterns.md`（Type Pattern 节；C# 7 同段见 `proposals\csharp-7.0\pattern-matching.md` Declaration Pattern 节）
- 「Unions are a long-requested C# feature, which allows expressing values from a closed set of types in a way that pattern matching can trust to be exhaustive.」→ `proposals\unions.md`（Motivation）
- 「Many class types are not intended to be extended by anyone but their authors, but the language provides no way to express that intent, let alone guard against it happening.」→ `proposals\closed-hierarchies.md`（Motivation）
- 「Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes.」→ `proposals\closed-hierarchies.md`（Cross-language concerns）
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」节）
- 「`dynamic` (probably should match what BCL decides for reflection APIs)」→ `proposals\unsafe-evolution.md`（「Should more constructs be `unsafe`?」节，open question）

`OPEN QUESTIONS`：

- C# unions 的最终落地形态与精确元数据未定稿（`proposals\unions.md` 自身标注 "the proposed union declaration syntax isn't universally loved"、语法可能修改）；VB 消费 union 类型所需识别的最小元数据集合未在本镜像核实。
- C# `switch` 表达式能否逐案覆盖本提案全部「属性链上立即收窄」场景，未做逐案对比（本附录仅指出其为现成参照系）。
- COM source generator 与语言层协作细节（属 dotnet/runtime，本镜像无正文，索引已列为 OPEN QUESTIONS）。
