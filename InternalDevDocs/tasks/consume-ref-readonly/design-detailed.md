# 详细设计：消费 ref readonly 返回（Consume Ref Readonly Returns，D4 → P1）

> 状态：详细设计（F2）。依据链：`../../meetings/meeting-consume-ref-readonly.md`（RESOLUTION 九条，唯一权威）→ `../../proposals/proposal-consume-ref-readonly.md` → `design-overview.md`（F1 概要）→ 本设计。
> 本设计把概要落到「可被实现者直接照做」的代码级设计。
> 源码事实均已用内置 Read 精读核实（2026-08-30），引用以 `文件:行号` 给出；文件相对路径均相对仓库根，编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`）。

## 0. 核心判定原则（贯穿全文）

- **readonly 判定 = 返回 `RefCustomModifiers` 含 required `modreq(In)`（attribute 身份 `System.Runtime.InteropServices.InAttribute`）**。不做「任何 required modreq 都豁免」。
- **VB 侧签名不带 byref**：绑定类型 = 元素类型，byref-ness 经 `isLValue`/`ReturnsByRef` 携带（`BoundPropertyAccess.vb:26` / `BoundCall.vb:26`）；readonly-ness 经新增 `ReturnsByRefReadOnly` 标志携带。
- **读取路径零改动**：`EmitExpression.vb:1179-1183` 的 auto-deref 是既有机制，导入豁免后自动生效。
- **写入三分类**：直接赋值拒（**复用 `ERR_LValueRequired` 30068**，`AdjustAssignmentTarget` 统一检查）；ByRef 实参 copy-out 传副本且写回丢弃（binder 降级 + lowering 省略写回）；With 块内 readonly-lvalue 接收器成员写经 RValue placeholder 判 **BC30068 拒绝**（复会 R8，与链式一致）、值捕获仅服务读取；推断褪 ByRef 操作副本（值捕获）。

---

## 1. 改动清单总览

| 文件（相对仓库根） | 改动函数 | 改动形状 | 目的（改动点） |
|---|---|---|---|
| `VB\Symbols\Symbol.vb` | `DeriveUseSiteInfoFromCustomModifiers`（:1097-1124） | 修改 | 按 `InAttribute` 身份豁免 required In modreq（改 1） |
| `VB\Symbols\MethodSymbol.vb` | `CalculateUseSiteInfo`（:700-711） | 修改 | 返回路径传 `allowInModifier:=True`（改 1）；新增 `ReturnsByRefReadOnly` 基类属性（改 2） |
| `VB\Symbols\Symbol.vb` | `DeriveUseSiteInfoFromParameter`（:1051-1062） | 修改 | 参数路径传 `allowInModifier:=True`（改 1） |
| `VB\Symbols\Metadata\PE\PEPropertySymbol.vb` | 属性导入（:122-125）、返回参数读取（:136-140）、`ReturnsByRef`（:283-287） | 修改 | 排除 In modreq 的 `AnyRequired`（改 1）；新增 `ReturnsByRefReadOnly` 覆盖（改 2） |
| `VB\Symbols\TypeSymbolExtensions.vb` | 新增 `IsWellKnownTypeInAttribute`（仿 :1350-1359 `IsWellKnownTypeUnmanagedType`） | 新增 | InAttribute 身份判定（改 1） |
| `VB\Symbols\Source\CustomModifierUtils.vb` | 新增 `HasInAttributeModifier`（仿 C# `CustomModifierUtils.cs:158-161`） | 新增 | modreq 数组是否含 required In（改 1/2） |
| `VB\Symbols\PropertySymbol.vb` | `IPropertySymbol_ByRefReturnIsReadonly`（:614-618）、`IPropertySymbol_RefKind`（:620-624） | 修改 | 填真值（改 2） |
| `VB\Symbols\MethodSymbol.vb` | `IMethodSymbol_ReturnsByReadonlyRef`（:1090-1094）、`IMethodSymbol_RefKind`（:1096-1100） | 修改 | 填真值（改 2） |
| `VB\Symbols\Metadata\PE\PEMethodSymbol.vb` | `ReturnsByRef`（:1011-1015）附近 | 新增 | 新增 `ReturnsByRefReadOnly` 覆盖（改 2） |
| `VB\Errors\Errors.vb` + `VBResources.resx` | **零改动**（复用 `ERR_LValueRequired` 30068 既有） | 复用 | 直接赋值拒用 30068，**不新增 BC 码**（改 3；零 xlf 同步） |
| `VB\Binding\Binder_Statements.vb` | `AdjustAssignmentTarget`（:1936-1940） | 修改 | readonly-lvalue 赋值目标报新码（改 3） |
| `VB\Binding\Binder_Invocation.vb` | 实参绑定（:2881-2960） | 修改 | readonly-lvalue 跳过直传、改走 copy-out 且标记无写回（改 4） |
| `VB\Lowering\LocalRewriter\LocalRewriter_Call.vb` | `RewriteByRefArgumentWithCopyBack`（:249-383） | 修改 | 对 readonly 来源省略写回（改 4） |
| `VB\Binding\Binder_WithBlock.vb` | 占位符选择（:238-242） | 修改 | readonly-lvalue 接收器改走 `BoundWithRValueExpressionPlaceholder`，`.X` 落非 lvalue 复用 30068 机制（改 5） |
| `VB\Lowering\WithExpressionRewriter.vb` | `CaptureWithExpression`（:321-368） | 修改 | 值捕获保留、专服务 `.Member` 读取（readonly-lvalue 写已被占位符层分流拒绝，改 5） |
| `VB\Lowering\LocalRewriter\LocalRewriter_With.vb` | `VisitWithStatement`（:43-57） | 修改 | RValue placeholder 替换值 RValue 化：值捕获路径对 readonly ByRef-返回 receiver 返回 lvalue temp，而 RValue placeholder 要求替换值 RValue → `MakeRValue()`；对既有 RValue placeholder 用例幂等 no-op（改 5，实现期新增接线，F20） |
| `VB\SymbolDisplay\SymbolDisplayVisitor.Members.vb` | `VisitProperty`/`VisitMethod` 显示（:81-85） | 修改 | 仅纯 debug 格式追加 `ReadOnly`（改 7） |
| 其余（读取路径 / For Each / 宿主 / WellKnown 表） | — | **零改动** | 依据见 §5、§6 |

> 改动点 6（For Each）为**实现期验证**：编译器零改动预期，测试/文档更新。

---

## 2. 改动点 1：modreq(In) 白名单豁免（导入层）

### 2.1 现状（源码实证）

- `VB\Symbols\Symbol.vb:1097-1124` `DeriveUseSiteInfoFromCustomModifiers`：`:1106-1107` `If Not modifier.IsOptional AndAlso (Not allowIsExternalInit OrElse Not ...IsWellKnownTypeIsExternalInit()) Then ... ERR_UnsupportedType1`。**required modreq 仅 `IsExternalInit` 豁免，无 In。**
- 返回路径：`VB\Symbols\MethodSymbol.vb:701` `DeriveUseSiteInfoFromCustomModifiers(Me.RefCustomModifiers)`（无豁免传参）。
- 参数路径：`VB\Symbols\Symbol.vb:1058` `DeriveUseSiteInfoFromCustomModifiers(param.RefCustomModifiers)`（无豁免传参）。
- 属性路径：`VB\Symbols\Metadata\PE\PEPropertySymbol.vb:124` `propertyParams.Any(Function(p) p.RefCustomModifiers.AnyRequired() OrElse p.CustomModifiers.AnyRequired())` → `ERR_UnsupportedProperty1`（**属性不走 `DeriveUseSiteInfoFromCustomModifiers`，是独立检查**）。
- 数据源：`MetadataDecoder.cs:1193-1198` 把 BYREF 之前的 modreq 解进 `info.RefCustomModifiers`。
- **VB 无 `IsWellKnownTypeInAttribute` helper**（Grep 无匹配）；`WellKnownTypes.cs:274` 枚举 `System_Runtime_InteropServices_InAttribute` 已存在（`WellKnownTypes.cs:651` 元数据名），无需新增 WellKnown 条目。
- C# 模板：`PEPropertySymbol.cs:363-367`（属性白名单）、`Symbol.cs:1293-1354`（`AllowedRequiredModifierType` 枚举结构）、`CustomModifierUtils.cs:158-161`（`HasInAttributeModifier`）。

### 2.2 改动形状

**(a) `VB\Symbols\TypeSymbolExtensions.vb`——新增 `IsWellKnownTypeInAttribute`（仿 `:1350-1359` `IsWellKnownTypeUnmanagedType`）**

```vb
' Keep in sync with C# equivalent.
<Extension>
Friend Function IsWellKnownTypeInAttribute(typeSymbol As TypeSymbol) As Boolean
    Dim namedTypeSymbol = TryCast(typeSymbol, NamedTypeSymbol)
    Return namedTypeSymbol IsNot Nothing AndAlso
        namedTypeSymbol.Name = "InAttribute" AndAlso
        namedTypeSymbol.Arity = 0 AndAlso
        namedTypeSymbol.ContainingType Is Nothing AndAlso
        IsContainedInNamespace(typeSymbol, "System", "Runtime", "InteropServices")
End Function
```

（`IsContainedInNamespace` 仿 `:1358` 用法。命名空间三节参数形式若与既有 helper 签名不一致，实现期按 `IsWellKnownTypeUnmanagedType` 既有写法对齐。）

**(b) `VB\Symbols\Source\CustomModifierUtils.vb`——新增 `HasInAttributeModifier`（仿 C# `CustomModifierUtils.cs:158-161`）**

```vb
Friend Shared Function HasInAttributeModifier(modifiers As ImmutableArray(Of CustomModifier)) As Boolean
    Return modifiers.Any(Function(modifier) Not modifier.IsOptional AndAlso
        DirectCast(modifier, VisualBasicCustomModifier).ModifierSymbol.IsWellKnownTypeInAttribute())
End Function
```

（文件已有 `HasIsExternalInitModifier`（`:129-131`）可作形状参照；`VisualBasicCustomModifier.ModifierSymbol` 返回 `TypeSymbol`。）

**(c) `VB\Symbols\Symbol.vb:1106-1107`——按 attribute 身份豁免 In**

`DeriveUseSiteInfoFromCustomModifiers` 增加 `Optional allowInModifier As Boolean = False` 形参，`If` 条件扩为：

```vb
If Not modifier.IsOptional AndAlso
   (Not allowIsExternalInit OrElse Not DirectCast(modifier, VisualBasicCustomModifier).ModifierSymbol.IsWellKnownTypeIsExternalInit()) AndAlso
   (Not allowInModifier OrElse Not DirectCast(modifier, VisualBasicCustomModifier).ModifierSymbol.IsWellKnownTypeInAttribute()) Then
```

- 命名与形状：`allowIsExternalInit` 先例（`:1099`）表明「布尔豁免开关」模式已在本函数内存在；也可在实现期升级为 C# `AllowedRequiredModifierType` 位枚举（`Symbol.cs:1289-1291`）统一多豁免——**推荐枚举**（可扩展性、对齐 C#），但不强制（本期只需 In，布尔参数改动面最小）。
- **`Out` 不豁免**：不加 `allowOutModifier` 分支；`OutAttribute` 的 required modreq 仍报 `ERR_UnsupportedType1`（对齐 C#：`Out` 仅函数指针参数允许，VB 不消费函数指针）。

**(d) 双路径传参**

| 调用点 | 改动 |
|---|---|
| `VB\Symbols\MethodSymbol.vb:701`（返回路径） | `DeriveUseSiteInfoFromCustomModifiers(Me.RefCustomModifiers, allowInModifier:=True)` |
| `VB\Symbols\Symbol.vb:1058`（参数路径） | `DeriveUseSiteInfoFromCustomModifiers(param.RefCustomModifiers, allowInModifier:=True)` |
| `VB\Symbols\MethodSymbol.vb:707`（`ReturnTypeCustomModifiers`） | **不改**：保留 `allowIsExternalInit:=IsInitOnly`；返回类型自定义修饰符（非 ref 修饰符）不涉 In modreq |

**(e) `VB\Symbols\Metadata\PE\PEPropertySymbol.vb:124`——属性路径排除 In**

`AnyRequired()` 谓词改为「排除 In 的身份」：

```vb
propertyParams.Any(Function(p) p.RefCustomModifiers.Any(Function(m) Not m.IsOptional AndAlso Not DirectCast(m, VisualBasicCustomModifier).ModifierSymbol.IsWellKnownTypeInAttribute()) OrElse
                                  p.CustomModifiers.AnyRequired())
```

（`Out` 不豁免；`CustomModifiers.AnyRequired()`（非 Ref）保持原样。）

> **实现期回填（F9 验证者 A/B 实证，2026-08-30）**：`:124` 只是「定义符号」视角。`ReadOnlySpan(Of Char).Item` 经泛型构造/重定向后是 `SubstitutedPropertySymbol`/`RetargetingPropertySymbol`，其 `RefCustomModifiers` 非空并走**基类 `PropertySymbol.CalculateUseSiteInfo`（:439）**（`RetargetingPropertySymbol.GetUseSiteInfo()` :306 → `CalculateUseSiteInfo()` :439 → `RefCustomModifiers` :254 委托底层含 In modreq）——**该路径也必须豁免**（实现已在 :439 处传 `allowInModifier:=True`）。裸 `PEPropertySymbol.RefCustomModifiers` 返回 Empty，故 `:124` 独立检查覆盖不到派生符号。已实现，勿遗漏。

### 2.3 解锁面（实证）

- **返回**：`s(0)`（`ReadOnlySpan(Of T).Item`）、`.GetPinnableReference()` 可绑定。
- **参数（顺带）**：C# 虚/抽象方法的 `in` 参数（`SourceOrdinaryMethodSymbol.cs:131`）与委托 `in` 参数（`SourceDelegateMethodSymbol.cs:277`）带 modreq(In)，豁免后可调用。VB 映射 `in` 参数为 `RefKind.Ref`（`ParameterSymbol.vb:308`），readonly 来源传 `in` 参数仍走 copy-out，无写穿。

---

## 3. 改动点 2：只读标志 ReturnsByRefReadOnly（符号层 + 语义模型 + 一致性校验）

### 3.1 现状（源码实证）

- `VB\Symbols\MethodSymbol.vb:1090-1094`：`IMethodSymbol_ReturnsByReadonlyRef` 硬编码 `False`；`:1096-1100` `IMethodSymbol_RefKind` = `If(ReturnsByRef, Ref, None)`。
- `VB\Symbols\PropertySymbol.vb:614-618`：`IPropertySymbol_ByRefReturnIsReadonly` 硬编码 `False`；`:620-624` `IPropertySymbol_RefKind` = `If(ReturnsByRef, Ref, None)`。
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:1011-1015` `ReturnsByRef = Signature.ReturnParam.IsByRef`；`:1031` `ReturnRefCustomModifiers = Signature.ReturnParam.RefCustomModifiers`。
- `VB\Symbols\Metadata\PE\PEPropertySymbol.vb:136-139`：`returnInfo = propertyParams(0)`（`:136`）、`_returnsByRef = returnInfo.IsByRef`（`:138`）、`_propertyType = returnInfo.Type`（`:139`）。`returnInfo` 是 `ParamInfo(Of TypeSymbol)`，含 `RefCustomModifiers`。

### 3.2 改动形状

**(a) 基类新增属性（`MethodSymbol` / `PropertySymbol`）**

两个基类各新增：

```vb
Public Overridable ReadOnly Property ReturnsByRefReadOnly As Boolean
    Get
        ' VB 源声明无 ref readonly 返回，默认非只读；PE 派生类型覆盖读 modreq(In)。
        Return False
    End Get
End Property
```

（`MethodSymbol.vb` 的 `ReturnsByRef` 在 `:1084-1088` 区域附近；`PropertySymbol.vb` 的 `ReturnsByRef` 是 `MustOverride`（`:49`）。`ReturnsByRefReadOnly` 用 `Overridable` 而非 `MustOverride`——PE 类型覆盖，源类型不覆盖。）

**(b) 语义模型填真值**

| 位置 | 现状 | 改为 |
|---|---|---|
| `MethodSymbol.vb:1090-1094` `IMethodSymbol_ReturnsByReadonlyRef` | `Return False` | `Return Me.ReturnsByRefReadOnly` |
| `MethodSymbol.vb:1096-1100` `IMethodSymbol_RefKind` | `Return If(Me.ReturnsByRef, RefKind.Ref, RefKind.None)` | `Return If(Me.ReturnsByRef, If(Me.ReturnsByRefReadOnly, RefKind.RefReadOnly, RefKind.Ref), RefKind.None)` |
| `PropertySymbol.vb:614-618` `IPropertySymbol_ByRefReturnIsReadonly` | `Return False` | `Return Me.ReturnsByRefReadOnly` |
| `PropertySymbol.vb:620-624` `IPropertySymbol_RefKind` | `Return If(Me.ReturnsByRef, RefKind.Ref, RefKind.None)` | `Return If(Me.ReturnsByRef, If(Me.ReturnsByRefReadOnly, RefKind.RefReadOnly, RefKind.Ref), RefKind.None)` |

**(c) PE 派生类型覆盖**

`VB\Symbols\Metadata\PE\PEMethodSymbol.vb`（`:1011-1015` 附近）：

```vb
Public Overrides ReadOnly Property ReturnsByRefReadOnly As Boolean
    Get
        Return ReturnsByRef AndAlso Signature.ReturnParam.RefCustomModifiers.HasInAttributeModifier()
    End Get
End Property
```

`VB\Symbols\Metadata\PE\PEPropertySymbol.vb`：`_returnsByRefReadOnly` 字段在 `:136-140` 设定：

```vb
Dim returnInfo As ParamInfo(Of TypeSymbol) = propertyParams(0)
_returnsByRef = returnInfo.IsByRef
_returnsByRefReadOnly = returnInfo.IsByRef AndAlso returnInfo.RefCustomModifiers.HasInAttributeModifier()
_propertyType = returnInfo.Type
```

并覆盖（`:283-287` 附近）：

```vb
Public Overrides ReadOnly Property ReturnsByRefReadOnly As Boolean
    Get
        Return _returnsByRefReadOnly
    End Get
End Property
```

**(d) 一致性校验（仿 C# `PEParameterSymbol.cs:415-421`）**

C# 契约：`isBad |= (RefKind == RefReadOnly) != hasInAttributeModifier`——返回带 modreq(In) 但语义不一致判 bad。VB 落点：**导入层**（`PEMethodSymbol`/`PEPropertySymbol` 解析返回签名时）——若 `returnInfo.RefCustomModifiers` 含 required In modreq 但 `Not returnInfo.IsByRef`（非 ByRef 返回却带 In modreq，第三方乱写元数据），判 unsupported（沿用 `_lazyCachedUseSiteInfo` 的 `ERR_UnsupportedMethod1`/`ERR_UnsupportedProperty1` 或直接 isBad）。实现期在 PE 返回解码处核对 `ParamInfo.RefCustomModifiers` 可用性后落点。

**实现期检查点**：`RetargetingMethodSymbol`/`WrappedMethodSymbol` 等包装符号是否需委托 `ReturnsByRefReadOnly` 到底层（C# 侧 `Retargeting` 默认继承——按实测调用链决定，不列为正式改动项）。

---

## 4. 改动点 3：直接赋值拒绝（复用 `ERR_LValueRequired` 30068 + 三写路径统一检查）

### 4.1 现状（源码实证）

- `VB\BoundTree\BoundAssignmentOperator.vb:56-61`（DEBUG `Validate`）：ByRef-返回属性作赋值左值 → `AccessKind.Get`（store-through-ref，无副本可写）。
- `VB\Binding\Binder_Statements.vb:1925-2011` `AdjustAssignmentTarget`：`:1936-1940` 对 `IsLValue`（ReturnsByRef）属性 `Return propertyAccess.SetAccessKind(PropertyAccessKind.Get)`——**store-through-ref 路径**；`:1946-1955` 不可写属性报 `ERR_NoSetProperty1`/`ERR_AssignmentInitOnly`。
- 三个写路径均经 `AdjustAssignmentTarget`：直接赋值（`BindAssignment` → `:1936`）、复合赋值（`BindCompoundAssignment` `:2044` 调）、Mid 赋值（`:2236` 调）。
- 错误码现状：`ERR_ReadOnlyProperty1 = 30098`（`Errors.vb:164`）、`ERR_UnsupportedProperty1 = 30643`（`:495`）、`ERR_UnsupportedMethod1 = 30657`（`:511`）。

### 4.2 改动形状

**(a) 复用 `ERR_LValueRequired`（30068），不新增 BC 码**

`VB\Errors\Errors.vb` 已存在 `ERR_LValueRequired = 30068`（「Expression is a value and therefore cannot be the target of an assignment.」）。**直接复用，不新增**——零新码（无上游撞号、无 13 语言 xlf 同步）。**不是新造，是延续 VB 对整族 read-only 赋值目标的既有迁移**：`BindingErrorTests.vb:2335` 注释 `' change error 30098 to 30068`（WorkItem 538107）证明 VB 早已把 read-only 赋值目标族从 30098 迁到 30068；`ERR_ReadOnlyProperty1`(30098) 在本编译器内**无任何上报点，是死码**；30068 是 `ReportAssignmentToRValue`（`Binder_Expressions.vb:1795-1809`）对「值样但不可赋」的活实践（常量→30074、readonly 变量→30064、其余→30068），ReadOnly 属性赋值/函数结果接收器字段赋值今天就走 30068——ref readonly 返回是这整族的最新成员。

**明确不复用**：`ERR_ReadOnlyProperty1`(30098)（`'ReadOnly' property` 误导 + 已死码）、`ERR_UnsupportedProperty1/Method1`(30643/30657)（元数据拒绝层级）、`ERR_ReadOnlyAssignment`(30064)（`'ReadOnly' variable` 语义槽位贴 C# CS8331，但 `s(0)` 这类 ref-返回属性访问不是 `ExpressionRefersToReadonlyVariable`（`Binder_Expressions.vb:1811-1837`，只认 FieldAccess/Local）能识别的 variable，硬送 30064 会报「'ReadOnly' variable」而用户面连 ReadOnly 字样都看不到——拒绝路径必须**显式避开 30064 variable 分支**）。

**与改 7 显示退化耦合（须同批落地）**：30068 的「is a value」在语义模型层对 readonly-lvalue 确不准（lvalue 有 home），其成立依赖显示退化（用户面值样）。若 IDE 快速信息格式证明含 `IncludeRef`（前提证伪，用户看得到 byref 上下文），30068 会突兀——此时**一行切换 30064**（`Binder_Expressions.vb:1805` 的 `err = ERRID...`），实现期先把 30064 备选测试写好。语义小不精确（readonly-lvalue 报「is a value」）接受，与退化显示的用户观感一致。

**(b) 统一检查点 `AdjustAssignmentTarget`**

`VB\Binding\Binder_Statements.vb:1936-1940` 的 `If propertyAccess.IsLValue Then` 分支内追加：

```vb
If propertyAccess.IsLValue Then
    Debug.Assert(propertySymbol.ReturnsByRef)
    If propertySymbol.ReturnsByRefReadOnly Then
        ReportDiagnostic(diagnostics, node, ERRID.ERR_LValueRequired, ...)
        isError = True
        Return propertyAccess.SetAccessKind(PropertyAccessKind.Get) ' 仍产出树形，isError 短路诊断
    End If
    WarnOnRecursiveAccess(propertyAccess, PropertyAccessKind.Get, diagnostics)
    Return propertyAccess.SetAccessKind(PropertyAccessKind.Get)
End If
```

- 复合赋值 `s(0) += 5` 与 Mid 赋值 `Mid(s(0), 1) = "x"` 经同一 `AdjustAssignmentTarget` **自动覆盖**（实证 `:2044`/`:2236` 均调它）。
- **`Mid(s(0), ...)` 的实参**：注意 `Mid` 语句的 target 是 `MidExpressionSyntax` 包裹的 `s(0)`，`AdjustAssignmentTarget` 在 `:2236` 对 `target`（含 `s(0)` 属性访问）调用——若 `target` 先经 `BoundMidExpression` 解出 `s(0)` 属性访问，检查点需确认解出的 `BoundPropertyAccess` 是否 `IsLValue=True`（实现期核对 `BindMidAssignment` 的 target 绑定形状，README §3「写路径」已锚定 `:2271` 产出 `BoundAssignmentOperator`）。
- **编译器生成的 copy-back 不误报**：`Binder_Invocation.vb:2923` `BindAssignment(argument, ...)` 生成 copy-back——对 readonly 来源，改动点 4 会让它**不走 copy-back 分支**（跳过 `BindAssignment`），故不会误报；**实现顺序：先改 4 再改 3**，或两者同批并测。
- **不送 30064 variable 分支**：readonly-lvalue 拒绝走 `ERR_LValueRequired`(30068) 检查，**不得**落入 `ERR_ReadOnlyAssignment`(30064) 的 variable 识别路径（`ExpressionRefersToReadonlyVariable` 只认 FieldAccess/Local，`s(0)` 会措辞失准）——实现时确认 `AdjustAssignmentTarget` 内 readonly 分支先于 30064 判定。

**(c) 拒绝面兜底**

若实现期发现某些 write-through 形态不经过 `AdjustAssignmentTarget`（如直接对 `BoundCall` 返回作左值），在 `BindAssignment`（`:2013`）入口补同构检查（LHS 为 `IsReadOnlyLValue` 即拒）。

### 4.3 新增 helper

`IsReadOnlyLValue(expr As BoundExpression) As Boolean`：`expr.Kind = BoundKind.PropertyAccess AndAlso DirectCast(expr, BoundPropertyAccess).PropertySymbol.ReturnsByRef AndAlso ...ReturnsByRefReadOnly` 或 `BoundCall` 同构。放 `VB\BoundTree\BoundExpressionExtensions.vb`（既有 `IsSupportingAssignment` 同文件，`:203`）。

---

## 5. 改动点 4：ByRef 实参 copy-out 丢弃写回

### 5.1 现状（源码实证）

- `VB\Binding\Binder_Invocation.vb:2881-2960`：`:2887-2890` lvalue + identity 转换 → **直传**（返回原 argument，无 temp——readonly 来源会写穿）；`:2892-2942` copy-out（`BoundByRefArgumentWithCopyBack`，`:2923` `copyBackExpression = BindAssignment(argument, ...)`）；`:2959` 非 lvalue → `PassArgumentByVal`（不写回，与字面量传 ByRef 同构）。
- `VB\Lowering\LocalRewriter\LocalRewriter_Call.vb:249-383` `RewriteByRefArgumentWithCopyBack`：`:303` `UseTwiceRewriter.UseTwice` 产出 firstUse（读）与 secondUse（写回）；`:319` firstUse 读入 temp；`:334-337` `copyBack = VisitAssignmentOperator(New BoundAssignmentOperator(secondUse, argument.OutConversion, ...))`；`:380` `copyBackArray.Add(copyBack)`。
- `BoundByRefArgumentWithCopyBack.vb` DEBUG `Validate` 断言 `OriginalArgument.IsSupportingAssignment()`——ByRef-返回属性 `IsLValue=True` → `IsSupportingAssignment=True`（`BoundExpressionExtensions.vb:208-210`），copy-out 分支可达。

### 5.2 改动形状

**(a) Binder：readonly-lvalue 跳过直传、改走 copy-out**

`VB\Binding\Binder_Invocation.vb:2881-2892`：在 `If isLValue AndAlso Conversions.IsIdentityConversion(...)` 直传分支（`:2887`）前插判断：

```vb
Dim isReadOnlyLValue As Boolean = argument.IsReadOnlyLValue()

If isReadOnlyLValue Then
    ' readonly 来源不可直传（callee 写穿只读内存）；降级为 copy-out，写回丢弃。
    isLValue = False  ' 或直接跳过 :2887 分支进入 copy-out 路径，但保留 temp 分配
End If
```

实现要点：
- readonly-lvalue 仍**进入 copy-out 分支**（`:2892`），因为它有 home、需按值取值再传副本。
- **关键：binder 必须跳过 `:2923` 的 `BindAssignment(argument, ...)` 调用**——该调用会经 `BindAssignment`（`:2016`）→ `AdjustAssignmentTarget`（改动点 3 的检查点），对 readonly-lvalue 误报 30068。对 readonly 来源改用占位（`copyBackExpression = Nothing` 或仅作 `hasErrors:=False`），不改树形语义。
- copy-back 的**实际省略在 lowering**（见 b：`RewriteByRefArgumentWithCopyBack` 按 `originalArgument.IsReadOnlyLValue()` 跳过写回生成）。
- **保守替代（更小改动面）**：readonly-lvalue 直接路由到 `PassArgumentByVal`（`:2959`，非 lvalue 路径）——codegen 分配 ByRef temp、不写回，与字面量传 ByRef 完全同构，且天然绕开 `:2923` 的 `BindAssignment`。但 `PassArgumentByVal` 需确认对 ByRef 参数产 temp（注释「Code gen will do this」，`:2956-2958`）——**实现期核对 `PassArgumentByVal` + codegen 对 ByRef 参数的实际 temp 分配**，选更稳者。

**(b) Lowering：readonly 来源省略写回**

`VB\Lowering\LocalRewriter\LocalRewriter_Call.vb` `RewriteByRefArgumentWithCopyBack`（`:249`）：在 `UseTwiceRewriter.UseTwice`（`:303`）后，若 `originalArgument.IsReadOnlyLValue()`：

```vb
If originalArgument.IsReadOnlyLValue() Then
    ' 只读来源：只取值进 temp，不生成写回（与字面量传 ByRef 的丢弃写回一致）。
    firstUse = useTwice.First.SetAccessKind(PropertyAccessKind.Get).MakeRValue()  ' 属性形态
    AddPlaceholderReplacement(argument.InPlaceholder, VisitExpressionNode(firstUse))
    ... ' 走 :319-330 的 temp 分配，跳过 :332-378 的 copyBack 生成与 :380 的 copyBackArray.Add
    Return New BoundSequence(...)  ' 只含 storeVal 与 boundTemp
End If
```

- **不改 `BoundByRefArgumentWithCopyBack` 节点结构**：在 lowering 内按 `OriginalArgument` 的 readonly-ness 分支即可（`OriginalArgument` 是 bound 树原始实参，readonly 信息可从其 `PropertySymbol`/`Method` 读）。若实现期发现 binder 已在 `:2923` 调 `BindAssignment` 造成误报，则 binder 对 readonly 来源改用 `Nothing`/占位避免调 `BindAssignment`。

**(c) 语义断言**

实现后用运行验证锁死：`M(s(0))`（`M(ByRef v As Integer)` 内 `v = 99`）→ `s(0)` 值不变（读 10 仍 10），callee 只改副本。字面量传 ByRef 行为是现成参照（`P(5)` 合法、写回丢弃）。

---

## 6. 改动点 5：With 块 readonly-lvalue 接收器成员写拒绝（值捕获仅服务读）

### 6.1 现状（源码实证）

- `VB\Binding\Binder_WithBlock.vb:238-242`：With 块 receiver 的占位符选择——ByRef-返回 receiver 走 `BoundWithLValueExpressionPlaceholder`（`.X = 5` 落 lvalue，成员写会 store-through-ref 写穿只读内存）；非 lvalue 走 `BoundWithRValueExpressionPlaceholder`。
- `VB\Lowering\WithExpressionRewriter.vb:321-368` `CaptureWithExpression`：`:324-326` `If Not (state.DoNotUseByRefLocal OrElse ...) Then Return CaptureInAByRefTemp(value, state)`——ByRef-返回 receiver 默认存进 **ByRef temp**，`.Item(0) = 5` 会写穿只读内存；`:337-347` 属性 draft-rewrite 走 `CaptureInATemp` 值捕获（`:347`）；`:349-359` 调用 draft-rewrite 走 `CaptureInATemp`（`:359`）；`:323` 注释「readonly reference cannot be stored in a temp, so do not capture in a ref」。
- **语义事实（复会 R8，2026-09-01）**：With 块内对 readonly-lvalue 接收器的成员写 `.X = 5` 与链式 `o.S(0).X = 5` 是**同一成员写**（spec 11.6 的占位符替换语义，`Binder_Expressions.vb:2637-2641`）——readonly-lvalue 在「能否写穿」上与 RValue 不可区分，静默操作副本会让用户以为写成功、实际原值不变，是不可发现陷阱。既有判例 `With (x).F.F.F : .F = ""`（RValue 接收器）早已按 BC30068 拒绝（`WithBlockErrorTests.vb:749-757`）。

### 6.2 改动形状（复会 R8，2026-09-01）

**不再操作副本**。readonly-lvalue 接收器在 With 块内的成员写编译期判 **BC30068 拒绝**，与链式一致；值捕获路径保留，专服务 `.Member` 读取（`With h(0) : Dim y = .X` 合法）。

**(a) `VB\Binding\Binder_WithBlock.vb:238-242` 占位符选择加只读判定**

LValue placeholder 分支条件加 `Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue()`：

```vb
If (boundExpression.IsLValue OrElse boundExpression.IsMeReference) AndAlso
   Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue() Then
    ... ' 既有 BoundWithLValueExpressionPlaceholder 分支（纯 ref / 可变 lvalue / Me 走这里）
Else
    ... ' readonly-lvalue 落这里：BoundWithRValueExpressionPlaceholder
End If
```

- **判定 helper**：`IsReadOnlyLValueOrMemberOfReadOnlyLValue()` 用**成员链版**判定（覆盖 `With o.S(0).Inner` 形态——receiver 是 readonly-lvalue 的成员访问，不能只看顶层裸 receiver）。
- readonly-lvalue 接收器改走 `BoundWithRValueExpressionPlaceholder`，`.X` 落非 lvalue → 自动复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068 机制，`.X = 5` 报恰一 `ERR_LValueRequired` @ `.X`。
- **`AdjustAssignmentTarget` 无需改动**：拒绝发生在 With 占位符层，不新增 store-through-ref 检查点。

**(b) `VB\Lowering\WithExpressionRewriter.vb:321-368` 值捕获保留，专服务读**

readonly-lvalue 接收器经 RValue placeholder 落到既有 `CaptureInATemp` 值捕获路径（`:337-359`），`.Member` 读取合法；`:324` 的 `CaptureInAByRefTemp` 对 readonly 来源不可达（占位符层已分流，实现期按需加守卫断言）。

> **实现期回填（F20，2026-09-01）**：RValue placeholder 要求替换值必须是 RValue（`LocalRewriter.AssertPlaceholderReplacement` `LocalRewriter.vb:73-81`），但 `CaptureInATemp` 对值类型 readonly ByRef-返回 receiver（PropertyAccess/Call）返回 **lvalue temp**（`:373`/`:388`，`Debug.Assert(expression.IsLValue)` `:395-396`）。须在 `LocalRewriter_With.vb:43-57` `VisitWithStatement` 对非 lvalue placeholder 的替换值做 `MakeRValue()`（`If Not node.ExpressionPlaceholder.IsLValue Then replaceWith = replaceWith.MakeRValue()`）。对既有 RValue placeholder 接收器（`Not value.IsLValue → CaptureInATemp(...).MakeRValue()` `:315-318`）本就是 RValue，故 `MakeRValue()` 幂等 no-op，仅对新增 readonly-lvalue → RValue placeholder 用例生效。`BoundLocal.MakeRValue`/`BoundFieldAccess.MakeRValue` 均返回非 lvalue 新节点（`BoundLocal.vb:31-37` / `BoundFieldAccess.vb:28-34`）。读取 `.Member` 仍从捕获副本取值、无诊断；写入 `.X = 5` 已在绑定期判 30068（`VisitWithStatement` `HasErrors` 短路 `LocalRewriter_With.vb:18`，不进此 lowering）。

- 测试覆盖：`With h(0) : .X = 5 : End With` 恰一 30068 @ `.X`（S13b）；`With h(0) : Dim y = .X : End With` 无诊断（S12，值捕获读）；嵌套写（`With h(0) : .Inner.X = 5` 恰一 30068 @ `.Inner.X`）、方法调用接收器写（`With o.S(0) : .X = 5`）、With 内成员链写（`With o.S(0).Inner : .X = 5`）、成员链读（`With o.S(0).Inner : Dim y = .X` 输出 10）、iterator 读（`Yield .X`）。

---

## 7. 改动点 6：For Each 解锁（实现期验证，编译器零改动预期）

### 7.1 现状（源码实证）

- `VB\Binding\Binder_Statements.vb:4291-4299` `s_isReadablePropertyWithoutArguments`：`Current` 谓词只查 `IsReadable AndAlso Not IsGenericMethod AndAlso GetCanBeCalledWithNoParameters`——**无「不得 ByRef 返回」排除**。
- `VB\Lowering\LocalRewriter\LocalRewriter_ForEach.vb:307-320`：`CurrentPlaceholder` → `boundCurrent` 以 RValue 替换进 `controlVariable = Current`（`BoundAssignmentOperator`），值读走 auto-deref（`EmitExpression.vb:1179-1183`）。
- 现状测试：`Compilers\VisualBasicSemanticTest\Semantics\ByRefLikeTests.vb:537-555`（S24）期望 `ERR_UnsupportedProperty1` + `System.ReadOnlySpan(Of T).Enumerator.Current`；`spec-byref-like-safety.md:228-230` Byref enumerators 条目。

### 7.2 改动形状

- **验证**：modreq 豁免落地后，`For Each c As Char In "ab".AsSpan()` 应无诊断、可展开。
- **更新 S24**：`ByRefLikeTests.vb:537-555` 从 `VerifyDiagnostics(ERR_UnsupportedProperty1)` 改为断言**无诊断** + 可运行（若加 emit 断言则验证展开 IL）。
- **更新 spec**：`spec-byref-like-safety.md:228-230` Byref enumerators 条目改写为「ref readonly 返回的 `Current` 已可消费，For Each over ref struct enumerator 可用」；同步修正「VB does not support properties that return by reference」的不精确表述（纯 ref 已可消费，带 modreq 的 ref readonly 曾经不可、现已可）。

---

## 8. 改动点 7：SymbolDisplay debug 格式

### 8.1 现状（源码实证）

- `VB\SymbolDisplay\SymbolDisplayVisitor.Members.vb:81-85`：`If symbol.ReturnsByRef AndAlso Format.MemberOptions.IncludesOption(SymbolDisplayMemberOptions.IncludeRef)` → `AddKeyword(SyntaxKind.ByRefKeyword)` + `AddCustomModifiersIfRequired(symbol.RefCustomModifiers)` + `AddSpace()`。**无 readonly 显示机制。**

### 8.2 改动形状

`VisitProperty`/`VisitMethod`（`SymbolDisplayVisitor.Members.vb` 中显示 byref 返回的成员处，`:81-85` 区域）追加 readonly 合成，但**仅限纯 debug / 内部诊断格式**：

```vb
If symbol.ReturnsByRef AndAlso Format.MemberOptions.IncludesOption(SymbolDisplayMemberOptions.IncludeRef) Then
    AddKeyword(SyntaxKind.ByRefKeyword)
    If symbol.ReturnsByRefReadOnly AndAlso IsPureDebugDisplayFormat(Format) Then
        AddKeyword(SyntaxKind.ReadOnlyKeyword)
        AddSpace()
    End If
    AddCustomModifiersIfRequired(symbol.RefCustomModifiers)
    AddSpace()
End If
```

- `IsPureDebugDisplayFormat(Format)`：实现期定——用现有格式判定（如 `Format = SymbolDisplayFormat.DebugFormat` 或内部诊断用的带 ref 格式），**默认 IDE 格式（tooltip）不带**。**debug 格式须有真实可达路径（防死代码）**，词序（规范形如 `ReadOnly ByRef Integer`）与可到达路径须进 `SymbolDisplayTests` 锁定。
- **对照**：`SymbolDisplayVisitor.Types.vb:445-447` 的「ByRef Like Structure」（ref-like 类型显示）**已存在、不改**；本改动只针对 ref readonly **返回**，且用户面不标（退化形态）。
- **前提（实现期第一验证项）**：「IDE 快速信息格式不含 `IncludeRef`」定义在 workspace/LSP 层、本仓库不可直接核实——**标 Suspect，实现期第一项确认**。退化**不许比纯 ref 更少**：`SymbolDisplayTests.vb:5404-5407` 实证纯 ref 在 `IncludeRef` 下显示 `ReadOnly ByRef P As Integer`——若 IDE 格式含 `IncludeRef`，ref readonly 至少与纯 ref 对等显示（此时 30068 错误码耦合切换 30064，见改 3）。
- **防叠字**：`VisitProperty` 的属性描述符可能是 `ReadOnly`（`ReadOnly` 声明属性），合成 `ReadOnly ByRef` 时须防与描述符叠成 `ReadOnly ByRef ReadOnly Integer`——词序测试覆盖。
- **承重墙**：`ReturnsByRefReadonly` 语义模型暴露（改 2）不受显示分层削弱——分析器/工具从语义模型读真值，显示只是呈现层。

---

## 9. 零改动清单 + 边界

### 9.1 零改动清单

| 文件/层 | 不改理由 |
|---|---|
| `VB\CodeGen\EmitExpression.vb:1179-1183` | auto-deref 是既有机制，导入豁免后自动生效（读取路径零改动） |
| `VB\BoundTree\BoundPropertyAccess.vb:26` / `BoundCall.vb:26` | `isLValue:=ReturnsByRef` 已携带 byref-ness，readonly 经新标志驱动写入分类 |
| `VB\CodeGen\EmitAddress.vb:46-51/:219-256` | `HasHome` 对 ByRef 返回 Call 恒有 home；只读来源按值读不涉地址发射 |
| `VB\Symbols\ParameterSymbol.vb:304-310` | `in` 参数仍映射 `RefKind.Ref`（§4a v1 不做，copy-out 保底） |
| `VB\Errors\Errors.vb:164/:495/:511` | 既有错误码（30098/30643/30657）**不复用**；直接赋值拒**复用 `ERR_LValueRequired`(30068)**，零新增 |
| `Compilers\Core\Portable\WellKnownTypes.cs` | `System_Runtime_InteropServices_InAttribute`（`:274`）已存在，零新增 |
| `VB\Binding\Binder_Statements.vb:4291-4299` / `LocalRewriter_ForEach.vb:307-320` | For Each 展开零改动（改 6 只验证 + 更新测试/文档） |
| `VB\Lowering\WithExpressionRewriter.vb:337-359` | 值捕获路径保留、专服务 `.Member` 读取（改 5 不改机制） |
| `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` | 编译期错误短路（`HasAnyErrors`），宿主打印路径零改动 |
| `VB\Compilation\VisualBasicCompilation.vb` | 结果报错在编译期，`HasSubmissionResult` 不受影响 |

### 9.2 边界

- **不引入声明语法**：VB 无 `ref readonly` 声明（D1「只消费不声明」），无 overrides/implements 需求；不加 `scoped`/`UnscopedRef`（M3）。
- **写入三分类是硬边界**：直接赋值拒（编译期，复用 `ERR_LValueRequired` 30068）；ByRef 实参 copy-out 传副本且写回丢弃；With 块内 readonly-lvalue 接收器成员写判 **BC30068 拒绝**（复会 R8，与链式一致）、值捕获仅服务读取。**唯一高危面 = 漏接线 = 静默写坏只读内存**，实现期以测试矩阵全绿为 P1 硬验收。
- **`in` 参数显示失准**：VB 映射 `in` 参数为可写 `Ref`，语义模型显示为 ByRef——是展示失准而非内存危害，§4a 后续修正（v1 不做）。
- **丢弃写回语义**：callee 实际执行 `x = 99`（改的是副本）——比「拒绝」更隐蔽，spec 硬性文档义务（R4）。
- **修错不算回归（D4）**：老项目若依赖（不存在的）写穿行为，是修错非回归；`s(0) = 5` 从「绑定失败（整个成员不可用）」变为「可读 + 直接赋值编译错误」，属能力新增 + 明确拒绝。
- **`.vbx` 与 Regular 同一编译器**：规则语义一致、诊断不区分模式。

---

## 10. 实现顺序建议（供 F9-F16 拆解参考）

1. **改 1**（导入豁免）→ 解锁 `s(0)` 读取、`GetPinnableReference`、`in` 参数调用——**先让读通**，此时写穿面暴露。
2. **改 2**（只读标志 + 一致性校验）→ 建立安全判定的数据基础。
3. **改 4**（ByRef 丢弃写回）→ 堵 ByRef 实参写穿。
4. **改 3**（直接赋值拒绝，复用 `ERR_LValueRequired` 30068）→ 堵 store-through-ref 写路径（依赖改 4 先处理 copy-back 误报）。
5. **改 5**（With 块 readonly-lvalue 成员写拒绝）→ 堵 With 块写穿（RValue placeholder + 30068；值捕获仅服务读）。
6. **改 6**（For Each 验证 + S24/spec 更新）。
7. **改 7**（SymbolDisplay debug 格式）。
8. **F16 全量测试收口**（test-plan 四层矩阵）。
