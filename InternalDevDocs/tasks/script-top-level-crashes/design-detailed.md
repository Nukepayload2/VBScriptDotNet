# 详细设计：脚本顶层崩溃族一次收口（script-top-level-crashes）

> 状态：详细设计（改动蓝图 + 裁决规则 + pass 条件）。依据：`design-overview.md`（总体设计 / 判定表 / 分批）→ 本文件 → `test-plan.md`。
> 路径相对仓库根；编译器前缀统一 `Compilers\VisualBasic\Portable\`（简写 `VB\`），共享编译器前缀 `Compilers\Core\Portable\`（简写 `Core\`）。
> 三态：锚点行均**已检查**（本代理逐条 Read `文件:行号`）；症状均**已运行**（`README.md` §六 探针）；推断项按 `manifest.md` 标「推测 / 猜测」。失败点缓存 `<项目根>/tmp/vortex-logs/top-level-implicit-shared/pitfalls.md`（P-001…P-038）。

## §0 通用约定

1. **测试执行**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**禁用 `dotnet test`**（EXIT 0 但静默不跑）；先 `dotnet build`，再直跑程序集 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）/ `-class <FQN>` / `-method <FQN>`（FQN 须完整到 `命名空间.类.方法`；缺段或拼错**静默 0 跑**，须核对 `TestCasesToRun > 0`）。编译器侧走七门 gate `powershell -File scripts\verify-vb-compiler-tests.ps1`。
2. **无副作用纪律**：单测禁网络 / 文件写入 / 进程启动 / 注册表写入。全部用例为内存 I/O（`VisualBasicScript.Create/RunAsync/Compile` + 内存 `StringReader`/`StringWriter`；编译器侧 `VerifyDiagnostics` 全内存）。`vbi.exe` 直跑探针**只作本阶段的取证手段，不作验收用例**。
3. **变更面纪律**：`Core\CodeGen\`（`ILBuilder` / `BasicBlock` / `EmitExpression` 所在层）**零改动**；容器种类零改动；`PublicAPI.*.txt` 零增量；`spec\` / `meetings\` / `proposals\` / `issues\` 文本零改动（改动作为义务记在 §账本与规范义务）。
4. **同文件串行**：`Symbols\Source\SourceMemberContainerTypeSymbol.vb`（F05 + F11）、`Binding\Binder_Expressions.vb`（F06 + F08）、`Errors.vb`+`ErrorFacts.vb`+`VBResources.resx`+13 `xlf`（F04 + F06）**不得**跨单元并行落地。
5. **每条 pass 断言必须有「反向对照」**：凡「让 X 工作」的单元必须同时钉住「相邻的 Y 不变」（如 F08：「实例成员隐式访问仍合法」；F11：「无 `Await` 与有 `Await` 都要过」；F10：「`Try` 体内与 `Using` 内的 `Await` 仍合法」）。

---

## §F04 `<Extension>` 施加于脚本类实例成员 → 新诊断（判定：**报错**）

### 目标

顶层 `<Extension>` 漏写 `Shared` 时给出一条**能定位到行的用户诊断**，取代 `Debug.Assert(Me.IsShared)`（`VB\Symbols\Source\SourceMethodSymbol.vb:1504`）。

### 上游前置决定（不推翻）

`meeting-script-extension-methods.md` **R2** 采纳候选 B：「维持『脚本类里只有 `Shared` 成员可作扩展方法』，补齐缺失诊断」，两处同批落地：①早期解码侧给 `:1500-1502` 的条件补 `Me.IsShared` 守卫（**消除 `:1504` 断言可达性**）；②完整解码侧在 `:1624-1648` 的序列里补分支。诊断**必须点名 `Shared`**，且按 R2 的「复会加强」承担叙事职责（讲出「标准模块的成员隐式 `Shared`，脚本顶层容器不是标准模块，所以这里的 `Shared` 是必需而不是非法」）。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMethodSymbol.vb:1500-1513` | 早期解码的三重条件（`MethodKind` ∈ {Ordinary, DeclareMethod} ∧ `AllowsExtensionMethods()` ∧ `ParameterCount <> 0`）里补 `Me.IsShared`；不满足时**不进入** `isExtensionMethod` 计算（保持 `False`），从而 `:1504` 断言不可达 |
| ② | `VB\Symbols\Source\SourceMethodSymbol.vb:1624-1648` | 在既有分支序列里补一支：`ElseIf Not Me.IsShared Then` → 报**新诊断**（消息点名 `Shared`）。**位置**：插在 `:1633` 的 `Debug.Assert(Me.IsShared)` **之前**（否则断言先炸）；与「`:1630` `ParameterCount = 0`」「首参 Optional/ParamArray/泛型约束」的相对次序**待裁**（见 §裁决登记 D1） |
| ③ | `VB\Errors\Errors.vb`（新码位）、`VB\Errors\ErrorFacts.vb`、`VB\VBResources.resx`、`VB\xlf\*.xlf`（13 份） | 新增一枚错误码 + 文案 + 分类 + 本地化全链 |

### 不变量（一个字都不许变）

- `<Extension> Shared Function` 的正向路径（`ExtensionMethodTests.vb:2425-2440` 的 `ScriptExtensionMethods` 形状）零改动通过。
- 既有的 BC36550 / BC36551 / BC36552 / BC36548 / BC36554 的诊断**序列语义**不变：嵌套容器仍走 BC36551（或被 R6 的候选 F 接管，见下），无参仍 BC36552，首参 Optional/ParamArray 仍 BC36548/BC36554。
- **普通 `Module` 与普通 `Class` 编译零行为变化**（`Module` 成员隐式 `Shared` ⇒ 新分支不可达；普通 `Class` 不满足 `AllowsExtensionMethods()` ⇒ 在 ② 的第 2 支就被拦下）。
- **不阻塞**候选 F（嵌套容器承载扩展方法，`meeting-script-extension-methods.md` R6）：新分支与 F 落在同一段序列，F04 的插入点不得把嵌套判据挤到不可达位置。

### 陷阱

- `SourceMethodSymbol.vb:1621-1648` 是**完整解码**序列，`attrData.IsTargetAttribute(...)` 进门后**只报错、不产出识别结果**（注释 `:1622` 逐字「Just report errors here. The extension attribute is decoded early.」）⇒ ② 只影响诊断，不影响扩展方法的识别；真正的识别在 ①。
- ① 若把 `Me.IsShared` 加进条件而不是**前置守卫**，会顺带改掉 `isExtensionMethod` 的取值语义——**必须**只在 `Shared` 为真时进入原逻辑，不改变原逻辑的其它入口条件。

### pass 条件

见 `README.md` §七 `F04` 行。核心两句：**漏 `Shared` → 新诊断且消息含 `Shared`**；**`<Extension> Shared Function` 正向零改动通过**。

---

## §F05 submission 共享分支改用无参共享构造器符号（判定：**修好**，甲）

### 目标

顶层 `Shared` 字段/属性带初始化器（含 `Shared ReadOnly`、含 `Shared Dim arr(2)` 的隐式上界）正常工作，不再 `TypeLoadException`。

### 上游前置决定（不推翻）

`meeting-submission-shared-members.md` **R1**：`TypeKind = Submission` 分支按 `isShared` 分叉——**共享时走同文件的 `EnsureCtor`**（`VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2771-2802`，`:2800` `New SynthesizedConstructorSymbol`，形参恒空），**实例时仍走 `SynthesizedSubmissionConstructorSymbol`**。形态是**两个符号**（issue 05 候选 B），**不是**「一个符号、形参表分叉」（候选 A）——依据 D5 已证实例 1（C# 的静态构造器是另一个类）。R5：甲与乙**同批设计、独立验收**，甲先落地止血。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737` | 现有 `If Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(initializers, False) Then New SynthesizedSubmissionConstructorSymbol(...)` 改造为：`isShared` 时改调 `EnsureCtor(...)`（同文件 `:2771-2802`，其判据是 `Members` 里是否已有 `StaticConstructorName`，键在 `:2773`）；`Not isShared` 时**逐字保持现状** |
| ② | （待实现期实证）与 `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:3195-3214` 的交互 | 两套机制都按 `.cctor` 名查重（本处 vs `:3201`）⇒ **plan 前置实证项**：同文件同时含 `Const d As Date = #…`（走 `CreateSharedConstructorsForConstFieldsIfRequired`）与 `Shared x As Integer = 5`（走新分支），确认两者落到**同一个** `.cctor` 且发射形状正确 |

### 不变量

- `GetScriptConstructor()`（`VB\Symbols\NamedTypeSymbol.vb:697-700`，`InstanceConstructors.Single()`）不受影响——共享构造器不进 `InstanceConstructors`（`VB\Symbols\MethodSymbol.vb:517-521` 的 `IsScriptConstructor` 要求 `MethodKind = Constructor`）。
- **非共享**实例初始化器路径（`<Initialize>` + 匿名提交构造器）**逐字节不变**。
- 共享初始化器的**执行时机与语义**不变：仍在（隐式）共享构造器里、仍是类型级、「先于首次访问该静态字段」（`InternalDevDocs\vblang\spec\type-members.md:1269` / `:2021` 的模型；参 pitfalls **P-001 / P-002 / P-003**）。**不得**借本单元把共享初始化器搬进 `<Initialize>`（`meeting-submission-shared-members.md` R3 已否决「丙」，附复活条件）。
- `Shared ReadOnly` 仍在共享构造器里赋值（`VB\CodeGen\EmitAddress.vb:261-284` 的 `HasHome` 要求 `MethodKind.SharedConstructor`）——甲 之下自然成立，**不引入不可验证 IL**（R4）。
- `beforefieldinit` 不改（甲 仍产 `.cctor`）。**注意 P-001**：VB 主动打该标志（`VB\Emit\NamedTypeSymbolAdapter.vb:496-499`）。

### 陷阱

- **P-010**：两个构造器生产者（`AddDefaultConstructorIfNeeded` 的提交分支 vs `CreateSharedConstructorsForConstFieldsIfRequired`）谁生效取决于 `Members` 里是否已有 `StaticConstructorName`——新分支必须复用 `EnsureCtor` 的**同一查重键**，不要另造。
- **P-011 / P-013**：不要动「初始化器归属/桶」；`<Initialize>` 走 `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:3243` 的脚本类快路径，`:3276` 的 `Throw ExceptionUtilities.Unreachable` 不可达——任何「改桶」的想法都在 R3 的否决范围内。
- `Shared Dim arr(2) As Integer`（无 `=`，只有数组上界）**也在触发面内**（issue 05 与会议已实测 exit 34）：触发条件是「**静态桶里有需要注入构造器的条目**」，不是「写了 `=`」。（issue 05 的边界表把条件写成「有初始化器」——**口径按会议**。）

### pass 条件

见 `README.md` §七 `F05` 行。核心：三种形状不再 `TypeLoadException`、共享初始化器在共享构造器里执行、**同文件 `Const` + `Shared` 字段落到同一个 `.cctor`**、非共享路径逐字不变。

---

## §F06 共享字段/属性初始化器里的 `Await` → 新脚本专属码（判定：**报错**，乙）

### 目标

把「共享字段/属性初始化器含 `Await`」从**断言终止**变成**一条能定位到行的诊断**（`meeting-submission-shared-members.md` R2 明定目标为「从崩改成报错」，**不是**「支持 `Await`」）。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Expressions.vb:4734-4749`（**调用结构**） | 在 `:4740-4741` 的 query 分支与 `:4742-4743` 的 `ElseIf Not IsInAsyncContext()` 之后**补一个子情形判据**：共享（`IsShared`）字段/属性的初始化器里出现 `Await` → 报**新码**。**硬事实（已检查）**：走 `GetAwaitInNonAsyncError()`（`:5055-5069`）里加 `case` 这条路**不可达**——共享字段时 `IsInAsyncContext()`（`:4726-4727`）为真 ⇒ `:4743` 根本不执行 |
| ② | 同上 | 判据必须覆盖**共享属性**：VB 的属性初始化器 `ContainingMember` 是 `PropertySymbol`（不是 backing field）⇒ **不能照抄 C# 的 `case SymbolKind.Field`**（C# 判据见 `Binder_Await.cs:152-213`；C# 之所以也覆盖静态属性是因为它把属性初始化器登记在 backing field 上） |
| ③ | `Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` / 13 `xlf` | 新码全链。**硬条件（R2）**：必须**新码**，不复用 `ERR_BadAwaitNotInAsyncMethodOrLambda = 36937`（消息不对症）。消息要点名「共享/静态初始化器在执行时是同步的共享构造器」 |

### 判据覆盖的子情形清单（R2 的 TODO，逐条进验收）

1. 共享**字段**初始化器含 `Await`（`Shared Dim x = Await Task.FromResult(1)`，issue 06 原形状）；
2. 共享 `ReadOnly` **字段**初始化器含 `Await`；
3. 共享**属性**初始化器含 `Await`（会议已实测 exit 35，与共享字段同一承重点）；
4. 共享**数组上界**隐式初始化器路径（`VB\Binding\Binder_Initializers.vb:234-260` 一带的 `BindArrayFieldImplicitInitializer`；该路径上能否出现 `Await` 需实现期实测判定——**若不可表达则显式记录为不可达**，不留悬空项）。

### 不变量

- **实例**（非共享）字段/属性初始化器里的 `Await` **仍合法**（`VB\Binding\Binder_Expressions.vb:4726-4727` 的原承诺面 + 顶层实例形状实测 exit 0）——**回归必测**。
- **嵌套类型**的字段/属性初始化器 `Await` 仍走 BC36937（属 F09 的面，**不得**被本单元的新判据截胡）。
- `Try` 体内 / `Using` 内的 `Await` 与本单元无关（F10 的面）。
- 判据必须只在**初始化器**这一上下文成立，不能扩到方法体（`SymbolKind.Method` 分支 `:4722-4723` 保持原样）。

### 陷阱

- `IsInAsyncContext()` 另有 10 处以上消费者（`Binder_Statements.vb` / `Binder_Lambda.vb` 的多处）——**不要**走「让 `IsInAsyncContext()` 对共享字段返回 False」这条低成本子形态（`meeting-submission-shared-members.md` R2 已给出理由：消息不对症 + 消费者扩面未清点）。本单元**只改调用结构**。
- `IsShared` 的读取点：新判据要按**初始化器所属符号**的 `IsShared`（字段符号 / 属性符号），**不要**按引用点所在成员（pitfalls **P-009**：`Await` 看 `IsScriptClass`、隐式 Me 看 `IsShared`、`ReadOnly` lvalue 看 `KindOfContainingMethodAtRunTime()+IsShared`，三套规则的键会分裂；pitfalls **P-014**）。
- 依赖 **F05（甲）已落地**：甲 之前共享初始化器进的是带参 `.cctor`（issue 05 先崩），新诊断会被 issue 05 的症状掩盖，验收会误判。

### pass 条件

见 `README.md` §七 `F06` 行。核心：四条子情形全部报新码、实例初始化器仍合法、嵌套类型仍 BC36937、新码全链完整、位置落在 `Await` 关键字。

---

## §F07 三处合成成员断言收窄（判定：**修好**）

### 目标

提交类顶层 `Event` / `WithEvents`（实例与 `Shared` 各一）正常工作，不再断言终止。

### 判定依据（为什么「收窄断言」而不是「改 `IsImplicitlyDeclared`」）

- 三处断言的**位置**证明它不是控制流：`VB\Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:61-78` 的 `MyBase.AddSynthesizedAttributes(...)`（`:62`）→ 断言（`:66`）→ **无条件**加三个合成特性（`:68-77`）；`VB\Symbols\Source\SynthesizedEventAccessorSymbol.vb:492-506` 同形（断言 `:495` → 无条件加 `CompilerGenerated` `:497-498`）。⇒ Release 下断言编译掉后**同一条路径**照常执行（`e307d0f` 实测顶层 `Event` exit 0 `EVENT-OK`）⇒ **收窄断言零行为变化**。
- 路线 B（把 `VB\Symbols\Source\ImplicitNamedTypeSymbol.vb:33-37` 的 `IsImplicitlyDeclared` 从 `IsImplicitClass OrElse IsScriptClass` 收窄到 `IsImplicitClass`）会把改动面扩散到该谓词的**全部消费者**（issue 07 正文自陈「影响面未清点」；同族 grep 命中 6 条里另有 3 条属别的话题），而不是问题所在。⇒ **不取**。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SynthesizedEventAccessorSymbol.vb:495` | `Debug.Assert(Not ContainingType.IsImplicitlyDeclared)` → `Debug.Assert(Not ContainingType.IsImplicitClass)` |
| ② | `VB\Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:66` | 同形（`Not Me.ContainingType.IsImplicitClass`） |
| ③ | `VB\Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93` | 同族第三处，**一并收口**（无遗留问题纪律）——实现期先逐行确认该断言的上下文同样不是控制流 |

### 不变量

- 真正的 `ImplicitClass`（非脚本、非提交的隐式类）容器上的原语义**保住**（断言仍生效）。
- `Event` / `WithEvents` 在**嵌套类型**里的行为逐字不变（实测 q34/q35 exit 0）。
- Release 发射结果不变（断言本就编译掉）。
- 不改 `IsImplicitlyDeclared` 本身 ⇒ 其余消费者（`Symbol.GetCustomAttributesToEmit`、`MethodCompiler.vb:1841` 等）行为不变。

### 陷阱

- 断言去掉后，发射路径要**真的产出正确结果**——不能只满足「不崩」。验收必须断言**语义**：`Event` 的 add/remove 访问器可用（`AddHandler` + `RaiseEvent` 计数）、`WithEvents` 的钩子生效（handler 被调用）。`e307d0f` 的 `EVENT-OK` 只证明「不崩」，**不证明语义正确**（**推测**：语义正确，未验；实现期必须实测）。
- `IsImplicitClass` 的属性名与语义须在实现期确认（`VB\Symbols\Source\ImplicitNamedTypeSymbol.vb:35` 已在用）；若不可从 `NamedTypeSymbol` 直接访问，改用等价的 `KnownTypeSymbol` / `DeclarationKind` 判据（**待实现期确认**，见 §裁决登记 D2）。

### pass 条件

见 `README.md` §七 `F07` 行。核心：两种成员 × （实例/`Shared`）四种探针**不再终止**且**语义正确**；嵌套类型不变；真隐式类的断言仍生效。

---

## §F08 脚本类共享成员的隐式 `Me` → BC30369（判定：**报错**）

### 目标

共享成员体 / 共享初始化器里隐式引用实例成员 → 报 **BC30369**（与普通类同码同形），取代「零诊断 + 运行期 `InvalidProgramException`」。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Expressions.vb:2262-2270` | 脚本类分支里把「隐式 ⇒ 一律 `Return True`」改为：**隐式**引用先查 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2235-2255`，同一 `Binder` 内已存在），命中共享上下文则照常落 `:2272-2278` 的 `ERR_BadInstanceMemberAccess`（BC30369）；**不命中仍 `Return True`**（`spec:243` / `spec:258` 的隐式访问承诺） |
| ② | 同上 | **显式**引用路径**逐字保持**：`errorId = ERRID.ERR_KeywordNotAllowedInScript`（BC36966，`:2267`） |

### 判据形状（实现期按此三条自检）

1. 脚本类 + **显式** `Me`/`MyBase`/`MyClass` → BC36966（不变）。
2. 脚本类 + **隐式** 引用 + **实例**成员（`containingMember` 非共享、`ContainingType` 非模块）→ 合法（`IsMeOrMyBaseOrMyClassInSharedContext()` 返回 False）。
3. 脚本类 + **隐式** 引用 + **共享**成员体/共享初始化器 → BC30369。

### 不变量（回归必须双向锁死）

- **实例方法体里隐式读顶层 `Dim` 仍然合法**（`spec:243`；issue 08 正文点名为必锁回归项）。
- 显式 `Me` 仍 BC36966（`spec:231-239` 的诊断族不变）。
- 顶层**语句**（`<Initialize>` 体）里的隐式引用：`ContainingMember` 是 `<Initialize>`（`IsShared = False`）⇒ 按第 2 条走 ⇒ **合法**（这是顶层语句读顶层 `Dim` 的既有能力，必须保住）。
- 普通类 / 嵌套类路径完全不变（BC30369 的既有行为不动）。
- 无新错误码（复用 `VB\Errors\Errors.vb:323` 的 `ERR_BadInstanceMemberAccess`）。

### 陷阱

- `IsMeOrMyBaseOrMyClassInSharedContext()` 的 Field 分支（`:2248-2251`）含 `DirectCast(containingMember, FieldSymbol).IsConst`；Property 分支（`:2244-2246`）不含 `IsConst`。**共享属性的初始化器**形状（`Shared ReadOnly Property P = F()`）判据落在 Property 分支——须实测确认它命中 BC30369（**推测**：命中，会议已实测共享属性初始化器 exit 34 属 issue 05 承重点，BC30369 面未单独实测）。
- 本单元与 F06 同文件（`Binder_Expressions.vb`）：`:2257-2286` 与 `:4740-4744` 两区不重叠，但**必须串行落地**以免 rebase。
- 与 F05（甲）的交互：`Shared y = F()` 今天先撞 issue 05；甲 之后才会露出本单元的判据 ⇒ **F08 的「初始化器形状」验收依赖 F05 已落地**（方法体形状不依赖）。

### pass 条件

见 `README.md` §七 `F08` 行。

---

## §F09 初始化器绑定诊断 gate 发射（判定：**修好**）

### 目标

初始化器（字段/属性）绑定中产生的 **error 诊断**必须阻止该类型的发射，取代「诊断报了、仍然发射、发射期断言终止」。

### 判定依据

`VB\CodeGen\EmitExpression.vb:207` 的注释逐字「Code gen should not be invoked if there are errors.」——本单元是把这条**已写下的契约**兑现，不是新增语义。判别性实证 v1–v3（零诊断 + exit 35）vs v4/v5（诊断 + exit 1）把根因锁在「诊断落在哪个袋 + 是否 gate」。

### 修法选型（**已裁：B**）

| 候选 | 形状 | 裁决 |
|---|---|---|
| A | 报错时给 bound 节点标错（`VB\Binding\Binder_Expressions.vb:4742-4744` 之外让 `BoundAwaitOperator` 带 `hasErrors`） | **不取**：只覆盖 `Await` 一条诊断，issue 09 自陈的「派生问题（还有哪些初始化器绑定期诊断落进同一个洞）未清点」会留成遗留问题 |
| **B** | **让发射门看得见初始化器诊断** | **采纳**：一处改动覆盖全部同类情形；复用 `:1288` 已有一项判据（`processedInitializers.HasAnyErrors`），不新增门项 |
| C | `BindingDiagnosticBag.GetInstance(template)` 复制已有诊断（`VB\Binding\BindingDiagnosticBag.vb:50-52`） | **不取**：`GetInstance` 有 100+ 调用点，语义影响面未清点，风险最大（issue 09 自陈） |

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Initializers.vb:19-55` | 给 `ProcessedFieldOrPropertyInitializers` 增一个**「绑定期产生了 error 诊断」入参**（可选参默认 `False`，保持既有调用点与 `Empty` 单例语义），`HasAnyErrors = bindingReportedErrors OrElse boundInitializers.Any(Function(i) i.HasErrors)`。**注意**：`:22-28` 的文档注释必须同步改写——`HasAnyErrors` 也反映「本路绑定期报过 error」（该来源由调用方经入参传入） |
| ② | `VB\Compilation\MethodCompiler.vb:599-623` | 两个桶各自 `BindingDiagnosticBag.GetInstance(_diagnostics)` 得到**本桶独立袋**，把它作为 `diagnostics` 实参传给 `BindFieldAndPropertyInitializers`，紧接着 `_diagnostics.AddRangeAndFree(袋)` 合并回共享袋，并把该袋的 `HasAnyErrors()` 作为 ① 的入参。`HasAnyErrors()` 的语义已核实为**只算 Error 严重度**（`Core\Diagnostic\DiagnosticBag.cs:56-82` 逐行；文档明写不考虑 warning / informational / `warnaserror` 提升）⇒ warning 不会误 gate；每桶只调一次（不进逐方法循环）。**不得**改用「在共享袋上取 `after AndAlso Not before` 增量」这条更省事的子形态：`_diagnostics` 是编译级共享袋且 `ConcurrentBuild` 默认开（`:107`/`:531`/`:1357` 回灌），别的成员先报了错就让 `before` 为真 ⇒ 本路新产生的错被 `Not before` 吞掉 ⇒ **欠 gate**（实测：该口径下「另一类型先报错」的判别性用例会重新崩溃，正是最危险的漏向）。诊断集合与顺序不变（`AddRange` 保序），上游 C# 侧同形 idiom 见 `Compilers\CSharp\Portable\Compiler\MethodCompiler.cs:867`/`:885` |

### 不变量

- `:1288` 的发射门**表达式形状不变**（不新增判据项）；`:1331` 的门不变；`:1554` 的二次门不变。
- **没有初始化器 error 的编译逐字节不变**（含「有 warning 的初始化器」「有既有 error 但与本路无关的编译」——后者本来就要失败）。
- 诊断集合不变（**只影响发射，不影响诊断**）：v1–v3 的 BC36937 照旧报出（这一点由 v4/v5 已证明该诊断存在于结果里）。
- `_hasDeclarationErrors`（`VB\Compilation\MethodCompiler.vb:29`/`:100`，`ReadOnly`）**不改**——本修法走 `ProcessedFieldOrPropertyInitializers` 通道，不动全局字段。
- 静态桶与实例桶**各自独立**判定（`:608-613` / `:617-622` 两次调用各自独立袋判定；不要用一个共用标志，否则会过度 gate）。

### 陷阱

- `HasAnyErrors()` 会 resolve lazy 诊断且是线性扫描 ⇒ 只在**每个类型的初始化器绑定**处调用两次（O(n)），**不要**放进逐方法循环。
- **不要**改用「在共享袋上取前后增量」的口径，理由见本节改动蓝图 ②。
- 顺序敏感：`_diagnostics` 是跨方法共享的（issue 09 自陈）。两桶的绑定与合并必须在**同一线程同一位置**紧邻、静态桶在前实例桶在后，不可跨方法移动；诊断的相对顺序靠 `AddRangeAndFree` 保序维持逐条顺序不变。
- F09 落地后 **F10 / F06 / F08 的「初始化器形状」才拦得住发射**（依赖方向见 `README.md` §四）。

### pass 条件

见 `README.md` §七 `F09` 行。

---

## §F10 脚本顶层补跑 `Catch`/`Finally`/`SyncLock` 内的 `Await` 检查（判定：**报错**）

### 目标

顶层 `Catch` / `Finally` / `SyncLock` 里的 `Await` 报 **BC36943**（与普通 `Async Function` 同码），取代三种后果：`Catch`/`Finally` → NRE（exit 3）、`SyncLock` → **静默产出运行期损坏的 IL**（exit 24，g14 新发现）。

### 根因（一句话）

BC36943 的检查住在 `BindMethodBlock` 的 `CheckOnErrorAndAwaitWalker`（`VB\Binding\Binder_Statements.vb:291`/`:330`/`:602-610`），而脚本 `<Initialize>` 的方法体是空壳（`VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`）⇒ 顶层语句从不过这道 walker。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Statements.vb:454-635` | 给 `CheckOnErrorAndAwaitWalker` 加一个**开关构造参数**（如 `onlyCheckAwaitInTryHandler As Boolean = False`）：为真时 `VisitBlock` 跳过 `:507-513` 的 `ERR_TryAndOnErrorDoNotMix` 报告（`reportedAnError` 不置位），只保留 `:602-610` 的 BC36943 分支。**默认值保持既有行为**，`BindMethodBlock` 的调用点零改动 |
| ② | `VB\Binding\Binder_Statements.vb`（同 `Partial Friend Class Binder`） | 新增一个 `Private Shared` 入口，接收 `binder`/合成块/诊断袋，以 `onlyCheckAwaitInTryHandler:=True` 调 walker（同一 `Binder` 的 partial 文件互相可达 `Private` 成员，**无需**改可见性） |
| ③ | `VB\Binding\Binder_Initializers.vb:102-203` | 在 `BindFieldAndPropertyInitializers` 内、`scriptInitializerOpt IsNot Nothing` 时，对已收集的顶层语句拼一个合成 `BoundBlock`（形状照 `:64-67` 的既有模板）并调 ②。**binder 用 `:122-126` 已构造的 `TopLevelCodeBinder`**（它是 async-aware：以 `<Initialize>` 为 containing member ⇒ `IsInAsyncContext()` 为真，`:516-523` 的表达式下钻守卫因此放行）。**BC36943 必须报进本方法的 `diagnostics` 形参袋**——F09 落地后该实参是**每桶独立袋**（合并回全局 `_diagnostics` 发生在本方法返回之后），该袋的 `HasAnyErrors()` 就是这一桶的发射 gate 入参；若**绕过形参袋**直接 `_diagnostics.Add(...)`，诊断不进独立袋 ⇒ `HasAnyErrors()` 为假 ⇒ **不 gate** ⇒ F10 的 pass 条件（报 BC36943 且不崩）无法达成 |

### 为什么这样落（设计取舍记录）

- **复用现有 walker + 开关**，而不是新造 walker：符合「延伸现有机制」；`_isInCatchFinallyOrSyncLock` 的状态机（`:525-541` Try / `:579-591` SyncLock / `:593-600` Using）已经完整，新造会重复实现且易漂移。
- **只做 await 位置检查**（开关关掉 On Error 报告）：顶层 `On Error Resume Next` 的既有行为（`spec:359` 规定报 unsupported 诊断）不在本任务范围；把 `ERR_TryAndOnErrorDoNotMix` 一并带进顶层属**未要求的扩面**，会污染回归面。
- **不去改 `GetBoundMethodBody` 让它返回真实体**：初始化器绑定发生在类型编译的**前置**（`VB\Compilation\MethodCompiler.vb:599-623`），早于逐方法编译；把体搬进方法体路径是一次结构性重写，远超本缺陷的需要（**否决**，记录在此以免反复回归）。
- **多树提交（`#Load`）**：`parentBinder` 会在换树时重建（`:118-127`），故 walk 需按树分界执行（或等价地保证同一树内语句与 binder 同源）。pass 条件含「多树提交下诊断只报一次、位置指向肇事树且行号正确」。

### 不变量

- 顶层 `Try` **体内**的 `Await` **仍合法**（实测 g5 exit 0）。
- 顶层 `Using` 内的 `Await` **仍合法**（实测 g10 exit 0；walker 的 `VisitUsingStatement` 不置 `_isInCatchFinallyOrSyncLock`，语义一致）。
- 普通 `Async Function` 同形状结果不变（BC36943 ×2，实测 g8）。
- 顶层 `On Error` / 行号标签的既有诊断与行为不变。
- 顶层语句在**非 async 上下文**下的其它 `Await` 诊断（若有）不变——本单元只加 Catch/Finally/SyncLock 这一条。
- `Await` 藏在 lambda 里不误报（walker 的 `VisitLambda` `:615-620` 不下钻，与普通方法一致）。
- 诊断**位置**落在肇事 `Await` 关键字（`node.Syntax`，`:608` 的既有形状）。

### 陷阱

- walker 的 `Visit`（`:516-523`）在非 async 上下文会拒绝下钻表达式 ⇒ **必须**用 async-aware 的 `TopLevelCodeBinder`，否则整条检查静默失效（**最容易踩的坑**）。
- `VisitTryStatement` 有 `Debug.Assert(Not node.WasCompilerGenerated)`（`:526`）⇒ 合成块里的 Try 必须来自真实语法（顶层语句正是真实语法 ⇒ 成立；但**不要**把字段初始化器也塞进这个 walk 的语义假设里，尽管表达式里不可能有 Try）。
- 依赖 **F09**：BC36943 必须报进 `BindFieldAndPropertyInitializers` 的**形参袋**——F09 落地后它是**每桶独立袋**，已不再直通全局 `_diagnostics`；报错落点错（例如直接 `_diagnostics.Add(...)`）会让该桶的 `HasAnyErrors()` 为假 ⇒ **拦不住发射**，崩溃依旧。同理，没有 F09 时诊断即便报出也拦不住发射（这正是根 A 的 A-2/A-3 必须成对的原因）。
- `SyncLock` 子形状（g14）**必须**进验收：它今天不崩而是静默产出坏 IL，最容易在「只看崩不崩」的验收里漏网。

### pass 条件

见 `README.md` §七 `F10` 行。

---

## §F11 顶层标签语句纳入实例初始化器序列（判定：**修好**）

### 目标

顶层 `GoTo` 指向顶层标签正常工作（跳转生效），不再 NRE。**与 `Await` 无关**（实测 g1/g11 无 `Await` 也崩）。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2613-2615` | `Case SyntaxKind.LabelStatement ' TODO (tomat): should be added to the initializers / Exit Select` → 改为与 `:2625-2637` 的 `Case Else` **同款收集**（受 `binder.BindingTopLevelScriptCode` 守卫；`reportAsInvalid` 分支同步照 `:2634-2636` 处理）。上游 TODO 的注释按「已实施」移除 |
| ② | （不动代码，验收面）`VB\Binding\Binder_Statements.vb:59-60` | 已有 `Case SyntaxKind.LabelStatement : Return BindLabelStatement(...)` ⇒ 标签入序列后即由既有路径绑定（`:926-948`，注释 `:930-934` 逐字保证标签符号必被找到） |

### 为什么可行（设计依据）

- 顶层标签的 `SourceLabelSymbol` **本来就存在**：标签由 `LabelVisitor` 扫整棵语法根收集（`VB\Binding\ExecutableCodeBinder.vb:50-74`），`GoTo` 因此能绑定成功、**零诊断**——缺的只是**语句本身没进体**。
- 普通方法里的标签语句就是普通语句 ⇒ 顶层补上后，`<Initialize>`（一个普通 async 方法，`MethodKind.Ordinary` + `IsAsync = True`，`VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:105-109`/`:51-55`）走的是与普通 async 方法**同一条**重写路径（实测 g4 证明该路径下 `GoTo` 跨 `Await` 合法且跳转生效）。
- 改动只有收集点一处，**不碰** codegen（崩溃栈顶在共享 `Core\CodeGen\BasicBlock.cs:325`，不修）。

### 不变量

- `AddInitializer` 的既有规则**逐字沿用**（`VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:1565-1585`；`:1523` vs `:1524` 的两桶 span 不对称）。标签语句**不是**元数据常量 ⇒ 走实例桶的无条件累加（`:1524`），**不得**按 `IsMetadataConstant` 跳过。
- 顶层**数字行号标签**（`LabelToken.Kind = IntegerLiteralToken`）与顶层 `On Error` 的既有诊断 / 行为不变（`VB\Binding\Binder_Statements.vb:627-631` 的 `_containsLineNumberLabel` 语义不受影响——该 walker 跑在 `<Initialize>` 的空壳上，F10 只做 await 检查，不会因 F11 而变化）。
- **无 `Await`** 的顶层 `GoTo`（前向 g1 / 反向 g11）与**有 `Await`** 的（g2）**都要过**。
- 顶层标签语句现在会成为实例初始化器序列的一项 ⇒ 调试语法偏移按既有多桶规则计算（`:3242-3258` 的脚本类快路径；pitfalls **P-011 / P-013**：桶与方法 `isShared` 必须一致，但 `<Initialize>` 走 `:3243` 的快路径，`:3276` 的 `Throw` 不可达）。

### 陷阱

- **不要**把标签当成「空操作可省」而改成「给 `GoTo` 报诊断」——那会新增一条 VB 别处不存在的 GoTo 限制，与 `spec:266`「A top-level `GoTo` is an ordinary executable statement」正面冲突，也与「普通上下文合法即可修」的判据相反（见 `README.md` §十 待确认项 1）。
- 该改动使**规范** `:266-268` 的 Decision（「不保证运行效果」）失效 ⇒ 必须同步改 spec（见 §账本与规范义务），否则规范与实现反向漂移。
- 本单元与 F05 同文件（`SourceMemberContainerTypeSymbol.vb`）：`:2613-2615` 与 `:2726-2737` 两区不重叠，但**必须串行落地**。

### pass 条件

见 `README.md` §七 `F11` 行。

---

## §诊断码分配

| 单元 | 码位来源 | 依据 | 实现期复核动作 |
|---|---|---|---|
| F04 | `Errors.vb:1634-1635` 之间现空的 **37005–37049** 带（自然选择 37005） | `meeting-script-extension-methods.md` OPEN QUESTIONS（逐字：`37005-37049` 现为空） | 重跑 `grep -n "= 3700[5-9]\|= 370[1-4][0-9]" VB\Errors\Errors.vb` 确认现空；若 F 也落地则与 F 分配不同码位 |
| F06 | `ERR_NextAvailable = 37341`（`VB\Errors\Errors.vb:1815`） | `meeting-submission-shared-members.md` R9(b) | 重跑同处 grep 确认 `ERR_NextAvailable` 的值未被别的改动推进 |

两枚码都必须走完 `Errors.vb` → `ErrorFacts.vb`（报错/非报错分类）→ `VBResources.resx` → 13 份 `xlf` 全链；F06 与 F04 **串行相邻**落地（同一批资源文件）。

## §账本与规范义务（本任务不执行，登记为义务）

### `..\..\upstream-merge.md`（改动面补登记）

现状（已检查）：`Binder_Expressions.vb` 在 §2.19 已于册、`Errors.vb` 在 §2.19 / §2.8 已于册；下列文件**均未在册**（判据：`grep` 本文件对文件名零命中）：

| 单元 | 未在册文件 | 建议登记 |
|---|---|---|
| F04 | `Symbols\Source\SourceMethodSymbol.vb` | 归 §2.6「顶层代码 / 脚本提交语义」的文件级锚点（§2.6 现有锚点全在宿主侧，编译器侧一个都没有——`meeting-submission-shared-members.md` R9(a) 已指出该欠账） |
| F05 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb` | 同 §2.6 |
| F06 | `Binder_Expressions.vb`（在册）**+ 新码位** | §2.19 以**新锚点 + 新码位**补登 |
| F07 | `Symbols\Source\ImplicitNamedTypeSymbol.vb`、`SynthesizedEventAccessorSymbol.vb`、`SourceWithEventsBackingFieldSymbol.vb`、`SynthesizedWithEventsAccessorSymbol.vb` | 新增条目或归 §2.6；**注意**：这三处断言是**上游行为**（推断，见 §裁决登记 D3），本地改动属「修上游在脚本模式下的可达缺陷」，登记时须写明理由 |
| F08 | `Binder_Expressions.vb`（在册） | §2.19 新锚点 |
| F09 | `Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb`（后者在 §二·补 的 `7a0111e` 行被列为未登） | 新增条目；`Binder_Initializers.vb` 的欠账可借本次一并补上 |
| F10 | `Binding\Binder_Initializers.vb`、`Binding\Binder_Statements.vb` | 同上 |
| F11 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb` | 同 §2.6；登记须注明「移除上游 `TODO (tomat)`」这一事实 |

**零改动的文件不要登记**：`Core\CodeGen\*`（`ILBuilder.cs` / `BasicBlock.cs`）、`CodeGen\EmitExpression.vb`、`Binding\BindingDiagnosticBag.vb`、`Emit\NamedTypeSymbolAdapter.vb`、`Errors\Errors.vb` 与 13 `xlf` 之外的资源文件。

### `spec\spec-scripting-dialect.md`（规范义务）

| # | 位置 | 义务 |
|---|---|---|
| 1 | `:16`（顶层形式映射表） | `Sub`/`Function` 行补「`Shared` 在脚本顶层是**必需**而不是非法」（容器不是标准模块）；`Dim` 行补 `Shared` 分支与其初始化器落点（共享构造器） |
| 2 | `:56`、`:273` 的「穷尽」主张 | 与实测不符（顶层 `Event`/`WithEvents`/`Property` 也被送进脚本类，`DeclarationTreeBuilder.vb:175-198`）；须改写或限定（`meeting-submission-shared-members.md` R8(c) 已照单接受） |
| 3 | `:176`（字段/属性初始化器是 async 上下文） | 收窄为「**非共享**字段/属性初始化器 + 顶层语句构成 async 上下文」，并补一句「共享字段/属性初始化器的 `Await` 报新码 X」（与 F06 配套；本条即会议 R8(a)） |
| 4 | `:231-239`（脚本诊断族表） | 为 F04 的新码与 F06 的新码**各加一行**并标 C# 对应物（CS1105 / CS8100） |
| 5 | `:266-268` + `:347`（顶层标签与 `GoTo`） | F11 之后 Decision 必须改写：顶层 `GoTo` 与普通方法内 `GoTo` 同义、跳转生效；删去「runtime effect … not guaranteed」与 Boundaries 里的同义句 |
| 6 | `:308-317`（C# 对照表） | 补一行「脚本类里的 `<Extension>` 成员必须 `Shared`，与标准模块相反」（会议 R16 ④-ii） |
| 7 | `:64`（source order） | 明写「实例初始化器与顶层语句」的单一序列；共享初始化器的时机（类型级、先于首次引用）单独写一句（会议 R8(b)） |

### `issues\`（订正义务，本任务不改文本）

| # | 位置 | 订正 |
|---|---|---|
| 1 | `issue-script-top-level-goto-await-crash.md` 的「根因方向」 | 「与 #10 同源」被实测证伪（g1/g11）；正确根因是 `SourceMemberContainerTypeSymbol.vb:2613-2615` 丢弃 `LabelStatement`（含上游 `TODO (tomat)`） |
| 2 | `issue-script-top-level-await-in-try-crash.md` 的标题与症状 | 触发面须补 `SyncLock` 子形状，且其后果是**编译通过 + 运行期 `SynchronizationLockException`**（坏产物，非崩溃）——g14 实测 |
| 3 | `issue-submission-shared-field-initializer-typeload.md` 的触发边界表 | 「有初始化器」应读作「**静态桶里有需要注入构造器的条目**」（会议已定口径） |
| 4 | `issue-initializer-diagnostic-does-not-gate-emit.md` | 根因链补 `VB\Compilation\MethodCompiler.vb:599-623` 这一环（会议 R6 明文要求） |

## §变更面汇总（W-GATE 第 3 条的白名单）

> **编译器源码（`Compilers\`）**：

| 文件 | 单元 | 改动性质 |
|---|---|---|
| `VB\Symbols\Source\SourceMethodSymbol.vb` | F04 | 加守卫 + 加诊断分支（`:1500-1513`、`:1624-1648`） |
| `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb` | F05、F11 | 构造器符号分叉（`:2726-2737`）；标签语句收集（`:2613-2615`） |
| `VB\Symbols\Source\SynthesizedEventAccessorSymbol.vb` | F07 | 断言收窄（`:495`） |
| `VB\Symbols\Source\SourceWithEventsBackingFieldSymbol.vb` | F07 | 断言收窄（`:66`） |
| `VB\Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb` | F07 | 断言收窄（`:93`） |
| `VB\Binding\Binder_Expressions.vb` | F06、F08 | 初始化器 `Await` 新判据（`:4734-4749` 一带）；脚本类隐式 `Me` 判据（`:2262-2270`） |
| `VB\Binding\Binder_Initializers.vb` | F09、F10 | `ProcessedFieldOrPropertyInitializers` 增 error 入参（`:19-55`）；顶层语句绑定后补跑 await 检查（`:102-203`） |
| `VB\Binding\Binder_Statements.vb` | F10 | walker 增开关 + 私有入口（`:454-635`） |
| `VB\Compilation\MethodCompiler.vb` | F09 | 初始化器两桶各自绑进独立袋、把该桶的 error 状态传入 `ProcessedFieldOrPropertyInitializers`，随后合并回共享袋（`:599-623`） |
| `VB\Errors\Errors.vb`、`VB\Errors\ErrorFacts.vb` | F04、F06 | 两枚新码 + 分类 |
| `VB\VBResources.resx`、`VB\xlf\*.xlf`（13 份） | F04、F06 | 新码文案 + 本地化 |

**测试（新增）**：`Compilers\VisualBasicSymbolTest\...\ExtensionMethodTests.vb`（F04 新用例）、`Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb`（F06/F08/F10 新用例）、`Compilers\VisualBasicEmitTest\...`（F09/F11 新用例）、`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`（L3/L4 新用例）。

**明确零 diff（W-GATE 核对）**：`Compilers\Core\Portable\CodeGen\*`（`ILBuilder.cs` / `BasicBlock.cs`）、`VB\CodeGen\EmitExpression.vb`、`VB\Binding\BindingDiagnosticBag.vb`、`VB\Emit\NamedTypeSymbolAdapter.vb`、`VB\Parser\*`、`VB\Syntax\*`、`PublicAPI.*.txt`、`spec\` / `meetings\` / `proposals\` / `issues\`。

---

## §裁决登记（本计划已裁，实现期不得翻转）

| # | 待裁项 | 裁决 | 依据 |
|---|---|---|---|
| D0 | 顶层 `GoTo`→顶层标签：修好 / 报错 | **修好**（待作者确认，见 `README.md` §十） | 普通上下文合法且跳转生效（g4）；规范该 Decision 的上下文是上游 TODO 的描述 |
| D1 | F04 新分支与 `ParameterCount = 0` / 首参检查的先后 | **插在 `:1633` 之前**（否则断言先炸）；与 BC36552 的先后按「无参 + 非 `Shared` 报哪一条」由 `meeting-script-extension-methods.md` OPEN QUESTIONS 保留为空 —— 实现期裁并记入 pass 条件（**不得**出现两条都报或都不报） | `:1630-1634` 的既有序列 + C# 的 `:243-246` 形状 |
| D2 | F07 的断言谓词可用性 | 优先 `Not …IsImplicitClass`；若从 `NamedTypeSymbol` 不可直接访问，改用等价判据（实现期确认后写入 pass 条件） | `ImplicitNamedTypeSymbol.vb:35` 已在用该属性（**推断可访问**，未在 `NamedTypeSymbol` 基类确认） |
| D3 | F07 的断言是否为上游行为 | **推测（高置信）= 上游行为**（`ImplicitNamedTypeSymbol` 与三处断言均未见本地登记），故本地改动是「修上游在脚本模式下的可达缺陷」；实现期以 `git log`/上游比对确认后写入账本理由 | 未在册 + 未被本任务复核 |
| D4 | F09 的修法 | **B**（让发射门看见初始化器诊断），见 §F09 选型表 | `EmitExpression.vb:207` 的契约注释 + issue 09 的三候选定价 |
| D5 | F10 是否复用 walker 的全部检查 | **只做 await 位置检查**（开关关掉 On Error 报告） | 避免未要求的扩面污染回归面 |
| D6 | 两枚新码的码位 | F04 = 37005 带；F06 = `ERR_NextAvailable` | 两份会议 RESOLUTION / OPEN QUESTIONS |

## §待定项（交给实现期的实测/确认，均已给判定路径，不留悬空设计）

| # | 待定项 | 判定路径 |
|---|---|---|
| U1 | 共享**属性**初始化器隐式引用实例方法是否命中 BC30369（F08 的 Property 分支） | 内存编译探针：`Shared ReadOnly Property P As Integer = F()`（`F` 为实例方法），断言 BC30369；不命中则须补 Property 分支的判据（**不得**只覆盖 Field） |
| U2 | 共享**数组上界**隐式初始化器路径能否出现 `Await`（F06 子情形 4） | 若不可表达 ⇒ 显式记录为**不可达**（附证据），不留悬空项 |
| U3 | 多树提交（`#Load`）下 F10 的诊断只报一次且位置正确 | 内存编译用例（`SourceReferenceResolver` 内存实现，不落盘） |
| U4 | F11 之后顶层数字行号标签在 `<Initialize>` 体里的发射形状 | 内存编译 + `Emit` 断言（不新增行为变化即可；若产生行为差异须打回重裁） |
| U5 | HEAD 的 Release 构建行为（F07 的旁证升级） | 构建 HEAD Release 后复跑 g12/g13/g1/g4 四探针；**若作者判定成本过高，可接受以「Debug 因断言而定、Release 因断言不可达而不断」的机制论证替代**（该论证已是实锤：断言非控制流） |
