# byref-like 类型安全 / ByRef-Like Type Safety

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [x] Implementation: [Complete](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [x] Specification: [Complete](../spec/spec-byref-like-safety.md)

## Summary
[summary]: #summary

本提案让 VB 正确、安全地使用 byref-like 类型（C# `ref struct`，如 `Span(Of T)` / `ReadOnlySpan(Of T)`），并保证规则**对 vbx（脚本/REPL）与常规编译模式（.vb 项目）都生效**。这是 VB 追上 .NET 生态（`Span`/`Memory`/C# 13 `allows ref struct` 接口类型）的重要一步。四件事：

1. **RestrictedType 判定细化**：ref struct 一律视为 **RestrictedType**（沿用既有受限类型概念，不引入用户可写的 `ref struct` 声明修饰符）；编译器认识 `IsByRefLikeAttribute`，`ITypeSymbol.IsRefLikeType` 不再硬编码 `False`，`IsRestrictedType()` 覆盖所有 ref-like。IDE tooltip 显示「ByRef Like Structure」修饰（仅显示、不可声明）。
2. **suppress obsolete**：编译器层对 ref-like 类型抑制元数据里的 `[Obsolete]`，`Span(Of Integer)` 可直接使用。
3. **ref-safe 规则移植**：把 `G:\Projects\RefStructHelper` 的 BCX 系列错误码移植进编译器内部，编译期拦截装箱 / `Nullable(Of T)` / 泛型参数 / LINQ / 闭包 / async 状态机 / 字段 / 数组元素等非法用法。
4. **REPL/脚本模式特有约束**：顶层变量持久化（脚本类字段）与提交结果（打印）不得是 byref-like。

## Motivation
[motivation]: #motivation

- **追上生态（重要一步）**：现代 .NET 的高性能类型大多是 ref struct（`Span`、`ReadOnlySpan`、`Utf8String` 等）；C# 13 让 ref struct 可消费 `allows ref struct` 接口类型。VB 若不能安全处理它们，就无法参与生态的底层互操作（`decisions.md` D1 / M7）。
- **现状三个断点**（源码核实，均需修复）：
  - VB 编译器没有 ref-like 概念：`ITypeSymbol.IsRefLikeType` 硬编码 `False`（`Compilers\VisualBasic\Portable\Symbols\TypeSymbol.vb:587-590`，注释「VB has no concept of ref-like types」）。
  - 受限类型分析只覆盖三个特殊类型（`TypedReference` / `ArgIterator` / `RuntimeArgumentHandle`）：`IsRestrictedType`（`Symbols\SpecialTypeExtensions.vb:84-93`）。`Span(Of Integer)` 这类任意 ref struct 不在保护内——装箱到 `Object`、作字段、跨 `Await` 存活目前编译器都不拦。
  - 没有「suppress ref struct obsolete error」：C# 侧对 ref-like 类型在元数据里的 `[Obsolete]` 做了过滤（`PENamedTypeSymbol.cs:998`），VB 没有 → 今天在 VB 里直接用 `Span(Of Integer)` 会撞元数据上的 obsolete 错误。
- **现状后果**：在 VB 里用 ref-like 要么被 obsolete 错误挡住，要么（suppress 后）静默产出坏 IL → 运行期 `InvalidProgramException`。把这类误用改成正确编译错误 = **修错不算回归**（`decisions.md` D4，不影响 P1 地位）。
- **两种模式同一编译器**：`vbc`（.vb 项目）与 vbx 脚本/REPL 都是同一份 fork Roslyn VB 编译器（`Compilers\VisualBasic\Portable\`），**ref-like 安全规则必须在两种模式下语义一致，不得分裂**——这正是本提案区别于「仅 REPL 限制」的关键。

## Detailed design
[design]: #detailed-design

### 1. RestrictedType 判定细化（两种模式共用）

- **设计立场：VBX 不引入用户可写的 `ref struct` 声明修饰符。** 没有「ByRef Like Structure」的声明语法；C# 声明的 ref struct 在 VB 侧就是 **RestrictedType**（沿用既有受限类型概念），通过**细化 RestrictedType 判定**使其与 C# 的 ref-like 规则接近。
- 细化点 A：`ITypeSymbol.IsRefLikeType` 从硬编码 `False` 改为「类型携带 `System.Runtime.CompilerServices.IsByRefLikeAttribute`」。（待改点：`TypeSymbol.vb:587-590`）
- 细化点 B：`IsRestrictedType()` 的覆盖从「三个特殊类型」扩展到「所有 `IsRefLikeType`」——这是本提案最核心的一行改动，现有所有受限类型检查点自动继承（见第 3 节）。
- 对元数据（PE）类型读取该属性；VB 源类型层面无 ref struct 声明语法（只能消费 C# 已声明的 ref-like），只需在消费处应用规则。

### 2. 编译器层 suppress ref struct obsolete error（两种模式共用）

- 仿 C# `PENamedTypeSymbol.cs:998`：`filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`——对 ref-like 类型，其元数据 `[Obsolete]` 一律视为不存在，不报 obsolete 错误。
- 这是消费任何 ref-like 类型的前置：`Span(Of Integer)` 在 VB 里直接可写、可绑定。
- 实现落点：VB 的 PE 类型 obsolete 读取路径（`Symbols\Metadata\PE\PENamedTypeSymbol.vb:1455-1458` 附近），对 `IsRefLikeType` 类型跳过 obsolete 数据。

### 3. ref-safe 规则移植（RefStructHelper → 编译器内部，两种模式共用）

把独立分析器 `RefStructHelper` 的检查移植进编译器，触发条件从「三个特殊类型」改为「所有 `IsRefLikeType`」。错误码映射（`Errors.vb` 已存在标准码，与 BCX 同号）：

| 错误码 | 诊断 | 触发场景 |
|--------|------|---------|
| BC31393（`ERR_RestrictedAccess`） | 调用 ref-like 继承自 `Object`/`ValueType` 的实例方法（隐式装箱） | 任意模式 |
| BC31394（`ERR_RestrictedConversion1`） | 转换到 `Object` / `ValueType`（装箱） | 任意模式 |
| BC31396（`ERR_RestrictedType1`） | `Nullable(Of T)`、作字段、作数组元素、作返回值、作 `ByRef` 参数、匿名类型/委托/转换目标、作泛型类型实参（`allows ref struct` 反约束例外除外）等 | 任意模式 |
| BC32061（`ERR_ConstraintIsRestrictedType1`） | 受限/特殊类型作类型约束（`Of T As` 受限类型） | 任意模式 |
| BC36598（`ERR_CannotLiftRestrictedTypeQuery`） | LINQ 查询装箱 | 任意模式 |
| BC36640（`ERR_CannotLiftRestrictedTypeLambda`） | Lambda 闭包捕获 | 任意模式 |
| BC37052（`ERR_CannotLiftRestrictedTypeResumable1`） | async/iterator 状态机捕获 | 任意模式 |

- **既有检查点只需扩展谓词，无需新建位置**：字段（`SourceMemberFieldSymbol.vb:142-143`）、数组/返回类型（`SourceMethodSymbol.vb:2346`、`Binder_Statements.vb:1158`）、转换（`Binder_Conversions.vb:121/248/508`）、lambda（`Binder_Lambda.vb`）、匿名类型（`Binder_AnonymousTypes.vb`）等已全部走 `IsRestrictedType*` 系，改 `IsRestrictedType()` 定义即自动覆盖。
- **字段限制对齐 C# `span-safety.md:266`**（ref-like 不能作字段，除 ref-struct 内嵌 ref-struct）；**数组元素限制对齐 `:268`**；**装箱限制对齐 `:270-271`**。
- **`allows ref struct` 例外（已实现）**：当目标泛型约束为 C# 13 `allows ref struct` 时，ref-like 允许作该类型实参（元数据识别 `GenericParameterAttributes.AllowByRefLike` = 0x0020）。此时仍不可装箱——接口调度经 ref struct 直接完成，不走 Object。完整规则面（类型实参放行 / override/implement 透传 / 方法体校验 / BCX31393 装箱拦截 / codegen constrained）见 spec 新增小节「allows ref struct 反约束消费」。

### 4. 模式特化

#### 4a. 脚本/REPL（vbx 模式）

提交 = 脚本类（`Submission#N`），顶层 `Dim`/`Const` 是**脚本类字段**（`Parser.vb:714` → `BinderBuilder.vb:436`），末尾表达式被隐式转换为提交返回类型 **`Object`** 并作为结果（`Binder_Initializers.vb:211-220` + `InitializerRewriter.vb:202-243`），宿主 `globals.Print(state.ReturnValue)`（`CommandLineRunner.cs:313-315`）。与 ref-like 相交得到三条 REPL 特有规则：

1. **顶层 byref-like 变量 → 编译错误**：`Dim s As New Span(Of Integer)(1)` 会成为脚本类字段（`Parser.vb:714` 模块级 `Dim` → 字段），撞「ref-like 不能作类的字段」→ 报 `ERR_RestrictedType1`（BC31396）。对齐 C# 脚本顶层 = 字段（`LanguageParser.cs:8452-8454`）与 `ERR_FieldAutoPropCantBeByRefLike`（`ErrorCode.cs:1533`）。
2. **提交结果 / `?` 打印 of byref-like → 编译错误**：末尾表达式或 `? expr` 结果为 ref-like 时，在「转换到提交返回类型 Object」处报 `ERR_RestrictedConversion1`（BC31394）。**不做**特殊打印（值绑定栈上、不可持久，打印一个无法引用的类型名没有交互价值）。
3. **跨顶层 `Await` → 编译错误**：脚本初始化方法恒为 async（`SynthesizedInteractiveInitializerMethod.vb:51-55`），顶层 `Await` + 作用域内 byref-like → async 状态机捕获 → 报 `ERR_CannotLiftRestrictedTypeResumable1`（BC37052）。对齐 C# `span-safety.md:262`（await/yield 处 ref-like 不得在作用域内）。

> 语义来源：`../meetings/meeting-byref-like-repl-safety.md`（2026-08-12，RESOLUTION PROPOSAL A：提交内可用、不跨提交持久化）。

#### 4b. 常规编译（.vb 模式）

- **模块/类级字段 of byref-like → 编译错误**：复用既有字段检查（`SourceMemberFieldSymbol.vb:142-143`），谓词扩展到 `IsRefLikeType` 即生效。
- **方法体局部 / ByVal 值参数：可用**。ref-like 栈绑定天然适合方法局部；这是 D1「消费 `allows ref struct` 接口类型」的主场景（`Dim x As IOf(Of Integer) = ...`、作 ByVal 参数传入）。返回值与 `ByRef` 参数见下方 / §3 表格。
- **`ByRef s As Span(Of T)` → 编译错误（BCX31396，RefStructHelper 已定稿）**：RestrictedType 用作 ByRef 参数属非法（`RefStructBCX31396Analyzer.vb:194-195` 检查 ByRef 参数、`RestrictedTypeUsageDemo.vb:61-62`；消息「cannot be used as … 'ByRef' parameter」）。**`scoped`/`UnscopedRef` 不是 VB 侧需要的概念**——C# 源码级生命周期注解服务于作者侧，VBX 只消费不声明 ref struct，没有「写 `scoped`」的场景；ref-like 的逃逸/生命周期以编译器内部规则表达，`allows ref struct` 接口消费同以内部规则处理（依赖 M8 元数据识别，`p1-immediate.md` 前置-2）。
- **async/iterator / lambda / LINQ / 泛型 / 装箱**：同第 3 节通用规则，无模式差异。

### 5. 显示：IDE tooltip 的「ByRef Like Structure」修饰（两种模式共用）

- ref-like（RestrictedType）类型在 IDE tooltip 上显示「**ByRef Like Structure**」修饰（如 `ByRef Like Structure Span(Of T)`）——**仅显示、不可声明**。
- **显示格式由 VBX 侧定稿**：VBX 是 fork 的编译器，**可以造语法/显示约定**；`RefStructHelper` 是原版 VB 的分析器插件，不能自己造语法，其文档措辞（`<IsByRefLike>` Structure）只描述插件自身，不构成 VBX 的显示约定。因此采用用户定稿格式「**ByRef Like Structure**」。
- 类比：C# 的 `ref struct` 显示；VB 对 `ByRef` 一类修饰符的展示惯例（如 `ByRef Function` 仅作为显示层修饰出现、不是可声明语法）。
- 实现落点：VB 的符号显示/分类路径（`SymbolDisplayVisitor.vb` 系）对 `IsRefLikeType` 类型追加该修饰词；不影响语言解析、不影响可写语法。
- 意义：把「这是 ref struct、有栈上限制」在 IDE 里显性化，让消费方一眼识别 RestrictedType 的边界，避免把它当普通结构体用。

### 6. 与既有决策 / 任务清单的关系

- **D1**（`decisions.md` / `p1-immediate.md` 前置-1）：本提案是 D1 的产品化；D1 实施时按本提案 Detailed design 落地（移植检查 + suppress obsolete + 补 REPL/脚本顶层拒绝与文案）。
- **M7**（ref struct 接口消费）：`allows ref struct` 接口类型在本提案规则下可消费，且两种模式一致。
- **M8 / 前置-2**（元数据识别）：`allows ref struct` 例外依赖识别 C# 13 泛型反约束元数据，属前置-2 范围。
- **与 `proposal-return-byref`（M3）区分**：本提案管 **ref-like 类型的合法性**（哪些用法编译期拒绝）；return-byref 管 **`Return True, value:=result` 写回 ByRef/Out 的语法**。两者互补，共享 ByRef 逃逸语义的对齐工作。

## Drawbacks
[drawbacks]: #drawbacks

- **行为变化**：把「obsolete 错误 / 运行期 `InvalidProgramException`」变成「编译期 ref-safe 错误」——属修错，不算回归（D4），但老项目若依赖（错误的）运行行为会被新编译期错误打断，需迁移说明。
- **移植成本**：RefStructHelper 是独立分析器，移植进编译器需逐码核对触发条件与诊断位置，并维护双份（分析器版退役？见 Unresolved questions）。
- **与 C# 规则差异面**：VB 无 `ref struct` 声明语法、无 `scoped`/`UnscopedRef` 用户级关键字（按设计不需要——ref struct = RestrictedType，只消费不声明）；ref-like 的逃逸/生命周期规则全部以编译器内部计算表达，与 C# 源码级 `scoped` 注解不互通。对纯消费场景影响小（C# 签名自带 scoped 信息，VB 照单全收），但 VB 无法精准表达「本函数参数不逃逸」类的新作者侧契约，长期需跟随 C# 规则演进。

## Alternatives
[alternatives]: #alternatives

- **保持 RefStructHelper 为外部分析器（现状）**：不追生态；VB 项目需手动引用 NuGet 分析器；REPL 与编译器内部无保护，`Span` 装箱仍会产出坏 IL。
- **仅 REPL 限制、常规模式不管**：规则分裂——同一编译器对 .vbx 和 .vb 两套行为，违背「对 vbx 和常规模式 vb 都要生效」的本提案前提；且 D1 的互操作价值（消费 ref struct 接口）主要在常规编译的方法体里，只限 REPL 等于没做。
- **照搬 C# ref-safe 全套（含 `scoped`/`UnscopedRef` 用户级关键字）**：超出 VB 定位；VBX 不引入用户可写的 ref struct 声明（ref struct = RestrictedType），`scoped` 无从表达、也没有作者侧场景；收益低、成本高，不采用。

## Unresolved questions
[unresolved]: #unresolved-questions

- **无。** 既有 5 条已定稿/移出：
  - `RefStructHelper` 独立分析器是否退役 / 发 NuGet：与本提案无关 → 移出。
  - `allows ref struct` 接口类型作为结果：非 VBX 专属，只要实现该接口就都得考虑 → 移出。
  - `ByRef s As Span(Of T)`：RefStructHelper 已定稿非法 → BCX31396（见 §4b）。
  - 诊断文案是否区分 REPL/常规：**不区分**，复用标准 restricted-type 错误（非法字段/非法转换）即可。
  - csi `new Span<int>(1)`：`new` 本身无问题，问题在值往哪存（结果装箱/字段），无独立诊断问题 → 移出。
- 实现期如发现新边界再回填。

## 相关文档

- 会议：`../meetings/meeting-byref-like-repl-safety.md`（REPL/脚本表面语义来源，RESOLUTION PROPOSAL A）
- 任务：`../tasks/p1-immediate.md` 前置-1（D1）、前置-2（M8 元数据识别）
- 决策：`../decisions.md` D1 / D4 / M3 / M7 / M8
- 参考来源：`G:\Projects\RefStructHelper`（BCX 系列分析器）；`../csharplang\proposals\csharp-7.2\span-safety.md`；`../csharplang\meetings\2020\LDM-2020-02-26.md`
