# VBScript.NET 设计决策记录

## 头部

- **本文件用途**：记录 VBScript.NET（.vbx）面对 C#/CLR/.NET 生态现实的设计决策，以及「C# 现实方向 → VBScript.NET 应对」映射。**本节原为 `csharplang-index.md` 第三节（M1–M8），按用户指示独立成文**——csharplang-index 只保留 C# interop 事实（T1–T8、文件索引、引用纪律），VBScript.NET 侧决策统一收敛到本文件。
- **如何使用**：meeting agent 评估提案时，先读本文件「二、M1–M8」定位相关映射与决策；C# 事实（T1–T8、文件索引、引用纪律）见 `csharplang-index.md`；历史会议决策见 `modvb\meetings/`。
- **相关文件**：`csharplang-index.md`（C# interop 事实索引）；`modvb\meetings/`（102 篇 LDM 会议纪要，其「附录：C# 生态与互操作考量」引用本文件 M1–M8）；`vblang\spec\types.md`（VB 受限类型规则）；`{{VBRefStructHelper}}`（VB ref struct 分析器）；`dream-of-vbdev.md`（愿景/意象输入，非决策权威）。

---

## 一、决策修正记录（2026-08-09）

以下决策修正了旧表述（原散落在 csharplang-index.md 第三节 / 会议附录），**以本文件为权威立场**。

> **引用纪律（2026-09-12 定，强制）**：引用本节的决策一律**按节号**（`D1`–`D6`）并写明小节，**不得按 `文件:行号` 引用**——本文件会持续增补，行号不稳定（已实测：D5 插入后 D4 整体下移，导致会议纪要里 `decisions.md:51-56` 这处「P1 硬约束」引用**已指向 D6**）。历史文档中遗留的行号引用，**以节号为准**解释。

### D1. ref struct 在 VB 的解法 = 自定义分析器（RefStructHelper）

- **VB 规范已有受限类型分析规则**：`System.RuntimeArgumentHandle`、`System.ArgIterator`、`System.TypedReference` 一类受限类型的栈引用限制已写入 `vblang\spec\types.md`。
- **`{{VBRefStructHelper}}` 已把规则扩展为 BCX 系列错误码，实现 ref-safe**：BCX31394（转 Object/ValueType）、BCX31396（Nullable(Of T) / 泛型类型实参）、BCX32061（受限/特殊类型作泛型约束）、BCX36598（LINQ 装箱）、BCX36640（lambda 闭包装箱）、BCX37052（async/iterator 状态机装箱）、BCX31393（继承实例方法装箱）。
- **但编译器层面尚未做到 suppress ref struct obsolete error**。
- **VBScript.NET 做法**：**移植 RefStructHelper 分析器进编译器内部，并在编译器层面 suppress ref struct obsolete error**——这是消费 C# 13 `ref struct` 接口类型（`allows ref struct` 反约束）的前提。
- **影响**：修正旧表述「VB 基本不支持 ref struct」→「VB 通过自定义分析器实现 ref-safe；编译器层面待移植 suppress obsolete error」。

### D2. vbx NativeAOT 桥接机制

- **AOT 对 dynamic / 晚期绑定不友好是事实，由用户（VBScript.NET 作者）负责。**
- **落地机制**：**生成 vbproj + 普通 VB 代码 → 用改版 VB 编译器编译 dll → 交给 .NET SDK 对 dll 执行 `PublishAot`**。
- **用户须确保产物 `IsAotCompatible`**；改版 VB 编译器在编译生成的 vbproj 时**已能产生相应警告**。
- **影响**：修正旧表述「编译到受管程序集 + source-gen 桥」→ 具体化为上述 PublishAot 桥；「双模路线」的编译产物侧即此桥。

### D3. postfix-casting `(As Type)` 语义

- `(As Type)` 是**显式转换**（meeting 已裁定锚定 CType 语义），**不是 TryCast**（`As?` 变体被拒）、**也不是 Option Strict Off 的 callsite 隐式转换**。
- 作用：把动态/Any/晚绑定值显式转成强类型 T，后续 `.Member` 变为早绑定——「默认安全、按需动态」路线的**类型化出口**。

### D5. 基础功能的落地细节以 C# / csi 实现为设计蓝本（用户定案，2026-09-10）

- **定案**：vbi 的**基础功能**（脚本/提交模型、构造器、初始化器、入口点、类型落点、名字查找等）在**落地详细设计**阶段，以 **C# / csi 的实现为参考蓝本**；目标是在**架构上让 vbi 追平 csi**，而不是另起一套模型。
- **依据**：csi 由微软官方维护约十年，其基础机制经过长期验证；本 fork 在这些区域与 C# 的分叉处，多数不是有意设计，而是**移植不完整**。
- **已证实例**（均有 `文件:行号` 锚点，非口号）：
  1. **提交构造器**——C# 把「提交构造器」与「静态初始化器构造器」分成两个类（`SynthesizedSubmissionConstructor : SynthesizedInstanceConstructor`，静态走独立的 `SynthesizedStaticConstructor`）；VB 共用一个类，导致共享构造器带上实例版形参（见 `issues\issue-submission-shared-field-initializer-typeload.md`）。
  2. **扩展方法承载**——C# 对「成员必须 `static`」有用户可见诊断 CS1105（`SourceOrdinaryMethodSymbol.cs:243-245`）；VB 只有 `Debug.Assert(Me.IsShared)`（`SourceMethodSymbol.vb:1504`）。
  3. **脚本里的顶层类型落点**——**脚本模式下两侧其实同形**：C# 的 `CreateScriptRootDeclaration`（`CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:266-303`）与 VB（`VisualBasic\Portable\Declarations\DeclarationTreeBuilder.vb:175-198`）都把非 namespace 的顶层成员（含兄弟类型）塞进脚本类，两侧 `CreateScriptClass` 修饰符也同形。差异在**另一条路径**：C# 的「文件式程序 / 顶层语句」（`SourceCodeKind.Regular` 的 `acceptSimpleProgram`，`CSharp\...\DeclarationTreeBuilder.cs:142` + `:152-156`）把兄弟类型留在命名空间层——**VB 没有这个模式**（全树无 `SimpleProgram`/`TopLevelStatements` 命中，无 `SynthesizedSimpleProgramEntryPointSymbol`）。**故这一条是「C# 有、VB 没有的一个特性」，不是「同一特性两侧建模不同」**；要在 VB 侧对齐属**新特性**，须评估与既有 `.vbx` 语义、`#Load`、`Submission` 链的兼容，不按移植修补对待。
  4. **正向测试覆盖错 kind**——既有 `SourceExtensionMethods` 用 `DeclarationKind.Script`，而产品出货的所有脚本宿主都产 `Submission`；测试覆盖的不是产品走的那条路。
- **落地约束**：
  - 参照 C# 时**要说明「为什么 VB 必须分叉」**，不能因存在分叉就默认放弃对齐（VB 有 C# 无的概念：`Module`、`Shared`、晚期绑定）。
  - 比较结论要落到 `文件:行号`，能实证就实证；「C# 这么做」本身不是理由，**C# 这么做且 VB 没有对应理由**才是。
  - 此原则作用于 **plan 阶段的 `design-detailed.md`**，也作用于 propose/meeting 阶段的候选设计。

### D6. 兼容性约束只对正式版（GA）成立（用户定案，2026-09-12）

- **定案**：脚本语义的兼容性约束**只对正式版（GA）成立**。在只有 beta / preview 发行版的期间，**不得以「breaking change / 破坏既有语义」为由否决设计**；beta 期间发出的脚本语义调整不落入 D4 的 P1 硬约束（「不引起无谓的 regression / breaking change」）。
- **实证（只有 beta，无 GA）**：
  - MSIX 清单 `Installer\VBInteractive.WindowsDesktop.Installer\Package.appxmanifest:12` `Version="1.2.0.0"`；`:15` `DisplayName` = `N2Fork VB Interactive (Preview)`、`:34` = `N2Fork VB Interactive (preview)`。
  - `proposals\README.md:73` 逐字「VBScript.NET **1.2 beta** 已发布到微软商店」；`:35` 编译器包版本 `2.0.0-Beta`。
  - 全仓 `正式版` / `GA` **零命中**。
- **边界（防止本条被滥用）**：本条**只解掉「兼容性」这一条否决理由**。其它理由——**实现可行性、与 D5 的同形性、机制收益与代价、规范的可表达性**——**不受影响**，仍须逐条论证。举「兼容性」以外的理由否决时，本条不适用。
- **用途**：propose / meeting 阶段的候选裁决与 plan 阶段的取舍，凡出现「这会改掉既有语义」类论据，先对照本条判断该论据是否成立。

### D4. 提案优先级判定规则（用户定案，2026-08-09）

- **P1 两档直接判入**：
  - **C# 已照顾到的、非底层内存机制相关用例** → P1（与 C# 生态同向，风险最低）；
  - **C# interop 用例**（如 **consume ref struct**）→ P1。
- **其余提案**：按 LDM 风险评估（三态判定 + 复杂度/成本/优先级、正文 LDM 追问清单）决定是 P1 还是更低优先级。
- **P1 硬约束（2026-08-09 修正理解）**：P1 提案在**设计上不引起无谓的 regression / breaking change**——即不改变「合法既有代码的正确语义」。**对 C# interop，破坏性变化有时不可避**：例如旧代码在 obsolete 类里误用 ref struct（运行期本会 `InvalidProgramException`），ModVB 体系下正确报错是**修错而非回归**，不影响其 P1 地位。
- **用途**：本规则是排优先级工作的判定闸门，与 M1–M8 映射配套使用（命中 P1 两档的提案优先进入实现路线图）。

---

## 二、M1–M8：C# 现实方向 vs VBScript.NET 应对

> 通用背景：VB LDM 已退化为「只做与 C# 兼容」，Anthony 主张 VB 保持特色。VBScript.NET（.vbx）基于修改版 Roslyn VB 编译器，**必须能适应 C#/CLR/.NET 现实**。下表按 ModVB 提案主题给「C# 现实方向 → VBScript.NET 应对」。

### M1 native-instruction-helpers（inactive）↔ 函数指针/内建 IL（T3）
- C# 现实：`delegate*` + `[UnmanagedCallersOnly]` + `System.Runtime.Intrinsics`（在 dotnet/runtime）把 IL 能力以 unsafe 形式暴露。
- 提案响应：VB 无 unsafe/指针，native-instruction-helpers 提供**VB 侧的安全 IL 出口**（方法调用内联为 IL opcode：localloc/volatile/checked 等）。
- 考量：方向**不冲突**，是互补的「VB 特色互操作桥」；但需与 C# 的 function pointer/UnmanagedCallersOnly 元数据互通，且注意 unsafe-evolution 后部分 opcode 语义落在 requires-unsafe 边界。

### M2 any-pseudotype / postfix-casting / typeless-declarations（active）↔ dynamic/COM 晚期绑定（T4, T7）
- C# 现实：`dynamic`（C# 4）边缘化，表达式树与 Span 冲突，unsafe-evolution 质疑 dynamic 的安全性；COM 语言层投入少，转向 source-gen。
- 提案响应：Any 伪类型 + `(As Any)` 单表达式晚期绑定 = VB 对 dynamic/COM 的差异化答案（VB6/VBA 回归）。
- 考量：**这是 VB 最大的特色面**，但与 AOT/trimming 方向**张力最大**（晚期绑定=反射，NativeAOT 难支持）。建议：保留晚期绑定面向 COM/Office 场景（VB 传统强项），同时提供类型化出口——postfix-casting `(As Type)` 是**显式转换**（meeting 已裁定锚定 CType 语义），**不是 TryCast**（`As?` 变体被拒）、**也不是 Option Strict Off 的 callsite 隐式转换**；它把动态/Any 值显式转成强类型 T，后续 `.Member` 变为早绑定。如此让 .vbx 脚本可「默认安全、按需动态」。**落地约束见 D3。**

### M3 return-byref / out-arguments（active）↔ ref 系（T2）
- C# 现实：ref returns（C# 7）、ref fields/scoped/UnscopedRef（C# 11）、ref readonly（C# 12）——ref 安全模型是 C# 低层主线。
- 提案响应：`Return True, value:=result` 一次返回+写回 ByRef/Out。
- 考量：方向**兼容**（C# 也在强化 byref 返回）。但 VB `ByRef` 语义≠C# `ref`（VB 不参与 ref-safe-context）；`.vbx` 若想让低层库互通，需明示 ByRef 参数的逃逸语义，避免被 C# 的 ref 安全规则卡住（跨语言调用时 RefSafetyRules 只对 C# 模块生效）。

### M4 type-predicates / intersection-union-types / shapeof-pattern-matching ↔ unions 与类型系统（T8）
- C# 现实：C# 15 正做 unions/closed hierarchies/discriminated unions，AOT 驱动「类型系统承担更多职责」。
- 提案响应：VB 的复合类型/流敏感类型谓词与该方向**同向**。
- 考量：**兼容且有机会借鉴**；但注意 C# 的 unions 依赖 `allows ref struct` 等新元数据/特性标志（CompilerFeatureRequired），VB 实现需识别这些元数据才能互操作。

### M5 runtime-library / scripting-interpreted / generative-compiler-scripting ↔ source-gen / AOT（T5, T6）
- C# 现实：编译期 source generators/incremental generators + NativeAOT/trimming；interceptors 服务 AOT 反射难题。
- 提案响应：.vbx 是脚本运行时；runtime-library 提供脚本所需运行库；scripting-interpreted 走解释执行。
- 考量：**最大现实摩擦点**。解释执行/动态生成与 AOT/trimming 天然冲突（反射、DynamicMethod、程序集加载）。VBScript.NET 若要做「脚本 + 现代 .NET」，应默认「编译到受管程序集 + source-gen 桥」，把 interpreted 模式做成显式 opt-in 的传统兼容层。**NativeAOT 落地机制见 D2**（生成 vbproj → 改版编译器编译 dll → .NET SDK `PublishAot`，须确保 `IsAotCompatible`）。

### M6 units-of-measure（inactive）、string-span-utf8（inactive）↔ 数值/字符串底层类型（T2, T3）
- C# 现实：nint/nuint（C# 11）、UTF-8 string literals `u8` → ReadOnlySpan<byte>（C# 11）。
- 考量：string-span-utf8 与 C# u8 直接同向，可实现互操作；units-of-measure C# 无对应（F# 有），是 VB 可保留的特色，但与 CLR 元数据无天然表达，需编译器合成。

### M7 delegate-enhancements / interface-delegation / implicit-interface-implementation ↔ ref struct interfaces / DIM / extensions（T2, T8）
- C# 现实：ref struct interfaces + `allows ref struct`（C# 13）、default interface methods（C# 8）、extensions（C# 14/15）。
- 考量：**方向兼容**。VB 的接口委托/隐式实现需与 DIM、`[UnscopedRef]` 接口成员规则协调。**VB 侧 ref struct 的解法是自定义分析器（决策见 D1）**：VB 规范已有 TypedReference 类受限类型的分析规则（`vblang\spec\types.md`），`{{VBRefStructHelper}}` 已扩展为 BCX 系列错误码实现 ref-safe；但编译器层面尚未 suppress ref struct obsolete error。vbscriptdotnet 应**移植 RefStructHelper 进编译器内部并在编译器层面 suppress 该 obsolete error**，才能消费 C# 13 的 ref struct 接口类型。

### M8 冲突/脱节点 明确提示
- **unsafe 模型分裂**：unsafe-evolution 的 VB 章节明确「VB 不需要 requires-unsafe」（无指针、无 unsafe 上下文）。但 .NET 11 后 C# 指针会更多地在「非 unsafe 上下文」出现、成员会标 requires-unsafe——VB 编译器必须**认识这些新元数据属性**（RequiresUnsafeAttribute/MemorySafetyRulesAttribute），否则无法正确校验「调用 C# requires-unsafe 成员」的安全性。这是**必须桥接**的点。
- **dynamic/晚期绑定 vs AOT**：若 VBScript.NET 愿景包含 NativeAOT，`Any`/晚期绑定将是主要障碍；需明确「脚本层允许动态、编译产物走类型化」的双模路线。落地机制见 M5/D2（生成 vbproj → 改版编译器编译 dll → .NET SDK `PublishAot`，须确保 `IsAotCompatible`）。

---

## 三、决策状态与一致性台账

- 本文件为 VBScript.NET 侧决策的**唯一权威来源**；`csharplang-index.md` 不再承载 M 节。
- 102 篇 meeting 附录中的「索引 Mx」引用已统一改指本文件（`决策文件 Mx`）。
- 旧表述「VB 基本不支持 ref struct（索引 M7）」已在相关会议附录按 D1 修正。
- **D4（优先级判定规则）已记录**（2026-08-09）：P1 = ①C# 已照顾到的非底层内存机制用例，②C# interop 用例（如 consume ref struct）；其余按 LDM 风险评估定级。P1 硬约束（修正） = 不引起**无谓**的回归（不改变合法代码的正确语义）；对 interop，纠正旧错误用法（如 obsolete 类里误用 ref struct → 正确报错）属修错，不算回归、不影响 P1。
