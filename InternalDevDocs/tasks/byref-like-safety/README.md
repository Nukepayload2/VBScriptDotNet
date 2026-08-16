# 任务：byref-like 类型安全（ref struct 支持，D1 前置-1）设计任务

本文件夹是 `proposal-byref-like-safety` 的设计任务存储（Vortex 代办列表 + 设计产物）。本任务是「byref-like 类型安全」的**进一步分解**：D1（移植 RefStructHelper + 编译器层 suppress ref struct obsolete error）在 REPL/脚本与常规编译两种模式下的语义边界、判定细化与测试计划。

- **依据链**：`../../proposals/proposal-byref-like-safety.md`（Active/Proposed）→ `../../meetings/meeting-byref-like-repl-safety.md`（RESOLUTION PROPOSAL A，方向定案）→ `../../spec/spec-byref-like-safety.md`（能力规范/结构事实/测试事实）→ `../../tasks/p1-immediate.md` 前置-1（D1）→ `../../compilers-index.md`（编译器树索引）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`）、测试计划（`test-plan.md`）。三份均要求无副作用测试矩阵。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

> 本任务仅覆盖**设计阶段**（F1-F5）；实现阶段（F9-F12）待 D1 正式实施时再拆（见 F9 行占位说明）。本代理一次性完成 F1-F5 的全部设计文档，状态先标 done，供验证者核对打回。

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`，待验证者核对） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`，待验证者核对） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`，待验证者核对） |
| F4 | 一致性审计 + 修复 + 复验 | F1/F2/F3 三份交付物与 proposal/meeting/spec/p1-immediate 交叉一致（PROPOSAL A、错误码表、ByRef Like Structure、两模式一致、scoped 非概念、无越权承诺）；源码事实行号全部真实；无残留冲突 | **done**（由验证者核对打回） |
| F5 | 验证 README 源码事实 | README「共享源码事实」每条 `文件:行号` 经验证者 Read/Grep 复核；与源码不冲突 | **done**（验证者） |
| F9-F12 | 实现阶段分解（判定细化 / suppress obsolete / REPL 三碰撞点 / 显示） | **占位**：待 D1 正式实施时按 design-detailed.md 改动清单逐条再拆（实施者 + 验证者串行交替），届时回填本表 | 待 D1 实施 |

> 本任务定位：**设计先行**。D1 实际实施在 `tasks\p1-immediate.md` 前置-1 落定时进行；届时本文件夹的 design-detailed.md 是实施者的直接照做底稿。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-12，本代理逐条 Read/Grep）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`），文件相对路径均相对仓库根。

### 判定细化（改动点 1）

- **`TypeSymbol.vb:587-592`**：`ITypeSymbol_IsRefLikeType` 硬编码 `False`，注释「VB has no concept of ref-like types」（`:589`）、`Return False`（`:590`）。**VB 编译器没有 ref-like 概念**。
- **`SpecialTypeExtensions.vb:84-93`**：`IsRestrictedType(this As SpecialType)` 只盖三个特殊类型——`TypedReference` / `ArgIterator` / `RuntimeArgumentHandle`。
- **`TypeSymbolExtensions.vb:365-367`**：`IsRestrictedType(this As TypeSymbol)` 委托 `this.SpecialType.IsRestrictedType()`；`TypeSymbolExtensions.vb:380-392` 的 `IsRestrictedTypeOrArrayType` 剥数组后判 `IsRestrictedType()`。**扩展谓词即自动继承既有检查点**。
- **`Core\Portable\WellKnownTypes.cs:273`**（枚举 `System_Runtime_CompilerServices_IsByRefLikeAttribute`）与 **`:650`**（元数据名 `"System.Runtime.CompilerServices.IsByRefLikeAttribute"`）；**`Core\Portable\WellKnownMember.cs:476`**（`System_Runtime_CompilerServices_IsByRefLikeAttribute__ctor`）。→ **`IsByRefLikeAttribute` 的 WellKnown 条目已存在，无需新增**。
- **C# 原型**（`CSharp\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2886-2912`）：`IsRefLikeType` 覆盖在 `TypeKind = Struct` 时读 `module.HasIsByRefLikeAttribute(_handle)`（`Core\Portable\MetadataReader\PEModule.cs:1235-1238`）。

### suppress obsolete（改动点 2）

- **C# 原型**：`PENamedTypeSymbol.cs:998` `filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null`（`GetCustomAttributes` 时过滤 `[Obsolete]`；`:1000` 另有 `filterIsByRefLikeAttribute = IsRefLikeType`）。
- **VB 侧缺失**：`VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb` **无 `IsRefLikeType` 覆盖**（Grep 无匹配）；VB PE 类型 obsolete 读取路径 `PENamedTypeSymbol.vb:1455-1460`（`ObsoleteAttributeData` 覆盖，`ObsoleteAttributeHelpers.InitializeObsoleteDataFromMetadata(_lazyObsoleteAttributeData, _handle, ContainingPEModule)` 于 `:1457`）。

### 既有检查点（扩展谓词即自动继承，改动点 3/4 复用的落点）

| 检查点 | 文件:行号 | 形态 |
|---|---|---|
| 字段 | `SourceMemberFieldSymbol.vb:142-143` | `varType.IsRestrictedTypeOrArrayType` → `ERR_RestrictedType1` |
| 返回/数组 | `SourceMethodSymbol.vb:2346-2347` | `retType.IsRestrictedArrayType` → `ERR_RestrictedType1` |
| 数组/静态/async 上下文 | `Binder_Statements.vb:1158/:1163/:1171` | `IsRestrictedArrayType` / `IsRestrictedType` → `ERR_RestrictedType1` / `ERR_CannotLiftRestrictedTypeResumable1` |
| 转换（装箱到 Object/ValueType） | `Binder_Conversions.vb:121/:248/:508` | `sourceType.IsRestrictedType()` → `ERR_RestrictedConversion1`；另 `:1571/:1582` 委托参数/返回 |
| lambda | `Binder_Lambda.vb:46/:107/:269/:285/:804/:948/:963` | `IsRestrictedType` / `IsRestrictedTypeOrArrayType` → `ERR_RestrictedType1` / `ERR_RestrictedResumableType1` |
| 匿名类型 | `Binder_AnonymousTypes.vb:32/:253` | `IsRestrictedTypeOrArrayType` → `ERR_RestrictedType1` |
| 泛型类型实参 | `ConstraintsHelper.vb:661` | `typeArgument.IsRestrictedType()` → `ERR_RestrictedType1` |
| 数组字面量元素 | `Binder_Expressions.vb:1608-1609` | `targetElementType.IsRestrictedType` → `ERR_RestrictedType1` |
| 其它参数 | `Binder_Utils.vb:1096-1104` | `paramType.IsRestrictedType` → `ERR_RestrictedType1` / `ERR_RestrictedResumableType1` |
| async/iterator 捕获 | `Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb:98/:113/:133` | `Not parameter.Type.IsRestrictedType()` / `Not local.Type.IsRestrictedType()` / `If type.IsRestrictedType()` |

### REPL 三碰撞点（改动点 4）

1. **脚本类字段（顶层持久化）**：模块级 `DimKeyword` → `Parser.vb:714`（`ParseSpecifierDeclaration`）；脚本类字段初始化绑定走 `BinderBuilder.vb:436-438`（`fieldOrProperty.ContainingType.IsScriptClass AndAlso Not TypeOf containingBinder Is TopLevelCodeBinder` → `New TopLevelCodeBinder(...)`）。
2. **结果装箱（打印）**：提交末尾表达式隐式转换到提交返回类型 `Object`——`Binder_Initializers.vb:211-220`（`:211` `Me.Compilation.IsSubmission AndAlso isLast AndAlso boundStatement.Kind = BoundKind.ExpressionStatement`；`:219` `submissionReturnType.IsObjectType()`；`:220` `ApplyImplicitConversion`）；落地 `Analysis\InitializerRewriter.vb:202-243`（`:202` `submissionResultType = method.ResultType`、`:205-223` 末尾非 Void 表达式 → `submissionResult`、`:242` `BoundReturnStatement`）。
3. **async 状态机**：`SynthesizedInteractiveInitializerMethod.vb:51-55`（`IsAsync` 恒 `True`）；`:166-170`（无显式 `ReturnTypeOpt` 时 `resultType = compilation.GetSpecialType(SpecialType.System_Object)`）。
4. **宿主打印**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:313-315`（`newScript.HasReturnValue()` 为真 → `globals.Print(state.ReturnValue)`）；`HasSubmissionResult`（`VisualBasicCompilation.vb:816-868`，`root.Members.LastOrDefault()` 判末尾于 `:833`）。

### 错误码（改动点 3 核实结果：全部已存在）

`Errors.vb` 已存在完整 restricted-type 族（与 BCX 同号），**无需新增**：

| 错误码 | 数值 | `Errors.vb` 行号 | 触发场景 |
|---|---|---|---|
| `ERR_RestrictedAccess` | 31393 | `:936` | 调用 ref-like 继承自 Object/ValueType 的实例方法（隐式装箱） |
| `ERR_RestrictedConversion1` | 31394 | `:937` | 转换到 Object/ValueType（装箱） |
| `ERR_RestrictedType1` | 31396 | `:939` | Nullable / 字段 / 数组元素 / 返回值 / ByRef 参数 / 匿名类型 / 转换目标等 |
| `ERR_ConstraintIsRestrictedType1` | 32061 | `:1133` | 受限/特殊类型作类型约束（`Of T As` 受限类型） |
| `ERR_CannotLiftRestrictedTypeQuery` | 36598 | `:1373` | LINQ 查询装箱 |
| `ERR_CannotLiftRestrictedTypeLambda` | 36640 | `:1425` | Lambda 闭包捕获 |
| `ERR_CannotLiftRestrictedTypeResumable1` | 37052 | `:1621` | async/iterator 状态机捕获 |

（另有 `ERR_RestrictedResumableType1 = 36932`，`Errors.vb:1560`。）

### C# 先例（跟随设定）

- **ref-like 栈约束**：`csharplang\proposals\csharp-7.2\span-safety.md:5`（confined to the execution stack）、`:262`（await/yield 处不得在作用域内）、`:266`（不能作字段，除 ref struct 内嵌 ref struct）、`:270-271`（不能装箱到 object/ValueType）。
- **CSX 持久化冲突**：`csharplang\meetings\2020\LDM-2020-02-26.md:46-56`（CSX 允许所有值持久化 → ref local 等非法）。
- **脚本顶层 = 字段**：`CSharp\Portable\Parser\LanguageParser.cs:8450-8456`（全局脚本层把 local-decls/local-funcs 解析为字段/方法）；C# 错误码 `ERR_FieldAutoPropCantBeByRefLike = 8345`（`CSharp\Portable\Errors\ErrorCode.cs:1533`）。

## 关键设计决策（源自 meeting RESOLUTION PROPOSAL A + proposal，设计文档必须吸收）

1. **PROPOSAL A 采纳**：byref-like 在 REPL/脚本**提交内可用、不跨提交持久化**。顶层 byref-like 变量（脚本类字段）、byref-like 结果（`?`/末尾表达式装箱到 `Object`）、跨顶层 `Await` 均编译错误；**方法体局部 / ByVal 值参数 / `allows ref struct` 接口消费可用**（返回值与 `ByRef` 参数 → BC31396）。
2. **PROPOSAL B 否决（语义不通）**：顶层 byref-like 作「提交内局部」两头落空——不持久 + 同提交方法访问不了（`ByRef` 传参又撞 BCX31396），与直接写在方法体内等价，且引入「顶层局部 vs 字段」双轨模型；不列独立增强。
3. **PROPOSAL C 否决**：不做 byref-like 结果特殊打印——值绑定栈上、不可持久，打印一个无法引用的类型名没有交互价值，且与「修错不算回归」（D4）精神相反。
4. **RestrictedType 判定细化**：ref-like = RestrictedType（沿用既有受限类型概念），**不引入用户可写的 `ref struct` 声明修饰符**；IDE tooltip 显示「**ByRef Like Structure**」修饰（如 `ByRef Like Structure Span(Of T)`）——**仅显示、不可声明**；显示格式由 VBX 侧定稿（VBX 是 fork 编译器可造显示约定；`RefStructHelper` 的 `<IsByRefLike>` 措辞不构成 VBX 约定）。
5. **`scoped`/`UnscopedRef` 非 VB 概念**：VBX 只消费不声明 ref struct，没有「写 `scoped`」的场景；ref-like 的逃逸/生命周期以编译器内部规则表达，`allows ref struct` 接口消费同以内部规则处理。
6. **两种模式同一编译器**：vbx（脚本/REPL）与常规 .vb 编译共用同一份 fork Roslyn VB 编译器，ref-like 安全规则**语义一致，不分裂**（`SourceCodeKind.Script` 同时服务 `.vbx` 与交互提交，`VisualBasicCompiler.vb:96`）。
7. **错误码复用 restricted-type 族**（BC31393/31394/31396/32061/36598/36640/37052），诊断**不区分 REPL/常规模式**，复用标准 restricted-type 错误文案即可。
8. **修错不算回归（D4）**：把「obsolete 错误 / 运行期 `InvalidProgramException`」变成「编译期 ref-safe 错误」= 修错，不构成回归，不影响 P1 地位。
9. **`allows ref struct` 例外依赖 M8 元数据识别（前置-2）**：当目标泛型约束为 C# 13 `allows ref struct` 时，ref-like 允许作该类型实参；此时仍不可装箱。本任务**不展开**该反约束实现，标注为依赖项。

## F1 验收条件（概要设计 pass 标准）

- 覆盖**现状缺口三点**（无 ref-like 概念 / RestrictedType 只盖三特殊类型 / 无 suppress obsolete）与**总体架构落点五步**（判定细化 → suppress obsolete → BCX 规则移植 → REPL 三碰撞点 → 显示）。
- 含 **REPL 行为对照表**：顶层 `Dim s As New Span(Of Integer)(1)` / `? New Span(Of Integer)(1)` / 末尾裸表达式 / 方法内 `Dim s As New Span` / ByVal 传参 / ByRef 传参 / `allows ref struct` 接口消费 / 跨顶层 Await。
- 说明判定原则（ref-like = RestrictedType、扩展谓词自动继承既有检查点）与 C# 先例跟随设定（span-safety / CSX 持久化 / 脚本顶层=字段）。
- 说明代价与边界（`.vbx` 与 REPL 同 kind 语义一致、修错不算回归、`.vbx` 顶层同样受限）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号区域 + 改动形状**（新属性/参数/分支），可被实施者直接照做。
- **改动点 1 判定细化**：`TypeSymbol.vb:587-592` `IsRefLikeType` 改为识别 `IsByRefLikeAttribute`；WellKnown 表**已存在** `IsByRefLikeAttribute`（`WellKnownTypes.cs:273`、`WellKnownMember.cs:476`）——核实结论写入；`SpecialTypeExtensions.vb:84-93` `IsRestrictedType` 从三特殊类型扩展到 `IsRefLikeType`。
- **改动点 2 suppress obsolete**：`PENamedTypeSymbol.vb:1455-1460` 附近，仿 C# `PENamedTypeSymbol.cs:998` 对 IsRefLikeType 类型跳过 obsolete 数据。
- **改动点 3 错误码核实**：逐码核对 `Errors.vb` 已存在（31393/31394/31396/32061/36598/36640/37052 全部在，无需补）。
- **改动点 4 REPL 三碰撞点落点**：顶层 byref-like Dim → 复用字段检查（`SourceMemberFieldSymbol.vb:142`）自动覆盖；结果装箱 → `Binder_Initializers.vb:211-220` 处 restricted 检查（或确认既有转换检查覆盖）；跨 Await → `IteratorAndAsyncCaptureWalker.vb`（:98/:113/:133 已用 `IsRestrictedType`，扩展谓词即覆盖）。
- **改动点 5 显示**：`SymbolDisplay\SymbolDisplayVisitor.Types.vb:89`（`VisitNamedType`）对 `IsRefLikeType` 追加「ByRef Like Structure」。
- **改动点 6 allows ref struct 反约束**：标注依赖前置-2（M8），不展开。
- 零改动清单 + 边界（`.vbx` 顶层受限、Regular 零影响证明）。

## F3 验收条件（测试计划 pass 标准）

- 分层测试矩阵（L1 语义层 / L2 REPL 层 / L3 显示层 / L4 Regular 模式），参考 C# ref struct 语义测试强度。
- 错误码矩阵覆盖装箱 / Nullable / 字段 / 数组元素 / 返回值 / ByRef 参数 / 泛型参数 / LINQ / Lambda / async 状态机。
- 既有测试更新清单 + 无副作用纪律（不网络/不写文件/不启动进程/不注册表；测不了就问用户）。
