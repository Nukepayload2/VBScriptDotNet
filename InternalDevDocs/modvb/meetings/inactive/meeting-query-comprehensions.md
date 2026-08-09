# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。上一场我们把 Anthony 原文第 9 章（LINQ Enhancements）前半部的查询增强按特征拆开逐个裁定；这场我们处理同一章的收尾块——"New Query comprehensions"。名义上这是一份建议，实则里面装了七样东西：一个 `Include` 理解（有代码），以及六个只登记了名字、连一行语法都没有的"Not Shown"项（`Skip Until`、`Take Until`、`Skip Last`、`Take Last`、`Left Join`、`Right Join`）。我们用一整场时间讨论了一个问题：**这一整块到底该不该长在语言里，还是本来就该住在 ORM 的库代码里。**

先说结论的倾向，免得读者以为我们又在全盘背书：我们承认关联加载样板（`Include` 链 + N+1 的威胁）是真实的痛点，但我们并不认为"循环体里写一行声明"是它的正确答案。本场最大的功绩是把"语言关键字"与"库功能"之间的那堵墙重新描了一遍。

## Agenda

* [Proposal - 新查询理解 / Query Comprehensions（`Include` 与未展示的 Skip/Take Until、Left/Right Join）](#proposal---新查询理解--query-comprehensionsinclude-与未展示的-skiptake-untilleftright-join)

## Proposal - 新查询理解 / Query Comprehensions（`Include` 与未展示的 Skip/Take Until、Left/Right Join）

_Related: [vblang #104 – Extend `For Each` Statement with Query Comprehensions](https://github.com/dotnet/vblang/issues/104)；[vblang #48 – Multiple `For` or `For Each` Control Variables Per Statement](https://github.com/dotnet/vblang/issues/48)；主线 2014-02-17 LDM（查询子句：范围变量名冲突 #17、`Select` 范围变量 `As` 子句 #50）；Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1527–1562）；ModVB：`meeting-for-each-enhancements.md`（#104 空间的既有裁定）、`proposal-wildcard-lambdas.md`（`.Include(*.Profile.Avatar)` 的竞争方案）、`proposal-query-enhancements.md`（同一章前半部，组 10 并行处理）_

### 场景与缺口

We started from the pain the proposal names, and we do not dispute that it is real.

ORM（最典型是 Entity Framework）里加载关联数据，今天只有两条路：写 `.Include(...).Include(...).ThenInclude(...)` 的链式调用，或者开着延迟加载让代理对象在访问导航属性时偷偷打 SQL。前者把"我要哪些关联"散在查询外面，后者把"打了多少次查询"藏进运行时。两者都离"遍历主数据 + 需要哪些关联"的自然表述有距离。把关联声明放进 `For Each` 循环体内，让意图与迭代同处一地——这个动机我们听懂了。

```vb
' Anthony 原文（L1551–1562）：
' New Query comprehensions:
' Shown: Include
For Each blog In context.Blogs
    Include post In blog.Posts,
            post.Author.Photo,
            blog.Owner.Photo
    ...
Next
```

But——这是本纪要的第一条结构观察——**这不是一份建议，是七份建议的登记表。** `Include` 有一行代码；其余六项（`Skip Until`、`Take Until`、`Skip Last`、`Take Last`、`Left Join`、`Right Join`）在原文里只出现在一行注释里，标注 "Not Shown"，无任何语法。把它们捆进同一份建议，等于让一个已设计的项为六个未设计的项背书，也让六个未设计的项共享一个不该共享的判断。

而且，即便只看 `Include` 本身，我们当场就看见了两个连在一起的问题：**它放在循环体的什么位置？循环体里的语句对"正在枚举的查询"还能做得了什么？** 这两个问题决定了 `Include` 到底是"加载指令"还是"访问表达式"，而我们后面会看到，这决定了它会不会 N+1——也就是它本该治的那种病。

### 候选方案

**PROPOSAL A — `Include` 作为 `For Each` 循环头/循环体理解（Anthony 原文形态）。**

```vb
For Each blog In context.Blogs
    Include post In blog.Posts,
            post.Author.Photo,
            blog.Owner.Photo
    ...
Next
```

`Include` 声明式列出遍历 `blog` 时需要一并加载的关联；`post` 是 `blog.Posts` 的范围变量，后续嵌套路径 `post.Author.Photo`、`blog.Owner.Photo` 用点号连接，语义对位 EF 的 `Include`/`ThenInclude`。多个关联逗号分隔。

**PROPOSAL B — `Include` 作为查询表达式子句（`From ... Include ...`），不碰循环。**

```vb
Let query = From blog In context.Blogs
            Include post In blog.Posts,
                    post.Author.Photo,
                    blog.Owner.Photo
```

查询理解本来就在 `From` 之后逐子句声明；`Include` 作为子句，降级为对源表达式追加 `.Include(...).ThenInclude(...)`，与 EF 的既有形态同构，且不重新定义 `For Each`。

**PROPOSAL C — 不做新语法：维持 EF 链式 `Include`/`ThenInclude` + 延迟加载；把 Anthony 自己的通配符 Lambda 当作主角。**

```vb
' Anthony 原文 L435 已经给了更简洁的形态：
Let users = context.Users.Include(*.Profile.Avatar).ToList()
```

`*.Profile.Avatar` 是 `proposal-wildcard-lambdas.md` 的通配符 lambda（`Function(u) u.Profile.Avatar` 的缩写）。若通配符 lambda 落地，`.Include(*.Profile.Avatar)` 一次表达式就能表达"加载谁的哪条路径"，留在查询语言内，可继续 `.Where(...).ToList()` 组合。

**PROPOSAL D — `Include` 作为循环体内显式语句，语义 = 逐项加载指令（运行时动作）。**

```vb
For Each blog In context.Blogs
    Include blog.Posts           ' 等价 context.Entry(blog).Collection(Function(b) b.Posts).Load()
    ...
Next
```

把 `Include` 当语句，每轮迭代对当前 `blog` 显式加载其关联。语义清楚、不需要改写 `For Each`，但它是**逐项加载**——每一轮一次查询，正是 N+1。

**PROPOSAL E — 家族全量处理：把七项当作一个"#104 范围"的整体设计。** 即把 `Skip Until`/`Take Until`/`Skip Last`/`Take Last`/`Left Join`/`Right Join` 与 `Include` 一并设计成循环头理解。我们在上一场 `for-each-enhancements` 里已经否决过"循环头全量查询理解"（`Order By`/`Select`）——`Select` 在循环头里改变循环体所见的值，是"投影式迭代"，语义与 `For Each` 的"逐元素访问"根本不同，是另一个特征。本场不再重启那个裁定，仅评估这些运算符各自是否该以任何形态进语言。

**PROPOSAL F — 拆分家族：`Include` 单独评；Skip/Take/Join 六项按"与既有运算符的关系"逐个归类（重复、库运算符、或需要独立设计的查询子句）。** We think this is the only honest reading, and the rest of the meeting follows it.

### 权衡：Q&A

**Q1：`Include` 到底在运行时做什么？——本场最重的一问。**

EF 的 `Include` 是**查询提供程序指令**：它在 SQL 层改写查询（JOIN 关联表），在查询被枚举时执行一次。而 `For Each` 是**逐迭代执行**的语句。于是 `Include` 出现在循环里，只有三种读法：

1. **循环体语句读法**（Anthony 示例的缩进看起来就是这样）：每轮迭代对当前 `blog` 执行一次加载。可降级为 `context.Entry(blog).Collection(...).Load()`——这是逐项加载，**每轮一次 SQL**。我们不需要计算都知道这是 N+1，而 N+1 恰恰是 `Include` 这种特性存在的全部理由。用 N+1 去治 N+1，自相矛盾。
2. **循环头源改写读法**：`For Each blog In context.Blogs` 被改写为枚举 `context.Blogs.Include(...).ThenInclude(...)`，查询整体执行一次、JOIN 一次。这在语义上成立——但 `Include` 必须挂在 `In` 源表达式上，而示例把它写在 `For Each` 下一行、与循环体同一缩进，这在 VB 里是循环体语句的形态。也就是说，**语法摆放位置与语义模型对不上**。若要把 `Include` 视为头子句，它应该出现在 `In` 之后、同一物理行或显式续行；示例的摆放会引导每个读者把它读成语句。
3. **延迟查询读法**：整个 `For Each` 变成一次延迟查询，循环体在枚举时才执行。这会重新定义 `For Each` 的执行时机——那是 VB 最古老、最稳定的语句之一，我们不打算为了 ORM 的便利改写它。

无论选哪种，都需要把"循环 vs 查询"的边界重新画一遍。上一场我们已经画过一次（`Where` 过滤我们判定为 `Consider`，因为它是纯布尔谓词、融合降级后仍是逐元素访问；`Select`/`Order By` 全量理解被否决）。`Include` 比 `Where` 远得多：它既不改变迭代条件，也不做投影，它是对**查询提供程序**下指令。这超出循环的职责。

**Q2：这是语言特性，还是 ORM 库特性？**

`Include` 对 `List(Of Blog)` 毫无意义——内存集合没有"关联加载"这回事。`context.Blogs` 必须是 `IQueryable`，且宿主查询提供程序必须理解导航属性（EF、LINQ to SQL 等）。也就是说，**这个关键字的全部语义都由外部库定义**。我们对"库功能长成语言关键字"非常警惕——这正是设计原则 #3（不引入第二种做事方式）与 #6（不为边缘场景加特性）的直撞点。主线"默认跟随 C#，除非有充分理由"（评价标准 1.5 归纳）在这里是安静的支持：C# 把 `Include` 留给 EF 的库 API，语言层从未接手。对 `..\..\vblang\meetings/` 全量检索 `Include`/`ThenInclude`，零命中（我们已核实）。

**Q3：通配符 Lambda 已经把这个场景覆盖了。**

Anthony 自己的第 2.3 节（L435）就写了 `Let users = context.Users.Include(*.Profile.Avatar).ToList()`。如果通配符 lambda 落地（ModVB 已独立提案，组 2 已评审），`.Include(*.Profile.Avatar)` 就是"取 `*.Profile.Avatar` 这条路径"——一次表达式，留在查询里，可组合。**循环体 `Include` 于是成了同一件事的第二种写法，而且是更受限的写法**（见 Q4）。Q3 是我们对 A 的最有力竞争者，也是我们对"为什么不值得为 `Include` 造关键字"的判断主轴。

**Q4：`post` 是什么？它的作用域到哪里？**

`Include post In blog.Posts` 引入了一个名字 `post`。它只在随后的 `Include` 项里用作路径锚点（`post.Author.Photo`）。问题：循环体 `...` 里能看到 `post` 吗？

- 若**能**——`Include` 就是一个声明范围变量的语句，`post` 的生存期、与 `blog` 的并列、与 #48 多控制变量的关系都要定义，这是个不小的语义面。
- 若**不能**——`post` 只是为了给路径命名，那为什么用 `In` 这个与 `For Each`/`From` 强绑定的词？`In` 在 VB 里明确表示"从集合取元素"，`post` 既不是元素也不是投影，它是个"路径变量"。

无论哪边，都是建议没有回答的。我们倾向于：若保留，`post` 只在该 `Include` 语句内可见（路径锚点），不进循环体——但这让 `In` 的选用显得名不副实。

**Q5：逗号列表与 #48 的碰撞。**

主线 2017-12-06 对 #48（多 `For`/`For Each` 控制变量）的记录是：

> "**Approved-in-Principle**. None of us could think of a good reason why this doesn't already work. Allowing the `For Each` case is virtually required by #104."

若 #48 落地，`For Each a, b In src` 里的逗号是"多控制变量列表"。而 `Include post In blog.Posts, post.Author.Photo, blog.Owner.Photo` 里的逗号混着两种东西：一个是**带 `In` 的范围变量声明**，两个是**裸导航路径**。同一份建议、两套逗号语义，且都与 #48 的逗号语义不同。这是解析地雷：`Include` 的逗号列表要么定义为第三种语义，要么与 #48 统一——而统一意味着 `post.Author.Photo` 必须能被解析成"控制变量声明"，说不通。

**Q6：`Skip Until` / `Take Until`——给既有运算符换名字。**

VB 查询理解早已支持 `Skip`、`Take`、`Skip While`、`Take While` 等子句；2014-02-17 主线 LDM 讨论范围变量名冲突时，明确把 `Distinct`、`Skip`、`Take` 称为 "innocuous clauses"：

> "Design2: If the last Select is followed only by the innocuous clauses (Distinct, Skip, Take) and if the Select has only one item, then skip name generation entirely."

`Take Until p` ≡ `Take While Not p`；`Skip Until p` ≡ `Skip While Not p`。若 "Until" 意为"直到谓词为真停止"，它就是 `While` 的否命题——**同一个运算符的第二种名字**，直接违反原则 #3。若 "Until" 在谓词时序上另有含义（例如取"谓词首次为真"之后的元素），建议完全没定义。我们在 `Suspect`：这六个名字更像是作者凭直觉列出的"听起来有用"清单，而非设计。

**Q7：`Skip Last` / `Take Last`——真新，但是库的活。**

这两个在标准 LINQ 里不存在，属于序列操作（需要预知长度：要么先 `Count()`，要么环形缓冲）。作为扩展方法（System.Interactive 等已有 `TakeLast`）完全可实现；作为语言关键字，除了少写一个 `.` 没有任何增量。且对 `IQueryable`，`SkipLast`/`TakeLast` 的 SQL 翻译（`OFFSET`/窗口函数）完全由提供程序定义——又是外部库语义。语言不该为此造关键字。

**Q8：`Left Join` / `Right Join`——VB 已有 `Group Join`，且建议一个字的语法都没有。**

VB 查询理解里，`Group Join ... Into g` 产生层级结果，配合 `From item In g.DefaultIfEmpty()` 可以表达左连接；右连接几乎无人要求（且与 SQL 不对称——换边即成左连接）。C# 两者都没有。主线按"默认跟随 C#"会把它留给库。而且最实在的一点：**建议没有给出 `Left Join`/`Right Join` 的任何语法**。没有语法就没有设计，没有设计就没有可评对象。把名字登记进建议不等于提出特性。

**Q9：`Include` 绑定到什么符号？语义模型怎么回答？**

若做 B（查询子句），`blog`、`post` 是表达式树片段而非运行时值；语义模型必须把 `post.Author.Photo` 暴露为一条 `PropertySymbol` 链（`Post.Author` → `Author.Photo`），IDE 要在 `Include post In blog.Posts,` 这个断行中间对 `post.` 做成员补全。可做，但是一整块编译器 + IDE 工作，且全部押在"提供程序会翻译"这一假设上。

### 深度追问：LDM 拷问清单

我们按评价标准第五部分的追问清单逐条过。

#### 1. 语法 / 文法歧义

- `Include` 作为上下文关键字：`Include` 在 VB 里今天可能是用户的局部函数名、成员名。上下文关键字的解析靠位置消歧，可行，但建议无文法（BNF），`For Each` 语句里突然插入一行 `Include ...` 如何进入文法树完全没定义。
- **摆放歧义**（Q1）：示例缩进把 `Include` 放在循环体形态，而语义（若要成立）必须是循环头形态。这是"语法形态与语义模型错位"——比普通歧义更糟，因为它会让大多数读者读到错误的语义。
- 逗号列表混型（Q5）：范围变量声明 + 裸路径 + #48 逗号语义，三套语义争夺同一个逗号。
- 无 body 区分：`Include` 若作为循环体语句，与普通语句（`Dim`、`Call`）靠什么区分？关键字 `Include` 是唯一信号，需进文法。

#### 2. 角案例与边界语义

- **多个 `Include` 的顺序与去重**：建议自己在 Unresolved 里问了（"多个 Include（逗号分隔）的执行顺序与去重规则"）。同一路径出现两次是否去重？`blog.Posts` 与 `blog.Posts.Author.Photo` 的包含关系怎么算？EF 会去重，但语言层没有规则。
- **嵌套路径深度**：`post.Author.Photo` 到几层？任意深度意味着语言要承认任意深度的导航路径表达式树，并在 `Include` 列表里解析"哪个点号属于路径、哪个逗号属于列表"。
- **空导航**：`blog.Owner` 为 `Nothing` 时 `blog.Owner.Photo` 是加载指令还是访问？若是访问，`Nothing` 解引用异常；若是加载指令，`Nothing` 无碍——语义取决于 Q1 的读法，而读法未定。
- **代理 vs 已加载**：`blog.Posts` 是延迟加载代理集合还是已加载的 `ICollection`，行为完全不同；语言不感知。

#### 3. 作用域与绑定

- `post` 的作用域（Q4）未定义。
- 绑定目标：`blog.Posts` 应绑定到 `Blog.Posts` 属性符号；嵌套 `post.Author.Photo` 绑定到 `Post.Author` → `Person.Photo`。语义模型需返回属性链，而不是一个值。这是表达式树式绑定，与普通成员访问的运行时求值不同——需显式区分"绑定用于翻译"与"绑定用于求值"。
- `In` 的语义被挪用（Q4）。

#### 4. 与既有特性的交互

- **#48 多控制变量**：逗号语义冲突（Q5），是最直接的交互面。
- **`for-each-enhancements` 的 `Where`（我们裁定 `Consider`）**：`For Each blog In context.Blogs Where blog.IsPublished Include ...` 能否组合？`Where` 是布尔谓词、降级为 `If`，`Include` 是提供程序指令，两者在降级模型里不在同一层。若同时落地，需要定义它们如何排序——这份建议提都没提。
- **查询表达式**：`From ... Include ...`（B）与 `From ... Where ...`、`Join`、`Group Join` 的子句序列如何共处，需完整查询文法扩展。
- **通配符 lambda**：`.Include(*.Profile.Avatar)` 是竞争方案（Q3），也是我们推荐的出路。
- **延迟加载代理**：`Include` 与 EF 的 `LazyLoadingEnabled` 互斥/协作关系是库语义，语言层无法表达。
- **晚期绑定**：见第 6 条。

#### 5. Breaking change 与兼容性

- 无直接破坏：`Include` 是全新上下文关键字，今天写 `Include ...` 在循环体里是语法错误，错误变程序。
- 但"循环变延迟查询"的读法（Q1 读法 3）若被采纳，是**重新定义 `For Each` 的执行时机**——对既有语义根基的破坏，必须显式排除，且这份建议没有排除。
- 上下文关键字 `Include` 与现有标识符/成员名的冲突需要全量扫描（`Imports System` 下用户类若有 `Include` 成员）。`Probably`：位置消歧可解，但需验证。

#### 6. Option Strict / 编译选项分叉

- `Include post In blog.Posts` 的路径要能翻译给提供程序，必须**早期绑定**：`blog.Posts` 的静态类型必须是 `Blog` 的属性。`Option Strict Off` + 晚期绑定下 `blog.Posts` 是 `Object`，无法合成表达式树——`Include` 在宽松路径下要么不工作，要么退化为运行时访问（又回到 Q1 的语义泥潭）。
- 建议对两路径只字未提。若保留特性，必须在 spec 里写明"`Include` 的路径仅限早期绑定，宽松模式报诊断"。

#### 7. IDE / IntelliSense 影响

- 断行中间的成员补全：`Include post In blog.Posts,` 之后输入 `post.Author.`，IDE 要补 `Author`；`post` 是路径锚点，补全来源是表达式树绑定而非局部变量。这是全新的 IDE 交互。
- 错误文案：无效路径（`blog.NotANavigation`）、`Include` 用在非 `IQueryable` 源上的诊断，都需要新设计。
- 重构：重命名 `Blog.Posts` 要传播到 `Include` 路径里——表达式树式绑定可以支持，但不在既有重构管道里。

#### 8. 数据 / 普遍性

- EF 用户真实，但**没有量化数据**。对照 Implicit-default-optional 的 85% 统计标准，这里一条都没有。我们不是在说痛点是假的；我们是在说没法量。
- 更尖锐的：即便 EF 用户，写深层 `Include` 链的多是 ORM 重度用户，而主线自我定位的"数十万安静客户"主力是业务 CRUD。`Suspect`：这个特性服务的是一小撮人，且那一小撮人已经有 `.Include(*.Profile.Avatar)` 的更好出路。
- 对 `..\..\vblang\meetings/` 的 `Include`/`ThenInclude` 检索零命中——主线从未把它当语言候选。

#### 9. 更简替代

- `.Include(*.Profile.Avatar)`（通配符 lambda）——一次表达式、留在查询里、可组合。这是压倒性的替代。
- 现有链式 `.Include().ThenInclude()`——丑但确定。
- 延迟加载——藏 N+1，但开箱即用。
- 若一定要语言化，`From ... Include ...`（B）比循环体形态少碰 `For Each`、少碰 #48 逗号、少碰执行时机问题。
- Analyzer/代码片段（如 `Inc` 代码片段或 `Include` 重构）可以在不动语言的前提下减轻样板。

#### 10. 复杂度 / 成本 / 优先级

- A 形态成本：文法 + 循环头源改写（或语句降级）+ 表达式树路径合成 + 逗号列表消歧 + IDE 补全 + 与 #48 对表 + 与 EF 提供程序配合验证。**特性级成本，服务 ORM 利基**。
- B 形态成本略低（不碰 `For Each`），但仍需查询文法扩展 + 表达式树 + 提供程序契约。
- C（通配符 lambda 领衔）成本已被组 2 的通配符提案消化，此处**边际成本为零**。
- 优先级：通配符 lambda 明显高于 `Include`；`Skip Until` 等六项没有优先级可言——它们连语法都没有。

#### 11. 运行时 / CLR 硬约束

- 无 CLR 约束：`Include` 的表达式树合成是编译期动作，不触 PEVerify、不触存储规则。
- 但 A 形态有**运行期性能陷阱**：循环体语句读法 = 每轮一次 SQL（N+1），这是正确性/性能层面的坑，比 CLR 约束更实在。

#### 12. 值不值得做

- 价值：真实但利基，且被通配符 lambda 分走大半。
- 成本：特性级，且需 EF 提供程序配合（语言层单方面做不完）。
- 风险：循环/查询边界重画、#48 逗号冲突、宽松路径分叉、N+1 陷阱。
- **结论：不值得为 `Include` 造语言关键字。** 若家族整体打包，我们会建议不做——六个无设计的名字不该拖一个已设计的名字，反过来也不该。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）**——新关键字，error→program，表面通过；但"循环变延迟查询"读法是语义根基破坏，必须显式排除。勉强通过。
- **原则 2（保持 VB-like）**——`Include post In blog.Posts` 读起来像英语，措辞本身是 VB 味的。但"像 VB"的是壳，语义是 EF 的。壳不抵芯。
- **原则 3（不引入第二种做事方式）**——**本特性最重的败点**。`Include` 是 `.Include().ThenInclude()` 的第二种写法；`Skip Until`/`Take Until` 是 `Skip While`/`Take While` 的第二种名字；而通配符 lambda 已经提供了第一种写法更优雅的形态。三重复合违规。
- **原则 5（读起来像英语、对新手友好）**——措辞通过；新手语义负担不通过（`Include` 到底加载还是访问，说不清）。
- **原则 6（不为边缘场景加特性）**——ORM 专属 + 无量化数据，败。
- **原则 8（不与既有语法冲突）**——与 #48 逗号列表冲突（Q5），`In` 语义被挪用（Q4）。
- **原则 9（消除常见样板）**——样板是真实的（`Include` 链），但消除样板的最短路径是通配符 lambda，不是本建议。
- **原则 10（冗长只在有用时是美德）**——`Include` 关键字在"让意图可见"处保留了冗长，符合；但为此要付整套文法。

对照主线表（2.3）：**#104（`For Each` 查询理解）在主线从未被设计出具体理解**——2017-12-06 只在 #48 语境里提了一句 "virtually required by #104"，没有任何 `Where`/`Include` 的设计；2014-02-17 的查询子句讨论（#17 范围变量名、#50 `Select` 的 `As` 子句）走的是"理解翻译为标准运算符"的路子。`Include` 在对照表**无对应行**，是 **Anthony 独立延伸**，且方向与主线"默认跟随 C#"相悖（C# 把 Include 留给库）。`Skip Until`/`Take Until`/`Left Join`/`Right Join` 与主线查询子句的关系：VB 已有 `Skip`/`Take`/`Skip While`/`Take While`/`Join`/`Group Join`，"Until/Last/Left/Right" 是新增名目。与 `for-each-enhancements` 的裁定（`Where`=`Consider`、全量循环头理解=否决）必须保持一致——本建议若是 A 形态，等于绕过那个裁定重新把查询理解塞回循环头。

### RESOLUTION:

**RESOLUTION:** We split the document. 七项不再作为一个家族裁定；`Include` 与六个 "Not Shown" 项各自归位。

1. **`Include` 循环内理解（A 形态）— `Reject`（就当前形态而言）。** 原因逐条：① 循环体语句读法 = 每轮一次 SQL（N+1），自相矛盾；循环头源改写读法 = 语法摆放与语义模型错位；"循环变延迟查询"读法 = 重新定义 `For Each`，不可接受。② 语义完全由外部 ORM 提供程序定义，是库功能披着语言关键字（原则 #3/#6）。③ 通配符 lambda 的 `.Include(*.Profile.Avatar)` 已覆盖同一场景且更简洁。④ 与 #48 逗号列表冲突。若未来要重审，唯一的诚实载体是 **B 形态（查询表达式子句 `From ... Include ...`）**，降级为 `.Include(...).ThenInclude(...)` 链，并需与提供程序定义显式契约——该形态列为 `Table`，本场不启动。
2. **`Skip Until` / `Take Until` — `Reject`。** 它们是既有 `Skip While` / `Take While` 的否命题（`Take Until p` ≡ `Take While Not p`），是同一运算符的第二种名字（原则 #3）。用户若想要，那是查询运算符的命名问题（库扩展或重命名），不是新理解。`OPEN QUESTIONS`：若作者意图的 "Until" 在谓词时序上与 "While" 不同，需先定义差异再谈。
3. **`Skip Last` / `Take Last` — `Table`（作为库运算符）。** 真新但属序列操作，扩展方法可实现；对 `IQueryable` 的 SQL 翻译由提供程序定义，语言不该为此造关键字。
4. **`Left Join` / `Right Join` — `Table`（作为查询子句研究项）。** VB 已有 `Group Join` 可表达左连接；`Right Join` 近乎无人要求（换边即左连接）；C# 两者皆无（主线默认跟随）；且建议未给任何语法——无语法即无可评。若未来研究，`Left Join` 单独设计为 `From ... Left Join ...` 子句，`Right Join` 不单设。
5. **家族不打包。** "New Query comprehensions" 作为整体不成立；六个 "Not Shown" 项必须从建议中移除或各自成文。We are not saying the pains are fake; we are saying a registry of names is not a proposal.

**Implication:**

- 返工建议原文：把 `Include` 项改为**查询表达式子句**形态并补降级模型（`From ... Include ...` → `.Include().ThenInclude()`）；六个 "Not Shown" 项要么删除、要么逐个成文（`Skip Until`/`Take Until` 说明与 `While` 的差异；`Skip Last`/`Take Last` 移入库运算符提案；`Left Join`/`Right Join` 给出完整子句文法）。
- 与 wildcard-lambdas 对表：`.Include(*.Profile.Avatar)` 场景应在通配符 lambda 提案里作为首要示例 champion，而非在这里。
- 与 for-each-enhancements 对表：循环头理解空间维持上一场裁定（`Where`=`Consider`、全量理解=否决）；本建议 A 形态不得绕过该裁定。
- 与 #48 对表：若未来设计任何"循环头子句"，必须先解决逗号语义冲突。
- 与 query-enhancements 对表：该建议（组 10）覆盖第 9 章前半部（元组解构、`Select FirstOrDefault()`、目标类型化 `Select`、BC36606），与本场无重叠；第 9 章后半部至此裁定完毕。
- 最小原型（若走 B 形态）：查询子句文法 + 表达式树路径合成 + `.Include/.ThenInclude` 降级 + 语义模型属性链 + IDE 补全；验证需一个真实 EF 提供程序参与。
- 未决问题移交至 OPEN QUESTIONS。

**三态判定：** 文档整体 `Table`。`Include`（A 形态）`Reject`；`Include`（B 形态，查询子句）`Table`；`Skip Until`/`Take Until` `Reject`；`Skip Last`/`Take Last` `Table`（库）；`Left Join`/`Right Join` `Table`（研究）。PROPOSAL C（通配符 lambda 领衔）作为已采纳方向留档，D（逐项加载语句）与 E（家族全量）按"否决/暂缓的备选方案"留档。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Skip Until`/`Take Until` 的 "Until" 是否与 `While` 有谓词时序差异（若作者意图如此，需定义差异；本仓库无法核实，Anthony 原文只列了名字）。
- `OPEN QUESTIONS`：若 `Include` 走 B 形态，提供程序契约的边界——语言只负责降级 `.Include().ThenInclude()`，还是语言还要认识"导航路径"这一概念？倾向：只降级，不识别。
- `OPEN QUESTIONS`：`Left Join` 作为查询子句的完整文法与语义（`On` 键、无匹配时的 `Nothing` 行为）——仅有方向，无设计。
- `TODO`：量化 EF/ORM 用户在 ModVB 目标人群中的占比，为普遍性补证据。
- `TODO`：全量扫描现有 VB 代码库，确认 `Include` 作为标识符的冲突面（`Probably`：位置消歧可解，未核实）。
- `Follow-up`：与 wildcard-lambdas 团队确认 `.Include(*.Profile.Avatar)` 作为该提案的 canonical 示例。
- `Follow-up`：把 `Skip Last`/`Take Last` 移交库运算符/框架提案（非语言）。

### 状态

- **LDM 状态：`Table`**；`Include` 循环内形态 `Reject`，查询子句形态 `Table`；`Skip Until`/`Take Until` `Reject`；`Skip Last`/`Take Last` `Table`（库）；`Left Join`/`Right Join` `Table`（研究）。
- **三态判定：Table** — 痛点真实、出路已有（通配符 lambda），语言关键字不值得；若 ORM 生态或数据证明需求，复活信号是"B 形态查询子句 + 提供程序契约 + 原型"。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-query-comprehensions.md` — 新查询理解（`Include` 关联加载 + 未展示的 `Skip Until`/`Take Until`/`Skip Last`/`Take Last`/`Left Join`/`Right Join`）。
- 来源：Anthony 原文第 9 章 "Language Integrated Query (LINQ) Enhancements"（`..\AnthonyDesign_wordpress.txt` L1527–1562，`Include` 示例与 "Not Shown" 注释原样摘录；L435 通配符 lambda `Include(*.Profile.Avatar)` 竞争形态）。
- 配方目标：把"遍历主数据 + 需要的关联"同处声明，以声明式替代 EF 链式 `Include`/`ThenInclude` 与延迟加载。

### 五维评分表

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。动机（ORM 样板）真实但无量化数据；`Include` 示例只演示"意图"不演示"行为"——执行语义（每轮加载 vs 查询改写 vs 延迟查询）未定义，示例无法演示改进；六个 "Not Shown" 项零设计、零效果可验 | 已检查 | 无原型（状态行占位链接）；核心执行语义未定使效果悬置；六项占建议主体却无任何可演示内容 |
| 特性 | **2/5** | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`Include` 语义整体来自 EF（外部库），语言只借了壳；七个项捆绑、血缘混杂（EF、SQL 连接、序列运算符、既有 VB `Skip While`/`Take While` 的否命题）；`In` 语义被挪用（原则 #8） | 已检查 | 与原则 #3 直接冲突（`.Include()` 的第二种写法）；`Skip Until`/`Take Until` 是既有运算符换名；无 VB 基因增量 |
| 品质 | **2/5** | 介于锚点 2–3。六章节模板齐全、示例与 Anthony 原文逐字一致、"Not Shown" 诚实标注（未虚构语法——这是加分）；但核心项执行语义未定义到不可实现、六个"特性"以名字登记而非设计（边界含糊的性质最重）、无文法（BNF）、无 Compatibility、无 Option Strict 分叉，Drawbacks/Alternatives/Unresolved 各 3 条泛泛 | 已检查 | 红旗：无文法；无兼容性分析；执行语义（Q1 三读法）这一根本风险在 Drawbacks 里提出却未展开；六项"Not Shown" 使建议主体名存实亡 |
| 属性 | **2/5** | 锚点 2："某关键维度受损且无应对"。风（演化一致性）受损：与 #48 逗号冲突、与 for-each-enh 裁定潜在绕过、与通配符 lambda 竞争未识别；暗风险：N+1 陷阱（循环体读法）、外部 ORM 绑定（EF 语义单点依赖）、循环/查询边界重画，无对冲设计 | 已检查（预测待定） | 风/暗双损无应对；对 VBScript.NET 的差异化无助益（C# 已把 Include 留给库）；实际影响须"已采纳"后定 |
| 炼金成分 | **3/5** | 锚点 3："部分来源未标注；标注与影响有偏差"。主要成分可辨认：Anthony 第 9 章、EF `Include`/`ThenInclude` 对应（建议明说了"与 EF 语义对应"）；但未标注：与自身 wildcard-lambda 建议（L435）的竞争关系、VB 既有 `Skip While`/`Take While` 运算符（成分重复）、主线 #104 关联、"库 vs 语言"定位 | 已检查 | 来源与影响有偏差：借鉴 EF 却未论证为何语言层接手；未交叉引用 wildcard-lambdas 提案；`Skip Until`/`Take Until` 与既有运算符的重复未声明 |

### 设计原则对照

- **与 VB 基因：偏离为主。** 措辞 `Include post In blog.Posts` 读起来像英语（原则 #5 通过）、新关键字无破坏（#1 通过）；但原则 #3（第二种做事方式）三重违规（`Include`、`Skip Until`/`Take Until`、与通配符 lambda 竞争）、原则 #6（ORM 利基 + 无数据）失败、原则 #8（#48 逗号冲突、`In` 挪用）失败。核心是"库功能长成语言关键字"。
- **与主线关系：Anthony 独立延伸，且与主线方向相悖。** 主线对 #104 从未设计出具体理解（2017-12-06 仅在 #48 语境提及）；2014-02-17 的查询子句讨论把理解当"翻译为标准运算符"；主线"默认跟随 C#"，C# 把 `Include` 留给库。`Skip Until`/`Take Until`/`Left Join`/`Right Join` 与既有查询子句（`Skip`/`Take`/`Skip While`/`Take While`/`Join`/`Group Join`）部分重复、部分无主线先例。与 for-each-enhancements（#104 空间已裁定）、wildcard-lambdas（竞争方案）、query-enhancements（同章前半部）交叉。
- **破坏性变更：无直接**（新上下文关键字，error→program）；但"循环变延迟查询"读法若被采纳是 `For Each` 执行时机的语义根基破坏，必须显式排除；上下文关键字 `Include` 与既有标识符的冲突面未扫描。

### 总评

- **达成程度：未达成（作为语言特性）。** `Include` 循环内形态自相矛盾（N+1 治 N+1 或重定义 `For Each`）；六个 "Not Shown" 项无设计可评；家族整体是"名字登记表"而非提案。痛点真实，但最优解（通配符 lambda `.Include(*.Profile.Avatar)`）已经存在于 Anthony 自己的文本里，且在另一份建议的轨道上。
- **LDM 三态建议：Table**。`Include`（A 形态）`Reject`；`Include`（B 形态，查询子句）`Table`——复活信号：EF/ORM 用户量化数据 + 提供程序契约 + 原型；`Skip Until`/`Take Until` `Reject`（既有运算符否命题）；`Skip Last`/`Take Last` `Table`（库）；`Left Join`/`Right Join` `Table`（研究）。
- **主要问题**：(1) `Include` 执行语义未定义且三种读法两种自杀（N+1 / 重定义 `For Each`）；(2) 语言与 ORM 的边界未界定，关键字语义完全依赖外部提供程序；(3) 六个 "Not Shown" 项以名字登记冒充设计；(4) 与 #48 逗号、for-each-enh 裁定、wildcard-lambda 竞争均未识别；(5) 无文法、无兼容性、无 Option Strict 分叉。

### 返工建议

- **范围切分**：七项拆开。`Include` 单独成文并改为**查询表达式子句**形态（`From ... Include ...`），配降级模型（→ `.Include(...).ThenInclude(...)`）与提供程序契约；六个 "Not Shown" 项从建议中移除或逐个成文。
- **补充章节**：文法（BNF：`Include` 子句形态、逗号列表 vs #48 逗号消歧、路径表达式文法）；执行语义（明确排除"循环变延迟查询"读法）；Compatibility（上下文关键字冲突面扫描、`langversion` 门控）；`Option Strict On/Off` 双路径（`Include` 路径限早期绑定，宽松模式诊断）。
- **补充证据**：若走 B 形态，最小原型（查询子句 + 表达式树路径合成 + `.Include/.ThenInclude` 降级 + 语义模型属性链 + IDE 补全），需一个真实 EF 提供程序参与验证；EF/ORM 用户占比数据；循环体读法 N+1 vs 查询改写的对比基准。
- **未决问题处理**：`post` 作用域（若保留，仅限 `Include` 语句内的路径锚点，不进循环体）；多个 `Include` 去重与顺序（委托提供程序，语言不定义）；嵌套路径深度（与表达式树支持对齐）；`Left Join`/`Right Join` 若无完整文法则从建议移除。
- **设计探索**：与 wildcard-lambdas 联合——让 `.Include(*.Profile.Avatar)` 成为通配符提案的 canonical 示例，本建议的样板动机随之消化；`Skip Last`/`Take Last` 移交库运算符提案。

---

## 附录：C# 生态与互操作考量

本提案主题是「新查询理解」（`Include` 与六个未展示项），在 C#/CLR/.NET 生态里的对应主线是 **LINQ 查询表达式**。索引（`..\..\csharplang-index.md`）未为查询表达式单列一条 T 条目——该领域在 C# 里长期休眠，直到 2025 年才被 left/right join 提案打破；所以本节以 csharplang 原文为主、索引为辅。

### 相关 C# 现实方向

**R1 — C# 查询表达式已休眠约 18 年，2025 年才破冰。** 查询表达式随 C# 3.0 / VS 2008 引入（`Language-Version-History.md` 的 C# 3 清单含 "[Query expressions, a.k.a LINQ (Language Integrated Query)]"，→ `Language-Version-History.md`）。此后 C# 从未扩展查询语法。left-right-join 提案自述：

> "It's worth noting that C# query expression support for LINQ hasn't evolved in a long time - this would be the first change in quite a while. At the same time, AFAIK there hasn't been any formal deprecation/archiving of this area of the language."（→ `proposals\left-right-join-in-query-expressions.md`，Drawbacks）

**R2 — BCL 先行，语言随后：.NET 10 已把 `LeftJoin()`/`RightJoin()` 放进 System.Linq。** 提案 Motivation 原话：

> "In .NET 10, new `LeftJoin()` and `RightJoin()` methods have been introduced into System.LINQ; see https://github.com/dotnet/runtime/issues/110292 for the API proposal, discussion, and performance information and benchmarks."（→ `proposals\left-right-join-in-query-expressions.md`，Motivation）

**R3 — 2025-02-12 LDM 通过 left/right join 查询子句，目标 C# 14。** 提案要做的正是把 `join` 子句扩展出可选 `left`/`right` 修饰符：

> "Introduce `left` and `right` modifiers to the LINQ query expression syntax `join` clause"（→ `proposals\left-right-join-in-query-expressions.md`，Summary）

文法改动（→ 同文件，Detailed design）：

```
join_clause
-    : 'join' type? identifier 'in' expression 'on' expression 'equals' expression
+    : ('left' | 'right')? 'join' type? identifier 'in' expression 'on' expression 'equals' expression
```

LDM 的结论：

> "We will proceed with this proposal, hopefully in the C# 14 timeframe (barring any other time constraints)."（→ `meetings\2025\LDM-2025-02-12.md`，Conclusion）

**R4 — 查询语法被定义为纯语法重写，provider 以方法调用形态消费。** LDM 与提案反复强调这一模型：

> "they're defined as pure syntactic rewrites, not involving semantics."（→ `meetings\2025\LDM-2025-05-05.md`）

> "Since query expressions are spec'ed as a syntactic rewrite, C# actually lets you use a *type* as the query source, as long as it has static members for the query operators you use!"（→ `meetings\2022\LDM-2022-04-06.md`）

且 left/right join 明确不需要表达式树改动，provider 无阻塞：

> "Note that the proposed `join` modifiers do not require any LINQ expression tree changes, as they're represented via existing MethodCallExpression's which reference the new `LeftJoin()` and `RightJoin()` methods. There is thus nothing blocking supporting them from LINQ providers (such as EF Core)."（→ `proposals\left-right-join-in-query-expressions.md`，Drawbacks）

**R5 — provider 掉队是 LDM 唯一的真实顾虑，且 LDM 点名 VB 在 `distinct` 上的相对优势。** 新方法在 BCL 里迟早存在，但并非所有 provider 都会翻译：

> "We can't really avoid query providers failing on these new joins; the methods will exist in the BCL regardless, and various query providers might fail on them no matter what."（→ `meetings\2025\LDM-2025-02-12.md`）

LDM 权衡后认为「语法缺席伤用户 > provider 报错伤用户」：

> "for example, C# does not support `distinct` in query syntax today, like VB does, but if increased `join` options are successful, that may be enough of a signal to add further operators; conversely, if this ends up causing significant increases in errors for users and lots of pain, we know that further changes here are harder than they appear at first glance."（→ `meetings\2025\LDM-2025-02-12.md`）

**R6 — Include 在 C# 生态里一直是库 API，语言层从未接手。** EF 的 `.Include()/.ThenInclude()` 属 dotnet/efcore 生态；我们对 `..\..\csharplang\proposals\` 检索 `Include`/`ThenInclude`，无任何语言特性提案命中（`include` 仅以普通英文词出现在无关文件）。这与本场 Q2 的 vblang 侧检索（`..\..\vblang\meetings\` 零命中）互相印证。

### 现实 vs 提案

- **`Include`（A 形态，循环内理解）— `Reject`，与 C# 现实兼容。** C# 把 `Include` 留在库层 18 年未语言化（R6）；本场否决循环内形态，方向与 C# 现实一致，无冲突。
- **`Include`（B 形态，查询子句）— `Table`，需桥接。** C# 现无 `Include` 子句，且 C# 对「扩展查询语法」本身都极谨慎（R3 是 18 年来第一次，还带 R5 的 provider 掉队顾虑）。VB 若独自加 `From ... Include ...`，是超越 C# 的 VB 特色面，须在「默认跟随 C#」主线下自证。桥接要点：降级目标必须是 provider 已认识的普通方法调用形态 `.Include(...).ThenInclude(...)`——这正是本场 B 形态的降级模型，也与 R4 的「纯语法重写」哲学同构。
- **`Skip Until`/`Take Until` — `Reject`，与 C# 生态脱节（无对应物）。** C# 查询语法连 `Skip`/`Take` 子句都没有（这些在 C# 是 BCL 方法）；`Until` 更是 VB 内部对既有 `Skip While`/`Take While` 的换名问题，与 C# 生态无交集。判定不变。
- **`Skip Last`/`Take Last` — `Table`（库），与 C# 兼容。** C# 生态的序列尾部操作一贯是 BCL/扩展方法（R2 显示的「BCL 先行」模式正是同类）。判定不变。
- **`Left Join`/`Right Join` — `Table`（研究），与 C# 生态强相关，需桥接。** 这是七项里与 C# 现实关系最重的一项：C# 正在把两者做成一等查询语法 + BCL 方法 + EF 10 翻译。VB 的 `Group Join` + `DefaultIfEmpty()` 早已能表达左连接——正是 C# 提案引用并嫌弃的「组合运算符」规避形态（"It's complicated, requiring combining multiple different LINQ operators in a specific way to form a complex construct, and is easy to accidentally get wrong."，→ `proposals\left-right-join-in-query-expressions.md`，Motivation/Drawbacks）——这是 VB 查询语法的相对优势；但 C# 一旦落版，生态话语权会向 `left join` 语法倾斜。桥接点见下节。

### 对 VBScript.NET 的适应建议

- **默认安全、按需动态：查询子句锁早期绑定。** `Include`/`Join` 路径要降级为表达式树/方法调用，`Option Strict Off` 下源是 `Object` 无法翻译（本场 §6 已述）。.vbx 应保持「查询理解只认早期绑定，宽松模式报诊断」，与 C# 查询表达式无 `dynamic` 源一致。
- **source-gen 桥 / 编译期降级：查询按「纯语法重写」落标准方法链。** .vbx 脚本编译为受管程序集时，任何查询子句一律降级为 provider 已消费的普通方法调用（`.Include().ThenInclude()`、`LeftJoin()`、`GroupJoin().SelectMany(...)`），不引入运行时查询解释器——这样 EF/IQueryable provider 零新契约即可消费，也与 R4/R3 的 C# 模型同构。解释执行（scripting-interpreted）若支持查询，应显式 opt-in 且限内存集合（LINQ to Objects），避免与 AOT/trimming 摩擦（索引 T5/T7）。
- **识别新元数据：`LeftJoin`/`RightJoin` 无新元数据，但可空性契约必须镜像。** 两者是普通泛型扩展方法，编译器不需要认识新特性标志；但 C# 提案给它们定义了**范围变量可空性规则**：

  > "In contrast, since `left join` returns outer elements which don't have correlated inner elements, the inner range variable it introduces is always nullable"（→ `proposals\left-right-join-in-query-expressions.md`，Detailed design）

  > "`right join` operates in a similar way, with one important difference: the already-existing, outer range variable is made nullable, rather than the inner range variable"（→ 同文件）

  .vbx 若实现 `Left Join`/`Right Join` 子句，必须对齐这套可空化语义，否则编译产物与 C#/EF 的翻译结果不一致。广义的「识别 C# 新元数据」（决策文件 M8 的 RequiresUnsafe/MemorySafetyRules）与查询主题无直接关系，此处不展开。
- **provider 消费契约：语言只重写，不识别提供程序语义。** C# 提案的纪律是语言只负责把子句翻译成方法调用（"The `left` and `right` modifiers of the `join` clause change the translation to the `LeftJoin()` and `RightJoin()` methods respectively, instead of `Join()`. Every other aspect of the translation remains the same."，→ `proposals\left-right-join-in-query-expressions.md`），导航路径/去重/顺序等语义委托 provider。这与本场 OPEN QUESTIONS「`Include` 若走 B 形态，倾向只降级、不识别导航路径」的倾向一致，.vbx 应沿用。

### 对既有 RESOLUTION/三态判定的影响

- **正文两处「C# 两者皆无」的前提已过时，需修正。** 正文 Q8 与 RESOLUTION 均断言「C# 两者都没有 / C# 两者皆无（主线默认跟随）」。截至本场日期（2026-08-08），该前提已不再成立：C# LDM 已于 2025-02-12 批准 `left join`/`right join` 查询子句（R3）。本附录即为此修正而设——RESOLUTION 第 4 条引用「主线默认跟随 C#」时，应改为「主线正在扩展查询语法，VB 需评估是否跟进」。
- **整体三态维持 `Table`，但 `Left Join`/`Right Join` 的复活信号被外部证据强化。** 原 RESOLUTION 把两者列为「研究项」、复活信号是「EF/ORM 用户量化数据 + 提供程序契约 + 原型」。C# 现实提供了比数据更硬的信号：.NET 10 BCL 已含 `LeftJoin()`/`RightJoin()`、C# 提案已给出完整文法与可空性规则、EF 10 跟进。复活信号可更新为「跟随 C# 8947 的设计落版 + provider 支持 + VB 主线跟随」。
- **`Right Join`「近乎无人要求」的判断需局部修正。** 本场 Q8 认为右连接无人要求（换边即成左连接）；但 C#/BCL/EF 10 的路线是三件套（Left+Right+EF）一起做，C# 提案的 `right join` 同样设计了独立语法与可空性规则。若 VB 未来研究 `Left Join`，不应再假设 `Right Join` 可忽略——至少要与 C# 的对称设计对齐（换边降级或独立子句）。
- **`Include` B 形态的降级目标与 C# provider 形态一致**，Table 判定不受影响；`Skip Until`/`Take Until` Reject、`Skip Last`/`Take Last` Table（库）均不受 C# 现实影响。

### 引用纪律与未核实项

- 本节 C# 原文均逐字摘自 `..\..\csharplang\` 下列文件，来源已在行内标注：`proposals\left-right-join-in-query-expressions.md`、`meetings\2025\LDM-2025-02-12.md`、`meetings\2025\LDM-2025-05-05.md`、`meetings\2022\LDM-2022-04-06.md`、`Language-Version-History.md`。
- **Suspect**：left/right join 是否已随 C# 14 落版。LDM 2025-02-12 目标「hopefully in the C# 14 timeframe」，但本仓库 `Language-Version-History.md` 的 C# 14.0 已发布清单未收录该特性；其最终落版版本与 champion issue #8947 的线上状态在本仓库无法核实。
- **OPEN QUESTIONS**：EF 10 对 `LeftJoin()`/`RightJoin()` 的具体 SQL 翻译与支持范围（属 dotnet/efcore，本仓库无正文）；`System.Linq` 新方法的性能基准与 API 讨论（dotnet/runtime #110292，本仓库无正文）；C# 后续是否按提案 Open questions 扩展 `distinct`/聚合/集合运算等更多查询子句（仅 LDM 提及，无设计）。
