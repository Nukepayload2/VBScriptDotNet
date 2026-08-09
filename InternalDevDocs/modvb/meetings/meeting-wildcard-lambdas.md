# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次是通配符 Lambda 专场。上周我们刚结束"可空性流分析"与"递归 Lambda 推断"两项与 Lambda 强相关的讨论，所以这次开会时，关于"参数如何被绑定"的许多前提已经在桌面上——但这也意味着我们开场就要面对一个沉甸甸的对照物：**上一条 lambda 建议（递归推断）是放宽推断，这一条是省略参数名**。两者动的是 Lambda 表达式的同一张脸。

## Agenda

* [Proposal: 通配符 Lambda 表达式（Wildcard Lambda Expressions）](#proposal-通配符-lambda-表达式)

## Proposal: 通配符 Lambda 表达式（Wildcard Lambda Expressions）

_Related: 主线无对应提案（对照表 2.3："通配 Lambda `*.X` — 主线无 / Anthony 有"）；C# 无对应特性；ModVB：`XAML 字面量`（`proposal-xaml-literals.md`）、`嵌入 VB 解析模式`（`proposal-embedded-vb-mode.md`）、`局部声明 / Let`（`proposal-local-declarations.md`）、`递归 Lambda 推断`（`proposal-recursive-lambda-inference.md`）_

### 场景与缺口

We started from the most mundane observation in the room：EF Core 配置链里最频繁出现的、几乎不算"逻辑"的样板——"取出参数的那个成员"：

```vb
' Vanilla VB
modelBuilder.Entity(Of Blog) _
            .Property(Function(b) b.Url) _
            .IsRequired()

modelBuilder.Entity(Of Blog) _
            .HasMany(Function(e) e.Posts) _
            .WithOne(Function(e) e.Blog) _
            .HasForeignKey(Function(e) e.ContainingBlogId) _
            .IsRequired()
```

每段 lambda 里，参数名 `b` / `e` 不携带任何信息——它只出现一次，紧接着就被取成员。The signal is `.Url`；everything else is ceremony. 数据绑定（`Include(*.Profile.Avatar)`）同理。Anthony 原文（2.3）给出的通配形态：

```vb
' ModVB
modelBuilder.Entity(Of Blog) _
            .Property(*.Url) _
            .IsRequired()

modelBuilder.Entity(Of Blog) _
            .HasMany(*.Posts) _
            .WithOne(*.Blog) _
            .HasForeignKey(*.ContainingBlogId) _
            .IsRequired()

Let users = context.Users.Include(*.Profile.Avatar).ToList()
```

`*` 表示"那个唯一的 lambda 参数"，`*.Member` 编译为 `Function(param) param.Member`，连续成员访问构成投影链。

We agree the boilerplate is real。但我们立即注意到两件事：**第一**，这不是 parity gap——C# 写 `b => b.Url` 一样冗长，主线 VB 从没把"lambda 更短"列为诉求，我们修的是自己加给自己的痛点，不是与 C# 对齐；**第二**，`*` 是乘法运算符，把"表达式最前面那颗星"同时当作 lambda 参数，是一步需要认真权衡的棋。整场讨论都在这两点上拉锯。

### 候选方案

**PROPOSAL A — 通配符 `*`（按原文，Anthony 2.3）。** `*.Member` 链，单参数，纯成员访问。`*` 不是合法标识符，因此不会遮蔽任何既有变量；语法上只出现在"今天必为错误"的位置（见追问 1）。

**PROPOSAL B — 命名占位符 `it.Url` / `$0.Url`（Kotlin / Groovy 的 `it` 惯例）。** `it` 读起来像英语（"its URL"），对新手直觉最好。代价：`it` 若成为语境关键字，会遮蔽现有名为 `it` 的变量——这是**破坏性变更**；`$0` 则读感与"取参数"无关。

**PROPOSAL C — API 特化：只在 `Include` / `Property` 等已知方法上做糖。** 编译器为特定 API 开特例。范围极小，但建立"魔法清单"——我们不想要。

**PROPOSAL D — 什么都不做；用 analyzer + 重构（`Function(x) x.Member` ⇄ `*.Member`）与代码片段替代。** 零语言表面积，DX 收益打折但确定。

### 权衡：Q&A

- **A vs B：兼容性 vs 可读性。** B 的 `it` 在英语读感上完胜；但"`it` 从普通标识符变成隐式参数"意味着现存的 `Dim it As Blog` 代码一旦进入 lambda 就改绑——我们在 2017 年 11 月对 Implicit Property Backing Fields 的判决是 "Rejected. The design team found this idea deeply unsettling."，理由之一正是"magic 隐含绑定"让人不安。`it` 把这种不安直接砸到用户代码上。`*` 无法被任何标识符撞车，兼容性净胜。
- **`_` 呢？** 更不行。VB 的 `_` 已被行继续符占用：单独 `_` 不能作标识符（`Dim _ As Integer` 是错误），且出现在行末时必然是续行标记。`Dim f As Func(Of Blog, String) = _.Url` 里的 `_` 在词法上要么被续行规则纠缠、要么直接报错——`_` 是 VB 里唯一一个"天然不可用作通配符"的字符。这反而帮我们排除了 C# 社区提过的 `_` 占位思路：C# 的 `_` 是 discard（弃元），读者容易与"占位参数"混淆。`Probably`：这是 VB 特有的排除理由，值得写进 speclet 的历史注释。
- **A vs C：魔法清单的门槛。** C 只在编译器认识的 API 上生效，自定义扩展方法、表达式树解释器一概不管用；而 "magic list" 每加一个 API 就多一分维护负担，与我们 2017-12 对 `For` 多变量 "None of us could think of a good reason why this doesn't already work" 的宽容态度相反——那种话只能对"通用机制"说，不能对"白名单"说。C 出局。
- **A vs D：语言特性 vs 工具。** D 的 analyzer 可以提示、重构可以互相转换，却不能让 `*.Url` **编译**；EF 表达式树场景必须由编译器下降，工具替代不了。但 D 的零表面让"值不值得"的天平很诚实——**如果工具 + EF 字符串重载（`Property("Url")`、`Include("Profile.Avatar")`）已经覆盖了 85% 的痛点，语言特性就不必做**。We `Suspect`：EF 各 API 的字符串重载覆盖度需要逐项核实，不能想当然。
- **一个让全场安静下来的观察：** `*.Url` 不是新能力，它只是 `Function(x) x.Url` 的**另一种拼法**。VB 设计原则第 3 条"不引入第二种做事方式"的门槛极高。支持方回应：它不是"第二种做事方式"，而是"把显然的东西默认掉"——和 VB 无处不在的默认（隐式 `Me`、缺省 `Public`、自动属性）同一种气质。这正是本建议的全部张力所在。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`*` 今天**从不**出现在表达式开头：乘法是中缀二元运算，`*` 永远夹在两个操作数之间。因此"表达式开始位置 + `*`"是一个干净的扩展点——任何今天能编译的代码都不含该位置，理论零破坏。规则草案：**`*` 仅在表达式起始处、且下一个 token 是 `.` 时，才解析为通配符**；否则保持语法错误。

需要当场写清的角案例：

```vb
' 合法：表达式开始 + `.` 后缀
Dim getter As Func(Of Blog, String) = *.Url

' 中缀乘法不受影响
Dim total As Decimal = unitPrice * quantity

' 二义陷阱：a * * .Url —— 解析为 a 乘以一个通配符 lambda
Dim nonsense = a * *.Url   ' 可解析，但类型错误（lambda 不是数值操作数）
                           ' 这是新报错，不是既有代码的破坏
```

`nonsense` 那行是新报错，我们需要一个不像谜语的错误文案（"`*` 在此处不是乘法；通配符 lambda 不能作数值操作数"）。另一个边界：**`*` 与 `.` 之间的空白**——VB 是 token 级空白不敏感，`* .Url` 必须与 `*.Url` 等价，这意味着文法要把"`*` + 成员访问链"当作一个复合 token 序列处理，而不是字符拼接。

**未决焦点：`*` 后紧跟 `(`。** 若 v1 只允许纯成员访问链，`*(...)` 天然非法，消歧干净；若放开方法调用（`*.ToString()`），`*(args)` 是否同时表示"调用通配符"？我们不打算放开，见追问 2。

#### 2. 角案例与边界语义

- **`*` 单独出现**（`Dim f = *`）→ 错误。通配符必须有成员访问链。
- **链与投影**：`*.Profile.Avatar` 合法，等价 `Function(p) p.Profile.Avatar`。
- **索引器属性 / 带参属性**（`*.Item(0)`、`*.Addresses("Home")`）→ **v1 明确拒绝**。`Probably`：v1 后若真实需求出现再议，但索引器会把"成员访问"的边界弄脏。
- **`!` 字典访问**（`*.!PrimaryEmail`）→ v1 拒绝。VB 的 `!` 是很有特色的惯用法，但它把"成员访问"扩展成"成员访问 + 字典/XML 访问"两种语义；留 OPEN。
- **方法调用**（`*.ToString()`）→ v1 拒绝。一旦允许调用，"纯投影"的承诺就破了，表达式树下降也复杂化。
- **目标为 `Sub` 委托**：`*.Url` 有返回值，不能转 `Action(Of Blog)` → 报错，与 `Function(x) x.Url` 一致。
- **递归 / 嵌套**：`*` 只有一层；不能在 `*.Url` 里再引用外层 lambda 的参数。`Function(outer) Function(inner) outer.Url` 需要两个名字，本建议不覆盖。

#### 3. 作用域与绑定

`*` 不是一个符号。语义模型里，`*.Url` 的 `GetSymbolInfo` 应返回 `Blog.Url` 属性符号；`*` 本身应解析为**合成参数符号**（名如 `$param`，不进入用户命名空间）。`*` 的类型来源是**目标类型化**：委托参数类型。在重载未决时，`*` 的类型与普通 lambda 一样推迟——直到目标委托确定。这与"递归 Lambda 推断"建议（`proposal-recursive-lambda-inference.md`）不冲突：那边放宽的是"推断何时可以提前"，这边只是"参数名缺省"，两者对合成符号的处理应共用一套基础设施。

#### 4. 与既有特性的交互

- **重载解析与 delegate relaxation**：`*.Url` 作为 lambda 实参参与重载解析，其转换分类必须与等价 lambda 完全一致。我们有真实先例：2015-01-14 纪要关于插值字符串 lambda 的重载决议 —— `f(Function() $"hello")` 在 `Func(Of FormattableString)` 与 `Func(Of String)` 之间 "we'd like this to pick the String overload. The way to accomplish this is to classify the lambda conversion as `DelegateRelaxationLevelReturnWidening`." 通配符绝不能改变 relaxation 等级，否则 `Property(*.Url)` 的重载选择会和 `Property(Function(b) b.Url)` 分叉。**铁律：与等价 lambda 逐位同语义。**
- **表达式树**：EF 场景要求 `*.Url` 下降为 `Expression(Of Func(Of Blog, String))`，且与 `Function(x) x.Url` **同一棵**树（PropertyExpression 节点）；绝不能默认降成编译委托——那会让 EF 解释器无法工作。这是本建议最重要的语义约束，不是 CLR 约束，是"功能契约"约束。
- **ByRef 委托参数**：若目标委托第一参数为 ByRef（罕见），`*` 读取该参数是否合法？`Probably`：与等价 lambda 行为对齐即可（读取 ByRef 合法），但写入（`*.Url = ...`）绝不允许——v1 只有读取。留 OPEN 待原型确认 lambda 对 ByRef 参数的既有规则。
- **Option Strict On / Off 分叉**：见追问 6。
- **`With` 语句**：`With` 内的 `.Member` 指向 With 目标；`*.Member` 是显式参数，二者不冲突。但 `With` 的存在让"`.` 开头的表达式 = 隐式目标"成为 VB 惯例——`*.` 是唯一带显式目标的成员访问，读者需要一点习惯时间。
- **查询理解（LINQ）**：`From b In blogs Select *.Url` 里 `*` 是否=范围变量？范围变量**不是** lambda 参数，语义不同。**v1 拒绝**；后续与查询增强建议一起看（`proposal-query-enhancements.md`）。
- **管道操作符 `|>`**（`proposal-pipeline-operator.md`）：`blogs |> Select(*.Url)` 的协同是诱人的，但两个建议各自未定型前不对表。

#### 5. Breaking change 与兼容性

这是本建议**最强的资产**，也是我们反复核查的点。论证：`*` 只出现在中缀位置（乘法），任何今天能编译的代码里都没有"表达式首 `*`"；因此新语法只占据今天的语法错误区，不触碰任何既有合法代码。需要排除的上下文清单（`Suspect` 为未逐项验证）：

- 二元乘法、`Dim x(*) As Integer`（数组维度）、`New Integer(*) { }`（数组类型——VB 用 `Integer()`，无 `*`）——均无冲突。
- 若 v1 放开 `*!`、`*(` 或独立 `*`，上述"零破坏"论证立即被削弱——这是我们坚持收紧边界的理由。
- **结论**：零破坏可信，但必须由原型做一次**全 token 上下文 `*` 扫描**（把仓库里所有 `*` 位置分类）来证实。这个原型工作项本身不贵，是 v1 的前置条件。

#### 6. Option Strict / 编译选项分叉

必须与等价 lambda 逐位一致：

```vb
' Option Strict On：Object 上无成员 Url ⇒ 编译错误
Dim f As Func(Of Object, Object) = *.Url

' Option Strict Off：晚绑定编译通过，运行期解析
Dim g As Func(Of Object, Object) = *.Url
```

严格/宽松两条路径对"`*` 的类型"的处理不得分叉——`*` 的类型就是委托参数类型，宽松路径下 `Object` 参数的成员访问保持晚绑定，**绝不**因为"我们认识这个 lambda"而早期化。这与 TypeOf 流分析纪要里的规则同源：编译期优化不得改变运行期行为与异常时机。

#### 7. IDE / IntelliSense

- **补全**：输入 `*.` 时，成员列表从哪来？这要求补全引擎在表达式未完成时完成**目标类型推断**（从 `Property(Of Blog, ...)` 的签名推出参数类型 `Blog`）。现有 VB 对 `Function(b) b.` 已有同类能力，但那是"用户写出的参数名"驱动；`*` 没有名字，补全必须由目标委托驱动。这是 v1 最大的**未验证**风险点。
- **着色 / 快速信息**：`*` 现在按运算符着色；在通配符语境应不同色或至少 InfoTip 说明"`*` 表示唯一的 lambda 参数"。悬停 `*.Url` 显示 `Blog.Url`。
- **重构**：展开（`*.Url` → `Function(x) x.Url`）与压缩（反向）重构是 PROPOSAL D 的落点，也可作为本特性的"逃生舱"。

#### 8. 数据 / 普遍性

场景真实（EF 配置、导航属性加载、数据绑定），但**没有任何量化数据**。我们的标杆是主线对 Implicit-default-optional 的 85% 统计——"高频才值得做"。单参数纯成员访问 lambda 在 EF 配置文件里确实高频，但：
- 高频发生在**配置代码**里，不是"数十万安静客户"的主业务代码里；
- EF 自身的字符串重载（`Property("Url")`、`Include("Profile.Avatar")`、`HasMany("Posts")`）已经吃掉一部分场景，代价是失去编译期检查；
- 这不是 parity gap，主线和 C# 都没有，优先级天然低于 parity 类特性。

#### 9. 更简的替代方案

- **现有 lambda**：`Function(x) x.Url` 已经只有 3 个 token 的噪音（关键字 + 括号 + 参数名）。样板不是 lambda，是"参数名"。
- **EF 字符串重载**：`Property("Url")` / `Include("Profile.Avatar")` 覆盖部分场景——`Suspect`：`HasForeignKey` 的强类型表达式形式（`Expression(Of Func(Of TEntity, TKey))`）确实需要 lambda，字符串形式覆盖度需逐项核实。
- **analyzer + 重构**：`Function(x) x.Member` ⇄ `*.Member` 的转换器可以在语言特性落地**之前**提供"预览该特性"的体验，并验证用户是否真的买单。这是低风险验证路径。
- **结论**：语言特性不是唯一路径；工具先行是诚实的 PROPOSAL D。

#### 10. 复杂度 / 成本 / 优先级

解析器改动**小**（表达式入口处 2-token 前瞻），新语法节点 + 语义模型 + 表达式树下降**中**，IDE 补全的目标类型推断是**唯一的硬骨头**。相对价值：只覆盖"单参数纯成员访问投影"这一窄带。这不是"值得做但太难"（2018-02 对 #211 的 "Fantastic idea, and too hard to do." 不适用——它不难，是窄）。优先级应排在模式匹配、可空性流分析、顶层语句之后。

#### 11. 运行时 / CLR 硬约束

无新 IL，纯下降。不触达 CLR 存储规则，PEVerify 无碍。唯一硬约束是**表达式树等价下降**（追问 4）——这是语义规则，不是 CLR 规则。通配符 lambda 不新增任何表达式树节点类型。

#### 12. 值不值得做

- **价值**：中——窄但真实，EF/绑定场景的高频样板，且符合 VB 低仪式基因。
- **成本**：中——解析 + 语义 + IDE 补全，真正的难点集中在 IDE。
- **风险**：低——零破坏论证可信；但 `*` 双语义（乘法/通配）是持续存在的**认知成本**，每次读者看到表达式开头的 `*` 都要停一下。
- **Not all of us are happy with** 把 `*` 作通配符的读感——它像 glob、像正则，不像 VB。`it` 读感更好但破坏兼容。这个两难没有免费午餐。

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心，本建议最亮的一点。
- **不引入"第二种做事方式"（原则 #3）**：最重的扣分项。`*.Url` 是 `Function(x) x.Url` 的并列拼法，扩展了语言表面积。支持方辩护：这是"默认显然之物"，与隐式 `Me` 同气质。
- **读起来像英语、对新手友好（原则 #5）**：扣分。`*.Url` 不像英语；`it.Url` 像英语但破坏兼容。VB 拿不出两全方案。
- **避免隐蔽的语义变化（原则 #7）**：临界。`*` 同一字符在乘法里是运算符、在表达式首是参数标记——虽语法位置可消歧，认知上仍是"一个字符两个意思"。`Return?` 被拒的同类警惕在此回响。
- **冗长只在有用时是美德（原则 #10）**：支持方。单次出现的参数名不帮助理解，删掉是美德的正确应用。
- **与主线关系（对照表 2.3）**：**Anthony 独立延伸**——主线对通配 Lambda 的态度是"无"。没有主线工作可借力，也没有主线冲突；它是纯粹的沙盒激进面。与 XAML 字面量（`{...}`）、管道操作符（`|>`）、递归 Lambda 推断（合成参数基础设施）三个建议必须对表，否则会造出重叠的语法。
- **与其他语言**：Kotlin / Groovy 的隐式单参 `it` 是真实先例；C# 的 lambda 始终需要显式参数名（哪怕类型推断），`_` 只是 discard，不是参数占位。VB 若做 `*.X`，是差异化亮点，也是"我们比两边主线都激进"的又一例证。

### RESOLUTION:

1. **场景成立，动机打折。** 单参数纯成员访问 lambda 是真实样板；但 EF 字符串重载 + analyzer/重构工具已能覆盖一部分，且这不是 parity gap。**价值：中，非头条。**
2. **采纳语法形态 A（`*`）而非 B（`it`/`$0`）**：`it` 作隐式参数会破坏现有标识符，`$0` 读感无关，`_` 与 VB 行继续符冲突。`*` 零遮蔽、扩展点干净，是唯一兼容的拼法。**否决 B、C；D 作为并行验证路径保留。**
3. **v1 范围 = 纯成员访问链**：仅 `.` 后缀、单参数、无方法调用、无索引器、无 `!`、无独立 `*`、无 LINQ 范围变量。放开任何一项都会削弱零破坏论证并弄脏表达式树下降。
4. **铁律：与等价 lambda 逐位同语义**——绑定、delegate relaxation（含 2015-01-14 的 `DelegateRelaxationLevelReturnWidening` 决议）、Option Strict 分叉、晚绑定行为、表达式树下降（必须同一棵树，否则 EF 无法解释）。
5. **`{...}` XAML 绑定语法划归 XAML 字面量建议**（`proposal-xaml-literals.md`）。注意：XAML 字面量建议用的是 `{Binding Command.Name}` 标记扩展形态，与原文的 `Text={*.PrimaryContact.FirstName}` 裸表达式形态**不一致**——两个建议必须由 XAML 团队先对表，本建议不为其背书。
6. **判定：Consider（原型门控）**——非 Active：无原型、IDE 目标类型推断未验证、`{...}` 依赖未定、主线无对应；非 Table / Reject：价值真实、零破坏论证可信、VB 低仪式基因契合。**原型必须交付三件事**：① 全 token 上下文 `*` 扫描（证实零破坏）；② `*.` 补全的目标类型推断验证；③ 表达式树等价下降测试（与 `Function(x) x.Url` 逐节点对比）。
7. **工具先行**：在语言特性前，先做 analyzer/重构（`Function(x) x.Member` ⇄ `*.Member`）与 EF 字符串重载覆盖度盘点。**若工具 + 字符串重载已覆盖 85% 场景，我们乐于不做语言特性**——"We're proud not to do anything" 在这里是体面的退路，不是失败。

### Implication:

- 起草 speclet：文法（表达式首 `*` + `.` 链）、`*` 全 token 上下文兼容性清单、表达式树等价下降规范、错误文案（`a * *.Url`、`*` 单独出现）。
- 最小原型：解析器 2-token 前瞻 + 合成参数符号 + 表达式树下降 + 补全目标类型推断；重点验证 EF `Include` / `Property` / `HasMany` 全链。
- 与 XAML 字面量团队对表 `{...}` 形态（`{Binding ...}` vs 裸表达式）；与管道操作符、查询增强建议登记协同点。
- 补数据：单参成员访问 lambda 在 EF/绑定配置中的占比；EF 各 API 字符串重载覆盖度逐项核实（`Suspect`）。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：ByRef 委托参数下 `*` 读取的精确规则（`Probably` 与等价 lambda 一致，待原型确认）。
- `OPEN QUESTIONS`：`*!Field`（字典/XML 访问）是否在 v1 之后加入；加入会如何影响零破坏论证。
- `OPEN QUESTIONS`：`a * * .Url` 的报错文案与诊断定位。
- `TODO`：全 token 上下文 `*` 扫描清单（把仓库内所有 `*` 位置分类，证明零破坏）。
- `TODO`：量化单参成员访问 lambda 占比，对齐 Implicit-default-optional 的 85% 数据标准。
- `Follow-up`：与递归 Lambda 推断建议共享"合成参数符号"基础设施的接口契约。
- `Follow-up`：EF 字符串重载覆盖度盘点结果（`Suspect`：`HasForeignKey` 强类型形式确需 lambda）。

### 状态

- **LDM 状态：LDM Considering**（原型门控）。
- **三态判定：Consider** — 价值真实但窄、动机被工具/字符串重载部分消化、IDE 目标类型推断未验证、XAML 依赖未定；以"原型三交付 + 工具先行"为 Gate，通过后升 Active，被工具证明可替代则体面 Reject。

---

## 附录：特性评价

# 建议评价报告：proposal-wildcard-lambdas.md

## 评价对象

- 建议：proposal-wildcard-lambdas.md — 通配符 Lambda 表达式（`*.Url` = `Function(x) x.Url`）
- 来源：Anthony 原文 2.3 "Wildcard Lambda Expressions"（`..\AnthonyDesign_wordpress.txt` L403–435；EF 配置链、嵌套成员访问、`Let users = ... Include(*.Profile.Avatar)`、`<TextBox Text={*.PrimaryContact.FirstName}/>` 全部出自该节）
- 配方目标：以 `*.Member` 取代单参数纯成员访问 lambda，消除 EF 配置/数据绑定场景的样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。主效果（EF/绑定样板消除）示例可操作；但关键子效果未验证——`*.` 补全的目标类型推断、表达式树等价下降均无原型；`{...}` XAML 场景依赖另一建议未落定；EF 字符串重载是否已消化部分场景未盘点 | 已检查 | 无原型封顶 3；动机被"EF 自身字符串重载 + 工具替代"部分削弱；非 parity gap |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。契合低仪式/消样板基因（#9/#10）；但 `*` 与乘法运算符冲突、读感像 glob 不像英语（偏离 #5）、是"第二种做事方式"（偏离 #3，最重）；`{...}` 示例打包了 XAML 绑定这一无关能力 | 已检查 | 同字符双语义临界于 #7；`it` 方案因兼容性被否，`*` 是"唯一兼容"而非"最优读感"的选择，属无奈折中 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文逐字一致、4 个未决问题具体诚实（健康区间）；但关键边界含糊——无文法（BNF）与消歧规则（`*`+`(` 的解析未定型）、无 breaking-change/compat 分析、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）、XAML 归属未定 | 已检查 | 缺文法与兼容性章节；`*` 的解析消歧是本建议的核心，却整体留在未决 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（加速 EF 配置编写）与光（差异化 DX）正向；风（Anthony 独立延伸、与主线扩展节奏断裂）与暗（`*` 双语义认知风险；XAML 依赖）负向；文档未充分权衡 XAML 依赖与 EF 字符串重载的替代压力 | 已检查（预测待定） | 风/暗影响待定，须"已采纳"后确认；"零破坏"论证未以扫描证实，是可信未证实 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料=Anthony 2.3（原创，可追溯）；`it` 替代方案借鉴 Kotlin/Groovy 惯例（作为被否方案留档）；`{...}` 借鉴 XAML 标记扩展但原文未显式点明；无 VB6 继承、无主线建议、无闭源杂质 | 已检查 | 未显式标注章节号；`{...}` 的 XAML 血缘未说明；借鉴 Kotlin `it` 作为被否方案未在 Alternatives 中标注来源 |

## 设计原则对照

- **与 VB 基因：部分一致**——契合 #9（消样板）、#10（单次参数名是"无用冗长"）；偏离 #3（第二种做事方式）、#5（`*` 读感）、#7（同字符双语义临界）。
- **与主线关系：Anthony 独立延伸**（对照表 2.3 明确"主线无"）。不冲突，也无主线可借力；与 XAML 字面量、管道操作符、递归 Lambda 推断三个建议有交集需协调。
- **破坏性变更：理论无**——`*` 不作表达式首 token，新语法只占今天必错的位置；但"零破坏"依赖 v1 收紧为纯 `.` 成员访问链，且须经全 token 上下文扫描证实。放开 `!`/`(`/独立 `*` 则论证被削弱。

## 总评

- **达成程度：部分达成**——概念与价值成立（样板真实、语法扩展点干净、零破坏可信）；动机强度、边界收敛、IDE 验证、XAML 归属未完成。
- **LDM 三态建议：Consider（原型门控）**——价值中、成本中、风险低但认知成本持续；以"原型三交付（零破坏扫描 + IDE 补全目标类型推断 + 表达式树等价）+ 工具先行"为 Gate。
- **主要问题**：① 是"第二种做事方式"，语言表面积扩张（原则 #3）；② `*` 乘法双语义是持续认知成本，`it` 又因兼容性不可行；③ 关键子效果（IDE 补全、表达式树等价）无原型验证；④ `{...}` XAML 场景归属他建议且形态不一致；⑤ 动机被 EF 字符串重载 + 工具替代部分消化，无量化数据。

## 返工建议

- **补充章节**：文法与消歧规则（表达式首 `*` + `.` 链的 BNF；`* .Url` 空白等价；`a * *.Url` 的报错）、Compatibility/breaking-change 分析（全 token 上下文 `*` 扫描清单）、表达式树等价下降规范（与 `Function(x) x.Url` 逐节点对比的验收）。
- **补充证据**：最小原型三交付；单参成员访问 lambda 的占比数据（对齐 85% 标准）；EF 各 API 字符串重载覆盖度逐项核实（`HasForeignKey` 强类型形式确需 lambda）。
- **未决问题处理**：v1 明确拒绝方法调用/索引器/`!`/独立 `*`/LINQ 范围变量；ByRef 委托参数与等价 lambda 对齐；`{...}` XAML 划归 `proposal-xaml-literals.md` 并由 XAML 团队先定 `{Binding ...}` 形态。
- **设计探索**：工具先行（analyzer + 重构 `Function(x) x.Member` ⇄ `*.Member`）作为语言特性的低成本验证路径；与递归 Lambda 推断共享合成参数符号基础设施；与管道操作符、查询增强的对表登记。

---

## 附录：C# 生态与互操作考量

本附录评估「通配符 Lambda（`*.Url` = `Function(x) x.Url`）」与 C#/CLR/.NET 现实方向的对应关系。依据 `..\..\csharplang-index.md` 与 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）。本提案主题落在 C# 的两条现实线上：**lambda discard 参数（C# 9）与 lambda 目标类型化 / 自然类型（C# 10）**（索引 T7/T8 相关、M2 动态权衡）。先声明诚实边界：这是一个**纯下降 / 纯语法糖特性**——正文追问 11 已确认「无新 IL、不触达 CLR 存储规则、不新增表达式树节点」，因此**没有元数据 / CLR 表面**可谈。其与 C# 生态的互操作相关性集中在三处：① C# 对「未用参数不必命名」的官方表达（discard `_`）与本提案的直接对应；② `*` 的类型来自目标委托的推断机制与 C# 目标类型化 / 自然类型的同构；③ 面向 C# 生态库（EF 表达式树）的**功能契约**（与等价 lambda 同一棵树）。C# 13 `params` collections 经核实与 lambda 参数省略**无直接关系**（见「现实 vs 提案」表末行），如实列作脱节，不硬凑。

正文已直接引用 C# 侧证据（VB 基因对照：「`_` 只是 discard，不是参数占位」）；本附录补充其**未覆盖的生态 / 元数据层面**与逐字原文。

### 相关 C# 现实方向

1. **Lambda discard 参数（C# 9，lambda-discard-parameters）——C# 对「未用参数不必命名」的官方表达。** C# 允许 `_` 作 lambda / 匿名方法参数。原文（Summary，→ `proposals\csharp-9.0\lambda-discard-parameters.md`）：
   > "Allow discards (`_`) to be used as parameters of lambdas and anonymous methods."
   其动机与本提案「样板是参数名」的论断**字面同题**（同文件 Motivation）：
   > "Unused parameters do not need to be named. The intent of discards is clear, i.e. they are unused/discarded."
   但 C# 的关键收敛点（同文件 Detailed design）：
   > "Note: if a single parameter is named `_` then it is a regular parameter for backwards compatibility reasons."
   **C# 的答案是「把参数名写成 `_`」，不是「省略参数」**——单参数时 `_` 甚至是普通参数（最接近 `*.Url` 的 C# 写法仍是 `_ => _.Url`，须写全）。这是本提案与 C# 的分水岭：C# 承认「不用命名」，语法上却永远拼出一个 `_`；VB `*.Url` 把参数**整个省掉**，走得更远。C# 对「不进入任何作用域」的纪律与本提案合成参数符号一致（同文件 Detailed design）：
   > "Discard parameters do not introduce any names to any scopes."

2. **Lambda 目标类型化 / 自然类型（C# 10，lambda-improvements）。** `*` 的类型来源是目标委托参数类型（正文追问 3）；C# lambda 自 C# 3 起即目标类型化。C# 10 为「无目标类型」场景引入自然类型，但要求**参数类型显式**（→ `proposals\csharp-10.0\lambda-improvements.md`，Detailed design，原文含笔误 `parameters types`）：
   > "An _anonymous function_ expression … has a natural type if the parameters types are explicit and the return type is either explicit or can be inferred."
   含义：**省略 / 隐含参数名的 lambda 在 C# 里永远没有自然类型**，必须落到目标委托——按正文追问 3 的目标类型化规则，VB `Dim f = *.Url`（无目标类型）同样无类型来源即报错，两者**同构**。两边共享同一套「目标类型驱动参数类型」机制，通配符只是在其上加一层「参数名缺省」。

3. **动态 / 晚期绑定边缘化（索引 T7），C# 官方视 VB 为独立模型。** `dynamic`（C# 4）长期无大演进、unsafe-evolution 质疑其在 AOT 下的安全性；与本提案 `Option Strict Off` 的 Object 化晚绑定路径方向相反。unsafe-evolution 的 VB 小节也确认 C# 官方将 VB 视为无 unsafe / 指针的独立模型（→ `proposals\unsafe-evolution.md`，VB 小节）：
   > "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
   这为「VB 保留自身差异化语法（如 `*.Url`）」提供了 C# 侧的制度性空间。

4. **表达式树 / EF 的跨语言消费契约（功能契约，非 CLR 规则）。** `*.Url` 必须下降为与 `Function(x) x.Url` **同一棵** `Expression(Of Func(...))` 树（正文追问 4，RESOLUTION #4）。这不是 CLR 约束，而是**互操作契约**：EF（C# 生态库）同时消费 C# 与 VB 的表达式树 lambda，树形不一致会让解释器分叉。C# 侧对表达式树 lambda 的约束是同一套（可解释性），本提案无新增 C# 侧负担。

### 现实 vs 提案

| 本提案要点 | C# 现实 | 判定 |
|---|---|---|
| `*.Member` 把唯一 lambda 参数**整体省略** | C# discard 参数仍须拼出 `_`；单参数 `_` 是普通参数（须写 `_ =>`），C# 从不省略参数名 | **兼容 / VB 超出 C#**——C# 承认「未用参数不必命名」但只做到「改名」，VB 做到「省略」；同一 delegate / 表达式输出，不冲突，VB 差异化增量 |
| `*` 合成参数符号**不进入用户命名空间** | C# discard 参数 "do not introduce any names to any scopes" | **兼容（同构）**——两边对「隐式参数不得遮蔽 / 污染作用域」的纪律一致 |
| `*` 类型来自目标委托（目标类型化） | C# lambda 目标类型化（自 C# 3）；自然类型须参数类型显式，省略 / 隐含类型者无自然类型 | **兼容（同构）**——无目标类型时 `Dim f = *.Url` 报错与 C# 无目标 lambda 报错是同一规则 |
| `Option Strict Off` 下 `*.Url` 走 Object 晚绑定 | C# dynamic 边缘化、AOT 视为负担（T7） | **方向相反 / 需桥接**——VB 保留遗产晚绑定（COM/Office），见适应建议 1 |
| 表达式树等价下降（EF 契约） | C# 表达式树 lambda 同样受可解释性约束；EF 跨语言消费 `Expression<T>` | **需桥接（功能契约）**——必须与等价 lambda 同一棵树，否则 C# 生态库消费分叉 |
| C# 13 `params` collections（`params ReadOnlySpan<T>` 等） | 生态 API 设计走向：以 `params` 集合参数避免数组分配 | **脱节（无直接互操作面）**——属方法参数传递约定，与 lambda 参数命名正交；`*.Url` 不涉及 `params` 机制，经核实无交集 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态。** `*.Url` 在 `Option Strict On` 下是强类型投影（目标委托参数类型），纯下降为标准 delegate / 表达式树，天然 AOT 友好（不引入动态）——与决策文件 M2「默认安全、按需动态」双模路线一致；`Option Strict Off` 的 Object 化路径保留为遗产晚绑定，**绝不**因「我们认识这个 lambda」而早期化（正文追问 6）。通配符 Lambda 不应成为 AOT / trimming 摩擦点：安全半边零动态。

2. **表达式树等价 = VBScript.NET 与 C# 生态库（EF）的互操作契约。** `*.Url` 与 `Function(x) x.Url` 的逐节点等价（RESOLUTION #4 铁律）本身就是互操作条款：EF 等 C# 生态库靠**同一棵 `Expression<T>` 树**解释查询，VB 侧任何「看似相同但不同形」的下降都会在 C# 库侧表现为不可解释 / 行为分叉。原型三交付中的「表达式树等价下降测试」应同时作为**互操作验收标准**（不止语义正确性）。

3. **无新元数据 = 低桥接成本（本提案的强项）。** 与递归 Lambda 推断（需桥接 `allows ref struct` / `CompilerFeatureRequired` 元数据，见该会议附录）不同，通配符 Lambda 纯下降为标准委托 / 表达式树，**不产生任何新元数据**——VB 编译器无需为它识别新属性，桥接点全部在语义层（表达式树契约、目标类型推断）。工具先行（analyzer / 重构）路径也与 C# source-gen / analyzer 文化同向（索引 T6）。

4. **`*` 选型被 C# discard 语义反向佐证。** 正文追问已述 VB 不能用 `_`（行继续符 + 与 C# discard 混淆）。C# 侧证据补强：C# 已把 `_` 定义为「弃元」语义，VB 若用 `_` 占位，跨语言读者会把「占位参数」误读为「丢弃参数」；`*` 与 C# 任何既有 lambda 语法零重叠，不会被 discard 语义污染。这是选 `*` 而非 `_` 的**生态层**理由，建议写进 speclet 的历史注释。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #2（选 `*` 弃 `_`）**：被 C# discard 参数语义**反向佐证**——C# 已把 `_` 定义为弃元，VB 若占用会带来「占位 vs 弃元」的跨语言混淆；`*` 与 C# lambda 语法零重叠。决议不动，可补生态层理由入 speclet 历史注释。
- **RESOLUTION #4（与等价 lambda 逐位同语义）**：C# discard 参数 "do not introduce any names to any scopes" 为「合成参数不污染作用域」提供 C# 侧先例；表达式树等价被确认为 EF 互操作契约。决议不动。
- **RESOLUTION #7（工具先行 / 85% 即退路）**：与 C# analyzer / source-gen 文化同向（T6），确认工具路径是生态常规做法而非 VB 特例。决议不动。
- **三态判定（Consider）**：**不变**。新增一条**生态观察**：C# 承认「未用参数不必命名」但止于 discard（改名），未做省略——.NET 生态对 lambda 参数省略**无期待**（显式参数名 + `_` 弃元是现状惯例）。因此 `*.Url` 既无 parity 压力、也无生态反向压力，是纯 VB 差异化增量，互操作风险低。不改变本次判定。

### 引用纪律与 OPEN QUESTIONS

- 本附录所有 C# 原文均逐字核对，来源标注如上：`→ proposals\csharp-9.0\lambda-discard-parameters.md`、`→ proposals\csharp-10.0\lambda-improvements.md`、`→ proposals\unsafe-evolution.md`；C# 13 `params` collections 的脱节判定见 `→ proposals\csharp-13.0\params-collections.md`（Summary / Motivation）。
- **OPEN QUESTIONS**：① EF 各 API（`Include` / `Property` / `HasMany` / `HasForeignKey`）对「表达式树 lambda 与字符串重载」的消费路径是否按**同一棵树**接受——正文 `Suspect` 已列，互操作视角需逐项核实；② C# 14 表达式树放宽（可选 / 具名参数，索引 T7）后，表达式树 lambda 的可解释性边界是否有变——本附录未深挖 C# 14 表达式树原文，列为待核实；③ C# discard 参数在 IDE / 补全中的呈现（`_` 按弃元着色）是否值得 VB 侧参考为 `*` 的着色先例。
