# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周讨论 Anthony 第 1 章 Type-Inference Enhancements 尾部的递归 / 互递归 Lambda 建议。上一场 TypeOf 流分析会议刚确立了"启用型"绑定原则（收窄只启用先前会报错的代码），这场会议我们会看到同样的原则再次成为安全性的支点——但这里的破坏面比类型收窄更靠近语言地基（变量声明顺序与明确赋值规则）。

## Agenda

* [Proposal: 递归 / 互递归 Lambda 类型推断（Recursive Lambda Inference）](#proposal-递归--互递归-lambda-类型推断)

## Proposal: 递归 / 互递归 Lambda 类型推断

_Related: [vblang #195 – Static Property Variables（2017 会议，拒绝窄用例并指向本地函数）](https://github.com/dotnet/vblang/issues/195)；[vblang #337 – Pattern Matching（2018 会议，阶段化原则与"不破坏"底线）](https://github.com/dotnet/vblang/issues/337)；ModVB：`proposal-local-declarations.md`（`Let` 关键字）、`proposal-conditional-best-common-type.md`（`If()` 推断）、`proposal-wildcard-lambdas.md`（Lambda 语法糖家族）_

### 场景与缺口

We started from the four examples at the tail of Anthony's Chapter 1（`..\AnthonyDesign_wordpress.txt` L213–241）。四个示例看似是一类问题——"让一些类型推断应该工作却报错的声明正确工作"——但拆开看，它们是两代人：

**第一类：推断缺口（低风险）。** `Let snapshot = Snapshot.Create()` 与 `Static cache = New Dictionary(Of Integer, Integer)`。`Dim snapshot = Snapshot.Create()` 在 vanilla VB 的 Option Infer On 下早就成立；`Static` 局部变量的初始化式推断我们同样 `Probably` 在 vanilla 里已经工作。这两条在建议里被写成 "This should work"，我们更愿意把它们定性为 **ModVB `Let` / `Static` 路径上的实现缺口**，而不是语言设计问题——真正的设计问题在下面。

**第二类：语言缺口（高风险）。** 单语句递归 Lambda 与互递归 Lambda：

```vb
' 建议原文（Anthony Ch.1 L223–228，原样摘录）：
Let factorial =
      Function(n As Integer) As Integer
          If n < 0 Then Throw

          Return If(n = 0, 1, n * factorial(n - 1))
      End Function

' 建议原文（L235–241）：
Let isEven = Function(n As Integer) As Boolean
                 Return n = 0 OrElse isOdd(n - 1)
             End Function

Let isOdd = Function(n As Integer) As Boolean
                Return n <> 0 OrElse isEven(n - 1)
            End Function
```

We see a genuine convenience gap here. 今天的递归 Lambda 必须写成两语句惯用法：先显式声明委托变量，再赋 Lambda 体引用自己；互递归则要先把两个委托变量都声明出来。建议想把它压成一条 `Let`。

但 We think 把四段代码捆成一个建议是**不诚实的边界**：它们风险差一个量级。会议主线因此先从解绑开始。

### 候选方案

**PROPOSAL A — 全文捆绑。** 四项一起放宽：方法调用推断、`Static` 推断、自引用递归、互递归；互递归采用"延迟到变量全部声明后再绑定"的 SCC 策略。

**PROPOSAL B — 只做安全半边。** 方法调用推断与 `Static` 推断先验证 vanilla 基线（若确为缺口则修）；自引用递归只允许"参数与返回类型全部显式标注"的 Lambda；互递归 Table，绑定到两个信号（见 RESOLUTION）。

**PROPOSAL C — 本地函数（local functions）作为正统解法。** 引入嵌套具名 `Function` / `Sub` 声明，天然支持递归与互递归，变量声明顺序与明确赋值规则**完全不动**。这是主线自己说过的方向。

**PROPOSAL D — 什么都不做。** 维持两语句惯用法与 `AddressOf` 具名方法；递归本就不是高频业务场景。

### 权衡：Q&A

- **A 是不是假命题——两语句形式今天已经能编译吗？** 这是全场最关键的一次澄清。若 `Dim factorial As Func(Of Integer, Integer)` 之后赋 Lambda、体内引用 `factorial` 在 vanilla VB 已成立（我们高度确信成立，这是流传多年的 VB 惯用法），那么"自引用"部分**根本不是放宽变量声明，而是纯语法糖 + 类型推断**：编译器只需把 `Let factorial = Function(...)` 视为"从显式签名推断委托类型 → 声明变量 → 再绑定 Lambda 体"。互递归的两语句形式（先声明 `Dim isEven As Func(Of Integer, Boolean)`、`Dim isOdd As ...`，再分别赋值）在 vanilla 是否能编译，我们 `Probably` 成立但需要原型核实——VB 对 Lambda 捕获变量的明确赋值检查比 C# 宽松，这正是我们要在原型里钉死的第一件事。**如果两语句互递归今天就能编译，整个建议就收缩为"把两语句压缩成一条、并把委托类型从显式签名推断出来"**，比"放宽声明前引用"小得多。
- **A vs B：互递归的静态可证明性。** 建议原文自己问："Can these rules be relaxed in lambdas that aren't invoked until all variables are declared and initialized to enable mutual recursion?"——我们把它翻译成可证明的命题。`Let a = Function(...)` 的**初始化式本身不执行**，所以只要互递归集合（SCC）里的每个变量都用 **Lambda 字面量初始化**，块内就没有任何代码会提前求值兄弟变量；块结束即全部赋值，任何调用都发生在赋值之后。这个论证是干净的。但一旦允许 `Let isOdd = a()`（非 Lambda 初始化式），`a()` 会在 `isOdd` 赋值前调用 `a` 的体、`a` 的体读 `isOdd`（此刻是 Nothing）→ 运行期 NullReferenceException。**编译器必须把 SCC 内变量的初始化式限定为 Lambda 字面量**，才能保住"错误留在编译期"的承诺。建议原文没有给出这个限定，这正是它未决问题的核心。
- **A vs B：Nothing-delegate 陷阱与"延迟调用"前提。** 即使 SCC 全部是 Lambda 初始化，用户仍可写 `Let c = Function() a()` 然后在块内、块后任意调用。块后调用没问题；但 "Lambda 未被提前调用"这个前提**在一般情形下不可静态证明**——编译器无法知道哪个 Lambda 会在未来被调用。我们能做的只有两条路：① 限定初始化式形态（Lambda 字面量），使"提前调用"在语法上不可能发生；② 在运行期保留 Nothing 委托的 NRE，把责任交给用户。我们选择 ①，但必须明说：**这是把"声明前引用"错误从编译器移走后的残余风险面**，不是零。
- **B vs C：为什么不直接上本地函数？** 2017 年主线在否决 `Static` 属性变量（#195）时写得很清楚：*"If we want to enable nesting we'd look at local functions and types as well."* 本地函数是**声明**而非**初始化**：没有声明顺序问题、没有 Nothing 委托陷阱、直接调用无需经委托装箱、调试器支持更好。递归 / 互递归的完整、干净解就是本地函数。但——VB 主线至今没有本地函数的成稿设计；递归 Lambda 是**零新语法**的增量，两者不冲突：本地函数是终点，递归 Lambda 是"不建语法就能先到 80%"的过渡。We think 它们应作为**同一个工作项的两阶段**，而不是互斥方案。这也顺带回答"第二种做事方式"的追问：递归今天已有两语句惯用法，加一条语句变体是第三个入口，必须靠"它消除样板（原则 #9）"来辩护。
- **破坏面：名字重绑定（这是全场第二尖锐的追问）。** 放宽"声明前引用"不只是开启错误代码——它可能**改写已经成功绑定的名字**。若方法里先有 Lambda 体引用 `g`，后声明局部 `g`，而模块级恰有方法 `g`，今天 `g(...)` 绑定模块方法；放宽后改为绑定局部 `g` → **同样的源码，重编译后调用不同的东西**。这正是设计原则所警惕的隐蔽语义变化（`Return?` 被拒的同类理由）。因此本特性必须沿用 TypeOf 会议确立的**"启用型"规则**：放宽只作用于"当前绑定失败或报错的名字"；先前绑定成功的名字，绑定原样保留。没有这条规则，自引用部分都不可接受。
- **C# 对照。** 2018 年模式匹配会议确立的底线是 *"we will follow C# unless there is a compelling reason to avoid adding more subtle differences between the languages"*。C# 没有单语句递归 Lambda；C# 的惯用法就是先声明 `Func<...>` 再赋值的两语句形式（与 VB 同）。C# 的解法是本地函数（C# 7 起）。所以"跟随 C#"在这里**不是**指照搬一种语法，而是指跟随它的结论：递归应走本地函数。`Probably`：我们不需要与 C# 对齐的语法——我们没有 C# 的 `=>`，我们的 `Function...End Function` 本来就更适合显式签名。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Let factorial = Function(n As Integer) As Integer ... End Function` 本身无新语法——`Function` Lambda 是既有文法。歧义点在**推断边界**：Lambda 的参数类型显式、返回类型**未**显式时（`Let f = Function(n As Integer) n * 2`），返回类型由体推断；体若引用 `f` 自身，则 `f` 的类型（其委托类型含返回类型）依赖体，而体依赖 `f` → **循环，必须拒绝**。规则必须是"参数与返回类型**全部**显式"这一硬边界，且要在错误文案里说清"缺少返回类型导致无法推断递归 Lambda"。这没有文法歧义，但有一道清晰的语义悬崖，spec 必须画出来。

#### 2. 角案例与边界语义

**自引用的最小可编译形态**（修正后的示例，见下）：

```vb
' 修正：原文 If n < 0 Then Throw 里的裸 Throw 在 Catch 之外是编译错误
'（vanilla VB BC30689 类错误），我们 Suspect 该示例不能按字面编译。
Let factorial =
      Function(n As Integer) As Integer
          If n < 0 Then Throw New ArgumentOutOfRangeException(NameOf(n))

          Return If(n = 0, 1, n * factorial(n - 1))
      End Function
```

**互递归的最小可编译形态**：

```vb
Let isEven = Function(n As Integer) As Boolean
                 Return n = 0 OrElse isOdd(n - 1)
             End Function

Let isOdd = Function(n As Integer) As Boolean
                Return n <> 0 OrElse isEven(n - 1)
            End Function
```

**SCC 的形态限定。** 互递归集合内每个变量的初始化式**必须是 Lambda 字面量**；`Let isOdd = a()` 形式的初始化式若引用集合内兄弟变量，编译错误。这是静态可证明性的前提（见 Q&A）。

**返回类型的依赖图必须无环。** 两个 Lambda 都显式签名时天然无环；若允许"一个显式、一个推断返回类型"，`Let f = Function(x As Integer) g(x)` + `Let g = Function(x As Integer) f(x)` 两者返回类型互相依赖 → 环 → 拒绝。v1 只允许"全体显式"，把"推断依赖图无环则放宽"留作未来扩展（`Probably` 可实现）。

**`Sub` Lambda / `Action`。** 规则对称适用于 `Sub`（显式参数、无返回类型），委托类型为 `Action(Of ...)`。

**ByRef 参数。** Lambda 可声明 `ByRef` 参数，但 `Func`/`Action` 委托族不支持 ByRef，只能转自定义委托。若 `Let` 无目标类型，这类 Lambda 的委托类型推断本身就会失败——与递归无关的既有边界，spec 里提一句即可。

**泛型与重载的静态调用。** 体内 `factorial(n - 1)` 是委托调用，不涉及泛型推断，按既有委托调用规则解析。递归经委托（`Func(Of T, T)`）时泛型委托实例化无特殊问题。

**`Static` + 递归。** `Static factorial = Function(...)...` 的委托变量是静态的，跨调用保留；自引用同样依赖"显式签名"边界。可行，但 `Static` 与闭包提升的交互（见第 7 条）需在原型验证。

#### 3. 作用域与绑定

这是本特性真正的改动面。vanilla VB 的局部变量作用域**从声明点开始**；Lambda 体引用"声明点之后的兄弟变量"今天要么绑定到外层同名成员、要么报错。本特性要求：**同一块内、Lambda 初始化式的变量，其名字对块内其他 Lambda 体可见**——即把作用域起点前移到一个"声明束"（declaration bundle）的顶部。语义模型里，Lambda 体内的 `isOdd` 必须解析为局部变量符号（绑定到提升后的闭包字段），而不是外层成员；`GetSymbolInfo` 返回 `LocalSymbol`。这正落在 TypeOf 会议说的"同一片实现面"上：局部变量符号的绑定顺序。

**与闭包提升（closure hoisting）的耦合。** `isEven` 引用 `isOdd`、`isOdd` 引用 `isEven` ⇒ 两者必须被提升进**同一个** closure frame，否则无法互相引用。Roslyn 的捕获分析在绑定之后才确定捕获集合——本特性要求**先预声明委托类型、再绑体、再跑捕获分析**，绑定流水线要支持一个"两遍声明束"阶段。这是实现成本的大头，也是建议原文 Drawbacks 里"可能影响增量编译"的实指。

#### 4. 与既有特性的交互

- **两语句惯用法（现状）**：`Dim factorial As Func(Of Integer, Integer)` 后赋值——保持可用，不得破坏。本特性是它的压缩，二者语义必须一致（同一委托类型、同一闭包）。
- **`AddressOf` 与具名方法**：`AddressOf` 到模块/类级函数是递归的另一条既有路径；`AddressOf` 的局部名解析不受影响。
- **Lambda 内赋值兄弟变量**：`Let a = Function() ... b = Nothing ...` 把 `b` 置空后再调用 `b()` 是运行期 NRE——但"委托变量可被赋 Nothing 后调用"今天对任何委托变量都成立，不是本特性新造的洞；不额外禁止。
- **`ByRef` 捕获**：VB 限制 Lambda 捕获 `ByRef` 参数，本特性不涉及。
- **表达式树**：递归 Lambda 捕获的是局部委托变量，体是委托调用——表达式树可表示变量捕获与委托调用，`Expression(Of Func(Of Integer, Integer))` 理论可承载。`Probably` 无 PEVerify 问题；在原型里验证闭包字段引用即可。
- **与 `If()` 公共类型推断（姊妹建议）交互**：`Let f = Function(n As Integer) If(cond, g, h)` 与递归无关；但 `If()` 推断改变推断结果（Object → 公共基类型）是**另一份建议**的破坏面，这里不掺和。

#### 5. Breaking change 与兼容性

- **纯启用 vs 重绑定**：自引用部分对"当前无同名可绑定符号"的代码是纯启用（非破坏）；但对"Lambda 体内名字当前成功绑定到外层成员、且块内后续声明同名局部"的代码是**重绑定**（破坏）。规则（沿用"启用型"）：**放宽只作用于当前绑定失败的引用**；先前成功的绑定原样保留。这要求绑定器对同一名字做"外层绑定 + 块内声明束绑定"双轨试探——与 TypeOf 会议的双轨试探是同一机制，建议写进同一份 speclet 草稿。
- **Option Infer 分叉**：`Let` 隐含推断。Option Infer Off 下 `Let x = ...` 是否仍允许？`Let` 关键字本身在 `proposal-local-declarations.md` 里定义，本建议依赖它；若 `Let` 始终推断，则与"Option Infer: This doesn't really make sense to us"（2018-02-07）的基调一致——`Let` 就是"我要推断"的显式表达。但 **Option Strict Off + 未标注参数类型** 的 Lambda（参数推断为 Object）不能进入递归放宽：`Function(n) ... factorial(n - 1)` 的 `factorial` 是 `Func(Of Object, Object)`，递归无意义且违背"显式签名"硬边界。两路径行为：Strict On 显式签名 → 允许；Strict Off 未标注 → 维持报错或推断为 Object，**不得**因放宽而改变既有推断结果。

#### 6. Option Strict / 编译选项分叉

严格路径（Option Strict On）：所有 Lambda 参数必须有类型或可目标类型化，显式签名自然满足——递归放宽全部可用。宽松路径（Option Strict Off）：未标注参数推断为 Object；放宽**不改变**既有晚期绑定/推断结果（与 TypeOf 会议同一规则："宽松路径下先前晚期绑定的调用保持晚期绑定"）。`Static` 与 `Option Infer` 的交互同第 5 条。

#### 7. IDE / IntelliSense / Edit-and-Continue

- **声明束的两遍绑定影响 IDE**：编辑器里逐语句的增量绑定是 Roslyn 的看家本领；"块级声明束"要求一次绑一束，键入中间某条 `Let` 时其余 Lambda 的解析状态会整体闪动。`Probably` 可接受，但必须验证语义模型的实时性。
- **闭包提升后的符号展示**：`isEven`/`isOdd` 提升到同一 frame 后，IDE 的调试器局部窗口与 InfoTip 必须展示两个变量，不能出现"变量不存在"。
- **错误文案**：新增"递归 Lambda 需要显式返回类型""互递归初始化式必须为 Lambda 字面量"等文案；重绑定防护的"该名称当前绑定到外部成员，已保留原绑定"提示。
- **Edit-and-Continue**：委托变量初始化与闭包 frame 的 EnC 支持是既有复杂区；本特性不新增 EnC 语法面，但声明束的改动点要标 EnC 风险。`Suspect`：互递归两遍绑定与 EnC 的交互未在建议中讨论，原型必须覆盖。

#### 8. 数据 / 普遍性

递归 Lambda 在业务代码里是**低频**的——绝大多数递归算法用迭代或具名函数表达；"数十万安静客户"的主流是 LINQ 与事件 Lambda，都不需要自引用。Anthony 自己的示例（阶乘、奇偶互递归）是教科书形态。We Suspect 这条特性**价值真实但小众**，属于"消除样板（原则 #9）"的尾巴，而不是头条。真正的高频缺口是**本地函数**（同一需求的完整形态）。普遍性数据没有，`TODO` 量化。

#### 9. 更简替代

- **两语句惯用法**：今天就能编译，样板多一条声明。这是最有力的竞争者——本特性的全部价值就是省那一条 `Dim ... As Func(...)`。
- **`AddressOf` 具名方法**：VBScript 遗产里的递归正是"具名函数 + Call"；VBScript.NET 的递归正统应该是具名/本地函数，而非 Lambda。这支持 PROPOSAL C。
- **本地函数（C）**：见 Q&A，是终态解。
- **Analyzer / 重构**：可以让两语句惯用法一键压缩成单语句，但不能让"不写声明"的代码编译——只能当脚手架，不能替代语言特性。

#### 10. 成本 / 优先级

实现成本集中在两块：① 声明束的两遍绑定 + 闭包 frame 提升；② "启用型"双轨名字试探（与 TypeOf 共享）。自引用 + 两个推断修复是**明显可落地的小增量**；互递归要额外承担 SCC 分析与形态校验，成本翻倍但价值不翻倍。按"缩小范围、分阶段落地"的取向，先落安全半边、互递归挂起，是性价比最优。**"值得做但太难"不适用于这里**——它不难，是**边界需要收敛**。

#### 11. 运行时 / CLR 硬约束

无新约束。递归经委托调用，`callvirt`/`ldnull` 是既有安全操作；闭包 frame 提升是既有机制；不触达 PEVerify 存储规则。表达式树路径（第 4 条）需验证，但 `Probably` 无碍。

#### 12. 值不值得做

价值（消除递归样板、零新语法、极 VB 的"让既有惯用法更聪明"）中高；成本（声明束 + 闭包 + 双轨试探）中；风险（名字重绑定）有"启用型"规则对冲，互递归的残余 NRE 风险靠"初始化式限定为 Lambda 字面量"收敛。**值得做——但只做安全半边，互递归不达可证明标准不落地。**

### VB 基因对照

- **消除常见样板（原则 #9）**：正中靶心——把两语句压缩为一条，且零新语法。
- **不引入"第二种做事方式"（原则 #3）**：本特性是递归的**第三个**入口（具名方法 / 两语句 / 单语句），这是最需要辩护的一点；靠"它同时是本地函数的前置阶段"来对冲。
- **避免隐蔽语义变化（原则 #7）**：唯一扣分项，靠"启用型"重绑定防护对冲；没有它，本特性就是又一个 `Return?`。
- **读起来像英语、对新手友好（原则 #5）**：`Let factorial = Function(n As Integer) As Integer ...` 的显式签名是 VB 的强项，比 C# 的 `n => n * factorial(n-1)` 自解释得多——但这是 `Function` 文法本身的好处，不是本特性的增量。
- **默认跟随 C#（原则 #4）**：C# 结论是"递归走本地函数"，本特性是临时替代，不与之冲突。
- **与主线关系（对照表 2.3）**：`Let` 关键字 = Anthony 独立延伸（依赖 `proposal-local-declarations.md`）；递归 Lambda 与互递归放宽 = 主线无对应，Anthony 独立延伸；本地函数 = 主线 2017 会议（#195）表达过意图但无成稿。**结论：Anthony 独立延伸，方向与主线兼容，但必须在"本地函数"与"启用型"两个锚点上对齐，否则会造出两套互递归语义。**

### RESOLUTION:

1. **解绑**。方法调用推断、`Static` 推断、自引用递归、互递归是四件事，不再作为一份建议捆绑。建议文档需按四份独立边界重写。
2. **推断缺口（方法调用 / `Static`）**：先验证 vanilla 基线——`Probably` 在 Option Infer On 下已工作，若确认只是 ModVB `Let`/`Static` 路径实现缺口，则按**小修复**直接落地（Active）。
3. **自引用递归 Lambda（显式签名）**：**原则上采纳**，限定硬边界——参数与返回类型**全部显式**。委托类型由签名先行推断，变量先声明，Lambda 体后绑定。这是"让既有两语句惯用法更聪明"的典型增量，极 VB。
4. **互递归**：**Table**，不拒绝但要求两个信号：① 静态可证明性论证——SCC 内所有变量的初始化式必须为 Lambda 字面量，把"不被提前调用"从不可证明的动态前提改写为可静态执行的形态限定；② 原型证实"两语句互递归在 vanilla 已能编译"，从而把整个特性降格为语法糖问题。两个信号未满足前，**不放松"声明前引用"**。
5. **"启用型"重绑定防护**：放宽只作用于"当前绑定失败的引用"；先前成功绑定到外层成员的名字，绑定原样保留。与 TypeOf 会议的双轨试探共写一份 speclet 草稿。
6. **本地函数工作项**：互递归与递归的完整解指向本地函数（#195 的意图）；本特性是零语法过渡阶段。两者归同一工作项，避免两套互递归语义。
7. **示例修正**：原文 `If n < 0 Then Throw` 的裸 `Throw` 在 Catch 之外是编译错误（`Suspect`），建议文档需替换为可编译示例。
8. **`Let` 依赖**：本特性依赖 `proposal-local-declarations.md` 的 `Let` 语义（隐含推断）；若 `Let` 不落地，本特性降级为在 `Dim` 上表达（价值减半）。

### Implication:

- 撰写最小原型：显式签名自引用 Lambda + `Let` 推断 + 闭包 frame；验证语义模型、IDE 补全与 Edit-and-Continue。
- 起草 speclet（与 TypeOf 会议共用）：声明束绑定模型、"启用型"双轨名字试探、SCC 形态校验、错误文案、`langversion` 门控。
- 核实 vanilla 基线：`Static` 推断、两语句自引用/互递归是否已编译——这是整份建议的前提事实，不能悬着。
- 与 `proposal-local-declarations.md` 对表 `Let` 的精确语义；与本地函数工作项对表接口。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：互递归的 SCC 形态限定是否足够——`Let c = Function() a()` 这种"块内新增、引用兄弟、但不在声明束内"的 Lambda 是否也要纳入同一分析（`Probably`：应纳入，否则洞还在）。
- `OPEN QUESTIONS`：`Option Strict Off` 下未标注参数类型的递归 Lambda 究竟应报错还是推断为 `Func(Of Object, Object)`，须与"放宽不改变既有推断结果"的规则对齐。
- `OPEN QUESTIONS`：声明束两遍绑定对 IDE 增量绑定与 EnC 的实际影响（原型验证项）。
- `TODO`：量化递归 Lambda / 两语句惯用法在真实代码中的占比，为普遍性补证据。
- `Follow-up`：两语句互递归在 vanilla 的编译结果核实后，更新本纪要与建议文档的定性（语法糖 vs 放宽）。
- `Follow-up`：`Expression(Of Func(Of Integer, Integer))` 承载递归 Lambda 的原型验证。

### 状态

- **LDM 状态：Consider（限定范围）**；自引用 + 两个推断修复按小增量推进，互递归 Table。
- **三态判定：Consider** — 价值真实但小众、边界需收敛、互递归未达可证明标准；本地函数是终态，本特性是过渡。VBScript.NET 优先级：本地函数 > 显式签名自引用递归 Lambda > 互递归。

---

## 附录：特性评价

# 建议评价报告：proposal-recursive-lambda-inference.md

## 评价对象

- 建议：proposal-recursive-lambda-inference.md — 递归 / 互递归 Lambda 类型推断（含方法调用推断与 `Static` 推断）
- 来源：Anthony 原文第 1 章 "Type-Inference Enhancements"（`..\AnthonyDesign_wordpress.txt` L213–241；方法调用、`Static`、自引用、互递归四段示例全部出自该章尾部）
- 配方目标：放宽局部变量类型推断与声明顺序限制，使递归 / 互递归 Lambda 及若干推断缺口正确工作；`Let` 关键字依赖 `proposal-local-declarations.md`

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。真正的语言缺口（递归 Lambda）只在"显式签名自引用"这一子场景可演示；互递归的效果悬于未证前提（"不被提前调用"无法静态判定）；方法调用 / `Static` 两条 `Probably` 在 vanilla 已工作，属实现缺口而非设计效果 | 已检查（无原型） | 四段示例捆绑掩盖了"哪些是设计、哪些是实现"；无原型封顶 3；互递归效果未显现 |
| 特性 | 4/5 | 锚点 4："主体延续 VB 基因，个别措辞轻微外来味"。零新语法、消除样板（原则 #9）、显式签名的 `Function...End Function` 极 VB | 已检查 | 打包了 4 个独立子特性，边界模糊（红旗 4.2）；"放宽声明前引用"与原则 #7 的张力未自行识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、3 个未决问题诚实列出；但核心问题（能否放宽）留作一句疑问、无 SCC 形态限定、无 breaking-change / 兼容性章节、无 vanilla 基线核实、状态行占位链接、`If n < 0 Then Throw` 示例疑似不可编译（裸 Throw 在 Catch 外） | 已检查 | 边界含糊（四子特性风险不分层）；"Lambda 未被提前调用"这一前提被轻描淡写；占位链接（`PROTOTYPE_OWNER/...`、`pr/1`） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（消除样板、迭代提速）受益；水（演化一致性）受牵动——放宽声明顺序触及语言地基；暗风险（名字重绑定 = 破坏性变更、Nothing 委托 NRE）未分析 | 已检查（预测待定） | 重绑定破坏面未识别；与本地函数工作项的重叠未权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 4/5 | 锚点 4："主要成分标注正确，个别来源或属性说明略含糊"。材料 = Anthony 第 1 章尾部（清晰）；`Let` 依赖未显式标注（借自 `proposal-local-declarations.md`）；无借鉴 C# 声明（正确——C# 无单语句递归 Lambda，惯用法同 VB 两语句形式）；继承 VB6/VBScript 的具名函数递归遗产未点明 | 已检查 | 未标注 `Let` 依赖；未点明与本地函数（主线 #195 意图）的血缘关系 |

## 设计原则对照

- **与 VB 基因：基本一致**（消除样板、零新语法、显式签名可读）；张力在原则 #7（隐蔽语义变化），靠"启用型"重绑定防护对冲；原则 #3（第二种做事方式）引入递归第三入口，靠"本地函数前置阶段"辩护。
- **与主线关系：Anthony 独立延伸**——`Let` 与递归 Lambda 均不在主线雷达；与主线不冲突但未对齐。需对齐的锚点：本地函数（主线 #195 表达过意图）、"启用型"双轨试探（本组 TypeOf 会议确立）。与 `proposal-local-declarations.md`（`Let`）、`proposal-conditional-best-common-type.md`（`If()` 推断）、`proposal-wildcard-lambdas.md`（Lambda 语法糖）同族。
- **破坏性变更：潜在有**——Lambda 体内名字若当前绑定外层成员、块内后续声明同名局部，"放宽声明前引用"会重绑定为局部；未分析且建议自述"仅放宽错误"，与事实不符。需"启用型"规则 + `langversion` 门控。互递归的 Nothing 委托 NRE 是运行期残余风险面。

## 总评

- **达成程度：部分达成**——自引用递归 Lambda 概念成立且价值真实；互递归的可证明性论证、breaking-change 分析、vanilla 基线核实、范围收敛均未完成；四子特性捆绑是不诚实边界。
- **LDM 三态建议：Consider（限定范围）**——显式签名自引用 + 方法调用 / `Static` 推断修复按小增量 Active；互递归 Table，绑定到"静态可证明性论证 + 两语句基线核实"两个信号；本地函数是终态方向。
- **主要问题**：① 四子特性捆绑、风险不分层；② "不被提前调用"前提未静态化，互递归可证明性缺失；③ 名字重绑定的破坏性未分析；④ vanilla 基线未核实（方法调用 / `Static` / 两语句互递归很可能已工作）；⑤ 未与本地函数工作项对齐，存在两套互递归语义风险；⑥ `If n < 0 Then Throw` 示例疑似不可编译。

## 返工建议

- **补充章节**：Compatibility / breaking-change（名字重绑定、`langversion` 门控与警告策略）；按四子特性拆分边界（Summary / Motivation 各自独立）；Spec 明确"显式签名"硬边界与返回类型推断环的拒绝规则。
- **补充证据**：vanilla 基线核实表（`Dim snapshot = Snapshot.Create()`、`Static` 推断、两语句自引用 / 互递归在 Option Infer On/Off 下的编译结果）；最小原型（显式签名自引用 + `Let` 推断 + 闭包 frame + EnC）。
- **未决问题处理**：互递归的 SCC 形态限定（初始化式必须为 Lambda 字面量）写进设计并论证；"启用型"双轨名字试探与 TypeOf 会议共写 speclet；`Option Strict Off` 分叉定案。
- **设计探索**：与本地函数工作项共建接口（同一闭包 frame、同一符号语义）；互递归的"推断依赖图无环"扩展；`Expression(Of Func(...))` 递归承载验证。

---

## 附录：C# 生态与互操作考量

本附录评估「递归 / 互递归 Lambda 推断」与 C#/CLR/.NET 现实方向的对应关系。依据 `..\..\csharplang-index.md` 与 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）。本提案主题落在 C# 的四条现实线上：**lambda 自然类型、本地函数、函数指针、ref struct closures / `allows ref struct`**（索引 T2/T3/T8、M7）。先声明诚实边界：这是一个**纯推断/绑定特性**，运行时互操作面很薄——真正的互操作面是**委托与闭包的元数据形态**，以及**未来零分配闭包方向的语义适配**；本附录围绕这两点展开，不硬凑不存在的直接运行时依赖。

正文「C# 对照」小节已讨论"跟随 C#"的语法层面；本附录补充其**未覆盖的生态/元数据层面**。

### 相关 C# 现实方向

1. **Lambda 自然类型（C# 10，lambda-improvements）。** C# 10 给 lambda 与方法组推断自然委托类型。原文（Summary，→ `proposals\csharp-10.0\lambda-improvements.md`）：
   > "A natural type for lambda expressions and method groups will allow more scenarios where lambdas and method groups may be used without an explicit delegate type, including as initializers in `var` declarations."
   自然类型的判定条件（同文件 Detailed design，原文含笔误 `parameters types`）：
   > "An _anonymous function_ expression … has a natural type if the parameters types are explicit and the return type is either explicit or can be inferred."
   关键点：**自引用 Lambda 在 C# 里被两条规则同时封死**——① C# 局部变量在其自身初始化式里已解析到自身但**未明确赋值**（读取即报 CS0165 类错误）；② 即便放宽明确赋值，自然类型的"返回类型可推断"分支因自引用成环而必然失败。C# 的答案不是"放宽"，而是本地函数。

2. **本地函数（C# 7，local-functions）——C# 递归 / 互递归的正统解。** 原文（→ `proposals\csharp-7.0\local-functions.md`）：
   > "Local functions may be called from a lexical point before its definition."
   该提案的设计期注记甚至记录了与互递归明确赋值同构的挣扎：
   > "After experimenting with that a bit (for example, it is not possible to define two mutually recursive local functions), we've since revised how we want the definite assignment to work."
   修订方向即"每次调用处须明确赋值"，与本提案互递归"Table"的根因（提前求值不可静态证明）是**同一个问题域**。同文件还给了"本地函数优先"的性能论据（→ `proposals\csharp-7.0\local-functions.md`）：
   > "Unless you convert a local function to a delegate, capturing is done into frames that are value types. That means you don't get any GC pressure from using local functions with capturing."

3. **函数指针 `delegate*`（C# 9，function-pointers）。** C# 以 `delegate*` 暴露 `ldftn`/`calli`。原文（Summary，→ `proposals\csharp-9.0\function-pointers.md`）：
   > "This proposal provides language constructs that expose IL opcodes that cannot currently be accessed efficiently, or at all, in C# today: `ldftn` and `calli`."
   函数指针**无闭包状态**（裸方法指针，不可捕获）。因此**自引用匿名函数在 C# 侧不存在函数指针形态**——递归若走 `delegate*`，只能是具名方法自调用，不是匿名函数递归。对 VBScript.NET（无指针/unsafe）而言这条线基本无互操作面。

4. **ref struct closures / `allows ref struct`（未来 NLinq 方向 + C# 13 元数据机制）。** `proposals\ref-struct-closures.md` 提议把 lambda 转成 `allows ref struct` 泛型参数约束的 `IFunc/IAction` 函数接口、闭包为 ref struct（零分配）。原文（Summary，→ `proposals\ref-struct-closures.md`）：
   > "The proposal is to allow C# to convert lambda expressions to generic type parameters constrained to specific `IFunc/IAction` interfaces. Furthermore, the type parameter should be marked `allows ref struct` and the backing closure should be a ref struct."
   它自述为 C# 10 自然委托类型的"struct 版"（同文件 Detailed design）：
   > "This is the analogue of the C# 10 'natural delegate type' feature, but producing a struct type instead of `Func<>`."
   配套元数据机制是 C# 13 的 `allows ref struct` 反约束（→ `proposals\csharp-13.0\ref-struct-interfaces.md`；原文 Motivation："The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET."）。

5. **动态 / 晚期绑定边缘化（索引 T7）。** `dynamic`（C# 4）长期无大演进；unsafe-evolution 甚至质疑 dynamic 在 AOT 下的安全性。与本提案 `Option Strict Off` 的 Object 化路径（未标注参数 → `Func(Of Object, Object)`）方向相反，见下文适应建议。

### 现实 vs 提案

| 本提案要点 | C# 现实 | 判定 |
|---|---|---|
| "显式签名硬边界"（RESOLUTION #3：参数与返回类型**全部显式**） | C# 自然类型同样要求"参数显式 + 返回类型显式或可推断" | **兼容（同构）**——本提案硬边界恰是 C# 自然类型条件的递归版特例：自引用令"返回可推断"分支成环，只能取"返回显式"；两者对"匿名函数递归必须全签名"的结论一致 |
| 单语句自引用递归 Lambda（压缩两语句惯用法） | C# 无单语句递归 Lambda；C# 惯用法 = 先声明 `Func<...>` 再赋值（与 VB 相同）；C# 以本地函数为正统解 | **兼容 / VB 超出 C#**——C# 以声明（本地函数）而非初始化（Lambda）解决；本提案是零新语法过渡，不与 C# 冲突，但属 VB 独有增量（正文 C# 对照已述） |
| 互递归（SCC）"Table"，绑定到静态可证明性 + vanilla 基线两信号 | C# 本地函数设计期注记同样卡在互递归的明确赋值（见方向 2） | **兼容**——C# 的挣扎佐证互递归静态判定本就微妙，支持"不放松声明前引用"；VB 的"两语句互递归在 vanilla 是否成立"与 C# "每次调用处明确赋值"是同一判定 |
| 闭包 frame 提升（isEven/isOdd 同 frame） | C# 本地函数捕获进 value-type frame（零 GC 压力）；ref struct closures 进一步零分配 | **兼容（今天）/ 需桥接（未来）**——今天双方都经委托/闭包；未来 C# 零分配方向见建议 4 |
| 递归经委托调用（`callvirt`/`ldnull`） | C# `delegate*` 无捕获、无自引用匿名形态 | **脱节（无互操作面）**——VB 递归 Lambda 与 C# 函数指针互操作无交集，VB 侧无需适配 |
| `Option Strict Off` 未标注 → `Func(Of Object, Object)` | C# dynamic 边缘化、AOT 视为负担（T7） | **方向相反**——VB 宽松路径是保留遗产（COM/Office 晚期绑定）；见建议 1 |

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态。** 显式签名自引用递归 Lambda 走"默认安全"路径——与 C# 自然类型安全边界同构；`Option Strict Off` 未标注路径保持既有晚期绑定行为（动态按需、不进本特性），与 RESOLUTION #5 及决策文件 M2 "默认安全、按需动态"双模路线一致。递归 Lambda 不应成为 AOT 摩擦点：安全半边（显式签名）不引入动态。

2. **source-gen 桥 / 编译期降低为合成方法。** 自引用 `Let` 的内部表示可优先从"委托变量 + 闭包捕获"**降低为合成具名方法**（只自捕获时），仅在边界处物化 `Func<>` 委托。这同时服务 AOT/trimming（减少运行时委托分配）与 C# 互操作（C# 消费方看到标准委托或普通方法），并天然靠近本地函数 value-type frame / ref struct closures 的零分配方向。`Probably`：对纯函数，方法递归与委托递归结果等价，仅在委托恒等与调用栈深度上有可观察差异，需在 speclet 里钉死。

3. **识别新元数据（必须桥接）。** VB 编译器需认识 C# 13 的 `allows ref struct` 反约束及其 `CompilerFeatureRequired` 特征标志（决策文件 M4/M7；同机制亦见于 `proposals\csharp-11.0\required-members.md` 与 `proposals\closed-hierarchies.md`），才能消费 NLinq 风格 C# 库的 `IFunc/IAction` 函数接口类型；否则"VB 递归 Lambda 与 C# 函数接口互操作"无从谈起。这是本提案**唯一硬性的元数据桥接点**，与递归特性本身正交，但决定 .vbx 能否站在 C# 零分配 lambda 生态旁边。

4. **未来 ref struct closure 场景下递归 Lambda 的可行性（OPEN QUESTION）。** 若生态走向零分配闭包（闭包为值类型、经泛型参数传递），自引用递归 Lambda 需要一个**指向自身/兄弟闭包的句柄**；ref struct 是值类型且不可含自身（大小递归），自引用只能经间接：委托字段（= 回到堆分配，违背零分配初衷）或 by-ref 字段（= 受 span-safety 生命周期约束）。互递归经 by-ref 字段互指是否被 span-safety 允许、函数接口目标类型是否有稳定的"实例身份"可捕获，**本附录未能核实**。`Suspect`：自引用闭包在该模型里可能不可表达。建议在原型里验证"闭包帧 + 自引用"在函数接口目标下是否成立，再决定是否纳入该方向；若不可行，递归 Lambda 将成为"必须退回委托"的少数情形（可接受的显式例外，但需文档化）。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #3（显式签名硬边界）**：被 C# 自然类型条件**同构佐证**，无改动；可把 C# 自然类型引用补进 speclet 的 Motivation 作外部锚点。
- **RESOLUTION #4（互递归 Table）**：C# 本地函数设计期注记（方向 2）为"互递归静态判定本就微妙"追加了 C# 侧旁证；Table 不动。
- **RESOLUTION #6（本地函数工作项）**：与 C# 正统解（本地函数）一致，确认"本地函数为终态、递归 Lambda 为过渡"的定位；适应建议 2 的"合成方法降低"可作为该工作项的前置实现探针。
- **三态判定（Consider）**：**不变**。新增一条**前瞻风险旗标**：ref struct closures 零分配方向可能使递归 Lambda 成为"仅委托形态"的特例（未来互操作缝隙），列入待办跟踪，不改变本次判定。

### 引用纪律与 OPEN QUESTIONS

- 本附录所有 C# 原文均逐字核对，来源标注如上：`→ proposals\csharp-10.0\lambda-improvements.md`、`→ proposals\csharp-7.0\local-functions.md`、`→ proposals\csharp-9.0\function-pointers.md`、`→ proposals\ref-struct-closures.md`、`→ proposals\csharp-13.0\ref-struct-interfaces.md`；元数据机制佐证见 `→ proposals\csharp-11.0\required-members.md`、`→ proposals\closed-hierarchies.md`。
- **OPEN QUESTIONS**：① 自引用 / 互递归 Lambda 在 ref struct closures（函数接口目标）下是否可表达（自引用值类型闭包 + span-safety 生命周期）；② VB 编译器识别 `allows ref struct` / `IFunc` 函数接口所需的完整属性集（`CompilerFeatureRequired` 各特性名清单）；③ C# 14 表达式树放宽（可选/具名参数，索引 T7）后，表达式树承载递归 Lambda（`Expression(Of Func(Of Integer, Integer))`）是否需要重估——本附录未深挖 C# 14 表达式树原文，列为待核实。
