# issue 35：并行执行时脚本提交绑定撞 `Binder_Conversions.vb:442` 断言（**两个受害者**：`ScriptTopLevelDefiniteAssignmentTests` 一格 25–50%，`SubmissionSharedHandlesHookupTests` 在全量 Emit 门 ≈17%）

- **登记日期**：2026-09-24（main）；**2026-09-28 追加第二个受害者**（由 issue 36 的收口诊断顺带发现，见 §一之二）
- **状态**：**Open**（症状与复现频次已实锤，**两个受害者各有一条最小配方**；根因未查）。**计划已建**：`..\tasks\parallel-submission-binding-assert\{README,test-plan}.md`（待开工；第一片是**复现＋定性**，不是修——定不出产品并发 vs 夹具共享就不许动测试）。**下一片的起点已由 §一之二改写**：宿主级双线程复现那条路已被实测走不通，须先**二分邻居用例**
- **性质**：**合法输入触发编译器内部断言**（Debug 构建下 `Debug.Assert` ⇒ `InvalidOperationException`）——按 `decisions.md` D7 的"合法输入崩编译器即必修"这条，属必修面，不因"只在测试并行下出现"而降级
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
- **未查的部分**：为什么"有邻居并发"才触发。可疑方向（**都只是方向，未取读数**）：跨线程共享的符号/编译状态、`TopLevelCodeBinder`/提交链上的懒计算缓存非线程安全、或 `Debug.Assert` 依赖的 `AsSemantic`/错误类型在某条并发路径上尚未就位。

## 四、处置要求（本仓对偶发红的既有口径）

1. **不许拿"重跑就好"当结论**；要给的结论只有两种：与相关改动无关（附证据），或由某处引入（附归因链）。
2. 最小化：把并发触发缩到**两个类的最小组合**并固定复现（`ScriptTopLevelDefiniteAssignmentTests` ＋ 单一邻居），再缩到单条源串＋一次并发编译的宿主级复现（`Script.Create`/`ContinueWithAsync` 双线程）。
3. 取证顺序：读 `Binder_Conversions.vb:442` 所在分支的前置条件（`argument.Type` 与 `targetType` 各自从哪来），再判断是"断言前提在并发下不成立"（产品并发缺陷）还是"测试夹具跨用例共享了状态"（装置缺陷）——两者落点完全不同，不许跳过这一步改测试。
4. 若最终判装置缺陷：按 §4.5 的先例用**串行集合**隔离（`CollectionDefinition(..., DisableParallelization:=True)`），并给出"为什么不可能再重叠"的构造性论证，而不是靠连跑碰运气。

## 五、影响面

产品侧：任何并发执行脚本提交的宿主（IDE 后台分析 + 运行、或两个线程各自跑提交链）理论上都可能命中——**未实测**（◇），实测方法见 §四.2。测试侧：Semantic 门的稳定性；全量门本次绿，但按 §一 的配方在 46 格规模下 3/4 红 ⇒ 门的可靠性受影响。
