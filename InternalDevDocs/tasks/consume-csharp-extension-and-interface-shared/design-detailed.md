# 详细设计：消费 C# 扩展成员与接口共享成员（consume-csharp-extension-and-interface-shared）

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（Detailed design 四层 + Part B）→ `../../meetings/meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION 六条）→ `design-overview.md`（F1 概要）→ `../../meetings/evaluation-consume-csharp-extension-and-interface-shared.md`。
> 本设计把概要落到「可被实施者直接照做」的代码级设计。每条改动给**改动文件 + 函数 + 行号区域 + 改动形状**。
> 源码事实均已用内置 Read/Grep 精读核实，引用以 `文件:行号` 给出；编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（简写 `VB\`），C# 前缀 `Compilers\CSharp\Portable\`（简写 `CS\`），Core 前缀 `Compilers\Core\Portable\`（简写 `Core\`）。**标「实现期核对」的项为未核实项，如实标注，不越权承诺。**

## 0. 核心判定原则（贯穿全文）

1. **范围只消费**：VB 侧声明（`Shared` 接口成员 / `<Extension>` 扩展属性）不在本任务范围。
2. **现有扩展方法路径不动**：`AddMemberIfExtension` 收 `[Extension]` 方法继续走 `ReducedExtensionMethodSymbol`，新增属性/运算符路径**并列而非改写**（对齐 C# `NamedTypeSymbol.cs:408-437` 两条并列收集路径）。
3. **复用现成挂点**：`ReducedExtensionPropertySymbol`（`Binder_XmlLiterals.vb:1524`）、汇聚点 `LookupForExtensionMethodsIfNeedTo`（`Binder_Lookup.vb:1152`）、运行时钩子 `VirtualStaticsInInterfaces`（`AssemblySymbol.vb:348-349`）复用，不从零建。
4. **同名优先级**：实例成员优先 → 扩展成员回退（`Binder_Lookup.vb:1162-1166` gate）；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`）。
5. **`Object` 接收者维持晚绑定**：`ShouldLookupExtensionMethods`（`Binder_Lookup.vb:1174-1179`）`Not container.IsObjectType()` 门保留，Strict Off 晚绑定通道保留。
6. **`.vbx` 与 Regular 语义一致**：同一编译器、同一路径，无区分开关。

---

## 1. 改动清单总览

| # | 文件（相对仓库根） | 改动函数 | 改动形状 | 目的 |
|---|---|---|---|---|
| A-① | `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb` | 新增扩展分组/标记类型识别（仿 CS `PENamedTypeSymbol.cs:2295/2451-2462`）；`GetMembers`（成员导入处，仿 CS `:2580-2630`） | 新增属性/方法 + 修改 | 读 `<G>$`/`<M>$`/`[ExtensionMarker]`，标记扩展成员（改动点 1） |
| A-① | `VB\Symbols\Metadata\PE\PEMethodSymbol.vb` / `PEPropertySymbol.vb` | 新增 `IsExtensionMember`（或扩展 `IsExtensionMethod`） | 新增属性 | 成员级识别 `[ExtensionMarker]`（改动点 1） |
| A-② | `VB\Symbols\NamedTypeSymbol.vb` | 新增 `GetExtensionMembers` / `AppendProbableExtensionMembers`；`GetExtensionMethods`（:338-351）扩 | 新增方法 + 修改 | 收集层收属性/运算符（改动点 2） |
| A-② | `VB\Symbols\NamespaceSymbol.vb` | `AddMemberIfExtension`（:518-526）扩为收属性/运算符 | 修改 | 收集层接收者扩展（改动点 2） |
| A-② | `VB\Binding\NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66` | `CollectProbableExtensionMethodsInSingleBinder` | 修改（追加调用） | 四处 binder 收集扩展成员（改动点 2） |
| A-③ | `VB\Binding\Binder_XmlLiterals.vb`（`ReducedExtensionPropertySymbol` 于 :1524） | 泛型归约补 `TypeArgumentInference` | 修改/新增 | 泛型扩展属性归约（改动点 3） |
| A-③ | `VB\Symbols\ReducedExtensionMethodSymbol.vb`（`Create` 于 :35-204） | 仿写扩展运算符/属性归约 | 新增 | 归约模板（改动点 3） |
| A-④ | `VB\Binding\Binder_Lookup.vb`（`MergeInternalXmlHelperValueIfNecessary` 于 :1259；`LookupForExtensionMethodsIfNeedTo` 于 :1152） | 泛化 `MergeInternalXmlHelperValueIfNecessary` → 收全部扩展属性 | 修改 | 汇聚点合并扩展属性（改动点 4） |
| A-④ | `VB\Semantics\Operators.vb`（`CollectUserDefinedOperators` 于 :2847） | 类型层级收集后追加扩展运算符候选 | 修改 | 扩展运算符决议挂点（改动点 4） |
| B-① | `VB\Binding\Binder_Expressions.vb`（:2913-2914） | `ERR_TypeParamQualifierDisallowed` 改判 | 修改 | 约束接口共享成员绑定（改动点 5） |
| B-② | `VB\CodeGen\EmitExpression.vb`（新增 `CallKind.ConstrainedCall` 于 :982-985/:1009-1019/:1127-1131；原 `ConstrainedCallVirt` 于 :984/:1054/:1111-1112） | 类型参数 SAIM 调用路由到 `constrained.` + **`call`**（F10 实证纠正，非 callvirt）+ `Binder_Invocation` 保留接收者 + `Binder.vb` BC37314 抑制 | 修改 | CodeGen（改动点 6） |
| B-③ | `VB\Semantics\Operators.vb`（`GetTypeToLookForOperatorsIn` 于 :2942） | 补接口约束共享运算符 | 修改 | 类型参数操作数运算符（改动点 7） |
| B-④ | `VB\Binding\Binder_Expressions.vb`（绑定处） | 新增运行时门控检查（复用 `SupportsRuntimeCapability`） | 修改 | 运行时不受支持诊断（改动点 8） |
| 其余 | 既有扩展方法链 / `MightContainExtensionMethods` 快速路径 / 宿主层 | — | **零改动** | 依据见 §9 |

> **改动点 8（诊断码族）定案（F9 2026-08-22）**：未命中复用 BC32098；运行时不受支持新建 BC `32134`（详见 §8 与 verification-checklist.md）。

---

## 2. 改动点 1（A-①）：元数据导入层——读 `<G>$`/`<M>$`/`[ExtensionMarker]`

### 2.1 现状（源码实证）

- **C# 14 扩展成员元数据形态**（任务 README 实证 dump）：外层 `[Extension]` 静态类 + 嵌套 `<G>$<哈希>` 分组类型（标 `[Extension]`，内放 `[ExtensionMarkerName]` 成员）+ `<M>$<哈希>` 标记类型（含 `<Extension>$(receiver)` 标记方法）+ 顶层无 `[Extension]` 实现方法。
- **VB PE 层现状**：`PEMethodSymbol.IsExtensionMethod`（`VB\Symbols\Metadata\PE\PEMethodSymbol.vb:688-711`）只读 `HasExtensionAttribute`（`:698` `_containingType.ContainingPEModule.Module.HasExtensionAttribute(Me.Handle, ignoreCase:=True)`）；`PENamedTypeSymbol.MightContainExtensionMethods`（`VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb:919-945`）只认「顶层非泛型 `[Extension]` 类」（`:927/:931`）。**无 `<G>$`/`<M>$`/`[ExtensionMarker]` 读取**。
- **共享 Core 基建已在位（关键发现）**：
  - `Core\Portable\MetadataReader\PEModule.cs:1061-1063`——`HasExtensionMarkerAttribute(EntityHandle token, out String markerName)`（读 string 值属性，仿 `:1056` `HasDefaultMemberAttribute`）。**VB PE 层可直接调用**。
  - `Core\Portable\WellKnownTypes.cs:343/:717`——`System_Runtime_CompilerServices_ExtensionMarkerAttribute`（元数据名 `"System.Runtime.CompilerServices.ExtensionMarkerAttribute"`）；`Core\Portable\WellKnownMember.cs:631` + `Core\Portable\WellKnownMembers.cs:4370-4372/:5851`——`.ctor(string)`。
  - `Core\Portable\Symbols\WellKnownMemberNames.cs:510`——`ExtensionMarkerMethodName = "<Extension>$"`。
- **C# 参考**：
  - `CS\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2295-2329`——`TryGetExtensionMarkerMethod()`：遍历本类型方法，找 `SpecialName|Static` 且名 `<Extension>$` 的唯一方法。
  - `:2451-2462`——识别标记类型：`HasSpecialName && IsStatic && Public && Class && 基类 Object && Arity 0 && 无接口`，再 `TryGetExtensionMarkerMethod`，构造带 `extensionInfo` 的扩展块符号。
  - `:2580-2595`（`CreateMethods`）/ `:2611-2630`（`CreateProperties`）——扩展块导入成员时遍历**分组类型句柄**的方法/属性，过滤 `HasExtensionMarkerAttribute(handle, out markerName) && markerName == MetadataName`（即成员标记名指向所属标记类型）。

### 2.2 改动形状

**(a) `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb`——分组/标记类型识别 + 扩展成员标记**

- **F12 澄清（2026-08-22，dump `tmp/exp-consume/dump/` 实证 + C# 参考）**：`IsExtensionGroupingType` 识别的是 **`<G>$` 分组类型**，**不是**本目下原写的 `<M>$` 形状。两型判别完全分离：
  - **`<G>$` 分组类型**（F13 收集层遍历对象）：外层 `[Extension]` 容器（如 `ExternLib.NewExt`，`class-extension=True`）的**嵌套类型**，**自身带 `[Extension]`**（dump：`NewExt+<G>$...` `class-extension=True`），内含 `[ExtensionMarker]` 实现成员（dump：`get_CharCount()` / `Shout()` / `prop CharCount` 均 `[ExtensionMarkerAttribute]`）+ 嵌套 `<M>$` 标记类型。判据：`Me.ContainingType IsNot Nothing AndAlso HasExtensionAttribute(_handle)`（F12 已实现；对齐 C# `CreateNestedTypes` 对 `type.IsExtension` 的分支，`PENamedTypeSymbol.cs:2384-2469`）。
  - **`<M>$` 标记类型**（本目原写的形状是对的，但属于 `<M>$`）：嵌套 `HasSpecialName && 静态(Abstract+Sealed) && Public && Class && 基类 Object && Arity=0 && Interfaces 空`，且含单个 `<Extension>$(receiver)` 标记方法（dump：`NewExt+<G>$...+<M>$...` 含 `Void <Extension>$(String)`，`class-extension=False`）。判据 = C# `TryGetExtensionMarkerMethod`（`PENamedTypeSymbol.cs:2295-2329`）+ `:2451-2462` 形状检查。F12 新增独立属性 `IsExtensionMarkerType` 承载（可延迟到 F14 精确归属 `markerName == 所属标记类型 MetadataName` 时用；收集层不依赖）。
- 成员导入处（`GetMembers` 走 `MetadataDecoder` 创建 `PEMethodSymbol`/`PEPropertySymbol`）标记：F12 采用**惰性读 `HasExtensionMarkerAttribute`**（`Core\PEModule.cs:1061`）——在 `IsExtensionMember`（§2.2b）getter 内 `ContainingPEModule.Module.HasExtensionMarkerAttribute(handle, markerName)` 现读现判，**不**改成员构造签名/加字段。**实现期核对取舍（F12 标注）**：CS 侧用 `markerName == 所属标记类型 MetadataName` 精确归属（`PENamedTypeSymbol.cs:2585/:2616`）；VB 简单消费只记「是扩展成员」（布尔），marker 名分组归属精度按需对齐（届时 `IsExtensionMarkerType` 已就位）。

**(b) `VB\Symbols\Metadata\PE\PEMethodSymbol.vb` / `PEPropertySymbol.vb`——成员级识别**

- 新增 `Friend Overridable ReadOnly Property IsExtensionMember As Boolean`：基类 `MethodSymbol` / `PropertySymbol` 声明 `Friend Overridable` 默认 `False`，`PEMethodSymbol` / `PEPropertySymbol` **`Overrides`** 返回「`_containingType.IsExtensionGroupingType` 且 自身带 `[ExtensionMarker]`」。（对方法，独立于现有 `IsExtensionMethod`（:688）——扩展属性非方法形态须属性侧独立；扩展方法 `[Extension]` 顶层不误标，`IsExtensionMember` 只认 `[ExtensionMarker]`，容器顶层 `Shout(String)`/`get_CharCount(String)` 均 False。）**F12 定案**：编译器内部 `Friend`，不新增公开符号形态（`IMethodSymbol`/`IPropertySymbol` 上不暴露，F9 #4 包装方案）。

**(c) 术语与 WellKnown 核对（实现期核对项 1）**

> **实现期核对**：C# 规范名 `ExtensionMarkerNameAttribute`（`csharplang\proposals\csharp-14.0\extensions.md:612-620`，compiler-use only、编译器合成），**但本树 WellKnown 表实际条目名是 `System_Runtime_CompilerServices_ExtensionMarkerAttribute`**（`WellKnownTypes.cs:343/:717`，`.ctor(string)`）——属性名差异须按**目标 C# 14 库实际发射的属性名**对齐（本树 C# 发射/导入均用 `ExtensionMarkerAttribute`，见 `CS\Emitter\Model\PEModuleBuilder.cs:1677/1926` 与 `CS\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2585`）。实施时以 `PEModule.HasExtensionMarkerAttribute`（`Core\PEModule.cs:1061`）读到的实际属性为准，**不假定 spec 名即元数据名**。

### 2.3 零新增（自动覆盖）

`MightContainExtensionMethods`（`PENamedTypeSymbol.vb:919`）对顶层 `[Extension]` 类已为真，扩展成员消费的快速路径**自动覆盖**，无需新标志（meeting 判定）。`PEAssemblySymbol.MightContainExtensionMethods`（`PEAssemblySymbol.vb:254`）同理。

---

## 3. 改动点 2（A-②）：收集层——`GetExtensionMethods`/`AddMemberIfExtension` 扩为收属性/运算符

### 3.1 现状（源码实证）

- `VB\Symbols\NamedTypeSymbol.vb:338-351`——`GetExtensionMethods(methods As ArrayBuilder(Of MethodSymbol), appendThrough As NamespaceSymbol, Name As String)`：`Me.GetSimpleNonTypeMembers(Name)` → `appendThrough.AddMemberIfExtension(methods, member)`。**只迭代当前类型普通成员，不遍历嵌套分组类型**；数组元素类型是 `MethodSymbol`。
- `VB\Symbols\NamespaceSymbol.vb:518-526`——`AddMemberIfExtension(bucket As ArrayBuilder(Of MethodSymbol), member As Symbol)`：**只收 `member.Kind = SymbolKind.Method`** 且 `MayBeReducibleExtensionMethod`。
- `VB\Symbols\NamedTypeSymbol.vb:305-317`——`AppendProbableExtensionMethods(name, methods)`：`Me.GetMembers(name)` + `member.Kind = SymbolKind.Method` + `MayBeReducibleExtensionMethod`。
- 四处 binder（`NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66`）的 `CollectProbableExtensionMethodsInSingleBinder` 全部调 `AppendProbableExtensionMethods`（类型或命名空间）。
- `VB\Symbols\NamespaceSymbol.vb:471-475`——命名空间级 `GetExtensionMethods`：`Me.TypesToCheckForExtensionMethods` 每个类型调 `containedType.GetExtensionMethods(...)`。

### 3.2 改动形状

**(a) 新增并列收集路径 `GetExtensionMembers` / `AppendProbableExtensionMembers`（仿 C# `doGetExtensionMembers`，`NamedTypeSymbol.cs:439-474`）**

- `VB\Symbols\NamedTypeSymbol.vb`：新增
  ```vb
  Friend Overridable Sub AppendProbableExtensionMembers(name As String, members As ArrayBuilder(Of Symbol))
      If Me.MightContainExtensionMethods Then
          For Each nested In Me.GetTypeMembers()   ' F9 已核实：VB GetTypeMembers(name) 按名查字典（空名查空），必须用无参版取全部嵌套类型（含 <G>$/<M>$）
              If Not nested.IsExtensionGroupingType Then Continue For
              For Each member In nested.GetMembers(name)
                  If member.IsExtensionMember Then members.Add(member)
              Next
          Next
      End If
  End Sub
  ```
  及 `GetExtensionMembers(members As ArrayBuilder(Of Symbol), appendThrough As NamespaceSymbol, Name As String)` 等价于 `GetExtensionMethods`（:338）但走嵌套分组类型。**F9 已定案（2026-08-22）**：VB `GetTypeMembers(name)` 按名查字典（`PENamedTypeSymbol.vb:807-817`，字典由 `GroupByName` :1294-1300 建、无空键），`name:=""` 查空——**必须改用无参 `GetTypeMembers()`**（`PENamedTypeSymbol.vb:780-784`，返回全部嵌套类型含 `<G>$`/`<M>$`），不可用 `GetTypeMembers(name:="")`；分组类型过滤靠改动点 1 的 `IsExtensionGroupingType`，无需按名过滤。（F9 验证者复核：原「C# 侧 `GetTypeMembers(name:"")`=全部」论据在本 fork C# 树不成立，`GetTypeMembers(ReadOnlyMemory<char>)` 亦为普通字典查找、无空名特例——VB 修正是独立成立的。）
- `VB\Symbols\NamespaceSymbol.vb`：新增命名空间级 `GetExtensionMembers(members, name)`（仿 :471-475），及 `AddMemberIfExtension` 扩为（**示意形状**，`bucket` 为并列收集桶）：
  ```vb
  ' 示意：现有方法桶不动，属性/运算符走并列收集桶
  Friend Sub AddMemberIfExtension(bucket As ArrayBuilder(Of MethodSymbol), member As Symbol)
      If member.Kind = SymbolKind.Method Then
          Dim method = DirectCast(member, MethodSymbol)
          If method.MayBeReducibleExtensionMethod OrElse method.IsExtensionMember Then
              BuildExtensionMethodsMapBucket(bucket, method)   ' 扩展运算符仍是 MethodSymbol，可并入
          End If
      End If
      ' 扩展属性（SymbolKind.Property）走并列桶：propertyBucket.Add(DirectCast(member, PropertySymbol)) 处
  End Sub
  ```
  **实现期核对**：`ArrayBuilder(Of MethodSymbol)` 与属性不兼容——需把扩展属性收集到**独立数组**（如 `ArrayBuilder(Of PropertySymbol)`/`ArrayBuilder(Of Symbol)`），或改桶为 `ArrayBuilder(Of Symbol)` 并只在归约处收窄。推荐并列数组，避免扰动现有方法路径（原则 2）。扩展运算符（`MethodKind.UserDefinedOperator` 且 `[ExtensionMarker]`）仍是 `MethodSymbol`，可并入方法桶，但须扩 `MayBeReducibleExtensionMethod` 判定或并行判 `IsExtensionMember`（见下）。
- 运算符（带 `[ExtensionMarker]`）仍是 `MethodSymbol`，可并入 `ArrayBuilder(Of Symbol)`；但 `MayBeReducibleExtensionMethod`（`MethodSymbol.vb:424` = `IsExtensionMethod AndAlso MethodKind <> ReducedExtension`）对扩展运算符为假（扩展运算符不带 `[Extension]`）——**须在收集分支用 `IsExtensionMember` 判断**。**F13 实证纠正（2026-08-22）**：扩展运算符 `op_Addition` 在 VB PE 符号模型落 **`MethodKind.Ordinary`** 而非 `UserDefinedOperator`——`PEMethodSymbol.ComputeMethodKind`（:405-417）→ `ValidateOverloadedOperator` 要求含含类型为操作数类型（`Operators.vb:373-395`，`OverloadedOperatorTargetsContainingType` :469），分组类型宿主不满足 → 校验失败落 Ordinary。**F14/F15 运算符挂点须按 `op_` 名或 `IsExtensionMember` 识别，不可按 `MethodKind.UserDefinedOperator` 过滤**。

**(b) 四处 binder 追加调用**

`NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66` 的 `CollectProbableExtensionMethodsInSingleBinder` 在现有 `AppendProbableExtensionMethods` 后**追加** `AppendProbableExtensionMembers`（命名空间场景追加命名空间级 `GetExtensionMembers`），收集属性/运算符到独立数组。`AddExtensionMethodLookupSymbolsInfoInSingleBinder`（各 binder）同步补扩展成员名（供智能提示）。

**(c) 快速路径**：`MightContainExtensionMethods`（`PENamedTypeSymbol.vb:919`）复用，无新标志。

---

## 4. 改动点 3（A-③）：归约层——复用 `ReducedExtensionPropertySymbol` + 泛型推断

### 4.1 现状（源码实证）

- `ReducedExtensionPropertySymbol`（`VB\Binding\Binder_XmlLiterals.vb:1524-1677`）**已存在**：构造收 `PropertySymbol`（断言 `IsShared && ParameterCount = 1`，`:1531-1532`——剥掉接收者参数）；`ReceiverType` 返回 `_originalDefinition.Parameters(0).Type`（`:1549-1553`）；`Parameters` 返回空（`:1667-1671`）；`IsShared` 返回 `False`（`:1655-1659`，视为实例属性）；`GetMethod`/`SetMethod` 走 `ReduceAccessorIfAny`（`:1597-1601`）。**F14 修正：C# 14 扩展属性形态（`IsShared=False, ParamCount=0`）与现有构造断言不符，不能直接复用，须 2 参构造传显式接收者（见 §4.2(a)）**。
- `ReducedExtensionMethodSymbol.Create`（`VB\Symbols\ReducedExtensionMethodSymbol.vb:35-204`）——归约模板：`CollectReferencedTypeParameters`（:66）收集接收者引用的类型参数 → `TypeArgumentInference.Infer`（:94-112）从实例类型推断 → `CheckConstraints`（:150-156）→ `DoesReceiverMatchInstance`（:177）。**泛型归约的推断流程模板**。
- `MethodSymbol.ReduceExtensionMethod`（`VB\Symbols\MethodSymbol.vb:817-819`）→ `ReducedExtensionMethodSymbol.Create`。

### 4.2 改动形状

**(a) 扩展属性归约（无泛型）——F14 修正：不能直接 `New ReducedExtensionPropertySymbol(prop)`**

**F14 实证（2026-08-22，probe `ExternLib.dll`）**：C# 14 扩展属性在分组类型里是 `IsShared=False, ParamCount=0`（接收者**不是**参数，存于 `<M>$` 标记类型的 `<Extension>$(receiver)` 方法），而 `ReducedExtensionPropertySymbol` 现有构造断言 `IsShared && ParameterCount = 1`（`Binder_XmlLiterals.vb:1531-1532`，为 `InternalXmlHelper.Value` 形态设计）。**直接 `New ReducedExtensionPropertySymbol(prop)` 会触发断言**——原设计「接收者仅 1 参」假设不成立。

实现改为：`ReducedExtensionPropertySymbol` 增 2 参构造 `New(originalDefinition, receiverType)`（显式接收者，`:1539`），`ReceiverType` 优先返回显式接收者；1 参构造（`InternalXmlHelper.Value` 形态）行为不变。接收者类型来源 = 新增 `PENamedTypeSymbol.GetExtensionReceiverType()`（`PENamedTypeSymbol.vb:1039`，读 `<M>$` 标记方法单参类型；泛型块返回分组类型自身类型参数 `$T0`，probe 实证 `ContainingSymbol = <G>$...(Of $T0)`）。C# 14 形态下 accessor 无接收者参数，`ParameterCount`/`Parameters` 不再减 1，且 `CallsiteReducedFromMethod` 映射到顶层静态 shim（真正可调用目标）。

**(b) 泛型扩展属性/运算符归约：仿 `ReducedExtensionMethodSymbol.Create` 补推断 + 运算符 MethodKind 定案**

- 扩展属性符号可能是泛型（接收者含类型参数——C# 14 泛型块 `extension(Of T)(value)` 使分组类型泛型，接收者 = 分组类型类型参数）。归约须：
  1. 从实例类型收集接收者引用的类型参数（仿 `Create` :66 `CollectReferencedTypeParameters`）；
  2. `TypeArgumentInference.Infer` 从接收者实参推断（仿 :94-112，构造 `BoundRValuePlaceholder(instanceType)`）；
  3. `CheckConstraints`（仿 :150-156）；
  4. 生成「固定类型参数的归约属性符号」。
  **F14 实现**：`ReducedExtensionMemberReducer.InferGroupingTypeArguments`（`ReducedExtensionMemberSymbols.vb:255`）用**合成 `SignatureOnlyMethodSymbol`**（类型参数 = 分组类型类型参数、单参 = 接收者类型）跑既有 `TypeArgumentInference.Infer`，`TypeSubstitution.Create(groupingType,...)` + `constructedGrouping.CheckConstraints(...)` 查约束，`ConstructGroupingType` 构造分组类型（未固定类型参数用类型参数自身填充，对齐 C# `fillNotInferredTypeArguments`），从构造类型取成员（签名已替换）。归约属性符号仍复用 `ReducedExtensionPropertySymbol`（2 参构造，携带 `fixedTypeParameters` 由构造类型承载，无需单独字段）。
- 扩展运算符归约：运算符是 `MethodSymbol`（**F13 实证：VB PE 落 `MethodKind.Ordinary`**，非 `UserDefinedOperator`——`ValidateOverloadedOperator` 要求含含类型为操作数类型，分组类型宿主不满足，见 §3.2(a)）。**F9 已定案（2026-08-22）：不能复用 `ReducedExtensionMethodSymbol.Create`，须新增运算符专用归约**——两个硬性障碍：① 入口 `Create` 要求 `MayBeReducibleExtensionMethod`（`MethodSymbol.vb:424-427`）且 `:185` 强制 `IsExtensionMethod`，而扩展运算符带 `[ExtensionMarker]` 不带 `[Extension]`，被拦；② 即便放宽入口，归约后 `MethodKind` 覆盖为 `ReducedExtension`（`ReducedExtensionMethodSymbol.vb:353-355`）。
  **F14 定案（2026-08-22）——运算符归约后形态**：新建 `ReducedExtensionOperatorSymbol`（`ReducedExtensionMemberSymbols.vb:484`），**保留全部操作数参数**（接收者 = 首参 = 左操作数，`Parameters` 不剥），`ReceiverType = 首参类型`，**`MethodKind = UserDefinedOperator`**，`IsMethodKindBasedOnSyntax = False`，`IsShared = True`，`ReducedFrom`/`CallsiteReducedFromMethod` = 原（构造后）运算符。复用 `Create` :66-180 推断模板（入口判据换 `IsExtensionMember` + 运算符名识别——F14 用 `OverloadResolution.GetOperatorInfo(method.Name).ParamCount <> 0`，因扩展运算符落 `Ordinary` 不可按 `MethodKind` 过滤）。
  **取舍（如实标注）**：① 转 `UserDefinedOperator` 使归约后符号通过 `CollectUserDefinedOperators`（`Operators.vb:2917-2931`）的 `method.MethodKind = opKind` 过滤——F15 只需在收集处追加扩展运算符候选并归约入 `opSet`，**零改动运算符决议机制**（`OperatorInvocationOverloadResolution` 断言 `ParameterCount = 2`，保留两参使其满足）。② `ValidateOverloadedOperator`/`OverloadedOperatorTargetsContainingType`（`Operators.vb:373-395/:469`）因 `IsMethodKindBasedOnSyntax=False`（`OrElse` 短路，`:2928`）被**正确绕开**——归约后含含类型是分组类型（非操作数类型），该校验本就不适用。③ 代价：符号「看起来」是普通用户定义运算符（`ContainingType`=分组类型），任何不短路走 `ValidateOverloadedOperator` 的路径需 F15 复核。
  **备选否决**：保持 `Ordinary` 则 F15 须扩 `CollectUserDefinedOperators` 过滤且仍须保留两参，收益小、F15 工作量大。

---

## 5. 改动点 4（A-④）：合并/挂点层——泛化汇聚点 + 追加扩展运算符候选

### 5.1 现状（源码实证）

- 汇聚点 `LookupForExtensionMethodsIfNeedTo`（`VB\Binding\Binder_Lookup.vb:1152-1171`）：`result` 好且非 `EagerlyLookupExtensionMethods` 且首位非 Method 即返回（:1162-1166，实例成员优先门）；否则 `LookupForExtensionMethods`（:1169）+ `MergeInternalXmlHelperValueIfNecessary`（:1170）。
- `MergeInternalXmlHelperValueIfNecessary`（:1259-1291）：**只收 `InternalXmlHelper.Value`**（`:1269` `name = StringConstants.ValueProperty` + `:1275` `IsOrImplementsIEnumerableOfXElement`），归约用 `New ReducedExtensionPropertySymbol(...)`（:1286），合并用 `MergePrioritized`（:1290，低优先级）。
- `CollectUserDefinedOperators`（`VB\Semantics\Operators.vb:2847-2903`）：类型层级收集（type1/type2 + 继承攀爬 `:2866-2901`），逐成员 `type.GetMembers(opName)` 收 `MethodKind.UserDefinedOperator`（:2917-2932）。**不查扩展运算符**。

### 5.2 改动形状

**(a) 泛化 `MergeInternalXmlHelperValueIfNecessary` → `MergeExtensionPropertiesIfNecessary`（改签名/改体，保留 InternalXmlHelper 特例）**

- `Binder_Lookup.vb:1259` 的函数扩为「先查既有 InternalXmlHelper.Value 特例（保留），再沿 binder 链收集作用域内 `[Extension]` 类的扩展属性候选」——复用 §3 的四处 binder 收集结果，对每个扩展属性候选 `New ReducedExtensionPropertySymbol`（泛型走 §4b）后 `lookupResult.MergePrioritized(singleResult)`（仿 :1290，保证扩展属性优先级低于实例成员）。
- `LookupForExtensionMethodsIfNeedTo`（:1152）在 `:1170` 调用点改名/扩调用。**F9 已定案（2026-08-22）**：扩展属性的 `arity`/`LookupOptions` 过滤（属性有 `ParameterCount`，`CheckViability` 已处理 arity）；setter **不单独打标**——C# 发射器把 `[ExtensionMarker]` 打在属性符号上（`SourcePropertySymbolBase.cs:1474`），`set_P(obj,v)` 随属性导入（`PENamedTypeSymbol.cs:2603-2634`）；`obj.P = v` **复用同一属性归约**（`ReducedExtensionPropertySymbol.SetMethod` 经 `ReduceAccessorIfAny` 包装 `set_P`，`Binder_XmlLiterals.vb:1679-1683`）。链式/With 运行时发射细节列为 L1/L2 测试用例补证（非实现期核实项）。

**F15 实施回填（2026-08-22）**：`MergeInternalXmlHelperValueIfNecessary` 保留为私有 helper（InternalXmlHelper 特例不动），新增 `MergeExtensionPropertiesIfNecessary` 先调它再合并扩展属性；`LookupForExtensionMethods` 签名加 `ByRef extensionMembers As ArrayBuilder(Of Symbol)` 出参，把四处 binder 收集的 `extensionMembers` 桶（`SymbolKind.Property`）交给合并函数，归约走 `ReduceExtensionMember(container, member, ...)` + `MergePrioritized`（低优先级，实例成员优先的 gate `Binder_Lookup.vb:1162-1166` 未动）。`Object` 接收者晚绑定（`ShouldLookupExtensionMethods` :1174-1179）门保留。

**(b) `CollectUserDefinedOperators` 追加扩展运算符候选**

- `Operators.vb:2847` 的 `CollectUserDefinedOperators(type1, type2, ...)` 在类型层级收集完成后（:2901 后），追加：遍历作用域（`Imports`）`[Extension]` 类的分组类型，收集接收者类型匹配 operand 的扩展运算符成员，经 `ReducedExtensionMemberReducer.ReduceExtensionMember` 归约后并入 `opSet`。**F14 定案（2026-08-22）**：归约后符号已转 `MethodKind.UserDefinedOperator` 且保留两参（`IsMethodKindBasedOnSyntax=False`），**现有 `method.MethodKind = opKind` 过滤（:2921）与 `OperatorInvocationOverloadResolution` 的 `ParameterCount=2` 断言（:3171）均直接满足，无需扩过滤**——F15 只需收集 + 归约 + 入 `opSet`（§4.2(b) 取舍）。

**F15 实施回填（2026-08-22，实证驱动）**：
- **作用域获取定案**：`CollectUserDefinedOperators` 加 `Optional binder As Binder = Nothing`（所有 `Resolve*` 调用点有 binder）；**按需重新收集**扩展运算符——新增 `Binder.CollectExtensionMembersFromBinders`（`Binder.vb` Friend 入口）沿 binder 链调 `CollectProbableExtensionMethodsInSingleBinder(opName, ...)` 收集 `[ExtensionMarker]` 运算符（运算符决议路径无预存 `extensionMembers` 桶，复用需跨层状态）。接收者匹配由归约内 `DoesReceiverMatchInstance` 负责，`GetTypeToLookForOperatorsIn` 处理后的 type1/type2 作为 instanceType 传入（先 type1 后 type2，同运算符去重）。
- **分组类型运算符是抛异常 stub（实证）**：`VecExt.<G>$...op_Addition` IL=`newobj; throw`（`73-1B-00-00-0A-7A`），真实实现在顶层容器 `VecExt.op_Addition`。`ReduceExtensionOperator` 对非泛型分组类型**优先用 `FindTopLevelShim` 取顶层实现**作为归约符号包装目标（否则运行期 `NotSupportedException`）。泛型扩展运算符的顶层 shim 未替换（列边界）。
- **归约运算符 `ReducedFrom`→Nothing**：`ReducedExtensionOperatorSymbol.ReducedFrom` 返回 Nothing（保留全部操作数，非 receiver-stripped 扩展方法形态），否则 `IsReducedExtensionMethod=True` 触发 `MethodCandidate` 断言失败且 `paramIndex+1` 误判。`CallsiteReducedFromMethod` 保持原运算符。
- **扩展属性绑定路径两处适配**：① `CreateBoundCallOrPropertyAccess` 属性分支对 C# 14 形态（新增 `ReducedExtensionPropertySymbol.HasExplicitReceiverType`）不走 `reducedFrom.Parameters(0)`（C# 14 形态越界），接收者经 `PassArgumentByVal` 转 r-value（lowering 时接收者成为顶层 shim 首参，须为值）；② setter `x.P = v` 复用同一归约属性（`SetMethod`→`ReduceAccessorIfAny`），无需单独打标。

---

## 6. 改动点 5（B-①）：绑定——`Binder_Expressions.vb:2913` 改判 + 约束接口查找

### 6.1 现状（源码实证）

- `VB\Binding\Binder_Expressions.vb:2910-2928`：`left.Kind = BoundKind.TypeExpression` 时 `type = DirectCast(left, BoundTypeExpression).Type`；`type.TypeKind = TypeParameter` → **无条件** `ReportDiagnosticAndProduceBadExpression(ERRID.ERR_TypeParamQualifierDisallowed)`（:2913-2914，BC32098）；否则 `LookupMember(lookupResult, type, rightName, rightArity, options, useSiteInfo)`（:2921）+ `BindSymbolAccess(..., QualificationKind.QualifiedViaTypeName, ...)`（:2924）。
- `ERR_TypeParamQualifierDisallowed = 32098`（`VB\Errors\Errors.vb:1165`）。
- 约束接口集获取：`ConstraintsHelper.GetNonInterfaceConstraint`（`VB\Symbols\ConstraintsHelper.vb:771-802`）用 `typeParameter.ConstraintTypesWithDefinitionUseSiteDiagnostics(useSiteInfo)` 遍历约束。接口约束 = 该集合中 `IsInterfaceType()` 者；effective interface set = 接口约束 + 其 `AllInterfaces`（`Core\Portable\Symbols\ITypeSymbol.cs:53`）。
- C# 参考：`CS\Binder\Binder_Expressions.cs:8144-8159`——类型参数分支用 `LookupMembersWithFallback(... options: options | MustNotBeInstance | MustBeAbstractOrVirtual)`（:8147），`IsMultiViable` 则 `CheckFeatureAvailability(IDS_FeatureStaticAbstractMembersInInterfaces)`（:8151）后绑定，空则 `ERR_LookupInTypeVariable`（:8156）。

### 6.2 改动形状

`Binder_Expressions.vb:2913-2914` 改判为：

```vb
If type.TypeKind = TYPEKIND.TypeParameter Then
    ' 先查约束接口（effective interface set）是否有名为 rightName 的共享成员
    ' typeParameter 是 TypeParameterSymbol；遍历 ConstraintTypesWithDefinitionUseSiteDiagnostics 中 IsInterfaceType 者 + AllInterfaces
    For Each iface In 接口约束集   ' 实现期核对：用 ConstraintsHelper 扩展方法枚举 effective interface set
        Dim candidate = LookupResult.GetInstance()
        LookupMember(candidate, iface, rightName, rightArity, options Or LookupOptions.MustNotBeInstance, useSiteInfo)
        If candidate.HasSymbol Then
            ' 绑定到约束接口共享成员（仿 :2921-2924 普通类型路径）
            lookupResult.SetFrom(candidate)
            candidate.Free()
            Return BindSymbolAccess(node, lookupResult, options, left, typeArguments, QualificationKind.QualifiedViaTypeName, diagnostics)
        End If
        candidate.Free()
    Next
    ' 无则维持 BC32098
    Return ReportDiagnosticAndProduceBadExpression(diagnostics, node, ErrorFactory.ErrorInfo(ERRID.ERR_TypeParamQualifierDisallowed), left)
End If
```

- `options Or MustNotBeInstance`（`LookupOptions.vb:39`）确保只取共享成员。**F9/F10 已定案（2026-08-22）**：SAIM 识别 = `IsStaticAbstractInterfaceMember`（`PEMethodSymbol.vb:926-930`，`ContainingType.IsInterfaceType() AndAlso IsShared AndAlso IsMustOverride`）；VB 全树无其他 SAIM 处理，识别已新建。
- `rightArity`/`typeArguments` 透传（对齐 :2921 的 arity 过滤）。
- **同名冲突**：多个接口约束有同名共享成员时合并为方法组由 `BindSymbolAccess` 重载决议处理；无成员维持 BC32098。
- **F10 已实现（2026-08-22）**：实际绑定在 `Binder_Expressions.vb:2920-2976`——约束接口集查找用 `MustNotBeInstance` + **`allAreStaticAbstract` 过滤**（比设计伪码更严：全部候选须为静态抽象接口成员才绑，否则维持 BC32098）；通用 helper `IsStaticAbstractInterfaceMember(symbol)`（:3044-3057）覆盖 Method/Property/替换符号；运行时门控于 :2959-2962 报 BC32134。普通类型路径 :2983-2989 未动。

---

## 7. 改动点 6（B-②）：CodeGen——`constrained.` + 虚调用

### 7.1 现状（源码实证）

- VB CodeGen **已有** `ConstrainedCallVirt` 支持：`VB\CodeGen\EmitExpression.vb:984`（`CallKind.ConstrainedCallVirt` 枚举值）、`:1054`（`callKind = CallKind.ConstrainedCallVirt` 时 `EmitReceiverRef(receiver, isAccessConstrained:=True, ...)`）、`:1111-1112`（`Case CallKind.ConstrainedCallVirt: _builder.EmitOpCode(ILOpCode.Constrained)`）。用于实例虚调用（接口方法、`MustOverride` 经类型参数）。
- SAIM 调用需 `constrained. !!T` + **`call`**（**F10 实证纠正，2026-08-22**：设计原写 callvirt，C# 参考对 static abstract/virtual 经类型参数发射 `constrained.`+`call`——`CS\CodeGen\EmitExpression.cs:1683-1694` `EmitStaticCallExpression`；callvirt 需栈上实例接收者，静态成员无接收者故非法；Regular 模式 harness IL dump `FE 16 <typespec> 28 <memberref>` 与 C# 逐字节同形证实）。`constrained` 前缀对「静态抽象接口成员经类型参数调用」在 CLR 语义上 = 用实际类型实参分发（C# 与 VB 同 IL，`static-abstracts-in-interfaces.md:10`）。

### 7.2 改动形状

- 绑定 `T.Zero`/`T.Add` 生成的是类型参数上调用**接口共享成员**的 bound 节点（`BoundCall`/`BoundPropertyAccess`，成员 `ContainingType` 是接口、`IsShared`、接收者类型是类型参数）。
- `EmitExpression.vb` 的调用发射处：对「接收者是类型参数 + 目标是接口共享抽象成员」的调用选 **新增 `CallKind.ConstrainedCall`**（**F10 已实现**：`EmitExpression.vb:982-985/1009-1019/1127-1131` 发射 `constrained.`+`call`）。配套改动：`Binder_Invocation.vb:858-864/896-898` 对 SAIM 经类型参数**保留接收者**（`clearIfShared:=False`，让发射器拿到类型参数）；`Binder.vb:961-965` `ReportDiagnosticsIfObsoleteOrNotSupported` 加 `receiverIsTypeParameter` 可选参（缺省 False），仅抑制「SAIM 经类型参数」的 BC37314（接口名访问/实例访问仍报）。

---

## 8. 改动点 7（B-③）：运算符——`GetTypeToLookForOperatorsIn` 补接口约束共享运算符

### 8.1 现状（源码实证）

- `VB\Semantics\Operators.vb:2942-2950`——`GetTypeToLookForOperatorsIn(type, ...)`：`GetNullableUnderlyingTypeOrSelf`（:2943）→ `type.Kind = TypeParameter` 则 `DirectCast(type, TypeParameterSymbol).GetNonInterfaceConstraint(useSiteInfo)`（:2945-2946，只取非接口约束）→ 返回。类型参数操作数的共享运算符被跳过。
- `ConstraintsHelper.GetNonInterfaceConstraint`（`ConstraintsHelper.vb:771-802`）只取非接口约束。
- C# 对齐：`static-abstracts-in-interfaces.md:229`——用户定义转换运算符考虑 `Sᵢ`/`Tᵢ` 的 **effective interface set**。

### 8.2 改动形状

`GetTypeToLookForOperatorsIn`（:2942）改为：类型参数时，除返回 `GetNonInterfaceConstraint`（非接口约束）外，**并行返回接口约束集**（供上层在接口约束上查共享运算符）。**实现期核对**：该函数当前返回单 `TypeSymbol`，需改返回结构（如返回非接口约束 + 由调用方 `CollectUserDefinedOperators` 对接口约束集再收集）或新增兄弟函数 `GetInterfaceConstraintsToLookForOperatorsIn`。`CollectUserDefinedOperators`（:2847）在对 `type1`/`type2` 收集完后，追加在接口约束集的接口上收集 `MethodKind.UserDefinedOperator` 的共享运算符（与扩展运算符候选合并，见 §5b）。

### 8.3 F18 实施回填（2026-08-22，实施+测试双确认）

- **`GetTypeToLookForOperatorsIn` 方案定案**：选「**新增兄弟收集**」而非改返回结构——`GetTypeToLookForOperatorsIn` **零改动**，仍返回单 `TypeSymbol`（非接口约束，供类型层级攀爬 :2866-2908）；`CollectUserDefinedOperators` 顶部捕获 `originalType1`/`originalType2`，新增 `CollectInterfaceConstraintSharedOperators`（`Operators.vb`）用原操作数类型参数算 effective interface set（接口约束 + `AllInterfacesWithDefinitionUseSiteDiagnostics`），在类型层级收集后、扩展运算符收集前并行追加。与 F15 `CollectExtensionUserDefinedOperators` **并列**。
- **归约定案：不需要归约（直接入 opSet）**。SAIM 运算符在构造接口 `IV(Of U)` 上已是 `op_Addition(U, U)`（参数表即操作数形态），`OperatorInvocationOverloadResolution` 的 `ParameterCount=2` 断言、`MethodCandidate` 的 `ReducedFrom Is Nothing` 断言、`CombineCandidates` 恒等转换全部自然满足。与 F14/F15 扩展运算符需归约（分组类型参数非操作数形态）成对照。
- **识别定案**：SAIM 运算符按「`IsShared AndAlso IsMustOverride AndAlso ContainingType.IsInterfaceType()`」识别（F13 实证落 `MethodKind.Ordinary`，**不可按 `MethodKind.UserDefinedOperator` 过滤**），与 F10 `IsStaticAbstractInterfaceMember` 等价；加 `ParameterCount = opInfo.ParamCount` 过滤 + `HashSet` 去重。
- **实现期新发现 1（设计 §8 未提及的门）**：`ResolveBinaryOperator` 的 `CanContainUserDefinedOperators`（`TypeSymbolExtensions.vb:523`）在「类型参数约束全为接口」时返回 False → 直接 `BinaryOperatorKind.Error`，**走不到 `CollectUserDefinedOperators`**。须放开「约束为接口 → True」（`:526`）。行为影响为零：接口约束无 SAIM 运算符时分辨率仍无候选 → 同 `ERR_BinaryOperands3`。
- **实现期新发现 2（Debug.Assert）**：`BoundUserDefinedBinaryOperator.Validate`（:41）与 `BoundUserDefinedUnaryOperator.Validate`（:33）断言 `MethodKind.UserDefinedOperator`；SAIM 运算符落 `Ordinary` 触发断言（DEBUG 构建）。放宽为「`UserDefinedOperator` **或** `Ordinary`+共享+抽象+接口」。
- **CodeGen 覆盖定案**：F10 `EmitCallExpression` 的 `CallKind.ConstrainedCall` 判定要求「接收者 = 类型参数 TypeExpression」，运算符调用原接收者为 Nothing → **F10 发射器本身未覆盖运算符**。最小改动 = 绑定侧（`Binder_Operators.vb` `BindUserDefinedNonShortCircuitingBinaryOperator`/`BindUserDefinedUnaryOperator`）给 SAIM 运算符调用挂 `New BoundTypeExpression(node, 类型参数)` 接收者（helper `GetStaticAbstractOperatorReceiver`），使 F10 的 `isStaticAbstractViaTypeParameter` 检测（`Binder_Invocation.vb:858-864`）命中 → BC37314 抑制 + `clearIfShared:=False` 保留接收者 → **F10 发射路径零改动**即产出 `constrained.`+`call`。IL 与 C# `EmitStaticCallExpression` 核心序列逐字节同形（harness dump 实证）。
- **测试**：S26/S26b（无诊断 + 运行期 30）/S26c（无约束 BC30452）/S26d（具体类型不回归）/S26e（一元 -10）全绿；回归 `StaticAbstractMembersInInterfacesTests` 70/70、运算符/CodeGen/扩展全绿。

---

## 9. 改动点 8（B-④）：运行时门控 + 诊断

### 9.1 现状（源码实证）

- 运行时钩子**已在位**：`VB\Symbols\AssemblySymbol.vb:335-359`（`SupportsRuntimeCapability`，`Case RuntimeCapability.VirtualStaticsInInterfaces` 于 :348-349）→ `:404-408`（`RuntimeSupportsVirtualStaticsInInterfaces`，检查 `SpecialMember.System_Runtime_CompilerServices_RuntimeFeature__VirtualStaticsInInterfaces`，`Core\Portable\SpecialMember.cs:165`）。
- C# 参考：`CS\Binder\Binder_Expressions.cs:10007-10030`——`CheckReceiverAndRuntimeSupportForSymbolAccess`：符号 `ContainingType.IsInterface && IsStatic && (IsAbstract || IsVirtual)` 时，接收者非类型参数 → `ERR_BadAbstractStaticMemberAccess`（:10022）；`Not RuntimeSupportsStaticAbstractMembersInInterfaces && SourceModule != ContainingModule` → `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`（:10026-10028）。

### 9.2 改动形状

- `Binder_Expressions.vb` 绑定 `T.Zero`/`T.Add` 处（§6 改判后）追加：若 `Not Me.Compilation.Assembly.SupportsRuntimeCapability(RuntimeCapability.VirtualStaticsInInterfaces)` **且** 成员所在模块非当前源码模块 → 报运行时不受支持诊断（替换正常绑定结果）。**复用** `AssemblySymbol.vb:348-349` 钩子，**不从零建**。
- **诊断码族（F9 已定案，2026-08-22）**：①「约束接口无同名共享成员」**复用 BC32098**（`Errors.vb:1165`，语义不变）；②「运行时不受支持」**新建专用 BC 码 `32134`**（仿 C# `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`，独立码让测试可区分未命中/运行时门控），注册两处：`Errors.vb` ERRID 枚举 + `VBResources.resx` 消息文案（`VBResources.resx:2745` 为 BC32098 文案先例、:4400 为 shebang 新码先例）；`ErrorFacts.IsBuildOnlyDiagnostic`（`ErrorFacts.vb:13`）**通常不追加**（普通编译错误）。32134 经 F9 验证确认空闲（32133=Errors.vb:1202 后 32134~32199 全空）。
- 特性门控（C# `IDS_FeatureStaticAbstractMembersInInterfaces`，`Binder_Expressions.cs:8151`）：**F9 定案：不做 LanguageVersion 门控**（fork `Latest = VisualBasic17_13`，纯消费、零新语法，仅运行时检查）。

---

## 10. 零改动清单 + 边界

### 10.1 零改动清单

| 文件/层 | 不改理由 |
|---|---|
| `VB\Symbols\NamespaceSymbol.vb:488-516`（`BuildExtensionMethodsMap`） | 现有扩展方法 map 构建路径（字典名→方法桶）不动，新增属性/运算符走并列收集（§3） |
| `VB\Symbols\MethodSymbol.vb:424`（`MayBeReducibleExtensionMethod`）/ `:817`（`ReduceExtensionMethod`） | 经典扩展方法归约路径不动，新增归约并列 |
| `VB\Binding\Binder_Lookup.vb:1174-1179`（`ShouldLookupExtensionMethods`） | `Not container.IsObjectType()` 门保留——`Object` 接收者维持晚绑定（边界） |
| `VB\Binding\Binder_Invocation.vb:610-614` | 组内扩展成员唯一性规则沿用（扩展属性/运算符并入方法组时复用） |
| `VB\Symbols\Metadata\PE\PEAssemblySymbol.vb:254`（`MightContainExtensionMethods`） | 快速路径自动覆盖，零改动 |
| `VB\BoundTree\BoundMethodGroup.vb:40`（`ExtensionMethodGroup`） | 符号呈现**定案为「包装」**（F9 2026-08-22：`ExtensionMethodGroup` 是延后收集的 lazy 包装、非呈现层；归约符号自动继承原成员 Name/Type/ContainingSymbol/Locations，扩展属性/运算符经归约符号接入，本设计不动其结构） |
| `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` / `VB\Compilation\VisualBasicCompilation.vb` | 消费侧零新语法，宿主层无改动 |
| `VB\CommandLine\VisualBasicCompiler.vb` | `.vbx` 与 Regular 同 `SourceCodeKind.Script`/同一编译器，无区分开关 |

### 10.2 边界

- **`Object` 接收者维持晚绑定**：`ShouldLookupExtensionMethods`（`Binder_Lookup.vb:1176`）门保留——`Object` 静态类型上扩展成员不可见，Strict Off 晚绑定通道保留（与扩展方法现状一致，既定取舍非缺陷）。
- **扩展成员仅早绑定参与**：只要接收者静态类型已知即早绑定命中；`Object` 静态类型才落晚绑定。实证里 Strict Off 静默晚绑定属「未找到扩展运算符」的回退，接入扩展运算符决议后早绑定命中即消除。
- **同名优先级**：实例成员优先 → 扩展成员回退（`Binder_Lookup.vb:1162-1166`）；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`）。
- **`Imports` 语义一致**：扩展属性/运算符与扩展方法一样需 `Imports` 所在命名空间才参与候选（四处 binder 收集天然满足）。
- **`static abstract` vs `static virtual`**：`static abstract` 只能经类型参数 `T.M`；`static virtual`（DIM 静态版）经类型参数仍 BC32098（F10 确认）。**经接口名直呼 static abstract/virtual 均报 BC37314**（F11 实证回填：与 fork 内 C# CS8926 对齐，spec `static-abstracts-in-interfaces.md:264-266` static virtual 只能在类型参数上调用——原「static virtual 可经接口名呼」假设不成立，接受为边界；DIM 直呼属双端新功能提案）。
- **SAIM 属性 setter 边界（F10 验证发现，F11 跟进）**：`T.P = v` 的 setter 阶段路径 `Binder_Statements.vb:1970` 未传 `receiverIsTypeParameter`，setter 阶段会报 BC37314——F11 补抑制或列为边界用例。
- **`.vbx` 与 Regular 语义一致**：同一编译器同一路径，不分裂。
- **声明侧不在范围**：VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性 → 维持现状错误（BC30270/BC30273），作后续提案。
- **泛型约束精度**：扩展属性/运算符的接收者含类型参数时归约须类型推断 + 约束检查（§4b）；SAIM 经类型参数绑定时仅接口约束集内查找，非接口约束（`GetNonInterfaceConstraint`）不参与。

---

## 11. 实现期核对项清单（未核实，如实标注）

| # | 项 | 现状证据 | 待定内容 |
|---|---|---|---|
| 1 | **WellKnown 属性名** | 本树表条目 `System_Runtime_CompilerServices_ExtensionMarkerAttribute`（`WellKnownTypes.cs:343/:717`）；C# spec 名 `ExtensionMarkerNameAttribute`（`extensions.md:612-620`）；发射/导入用 `ExtensionMarkerAttribute` | 按目标 C# 14 库**实际发射属性名**对齐（读 `PEModule.HasExtensionMarkerAttribute`，`PEModule.cs:1061`） |
| 2 | **诊断码族** | 现有 `ERR_TypeParamQualifierDisallowed = 32098`（`Errors.vb:1165`）；C# 专用码 `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`（`Binder_Expressions.cs:10028`） | SAIM 缺失/运行时不受支持的 BC 码号定案（新建或复用） |
| 3 | **SAIM 成员在 VB 符号模型的识别** | dump 原始标志 `Static|Abstract|Virtual`，`newslot=False`；VB 全树无 `StaticAbstract` 绑定/收集处理 | `PEMethodSymbol` 导入后如何标记「接口共享抽象成员」（`IsShared` + 接口声明 + 抽象判定），供 §6 查找过滤 |
| 4 | **扩展成员符号呈现** | `ExtensionMethodGroup`（`BoundMethodGroup.vb:40`）存在 | 合成扩展属性/运算符符号接入回退决议，还是包装底层 `[ExtensionMarker]` 成员（meeting 开放项） |
| 5 | **收集桶类型** | `ArrayBuilder(Of MethodSymbol)` 贯穿现有扩展方法链 | 属性/运算符走并列数组（`ArrayBuilder(Of Symbol)`/`ArrayBuilder(Of PropertySymbol)`）的落点 |
| 6 | **扩展属性 setter / 链式 / `With`** | 无实证 | 随 C# 发射消费（`set_P(obj,v)` 对称），细节实现期实证枚举 |
| 7 | **上游 Roslyn 基准是否含 VB SAIM 参考实现** | `upstream-merge.md` 基准 commit 未核对（`Suspect`） | 若有可低成本合并；运行时钩子在位、C# 参考路径完整，不阻塞 |
| 8 | **分组类型遍历 API** | `PENamedTypeSymbol.GetTypeMembers` 返回嵌套类型 | `GetTypeMembers(name:="")` 是否返回全部 `<G>$` 分组类型，按实测定 |
| 9 | **运算符归约复用** | `ReducedExtensionMethodSymbol.Create` 断言 `MethodKind <> ReducedExtension`（:49） | 扩展运算符（`MethodKind.UserDefinedOperator`）复用或新增专用归约 |

### 九项定案回填（F9 实施+验证通过，2026-08-22）

> 完整证据见 `verification-checklist.md`（同目录，实施者产出 + 验证者复核）。本节为 §11 各行定案结论。

| # | 定案 |
|---|---|
| 1 | 元数据名 = `System.Runtime.CompilerServices.ExtensionMarkerAttribute`（非 spec 名 `ExtensionMarkerNameAttribute`）；复用 Core `PEModule.HasExtensionMarkerAttribute`（`PEModule.cs:1061`） |
| 2 | 「未命中」复用 BC32098；「运行时不受支持」新建 `32134`（空闲已确认），注册 `Errors.vb` ERRID 枚举 + `VBResources.resx` 消息文案（`ErrorFacts` 通常不追加） |
| 3 | SAIM 识别 = `ContainingType.IsInterfaceType() AndAlso IsShared AndAlso IsMustOverride`（`PEMethodSymbol.vb:914/:827`），加 Friend 属性 `IsStaticAbstractInterfaceMember`，无需新字段；VB 全树无现成 SAIM 处理（已 grep 确认） |
| 4 | 符号呈现定案「包装」底层 `[ExtensionMarker]` 成员（复用归约符号），不合成 |
| 5 | 新收集路径统一 `ArrayBuilder(Of Symbol)` 并列桶（对齐 C# `GetAllExtensionMembers`）；运算符入方法桶须扩 `MayBeReducibleExtensionMethod OrElse IsExtensionMember` |
| 6 | `[ExtensionMarker]` 打在属性不在 setter；`obj.P = v` 复用属性归约；链式/With 列测试 |
| 7 | 维持 Suspect、非阻塞（fork 修剪树无法核实上游，C# 参考路径完整） |
| 8 | `GetTypeMembers(name:="")` → **改无参 `GetTypeMembers()`**（VB 按名查字典无空键，`PENamedTypeSymbol.vb:807-817/780-784`） |
| 9 | **不能复用 `ReducedExtensionMethodSymbol.Create`**，新增运算符专用归约，复用 :66-180 模板（详见 §4.2(b)） |

---

## 12. 无副作用测试矩阵（设计级要点）

> 完整分层矩阵见 `test-plan.md`（F3）。约束：不发起网络、不写文件（除既有测试装置）、不启动进程、不写注册表。测试装置需引用含 C# 14 扩展成员 / SAIM 的测试库（沿用 `tmp/exp-consume/` 产物或 `TestReferences`）。

- **L1 语义层**（`Compilers\VisualBasicSemanticTest\`）：`"hello".CharCount` → 5；`New MyVec(1,2) + New MyVec(10,20)` → (11,22)；`VBSum(Of MyNum)` 泛型算法（`T.Zero`/`T.Add`）→ 30；Strict On/Off 分叉；`Object` 接收者晚绑定保留；`Imports` 作用域（有/无 Imports）；`static virtual` 经接口名；同名优先级（实例成员优先）。
- **L2 REPL 层**（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`，`CreateRunner` + `TestConsoleIO` 纯内存）：`.vbx` 脚本里扩展属性/运算符/`T.Zero` 可用；与 Regular 语义一致。
- **L3 符号/元数据层**（`Compilers\VisualBasicSymbolTest\`）：`PEMethodSymbol.IsExtensionMember`/`ReducedExtensionPropertySymbol` 归约正确性；`HasExtensionMarkerAttribute` 读取。
- **L4 Regular 模式**（`Compilers\VisualBasicSemanticTest\`）：`.vb` 项目同用例，验证与 `.vbx` 一致；运行时不受支持门控（构造无 `VirtualStaticsInInterfaces` 的运行时 → 报运行时不受支持）。
- **既有测试更新**：`BindingErrorTests.vb`（BC32098 既有用例语义调整：约束接口无同名共享成员仍报 BC32098）、扩展方法既有用例不回归、`ExtensionMethodGroup` 相关智能提示用例。
- **无副作用纪律**：以上全部走内存编译/REPL 纯内存 I/O；测不了就问用户。
