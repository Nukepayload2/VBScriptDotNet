# VBScript.NET 设计决策记录

## 头部

- **本文件用途**：记录 VBScript.NET（.vbx）面对 C#/CLR/.NET 生态现实的设计决策，以及「C# 现实方向 → VBScript.NET 应对」映射。**职责划分**：C# interop 事实（T1–T8、文件索引、引用纪律提示）只写在 `csharplang-index.md`；M1–M8 映射与 VBScript.NET 侧决策只写在本文件。
- **如何使用**：meeting agent 评估提案时，先读本文件「二、M1–M8」定位相关映射与决策；C# 事实（T1–T8、文件索引、引用纪律）见 `csharplang-index.md`；历史会议决策见 `modvb\meetings/`。
- **相关文件**：`csharplang-index.md`（C# interop 事实索引）；`modvb\meetings/`（102 篇 LDM 会议纪要，其「附录：C# 生态与互操作考量」引用本文件 M1–M8）；`vblang\spec\types.md`（VB 受限类型规则）；`{{VBRefStructHelper}}`（VB ref struct 分析器）；`dream-of-vbdev.md`（愿景/意象输入，非决策权威）。

---

## 一、决策修正记录（2026-08-09）

VBScript.NET 侧决策**以本节为权威立场**：`csharplang-index.md`、`modvb\meetings/` 附录或提案中若出现与本节不同的表述，一律以本节为准。

> **引用纪律（强制）**：引用本节的决策一律**按节号**（`D1`–`D9`）并写明小节，**不得按 `文件:行号` 引用**——本文件会持续增补，行号不稳定。`D` 编号按定案先后固定，章节出现顺序不等于编号顺序。**锚点基准（强制）**：文档里的产品码/测试码 `file:line` 一律取**当前工作树**读数，并在该条目上用字段式标注时点（例：「锚点为 `2026-09-24` 工作树读数」）；提交态与工作树态两种基准**不得混用**——合并上游时按「三、合并步骤」统一重定基，重定基属机械动作，不需要逐条请示。历史文档中遗留的行号引用，**以节号为准**解释。

### D1. ref struct 在 VB 的解法 = 自定义分析器（RefStructHelper）

- **VB 规范已有受限类型分析规则**：`System.RuntimeArgumentHandle`、`System.ArgIterator`、`System.TypedReference` 一类受限类型的栈引用限制已写入 `vblang\spec\types.md`。
- **`{{VBRefStructHelper}}` 已把规则扩展为 BCX 系列错误码，实现 ref-safe**：BCX31394（转 Object/ValueType）、BCX31396（Nullable(Of T) / 泛型类型实参）、BCX32061（受限/特殊类型作泛型约束）、BCX36598（LINQ 装箱）、BCX36640（lambda 闭包装箱）、BCX37052（async/iterator 状态机装箱）、BCX31393（继承实例方法装箱）。
- **但编译器层面尚未做到 suppress ref struct obsolete error**。
- **VBScript.NET 做法**：**移植 RefStructHelper 分析器进编译器内部，并在编译器层面 suppress ref struct obsolete error**——这是消费 C# 13 `ref struct` 接口类型（`allows ref struct` 反约束）的前提。
- **结论**：VB 侧 ref-safe 由自定义分析器承担；编译器层面的 suppress ref struct obsolete error 属待移植项。

### D2. vbx NativeAOT 桥接机制

- **AOT 对 dynamic / 晚期绑定不友好是事实**；该取舍**超出实施范围，须由作者决定**。实施侧只按下列机制落地，不自行改判动态语义的存废。
- **落地机制**：**生成 vbproj + 普通 VB 代码 → 用改版 VB 编译器编译 dll → 交给 .NET SDK 对 dll 执行 `PublishAot`**。
- **用户须确保产物 `IsAotCompatible`**；改版 VB 编译器在编译生成的 vbproj 时**已能产生相应警告**。
- **结论**：「双模路线」的编译产物侧即上述 PublishAot 桥；「编译到受管程序集 + source-gen 桥」一律按此机制理解。

### D3. postfix-casting `(As Type)` 语义

- `(As Type)` 是**显式转换**（meeting 已裁定锚定 CType 语义），**不是 TryCast**（`As?` 变体被拒）、**也不是 Option Strict Off 的 callsite 隐式转换**。
- 作用：把动态/Any/晚绑定值显式转成强类型 T，后续 `.Member` 变为早绑定——「默认安全、按需动态」路线的**类型化出口**。

### D5. 基础功能的落地细节以 C# / csi 实现为设计蓝本

- **定案**：vbi 的**基础功能**（脚本/提交模型、构造器、初始化器、入口点、类型落点、名字查找等）在**落地详细设计**阶段，以 **C# / csi 的实现为参考蓝本**；目标是在**架构上让 vbi 追平 csi**，而不是另起一套模型。
- **依据**：csi 由微软官方长期维护，其基础机制经过验证；本 fork 在这些区域与 C# 的分叉处，**下条列举的实例**均属**移植不完整**（各条带 `文件:行号` 锚点）。`Suspect`：未列举的分叉是否也属移植不完整，全量清点未做，采信前须逐处核对。
- **已证实例**（均有 `文件:行号` 锚点，非口号）：
  1. **提交构造器**——C# 把「提交构造器」与「静态初始化器构造器」分成两个类（`SynthesizedSubmissionConstructor : SynthesizedInstanceConstructor`，静态走独立的 `SynthesizedStaticConstructor`）；VB 侧原先共用一个类、共享构造器带上实例版形参（见 `issues\issue-submission-shared-field-initializer-typeload.md`），该按 `isShared` 的分立现落在 `SourceMemberContainerTypeSymbol.vb:2744-2762`（共享走 `EnsureCtor`，实例走 `SynthesizedSubmissionConstructorSymbol`）⇒ 属移植滞后而非有意设计，在册见 `upstream-merge.md` §2.20 F05。
  2. **扩展方法承载**——C# 对「成员必须 `static`」有用户可见诊断 CS1105（`SourceOrdinaryMethodSymbol.cs:243-245`）；VB 侧该处只有 `Debug.Assert(Me.IsShared)`（`SourceMethodSymbol.vb:1505`，早期解码门加 `Me.IsShared` 后已不可达），用户可见诊断 `BC37005`（`ERR_ExtensionMethodNotShared`，`Errors.vb:1636`）现报在完整解码序列 `SourceMethodSymbol.vb:1634-1635` ⇒ 同一分叉类的缺口，在册见 `upstream-merge.md` §2.20 F04。
  3. **脚本里的顶层类型落点**——**脚本模式下两侧其实同形**：C# 的 `CreateScriptRootDeclaration`（`CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:266-303`）与 VB（`VisualBasic\Portable\Declarations\DeclarationTreeBuilder.vb:175-198`）都把非 namespace 的顶层成员（含兄弟类型）塞进脚本类，两侧 `CreateScriptClass` 修饰符也同形。差异在**另一条路径**：C# 的「文件式程序 / 顶层语句」（`SourceCodeKind.Regular` 的 `acceptSimpleProgram`，`CSharp\...\DeclarationTreeBuilder.cs:142` + `:152-156`）把兄弟类型留在命名空间层——**VB 没有这个模式**（全树无 `SimpleProgram`/`TopLevelStatements` 命中，无 `SynthesizedSimpleProgramEntryPointSymbol`）。**故这一条是「C# 有、VB 没有的一个特性」，不是「同一特性两侧建模不同」**；要在 VB 侧对齐属**新特性**，须评估与既有 `.vbx` 语义、`#Load`、`Submission` 链的兼容，不按移植修补对待。
  4. **正向测试覆盖错 kind**——既有 `SourceExtensionMethods` 用 `DeclarationKind.Script`，而产品出货的所有脚本宿主都产 `Submission`；测试覆盖的不是产品走的那条路。
- **落地约束**：
  - 参照 C# 时**要说明「为什么 VB 必须分叉」**，不能因存在分叉就默认放弃对齐（VB 有 C# 无的概念：`Module`、`Shared`、晚期绑定）。
  - 比较结论要落到 `文件:行号`，能实证就实证；「C# 这么做」本身不是理由，**C# 这么做且 VB 没有对应理由**才是。
  - 此原则作用于 **plan 阶段的 `design-detailed.md`**，也作用于 propose/meeting 阶段的候选设计。

### D6. 兼容性约束只对正式版（GA）成立

- **定案**：脚本语义的兼容性约束**只对正式版（GA）成立**。在只有 beta / preview 发行版的期间，**不得以「breaking change / 破坏既有语义」为由否决设计**；beta 期间发出的脚本语义调整不落入 D4 的 P1 硬约束（「不引起无谓的 regression / breaking change」）。
- **实证（只有 beta，无 GA）**：
  - MSIX 清单 `Installer\VBInteractive.WindowsDesktop.Installer\Package.appxmanifest:12` `Version="1.2.0.0"`；`:15` `DisplayName` = `N2Fork VB Interactive (Preview)`、`:34` = `N2Fork VB Interactive (preview)`。
  - `proposals\README.md:78` 逐字「VBScript.NET **1.2 beta** 已发布到微软商店」；`:35` 编译器包版本 `2.0.0-Beta`（同一版本串的出货源：`Installer\Toolset\Nukepayload2.Compilers.VBScriptDotNet.Package.csproj:18` `<Version>2.0.0-Beta</Version>`）。
  - **清点（2026-09-24）**：产品与版本面无 GA / 正式版标记——MSIX 清单标 `1.2.0.0` + `(Preview)`/`(preview)`，Toolset 包为 `2.0.0-Beta`。仓内 `正式版` / `GA` 字样的命中只出现在复述本条的文档、`modvb` 对 C# 15 unions 的讨论与上游 C# 会议原文里，无一处指向本产品的 GA 发行。
- **边界（防止本条被滥用）**：本条**只解掉「兼容性」这一条否决理由**。其它理由——**实现可行性、与 D5 的同形性、机制收益与代价、规范的可表达性**——**不受影响**，仍须逐条论证。举「兼容性」以外的理由否决时，本条不适用。
- **用途**：propose / meeting 阶段的候选裁决与 plan 阶段的取舍，凡出现「这会改掉既有语义」类论据，先对照本条判断该论据是否成立。

### D7. 可移植的脚本方言形状以 C# 脚本方言自动裁决，无人值守推进

- **定案**：凡**理论上能移植到 VB.NET 脚本方言**的语义、限制范围、诊断归属与**测试策略**，一律以**本仓 C# 侧的现行实现为判据自动裁定**，不逐条请示。推进方式：`issues\` 逐条 → 取证 C# 做法（`文件:行`）→ 判可移植 → 立/并任务（`tasks\<slug>\`）→ 实施 → 档 1 验证 → 账本收口，**持续修到清零**。
- **可移植性三问**（全「是」才自动裁决）：① VB 侧存在对应表达，不需要新造语言特性；② C# 侧判据在**本仓树内**可定位（不靠外部文档印象，也不靠"CSI 大概如此"）；③ 分歧不是落在「VB 有而 C# 无」的概念上（`Module`、`Shared`、晚期绑定、`Handles`/`WithEvents` 等）。
- **例外（需上报的只有 (a)/(b) 两类；(c) 不回给人，见下条处置）**：(a) VB 语言层没有对应机制——需新造语言特性才能表达的形状。顶层 `Dim` 的字段类型推断**不属**本例外：C# 只是把已有的 `var` 推断结果用在提升后的字段上（`SourceMemberFieldSymbol.cs:530` 的 `IsScriptClass` 分支），VB 侧对应机制是已作用于局部变量的 `Option Infer`（现状 `SourceMemberFieldSymbol.vb:186-205` 对容器不敏感、一律兜 `Object`）⇒ 按可移植项处理，判据与硬边界（不得照抄 `CS0029` 的硬报错，那会改掉 `Option Strict Off` 的全局语义；改动只对脚本类生效）见 `issues\issue-script-top-level-field-type-inference.md`；(b) 缺陷在 C#/VB **共享**的上游代码里且上游亦未修（目标 `dotnet/roslyn`，本 fork 不修，如 issue 25）；(c) 同一处存在两个以上等价形态而 C# 侧无对应物可判（如提交类成员体内 `MyBase` 究竟按 `Object` 解析还是报无基类）⇒ 判定与处置见下条，**不回给人**。
  - **(c) 的处置（单一判据）**：例外 (c) **不回给人**——由主线选定落点并**留痕**：选了什么、**否掉了什么及为什么**、风险、实施第一步必须钉死的实测项；随后照常排进实施队列。留痕首例见 `tasks\submission-shared-handles-hookup\` README §一。**停手上报的条件只有两个**：① 在本仓树内取不到任何判据；② 设计取向类问题（(a)/(b) 两类）。等价形态的数量、以及「难以用 VB 自洽性排优先级」都**不是**上报理由。
- **分叉纪律（承接 D5）**：与 C# 不一致处必须写出「**为什么 VB 必须分叉**」；写不出理由即按「移植不完整」判为缺陷处理。「C# 这么做」本身不是理由，**C# 这么做且 VB 没有对应理由**才是。
- **测试回收**：为绕开已知缺陷而改写的测试形状，在结论定案后必须回收成**符合该结论的形状**——能用了就钉成合法并取值，按 C# 属预期被拒就钉成"被拒 + 消息指向真原因"，两种都不许留在临时形状上（例：`ScriptModeStatementConformanceTests.vb` 体内两格因 issue 29 从 `Me.ToString()` 换成 `Me.字段`；该条改判后它们要以**预期被拒**的形状回收，而不是悄悄留着）。
- **口径连带**：D7 生效期间，「报出诊断即算覆盖」的判定必须配一个正向对照（同形状在合法容器里跑通并断具体值），否则该格记 `缺口`（缺陷登记见 `issues\README.md` 28 / 29 / 30 各行）。
- **冲突裁定**：当"C# 脚本模式的**实际**策略"与任何一方的直观预期冲突时，**以 C# 的实际策略为准**——包括推翻本条上文或更早的裁定。此时 VB 侧与 C# 同形的行为是**预期行为**，不得判为缺陷。
  - **实例**：提交类成员体内 `MyBase` 取不到基类成员。C# 在 `Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57` 用注释点名「Returns null for a submission class. This ensures that a submission class does not inherit methods such as ToString or GetHashCode」，并以 `Compilers\CSharp\Test\Symbol\Symbols\ImplicitClassTests.cs:63` 断言**非提交**脚本类 `BaseType()` 亦为 `null`、`:76` 断言裸 `ToString` 解析不到符号。故 **`MyBase` 取到 `Nothing` 是预期行为**；「`MyBase` 应指向 `System.Object`」不成立，据其落地的一切兜底均不作实施依据（登记见 `issues\issue-script-class-explicit-me-in-member-bodies.md`、`issues\issue-submission-member-inherited-object-lookup.md`）。
  - **反噬效应**：以 C# 实测为准时，分叉可能在**反方向**。同一例里 VB 的真分歧不是"提交类查不到 `Object` 成员"，而是 `Compilers\VisualBasic\Portable\Symbols\Source\ImplicitNamedTypeSymbol.vb:59` 按 `TypeKind.Submission` 判、C# 按 `IsScriptClass` 判 ⇒ **VB 的非交互脚本类反而比 C# 多继承了 `Object` 成员**。修方向随之反转。
  - **前置纪律**：引用"C# 的实际策略"必须先在**本仓树内**取证（源码 + C# 自己的测试断言），取到才裁；**树内取不到任何判据**即停手上报（这是唯一的停手条件，见上「(c) 的处置」）。不得用"C# 应该会让 `this.ToString()` 可用"这类直觉当判据。
  - **第二个实例**：「顶层 `Sub`/`Function`/`Property` **成员体内**允许显式 `Me`/`MyClass`/`MyBase`」这条收窄（issue 28）与 C# 相反——C# 的 `Binder\Binder_Expressions.cs:55-73` 在脚本类里**一律拒显式 `this`/`base`，成员体也算**（CS0027 / CS1512）。**锚点纪律**：判据以 `HasThis` 源码 + csi 实跑读数（下条）为准；本仓 `Compilers\CSharp\Test\Semantic\Semantics\SemanticErrorTests.cs:1365` 的 `this.goo(); // 5` **只作旁证**——同段 `:1381` 的 `this.goo(); // OK` 处在注释块内，未逐行核对前不得当断言锚。⇒ 该收窄按 C# 回退，回退幅度见下条「部分回退」；被放弃的收益（被局部遮蔽的顶层字段失去 `Me.` 逃生口）属**已明示接受的代价**。登记见 `issues\issue-script-class-explicit-me-in-member-bodies.md` 的「撤销改判」条与 `tasks\script-class-explicit-keyword-parity-revert\`。
  - **C# 侧证据（已运行）**：`Microsoft (R) Visual C# 交互窗口编译器 5.10.0-1.26380.3`（从 `CSharpInteractive.rsp` 加载上下文）下输入 `void test() { this.ToString(); }` → 逐字 `(1,15): error CS0027: 关键字 "this" 不可在静态属性、静态方法或静态字段初始化值设定内使用`；读数在册于 `tasks\script-class-explicit-keyword-parity-revert\README.md`（**2026-09-23 读数，未经主线复跑**）。⇒ 定性：**错在记录面而不是代码面**——VB 原判据（按容纳类型划界）本就与 C# 同形，需要修的是记录面（spec 措辞、队列 #1 的三问②"是"、issue 28 的"过宽属缺陷"推断），修复线因此是**回退 + 改记**，而不是继续在错判据上加固。（**待复核一处**：该读数的中文文案与"静态上下文"措辞相同，而本仓资源把 `ERR_ThisInBadContext`(=CS0027) 定为 "Keyword 'this' is not available in the current context"、静态措辞属 `ERR_ThisInStaticMeth`(=CS0026)（`CSharpResources.resx`、`ErrorCode.cs:37-38`）⇒ 码与文案的配对要么随 csi 版本不同、要么那次命中的其实是静态分支（脚本类的顶层方法是否被编成 `static` 待查）。**两种情形都不动摇结论**：显式 `this` 在脚本类成员里被拒。）
  - **判据次序决定「部分回退」**：C# 的 `HasThis` 把**静态检查放在脚本门之前**（`:45-49`：`memberOpt?.IsStatic == true` ⇒ `inStaticContext = true`、`return false`，调用方据此选 CS0026/CS1511；`:64-73` 才是脚本门的 CS0027/CS1512）。⇒ 与 C# 同形的 VB 判据是：**`Shared` 顶层成员里的显式关键字给普通共享诊断 BC30043（隐式给 BC30369），实例成员体里的显式关键字给 BC36966**——前者正是 issue 28 的 F01 已落地的 C1/C3 格子，**不回退**；只回退"实例成员体放行"这一条。
- **与 D4 的分工**：D4 管「要不要做、排哪一档」，D7 管「做成什么样」。D7 不改变优先级判定，只免除逐条设计请示。

### D8. 发现的问题必须先实锤，推测不得作为实施前提

- **定案**：凡「发现的问题」——缺陷判读、根因、"某行为属预期"、修复方向——**必须先用可复现的实测读数实锤**，才能充当任何后续动作的前提（登记 `issues\`、立 `tasks\<slug>\`、裁决、写 spec 结论位、改判据）。未实锤的写法只有一种：**标成推测，并写清"要把它变成实锤需要跑什么"**。不许用"读码看起来如此"顶替读数。
- **理由（为什么代价不对称）**：错误前提沿「登记 → 立任务 → 实施 → 收口 → 出货」这条链逐级放大，纠正它的最坏形态是**把已经放出去的错误行为以 breaking change 的方式回修**。D6 只解掉 beta 期的「兼容性」否决，GA 之后不再解；因此前置实锤比事后回修便宜。
- **档位（证据阶梯，与 `HANDOFF.md` §2 第 5 条的 ✔／◇／✎ 同口径）**：**已运行**（可复现读数，含探针源码与输出）＞ **已检查**（本仓树内 `文件:行` + 该处自己的测试断言，逐行核对过）＞ **推测**。读码只能定「机制存在与否」，定不了「实际报什么码、报在哪、是否真的报」。
- **对照类判据的连带要求**：判据涉及 C# 侧现行策略时，**两侧都要到「已运行」**。本仓树内有 C# 编译器本体的 `Compilers\CSharp\Portable\**`、`Errors\ErrorCode.cs`、`Errors\ErrorFacts.cs` 与 C# 自己的测试，但**没有交互式宿主 csi 的源码**（`Compilers\CSharp\` 下只有 `csc`，本 fork 裁掉了交互式工具）；csi 源码在册于**仓外**的上游检出 `{{Roslyn}}`（本机值见 `portal.local.md`，机器本地路径不入仓文本）的 `src\Interactive\csi`，可运行的 csi 二进制与己方宿主一起记在 `HANDOFF.md` §6.1。⇒ 要机制去读上游源码就行，**要"实际报什么码、是否真的报"必须回到 §6.1 跑二进制**；只取源码那一侧最多得到"疑似"，得不到"实锤"——本条的两条实例正是这样定案的：
  - 方法体内 `Dim a = b`（`b` 尚未声明）：推测报 `BC30451`，`vbi` 实跑报 **`BC32000`**，且 `Option Explicit` 两档同判 ⇒ 猜错的码若进了判据，就直接变成一条错断言。
  - 脚本顶层 `Dim a = b` / `Dim b = a`（互指）：读码只给出**疑似**分叉（C# 侧确有 `ERR_RecursivelyTypedVariable` 分支、VB 侧守卫静默退回 `Object`），而按 D7 自己的口径「判分叉前必须先要一次 csi 读数」，那时它**还不能登记成缺陷**。两侧实跑后定案：csi `(1,5) error CS7019`（报错）vs VB **编译期零诊断 + 运行期 NRE** ⇒ 分叉成立且 C# 更硬；同一批里非循环的前向引用（`a = b` / `b = 5`）两侧读数逐字相同（`Int32`／`0`）⇒ 实测同时划清了"哪一格真分叉、哪一格已对齐"，这是读码给不出的分辨率。
- **本仓已付过学费的实例**：issue 28 的原判据（"C# 允许脚本类成员体内用 `this`"）**前提为假**，据其落地的 F01–F03 整批作废为回退输入；issue 29 的原症状在产品两条路径**都不复现**，唯一"证据"是一次内存编译的临时用例；issue 21 初判的"顶层容器特有缺陷"被实测推翻；`HANDOFF.md` §4.6 自查出一条根本不存在的"已落地"。
- **边界**：① 实锤只决定「能不能当前提」，**不**决定「要不要修、排哪档」——后者仍归 D4 与 D7；② 不要求纯措辞的文档改动跑测试，那一面按 §2 第 4 条「写『已落地』要 grep 得到」把关；③ 实测入口、编码与 Git Bash 的坑（`MSYS_NO_PATHCONV`、诊断正文是机内 ANSI 码页需按 CP936 解、循环类探针必须同树成文件而非逐条 REPL 提交）在册于 `HANDOFF.md` §6.1，缺了这一段会让读数看起来"没有输出"。

### D4. 提案优先级判定规则

- **P1 两档直接判入**：
  - **C# 已照顾到的、非底层内存机制相关用例** → P1（与 C# 生态同向，风险最低）；
  - **C# interop 用例**（如 **consume ref struct**）→ P1。
- **其余提案**：按 LDM 风险评估（三态判定 + 复杂度/成本/优先级、正文 LDM 追问清单）决定是 P1 还是更低优先级。
- **P1 硬约束**：P1 提案在**设计上不引起无谓的 regression / breaking change**——即不改变「合法既有代码的正确语义」。**对 C# interop，破坏性变化有时不可避**：例如旧代码在 obsolete 类里误用 ref struct（运行期本会 `InvalidProgramException`），ModVB 体系下正确报错是**修错而非回归**，不影响其 P1 地位。
- **用途**：本规则是排优先级工作的判定闸门，与 M1–M8 映射配套使用（命中 P1 两档的提案优先进入实现路线图）。

### D9. 脚本方言的对标基准；C# 侧取「意图」而非「实现」

- **定案**：VBScriptDotNet 脚本方言（.vbx）的基准是**「VB.NET 的语法 ＋ C# Interactive 的特性」**。凡 VB.NET 语法侧无对应物、而落在脚本／交互特有机制上的行为，以 C# Interactive 为设计参照；**该参照取 C# 的「意图」，不取 C# 的「实现」**。
- **「实现」不予采信的三条判据（满足其一即不采信）**：
  1. **行为随入口而变**——同一份文件、同一段代码，换一个入口（进／出口文件、宿主、平台路径处理）就得到不同结果；
  2. **诊断指向假原因**——报出的错误码或文案与真实成因不符（典型：把「确实存在」报成「找不到」）；
  3. **可证明是漏洞而非契约**——同一形状在一处跑通、换入口即崩，且两侧都未声明过该契约。
- **与 D7 的分工**：D7 说「以本仓 C# 侧现行实现为判据自动裁决」，本条**收窄**它：三问（VB 侧有对应表达／判据在树内可定位／分歧不落在 VB 专有概念上）**仍要逐条过**；三问全过之后，对标**取 C# 的意图**——实测行为若命中上面三条，判为 C# 侧缺陷，**既不作为照抄的依据，也不作为否决他人设计的依据**。
- **与 D5 的分工**：采信 C# 意图时**仍要写正面论证**：「C# 想达到什么效果、它的实现为什么没达到、VB 用什么手段达到」。本条**不豁免** D5 的「为什么 VB 必须分叉」义务。
- **首个实例（issue 34 / `#Load` 去重，2026-09-28 定案）**：
  - **事实（已运行，2026-09-28 读数；探针留档 `tmp\probes-cycle-sem\`）**：C# 的去重表 `loadedSyntaxTreeMapBuilder` **从不预置入口文件的路径**——判定门在 `Compilers\CSharp\Portable\Compilation\SyntaxAndDeclarationManager.cs:236`，而 `:247` 的 `Add` 排在递归**返回之后**，`:270-275` 的 else 分支静默跳过、零诊断。⇒ `#load` 链一旦绕回**入口文件**即去重失效：`cyc-main.csx` 作入口加载 `cyc-a.csx` → exit 0；**把 `cyc-a.csx` 自身作入口** → `CS0102`（类型中已含"z"的定义）＋ `CS0229` ×2、exit 1。**同一形状，行为随入口而变** ⇒ 命中上面第 1 条。
  - **裁决**：脚本方言取 **once 语义**——一次编译内同一文件只展开一次，静默跳过、零诊断；**成环不是错误，是「已见过 ⇒ 跳过」**，与菱形共用同一条规则。编译器路径与 NuGet 预扫描路径**各自构造集合并各自预置入口文件路径**。旧祖先栈机制（`activeLoads`）连同它对 `BC2001`（找不到文件）的复用**整体删除**——按本节「误导性诊断该消失而不是换码」的取法（诊断与真实成因不符时，优先**删掉**该诊断，而不是另换一个码），`BC2001` 自此只剩「真的解析不到」一种用途。
  - **锚点为 2026-09-28 工作树读数**；改动面登记在 `upstream-merge.md`，结论位写进 `spec-scripting-dialect.md`（中英两份）的 `#Load` 一节。
- **边界**：本条**只**管「对标 C# 时采信什么」。它**不**放宽 D5 的分叉论证义务、**不**豁免 D8 的实锤要求、**不**改动 D4 的优先级判定、**不**授予「凡 C# 如此即免于请示」的权限；D7 三问与 D8 档位标注仍逐条适用。

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

- **本文件**＝VBScript.NET 侧决策的唯一权威来源；C# interop 事实（T1–T8、文件索引、引用纪律提示）见 `csharplang-index.md`。
- **M1–M8 引用**：`modvb\meetings/` 附录中的「决策文件 Mx」指向本文件 §二；映射条目不另立判据。
- **ref struct 面**：以 D1 为准。
- **优先级判定**：以 D4 为准（P1 两档、P1 硬约束、interop 修错非回归）。
- **各决策的落地状态**：不在本节维护——逐条见 `issues\README.md` 与 `tasks\<slug>\`；编译器改动面见 `upstream-merge.md`；版本归档见 `spec\README.md`。
