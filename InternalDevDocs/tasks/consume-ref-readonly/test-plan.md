# 测试计划：消费 ref readonly 返回（Consume Ref Readonly Returns，D4 → P1）

> 状态：测试计划（F3）。依据链：`../../meetings/meeting-consume-ref-readonly.md`（RESOLUTION 九条）→ `design-overview.md`（F1）→ `design-detailed.md`（F2，改动点 1-7）→ 本测试计划。
> 本计划把 design-detailed 的各改动点升级为**分层测试设计**：参考 C# ref readonly / ref-safety 测试强度，并按 **VB 语法特性**（ReadOnlySpan 索引器、copy-out 文化、With 块、复合赋值、Mid、类型推断、表达式 lambda、REPL `.vbx` 同 kind）综合设计。
> **写路径矩阵（§2）是 P1 硬验收**：直接赋值/复合赋值/Mid/ByRef 实参（可变/readonly 接收方）/With 块/成员链/For Each/类型推断/表达式 lambda——缺一即打回。

## 0. 目标与验收

- **目标**：在四层矩阵（语义 → REPL → 显示 → Regular）之上，覆盖「消费 ref readonly 返回」的全部边界，保证：
  1. `s(0)`（`ReadOnlySpan(Of T).Item`）读取解锁（modreq 豁免，auto-deref 生效）；
  2. `.GetPinnableReference()` 解锁；
  3. `ReturnsByRefReadonly`/`RefKind.RefReadOnly` 暴露到符号层 + 语义模型（分析器/工具可读真实语义 + 一致性校验）；
  4. **写路径全覆盖**：直接赋值拒、复合赋值拒、Mid 拒、With 块内 readonly-lvalue 成员写拒、成员链成员写拒（均 `ERR_LValueRequired`(30068)）；ByRef 实参 copy-out 传副本且写回丢弃；值捕获仅服务 With/读取；For Each 解锁；类型推断褪 ByRef；表达式 lambda/draft-rewrite 不写穿；
  5. `in` 参数（虚方法/委托）顺带解锁；
  6. SymbolDisplay：debug 格式 `ByRef ReadOnly`、IDE 格式退化（不带）、ref-like 类型显示「ByRef Like Structure」不回归；
  7. `.vbx` 与 REPL 同 kind 语义一致、Regular 零影响（读取解锁 + 写路径拒绝在两种模式一致）；
  8. 既有测试更新（S24 翻转、spec 条目、错误码矩阵）无回归。
- **验收**：四层矩阵全绿；既有测试按 §6 更新；无副作用纪律满足（§7）。

## 1. 参考的 C# 测试强度（分层）

| 层 | C# 测试锚点 | 强度特征 | VB 落点 |
|---|---|---|---|
| L1 语义层 | C# ref-safety / readonly-ref 语义测试（`RefReadOnly` 相关语义测试）、`CodeGenReadOnlySpanConstructionTest.cs:1643-1651`（foreach over ReadOnlySpan 全绿） | 精确 `VerifyDiagnostics` 断言错误码与位置；同一类型在合法/非法两种场景对照 | `Compilers\VisualBasicSemanticTest\`（`CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`，`BasicTestBase`） |
| L2 REPL/脚本 | C# csi 端到端 | 全输出断言（错误块 + 无运行 / 输出值） | `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`（`CreateRunner(input:=...)` + `TestConsoleIO`） |
| L3 显示层 | C# `SymbolDisplay` 对 `ref readonly` 的显示测试 | 字符串精确断言 | `Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb` |
| L4 常规模式 | C# 普通项目 ref readonly 消费 | 编译 API 断言，不跑宿主 | `Compilers\VisualBasicSemanticTest\`（`SourceCodeKind.Regular`） |

> 强度原则：**同一输入在「脚本（Script）/ 普通（Regular）」两种模式下分别断言**；**同一 readonly 来源既要验证可读面（读取/推断/For Each/ByVal）也要验证拒绝面（直接赋值/复合/Mid/ByRef 直传）**。
>
> **引用装置（关键）**：Span 引用走 `CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`（netcore 引用集含真 `ReadOnlySpan(Of T)`；先例 `ByRefLikeTests.vb:532`）。**C# emit 测试库**（声明 `ref readonly` 索引器/方法/属性、`in` 参数虚方法/委托、`ref readonly string` 返回等）走 `CreateCSharpCompilation` + `EmitToImageReference`（先例 `RefFieldTests.vb:26/39`）。全部经测试基建内置，非网络下载。

## 2. 写路径矩阵（P1 硬验收）

> 每个写路径必须给出「拒绝（`ERR_LValueRequired`(30068)）」或「副本（copy-out/值捕获，值不变）」的断言。C# emit 测试库成员：`Item(i)`（ref readonly 索引器）、`Get() `（ref readonly 方法）、`SpanItem`（ref readonly 属性）、`M(in int)` / `Virt(in int)`（虚）/ `Del`（委托，in 参数）、`S`（ref readonly String 属性，供 Mid 用例）。

> 安全不变量：**任何写路径都不得写穿只读内存**——要么「拒绝」（`ERR_LValueRequired`(30068)，store-through-ref 直写 readonly-lvalue），要么「副本」（copy-out，操作副本、原值不变）。断言二选一，均安全；W7b（With 块 `.Member` 写）与 W8b（成员链成员写）**复会 R8（2026-09-01）定稿为拒绝**（与链式一致，不再操作副本），S13b/S13c/S13e 翻转、S12/S13d/S13f 读取保持无诊断。

| # | 写路径 | 用例形态 | 期望 |
|---|---|---|---|
| W1 | 直接赋值 | `s(0) = 5`（`ReadOnlySpan(Of Integer).Item`） | **拒绝：`ERR_LValueRequired`(30068)**（readonly-lvalue store-through-ref，改 3） |
| W2 | 复合赋值 | `s(0) += 5` | **拒绝：`ERR_LValueRequired`(30068)**（`AdjustAssignmentTarget` 经 `BindCompoundAssignment` 自动覆盖） |
| W3 | Mid 赋值 | `Mid(o.S, 1) = "x"`（`o.S` 是 C# emit 库 `ref readonly String` 属性） | **拒绝：`ERR_LValueRequired`(30068)**（Mid 经 `AdjustAssignmentTarget`） |
| W4 | ByRef 实参（可变接收方） | `M(s(0))`（`M(ByRef v As Integer)` 内 `v = 99`） | **副本：无诊断 + copy-out 丢弃写回**——运行后 `s(0)` 仍 10（改 4） |
| W5 | ByRef 实参（readonly/in 接收方） | `M(s(0))`（C# emit 库 `M(in int)`） | **副本：无诊断 + copy-out 传副本**——值正确、无写穿（VB 映射 in→Ref，多一次拷贝；§4a v1 不做零拷贝） |
| W6 | With 块 `.Member` 读取 | `With h(0) : Dim y = .X : End With`（C# emit 库 `ref readonly Row Item`，`Row.X` 可写） | 无诊断 + y 为值（值捕获读，改 5） |
| W7a | With 块内对 readonly-lvalue 直接赋值 | `With s : .Item(0) = 5 : End With` | **拒绝：`ERR_LValueRequired`(30068)**（`.Item(0)` 本身是 readonly-lvalue，`AdjustAssignmentTarget` 检查） |
| W7b | With 块 `.Member` 写入（R8 核心） | `With h(0) : .X = 5 : End With`（`h(0)` 是 `ref readonly Row`） | **拒绝：`ERR_LValueRequired`(30068)**——readonly-lvalue 接收器走 RValue placeholder、`.X` 落非 lvalue（复会 R8 定稿，与链式 W8b 一致；值捕获仅服务读） |
| W8a | 成员链直接赋值 | `o.S(0) = newRow`（`o.S` 返回 `ref readonly Row` 索引器） | **拒绝：`ERR_LValueRequired`(30068)**（store-through-ref 直写 readonly-lvalue） |
| W8b | 成员链成员写（R8 成员链） | `o.S(0).X = 5` | **拒绝：`ERR_LValueRequired`(30068)**——FieldAccess/ArrayAccess 目标基 receiver 为 readonly-lvalue 即拒（改 5 收口，`AdjustAssignmentTarget` FieldAccess/ArrayAccess 分支）；【F13 实测：拒绝（非值捕获），S15b/S15c 绿】 |
| W9 | For Each | `For Each c As Char In "ab".AsSpan()` | **无诊断 + 枚举值正确**（改 6，S24 翻转） |
| W10 | 类型推断 | `Dim x = s(0)` | 无诊断 + x 为元素类型值（褪 ByRef + auto-deref） |
| W11 | 表达式 lambda / draft-rewrite | 表达式 lambda 内读取 `s(0)`；draft-rewrite 场景（`WithExpressionRewriter` 值捕获路径） | 无诊断、不写穿（改 5：值捕获仅服务读） |
| W12 | 可变 ref 返回**不回归** | C# emit 库 `ref int` 索引器：读值、`= 42` 写回、作 ByRef 实参 | 全部仍可用（纯 ref 写回语义保持，`BoundAssignmentOperator.vb:59-61` AccessKind.Get 写回） |

## 3. L1 语义层测试（`Compilers\VisualBasicSemanticTest\`）

> 装置：`CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`（Span）+ `CreateCSharpCompilation`/`EmitToImageReference`（C# emit 库）+ `VerifyDiagnostics`。纯编译 API，无副作用。Span 用例复跑会议实证三件套（Length 3 / Item BC30643 / GetPinnableReference BC30657）作为**翻转前对照**（实现后断言翻转）。

| # | 用例 | 源码 | 断言 |
|---|---|---|---|
| S1 | 读取解锁（索引器） | `"abc".AsSpan()(0)` | **无诊断**（原 BC30643 翻转） |
| S2 | 读取解锁（方法） | `"abc".AsSpan().GetPinnableReference()` | **无诊断**（原 BC30657 翻转） |
| S3 | 读取解锁（普通属性不回归） | `"abc".AsSpan().Length` | 无诊断 + 值 3 |
| S4 | 语义模型只读标志 | `GetSymbolInfo(s(0))` / `GetTypeInfo` | `PropertySymbol.ReturnsByRefReadonly=True`、`RefKind.RefReadOnly`（改 2） |
| S5 | 语义模型方法只读标志 | `GetSymbolInfo(GetPinnableReference())` | `MethodSymbol.ReturnsByRefReadonly=True`、`RefKind.RefReadOnly` |
| S6 | 直接赋值拒绝 | `Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10}) : s(0) = 5` | **`ERR_LValueRequired`(30068)**（W1） |
| S7 | 复合赋值拒绝 | `s(0) += 5` | **`ERR_LValueRequired`(30068)**（W2） |
| S8 | Mid 赋值拒绝 | `Mid(o.S, 1) = "x"`（C# emit 库 `ref readonly String` 属性） | **`ERR_LValueRequired`(30068)**（W3） |
| S9 | ByRef copy-out 丢弃写回 | `Sub M(ByRef v As Integer) : v = 99 : End Sub` + `M(s(0))` | 无诊断；**运行断言 `s(0) = 10`**（W4，改 4） |
| S10 | ByRef `in` 接收方 | C# emit 库 `M(in int)` + `M(s(0))` | 无诊断；值正确、无写穿（W5） |
| S11 | 类型推断 | `Dim x = s(0)` | 无诊断；x 类型 = 元素类型（W10） |
| S12 | With `.Member` 读取 | `With h(0) : Dim y = .X : End With`（C# emit 库 `ref readonly Row`） | 无诊断 + y 为值（W6，改 5） |
| S13 | With 内 readonly-lvalue 直接赋值 | `With s : .Item(0) = 5 : End With` | **`ERR_LValueRequired`(30068)**（W7a） |
| S13b | With `.Member` 写入（R8 核心） | `With h(0) : .X = 5 : End With` | **恰一 `ERR_LValueRequired`(30068) @ `.X`**（W7b，复会 R8 定稿拒绝；值捕获仅服务读） |
| S13c | With 内嵌套写（复会 R8 补充） | `With h(0) : .Inner.X = 5 : End With`（`.Inner` 是 readonly-lvalue 的成员访问） | **恰一 `ERR_LValueRequired`(30068) @ `.Inner.X`**（完整 LHS，成员链版 helper 判定，W7b 嵌套形态；F20 测试定名 `S13g_NestedWriteInWithBlock`） |
| S13c2 | With 内方法调用接收器 `.Member` 写（F20 补录） | `With o.S(0) : .X = 5 : End With`（`o.S(0)` 是 ref readonly Row 方法调用） | **恰一 `ERR_LValueRequired`(30068) @ `.X`**（readonly 方法调用 receiver 走 RValue placeholder，BoundKind.Call 分支；测试 `S13c_WithBlockReadonlyMethodCallReceiverMemberWrite`） |
| S13d | With 内成员链读取（复会 R8 补充；F20 定稿 iterator 形态） | iterator 内 `With h(0) : Yield .X : End With` | 无诊断（值捕获读保留，覆盖 `DoNotUseByRefLocal`；readonly 值捕获总是安全，不报 BC37326；测试 `S13d_WithBlockReadonlyReceiverInIterator`） |
| S13e | With 内成员链写（复会 R8 补充） | `With o.S(0).Inner : .X = 5 : End With`（receiver 为 readonly-lvalue 成员访问） | **恰一 `ERR_LValueRequired`(30068) @ `.X`**（成员链版 helper 判定） |
| S13f | With 内 `.Item` 读取（复会 R8 补充；F20 定稿成员链读形态） | `With o.S(0).Inner : Dim y = .X : End With` | 无诊断 + y 为值（输出 10，值捕获读保留；测试 `S13f_WithBlockReadonlyReceiverMemberChainRead`；`.Item(0)` 读由 G4/S13 覆盖） |
| S14 | 成员链读取 | `o.S(0)` / `o.S(0).X`（C# emit 库） | 无诊断 |
| S15a | 成员链直接赋值 | `o.S(0) = newRow`（C# emit 库） | **`ERR_LValueRequired`(30068)**（W8a） |
| S15b | 成员链成员写 | `o.S(0).X = 5`（C# emit 库） | **`ERR_LValueRequired`(30068)**（W8b 拒绝面定稿；【F13 实测拒绝，S15b 绿】） |
| S16 | For Each 解锁 | `For Each c As Char In "ab".AsSpan() : Console.WriteLine(c) : Next` | **无诊断** + 枚举正确（W9，改 6） |
| S17 | 表达式 lambda 读取 | `Dim f = Function() s(0)`（表达式 lambda 内读取） | 无诊断（W11） |
| S18 | `in` 参数虚方法调用 | C# emit 库虚方法 `Virt(in int)`，派生/基类调用 `o.Virt(5)` | **无诊断**（R2 顺带解锁；原因 modreq(In) 被挡） |
| S19 | `in` 参数委托调用 | C# emit 库委托 `Delegate Sub Del(in x As Integer)` + `d(5)` | **无诊断**（R2 顺带解锁） |
| S20 | 一致性校验 | C# emit 库手工造「非 ByRef 返回却带 modreq(In)」成员 | **unsupported**（改 2d，仿 `PEParameterSymbol.cs:415-421`；防第三方乱写元数据） |
| S21 | 可变 ref 返回不回归 | C# emit 库 `ref int` 索引器：读值/`= 42` 写回/作 ByRef 实参 | 全部无诊断 + 写回生效（W12） |
| S22 | 错误码不复用 | 触发`ERR_LValueRequired`(30068)的用例 | 断言**不是** `ERR_ReadOnlyProperty1`(30098)/`ERR_ReadOnlyAssignment`(30064)/`ERR_UnsupportedProperty1`(30643)/`ERR_UnsupportedMethod1`(30657)（30064 显式否决：`'ReadOnly' variable` 对 `s(0)` 措辞失准） |
| S22b | 30064 切换出口（备用） | 若 IDE 含 `IncludeRef` 前提证伪，30068 切 30064 的备选测试（`Binder_Expressions.vb:1805` 一行切换） | 实现期先把 30064 备选断言写好，前提证伪时启用 |
| S23 | 普通属性赋值不回归 | `Dim x As New List(Of Integer) : x(0) = 5` | 无诊断（非 readonly 索引器写回保持） |

## 4. L2 REPL 层测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb`）

> 装置：`CreateRunner(input:=...)` + `TestConsoleIO`（内存 `StringReader`/`StringWriter`）。全输出断言（`AssertEqualToleratingWhitespaceDifferences`，含 `>` 提示符与 `«Red»` 错误块）。**纯内存，无副作用。**

| # | 用例 | 输入 | 断言 |
|---|---|---|---|
| R1 | 读取解锁（`?` 索引器） | `? "ab".AsSpan()(0)` | 输出 `"a"c`（VB formatter 显 Char 'a'，F16 实测），无错误块 |
| R2 | 读取解锁（GetPinnableReference） | `? "ab".AsSpan().GetPinnableReference()` | 输出 `"a"c`（F16 实测：RValue auto-deref 显 Char 'a'），无错误块 |
| R3 | 直接赋值拒绝 | 方法体 `Sub F()` 内 `Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})` + `s(0) = 5`（F16 实测：顶层 `Dim ReadOnlySpan` 受 BC31396 拒，须放方法体局部） | 交互模式 Sub 定义提交即报：`«Red»` 错误块含**`ERR_LValueRequired`(30068)**，无运行 |
| R4 | ByRef copy-out 丢弃写回 | 定义 `Sub M(ByRef v As Integer) : v = 99 : End Sub`，后 `Function G() : Dim s ... : M(s(0)) : Return s(0) : End Function` + `? G()`（F16 实测：顶层 `Dim ReadOnlySpan` 受 BC31396 拒，放方法体内） | 输出 `10`（写回丢弃，值不变） |
| R5 | For Each 脚本 | 多行提交 `Function F() As String`（StringBuilder 拼串 `a;b;`）+ `? F()`（F14 实测：TestConsoleIO 不捕获脚本 `Console.WriteLine`，改 Function + `?` 形态） | 输出 `"a;b;"`（枚举正确，无 BC30643） |
| R6 | `.vbx` 同 kind | `main.vbx` 含 `Print("ab".AsSpan()(0))`（F16 实测：脚本模式 `Console.Write` 不被 TestConsoleIO 捕获，用 `Print`） | 退出码 0、输出 `"a"c`（VB formatter 显 Char 'a'） |
| R7 | `.vbx` 直接赋值拒绝 | `main.vbx` 含方法体 `Sub F()` 内 `Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10})` + `s(0) = 5`（F16 实测：顶层 `Dim ReadOnlySpan` 受 BC31396 拒，须放方法体局部） | 退出码 1（编译错误），无 `«Red»` 块（.vbx 编译错误走 `Console.Error`，TeeWriter 并入 Out）、断言含**`ERR_LValueRequired`(30068)** |
| R8 | 跨提交状态保持 | 提交 1 为方法体 `Sub F()` 定义（内 `s(0) = 5`，报 30068；F16 实测：顶层 `Dim ReadOnlySpan` 受 BC31396 拒，放方法体内）→ 提交 2 `? 1 + 2` | 提交 2 输出 `3`（前提交失败不污染会话） |

> **F16 实测偏离（实现期回填）**：
> 1. **Char 显示形态**：`? "ab".AsSpan()(0)` / `? "ab".AsSpan().GetPinnableReference()` 的 REPL 输出是 `"a"c`（VB formatter 把 Char 显成字面量），非本表原计划的数字 `97`。断言以实测 `"a"c` 为准（值同为 Char 'a' = 97）。
> 2. **REPL/.vbx 顶层 byref-like `Dim` 受限**：`Dim s As New ReadOnlySpan(Of Integer)(...)` 在交互/脚本模式顶层被 byref-like 字段规则拒（BC31396，byref-like-safety 任务的 REPL 语义：byref-like 局部才可用）。因此写路径用例（R3/R4/R7/R8）把 `Dim s` 放进方法体局部（`Sub F()`/`Function G()`）——方法体内 byref-like 局部合法，`s(0) = 5` 仍触发 30068。
> 3. **`.vbx` 脚本模式编译错误形态**：无 `«Red»` 块（交互模式才有）；编译错误走 `Console.Error`（`TestConsoleIO` 的 TeeWriter 并入 Out），断言改为退出码 1 + 含 BC30068。
> 4. **脚本输出捕获**：`.vbx`/交互模式 `Console.Write`/`Console.WriteLine` 不被 `TestConsoleIO` 捕获，读取断言改用 `Print(...)`（.vbx）或 `Function` 拼串 + `? F()`（交互，同 F14 R5 形态）。

## 5. L3 显示层测试（`Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb`）

> 装置：`SymbolDisplay.ToDisplayString` / `ToMinimalDisplayString`。纯字符串，无副作用。

| # | 用例 | 符号 | 断言 |
|---|---|---|---|
| D1 | debug 格式 readonly 返回 | C# emit 库 `ref readonly int` 方法/属性，用纯 debug/内部诊断格式显示 | 含 `ByRef ReadOnly Integer`（改 7） |
| D2 | IDE 默认格式退化 | 同上，默认/IDE 格式 | **无** `ByRef`/`ReadOnly`，显示元素类型 `Integer`（tooltip 退化形态，R5） |
| D3 | 可变 ref 返回不回归 | C# emit 库 `ref int` 方法，debug 格式 | 含 `ByRef Integer`，**无** `ReadOnly` |
| D4 | ref-like 类型显示不回归 | `Span(Of Integer)`（`IncludeTypeKeyword`） | 仍输出 `ByRef Like Structure Span(Of Integer)`（`SymbolDisplayVisitor.Types.vb:445-447` 已存在，不改） |
| D5 | 语义模型 RefKind | `GetSymbolInfo` 的 `RefKind` | readonly 返回 = `RefKind.RefReadOnly`（对齐改 2b） |
| D6 | 退化不许比纯 ref 更少（IncludeRef 前提验证） | C# emit 库 `ref readonly int` 方法，在**含 `IncludeRef` 的格式**下显示 | 至少与纯 ref 对等（纯 ref 对照 `SymbolDisplayTests.vb:5404-5407` 显 `ReadOnly ByRef P As Integer`）；**这是「IDE 格式不含 IncludeRef」前提的第一验证项**——若含 IncludeRef，退化前提证伪 → 30068 切 30064 |
| D7 | 叠字回归 | `ReadOnly` 声明属性 + ref readonly 返回（`VisitProperty` 描述符 `ReadOnly` + 合成 `ByRef ReadOnly`） | 不得输出 `ReadOnly ByRef ReadOnly Integer` 双叠（词序/去重，改 7） |
| D8 | 词序规范形 + debug 可达路径 | C# emit 库 `ref readonly int` 方法，纯 debug 格式 | 词序锁定为规范形（如 `ReadOnly ByRef Integer`）进 `SymbolDisplayTests` 固化；debug 格式必须有真实可达路径（防死代码） |

## 6. L4 Regular 模式（`Compilers\VisualBasicSemanticTest\`，`SourceCodeKind.Regular`）

> 装置：`VisualBasicCompilation.Create` + `TestOptions.Regular`，不进宿主、不跑脚本。纯编译 API，无副作用。

| # | 用例 | 源码（`.vb` 项目） | 断言 |
|---|---|---|---|
| G1 | 读取解锁（Regular） | `Class C : Sub F() : Dim x = "ab".AsSpan()(0) : End Sub : End Class` | 无诊断（Regular 同样解锁） |
| G2 | 直接赋值拒绝（Regular） | `Class C : Sub F() : Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10}) : s(0) = 5 : End Sub` | **`ERR_LValueRequired`(30068)** |
| G3 | ByRef copy-out 丢弃写回（Regular） | `Class C : Sub M(ByRef v As Integer) : v = 99 : End Sub : Sub F() : Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10}) : M(s(0)) : ...` | 无诊断；运行断言 `s(0)=10` |
| G4 | With 值捕获（Regular） | `Class C : Sub F() : Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10}) : With s : Dim x = .Item(0) : End With : End Sub` | 无诊断 |
| G5 | For Each（Regular） | `Class C : Sub F() : For Each c As Char In "ab".AsSpan() : ... : Next : End Sub` | 无诊断 |
| G6 | 复合赋值/Mid（Regular） | `s(0) += 5` / `Mid(o.S, 1) = "x"` | **`ERR_LValueRequired`(30068)** |
| G7 | 可变 ref 写回（Regular） | C# emit 库 `ref int` 索引器 `= 42` | 无诊断 + 写回生效 |

## 7. VB 特性维度

| 维度 | VB 特性 | 用例来源 |
|---|---|---|
| V1 | `ReadOnlySpan(Of T)` 索引器读取 | S1/R1/G1 |
| V2 | `GetPinnableReference` | S2/R2 |
| V3 | For Each 枚举（ref struct enumerator `Current`） | S16/R5/G5 |
| V4 | `in` 实参 copy-out（虚/委托顺带解锁） | S18/S19 |
| V5 | With 块 | S12/S13/S13b/S13c/G4 |
| V6 | 成员链 | S14/S15 |
| V7 | 类型推断 `Dim x = s(0)` | S11/W10 |
| V8 | 表达式 lambda / draft-rewrite | S17/W11 |
| V9 | Option Strict On/Off 一致 | 抽 1 用例双模式断言（不分裂） |
| V10 | 复合赋值 / Mid | S7/S8/G6 |
| V11 | 可变 ref 返回不回归 | S21/G7/W12 |
| V12 | 错误码不复用纪律 | S22 |

## 8. 既有测试更新清单

| 文件:行号 | 现状 | 更新 | 原因 |
|---|---|---|---|
| `Compilers\VisualBasicSemanticTest\Semantics\ByRefLikeTests.vb:537-560`（S24） | **已翻转（F14 落地）**：`AssertNoDiagnostics` + `CompileAndVerify` 枚举 `a`/`b`；注释说明 modreq(In) 豁免 + For Each `Current` 谓词只查 `IsReadable` + RValue 值读 | 已完成，无需再改 | For Each 解锁（改 6，R6/RESOLUTION） |
| `InternalDevDocs\spec\spec-byref-like-safety.md:228-230` | Byref enumerators 条目「VB does not support properties that return by reference」 | 修订为「ref readonly 返回的 `Current` 已可消费，For Each over ref struct enumerator 可用」；修正「纯 ref 已可消费、带 modreq 的 ref readonly 曾不可现已可」的不精确表述 | 改 6 + R9 范围修正 |
| `Compilers\VisualBasicSemanticTest\Semantics\ByRefLikeTests.vb`（其他 ByRef 返回用例） | 现有 ByRef 返回消费用例 | 保留，**不修改**；新增 readonly 返回对照（S1-S23） | 纯 ref 语义不回归 |
| `Compilers\VisualBasicSymbolTest\SymbolDisplay\SymbolDisplayTests.vb` | `ByRef Like Structure` 断言（byref-like 任务） | 保留；新增 D1-D8 ref readonly 显示断言（含退化约束 D6、叠字 D7、词序 D8） | 改 7 |
| `Compilers\VisualBasic\Portable\VBResources.resx` + 13 xlf | **零改动** | 复用既有 `ERR_LValueRequired`(30068)（已有 resx/xlf 条目），**不新增错误码**、无 13 语言同步 | 改 3 |
| `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` | 现有 REPL 测试 | 新增用例（§4 R1-R8），**不修改**既有退出码断言 | 编译期短路不影响既有 REPL 行为 |

**实现期检查点（实测回填）**：① W7b/W8b（With/成员链 `.Member` 写入）**复会 R8（2026-09-01）定稿为 `ERR_LValueRequired`(30068) 拒绝**（与链式一致；值捕获仅服务读）——F13 原值捕获写路径已由复会修正为拒绝，W7b/S13b/S13c/S13e 翻转，S12/S13d/S13f 读取保持无诊断；【F13 原回填「W7b=值捕获（S13b/S13c/S13d 绿）」作废，以 R8 为准；W8b 保持拒绝（S15b/S15c 绿）】② W7a/W8a（对 readonly-lvalue 的直接赋值）必须`ERR_LValueRequired`(30068)，若漏网（写穿）即 P1 阻断；③ S20 一致性校验的实际落点（PE 解码 vs UseSiteInfo）；④ R2 `GetPinnableReference` 在 REPL 的显示形态；⑤ S18/S19 虚方法/委托 `in` 调用的实际绑定路径；⑥ `ERR_LValueRequired`(30068)编号定稿；⑦ **REPL/.vbx 顶层 byref-like `Dim` 受 BC31396（ref struct 字段规则，byref-like-safety 任务 REPL 语义）拒**，写路径用例须放方法体局部（§4 表 + F16 实测偏离记录已回填，R3/R4/R7/R8 为方法体局部形态）。发现与计划基线不符时如实记录差异并回填本计划（对齐 byref-like-safety 任务 test-plan 的实现期修订惯例）。

## 9. 无副作用纪律

- **L1/L4**：纯编译 API / `VisualBasicCompilation.Create` + `VerifyDiagnostics`，不跑宿主、不 emit 到磁盘（`CompileAndVerify` 已内存 `MemoryStream` 门控，不落盘、进程内运行）。
- **L2**：`CreateRunner(input:=...)` + `TestConsoleIO`（`CommandLineRunnerTests.vb:93-118`（`CreateRunner`）+ `Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb`（内存 Reader/Writer））——内存 `StringReader`/`StringWriter`；`.vbx` 文件写仅走既有 `CreateIsolatedTempDirectory` 装置（测试输出目录下的隔离临时目录）。无网络、无进程、无注册表。
- **L3**：`SymbolDisplay.ToDisplayString` 纯字符串。
- **引用程序集**：Span 走 `CreateCompilation(source, targetFramework:=TargetFramework.NetLatest)`；C# emit 测试库走 `CreateCSharpCompilation` + `EmitToImageReference`（内存）。全部经测试基建内置，非网络下载。
- **新增用例若写不了**：按项目约定，不写就测不了的部分在实现期向用户说明并询问（不静默跳过）。
