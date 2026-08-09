# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次我们审议 `With` 语句的三项增强：命名 `With` 变量、`.Me` 伪成员、`With` 目标自身的复合赋值。三件提案捆绑在同一份文档里，但我们的结论是三者命运不同——这是本次讨论的主线。

## Agenda

* [Proposal: With 增强（命名 With 变量 / `.Me` 伪成员 / 复合赋值）](#proposal-with-增强)

## Proposal: With 增强（命名 With 变量 / `.Me` 伪成员 / 复合赋值）

_Related: `meetings/2016/LDM-2016-05-06-VB.md` – Tuples 讨论中的 "Auto With the return variable" 反提案（已 Table）；`meetings/2014/LDM-2014-02-17.md` – `With`/`From` 同用、`With` 块内 `!` 默认属性访问；Anthony 原文 3.7 `With`（`..\AnthonyDesign_wordpress.txt` L976–1005）及 StoredProcedure 嵌套 `With` 示例（L2241–2266）_

### 场景与缺口

`With` 语句是 VB6 遗传的核心简写：把"对同一个目标的连续成员操作"收进一个块里，用前导点省略目标。但我们确认了它的两个真实缺口：

1. **目标自身无名可寻。** 块内只能 `.Member`，无法把目标本身当值用（传入方法、放进集合）。对**匿名目标**（`With command.CreateParameter()` 直接跟调用）这是硬缺口；对**已命名目标**其实是软缺口——今天先 `Dim parameter = ...` 再 `With parameter` 就能绕过去：

```vb
' 今天：先建临时局部再 With —— 合法，且块内 parameter 可当值用。
Dim parameter = command.CreateParameter()
With parameter
    .ParameterName = "@id"
    .DbType = DbType.Guid
    .Value = id
    command.Parameters.Add(parameter)
End With
```

2. **对目标本身做复合赋值无语法。** `With builder : &= item.Header : End With` 今天编译不过——`&=` 需要一个左值，而前导点才把接收者换成 With 目标；裸 `&=` 没有接收者。这是新语法缺口。

`With` 块本身是 VBScript/VB6 资产，VBScript.NET 的 ADO 参数设置样板（`CreateParameter` + 四个字段 + `Parameters.Add`）正是它的典型受益场景。

### 候选方案

**PROPOSAL A — 全三件一起上。** 按建议原文：命名 `With` 变量（`With parameter = expr`）、`.Me` 伪成员、块内对目标自身的 `&=`。

**PROPOSAL B — 只做命名 `With` 变量 + `.Me`，复合赋值不做。** `.Me` 与命名变量分别覆盖"匿名目标可引用"与"命名目标可引用"两侧；`&=` 留待单独的运算符建议解决。

**PROPOSAL C — 只做 `.Me`。** 命名 `With` 与今天的 `Dim t = expr : With t` 等价（见上），是纯糖；`.Me` 是唯一真正新增的能力。语法增量也最小——`.Me` 只是 `With` 块里一个原本报错的成员名，文法零改动。

**PROPOSAL D — 什么都不做。** `With` 是遗产特性，VBScript.NET 可以继续要求作者先 `Dim` 临时变量再 `With`。

### 权衡：Q&A

- **命名 `With` 变量 vs `.Me`：是并集还是互相替代？** 命名变量解决"要传目标本身"且块内可读性好（`parameter` 比 `.Me` 更明白）；`.Me` 解决"不想为一个一次性目标建局部"（省一行 + 缩窄作用域）。两者覆盖不同痛点，不是替代关系。但**命名变量的增量价值确实只有"省一行 Dim + 收紧作用域"**——这不是否定它，而是把它从"解决缺口"降级为"体验优化"。
- **C 的诱人与否。** `.Me` 是三个里唯一"文法是既有的"（`With` 块内 `.member` 本来就是文法槽，`Me` 是保留字、`.Me` 今天必然报错，引入零语法风险）。它也是唯一真正打开新能力（匿名目标可引用）的一件。但命名变量提供了比 `.Me` 更好的可读性——`.Me` 与类内 `Me` 的拼写冲突是真实代价（见 LDM 追问 #1）。
- **`&=` 是不是三件里最弱的？** 是。动机（`StringBuilder` 追加样板）今天用链式调用已解决：

```vb
' 今天就能写：.Append 返回 builder，可连续链。
With builder
    .Append(item.Header).Append(vbCrLf).Append(item.Description).Append(vbCrLf)
End With
```

  而提议的 `&=` 要"按目标类型决定"是展开 `builder = builder & ...` 还是调用 `Append`——这是**运算符被译为方法调用**，VB 没有先例，而且与 `With` 求值一次的语义直接打架（见 LDM 追问 #2、#4）。我们对运算符扩展有明确的怀疑史：2016 年 Tuples 讨论里，`location += acceleration`（运算符在元组上逐成员分布）得到的是 "Cute but nope."。这次 `&=` 的复杂度远高于那个、价值远低于它。
- **D 的代价。** 什么都不做的代价是：匿名 With 目标依然无法引用（必须回退 `Dim` 临时变量），ADO/构建器样板依然多两行。这是可承受的代价，也是主线一贯的立场——但 VBScript.NET 既然要盘活 VB6 的 `With` 遗产，我们可以做得更好。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**命名 `With` 变量是文法真歧义。** `With` 后接一个表达式；`a = b` 在 VB 里是合法表达式（相等测试）。`Probably`：`With flag = otherFlag` 今天被解析为 `With (flag = otherFlag)`——对一个布尔结果开 `With`（退化但合法）。引入 `With name = expr` 后，同样的词形变成"命名声明"。这是**既有代码的语义改变**（重解析），虽然现实中没人会 `With` 一个布尔相等式，编译器仍必须给出一条消歧规则。

我们的倾向：**`With` 后紧跟 `identifier =` 视为命名声明**（与 `For i = ...`、`Dim x = ...` 同族的上下文相关判定）；想表达相等必须加括号 `With (a = b)`。这与 VB 处理"语句级 `x = y` 是赋值、表达式级是相等"的既有惯例一致。代价：一条退化代码路径的重编译变化，需写进 Compatibility 分析并配 `langversion` 门控。

`.Me` 与 `&=` 无文法歧义：`.Me` 落在既有 `.member` 槽、`Me` 是保留字故 `.Me` 今天必错（除非 `[Me]` 转义成员，无冲突）；裸 `&=` 今天无接收者、必错，都是纯新增。

#### 2. 角案例与边界语义

**`With` 求值一次的交互是本建议最硬的地基。** VB 的 `With` 把目标表达式求值一次存入隐藏临时，块内所有 `.Member` 都打在临时上。两个直接后果：

- **值类型目标不写回。** `With someStruct : .X = 5 : End With` 不会把 5 写回 `someStruct`（经典 VB 陷阱）。命名变量如果被当作"别名"看待，作者会期待它写回——但它与 `.Member` 共享同一个临时，**不写回**。语义必须写成：命名变量与 `.Me` 都是**指向求值一次之临时**的只读别名，不改变任何 `With` 现有写回语义。
- **`&=` 无处安放。** 若 `&=` 展开成 `target = target & x`，而 `target` 是临时——等于把结果写进临时、不写回，毫无意义。要让 `&=` 有意义，要么**重求值原左值表达式**（`With builder` 时是 `builder`，`With GetString()` 时是调用——不可赋值；`With obj.Prop` 时每轮重走 getter/setter，违背求值一次），要么**译成方法调用**（`StringBuilder.Append`）。两条路都改变 `With` 的既有契约。结论见 RESOLUTION。

**嵌套 `With`。** `.Me` 必须绑定到**最近封闭**的 `With` 目标。Anthony 的 StoredProcedure 示例正是这样用的——外层命名、内层匿名，`.Me` 指内层参数：

```vb
With command = connection.CreateCommand()
    .CommandType = CommandType.StoredProcedure
    With .CreateParameter()
        .ParameterName = "@city"
        .DbType = DbType.String
        .Value = city
        command.Add(.Me)          ' .Me = 内层参数；外层 command 因已命名而可达
    End With
    Using reader = .ExecuteReader()
        ' ...
    End Using
End With
```

我们**不**为此引入"引用外层目标"的语法（如 `..Me` 或 `Me.Me`）——需要外层目标时把它命名即可。这个限制我们明确接受，并写进 spec。

#### 3. 作用域与绑定

- **命名变量的作用域 = `With` 块**，是块级局部；语义模型在 `parameter` 处返回局部符号（`LocalSymbol`），类型 = 目标表达式的静态类型。嵌套 `With` 里，命名变量在各自块内可见；与既存外层局部同名 → 与今天 `Dim` 在块内重名的规则一致，报错而非遮蔽。
- **`.Me` 返回什么符号？** 它不是真实成员。`Probably`：语法树中作为 `MemberAccessExpression`，绑定为一个合成的伪符号（类似 `My` 的处理），语义模型 `GetSymbolInfo(.Me)` 返回该伪符号或 `Nothing`。这必须与 IDE 团队一起定——补全列表里不应把 `.Me` 当普通成员展示，InfoTip 应显示"With 目标"。
- **命名变量可否重绑定？** 我们倾向**禁止**（只读别名）。若允许 `parameter = other`，则 `.Member` 是否跟随新值立刻产生"别名 vs 拷贝"问题，且与求值一次冲突。`ReadOnly` 别名语义干净、可预测。

#### 4. 与既有特性的交互

- **Late binding / Option Strict Off**：`With expr` 在宽松模式下，`.Me` 的静态类型即目标表达式类型；若为 `Object`，`.Me` 传给方法实参与今天 `.Member` 同样走晚期绑定——无新语义，只多了"能引用目标"这一个口子。规则：`.Me` 是编译期构造，不改变任何既有绑定的早晚性。
- **ByRef**：把命名变量作 `ByRef` 实参？只读别名不可作写实参（`ByRef` 需可写左值）——报错，与 `ReadOnly` 局部一致。`.Me` 同理。
- **Lambda 捕获**：命名变量被 lambda 捕获时，lambda 可延迟执行而 `With` 块已结束。只读别名在 lambda 内只读捕获——语义可定义，但 `&=` 若引入写回，闭包捕获就变得危险。又一个支持砍 `&=` 的论据。
- **与 `With` 语句自身的递归交互**：命名变量 + `With .CreateParameter()`（内层目标是外层成员）已在上面示例验证——内层 `.Me` 指内层，外层靠命名。无死锁。

#### 5. Breaking change 与兼容性

- **`With x = expr` 重解析是唯一的真实破坏**，且是退化路径（见 #1）。其余两件是纯新增（`.Me`、裸 `&=` 今天必报错）。
- 无 `Shadows`/重载重解析类风险：这三件都不触碰成员查找规则（`.Me` 是伪成员，不与任何真实成员名竞争）。
- 破坏程度：极低、范围极小，但仍需按流程走 `langversion` 门控 + Compatibility 章节。

#### 6. Option Strict / 编译选项分叉

- **命名变量与 `Dim x = expr` 完全同轨**：Option Strict On + Option Infer On → 推断类型；Option Strict On + Infer Off → 无 `As` 子句即报错（BC30990 同款）；Option Strict Off → 按推断规则走。v1 **不引入** `With parameter As Type = expr` 的 `As` 子句；若用户需要，走 `Dim` 声明。两条路径行为一致。
- **`&=` 是这里最乱的**：Option Strict Off 下 `&= ` 对 `Object` 目标是晚期绑定运算符，On 下是类型化运算符；Append 魔法只在 On 下可定义。**两条路径根本不一致**——这是砍掉它的第三个理由。
- `.Me` 无 Strict 分叉（纯编译期，见 #4）。

#### 7. IDE / IntelliSense

- 补全：`With` 块内补全 `.` 时，`.Me` 应作为伪条目置顶展示；命名变量的补全即局部变量补全。
- 语义模型 / 查找引用：`.Me` 是伪成员，改名、导航、Find All References 均需特殊处理；`GetSymbolInfo(.Me)` 的返回是 OPEN QUESTION。
- 签名帮助：`command.Parameters.Add(.Me)` 的 InfoTip 应显示参数类型即目标静态类型。
- 这些都不阻塞设计，但必须在原型里验证——"不验证等于没设计"。

#### 8. 数据 / 普遍性

- ADO `CreateParameter` 样板是真实且高频的 VBScript 遗产场景，但没有占比数据。`With` 语句本身在新代码中的使用率**缺乏证据**——主流的 `With` 批评（可读性差、值类型陷阱）暗示其流行度在下降。`Suspect`：命名 `With` 与 `.Me` 的真实受益人群以 VB6 迁移者为主，这正是 VBScript.NET 的目标人群；但"数十万安静客户"里需要"把 With 目标当值传出去"的刚性频次，我们无法量化。
- 主线侧信号：2016-05-06 我们审议过 "Counter-proposal: Auto "With" the return variable so you can use .min and .max in the scope of the function."，结论是 "Let's table this until we get more user/dogfooding feedback. We'll soon know whether this is essential or not."——主线对 With 相邻的扩展一贯**等用户反馈**。十年过去，没有看到强烈的社区回潮。这应当让我们对"高优先级"保持克制。

#### 9. 更简替代

- **`.Me` 的最大竞争者是我们自己**：`Dim t = expr : With t`（见场景部分）。`.Me` 的价值 = 省一行 + 匿名目标直接可引用。这个价值真实但克制。
- **`&=` 的最大竞争者今天就在**：`.Append(...).Append(...)` 链式调用。提案未在 Alternatives 里提它，是明显遗漏。
- Analyzer/代码生成器可以提示"此 With 目标值得命名"，但不能代替"匿名目标可引用"这一语言能力。

#### 10. 成本 / 优先级

- `.Me`：文法零改动、绑定小（伪成员解析 + 合成符号）、IDE 中等。成本最低、能力唯一。
- 命名 `With`：上下文相关文法（`identifier =` 消歧）+ 声明规则复用 `Dim`。成本中低。
- `&=`：需要全新的"运算符→方法"降低机制，或改写 `With` 求值契约，二者都是大工程。成本高、价值被链式调用覆盖。**"值得做但太难"在这里不成立——它根本不够值得。**

#### 11. 运行时 / CLR 硬约束

- 无新约束。`.Me` 与命名变量是编译期别名，codegen 不变（仍访问既有临时）；`&=` 若做 lvalue 回写也只是普通赋值 IL。不触达 PEVerify，无表达式树问题（表达式树按目标静态类型生成）。CLR 不是约束，是借口——真正的约束来自 `With` 的求值契约本身。

#### 12. 值不值得做

- `.Me`：价值（消除真实样板、打开新能力）高；成本低；风险低。**值得。**
- 命名 `With`：价值中（纯糖，但可读性好）；成本中低；风险有（文法消歧 + 重解析破坏，均小）。**值得，但优先级低于 `.Me`。**
- `&=`：价值被替代方案覆盖；成本高；风险中高（求值契约、Option Strict 分叉、闭包捕获）。**不值得，本阶段否决。**

### VB 基因对照

- **消除常见样板（原则 #9）**：`.Me` 与命名 `With` 正中靶心——把"`Dim` 临时变量 + `With`"两行合成一行/零行，且保留 `.Member` 缩写。
- **不引入"第二种做事方式"（原则 #3）**：`.Me` 与命名变量是**互为第二方式**的一对（同一能力两种写法），我们接受——因为两者覆盖"匿名/命名"两侧，且 `.Me` 是既有文法槽的合法化。`&=` 则是为 `Append` 引入第二种方式，拒绝。
- **避免隐蔽语义变化（原则 #7）**：命名 `With` 的 `With x = expr` 重解析是隐蔽变化的苗头，靠消歧规则 + `langversion` 门控对冲；`&=` 的"每轮重取目标 vs 求值一次"是同类问题且无解，故拒。
- **读起来像英语（原则 #5）**：`command.Parameters.Add(.Me)` 读得通，但 `.Me` 与类内 `Me` 的拼写混淆是真实减分——这是它不如命名变量可读的地方，也是我们要求 IDE InfoTip 明确标注的原因。
- **保持 VB-like（原则 #2）**：三件都是 VB 词汇（`With`、`&=`、`Me`）的再利用，无外来语法。但"运算符译为方法调用"（Append 魔法）在**语义层面**不 VB。
- **与主线关系（对照表 2.3）**：`With` 语句块增强在 vblang 主线无活动（2014–2018 会议笔记中无该主题），2016 年 Auto-With 反提案被 Table 待用户反馈——这是 **Anthony 独立延伸**，方向与"盘活 VB6 资产"一致，但主线对其持"等证据"的保守态度。与主线的多 `For` 变量、`Select TypeOf` 等 Approved 主题不同，本件没有主线背书。

### RESOLUTION:

1. **拆分本建议**：三件独立评估、独立排期。捆绑是三件一起上（PROPOSAL A）会拖累最强的 `.Me`。
2. **`.Me` 伪成员：采纳（Active）。** 绑定最近封闭 `With` 目标；嵌套时内层 `.Me` = 内层目标，外层目标必须命名；不引入 `..Me`/`Me.Me`。`.Me` 是编译期伪符号，与任何真实成员不冲突（`Me` 为保留字）；codegen 复用既有临时。
3. **命名 `With` 变量：原则采纳（Consider，优先级次之）。** 语法 `With name = expr`；消歧规则 = `With` 后 `identifier =` 视作命名声明，相等须加括号；命名变量是**只读别名**（禁重绑定），与 `Dim x = expr` 同轨处理 Option Strict/Infer 分叉；v1 无 `As` 子句。
4. **复合赋值 `&=`（作用于 With 目标自身）：本阶段否决（Reject）。** 三个理由：(a) `StringBuilder` 追加已被 `.Append(...).Append(...)` 链式覆盖；(b) 与 `With` 求值一次契约冲突（写回需重求值左值，违背既有语义）；(c) Option Strict 两路径无法一致。若未来重启，先回答"lvalue 写回 vs 运算符→方法"二选一。
5. **不改变 `With` 求值一次、值类型不写回等任何既有语义**；`.Me`/命名变量都是指向既有临时的新引用方式。

### Implication:

- 撰写最小原型：`.Me` 伪成员解析 + 合成符号 + 嵌套绑定（最近封闭）；命名变量的声明规则复用 `Dim`。验证语义模型 `GetSymbolInfo(.Me)` 与 IDE 补全/InfoTip。
- 起草 speclet：`.Me` 绑定规则、命名变量消歧文法、只读别名语义、与 `Dim` 的 Option 分叉对齐表、`langversion` 门控。
- 补 Compatibility 分析：`With x = expr` 重解析的退化破坏逐条列证。
- 在 Alternatives 中补充 `.Append` 链式调用作为 `&=` 的替代证据，归档本次否决。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`.Me` 的 `GetSymbolInfo` 返回什么（伪符号 vs `Nothing`）——与 IDE 团队共同定夺。
- `OPEN QUESTIONS`：`With (a = b)` 的现有合法性与真实占比（`Probably` 合法，需编译器验证并量化）。
- `OPEN QUESTIONS`：命名变量的 `As` 子句（`With x As Type = expr`）v1 后是否按需补上。
- `TODO`：量化 `With` 语句使用率与 ADO 参数样板占比，为普遍性补证据。
- `Follow-up`：将 `&=` 的否决与理由记入归档，防止它在"运算符增强"的其它建议中复活而不带上下文。

### 状态

- **LDM 状态：Active（`.Me`）/ Consider（命名 `With`）/ Rejected（`&=`）。**
- **三态判定：Consider（拆分落地）** — 最强件 `.Me` 先行；命名 `With` 紧随；`&=` 否决留档。

---

## 附录：特性评价

# 建议评价报告：proposal-with-enhancements.md

## 评价对象

- 建议：proposal-with-enhancements.md — `With` 增强（命名 `With` 变量 / `.Me` 伪成员 / 复合赋值）
- 来源：Anthony 原文 3.7 `With`（`..\AnthonyDesign_wordpress.txt` L976–1005）；嵌套 `With` + `.Me` 组合另见于 StoredProcedure 演示（L2241–2266，`Replaces` 章节）。**提案文档本身未标注章节来源。**
- 配方目标：让 `With` 目标本身可命名、可引用（`.Me`）、可复合赋值（`&=`），消除 ADO/构建器样板

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 的真实缺口（匿名目标不可引用）成立，`.Me` 示例可操作；但 `&=` 子效果消退——StringBuilder 追加已被 `.Append` 链覆盖，且"按目标类型决定 Append 还是展开"语义未定型，该子效果证据悬置 | 已检查（无原型） | 核心语法未定型 ⇒ 效果封顶 3–4；`&=` 声称的效果与既有链式调用重复 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`.Me`/命名 `With` 是 VB6 `With` 的延续（VB 化改造）；`&=`（目标自身复合赋值 + Append 魔法）是打包的次要无关能力，与既有 `With` 求值一次语义交互未理顺 | 已检查 | Append"运算符→方法"降低无 VB 先例、属杂质；与 `With` 契约冲突未识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与 Anthony 3.7 逐字一致；但 Drawbacks 术语自相矛盾（"对目标**成员**的复合赋值" vs 示例中裸 `&=` 作用于**目标自身**）；无文法/BNF、无 Compatibility、无 Option Strict 分析；3 个未决问题全是核心语义（嵌套 `.Me`、重绑定、Append 降低）；状态行为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | Alternatives 遗漏 `.Append` 链式调用这一最有力竞争者；未标注来源章节 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/光正（盘活 VB6 `With` 资产、VBScript.NET 低仪式 ADO 样板）；雷小正（微提速）；风受损（`&=` 的语义分叉破坏演化一致性）；暗（`With x = expr` 重解析是隐蔽语义变化，`.Me` 与类 `Me` 认知冲突被作者自认但未给对冲） | 已检查（预测待定） | 实际影响须"已采纳"后定；与主线的保守张力未讨论 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源 = Anthony 3.7 未标注；继承 VB6 `With` 未点明；`.Me` 与既有 `Me` 关键字的冲突未评估；Append 魔法 = 原创、影响未分析。无照搬 C#（C# 无 `With` 语句）——这是唯一的干净处 | 已检查 | 三件来源混杂于一份文档，成分标注混乱 |

## 设计原则对照

- **与 VB 基因：主体一致，`&=` 偏离。** `.Me`/命名 `With` 延续低仪式、消除样板、继承 VB6 遗产（原则 #2/#5/#9）；`&=` 违反"不引入第二种做事方式"（#3）与"避免隐蔽语义变化"（#7）。
- **与主线关系：Anthony 独立延伸。** vblang 主线对 `With` 语句块增强无活动（2014–2018 会议笔记无该主题）；2016-05-06 "Auto With the return variable" 反提案被 Table 待用户反馈（"Let's table this until we get more user/dogfooding feedback. We'll soon know whether this is essential or not."）。主线态度保守、等证据；本建议方向与"盘活 VB6 资产"一致但无主线背书。相关主线痕迹：2014-02-17 `With`/`From` 同用（"Approved, but low priority. VB-specific."）与 `!` 默认属性访问讨论。
- **破坏性变更：有（极小、退化但真实）。** `With x = expr`（相等式）现有代码被重解析为命名声明，需消歧规则 + `langversion` 门控；`.Me` 与裸 `&=` 为纯新增，无破坏。文档未分析。

## 总评

- **达成程度：部分达成。** `.Me` 概念与价值成立；命名 `With` 是成立但降级的糖；`&=` 未达成且被替代方案覆盖。安全性论证、兼容性分析、三件范围分离均未完成。
- **LDM 三态建议：Consider（拆分落地）** — `.Me` Active（文法零改动、能力唯一、风险低）；命名 `With` Consider（真实但纯糖，文法消歧需确认，优先级次之）；`&=` Reject（价值被链式覆盖、与求值契约冲突、Option Strict 分叉无解）。
- **主要问题**：① 三独立特性捆绑于一份文档（弱提案红旗）；② `&=` 语义未定型且动机可被 `.Append` 链覆盖；③ `With x = expr` 重解析破坏未分析；④ 未决问题即核心语义，效果证据封顶；⑤ 与主线保守立场（等用户反馈）的张力未讨论。

## 返工建议

- **补充章节**：文法（BNF：`With` 语句消歧规则 `identifier =`）；Compatibility（`With x = expr` 重解析、`langversion` 门控）；Option Strict/Infer 分叉对齐表；与求值一次/值类型不写回语义的交互声明；嵌套 `With` 外层目标访问规则（命名外层）。状态行链接替换真实原型分支。
- **补充证据**：最小原型（`.Me` 伪成员解析 + 嵌套绑定）；语义模型 `GetSymbolInfo(.Me)` 与 IDE 补全/InfoTip 验证；`With` 语句使用率与 ADO 样板占比数据。
- **未决问题处理**：`.Me` = 最近封闭目标；命名变量 = 只读别名（禁重绑定）、与 `Dim` 同轨；`&=` 归档否决并附 `.Append` 链式替代证据。
- **设计探索**：命名 `With` 与 `.Me` 是否需同时提供，还是 `.Me` 先行观察；`With x As T = expr` 的 `As` 子句 v1 后是否需要；将 `.Me` 的伪符号设计与 IDE 团队的接口契约单独成节。

---

## 附录：C# 生态与互操作考量

> 本附录对照 dotnet/csharplang 官方仓库（镜像 `..\..\csharplang`）的 C# 现实方向，评估本提案（With 增强：命名 `With` 变量 / `.Me` 伪成员 / `&=`）在 C#/CLR/.NET 生态中的位置。引用纪律：C# 原文逐字转引并标注来源；无法核实处标 **OPEN QUESTIONS**。结论先行：本提案是纯 VB 语法糖，与 C# 生态的**语义对照**（尤其 C# `with` 表达式的复制语义）远比**互操作契约**重要——它不产生新元数据、不触碰 AOT/trimming 边界。

### 相关 C# 现实方向

C# 生态与本提案主题最相关的是**同名而不同义**的 `with` 表达式（C# 9 record `with`、C# 10 record struct `with`）及其赖以成立的**复制语义**与 `init` accessor，另含构造期一次性设置成员的**对象初始化器**惯用法。

**1. C# record `with` 表达式（C# 9）——复制后修改、非破坏性变异。** 来源：`proposals\csharp-9.0\records.md`（`with` expression 节）。原文：

> A `with` expression allows for "non-destructive mutation", designed to produce a copy of the receiver expression with modifications in assignments in the `member_initializer_list`.

> A `with` expression is not permitted as a statement.

> A valid `with` expression has a receiver with a non-void type. The receiver type must be a record.

> First, receiver's "clone" method (specified above) is invoked and its result is converted to the receiver's type. Then, each `member_initializer` is processed the same way as an assignment to a field or property access of the result of the conversion.

配套复制机制（同文件 Copy and Clone members 节）：

> The purpose of the copy constructor is to copy the state from the parameter to the new instance being created.

> A synthesized public parameterless instance "clone" method with a compiler-reserved name

**2. C# 10 把 `with` 放宽到 struct——先复制再改。** 来源：`proposals\csharp-10.0\record-structs.md`（Allow `with` expression on structs 节）。原文：

> It is now valid for the receiver in a `with` expression to have a struct type.

> For a receiver with struct type, the receiver is first copied, then each `member_initializer` is processed the same way as an assignment to a field or property access of the result of the conversion. Assignments are processed in lexical order.

**3. `member_initializer` 只能做字段/属性赋值**（records.md）：`which must be an accessible instance field or property of the receiver's type`。即 C# `with` 只能「改属性/字段」，不能调用方法、不能做任意成员操作——比 VB `With` 块的表达能力窄，且 C# `with` 的接收者必须是 record（C# 9）或 struct（C# 10），**不适用于任意类**；VB `With` 对任意类型生效。

**4. `init` accessor 划定「构造期」可写窗口。** 来源：`proposals\csharp-9.0\init.md`。原文：

> The `init` accessor makes immutable objects more flexible by allowing the caller to mutate the members during the act of construction.

> An instance property containing an `init` accessor is considered settable in the following circumstances, except when in a local function or lambda:
>
> - During an object initializer
> - During a `with` expression initializer
> - Inside an instance constructor of the containing or derived type, on `this` or `base`
> - Inside the `init` accessor of any property, on `this` or `base`
> - Inside attribute usages with named parameters

> The times above in which the `init` accessors are settable are collectively referred to in this document as the construction phase of the object.

对象初始化器（`new P() { X = 1, Y = 2 }`）是 C# 侧「一次性设置多个成员」的惯用法，但**只覆盖构造期**；对既有对象，C# 没有块级简写，只能逐属性赋值。

**5. C# 未来方向与块内伪成员的空白（索引 T8）。** C# 15/16 主线是 unsafe evolution、unions/closed hierarchies、extensions；extensions（C# 14 已落地）扩展的是「对既有类型的成员访问」，与本提案的「块内伪成员」概念距离远。在 `..\..\csharplang\meetings\2025` 与 `meetings\2026` 中 grep `with expression` **无命中**——C# 近期没有在「`with`/块内引用目标自身」上做新的语言投入（见 OPEN QUESTIONS）。

### 现实 vs 提案

按三件拆开判定（兼容 / 冲突 / 需桥接 / 脱节）：

| 提案件 | 判定 | 理由 |
|---|---|---|
| `.Me` 伪成员 | **脱节（VB 特色，非障碍）** | C# 无 `With` 块 ⇒ 不存在「块内引用目标自身」的问题域；C# `with` 把接收者求值一次后隐式作为各 `member_initializer` 的基底，但初始化器内无法引用接收者本身。`.Me` 无 C# 对应语法、无对应元数据——纯 VB 侧编译期糖，不产生跨语言契约。 |
| 命名 `With` 变量 | **兼容** | 与 C# record `with` 的「给接收者一个局部名并引用之」在读写模式上可互译（`var p2 = p1 with { X = 5 };` vs `With p1 : .X = 5 : t = p1`）；两侧都是「把目标对象在局部命名」。差异仅在风格：C# 复制变异、VB 就地变异。 |
| `&=`（目标自身复合赋值） | **冲突（且已被本会议否决）** | 与 C# `with` 复制语义相反。C# 生态把「改一个对象」明确设计成两条路：**复制**（`with`，非破坏性、产生新值）或**方法调用**（`.Append(...)`，副作用），不存在「对既有左值静默重写的运算符魔法」。本提案曾设想的 `&=`（写回需重求值左值 / 译成 `Append`）恰好踩在 C# 明确回避的地带上。 |

**关键对照：VB `With` 的「不写回」与 C# `with` 的「不写回」同构，但声明程度相反。**

- C# `with` 对值类型「先复制再改」，「原变量不变」是**被设计且有用**的行为——结果作为新值返回。
- VB `With someStruct : .X = 5`「求值一次存入临时、写临时、不写回」——**同构的不写回**，但在 VB 里是**未声明的陷阱**（值类型目标静默丢失修改，正是本会议第 2 节点名的经典 VB 陷阱）。
- 本提案 RESOLUTION 第 5 条「不改变 `With` 求值一次、值类型不写回」在生态上等于：保持 VB 的**隐式**不写回，与 C# 的**显式**不写回行为同构、文档地位相反。若未来有人主张给 `With` 加「写回」，C# 生态给不出可借鉴先例——C# 靠复制返回值解决，VB 靠 `ByRef`/`Return` 解决，两条路都不靠「对 `With` 左值重写」。这从生态侧**额外佐证了 `&=` 的否决**。

**C# 无 `With` 语句，是生态空白而非竞争。** 对既有对象的连续变异，C# 只能重复成员赋值；对象初始化器只覆盖构造期。VBScript.NET 面向 COM/ADO 既有对象的 `With` 场景（`CreateParameter` 样板正是典型），在 C# 侧没有对应物——这是 VB 可保留的差异化资产，与索引「C# 语言层面对 COM 新增投入少、重心转向 source-gen 互操作」（T4）的判断方向一致。

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：`.Me` 与命名 `With` 都是编译期别名，不引入任何新的晚期绑定点；`.Me` 静态类型 = 目标静态类型，仅当目标为 `Object` 时才走既有晚期绑定通道——符合「默认安全、按需动态」双模路线，也呼应索引 T7「动态/晚期绑定在 C# 生态被边缘化、被 AOT 视为负担」的现实。另据 unsafe-evolution 的 VB 小节（原文「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`），VB 无 unsafe/指针通道，编译期语法糖是唯一安全出口——`.Me`/命名变量正属此类。
- **source-gen 桥**：`With` 块是纯编译期展开，可被 source generator / 脚本编译器降级为普通成员赋值序列，无反射、无动态代码生成，天然兼容 AOT/trimming 方向（索引 T5/T6）。
- **识别新元数据（唯一实际需桥接点）**：当 VBScript.NET 消费 C# record 类型时，需识别 record 合成成员（copy constructor、编译器保留名的 `clone` 方法、`PrintMembers`、`Deconstruct`）以正确显示成员表与补全；更直接的是 **record 的 `init`-only 属性**——VB `With r : .X = 5` 对 `init` 属性应报「只读/构造期外不可写」类错误，错误信息应点明 C# record 的 `init` 语义，否则 VB 用户会困惑「为何 `With` 里不能改」。建议在编译器错误文案与文档里把 C# record `init` 与 VB 只读属性统一呈现。
- **不需要新 CLR 支持**：`.Me`/命名变量零新元数据、零运行时改动，与会议第 11 节「运行时/CLR 硬约束」结论一致。

### 对既有 RESOLUTION / 三态判定的影响

- **无实质影响，且生态侧部分印证原裁决。** `.Me`（Active）与命名 `With`（Consider）均为纯 VB 语法糖，与 C# 生态零契约冲突。
- **`&=`（Reject）获得生态侧佐证**：C# `with` 的「复制而非写回」设计表明，主流 .NET 语言对「批量改一个对象」给出的答案是复制返回值或方法调用，而非「对既有左值静默重写的运算符魔法」；本提案否决 `&=` 的三理由（链式覆盖、求值契约冲突、Option Strict 分叉）之外，可补第四条生态论据。
- **归档建议**：在 spec 的 Alternatives 中补充「C# record `with` 的非破坏性变异作为对照语料」，记录 VB `With`（就地变异）与 C# `with`（复制变异）的语义分界，防止未来提案把两者混为一谈（两者仅同名，语义镜像）。

### 引用纪律与 OPEN QUESTIONS

- 逐字引用已核实（直接读源文件）：`proposals\csharp-9.0\records.md`、`proposals\csharp-10.0\record-structs.md`、`proposals\csharp-9.0\init.md`；unsafe-evolution VB 小节引用来自索引第四节核实清单（`proposals\unsafe-evolution.md`）。
- **OPEN QUESTIONS**：C# 是否在 2025–2026 推进「`with` 扩展到任意类型 / 接口 / 非 record」？本镜像 `meetings\2025` 与 `meetings\2026` grep `with expression` 无命中，无法确认该方向是否活跃——如涉及需到 dotnet/csharplang 在线仓库进一步核实。
- **Suspect**：无。本附录未使用未经核实的 C# 原文。
