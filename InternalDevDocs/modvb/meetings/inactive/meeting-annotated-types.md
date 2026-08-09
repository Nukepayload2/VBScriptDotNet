# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们处理一份很特别的建议——它是一把**伞**。`annotated-types`（注释类型 / 类型变体，Anthony 18.20）目前是 inactive，但它名下挂着五件事：XML/JSON schema 注释、匿名委托统一到 `Action`/`Func`、度量单位注释、轻量"类型提供器"，以及一份"寻找统一方案"的宣言。过去几场会议已经替我们拆过台：xml-schema-types 会议把"注释型变体"方向移交到了这里，json-literals 会议把 schema 标注判定为"与字面量正交的另一条线"并入这里，delegate-enhancements 会议把 `<Function(...)>` 的尖括号语法判了 Table，any-pseudotype 会议把 `Any` = `Object` + 注解的表示推进来对齐。也就是说，本建议的伞下已经攒了四份兄弟决议的移交物。今天的问题是：**这把伞该不该撑起来？**

## Agenda

* [Proposal: 注释类型 / 类型变体（Annotated types / Type flavors）](#proposal-注释类型--类型变体)

## Proposal: 注释类型 / 类型变体

_Related: [vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)（[#27 – Guid literals](https://github.com/dotnet/vblang/issues/27)）；[vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；主线会议纪要 2017-10-18（Annotated types / XML and JSON）；ModVB：`proposal-xml-schema-types.md`、`proposal-json-literals.md`、`proposal-delegate-enhancements.md`、`proposal-any-pseudotype.md`、`proposal-units-of-measure.md`_

### 场景与缺口

We started from the proposal's own opening——一段几乎逐字出现在主线 2017 年会议里的追忆。Anthony 的原文是：

> When building Roslyn we made the painful decision to cut this feature for time and I've been stewing over how to gracefully get it back.

说的是 VS2008 时代 VB 的 schema 文件驱动 XML IntelliSense（编辑器里对 XML 元素/属性给出提示），在构建 Roslyn 时因时间压力被砍掉；如今 JSON 地位上升，他希望用同样的思路给 `jsonObject!` 提供补全。We 想先钉死一个事实：**这个缺口主线不仅认，而且 2017 年就用几乎相同的措辞讨论过。** 2017-10-18 会议 "XML and JSON" 一节的问句是（逐字引用）：

> Is there an even richer notion of an annotated type which could be used to add and track simple information about XML and JSON literals to better enable the IDE to provide completion when using XML-axis properties or the dictionary-access operator?

并给出雏形（逐字引用）：

```vb
Dim x As <contact>     ' Type: <contact>
Dim y = x.<address>    ' Type: <contact>.<address>
Dim z = y.<city>.Value
```

```vb
Dim a As {"contact"} ' Type: {"contact"}
Dim b = a!address    ' Type: {"contact"}.{"address"}
```

也就是说，`As <contact>` 与 `As {"contact"}` 这两个语法形态、以及"轴访问逐步精化类型"的心智模型，主线在 2017 年就已经画出来了，并且画在了同一句话里。本建议不是新方向，而是**把主线当年没往下走的那一步捡起来**。所以本场的开场问题不是"要不要"，而是两件事：**主线为什么没往下走？我们有没有能力走通？**

建议原文的动机给出了一层解释：`<PurchaseOrder>` / `{"trade-message"}` "don't have to be realized as their own types (in fact it's better if they aren't) but more of an annotation on e.g. `XElement` or `JsonObject` that tools can use"（逐字引用）。先例是元组名——"`(x As Integer, y As Integer)` doesn't generate a new type but annotates the declaration in such a way as to surface those names on `ValueTuple(Of Integer, Integer)`"；以及 `dynamic`——"C# does basically the same thing to represent `dynamic` which is otherwise represented as `System.Object` within the .NET runtime. And ModVB will do the same with the `Any` type."（均为逐字引用）。然后它把话题扩到匿名委托"风味"、度量单位、轻量类型提供器，最后一句话是（逐字引用）：

> At this point I'm laughing maniacally. But yeah, that's **6** scenarios and I thought up like 2-3 more after I stopped writing this section. I think that's enough to justify searching for a unified solution.

We 认真对待这句"searching for a unified solution"，但整场会议我们一直在追问同一件事：**六个场景真的指向一个统一方案，还是它们只是共享一个词（annotation）？** 我们倾向于后者，而这场会议的几乎所有结论都从这里长出来。

### 候选方案

**PROPOSAL A — 统一"类型变体/注释"机制（伞型建议的完整形态）。** 编译器理解注释：定义一种或一族语法，把注释附着在变量/类型引用上，注释经类型推断传播、在语义模型中可查、可选地以元数据（attribute）形式进产物程序集，供 IDE 与诊断使用。XML 侧 `<geo:Address>`、JSON 侧 `{"trade-message"}`、委托风味 `<Function(...) As ...>`、单位注释、类型提供器元数据都是同一机制的不同拼写。这是建议原文"searching for a unified solution"的字面实现。

**PROPOSAL B — 只统一"表示"，不统一"语法"（We 的本场倾向）。** 承认五种子场景语法各异、且各自有各自的归属（XML/JSON 归 schema 注释、委托归 delegate-enhancements、单位归 units-of-measure、类型提供器待语法落地），但统一两条**红线**：① 注释 = "底层类型 + 元数据"，运行时表示不变、不改变绑定；② 注释的表示（attribute / 语义模型注解表）与元组名、`dynamic`/`Any` 的表示同族，互相一致。伞作为"机制契约"存在，不作为"新语法"存在。

**PROPOSAL C — 纯 IDE / 分析器，语言零改动。** 建议原文 Alternatives 的第 3 条（"用分析器 / 编辑器扩展读取元数据"），也是 2017-10-18 主线明确表达过的倾向——"We might be able to do a lot with no compiler changes and should investigate with IDE team."（逐字引用）。XML/JSON 补全交给编辑器从 schema 文件/初始化结构做启发式；语言不动。

**PROPOSAL D — 属性（attribute）路线。** 2017-10-18 对 #184（Tagged String literals）的反馈逐字写道："Using attributes would be better than a special tag."、"Attribute should be on the producing APIs rather than the strings themselves."——验证类场景（GUID、连接串、日期格式）由产生这些值的 API 挂属性 + 分析器消费。这是主线程为"带注释字符串"画的另一条路。

**PROPOSAL E — 什么都不做，维持 inactive。** 伞不撑，六个场景各自按各自归属的会议处理；其中 XML/JSON 注释等 IDE 调查（C）的结果。

### 权衡：Q&A

- **六个场景真的需要"统一方案"吗？** 我们把六个场景排开：① XML schema 注释（`order As <PurchaseOrder>`）、② JSON schema 注释（`request As {"trade-message"}`）、③ 匿名委托风味（`<Function(...)>`）、④ 单位注释、⑤ 类型提供器元数据、⑥（Anthony 后补的）2-3 个未写明场景。它们的语法互不相同，宿主类型互不相同（`XElement` / `JsonObject` / `Func` / 数值 / 任意类型引用），消费方也互不相同（轴补全 / `!` 补全 / API 签名展示 / 数值运算校验 / 数据库与 REST 补全）。**We 不认为存在一个共同的语法面**——建议自身的 Unresolved #1 也在问"是否存在统一语法"，我们给的答案是否定的。真正能统一的只有"表示"与"不改变绑定"这两条契约，这正是 PROPOSAL B。
- **元组名与 `dynamic` 的先例有多硬？** 很硬，但要说清楚它证明了什么。元组名是挂在 `ValueTuple(Of Integer, Integer)` 上的**确定位置**（元组成员）的注释；`dynamic`/`Any` 是挂在 `Object` 上的**类型整体**的注释（`DynamicAttribute` 等价物）。它们都证明"底层类型 + 元数据"在 CLR 上可行、PEVerify 无碍。但它们也暴露了本建议与它们的**本质差异**：元组名和 `dynamic` 的宿主类型与注释之间是编译器强制的关系（元组名只能挂在元组上，`dynamic` 只能挂在 `Object` 上），而 `<PurchaseOrder>` 的宿主是任意 `XElement` 变量——注释与宿主之间没有编译器强制的约束。这意味着"注释"在本建议里比在元组名里**更自由也更不可信**：元组名不会错，`<PurchaseOrder>` 可以标在不是 PurchaseOrder 的 XML 上。这不是反对理由，但它是"注释经类型推断传播"时最危险的种子（见深度追问第 3、5 条）。
- **主线为什么没往下走？** 2017-10-18 的纪要里，"Annotated types" 一节的收尾是两句没有决议的反馈："This is one end of the spectrum of providing a better tooling experience for untyped data over the wire. Type providers are on the other end." 与 "We might be able to do a lot with no compiler changes and should investigate with IDE team."（均为逐字引用）。也就是说，主线把方向画成了"无编译器改动 / 类型提供器"的一条光谱，然后**把作业留给了 IDE 团队**，自己没有收尾。而 #184（Tagged String literals）那一边，反馈明显偏向了属性路线（"Using attributes would be better than a special tag"）。We 认为主线不是否定注释类型，而是**对"要不要让编译器理解注释"这个问题悬置了**——纪要里那三条 "Bullets dodged"（编译器理解注释并经类型推断保留、把注释语义一般化、注释任意类型）逐字记录的都是被躲开的子弹，不是被接受的方案。本建议恰恰是去捡这三颗子弹。捡起来没有错，但要意识到自己是在主线明确躲开的雷区里走。
- **属性路线（D）和语法路线（A/B）的边界在哪里？** 2017 对 #184 的反馈其实已经把地图画好了：验证类场景（"这字符串是 GUID / 连接串"）用 **API 上挂属性**，人人受益、不改调用方代码；"string literal 上贴 tag" 被否决（"Using attributes would be better than a special tag"）。而**类型变体场景**（"这个 `JsonObject` 是 trade-message"）需要的是**变量级**注释，API 属性覆盖不了调用点的变量——`request` 可能是从任何地方传进来的。所以两类场景的答案本就不同：验证类走属性 + 分析器，类型变体走变量注释。**这又是一次"统一方案"的反证**——连 2017 主线自己都没打算用一套机制覆盖两类。
- **`<Function(...)>` 风味是这条建议的资产还是负债？** 见 delegate-enhancements 会议的 RESOLUTION #7：`<Function(...)>` 显式匿名委托类型因与 XML 字面量/轴共享 `<` 主权字符、无 C# parity、类型系统工作量高，被判 **Table**。本建议把它改造成"`Func(Of Node, Node, Node)` 的带名风味"重提——概念不同了（不引入新类型，只给 `Func` 加参数名注释），但**语法一样撞 XML**。We 认为"参数名浮现在 `Func` 上"这个需求是真实的（`Func(Of Node, Node, Node)` 的两个参数都叫 `Node`，读不出 `original`/`rewritten`），但它的解法未必是尖括号语法——参数名可以像元组名一样作为**注释**挂在 `Func` 上，甚至可以是分析器层面的东西。语法形态继承 Table。
- **"不信任第三方代码跑在编译器里"之后还剩下什么？** Anthony 对类型提供器的取舍很清醒：不做 F# 那种编译期代码生成/注入，因为"a little less ... trusting of 3rd party code running inside the compiler"（逐字引用）。但一旦放弃"第三方代码进编译器"，剩下的"给任何类型引用附加任意元数据、工具据此补全"就**没有编译器侧的工作可做了**——数据库/API 的补全逻辑全在编辑器扩展里。那么语言还需要做什么？Anthony 自己承认 "I haven't landed on an intuitive syntax"（逐字引用）。一个连语法都没有、编译器侧无事可做的场景，不该进语言管线。而且他的示例是"满栈"的：`Let db = New SqlDbProxy(For "AdventureWorks")()` 依赖关键字实参、`For Each p In db.Tables!Products` + `Where p!Price(As Decimal) >= 100` 依赖 `!`、后置转换 `(As Decimal)`、`For Each ... Where` 增强，`returnValue:=Out code As UInt32` 依赖 `Out` 实参——**这个场景要同时等五个其他特性落地才能被写出**。We Suspect 它是一幅愿景图，不是一份设计。
- **Erik Meijer 的专利轶事可信吗？** Anthony 转述："Once Erik Meijer (who was a significant influence on VB9) told me that the way the XML IntelliSense was done was actually essentially 'type provider', at least for the purposes of filing a patent on it."（逐字引用）。We 无从核实这次对话与专利主张，标记 `Suspect`。它不影响设计，但被用作"寻找统一方案"的论据之一，证据等级不够。
- **单位注释与 units-of-measure 是互锁还是分层？** units-of-measure 建议自身把"真实类型 vs 数值变量上的注释"列为未决问题，且其唯一无争议结论是**泛型运算符**（单位系统的库级地基）。本建议想用注释形态承接单位，等于在 units 定案之前替它做决定。**We 明确：两案互锁，本建议不抢先定单位的形态；泛型运算符先行。** 详见 OPEN QUESTIONS。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

本建议没有给出统一文法，因为每个子场景的语法都撞上既有主人：

- `As <PurchaseOrder>`：`<` 是 XML 字面量与 XML 轴属性的主权字符（delegate-enhancements 会议已为同类语法做过完整推演）。类型位置（`As` 之后）今天没有任何类型文法以 `<` 起头，所以它是加性的——但 tokenizer 的 XML 扫描是上下文相关的，`<Function(` 在泛型实参（`List(Of <Function(...)>)`）等位置极易被误判进 XML 词法。We 无法给出干净的判定规则，`Suspect`：需要原型验证，失败概率不低。
- `As {"trade-message"}`：`{` 在 VB 里已有五种含义（数组字面量、`With` 成员初始化器、`From` 集合初始化器、JSON 对象字面量提案、schema 标注），加注释是第六种。`{` 后跟一个字符串字面量（`{"trade-message"}`）与 JSON 对象（`{ "k": v }`）、数组（`{"a", "b"}`）在词法上区分靠冒号/逗号，可解析但必须写进 spec。
- 委托风味 `<Function(...)>`：继承 delegate-enhancements 的 Table——同上，`<` 撞 XML。
- 单位注释、类型提供器元数据：**语法未定义**。Anthony 对类型提供器明确 "haven't landed on an intuitive syntax"。

**结论：五个子场景五种语法，无统一文法。** 这正是"统一方案"最直接的失败面——它要的语法统一不存在，能统一的只有表示与契约（B）。

#### 2. 角案例 / 边界语义

- **轴结果类型分叉**：xml-schema-types 会议已否决"schema 说 `Country` 最多一次就把 `.<Country>` 变单值"——轴结果维持集合语义，注释只贡献补全与诊断。本建议必须继承这条决议，否则与自己的移交物打架。
- **`.Value` 语义**：`address.<Country>.Value` 的聚合语义不动。注释不重定义成员。
- **注释经类型推断传播**（2017 主线 "Bullets dodged" 的问句，逐字："If the compiler understood annotations it could preserve them through type-inference at least."）：`Dim x = order` 时 `x` 是否继承 `<PurchaseOrder>` 注释？`Dim list = {order1, order2}` 时数组元素是否继承？`Function F(o As <PurchaseOrder>)` 返回它时返回类型注释是什么？若传播，传播到哪一层、何时截断（跨越泛型实例化？跨越异步边界？）——**这是本建议最核心的未设计问题**，且它直接威胁原则 #7（见第 5 条）。
- **泛型 / ByRef / 属性字段**：`List(Of <PurchaseOrder>)`、`ByRef address As <PurchaseOrder>`、属性 getter 返回 `<PurchaseOrder>`——注释在结构类型内部的附着点未定义。元组名先例只覆盖"元组成员"，覆盖不了这些。
- **递归 schema / `xs:any`**：xml-schema 会议已判注释形态可处理递归（不建类型、不展开），`xs:any` 回落 `XElement`。继承该决议。
- **空集合与可选元素**：schema `minOccurs="0"` 只影响补全列表，不影响运行时。
- **类型提供器示例**：`db.Tables!Products` 的 `!` 链在注释模型下仍按 `!` 字典访问绑定；`p!Price(As Decimal)` 依赖后置转换。边界全部悬空。

#### 3. 作用域与绑定

注释模型下语义模型几乎不变：`order` 的类型仍是 `XElement`，`order.<Country>` 仍绑定 `Elements("Country")`，只是符号上多挂一份"schema 注释"供 IDE 与诊断消费。注释**不得参与符号身份与重载决议**（与元组名一致——元组名不参与相等性）。真正要设计的是：注释的语义模型呈现（`GetTypeInfo` 之外的新 API？注解表？）、注释经类型推断传播时的"绑定是否改变"。We 明确：**注释只作为附着在类型/变量上的旁路信息，不进绑定器的主路径**——这条是 A 与 B 的分水岭。若注释参与绑定（例如 schema 说 `Country` 是字符串，就让 `.<Country>` 的结果类型变成 `String`），就退化成 xml-schema 会议否决的语言级"类型"形态。

#### 4. 与既有特性的交互

- **XML 轴 / `.Value`**：零改变（继承 xml-schema 决议）。
- **`!` 字典访问**：2017-08-23 已判 `!` 的读价值"all the value just evaporated"（主线的评语）；注释给 `request!` 加补全是增量，但**不得把晚期绑定/字典访问改为早期绑定**。
- **Late binding / Option Strict Off**：注释不得改变既有绑定；Strict Off 下 `request!foo` 继续编译，注释只做补全提示，不做绑定性诊断（继承 xml-schema 决议第 8 条）。
- **TypeOf 流分析**：`Dim order As <PurchaseOrder>` 的底层类型是 `XElement`，流分析仍按 `XElement` 收窄；注释与收窄正交，不需要互操作规则（`Probably`）。
- **`Any` / 元组名**：`Any` = `Object` + 注解（any-pseudotype 会议已对齐）；元组名 = `ValueTuple` + 名字注释。本建议的表示必须与这两者同族，否则"伪类型/注释类型"机制四分五裂。**这是 PROPOSAL B 里唯一必须立刻做的事。**
- **属性（attribute）本身**：若注释以 attribute 形式落地（`TupleElementNamesAttribute` / `DynamicAttribute` 的机制），与用户手写 attribute 的交互（冲突、重复、遮蔽）要定义。

#### 5. Breaking change 与兼容性

- **现稿零破坏**：`As <...>`、`As {"..."}` 今天都是解析错误，加性语法。
- **潜在破坏面在"注释经类型推断传播"**：若 `Dim x = order` 的 `x` 继承了 `<PurchaseOrder>` 注释，而注释又影响后续绑定/补全/重载解析，那么**重编译后同一源码可能解析到不同的成员或不同的警告集**。这与 TypeOf 流分析会议遇到的"启用型"问题是同一族，但更隐蔽——流分析收窄是"启用先前会失败的绑定"，注释传播是"改变可见信息"。若注释只做补全不做绑定，破坏为零；一旦参与绑定，就需要 `langversion` 门控与警告策略。**We 的立场：v1 注释不进绑定路径，破坏保持为零。**
- **标识符冲突**：`<`、`{` 都不是新关键字，无保留字问题；但 `{"trade-message"}` 与未来 JSON 对象字面量共享 `{`，文法冲突面要专门设计。

#### 6. Option Strict / 编译选项分叉

注释必须在 Strict On/Off 下行为一致。Strict On：注释驱动补全，并可选地对"schema 明确不允许"的轴访问产生**警告**（非错误）；Strict Off：注释仅驱动补全，不改变任何绑定（继承 xml-schema 决议第 8 条）。两条路径对"注释是否存在"的可用成员集合保持一致。We 认为这是可行且必须的，但要注意：Strict Off 下 `request!foo` 今天就是晚期绑定/字典访问，补全列表里多出 schema 成员不改变语义；Strict On 下同理。**分叉风险低。**

#### 7. IDE / IntelliSense 影响

这是本特性的**全部价值面**，但建议原文对 IDE 只字未提。要问的：`order.<` 之后补全子元素名、`order.@` 补全属性名、`request!` 补全 JSON 属性名；InfoTip 显示 `<PurchaseOrder>` 还是 `XElement`；schema 缺失（前缀未解析）时降级提示；注释经类型推断传播后 IDE 如何展示。2017-10-18 主线把 "investigate with IDE team" 留成了作业——**这份作业到今天没有交**。We 明确要求：在谈任何语言语法之前，先回答"能否零编译器改动在 IDE 侧达成"（PROPOSAL C 调查）。若 C 能达成 80%，语言侧只需要最薄的注释语法；若不能，我们才知道语言要背多少。

#### 8. 数据 / 普遍性

最软的一环。六个场景里：JSON 注释的普遍性最强（`jsonObject!` 补全在云原生业务里是真实高频），XML 注释次之（但 schema-first 是 niche，xml-schema 会议已定性），委托风味再次（`Func/Action` 已覆盖多数签名），单位注释与类型提供器是边缘。**没有任何量化数据**——`Suspect`：这更多是 Anthony 的"6 个场景 + 2-3 个后补"的自证，不是市场证据。而 2017 主线对同类话题（GUID 字面量）的结论值得引为镜鉴："There isn't a good case to be made ... The only notable benefit I've heard from users is _validation_."——验证需求最终被导向了属性 + 分析器。We 不确定注释类型的客户群能大过当年的 GUID 客户群。

#### 9. 更简替代

- **属性路线（D）**：2017 主线的首选。验证类场景全覆盖，人人受益。局限：覆盖不了调用点的变量级注释。
- **纯 IDE / 分析器（C）**：2017 主线的字面倾向。局限：意图要猜（分析器 "must inspect every string literal in the program" 且要判断意图——2017 对分析器路线的两条顾虑逐字记录了）。
- **元组名式的"位置注释"**：委托风味可能用 `Func` 上的参数名注释达成，语法未必需要尖括号。
- **保持现状**：XML/JSON IntelliSense 继续缺席。"We're proud not to do anything" 在桌上，而且对 80% 的"安静客户"来说，缺失的只是 IDE 提示，不是语言能力。
- **`IOperation` 分析器**：2017 对 #184 问过 "Could this be done with an analyzer? Does IOperation have the perf it needs to do this? _Test it_."——这条 `_Test it_` 的作业也没人交。先交作业，再谈语法。

#### 10. 复杂度 / 成本 / 优先级

- **A（统一机制）**：四条新语法 + 注释表 + 经类型推断传播 + 语义模型 API + attribute 发射 + IDE 协议 + spec，接近一个大型特性。且"传播规则"的设计难度接近可空性流分析。
- **B（统一表示）**：只做"注释 = 底层类型 + 元数据"的契约与表示一致性，成本小，且能立刻被 `Any`/元组名采纳。
- **C（IDE 调查）**：编辑器侧工作，成本在 IDE 团队，但这是 2017 主线明文的作业。
- **优先级**：低于模式匹配、可空性流分析、`Any`。伞型建议**不是头条特性**。对 VBScript.NET（整片无类型数据原住民代码待迁移），JSON 注释的价值最高，但也是 C/D 已部分覆盖的场景。

#### 11. 运行时 / CLR 硬约束

无 PEVerify 障碍。注释的运行时表示只有两条路：**attribute**（`DynamicAttribute` / `TupleElementNamesAttribute` 机制，VB 2018-02-07 对可空注释的评估也提到 "It is possible that VB could add the emitting attributes based on a simpler attributing system"——逐字引用）或**纯语义模型/编译器内部表**（不发射，只供 IDE）。前者有跨程序集可见性的好处（API 边界上 `Func(Of Node, Node, Node)` 的参数名能读出来），后者零产物体积。**"任意元数据附在任意类型引用上"**若真要发射，attribute 的目标（targets）是否覆盖所有附着点（局部变量、表达式、语句）是硬约束——CLR 属性只能挂在声明的既定目标上，Anthony 想要的那种"表达式级元数据"（`p!Price(As Decimal)`）没有 CLR 对应物。`Suspect`：这可能是类型提供器语法迟迟"未落地"的深层原因。

#### 12. 值不值得做

按价值 × 成本 × 风险逐条打分：

| 子场景 | 价值 | 成本 | 风险 | 判定 |
|--------|------|------|------|------|
| 统一机制（A，伞） | 中（IDE 补全面广） | 高（四语法 + 传播 + 语义模型） | 中高（传播改变可见信息、文法六义） | **不值得作为伞做** |
| 表示统一（B） | 中高（`Any`/元组名/注释同族） | 低（契约 + attribute 机制） | 低 | **值得，立刻做** |
| XML/JSON 注释（挂在 B 下） | 高（jsonObject! 补全） | 待 IDE 调查定 | 低（继承 xml-schema 决议） | **值得，但先交 IDE 作业** |
| 委托风味 | 中（`Func` 参数名） | 中 | 中（尖括号撞 XML） | **Table**（继承 delegate-enhancements） |
| 单位注释 | 中 | 未定 | 未定 | **互锁 units-of-measure，不先行** |
| 轻量类型提供器 | 低（示例不可编译、语法未落地） | 高 | 中 | **Table / Suspect** |

**We 的结论：伞不值得撑，撑伞之前先交 2017 的作业。** 若坚持把五件事捆绑成一个统一语言特性，我们会建议整份维持 inactive。

### VB 基因对照

- **不破坏现有代码（原则 #1）**：注释不进绑定路径则满分（加性语法 + 零绑定变化）；一旦"注释经类型推断传播"参与绑定/重载解析，就有隐蔽破坏面，需要 `langversion` 门控。We 要求 v1 走"零破坏"路径。
- **保持 VB-like（原则 #2）**：`order As <PurchaseOrder>` 读起来像它描述的 XML，`request As {"trade-message"}` 读起来像它描述的 JSON——很 VB（xml-schema 会议已认可）；但 `<Function(...)>` 撞 XML、`{"..."}` 撞 `{` 六义，形态冲突面大。
- **不引入"第二种做事方式"（原则 #3）**：**最大扣分项**。声明一个 XML 变量从此有三种写法（`As XElement`、`As <geo:Address>`、`As XElement + 属性`）；"给数据加类型信息"已经有真实类型、属性、分析器三条路，注释是第四条。除非收编进"底层类型 + 元数据"的统一表示（B），否则扩展表面失控——建议自己的 Drawbacks 也承认"极易失控"。
- **默认跟随 C#（原则 #4）**：C# 没有注释类型语法——它用 `DynamicAttribute`（属性）表示 `dynamic`，用 `TupleElementNamesAttribute` 表示元组名。**先例全都指向"属性，不是语法"**。F# 类型提供器被 Anthony 明确拒绝。本建议在"语法 vs 属性"上偏离了既有先例，需要充分理由，而它没有给。
- **读起来像英语、对新手友好（原则 #5）**：`order As <PurchaseOrder>` 零括号零前缀，新手能读；这是真优点，也是 XML/JSON 注释值得保留的原因。
- **不为边缘场景加特性（原则 #6）**：单位、类型提供器是边缘；JSON 注释不是（但已被 C/D 部分覆盖）。
- **避免隐蔽的控制流/语义变化（原则 #7）**：风险项在"注释经类型推断传播"。若传播改变重载决议或警告集，就是隐蔽语义变化——与 `Return?` 被拒、TypeOf 收窄需要"启用型"规则是同一族问题。
- **不与既有语法冲突（原则 #8）**：`<` 撞 XML（delegate-enhancements 会议已判 Table）、`{` 撞六义（json-literals 会议已列）。双重命中。
- **消除常见样板（原则 #9）**：弱命中。本特性消除的是"无 IntelliSense 的摸索"与"手写 `Deserialize` 后的补全缺失"，不是语句样板。对 VBScript 迁移者，`jsonObject!` 补全是真实增益，但它主要是 IDE 体验。
- **冗长只在有用时是美德（原则 #10）**：n/a。
- **与主线关系（对照表 2.3）**：主线 2017-10-18 已讨论同一方向并倾向"无编译器改动 + IDE 调查"，此后无在案后续（vblang proposals 目录无对应提案，`Probably`）；本建议把方向推成语言级统一机制 = **Anthony 独立延伸且更激进**，与 `null` 字面量、空安全全家桶同构（主线保守、Anthony 激进）。但其中"表示统一"（元组名/dynamic 先例）与主线讨论一致。

### RESOLUTION:

1. **伞型"统一类型变体机制"（PROPOSAL A）以现稿形态不进入设计管线。** 五个子场景五种语法、无统一文法、传播规则未设计、类型提供器语法未落地、示例不可编译（`? api.HttpGet("/repos/...")` 里的 `?` 是即时窗口提示符，不是合法 VB 语句——与本系列 any-pseudotype / postfix-casting 相同的示例缺陷）。**Table。** 本建议维持 inactive，但不 Reject（主线 2017 讨论未关闭、元组名/dynamic 先例真实、xml-schema 会议已把方向移交过来）。
2. **采纳 PROPOSAL B 的表示统一红线**：任何"注释类型"必须满足 ① 运行时表示不变（底层类型照旧，如 `XElement` / `JsonObject` / `Func`）；② 不改变绑定（不进绑定器主路径，不参与符号身份与重载决议）；③ 注释 = 底层类型 + 元数据，其表示与元组名（`ValueTuple` + 名字注释）、`Any`（`Object` + 注解）、`dynamic`（`DynamicAttribute` 机制）同族。**这是伞下唯一立刻可做的事。**
3. **继承 xml-schema-types 会议的移交**：XML/JSON schema 注释（`<geo:Address>` / `{"trade-message"}`）作为本机制的第一、二个用例推进，语法保留 `<ns:Name>` / `{"uri"}` 形态（与 2017-10-18 主线样例一致），轴结果维持集合语义、`.Value` 不动、注释不改变绑定。
4. **交 2017 主线的 IDE 作业**：在谈任何 XML/JSON 注释的**语言语法**之前，完成"纯 IDE / 分析器可否零编译器改动恢复 schema 驱动的补全"的调查（2017-10-18："We might be able to do a lot with no compiler changes and should investigate with IDE team."）。若可行，语言只加最薄的注释语法（甚至不加）；调查结果决定激活信号的强度。
5. **委托风味 `<Function(...) As ...>` 继承 delegate-enhancements 的 Table**：`<` 与 XML tokenizer 冲突、无 C# parity。将"`Func(Of Node, Node, Node)` 参数名浮现在 API 签名"作为**注释用例**（挂在 `Func` 上的参数名注释，非尖括号语法）单列，与表示统一（B）一起评估。
6. **单位注释与 units-of-measure 互锁，本建议不抢先定形态**：units 的唯一无争议结论（泛型运算符）先行；"真实类型 vs 注释"由 units 建议定，本建议只保证"若选注释形态，则遵守第 2 条红线"。
7. **轻量类型提供器 = Table / Suspect**：语法未落地（Anthony 自认）、示例依赖五个其他特性、Erik Meijer 专利轶事未核实、CLR attribute 目标覆盖不了"表达式级元数据"（`Suspect`：这可能是语法难产的原因）。工具侧价值由 IDE 扩展达成，不构成语言特性。
8. **激活信号（维持 inactive 所绑定的信号）**：本建议在以下条件齐备前保持 inactive——（a）第 4 条 IDE 调查完成并证明"必须语言语法才能达成补全"；（b）出现真实的量化数据或用户反馈（JSON/XML schema 驱动开发的占比、`jsonObject!` 补全的需求频次）；（c）第 2 条表示统一红线已被 `Any` / 元组名 / 至少一个注释用例采纳落地；（d）每个子场景拆出独立的单页 spec，不再以伞形捆绑。

### Implication:

- 起草一份"注释表示与传播"speclet（PROPOSAL B）：注释 = 底层类型 + 元数据的表示选型（attribute vs 语义模型表，参考 `TupleElementNamesAttribute` / `DynamicAttribute` 机制与 2018-02-07 可空注释评估）、附着点（变量/参数/返回/泛型实参）、经类型推断传播的规则（传播到哪一层、何时截断）——其中传播规则回应 2017 主线被躲开的子弹："If the compiler understood annotations it could preserve them through type-inference at least."
- 完成 2017 主线遗留作业：IDE/分析器可否零编译器改动恢复 XML/JSON schema 补全；并评估 `IOperation` 分析器的性能（2017 对 #184 的 "_Test it_"，逐字）。
- 与 any-pseudotype / 元组名团队对表，把"`Object` + 注解"、"`ValueTuple` + 名字注释"、"`XElement` + schema 注释"的表示统一为一份契约。
- 将委托风味（`Func` 参数名注释）单列为一个小建议，与 delegate-enhancements 的 Table 决议并行推进，不复活尖括号语法。
- 类型提供器场景写入 OPEN QUESTIONS 而非设计管线；标注 Erik Meijer 轶事与"VS2008 被砍"为 Suspect，待独立核查。
- 单元测试约束（无副作用：不启动进程、不写文件、无网络）：若做注释表示原型，测试仅覆盖编译期/emit 断言（attribute 发射正确性、语义模型注解表查询）。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：**统一语法是否存在**——建议自身 Unresolved #1。We 的倾向答案：不存在也不必要，统一的是表示（B）；但若有人能给出一个覆盖 XML/JSON/委托的单一语法，值得回来重开。
- `OPEN QUESTIONS`：注释经类型推断传播的精确规则——`Dim x = order` 是否继承 `<PurchaseOrder>`？跨越泛型实例化、异步边界、`Func` 构造时是否截断？这是本建议最有价值也最危险的未决点。
- `OPEN QUESTIONS`：注释在泛型（`List(Of <geo:Address>)`）、ByRef、属性/字段、函数返回类型上的附着点与存活规则。
- `OPEN QUESTIONS`：注释的发射形态——attribute（跨程序集可见、参数名能读）vs 纯语义模型表（零产物体积）；两者在 API 边界上的行为差异。
- `OPEN QUESTIONS`：`{"trade-message"}` 与 JSON 对象字面量、数组字面量共享 `{` 的消歧规则（json-literals 会议遗留的"第六种 `{`"）。
- `OPEN QUESTIONS`：TypeOf 流分析 / `Any` 动态调用与注释的交互（v1 我们倾向正交，未定案）。
- `Suspect`：Erik Meijer "XML IntelliSense 即 type provider / 专利"轶事；"VS2008 特性被砍于 Roslyn 构建期"为 Anthony 自述，主线文档仅能佐证"XML IntelliSense 缺失"这一现状，无法佐证"砍"的时间与决策细节，需独立核查。
- `TODO`：量化 JSON/XML schema 驱动开发的真实占比；`jsonObject!` 补全的需求频次（VBScript.NET 迁移代码尤其相关）。
- `TODO`：完成 2017 主线 IDE 调查作业并记录结论——这是激活信号 (a) 的唯一证据来源。
- `Follow-up`：与 any-pseudotype（`Object` + 注解）、delegate-enhancements（`<Function(...)>` Table）、units-of-measure（真实类型 vs 注释）、json-literals / xml-schema-types（`{` 六义、轴语义）对表；跟踪主线 #184 / #101 / #139 状态。

### 状态

- **LDM 状态：伞型建议 LDM Considering（维持 inactive）**；表示统一红线（B）= **LDM In Process**；XML/JSON 注释 = **LDM In Process**（绑定 IDE 调查）；委托风味、单位注释、轻量类型提供器 = **LDM Considering（Table）**。
- **三态判定：Table** — 维持 inactive，附明确激活信号。不是背书，也不是否决：主线 2017 的讨论未关闭，元组名/`dynamic` 先例是真实的，xml-schema 会议已把注释方向移交过来；但"统一方案"作为一个语言特性既不成熟也不必要，先交 2017 的 IDE 作业、先落地表示统一红线。

---

## 附录：特性评价

# 建议评价报告：proposal-annotated-types.md

## 评价对象

- 建议：proposal-annotated-types.md — 注释类型 / 类型变体（XML/JSON schema 注释、匿名委托风味、单位注释、轻量类型提供器 + "统一方案"宣言）
- 来源：Anthony 原文 18.20 "Annotated types/Type flavors"（`..\..\AnthonyDesign_wordpress.txt` L3090–3196）；语法形态与主线 2017-10-18 "Annotated types / XML and JSON" 讨论一致；底层动机（VS2008 schema IntelliSense 被砍）承自 Anthony 自述，与 `proposal-xml-schema-types.md` 同源；本建议为 inactive，xml-schema / json-literals / delegate-enhancements / any-pseudotype 四场会议已将其子方向移交/判 Table
- 配方目标：让 XML/JSON 等"类型变体"以注释形式标注在声明上供 IDE 使用，并寻求一个覆盖多场景的统一方案

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 命名了具体场景（XML/JSON IntelliSense），但①六个场景无一是端到端可演示的——示例只写声明（`order As <PurchaseOrder>`），改进（`order.<` 补全）是 IDE 行为，文档未演示；②类型提供器示例不可编译（`?` 即时窗口提示符 + 依赖五个未落地特性）；③无原型；④未决问题 6 个关键设计点 → 效果证据等级封顶 | 已检查（书面，无原型） | 核心语法（类型提供器）未定型；"统一方案"目标本身未定义验收标准；效果全部押在 IDE，而 IDE 章节缺失 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。五个强无关子场景捆绑（XML schema、JSON schema、匿名委托、单位、类型提供器），边界模糊；元组名 / `dynamic` 先例是 VB/C# 既有实践（真基因），但未被引用为设计依据；`<Function(...)>` 撞 XML、与 delegate-enhancements Table 决议冲突；`{"trade-message"}` 是 `{` 第六义 | 已检查 | 捆绑违反"职责单一"；尖括号形态未做 VB 化改造；未引用元组名/dynamic 先例作为基因辩护 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、Drawbacks/Alternatives 诚实、6 个未决问题具体（如实列出是加分）；但 Detailed design 是"场景目录"而非设计——无文法、无统一机制、无语义模型/绑定模型、无兼容性分析；示例大部分不可编译或不可演示；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；类型提供器语法明确"未落地"却仍列入设计 | 已检查 | 无 BNF、无 Compatibility 章节、无 IDE 章节；"统一方案"无机制；未引用 2017-10-18 主线同一方向的讨论 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。光/水受益（VBScript.NET 迁移、无类型数据原住民、与 C# 差异化）；风受损（六场景捆绑 = 演化一致性断裂、五种语法互相独立、与 `{`/`<` 文法主人冲突）；暗风险（注释经类型推断传播的隐蔽语义变化、类型提供器范围无边、`Suspect` 专利轶事被用作论据）且文档无对冲设计 | 已检查（预测待定） | 与主线"无编译器改动 + IDE 调查"倾向断裂；注释传播的破坏面未识别；类型提供器边界无边 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 18.20（已标注）；但①未标注与主线 2017-10-18 同一方向的直接关系（最大遗漏——同一语法形态、同一光谱、同一 IDE 倾向）；②"统一方案"借鉴 F# 类型提供器（明确拒绝）与 C# `dynamic`（未声明）；③Erik Meijer 轶事被用作论据但未标 Suspect；④`Any` 表示依赖的姊妹建议（any-pseudotype）未交叉引用；⑤元组名先例（C# `TupleElementNamesAttribute`）未点明 | 已检查 | 主线血缘未标；F#/C# 成分未展开；`Suspect` 论据未分级 |

## 设计原则对照

- **与 VB 基因：部分一致、部分偏离。** 一致：#5（`order As <PurchaseOrder>` 读起来像它描述的数据）、#1（注释不进绑定路径则纯加性零破坏）、元组名/dynamic 先例是 VB/C# 既有实践。偏离：#3（六个子场景捆绑、每条都是"第二种做事方式"——真实类型/属性/分析器之外的第四条路）、#8（`<` 撞 XML、`{` 撞六义）、#6（单位/类型提供器是边缘场景）、#4（C# 用 `DynamicAttribute` 表示注释而非语法；F# 类型提供器被明确拒绝）、#7（注释经类型推断传播 = 隐蔽语义变化风险）。
- **与主线关系：Anthony 独立延伸（更激进）**——主线 2017-10-18 讨论了同一方向（`As <contact>` / `As {"contact"}` 形态、补全目标、"类型提供器光谱另一端"、"IDE 调查"倾向），此后无在案后续（`Probably`：vblang proposals 目录无对应提案）；本建议把方向推成"统一语言机制"，与 `null` 字面量、空安全全家桶同构（主线保守、Anthony 激进）。但其中"表示统一"（元组名/dynamic 先例）与主线讨论一致，是唯一可先落地的主线共享面。
- **破坏性变更：现稿无（加性语法）；机制形态潜在有**——注释经类型推断传播若参与绑定/重载决议，会改变同一源码重编译后的绑定或警告集；需 `langversion` 门控。文档未做任何兼容性分析。

## 总评

- **达成程度：未达成（作为"统一方案"）/ 部分达成（作为场景目录）**——六个场景真实且互相独立，但"统一机制"既无语法、无文法、无传播规则、无 IDE 设计，也没有主线"IDE-first"作业的答案。伞没有伞骨。
- **LDM 三态建议：Table**——维持 inactive，附明确激活信号（IDE 调查证明必须语言语法 + 量化数据 + 表示统一红线落地 + 子场景拆分为单页 spec）。XML/JSON 注释方向为 **Consider**（作为表示统一红线下的用例，绑 IDE 调查）；委托风味、单位、类型提供器均 Table / 互锁。
- **主要问题**：① 六个强无关子场景捆绑，无统一机制且无统一语法（建议自身 Unresolved #1 即无答案）；② 轻量类型提供器语法未落地、示例不可编译、依赖五个其他特性、Erik Meijer 轶事未核实（Suspect）；③ 未引用 2017-10-18 主线讨论——同一方向、同一语法形态、同一"IDE 调查"倾向、同一"类型提供器光谱"；④ 无文法、无兼容性分析、无 IDE 章节；⑤ 注释经类型推断传播的破坏面未识别（原则 #7）；⑥ 与 delegate-enhancements（`<Function(...)>` Table）、units-of-measure（互锁）、any-pseudotype（`Object` + 注解）的交接未处理；⑦ 未决问题 6 个关键设计点 → 效果证据封顶。

## 返工建议

- **补充章节**：Detailed design 改按"表示统一红线 + 子场景用例"重写（统一表示 = 底层类型 + 元数据，选型 attribute vs 语义模型表，参考 `TupleElementNamesAttribute` / `DynamicAttribute`；附着点；传播规则）；文法小节（`<`/`{` 各自产生式与 tokenizer 判定流程）；IDE 章节（补全列表、InfoTip、schema 缺失降级）；Compatibility / breaking-change（传播规则、`langversion` 门控、警告策略）。
- **补充证据**：引用并回应 2017-10-18 主线讨论（同方向、IDE-first 倾向、类型提供器光谱）；完成"纯 IDE/分析器可否零编译器改动恢复 schema 补全"调查（2017 主线作业 + `IOperation` 性能 `_Test it_`）；最小原型（一条注释语法 → 语义模型注解表 → IDE 补全）；JSON/XML schema 开发占比数据；Erik Meijer 轶事与 VS2008 被砍事实的独立核查（Suspect 项降级为 Probably/已核验或删除）。
- **未决问题处理**：拆分为五份独立建议或收敛为表示统一红线（B）；`<Function(...)>` 委托风味单列为"`Func` 参数名注释"用例（不复活尖括号）；单位注释与 units-of-measure 互锁、泛型运算符先行；类型提供器单列并附可编译示例（删除 `?`；移除对未落地特性的满栈依赖）；`{"trade-message"}` 的 `{` 消歧与 json-literals 统一 shape-grammar 会议一次定。
- **设计探索**：与 any-pseudotype / 元组名统一"伪类型/注释类型"的擦除表示（`Object`/`ValueTuple`/`XElement` + 元数据）；注释经类型推断传播的边界实验（泛型、异步、`Func` 构造）；2017 主线 "nullable and tuple names are annotations applied to an underlying type" 的警告语义（注释冲突时报告转换警告）作为传播规则的副产品探索。

---

## 附录：C# 生态与互操作考量

> 本附录补充 meeting 正文未展开的 C#/CLR/.NET 生态视角。素材基于 `..\..\..\csharplang-index.md`（浓缩索引 T5/T6/T7/T8、M2/M4/M8），并对其中关键原文在 `..\..\..\csharplang`（dotnet/csharplang 官方仓库镜像）逐一 Grep 核实。本提案（注解类型 / 类型变体，类型 = Object + 注解）与 C# 用元数据属性表达「注解」的三条机制直接相关：`dynamic`（`DynamicAttribute`，C# 4）、可空引用类型（`NullableAttribute`，C# 8）、元组名（`TupleElementNamesAttribute`，C# 7）——这三者正是 RESOLUTION #2「表示统一红线」在 C#/CLR 侧的现实对应物。

### 一、相关 C# 现实方向

**1. C# 没有通用「任意类型注解」机制；它的注解先例都是语言强制的「良构 attribute」——这是本提案 B 红线的 C# 对应。**
- `dynamic`（C# 4）：`Language-Version-History.md` C# 4 条目逐字列出 "Dynamic binding"。运行时擦除为 `System.Object`，元数据以 `DynamicAttribute` 标记；csharplang 仓库无 csharp-4.0 提案存档，但 `proposals\csharp-11.0\generic-attributes.md` 把 `DynamicAttribute` 列为 well-known attribute（逐字）："there isn't a symbol to "attach" the `DynamicAttribute` or other well-known attribute to"——即 `dynamic`/`List<string?>`/`nint` 这类「依赖属性的类型」在 IL 里必须有符号可挂 attribute。
- 可空引用类型（C# 8）：元数据表示为属性，`proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation，逐字）："Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them."。属性名 `NullableAttribute` 可从仓库内 IL 示例核到（`proposals\csharp-14.0\extensions.md`：`.custom instance void NullableAttribute::.ctor(uint8) = (...)`）；其数量膨胀与压缩策略在 `meetings\2022\LDM-2022-01-24.md`（逐字）："there won't be nearly the proliferation of these attributes that `NullableAttribute` would have had as originally designed"。
- 元组名（C# 7）：`TupleElementNamesAttribute` 挂在 `ValueTuple<T...>` 位置。`proposals\csharp-7.0\tuples.md` 只是占位符（spec 已外迁 dotnet/csharpstandard），仓库内可核实的用法是 `meetings\2024\LDM-2024-10-14-Enumerable-extensions.cs`：`[TupleElementNames(new[] { "Key", "Value" })]` 直接标注在元组参数上。
- **共通特征**：三条先例都（a）语言强制「宿主类型 ↔ 注解」的对应（`dynamic` 只挂 `Object`、元组名只挂元组、NRT 只挂引用类型/类型参数）；（b）良构 attribute 只挂声明的既定目标（参数/局部/字段/返回/泛型实参），**从不挂表达式**。这与本会议第 11 节「表达式级元数据无 CLR 对应物」的 Suspect 一致。

**2. C# 的「注解经类型推断传播」唯一先例是 NRT 的 nullness 传播——它会改变警告，且必须 opt-in。**
- `proposals\csharp-8.0\nullable-reference-types.md`（Type inference，逐字）："In type inference, if a contributing type is a nullable reference type, the resulting type should be nullable. In other words, nullness is propagated."
- 这正面回应了本会议反复引用的 2017 主线子弹（"If the compiler understood annotations it could preserve them through type-inference at least"）：C# 把它做成了语言特性，但代价是**传播伴随可见信息改变**——NRT 的传播会给既有代码带来新警告（同文件 Breaking changes，逐字）："if type inference infers nullness from `null` expressions, then existing code will sometimes yield nullable rather than non-nullable types, which can lead to new warnings."，因此（同文件，逐字）："So nullable warnings also need to be optional"。**这证明「注释经类型推断传播」可实现，但不免费**：要么像 NRT 一样做成带 langversion/警告门控的正式特性，要么严格停在绑定器之外（本会议 v1 立场）。C# 没走「第三种路」。

**3. `dynamic` 与晚期绑定被 AOT/trimming 视为负担（索引 T7、M8）。**
- unsafe-evolution 把 `dynamic` 列入「是否应标 unsafe」的开放问题，`proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"，逐字）："`dynamic` (probably should match what BCL decides for reflection APIs)"。**未裁决。**
- 表达式树（解释/动态通道）与 C# 低层类型主线互斥：`proposals\csharp-14.0\first-class-span-types.md`（Expression trees，逐字）："Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine"。
- 方向本质：C# 靠类型系统与 source-gen 取代动态/反射职责（索引 T5/T6）——框定了本提案「类型提供器」示例里 `!` 链与动态风的 AOT 代价。

**4. unsafe-evolution 对 VB 的明确表态 + 新元数据识别（索引 T8、M8）。**
- `proposals\unsafe-evolution.md`（「VB」小节，逐字）："We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."
- C# 15 unions / closed hierarchies / `allows ref struct` 依赖 `CompilerFeatureRequired` 等特性标志；unsafe-evolution 引入 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`。C# 元数据里的**注解类 attribute**（`DynamicAttribute`/`NullableAttribute`/`TupleElementNamesAttribute`）与**安全/特性标志类 attribute** 是两族，VB/.vbx 都要认识（决策文件 M8 明示必须桥接）。

### 二、现实 vs 提案

| 维度 | C# 现实方向 | 与本提案关系 | 判定 |
|---|---|---|---|
| 表示统一红线（B：注释 = 底层类型 + 元数据，表示同族） | `dynamic`/NRT/元组名全部 = 底层类型 + well-known attribute；PEVerify 无碍、跨程序集可读 | RESOLUTION #2 的三条要求（运行时表示不变、不改变绑定、表示同族）**逐字对应** C# 三条先例的行为；C# 是活证据 | **兼容（同向）** |
| 伞型统一机制（A） | C# 无「任意类型注解」机制；三条先例全部语言强制「宿主 ↔ 注解」对应 | 本提案要注释任意 `XElement` 变量，宿主与注释间无编译器强制约束——C# 无先例，「更自由也更不可信」 | **脱节**（支持 Table 判定） |
| 表达式级元数据（类型提供器 `p!Price(As Decimal)`） | 良构 attribute 只挂声明目标、不挂表达式；CLR 无表达式级元数据 | 与第 11 节 Suspect 一致；C# 现实进一步确认这是硬约束 | **需桥接**（表达式级注解只能 IDE/语义模型侧，不能进元数据） |
| 注释经类型推断传播 | NRT 唯一先例：传播 nullness 且改变警告集；opt-in + warning 门控 | 本建议最危险的未决点；C# 证明可实现但「不免费」——传播必然伴随可见信息改变 | **兼容但要付 C# 的学费**（要么正式特性带门控，要么停在绑定器外） |
| `Any` 擦除（Object + 注解） | `dynamic` 元数据 = `DynamicAttribute` 挂 `Object`；边缘化 + AOT 质疑 | 跨语言互通要求 .vbx 发射/识别同一 `DynamicAttribute`；与 any-pseudotype 附录结论一致 | **兼容（机制）/ 需桥接（AOT 叙事）** |
| AOT / trimming | 类型系统承担更多职责、source-gen 替代反射；attribute 静态、AOT 安全 | 注解若停在「声明级 attribute + IDE/诊断消费」则 AOT 零成本；滑向动态/反射即撞墙 | **兼容**（只要注解不进运行时路径） |

**脱节提醒**：C# 的三条注解先例都是**编译器强制的良构 attribute**，本提案的「任意类型引用 + 任意元数据」在 C#/CLR 现实里没有对应物——这正是 2017 主线把作业留给 IDE 团队、而不是交给编译器的原因。表示统一（B）在 C# 现实里有硬先例；伞型（A）没有。

### 三、对 VBScript.NET 的适应建议

1. **表示统一红线直接对标 C# 的 well-known attribute 族。** PROPOSAL B 的「注释 = 底层类型 + 元数据」在选型（attribute vs 语义模型表）时，attribute 路线应优先参考并复用 `DynamicAttribute`/`NullableAttribute`/`TupleElementNamesAttribute` 的机制——构造参数编码、附着目标、压缩/嵌套策略（LDM-2022-01-24 记录了对 `NullableAttribute` 数量膨胀的担忧与压缩策略，见引用表）。语义模型表只应作为「不发射、只供 IDE」的补充：表达式级注解进不了 CLR 元数据，只能走语义模型/IDE 表。
2. **跨语言注解元数据互操作是硬前提。** .vbx 编译器必须**发射并识别**三类 well-known 注解 attribute，否则跨语言调用会读错 C# 签名：`DynamicAttribute`（`Any`/动态位置按 C# 规则发射，C# 才能把 .vbx 产的 `Object` 成员当 `dynamic` 消费，反之亦然）、`NullableAttribute`（NRT 注解不一致会制造假 null 警告或漏警告）、`TupleElementNamesAttribute`（`Func(Of Node, Node, Node)` 参数名注释若要跨程序集可读，走的正是这条路）。
3. **默认安全、按需动态；注解不进运行时路径。** 与决策文件 M2/M8 一致：`.vbx` 脚本层允许 `Any`/晚期绑定（迁移友好），编译产物走类型化；XML/JSON schema 注解停在「声明级 attribute + IDE/诊断消费」，不参与绑定、不产生运行时行为——这样注解在 NativeAOT/trimming 下零成本。类型提供器场景的「表达式级注解」明确为 IDE-only，不是 CLR 元数据。
4. **识别安全/特性标志新元数据。** `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`（unsafe-evolution）、`CompilerFeatureRequired`（unions / `allows ref struct`）——VB 编译器必须认识，才能正确校验「调用 C# requires-unsafe 成员 / 消费 C# 15 新类型」。注解类型本身不受影响，但跨语言边界的新元数据识别是互操作前提（决策文件 M8）。
5. **差异化论证对着 C# 讲。** C# 现实表明：「注解的任意通用化」不在 C# 路线图（C# 只做语言强制的良构 attribute），但「工具信息进元数据、被 IDE/分析器跨程序集读取」正是 C# 承认的方向（NRT 的 attribute 路线）。VBScript.NET 的 XML/JSON schema 注解若定位为「VB 版良构 attribute + IDE 补全」，在生态里是**增量而非异类**；若定位为「统一任意注解机制」，则在 C#/CLR 现实里没有对话对象。

### 四、对既有 RESOLUTION / 三态判定的影响

- **无推翻，有强化。** RESOLUTION #2（表示统一红线）获得 C# 生态直接背书：`dynamic`/NRT/元组名三条先例证明「底层类型 + 元数据 attribute」就是 CLR 表达注解的既定方式，红线的三条要求与 C# well-known attribute 的行为完全一致。C# 现实还顺带支持 RESOLUTION #3（xml-schema 移交）——注解停在声明级 attribute 与 C# 先例同构。
- **三态判定（Table）不变，多一条 C# 侧佐证。** C# 没有「任意类型注解」机制 = 伞型统一方案（A）在生态里没有对应方向，呼应「伞不值得撑」；而「注释经类型推断传播」的 C# 先例（NRT）证明传播必然改变警告集——支持会议「v1 注释不进绑定路径」的立场（否则就得像 NRT 一样背起 langversion/警告门控的正式特性成本）。
- **OPEN QUESTIONS 增补 / 对表**：① 「注释经类型推断传播」的规则应以 C# NRT 的传播语义（"nullness is propagated"）为参考基准，明确「传播到哪一层、何时截断、是否触发警告」——C# 的答案是「传播 + 警告 + opt-in 门控」，可作互操作口径；② 「注释的发射形态」应以 C# well-known attribute 的「构造参数 + 附着目标」清单为互操作基准，并确认 CLR 目标覆盖不了表达式（类型提供器只能 IDE 侧）；③ 与 any-pseudotype 会议的 C# 附录对表：`Any` 擦除（`DynamicAttribute`）、AOT 双模路线、新元数据识别三处结论一致，本附录的 well-known attribute 互操作清单是它的补充。

### 五、引用纪律

本附录引用的 C# 原文均经 `..\..\..\csharplang` Grep 逐字核实：

| 原文（逐字） | 来源 |
|---|---|
| "In type inference, if a contributing type is a nullable reference type, the resulting type should be nullable. In other words, nullness is propagated." | `proposals\csharp-8.0\nullable-reference-types.md`（Type inference） |
| "Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them." | `proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation） |
| "if type inference infers nullness from `null` expressions, then existing code will sometimes yield nullable rather than non-nullable types, which can lead to new warnings." | `proposals\csharp-8.0\nullable-reference-types.md`（Breaking changes） |
| "So nullable warnings also need to be optional" | `proposals\csharp-8.0\nullable-reference-types.md`（Breaking changes） |
| "there isn't a symbol to "attach" the `DynamicAttribute` or other well-known attribute to" | `proposals\csharp-11.0\generic-attributes.md` |
| "there won't be nearly the proliferation of these attributes that `NullableAttribute` would have had as originally designed" | `meetings\2022\LDM-2022-01-24.md` |
| `.custom instance void NullableAttribute::.ctor(uint8) = (...)` | `proposals\csharp-14.0\extensions.md`（IL 示例） |
| `[TupleElementNames(new[] { "Key", "Value" })]` | `meetings\2024\LDM-2024-10-14-Enumerable-extensions.cs` |
| "`dynamic` (probably should match what BCL decides for reflection APIs)" | `proposals\unsafe-evolution.md`（"Should more constructs be `unsafe`?"） |
| "Overloads taking spans like `MemoryExtensions.Contains` are preferred over classic overloads like `Enumerable.Contains`, even inside expression trees - but ref structs are not supported by the interpreter engine" | `proposals\csharp-14.0\first-class-span-types.md`（Expression trees） |
| "We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either." | `proposals\unsafe-evolution.md`（「VB」小节） |
| "Dynamic binding" / "Embedded interop types ("NoPIA")" | `Language-Version-History.md`（C# 4 条目） |

### OPEN QUESTIONS（本附录未核实 / 需外部确认）

- **`DynamicAttribute` 的规范正文**：C# 4 时代无 proposal 存档；csharplang 仓库内仅 `generic-attributes.md` 把它列为 well-known attribute，其余（构造参数、附着目标清单）在 dotnet/roslyn 与 dotnet/runtime，本库无正文。
- **`TupleElementNamesAttribute` 的规范正文**：`proposals\csharp-7.0\tuples.md` 是占位符；仓库内可核实仅 `LDM-2024-10-14-Enumerable-extensions.cs` 的用法示例，完整规范已外迁 dotnet/csharpstandard。
- **NRT 传播的完整规则**：本库正文只给方向（传播 nullness、警告 opt-in），警告抑制/流分析的精确机制在 roslyn 与 csharpstandard，未在 csharplang 正文核实。
- **`dynamic` 是否标 unsafe：unsafe-evolution 开放问题未裁决**（"probably should match what BCL decides"），其走向直接影响 `Any` 的 AOT 叙事——`Suspect` 任何把「C# 已判 dynamic 死刑」当既定事实的说法。
- **`NullableContextAttribute`（模块/类型级压缩属性）的名称与规则**：csharplang 正文未出现该名称，属 roslyn/runtime 实现细节，本附录只引用了 `NullableAttribute`（`extensions.md` IL 示例可核）。
