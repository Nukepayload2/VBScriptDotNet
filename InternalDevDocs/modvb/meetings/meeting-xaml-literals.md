# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周的议题横跨"字面量"与"声明式 UI"两片旧地：建议把整段 XAML 变成 VB 的一等表达式。讨论进行到一半时我们意识到，与其说这是一份语言设计建议，不如说它是一份把"XAML 编译器"塞进 Roslyn 的愿望清单——语言会议很少同时面对这么大的实现面与这么少的规范文本。我们把能定的小范围定下来，其余如实标记。

## Agenda

* [Proposal - XAML Literals / XAML 字面量](#proposal---xaml-literals)

## Proposal - XAML Literals
_Related: [vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)（2017-10-18 讨论，"字面量/构造模式"判据）；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；[vblang #184 – Tagged String literals（annotated types，含 XML/JSON IntelliSense）](https://github.com/dotnet/vblang/issues/184)；[vblang #303 – Null conditional operators for add/removeHandler](https://github.com/dotnet/vblang/issues/303)（2018-05-30，WPF 场景）；[vblang #211](https://github.com/dotnet/vblang/issues/211)（2018-02-07，"Fantastic idea, and too hard to do"）；Anthony D. Green 原文第 4 章 "UI, XML, XAML"；交互建议：[embedded-vb-mode](../proposals/proposal-embedded-vb-mode.md)、[xml-schema-types](../proposals/proposal-xml-schema-types.md)、[local-declarations（`Let`）](../proposals/proposal-local-declarations.md)_

### 场景与缺口

We started from the scenario the proposal names: today, creating a UI object in VB means either shipping a `.xaml` file and loading it through `InitializeComponent`, or building the object graph imperatively. Both cut the connection between the declarative description of the UI and the code that hosts it. The proposal wants XAML to be a first-class expression:

```vb
' XAML Literals.
Let button = <?xaml?>
             <Button Content="{Binding Command.Name}"
                     Clicked="OnButtonClicked"/>
```

We have real sympathy for the impulse. It is the same impulse that produced JSON literals, and the main line gave us a usable criterion there: *"the principle for making a new 'literal' or construction pattern is when the ceremony of creating an object(-graph) obscures the structure of the data itself"* (2017-10-18). An imperative `Button` tree with nested content, style setters, and event wiring absolutely does obscure its own structure; a XAML-shaped description does not. On that test alone, the scenario clears.

But the meeting took a hard left turn once we asked what "compiler translates this XAML into a target framework's construction calls" actually means. The phrase in the proposal's Detailed design is one line; the reality is a compiler for each target framework. And we found the proposal's own example cannot be compiled against any real framework, which forced us to slow down and enumerate rather than enthuse.

A second, quieter thread: the main line has already visited the WPF corner. In 2018-05-30, reviewing #303 (null-conditional `AddHandler`), we wrote that *"generally humans don't do the AddHandlers thing and generally all controls are instantiated"* — the observation being that in modern UI stacks the declarative side is already handled by XAML tooling, and hand-wiring is rare. That note went to `No Plans` as *"really seems like a side case"*. The gap Anthony points at is real but narrower than it looks, because the toolchain already occupies most of the ground this feature would take.

### 候选方案

We enumerated the ways the feature could be realized.

**PROPOSAL A — 完整编译器内建 XAML 字面量（as proposed）。** New `<?xaml?>` parsing mode; the compiler lexes the region as XML/XAML, resolves element names against target-framework metadata, and lowers the whole thing to object-construction, property-set, binding-registration, and event-wiring calls. The full language feature.

**PROPOSAL B — XML 字面量特化（specialization）。** The proposal's own Alternatives suggestion: treat XAML as a specialization of VB's existing XML literals, sharing the same parser foundation (the mirror image of `<?vb?>` in `embedded-vb-mode`). Lowering produces framework objects instead of `System.Xml.Linq` values.

**PROPOSAL C — 源代码生成器（source generator）。** No language change at all. A generator consumes `.xaml` files (and, if desired, XAML hosted in an attribute or doc-comment payload) and emits typed VB partial classes with `InitializeComponent`, typed `x:Name` fields, compiled bindings, and event wiring. "XAML near code" becomes a file-organization and tooling story.

**PROPOSAL D — 维持现状（status quo）。** Keep `.xaml` + `InitializeComponent`, or imperative construction. The proposal's own Alternatives argues this leaves the co-location gap in place; we agree, but the defense below deserves a hearing because modern toolchains (XamlC, WinUI, MAUI source generators) have already closed much of it since this idea was first shaped.

### 权衡：Q&A

We walked the design through the LDM questioning list. What follows is the discussion that actually bit; not all of it is in priority order.

#### 1. 语法 / 文法歧义

`<?xaml?>` is not an empty slate — it is the XML *processing instruction* syntax. VB's spec defines processing instructions today (they produce `System.Xml.Linq.XProcessingInstruction`), and states that *"XML processing instructions cannot contain embedded expressions, as they are valid syntax within the processing instruction."* So there are two incompatible readings of the same tokens:

- If `<?xaml?>` is a genuine processing instruction, then in an XML-literal context `<?xaml?>` followed by `<Button .../>` is *two* XML entities: a PI and a sibling element. The XAML element would need to be extracted out of the literal — nothing in the proposal describes that.
- If `<?xaml?>` is a *mode marker* (like `<?vb?>` in `embedded-vb-mode`), then it is not part of the XML at all; it is a lexical directive that says "parse the following region as XAML". But then it is new top-level syntax inside an *expression* position (`Let button = <?xaml?>...`), and the parser must switch lexical modes mid-expression and switch back when the region ends. The proposal gives no terminator — the example just ends at end-of-line. `Probably` the intended reading is self-delimiting via the matching XAML end tag, but nothing states it, and a `<Button .../>` inside an expression is *already* a valid VB XML literal. The marker is the only thing telling the two apart; the grammar of the marker, its termination, and its relationship to the XML-literal grammar are all undefined.

Also note the region flips the lexical rules: XML is case-sensitive, so markup keywords must match casing exactly (already true for XML literals), while any embedded VB expression inside the XAML would need to flip back to VB's case-insensitive lexing. The proposal shows no embedded expression, but real XAML needs them (converter parameters, `{x:Bind ...}` data, computed values). The round-trip lexing is exactly the machinery `embedded-vb-mode` is trying to build — in the opposite direction. We treat the two proposals as two faces of one parser-mode problem; see Implication.

#### 2. 角案例与边界语义

The example is a self-closing single element. XAML's real surface is far larger, and every one of these features would have to be either supported or rejected at the edge:

- **多根与文本内容。** A XAML literal producing more than one top-level object would have no single type; the proposal is silent on whether `<Grid><Button/></Grid>` (one root) is the only supported shape.
- **附加属性。** `Grid.Row="2"` is a *dotted attribute name* — legal XML, but only meaningful if the compiler knows `Grid.Row` is an attached property and which `Grid` type owns it. That is framework metadata, not language syntax.
- **标记扩展。** `{Binding Command.Name}` is one thing; real XAML has nested markup extensions (`{Binding Text, Source={StaticResource key}}`), escape sequences (`{}` for a literal brace), `x:Static`, `x:Type`, and converters. The compiler cannot lower `{DynamicResource ...}` — resource lookup is a runtime framework behavior. So *"把 XAML 翻译为目标框架的构造调用"* is only sound for the static subset; the dynamic subset cannot be lowered by a compiler at all.
- **资源字典 / 样式 / 数据模板。** `Style`, `DataTemplate`, and resource dictionaries contain nested XAML element trees that are often *strings at runtime* (a `DataTemplate` is an object that lazily instantiates). A literal form drags all of XAML's own edge cases into the language's edge cases.

#### 3. 作用域与绑定

The proposal's strongest claim is "编译期校验的潜力" — but let's separate it in half, because only half survives.

- **属性名（`Content=`）可以编译期校验。** If the root element resolves to a CLR type, the property names on it are checkable. This is exactly what compiled bindings (XamlC) already do.
- **事件处理器名（`Clicked="OnButtonClicked"`）可以编译期校验，但作用域未定义。** In a `.xaml` file, the handler is a member of the code-behind class. In a *literal*, what is the enclosing scope? The current type's members? A local? The example sits at the top level of an immersive file (`Let button = ...`), where there is no enclosing class at all — so `OnButtonClicked` has nothing to bind to unless the host story (see `top-level-code`) is settled first. And there is a name-collision question: two methods named `OnButtonClicked`, one on the type and one as an enclosing local — which wins?
- **绑定路径（`{Binding Command.Name}`）不能编译期校验。** Without a pinned data-context type (XAML's `x:DataType`), `{Binding ...}` is resolved at runtime by the binding engine against whatever the `DataContext` happens to be. The proposal's example has no `x:DataType`, so the "编译期校验" for bindings does not materialize in the very example that advertises it. `Probably` the honest claim is: *event and property names* are compile-time; *binding paths* are runtime unless the data type is pinned.
- **根元素类型解析。** The example's `<Button>` is unprefixed, with no `xmlns`. There is no default namespace to resolve it against. WPF maps the default `xmlns` to `System.Windows.Controls`; Xamarin.Forms/MAUI map a different default to `Xamarin.Forms`/`Microsoft.Maui.Controls`. The compiler cannot pick a `Button` — and this is the single most disqualifying detail in the document, see item 5.

#### 4. 与既有特性的交互

**与 XML 字面量。** PROPOSAL B (specialization) looks like it reuses machinery, but the two features are different in kind. The spec is explicit: *"The result of an XML literal expression is a value typed as one of the types from the `System.Xml.Linq` namespace."* XML literals are a *data* construct; a XAML literal must produce *CLR objects* (`Button`, `Grid`, ...) with a completely different type mapping and a completely different lowering. They share only the XML lexer. Worse, the specialization would inherit XML literals' quirks — notably *"XML namespaces declared in an element do not apply to XML literals inside embedded expressions,"* which would make nested XAML/expression interplay even more confusing than it already is. We do not think B is a specialization; it is a new feature wearing XML literal's clothes.

**与 `With` / `AddHandler`。** VB already has an idiomatic, compiled, VB-flavored way to build an object and wire events in one breath:

```vb
Dim button As New Button With {
    .Command = viewModel.Command,
    .CommandParameter = viewModel,
    .Content = "Submit"
}
AddHandler button.Clicked, AddressOf OnButtonClicked
```

The LDM has been here before: in 2014-02-17, discussing delegates in object initializers, we noted that `=` *"is the XAML syntax, and nice and short ... It's also nice not to add new syntax."* VB chose not to add the syntax. The gap the XAML literal targets is partially covered by machinery that already exists, and that machinery is more VB than a wholesale foreign DSL. `Probably` the co-location benefit of a XAML literal is real, but it is a benefit of *placement*, and placement is a tooling question (item 9).

**与 `Let` / 沉浸式文件。** The example uses Anthony's `Let` declaration at what is, in the current grammar, an expression position. `Let` is a separate proposal (`local-declarations`) and is itself unresolved; a feature that depends on it cannot be evaluated independently.

**与 `Option Strict Off` 的晚期绑定。** If the root element resolves to `Object` (the "can't resolve `Button`" fallback), then property assignments become late-bound under `Option Strict Off`. That is a real semantic fork: the same markup means a compile-time-checked object graph under `Strict On` and a reflection-based call under `Strict Off`. We regard that as unacceptable unless explicitly designed.

#### 5. Breaking change 与兼容性

For *existing code* there is no break — `<?xaml?>` is not legal VB today, so this feature only turns errors into programs. But there are two compat-adjacent surfaces we do not like:

- **语法空间冲突。** The `<?...?>` form is reserved-by-meaning for XML processing instructions. Carving `<?xaml?>` out of it is fine for today, but it forecloses the XML-literal space from growing in that direction, and it creates a parsing context where `<?xaml?>` inside an XML literal means something different from `<?xaml?>` at expression position. We are uncomfortable with a marker whose meaning depends on lexical mode.
- **工具链冲突（不是旧代码，是生态）。** The proposal's own Drawbacks names it: the feature overlaps the XAML toolchain (visual designer, hot reload, the XAML/BAML compiler). Those are not languages to be matched — they are products the compiler team does not own. A language feature that requires the compiler to reimplement the WPF/MAUI XAML compiler inevitably drifts out of sync with the actual XAML compiler, and then the "literal" and the `.xaml` file disagree on what a given element means. That is a maintenance contract no language feature should sign.

And the disqualifying detail: **the example does not compile against any real framework.** `Content` is the WPF `Button`'s content property (it is a `ContentControl`), while `Clicked` is the Xamarin.Forms/MAUI `Button` event (whose content property is `Text`). No mainstream `Button` type has both `Content` and `Clicked` — and without an `xmlns` the compiler cannot even choose a framework to fail against. This is the same class of error we flagged in the top-level-code meeting (`Starfield.vb` calling methods on an undeclared `buffer`): a motivating example that cannot compile under `Option Strict On`. We will not let a demonstration carry the weight of a design decision when it does not compile.

#### 6. Option Strict / 编译选项分叉

Covered in item 4 under late binding, but worth stating as its own rule: whatever the feature does, both compiler paths must lower the same markup to the same *semantics*. If the root type resolves, `Strict On` and `Strict Off` must both produce a statically-typed object graph; if it does not resolve, both must fail identically. A fallback-to-`Object` path for `Strict Off` would create two languages. Since the proposal gives no resolution rules, this is an OPEN QUESTION, not a designed fork.

#### 7. IDE / IntelliSense 影响

A XAML literal inside VB source means the VB editor must render a mini-XAML editing surface: completion for element names, properties, attached properties, markup extensions, and resource keys, plus error squiggles that map back into the embedded region. This is the "XML/JSON IntelliSense" problem the main line already visited — in 2017-10-18, on annotated types, we wrote that *"We might be able to do a lot with no compiler changes and should investigate with IDE team."* A full XAML editing experience is not an LDM decision; it is a product (there is literally a XAML designer product). Anthony's own chapter notes the historical precedent (XML schema "types" to re-enable XML IntelliSense, and the Erik Meijer anecdote that the 2008 implementation was "essentially a 'type provider'"). The IDE cost alone is the size of a feature release. We should not pretend otherwise.

#### 8. 数据 / 普遍性

We have no user-request data, and the main line never proposed XAML literals — there is no issue to cite, no prior discussion. What we do have:

- The literal-construction principle (2017-10-18) supports the *category*; it was applied to JSON, which is framework-neutral. XAML literals are the framework-bound end of the same spectrum, and the framework-bound end is where the toolchain already lives.
- The main line's WPF observation (2018-05-30, #303) cuts against pervasiveness: in modern UI stacks, *"generally all controls are instantiated"* — declaratively, in `.xaml` files. The co-location gap is real but is a *developer-experience* gap, not a capability gap, and it is most acute for the smallest programs.
- For VBScript.NET specifically, the scripting identity (top-level code, embedded pages) does not lean on Windows-desktop XAML. The one artifact of evidence Anthony provides is a video demo ("XAML/Xamarin.Forms"); as with the top-level-code Past Demos, a video is not inspectable evidence. It stops at "已提供/已检查" on our ladder.

`Suspect`: the co-location demand, if it exists, will show up as requests for better `.xaml`↔code-behind navigation and tooling, not as demands for new syntax. We have not seen data to the contrary.

#### 9. 更简替代

**PROPOSAL C is the honest winner of this meeting.** A source generator can deliver nearly every concrete benefit the proposal claims — compiled property checks, event wiring, typed `x:Name` fields, and (with an `x:DataType` contract) compiled bindings — with zero new syntax, zero new parsing modes, zero collision with the XML-literal grammar, and full reuse of the existing XAML toolchain. Modern .NET UI stacks already do this (XamlC, WinUI, MAUI generators). The "XAML near code" placement problem is solved by file organization and IDE affordances, not by the compiler. Against PROPOSAL C, the only things A adds are: (a) the literal is in the same file, and (b) the compiler sees it directly. We do not think (a) is worth (b)'s cost.

The status-quo defense ("`InitializeComponent` already works") is real but narrower than it looks for the one-file/small-program scenario, which is exactly where C — not A — is the right next step.

#### 10. 复杂度 / 成本 / 优先级

We tried to scope what A actually requires, per target framework: a mapping from XAML namespaces to CLR namespaces, element-to-type resolution, property and attached-property resolution, markup-extension registry and `ProvideValue` semantics, resource lookup, and event wiring — plus the IDE surface of item 7, plus the round-trip lexer of item 1. That is a XAML compiler. The proposal's Drawbacks says the same thing in one line ("编译器的复杂度与维护成本显著上升") and we agree, but the agreement is not a cost estimate. As a language feature it would be one of the largest surfaces VB has ever shipped, for a scenario the toolchain already serves. Cost/value does not balance for A. It does balance for C, which is the point.

#### 11. 运行时 / CLR 硬约束

No CLR-level constraint: a compiler-emitted object graph is ordinary IL. The hard constraint is *semantic*, not metaprogrammatic — the dynamic subset of XAML (resource lookup, `DynamicResource`, dependency-property coercion, dispatcher/timing behavior) is runtime framework behavior that a compiler cannot reproduce by construction. So a literal would silently split XAML into a static subset it can lower and a dynamic subset it must punt to `XamlReader` or runtime services — meaning *"字面量"* would not actually be the whole of XAML. That is a fidelity promise the proposal does not make explicit.

#### 12. 值不值得做

Value: real but narrow (co-location for small programs; declarative UI is already served elsewhere). Cost: very high for A (a XAML compiler per framework), low for C. Risk: framework-coupling, a third way to do UI, PI-syntax collision, and a maintenance contract with the XAML toolchain. The phrase that fits best is one we used for #211 in 2018: *"Fantastic idea, and too hard to do."* We are not rejecting the scenario; we are rejecting the vehicle. The scenario's value should be harvested through PROPOSAL C, and A should wait for signals it has not received.

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）** — 通过。`<?xaml?>` 今天非法，只把错误变成程序；但生态冲突（XAML 工具链）是新的破坏面。
- **原则 2（保持 VB-like）** — 半通过。`<?xaml?>` 与 XML 字面量同源，是 VB 自己的血脉；但整段 XAML 是一个外来 DSL 的批发进口——元素、属性、标记扩展都是另一门语言的语法。We don't think wholesale DSL import reads as "VB-like".
- **原则 3（不引入第二种做事方式）** — 这是最锋利的一条。2018-06-13 立下标准：*"our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"*。今天声明式 UI 已有两种正规途径（`.xaml` 文件、命令式代码），XAML 字面量是第三种。The counter that worked for top-level code (the main line never provided *any* way for that scenario) does not work here: the main line, and the ecosystem, already provide the declarative way.
- **原则 4（默认跟随 C#）** — 不适用：C# 没有 XAML 字面量，C# 社区用 `.xaml` + code-behind + source generators。这不是一个 VB 落后于 C# 的 parity gap；不存在"跟随 C#"的问题，也就没有"充分理由偏离"的入口。This is the rare feature where the C# default and the VB instinct point the same way — to the toolchain.
- **原则 5（读起来像英语、对新手友好）** — XAML 片段本身可读，但它要求新手同时学 VB 与 XAML 两种语言；对首次开发者，负担不是零。
- **原则 6（不为边缘场景加特性）** — WPF 桌面 UI 在脚本产品里是边缘；若 VBScript.NET 明确绑定某个 UI 框架，则需重议。
- **原则 7（避免隐蔽的控制流/语义变化）** — `{Binding Command.Name}` 是字符串，运行时才解析；字面量对编译器半透明。这与我们拒绝 `Return?` 的理由同源（2018-05-30：*"Control flow would be altered by a very subtle character"*）——这里是一个看似静态的标记下藏着运行时语义。
- **原则 8（不与既有语法冲突）** — 失败。`<?xaml?>` 与 XML 处理指令语法同形，语义两读。
- **原则 9（消除常见样板）** — 通过，但被移除的样板大部分已被 `.xaml` + `InitializeComponent` 移除。
- **原则 10（冗长只在有用时是美德）** — XAML 的冗长是它自己的；本特性还要求每处字面量携带框架上下文（`xmlns`），而示例恰恰省略了这个无法省略的仪式。

对照主线表（2.3）：主线无 XAML 字面量行——这是 **Anthony 独立延伸**。与其方向最近的主线工作是 JSON literals（#101，2017-10-18，Table 待更多反馈）、XML patterns（#139）与 annotated types（#184，XML/JSON IntelliSense）；它们在"数据即语言"的谱系上与 Anthony 一致，但 XAML 字面量走到谱系的框架耦合端，与主线"保守、跟随工具链"的立场分道。与 2018-05-30 #303 的 WPF 观察精神一致：现代 UI 栈的声明式一侧已被工具链覆盖。

### RESOLUTION:

**RESOLUTION:** We split the proposal into the scenario and the vehicle, and rule on each separately.

1. **场景成立，车不对。** 声明式 UI 与承载代码的共置是真实的 DX 缺口，符合 2017-10-18 的"构造仪式遮蔽结构"判据；但建议的载体——编译器内建 XAML 字面量（PROPOSAL A）——是"把 XAML 编译器塞进 Roslyn"，成本、框架耦合、工具链冲突与语法冲突四重账都对不上。
2. **PROPOSAL A — `Table`，非 Reject。** 我们不为否定这个想法，而是为它立复活信号，缺一不可：
   - 一份**框架无关的 XAML→CLR 类型解析契约**（或明确承诺唯一目标框架并提供完整 `xmlns` 映射）；
   - 一个**可运行原型**证明 lowering 与 IDE 体验成立；
   - **共置需求的证据**（用户请求、代码库统计），而非视频 demo。
3. **PROPOSAL B（XML 字面量特化）— 否决作为载体。** XML 字面量定型于 `System.Xml.Linq`，XAML 需要 CLR 对象实例化与框架类型解析；二者只共享 XML 词法。且 `<?xaml?>` 标记与既有处理指令语义冲突，会继承 XML 字面量"命名空间不流入嵌入表达式"的怪癖。`<?xaml?>` 的 PI 形制若保留，只能作为词法模式标记，且必须与 `embedded-vb-mode` 的 `<?vb?>` 共享同一套模式切换机制。
4. **PROPOSAL C（源代码生成器）— `Consider`，本场真正的产出。** 它交付编译期属性校验、事件接线、类型化 `x:Name` 与（在 `x:DataType` 契约下）编译期绑定，零语言面、零解析模式、零文法冲突，完全复用现有 XAML 工具链。建议为生成器契约写一份 speclet，并探索"XAML 近代码"的文件组织/IDE 支持。
5. **示例不可编译，必须修正或撤回。** `<Button Content="..." Clicked="..."/>` 的 `Content`（WPF `Button`/`ContentControl`）与 `Clicked`（Xamarin.Forms/MAUI `Button` 事件，内容属性是 `Text`）不属于任何主流框架的同一个 `Button`，且无 `xmlns` 无法解析根类型。所有示例须在显式框架命名空间、`Option Strict On` 下编译通过。
6. **编译期校验的承诺必须两分。** 属性名/事件名可编译期校验（作用域需定义）；绑定路径（`{Binding ...}`）运行时解析，除非钉住 `x:DataType`。

**Implication:**

- 与 `embedded-vb-mode` 是同一枚硬币：`<?vb?>`（XAML/HTML 内嵌 VB）与 `<?xaml?>`（VB 内嵌 XAML）是相反的嵌入方向，必须共享模式切换词法；两份建议应交叉引用，并明确定义每个标记的终止与嵌套规则。
- 与 `local-declarations`（`Let`）的依赖必须显式：示例依赖 `Let` 与顶层声明，而二者均未定型；复活时需在该两项落定之后重评。
- 会议记录链接回提案页；提案状态保持 `Proposed`，Unresolved questions 按本纪要重写。
- TODO：核实 `Content`/`Clicked` 示例对主流框架 API 的对应关系（我们的结论是"无对应"，须写进提案 Drawbacks）；探索 PROPOSAL C 的 speclet。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`<?xaml?>` 的精确文法——模式标记 vs 处理指令；终止方式（自定界 by 结束标签 vs 换行）；与 XML 字面量文法在表达式位置的区分。
- `OPEN QUESTIONS`：根元素类型推断规则；无默认 `xmlns` 时的报错策略；`Option Strict On/Off` 双路径下是否都禁止回退到 `Object`（我们倾向都禁止）。
- `OPEN QUESTIONS`：事件名 `Clicked="OnButtonClicked"` 的绑定作用域（当前类型成员？局部？沉浸式文件中无宿主类型时怎么办）。
- `TODO`：把"示例不可编译"验证为正式兼容性小节（列出 `Content`/`Clicked` 与各框架 `Button` 属性的对照）。
- `TODO`：为 PROPOSAL C（source generator 契约）起草 speclet，含 `x:DataType` 编译期绑定与类型化 `x:Name`。
- `Follow-up`：与 `embedded-vb-mode` 统一模式切换机制后的行为差异表（`<?vb?>` 与 `<?xaml?>` 的嵌套与互斥规则）。

### 状态

- **LDM 状态：Table（PROPOSAL A，附复活信号）**；PROPOSAL B 按"否决的备选方案"留档；PROPOSAL C 为 `Consider`。
- **三态判定：Table** — 场景真实但载体不可行；价值应经 source generator 收割；A 的复活取决于框架解析契约、可运行原型与需求证据三者齐备。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-xaml-literals.md` — XAML 字面量（`<?xaml?>` 起始标记的内联 XAML 表达式）。
- 来源：Anthony 原文第 4 章 **"UI, XML, XAML"**（`..\AnthonyDesign_wordpress.txt` L1120–1186），附视频 demo（"XAML/Xamarin.Forms"）；该章同时包含 `embedded-vb-mode`、XML Schema 类型、隐式 `Return`/`Yield` 三个未切分的能力。
- 配方目标（Motivation 摘要）：把声明式 UI 描述与承载代码放在同一处，`{Binding ...}` 与事件处理器直接引用 VB 成员，获得编译期校验潜力。

### 五维评分表

| 维度 | 得分 | 评价（对照行为锚点） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | 目标改进（共置、免 `InitializeComponent` 样板）说得清，但唯一示例不可编译（`Content`+`Clicked` 混合、无 `xmlns`），"编译期校验潜力"言过其实（`{Binding}` 运行时解析，示例无 `x:DataType`）；无原型，视频止于"已提供" → "改进不可衡量，示例不能演示改进" | 已检查 | 主效果仅书面；关键子效果（绑定校验、事件接线作用域）未显现；核心语法（终止、框架映射）未定型 = 效果未显现 |
| 特性 | **2/5** | `<?xaml?>` 与 XML 字面量同源（VB 血脉）但整段 XAML 是外来 DSL 批发进口，未做 VB 化改造；且捆绑了绑定解析、事件接线、框架翻译多个强能力 → "外来特性直接照搬未 VB 化；或多个强无关能力捆绑" | 已检查 | 借鉴 VB9 XML literals 与 XAML 框架语义未定性；与 `Let`、`embedded-vb-mode` 的依赖未切分 |
| 品质 | **2/5** | 六章节模板齐全、Drawbacks/Alternatives/Unresolved 诚实具体（4 项未决如实列出）；但无文法（`<?xaml?>` 终止/模式未定义）、无兼容性小节、无 Option Strict 分叉，示例与"可演示"的承诺冲突 → "自相矛盾；示例与正文冲突"（介于 2 与 3，取 2） | 已检查 | 红旗：状态栏占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；单一示例无法编译；"框架翻译"仅一句带过 |
| 属性 | **2/5** | 对桌面 UI 场景（水=差异化，与 C# 无此特性）单向受益；但雷=编译器迭代被"实现 XAML 编译器"拖慢，暗=XAML 工具链冲突（设计器/热重载/BAML）、PI 语法冲突、"第三种做事方式"——Drawbacks 列出冲突却无应对 → "某关键维度受损且无应对" | 已检查（预测性结论，标"待定"） | 工具链冲突未对冲；framework-coupling 使特性只对引用特定框架的项目有意义；风=与"数据即语言"方向一致但走到框架耦合端 |
| 炼金成分 | **3/5** | 材料出处（Anthony 第 4 章）标注准确，但第 4 章实际是"UI, XML, XAML"全家桶（XAML + embedded-vb + XML Schema + 隐式 Return/Yield），本建议只取一片未交代；借鉴 VB9 XML literals 未点名；最大成分——XAML 框架语义（外部 DSL、其运行时绑定引擎）——未定性 → "部分来源未标注；标注与影响有偏差" | 已检查 | 未声明 XML literals 血缘；未声明对 XamlC/MAUI source generator 的既有方案负向依赖；无杂质但来源含混 |

### 设计原则对照

- **与 VB 基因：部分偏离。** 原则 9（消样板）有共鸣；原则 2（VB-like）半通过（XML 血脉 vs 外来 DSL）；原则 3（第二种做事方式）——最锋利——第三种方式，2018-06-13 高门槛不通过；原则 7（隐蔽语义变化）——`{Binding}` 运行时解析，半透明；原则 8（不与既有语法冲突）——`<?xaml?>` 与处理指令语义两读，失败。
- **与主线关系：Anthony 独立延伸。** 主线无 XAML 字面量建议；最近亲属为 JSON literals（#101，2017-10-18，Table 待反馈）、XML patterns（#139）、annotated types（#184）。共享"数据即语言"方向但走到框架耦合端；与 2018-05-30 #303 的 WPF 观察（"generally all controls are instantiated"）精神相反——主线认为工具链已覆盖声明式一侧。
- **破坏性变更：无（对旧代码）。** 但存在两个非旧代码冲突面：`<?...?>` 语法空间与 XML 处理指令语义冲突；与 XAML 工具链（设计器/热重载/BAML 编译器）的生态冲突。若实施，需要 `langversion` 门控与框架引用检查。

### 总评

- **达成程度：未达成**（作为规范文本）。场景真实，但载体不可行：单一示例不可编译、无文法、无框架映射契约、编译期校验承诺未两分，成本是"实现 XAML 编译器"。
- **LDM 三态建议：Table（PROPOSAL A，附复活信号）**；PROPOSAL B 否决留档；**PROPOSAL C（source generator）为 Consider**——无语言面的价值收割路径。
- **主要问题**：(1) 示例不可编译（`Content`+`Clicked` 无对应框架、无 `xmlns`）；(2) 需内建 XAML 编译器（类型/附加属性/标记扩展/资源解析）——成本远超语言特性；(3) `<?xaml?>` 与 XML 处理指令语法冲突；(4) `{Binding}` 运行时解析使"编译期校验潜力"言过其实；(5) 与既有 XAML 工具链重叠冲突无应对；(6) 与 `embedded-vb-mode` 的嵌入方向、与 `Let`/沉浸式文件的依赖未切分。

### 返工建议

- **补充章节**：`<?xaml?>` 文法（模式标记 vs 处理指令、终止规则、与 XML 字面量表达式位置的区分）；目标框架 `xmlns`→CLR 类型映射契约；根元素类型推断与报错；事件名绑定作用域；标记扩展（静态可降级 vs 动态需运行时）清单；Option Strict 分叉；兼容性小节（PI 语法空间、工具链冲突、`langversion` 门控）。
- **补充证据**：单一真实框架下的可编译示例（显式 `xmlns`，`Option Strict On` 验证，替换 `Content`+`Clicked` 混合）；原型分支真实链接替换占位符；视频 demo 转可复现代码仓库；共置需求的用户/代码库数据。
- **未决问题处理**：绑定校验两分——属性名/事件名编译期、绑定路径运行时（除非 `x:DataType` 钉住）；资源/样式/模板移出 v1 范围或声明运行时委托 `XamlReader`；工具链集成——明确字面量与 `.xaml` 文件语义必须一致，或声明字面量是静态子集。
- **方向调整**：本建议应改写为 PROPOSAL C 的引导文档——source generator 契约（类型化 `x:Name`、编译期事件接线、`x:DataType` 编译期绑定、与 XamlC/MAUI 复用），并把 `<?xaml?>` 语法作为"若 C 证明需求存在"的后续语言步骤，而非当前交付物。

---

## 附录：C# 生态与互操作考量

### 关系强度声明

本提案（XAML 字面量）不是一份 interop 提案：它不触及指针、marshaling、调用约定或新 CLR 元数据，正文第 11 节「运行时 / CLR 硬约束」也确认编译器产出的对象图是普通 IL。对照 C# interop 索引后，我们如实判断**直接对应关系弱**——C# 语言层没有、也从未提议过"把 XAML 变成一等表达式"。但有四条 C# 现实方向确实投影到这个提案上：嵌入式字面量哲学（collection expressions）、嵌入标记内容作为数据（raw string literals）、声明式 UI 的生态走向（source generators + 代码优先 MVU）、以及"C# 没有 XAML 字面量"这一事实本身。附录按这些轴对照，不硬凑。

### 相关 C# 现实方向

#### D1 嵌入式字面量哲学：C# 12 collection expressions

C# 12 引入集合表达式作为"构造字面量"，其 Summary 原句：

> "Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values."
> → `proposals\csharp-12.0\collection-expressions.md`（Summary）

其 Motivation 给出"字面量让编译器有优化自由度"的理由，与 VB 侧 2017-10-18 的"构造仪式遮蔽结构"判据是同一直觉的两岸：

> "Having a literal form allows for maximum flexibility from the compiler implementation to optimize the literal to produce at least as good a result as a user could provide, but with simple code."
> → `proposals\csharp-12.0\collection-expressions.md`（Motivation）

要点：C# 只对**框架中立的数据**做字面量（数组、`Span<T>`、`List<T>` 等，target-typed），从没把字面量扩展到**框架绑定的 UI 构造**。XAML 字面量是把同一哲学用到谱系的另一端——数据变成 `Button`/`Grid` 对象图，这一端 C# 明确留给了工具链与代码。

#### D2 嵌入标记内容作为数据：C# 11 raw string literals

C# 11 的原始字符串字面量是 C# 处理"嵌入式标记"的现有答案。其 Summary 原句：

> "Allow a new form of string literal that starts with a minimum of three `"""` characters (but no maximum), optionally followed by a `new_line`, the content of the string, and then ends with the same number of quotes that the literal started with."
> → `proposals\csharp-11.0\raw-string-literal.md`（Summary）

且其首个示例恰是 XML 内容：

```
var xml = """
          <element attr="content"/>
          """;
```
> → `proposals\csharp-11.0\raw-string-literal.md`（Summary）

注意这里的取向：C# 把 XML 内容当作**字符串数据**，构造/解析交给独立代码。这与 XAML 字面量（嵌入标记即构造 CLR 对象）是相反的哲学——C# 刻意不把嵌入内容直接变成对象图，而是保持"数据"与"构造"分离。

#### D3 声明式 UI 的生态走向：source generators + 代码优先 MVU

这是与本提案关系最强的现实方向，分两层。

**（a）XAML 的编译期处理已由 source generators 承担（XamlC / WinUI / MAUI）。** C# 生态把"声明式 XAML → 类型化代码"作为生成器工作，而非语言特性；这与 AOT/trimming 压力一致。C# LDM 在讨论 interceptors 时，明确把"类型系统外信息影响运行时代码"视为 AOT 难题：

> "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible."
> → `meetings\2023\LDM-2023-07-24.md`

**（b）C# LDM 正在探索代码优先的声明式 UI，方向是"留在语言里"，不是把 XAML 搬进语言。** 就在本 VB 会议前不到一个月（2026-07-15），C# LDM 与 Microsoft.UI.Reactor（WinUI 的 MVU 原型）团队开会，议程即 "Declarative UI construction"：

> "Reactor provides a C#-centric Model-View-Update programming model in which application code constructs an immutable graph of elements and the framework reconciles successive graphs to update the native UI."
> → `meetings\2026\LDM-2026-07-15.md`

会上 C# LDM 把"子元素 + 属性"的 UI 树构造问题归结为初始化器语法（对象/集合初始化器、factory initializers、混合初始化器），而非外来 DSL：

> "For example, collection expressions provide concise children and support spread elements, but do not by themselves provide a way to configure the containing element."
> → `meetings\2026\LDM-2026-07-15.md`

同一方向的候选提案 `mixed-object-and-collection-initializers` 的 Motivation 原句，几乎就是本提案想解决的问题的 C# 表述：

> "The most natural way to express "build an object that has some configured properties **and** a sequence of contained items" is to write the properties and the items together in a single initializer."
> → `proposals\mixed-object-and-collection-initializers.md`（Motivation）

而 C# LDM 对该探索的结论，明确是"成立工作组、留在语言内做实验"，不是"拥抱 XAML"：

> "We did not select or approve any individual language feature. We will form a working group to explore the space holistically."
> → `meetings\2026\LDM-2026-07-15.md`（Conclusion）

**事实核查：** `..\..\csharplang\proposals` 目录下没有任何以 XAML 为主题的提案文件（`**/*xaml*` 文件名无命中）；XAML 只散见于个别 LDM 会议与提案的旁及（如 2014/2017/2024/2026 的 LDM 记录、`mixed-object-and-collection-initializers.md` 的 HTML 元素示例）。这从文件层证实了本场「原则 4」的断言：C# 没有 XAML 字面量，也不在往这个方向走。

### 现实 vs 提案

| C# 现实方向 | 本提案对应面 | 判定 | 理由 |
|---|---|---|---|
| C# 12 collection expressions（字面量构造哲学） | PROPOSAL A 的"字面量/构造模式" | **兼容**（哲学层面） | 同一直觉：字面量让编译器优化构造、遮蔽仪式；但 C# 只对框架中立数据做，XAML 走到框架绑定端 |
| C# 11 raw string literals（嵌入内容即数据） | `<?xaml?>` 嵌入即构造对象 | **冲突**（取向） | C# 刻意保持"数据（字符串）"与"构造（代码/解析器）"分离，不把嵌入标记直接降级为对象图 |
| source generators / interceptors（编译期生成替代反射，AOT 压力） | PROPOSAL C | **兼容 / 需桥接** | 正是本场 RESOLUTION 的 Consider 产出；VBScript.NET 的 UI 场景应复用 XamlC/MAUI/WinUI 生成器，而非再造语言面 |
| 代码优先 MVU（LDM-2026-07-15, Reactor） | PROPOSAL A 的载体（编译器内建 XAML） | **冲突 / 脱节** | C# LDM 探索的是留在语言内的初始化器与 MVU 图重建，明确不选外来 DSL；本提案的载体与生态主流走向相反 |
| CLR / 元数据 / 互操作层 | 本提案无 CLR 约束 | **脱节（如实）** | 无指针、无 marshaling、无新元数据；interop 索引 T2/T3/T4 主线均不适用 |

### 对 VBScript.NET 的适应建议

- **UI 场景默认走 source-gen 桥（PROPOSAL C）。** 与 C# 生态的 XAML 工具链（XamlC、WinUI、MAUI generators）同向；VBScript.NET 不应把"实现 XAML 编译器"作为语言面承诺，而应把 `.xaml` → 类型化 partial class 的生成作为文件组织与工具链故事。
- **若未来绑定具体 UI 框架，可骑乘 C# 的代码优先 MVU 方向。** C# LDM 的初始化器探索（对象/集合/混合初始化器、factory initializers）与 VB 的 `With {...}` + `AddHandler` 天然亲近——VB 已有比整段外来 DSL 更 VB-like 的构造方式（本场第 4 节已论证）。Reactor 式的"不可变图重建"模型对脚本语言也友好。
- **元数据识别：无新增桥接点。** 本提案不产生新 CLR 元数据需求；消费 C# 生成的 UI 代码时，VB 编译器只需正常识别既有特性（partial class、`[GeneratedCode]`/`[CompilerGenerated]`、compiled bindings 的属性名/事件名解析）——属常规兼容，没有 RefSafetyRules 式的新护栏要学。
- **默认安全 / 按需动态。** 若未来允许 `Option Strict Off` 下把 XAML 作为 `XamlReader` 动态加载，注意其运行时反射语义与 AOT/trimming 的张力（本场第 4/6 节已禁止字面量回退到 `Object`）——但这属于框架运行时行为，非本提案语言面。
- **定位判断。** 本提案的差异化价值不在"语言比 C# 强"，而在"脚本场景用工具链而非语言解决共置"——这与 C# 生态的克制一致，正是 ModVB 可复用的判断。

### 对既有 RESOLUTION / 三态判定的影响

**判定不变：PROPOSAL A = Table（附复活信号），PROPOSAL B 否决留档，PROPOSAL C = Consider。** C# 现实证据不改变本场结论，反而从生态侧加固它：

1. **场景成立被加固。** collection expressions 证明"字面量让构造仪式让位"是活方向，且 C# LDM 2026-07-15 仍把"子元素 + 属性共置构造"当作开放问题——本场判据 1（2017-10-18 构造仪式遮蔽结构）得到 C# 侧平行印证。
2. **载体否决被加固。** raw string literals 显示 C# 把嵌入标记当数据而非对象图；MVU 探索显示 C# 生态的声明式 UI 方向是"留在语言内"。PROPOSAL A（编译器内建 XAML）与生态主流走向相反，本场对载体的四重否决（成本/框架耦合/工具链冲突/语法冲突）没有一条被 C# 现实推翻。
3. **Consider（PROPOSAL C）被加固。** XamlC/WinUI/MAUI generators 已是既成事实，interceptors 讨论进一步确认"编译期生成替代反射"是 AOT 压力的主流响应。PROPOSAL C 不是 VB 特色，而是生态共识在 VB 侧的落地。
4. **复活信号可补一条生态判据。** 除非 VB 能论证"框架绑定字面量"是 C# 明确放弃、而 VBScript.NET 应占有的差异化面（对应本场原则 4"充分理由偏离"的入口），否则 A 的复活门槛维持原样。C# 的克制本身不构成 VB 该偏离的理由——它构成的恰恰是"工具链已覆盖"的证据。

### 引用清单（本附录逐字引用的 C# 原文）

| C# 原文（逐字） | 来源文件 |
|---|---|
| "Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values." | `proposals\csharp-12.0\collection-expressions.md`（Summary） |
| "Having a literal form allows for maximum flexibility from the compiler implementation to optimize the literal to produce at least as good a result as a user could provide, but with simple code." | `proposals\csharp-12.0\collection-expressions.md`（Motivation） |
| "Allow a new form of string literal that starts with a minimum of three `"""` characters (but no maximum), optionally followed by a `new_line`, the content of the string, and then ends with the same number of quotes that the literal started with." | `proposals\csharp-11.0\raw-string-literal.md`（Summary） |
| "Reactor provides a C#-centric Model-View-Update programming model in which application code constructs an immutable graph of elements and the framework reconciles successive graphs to update the native UI." | `meetings\2026\LDM-2026-07-15.md` |
| "For example, collection expressions provide concise children and support spread elements, but do not by themselves provide a way to configure the containing element." | `meetings\2026\LDM-2026-07-15.md` |
| "We did not select or approve any individual language feature. We will form a working group to explore the space holistically." | `meetings\2026\LDM-2026-07-15.md`（Conclusion） |
| "The most natural way to express "build an object that has some configured properties **and** a sequence of contained items" is to write the properties and the items together in a single initializer." | `proposals\mixed-object-and-collection-initializers.md`（Motivation） |
| "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." | `meetings\2023\LDM-2023-07-24.md` |

### OPEN QUESTIONS

- 无 C# 原文存疑点：本附录所有逐字引用均已在 `..\..\csharplang` 对应文件核实，无 **Suspect** 项。
- 开放问题（非本提案范围）：VBScript.NET 是否把代码优先 MVU（Reactor 式）或混合初始化器作为未来 UI 方向；XAML 工具链（XamlC/MAUI generators）的具体语言协作属 dotnet/runtime 与独立产品，`csharplang` 无正文可核实（与索引第四节 OPEN QUESTIONS 一致）。
