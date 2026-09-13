# 脚本/提交类顶层成员隐式 `Shared` / Implicitly Shared Top-Level Members

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**提案内容**：脚本/提交类的顶层成员**默认按 `Shared`（共享）处理**——不写 `Shared` 的顶层 `Dim` / `Sub` / `Function` / `Property` 变成**共享成员**，而不是今天的**实例成员**。这等于把 `spec\spec-scripting-dialect.md:60` 的语义反过来。

**边界（先钉死，防止误读）**：**容器种类不变**。`TypeKind` / `DeclarationKind` / `SourceTypeFlags` **全不动**；仍是 `TypeKind.Submission` / `DeclarationKind.Submission`；不引入标准模块的任何附带语义；`Shared` 仍可写（写了也是共享，不是错误）；容器仍被实例化；顶层可执行语句仍进实例初始化器。

> **与已否决方向的分界**：把容器**种类改成 `Module`** 的方向（记为 ③）已被两次否决，理由包括「会激活约 60 处 `TypeKind.Module` 语义」「`TypeKind.Submission` 的 27 处派发点失明」「`IsSubmissionClass` 的 6 处消费点集体翻假」「反 D5」。**本提案一条都不触碰这些面**——见 §1 的「不碰什么」清单。复会请勿以 ③ 的否决理由拒本提案。

**一句话结论**：顶层「是实例还是共享」这件事，**在出货面上没有可观察差异**（§6），但它决定了「顶层扩展方法要不要手写一个 `Shared`」；而**默认共享达成这件事比另一条候选（①a）更省**（§2），因为它不需要越过「属性识别早于修饰符判定」这道顺序风险。

**本提案依赖两条已采纳的修法先落地**：`meeting-submission-shared-members.md` 裁定的 **甲**（按 `isShared` 分叉出独立的静态构造器符号）与 **乙**（移植 CS8100 语义加绑定期诊断）——原因见 §3。

## Motivation
[motivation]: #motivation

- **VB 用户的心智模型把顶层当成模块，而实现是类。** 经典 VBScript 只有全局变量与过程，**没有类成员这一层**；顶层「东西天然共享」是 VB 用户的历史直觉。实现上顶层代码装在一个**类**里，成员默认**实例**，于是写顶层扩展方法必须额外手写一个 `Shared`——这个别扭是用户直觉与实现模型的错配。本提案消掉这个错配。
- **它消掉的是①a 想消、但过不去的那道关。** 候选 ①a（给带 `<Extension>` 的顶层成员隐式 `Shared`）的唯一未解实现风险是**顺序**：修饰符判定在 `Binder_Utils.vb:85`（只收 token），属性识别经 `QuickAttributeChecker` 在符号构造期（`SourceMemberMethodSymbol.vb:85-89`），晚于前者。**本提案的注入点是容器**——`SourceMethodSymbol.vb:417-420` 的 `DecodeMethodModifiers(modifiers, container, …)` 里容器本就是入参——**不需要碰那个只收 token 的地方**（§2）。**即：为了同一个目标，本提案比 ①a 少一道风险。**
- **`spec` 已经把这件事写成可翻的一页。** `:60` 逐字承诺「顶层 `Sub`/`Function` 默认是**实例成员**」；`:48` 承诺「顶层成员是实例成员，**除非声明为 `Shared`**」。本提案改的是这两句，**不改声明模型、不改提交链、不改入口点形状**（§10 列出全部受影响句与不受影响句）。
- **本提案是 D6 的适用面。** `decisions.md` **D6** 定案「兼容性约束只对正式版（GA）成立；beta 期间不得以 breaking change / 破坏既有语义为由否决设计」，并附实证（只有 1.2 beta / `2.0.0-Beta`，全仓「正式版/GA」零命中）。**本提案正落在 D6 的适用区间内**——请复会遵守 D6 的边界：它只解掉「兼容性」一条否决理由，其余理由（可行性、机制收益与代价、与 D5 的同形性、规范可表达性）仍须逐条论证，本提案的 Drawbacks 就是按后者给的。
- **两条被当作反对理由的事项，实测后不成立**：
  1. **「`Shared` 会失去意义」**——差异只在**宿主 API 反复 `RunAsync` 同一个 `Script` 对象**时可观察，而 `.vbx` 每次是新进程、REPL 后产生的提交不重跑前序、`#Load` 多树合成**一个**提交；且作者已明确**不发布 API 版本的 vbscript.net**（§6）。
  2. **「`Shared ReadOnly` 救不了」**——`HasHome`（`EmitAddress.vb:261-284`）**只管「要地址」的形状**（`x += 1` / `ByRef` / `x(i) = v`），而普通初始化器走 `EmitFieldStore`，**根本不查 `HasHome`**；那些要地址的形状先被绑定层 BC30064 拦掉（`Binder_Expressions.vb:1802`）（§8）。

## Detailed design
[design]: #detailed-design

> **证据等级标注**：阶梯为 未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑。本节锚点均在工作树逐行复核，故为**已检查**；标「已运行」的为探针实测。/ 本阶段**未改编译器、未加单元测试**，故不出现「已采纳」「有结果支撑」。

### 0. 实证入口与材料来源

- 编译器：Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（`2.0.0-Beta+5816a5c`，当期工作树）。对照 Release `Interactive\vbi\bin\Release\net10.0\vbi.exe`（`e307d0f`，**较旧**，仅作参照）。
- 退出码语义（实测归纳）：`0` 正常 / `1` 报了编译错误 / `34` 宿主 `TypeLoadException` / `35` 断言终止 / `58` `InvalidProgramException`。
- **本提案的事实来源是十份调查材料**（`tmp\meetings\script-extension-methods\`，工作材料不入库）：`investigation-csharp-static-across-submissions.md`、`investigation-vb-submission-as-module.md`、`author-review-module-candidates.md`、`investigation-instance-vs-shared.md`、`investigation-default-shared-cost.md`、`investigation-shared-initializer-async.md`、`investigation-refstruct-static-field.md`、`investigation-toplevel-readonly.md`、`investigation-readonly-init-outside-ctor.md`、`investigation-toplevel-refstruct-two-modes.md`；另有 main 合成材料 `ruling-instance-vs-shared.md`。
- **交叉复核的必要性有先例**：这批材料互相复核时已实测出两处锚点漂移——`BindingDiagnosticBag.vb` 的 `GetInstance(template)` 实在 `:50-52`（有调查写成 `:58-60`，那是 `GetConcurrentInstance`）；`SynthesizedConstructorBase.vb` 的 `MethodKind` 承重行在 **`:192`**（验证者曾写 `:193`，`:193` 是 `End Get`）。故本提案的锚点按**复核后**的行号给出。
- 未跑项：**全部实测均来自当期 Debug 构建**，Release 未跑；**本树跑不了 C# 单测**（`Compilers/Test/Utilities/` 只有 `VisualBasic`），凡引 C# 测试均为「**读到断言文本**」而非跑过。

### 1. 本提案不碰什么（与 ③ 的分界，**已检查**）

默认共享只改**成员的默认共享性**这一轴。以下全部**不受影响**：

- `TypeKind` / `DeclarationKind` / `SourceTypeFlags` 的取值与映射（`SourceMemberContainerTypeSymbol.vb:147-180` 的 `Select Case` 一个字不动）。
- `TypeKind.Submission` 的 **27 处**派发点（`grep` 计数）与 `IsSubmissionClass` 的 **6 处**：
  - 跨提交查找入口 `Binder_Lookup.vb:580-584`、`:609-613`；
  - 前序提交引用绑定 `Binder_Expressions.vb:2611-2612`；
  - 提交字段合成 `MethodCompiler.vb:574`；
  - `Symbol.vb:625`、`SynthesizedSubmissionFields.vb:31`、`SynthesizedEntryPointSymbol.vb:320`。
- 扩展方法承载能力：`AllowsExtensionMethods`（`NamedTypeSymbolExtensions.vb:108-111`）与 `MightContainExtensionMethods`（`SourceMemberContainerTypeSymbol.vb:3336-3344`）**都不读成员的共享性**。
- 入口点形状：`<Initialize>`（`SynthesizedInteractiveInitializerMethod.vb:51-55` `IsAsync`、`:87-91` `IsShared`）、`<Factory>`（`SynthesizedEntryPointSymbol.vb:86-90`、`:319-321`、`:340-389`）、合成条件（`SourceMemberContainerTypeSymbol.vb:2761`）。
  - 注：`spec:137`/`:165` 已声明这两者的**形状**是实现细节、非契约，故即便动也不构成规范破坏；本提案不动。

### 2. 注入点：为什么比 ①a 省（**已检查**）

- ①a 的做法是「认出 `<Extension>` 属性 → 该成员按 `Shared` 处理」。它的判据必须落在**属性识别**处，而属性识别（`SourceMemberMethodSymbol.vb:85-89` 的 `QuickAttributeChecker`，符号构造期）**晚于**修饰符判定（`Binder_Utils.vb:85` 只收 token，语法阈值处翻译）——这是 ①a 唯一的实现风险，且需先做顺序 spike 才能证实。
- 本提案的判据落在**容器**：`SourceMethodSymbol.vb:417-420` 的 `DecodeMethodModifiers(modifiers, container, …)` 里**容器已是入参**，判断「容器是不是脚本/提交类」不需要新增信息通道。
- **附带收益**：默认共享之下，顶层 `<Extension>` 成员的 `Shared` 要求**自动满足**——`SourceMethodSymbol.vb:1504`/`:1634` 两处 `Debug.Assert(Me.IsShared)` 不再可能为假，issue 04（`issues\issue-script-top-level-extension-method-crash.md`）在 `.vbx`/REPL 形态上随之消失。

### 3. 依赖：issue 05 / 06 与「甲 + 乙」（**已检查 + 已运行**）

**默认共享会把 issue 05/06 从边角变成主路**，因为分流**只看 `IsShared`、不看用户写没写关键字**（`SourceMemberFieldSymbol.vb:628-632`、`:665-673`；`SourceMemberContainerTypeSymbol.vb:2669-2681`）。默认共享之下：

- **每一个**带初始化器的顶层 `Dim x = …` 都搬进静态桶 ⇒ 触发 **issue 05** 的宿主 `TypeLoadException`（`EXITCODE=34`）；
- 其中含 `Await` 的形状触发 **issue 06** 的编译器断言终止（`EXITCODE=35`）——**包括 `spec` 自带的示例 `spec-scripting-dialect.md:90-91` 的 `Dim value = Await …`**。

**⇒ 本提案被这两条硬阻塞，顺序是「先甲+乙，后本提案」。** `meeting-submission-shared-members.md` 已裁定采纳：

- **甲**＝按 `isShared` 在 `SourceMemberContainerTypeSymbol.vb:2726-2737` 分叉出**独立的静态构造器符号**（对齐 C# 的 `SynthesizedStaticConstructor`；`decisions.md` **D5 已证实例 1** 的逐点复制）。甲天然只动 `Submission`，非提交脚本类（`DeclarationKind.Script`）是会议列出的独立验证用例。
- **乙**＝移植 CS8100 语义加绑定期诊断，**目标定为「从崩改成报错」**；三条件：新脚本专属码 / 覆盖共享属性 / 改调用结构（提案 `proposal-submission-shared-members.md` 的硬事实：只往 `GetAwaitInNonAsyncError` 加分支**不可达**，因为共享字段时 `IsInAsyncContext()` 为真）。

甲落地后，顶层 `Shared` 字段/属性的初始化器可用；本提案再把它们普遍化到默认共享。

### 4. 宿主对象 `Print` / `Args`：编译期 BC30469，不是崩（**已运行**）

- `<host-object>` 是**实例字段**：`SynthesizedSubmissionFields.vb:57` 逐字 `isShared:=False`；赋值在 `SynthesizedSubmissionConstructorSymbol.vb:84-97`；绑定门槛 `Binder_Expressions.vb:2612` 的 `Not currentMember.IsShared`。
- 实测：`Shared Sub` 体内 `Print` → **BC30469**；`Args` → **BC30469**；实例 `Sub`（对照）→ 正常；`Shared` 方法之间互调 → 正常。
- **修法与硬点**：要把宿主对象字段**共享化**，且**必须同时放开 `isReadOnly:=True`**——静态只读字段只能在 `.cctor` 赋值，而宿主对象是**运行时**才到的。**这一处极易漏**（只改 `isShared` 不改 `isReadOnly`，会从 BC30469 变成写不进去的只读字段）。
- **代价**：宿主对象从「每次运行一份」变成「**类型级一份**」⇒ **并发 / 重入互相覆盖**（见 Drawbacks）。
- **注意这不是本提案独有的新增面**：`Binder_Expressions.vb:2612` 的判据方向与同一条边界的另一处相反——绑定期对脚本类的**任何**字段/属性都算 async 上下文（`Binder_Expressions.vb:4726-4727`，**不看** `IsShared`）。同一条边界两个方向相反的判据，说明「脚本类里的共享面」本就没有被统一铺开（另见 issue 08 引的示例注释 `Binder_Expressions.vb:2261` 逐字「No code in a script class is shared.」——该前提在本 fork 不成立）。

### 5. 宿主 API：零代码改动，但语义变（**已运行 + 推测**）

- `ScriptState` / `ScriptVariable` / `ScriptExecutionState` **不用改一行**：`ScriptState.cs:94-117` 的 `DeclaredFields` 本来就含静态字段；`ScriptVariable.cs:19-29`/`:51-72` 用 `_field.GetValue(_instance)`——**对静态字段传实例参数不抛**（已跑探针确认）。公开 API 签名一条不动（`PublicAPI.Shipped.txt:139`/`:142`/`:145-150`）。
- **但语义变**（**推测**，未在本提案形态上跑）：写值会影响此后所有运行、重跑**不回到初值**——**与仓内既有断言 `ScriptTests.vb:136-149` 相反**。**「那条测试要不要改」是一个明确的裁决点**（见 Unresolved 3）。

### 6. 「重置」的准确形状（**已检查 + 已运行**）

这一条此前被反复讲错，此处定死：

- **实例字段今天就是每次运行回初值。** `ScriptTests.vb:136-149` 逐字三条断言：写值生效（`Assert.Equal(2, …)`）/ **重跑回初值**（`Assert.Equal(1, rerunState.GetVariable("x").Value)`）/ 续跑看到写值（`Assert.Equal(2, continuedState.ReturnValue)`）。
- **所以「重置」不是默认共享新引入的毛病。** 默认共享之下，共享字段的初始化时机从 `.cctor`（一次）变成 `<Initialize>`（每次运行）——**而那正是实例字段今天的行为**。
- **「`Shared` 失去意义」的准确含义**是：`Shared` 与 `Dim` 在**可观察行为上没区别了**（只剩存储位置不同）。
- **而该差异只在宿主 API 反复 `RunAsync` 同一个 `Script` 对象时可观察**：
  - `.vbx` 文件执行：每次新进程、新编译、新脚本上下文 ⇒ 不会重跑；
  - REPL：后产生的提交是新类型、新 `<Initialize>`；前序提交不重跑；
  - `#Load` 多树：合成**一个**提交，`<Initialize>` **只跑一次**。
- **作者已明确「不发布 API 版本的 vbscript.net」** ⇒ 该差异在出货面上**无从观察**。

### 7. ref struct：**空结果**（**已检查 + 已运行**）

**这一轴此前被怀疑会「悄悄收紧顶层能声明什么」，实测为空结果。**

- C# 侧静态字段确实**更严**：`SourceMemberFieldSymbol.cs:67` 的判据含 `this.IsStatic` ⇒ **静态字段在任何宿主都报 CS8345，连 ref struct 内部也报**；`readonly` 不在判据里、不豁免。
- **但 VB 的判据 `SourceMemberFieldSymbol.vb:140-145` 只看类型、无 `Shared` 分支。** 实测（已运行）：`Dim f As Span(Of Integer)` 与 `Shared g As Span(Of Integer)` 报**同一条 BC31396**；`Shared t As Integer` 无错误。
- ⇒ 默认共享对顶层 ref struct 声明**既不变严也不变松**。
- **与 byref-like 既有定案不冲突**：定案的依据是「**字段**」而非「实例字段」——`spec\spec-byref-like-safety.md:107` 逐字「A top-level declaration of a byref-like type is an error (BC31396), **because the declaration would become a field of the script class and byref-like types cannot be fields.**」；同文件 `:237`「**Fields of classes and static fields cannot have a byref-like type**, because the field storage would live on the heap.」已把实例与静态并列。`meeting-byref-like-repl-safety.md:91` 的定案句一字不用改。
- **顺带澄清一条 u8 的印象**：C# 的 UTF-8 字面量字节确实进静态字段，但字段类型是**字节载体**（`int32`/`uint8`，RVA 数据），**不是 span** ⇒ **span 类型的静态字段从未放行**；`static readonly ReadOnlySpan/Span` 全仓零命中。

### 8. `readonly`：技术约束**全部查清，无硬墙**（**已运行 + 已检查**）

- **VB 脚本顶层 `ReadOnly x As Integer = 5` 今天就能跑**（实测 `EXITCODE=0`，`RO-INSTANCE 5`）；其 `stfld` 落在 `VB$StateMachine_1_<Initialize>.MoveNext` 的状态机里、**不在 `.cctor`**。**C# 同形**：`readonly int x = 5;` 在脚本里字段是 `Public, InitOnly`，`stfld` 同样落在 `<Initialize>` 状态机（`MethodCompiler.cs:1013-1021`；`<Initialize>` 的 `MethodKind` 是 `Ordinary`，`SynthesizedInteractiveInitializerMethod.cs:129-132`）。
- **IL 校验会判 1 条 `InitOnly`，但那是测试层**：`Microsoft.ILVerification` 9.0.7 只被 `Compilers/Test/Core/Microsoft.CodeAnalysis.Test.Utilities.csproj:119` 引用；`FailsPEVerify` 全仓 **782 处全在测试文件**、非测试目录**零命中**。**运行期不拦**（实测：含该违规的程序集 `Assembly.Load` 并执行成功；作者探针实跑 exit 0）。
- **`HasHome` 只管「要地址」的形状**：`EmitAddress.vb:261-284` 的判据只影响 `x += 1` / `ByRef` / `x(i) = v` 一类**需要地址**的形状，而那些形状**先被绑定层 BC30064 拦掉**（`Binder_Expressions.vb:1802`）。普通初始化器走 `EmitFieldStore`（`EmitExpression.vb:2193-2203`），**不查 `HasHome`**。
  > **更正**：此前把 `HasHome` 当作「共享只读字段的初始化器搬不出 `.cctor`」的机制障碍，**该依据有误**（`meeting-submission-shared-members.md` 否候选「丙」的第 2 条理由即建立在此）。据实更正。
- 反射（**旁证，不作结论依据**）：实例只读在 .NET 10 与 Framework 4.8 都能被 `FieldInfo.SetValue` 改；**静态只读在 .NET 10 抛 `FieldAccessException`**（Framework 4.8 成功）；`Unsafe.AsRef` 在 .NET 10 实测能绕过。
- **⇒ 默认共享不会遇到任何只读硬墙。**
- **作者裁定：不限制脚本顶层 `ReadOnly`**（2026-09-13）。曾备过一条「禁止顶层 `ReadOnly` 字段」的可选项，其立论前提是**共享字段的初始化器异步写只读字段做不到**；该前提已被本节推翻——异步初始化写只读字段**只是 IL 校验报错，CLR 不拦截、写入成功**（实测 exit 0）。前提消失，该项**撤回，不再作为备选**。

### 9. `Span` 的两条模式分野（**已运行 + 已检查**，供规范与诊断措辞参考）

- C# **顶层语句模式**（`SourceCodeKind.Regular` + `acceptSimpleProgram`）：顶层变量是**局部**（`SimpleProgramBinder.cs:26-39`）⇒ `Span` **可用**（实测三条全 `EXITCODE=0`：`Span<int> s = stackalloc int[4];`、`ReadOnlySpan<byte> u = "abc"u8;`、以及 `await Task.Yield(); Span<int> s2 = stackalloc int[4];`）。
- C# **脚本模式**（csx）：顶层变量是**字段** ⇒ 报 **CS8345**（`ERR_FieldAutoPropCantBeByRefLike` = 8345，`ErrorCode.cs:1533`）。
- **根源一条**：同一条 ref-like 检查**只挂在「字段」上**。
- **一条已推翻的推测（不要沿用）**：**async 里可以有 ref struct 局部，只要不跨 await**；跨了才报 **CS4007**（`IteratorAndAsyncCaptureWalker.cs:91-94`、`ErrorCode.cs:1109`）。⇒ 「`<Initialize>` 恒 async 会双重挡住 ref struct」**不成立**。
- **VB 没有「顶层当局部」那条路**（独立验证，非转述）：`grep SimpleProgram` 在 VB 全树 **零命中**；实测同一段 `Dim x As Integer = 5`——Script 模式 0 诊断，Regular 模式 `error BC30001: 语句在命名空间中无效`。

### 10. 规范影响面（**已检查**）

**必改 6 句**：`spec-scripting-dialect.md:16`（映射表 `Sub`/`Function` 行）、`:48`（**仅后半**：「instance members unless declared `Shared`」+ `<Extension>` 那句的 `Shared`）、`:58`、`:60`（整段）、`:64`、`:66`（作用域不进入嵌套类型）。
**另加**：`:176`（async 上下文）、`:311`（对照表行）、示例 `:90-91`。
**建议增补**：`:231-239`（诊断族表）、`:284-286`、`:310`。
**不动**：`:131`、`:137`、`:163`、`:165`、`:234`、`:243`。
**注意**：`:48` 的「a script class is instantiated」**不能删**（`<Factory>`/`<Main>` 仍 new 实例）；本提案不含「状态静态化」（那已被否决）。

### 11. 上游登记面（**已检查**）

本提案若落地，改动面落在 `SourceMemberFieldSymbol.vb` / `SourceMemberMethodSymbol.vb` / 绑定层。登记判定方式照 `proposal-submission-shared-members.md` §7b：**须在合并前逐条补登记**，并注意 `upstream-merge.md` 现有的「**二·补、已知欠账**」节——该节的条目在合并前必须逐 diff 复核后补登记，本提案的改动面若与之重叠，一并处理。

### 12. 测试策略（含作者指定的一项）

- **必须避开 `Object` 接收者盲区**：接收者为 `Object` 时，`Binder_Lookup.vb:1178` 的 `Not container.IsObjectType()` 把 `Object` 排除出扩展查找，加上宿主写死的 `Option Strict Off`（`VisualBasicScriptCompiler.vb:218`），「找不到成员」会**静默降级为晚绑定**——既有测试 `ExtensionMethodTests.vb` 的 `InteractiveExtensionMethods` 正是这样**空转**的。验收用例必须用**非 `Object`** 接收者、**跨提交调用并断言返回值**，再加一条负例。
- **作者指定：把已知未通过校验的形状设为断言测试。** 即对「预期不被 PEVerify 通过」的形状，用 `Verification.FailsPEVerify` / `ILVerifyMessage` 那套**显式断言**把它锁住，**而不是留作意外**。已确认的适用形状至少包括：脚本顶层 `ReadOnly` 字段（`stfld` 落在非构造器的状态机里，ILVerify 判 1 条 `InitOnly`）。既有先例锚点：`Compilers\CSharp\Test\Emit\CodeGen\CodeGenScriptTests.cs:557`（脚本提交测试本来就写 `verify: Verification.FailsPEVerify`），全仓同类用法 **782 处**。
- **无副作用纪律**：单元测试禁网络请求 / 文件写入 / 启动进程 / 注册表写入；脚本用例用内存 `StringReader`/`StringWriter`。

## Drawbacks
[drawbacks]: #drawbacks

- **宿主对象变成类型级一份。** 修 `Print`/`Args` 必须把 `<host-object>` 字段共享化并放开 `isReadOnly`（§4）。代价是宿主对象从「每次运行一份」变成「类型级一份」⇒ **并发 / 重入互相覆盖**。仓内现有用法（`.vbx` 单次运行、REPL 单线程提交）不触发，但这是**语义面的真实收窄**，不是零代价。
- **宿主 API 的语义变，且与仓内既有断言相反。** `ScriptTests.vb:136-149` 的「重跑回初值」在默认共享之下对原本共享的字段不再成立（§5）。虽然该场景在出货面上无从观察（§6），但**测试与语义要走一致**——要么改测试、要么把该断言收窄到实例字段。**这是一个需要显式裁决的点**（Unresolved 3）。
- **`spec:66` 的作用域会反转**：嵌套类里的代码将能**不限定名**读到顶层成员（**有已运行的代理实验**：顶层 `Shared` 被嵌套类读到 `99`；同形实例字段报 BC30469）。这是模块语义的正常结果，但相对今天是一处**放宽**，规范要写明。
- **`spec:64` 的顺序保证与 `ScriptVariable` 语义变宽**是**推测**（未在本提案形态上跑）：前者指共享初始化器改道后与实例初始化器的先后，后者指共享字段被宿主 API 写入后的可见范围。落地前必须补跑。
- **与 D5 的分叉。** C# 顶层**默认实例**（写 `static` 才显式共享）。本提案是**主动分叉**，须按 D5 的落地约束说明「为什么 VB 必须分叉」——本提案给出的 VB 特有理由是**用户心智模型**（经典 VBScript 无类成员层，顶层天然共享；§Motivation），而**不是**「C# 做不到」。**D5 明确把「VB 有 C# 无的概念：`Module`、`Shared`」列为要求论证的义务条款而非许可清单**，故该理由须由会议独立裁量其充分性。
- **落地依赖前置。** 本提案必须在「甲 + 乙」之后（§3），否则顶层每一个带初始化器的 `Dim` 都会崩或报错。

## Alternatives
[alternatives]: #alternatives

### A. 维持现状（顶层 = 实例）——**当前的基线**

- 代价：顶层扩展方法**永久**必须手写 `Shared`；用户心智错配保留；需靠叙事层（呈现/规范措辞/诊断文案）缓解。
- 收益：零机制工作；与 csi 同形；`spec` 现文不动；`<Initialize>` / `<Factory>` / 宿主契约不动。
- **注**：两次会议都未能为「顶层必须是实例」举出任何**现实用户用例**——`investigation-instance-vs-shared.md` 穷举的甲类（有真实用户用例会坏）3 条 + 1 条准，**没有一条的形状是「用户为了用实例成员而必须留实例」**，全部是缺陷（issue 05 / 06 / 07 / 08 / 09 族）。

### B. 顶层成员默认共享（**本提案**）

见 Summary 与 Detailed design。承重锚点：§2（注入点）、§3（依赖）、§4–§9（各轴代价）。

### C. 只对带 `<Extension>` 的顶层成员隐式 `Shared`（①a，即此前的候选 G）

- **买到**：与 B 相同的直接收益（顶层扩展方法不必写 `Shared`）；对缺口①②③④零收益。
- **代价**：唯一风险是**顺序**——属性识别（`SourceMemberMethodSymbol.vb:85-89`）晚于修饰符判定（`Binder_Utils.vb:85` 只收 token）。**必须先做顺序 spike 才能定其可行性。**
- **与 B 的关系**：**B 用更少的风险达成同一目标**（§2）。若 B 被采纳，①a 不再必要；若 B 被否，①a 是次优选择。

### D. 把容器种类改成 `Module`（③）——**已两次否决，本提案不采用**

- 否决理由（`meeting-script-extension-methods.md`）：甲（只改 `DeclarationTreeBuilder.vb:140`）源码级不可行——真正的致命点是 `IsScriptClass` 变假 ⇒ `MethodCompiler.vb:564` 整块不执行 ⇒ `<Initialize>` / `<Factory>` / 提交构造器**全不合成**；乙（完整转换）是「第三种容器」的语义换血；且跨提交入口按 `TypeKind` 分派，比已否的「窄 D」更重；并与 D5 反向。
- **本提案与 ③ 的分界见 §1**——请勿以 ③ 的否决理由拒本提案。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **`Print` / `Args` 的共享化形态。** 是把 `<host-object>` 字段改成共享（并放开 `isReadOnly`），还是另立一个共享入口？前者改动小但把宿主对象变成类型级一份（并发/重入）；后者要新造机制。**须在落地前定，并给出并发/重入的可观察后果。**
2. **宿主 API 语义变的处置。** `ScriptTests.vb:136-149` 的「重跑回初值」对共享字段不再成立——**改测试**还是**把断言收窄到实例字段**？（作者已明确不发布 API 版本，可作裁决输入。）
3. **`spec:64` 的顺序保证是否失守。** 共享初始化器改道后与实例初始化器、顶层语句的先后关系**未跑**（推测）。须补跑并给出规范措辞。
4. **`spec:66` 反转后的作用域规则措辞。** 嵌套类不限定名读顶层成员——规范要写成明文规则（有已运行的代理实验可作依据）。
5. **非提交脚本类（`DeclarationKind.Script`）是否一并改。** 本提案的注入点落在容器上，天然会同时命中非提交脚本类；而该形态今天**健康**。须给出独立验证用例，并决定是「一并改」还是「只改 `Submission`」。
6. **宿主对象的并发/重入语义**（与 1 相关）：类型级一份之后，REPL 的长会话、异步提交、以及同进程多脚本场景的可见性边界须定。
7. **与 D5 的关系须由会议裁量。** 本提案是主动分叉；VB 特有理由（用户心智模型）是否足以满足 D5 的「说明为什么 VB 必须分叉」，**不是本提案能自证的**。

## 全称主张自检

> 纪律：全文每一个全称主张（无 / 都 / 任何 / 唯一 / 不可能 / 零命中）逐条给 `文件:行号` 或实测；举不出证据即降级。

| # | 主张 | 证据 | 判定 |
|---|---|---|---|
| 1 | `TypeKind.Submission` 派发点 **27 处**、`IsSubmissionClass` **6 处** | `grep` 计数（`investigation-default-shared-cost.md`） | **已检查**（`grep`） |
| 2 | `AllowsExtensionMethods` 与 `MightContainExtensionMethods` **都不读**成员共享性 | `NamedTypeSymbolExtensions.vb:108-111`、`SourceMemberContainerTypeSymbol.vb:3336-3344` | **已检查** |
| 3 | `Microsoft.ILVerification` **只被**测试工具项目引用 | 全仓 csproj/props/targets grep **唯一命中** `Compilers/Test/Core/Microsoft.CodeAnalysis.Test.Utilities.csproj:119` | **已检查**（`grep`） |
| 4 | `FailsPEVerify` 全仓 **782 处全在测试文件**、非测试目录**零命中** | `grep` 计数 + 排除 `test` 后零命中 | **已检查**（`grep`） |
| 5 | C# 静态字段**在任何宿主都报** CS8345 | `SourceMemberFieldSymbol.cs:67` 判据含 `this.IsStatic` | **已检查**；**C# 测试未跑**（本树无 C# 测试工具） |
| 6 | `static readonly ReadOnlySpan/Span` 全仓**零命中** | `grep` | **已检查**（`grep`）；「u8 字节进静态字段但类型是字节载体」为**推测**（未实测 IL） |
| 7 | VB 脚本专属族**没有一条**是成员修饰符形状 | 族内逐条报点已列；全树 **29 处 `IsScriptClass` 逐条看过无一修饰符合法性检查**；`SourceMemberFieldSymbol.vb` 里 `IsScriptClass`/`Script` **零命中** | **已检查** |
| 8 | `grep SimpleProgram` 在 VB 全树 **零命中** | `grep` | **已检查**（`grep`） |
| 9 | 默认共享「在出货面上**无从观察**」 | 三个场景逐个给机制依据（§6）；`TestOptions.Script` 重跑语义有 `ScriptTests.vb:136-149` 锚点 | **已检查**；「出货面上」这一范围判断以作者声明「不发布 API 版本」为前提 |
| 10 | `<host-object>` 是实例字段 | `SynthesizedSubmissionFields.vb:57` 逐字 `isShared:=False` | **已检查**（逐字） |
| 11 | `HasHome` **只管**要地址的形状 | `EmitAddress.vb:261-284` 的 6 档判据；`EmitFieldStore` 在 `EmitExpression.vb:2193-2203` 无该检查 | **已检查**；「绕过后的实际表现」为**推测**（未实测） |
| 12 | IL 校验**运行期不拦** | 实测：程序集 `Assembly.Load` 并执行成功；作者探针 `ReadOnly x = 5` 实跑 exit 0 | **已运行** |
| 13 | **实锤**项（实测）：VB 顶层 `ReadOnly x = 5` 可跑 / 两形态同 BC31396 / `Shared Print` → BC30469 / 顶层语句模式 `Span` 三条 exit 0 / csx `Span` → CS8345 / Regular 模式 `Dim x` → BC30001 | 各自探针 | **已运行** |
| 14 | 「`<Initialize>` 恒 async 会双重挡 ref struct」 | **已被实测推翻**（CS4007 只在跨 await 时发） | 已删除该主张 |
| 15 | 「`HasHome` 挡住共享只读初始化器」 | **依据有误，已更正**（§8） | 已更正 |

**降级为「推测」的项**（正文均已标注）：宿主 API 语义变的实际表现（§5）、`spec:64` 顺序失守（Drawbacks）、`spec:66` 反转的规范后果（代理实验为已运行，规范措辞未定）、u8 发射形状的细节。

**未复现**：Release 构建行为（全部只跑 Debug）；本提案形态本身的端到端行为（本阶段未改编译器）；C# 侧实测（本树无 C# 运行环境，凡引 C# 测试为「读到断言文本」）。

## 相关文档

- `proposals\proposal-submission-shared-members.md` — **前置**：issue 05–09 的修法（甲 + 乙），本提案依赖其落地。
- `proposals\proposal-scripting-dialect.md` / `spec\spec-scripting-dialect.md` — 被改写的语义（`:16` / `:48` / `:58` / `:60` / `:64` / `:66` 等）。
- `proposals\proposal-script-extension-methods.md` / `meetings\meeting-script-extension-methods.md` — 顶层扩展方法与 `<Extension>` 的既有裁决；本提案消掉其中「必须手写 `Shared`」这一条。
- `spec\spec-byref-like-safety.md`（`:107` / `:237`）与 `meetings\meeting-byref-like-repl-safety.md`（`:91` / `:113`）— ref struct 与顶层声明的既有定案（§7）。
- `decisions.md` **D5**（以 C#/csi 为蓝本、须说明分叉理由）与 **D6**（兼容性只对 GA 成立）。**按节号引用，不按行号。**
- `issues\` 05 / 06 / 07 / 08 / 09 — 本提案依赖的缺陷族。
- 工作材料（`tmp\`，不入库）：十份 `investigation-*.md` 与 main 合成材料 `ruling-instance-vs-shared.md`（均在 `tmp\meetings\script-extension-methods\`）。
- 上游合并面：`upstream-merge.md`（含「二·补、已知欠账」节）。
