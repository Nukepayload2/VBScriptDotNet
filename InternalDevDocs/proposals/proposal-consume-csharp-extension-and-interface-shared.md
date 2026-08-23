# 消费 C# 扩展成员与接口共享成员 / Consume C# Extension Members and Interface Shared Members

* [x] Proposed
* [ ] Prototype: [Not Started](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

让 VB 编译器（vbc 与 vbx 脚本/REPL 是同一份 fork Roslyn，`Compilers\VisualBasic\Portable\`）**消费** C# 定义的两类现代能力，补齐「C# 生态互通」缺口：

- **A. 扩展成员消费（extension everything）**：C# 14 扩展成员 = 扩展方法 + 扩展属性 + 扩展运算符（`LanguageVersion.cs` CSharp14 注释原文「Extension methods, properties and operators」）。扩展方法已被 VB 消费（C# 发射为 `[Extension]` 静态方法）；**扩展属性与扩展运算符未被消费**。
- **B. 接口共享成员消费（SAIM = static abstract interface members，静态抽象接口成员，C# 11 特性）**：C# 接口可声明「抽象静态成员」（无方法体，要求实现类型提供实现），并**经类型参数访问**——.NET 泛型数学（`INumber(Of T)` 等）即建于此。VB 现无法经类型参数访问（`T.Zero` 报 BC32098）；具体类型静态成员（`MyNum.Zero`）与消费 C# 泛型方法（`Mathy.Sum(Of MyNum)`）可用，但 VB 自己写「泛型数学式」算法不可。

**范围**：本提案严格只做**消费**（调用/使用 C# 定义的扩展成员与接口共享成员）。VB 侧**声明**（VB 源码写 `Shared` 接口成员、`<Extension>` 扩展属性/运算符）**暂不在本提案范围**——用户定案（2026-08-22）：消费优先，声明暂缓。

**策略**：消费路径从 VB 现有 **extension 策略**（`<Extension>` 方法回退解析：实例成员优先 → 扩展成员回退，仅早绑定参与）与 **shared 策略**（类型名访问共享成员、静态分发）**延申**，不引入新机制。未决问题按此原则裁决（见 Unresolved questions 顶部的原则裁决）。

## Motivation
[motivation]: #motivation

- **判入依据（`decisions.md` D4 两档直接判入）**：D4 ①「C# 已照顾到的非底层内存机制用例」→ P1；②「C# interop 用例（如 consume ref struct）」→ P1。本提案是**典型 C# interop 用例**：消费 C# 14 扩展成员与 C# 11 静态抽象接口成员，方向见 `csharplang-index.md` T8（extensions）与 `decisions.md` M7（ref struct interfaces / DIM / extensions 方向兼容）。
- **生态现实**：.NET 泛型数学（`INumber(Of T)` 等）建立在 SAIM 上（`csharplang\proposals\csharp-11.0\static-abstracts-in-interfaces.md`，champion #4436，C# 11 已发布）；C# 14 扩展属性/运算符已进入语言并被新库采用。VB 不能写 `T.Zero` 泛型算法、不能用 `.Property`/`a + b` 天然语法消费扩展成员，就无法参与生态。
- **现状断点（源码 + 实证核实）**：
  1. 扩展成员收集只认方法：`NamespaceSymbol.AddMemberIfExtension`（`Compilers\VisualBasic\Portable\Symbols\NamespaceSymbol.vb:518-526`）只收 `SymbolKind.Method`；`NamedTypeSymbol.GetExtensionMethods`（`:338-351`）只迭代普通成员。C# 14 扩展属性/运算符的真实形态在**嵌套分组类型**里，不走这条路径。
  2. SAIM 全缺：VB 编译器全树无 `StaticAbstract` 处理（grep `Compilers/VisualBasic` 零匹配）；解析器拒绝 `Shared` 修饰接口成员；类型参数限定成员被拒。
- **实证结果（fork vbi.exe + 自建 C# 14 测试库，`tmp/exp-consume/`）**：

  | C# 能力 | VB 天然语法消费 | 实证 |
  |---------|-----------------|------|
  | 经典扩展方法 `this int` | ✅ | `5.Twice()` → 10 |
  | C#14 扩展方法 | ✅ | `"hi".Shout()` → HI（发射 `[Extension]` 静态方法） |
  | C#14 扩展属性 | ❌ | `"hello".CharCount` → BC30456 |
  | C#14 扩展运算符 | ❌ | `a + b` → Strict On: BC30452；**Strict Off: 静默降级晚绑定 → 运行期 InvalidCastException** |
  | SAIM 具体类型静态成员 | ✅ | `MyNum.Zero` → 0 |
  | SAIM 经 C# 泛型方法 | ✅ | `Mathy.Sum(Of MyNum)` → 6 |
  | SAIM 经类型参数 `T.Zero` | ❌ | BC32098「类型参数不能限定成员」 |
  | VB 声明 `Shared` 接口成员 | ❌ | BC30270/BC30273（声明侧暂不在本提案范围，仅作现状上下文） |

- **现状后果**：用户拿到 C# 14 库，`.Property`/`a+b` 天然语法不可用，只能退而调用裸访问器 `NewExt.get_CharCount(s)` / `VecExt.op_Addition(a, b)`（实证可用但丑陋、与生态文档不符）；SAIM 接口无法在 VB 侧做泛型算法。Strict Off（vbi 默认）下扩展运算符**静默运行期失败**比编译错误更糟。

## Detailed design
[design]: #detailed-design

### Part A：扩展成员消费（extension everything）

**A1. 识别扩展成员元数据**（实证：反射 dump `ExternLib.dll` 元数据，见 `tmp/exp-consume/dump/`）：

C# 14 扩展方法（实例）发射为外层 `[Extension]` 静态类上的**顶层 `[Extension]` 静态方法**（`Shout(String)`）→ VB 已消费。扩展属性/运算符发射为：

| 元素 | 形态 | 关键特性 |
|------|------|---------|
| 外层静态类 | `NewExt` / `VecExt` | `[Extension]` |
| **扩展分组类型** | 嵌套 `<G>$<内容哈希>` | `[Extension]`；存放真实成员 |
| **扩展成员** | 分组类型内属性 `CharCount` / 运算符 `op_Addition` | `[ExtensionMarkerName]` |
| **扩展标记类型** | 嵌套 `<M>$<哈希>`，含 `<Extension>$(receiver)` 标记方法 | 编码接收者签名（含泛型约束全保真） |
| **顶层实现方法** | `get_CharCount(String)` / `op_Addition(MyVec,MyVec)` | **无** `[Extension]` |

关键点：**扩展属性/运算符不是方法形态的 `[Extension]`**，所以 VB 现有 `AddMemberIfExtension`（只收 Method）永远看不到它们。（规范属性名 `ExtensionMarkerNameAttribute`，`csharplang\proposals\csharp-14.0\extensions.md:610-620`；`[ExtensionMarkerName]` 为简称，实现导入以规范名为准。）

**A2. 消费路径（从 VB 现有扩展方法机制延申，源码落点已核实）**：

VB 现有机制完整调用链：`LookupForExtensionMethodsIfNeedTo`（`Binding\Binder_Lookup.vb:1152-1171`，**唯一汇聚点**）→ `LookupForExtensionMethods`（`:1181`，逐层 binder 收集）→ `CollectProbableExtensionMethodsInSingleBinder`（`NamedTypeBinder.vb:99` 等 4 处 binder）→ `NamedTypeSymbol.AppendProbableExtensionMethods` → `GetExtensionMethods`（`Symbols\NamedTypeSymbol.vb:338`）→ `AddMemberIfExtension`（`Symbols\NamespaceSymbol.vb:518`，**只收 `SymbolKind.Method`**）→ 归约 `ReduceExtensionMethod`（`Symbols\MethodSymbol.vb:817`）→ `ReducedExtensionMethodSymbol.Create`。

本提案按四层**延申**（每层都有现成挂点；C# 参考实现 = `Symbols\NamedTypeSymbol.cs:408-437` 并列两条收集路径 + `Symbols\Metadata\PE\PENamedTypeSymbol.cs` 扩展成员导入）：

1. **元数据导入层**：VB PE 符号层现只读 `HasExtensionAttribute`（`Symbols\Metadata\PE\PEMethodSymbol.vb:698` / `PENamedTypeSymbol.vb:931`），**不读 C# 14 扩展成员**。新增读 `<G>$` 分组类型 + `[ExtensionMarkerName]` 成员 + `<M>$` 标记类型（仿 C# `PENamedTypeSymbol.TryGetExtensionMarkerMethod`、`module.HasExtensionMarkerAttribute`）。`Module` 层已暴露 `HasExtensionAttribute`，加等价 `HasExtensionMarkerAttribute` + 分组类型遍历。
2. **收集层**：把 `GetExtensionMethods` / `AddMemberIfExtension` 从「只收方法」扩为「收方法 + 属性 + 运算符」——分组类型里 `[ExtensionMarkerName]` 成员即候选。`MightContainExtensionMethods` 快速路径对 `[Extension]` 类天然为真，无需新标志。
3. **归约层**：扩展属性**已有** `ReducedExtensionPropertySymbol`（`Binding\Binder_XmlLiterals.vb:1524`，剥接收者参数）；泛型扩展属性需补「接收者实参 → 泛型参数推断 + 约束检查」（仿 `ReducedExtensionMethodSymbol.Create`，`ReducedExtensionMethodSymbol.vb:35-204` 的 `TypeArgumentInference` 流程）。扩展运算符归约后即普通静态方法候选，泛型同理需推断。
4. **合并/挂点层**：扩展属性在汇聚点 `LookupForExtensionMethodsIfNeedTo` 把 `MergeInternalXmlHelperValueIfNecessary`（`Binder_Lookup.vb:1259`，现只收 `InternalXmlHelper.Value` 特例）**泛化**为收全部扩展属性；扩展运算符在 `CollectUserDefinedOperators`（`Semantics\Operators.vb:2847`）类型层级收集完后，追加在作用域（`Imports`）`[Extension]` 类的分组类型运算符候选。

沿用规则：
- **优先级**：实例成员优先于扩展成员（扩展成员是回退，与 C# 一致）。
- **现有扩展方法路径不动**：`AddMemberIfExtension` 收 `[Extension]` 方法继续走 `ReducedExtensionMethodSymbol`，新增路径并列而非改写。
- **`Imports` 语义一致**：扩展成员与扩展方法一样，需 `Imports` 其所在命名空间才参与候选（实证 test1/test2b 均含 `Imports ExternLib`）。

目标状态可运行示例（本提案落地后）：

```vb
#R "C:\...\ExternLib.dll"
Imports ExternLib

' A. 扩展属性 / 扩展运算符消费
Console.WriteLine("count=" & "hello".CharCount)          ' 5
Dim s As MyVec = New MyVec(1, 2) + New MyVec(10, 20)     ' (11,22)
```

### Part B：接口共享成员消费（SAIM）

> **范围**：只做消费（VB 经类型参数使用 C# 声明的接口共享成员）。VB 源码**声明** `Shared` 接口成员不在本提案范围（用户定案 2026-08-22：消费优先，声明暂缓）。
>
> **策略：从 VB 现有 shared 策略延申。** VB 已支持 `MyClass.SharedMember`（类型名访问共享成员、静态分发）。本提案把「接口上 `static abstract`/`static virtual` 成员」识别为共享成员后，允许**经约束类型参数** `T.Member` 绑定——shared 策略在「类型参数限定」上的延申；运行期按实际类型实参分发（`constrained.` + 虚调用，与 C# 一致）。

**B1. 识别 SAIM 元数据**（实证：`IHasZero`1.get_Zero / Add 原始标志 = `Static|Abstract|Virtual`，`newslot=False`）。VB `PEMethodSymbol` 需把「接口上 `static abstract` 的成员」标记为接口共享抽象成员，供消费侧识别。

**B2. 消费侧（本提案主目标）**：类型参数约束为接口时，允许 `T.Zero` / `T.Add(...)` 绑定到约束接口的共享成员；CodeGen 发射 `constrained.` 前缀 + 虚调用（与 C# 语义一致：运行期用实际类型实参的成员实现）。

**B3. 关键落点（源码核实）**：
- **绑定**：`Binder_Expressions.vb:2913-2914`——`left` 为类型表达式且 `type.TypeKind = TypeParameter` 时**无条件**报 `ERR_TypeParamQualifierDisallowed`（BC32098）。改：先查该类型参数约束接口（effective interface set）是否有名为 `rightName` 的共享成员，有则绑定（仿同函数 `:2921` 对普通类型的 `LookupMember` 路径）；无则维持 BC32098。
- **CodeGen**：`T.Member` → `constrained. !!T` + 虚调用。C# 参考：`Binder_Expressions.cs:8151`（`IDS_FeatureStaticAbstractMembersInInterfaces` 特性门控）与 `:10026`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces` 运行时支持检查）。**VB 已有运行时门控钩子**：`SupportsRuntimeCapability` / `RuntimeCapability.VirtualStaticsInInterfaces`（`AssemblySymbol.vb:335/348-349/404-408`，检查 `RuntimeFeature.VirtualStaticsInInterfaces`）——Part B 运行时不受支持检查**复用此钩子**，不必从零建（会议复核发现，正向证据）。`T` 作运算符操作数同理：`GetTypeToLookForOperatorsIn`（`Operators.vb:2942`）现只取类型参数的非接口约束，需补接口约束上的共享运算符。
- **错误码**：识别缺失 / 运行时不受支持时的诊断用既有 BC 族或仿 C# 专用码（错误码号待任务计划阶段定）。

目标状态可运行示例（本提案落地后）：

```vb
#R "C:\...\ExternLib.dll"
Imports ExternLib

Function VBSum(Of T As IHasZero(Of T))(items() As T) As T
    Dim result As T = T.Zero                             ' 接口共享成员经类型参数
    For Each item In items
        result = T.Add(result, item)
    Next
    Return result
End Function

Console.WriteLine("vbsum=" & VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value)   ' 30
```

（约束语法 `Of T As IHasZero(Of T)` 当前已被 fork 接受，仅 `T.Zero` 被拒——见 Motivation 实证表。）

## Drawbacks
[drawbacks]: #drawbacks

- **扩展成员收集有性能成本**：遍历 `<G>$` 分组类型比现方法收集贵；需 `MightContainExtensions` 类快速路径规避（C# 已这么做）。
- **扩展属性交互面**：setter（`obj.P = v`）、链式调用、`With` 语句与扩展属性的交互需额外规则。
- **SAIM 打开语言面**：VB 作者可写约束复杂泛型算法，增加语言复杂度与测试面。
- **Object 接收者边界**：按现有 extension 策略，扩展成员只在**早绑定**参与、`Object` 接收者维持晚绑定——保留 Strict Off 晚绑定通道，但 `Object` 接收者上扩展成员不可见（与扩展方法现状一致，属既定取舍而非缺陷）。

## Alternatives
[alternatives]: #alternatives

- **只做扩展方法（维持现状）**：扩展属性/运算符仍不可用，生态库 API 只能裸方法调用，提案无价值。
- **消费启发式识别裸 `get_`/`op_` 方法**（不读 `<G>$`/`[ExtensionMarkerName]`）：实现简单，但无法区分「扩展成员实现方法」与「普通静态方法同名」，误判风险高，放弃。
- **SAIM 用工厂/委托绕开**（用户自写泛型参数包装）：重复造轮子，且无法恢复 `T.Zero` 的声明式语法。
- **（已定案排除）声明侧**：VB 源码声明 `Shared` 接口成员 / `<Extension>` 扩展属性**暂不做**（用户定案 2026-08-22，消费优先）。

## Unresolved questions
[unresolved]: #unresolved-questions

> **原则裁决（2026-08-22，用户）**：目标是**消费 C# 扩展**，消费策略从 VB 现有 extension/shared 策略**延申**。以下曾列的问题按此原则消解，不再作未决：
>
> - **扩展属性 setter**：随 C# 发射而消费（`obj.P = v` → `set_P(obj, v)`，与 getter 对称）。
> - **Option Strict Off / Object 接收者**：沿用现有 extension 策略——扩展成员只在**早绑定**查找参与；`Object` 接收者维持晚绑定（与扩展方法现状一致；实证里 Strict Off 静默晚绑定属「未找到扩展运算符」的回退，接入扩展运算符决议后早绑定命中即消除）。
> - **`InterfaceName.Member` 直呼共享成员**：对齐 C#——`static abstract` 只能经类型参数 `T.M`；`static virtual`（带默认实现）可经接口名呼。
> - **`static virtual`（DIM 静态版）**：一并消费（C# 发射即可消费）。
> - **同名优先级**：沿用现有规则（实例成员优先 → 扩展成员回退；组内扩展成员须唯一，`Binder_Invocation.vb:610-614`）。

仍待定（实现问题，非设计问题）：

1. 上游 Roslyn VB 是否已有 SAIM 参考实现可低成本合并（fork 是修剪/本地化 Roslyn，需核对上游基准 commit 是否含 VB SAIM——未核实，`Suspect`）。
2. 扩展成员在 VB 符号模型里的呈现方式：合成「扩展属性/运算符」符号接入现有 `ExtensionMethodGroup`/回退决议，还是包装底层 `[ExtensionMarkerName]` 成员——留任务计划阶段细化。

> **已定案**：声明侧（VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性）**不在本提案范围**（用户 2026-08-22，消费优先）。

## 证据来源与证据等级

- 实证（已运行）：fork `vbi.exe` 消费自建 C# 14 测试库 `tmp/exp-consume/` 的逐项结果表（见 Motivation）；元数据反射 dump（`tmp/exp-consume/dump/`）确认扩展分组/标记类型与 SAIM 标志。
- 源码（已检查）：
  - Part A 汇聚点：`Binder_Lookup.vb:1152-1171`（`LookupForExtensionMethodsIfNeedTo`，唯一扩展成员合并点）、`:1181`（收集主循环）、`:1259`（`MergeInternalXmlHelperValueIfNecessary` 特例）、`:1286`（`ReducedExtensionPropertySymbol` 已存在）；`NamespaceSymbol.vb:518-526`（`AddMemberIfExtension` 只收 Method）、`NamedTypeSymbol.vb:338-351`（`GetExtensionMethods`）；`ReducedExtensionMethodSymbol.vb:35-204`（归约含类型推断模板）；`Binder_XmlLiterals.vb:1524`（`ReducedExtensionPropertySymbol` 类定义）；VB PE 层只读 `HasExtensionAttribute`（`PEMethodSymbol.vb:698`/`PENamedTypeSymbol.vb:931`），无 `[ExtensionMarkerName]` 读取。
  - Part B 落点：`Binder_Expressions.vb:2913-2914`（BC32098 无条件抛出）、`:2921`（普通类型 `LookupMember` 参照）；`Operators.vb:2847`（`CollectUserDefinedOperators`）、`:2942`（类型参数操作数只取非接口约束）；C# 参考 `Binder_Expressions.cs:8151/10026`、`PENamedTypeSymbol.cs:2295/2585/2616`（扩展成员导入）；VB 无 `StaticAbstract`（全树 grep）。
  - C# 参考实现 `NamedTypeSymbol.cs:408-437`（并列两条收集路径：`doGetExtensionMembers` + `DoGetExtensionMethods`）。
- C# 事实（已提供，镜像）：`csharplang\proposals\csharp-14.0\extensions.md`（C# 14 扩展成员）、`csharplang\proposals\csharp-11.0\static-abstracts-in-interfaces.md`（SAIM）。
- ModVB 参考（已提供）：`modvb\proposals\proposal-extension-properties.md`（VB 侧扩展属性，其 Unresolved question 明示「C# 未暴露扩展属性时如何消费/生成」——本提案消费侧补上这一问）。
- 预测性判断（待定）：上游 Roslyn VB 是否含 SAIM 参考实现（Unresolved #5）。
