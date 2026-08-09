# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上周我们把 LINQ 查询增强（元组解构 `From x, y In ...`、`Select FirstOrDefault()` 聚合写法、BC36606 修复）收了尾；本周接手同一片领域的下一块：`Insert` / `Update` / `Delete` 表达式。它们是 Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements" 末尾的三段演示，试图把增删改也拉进"声明式、可组合、形似查询"的阵营。我们带着 2014 年表达式序列被拒的旧账和 2018 年 `Return?` 被否的教训重读了它，讨论比预想的要长——因为问题不在语法形态，而在"表达式是否应该承担副作用"这个根基。

## Agenda

* [Proposal: Insert / Update / Delete 表达式（DML Expressions）](#proposal-insert--update--delete-表达式)

## Proposal: Insert / Update / Delete 表达式

_Related: [vblang #104 – Extend `For Each` Statement with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)；[vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)；[vblang #167 – Return?](https://github.com/dotnet/vblang/issues/167)；[vblang #211 – usage of any installed programming language](https://github.com/dotnet/vblang/issues/211)；ModVB：`proposal-query-comprehensions.md`、`proposal-set-statement.md`、`proposal-query-enhancements.md`_

### 场景与缺口

We started from a plain observation:今天把数据写进数据库，VB 开发者只有两套姿势——ORM 的命令式 API（`DbSet.Add` / `Remove` / `SaveChanges`）或手写 SQL 字符串。前者样板在调用点堆叠，后者丢掉了类型安全。Anthony 原文在第 9 章末尾（L1565–1587）给出了第三套姿势——把增删改写成"目标集合 + 匹配条件 + 字段映射"的声明式表达式，形状完全对齐查询理解：

```vb
' Insert expression.
Let data = [
      {"name": "Leo"},
      {"name": "Donny"},
      {"name": "Raph"},
      {"name": "Mikey"}
    ]

Let newStudentIds =
      Insert s In db.Students
        From dto In data
         Set s.Name = dto!name

' Update expression.
? Update
    t In db.Teachers
  Where
    t.Id = teacherId
  Set
    t.EmailAddress = $"{t.FirstName}.{t.LastName}@university.edu"

' Delete expression.
? Delete c In db.Classes Where c.IsDeleted
```

`Delete c In db.Classes Where c.IsDeleted` 读起来像英语，`Insert ... From dto In data Set s.Name = dto!name` 把"批量 DTO 灌入"压进一行声明——这是本建议最亮的资产。对 VBScript.NET 的脚本语境（JSON 数组 → 数据库的批量导入）它尤其诱人，因为 `data` 就是 JSON 字面量数组，`dto!name` 是字典访问。

但缺口也是结构性的：这三段演示**没有定义任何执行语义**。表达式何时真正写库？`newStudentIds` 是惰性序列还是即时执行的结果？多条 DML 是否同事务？`Update`/`Delete` 返回什么？它们全是 OPEN QUESTIONS。We think 这与其说是"还没设计完"，不如说是"表达式的形状与副作用的水性不合"——而这件事 VB 早在 2014 年就趟过一回。

### 候选方案

**PROPOSAL A — 全量 DML 表达式家族（原文形态）。** `Insert` / `Update` / `Delete` 三种表达式原样落地：`Insert s In target From source Set mapping`、`Update t In target Where cond Set mapping`、`Delete c In target Where cond`，全部可在 `Let newStudentIds = ...` 的表达式位置使用。

**PROPOSAL B — 只做 `Insert` 表达式。** `Update` / `Delete` 继续走命令式 API。论据：`Insert` 是纯数据流（DTO → 实体字段的映射），最接近查询投影，副作用最浅；`Update` / `Delete` 是"匹配 + 改写 / 删除"，语义与具体数据源的批量行为绑得最深，语言层最难统一。

**PROPOSAL C — 语句式 DML（statement），不是表达式。** `Insert` / `Update` / `Delete` 作为语句返回 `Void`（或显式结果变量），不进表达式上下文。副作用在语句位置是 VB 的舒适区——`For Each`、`Do While` 全都是语句；脚本语境的"逐条执行"也更吻合。

**PROPOSAL D — 什么都不做。** 维持 ORM 命令式 API 与手写 SQL 现状；语言层不碰 DML，只留 analyzer/重构做脚手架。

### 权衡：Q&A

- **A vs B：Update / Delete 是否值得进语言？** `Delete c In db.Classes Where c.IsDeleted` 读起来确实漂亮，但它的编译目标是什么？要么绑定到 EF `DbSet` 的元数据（语言给库开小灶），要么要求 `In` 目标实现某种数据源契约——而批量 Update/Delete 的"受影响行数、逐条 vs 集合、级联、并发冲突"语义全在库层。语言层把形状定死，库层把语义塞回去，最后谁也没省事。**结论：Update / Delete 的价值不足以承担它们与数据源的耦合深度。**
- **A vs C：为什么原文非要表达式？** 因为 `Let newStudentIds = Insert ...` 想直接拿到插入后的主键集合。但"为了拿到返回值就把整条 DML 塞进表达式"，正是 2014 年我们拒绝表达式序列（#35）的同类冲动——那句话今天依然成立："VB has never allowed assignment in expressions, so it doesn't naturally go that far."（2014-02-17 #34）。副作用表达式在 VB 里没有站稳的土壤。
- **B vs D：Insert 值得吗？** 批量 DTO 灌入是真实场景，尤其脚本语境（CSV/JSON → 数据库）。但它依赖两个尚未落地的子系统：JSON 字面量的类型（`dto` 到底什么类型）与 `!` 操作符的类型化绑定（2017-10-18 我们讨论过 XML/JSON 标注类型的方向，`a!address` 应返回 `{"contact"}.{"address"}` 而非 `Object`）。没有它们，`dto!name` 在 Option Strict On 下就是一团 `Object`，映射样板会从 ORM 挪到类型转换上。**结论：Insert 的价值真实，但被前置依赖卡死。**
- **C 的脚本性。** VBScript.NET 是脚本语境，脚本语言里"一条 DML 语句、可能 `Await`、结果进变量"是自然形态。C 能让 DML 先以低风险形状落地，还能带出 async（`Await Insert ...`）——这是 A/B 都没有的空间。我们喜欢这一点。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**动词是合法标识符。** `Insert`、`Update`、`Delete` 今天都不是 VB 保留字——`Dim Delete As Integer`、`Function Update()`、`db.Students.Add(...)` 里它们全是普通标识符。让它们变成 DML 动词 = 新增上下文关键字。好消息是破坏面比想象窄：`Delete c In db.Classes` 这种形状今天本身就是语法错误（`c In db.Classes` 不是合法表达式，`In` 只在 `For Each`/查询/`Join` 上下文有意义），所以"表达式位置 + 后随 `标识符 In`"的识别不会改写今天能编译的代码。真正要防的是 `Update(x)` 作为方法调用、`Insert` 作为变量名——这些必须保持原解析。**上下文关键字 + 完整形状匹配**（`动词 标识符 In` 才算 DML）是唯一出路，但 parser 在 `Let x = Insert` 之后要前瞻多少个 token 才能决定走 DML 分支，需要 spec 级推敲。

**`Set` 三重语义。** `Set s.Name = dto!name` 里的 `Set` 撞上了三样东西：① VB 今天 `Property ... Set(value)` 的属性 setter 定义；② ModVB 的 `Set` 赋值语句建议（`proposal-set-statement.md`，`Set obj.Position += acceleration`、`Set left = Null, right = Null`）；③ 每个 VB6 老人都记得的 `Set x = New Foo` 对象引用赋值。同一个关键字三种含义挤在一个特性家族里，解析器要靠"`Set` 处于 DML 上下文"来消歧——`Probably` 可行，但这是教科书级的"同一记号多义"警示。

**`In` 的三重身份。** `Insert s In db.Students` 的 `In`，与查询 `From x In list` 的 `In`、以及 2018-05-30 我们否掉的 `In` 操作符（#305，"not significantly more expressive ... than `.Contains`"）不是一回事。这是声明 DML 目标的语法糖 `In`。三重 `In` 语义堆叠会让新手在"这个 `In` 是哪个 `In`"上反复横跳。

**`?` 前缀。** 原文 `? Update ...` / `? Delete ...` 用了 `?`。我们核实了原文上下文——`?` 是 Anthony 全文贯穿使用的交互式求值提示符（`? flags(...)`、`? From ...`），**不是语言语法**。`Probably`：会议纪要照录原文，但 spec 必须把 `?` 排除在文法之外，否则 `?` 会成为一个无家可归的新记号。

#### 2. 角案例与边界语义

**`Where` 缺省 = 清表？** `Delete c In db.Classes Where c.IsDeleted` 里 `Where` 吃一个布尔表达式，与查询一致。但 `Where` 能不能省略？`Delete c In db.Classes` 是否等价于 SQL 的 `DELETE FROM Classes`（无 WHERE 清表）？对一个以"永远别误删数据"为本能的语言，这是不可接受的默认。**规则倾向：DML 的 `Where` 要么强制，要么缺省时编译警告**（"此语句将作用于全部记录"）。`Suspect`：原文未提及，我们认为必须补。

**`Set` 的值语义。** `Set s.Name = dto!name` 里 `dto!name` 在 JSON 类型系统未落地前是 `Object`；`Set t.EmailAddress = $"{t.FirstName}.{t.LastName}@university.edu"` 是 `String`。`Set` 赋值遵循普通赋值的宽化/窄化规则即可，但"是否允许 `Set` 复合赋值（`Set s.Score += 1`）"原文没写——ModVB `Set` 语句支持复合赋值，DML 的 `Set` 若也支持，等于在"字段映射"之外又开了"读改写"的口子，副作用面进一步扩大。**倾向：v1 的 DML `Set` 只支持简单赋值。**

**空来源与重复执行。** `From dto In data` 中 `data` 为空数组 = 插入零条，合法。但 `Insert` 表达式放进循环会重复插入；放进 lambda 会延迟到不可预测的时机。表达式的"惰性直觉"与 DML 的"即时执行"直接冲突——这是我们必须钉死的执行模型问题。

**`Where` 谓词的纯度。** 查询的 `Where` 本就允许任意谓词（含副作用）；DML 的 `Where` 是否要求纯谓词？`Suspect`：语言无法强制纯度，但应在文档中警示"`Where` 谓词应无副作用"。

#### 3. 作用域与绑定

**语义模型的符号。** `Insert` 表达式的 `GetTypeInfo` 应返回结果类型（`IEnumerable(Of Key)` / `Integer`）；`s` 绑定到 `Student`；`Set` 目标的 `s.Name` 绑定到属性 setter 还是 EF 元数据字段？如果 DML 要走 EF 专用 lowering，绑定就要进 EF 的元数据，而那是**库的语义，不是语言的语义**——语言表达式的 `GetSymbolInfo` 返回一个库对象的属性引用，这在语义模型层面是史无前例的。`Probably`：这是"语言给库开小灶"最直接的证据，也是我们最不安的点。

**`dto!name` 的绑定。** `!` 是字典访问操作符。在 JSON 类型系统（`{"name": "Leo"}` 有类型 `{"name": String}`）落地后，`!name` 应返回 `String`；在那之前，`dto` 是 `Object`，`!name` 走 `IDictionary`——Option Strict 分叉由此而来。

#### 4. 与既有特性的交互

**查询表达式的执行模型反差。** DML 复用了查询的 `From` / `Where` 子句文法，但查询是**纯投影 + 惰性**，DML 是**副作用 + 即时**（若即时）。同一套文法的两种执行直觉并存，是对"读代码即懂行为"的最大威胁。若 DML 选惰性（返回一个枚举器，遍历时执行），`Let newStudentIds = Insert ...` 只注册不执行，副作用时机比即时更隐蔽。**两害相权，`Probably` 选即时**，并在文档用大字写明与查询的对立。

**事务。** EF 的事务边界在 `SaveChanges()`。`Insert` 表达式若即时写库，多条 DML 如何同事务？若排队到 `SaveChanges`，返回的 `newStudentIds` 在提交前是否可读？这是执行模型的核心未决，原文一字未提。

**`Let` 声明（ModVB 以 `Let` 替换 `Dim`）。** `Let newStudentIds = Insert ...` 在语法上自洽，但 `Let` 声明一个集合、而表达式在求值时写库——声明式外表 + 命令式内脏。

**async。** EF 有 `SaveChangesAsync`。`Insert` 表达式没有 async 形态；脚本语境（VBScript.NET 尤其需要）很可能要 `Await Insert ...`。A/B 都没有这个设计空间，C 有。

**表达式树。** DML 有副作用，`Probably` 应禁止进入表达式树上下文（表达式树语义要求可解释的纯操作；EF 的表达式树是给查询用的）。这条要写进 spec 的不变量。

#### 5. Breaking change 与兼容性

核心问题：**旧代码重编译后行为变不变。** 我们上面推演过——DML 形状（`动词 标识符 In`）今天不可编译，所以这部分的引入不改变任何现有合法代码。真正的兼容性债务在别处：

- **上下文关键字的长期成本。** 一旦 `Insert` / `Update` / `Delete` 在"表达式起始位置"获得语法特权，未来任何库（或用户代码）声明 `Update(...)` 方法、`Insert` 属性、`Delete` 局部变量，都会在解析器里经过 DML 形状判定。这本身不是破坏，但它是持续的解析复杂度和 IDE 混淆源——2014 年我们在 `Let` / 查询表达式的歧义上（#34）已经付过类似学费。
- **`Set` 与 ModVB `Set` 语句的叠层。** 若两个特性都进，`Set` 的解析规则要叠加"DML 上下文内 vs 语句上下文"，任何一边的 spec 遗漏都会造成跨特性回归。
- **`langversion` 门控。** 新语法必须受 langversion 与（若适用）Option 门控，旧项目不因升级编译器而获得 DML 语法特权。

`Probably`：破坏面窄，但"窄"不等于"免费"——我们需要一份逐条的兼容性表格，而不是一句"无破坏"。

#### 6. Option Strict / 编译选项分叉

`dto!name` 是分叉放大器。宽松模式：`Object` 隐式转换为 `Student.Name`（晚期绑定或宽化），DML 静默编译；严格模式：窄化/无转换 → 报错。**规则：DML 的 `Set` 赋值遵循普通赋值的规则，不引入 DML 特有的宽松化**——否则同一个 `Insert` 在两条路径下产生不同行为，违反"严格/宽松行为一致"的不变量（我们要求所有特性如此）。但也要意识到：在 JSON 类型系统缺席时，这个特性在 Option Strict On 下几乎不可用——分叉会实际推高对前置依赖的紧迫性。

#### 7. IDE / IntelliSense

`Insert s In db.Students` 之后，`Set` 子句要补全 `Student` 的属性；`From dto In data` 之后要补全 `dto` 的 JSON 字段。这要求语义模型有新的 DML 表达式节点与作用域链。另：`Where` 缺省时 IDE 是否给出"此语句将作用于全部记录"的红色波浪线？危险的 DML 恰恰是 IDE 最该干预的地方。这些都不做进规范等于没设计。

#### 8. 数据 / 普遍性

2018-05-30 我们说过："the majority of Visual Basic customers ... primarily want VB to keep doing what it does now." 批量 DTO 灌入是真实场景（数据迁移、EAV 导入、JSON 批量写入），但它在"数十万安静客户"里的占比没有任何量化数据。对照 #305（`In` 操作符因"没有显著真实案例"未推进），DML 的场景更具体——ORM 是 VB 业务代码的基础设施——但**语言层** DML 的刚性需求频次未证。脚本语境（VBScript.NET 的 CSV/JSON → 数据库）提升了普遍性，但那是 ModVB 沙盒的判断，不是主线的数据。`Suspect`：这仍是"真实但小众"的一档。

#### 9. 更简替代

- **ORM 命令式 API**（`Add` / `AddRange` / `Remove` / `SaveChanges`）：现状，样板在调用点堆叠，但确定、类型安全、库层负责语义。
- **EF 的库层批量 DML**（`ExecuteUpdate` / `ExecuteDelete`）：`Suspect`——这类 API 已在库层解决批量更新/删除的大部分场景，语言层 DML 与它直接重叠；Anthony 原文写作时的 EF 生态与今天已不同。这是一个必须在返工时核实的对照物。
- **SQL 插值字符串**：2015-01-14 我们讨论过 `Sql.ExecuteQuery($"from p in {x} select p.Name")`——插值查询串已经能表达 DML，代价是丢类型安全、方言绑定。
- **Analyzer / 重构**：提示"此处可转 DML 表达式"——不能替代语言特性，但能缓解部分样板痛苦，成本几乎为零。

#### 10. 复杂度 / 成本 / 优先级

完整 A 需要：新文法（DML 表达式 + `Set` 子句 + 上下文关键字）、数据源契约（`In` 目标的要求，接口形态还是 EF 专用？）、执行模型（即时 / 惰性 / 事务边界）、返回语义（`Insert` → 键集合？`Update` / `Delete` → 受影响行数？）、IDE 支持。这是接近一个查询理解特性的实现量，且其中一半（执行、事务、契约）是**库语义的移植，不是语言语义**。收缩到 B（仅 Insert）：实现面小得多，但仍被 JSON 类型与 `!` 绑定卡住。优先级上，DML 应排在查询理解（#104 主线方向）与 JSON 类型系统之后——它是**后置特性**。

#### 11. 运行时 / CLR 硬约束

无 IL 层约束——DML 降级为普通方法调用（`AddRange` / `RemoveRange` / `SaveChanges`），`call` / `callvirt` 都是既有安全操作，PEVerify 无碍，不触达 CLR 存储规则。真正的约束是**数据源契约**：若要求 `In` 目标实现特定接口，那是库层约束。另外表达式树必须排除 DML（见第 4 条）。

#### 12. 值不值得做

- **价值**：声明式可读性（`Delete c In db.Classes Where c.IsDeleted` 无可否认地漂亮）、批量灌入样板消除、与查询表达式对称。真实。
- **成本**：高——新文法 + 执行模型 + ORM 耦合 + IDE。
- **风险**：表达式副作用（2014 表达式序列的教训）、`Set` / `In` / 动词三重的记号冲突、与 EF 库层批量 DML 重叠、Option Strict 分叉、前置依赖未落地。

价值 × 成本 × 风险：**"Fantastic idea, and too hard to do"** 比"我们应该做"更接近真相。但要诚实——这不是"太难"，而是"形状错了"。把 DML 从表达式搬回语句（PROPOSAL C），把 `Insert` 从全家族里单独拎出来（PROPOSAL B），风险的质地就变了。我们不该因为 A 不成立就把整个方向烧掉。

### VB 基因对照

- **读起来像英语、对新手友好（原则 #5）**：`Delete c In db.Classes Where c.IsDeleted` 是本建议最亮的地方——无需解释，意图自明。`Where` 缺省会删除全部这一隐患若靠强制/警告对冲，这条仍然成立。
- **消除常见样板（原则 #9）**：批量 DTO 灌入的样板确实消除。但这是"低频、高仪式"场景，不是"高频、小样板"——与 `TypeOf` 收窄（上次会议）的普遍性不在一个量级。
- **不引入"第二种做事方式"（原则 #3）**：**扣分项**。DML 表达式是继 ORM 命令式、SQL 字符串之后的第三种做事方式。扩展表面的门槛极高，而本建议直接踩线。
- **避免隐蔽的控制流/语义变化（原则 #7）**：**主要扣分项**。`Return?` 因"Control flow would be altered by a very subtle character"被否（2018-05-30）；表达式带副作用是这条原则的远亲。即时 vs 惰性的执行模型若不钉死，就是又一个隐蔽语义。
- **不破坏现有代码（原则 #1）**：形状匹配限定了破坏面，但上下文关键字是长期解析债务，不能声称"无破坏"。
- **与主线关系（对照表 2.3）**：查询理解是主线 #104 的方向（`For Each` 查询理解 Approved-in-Principle 的延伸），但 DML **不在主线任何工作项**。它是 Anthony 独立延伸；与 2014 年"表达式序列被拒 + VB 无赋值表达式"的既有决议构成历史张力；与 EF 库层批量 DML 在库层重叠。`Insert` / `Update` / `Delete` 语法本身带有 SQL DML 的血缘（继承 VB 的查询理解外壳，但动作语义来自 SQL）。

### RESOLUTION:

1. **PROPOSAL A（全量 DML 表达式家族）被否决。** 语句级副作用以表达式形态进入语言，与 2014 年表达式序列的否决（#35）和"VB has never allowed assignment in expressions"（#34）的历史决议直接冲突；`Update` / `Delete` 与数据源的耦合深度让语言层无法给出统一语义；与 EF 库层批量 DML 重叠。作为表达式家族，它不成立。
2. **PROPOSAL B（仅 `Insert` 表达式）原则上吸引、但条件未满足。** `Insert` 是纯数据流、最接近查询，是家族里唯一值得挽救的部分。但它被两个前置依赖卡死：JSON 字面量的类型系统、`!` 操作符的类型化绑定（2017-10-18 的 XML/JSON 标注类型方向）。在那之前，`dto!name` 在 Option Strict On 下不可用。**Table：等待前置依赖。**
3. **PROPOSAL C（语句式 DML）是更 VB 的方向，值得作为独立工作项探索。** 副作用在语句位置是 VB 的舒适区；语句形态能带出 `Await Insert ...` 的 async 空间；不污染表达式上下文。这应该是一个**独立的、以语句为第一形态**的设计，而不是 A 的降级版。
4. **若任何 DML 形态被重新提起，以下前置条件必须写进 spec**：① 数据源契约（`In` 目标的要求，接口形态 vs EF 专用，明确拒绝"语言给库开小灶"的模糊地带）；② 执行模型（`Probably` 即时执行，明确与查询惰性的对照）；③ 返回语义（`Insert` → 键集合，`Update` / `Delete` → 受影响行数，若做成语句则用结果变量）；④ `Where` 缺省 = 强制或编译警告；⑤ 上下文关键字 + `langversion` 门控 + 逐条兼容性表格；⑥ DML 禁止进入表达式树。
5. **DML 的 `Set` 子句与 ModVB `Set` 语句必须共用一套解析规则**，禁止同一关键字两套职责叠层而互不知情。
6. **优先级**：DML 排在查询理解（#104）与 JSON 类型系统之后；本轮会议不分配实现资源。

### Implication:

- 起草一份语句式 DML（PROPOSAL C 形态）的 speclet 草案，与 `Set` 语句团队对表解析规则；不作为本轮交付，作为后续工作项的种子。
- 返工要求原文作者/维护者补充：文法（BNF 或 spec 变更）、执行模型、数据源契约、兼容性分析、EF 库层批量 DML 的对照（`Suspect`：需核实 `ExecuteUpdate` / `ExecuteDelete` 的确切版本与能力）。
- 将 `Insert` 表达式与 JSON 类型系统、`!` 绑定挂接，列入 JSON 子系统的前置依赖清单。
- 核实 `?` 前缀在原文的 REPL 身份（`Probably` 非语法），并在任何 spec 中明确排除。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：DML 的即时 vs 惰性执行——倾向即时，但事务边界（多条 DML 如何同事务、`SaveChanges` 的排队模型）完全未定。
- `OPEN QUESTIONS`：数据源契约的形态——接口（如 `IDmlSet(Of T)`）、EF 专用、还是放弃语言层 lowering 只做语法糖委托？这是"语言给库开小灶"问题的核心。
- `OPEN QUESTIONS`：`Insert` 返回键集合的可行性——依赖 EF 保存后回填主键，契约如何表达"插入后读 `s.Id`"。
- `OPEN QUESTIONS`：`Set` 子句是否支持复合赋值 / 方法调用；`Where` 缺省的强制 vs 警告策略。
- `TODO`：量化批量 DTO 灌入场景的真实占比，为数据/普遍性补证据。
- `Follow-up`：核实 EF `ExecuteUpdate` / `ExecuteDelete` 的能力边界，评估语言层 DML 与之重叠的精确程度（`Suspect`，待验证）。

### 状态

- **LDM 状态：LDM Reviewed: No Plans（作为表达式家族）**；`Insert` 表达式与语句式 DML 列为 LDM Considering（待前置依赖）。
- **三态判定：Table** — 价值真实、但形状（表达式）与根基（副作用）冲突；仅 `Insert` 可挽救且被前置依赖卡死；语句式方向值得独立探索但非本轮工作。

---

## 附录：特性评价

# 建议评价报告：proposal-insert-update-delete-expressions.md

## 评价对象

- 建议：proposal-insert-update-delete-expressions.md — `Insert` / `Update` / `Delete` 表达式
- 来源：Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1565–1587，与 `Include` 查询理解同章；`?` 前缀为原文交互式求值提示符，非语法）
- 配方目标：以声明式写法对数据源（如 EF `DbSet`）执行增删改，消除 ORM 命令式样板，支持 DTO 批量灌入

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。声明式 DML 的可读性（`Delete c In db.Classes Where c.IsDeleted`）明确、示例可操作，但返回语义、执行模型、数据源绑定三个关键子效果全部未定；未决问题 5 个（≥4 关键点）→ 效果证据封顶 | 已检查 | 无原型封顶；执行/事务/返回语义 100% 开放；效果依赖未落地的 JSON 类型与 `!` 绑定 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。查询理解外壳（`From`/`Where`）是 VB 血缘，动作语义来自 SQL DML（INSERT/UPDATE/DELETE）；但"表达式承担副作用"与 VB"表达式纯性"直觉（2014 #34/#35）冲突，`Set`/`In` 三重的记号碰撞是杂质 | 已检查 | 语句副作用进表达式违反原则 #7 的远亲；`Update`/`Delete` 与数据源耦合最深，非语言层可统一 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、5 个未决问题具体诚实（如实列出是加分）；但 Detailed design 无文法/BNF、无执行模型、无数据源契约、无兼容性分析，关键边界（`Where` 缺省、`Set` 范围）全含糊 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；无 breaking 分析；`?` 前缀未声明为非语法 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光（差异化：脚本语境的声明式 DML）与水（数据导向业务）受益；暗风险突出（ORM 耦合、上下文关键字长期债务、与 EF 库层重叠）且文档未设计对冲 | 已检查（预测待定） | 与主线"默认跟随 C#、扩展设高门槛"方向断裂未讨论；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 9 章；SQL DML 血缘（INSERT/UPDATE/DELETE 的动作语义）未标注；查询理解外壳（`From`/`Where` 复用 LINQ）未标注；EF `DbSet` 依赖已提及但契约形态未定；`!`/JSON 字面量的子系统依赖隐含未点明 | 已检查 | 继承 SQL 形状却未声明；"表达式的副作用"这一最关键成分的影响预估与实际（2014 否决）明显偏差 |

## 设计原则对照

- **与 VB 基因：偏离**（主体部分）。读起来像英语（#5）与消除样板（#9）合格，但"表达式承担副作用"违反 VB 长期"表达式无赋值/无副作用"的直觉（2014-02-17 #34/#35），触碰原则 #7（隐蔽语义变化）的远亲；`Set`/`In` 三重记号违反原则 #8（不与既有语法冲突）；DML 表达式是第三种做事方式，违反原则 #3。
- **与主线关系：Anthony 独立延伸**——查询理解是主线 #104 方向，但 DML 不在主线任何工作项；与 2014 表达式序列否决存在历史张力；与 EF 库层批量 DML 在库层重叠。属"沙盒式激进延伸"而非主线一致。
- **破坏性变更：有**（潜在）——`Insert`/`Update`/`Delete` 从合法标识符变为上下文关键字（长期解析债务，形状匹配下窄）；`Set` 与 ModVB `Set` 语句/属性 setter 名称复用需消歧；文档完全未分析。

## 总评

- **达成程度：部分达成**——声明式 DML 的概念与可读性成立；执行语义、数据源契约、关键字冲突、兼容性全部未解；作为"表达式"的形态与 VB 根基冲突。
- **LDM 三态建议：Table**——全量表达式家族 A 建议 Reject；`Insert` 表达式 B 待 JSON 类型系统与 `!` 绑定落地后重新评估；语句式 DML C 值得作为独立工作项探索（更 VB、可 `Await`）。
- **主要问题**：① 语句副作用进表达式与 VB"表达式纯性"的历史决议冲突（最重）；② 数据源绑定 = 语言给 EF 开小灶，契约形态未定；③ 上下文关键字 + `Set`/`In` 记号冲突与兼容性空白；④ 返回/执行/事务语义全开放；⑤ 与 EF 库层批量 DML 重叠未对照。

## 返工建议

- **补充章节**：文法（BNF 或 spec 变更，含上下文关键字的形状匹配规则）；执行模型（即时/惰性/事务边界，与查询惰性的对照）；数据源契约（接口 vs EF 专用）；兼容性/breaking 分析（上下文关键字逐条 + `langversion` 门控）；Option Strict 分叉（`dto!name` 的严格/宽松路径）。
- **补充证据**：批量 DTO 灌入场景的普遍性数据；EF `ExecuteUpdate`/`ExecuteDelete` 的能力边界与重叠分析（`Suspect`，待核实）；最小原型（`Insert` 表达式，绑定 JSON 类型 + `!` 操作符落地后）。
- **未决问题处理**：明确 `Where` 缺省 = 强制或编译警告；`Set` 子句 v1 仅支持简单赋值；`Insert` 返回键集合的契约设计；`?` 前缀声明为非语法。
- **设计探索**：语句式 DML（PROPOSAL C）作为独立 speclet——`Insert Into ... From ...` as statement + 结果变量 + `Await`；与 `Set` 语句共用解析规则；评估将 `Insert` 从"表达式"改写为"语句"后风险质地的变化。

---

## 附录：C# 生态与互操作考量

### 本附录的边界

本提案主题——把 `Insert` / `Update` / `Delete`（数据库 CRUD）做成语言级表达式——与 C# interop 的现实关系**偏弱**：C# 生态里不存在语言级写库（DML）语法，写库由 ORM 库层承担。据此如实说明：本附录不硬凑"对应物"，而是给出 C# 生态在**数据库操作、查询语言、表达式树、ORM 互操作**四个相邻面上的现实走向，以及它们对本提案判定的支撑或修正。

### 相关 C# 现实方向

1. **DML 明确不进 C# 语言，写入归 ORM 库层。** C# 3 把 LINQ query expressions（`from` / `join` / `where` / `select`）放进语言，但增删改从未有语言级语法；对 `proposals` 目录与 `meetings\2025` 的 Grep（`\bSQL\b`、`\bdatabase\b`、`Entity Framework`、`ORM`）只有零星真实命中、且集中在本附录引用的几处——SQL/database/DML 在 csharplang 中处于高度边缘地位，这一"缺席"本身就是关系弱的结构性证据。EF Core 的写库是库方法调用（`DbSet.Add` / `Remove` / `SaveChanges`），批量写库另有库层 `ExecuteUpdate` / `ExecuteDelete`（`Suspect`：属 dotnet/efcore，本库无正文可核实确切版本与能力）。
2. **C# 只读查询才进语言，且只到 join——从不越界到有副作用的写库。** 活跃提案 `left-right-join-in-query-expressions.md`（champion #8947，未归档）的 Summary 原文：「Introduce `left` and `right` modifiers to the LINQ query expression syntax `join` clause」；其 Motivation 把动机落在 SQL 对齐：「Many EF users have complained about the complexity of this construct for expressing a simple SQL LEFT JOIN.」→ `proposals\left-right-join-in-query-expressions.md`。C# 愿意为"贴近 SQL 的只读形状"加语法，但形状止于 join。
3. **C# 以 ORM 类型模型为利益相关者，介入点在"实体怎么写"而非"怎么写库"。** `required-members.md`（C# 11）Motivation 原文：「These scenarios are extremely common in database model ORMs, such as EF Core, which need to have a public parameterless constructor but then drive nullability of the rows based on the nullability of the properties.」→ `proposals\csharp-11.0\required-members.md`。EF Core 是 C# 特性设计的显式输入，但 C# 的回应是塑造实体类型模型（required members / `init`），不是添加 DML 语法。
4. **表达式树拒绝赋值与语句——"表达式纯性"是 C# 与 VB 共享的根基。** LDM-2015-04-14 原文：「While statements are part of the Expression Tree API, the languages will not convert them. Also, assignment operators will not be converted.」及「This is a remnant of the first wave of Linq, which focused on allowing lambdas for simple, declarative queries, that could be translated to SQL.」→ `meetings\2015\LDM-2015-04-14.md`。同一篇还记录了对"给 LINQ provider 施加新节点压力"的长期顾虑：「There has traditionally been an argument against adding more support to the languages based on the pressure this would put on existing Linq providers to support the new nodes that would start coming from Linq queries in C# and VB.」→ 同文件。这与本提案 RESOLUTION ⑥"（DML 禁止进入表达式树）"以及 2014 #34/#35"VB 表达式无赋值"的直觉同源。
5. **语言演进会扰动 ORM 翻译引擎——ORM 是语言变化的敏感下游。** `first-class-span-types.md`（C# 14）Expression trees 一节原文：「Similarly, translation engines like LINQ-to-SQL need to react to this if their tree visitors expect `Enumerable.Contains` because they will encounter `MemoryExtensions.Contains` instead.」→ `proposals\csharp-14.0\first-class-span-types.md`。C# 14 隐式 span 转换改变了会出现在查询树里的方法重载，LINQ-to-SQL / EF 翻译器必须跟随调整——"语言层一行改动、库层被迫适配"的实证。
6. **"SQL 作为 DSL"的 C# 尝试停在库层与字符串处理，从未进语言。** 2014 年 C# LDM 讨论过让插值串交给可信处理函数以预防注入，并给出 `SQL$"…"` / `URI$"…"` 形态示例，但结论是推迟：「We don't think accommodating custom interpolators in C# is the sweet spot at this point.」→ `meetings\2014\LDM-2014-05-21.md`。SQL 在 C# 生态始终是"外部字符串喂给库 / EF 表达式树翻译"，不是内建 DSL。
7. **动态 / 晚期绑定边缘化（索引 T7）。** C# 的 `dynamic`（C# 4）长期无演进；表达式树与 Span 冲突；unsafe-evolution 把"dynamic 是否应标 unsafe"列为 open question。C# 生态没有"晚期绑定写库"的方向参照。

### 现实 vs 提案

整体判定：**脱节（detached）为主，且是"预期内脱节"**——C# 生态没有语言级 DML 这块地，本提案既不追随也不背离 C#；分项看则有兼容、冲突与需桥接。

| 提案项 | 判定 | 理由 |
|---|---|---|
| PROPOSAL A（DML 表达式家族，已否决） | 脱节 | C# 从无语言级写库语法先例。A 若落地会成为 .NET 生态独一无二的语言级 CRUD，与 C#/EF 现状分叉；但这不构成"背离 C# 的同向竞争"，因为 C# 从未主张这块地。 |
| RESOLUTION 1 / ⑥（副作用不进表达式、DML 排除出表达式树） | 兼容（同源） | C# 表达式树同样拒绝赋值/语句（见现实方向 4）。"表达式纯性"是两门语言共享的根基，本提案对 2014 #34/#35 的遵从与 C# 现状在 .NET 生态层面一致。 |
| PROPOSAL C（语句式 DML，建议独立探索） | 兼容（互补） | C# 的写库本来就是库方法调用（语句形态）。语句式 DML 的 lowering 目标是现成的库 API，生态桥接对象现成，不构成障碍。 |
| `dto!name` 晚期绑定（Option Strict Off 分支） | 冲突（需桥接） | 与决策文件 M2/M5 同款张力：晚期绑定 = 反射，NativeAOT/trimming 难支持。若 .vbx 愿景含 NativeAOT，这是主要障碍。 |
| `Insert` 返回键集合（依赖 EF 保存后回填主键） | 脱节 | 语言侧无先例，EF 侧是库行为；契约形态在 C# 生态无对应讨论，维持 OPEN QUESTION。 |

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需动态**：语句式 DML 优先走强类型绑定（`In` 目标绑定 EF `DbSet` 元数据）；`dto!name` 在 JSON 类型系统落地前仅在 Option Strict Off 下放宽为 `IDictionary` 访问。与决策文件 M2 的"默认安全、按需动态"双模路线一致。
- **source-gen 桥**：C# 生态把 ORM 的 AOT 出路押在编译期生成（source generators、EF Core compiled model、interceptors）。.vbx 的 DML 若 lowering 到普通库调用，天然可被 source-gen 处理；解释模式只作显式 opt-in 的传统兼容层（决策文件 M5）。
- **识别新元数据**：C# 14 起查询树里会出现 span 重载（`MemoryExtensions.Contains`），`[OverloadResolutionPriority]` 可能改变重载选择。.vbx 编译器须像 LINQ-to-SQL 一样"react to this"，识别这些新元数据与重载规则，否则会把 C# 侧行为差异误判为错误。更一般地，unsafe-evolution 的 VB 小节原文——「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`——是"VB 编译器必须认识 C# 新元数据属性"的基线（本提案虽不触达指针，但同一原则适用：跨语言消费 C# 侧元数据时需识别）。
- **表达式树不变量对齐 C#**：DML 排除出表达式树（RESOLUTION ⑥）与 C# 现状天然一致，写进 spec 不会产生与 C# 生态的分歧。
- **差异化定位**：C# 不占语言级 DML 这块地，.vbx 若把语句式 DML 做成本地特色，不撞 C# 兼容性约束；但"第三种做事方式"（原则 #3）的生态成本仍在——EF 命令式、SQL 字符串、.vbx DML 三套并存。

### 对既有 RESOLUTION / 三态判定的影响

**C# 现实强化既有结论，不改变三态判定（仍为 Table）。**

- RESOLUTION 1（A 否决）：C# 从无语言级 DML 先例，是"表达式家族不成立"的旁证——连 C# 都没把这层抽象放进语言，VB 侧开这个口子的生态理由更弱。
- RESOLUTION ⑥（DML 禁入表达式树）：LDM-2015-04-14 证明 C# 表达式树同样拒绝赋值/语句，该不变量在 .NET 层面两语言一致，可写进 spec 而无生态分歧之虞。
- PROPOSAL C（语句式）：C# 的"写库 = 库调用"正好是它的 lowering 目标，生态桥接对象现成；这略为降低 C 的落地风险——不需要"语言给 EF 开小灶"，只需 lowering 到既有 API。
- `Suspect` 项维持：EF `ExecuteUpdate` / `ExecuteDelete` 的确切版本与能力边界属 dotnet/efcore，csharplang 仓库无法核实，**保留 OPEN QUESTION**；本附录无新增 C# 原文可裁决它。

### 引用纪律

本附录 C# 原文均逐字核对自 `..\..\csharplang` 镜像，逐条标注来源路径（见各现实方向条目）。索引第四节其余已核实引文（native-integers / function-pointers / blittable / span-safety / ref-struct-interfaces）与 DML 主题关系弱，未使用。EF Core 库层批量 DML、interceptors 最终状态等属 csharplang 之外的 dotnet/efcore 与 dotnet/runtime，标 **Suspect / OPEN QUESTIONS**。
