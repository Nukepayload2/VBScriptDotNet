# issue 35：并行执行时脚本提交绑定撞 `Binder_Conversions.vb:442` 断言（**两个受害者**：`ScriptTopLevelDefiniteAssignmentTests` 一格 25–50%，`SubmissionSharedHandlesHookupTests` 在全量 Emit 门 ≈17%）

- **登记日期**：2026-09-24（main）；**2026-09-28 追加第二个受害者**（由 issue 36 的收口诊断顺带发现，见 §一之二）
- **状态**：**🔒 RESERVE（2026-10-02 作者裁决：暂不交付）**。根因**已定位并经读码核实**（`ReferenceManager.vb` 缓存的「查／建／发布」三步不原子）；候选修复 **R2 已写完、其目标形状上验证有效**，但**已从工作树撤回**（备份 `tmp\backup\r2\ReferenceManager.vb.{orig,R,R2}`）。**配套的 6 条回归用例已禁用**（三个测试文件保留但 `[Fact]` 全部注释掉，文件头有 RESERVE 横幅）。**RESERVE 的理由**（见 §三之二）：**C# 侧是同一个缺陷**、逐行同构 ⇒ 单独修 VB ＝ 对上游形成分叉，而 D5 要求给出技术性理由；目前只有"本 fork 只发布 VB 脚本"这个**产品范围**理由。且 R2 在普通编译上另有回归（Symbol 门 28 条）。**解除 RESERVE 的三条路径见 §三之二末。**
- **性质**：**合法输入触发编译器内部断言**（Debug 构建下 `Debug.Assert` ⇒ `InvalidOperationException`）——按 `decisions.md` D7 的"合法输入崩编译器即必修"这条，属必修面，不因"只在测试并行下出现"而降级。**注**：RESERVE 是**交付节奏**决定，**不改变"这是必修缺陷"的定性**——本条不得因为 RESERVE 就从待办里划掉。
- **前置**：由 `HANDOFF.md` §5 行 K（原"未定性偶发红"）升级而来；升级理由＝**拿到了稳定复现配方与完整 payload**，不再是"重跑就好"

## 一、复现配方（已运行，频次实测）

失败格：`Microsoft.CodeAnalysis.VisualBasic.UnitTests.ScriptTopLevelDefiniteAssignmentTests.CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning`

| 配置（`dotnet test --filter`） | 结果（main 亲跑） |
|---|---|
| 该类单独（7 格） | **6/6 全绿** |
| 该类 ＋ `ScriptTopLevelDimInferenceTests`（14 格） | **2/4 红** |
| 该类 ＋ `ScriptTopLevelRecursiveDimInferenceTests`（35 格） | 1/4 红 |
| `~ScriptTopLevel`（46 格） | 3/4 红 |
| 全量 Semantic 门（5914 格） | 本次 1/1 绿（**不构成处置**，见 §四） |

⇒ **判别量＝是否有另一个测试类与它并发**，不是格数、不是我的新类。单类跑怎么跑都不红，加任何一个邻居就 25–50% 红。

### 一之二、**受害者 #2：Emit 门**（2026-09-28 由 issue 36 的收口诊断顺带发现）

`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests`（**issue 36 新增的回归钉所在的类**）在**全量 Emit 门**下偶发同签名红，定向跑必绿。

| 配置 | 格数 | 跑次 | 红 |
|---|---|---|---|
| E1 仅该新类 | 18 | 6 | **0** |
| **E2 ＋ `SubmissionSharedInitializerTests`（pre-existing 类）** | 24 | 8 | **4（50%），4/4 全是这条断言** |
| E3 四个 pre-existing 提交类同跑 | 40 | 8 | 0 |
| 全量 Emit 门（4388 格） | 4388 | 23 次**有效**跑 | **红 4 次（≈17%），4 次 payload 逐字相同** |

**payload 与 §二 同签名**（断言文本、`Binder.CreateConversionAndReportDiagnostic ... Binder_Conversions.vb:442` 栈帧、`Pool leak` 清单全部逐字相同）。触发栈（从 `:736` 的 `VerifyDiagnostics()` 进入，`RunSubmissionChain` 一次都没跑到）：

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

**处置口径**：本条**不许**用测试侧手段消红——`HANDOFF.md` §4.5 的串行集合只在本仓知道"全部写方"时成立，而这里的邻居是 4388 用例中任意一个；把 `DisableParallelization` 加到整门等于掩盖缺陷，按 D8 不许。真正修法必落产品侧 `Binder_Conversions.vb:442` 附近。

> **根因假设【猜的，未证实】**：跨编译共享的 `PENamedTypeSymbol` 惰性初始化被另一线程观察到半初始化态。证实法：在 `:442` 前临时打印 `argument.Type`/`targetType` 的 `SymbolEqualityComparer.Default.GetHashCode` 及各自 `IsSameTypeIgnoringAll` 的自反性，用 E2 配方跑 20 次，看是否出现"同符号与自己不相等"。**需动产品码，未做。**

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
- **不是 issue 32 的推断改动引入的**（◇ 弱证据）：失败格的形状是 `Dim s As String`（**带显式 `As`**），按 `SourceMemberFieldSymbol.vb:135` 的 `AsClause IsNot Nothing → Return Nothing` 根本不进推断路径；这只排除"推断该字段"这条通路，没排除"推断路径新增的额外绑定"对共享状态的影响。
- **落点在已提交代码**：`Binder_Initializers.vb` 与 `ScriptTopLevelDefiniteAssignmentTests.vb` 自 `2173a56`（2026-09-24 21:49）起已在 HEAD，本条与"未提交工作树"无关——它现在是一条**独立缺陷**，不是本批 diff 的收尾项。

### 三之二、根因已定位（2026-09-28，读码确认 ＋ 决定性实验）

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

**"什么时候炸"也澄清了（2026-10-02，DupTrace 机制级证据）**：

- **独立提交之间永不相遇**——一堆并发跑独立提交的格子**没有鉴别力**（实测 ORIGINAL 全绿）。**重复符号本身不炸，炸的是"两个符号在同一个编译里相遇"。**
- 相遇的形状＝**两段提交链**（`previous:=`）：后一段**通过前一段的编译**解析 `Handles Me.Ev`（连带 `System.EventHandler`），**却用自己的 body**——正是 `SharedHookupAcrossSubmissions_*` 的形状。ORIGINAL 上该格 **4/4 红**，16 个线程**全部**抛 `InvalidOperationException`。
- 插桩实测 ORIGINAL 上每波 16 个线程对**同一个** `AssemblyMetadata` 各自 CREATE ＋ PUBLISH 一个符号，**16/16 命中**，而测试仍绿。⇒ 与 §三之二那条决定性实验（关掉复用 ⇒ 4/8 偶发变 8/8 **必现**）互为印证。
- **守门格的两条前提缺一不可**：**自建缓存必空的 `AssemblyMetadata`**（否则窗口早被前面的编译关上）＋ **专用 `Thread` 由闸门齐放**（`Task.Run` 版在 ORIGINAL 上实测 **0/6 全绿**）。

**同源、尚未修的两条确定性红**（用"全新 `MetadataReference` ＋ 16 线程"撞到，**确定复现**）：① `WeakList.cs:157/27` 的 `Add` 断言（`WeakList` 被并发改写，且其**枚举器走完一轮会改写列表**）；② `TypeSymbolExtensions.vb:999` `CheckTypeArguments` 抛 `ArgumentException`（经 `SynthesizedInteractiveInitializerMethod.vb:171`）。二者与本条同根——**同进程多编译共用同一份元数据**——**不得只修本条断言而把这两条留着**；已分别立册为 issue 37／38，且**二者在候选 R2 下是否仍红尚未测**，见 `..\..\tmp\vortex-logs\parallel-submission-binding-assert\`。

**修复途中的三条结论（2026-10-02）**：

**① 候选 R 制造了新红。** R（只把「查/建/发布」并入同一次 `SyncLock`、把发布搬进创建点）虽在原配方上有效且 DupTrace 证明它**确实消灭了重复符号**，却**自己引入了缺陷**——它让**初始化留在锁外**而发布进了锁内，锁内因此出现"**已发布但尚未 `SetReferences`**"的 `PEAssemblySymbol`，别的编译进得来就捡到它，`ReuseAssemblySymbols`（`CommonReferenceManager.Binding.cs:820`）读其 bound references 时炸（`NonMissingModuleSymbol.vb:136`）。⇒ **"R 下换了一种红"是 R 自己引入的**，**不要**归到 `WeakList`／issue 37 上。

**⚠ 承重的是"临界区的跨度"，不是"发布排在初始化之后"**：变异 **M-B**（发布前移到创建点、**但初始化与发布仍在同一把锁内**）守门格 **0/4 全绿**，与最终方案 R2 等价。⇒ **真正必需的是「从重读缓存到发布的整段都在同一次持锁内」**；R2 仍把发布留在 `UpdateSymbolCacheNoLock` 只是**可读性／最小惊讶**的取舍。**收窄锁区间（M-C＝整份 R）才变红。**

**② 候选 R2 仍有 Symbol 门回归，⚠ 本条尚未收口**（七门实测 2026-10-02）：Symbol 门 3407 格里 **28 条失败**（连跑四次 26/28/30/28），三簇：
- **7 条** `Debug.Assert allAssemblyData(i).IsLinked = bindingResult(i).AssemblySymbol.IsLinked`（`ReferenceManager.vb:429`）——R2 **采纳**缓存符号时**未校验 `IsLinked` 兼容性**；原代码只在**新建**分支进那一行，故不会踩。
- **6 条** `Assert.NotSame() Failure: Values are the same instance`（`NoPia.LocalTypeSubstitution*`）——测试**要求不同实例**，修复让跨编译共享 ⇒ **共享范围过宽**。
- **7 条** `UsedAssembliesTests` 引用类型／顺序不符 ＋ 5 条 `AggregateException` ＋ 3 条零散。

⇒ **L2 777/0 未变**，故障在编译器符号层。**教训**：四条配方全绿 ＋ 变异可证**只覆盖了脚本／提交形状，没覆盖普通编译的符号身份语义**。**这也是为什么 issue 35 §六 那条「任何后续改动都必须带'普通类/模块属性位不变'的对照格」不能只对照属性位**——**符号身份/实例同一性**同样要被对照。

**③ C# 侧是同一个缺陷，不是"类似缺陷"（2026-10-02 逐行核对，未修改任何代码）**

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

**处置建议（待作者裁决）：reserve。** 单独修 VB 侧＝对上游 C# 形成分叉，而 `decisions.md` D5 要求「**为什么 VB 必须分叉**」——目前能给的唯一理由是「本 fork 只发布 VB 脚本、C# 侧是死代码」，那是**产品范围**理由、不是**技术**理由。**故本条暂不交付**，连同 R2 的 Symbol 门回归一并保留在工作树待议。

**怎么了结（若要推进）**：① 恢复上游一致性——**两侧一起改**（C# 侧同样把整段并入一次持锁），再回归两个语言的普通编译对照；或 ② 取得技术性理由（例如证明 C# 侧该路径在本 fork 内不可达且未来也不会用于产品）并写进 D5；或 ③ 放弃本条、按"上游共享缺陷"登记后交由 `dotnet/roslyn` 上游处理，**fork 不单边修**。

**未闭合**：**已无"第二条创建路径"这一项**——插桩对 `CachedSymbols` 的每一次写入计数后确认：无修复时同一 PE 产生多个符号（`dup-fresh` 16 组／`dup-chain` 8 组），带修复为 **0 组**，且无"建了未发布"的孤儿。候选修法 R 因自身引入的缺陷已撤回、R2 因普通编译回归已撤回（详见 §三之二 ①②），最终 **RESERVE**。仍未闭合的是 **issue 37**（`WeakList` 并发改写，R2 下未复现，归因已动摇）与 **issue 38**（`CheckTypeArguments`，需 `Net461` 自建 CoreLib 才能坐实），两者**都不属本条**，见各自 issue。

## 四、处置要求（本仓对偶发红的既有口径）

1. **不许拿"重跑就好"当结论**；要给的结论只有两种：与相关改动无关（附证据），或由某处引入（附归因链）。
2. 最小化：~~把并发触发缩到**两个类的最小组合**并固定复现（`ScriptTopLevelDefiniteAssignmentTests` ＋ 单一邻居），再缩到单条源串＋一次并发编译的宿主级复现~~ —— **2026-09-28 更新：前一半做到了（`SubmissionSharedHandlesHookupTests` ＋ `SubmissionSharedInitializerTests` ＝ 24 格 / 50% 红），后一半「缩到单条源串 ＋ 宿主级双线程」已被实测否掉**（独立 exe、16 线程 × 300 轮 × 4 组，**0 命中**；且单独测过"共享引用 vs 每次新建 `MetadataReference`"这个变量，两者都不复现）。⇒ 最小化的判别量**不是单条源串，是"并发的编译工作量"**；宿主级复现这条路**放弃**，改走"定位共享状态 → 消除它"。
3. 取证顺序：~~读 `Binder_Conversions.vb:442` 所在分支的前置条件……~~ —— **2026-09-28 已完成**，结论＝**产品并发缺陷**，见 §三之二。修法落在 `ReferenceManager.vb` 的缓存发布原子性上，**不是** `Binder_Conversions.vb:442`；那句断言**本身不许删改或降级**——它是发现问题的东西。
4. 若最终判装置缺陷：按 §4.5 的先例用**串行集合**隔离（`CollectionDefinition(..., DisableParallelization:=True)`），并给出"为什么不可能再重叠"的构造性论证，而不是靠连跑碰运气。

## 五、影响面

产品侧：任何并发执行脚本提交的宿主（IDE 后台分析 + 运行、或两个线程各自跑提交链）理论上都可能命中——**未实测**（◇），实测方法见 §四.2。测试侧：Semantic 门的稳定性；全量门本次绿，但按 §一 的配方在 46 格规模下 3/4 红 ⇒ 门的可靠性受影响。
