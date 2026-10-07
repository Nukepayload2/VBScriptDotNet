# issue 31：跨提交的共享 `Handles`——事件在前一次提交、处理器在后一次提交时仍然 ICE

- **登记日期**：2026-09-24
- **状态**：**Fixed**（已验证，commit 待作者提交后补；采"令其可用"而非报错——落点见 §八，取代 §七的"停在设计裁定前"）
- **性质**：**合法输入崩编译器**（强形态，作者判定原则第一条）⇒ 不受"是否 C# 对等"影响，本身即必修。C# 侧无对偶概念（无 `Handles` / `WithEvents`），故本条**不是** D7 自动裁的产物，判据由 VB 自身契约给出。
- **前置**：issue 18-B（`Shared` 提交类 `Handles` 无 `.cctor` 可注入）**只修了"事件与处理器同属一次提交"这一格**（`tasks\submission-shared-handles-hookup\`，账本 `..\upstream-merge.md` §2.25(d)）。本条是它**刻意留在范围外**的另一格，当时以探针发现、未展开。

## 一、症状（已实测，档 2 = 真实交互链）

流水账：`..\..\tmp\vortex-logs\submission-shared-handles-hookup\probe-chain.txt`（改动前二进制下的读数，本条与那次改动的关系见 §三）。

形状（REPL / `PreviousSubmissionChain` 两棵树）：

```vb
' 提交 #0
Shared Event E()
' 提交 #1
Shared Sub H() Handles Me.E
RaiseEvent E()   ' 在 #1 里触发
```

读数（同一次探针里先绑定 `#0`、再绑定 `#1`）：

| 步骤 | 读数 |
|---|---|
| `#0` 取诊断 | 零诊断；`SharedConstructors` 长度 **0** |
| `#1` 取诊断 | **抛** `System.IndexOutOfRangeException`（"Index was outside the bounds of the array."） |
| `#1` 取 `HandledEvents` | 同样抛 |
| `#1` `Emit` | 同样抛 |

栈顶逐字（探针输出）：

```
ImmutableArray`1.get_Item(Int32 index)
 at SourceMemberMethodSymbol.BindSingleHandlesClause(...) Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberMethodSymbol.vb:797
 at SourceMemberMethodSymbol.GetHandles(...) :590
 at SourceMemberMethodSymbol.get_HandledEvents() :565
```

⇒ 不是"静默不投递"、也不是诊断文案错位，而是 `GetDiagnostics()` / `Emit()` 双双抛托管异常。

## 二、根因（档 3 读码 + 上述实测，两半都有据）

1. `BindSingleHandlesClause` 对「处理器 `Shared` ∧ 事件 `Shared`」这一形状**无条件**取 `ContainingType.SharedConstructors(0)`（`:797` 的裸索引，无 `If length > 0` 前置），因此**任何**走到该支的形状都必须有一个共享构造器可注入。
2. 提交类的 `.cctor` 只在两处会被造出来：`AddDefaultConstructorIfNeeded`（有共享初始化器时）与 §2.25(d) 的 `AddWithEventsHookupConstructorsIfNeeded` 提交类分支。后者的判据是**在 `members.Members`（＝本提交自己的成员字典）里 `TryGetValue(eventName)` 找得到该事件**，找不到就 `Continue For`（见 `SourceMemberContainerTypeSymbol.vb:2880-2900` 区）。
3. 而跨提交的事件**不在**本提交的成员字典里——它沿 `Binder.LookupInSubmissions`（`Binder_Lookup.vb:858`）从 `PreviousSubmission` 查到，于是：绑定层认定"这是一个合法的共享 `Handles`，去 `SharedConstructors(0)` 挂"，收集层认定"这里没有我的事，不建 `.cctor`"。**两层的判据不同集** ⇒ 空数组裸索引。

⇒ 病灶是**两半判据不一致**，不是 `:797` 少一个 `If`。只在 `:797` 加防御性判断会把"合法脚本静默丢失事件挂钩"换掉 ICE——按 D5/D7 的分叉纪律，那种"消症状"的修法要登记为不可接受。

## 三、预期语义与判据（本条要冻结的东西）

1. **预期**：跨提交的 `Handles Me.<共享事件>` 与同提交的形状**行为一致**——零诊断、`#1` 可 `Emit`、`RaiseEvent` 时处理器被调用**恰好一次**（同提交那格的实测口径：`SubmissionSharedHandlesHookupTests.vb` 的 T6 锁"两次运行只挂一次"）。
2. **C# 无对偶 ⇒ 判据自 VB 侧取**：普通编译里 `Handles` 的事件容器为 `Me.` / `MyClass.` 时事件必须能在**容纳类型或其基类型**上找到（`MyBase.` 走基类链）；脚本方言把"提交链"当作同一逻辑类的连续声明面，因此"事件在更早提交里"就是这条规则的脚本对偶，而不是新语义。据此，**修法方向**（择一，须在实施片段里先冻结再动手）：
   - **甲（推荐先评估）**：`AddWithEventsHookupConstructorsIfNeeded` 的提交类分支里，事件名查不到本提交成员时**沿提交链继续查**（`GetPriorSubmissionMembers` / `ImplicitNamedTypeSymbol` 的 `PreviousSubmission` 面），只要**任一**沿链结果是 `Shared Event` 就 `EnsureCtor(isShared:=True)`。判据与绑定层同源，两半重新同集。
   - **乙**：把"是否需要 `.cctor`"的判据放宽为「本提交有 `Shared` 且带 `Handles` 且事件容器是关键字（非 `MyBase`）的方法」，不看事件能不能查到——代价＝给"事件名写错"的提交也建一个空 `.cctor`（该形状本应只报 BC30183/BC31407 一类诊断），需实测那两种诊断的可达性后再判。
   - 两法都必须保持 §2.25(d) 的既有约束：**不得**无条件给无关提交类造 `.cctor`，`Class` / `Module` 分支一字不动（非脚本面免疫）。
3. **必须钉的反例格**：
   - `MyBase.E` 容器（提交类无基类型）→ 走原有诊断，不建 `.cctor`，不 ICE；
   - 事件名根本不存在 → 原有诊断（不得被新判据"顺手放过"）；
   - 事件在更早提交但**非** `Shared`、处理器 `Shared` → 原有 BC30043/BC30369 一类诊断，不得变成运行时；
   - 非脚本普通类的跨类型 `Handles`（`Class A` 的事件 + `Class B` 的 `Handles A.E`）行为逐字不变＝本条改动**只**在 `TypeKind.Submission` 分支内。

## 四、验证义务（开工时按此顺序）

1. 真值先行：先把上面四格在**当前码**上跑一遍（前 3 格现状预期是 ICE 或既有诊断，第 4 格预期不变），读数写进流水账，再改码——同 issue 18-B 的口径（登记时的症状被实测推翻过一次，不许再犯）。
2. 红灯可复现：新增的"跨提交共享 `Handles` 取到值 + 恰好一次"用例必须在未修时**失败**（ICE 也算失败），修后转绿。
3. 回归：定向（`SubmissionSharedHandlesHookupTests`、`ScriptSemanticsTests`、`ScriptMode*`）由实施者跑；七门全量与 L2 由 main 跑，实施者**不得**跑全量（token 纪律）。
4. 账本：收口后在 `..\upstream-merge.md` §2.25(d) 追加"跨提交那一格也已收"，并补本条的 issue→task 指针；**commit 号一律不预填**（作者 裁定：main 不创建提交）。

## 五、范围切分

- 范围内：上述判据冻结 + 择一修法 + 四格反例 + 正例。
- 非范围：issue 18-A（`.cctor` 何时被 CLR 触发的原因）、`WithEvents` 在提交类里的实例级挂钩（`meeting-with-events-in-submissions.md`）、实例级 `Handles`（已可用）。

## 六、必须一并判的第三条路（与 issue 19 的关系）

`issues\issue-cross-submission-handles-clause-crash.md`（issue 19，已 Fixed）对**跨提交的 `WithEvents` 容器**下的判据是「**报错**」而不是「修好」：新码 `BC37343`（`SourceMemberMethodSymbol.vb:707-711` 的 `TryCast` 早退），依据 `meetings\meeting-with-events-in-submissions.md:120`（R5：跨提交走诊断、覆盖属性路线否决）与 `proposals\proposal-with-events-in-submissions.md:439`。本条是**同一条边界的共享分支**，因此开工前必须先回答：

| 路线 | 内容 | 代价 / 判据 |
|---|---|---|
| 丙（新增要考虑，可能是正解） | 跨提交的 `Handles Me.<共享事件>` 也走**诊断**（复用 `BC37343` 或新码），只把「同提交」留作合法 | 与 issue 19 的既有裁定**同形**，改动最小；但要解释"为什么实例 `WithEvents` 与共享 `Event` 两条跨提交路径最终都只报错"——若答不出共享支为何能修而实例支不能，就应选丙 |
| 甲（§三原推荐） | 收集层沿提交链查事件，找到 `Shared Event` 就建 `.cctor` 并挂钩 | 真"能用"，但会把**跨提交共享事件**做成一条实例路径没有的**单边能力**（不对称） |
| 乙 | 放宽 `.cctor` 判据 | 会给"事件名写错"的提交也造空 `.cctor`，且仍不解决"挂到哪个 `.cctor`"的问题 |

⇒ **真值先行的第一步之后，先做丙/甲的取舍判定并写进本文件**，不得直接跳到实施。判据来源按 D7：C# 无 `Handles` 对偶，故取本仓既有裁定（issue 19 / R5）＋"对称性"这条自证理由。

## 七、取证与取舍分析〔已被 §八 取代〕

1. **vbi REPL 探针不能造跨提交**：`printf 'Shared Event E()\nShared Sub H() Handles Me.E\n…' | vbi`（无参、管道 stdin）把各行的提交**并成同一次提交**跑，命中的是 §2.25(d) 已修的"同提交"路径（exit 0、无 ICE）。⇒ 真值先行必须用**链式提交的单元测试**（`CreateSubmission(#1, previous:=#0)` 那种，见 `Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests.vb` 的 `CreateScriptCompilation`/`CreateSubmission` 手法），把 issue §三 的四格反例 + 正例在**当前码**上跑出来再谈改。**尚未**把这四格做成实跑读数 ⇒ §四.1 的"真值先行"未完成。
2. **代码级病灶定位（档 3 读码）**：收集层 `AddWithEventsHookupConstructorsIfNeeded` 的提交分支在事件名不在**本提交** `members.Members` 时 `Continue For`（`SourceMemberContainerTypeSymbol.vb:2891-2893`），故不建 `.cctor`；绑定层 `BindSingleHandlesClause`（`SourceMemberMethodSymbol.vb:797`）在**沿链**查到事件后**无条件** `SharedConstructors(0)` ⇒ 两阶段成员集不同 ⇒ 空数组越界。
3. **三条路的代价（须在写码前冻结其一）**：
   - **甲**：收集层查不到本提交事件时**沿提交链**找 `Shared Event`。真能用，但**依赖"符号收集期就能解析出前一次提交的类/成员"——此可用性未证实**（收集早于绑定，链可能尚未接好）。这正是 §四.1/§六 警告的"未取真值别硬改符号层"。
   - **乙**：把建 `.cctor` 的触发放宽为"本提交有 `Shared` 且带关键字容器（非 `MyBase`）的 `Handles` 方法"，**不查事件是否存在**。保证有 host，但会给"事件名写错/不存在"的提交也造**空 `.cctor`**——须实测那种形状仍由绑定层报既有诊断（BC30456/BC31407 一类）且**永不成功发射**，否则等于用空构造器换掉诊断（D5/D7 不接受"消症状"）。
   - **丙**：跨提交 `Handles Me.<共享事件>` 一律走**诊断**（复用/新增码），只留"同提交"合法——与 issue 19 对**实例**跨提交的裁定同形、改动最小；代价是把共享也降为"只能报错"。
4. **对称性分析（此处能答的部分）**：共享 `Handles` 的挂钩只需一个**可合成的 `.cctor`** + 一个**能沿链查到的共享事件**，**不需要** issue 19 实例 `WithEvents` 依赖的"基类可覆盖属性 + 虚派发"机器（提交类无基类型）⇒ 原则上甲是"真能修"、与实例那条不同，不构成"共享能修实例不能"的无解不对称。唯一未决＝甲所需的"收集期沿链解析事件"能否做到。
5. **推荐下一步（给作者/下一会话）**：先做**一次最小真值实验**——一个链式提交单测，验证收集期能否看见前一次提交的 `Shared Event`（能⇒甲；不能⇒在乙[须先证诊断不丢]与丙之间按"与 issue 19 对称"取丙）。**本条仍 Open、未开工**：甲-vs-丙 是有真实取舍、且 C# 无对偶可自动裁的设计选择，按 `decisions.md` D7「(a)/(b) 才上报、设计取向需裁定」的口径，这一步宜由作者定方向后再实施，不宜由工具静默选一条改符号/诊断语义。

## 八、收口：设计裁定与实测（档 1 已运行；✔＝亲验）

§七.5 把方向留给作者、并担心"收集期能否沿链解析事件"未证实。本节记录实际收口，并说明该担心为何**不成立**、从而不必在甲/乙/丙之间三选一。

1. **裁定：采"令其可用"（甲的结果），但落点选在"不需要收集期沿链解析"的那一半。** 关键读码（✔）：绑定层 `SourceMemberMethodSymbol.BindSingleHandlesClause` 在 `eventSymbol Is Nothing` 时**先** `Return Nothing`（`SourceMemberMethodSymbol.vb` 里早于 `SharedConstructors(0)` 那行），只有**沿链查到共享事件**后才会走到 `ContainingType.SharedConstructors(0)` 的裸索引。⇒ 触发 ICE 的**唯一**条件是"绑定层沿链找到了共享事件"，此时它必然需要一个 host。收集层不需要复制绑定层的链解析，只要**不再要求事件落在本提交成员字典**、对"提交类里有 `Shared` 且带关键字容器（非 `MyBase`）的 `Handles` 方法"这一形状**幂等地 `EnsureCtor(isShared:=True)`** 即可让两半重新同集。

2. **产品码改动（✔）**：`SourceMemberContainerTypeSymbol.AddWithEventsHookupConstructorsIfNeeded` 的 `TypeKind.Submission` 分支，事件名在本提交成员里查不到时，原为 `Continue For`（不建 host）；改为先 `EnsureCtor(members, isShared:=True, ...)` 再 `Continue For`。事件根本不存在时绑定层仍早退报诊断（BC30183/BC31407 一类），我们多造的 `.cctor` 因无人挂入而是惰性的——不吞诊断、不静默丢钩。`Class`/`Module` 分支一字未动（非脚本面免疫，`..\decisions.md` **D10**）。

3. **四格反例 + 正例（档 1 实跑，✔）**——见 `Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests.vb` 新区块"cross-submission shared Handles (issue 31)"：
   - `CrossSubmissionSharedHandles_SynthesizesHostAndWiresUp`：`#0` 声明 `Shared Event Ev`，`#1`（`previous:=#0`）声明 `Shared Sub H ... Handles Me.Ev` → `#1` **零诊断**、`ScriptClass.SharedConstructors.Length = 1`、挂钩 `MethodKind.SharedConstructor`（修前该格抛 `IndexOutOfRangeException`）。
   - `CrossSubmissionMissingEvent_StillReportsNotFound`：事件名根本不存在 → `GetDiagnostics` 仍含至少一条 Error 级诊断（新判据未"顺手放过"，惰性 `.cctor` 不消症状）。
   - 同提交那格、`MyBase.` 容器、实例事件/共享处理器等 §三.3 反例仍由 §2.25(d) 既有断言钉住，未回归。

4. **回归口径（✔）**：定向 `SubmissionSharedHandlesHookupTests` 12/12 绿；七门全量 Emit 门 **4380→4382 / Passed 4277→4279**（+2 新格），Semantic 门维持 5869/5765；L2 `Scripting\VisualBasicTest` 直跑 **768/0**，`GATES exit=0`、`L2 exit=0`。

5. **对称性问题的回答（回 §六）**：共享支"能修"而实例 `WithEvents` 支"只能报错"并非无解不对称——共享支的 host 是一个可合成的 `.cctor`（无需基类可覆盖属性与虚派发），跨提交事件由绑定层沿链解析、收集层只需补 host；实例 `WithEvents` 依赖 R5（`meeting-with-events-in-submissions.md:120`）已否决的覆盖属性机器，故仍走 `BC37343`。二者落点不同源于**机制**不同，不是任意偏置。
