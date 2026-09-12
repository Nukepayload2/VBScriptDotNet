# [BUG] 提交里的顶层 `Shared` 字段带初始化器 → 宿主 `TypeLoadException`（共享提交构造器被塞进实例初始化体）

- **状态**：Open
- **发现**：2026-09-10（设计讨论「脚本类的 `Shared` 成员」时探针附带发现）
- **严重度**：高（用户可达、无任何编译诊断、宿主抛未处理异常并打印完整栈；`.vbx` 脚本与 vbi REPL 两条路径都在内）
- **影响面**：任何 `TypeKind.Submission` 编译（`.vbx` 脚本执行、vbi REPL 提交）；`vbc` 不产生提交编译，不受影响
- **版本**：分支 `with-modified-vbsyntax` 工作树，Debug 编译器 `2.0.0-Beta+5816a5c`（2026-09-10）

## 复现步骤

写入 `probe.vbx`：

```vb
Shared sx As Integer = 5
```

运行 `vbi probe.vbx`。（无顶层语句也崩；加不加语句无关。）

## 实际

```
System.TypeLoadException: Could not load type 'Submission#0' from assembly
'?*<guid>#1-0, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null'.
  + System.Reflection.RuntimeAssembly.GetTypeCore(...)
  + Microsoft.CodeAnalysis.Scripting.ScriptBuilder.GetEntryPointRuntimeMethod(IMethodSymbol, Assembly)
      在 Scripting\Core\ScriptBuilder.cs : 194
```

**编译期零诊断**，宿主在取入口点时才发现类型装不上。

## 触发边界（已运行实证，2026-09-10，Debug 版 `vbi.exe` 直跑 `.vbx`）

| 顶层写法 | 结果 |
|---|---|
| `Shared Sub S()` + 调用 | ✅ 正常 |
| `Shared sx As Integer`（**无**初始化器） | ✅ 正常 |
| `Shared sx As Integer = 5`（**带**初始化器） | ❌ TypeLoadException |
| `Dim iy As Integer = 7`（实例字段带初始化器） | ✅ 正常 |
| `Module M` + 调用（无 Shared 字段） | ✅ 正常 |

**触发条件 = 顶层 `Shared` 字段 ∧ 有初始化器。** 与 module、与顶层语句均无关（上表逐项隔离）。

## 预期

要么正常工作（顶层 `Shared` 字段初始化器在共享构造器里执行），要么给出编译诊断。现状是**两者皆无**——编译通过、宿主崩。

## 根因（源码核实；「非法 IL → TypeLoadException」一环为**推测**）

**链条三环**，逐环 Read 本工作树：

1. **submission 的共享分支也会造提交构造器**：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737`

   ```vb
   If TypeKind = TypeKind.Submission Then
       ' Only add a constructor if it is not shared OR if there are shared initializers
       If Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(initializers, False) Then
           Dim constructor As New SynthesizedSubmissionConstructorSymbol(syntaxRef, Me, isShared, binder, diagnostics)
           AddMember(constructor, binder, members, omitDiagnostics:=False)
   ```

   `isShared` 原样透传。**证据等级：已检查。**

2. **该符号无条件带一个 `submissionArray As Object()` 形参**：`Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:31-38`

   ```vb
   Dim submissionArrayType = compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_Object))
   diagnostics.Add(submissionArrayType.GetUseSiteInfo(), NoLocation.Singleton)

   _parameters = ImmutableArray.Create(Of ParameterSymbol)(
       New SynthesizedParameterSymbol(Me, submissionArrayType, 0, isByRef:=False, name:="submissionArray"))
   ```

   形参表**不查 `isShared`**（`Parameters` 于 `:40-44` 直接回吐 `_parameters`）。**证据等级：已检查。**

3. **该符号被当作 `.cctor` 发射**：`Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb:190-194`

   ```vb
   Public NotOverridable Overrides ReadOnly Property MethodKind As MethodKind
       Get
           Return If(m_isShared, MethodKind.SharedConstructor, MethodKind.Constructor)
       End Get
   End Property
   ```

   共享时 `MethodKind = SharedConstructor`、`Name` 为 `.cctor`（`:59-63`）。于是一个 **`.cctor` 带着一个参数**被发射。

**推测**：带形参的 `.cctor` 是非法元数据，类型因此无法加载 —— 与 `Assembly.GetType` 抛 `TypeLoadException`（而非纯「找不到类型」）的症状一致。

### 更正：先前版本写错的环节

本 issue 初版把根因写成「共享构造器被注入含 `Me` 的实例初始化体」。**该环节不成立**，已删：

- `Symbols\MethodSymbol.vb:516-520` 的 `IsScriptConstructor` 要求 `MethodKind = MethodKind.Constructor`；
- 共享构造器的 `MethodKind` 是 `SharedConstructor`（上环 3），故 `:528-532` 的 `IsSubmissionConstructor` 为**假**；
- 于是 `Compilation\MethodCompiler.vb:1535-1537` 的 `If(method.IsSubmissionConstructor, MakeSubmissionInitialization(...), ...)` **不会**给它挂提交初始化体。

正确结论是：**问题出在「这个共享构造器被造出来了、且带着实例版形参」，不出在它的方法体。**

## C# 对照：csi 为什么没这个问题（证据等级：**已检查**）

C# 把两件事拆成两个**不同的类**，VB 共用了同一个：

| | C# | VB |
|---|---|---|
| 实例构造器 | `SynthesizedSubmissionConstructor`，且 **`Inherits SynthesizedInstanceConstructor`**（`Symbols\Synthesized\SynthesizedSubmissionConstructor.cs:12`），只在实例分支创建（`Symbols\Source\SourceMemberContainerSymbol.cs:5701-5708`） | `SynthesizedSubmissionConstructorSymbol`，`isShared` 透传，两条分支共用一个类 |
| 静态初始化器的构造器 | **`SynthesizedStaticConstructor`**（另一个类，无参、无提交初始化体），独立一支（`SourceMemberContainerSymbol.cs:5714-5719`） | 无对应；submission 走上面那个类 |

C# 的 `MakeSubmissionInitialization` 调用点形状与 VB 相同（`Compiler\MethodBodySynthesizer.cs:64-67`），但 `SynthesizedStaticConstructor` 既不继承实例构造器、也不带形参，所以这条路在 C# 里根本走不到。

**即：VB 的 submission 分支绕开了常规的共享构造器符号（`EnsureCtor` 用的是 `SynthesizedConstructorSymbol`，见 `SourceMemberContainerTypeSymbol.vb:2800`），改用一个为实例版设计的符号，而该符号的形参表没有跟着 `isShared` 分叉。**

## 修复方向（候选，未拍板）

- **A. 共享时不带形参**：`SynthesizedSubmissionConstructorSymbol` 的 `_parameters` 按 `isShared` 分叉（共享时空数组）。改动最小，但共享构造器仍由「提交构造器符号」担任。
- **B. 共享时改用常规符号**：`AddDefaultConstructorIfNeeded` 的 submission 分支在 `isShared` 时走 `EnsureCtor` 那条路（同 C# 的两类分立）。
- 两者都需评估：共享构造器的 `MakeSubmissionInitialization` 不再被调用后，`<submissionArray>[slot] = Me` 这类实例登记是否仍由实例构造器承担（应当仍由实例路径承担）；以及 `AddWithEventsHookupConstructorsIfNeeded` 在 `TypeKind.Submission` 分支的 TODO（`SourceMemberContainerTypeSymbol.vb:2804-2806`）与本处的关系。

### 补充（2026-09-12）：候选重排，另有「取消本形状」的修法

上述 A / B 都是「修好这个共享构造器符号」的路线。**另有一条更上游的路**：**把共享字段/属性的初始化器改道并入已有的异步 `<Initialize>`**（不再为它们合成共享构造器）——那样 `AddDefaultConstructorIfNeeded` 的共享分支条件（`:2729`）不成立，**本 issue 直接消失**。该路线记在 `proposals\proposal-submission-shared-members.md` 的 **丙（主候选）**，其发射层可行性由 `Lowering\LocalRewriter\LocalRewriter_FieldOrPropertyInitializer.vb:47-54`（共享字段按 `stsfld` 赋值，不要求宿主方法共享）与 `CodeGen\EmitExpression.vb:669-688`（`EmitFieldLoad` 按 `field.IsShared` 分派静态字段装载、根本不触接收者）支撑，且经判别性实证（静态桶空 → exit 0；仅有数组上界条目 → exit 34）；宿主契约零改动（`GetSubmissionInitializer` 无调用方）。
⇒ **裁决顺序**：丙 若采纳，本 issue 连同 A/B 一并作废；丙 被否时，A/B 仍是最小修法。本 issue 正文（症状 / 根因 / 预期）不受影响。

## 与在办提案的关系

`InternalDevDocs\proposals\proposal-script-extension-methods.md` 的候选 B 依赖「脚本类里的 `Shared` 成员是健康的」。本缺陷说明：**`Shared` 方法健康，`Shared` 字段带初始化器不健康**——同一片区域还有洞，会议裁决时不应假定该区域整体可用。候选 D 亦触及同一 `TypeKind.Submission` 分支。

## 相关

- `InternalDevDocs\issues\issue-script-top-level-extension-method-crash.md`——同轮发现，同属「脚本类 `Shared`/扩展方法区域」。
- `InternalDevDocs\issues\issue-vbi-imports-switch-nre.md`——同一「宿主路径无守卫」形状的前例。
- `Scripting\Core\ScriptBuilder.cs:68-75`（`GenerateSubmissionId` 产出 `Submission#N` 与程序集名）、`:189-196`（`GetEntryPointRuntimeMethod` 报错点）。
