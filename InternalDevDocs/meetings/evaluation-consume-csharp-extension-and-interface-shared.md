# 评审报告：消费 C# 扩展成员与接口共享成员

> 本报告是 `meeting-consume-csharp-extension-and-interface-shared.md` 的**独立评审文档**（内部评价），与官方 LDM 会议记录分开存放。依据 `modvb\evaluation-standard.md` 五维框架，证据等级按六档阶梯标注。**评分落到提案当前状态：Proposed（实证已运行、无原型实现、无 spec）**。

## 五维评价

| 维度 | 得分 | 评价 | 证据等级 | 问题 |
|------|------|------|----------|------|
| 效果 | 4/5 | 目标改进清晰（补齐 C# 生态互通）、边界完整（范围定案消费侧）；**问题侧已实证运行**（BC30456/BC30452/BC32098 断点逐项核实），但**方案侧无原型**（Prototype: Not Started），四层/约束绑定机制止于源码锚定 | 已检查 + 已运行（问题实证）/ 方案无原型 | 方案需原型验证；扩展属性交互面细节未实证 |
| 特性 | 5/5 | 完全延续 VB 基因：零新语法、延申现有 extension/shared 策略；复用 `ReducedExtensionPropertySymbol`/`LookupForExtensionMethodsIfNeedTo`/`SupportsRuntimeCapability`；职责单一（只消费）无杂质 | 已检查 | 无 |
| 品质 | 4/5 | 六章节结构完整（Summary/Motivation/Detailed design/Drawbacks/Alternatives/Unresolved questions + 证据来源附录）；源码锚点逐行标注、实证表清晰、范围与原则裁决透明；**个别细节不精确**：A1 简写 `[ExtensionMarker]` 与 C# 实际 `ExtensionMarkerNameAttribute` 不符、「VB 全树零匹配 StaticAbstract」表述未提及运行时钩子已在位 | 已检查 | 术语对齐；运行时钩子发现未写入提案 |
| 属性 | 4/5 | 主维度明显受益：雷（实现成本被现有机制摊薄）、风（与 C# 生态方向一致、M7 兼容）、水（消费现代 C# 库强化互通差异化）、光（盘活泛型数学/扩展库资产）；**暗风险小且被显式识别**（扩展成员收集性能、SAIM 打开语言面、Strict Off 晚绑定边界均在 Drawbacks 记录）；**范围定案（消费/声明分离）进一步收窄风险**；唯性能与交互细节未原型验证 | 已检查（待定） | 性能成本、setter/With 交互需原型确认 |
| 炼金成分 | 4/5 | 材料来源标注准确完整：C# 14 extensions.md / C# 11 static-abstracts-in-interfaces.md（镜像）、ModVB `proposal-extension-properties.md`（声明侧参考）、VB 编译器源码（锚点）、实证（tmp/exp-consume）；成分影响符合预期（借鉴 C#=风属性兼容、继承 VB 现有机制=盘活资产）；**个别来源表述略含糊**（`[ExtensionMarker]` 术语、零匹配表述） | 已检查 | 术语与表述细节 |

## 总判定

- 达成程度：**达成**（消费侧方向明确、机制可执行、源码锚点全部复核、运行时钩子在位；未决问题收窄到实现级）。
- LDM 三态建议：**Active**（D4 P1 直接判入）。
- 主要问题：①方案无原型（效果证据止于问题侧实证）；②A1 元数据术语与 C# 规范需对齐；③运行时钩子已在内（`AssemblySymbol.vb:348-349`）未写入提案，宜补充为正向证据。

**未决问题计数的「≥4 封顶」规则审视**：提案 Unresolved questions 节曾列 6 项设计问题（setter / Strict Off·Object 接收者 / `InterfaceName.Member` / `static virtual` / 同名优先级 + 声明侧范围），但其中 **5 项已由原则裁决块（用户 2026-08-22）消解为决策、1 项（声明侧）已定案排除**，剩余**仅 2 项实现级待定**（上游 SAIM 参考合并、符号呈现方式），均非核心语法未定型。因此「未决问题 ≥4 → 效果证据封顶至 3–4」规则**不触发**；效果维持 4/5 由「方案无原型」单独支撑。

## 返工建议

- 补充证据：把 `AssemblySymbol.vb:348-349/404-408`（`RuntimeCapability.VirtualStaticsInInterfaces`）写入 Detailed design 作为 Part B 运行时门控的现成落点——降低实现成本估算。
- 术语对齐：A1 元数据表按 C# 规范 `ExtensionMarkerNameAttribute` 改写（`extensions.md:610-620`），`[ExtensionMarker]` 仅作内部简写注明。
- 原型优先级：先 Part B `T.Zero`（改动点最小、收益可演示泛型数学）→ 再 Part A 扩展属性 → 扩展运算符。
- 未决问题处理：确认上游 Roslyn 基准是否含 VB SAIM（`Suspect` 消解）；符号呈现方式并入任务计划阶段。

---

## 附录：C# 生态与互操作考量

> 依据 `..\csharplang-index.md` 与 `InternalDevDocs\csharplang\` 镜像。C# 原文引用均先在镜像 Grep 核实、逐字转抄并标注来源。`decisions.md` 的 M1–M8 / D1–D4 为 VBScript.NET 侧权威。

**C# 现实方向 vs 提案**：
- **扩展成员（C# 14/15，`csharplang\proposals\csharp-14.0\extensions.md`）**：C# 扩展从方法扩到属性/运算符。元数据编码已核实——「Extension blocks are grouped by their CLR-level signature. Each CLR equivalency group is emitted as an **extension grouping type** with a content-based name.」（`extensions.md:487`）；成员标 `[ExtensionMarkerName]` 引用标记类型（`:595`、`:610-620`）。C# 15 续扩 extensions，元数据格式仍可能演进——VBScript.NET 消费侧须跟随。
- **SAIM（C# 11，`static-abstracts-in-interfaces.md`）**：「The members can be accessed off of type parameters that are constrained by the interface.」（`:10`）——`T.Zero`/`T.Add` 即此语义；`static abstract T Zero { get; }` / `static abstract T operator +(T t1, T t2)`（`:23-24`）。CodeGen `constrained.` 调用与 C# 一致。泛型数学（`INumber(Of T)`）建于此——VB 消费后即可写 VB 版泛型数学算法。
- **上游参考实现**：C# 侧 `NamedTypeSymbol.cs:408-437`（两条并列收集路径）、`PENamedTypeSymbol.cs`（扩展成员元数据导入）、`Binder_Expressions.cs:8151/10026-10028`（SAIM 特性门控 + 运行时检查）均已在位，是 VB 侧实现的直接对照物。

**对 VBScript.NET 的适应建议**：
- Part B 运行时门控**复用现有 `SupportsRuntimeCapability` / `RuntimeCapability.VirtualStaticsInInterfaces`**（`AssemblySymbol.vb:335/348-349/404-408`），不必从零建；诊断码号仿 C# 专用码用 BC 族。
- Part A 用 `MightContainExtensions` 类快速路径规避分组类型遍历性能成本（C# 先例）。
- 扩展成员消费对齐 C# 元数据（`[ExtensionMarkerName]`）、`Imports` 语义与现有扩展方法一致；`Object` 接收者维持晚绑定（Strict Off 通道保留，与扩展方法现状一致）。
- 声明侧（VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性）作后续提案，先消费后声明——与 C# 的元数据编码对齐后再做声明，可保证对称。

**对 RESOLUTION 的影响**：无变化——以上均支持 RESOLUTION 的 Active 判定与 P1 优先级，并为实现提供现成落点（运行时钩子、C# 参考路径、`MightContainExtensions` 快速路径）。
