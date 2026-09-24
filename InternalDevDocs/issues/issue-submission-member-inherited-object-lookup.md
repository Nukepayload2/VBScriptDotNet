# [BUG] 脚本类"无基类型"的判据与 C# 不同形：VB 按 `TypeKind.Submission` 判、C# 按 `IsScriptClass` 判 ⇒ 非提交脚本类多继承了 `Object` 成员；本 fork 新落的 `MyBase`→`Object` 兜底与 C# 相反须回退

**状态**：**Not A Bug（可观测层与 C# 同形；仅补真值表测试、产品码零改动）**（2026-09-22 登记，同日改判重定范围 + 第二次更正：原症状在产品路径不复现；**2026-09-24 主线跑 S-F02a 真值表收口**：裸 `Object` 成员在提交类不可达（BC30451）、非提交脚本类与脚本内普通类可达（零错误）——与 C# 观测同形；V-B 判定＝**不收严 `:59`**（收严会让 VB 比 C# 更严）。落点＝新增 `ScriptBareObjectMemberTruthTableTests.vb` 9 格，详见末节「收口」）

**第二次更正（2026-09-22，main 实测；日志 `tmp\vortex-logs\submission-object-member-lookup\03-main-audit-s-f01b-and-R-probes.md`）**

1. **原症状（提交类成员体内 `Me.ToString()` / `MyClass.ToString()` 报 BC30456）在产品两条路径都不复现**：F03 重建后的发布版宿主下，探针 `R1`–`R6`（`tmp\spec-check-me\`）全部**零诊断**且打印出 `Submission#0`——包括逐字照抄 L2 换形状那一格源串的 `R5`。打印的容器实名 `Submission#0` 说明路径确实是**提交类**（非交互 `/script` 打印 `Script`）。F03 与 S-F01b 亦各自独立看到同一现象（`E@28` 零诊断、"P5 打出 `Submission#0`"）。⇒ 本文件此前「影响面＝`.vbx` 文件执行与 vbi REPL 提交都是提交类 ⇒ 都受影响」是**从容器类型推出来的**，不是逐路径测出来的；现降级为**未成立**。
2. **BC30456 至今唯一证据**是 F01 的 **L1 内存编译临时用例**，且其消息里的类型名是 `'Script'`（不是 `Submission#0`）⇒ 那次量的可能是**另一种产法/另一套选项组合**。`Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:489-491` 的换形状注释（"…files BC30456"）是**转述 F01**，F02 自己未实测（其日志显式声明"属另一条缺陷，本片段不修"）⇒ 回收该形状前必须先量。
3. **C# 是双层机制，"改判据＝与 C# 同形"不成立**（S-F01b U1，main 逐字复核）：显式 `this` / `base` 在 C# 脚本类里被**关键字门**一律拒绝（`Binder\Binder_Expressions.cs:55-73` 的 `HasThis` 逐字 `return !inTopLevelScriptMember || !isExplicit;` ⇒ 成员体也算；`BindBase` `:2636-2639` 报 CS1512，显式 `this.X` 报 CS0027，钉在 `Test\...SemanticErrorTests.cs:1354-1417`）；而**裸** `ToString()` 只在提交类报 CS0103（`Binder_Lookup.cs:399-401` 不查基链），**非提交 Script 类解析成功**——扩展走查在声明基为 null 时把 `Object` 补回（`TypeSymbolExtensions.cs:226-232` + `:269-288`）。⇒ VB 没有对应的走查兜底，**单把 `ImplicitNamedTypeSymbol.vb:59` 的判据改成 `IsScriptClass` 会让 VB 比 C# 更严**（U3 的同一警告）；且只回退本 fork 的 `MyBase` 兜底会落到**完全无诊断**（U2：完整回退炸 `BoundNodes.xml.Generated.vb:6012-6024` 非空断言，保留 `ErrorType` 则走 `Binder_Expressions.vb:3070-3072` 静默）⇒ 回退与诊断方案必须同批设计，不能凭猜选。
4. ⇒ **本文件的"真缺陷在反方向"一条（`TypeKind.Submission` vs `IsScriptClass` 不同形）暂挂为待实测**：先做 S-F02a 的 V1–V9 真值表（两轴＝HostObjectType 有/无 × Script/Submission 产法），交付每格「诊断集合 + 类型符号 + `BaseType` 实取值 + 运行输出」，再判 VB 到底差在哪一格。
**改判记录（2026-09-22，按 `decisions.md` D7 的冲突裁定）**：本条**登记时的原症状不是缺陷**——当时判它的理由是"C# 里 `this.ToString()` 在提交类成员体内可用"，**该前提是错的**。实测 C# 源码与 C# 自己的测试：

```csharp
// Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57
/// Returns null for a submission class.
/// This ensures that a submission class does not inherit methods such as ToString or GetHashCode.
internal override NamedTypeSymbol BaseTypeNoUseSiteDiagnostics
    => IsScriptClass ? null : this.DeclaringCompilation.GetSpecialType(SpecialType.System_Object);
```

`Compilers\CSharp\Test\Symbol\Symbols\ImplicitClassTests.cs:63` 对**非提交**脚本类也断言 `Assert.Null(scriptClass.BaseType())`，`:76` 断言裸 `ToString` 解析不到符号；C# 发射侧同样是"符号层无基类、发射补 `Object`"（`CSharp\Portable\...\NamedTypeSymbolAdapter.cs:286-303`，与本 fork VB 的 `Emit\NamedTypeSymbolAdapter.vb:243-247` 及其 `Debug.Assert(baseType Is Nothing)` 同形）。⇒ **脚本类取不到 `Object` 继承成员是预期行为**。
**作者裁定（2026-09-22）**：「遇到冲突？那按 C# script 实际策略来定。比如，`MyBase` 取到 `Nothing` 成为了预期行为。」据此，先前"「`MyBase` 应该指向 `System.Object`」"的口头裁定与据其落地的兜底（`Binding\Binder_Expressions.vb:2444-2451` 一带的 `GetBaseTypeOfScriptClass`）**一并作废并回退**。
**真缺陷在反方向**：VB 的 `Symbols\Source\ImplicitNamedTypeSymbol.vb:51-60` 算出 `System.Object` 后 `Return If(Me.TypeKind = TypeKind.Submission, Nothing, baseType)`——判据是 `TypeKind.Submission`，而 C# 用 `IsScriptClass` ⇒ **VB 的非提交脚本类（`DeclarationKind.Script`，`TypeKind.Class`）继承了 C# 刻意拒绝的 `Object` 成员**，两侧对"脚本类"的边界划法不同形。
**证据等级**：C# 侧**已检查**（源码注释 + C# 自己的测试断言逐行读）；VB 侧形状差异**已运行**（F01/F02 期间的内存编译实测，见下对照表）
**严重度**：低-中（不崩、不误诊；是**方言间不一致**——本 fork 自称与 C# 脚本模式同形，而这条不同形；且刚落的 `MyBase` 兜底给了 C# 没有的能力，属会被上游合并与后续移植反复绊到的分叉）
**影响面**：① `IsScriptClass` 为真但 `TypeKind` 非 `Submission` 的脚本编译（`DeclarationKind.Script`，如 `/script` 生产路径与非提交 API 编译）里 `Object` 继承成员可查、而 C# 拒绝；② 本 fork 新落的 `MyBase`→`Object` 兜底及其三处测试断言；③ 下游任务里为绕本问题改写的测试形状（`Me.ToString()` → `Me.字段`）

## 症状

```vb
Imports System

Dim counter As Integer = 42

Sub Show()
    Console.WriteLine(Me.ToString())        ' 期望：Object.ToString()，实际 BC30456
    Console.WriteLine(MyClass.ToString())   ' 同上
End Sub
```

诊断指向接收者成员名，措辞为「'ToString' is not a member of ...」。

## 对照（已运行，判别性）

| 形状 | 落点 | 实测 |
|---|---|---|
| 体内 `Me.ToString()` / `MyClass.ToString()` | **提交类**（`DeclarationKind.Submission`，`TypeKind.Submission`） | ❌ BC30456 |
| 同形状 | **非交互脚本类**（`DeclarationKind.Script` → `TypeKind.Class`，`/script`） | ✅ 零诊断并运行，输出实例的类型全名 |
| 同形状 | 脚本里声明的**普通类**（脚本类的嵌套类型） | ✅ 合法（对照 `tmp\spec-check-me\K` / `M`，`MyBase.ToString()` 亦合法） |
| 体内 `MyBase.ToString()` | **提交类** | ✅ 零诊断，绑定到 `System.Object.ToString`（任务 F01 只在 `MyBase` 的**绑定路径**上补了 `Object`） |
| 体内 `Me.字段` / `MyClass.字段` | **提交类** | ✅ 零诊断（字段声明在提交类自身上，不需基类型） |

即：失败面精确地等于「**提交类 + 需要沿基类型链查找的成员**」。

## 根因（源码核实）

`Compilers\VisualBasic\Portable\Symbols\Source\ImplicitNamedTypeSymbol.vb:51-60`：先算出 `System.Object` 做可用站点检查，再

```vb
Return If(Me.TypeKind = TypeKind.Submission, Nothing, baseType)
```

注释逐字「Although submission semantically doesn't have a base class we need to emit one.」——提交类的**符号层基类型刻意是 `Nothing`**（发射仍要有基类型）。因此以 `Me` / `MyClass` 为接收者的成员解析沿 `BaseType` 上溯时到不了 `Object`，`ToString` / `GetType` / `Equals` / `GetHashCode` 这类继承成员查无此名。`MyBase` 不受影响是因为它的类型取自 `BaseTypeNoUseSiteDiagnostics` 这一条**绑定路径**，任务 F01 在该路径上加了脚本类的 `Object` 兜底；`Me` / `MyClass` 走的是成员查找，不在该路径上。

## 预期行为（2026-09-22 按 C# 实测重写）

**判据**：与 C# 脚本模式同形——**任何**脚本类（提交与非提交都算）都报告无基类型，因此 `MyBase` / `Me` / `MyClass` 在成员体内都**取不到** `Object` 的继承成员；需要 `ToString` 之类的调用要写在实例上。原先"让提交类成员体内也能取到 `Object` 成员"的两条候选修法（查找回合 / 复制 `MyBase` 特例）**全部作废**，不再实施。

要做的是两件事：
1. **回退**本 fork 刚落的 `MyBase`→`System.Object` 兜底（`Binding\Binder_Expressions.vb` 的 `GetBaseTypeOfScriptClass` 及其在 `BindMyBaseExpression` 成功分支的接入点）与据其写入的三处测试断言（`ScriptSemanticsTests.vb` 的 `ExplicitMyBaseInTopLevelFunctionBody_BindsToSystemObject`、`ExplicitMyBaseInScriptFunctionBody_RunsAndPrintsTheScriptClassName`、`TopLevelMyBaseInInstanceMethod_BindsToSystemObject`）。回退后 `MyBase` 在脚本类成员体内的诊断真值由补取证 S-F01b 的 U2 定（不得凭印象写）。
2. **收严判据到 `IsScriptClass`**：`Symbols\Source\ImplicitNamedTypeSymbol.vb:51-60` 的 `Return If(Me.TypeKind = TypeKind.Submission, Nothing, baseType)` 改为按 `IsScriptClass` 判，使非提交脚本类也不继承 `Object` 成员——与 `Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57` 逐字同形。伴随点（发射补 `Object`、`LookupBaseMembers`、可覆盖性判定）由 S-F01b 的 U3 逐条给「跟着改 / 保持 / 加断言」，**不在此预先拍死**。

**登记为预期、不再重开的部分**：`Me.ToString()` / `MyClass.ToString()` 在脚本类成员体内报"取不到成员"（VB 现读 BC30456）；这与 C# 同形，**不是缺陷**。诊断文案是否诚实属另一条待 S-F01b 判的次要项。

**关于 `ImplicitNamedTypeSymbol.vb:59` 的旧约束（已消解）**：登记本 issue 时我把它列为**禁止项**（怕动全编译器的 `BaseType` 取值），后来又在没有消费点清点的情况下降级为"若证据支持就给提交类 `Object` 基"。**两次都偏了**：S-F01 的清点显示该行的 `Nothing` 是 C# 同形的设计（配合发射侧补 `Object` + 断言），要动的不是"给不给 `Object`"，而是**这条判据的范围**——从 `TypeKind.Submission` 扩到 `IsScriptClass`，让非提交脚本类也走同一条规则。教训记进 `pitfalls.md`：以 C# 为判据时，先确认 C# 在该行的**意图**，再谈改法；"看着危险"不构成禁令，也"看着能放开"不构成理由。

## 与在办任务的关系

- `tasks\script-class-explicit-me-scope\`：F01 的解禁使该形状从「BC36966（拒绝关键字）」变成「BC30456（拒绝成员名）」——两者都是编译错误，**无回退**，但新错误对位置的说明同样不可用。该任务 §一 非范围已把它排除，`test-plan.md` §二 E4 因此把体内正向用例的形状定为 `Me.字段` / `MyClass.字段`，不用 `Me.ToString()`。
- `issues\issue-submission-shared-member-implicit-me.md`（issue 08）与本条同属「脚本类符号层刻意为之的性质，在绑定/查找的另一侧变成缺口」一族。

## 未复现 / 未查

- 受影响成员清单未逐条实测（`ToString` / `GetType` 实锤，`Equals` / `GetHashCode` / `ReferenceEquals` **推测**同源）。
- 宿主对象（globals）成员的显式接收者形状未测。
- `MyClass` 在非交互脚本类（`TypeKind.Class`）下是否零诊断——F01 只测了 `MyBase` 与该形状下的 `ToString()`（输出 `ScriptScript`），`MyClass.GetType()` 等未测。
- 提交类「无基类型」这一事实在其它代码路径上的依赖面未清点（选甲修法前必须清点）。

## 收口：V-A 实测真值表 + V-B 判定（2026-09-24，主线亲跑；S-F02a 完成）

**只加测试、不改产品码。** 落点＝新增 `Compilers\VisualBasicSemanticTest\Semantics\ScriptBareObjectMemberTruthTableTests.vb`（9 格，档 1 已运行 ✔）。上节"受影响成员清单未逐条实测"的空白由下表填齐。探针＝**裸名**（无显式接收者）访问继承自 `System.Object` 的成员。

| 容器（`scriptClass.TypeKind` / `BaseType`） | 裸 `ToString()` / `GetHashCode()`：顶层 / 实例体 / `Shared` 体 | 裸 `GetType()` |
|---|---|---|
| 提交类（Submission / `Nothing` 基） | **BC30451**（未声明）× 全三位置 | BC30182 |
| 非提交脚本类（`TypeKind.Class` / `Object` 基） | **零错误 / 零错误 / BC30369**（解析到 `Object` 实例成员后，因无实例被拒） | BC30182 |
| 脚本所声明的普通类（`Class` / `Object` 基） | 零错误 / BC30369（无顶层）| BC30182 |

**要点（实测）：**
1. `GetType()` 不是 `Object.GetType` 的探针——裸 `GetType()` 绑定到 `GetType` **运算符**关键字，三容器一律 BC30182「应为类型」；故它不计入 `Object` 成员可达性结论（已在测试里单独钉成一格作混淆说明）。
2. 提交类对裸 `Object` 成员 **不可达**（BC30451），非提交脚本类与脚本内普通类 **可达**（顶层/实例零错误）；后二者在 `Shared` 体里都退化成 BC30369（解析成功、但按实例成员对待）——**非提交脚本类与"脚本把它当类"完全同形**。
3. `Me.字段` / `MyClass.字段`（同容器显式接收者路径）另由 issue 28/29 既有格覆盖，本表不动。

**V-B 判定：不收严 `ImplicitNamedTypeSymbol.vb:59`（维持 `TypeKind.Submission`）——产品码零改动。** 理由：
- VB 现状下，提交类裸 `ToString()` → BC30451；C# 侧同样不把裸 `Object` 成员解析出来（`ImplicitClassTests.cs:63/:76`，◇继承未本轮复跑）⇒ **提交类两侧同形**，本 issue 原设想的"取不到 = 缺陷"在产品路径**不成立**（与 R1–R6 的"显式 `Me.ToString()` 可执行"一致：可达性是显式接收者路径给的，非裸名路径）。
- 非提交脚本类：VB 靠"基类型＝`Object`"让裸成员可达；C# 靠 `TypeSymbolExtensions.cs:226-232` 的绑定期走查"补回 `Object`"达到**同样可观测**的可达（其符号层基类型仍为 `null`）。⇒ 两侧**观测结果相同、机制不同**。若照 C# 的谓词把 VB 的 `:59` 从 `TypeKind.Submission` 收严到 `IsScriptClass`，VB 的非提交脚本类会**丢掉 `Object` 基、又没有那层走查兜底** ⇒ 变成 BC30451，即 **VB 比 C# 更严**——这正是队列 #2 判的过度分叉，故否决。
- ⇒ 本条以「可观测层与 C# 同形、无需改码」收口；`MyBase`→`Object` 兜底已由 `script-class-explicit-keyword-parity-revert` 回退，无需再动。C# 列读数为继承（◇），VB 列为本轮实测（✔）。
