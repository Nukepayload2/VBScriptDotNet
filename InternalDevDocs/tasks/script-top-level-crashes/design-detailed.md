# 详细设计：脚本顶层崩溃族一次收口（script-top-level-crashes）

> 状态：详细设计（改动蓝图 + 裁决规则 + pass 条件）。依据：`design-overview.md`（总体设计 / 判定表 / 分批）→ 本文件 → `test-plan.md`。
> 路径相对仓库根；编译器前缀统一 `Compilers\VisualBasic\Portable\`（简写 `VB\`），共享编译器前缀 `Compilers\Core\Portable\`（简写 `Core\`）。
> 三态：锚点行均**已检查**（本代理逐条 Read `文件:行号`）；症状均**已运行**（`README.md` §六 探针）；推断项按 `manifest.md` 标「推测 / 猜测」。失败点缓存 `<项目根>/tmp/vortex-logs/top-level-implicit-shared/pitfalls.md`（只追加不覆盖；开工先读）。

## §0 通用约定

1. **测试执行**：`Scripting\VisualBasicTest`（net10.0，MTP/xunit.v3）**禁用 `dotnet test`**（EXIT 0 但静默不跑）；先 `dotnet build`，再直跑程序集 `dotnet <输出>\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.dll -automated`（全量）/ `-class <FQN>` / `-method <FQN>`（FQN 须完整到 `命名空间.类.方法`；缺段或拼错**静默 0 跑**，须核对 `TestCasesToRun > 0`）。编译器侧走七门 gate `powershell -File scripts\verify-vb-compiler-tests.ps1`。
2. **无副作用纪律**：单测禁网络 / 文件写入 / 进程启动 / 注册表写入。全部用例为内存 I/O（`VisualBasicScript.Create/RunAsync/Compile` + 内存 `StringReader`/`StringWriter`；编译器侧 `VerifyDiagnostics` 全内存）。`vbi.exe` 直跑探针**只作本阶段的取证手段，不作验收用例**。
3. **变更面纪律**：`Core\CodeGen\`（`ILBuilder` / `BasicBlock` / `EmitExpression` 所在层）**零改动**；容器种类零改动；`PublicAPI.*.txt` 零增量；`spec\` / `meetings\` / `proposals\` / `issues\` 文本零改动（改动作为义务记在 §账本与规范义务）——本任务**变更面白名单不含这四个目录**，其**整体** `git diff` 状态不作为验收判据。
4. **同文件串行**：`Symbols\Source\SourceMemberContainerTypeSymbol.vb`（F05 + F11）、`Binding\Binder_Expressions.vb`（F06 + F08）、`Errors.vb`+`ErrorFacts.vb`+`VBResources.resx`+13 `xlf`（F04 + F06）**不得**跨单元并行落地。
5. **每条 pass 断言必须有「反向对照」**：凡「让 X 工作」的单元必须同时钉住「相邻的 Y 不变」（如 F08：「实例成员隐式访问仍合法」；F11：「无 `Await` 与有 `Await` 都要过」；F10：「`Try` 体内与 `Using` 内的 `Await` 仍合法」）。

---

## §F04 `<Extension>` 施加于脚本类实例成员 → 新诊断（判定：**报错**）

### 目标

顶层 `<Extension>` 漏写 `Shared` 时给出一条**能定位到行的用户诊断**，取代 `Debug.Assert(Me.IsShared)`（`VB\Symbols\Source\SourceMethodSymbol.vb:1505`）。

### 上游前置决定（不推翻）

`meeting-script-extension-methods.md` **R2** 采纳候选 B：「维持『脚本类里只有 `Shared` 成员可作扩展方法』，补齐缺失诊断」，两处同批落地：①早期解码侧给 `:1500-1503` 的条件补 `Me.IsShared` 守卫（**消除 `:1505` 断言可达性**）；②完整解码侧在 `:1622-1652` 的序列里补分支。诊断**必须点名 `Shared`**，且按 R2 的「复会加强」承担叙事职责（讲出「标准模块的成员隐式 `Shared`，脚本顶层容器不是标准模块，所以这里的 `Shared` 是必需而不是非法」）。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMethodSymbol.vb:1500-1514` | 早期解码的进门条件补上 `Me.IsShared` 成为四重条件（`MethodKind` ∈ {Ordinary, DeclareMethod} ∧ `AllowsExtensionMethods()` ∧ `ParameterCount <> 0` ∧ `Me.IsShared`）；不满足时**不进入** `isExtensionMethod` 计算（保持 `False`），从而 `:1505` 断言不可达 |
| ② | `VB\Symbols\Source\SourceMethodSymbol.vb:1622-1652` | 在既有分支序列里补一支：`ElseIf Not Me.IsShared Then` → 报**新诊断**（消息点名 `Shared`）。**位置**：插在 `:1638` 的 `Debug.Assert(Me.IsShared)` **之前**（`:1637` 是 `Else`；否则断言先炸）；与「`:1631` `ParameterCount = 0`」「首参 Optional/ParamArray/泛型约束」的相对次序为**新分支落在 `:1631` 之后**（见 §裁决登记 D1） |
| ③ | `VB\Errors\Errors.vb`（新码位）、`VB\Errors\ErrorFacts.vb`、`VB\VBResources.resx`、`VB\xlf\*.xlf`（13 份） | 新增一枚错误码 + 文案 + 分类 + 本地化全链 |

### 不变量（一个字都不许变）

- `<Extension> Shared Function` 的正向路径（`ExtensionMethodTests.vb:2425-2440` 的 `ScriptExtensionMethods` 形状）零改动通过。
- 既有的 BC36550 / BC36551 / BC36552 / BC36553 / BC36554 的诊断**序列语义**不变：嵌套容器仍走 BC36551（或被 R6 的候选 F 接管，见下），无参仍 BC36552，首参 Optional/ParamArray 仍 BC36553/BC36554。
- **普通 `Module` 与普通 `Class` 编译零行为变化**（`Module` 成员隐式 `Shared` ⇒ 新分支不可达；普通 `Class` 不满足 `AllowsExtensionMethods()` ⇒ 在 ② 的第 2 支就被拦下）。
- **不阻塞**候选 F（嵌套容器承载扩展方法，`meeting-script-extension-methods.md` R6）：新分支与 F 落在同一段序列，F04 的插入点不得把嵌套判据挤到不可达位置。

### 陷阱

- `SourceMethodSymbol.vb:1622-1652` 是**完整解码**序列，`attrData.IsTargetAttribute(...)` 进门后**只报错、不产出识别结果**（注释 `:1623` 逐字「Just report errors here. The extension attribute is decoded early.」）⇒ ② 只影响诊断，不影响扩展方法的识别；真正的识别在 ①。
- ① 若把 `Me.IsShared` 加进条件而不是**前置守卫**，会顺带改掉 `isExtensionMethod` 的取值语义——**必须**只在 `Shared` 为真时进入原逻辑，不改变原逻辑的其它入口条件。

### pass 条件

见 `README.md` §七 `F04` 行。核心两句：**漏 `Shared` → 报 BC37005（`ERR_ExtensionMethodNotShared`）且消息含 `Shared`**；**`<Extension> Shared Function` 正向零改动通过**。

---

## §F05 submission 共享分支改用无参共享构造器符号（判定：**修好**，甲）

### 目标

顶层 `Shared` 字段/属性带初始化器（含 `Shared ReadOnly`、含 `Shared Dim arr(2)` 的隐式上界）正常工作，不再 `TypeLoadException`。

### 上游前置决定（不推翻）

`meeting-submission-shared-members.md` **R1**：`TypeKind = Submission` 分支按 `isShared` 分叉——**共享时走同文件的 `EnsureCtor`**（`VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2774-2805`，`:2803` `New SynthesizedConstructorSymbol`，形参恒空），**实例时仍走 `SynthesizedSubmissionConstructorSymbol`**。形态是**两个符号**（issue 05 候选 B），**不是**「一个符号、形参表分叉」（候选 A）——依据 D5 已证实例 1（C# 的静态构造器是另一个类）。R5：甲与乙**同批设计、独立验收**，甲先落地止血。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2722-2740` | 现有 `If Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(initializers, False) Then New SynthesizedSubmissionConstructorSymbol(...)` 改造为：`isShared` 时改调 `EnsureCtor(...)`（同文件 `:2774-2805`，其判据是 `Members` 里是否已有 `StaticConstructorName`，键在 `:2776`）；`Not isShared` 时**逐字保持现状** |
| ② | 与 `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:3198-3217` 的交互 | 两套机制都按 `.cctor` 名查重（本处 vs `:3204`）⇒ **plan 前置实证项**：同文件同时含 `Const d As Date = #…`（走 `CreateSharedConstructorsForConstFieldsIfRequired`）与 `Shared x As Integer = 5`（走新分支），两者落到**同一个** `.cctor` 且发射形状正确——由 `F05-L2-4`（共享构造器唯一）与 `F05-L3-2`（两条初始化取值正确）锁定 |

### 不变量

- `GetScriptConstructor()`（`VB\Symbols\NamedTypeSymbol.vb:697-700`，`InstanceConstructors.Single()`）不受影响——共享构造器不进 `InstanceConstructors`（`VB\Symbols\MethodSymbol.vb:517-521` 的 `IsScriptConstructor` 要求 `MethodKind = Constructor`）。
- **非共享**实例初始化器路径（`<Initialize>` + 匿名提交构造器）**逐字节不变**。
- 共享初始化器的**执行时机与语义**不变：仍在（隐式）共享构造器里、仍是类型级、「先于首次访问该静态字段」（`InternalDevDocs\vblang\spec\type-members.md:1269` / `:2021` 的模型；参 pitfalls **P-001 / P-002 / P-003**）。**不得**借本单元把共享初始化器搬进 `<Initialize>`（`meeting-submission-shared-members.md` R3 已否决「丙」，附复活条件）。
- `Shared ReadOnly` 仍在共享构造器里赋值（`VB\CodeGen\EmitAddress.vb:261-284` 的 `HasHome` 要求 `MethodKind.SharedConstructor`）——甲 之下自然成立，**不引入不可验证 IL**（R4）。
- `beforefieldinit` 不改（甲 仍产 `.cctor`）。**注意 P-001**：VB 主动打该标志（`VB\Emit\NamedTypeSymbolAdapter.vb:496-499`）。

### 陷阱

- **P-010**：两个构造器生产者（`AddDefaultConstructorIfNeeded` 的提交分支 vs `CreateSharedConstructorsForConstFieldsIfRequired`）谁生效取决于 `Members` 里是否已有 `StaticConstructorName`——新分支必须复用 `EnsureCtor` 的**同一查重键**，不要另造。
- **P-011 / P-013**：不要动「初始化器归属/桶」；`<Initialize>` 走 `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:3246` 的脚本类快路径，`:3279` 的 `Throw ExceptionUtilities.Unreachable` 不可达——任何「改桶」的想法都在 R3 的否决范围内。
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
| ① | `VB\Binding\Binder_Expressions.vb:4743-4760`（**调用结构**） | 在 `:4749-4750` 的 query 分支与 `:4751-4752` 的 `ElseIf Not IsInAsyncContext()` 之后补一个子情形判据 `:4753-4754`（判据本体 `IsInSharedInitializerContext()` 在 `:4737-4741`）：共享（`IsShared`）字段/属性的初始化器里出现 `Await` → 报 **BC37341**（`ERR_BadAwaitInSharedInitializer`）。**硬事实（已检查）**：走 `GetAwaitInNonAsyncError()`（`:5066-5080`）里加 `case` 这条路**不可达**——共享字段时 `IsInAsyncContext()`（`:4729-4730`）为真 ⇒ `:4752` 根本不执行 |
| ② | 同上 | 判据必须覆盖**共享属性**：VB 的属性初始化器 `ContainingMember` 是 `PropertySymbol`（不是 backing field）⇒ **不能照抄 C# 的 `case SymbolKind.Field`**（C# 判据见 `Binder_Await.cs:152-213`；C# 之所以也覆盖静态属性是因为它把属性初始化器登记在 backing field 上） |
| ③ | `Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` / 13 `xlf` | 新码全链。**硬条件（R2）**：必须**新码**，不复用 `ERR_BadAwaitNotInAsyncMethodOrLambda = 36937`（消息不对症）。消息要点名「共享/静态初始化器在执行时是同步的共享构造器」 |

### 判据覆盖的子情形清单（R2 的 TODO，逐条进验收）

1. 共享**字段**初始化器含 `Await`（`Shared Dim x = Await Task.FromResult(1)`，issue 06 原形状）；
2. 共享 `ReadOnly` **字段**初始化器含 `Await`；
3. 共享**属性**初始化器含 `Await`（会议已实测 exit 35，与共享字段同一承重点）；
4. 共享**数组上界**隐式初始化器路径（`VB\Binding\Binder_Initializers.vb:253-279` 一带的 `BindArrayFieldImplicitInitializer`；**可表达**，`Shared Dim arr(Await Task.FromResult(2)) As Integer` 报 BC37341——见 §待定项 U2 的结论）。

### 不变量

- **实例**（非共享）字段/属性初始化器里的 `Await` **仍合法**（`VB\Binding\Binder_Expressions.vb:4729-4730` 的承诺面 + 顶层实例形状实测 exit 0）——**回归必测**。
- **嵌套类型**的字段/属性初始化器 `Await` 仍走 BC36937（属 F09 的面，**不得**被本单元的新判据截胡）。
- `Try` 体内 / `Using` 内的 `Await` 与本单元无关（F10 的面）。
- 判据必须只在**初始化器**这一上下文成立，不能扩到方法体（`SymbolKind.Method` 分支 `:4725-4726` 保持原样）。

### 陷阱

- `IsInAsyncContext()` 另有 10 处以上消费者（`Binder_Statements.vb` / `Binder_Lambda.vb` 的多处）——**不要**走「让 `IsInAsyncContext()` 对共享字段返回 False」这条低成本子形态（`meeting-submission-shared-members.md` R2 已给出理由：消息不对症 + 消费者扩面未清点）。本单元**只改调用结构**。
- `IsShared` 的读取点：新判据要按**初始化器所属符号**的 `IsShared`（字段符号 / 属性符号），**不要**按引用点所在成员（pitfalls **P-009**：`Await` 看 `IsScriptClass`、隐式 Me 看 `IsShared`、`ReadOnly` lvalue 看 `KindOfContainingMethodAtRunTime()+IsShared`，三套规则的键会分裂；pitfalls **P-014**）。
- 依赖 **F05（甲）**：共享初始化器进的是带参 `.cctor`（issue 05 先崩）时，新诊断会被 issue 05 的症状掩盖，**验收会误判** ⇒ F05 必须先落地。

### pass 条件

见 `README.md` §七 `F06` 行。核心：四条子情形全部报 **BC37341**（`ERR_BadAwaitInSharedInitializer`）、实例初始化器仍合法、嵌套类型仍 BC36937、新码全链完整、位置落在 `Await` 关键字。

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
| ③ | `VB\Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93` | 同族第三处，**一并收口**（无遗留问题纪律）——落地时逐行确认：断言之后 `:95-96` 无条件加 `CompilerGenerated` ⇒ 该处同样不是控制流 |

### 不变量

- 真正的 `ImplicitClass`（非脚本、非提交的隐式类）容器上该断言的语义**保住**（仍不放行）。
- `Event` / `WithEvents` 在**嵌套类型**里的行为逐字不变（实测 q34/q35 exit 0）。
- Release 发射结果不变（断言本就编译掉）。
- 不改 `IsImplicitlyDeclared` 本身 ⇒ 其余消费者（`Symbol.GetCustomAttributesToEmit`、`MethodCompiler.vb:1841` 等）行为不变。

### 陷阱

- 断言去掉后，发射路径要**真的产出正确结果**——不能只满足「不崩」。验收必须断言**语义**：`WithEvents` 的钩子生效（handler 被调用）、`Shared Event` 的投递生效（顶层 `Shared Sub` 里 `RaiseEvent` ⇒ handler 计数 = 1）、实例 `Event` 的合成 add/remove 访问器被真实调用（可观测证据 = `AddHandler` / `RemoveHandler` 调用到该访问器 + 运行不崩，**不**作更强断言）。**实例 `Event` 的 raise→handler 计数在无副作用验收下不可达**（顶层 `RaiseEvent` 语句不支持；顶层实例 `Sub` / lambda 里的 `RaiseEvent` 撞 `Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:36` 的独立断言）⇒ 该半登记为缺口，见 `README.md` §七 F07 行。`e307d0f` 的 `EVENT-OK` 只证明「不崩」，**不证明语义正确**。
- `IsImplicitClass` 的属性名与语义：`ImplicitNamedTypeSymbol.vb:35` 已在用，且它是 `NamedTypeSymbol.vb:717` 的 `Public Overridable` 属性 ⇒ 三处 `ContainingType` 直接可访问（**实锤**，无需备用判据；见 §裁决登记 D2）。

### pass 条件

见 `README.md` §七 `F07` 行。核心：两种成员 × （实例/`Shared`）四种探针**不再终止**且**语义正确**——`Shared Event` 与两种 `WithEvents` 形状由 **raise→handler 计数**判定，实例 `Event` 由**合成 add/remove 访问器被真实调用**判定（raise 半不可达，作为缺口登记在 `README.md` §七 F07 行）；嵌套类型不变；真隐式类的断言仍生效。

---

## §F08 脚本类共享成员的隐式 `Me` → BC30369（判定：**报错**）

### 目标

共享成员体 / 共享初始化器里隐式引用实例成员 → 报 **BC30369**（与普通类同码同形），取代「零诊断 + 运行期 `InvalidProgramException`」。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Expressions.vb:2263-2273` | 脚本类分支里把「隐式 ⇒ 一律 `Return True`」改为：**隐式**引用先查 `IsMeOrMyBaseOrMyClassInSharedContext()`（`:2235-2255`，同一 `Binder` 内已存在），命中共享上下文则照常落 `:2275-2281` 的 `ERR_BadInstanceMemberAccess`（BC30369）；**不命中仍 `Return True`**（`spec:243` / `spec:258` 的隐式访问承诺） |
| ② | 同上 | **显式**引用路径**逐字保持**：`errorId = ERRID.ERR_KeywordNotAllowedInScript`（BC36966，`:2266`） |

### 判据形状（落地时按此三条自检）

1. 脚本类 + **显式** `Me`/`MyClass` → BC36966（不变）。**显式 `MyBase` 不在此列**：`Binder_Expressions.vb:2374` 在 `CanAccessMyBase` 返回 False（BC36966 已报出）**之后仍**构造 `BoundMyBaseReference`，而提交类 `BaseType` 为 Nothing ⇒ BoundNodes 的 type 非空断言先炸（exit 35）。属**独立缺陷、不在 F08 改动面**（顶层显式 `MyBase`：该处无条件构造 `BoundMyBaseReference`，而提交类 `BaseType` 为 Nothing ⇒ 绑定节点的 type 非空断言先炸；`issues\` 目录无对应条目，仅在本文件与流水账登记）。
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
- 本单元与 F06 同文件（`Binder_Expressions.vb`）：`:2257-2289` 与 `:4737-4760` 两区不重叠，但**必须串行落地**以免 rebase。
- 与 F05（甲）的交互：`Shared y = F()` 先撞 issue 05；F05 落地前本单元的判据不可见 ⇒ **F08 的「初始化器形状」验收依赖 F05（甲）**（方法体形状不依赖）。

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
| A | 报错时给 bound 节点标错（`VB\Binding\Binder_Expressions.vb:4749-4755` 之外让 `BoundAwaitOperator` 带 `hasErrors`） | **不取**：只覆盖 `Await` 一条诊断，issue 09 自陈的「派生问题（还有哪些初始化器绑定期诊断落进同一个洞）未清点」会留成遗留问题 |
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

见 `README.md` §七 `F09` 行。核心：嵌套类型（实例字段 / 共享字段 / 实例属性）的 `Await` 初始化器报 BC36937 且该桶不发射；**语句种类**（顶层语句绑定报出的 bag-only 诊断，如 BC30582 / BC31003）走同一条通道 gate 同一个桶；warning 不 gate（`HasAnyErrors` 只算 error 级）；没有初始化器 error 的编译零行为变化。

用例分两桶（判别力口径与依赖登记见 `test-plan.md` §3 F09 表下）：嵌套类型那组是**判别性**用例（门不生效时 codegen 在 `EmitExpression.vb:206-209` 的 `Case Else` 抛 `UnexpectedValue('AwaitOperator')`）；语句种类里 `SyncLock <非引用类型>` / 非末条裸表达式两条载体自身可发射、只锁诊断集合，其判别性由同桶附加的**不可发射**形状（顶层 `Try` / `Catch` 内 `Await`）承担——BC36943 由后绑定 walker 报出、bound 节点不可变 ⇒ 只能落袋，门不生效时 `<Initialize>` 进发射期并在 `Core\CodeGen\ILBuilder` 里崩（探针 g6/g7 / g15 实测 exit 3；该层本任务零改动）。该崩溃属**可被独立修复**的缺陷而非 codegen 的结构性缺口 ⇒ 若日后被修掉，语句种类的判别力须按 P-033 复查。

---

## §F10 脚本顶层补跑 `Catch`/`Finally`/`SyncLock` 内的 `Await` 检查（判定：**报错**）

### 目标

顶层 `Catch` / `Finally` / `SyncLock` 里的 `Await` 报 **BC36943**（与普通 `Async Function` 同码），取代三种后果：`Catch`/`Finally` → NRE（exit 3）、`SyncLock` → **静默产出运行期损坏的 IL**（exit 24，g14 新发现）。

### 根因（一句话）

BC36943 的检查住在 `BindMethodBlock` 的 `CheckOnErrorAndAwaitWalker`（`VB\Binding\Binder_Statements.vb:291`/`:330`/`:625-636`），而脚本 `<Initialize>` 的方法体是空壳（`VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`）⇒ 顶层语句从不过这道 walker。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Binding\Binder_Statements.vb:454-658` | 给 `CheckOnErrorAndAwaitWalker` 加一个**开关构造参数**（如 `onlyCheckAwaitInTryHandler As Boolean = False`）：为真时 `VisitBlock` 跳过 `:509-516` 的 `ERR_TryAndOnErrorDoNotMix` 报告（`reportedAnError` 不置位），只保留 `:625-636` 的 BC36943 分支。**默认值保持既有行为**，`BindMethodBlock` 的调用点零改动 |
| ② | `VB\Binding\Binder_Statements.vb`（同 `Partial Friend Class Binder`） | 新增一个 `Private Shared` 入口，接收 `binder`/合成块/诊断袋，以 `onlyCheckAwaitInTryHandler:=True` 调 walker（同一 `Binder` 的 partial 文件互相可达 `Private` 成员，**无需**改可见性） |
| ③ | `VB\Binding\Binder_Initializers.vb:102-210` | 在 `BindFieldAndPropertyInitializers` 内、`scriptInitializerOpt IsNot Nothing` 时，对已收集的顶层语句拼一个合成 `BoundBlock`（形状照 `:64-67` 的既有模板）并调 ②。**binder 用 `:122-126` 已构造的 `TopLevelCodeBinder`**（它是 async-aware：以 `<Initialize>` 为 containing member ⇒ `IsInAsyncContext()` 为真，`:539-546` 的表达式下钻守卫因此放行）。**BC36943 必须报进本方法的 `diagnostics` 形参袋**——F09 落地后该实参是**每桶独立袋**（合并回全局 `_diagnostics` 发生在本方法返回之后），该袋的 `HasAnyErrors()` 就是这一桶的发射 gate 入参；若**绕过形参袋**直接 `_diagnostics.Add(...)`，诊断不进独立袋 ⇒ `HasAnyErrors()` 为假 ⇒ **不 gate** ⇒ F10 的 pass 条件（报 BC36943 且不崩）无法达成 |

### 为什么这样落（设计取舍记录）

- **复用现有 walker + 开关**，而不是新造 walker：符合「延伸现有机制」；`_isInCatchFinallyOrSyncLock` 的状态机（`:548-565` Try / `:602-615` SyncLock / `:616-623` Using）已经完整，新造会重复实现且易漂移。
- **只做 await 位置检查**（开关关掉 On Error 报告）：顶层 `On Error Resume Next` 的既有行为（`spec:359` 规定报 unsupported 诊断）不在本任务范围；把 `ERR_TryAndOnErrorDoNotMix` 一并带进顶层属**未要求的扩面**，会污染回归面。
- **不去改 `GetBoundMethodBody` 让它返回真实体**：初始化器绑定发生在类型编译的**前置**（`VB\Compilation\MethodCompiler.vb:599-623`），早于逐方法编译；把体搬进方法体路径是一次结构性重写，远超本缺陷的需要（**否决**，记录在此以免反复回归）。
- **多树提交（`#Load`）**：`parentBinder` 会在换树时重建（`:120-129`），故 walk 需按树分界执行（或等价地保证同一树内语句与 binder 同源）。pass 条件含「多树提交下诊断只报一次、位置指向肇事树且行号正确」。

### 不变量

- 顶层 `Try` **体内**的 `Await` **仍合法**（实测 g5 exit 0）。
- 顶层 `Using` 内的 `Await` **仍合法**（实测 g10 exit 0；walker 的 `VisitUsingStatement` 不置 `_isInCatchFinallyOrSyncLock`，语义一致）。
- 普通 `Async Function` 同形状结果不变（BC36943 ×2，实测 g8）。
- 顶层 `On Error` / 行号标签的既有诊断与行为不变。
- 顶层语句在**非 async 上下文**下的其它 `Await` 诊断（若有）不变——本单元只加 Catch/Finally/SyncLock 这一条。
- `Await` 藏在 lambda 里不误报（walker 的 `VisitLambda` `:638-643` 不下钻，与普通方法一致）。
- 诊断**位置**是整条肇事 `Await` 表达式（`node.Syntax`，`:631` 的既有形状；与普通 async 方法里的 BC36943 同形，见 `Semantics\AsyncAwait.vb:3994-4022`）。

### 陷阱

- walker 的 `Visit`（`:539-546`）在非 async 上下文会拒绝下钻表达式 ⇒ **必须**用 async-aware 的 `TopLevelCodeBinder`，否则整条检查静默失效（**最容易踩的坑**）。
- `VisitTryStatement` 有 `Debug.Assert(Not node.WasCompilerGenerated)`（`:549`）⇒ 合成块里的 Try 必须来自真实语法（顶层语句正是真实语法 ⇒ 成立；但**不要**把字段初始化器也塞进这个 walk 的语义假设里，尽管表达式里不可能有 Try）。
- 依赖 **F09**：BC36943 必须报进 `BindFieldAndPropertyInitializers` 的**形参袋**——F09 落地后它是**每桶独立袋**，已不再直通全局 `_diagnostics`；报错落点错（例如直接 `_diagnostics.Add(...)`）会让该桶的 `HasAnyErrors()` 为假 ⇒ **拦不住发射**，崩溃依旧。同理，没有 F09 时诊断即便报出也拦不住发射（这正是根 A 的 A-2/A-3 必须成对的原因）。
- `SyncLock` 子形状（g14）**必须**进验收：它不崩而是静默产出坏 IL，最容易在「只看崩不崩」的验收里漏网。

### pass 条件

见 `README.md` §七 `F10` 行。

---

## §F11 顶层标签语句纳入实例初始化器序列（判定：**修好**）

### 目标

顶层 `GoTo` 指向顶层标签正常工作（跳转生效），不再 NRE。**与 `Await` 无关**（实测 g1/g11 无 `Await` 也崩）。

### 改动蓝图

| # | 落点 | 改动形状 |
|---|---|---|
| ① | `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2621-2633` | `Case SyntaxKind.LabelStatement ' TODO (tomat): should be added to the initializers / Exit Select` 的**丢弃分支删除**（`LabelStatementSyntax` 继承 `ExecutableStatementSyntax`，`Syntax.xml.Syntax.Generated.vb:13444-13445`）⇒ 顶层标签由既有的 `:2621-2633` `Case Else` **同款收集**（受 `binder.BindingTopLevelScriptCode` 守卫；`reportAsInvalid` 分支照 `:2630-2632` 处理）。上游 TODO 的注释随分支一并移除 |
| ② | （不动代码，验收面）`VB\Binding\Binder_Statements.vb:59-60` | 已有 `Case SyntaxKind.LabelStatement : Return BindLabelStatement(...)` ⇒ 标签入序列后即由既有路径绑定（`:949-972`，注释 `:953-954` 逐字保证标签符号必被找到） |

### 为什么可行（设计依据）

- 顶层标签的 `SourceLabelSymbol` **本来就存在**：标签由 `LabelVisitor` 扫整棵语法根收集（`VB\Binding\ExecutableCodeBinder.vb:50-74`），`GoTo` 因此能绑定成功、**零诊断**——缺的只是**语句本身没进体**。
- 普通方法里的标签语句就是普通语句 ⇒ 顶层补上后，`<Initialize>`（一个普通 async 方法，`MethodKind.Ordinary` + `IsAsync = True`，`VB\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:105-109`/`:51-55`）走的是与普通 async 方法**同一条**重写路径（实测 g4 证明该路径下 `GoTo` 跨 `Await` 合法且跳转生效）。
- 改动只有收集点一处，**不碰** codegen（崩溃栈顶在共享 `Core\CodeGen\BasicBlock.cs:325`，不修）。

### 不变量

- `AddInitializer` 的既有规则**逐字沿用**（`VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb:1565-1585`；`:1523` vs `:1524` 的两桶 span 不对称）。标签语句**不是**元数据常量 ⇒ 走实例桶的无条件累加（`:1524`），**不得**按 `IsMetadataConstant` 跳过。
- 顶层**数字行号标签**（`LabelToken.Kind = IntegerLiteralToken`）与顶层 `On Error` 的既有诊断 / 行为不变（`VB\Binding\Binder_Statements.vb:650-654` 的 `_containsLineNumberLabel` 语义不受影响——该 walker 跑在 `<Initialize>` 的空壳上，F10 只做 await 检查，不会因 F11 而变化）。
- **无 `Await`** 的顶层 `GoTo`（前向 g1 / 反向 g11）与**有 `Await`** 的（g2）**都要过**。
- 顶层标签语句是实例初始化器序列的一项 ⇒ 调试语法偏移按既有多桶规则计算（`:3245-3261` 的脚本类快路径；pitfalls **P-011 / P-013**：桶与方法 `isShared` 必须一致，但 `<Initialize>` 走 `:3246` 的快路径，`:3279` 的 `Throw` 不可达）。

### 陷阱

- **不要**把标签当成「空操作可省」而改成「给 `GoTo` 报诊断」——那会新增一条 VB 别处不存在的 GoTo 限制，与 `spec:266`「A top-level `GoTo` is an ordinary executable statement」正面冲突，也与「普通上下文合法即可修」的判据相反（见 `README.md` §十 第 1 条「已裁决：修好」）。
- 该改动使**规范** `:266-268` 的 Decision（「不保证运行效果」）失效 ⇒ 必须同步改 spec（见 §账本与规范义务），否则规范与实现反向漂移。
- 本单元与 F05 同文件（`SourceMemberContainerTypeSymbol.vb`）：`:2621-2633` 与 `:2722-2740` 两区不重叠，但**必须串行落地**。

### pass 条件

见 `README.md` §七 `F11` 行。

---

## §诊断码分配

| 单元 | 码位来源 | 依据 | 码位复核（已完成） |
|---|---|---|---|
| F04 | `Errors.vb:1636` 的 `ERR_ExtensionMethodNotShared = 37005`——占用官方空带（`:1634` 的 `37004` 与 `:1637` 的 `37050` 之间）的起始槽位；**该带现余 37006–37049 为空** | `meeting-script-extension-methods.md` OPEN QUESTIONS（逐字：`37005-37049` 现为空） | 已重跑 `grep -n "= 3700[5-9]\|= 370[1-4][0-9]" VB\Errors\Errors.vb` 复核：37005 已占用，37006–37049 仍空 |
| F06 | `ERR_BadAwaitInSharedInitializer = 37341`（`VB\Errors\Errors.vb:1816`，`ERR_NextAvailable` 随之推进到 `:1818` 的 37342） | `meeting-submission-shared-members.md` R9(b) | 已重跑同处 grep 确认取值 |

两枚码都必须走完 `Errors.vb` → `ErrorFacts.vb`（报错/非报错分类）→ `VBResources.resx` → 13 份 `xlf` 全链；F06 与 F04 **串行相邻**落地（同一批资源文件）。

## §账本与规范义务

> `..\..\upstream-merge.md` 的改动面补登记由 W-GATE 执行（结果落在该文件 §2.20）；`spec\` / `issues\` 的正文修订仍是本任务的登记义务，由后续规范 / issue 阶段执行。

### `..\..\upstream-merge.md`（改动面补登记 —— 已按下表执行）

现状（已检查）：`Binder_Expressions.vb` 在 §2.19 已于册、`Errors.vb` / `ErrorFacts.vb` / `VBResources.resx` 在 §2.19 / §2.8 已于册、`Binder_Statements.vb` 在 §2.4 / §2.19 已于册；下表是本任务的改动面文件与补登去向，**已逐条落入 §2.20**（13 份 `xlf` 同批在册）：

| 单元 | 改动面文件 | 落点建议（**实际执行：八单元统一登记为 §2.20 一条**） |
|---|---|---|
| F04 | `Symbols\Source\SourceMethodSymbol.vb` | 归 §2.6「顶层代码 / 脚本提交语义」的文件级锚点（§2.6 现有锚点全在宿主侧，编译器侧一个都没有——`meeting-submission-shared-members.md` R9(a) 已指出该欠账） |
| F05 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb` | 同 §2.6 |
| F06 | `Binder_Expressions.vb`（在册）**+ 新码位** | §2.19 以**新锚点 + 新码位**补登 |
| F07 | `SynthesizedEventAccessorSymbol.vb`、`SourceWithEventsBackingFieldSymbol.vb`、`SynthesizedWithEventsAccessorSymbol.vb`（**`ImplicitNamedTypeSymbol.vb` 不在改动面**：路线 B 未取，落地的是三处断言的谓词收窄） | 新增条目或归 §2.6；**注意**：这三处断言是**上游行为**（见 §裁决登记 D3），本地改动属「修上游在脚本模式下的可达缺陷」，登记时须写明理由 |
| F08 | `Binder_Expressions.vb`（在册） | §2.19 新锚点 |
| F09 | `Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb`（后者在 §二·补 的 `7a0111e` 行被列为未登） | 新增条目；`Binder_Initializers.vb` 的欠账可借本次一并补上 |
| F10 | `Binding\Binder_Initializers.vb`、`Binding\Binder_Statements.vb` | 同上 |
| F11 | `Symbols\Source\SourceMemberContainerTypeSymbol.vb` | 同 §2.6；登记须注明「移除上游 `TODO (tomat)`」这一事实 |

> 上表第三列是计划期的落点建议；**实际执行**时八单元的改动面合并登记为 `..\..\upstream-merge.md` **§2.20** 一条——`Symbols\Source\` 五处（`SourceMethodSymbol.vb` / `SourceMemberContainerTypeSymbol.vb` / `SynthesizedEventAccessorSymbol.vb` / `SourceWithEventsBackingFieldSymbol.vb` / `SynthesizedWithEventsAccessorSymbol.vb`）、`Compilation\MethodCompiler.vb`、`Binding\Binder_Initializers.vb` 与 13 份 `xlf` 在该条**首次在册**，在册文件（`Binder_Expressions.vb` / `Binder_Statements.vb` / `Errors.vb` / `ErrorFacts.vb` / `VBResources.resx`）在该条以新锚点 / 新码位增补。

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
| 1 | `issue-script-top-level-goto-await-crash.md` 的「根因方向」与「相关」节的「修复方向」句（`:97`） | ①「根因方向」的「与 #10 同源」被实测证伪（g1/g11）；正确根因是顶层 `LabelStatement` 的收集路——它由 `SourceMemberContainerTypeSymbol.vb:2621-2633` 的 `Case Else` 与其它顶层可执行语句**同款**收集（无专用分支），标签语句不入 `instanceInitializers` 时 `GoTo` 的分支目标没有落地块。②`:97` 的「修复方向」句写「**判「修好」**——…（**该取舍待作者确认**）」，是本任务文件夹之外**唯一**仍带「待作者确认」字样的正文（`grep "待作者确认"` 在本任务文件夹之外的命中仅此一处）；须随 F11 的「已裁决：修好」（`README.md` §十 第 1 条）改写：删去「该取舍待作者确认」，与 `spec:266-268` 的 Decision 改写同批落地 |
| 2 | `issue-script-top-level-await-in-try-crash.md` 的标题与症状 | 触发面须补 `SyncLock` 子形状，且其后果是**编译通过 + 运行期 `SynchronizationLockException`**（坏产物，非崩溃）——g14 实测 |
| 3 | `issue-submission-shared-field-initializer-typeload.md` 的触发边界表 | 「有初始化器」应读作「**静态桶里有需要注入构造器的条目**」（会议已定口径） |
| 4 | `issue-initializer-diagnostic-does-not-gate-emit.md` | 根因链补 `VB\Compilation\MethodCompiler.vb:599-623` 这一环（会议 R6 明文要求） |

## §变更面汇总（W-GATE 第 3 条的白名单）

> **编译器源码（`Compilers\`）**：

| 文件 | 单元 | 改动性质 |
|---|---|---|
| `VB\Symbols\Source\SourceMethodSymbol.vb` | F04 | 加守卫 + 加诊断分支（`:1500-1514`、`:1622-1652`） |
| `VB\Symbols\Source\SourceMemberContainerTypeSymbol.vb` | F05、F11 | 构造器符号分叉（`:2722-2740`）；标签语句收集（`:2621-2633`；删除 `Case SyntaxKind.LabelStatement` 丢弃分支） |
| `VB\Symbols\Source\SynthesizedEventAccessorSymbol.vb` | F07 | 断言收窄（`:495`） |
| `VB\Symbols\Source\SourceWithEventsBackingFieldSymbol.vb` | F07 | 断言收窄（`:66`） |
| `VB\Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb` | F07 | 断言收窄（`:93`） |
| `VB\Binding\Binder_Expressions.vb` | F06、F08 | 初始化器 `Await` 新判据（`:4737-4741` 判据 + `:4753-4754` 落点）；脚本类隐式 `Me` 判据（`:2263-2273`） |
| `VB\Binding\Binder_Initializers.vb` | F09、F10 | `ProcessedFieldOrPropertyInitializers` 增 error 入参（`:19-55`）；顶层语句绑定后补跑 await 检查（`:102-210`） |
| `VB\Binding\Binder_Statements.vb` | F10 | walker 增开关 + 私有入口（`:454-658`） |
| `VB\Compilation\MethodCompiler.vb` | F09 | 初始化器两桶各自绑进独立袋、把该桶的 error 状态传入 `ProcessedFieldOrPropertyInitializers`，随后合并回共享袋（`:599-623`） |
| `VB\Errors\Errors.vb`、`VB\Errors\ErrorFacts.vb` | F04、F06 | 两枚新码 + 分类 |
| `VB\VBResources.resx`、`VB\xlf\*.xlf`（13 份） | F04、F06 | 新码文案 + 本地化 |

**测试（新增）**：`Compilers\VisualBasicSymbolTest\...\ExtensionMethodTests.vb`（F04 新用例）、`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb`（F06/F08/F10 新用例；该文件已有 `CreateSubmission` 与脚本编译选项的基建，`Binding\BindingErrorTests.vb` 没有）、`Compilers\VisualBasicEmitTest\...`（F09/F11 新用例）、`Scripting\VisualBasicTest\ScriptTopLevelCrashTests.vb`（L3/L4 新用例，含多树 `#Load` 的 L3 用例）。

**明确零 diff（W-GATE 核对）**：`Compilers\Core\Portable\CodeGen\*`（`ILBuilder.cs` / `BasicBlock.cs`）、`VB\CodeGen\EmitExpression.vb`、`VB\Binding\BindingDiagnosticBag.vb`、`VB\Emit\NamedTypeSymbolAdapter.vb`、`VB\Parser\*`、`VB\Syntax\*`、`PublicAPI.*.txt`、`spec\` / `meetings\` / `proposals\`（后三个目录的正文修订是登记义务）。**`issues\` 不在本任务的变更面白名单内**：其正文订正同样是登记义务，故 `issues\` 目录的**整体** `git diff` 状态不作为本任务的越权判据。

---

## §裁决登记（本计划已裁，实现期不得翻转）

| # | 待裁项 | 裁决 | 依据 |
|---|---|---|---|
| D0 | 顶层 `GoTo`→顶层标签：修好 / 报错 | **修好**（**已裁决**，见 `README.md` §十 第 1 条） | 普通上下文合法且跳转生效（g4）；规范该 Decision 的上下文是上游 TODO 的描述；作者的判定原则亦指向「修好」（`README.md` §一） |
| D1 | F04 新分支与 `ParameterCount = 0` / 首参检查的先后 | **插在 `:1638` 的 `Debug.Assert` 之前**（`:1637` 的 `Else` 之前；否则断言先炸）；**落在 `ParameterCount = 0` 分支之后、首参检查之前** ⇒「无参 + 非 `Shared`」只报 BC36552，不出现两条都报或都不报 | `:1631-1638` 的既有序列 + C# 的 `:243-246` 形状 |
| D2 | F07 的断言谓词可用性 | **实锤可访问**：`IsImplicitClass` 是 `NamedTypeSymbol.vb:717` 的 `Public Overridable` 属性 ⇒ 三处 `ContainingType` 直接用 `Not …IsImplicitClass`，无需备用判据 | `NamedTypeSymbol.vb:717`（收口轮实读）+ `ImplicitNamedTypeSymbol.vb:35` 已在用 |
| D3 | F07 的断言是否为上游行为 | **实锤 = 上游行为**：三处断言所在文件的最近一次提交都是 `e814cb1 add base compiler`（fork 导入基础编译器树）⇒ 断言随上游树进入、未被本地改过；故本地改动是「修上游在脚本模式下的可达缺陷」，已据此写入 `..\..\upstream-merge.md` §2.20 的登记理由 | `git log --oneline -1` 逐文件（收口轮实跑）+ 三文件均未见本地登记 |
| D4 | F09 的修法 | **B**（让发射门看见初始化器诊断），见 §F09 选型表 | `EmitExpression.vb:207` 的契约注释 + issue 09 的三候选定价 |
| D5 | F10 是否复用 walker 的全部检查 | **只做 await 位置检查**（开关关掉 On Error 报告） | 避免未要求的扩面污染回归面 |
| D6 | 两枚新码的码位 | F04 = 37005（`ERR_ExtensionMethodNotShared`）；F06 = 37341（`ERR_BadAwaitInSharedInitializer`，`ERR_NextAvailable` 推进为 37342） | 两份会议 RESOLUTION / OPEN QUESTIONS |

## §待定项（U1–U5 **均已有结论**：实测或显式记录不可达，无悬空项）

| # | 待定项 | 结论 |
|---|---|---|
| U1 | 共享**属性**初始化器隐式引用实例方法是否命中 BC30369（F08 的 Property 分支） | **结论：命中 BC30369**（走 Property 分支，无需补判据）。用例 `F08-L2-6`（`TopLevelSharedPropertyInitializer_CallingInstanceMethod_ReportsBadInstanceMemberAccess`）实测通过，位置 = 调用处标识符 `F` |
| U2 | 共享**数组上界**隐式初始化器路径能否出现 `Await`（F06 子情形 4） | **结论：可表达**。`Shared Dim arr(Await Task.FromResult(2)) As Integer` 报 **BC37341**（用例 `F06-L2-4` 通过；`.vbx` 探针同形同号），判据无须为这条路径开特例 |
| U3 | 多树提交（`#Load`）下 F10 的诊断只报一次且位置正确 | **结论：已收**。实现是「逐条顶层语句各走一次 walk，每条用其所在树的 `TopLevelCodeBinder`」⇒ 多树天然按树分界；用例 `F10-L3-3`（`TopLevelAwaitInCatch_AcrossLoadedFiles_IsReportedOncePerTree`，内存 `SourceReferenceResolver`，不落盘）实测通过：被载文件与主文件各一条 BC36943、不重复报 |
| U4 | F11 之后顶层数字行号标签在 `<Initialize>` 体里的发射形状 | **结论：未引入行为差异（判别力由新断言承担）**。数字行号标签与具名标签走同一条收集路（`:2621-2633` 的 `Case Else`）；`_containsLineNumberLabel` 的消费全在方法体路径的 `BindMethodBlock` 内（`Binder_Statements.vb:342` 的判据条件与 `:365` 传给 `BoundUnstructuredExceptionHandlingStatement` 的实参，两处决定是否包该语句），而 `<Initialize>` 的补跑 walk 是同文件 `:523-537` 的 `VisitBlockOnlyCheckAwaitInTryHandler`——它**忽略全部 out 参数** ⇒ 诊断不变。用例 `NumericLineNumberLabel_Emits`（`Compilers\VisualBasicEmitTest\Emit\SubmissionTopLevelLabelTests.vb`）在 `Emit` 成功之外**新增**一条判别性断言：重复数字行号标签报恰好一条 BC30094（标签语句被丢弃时该断言必然失败；同时锁定「数字行号标签与具名标签同号」） |
| U5 | HEAD 的 Release 构建行为（F07 的旁证升级） | **结论：已收**。HEAD（`d7b0fc8`）在独立 worktree（`git worktree add … --detach`，不触碰本工作树）构建 Release：0 错误；四探针实测——g12 exit 0（`EVENT-OK`）、g13 exit 34（`TypeLoadException`）、g1 exit 3（NRE @ `Core\CodeGen\BasicBlock.cs:325`）、g4 exit 0（`A`→`C`）⇒ F07 的旁证由较旧二进制 `e307d0f` 升级为 HEAD。同一组探针在**本任务修复后的工作树** Release 构建上全部 exit 0（g12 `EVENT-OK` / g13 `OK 5` / g1 `A`+`C` / g4 `A`+`C`） |
