# issue 35：并行执行时脚本提交绑定撞 `Binder_Conversions.vb:442` 断言

- **登记日期**：2026-09-24
- **状态**：**Open（上游来源，本 fork 不修；D7 例外 (b)）**。测试侧已按作者批准隔离两个受害类（§七），产品码不动——缺陷在 C#/VB 共享的上游代码里，本 fork 逐字未改（§下「来源定性」）。
- **症状**：两个测试类在并发编译下抛 `InvalidOperationException : argument.Type.IsSameTypeIgnoringAll(targetType)`（payload 见 §二）。受害者一：`ScriptTopLevelDefiniteAssignmentTests.CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning`；受害者二：`SubmissionSharedHandlesHookupTests`（全量 Emit 门 ≈17% 红，§一之二）。
- **触发条件**：进程内同时有两个编译共享同一份 `AssemblyMetadata`，且受害方走 `previous:=` 提交链（§三之二）。并发来源不必是自注入并发的测试——普通 xunit 类间并行即可（§六）。
- **根因**：`ReferenceManager.vb` 的「持锁查缓存 → 锁外建符号 → 持锁发布」三段不原子，并发下同一元数据产生两个 `PEAssemblySymbol`；`Binder_Conversions.vb:442` 的符号级 `IsSameTypeIgnoringAll` 随后否掉快路径判定的 `Identity`（§三之二）。
- **处置**：测试侧隔离（§七）＋ 产品不修（D7 例外 (b)）。上游修复后应删掉隔离集合、按 §七 保留的配方复跑确认不再需要。
- **来源定性**：对照 `{{Roslyn}}\src\...`（`release/stable @ 0e401fcf66c`）逐文件比对——

  | 文件 | 与上游 |
  |---|---|
  | `Compilers\VisualBasic\Portable\Symbols\ReferenceManager.vb` | **完全一致，0 差异**（59070 bytes／1028 行） |
  | `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs`（那把进程级 `static` 锁） | **完全一致** |
  | `Compilers\Core\Portable\InternalUtilities\WeakList.cs` | **完全一致** |
  | `Compilers\CSharp\Portable\Symbols\ReferenceManager.cs`（同构对照） | **完全一致** |
  | `Compilers\VisualBasic\Portable\Emit\NamedTypeSymbolAdapter.vb` | **完全一致** |

  ⇒ **缺陷所在的整条链（缓存非原子 ＋ 那把锁 ＋ `WeakList` ＋ C# 同构）全部是上游原样，本 fork 一行未改。**

- **判据**：`decisions.md` **D7 例外 (b)**——「缺陷在 C#/VB 共享的上游代码里且上游亦未修 ⇒ 不修，目标 `dotnet/roslyn`，改它只增合并冲突面」。仓内先例＝问题单 25。**外部提交动作**按 D7 的 2026-11-18 补记只做仓内记录（不向 `dotnet/roslyn` 提交）。
- **「并发共享元数据」是上游的预期用法，不是超纲**：上游为它专门设计了进程级符号缓存、跨编译的全局锁与一条显式契约（`CommonReferenceManager.State.cs:22-33` 逐字「**All the above data should be updated at once while holding this lock.**」）。那把 `static` 锁的存在本身就是证据——若并发不在预期内，根本不需要一把跨编译的锁。真实发生的事是：**上游实现违反了自己写下的契约**（契约要求一次持锁完成更新，实现却是「查缓存持锁 → 锁外建符号 → 再取锁发布」三段分离）。
- **这是 diff 事实，不是读码推断**：只读两侧代码形状只能得到「C# 侧逐行同构 ⇒ 单独修 VB 会形成分叉」这个**推断**；实际 diff 显示相关文件与上游**逐字相同**，所以**根本不存在"fork 的修改面"**。正确表述是「**这是上游缺陷；本 fork 按 D7 例外 (b) 不修**」。
- **前置**：由 `..\..\tmp\HANDOFF.md` §5 行 K（本机文档，原"未定性偶发红"）升级而来；升级理由＝**拿到了稳定复现配方与完整 payload**，不再是"重跑就好"。

## 一、复现配方（已运行，频次实测）

失败格：`Microsoft.CodeAnalysis.VisualBasic.UnitTests.ScriptTopLevelDefiniteAssignmentTests.CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning`

| 配置（`dotnet test --filter`） | 结果（亲跑） |
|---|---|
| 该类单独（7 格） | **6/6 全绿**（读数记于类内 6 格时；⚠ 与该类当前 7 格不符，未复跑） |
| 该类 ＋ `ScriptTopLevelDimInferenceTests`（14 格） | **2/4 红** |
| 该类 ＋ `ScriptTopLevelRecursiveDimInferenceTests`（35 格） | 1/4 红 |
| `~ScriptTopLevel`（46 格） | 3/4 红 |
| 全量 Semantic 门（当时基线 5914 格） | 本次 1/1 绿（**不构成处置**，见 §四） |

⇒ **判别量＝是否有另一个测试类与它并发**，不是格数、不是新增测试类。单类跑怎么跑都不红，加任何一个邻居就 25–50% 红。

### 一之二、**受害者 #2：Emit 门**

`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests`（**issue 36 新增的回归钉所在的类**）在**全量 Emit 门**下偶发同签名红，定向跑必绿。

| 配置 | 格数 | 跑次 | 红 |
|---|---|---|---|
| E1 仅该新类 | 18 | 6 | **0** |
| **E2 ＋ `SubmissionSharedInitializerTests`（pre-existing 类）** | 24 | 8 | **4（50%），4/4 全是这条断言** |
| E3 四个 pre-existing 提交类同跑 | 40 | 8 | 0 |
| 全量 Emit 门（当时基线 4388 格） | 4388 | 23 次**有效**跑 | **红 4 次（≈17%），4 次 payload 逐字相同** |

**payload 与 §二 同签名**（断言文本、`Binder.CreateConversionAndReportDiagnostic ... Binder_Conversions.vb:442` 栈帧、`Pool leak` 清单全部逐字相同）。触发栈（从 `SubmissionSharedHandlesHookupTests.vb:736` 的 `VerifyDiagnostics()` 进入，`RunSubmissionChain` 一次都没跑到）：

```
at Binder.CreateConversionAndReportDiagnostic ... Binder_Conversions.vb:line 442
at Binder.ApplyConversion ... :410  / ApplyImplicitConversion ... :314
at Binder.BindReturn ... Binder_Statements.vb:line 5171
at Binder.BindGlobalStatement ... Binder_Initializers.vb:line 234
at MethodCompiler.CompileNamedType ... :617
```

**"不是 issue 36 那批引入"的证据是行为证伪，不是推断**（三条变异，每条还原后 `-t:Rebuild`）：

| 变异 | 若"本批引入"为真应 | 实测 |
|---|---|---|
| 删掉**被测的那段** `Shared Sub H … Handles Me.Ev` | 红消失 | **8/8 仍红**，7/8 同一断言 |
| `Return Sink.Take()` → `Return CStr(1)`（去掉跨程序集调用） | 红消失 | **8/8 仍红**，3/8 同一断言 |
| ALC 改 `isCollectible:=True` | 红消失 | 5/8 红，payload 不变 |
| 去掉邻居类并发 | 红消失 | **0/6** ✔ |

**两条曾被怀疑的线索均已证伪（有读数）**：①"一条链里有两个同名 `Sink`"——`TraceSinkSource` 只拼在 link 0，两程序集简单名是**互不相同的 GUID**，逐字复刻 `RunSubmissionChain` 200 次 trace 恒等；②ALC 泄漏——托管堆稳定在 11 MB，且结构上失败发生在 ALC 创建**之前**。

**一条重要的负面读数**：宿主级独立 exe（16 线程 × 300 轮 × 4 组）**复现失败，0 命中** ⇒ §四.2 计划里的"缩到单条源串 ＋ 双线程宿主级复现"**这条路走不通**；下一片必须先**二分出是哪个邻居用例在弄脏共享状态**，再谈宿主复现。

**处置口径（登记时的判断）**：本条**不许**用测试侧手段消红——`..\..\tmp\HANDOFF.md` §4.5 记的串行集合只在本仓知道"全部写方"时成立，而这里的邻居是 4388 用例中任意一个；把 `DisableParallelization` 加到整门等于掩盖缺陷，按 D8 不许。真正修法必落产品侧。该口径约束的是「整门串行」；只隔离受害类并给构造性论证属另一回事，见 §六 候选 1 与 §七。

> **根因假设（推测，未证实）**：跨编译共享的 `PENamedTypeSymbol` 惰性初始化被另一线程观察到半初始化态。证实法：在 `:442` 前临时打印 `argument.Type`/`targetType` 的 `SymbolEqualityComparer.Default.GetHashCode` 及各自 `IsSameTypeIgnoringAll` 的自反性，用 E2 配方跑 20 次，看是否出现"同符号与自己不相等"。**需动产品码，未做。**

## 二、Payload（逐字，`--logger "console;verbosity=detailed"`）

```
System.AggregateException : One or more errors occurred. (argument.Type.IsSameTypeIgnoringAll(targetType)) (Pool leak detected! ... )
---- System.InvalidOperationException : argument.Type.IsSameTypeIgnoringAll(targetType)
---- Pool leak detected! The following pooled objects were not returned:
     Microsoft.CodeAnalysis.VisualBasic.BindingDiagnosticBag (from BindingDiagnosticBag.vb:14) (allocated at BindingDiagnosticBag.vb:44): 3
     Microsoft.CodeAnalysis.DiagnosticBag (from DiagnosticBag.cs:340) (allocated at DiagnosticBag.cs:327): 1
     ArrayBuilder<BoundExpression> (allocated at Binder_Invocation.vb:2689): 1
     ArrayBuilder<Int32>          (allocated at Binder_Invocation.vb:2688): 1
     ArrayBuilder<BoundInitializer>(allocated at Binder_Initializers.vb:117): 1
```

**读法（实锤部分）**：断言是第一因，池泄漏是它**异常展开时跳过 `Free()`/`ToImmutableAndFree()` 的下游后果**——`Binder_Initializers.vb:117` 是 `BindFieldAndPropertyInitializers` 里 `boundInitializers = ArrayBuilder(Of BoundInitializer).GetInstance()` 那一行（**上游原码，非本批新增**），`Binder_Invocation.vb:2688/2689` 同理 ⇒ 泄漏清单只说明"异常从这条绑定路径里抛出"，**不**说明这几处漏了释放。

## 三、归因（已到什么程度）

- **不是 issue 33 引入的**：issue 33 的 9 行不在场时同样出现该红（RD-F02 在其"改前基线"跑里测到 1 次红；且 `pairNew` 与 `pairOld` 的差别只是邻居是谁）。
- **不是 issue 32 的推断改动引入的**（**推测**：弱证据）：失败格的形状是 `Dim s As String`（**带显式 `As`**），按 `SourceMemberFieldSymbol.vb:135` 的 `AsClause IsNot Nothing → Return Nothing` 根本不进推断路径；这只排除"推断该字段"这条通路，没排除"推断路径新增的额外绑定"对共享状态的影响。
- **落点在已提交代码**：`Binder_Initializers.vb` 与 `ScriptTopLevelDefiniteAssignmentTests.vb` 自 `2173a56`（2026-09-24 21:49）起已在 HEAD，本条与"未提交工作树"无关——它现在是一条**独立缺陷**，不是本批 diff 的收尾项。

### 三之二、根因已定位

**共享可变状态＝进程级、按 `AssemblyMetadata` 缓存的 `PEAssemblySymbol` 图。** 测试侧两个引用对象是**进程级 static**，每个提交编译都拿到同一份元数据。

**缺陷点＝"查缓存 / 建符号 / 发布缓存"三步不原子**（`Compilers\VisualBasic\Portable\Symbols\ReferenceManager.vb`，逐行读码确认）：

| 步骤 | 位置 | 是否持锁 |
|---|---|---|
| 查缓存、取已有 `PEAssemblySymbol` | `:871-879`，注释逐字「accessing cached symbols requires a lock」 | ✅ `SyncLock SymbolCacheAndReferenceManagerStateGuard`，`:879` 放锁 |
| `If AssemblySymbol Is Nothing` 就**新建** | `:373-375` | ❌ **在两个锁区间之外** |
| 重新入锁并**发布**进缓存 | `:423-432`（`UpdateSymbolCacheNoLock`） | ✅ |

⇒ 两个编译各自"读到缓存空"、**各建一个 `PEAssemblySymbol`**、各发布 ⇒ 进程里同一份元数据存在两个符号对象。

**为什么恰好炸在这句断言**（读码确认）：快路径 `ConversionEasyOut`（`Binder_Conversions.vb:516`）只按 `SpecialType` 查表就判成 `Identity`，随后 `:442` 的符号级 `IsSameTypeIgnoringAll`（`InstanceTypeSymbol.vb:135`）否掉——**炸的条件是"两个不同符号对象、`SpecialType` 都是 `System_String`"**。

**决定性实验**：把符号复用整个关掉（⇒ 必然各建一个）⇒ 24 格从 4/8 偶发变成 **8/8 必现、每次固定 4 条**。⇒ 一旦两个符号共存，失败就**变必现**；并发只是制造共存的机会。

**触发面不是"哪条用例"，是"并发的编译工作量"**（实测，各 8 次）：6 条 `SubmissionSharedInitializerTests` 逐条当邻居 = 4/4/4/4/5/3 红；**完全无关的既有类** `SubmissionEventMemberTests` 3/8、`SubmissionTopLevelLabelTests` 6/8 红；而"一条 hooks 受害者 ＋ 一条 initializer"＝ **0/8 红**（缩到最小反而绿）。⇒ **没有必需的邻居用例**，所以 §四"串行集合隔离"那条路**不适用**。

**"什么时候炸"也澄清了**：

- **独立提交之间永不相遇**——一堆并发跑独立提交的格子**没有鉴别力**（实测 ORIGINAL 全绿）。**重复符号本身不炸，炸的是"两个符号在同一个编译里相遇"。**
- 相遇的形状＝**两段提交链**（`previous:=`）：后一段**通过前一段的编译**解析 `Handles Me.Ev`（连带 `System.EventHandler`），**却用自己的 body**——正是 `SharedHookupAcrossSubmissions_*` 的形状。ORIGINAL 上该格 **4/4 红**，16 个线程**全部**抛 `InvalidOperationException`。
- 插桩实测 ORIGINAL 上每波 16 个线程对**同一个** `AssemblyMetadata` 各自 CREATE ＋ PUBLISH 一个符号，**16/16 命中**，而测试仍绿。⇒ 与 §三之二那条决定性实验（关掉复用 ⇒ 4/8 偶发变 8/8 **必现**）互为印证。
- **守门格的两条前提缺一不可**：**自建缓存必空的 `AssemblyMetadata`**（否则窗口早被前面的编译关上）＋ **专用 `Thread` 由闸门齐放**（`Task.Run` 版在 ORIGINAL 上实测 **0/6 全绿**）。

**同源、尚未修的两条确定性红**（用"全新 `MetadataReference` ＋ 16 线程"撞到，**确定复现**）：① `WeakList.cs:157/27` 的 `Add` 断言（`WeakList` 被并发改写，且其**枚举器走完一轮会改写列表**）；② `TypeSymbolExtensions.vb:999` `CheckTypeArguments` 抛 `ArgumentException`（经 `SynthesizedInteractiveInitializerMethod.vb:171`）。二者与本条同根——**同进程多编译共用同一份元数据**——**不得只修本条断言而把这两条留着**；已分别立册为 issue 37／38，**两者均已在候选 R2 下复测**：#37 **不复现**（已结案＝不是独立缺陷）；#38 **仍复现**（且在**原码**上 6/6，且栈经 `SynthesizedInteractiveInitializerMethod` ⇒ **必须走提交路径＋并发**）。两者与本条**同源且同为上游来源**，见本文件开头的来源定性与 `issue-check-type-arguments-concurrent.md`。

**修复途中的三条结论**：

**① 候选 R 制造了新红。** R（只把「查/建/发布」并入同一次 `SyncLock`、把发布搬进创建点）虽在原配方上有效且 DupTrace 证明它**确实消灭了重复符号**，却**自己引入了缺陷**——它让**初始化留在锁外**而发布进了锁内，锁内因此出现"**已发布但尚未 `SetReferences`**"的 `PEAssemblySymbol`，别的编译进得来就捡到它，`ReuseAssemblySymbols`（`CommonReferenceManager.Binding.cs:820`）读其 bound references 时炸（`NonMissingModuleSymbol.vb:136`）。⇒ **"R 下换了一种红"是 R 自己引入的**，**不要**归到 `WeakList`／issue 37 上。

**⚠ 承重的是"临界区的跨度"，不是"发布排在初始化之后"**：变异 **M-B**（发布前移到创建点、**但初始化与发布仍在同一把锁内**）守门格 **0/4 全绿**，与最终方案 R2 等价。⇒ **真正必需的是「从重读缓存到发布的整段都在同一次持锁内」**；R2 仍把发布留在 `UpdateSymbolCacheNoLock` 只是**可读性／最小惊讶**的取舍。**收窄锁区间（M-C＝整份 R）才变红。**

**② 候选 R2 曾有 Symbol 门回归 —— 已随 R2 一并撤回**（七门实测；该 28 条是**撤回前**的读数，R2 现已不在树上，故不再是活的缺陷）：Symbol 门 3407 格里 **28 条失败**（连跑四次 26/28/30/28），三簇：
- **7 条** `Debug.Assert allAssemblyData(i).IsLinked = bindingResult(i).AssemblySymbol.IsLinked`（`ReferenceManager.vb:429`）——R2 **采纳**缓存符号时**未校验 `IsLinked` 兼容性**；原代码只在**新建**分支进那一行，故不会踩。
- **6 条** `Assert.NotSame() Failure: Values are the same instance`（`NoPia.LocalTypeSubstitution*`）——测试**要求不同实例**，修复让跨编译共享 ⇒ **共享范围过宽**。
- **7 条** `UsedAssembliesTests` 引用类型／顺序不符 ＋ 5 条 `AggregateException` ＋ 3 条零散。

⇒ **L2 777/0 未变**，故障在编译器符号层。**教训**：四条配方全绿 ＋ 变异可证**只覆盖了脚本／提交形状，没覆盖普通编译的符号身份语义**。**这也是为什么「普通类/模块属性位不变」那类对照格不够用**（issue 36 记的只是属性位）——**符号身份/实例同一性**同样要被对照。

**③ 本条能打死整个测试门，不只是让一格红（严重度上调）**

临时启用守门格 `ConcurrentSubmissionsOverFreshMetadataReferenceTests` 跑全量 Emit 门时（一次性实验）：`Binder.CreateConversionAndReportDiagnostic` 的 `Debug.Fail` 变成**未捕获异常**打死 **test host 进程**，`dotnet test` 报「**活动的测试运行已中止**」，并给出**残缺计数**——同一次现象在不同运行里分别报出 **262** 与 **2361**（远小于当时的 4388 格），**看起来像丢了上千个用例**，实则是运行在中途崩掉。

⇒ 三点后果：① **本条不是"偶发红一格"，而是能中止整个门运行**；② 门基线数字会被这种中止污染，报数前**必须先看有没有"中止"**；③ 那三格复现／压力测试**已删除**（它们测的是产品不产生的并发条件）；若将来重建这类并发用例，**第一个撞上的是这个中止，不是 28 条红**。

**④ 真实使用能不能碰到？（读码 ＋ 既有复现读数）**

**用户路径：基本碰不到。** 三条依据：

1. **脚本宿主没有任何并发原语**——`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` 零命中；`Scripting\Core\Script.cs` 只有 `Interlocked.CompareExchange` 做**惰性一次性初始化**（`_lazyCompilation`／`_lazyExecutor`），**不是并行编译**。⇒ 单个 `vbi` 会话内提交严格顺序，不存在"两个编译同时绑同一份元数据"。
2. **最强反面证据**：用**真实链形状**（`CreateScriptCompilation`、共享 Net461 引用、event 与 sem 两种形状）8 线程 × 40 轮 ⇒ **0 命中**；改用"每次编译新建引用"同样 **0 命中**。
3. **能复现的最强配方依赖测试专用 API**（该测试文件**现已删除**）：它曾调 `AssemblyMetadata.CopyWithoutSharingCachedSymbols()`，其**唯一作用**就是强行造出"符号缓存为空的首个写入者"条件。**任何生产宿主都不会调它**——宿主共享同一份 `MetadataReference`，缓存是热的。

**但有两类进程确实会碰到：**

- **测试进程 —— 每天都在发生**：`SubmissionSharedHandlesHookupTests` 走 `CreateSubmission`、用**进程级共享引用**、**没有**那个测试专用 API，在整门并行下 ≈17% 红，并能**中止整个门运行**（见 ④）。这是本条目前唯一确定会发生的现实代价。
- **工具进程 —— 未查实（未知项）**：外部 IDE／语言服务（`vb-ls` 之类用本仓 `Microsoft.CodeAnalysis.VisualBasic.dll`）在一个进程里并发编译并共享 `MetadataReference` 时是否命中，**未查**。本仓 `Compilers\Core\MSBuildTask\Vbc.cs` 走**命令行拼装**（`commandLine.AppendWhenTrue("/novbruntimeref", …)`）、**不构造 `MetadataReference`**；`/m` 并行构建时跨项目是否共享同一 `AssemblyMetadata` 实例，取决于 `vbc` 命令行引用解析侧的缓存，**未追**。

⇒ **优先级含义**：若确认无任何用户路径命中，本条性质更接近「**测试装置缺陷**」而非「产品并发缺陷」，代价就只剩"门不稳定"。

**✅ 该判断已闭合（作者裁决）**：既然产品路径不并发，**正确处置是让测试也不并发**，而不是为了一个不可达的场景去改产品码。已**删除**三个注入并发的测试文件：

| 已删除 | 作用 |
|---|---|
| `ConcurrentSubmissionsOverFreshMetadataReferenceTests.vb` | 本条的确定性复现格（16 闸门线程 ＋ `CopyWithoutSharingCachedSymbols()` 冷缓存）——**它测的就是产品从不产生的条件** |
| `ConcurrentSubmissionLoadOverSharedReferencesTests.vb` | 压力格（60 波 × 16 线程）——同理 |
| `ConcurrentSubmissionsOverOneMetadataReferenceTests.vb` | 上述复现格的普通编译对照，随之无意义 |

备份在 `..\..\tmp\backup\r2\*.deleted`。**`SubmissionSharedHandlesHookupTests` 保留**——它**零线程原语**、顺序执行，测的是真实形状。删除后 Emit 门基线仍为 **4388**（当时基线；那几格本就禁用、未计入）。

⚠ **留一句给将来**：根因层面的非原子性**依然客观存在**（读码核实）。今天不修，是因为**产品不并发**；若将来产品引入并发（多线程宿主、IDE 后台分析与前台构建并行），它会**重新变成真缺陷**，届时的正确修法是把「查／建／发布」并入同一次持锁（C# 侧同形，参见 ⑤）。

**⑤ C# 侧是同一个缺陷，不是"类似缺陷"**

C# 与 VB 的 `ReferenceManager` 是**两份独立文件、同一套逻辑**：

| 步骤 | C# `Compilers\CSharp\Portable\Symbols\ReferenceManager.cs` | VB `Compilers\VisualBasic\Portable\Symbols\ReferenceManager.vb` |
|---|---|---|
| 查缓存（持锁） | `:1017 lock` → `:1019 foreach` | `:871 SyncLock` → `:873 For Each` |
| **建符号（已放锁）** | `:428-430`（在 `:311` 锁区间**外**） | `:373-375`（在 `:265` 锁区间外） |
| `SetupReferencesForSourceAssembly` | `:464` | 对应处 |
| `InitializeNewSymbols` | `:481` | `:469` |
| **再入锁 + 发布** | `:486 lock` → `:497 UpdateSymbolCacheNoLock` | `:462` → `:474` |

连注释都是同一句「accessing cached symbols requires a lock」，连方法名都叫 `UpdateSymbolCacheNoLock`。⇒ **C# 侧不是"处理了这个问题"，它就是同一个缺陷。**

**行为实验在本 fork 内无法对称进行（结构性事实）**：`Compilers\CSharp\Portable\Scripting\` **不存在**（本 fork 裁掉了 C# 脚本层，也没有 csi）⇒ **C# 侧没有与本条那条失败测试同构的宿主路径**可走。用编译器 API 单独跑的 C# 探针（`tmp\cs-side-probe\`）0 复现，但**其 VB 阳性对照未能命中目标签名**（harness 自身有 `typeArguments` 缺陷）⇒ **该 0 作废，不得引用为"C# 无此缺陷"的证据**。

**处置建议（登记时）＝reserve**：单独修 VB 侧＝对上游 C# 形成分叉，而 `decisions.md` D5 要求「**为什么 VB 必须分叉**」——当时能给的唯一理由是「本 fork 只发布 VB 脚本、C# 侧是死代码」，那是**产品范围**理由、不是**技术**理由，故本条不交付。该建议的落点是产品不修 ＋ 测试侧隔离（§七）。

**怎么了结（若要推进）**：① 恢复上游一致性——**两侧一起改**（C# 侧同样把整段并入一次持锁），再回归两个语言的普通编译对照；或 ② 取得技术性理由（例如证明 C# 侧该路径在本 fork 内不可达且未来也不会用于产品）并写进 D5；或 ③ 放弃本条、按"上游共享缺陷"登记后交由 `dotnet/roslyn` 上游处理，**fork 不单边修**（该方向已由 2026-11-18 裁决定为不外部提交，见 `decisions.md` D7 补记）。

**未闭合**：**已无"第二条创建路径"这一项**——插桩对 `CachedSymbols` 的每一次写入计数后确认：无修复时同一 PE 产生多个符号（`dup-fresh` 16 组／`dup-chain` 8 组），带修复为 **0 组**，且无"建了未发布"的孤儿。候选修法 R 因自身引入的缺陷已撤回、R2 因普通编译回归已撤回（详见 §三之二 ①②）；产品不修、留档（§七 测试侧隔离）。仍未闭合的是 **issue 37**（`WeakList` 并发改写，R2 下未复现，归因已动摇）与 **issue 38**（`CheckTypeArguments`，需 `Net461` 自建 CoreLib 才能坐实），两者**都不属本条**，见各自 issue。

## 四、处置要求（本仓对偶发红的既有口径）

1. **不许拿"重跑就好"当结论**；要给的结论只有两种：与相关改动无关（附证据），或由某处引入（附归因链）。
2. 最小化：并发触发可缩到**两个类的最小组合**并固定复现（`ScriptTopLevelDefiniteAssignmentTests` ＋ 单一邻居；另一受害者另有 `SubmissionSharedHandlesHookupTests` ＋ `SubmissionSharedInitializerTests` ＝ 24 格 / 50% 红）。「缩到单条源串 ＋ 宿主级双线程」这条路已被实测否掉（独立 exe、16 线程 × 300 轮 × 4 组，**0 命中**；且单独测过"共享引用 vs 每次新建 `MetadataReference`"这个变量，两者都不复现）。⇒ 最小化的判别量**不是单条源串，是"并发的编译工作量"**；宿主级复现这条路**放弃**，改走"定位共享状态 → 消除它"。
3. 取证顺序：`Binder_Conversions.vb:442` 所在分支的前置条件、断言触发机制与落点均已定位（§三之二）。修法落在 `ReferenceManager.vb` 的缓存发布原子性上，**不是** `Binder_Conversions.vb:442`；那句断言**本身不许删改或降级**——它是发现问题的东西。
4. 若最终判装置缺陷：按 §4.5 的先例用**串行集合**隔离（`CollectionDefinition(..., DisableParallelization:=True)`），并给出"为什么不可能再重叠"的构造性论证，而不是靠连跑碰运气。

## 五、影响面

产品侧：任何并发执行脚本提交的宿主（IDE 后台分析 + 运行、或两个线程各自跑提交链）理论上都可能命中——**推测**，未实测，实测方法见 §四.2。测试侧：Semantic 门的稳定性；全量门当时绿，但按 §一 的配方在 46 格规模下 3/4 红 ⇒ 门的可靠性受影响。

## 六、普通并行下的复现（复核记录）

**复核时点：2026-11-18。** **触发条件比原记录更宽**：原状态里的「只在测试自己注入并发时才复现」已被否证——**不需要任何注入并发的测试**，普通 xunit **类间并行**就够。

实测（Debug，串行执行、一次只跑一个测试进程）：

| 跑法 | 结果 |
|---|---|
| Emit 整门默认并行 × 7 | 0/7 红（4389/4286/103/0） |
| Semantic 整门默认并行 × 7 | 0/7 红（5919/5815/104/0） |
| 整门关并行（临时 runsettings，耗时 2.3×/3.2×） | 0/2、0/2 |
| 受害者类单跑 `--filter` × 5 | 0/5（18/18、7/7） |
| **受害者类 + 一个普通邻居类（并行）** | **Emit 7/8 红（87.5%）、Semantic 2/6 红（33%）** |
| 同组合关并行 × 8 | 0/8 |

⇒ 判别量是**「是否有另一个测试类在本进程并发编译」**，与邻居身份、用例数无关；payload 与 §二 逐字一致。同日 3 次七门全量有 2 次各红 1 格（命中率随负载波动，机制未消失）。

**机制确认（与 §三之二 一致）**：断言是第一因；`Pool leak` 是断言异常展开跳过 `Free()`/`ToImmutableAndFree()` 的下游副产物（清单全是 bag/builder，绿色运行 0 泄漏）。根因仍是 `ReferenceManager` 的「持锁查缓存 → 锁外建符号 → 持锁发布」三段不原子，并发下产生「同一元数据两个 `PEAssemblySymbol`」；受害者必须是 `previous:=` 提交链。

**影响面（量化）**：门脚本 `scripts\verify-vb-compiler-tests.ps1` 用精确四元组、无重试、fail-fast——Semantic 红会跳过 IOperation/Emit/CommandLine，Emit 红会跳过 CommandLine。按本次观测约「每 1.5 次七门全量红一次」。

**处置候选**：

1. **受害类串行集合**（`CollectionDefinition(..., DisableParallelization:=True)` 只罩住两个受害类）：构造性论证＝该集合不与任何其它集合并行，故「另一个类并发编译」这个条件在结构上不可能出现，与邻居是谁无关；代价＝这两类的用例失去并行、整门略慢。此路径正是 §四.4 允许的先例，但与 §一之二「处置口径」的「不许测试侧手段消红」字面冲突——那一条针对的是「把 `DisableParallelization` 加到整门掩盖缺陷」，与「只隔离受害者、给构造性论证」不是同一件事；作者批准后已落地（§七）。
2. **门级重试**：不推荐——会掩盖任何门红，违反 §四.1。
3. **不修 + 文档写明命中率**：代价≈每 1.5 次全量红一次，CI 随机红。

**产品侧结论不变**：仍按 D7 例外 (b) 不单边修（上游逐字相同）；本条保持 **OPEN**，不是已修。

证据：`..\..\tmp\vortex-logs\embeddable-scripting-host\30-flake-investigation.md`、`30-flake-*.trx`、`30-*-summary.txt`。

## 七、处置落地：受害类串行集合

**时点：2026-11-18。**

作者批准后，按 §六 候选 1 落地：两个受害者类分别进入各自测试工程内的 `DisableParallelization` 串行集合，其它测试保持默认并行。

| 工程 | 集合定义 | 受害类 |
|---|---|---|
| `Compilers\VisualBasicEmitTest` | `SerialScriptSubmissionBinding` | `SubmissionSharedHandlesHookupTests` |
| `Compilers\VisualBasicSemanticTest` | `SerialScriptSubmissionBinding` | `ScriptTopLevelDefiniteAssignmentTests` |

**构造性论证**：xunit 对 `DisableParallelization:=True` 的 collection 不与任何其它 collection 并行 ⇒「另一个测试类在本进程内并发编译」这一条件在结构上不成立，与邻居是哪一类无关；根因（`ReferenceManager` 的「查缓存 → 建符号 → 发布」三段不原子）仍在，只是测试装置不再制造它唯一需要的并发窗口。

- **产品侧不修**：仍按 D7 例外 (b)（相关文件与上游逐字相同；外部提交按 D7 的 2026-11-18 补记只做仓内记录），本条**保持 OPEN**，不是已修。
- **验证（2026-11-18，Debug，串行执行，一次只跑一个测试进程）**：确定性配方（受害类 ＋ 邻居类并行）各 8 轮 0 红（Emit 24/24 ×8，Semantic 14/14 ×8；隔离前同配方为 7/8、2/6 红）；Emit 门 5 轮 `4389/4286/103/0`、Semantic 门 5 轮 `5919/5815/104/0`，退出码全 0；Phase2 143/143/0/0。整门墙钟与隔离前同量级（Emit 中位 51.4s vs 对照 56.2s；Semantic 34.1s vs 33.6s）。
- **保留用于回归**：确定性配方、命令与 trx 留在 `..\..\tmp\vortex-logs\embeddable-scripting-host\31-*`；将来上游修复 `ReferenceManager` 后，应先删掉这两个集合、再按该配方复跑，确认不再需要隔离。
