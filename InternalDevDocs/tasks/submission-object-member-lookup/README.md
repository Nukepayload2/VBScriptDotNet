# 任务：提交类成员体内取 `Object` 继承成员（与 C# 脚本模式对等）（submission-object-member-lookup）——任务计划

> **✅ 已收口（S-F02a）**：真值表已交付＝新增 `Compilers\VisualBasicSemanticTest\Semantics\ScriptBareObjectMemberTruthTableTests.vb` 9 格（仅测试、产品码零改，Semantic 5870→5879/5775 七门全绿）。V-A 读数与 V-B 判定（**不收严 `:59`**）见 `..\..\issues\issue-submission-member-inherited-object-lookup.md` 末节「收口」与 `..\..\..\tmp\HANDOFF.md` §4.13（本机文档）；issue 29 转 Not A Bug。下方 S-F02a/S-F02b 原文为计划史，S-F02b（谈回退）作废。
>
> **状态：取证已交付、前提被实测推翻 ⇒ 实施拆两步**（S-F01 + S-F01b 已交付；`R1`–`R6` 探针见 `tmp\vortex-logs\submission-object-member-lookup\03-main-audit-s-f01b-and-R-probes.md`）。流水账在 `tmp\vortex-logs\submission-object-member-lookup\`。缺陷登记：`..\..\issues\issue-submission-member-inherited-object-lookup.md`（issue 29，已带「更正」条目）。**子 agent 禁读 `issues\`**——症状、对照与判据已在此复述。
>
> **⚠ 更正（实测）**：本任务原前提「**提交类成员体内**以 `Me`/`MyClass` 取 `Object` 继承成员报 BC30456」在产品两条路径**都不复现**——F03 重建后的发布版宿主下 `R1`–`R6` 全部**零诊断**并打印 `Submission#0`（`R5` 逐字照抄 L2 换形状格的源串）。BC30456 唯一证据是 F01 的 **L1 内存编译临时用例**（其消息里的类型名是 `'Script'` 而非 `Submission#0` ⇒ 可能是另一种产法/选项组合）。⇒ **本任务不得在真值表之前改任何产品码**，先做 S-F02a。
>
> **⚠ 第四个新事实（S-F01b U1，main 逐字复核）**：C# 是**双层机制**——① 显式 `this`/`base` 在 C# 脚本类里被**关键字门**一律拒（`Binder\Binder_Expressions.cs:55-73` 逐字 `return !inTopLevelScriptMember || !isExplicit;`，成员体也算；`BindBase` `:2636-2639` CS1512、显式 `this.X` CS0027）；② 裸 `ToString()` 只在**提交类**报 CS0103（`Binder_Lookup.cs:399-401` 不查基链），**非提交 Script 类解析成功**——扩展走查在声明基为 null 时补回 `Object`（`TypeSymbolExtensions.cs:226-232`+`:269-288`）。⇒ 原第 ② 件事（把 `ImplicitNamedTypeSymbol.vb:59` 的判据从 `TypeKind.Submission` 收严到 `IsScriptClass`）**不等于与 C# 同形**：VB 没有那层走查兜底，收严会**比 C# 更严**（U3 同一警告）⇒ **暂缓，等真值表**。另外只回退 `MyBase` 兜底会落到**完全无诊断**（U2）⇒ 回退与诊断方案必须同批设计。

- **一句话（改判）**：本任务**不再**"让成员体内取到 `Object` 继承成员"。补取证显示 C# **刻意**不让脚本类继承 `ToString` / `GetHashCode`（`Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57` 注释点名；`Compilers\CSharp\Test\Symbol\Symbols\ImplicitClassTests.cs:63` 连**非提交**脚本类也断言 `BaseType()` 为 `null`、`:76` 断言裸 `ToString` 解析不到符号）⇒ 取不到是预期行为。任务变成两件事：**①回退**本 fork 刚落的 `MyBase`→`System.Object` 兜底及其测试与规范措辞；**②收严**VB 的「脚本类无基类型」判据——`Compilers\VisualBasic\Portable\Symbols\Source\ImplicitNamedTypeSymbol.vb:51-60` 现在按 `TypeKind.Submission` 判，C# 按 `IsScriptClass` 判 ⇒ VB 的**非提交脚本类多继承了 `Object` 成员**，这一侧才是真缺陷。
- **作者裁定**：先是「我的预期是修成和 C# 脚本模式对等的行为」；C# 实测与之相反后裁定「遇到冲突？那按 C# script 实际策略来定。比如，`MyBase` 取到 `Nothing` 成为了预期行为」。⇒ 本任务的判据是 **C# 的实际策略**，不是"能用就行"（`decisions.md` D7 的冲突裁定条）。
- **前置事实**：任务 `script-class-explicit-me-scope` 的 F01 已解除成员体内的显式关键字禁令（该部分**保留**，与 C# 的差异面另计），并按当时裁定在 `MyBase` 的**绑定路径**上补了 `Object` 兜底 + 三处测试（`ScriptSemanticsTests.vb` 的 `ExplicitMyBaseInTopLevelFunctionBody_BindsToSystemObject`、`ExplicitMyBaseInScriptFunctionBody_RunsAndPrintsTheScriptClassName`、`TopLevelMyBaseInInstanceMethod_BindsToSystemObject`）——**本任务把这三处连实现带断言回退**（回退方案与诊断真值由 S-F02a 的表决定，见上）。`Me.ToString()` / `MyClass.ToString()` 的现状**不再是"BC30456"**——产品两条路径实测零诊断（`R1`–`R6`），只有 L1 内存编译报 BC30456 且未定位成因 ⇒ 属待实测项。

## 一、症状与已知对照（不重做，可复核）

| 形状 | 容器 | 现状 |
|---|---|---|
| 成员体内 `Me.ToString()` / `MyClass.ToString()` | **提交类**（`TypeKind.Submission`） | **产品两条路径实测零诊断并正确执行**（`R1`/`R2`/`R4`/`R6`，F03 重建后的宿主 ⇒ **改动后**档 2；`R5` 逐字照抄 L2 换形状格）；**唯** L1 内存编译的临时用例报 BC30456（消息里类型名 `'Script'`）⇒ 成因未定位，见 §一末 |
| 同形状 | 非交互脚本类（`DeclarationKind.Script` → `TypeKind.Class`） | ✅ 零诊断并运行（F01 的 `/script` 运行证据，打印 `Script`） |
| 同形状 | 脚本里声明的普通类（`Class NoBase`，无 `Inherits`） | ✅ 合法（`MyBase.ToString()` 亦合法，输出 `Submission#0+NoBase`） |
| 成员体内 `MyBase.ToString()` | 提交类 | ✅ 零诊断（F01 已在绑定路径补 `Object` 兜底 ⇒ 待回退项） |
| 成员体内 `Me.字段` / `MyClass.字段` | 提交类 | ✅ 零诊断（成员声明在该类自身上，不需基类型链） |

⇒ **原「失败面＝提交类 + 需沿基类型链查找的成员」这一结论已不被产品读数支持**；剩下的问题是「L1 内存编译为什么报 BC30456」与「VB 提交类到底有没有走到 `Object`（哪条路走的）」，两问都由 S-F02a 的真值表回答。根因侧的既有事实保留：`Symbols\Source\ImplicitNamedTypeSymbol.vb:51-60` 对 `TypeKind.Submission` 返回 `Nothing`，注释逐字「Although submission semantically doesn't have a base class we need to emit one.」——**符号层无基类与查找能否到 `Object` 不是一回事**（C# 正是靠 `TypeSymbolExtensions.cs:226-232` 的走查把两者解耦）。

## 二、范围与非范围

### 范围内
| # | 单元 | 说明 |
|---|---|---|
| S-F01 | 取证（**只读**） | ① `BaseType` / `BaseTypeNoUseSiteDiagnostics` 在 VB 树内的**消费点清点**：谁依赖「提交类无基类型」这一事实；② **C# 脚本模式对照**（§四）；③ 候选修法与推荐（含"改符号层基类型"这一条的真实风险清单）。行为侧证据（哪些成员受影响）**只能用单元测试取**——发布版宿主是改动前二进制，禁以探针冒充 |
| S-F02a | **实测真值表（只加测试、不改产品码）** | 独占构建面。把 S-F01b 的 U4 清单 **V1–V9** 做成实跑格（两轴：`HostObjectType` 有/无 × `Script`/`Submission` 产法，产法见 `ScriptSemanticsTests.vb:1495-1496`）+ **U2 的三个回退变体**（完整回退 / 保留 `ErrorType` / 加诊断门）各跑一遍，交付每格「诊断集合（ID+span+消息）+ 表达式类型符号 + 该容器 `BaseType` 实取值 + 运行输出」；并复跑 main 的 `R1`–`R6` 源形状成 L1 用例，**回答"L1 为什么报 BC30456、宿主为什么不报"**。三态逐格标；**产品源码一字不改**（`git status` 只允许出现测试树） |
| S-F02b | 修复（**由 S-F02a 的表决定形态**） | 目标仍是与 C# 实际策略同形，但**具体动作不在这里预设**：`MyBase` 兜底回退（连同三处断言）与「`:59` 判据是否收严」两项**都要等表**——收严在 VB 里会比 C# 更严（无走查兜底）、只回退会完全无诊断（U2）。不许留两份相似特例当技术债，除非写得出理由 |
| S-F03 | 单元测试 | §三 契约逐格断言（值/类型级，不是"跑通即过"）；**测试回收**＝把在办任务里为躲本缺陷换形状的格子（`ScriptModeStatementConformanceTests.vb` 体内两格原用 `ToString`，现用 `Me.字段` / `MyClass.字段`）恢复成 `ToString` 形状，**但恢复后的期望（零诊断 / 某条诊断）必须由 S-F02a 实测决定，不得预设"正例"**；且该两格注释里的 BC30456 断言属**转述未实测**，须一并改写 |
| S-F04 | 回归 | 七门 gate + L2 全量 `-automated` + **重建发布版宿主**跑 §三 全部形状（此时才有真探针证据） |
| S-F05 | 账本 | issue 29 转 Fixed（commit 不得预填）；`upstream-merge.md` 入账；如动到符号层，登记分歧并写复核指引 |

### 非范围
- 不改 `script-class-explicit-me-scope` 已冻结的显式关键字判据（同函数不得回退）。
- 不顺手改解析门（另一在办任务 `auto-property-top-level-gate` 的面）。
- 不改 REPL 打印/格式化通路、不改 `Script<T>` 宿主契约。

## 三、行为契约（改判后；"对等"= 与 C# 实际策略同形）

1. **两类脚本类都不暴露 `Object` 继承成员**：提交类与非提交脚本类（`DeclarationKind.Submission` / `DeclarationKind.Script`）在符号层都报告无基类型；成员体内 `Me.ToString()`、`Me.GetType()`、`MyClass.ToString()`、`MyBase.X` 一律取不到 `Object` 成员。VB 现状只在提交类上成立 ⇒ **收严 `ImplicitNamedTypeSymbol.vb:51-60` 的判据到 `IsScriptClass`**，与 `Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57` 同形。
2. **回退兜底**：删掉 F01 为 `MyBase` 加的 `GetBaseTypeOfScriptClass` 接入（`Binding\Binder_Expressions.vb` 的 `BindMyBaseExpression` 成功分支），并回退其四处断言（`ExplicitMyBaseInTopLevelFunctionBody_BindsToSystemObject`、`ExplicitMyBaseInScriptFunctionBody_RunsAndPrintsTheScriptClassName`、`TopLevelMyBaseInInstanceMethod_BindsToSystemObject`，以及 L2 里 `Me.字段`/`MyClass.字段` 换形状格按 §五 回收）。
3. **回退后的诊断真值不许猜**：`MyBase` 在无基类型的脚本类成员体内到底报哪条诊断、消息是否诚实，由 S-F01b 的 **U2** 实测/读码定；若与 C# 不同形再单列修诊断（诊断文案属另一条账，不得顺手新造诊断码）。
4. **发射面与断言不动**：符号层无基类型、发射仍写 `Object` 基 + `Debug.Assert(baseType Is Nothing)` 这一分裂与 C# 同形（C# 的 `NamedTypeSymbolAdapter.cs:286-303` 同构），伴随点的处置由 S-F01b 的 **U3** 逐条给「跟着改 / 保持 / 加断言」。
5. **反例锁**：普通编译（`SourceCodeKind.Regular`）与脚本内声明的普通类**零变化**（普通类里 `Me.ToString()` 照旧合法——这是"脚本类特殊"与"普通类普通"的分界，不得被第 1 条外溢）；`Overrides` / 可覆盖性 / 接口实现查找不得因收严出现新诊断或新合法形状（S-F01 清点已点名 `OverrideHidingHelper`、`OverriddenMembersResult` 面）。
6. **不再重开**：成员体内 `Me.ToString()` 取不到 = 预期行为；"为让它可用而改查找回合"的甲/乙两案已废。

## 四、C# 对照（已完成部分与剩余项）
1. **已实锤**（不必重做，引用即可）：C# 符号层 `IsScriptClass ? null : Object`；`ImplicitClassTests.cs:63` 非提交脚本类也断言无基类型、`:76` 裸 `ToString` 无符号；发射侧补 `Object` + 断言 null。
2. **待补**（S-F01b）：C# 里 `base.ToString()` / `this.X`（X 为 `Object` 成员）在脚本成员体内的**诊断码与消息原文**；据此定 U2/U3/U4。
3. 产出仍是「VB 现状 / C# 做法 / 采用形态 / 若有分叉的理由」表，全部引两侧 `文件:行`。

## 五、旧约束「禁止改 `ImplicitNamedTypeSymbol.vb:59`」的处置（已消解）
该禁令写于没有消费点清点时，且方向也判反了：真正要动的**不是**给提交类换成 `Object` 基，而是把 `Nothing` 的**适用范围**从 `TypeKind.Submission` 扩到 `IsScriptClass`。禁令作废，代之以 §三 第 4、5 条的锁（发射面不动、普通类不外溢）。

## 六、串行与并发约束
与 `script-class-explicit-me-scope`（F02 验证中、F03 待跑）和 `auto-property-top-level-gate`（AG-F01 取证中）**不得并发构建/跑测试**（同批 dll 会 BC2012）。本任务只读取证先行；实现者等前两者的构建面腾空再派。风险与停止上报：若清点显示"提交类无基类型"被 ≥3 处实质依赖，或修复必须动 `Conversions`/`Emit` 层 ⇒ 停手交复核，不硬改。

## 五、范围重开（回退已落地之后）

前置变化：`script-class-explicit-keyword-parity-revert` 已把显式 `Me`/`MyClass`/`MyBase` 恢复成"整个脚本类禁用/按 C# 同形"，`MyBase`→`Object` 兜底已删 ⇒ 本任务原 S-F02「让 `Me`/`MyClass` 接收者解析到 `Object` 继承成员」**不再可达**（显式形状先被 BC36966 拒），本任务的 F01/F02 目标随之作废。

剩下只有两问，且都**只加测试、不改产品码**：

| # | 问题 | 已知 | 待测 |
|---|---|---|---|
| V-A | **裸名**（无显式接收者）取 `Object` 继承成员：`ToString()` / `GetType()` / `GetHashCode()` 在**提交类**（`TypeKind.Submission`）vs **非提交脚本类**（`DeclarationKind.Script` → `TypeKind.Class`）vs **脚本内普通类** 三容器下的诊断与取值 | `ImplicitNamedTypeSymbol.vb:51-60` 仅对 `TypeKind.Submission` 返回 `Nothing` 基类型 ⇒ 三层继承查找在提交类里到不了 `Object`；但 issue 29 的 `R1`–`R6` 档 2 实测显示"体内 `Me.ToString()`"**不复现**（那形状先撞 BC36966），裸名面**从未实测** | 三容器 × {顶层代码 / 实例成员体内 / `Shared` 成员体内} 的真值表；若裸名也全部可达 ⇒ issue 29 的失败面判为"不可达"并转 Not A Bug |
| V-B | `ImplicitNamedTypeSymbol.vb:59` 的判据（VB 按 `TypeKind.Submission`、C# 按 `IsScriptClass`）**是否收严** | 收严的代价已量化：C# 另有 `TypeSymbolExtensions.cs:226-232` 的走查兜底，VB 无 ⇒ 单改判据会让 **VB 比 C# 更严**（非提交脚本类也查不到 `Object` 成员，而现状能查到） | 先做 V-A 的真值表；仅当读数显示"非提交脚本类因此比 C# 多出一批可达成员"才立改判片段，并把 C# 兜底是否一并移植写进判据 |

- **禁止**：在 V-A/V-B 读数出来前改 `:59` 的返回值或给查找层挂 `Object`（两者都会把未证实的假设做成语义）。
- 并发：本片段只读 + 只加测试；构建面独占规则照旧（同时刻只有一个重建者）。
