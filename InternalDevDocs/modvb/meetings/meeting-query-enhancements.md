# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。这次的材料来自 Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1527–1650）。我们本以为这周会讨论一套连贯的查询语法增强，但读了文档之后，开场十分钟就达成了第一条共识：**这不是一个特性，是四个。** 元组解构、聚合函数、目标类型化 `Select`、BC36606 修复——四件事的血缘、主线状态与成本画像完全不同。上一场 `For Each` 增强会议我们已经用同一把剪刀拆过一份四件套（元组解构 / `Exit For` / `Where` 过滤 / `Await Each`），今天这把剪刀又要派上用场。不同的是，这次的四件套里，有两件撞上了主线既有的定案与先例：一件撞 2016 年元组会议的括号定案，一件撞 2014 年 BC36606 的已批准修复。

## Agenda

* [Proposal: 查询增强（元组解构 / 聚合函数 / 目标类型化 Select / BC36606 修复）](#proposal-查询增强元组解构--聚合函数--目标类型化-select--bc36606-修复)

## Proposal: 查询增强（元组解构 / 聚合函数 / 目标类型化 Select / BC36606 修复）

_Related: 2014-02-17 LDM 笔记 #17 "Allow scalar query expressions to clash names"（BC36606，*Approved. Parity with C#*）；[vblang #48 – Multiple `For` or `For Each` Control Variables Per Statement](https://github.com/dotnet/vblang/issues/48)（_Related: [#104 – Extend `For Each` with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)_）；[vblang #337 – Pattern Matching（含递归/元组模式）](https://github.com/dotnet/vblang/issues/337)；2016-05-06 元组 LDM（解构语法定案）；ModVB：`proposal-query-comprehensions.md`、`meeting-for-each-enhancements.md`、`inactive/proposal-target-typed-conversions.md`（Anthony 自评"会打架"）_

### 场景与缺口

We started from the proposal's four scenarios, each claiming a distinct query-comprehension gap：

```vb
' ① 元组解构：坐标对直接进 From，免去额外 Select 投影。
From x, y In EnumerateCoordinates()
Order By y Descending

' ② 聚合函数：取第一个匹配项，不必动用 Aggregate 子句。
Let firstMatch = From c In db.Contacts
                 Where c.Id = contactId
                 Select FirstOrDefault()

' ③ 目标类型化 Select：方法组按目标类型定型为委托。
Let actions As IEnumerable(Of Action) =
      From obj In objects
      Select AddressOf obj.M   ' <- Typed to `Action`.

' ④ BC36606 修复：范围变量名与 Object 成员名冲突的误报。
Let strings = From n In 1 To 5
              Select n.ToString()
```

There is a clear DX parity gap in ①——取坐标对而不先投影，确实是查询里常见到让人想抱怨的样板。③ 也击中一个真实痛点：`AddressOf obj.M` 在 `Select` 里没有自然类型，今天要么报错要么需要显式 `CType`。但 ② 和 ④ 一进门就撞上了我们已经做过或正在做的事情：② 撞上既有的 `Aggregate ... Into FirstOrDefault()` 子句，④ 撞上 2014 年主线已批准、且（`Probably`）已实施的修复。**这不是一张待办清单，而是一份需要先对表的清单。**

### 候选方案

因为四件事血缘不同，We 按特性分别枚举候选方案。

**特性① From 元组解构：**

**PROPOSAL A — 裸逗号形式（Anthony 原文形态）。** `From x, y In EnumerateCoordinates()`：逗号分隔的范围变量列表 + 单一 `In` 源 = 解构。需要文法上区分"多变量解构"与既有的"多生成器"（`From a In x, b In y`）。

**PROPOSAL B — 括号形式（2016 主线定案形态）。** `From (x, y) In EnumerateCoordinates()`。2016-05-06 元组会议逐字记录了这条定案，解构语法列表里就含 `From (x, y) In GetPoints()`。

**PROPOSAL C — From 不做解构。** 维持单范围变量 `From`，解构交给 `Let` / `For Each`（`Let suit, rank = GetCard()`；`For Each key, value In dict`）。查询内先 `From p In points` 再 `Order By p.Y`。

**特性② Select 聚合函数：**

**PROPOSAL A — `Select FirstOrDefault()` 裸聚合（Anthony 原文形态）。** 无接收者的方法名出现在 `Select` 位置 = 终端聚合，整个查询结果从 `IEnumerable(Of T)` 变为 `T`。

**PROPOSAL B — 维持 `Aggregate ... Into FirstOrDefault()`。** 主线既有子句，功能等价，不改动。

**PROPOSAL C — 维持链式 `(From ...).FirstOrDefault()`。** 查询保持 `IEnumerable(Of T)`，取第一个用后缀调用。

**特性③ 目标类型化 Select：**

**PROPOSAL A — 查询结果整体目标类型化（Anthony 原文形态）。** `Let actions As IEnumerable(Of Action) = From ... Select AddressOf obj.M`：目标类型从声明流入终段 `Select` lambda，`AddressOf obj.M` 据此定型为 `Action`。

**PROPOSAL B — 仅限方法组转换。** 不推广到任意表达式；当 `Select` 表达式是 `AddressOf` 方法组且查询结果有目标类型时，允许方法组→委托定型。范围更窄。

**PROPOSAL C — 维持显式转换。** `Select CType(AddressOf obj.M, Action)` / `Select New Action(AddressOf obj.M)`。

**特性④ BC36606 修复：**

**PROPOSAL A — 按建议修复。** 范围变量名与 `Object` 成员名冲突时（`Select n.ToString()`）停止报 BC36606。

**PROPOSAL B — 移植 2014 已批准修复。** 2014-02-17 LDM 笔记 #17 已 "Approved. Parity with C#"，批准的两部分设计（Design1 改错误文案 / Design2 单元素标量 `Select` 跳过名字生成）直接决定本特性形态；若基础编译器已修复，本特性是 no-op。

### 权衡：Q&A

- **①A 与①B：裸逗号还是括号？** 这是我们这场会花时间最多的一问，因为它与上一场 `For Each` 会议的结论正面相遇。上一场我们决定 `For Each key, value In dict` 用裸逗号、合并 #48——但那时我们论证过：**`For Each` 的逗号今天没有任何语义**，所以 #48 可以安全地把逗号指派给"多控制变量列表"。`From` 不是这样：`From a In x, b In y` 已经是合法的多生成器语法，逗号已经载荷了"生成器分隔"的语义。`From x, y In c` 今天是一个错误（`x` 缺 `In`），所以裸逗号形式不会破坏既有代码，但它**必须**重定义逗号在 `From` 里的读取规则——从"每个逗号项自含 `In`"变成"裸标识符列表 + 单一尾部 `In` = 解构"。这正是 2016 年会议选择括号的理由："Yes, but they need parenthesis. Resolves some ambiguities, such as the last example, which would be a breaking change."
- **① 与 For Each 的一致性义务。** 上一场会议把 `For Each` 解构定为裸逗号（合并 #48，对 2016 括号定案的 documented deviation）。如果 `From` 用括号而 `For Each` 用裸逗号，同一概念两种形态；如果 `From` 也裸逗号，就需要一条明确的消歧规则。两场会议必须共享一个"解构逗号"故事，否则将来同一份 spec 两处实现。**结论：① 的语法决定挂在 For-Each/#48 合流之后的消歧规则上，不能独立拍板。**
- **②A 与②B：`Select FirstOrDefault()` 是不是 Aggregate 的第二种写法？** 是。`Aggregate c In db.Contacts Where c.Id = contactId Into FirstOrDefault()` 已经存在且功能等价（建议原文自认"Less need for Aggregate clause"）。原则 #3（不引入"第二种做事方式"）的扩展表面门槛极高，而这里省下的只是把一个子句换到 `Select` 位置。更糟的是 `Select FirstOrDefault()` 使 `Select` 从投影变成终端操作——违背 `Select` "只做投影"的直觉，也违背原则 #7（避免隐蔽语义变化）。**结论：② 是这套里最不占理的一项。**
- **③ 与目标类型化转换的"会打架"？** Anthony 自己的 `inactive/proposal-target-typed-conversions.md` 末尾标注 "I think this will cause fist fights."——当"转换表达式省略类型实参靠目标类型"与"查询结果目标类型化"同时存在时，两者会争夺"谁定类型"。上一场 `If()` 会议我们已经裁决过同类冲突的一半（有目标类型时目标类型统一裁决）。③ 是这条线索的查询侧延伸。
- **④ 是特性还是考古？** 2014-02-17 LDM 笔记 #17 批准的就是这个场景（`From arg In args Select arg.ToString()` 报 BC36606，*Approved. Parity with C#*），Design2 正是 `Select n.ToString()` 这一形态（单元素标量 `Select` 跳过名字生成）。`Probably`：主线已按此修复，当前基础编译器对 `From n In Enumerable.Range(1, 5) Select n.ToString()` 大概率不再报 BC36606。若属实，④ 在 ModVB 里是 no-op；若不属实，正确动作是移植已批准修复，而不是把它当作新设计。**结论：④ 先验证，再谈修不修。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**①裸逗号**是最重的文法问题。现行 `From` 子句文法：`From 生成器 {, 生成器}`，每个生成器是 `[变量 [As 类型]] In 表达式`。`From x, y In c` 会先被读成"生成器1=`x`（缺 `In`）→ 错误"。要支持裸逗号解构，需要一条前瞻规则：**逗号分隔的裸标识符列表后跟单一 `In` ⇒ 解构；否则按多生成器读**。这条规则在 `From a In x, b In y`（每项自含 `In`）下不产生歧义，但必须写进文法，且 parser 要处理混合形态 `From x, y In a, z In b`（解构 + 生成器并存）——建议完全没有讨论这个角案例。

**②裸聚合**是第二重的文法问题。`Select FirstOrDefault()` 里的 `FirstOrDefault` 是一个无接收者的方法名调用，今天的语法位置不允许这种东西。要让它在 `Select` 里成为聚合关键字，要么把它做成**保留字集合**（`FirstOrDefault` / `Count` / `Sum` / `Average` / `Min` / `Max`……——保留字会污染用户命名空间），要么用上下文相关文法判定"`Select` 后紧跟无接收者调用 = 聚合"。若作用域里恰好有一个名为 `FirstOrDefault` 的局部函数，判定会撞车。

**③与④无新文法。** ③ 是类型推断方向问题，④ 是错误抑制问题。

#### 2. 角案例与边界语义

**①解构数量不匹配。** `From x, y In source` 中元素若不是二元结构（无 `Deconstruct`、非元组、非 `KeyValuePair`），是编译错误；三元结构绑两个名字同样错误。解构依赖 `Deconstruct` 或元组转换（2016 已定元素级转换规则）。建议未写。

**①元素类型可空。** `EnumerateCoordinates()` 若返回 `IEnumerable(Of (X As Integer?, Y As Integer?))`，解构出的 `x`、`y` 的可空性如何传播——与 ModVB 可空性流分析共享规则。建议未写。

**②聚合的完整集合与返回类型。** 建议的 Unresolved questions 自己承认"完整集合与返回类型（可空与否）待定"。`FirstOrDefault` 在引用类型源上返回 `Contact`（可能为空）、在值类型源上返回默认值——这与 ModVB 空安全家族（`Contact?`）的交互必须定稿，否则"取第一个"的可空语义没人说得清。

**②多聚合与分组。** `Select FirstOrDefault(), Count()` 需要新的分组/标记语法（建议 Drawbacks 自认）；在 `Group By` 分组后的 `Select Count()` 语义也完全未定义。这不是可留到实现期的细节。

**③不兼容目标类型。** `Let actions As IEnumerable(Of Action) = From obj In objects Select AddressOf obj.M` 若目标类型是 `IEnumerable(Of Func(Of Integer))`，方法组不兼容——错误应报在 `Select` 表达式上还是赋值上？建议未写。

**④仍应报错的冲突场景。** 建议自认"精确判定条件待定义"。2014 的 Design1（改进文案）与 Design2（跳过名字生成）给出的是两种不同力度的判定；哪些名字冲突仍应报错（如用户显式写 `Select x As ToString`），没有定义。

#### 3. 作用域与绑定

**①**：解构出的 `x`、`y` 是查询作用域内的范围变量，语义模型返回 `RangeVariableSymbol`；绑定到源类型的 `Deconstruct` 或元组 `Item1`/`Item2`。2016 笔记对元组名字有一段警示："names are associated with declarations, not values"——解构是编译期动作，不经过晚期绑定，`x`/`y` 是编译器合成的位置名，无需运行时元组名支持。这条与上一场 `For Each` 会议的结论一致。

**②**：`Select FirstOrDefault()` 里的 `FirstOrDefault` 绑定到什么符号？如果是合成的聚合操作符，语义模型返回什么？如果是方法符号，则与 `Select c.FirstOrDefault()` 撞名。建议未写。

**③**：目标类型化后，`Select` lambda 的委托类型由目标类型决定；语义模型应在 `Select` 表达式处返回重定向后的 lambda 类型。这是编译器可做的最小承诺。

#### 4. 与既有特性的交互

- **`Let` 关键字冲突（最刺眼的交互）。** 建议的示例全部用 ModVB 的 `Let` 作为语句关键字（`Let firstMatch = ...`）。但 `Let` 已经是查询表达式的**子句关键字**（`From ... Let x = ... Select ...`）。2014 年会议对这事有过一句直接的记录："Does the keyword 'Let' cause ambiguities if we're using this inside query expressions? Note that 'Let' is bad for query expressions." ModVB 全局 `Let` 替换 `Dim` 的延伸（local-declarations 会议已拆件）与查询 `Let` 子句撞在同一拼写上——查询内外的 `Let` 语义不同，这是"同一个字两种语法"的教科书案例。
- **`Aggregate` 子句竞争**：② 的直接对手，已述。
- **范围表达式依赖**：④ 的示例 `From n In 1 To 5` 依赖 `1 To 5` 范围表达式（主线 #25，speclet needed）。该示例在主线 VB 里今天连源都写不出来；等价的主线形态是 `From n In Enumerable.Range(1, 5)`，而那正是 2014-02-17 LDM 笔记 #17 的原例。④ 与 range-expressions 建议共享依赖。
- **For-Each 解构（上一场会议）**：① 与它共享"解构逗号"故事，见 Q&A。
- **表达式树 / IQueryable**：② 的聚合若是终端操作，对 `IQueryable` 源必须翻译为可调用的方法（`.FirstOrDefault()` 在表达式树里是合法调用节点）；③ 的目标类型化若改变 `Select` lambda 的返回类型，表达式树按最终类型生成。都无 CLR 障碍，但翻译规则要写。
- **晚期绑定 / Option Strict Off**：解构与聚合都是编译期动作，不经过晚期绑定；③ 的方法组→委托转换是早期绑定，两路径一致。唯一分叉在 ② 的结果类型——宽松模式下无显式类型时 `firstMatch` 可能退化为 `Object`（与 typeless-declarations 建议交互）。

#### 5. Breaking change 与兼容性

四件事**全部是 error→program**：`From x, y In c` 今天语法错误，`Select FirstOrDefault()` 今天解析错误，`Select AddressOf obj.M` 今天推断失败，BC36606 今天报错——移除它们没有破坏任何既有合法代码。这是这套建议最安全的一面。**但**有两处需要警惕：

- **①的文法重解释**：`From a, b In c` 从"错误"变成"解构"，意味着 parser 对同一形态的新旧两种读取。虽然旧读取是错误，但错误位置与诊断文案的变化要审计；混合形态 `From x, y In a, z In b` 若进入语言，需保证不与任何现有合法 `From` 冲突。
- **③的目标类型化是否改变既有合法推断**：`Select AddressOf obj.M` 今天不能编译，所以没有合法代码可破坏。但若未来"查询结果目标类型化"推广到任意表达式（非仅方法组），它可能与既有推断竞争——这正是"会打架"的根源。v1 应限定为 ③B（仅方法组）。

#### 6. Option Strict / 编译选项分叉

- ① 在严格/宽松两路径下行为一致（元组结构是静态事实）。
- ② 在宽松模式 + 无显式类型下，`firstMatch` 的类型需要定义（严格模式推 `Contact`，宽松模式是否推 `Object`？）。这是 ② 的主要分叉点。
- ③ 的方法组→委托转换是早期绑定，两路径一致。
- ④ 是错误抑制，两路径一致。

#### 7. IDE / IntelliSense

- ① 解构出的 `x`、`y` 需要补全与 InfoTip；`From (x, y)` 括号形式的语法高亮需与 `Dim` 解构一致。
- ② 最麻烦：IntelliSense 必须在 `Select` 位置区分"裸聚合名"（下拉出聚合集合）与"普通成员调用"。这要求聚合保留字集或上下文判定在 IDE 里也有实现，否则补全会误导。
- ③ 透明：`actions` 显示 `IEnumerable(Of Action)`，`Select` lambda 内部的目标类型化不改变用户可见的补全。

#### 8. 数据 / 普遍性

四件事都没有量化数据。④ 有 2014 年批准时的真实 C# parity 案例（真实用户代码 `From arg In args Select arg.ToString()`），是唯一的"有据可查的痛点"。③ 的方法组进查询是 plausible 但未测量。① 的坐标对解构比上一场 `For Each` 覆盖的字典解构更窄（`For Each (key, value) In dict` 2016 已定案，字典遍历人人遇到；坐标对投影是特定领域）。② 的聚合场景已经有 `Aggregate` 与后缀 `.FirstOrDefault()` 两个现成出口，需求缺口最小。`Suspect`：这套建议整体是"改进可演示、普遍性未证"。

#### 9. 更简替代

- ①：`From p In points Order By p.Y`（两字符投影）或复用 `Let`/`For Each` 解构——上一场会议已把字典场景收入 `For Each` 裸逗号解构。查询内坐标对解构的增量是"省一次投影"，诚实地说不大。
- ②：`Aggregate ... Into FirstOrDefault()`（已存在）与 `(From ...).FirstOrDefault()`（已存在）——两个现成出口。② 没有覆盖任何现有语法覆盖不到的场景。
- ③：`Select CType(AddressOf obj.M, Action)`——一行、确定、一次性。这是四个特性里唯一"现有替代都带样板"的，③ 的样板消除主张成立。
- ④：已修复（`Probably`），若属实则无替代可谈。

#### 10. 复杂度 / 成本 / 优先级

- ① 中等：parser 前瞻规则 + binder 解构绑定。若不与 For-Each 逗号故事合并，两处实现各写一份消歧规则，成本翻倍且自造不一致。
- ② 中偏高：新文法（聚合名判定）+ 降级 + 语义模型 + 表达式树翻译，且多聚合设计未定型——**成本最高、价值最低**的一项。
- ③ 中偏高：查询结果目标类型化需要让目标类型穿过查询翻译流入终段 lambda；实现面与 `inactive/proposal-target-typed-conversions.md` 重叠，需并案协调。但 ③B（仅方法组）把范围缩得很小，成本可控。
- ④ 几乎为零：验证 + 若需则移植 2014 已批准修复。

#### 11. 运行时 / CLR 硬约束

无。四件事都是纯编译期概念；解构降级为"取元素 + 拆 `Deconstruct`/`Item`"，聚合降级为 BCL 方法调用（`FirstOrDefault` 是表达式树合法节点），方法组→委托是既有 `newdelegate`，无 PEVerify 问题，不触达 CLR 存储规则。①③ 不进表达式树、④ 是错误抑制。

#### 12. 值不值得做

逐维打分。**价值**：③ > ① ≈ ④ > ②（③ 消除唯一的"无现成替代"样板，④ 有 2014 真实案例，① 窄但真实，② 几乎无缺口）；**成本**：④ ≈ 0 < ① < ③ ≈ ②；**风险**：四件皆 error→program（安全），但 ① 有文法重解释与跨会议一致性义务，③ 有"会打架"协调义务，② 有保留字/判定污染。结论清晰：**④ 先验证后定；③B 值得独立做；① 挂在 For-Each 合流之后；② 是这套里唯一该认真考虑拒绝的。**

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：② 顶格违反（`Aggregate` 已存在，`Select` 聚合是第二出口）；④ 是修复不是新方式（免于该项）。
- **默认跟随主线 / C#（原则 #4）**：① 裸逗号偏离 2016 定案（括号），需"充分理由"论证；④ 是主线 2014 已批准项（跟随）；③ 在主线无对应物，是 Anthony 独立延伸，且 Anthony 自己预警"会打架"。
- **读起来像英语、对新手友好（原则 #5）**：`From x, y In points` 读起来顺；`Select FirstOrDefault()` 读起来像投影了一个返回值，不像终端聚合——语义与语感错位。
- **避免隐蔽语义变化（原则 #7）**：② 把 `Select` 从投影悄悄变成终端操作，是被拒 `Return?` 同族的隐患；①③④ 无此问题。
- **消除常见样板（原则 #9）**：③ 正中靶心（消 `CType` 样板）；① 消投影样板但增量小；④ 消除一个误报（若未修）。
- **永不破坏既有代码（原则 #1）**：四件皆 error→program，本套最亮的地方。
- **与主线关系（对照表 2.3）**：④ = **主线一致**（2014 已批准）；① = **与主线定案冲突/Anthony 独立延伸**（2016 括号 vs 裸逗号）；② = **与主线现有子句竞争**（Anthony 独立延伸）；③ = **Anthony 独立延伸**（主线无对应，与 inactive 目标类型转换"会打架"）。**同一份文档四条不同的主线关系——这是本套最根本的"基因分裂"。**

### RESOLUTION:

1. **拆分文档。** 建议以"查询增强"为全貌，但四件事血缘与主线状态不同，不能同生共死。按上一场 `For Each` 会议的先例，拆成四份各自裁定。
2. **④ BC36606 — 先验证，后谈修。** `Probably`：主线 2014-02-17 LDM 笔记 #17（"Approved. Parity with C#"，Design2：单元素标量 `Select` 跳过名字生成）已覆盖 `Select n.ToString()` 形态，基础编译器大概率不再报 BC36606。动作：用 `From n In Enumerable.Range(1, 5) Select n.ToString()` 对照当前编译器验证；若已修复，关闭本项（no-op）；若未修复，移植 2014 已批准修复（Design2 + 必要时 Design1 文案），**不当作新设计**。
3. **① From 元组解构 — Consider（挂在 For-Each 合流之后）。** 裸逗号形式（Anthony 原文）需要一条消歧规则（"裸标识符列表 + 单一尾部 `In` = 解构"），与多生成器 `From a In x, b In y` 的逗号语义区分。这条规则必须与上一场 `For Each` 会议合并 #48 的裸逗号故事共享同一份 spec，并对 2016 括号定案做 documented deviation。消歧规则写不出来之前，回退到 2016 已定案的括号形式 `From (x, y) In`。混合形态 `From x, y In a, z In b` 必须显式定义。
4. **② Select 聚合函数 — Reject（方向）。** 第二做事方式（`Aggregate ... Into FirstOrDefault()` 已存在）、`Select` 语义被悄悄改成终端操作（原则 #7）、裸聚合名的保留字/上下文判定污染命名空间、多聚合与返回类型可空语义未定义。价值（省一个子句）× 成本（新文法 + 降级 + 语义模型 + 表达式树）× 风险（语义错位）三边都不占。若未来想做"更聪明的聚合"，应改进 `Aggregate` 子句本身，而不是让 `Select` 兼任投影与聚合两个角色。
5. **③ 目标类型化 Select — Consider（拆分独立提案，限定 ③B）。** 本套最可辩护的一项：唯一"现成替代都带样板"的痛点，且与 2016 已定的元组/数组字面量目标类型化、以及上一场 `If()` 的目标类型化裁决同属一条主线。v1 范围 = ③B（仅 `Select` 表达式为 `AddressOf` 方法组时，按查询结果目标类型定型），不推广到任意表达式；与 `inactive/proposal-target-typed-conversions.md` 的"会打架"裁决链并案协调（有目标类型时目标类型统一裁决）。
6. **一致性义务。** ① 与 `For Each` 解构共享"解构逗号"故事；② 若被复活必须与 `Aggregate` 子句、`Group By`、ModVB 空安全家族（`Contact?`）对表；③ 与目标类型化转换、`If()` 目标类型化、数组/元组字面量对表；④ 与 range-expressions 的 `1 To 5` 依赖对表。跨会议对表不做，任何一项都不能独立落地。

### Implication:

- 验证 BC36606 在基础编译器的现状，决定④是关闭还是移植。
- 起草 ① 的消歧规则 speclet：`From` 子句的文法扩展、解构与多生成器的判定、混合形态语义、与 `For Each` 解构共享的"解构逗号"章节；同时给出对 2016 括号定案的 deviation 声明。
- 将 ③ 拆为独立提案（范围 = 方法组目标类型化），与 inactive 目标类型化转换的裁决链并案。
- 将 ② 标记为 Reject 存档；记录复活条件（改进 `Aggregate` 子句而非 `Select`）。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：① 的消歧规则——`From x, y In a, z In b` 混合形态是否允许；解构列表中的显式 `As` 类型（`From x, y As Integer In c`）；解构元素可空性的传播。
- `OPEN QUESTIONS`：③ 的目标类型化范围——是否仅 `AddressOf` 方法组，还是未来推广到任意 `Select` 表达式；与"会打架"目标类型化转换的裁决链。
- `OPEN QUESTIONS`：② 若复活——聚合名的保留字集 vs 上下文判定；多聚合语法；`Group By` 后的聚合；结果类型在 Option Strict Off 下的退化。
- `TODO`：对照当前编译器验证 `From n In Enumerable.Range(1, 5) Select n.ToString()` 是否仍报 BC36606（④ 的开/关依据）。
- `Follow-up`：与 `meeting-for-each-enhancements.md` 对表"解构逗号"的单一 spec；与 `proposal-query-comprehensions.md`（#104 范围）对表循环头查询理解与 `From` 子句的边界；与 `inactive/proposal-target-typed-conversions.md` 对表"谁定类型"。

### 状态

- **LDM 状态：文档整体 Consider（偏 Table）**；④ = 验证后定（若已修复则关闭）；① = Consider（挂 For-Each 合流）；② = Reject（方向）；③ = Consider（拆独立提案，限定方法组）。
- **三态判定：Consider** — 价值与风险逐特性分离后，只有③（目标类型化 `Select`）值得独立推进，④是考古确认，①是合流后的事，②应拒绝。整份文档按原样"作为查询增强"交付，既过宽又互相拖累，不予背书。

---

## 附录：特性评价

# 建议评价报告：proposal-query-enhancements.md

## 评价对象

- 建议：proposal-query-enhancements.md — 查询增强（`From` 元组解构 / `Select` 聚合函数 / 目标类型化 `Select` / BC36606 修复）
- 来源：Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1539–1650；四个示例逐字出自该章；该章还含 range expressions、Include 查询理解、Insert/Update/Delete 表达式等未纳入本建议的相邻内容）
- 配方目标：查询语法四件套——`From` 直接解构坐标对、`Select` 内联聚合、方法组按目标类型定型、BC36606 误报停止

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。四个场景示例可演示、与原文一致；但①无原型（证据止于书面）、②关键子效果（多聚合/可空返回类型）缺失、④可能已是主线修复（no-op）、③的目标类型化范围未定——四个特性没有一个达到"已运行"证据级 | 已提供/已检查（状态行 Prototype/Implementation 为占位链接，无运行证据） | ③是唯一"改进可衡量"项；其余普遍性无数据；④的效果取决于基础编译器现状 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。四件强无关能力捆绑（red flag）；①裸逗号偏离 2016 主线定案且未声明"充分理由"；②是既有 `Aggregate` 子句的第二出口；③是 Anthony 独立延伸（自评"会打架"）；只有④是主线一致 | 已检查 | 与 `Select` 语义（投影/聚合）错位；`Let` 语句关键字与查询 `Let` 子句撞名未识别；C# 平行物（方法组转换）未标注 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、Drawbacks/Alternatives/Unresolved 三节如实（1–3 健康区间附近）；但①无文法消歧规则、②聚合设计停在"要点"级、③目标类型化范围未定、④未对照 2014-02-17 LDM 笔记 #17 已批准修复（把已批准项写成新修复）、无 Compatibility/breaking-change 章节、状态行为占位链接 | 已检查 | 四特性捆绑使边界模糊；BC36606 项对主线状态的误判是事实性缺口；未引 2016 括号定案、2014-02-17 LDM 笔记 #17、For-Each 会议的既有裁决 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。光（①③消除样板）与雷（实现面小）有正向；但风严重受损——①偏离 2016 定案且与 For-Each 裸逗号未统一（一致性断裂）、②与 `Aggregate` 竞争制造第二出口、③与 inactive 目标类型化"会打架"；文档未对冲任一断裂 | 已检查（预测待定） | 四特性的主线关系互相矛盾（一致/冲突/竞争/独立延伸），文档未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 2/5 | 锚点 2："来源混淆、影响预估与实际明显不符；混入无关特性未说明"。材料=Anthony 第 9 章（未标章节号）；**未标注**与 2016 元组括号定案、2014-02-17 LDM 笔记 #17 BC36606 先例、既有 `Aggregate` 子句、For-Each 解构会议的共享特征；④把主线已批准修复写成新特性（影响预估与实际不符）；②对 `Aggregate` 的竞争关系未声明 | 已检查 | 无杂质但先例继承关系大面积欠标注；跨建议依赖（range-expressions、For-Each、目标类型化转换）未交叉引用 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离。** 一致：四件皆 error→program（原则 #1 安全）、③消除样板（#9）、①读起来顺（#5）。偏离：②顶格违反原则 #3（第二做事方式）并违背原则 #7（`Select` 语义悄悄变成终端操作）；①裸逗号偏离 2016 主线定案且无"充分理由"论证（#4）；②③是 Anthony 独立延伸，其中③自评"会打架"。
- **与主线关系：混合，无法一句话归类。** ④ = 主线一致（2014-02-17 LDM 笔记 #17 Approved）；① = 与主线 2016 定案冲突（Anthony 独立延伸）；② = 与主线既有 `Aggregate` 子句竞争（Anthony 独立延伸）；③ = 主线无对应物（Anthony 独立延伸，与 inactive 目标类型化转换交互）。同一份文档承载四种关系，是设计上的根本缺陷。
- **破坏性变更：无（直接）。** 四件今天均为错误或误报，error→program 无既有代码破坏。间接风险：①的文法重解释（错误→解构）需审计诊断变化；③若推广到非方法组会与既有推断竞争。

## 总评

- **达成程度：部分达成。** ③（目标类型化 `Select`）概念成立、痛点真实、可限定范围落地；④大概率已由主线修复（需验证）；①价值窄且被文法歧义与跨会议一致性义务拖累；②与现有 `Aggregate` 子句重复、应拒绝。
- **LDM 三态建议：Consider。** 拆分后：③ = **Consider→Active 候选**（拆独立提案，限定方法组目标类型化，与"会打架"并案）；① = **Consider**（挂 For-Each 合流 + 消歧规则，否则回退 2016 括号定案）；④ = **验证后定**（已修复则关闭，未修复则移植 2014 批准项）；② = **Reject**（第二做事方式）。
- **主要问题**：① 四特性捆绑、血缘互相矛盾；② ②的聚合设计（多聚合、可空、分组）未定型；③ ①裸逗号文法歧义与 For-Each/2016 的一致性义务未识别；④ BC36606 项误判主线状态（已批准/可能已修复）；⑤ `Let` 关键字与查询 `Let` 子句撞名未识别。

## 返工建议

- **范围切分**：拆成四份文档或显式分节；④标注"验证已修复 / 移植 2014 批准项"而非新设计；③独立成案；②标记 Reject 存档并记录复活条件。
- **补充章节**：① 文法（`From` 子句消歧规则 BNF、混合形态 `From x, y In a, z In b`、解构列表显式 `As`）；Compatibility/breaking-change（错误→程序的诊断变化审计）；Option Strict On/Off + Infer 双路径验证示例；② 的聚合集合/可空返回/`Group By` 交互；③ 的目标类型化范围与裁决链。
- **补充证据**：④ 对照当前编译器验证 `From n In Enumerable.Range(1, 5) Select n.ToString()`；③ 的最小原型（方法组在查询中的目标类型化 + 语义模型 + 表达式树）；真实代码中"查询内取坐标对/方法组进 `Select`"的频次数据。
- **未决问题处理**：① 的消歧规则与 `meeting-for-each-enhancements.md` 共享单一"解构逗号" spec；② 的结果类型与 ModVB 空安全家族对表后再议复活；③ 与 `inactive/proposal-target-typed-conversions.md` 的"谁定类型"裁决链并案；`Let` 关键字与查询 `Let` 子句的撞名在 local-declarations 文档中补一致性命中说明。

---

## 附录：C# 生态与互操作考量

> 本附录评估查询增强四件套与 C# 生态现实的关系。与索引（`..\..\csharplang-index.md`）的强 interop 主题（Span、nint、函数指针、ref struct 接口、blittable）不同，本提案（LINQ 语法增强）对应的 C# 现实是**对查询表达式/方法链的态度**与 **EF/IQueryable 消费契约**。索引第四节 6 段可直接引用的原文均为低层互操作内容，与本提案关系弱，故不强行引用；本附录引用的 C# 原文全部在 `..\..\csharplang` 逐字核实。

### 相关 C# 现实方向

**R1 — C# 查询表达式自 C# 3.0 起长期休眠，2025 年首次破冰。**
- C# 3.0 以查询表达式承载 LINQ（翻译契约见 C# spec §11.7.1 / §12.20.3.5，现迁至 dotnet/csharpstandard，本镜像无正文）。此后二十余年查询语法几乎零演进，直到 left/right join 提案才成为多年来的第一个查询语法变更。
- 提案 Drawbacks 自认：「It's worth noting that C# query expression support for LINQ hasn't evolved in a long time - this would be the first change in quite a while. At the same time, AFAIK there hasn't been any formal deprecation/archiving of this area of the language.」→ `proposals\left-right-join-in-query-expressions.md`
- LDM-2025-02-12 以"全体一致"通过该提案（champion #8947），目标随 .NET 10 的 `LeftJoin()`/`RightJoin()` 落地：「The LDM is unanimously in favor of moving forward with this, and a slight majority think we should try to get it in for C# 14, to coincide with the new methods being added to the BCL.」→ `meetings\2025\LDM-2025-02-12.md`。**Suspect**：`Language-Version-History.md` 的 C# 14（.NET 10）已发布清单不含该特性——它未进入 C# 14 最终版（或版本历史未更新）。当前可靠状态是"LDM 已批准、开发中"。
- 与索引 T1 的「先语言成型、再 runtime 跟上」相反：在 LINQ/查询领域，C# 是 **BCL 方法先行（.NET 10 的 `LeftJoin()`/`RightJoin()`，dotnet/runtime#110292）、语言语法殿后**，EF 10 同步。这确立了"查询语法增强必须锚定 BCL 方法"的惯例。

**R2 — C# 对查询语法保持"翻译契约"：查询表达式只是扩展方法链的语法糖，provider 是消费契约。**
- 查询表达式逐子句降级为扩展方法调用；新查询语法必须映射到可翻译的 BCL 方法。left/right join 正是这样设计：「Note that the proposed `join` modifiers do not require any LINQ expression tree changes, as they're represented via existing MethodCallExpression's which reference the new `LeftJoin()` and `RightJoin()` methods. There is thus nothing blocking supporting them from LINQ providers (such as EF Core).」→ `proposals\left-right-join-in-query-expressions.md`
- C# 明确权衡"加语法 vs 掉回方法链 vs provider 报错"：「We can't really avoid query providers failing on these new joins; the methods will exist in the BCL regardless, and various query providers might fail on them no matter what.」以及「While query syntax support might widen that gap, it's very possible that lack of it will hurt users who need the new operator more than it would cause users to hit query provider errors, since users who need this will have to drop the entire query back to method syntax.」→ `meetings\2025\LDM-2025-02-12.md`
- 这也是**EF/IQueryable 消费契约**：不做语法糖，就是"部分特性"——「Also, since first-class left/right join is being introduced into .NET and EF 10, not including support in C# would mean a partial feature that's implemented only in some parts of the stack and not in others.」→ `proposals\left-right-join-in-query-expressions.md`（Alternatives）

**R3 — C# 承认查询语法落后于 VB。**
- 原文：「We may want to expand further in the future; for example, C# does not support `distinct` in query syntax today, like VB does, but if increased `join` options are successful, that may be enough of a signal to add further operators; conversely, if this ends up causing significant increases in errors for users and lots of pain, we know that further changes here are harder than they appear at first glance.」→ `meetings\2025\LDM-2025-02-12.md`
- 即：**VB 的查询理解文法是比 C# 查询语法更丰富的一面**（VB 有 `Distinct`、`Aggregate ... Into`，C# 至今没有）。本提案的 ② 主题（聚合）恰好也在 C# 的缺口清单上（见 R4），但 VB 已有 `Aggregate` 子句——VB 不是缺聚合能力，而是缺"第二种写法"。

**R4 — C# 的查询语法缺口清单与 ② 直接撞名。**
- 提案 Open questions 原文：「As noted above, this is the first proposal for evolving C#'s support around LINQ in a long while; there are quite a few other gaps in this area: additional C# query expression clauses (distinct, aggregates, set operations...), as well as evolving expression tree support to allow for newer C# constructs」→ `proposals\left-right-join-in-query-expressions.md`
- C# 把 `aggregates` 列为查询语法缺口之一，与 ② `Select FirstOrDefault()` 主题相同；但 C# 的路线是"加新子句、翻译到 BCL 方法"（left/right join 先例），不是让 `Select` 兼任聚合（见「现实 vs 提案」②）。

**R5 — C# 的增长引擎是方法链/扩展成员/lambda + 目标类型化，不是查询语法。**
- C# 14 正式落地扩展成员：「Extension methods and properties: allows extending an existing type with instance or static methods and properties.」→ `Language-Version-History.md`（C# 14.0 清单）；配套 `proposals\csharp-14.0\extension-operators.md`。LDM-2024-10-14 做了「converting LINQ to the respective new forms」的演练，讨论扩展成员如何重塑 LINQ 库本身的写法（`extensions` 集合 vs `extension` 单类型两种形态）。→ `meetings\2024\LDM-2024-10-14.md`
- 目标类型化是 C# 强文化：C# 12 集合表达式「Collection literals are target-typed.」→ `proposals\csharp-12.0\collection-expressions.md`；C# 9 target-typed new 一脉相承。这是 ③ 在 C# 侧的"世界观"同源。

**R6 — 表达式树在 C# 被边缘化只做小修，且新特性会扰动 provider 的 tree visitor。**
- C# 14 仅放宽表达式树的可选/具名参数：「Errors are reported for calls in `Expression` trees when the call is missing an argument for an optional parameter, or when arguments are named.」→ `proposals\csharp-14.0\optional-and-named-parameters-in-expression-trees.md`
- 更重要的警示来自 first-class-span-types：新重载会改变查询翻译到的节点——「Similarly, translation engines like LINQ-to-SQL need to react to this if their tree visitors expect `Enumerable.Contains` because they will encounter `MemoryExtensions.Contains` instead.」→ `proposals\csharp-14.0\first-class-span-types.md`（Expression trees 一节）。凡让查询翻译到"新方法/新节点"的改动，EF/LINQ-to-SQL 的 tree visitor 都可能遭遇意外节点，需要 provider 迁移路径。

### 现实 vs 提案

| 特性 | C# 现实对应 | 判定 | 理由 |
|---|---|---|---|
| ① From 元组解构 | 无对应物。C# 查询语法无解构；C# 的解构在语句/模式（`var (x, y) = p`）与 `Deconstruct` 契约层 | **兼容（VB 特色，无 C# 参照）** | C# 查询语法保守且 C# 不把解构视为查询语法该管的事（R1）；VB 解构依赖同一 CLR `Deconstruct` 契约，跨语言互通无碍。C# 的保守态度只提示"进查询语法要谨慎"，不构成否决 |
| ② Select 聚合函数 | C# 把 aggregates 列为查询语法缺口（R4），但路线是"新子句→BCL 方法翻译"（R2） | **需桥接 / 方向不合** | ② 的"Select 变终端"违背 C# 翻译契约精神——C# 不会让 `Select` 兼任投影与聚合。技术上 `FirstOrDefault` 是合法表达式树节点（本会 §11 已确认），EF 可翻译；但 C# 的破冰先例（left/right join）示范的是"加子句、翻译到方法"，② 是"复用关键字改语义"，与 C# 惯例背道而驰 |
| ③ 目标类型化 Select | 目标类型化是 C# 强文化（R5）；方法组→委托是 CLR 既有 `newdelegate` | **兼容（C# 同向）** | ③B 把范围限定为"查询结果目标类型化 + 方法组→委托定型"，正落在 C# 目标类型化谱系内；.vbx 消费 C# 库时方法组→委托元数据互通，无 CLR 障碍 |
| ④ BC36606 修复 | 无直接对应。C# 范围变量名不与 `Object` 成员晚期绑定冲突 | **兼容（无 C# 参照）** | VB 特有的错误抑制；2014 "Parity with C#" 意味把 VB 行为对齐到 C# 的"范围变量不遮蔽成员解析"事实 |

补充宏观背景：C# 影响 CLR/.NET 生态的主要方式是"先语言成型、再 runtime 跟上"（索引 T1），而查询增强四件套全部是纯编译期概念（本会 §11：不触达 CLR 存储规则、不新增运行时依赖）。因此本提案**不在 C# 影响 CLR 的核心带上**；它受 C# 的"文化/习惯法"（查询语法怎么演进、provider 契约怎么守）影响，而非硬性技术约束。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态（呼应决策文件 M2/M5）**：③B 是早期绑定（方法组→委托），契合"默认安全"；② 若未来复活，其在 Option Strict Off 下退化为 `Object` 的路径是"按需动态"入口，应显式标注为宽松模式行为。.vbx 应让查询语法默认静态定型、动态仅作 opt-in。
- **source-gen / 编译到受管程序集桥（呼应决策文件 M5）**：若 .vbx 要支持 EF/IQueryable，查询必须翻译到 `MethodCallExpression` 链供 provider 静态消费（R2）。解释执行（scripting-interpreted）天然破坏该契约；默认路径应为"编译到受管程序集 + source-gen 桥"，让 provider 能按表达式树翻译。
- **识别新元数据 / 新 BCL 方法**：.NET 10 `System.Linq` 新增 `LeftJoin()`/`RightJoin()`（dotnet/runtime#110292），VB 编译器与 IntelliSense 需认识这些扩展方法，方法链（乃至未来 VB 若跟进 `Left Join` 子句）才可用。同理，C# unsafe-evolution 引入的 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`（决策文件 M8）虽与查询主题无关，却是"必须认识新元数据"的同型义务——.vbx 的元数据识别基线要随 C# 现实滚动更新。
- **守住 provider 消费契约（呼应 R6 教训）**：新查询特性 ①③ 不进表达式树（本会 §11 已定），风险低；② 若做，必须翻译到 provider 认识的 BCL 调用，并参照 first-class-span-types 的警示——凡引入"同功能新重载/新节点"，EF/LINQ-to-SQL 的 tree visitor 可能遭遇意外节点，需提供迁移路径。

### 对既有 RESOLUTION/三态判定的影响

C# 现实**强化既有裁决，不新增否决或批准依据**：

- **② Reject 获外部印证**：C# 把 aggregates 列为查询语法缺口（R4），说明"聚合缺口"真实存在，但 C# 示范的解法是"加新子句、翻译到 BCL 方法"（left/right join 先例），与 RESOLUTION 第 4 条"应改进 `Aggregate` 子句本身，而不是让 `Select` 兼任投影与聚合"同构。C# 先例支持"拒绝 Select 聚合、改进 Aggregate"的方向。
- **③ Consider（③B）获文化背书**：C# 是目标类型化驱动的语言（R5），③B（方法组按目标类型定型）落在 C# 谱系内，跨语言调用无缝。RESOLUTION 第 5 条的"限定方法组、不推广任意表达式"与 LDM-2025-02-12 的谨慎破冰策略（"further changes here are harder than they appear at first glance"）互相印证。
- **① Consider 不变**：C# 查询语法无解构参照物，① 是纯 VB 特色扩张；C# 的保守态度只强化"需消歧规则 + 跨会议一致性"的门槛（RESOLUTION 第 3 条），不改变判定。
- **④ 不变**：VB 特有错误抑制，与 C# 现实无交互。
- **三态判定（Consider）保持不变**。本附录提供的不是新的三态依据，而是"外部一致性校验"：VB 查询语法增强落在 C# 未覆盖、且 C# 承认落后（R3）的领地上，属于 VB 可保留的特色面。

### 引用纪律与本附录的 OPEN QUESTIONS

- 本附录引用的 C# 原文均已在 `..\..\csharplang` 逐字核实（见各条来源标注）。索引第四节 6 段可直接引用的原文均为低层互操作内容（Span/nint/函数指针/ref struct 接口/blittable/unsafe-evolution VB），与本 LINQ 语法提案关系弱，未强行引用。
- `OPEN QUESTIONS`：C# left/right join 的最终发布版本——LDM-2025-02-12 批准、目标 C# 14，但 `Language-Version-History.md` 的 C# 14 已发布清单不含该特性（**Suspect**：可能延迟至 C# 15，或版本历史未更新）；C# extensions 重塑 LINQ 库写法的最终形态（`extensions` vs `extension`，LDM-2024-10-14 仍在权衡）——若 C# extensions 落地，`System.Linq` 方法链的表述方式会否重构，进而改变 VB 查询语法翻译到的目标（OPEN）。
- `OPEN QUESTIONS`：EF Core 对 .NET 10 `LeftJoin()`/`RightJoin()` 的表达式树翻译细节在 dotnet/runtime 与 efcore 仓库，本镜像无正文，未深挖。
