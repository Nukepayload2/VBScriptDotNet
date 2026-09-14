# 总体设计（script-top-level-crashes-2）

## 1. 这批崩溃长什么样

四个族（C1–C4）是同一句话的不同说法：**一个在类体里合法、且被顶层声明映射表放进脚本类的形状，落到合成提交类上时，某个断言/`Throw` 前提不成立，而那条路径没有任何诊断出口。**

- 前提不成立的原因有四种，但**都不是「用户写错了」**：
  - **C1** 构造器：脚本类的实例构造器被宿主独占（`GetScriptConstructor` 要求全类型**只有一个**实例构造器，且必须是合成的那个）。
  - **C2** 事件 raise：脚本类的非限定成员引用一律解析成**上一提交引用**而不是 `Me`，而降级代码断言接收者是 `Me`。
  - **C3** `Handles`：宿主类型枚举只列了 `Class`/`Module`，没列 `Submission`。
  - **C4** `MyBase`：提交类没有基类型，而错误路径要先取基类型才能构造错误节点。

第五族（C5）不同源：它不是「前提不成立」，而是**顶层语句拿不到方法体级检查**——第一轮已从同一个缺口（`<Initialize>` 的方法体是空壳）补出了 `BC36943` 与 `BC30369` 两条判据，本任务补第三条 `BC30101`。

## 2. 修法形状

| 族 | 形状 | 一个单元改几处 |
|---|---|---|
| C1 | **新诊断**：提交类里声明实例构造器 → 报错；普通类分支的行为一字不动 | 1 个新 ERRID + 注册链 + 判据落点 |
| C2 | **降级富化**：事件 raise 的接收者自己降级，不再假定它已是 `Me` | 1 处（`Else` 分支开头） |
| C3 | **枚举扩项**：`Submission` 并入 `Handles` 的合法宿主 | 1 处（`Select Case`） |
| C4 | **兜底补齐**：错误路径沿用 `Me` 错误路径已有的 `ErrorTypeSymbol.UnknownResultType` 写法 | 1 处（1 行） |
| C5 | **判据补齐**：顶层语句补跑「分支不得离开 `Finally`」这条判据 | 1 处判据落点 + 复用 `Binder_Initializers` 已有的逐语句补齐模式 |

**共同纪律**：修法只让**本族形状**的行为变化。C2/C3/C4 的目标是「合法形状照常工作」，C1/C5 的目标是「非法形状报出普通上下文里那条诊断」。四族的**普通编译上下文对照组必须零行为变化**。

## 3. 为什么 C1 判「报错」而不是「修好」

判定原则说「普通上下文里合法就该修好」。C1 走了例外分支，理由**不是**「实现难」，而是三条**实锤**的不变量：

1. `Symbols\NamedTypeSymbol.vb:697-700`：`GetScriptConstructor` = `DirectCast(InstanceConstructors.Single(), SynthesizedConstructorBase)`。让用户构造器参与成员表后该行抛 `InvalidCastException`（**已运行**：可行性补丁下 `vbi.exe` exit `2147500034`，栈顶即此行）。
2. `Symbols\Source\SynthesizedEntryPointSymbol.vb:337-338`：宿主的 `<Factory>` 体是 `Dim submission As New Submission#N(submissionArray) : Return submission.<Initialize>()`——**宿主构造提交类**，用户声明的构造器没有调用点。
3. 提交数组的状态恢复（`SynthesizedSubmissionConstructorSymbol.MakeSubmissionInitialization`）挂在合成构造器上；换成用户构造器，提交数组永不赋值 ⇒ 即便强行放行也只是把崩溃从编译期挪到运行期。

因此 C1 的产物是：**「脚本不支持这样用」的诊断**——判定原则给出的第二条出路。C# 侧同形（`SourceMemberContainerSymbol.cs:5655-5708` 在用户已声明构造器时**不**合成提交构造器）不构成反例：那条分支在 C# 里同样会让提交初始化不发生，属于同一未定义的角落，不能作为「C# 能跑」的证据（**已检查**，未运行 C#）。

## 4. 全称主张剪枝自检

对本文件夹四份文档的**全称主张**（无 / 都 / 任何 / 唯一 / 不可能 / 一律 / 全部）逐条挂证据；举不出证据的降级为推测或删除。

| 主张 | 状态 | 证据 |
|---|---|---|
| C5「脚本模式下顶层 `Try`/`Catch`/`Using`/`SyncLock`/循环/`Select`/`With`/`If` 的跳出**都**正常」 | **实锤** | `tmp\probes\run_probes4.py` 的 11 个块形状逐个实测，只有 `finally` 为 exit `2148734266`，其余 10 个 exit 0 |
| C1「提交类的实例构造器**唯一**且必须是合成的」 | **实锤** | ① `NamedTypeSymbol.vb:697-700` 的 `.Single()`；② 可行性补丁下实际抛 `InvalidCastException`（exit `2147500034`） |
| C2「脚本类的非限定成员引用**一律**解析成上一提交引用」 | **实锤（本族形状）** | 插桩实测：`RaiseEvent TopEvent` 的事件字段接收者 = `PreviousSubmissionReference`（该次运行的 `RAISEDBG` 输出）。**限定**：本主张只对**走了 `TryBindInteractiveReceiver` 的路径**成立（`Binder_Expressions.vb:2570-2576`）；顶层字段读写同样走这条路（同形未单独插桩，**推测**） |
| C3「`Submission` 落 `Throw UnexpectedValue`」 | **实锤** | `SourceMemberMethodSymbol.vb:782-783` 原文 + 探针栈顶逐字 `Unexpected value 'Submission' of type 'Microsoft.CodeAnalysis.TypeKind'` |
| C4「提交类 `BaseTypeNoUseSiteDiagnostics` 为 `Nothing`」 | **实锤** | 探针栈顶 `BoundMyBaseReference..ctor` 的 `Field 'type' cannot be null`；且错误路径的 `If(Me.ContainingType IsNot Nothing, …BaseTypeNoUseSiteDiagnostics, …)` 在 `ContainingType` 非空时只能取到 `Nothing` |
| C5「`BC30101` **唯一**报点在 `ControlFlowPass.VisitFinallyBlock`」 | **实锤** | 全仓 `ERR_BranchOutOfFinally` 命中：`Errors.vb:166`（定义）、`ErrorFacts.vb:138`（BuildOnly 清单）、`ControlFlowPass.vb:167`（唯一 `diagnostics.Add` 来源）、`Binder_Initializers.vb:292`（U5 收口点的过滤读取，不报点） |
| C1「普通类分支会复用用户已声明的构造器」 | **实锤** | `SourceMemberContainerTypeSymbol.vb:2796-2816` 的 `TryGetValue` + `ParameterCount = 0 ⇒ Return`；且 `TypeKind.Class` 走的是同一条 `EnsureCtor` |
| U6「13 份 xlf 各缺 7 条」 | **实锤** | 集合差自算（`resx data@name` − `xlf trans-unit@id`），**不采自报数** |
| 「C2/C3/C4 的普通上下文对照组零行为变化」 | **待定** | 改动只落在脚本类分支/错误路径/枚举扩项上，但**未经运行验证**；由每个单元的验证者以 vbc 对照探针 + 编译器三层测试确认 |
