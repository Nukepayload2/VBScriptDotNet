# 任务：脚本类显式 `Me` / `MyBase` / `MyClass` 禁令范围收窄（script-class-explicit-me-scope）——任务计划

> **状态：F01 / F02 / F03 已完成，F04（账本）待做**（实测：七门 Failed=0 且等式成立 **19882 = 19865 + 17**，Semantic 门 5843/5739/104/0，其余六门逐字等于基线；`Scripting\VisualBasicTest` 全量 **732/0** ×3、`*CrashTests*` 44/0；重建发布版宿主成功并复跑 23 格探针，7 格按判据变化、A/J/L 仍 BC36966、O1/O2 仍 BC30188（属 `auto-property-top-level-gate`）、新探针 `P`=`42`（遮蔽逃生口恢复）、`Q`=`Submission#0`。`scripts\verify-vb-compiler-tests.ps1` 的 Semantic 基线现为 5843/5739/104/0。流水账：`07-verifier-f03.md`）

> **⚠ 改判横幅（2026-09-22，实施中途）**：本任务判据里「**`MyBase` 在成员体内按 `System.Object` 解析**」一项已被 C# 实测推翻——C# 刻意让脚本类无基类型，注释点名是为了不让它继承 `ToString` / `GetHashCode`（`Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57`；`Compilers\CSharp\Test\Symbol\Symbols\ImplicitClassTests.cs:63`、`:76`），作者裁定「冲突按 C# 实际策略定，`MyBase` 取到 `Nothing` 是预期」。**其余三条判据不受影响，继续实施**。已落的 `MyBase` 兜底 + 四处测试（`ExplicitMyBaseInTopLevelFunctionBody_BindsToSystemObject`、`ExplicitMyBaseInScriptFunctionBody_RunsAndPrintsTheScriptClassName`、`TopLevelMyBaseInInstanceMethod_BindsToSystemObject` 及 L2 换形状格）**由 `tasks\submission-object-member-lookup\` 回退**，规范措辞已先行改判（`spec\spec-scripting-dialect.md:246`）。本任务 §一 F01 行、§二 判据 4、`test-plan.md` 的 D1 / E2 / H1 单元格按此读。

> **✅ 第二处冲突已裁定（2026-09-23，作者）＝「照 C# 回退」**：C# 对显式 `this` / `base` 的门是**关键字级**且**覆盖成员体**——`Compilers\CSharp\Portable\Binder\Binder_Expressions.cs:55-73` 逐字 `return !inTopLevelScriptMember || !isExplicit;`（`BindBase` `:2636-2639` CS1512、显式 `this.X` CS0027；作者用 `Microsoft (R) Visual C# 交互窗口编译器 5.10.0-1.26380.3` 实跑 `void test() { this.ToString(); }` → `(1,15): error CS0027`，`SemanticErrorTests.cs:1365` 的相似行只作旁证）。⇒ **本任务的核心收窄（成员体允许显式 `Me`/`MyClass`）判为改判错误，不作为功能提交**；被放弃的收益（遮蔽逃生口，探针 `P`=`42`）由作者明示接受。实施转为 `tasks\script-class-explicit-keyword-parity-revert\`（**部分回退**：`Shared` 落 BC30043/BC30369 的次序**保留**，因为 C# 的静态检查同样排在脚本门之前；`MyBase`→`Object` 兜底的删除**保留**）。**本任务的 F04 账本随之改写**：issue 28 记 **Not A Bug（撤销改判）**而非 Fixed，`upstream-merge.md:280` 登记「显式路径保持上游判据 + 共享诊断在先」，`spec` 与 `zh-CN` 已按新裁定改写。

- **一句话**：把 BC36966 对显式 `Me` / `MyBase` / `MyClass` 的禁令从「容纳类型是脚本类」收窄到「顶层脚本代码」，让顶层 `Sub` / `Function` / `Property` 的成员体回到普通类语义，并把 `Shared` 成员里的显式引用交回普通判据 BC30043 / BC30369。
- **依据链（人的登记，非 agent 输入）**：证据与根因记在 `..\..\issues\issue-script-class-explicit-me-in-member-bodies.md`（issue 28：16 探针实测、`MyBase` 定案、陷阱与回归清单）；`..\..\issues\` 属人工维护区，**子 agent 禁止阅读**。判定采纳见 `..\..\meetings\meeting-scripting-dialect.md:51-80`、`:182`；在册的上游分歧条目见 `..\..\upstream-merge.md:280`。
- **实施所需信息自足**：判据全文在 §二，形状矩阵与 pass 条件在 `test-plan.md` §一 / §二，两条陷阱在 §二硬约束——本任务的文件不依赖 `issues\` 正文即可执行。
- **规范侧口径**：`..\..\spec\spec-scripting-dialect.md:234`、`:246-270` 已按判据写好，本任务让实现兑现，不得反向改规范。
- **调度方式**：Vortex 涡流。实施者 agent 产出 → 验证者 agent 按本计划 pass 条件核对 → 打回修复 → 通过关闭；main 只调度与审计，不改代码、不催促。每步一个日志文件。
- **作者给定的判定原则（沿用 issue 层）**：同形状放进普通类若合法，脚本类里也应合法；诊断不得对位置或原因说谎。

---

## 一、范围与非范围

### 范围内

| # | 单元 | 落点 |
|---|---|---|
| F01 | 判据收窄 + `Shared` 交回普通判据 + `MyBase` 按 `System.Object` | `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:2257-2289`（`CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext`），必要时同文件 `BindMyBaseExpression` 的错误路径 |
| F02 | 既有断言同步 + 新增正/负例 | `ScriptSemanticsTests.vb:588`、`:713`、`:724`；`CodeGenScriptTests.vb:94-95`、`:115-116`；`ScriptModeStatementConformanceTests.vb:487-492`、`:497-502` 及其文档注释 `:483-484`；`ScriptModeParserArmConformanceTests.vb:167-169` 注释；`Scripting\VisualBasicTest\` 新增用例 |
| F03 | 回归面：编译器七门 gate + `Scripting\VisualBasicTest` 全量 + 宿主探针复跑 | `scripts\verify-vb-compiler-tests.ps1`；`Scripting\VisualBasicTest` 程序集直跑 `-automated`；重建 `Interactive\vbi` 发布版后复跑 `tmp\spec-check-me\` 的 16 个探针 |
| F04 | 账本与文档义务 | `..\..\upstream-merge.md:280` 条目改写（**显式**路径不再逐字保持 + 新判据 + issue 28 指针）；`..\..\issues\README.md` 行 28 与本 issue 状态转 **Fixed**（注明 commit，不得预填）；`..\..\issues\issue-script-class-explicit-me-in-member-bodies.md` §修复后会新增的行为变化 逐项落账 |

### 非范围（显式）

- **不改 `Return` 半条的覆盖面**：顶层 lambda 里的 `Return` 现状不受 `BindingTopLevelScriptCode` 把守（issue 28 §两条附带观察），那是另一条缺陷，本任务不顺手改。
- **不动 `ImplicitNamedTypeSymbol.vb:59`**：提交类基类型的 `Nothing` 是刻意选择，`MyBase` 的修正在**绑定路径**上做（issue 28 §`MyBase` 的基类型）。
- **不改 `ERR_BadAwaitInSharedInitializer`（BC37341）的「容纳类型是脚本类」形状**：那条本就要覆盖整个脚本类。
- **不新增诊断码、不改 `VBResources.resx` 与 13 份 xlf**：现有消息「You cannot use '{0}' in top-level script code」在新范围下逐字准确。
- **不改 `spec\` / `meetings\` / `proposals\` 正文**：规范已写好，本任务只让实现兑现；文档修订义务只落在 `upstream-merge.md` 与 issue 状态。
- **不引入 `Module` 化 / 顶层成员默认 `Shared` / 容器种类变更**：`meetings\inactive\meeting-top-level-implicit-shared.md` 方向不变。

---

## 二、判据（F01 的实现边界，逐条可验）

1. **顶层脚本代码里仍禁**：顶层语句、脚本类的字段/属性初始化器、以及写在二者中的 lambda 与查询表达式 → BC36966。
2. **成员体内放行**：顶层 `Sub` / `Function` / `Property`（含 `Property` 访问器）体内、以及写在其中的 lambda 里的显式三关键字 → 普通类语义。
3. **`Shared` 成员不豁免**：显式引用 → BC30043；隐式引用 → BC30369。BC36966 不得顶掉这两条。
4. **`MyBase` 对 `System.Object` 解析**：与无 `Inherits` 子句的普通类一致。

**两条硬约束（issue 28 已实锤，照抄现成谓词会破）**：
- `Binder.BindingTopLevelScriptCode`（`Binder.vb:428-443`）**不能直接当判据用**：`ContainingMember` 在 lambda 体内是 `LambdaSymbol`、在字段/属性初始化器里是字段/属性符号（`DeclarationInitializerBinder.vb:49-58`），两处都落 `Case Else` → `False`。判据必须**上溯到最近的非 lambda 容纳成员**，并显式覆盖脚本类的字段/属性初始化器。
- 判据不得按词法位置或「源码树是否顶层树」实现——那样顶层 `Sub` 体内的 lambda（应放行）与顶层语句里的 lambda（应禁）无法区分。

## 三、批次与串行约束

F01 →（构建+定向测试）→ F02 →（新增用例与注释）→ F03（全量回归，只读验证）→ F04（文档与账本）。F01 / F02 同属一次编译器重建面，实施者可合并为一步产出但 pass 条件逐条判。禁止并行改同一批文件。

## 四、验证档位（Vortex「验证档位与计划报告」）

| 单元 | 目标档位 | 手段 | 证据 |
|---|---|---|---|
| F01 | **档 1** | 编译器语义测试断言（内存编译，零副作用） | 已运行 |
| F02 | **档 1** | 新增/更新用例 + 七门 gate 与 `Scripting\VisualBasicTest` 全量 | 已运行 |
| F03 | **档 1 + 档 2** | 自动化全量（档 1）+ 重建发布版宿主复跑 16 探针（档 2 集成面，观察 stdout/诊断文本） | 已运行 / 有结果支撑 |
| F04 | **档 3** | 文档与账本逐条核对（无运行可断言） | 已检查 |

无副作用禁令仍然生效：不写盘（隔离临时目录外）、不联网、不起常驻进程；宿主探针复跑属既有允许的集成验证面，跑完即退出。

## 五、风险、回滚与停止上报条件

- **风险面**：改的是上游同名文件的同名函数 ⇒ 与上游 VB 编译器的第 N 处分歧，必须入 `upstream-merge.md`（F04）。
- **回归面**：判据上溯 lambda 若写宽了，会把「顶层语句里的 lambda」放开（探针 J 是这条的报警线）；写窄了会把 `Shared` 成员的 BC30043/BC30369 让位给 BC36966（探针 E / G1 是报警线）。
- **停止并上报**：若 `MyBase` 走绑定路径无法拿到 `System.Object`（例：`CanAccessMyBase` 之前的 `GetBaseType` 早退把类型判成 `Nothing` 并连带出 UseSiteInfo 噪声），或任一 gate 出现无法归因于本改动的既有失败 ⇒ 停手上报，不猜、不用低档证据冒充档 1、不为过 gate 而放宽判据。
- **回滚**：改动集中在一个函数 + 测试断言，`git revert` 单提交即可；无产物形状变化（不发新码、不动元数据）。

## 六、文件索引

| 文件 | 内容 |
|---|---|
| `README.md` | 本文件：范围、判据、批次、档位、风险 |
| `test-plan.md` | 逐单元验收矩阵（形状 × 期望 × 层次） |
