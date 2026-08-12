# spec：byref-like 类型安全（ref struct 支持）

**状态**：Active（proposal `proposals/proposal-byref-like-safety.md` Active/Proposed，LDM 会议 2026-08-12 决议方向定案）；所属里程碑：2.0 beta（`with-modified-vbsyntax` 分支，进行中）。

## 能力规范

- **适用面**：本能力对 **vbx（脚本/REPL）与常规编译模式（.vb 项目）都生效，语义一致**——两种模式共用同一份 fork Roslyn VB 编译器（`Compilers\VisualBasic\Portable\`），ref-like 安全规则不得分裂。
- **RestrictedType 判定细化**：ref struct 一律视为 **RestrictedType**（沿用既有受限类型概念，**不引入用户可写的 `ref struct` 声明修饰符**）；编译器认识 `System.Runtime.CompilerServices.IsByRefLikeAttribute`，`ITypeSymbol.IsRefLikeType` 不再硬编码 `False`，`IsRestrictedType()` 覆盖从三个特殊类型（`TypedReference` / `ArgIterator` / `RuntimeArgumentHandle`）扩展到所有 ref-like。
- **suppress ref struct obsolete error**：编译器层对 ref-like 类型抑制元数据里的 `[Obsolete]`（仿 C# `PENamedTypeSymbol.cs:998` 的 `filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`），`Span(Of Integer)` 可直接使用、可绑定。
- **ref-safe 规则移植**：把 `G:\Projects\RefStructHelper` 的 BCX 系列错误码移植进编译器内部，触发条件从「三个特殊类型」改为「所有 `IsRefLikeType`」，编译期拦截非法用法（错误码走 restricted-type 族，与 BCX 同号）：
  - BCX31393（`ERR_RestrictedAccess`）：调用 ref-like 继承自 `Object`/`ValueType` 的实例方法（隐式装箱）；
  - BCX31394（`ERR_RestrictedConversion1`）：转换到 `Object` / `ValueType`（装箱）；
  - BCX31396（`ERR_RestrictedType1`）：`Nullable(Of T)`、作字段、作数组元素、作返回值、作 `ByRef` 参数、匿名类型/委托/转换目标、作泛型类型实参（`allows ref struct` 反约束例外除外）等；
  - BCX32061（`ERR_ConstraintIsRestrictedType1`）：受限/特殊类型作类型约束（`Of T As` 受限类型）；
  - BCX36598（`ERR_CannotLiftRestrictedTypeQuery`）：LINQ 查询装箱；
  - BCX36640（`ERR_CannotLiftRestrictedTypeLambda`）：Lambda 闭包捕获；
  - BCX37052（`ERR_CannotLiftRestrictedTypeResumable1`）：async/iterator 状态机捕获。
- **ByRef ref-struct 参数 → BCX31396 非法**（RefStructHelper 已定稿）；**返回值 → BCX31396 非法**；**字段/数组元素 → BCX31396**。字段限制对齐 C# `span-safety.md`（ref-like 不能作字段，除 ref struct 内嵌 ref struct）、数组元素限制与装箱限制同向对齐。
- **`scoped`/`UnscopedRef` 不是 VB 侧需要的概念**：VBX 只消费不声明 ref struct，没有「写 `scoped`」的场景；ref-like 的逃逸/生命周期以编译器内部规则表达；`allows ref struct` 接口消费同以内部规则处理（依赖 M8 元数据识别，`tasks\p1-immediate.md` 前置-2）。
- **显示**：IDE tooltip 对 ref-like（RestrictedType）类型显示「**ByRef Like Structure**」修饰（如 `ByRef Like Structure Span(Of T)`），**仅显示、不可声明**；显示格式由 VBX 侧定稿（VBX 是 fork 编译器可造语法/显示约定；`RefStructHelper` 是原版 VB 插件不能造语法，其 `<IsByRefLike>` 措辞只是插件描述，不构成 VBX 约定）。
- **REPL/脚本（vbx）特有约束（顶层，PROPOSAL A）**：
  - 顶层 byref-like `Dim`（会成为脚本类字段）→ 编译错误（BC31396 系，语义锚定「ref-like 不能作类的字段」，对齐 C# `ERR_FieldAutoPropCantBeByRefLike`）；
  - 提交结果 / `?` 打印结果为 byref-like（在「转换到提交返回类型 `Object`」处装箱）→ 编译错误（BC31394）；
  - 跨顶层 `Await`（脚本初始化方法恒为 async 状态机）→ 编译错误（BC37052）。
  - **可用**：方法体局部 / ByVal 值参数 / `allows ref struct` 接口消费。
- **候选方案结论**：A（顶层全禁，对齐 C# CSX）**采纳**——最安全、语义最简单、与 C# 完全同向，v1 及后续都走 A；B（提交内局部）**否决**——不持久 + 同提交方法访问不了（ByRef 传参又撞 BCX31396），与直接写在方法体内等价、双轨模型无独立价值；C（特殊打印）**否决**——值绑定栈上、不可持久，打印一个无法引用的类型名没有交互价值，且把错误降级为花哨输出与「修错不算回归」（D4）精神相反。
- **Unresolved = 0**：既有 5 条已全部定稿/移出（RefStructHelper 是否退役/NuGet 与本提案无关移出；`allows ref struct` 接口作结果移出；`ByRef s As Span(Of T)` 定稿 BCX31396；诊断文案不区分 REPL/常规；csi `new Span<int>(1)` 的 `new` 本身无问题移出）；实现期如发现新边界再回填。

## 结构事实

- **编译器层落点**（`Compilers\VisualBasic\Portable\`）：
  - `Symbols\TypeSymbol.vb`：`IsRefLikeType`——不再硬编码 `False`，改为「类型携带 `IsByRefLikeAttribute`」；
  - `Symbols\TypeSymbolExtensions.vb` / `Symbols\SpecialTypeExtensions.vb`：`IsRestrictedType()`——覆盖从三个特殊类型扩展到所有 `IsRefLikeType`（`TypeSymbol` 层扩展委托 `SpecialType` 层判定，扩展谓词即自动继承既有检查点）；
  - `Symbols\Source\`：字段检查（`SourceMemberFieldSymbol`）、数组/返回类型检查（`SourceMethodSymbol`）等既有检查点；
  - `Binding\Binder_Conversions.vb`（转换）、`Binding\Binder_Lambda.vb`（lambda 闭包）、`Binding\Binder_AnonymousTypes.vb`（匿名类型）等既有检查点——扩展谓词后自动覆盖，无需新建位置；
  - `Errors\Errors.vb`：`ERR_Restricted*` 系列错误码（BC31393/31394/31396/32061/36598/36640/37052 同号）。
- **脚本层落点**：
  - `Parser\Parser.vb`：顶层模块级 `Dim` → 脚本类字段；
  - `Binding\Binder_Initializers.vb`：提交末尾表达式隐式转换到提交返回类型 `Object`（装箱点）；
  - `Symbols\Source\SynthesizedInteractiveInitializerMethod.vb`：脚本初始化方法恒为 async；
  - `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`：`globals.Print(state.ReturnValue)` 打印路径。
- **显示层落点**：`SymbolDisplay\SymbolDisplayVisitor.vb` 系——对 `IsRefLikeType` 类型追加「ByRef Like Structure」修饰词（仅显示、不影响解析与可写语法）。
- 本 spec 不堆行号；实现细节（改动函数、行号、改动形状）以 `tasks\p1-immediate.md` 前置-1 为唯一来源。

## 规范依据链

proposal（`proposals/proposal-byref-like-safety.md`，Active/Proposed）→ LDM 会议（`meetings/meeting-byref-like-repl-safety.md`，RESOLUTION PROPOSAL A）→ 本 spec。实现细节见 `tasks\p1-immediate.md` 前置-1（D1：移植 RefStructHelper + 编译器层 suppress ref struct obsolete error）、前置-2（M8：`allows ref struct` 元数据识别）。

## 测试事实

- 测试纪律：单测无副作用（不发起网络、不写文件、不启动进程、不写注册表）。
- REPL 行为矩阵（`Scripting\VisualBasicTest\`）：`? New Span(Of Integer)(1)` 报错、顶层 `Dim` of span 报错、方法内局部可用、方法内消费 `allows ref struct` 接口可用；跨顶层 `Await` + 作用域内 byref-like 报错（BC37052）。
- 诊断不区分 REPL/常规模式，复用标准 restricted-type 错误（非法字段/非法转换）文案即可。

## 与 modvb 的关系

本 spec 描述 VBScript.NET 产品自身能力，与 modvb 提案库分离；实现细节来源为产品自身设计文档（proposal / meeting / p1-immediate），不依赖 modvb 原文；外部参考仅 `G:\Projects\RefStructHelper`（BCX 系列分析器）与 `csharplang` 镜像（span-safety / CSX 持久化先例）。
