# Visual Basic Language Design Meeting
August 8, 2026

## Agenda

* [Proposal - Module Enhancements（模块增强：泛型 / 嵌套 / `StandardModule`）](#proposal---module-enhancements模块增强泛型--嵌套--standardmodule)

## Proposal - Module Enhancements（模块增强：泛型 / 嵌套 / `StandardModule`）

_Related: [vblang 2014-02-10 – Strict Module](https://github.com/dotnet/vblang/blob/main/meetings/2014/LDM-2014-02-10.md)（主线已批准、从未落地的非提升模块方向）；vblang 2014-02-17 #20 – Partial Modules（已入主线）；交互建议：[top-level-code](../proposals/proposal-top-level-code.md)（合成模块宿主）、[extension-properties](../proposals/proposal-extension-properties.md)、[out-arguments](../proposals/proposal-out-arguments.md)（原文第 8 章同章特性）；Anthony 原文第 8 章 "General Modernization and Evolution II (Declarations)"_

### 场景与缺口

We opened with the classic module's defining trade. The spec says it plainly:

> A *standard module* is a type whose members are implicitly `Shared` and scoped to the declaration space of the standard module's containing namespace, rather than just to the standard module declaration itself. Standard modules may never be instantiated. It is an error to declare a variable of a standard module type.

That scoping is the whole appeal of `Module` in VB — a static utility class whose members you can call unqualified:

```vb
Module IOHelpers
    Public Function ReadAll(path As String) As String
        Return IO.File.ReadAllText(path)
    End Function
End Module

' 调用点无需限定：
Dim content = ReadAll("C:\data.txt")
```

But the same rule is the source of the two complaints this proposal targets. First, **namespace pollution**: every lifted member becomes an unqualified name in the containing namespace, so two modules in a namespace may define the same helper and every unqualified use becomes ambiguous (the spec's "two fully qualified names" rule at `types.md:699`):

```vb
Module Alpha
    Public Sub Log(message As String) : End Sub
End Module
Module Beta
    Public Sub Log(message As String) : End Sub
End Module

' 提升后，无前缀的 Log(...) 在命名空间内歧义 —— 正是污染的来源。
```

Second, **modules cannot be generic and cannot be nested** — the spec at `general-concepts.md:1447` says types "except for standard modules and enumerated types" may declare type parameters, and `types.md:728` says "A module may only be declared in a namespace and may not be nested in another type."

This is not a new observation. We pulled up the 2014-02-10 LDM, which was prompted by an MVP remark that aged well:

> I use Partial Private Sub New to achieve the same effect as in C# with static class. The advantage of this compared to Module is that static class does not "pollute" namespaces with it's members (and I also cannot have generic module anyway).

And the meeting recorded:

> There's a clear VB parity gap here, which has been asked-for many times on user-voice and connect.

That 2014 meeting **approved the concept** of a non-lifting module, named it "strict module" (as "the best of a bad bunch" among a dozen candidates), and resolved three questions that are directly relevant to today's proposal:

- **Generics**: "Strict modules CAN be generic; however generic modules CANNOT contain extension methods."
- **Nesting**: "Strict modules cannot be nested." (C# static classes can be nested; we said no anyway.)
- **Metadata**: "The metadata we emit for 'Strict Module' should be the same as the metadata for C# static classes."

We also noted that the feature was **never shipped and never revisited** — a search of the entire main-line meetings and proposals folder shows no mention of strict modules after 2014-02-10. The resolution was approved in principle and then left on the shelf.

What Anthony's proposal adds, and what makes it materially different from 2014, is the **polarity flip**: instead of a new opt-in kind of module, the proposal makes *non-lifting the default* and pushes classic behavior behind the `<StandardModule>` attribute. The entire design, in the original text (Chapter 8), is two comments plus two code blocks — roughly ten lines, all of it a sketch:

```vb
' Modules may be generic, nested, and won't hoist members
' into containing namespace by default.
Module Utils(Of T)
    ...
    Module Strings
        ...

' Classic behavior can be opted into with attribute.
<StandardModule>
Module IOHelpers
    ...
```

We think the motivation is real, and the 2014 resolution already tells us the concept is one we want. The question is whether Anthony's package — three orthogonal changes bundled with the default reversed — is the way to get it. Our answer, after a long session, is: **the package as written is not; but two of its three axes are worth rescuing.**

### 候选方案

We split the proposal along its three axes, because they do not stand or fall together, and because each has a different relationship to the 2014 resolutions.

**PROPOSAL A — 三合一（按建议原文）。** Modules become generic, nestable, and non-lifting by default; `<StandardModule>` opts back into classic lifting. This is the proposal as written.

**PROPOSAL B — 继承 2014 的 opt-in 严格模块。** Keep today's `Module` exactly as it is; add a separate non-lifting module kind ("strict module", or a modifier) that may be generic and may not be nested, with metadata matching C# static classes. This is the 2014 direction, zero breaking change, unimplemented.

**PROPOSAL C — 保留默认提升，只加泛型与嵌套。** No polarity flip. Generic modules simply never lift (a lifted member whose type parameter has no binding point is incoherent); nested modules lift to the outermost namespace, defined by rule. Classic modules keep lifting.

**PROPOSAL D — 现状 + `Shared Class` 替代。** Don't touch modules at all; the proposal's own Alternatives suggests a `Shared Class` + `Friend` type. We considered and rejected this in 2014 (VB has no static-class syntax; the lifting is what makes `Module` ergonomic), and nothing has changed.

**PROPOSAL E — 极性翻转但以 langversion 门控。** The A default flip applies only under a new language version; older versions keep lifting. This is a mitigation we considered seriously and ultimately rejected — more below.

### 权衡：Q&A

We walked the axes one at a time. The points below are the ones that actually bit.

**1. 极性翻转（A / E 的核心）值得吗？** This is the sharpest question, and it is where the proposal parts company with the 2014 design. Lifting is not a feature bolted onto modules; it *is* what a module is (spec `types.md:689`). Flipping the default means every existing `Module`, on recompile, silently stops exporting its members into the namespace. We do not have to imagine the failure mode: `ReadAll("C:\data.txt")` in the example above stops compiling, and every unqualified use of a module member across the codebase fails at once. The migration — mechanically adding `<StandardModule>` to each module — is possible, but any module missed *silently changes behavior* rather than erroring in a way the compiler can explain. Silent semantic change on upgrade is precisely the class of break we refuse. The proposal's Drawbacks section concedes this ("迁移成本与出错面不小") but does not quantify it. The Alternatives section does engage with an opt-in direction — "反向设计：默认提升，用特性关闭提升" — but dismisses it in a single clause, and it never references the 2014 resolution that already approved this exact concept, named it, and resolved its three sub-questions. We `Suspect` the author re-derived the question from scratch without the precedent on the shelf — and the polarity flip is exactly the axis on which the two designs contradict each other.

**2. A vs B：默认现代还是显式选择？** The strongest argument for A is product-identity: for VBScript.NET, where most modules are written new, "modern by default" is attractive and B's "explicitly choose the strict module" reads as ceremony. The proposal makes this exact point — opt-in means "新代码更少受益", because fresh code must remember the non-lifting form exists. That objection is real, and we want to record it fairly: a default is a kind of documentation, and A documents the modern behavior for everyone who writes `Module`. But the LDM's founding contract — "we will almost never make breaking changes" — is not waived for greenfield products, because the flip is not a *new* behavior, it is a *changed* behavior of existing syntax. B delivers the same end state for new code at zero break, and the authorial cost it imposes on new code is cheap: a one-time choice, surfaced by IntelliSense and IDE guidance rather than paid silently by every existing codebase. We also note the counterintuitive framing: 2014 spent real time deciding the strict module *must be a distinct kind* precisely so that "Module" keeps its meaning. A collapses that distinction by making one keyword mean two opposite things depending on an attribute. That violates the "avoid subtle semantic change" instinct (the `Return?` decision) more than it serves the modernization goal.

**3. E（langversion 门控）能救 A 吗？** We considered gating the flip behind a new langversion. It reduces the surface of "old code breaks," but it creates a worse problem: the *same source file* now means different things depending on the project's langversion. A library compiled at the old version lifts; the same file compiled at the new version does not. That is a fork in semantics keyed to a project setting — exactly what we rejected for other features — and it makes "what does this module export" unanswerable without knowing the build. Worse, upgrading a project to the new langversion flips every module at once, which is the silent-break problem again, just postponed. We are not convinced a gate makes the flip acceptable.

**4. 泛型模块：2014 说可以，但提升要排除。** The proposal bundles generics with the flip, but the two are separable. A *non-lifting* generic module is coherent and useful:

```vb
Module SortUtils(Of T)
    Public Function MergeSorted(left As T(), right As T()) As T()
        Dim result(left.Length + right.Length - 1) As T
        ' ...归并排序，省略实现
        Return result
    End Function
End Module

' 限定访问，类型参数显式给定：
Dim merged = SortUtils(Of Integer).MergeSorted(a, b)
```

This goes *beyond* C#, where static classes cannot declare type parameters — so there is no cross-language precedent and no "follow C#" default to lean on. But 2014 already resolved in favor ("Strict modules CAN be generic"), so the concept has a main-line pedigree. The critical exclusion is that a generic module must not be able to lift its members: a lifted member has an open type parameter, and the unqualified call site has no receiver to drive inference. We can imagine argument-driven inference (`MergeSorted(a, b)` inferring `T` from the arguments), but the proposal does not specify it, and we will not invent it here. **A generic module must be non-lifting, and cannot contain extension methods** (2014 resolution, consistent with spec `type-members.md:921`: the containing type must not be an open generic type).

**5. 嵌套模块：2014 说不行，新证据不足。** The proposal's nesting example is the thinnest part:

```vb
Module Utils
    Module Strings
        Public Function Quoted(value As String) As String
            Return """" & value & """"
        End Function
    End Module
End Module
```

Two problems. First, the hoisting question the proposal itself leaves open ("嵌套模块成员提升到哪一级？原文未说明"): if nested modules may lift, do their members lift to the parent module, to the outermost namespace, or both? Each answer has a failure mode — flattening into the namespace recreates the pollution the proposal is trying to remove; lifting into the parent only works if the parent itself is a lifting module, making lifting *transitive* and the visibility rules effectively unstateable. Second, 2014 explicitly resolved "Strict modules cannot be nested" with no recorded rationale beyond the hoisting entanglement, and the proposal offers no new use case or data that would overturn it. C# allows nested static classes, but that is a reason to study, not a reason to copy — the "follow C# unless compelling" rule does not extend to a feature we already considered and declined. **We are `Suspect` that nesting is an organizational convenience chasing a syntax, with the actual value coming from non-lifting + generics alone.**

**6. `<StandardModule>` 是复用好还是新特性好？** The proposal reuses the existing `Microsoft.VisualBasic.CompilerServices.StandardModuleAttribute`. Conceptually this is elegant: today the compiler *auto-emits* `[StandardModule]` onto every module (2014 IL notes: "VB modules are currently emitted as `[StandardModule] .class private auto ansi sealed`"), and consumers key lifting behavior off the attribute for imported types. Making the attribute the *source-level* contract — present = lift, absent = don't — is actually the cleaner design. But it forces the metadata question: a non-lifting module must be emitted *without* the attribute, which means the compiler stops auto-emitting it. That is fine for new binaries, and it is exactly the signal C# consumers and VB importers need ("if they come with [StandardModule] then we auto-open" — 2014). What it is not is compatible with the polarity flip, because the attribute's presence becomes the *only* marker separating two behaviors of the same keyword. Combined with question 1, the attribute story makes a good argument for B (a distinct kind) and a bad argument for A.

**7. 泛型模块 + 扩展方法 / 泛型扩展成员的区别。** We were careful to separate two things that look alike. A generic *method* inside a non-generic module is legal today and is what Anthony's own extension-properties example shows (`<Extension> ReadOnly Property SecondOrDefault(Of T)(...)` in `Module MyExtensions`, annotated "Note: Can be generic."). That is a method-level type parameter and is untouched by this proposal. A generic *module* is a type-level type parameter, which makes the containing type open-generic and, per 2014 and spec `type-members.md:921`, cannot host extension members. The proposal does not acknowledge this distinction, and the reader could easily conclude "generic modules can have extension methods" from the extension-properties chapter's "Can be generic" note. We flag it as a documentation trap.

**8. 与 top-level-code 的冲突面必须现在处理。** The immersive-files meeting already registered this as a TODO: the synthesized module host for top-level code (whether it lifts, and what member visibility defaults to) is decided in parallel, and the two documents do not acknowledge each other. If modules default to non-lifting, the immersive host should be non-lifting with `Friend`-default members — which the top-level-code note already `Suspect`ed. If modules keep lifting (B), the immersive host inherits classic semantics and every immersive file silently exports its helpers into the namespace — the pollution the immersive-files design was trying to avoid. Either way, this proposal and top-level-code must be specified against the same module semantics. We will not resolve one without the other.

### 深度追问：LDM 拷问清单

We then held the proposal against the full questioning list.

#### 1. 语法 / 文法歧义

`Module Utils(Of T)` — the `Of` type-parameter list is an existing shape on `Class`/`Structure`/`Interface` declarations, and `Module` simply does not accept it today. Extending the module declaration grammar is a contained change, and there is no genuine ambiguity with an expression: a declaration context is disjoint from a statement context. `Probably` clean. `Module Strings` nested inside a `Module` — nesting is grammar VB already has for types (a class may contain a class), but spec `types.md:728` forbids it for modules, so this is a real grammar-plus-binder change, not a default. `<StandardModule>` is an ordinary attribute block; no ambiguity. One genuine ambiguity candidate: inside a *generic* module, does a nested module see the parent's type parameters? The spec describes nested types inside generic *class* declarations doing implicit instance-type lookup (spec `types.md:1225`), but a module has no instance type. If `Module Strings` is nested inside `Utils(Of T)`, must it be written `Utils(Of Integer).Strings`? The proposal does not say, and we are not willing to guess. OPEN QUESTION.

#### 2. 角案例与边界语义

**Partial generic modules.** `Partial Module` is already in the language (2014-02-17 #20, "Approved. Already in Main."). A generic module that is also `Partial` requires the type parameter lists of all parts to agree, with a new consistency diagnostic — ordinary generic-partial machinery, but it must be specified. **Nested + partial** compounds it: partial nested modules across files, with the parent also partial. **Nested modules inside a generic parent** inherit the open-type-parameter problem. **Module-level `WithEvents` / `Handles`** — modules may have fields (a module can host `WithEvents` fields today; 2014 #20 allowed partial methods inside modules), and non-lifting changes visibility but not the field model; `Handles` binding is unaffected. **`Shadows` in nested modules** — shadowing across the nesting/hoisting boundary needs a rule. **`Shared Sub New` module initializers** — unchanged, but a *generic* module's shared constructor runs per constructed type, which is new behavior worth an explicit statement. **Constraints** — a generic module's type parameter may carry `As Structure`/`As Class`/`As New` constraints; we see no reason to forbid them, but the proposal is silent.

#### 3. 作用域与绑定

The semantic-model question drives everything downstream. For a non-lifting module, a member's scope is the module's declaration space and *nothing else*: `GetMembers()` on the containing namespace no longer returns lifted members, and the "two fully qualified names" rule (`types.md:699`) collapses to one. The IDE consequence is large: completion on the namespace no longer shows unqualified helpers, and `Find All References` for a helper that was previously unqualified now resolves only through the module name. For a *lifting* module (`<StandardModule>`), today's binding rules must be preserved verbatim, including the import-alias note (`source-files-and-namespaces.md:317`: declarations in a module do not introduce names into the containing declaration space). We regard "the binder keeps two different lookup paths for the two module kinds" as a required consequence, not a simplification to be avoided.

#### 4. 与既有特性的交互

- **Extension methods** — see Q&A 7: allowed in non-generic modules; the "standard module" host requirement (`type-members.md:908`) must be restated as "a module that is in scope" so that non-lifting modules can still host them when imported (`source-files-and-namespaces.md:518` permits importing standard modules).
- **`<HideModuleName>`** — 2014 resolved that IntelliSense ignores this attribute on strict modules. For a non-lifting module the name *is* the access path, so `HideModuleName` on a non-lifting module would make its members unreachable — it must either be an error or be ignored. Undecided, but it cannot be left to the consumer.
- **`Global` / `My.`** — `My` is namespace injection; a non-lifting module in `My`'s scope should not leak members into `My`. Interaction to be stated.
- **`CallerInfo` / `ParamArray`** — orthogonal, no interaction.
- **Late binding / Option Strict** — module member resolution is early-bound static resolution; a non-lifting module does not change late binding. No divergence expected (item 6).

#### 5. Breaking change 与兼容性

The proposal as written (A) is a **recompile-time breaking change for every existing module**, and the Drawbacks section understates it: the break is not "migration cost," it is *silent behavior change* for any module that does not get the attribute. We see no versioning trick (E) that fixes this. The metadata flip (stopping auto-emit of `[StandardModule]`) is a *new-binary* behavior and therefore not a break of existing binaries, but it is a change in what consumers of new binaries can rely on (C# static-import auto-open). A compatibility section is mandatory and is absent. This alone would put the feature at Reject under principle 1 if taken as A.

#### 6. Option Strict / 编译选项分叉

We see no new divergence: module member resolution does not involve the late binder, and `Option Strict On`/`Off` both resolve lifted and non-lifted members identically (the difference is whether the member is *reachable unqualified*, which is not an Option Strict question). The one honest caution: if generic modules are ever allowed to lift (Q&A 4), the two Option paths must agree on whether `T` inference from arguments is performed — we prefer "generic modules never lift" so this fork never opens.

#### 7. IDE / IntelliSense 影响

Non-lifting flips what completion shows on a namespace from "everything the modules in it export" to "the modules themselves." That is the *intended* pollution reduction, but it is a visible change in the IDE surface for every project that adopts it, and the proposal does not address completion, quick-info, rename, or the Class View representation of a nested module. The top-level-code synthesized host compounds this: the debugger and Class View must show `_ImmersiveEntry`-style synthesized modules whose members are `Friend` and non-lifted (if the immersive host is non-lifting). We regard the IDE story as part of the design, not an implementation detail.

#### 8. 数据 / 普遍性

The 2014 record gives us the qualitative signal ("asked-for many times on user-voice and connect"; the MVP quote) but no numbers. We have no data on how many VB codebases actually suffer namespace collisions from lifted modules, nor how many modules would be *generic* if they could be. For VBScript.NET specifically — a product where tool libraries and scripting helpers are the bread and butter — non-lifting modules and generic utilities are closer to core than to edge, which is why the *narrow subset* (B's direction) is attractive to us. But we will not dress up a product-positioning argument as usage data. `Suspect`: the polling complaint is real but unquantified; the generic-module demand is unproven.

#### 9. 更简替代

The strongest alternative is the 2014 design itself (B): same end state for new code, zero break. The proposal's own `Shared Class` alternative (D) we reject for the reasons recorded in 2014 (no VB static-class syntax; lifting is the ergonomics). An analyzer could flag namespace pollution and suggest `Friend` members — a legitimate stopgap that does not change the language, which we would happily ship as tooling while the design is pending. F# modules are not a template: VB has no `open`, and the "auto-open" comparison Anthony drew in 2014 ("VB's modules behave very much like F#'s modules when the AutoOpen attribute is applied") cuts the other way — it argues for making the *opt-in* explicit, not for flipping the default.

#### 10. 复杂度 / 成本 / 优先级

Hoisting is not a local feature; it is woven through name lookup (the spec's standard-module resolution rules at `general-concepts.md:1400–1433` appear in four places). Changing the default touches every one of those rules plus the binder's module-kind checks, the emitter (attribute auto-emit), and the IDE's completion/find-ref. That is a high-cost change. Generic non-lifting modules, in contrast, are a localized binder change (type parameters on a sealed abstract type, minus extension members) plus grammar — moderate. Nesting is medium (grammar + binder + access rules + hoisting interaction). Cost/value only balances for the generic non-lifting module; it does not balance for the flip or for nesting.

#### 11. 运行时 / CLR 硬约束

None blocking. A generic sealed abstract type is legal IL and passes PEVerify; nested modules are nested types; module initializers on a generic type run per constructed type (a design fact, not a constraint). The one hard CLR-adjacent rule is extension members: the containing type of an extension member must not be an open generic type (spec `type-members.md:921`), which matches 2014. No expression-tree involvement. The metadata contract (`[StandardModule]` presence) is a compiler-level concern, not a CLR one.

#### 12. 值不值得做

We scored it on value × cost × risk. Value: real for the narrow subset (non-lifting generic modules), modest for nesting, and the pollution-reduction goal is partly achievable at zero break via B. Cost: high for the flip and for nesting, moderate for the generic subset. Risk: the flip is a recompile-break with silent semantics (fatal under principle 1); nesting is 2014-rejected with no new evidence; the generic subset carries only the extension-method exclusion, which is known. **Net: the package as written is not worth doing; the generic non-lifting module is worth doing; nesting and the flip are not, on the evidence before us.**

### VB 基因对照

We then held the axes against the design principles and the main-line table.

- **原则 1（永不破坏现有代码）** — A **违反**：默认翻转 = 重编译破坏 + 静默语义变化。B **通过**。这是分区线。
- **原则 2（保持 VB-like）** — 半通过。`<StandardModule>` 是 VB6/`Microsoft.VisualBasic` 血脉，继承性强；但"一个关键字，两种相反语义，由特性切换"体感不统一。泛型模块与嵌套模块本身是干净的 VB 形态。
- **原则 3（不引入第二种做事方式）** — 反直觉地，**A 比 B 更违反这条**。B 增加一个"严格模块"种类，是 2014 明确接受的第二方式；A 不增加种类，却把既有 `Module` 的语义原地反转，等于把第一种方式改成了第二种——这比"加一个第二方式"更隐蔽。2018 年主线对"making a second way to do things"设了高门槛，但对"悄悄改变第一种方式"没有设门槛，而这正是更危险的那个。
- **原则 4（默认跟随 C#，除非有充分理由）** — 混合。C# static class 可嵌套、不可泛型。嵌套与 C# 一致但 2014 否决（有理由偏离，不需要再抄）；泛型模块超出 C#，无从跟随，需独立论证——2014 的决议是我们唯一的锚。
- **原则 5（读起来像英语、对新手友好）** — 中性。`SortUtils(Of Integer).MergeSorted(a, b)` 比 `MergeSorted(a, b)` 仪式多，但更明确；对新手，限定访问反而减少了"这个函数哪来的"的疑问。
- **原则 6（不为边缘场景加特性）** — 嵌套模块最接近边缘：纯组织性便利，无数据支撑，2014 已否决。泛型模块对工具库是核心场景。
- **原则 7（避免隐蔽的控制流/语义变化）** — A 正中此条雷区：同一源码、同一 `Module` 关键字，是否提升取决于项目里是否写了 `<StandardModule>`。这正是 `Return?` 被拒的同类理由。
- **原则 8（不与既有语法冲突）** — 通过。`Module ... Of`、嵌套 `Module`、`<StandardModule>` 均无与既有语法的解析冲突。
- **原则 9（消除常见样板）** — 目标的正当性在此：移除"命名空间被工具成员污染"这一高频痛点的样板（无谓的限定与命名规避）。
- **原则 10（冗长只在有用时是美德）** — 对 VBScript.NET，限定访问是有用的冗长（明确来源）；对老 VB 业务代码，它是负担。两条路线各自成立，这正是为什么默认值的选择必须保守。

对照主线表（2.3）：表中没有"模块"行，但 2014-02-10 的 strict module 决议是主线**已批准、未落地**的方向。本建议与主线的关系是**三分**：泛型部分与主线一致（2014 已批准）；嵌套部分**与主线决议冲突**（2014 明确否决）；极性翻转部分**与主线方向相反**（主线选择新增种类，而非反转既有语义）。这不是"主线保守、Anthony 激进"的通常张力，而是"Anthony 独立延伸且直接改写了主线已定的结论"——这个分量我们要如实记录。

### RESOLUTION

We split the proposal along its three axes and rule on each separately.

1. **极性翻转（默认不提升 + `<StandardModule>` 回退）— `Reject`。** 它是重编译破坏 + 静默语义变化，违反原则 1 与原则 7，且 `langversion` 门控（E）不能挽救——同一文件在不同版本下语义不同的代价比它救下的更大。非提升模块的价值由 **PROPOSAL B** 承载：继承 2014-02-10 的 opt-in"严格模块"方向（`RESOLUTION: yes, we should allow it` 仍然有效），新增一种非提升模块种类，零破坏地获得同样的现代组织方式。`<StandardModule>` 作为*新种类的属性契约*（而非*既有模块的语义开关*）复用是好的；把它当作*默认值开关*是坏的。

2. **泛型模块 — `Consider`，返工后可 `Active`（仅限非提升形态）。** 与 2014 决议一致：非提升模块可为泛型；泛型模块禁止含扩展成员；泛型模块**不得提升成员**（提升后类型参数在无接收者调用点无法绑定）。`Probably` 泛型模块的共享构造函数按构造类型运行；约束（`As Structure`/`As Class`）允许。此子集价值真实、成本中等、风险已知，是本建议中最值得抢救的部分。

3. **嵌套模块 — `Table`。** 2014 决议 "Strict modules cannot be nested" 未被新证据推翻；嵌套的提升归属（父模块 / 最外层命名空间 / 两者）无定义；价值未量化，接近边缘场景。复活信号：有人给出明确的组织性 use case 与提升归属规则。

4. **与 top-level-code 统一 — TO DO。** 合成沉浸式宿主的提升语义与成员可见性默认值，必须与本建议使用同一套模块语义（top-level-code 篇已登记此 TODO，我们在本决议中重申：两份建议的宿主语义同时定稿，不各自漂移）。

**Implication:**

- 起草一份独立的"非提升模块（strict module，名称待定）"speclet，以 2014-02-10 决议为基线，补齐：文法、绑定规则、`<StandardModule>`/元数据契约、`HideModuleName` 交互、扩展成员排除、IDE 行为。此 speclet 与本建议解耦。
- 泛型模块的最小原型：`Module SortUtils(Of T)` 非提升 + 限定访问 + 禁止扩展成员；验证语义模型（`GetMembers`、符号作用域）与 IDE 补全。
- 与 top-level-code 团队对表，确定合成宿主采用哪套模块语义，并交叉引用两份文档。
- 本建议（proposal-module-enhancements.md）作为"三轴打包"的原始形态留档；返工后应按本决议拆分。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：嵌套模块在泛型父模块内的限定与类型参数可见性（`Utils(Of Integer).Strings`？子模块是否隐式接收 `T`？）。
- `OPEN QUESTIONS`：非提升模块上 `<HideModuleName>` 是报错还是忽略（2014 决议对 strict module 是"IntelliSense 忽略"）。
- `OPEN QUESTIONS`：若未来允许泛型模块提升成员，`T` 是否由调用点实参推断（我们倾向"泛型模块永不提升"，此问不打开）。
- `OPEN QUESTIONS`：非提升模块可否含 `WithEvents` 字段与 `Handles` 处理器（`Probably` 可以，需明文）。
- `TODO`：量化"命名空间污染"与"模块需要泛型"的真实占比，为数据/普遍性补证据。
- `TODO`：兼容性小节（重编译破坏面、`[StandardModule]` 停发对消费方的影响、`langversion` 分叉表）。
- `Follow-up`：确认 strict module 在 2014 后从未进入主线提案文件夹（我们检索的结果如此，标注为可复核事实）。

### 状态

- **LDM 状态：Table（整体）**；泛型 + 非提升窄子集 `Consider`（返工后可 `Active`）；极性翻转 `Reject`；嵌套模块 `Table`。
- **三态判定：Table** — 价值存在于窄子集，但建议原文作为规范文本未达成；按本决议拆分并返工后，泛型非提升模块可独立升为 `Active`。

---

## 附录：特性评价

### 评价对象

- 建议：`proposal-module-enhancements.md` — 模块增强（泛型 / 嵌套 / `StandardModule`）。
- 来源：Anthony 原文第 8 章 "General Modernization and Evolution II (Declarations)"，共 3 行注释 + 2 段代码（约 10 行全部为草图）：`Module Utils(Of T)` 嵌套 `Module Strings`；`<StandardModule> Module IOHelpers`。
- 配方目标：让模块"更现代"——可泛型、可嵌套，且默认不把成员提升到包含命名空间，经典提升行为以 `<StandardModule>` 显式回退。

### 五维评分表

| 维度 | 得分 | 评价（对照行为锚点） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | **2/5** | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。Motivation 给出痛点的定性描述（污染、组织弱）但无强度、无数据；示例体均为 `...` 占位，不能编译、不能演示特性；核心语义（提升归属、泛型+提升关系、`<StandardModule>` 元数据）未定 ≥4 项 → 效果证据封顶 | 已提供 / 已检查 | 无原型、无量化、"改进"不可验证；示例不可运行是硬伤 |
| 特性 | **2/5** | 锚点 2："多个强无关能力捆绑"。三轴（泛型 / 嵌套 / 极性翻转）是三个独立特性被打包进一份建议，边界模糊；`<StandardModule>` 血脉继承强，但极性翻转与"兼容/宽松"基因直接冲突；泛型模块超出 C#（C# static class 不可泛型）却未论证 | 已检查 | 捆绑；极性翻转违反原则 1/7；与 C# 的偏离（超出）无论证 |
| 品质 | **3/5** | 锚点 3："缺某一章节或在关键处边界含糊"。六章节模板齐全、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design 仅 4 行 + 2 代码块，无文法、无语义、无边界、无兼容性章节；状态行占位链接（`PROTOTYPE_OWNER/roslyn/BRANCH_NAME`、`pr/1`）；Drawbacks 未量化迁移成本 | 已检查 | 红旗：状态占位链接；设计深度近乎为零；嵌套提升归属、泛型+扩展成员、`<StandardModule>` 元数据均未写 |
| 属性 | **2/5** | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。水/光正向（现代组织、盘活 VB6 `StandardModule` 资产）；但暗风险突出：重编译破坏、静默语义变化、`[StandardModule]` 停发的消费方影响、与 top-level-code 合成宿主冲突、三轴纠缠——文档无任何对冲设计 | 已检查（预测性，标"待定"） | 破坏性变更无分析；与主线 2014 决议的极性冲突未识别；"同一关键字两种相反语义"无缓解 |
| 炼金成分 | **3/5** | 锚点 3："部分来源未标注"。Anthony 第 8 章来源标注准确；但未标注与 2014-02-10 strict module 决议的关系（极性反转 + 嵌套否决 + 泛型批准全部命中共三条决议）；`<StandardModule>` 继承 VB6/`Microsoft.VisualBasic` 未点明；"组织方式"借鉴 C# static class 未点明 | 已检查 | 与主线决议的关系完全缺席是最大的成分缺失；无杂质但无追溯 |

### 设计原则对照

- **与 VB 基因：偏离**。唯一强一致点是 `<StandardModule>` 的 VB6 血脉（原则 2 半通过）与消除样板的目标正当（原则 9）。核心偏离在原则 1（默认翻转 = 重编译破坏）与原则 7（同一关键字按特性切换相反语义，隐蔽语义变化）。
- **与主线关系：Anthony 独立延伸，且部分与主线冲突**。主线无"模块增强"行；2014-02-10 strict module 是主线**已批准未落地**方向。泛型部分与主线一致（2014 已批准）；嵌套部分与主线冲突（2014 明确否决）；极性翻转部分与主线方向相反（主线选择新增种类而非反转语义）。
- **破坏性变更：有**。默认翻转 = 全部既有模块重编译破坏 + 静默语义变化；`[StandardModule]` 停发 = 新二进制消费方行为变化（C# static import auto-open）。无 langversion 门控、无迁移工具、无兼容性分析。

### 总评

- **达成程度：未达成**（作为规范文本）。动机成立、方向有 2014 先例支撑，但三轴打包、核心语义反转、关键边界未定义，作为可实现的规范不成立。
- **LDM 三态建议：Table（整体）**。拆分后：泛型 + 非提升窄子集返工后可 `Consider`/`Active`（对 VBScript.NET 值得优先孵化）；极性翻转 `Reject`；嵌套模块 `Table`。
- **主要问题**：① 默认翻转的破坏性未量化且无对冲，违反原则 1/7；② 三轴纠缠，嵌套提升归属未定义；③ 泛型模块与提升/扩展成员的关系未定义（2014 决议未引用）；④ 未与 2014 strict module 决议对话，极性反转直接改写主线结论；⑤ 无原型、无文法、无兼容性小节，示例不可编译。

### 返工建议

- **拆分**：拆为三份独立建议——(a) 非提升模块（strict module，以 2014-02-10 决议为基线）；(b) 泛型模块（仅非提升形态，禁止扩展成员）；(c) 嵌套模块（当前证据不足，`Table`）。不再三轴捆绑。
- **补充章节**：文法（`Module ... Of`、嵌套 `Module`、类型参数列表一致性）；绑定规则（非提升成员的符号作用域、命名空间 `GetMembers` 变化）；`<StandardModule>` 元数据契约（编译器是否停发 `[StandardModule]`、对消费方的影响）；兼容性小节（重编译破坏面、`langversion` 分叉表、迁移工具）；IDE 小节（补全、Find All References、Class View、top-level-code 合成宿主）。
- **补充证据**：最小原型（非提升泛型模块）；命名空间污染 / 泛型模块需求的量化数据；`HideModuleName`、`WithEvents`、`Shared Sub New`（泛型构造类型）等角案例的逐条验证。
- **未决问题处理**：嵌套提升归属（倾向"非提升模块不提升，嵌套模块属于父模块作用域，不跨级提升"）；泛型模块永不提升（关闭"实参推断 T"之问）；`<StandardModule>` + `(Of T)` 组合（倾向报错：提升与泛型互斥）。
- **主线对齐**：在返工建议中显式引用 2014-02-10 的三条决议（泛型批准 / 扩展成员排除 / 嵌套否决），逐条说明沿用或推翻及理由。

---

## 附录：C# 生态与互操作考量

> 本附录基于 `..\..\csharplang`（dotnet/csharplang main 分支镜像）核实。所有 C# 原文逐字引用并标注来源（路径相对 csharplang）；无法在本仓库核实处标注 **OPEN QUESTIONS**。
> 结论摘要：本提案三轴在 C# 生态的对应物**全部是已完成特性**（`[ModuleInitializer]` C# 9、top-level statements C# 9、file-local types C# 11、extensions C# 14），C# 很早就用「静态容器 + 显式作用域控制」回答了 VB `Module` 想回答的问题。附录不推翻既有 RESOLUTION，但给出一处论据修正（`[StandardModule]` 对 C# 不透明）与一处新边界（泛型模块 `Shared Sub New` 是 VB-only 语义）。

### 相关 C# 现实方向

**C#-A：模块初始化器 `[ModuleInitializer]`（C# 9）。** C# 对「程序集加载期一次性初始化」的答案是**方法级属性**，而非类型级语法。`proposals\csharp-9.0\module-initializers.md`（Summary）：

> Although the .NET platform has a feature that directly supports writing initialization code for the assembly (technically, the module), it is not exposed in C#.

其 Motivation 第一条：

> Enable libraries to do eager, one-time initialization when loaded, with minimal overhead and without the user needing to explicitly call anything

对包含类型的约束（Detailed design，requirement 4）与本提案的泛型模块直接相关：

> The method must not be generic or be contained in a generic type.

LDM-2020-04-08 结论（`meetings\2020\LDM-2020-04-08.md`）：

> Let's let any static method be a module initializer, and mark that method using a well-known attribute. We'll also allow multiple module initializer methods, and they will each be called in a reserved, but deterministic order.

**C#-B：顶层语句（C# 9）→ 合成 `Program` 类。** C# 为「无样板入口」合成的宿主是 `partial class Program`，**不是**静态容器，且其成员不提升到全局命名空间。`proposals\csharp-9.0\top-level-statements.md`（Summary）：

> The semantics are that if such a sequence of *statements* is present, the following type declaration, modulo the actual method name, would be emitted:
> ```csharp
> partial class Program
> {
>     static async Task Main(string[] args)
>     {
>         // statements
>     }
> }
> ```

（Semantics）：

> The type is named "Program", so can be referenced by name from source code. It is a partial type, so a type named "Program" in source code must also be declared as partial.

顶层局部变量/局部函数对整个程序 simple-name 可见，但在顶层语句之外访问即报错——即**名字域限定在合成方法内，不成为命名空间成员**。

**C#-C：file-local types（C# 11）。** C# 对「实现细节污染共享命名空间」的答案是 **per-file 可见性修饰符**，而非翻转任何既有语义。`proposals\csharp-11.0\file-local-types.md`（Summary）：

> Permit a `file` modifier on top-level type declarations. The type only exists in the file where it is declared.

其动机（Motivation）恰是 source generators 需要不撞名的私有实现：

> Our primary motivation is from source generators. Source generators work by adding files to the user's compilation.
> 1. Those files should be able to contain implementation details which are hidden from the rest of the compilation, yet are usable throughout the file they are declared in.
> 2. We want to reduce the need for generators to "search" for type names which won't collide with declarations in user code or code from other generators.

生成名对消费者不可依赖（Attributes 节）：

> This means detecting the presence of a file-local type by a hard-coded string name is likely to be impractical, because it requires depending on the internal name generation strategy of the compiler, which may change over time.

**C#-D：静态类与扩展成员容器。** C# static class 可嵌套、不可泛型、永不提升。C# 对扩展成员的容器约束——即便在 C# 14 全新 extensions 设计中**仍未放松**（`proposals\csharp-14.0\extensions.md`）：

> Extension declarations shall only be declared in non-generic, non-nested static classes.

以及：

> Extensions are declared inside top-level non-generic static classes, just like extension methods today, and can thus coexist with classic extension methods and non-extension static members:

**C#-E：`[StandardModule]` 对 C# 完全不透明（已核实缺席）。** 我们对 csharplang 全仓库 Grep `StandardModule` / `VB module` / `auto-open`：**零命中**。C# 消费方把 VB `Module` 视为普通 `sealed abstract` 静态类；`[StandardModule]` 属性对 C# 无意义，「auto-open」只是 VB 导入方之间的契约（2014 决议），C# 侧无对应物。

### 现实 vs 提案

| 提案轴 | C# 现实 | 判定 | 理由 |
|---|---|---|---|
| **极性翻转**（默认不提升 + `<StandardModule>` 回退） | C# 静态类永不提升；污染用 file-local types / 作用域控制解决（C#-C）；无「翻转默认」先例 | **脱节 + 冲突** | 脱节：C# 生态不存在「提升」这一默认语义，无「翻转默认」的参照系；冲突：停发 `[StandardModule]` 改变的是 **VB 导入方契约**（2014 元数据决议）——而 C#-E 说明该属性对 C# 消费方本就不透明，翻转的破坏面纯在 VB 侧，且无 C# 互操作收益可对冲 |
| **泛型模块** | C# 静态类不可泛型（C#-D）；`[ModuleInitializer]` 明令禁止泛型包含类型（C#-A）；扩展容器必须非泛型非嵌套（C#-D） | **兼容 + 需桥接** | CLR 元数据层兼容（泛型 sealed abstract 类型合法 IL，PEVerify 通过，本决议第 11 条已确认）；「泛型模块禁止扩展成员」与 C# 完全同向——两条语言在同一约束上汇合；桥接点：泛型模块 `Shared Sub New` 按构造类型运行，C#/CLR 无「泛型模块初始化器」概念，C# 消费方无法获得等价加载期语义 |
| **嵌套模块** | C# 静态类可嵌套（C#-D） | **脱节** | C# 先例存在，但 C# 无提升概念，嵌套静态类不存在「成员提升到哪一级」的纠缠；C# 先例对 VB 特有的提升归属问题**无答案**，不足以推翻 2014 否决 |
| **top-level-code 合成宿主** | C# 顶层语句合成 `partial class Program`，成员不提升、限定在合成方法内（C#-B） | **需桥接（支持 RESOLUTION #4）** | C# 先例明确「新宿主 = 合成类型 + 成员不泄漏到命名空间」；若 VB 合成宿主是 module 且 module 默认提升（B 路线），沉浸式文件会静默导出助手——C# 的 `Program` 模型是「宿主非提升」的直接佐证 |

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态**：C# 生态的「默认」方向是静态容器永不提升 + 显式作用域控制（file-local 用 per-file 修饰符，C# `using static` 是 C# 6 起的唯一「显式带入静态成员」通道——又一个「显式优于默认」的先例）。VBScript.NET 应据此沿用 RESOLUTION #1 的 B 路线（opt-in strict module）：非提升模块在 C# 侧恰好就是普通静态类，**零桥接成本**。动态/晚期绑定（`Any`）作为按需出口保留，与静态模块持有者分开。
2. **source-gen 桥**：C# 9 source generators + C# 11 file-local types 的合流方向是「生成器产物 = 不污染命名空间的实现细节」。VBScript.NET 的非提升模块 / 合成宿主应复用同一思路：让编译器与生成器把助手落到非提升模块（= C# 静态类），必要时吸收 per-file 可见性（`file` 修饰符思想）作组织层。
3. **识别新元数据**：VBScript.NET 编译器须能消费三类 C# 元数据——(a) `[ModuleInitializer]` 方法：含此类方法的 C# 程序集在加载期急切执行，VB 侧调用其成员不触发该语义差异，但需在文档/诊断中说明 VB `Shared Sub New`（惰性、按类型 `.cctor`）与之**不等价**；(b) file-local 类型：生成名 unspeakable 且实现可变，VB 导入方**不得硬编码名称**（C#-C 引文）；(c) C# 14 extension 容器（非泛型静态类，`[Extension]`）与 VB 扩展成员宿主规则（`type-members.md:908`）协调。
4. **急切初始化的显式出口**：若 VBScript.NET 需要「程序集加载期一次性初始化」，应显式支持/映射 C# 的 `[ModuleInitializer]`（互操作机制），而非依赖 `Shared Sub New`。泛型模块永不提升（RESOLUTION #2）恰好规避了「泛型包含类型不能挂模块初始化器」的 C# 冲突——这是一个幸运的巧合，应在 speclet 中写实。

### 对既有 RESOLUTION / 三态判定的影响

本附录**不推翻既有判定**，提供三点 C# 侧佐证与一处论据修正：

- **强化 RESOLUTION #1（极性翻转 Reject）**：C# 现实（C#-C、C#-D）显示污染问题由作用域/显式通道控制解决，无需翻转默认；且 C#-E 说明翻转的破坏面纯在 VB 侧，无 C# 互操作收益。
- **强化 RESOLUTION #2（泛型模块 Consider，非提升形态）**：C#-D 证明「扩展排除」是两条语言的共同约束；但揭示一个新边界——泛型模块 `Shared Sub New` 按构造类型运行是 **VB-only** 语义，speclet 需明文声明其与 C# `[ModuleInitializer]`（禁泛型包含类型）互斥。
- **修正一处论证**：A 派可能主张「C# 消费方依赖 `[StandardModule]` 做 auto-open」。C#-E 已核实该属性对 C# 不透明，此论据**不成立**；破坏面应如实描述为 VB 导入方 / VB6 遗产工具。
- **支持 RESOLUTION #4（top-level-code 统一）**：C# `Program` 合成类不提升（C#-B），是「宿主非提升」的直接先例。

新增 **OPEN QUESTIONS**：

- `OPEN QUESTION`：泛型模块 `Shared Sub New` 的按构造类型运行语义，是否应在 speclet 中声明为 VB-only 并排除 C# 互操作承诺（C# `[ModuleInitializer]` 禁止泛型包含类型，C# 消费方无法获得等价加载期初始化）。
- `OPEN QUESTION`：VBScript.NET 是否提供 `[ModuleInitializer]` 作为 VB `Shared Sub New` 之外的急切初始化出口（涉及编译器发不发 `<Module>` 级初始化器）。
- `OPEN QUESTION`：VB 导入含 file-local 类型的 C# 程序集时，除正常成员查找外是否还有安全引用路径（生成名不可依赖）。

### 引用来源清单（全部逐字核实）

| 原文 | 来源 |
|---|---|
| "Although the .NET platform has a feature that directly supports writing initialization code for the assembly (technically, the module), it is not exposed in C#." | `proposals\csharp-9.0\module-initializers.md`（Summary） |
| "Enable libraries to do eager, one-time initialization when loaded, with minimal overhead and without the user needing to explicitly call anything" | `proposals\csharp-9.0\module-initializers.md`（Motivation） |
| "The method must not be generic or be contained in a generic type." | `proposals\csharp-9.0\module-initializers.md`（Detailed design，requirement 4） |
| "When one or more valid methods with this attribute are found in a compilation, the compiler will emit a module initializer which calls each of the attributed methods. The calls will be emitted in a reserved, but deterministic order." | `proposals\csharp-9.0\module-initializers.md`（Detailed design） |
| "Let's let any static method be a module initializer, and mark that method using a well-known attribute. We'll also allow multiple module initializer methods, and they will each be called in a reserved, but deterministic order." | `meetings\2020\LDM-2020-04-08.md`（Conclusion） |
| "The semantics are that if such a sequence of *statements* is present, the following type declaration, modulo the actual method name, would be emitted:" + `partial class Program { static async Task Main(string[] args) {...} }` | `proposals\csharp-9.0\top-level-statements.md`（Summary） |
| "The type is named 'Program', so can be referenced by name from source code. It is a partial type, so a type named 'Program' in source code must also be declared as partial." | `proposals\csharp-9.0\top-level-statements.md`（Semantics） |
| "The primary goal of the feature therefore is to allow C# programs without unnecessary boilerplate around them, for the sake of learners and the clarity of code." | `proposals\csharp-9.0\top-level-statements.md`（Motivation） |
| "Permit a `file` modifier on top-level type declarations. The type only exists in the file where it is declared." | `proposals\csharp-11.0\file-local-types.md`（Summary） |
| "Our primary motivation is from source generators. Source generators work by adding files to the user's compilation. … We want to reduce the need for generators to 'search' for type names which won't collide with declarations in user code or code from other generators." | `proposals\csharp-11.0\file-local-types.md`（Motivation） |
| "This means detecting the presence of a file-local type by a hard-coded string name is likely to be impractical, because it requires depending on the internal name generation strategy of the compiler, which may change over time." | `proposals\csharp-11.0\file-local-types.md`（Attributes） |
| "Extension declarations shall only be declared in non-generic, non-nested static classes." | `proposals\csharp-14.0\extensions.md` |
| "Extensions are declared inside top-level non-generic static classes, just like extension methods today, and can thus coexist with classic extension methods and non-extension static members:" | `proposals\csharp-14.0\extensions.md` |

**已核实缺席**：`StandardModule` / `VB module` / `auto-open` 在 csharplang 全仓库 Grep **零命中**（C#-E）。

**OPEN QUESTIONS**：见上节。
