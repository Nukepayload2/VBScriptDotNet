# issue 37：同进程多编译共用一份元数据时，`WeakList` 被并发改写，`Add`/`Resize` 的自检炸掉

- **登记日期**：2026-10-02（main）
- **状态**：**Open，但归因已动摇**（2026-10-02 更新：最终修复 R2 下**未复现**；且它在**中间候选 R 之下复现过，而 R 已被证明自己引入了缺陷** ⇒ 本条**可能不是独立缺陷**）
- **来历**：行 K（issue 35）追查第二个受害者时，用"自建全新 `MetadataReference` ＋ 16 线程"这套确定复现的配置顺带撞出来的**两条**确定性红之一。**不是 issue 35 登记的那句断言**，签名不同。
- **与 issue 35 的关系**：不推翻它。issue 35 的根因是 `ReferenceManager.vb` 的**缓存创建与发布不原子**，已定位并由最终修复 R2 处理（`upstream-merge.md` §2.25(k)）。本条曾被视为同族，**现证据指向不同源**（见下）。

## 触发面与症状

构造一个**全新的**（⇒ 缓存必空的）`MetadataReference`，让 16 个线程各自建一次提交并跑 ⇒ **必现**：

- `Compilers\Core\Portable\InternalUtilities\WeakList.cs:157`（`Add`）与 `:27`（`Resize`）的 `Debug.Assert(_size == _items.Length)` 炸掉。

## 为什么 `WeakList` 挡不住（**读码确认**）

`WeakList<T>` 本身**不是线程安全的**，而危险点在**枚举器的收尾**：

- `Enumerator` 构造时**快照** `_count = weakList._size`（`:174`）；
- 每次 `MoveNext` **重新读** `_weakList._items`（`:188`）——若期间发生过 `Add` 满时的 `Resize()`（`:150-159`），`_items` 已被**换成新数组**（`:67` / `:84`），而 `_nextIndex`／`_count` 仍按旧布局 ⇒ **索引错位**；
- 枚举**结束时改写列表**：`Clear`（`:208-209`）或 **`Shrink` 原地压缩重排**（`:214`，`Compact` 会把存活元素往前搬）。⇒ **一次看起来只读的 `For Each` 是在写列表。**

## 已排除的（别重做）

- **不是"忘了加锁"**：`CommonReferenceManager.State.cs:34` 的 `SymbolCacheAndReferenceManagerStateGuard` 是 **`static`**（进程级）⇒ 跨 `ReferenceManager` 实例有互斥。已逐个查过所有 `CachedSymbols` 访问点：Core 的 `CommonReferenceManager.Binding.cs:583` / `Resolution.cs:331` 只**传递**列表不枚举不改；C# 侧 `ReferenceManager.cs:1014-1017` 持锁枚举、`:634` 的 `Add` 在 `UpdateSymbolCacheNoLock` 内。**没有裸访问。**
- `CachedSymbols` **确实会被两个 `AssemblyMetadata` 共享**（`AssemblyMetadata.cs:77-82` 复制构造在 `shareCachedSymbols` 时令 `Me.CachedSymbols = other.CachedSymbols`；`:262` 有该路径）——但因锁是 `static`，共享本身不构成竞态。

## 预期行为

并发地"遍历缓存"与"往缓存添加"应当互不破坏：要么全在锁下，要么容器自身线程安全。现在一个**读操作会在收尾时改写容器**，于是即使调用方都持锁，**两个持同一把锁的线程也不会交错**——问题必然出在**某条没持锁的路径**上，那条路径**尚未找到**。

## 未闭合（**待查**，2026-10-02 更新后优先级下降）

- **哪一处 `WeakList` 的访问没有持 `SymbolCacheAndReferenceManagerStateGuard`**——主线已排除"忘了加锁"（锁是 `static` 进程级，所有访问点都在锁内；第二个 `WeakList` 是实例级且枚举点在锁内）⇒ 进程级共享的 `WeakList` **只有 `AssemblyMetadata.CachedSymbols` 一个**。
- **2026-10-02 的新证据（重要）**：本条在**中间候选 R** 之下复现过，但 **R 已被证明自己引入了缺陷**（它让初始化留在锁外而发布进了锁内，锁内出现"已发布但尚未 `SetReferences`"的符号，`ReuseAssemblySymbols` 读其 bound references 时炸）⇒ **R 下观察到的这条红很可能是 R 的副产品，不是独立缺陷**。
- **最终修复 R2 下未复现**：四条配方共 **26 次运行，失败签名全为 `none`**。⚠ **这既不能证明"已修"，也不能证明"仍坏"**——R2 修的不是 `WeakList` 的线程安全性，而是"发布点与创建点不原子"。
- **怎么坐实／了结**：① 在 R2 下把"60 波 × 16 线程"压力格跑**更多轮**（当前轮数可能不够）；或 ② 给 `WeakList` 加内部锁做 A/B——若加锁后症状消失，则本条成立；若不消失，本条应**降级为非缺陷**并删除。

## 修法方向（未定，且**先别动**）

- 在**调用方**补锁（改动面小、可向 C# 解释）——但要先知道**到底有没有漏锁的调用方**。
- 给容器本身加同步（改动面大、且是**上游同名文件** `Compilers\Core\Portable\InternalUtilities\WeakList.cs`，合并冲突面需登记账本）。
- ⚠ 若上条 ② 证明本条**不成立**，则**不要**做以上任何一项。**本轮明确不动它。**

## 复现配方

见 `..\..\tmp\vortex-logs\parallel-submission-binding-assert\` 下的探针（自建全新 `MetadataReference` ＋ 16 线程，**必现**）。**注**：issue 35 的"宿主级独立 exe 16 线程 × 300 轮"那条配方**复现不出来**（0 命中），已放弃；本条用的是另一套配置。
