# 概要设计：byref-like 类型安全（ref struct 支持，D1 前置-1）

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-byref-like-safety.md`（Active/Proposed）→ `../../meetings/meeting-byref-like-repl-safety.md`（RESOLUTION PROPOSAL A）→ `../../spec/spec-byref-like-safety.md`。
> 本设计吸收 meeting RESOLUTION PROPOSAL A 与 proposal Detailed design §1-§6，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出。

## 1. 背景与目标

**目标（一句话）**：让 VB 正确、安全地使用 byref-like 类型（C# `ref struct`，如 `Span(Of T)` / `ReadOnlySpan(Of T)`），规则对 **vbx（脚本/REPL）与常规编译模式（.vb 项目）都生效且语义一致**——这是 VB 追上 .NET 生态（`Span`/`Memory`/C# 13 `allows ref struct` 接口类型）的第一步。

**现状缺口（三点）**：

1. **无 ref-like 概念**：`ITypeSymbol.IsRefLikeType` 硬编码 `False`，注释「VB has no concept of ref-like types」（`TypeSymbol.vb:587-592`）。
2. **RestrictedType 只盖三特殊类型**：`IsRestrictedType`（`SpecialTypeExtensions.vb:84-93`）只保护 `TypedReference`/`ArgIterator`/`RuntimeArgumentHandle`。`Span(Of Integer)` 这类任意 ref struct 不在保护内——装箱到 `Object`、作字段、跨 `Await` 存活目前编译器都不拦。
3. **无 suppress ref struct obsolete**：C# 对 ref-like 类型的元数据 `[Obsolete]` 做了过滤（`PENamedTypeSymbol.cs:998`），VB 没有 → 今天在 VB 里直接用 `Span(Of Integer)` 会撞元数据 obsolete 错误。

**现状后果**：在 VB 里用 ref-like 要么被 obsolete 错误挡住，要么（suppress 后）静默产出坏 IL → 运行期 `InvalidProgramException`。把这类误用改成正确编译错误 = **修错不算回归**（D4，`../../tasks/p1-immediate.md` 前置-1）。

**状态**：proposal 与 LDM 会议均为 **Active**；会议采纳 **PROPOSAL A**（顶层全禁，对齐 C# CSX），实现落点为**编译器层、两种模式共用同一编译器**（RESOLUTION PROPOSAL A / proposal §1-§6）。

**RESOLUTION PROPOSAL A 吸收映射**：

| 决议 | 内容 | 落在本设计 |
|------|------|-----------|
| 1 | 方向定案 PROPOSAL A：提交内可用、不跨提交持久化 | 第 2、3、4 节 |
| 2 | 顶层 byref-like `Dim` → 编译错误（脚本类字段，锚定「ref-like 不能作类的字段」） | 第 2、3 节 |
| 3 | byref-like 结果/打印 → 编译错误（`ERR_RestrictedConversion1` BC31394）；诊断不区分 REPL/常规；不做特殊打印 | 第 2、3、4 节 |
| 4 | 跨顶层 `Await` → 编译器层既有检查生效（`ERR_CannotLiftRestrictedTypeResumable1` BC37052），REPL 零额外处理 | 第 2 节 |
| 5 | PROPOSAL B 否决（语义不通） | 第 6 节 |
| 6 | 与 D1 衔接：移植 RefStructHelper → suppress obsolete → 补 REPL/脚本顶层拒绝与文案 | 第 5 节 |

## 2. 总体架构（五步落点）

**核心观察**：VB 编译器**已有**完整的 restricted-type 检查点（字段、返回/数组、转换、lambda、匿名类型、泛型约束、async/iterator 捕获），它们全部经 `IsRestrictedType()` / `IsRestrictedTypeOrArrayType()` 谓词。因此本任务的实质是：**把 `IsRestrictedType()` 的判定从「三特殊类型」扩展到「所有 ref-like」**——这一处谓词扩展让既有检查点自动继承，不必新建任何检查位置。在此基础上叠加「识别 `IsByRefLikeAttribute`（判定细化）」「suppress obsolete」「REPL 三碰撞点」与「显示」四件事。五步落点如下。

### 2.1 判定细化（`VB\Symbols\`）

- `TypeSymbol.vb:587-592`：`ITypeSymbol_IsRefLikeType` 不再硬编码 `False`，改为「类型携带 `System.Runtime.CompilerServices.IsByRefLikeAttribute`」。需在 `TypeSymbol` 基类加一个 `Protected Overridable` 属性，由 `ITypeSymbol_IsRefLikeType` 显式实现委托，派生类型（如 `PENamedTypeSymbol`）覆盖之。
- `SpecialTypeExtensions.vb:84-93`：`IsRestrictedType(this As SpecialType)` 从三特殊类型扩展到「`this.IsRefLikeType()`」——**这是本提案最核心的一行改动**。
- `TypeSymbolExtensions.vb:365-367`（`IsRestrictedType(this As TypeSymbol)` 委托 `SpecialType` 层）与 `:380-392`（`IsRestrictedTypeOrArrayType` 剥数组）无需改动，自动覆盖。
- **WellKnown 表已存在 `IsByRefLikeAttribute`**：`Core\Portable\WellKnownTypes.cs:273/:650`、`Core\Portable\WellKnownMember.cs:476`——实现无需新增条目，只需读取。

### 2.2 suppress obsolete（`VB\Symbols\Metadata\PE\`）

仿 C# `PENamedTypeSymbol.cs:998`（`filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`）：对 ref-like 类型，其元数据 `[Obsolete]` 一律视为不存在。VB 落点：`PENamedTypeSymbol.vb:1455-1460` 的 `ObsoleteAttributeData` 覆盖处，对 `IsRefLikeType` 类型跳过 `InitializeObsoleteDataFromMetadata`（返回 `Nothing`）。这是消费任何 ref-like 类型的前置：`Span(Of Integer)` 直接可写、可绑定。

### 2.3 BCX 规则移植（复用既有检查点，`VB\Binding\` / `VB\Symbols\Source\`）

把独立分析器 `{{VBRefStructHelper}}` 的 BCX 系列错误码移植进编译器内部，**触发条件从「三个特殊类型」改为「所有 `IsRefLikeType`」**——既有检查点全部走 `IsRestrictedType*` 谓词，改 `IsRestrictedType()` 定义即自动覆盖：

- 字段：`SourceMemberFieldSymbol.vb:142-143`；返回/数组：`SourceMethodSymbol.vb:2346`、`Binder_Statements.vb:1158`；转换（装箱）：`Binder_Conversions.vb:121/248/508`；lambda：`Binder_Lambda.vb`（:46/:107/:269/:285/:804/:948/:963）；匿名类型：`Binder_AnonymousTypes.vb:32/:253`；泛型约束：`ConstraintsHelper.vb:661`；async/iterator 捕获：`IteratorAndAsyncCaptureWalker.vb`（:98/:113/:133）。
- 字段限制对齐 C# `span-safety.md:266`；数组元素限制对齐 `:268`；装箱限制对齐 `:270-271`。
- **`allows ref struct` 例外**（依赖 M8 元数据识别，前置-2）：目标泛型约束为 `allows ref struct` 时，ref-like 允许作该类型实参；此时仍不可装箱。

### 2.4 REPL 三碰撞点（`VB\Parser\` / `VB\Binding\` / `Scripting\`）

把 REPL 机制与 byref-like 规则相交，三个碰撞点：

1. **脚本类字段（顶层持久化）**：顶层 `Dim s As New Span(Of Integer)(1)` → `Parser.vb:714`（模块级 `DimKeyword`）→ 脚本类字段（`BinderBuilder.vb:436`）→ 撞「ref-like 不能作类的字段」（`SourceMemberFieldSymbol.vb:142` 字段检查**自动覆盖**）→ `ERR_RestrictedType1`（BC31396）。
2. **结果装箱（打印）**：`? New Span(Of Integer)(1)` / 末尾裸表达式 → `Binder_Initializers.vb:211-220` 隐式转换到提交返回类型 `Object`（`InitializerRewriter.vb:202-243` 落地）→ 撞「ref-like 不可装箱」→ `ERR_RestrictedConversion1`（BC31394）。
3. **async 状态机**：`<Initialize>` 恒为 async（`SynthesizedInteractiveInitializerMethod.vb:51-55`），顶层 `Await` + 作用域内 byref-like → `IteratorAndAsyncCaptureWalker.vb:98/:113/:133`（已用 `IsRestrictedType`，扩展谓词即覆盖）→ `ERR_CannotLiftRestrictedTypeResumable1`（BC37052）。

宿主 `CommandLineRunner.cs:313-315`（`globals.Print`）**零改动**——byref-like 结果报错发生在编译期，`HasSubmissionResult`/`globals.Print` 现有路径不受影响（错误在 `HasAnyErrors()` 处短路，与 optional-question-prefix 的机制同构）。

### 2.5 显示（`VB\SymbolDisplay\`）

`SymbolDisplayVisitor.Types.vb:89`（`VisitNamedType`）对 `IsRefLikeType` 类型追加「**ByRef Like Structure**」修饰（如 `ByRef Like Structure Span(Of T)`）——仅显示、不可声明。不影响解析与可写语法。

## 3. REPL 行为对照表

> 「可用」= 提交通过编译、正常交互；「编译错误」= 提交被 `HasAnyErrors()` 短路、不运行。`*` 依赖 M8 元数据识别（前置-2）。

| 提交 | 机制 | 提案后行为 |
|------|------|-----------|
| 顶层 `Dim s As New Span(Of Integer)(1)` | 脚本类字段（`Parser.vb:714` → `BinderBuilder.vb:436`）→ 字段检查（`SourceMemberFieldSymbol.vb:142`） | **编译错误 BC31396**（ref-like 不能作类的字段，对齐 C# `ERR_FieldAutoPropCantBeByRefLike`） |
| `? New Span(Of Integer)(1)` | PrintStatement → 隐式转换到 `Object`（`Binder_Initializers.vb:219-220`） | **编译错误 BC31394**（ref-like 不可装箱） |
| 末尾裸表达式 `New Span(Of Integer)(1)` | 末尾非 Void 表达式 → 提交返回 `Object`（`InitializerRewriter.vb:202-243`） | **编译错误 BC31394**（同上，结果装箱点） |
| 方法内 `Dim s As New Span(Of Integer)(1)` | 方法体局部（非脚本类字段） | **可用**（栈绑定天然适合方法局部） |
| ByVal 传参 `Sub F(s As Span(Of Integer))` | 值参数 | **可用** |
| ByRef 传参 `Sub F(ByRef s As Span(Of Integer))` | ByRef 参数 | **编译错误 BC31396**（RefStructHelper 已定稿非法；消息「cannot be used as … 'ByRef' parameter」） |
| `allows ref struct` 接口消费 `Dim x As IOf(Of Integer) = ...` * | 方法体/局部，接口调度经 ref struct 直接完成 | **可用**（依赖前置-2 M8 识别反约束元数据；仍不可装箱） |
| 跨顶层 `Await`（作用域内 byref-like） | `<Initialize>` 恒为 async（`SynthesizedInteractiveInitializerMethod.vb:51-55`）→ 状态机捕获 | **编译错误 BC37052**（对齐 C# `span-safety.md:262`） |

## 4. 判定原则

- **ref-like = RestrictedType**：不引入用户可写的 `ref struct` 声明修饰符；C# 声明的 ref struct 在 VB 侧就是 RestrictedType（沿用既有受限类型概念），通过细化 RestrictedType 判定与 C# 的 ref-like 规则接近。
- **扩展谓词即自动继承**：所有既有 restricted-type 检查点（字段/返回/数组/转换/lambda/匿名类型/泛型约束/async 捕获）都经 `IsRestrictedType()` / `IsRestrictedTypeOrArrayType()`，改定义即自动覆盖，**不新建检查位置**。
- **错误码走 restricted-type 族**（BC31393/31394/31396/32061/36598/36640/37052），诊断**不区分 REPL/常规模式**，复用标准 restricted-type 错误文案（非法字段/非法转换）即可。
- **提交内可用、不跨提交持久化**（PROPOSAL A）：方法体局部 / ByVal 值参数 / `allows ref struct` 接口消费可用；顶层持久化（字段）、结果装箱（打印）、跨顶层 Await 编译错误。
- **`scoped`/`UnscopedRef` 非 VB 概念**：VBX 只消费不声明 ref struct，逃逸/生命周期以编译器内部规则表达。

## 5. C# 先例与跟随设定

- **ref-like 栈约束原文**：`csharplang\proposals\csharp-7.2\span-safety.md:5`（confined to the execution stack）、`:262`（await/yield 处不得在作用域内）、`:266`（不能作字段）、`:270-271`（不能装箱）。**VB 逐条对齐**。
- **CSX 持久化冲突**：`csharplang\meetings\2020\LDM-2020-02-26.md:46-56`——CSX 允许所有值持久化 → ref local 等非法；C# 接受「交互方言与语言本体有距离」。VB REPL 与 `.vbx` 同 kind（Script）的顶层字段持久化机制同源，**跟随同一设定**。
- **脚本顶层 = 字段**：`CSharp\Portable\Parser\LanguageParser.cs:8450-8456`（全局脚本层把 local-decls 解析为字段）；C# 错误码 `ERR_FieldAutoPropCantBeByRefLike = 8345`（`ErrorCode.cs:1533`）。VB 的 `Parser.vb:714` + `BinderBuilder.vb:436` 机制同构，顶层 `Dim` 自动落进字段检查。
- **C# suppress ref-like obsolete 原型**：`PENamedTypeSymbol.cs:998`——VB 跟随同一过滤逻辑，落点在 `PENamedTypeSymbol.vb:1455-1460`。
- **两种模式同一编译器**：vbx 与常规 .vb 共用同一份 fork Roslyn VB 编译器，规则语义一致不分裂（这是本提案区别于「仅 REPL 限制」的关键，`../../tasks/p1-immediate.md` 前置-1）。

## 6. 代价与边界

- **`.vbx` 与 REPL 同 kind（Script），语义一致**：`SourceCodeKind.Interactive` 已废弃，REPL 与 `.vbx` 都走 `SourceCodeKind.Script`（`VisualBasicCompiler.vb:96`）。「顶层禁」同时约束两者，避免「REPL 禁、脚本不禁」的分裂（对齐 C# 对 csx 的同一判断）。
- **`.vbx` 顶层同样受限**：`.vbx` 脚本文件顶层 `Dim` of span / 顶层结果 / 顶层 Await + span 与 REPL 报同样错误（同 kind、同一编译器路径）。
- **修错不算回归（D4）**：今天 `Span` 用法要么 obsolete 错误、要么（suppress 后）运行期 `InvalidProgramException`；本设计把它变成正确编译错误 = 修错，不构成回归，不影响 P1 地位。老项目若依赖（错误的）运行行为会被新编译期错误打断，需迁移说明。
- **与退出码/打印正交**：byref-like 结果报错发生在编译期，`HasSubmissionResult`（`VisualBasicCompilation.vb:816-868`）/`globals.Print`（`CommandLineRunner.cs:313-315`）现有路径不受影响。
- **`allows ref struct` 例外依赖前置-2**：本设计不展开该反约束实现，标注依赖 M8 元数据识别。
- **行为变化面**：错误码复用 restricted-type 族，诊断文案不区分 REPL/常规；无新错误码、无新语法、无新关键字。
