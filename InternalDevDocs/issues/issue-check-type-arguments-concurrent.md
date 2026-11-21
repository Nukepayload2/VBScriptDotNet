# issue 38：同进程多编译共用一份元数据时，`CheckTypeArguments` 抛 `ArgumentException`

- **登记日期**：2026-10-02
- **状态**：**Open（未定性）**。原码上 6/6 复现、且不是候选修法的副产品（下表），但「是否只在自建 CoreLib 引用下成立」的决定性对照尚未做（见「未闭合」）。⚠ **"换编译条件就触发不了"不构成否证**——那次实验**无效**：它用**普通编译＋单线程**，而本条的失败**必须**走提交路径（栈经 `SynthesizedInteractiveInitializerMethod`）**且并发**；**换条件的"触发不了"是无效否证，什么也证明不了**。
- **⚠ 判据纪律：「触发不了」≠「误报」。否证必须在同一条件下做。** 同条件的真伪对照：

  | 对象 | ORIGINAL 上复现？ | 带 R/R2 后？ | 判定 |
  |---|---|---|---|
  | **本条 `CheckTypeArguments`** | **能**，`P2-new-pin-ORIGINAL-run1..6` **6/6** | **仍复现**（`P-new-pin-WITH-FIX`／`Q-pin-WITH-FIX-innerguard`／`R-pin-wave4-WITH-FIX` 各 6 次） | **原码上成立，且非候选修法的副产品** |
  | `issue-weaklist-concurrent-mutation` | **不能** | 只在 `M1-mutation-publish-after-lock` 等**变异体**下出现 | 结案＝不是独立缺陷 |

  ⇒ **两条必须分开**：#37 是变异体产物（已结案），**本条在原码上成立**，二者不可混为一谈。
- **⚠ 探针曾经的误诊（记录以免重犯）**：探针第一版报 `BC30183`，当时判成"引用程序集未绑定成功"；该判断**错误**——`BC30183` = `ERR_InvalidUseOfKeyword`（`Errors.vb:218`，消息「关键字作为标识符无效」），真因是**探针里的 VB 源码把 `Get` 用作方法名，而 `Get` 是 VB 保留字**（文件 I/O 的 `Get`/`Put`）。`vbc` 直接把波浪线画在 `Public Shared Function Get()` 上。⇒ **编译器根本没走到引用绑定，mscorlib 与本条无关**。**诊断"引用/程序集"类失败前，先确认探针自己那侧能编译通过**，否则量的全是探针的错。
- **来历**：行 K（issue 35）追查第二个受害者时，用"自建全新 `MetadataReference` ＋ 16 线程"这套确定复现的配置顺带撞出来的**两条**确定性红之二。**不是 issue 35 登记的那句断言**，也不是 `issue-weaklist-concurrent-mutation.md` 那条 `WeakList` 断言——**三条签名各不相同**。
- **与 issue 35 的关系**：**确属不同族**（见下）。issue 35 的根因（缓存创建与发布不原子）**已定位但未修**（上游来源，D7 例外 (b)），本条**推测与它无关**（未证实）。
- **与 issue 37 的关系**：issue 37 已结案为「不是独立缺陷」；本条**仍然 Open**，不得随之降级。

## 触发面与症状

构造**全新的**（⇒ 缓存必空）`MetadataReference`，16 线程各自建一次提交并跑 ⇒ **必现**：

```
System.ArgumentException
  at Microsoft.CodeAnalysis.TypeSymbolExtensions.CheckTypeArguments(TypeSymbol type, ...)
  ... 经 Microsoft.CodeAnalysis.VisualBasic.SynthesizedInteractiveInitializerMethod（:171）
```

## 与另两条红的关系（**已区分**）

| | 抛点 | 签名 |
|---|---|---|
| issue 35 | `Binder_Conversions.vb:442` | `InvalidOperationException : argument.Type.IsSameTypeIgnoringAll(targetType)` ＋ 池泄漏清单 |
| issue 37 | `WeakList.cs:27` / `:157` | `Debug.Assert(_size == _items.Length)`（`Resize`）／`Debug.Assert(_size < _items.Length)`（`Add`） |
| **本条** | `TypeSymbolExtensions.vb:999` | `ArgumentException` |

⇒ **三条是不同的失败形态**，不能因为"都在并发下出现"就当同一条。按项目口径，同族 ≠ 同因；**同因需要证据**。

## 未闭合：候选解释（推测）

- **抛出点的前置条件在并发下为何不成立**。`CheckTypeArguments` 抛 `ArgumentException` 通常意味着看到了一组自相矛盾的类型实参。
- **最可能的解释（推测）＝"自建 CoreLib 引用"这一非常规配置**（原 issue 已列，**现在有对照证据了**）：本条当初是用**自建引用**撞到的；而最终修复 R2 那一轮用的是 `Net461` 的 `MscorlibRef_v4_0_30316_17626` / `MsvbRef_v4_0_30319_17929` ⇒ **两次不是同一套引用**，"R2 下未复现"**可能只是配置不同**，而不是缺陷被修掉。
- **坐实／了结的办法（唯一）**：用 `MetadataReference.CreateFromImage(Net461.ReferenceInfos.mscorlib.ImageBytes)` **自建真 CoreLib** 跑同一脚本——**若只在那种配置下出现，即坐实"配置问题"、本条应从并发族降级**；若用标准引用也复现，才是并发缺陷。
- 是否与 issue 35 的第二条"产生第二个 `PEAssemblySymbol` 的路径"、或 issue 37 的"无锁访问路径"是同一条——**目前证据倾向于"都不是"**。

## 修法方向（未定）

- **先定性再动手**：本条是三条里**最可能是配置问题**的一条。**定性未做完之前不许改产品码。**
- **不要**在 issue 35／37 的修复里顺手带它——三者签名各异，**同族 ≠ 同因**。

## 复现配方

自建**全新** `MetadataReference`（缓存必空）＋ 16 个专用线程各自建一次提交并跑，**必现**；探针与日志在仓库外的 `<项目根>/tmp/probe/logs/`（`P2-new-pin-ORIGINAL-*`、`S2-pin-defaultrefs-ORIGINAL-*` 等，不入库；`<项目根>` 指本机 clone 根，故不写具体路径）。**注**：issue 35 的"宿主级独立 exe 16 线程 × 300 轮"配方**复现不出来**（0 命中），已放弃；本条用的是另一套配置。

## 复核记录：归属证据与处置

**时点：2026-11-18。**

**栈上两处代码均为上游原样，fork 未改**：

- `Compilers\VisualBasic\Portable\Symbols\Source\SynthesizedInteractiveInitializerMethod.vb`：git 历史只有 `e814cb1 add base compiler`（上游快照导入），之后零改动；`upstream-merge.md` 台账未登记该文件。
- `Compilers\VisualBasic\Portable\Symbols\TypeSymbolExtensions.vb`：fork 改过（`3dfb3bc`／`e307d0f`／`5803347`），但 hunk 位于 363／391／524／1358 行附近，`CheckTypeArguments`（`:992`）**逐字未动**；其正文是纯守卫（`typeArg Is Nothing` ⇒ `TypeArgumentCannotBeNothing`；个数不符 ⇒ `WrongNumberOfTypeArguments`）。
- 共享层 `ReferenceManager`／`CommonReferenceManager.State`／`WeakList`／C# `ReferenceManager.cs` 与上游 `release/stable @ 0e401fcf66c` **逐字相同、0 差异**（**推测**：复用 issue 35 的 diff 读数，本条未独立复跑）。
- 机制读法：`SynthesizedInteractiveInitializerMethod.vb:171` 的 `taskT.Construct(resultType)`，`resultType` 取自 `GetSpecialType(SpecialType.System_Object)` 或 `GetTypeByReflectionType(submissionReturnType)`；异常意味着并发下某次查找返回了 `Nothing`，被 `Construct` 的守卫捕获。
- 补充：`ScriptCompilationInfo`／`SynthesizedInteractiveInitializerMethod`／`Construct` 均为上游既有 VB 脚本机制——「提交路径 ＋ 并发」不是本 fork 新造的用法；fork 在 `Scripting\` 侧改的是方言语义与宿主装配，不在本栈上。

**仍未闭合（与上文「未闭合」一致）**：决定性配置对照实验尚未做——用 `MetadataReference.CreateFromImage(Net461.ReferenceInfos.mscorlib.ImageBytes)` 造真 CoreLib，与自建 CoreLib 在同一条 16 线程配方下各跑 6 轮。若只在自建引用下复现，本条应从「并发族」降级为测试配置问题；若真 CoreLib 也复现，才坐实并发缺陷。**本条保持 Open，不因本复核降级。**

**处置：只做仓内记录，不向 `dotnet/roslyn` 提交**（作者裁决，2026-11-18）。理由：可行的修法都要求改动共享层的设计语义（把进程级符号缓存的「查／建／发布」改为原子，或改变跨编译共享元数据的设计约定），作者判断上游不接受这类设计变更。**与 D7 例外 (b) 的差异**：(b) 的目标字段写「目标 `dotnet/roslyn`」，本条不作外部提交——差异已记入 `decisions.md` D7 的裁决补记；「本 fork 不单边修」的结论与 (b) 一致。**暴露面限定**：产品宿主无并发提交原语；测试进程有暴露面（issue 35 已实测：普通类间并行即可命中，且曾中止 test host）。若将来产品引入并发（多线程宿主／后台分析），按当时证据重新评估。

**与 issue 35 的关系不变**：两者的签名、抛点、配方都不同，是否同源仍未证实；issue 35 的串行隔离（§七）不覆盖本条，本条也没有被该隔离掩盖——它的触发夹具是自建引用 ＋ 16 线程，不在七门里。
