# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周处理一组字符串建议，第一份是插值字符串优化。这个话题把我们带回了 2014 年那场 String interpolation 笔记里**悬而未决**的两个问题——"compiler folding 吗？""常量孔求值吗？"——所以讨论比预想的要顺：主线自己就埋过这些种子。但建议把一件纯优化和一件**新语法特性**捆在一起，这让我们花了不少时间把它们拆开。

## Agenda

* [Proposal: 插值字符串优化与 StringBuilder &=](#proposal-插值字符串优化与-stringbuilder-)

## Proposal: 插值字符串优化与 StringBuilder &=

_Related: [vblang 2014-04-02 String interpolation 笔记](../../vblang/meetings/2014/LDM-2014-04-02.md)；[2015-01-14 重载决议与工厂方法](../../vblang/meetings/2015/LDM-2015-01-14-VB.md)；[vblang #86 – 优化 Int/Fix/Math 转换](https://github.com/dotnet/vblang/issues/86)；[2018-03-21 #86 讨论](../../vblang/meetings/2018/vbldm-notes-2018.03.21.md)；[2018-06-13 表面扩展门槛](../../vblang/meetings/2018/vbldm-notes-2018.06.13.md)；ModVB `proposal-interpolated-string-optimization.md`（Anthony 原文第 6 章）_

### 场景与缺口

插值字符串被编译成一次 `String.Format` 调用。若一个逻辑输出被拆成多个相邻的 `$"..."` 再用 `&` 拼接，就会产生多次格式化与多次中间字符串分配；日志、异常消息等高频路径上，这种冗余可测。原文的第 6 章示例：

```vb
' 今天：三次 String.Format + 两次拼接分配。
Console.WriteLine(
    $"Greetings, {firstName}. " &
    $"The current time is {Date.Now}, " &
    $"on {Date.Today.DayOfWeek}.")
```

Anthony 还给了两个相邻主张：一是**无运行时插值**的插值字符串（只有常量文本与 `NameOf` 这类编译期常量）不该调用 `String.Format`，可以直接当常量字符串用；二是 **`&=` 对 `StringBuilder` 生效**（等同追加），"无论拼接对象是字符串变量还是 StringBuilder"。

关于第一、二个主张，2014 年的主线笔记其实已经问过同样的问题：

> "Will there be compiler folding? e.g.
> `var x = "hello \{a}" + "world \{b}";`
> ==> `var x = "hello \{a}world \{b}";`
> ==> `var x = String.Format("hello {0}world {1}", a, b);`"

> "Will the compiler do compile-time evaluation if the arguments that go in the hole are constant and which don't depend on the current culture? It seems risky to take a dependency on the internal behavior of String.Format…"

这是主线自己留的开放问题，从未落地。2015 年那条笔记则把优化空间明确留给了实现：工厂方法的 `Create` "leaves the implementors of the factory free to do lots of nice optimizations." 所以我们今天不是在开拓新地，而是在回答一桩旧账——外加一个主线从来没提过的新主张（`StringBuilder &=`）。

### 候选方案

**PROPOSAL A — 相邻插值字符串合并为一次 `String.Format`。** 按建议原文：`$"A {x}" & $"B {y}"` 折叠为 `String.Format("A {0}B {1}", x, y)`，只格式化一次、只分配一次。

**PROPOSAL B — 无运行时插值时跳过格式化。** 插值字符串的孔全为编译期常量（如 `NameOf(description)`）或根本无孔时，不发出 `String.Format`，直接发常量字符串：

```vb
' 建议主张：这一处不应调用 String.Format。
Throw New ArgumentNullException(
            description,
            $"Argument '{NameOf(description)}' may not be null.")
```

**PROPOSAL C — `StringBuilder` 的 `&=`。** `builder &= "Line" & vbCrLf` 对 `StringBuilder` 生效，语义为追加，与字符串变量的 `&=`（拼接再赋值）区分。

**PROPOSAL D — 显式替代语法。** 不复用 `&=`，而是引入显式的 builder 追加语法、或 `AppendFormat` 化、或直接推进与 C# interpolated string handlers 的互操作（Anthony 自己在原文互操作清单里就列了 "Interpolated string handlers"）。这是"第二种做事方式"的成本更低、方向更远的选择。

**PROPOSAL E — 什么都不做。** 保持现状，靠开发者手写 `StringBuilder.Append`，或把优化完全交给运行时（`String.Format` 内部折叠）。显式"什么都不做"是 LDM 决策工具箱里的合法选项——对不成熟的想法，不做本身就是结论。

### 权衡：Q&A

- **A vs E：合并真的无损吗？** 不。合并会翻转一个可观察的顺序，详见下文拷问 2。这是整场最尖锐的追问。如果这个翻转不能被接受，A 的广泛形态就必须收窄，而收窄后的价值又撑不起实现成本——我们一度倾向直接不做。
- **B vs E：折叠安全吗？** 对 `NameOf` 这类**字符串值**的常量孔，安全——`String.Format` 对 String 参数原样插入，不经过 `ToString`、不依赖文化。但对**数值**常量孔（如 `{1 + 2}`），折叠依赖"String.Format 在特定文化下会产生什么字符串"——2014 年笔记原话 "It seems risky to take a dependency on the internal behavior of String.Format"，我们原样采纳。B 只做字符串孔。
- **C vs D/E：`&=` 是优化还是特性？** 它是**特性**，不是优化。2018-03-21 处理 #86 时我们自己定的规矩："Treat it as an optimization, not a feature to keep it narrow and get it done." 而且同一句话是："This does not effect or comment on considering syntax enhancements." ——优化和语法增强**不该捆绑**。这份建议把两件纯优化和一件新语法特性塞进一份提案，第一件事就是拆开。
- **A + C 的组合自相矛盾。** 合并 A 想把相邻插值拼成**一个大的中间字符串**；`StringBuilder &=` C 想**避免中间字符串**。两者组合（`builder &= $"A{x}" & $"B{y}"`）时，先合并成一个字符串再 Append——恰好把 StringBuilder 的意义抵消了。建议原文自己也承认"未展示组合规则"。
- **C 的"类型相关语义"。** `s &= x`（字符串）是拼接再赋值，`builder &= x`（StringBuilder）是追加——同一个 `&=`，行为随静态类型而变。这与我们拒掉 `Return?`（#167）的理由同源："Control flow would be altered by a very subtle character." 这里是**操作语义**被一个静态类型改变，比控制流更隐蔽。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

无新文法。合并与折叠是**降级（lowering）**层面的变换，不新增任何 token 或产生式；`&=` 文法早已存在，需要决定的是**绑定**（绑到什么）而非**文法**。真正的"歧义"在语义层：`&=` 的涵义取决于左手边的静态类型——这是给读者的歧义，不是给解析器的。`?` 是 VB6 `Debug.Print` 的遗产，在 VB.NET 不可编译，示例需重写（见拷问 8）。

#### 2. 角案例与边界语义

**合并翻转求值顺序（本场核心）。** 考虑带副作用孔的相邻插值：

```vb
Function GetA() As String
    Console.WriteLine("evaluating A")
    Return "a"
End Function

Function GetB() As String
    Console.WriteLine("evaluating B")
    Return "b"
End Function

Dim s As String = $"A {GetA()}" & $"B {GetB()}"
```

原语义（两次 `String.Format`）：`GetA()` 求值 → **第一个 `String.Format` 立即格式化**（调用孔值 `ToString`）→ `GetB()` 求值 → 第二个 `String.Format` 格式化 → `&` 拼接。合并后（一次 `String.Format`）：`GetA()` 求值 → `GetB()` 求值 → **进入 `String.Format` 才逐个格式化**。也就是说，**"第一个孔的格式化（含其 `ToString` 副作用 / 异常时机）"与"第二个孔表达式的求值"的相对顺序发生了翻转**。VB 对操作数与实参的求值顺序是有规定的，两个顺序都是指定行为——重编译后行为变化，这触碰了我们几乎从不破坏性的底线（2018-06-13："We will almost never make breaking changes to Visual Basic"）。

对纯 `String` 孔（`ToString` 无副作用）不可观察；对自定义 `IFormattable`、副作用 `ToString`、或求值中途抛异常的场景**可观察**。因此：**A 的广泛形态不能做**。可救的收窄形态是"只合并所有孔均为 side-effect-free 的简单表达式"——但如何证明 `side-effect-free` 本身就是编译器里一条漫长的路，收益（省一次 `String.Format`）很小。

**合并的边界判定。** "相邻"如何定义？中间隔一个字符串字面量算不算相邻（`"prefix " & $"A{x}"`）？隔括号、跨隐式续行、跨注释呢？若只合并插值-插值而不合并插值-字面量，规则不完整；若全合并，字面量片段本身无孔、折叠它们无副作用风险，自然应当并入同一格式串。`Probably`：字面量片段参与合并是安全的，应当覆盖；但这会让"A 的纯度门"更加复杂。

**B 的折叠边界。** `$"hello"`（无孔）与 `$"x {NameOf(y)} z"`（孔为字符串常量）可折叠；`{1 + 2}`（数值常量孔）不可——依赖 `String.Format` 的文化行为。孔里是 `String` 常量时无文化问题。折叠**不得**改变语言层面的 const 规则：2014 年笔记明确 "Answer: No, never, not even for the ones that are clearly recognizable as constants"——`Const x As String = $"hello"` 必须**继续报错**。折叠只是降级到 `ldstr`，不影响"插值字符串不是常量表达式"这一语言事实。这是 B 与既有决议的唯一摩擦点，结论：并行不悖。

**`&=` 的目标形态。** 字符串变量的 `&=` 要求左手边是**可赋值 lvalue**（`GetString() &= x` 非法，除非 ByRef 返回）。若 `StringBuilder &=` 意味着 `builder.Append(x)`，那么 `GetBuilder() &= x`（对函数返回的 builder 追加）在"追加"语义下是合理的——但那样 `&=` 就不再是"复合赋值"而变成了"对任意表达式的方法调用"，这是概念上的大放宽。`Probably`：C 若做，应仍要求 lvalue，保持 `&=` 的赋值运算符身份。

**`&=` 的右侧。** `builder &= "Line" & vbCrLf`：整段右侧求值后一次 `Append`（与手写 `builder.Append("Line" & vbCrLf)` 分配量相同），还是拆成 `builder.Append("Line").Append(vbCrLf)`（零中间分配）？后者才是性能故事，但改变了右侧链的求值/异常顺序。且 `builder &= 42` 走 `&` 的操作数规则（Strict On 报错）还是 `Append(Integer)` 重载（Strict On 合法）？两条路在 Option Strict On 下分叉。

**循环是 C 的唯一价值点。** `For Each x In items : builder &= x : Next` 每次迭代零中间分配，是真实 O(n)。但同样的代码 `For Each x : builder.Append(x) : Next` 早已成立——`&=` 只是省掉 `Append` 三个字母。

#### 3. 作用域与绑定

语义模型层面，A/B 不改变绑定：插值字符串仍是 `InterpolatedStringExpressionSyntax`，符号不变，只有降级后的 IL 不同——`GetTypeInfo` 仍返回 String。折叠为字面量后，语义模型**仍应**把该节点报告为插值字符串表达式（不是常量表达式），只是带常量值；IDE 补全与悬停不变。C 的 `builder &= x` 在语义模型里是赋值语句节点，符号应绑到 `StringBuilder.Append`；"赋的什么值"需要定义——`Append` 返回 builder 本身，但 `&=` 是语句不是表达式，返回值语义实际不可见，只需在规范里说清。

#### 4. 与既有特性的交互

- **重载决议 / "插值字符串是字符串"（2015 决议）**：折叠与合并**不得**改变重载决议。`f($"hello")` 在有 `String` 与 `FormattableString` 两个重载时仍按 2015 年原则走——"an interpolated string *is* a string. End of story."——只是 `f` 拿到的实参在 IL 层是字面量而非 `Create(...)` 调用。这是 A/B 的不变量，必须写进 spec。
- **Late binding / Option Strict Off**：`Dim o As Object = New StringBuilder() : o &= "x"` 今天走晚期绑定拼接并**用新 String 替换 builder**。若 `StringBuilder &=` 按静态类型分派，`Object` 变量内装 builder 时行为不变、静态类型 `StringBuilder` 时行为变为追加——同一种值，两种命运，取决于声明类型。C 必须明确：`&=` 的追加语义只作用于静态类型已知为 `StringBuilder`（或其派生）的 lvalue；`Object` 一律保持既有拼接语义。
- **表达式树**：2018-03-21 对 #86 的表态可直接挪用——"Expression trees themselves will be unchanged. The IL output of expression trees is not expected to be high performance." 折叠/合并只在普通 IL 生成路径做，表达式树生成保持原样。这同时压低了 A 的实现成本。
- **`NameOf` 孔**：`NameOf(description)` 是编译期常量（2014-10 决议："The nameof expression is a constant. In all cases, nameof(…) is evaluated at compile-time to produce a string."），孔求值不产生副作用——这是 B 最干净的触发面，也是 Anthony 原文唯一给出的例子。诚实地说：**B 在原文里只被演示了 `NameOf` 一种孔**，数值常量孔是 `Suspect` 扩展。

#### 5. Breaking change 与兼容性

- **A**：有。重编译 `$"A{GetA()}" & $"B{GetB()}"` 改变可观察求值顺序（见拷问 2）。这不是"性能更好"的优化，是语义变化。2018-03-21 对 CInt/Fix 的表态再次适用："Probably some people that use CInt() probably don't care and do want optimization. But this is a breaking change and can't be done."——我们把它改成："Probably some people that use `&` between interpolated strings don't care. But the reordering is a breaking change and can't be done."
- **B**：无。等价折叠，`ldstr` 与 `String.Format("无孔串")` 行为一致；唯一要守的是 const 规则不变。
- **C**：additive——`builder &= x` 今天对 StringBuilder 是编译错误，放开后旧代码不被改变。但它是**新表面**，且与未来可能出现的 `&` 支持不对称（`builder & x` 仍错误），制造"差一个等号就不同"的认知负担。

#### 6. Option Strict / 编译选项分叉

A/B 是降级变换，与 Option Strict 无关，两条路径一致。C 的分叉最明显：`builder &= 42` 在 Strict On 下若走 `&` 操作数规则则报错、若走 `Append(Integer)` 则合法——而字符串变量的 `s &= 42` 在 Strict On 下**一定报错**。若要让 `&=` 在两种目标类型下保持一致的"拼接"精神，`Probably`：右侧按 `&` 的操作数规则绑定（Strict On 只允许 String/Char），降级为 `Append(CObj(rhs))`；不采纳 `Append` 的全部重载，避免静默差异。

#### 7. IDE / IntelliSense

A/B 无语法变化，IDE 表面几乎无感知；折叠为字面量后单步调试少一步调用，断点与监视窗口仍显示语句与最终字符串。C 需要：悬停/签名帮助解释"追加"语义；错误文案区分"对 StringBuilder 的 `&=` 是追加"；分析器机会——建议把循环内字符串 `&=` 换成 `StringBuilder`（`Probably` 这是比语言特性更好的介入点）。

#### 8. 数据 / 普遍性

- B 的 `Throw New ArgumentNullException(description, $"...{NameOf(description)}...")` 守卫惯用法在真实代码里**高频**——这是 B 最强的普遍性证据，虽然建议没有给数据。
- A 的"三段相邻插值 `&` 链"是**可读性驱动**的长消息拆分。但 VB 字符串字面量本身可跨行，作者用 `&` 通常是为了避免嵌入换行——模式真实但占比没有量化。`Suspect`：A 的频率低于建议所暗示的。
- C 的 `&=` 便利性，"hundreds of thousands of quiet customers"（2018-05-30）大多已经会写 `Append`；省三个字母不构成特性级需求。
- 证据等级：全部止于"已检查"，无原型、无基准。按我们的规矩，效果维度封顶 3 分。

#### 9. 更简替代

- **B 的替代**：开发者直接写字面量；analyzer 可提示"此插值字符串无运行时孔，可写成字面量"。analyzer 方案零编译器风险。但 2018-03-21 对同类 analyzer 的态度是保留的——"Probably won't do this. Folks might write this, that would be fine if they communicate usage."（那里 analyzer 建议会改变程序语义，因此不做）；而 B 的编译器折叠是**纯等价变换**、成本极低，值得编译器直接做，analyzer 不必。
- **A 的替代**：无用户侧 workaround；但收益太小、语义风险实存。更远的方向是 **C# interpolated string handlers**——它们把"格式化"和"直接写入 builder"在库+编译器协作下完成，能同时解决 A 的中间分配和 C 的追加诉求，且是跨语言一致方向。`Probably`：handler 方向才是这条路的正主，A 是它的穷亲戚。
- **C 的替代**：`builder.Append(...)` 已存在且低仪式。analyzer（建议字符串 `&=` 循环改 StringBuilder）比新语法更符合我们的门槛。
- "什么都不做"的代价：热路径上每次逻辑输出多一两次分配；可接受，且 runtime 层 `String.Format` 内部优化在持续。

#### 10. 复杂度 / 成本 / 优先级

B：常量孔折叠，成本最低，风险最低，安全。A：降级变换本身不难，难在纯度门与语义证明，广泛形态不可做。C：绑定 + 降级 + 规范 + IDE，成本中等，但它撞上我们最高的那道门槛——"our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"。`Append` 已经是那个方式，`&=` 是第二个。优先级：B 可以随下一个版本的低成本优化窗口进；A、C 都不在近期。

#### 11. 运行时 / CLR 硬约束

无 CLR 约束。`ldstr`、`String.Format`、`StringBuilder.Append` 都是既有安全操作，PEVerify 无碍；不触达存储规则。唯一的"运行时依赖"是 `String.Format` 的语义（文化、`IFormattable`、`ICustomFormatter`）——折叠 B 只吃字符串孔避开它；合并 A 不改变文化（两侧同为 CurrentCulture），只改变时序。表达式树按 2018-03-21 表态不参与优化。

#### 12. 值不值得做

- B：价值真实（守卫惯用法高频）、成本低、风险无——**值得做**，作为纯优化。
- A：价值小、风险实存。广泛形态不值得；收窄形态（side-effect-free 孔）成本不成比例。**暂不做**，除非先拿到 A 模式占比数据，且与 handler 方向对表。
- C：价值（省三个字母）远低于门槛；引入类型相关语义违背我们最在意的两条原则。**不做**。
- 整体打分：价值（B 中、A 小、C 小）× 成本（B 低、A 中、C 中）× 风险（B 无、A 有、C 有语义分叉）。只有 B 全绿。

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：C 直接违反——`Append` 已存在，`&=` 制造第二个；2018-06-13 明确这类门槛很高，"even when it's a good idea"。
- **避免隐蔽的控制流/语义变化（原则 #7）**：A 违反（求值顺序翻转）；C 违反（同一 token，语义随静态类型变，`Return?` 同源）。B 不违反。
- **消除常见样板（原则 #9）**：B 是"零新语法消灭样板"的反向——它不消灭语法，只消灭热路径上的分配，仍契合"让既有写法更聪明"。C 是正面撞板——样板已由 `Append` 承担，`&=` 只是换拼写。
- **读起来像英语、对新手友好（原则 #5）**：`builder &= x` 读起来"像拼接"，实际是追加——对新手尤其误导。
- **默认跟随 C#（原则 #4）**：C# 没有 `StringBuilder &=`；C# 的方向是 handler 互操作。C 是故意不跟 C# 的独立延伸，且没有充分理由。A/B 在 C# 也没有对应（C# 编译器同样不合并相邻插值——`Suspect`，未核实 C# 具体决议，仅常识），但它们是纯降级、不改变语法表面，不与任何方向冲突。
- **与主线关系（对照表 2.3）**：A/B 是对主线 2014-04-02 开放问题（folding、常量孔求值）的直接重答，方向**主线一致**（Anthony 独立推进、主线从未落地）；C 无主线对应，是 **Anthony 独立延伸**，且与主线"高表面扩展门槛"相抵触。这条建议整体落在 2.3 表里"主线保守、Anthony 激进"的经典张力上。

### RESOLUTION:

1. **拆案**。本建议是三种独立能力的捆绑（合并优化 / 折叠优化 / `&=` 新语法），按 2018-03-21 原则 "Treat it as an optimization, not a feature to keep it narrow and get it done" 与 "This does not effect or comment on considering syntax enhancements"，分别对待。捆绑本身是品质红旗。
2. **采纳 B（无运行时插值 → 常量字符串），作为纯优化**。范围：无孔插值字符串，以及**全部孔均为编译期常量且孔值为 String** 的插值字符串（`NameOf` 是首批触发面）。**不**扩展到数值常量孔（`{1 + 2}`）——折叠依赖 `String.Format` 的文化行为，"It seems risky to take a dependency on the internal behavior of String.Format"。
3. **B 不改变语言层 const 规则**。`Const x = $"hello"` 继续非法（2014 决议 "No, never, not even for the ones that are clearly recognizable as constants"）；折叠仅是降级，语义模型仍报告插值字符串节点，重载决议仍遵守"插值字符串是字符串"（2015 决议）。
4. **不采纳 A 的广泛形态**。相邻插值合并翻转"第一孔格式化"与"后续孔求值"的顺序，是可观察的语义变化，重编译即破坏——"But this is a breaking change and can't be done." 收窄形态（side-effect-free 孔纯度门）价值与成本不成比例，且应与 C# handler 方向对表后决定。A 状态：`Table`。
5. **不采纳 C（`StringBuilder &=`）**。它是特性而非优化；制造第二种做事方式（`Append` 已存在），且语义随静态类型变化，与 `Return?`（#167）"subtle character" 同源。需要追加时请写 `Append`。`Not all of us are happy with` 拒绝给"少打三个字母"的新表面，但这就是门槛。C 状态：`Reject`（No Plans）。
6. **不改变表达式树生成**（2018-03-21 表态），优化只在普通 IL 路径做。

### Implication:

- B 落地最小原型：无孔 + `NameOf` 孔的常量折叠；验证分配差异、语义模型节点不变、重载决议不变、const 规则不变（`Const x = $"hello"` 仍报错）。
- 起草 B 的 speclet：孔值分类规则（String 常量 vs 其他）、文化安全矩阵、与 2015 重载决议不变量、`langversion` 门控策略（是否需要——`Probably` 不需要，因非语法特性）。
- A：先量化"相邻插值 `&` 链"在真实代码库的占比，再决定纯度门形态；与 C# handler 互操作方向对表。不承诺排期。
- C：作为记录型决定归档——"为什么不做 `StringBuilder &=`"，供未来数据反驳。
- 补一份 Compatibility 分析：A 的求值顺序翻转逐例列证（副作用 `ToString`、异常时机、`IFormattable`）；C 的 `Object`/静态类型分叉逐条列证。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：数值常量孔（`{1 + 2}`）在特定文化下是否与 `String.Format` 输出恒等——若后续有文化矩阵证据，B 可扩展；当前保持关闭。
- `OPEN QUESTIONS`：A 若坚持收窄形态，"side-effect-free" 的证明边界（简单名？字段？`NameOf`？）与收益比。待数据。
- `OPEN QUESTIONS`：C 若未来复活，右侧绑定走 `&` 操作数规则还是 `Append` 重载、是否要求 lvalue——本会议倾向 `&` 规则 + lvalue，但不承诺。
- `TODO`：为 B 的原型补基准数字（分配次数 / 字节），把证据从"已检查"推到"已运行"。
- `TODO`：量化 A 模式与 B 模式（`NameOf` 守卫）的真实占比。
- `Follow-up`：与 C# LDM 就 interpolated string handlers 互操作交换意见（2018-06-13："C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries."）。

### 状态

- **LDM 状态**：B 为 `LDM In Process`（优化窗口）；A 为 `LDM Considering`（待数据）；C 为 `LDM Reviewed: No Plans`。
- **三态判定：Table（拆分后 B 独立 Active）** —— B 价值真实、成本低、零风险，作为纯优化进；A 语义风险实存、价值不足，Table 待数据；C 撞表面扩展门槛与类型相关语义红线，Reject。整份建议**不原样采纳**。

---

## 附录：特性评价

# 建议评价报告：proposal-interpolated-string-optimization.md

## 评价对象

- 建议：proposal-interpolated-string-optimization.md — 插值字符串优化与 `StringBuilder &=`
- 来源：Anthony 原文第 6 章 "6. Strings and String Pattern Matching"（`..\AnthonyDesign_wordpress.txt` L1251–1268；三主张全部出自该节）
- 配方目标：合并相邻插值字符串为一次 `String.Format`；无运行时插值时跳过格式化；`&=` 对 `StringBuilder` 生效（追加）
- 主线血缘：A/B 直接取材 vblang 2014-04-02 笔记的开放问题（compiler folding、常量孔求值）；C 无主线对应，为 Anthony 独立延伸

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（减少分配）、`NameOf` 折叠可直接演示；但主效果（合并 A）在原样设计下**不可安全达成**（求值顺序翻转），`?` 示例不可编译，无原型/基准，改进强度不可量化 | 已检查 | 主效果带语义缺陷；示例不能演示本特性的改进 |
| 特性 | 2/5 | 锚点 2："多个强无关能力捆绑"。三件事（合并 / 折叠 / `&=`）捆绑一份提案；折叠与合并零新语法、极 VB，但 `&=` 制造第二种做事方式（原则 #3）、类型相关语义（原则 #7），与 `Return?`（#167）同源 | 已检查 | 捆绑拉低整体；`&=` 与 VB 基因冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、4 个未决问题具体诚实（健康区间）；但示例 `?` 不可编译、无 Compatibility/breaking-change 分析、未识别 A 的求值顺序翻转、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）、未注明材料来源章节、`Let` 未注明是 ModVB 扩展 | 已检查 | 最关键的语义缺陷（顺序翻转）未被识别 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。水/雷受益（盘活字符串性能资产、低成本优化提速迭代）；风/暗受损（求值顺序断裂=演化一致性断裂、`&=` 类型相关=隐蔽语义分叉）；文档未权衡 | 已检查（预测待定） | 实际影响须"已采纳"后定；暗风险被低估 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。材料=Anthony 第 6 章；但折叠/合并直接取材 2014-04-02 主线开放问题未标注；继承 VB6 `?`、ModVB `Let` 未说明；C 与 C# interpolated string handlers 方向（Anthony 自己在原文互操作清单列入）对立而未论 | 已检查 | 主线血缘未声明；与 C# 方向的对立论证缺失 |

## 设计原则对照

- **与 VB 基因：部分一致**——A/B 是完全 VB 的纯优化（零新语法、消除热路径样板、可读性无损）；C 偏离三条核心原则：制造第二种做事方式（#3）、隐蔽语义变化且类型相关（#7）、对新手误导（#5）。
- **与主线关系：主线一致（独立推进）+ 独立延伸并存**——A/B 是对主线 2014-04-02 开放问题的重答，方向一致但主线从未落地，Anthony 独立推进；C 无主线对应，是 Anthony 独立延伸，且与主线"高表面扩展门槛"（2018-06-13）相抵触。
- **破坏性变更：A 有**（合并翻转求值顺序，重编译后异常时机/副作用顺序变化，需 `langversion` 门控与警告策略，建议未分析）；**B 无**（纯等价折叠，const 规则不变）；**C 无**（additive）但有 `Object`/静态类型分叉风险。

## 总评

- **达成程度：部分达成**——B 的概念与价值成立且安全；A 的语义缺陷未被识别、广泛形态不可做；C 不应做。
- **LDM 三态建议：Table**（拆分后：B→Active 纯优化、A→Table 待数据、C→Reject/No Plans）。
- **主要问题**：① 三种独立能力捆绑，且含一件特性与两件优化——违反"optimization not feature"原则；② A 的合并翻转可观察求值顺序，属破坏性变更而未分析；③ `&=` 制造第二种做事方式且语义随静态类型变，与 `Return?` 同源；④ 无兼容性分析、无原型、示例不可编译。

## 返工建议

- **补充章节**：Compatibility / breaking-change（A 的求值顺序翻转逐例、异常时机、表达式树、`langversion` 门控）；Option Strict 分叉（C 的 `Append` 重载 vs `&` 操作数规则）；明确与 2015 重载决议不变量、2014 const 决议的关系。
- **补充证据**：B 的最小原型 + 分配基准（把证据从"已检查"推到"已运行"）；A 与 B 模式的真实占比数据；`?` 示例改写为可编译的 VB.NET（如 `Console.WriteLine`）。
- **未决问题处理**：数值常量孔折叠 = 文化安全矩阵后决定（当前关闭）；A 的纯度门 = 只合并 side-effect-free 孔、先量化频率；C = 明确右侧绑定规则与 lvalue 要求，或直接改走 C# handler 互操作方向。
- **设计探索**：与 C# interpolated string handlers 方向的对表——handler 能否同时覆盖 A 的中间分配与 C 的追加诉求，若可，A/C 都不值得在编译器侧单独做。

---

## 附录：C# 生态与互操作考量

本提案是 ModVB 系列中与 C#/CLR 生态关联最直接的一份：C# 10 已用 **interpolated string handlers** 重写了插值字符串的低层降级路径，并独立落地了本提案 PROPOSAL B 的目标（constant interpolated strings）。因此本附录不只是"背景"，它直接回答正文里两个悬置点——拷问 9 说"A 的正主是 handler 方向"、Follow-up 说"与 C# LDM 就 interpolated string handlers 交换意见"。以下 C# 原文全部在 `..\..\csharplang`（dotnet/csharplang 官方仓库镜像，见 `..\..\csharplang-index.md`）逐字核实。

### 相关 C# 现实方向

C# 的方向可概括为「用库+编译器协作的 handler 模式，把插值字符串降级从 `String.Format` 换成直接写缓冲」，分四块：

1. **C# 10 improved interpolated strings（本提案最直接对应）** → `proposals\csharp-10.0\improved-interpolated-strings.md`。
   - 出发点与本提案「场景与缺口」相同：原文「Today, string interpolation mainly lowers down to a call to `string.Format`.」（Motivation）——这正是 VB 今天的状态，也是本提案要优化的基线。
   - 解法是 `[InterpolatedStringHandler]` 模式：参数类型带 `(int literalLength, int formattedCount, …, out bool)` 构造器 + `AppendLiteral`/`AppendFormatted` 实例方法，插值字符串对该类型存在隐式 handler 转换。条件求值（logging 场景）由此实现。
   - 框架提供 `DefaultInterpolatedStringHandler`：原文「We introduce a new type in `System.Runtime.CompilerServices`: `DefaultInterpolatedStringHandler`. This is a ref struct with many of the same semantics as `ValueStringBuilder`, intended for direct use by the C# compiler.」C# 10 起 `string` 型插值字符串默认走它（前提是运行时存在该类型），最终值由 `ToStringAndClear()` 取出。`ValueStringBuilder` 正是本提案「StringBuilder 池化」想做的事——C# 已把它做进语言默认路径。
   - **常量插值直接折叠**：重载决议示例 `Log($"");` 与 `Log($"{"test"}");` 走 `Log(string)`（是常量表达式），`Log($"{1}")` 走 handler。原文「This is introduced so that things that can simply be emitted as constants do so, and don't incur any overhead, while things that cannot be constant use the handler pattern.」
   - **相邻插值字符串在 C# 是"合并"的**：原文「An additive expression composed entirely of interpolated string expressions and using only `+` operators is considered to be an interpolated string literal for the purposes of conversions and overload resolution.」（原文斜体强调，此处去下划线）——纯 `+` 连接的插值字符串链被当作**一个**插值字符串字面量，合并进**一个 handler 会话**。这直接命中本提案 PROPOSAL A 的主题。
   - Span 化是显式目标：handler 有 `AppendFormatted(ReadOnlySpan<char> value)`；"堆外零分配"版本（`GetInterpolatedString` 接收 `Span<char>` + stackalloc）在提案中被推迟到 `params Span<T>` 时代，即 C# 13 params collections 落地的方向。

2. **C# 10 constant interpolated strings（PROPOSAL B 的 C# 版）** → `proposals\csharp-10.0\constant_interpolated_strings.md`。
   - `const string S2 = $"Hello{" "}World";` 是合法 C#。规则原文：「Interpolated strings `${}`, provided that all components are constant expressions of type `string` and all interpolated components lack alignment and format specifiers.」`nameof` 是常量表达式允许的构造之一，所以 `$"Argument '{nameof(description)}' may not be null."` 在 C# 是编译期常量——与本提案 PROPOSAL B 的 `NameOf` 触发面一字不差地对应。
   - **重要差异**：C# **改了语言层 const 规则**（插值字符串可以当常量）；本会议 B 明确「`Const x = $"hello"` 继续非法」（2014 决议）——见「现实 vs 提案」。

3. **条件求值（logging 场景）** → `meetings\2021\LDM-2021-03-24.md` 结论原文「We will have conditional evaluation of interpolated string holes without a special syntax for calling this out.」；`meetings\2021\LDM-2021-04-19.md` 结论原文「We accept conditional evaluation of interpolation holes as written in the spec.」，并注明「The proposal does not involve conditional evaluation when the interpolated string literal is used directly as a `string`, but other types can introduce this.」——即默认路径（`DefaultInterpolatedStringHandler`）无条件求值，条件求值只由自定义 handler 引入。这正是本提案「日志等高频路径」场景的 C# 解法。

4. **2025 年 handler 仍在演进（方向是活的，不是死胡同）** → `proposals\interpolated-string-handler-argument-value.md`（设计会议 `meetings\2025\LDM-2025-04-07.md`）：为 logging 场景新增 `[InterpolatedStringHandlerArgumentValue]`，原文「In order to solve a pain point in the creation of handler types and make them more useful in logging scenarios, we add support for interpolated string handlers to receive a new piece of information, a custom value supplied at the call site.」姊妹提案 `proposals\rejected\interpolated-string-handler-method-names.md` 被拒（改用传值方案）。C# 还在给 handler 加参数化能力。

### 现实 vs 提案

| 提案子项 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| **B（无运行时插值 → 常量字符串）** | C# 10 constant interpolated strings + 常量插值重载优先（走 string 重载） | **兼容（C# 已独立落地，边界可直接借鉴）** | 目标完全同向。C# 的常量判据给本会议 OPEN QUESTION「数值常量孔」一个参考答案：只允许 **string 常量组件**、**禁止 alignment/format**，数值常量孔（`{1 + 2}`）被排除——与本会议「B 不做数值常量孔」一致。唯一分歧是 const 规则（见下）。 |
| **A（相邻插值合并为一次 `String.Format`）** | C# 把纯 `+` 连接的插值字符串链视为单个插值字符串字面量，合并进**一个 handler 会话** | **需桥接（A 的形态是错的，目标是对的，正解=handler）** | C# 证明「合并相邻插值」安全可做——但**不合并成一次 `String.Format`**，而是每个孔按词法顺序求值并立即 `AppendFormatted`。本会议拷问 2 发现的「第一孔格式化与第二孔求值顺序翻转」在 handler 形态下**根本不会发生**（求值 A → 格式化 A → 求值 B → 格式化 B 的相对顺序与两次 `String.Format` 一致）。即：A 的诉求（无中间字符串、单次构建）由 handler 达成且保留逐孔语义。正文「与 C# handler 方向对表」的 follow-up 现在有具体机制可对。 |
| **C（`StringBuilder &=`）** | C# 无 `&=`；「追加」诉求由 handler 的 `AppendLiteral`/`AppendFormatted` 以**方法调用**承担 | **兼容（Reject 与 C# 方向一致）** | C# 从未给 StringBuilder 造运算符，而是把能力放进插值字符串本身、由编译器降级为 Append 调用。不存在 `&=` 的「同一 token 语义随静态类型变」分叉（拷问 1 / 拷问 4）。本会议对 C 的 Reject 正好落在 C# 方向上。 |
| **D（显式替代 / handler 互操作）** | handler 模式是 C# 10 起的默认 | **需桥接（VB 插值现状停在 C# 10 之前）** | VB 插值现状 = C# 10 之前的基线（`String.Format` 降级）。要消费 C# 侧 handler-based API，VB 编译器必须识别 handler 元数据（见下）。 |

**两个需明确写出的分歧/澄清：**

- **const 规则分歧（B 的边界）**：C# 把常量插值做成了语言层 const 表达式（`const string S = $"..."` 合法）；本会议 B 保持「插值字符串不是常量表达式」的语言事实（2014 决议），折叠只是降级到 `ldstr`。两者优化效果相同（编译期算好字符串），但语言表面不同。对 VBScript.NET 而言，维持保守的 const 规则与 C# 互操作不冲突（C# 常量插值产出的 `const` 字符串在元数据里是普通常量，VB 侧正常消费），无需变更 B。
- **正文 Suspect 条目被澄清**：正文「VB 基因对照」写「C# 编译器同样不合并相邻插值——`Suspect`，未核实 C# 具体决议，仅常识」。现在可核实：C# **合并**相邻插值（`+` 链 → 单 handler 会话），但不以「一次 `String.Format`」的方式。会议直觉「合并会翻转顺序」对 `String.Format` 形态成立、对 handler 形态不成立。这**支持**「合并诉求合理」，但实现载体必须换。

### 对 VBScript.NET 的适应建议

1. **识别新元数据（最高优先级桥接）**：VB 编译器需认识 `InterpolatedStringHandlerAttribute`、`InterpolatedStringHandlerArgumentAttribute`、`InterpolatedStringHandlerArgumentValueAttribute`（2025 新）与 `DefaultInterpolatedStringHandler`，并在重载决议中支持「插值字符串 → handler 的隐式转换」。否则 .vbx 调用 C# 日志 API（`logger.LogDebug($"...")`）会退回 `string` 重载，丢失零分配与条件求值，甚至报错。
2. **B 直接采用 C# 判据作「文化安全矩阵」**：C# 常量插值的边界（全部组件为 `string` 常量、无 alignment/format）与本会议 B 的字符串孔判定一致，可直接作为 B 的 speclet 输入；数值常量孔按 C# 同样排除，OPEN QUESTION 保持关闭。
3. **A 的目标改由 handler 承载**：若 VB 想要「相邻插值零中间分配」，应实现 handler 降级（`&` 连接的纯插值字符串链视为单个插值字符串）——注意 C# 规则针对 `+`，VB 需为 `&` 定义等价规则。这是正文「与 C# handler 方向对表」的具体落点，而不是 side-effect-free 纯度门。
4. **ref struct 缺口（前置条件）**：`DefaultInterpolatedStringHandler` 是 ref struct，VB 的 ref struct 支持目前是**分析器层面**（RefStructHelper/BCX，决策文件 D1）、编译器层面待移植 + suppress obsolete error（决策文件 M7）。「VB 插值字符串降级到 handler」的前提是 VB 能消费 ref struct——这是比本提案更大的决策点。中间态：先做 B（常量折叠，无 ref struct 依赖），handler 降级列为后续。
5. **默认安全 / 按需动态**：B 是「默认安全」的纯优化（零新语法、零反射、AOT/trimming 友好）；logging 条件求值（handler 的 `out bool`）是「按需动态」。若 .vbx 走解释执行，handler 语义需在运行时模拟（成本高），建议解释器模式直接落到 `String.Format`（传统兼容层）；编译到受管程序集时走编译器内联。
6. **不要给 `&=` 加追加语义**：C# 无此运算符，handler 已覆盖「追加」诉求；保持 `&=` 的拼接身份，避免与 C# 方向制造两个「追加」语法。C 的 Reject 归档可补一句 C# 侧证据。

### 对既有 RESOLUTION / 三态判定的影响

- **B（Active）**：判定不变。C# 10 独立验证了同向优化且已大规模落地，风险进一步降低；C# 判据可直接借鉴。OPEN QUESTION「数值常量孔」的 C# 参考答案是「排除」——保持关闭。
- **A（Table）**：判定不变，但 follow-up 内容升级：从「与 C# handler 方向对表（待定）」到「C# 已证明合并诉求由 handler 达成且不翻转顺序」。A 若复活，应重述为「采用 handler 降级」而非「side-effect-free 纯度门」。三态维持 Table，理由更新。
- **C（Reject）**：判定不变。C# 无 `StringBuilder &=`，追加诉求由 handler 方法调用承担；确认「拒绝 + 记录型归档」正确。
- 正文 Follow-up「与 C# LDM 就 interpolated string handlers 互操作交换意见」由本附录部分兑现：C# 侧机制已核实，下一步是把 handler 元数据识别与 `&`-链降级纳入 B 之后的实现排期。

### 引用纪律 / OPEN QUESTIONS

本附录 C# 原文全部逐字核实，来源（均为 `..\..\csharplang` 下路径）：
- 「Today, string interpolation mainly lowers down to a call to `string.Format`.」→ `proposals\csharp-10.0\improved-interpolated-strings.md`（Motivation）
- 「We introduce a new type in `System.Runtime.CompilerServices`: `DefaultInterpolatedStringHandler`. This is a ref struct with many of the same semantics as `ValueStringBuilder`, intended for direct use by the C# compiler.」→ `proposals\csharp-10.0\improved-interpolated-strings.md`（InterpolatedStringHandler and Usage）
- 「This is introduced so that things that can simply be emitted as constants do so, and don't incur any overhead, while things that cannot be constant use the handler pattern.」→ `proposals\csharp-10.0\improved-interpolated-strings.md`（Better conversion from expression adjustments）
- 「An additive expression composed entirely of interpolated string expressions and using only `+` operators is considered to be an interpolated string literal for the purposes of conversions and overload resolution.」（原文斜体强调，去下划线）→ `proposals\csharp-10.0\improved-interpolated-strings.md`（Interpolated strings through binary expressions and conversions）
- 「Interpolated strings `${}`, provided that all components are constant expressions of type `string` and all interpolated components lack alignment and format specifiers.」→ `proposals\csharp-10.0\constant_interpolated_strings.md`（Constant Expressions）
- 「We will have conditional evaluation of interpolated string holes without a special syntax for calling this out.」→ `meetings\2021\LDM-2021-03-24.md`
- 「We accept conditional evaluation of interpolation holes as written in the spec.」与「The proposal does not involve conditional evaluation when the interpolated string literal is used directly as a `string`, but other types can introduce this.」→ `meetings\2021\LDM-2021-04-19.md`
- 「In order to solve a pain point in the creation of handler types and make them more useful in logging scenarios, we add support for interpolated string handlers to receive a new piece of information, a custom value supplied at the call site.」→ `proposals\interpolated-string-handler-argument-value.md`（Summary）

`OPEN QUESTIONS`：
- VB 编译器引入 handler 元数据识别的最小范围（仅消费 C# handler-based API vs 也降级到 `DefaultInterpolatedStringHandler`）——未核实 dotnet/vblang 有无对应计划，待查 `vblang`。
- `DefaultInterpolatedStringHandler` 的精确公开 API 形状（`AppendFormatted` 重载全集、`ToStringAndClear` 签名）以 .NET 6 实际运行时为准，本附录只引提案定义；若写 speclet 需以 `dotnet/runtime` 源码核实。
- 解释执行模式下 handler 语义的模拟成本未评估。
