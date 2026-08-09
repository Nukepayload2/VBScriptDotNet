# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本次审议初始化器增强建议——它一口气打包了五件互不相干的事：嵌套元素无匹配 `Add` 时调用 `New`、嵌套成员/集合初始化器、`With` 与 `From` 组合、`With` 内 `!` 字典访问、查询式集合初始化器。上一轮 `With` 增强会议给我们上了一课：捆绑件必须拆开独立评估。这一次我们照做了，结论同样不是齐头并进——五件的命运各不相同，从 Active 到 Table 都有。

## Agenda

* [Proposal: 初始化器增强（Initializer Enhancements）](#proposal-初始化器增强)

## Proposal: 初始化器增强

_Related: `meetings/2014/LDM-2014-02-17.md` – #29 `With`/`From` 同用（Approved, but low priority. VB-specific.）、#26 对象初始化器内委托与嵌套集合初始化器缺口、#44 字典成员初始化器；`meetings/2014/LDM-2014-10-01.md` – 初始化器构造期语义与 ByRef copy-back；Anthony 原文第 9 章 "LINQ Enhancements"（`..\AnthonyDesign_wordpress.txt` L1589–1630，位于 IUD 表达式与 In/NotIn 之间）；ModVB：`proposal-range-expressions.md`、`proposal-query-comprehensions.md`、`proposal-in-notin-operators.md`、`meeting-with-enhancements.md`_

### 场景与缺口

We started from a straightforward observation：今天的对象/集合初始化器填复杂对象要写的样板太多。一个节假表、一个字典、一个带子对象和子集合的 UI 对象，要么逐条 `Add`，要么逐字段赋值，要么 `Enumerable.Range(...).Select(...).ToHashSet()` 链式调用。我们逐条核对了建议里的五个缺口，并在编译器源码里逐一确认——**其中四个是"今天报错"，一个是"今天能写但很啰嗦"**：

```vb
' 缺口 1：嵌套元素无匹配 Add。今天 `Add(2026, 1, 1)` 无匹配重载 → 报错。
Let usFederalHolidays =
      New List(Of DateOnly) From {
            {2026, 1, 1}, {2026, 1, 19}, {2026, 2, 16}, {2026, 5, 25},
            {2026, 6, 19}, {2026, 7, 3}, {2026, 9, 7}, {2026, 10, 12},
            {2026, 11, 11}, {2026, 11, 26}, {2026, 12, 24}, {2026, 12, 25}
          }

' 缺口 2：嵌套成员/集合初始化器。今天 field initializer 只收 `.Name = expr`。
? New MessageBoxFrom With {
        .Caption = "Select one",
        .AcceptButton With { .Text = "Proceed" },   ' 报错：期望 '='（ERR_ExpectedQualifiedNameInInit）
        .Choices From { "Option 1", "Option 2", "Option 3" }   ' 报错：同上
      }

' 缺口 3：With 与 From 组合。今天两种顺序都报
' ERR_CantCombineInitializers（"An Object Initializer and a Collection
' Initializer cannot be combined in the same initialization."）。
? New List(Of Object) With { .Capacity = 8 } From { ... }

' 缺口 4：With 内 ! 字典访问。今天无前导点的 field initializer 报错。
? New Dictionary(Of String, Integer) With {
        !One = 1, !Two = 2, !Three = 3
      }

' 缺口 5：查询式集合初始化器。今天 `From` 后面只许 `{`。
Let digits = New HashSet(Of Integer) From n In 0 To 9
```

其中缺口 3 与缺口 4 不是我们的发明——主线 2014-02-17 会议上就记录过。缺口 3（#29 "Allow With and From together"）是 **Approved, but low priority. VB-specific.**，且明确写了设计顺序："With" comes first and "From" comes next；缺口 4（#44 "Dictionary member initializers"）的 RESOLUTION 是 "We like 1, 2 and 4"，选项 2 正是 `!name = "hello"`。十二年过去这两件都没落地——本仓库的 Roslyn 编译器里 `ERR_CantCombineInitializers` 还在，field initializer 的文法仍然只收前导点加标识符。We see a clear legacy here：主线**批准过**但我们从未实现。

缺口 2 也有主线指纹：#26 的 NOTE 里明确写 "C# lets you use assignment for collection-initializers: `var x = new Order { .Items = {1,2,3} }` … We can't do this."——这是一个被记录在案的 lacuna。

### 候选方案

我们把建议原文拆成五件，各自独立评估（下文用 A–E 编号，与原文顺序一致）：

**PROPOSAL A — 无匹配 `Add` 时嵌套元素调用 `New`。** 当集合初始化器里一个嵌套 `{...}`（在 VB 里等于"一次 `Add` 调用的实参表"）无法解析到任何 `Add` 重载时，回退为"用这些实参 `New` 一个元素再 `Add`"。即 `{2026,1,1}` 在无 `Add(Int32,Int32,Int32)` 时变成 `Add(New DateOnly(2026,1,1))`。

**PROPOSAL B — 嵌套成员与集合初始化器。** 放宽 field initializer：`.Member With { ... }` 对成员再做一次对象初始化，`.Member From { ... }` 对集合成员用 `From` 填充。不同成员按类型各取所需。

**PROPOSAL C — 组合 `With` 与 `From`。** 同一初始化器先 `With { ... }` 设集合自身属性，再 `From { ... }` 填元素。顺序按主线 #29 的设计：`With` 先、`From` 后。

**PROPOSAL D — `With` 内字典访问 `!Key = value`。** 在对象初始化器里用 `!` 运算符对目标自身做键写：`!One = 1` 即 `dict("One") = 1`。复用既有的 `!` 字典访问语法。

**PROPOSAL E — 查询式集合初始化器。** `From n In 0 To 9` 以查询式把范围/查询结果直接作为集合初始化内容。与 `proposal-range-expressions.md`（`0 To 9` 字面量）和 `proposal-query-comprehensions.md`（查询理解家族）共享设计面。

### 权衡：Q&A

- **C 是不是五件里最干净的？** 是。它只要求移除一个我们亲手写下的错误（`ERR_CantCombineInitializers`），并让 `With {...}` 之后的 `From {...}` 被真正解析，而不是当作 trailing 错误吞掉。主线上已批准、方向明确、实现面最小。唯一的减分是**它的增量价值比表面小**——`New List(Of Object)(8) From { ... }`（构造实参 + `From`，今天已支持）已经覆盖了"设容量 + 填元素"这个最典型用例。`With` 组合只对"构造函数收不进去的属性"才有净增量。但既然主线批准过、实现几乎免费，我们没理由不落地。
- **D 是否要顺带把 `!` 全局后移成"默认属性访问"？** 不要。2014 年 #44 的选项 4 想过把 `!` 后移成处处"默认属性访问"，还带出 `!x=3` 与 `!(x)=3` 意义分叉的麻烦。建议原文只要求初始化器内的窄范围，我们照窄做——不重开全局后移的辩论。`!` 复用是 VB 词汇的正确再利用，但 XML 字典访问（`x!name`）是同一运算符的另一张脸，必须靠**目标类型**在绑定期消歧。
- **B 的自动 `New` 是最大的设计分叉。** 建议原文把它列为未决问题（"嵌套 `With` 的目标成员为 `Nothing` 时是否自动 `New`"）。C# 的语义是"取成员值，再对其做初始化"——成员为 `Nothing` 时运行期 NRE，**不自动 New**。自动 New 听上去很 VB（低仪式、宽容），但它引入三态语义：取 getter → 判空 → New → 回写 setter。属性 getter 每次返回新实例、只读集合成员、值类型成员……这些角案例会把一个甜语法变成语义泥潭。We 的倾向：v1 明确**不自动 New**，跟随 C# 的 get-then-initialize；自动 New 单独 Table，等有数据再说。
- **A 是"值得做但太难"还是"不值得做"？** 两者之间——价值真实（节假日表、配置数据这类字面量集合是真实场景），但它是五件里唯一**改变既有重载解析失败语义**的启发式，也是唯一与"默认跟随 C#"正面相悖的（C# 要求显式 `new(...)`）。它必须配"仅当 `Add` 完全失败时启用"规则，否则 `{2026,1,1}` 在同时存在三参 `Add` 与三参构造函数时会改变绑定。这跟上一轮 `TypeOf` 收窄的"启用型"规则是同一哲学：只让先前失败的代码变可编译，不动先前成功的绑定。
- **E 是不是最"沙盒"的一件？** 是。它同时依赖 `0 To 9` 范围字面量（range-expressions 建议，同组未落地）和查询绑定器，还把 `From` 的文法从"必须跟 `{`"扩到"可以跟查询式"——`From n In 0 To 9` 的 lookahead 消歧不难（`{` 与标识符 token 不相交），但它与既有 `From` 的视觉重载是真实的认知负担。更关键的是：**它最大的用例今天用构造函数实参一行就写完了**——`New HashSet(Of Integer)(Enumerable.Range(0, 10))`。E 是"锦上添花"里最远的那朵花。
- **五件捆绑在一份文档里，是不是红旗？** 是。上一轮 `With` 增强会议我们已经把"三件捆绑"定为红旗并拆开；这次是五件。任何一件的排期都不该被其余四件的争议拖住。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**C 无歧义。** `With {...}` 之后出现 `From`，当前代码路径是把 `From` 当 contextual keyword 吞掉并报 `ERR_CantCombineInitializers`（`Parser.vb` L2326–2337）。把它变成合法解析 = 纯 enabling，不重解析任何既存有效代码。**D 有文法新增。** field initializer 文法现在是 `{Key? "." IdentifierOrKeyword "="}? Expression`（`Parser.vb` L2438），无前导点直接 `ERR_ExpectedQualifiedNameInInit`（L2652）。要新增 `"!" <Name> "=" <Expression>` 与 `"." <Name> "="` 并列。`!` 后的名字是**键数据**不是标识符绑定——所以关键字、空格、XML 名字（`!first-name`）能否作键，必须给规则（见 OPEN QUESTIONS）。**B 有文法新增。** field initializer 的值域要从 `Expression` 扩到 `Expression | "With" "{" ... "}" | "From" "{" ... "}"`，并在 `.Member` 之后用 lookahead（`=` vs `With`/`From`）消歧。嵌套深度不限：`.Outer With { .Inner With { ... } }` 必须成立，因为嵌套对象图天然多层。**E 有文法消歧。** `From` 后是 `{`（集合）还是 `<identifier> In`（查询式）——`{` 与标识符 token 不相交，parser 一眼消歧；但 `New X From n In 0 To 9` 里 `n In 0 To 9` 是查询子句，必须复用查询绑定器，这决定了 E 与 query-comprehensions 建议共享文法实现。

#### 2. 角案例与边界语义

**A：元素类型从哪来？** `List(Of DateOnly)` 的元素类型是泛型实参；非泛型集合（`ArrayList`）没有可发现的元素类型 → 无回退。**Add 部分匹配怎么办？** `Add` 有可选参数、`ParamArray`、"几乎匹配"时是"失败"还是"成功"？规则必须钉死：**仅当 `Add` 重载解析整体失败（无候选可用或全部不可用）时才尝试 `New`**；`Add` 走通就绝不回退。**更深嵌套 `{{{...}}}`**：目前嵌套一层已把内层元素当 `Add` 实参表（`Binder_ObjectInitializer.vb` `BindCollectionInitializerElement` L951–1007 把内层 `{...}` 的元素逐项绑成实参），回退逻辑必须与这个"内层花括号 = 一次 Add 实参表"的 VB 专属语义严格对齐——这正是建议的"无匹配 `Add`"缺口与 VB 既有约定一致的地方，也是它比 C# 的嵌套集合初始化语义更简单的地方。

**B：get-then-initialize 的成员必须是可写左值吗？** `.Choices From { ... }` 若 `Choices` 是 `ReadOnly` 属性、由构造函数预置的 `List`，C# 语义下"取成员再初始化"完全成立（不写回 setter，只 `Add` 到已取得的实例）。但 `.AcceptButton With { ... }` 对**引用类型成员**的"取再初始化"是原地改对象，不回写 setter——语义模型必须区分"集合成员（Add 到已取实例）"与"对象成员（原地改已取实例）"两种模式。**嵌套初始化器内 omitted-left 绑定作用域**：`New C With { .A With { .B = .X } }` 里的 `.X` 绑定到内层目标（`A`）还是外层目标（`C`）？`ObjectInitializerBinder.TryBindOmittedLeftForMemberAccess`（`Binder_ObjectInitializer.vb` L1011–1037）今天把 omitted-left 绑到接收者；嵌套后必须**绑到最近嵌套目标**，与 `With` 块嵌套时 `.Me` 绑到最近封闭目标（`meeting-with-enhancements` 决议）同一规则。

**C：求值顺序。** 主线 #29 已定："With" comes first and "From" comes next——先设 `Capacity` 再填元素。这要求 `With { ... }` 的属性赋值先于 `From { ... }` 的 `Add` 调用，与声明顺序一致。**D：`!` 后名字的键值语义。** `!One = 1` 的 `One` 是裸标识符形式的字符串键；`!Me` 这种与关键字同形的键需要规则（`!` 名不是符号绑定，理论上不受保留字限制，但 parser 得接受 `!Class`）。**E：`From n In 0 To 9` 的求值。** 查询结果逐元素 `Add`；`HashSet` 天然去重、顺序无关，但 `List` 保序——同一语法对有序/无序集合的语义要写清楚。

#### 3. 作用域与绑定

- **A**：嵌套 `{2026,1,1}` 的语义模型应报告"这是 `Add` 调用"，`New DateOnly(...)` 是编译器合成（`SetWasCompilerGenerated`，与现有 `Add` 调用的 compiler-generated 处理一致）。IDE 悬停应显示 `Add(New DateOnly(2026, 1, 1))`。
- **B**：`.AcceptButton` 绑到成员符号；嵌套初始化器把成员的类型当新目标。语义模型在嵌套初始化器内查询 `GetTypeInfo` 应返回成员类型，而非外层类型。omitted-left（`ObjectInitializerBinder`）绑定最近嵌套目标（见 #2）。
- **C**：组合后是一个 `ObjectCreationExpression` 的单一初始器，`With` 部分生成赋值序列、`From` 部分生成 `Add` 序列，顺序相接。语法树里 `With`/`From` 是同一初始器的两部分还是两个节点，是实现细节但影响 IDE 展示，必须定。
- **D**：`!One` 绑定到 `Item`/Default 索引器，`GetSymbolInfo(!One)` 应返回索引器符号，实参 `"One"` 为字符串常量。XML 轴访问的冲突由目标类型消歧：目标为 `Dictionary(Of String,T)` 或带 `Item(String)` 的类型 → 键写；目标为 `XElement` → 报错（初始化器里 XML 轴无意义）。
- **E**：范围变量 `n` 是查询范围变量，语义模型按查询绑定器报告。

#### 4. 与既有特性的交互

- **构造期语义（2014-10-01）**：VB 初始化器允许引用其他成员、全部在构造期执行。A 的 `New DateOnly(...)` 是构造期普通构造调用，无冲突；但初始化器里若引用 `Me.X` 这类其他成员，A 的回退 `New` 与既有求值顺序的交互要写进 spec。
- **ByRef copy-in/copy-out**：B 的"取成员再初始化"不经过 ByRef；但若成员 getter 有副作用（每次返回新实例），get-then-initialize 会"丢"修改——这正是我们不自动 New 的另一个理由（避免 getter 语义被隐性重放）。
- **集合模式要求**：现有 `From` 要求目标满足 GetEnumerator 模式或实现 `IEnumerable`，且至少一个可访问 `Add`（`Binder_ObjectInitializer.vb` L842–917，`ERR_NotACollection1` / `ERR_NoAddMethod1`）。B 的 `.Choices From {...}` 中 `Choices` 成员同样必须满足——这是既有校验的复用，不是新规则。
- **匿名类型**：`New With { .A = 1 }` 的匿名类型推断不涉及 `!`（匿名类型无索引器）、不涉及嵌套 `With`。D 明确只作用于具名类型。
- **表达式树**：对象/集合初始化器本就不进表达式树，A–E 无表达式树降级问题。
- **与 `With` 语句增强的交互**：嵌套初始化器与 `With` 块共享 omitted-left 概念；`meeting-with-enhancements` 的 `.Me` 伪成员在初始化器里**不引入**（初始化器里 `.Member` 已够用），保持两处规则独立但同源。

#### 5. Breaking change 与兼容性

好消息是：**五件全部是 enabling**——今天全部报错，改了之后变成可编译，不改变任何既存有效代码的绑定。逐件核实：

- **A**：只改变"`Add` 重载解析失败"的路径；`Add` 走通绝不回退 → 无有效代码变化。但"先前报错的代码现在编译"仍是行为变化，需 `langversion` 门控，且**必须写死"Add 优先"**，否则重载/构造函数并存时绑定漂移。
- **B**：`.Member With {...}` / `.Member From {...}` 今天必错（`ERR_ExpectedQualifiedNameInInit` 或缺 `=`）→ 纯新增。
- **C**：`With{...} From{...}` 今天必错（`ERR_CantCombineInitializers`）→ 纯新增。唯一的残余问题：`With` 后跟 `From` 现在被当错误吞掉，改合法后错误信息消失——这是设计目的。
- **D**：无前导点的 field initializer 今天必错 → 纯新增。但 `!` 是既有运算符，**绑定期**必须证明 `!One = 1` 在今天不会走任何既有路径（parser 已确认：无点即报错，无歧义）。
- **E**：`From` 后跟查询式今天必错 → 纯新增。

无 `Shadows`/重载重解析类风险（这五件都不触碰成员查找规则本身，只有 A 触碰 Add 重载解析的**失败路径**）。

#### 6. Option Strict / 编译选项分叉

- **A**：`Add` 在 `Option Strict Off` 下对 `Object` 目标走晚期绑定。回退 `New` 必须**只对早期绑定集合**启用；晚期绑定目标（`Object` 集合）无元素类型 → 不回退。两路径必须一致：Strict On 启用回退、Strict Off 不改变任何既有晚期绑定行为。
- **D**：`!` 在 Strict Off 下对 `Object` 目标是晚期绑定默认属性访问。初始化器内 `New Object With { !Key = ... }` v1 **不允许**——`!` 键写只在具名类型上早期绑定。两条路径一致。
- **B / C / E**：无 Strict 分叉（纯文法 + 既有绑定规则的复用）。

#### 7. IDE / IntelliSense

- **A**：嵌套 `{2026,1,1}` 悬停/补全要显示合成 `Add(New DateOnly(...))`；当 `Add` 与 `New` 都失败时报错文案要同时给出两个原因（建议 Drawbacks 自认的"到底哪个失败"问题，IDE 是主要的解释者）。
- **B**：嵌套初始化器内补全显示成员类型的成员；`InfoTip` 应区分"对象成员（原地改）"与"集合成员（Add）"。
- **C**：组合初始化器的 `With`/`From` 两部分在语法树里要可分别导航；`From` 部分的 `Add` 调用在语义模型中可见。
- **D**：`!` 之后不弹键补全（键是数据不是符号）；InfoTip 显示"索引器赋值，键 'One'"。
- 这些都在原型里验证，"不验证等于没设计"。

#### 8. 数据 / 普遍性

`With`/`From`/`!` 组合填字典、填复杂对象图，这类字面量集合场景在业务代码里真实存在——但没有占比数据。主线 2014 年对 #29 的判语是 **low priority**，十二年没落地也没见社区回潮，这应当让我们对"高优先级"保持克制。`List(Of DateOnly)` 节假日表这类场景**真实但窄**；字典初始化（D）是其中最普遍的。`Suspect`：五件的真实受益人群以"构造配置型对象图"的开发者为主，而这正是 VBScript.NET 想服务的业务开发者——但"数十万安静客户"里多少人会被这五件打动，我们无法量化。

#### 9. 更简替代

- **A**：逐元素显式 `New DateOnly(...)`；或把节假日表抽成 `GetUsFederalHolidays()` 辅助函数返回 `List(Of DateOnly)`。后者其实更符合"数据即配置"的常规做法。
- **B**：**今天就能写的显式形式**——`.AcceptButton = New Button With { .Text = "Proceed" }`、`.Choices = New List(Of String) From { "Option 1", ... }` 全部合法。B 的全部价值 = 省略 `= New X`/`= New List(...) From` 这一段仪式。价值真实但克制。
- **C**：`New List(Of Object)(8) From { ... }`（构造实参 + From，今天已支持）覆盖"设容量 + 填元素"；`With` 组合只对构造参数收不进去的属性有净增量。
- **D**：逐条 `dict("One") = 1` 语句，或 `.Add("One", 1)`。三键字典怎么写都不费劲——D 的价值在键多的字面量。
- **E**：`New HashSet(Of Integer)(Enumerable.Range(0, 10))` 一行零新语法。E 被构造函数实参形式完全覆盖主要用例。

#### 10. 成本 / 优先级

- **C**：移除错误 + 解析 `From`-after-`With`。成本最低、主线背书最硬（#29 Approved）。**最高优先级。**
- **D**：文法新增 + 索引器绑定 + XML 消歧。成本中低。主线 #44 背书（"We like 1, 2 and 4"）。**次之。**
- **B**：文法新增 + omitted-left 嵌套作用域 + get-then-initialize 语义。成本中。设计分叉（自动 New）未决。**再次之。**
- **A**：绑定回退 + 元素类型发现 + "Add 优先"规则。成本中。语义风险最高。**降级。**
- **E**：查询绑定器集成 + 依赖 range-expressions / query-comprehensions。成本高、被构造函数实参覆盖。**搁置。**

#### 11. 运行时 / CLR 硬约束

无新约束。A 的 `New DateOnly(...)` 是普通构造调用 IL，B/C/D/E 的赋值与 `Add` 是既有 IL 形态。不触达 PEVerify，无 CLR 存储规则问题，无表达式树问题（初始化器不进表达式树）。CLR 不是约束。

#### 12. 值不值得做

- **C**：价值（主线批准、补一个真 lacuna）中；成本极低；风险无。**值得，立刻做。**
- **D**：价值（字典字面量，VB 词汇复用）中；成本中低；风险低（XML 消歧可解）。**值得，配套 spec。**
- **B**：价值（省略 `= New` 仪式、补 #26 记录的缺口）中；成本中；风险中（自动 New 分叉，v1 不做则风险降）。**值得，但不急。**
- **A**：价值（节假日表这类字面量集合）真实但窄；成本中；风险中高（隐式构造、偏离 C#）。**"值得做但语义危险"，先原型后决定。**
- **E**：价值被构造函数实参覆盖；成本高；风险中（`From` 视觉重载 + 依赖未落地建议）。**不值得现在做。**

### VB 基因对照

- **消除常见样板（原则 #9）**：A、B、D 正中靶心——把"逐条 `Add`/`= New X`/`dict("x") = y`"压成字面量。这是五件共有的最亮点。
- **不引入"第二种做事方式"（原则 #3）**：A 引入"隐式 New"作为显式 `New` 的第二方式；E 引入"查询式填充"作为 `From {}` 与构造实参的第三方式；B 的 `.X With {}` 是显式 `= New` 的第二方式。这三件都踩线。C 只是移除限制、D 是复用既有 `!` 运算符——这两件零新增方式。
- **避免隐蔽语义变化（原则 #7）**：A 是最大违例苗头——`{2026,1,1}` 从"报错"变成"悄悄构造"。靠"Add 优先 + 仅失败启用"规则对冲；不写这条规则，A 就是又一个 `Return?`。B 的自动 New 若上马同理。
- **读起来像英语、对新手友好（原则 #5）**：`New List(Of DateOnly) From { {2026,1,1}, ... }` 与 `New HashSet(Of Integer) From n In 0 To 9` 读起来都通顺——"从 n 在 0 到 9 里"。这是 A/E 最动人的地方，也是最危险的糖。
- **保持 VB-like（原则 #2）**：`!`、`With`、`From`、嵌套花括号=Add 实参表，全是 VB 词汇与既有约定。五件里没有外来语法——问题都在语义层面不在词形。
- **与主线关系（对照表 2.3）**：C 是**主线一致**（2014 #29 已 Approved，直接采用）；D 是**主线一致方向**（#44 讨论过、决议倾向明确，主线未落地）；B 是主线记录缺口的细化（#26 NOTE）；A 与 E 是 **Anthony 独立延伸**——A 偏离"默认跟随 C#"（C# 要求显式 new），E 是 Anthony 的 LINQ 统一愿景。这与"主线保守、Anthony 激进"的根本张力一致。

### RESOLUTION:

1. **拆分本建议**：五件独立评估、独立排期。捆绑（PROPOSAL A–E 一起上）会拖累最干净的 C。这是五件捆绑的弱提案红旗，照 `meeting-with-enhancements` 的拆分先例处理。
2. **C — 组合 `With` 与 `From`：采纳（Active）。** 按主线 2014-02-17 #29 已批准的方向实现："With" 先、"From" 后。实现 = 移除 `ERR_CantCombineInitializers` + 让 `With {...}` 后的 `From {...}` 合法解析。纯 enabling；`langversion` 门控 + Compatibility 章节。
3. **D — `With` 内 `!Key = value` 字典访问：原则采纳（Consider）。** 与 #44 决议（"We like 1, 2 and 4"）一致；只做初始化器内窄范围，**不**把 `!` 全局后移为默认属性访问；v1 限具名类型（`Dictionary(Of String, T)` 及带 `Item(String)`/Default 索引器者）；键文法（关键字/空格/XML 名）与 `!`-XML 消歧写入 spec 后落地。
4. **B — 嵌套成员与集合初始化器：原则采纳（Consider，优先级次之）。** 解决 #26 记录的 lacuna；文法 = field initializer 值域扩到 `With {...}`/`From {...}`；**v1 不自动 New**——跟随 C# 的 get-then-initialize（成员 `Nothing` → 运行期异常，不隐式构造）；嵌套初始化器内 omitted-left 绑定到**最近嵌套目标**。自动 New 单独 Table。
5. **A — 嵌套元素无匹配 `Add` 时调用 `New`：Table。** 价值真实但语义危险。必须配三条规则才可再议：`Add` 优先（走通绝不回退）、仅当 `Add` 重载解析整体失败时启用、只对早期绑定集合启用。与"默认跟随 C#"相悖需在 spec 里正面论证。列为原型验证项。
6. **E — 查询式集合初始化器：Table。** 依赖 `proposal-range-expressions`（`0 To 9`）与查询绑定器；主要用例被 `New HashSet(Of Integer)(Enumerable.Range(0, 10))` 覆盖；等 range-expressions 落地、与 query-comprehensions 团队对表后再议。
7. **不改变既有初始化器任何语义**：构造期执行、ByRef copy-back、集合模式要求（GetEnumerator/IEnumerable + `Add`）、omitted-left 绑定，全部原样保留。

### Implication:

- 撰写最小原型：**C**（移除 `ERR_CantCombineInitializers` + 解析 `From`-after-`With`）；验证语义模型与 IDE 对组合初始化器的展示。
- 起草 speclet：**D** 的 `!` 键文法与索引器绑定（含 XML 消歧）；**B** 的 field-initializer 文法、get-then-initialize 语义、omitted-left 最近嵌套绑定。
- 补 Compatibility 分析：五件全部 enabling 的证据表 + `langversion` 门控 + 警告策略（尤其 A 若启用时的"隐式构造"警告）。
- 与 range-expressions / query-comprehensions 团队对表（E 的依赖）；与 `meeting-with-enhancements` 对表（嵌套初始化器 omitted-left 绑定与 `With` 块 `.Me` 绑定同规则）。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：D 的 `!` 键名文法——关键字（`!Class`）、含空格、XML 名字（`!first-name`）是否可作键；`!(expr)` 形式要不要（2014 #44 选项 4 的 `!x=3` vs `!(x)=3` 分叉）。
- `OPEN QUESTIONS`：B 的自动 `New`（成员 `Nothing` 时）v1 后是否按需补上；若补，需先答"getter 重放 vs 缓存"。
- `OPEN QUESTIONS`：A 的元素类型发现（非泛型集合）、`Add` 部分匹配（可选参数/`ParamArray`）判定、Strict Off 下的边界。
- `OPEN QUESTIONS`：E 支持的查询子句范围（`Where`/`Select`/`Order By`）——等 range/query 建议落地后一起定。
- `TODO`：量化"字面量集合初始化"在真实代码库的占比，为普遍性补证据（尤其 D 的字典场景）。
- `Follow-up`：把 C 与 2014 #29、D 与 #44 的渊源写进各自 proposal 的 Related 区，让主线背书可审计。

### 状态

- **LDM 状态：Active（C）/ Consider（D、B）/ Table（A、E）。**
- **三态判定：Consider（拆分落地）** — 最干净、主线背书的 C 先行；D 配套 spec 紧随；B 设计分叉收敛后跟进；A、E 搁置待信号。

---

## 附录：特性评价

# 建议评价报告：proposal-initializer-enhancements.md

## 评价对象

- 建议：proposal-initializer-enhancements.md — 初始化器增强（嵌套元素 `New` 回退 / 嵌套成员与集合初始化器 / `With`+`From` 组合 / `!` 字典访问 / 查询式集合初始化器）
- 来源：Anthony 原文第 9 章 "LINQ Enhancements"（`..\AnthonyDesign_wordpress.txt` L1589–1630，位于 IUD 表达式 L1573–1587 与 In/NotIn L1632–1636 之间）。**提案文档本身未标注章节来源。**
- 配方目标：用最小语法消除复杂对象/集合构造的样板（逐条 `Add`、逐字段赋值、`= New` 仪式），并支持查询式/字典式字面量填充

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 的样板痛点真实、五段示例均可操作；但 5 个未决问题里有 4 个是核心语义（`New` 匹配规则、`!` 适用范围、查询子句范围、求值顺序、自动 New）⇒ 效果封顶 3–4；且 C 的子效果被 `(8) From {}` 覆盖、E 的子效果被构造实参形式覆盖，关键子效果消退 | 已检查（状态行占位，无原型/运行） | 无原型封顶 4；A/E 声称的效果与既有替代（构造函数实参、`= New`）重复；文档未量化任何场景 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`!`/`With`/`From`/嵌套花括号=Add 实参表均为 VB 词汇与既有约定（VB 化改造）；但 A（隐式构造，偏离 C# 显式 `new`）与 E（查询式填充，第三种方式）是打包的次要/激进能力；五件捆绑边界模糊 | 已检查 | A/E 违反"不引入第二种做事方式"（原则 #3）与"默认跟随 C#"（#4）未识别；B 的自动 New 是未定形的语义突变 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节模板齐全、示例与 Anthony 第 9 章逐字一致；但缺文法（BNF）、Compatibility/breaking 分析、Option Strict 分叉；5 个未决问题具体诚实但多个即核心语义（依标准应封顶效果而非扣品质）；状态行为占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；`Hashset` 大小写不符 `HashSet`（VB 大小写不敏感可绑定，属笔误级） | 已检查 | 一份文档捆绑五独立特性（弱提案红旗）；未标注来源章节；未与 2014-10-01 初始化器语义（构造期、ByRef copy-back）交互 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/水/光正（字面量提速、VB 差异化、复用 `!`/`With`/`From` 资产）；风受损（A 隐式构造、E 查询式扩张语义面，需与 range/query 建议保持一致）；暗（A 的"报错→代码"语义变化 + 重载/构造歧义；E 依赖未落地建议） | 已检查（预测待定） | 实际影响须"已采纳"后定；文档对 A/E 的风险只在 Drawbacks 各提一句，未给对冲设计 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源 = Anthony 第 9 章未标注；继承 VB 的 `!` 字典访问、`With`/`From`、嵌套花括号约定未点明；借鉴主线 #29（`With`/`From` 顺序）与 #44（`!` 选项 2）未标注——这是最大的标注缺失，因为 C/D 恰好有主线背书；B 的嵌套集合初始化思路有 C# `Order { .Items = {1,2,3} }` 先例（#26 NOTE 明示）未标注；E 为原创 | 已检查 | 主线渊源（#29 Approved / #44 RESOLUTION）未认领，是"光"资产（主线背书）被浪费；无闭源等杂质 |

## 设计原则对照

- **与 VB 基因：C、D 一致；B 主体一致（自动 New 待定）；A、E 偏离。** C 纯移除限制、D 复用 `!` 运算符，零新增做事方式（原则 #3）、无隐蔽语义变化（#7）；B 的 get-then-initialize 是 VB 词汇延续，但自动 New 若上马则踩 #7；A 引入隐式构造（踩 #4 默认跟随 C#、#7 隐蔽语义变化），靠"Add 优先 + 仅失败启用"对冲；E 引入第三种填充方式（踩 #3），且依赖未落地的 range/query 建议。
- **与主线关系：C = 主线一致（2014 #29 已 Approved，ModVB 直接采用）；D = 主线一致方向（#44 "We like 1, 2 and 4"，主线未落地）；B = 主线记录缺口的细化（#26 NOTE "We can't do this"）；A、E = Anthony 独立延伸（A 偏离 C#，E 为 LINQ 统一愿景）。** 与 `proposal-range-expressions.md`（E 依赖）、`proposal-query-comprehensions.md`（E 共享查询绑定器）、`meeting-with-enhancements.md`（嵌套 omitted-left 绑定同源）交互。
- **破坏性变更：无（全部 enabling）。** 五件今天全部报错，改后变为可编译，不改变任何既存有效代码绑定；A 必须钉死"Add 优先 + 仅失败启用"，C 需 `langversion` 门控。文档未做此分析。

## 总评

- **达成程度：部分达成。** C、D 概念与主线渊源成立、范围可缩、风险低；B 成立但自动 New 分叉未收敛；A 价值真实但语义危险未对冲；E 被既有替代覆盖且依赖未落地建议。安全性论证、兼容性分析、五件范围分离均未完成。
- **LDM 三态建议：Consider（拆分落地）** — C Active（主线批准、移除一个既有错误、纯 enabling）；D Consider（#44 背书、`!` 复用、需 spec）；B Consider（#26 缺口、get-then-initialize 语义、自动 New Table）；A Table（原型验证 "Add 优先" 规则后再议）；E Table（等 range/query 落地）。
- **主要问题**：① 五独立特性捆绑于一份文档（弱提案红旗）；② A 的隐式构造启发式与"默认跟随 C#"正面相悖、且会改变既有重载解析失败语义，未给对冲；③ E 被 `New HashSet(Of Integer)(Enumerable.Range(0, 10))` 一行覆盖，且依赖未落地建议；④ 缺文法/Compatibility/Option Strict 分析；⑤ 未标注 Anthony 章节来源与主线 #29/#44 渊源（"光"资产浪费）。

## 返工建议

- **补充章节**：文法（BNF：field initializer 的 `!`/`With`/`From` 值域扩展、`From` 的 `{` vs `<identifier> In` lookahead 消歧）；Compatibility（五件全部 enabling 的证据表 + `langversion` 门控 + A 的警告策略）；Option Strict 分叉（A/D 的早期绑定限定）；与 2014-10-01 初始化器语义（构造期执行、ByRef copy-back、omitted-left 绑定）的交互声明。
- **补充证据**：最小原型（先做 C 移除 `ERR_CantCombineInitializers`）；D 的 `!` 索引器绑定 + XML 消歧验证；A 的"Add 优先 + 仅失败启用"原型与歧义样例；E 的查询绑定器集成预估。
- **未决问题处理**：C 求值顺序 = 主线 #29 已定（With 先、From 后）；B 自动 New = v1 不做（Table），omitted-left = 最近嵌套目标；D 键文法 = 收合法标识符 + 决定 `!(expr)`；A 匹配规则 = 仅早期绑定集合 + Add 整体失败时启用；E 查询子句 = 随 range/query 建议。
- **设计探索**：拆分后的五件各自独立成文；为 C/D 补主线渊源（Related 区引 #29/#44）；与 range-expressions、query-comprehensions 团队对表后重议 E；将 A 的"仅失败启用"规则与 `meeting-typeof-flow-analysis` 的"启用型"绑定规则对照（同一哲学，可共用 spec 语言）。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang-index.md`（dotnet/csharplang 镜像）与镜像内原文核对 C# 现实方向。引用均逐字，来源路径为 csharplang 内相对路径。先说明关系强弱：**与低层 CLR/内存互操作关系弱**（会议 §11 已确认本提案不触达 PEVerify、无 CLR 存储规则、无新运行时约束——A–E 全部是普通构造/赋值/`Add` IL 形态）；**与本提案同域的 C# 初始化器生态关系强**——C# 3 对象初始化器、C# 6 字典初始化器、C# 9 record `with` 表达式、C# 12 collection expressions、以及 2025–2026 仍在推进的初始化器工作（见下），构成 VBScript.NET 必须对齐的"C# 现实"。

### 一、相关 C# 现实方向

**1. C# 3 对象/集合初始化器基线（2007）**
C# 3 起 `{ ... }` 是对象初始化器的统一容器：member initializer 与集合初始化器在包裹层互斥，但**成员值可以是嵌套集合初始化器**——`new Order { Items = { 1, 2, 3 } }`（VB 主线 #26 NOTE 引的正是这个写法，见本会议正文）。这是本提案 **B** 的对应物。C# 6（2015）追加 Dictionary initializer（`Language-Version-History.md` C# 6 段："Dictionary initializer"），用索引器 member initializer 写键值——本提案 **D** 的对应物。

**2. C# 12 collection expressions——target-typed 字面量哲学**
C# 12 落地 `[1, 2, 3]`，是本提案 **A/E** 面对的"同痛点的另一条 C# 答案"：
- "Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values. Inlining other collections into these values is possible using a spread element `..e` like so: `[e1, ..c2, e2, ..c2]`." → `proposals\csharp-12.0\collection-expressions.md`（Summary）
- "Collection literals are target-typed." → 同文件（Detailed design）
- "provides a uniform and efficient way of creating collections using collection-like types (`List<int> list = [1, 2, 3];`)" → `Language-Version-History.md`（C# 12.0 段）
- 动机点名集合初始化器的缺陷（正是本提案想消除的样板）："Collection initializers, which require syntax like `new List<T>` (lacking inference of a possibly verbose `T`) prior to their values, and which can cause multiple reallocations of memory because they use N `.Add` invocations without supplying an initial capacity." → 同文件（Motivation）
- 对"无 `Add` 的集合类型"，C# 用 Builder 出口（**桥接点**）："A *create method* is indicated with a `[CollectionBuilder(...)]` attribute on the *collection type*. The attribute specifies the *builder type* and *method name* of a method to be invoked to construct an instance of the collection type." → 同文件（Create methods）

**3. C# 13 继续扩展初始化器成员形态**
- "allows indexers in object initializers to use implicit Index/Range indexers (`new C { [^1] = 2 }`)." → `Language-Version-History.md`（C# 13.0 段，Implicit indexer access in object initializers）

**4. C# 9 with 表达式（record，与 VB `With` 词形撞车但语义不同）**
- "A `with` expression allows for "non-destructive mutation", designed to produce a copy of the receiver expression with modifications in assignments in the `member_initializer_list`." → `proposals\csharp-9.0\records.md`（§`with` expression）
- "A valid `with` expression has a receiver with a non-void type. The receiver type must be a record." → 同文件同节

**5. C# 2025–2026 仍在推进的同域工作（working proposals，未落地）**
- **mixed object and collection initializers**（champion #10185）：解除"一个 `{ ... }` 只能是对象或集合之一"的限制——与 VB 的 `ERR_CantCombineInitializers` 是同一限制的 C# 侧。原文："Today, a `{ ... }` initializer following `new T(...)` must be **either** an object initializer (only `Member = value` / `[args] = value`) **or** a collection initializer (only expressions that bind to `Add` calls) — never a mix of both. This proposal relaxes that restriction." → `proposals\mixed-object-and-collection-initializers.md`（Summary）；其兼容性结论："This is a pure extension." → 同文件（Back-compat analysis）；还把它对"任意交错 vs 分组"列为 Open LDM question。
- **dictionary expressions**（champion #8659）："Dictionary Expressions are a continuation of the C# 12 Collection Expressions feature. They extend that system with a new terse syntax, `["mads": 21, "dustin": 22]`, for creating common dictionary values." → `proposals\dictionary-expressions.md`（Summary）
- **compound assignment in object initializer and `with`**（champion #9896）：把 `+=`（事件挂接）等复合赋值引入 member initializer，使声明式 UI 对象在单一表达式内完成装配 → `proposals\compound-assignment-in-initializer-and-with.md`（Summary）
- **immediately enumerated collection expressions**（champion #9754）：探索"无 target 类型"的 collection expressions（`foreach (bool b in [true, false])`）→ `proposals\immediately-enumerated-collection-expressions.md`（Summary）

### 二、现实 vs 提案（A–E 判定）

| 提案 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| **C**（`With`+`From` 组合） | C# #10185 正解除同一"对象/集合初始化器互斥"限制 | **兼容 / 同向** | C# 今天仍与 VB 一样不允许顶层混用（这是 #10185 的 Motivation），C# 正在解除且判为 pure extension——VB 的 C 不是孤例，是与 C# 当前工作平行的本地实现。C# 版更激进（讨论任意交错），VB 版保守（`With` 先 `From` 后，主线 #29 顺序），方向不冲突。 |
| **D**（`!Key = value` 字典访问） | C# 6 Dictionary initializer + C# 13 索引器初始化 + future dictionary expressions | **兼容 / 同向** | C# 自 2015 起持续证明"初始化器里写键值"是稳定需求。VB `!` 走 `Item(String)` 索引器，与 C# 索引器 member initializer 底层同机制（索引器 store）；语法是 VB 词汇复用，效果等价。 |
| **B**（嵌套成员/集合初始化器） | C# 3 即支持 `new Order { Items = {1,2,3} }`；get-then-initialize | **兼容 / 追赶** | VB 落后 C# 15+ 年（#26 lacuna）。v1"不自动 New、跟随 get-then-initialize"与 C# 语义一致 → 跨语言行为一致，互操作友好。C# 侧 compound-assignment 正在扩展 member initializer 形态，说明该语法面仍在生长。 |
| **A**（无匹配 `Add` 时隐式 `New`） | C# 12 collection expressions 用 target-typed 字面量解决"免写 new"；对无 `Add` 类型用 CreateMethod；**不隐式构造元素** | **冲突 / 需桥接** | C# 的答案不是"隐式 `New` 回退"，而是"target-typed 显式构造"。A 的启发式与 C# 哲学正面相悖（C# 要求显式 `new(...)`）；A 想解决的"无匹配 `Add`"在 C# 生态已被 collection expressions 的 CreateMethod 以另一条路覆盖。A 需承认是 VB 特色而非追赶 C#。 |
| **E**（查询式集合初始化器） | 无对应；C# 用 LINQ + collection expressions spread（`[..range]`）；#9754 探索无 target 类型 | **脱节 / 独立** | E 是纯 VB 特色（Anthony LINQ 愿景），C# 无 query-initializer 形式。C# 的"从序列构造集合"答案是 spread + LINQ，语法与哲学均不同。 |

**需桥接的元数据点：`CollectionBuilderAttribute`（CreateMethod）。**
C# 12 为"无 `Add` 的集合类型"提供 Builder 出口（上文引文）。`ImmutableArray`/`FrozenDictionary` 等类型将依赖此属性。VBScript.NET 若用 `From {}`/collection 语义消费这类类型，**必须识别该属性**（把"无 `Add`"路由到 CreateMethod），否则只能对"有 `Add`"类型工作——这是本提案域最具体的一个"识别新元数据才能互操作"落点（决策文件 M4/M8 精神的实例）。

**与表达式树的不对称（需桥接 / OPEN QUESTIONS）。**
会议正文 §4 称"对象/集合初始化器本就不进表达式树"——这是 VB 现状。C# 对象初始化器有表达式树编码（`MemberInitExpression`），C# 库接受 `Expression<Func<T>>` 参数时可用初始化器构造；VB 无法把初始化器喂给表达式树。若 .vbx 面向消费 `Expression` 的 C# API（LINQ-to-Entities 类、断言/查询库），这是真实互操作不对称。→ OPEN QUESTIONS（C# 侧确切编码需在 dotnet/csharpstandard 核实后给出精确引用）。

**词汇撞车提示（不构成互操作障碍）。**
C# 9 record 的 `with { member_initializer_list }` 与 VB 初始化器的 `With { ... }` 词形相同但语义不同：C# 是"非破坏性突变"、receiver 必须是 record、走 clone 方法；VB 是"构造期初始化"。两者都是编译期语法，生成的 IL（构造+赋值 vs clone+赋值）无元数据冲突；但 C# 程序员读 VB `With` 时可能误以为 clone 语义——文档应明示 VB `With` 的构造语义。

### 三、对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：A–E 全部是编译期静态特性（构造调用 + 属性 setter + `Add` 调用 + 索引器 store），无反射、无晚期绑定 → 与 NativeAOT/trimming 天然兼容（索引 T5/T6）。建议 .vbx 默认把初始化器编译为直接 IL；仅 `Option Strict Off` 的 `Object` 目标保留晚期绑定（D 的 v1 已限具名类型、A 的回退只对早期绑定集合启用——与 C# 的"静态优先"一致）。
2. **source-gen 桥**：若 .vbx 走解释执行（M5 scripting-interpreted），初始化器语义需在解释器实现；建议用 source-gen 把字面量初始化编译为直接构造代码（与 C# collection expressions "编译器尽量优化分配"的哲学一致），interpreted 模式留作显式 opt-in 的传统兼容层。
3. **识别新元数据**：至少识别 `CollectionBuilderAttribute`（CreateMethod），让 VB 的 `From {}` 能消费 C# 生态的 Builder 集合类型；否则 `ImmutableArray` 等类型在 VB 侧不可用。
4. **追齐 C# 初始化器面**：C/D/B 都是"追赶/同向"C#——把它们当作与 C# 初始化器生态对齐的兼容层（.vbx 用户写 C# 类型时用 VB 语法获得与 C# 等价的构造能力），而非差异化卖点；差异化卖点在 A/E（Anthony 特色），但 A 必须正面回应"与 target-typed 哲学相悖"（可同时探索 target-typed 字面量的 VB 化路线，而非仅隐式 `New` 回退）。

### 四、对既有 RESOLUTION/三态判定的影响

本附录**不修改**正文 RESOLUTION 1–7（那些是 VB 内部设计决策）。C# 生态证据的作用是**强化/佐证**既有判定：

- **C（Active）**：C# #10185 正在解除同一限制且判为 pure extension——"主线批准（#29）+ C# 同向"双重背书，Active 进一步坐实。
- **D（Consider）**：C# 6/13 索引器初始化 + dictionary expressions 证明"初始化器内写键值"是稳定生态需求；VB `!` 语法差异不改变结论。
- **B（Consider）**：C# 3 已有对应能力，VB 是追赶，风险面小；v1"不自动 New"跟随 C# 语义正确。
- **A（Table）**：C# target-typed 哲学进一步佐证"隐式构造"偏离生态共识；A 若要复活，建议同时评估 target-typed 字面量路线。
- **E（Table）**：C# 无对应且用 spread/LINQ 覆盖同一痛点，生态独特性不强；保持 Table。

### 五、引用纪律与 OPEN QUESTIONS

- 上列 C# 原文均**逐字**核自 csharplang 镜像；来源路径见各引文后的 `→`。未从镜像外引入未核实引用。
- `OPEN QUESTIONS`：C# 对象初始化器在表达式树中的确切编码（`MemberInitExpression`）与 VB"初始化器不进表达式树"的精确差异，需在 dotnet/csharpstandard 核实后引用。
- `OPEN QUESTIONS`：C# mixed-object-and-collection-initializers 若最终允许任意交错，是否影响 VB C 的"`With` 先 `From` 后"顺序设计（当前判定：不冲突，但值得追踪该提案的 LDM 决议）。
- `OPEN QUESTIONS`：`CollectionBuilderAttribute` 在 dotnet/runtime 中的实际采用范围（哪些 BCL 类型已加该属性），决定 .vbx 识别它的优先级。
