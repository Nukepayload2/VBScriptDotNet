# issue 38：同进程多编译共用一份元数据时，`CheckTypeArguments` 抛 `ArgumentException`

- **登记日期**：2026-10-02（main）
- **状态**：**Open（本条配置假设已被削弱，但仍未结案）**。2026-10-02 用单线程、只换 CoreLib 引用构造方式（`CreateFromFile` vs `CreateFromImage`，**不带并发**）做判据：**两种方式 8/8 全绿、完全一致** ⇒ **"自建 CoreLib 引用"这一配置差异并未单独触发本条**。⚠ 但该探针用的是**普通编译、单线程**，**不复现当初的条件**（并发 ＋ 两段提交链），所以只能说"配置差异不足以单独造成它"，**不能说它已被排除**。
- **⚠ 一次误诊的更正（留给下一个接手的人）**：该探针第一版报 `BC30183`，我当时判成"引用程序集未绑定成功"。**那是错的**——`BC30183` = `ERR_InvalidUseOfKeyword`（`Errors.vb:218`，消息「关键字作为标识符无效」），真因是**探针里的 VB 源码把 `Get` 用作方法名，而 `Get` 是 VB 保留字**（文件 I/O 的 `Get`/`Put`）。`vbc` 直接把波浪线画在 `Public Shared Function Get()` 上。⇒ **编译器根本没走到引用绑定，mscorlib 与本条无关**。教训：**诊断"引用/程序集"类失败前，先确认探针自己那侧能编译通过**，否则量的全是探针的错。
- **来历**：行 K（issue 35）追查第二个受害者时，用"自建全新 `MetadataReference` ＋ 16 线程"这套确定复现的配置顺带撞出来的**两条**确定性红之二。**不是 issue 35 登记的那句断言**，也不是 `issue-weaklist-concurrent-mutation.md` 那条 `WeakList` 断言——**三条签名各不相同**。
- **与 issue 35 的关系**：**确属不同族**（见下）。issue 35 的根因（缓存创建与发布不原子）**已定位但未修**（RESERVE），本条**大概率与它无关**。
- **与 issue 37 的关系**：issue 37 已于 2026-10-02 结案为「不是独立缺陷」；本条**仍然 Open**，不得随之降级。

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
| issue 37 | `WeakList.cs:27/157` | `Debug.Assert(_size == _items.Length)` |
| **本条** | `TypeSymbolExtensions.vb:999` | `ArgumentException` |

⇒ **三条是不同的失败形态**，不能因为"都在并发下出现"就当同一条。按项目口径，同族 ≠ 同因；**同因需要证据**。

## 未闭合（**待查**，2026-10-02 更新：已找到最可能的解释）

- **抛出点的前置条件在并发下为何不成立**。`CheckTypeArguments` 抛 `ArgumentException` 通常意味着看到了一组自相矛盾的类型实参。
- **最可能的解释＝"自建 CoreLib 引用"这一非常规配置**（原 issue 已列，**现在有对照证据了**）：本条当初是用**自建引用**撞到的；而最终修复 R2 那一轮用的是 `Net461` 的 `MscorlibRef_v4_0_30316_17626` / `MsvbRef_v4_0_30319_17929` ⇒ **两次不是同一套引用**，"R2 下未复现"**可能只是配置不同**，而不是缺陷被修掉。
- **坐实／了结的办法（唯一）**：用 `MetadataReference.CreateFromImage(Net461.ReferenceInfos.mscorlib.ImageBytes)` **自建真 CoreLib** 跑同一脚本——**若只在那种配置下出现，即坐实"配置问题"、本条应从并发族降级**；若用标准引用也复现，才是并发缺陷。
- 是否与 issue 35 的第二条"产生第二个 `PEAssemblySymbol` 的路径"、或 issue 37 的"无锁访问路径"是同一条——**目前证据倾向于"都不是"**。

## 修法方向（未定）

- **先定性再动手**：本条是三条里**最可能是配置问题**的一条。**定性未做完之前不许改产品码。**
- **不要**在 issue 35／37 的修复里顺手带它——三者签名各异，**同族 ≠ 同因**。

## 复现配方

见 `..\..\tmp\vortex-logs\parallel-submission-binding-assert\` 下的探针（自建全新 `MetadataReference` ＋ 16 线程，**必现**）。**注**：issue 35 的"宿主级独立 exe 16 线程 × 300 轮"配方**复现不出来**（0 命中），已放弃；本条用的是另一套配置。
