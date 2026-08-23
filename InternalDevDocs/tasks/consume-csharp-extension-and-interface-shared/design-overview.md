# 概要设计：消费 C# 扩展成员与接口共享成员（consume-csharp-extension-and-interface-shared）

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（Active/Proposed）→ `../../meetings/meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION 六条）→ `../../meetings/evaluation-consume-csharp-extension-and-interface-shared.md`（五维 + C# 生态）→ `design-detailed.md`（F2）。
> 本设计吸收 meeting RESOLUTION 六条与 proposal Detailed design 四层 + Part B，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准，引用以 `文件:行号` 给出；编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`），C# 前缀 `Compilers\CSharp\Portable\`（简写 `CS\`），Core 前缀 `Compilers\Core\Portable\`（简写 `Core\`）。

## 1. 背景与目标

**目标（一句话）**：让 VB 编译器（vbc 与 vbx 脚本/REPL 是同一份 fork Roslyn，`VB\`）**消费** C# 定义的两类现代能力，补齐「C# 生态互通」缺口：

- **Part A 扩展成员消费**：C# 14 扩展成员 = 扩展方法 + 扩展属性 + 扩展运算符。扩展方法已被 VB 消费（C# 发射为 `[Extension]` 静态方法）；**扩展属性与扩展运算符未被消费**。
- **Part B 接口共享成员消费**（SAIM = static abstract interface members，C# 11）：允许**经类型参数**访问约束接口的共享成员（`T.Zero` / `T.Add`）——.NET 泛型数学（`INumber(Of T)` 等）建于此。VB 现无法经类型参数访问（`T.Zero` 报 BC32098）。

**范围定案（用户 2026-08-22）**：严格只做**消费**。VB 侧**声明**（VB 源码写 `Shared` 接口成员、`<Extension>` 扩展属性/运算符）**不在本任务范围**，作后续提案。

**现状断点（源码 + 实证核实）**：

| 断点 | 源码证据 | 实证 |
|------|---------|------|
| Part A：扩展成员收集只收方法 | `NamespaceSymbol.AddMemberIfExtension`（`VB\Symbols\NamespaceSymbol.vb:518-526`）只收 `SymbolKind.Method`；`NamedTypeSymbol.GetExtensionMethods`（`VB\Symbols\NamedTypeSymbol.vb:338-351`）只迭代普通成员 | `"hello".CharCount` → BC30456 |
| Part A：PE 不读 C# 14 扩展成员元数据 | VB PE 层只读 `HasExtensionAttribute`（`VB\Symbols\Metadata\PE\PEMethodSymbol.vb:698` / `PENamedTypeSymbol.vb:931`），无 `<G>$`/`<M>$`/`[ExtensionMarker]` 读取 | 扩展属性/运算符不可消费 |
| Part A：扩展运算符在 Strict Off 下静默晚绑定 | 扩展运算符决议未接入 → 回退晚绑定 | `a+b` → Strict On BC30452 / Strict Off 运行期 InvalidCastException |
| Part B：类型参数限定成员被无条件拒绝 | `Binder_Expressions.vb:2913-2914`——`TypeKind = TypeParameter` 即报 `ERR_TypeParamQualifierDisallowed`（BC32098，`Errors.vb:1165`） | `T.Zero` → BC32098 |

**现状后果**：用户拿到 C# 14 库，`.Property`/`a+b` 天然语法不可用，只能裸调 `NewExt.get_CharCount(s)` / `VecExt.op_Addition(a, b)`；SAIM 接口无法在 VB 侧写泛型数学算法。Strict Off（vbi 默认）下扩展运算符**静默运行期失败**比编译错误更糟。

**策略**：消费路径从 VB 现有 **extension 策略**（`<Extension>` 方法回退解析：实例成员优先 → 扩展成员回退，仅早绑定参与）与 **shared 策略**（类型名访问共享成员、静态分发）**延申**，不引入新机制。

## 2. 总体架构落点

### 2.1 Part A：扩展成员消费（四层延申）

> 核心观察：VB **已有**完整的扩展方法消费链（汇聚点 → 逐层 binder 收集 → 归约），并且 `ReducedExtensionPropertySymbol` **已存在**（`VB\Binding\Binder_XmlLiterals.vb:1524`，XML 轴 `Value` 早已用「剥接收者参数的属性符号」实现）。扩展属性/运算符的归约机制不是从零造，是现成挂点。C# 参考实现 = `CS\Symbols\NamedTypeSymbol.cs:408-437` **两条并列收集路径**（`doGetExtensionMembers` + `DoGetExtensionMethods`）。

**① 元数据导入层**：VB PE 符号层现只读 `HasExtensionAttribute`（`PEMethodSymbol.vb:698` / `PENamedTypeSymbol.vb:931`），**不读 C# 14 扩展成员**。C# 14 扩展属性/运算符发射为外层 `[Extension]` 静态类 + 嵌套 `<G>$<哈希>` 分组类型（标 `[Extension]`，内放 `[ExtensionMarkerName]` 成员）+ `<M>$<哈希>` 标记类型（含 `<Extension>$(receiver)` 标记方法）+ 顶层无 `[Extension]` 实现方法。新增读 `<G>$` 分组类型 + `[ExtensionMarkerName]` 成员 + `<M>$` 标记类型，仿 C# `PENamedTypeSymbol.cs:2295`（`TryGetExtensionMarkerMethod`）、`:2585/:2616`（`module.HasExtensionMarkerAttribute`）。**关键发现**：`Core\Portable\MetadataReader\PEModule.cs:1061` 已暴露 `HasExtensionMarkerAttribute(EntityHandle, out String)`（共享 Core API），且 WellKnown 表已有 `System_Runtime_CompilerServices_ExtensionMarkerAttribute`（`Core\Portable\WellKnownTypes.cs:343/:717`，`.ctor(string)`）——**底层读取基建已在位**（实现期核对属性名，见 §5 核对项）。

**② 收集层**：把 `GetExtensionMethods`（`NamedTypeSymbol.vb:338`）从「只收方法」扩为「收方法 + 属性 + 运算符」，新增**并列收集路径** `GetExtensionMembers`（仿 C# `doGetExtensionMembers`）遍历分组类型；`AddMemberIfExtension`（`NamespaceSymbol.vb:518`）扩为接受属性/运算符。四处 binder 的 `CollectProbableExtensionMethodsInSingleBinder`（`NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66`）同步追加。`MightContainExtensionMethods`（`PENamedTypeSymbol.vb:919`，顶层非泛型 `[Extension]` 类为真）快速路径**天然复用**，无需新标志。

**③ 归约层**：扩展属性**复用** `ReducedExtensionPropertySymbol`（`Binder_XmlLiterals.vb:1524`）；泛型扩展属性/运算符需补「接收者实参 → 泛型参数推断 + 约束检查」，仿 `ReducedExtensionMethodSymbol.Create`（`ReducedExtensionMethodSymbol.vb:35-204`，`TypeArgumentInference` 流程 + `CheckConstraints`）。扩展运算符归约后即普通静态方法候选，泛型同理需推断。

**④ 合并/挂点层**：扩展属性在汇聚点 `LookupForExtensionMethodsIfNeedTo`（`Binder_Lookup.vb:1152-1171`，**唯一扩展成员合并点**）把 `MergeInternalXmlHelperValueIfNecessary`（`Binder_Lookup.vb:1259`，现只收 `InternalXmlHelper.Value` 特例）**泛化**为收全部扩展属性；扩展运算符在 `CollectUserDefinedOperators`（`VB\Semantics\Operators.vb:2847`）类型层级收集完后，追加在作用域（`Imports`）`[Extension]` 类的分组类型运算符候选。

### 2.2 Part B：接口共享成员消费（SAIM 约束接口绑定）

> 核心观察：VB **已有**共享成员访问与**运行时门控钩子**——`SupportsRuntimeCapability(RuntimeCapability.VirtualStaticsInInterfaces)`（`VB\Symbols\AssemblySymbol.vb:335/:348-349`）→ `RuntimeSupportsVirtualStaticsInInterfaces`（`:404-408`，检查 `RuntimeFeature.VirtualStaticsInInterfaces`）。Part B 运行时不受支持检查**复用此钩子**，不从零建（meeting 发现一，正向证据）。

- **绑定**：`Binder_Expressions.vb:2913-2914` 从「类型参数无条件拒绝」改为「先查该类型参数约束接口（effective interface set：接口约束 + 其 `AllInterfaces`）是否有名为 `rightName` 的共享成员，有则绑定（仿 `:2921` 对普通类型的 `LookupMember` 路径），无则维持 BC32098」。
- **CodeGen**：`T.Member` → `constrained. !!T` + 虚调用（与 C# 一致，`static-abstracts-in-interfaces.md:10`）。VB CodeGen **已有** `ConstrainedCallVirt` 支持（`VB\CodeGen\EmitExpression.vb:984/:1054/:1111-1112`）——需把「类型参数上静态抽象接口成员调用」路由到该路径。
- **运算符**：`GetTypeToLookForOperatorsIn`（`Operators.vb:2942`）现只取类型参数的非接口约束（`GetNonInterfaceConstraint`，`ConstraintsHelper.vb:771`），需补接口约束上的共享运算符（对齐 `static-abstracts-in-interfaces.md:229` 的 effective interface set 规则）。
- **运行时门控**：类型参数上访问静态抽象接口成员时，若 `Not SupportsRuntimeCapability(RuntimeCapability.VirtualStaticsInInterfaces)` 且成员来自引用程序集 → 报运行时不受支持错误（诊断码族实现期定案，见 §5）。C# 参考 `CS\Binder\Binder_Expressions.cs:10026-10028`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`）。

## 3. 行为对照表

> 「提案后」= 本任务落地后（仅消费侧；声明侧不在范围）。`*` = 实现期核对/依赖项。

| 场景 | 机制 | 现状行为 | 提案后行为 |
|------|------|---------|-----------|
| 扩展属性 `.P`（早绑定，接收者类型已知） | 收集层收属性 + `ReducedExtensionPropertySymbol` 归约 + 汇聚点泛化合并 | `"hello".CharCount` → BC30456 | 绑定到归约扩展属性 → `5` |
| 扩展运算符 `a+b`（早绑定） | `CollectUserDefinedOperators` 追加扩展运算符候选 + 归约 | Strict On → BC30452；**Strict Off → 静默晚绑定运行期 InvalidCastException** | 早绑定命中归约扩展运算符（Strict On/Off 均编译期解析，消除静默运行期失败） |
| `T.Zero` / `T.Add(...)`（`T As IHasZero(Of T)`） | `Binder_Expressions.vb:2913` 改判 + 约束接口查找 + `constrained.` CodeGen | BC32098「类型参数不能限定成员」 | 绑定到约束接口共享成员，`constrained.` 虚调用 → 可写 VB 版泛型数学算法 |
| Strict On/Off 分叉 | 扩展成员仅在**早绑定**查找参与；Strict Off 晚绑定通道保留 | Strict Off 下扩展属性/运算符找不到 → 晚绑定/报错 | 早绑定命中消除；`Object` 接收者维持晚绑定（与扩展方法现状一致） |
| `Object` 接收者 | `ShouldLookupExtensionMethods`（`Binder_Lookup.vb:1174-1179`）`Not container.IsObjectType()` 门 | `Object` 接收者上扩展方法不可见 | **不变**：`Object` 接收者维持晚绑定（Strict Off 通道保留），扩展成员仅早绑定参与 |
| `Imports` 作用域 | 四处 binder 收集（`ImportedTypesAndNamespacesMembersBinder.vb:130` 等） | 扩展方法需 `Imports` 所在命名空间才参与 | **不变**：扩展属性/运算符同样需 `Imports` 其所在命名空间（`Imports` 语义一致） |
| `static virtual` 经接口名（DIM 静态版） | 类型名共享成员路径（`Binder_Expressions.vb:2921`） | 具体类型静态成员 `MyNum.Zero` 已可用 | 对齐 C#：`static abstract` 只能经类型参数 `T.M`；`static virtual`（带默认实现）可经接口名呼 * |
| 同名优先级 | 实例成员优先 → 扩展成员回退（`Binder_Lookup.vb:1162-1166` gate + `MergeOverloadedOrPrioritized`）；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`） | 扩展方法已遵循 | **不变**：扩展属性/运算符沿用同一优先级（实例成员优先 → 扩展成员回退） |
| 运行时不受支持门控 | 复用 `SupportsRuntimeCapability(VirtualStaticsInInterfaces)`（`AssemblySymbol.vb:335/:348-349`） | 无检查（`T.Zero` 被拒，走不到） | 类型参数上访问 SAIM 时检查，不受支持报编译错误（诊断码族实现期定案）* |
| VB 声明 `Shared` 接口成员 / `<Extension>` 扩展属性 | 解析/声明侧 | BC30270/BC30273 | **不在范围**（声明侧作后续提案） |

## 4. 延申原则与 C# 参考跟随

1. **只消费、零新语法**：不新增语法表面积，只把现有 extension/shared 策略的候选面从「方法」扩到「属性/运算符」、从「普通类型限定」延申到「类型参数限定」。既有合法代码语义不变（P1 硬约束）。
2. **现有扩展方法路径不动**：`AddMemberIfExtension` 收 `[Extension]` 方法继续走 `ReducedExtensionMethodSymbol`，新增属性/运算符路径**并列而非改写**（对齐 C# `NamedTypeSymbol.cs:408-437` 两条并列收集路径）。
3. **复用现成挂点**：`ReducedExtensionPropertySymbol`（`Binder_XmlLiterals.vb:1524`）、汇聚点 `LookupForExtensionMethodsIfNeedTo`（`Binder_Lookup.vb:1152`）、运行时钩子 `VirtualStaticsInInterfaces`（`AssemblySymbol.vb:348-349`）全部复用，**实现成本被现有机制摊薄**。
4. **术语对齐 C# 规范**：元数据导入以 C# 规范 `ExtensionMarkerNameAttribute`（`csharplang\proposals\csharp-14.0\extensions.md:610-620`）为语义基准，`[ExtensionMarker]` 仅作内部简写。**实现期核对**：本树 WellKnown 表实际条目名为 `System_Runtime_CompilerServices_ExtensionMarkerAttribute`（`WellKnownTypes.cs:343`），属性名差异须按目标 C# 库实际发射对齐（§5 核对项 1）。
5. **C# 参考实现跟随**：收集层仿 `NamedTypeSymbol.cs:408-437`（并列两条路径）；PE 导入仿 `PENamedTypeSymbol.cs:2295/2451-2462/2585/2616`；SAIM 绑定/运行时检查仿 `Binder_Expressions.cs:8144-8159/:10007-10030`。
6. **`.vbx` 与 Regular 语义一致**：vbx（脚本/REPL）与 .vb 共用同一份 fork 编译器，规则语义一致不分裂（同 `SourceCodeKind.Script`）。

## 5. 代价与边界

### 代价
- **扩展成员收集有性能成本**：遍历 `<G>$` 分组类型比现方法收集贵；`MightContainExtensionMethods`（`PENamedTypeSymbol.vb:919`）快速路径规避（C# 已这么做，`NamedTypeSymbol.cs:416`）。对 `[Extension]` 类天然为真，无需新标志。
- **扩展属性交互面**：setter（`obj.P = v` → `set_P(obj, v)`，与 getter 对称）、链式调用、`With` 语句的交互需额外规则——随 C# 发射而消费，细节留实现阶段实证枚举。
- **SAIM 打开语言面**：VB 作者可写约束复杂泛型算法，增加语言复杂度与测试面。

### 边界
- **`Object` 接收者维持晚绑定**：`ShouldLookupExtensionMethods`（`Binder_Lookup.vb:1176`）`Not container.IsObjectType()`——`Object` 接收者上扩展成员不可见，Strict Off 晚绑定通道保留（与扩展方法现状一致，属既定取舍而非缺陷）。
- **扩展成员仅早绑定参与**：Strict Off 下只要接收者静态类型已知，扩展成员即早绑定命中；`Object` 静态类型才落晚绑定。
- **同名优先级**：实例成员优先 → 扩展成员回退；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`），与 C# 一致无新歧义面。
- **`InterfaceName.Member` 直呼 vs 经类型参数**：对齐 C#——`static abstract` 只能经类型参数 `T.M`；`static virtual`（带默认实现）可经接口名呼（原则裁决已消解）。
- **范围只消费**：声明侧（VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性）不在范围，作后续提案（与 ModVB `proposal-extension-properties.md` 衔接）。
- **上游 SAIM 参考实现**：未核实（`Suspect`，`upstream-merge.md` 基准 commit 是否含 VB SAIM）；运行时钩子在位、C# 参考路径完整，从零实现成本可控，此项不阻塞。
- **符号呈现方式**：合成「扩展属性/运算符」符号接入现有 `ExtensionMethodGroup`/回退决议（`VB\BoundTree\BoundMethodGroup.vb:40`），还是包装底层 `[ExtensionMarkerName]` 成员——留任务计划阶段细化（实现问题非设计问题）。
- **行为变化面**：纯消费侧、零新语法、无 breaking change；仅把「编译错误/静默运行期失败」变为「正确绑定」，属修错不算回归。
