# issue 38：同进程多编译共用一份元数据时，`CheckTypeArguments` 抛 `ArgumentException`

- **登记日期**：2026-10-02（main）
- **状态**：**Open**（原为**确定复现**；**2026-10-02 更新：最终修复 R2 下未复现，且已找到最可能的解释——两次用的是不同引用集**）
- **来历**：行 K（issue 35）追查第二个受害者时，用"自建全新 `MetadataReference` ＋ 16 线程"这套确定复现的配置顺带撞出来的**两条**确定性红之二。**不是 issue 35 登记的那句断言**，也不是 `issue-weaklist-concurrent-mutation.md` 那条 `WeakList` 断言——**三条签名各不相同**。
- **与 issue 35 的关系**：**确属不同族**（见下）。issue 35 的根因（缓存创建与发布不原子）已由最终修复 R2 处理，本条**大概率与它无关**。

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
