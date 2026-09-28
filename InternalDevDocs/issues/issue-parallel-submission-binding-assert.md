# issue 35：并行执行时脚本提交绑定撞 `Binder_Conversions.vb:442` 断言（`ScriptTopLevelDefiniteAssignmentTests` 一格，25–50% 复现）

- **登记日期**：2026-09-24（main）
- **状态**：**Open**（症状、复现配方、非归因项均已实锤；根因未查）
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
