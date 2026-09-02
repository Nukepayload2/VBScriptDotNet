# 任务：消费 ref readonly 返回（Consume Ref Readonly Returns，D4 → P1）设计任务

本文件夹是 `proposal-consume-ref-readonly` 的设计任务存储（Vortex 代办列表 + 设计产物）。本任务是「让 VB 消费 C# `ref readonly` 返回」的**设计拆分**：根因是元数据 `modreq([In])` 被 VB 导入层判「不支持」（BC30643/BC30657），设计要把它收敛为一个口子（豁免 required In modreq）并锁死写穿只读内存的安全底线（按接收方分类的写入语义）。

- **依据链**：`../../proposals/proposal-consume-ref-readonly.md`（Active/Proposed）→ `../../meetings/meeting-consume-ref-readonly.md`（RESOLUTION 九条，**唯一权威，plan 以 RESOLUTION 为准**）→ `../../decisions.md`（D1 只消费不声明 / D4 C# interop → **P1** / M3）→ `../../compilers-index.md`（编译器树索引）→ `../../spec/spec-byref-like-safety.md`（Byref enumerators 条目实现后需修订）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`）、测试计划（`test-plan.md`）。三份均要求无副作用测试矩阵（Restriction 触媒纪律）。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替，进度由任务列表驱动、不可催促。本任务是**设计先行**——设计文档是后续实现者的直接照做底稿。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

> 本任务覆盖**设计阶段（F1-F5）**；实现阶段（F9-Fn）待设计定稿后按 `design-detailed.md` 改动清单逐条再拆（实施者 + 验证者串行交替）。本代理一次性完成 F1-F5 的全部设计文档，状态先标 done，供验证者核对打回。

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`，待验证者核对） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`，待验证者核对） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`，待验证者核对） |
| F4 | 一致性审计 + 修复 + 复验 | F1/F2/F3 与 meeting RESOLUTION 九条、proposal、decisions、spec 交叉一致（无越权承诺、无遗漏决议吸收）；源码事实行号全部真实；写路径矩阵全覆盖 | **done**（由验证者核对打回） |
| F5 | 验证 README 源码事实 | README「共享源码事实」每条 `文件:行号` 经验证者 Read/Grep 复核；与源码不冲突 | **done**（验证者） |
| F9 | 实现：modreq(In) 白名单豁免（导入层） | 见 `design-detailed.md` 改动点 1 | **done**（F16 复核全绿） |
| F10 | 实现：只读标志 ReturnsByRefReadOnly（符号层 + 语义模型 + 一致性校验） | 见改动点 2 | **done**（F16 复核全绿） |
| F11 | 实现：直接赋值拒绝（复用 `ERR_LValueRequired` 30068 + 三写路径统一检查） | 见改动点 3 | **done**（F16 复核全绿） |
| F12 | 实现：ByRef 实参 copy-out 丢弃写回 | 见改动点 4 | **done**（F16 复核全绿） |
| F13 | 实现：With 块 readonly-lvalue 成员写拒绝（值捕获仅服务读） | 见改动点 5 | **done**（F16 复核全绿；复会 R8 2026-09-01 修正为拒绝语义） |
| F14 | 实现：For Each 解锁验证 + S24 翻转 + spec 修订 | 见改动点 6 | **done**（F16 复核全绿） |
| F15 | 实现：SymbolDisplay debug 格式（**首步验证「IDE 格式不含 IncludeRef」前提**——workspace/LSP 层，证伪则 30068 切 30064） | 见改动点 7 | **done**（F16 复核全绿） |
| F16 | 实现：全量测试收口（test-plan 四层矩阵全绿 + 无副作用纪律） | 见 test-plan | **done**（F16 实施者完成，待验证者核对） |

> 注：**编号为功能拆分标识，非执行顺序；执行顺序见 design-detailed §10（改4 先于改3）**。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-30，本代理逐条 Read 复核行号）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`），Core/C#/测试分别前缀 `Compilers\Core\Portable\` / `Compilers\CSharp\Portable\` / `Compilers\VisualBasicSemanticTest\` 等。文件相对路径均相对仓库根。

### 导入拒绝路径（改动点 1 的现状）

- **`VB\Symbols\Symbol.vb:1097-1124`** `DeriveUseSiteInfoFromCustomModifiers`：对 required modreq 报 `ERR_UnsupportedType1`，仅 `allowIsExternalInit` 豁免（`:1106-1107` 实锤：`If Not modifier.IsOptional AndAlso (Not allowIsExternalInit OrElse Not ...IsExternalInit())`）。**这是本提案的豁免落点。**
- **`VB\Symbols\Symbol.vb:1058`** `DeriveUseSiteInfoFromParameter`：`DeriveUseSiteInfoFromCustomModifiers(param.RefCustomModifiers)` ——**参数路径与返回路径共用同一函数**，这是「统一豁免 In」的结构基础。
- **`VB\Symbols\MethodSymbol.vb:700-711`** `CalculateUseSiteInfo`：`:701` `DeriveUseSiteInfoFromCustomModifiers(Me.RefCustomModifiers)`（返回 ref modreq 检查，无豁免）；`:707` 仅对 `ReturnTypeCustomModifiers` 传 `allowIsExternalInit:=IsInitOnly`。
- **`VB\Symbols\Metadata\PE\PEPropertySymbol.vb:122-125`**：属性签名 `propertyParams.Any(Function(p) p.RefCustomModifiers.AnyRequired() OrElse p.CustomModifiers.AnyRequired())` → `ERR_UnsupportedProperty1`。
- **`VB\Symbols\Symbol.vb:1008-1019`** `GetSymbolSpecificUnsupportedMetadataUseSiteErrorInfo`：级联成 `ERR_UnsupportedMethod1`/`ERR_UnsupportedProperty1`/`ERR_UnsupportedField1`。
- **`Compilers\Core\Portable\MetadataReader\MetadataDecoder.cs:1193-1198`** `DecodeParameterOrThrow`：`BYREF` 之前解出的 modifiers 赋给 `info.RefCustomModifiers`（modreq([In]) 在 BYREF 之前）。

### 签名剥离 + 自动解引用（读取路径零改动）

- **`VB\BoundTree\BoundPropertyAccess.vb:26`**：`isLValue:=propertySymbol.ReturnsByRef`。
- **`VB\BoundTree\BoundCall.vb:26`**：`isLValue:=method.ReturnsByRef`。
- **`VB\Symbols\Metadata\PE\PEPropertySymbol.vb:136-139`**：`_returnsByRef = returnInfo.IsByRef`、`_propertyType = returnInfo.Type`（绑定类型 = 元素类型）。
- **`VB\CodeGen\EmitExpression.vb:1179-1183`**：`useKind = UsedAsValue AndAlso method.ReturnsByRef` → `EmitLoadIndirect`（**「用到它的时候自动 dereference」早已存在**）。

### 写路径（改动点 3/4 的落点）

- **`VB\BoundTree\BoundAssignmentOperator.vb:56-61`**（DEBUG `Validate`）：ByRef-返回属性作赋值左值 → `AccessKind.Get`（store-through-ref，无副本可写）。
- **`VB\Binding\Binder_Statements.vb:1925-2011`** `AdjustAssignmentTarget`：**赋值目标统一检查点**；`:1936-1940` 对 `IsLValue`（ReturnsByRef）属性直接 `SetAccessKind(PropertyAccessKind.Get)`（store-through-ref 路径）；`:1946-1955` 不可写属性 → `ERR_NoSetProperty1`/`ERR_AssignmentInitOnly`。
- **`VB\Binding\Binder_Statements.vb:2031-2074`** `BindCompoundAssignment`：复合赋值 `s(0) += 5` → `:2044` 调 `AdjustAssignmentTarget` → `:2073` `New BoundAssignmentOperator(node, left, placeholder, right, ...)`。
- **`VB\Binding\Binder_Statements.vb:2230-2274`** Mid 赋值：`Mid(s(0), 1) = "x"` → `:2236` 调 `AdjustAssignmentTarget` → `:2271` `New BoundAssignmentOperator(node, target, placeholder, right, ...)`。
- **`VB\Binding\Binder_Invocation.vb:2881-2960`** 实参直传/copy-out：`:2887-2890` lvalue+identity **直传**（无 temp —— 对 readonly 来源会写穿，须跳过）；`:2892-2942` copy-out（`BoundByRefArgumentWithCopyBack`，`:2923` `copyBackExpression = BindAssignment(argument, ...)`）；`:2959` 非 lvalue → `PassArgumentByVal`（ByRef temp 由 codegen 分配，不写回 —— 与字面量传 ByRef 的丢弃写回同构）。
- **`VB\Lowering\LocalRewriter\LocalRewriter_Call.vb:249-383`** `RewriteByRefArgumentWithCopyBack`：`:303` `UseTwiceRewriter.UseTwice` 产出 firstUse（读）与 secondUse（写回）；`:319` firstUse 读入 temp；`:334-337` `copyBack = VisitAssignmentOperator(New BoundAssignmentOperator(secondUse, argument.OutConversion, ...))`；`:380` `copyBackArray.Add(copyBack)` —— **写回对只读来源省略 = 不生成 secondUse 且不 append**。
- **`VB\CodeGen\EmitAddress.vb:46-51`**：`AllowedToTakeRef` 为假 → `EmitAddressOfTempClone`；**:219-256** `HasHome`：`BoundKind.Call` 且 `method.ReturnsByRef` → 有 home。

### With 块（改动点 5 的落点）

- **`VB\Binding\Binder_WithBlock.vb:238-242`**：With 块 receiver 占位符选择——ByRef-返回 receiver 走 `BoundWithLValueExpressionPlaceholder`；非 lvalue 走 `BoundWithRValueExpressionPlaceholder`。**复会 R8（2026-09-01）落点**：LValue 分支加 `Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue()`（成员链版 helper，覆盖 `With o.S(0).Inner`），readonly-lvalue 改走 RValue placeholder，`.X` 落非 lvalue 自动复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068。
- **`VB\Lowering\WithExpressionRewriter.vb:321-368`** `CaptureWithExpression`：`:323` 注释「readonly reference cannot be stored in a temp, so do not capture in a ref」；`:324-326` `CaptureInAByRefTemp`（ByRef 捕获，`.Item = x` 会写穿）；`:337-347` 属性 draft-rewrite 走 `CaptureInATemp` 值捕获（`:347`）；`:349-359` 调用 draft-rewrite 走 `CaptureInATemp`（`:359`）。**值捕获路径已有，复会 R8 保留、专服务 `.Member` 读取。**

### 只读标志（改动点 2 的落点）

- **`VB\Symbols\MethodSymbol.vb:1090-1094`**：`IMethodSymbol_ReturnsByReadonlyRef` 硬编码 `False`；**:1096-1100** `IMethodSymbol_RefKind` 硬映射 `If(ReturnsByRef, Ref, None)`。
- **`VB\Symbols\PropertySymbol.vb:614-618`**：`IPropertySymbol_ByRefReturnIsReadonly` 硬编码 `False`；**:620-624** `IMethodSymbol_RefKind` 同映射 `Ref`。
- **`VB\Symbols\Metadata\PE\PEMethodSymbol.vb:1011-1015`** `ReturnsByRef = Signature.ReturnParam.IsByRef`；**:1031** `ReturnRefCustomModifiers = Signature.ReturnParam.RefCustomModifiers`（**只读标志的直接数据源**）。
- **`VB\Symbols\Metadata\PE\PEPropertySymbol.vb:283-287`** `ReturnsByRef = _returnsByRef`（`:138` 从 `returnInfo.IsByRef` 设定；`returnInfo = propertyParams(0)` 于 `:136`，其 `RefCustomModifiers` 即只读标志数据源）。
- **`VB\Symbols\ParameterSymbol.vb:304-310`**：`IParameterSymbol_RefKind = If(IsByRef, Ref, None)`（忽略 In/Out —— §4a v1 不做的锚点）。

### 显示（改动点 7 的落点）

- **`VB\SymbolDisplay\SymbolDisplayVisitor.Members.vb:81-85`**：`If symbol.ReturnsByRef AndAlso Format.MemberOptions.IncludesOption(IncludeRef)` → `AddKeyword(ByRefKeyword)` + `AddCustomModifiersIfRequired`。无 readonly 显示机制。
- **`VB\SymbolDisplay\SymbolDisplayVisitor.Types.vb:445-447`**：ref-like **类型** 显示「ByRef Like Structure」**已存在**（byref-like-safety 任务落地）——本任务不改类型显示，只对照说明 ref readonly 返回的显示策略与其不同。

### For Each（改动点 6 的落点）

- **`VB\Binding\Binder_Statements.vb:4291-4299`** `s_isReadablePropertyWithoutArguments`：`Current` 谓词只查 `IsReadable AndAlso Not IsGenericMethod AndAlso GetCanBeCalledWithNoParameters`，**无「不得 ByRef 返回」排除**。
- **`VB\Lowering\LocalRewriter\LocalRewriter_ForEach.vb:307-320`**：`CurrentPlaceholder` → `boundCurrent` 以 RValue 替换进 `controlVariable = Current`（`BoundAssignmentOperator`），值读走 auto-deref。
- **`Compilers\VisualBasicSemanticTest\Semantics\ByRefLikeTests.vb:537-560`**（S24）：**已翻转（F14 落地）**——从「期望 `ERR_UnsupportedProperty1` + `System.ReadOnlySpan(Of T).Enumerator.Current`」改为 `AssertNoDiagnostics` + `CompileAndVerify` 枚举 `a`/`b`；注释更新为说明 modreq(In) 豁免（改1）+ For Each `Current` 谓词只查 `IsReadable`（`Binder_Statements.vb:4327-4335`）+ RValue 值读（`EmitExpression.vb:1179-1180` auto-deref）。
- **`InternalDevDocs\spec\spec-byref-like-safety.md:228-230`**：Byref enumerators 条目「VB does not support properties that return by reference」表述不精确（纯 ref 已可消费），实现后修订。

### C# 参照（跟随设定）

- **`Compilers\CSharp\Portable\Symbols\Metadata\PE\PEPropertySymbol.cs:363-367`**：属性导入白名单 `!m.Modifier.IsWellKnownTypeInAttribute()` —— **C# 已豁免 In modreq 的既有结构**，VB 对齐而非发明。
- **`Compilers\CSharp\Portable\Symbols\Source\CustomModifierUtils.cs:158-161`**：`HasInAttributeModifier()`。
- **`Compilers\CSharp\Portable\Symbols\Metadata\PE\PEParameterSymbol.cs:415-427`**：一致性契约（RefReadOnly 返回应恒带 modreq(In)，不一致判 bad）。
- **`Compilers\CSharp\Portable\Symbols\Metadata\PE\PEParameterSymbol.cs:287-307`**：in 参数识别实际映射 `[IsReadOnly]→In`、`[RequiresLocation]→RefReadOnlyParameter`、`[Out]→Out`（§4a 后续用）。
- **`Compilers\CSharp\Portable\Symbols\Symbol.cs:1261-1267`**：参数 RefCustomModifiers 允许 `System_Runtime_InteropServices_InAttribute`。
- **`Compilers\CSharp\Portable\Symbols\Symbol.cs:1293-1354`**：`AllowedRequiredModifierType` 白名单枚举结构（VB 白名单的模板）。
- **`Compilers\CSharp\Portable\Symbols\Source\SourceOrdinaryMethodSymbol.cs:131`**：`addRefReadOnlyModifier: IsVirtual || IsAbstract` —— 虚/抽象成员 `in` 参数带 modreq(In)。
- **`Compilers\CSharp\Portable\Symbols\Source\SourceDelegateMethodSymbol.cs:277`**：`addRefReadOnlyModifier: true` —— 委托恒发 modreq(In)。
- C# **CS8331**（上游 `Binder.ValueChecks.cs:1970-1974`，「Cannot assign to … readonly variable」）——30068 复用方向的文本参照；本树 CSharp 裁剪无该文件，仅作文本参照。
- **`Compilers\Core\Portable\WellKnownTypes.cs:274`**：`System_Runtime_InteropServices_InAttribute` 枚举已存在（`:651` 元数据名）——无需新增 WellKnown 条目。

### 错误码现状

- **`VB\Errors\Errors.vb:164`** `ERR_ReadOnlyProperty1 = 30098`（**不复用**：`s(0)` 的 PropertySymbol 非 `ReadOnly` 声明，复用会误导 + 已死码）；`:495` `ERR_UnsupportedProperty1 = 30643`（BC30643，**不复用**：语义是元数据拒绝，层级不同）；`:499` `ERR_UnsupportedType1 = 30649`；`:511` `ERR_UnsupportedMethod1 = 30657`（BC30657，**不复用**）。
- **复用 `ERR_LValueRequired`(30068)**（「Expression is a value and therefore cannot be the target of an assignment.」）：**零新码**——延续 30098→30068 既有迁移（`BindingErrorTests.vb:2335` 注释 `' change error 30098 to 30068`、WorkItem 538107）、`ReportAssignmentToRValue`（`Binder_Expressions.vb:1795-1809`）活实践；**显式否决 30064**（`'ReadOnly' variable` 对 `s(0)` 措辞失准）；与 R5 显示退化耦合同批落地（若 IDE 含 `IncludeRef` 前提证伪，一行切 30064，`Binder_Expressions.vb:1805`）。

## 关键设计决策（源自 meeting RESOLUTION 九条，设计文档必须吸收）

1. **采纳核心机制（R1）**：豁免 required `modreq(In)` 导入 + `ReturnsByRefReadOnly` 只读追踪 + 按接收方分类写入语义（直接赋值拒 / ByRef 实参 copy-out 传副本 / With、推断褪 ByRef），**不引入声明语法**。D4 → **P1**。
2. **modreq 豁免范围（R2）**：**统一豁免 required In modreq（返回 + 参数双路径）**，落点 `Symbol.vb:1106-1108` 按 **attribute 身份（`InAttribute`）** 加入良性白名单（**不做「任何 required modreq 都豁免」**，不破坏既有纪律）；顺带解锁虚方法/委托 `in` 参数调用；`Out` 本次不豁免。
3. **直接赋值拒绝（R3）**：**复用 `ERR_LValueRequired`(30068)**（零新码；延续 30098→30068 迁移 `BindingErrorTests.vb:2335`；显式否决 `ERR_ReadOnlyAssignment` 30064——`'ReadOnly' variable` 对 `s(0)` 措辞失准），不复用 `ERR_UnsupportedProperty1/Method1` 与 `ERR_ReadOnlyProperty1`。**拒绝面 = 所有 store-through-ref 写路径**：直接赋值 `s(0) = 5`、复合赋值 `s(0) += 5`、`Mid` 赋值——三者统一检查（`AdjustAssignmentTarget` 处 LHS 为 readonly-lvalue 即拒，且不送 30064 variable 分支）。**与 R5 显示退化耦合同批落地**（前提证伪一行切 30064，`Binder_Expressions.vb:1805`）。
4. **丢弃写回（R4）**：接受（VB copy-out 文化，与字面量传 ByRef 一致），**不警告**；spec 显式声明「写回被丢弃，等价于传常量 ByRef」为硬性文档义务；「callee 实际写副本但写回被丢弃」的可选诊断位（default 关闭）留实现期评估，不阻塞。
5. **只读标志暴露（R5）**：暴露 `ReturnsByRefReadonly`/`RefKind.RefReadOnly` 到符号层 + 语义模型（填既有 `IMethodSymbol_ReturnsByRefReadonly`/`IPropertySymbol_ReturnsByRefReadonly`），供分析器/工具读真实语义 + 一致性校验；**SymbolDisplay 合成 `ByRef ReadOnly` 仅限纯 debug/内部诊断格式，IDE 显示格式不带**（tooltip 退化形态）；不引入 VB 源语法。仿 C# `PEParameterSymbol.cs:415-421` 加一致性校验。
6. **For Each 解锁（R6）**：通过（实现期验证）。落地后更新 `ByRefLikeTests.vb:538`（S24）从期望报错改期望通过，修订 `spec-byref-like-safety.md:228-230`。
7. **§4a readonly 参数识别（R7）**：v1 不做（copy-out 保底正确），列为后续优化。
8. **`With` 块写穿（R8，复会 2026-09-01 修正）**：readonly-lvalue 接收器在 With 块内的成员写 `.X = 5` 与链式 `o.S(0).X = 5` 是同一成员写，判 **BC30068 拒绝**（落点 `Binder_WithBlock.vb:238-242` 占位符选择加 `Not IsReadOnlyLValueOrMemberOfReadOnlyLValue()`，readonly-lvalue 走 `BoundWithRValueExpressionPlaceholder`，复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068；`AdjustAssignmentTarget` 不改）；**值捕获保留，专服务 `.Member` 读取**（`With o.S(0) : Dim y = .X` 合法）；测试 S13b/S13c/S13e 翻转、S12/S13d/S13f 读取保持无诊断。
9. **范围修正（R9）**：`ref readonly` **返回**是 C# 7.2（`readonly-ref.md`），非 C# 12；`Binder\` 路径笔误修正为 `Binding\`。

## F1 验收条件（概要设计 pass 标准）

- 覆盖现状缺口（BC30643/BC30657 实证：`"abc".AsSpan()(0)` 报 BC30643、`.GetPinnableReference()` 报 BC30657、纯 `ref` 全通）与总体架构落点（导入豁免 → 只读标志 → 写入分类 → With/For Each → 显示）。
- 含 **RESOLUTION 九条吸收映射表**：逐条给出「决议内容 → 落在本设计哪节」。
- 说明按接收方分类写入语义（直接赋值拒 / ByRef 实参 copy-out 传副本 / With、推断褪 ByRef）与 C# 先例跟随设定（`PEPropertySymbol.cs:363-367` 白名单、CS8331、VB copy-out 丢写回文化）。
- 说明代价与边界（不引入声明语法、不加 scoped/UnscopedRef、`.vbx` 与 Regular 同一编译器、修错不算回归 D4）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号区域 + 改动形状**（新属性/参数/分支），可被实施者直接照做。
- **改动点 1 导入豁免**：`Symbol.vb:1106-1108` 按 `InAttribute` 身份加入白名单（新 `allowInModifier` 参数或仿 C# `AllowedRequiredModifierType` 枚举）；`MethodSymbol.vb:701`（返回）与 `Symbol.vb:1058`（参数）双路径传参；`PEPropertySymbol.vb:124` 排除 In modreq；新增 `IsWellKnownTypeInAttribute` + `HasInAttributeModifier` helper；`Out` 不豁免。
- **改动点 2 只读标志**：`MethodSymbol`/`PropertySymbol` 基类新增 `ReturnsByRefReadOnly` 默认 `False`；填 `IMethodSymbol_ReturnsByReadonlyRef`（`:1090-1094`）/`IPropertySymbol_ByRefReturnIsReadonly`（`:614-618`）与 `RefKind`（`:1096-1100`/`:620-624`）；`PEMethodSymbol.vb:1011-1015`/`PEPropertySymbol.vb:136-140` 从 `RefCustomModifiers` 判 modreq(In)；仿 `PEParameterSymbol.cs:415-421` 一致性校验。
- **改动点 3 直接赋值拒绝**：复用 `ERR_LValueRequired`(30068)；统一检查点 `AdjustAssignmentTarget`（`Binder_Statements.vb:1936-1940`）对 readonly-lvalue 报 30068（且不送 30064 variable 分支）；复合赋值（`:2073`）与 Mid（`:2271`）经同一检查点自动覆盖；编译器生成的 copy-back（`Binder_Invocation.vb:2923`）不误报（改 4 先于改 3）。
- **改动点 4 ByRef 丢弃写回**：`Binder_Invocation.vb:2881-2892` 对 readonly-lvalue 跳过直传改走 copy-out；`LocalRewriter_Call.vb:249-383` 对 readonly 来源省略写回（不生成 secondUse、不 append `copyBackArray`）。
- **改动点 5 With 块拒绝**：`Binder_WithBlock.vb:238-242` 占位符选择加 `Not IsReadOnlyLValueOrMemberOfReadOnlyLValue()`（成员链版 helper），readonly-lvalue 接收器改走 `BoundWithRValueExpressionPlaceholder`、`.X` 落非 lvalue 复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068；`WithExpressionRewriter.vb:324-359` 值捕获保留、专服务 `.Member` 读取。
- **改动点 6 For Each**：实现期验证 `Binder_Statements.vb:4291-4299`/`LocalRewriter_ForEach.vb:307-320`；更新 S24 + spec。
- **改动点 7 显示**：`SymbolDisplayVisitor.Members.vb:81-85` 仅纯 debug 格式追加 `ReadOnly`。
- 零改动清单 + 边界（`EmitExpression.vb:1179-1183` auto-deref 零改动、`ParameterSymbol.vb:308` §4a 不做、`Errors.vb` 复用纪律、`WellKnownTypes.cs` 零新增）。

## F3 验收条件（测试计划 pass 标准）

- 分层测试矩阵（L1 语义 `VisualBasicSemanticTest` / L2 REPL `Scripting\VisualBasicTest` / L3 显示 `VisualBasicSymbolTest` / L4 Regular），写路径矩阵**必须覆盖**：直接赋值/复合赋值/Mid/ByRef 实参（可变/readonly 接收方）/With 块/成员链/For Each/类型推断/表达式 lambda。
- VB 特性维度（`ReadOnlySpan` 索引器读取、`GetPinnableReference`、For Each 枚举、`in` 实参 copy-out、With 块、Option Strict、REPL `.vbx` 同 kind）。
- 既有测试更新清单（S24 翻转、`spec-byref-like-safety.md:228-230`、错误码矩阵对照）+ 无副作用纪律（不网络/不写文件/不启动进程/不注册表；REPL 用例用内存 `StringReader`/`StringWriter`）。
