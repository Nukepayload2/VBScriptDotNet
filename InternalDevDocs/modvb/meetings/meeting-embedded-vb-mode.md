# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天与 top-level-code（沉浸式文件）会议同日——那份会议记录在交互清单里把 `.vbxhtml` 页面中的 `<{ ... }>` 嵌入表达式**归属到本建议名下**，所以我们进会场时已经欠了它一份文法定夺。这份文档（`proposal-embedded-vb-mode.md`，Anthony 原文第 4 章 "UI, XML, XAML"）给出的是 `<?vb?>` 解析模式，两份文档各自定义的嵌入语法并不相同，这个不一致贯穿了整场讨论。

## Agenda

* [Proposal - Embedded VB Parsing Mode (`<?vb?>`) / 嵌入 VB 解析模式](#proposal---embedded-vb-parsing-mode-vb)

## Proposal - Embedded VB Parsing Mode (`<?vb?>`)

_Related: [vblang #101 – JSON Literals（2017-10-18 讨论了嵌入语法）](https://github.com/dotnet/vblang/issues/101)；[vblang #211 – usage of any installed programming language（"Fantastic idea, and too hard to do"）](https://github.com/dotnet/vblang/issues/211)；ModVB 姊妹建议：[xaml-literals](../proposals/proposal-xaml-literals.md)（`<?xaml?>` 字面量）、[xml-schema-types](../proposals/proposal-xml-schema-types.md)（`<ResponseMessage>` Schema 类型）、[top-level-code](../proposals/proposal-top-level-code.md)（`.vbxhtml` 归属交叉）_

### 场景与缺口

We opened from the classic Web 模板的切口。传统 ASP / 经典 ASP.NET Web Forms 在 HTML 与代码之间用 `<%= %>`、`<% %>` 转义序列来回切换解析模式，代码片段支离破碎、难以阅读和重构。Anthony 的主张是：让开发者写一个"纯 VB"的模板——`<?vb?>` 之后整个文档按 VB 解析，HTML 标签只是 XML 字面量，元素文本是 VB 表达式，控制流是真正的 VB 语句：

```vb
<?vb?>
<html>
  <head><title>document.Title</title></head>
  <body>
  <h1>$"{document.Title} >> {document.LastUpdated}"</h1>

  If includeDisclaimer Then
      <div>
          ...
      </div>
  End If

  For Each p In document.Paragraphs
      <p>p.Text</p>
  Next
  </body>
</html>
```

以及配套的"XML 表达式语句隐式 `Return`/`Yield`"：

```vb
Function GetResponse() As <ResponseMessage>
    <ResponseMessage>
        ...
    </ResponseMessage>
End Function
```

We think the *attraction* is genuine and very VB. VB 是主流 .NET 语言里唯一带一等 XML 字面量的语言（自 VB 9.0 起），`<%= %>` 嵌入表达式、XML 轴属性（`.<name>`、`.@attr`、`...<name>`）都是 VB 独有的强项。把"页面即程序"做进 XML 字面量，方向上是把 VB 的既有血脉向前推一步——而不是从 C# 抄一个东西进来。Anthony 把四个 Past Demos（Web Page、Web Controls/Components、XAML/Xamarin.Forms、HTML/JavaScript Transpilation）都以视频链接给出。**照旧声明证据边界：这是视频，没有代码。** 我们只能确认"有人跑过一次"，无法复核、无法复现、无法在此基础上构建。证据阶梯止于"已提供/已检查"。

但 We have deep reservations the moment we looked at the *inversion*. 今天的 VB XML 字面量里，文本默认是**字面量文本**，`<%= expr %>` 是逃入代码的显式标记；本建议把默认值翻转成"文本即表达式"。这个翻转是本建议的全部价值，也是它全部困难的来源。我们决定把下面这场讨论的每个追问都挂在这条翻转上。

### 候选方案

We enumerated four ways the feature could be realized. 前两个是原文建议的两种可能形态，后两个是我们的再切分。

**PROPOSAL A — 文件级 `<?vb?>` 解析模式（按原文）。** `<?vb?>` 之后的整个文件切换为 VB 解析；HTML/XML 标签作为 XML 字面量，元素文本中的表达式直接求值，`If ... End If`、`For Each ... Next` 直接控制 XML 输出；函数与属性体内的 XML 表达式语句隐式 `Return`/`Yield`。这是建议原文的完整形态，parser 在 VB 语法与 XML 文本之间动态切换。

**PROPOSAL B — 块级 `<?vb?>` 字面量（有界表达式）。** 不切换整个文件的解析模式，而是把 `<?vb?>` 当作一个**表达式字面量的起始标记**，与姊妹建议 `<?xaml?>` 完全对称（`Let button = <?xaml?> <Button .../>`）。其作用域是与之配对的一个有界表达式，产物是一个 `XElement`/`XDocument` 值；文本节点按表达式解析，但**不引入**隐式 `Return`/`Yield`，不改变任何函数体语义。代价：`If`/`For Each` 无法直接嵌在文档中间——它们退回到 `<%= If(...) %>` 表达式形式，或回到文档外面。B 失去"语句驱动输出"的魔法，但换到极小的实现面与零语义破坏。

**PROPOSAL C — 现状 + 工具/代码生成。** 保留 `<%= %>`（既有机制），把模板交给**编译期工具**（source generator 或构建任务）预处理为完整 VB 代码树。这恰好兑现原文自己标注的 "Not Shown: Full-Fidelity VB Code-Trees to support inspection, execution, and translation of embedded VB code to target DSLs such as JavaScript."——用工具而非语言语法达成"可检查、可执行、可翻译到 JavaScript"。零语言表面扩张，与主线把 XML 智能提示当**工具方向**推进的立场一致。代价：模板语法与 VB 语法是两层，模板作者得到的不是编译器的原生支持。

**PROPOSAL D — 只取隐式 `Return`/`Yield` 子集。** 完全不碰解析模式。只让"函数/属性体内的 XML 表达式语句"隐式作为返回值。这是一个独立、小而纯 VB 的特性——VB 本来就有 `FunctionName = value` 的隐式返回传统，把最后一条 XML 表达式语句纳入这个传统是自然延伸。但它依赖 `As <ResponseMessage>` 这样的 XML Schema 类型（`proposal-xml-schema-types.md` 的特性），且 `Return`/`Yield` 的选择规则完全未定。

We agreed early that 这不是一个特性，是**五个独立承诺捆在一起**：(i) `<?vb?>` 模式开关；(ii) 文本节点即表达式；(iii) 语句驱动 XML 输出；(iv) XML 表达式语句隐式 `Return`/`Yield`；(v) "Not Shown" 的 `InitializeComponent` 隐式自应用、流式写入 Builder 模式、完整保真 VB 代码树。它们绝不该同生共死。

### 权衡：Q&A

We walked the design through the LDM questioning list。以下按拷问库逐条记录真正咬人的问题，不按优先级。

**1. 语法/文法歧义：文本节点是表达式还是文本？这是整场最尖锐的追问。** 模式翻转的直接后果：`<title>document.Title</title>` 里 `document.Title` 是表达式，但 `<p>Welcome to our site</p>` 里的 `Welcome to our site` **不是任何合法 VB 表达式**。于是要么 (a) 文本节点一律是表达式——静态文本必须写成 `$"Welcome to our site"`，最常见（大部分静态）的模板场景反而更繁琐；要么 (b) 加启发式"能解析成 VB 就按表达式，否则按文本"——这是确定性灾难，同一个字符串在不同上下文（`document` 是否在作用域内）会解析成不同含义。`<%= %>` 之所以稳，正是因为它用显式标记让边界零歧义。而本建议的边界**在文法上不可判定**。We think this is a design-internal contradiction, not a missing detail: 翻转把"逃入代码"换成"逃入文本"，逃逸语法并没有消失，只是换了方向（`$"..."`），而文本/表达式交界处无法给出无歧义文法。

**2. 语法/文法歧义：`<?vb?>` 自身与既有 PI 的关系。** `<?vb?>` 的字面形状是一个 XML 处理指令（Processing Instruction）。在今天合法 VB 的 XML 字面量里，`<?vb?>` 就是一个 PI：`Dim x = <a><?vb?></a>` 可以编译。把它放成块级字面量起始标记（B）会在"PI 内容"与"VB 代码"之间制造文法歧义；把它放成文件级开关（A）虽然不歧义（文件顶层今天没有 PI 的合法位置），却让 `<?xaml?>`（表达式标记）与 `<?vb?>`（模式标记）两种**同形不同职**的标记并存。姊妹建议 `xaml-literals` 用同一形状做表达式，本建议用同一形状做模式——两份文档彼此没有交叉引用，`Probably` 需要在文法层统一"`<?...?>` 标记家族"的职责，否则就是两个随意挂上的钉子。

**3. 角案例：属性值是不是表达式？** 原文只展示了**元素文本**，从未展示属性。`<a href=url>`——`url` 是表达式还是文本？`<img src=product.ImageUrl />` 若按表达式解析，静态属性 `<div class="active">` 必须引号化；若不按表达式解析，则文本与属性两套规则不一致，心智负担翻倍。HTML 的属性位恰是 Web 模板里高频嵌入位（`class`、`href`、`src`、`data-*`），这个缺口不是边角，是主干缺失。`OPEN QUESTION`：没有任何一种形态（A/B）在文档里回答了它。

**4. 作用域与绑定：`document` 从哪来？** 示例里 `document.Title`、`document.Paragraphs`、`includeDisclaimer` 全部未声明。`Option Strict On` 下 `document` 必须可解析，否则是编译错误。这与 top-level-code 会议抓到的 `Starfield.vb` 未声明 `buffer` 是同一类问题。若 `document` 是框架提供的环境对象（如 ASP 的 `Page`/`ViewData`），需要一个明确的绑定机制；若 `includeDisclaimer` 是页级属性，需要说明它在哪个宿主上。`.vbxhtml` 的 `<{ ViewData!Message }>` 同样没有任何文档说明 `ViewData` 绑定到谁。**结论：示例要么补声明，要么明确"环境对象"机制——否则每个示例都不能在 `Option Strict On` 下编译。** We will insist on this, 与 top-level-code 决议保持一致。

**5. 与既有特性交互：XML 字面量语义翻转 = 隐蔽语义变化。** 这是原则 7 的直接命中。同一段源码 `<title>document.Title</title>` 在既有 XML 字面量里是文本 `document.Title`，在 `<?vb?>` 模式里是表达式求值。2018-05-30 主线否决 `Return?` 时说：*"We think this is a bad idea. Control flow would be altered by a very subtle character."* 这里改的不是一个字符，是整个默认值——但性质相同：**细微的上下文差异改变语义**。文件级开关（A）使同一文件内两种语义不可并存，等于把既有 XML 字面量的文本默认值在某个开关下静默改写。**红线：任何形态都不得改变既有 XML 字面量的文本语义。** B（新增有界语法）不碰既有语法，过关；A（翻转既有字面量的默认）不过关。

**6. 隐式 `Return`/`Yield` 的选择规则。** `GetResponse()` 返回 `As <ResponseMessage>`，体内末尾 XML 语句隐式返回。但：函数内多条 XML 语句时，是全部 `Yield` 还是最后一条 `Return`？XML 语句与非 XML 语句交错时，哪一条"成为"返回值？函数已有显式 `Return` 时二者如何共存？`Sub` 内的 XML 语句是"输出"还是丢弃？`Iterator` 函数才应该 `Yield`，那么规则是否绑定 `Iterator` 修饰符？——**全部未定**，建议原文的 Unresolved questions 自己承认了。且 `As <ResponseMessage>` 是 `xml-schema-types` 的特性，本建议依赖它却未交叉引用。`Probably` 可设计成"仅当 XML 表达式语句处于 return 位置且无显式 Return 时隐式返回；`Iterator` 函数中多条 XML 语句逐个 `Yield`"，但这是新设计，不是从原文能读出来的。D 若不解决这套规则就不能独立推进。

**7. Breaking change 与兼容性。** 若走 B/D（纯新增语法），旧代码行为不变——`<?vb?>` 今天在文件顶层是编译错误，`<title>` 作独立语句今天也是编译错误，错误变成程序不构成破坏。真正的兼容风险在别处：`xml-schema-types` 使 `<ResponseMessage>` 成为**类型名**后，与既有 XML 字面量默认推断出的 `XElement` 之间的转换关系需要定义；以及任何改变既有 XML 字面量文本语义的形态（A）都构成破坏。We see no other break surface。一致性：`Suspect` 无破坏，但需要 `langversion` 门控与状态机标签，不能裸奔。

**8. Option Strict / 编译选项分叉。** 文本节点表达式的类型推断必须两条路径一致：`Option Strict On` 下 `<title>document.Title</title>` 要求 `document` 静态可解析、`document.Title` 静态可绑定；`Option Strict Off` 下是晚期绑定。若 `document` 未声明，Strict Off 晚绑定可过而 Strict On 报错——这正是第 4 点问题的双路径展开。**所有示例必须在 `Option Strict On` 下可编译，否则文档必须声明自己只面向 `Option Strict Off`。** 本建议没有做这个声明。

**9. IDE / IntelliSense 影响。** 混合"文本+表达式"的节点在编辑器里如何显示补全？一个 `<p>` 元素里既有静态文本又有表达式的断点、单步、错误定位如何做？模板里隐藏的控制逻辑正是 Drawbacks 自认的调试弱点——控制流从过程式变成文档内嵌，IDE 必须在展开后的代码树与模板行之间做映射，否则错误定位会落在"生成代码"而不是"模板行"。这个映射没有任何设计。`Probably` 标准 Roslyn 映射可以工作，但不做进规范等于没设计。

**10. 数据 / 普遍性。** 我们没有 VB 侧的用户请求数据。主线 2017-10-18 讨论 JSON 字面量时，面对"要不要像 XML 的 `<%= %>` 那样做嵌入语法"，记录是：*"Embedded syntax, should there be any special syntax like XML's `<%= %>`? _No strong feelings_."*——主线对嵌入语法本身没有强需求信号；同日议程里 XML 方向是 **"XML types for XML IntelliSense"**，即把 XML 智能提示当 IDE/类型工具做，而不是把模板模式做进语言。Web 模板市场现实已被 Razor（C#/工具层）、Blazor 等占据。对 VBScript.NET 这个脚本型产品，"页面即程序"是核心场景而非边缘，**但这是产品使命，不是用户请求**——与 top-level-code 的结论同一逻辑：数据缺口真实，产品定位是数据的替代品。We will not pretend otherwise。

**11. 更简替代：`<%= %>` 今天已经能做什么。** 我们把"现状"逐条写了一遍，发现比想象中强：

```vb
Dim page =
    <html>
        <head><title><%= document.Title %></title></head>
        <body>
            <h1><%= $"{document.Title} >> {document.LastUpdated}" %></h1>
            <%= If(includeDisclaimer, <div>...</div>, Nothing) %>
            <%= From p In document.Paragraphs Select <p><%= p.Text %></p> %>
        </body>
    </html>
```

条件输出用 `<%= If(cond, <div/>, Nothing) %>`，循环输出用 `<%= From ... Select <p>...</p> %>`——**同样的语义，今天就能写**，只是更繁琐、嵌套更深。本建议的真正增量不是表达能力，而是"语句直接驱动输出"的**体感**。我们承认体感有价值，但"有价值"不等于"值得在语言里加一个翻转默认值的解析模式"。更简的替代是 C：把模板交给工具生成 VB 代码树，原文自己都把这条路标成 "Not Shown: Full-Fidelity VB Code-Trees"——那不是不设计，是还没设计；工具化让它提前兑现。

**12. 成本 / 优先级。** 文件级解析模式（A）需要在词法/语法层面动态切换，复杂度量级接近"重写 parser"。主线 2018-02-07 讨论 #211（使用任意已安装语言）时给出的判断：*"Fantastic idea, and too hard to do."* 以及 *"It would require rewriting considerable parts of Roslyn."*——本建议的 parser 切换复杂度是同一量级，只是范围小一些。B 的有界字面量把成本压到"XML 字面量语法的一个扩展"，可落地。D 的成本更低，但被 `xml-schema-types` 卡住。C 的成本在工具链不在编译器。**成本/价值只在 B、C、D 上平衡；A 的完整形态失衡。**

**13. 运行时 / CLR 硬约束。** 无。`XElement` 构建是既有库调用，`castclass`/`newobj` 无新意，PEVerify 无碍。真正的运行时正确性问题不是 CLR，而是**输出编码**：`<p>p.Text</p>` 若 `p.Text = "<script>..."`，原样输出就是 XSS。文档对输出转义只字未提。`OPEN QUESTION`：文本节点表达式的输出是否 HTML-encode？这直接决定 Web 模板的安全性，而建议完全沉默。

**14. 值不值得做。** 价值（纯 VB 模板）真实但模糊、无数据；成本（A 的 parser 切换）高；风险（文本/表达式边界不可判定、XSS 未设计、既有 XML 字面量语义被翻转）高。**方向值得继续探索，这份文档作为规范远未就绪。** 我们把三态判定留给 RESOLUTION，但先把话放这里：A 的完整形态我们不会推荐；B、D、C 值得分别定夺。

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 2（保持 VB-like）** — 分裂。XML 字面量、`<%= %>`、轴属性是**纯 VB 血脉**，把模板做进 XML 字面量方向上是延续；但 `<?vb?>` PI 标记、文本即表达式、隐式 `Return`/`Yield` 都是外来的模板模型（ASP/Razor/JSX 气味），`$"..."` 依赖插值字符串（VB 15/C# 6 的借用）。"看起来像 VB"与"引入外来模板模型"在同一份建议里拉扯。
- **原则 3（不引入第二种做事方式）** — 这是最锋利的一条。2018-06-13 主线把"making a second way to do things"的扩张门槛设得很高。`<%= %>` 已经是"在 XML 里嵌入 VB"的**第一种方式**，本建议再做 `<?vb?>` 模式，就是同一件事的第二套语法。我们的回应与 top-level-code 时不同：那里脚本/入口场景主线**从未提供过方案**，所以不算抢占地盘；而这里 `<%= %>` 是**既有的、活着的**机制——本建议是在已有方案旁边再造一个方案，门槛直接撞上。We think A 过不了这条；B 作为"有界新增语法"也仍需论证与 `<%= %>` 的并存关系。
- **原则 7（避免隐蔽控制流/语义变化）** — 文本节点默认值翻转是最隐蔽的语义变化，`Return?` 的否决理由（*"Control flow would be altered by a very subtle character"*）按比例放大后正适用。这是 A 的第二道死线。
- **原则 9（消除常见样板）** — 目标正确：`<%= %>` 嵌套确实繁琐。但翻转把最常见（大部分静态）的模板场景变成必须引号化，样板没有消失，只是换了位置。九成场景吃亏换一成场景爽快，不划算。
- **原则 4（默认跟随 C#）** — C# 的 Web 模板答案（Razor）是**工具层**，不是语言特性；C# 没有把模板解析模式做进语言。偏离 C# 去发明一个语言级模板模式，需要"充分理由"（2018-12-19：*"we will follow C# unless there is a compelling reason"*），而本建议没有给出数据或场景上的充分理由。
- **原则 1（永不破坏）** — 纯新增形态（B/D）不破坏；翻转既有语义（A）破坏。红线见第 5 点。
- **对照主线表（2.3）** — UI/XML 一行：主线方向是 **IDE 工具**（2017-10-18 议程 "XML types for XML IntelliSense"，配合 `xml-schema-types` 建议的 Schema 类型），Anthony 方向是**语言语法**（嵌入模式、XAML 字面量、Schema 类型）。这不是"主线一致/更激进"，而是**工具 vs 语言的分叉**：主线把 XML 智能提示当 IDE 能力推进，Anthony 想把模板模式写进语法。We think 对 VBScript.NET 这个脚本型产品，语言级模板有真实吸引力，但这个分叉意味着本建议**与主线立场不一致**，需要在产品定位上给出明确交代。

### RESOLUTION:

**RESOLUTION:** We split the proposal into its separable promises, and rule on each separately. 不整体背书，也不整体否决。

1. **文件级 `<?vb?>` 解析模式（PROPOSAL A）— `Table`，附复活信号。** 否决的理由有三，逐条记档：(i) 文本节点"表达式还是文本"在文法上不可判定，翻转默认值把最常见静态场景变成引号化，且违反原则 7（隐蔽语义变化）；(ii) parser 动态切换复杂度量级接近 #211 被否的判断（*"It would require rewriting considerable parts of Roslyn"*），成本/价值失衡；(iii) 与既有 `<%= %>` 机制构成"第二种做事方式"，撞原则 3 的高门槛。**这不是否决方向的永久判断**——"纯 VB 模板"的吸引力是真的。复活信号：最小原型证明块级切换在真实 parser 上可行；文本/表达式边界给出**无启发式**的文法（我们暂时想不出，`OPEN QUESTION`）；XSS 输出编码设计；Web 模板场景在 VBScript.NET 目标用户中的占比数据。
2. **块级 `<?vb?>` 字面量（PROPOSAL B）— `Consider`，返工候选。** 作为有界表达式字面量与 `<?xaml?>` 对称，不切换文件模式、不改变既有 XML 字面量语义、不含隐式 `Return`/`Yield`。它把"语句驱动输出"让渡出去，换取零破坏与小实现面。返工必须先回答：文本/表达式边界文法、属性值规则、输出编码，以及与 `<?xaml?>` 统一"`<?...?>` 标记家族"。若这三项有解，B 是可在沙盒内孵化的形态。
3. **隐式 `Return`/`Yield` 子集（PROPOSAL D）— `Consider`，但被 `xml-schema-types` 阻塞。** 独立、纯 VB（继承 `FunctionName = value` 隐式返回传统），是五个承诺里最干净的一个。但 `As <ResponseMessage>` 依赖 XML Schema 类型，且 `Return`/`Yield` 选择规则（`Iterator` 绑定、多条 XML 语句、与显式 `Return` 混合、`Sub` 内语义）必须先定。在 `xml-schema-types` 落地前不单独推进；落地后作为其后续工作项。
4. **工具/代码生成（PROPOSAL C）— 推荐优先路径。** 用 source generator / 构建任务把模板编译为完整 VB 代码树，兑现原文 "Not Shown: Full-Fidelity VB Code-Trees" 的承诺，零语言表面扩张，与主线"XML types for XML IntelliSense"的工具方向一致。`.vbxhtml` 的 `<{ ... }>` 由工具消费或由本建议补一个 speclet 定义——见 OPEN QUESTIONS。
5. **红线：任何形态不得改变既有 XML 字面量的文本默认值。** 这是不谈判的。
6. **示例纪律：** 所有示例必须在 `Option Strict On` 下可编译；`document`、`includeDisclaimer`、`p` 的来源必须声明，或文档必须明确"环境对象"绑定机制。

**Implication:**

- 与 `xaml-literals` 对表，统一 `<?...?>` 标记家族的文法与职责（表达式 vs 模式）——TO DO。
- 与 `xml-schema-types` 建立依赖关系：D 的 `As <ResponseMessage>` 返回类型依赖它，交叉引用补上——TO DO。
- 与 top-level-code 会议记录**修正交叉引用**：那边把 `<{ ... }>` 归属本建议，但本建议未定义它。`<{ ... }>` 与 `<?vb?>` 是两个语法，归属需重新定夺——见 OPEN QUESTIONS。
- 若走 B，起草新 speclet：文本/表达式边界文法、属性值规则、输出编码语义。
- 状态行占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）必须替换为真实原型，否则证据止于"已提供"。

### OPEN QUESTIONS / TODO

- `OPEN QUESTION`：文本节点"表达式还是文本"的**无启发式文法**——"一律表达式"与"解析失败按文本"都不可接受，第三种方案我们没有想出来。
- `OPEN QUESTION`：属性值是否表达式、静态属性如何处理。
- `OPEN QUESTION`：隐式 `Return`/`Yield` 选择规则——`Iterator` 绑定？多条 XML 语句？与显式 `Return` 混合？`Sub` 内 XML 语句是"输出"还是丢弃？
- `OPEN QUESTION`：文本节点输出是否 HTML-encode（XSS 面），转义规则。
- `OPEN QUESTION`：`<{ ... }>` 与 `<?vb?>` 的关系——top-level-code 记录把 `<{ ... }>` 归给本建议，本建议未定义它；两份文档必须有一方修订。
- `TODO`：量化 Web 模板场景在 VBScript.NET 目标用户中的占比（数据/普遍性）。
- `TODO`：最小原型（块级字面量）验证 parser 可行性；替换状态行占位链接。
- `TODO`：与 `xaml-literals`、`xml-schema-types` 的交叉引用与依赖表。
- `Follow-up`：`InitializeComponent` 隐式自应用、流式写入 Builder 模式（原文两个 "Not Shown"）在 B 形态下是否还需要——`Probably` 不需要，B 已把这两个承诺排除在范围外。

### 状态

- **LDM 状态：`LDM Considering`（整体未关闭，拆分后逐项定夺）**——五个承诺已拆开，每项标了独立的后续动作；这与"整体 `Table`"不矛盾：`Considering` 表达设计团队对方向持保留的开放态度，`Table` 表达 ModVB 侧的优先级判定。
- **三态判定：Table（整体）** —— 作为规范文本未就绪，方向保留。五个承诺拆开后，B（有界字面量）与 D（隐式 `Return`/`Yield`）各自有资格在返工后进入 `Consider`；A 的完整形态在红线（原则 7、原则 3）上过不去；C 是最低风险、最高确定性的推进路径。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-embedded-vb-mode.md` — 嵌入 VB 解析模式（`<?vb?>`）。
- 来源：Anthony 原文**第 4 章 "UI, XML, XAML"**（`..\AnthonyDesign_wordpress.txt` L1120–1164），含 "Embedded VB Parsing Mode (No `<%= %>` or XML escapes required)"、XML 表达式语句隐式 `Return`/`Yield`，及三条 "Not Shown" 承诺（Full-Fidelity VB Code-Trees / `InitializeComponent` 隐式自应用 / 流式写入 Builder 模式）与四个 Past Demo 视频链接。
- 配方目标（Motivation 摘要）：消除 `<%= %>` 转义切换，让开发者写"纯 VB"模板；HTML 标签即 XML 字面量，文本与控制流都是真 VB 代码；XML 表达式语句在函数/属性体内隐式 `Return`/`Yield`。

### 五维评分表

| 维度 | 得分 | 评价（对照行为锚点） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **3/5** | 主效果（纯 VB 模板、免转义切换）书面清晰、示例可读、来自原文逐字；但核心语义（文本/表达式边界）未定型，关键子效果（语句驱动输出、隐式 Return/Yield 的选择规则、`.vbxhtml` 集成）缺失或未定义，未决关键点 ≥4 → 效果证据封顶；示例在 `Option Strict On` 下不可编译（`document` 未声明） | 已检查（Past Demos 视频止于"已提供"，无代码不可"已运行"复核） | 核心语法未定型 = 主效果未完整显现；文本节点边界矛盾（静态文本无法解析为表达式）；`document`/`includeDisclaimer` 未声明 |
| 特性 | **3/5** | 主体继承 VB 基因（XML 字面量、`<%= %>`、轴属性是纯 VB 血脉，"页面即程序"方向自然）；但明显借鉴外部模板模型（ASP/Razor/JSX 的文本即表达式、`<?...?>` 标记、隐式 Return）未说明，且打包了模式开关、文本表达式、语句输出、隐式 Return、Builder 等多个无关能力 → "明显借鉴外部但做了 VB 化改造；或打包了次要无关能力" | 已检查 | 未承认 ASP/Razor/JSX 来源；五个独立承诺捆绑未切分；与 `<%= %>` 构成第二种做事方式（原则 3） |
| 品质 | **3/5** | 六章节模板结构完整，示例与原文逐字一致，Unresolved questions 4 项如实列出（诚实，加分）；但状态行为占位链接（红旗），无文法（BNF）、无兼容性小节、无 Option Strict 分析，且**核心边界（文本/表达式）通篇缺席**——这不是未决，是没被看见 | 已检查 | 红旗：状态占位链接；无文法/无 breaking-change 分析；文本/表达式边界未进入文档视野；与 top-level-code 的 `<{ ... }>` 归属交叉不一致 |
| 属性 | **3/5** | 对 VBScript.NET（火=Web 模板新阶段、水=差异化——VB 唯一带一等 XML 字面量、光=盘活 VB6/经典 ASP 网页资产）明显正向；但风=与主线工具方向（XML IntelliSense）分叉、与 `<%= %>` 双语法风险；暗=parser 复杂度、文本语义翻转、XSS 未设计、与既有 XML 字面量冲突 → "有得有失，文档未充分权衡" | 已检查（预测性，标"待定"） | 未对冲暗风险：XSS 编码、文本/表达式歧义、与 `xaml-literals`/`xml-schema-types` 的依赖三处无应对设计 |
| 炼金成分 | **3/5** | 主要材料（Anthony 第 4 章）标注准确、三个 "Not Shown" 如实列出；但借鉴 ASP/Razor/JSX 模板模型未标注、拒绝的 `<%= %>` 恰是既有 VB 特性未点明、与 `<?xaml?>`/`<ResponseMessage>` 的姊妹建议依赖未交叉引用 → "部分来源未标注（借鉴了外部却没提）；标注与影响有偏差" | 已检查 | 未声明外部模板模型来源；`<?xaml?>`（同形标记）与 `<ResponseMessage>`（返回类型依赖）两份建议未交叉引用 |

### 设计原则对照

- **与 VB 基因：部分一致**。继承 XML 字面量血脉（原则 2 正向）、消除样板目标正确（原则 9）；但文本默认值翻转违反原则 7（隐蔽语义变化）、与 `<%= %>` 双语法撞原则 3（第二种做事方式）、偏离原则 4（C# 用工具而非语言做模板），且常见静态模板场景被引号化反而更繁琐，伤原则 5（读起来像英语）与原则 9 的本意。
- **与主线关系：与主线冲突（工具 vs 语言分叉）**。主线 2017-10-18 将 XML 方向定为 "XML types for XML IntelliSense"（IDE/工具），并把 JSON 字面量的嵌入语法判为 *"No strong feelings"*；Anthony 走语言语法（嵌入模式、XAML 字面量、Schema 类型），更激进且方向不同。`<?xaml?>` 姊妹建议与 `xml-schema-types` 建议同属这一激进延伸。
- **破坏性变更：取决于形态**。纯新增形态（B/D）无破坏（错误变程序）；翻转既有 XML 字面量文本默认值（A）构成破坏。与 `xml-schema-types` 结合时，`<ResponseMessage>` 类型名与既有 `XElement` 推断的转换关系需定义。

### 总评

- **达成程度：未达成（部分子集有价值）**。动机真实、方向有 VB 血脉，但核心语法（文本/表达式边界）在文法上不可判定，关键子效果（隐式 Return/Yield 规则、属性值、`.vbxhtml` 归属）未定义，示例不可在 `Option Strict On` 下编译，安全（XSS）未设计。作为规范文本未达成；作为"五个可拆承诺"的清单成立。
- **LDM 三态建议：Table（整体）**。A（文件级模式）Table；B（块级字面量）与 D（隐式 Return/Yield 子集）返工后可 `Consider`；C（工具/代码生成）为推荐优先路径，兑现原文 "Full-Fidelity VB Code-Trees" 承诺且零语言扩张。
- **主要问题**：(1) 文本节点"表达式还是文本"文法不可判定——特性核心的矛盾；(2) 文本默认值翻转违反原则 7、与 `<%= %>` 双语法违反原则 3；(3) `document`/`includeDisclaimer` 未声明，示例不可在 `Option Strict On` 下编译；(4) XSS 输出编码未设计；(5) 与 `xaml-literals`（`<?xaml?>` 同形）、`xml-schema-types`（`<ResponseMessage>` 依赖）、top-level-code（`<{ ... }>` 归属）三份文档的交叉不一致；(6) 状态行占位链接、Past Demos 无代码。

### 返工建议

- **补充章节**：文法（`<?vb?>` 的 BNF 归属——文件级 vs 块级；文本节点/属性值的文本-表达式边界）；兼容性与 breaking-change 分析（既有 XML 字面量文本默认值不变红线）；Option Strict 双路径行为；输出编码/XSS 语义。
- **补充证据**：块级字面量最小原型（替换状态行占位链接）；文本/表达式边界的无启发式文法或显式转义机制；所有示例在 `Option Strict On` 下编译通过（声明 `document` 或定义"环境对象"）；Web 模板场景在 VBScript.NET 目标用户中的占比数据。
- **未决问题处理**：逐条定夺——文本边界（倾向：块级字面量 + 文本节点一律表达式 + 静态文本须插值引号化，接受其繁琐，放弃 A 的文件级翻转）；属性值规则；隐式 Return/Yield（倾向：`Iterator` 绑定 + return 位置无显式 Return 时隐式返回）；`<{ ... }>` 与 `<?vb?>` 的关系（建议把 `.vbxhtml` 的 `<{ ... }>` 归还给 top-level-code 或本建议补 speclet，二选一，消除跨文档矛盾）。
- **范围切分**：明确本建议只承载"块级 `<?vb?>` 字面量"一个特性；语句驱动输出（A 的 If/For Each 内嵌）、隐式 Return/Yield（D）、InitializeComponent 自应用、流式 Builder、完整代码树（C）各自独立成文，避免一份建议承载五个承诺。

---

## 附录：C# 生态与互操作考量

> 本附录只追加不改写正文，评估维度是「C# 现实方向 vs 本提案响应」。先坦白一句边界：**本提案与 C# 的 interop 主线（Span/ref/unsafe、COM、AOT 低层，即索引 T2/T3/T4/T5）关系弱**——它不碰低层内存、不碰非托管调用、没有新 IL。真正相关的 C# 方向是**脚本化与工具化**（top-level statements、simple C# programs、polyglot notebooks、source-gen），即索引 T1/T8 与 M5 的延长线。下面如实按这个重心展开，不硬凑低层互操作。

### 一、相关 C# 现实方向（逐字引用 + 来源）

**R1 — Top-level statements（C# 9）：文件即程序，省去入口样板。**
C# 把"一个小文件就是一个程序"做成了语言特性，但机制是**编译器合成入口**，不是解析模式切换：

> "There's a certain amount of boilerplate surrounding even the simplest of programs, because of the need for an explicit `Main` method. This seems to get in the way of language learning and program clarity. The primary goal of the feature therefore is to allow C# programs without unnecessary boilerplate around them, for the sake of learners and the clarity of code."

→ `proposals\csharp-9.0\top-level-statements.md`（Motivation）

且入口有且只有一个（同文件 Detailed design）：

> "Only one *compilation_unit* is allowed to have *statement*s."

这与本提案"文件级 `<?vb?>` 模式"共享"文件即单元"的直觉，但 C# 只解决入口样板，不解决模板输出。

**R2 — Simple C# programs（LDM-2021-05-12）：瞄准从脚本语言过来的新用户，但把脚本化当作工具/项目结构问题。**
这份 LDM 讨论 `#r`/`#load` 这类**文件级指令**与 REPL 预期，明确把"脚本化"放在项目结构与工具层：

> "C# has historically had a focus on very enterprise scenarios: professional developers working on larger projects using a dedicated IDE. Our user studies have shown that this workflow isn't what many newer users are expecting, particularly if they're coming from scripting languages like Go, Javascript, or Python. These users instead expect to be able to simple make a `.cs` file and run it, with potentially more ceremony as they start adding more complex dependencies or other scenarios."

> "Investing in tooling that puts NuGet directives in C#, as well as potentially `#load` or other similar file-based directives, is going to necessitate that we reconcile file structure and project structure in C#."

> "What about output settings such as dll vs exe, or single-file and trimming settings? We don't have answers for these today…"（同段：这些答案要等与 SDK 团队讨论后落定）

→ `meetings\2021\LDM-2021-05-12.md`（Simple C# programs）

**R3 — 交互式/notebook 脚本生态：C# 的脚本答案是 "C#-adjacent tooling"。**
2022 年未来议程把交互式脚本列为工具方向，而非语言特性：

> "there is interest in investing more in the "C#-adjacent" tooling experience, and seeing what can be done to further improve the polyglot notebooks and interactive scenarios and bring them to parity with regular C# projects."

→ `meetings\2022\LDM-2022-08-24.md`（C# feature triage — Tooling improvements）

（注：dotnet interactive / polyglot notebooks 的 kernel 协议本体在 dotnet/interactive 生态，csharplang 仓库无正文——**Suspect / OPEN QUESTIONS**，需到该仓库核实。）

**R4 — 模板留在工具层（Razor）+ 编译期生成替代运行时动态（source-gen / interceptors / AOT）。**
- C# 的 Web 模板答案是 Razor，是**工具层/DSL 引擎**，不是语言解析模式——本会议正文第 10、14 点已借用的生态事实；csharplang 仓库无 Razor 正文，此处仅作生态常识引用（非仓库原文，标 Suspect）。
- 编译期生成压过运行时反射，interceptors 讨论把话说得很直接：

> "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible. To address this, we think that we need to take another look at the scenarios that are considering interceptors and see if we can put that information back into the type system"

→ `meetings\2023\LDM-2023-07-24.md`（Interceptors）

**R5 — dynamic / 表达式树边缘化（索引 T7）。**
C# 14 仅放宽表达式树里的可选/具名参数；`first-class-span-types` 还指出 ref struct 不被表达式树解释器支持。动态/晚期绑定不是 C# 的前进方向，反而被 AOT/trimming 视为负担。本提案 `Option Strict Off` 晚期绑定面正好落在这条逆风带上。

### 二、现实 vs 提案（对应表）

| 本提案组件 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| PROPOSAL A 文件级 `<?vb?>` 模式 | R1/R2/R3（入口省略 + 工具层，无语言级解析模式） | **冲突 / 脱节** | C# 解决"文件即程序"用编译器合成入口与工具指令，从未把模板解析模式做进语言；A 在 C# 生态没有同行者，复活信号缺生态印证 |
| PROPOSAL B 块级 `<?vb?>` 字面量 | R3（脚本/交互作为 C#-adjacent tooling） | **需桥接** | 作为 VB 差异化语言特性无 C# 对应；文本/表达式边界、属性值、输出编码都是 VB 独有的设计空白，C# 无参考实现可抄 |
| PROPOSAL C 工具/代码生成 | R4（source-gen 编译期生成；Razor 工具层） | **兼容（强）** | C# 生态的模板与元编程答案就在工具层；推荐路径 C 与 C# 现实同向，原则 4（默认跟随 C#）下最低摩擦 |
| PROPOSAL D 隐式 Return/Yield | R1（top-level 的 Main 合成是编译器隐式入口） | **兼容（弱）/ 需桥接** | C# 有"编译器隐式入口/返回"先例可类比；但 D 的返回类型绑 `xml-schema-types`，C# 无对应 |
| 文本即表达式 / `document` 晚期绑定 | R4/R5（把信息放回类型系统；dynamic 边缘化） | **冲突（条件性）** | Strict Off 晚期绑定=运行时反射，与 AOT/编译期生成张力大；默认应静态绑定环境对象 |
| XML 字面量 / 轴属性（VB 血脉） | 无（C# 无一等 XML 字面量；XAML 是工具层标记） | **脱节（差异化资产）** | 无冲突，也无生态借力；保留为差异化，但别指望 C# 侧工具支持 |

### 三、对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：模板的 `document`/`includeDisclaimer` 环境对象默认静态绑定（`Option Strict On` 默认 + 显式"环境对象"机制），晚期绑定做成显式 opt-in 的 COM/Office 兼容层——对齐 R4/R5（把信息放回类型系统、dynamic 边缘化）。XSS 输出编码与本建议正交，必须单独设计。
2. **source-gen 桥**：把 PROPOSAL C 作为 `.vbx` 模板主路径——`.vbxhtml` / `<?vb?>` 模板在编译期（source generator / 构建任务）转成完整 VB 代码树，与 C# 的 source generators / incremental generators（Roslyn）对齐；解释执行 / 动态程序集加载做成显式 opt-in 的传统兼容层（决策文件 M5 同款结论）。
3. **识别新元数据**：`.vbx` 编译器要能消费现代 C# 库，需认识 `CompilerFeatureRequired`、`RefSafetyRules`、`RequiresUnsafe`/`MemorySafetyRules` 等新元数据，否则模板里引用 C# 15 新类型时安全校验会错判（决策文件 M8 通用要求，此处只提醒不展开）。
4. **notebook / polyglot 桥接（本提案最现实的生态结合点）**：`.vbx` 若面向宿主嵌入场景，应作为 dotnet interactive / polyglot notebooks 的 kernel 或宿主协议接入方，复用 C# 已有的交互工具生态，而不是在语言里再造一套解析模式。这是 R3 的正面响应：C# 把脚本做成工具，`.vbx` 把脚本做成"可被这些工具托管的语言"。

### 四、对既有 RESOLUTION / 三态判定的影响

- **支持 RESOLUTION 第 4 点（PROPOSAL C 推荐优先路径）**：C# 现实（R4：模板与元编程留在工具层 + source-gen 编译期生成）给 C 提供了生态同行者。三态判定 "Table（整体）" 不变，但 C 的优先性被 C# 现实进一步背书——这是本附录最重要的确认。
- **削弱 A 的复活信号**：复活信号要求最小原型、无启发式文法、XSS 编码与用户占比数据；C# 生态从未把模板解析模式做进语言（R2/R4），A 没有同行者，"语言级模板模式"这一假设本身缺乏生态印证。复活信号清单不变，但每一项都更难拿证据。
- **B 的返工条件不受 C# 现实影响**：文本/表达式边界文法、属性值规则、输出编码是 VB 独有的设计空白，C# 无参考实现——C# 现实既不提供解法，也不构成障碍。
- **不改写正文**：以上全部是补充视角，RESOLUTION 与三态判定原文不动。

### 五、引用纪律 / OPEN QUESTIONS

**已逐字核实的 C# 原文（本附录引用，标注来源）：**
- Top-level statements Motivation → `proposals\csharp-9.0\top-level-statements.md`
- Simple C# programs 三段（enterprise 场景、`#r`/`#load` 文件结构调和、project settings 未定）→ `meetings\2021\LDM-2021-05-12.md`
- polyglot notebooks / C#-adjacent tooling → `meetings\2022\LDM-2022-08-24.md`
- interceptors / AOT 反射困境 → `meetings\2023\LDM-2023-07-24.md`

**OPEN QUESTIONS / Suspect：**
- dotnet interactive / polyglot notebooks 的 kernel 宿主协议细节——属 dotnet/interactive 生态，csharplang 仓库无正文 → **Suspect**，需到 dotnet/interactive 核实。
- Razor 作为"C# 的 Web 模板答案"是生态常识，csharplang 仓库无正文 → **Suspect**（生态事实，非仓库引用）。
- source generators 的正式设计文档在 Roslyn 仓库，csharplang 无 `proposals\csharp-9.0\source-generators.md` → **OPEN QUESTIONS**：正式文档路径待补。
- C# scripting（csi）的语法/宿主语义在 csharplang 仓库无提案正文 → **OPEN QUESTIONS**。
