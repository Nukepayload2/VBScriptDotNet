# 详细设计：byref-like 类型安全（ref struct 支持，D1 前置-1）

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-byref-like-safety.md`（Active/Proposed，Detailed design §1-§6）→ `../../meetings/meeting-byref-like-repl-safety.md`（RESOLUTION PROPOSAL A）→ `design-overview.md`（F1 概要，已通过）。
> 本设计把概要落到「可被 D1 实施者直接照做」的代码级设计。
> 源码事实均已用内置 Read/Grep 精读核实，引用以 `文件:行号` 给出；文件相对路径均相对仓库根，编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`）。

## 0. 核心判定原则（贯穿全文）

**byref-like 类型（C# `ref struct`）在 VB 侧一律视为 RestrictedType**（沿用既有受限类型概念），通过**细化 RestrictedType 判定**使其与 C# 的 ref-like 规则接近。本设计的核心观察是：

> **VB 编译器已有完整的 restricted-type 检查点（字段 / 返回 / 数组 / 转换 / lambda / 匿名类型 / 泛型约束 / async 捕获），它们全部经 `IsRestrictedType()` / `IsRestrictedTypeOrArrayType()` 谓词。REPL 三碰撞点（脚本类字段 / 结果装箱 / 跨 Await）全部落在既有检查点的覆盖范围内。因此把 `IsRestrictedType()` 的判定从「三个特殊类型」扩展到「所有 `IsRefLikeType`」，即可自动继承全部检查点——本任务的代码改动集中在四处：判定细化、suppress obsolete、显示、WellKnown 核实（零新增），外加测试验证。**

---

## 1. 改动清单总览

| 文件（相对仓库根） | 改动函数 | 改动形状 | 目的 |
|---|---|---|---|
| `VB\Symbols\TypeSymbol.vb` | 新增 `Friend Overridable ReadOnly Property IsRefLikeType`；`ITypeSymbol_IsRefLikeType`（:587-592） | 新增属性 + 修改 | 让 ref-like 判定可被派生覆盖；默认 `False`（Regular 与源类型零影响） |
| `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb` | 新增 `IsRefLikeType` 覆盖（仿 C# :2886-2912）；修改 `ObsoleteAttributeData`（:1455-1460） | 新增覆盖 + 修改 | ① PE 类型读 `IsByRefLikeAttribute`；② 对 ref-like 类型 suppress obsolete（改动点 2） |
| `VB\Symbols\TypeSymbolExtensions.vb` | `IsRestrictedType`（:365-367） | 修改 | 从「`SpecialType.IsRestrictedType()`」扩展为「`IsRefLikeType OrElse SpecialType.IsRestrictedType()`」（改动点 1 核心） |
| `VB\SymbolDisplay\SymbolDisplayVisitor.Types.vb` | `AddTypeKind`（:435-453） | 修改 | 对 `IsRefLikeType` 的 Struct 类型追加「ByRef Like Structure」修饰（改动点 5） |
| 其余（既有检查点 / 转换分类器 / REPL 三碰撞点 / 宿主层） | — | **零改动** | 谓词扩展自动覆盖；依据见 §5、§8 |

> **改动点 3（错误码）核实结论：无改动**。`Errors.vb` 已存在全部 7 个 restricted-type 错误码（31393/31394/31396/32061/36598/36640/37052），见 §4。
> **改动点 6（`allows ref struct` 反约束）：不展开**，依赖 M8 元数据识别（前置-2），见 §7。

---

## 2. 改动点 1：判定细化（RestrictedType 判定覆盖所有 ref-like）

### 2.1 现状（源码实证）

- `VB\Symbols\TypeSymbol.vb:587-592`：`ITypeSymbol_IsRefLikeType` 硬编码 `Return False`，注释「VB has no concept of ref-like types」（`:589`）。该属性是显式接口实现，**不可被派生覆盖**（VB 无 `Protected/Friend Overridable` 中间属性）。
- `VB\Symbols\SpecialTypeExtensions.vb:84-93`：`IsRestrictedType(this As SpecialType)` 只盖 `TypedReference` / `ArgIterator` / `RuntimeArgumentHandle`。
- `VB\Symbols\TypeSymbolExtensions.vb:365-367`：`IsRestrictedType(this As TypeSymbol)` 委托 `this.SpecialType.IsRestrictedType()`；`:380-392` `IsRestrictedTypeOrArrayType` 剥数组后判 `IsRestrictedType()`。
- **WellKnown 核实结论（关键）**：`IsByRefLikeAttribute` 的 WellKnown 条目**已存在，无需新增**——`Core\Portable\WellKnownTypes.cs:273`（枚举 `System_Runtime_CompilerServices_IsByRefLikeAttribute`）、`:650`（元数据名 `"System.Runtime.CompilerServices.IsByRefLikeAttribute"`）、`Core\Portable\WellKnownMember.cs:476`（`System_Runtime_CompilerServices_IsByRefLikeAttribute__ctor`）、`Core\Portable\MetadataReader\PEModule.cs:1235-1238`（`HasIsByRefLikeAttribute(EntityHandle)` 读属性）。
- C# 原型：`CSharp\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2886-2912`——`IsRefLikeType` 覆盖在 `TypeKind = Struct` 时 `isByRefLike = module.HasIsByRefLikeAttribute(_handle).ToThreeState()`。

### 2.2 改动形状

**(a) `VB\Symbols\TypeSymbol.vb`——基类引入可覆盖属性**

在 `TypeSymbol` 基类（`Symbols\TypeSymbol.vb`）新增：

```vb
Friend Overridable ReadOnly Property IsRefLikeType As Boolean
    Get
        ' VB 源类型无 ref struct 声明语法，默认不是 ref-like；PE 类型覆盖此属性读 IsByRefLikeAttribute。
        Return False
    End Get
End Property
```

`ITypeSymbol_IsRefLikeType`（:587-592）改为：

```vb
Private ReadOnly Property ITypeSymbol_IsRefLikeType As Boolean Implements ITypeSymbol.IsRefLikeType
    Get
        Return Me.IsRefLikeType
    End Get
End Property
```

- 命名与访问级别：`Friend Overridable`（VB 编译器内部符号访问；若需被 `TypeSymbolExtensions` 跨程序集可见则为 `Public`/`Friend`——`TypeSymbolExtensions` 与 `TypeSymbol` 同程序集，`Friend` 即可）。
- **Regular 零影响**：基类默认 `False`；源类型符号（`SourceNamedTypeSymbol` 等）不覆盖 → 判定仍为 `False`。

**(b) `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb`——PE 类型覆盖**

仿 C# `PENamedTypeSymbol.cs:2886-2912`，在 `PENamedTypeSymbol`（`Symbols\Metadata\PE\PENamedTypeSymbol.vb`，类于 `:24`，`_handle` 于 `:33`）新增：

```vb
Friend Overrides ReadOnly Property IsRefLikeType As Boolean
    Get
        If Not _lazyIsByRefLike.HasValue Then
            Dim isByRefLike = False
            If TypeKind = TypeKind.Struct Then
                isByRefLike = ContainingPEModule.Module.HasIsByRefLikeAttribute(_handle)
            End If
            _lazyIsByRefLike = isByRefLike
        End If
        Return _lazyIsByRefLike
    End Get
End Property
```

- `_lazyIsByRefLike As Boolean? = Nothing` 新增为字段（仿 `_lazyObsoleteAttributeData` 于 `:90` 的惰性模式）。
- **包装符号委托**：若 `IsRestrictedType()` 的查询路径经过 `RetargetingNamedTypeSymbol`（`Symbols\Retargeting\RetargetingNamedTypeSymbol.vb:26`）或 `WrappedNamedTypeSymbol`（`Symbols\Wrapped\WrappedNamedTypeSymbol.vb:19`），实现期确认是否需要在这些包装符号上委托底层 `IsRefLikeType`（C# 侧 `RetargetingNamedTypeSymbol` 未显式覆盖，默认继承——实现期按实测调用链决定）。**这是实现期检查点，不是本文改动项**。

**(c) `VB\Symbols\TypeSymbolExtensions.vb`——核心一行改动**

`IsRestrictedType(this As TypeSymbol)`（:365-367）改为：

```vb
<Extension()>
Public Function IsRestrictedType(this As TypeSymbol) As Boolean
    ' RestrictedType = ref-like（IsRefLikeType）+ 三个遗留特殊类型（TypedReference/ArgIterator/RuntimeArgumentHandle）。
    Return this.IsRefLikeType OrElse this.SpecialType.IsRestrictedType()
End Function
```

- **为何主落点在 `TypeSymbol` 层而非 `SpecialType` 层**：`SpecialType` 枚举只覆盖 well-known 类型，任意用户定义 ref struct 的 `SpecialType = None`——`SpecialType` 层扩展覆盖不到。`SpecialTypeExtensions.vb:84-93` **保留三特殊类型检查**作为遗留兜底（`TypedReference` 等不是 `IsByRefLike` 属性标记的 ref struct，属独立受限机制）。任务 README/提案中「`SpecialTypeExtensions.vb:84-93` 从三特殊类型扩展到 `IsRefLikeType`」的意图由此达成（判定覆盖所有 ref-like），落点按 TypeSymbol 层更正确。
- `IsRestrictedTypeOrArrayType`（:380-392）剥数组后调 `IsRestrictedType()`，自动覆盖「数组元素为 ref-like」。

### 2.3 谓词扩展后自动继承的检查点（零改动，供实施者回归锚点）

| 检查点 | 文件:行号 | 现在对 `Span(Of Integer)` 的行为 |
|---|---|---|
| 字段（含脚本类字段） | `SourceMemberFieldSymbol.vb:142-143` | BC31396 |
| 数组元素 | `Binder_Statements.vb:1158` | BC31396 |
| 数组/静态/async 上下文 | `Binder_Statements.vb:1158/:1163/:1171` | BC31396 / BC37052 |
| 转换（装箱） | `Binder_Conversions.vb:508-513`（`ApplyConversion`） | BC31394 |
| 直接转换 CType | `Binder_Conversions.vb:121-125`（`ApplyDirectCastConversion`） | BC31394 |
| TryCast | `Binder_Conversions.vb:248-251`（`ApplyTryCastConversion`） | BC31394 |
| 转换分类器（禁装箱） | `Conversions.vb:3390-3398`（`ClassifyValueTypeConversion`） | `NoConversion`（`Not source.IsRestrictedType()` 守卫跳过 `WideningValue` 路径） |
| lambda | `Binder_Lambda.vb:46/:107/:269/:285/:804/:948/:963` | BC31396 / BC36932 |
| 匿名类型 | `Binder_AnonymousTypes.vb:32/:253` | BC31396 |
| 泛型类型实参 | `ConstraintsHelper.vb:661` | BC31396 |
| 数组字面量元素 | `Binder_Expressions.vb:1608-1609` | BC31396 |
| 参数（ByRef 等） | `Binder_Utils.vb:1096-1104` | BC31396 |
| async/iterator 捕获 | `Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb:98/:113/:133` | BC37052 |

> `Conversions.vb:3393` 是**转换分类器层**的禁装箱守卫（`ClassifyValueTypeConversion`）——这是「ref-like 不可装箱」在转换计算层的真正拦截点：ref-like → Object/ValueType 直接返回 `NoConversion`，随后 `ApplyConversion`（`Binder_Conversions.vb:508`）在 `NoConversion` 分支报 BC31394。**这两层叠加保证「装箱」被完整拦截**。

---

## 3. 改动点 2：编译器层 suppress ref struct obsolete error

### 3.1 现状（源码实证）

- **真正的机制**：BC30668（`ERR_UseOfObsoleteSymbol2`）来自 `VB\Symbols\ObsoleteAttributeHelpers.vb:33-39`（`GetObsoleteDataFromMetadata`）→ `Core\Portable\MetadataReader\PEModule.cs:1249-1275`（`TryGetDeprecatedOrExperimentalOrObsoleteAttribute`）。类型带 `[Obsolete("Types with embedded references are not supported...", true)]`（`ByRefLikeMarker`，`PEModule.cs:1245`）时，仅当 `ignoreByRefLikeMarker=True` 才返回 `Nothing`（`:1269-1270`）。VB 现硬编码 `ignoreByRefLikeMarker:=False`（`ObsoleteAttributeHelpers.vb:36`）→ ref-like 类型报 obsolete。
- **C# 正确原型**：`CSharp\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.cs:3028-3029`——`bool ignoreByRefLikeMarker = this.IsRefLikeType;` 传入 C# 版 `ObsoleteAttributeHelpers.InitializeObsoleteDataFromMetadata`（C# helper 有 `ignoreByRefLikeMarker` 形参）。仅**类型级**传 `IsRefLikeType`；方法/字段/属性/事件传 `False`（`PEMethodSymbol.cs:1703` / `PEFieldSymbol.cs:722` / `PEPropertySymbol.cs:1101` / `PEEventSymbol.cs:543`）。
- VB 侧缺失：`ObsoleteAttributeHelpers.vb` 的 `InitializeObsoleteDataFromMetadata`/`GetObsoleteDataFromMetadata` 无 `ignoreByRefLikeMarker` 形参，硬编码 `False`。

### 3.2 改动形状

**(a) `VB\Symbols\ObsoleteAttributeHelpers.vb`——加 `ignoreByRefLikeMarker` 形参**

`InitializeObsoleteDataFromMetadata`（:26）与 `GetObsoleteDataFromMetadata`（:33）各加 `ignoreByRefLikeMarker As Boolean` 形参；`:36` 的硬编码 `ignoreByRefLikeMarker:=False` 改为传参 `ignoreByRefLikeMarker:=ignoreByRefLikeMarker`；`:35` 注释同步更新。

**(b) 5 个调用点传参**

| 调用点 | 传值 |
|---|---|
| `PENamedTypeSymbol.vb:1457`（类型级） | `ignoreByRefLikeMarker:=Me.IsRefLikeType` |
| `PEMethodSymbol.vb:1316` | `ignoreByRefLikeMarker:=False` |
| `PEPropertySymbol.vb:345` | `ignoreByRefLikeMarker:=False` |
| `PEFieldSymbol.vb:320` | `ignoreByRefLikeMarker:=False` |
| `PEEventSymbol.vb:255` | `ignoreByRefLikeMarker:=False` |

- **为何不用旧版「`ObsoleteAttributeData` 返回 `Nothing`」**：那会把 ref-like 类型上**合法的用户 `[Obsolete]`** 一并 suppress（C# 只 suppress `ByRefLikeMarker` 一种 marker，保留用户自定义 obsolete）。`ignoreByRefLikeMarker` 形参法精确对齐 C#。
- **实现期检查点**：C# `PENamedTypeSymbol.cs:998-1010` 还有 `filterObsoleteAttribute = IsRefLikeType && ObsoleteAttributeData is null` + `filterIsByRefLikeAttribute = IsRefLikeType`（在 `GetAttributes()` 过滤 `[Obsolete]`/`[IsByRefLike]` 属性本身）。VB 侧若 `GetAttributes()` 把 `[IsByRefLike]`/marker `[Obsolete]` 暴露为符号属性，实现期按需对齐；不影响 suppress obsolete 主效果（BC30668 走 `ObsoleteAttributeData` 路径）。

---

## 4. 改动点 3：错误码核实（无改动）

逐码核对 `Errors.vb`，**全部已存在**（与 BCX 同号），无需新增：

| 错误码 | 数值 | `Errors.vb` 行号 |
|---|---|---|
| `ERR_RestrictedAccess` | 31393 | `:936` |
| `ERR_RestrictedConversion1` | 31394 | `:937` |
| `ERR_RestrictedType1` | 31396 | `:939` |
| `ERR_ConstraintIsRestrictedType1` | 32061 | `:1133` |
| `ERR_CannotLiftRestrictedTypeQuery` | 36598 | `:1373` |
| `ERR_CannotLiftRestrictedTypeLambda` | 36640 | `:1425` |
| `ERR_CannotLiftRestrictedTypeResumable1` | 37052 | `:1621` |

（另有 `ERR_RestrictedResumableType1 = 36932`，`Errors.vb:1560`。）

**测试锚点**：`ERR_RestrictedType1` 的完整消息文本（既有测试实证，`Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:13619`）为：

> `'TypedReference' cannot be made nullable, and cannot be used as the data type of an array element, field, anonymous type member, type argument, 'ByRef' parameter, or return statement.`

`Span(Of Integer)` 场景断言用同一模板（类型名替换）。既有测试：`BindingErrorTests.vb:13522`（BC31394）、`:13601/:13638/:13683`（BC31396）、`:17527`（BC36640）、`GenericsTests.vb:245`（BC31396）。

---

## 5. 改动点 4：REPL 三碰撞点落点（零新增检查）

> **核心结论**：REPL 三碰撞点全部由「扩展 `IsRestrictedType()` 谓词」**自动覆盖**，不需要在 REPL 路径新增任何检查。逐条论证如下，实施者只需用测试验证（见 test-plan §2/L2）。

### 5.1 脚本类字段（顶层 byref-like `Dim` → BC31396）

- 机制：顶层模块级 `DimKeyword` → `Parser.vb:714`（`ParseSpecifierDeclaration`）；脚本类字段初始化绑定走 `BinderBuilder.vb:436-438`（`fieldOrProperty.ContainingType.IsScriptClass AndAlso Not TypeOf containingBinder Is TopLevelCodeBinder` → `New TopLevelCodeBinder(...)`）。
- 覆盖：脚本类字段仍是 `SourceMemberFieldSymbol`，其声明期错误检查 `SourceMemberFieldSymbol.vb:142-143`（`varType.IsRestrictedTypeOrArrayType` → `ERR_RestrictedType1`）对 `Dim s As New Span(Of Integer)(1)` 直接报 **BC31396**。语义锚定「ref-like 不能作类的字段」（对齐 C# `span-safety.md:266` / `ERR_FieldAutoPropCantBeByRefLike`，`ErrorCode.cs:1533`）。
- **零新增**：字段检查是既有位置，谓词扩展即覆盖。

### 5.2 结果装箱（`? New Span(Of Integer)(1)` / 末尾裸表达式 → BC31394）

- 机制：提交末尾表达式隐式转换到提交返回类型 `Object`——`Binder_Initializers.vb:211-220`（`:219` `submissionReturnType.IsObjectType()`、`:220` `expression = ApplyImplicitConversion(expression.Syntax, submissionReturnType, expression, diagnostics)`）；落地 `Analysis\InitializerRewriter.vb:202-243`（`:202` `submissionResultType = method.ResultType`、`:242` `BoundReturnStatement`）。
  - **`? expr`（PrintStatement）同样走此路径**：`BindPrintStatement`（`Binder_Statements.vb:2735-2738`）返回 `New BoundExpressionStatement(printStmt, boundExpression)`——bound 形态是 `BoundKind.ExpressionStatement`，语法节点仍是 `PrintStatementSyntax`。因此 `Binder_Initializers.vb:211` 的 `boundStatement.Kind = BoundKind.ExpressionStatement` 对 `? expr` 为真，`:220` 的 `ApplyImplicitConversion` 同样执行 → `? New Span(Of Integer)(1)` 的结果装箱在 `Binder_Conversions.vb:508` 报 BC31394。
- 覆盖（两层叠加）：
  1. `ApplyImplicitConversion`（`Binder_Conversions.vb:307-315`）→ `ApplyConversion`（`:320`）→ 在 `:507-513`：`If Conversions.NoConversion(convKind.Key) Then If sourceType.IsValueType AndAlso sourceType.IsRestrictedType() AndAlso (targetType.IsObjectType() OrElse targetType.SpecialType = System_ValueType) Then ReportDiagnostic(ERR_RestrictedConversion1)`——**前提是 `NoConversion` 为真**；
  2. `NoConversion` 由 `Conversions.vb:3376` `ClassifyValueTypeConversion` 保证：`:3390-3398` `If IsValueType(source) Then If Not source.IsRestrictedType() Then ... Return ConversionKind.WideningValue`——ref-like（RestrictedType）**不进装箱路径**，落空后函数返回 `NoConversion`。
- 结论：ref-like → Object 是 `NoConversion`，`ApplyConversion:508` 报 **BC31394**。**零新增**；但必须用测试确认「ref-like → Object 被判 NoConversion」（实现期检查点：`Conversions.vb:3393` 守卫在谓词扩展后对 `Span` 生效）。

### 5.3 跨顶层 `Await`（→ BC37052）

- 机制：`<Initialize>` 恒为 async（`SynthesizedInteractiveInitializerMethod.vb:51-55` `IsAsync` 恒 `True`）；顶层 `Await` + 作用域内 byref-like 落入 async 状态机捕获检查。
- 覆盖：`Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb:98/:113/:133`（`Not parameter.Type.IsRestrictedType()` / `Not local.Type.IsRestrictedType()` / `If type.IsRestrictedType()`）——谓词扩展即覆盖，报 **BC37052**（`ERR_CannotLiftRestrictedTypeResumable1`）。对齐 C# `span-safety.md:262`。
- **D1-7 更正**：谓词扩展覆盖常规捕获路径；但 Debug 构建下 `HoistInDebugBuild` 会把 ref-like 合成局部（如顶层 ByVal span 实参临时）提升成字段且不查 restricted → 运行期 TypeLoadException。D1-7 追加 `Not local.Type.IsRestrictedType()` 守卫并报 BC37052，补齐此缺口。

### 5.4 宿主层（零改动）

`CommandLineRunner.cs:313-315`（`HasReturnValue()` → `globals.Print(state.ReturnValue)`）与 `VisualBasicCompilation.vb:816-868`（`HasSubmissionResult`）——byref-like 结果报错发生在编译期，`HasAnyErrors()`（`CommandLineRunner.cs:300`）短路，宿主打印路径不受影响。

---

## 6. 改动点 5：显示「ByRef Like Structure」

### 6.1 现状（源码实证）

- `VB\SymbolDisplay\SymbolDisplayVisitor.Types.vb:89` `VisitNamedType` → `:113` `AddTypeKind(symbol)`。
- `AddTypeKind`（:435-453）：`TypeKind.Struct` → `GetTypeKindKeyword` 返回 `SyntaxKind.StructureKeyword`（:468-469）→ `AddKeyword(keyword)`（:450）+ `AddSpace()`（:451）。
- `VB\SymbolDisplay\SymbolDisplayVisitor.vb` 现状**无** `IsRefLikeType`/`IsRestrictedType` 处理（Grep 无匹配）。

### 6.2 改动形状

`AddTypeKind`（:435-453）的 `Else` 分支（:444-452）改为：

```vb
Else
    If symbol.IsRefLikeType AndAlso symbol.TypeKind = TypeKind.Struct Then
        ' 显示「ByRef Like Structure」修饰（仅显示、不可声明；VBX 侧定稿格式）。
        Builder.Add(New SymbolDisplayPart(SymbolDisplayPartKind.Keyword, symbol, "ByRef Like Structure"))
        AddSpace()
    Else
        Dim keyword = GetTypeKindKeyword(symbol.TypeKind)
        If keyword = SyntaxKind.None Then
            Return
        End If

        AddKeyword(keyword)
        AddSpace()
    End If
End If
```

- **不改解析、不改可写语法**：`AddTypeKind` 只在 `Format.KindOptions.IncludesOption(SymbolDisplayKindOptions.IncludeTypeKeyword)` 时输出（:436），纯显示层。
- 显示格式「**ByRef Like Structure**」由 VBX 侧定稿（`../../proposals/proposal-byref-like-safety.md` §5；`RefStructHelper` 的 `<IsByRefLike>` 措辞不构成 VBX 约定）。
- **注意**：`symbol.IsRefLikeType` 是 `ITypeSymbol` 公开接口属性（`ITypeSymbol.IsRefLikeType`），`INamedTypeSymbol` 可直接访问，无需内部符号转换。

---

## 7. 改动点 6：`allows ref struct` 反约束（依赖项，不展开）

- 依赖：`../../tasks/p1-immediate.md` 前置-2（M8：识别 C# 13 `allows ref struct` 反约束元数据）。
- 内容（提案 §3 例外条款）：当目标泛型约束为 `allows ref struct` 时，ref-like 允许作该类型实参（`ConstraintsHelper.vb:661` 的 `ERR_RestrictedType1` 需放行）；此时仍不可装箱。
- **本任务不实现**：标注为依赖项。D1 落地时若前置-2 未就绪，`allows ref struct` 接口消费用例的编译报错属预期（`ConstraintsHelper.vb:661` 现在会对 `IOf(Of Span(Of Integer))` 报 BC31396）——这与「方法内消费 `allows ref struct` 接口可用」的最终目标存在**时序差**，实施期在 test-plan 中如实标注。

---

## 8. 零改动清单 + 边界

### 8.1 零改动清单

| 文件/层 | 不改理由 |
|---|---|
| `VB\Symbols\SpecialTypeExtensions.vb:84-93` | 保留三特殊类型遗留检查（`TypedReference` 等非 `IsByRefLike` 标记，独立受限机制） |
| `VB\Binding\Binder_Initializers.vb:211-220` | 结果装箱由 `ApplyImplicitConversion` → `ApplyConversion:508` 自动覆盖（§5.2） |
| `VB\Analysis\InitializerRewriter.vb:202-243` | 结果回传路径不改；错误在编译期短路 |
| `VB\Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb` | 谓词扩展覆盖常规捕获；Debug hoisting 合成局部需 D1-7 追加 `IsRestrictedType` 守卫（§5.3 已更正） |
| `VB\Symbols\Source\SourceMemberFieldSymbol.vb` / `SourceMethodSymbol.vb` | 既有检查点，谓词扩展即覆盖 |
| `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` | 编译期短路，宿主零改动（§5.4） |
| `VB\Compilation\VisualBasicCompilation.vb`（`HasSubmissionResult`） | 结果报错在编译期，打印路径不变 |
| `VB\CommandLine\VisualBasicCompiler.vb` | `.vbx`/REPL 同 `SourceCodeKind.Script`，无需区分开关 |

### 8.2 边界

- **`.vbx` 顶层同样受限**：REPL 与 `.vbx` 同 kind（`SourceCodeKind.Script`，`VisualBasicCompiler.vb:96`）。`.vbx` 脚本文件顶层 `Dim` of span / 顶层结果 / 顶层 Await + span 报与 REPL 相同错误（同 kind、同一编译器路径）。这是**已接受的代价**（对齐 C# 对 csx 的同一判断，`LDM-2020-02-26.md:46-56`）。
- **方法体局部 / ByVal 值参数 / 按值返回可用**：`Sub F(s As Span(Of Integer))`、`Dim x As Span(Of Integer)`、`Function F() As Span(Of Integer)` 正常绑定（ref struct 按值返回合法，对齐 C#；VB 源符号层无返回值 restricted 检查）。**`ByRef` 参数 → BC31396**（`Binder_Utils.vb:1096-1104`）。
- **`scoped`/`UnscopedRef` 非 VB 概念**：不引入用户级关键字；ref-like 逃逸/生命周期以编译器内部规则表达（本设计不实现逃逸流分析——那是 RefStructHelper 未来项，不在 D1 范围）。
- **Regular 零影响证明**：
  1. `SourceCodeKind.Regular` 下 `IsRefLikeType` 基类默认 `False`（§2.2a）→ `IsRestrictedType()` 行为与现状完全一致（仍只保护三特殊类型）。
  2. `.vb` 项目里若用户引用含 ref struct 的程序集并作字段/返回值/ByRef 参数/装箱 → 报 restricted 错误。**这是新语义**（Span 在 Regular 项目里同样被 restricted 保护），但它是「把 obsolete 误用/坏 IL 变成正确编译错误」= **修错不算回归**（D4），不是行为分裂——`.vbx` 与 `.vb` 语义一致正是本提案的前提。
  3. `AddTypeKind` 显示改动只在 `IncludeTypeKeyword` 时输出，且 ref-like 判定在 Regular 下对源类型为 `False` → 常规符号显示不变。

---

## 9. 无副作用测试矩阵（设计级要点）

> 完整分层矩阵见 `test-plan.md`；此处给设计级要点与测试装置锚点。约束：不发起网络、不写文件（除既有测试装置）、不启动进程、不写注册表。

- **L1 语义层**（`Compilers\VisualBasicSemanticTest\`，`CreateSubmission` / `VisualBasicCompilation.Create`）：`IsRefLikeType` 判定、suppress obsolete（`Span(Of Integer)` 无 obsolete 错误）、错误码矩阵（装箱 BC31394 / Nullable、字段、数组元素、返回值、ByRef 参数、泛型类型实参 BC31396 / LINQ BC36598 / Lambda BC36640 / async 状态机 BC37052）。测试装置需引用含 `Span(Of T)` 的引用程序集（`TestReferences` 既有 `System.Memory` 或 mscorlib 自带）。
- **L2 REPL 层**（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`，`CreateRunner(input:=...)` + `TestConsoleIO` 纯内存）：顶层 `Dim` of span 报 BC31396、`? New Span(Of Integer)(1)` 报 BC31394、跨顶层 Await + 作用域内 byref-like 报 BC37052、方法内局部可用、ByVal 传参可用、`allows ref struct` 接口消费可用（依赖前置-2，未就绪则如实标注）、`ByRef s As Span` 报 BC31396。
- **L3 显示层**（`Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb` 或 `Compilers\VisualBasicTest\SymbolDisplayTests.vb`）：断言 `SymbolDisplay` 输出含「ByRef Like Structure」（`Span(Of Integer)` 显示为 `ByRef Like Structure Span(Of Integer)`）。
- **L4 Regular 模式**（`Compilers\VisualBasicSemanticTest\`）：`.vb` 项目里 Span 作字段 / 返回值 / ByRef 参数报 restricted 错误；普通结构体（非 ref-like）行为不变。
- **既有测试更新**：`BindingErrorTests.vb`（BC31394/BC31396/BC36640 既有用例不回归）、`GenericsTests.vb:245`、`TypeSymbol.vb` 注释变化（「VB has no concept of ref-like types」移除）。`TypeSymbol.vb` 无既有 `IsRefLikeType` 断言（显式实现 `Return False`），实现后补断言。
