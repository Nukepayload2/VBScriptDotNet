# 测试计划：byref-like 类型安全（ref struct 支持，D1 前置-1）

> 状态：测试计划（F3）。依据链：`../../proposals/proposal-byref-like-safety.md`（Active/Proposed）→ `../../meetings/meeting-byref-like-repl-safety.md`（RESOLUTION PROPOSAL A）→ `design-overview.md`（F1）→ `design-detailed.md`（F2，§5/§9）→ 本测试计划。
> 本计划把 design-detailed §5（REPL 三碰撞点零新增）与 §9（设计级矩阵）升级为**分层测试设计**：参考 C# ref struct 语义测试强度（`Compilers\CSharp\Test\` 中 `SpanStackSafetyTests` / `RefEscapingTests` / `AttributeTests_IsByRefLike` / `RefStructInterfacesTests`），并按 **VB 语法特性**（RestrictedType 概念、`.vbx` 与 REPL 同 kind、无 `ref struct` 声明语法、`scoped` 非概念）综合设计。
> 实现阶段由实施者照本计划补测试，验证者按「无副作用纪律」与「行为对照表」核对。

## 0. 目标与验收

- **目标**：在 C# 测试强度（语义诊断 → REPL 全输出 → 显示断言 → Regular 对照）之上，覆盖 byref-like 安全性的全部边界，保证：
  1. `IsRefLikeType` 判定正确（`Span(Of Integer)` 是 ref-like；普通结构体不是）；
  2. suppress obsolete 生效（`Span(Of Integer)` 无 obsolete 错误、可绑定）；
  3. 错误码矩阵（装箱 / Nullable / 字段 / 数组元素 / 返回值 / ByRef 参数 / 泛型参数 / LINQ / Lambda / async 状态机）全对；
  4. REPL 三碰撞点（顶层字段 / 结果装箱 / 跨 Await）报正确错误；
  5. 方法体局部 / ByVal 值参数 / `allows ref struct` 接口消费**可用**；
  6. 显示「ByRef Like Structure」；
  7. `.vbx` 与 REPL 语义一致、Regular 零影响（Span 在 `.vb` 里同样受限 = 新语义，属「修错不算回归」D4）。
- **验收**：四层矩阵全绿；既有测试按 §7 更新；无副作用纪律满足（§8）。

## 1. 参考的 C# 测试强度（分层）

C# 对同一特性（ref-like 安全 / `[IsByRefLike]`）的测试锚点与强度：

| 层 | C# 测试锚点 | 强度特征 | VB 落点 |
|---|---|---|---|
| L1 语义层 | `CSharp\Test\Semantic\Semantics\SpanStackSafetyTests.cs`（ref struct 栈上安全、字段/装箱/await 限制）、`SemanticErrorTests.cs`、`AttributeTests_IsByRefLike.cs`（`Emit3\Attributes\`） | 精确 `VerifyDiagnostics` 断言错误码与位置；同一类型在合法/非法两种场景对照 | `Compilers\VisualBasicSemanticTest\`（`CreateSubmission` / `VisualBasicCompilation.Create`，`BasicTestBase.vb:437`） |
| L2 REPL/脚本 | C# csi 端到端（`CommandLineTests.cs`）；`RefStructInterfacesTests.cs`（`Emit3\`，`allows ref struct` 接口消费） | 全输出断言（错误块 + 无运行）；`allows ref struct` 接口消费端到端 | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`（`CreateRunner(input:=...)` + `TestConsoleIO`） |
| L3 显示层 | C# `SymbolDisplay` 对 `ref struct` 的显示测试 | 字符串精确断言 | `Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb` |
| L4 常规模式 | C# 普通 `ref struct` 字段/装箱错误在 Regular 项目同样生效 | 编译 API 断言，不跑宿主 | `Compilers\VisualBasicSemanticTest\`（`SourceCodeKind.Regular`） |

> 强度原则：**同一输入在「脚本（Script）/ 普通（Regular）」两种模式下分别断言**（C# 的 ref struct 规则两种模式一致，VB 跟随）；**同一类型既要验证受限面（字段/装箱/ByRef/await）也要验证可用面（方法局部 / ByVal 传参）**。

## 2. VB 语法特性综合维度

| 维度 | VB 特性 | 关键用例来源 |
|---|---|---|
| V1 RestrictedType 判定细化 | `Span(Of Integer)` 是 ref-like（`IsRefLikeType` 读 `[IsByRefLike]`）；普通结构体不是；三遗留特殊类型仍受限 | design-detailed §2 |
| V2 suppress obsolete | `Span(Of Integer)` 直接可写、可绑定（无 obsolete 错误） | design-detailed §3 |
| V3 错误码矩阵 | BC31393/31394/31396/32061/36598/36640/37052 | design-detailed §4 |
| V4 REPL 三碰撞点 | 顶层字段（BC31396）/ 结果装箱（BC31394）/ 跨 Await（BC37052） | design-detailed §5 |
| V5 方法体可用面 | 局部 / ByVal 值参数 / 返回（返回→BC31396） | proposal §4b |
| V6 `allows ref struct` 接口消费 | 依赖前置-2（M8）；未就绪则如实标注时序差 | design-detailed §7 |
| V7 显示 | 「ByRef Like Structure」仅显示、不可声明 | design-detailed §6 |
| V8 `.vbx` 与 REPL 同 kind | `.vbx` 顶层同样受限；诊断不区分 REPL/常规 | overview §6 |
| V9 Regular 零影响 | `.vb` 里 Span 作字段/返回/ByRef/装箱报 restricted；普通结构体不变 | design-detailed §8.2 |

## 3. L1 语义层测试（`Compilers\VisualBasicSemanticTest\`）

> 装置：`VisualBasicCompilation.Create`（Regular）/ `CreateSubmission`（Script，`BasicTestBase.vb:437`）+ `AssertTheseDiagnostics` / `VerifyDiagnostics`。引用含 `Span(Of T)` 的引用程序集（`TestReferences` 既有 mscorlib/`System.Memory`）。**纯编译 API，无副作用。** 测试命名建议仿 C# `SpanStackSafetyTests` 的 `<Features>_<Scenario>` 风格。

| # | 用例 | 提交/源码 | 断言 |
|---|---|---|---|
| S1 | IsRefLikeType 判定 | `Dim s As Span(Of Integer)`（方法内，可编译） | `GetTypeInfo(...).Type.IsRefLikeType` 为 True（语义模型断言）；普通结构体为 False |
| S2 | suppress obsolete | `Dim s As New Span(Of Integer)(1)`（方法内） | **无** obsolete 诊断、可绑定（现状报 obsolete 错误，实现后消失） |
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

## 4. L2 REPL 层测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

> 装置：`CreateRunner(input:=...)` + `TestConsoleIO`（`:66-91`，内存 `StringReader`/`StringWriter`）。全输出断言（`AssertEqualToleratingWhitespaceDifferences`，含 `>` 提示符与 `«Red»` 错误块）。**纯内存，无副作用。**

| # | 用例 | 输入 | 断言 |
|---|---|---|---|
| R1 | 顶层 Dim of span | `Dim s As New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31396**（ref-like 不能作字段），无运行 |
| R2 | `?` 结果装箱 | `? New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31394**（装箱），无运行 |
| R3 | 末尾裸表达式结果 | `New Span(Of Integer)(1)` | `«Red»` 错误块含 **BC31394**（`InitializerRewriter` 结果回传装箱点），无运行 |
| R4 | 方法内局部可用 | 多行提交 `Sub F() : Dim s As New Span(Of Integer)(1) : Console.WriteLine("ok") : End Sub` 后 `F()` | 输出 `ok`，无错误块 |
| R5 | ByVal 传参可用 | 多行提交 `Sub F(s As Span(Of Integer)) : Console.WriteLine("ok") : End Sub` 后 `F(New Span(Of Integer)(1))` | 输出 `ok`，无错误块 |
| R6 | ByRef 传参非法 | `Sub F(ByRef s As Span(Of Integer))` | `«Red»` 错误块含 **BC31396** |
| R7 | 跨顶层 Await | `Dim s As New Span(Of Integer)(1)`（提交 1，预期 R1 报错后仍无法继续）→ 或单提交含 `Await Task.FromResult(0)` + 作用域内 Span | 报 **BC37052**（async 状态机捕获；若先报 BC31396 则记录实际首错） |
| R8 | `.vbx` 顶层受限（同 kind） | `main.vbx` 文件内容 `Dim s As New Span(Of Integer)(1)` | 报 **BC31396**（`.vbx` 与 REPL 同 kind 语义一致） |
| R9 | `.vbx` 方法内可用 | `main.vbx` 文件内容 `Sub F() : Dim s As New Span(Of Integer)(1) : Console.WriteLine("ok") : End Sub` + `F()` | 退出码 0、输出 `ok` |
| R10 | `allows ref struct` 接口消费 | 多行提交引用实现 `allows ref struct` 接口的类型（如 `IOf(Of Integer)`），方法内 `Dim x As IOf(Of Integer) = span` | 可用（依赖前置-2 M8；**未就绪则报 BC31396**，如实标注为预期时序差，见 §6 注） |

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
| G2 | 返回值受限 | `Class C : Function F() As Span(Of Integer) : End Class` | **BC31396** |
| G3 | ByRef 参数受限 | `Class C : Sub F(ByRef s As Span(Of Integer)) : End Sub` | **BC31396** |
| G4 | 装箱受限 | `Class C : Sub F() : Dim o As Object = New Span(Of Integer)(1) : End Sub` | **BC31394** |
| G5 | 普通结构体不回归 | `Class C : Dim f As Point : End Class`（普通 struct） | 无诊断 |
| G6 | 三遗留特殊类型不回归 | `Class C : Dim t As TypedReference : End Class` | 仍 **BC31396**（现状行为保持） |
| G7 | 方法局部/ByVal 可用 | `Class C : Sub F(s As Span(Of Integer)) : Dim x As New Span(Of Integer)(1) : End Sub` | 无诊断（可用面在 Regular 同样成立） |

> **修错不算回归（D4）**：G1/G2/G3/G4 是**新语义**——`.vb` 项目里 Span 作字段/返回/ByRef/装箱现在报 restricted 错误（取代「obsolete 错误 / 运行期 InvalidProgramException」）。这不是行为分裂（`.vbx` 与 `.vb` 语义一致正是本提案前提），属 D4 定义的修错。老项目依赖错误运行行为的需迁移说明。

## 7. 既有测试更新清单

| 文件:行号 | 现状 | 更新 | 原因 |
|---|---|---|---|
| `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:13522/:13601/:13638/:13683/:17527` | 三遗留特殊类型 restricted 错误断言 | 保留，**不修改**；新增 Span 对照用例（S3-S12） | 谓词扩展不破坏遗留受限；错误码/消息模板不变 |
| `Compilers\VisualBasicSemanticTest\Binding\GenericsTests.vb:245` | `RuntimeArgumentHandle()` → BC31396 | 保留；新增 `Span(Of Integer)` 泛型实参 → BC31396 | 同上 |
| `Compilers\VisualBasic\Portable\Symbols\TypeSymbol.vb:587-592` | 注释「VB has no concept of ref-like types」 | 注释随实现更新（改为「ref-like 判定经派生覆盖，源类型默认 False」） | 判定细化后注释过时 |
| `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` | 现有 REPL 测试（:93-839） | 新增用例（§4 R1-R10），**不修改**既有退出码断言 | 结果报错在编译期短路，不影响既有 REPL/退出码行为 |
| `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb` | `HasSubmissionResult` 断言 | 新增：`Span` 结果的提交 → `HasSubmissionResult` 为 False（编译期已报错短路） | byref-like 结果不产生返回值 |

## 8. 无副作用纪律

- **L1/L4**：纯编译 API / `VisualBasicCompilation.Create` + `VerifyDiagnostics`，不跑宿主、不 emit 到磁盘（`CompileAndVerify` 在 `CompilerTestHarness.vb:215-223` 已内存 `MemoryStream` 门控，不加载动态程序集）。
- **L2**：`CreateRunner(input:=...)` + `TestConsoleIO`（`CommandLineRunnerTests.vb:66-91`）——内存 `StringReader`/`StringWriter`；`.vbx` 文件写仅走既有 `CreateIsolatedTempDirectory` 装置（系统临时目录自清理）。无网络、无进程、无注册表。
- **L3**：`SymbolDisplay.ToDisplayString` 纯字符串。
- **引用程序集**：Span 测试依赖的引用经 `TestReferences`（测试基础设施内置，非网络下载）；若目标 corlib 无 `Span(Of T)`，用 `System.Memory` 测试引用（既有测试装置），不引入网络获取。
- **新增用例若写不了**：按项目约定，不写就测不了的部分在实现期向用户说明并询问（不静默跳过）。

## 9. 与 design-detailed §9 的关系

本计划是 design-detailed §9（设计级要点）的**分层扩展**：

| design-detailed §9 | 本计划扩展 |
|---|---|
| L1 语义层 | 拆为 §3 S1-S16（含 `IsRefLikeType` 语义模型断言 S1、转换分类器 NoConversion 直接断言 S15、三遗留特殊类型回归 S16） |
| L2 REPL 层 | 拆为 §4 R1-R10（含 `.vbx` 同 kind 对照 R8/R9、`allows ref struct` 接口消费 R10） |
| L3 显示层 | 拆为 §5 D1-D4（含普通结构体不受影响 D2、不可声明反断言 D3、三遗留类型 D4） |
| L4 Regular 模式 | 拆为 §6 G1-G7（含「修错不算回归」说明与普通结构体不回归 G5） |
| —（新增） | §7 既有测试更新清单、§8 无副作用纪律、§6 注（`allows ref struct` 前置-2 时序差） |

> 实施期若发现「谓词扩展自动覆盖」的断言与实测不符（如 `Conversions.vb:3393` 守卫对 `Span` 不生效、包装符号需委托 `IsRefLikeType`），如实记录差异并回填本计划（对齐 optional-question-prefix 任务 test-plan §11 的实现期修订惯例）。
