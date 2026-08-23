# 测试计划：消费 C# 扩展成员与接口共享成员（consume-csharp-extension-and-interface-shared）

> 状态：测试计划（F3）。依据链：`../../proposals/proposal-consume-csharp-extension-and-interface-shared.md`（Active，实证基线 + 目标示例）→ `../../meetings/meeting-consume-csharp-extension-and-interface-shared.md`（RESOLUTION 六条）→ `design-overview.md`（F1，行为对照表 §3）→ `design-detailed.md`（F2，改动点 1-8 + §12 设计级矩阵）→ 本测试计划。
> 本计划把 design-detailed §12（设计级矩阵）升级为**分层测试设计**：参考 C# extensions / static-abstracts 测试强度（`Compilers\CSharp\Test\` 中 `ExtensionTests` / `StaticAbstractMembersInInterfacesTests` 语义强度），并按 **VB 特性维度**（Strict On/Off 分叉、`Object` 接收者晚绑定、`Imports` 作用域、同名优先级、`static abstract` 仅经类型参数 vs `static virtual` 经接口名、`With`/链式、`ExtensionMarkerNameAttribute` 术语、`.vbx` 与 Regular 同 kind）综合设计。
> 实现阶段由实施者照本计划补测试，验证者按「无副作用纪律」与「行为对照表」核对；**标「实现期核对」的项为未核实项，如实标注待回填，不越权承诺**（对齐 design-detailed §11 实现期核对项清单）。

## 0. 目标与验收

- **目标**：在 C# 测试强度（语义诊断 → REPL 全输出 → 显示断言 → Regular 对照）之上，覆盖「消费 C# 14 扩展成员（Part A：扩展属性/运算符）」与「消费 C# 11 接口共享成员（Part B：SAIM `T.Zero`/`T.Add`）」的全部边界，保证：
  1. **扩展属性 `.P`** 早绑定命中（`"hello".CharCount` → 5），setter/链式/`With` 对称消费；
  2. **扩展运算符 `a+b`** 早绑定命中，Strict On/Off 均编译期解析（消除实证里 Strict Off 静默晚绑定运行期失败）；
  3. **`T.Zero`/`T.Add` 泛型算法** 可写 VB 版（`VBSum(Of MyNum)` → 30），emit 走 `constrained.` + 虚调用；
  4. **Strict On/Off 分叉**：扩展成员仅早绑定参与，`Object` 接收者维持晚绑定（`ShouldLookupExtensionMethods` 门保留）；
  5. **同名优先级**：实例成员优先 → 扩展成员回退；组内扩展成员须唯一；
  6. **`static abstract` 仅经类型参数 `T.M`、`static virtual`（DIM 静态）可经接口名呼**；
  7. **`ExtensionMarkerNameAttribute` 术语识别**：VB PE 层按元数据实际属性名识别（实现期核对，design-detailed §11 核对项 1）；
  8. **运行时不受支持门控**：复用 `SupportsRuntimeCapability(VirtualStaticsInInterfaces)` 钩子，不受支持报编译错误（诊断码族实现期核对）；
  9. **`Imports` 作用域一致**、**`.vbx` 与 Regular 同 kind**；
  10. **VB 声明侧维持现状报错**（BC30270/BC30273，范围只消费）。
- **验收**：四层矩阵全绿；既有测试按 §7 更新（**含 `StaticAbstractMembersInInterfacesTests.vb` 消费用例 BC32098 翻转**）；无副作用纪律满足（§8）。

## 1. 参考的 C# 测试强度（分层）

C# 对同一特性的测试锚点与强度：

| 层 | C# 测试锚点 | 强度特征 | VB 落点 |
|---|---|---|---|
| L1 语义层 | `CSharp\Test\Semantic\Semantics\ExtensionTests.cs`（扩展成员）、`StaticAbstractMembersInInterfacesTests.cs`（SAIM，泛型数学 `T.Zero`/`T.Add`） | 精确 `VerifyDiagnostics` 断言错误码与位置；同一特性正反对照（可用面/受限面）；C# 源自建库 emit → VB 消费 | `Compilers\VisualBasicSemanticTest\`（`CreateSubmission` / `VisualBasicCompilation.Create` + `AssertTheseDiagnostics`/`VerifyDiagnostics`，`BasicTestBase.vb`） |
| L2 REPL/脚本 | C# csi 端到端（`CommandLineTests.cs`） | 全输出断言（错误块 + 运行输出）；`.vbx` 脚本与交互提交 | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`（`CreateRunner(input:=...)` + `TestConsoleIO`） |
| L3 显示层 | C# `SymbolDisplay` 对扩展成员 / SAIM 符号显示测试 | 字符串精确断言 + 元数据层符号属性 | `Compilers\VisualBasicSymbolTest\SymbolsTests\`（`StaticAbstractMembersInInterfacesTests.vb` 先例） |
| L4 常规模式 | C# 普通 `.cs` 项目消费扩展成员/SAIM | 编译 API 断言，不跑宿主；emit 验证 | `Compilers\VisualBasicSemanticTest\`（`SourceCodeKind.Regular`） |

> 强度原则：**同一输入在「脚本（Script）/ 普通（Regular）」两种模式下分别断言**；**同一特性既要验证可用面（`.P`/`a+b`/`T.Zero` 绑定成功）也要验证受限面（无 Imports 报错、`Object` 接收者不早绑定、同名歧义、运行时不受支持）**。

> **可构建性注记**：本仓库 `Compilers\CSharp\Test\` 的 C# 参考测试**不可构建**（C# 测试项目不在 `VBInteractive.sln`），四文件只作**测试强度基准**。**关键实证（正向）**：本仓库内 C# 编译器**完整支持 C# 14 扩展成员**——`Compilers\CSharp\Portable\Parser\LanguageParser.cs:1776-1823` 解析 `extension(...)` 块（`ExtensionBlockDeclaration`）、`SourceNamedTypeSymbol_Extension.cs` / `ExtensionGroupingInfo.cs` 源符号支持、`PEModuleBuilder.cs:1677/1926` 合成 `ExtensionMarkerAttribute`、`PEModule.cs:1061` 已暴露共享读取 API `HasExtensionMarkerAttribute`。故测试装置可用**仓库内 C# 编译器在内存自建 C# 14 扩展成员/SAIM 测试库**（`CreateCSharpCompilation` + `EmitToImageReference()`，零外部依赖），对齐 `StaticAbstractMembersInInterfacesTests.vb:17-27` 的既有装置。

## 2. VB 特性综合维度

| 维度 | VB 特性 | 关键用例来源 |
|---|---|---|
| V1 扩展属性 `.P` | 早绑定命中归约扩展属性（`ReducedExtensionPropertySymbol` 复用）；setter/链式/`With` 对称 | design-detailed 改动点 3/4 |
| V2 扩展运算符 `a+b` | `CollectUserDefinedOperators` 追加扩展运算符候选；Strict On/Off 均解析 | design-detailed 改动点 4 |
| V3 `T.Zero`/`T.Add` 泛型算法 | 约束接口绑定 + `constrained.` CodeGen；`static abstract` 仅经类型参数 | design-detailed 改动点 5/6 |
| V4 Strict On/Off 分叉 | 扩展成员仅**早绑定**查找参与；Strict Off 晚绑定通道保留 | overview §3 行为对照表 |
| V5 `Object` 接收者晚绑定 | `ShouldLookupExtensionMethods`（`Binder_Lookup.vb:1174-1179`）`Not container.IsObjectType()` 门 | overview §3 / §5 边界 |
| V6 `Imports` 作用域 | 四处 binder 收集（`ImportedTypesAndNamespacesMembersBinder.vb:130` 等）需 `Imports` 才参与 | overview §3 |
| V7 同名优先级 | 实例成员优先 → 扩展成员回退（`Binder_Lookup.vb:1162-1166`）；组内扩展成员须唯一（`Binder_Invocation.vb:610-614`） | design-detailed §0 原则 4 |
| V8 `static abstract` vs `static virtual` | `static abstract` 只能经类型参数 `T.M`；`static virtual`（DIM 静态版）可经接口名呼 | overview §3 |
| V9 `ExtensionMarkerNameAttribute` 术语 | 元数据实际属性名 `ExtensionMarkerAttribute`（`PEModule.cs:1061`、`AttributeDescription.cs:501`）；C# spec 名 `ExtensionMarkerNameAttribute`（`extensions.md:612-620`） | design-detailed §11 核对项 1 |
| V10 运行时不受支持门控 | 复用 `SupportsRuntimeCapability(VirtualStaticsInInterfaces)`（`AssemblySymbol.vb:348-349`）；诊断码族实现期定案 | design-detailed 改动点 8 |
| V11 `.vbx` 与 Regular 同 kind | 同一编译器同一路径（`SourceCodeKind.Script`），语义一致 | overview §4 原则 6 |
| V12 声明侧不在范围 | VB 写 `Shared` 接口成员 / `<Extension>` 扩展属性维持现状报错 | overview §3（BC30270/BC30273） |

## 3. L1 语义层测试（`Compilers\VisualBasicSemanticTest\`）

> **装置**：`CreateCompilation(source, references:={csRef}, targetFramework:=TargetFramework.NetLatest)`（Regular）/ `CreateSubmission(code, references:=...)`（Script）+ `AssertTheseDiagnostics` / `VerifyDiagnostics`。C# 测试库**在内存自建**：`CreateCSharpCompilation(csSource, parseOptions:=CSharpParseOptions.Default.WithLanguageVersion(CSharp.LanguageVersion.Preview), referencedAssemblies:=TargetFrameworkUtil.GetReferences(TargetFramework.NetLatest)).EmitToImageReference()`（先例 `StaticAbstractMembersInInterfacesTests.vb:17-27/:106`、`RefFieldTests.vb:26-28`）。**纯编译 API，无副作用。**
> **C# 库基座（ExternLib 风格，测试内嵌）**：
> ```csharp
> namespace ExternLib
> {
>     public static class ClassicExt { public static int Twice(this int value) => value * 2; }
>     public static class NewExt
>     {
>         extension(string s)
>         {
>             public int CharCount => s.Length;
>             public string Shout() => s.ToUpperInvariant();
>         }
>     }
>     public interface IHasZero<T> where T : IHasZero<T>
>     {
>         static abstract T Zero { get; }
>         static abstract T Add(T a, T b);
>     }
>     public struct MyNum : IHasZero<MyNum>
>     {
>         public int Value; public MyNum(int v) { Value = v; }
>         public static MyNum Zero => new MyNum(0);
>         public static MyNum Add(MyNum a, MyNum b) => new MyNum(a.Value + b.Value);
>     }
>     public static class Mathy
>     {
>         public static T Sum<T>(T[] items) where T : IHasZero<T>
>         { T result = T.Zero; foreach (var t in items) result = T.Add(result, t); return result; }
>     }
> }
> public static class VecExt
> {
>     extension(MyVec v)
>     {
>         public static MyVec operator +(MyVec a, MyVec b) => new MyVec(a.X + b.X, a.Y + b.Y);
>     }
> }
> public struct MyVec { public int X; public int Y; public MyVec(int x, int y) { X = x; Y = y; } }
> ```
> 各用例如需聚焦变体（同名、setter、泛型接收者、DIM 静态、多接口约束、旧运行时引用），在该行注明 C# 变体。

### 3.1 Part A 扩展成员消费（S 系列，建议 `ExtensionMemberConsumptionTests.vb`）

| # | 用例 | 提交/源码（VB） | 断言 |
|---|---|---|---|
| S1 | 扩展属性 getter 基本消费（早绑定） | `Imports ExternLib` + `Module M : Sub Main() : Dim c As Integer = "hello".CharCount : End Sub : End Module`（引用基座库） | **无诊断**；语义模型 `GetSymbolInfo(...).Symbol` 为 `Property CharCount As System.Int32`（**实现期核对**显示字符串与归约形态：`ReducedExtensionPropertySymbol`，`IsShared`=False、`Parameters` 空） |
| S2 | 扩展属性 setter 对称 | C# 变体 `extension(string s) { public int P { get; set; } }`；VB `Dim x As New C : x.P = 5` | **无诊断**（`set_P(obj, v)` 对称消费；**实现期核对** setter 走属性赋值路径是否复用同一归约，design-detailed §5.2(a)）。**F16 回填**：C# 自动实现属性（`public int P { get; set; }`）在扩展块内被本仓 C# 编译器拒（CS9282，合成后备字段非允许的扩展块成员），改用显式 getter/setter + 静态字典后备实现；VB 可写 setter 端到端编译 0 诊断（S2 实测 PASS）。 |
| S3 | 扩展属性链式 | C# 变体两个扩展属性 `A`/`B`；VB `x.A.B` | **无诊断**（链式逐段早绑定；**实现期核对**中间结果类型传播） |
| S4 | 扩展属性 `With` 块 | `With "abc" : Dim c As Integer = .CharCount : End With` | **无诊断**（`With` 内扩展属性消费；**实现期核对** `With` 路径，design-detailed §11 核对项 6） |
| S5 | 扩展运算符二元 `+`（Strict On） | `Option Strict On` + `Imports ExternLib` + `Dim s As MyVec = New MyVec(1,2) + New MyVec(10,20)` | **无诊断**（早绑定命中归约扩展运算符；对照现状 Strict On → BC30452） |
| S6 | 扩展运算符 Strict Off 分叉 | `Option Strict Off`（Strict Off 默认）+ `a + b` | **无诊断**（早绑定命中，**消除实证里 Strict Off 静默晚绑定运行期 InvalidCastException**；Strict On/Off 均编译期解析） |
| S7 | 扩展运算符 `Object` 接收者晚绑定对照 | `Option Strict Off` + `Dim a As Object = New MyVec(1,2) : Dim b As Object = New MyVec(10,20) : Dim s = a + b` | 不早绑定（`ShouldLookupExtensionMethods` `Not container.IsObjectType()` 门）；Strict Off 下维持晚绑定路径（**实现期核对**：编译期无诊断，运行期行为如现状，或报晚绑定不适用诊断）；对照 Strict On 同源码报晚绑定禁用诊断（**实现期核对**实际码，BC42016/BC30452 族） |
| S8 | 同名优先级——实例成员优先 | 接收者类含实例属性 `CharCount`，作用域另有扩展属性 `CharCount`；VB `x.CharCount` | **无诊断**；语义模型 Symbol 为**实例成员**（扩展成员回退，`Binder_Lookup.vb:1162-1166` gate；对照扩展方法现状 S 系列先例） |
| S9 | 同名优先级——组内扩展成员唯一 | 同一接收者两个扩展属性同名（C# 基座变体，如 `extension(string s)` 内两处 `CharCount` 或两扩展类同名） | 报歧义诊断（组内扩展成员须唯一，`Binder_Invocation.vb:610-614`；**实现期核对**实际诊断码）。**F16 回填**：两扩展类同名属性在作用域内**不报歧义**——`MergeExtensionPropertiesIfNecessary` 经 `LookupResult.MergePrioritized`（`LookupResult.vb:444-448`，仅 `other.Kind > Me.Kind` 时替换）合并，首个 viable 胜出静默绑定（S9 实测无诊断、Symbol 存在）；与「组内须唯一」预期不符，接受现状并记录。 |
| S10 | `Object` 接收者扩展成员不可见（晚绑定保留） | `Dim o As Object = "abc" : Dim c = o.CharCount` | Strict On → 报晚绑定禁用/成员不存在诊断（**实现期核对**实际码，BC42016/BC30456 族）；Strict Off → 维持晚绑定（编译期无早绑定命中，运行期失败如现状；**实现期核对**编译期是否有诊断） |
| S11 | `Imports` 作用域——无 Imports | 引用基座库但**不** `Imports ExternLib`，`"hello".CharCount` | **BC30456**（成员未找到；`Imports` 语义一致） |
| S12 | `Imports` 作用域——有 Imports | 同 S11 加 `Imports ExternLib` | **无诊断**（对照 S11 翻转） |
| S13 | 泛型扩展属性归约 | C# 变体 `extension(T r) { public int Count<T>(this T r) ... }` 或 `public static int GCount<T>(this T r)`；VB 用具体实例类型消费 | **无诊断**（`TypeArgumentInference` 从接收者实参推断 + 约束检查；**实现期核对** `ReducedExtensionPropertySymbol` 泛型归约路径，design-detailed §4b） |
| S14 | 经典扩展方法不回归 | `5.Twice()`（基座 `ClassicExt`） | **无诊断**（C#3 `[Extension]` 顶层方法，现有 `ReducedExtensionMethodSymbol` 路径不动） |
| S15 | C#14 扩展方法不回归 | `"hi".Shout()`（基座 `NewExt` 内扩展方法，发射为 `[Extension]` 顶层方法） | **无诊断**（实证已消费，`5.Twice()`→10、`"hi".Shout()`→HI；现状行为保持） |

### 3.2 Part B 接口共享成员消费（S 系列续，建议 `InterfaceSharedMemberConsumptionTests.vb`）

| # | 用例 | 提交/源码（VB） | 断言 |
|---|---|---|---|
| S16 | `T.Zero` 泛型算法（Part B 主目标） | `Function VBSum(Of T As IHasZero(Of T))(items() As T) As T : Dim result As T = T.Zero : For Each item In items : result = T.Add(result, item) : Next : Return result : End Function` + `VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value` | **无诊断**（F10/F11 已实证：绑定到约束接口共享成员；CodeGen `constrained.` + **`call`**——IL 形态 F10 实证纠正、非 callvirt；对齐实证目标 30） |
| S17 | `T.Add` 二元方法经类型参数 | 同 S16 的 `T.Add(result, item)` | **无诊断**（静态抽象二元方法经类型参数调用） |
| S18 | `T.Member` 无约束 → 维持 BC32098 | `Class C(Of T) : Sub F() : Dim x = T.goo : End Sub : End Class` | **BC32098**（无接口约束集 → 无候选 → 维持现状错误；`Binder_Expressions.vb:2913` 改判后回退路径） |
| S19 | `T.Member` 类约束（非接口）→ 维持 BC32098 | `Class C(Of T As DataHolder) : Sub F() : T.Var1 = 4 : End Sub : End Class`（`DataHolder` 为类） | **BC32098**（类约束不在接口约束集；对照既有 `BindingErrorTests.vb:15808`） |
| S20 | `T.Member` effective interface set | C# 变体 `interface I2 { static abstract int M(); }` + `interface I1 : I2 { }`；VB `Sub F(Of T As I1)() : Dim x = T.M() : End Sub` | **无诊断**（接口约束 + 其 `AllInterfaces` 的 effective interface set 内查找；design-detailed §6.1） |
| S21 | `static abstract` 仅经类型参数（接口名直呼被拒） | `I1.M01()`（接口名直呼 `static abstract`） | **维持 BC37314**（`A shared abstract or virtual interface member cannot be accessed`，设计边界不变；对照 `StaticAbstractMembersInInterfacesTests.vb:275` 现状） |
| S22 | `static virtual`（DIM 静态）经接口名 | C# 变体 `interface I1 { static virtual int Foo() => 42; }`；VB `I1.Foo()` | **BC37314**（F11 实证回填：VB 与 fork 内 C# **对齐拒绝**经接口名直呼 static abstract/virtual——C# 亦 CS8926（`Binder_Expressions.cs:10011-10024`），spec `static-abstracts-in-interfaces.md:264-266` static virtual 只能在类型参数上调用；原「无诊断」假设不成立，接受为边界） |
| S23 | 运行时不受支持门控 | 引用集改 `TargetFramework.Mscorlib40` 之类**无 `VirtualStaticsInInterfaces`** 的运行时 + C# SAIM 库（C# 库对旧框架编译可行性见 §8 注）；VB `T.Zero` | 报运行时不受支持诊断（**实现期核对项 2**：诊断码族定案——复用 BC32098 变体或新增 BC 族错误码；对齐 C# `ERR_RuntimeDoesNotSupportStaticAbstractMembersInInterfaces`；复用 `SupportsRuntimeCapability(VirtualStaticsInInterfaces)` 钩子） |
| S24 | `nameof(T.M)` | `Class C(Of T As IHasZero(Of T)) : Shared Sub S() : Dim s = NameOf(T.Zero) : End Sub : End Class` | **无诊断**（Part B 后；对照既有 `NameOfTests.vb:2010` 现状 BC32098） |
| S25 | `AddressOf T.M` | `Class C(Of T As IHasZero(Of T)) : Shared _d As Func(Of T, T, T) = AddressOf T.Add : End Class` | **BC37314**（F11 实证回填：delegate 创建路径 `Binder_Delegates.vb:321` 未传 `receiverIsTypeParameter`，F10 未覆盖；可接受边界或后续 F 码补 delegate 形态） |
| S26 | `T` 作运算符操作数（接口约束共享运算符） | C# 变体 `interface IV(Of T) where T : IV(Of T) { static abstract T operator +(T, T); }`；VB `Sub F(Of T As IV(Of T))(a As T, b As T) : Dim s = a + b : End Sub` | **无诊断**（`GetTypeToLookForOperatorsIn` 补接口约束共享运算符，design-detailed 改动点 7；**实现期核对**） |
| S27 | 同名冲突——多接口约束同名共享成员 | C# 变体 `I1 { static abstract T Zero(); }` / `I2 { static abstract T Zero(); }`；VB `Sub F(Of T As {I1, I2})()` | 多个接口约束同名共享成员合并为方法组由重载决议（**实现期核对**：绑定成功走重载决议 vs 歧义诊断；design-detailed §6.2「同名冲突」段） |
| S28 | VB 声明 `Shared` 接口成员维持报错（范围只消费） | `Interface I(Of T) : Shared ReadOnly Property Zero As T : End Interface` | **BC30270**（`'Shared' is not valid on an interface member declaration`；声明侧不在范围，现状保持；对照 `StaticAbstractMembersInInterfacesTests.vb:30-52`） |
| S29 | `ExtensionMarkerNameAttribute` 术语识别（实现期回填） | 引用基座库后 `comp.GetMember(Of ...)`/语义模型取 `"hello".CharCount` 的符号，检查其归约来源 | 归约符号能溯源到扩展分组类型内带 `[ExtensionMarker]` 属性的成员（**实现期核对项 1**：元数据实际属性名 `ExtensionMarkerAttribute`，`PEModule.cs:1061`；规范名 `ExtensionMarkerNameAttribute` 仅语义基准，实施以实测为准） |

## 4. L2 REPL 层测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

> **装置**：`CreateRunner(input:=...)` / `CreateRunner(args:={"main.vbx"}, workingDirectory:=directory)` + `TestConsoleIO`（`:66-91`，内存 `StringReader`/`StringWriter`）。`.vbx` 文件经既有 `CreateIsolatedTempDirectory`（`:41-45`）+ `File.WriteAllText` 装置写临时目录。C# 测试库**在测试内 emit 到同一隔离临时目录**（`CreateCSharpCompilation(...).Emit(Path.Combine(directory, "ExternLib.dll"))`，参照 `:47-64` `CreateLibraryAssembly` 先例；脚本经 `#R "<temp>\ExternLib.dll"` 或 `/R:` 参数引用）。`Console.WriteLine` 不被 `TestConsoleIO` 捕获，输出断言用 `Print`（对齐 byref-like R9 先例）。**纯内存 + 既有临时目录装置，无副作用。**
> **注（实现期核对）**：REPL 宿主与测试项目均 net10.0，SAIM 运行期可用；`#R`/`/R:` 引用仓库内 C# 编译器 emit 的库在宿主解析的机制（类型同一性）待实现期首跑确认，若不可行回填（见 §8「测不了就问用户」）。

| # | 用例 | 输入/`.vbx` | 断言 |
|---|---|---|---|
| R1 | `.vbx` 扩展属性消费 | `main.vbx`：`#R "<temp>\ExternLib.dll"` + `Imports ExternLib` + `Print("hello".CharCount)` | 退出码 0、输出含 `5` |
| R2 | `.vbx` 扩展运算符消费 | `main.vbx`：`#R` + `Imports ExternLib` + `Dim a As MyVec = New MyVec(1,2) : Dim b As MyVec = New MyVec(10,20) : Print((a + b).ToString())` | 退出码 0、输出含 `(11,22)`。**F16 回填**：交互宿主默认 `Option Infer Off`，`Dim a = New MyVec(...)` 会把 `a` 推断为 `Object`，`a + b` 走晚绑定 `AddObject` 运行期 `InvalidCastException`——必须显式 `As MyVec`（早绑定命中）。`MyVec` 需覆写 `ToString()`（`$"({X},{Y})"`）才能输出 `(11,22)`（F15 cs-lib 同款）。 |
| R3 | `.vbx` `T.Zero` 泛型算法 | `main.vbx`：`#R` + `Imports ExternLib` + `Function VBSum(Of T As IHasZero(Of T))(...) ...` + `Print(VBSum(Of MyNum)({New MyNum(10), New MyNum(20)}).Value)` | 退出码 0、输出含 `30` |
| R4 | `.vbx` 与 Regular 同 kind——编译错误一致 | `main.vbx`：`#R` + **无** `Imports ExternLib` + `Print("hello".CharCount)` | `Assert.Equal(1, runner.RunInteractive())`；错误流含 **BC30456**（与 L1 S11/Regular 同码，`.vbx` 与 `.vb` 同一编译器） |
| R5 | 交互提交扩展属性 | `CreateRunner(input:="#R ""<temp>\ExternLib.dll""" & vbCrLf & "? "hello".CharCount")` | 输出含 `5` 值行（参照 `TestPrint` 的 `?` 精确格式） |
| R6 | 交互提交 `T.Zero` | `CreateRunner(input:="#R ..." & vbCrLf & "Function F(Of T As IHasZero(Of T))(x As T) As T : Return T.Zero : End Function" & vbCrLf & "? F(Of MyNum)(New MyNum(0)).Value")` | 输出含 `0` 值行，无 `«Red»` 错误块 |
| R7 | Strict On 分叉——扩展运算符 | `main.vbx`：`Option Strict On` + `#R` + `Imports ExternLib` + `Print((New MyVec(1,2) + New MyVec(10,20)).ToString())` | 退出码 0、无 `«Red»` 错误块（早绑定命中，无 BC30452；对照实证 Strict On 现状 BC30452） |
| R8 | Strict Off `Object` 接收者晚绑定对照 | `main.vbx`：`Option Strict Off` + `#R` + `Imports ExternLib` + `Dim o As Object = New MyVec(1,2) : Print((o + New MyVec(10,20)).ToString())` | **实现期核对**：不早绑定（`Object` 接收者门），Strict Off 晚绑定通道保留——可能无编译诊断 + 运行期失败，或报晚绑定诊断；如实记录首错/行为（对照 L1 S7/S10） |
| R9 | `Imports` 作用域（交互） | `CreateRunner(input:="#R ..." & vbCrLf & "? "hello".CharCount")`（**无** `Imports ExternLib`） | `«Red»` 错误块含 **BC30456**；再提交 `Imports ExternLib` 后 `? "hello".CharCount` → 输出 `5`（作用域一致） |
| R10 | 跨提交状态保持 | 提交 1 `#R "<temp>\ExternLib.dll"` + `Imports ExternLib`（成功）→ 提交 2 `? "hello".CharCount` → 提交 3 `? 1 + 2` | 提交 2 输出 `5`；提交 3 输出 `3`（前提交状态不污染会话；参照 byref-like R13） |

## 5. L3 显示/符号层测试（`Compilers\VisualBasicSymbolTest\SymbolsTests\`）

> **装置**：`CreateCompilation(..., references:={csRef}, targetFramework:=TargetFramework.NetLatest)` + `comp.GetMember(Of ...)`（`BasicTestBase` 符号断言，先例 `StaticAbstractMembersInInterfacesTests.vb:126`）+ `SymbolDisplay.ToDisplayString`（`ByRefLikeDisplayTests.vb` 先例）。**纯字符串/符号，无副作用。**

| # | 用例 | 符号/源码 | 断言 |
|---|---|---|---|
| D1 | `PEMethodSymbol.IsExtensionMember` / `PEPropertySymbol.IsExtensionMember` | 从 C# 库分组类型 `NewExt` 内取 `CharCount` 属性符号、`Shout` 方法符号 | 标记 `[ExtensionMarker]` 的属性/方法 `IsExtensionMember` 为 **True**；普通成员（如 `MyNum.Zero` 顶层实现）为 **False**（**实现期核对**：符号属性命名与公开形态，design-detailed §2.2(b)） |
| D2 | `HasExtensionMarkerAttribute` 原始读取 | 遍历 C# 库分组类型成员句柄，调 `PEModule.HasExtensionMarkerAttribute(handle, markerName)` | 对扩展成员返回 **True** 且 `markerName` 指向所属标记类型（`<M>$` 的 MetadataName）；对非扩展成员返回 False（**实现期核对** `markerName` 精确值） |
| D3 | `ReducedExtensionPropertySymbol` 归约正确性 | `comp.GetMember(Of ...)` 取归约后扩展属性符号（S1 同源） | `ReceiverType` = 接收者类型、`Parameters` 空、`IsShared` = **False**、`GetMethod`/`SetMethod` 走 `ReduceAccessorIfAny`（对照 XML `InternalXmlHelper.Value` 先例，`XmlLiteralSemanticModelTests.vb:420`） |
| D4 | 扩展属性符号显示（属性 glyph / 扩展来源标注） | `SymbolDisplay.ToDisplayString` 归约扩展属性符号 | **实现期核对**显示字符串：预期 `Property CharCount As System.Int32`；是否带「扩展来源」标注（如扩展类名/命名空间）留实现期实测回填（design-detailed §11 核对项 4：合成 vs 包装底层 `[ExtensionMarker]` 成员）。**F16 回填**：`MinimallyQualifiedFormat` 实际为 `Property <G>$34505F560D9EACF86A87F3ED1F85E448.CharCount As Integer`——归约属性 `ContainingType` 是 C#14 分组类型（mangled `<G>$<hash>` 名），类型用 VB 关键字别名 `Integer`（非 `System.Int32`）；含含类型名而非扩展来源类名。 |
| D5 | 扩展运算符符号显示 | C# 库 `VecExt` 分组类型内 `op_Addition` 运算符符号 | 显示为 `Operator +(...)` 形态（**实现期核对**显示字符串与 `MethodKind.UserDefinedOperator` 归约后形态）。**F16 回填**：归约运算符（`ReducedExtensionOperatorSymbol`，`MethodKind=UserDefinedOperator`、保留两操作数）`MinimallyQualifiedFormat` 实际为 `Operator VecExt.+(a As MyVec, b As MyVec) As MyVec`——含含类型为 `[Extension]` 容器 `VecExt`（非分组类型）+ 参数名，非计划假设的 `Operator +(MyVec, MyVec)`。 |
| D6 | 普通共享成员不误标 | `MyNum.Zero` 顶层 `[Extension]` 无、`MyNum.Add` 普通静态 | `IsExtensionMember` 为 **False**、`HasExtensionMarkerAttribute` 为 False（对照 D1/D2 反例） |

## 6. L4 Regular 模式（`Compilers\VisualBasicSemanticTest\`，`SourceCodeKind.Regular`）

> **装置**：`VisualBasicCompilation.Create` + `TestOptions.Regular`（`.vb` 项目，`CreateCompilation(source, references:=..., targetFramework:=TargetFramework.NetLatest)`），不进宿主、不跑脚本。**纯编译 API，无副作用。**

| # | 用例 | 源码（`.vb` 项目） | 断言 |
|---|---|---|---|
| G1 | `.vb` 扩展属性消费 | `Module M : Sub Main() : Dim c As Integer = "hello".CharCount : End Sub : End Module`（`Imports ExternLib` + 引用库） | **无诊断**（与 `.vbx` R1 同语义） |
| G2 | `.vb` 扩展运算符消费 | `Module M : Sub Main() : Dim s As MyVec = New MyVec(1,2) + New MyVec(10,20) : End Sub : End Module` | **无诊断**（与 R2 同语义） |
| G3 | `.vb` `T.Zero` 泛型算法 + emit | `Module M : Function VBSum(Of T As IHasZero(Of T))(items() As T) As T : ... T.Zero ... T.Add ... : Sub Main() : Console.WriteLine(VBSum(Of MyNum)({...}).Value) : End Sub : End Module` | `VerifyDiagnostics` 无错误；`CompileAndVerify`（内存 `MemoryStream`，`CompilerTestHarness.vb:215-223`）成功；**实现期核对** emit IL 含 `constrained.` + callvirt（可选 IL 断言，对齐 `StaticAbstractMembersInInterfacesTests` 发射强度） |
| G4 | `.vb` 运行时不受支持门控 | 引用集为无 `VirtualStaticsInInterfaces` 的运行时 + C# SAIM 库；`T.Zero` | 报运行时不受支持诊断（**实现期核对项 2**：诊断码族；C# 库对旧框架引用编译可行性见 §8 注） |
| G5 | `.vb` `Object` 接收者晚绑定对照 | `Module M : Sub Main() : Dim o As Object = "abc" : Dim c = o.CharCount : End Sub : End Module` | Strict On → 晚绑定禁用/成员不存在诊断（**实现期核对**实际码）；Strict Off → 晚绑定路径（对照 L1 S10） |
| G6 | `.vb` `Imports` 作用域 | 无 `Imports ExternLib` 同 G1 | **BC30456**（与 L1 S11/R4 同码） |
| G7 | `.vb` 同名优先级 | 接收者类含实例属性 `CharCount` + 扩展属性 `CharCount` | **无诊断**，语义模型 Symbol 为实例成员（与 L1 S8 一致） |
| G8 | `.vb` 声明侧维持报错（范围只消费） | `Interface I(Of T) : Shared ReadOnly Property Zero As T : End Interface` | **BC30270**（声明侧不在范围，现状保持；与 L1 S28 一致） |

## 7. 既有测试更新清单

| 文件:行号 | 现状 | 更新 | 原因 |
|---|---|---|---|
| `Compilers\VisualBasicSymbolTest\SymbolsTests\StaticAbstractMembersInInterfacesTests.vb`（**核心，多处翻/调**） | `ConsumeAbstractStaticMethod_01/02`（:228/:311）、`ConsumeAbstractStaticMethod_AddressOf_01`（:380）、`_AddressOf_DirectCastToDelegate_01`（:465）、`_AddressOf_TryCastToDelegate_01`（:550）、`_AddressOf_CTypeToDelegate_01`（:635）、`_AddressOf_DelegateCreation_01`（:720）、`ConsumeAbstractStaticPropertyGet/Set/Compound/02`（:1004/:1085/:1167/:1255）、`ConsumeAbstractStaticIndexedProperty_03/04`（:1318/:1575）、`ConsumeAbstractStaticEventAdd/Remove/02`（:1892/:1973/:2054）、`ConsumeAbstractUnary/BinaryOperator_01`（:2304/:2365）、`ConsumeAbstractConversionOperator_01`（:2515）——**`T.M01()`/`T.M04()`/属性/事件/运算符/转换 均断言 BC32098** | **翻转/调整**：`T.` 后跟 `static abstract`/`static virtual` 成员（M01、属性、运算符等）改断言绑定成功（对齐 L1 S16/S17/S26）；**仍需报错者**维持报错但码可能变（`T.M00()` 不存在、`T.M03()` 实例 DIM、`T.M05()` protected——**实现期核对**各码）；`I1.M01()` 接口名直呼 static abstract 维持 BC37314（S21） | **这是 Part B 主目标**：`Binder_Expressions.vb:2913` 改判后 `T.Member` 绑定约束接口共享成员；不更新则实现后此文件挂红。定义侧（`DefineAbstractStaticMethod_01` :30 / `ImplementAbstractStaticMethod_*` / `DefineVirtualStatic*`）**不修改**（声明/实现侧不在范围） |
| `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:15808`（`BC32098ERR_TypeParamQualifierDisallowed`） | `Class C1(Of T As DataHolder)`（**类约束**）`T.Var1 = 4` → BC32098 | **保留语义，复核**：类约束不在接口约束集 → 仍 BC32098；若诊断消息/位置因改判路径调整则更新断言文本 | `T.Member` 改判后「无接口约束候选 → 回退 BC32098」路径（L1 S19） |
| `Compilers\VisualBasicSemanticTest\Binding\Binder_Expressions_Tests.vb:481`（`TypeParamCantQualify`） | `Class C(Of T)`（**无约束**）`x = T.goo` → BC32098 | **保留**：无约束 → 无接口约束集 → 仍 BC32098；复核 | 同上回退路径（L1 S18） |
| `Compilers\VisualBasicSemanticTest\Semantics\NameOfTests.vb:2010` | `Class C3(Of T As C2)`（**类约束**）`NameOf(T.M1)` → BC32098 | **保留语义，复核**：类约束不在接口约束集 → 仍 BC32098；Part B 落地后 `nameof(T.M)` 对接口约束共享成员应可用（新用例 S24，此处为类约束对照不翻） | 同上回退路径 |
| `Compilers\VisualBasicSemanticTest\Semantics\XmlLiteralSemanticModelTests.vb:397-489`（`ValueExtensionProperty` / `LookupValueExtensionProperty`） | XML 轴 `x.<y>.Value` 经 `MergeInternalXmlHelperValueIfNecessary` 特例 + `ReducedExtensionPropertySymbol` → `Property InternalXmlHelper.Value As String` | **保留，不修改**；新增扩展属性用例（S1-S13）后回归验证泛化不改特例 | design-detailed §5.2(a)：`MergeInternalXmlHelperValueIfNecessary` 泛化为收全部扩展属性但**保留 InternalXmlHelper.Value 特例** |
| `Compilers\VisualBasicSemanticTest\ExtensionMethods\SemanticModelTests.vb` | 现有 VB `<Extension>` 方法语义模型断言（`MethodKind.ReducedExtension`、`CallsiteReducedFromMethod`、`ReducedFrom.ReduceExtensionMethod`） | **保留，不修改**；新增 C# 库消费用例（S1-S15）回归验证经典/新扩展方法路径不动 | design-detailed §0 原则 2：现有扩展方法路径并列而非改写 |
| `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb` | 现有脚本编译 API 断言 | **新增（可选）**：SAIM 泛型算法提交 `HasSubmissionResult` 断言（对照 byref-like 计划 §7 末条） | 扩展矩阵覆盖 |

> **实现期检查点**：① `StaticAbstractMembersInInterfacesTests.vb` 各 `T.Mxx()` 翻转后实际诊断码/无诊断需逐用例实测回填（尤其 `T.M03()` 实例 DIM、`T.M04()` 普通 static、`T.M05()` protected）；② 既有 BC32098 三处（BindingErrorTests/Binder_Expressions_Tests/NameOfTests）改判后路径是否仍稳定报 BC32098，按实测回填。

> **F11 实证回填（2026-08-22，实施+验证双确认）**：
> - **翻转范围**：仅 `isVirtual:False`（static abstract）Consume 用例中「方法调用 / 属性·索引 getter / `nameof`」翻转（BC32098→无诊断）；**事件、`AddressOf` 委托、setter/compound 未翻转**——事件走独立 `BindEventAccess` 路径（`Binder_Statements.vb:2392`）仍 BC32098；`AddressOf`（delegate 创建，`Binder_Delegates.vb:321`）与 setter（`Binder_Statements.vb:1970`）因未传 `receiverIsTypeParameter` 报 BC37314。
> - **实测码**：`T.M00()`（不存在）/`T.M03()`（实例 DIM）/`T.M04()`（plain static）→ BC32098 维持；`T.M05()`（protected）→ 方法 BC30390 / 属性 BC30389；接口名直呼 static abstract/virtual → BC37314（与 fork C# CS8926 对齐）。
> - **G3 emit**：ILVerify 拒收 `constrained.+call`（已知限制，C# SAIM 测试 30+ 处同款 `verify:=Verification.Skipped`），G3 对齐 C# 惯例。
> - **定义侧**（Define*/Implement*/DefineVirtualStatic*）不修改，已核实。

## 8. 无副作用纪律

- **L1/L4**：`CreateCompilation` / `CreateSubmission` + `AssertTheseDiagnostics` / `VerifyDiagnostics`，不跑宿主；emit 验证走 `CompileAndVerify` 内存 `MemoryStream` 门控（`CompilerTestHarness.vb:215-223`，不加载动态程序集、不写磁盘）。C# 测试库经 `CreateCSharpCompilation(...).EmitToImageReference()` **内存自建**（仓库内 C# 编译器支持 C# 14 扩展语法与 SAIM，源码实证见 §1）。
- **L2**：`CreateRunner` + `TestConsoleIO` 内存 `StringReader`/`StringWriter`；`.vbx` 文件与 C# 测试库 emit 仅走既有 `CreateIsolatedTempDirectory` 装置（系统临时目录自清理，先例 `CommandLineRunnerTests.vb:41-64/1049-1078`）。无网络、无进程、无注册表。
- **L3**：`GetMember(Of ...)` 符号断言 + `SymbolDisplay.ToDisplayString` 纯字符串。
- **引用装置**：C# 库引用统一走 `CreateCSharpCompilation`（`TargetFramework.NetLatest` 引用集）→ `EmitToImageReference()`（L1/L3/L4）或 `Emit(path)` 到隔离临时目录（L2）。**不依赖 `tmp/exp-consume/ExternLib.dll` 外部产物**（该产物仅作实证基线参考；若仓库内 C# 编译器 emit 的元数据形态与 .NET 10 SDK 发射有差异，按实测回填并对照 `tmp/exp-consume/dump/`）。
- **运行时不受支持门控的旧框架引用**：S23/G4 需要 C# SAIM 库对无 `VirtualStaticsInInterfaces` 的旧框架引用集编译——`static abstract` 接口成员本身可能要求较新框架类型，**实现期核对** C# 侧能否对旧引用编译；若不能，改用「手工移除 `RuntimeFeature.VirtualStaticsInInterfaces` 的引用集」或直接构造 `AssemblySymbol` 断言 `RuntimeSupportsVirtualStaticsInInterfaces` 返回 False 的单元测试路径，向用户说明取舍。
- **新增用例若写不了**：按项目约定，不写就测不了的部分在实现期向用户说明并询问（不静默跳过）——已知待确认项：L2 `#R`/`/R:` 引用仓库内 emit 库的宿主解析机制、L2 R8 晚绑定运行期行为、S23/G4 旧框架引用装置、泛型扩展属性/运算符归约（S13/S26）实际诊断。

## 9. 与 design-detailed §12 的关系

本计划是 design-detailed §12（设计级矩阵）的**分层扩展**：

| design-detailed §12 | 本计划扩展 |
|---|---|
| L1 语义层 | 拆为 §3.1 S1-S15（Part A 扩展成员：getter/setter/链式/`With`/Strict 分叉/`Object` 晚绑定/`Imports`/同名优先级/泛型归约/经典与新扩展方法回归）+ §3.2 S16-S29（Part B SAIM：`T.Zero`/`T.Add` 泛型算法、回退 BC32098、effective interface set、`static abstract` vs `static virtual`、运行时门控、`nameof`/`AddressOf`、运算符操作数、同名冲突、声明侧维持、术语识别） |
| L2 REPL 层 | 拆为 §4 R1-R10（`.vbx` 扩展属性/运算符/`T.Zero` 端到端、`.vbx` 与 Regular 同 kind 错误一致、交互提交、Strict On/Off 分叉、`Object` 晚绑定对照、`Imports` 作用域、跨提交状态保持） |
| L3 符号/元数据层 | 拆为 §5 D1-D6（`IsExtensionMember`、`HasExtensionMarkerAttribute` 原始读取、`ReducedExtensionPropertySymbol` 归约、扩展属性/运算符符号显示、普通成员不误标） |
| L4 Regular 模式 | 拆为 §6 G1-G8（`.vb` 同能力消费、emit 验证含 `constrained.`、运行时门控、`Object` 晚绑定、`Imports`、同名优先级、声明侧维持） |
| —（新增） | §7 既有测试更新清单（**核心：`StaticAbstractMembersInInterfacesTests.vb` 消费用例 BC32098 翻转** + 既有 BC32098 三处复核 + XML `Value` 特例回归 + 扩展方法回归）、§8 无副作用纪律（**C# 库内存自建装置**）、§1 可构建性注记（**仓库内 C# 编译器支持 C# 14 扩展语法的实证**） |

> 实施期若发现「预期绑定/诊断」与实测不符（如 `T.M03()` 实例 DIM 实际码、`I1.M04()` 普通 static 经接口名可呼性、`Object` 接收者晚绑定编译期诊断、泛型扩展属性归约、运行时门控旧框架装置），如实记录差异并回填本计划（对齐 byref-like-safety 任务 test-plan §9 末的实现期修订惯例）。
