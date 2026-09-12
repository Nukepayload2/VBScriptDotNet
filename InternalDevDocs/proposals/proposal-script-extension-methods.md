# 脚本类中扩展方法的合法形式与缺失诊断 / Extension Methods in Script Classes

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

脚本模式下**只有两种类型可以承载扩展方法**：标准模块（`TypeKind.Module`）与脚本类（`IsScriptClass`）——判据是全局唯一的 `AllowsExtensionMethods`（`Compilers\VisualBasic\Portable\Symbols\NamedTypeSymbolExtensions.vb:108-111`：`container.TypeKind = TypeKind.Module OrElse container.IsScriptClass`）。这个白名单在脚本模式下暴露三处缺口，且三处互相纠缠：

1. **脚本类的非 `Shared` 成员被当成扩展方法却没有诊断。** `<Extension> Function`（漏 `Shared`）在脚本顶层是普通用户可写的输入，Debug 构建**进程被 `Debug.Assert(Me.IsShared)` 终止**（`Symbols\Source\SourceMethodSymbol.vb:1504`），Release 构建越过断言后在 codegen 抛 `NullReferenceException`（`CodeGen\Optimizer\StackScheduler.Analyzer.vb:663`）。
2. **脚本顶层的 `Module` 被降级成脚本类的嵌套类型**，其中的扩展方法**静默失效**：调用点只报 BC30456，不报任何扩展方法相关诊断；**连模块自己的方法体内都取不到自己声明的扩展方法**。
3. **非 `Shared` 声明一旦放行，降级期产出的是「静态调用形状」的 bound tree**（接收者被搬进实参表、`BoundCall.ReceiverOpt` 清空），而实例成员声明要求「实例调用形状」——两种形状在同一 `BoundCall` 上不可同时成立。这是候选 A（「给脚本类开口子、让非 `Shared` 成员也当扩展方法」）的硬障碍。
4. **承载在脚本类里的扩展方法跨提交不可用**：REPL 提交 2 声明 `<Extension> Shared Function Twice`，提交 3 调 `"abc".Twice()` 报 BC30456；而同一场景下跨提交的**普通成员**（`Class C` / `C.F()`）正常可用。即提交链遍历**只实现了「成员查找」这一半**，扩展方法收集没有对应的链节点（VB 自己在上游注释与本仓 TODO 里自陈该缺口）。该缺口对既有测试**不可见**：两个正向测试用 `Object` 接收者，而 `Object` 接收者被排除出扩展查找（`Binder_Lookup.vb:1178`），叠加 Option Strict Off 的静默晚绑定后，「找不到扩展」与「找到扩展」同样零诊断（§6.1.1）。

本提案钉住这四件事的事实基线，并把**十一条候选**（组 ① 面向缺口 1：A / B / C / G；组 ② 面向缺口 2：F / 窄 D / D / E-a / E-b / E-c；组 ③ 面向缺口 4：H）的收益、代价与实证逐条列出，供 LDM 会议裁定取舍。

## Motivation
[motivation]: #motivation

- **能力是真的、用户可达、且已经能用一半。** 脚本顶层 `<Extension> Shared Function Twice(s As String) As String` 后 `"abc".Twice()` 输出 `abcabc`（已运行，§1 p1）。这不是「要不要新增能力」，而是「既有能力的边界与把关点没写清楚」。**但要注意现有测试的成色**：编译器级两个正向测试（`ExtensionMethodTests.vb:2427-2440` 的 `ScriptExtensionMethods`、`:2443-2481` 的 `InteractiveExtensionMethods`）都用 `Object` 接收者，而 VB 在 `Binder_Lookup.vb:1178` 把 `Object` 接收者**排除出扩展查找**，再叠加 Option Strict Off 的静默晚绑定，二者使这两个测试**无法分辨扩展是否真的被解析**（p13/p13d/p14 实测，详见 §6.1.1）——这既解释了缺口长期未被发现，也说明「有正向测试」不等于「能力被验证」。
- **最坏失败模式是「编译器进程被断言终止」，不是「报错」。** 输入是最普通不过的顶层声明；凡走 Scripting 宿主的路径都受影响（`.vbx` 执行、vbi REPL、`Script` API 消费者已实测复现）。普通 VB 的 `vbc` 常规编译不产生脚本类，不受影响。详细登记见 `issues\issue-script-top-level-extension-method-crash.md`。
- **C# 已经解过同一个问题，VB 是移植不完整的半边。** C# 有三段显式检查（嵌套类型 → `ERR_ExtensionMethodsDecl`；容器白名单 → `ERR_BadExtensionAgg`(CS1106)；**`else if (!IsStatic)` → `ERR_BadExtensionMeth`(CS1105)**，`Compilers\CSharp\Portable\Symbols\Source\SourceOrdinaryMethodSymbol.cs:230-246`），且**显式豁免脚本类**（`:234` 的 `!ContainingType.IsScriptClass`），并有正反两面测试（`Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs:1104-1119`、`Compilers\CSharp\Test\Symbol\Symbols\ExtensionMethodTests.cs:3790-3801`）。VB 侧搬了容器白名单与嵌套诊断，**唯独把「必须 `Shared`」从用户可见诊断降级成 `Debug.Assert`**（`SourceMethodSymbol.vb:1504`、`:1634`），并且**与缺口 1 对应的负向测试为零**（今天不存在该诊断，故无从测起）。需要说明范围：VB 侧**并非**没有扩展方法的负向测试——`SymbolErrorTests.vb:17292`（BC36551）与 `:17315`（BC36552）都有，缺的只是「非 `Shared` 成员」这一条的（对比 C# 的 CS1105 负向测试 `ScriptSemanticsTests.cs:1112-1118`）。
- **脚本方言把「顶层 `Module`」这一合法 VB 构造悄悄降级了。** 常规 VB 里写在文件顶层的 `Module M` 落在**命名空间层**（`Declarations\DeclarationTreeBuilder.vb:66-76` 的 `VisitNamespaceChildren`，由 `:199-201` 的 `Regular` 分支调用），扩展方法可用；脚本模式里同一个 `Module` 被塞进脚本类成为**嵌套类型**（`DeclarationTreeBuilder.vb:179-196`，与 C# `.csx` 的 `CreateScriptRootDeclaration` 同构），于是**同一段文本在不同模式下可编译性不同**——这与脚本方言 spec 自己立的原则冲突：`spec\spec-scripting-dialect.md:33`「The dialect does not add a second way to declare anything」、`:35`「only their container changes」。它改的不只是容器，还改掉了「这个容器能不能承载扩展方法」。
- **常规 VB 里用户根本写不出「嵌套的 `Module`」，脚本模式却由编译器造了出来。** 模块错位于非文件/命名空间层是**语法错误**：`Parser\BlockContexts\DeclarationContext.vb:54-63` 对 `SyntaxKind.ModuleStatement` 直接报 **BC30617**（`ERR_ModuleNotAtNamespace`，`Errors.vb:469`；消息「'Module' statements can occur only at file or namespace level.」，`VBResources.resx:1325-1327`），而同一处的 `ClassStatement`/`StructureStatement`/`EnumStatement` 是合法嵌套。也就是说：脚本里顶层 `Module` 在**解析期**满足「文件级」要求，**降级发生在声明树构造期**（`DeclarationTreeBuilder.vb:187`），于是 Language 层明令禁止的状态被语义层无声地制造出来，其扩展方法随之不可达（p3/p6）。
- **规范必须落笔。** `spec\spec-scripting-dialect.md:48`（脚本类是可承载扩展方法的两种类型之一、嵌套类报 BC36551）、`:60`（顶层 `Sub`/`Function` 默认是实例成员，`Shared` 是扩展方法唯一形式）、`:62`（顶层类型是嵌套类型）三句话已把现状写进规范，本缺陷正是它们之间的空洞；`vblang\spec\type-members.md:908`「They can only be declared in standard modules」与 `vblang\spec\types.md:689`「a type whose members are ... scoped to the declaration space of the standard module's containing namespace」是本问题在标准规范层的出处。
- **能力即便可承载，跨提交也用不了——而 C# 侧是可用的。** REPL 提交 2 声明 `<Extension> Shared Function Twice`，提交 3 调 `"abc".Twice()` 报 BC30456（已运行，p11）；同形状的普通成员跨提交正常（p12）。C# 把提交链遍历做成 binder 节点（`InSubmissionClassBinder`，`Compilers\CSharp\Portable\Binder\InSubmissionClassBinder.cs:34-40`）并有跨提交测试（`Compilers\CSharp\Test\Symbol\Symbols\ExtensionMethodTests.cs:3804-3830`），VB 则把提交链遍历只放进成员查找、在两处留下同一句 `' TODO (tomat): extension methods`（`Binder_Lookup.vb:932`、`:2050`）。按 `decisions.md:35` 的 D5（基础功能以 C#/csi 为蓝本、须说明「为什么 VB 必须分叉」），本项**解释不了**——缺口性质是移植不完整（D5 的判定口径）。

## Detailed design
[design]: #detailed-design

> **证据等级**：阶梯为 未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑。源码结论逐行复核故为**已检查**；探针结论为**已运行**。
> **断言三态**：实锤 / 推测 / 猜测，未标注按猜测处理。

### 1. 事实基线：十四项探针

运行环境：Debug 版 `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（版本 `2.0.0-Beta+5816a5c`，2026-09-10 构建）；Release 对照 `Interactive\vbi\bin\Release\net10.0\vbi.exe`（版本 `2.0.0-Beta+e307d0f`，编译器 DLL 2026-08-23）。

| # | 顶层写法 | 结果 | 等级 |
|---|---|---|---|
| p1 | `<Extension> Shared Function Twice(s As String) As String` | ✅ 输出 `abcabc`，退出码 0，无断言 | 已运行 |
| p2 | `<Extension> Function Twice(...)`（无 `Shared`） | ❌ **Debug：进程断言终止**（`Me.IsShared` @ `SourceMethodSymbol.vb:1504`）；**Release：codegen NRE** @ `StackScheduler.Analyzer.vb:663` | 已运行 |
| p3 | `Module ExtHost` 内 `<Extension> Function Twice(...)` | ⚠️ `ExtHost.Twice("abc")` 可调用；`"abc".Twice()` 报 **BC30456**；**不报 BC36551**——扩展方法静默失效 | 已运行 |
| p4 | 嵌套 `Class C` 内 `<Extension> Function/Shared Function` | ✅ 正确报 **BC36551**（`ERR_ExtensionMethodNotInModule`），调用点 BC30456 | 已运行 |
| p5 | 顶层 `Module M` / `Function Go()` | `M.Go()` 可调用；未限定的 `Go()` 报 **BC30451**（M 确是嵌套类型，不在脚本顶层作用域） | 已运行 |
| p6 | p3 的模块内部自己调用 `"abc".Twice()` | ❌ 仍报 **BC30456**——**模块自己的方法体内也取不到自己的扩展方法** | 已运行 |
| p7 | 脚本里写 `Namespace N` | ❌ 报 **BC36965**（`ERR_NamespaceNotAllowedInScript`），命名空间在脚本里不可用 | 已运行 |
| p8 | 嵌套 `Module` 内访问脚本类的顶层实例 `Dim x` | ❌ 报 **BC30469**（访问非共享成员需要对象引用）——设计意图 ③ 成立 | 已运行 |
| p9 | 嵌套 `Module` 内不限定名访问脚本类顶层 `Shared Function Tag()` | ✅ 输出 `t`——设计意图 ⑦ 语法成立 | 已运行 |
| p10 | 顶层 `Shared Tag As String = "t"`（带初始化器的共享字段） | ❌ 宿主 `TypeLoadException: Could not load type 'Submission#0'`（栈顶 `ScriptBuilder.GetEntryPointRuntimeMethod`，`Scripting\Core\ScriptBuilder.cs:194`）——见 §8 与 `issues\issue-submission-shared-field-initializer-typeload.md` | 已运行 |
| p11 | REPL 提交 1：`Imports System.Runtime.CompilerServices`；提交 2：`<Extension> Shared Function Twice(...)`；提交 3：`? "abc".Twice()` | ❌ **`error BC30456: 'Twice' 不是 'String' 的成员`**——跨提交扩展方法不可见（缺口 4） | 已运行 |
| p12 | REPL 跨提交**普通成员**对照：提交 1 `Class C` / `Shared Function F() As Integer` → `Return 7`；提交 2 `? C.F()` | ✅ 输出 `7`——`LookupInSubmissions` 本身工作，缺口专属**扩展方法收集** | 已运行 |
| p13 | `.vbx`（单提交）`<Extension> Shared Function F(o As Object) As Object` + `(New Object).F()` | ⚠️ 编译**零诊断**，运行时 `MissingMemberException: Public member 'F' on type 'Object' not found.`——**`Object` 接收者被排除出扩展查找**（`Binder_Lookup.vb:1178`），继而走晚绑定（同提交、同类内） | 已运行 |
| p13d | 同 p13，仅首行加 `Option Strict On` | ❌ 报 **BC30574**（Option Strict On 不允许后期绑定），**不是**「扩展被采用」——判别探针，证明 `Object` 接收者根本不触发扩展查找（与 Option Strict 无关） | 已运行 |
| p14 | REPL 按既有 VB 测试的形状（`? (New Object).G().F()`，`G`/`F` 均为 `(this) Object` 扩展） | ⚠️ 编译**零诊断**，运行时 `MissingMemberException: Public member 'G' on type 'Object' not found.`——**用 `Object` 接收者的用例对「扩展是否被解析」不可分辨** | 已运行 |

**结论（实锤）**：p1 合法可用；p2 崩溃；p3/p6 静默失效；在 p1–p4 覆盖的四条路径中，p4 是**唯一**报出正确诊断的那条（实锤）；p8/p9 划出模块与脚本类之间的双向可见性边界；p10 划出一条不能假定「脚本类 `Shared` 成员整体可用」的红线；p11/p12 一组对照把缺口 4 定成「提交链遍历只做了一半」，而非「提交链整体不可用」；p13/p13d/p14 定位既有测试空转的原因（`Object` 接收者被排除出扩展查找 + Option Strict Off 静默晚绑定）。

**1.1 本阶段证据边界（须与上表同读，否则会误判证据强度）**

- **C# 侧测试在本仓跑不了**：`Compilers\CSharp\Test\Symbol\*.csproj` 不可求值（`MSB4019：找不到 …targets\ILAsm.targets`），且无预编译产物。因此本文中**一切 C# 行为结论都是「已检查（源码）」级**，凡涉及「该测试在本 fork 是否通过」的表述一律标**未验证/推测**（§6.3、§6.1.1 对照表均按此标注）。
- **`.vbx` 的 Release 分支 codegen NRE 本期未重跑**：可用的 Release 编译器 DLL 为 2026-08-23 版（版本 `2.0.0-Beta+e307d0f`，比当期旧），p2 的 Release 结论沿用该构建的既有复现，未在当期构建上复测（与 `issues\issue-script-top-level-extension-method-crash.md` 的登记口径一致）。
- **撰写者探针 p5/p7/p8/p9/p10 本期未复跑**：其中 p10 的锚点经独立抽检全部命中。这五项保持「已运行（既往）」而非「本期已运行」。
- **VB 侧测试可跑**：`Compilers\VisualBasicSymbolTest` 有预编译产物，本轮用 `dotnet vstest` 做过单测执行（`InteractiveExtensionMethods` → 1 passed），故「VB 测试实际通过与否」在本文中是**已运行**级；C# 侧不是。

### 2. 宿主与脚本类 kind 的映射（decision-relevant）

讨论修复范围之前必须先钉住：**哪个宿主产出哪种脚本类 kind。** 两者由 `IsScriptClass` 统一覆盖（`SourceMemberContainerTypeSymbol.vb:1295-1300`：`DeclarationKind.Script OrElse DeclarationKind.Submission`），因此**只看「是否被 `IsScriptClass` 覆盖」来判断修复范围会出错**。

| 宿主 / 入口 | 编译入口 | 脚本类 kind | 锚点 | 等级 |
|---|---|---|---|---|
| **所有 Scripting 宿主**：`.vbx` 执行、vbi REPL、`/check`、`Script` API 消费者 | `VisualBasicScriptCompiler.CreateSubmission` → `CreateScriptCompilation` | **`DeclarationKind.Submission`**（`TypeKind.Submission`），脚本类名 `Submission#N` | `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:208-232`（`scriptClassName:=submissionTypeName` 在 `:215`）→ `VisualBasicCompilation.vb:368-388` 的 `CreateScriptCompilation`，`:388` 写死 `isSubmission:=True` | 已检查 + 已运行 |
| **编译器式脚本编译**（script parser 驱动的编译） | `VisualBasicCompiler` → 公有 `VisualBasicCompilation.Create` | **`DeclarationKind.Script`**（`TypeKind.Class`），入口点 `<Main>` | `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCompiler.vb:96`（`parseOptions.WithKind(SourceCodeKind.Script)`）+ `:165` 的 `VisualBasicCompilation.Create(...)` → `VisualBasicCompilation.vb:327-341` 公有重载，`:339` 写死 `isSubmission:=False`；门控是 `VisualBasicCommandLineParser.vb:1493` 的 `If(IsScriptCommandLineParser, ...)` | 已检查 |
| 编译器级测试 | 取决于测试用的入口 | 两者都有：`ScriptExtensionMethods`（`ExtensionMethodTests.vb:2436-2438` 用 `CreateCompilationWithMscorlib461` + `TestOptions.Script` → `Script`）与 `InteractiveExtensionMethods`（`:2464-2475` 用 `CreateScriptCompilation` → `Submission`） | 同上 | 已检查 |

**规范口径。** `spec\spec-scripting-dialect.md:163`：「A `.vbx` file that is executed as a script **is a submission** and therefore goes through `<Factory>`; the two paths are distinguished by whether the compilation is a submission, **not by the file extension**。」本提案与这句保持一致——**不能**按扩展名或「脚本 vs REPL」切分 kind。

**独立实测确证（p10）**：`vbi p10.vbx` 的失败信息直接打出类型名 `Submission#0`——`.vbx` 执行走的就是 Submission，与 `ScriptExtensionMethods` 覆盖的 `Script` kind 不同。即：**既有正向测试覆盖的 kind，并非产品实际出货的 kind**（两者都被 `IsScriptClass` 覆盖，故行为同构；但任何只针对单一 kind 的修复都会漏掉真正出货的那条路径）。

**`DeclarationKind.Script` 在本产品内的触达面。** 该 kind 需要「script parser ＋ 公有 `VisualBasicCompilation.Create`」同时成立。两条事实把它挡在产品的执行路径之外：

1. **执行路径的编译入口不是 `CommonCompiler`。** `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` 的三条执行路径（`:243`、`:291`、`:357`）都用 `Script.CreateInitialScript(Of …)`（`ScriptCompiler` → `VisualBasicScriptCompiler.CreateSubmission`），该文件**零 `CreateCompilation` 调用**（全文件检索计数为 0）；它持有的 `_compiler`（`CommonCompiler`，`:27`、`:42`）只负责取参、help/版本输出与诊断汇报（如 `:114`、`:143`、`:249`）。
2. **`vbi` 的 compile 模式用的不是 script parser。** `vbi` 只在 `VbiCompileMode.IsCompileInvocation` 为真时进编译模式（`Interactive\vbi\Vbi.vb:59-62`），而该判定只认 `.vb` 与 `/out:`/`/target:`（`Vbi.Compile.vb:32-59`），且 `VbiCompiler` 走 `VisualBasicCompiler` 的**默认** parser；`Compilers\Core\Portable\CommandLine\CommandLineParser.cs:1133-1148` 的 `ToCommandLineSourceFile` 在非 script parser 下**硬编码 `isScriptFile = false`**（`:1146`），因此 `vbi foo.vbx /out:x.dll` 也不会把 `.vbx` 当脚本源。

需要更正的一点：`VisualBasicCommandLineParser.Script`（`VisualBasicCommandLineParser.vb:33`）在本仓**有**调用方，共 8 处——产品内 `Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:18`、`Scripting\VisualBasic\Hosting\VisualBasicReplServiceProvider.vb:21`；测试/工具 `Compilers\Test\Utilities\VisualBasic\MockVbi.vb:11`、`Compilers\VisualBasicCommandLineTest\CommandLineTests.vb:67`、`Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb:168`/`:194`/`:207`/`:220`（已检查）。它被用于**取参与解析**（`Vbi.vb:18` 把它作为 `CommonCompiler` 的 parser 传入），但**不构成**「产品里有 Script kind 的编译」：上一条 1 与 2 共同决定编译入口仍是 `CreateSubmission`。

**结论**：本产品当前所有脚本宿主产的都 `DeclarationKind.Submission`（证据等级：已检查；「不存在本仓之外的触达点」为**推测**）。

### 3. 缺口 1：脚本类的非 `Shared` 成员没有用户可见诊断

**3.1 放行判据与四个消费点。** `AllowsExtensionMethods`（`NamedTypeSymbolExtensions.vb:108-111`）全仓有 4 个消费点：

| 消费点 | 作用 |
|---|---|
| `Symbols\Source\SourceMethodSymbol.vb:1501` | 早期解码进门后走 `Debug.Assert(Me.IsShared)`（`:1504`） |
| `Symbols\Source\SourceMethodSymbol.vb:1627` | 完整解码：`Not AllowsExtensionMethods()` → BC36551；否则落进 `Else`（`:1633-1648`） |
| `Symbols\Source\SourceMemberMethodSymbol.vb:86-89` | 快速属性过滤：不允许则**抹掉** `QuickAttributes.Extension` |
| `Symbols\Source\SourceMemberContainerTypeSymbol.vb:3341` | `MightContainExtensionMethods` 的三合一判据 |

**3.2 诊断序列里独缺 `Shared` 检查。** `SourceMethodSymbol.vb:1621-1648` 的分支序列：`MethodKind` 非法 → BC36550；`Not AllowsExtensionMethods()` → BC36551；`ParameterCount = 0` → BC36552；`Else` 内先 `Debug.Assert(Me.IsShared)`（`:1634`）再查首参 Optional / ParamArray / 泛型约束。**「成员必须 `Shared`」没有对应诊断。**

这条缺失是**结构性的历史遗留（实锤）**：标准模块的成员**隐式 `Shared`**（`vblang\spec\types.md:689`），「扩展方法必须落在 `Shared` 成员上」在模块内**不可能被违反**，从未需要诊断。脚本类第一次打破该前提——它**没有**隐式 `Shared`：按普通类构造（`DeclarationTreeBuilder.vb:139-149`，修饰符 `Friend Or Partial Or NotInheritable`），顶层 `Sub`/`Function` 默认是**实例成员**（`spec-scripting-dialect.md:60`）。唯一把关点因此只剩一条 Debug 断言。C# 用 CS1105 解掉了同一问题（`SourceOrdinaryMethodSymbol.cs:243-246`）并同样豁免脚本类（`:234`）。

**3.3 断言先于诊断（实锤）。** `Symbol_Attributes.vb:282` 的 `EarlyDecodeWellKnownAttributes` 在 `:292` 的 `ValidateAttributeUsageAndDecodeWellKnownAttributes` **之前**执行；p2 的断言栈即 `LoadAndValidateAttributes:282` → `EarlyDecodeWellKnownAttributes:439` → `EarlyDecodeWellKnownAttribute:1504`。**只加诊断不改早期解码，Debug 构建仍会终止进程。**

**3.4 建议落点（待定）。** 两处：早期解码在 `:1500-1502` 的条件里补 `Me.IsShared AndAlso`（使 `isExtensionMethod` 保持 `False`，消除 `:1504` 断言可达性）；完整解码在 `:1624-1648` 的序列里补一条分支。分支先后会改变与 BC36552 的优先级，见 Unresolved 5。**注意**：早期解码这处**不是**缺口 2 所需的改动——嵌套模块的成员隐式 `Shared`，`:1504` 与 `:1634` 两条断言都不会触发（p3 实测无断言，已运行），故候选 F 不必碰早期解码（已检查）。

### 4. 缺口 2：脚本顶层的 `Module` 被降级成嵌套类型，扩展方法静默失效

**4.1 声明树的落点差异（实锤，源码逐行）。** 需要同时记住一条语言层事实：模块错位于非文件/命名空间层**本就是语法错误**——`DeclarationContext.vb:54-63` 对 `SyntaxKind.ModuleStatement` 报 **BC30617**（`ERR_ModuleNotAtNamespace`），而对 `ClassStatement`/`StructureStatement`/`EnumStatement` 放行。脚本模式下这条错误在解析期**不会**发生（顶层 `Module` 在编译单元层是合法的），降级发生在下一阶段的声明树构造，因此「错位」在语义层出现、语法层无声（详见 Motivation）。

| 模式 | 顶层类型声明的落点 | 锚点 |
|---|---|---|
| 常规 VB（`SourceCodeKind.Regular`） | **命名空间层**——`children.Add(namespaceOrType)` | `DeclarationTreeBuilder.vb:66-76`，由 `:199-201` 的 `Else` 分支调用 |
| VB 脚本（`Script`）/ C# `.csx` | **脚本类的嵌套类型**——非 Namespace 声明一律进 `scriptChildren` | `DeclarationTreeBuilder.vb:179-196`；C# 同构 `Compilers\CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:274-300` |
| C# 顶层语句 | **命名空间层**——`childrenBuilder.Add(namespaceOrType)`；只有非类型非语句成员才触发隐式类包裹 | `DeclarationTreeBuilder.cs:150-156`、`:183-186`、`:205-213` |

即：**VB 脚本模式与 C# `.csx` 同形**（都把非 namespace 的顶层类型嵌进脚本类——`decisions.md:42` 的 D5 核对结论与本节一致），而「把类型留在命名空间层」在两侧都只出现在**非脚本**路径上：C# 是「文件式程序 / 顶层语句」模式（`DeclarationTreeBuilder.cs:142`、`:150-156` 的 `acceptSimpleProgram`），VB 是常规模式（`DeclarationTreeBuilder.vb:66-76`）。**但 VB 没有顶层语句模式**（`decisions.md:42`：全树无 `SimpleProgram`/`TopLevelStatements` 命中），因此候选 D / 窄 D 在 VB 侧不是「对齐既有模式」而是**新增一条路径**——这一点已计入它们的代价（见 Drawbacks）。

**4.2 收集点：扩展方法从哪里取？** 绑定期入口是 `Binding\Binder_Lookup.vb:1183-1262` 的 `LookupForExtensionMethods`——它**沿 binder 链逐层**调用 `CollectProbableExtensionMethodsInSingleBinder`（`:1219`），链上每个 binder 各贡献一批候选（`:1257` 的 `currentBinder.m_containingBinder`）。脚本顶层代码的 binder 链由 `BinderFactory.vb:172-177` 的 `NodeUsage.ScriptCompilationUnit` 构造：**`NamedTypeBinder(rootNamespaceBinder, SourceScriptClass)`**，其父是 `NodeUsage.CompilationUnit` 的 **`NamespaceBinder`**（`:159-161`，`:174` 断言 `TypeOf rootNamespaceBinder Is NamespaceBinder`）。链上只有两个相关收集者：

- `Binding\NamedTypeBinder.vb:99-107` → `_typeSymbol.AppendProbableExtensionMethods`，`_typeSymbol` = **脚本类本身**。这解释了 p1：脚本类在全局命名空间下，`_containingSymbol.Kind = SymbolKind.Namespace` 成立、`AllowsExtensionMethods()` 成立、`AnyMemberHasAttributes` 成立 → `MightContainExtensionMethods = True`（`SourceMemberContainerTypeSymbol.vb:3336-3348`）→ 收集脚本类自己的 `<Extension> Shared` 成员。
- `Binding\NamespaceBinder.vb:85-93` → `_nsSymbol.AppendProbableExtensionMethods`，实现点是 **`Symbols\PEOrSourceOrMergedNamespaceSymbol.vb:88-117`**（`:109` 直调 `GetExtensionMethods(methods, name)`；第 40 次查询后转 `EnsureExtensionMethodsAreCollected`）→ `Symbols\NamespaceSymbol.vb:484-488` 遍历 `TypesToCheckForExtensionMethods`；对源命名空间该属性返回 **`Me.GetModuleMembers()`**（`SourceNamespaceSymbol.vb:489-498`，注释「only Modules can contain extension methods in source」），而 `GetModuleMembers` 只取 **`_declaration.Children` 中 `DeclarationKind.Module` 的直接子节点**（`:253-270`）。

**4.3 结论（实锤）：嵌套在脚本类里的 `Module` 没有收集点。** 它既不在命名空间声明的直接子节点里（`DeclarationTreeBuilder.vb:187` 把它放进 `scriptChildren`），也不在任何 binder 链上的 `NamedTypeBinder` 里（顶层代码链上只有脚本类那一个 NamedTypeBinder）。规范层对应表述是 `vblang\spec\types.md:689`——模块成员的作用域是**「模块所在命名空间的声明空间」**，而嵌套模块的 containing symbol 是**类而不是命名空间**。p3 与 p6 实测印证。

**4.4 「保留嵌套、只放宽 `MightContainExtensionMethods` 那条 `_containingSymbol.Kind = SymbolKind.Namespace` 检查」——按场景分别定价。** `SourceMemberContainerTypeSymbol.vb:3341` 的检查对嵌套模块返回 `False`（它的 `_containingSymbol` 是脚本类）——这是**实锤**。但它的后果**按调用位置不同**，需分开定价：

- **对 p6（模块自己的方法体）**：`BinderFactory.vb:205-214` 的 `NodeUsage.TypeBlockFull` → `New NamedTypeBinder(containingBinder, symbol)` ⇒ 模块方法体内的 binder 链**含 `NamedTypeBinder(模块)`** → `NamedTypeBinder.vb:99-107` → `NamedTypeSymbol.vb:318-330` 的 `AppendProbableExtensionMethods` **只**被 `MightContainExtensionMethods` 门控（已检查）。因此放宽 `:3341` 后 p6 应可生效（已检查；实跑为**推测**）。
- **对 p3（脚本顶层调用点）**：顶层链上只有脚本类那一个 `NamedTypeBinder`，命名空间那一环走 `GetModuleMembers()`（只看命名空间直接子节点）。按现有链路，放宽检查不足以让顶层发现该模块，需**新造收集点**（已检查；「需新造」这一步为**推测**，需先实现才可证）。

### 5. 缺口 3（候选 A 证伪）：扩展调用是「静态形状」，实例声明要求「实例形状」

**5.1 归因到降级层。** 把接收者搬进实参表、并清空 `ReceiverOpt` 的是 **`Lowering\LocalRewriter\LocalRewriter_Call.vb:77`**（调用点）→ `:120-147` 的 `UpdateMethodAndArgumentsIfReducedFromMethod`：它以 `method.CallsiteReducedFromMethod` 判定「这是扩展方法调用」，把 `receiver` 插到实参表第 0 位（`:133-143`），然后 `receiver = Nothing`（`:145`）、`method = reducedFrom`（`:146`）。**发射端看到的 `BoundCall` 就是「接收者进实参表、`ReceiverOpt` 为空」的静态调用形状。**

绑定期**不**做这件事：`Binder_Invocation.vb:891-901` 调的 `AdjustReceiverTypeOrValue` 只有 5 参重载（`Binder_Expressions.vb:824-830`）**硬编码 `clearIfShared:=True`**（`:830`），而它按 `methodOrProperty.IsShared` 决定是否清空接收者；**`ReducedExtensionMethodSymbol.IsShared` 恒为 `False`**（`Symbols\ReducedExtensionMethodSymbol.vb:457-460`），所以**对扩展调用不生效**，绑定期保留接收者。真正清空的是降级层。

**5.2 实测印证（已运行）。** p1（`Shared`）在 Debug 编译器上正常运行并输出 `abcabc`，而 StackScheduler 的分支断言是：非共享分支解引用 `receiver.Type`（`StackScheduler.Analyzer.vb:663`），共享分支断言 `receiver Is Nothing OrElse receiver.Kind = BoundKind.TypeExpression`（`:682`）。p1 通过即说明扩展调用的 `receiver Is Nothing`（字面量 `"abc".Twice()` 不可能是 TypeExpression）——**实锤（由 `:682` 断言在 Debug 构建中通过反推）**。StackScheduler 在 Debug 构建中也执行：`CodeGen\CodeGenerator.vb:90` 与 `Optimizer.vb:29` 都是无条件调用（已检查）。

**5.3 崩溃链（已运行）。** p2 在 Debug 构建被 `Debug.Assert(Me.IsShared)` 终止；在 Release 构建越过断言后，`StackScheduler.Analyzer.VisitCall` 进入**非共享分支**（`Not node.Method.IsShared`，`:662`）并对 `receiver`（= `node.ReceiverOpt` = `Nothing`）求 `receiver.Type`（`:663`）→ `NullReferenceException`（实锤，实测栈：`VisitCall:686 → VisitArguments:715 → VisitCall:663`）。发射端同形：`CodeGen\EmitExpression.vb:1020-1034` 用 `method.IsShared` 决定 `CallKind`，非共享分支第一步即 `Dim receiverType = receiver.Type`（`:1034`），并把 `stackBehavior` 再减 1（`:1033`）以计入 `this`。

**5.4 为什么 A 不是「加个分支」就能做。** 扩展调用要求「接收者作为第 0 个实参、以静态调用发射」，实例成员声明要求「接收者作为 `this`、以实例调用发射」；两者读同一个 `BoundCall`，`ReceiverOpt` 只能是其一。让 A 成立须二选一改写形状，并连带改动：降级（`LocalRewriter_Call.vb:77`、`:120-147`）、栈调度（`StackScheduler.Analyzer.vb:656-683`）、发射（`EmitExpression.vb:1020-1034`）、委托创建（`EmitExpression.vb:470-515` 的 `AddressOf` 路径）与 `BoundCall` 的全部既有消费者。**更硬的一条（推测，风险确凿）**：即便写成实例调用，`MethodReference` 的声明类型是**脚本类**，而 `this` 的静态类型是**接收者类型**（如 `String`）——IL 层面的 `this` 类型错配，直接产出不可验证的 IL。C# 在同一位置选择禁止（CS1105）而非放行（已检查）。综合：**A 按现有机制已证伪**；若仍要走，等于新造一套「脚本类实例成员充当扩展方法」的绑定+发射模型。

### 6. 缺口 4：承载在脚本类里的扩展方法跨提交不可用

**6.1 实测（已运行）**：p11 在 REPL 里把「声明」与「调用」放在不同提交，报 **BC30456**（接收者是 `String`，故是真正的编译期扩展解析失败）；p12 的跨提交**普通成员**对照正常输出 `7`。这一组对照把缺口的边界钉死：提交链本身可用（成员查找走通了），**不可用的是扩展方法收集**。

**6.1.1 为什么既有测试没逮到它（已运行 + 已检查）**：原因有**两层，必须分开写**——

1. **扩展查找根本没跑（主因，与 Option Strict 无关）。** `Binding\Binder_Lookup.vb:1176-1181` 的 `ShouldLookupExtensionMethods` 在 `:1178` 用 `Not container.IsObjectType()` **把 `Object` 接收者显式排除**出扩展查找。也就是说：`o.F()`（`o As New Object()`）这一步永远不会去找扩展方法，无论编译选项如何。
2. **零诊断（副因）由 Option Strict Off 提供。** VB 默认编译选项是 `OptionStrict.Off`（`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb:75`），「成员找不到」于是静默降级为**晚绑定**，查不到也无声。

判别探针 **p13d**（与 p13 同文件、只把首行换成 `Option Strict On`）给出 `BC30574`（Option Strict On 不允许后期绑定），**而不是「扩展被采用」**——这直接证明第 1 层成立：即使禁止晚绑定，扩展也不会被采用。p13（Option Strict Off）则在单提交、同类内得到运行时 `MissingMemberException` + 编译零诊断；p14 用既有测试的 `Object` 接收者形状跨提交，同样零诊断、运行时抛异常。而 VB 侧两个「脚本内扩展方法」正向测试正好都用 `Object` 接收者：

| 测试 | 形状 | 能否分辨「扩展被解析」 |
|---|---|---|
| `ScriptExtensionMethods`（`ExtensionMethodTests.vb:2427-2440`） | `Dim o As New Object()` + `o.F()`，只 `VerifyDiagnostics()` + `Assert.True(MightContainExtensionMethods)` | **不能**：Object 接收者 ⇒ 晚绑定 ⇒ 零诊断；断言只查声明侧门（`MightContainExtensionMethods`），不涉及调用点解析 |
| `InteractiveExtensionMethods`（`:2443-2481`） | `source1` 里 `? o.G().F()`，`F` 只在 `source0`（**形状上正是跨提交**），只 `VerifyDiagnostics()` | **不能**：同上；该测试实测**通过**（本轮运行：1 passed），但它对「F 是否被解析」无鉴别力 |
| C# 同形测试 `InteractiveExtensionMethods`（`ExtensionMethodTests.cs:3804-3830`） | 同样的 `Object` 接收者与跨提交形状 | **能**：C# 无晚绑定，找不到扩展即 CS1061，故 `VerifyDiagnostics()` 干净**证明**两个扩展都被解析 |

即：**同一个测试形状，在 C# 是有效鉴别器，在 VB 是空转**。差别的根因是上面两层中的第 1 层——**VB 把 `Object` 接收者排除出扩展查找**（`Binder_Lookup.vb:1178`），C# 没有这条排除；Option Strict Off 只决定「排除之后是静默晚绑定还是报 BC30574」。这解释了缺口 4 为何长期无人发现，也说明补 H 时必须**同时**补一条非 `Object` 接收者的用例（见 Unresolved 13/15）。

**6.2 机制链（VB 侧，已检查）**：

| 环节 | 现状 | 锚点 |
|---|---|---|
| 脚本单元的 binder 入口 | 只建**当前提交**脚本类的一个 `NamedTypeBinder` | `BinderFactory.vb:172-177`；上游原注释自陈「we'll need to plug-in submissions, interactive imports and host object members」（`:176`） |
| 扩展方法收集 | 只问当前容器：`_typeSymbol.AppendProbableExtensionMethods` | `NamedTypeBinder.vb:99-107` → `NamedTypeSymbol.vb:318-330` |
| 链遍历者 | 只沿 binder 链向上（`currentBinder.m_containingBinder`），**链里没有前序提交节点** | `Binder_Lookup.vb:1183-1262`，推进在 `:1257` |
| 提交链遍历 | **已实现，但只覆盖成员查找**：`LookupInSubmissions` 逐提交用 `LookupWithoutInheritance(submission.ScriptClass, …)`，末尾留 `' TODO (tomat): extension methods` | `Binder_Lookup.vb:858-936`（关键行 `:874-875`、`:885`、`:916`），TODO 在 `:932` |
| 名字枚举侧 | 同构，TODO 同款 | `Binder_Lookup.vb:2030-2051`，TODO 在 `:2050` |

**结论（已检查 + 已运行）**：VB 把提交链遍历放在了**成员查找方法**里（`TypeKind.Submission` → `LookupInSubmissions`，`:583-584`），而扩展方法收集走的是**独立的 binder 链遍历**，那条链上没有提交节点——上游作者在两处留下了同一句 `' TODO (tomat): extension methods`。p12（成员可用）与 p11（扩展不可用）正是这条结构差异的行为证明。

**6.3 C# 对照（已检查）**：C# 把提交链遍历放进 **binder 节点**而不是某个 lookup 方法，因而成员查找与扩展收集一并覆盖：

- `InSubmissionClassBinder.GetAllExtensionCandidatesInSingleBinder`（`Compilers\CSharp\Portable\Binder\InSubmissionClassBinder.cs:34-40`）：`for (var submission = this.Compilation; submission != null; submission = submission.PreviousSubmission)` → 对每个提交的脚本类调 `GetAllExtensionMembers`。
- 装入点：`BinderFactory.BinderFactoryVisitor.cs:1001-1004`（`:1003` 的 `New InSubmissionClassBinder(...)`，仅 `isSubmissionClass` 分支）。
- 链遍历者据此自动覆盖全部提交：`Binder_Lookup.cs:221` 的 `EnumerateAllExtensionMembersInSingleBinder`、`:252` 的 `EnumerateExtensionBlockMembersInSingleBinder`（均在 `:221`/`:252` 处调 `GetAllExtensionCandidatesInSingleBinder`）。
- `GetAllExtensionMembers` 同时收集 classic extension methods（`DoGetExtensionMethods`）与 C# 14 分组类型成员（`doGetExtensionMembers`）：`CSharp\Portable\Symbols\NamedTypeSymbol.cs:408-434`。
- 行为侧有直接跨提交测试：`Compilers\CSharp\Test\Symbol\Symbols\ExtensionMethodTests.cs:3804-3830` 的 `InteractiveExtensionMethods`——`s1`（`previousScriptCompilation: s0`）里 `o.G().F()` 的 `F` **只在 `s0` 声明**，而 `s1.VerifyDiagnostics()` 干净。因为 C# 无晚绑定（扩展找不到即 CS1061），这个断言**确实**证明两个扩展都被解析（已检查；该测试在本 fork 的实际通过情况**未验证**，标**推测**）。VB 侧有一个**形状完全相同**的测试（`ExtensionMethodTests.vb:2443-2481`，同样 `previousScriptCompilation`，同样 `? o.G().F()`），但因 VB 的 Option Strict Off 晚绑定而**无鉴别力**（§6.1.1，已运行 + 已检查）。

**6.4 与 D5 的关系（已检查）**：`decisions.md:35` 的 D5 要求 vbi 的基础功能以 C#/csi 实现为蓝本、并说明「为什么 VB 必须分叉」。本缺口正落在 D5 点名的「名字查找」区域（`decisions.md:37`），且 `BinderFactory.vb:176` 的上游注释自陈设计意图就是「plug-in submissions」。VB 特有理由排查：`AllowsExtensionMethods` 的容器判据、晚期绑定/Option Strict（扩展解析走早绑定重载解析）、`Overloads`/遮蔽与 proximity（`Binder_Lookup.vb:1206`、`:1256` 的 proximity 在 C# 侧有对应物）——三项都解释不了该缺口（锚点为**已检查**；「无 VB 特有理由」这一结论为**推测**）。缺口性质因此是**移植不完整**。

**6.5 与缺口 2 的边界（已检查；一处未调查）**：两者都在脚本类里，但治的不是一件事——缺口 2 是「模块被降级后**连声明点都不可达**」（同提交内即可复现），缺口 4 是「声明点可达、但只对当前提交可见」（跨提交才复现）。按 D5 的核对（`decisions.md:42`），**脚本模式下 C# 与 VB 把顶层类型嵌进脚本类这一点是同形的**，故「类型被嵌进脚本类」不是 VB 独有；缺口 2 的**具体形态**（模块的扩展方法不可达）是 VB 专有的，因为 `Module` 是 VB 概念。C# 侧是否存在与缺口 2 等价的缺口（例如脚本类里嵌套的 `static class` 里的扩展方法），**本轮未调查**（标**未提供**），不作为任何结论的依据。

### 7. 与 C# 的逐项对照

| 维度 | C# | VB（本 fork） | 等级 |
|---|---|---|---|
| 容器白名单 | `!IsScriptClass && !(IsStatic && Arity == 0)` 才报错（`:234`） | `TypeKind.Module OrElse IsScriptClass` 才放行（`NamedTypeSymbolExtensions.vb:110`） | 已检查 |
| 嵌套类型 | `ContainingType.ContainingType != null` → `ERR_ExtensionMethodsDecl`（`:230-232`） | `Not AllowsExtensionMethods()` → BC36551（`SourceMethodSymbol.vb:1627-1628`） | 已检查 |
| **成员必须 static / `Shared`** | `else if (!IsStatic)` → **`ERR_BadExtensionMeth`(CS1105)**（`:243-246`，消息「Extension method must be static」，`CSharpResources.resx:2540-2541`） | **无对应诊断**，只有 `Debug.Assert(Me.IsShared)`（`:1504`、`:1634`） | 已检查 + 已运行 |
| **首参检查的位置** | 在 `:213-229`（`IsValidExtensionParameterType` / `Ref` / `In`），**早于** `:230` 的嵌套类型检查，也早于 `:243` 的 `!IsStatic` | 在 `:1636-1647`，位于 `:1624-1632` 的 MethodKind/容器/参数个数检查**之后**，且整段在 `Else` 内 | 已检查 |
| 正向测试（脚本内扩展方法可用） | `ScriptSemanticsTests.cs:1104-1119`、`ExtensionMethodTests.cs:3790-3801` | `ExtensionMethodTests.vb:2427-2440`（`Script` kind）与 `:2443-2477`（`Submission` kind） | 已检查 |
| 负向测试（漏 static） | `ScriptSemanticsTests.cs:1112-1118` 钉住 CS1105 | **无** | 已检查 |
| 顶层类型落点（脚本） | `.csx` → script class（`DeclarationTreeBuilder.cs:274-300`） | 同构（`DeclarationTreeBuilder.vb:179-196`） | 已检查 |
| 顶层类型落点（顶层语句 / 常规） | **命名空间层** | 常规模式同为命名空间层；脚本模式不同 | 已检查 |
| **提交链上的扩展方法收集** | `InSubmissionClassBinder` 作为链上节点遍历 `Compilation → PreviousSubmission`（`InSubmissionClassBinder.cs:34-40`，装入点 `BinderFactory.BinderFactoryVisitor.cs:1003`），有跨提交测试（`ExtensionMethodTests.cs:3804-3830`，因 C# 无晚绑定而**有效**） | **无链上节点**：收集只问当前容器（`NamedTypeBinder.vb:99-107`），提交链遍历只做成员查找，两处留 `' TODO (tomat): extension methods`（`Binder_Lookup.vb:932`、`:2050`）；跨提交实测报 BC30456（p11） | C# 已检查 / VB 已检查 + 已运行 |
| **既有脚本内扩展方法测试的鉴别力** | 同形测试有效（无「`Object` 接收者排除」这条，扩展对 `object` 接收者照常查找，找不到即 CS1061） | **空转**（两层：`Object` 接收者被 `Binder_Lookup.vb:1178` 排除出扩展查找＝主因；Option Strict Off 把「找不到」降级为晚绑定＝零诊断的副因；p13/p13d/p14 实测；两个测试的接收者都是 `Object`） | 已运行 + 已检查 |

附注：VB 侧 `ScriptExtensionMethods` 标着 `<ConditionalFact(GetType(NoUsedAssembliesValidation))>` 并挂 roslyn issue 40680（`ExtensionMethodTests.vb:2425-2426`），部分配置下会被跳过（「跳过时是否覆盖本场景」为**推测**）。

### 8. 候选全集（十一条，按缺口分组，各自定价）

> 十一条**并列**，本节只给收益/代价/实证，不给结论。取舍见 §Unresolved questions。
> 分组：**组 ① 面向缺口 1**（脚本类自身的 `Shared` 前提）＝ A / B / C / G；**组 ② 面向缺口 2**（顶层 `Module` 的落点与可达性）＝ F / 窄 D / D / E-a / E-b / E-c；**组 ③ 面向缺口 4**（承载之后的跨提交可用性）＝ H。E 家族（E-a / E-b / E-c）按机制档位分列，**不是三个独立方案**，而是**同一收敛方向的三层增量，其中第三层引入独立机制**（E-c 换的是成员查找，与 E-a/E-b 的扩展收集不是同一类机制）；E 同时还覆盖设计意图 ⑥/⑧，跨两组。**组 ③ 与组 ①② 正交**（见 H 的说明）：组 ①② 治「脚本类自身能不能承载扩展方法」，组 ③ 治「承载了之后跨提交能不能用」。

#### 组 ①：面向缺口 1

**A. 给脚本类开口子：非 `Shared` 成员也能作扩展方法。**
**已证伪**（§5，实锤 + 推测）。收益：书写更符合直觉。代价：重造降级+发射模型，IL `this` 错配；C# 在同一位置禁止。

**B. 维持现状：只认脚本类里的 `Shared Sub`/`Function`，补齐缺失诊断。**
收益：最小改动；**不改 IL 形状**，且 `Shared` 这条发射路径已由 p1 实测健康（已运行）；与 C# 的 `ERR_BadExtensionMeth` 同构，是「延申现有机制」最省的一条（已检查）。代价：两处改动（§3.4，其中早期解码那处是**必需**的，否则 p2 仍终止进程——实锤）；写法别扭——`Shared` 在标准模块里**非法**（BC30433 `ERR_ModuleCantUseMethodSpecifier1` = 30433，消息「Methods in a Module cannot be declared '{0}'」，非法修饰符表 `Binder_Utils.vb:1685-1695` 含 `SyntaxKind.SharedKeyword`，测试 `SymbolErrorTests.vb:4404` 明列，已检查），于是同一概念在两种宿主里修饰符要求相反；**不解决缺口 2**（已检查）。

**C. 一并禁止脚本类的扩展方法（撤销 `AllowsExtensionMethods` 里的 `IsScriptClass`）。**
收益：消除脚本类这一条崩溃面（p2 的触发前提不再成立——已检查），并消掉「必须 `Shared`」的别扭。代价：打破既有正向测试（`ExtensionMethodTests.vb:2427-2440`、`:2443-2477`）与 p1 已运行写法（已运行）；与 C# 显式豁免 `IsScriptClass`（`SourceOrdinaryMethodSymbol.cs:234`）**反向**分叉（已检查）；且 `:3341` 那一处判据的改动会影响 `MightContainExtensionMethods`，需连同 4 个消费点一起评估（已检查）。

**G. 只对带 `<Extension>` 的顶层成员隐式 `Shared`。**（本轮新增）
定义：脚本类顶层的、标了 `<Extension>` 的 `Sub`/`Function`，**按 `Shared` 处理**（等价于自动补上 `Shared`）。这是 A 与 B 之间的第三条路：不是让实例成员当扩展（A），也不是把现状定为非法（B），而是**让现状成为合法**。
收益：**同时消掉 p2 的崩溃与「必须写 `Shared`」的别扭**（二者都是同一前提的后果——已检查）；且**不改 IL 形状**——隐式 `Shared` 后走的是 p1 已运行证明健康的静态调用发射路径，与 A 需要重造降级+发射模型形成根本区别（「隐式 `Shared` 后绑定仍产出 `BoundCall.ReceiverOpt = Nothing` 的静态形状」为**推测**，需实现后验证；p1 证明的是该形状可正确发射，已运行）；`AllowsExtensionMethods` 无需改动（设计判断）。
真障碍（顺序感知，这是 G 唯一的硬问题）：`Shared` 标志来自**语法修饰符翻译**（`Binder_Utils.vb:91` 的 `Case SyntaxKind.SharedKeyword : Return SourceMemberFlags.Shared`），而 `<Extension>` 的识别在**符号构造期**才经 `QuickAttributeChecker`（`SourceMemberMethodSymbol.vb:85`）——**属性识别晚于标志判定**（已检查）。两条实现路线：**(a)** 把「该成员是否带 `[Extension]`」前移到标志计算点——`Binder_Utils` 的修饰符翻译处有 `syntax` 可用，`QuickAttributeChecker` 本身是**语法级**判定，可在装饰阶段调用；**(b)** 让符号的 `IsShared` 覆盖为「标志或属性存在」，但这会与属性解码顺序形成循环依赖（属性类型解析需要符号已绑定），风险更高。此外需确认「标志计算早于 `SourceMemberMethodSymbol.vb:86-89` 的快速属性过滤」这一顺序是否成立（**推测**，未验证）。
第二顺位代价（语义面，已检查）：隐式 `Shared` 使该成员**失去实例上下文**——不能访问顶层 `Dim`、不能用 `Me`，与现状（`spec-scripting-dialect.md:60` 的「顶层 `Function` 可读写顶层 `Dim`」）冲突，规范必须明说；且这引入一条隐式规则「同一段顶层代码，加个属性就换了共享性」，可读性有损。
与其它候选的关系：与 B **互斥**（同一场景「合法化」vs「报错」二选一）；与 A 的区别在 IL 形状（A 改形状、G 走既有形状）；与 F 正交（F 面向缺口 2）。

#### 组 ②：面向缺口 2

**F. 只补诊断、不改能力：顶层 `Module` 的 `<Extension>` 收集不到时报出来。**（本轮新增）
定义：**保留现状的能力与语义**（`Module` 仍被降级为嵌套类型、扩展方法仍不可用），只把**静默失效变成响**——在声明点报一条脚本专属诊断（或强化调用点 BC30456 的指向）。
现状为什么连 BC36551 都不报（已检查 + 已运行）：嵌套 `Module` 的容器判据走的是 `AllowsExtensionMethods()` 的 `TypeKind.Module` 分支（`NamedTypeSymbolExtensions.vb:110`），**为真**，故 `SourceMethodSymbol.vb:1627` 的 `ElseIf Not AllowsExtensionMethods()` 不成立；随后落进 `:1633` 的 `Else`，而模块成员**隐式 `Shared`**（`vblang\spec\types.md:689`）使 `:1634` 的 `Debug.Assert(Me.IsShared)` 通过 → **零诊断**（p3 实测印证）。
落点与判据：在 `SourceMethodSymbol.vb:1624-1648` 的序列里补一条「承载模块不在文件/命名空间层」的检查（放在 `:1633` 的 `Else` 之前或之内，否则进不去），判据为 `m_containingType.ContainingType IsNot Nothing`（模块被嵌在任何类型里）；若要把改动限制在脚本，可加「其 containing symbol `IsScriptClass`」。语言层依据：模块错位于非文件/命名空间层本就是**语法错误**（`DeclarationContext.vb:54-63` → **BC30617**，见 Motivation），脚本模式只是把这条错误的可检出点从解析期挪到了语义期。
收益：**不改变能力、不改 IL 形状**；诊断落在**声明点**（比调用点 BC30456 指向更准）；成本低于 B——**不需要碰早期解码**，因为嵌套模块的 `isExtensionMethod` 本来就为 `False`（`:1511` 的 `m_containingType.MightContainExtensionMethods` 为假，p3 无崩溃即其证据），故不存在 B 那样的断言可达性问题（已检查 + 已运行）。
代价：与 B 落在**同一段序列**（`:1624-1648`），两者若同时采纳需要合并设计诊断码与优先级；需要新码（或复用/强化既有码，见 Unresolved 6/11）；**不改变能力**，用户要么改写要么接受不可用。
与 D/窄 D 的关系：**互补**。D/窄 D 让能力可用（届时 F 的触发面收窄到仍不可达的残留场景）；只选 F 则「静默失效」变「明确报错」，这是保住现状前提下的最小改善。

**窄 D. 只把顶层 `Module` 放回命名空间层，`Class`/其余类型保持嵌套。**
通路**已存在**（已检查）：`DeclarationTreeBuilder.vb:184-188` 的脚本分支已有「Namespace 声明 → `childrenBuilder`（根命名空间层）」的通路，把 `:184` 的条件从 `decl.Kind = DeclarationKind.Namespace` 扩为「或 `DeclarationKind.Module`」即可；之后的链与 D 的链**同构**（`SourceNamespaceSymbol.vb:253-270` → `NamespaceBinder.vb:91` → `MightContainExtensionMethods` 三条件全真；已检查，实跑为**推测**）。
收益：**解缺口 2**；改动面是**一个条件表达式**，远小于 D 与 E-c。
代价（已检查 + 推测）：**只断「模块的跨提交可见性」**——模块移出脚本类成员表后，`LookupInSubmissions`（`Binder_Lookup.vb:583-584`、`:874-875`）看不到它，REPL 里「先定义 `Module`、下一提交再用」会断（实锤链 + 「会断」为**推测**）；而 **`Class` 的跨提交可见性保住**（§9.2 的 REPL 基线不断，已运行）。这是一条「用更窄的代价换同一收益」的候选。
未决：模块在 REPL 里是否常被当作状态载体、以及是否与 B/F 组合（见 Unresolved 3、Unresolved 10、Unresolved 11）。

**D. 让脚本里的顶层类型声明全部留在命名空间层（对齐 C# 顶层语句 / 常规 VB）。**
收益：解决缺口 2（§4.2 收集点链 + §4.3 结论的推演；已检查，实跑为**推测**）；不需要 `Shared`（模块成员本就隐式共享，已检查）；`AllowsExtensionMethods` / `IsScriptClass` 零改动。
代价：改动 `DeclarationTreeBuilder` 的脚本分支，是所有脚本编译的声明树入口（已检查）；规格冲击面明确（`spec-scripting-dialect.md:62`、`:66`、`:48` 都要改写）；**跨提交可见性会断**——见 §9（§9.1/§9.2 实锤链 + §9.3 推测）。

**E. 用户的设计：module 留在脚本类里，但语义上「仿佛在脚本类外面」。**

设计意图与现状对照（**逐行复核**，探针见 §1）：

| 用户要求 | 现状 | 证据（锚点 / 探针） | 等级 |
|---|---|---|---|
| ① 脚本类 `Shared Sub/Function` 破例作扩展 | ✅ 成立 | `NamedTypeSymbolExtensions.vb:108-111`；p1 → `abcabc` | 已运行 |
| ② 脚本类里允许套 module | ✅ 成立 | `DeclarationTreeBuilder.vb:187` 进 `scriptChildren`；p3 可塞、p5 `M.Go()` 可调用 | 已运行 |
| ③ module 不能访问脚本类的 instance 成员 | ✅ **已成立** | p8 → **BC30469**（与常规 VB 的嵌套类型规则一致，无需改动） | 已运行 |
| ④ module 能定义 extension | ⚠️ 能定义但**静默失效** | p3：调用点只报 BC30456；p6：**模块自己的方法体内也取不到** | 已运行 |
| ⑤ 该 extension 要能被脚本类**和模块自己**访问 | ❌ 不成立 | p3（脚本类侧）与 p6（模块侧）均 BC30456 | 已运行 |
| ⑥ 该 module **仿佛在脚本类外面一样** | ❌ 不成立 | p5：`M.Go()` 必须限定名；成员不在脚本类声明空间 | 已运行 |
| ⑦ 访问脚本类的 shared members 免写 `Script.` | ⚠️ **语法成立、底下有雷** | p9 → 输出 `t`（成立）；但 p10：顶层 `Shared` 字段**带初始化器** → 宿主 `TypeLoadException`（`Submission#0`），见 `issues\issue-submission-shared-field-initializer-typeload.md` | 已运行 |
| ⑧ 脚本类访问 module 的 members 免写 module 名 | ❌ 不成立 | p5 → **BC30451** | 已运行 |

**定义**：E = ⑥「仿佛在外面」+ ⑧「双向免限定名」+ ⑤「扩展方法双向可见」，即**不搬层**但改**语义作用域**。E 与 A/B/C/D/窄 D 都**不是同一件事**：A 是非 `Shared` 也当扩展（已证伪，与 E 正交）；B 是补脚本类自身的诊断（E 不解决它，E 下用户仍可在脚本类上漏写 `Shared`）；C 与 E 方向相反（C 撤销脚本类承载能力，E 则提供第三个去处，客观上使 C 更可行）；D/窄 D 是**搬层**，E 是**不搬层改作用域**——关键差别是 **E 不破跨提交可见性**（模块仍在脚本类成员表里，`LookupInSubmissions` 照常看到它；§9.2 基线不断），换取的代价是改查找语义。

**E 家族的三档分解定价（⑧ 与 ⑤ 是两件事，应分开决策）**：

- **E-a ＝ 只放宽 `MightContainExtensionMethods`（`SourceMemberContainerTypeSymbol.vb:3341`）——解决 ⑤ 的模块侧（p6）。**
  链路（已检查）：`BinderFactory.vb:205-214` ⇒ 模块方法体内链含 `NamedTypeBinder(模块)` → `NamedTypeBinder.vb:99-107` → `NamedTypeSymbol.vb:318-330`（**只**被 `MightContainExtensionMethods` 门控）。改动量：一处条件。脚本作用域写法应为 `_containingSymbol.Kind = SymbolKind.Namespace OrElse _containingSymbol.IsScriptClass`（否则会连带放宽常规 VB 的嵌套模块语义）。
  代价/风险（已检查）：该判据被多处复用——`Symbols\Source\SourceMemberContainerTypeSymbol.vb:3341` 自身与同文件 `:3350-3359`（`BuildExtensionMethodsMap`）；`Symbols\NamedTypeSymbol.vb:351-364`（`GetExtensionMethods`）、`:387-398`（`AppendProbableExtensionMembers`）、`:407-422`（`GetExtensionMembers`）。放宽会改变所有「containing symbol 是脚本类的嵌套类型」的收集行为（`AllowsExtensionMethods` 仍挡住嵌套 `Class`，故只有嵌套 `Module` 受益——已检查）；且只解 p6 不解 p3，留下**「在模块里能调用、在脚本顶层不能调用」的半截边界**，比现状更难解释（已检查）。收益等级：已检查；实跑为推测。
- **E-b ＝ E-a ＋ 收集点下探——解决 ⑤ 的脚本类侧（p3）。**
  需要让脚本顶层链上的收集者看到嵌套模块。落点候选两处（已检查）：给脚本类重写 `AppendProbableExtensionMethods`（`NamedTypeSymbol.vb:318-330`）使其追加嵌套模块的候选；或让 `NamespaceSymbol.TypesToCheckForExtensionMethods`（`SourceNamespaceSymbol.vb:489-498`）对脚本类递归。两者都要引入**新的遍历面与缓存**（`PEOrSourceOrMergedNamespaceSymbol.vb:91-117` 的 `_lazyExtensionMethodsMap` 与第 40 次查询阈值、`EnsureExtensionMethodsAreCollected`）。
  另有一条既有约束要注意（已检查）：`Binder_Lookup.vb:1214`、`:1227-1248` 的 `seenContainingTypes` 去重要求「同一 containing type 的候选应连续分组」；把模块成员追加在脚本类成员之后仍各自成组（**推测**，未验证）。
  收益等级：已检查；实跑为推测。
- **E-c ＝ E-b ＋ ⑧「成员作用域下探」（脚本类免限定名访问 module 成员）。**
  这是**与 ⑤ 完全不同的一件事**（已检查）：⑤ 是扩展方法**收集**，⑧ 是普通**成员查找**。落点在 `Binder_Lookup.LookupInClass`（`:636`）或 `LookupWithoutInheritance`（`:2148`）一类的成员查找路径上，或改 `NamedTypeSymbol.GetMembers` 对脚本类的语义（已检查）。它与 `SourceNamespaceSymbol.GetModuleMembers()`（`:253-270`）**不是同一层**（已检查）：后者是**命名空间层**的模块枚举，⑧ 是**类成员**查找，无法复用，需新造。
  脚本专属的 binder 偏差在本仓**已有成熟先例**，属「延申现有机制」（已检查）：`NamedTypeBinder.vb:44`、`:50`（脚本类时 `GetBinder` 返回 `Me`）、`Binder.vb:434-438`、`BinderBuilder.vb:436`、`BinderFactory.vb:180`、`:194`、`Binder_Expressions.vb:2263`、`:2567`、`:4727`。
  代价/风险（推测，风险确凿）：改变**每一次名字查找**的语义（所有脚本编译），新增歧义面（模块成员 vs 脚本类成员 vs 已导入命名空间成员）、与继承/遮蔽规则的交互，以及 IDE 侧名字枚举（`AddLookupSymbolsInfoInSubmissions`，`Binder_Lookup.vb:2030-2051`）与缓存面。这是十一条候选里改动面与回归风险最大的一条。

**E 与 ⑦ 的边界**：⑦ 是「脚本类 `Shared` 成员的免限定名访问」，p9 证明语法成立，但 p10 证明**顶层 `Shared` 字段带初始化器会触发宿主 `TypeLoadException`**。链条（已检查 + 一处推测）：`SourceMemberContainerTypeSymbol.vb:2726-2737`（`:2729` 的 `Not isShared OrElse …` 放行共享分支；`:2735` 把 `isShared` 透传给 `SynthesizedSubmissionConstructorSymbol`）→ 该符号在共享时 `MethodKind = SharedConstructor`（`Compilers\VisualBasic\Portable\Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb:190-194`）、`Name = .cctor`（同文件 `:59-63`，`WellKnownMemberNames.cs:29`）→ 提交初始化**不会**被挂（`MethodCompiler.vb:1535-1537` 经 `IsScriptConstructor`（`MethodSymbol.vb:517-521`，要求 `MethodKind = Constructor`）与 `IsSubmissionConstructor`（`:529-533`）被挡）→ **真正的断点在 `SynthesizedSubmissionConstructorSymbol.vb:31-38`：形参表不随 `isShared` 分叉**，`:36-37` 无条件造一个 `submissionArray As Object()`，于是发射出**带参数的 `.cctor`**（已检查）→ 宿主 `TypeLoadException`（已运行）。「带形参的 `.cctor` 属非法元数据因而类型无法加载」这一步为**推测**（与 `Assembly.GetType` 抛 `TypeLoadException` 而非单纯「找不到」的症状一致）。**已登记**：`issues\issue-submission-shared-field-initializer-typeload.md`（其「更正」节 `:89-97` 与本节一致）。

#### 组 ③：面向缺口 4

**H. 给提交链加「扩展容器」节点（对齐 C# `InSubmissionClassBinder`）。**（本轮新增）
定义：让 VB 的脚本 binder 链在**扩展收集**时也遍历提交链。两个实现形态：**(a)** 新增一个对齐 C# 的 binder 类型（`InSubmissionClassBinder` 的 VB 对应物），override `CollectProbableExtensionMethodsInSingleBinder`（`Binder.vb` 的虚方法，被 `Binder_Lookup.vb:1219` 调用），在其中 `For submission = Compilation → PreviousSubmission`，对每个提交的脚本类取候选；**(b)** 不新增类型，在 `NamedTypeBinder.vb:99-107` 的脚本类分支里遍历提交链。调用点（`Binder_Lookup.vb:1219`）已现成，两种形态都不需要改链遍历者。
收益：关掉缺口 4——跨提交扩展方法可用，与 C#/csi 对齐（D5 的强制口径）；`LookupInSubmissions` / `AddLookupSymbolsInfoInSubmissions` 的两处 `' TODO (tomat): extension methods`（`Binder_Lookup.vb:932`、`:2050`）可一并收口（成员查找那半已实现，扩展这半由新节点承担，语义上正好落在 TODO 的位置）。
代价（已检查）：改动落在**共享编译器树的 Binding 层**（`BinderFactory.vb`、`NamedTypeBinder.vb` 或新增 binder 类型）→ 须走 `upstream-merge.md` 记账与 3-way 评审；**跨提交的 proximity 语义需要定义**——当前 `proximity` 从 0 起、每上一级 binder `+= 1`（`Binder_Lookup.vb:1206`、`:1256`），提交链要不要计入、以及 C# 怎么算，**未验证**（见 Unresolved 13）。
重复收集风险（**推测**）：链遍历用 `seenContainingTypes` 去重并要求同一 containing type 的候选连续分组（`Binder_Lookup.vb:1214`、`:1227-1248`）；跨提交会引入多个不同的脚本类（各自是不同 containing type），分组约束应仍成立，但未实现验证。
测试面（**已检查 + 已运行，结论与初判不同**）：VB 侧**有一个**形状上正是跨提交的用例——`InteractiveExtensionMethods`（`ExtensionMethodTests.vb:2443-2481`，`source1` 的 `? o.G().F()` 里 `F` 只在 `source0`），本轮实测它**通过**（1 passed）；但它与 `ScriptExtensionMethods`（`:2427-2440`）都用 `Object` 接收者，而 `Object` 接收者被 `Binder_Lookup.vb:1178` 排除出扩展查找（主因），Option Strict Off 又把「找不到」降级为静默晚绑定（副因），二者叠加使这两个用例**无法分辨扩展是否被解析**（§6.1.1，p13/p13d/p14）。因此「H 落地后既有测试不翻转」这一判断**成立但仍标推测**（依据是它们只查诊断与声明侧门——已检查；但未实现验证），且原因是**空转**而非覆盖到位。
H 因此需要**新增**用例，且必须避开这个盲区：① 正例——接收者用**非 `Object`** 类型（如 `String`，p1 的形状），跨提交调用并**断言返回值**（不能只 `VerifyDiagnostics`）；② 负例——前序提交漏 `Shared` 时后续提交应报 B/G 的诊断；③ 同一盲区也影响缺口 1 的既有用例（`ScriptExtensionMethods` 的 `o.F()`），若要求「测试真的验证能力」，需要一并补。
证据等级：**已运行**（VB 行为 p11–p14、以及本轮对 `InteractiveExtensionMethods` 的单测执行结果）+ **已检查**（C# 机制与两侧测试源码、`Binder_Lookup.vb:1176-1181` 的接收者排除条件、`VisualBasicCompilationOptions.vb:75` 的默认 Option Strict）。

**H 与其它候选的关系（正交——已检查）**：组 ① 的 A/B/C/G 治的是「脚本类自身能不能承载扩展方法」，组 ② 的 F/窄 D/D/E-a/E-b/E-c 治的是「顶层 `Module` 的落点与可达性」，H 治的是「承载了之后跨提交能不能用」；H 不改变任何单提交行为（p1 在 H 前后应不变——设计判断），落点也不与任何既有候选重叠。两条交互必须写进取舍：

- **H 降低候选 C 的代价。** C（禁止脚本类承载扩展方法）本要以「拿走一项可用能力」为代价；实测表明该能力**跨提交本就不可用**（p11），故 C 实际拿走的是「单提交内可用」这一半（已运行）。
- **H 让 B 与 G 的故事不完整。** B（补 `Shared` 缺失诊断）与 G（隐式 `Shared`）都只解决「单个提交内能不能承载」；即便采纳，跨提交扩展方法仍不可用（p11）。故「脚本里用扩展方法」这个故事要讲完整，需要组 ① 的一条（决定怎么写）**加上**组 ③ 的 H（决定跨提交可不可用）——这是本提案新增的一条组合约束（已运行 + 已检查）。

### 9. 跨提交可见性：D / 窄 D 代价的实测基线

**9.1 现状机制（实锤）。** 跨提交可见性**不走命名空间，走脚本类的成员表**：`Binder_Lookup.vb:569-598` 的 `Lookup` 在 `TypeKind.Submission` 时进入 `LookupInSubmissions`（`:583-584`），后者沿 `submission.PreviousSubmission` 逐提交调用 `LookupWithoutInheritance(submissionSymbols, submission.ScriptClass, ...)`（关键行 `:874-875`、`:885`、`:916`）；名字枚举侧同构（`:2030-2051`）。**只有脚本类的成员（含其嵌套类型）在链上可见。** 且前序提交**不是**元数据引用：`VisualBasicCompilation.Create`（`:391-439`）只把 `previousSubmission` 塞进构造（`:423`），`VisualBasicScriptCompiler.CreateSubmission`（`:208-232`）只把它作为实参传入，**没有任何 `CompilationReference`**。

**9.2 实测基线（已运行）。** REPL 提交 1 `Class C` / `Shared Function F()`，提交 2 `? C.F()` → 输出 `7`。即**当前**跨提交可见的类型是「上一提交脚本类的嵌套类型」。

**9.3 推论（已检查 + 推测/风险确凿）。**
- **D 若作用于 `DeclarationKind.Submission`（＝当前所有交互/脚本执行宿主，§2）**：上一提交的 `Class C` 与 `Module M` 都不在脚本类成员表里、也不在任何引用里 → **后续提交看不到它们**，REPL 的「定义类型 → 下次使用」会断（`Class` 与 `Module` 一起断）。三条可行解，均须会议裁定：① 按 §2 修正映射，把 D 限制在 `DeclarationKind.Script` 等于**放过 `.vbx` 执行与 REPL**（恰好是 p3/p5/p6 立论的全部场景），**不解决产品问题**；② 给 `LookupInSubmissions` 加一条「前序提交的全局命名空间」查找路径——新机制；③ 把前序提交作为 `CompilationReference` 加入引用——改动面大，且与 `ReferencesSupersedeLowerVersions`（`VisualBasicCompilation.vb:401`）交互。
- **窄 D（只搬 `Module`）**：只断模块的跨提交可见性，`Class` 基线（§9.2）保住。代价比 D 小一个量级。
- **E（不搬层）**：跨提交可见性不受影响——模块仍在脚本类成员表里，§9.1 的链不经过落点层（已检查），代价转移为 E-c 的成员查找语义改动。
- **H（提交链上再加一个扩展容器节点）**：与 §6 的缺口 4 直接对应；它不改落点层，故与本节（D / 窄 D 的跨提交代价）**互不影响**——D/窄 D 断的是「普通成员与新搬层的类型」的可见性，H 补的是「扩展方法」的跨提交收集，两者可以叠加（已检查）。
- 另注：`Binder_Lookup.vb:932`、`:2050` 各有一句上游遗留 `' TODO (tomat): extension methods`，位于提交链的成员查找/名字枚举路径上（**已检查**；可触发性未验证，见 Unresolved 9）。

### 10. 可运行示例

**10.1 当前可用的脚本顶层扩展方法写法**（p1；在 §1 覆盖的写法中，这是唯一实测可用的那条，已运行）：

```vbnet
' probe.vbx
Imports System.Runtime.CompilerServices

<Extension>
Shared Function Twice(s As String) As String   ' 必须写 Shared；漏写的后果见 §1 p2 与 §3
    Return s & s
End Function

Console.WriteLine("abc".Twice())               ' 输出 abcabc
```

**10.2 三条失效路径**（p2/p3/p4，均已运行）：

```vbnet
' 缺口 1：漏 Shared —— Debug 进程终止 / Release codegen NRE
<Extension>
Function Twice(s As String) As String
    Return s & s
End Function

' 缺口 2：顶层 Module 被降级成嵌套类型 —— 扩展方法静默失效（BC30456）
Module ExtHost
    <Extension>
    Function Twice2(s As String) As String     ' 不报 BC36551，但也不生效
        Return s & s
    End Function
End Module

' 有诊断的那条：嵌套 Class 里的 <Extension> → BC36551
Class C
    <Extension>
    Shared Function Twice3(s As String) As String
        Return s & s
    End Function
End Class
```

**10.3 对照：常规 VB（`SourceCodeKind.Regular`）里同一个 `Module` 落在命名空间层，扩展方法可用**——这正是缺口 2 的差异面（`DeclarationTreeBuilder.vb:66-76` vs `:179-196`）。脚本模式与常规模式对**同一段文本**给出不同可编译性。

**10.4 缺口 4 的最小复现（跨提交，已运行）**：

```text
> Imports System.Runtime.CompilerServices
> <Extension>
. Shared Function Twice(s As String) As String
.     Return s & s
. End Function
> ? "abc".Twice()
(1) : error BC30456: 'Twice' 不是 'String' 的成员
```

对照（普通成员跨提交正常）：提交 1 `Class C` / `Shared Function F()` → `Return 7`，提交 2 `? C.F()` → `7`（p12）。两者用同一个 REPL 会话形状，差别只在「收集的是普通成员还是扩展方法」。

### 11. 与其它单元的分工

- **脚本方言的完整声明/提交模型**（script class、`PreviousScriptCompilation`、入口点合成、`Imports` 累积）—— `proposals\proposal-scripting-dialect.md`（本文只写「顶层类型落点层」这一面，以及它牵动的跨提交可见性）。
- **消费 C# 扩展成员**（扩展属性/运算符、SAIM）—— `proposals\proposal-consume-csharp-extension-and-interface-shared.md`。
- **本缺陷的原始登记**（复现步骤、C# 对照表）—— `issues\issue-script-top-level-extension-method-crash.md`。

## Drawbacks
[drawbacks]: #drawbacks

- **B 的写法是反常的。** `Shared` 在标准模块里非法（BC30433），在脚本类里却必须写；「扩展方法」这一个概念在两个宿主里要求相反的修饰符，教学与文档成本高，且与 `spec-scripting-dialect.md:60` 那句本就别扭的规范相互加固。
- **B 不解决缺口 2。** 用户按 VB 直觉在脚本顶层写 `Module M` 放扩展方法，得到的是一条 BC30456（「`Twice` 不是 `String` 的成员」），指向调用点而非声明点，且**不提示**真正原因。这条静默失效比崩溃更难排查。
- **A 的代价是重造降级+发射模型**（§5.4），且自然产出的 IL 有 `this` 类型错配（推测，风险确凿），与「延申现有机制优先、不凭空造机制」直接冲突。
- **C 与 C# 反向分叉**，并打破既有测试与已可用写法（p1 已运行）。
- **D 改动最中央的路径，且按 §2 的映射很难同时满足产品与规范。** 限制在 `DeclarationKind.Script` 等于只覆盖编译器式脚本编译路径、放过真正出货的宿主；覆盖 `Submission` 则断掉 REPL 的类型可见性（§9.3）。规范冲击面明确（`spec-scripting-dialect.md:62`、`:66`、`:48`），且按 D5 它是**新特性**而非对齐既有模式（`decisions.md:42`）。
- **窄 D 用一个更窄的代价换同一收益**，但会留下「`Class` 跨提交可见、`Module` 不可见」的不对称语义，规范需要额外解释。
- **E 家族的代价按档递增，且各有半截边界。** E-a 只解 p6，留下「模块内可调用、脚本顶层不可调用」的半截边界；E-b 需要新遍历面与缓存；**E-c 改的是每一次名字查找的语义**（§8 的 E-c），回归风险与 IDE 名字枚举面是十一条里最大的。E 还有一层结构性错配：设计意图是「module 仿佛在外面」，而实现手段（成员查找下探）会让**常规 VB 与脚本模式的嵌套模块语义分叉**——这本身是新的方言差异面。
- **H 改的是共享编译器树的 Binding 层，且顺带要求定义「跨提交 proximity」。** 改动落在 `BinderFactory.vb` / `NamedTypeBinder.vb`（或新增 binder 类型），须进 `upstream-merge.md` 台账并过 3-way 评审；`Binder_Lookup.vb:1206`/`:1256` 的 proximity 目前按 binder 链层级递增，提交链是否计入、与 C# 如何对齐**未验证**（Unresolved 13）。此外 H 的落点若只改**经典扩展方法**分支，则**平行的那一条**（C# 14 扩展成员，走 `AppendProbableExtensionMembers`/`GetExtensionMembers`，`NamedTypeSymbol.vb:387-422`，同为链驱动）是否同样跨提交不可用、是否要一并处理**未验证**（标**推测**：同一链机制的缺口应同形，但未实测）。
- **缺口 4 让候选 C 的代价下降、让 B 与 G 的叙事不完整（新增两条取舍约束）。** C 本要以「拿走一项可用能力」为代价，实测表明该能力**跨提交本就不可用**（p11）；B/G 只解决单提交内的承载，即便采纳，跨提交仍报 BC30456（p11）。**若会议只取组 ① 而不取 H，则用户拿到的仍是一条跨提交断掉的能力**——这条必须写进会议决议的使用说明或规范限制。
- **现有测试的成色比「有正向测试」看起来更薄。** 两个既有脚本内扩展方法测试都空转，原因分两层：**主因**是 `Object` 接收者被 `Binder_Lookup.vb:1178` 的 `Not container.IsObjectType()` 排除出扩展查找（与 Option Strict 无关，判别探针 p13d 得 BC30574 而非扩展被采用）；**副因**是 Option Strict Off 把「找不到」降级为静默晚绑定，于是连诊断也没有（§6.1.1，已运行 + 已检查）。它们既不能证明能力可用，也不会在能力被破坏时报警——这既解释「为什么缺口长期没人发现」，也是本提案在补测试时必须一并处理的债务（Unresolved 15）。
- **按 D5 的核对，候选 D / 窄 D 是「新特性」而非「对齐既有模式」。** 需更正一个易被误读的对照：`decisions.md:42` 明确「**脚本模式下两侧其实同形**」（C# 与 VB 都把顶层类型嵌进脚本类），差异只在 C# 另有的「文件式程序 / 顶层语句」模式（`acceptSimpleProgram`），而 **VB 没有这个模式**（全树无 `SimpleProgram`/`TopLevelStatements` 命中）。故把脚本里的类型搬回命名空间层在 VB 侧是**新增一条路径**，须评估与 `.vbx` 语义、`#Load`、`Submission` 链的兼容，不按移植修补对待（`decisions.md:42`，已检查）。
- **E、B、G 都踩在「脚本类 `Shared` 成员」这片雷区上。** ⑦ 在语法层成立（p9），但 p10 证明顶层 `Shared` 字段带初始化器会让宿主 `TypeLoadException`（已登记）。**裁决 E / B / G 时不应假定「脚本类的 `Shared` 成员整体可用」**——这条假设在共享字段初始化器上已被证伪。
- **G 的隐式规则与实例上下文代价。** 隐式 `Shared` 会让「加了 `<Extension>` 的顶层成员」失去实例上下文（不能访问顶层 `Dim`、不能用 `Me`），与 `spec-scripting-dialect.md:60` 的现状冲突；且实现上必须解决「属性识别晚于修饰符判定」的顺序问题（§7 G 的路线 (a)/(b)），路线 (b) 有循环依赖风险。
- **F 只是「把静默变响」。** 它不改变任何能力；若用户真正想要的是「在脚本顶层写 `Module` 放扩展方法」，F 只告诉他不行，不给替代路径（替代路径需要 D / 窄 D / E-b，或把扩展方法写在脚本类上并用 `Shared`）。
- **Fork 分叉成本（十一条共性）。** 任何新增诊断都进 fork 差异面（`upstream-merge.md` 台账）并牵动 13 个语言 xlf 同步（已采纳的做法）；上游 VB 从未有过「扩展方法必须 `Shared`」诊断，引入后与上游错误码流并行演进的窗口需要维护（码位见 Unresolved 6）。
- **各条候选都有代价，没有一条只需付收益。** A 已证伪；B 只补通知；C 收敛能力；F 只把静默变响；G 带来一条隐式规则与实例上下文损失；H 改共享编译器树的 Binding 层并要求定义跨提交 proximity；D/窄 D 断跨提交可见性；E 家族改动面递增且 E-c 改查找语义。若期望的是「脚本里写扩展方法更顺手」，**单取组 ①（B 或 C 或 G）不够**：组 ① 只决定「怎么写」，跨提交能不能用由组 ③ 决定（p11 已运行），故需要**组 ① 的一条 ＋ H**；缺口 2（顶层 `Module` 的落点）另按需配 D / 窄 D / E-b 或只取 F（把静默变响）。

## Alternatives
[alternatives]: #alternatives

- **A. 给脚本类开口子（非 `Shared` 成员当扩展方法）。** **已证伪**（§5）。保留此条仅为记录原设想与实证结论。
- **B. 维持现状 + 补诊断。** 可行且最小；不动 IL 风险面。缺点见 Drawbacks。与 C# 的 `ERR_BadExtensionMeth` 同构。
- **C. 撤销 `AllowsExtensionMethods` 的 `IsScriptClass` 子句。** 可行但代价明确：破既有正向测试与已可用写法，且与 C# 反向分叉。
- **G. 只对带 `<Extension>` 的顶层成员隐式 `Shared`。** 与 B 互斥的第三条路：不走 IL 形状改动、不把现状定为非法，而是让现状合法化。真障碍是属性识别与修饰符判定的顺序（§7 G）。
- **F. 只补诊断、不改能力（顶层 `Module` 的 `<Extension>` 收集不到时报出来）。** 现状连 BC36551 都不报（`AllowsExtensionMethods()` 的 `TypeKind.Module` 分支放行），F 补上声明点诊断；与 B 落在同一段序列，与 D / 窄 D / E-b 互补。
- **D. 顶层类型全部留在命名空间层。** 与「C# 顶层语句模型」「常规 VB」两侧同构；但按 §2 的映射，它的可用范围与跨提交代价需要会议明确（§9.3），且按 D5 的核对它是**新特性**而非对齐既有模式（`decisions.md:42`，见 Drawbacks）。
- **窄 D. 只把 `Module` 放回命名空间层。** 改动量一个条件表达式（`DeclarationTreeBuilder.vb:184`），解缺口 2，代价限于模块的跨提交可见性。与 D 并列，不是 D 的子集——它**有意**保留 `Class` 的嵌套以保住 §9.2 的基线。
- **E. 不搬层、改语义作用域（module 仿佛在脚本类外面）。** 三档分解定价见 §8；⑧ 与 ⑤ 是两件事，应分开决策。
- **H. 给提交链加「扩展容器」节点（对齐 C# `InSubmissionClassBinder`）。** 治缺口 4，与 A/B/C/D/窄 D/E-a/E-b/E-c/F/G **正交**（那些治「能不能承载」，H 治「承载后跨提交能不能用」）；对齐 D5 的「以 C#/csi 为蓝本」口径，且 C# 侧有机制 + 跨提交测试可对照。
- **「保留嵌套、只放宽 `MightContainExtensionMethods`」的窄变体（＝ E-a）。** **按场景分别定价**：解 p6（已检查，实跑为推测），**不解 p3**（已检查；「需新造收集点」为推测）。把它整体判为「不可行」是过宽的结论——它是 E 家族的**第一档**，有独立收益，只是收益范围小于缺口 2 的全貌。
- **不改编译器，只在文档/规范层禁止脚本里的扩展方法。** 不成立：p2 的失败模式是**断言终止进程 / NRE**，文档禁令无法阻止用户写出该代码（已运行）。
- **给整个脚本类加隐式 `Shared`。** 不成立：脚本类的实例成员是顶层 `Dim` 状态与顶层 `Sub`/`Function` 互调的前提（`spec-scripting-dialect.md:60`、`:64` 的 initializer 模型），改成隐式共享会推翻整个脚本方言的实例模型（已检查，仅记录为被排除项）。**注意此条与候选 G 不同**：G 只作用于带 `<Extension>` 的顶层成员，不触碰实例成员——上述否决理由不适用于 G（已检查）。

### 与 C# 的对照（汇总表见 §7）

C# 的同一问题有**三段**检查 + **正反两面**测试，且显式豁免脚本类；VB 只移植了前两段与正向测试。这不是设计分歧，是移植缺口。本文核对到的**有意分叉**只在容器白名单的形状上（C# 用「非静态且非脚本类」判定，VB 用「模块或脚本类」正向放行），两者语义等价（已检查）。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **十一条候选如何取舍（核心未决，会议裁定）。** 十一条并列摆出（§8，按缺口分三组）。关键张力：A 已证伪；B 最小但不解决缺口 2 也不解决缺口 4；C 与 C# 反向分叉但**其代价因缺口 4 而下降**（该能力跨提交本不可用，p11）；G 与 B 互斥（合法化 vs 报错）；F 只补诊断不改能力；D 按 §2 的映射要么放过产品路径、要么断 REPL；窄 D 用更窄的代价换缺口 2；E 不破跨提交可见性但改动面最大；H 与其余十条正交。**组合关系**：面向缺口 2 的候选（F/窄 D/D/E-b）与面向缺口 1 的候选（B/C/G）可组合；D / 窄 D / E-b 若采纳，B 或 G 仍需独立裁定（它们解决的是脚本类自身那一条路径），且会同时出现两套书写位置（脚本类上的 `Shared` 扩展方法 + 模块里的普通扩展方法），规范需给出推荐写法；**「脚本里用扩展方法」要讲完整，需要组 ① 的一条（怎么写）加上组 ③ 的 H（跨提交能不能用）**。
2. **候选 D 的作用域如何界定（按 §2 修正后的问法）。** 应问：**D 是否作用于 `DeclarationKind.Submission`——即当前所有交互/脚本执行宿主？** 若否（只作用于 `DeclarationKind.Script`），则只覆盖编译器式脚本编译路径，**放过 `.vbx` 执行与 REPL**，不解决 p3/p5/p6 的场景。若是，则 §9.3 的三条可行解（限 kind / 扩查找 / 加 `CompilationReference`）需逐条定价，并说明与 `spec-scripting-dialect.md:98-104`「script class vs submission class」两分法的关系。**另一类宿主需一并纳入范围说明**：编译式脚本编译（`<Main>` 路径，`DeclarationKind.Script`，§2）——它在产品内目前无触达点（推测），但在编译器测试里被覆盖（`ScriptExtensionMethods`）。
3. **是否必须「所有」顶层类型一起搬？** 此前只问了 `Dim`/语句/`#Load` 是否受影响，从未问这个（窄 D 即由此产生）。会议需裁定：类型落点层是按 kind 一刀切，还是允许 `Module` 与 `Class` 分置？分置会引入不对称语义（见 Drawbacks），需规范给出理由。
4. **E 的三档如何取舍（⑧ 与 ⑤ 分开决策）。** ① E-a（只放宽 `:3341`，解 p6）——是否接受「模块内可调用、脚本顶层不可调用」的半截边界？② E-b（放宽＋收集点下探，解 p3）——新遍历面与缓存的成本是否可接受？③ E-c（成员作用域下探，解 ⑧）——改每一次名字查找的语义是否值得？三档可否只取其一？**⑧ 与 ⑤ 是不同机制**（成员查找 vs 扩展收集），不应打包决策。
5. **B 的新诊断与既有编号的先后与措辞。** `SourceMethodSymbol.vb:1624-1648` 的分支顺序决定「无参 + 非 `Shared`」时报 BC36552 还是新码。**C# 的实际顺序是：首参检查在 `:213-229`，`else if (!IsStatic)` 在 `:243`——「非 static」在序列末尾、且在首参检查之后独立成支**（已检查）。VB 是否照抄这个形状（把新分支放在 `:1630` 的 `ParameterCount` 检查之后、`:1633` 的 `Else` 之前/之内）？消息措辞有三种候选（对齐 C#「Extension method must be static」的直译／对齐 VB 现有 `ERR_ExtensionMethodNotInModule` 的句式／点明 `Shared` 关键字），**待定**。
6. **新错误码的码位（待定）。** 按 `Errors.vb:1601-1606` 的 fork 策略需占官方中间空档：已用 36959 / 36967 / 37002-37004；`37005-37049` 现为空（已核对，无定义），`37005` 是自然选择；`369xx` 段在 36983 之后即为官方前沿（36984+）无空档可用。需同时确认 13 个 xlf 与 `VBResources.resx` 的同步面（xlf 须符合原版 VB 编译器 xlf 规范）。
7. **早期解码的守卫方式（待定）。** §3.4 建议在 `SourceMethodSymbol.vb:1500-1502` 补 `Me.IsShared`；替代做法是把 `:1504` 的 `Debug.Assert` 改成 `If Me.IsShared Then ... End If`。两者对 `IsExtensionMethod` 的可见性、以及对 `SourceMemberMethodSymbol.vb:86-89` 快速属性过滤的影响是否等价，**未验证**。
8. **负向测试与回归面归属（待定）。** 负向测试落点应是 `ExtensionMethodTests.vb`（与 `ScriptExtensionMethods` 并列），但该测试受 `ConditionalFact(NoUsedAssembliesValidation)` + roslyn issue 40680 影响会跳过，新测试是否沿用同一门、是否需在 `VisualBasicSemanticTest` 另加不受门控的用例，**待定**。并且：**既有正向测试只覆盖两种 kind 各自的 `Shared` 形式，负向零覆盖**；若候选按 kind 切分作用域（§2、Unresolved 2），需为 `Script` 与 `Submission` **两种 kind 各补**一条负向用例。
9. **`Binder_Lookup.vb:932` / `:2050` 的两处 TODO 与候选 H 的关系（已定性，收口方式待定）。** p11 已实测确认这两处 TODO 描述的就是缺口 4（不是悬置的其他问题），因此本条**不再是 Suspect**，转为 H 的一部分：H 落地时是「删掉这两句 TODO」还是「保留并说明扩展收集已由新节点承担」，需要在实现时定；其中 `:932` 位于 `LookupInSubmissions`（成员查找已完成）而 `:2050` 位于名字枚举侧，**名字枚举（IDE 补全）是否也要跨提交收集扩展方法**，C# 侧对应物（`AddMemberLookupSymbolsInfoInSubmissions`）与 VB 的差异**未验证**。
10. **窄 D 的可行性前提（待定）。** 窄 D 假定「REPL 用户对 `Module` 的跨提交依赖是低频的」——这需要产品判断而非源码证据：需确认 vbi REPL 与 `.vbx` 用户是否会把顶层 `Module` 当作状态载体（若是，窄 D 的代价升到接近 D）。同时需确认窄 D 与 B/F 组合时，规范如何解释「模块扩展方法可用、脚本类扩展方法必须写 `Shared`」的双轨写法。
11. **候选 F 的码位与脚本作用域（待定）。** F 的诊断是**脚本专属**还是**通用 VB**？语言层依据（模块只能在文件/命名空间层，BC30617）是通用的，因此通用诊断在原理上也成立；但通用化会改变常规 VB 的既有行为面（常规用户写不出嵌套 `Module`，故实际影响面小），脚本专属则需在判据里加 `IsScriptClass`。另外 F 与 B 落在 `SourceMethodSymbol.vb:1624-1648` 的**同一段序列**：两条诊断是否共用一个新码、还是各用一个、以及「嵌套模块 + 非 `Shared`」能否同时成立（模块成员隐式 `Shared`，故该组合不可达——已检查）都需裁定；码位见 Unresolved 6。
12. **候选 G 的实现路线与隐式范围（待定）。** ① 路线 (a)（把属性识别前移到 `Binder_Utils.vb:91` 所在的修饰符翻译点）与 (b)（符号 `IsShared` 覆盖）如何取舍？(a) 需要确认 `QuickAttributeChecker` 在装饰阶段可用且其判定与后续完整解码一致；(b) 有循环依赖风险。② 隐式 `Shared` 的范围是「所有顶层成员」还是「仅带 `<Extension>` 的成员」？后者不触碰实例模型（已检查），前者会推翻脚本方言的实例模型。③ 是否需要在「隐式生效」时给出信息级提示或文档说明，以免「同段代码加个属性就换了共享性」难以察觉？④ G 与 F 的交互：G 生效后，脚本类的 `<Extension>` 成员不再需要 `Shared`，但**嵌套模块**的扩展方法仍不可达（G 不作用于模块）——两条缺口不会被 G 一起关掉（已检查）。

13. **候选 H 的取舍、实现形态与跨提交 proximity（待定）。** ① 形态 (a)（新增 binder 类型，对齐 C# `InSubmissionClassBinder`）与 (b)（在 `NamedTypeBinder` 的脚本类分支里遍历提交链）如何取舍？(a) 与 C# 同形、便于日后对照，但新增共享树类型；(b) 改动小，但把提交链知识塞进通用 binder，与 D5「以 C# 为蓝本」的口径有张力。② **跨提交的 proximity 语义**：`Binder_Lookup.vb:1206` 的 `proximity` 从 0 起、每上一级 binder `+= 1`（`:1256`），前序提交的候选算第几级？C# 侧如何算（`InSubmissionClassBinder` 遍历时把结果放进同一批候选，proximity 由链位置决定）**未验证**；错的定义会让「同名扩展方法在新旧提交各有一份」时的重载决议行为与 C# 分叉。③ 跨提交遍历是否引入重复收集（同一方法被当前提交与链上两层都取到）——`seenContainingTypes`（`:1214`、`:1227-1248`）按 containing type 去重，跨提交的 containing type 互不相同，故理论上不重复（**推测**）。④ H 与组 ① 的组合方式（H 单独 / H+B / H+G）以及是否需要在同一轮落地。
14. **缺口 4 对 spec 的冲击面（待定）。** `spec\spec-scripting-dialect.md:66` 规定顶层声明的「Scope」是「本编译单元的顶层代码 + 链上更晚的提交」；跨提交扩展方法一旦可用，其可见性归属（是「成员可见」还是「扩展收集可见」）需要规范明确——现状是**成员可见但扩展不可收集**，这是一条规范没写、行为又反直觉的中间态。同时 `:98-104` 的 script/submission 两分法与 H 的落点（只在 submission 链上有意义，`DeclarationKind.Script` 无链）需要一并说清。
15. **既有两个正向测试要不要一并修（待定，新增）。** `ScriptExtensionMethods` 与 `InteractiveExtensionMethods` 都用 `Object` 接收者，因此只能查「声明侧门」（`MightContainExtensionMethods`）与「零诊断」，查不到「扩展是否被解析」。原因已定位到两层（§6.1.1）：**主因**是 `Binder_Lookup.vb:1176-1181` 的 `ShouldLookupExtensionMethods` 在 `:1178` 用 `Not container.IsObjectType()` 把 `Object` 接收者排除出扩展查找；**副因**是 Option Strict Off 的静默晚绑定（`VisualBasicCompilationOptions.vb:75`），判别探针 p13d（`Option Strict On`）得 BC30574 而非扩展被采用（均已运行）。H 落地时是否**同时**把它们改成非 `Object` 接收者（如 `String`）并加返回值断言？若不改，缺口 1 与缺口 4 都会缺一条真正有效的正向测试；若改，则要评估它们是否会从「通过」变成「失败」——按本提案的实证，**改后应当失败**（p11），这正是需要补的能力。该项也牵动 `Compilers\VisualBasicSymbolTest` 的门控（`ConditionalFact(NoUsedAssembliesValidation)` + roslyn issue 40680，见 Unresolved 8）。
16. **「编译器式路径（非宿主）是否同样受缺口 4 影响」的确认方式（待定）。** 本提案的判据是：两条路径共用同一份编译器、且源码中**不存在**跨提交的扩展收集代码，故缺口对编译器式路径同样成立（`BinderFactory.vb:172-177`、`NamedTypeBinder.vb:99-107`、`Binder_Lookup.vb:1186-1262` 的链遍历，已检查）；但 p11 的实测是在 Scripting 宿主上做的，**编译器级**（`CreateScriptCompilation` + `previousScriptCompilation` + 非 `Object` 接收者）的直接见证**尚无**（既有那个测试因晚绑定而无效）。该结论因此标**推测**，需要一条新测试来落定——这也是 H 的验收条件之一。

## 相关文档

- `decisions.md:35-46`（D5）— 基础功能以 C#/csi 实现为蓝本的定案；本文的缺口 1（D5 已证实例 2，`:41`）、缺口 2 的落点层核对（`:42`）、缺口 4（D5 点名的「名字查找」区域，`:37`）均与 D5 对齐；正向测试覆盖错 kind 一项见 `:43`
- `proposals\proposal-with-events-in-submissions.md` — 同批按 D5 做 C#/csi 对照的姊妹提案（其对象是提交类的 `WithEvents`/`Handles`，与本提案共享「提交链 + 基础机制移植不完整」的问题域）
- `issues\issue-script-top-level-extension-method-crash.md` — 缺口 1 的原始登记（复现步骤、C# 对照表、规范发现路径）
- `issues\issue-submission-shared-field-initializer-typeload.md` — **另一处独立缺陷（Open）**：顶层 `Shared` 字段带初始化器 → 宿主 `TypeLoadException`。链（与本文 §8「E 与 ⑦ 的边界」一致，行号已按实读校正）：`SourceMemberContainerTypeSymbol.vb:2726-2737`（`:2729` 的 `Not isShared OrElse …` 放行共享分支、`:2735` 透传 `isShared`）→ 共享时 `MethodKind = SharedConstructor`（`Compilers\VisualBasic\Portable\Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb:190-194`）、`Name = .cctor`（同文件 `:59-63`；`WellKnownMemberNames.cs:29`）→ `MethodCompiler.vb:1535-1537` 的 `MakeSubmissionInitialization` **不会**被挂（经 `IsScriptConstructor`（`MethodSymbol.vb:517-521`）/ `IsSubmissionConstructor`（`:529-533`）被挡）→ 真正的断点在 **`SynthesizedSubmissionConstructorSymbol.vb:31-38`**。**本提案不解决它**，仅作为「候选 B / G 与设计意图 ⑦ 所在区域的已知风险」登记
- `spec\spec-scripting-dialect.md:33-35`、`:48`、`:60`、`:62`、`:66`、`:98-104`、**`:163`** — 脚本方言的规范出处与本提案冲击面（`:163` 是「kind 由是否 submission 决定、不由扩展名决定」的权威口径）
- `InternalDevDocs\vblang\spec\type-members.md:908` — 「They can only be declared in standard modules」（上游规范出处）
- `InternalDevDocs\vblang\spec\types.md:689` — 标准模块成员的作用域是「其所在命名空间的声明空间」（缺口 2 的规范层根因）
- `proposals\proposal-scripting-dialect.md` — 脚本方言的声明与提交模型（本提案的父域）
- `proposals\proposal-consume-csharp-extension-and-interface-shared.md` — 消费 C# 扩展成员（扩展能力的另一端）
- `Compilers\CSharp\Portable\Symbols\Source\SourceOrdinaryMethodSymbol.cs` / `Compilers\CSharp\Portable\Declarations\DeclarationTreeBuilder.cs` — C# 对照基准（三段检查 + 顶层语句的命名空间层落点）
- `upstream-merge.md` — fork 差异面台账（新增诊断须登账）
