# F9 实现前核对表：消费 C# 扩展成员与接口共享成员

> 日期：2026-08-22
> 总览：**九项全部定案（8 项已检查、1 项 Suspect 但非阻塞）**，无阻塞项。其中 2 项（#8 遍历 API、#9 运算符归约）发现设计文档需修正，1 项（#2 诊断码）给出明确码号建议。
> 方法：全部结论以内置 Read/Grep 精读源码得出，逐项给出 `文件:行号` 锚点；未跑编译/测试（本任务为纯源码核对）。
> 证据等级：**已检查** = 亲自读源码；**Suspect** = 无法在本仓库定案（如实标注）。

编译器前缀：`Compilers\VisualBasic\Portable\` = VB，`Compilers\CSharp\Portable\` = CS，`Compilers\Core\Portable\` = Core。文件路径相对仓库根 `C:\Users\james\Projects\VBScriptDotNet`。

---

## 1. WellKnown 属性名 —— 已检查，定案

### 定案结论
本树 C# 发射/导入扩展成员标记属性的**确切元数据名**是 `System.Runtime.CompilerServices.ExtensionMarkerAttribute`（单 string 构造参数，值 = 标记类型名）。**不是** C# 14 spec 名 `ExtensionMarkerNameAttribute`。VB PE 导入层必须匹配 `"System.Runtime.CompilerServices.ExtensionMarkerAttribute"`，直接复用 Core 共享钩子 `PEModule.HasExtensionMarkerAttribute` 即可，**不要假定 spec 名即元数据名**。

### 证据锚点
- `Core\Portable\MetadataReader\PEModule.cs:1061-1064` —— `HasExtensionMarkerAttribute(EntityHandle token, out string markerName)` 读 string 值属性，走 `AttributeDescription.ExtensionMarkerAttribute`。
- `Core\Portable\Symbols\Attributes\AttributeDescription.cs:501` —— `ExtensionMarkerAttribute = new AttributeDescription("System.Runtime.CompilerServices", "ExtensionMarkerAttribute", s_signatures_HasThis_Void_String_Only)`：**全名 + .ctor(string) 签名**。
- `Core\Portable\WellKnownTypes.cs:343` —— 枚举值 `System_Runtime_CompilerServices_ExtensionMarkerAttribute`；`:717` —— 元数据全名 `"System.Runtime.CompilerServices.ExtensionMarkerAttribute"`。
- `Core\Portable\WellKnownMember.cs:631` + `Core\Portable\WellKnownMembers.cs:4370-4372/:5851` —— `.ctor(string)` 构造条目。
- `Core\Portable\Symbols\WellKnownMemberNames.cs:510/:515/:520` —— `ExtensionMarkerMethodName = "<Extension>$"`、`ExtensionGroupingTypePrefix = "<G>$"`、`ExtensionMarkerTypePrefix = "<M>$"`。
- `CS\Portable\Emitter\Model\PEModuleBuilder.cs:1677` —— `SynthesizeExtensionMarkerAttribute(Symbol, string markerName)`；`:1923-1928` —— `TrySynthesizeExtensionMarkerAttribute` 用 `WellKnownMember.System_Runtime_CompilerServices_ExtensionMarkerAttribute__ctor` + string 实参（C# 发射器实际写出的属性）。
- 消费侧落点：`CS\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2585/:2616`（按 `markerName == MetadataName` 归属）、`CS\Symbols\Metadata\PE\PEPropertySymbol.cs:847`（滤掉 marker 属性）。

### 对设计文档的调整
无。设计 §2.2(c) 已正确预言「实施时以 `PEModule.HasExtensionMarkerAttribute` 读到的实际属性为准」。本条已核实为真。

---

## 2. 诊断码族 —— 已检查，定案（给码号）

### 定案结论
- **「约束接口无同名共享成员」（SAIM 查找未命中）→ 复用现有 BC32098**（`ERR_TypeParamQualifierDisallowed`），语义不变：`T.M` 找不到共享成员仍报 BC32098。不新建码。C# 侧对应场景用 `ERR_LookupInTypeVariable`，但 VB 已有 BC32098 覆盖该语义，按设计 §6.2 维持。
- **「运行时不受支持」（`VirtualStaticsInInterfaces` 缺失且成员非本源码模块）→ 新建专用 BC 码**，建议码号 **`32134`**，命名仿 C#：`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`。
  - 理由：① C# 参考实现正是独立码（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`），独立码才能让 `BindingErrorTests` 区分「未命中」与「运行时门控」，避免吞掉真正错误；② 32xxx 是泛型/约束族，紧邻 BC32098（同族语义）；③ `32134` 是 32133 之后第一个空闲码（见下）。
- **注册位置（3 处）**：① `Errors.vb` ERRID 枚举加编号；② `VBResources.resx` 加消息文案；③ `ErrorFacts.vb` 的 `IsBuildOnlyDiagnostic` 仅在「编译期致命」时追加（SAIM 运行时门控是普通编译错误，**通常不追加**，若希望归类 fatal 则仿 shebang 加）。**实现期核对提醒**：还须按设计 §9.2 在绑定处补语言版本/特性门控（fork `Latest = VisualBasic17_13`，纯消费可暂不门控，仅运行时检查）。

### 证据锚点
- `VB\Errors\Errors.vb:1165` —— `ERR_TypeParamQualifierDisallowed = 32098`。
- `VB\Errors\Errors.vb:1148-1233` —— 32xxx 族全貌：32123 结束、`:1191` 32124、`:1204` 跳 32200；32134-32199 空闲（建议落点）。
- `VB\Errors\Errors.vb:1632-1633` —— shebang 新码 `37003/37004` 注册先例（新特性码区）。
- `VB\Errors\ErrorFacts.vb:13` —— `IsBuildOnlyDiagnostic`（大 `Select Case`），shebang 两码追加于 `:1228-1229`。
- `VB\VBResources.resx:2745` —— `ERR_TypeParamQualifierDisallowed` 文案；`:4400` —— shebang 文案注册先例。
- `CS\Binder\Binder_Expressions.cs:10026-10028` —— `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces` 触发条件（`!RuntimeSupportsStaticAbstractMembersInInterfaces && SourceModule != symbol.ContainingModule`）。
- `CS\Errors\ErrorCode.cs:1949/:1956/:1959` —— `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces = 8919`、`ERR_BadAbstractStaticMemberAccess = 8926`、`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfacesForMember = 8929`（C# 独立码族模型）。

### 对设计文档的调整
建议回填：§8「诊断码族」从「实现期核对」改为定案——未命中复用 BC32098；运行时不受支持新建 `32134`（或任务最终拍板其他空闲码），注册 Errors.vb + VBResources.resx（+可选 ErrorFacts）。

---

## 3. SAIM 成员在 VB 符号模型的识别 —— 已检查，定案

### 定案结论
`PEMethodSymbol` 导入后，SAIM（原始标志 `Static|Abstract|Virtual, newslot=False`）可直接用**现有标志组合**识别，**无需新增元数据字段**：
- `IsShared` = True（Static 位）；
- `IsMustOverride` = True（Virtual AND Abstract）；
- 所在类型 `ContainingType.IsInterfaceType()`。

最省改动方案：在 `PEMethodSymbol` 上加一个 `Friend ReadOnly Property IsStaticAbstractInterfaceMember`（= `ContainingType.IsInterfaceType() AndAlso IsShared AndAlso IsMustOverride`），Part B 约束接口查找过滤直接用它（配合 `LookupOptions.MustNotBeInstance`）。若也要覆盖 `static virtual`（DIM 静态，无 Abstract 位），过滤扩展为 `IsShared AndAlso (<Virtual 位>)`（`MethodFlags` Friend 属性可直接读原始 `MethodAttributes`）。

**VB 全树当前无任何 SAIM 绑定/收集处理**——grep `StaticAbstract` / `static abstract` / `IsStaticAbstract` 均无命中，唯一相关的是运行时钩子 `VirtualStaticsInInterfaces`（已存在）。识别逻辑必须新建，但基建（运行时钩子）已就位。

### 证据锚点
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:914-916` —— `IsShared` = `(_flags And MethodAttributes.Static) <> 0`。
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:827-831` —— `IsMustOverride` = `Virtual` AND `Abstract`。
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:884-896` —— `IsOverridable`（Virtual 组合）；`:351-353` —— `Friend ReadOnly Property MethodFlags As MethodAttributes`（原始标志可直读）。
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:33` —— `_containingType As PENamedTypeSymbol`（`ContainingType.TypeKind = Interface` 可用）。
- `VB\Symbols\AssemblySymbol.vb:348-349` —— `RuntimeCapability.VirtualStaticsInInterfaces`；`:404-407` —— `RuntimeSupportsVirtualStaticsInInterfaces`（既有运行时钩子）。
- `VB\Binding\LookupOptions.vb:39` —— `MustNotBeInstance = 1 << 4`（Part B 共享成员过滤）。
- Grep 全树 `Compilers\VisualBasic\Portable\` —— `StaticAbstract`/`IsStaticAbstract` 无命中（仅 `VirtualStaticsInInterfaces` 钩子）。

### 对设计文档的调整
无实质调整。设计 §6.1「VB 全树无 StaticAbstract 处理，识别须新建」已证实；落地用「现有标志组合 + Friend 属性」即可，不需新 PackedFlags 位。

---

## 4. 扩展成员符号呈现 —— 已检查，定案（关闭设计 §10.1 开放项）

### 定案结论
推荐**包装底层 `[ExtensionMarker]` 成员**（经归约符号），**不合成**全新符号。依据：
- 现有扩展方法架构全部是「包装」：`ReducedExtensionMethodSymbol` 包装原方法（`_curriedFromMethod`，携带 `ReducedFrom`）、`ReducedExtensionPropertySymbol` 包装原属性（`_originalDefinition`，`ReducedFrom`/`ReducedFromDefinition`）。
- 扩展属性直接复用 `ReducedExtensionPropertySymbol`（现构造断言 `IsShared && ParameterCount = 1`，恰好符合扩展属性剥接收者）。扩展运算符须新建运算符专用归约（见 #9）。
- 包装符号自动继承原成员的 Name/Type/ContainingSymbol/DeclaredAccessibility/Locations，bind/emit 全复用；合成符号则要新写一整条 bind/emit 路径，违反原则 2（并列不改写）。
- `ExtensionMethodGroup`（`BoundMethodGroup.vb:40`）是**延后收集**扩展方法的 lazy 包装，不是符号呈现层——扩展属性/运算符接入时扩它的收集形态（见 #5），结构本身不动。

### 证据锚点
- `VB\BoundTree\BoundMethodGroup.vb:30-36` —— `AdditionalExtensionMethods`（延后收集）；`:40-79` —— `ExtensionMethodGroup.LazyLookupAdditionalExtensionMethods`（按名+arity 经 `_lookupBinder.LookupExtensionMethods` 收集）。
- `VB\Symbols\ReducedExtensionMethodSymbol.vb:19-27` —— 包装 `_curriedFromMethod`；`:203` —— `New ReducedExtensionMethodSymbol(receiverType, possiblyExtensionMethod, fixedTypeParameters, proximity)`。
- `VB\Binding\Binder_XmlLiterals.vb:1524-1535` —— `ReducedExtensionPropertySymbol` 包装 `_originalDefinition`，断言 `IsShared` + `ParameterCount = 1`；`:1537-1547` —— `ReducedFrom`/`ReducedFromDefinition`。

### 对设计文档的调整
建议回填 §10.1「BoundMethodGroup.vb:40 合成 vs 包装留任务计划阶段」→ 定案为「包装」，避免任务计划阶段再纠结。

---

## 5. 收集桶类型 —— 已检查，定案

### 定案结论
- 现有扩展方法链桶型**全部是 `ArrayBuilder(Of MethodSymbol)`**（类型级、命名空间级、map 级、binder 收集），且入口判断 `MayBeReducibleExtensionMethod` 只认 `SymbolKind.Method`。
- **扩展属性（`PropertySymbol`）不能进 `ArrayBuilder(Of MethodSymbol)`**。最小扰动方案 = **并列桶**：
  - 新增 `ArrayBuilder(Of Symbol)`（或 `ArrayBuilder(Of PropertySymbol)`）收集扩展属性，走新 `AppendProbableExtensionMembers`/`GetExtensionMembers`（遍历 `<G>$` 分组类型，见 #8）；
  - 扩展运算符（`MethodKind.UserDefinedOperator` 仍是 `MethodSymbol`）**可并入方法桶**，但入口判断须从 `MayBeReducibleExtensionMethod` 扩为 `MayBeReducibleExtensionMethod OrElse IsExtensionMember`（扩展运算符带 `[ExtensionMarker]` 而非 `[Extension]`，见 #3）。
  - C# 参考对齐：C# `GetAllExtensionMembers(ArrayBuilder<Symbol> members, ...)`（`NamedTypeSymbol.cs:408`）正是 `ArrayBuilder<Symbol>` 混合桶——VB 新路径对齐它，既收属性也收运算符；现有方法 map 路径保持 `ArrayBuilder(Of MethodSymbol)` 不动（原则 2）。
- 四处 binder（`NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66`，基类 `Binder.vb:220`）在现有 `AppendProbableExtensionMethods` 后追加 `AppendProbableExtensionMembers` 即可，现有方法路径零扰动。

### 证据锚点
- `VB\Symbols\NamedTypeSymbol.vb:305-317` —— `AppendProbableExtensionMethods(name, methods As ArrayBuilder(Of MethodSymbol))`，只收 `SymbolKind.Method` + `MayBeReducibleExtensionMethod`，遍历 `Me.GetMembers(name)`（不遍历嵌套类型）。
- `VB\Symbols\NamedTypeSymbol.vb:338-351` —— `GetExtensionMethods(methods As ArrayBuilder(Of MethodSymbol), ...)`，走 `GetSimpleNonTypeMembers(Name)`。
- `VB\Symbols\NamespaceSymbol.vb:518-526` —— `AddMemberIfExtension(bucket As ArrayBuilder(Of MethodSymbol), member As Symbol)`，只收 `SymbolKind.Method`。
- `VB\Symbols\NamespaceSymbol.vb:471-475` —— 命名空间级 `GetExtensionMethods(ArrayBuilder(Of MethodSymbol), name)`。
- `VB\Symbols\NamespaceSymbol.vb:488-516` —— `BuildExtensionMethodsMap(Dictionary(Of String, ArrayBuilder(Of MethodSymbol)), ...)`。
- `CS\Symbols\NamedTypeSymbol.cs:408` —— `GetAllExtensionMembers(ArrayBuilder<Symbol> members, ...)`；`:439-474` —— `doGetExtensionMembers` 遍历 `GetTypeMembers(name: "")` 收集混合成员。
- 四处 binder 锚点：`VB\Binding\NamedTypeBinder.vb:99`、`NamespaceBinder.vb:85`、`ImportedTypesAndNamespacesMembersBinder.vb:130`、`TypesOfImportedNamespacesMembersBinder.vb:66`、基类 `Binder.vb:220`。

### 对设计文档的调整
设计 §3.2(a) 已预言「并列收集桶 + `ArrayBuilder(Of Symbol)`」——核实为正确方向。建议把「推荐并列数组」升级为明确落点：新路径统一 `ArrayBuilder(Of Symbol)`，运算符并入方法桶须扩入口判断（`IsExtensionMember`）。

---

## 6. 扩展属性 setter / 链式 / With —— 已检查（结构部分）+ Suspect（实证部分）

### 定案结论
- **setter 不单独打标**：C# 发射器把 `[ExtensionMarker]` 打在**属性符号**上（`SourcePropertySymbolBase.cs:1474`），不打在访问器方法上。`set_P(obj, v)` 是分组类型里普通 static 方法，随属性导入。
- **C# PE 导入对称**：`CreateProperties` 对扩展块遍历分组类型属性，`HasExtensionMarkerAttribute(propertyDef, markerName) && markerName == MetadataName` 过滤（`PENamedTypeSymbol.cs:2616`）；导入后 `PEPropertySymbol` 过滤属性自身属性列表中的 marker（`PEPropertySymbol.cs:833-847`）。
- **VB 消费 `obj.P = v` 无需独立 setter 路径**：`ReducedExtensionPropertySymbol.SetMethod` 已包装 `set_P`（`ReduceAccessorIfAny`），`ReducedExtensionAccessorSymbol.ParameterCount = 原方法 - 1`（剥接收者），参数经 `ReducedAccessorParameterSymbol.MakeParameters` 重构。属性赋值路径复用同一归约即可。
- **未实证部分（Suspect）**：`obj.P = v` 的 VB 属性赋值绑定路径（`BindAssignmentToProperty`/`SetMember`）与链式/`With` 语句的**运行时发射**细节未在本仓库实证枚举——需 L1/L2 测试（读内存编译）补证。结构证据充分支持复用，无阻塞。

### 证据锚点
- `CS\Symbols\Source\SourcePropertySymbolBase.cs:1472-1475` —— `IsExtensionBlockMember()` 时 `SynthesizeExtensionMarkerAttribute(this, ...)`（打在属性上）。
- `CS\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2603-2634` —— `CreateProperties`：`GetPropertyMethodsOrThrow` 取 getter/setter、`:2616` 按属性 marker 过滤、`:2624-2625` 分别取 `methods.Getter/Setter`。
- `CS\Symbols\Metadata\PE\PEPropertySymbol.cs:833-847` —— 导入属性时过滤自身 `[ExtensionMarker]`。
- `VB\Binding\Binder_XmlLiterals.vb:1679-1683` —— `SetMethod` = `ReduceAccessorIfAny(_originalDefinition.SetMethod)`；`:1709-1711` —— `ReduceAccessorIfAny` → `New ReducedExtensionAccessorSymbol(Me, methodOpt)`；`:1934-1936` —— `ParameterCount = _originalDefinition.ParameterCount - 1`；`:1940-1945` —— `Parameters` = `ReducedAccessorParameterSymbol.MakeParameters`。

### 对设计文档的调整
设计 §5.2(a)「setter 是否对称标记」→ 定案：**marker 在属性不在 setter**，属性赋值复用属性归约。建议在 §5 回填该实证结论，并把「链式/With 实证枚举」列为 L1/L2 测试用例而非实现期核实项。

---

## 7. 上游 Roslyn 基准是否含 VB SAIM —— Suspect（非阻塞）

### 定案结论
- 本 fork 的 VB 树（修剪自基准 commit `0e401fcf...`，2026-07-27，`release/stable`）**不含任何 VB SAIM 绑定/收集实现**（grep 无 `StaticAbstract` 命中，见 #3）。
- **无法在本仓库核实上游完整 Roslyn 在该 commit 是否有 VB SAIM**——fork 的 `Compilers\` 是修剪过的树，本地无上游镜像（`upstream-merge.md:15` 明示）。此项维持 **Suspect**。
- **非阻塞**：C# 参考路径完整（`Binder_Expressions.cs:10007-10030`、`PENamedTypeSymbol.cs:2295/2451-2462/2580-2630`），VB 侧运行时钩子已在位（`AssemblySymbol.vb:404-407`）。即使上游无 VB 实现，也可按 C# 路径镜像到 VB。合并时以 `upstream-merge.md` 第三/四节流程核对即可。

### 证据锚点
- `InternalDevDocs\upstream-merge.md:10-15` —— 基准 commit `0e401fcf66cbfd4aeb27a78408ab91cab3a6f207`、日期 2026-07-27、`release/stable`；「本 fork 的 `Compilers\` 是修剪过的 Roslyn 编译器树，非完整上游镜像」。
- `upstream-merge.md:84` —— 合并时跑七门 gate + Scripting 程序集（合并前置条件）。
- grep 结果（#3 复用）—— VB 树无 SAIM 处理。

### 对设计文档的调整
无。设计 §11 第 7 项标注 `Suspect`、非阻塞——已证实并维持。

---

## 8. 分组类型遍历 API —— 已检查，**发现设计文档需修正**

### 定案结论
**`GetTypeMembers(name:="")` 在 VB PE 层不会返回全部嵌套类型——必须改用 `GetTypeMembers()`（无参）或 `GetTypeMembersUnordered()`。**

证据链：
- `PENamedTypeSymbol.GetTypeMembers(name As String)`（:807-817）执行 `_lazyNestedTypes.TryGetValue(name, t)`——**按精确元数据名查字典**；
- 字典由 `GroupByName`（:1294-1300）按 `s.Name` 建键——不存在空键，`name:=""` 查空；
- 基类 `NamedTypeSymbol.GetTypeMembers(name As String)`（:631）文档注释 =「给定名字的类型」，无「空名=全部」约定；
- **（F9 验证者复核修正，2026-08-22）** 原「C# 侧 `GetTypeMembers(name: "")` 是全部」论据**不成立**：本 fork C# 树 `GetTypeMembers(ReadOnlyMemory<char>)` 亦为普通字典查找、无空名特例（`PENamedTypeSymbol.cs:2046-2058`、`SourceMemberContainerSymbol.cs:1400-1409`）。VB 结论独立成立：**VB `GetTypeMembers(name)` 按名查字典、无空名约定，必须用无参 `GetTypeMembers()`**。

正确 API：`GetTypeMembers()`（:780-784，全部嵌套类型，排序）或 `GetTypeMembersUnordered()`（:774-778，全部，无序）。收集扩展成员时用 `GetTypeMembers()` 遍历，再对每个嵌套类型判 `IsExtensionGroupingType`（仿 C# `doGetExtensionMembers` 对 `nestedType.IsExtension` + `ExtensionParameter` 的过滤），并滤掉 `<M>$` 标记类型（它们也是嵌套类型）。

### 证据锚点
- `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb:807-817` —— `GetTypeMembers(name)` 查字典，无空名约定。
- `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb:780-784` —— `GetTypeMembers()` 全部嵌套类型（`Flatten(DeclarationOrderSymbolComparer.Instance)`）。
- `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb:774-778` —— `GetTypeMembersUnordered()` 全部嵌套类型（`Flatten()`）。
- `VB\Symbols\Metadata\PE\PENamedTypeSymbol.vb:1294-1300` —— `GroupByName` 按 `s.Name` 键控字典。
- `VB\Symbols\NamedTypeSymbol.vb:623-639` —— 基类 `GetTypeMembers` 三个重载签名；`:625-631` 文档 =「给定名字的类型」。
- ~~`CS\Symbols\NamedTypeSymbol.cs:441` —— C# 侧 `GetTypeMembers(name: "")` 返回全部~~（F9 验证者修正：本 fork C# 无此语义，见上「定案结论」更正）。

### 对设计文档的调整
**必须回填设计 §3.2(a)**：`AppendProbableExtensionMembers` 中的 `Me.GetTypeMembers(name:="")` 改为 `Me.GetTypeMembers()`（或 `GetTypeMembersUnordered()`）。这是本核对发现的 2 处「设计形状错误」之一（另一处见 #9）。

---

## 9. 运算符归约复用 —— 已检查，**发现设计文档需修正**

### 定案结论
**扩展运算符不能复用 `ReducedExtensionMethodSymbol.Create`，必须新增运算符专用归约。** 两个硬性障碍：
1. **入口检查失败**：`Create` :47-49 要求 `MayBeReducibleExtensionMethod`（= `IsExtensionMethod AndAlso MethodKind <> ReducedExtension`，`MethodSymbol.vb:424-427`），而 `PEMethodSymbol.IsExtensionMethod` 读的是 `[Extension]` 属性（:688-698）——扩展运算符带 `[ExtensionMarker]` 不带 `[Extension]`，故为 False；`:185` 又强制 `IsExtensionMethod`。两项均拦下扩展运算符。
2. **归约后 MethodKind 错误**：即便放宽入口，`ReducedExtensionMethodSymbol.MethodKind` 覆盖为 `MethodKind.ReducedExtension`（:353-355），而运算符决议按 `MethodKind.UserDefinedOperator` 过滤（`CollectUserDefinedOperators` 只收 `MethodKind.UserDefinedOperator`，`Operators.vb:2917+`）——归约后会被决议层无视。

**推荐**：新建运算符专用归约（`ReducedExtensionOperatorSymbol` 或 `ReducedExtensionMethodSymbol` 加「保 `MethodKind.UserDefinedOperator`」的变体），**复用 `Create` :66-180 的推断/约束/接收者匹配模板**（`CollectReferencedTypeParameters` → `TypeArgumentInference.Infer` → `CheckConstraints` → `DoesReceiverMatchInstance`），仅把入口判据换成 `IsExtensionMember`，并把 `MethodKind` 保持为 `UserDefinedOperator`。接收者剥除 = 首个参数（运算符 `static op_X(a, b)` 的 `a` 即接收者）。

### 证据锚点
- `VB\Symbols\ReducedExtensionMethodSymbol.vb:47-50` —— 入口断言 `IsDefinition AndAlso MayBeReducibleExtensionMethod AndAlso MethodKind <> ReducedExtension`。
- `VB\Symbols\ReducedExtensionMethodSymbol.vb:182-187` —— `:185` `If Not possiblyExtensionMethod.IsExtensionMethod OrElse MethodKind = ReducedExtension Then Return Nothing`。
- `VB\Symbols\ReducedExtensionMethodSymbol.vb:353-355` —— `MethodKind` 覆盖返回 `MethodKind.ReducedExtension`。
- `VB\Symbols\ReducedExtensionMethodSymbol.vb:63-180` —— 可复用模板（接收者类型收集 :66、类型推断 :94-112、约束检查 :150-156、接收者匹配 :177）。
- `VB\Symbols\MethodSymbol.vb:424-427` —— `MayBeReducibleExtensionMethod = IsExtensionMethod AndAlso MethodKind <> ReducedExtension`。
- `VB\Symbols\Metadata\PE\PEMethodSymbol.vb:688-698` —— `IsExtensionMethod` 读 `HasExtensionAttribute`（`[Extension]`），扩展运算符无 `[Extension]`。
- `VB\Semantics\Operators.vb:2847-2903` —— `CollectUserDefinedOperators`（type1/type2 层级收集）；`:2917-2932` 逐成员收 `MethodKind.UserDefinedOperator`（扩展运算符追加挂点，见设计 §5b）。
- `VB\Binding\Binder_XmlLiterals.vb:1524-1535` —— 属性归约先例（包装 `PropertySymbol`），运算符归约仿此形态。

### 对设计文档的调整
**必须回填设计 §4.2(c)**：「运算符归约是否复用 `ReducedExtensionMethodSymbol`」从「实现期核对」→ 定案为「**不能复用，须新增运算符专用归约**」，并注明复用 :66-180 模板。设计 §3.2(a) 中「扩展运算符可并入方法桶」亦须同步：入桶入口判断扩 `IsExtensionMember`（见 #5）。

---

## 对 design-detailed.md 的改动建议汇总

| # | 位置 | 建议 | 性质 |
|---|---|---|---|
| 1 | §2.2(c) | 维持（已核实「实际发射名 = `ExtensionMarkerAttribute`」） | 无需改 |
| 2 | §8（改动点 8） | 「诊断码族实现期核对」→ 定案：未命中复用 BC32098；运行时不受支持新建 **`32134`**，注册 `Errors.vb`+`VBResources.resx`（+可选 `ErrorFacts.IsBuildOnlyDiagnostic`） | 回填定案 |
| 3 | §6.1 | 维持（已证实「VB 全树无 SAIM 处理」，识别用现有标志组合） | 无需改 |
| 4 | §10.1 | 「合成 vs 包装留任务计划」→ 定案「包装」（复用归约符号） | 回填定案 |
| 5 | §3.2(a) | 定案：新收集路径统一 `ArrayBuilder(Of Symbol)`（对齐 C# `GetAllExtensionMembers`）；运算符入方法桶须扩 `MayBeReducibleExtensionMethod OrElse IsExtensionMember` | 回填细化 |
| 6 | §5.2(a) | 定案：marker 在属性不在 setter；`obj.P = v` 复用属性归约；链式/With 列入测试而非实现期核实 | 回填定案 |
| 7 | §11 第 7 项 | 维持 `Suspect`（非阻塞） | 无需改 |
| 8 | §3.2(a) `GetTypeMembers(name:="")` | **修正为 `GetTypeMembers()`**（VB 无 C# 空名=全部语义） | 必须改 |
| 9 | §4.2(c) | **修正为「不能复用，须新增运算符专用归约」** | 必须改 |

**需要提前回填的 2 处硬性修正**：#8（`GetTypeMembers(name:="")` → `GetTypeMembers()`）、#9（运算符须专用归约）。其余 7 项为「核实确认」或「定案回填」，可随详细设计更新一并处理。
