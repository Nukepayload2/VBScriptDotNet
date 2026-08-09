# Visual Basic Language Design Meeting
August 8, 2026

## Agenda
* [Proposal - Immersive Files ("Top-Level Code") / 顶级代码（沉浸式文件）](#proposal---immersive-files-top-level-code)

## Proposal - Immersive Files ("Top-Level Code")
_Related: [vblang #102](https://github.com/dotnet/vblang/issues/102) - Support Top-Level Statements in a Single Entry-Point File（主线，2017-12-06 推迟，此后未再讨论）；Anthony D. Green 原文第 2.1 节 "Immersive Files（'Top-Level Code'）"；交互建议：[embedded-VB-mode](../proposals/proposal-embedded-vb-mode.md)、[local-declarations（`Let`）](../proposals/proposal-local-declarations.md)、[module-enhancements](../proposals/proposal-module-enhancements.md)_

### 场景：缺口在哪里

We opened with a scenario that anyone who has taught Visual Basic to a first-time programmer will recognize. The canonical first program, today, looks like this:

```vb
Module Program
    Sub Main()
        Console.WriteLine("Hello, World!")
    End Sub
End Module
```

The proposal under review (Anthony 原文第 2.1 节) compresses that to a single line:

```vb
Console.WriteLine("Hello, World!")
```

We think the motivation is genuine and, in the abstract, hard to argue with. The `Module`/`Sub Main`/`End Sub`/`End Module` shell is pure ceremony for the learner and for the one-off script writer. Anthony's framing — that the entire source file *is* the program, an "Immersive File" — is attractive precisely because it matches the mental model of the QBasic era: you type statements, you run them. His `SecondProgram.vb` and `Starfield.vb` examples extend that to functions and loops without any container:

```vb
' This is an entire program.
Imports System.Console

Function Prompt(message As String) As String
    WriteLine(message)
    Return ReadLine()
End Function

Let name = Prompt("What is your name?")

WriteLine($"Hello, {name}.")
```

And the `Form1.vb` example promises WinForms event handlers without the class shell:

```vb
Sub Button1_Click() Handles Button1.Click
    MsgBox("Hi")
End Sub

Sub Button2_Click() Handles Button2.Click
    MsgBox("Bye")
End Sub
```

The proposal closes with "Past Demos (Early Prototypes)" — seven video links (基础语法、Notebooks、QBasic 式游戏循环、网页、Web 控件/组件、Web API、XAML/Xamarin.Forms). **We want to be clear about the evidence here: these are videos, and there is no code.** We can watch that the direction was prototyped; we cannot inspect, reproduce, or build on what was demonstrated. The evaluation standard's six-step evidence ladder treats that as "已运行" only in the sense that *someone* ran something once; as documentation it stops at "已提供/已检查". We will not let a video carry the weight of a design decision.

The history of this idea in the main line is instructive. vblang #102 ("Support Top-Level Statements in a Single Entry-Point File") was discussed on 2017-12-06. We decided then to take a wait-and-see approach with try.dot.net, because that site's scripting dialect was teaching "a slightly different version of the language with slightly different semantics," and the note resolved to reconcile the standard and scripting dialects in the new year. **It was deferred to January 2018 and never returned to.** The world did not stand still: C# 9 shipped top-level statements in 2020, and they have proven popular in tutorials and small programs. So the parity gap is real, visible, and now has years of C# field data behind it. What the main line deferred out of caution, Anthony has picked up with a much wider scope — not just a single entry-point file, but pages, notebooks, and forms as immersive files.

There is a second, quieter voice in the room. 2018-05-30 recorded that "the majority of Visual Basic customers (hundreds of thousands of quiet customers each month) primarily want VB to keep doing what it does now," and 2018-06-13 set a deliberately high bar for "a second way to do things." A first-program author writes Hello World once. The quiet customers who sustain the language never see the boilerplate again. So we must ask, honestly, whether the audience that benefits most from this feature is also the audience whose demand we can actually measure. We think the answer is yes for a scripting-oriented product — and that this is precisely the product ModVB is — but we have no VB-specific request data, and we should not pretend otherwise.

### 候选方案

We enumerated the ways the feature could be realized.

**PROPOSAL A — 隐式模块宿主（implicit module host）.** The compiler implicitly places the file's code into a synthesized `Module`. Member declarations become module members; executable statements are collected into a synthesized `Sub Main`. This is the proposal's own "Alternatives" suggestion, and it is the natural VB answer: VB already synthesizes an entry point for projects that lack a user `Main`, modules already lift their members, and the CLR sees nothing new. The lowering for `FirstProgram.vb` would be approximately:

```vb
Module _ImmersiveEntry
    Sub Main()
        Console.WriteLine("Hello, World!")
    End Sub
End Module
```

**PROPOSAL B — 显式 `Module` 语法糖（explicit `Module` sugar）。** Top-level code is a desugaring of an explicit `Module Program` + `Sub Main`, and the IDE may materialize that shell on demand (a "show the shell" command). The distinction from A is philosophical: the container exists in the source model and can be reified, so there is one mental model and one representation.

**PROPOSAL C — 类 `Main` 入口生成（class-based `Main` generation）。** Mirror C#: synthesize a `Class` with a `Shared Sub Main`, keeping the language surface free of module semantics. This maximizes cross-language symmetry with C# top-level statements.

**PROPOSAL D — 维持现状（status quo）。** Keep requiring the shell; the project template already ships `Sub Main`. The proposal's own "Alternatives" argues this leaves the onboarding cost in place, and we agree, but the argument deserves a hearing below because "template does it" is a real counter that the main line has effectively been leaning on for eight years.

We also agreed early that this is not one feature. The document bundles four distinct promises — (i) a file of statements is a program; (ii) a `.vbxhtml` file is a web page; (iii) a `.vb` file hosts `Handles` event handlers; (iv) notebooks and game loops — and they do not stand or fall together. We will try hard to keep them separable.

### 权衡（Q&A）

We walked the design through the LDM questioning list. The points below are not in priority order; they are the questions that actually bit.

**1. 语法/文法歧义。** Today a VB compilation unit is `Option`/`Imports` statements followed by `Namespace`/type declarations; statements cannot appear at file scope, so a bare-statement file is a compile error. Extending the grammar to allow `Imports`, members, and statements at the top level is a contained change, and we see no genuine ambiguity between a member declaration and a statement — the two are disjoint syntactically (`Function`/`Sub`/`Class`/`Property` keywords versus statement forms). One caution: VB has no expression statements, so the one-line program relies on a *call statement* (`Console.WriteLine(...)`), which is fine. `Probably` clean. The larger grammar question is the `<{ ... }>` expression syntax in the `.vbxhtml` example — that is not this proposal's grammar at all, and we will return to it under interaction.

**2. `Let` 与既有语法的冲突。** The proposal uses Anthony's `Let` declaration keyword at the top level. `Let` is already a contextual keyword in VB for LINQ query clauses:

```vb
From p In products
Let q = p.Price * 1.1      ' 查询子句 Let（既有语法）
Select q

Let q = 42                 ' 顶层声明 Let（新语法）—— 解析器需区分上下文
```

`Probably` resolvable — query `Let` only appears inside a query context, so a statement-position `Let` is a distinct parse — but we need this confirmed against the full parser, and against the VB6-era `Let x = 1` assignment statement. The companion proposal `local-declarations` flags the same concern; whichever lands first must not foreclose the other. This is the kind of "subtle character changes meaning" case we normally reject (cf. the `Return?` decision), though here the disambiguation is by context rather than by a character, which is more defensible.

**3. 角案例：多个沉浸式文件。** The proposal leaves multi-file semantics open ("多个沉浸式文件之间如何互相引用"). We spent the most time here, because it determines everything about visibility and state. Two coherent models exist:

- **每文件一个隐式模块**（one synthesized module per file）— files are islands; cross-file reference requires qualification through a hidden name. We think this is a non-starter for any useful tooling story.
- **整个项目共享一个隐式模块**（one synthesized module per project）— members are mutually visible, mirroring the fact that module members already get lifted into the containing namespace. This is the model we like.

If the host is a single shared module, the entry point must be singled out: **exactly one immersive file per compilation may contain executable statements**, echoing the very title of vblang #102 ("a Single Entry-Point File"). Other immersive files contribute declarations only. Example of the intended shape:

```vb
' Program.vb —— 唯一含顶层可执行语句的沉浸式文件
Imports System.Console
WriteLine(Greet("Ada"))
WriteLine(Greet("Grace"))

' Helpers.vb —— 沉浸式声明文件：只允许成员声明，无顶层语句
Function Greet(name As String) As String
    Return $"Hello, {name}."
End Function
```

We like this split: it kills the "implicit global state" objection for the statement file, keeps cross-file declaration access working, and bounds the feature to something we can actually specify. It is, however, a *decision the document does not make*, and the invisible module name (a reserved, collision-checked name like `_ImmersiveEntry`) needs explicit conflict reporting. We will record the naming rules as an OPEN QUESTION rather than pretend we settled them.

**4. 角案例：命名空间与可见性。** The proposal says the host container and namespace inference are "原文未说明". We think the defensible default is "the same rules as a `Module` in the same location": namespace inferred exactly as today (project root namespace plus folder), and no new inference machinery. Visibility is thornier. Classic VB modules default members to `Public` and lift them into the containing namespace — which is precisely the namespace pollution the `module-enhancements` proposal is trying to walk back. If the synthesized host follows classic behavior, an immersive file would silently export every helper into the project namespace. We therefore `Suspect` the synthesized host should be a *non-hoisting* module whose members are `Friend` by default, in line with the `module-enhancements` direction — but that in turn means the "file is the whole program" illusion only holds for the entry file, and cross-file access must go through names we have decided to hide. **This is a real tension between two proposals that both touch the same machinery, and the two documents do not acknowledge each other.** We flag it as a TODO to reconcile.

**5. 作用域与绑定：顶层变量绑定到哪里。** The semantic question — what symbol does `name` in `SecondProgram.vb` resolve to — drives IntelliSense, refactoring, and the debugger. Under our split: top-level `Let` in the entry file binds to a *local* of the synthesized `Main` (not a module field), so it is ordinary local-variable semantics: definite assignment applies, no cross-file sharing, no static state. `Probably` the cleanest line. But it raises an edge we could not fully close: in a *declaration-only* immersive file (no statements), is a top-level `Let` a module field, or is it disallowed? Both answers are defensible; the second is simpler and we lean to it, but the proposal is silent and we are `Suspect` we have not seen all the cases. A related corner: may an immersive file contain a `Namespace ... End Namespace` block? We think the entry file should not, and declaration files should follow today's rules. OPEN QUESTION.

**6. 与既有特性交互：`Handles` / WinForms。** The `Form1.vb` example is the one that makes us most uncomfortable. In real VB, `Sub Button1_Click() Handles Button1.Click` works because `Button1` is a `WithEvents` field of the partial class the WinForms designer generates. At the top level of an implicit module there is no such field, and the proposal does not say where `Button1` comes from. If immersive files participate in a designer-generated partial *class*, the host is a class, not a module — contradicting PROPOSAL A. If the host is a module, the designer would have to synthesize `WithEvents` fields on a module, and the whole design-time story (designer serialization, `InitializeComponent`, the toolbox) is unspecified. The proposal lists this honestly in its Unresolved questions, and we respect that, but it means **the WinForms claim cannot be validated from this document at all.** We do not believe `Handles`-at-top-level is in the entry-point subset; it is a separate scenario that needs its own speclet. Table.

**7. 与既有特性交互：`.vbxhtml` 与 `Imports`/`Option`。** The `.vbxhtml` example is a web page with `<{ ViewData!Message }>` embedded expressions. Two problems. First, `<{ ... }>` is not grammar defined by this proposal; it belongs to the `embedded-vb-mode` proposal (the `<?vb?>` parsing mode), and the proposal admits the split is "原文未明确切分". Second, `ViewData` is a framework-provided ambient object with undefined identity — nothing in the document says who binds it. We concluded the `.vbxhtml` promise is out of scope for the top-level-code proposal and must live or die with `embedded-vb-mode`. The good news is the interaction we *do* need — file-scope `Imports`, `Option Strict On/Off`, `Option Explicit` — is boring and already well-defined; `Imports` at file scope is exactly today's semantics, and an immersive file should be subject to the same `Option` statements as any other file.

**8. Breaking change。** We can see none for existing code. A file with bare statements is a compile error today; this feature turns an error into a program, and errors being removed are not breaks. The synthesized-module name introduces a new collision diagnostic, which is also not a break. The only real compat surface is `Let` (item 2), and there is no evidence that VB.NET accepts `Let x = 1` as an assignment today — `Suspect` it was dropped when the language moved to .NET, with `Let` surviving only as a query keyword. We will verify against the parser before committing, but we do not expect a legacy break. Consistent with the main line's "we will almost never make breaking changes," the feature clears the bar.

**9. Option Strict 分叉。** Both compiler paths must compile the same entry point. Under `Option Strict On`, top-level `Let` infers statically (`name` is `String`); under `Option Strict Off`, a `Let x = <Object-typed expression>` binds to `Object` and late-bound calls work exactly as they do inside a `Sub Main` today. We see no new divergence — provided the synthesized entry point is subject to the file's `Option` statements like any method body. But note the `Starfield.vb` example: it calls `buffer.Clear(...)` and `buffer.FillRectangle(...)` **without ever declaring `buffer`**. Under `Option Strict Off` that would be a late-bound call that only fails at runtime; under `Option Strict On` it is a compile error. The example as written cannot compile under `Option Strict On`. Either `buffer` is an ambient/host member we were not told about, or the example is incomplete. We must insist that every example in the proposal compile under `Option Strict On`, or the document must say it targets `Option Strict Off`.

**10. IDE / IntelliSense 影响。** Where does a top-level member appear in Class View, and what does the debugger show in the call stack? Under PROPOSAL A the honest answers are "inside a synthesized module named `_ImmersiveEntry`" and "a synthesized `Main`". That is mildly confusing for the very beginners the feature targets, but it is transparent and we think acceptable; C# lives with `<Program>$`. Go-to-definition on a top-level `Function` must land in the file, and breakpoints on top-level statements must map into the generated `Main` — both `Probably` standard Roslyn work. The single shared module model (item 3) is also what makes "find all references" across immersive files behave like module members, which we regard as a requirement, not a nicety.

**11. 数据 / 普遍性。** We have no VB-specific user data, and the main line never returned to #102, so there is no institutional signal either. What we do have: (a) C# 9 shipped and the feature is widely used in tutorials and small programs; (b) the main line's own 2017 rationale — reconciling the standard dialect with the scripting dialect — remains unresolved, and this proposal is arguably the better version of that reconciliation; (c) the product this LDM serves is a scripting-oriented VB, where "type statements, run them" is not an edge case but the core identity. The "quiet customers" caution applies to the general VB audience, not to a scripting fork. We think the *data gap is real but the product mission is the data*: for VBScript.NET this is central, for the general VB audience it is peripheral.

**12. 更简替代：模板不是对手。** The status-quo defense — "the project template already generates `Sub Main`" — covers Hello World and nothing else. It does not help a one-off script, a notebook, a game loop, or a page-as-file, and it does not reconcile the scripting dialect that #102 was deferred to fix. As a counter-argument it gets a hearing and we reject it for the narrow subset. We do, however, take the *spirit* of "treat it as an optimization, not a feature": the narrow subset is best implemented as an extension of machinery VB already has (entry-point synthesis), not as a new runtime concept.

**13. 成本 / 优先级。** The narrow subset — statements into a synthesized module `Main`, declarations into a shared module, name/conflict handling, IDE mapping — is moderate, well-understood Roslyn work with no CLR involvement (item 14). The broad bundle — `.vbxhtml` rendering, notebooks, top-level `Handles`, Web API — is expensive, depends on at least two other proposals (`embedded-vb-mode`, `module-enhancements`), and is nowhere near specified. Cost/value only balances for the narrow part, and only in a scripting-oriented product. We are comfortable saying: worth doing narrowly, not worth the bundle.

**14. 运行时 / CLR 硬约束。** None. A module with a `Shared Sub Main` is exactly what the compiler already emits for module-based projects; `[StandardModule]` types and synthesized entry points are long-settled PE territory. We see no PEVerify, metadata, or hosting constraint. `Handles` at module level would need `WithEvents` fields on the module — modules can have fields, so even that is a design problem, not a CLR one.

**15. 值不值得做。** Value is high for the target audience and low for the general "quiet" majority; cost is moderate for the narrow subset; risk of breakage is nil but risk of *design debt* is real because the document leaves host identity, visibility, and multi-file rules open. On the whole: **the idea is worth doing; the document, as a specification, is not ready.** That is the honest position, and it is the position the remainder of the note operationalizes.

### VB 基因对照

We then held the feature against our design principles, and against the main-line table.

- **原则 1（永不破坏现有代码）** — 通过。无可见破坏，`Let` 待核查。
- **原则 2（保持 VB-like）** — 半通过。隐式模块宿主、语句式程序、`Handles` 直觉都是纯正 VB 血脉；但 `.vbxhtml`、notebook、`Let` 把多份不同建议的语法捆了进来，体感不统一。
- **原则 3（不引入第二种做事方式）** — 这是本建议最锋利的一条。顶级代码就是"第二种写程序的方式"，主线 2018-06-13 对"making a second way to do things"设了高门槛。我们的回应是：门槛针对的是**已有方案的替代**，而脚本/入口文件场景主线从未提供过方案——模板是脚手架，不是语言特性。所以这不是"第二方式"抢占地盘，而是补上主线推迟后一直空缺的位置。这个论证并不轻松，但房间多数认为成立。
- **原则 4（默认跟随 C#）** — 我们看了 C# 9 的设计，故意不照搬。C# 把顶层代码放进一个合成的 `<Program>$` 类；VB 的天然宿主是模块，模块提升成员的语义与 `Handles` 的存在使 C# 方案无法直接移植。这是"有充分理由偏离"的情形，符合 2018-12-19 确立的平衡（跟随 C#，但"VB-like 优先"）。
- **原则 5（读起来像英语、对新手友好）** — 通过，而且是本建议最强的正面。
- **原则 6（不为边缘场景加特性）** — 对一般 VB 受众，脚本是边缘；对 VBScript.NET 是核心。依产品定位而定。
- **原则 7（避免隐蔽控制流）** — 顶层语句的隐式入口是"隐蔽的 Main"，但语义可预期且 C# 已确立先例；真正的隐蔽风险在 `Handles`/`.vbxhtml` 的未定义绑定，已按 Table 处理。
- **原则 8（不与既有语法冲突）** — 唯一的真实冲突面是 `Let`（查询子句），待 parser 核查。
- **原则 9（消除常见样板）** — 通过，且是本建议的核心主张。
- **原则 10（冗长只在有用时是美德）** — 样板对新手无用，移除正当。

对照主线表（2.3）：顶层语句一行，主线立场是"推迟考虑"，Anthony 是"Immersive Files"——**一致 / 更激进**。我们的判断与 Anthony 共享直觉，但节奏不同：我们只拿走"单文件入口"这一块做 Active，把"沉浸式全家桶"按下 Table。这正是 2018-12-19 模式匹配那场会议的做法——对一个激动人心的大方向，先定下能定的小范围，其余留作后续。

### RESOLUTION

**RESOLUTION:** We split the proposal into a narrow subset and a broad bundle, and we rule on each separately.

1. **单文件入口子集（single-entry immersive file）— `Consider`，返工后可在 ModVB 沙盒内升为 `Active` 孵化。** 定案要点：
   - 宿主为**隐式模块**（PROPOSAL A），不采用 C# 的类生成（C 否决）——模块是 VB 的天然宿主，且编译器已有入口点合成机制可复用。
   - 显式 `Module` 语法糖（B）**否决**：它的"可显性化"恰恰消解了本建议的核心价值——没有外壳。我们不需要在源模型里保留一个随时可显现的容器。
   - **每次编译至多一个沉浸式文件可含顶层可执行语句**（呼应 #102 标题 "Single Entry-Point File"）；其余沉浸式文件只含声明，并入同一合成模块，成员相互可见。
   - 顶层 `Let` 绑定到合成入口点的**局部变量**，不是模块字段，不是跨文件共享状态。（声明文件中的顶层 `Let` 倾向禁止——见 OPEN QUESTIONS。）
   - 命名空间沿用现行文件规则；合成模块使用保留名并对冲突报错。
   - 每个示例必须能在 `Option Strict On` 下编译；`Starfield.vb` 的未声明 `buffer` 必须补声明或说明上下文。

2. **沉浸式全家桶（`.vbxhtml` 页面、notebook、顶层 `Handles`、Web API）— `Table`。** 这不是否决，是搁置，并附明确的复活信号：
   - `Handles`/WinForms 需要一个独立 speclet 说明 `WithEvents` 字段与设计器交互如何映射到非类宿主；
   - `.vbxhtml` 依赖 `embedded-vb-mode` 落地并给出 `<{ ... }>` 的文法与绑定；
   - Past Demos 须公开可复现的代码仓库（视频不算证据）。

**Implication:**
- 与 `module-enhancements` 存在真实冲突面（合成模块是否提升成员、成员默认可见性），两份建议必须交叉引用并统一语义——TO DO。
- 与 `local-declarations`（`Let`）的解析兼容性必须先行核查——TO DO。
- 文档需要补文法（compilation unit 扩展）、多文件语义、可见性、以及一份兼容性小节——见附录返工建议。
- 主线 #102 的"脚本方言与标准方言调和"目标仍未达成；本子集应在其提案页记录为对 #102 的延续回应。

**三态判定：** 整体 `Consider`；窄子集返工后候选 `Active`；宽泛全家桶 `Table`（附复活信号）；PROPOSAL B、C 分别按"否决的备选方案"留档。

### OPEN QUESTIONS / TODO

- [ ] 合成模块的保留名与冲突报错规则（`_ImmersiveEntry` 撞名、`<HideModuleName>` 是否携带）。
- [ ] 声明文件中顶层 `Let` 是字段还是禁止（我们倾向禁止，未定案）。
- [ ] 沉浸式文件可否含 `Namespace ... End Namespace` 块（入口文件倾向禁止）。
- [ ] `Let` 与查询子句 `Let`、VB6 `Let` 赋值语句的 parser 兼容性核查。
- [ ] 与 `module-enhancements` 的宿主模块提升/可见性语义统一。
- [ ] 顶层 `Return` / `Await` 是否允许（C# 允许顶层 `return` 与 `await`；VB 对应 `Exit Sub`/`Async Sub Main` 合成，`Probably` 允许，未定案）。
- [ ] 所有示例在 `Option Strict On` 下可编译的验证（`Starfield.vb` 的 `buffer`）。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-top-level-code.md` — 顶级代码 / 沉浸式文件（Immersive Files, "Top-Level Code"）。
- 来源：Anthony 原文章节 **2.1 Immersive Files（'Top-Level Code'）**（第 2 章 "Streamlining and Boilerplate Reduction"），附 "Past Demos (Early Prototypes)" 七条视频链接。
- 配方目标（Motivation 摘要）：降低入门门槛（一行 Hello World）、脚本/小工具/QBasic 式体验、整个文件即程序/页面（`.vbxhtml`）、WinForms 顶层事件处理器。

### 五维评分表

| 维度 | 得分 | 评价（对照行为锚点） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **3/5** | 主效果（单文件即程序、一行 Hello World、脚本/QBasic 体验）书面清晰、示例基本可编译，锚点接近 4；但关键子效果（多文件、页面、`Handles`、notebook）未定型或不可验证，核心语法（宿主/命名空间/可见性）未决 ≥4 项 → 效果证据封顶，落在"只覆盖部分场景；主效果显现但关键子效果缺失/消退" | 已检查（Past Demos 视频止于"已提供"，无代码，不可"已运行"复核） | 核心语法未定型 = 效果未显现；`Starfield.vb` 引用未声明的 `buffer`，在 `Option Strict On` 下不可编译；WinForms/页面承诺无法从文档验证 |
| 特性 | **3/5** | 主体继承 VB 基因（模块提升、入口合成、语句式程序、`Handles`、QBasic 血脉），但明显借鉴 C# 9 top-level statements 却未说明，且打包了 `.vbxhtml`、`Let`、notebook 等多个无关能力 → "明显借鉴外部但做了 VB 化改造；或打包了次要无关能力" | 已检查 | 文档未承认 C# 9 来源；与 `embedded-vb-mode`、`local-declarations`、`module-enhancements` 的依赖未切分；`Let` 作为独立延伸混入 |
| 品质 | **3/5** | 六章节模板结构完整，Drawbacks/Alternatives/Unresolved 具体诚实（≥4 项关键未决如实列出），但状态栏为占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`），无文法（BNF）、无兼容性小节，`.vbxhtml` 边界含糊 → "缺某一章节或在关键处边界含糊" | 已检查 | 红旗：状态占位链接；无文法/无 breaking-change 分析；"这属于哪个建议"的边界未切分 |
| 属性 | **3/5** | 对 VBScript.NET（火=脚本新阶段、水=差异化、光=盘活 QBasic/VB6 资产）明显正向；但风=与主线"推迟"立场断裂、与多份建议一致性风险，暗=合成宿主与 `module-enhancements` 冲突、`Let` 关键字歧义、Past Demos 不可复核 → "有得有失，文档未充分权衡" | 已检查（预测性结论，标"待定"） | 未对冲暗风险：多文件可见性、宿主命名冲突、与既有特性交互三处冲突无应对设计 |
| 炼金成分 | **4/5** | 主要材料（Anthony 原文 2.1、QBasic/VB6 继承）标注准确；但借鉴 C# 9 "Top-Level Code" 未点名，`Let`/`.vbxhtml` 源自其他章节未交叉引用 → "主要成分标注正确，个别来源或属性说明略含糊" | 已检查 | C# 来源未标注；成分间依赖关系未交代 |

### 设计原则对照
- **与 VB 基因**：部分一致。符合原则 1（不破坏）、5（新手友好）、9（消样板）；偏离原则 3（引入第二方式，但有正当性论证）与原则 6（边缘/核心依产品定位而定）。
- **与主线关系**：主线一致 / 更激进（对照表 2.3 顶层语句行：主线"推迟考虑"，Anthony "Immersive Files"）。窄子集是对主线 #102（2017-12-06 推迟）的延续回应；宽泛全家桶为 Anthony 的激进延伸。
- **破坏性变更**：无（裸语句文件当前为编译错误，此特性使其变为程序）。唯一风险面是 `Let` 关键字（与查询子句 `Let`、VB6 赋值 `Let` 的解析兼容），`Suspect` 无实际遗留破坏，须 parser 核查。

### 总评
- **达成程度**：部分达成。单文件入口子集的动机与基本形态清晰、可论证；但核心语法（宿主、命名空间、可见性、多文件）、交互（`Handles`、`.vbxhtml`）与证据（Past Demos 无代码、状态占位）均未到位，作为规范文本未达成。
- **LDM 三态建议**：**Consider**。窄子集在返工后可为 VBScript.NET 的 `Active` 孵化候选；宽泛全家桶 `Table`（附复活信号）。
- **主要问题**：(1) 核心宿主/作用域/命名空间未定；(2) 多文件模型未定（共享模块 vs 每文件模块）；(3) `Handles`/WinForms 语义与设计器字段关联空洞；(4) `.vbxhtml` 依赖 `embedded-vb-mode`，边界未切分；(5) 状态栏占位、Past Demos 无代码、示例未在 `Option Strict On` 下验证；(6) 与 `Let`/`Dim` 建议的依赖与关键字兼容未交代。

### 返工建议
- **补充章节**：文法（compilation unit 扩展：`Imports`/成员/语句在文件作用域）；多文件语义（单入口文件 + 共享合成模块 + 命名冲突）；宿主模块命名、成员可见性、与 `module-enhancements`（提升/`<StandardModule>`）的统一；兼容性小节（`Let` 关键字、错误→程序的转变）。
- **补充证据**：把 Past Demos 转成可复现的代码仓库链接（视频止于"已提供"）；原型分支真实链接替换占位符；所有示例在 `Option Strict On` 与 `Off` 双路径下编译通过（修复 `Starfield.vb` 的 `buffer`）。
- **未决问题处理**：逐条定夺——顶层 `Let` 绑定到入口点局部（倾向）；声明文件顶层 `Let` 禁止（倾向）；顶层 `Return`/`Await` 允许（`Probably`）；合成模块保留名与冲突报错；`Handles` 场景拆独立 speclet；`.vbxhtml` 移交给 `embedded-vb-mode`。
- **范围切分**：明确声明本建议只覆盖 `.vb` 沉浸式文件子集，`.vbxhtml`、notebook、`Handles`、Web API 各归其位，避免一份建议承载四个特性。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang-index.md` 与 dotnet/csharplang 官方镜像（`..\..\csharplang`，main 分支）核实。凡 C# 原文均逐字标注出处；无法核实的标 **Suspect** / **OPEN QUESTIONS**。本附录只追加考量，不改动正文 RESOLUTION 与三态判定。

### 相关 C# 现实方向

本提案（顶级代码 / 沉浸式文件）在 C# 生态中有两个直接对应物：**C# 9 top-level statements（语言特性，已落地）** 与 **"simple C# programs"（脚本化方向，LDM 探索中）**。

**1. C# 9 top-level statements —— 官方提案原文（已逐字核实）：**
- 「Allow a sequence of *statements* to occur right before the *namespace_member_declaration*s of a *compilation_unit* (i.e. source file).」→ `proposals\csharp-9.0\top-level-statements.md`（Summary）
- 「Only one *compilation_unit* is allowed to have *statement*s.」→ 同文件（Detailed design — Syntax）
- 「The type is named "Program", so can be referenced by name from source code. It is a partial type, so a type named "Program" in source code must also be declared as partial.」→ 同文件（Semantics）
- 「It is possible to specify a different entry point via the `-main:<type>` compiler switch.」→ 同文件（Semantics）
- 「The entry point method always has one formal parameter, `string[] args`.」→ 同文件（Semantics）
- 「Async operations are allowed in top-level statements to the degree they are allowed in statements within a regular async entry point method.」→ 同文件（Semantics）

C# 的合成入口点签名按「是否出现 `await` / 是否出现带表达式的 `return`」四态决定：`static void` / `static Task` / `static int` / `static Task<int> Main(string[] args)`——见同文件 Semantics 的签名表。C# 侧对"显式声明的入口点候选"的处理是「Explicitly declared methods that by convention could be considered as an entry point candidates are ignored. A warning is reported when that happens.」→ 同文件（Semantics）。

**2. "simple C# programs"（LDM-2021-05-12）—— 脚本化方向（已逐字核实）：** C# 在 2021 年把"一个 `.cs` 文件即可运行"当作新一代用户（从 Go/JS/Python 而来）的核心期待，并开始探索 `#r`/`#load` 指令与 dll/exe、single-file/trimming 设置该放文件层还是项目层。当日为探索性讨论，未定案：
- 「These users instead expect to be able to simple make a `.cs` file and run it, with potentially more ceremony as they start adding more complex dependencies or other scenarios.」→ `meetings\2021\LDM-2021-05-12.md`（Simple C# programs）
- 「What about output settings such as dll vs exe, or single-file and trimming settings? We don't have answers for these today, and some of these answers will be driven by discussions with the SDK teams, but they're all part of determining where the cliff of complexity will land.」→ 同文件（Simple C# programs）

**3. 治理节奏（索引 T1）：** C# 是 CLR/.NET 低层能力的主要推动者；top-level statements 归档于 `proposals\csharp-9.0\`、随 C# 9（2020）发布，已积累数年的教程与小工具场数据——这正是正文 item 11 引用的"C# 9 已发布且广受欢迎"的出处侧证。

### 现实 vs 提案

**兼容（同向，占多数）：**
- **入口点合成机制同一。** C# 合成 `Program.$Main`，提案（PROPOSAL A）合成 `Module _ImmersiveEntry.Sub Main`；两者在 PE/metadata 上都只是普通入口点方法，无新元数据、无 PEVerify 约束（正文 item 14 已独立得出同样结论）。C# 事实与 VB 事实相互印证。
- **排他性约束同构。** C#「Only one *compilation_unit* is allowed to have *statement*s.」与提案「每次编译至多一个沉浸式文件含顶层可执行语句」是同一规则的两种表述。
- **脚本市场压力一致。** LDM-2021-05-12 确认 C# 用户期待"make a `.cs` file and run it"；这与 VBScript.NET 的"type statements, run them"是同一批用户、同一个生态信号。vblang #102 当年推迟调和"脚本方言 vs 标准方言"，C# 已用 top-level + simple-C#-programs 亲自验证了这条路——提案是对该推迟的延续回应，方向与 C# 现实同向。

**冲突 / 需桥接（4 点）：**
- **混编程序集入口点仲裁。** 同一程序集若同时含 C# top-level 与 VB 沉浸式文件，两套机制各合成一个入口点。C# 提供 `-main:<type>` 开关（原文见上）；VB 侧应对应 `<StartupObject>`。需桥接：混编时唯一入口点如何仲裁、其余合成入口点如何处理。C# 的"忽略 + 警告"模型（原文见上）是 VB 可对齐的形态。
- **`args` / `return` / `await` 对齐。** C# 合成入口点恒带 `string[] args`、支持 `return expr`（退出码）与 `await`（自动升级为 async Task）。VB 提案对顶层 `Return`/`Await` 仍是 OPEN QUESTION（正文 OPEN QUESTIONS 项）。若要 .vbx 脚本与 C# top-level 在"命令行参数、退出码、异步"上可互换，需补齐 `Sub Main(args() As String)`、`Async Sub Main`、`Function Main As Integer`（或 `Environment.ExitCode`）语义。
- **宿主差异 = 有理由的偏离。** C# 用 `Program` partial class（且要求源码中同名类型须 partial——这条约束对 VB 无意义）；VB 用隐式模块。这是正文原则 4 已定的"VB-like 优先"偏离。注意其代价：混编/调试时 IDE 将同时看到 `<Program>$` 与 `_ImmersiveEntry` 两种合成形态，IDE 需同时认知。
- **AOT / single-file / trimming。** C# 把 single-file/trimming 挂在"项目级设置、文件内不管"的探索方向（LDM-2021-05-12 未答，倾向项目层）。若 VBScript.NET 走 NativeAOT，`Option Strict Off` 的晚期绑定沉浸式文件是主要障碍（同决策文件 M5/M8）。窄子集默认编译到正常 Main，无碍；解释/动态路径须显式 opt-in。

**脱节（1 点）：**
- **`.vbxhtml` / notebook（Table 部分）在 C# 生态的对应物不是语言语法，而是工具级脚本指令**（`#r`/`#load`、SDK 层面的"复杂度悬崖"）。C# 的取向是把脚本复杂度"放文件之外"（项目/SDK 层），而 Anthony 全家桶想做成语言语法。二者方向脱节——这从生态侧强化了正文对全家桶的 Table 判定。

### 对 VBScript.NET 的适应建议

- **窄子集入口点与 C# 生成入口点保持同构**：`Sub Main(args() As String)`（接受命令行参数）、支持 `Async Sub Main`、退出码用 `Function Main As Integer` 或 `Environment.ExitCode`。这样 .vbx 脚本可被 `dotnet run`/宿主进程以与 C# top-level 相同的方式调用，实现"同一入口点语义、两门语言"。
- **混编入口点仲裁**：对齐 C#「忽略 + 警告」模型 + `-main:`/`<StartupObject>` 语义，唯一入口点，其余合成入口点忽略并出诊断。此项应并入返工清单。
- **脚本运行时默认"编译到受管程序集 + source-gen 桥"**（决策文件 M5），解释模式显式 opt-in——使 immersive 脚本与 C# top-level 程序共享 AOT/trimming 友好形态；`Option Strict Off` 动态路径归入"按需动态"的兼容层。
- **识别跨语言合成类型名**：C# 文档声明合成 `Program` 类型可被名字引用；.vbx 若要消费 C# 生成的入口类型（或反之），VB 编译器须能解析跨语言的合成入口类型，并在调试器/Class View 中同时呈现 `_ImmersiveEntry` 与 `<Program>$`。
- **元数据认知提醒（弱相关，一句带过）**：unsafe-evolution 对 VB 的官方表态是 VB 无需支持 `requires-unsafe`（索引 T8，原文已核实）；对 top-level 无直接影响，但 VB 编译器须持续认识 C# 引入的新元数据属性（`RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute` 等），以免混编时误判。

### 对既有 RESOLUTION / 三态判定的影响

C# 生态现实**强化而非改变**原判定，无推翻项：
- **强化窄子集 `Consider`（返工后 `Active`）**：C# top-level statements 已证明"入口点合成 + 单文件排他"这条路可行且被市场接受；本提案与 C# 无元数据冲突，脚本市场压力被 C# 侧证。上调依据更充分。
- **强化 PROPOSAL C 否决（类生成）**：C# 的 `Program` 类方案附带"必须 partial、方法名实现相关、不可按名引用"的怪癖；VB 模块宿主回避之，反证 PROPOSAL A 是 VB 的正确形态。
- **强化全家桶 Table**：`.vbxhtml`/notebook 在 C# 生态对应工具级脚本指令而非语言语法，语言语法化会与"复杂度放文件外"的生态方向脱节——Table（附复活信号）判定在生态侧成立。
- **新增 2 项桥接 TO DO（并入返工清单）**：混编入口点仲裁；`args`/`await`/退出码语义定案。

### OPEN QUESTIONS（本附录新增）

- [ ] VB 沉浸式入口点是否/如何暴露命令行参数（`Sub Main(args() As String)`）——C# 恒有 `string[] args`（原文已核实），VB 未定。
- [ ] 混编程序集（C# top-level + VB immersive）的入口点优先规则与冲突诊断形式。
- [ ] .vbx 在 NativeAOT 下 `Option Strict Off` 动态路径的处理（桥接 vs 禁止）。
