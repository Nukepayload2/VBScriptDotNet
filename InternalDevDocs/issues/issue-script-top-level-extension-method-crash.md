# [BUG] 脚本顶层 `<Extension>` 成员缺 `Shared` 时无诊断：Debug 断言终止进程，Release 落到 codegen NRE

- **状态**：Open
- **发现**：2026-09-10（复核 `spec-scripting-dialect.md` 的脚本类修饰符段落时附带发现）
- **严重度**：中高（Debug 构建**进程被断言终止**，非普通诊断；输入是普通用户可写的顶层代码）
- **影响面**：`.vbx` 脚本与交互式会话（任何 `SourceCodeKind.Script` / `Submission` 编译的宿主，含 vbi REPL、`vbi script.vbx`、`Scripting` API 消费者）。`vbc` 不产生脚本编译，不受影响。
- **版本**：分支 `with-modified-vbsyntax` 工作树

## 现状事实（先钉住「什么能跑」）

脚本顶层声明扩展方法**是受支持的能力**，与标准模块同列：

| 写法（脚本顶层） | 结果 |
|---|---|
| `<Extension> Shared Function Twice(s As String) As String` | ✅ 正常工作，`"abc".Twice()` 输出 `abcabc` |
| `<Extension> Function Twice(...)`（无 `Shared`） | ❌ 见下「实际」 |
| 嵌套 `Class` 内的 `<Extension>` | ✅ 正确报 BC36551（`ERR_ExtensionMethodNotInModule`） |

三条均为**已运行**实证（2026-09-10，Debug 版 `vbi.exe` 直跑 `.vbx`）。

## 复现步骤

写入 `probe.vbx`：

```vb
Imports System.Runtime.CompilerServices

<Extension>
Function Twice(s As String) As String
    Return s & s
End Function

Console.WriteLine("abc".Twice())
```

运行 Debug 版 `vbi probe.vbx`。

## 实际

**Debug 构建**：进程被断言终止，退出前打印

```
Process terminated.
Assertion failed.
Me.IsShared
   at Microsoft.CodeAnalysis.VisualBasic.Symbols.SourceMethodSymbol.EarlyDecodeWellKnownAttribute(...)
      in Compilers\VisualBasic\Portable\Symbols\SourceMethodSymbol.vb:line 1504
```

**Release 构建**（2026-08-23 版编译器，Debug 断言被编译掉）：越过断言后在 codegen 抛 `NullReferenceException`

```
System.NullReferenceException
  at Microsoft.CodeAnalysis.VisualBasic.CodeGen.StackScheduler.Analyzer.VisitCall(BoundCall)
     in Compilers\VisualBasic\Portable\CodeGen\Optimizer\StackScheduler.Analyzer.vb:663
```

（Release 一条来源于**较旧**的 Release 二进制，仅用于说明「断言不是唯一防线」；NRE 与断言的因果链为**推测**，修复时需在当期构建上复测。）

## 预期

`<Extension>` 施加于脚本类的**实例**成员时，应有一条**编译诊断**（而非断言/NRE）。

理由：扩展方法在 VB 中必须落在 `Shared` 成员上——标准模块的成员全部隐式 `Shared`，所以该前提在模块内**不可能被违反**，也就从未需要一条用户可见的诊断；脚本类把这个前提打破了，而唯一的把关点只是一条 `Debug.Assert`。

### C# 对照（证据等级：**已检查**）

这不是 VB 独有的设计难题，C# 侧是同一个问题且**已经解掉了**，两边的容器白名单形状完全平行：

| | C# | VB |
|---|---|---|
| 容器白名单 | `SourceOrdinaryMethodSymbol.cs:234` —— `!ContainingType.IsScriptClass && !(ContainingType.IsStatic && ContainingType.Arity == 0)` 才报错 | `NamedTypeSymbolExtensions.vb:110` —— `TypeKind.Module OrElse IsScriptClass` 才放行 |
| 嵌套类型 | `:230-232` → `ERR_ExtensionMethodsDecl` | `SourceMethodSymbol.vb:1627-1628` → BC36551 |
| **成员必须 static/Shared** | `:243-245` → **`ERR_BadExtensionMeth`（CS1105，「Extension method must be static」）** | **无对应诊断——只有 `Debug.Assert(Me.IsShared)`** |
| 首参数不能 Optional/ParamArray | 有 | 有（BC36548/BC36554 邻域） |

C# 的脚本扩展方法行为带**正反两面**的既有测试：`Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs:1110-1118`

```csharp
// No error for extension method defined in interactive session.
var s0 = CreateSubmission("static void E(this object o) { }", references);
var s1 = CreateSubmission("void F(this object o) { }", references, previous: s0);
s1.VerifyDiagnostics(
    // (1,6): error CS1105: Extension method must be static
    Diagnostic(ErrorCode.ERR_BadExtensionMeth, "F"));
```

即：**C# 的 `.csx`／交互式顶层允许扩展方法**（`IsScriptClass` 白名单，注释明写 "No error for extension method defined in interactive session"），**但必须 `static`**，否则 CS1105。

**结论：VB 是移植不完整的**——白名单与嵌套诊断都照搬了，唯独把「必须 static」这一条从**用户可见诊断**降级成了 `Debug.Assert`。

VB 侧的测试也是同心的半边：**正向有、负向无**。`Compilers\VisualBasicSymbolTest\SymbolsTests\ExtensionMethods\ExtensionMethodTests.vb:2425-2440` 的 `ScriptExtensionMethods` 正是正向用例——`<Extension> Shared Function F(o As Object)` + `TestOptions.Script` + `comp.VerifyDiagnostics()` + `Assert.True(comp.SourceAssembly.MightContainExtensionMethods)`；对应 C# 侧的 `ExtensionMethodTests.cs:3790-3802` 是同形状的 `static object F(this object o)`。**负向**（漏写 `Shared`）在 VB 侧没有任何用例，C# 侧则由 `ScriptSemanticsTests.cs:1110-1118` 钉住 CS1105。

> 附注：`ScriptExtensionMethods` 标着 `<ConditionalFact(GetType(NoUsedAssembliesValidation))>` 并挂了 roslyn issue 40680，在部分配置下会被跳过——这也是本缺陷长期未被逮到的原因之一（证据等级：**已检查**，标签已读；「跳过时是否覆盖本场景」为**推测**）。

修复方向因此收窄为一个：**补一条 C# `ERR_BadExtensionMeth` 的对应诊断**（「扩展方法必须声明为 `Shared`」类），落点在 `SourceMethodSymbol.vb:1624-1634` 那条分支序列内，`AllowsExtensionMethods`/`IsScriptClass` 均无需改动。码位按 `Errors.vb:1601-1606` 的 fork 编号策略取官方中间空档；具体码位与消息措辞待定——**需用户定夺方向**（是否走 proposal→meeting）。

## 根因（源码核实，证据等级：**已检查**）

1. **脚本类被列入「可承载扩展方法的容器」**：`Compilers\VisualBasic\Portable\Symbols\NamedTypeSymbolExtensions.vb:108-111`

   ```vb
   Friend Function AllowsExtensionMethods(container As NamedTypeSymbol) As Boolean
       Return container.TypeKind = TypeKind.Module OrElse container.IsScriptClass
   End Function
   ```

   这是全 VB 侧唯一的容器判据，被两处消费（下条）。
2. **`IsScriptClass` 覆盖脚本与提交两种类**：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb:1295-1300`——`kind = DeclarationKind.Script OrElse kind = DeclarationKind.Submission`。
3. **放行即不报 BC36551**：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMethodSymbol.vb:1621-1634`，`:1627-1628` 的 `ElseIf Not m_containingType.AllowsExtensionMethods()` → BC36551；脚本类使该分支不成立，于是**跳过诊断**继续往下。属性早解码侧同形：`SourceMethodSymbol.vb:1496-1521`，`:1500-1502` 用同一判据进门，`:1504` 直接把 `Me.IsShared` 写成断言。
4. **`Shared` 前提在模块内不可能被违反，故无诊断可复用**：`SourceMemberMethodSymbol.vb:86-89` 的快速属性过滤只按容器类型决定是否保留 `Extension` 属性，不看 `Shared`。全仓 `ERRID` 中未见「扩展方法必须 Shared」类诊断（`Compilers\VisualBasic\Portable\Errors\Errors.vb:1327-1336` 邻域仅 `ERR_ExtensionOnlyAllowedOnModuleSubOrFunction`(36550) / `ERR_ExtensionMethodNotInModule`(36551) / `ERR_ExtensionMethodNoParams`(36552) 等，均已检查）。
5. **脚本类顶层方法默认是实例成员**，这正是 `IsShared = False` 的来源：`Compilers\VisualBasic\Portable\Declarations\DeclarationTreeBuilder.vb:143` 把脚本类建成 `Friend Or Partial Or NotInheritable` 的**普通类**（非模块），顶层 `Sub`/`Function` 即实例成员；只有显式写 `Shared` 才会共享。见 `InternalDevDocs\spec\spec-scripting-dialect.md` 的「Top-level `Sub` and `Function` are instance members」。

## 与文档的关联（本 issue 的发现路径）

`spec-scripting-dialect.md` 原有一句「A script class is not `Shared` and contains no extension members.」——该句是 `Compilers\CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:292` 的 C# 注释 `//Script class is not static and contains no extensions.` 被逐字移植到 `Compilers\VisualBasic\Portable\Declarations\DeclarationTreeBuilder.vb:192` 后，再被我误译进 VB 规范：

- 「not `Shared`」在 VB 中**无内容**——`Shared` 是成员修饰符而非类型修饰符（`InternalDevDocs\vblang\spec\types.md` 与 `type-members.md` 中 `Shared` 仅以成员修饰符出现），VB 没有 `Shared Class` 这种形式，否定一个不存在的东西不构成断言。
- 「contains no extensions」在上表**被证伪**：脚本类**可以**承载扩展方法。

规范该句已按实证改写（`spec-scripting-dialect.md`:48 附近，并补 `[vblang-types]` 链接定义）。

## 相关

- `InternalDevDocs\spec\spec-scripting-dialect.md`——本缺陷的发现路径与现状语义落点。
- `InternalDevDocs\vblang\spec\type-members.md:908`——「They can only be declared in standard modules」；本 issue 是该上游规则在脚本类上被放宽后遗留的诊断空洞。
- `InternalDevDocs\vblang\spec\types.md:689`、`:728`——标准模块语义（成员隐式 `Shared`、**永不可实例化**）；脚本类与它相反（每提交实例化一次）。
- `InternalDevDocs\issues\issue-vbi-imports-switch-nre.md`——同一轮「先实证、再登记」流程的前例。
