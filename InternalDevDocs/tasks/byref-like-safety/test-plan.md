# 测试计划：byref-like 类型安全（ref struct 支持，D1 前置-1）

> 状态：测试计划（F3，2026-08-13 评审修订）。依据链：`../../proposals/proposal-byref-like-safety.md`（Active/Proposed）→ `../../meetings/meeting-byref-like-repl-safety.md`（RESOLUTION PROPOSAL A）→ `design-overview.md`（F1）→ `design-detailed.md`（F2，§5/§9）→ 本测试计划。
> 本计划把 design-detailed §5（REPL 三碰撞点零新增）与 §9（设计级矩阵）升级为**分层测试设计**：参考 C# ref struct 语义测试强度（`Compilers\CSharp\Test\` 中 `SpanStackSafetyTests` / `RefEscapingTests` / `AttributeTests_IsByRefLike` / `RefStructInterfacesTests`），并按 **VB 语法特性**（RestrictedType 概念、`.vbx` 与 REPL 同 kind、无 `ref struct` 声明语法、`scoped` 非概念、CType/DirectCast/TryCast 三显式转换、字符串 `&`、For Each 展开、With/ReDim/Nothing、`Of T As` 约束）综合设计。
> **评审修订（2026-08-13）**：对照 C# 四文件真实覆盖矩阵（agent 精读提取，约 1000+ 测试）补 6 类差距——① 既有测试翻转补 `RefFieldTests.vb`（BC30668 翻转，**阻断级**）；② 错误码矩阵补 BC31393/BC32061；③ VB 特有语法维度（CType/DirectCast/TryCast、接口装箱、TypeOf Is、`&` 连接、For Each、集合初始化器、With/ReDim/Nothing、匿名类型、约束、Async 参数、Iterator）；④ Span 引用装置改 `CreateCompilation(targetFramework:=NetLatest)`（§8）；⑤ 注明 C# 参考测试在本仓库不可构建、只作强度基准（§1）；⑥ REPL 跨提交状态与 `?` 打印精确断言（§4）。
> 实现阶段由实施者照本计划补测试，验证者按「无副作用纪律」与「行为对照表」核对。

## 0. 目标与验收

- **目标**：在 C# 测试强度（语义诊断 → REPL 全输出 → 显示断言 → Regular 对照）之上，覆盖 byref-like 安全性的全部边界，保证：
  1. `IsRefLikeType` 判定正确（`Span(Of Integer)` 是 ref-like；普通结构体不是）；
  2. suppress obsolete 生效（`Span(Of Integer)` 无 obsolete 错误、可绑定）；
  3. 错误码矩阵（装箱 / Nullable / 字段 / 数组元素 / 返回值 / ByRef 参数 / 泛型参数 / **泛型约束** / **继承基类实例方法** / LINQ / Lambda / async 状态机）全对；
  4. REPL 三碰撞点（顶层字段 / 结果装箱 / 跨 Await）报正确错误；
  5. 方法体局部 / ByVal 值参数 / `allows ref struct` 接口消费**可用**；
  6. 显示「ByRef Like Structure」；
  7. `.vbx` 与 REPL 语义一致、Regular 零影响（Span 在 `.vb` 里同样受限 = 新语义，属「修错不算回归」D4）；
  8. **VB 特有语法全部覆盖**：CType/DirectCast/TryCast 三显式转换、接口装箱、TypeOf…Is、字符串 `&`、For Each 展开、集合初始化器、With/ReDim/Nothing、匿名类型、`Of T As` 约束、Async 参数、Iterator/Yield。
- **验收**：四层矩阵全绿；既有测试按 §7 更新（**含 `RefFieldTests.vb` BC30668 翻转**）；无副作用纪律满足（§8）。

## 1. 参考的 C# 测试强度（分层）

C# 对同一特性（ref-like 安全 / `[IsByRefLike]`）的测试锚点与强度：

| 层 | C# 测试锚点 | 强度特征 | VB 落点 |
|---|---|---|---|
| L1 语义层 | `CSharp\Test\Semantic\Semantics\SpanStackSafetyTests.cs`（ref struct 栈上安全、字段/装箱/await 限制）、`SemanticErrorTests.cs`、`AttributeTests_IsByRefLike.cs`（`Emit3\Attributes\`） | 精确 `VerifyDiagnostics` 断言错误码与位置；同一类型在合法/非法两种场景对照 | `Compilers\VisualBasicSemanticTest\`（`CreateSubmission` / `VisualBasicCompilation.Create`，`BasicTestBase.vb:437`） |
| L2 REPL/脚本 | C# csi 端到端（`CommandLineTests.cs`）；`RefStructInterfacesTests.cs`（`Emit3\`，`allows ref struct` 接口消费） | 全输出断言（错误块 + 无运行）；`allows ref struct` 接口消费端到端 | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`（`CreateRunner(input:=...)` + `TestConsoleIO`） |
| L3 显示层 | C# `SymbolDisplay` 对 `ref struct` 的显示测试 | 字符串精确断言 | `Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb` |
| L4 常规模式 | C# 普通 `ref struct` 字段/装箱错误在 Regular 项目同样生效 | 编译 API 断言，不跑宿主 | `Compilers\VisualBasicSemanticTest\`（`SourceCodeKind.Regular`） |

> 强度原则：**同一输入在「脚本（Script）/ 普通（Regular）」两种模式下分别断言**（C# 的 ref struct 规则两种模式一致，VB 跟随）；**同一类型既要验证受限面（字段/装箱/ByRef/await）也要验证可用面（方法局部 / ByVal 传参）**。

> **可构建性注记（评审修订 ⑤）**：本仓库 `Compilers\CSharp\Test\` 的 C# 参考测试**不可构建**——它们依赖 `CreateCompilationWithMscorlibAndSpan` 等装置（定义在 Roslyn 上游 `CSharpTestBase.cs:2898`，本仓库 `Compilers\Test\Utilities\CSharp\` 缺失，C# 测试项目亦不在 `VBInteractive.sln`）。因此四文件只作**测试强度基准**（覆盖面、正反对照、错误码），不照抄可运行用例。VB 侧的 Span 引用注入走 §8 的 `CreateCompilation(targetFramework:=NetLatest)` 或 C# emit 引用（`RefFieldTests.vb` 先例）。

## 2. VB 语法特性综合维度

| 维度 | VB 特性 | 关键用例来源 |
|---|---|---|
| V1 RestrictedType 判定细化 | `Span(Of Integer)` 是 ref-like（`IsRefLikeType` 读 `[IsByRefLike]`）；普通结构体不是；三遗留特殊类型仍受限 | design-detailed §2 |
| V2 suppress obsolete | `Span(Of Integer)` 直接可写、可绑定（无 obsolete 错误） | design-detailed §3 |
| V3 错误码矩阵 | BC31393/31394/31396/32061/36598/36640/37052（**评审修订 ②**：31393 继承基类方法、32061 泛型约束各补用例） | design-detailed §4；C# `BaseMethods`/`AllowsConstraint_01..47` |
| V4 REPL 三碰撞点 | 顶层字段（BC31396）/ 结果装箱（BC31394）/ 跨 Await（BC37052） | design-detailed §5 |
| V5 方法体可用面 | 局部 / ByVal 值参数 / **按值返回**（ref struct 按值返回合法，对齐 C# `Span(Of T) Foo()`） | proposal §4b |
| V6 `allows ref struct` 接口消费 | 依赖前置-2（M8）；未就绪则如实标注时序差 | design-detailed §7 |
| V7 显示 | 「ByRef Like Structure」仅显示、不可声明 | design-detailed §6 |
| V8 `.vbx` 与 REPL 同 kind | `.vbx` 顶层同样受限；诊断不区分 REPL/常规 | overview §6 |
| V9 Regular 零影响 | `.vb` 里 Span 作字段/返回/ByRef/装箱报 restricted；普通结构体不变 | design-detailed §8.2 |
| V10 **三显式转换**（评审修订 ③） | `CType(span, Object)` / `DirectCast(span, Object)` / `TryCast(span, IEquatable(Of Integer))` → BC31394；与隐式装箱（S3）对照 | C# 显式装箱 `(object)x` CS0030（IllegalBoxing） |
| V11 **接口装箱/拆箱**（评审修订 ③） | `Dim i As IDisposable = span` → BC31394；`CType(i, Span(Of Integer))` 拆箱 → BC31396/31394；`TypeOf span Is IDisposable` → 恒假/报错 | C# IllegalBoxing/Unboxing/IsOperator |
| V12 **字符串 `&` 连接**（评审修订 ③） | `"a" & span` / `span & span` → 装箱 → BC31394（VB 特有，区别于 C# handler） | C# InterpolatedString 族转义对照 |
| V13 **For Each 展开**（评审修订 ③） | `For Each c As Char In span` → 索引器展开 IL（与 C# `Foreach_Span_01/02` 一致） | C# Foreach_Span |
| V14 **集合初始化器**（评审修订 ③） | `From { New Span(Of Integer)(1) }` 数组字面量 → 数组元素 → BC31396 | C# CollectionExpression |
| V15 **With / ReDim / Nothing**（评审修订 ③） | `With span` 成员访问可用；`ReDim a(9) As Span(Of Integer)` → BC31396；`Dim s As Span(Of Integer) = Nothing` 可用（Nothing=default，不装箱） | C# default/obj-init 对照 |
| V16 **匿名类型 / 泛型约束**（评审修订 ③） | `New With {.S = span}` → 匿名类型成员 → BC31396；`Sub F(Of T As Span(Of Integer))` → BC32061 | C# AnonymousType / AllowsConstraint |
| V17 **Async 参数 / Iterator**（评审修订 ③） | `Async Function F(s As Span(Of Integer))` → 参数转义/状态机 → BC37052/31396；`Iterator Function` Yield span → BC37052 | C# AsyncParams / Iterator_01..06 |

## 3. L1 语义层测试（`Compilers\VisualBasicSemanticTest\`）

> 装置：`VisualBasicCompilation.Create`（Regular）/ `CreateSubmission`（Script，`BasicTestBase.vb:437`）+ `AssertTheseDiagnostics` / `VerifyDiagnostics`。Span 引用注入走 `CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`（§8；**非** `System.Memory`）。**纯编译 API，无副作用。** 测试命名建议仿 C# `SpanStackSafetyTests` 的 `<Features>_<Scenario>` 风格。

| # | 用例 | 提交/源码 | 断言 |
|---|---|---|---|
| S1 | IsRefLikeType 判定 | `Dim s As Span(Of Integer)`（方法内，可编译） | `GetTypeInfo(...).Type.IsRefLikeType` 为 True（语义模型断言）；普通结构体为 False |
| S2 | suppress obsolete | `Dim s As New Span(Of Integer)(1)`（方法内） | **无** obsolete 诊断、可绑定（现状报 obsolete 错误，实现后消失；**对照 `RefFieldTests.vb:41` BC30668**） |
| S3 | 装箱 → 转换 | `Dim o As Object = New Span(Of Integer)(1)` | **BC31394** `ERR_RestrictedConversion1`（`Binder_Conversions.vb:508` 路径） |
| S4 | Nullable | `Dim n? As Span(Of Integer)` | **BC31396**（`Nullable(Of T)` 非法） |
| S5 | 字段 | `Dim f As Span(Of Integer)`（类字段） | **BC31396**（`SourceMemberFieldSymbol.vb:142`） |
| S6 | 数组元素 | `Dim a(9) As Span(Of Integer)` | **BC31396** |
| S7 | 返回值 | `Function F() As Span(Of Integer)` | **BC31396**（`SourceMethodSymbol.vb:2346`） |
| S8 | ByRef 参数 | `Sub F(ByRef s As Span(Of Integer))` | **BC31396**（`Binder_Utils.vb:1096-1104`） |
| S9 | 泛型实参 | `Dim c As New C(Of Span(Of Integer))`（`Class C(Of T)`） | **BC31396** `ERR_RestrictedType1`（`ConstraintsHelper.vb:661`） |
| S10 | LINQ | `Dim q = From s In spans Select s`（Span 参与） | **BC36598** `ERR_CannotLiftRestrictedTypeQuery` |
| S11 | Lambda 闭包 | `Dim f = Function() span`（捕获 Span） | **BC36640** `ERR_CannotLiftRestrictedTypeLambda`（`Binder_Lambda.vb`） |
| S12 | async 状态机 | `Async Function F() ... Dim s As Span(Of Integer) : Await ...` | **BC37052** `ERR_CannotLiftRestrictedTypeResumable1`（`IteratorAndAsyncCaptureWalker.vb:98/:113/:133`） |
| S13 | 方法内局部可用 | `Sub F() : Dim s As New Span(Of Integer)(1) : End Sub` | 无诊断（可用面） |
| S14 | ByVal 值参数可用 | `Sub F(s As Span(Of Integer))` | 无诊断（可用面） |
| S15 | 转换分类器 NoConversion | `Conversions.ClassifyConversion(spanType, objectType)` 直接断言 | `NoConversion`（`Conversions.vb:3393` 守卫，实现期检查点） |
| S16 | 三遗留特殊类型回归 | `Dim t As TypedReference` / `Dim o As Object = New ArgIterator` | 仍报 BC31396 / BC31394（谓词扩展不破坏遗留受限） |
| S17 | **继承基类实例方法**（评审修订 ②，V3 补码） | `span.GetHashCode()` / `span.ToString()` / `span.GetType()`（调继承自 Object/ValueType 的实例方法） | **BC31393** `ERR_RestrictedAccess`（`Errors.vb:936`；C# `BaseMethods` `default(Span).GetHashCode()` → CS0029 对应） |
| S18 | **泛型约束**（评审修订 ②，V3 补码） | `Sub F(Of T As Span(Of Integer))` / `Class C(Of T As {Structure, Span(Of Integer)})` | **BC32061** `ERR_ConstraintIsRestrictedType1`（`Errors.vb:1133`；C# `AllowsConstraint_01..47` 对应） |
| S19 | **CType/DirectCast 显式装箱**（评审修订 ③，V10） | `CType(span, Object)` / `DirectCast(span, Object)` | **BC31394**（显式路径，C# `(object)x` CS0030 对应；对照 S3 隐式路径） |
| S20 | **TryCast**（评审修订 ③，V10） | `TryCast(span, IEquatable(Of Integer))` | **BC31394** 或对应 restricted 诊断（`Binder_Conversions.vb:248-251`；实现期核对实际码） |
| S21 | **接口装箱**（评审修订 ③，V11） | `Dim i As IDisposable = span`（隐式转接口） | **BC31394**（C# `IllegalBoxing` 对应；接口转换本质是装箱） |
| S22 | **接口拆箱**（评审修订 ③，V11） | 先 `Dim i As IDisposable` 再 `CType(i, Span(Of Integer))` | **BC31396**/31394（实现期核对；C# `Unboxing` 对应） |
| S23 | **字符串 `&` 连接**（评审修订 ③，V12） | `Dim s As String = "a" & span` / `span & span` | **BC31394**（`&` 把 operand 装箱到 Object；C# handler 转义对照） |
| S24 | **For Each 展开**（评审修订 ③，V13） | `For Each c As Char In span`（`ReadOnlySpan(Of Char)`） | 无诊断 + 索引器展开 IL（C# `Foreach_Span_01/02` 一致；实现期核对 VB 展开规则） |
| S25 | **集合初始化器**（评审修订 ③，V14） | `Dim a = {New Span(Of Integer)(1), New Span(Of Integer)(2)}` | **BC31396**（数组字面量 → 数组元素，`Binder_Expressions.vb:1608`） |
| S26 | **With 块 / Nothing**（评审修订 ③，V15） | `With span : .Slice(1) : End With`；`Dim s As Span(Of Integer) = Nothing` | With 成员访问无诊断（可用面）；Nothing 赋值无诊断（Nothing=default 不装箱） |
| S27 | **匿名类型成员**（评审修订 ③，V16） | `Dim a = New With {.S = span}` | **BC31396**（`Binder_AnonymousTypes.vb:32/:253`） |
| S28 | **Async 参数 / Iterator**（评审修订 ③，V17） | `Async Function F(s As Span(Of Integer))`；`Iterator Function F() : Yield span : End Function` | Async 参数 → **BC37052**/31396（`IteratorAndAsyncCaptureWalker` 参数路径）；Iterator Yield → **BC37052**（C# `AsyncParams`/`Iterator_01..06` 对应） |

## 4. L2 REPL 层测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

> 装置：`CreateRunner(input:=...)` + `TestConsoleIO`（`:66-91`，内存 `StringReader`/`StringWriter`）。全输出断言（`AssertEqualToleratingWhitespaceDifferences`，含 `>` 提示符与 `«Red»` 错误块）。**纯内存，无副作用。**

| # | 用例 | 输入 | 断言 |
|---|---|---|---|
| R1 | 顶层 Dim of span | `Dim s As New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31396**（ref-like 不能作字段），无运行 |
| R2 | `?` 结果装箱 | `? New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31394**（装箱），无运行 |
| R3 | 末尾裸表达式结果 | `New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31394**（`InitializerRewriter` 结果回传装箱点），无运行 |
| R4 | 方法内局部可用 | 多行提交 `Sub F() : Dim s As New Span(Of Integer)(1) : Console.WriteLine("ok") : End Sub` 后 `F()` | 输出 `ok`，无错误块 |
| R5 | ByVal 传参可用（D1-7 更正） | 多行提交 `Sub F(s As Span(Of Integer)) : ... : End Sub`，**方法体内**调用 `F(New Span(Of Integer)(1))` | 方法体内调用输出 `ok`（可用面）；**顶层**调用 `? F(New Span(Of Integer)(1))` → **BC37052**（D1-7 修复原 TypeLoadException） |
| R6 | ByRef 传参非法 | `Sub F(ByRef s As Span(Of Integer))` | `«Red»` 错误块含 **BC31396** |
| R7 | 跨顶层 Await | `Dim s As New Span(Of Integer)(1)`（提交 1，预期 R1 报错后仍无法继续）→ 或单提交含 `Await Task.FromResult(0)` + 作用域内 Span | 报 **BC37052**（async 状态机捕获；若先报 BC31396 则记录实际首错） |
| R8 | `.vbx` 顶层受限（同 kind） | `main.vbx` 文件内容 `Dim s As New Span(Of Integer)(1)` | 报 **BC31396**（`.vbx` 与 REPL 同 kind 语义一致） |
| R9 | `.vbx` 方法内可用 | `main.vbx` 文件内容 `Sub F() : Dim s As New Span(Of Integer)(1) : Console.WriteLine("ok") : End Sub` + `F()` | 退出码 0、输出 `ok` |
| R10 | `allows ref struct` 接口消费 | 多行提交引用实现 `allows ref struct` 接口的类型（如 `IOf(Of Integer)`），方法内 `Dim x As IOf(Of Integer) = span` | 可用（依赖前置-2 M8；**未就绪则报 BC31396**，如实标注为预期时序差，见 §6 注） |
| R11 | **REPL 三显式转换/接口装箱**（评审修订 ⑥，实现期回填） | `? CType(New Span(Of Integer)(1), Object)` / `? CType(New Span(Of Integer)(1), IDisposable)` | CType→Object 含 **BC31394**；接口含 **BC30311**，无运行 |
| R12 | **REPL `&` 字符串连接**（评审修订 ⑥，实现期回填） | `? ("a" & New Span(Of Integer)(1))` | `«Red»` 错误块含 **BC30452** |
| R13 | **跨提交状态保持**（评审修订 ⑥） | 提交 1 `Dim s As New Span(Of Integer)(1)`（报 BC31396 失败）→ 提交 2 敲 `1 + 2` | 提交 2 正常输出 `3`（前提交失败不污染会话；错误块只含 BC31396 无崩溃） |
| R14 | **方法内 For Each**（评审修订 ⑥，实现期回填） | 多行提交 `Sub F() : Dim s As New ReadOnlySpan(Of Char)("ab".ToCharArray()) : For Each c As Char In s : Console.WriteLine(c) : End Sub` 后 `F()` | 报 **BC30643**（`ReadOnlySpan(Of T).Enumerator.Current` 是 ref 返回属性，VB 不支持；对齐 S24） |
| R15 | **`?` 裸表达式结果精确格式**（评审修订 ⑥） | `? 42` | 精确断言含 `42` 值行（`«Gray»` 标记参照 `TestPrint`；与 optional-question-prefix R 系列一致，不加宽断言） |

## 5. L3 显示层测试（`Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb`）

> 装置：`SymbolDisplay.ToDisplayString` / `ToMinimalDisplayString`。**纯字符串，无副作用。**

| # | 用例 | 符号 | 断言 |
|---|---|---|---|
| D1 | ref-like 类型显示 | `Span(Of Integer)`（含 `IncludeTypeKeyword` 格式） | 输出含 `ByRef Like Structure Span(Of Integer)` |
| D2 | 普通结构体不受影响 | `Point`（普通 struct） | 输出 `Structure Point`，**无** `ByRef Like` 前缀 |
| D3 | 不引入可写语法 | 反断言：`ByRef Like` 不是可声明语法（解析 `ByRef Like Structure X` 报错） | 语言解析不变（可并入 L1 语法回归） |
| D4 | 三遗留特殊类型显示 | `TypedReference` | 无 `ByRef Like` 前缀（非 ref-like，仅 RestrictedType） |

## 6. L4 Regular 模式（`Compilers\VisualBasicSemanticTest\`，`SourceCodeKind.Regular`）

> 装置：`VisualBasicCompilation.Create` + `TestOptions.Regular`，不进宿主、不跑脚本。**纯编译 API，无副作用。**

| # | 用例 | 源码（`.vb` 项目） | 断言 |
|---|---|---|---|
| G1 | 字段受限 | `Class C : Dim f As Span(Of Integer) : End Class` | **BC31396**（新语义：Span 在 Regular 同样受限） |
| G2 | 按值返回**可用**（实现期回填） | `Class C : Function F() As Span(Of Integer) : End Class` | **无诊断**（ref struct 按值返回合法；VB 源符号层无返回值 restricted 检查） |
| G3 | ByRef 参数受限 | `Class C : Sub F(ByRef s As Span(Of Integer)) : End Sub` | **BC31396** |
| G4 | 装箱受限 | `Class C : Sub F() : Dim o As Object = New Span(Of Integer)(1) : End Sub` | **BC31394** |
| G5 | 普通结构体不回归 | `Class C : Dim f As Point : End Class`（普通 struct） | 无诊断 |
| G6 | 三遗留特殊类型不回归 | `Class C : Dim t As TypedReference : End Class` | 仍 **BC31396**（现状行为保持） |
| G7 | 方法局部/ByVal 可用 | `Class C : Sub F(s As Span(Of Integer)) : Dim x As New Span(Of Integer)(1) : End Sub` | 无诊断（可用面在 Regular 同样成立） |
| G8 | **显式转换/接口装箱**（评审修订 ③，实现期回填） | `Class C : Sub F() : Dim o As Object = CType(New Span(Of Integer)(1), Object) : Dim i As IDisposable = New Span(Of Integer)(1) : End Sub` | CType→Object **BC31394**；接口装箱 **BC30311**（接口目标无 restricted 检查，落通用类型不匹配） |
| G9 | **泛型约束**（评审修订 ③，实现期回填） | `Class C(Of T As Span(Of Integer))` | **BC32048**（ref struct 是值类型，不能作特定类型约束；BC32061 只对 restricted 类约束） |
| G10 | **继承方法调用**（评审修订 ③，实现期回填） | `Class C : Sub F() : Dim s As New Span(Of Integer)(1) : s.GetHashCode() : End Sub` | **BC40000**（WRN_UseOfObsoleteSymbol2：`Span` 重写 `GetHashCode` 并标 `[Obsolete]`；BC31393 在 VB 编译器零报告点。注：`New` 表达式不能作独立语句起始，故用局部变量） |
| G11 | **字符串 `&` 连接**（评审修订 ③，实现期回填） | `Class C : Sub F() : Dim s As String = "a" & New Span(Of Integer)(1) : End Sub` | **BC30452**（restricted 操作数无适用 `&` 重载；对齐 C# CS0019） |
| G12 | **数组字面量**（评审修订 ③） | `Class C : Sub F() : Dim a = {New Span(Of Integer)(1)} : End Sub` | **BC31396**（数组元素） |
| G13 | **匿名类型/With/Nothing**（评审修订 ③） | `Class C : Sub F() : Dim a = New With {.S = New Span(Of Integer)(1)} : Dim s As Span(Of Integer) = Nothing : End Sub` | 匿名类型 → **BC31396**；Nothing 赋值无诊断 |

## 7. 既有测试更新清单

| 文件:行号 | 现状 | 更新 | 原因 |
|---|---|---|---|
| `Compilers\VisualBasicSemanticTest\Semantics\RefFieldTests.vb:41`（**评审修订 ①，阻断级**） | **BC30668** `'S(Of Integer)' is obsolete: 'Types with embedded references are not supported in this version of your compiler.'` | **翻转**：D1 suppress obsolete 后无 BC30668 → 改为断言「无 obsolete 诊断，绑定为可用 ref struct」；`:44` BC30656 `Field 'F' is of an unsupported type` 同理复核 | **这是 D1 主目标**（suppress obsolete error）；不翻转则实现后此文件挂红 |
| `Compilers\VisualBasicSemanticTest\Semantics\RefFieldTests.vb:56-80` | `CanUsePassThroughRefStructInstances`：`ReadOnlySpan(Of Char)` 赋值 → BC30668 | **翻转**：无 BC30668 → 断言可用（D1 后 PassThrough ref struct 实例可绑定） | 同上 |
| `Compilers\VisualBasicSemanticTest\Semantics\RefFieldTests.vb:26/39` | 用 C# emit ref struct 引用（`CreateCSharpCompilation` + `EmitToImageReference`） | **保留装置**，作 Span 引用注入先例（§8）；断言随 D1 翻转 | 本文件已演示「造 C# ref struct → VB 消费」完整链路 |
| `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:13522/:13601/:13638/:13683/:17527` | 三遗留特殊类型 restricted 错误断言 | 保留，**不修改**；新增 Span 对照用例（S3-S28） | 谓词扩展不破坏遗留受限；错误码/消息模板不变 |
| `Compilers\VisualBasicSemanticTest\Binding\GenericsTests.vb:245` | `RuntimeArgumentHandle()` → BC31396 | 保留；新增 `Span(Of Integer)` 泛型实参 → BC31396、`Of T As Span` → BC32061 | 同上 |
| `Compilers\VisualBasic\Portable\Symbols\TypeSymbol.vb:587-592` | 注释「VB has no concept of ref-like types」 | 注释随实现更新（改为「ref-like 判定经派生覆盖，源类型默认 False」） | 判定细化后注释过时 |
| `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` | 现有 REPL 测试（:93-839） | 新增用例（§4 R1-R15），**不修改**既有退出码断言 | 结果报错在编译期短路，不影响既有 REPL/退出码行为 |
| `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb` | `HasSubmissionResult` 断言 | 新增：`Span` 结果的提交 → `HasSubmissionResult` 为 False（编译期已报错短路） | byref-like 结果不产生返回值 |

## 8. 无副作用纪律

- **L1/L4**：纯编译 API / `VisualBasicCompilation.Create` + `VerifyDiagnostics`，不跑宿主、不 emit 到磁盘（`CompileAndVerify` 在 `CompilerTestHarness.vb:215-223` 已内存 `MemoryStream` 门控，不加载动态程序集）。
- **L2**：`CreateRunner(input:=...)` + `TestConsoleIO`（`CommandLineRunnerTests.vb:66-91`）——内存 `StringReader`/`StringWriter`；`.vbx` 文件写仅走既有 `CreateIsolatedTempDirectory` 装置（系统临时目录自清理）。无网络、无进程、无注册表。
- **L3**：`SymbolDisplay.ToDisplayString` 纯字符串。
- **引用程序集**（**评审修订 ④**）：Span 测试依赖的引用走 `CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`（`CompilationTestUtils.vb:24-33`，netcore 引用集含真 `Span(Of T)`；先例 `RefFieldTests.vb:67`）——**不是** `System.Memory`（本仓库 TestReferences 无此引用）。跨 C# emit 引用走 `CreateCSharpCompilation` + `EmitToImageReference`（先例 `RefFieldTests.vb:26/39`）。全部经测试基建内置，非网络下载。
- **新增用例若写不了**：按项目约定，不写就测不了的部分在实现期向用户说明并询问（不静默跳过）。

## 9. 与 design-detailed §9 的关系

本计划是 design-detailed §9（设计级要点）的**分层扩展**：

| design-detailed §9 | 本计划扩展 |
|---|---|
| L1 语义层 | 拆为 §3 S1-S28（含 `IsRefLikeType` 语义模型断言 S1、转换分类器 NoConversion 直接断言 S15、三遗留特殊类型回归 S16；**评审修订补** S17-S28：继承方法 BC31393、约束 BC32061、CType/DirectCast/TryCast、接口装箱/拆箱、`&` 连接、For Each、集合初始化器、With/Nothing、匿名类型、Async 参数/Iterator） |
| L2 REPL 层 | 拆为 §4 R1-R15（含 `.vbx` 同 kind 对照 R8/R9、`allows ref struct` 接口消费 R10；**评审修订补** R11-R15：REPL 显式转换/接口装箱、`&` 连接、跨提交状态、For Each、`?` 精确格式） |
| L3 显示层 | 拆为 §5 D1-D4（含普通结构体不受影响 D2、不可声明反断言 D3、三遗留类型 D4） |
| L4 Regular 模式 | 拆为 §6 G1-G13（含「修错不算回归」说明与普通结构体不回归 G5；**评审修订补** G8-G13：显式转换/接口装箱、约束、继承方法、`&` 连接、数组字面量、匿名类型/With/Nothing） |
| —（新增） | §7 既有测试更新清单（**评审修订补 `RefFieldTests.vb` BC30668 翻转**）、§8 无副作用纪律（**评审修订改 Span 引用装置**）、§6 注（`allows ref struct` 前置-2 时序差） |

> 实施期若发现「谓词扩展自动覆盖」的断言与实测不符（如 `Conversions.vb:3393` 守卫对 `Span` 不生效、包装符号需委托 `IsRefLikeType`、S20/S22 实际错误码与预期码不一致），如实记录差异并回填本计划（对齐 optional-question-prefix 任务 test-plan §11 的实现期修订惯例）。
> **评审修订实现期检查点**：① S17 的 `span.GetHashCode()` 实际码是 31393 还是 31394（`ReclassifyInvocationExpressionAsStatement` 路径）——实现期以实测为准回填；② S20 TryCast、S22 拆箱、S24 For Each、S28 Async 参数的实际码同理；③ `RefFieldTests.vb` 翻转后若仍有 BC30656 等其他诊断需逐一核对其预期变化。

> **实现期实测回填（D1-2 验证后，2026-08-13）**：D1-2 落地发现 D1-1 缺**构造类型委托**——`SubstitutedNamedType` / `WrappedNamedTypeSymbol` / `RetargetingNamedTypeSymbol` 未把 `IsRefLikeType` 委托到 `OriginalDefinition` / `_underlyingType`，导致 `Span(Of Integer)` 的 restricted 检查全不生效；已补 3 文件委托（design-detailed §2.2(b)「实现期检查点」落地）。修复后实测码与计划基线差异（**均判为正确、对齐 C#**）：
> - **S7/G2 按值返回 = 可用**（无诊断）：VB 源符号层无返回值 restricted 检查（`SourceMethodSymbol.vb:2346` 不存在）；C# `Span(Of T) Foo()` 合法。
> - **S17/G10 继承方法调用 = BC40000**（WRN_UseOfObsoleteSymbol2）：`Span` 重写 `GetHashCode` 并标 `[Obsolete]`，非继承 Object 调用；BC31393 在 VB 编译器零报告点。
> - **S18/G9 泛型约束 = BC32048**：ref struct 是值类型，不能作特定类型约束；BC32061 只对 restricted 类约束生效。
> - **S20/S22 TryCast/拆箱 = BC30311**：restricted 检查仅对 Object/ValueType 目标（`Binder_Conversions.vb:248`）；接口目标落通用类型不匹配。
> - **S21 接口装箱 = BC30311**（非 BC31394）：接口转换路径无 restricted 检查。
> - **S23/G11/R12 `&` 连接 = BC30452**：restricted 操作数无适用 `&` 重载（对齐 C# CS0019）。
> - **S24 For Each = BC30643**：`ReadOnlySpan(Of T).Enumerator.Current` 是 ref 返回属性，VB 不支持。
> - **S28 Async/Iterator 参数 = BC36932**（ERR_RestrictedResumableType1，`Binder_Utils.vb:1102-1104` 编译期参数检查）；BC37052 是状态机捕获码（S12 已测）。
> - **S10/S11 LINQ/Lambda = emit 诊断**（`VerifyEmitDiagnostics`）：BC36598/BC36640 是 lowering 诊断。
> - **另补**：`SemanticModelAPITests.vb:2979` 的 BC30668 翻转（D1-1 遗漏、D1-2 补）。
> - **D1-5/D1-7 追加**：顶层调用 ByVal span 参数方法 `? F(New Span(Of Integer)(1))` 原运行期 TypeLoadException（Debug hoisting 把 ref-like 合成局部提升成字段），D1-7 在 `IteratorAndAsyncCaptureWalker.HoistInDebugBuild` 加 `Not local.Type.IsRestrictedType()` 守卫并报 BC37052，已修；方法体内调用仍可用。另：`Sub F() : ... : End Sub` 单行方法体被 BC30040 拒（VB 既有规则，非 D1 引入），R4/R5/R6/R9/R14 已改多行；R7 顶层 `Dim s` 首错为 BC31396（脚本类字段检查先于 async 捕获）；`CompilationAPITests` 的 `HasSubmissionResult` span 断言未落地（byref-like 结果在编译期短路，无需宿主断言）。
