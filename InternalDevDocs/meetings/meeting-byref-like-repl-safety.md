# Visual Basic Language Design Meeting
August 12, 2026

本次会议是**前置议题**：不评估某个既有 proposal，而是回到产品源码，把「byref-like 类型（C# `ref struct`，VB 称 restricted type）在 REPL/脚本环境下的安全性」盘清楚，为 `tasks\p1-immediate.md` 的**前置-1（D1：移植 RefStructHelper + 编译器层 suppress ref struct obsolete error）**定 REPL 侧语义契约。为什么现在谈：D1 一旦落地，byref-like 在 VB 里第一次真正可用，REPL 是用户第一个撞上的地方——`Span(Of Integer)` 会立刻出现在 `Dim`、`?` 打印、顶层 `Await` 三种写法里。如果安全性不先定清楚，D1 落地后 REPL 会以「运行期 InvalidProgramException 或费解的编译错误」回敬用户。

## Agenda

* [现状盘点：REPL submission 机制与 byref-like 的三个碰撞点（源码核实）](#现状盘点repl-submission-机制与-byref-like-的三个碰撞点源码核实)
* [借鉴：C# ref-like 安全规则 与 CSX 持久化冲突](#借鉴c-ref-like-安全规则-与-csx-持久化冲突)
* [议题讨论：byref-like 在 REPL 的安全边界（候选方案）](#议题讨论byref-like-在-repl-的安全边界候选方案)
* [RESOLUTION（本次会议汇总）](#resolution本次会议汇总)
* [附录：C# 生态与互操作考量](#附录c-生态与互操作考量)

## 现状盘点：REPL submission 机制与 byref-like 的三个碰撞点（源码核实）

先把产品侧的两块事实摊开：REPL 把一次提交编译成什么，以及 byref-like 目前被编译器怎么对待。每条标注 `文件:行号`。

### REPL submission 机制（编译器层）

- **提交 = 一个脚本类 + 一个脚本初始化方法。** 顶层代码进入脚本类（`Submission#N`），主体是合成的 `<Initialize>` 方法（`SynthesizedInteractiveInitializerMethod`，`Compilers\VisualBasic\Portable\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:12-15`）。
- **`<Initialize>` 是 async 方法。** `IsAsync = True`（`:51-55`）——即使提交里没有 `Await`，脚本初始化器也是 async 形状。
- **提交的返回类型默认是 `Object`。** `CalculateReturnType`：未显式指定 `ScriptCompilationInfo.ReturnTypeOpt` 时取 `System.Object`（`:166-170`）。
- **顶层 `Dim`/`Const` 是脚本类的字段，不是方法局部。** 模块级 `DimKeyword` 走 `ParseSpecifierDeclaration`（`Compilers\VisualBasic\Portable\Parser\Parser.vb:714`）；顶层字段初始化绑定走 `TopLevelCodeBinder`（`Compilers\VisualBasic\Portable\Binding\BinderBuilder.vb:436` 的 `IsScriptClass` 分支）。**这是跨提交持久化的机制**：`Dim x = 5` 后下一条提交仍能看到 `x`。
- **末尾表达式被隐式转换为提交返回类型（Object）并作为结果返回。** `Binder_Initializers.vb:211-220`（「insert an implicit conversion to the submission return type」），落地在 `Analysis\InitializerRewriter.vb:202-243`：末尾非 Void 的表达式语句 → `submissionResult` → 转换后 `BoundReturnStatement` 返回。**这是一个装箱点**。

### REPL 宿主层（打印路径）

- 交互循环 `RunInteractiveLoopAsync`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:224`）→ `BuildAndRunAsync`（`:296`）→ 编译通过 → 运行 → `newScript.HasReturnValue()` 为真 → `globals.Print(state.ReturnValue)`（`:313-315`）。
- `state.ReturnValue` 是 `object`（`ScriptState(Of Object)`）；`HasSubmissionResult`（`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:816`）判定提交末尾是否有结果。
- 打印走 `ObjectFormatter`（反射格式化对象）。**byref-like 若成为结果，既装不进 `object`，也反射不了。**

### byref-like 目前被编译器怎么对待（现状）

- **VB 编译器没有 ref-like 概念。** `ITypeSymbol.IsRefLikeType` 硬编码 `False`，注释「VB has no concept of ref-like types」（`Compilers\VisualBasic\Portable\Symbols\TypeSymbol.vb:587-590`）。
- **受限类型分析只覆盖三个特殊类型**：`TypedReference` / `ArgIterator` / `RuntimeArgumentHandle`（`Symbols\SpecialTypeExtensions.vb:84-93` 的 `IsRestrictedType`）。`Span(Of Integer)` 这类任意 ref struct **不在覆盖内**——装箱到 Object、作字段、跨 Await 存活目前编译器都不拦。
- **没有「suppress ref struct obsolete error」。** C# 侧对 ref-like 类型在元数据里的 `[Obsolete]` 做了过滤（`PENamedTypeSymbol.cs:998`：`filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`），VB 没有对应逻辑 → 今天在 VB 里直接用 `Span(Of Integer)` 会撞元数据上的 obsolete 错误。
- **`G:\Projects\RefStructHelper` 是独立分析器**，把这套规则扩展到所有 ref struct：BCX31394（转 Object/ValueType）、BCX31396（Nullable / 泛型类型实参）、BCX32061（受限/特殊类型作泛型约束）、BCX36598（LINQ）、BCX36640（lambda）、BCX37052（async/iterator）、BCX31393（继承实例方法装箱）；未来项含 scoped/unscoped 返回流分析、`allows ref struct` 约束（README.md）。

### 三个碰撞点

把 REPL 机制与 byref-like 规则相交，得到三个必然碰撞点：

1. **持久化（字段）**：顶层 `Dim s As New Span(Of Integer)(1)` → 脚本类字段。ref-like 不能是类的字段（见下 C# 借鉴）。今天：obsolete 错误（suppress 未做）；D1 之后：若不处理，要么 InvalidProgramException，要么必须给清晰编译错误。
2. **结果装箱（打印）**：`? New Span(Of Integer)(1)` / 末尾裸表达式 → 隐式转换到 `Object`（装箱）。ref-like 不可装箱。今天：obsolete 错误或（suppress 后）静默产坏 IL → 运行期 InvalidProgramException。
3. **异步状态机**：`<Initialize>` 恒为 async，顶层 `Await` + 作用域内 byref-like → 状态机装箱（RefStructHelper BCX37052 / 编译器 `ERR_CannotLiftRestrictedTypeResumable1`）。C# 规则本就禁止（见下）。

## 借鉴：C# ref-like 安全规则 与 CSX 持久化冲突

C# 侧已有完整答案，逐字核实如下（`..\csharplang` 镜像）。

- **ref-like 必须仅存栈上**：
  > "The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack." → `csharplang\proposals\csharp-7.2\span-safety.md:5`
- **ref-like 不能作字段、不能装箱、不能跨 await/yield**：
  > "A `ref struct` type may not be the declared type of a field, except that it may be the declared type of an instance field of another `ref struct`." → `span-safety.md:266`
  > "A value of a `ref struct` type may not be boxed: There is no conversion from a `ref struct` type to the type `object` or the type `System.ValueType`…" → `span-safety.md:270-271`
  > "Neither a ref local, nor a local of a `ref struct` type may be in scope at the point of a `yield return` statement or an `await` expression." → `span-safety.md:262`
- **CSX 的持久化模型与 ref-like 冲突（“第三种方言”担忧的同源）**：
  > "CSX is designed to allow all values to be persisted, which is important for the scripting 'submission' system, but this makes a number of types of statements illegal that we have support for in the current design, like ref locals." → `csharplang\meetings\2020\LDM-2020-02-26.md:49-52`
  > "…since the semantics of this design have subtle differences from CSX this would effectively create a third dialect of C#." → 同文件 `:46-48`
- **C# 脚本顶层变量 = 字段**（持久化的机制）：C# 解析器在全局脚本层把 local-decls/local-funcs 解析为脚本作用域的字段/方法——
  > "if we're at the global script level, then we don't support local-decls or local-funcs. The caller instead will look for those and parse them as fields/methods in the global script scope." → `csharplang\Portable\Parser\LanguageParser.cs:8452-8454`
  后果：顶层 `var s = new Span<int>(1);` 会成为字段 → 撞 ref-like 不能作字段（C# 错误码 `ERR_FieldAutoPropCantBeByRefLike`，`ErrorCode.cs:1533`）。

**对 VBScript.NET 的含义**：VB REPL 就是 VB 的交互方言，与 CSX 同构——顶层变量持久化（脚本类字段）与 byref-like 栈约束天然冲突。C# 的选择是：**脚本/交互顶层一律禁止**（ref local 与 ref struct 顶层变量都非法）。VBScript.NET 的 REPL 与 .vbx 脚本模式共用 `Script` kind（`SourceCodeKind.Interactive` 已废弃，两者同为 Script，见 `meeting-optional-question-prefix.md` 已核实事实），所以这个「顶层禁止」同时覆盖 REPL 与脚本文件，语义必须一致。

## 议题讨论：byref-like 在 REPL 的安全边界（候选方案）

### 场景与缺口

D1 落地后（移植 RefStructHelper + suppress obsolete error），`Span(Of Integer)` 等 ref-like 在 VB 里第一次可用。REPL/脚本是入口，三种写法立即撞上边界：顶层 `Dim`（字段持久化）、`?`/末尾表达式（结果装箱）、顶层 `Await`（async 状态机）。缺的是一份「哪些写法合法、哪些报什么错」的语义契约——否则 D1 的价值在 REPL 上兑现不了。

### 候选方案

**PROPOSAL A — 顶层全禁（对齐 C# CSX）。** 顶层 byref-like 变量、byref-like 结果、跨 Await 一律编译错误；byref-like 只在方法体内（局部、参数、返回值、`allows ref struct` 接口消费）可用。最安全、语义最简单、与 C# 完全同向；代价是 REPL 交互层不能用一行 `Dim s = ...AsSpan()` 就持有一个 span 继续玩。

**PROPOSAL B — 提交内局部。** 把顶层 byref-like `Dim` 解析为「提交生命周期的局部」而非字段——不跨提交持久化，但在同一提交内可用。**否决（一看就不通）**：顶层变量在脚本里的价值就是与提交方法共享（字段语义）；B 把它降为 `<Initialize>` 局部后，同提交方法访问不了它（想传就得 ByRef 传 ref-struct 参数，而 `ByRef` ref-struct 参数本身已定稿非法 → BCX31396），等于「既不能持久、方法也用不上」——与直接写在方法体内等价，却多一套「顶层局部 vs 字段」的双轨解析模型，无独立价值。

**PROPOSAL C — byref-like 结果特殊打印。** 不报错，REPL 对 byref-like 结果打印类型描述（如 `Span(Of Integer) (ref struct)`）。**否决**：值本身绑定栈上、不可持久，打印一个无法引用的类型名对交互没有增量价值；且要为「可打印但不可用」单独开打印通道，与「修错不算回归」（D4）的精神相反——把错误悄悄降级为花哨输出。

### 权衡：Q&A

- **A vs B：B 不通，弃。** B 的「提交内局部」两头落空：不持久（无法跨提交用）+ 同提交方法访问不了（降为 `<Initialize>` 局部，Helper 方法看不到；想传就得 ByRef 传 ref-struct 参数，而 `ByRef` ref-struct 参数已定稿非法 BCX31396）。结果 B 与「直接写在方法体内」等价，还引入「顶层局部 vs 字段」双轨模型，**无独立价值 → 否决 B**；A 对齐 C# CSX（ref local/顶层 ref struct 非法，LDM-2020-02-26 承认这是持久化模型的结果），零新模型，v1 及后续都走 A。
- **D1 的目标场景不受 A 影响。** D1 解锁的是「消费 C#13 `allows ref struct` 接口类型」——这在**方法体/局部**里进行（`Dim x As IOf(Of Integer) = ...`、作为参数传入），不是顶层持久化。A 禁的是顶层持久化与结果装箱，两者不冲突。
- **`.vbx` 脚本文件与 REPL 同 kind。** `SourceCodeKind.Interactive` 已废弃，REPL 与 `.vbx` 都走 `Script`。A 的「顶层禁」同时约束两者，避免「REPL 禁、脚本不禁」的分裂。C# 对 csx 接受了同一件事（LDM-2020-04-15「we are ok with this remaining distance」同源判断）。
- **修错不算回归（D4）。** 今天 `Span` 用法要么 obsolete 错误、要么（suppress 后）运行期 InvalidProgramException；A 把它变成**正确编译错误** = 修错，不构成回归，不影响 P1 地位。
- **与退出码/打印正交。** byref-like 结果报错发生在编译期，`HasSubmissionResult`/`globals.Print` 现有路径不受影响（错误在 `HasAnyErrors()` 处短路，与 optional-question-prefix 的机制同构）。
- **Async 初始器是既定事实。** `<Initialize>` 恒为 async（`SynthesizedInteractiveInitializerMethod.vb:51-55`），顶层 `Await` 是 2.0 beta 已修复的合法能力；byref-like 跨 Await 存活由 D1 移植后的编译器层检查（BCX37052）自动报错，REPL 无需额外处理。

### RESOLUTION:

1. **方向定案（PROPOSAL A）**：byref-like 类型在 REPL 与 .vbx 脚本顶层**可用但受限**——只在**方法体局部、方法参数/返回值、`allows ref struct` 接口消费**中可用；**不可持久化为脚本类字段、不可作为提交结果装箱打印、不可跨越顶层 `Await` 存活**。
2. **顶层 byref-like `Dim` → 编译错误**：`Dim s As New Span(Of Integer)(1)`（REPL 或 .vbx 顶层）报错，语义锚定「ref-like 不能作类的字段」（对齐 C# `span-safety.md:266` / `ERR_FieldAutoPropCantBeByRefLike`）。错误码复用 restricted-type 族（`ERR_RestrictedType1`/`ERR_RestrictedConversion1` 系，BC31396/BC31394）。
3. **byref-like 结果/打印 → 编译错误**：`? New Span(Of Integer)(1)` 与末尾裸表达式结果为 byref-like 时，在「转换到提交返回类型 Object」处报 `ERR_RestrictedConversion1`（BC31394）。诊断**不区分 REPL/常规模式**，复用标准 restricted-type 错误（非法字段/非法转换）文案即可。**不做** PROPOSAL C 的特殊打印。
4. **跨 Await → 编译器层既有检查生效**：顶层 `Await` + 作用域内 byref-like 由 D1 移植后的 `ERR_CannotLiftRestrictedTypeResumable1`（BC37052）报错，REPL 零额外处理。
5. **PROPOSAL B 否决（语义不通）**：顶层 byref-like 作「提交内局部」两头落空——不持久 + 同提交方法访问不了（ByRef 传参又撞 BCX31396），与直接写在方法体内等价，且引入「顶层局部 vs 字段」双轨模型；不列独立增强。
6. **与 D1 衔接（验收依据）**：D1 落地顺序 = 移植 RefStructHelper 检查进编译器 → 编译器层 suppress ref struct obsolete error → 补本会议定义的 REPL/脚本顶层 byref-like 拒绝与文案。本会议决议是前置-1 的 REPL 侧语义契约。

### 状态

- **LDM 状态**：方向定案（指导性决议，服务于 `tasks\p1-immediate.md` 前置-1）。
- **三态判定**：本会议是前置议题，不产出对既有 proposal 的三态判定；它给 D1（RefStructHelper 移植 + suppress obsolete error，P1 interop 前置）补上 REPL/脚本侧的语义边界。**产出物 = 前置-1 的 REPL 语义契约**。决议已形式化为正式提案 `proposals\proposal-byref-like-safety.md`（**Active / Proposed**，覆盖 vbx 与常规编译模式，本会议的 REPL 表面语义为其中 4a 节）。

### OPEN QUESTIONS / TODO / Follow-up

- `TODO`：错误码映射表（BC31393/31394/31396/32061/36598/36640/37052 ↔ 既有 `ERR_Restricted*` 系列，核对哪些已存在于 `Errors.vb`）。
- `Follow-up`：本决议已形式化为 `proposals\proposal-byref-like-safety.md`（2026-08-12，Active/Proposed）；后续按该提案的 Detailed design（含常规编译模式）推进实施。
- `Follow-up`：补 REPL 行为矩阵无副作用单测（`Scripting\VisualBasicTest\`）——`? New Span(Of Integer)(1)` 报错、顶层 `Dim` of span 报错、方法内局部可用、方法内消费 `allows ref struct` 接口可用。

---

## RESOLUTION（本次会议汇总）

1. **byref-like 在 REPL/脚本：提交内可用、不跨提交持久化**。顶层 byref-like 变量、byref-like 结果（打印）、跨顶层 Await 均编译错误；方法体局部/ByVal 值参数/`allows ref struct` 接口消费可用（返回值与 `ByRef` 参数 → BC31396，RefStructHelper 定稿，见提案 §3/§4b）。对齐 C# `span-safety` 与 CSX 持久化冲突（LDM-2020-02-26）。
2. **错误码走 restricted-type 族**（BC31393/31394/31396/…/37052），编译期拦截，杜绝运行期 InvalidProgramException；**修错不算回归**（D4）。
3. **REPL 与 `.vbx` 脚本同 kind（Script），语义一致**，不分裂。
4. **为 p1-immediate 前置-1 提供 REPL 侧语义契约**：D1 落地时按本决议补 REPL/脚本顶层 byref-like 拒绝与文案。
5. **测试纪律**：所有新行为补无副作用单测，不引入网络/文件写入/进程启动等副作用。

## 附录：C# 生态与互操作考量

> 依据：`..\csharplang-index.md` 与 `..\csharplang` 镜像。C# 原文引用均先在 `..\csharplang` Grep 核实、逐字转抄并标注来源。`decisions.md` 的 M1–M8 / D1–D4 为 VBScript.NET 侧权威。

- **ref-like 栈约束的原文**（`csharplang\proposals\csharp-7.2\span-safety.md`）：`:5`（confined to the execution stack）、`:266`（不能作字段）、`:270-271`（不能装箱到 object/ValueType）、`:262`（await/yield 处不得在作用域内）。
- **CSX 持久化与 ref-like 冲突**（`csharplang\meetings\2020\LDM-2020-02-26.md:46-56`）：CSX 允许所有值持久化，故 ref local 等非法；这正对应 VBScript.NET REPL 的顶层字段持久化机制。C# 接受「交互方言与语言本体有距离」（同源判断见 `LDM-2020-04-15`，本库 `meeting-vb-repl-parity-with-csharp-repl.md` 已引）。
- **C# 脚本顶层 = 字段**（`csharplang\Portable\Parser\LanguageParser.cs:8452-8454`）：机制核实；C# 错误码 `ERR_FieldAutoPropCantBeByRefLike`（`ErrorCode.cs:1533`）证明 ref-like 顶层字段被显式拒绝。
- **C# suppress ref-like obsolete 的机制**（`csharplang\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.cs:998`）：`filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`——D1「编译器层 suppress ref struct obsolete error」的 C# 侧原型；VB 侧缺失（`TypeSymbol.vb:587-590`）。
- **与 `decisions.md` 的关系**：本会议是 D1（ref struct 解法 = 自定义分析器移植进编译器 + suppress obsolete）与 M3/M7（byref 互操作 / ref struct 接口消费）在 REPL 场景的落地边界。`M8` 提示的 `allows ref struct` 元数据识别（前置-2）与本决议正交：识别元数据解决「能不能消费」，本决议解决「REPL 里怎么用安全」。
