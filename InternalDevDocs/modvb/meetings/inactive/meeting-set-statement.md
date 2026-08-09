# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们把 `Set` 赋值语句建议（`proposal-set-statement.md`，Anthony 原文第 3.2 节 "Assignment"）摆上桌。它名义上是一个特性，实际捆绑了三件几乎互不相干的事：复合赋值的显式前缀、逗号分隔的多重赋值、以及"对已存在变量的元组解构赋值"。We 花了整场会议把它们拆开讨论——因为三者的价值、成本与风险完全不同，捆在一起谁也说服不了谁。拆完之后 We 的结论比开场时想象的更收敛。

## Agenda

* [Proposal: Set 赋值语句](#proposal-set-赋值语句)

## Proposal: Set 赋值语句

_Related: 主线 2016 元组会议（解构与多 lvalue 赋值）；[vblang #337 – Pattern Matching](https://github.com/dotnet/vblang/issues/337)；[vblang #197 – Inferred `Set` Parameter Type](https://github.com/dotnet/vblang/issues/197)；[vblang #190 – `Try` assignment](https://github.com/dotnet/vblang/issues/190)；ModVB：`proposal-local-declarations`（`Let` 声明解构）、`proposal-null-literal`（`Null` 字面量）、`proposal-with-enhancements`（`With` 复合赋值）_

### 场景与缺口

We started from a historical observation that has been around since VB6：`Set` 曾是语言中最高频的语句之一——对象引用赋值必须 `Set`，`Set obj = Nothing` 释放引用，VBScript 里 `Set fso = CreateObject(...)`。VB.NET 把语句级 `Set` 移除了，只把它保留为属性访问器关键字：

```vb
' VB6 / VBScript 时代：
Set fso = CreateObject("Scripting.FileSystemObject")   ' 对象引用赋值必须 Set
Set obj = Nothing                                       ' 释放引用

' VB.NET 现状：Set 只在 Property 块内作为访问器出现。
Property Title As String
    Get
        Return _title
    End Get
    Set(value As String)
        _title = value
    End Set
End Property
```

Anthony 的建议（原文 3.2 节，三行示例）试图把 `Set` 请回来，作为**语句级赋值前缀**，一次解决三件事：

```vb
' Anthony 原文 3.2，逐字：
Set obj.Position += acceleration

Set left = Null,
    right = Null

Set suit, rank = GetNextCard()
```

对应三个目标：
1. **显式复合赋值前缀**：`Set obj.Position += acceleration`，让编译器"不再需要猜测这是复合赋值还是表达式"；
2. **逗号分隔多重赋值**：一条语句把 `left`、`right` 同时置 `Null`；
3. **元组解构赋值**：把 `GetNextCard()` 的元组两元素赋给**已存在**的变量 `suit`、`rank`。

We agree there is a genuine gap in the third item——主线 2016 元组会议就讨论过"把元组解构到多个 lvalue"，当时给了 `(x, y) = GetPoint()` 的语法并标注 *"Probably. Though it will be the first time a statement in VB can begin with anything other than a keyword, an identifier, or a number."*。这个缺口是真实存在的。但前两项的缺口存疑，见下文。

### 候选方案

**PROPOSAL A — 照建议原文整体落地。** `Set` 作为通用赋值语句前缀，支持复合赋值、逗号多目标、解构赋值三种形态。

**PROPOSAL B — 只留解构赋值，砍掉复合前缀与逗号多重赋值。** `Set suit, rank = GetNextCard()` 与声明式 `Let suit, rank = GetCard()`（`proposal-local-declarations`）构成"声明/赋值"配对；复合赋值维持现状；多重赋值维持 `a = Nothing : b = Nothing`。

**PROPOSAL C — 不引入 `Set` 关键字，走主线 2016 元组会议的括号方向。** 解构赋值用 `(suit, rank) = GetNextCard()`；多重赋值与复合前缀均不做。这是"默认跟随 C#"的选项（C# 7 也是 `(a, b) = GetPoint()`）。

**PROPOSAL D — 严格复刻 VB6 语义。** `Set` 只用于**对象引用**赋值（`Set obj = Nothing`、`Set obj = newThing`），数值/值类型赋值仍走 `=`。因 VB.NET 里 `obj = Nothing` 已经做同样的事，We 开场就判它纯仪式，仅留档。

### 权衡：Q&A

- **复合赋值前缀（A 的第 1 件）真解决了什么？** We 反复回访建议的 Motivation，其核心主张是"对属性写复合赋值（`obj.Position += acceleration`）会被编译器拒绝——因为复合赋值需要读改写且隐式触发属性 Get/Set"。**We Suspect 这个主张对可读写属性不成立**：VB 的复合赋值运算符本就对可读写属性做读改写，`obj.Position += acceleration` 编译为"读 Position、加、写回"，这与 `obj.Position = obj.Position + acceleration` 等价且今天即可编译。真正被拒绝的是**只读属性**（没有 Setter，读改写无从写回）——但 `Set` 前缀同样救不了只读属性，因为你依然要写回。所以前缀既没有解锁新场景，也没有消除歧义——**歧义根本不存在**。这一项的 Motiviation 是虚的，`Probably` 源于把"复合赋值在某些类型上没有运算符（如 `StringBuilder` 的 `&=`）"或"只读属性"与"属性复合赋值"混为一谈。这一结论必须用编译器验证后才能定案，见 OPEN QUESTIONS。
- **`Set` 与 `Let` 的配对自洽吗？** `Let suit, rank = GetCard()`（声明）与 `Set suit, rank = GetNextCard()`（赋值）是一对，读起来像英语命令，We like 这个直觉。但注意：VB6 的 `Set` 只对**对象引用**赋值，`Let` 才是数值赋值；Anthony 把 `Set` 改造成"通用赋值"，语义与 VB6 并不一致——这是"继承 VB6 的皮、换 VB6 的骨"。且 `Let` 本身在主线无对应（2.3 对照表：`Let` 替换 `Dim` 是 Anthony 独立延伸），所以这对关键字是 ModVB 独有的体系，主线没有锚点。
- **逗号双义是不是坏味道？** 这是 We 整场最警惕的点。同一语句族里逗号有两种含义：

  ```vb
  Set left = Null, right = Null    ' 逗号 = "还有下一个赋值目标"（每项自带 =）
  Set suit, rank = GetNextCard()   ' 逗号 = "这是解构目标列表"（共享末尾一个 =）
  ```

  可解析（是否带 `=` 可区分），但"同一个字符在相邻语句里扮演两种语义角色"正是设计原则所警惕的"细微字符改变语义"。2018 模式匹配会议在 `Case` 逗号上撞过同一堵墙——`f(Matches 1 To 10, 47)` 里"无法分清 47 是模式还是第二个实参"，当时的结论是"逗号保持 `Case` 专属，不进模式语法"。We 在这里得出类似结论：逗号多重赋值和解构共用逗号，是给同一语句族埋了两套读法。
- **A vs C：`Set` 前缀 vs 括号解构。** 2016 会议已经为括号形式开了路：*"Yes, but they need parenthesis. Resolves some ambiguities"*，并且括号形式是"默认跟随 C#"的形态。Anthony 的 `Set` 前缀有一个真实优势：它解决了 2016 会议自己点名的尴尬——"statement 以 `(` 开头，VB 历史上从未有过"。`Set suit, rank = ...` 让语句回到"以关键字开头"，We 认可这是对主线方案的有效修正。但代价是关键字复用（见下条）与一套新语句文法。`Probably`：这个权衡值得为解构子特性单独立案再打一次，不值得在捆绑建议里草率通过。
- **`Set` 关键字的语境冲突有多重？** 三层：① 属性访问器 `Set(value As Integer)` 与语句 `Set left = Null` 在同一 Property 块内可同时出现——解析靠"`Set` 后是否紧跟 `(`"区分，可区分但读代码的人会困惑；② 主线 2017 年刚把访问器 `Set` 的**参数推断**（#197）Approved-in-Principle，理由正是"访问器 `Set` 的参数列表本就可省略、IDE 不生成即可"——主线正在让 `Set` 访问器更隐身，ModVB 却在语句层把它复活，两个方向相撞；③ 保留关键字问题：`Set` 已是 VB 保留关键字，`Dim [Set] As Integer` 才能把它当标识符，所以**引入语句级 `Set` 不破坏既有代码**（无 hard breaking），但 `[Set]` 转义标识符的语境重解析需要过一遍。结论：不是硬破坏，是概念负担。
- **多重赋值值不值？** `Set left = Null, right = Null` 的替代是 `left = Nothing : right = Nothing`（冒号分隔）或声明式 `Dim left = Nothing, right = Nothing`。We don't see 它比现状更 VB、更短或更清晰。若 `Null` 字面量（`proposal-null-literal`）落地，`Null` 对值类型永不表示默认值——`Set left = Null` 在 `left` 为值类型时**直接是编译错误**，这个示例甚至绑定了另一个争议特性。值：边际。
- **解构赋值（B/C 的核心）与模式匹配的关系。** 2018 模式匹配会议把"Declaration pattern"定义为 *"type match with assignment equivalent"*，并说 *"The most compelling case for patterns is TypeCheck/assignment"*。解构赋值是"assignment without type match"——它不需要类型测试，只要把 RHS 元组按元素赋给既有 lvalue。两者共享 lowering 直觉（把元组按元素拆开），但语义边界清晰：模式匹配管"类型测试 + 绑定新变量"，解构赋值管"把元组拆给既有变量"。We think 它不该等模式匹配，但也**不该抢模式匹配的地盘**——它属于元组/解构基础设施，不属于模式文法。
- **副作用与求值次数。** 建议 Drawbacks 自己承认"复合赋值到属性会对 `obj` 及 Get 结果多次求值"。这恰恰是问题所在：读改写语义里接收者、索引、Get、Set 各求值几次，必须进 spec。`Set GetObj().Position += acceleration` 里 `GetObj()` 求值几次？`Set obj.Items(GetIndex()) += 1` 里 `GetIndex()` 求值几次？如果不进 spec，这个特性就是又一个"隐蔽的语义变化"（原则 #7）。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Set` 语句的启动 token 是关键字，本身无歧义。歧义在内部：
- **逗号双义**（见 Q&A）：`Set a = e1, b = e2` 与 `Set a, b = e3` 是同一语句族的两种逗号含义。需要 BNF 明确"每个目标是否可自带 `=`"，以及"多个 `=` 与单个末尾 `=` 的归属"。
- **`Set(` 与访问器**：Property 块内 `Set(value As Integer)` 是访问器声明；`Set (x) = 1`（若允许括号目标）与访问器撞形。建议原文未给任何文法，`Probably` 需上下文相关判断"`Set` 后跟 `(` 即访问器，跟标识符即语句"。
- **`Set obj.Position += acceleration` 与 `obj.Position += acceleration` 并存**：同一个复合赋值两种写法，新增一种"做事方式"（原则 #3）。

#### 2. 角案例与边界语义

- **解构长度不匹配**：`Set a, b, c = GetPoint()`（RHS 二元组）——编译错误还是运行时 `ArgumentException`？解构语义应复用现有 `Dim (a, b) = ...` 的检查（长度不匹配编译期报错）。
- **值类型与 `Null`**：`Set left = Null` 在 `left As Integer` 时，按 `proposal-null-literal` 语义是编译错误；`left` 为可空值类型 `Integer?` 时合法。这个示例把 `Set` 语句与 `Null` 字面量（另一争议特性）耦合了。
- **复合赋值的求值顺序**：接收者求值一次后暂存？索引表达式求值一次还是两次？Get 结果参与运算后再 Set——若 Set 抛异常，Get 的副作用已发生。今天 `obj.Position += acceleration` 已有既定语义（读改写，接收者/索引各一次）；`Set` 前缀若只是语法糖，必须与既定语义**逐位一致**，不得引入新求值次数。
- **`Set` 到 `ByRef`**：`Set suit, rank = GetNextCard()` 中 `suit` 若是 `ByRef` 参数，元素赋值走 copy-in/copy-out 还是直接写存储位置？与 2016 ByRef Returns 的"我们从不调用 setter"是同一类问题。
- **晚绑定**：Option Strict Off 下 `Set suit, rank = GetNextCard()` 若 `suit` 为 `Object`，解构的元素赋值走晚绑定吗？解构的 RHS 必须是元组吗，还是任意可索引值？

#### 3. 作用域与绑定

- 语义模型里 `Set a = e1, b = e2` 的每个 `=` 返回什么符号？`a` 绑定到局部变量、字段还是属性？解构 `Set suit, rank = GetNextCard()` 中 `suit`、`rank` 绑定到**既有声明**，与 `Let suit, rank = GetCard()`（绑定到**新声明**）必须返回不同符号——`GetSymbolInfo` 要区分"引用既有"与"定义新"。
- `Set obj.Position += acceleration` 的复合赋值绑定到 `Position` 的 Get/Set 访问器对，与今天 `obj.Position += 1` 的绑定**必须一致**——如果 `Set` 前缀改变了绑定，就是破坏。

#### 4. 与既有特性的交互

- **复合赋值今天即可用**：可读写属性、索引器、`With` 块内的 `.Member` 复合赋值都已存在。若 `Set` 前缀不改变行为，它就是纯冗余；若改变行为，就是破坏。
- **`With` 复合赋值**：`proposal-with-enhancements` 已经覆盖了 `With` 块内 `&=` 的场景（如 `StringBuilder` 追加），那里才是复合赋值真正的"缺口"（`builder &= item` 对 `StringBuilder` 今天确实不编译）。`Set` 前缀与 `With` 复合赋值是**两个方案抢同一片地**，We prefer `With` 增强那一片，因为它零新关键字。
- **Lambda / 闭包**：`Set` 语句在 lambda 内对捕获变量解构赋值，与普通赋值同规则，无特殊点。
- **元组转换**：解构赋值的元素转换复用 2016 会议既定规则——*"They are element-wise convertible. The conversion is a widening conversion if and only if each element conversion is a widening conversion"*。

#### 5. Breaking change 与兼容性

- 引入语句级 `Set`：`Set` 已是保留关键字，`[Set]` 转义标识符不受影响 ⇒ **无 hard breaking**。`Probably` 需要走一遍"`[Set]` 作语句开头"的重解析测试。
- **概念性破坏**：把"属性访问器 `Set`"与"赋值语句 `Set`"放在同一门语言里，重编译不出错，但代码含义的**第一遍阅读**会错——这正是语言设计最贵的破坏形式之一。
- 若接受"复合赋值前缀"，且 `obj.Position += acceleration` 今天本就合法，则无旧代码行为变化（纯增量）；但前提是 Motivation 的事实主张经得起验证。

#### 6. Option Strict / 编译选项分叉

- **解构**：Option Strict On 下元素转换必须显式（与 `Dim (a, b) = ...` 一致）；Off 下允许隐式/晚绑定。两路径规则应与现有元组解构**完全一致**，不得分叉。
- **多重赋值**：`Set left = Null, right = Null` 的 `Null` 对 `Object`（Off 下任意类型）可赋值；对值类型在两条路径下都报错（`Null` 字面量语义）。`Set` 语句不得比普通 `=` 更宽松或更严格。

#### 7. IDE / IntelliSense 影响

- `Set` 作语句关键字需要新的分类着色与语句补全；多目标语句中每个目标的绑定、每个 `=` 的符号高亮需要设计。
- 解构赋值 `Set suit, rank = GetNextCard()` 中 `suit`/`rank` 的"转到定义"要跳到既有声明，IDE 要能区分"声明/赋值"两种解构。
- 这些都在原型中验证；不做进规范等于没设计。

#### 8. 数据 / 普遍性

- 解构到既有变量的需求在 2016 元组会议已被主线确认存在（多 lvalue 赋值，*"Probably"*），有真实先例。
- 多重赋 `Null`/`Nothing` 的场景（如批量清理）在业务代码中存在，但频次没有量化数据；且 `left = Nothing : right = Nothing` 已是可行替代。
- 复合赋值前缀没有找到任何真实用户诉求——Motivation 里引用的"被拒绝"场景本身存疑。

#### 9. 更简替代

- 复合前缀：`obj.Position = obj.Position + acceleration` 显式读改写；`With` 复合赋值（`proposal-with-enhancements`）覆盖 `StringBuilder` 型追加。**两者都不需要 `Set` 关键字。**
- 多重赋值：`left = Nothing : right = Nothing`；声明式 `Dim left = Nothing, right = Nothing`。
- 解构赋值：`(suit, rank) = GetNextCard()`（2016 括号方向）；或等模式匹配落地后看 `Case x As T` 声明模式是否顺手覆盖。
- Analyzer/重构可提示"此处可解构"，不能替代语法，但可作为先行者验证需求。

#### 10. 复杂度 / 成本 / 优先级

三件子特性成本完全不同：
- 复合前缀：实现最便宜（语法糖），价值接近零，建议不做。
- 逗号多重赋值：文法 + 绑定改动中量，价值边际。
- 解构赋值：最贵但最真实——需要新语句文法、多目标绑定、IDE 三件套；但它解决的是主线已确认的缺口。
"值得做但太难"的热情不抵消成本；We 不会为一个三合一捆绑建议批预算。

#### 11. 运行时 / CLR 硬约束

无硬约束。`Set` 语句是纯编译期语法糖：复合前缀下放为读改写 IL（临时变量暂存接收者）；解构下放为元素逐个赋值；多重赋值下放为顺序赋值。不触达 CLR 存储规则，PEVerify 无碍。唯一要盯的是求值次数的 IL 形态（接收者/索引只算一次）。

#### 12. 值不值得做

- 价值：解构赋值真实（主线已确认缺口）；多重赋值边际；复合前缀接近零。
- 成本：三合一整体高；拆出解构子特性后中。
- 风险：`Set` 关键字复用（概念负担）、逗号双义（原则 #7）、Motivation 事实存疑（品质风险）。
**值得做——但只做解构赋值那一小块，而且必须先与括号形式、模式匹配对表。**

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：直接违反。VB 已有 `=` 赋值语句、`(a, b) =` 解构、`Dim a = 1, b = 2` 声明，再加 `Set` 前缀是第三套赋值语法。除非解构子特性证明 `Set` 是"替代括号"而非"叠加括号"，否则门槛过不去。
- **避免隐蔽的控制流/语义变化（原则 #7）**：逗号双义是典型"细微字符改变语义"；复合赋值若与现状求值次数不一致也是。此条对复合前缀与逗号多重赋值是致命的。
- **保持 VB-like / 读起来像英语（原则 #2/#5）**：`Set suit, rank = GetNextCard()` 读起来像命令，确实有 VB6 味道；`Set left = Null, right = Null` 读起来像声明不像赋值，味道存疑。
- **冗长只在有用时是美德（原则 #10）**：`Set` 前缀对复合赋值是无用冗长；对解构赋值是"避免 `(` 开头"的合理代价。同一关键字，两半截然不同的评价。
- **永不破坏现有代码（原则 #1）**：hard breaking 无；概念性破坏有（访问器 `Set` vs 语句 `Set`）。
- **与主线关系（2.3 对照表）**：解构赋值 = 主线 2016 已确认方向的替代语法（"Anthony 独立延伸，方向一致"）；`Let`/`Set` 关键字体系 = 主线无、Anthony 独立延伸；`Null` 字面量依赖 = 主线 2014 拒绝、Anthony 激进。整体与主线**不冲突但不同轨**，比 `TypeOf` 流分析那一次离主线远得多。

### RESOLUTION:

1. **拆分拒绝三合一捆绑**。本建议名义一个特性、实为三个互不相关的语法，We 不接受整体评审。这一条本身也是建议文书的失败——它属于红旗清单"一份提案混杂多个独立特性"。
2. **复合赋值前缀：Rejected**。VB 复合赋值对可读写属性今天即可编译（读改写），`Set` 前缀既不解锁新场景也不消除歧义；Motivation 的核心主张 `Suspect`。真正的复合赋值缺口（`StringBuilder` 型 `&=`）由 `proposal-with-enhancements` 的 `With` 复合赋值接管，零新关键字。
3. **逗号多重赋值：Table**。值边际、逗号双义违反原则 #7、示例还耦合了 `Null` 字面量。若未来有量化场景（批量清理）再说；即便做，也必须与解构的逗号语义显式分开或放弃逗号（如 `: ` 或独立语句）。
4. **解构赋值：Consider，限定条件**。`Set suit, rank = GetNextCard()` 解决的是主线 2016 元组会议确认的真实缺口，且它把语句带回"以关键字开头"，是对括号方案的有效修正。**但**：① 必须与 `(suit, rank) = GetNextCard()`（2016 方向，也是 C# 方向）正式对表，证明 `Set` 优于括号或两者并存的价值；② 必须与 `Let suit, rank = GetCard()`（`proposal-local-declarations`）共用同一套解构 lowering 与求值规则；③ 必须解决 `Set` 关键字与属性访问器、主线 #197（Inferred `Set` Parameter Type）的概念冲突——`Not all of us are happy with` 把一个已用于访问器的保留关键字同时用作语句关键字。
5. **`Set` 关键字语义不得复刻 VB6 的对象引用限定**（PROPOSAL D 否决）：VB.NET 的对象赋值已由 `=` 承担，`Set obj = Nothing` 是纯仪式。若 `Set` 复活，它是通用赋值语句前缀，不是 VB6 语义；这点必须在 spec 里写死，防止用户带着 VB6 心智模型误读。
6. **求值顺序进 spec**：解构/复合/多重三种形态的接收者、索引、Get/Set 求值次数必须与现有复合赋值逐位一致；任何新求值次数都是 Reject。

### Implication:

- 将本建议拆为三份独立文书：`proposal-set-statement` 收缩为"元组解构赋值到既有变量"单一主题；复合前缀并入 `proposal-with-enhancements` 的竞品分析；多重赋值降级为 Table 备注。
- 起草解构赋值的 speclet：BNF（`Set` 语句、目标列表、单一共享 `=`）、与 `Dim (a, b) = ...`/`Let a, b = ...` 的共享 lowering、元素级转换规则、Option Strict 分叉、IDE 语义模型（"引用既有" vs "定义新"）。
- 与 2016 元组会议方向、模式匹配声明模式（`Case x As T`）分别对表，产出"括号解构 vs `Set` 解构 vs 模式声明"三方案对比文档。
- 补一份 Compatibility 分析：`[Set]` 转义重解析、Property 块内 `Set(` 语境、与 #197 的方向冲突逐条列证。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**验证 `obj.Position += acceleration` 今天是否真的被编译器拒绝。** We Suspect 对可读写属性它合法（读改写），若合法则复合前缀的 Motivation 全盘不成立。此验证是整份建议的第一块多米诺。
- `OPEN QUESTIONS`：`Set suit, rank = GetNextCard()` 中 `suit`/`rank` 若不存在（Option Explicit Off 下）是隐式声明还是报错；与 `Let` 声明解构的区分。
- `OPEN QUESTIONS`：解构长度不匹配的报错策略（编译期 vs 运行期）。
- `OPEN QUESTIONS`：`Set` 语句可否与 `Let` 一样进入 `For Each`/`From` 子句（2016 会议曾给 `From (x, y) In GetPoints()`）；若可，文法扩张面失控。
- `TODO`：量化"批量置空/批量赋值"的真实占比，为多重赋值子特性补证据或归档。
- `TODO`：与 `With` 复合赋值团队对表，确认复合赋值缺口的边界（`StringBuilder` 型 `&=` 等）。
- `Follow-up`：若 `proposal-local-declarations` 的 `Let` 声明解构先落地，解构 lowering 的接口契约以它为准。

### 状态

- **LDM 状态：Table（整体）**。三合一捆绑不可接受；复合前缀 Reject；多重赋值 Table；解构赋值 Consider（限定条件未满足前不启动）。
- **三态判定：Table** — 最有价值的那块（解构赋值）被最没价值的两块拖累，且必须先与括号方向、模式匹配对表、并验证 Motivation 事实主张；条件满足后解构子特性可单独升为 Active/Consider。

---

## 附录：特性评价

# 建议评价报告：proposal-set-statement.md

## 评价对象

- 建议：proposal-set-statement.md — `Set` 赋值语句（复合赋值前缀 + 逗号多重赋值 + 元组解构赋值）
- 来源：Anthony 原文第 3.2 节 "Assignment"（`..\AnthonyDesign_wordpress.txt` L831–841，三行示例逐字：`Set obj.Position += acceleration`、`Set left = Null, right = Null`、`Set suit, rank = GetNextCard()`）
- 配方目标：用显式 `Set` 关键字一次支持"属性复合赋值 / 多重赋值 / 元组解构赋值"，消除"复合赋值 vs 表达式"的歧义猜测

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。核心主张（"对属性复合赋值被拒绝"）对可读写属性 `Suspect` 不成立；三件子特性强弱悬殊；解构赋值有真实缺口（主线 2016 确认）但无原型/数据 | 已检查（事实主张证据悬置） | Motivation 可能建立在错误事实上；无量化场景；无原型，效果止于书面且部分可疑 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。把 VB6 对象引用关键字 `Set` 改造成通用赋值前缀（换骨留皮）；三个弱相关能力捆绑；复合赋值部分与 VB 基因无关（现状已工作）；与属性访问器 `Set` 概念冲突 | 已检查 | 捆绑杂质；`Set` 语义与 VB6 不一致且未声明；与主线 #197 方向相撞 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题诚实（健康区间）；但无 BNF、无兼容性/breaking 分析、未认识到复合赋值今天即可工作、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） | 已检查 | 核心事实主张未验证；逗号双义未识别；未与 2016 元组会议对表 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（一致性断裂）"。光小正向（盘活 VB6 资产）；水受损（第三套赋值语法、逗号双义、访问器/语句 `Set` 复用）；雷微小（复合前缀若落地是纯冗余，不加速迭代）；暗风险（与括号解构、`With` 复合赋值、模式匹配竞争地盘）未权衡 | 已检查（预测待定） | 与三个既有/在途特性抢地盘而未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料来源=Anthony 3.2 节，标注准确；但"继承 VB6 `Set`"未标注、与主线 2016 元组会议/`Let` 声明解构的关系未标注、未声明其解构概念映射到 C# 7 解构；`Null` 依赖未点名 | 已检查 | 血缘标注不全；未声明对主线已确认方向的替代关系；无杂质（未混入闭源） |

## 设计原则对照

- **与 VB 基因：偏离为主**。原则 #3（不引入第二种做事方式）违反——第三套赋值语法；原则 #7（避免细微字符改变语义）违反——逗号双义；原则 #10（冗长只在有用时是美德）——复合前缀是无用冗长。原则 #9（消除样板）仅对解构赋值部分成立；原则 #2/#5（读起来像英语）对 `Set suit, rank = ...` 成立。
- **与主线关系：Anthony 独立延伸，且与主线已定方向竞争**——解构赋值与 2016 元组会议的括号解构（也是 C# 方向）正面竞争；`Set` 关键字与主线 #197（Inferred `Set` Parameter Type，Approved-in-Principle）在访问器语义上相撞；`Null` 依赖是主线 2014 拒绝的激进项。不同于 `TypeOf` 流分析的"主线一致"，此建议离主线较远。
- **破坏性变更：无 hard breaking（`Set` 已是保留关键字），有概念性破坏**——属性访问器 `Set` 与赋值语句 `Set` 的语境混淆、`[Set]` 转义重解析待过一遍。复合前缀若只是语法糖则零行为变化，前提是 Motivation 事实主张成立。

## 总评

- **达成程度：部分达成**——解构赋值子特性价值真实且被主线先例背书；复合前缀动机存疑；多重赋值价值边际。作为整体建议：未达成。
- **LDM 三态建议：Table（整体）**。拆解后：复合赋值前缀 Reject；逗号多重赋值 Table；解构赋值 Consider（限定条件：与括号方向/模式匹配对表、解决 `Set` 关键字冲突、验证 Motivation）。
- **主要问题**：① 三特性捆绑，边界模糊；② Motivation 事实主张 `Suspect`（复合赋值对可读写属性今天即可编译）；③ 逗号双义违反原则 #7；④ `Set` 关键字与属性访问器/主线 #197 冲突；⑤ 未与主线 2016 元组会议括号方向、`With` 复合赋值、模式匹配声明模式任一者对表。

## 返工建议

- **拆建议**：`proposal-set-statement` 收缩为"元组解构赋值到既有变量"单一主题；复合前缀并入 `proposal-with-enhancements` 竞品分析；多重赋值降为 Table 备注。
- **补充章节**：BNF（`Set` 语句文法、逗号目标列表两种形式的消歧规则）；Compatibility / breaking-change（`[Set]` 转义、Property 块内 `Set(` 语境、与 #197 冲突）；求值顺序（接收者/索引/Get 各求值几次）；Option Strict 分叉；解构长度不匹配。
- **补充证据**：最小原型（解构赋值 + 多目标绑定 + IDE 语义模型）；用编译器验证 `obj.Position += acceleration` 的现状行为（OPEN QUESTION，决定 Motivation 生死）；批量置空场景的占比数据。
- **未决问题处理**：逗号双义显式二选一（放弃多重赋值、或放弃逗号改用其它分隔）；`Set` 语义写死为"通用赋值前缀，非 VB6 对象限定"；解构 lowering 以 `Let` 声明解构为先落地的接口契约。
- **设计探索**：产出"括号解构（2016/C#）vs `Set` 解构（Anthony）vs 模式声明（`Case x As T`）"三方案对比；明确解构赋值属元组基础设施、不属于模式文法。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库镜像 `..\..\csharplang` 的 C# interop 浓缩索引）。**先说实话：本提案与 C# interop 的整体关系偏弱**——`Set` 语句是纯编译期语法糖（本会议第 11 节已确认"不触达 CLR 存储规则，PEVerify 无碍"），不产生新元数据、不依赖新 CLR 能力，不存在 M1/M2 那种"必须桥接新元数据"的硬约束。真正值得写的是两点：① C# 把赋值当**表达式**、VB 把赋值当**语句**的语法哲学差异——这是跨语言迁移与工具链互操作最根本的摩擦面；② 解构赋值子特性与 C# 7 元组解构**直接对应**，且有逐字可引的 C# 决策记录。以下引用一律逐字 + 标来源。

### 相关 C# 现实方向

**R1 — 赋值在 C# 是表达式（返回被赋的值），且 C# 仍在给它加新形态。**
- C# 的赋值不是语句而是表达式，其正常语义被 LDM 记为：*"The normal semantics of assignment is that the result is the value of the LHS after assignment."* → `meetings\2016\LDM-2016-07-13.md`（"Void as result of deconstructing assignment?"）。
- 这一方向没有收敛，反而在加码：C# 14 的 null-conditional assignment 把"条件访问 + 赋值"也做进表达式——*"Permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression."* → `proposals\csharp-14.0\null-conditional-assignment.md`（Summary）。复合赋值（`+=` 等）同样在 C# 是表达式，对可读写属性做读改写，无需任何前缀或关键字。
- 对照：本提案把赋值（含复合赋值）重新包装成**关键字前缀语句** `Set ...`，与 C# 把赋值持续表达式化的方向正好相反。

**R2 — C# 元组解构赋值 `(x, y) = ...`，LDM 明确决定"仍是表达式"，否决了"不产生值的赋值语句"方向。**
- C# 7 的解构赋值形态是括号形式（本会议 PROPOSAL C 的方向）：*"(x, y) = currentFrame.Crop(x, y); // x and y are existing variables"* → `meetings\2016\LDM-2016-04-12-22.md`（"Deconstructing assignments"）。
- LDM 曾认真考虑过"做成不产生值的赋值语句"这个 fallback：*"As a fallback we can say that this is a new form of assignment _statement_, which doesn't produce a value."* → 同上。但最终**否决了语句方向**，保持表达式：*"We decided that deconstructing assignment should still be an expression. As a stop gap we said that its type could be void."* → `meetings\2016\LDM-2016-07-13.md`。这让解构赋值能进 `for` 迭代器：`for (... ;; (current, next) = (next, next.Next)) { ... }` → 同上。
- 求值顺序 C# 也定了（breadth-first：先 LHS 各表达式左到右、再 RHS 左到右、再逐元素转换、再逐元素赋值，确保 `(x, y) = (y, x)` 交换成立）→ `meetings\2016\LDM-2016-07-13.md`。

**R3 — C# 解构的跨类型协议是 `Deconstruct` 方法查找——这是真正的互操作接口。**
- C# 的 `(x, y) = e` 不只对元组有效，还按协议查找 `Deconstruct` 方法：*"a method is selected by searching in *type* for accessible declarations of `Deconstruct` and selecting one among them using the same rules as for the deconstruction declaration."* → `proposals\csharp-8.0\patterns.md`（positional pattern）。
- 协议明确允许实例与扩展两种形态：*"_Deconstruction should be specified with an instance (or extension) method_."*，扩展形态使"已有类型可被增补为可解构"——*"it can be specified with an extension method so that existing types can be augmented to be deconstructable outside of their own code."* → `meetings\2016\LDM-2016-05-03-04.md`。方法形状是 out 参数：`public static void Deconstruct(this Name name, out string first, out string last) { ... }` → 同上。
- 含义：VB 若想"把 C# 库里的任意类型解构"，lowering 必须实现**与 C# 相同的协议查找**（实例 + 扩展 `Deconstruct(out ...)`，按 arity 重载），退化到元组——这是本提案解构子特性与 C# 生态**真正需要桥接**的点。

**R4 — dynamic/晚期绑定被 C# 边缘化，unsafe-evolution 甚至把它列入"是否应更 unsafe"的开放问题。**
- unsafe-evolution 的开放问题清单：*"- `dynamic` (probably should match what BCL decides for reflection APIs)"* → `proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"）。
- 本会议解构赋值的晚绑定路径（Option Strict Off 下 `Set suit, rank = GetNextCard()` 若 `suit` 为 `Object` 走晚绑定）与这一现实张力最大。

**R5 — C# 无 `Set` 语句、无逗号多重赋值；`set` 只在属性声明里当访问器关键字。**
- C# 一条语句赋多个目标的唯一形态就是元组解构 `(x, y) = e`（R2）。`Set left = Null, right = Null` 这类"每项自带 `=` 的逗号列表"在 C# 无对应；C# 等价物是两条语句，或 `(left, right) = (null, null)`。
- C# 的 "set" 概念只存在于：属性声明里的 `set` 访问器关键字，以及元数据里 `set_PropertyName` 的 specialname 方法。本提案在语句层复活 `Set`，在 C# 生态里没有可对齐的对象。

### 现实 vs 提案

| 子特性 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| 复合赋值前缀 `Set obj.Position += acceleration` | 复合赋值是表达式，对可读写属性读改写，无前缀 | **脱节** | C# 无对应物；且提案动机（"复合赋值到属性被拒绝"）在 C# 现实下同样不成立——C# 复合赋值对可读写属性正常。C# 还在给赋值表达式加法（R1），方向相反。 |
| 逗号多重赋值 `Set left = Null, right = Null` | 无对应；等价物 = 分句或 `(left, right) = (null, null)` | **无对应 / 需桥接** | 纯语法糖，IL 层零摩擦；C# 无此文法，跨语言迁移时需改写为分句。示例还绑定 `Null` 字面量（本会议已 Table）。 |
| 解构赋值 `Set suit, rank = GetNextCard()` | C# 7 `(x, y) = e`，保持表达式（R2） | **兼容但语法哲学分叉** | 语义/lowering 兼容（逐元素赋值、求值顺序 breadth-first 一致），IL 相同、元数据无摩擦。分叉在表层语法：C# 用括号且是表达式；本提案用 `Set` 前缀且是语句。摩擦点是"赋值表达式 vs 赋值语句"的哲学差异，不是 IL/元数据差异。 |
| （元层）赋值表达式 vs 赋值语句 | C# 持续表达式化（R1/R2），最新证据是 C# 14 null-conditional assignment | **摩擦点** | 跨语言阅读、代码迁移（C#↔VB）、工具链（IDE、代码转换器）都要在"表达式/语句"边界做转换；这比任何单条语法都更根本。 |

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态**：解构赋值的晚绑定路径（Option Strict Off）应保持显式 opt-in，默认走类型化元素赋值——与 C# 现实（R4：dynamic 被边缘化、AOT 敌视反射）对齐，守住 .vbx 脚本"默认可 AOT、可静态推理"的出口。本会议 RESOLUTION 未锁定晚绑定细节，此处是 .vbx 实现应写死的默认。
- **source-gen 桥 / 纯编译期语法糖**：`Set` 三种形态的 lowering 全部下放为现有 IL（stloc/stfld/复合运算），不引入运行时依赖——这是 .vbx 脚本能编译为受管程序集、走 source-gen 生态（索引 T6）的前提。解释执行模式应做成显式兼容层，不承载解构 lowering。
- **识别新元数据：`Deconstruct` 协议**：这是本提案与 C# 生态唯一的硬桥接点。若 .vbx 解构赋值要消费 C# 7+ 库（`KeyValuePair<TKey,TValue>`、positional record、自定义 `Deconstruct` 类型），lowering 必须实现与 C# 相同的协议查找（R3：实例 + 扩展 `Deconstruct(out ...)`，按 arity 重载，退化为元组）。否则 .vbx 脚本无法解构 C# 类型，互操作断裂。
- **属性 setter 识别**：.vbx 把 `obj.Prop = v` 绑定到 C# 属性时，识别元数据 `set_Prop` specialname 是既有标准行为，`Set` 语句不改变它；但 `Set` 若包装复合赋值，其求值顺序（接收者/索引/Get/Set 各几次）必须与 CLI 属性读改写惯例一致——本会议 RESOLUTION #6 已要求，此处与 C# 现实同向，可直接借用 R2 的 breadth-first 模型作参照。

### 对既有 RESOLUTION / 三态判定的影响

- **三态判定不受影响**：Table 的判定依据是 VB 内部设计原则（原则 #3/#7）、主线 VB 2016 元组会议与 #197，C# 现实不推翻任何一条。
- **但给"解构赋值子特性"的对表义务增加一个维度**：RESOLUTION #4 已要求与 `(suit, rank) = ...`（2016 方向、也是 C# 方向）对表；R3 表明还应与 C# 的 `Deconstruct` **协议**对表——因为括号方向在 C# 里附带一整套跨类型解构协议，`Set` 方向若只支持"元组 RHS"而查不了 `Deconstruct`，将在消费 C# 库时劣于括号方向。
- **印证 RESOLUTION #5 的判读**：C# 把解构赋值保持为表达式（R2），而本提案把赋值做成语句——"`Set` 是替代括号而非叠加括号"的主张，面对 C# 现实应更谨慎：`Set` 替代的不只是括号语法，还把解构从"表达式生态"挪进了"语句生态"，失去在 `for` 迭代器、`IIf`/三元式表达式体内使用的可能。这是一个此前对表时未充分展开的损失项。

### 引用纪律 / OPEN QUESTIONS

- **已逐字核实**（来源均在 `..\..\csharplang` 镜像）：
  - 「The normal semantics of assignment is that the result is the value of the LHS after assignment.」→ `meetings\2016\LDM-2016-07-13.md`
  - 「We decided that deconstructing assignment should still be an expression. As a stop gap we said that its type could be void.」→ `meetings\2016\LDM-2016-07-13.md`
  - 「As a fallback we can say that this is a new form of assignment _statement_, which doesn't produce a value.」→ `meetings\2016\LDM-2016-04-12-22.md`
  - 「Permits assignment to occur conditionally within a `a?.b` or `a?[b]` expression.」→ `proposals\csharp-14.0\null-conditional-assignment.md`
  - 「a method is selected by searching in *type* for accessible declarations of `Deconstruct` and selecting one among them using the same rules as for the deconstruction declaration.」→ `proposals\csharp-8.0\patterns.md`
  - 「_Deconstruction should be specified with an instance (or extension) method_.」→ `meetings\2016\LDM-2016-05-03-04.md`
  - 「- `dynamic` (probably should match what BCL decides for reflection APIs)」→ `proposals\unsafe-evolution.md`
- **OPEN QUESTIONS**：
  - C# 规范中"简单赋值表达式的结果是赋给左操作数的值"的**规范级原文**未能从本镜像逐字核实——`spec\` 目录只是链接索引（正文已迁到 dotnet/csharpstandard）。上文"赋值即表达式"的论断以 LDM-2016-07-13 决策记录为据，足够可靠；若要规范级引文，请查 csharpstandard。
  - C# 14 null-conditional assignment 的最终落地状态（库中为提案形态，未查 LDM 批准记录）。
  - `Deconstruct` 协议在 .vbx 具体绑定的边界（是否连扩展形态一起查、与 `GetSymbolInfo` 的"引用既有"语义如何交互）不在本会议范围，建议对表时确认。
