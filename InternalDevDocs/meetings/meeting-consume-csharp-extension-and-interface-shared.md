# Visual Basic Language Design Meeting
August 22, 2026

本周议题是 `proposal-consume-csharp-extension-and-interface-shared`——让 VB 编译器**消费**两类 C# 已发布能力：Part A C# 14 扩展成员（扩展属性/运算符；扩展方法早已可用），Part B C# 11 接口共享成员（SAIM，泛型数学 `INumber(Of T)` 的地基）。范围已由用户定案（2026-08-22）：严格只做消费，VB 侧声明（`Shared` 接口成员 / `<Extension>` 扩展属性）不在本提案范围。策略是从 VB 现有的 extension/shared 策略**延申**，不引入新机制。

这场讨论比我们预想的更有底气。它不是"要不要做"的辩论——判入依据 D4 两档直接命中 P1，方向没有悬念；真正的活儿是把断点逐个钉死在源码里。翻源码时我们有两个发现，一个修正了提案对现状的表述（`RuntimeCapability.VirtualStaticsInInterfaces` 钩子其实已在位），一个对齐了 C# 元数据术语（`ExtensionMarkerNameAttribute`）。细节见下文。评审用五维评分与 C# 生态考量另存于 `evaluation-consume-csharp-extension-and-interface-shared.md`，本纪要只记讨论与落定。

## Agenda

* [Proposal: 消费 C# 扩展成员与接口共享成员](#proposal-消费-c-扩展成员与接口共享成员)

## Proposal: 消费 C# 扩展成员与接口共享成员

_Related: [`../proposals/proposal-consume-csharp-extension-and-interface-shared.md`](../proposals/proposal-consume-csharp-extension-and-interface-shared.md)；关联 `../proposals/proposal-byref-like-repl-safety.md`（D1 REPL 侧语义契约）、`decisions.md` D4（P1 两档判入）与 M7（ref struct interfaces / DIM / extensions 方向兼容）；姊妹参考 `../modvb/proposals/proposal-extension-properties.md`（VB 侧扩展属性声明，其 Unresolved question 明示「C# 未暴露扩展属性时如何消费/生成」——本提案消费侧补上这一问）_

### 场景与缺口

今天 VB 只能消费 C# 的经典 `[Extension]` 扩展方法；C# 14 扩展成员里的**扩展属性**和**扩展运算符**，VB 消费不到。SAIM 侧，具体类型静态成员（`MyNum.Zero`）与经 C# 泛型方法（`Mathy.Sum(Of MyNum)`）都能用，但 VB 自己写不了泛型数学——`T.Zero` 经类型参数访问被无条件拒绝。我们认同的缺口陈述是：**补齐 C# 生态互通**——.NET 泛型数学（`INumber(Of T)` 等）建在 SAIM 上（`csharplang\proposals\csharp-11.0\static-abstracts-in-interfaces.md`，champion #4436，C# 11 已发布）；C# 14 扩展属性/运算符已入语言并被新库采用。VB 不能写 `T.Zero` 泛型算法、不能用 `.Property`/`a + b` 天然语法消费扩展成员，就无法参与生态。

现状的难堪之处，实证一跑就显出来（fork vbi.exe + 自建 C# 14 测试库 `tmp/exp-consume/`，已运行）：

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

用户拿到 C# 14 库，`.Property`/`a + b` 天然语法不可用，只能退而调用裸访问器 `NewExt.get_CharCount(s)` / `VecExt.op_Addition(a, b)`——实证可用但丑陋、与生态文档不符；SAIM 接口无法在 VB 侧做泛型算法。最糟的是 Strict Off（vbi 默认）下扩展运算符**静默运行期失败**，比编译错误更糟：一个库成员装上就能用，运行期才炸。

判入依据是 `decisions.md` D4，两档直接命中 P1：①「C# 已照顾到的非底层内存机制用例」——扩展成员（C# 14）与 SAIM（C# 11）均已发布且非底层内存机制；②「C# interop 用例」——直接命中。方向与 `decisions.md` M7（extensions 方向兼容）一致。P1 硬约束（不引起无谓 regression / 不改变合法既有代码的正确语义）也守得住：纯消费侧、零新语法、扩展成员仅在早绑定查找作为回退候选且需 `Imports` 作用域，既有合法代码语义不变。

### 翻源码定位：断点逐一核实

提案的 Detailed design 给了源码锚点，我们逐行翻过去核实。沿着扩展方法的既有调用链往下走，第一站是 `Binder_Lookup.vb:1152-1171` 的 `LookupForExtensionMethodsIfNeedTo`——提案说这是扩展成员的**唯一汇聚点**。顺着收集主循环（`:1181`）逐层 binder 下去，落到 `NamespaceSymbol.vb:518-526` 的 `AddMemberIfExtension`：**只收 `SymbolKind.Method`**。C# 14 扩展属性/运算符的真实形态在嵌套分组类型里，根本走不到这条路径——这一眼就确认了 Part A 的断点。

再翻 `NamedTypeSymbol.vb:338-351` 的 `GetExtensionMethods`，只迭代普通成员；`Binder_Lookup.vb:1259` 的 `MergeInternalXmlHelperValueIfNecessary` 只收 `InternalXmlHelper.Value` 一个特例。让我们意外的是 `Binder_XmlLiterals.vb:1524`——`ReducedExtensionPropertySymbol` **已经存在**：XML 轴早就把 `Value` 归约成剥掉接收者参数的属性符号。扩展属性的归约机制不是从零造，是现成挂点。

Part B 的断点在 `Binder_Expressions.vb:2913-2914`：`left` 为类型表达式且 `type.TypeKind = TypeParameter` 时**无条件**报 `ERR_TypeParamQualifierDisallowed`（BC32098）。同函数 `:2921` 对普通类型走 `LookupMember` 路径——提案的改法就是让类型参数先查约束接口，仿这一行。运算符侧 `Operators.vb:2847` 的 `CollectUserDefinedOperators` / `:2942` 的 `GetTypeToLookForOperatorsIn` 现只取类型参数的非接口约束，接口约束上的共享运算符被跳过。

C# 参考实现我们也对了：`NamedTypeSymbol.cs:408-437` 有**两条并列收集路径**（`doGetExtensionMembers` + `DoGetExtensionMethods`）；`Binder_Expressions.cs:8151` 有 SAIM 特性门控（`IDS_FeatureStaticAbstractMembersInInterfaces`）、`:10026/10028` 有运行时检查（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`）。VB 侧不是没有参照物。全部锚点核实结果（`文件:行号` 均已逐行翻过）汇总如下：

| 断点 | 提案证据 | 复核 |
|------|---------|------|
| 扩展成员收集只认方法 | `NamespaceSymbol.vb:518-526`（`AddMemberIfExtension` 只收 `SymbolKind.Method`） | ✅ 已核实 |
| `GetExtensionMethods` 只迭代普通成员 | `NamedTypeSymbol.vb:338-351` | ✅ 已核实 |
| 扩展成员唯一汇聚点 | `Binder_Lookup.vb:1152-1171`（`LookupForExtensionMethodsIfNeedTo`） | ✅ 已核实 |
| 扩展属性特例仅 `InternalXmlHelper.Value` | `Binder_Lookup.vb:1259`（`MergeInternalXmlHelperValueIfNecessary`） | ✅ 已核实 |
| 归约属性符号已存在 | `Binder_XmlLiterals.vb:1524`（`ReducedExtensionPropertySymbol`，剥接收者参数） | ✅ 已核实 |
| SAIM 类型参数限定被无条件拒绝 | `Binder_Expressions.vb:2913-2914`（`ERR_TypeParamQualifierDisallowed` = BC32098，`TypeKind = TypeParameter` 即抛） | ✅ 已核实 |
| 类型参数操作数只取非接口约束 | `Operators.vb:2847`（`CollectUserDefinedOperators`）/ `:2942`（`GetTypeToLookForOperatorsIn`） | ✅ 已核实 |
| C# 参考：两条并列收集路径 | `NamedTypeSymbol.cs:408-437`（`doGetExtensionMembers` + `DoGetExtensionMethods`） | ✅ 已核实 |
| C# 参考：SAIM 特性门控 + 运行时检查 | `Binder_Expressions.cs:8151`（`IDS_FeatureStaticAbstractMembersInInterfaces`）、`:10026/10028`（`ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`） | ✅ 已核实 |

翻源码的过程中，我们有两个发现值得单独说。

**发现一：SAIM 运行时门控已部分在位——这修正了提案「全树无 StaticAbstract 处理」的表述。** 提案写的是「VB 编译器全树无 `StaticAbstract` 处理（grep `Compilers/VisualBasic` 零匹配）」。我们 grep 确认了这个说法**基本属实**——`StaticAbstract` 全树仅命中注释（`AssemblySymbol.vb:406`，引用 C# 属性名），确实没有 StaticAbstract 的绑定/收集路径。**但**就在同一份 `AssemblySymbol.vb` 里，`SupportsRuntimeCapability`（`:335`）已经有 `Case RuntimeCapability.VirtualStaticsInInterfaces`（`:348-349`）→ `RuntimeSupportsVirtualStaticsInInterfaces`（`:404-408`，检查 `RuntimeFeature.VirtualStaticsInInterfaces`）。也就是说：**「运行时不受支持检查」所需的基础设施已经在位**——Part B 的运行时门控可复用这个钩子，不必仿 C# 从零建。这是对提案可行性评估的**正向增量**，我们建议写进 Detailed design。

**发现二：C# 扩展成员元数据编码已核实，术语要对齐。** C# 14 扩展成员的编码形态是：扩展分组类型（content-based 名，`extensions.md:487`）+ 嵌套扩展标记类型 + 成员标 `[ExtensionMarkerName]` 引用标记类型 + 顶层静态实现方法（**无** `[Extension]`）。提案 A1 表格把属性简写成 `[ExtensionMarker]`，但 C# 规范里真实属性名是 `ExtensionMarkerNameAttribute`（`extensions.md:610-620`）——存在**术语不一致**。实现导入时须对准真实属性名（`TryGetExtensionMarkerMethod` / `module.HasExtensionMarkerAttribute`），否则会漏收。属品质细节，不影响方向，但值得写进 spec 落款。

### 候选方案与权衡

**PROPOSAL A — 四层延申消费扩展成员（采用，提案方案）。** 元数据导入层（读 `<G>$` 分组类型 + `[ExtensionMarkerName]` 成员 + `<M>$` 标记类型）→ 收集层（`GetExtensionMethods`/`AddMemberIfExtension` 从「只收方法」扩为「收方法 + 属性 + 运算符」）→ 归约层（复用 `ReducedExtensionPropertySymbol`，泛型扩展属性/运算符补类型推断）→ 合并/挂点层（汇聚点泛化 `MergeInternalXmlHelperValueIfNecessary`；`CollectUserDefinedOperators` 追加作用域内 `[Extension]` 类分组类型运算符候选）。每层都有现成挂点，C# 参考实现 = `NamedTypeSymbol.cs:408-437` 并列两条收集路径 + `PENamedTypeSymbol.cs` 扩展成员导入。

**PROPOSAL B — SAIM 经约束接口绑定（采用，提案方案）。** `Binder_Expressions.vb:2913` 从「类型参数无条件拒绝」改为「先查该类型参数约束接口（effective interface set）是否有同名共享成员，有则绑定（仿 `:2921` 普通类型 `LookupMember` 路径），无则维持 BC32098」；CodeGen 发 `constrained.` + 虚调用；`GetTypeToLookForOperatorsIn` 补接口约束上的共享运算符。

**PROPOSAL C — 仅消费，声明分离（采用，范围定案）。** VB 侧声明 `Shared` 接口成员 / `<Extension>` 扩展属性不在范围。好处：缩小实现面、暗风险最小、先行兑现消费价值；声明侧留作后续提案（与 ModVB `proposal-extension-properties.md` 衔接）。

**已否决备选（留档）：** 启发式识别裸 `get_`/`op_` 方法（不读 `<G>$`/标记类型）——实现简单，但我们一致认为无法区分「扩展成员实现方法」与「普通静态方法同名」，误判风险高：C# 元数据里顶层实现方法无 `[Extension]`，仅靠名字前缀启发式在混合库中必然误收（例如手写 `get_Count` 静态方法）。**否决。** SAIM 用工厂/委托绕开（用户自写泛型参数包装）——重复造轮子，且无法恢复 `T.Zero` 的声明式语法。**否决。** 声明侧已定案排除（用户 2026-08-22，消费优先）。

几个权衡点我们逐一过了一遍：

- **D4 P1 两档都命中**（C# 已照顾到的非底层内存机制用例 + C# interop 用例）；P1 硬约束满足（纯消费侧、零新语法、扩展成员仅在早绑定回退候选、需 `Imports` 作用域，既有合法代码语义不变）。
- **Strict Off / Object 接收者**：扩展成员只在**早绑定**查找参与，`Object` 接收者维持晚绑定（与扩展方法现状一致）。实证里 Strict Off 静默晚绑定运行期失败属「未找到扩展运算符」的回退——接入扩展运算符决议后早绑定命中即消除。**判定**：保留晚绑定通道不属缺陷，是既定取舍。
- **同名优先级**：沿用现有规则——实例成员优先 → 扩展成员回退；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`）。与 C# 一致，无新歧义面。
- **`InterfaceName.Member` 直呼 vs 经类型参数**：对齐 C#——`static abstract` 可经类型参数 `T.M` 消费；`static virtual`（带默认实现）经类型参数仍 BC32098、经接口名直呼与 `static abstract` 同为 BC37314（与 C# CS8926 一致，`static-abstracts-in-interfaces.md:264-266` static virtual 只能在类型参数上调用）。**F10/F11 实证覆盖本条（2026-08-22）**：原「static virtual 可经接口名呼」表述已由实现修正。
- **运行时支持检查**：这是我们本场最有底气的正向增量——VB 已有 `RuntimeCapability.VirtualStaticsInInterfaces` 钩子（`AssemblySymbol.vb:348-349/404-408`），Part B 的运行时门控直接复用，不必从零建。诊断码号留任务计划阶段定。
- **上游 Roslyn VB 是否已有 SAIM 参考实现可低成本合并**：**未核实（`Suspect`）**——fork 是修剪/本地化 Roslyn，需核对上游基准 commit（`upstream-merge.md`）。但结合发现一，即使上游无 SAIM 绑定路径，运行时钩子与 C# 参考路径足以支撑从零实现，成本可控，此项不阻塞。
- **扩展成员收集的性能成本**：遍历 `<G>$` 分组类型比现方法收集贵；需 `MightContainExtensions` 类快速路径规避（C# 已这么做）。`MightContainExtensionMethods` 对 `[Extension]` 类天然为真，无需新标志。**判定**：可接受，C# 先例。
- **扩展属性交互面（setter / 链式 / `With`）**：setter（`obj.P = v` → `set_P(obj, v)`）、链式、`With` 需额外规则。原则裁决：随 C# 发射而消费（getter 对称），细节留实现阶段实证枚举。
- **符号呈现方式**：合成「扩展属性/运算符」符号接入现有 `ExtensionMethodGroup`/回退决议，还是包装底层 `[ExtensionMarkerName]` 成员——留任务计划阶段细化，属实现问题非设计问题。
- **与既有语法/机制的协同**：复用 `ReducedExtensionPropertySymbol`（已存在，`Binder_XmlLiterals.vb:1524`）与 `LookupForExtensionMethodsIfNeedTo` 汇聚点（`Binder_Lookup.vb:1152`），现有扩展方法路径不动（并列而非改写）——**实现成本被现有机制大幅摊薄**。

### VB 基因对照

本提案**没有新增任何语法表面积**——只把 VB 现有 extension/shared 策略的候选面从「方法」扩到「属性/运算符」、从「普通类型限定」延申到「类型参数限定」，与「不引入第二种做事方式」原则（`vblang\meetings\2018\vbldm-notes-2018.06.13.md` Review process）直接相容。它是消费 C# 已发布特性，按 C# 语义对齐（`constrained.` 调用、`static abstract` 经类型参数、`static virtual` 受限访问、实例成员优先），无偏离理由——C# 领跑 CLR/库层变更，VB 做语法与语义适配，符合「默认跟随 C#」。零新语法、纯增量、既有合法代码语义不变 → 无 breaking change 面，符合「We will almost never make breaking changes to Visual Basic」。

与主线对照表（evaluation-standard 2.3）看，本提案属 extensions **消费**侧，主线/M7 方向兼容，**非 Anthony 式激进延伸**——不扩张语法面，只补齐 C# 生态互通缺口。与 ModVB 姊妹提案 `proposal-extension-properties.md`（声明侧）互补：其 Unresolved question「C# 未暴露扩展属性时如何消费/生成」由本提案消费侧补上（C# 14 已暴露，消费路径即本提案四层）。

### RESOLUTION:

1. **方向定案（PROPOSAL A + B，范围 C）**：VB 编译器消费 C# 14 扩展成员（扩展属性/运算符）与 C# 11 接口共享成员（SAIM）——只做消费、零新语法、从 VB 现有 extension/shared 策略四层延申 / 经约束接口绑定。
2. **范围锁定**：VB 侧声明（`Shared` 接口成员 / `<Extension>` 扩展属性）不在本提案范围，消费优先（用户 2026-08-22 定案）；声明侧作后续提案，与 ModVB `proposal-extension-properties.md` 衔接。
3. **Part A 落点**：元数据导入层读分组/标记类型 + `[ExtensionMarkerName]`（以 C# 规范真实属性名 `ExtensionMarkerNameAttribute` 为准，非提案简写 `[ExtensionMarker]`）；收集层扩 `GetExtensionMethods`/`AddMemberIfExtension`；归约层复用 `ReducedExtensionPropertySymbol` + 补泛型推断；挂点层泛化 `MergeInternalXmlHelperValueIfNecessary` + `CollectUserDefinedOperators` 追加作用域扩展运算符候选。现有扩展方法路径不动（并列而非改写）。
4. **Part B 落点**：`Binder_Expressions.vb:2913` 改为「约束接口 effective interface set 上有同名共享成员则绑定、否则维持 BC32098」；CodeGen `constrained.` + 虚调用；**运行时门控复用现有 `RuntimeCapability.VirtualStaticsInInterfaces` 钩子（`AssemblySymbol.vb:348-349`）**；`GetTypeToLookForOperatorsIn` 补接口约束共享运算符。
5. **优先级**：**D4 P1 两档直接判入**，满足 P1 硬约束（无 breaking change / 无谓 regression）。
6. **边界**：`Object` 接收者维持晚绑定（Strict Off 通道保留）、扩展成员仅早绑定参与、同名优先级沿用实例成员优先 → 扩展成员回退、`Imports` 语义一致。

### Implication

- spec 合并时把「四层延申 + SAIM 约束接口绑定 + 运行时钩子复用」写进 Detailed design；补 A1 元数据术语对齐（`ExtensionMarkerNameAttribute`）。
- 最小原型：先在 fork vbi 里最小实现 Part B 的 `T.Zero`（`Binder_Expressions.vb:2913` 改判 + `constrained.` CodeGen），验证泛型数学算法；再按四层推进 Part A（先扩展属性、后扩展运算符）。
- 补测试矩阵（无副作用）：`T.Zero`/`T.Add` 泛型算法、`"hello".CharCount`、`a + b` 扩展运算符、Strict On/Off 分叉、`Object` 接收者晚绑定、`Imports` 作用域、`static virtual` 受限（接口名 BC37314、经类型参数 BC32098）、同名优先级、运行时不受支持门控。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`（`Suspect`）：上游 Roslyn VB 基准 commit（`upstream-merge.md`）是否含 SAIM 参考实现可低成本合并——未核实。但运行时钩子在位、C# 参考路径完整，从零实现成本可控，此项不阻塞。
- `OPEN QUESTIONS`：扩展成员在 VB 符号模型里的呈现方式——合成符号接入 `ExtensionMethodGroup`/回退决议，还是包装底层 `[ExtensionMarkerName]` 成员。任务计划阶段细化。
- `OPEN QUESTIONS`：扩展属性 setter / 链式调用 / `With` 语句交互的具体规则——随 C# 发射消费，细节留实现阶段实证枚举。
- `TODO`：诊断码族定案——SAIM 识别缺失 / 运行时不受支持的具体 BC 码号（用既有 BC 族或仿 C# 专用码，任务计划阶段定）。
- `TODO`：A1 元数据导入的术语对齐——按 C# 规范 `ExtensionMarkerNameAttribute` 实现 `TryGetExtensionMarkerMethod`/`module.HasExtensionMarkerAttribute` 等价 VB 侧。
- `TODO`：无副作用单测（`Scripting\VisualBasicTest\` + `VisualBasicSemanticTest\`）：Part A/B 目标示例 + 分叉矩阵。
- `Follow-up`：声明侧（VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性）作后续提案，与 ModVB `proposal-extension-properties.md` 衔接。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——D4 P1 两档直接判入（C# interop 用例），零新语法、纯消费侧、无 breaking change；实证已运行（断点逐项核实）、源码锚点全部复核、运行时钩子已在位，实现成本被现有机制摊薄；范围定案（消费/声明分离）收窄风险。提案归 active 根目录。

---

_五维评分、返工建议与 C# 生态/互操作考量见 [evaluation-consume-csharp-extension-and-interface-shared.md](evaluation-consume-csharp-extension-and-interface-shared.md)。_
