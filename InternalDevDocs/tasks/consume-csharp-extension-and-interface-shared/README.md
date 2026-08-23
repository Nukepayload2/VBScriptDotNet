# 任务：消费 C# 扩展成员与接口共享成员（consume-csharp-extension-and-interface-shared）

本文件夹是 `proposal-consume-csharp-extension-and-interface-shared` 的设计任务存储（Vortex 代办列表 + 设计产物）。本任务分两段能力：**Part A** 消费 C# 14 扩展成员（扩展属性/运算符；扩展方法已可用）、**Part B** 消费 C# 11 接口共享成员（SAIM，`T.Zero`/`T.Add` 经类型参数）。**范围定案**：只做消费，VB 侧声明（`Shared` 接口成员 / `<Extension>` 扩展属性）不在本任务范围（用户 2026-08-22）。

- **依据链**：`../../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（Active，D4 P1 判入）→ `../../meetings/meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION 六条）→ `../../meetings/evaluation-consume-csharp-extension-and-interface-shared.md`（五维评分 + C# 生态）→ `../../compilers-index.md`（编译器树索引）。
- **交付物**：概要设计（`design-overview.md`）、详细设计（`design-detailed.md`）、测试计划（`test-plan.md`）。三份均要求无副作用测试矩阵。
- **调度方式**：Vortex 涡流触媒（实施者 agent 产出 → 验证者 agent 核对 → 打回修复 → 通过关闭），main 只调度；实施者与验证者串行交替。
- **流水账**：`<项目根>/tmp/vortex-logs/`。

## 代办列表（Vortex 功能拆分）

> 本任务仅覆盖**设计阶段**（F1-F4）；实现阶段（F9+）待设计通过后按 design-detailed.md 改动清单逐条再拆（实施者 + 验证者串行交替），届时回填本表。

| # | 功能 | 验收条件（pass 标准） | 状态 |
|---|------|---------------------|------|
| F1 | 概要设计 | 见下「F1 验收条件」 | **done**（`design-overview.md`，2026-08-22 验证者通过） |
| F2 | 详细设计 | 见下「F2 验收条件」 | **done**（`design-detailed.md`，2026-08-22 验证者通过） |
| F3 | 测试计划 | 见下「F3 验收条件」 | **done**（`test-plan.md`，2026-08-22 验证者通过；§7 两处行号偏移已修 :1973/:2365） |
| F4 | 一致性审计 + 修复 + 复验 | F1/F2/F3 三份交付物与 proposal/meeting/evaluation 交叉一致（四层延申、SAIM 约束接口绑定、运行时钩子复用、范围只消费、术语 `ExtensionMarkerNameAttribute`、无越权承诺）；源码事实行号全部真实 | **done**（2026-08-22 验证者：8 项全过、无打回） |
| F5 | 验证 README 源码事实 | README「共享源码事实」每条 `文件:行号` 经验证者 Read/Grep 复核；与源码不冲突 | **done**（2026-08-22 验证者：逐条 ✅） |
| F9 | 实现前核对（§11 九项源码定案） | 九项全部 Read/Grep 定案、`verification-checklist.md` 产出、2 处硬性修正回填设计（`GetTypeMembers()`/运算符专用归约） | **done**（2026-08-22 实施者+验证者 PASS，BC32134 空闲确认） |
| F10 | Part B 最小原型：`T.Zero` 绑定改判 + `constrained.` CodeGen + 运行时门控/诊断 | 7 文件改动构建 0 错误；`.vbx` `T.Zero`/`T.Add` 泛型算法 `vbsum=30`；无约束 BC32098；BC32134 实证；IL `constrained.`+`call`（实证纠正 callvirt）；13 既有失败全为 isVirtual:False 预期翻转 | **done**（2026-08-22 验证者 PASS；setter 边界留 F11） |
| F11 | Part B 测试（L1 语义 + L4 门控 + 既有 BC32098 用例翻转） | SymbolTest 70/70（13 翻转、定义侧未动）、SemanticTest 16/16、任务 C 三处 BC32098 维持；边界 5 项已证实（S22 static virtual 经接口名与 C# CS8926 对齐、事件/AddressOf/setter 未翻转） | **done**（2026-08-22 验证者 PASS） |
| F12 | Part A 元数据导入（`<G>$`/`<M>$`/`ExtensionMarker`） | 分组/标记类型识别 + `IsExtensionMember`；读 `HasExtensionMarkerAttribute` | **done**（2026-08-22 实施者：5 文件改动构建 0 错误 + 冒烟实证；`IsExtensionGroupingType`=`<G>$`/`IsExtensionMarkerType`=`<M>$` 澄清回填 design §2.2(a)） |
| F13 | Part A 收集层（扩收属性/运算符） | `GetExtensionMembers`/`AppendProbableExtensionMembers` + 四处 binder；`ArrayBuilder(Of Symbol)` 并列桶 | 待 F12 |
| F14 | Part A 归约层（复用 `ReducedExtensionPropertySymbol` + 泛型推断 + 运算符专用归约） | 扩展属性/运算符归约可用 | 待 F13 |
| F15 | Part A 挂点层（泛化汇聚点 + 扩展运算符决议） | `MergeExtensionPropertiesIfNecessary` + `CollectUserDefinedOperators` 追加扩展运算符候选 | 待 F14 |
| F16 | Part A 测试（L1/L2/L3） | `ExtensionMemberConsumptionTests.vb` S1-S15（16/16）、`ExtensionMemberConsumptionSymbolTests.vb` D1-D6（6/6）、R 系列 5/5（#R 机制可行）、XmlLiteral+ExtensionMethods 回归 18/18；综合 Semantic 49/49、Symbol 76/76；4 项差异回填 test-plan | **done**（2026-08-22 验证者 PASS） |
| F17 | 全量回归 + 七门 gate | 等价命令 7 门**全绿**（Phase2 143/143、Syntax 4070/4067/3、IOperation 1574/1566/8、Emit 4330/4227/103、CommandLine 475/468/7、Symbol **3398/3374/24/0**、Semantic **5745/5641/104/0**）；Scripting 程序集 `-automated` **165/165**；完整 gate 脚本 verbatim exit 0 | **done**（2026-08-23 F20 收尾后全绿） |
| F20 | F17 收尾修复：BC32134 登记 + 运算符断言翻转 | BC32134 入**非 build-only** 列表（`ErrorFacts.vb:923`，对齐 C# CS8925 非 build-only，`IsBuildOnlyDiagnostic`=False）；运算符 Consume 断言翻转（isVirtual:True/接口名段保留）；gate 基线更新（Symbol 3398/Semantic 5745）；1 次打回（误入 build-only）→ 返工 → 复验闭环 | **done**（2026-08-23 验证者 PASS） |
| F18 | 改动点 7：类型参数接口约束共享运算符 + S26 测试 | `GetTypeToLookForOperatorsIn` 零改动（兄弟收集 `CollectInterfaceConstraintSharedOperators`）；SAIM 运算符直接入 opSet 不归约；绑定侧挂 `BoundTypeExpression` 接收者命中 F10 发射（零改动）；S26 五例 + 运行期 `Add(10,20).Value=30`，IL 与 C# 逐字节同形 | **done**（2026-08-22 验证者 PASS） |
| F19 | Part B 补全：setter（`T.P=v`）+ `AddressOf T.M` delegate | setter `Binder_Statements.vb:1973-1978`、AddressOf `Binder_Delegates.vb:323-328/:994-1013` + `EmitExpression.vb:479-492`（`constrained.`+`ldftn`）；运行期 `T.P=42`/委托 `30` 实证；Semantic 18/18、Symbol 70/70 | **done**（2026-08-22 验证者 PASS） |
| F21 | 收口三项遗留：SAIM 入表达式树诊断（BC37340）+ IntelliSense 访问器排除 + 复合赋值运行期值 | ①新增 `ERR_ExpressionTreeContainsAbstractStaticMemberAccess=37340`（Errors.vb/VBResources.resx/ErrorFacts.vb 非 build-only + `DiagnosticsPass_ExpressionLambdas.vb` 三挂点 `IsInExpressionLambda` 门控）；11 处既有 expr-tree 断言翻转 + S29（普通 AddressOf/赋值仍无诊断）；②`IsNameableExtensionMember` 显式排除访问器 + D7；③`T.P+=1` 运行期值 6 + 修复 `UseTwiceRewriter.vb:313` 类型参数接收者丢失（Bad IL） | **done**（2026-08-23 验证者 PASS；全量七门全绿：Symbol 3399/3375/24/0、Semantic 5747/5643/104/0、Scripting 165/165） |

> **已知后续项（2026-08-22 F19/F13 验证确认，F21 已全部收口）**：① VB 对「SAIM 入表达式树」（`Function() AddressOf T.M01` 等）**静默无诊断**，C# 报 CS8927——**F21 新增等价 VB 诊断 BC37340**（`ERR_ExpressionTreeContainsAbstractStaticMemberAccess`，对齐 CS8927 语义）；② 复合赋值 `T.P += 1` 运行期值未单独实证——**F21 运行期值断言 6 实证**（并修复 `UseTwiceRewriter` 类型参数接收者丢失导致的 Bad IL）；③ **IntelliSense 全成员路径可能漏出访问器**——**F21 `IsNameableExtensionMember` 显式排除访问器**（`IsAccessor`/`MethodKind.PropertyGet/PropertySet`，D7 用例锁定；实证 `get_CharCount`/`set_CharCount` 落 `PropertyGet/PropertySet`，原「落 Ordinary」记录不成立，防御性保留）。

## 共享源码事实（所有 Vortex agent 以此为基准，不必重读全部源码）

> 已核实（2026-08-22，proposal/meeting 深挖逐条 Read/Grep）。引用以 `文件:行号` 给出，如需深读请直接 Read 该文件该区域。编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`），文件相对路径均相对仓库根。

### Part A 扩展成员消费（四层落点）

- **汇聚点**：`VB\Binding\Binder_Lookup.vb:1152-1171`（`LookupForExtensionMethodsIfNeedTo`——扩展成员唯一合并点；`:1169` 方法收集 + `:1170` `MergeInternalXmlHelperValueIfNecessary`）；`:1181`（`LookupForExtensionMethods` 收集主循环，逐层 binder）。
- **收集**：`CollectProbableExtensionMethodsInSingleBinder` 四处 override——`NamedTypeBinder.vb:99` / `NamespaceBinder.vb:85` / `ImportedTypesAndNamespacesMembersBinder.vb:130` / `TypesOfImportedNamespacesMembersBinder.vb:66` → `NamedTypeSymbol.AppendProbableExtensionMethods` → `GetExtensionMethods`（`VB\Symbols\NamedTypeSymbol.vb:338-351`）→ `AddMemberIfExtension`（`VB\Symbols\NamespaceSymbol.vb:518-526`，**只收 `SymbolKind.Method`**）。
- **扩展属性特例**：`MergeInternalXmlHelperValueIfNecessary`（`Binder_Lookup.vb:1259`，只收 `InternalXmlHelper.Value`）；`ReducedExtensionPropertySymbol` **已存在**（`VB\Binding\Binder_XmlLiterals.vb:1524`，剥接收者参数的属性符号）。
- **归约**：`MethodSymbol.ReduceExtensionMethod`（`VB\Symbols\MethodSymbol.vb:817`）→ `ReducedExtensionMethodSymbol.Create`（`VB\Symbols\ReducedExtensionMethodSymbol.vb:35-204`，含 `TypeArgumentInference` 泛型推断 + 约束检查）。
- **PE 元数据**：VB 只读 `HasExtensionAttribute`（`VB\Symbols\Metadata\PE\PEMethodSymbol.vb:698` / `PENamedTypeSymbol.vb:931`），**不读 C# 14 扩展成员**；C# 参考：`CSharp\Portable\Symbols\NamedTypeSymbol.cs:408-437`（两条并列收集路径 `doGetExtensionMembers` + `DoGetExtensionMethods`）、`CSharp\Portable\Symbols\Metadata\PE\PENamedTypeSymbol.cs:2295`（`TryGetExtensionMarkerMethod`）、`:2585/:2616`（`module.HasExtensionMarkerAttribute`）、`CSharp\Portable\Symbols\Metadata\PE\PEAssemblySymbol.cs`（扩展成员导入）。
- **运算符决议**：`VB\Semantics\Operators.vb:2847`（`CollectUserDefinedOperators`，类型层级收集 + 继承攀爬）、`:2942`（`GetTypeToLookForOperatorsIn`，类型参数操作数只取非接口约束）。
- **元数据编码实证**（`tmp/exp-consume/dump/` 反射 dump）：C# 14 扩展属性/运算符发射为外层 `[Extension]` 静态类 + 嵌套 `<G>$<哈希>` 分组类型（标 `[Extension]`，内放 `[ExtensionMarkerName]` 成员）+ `<M>$<哈希>` 标记类型（含 `<Extension>$(receiver)` 标记方法）+ 顶层无 `[Extension]` 实现方法（`get_CharCount(String)` / `op_Addition(MyVec,MyVec)`）。C# 规范原文：`csharplang\proposals\csharp-14.0\extensions.md:487`（分组类型）、`:595/:610-620`（`ExtensionMarkerNameAttribute`）、`:1147`（getter/setter 不标 `[Extension]`）。

### Part B 接口共享成员消费（SAIM 落点）

- **绑定拒绝点**：`VB\Binding\Binder_Expressions.vb:2913-2914`——`left` 为类型表达式且 `type.TypeKind = TypeParameter` 时**无条件**报 `ERR_TypeParamQualifierDisallowed`（BC32098）；`:2921` 对普通类型走 `LookupMember` 路径（改法参照）。
- **运算符**：`Operators.vb:2847`/`:2942`——类型参数操作数只取非接口约束，需补接口约束共享运算符。
- **运行时门控钩子（已在位）**：`VB\Symbols\AssemblySymbol.vb:335`（`SupportsRuntimeCapability`）、`:348-349`（`Case RuntimeCapability.VirtualStaticsInInterfaces`）、`:404-408`（`RuntimeSupportsVirtualStaticsInInterfaces`，检查 `RuntimeFeature.VirtualStaticsInInterfaces`）。
- **SAIM 元数据实证**（dump）：`IHasZero`1.get_Zero / Add 原始标志 = `Static|Abstract|Virtual`，`newslot=False`。C# 规范原文：`csharplang\proposals\csharp-11.0\static-abstracts-in-interfaces.md:10`（经类型参数访问）、`:23-24`（`static abstract T Zero` / `static abstract T operator +`）。
- **C# 参考**：`CSharp\Portable\Binder\Binder_Expressions.cs:8151`（`IDS_FeatureStaticAbstractMembersInInterfaces` 特性门控）、`:10026-10028`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces` 运行时检查）。

### 实证基线（fork vbi.exe + 自建 C# 14 测试库 `tmp/exp-consume/`，已运行）

| C# 能力 | VB 消费 | 实证 |
|---------|---------|------|
| 经典/ C#14 扩展方法 | ✅ | `5.Twice()`→10、`"hi".Shout()`→HI |
| C#14 扩展属性 | ❌ | `"hello".CharCount` → BC30456 |
| C#14 扩展运算符 | ❌ | `a+b` → Strict On BC30452 / Strict Off 晚绑定运行期失败 |
| SAIM 具体类型/经 C# 泛型 | ✅ | `MyNum.Zero`→0、`Mathy.Sum(Of MyNum)`→6 |
| SAIM 经类型参数 `T.Zero` | ❌ | BC32098 |
| VB 声明 `Shared` 接口成员 | ❌ | BC30270/BC30273（声明侧不在范围） |

## 关键设计决策（源自 meeting RESOLUTION + proposal，设计文档必须吸收）

1. **只消费、声明侧暂缓**（用户 2026-08-22）：VB 侧声明 `Shared` 接口成员 / `<Extension>` 扩展属性不在范围；声明侧作后续提案。
2. **Part A 四层延申**：①元数据导入层（读 `<G>$`/`<M>$`/`[ExtensionMarkerName]`，仿 C# PE 导入）；②收集层（`GetExtensionMethods`/`AddMemberIfExtension` 从「只收方法」扩为「收方法+属性+运算符」）；③归约层（复用 `ReducedExtensionPropertySymbol`，泛型补类型推断，仿 `ReducedExtensionMethodSymbol.Create`）；④合并/挂点层（汇聚点泛化 `MergeInternalXmlHelperValueIfNecessary` + `CollectUserDefinedOperators` 追加扩展运算符候选）。
3. **Part B 约束接口绑定**：`Binder_Expressions.vb:2913` 从「类型参数无条件拒绝」改为「先查约束接口（effective interface set）有同名共享成员则绑定、否则维持 BC32098」；CodeGen `constrained.` + 虚调用。
4. **运行时门控复用**：Part B 运行时不受支持检查复用现有 `RuntimeCapability.VirtualStaticsInInterfaces` 钩子（`AssemblySymbol.vb:348-349`），不从零建。
5. **现有扩展方法路径不动**：`AddMemberIfExtension` 收 `[Extension]` 方法继续走 `ReducedExtensionMethodSymbol`，新增路径并列而非改写。
6. **边界**：`Object` 接收者维持晚绑定（Strict Off 通道保留）、扩展成员仅早绑定参与、同名优先级沿用实例成员优先 → 扩展成员回退、`Imports` 语义一致。
7. **术语**：元数据导入按 C# 规范 `ExtensionMarkerNameAttribute`（`extensions.md:610-620`），非提案简写 `[ExtensionMarker]`。
8. **原型顺序**（Implication）：先 Part B `T.Zero`（改动点最小）→ 再 Part A 扩展属性 → 扩展运算符。

## F1 验收条件（概要设计 pass 标准）

- 覆盖**现状断点**（Part A：收集只收方法 / PE 不读扩展成员元数据 / 扩展属性运算符不可消费；Part B：BC32098 类型参数无条件拒绝）与**总体架构落点**（Part A 四层 + Part B 约束接口绑定）。
- 含 **行为对照表**：扩展属性 `.P`、扩展运算符 `a+b`、`T.Zero`/`T.Add`、Strict On/Off 分叉、`Object` 接收者晚绑定、`Imports` 作用域、`static virtual` 经接口名、同名优先级。
- 说明延申原则（复用 `ReducedExtensionPropertySymbol`/汇聚点/运行时钩子、现有路径并列不改写）与 C# 参考实现跟随（`NamedTypeSymbol.cs:408-437`、`PENamedTypeSymbol.cs`）。
- 说明代价与边界（Object 晚绑定保留、性能快速路径 `MightContainExtensions`、范围只消费）。

## F2 验收条件（详细设计 pass 标准）

- 逐条给出**改动文件 + 函数 + 行号区域 + 改动形状**（新属性/参数/分支/符号类），可被实施者直接照做。
- **Part A 元数据导入**：VB PE 层新增读 `<G>$`/`<M>$`/`[ExtensionMarkerName]`（仿 C# `PENamedTypeSymbol.cs:2295/2585/2616`），WellKnown 表核对 `ExtensionMarkerNameAttribute` 是否已存在（未核实，实现期核对）。
- **Part A 收集**：`GetExtensionMethods`/`AddMemberIfExtension` 扩为收属性/运算符；分组类型遍历；`MightContainExtensionMethods` 快速路径复用。
- **Part A 归约**：复用 `ReducedExtensionPropertySymbol`，泛型补类型推断；扩展运算符归约。
- **Part A 挂点**：汇聚点泛化 `MergeInternalXmlHelperValueIfNecessary`；`CollectUserDefinedOperators` 追加扩展运算符候选。
- **Part B 绑定**：`Binder_Expressions.vb:2913` 改判 + 约束接口查找；`constrained.` CodeGen；`GetTypeToLookForOperatorsIn` 补接口约束共享运算符。
- **Part B 运行时门控**：复用 `AssemblySymbol.vb:348-349` 钩子；诊断码族定案（实现期核对）。
- 零改动清单 + 边界（`.vbx` 与 Regular 语义一致、`Object` 晚绑定保留、现有扩展方法路径不动）。

## F3 验收条件（测试计划 pass 标准）

- 分层测试矩阵（L1 语义层 / L2 REPL 层 / L3 显示层 / L4 Regular 模式），参考 C# extensions / static-abstracts 测试强度。
- 用例矩阵覆盖：扩展属性 `.P`、扩展运算符 `a+b`、`T.Zero`/`T.Add` 泛型算法、Strict On/Off 分叉、`Object` 接收者晚绑定、`Imports` 作用域、`static virtual` 经接口名、同名优先级、`ExtensionMarkerNameAttribute` 术语识别、运行时不受支持门控。
- 既有测试更新清单 + 无副作用纪律（不网络/不写文件/不启动进程/不注册表；测不了就问用户）。
