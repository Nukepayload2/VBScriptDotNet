# 验收矩阵（script-class-explicit-keyword-parity-revert）

判据见 `README.md` §一（K1–K12 编号在此复用）。**子 agent 禁读 `..\..\issues\`**。每格标三态。

## 〇、起点事实（当前工作树 = 待回退状态，档 1 已跑过一轮）

`Binder_Expressions.vb` 现状：BC36966 只在 `IsBindingTopLevelScriptCode()` 为真时开火；`BindMyBaseExpression` 成功分支走 `GetBaseTypeOfScriptClass()`（脚本类基类型缺失时给 `System.Object`）；`CanAccessMyBase` 的断言被放宽为 `IsClassType OrElse IsScriptClass`。实测（同工作树，F03 验证）：成员体内显式关键字**零诊断**、宿主 `R1`/`R2`/`R4`/`R5`/`R6` 打印 `Submission#0`、`P`=`42`（遮蔽逃生口）。**这些正是本任务要变红的格**。C# 侧判据为档 2（作者 csi 5.10.0 实跑 `void test() { this.ToString(); }` → `(1,15): error CS0027`）。

## 一、逐格验收（档 1，除注明外全在 `ScriptSemanticsTests.vb` 的 `Top level scripts: scope of the explicit …` region 内改写）

| 格 | 形状 | 回退后期望 | 现状 |
|---|---|---|---|
| K1 | 顶层 `Function` 体内 `Return Me.counter` | BC36966，波浪线 `Me` | 零诊断 ⇒ 必须翻 |
| K2 | 同 K1 用 `MyClass.counter` | BC36966 | 零诊断 ⇒ 必须翻 |
| K3 | 顶层 `Sub` 体内 `Console.WriteLine(MyBase.ToString())` | BC36966 | 绑到 `Object` ⇒ 必须翻 |
| K4 | 顶层 `Property` 的 `Get` 体内 `Return Me.x` | BC36966 | 零诊断 ⇒ 必须翻 |
| K5 | 成员体内 lambda 里的显式 `Me` | BC36966 | 零诊断 ⇒ 必须翻 |
| K6 | 顶层语句 / 顶层字段与属性初始化器 / 写在这二者中的 lambda | BC36966（**不得变松**，A1–A5 原样保留） | 已如此 |
| K7 | `Shared Function` 体内显式 `Me` | **BC30043**（不回退，C1 保留） | 已如此 |
| K8 | `Shared Sub` 体内隐式读实例字段 | **BC30369**（C2 保留） | 已如此 |
| K9 | `Shared Function` 体内 `MyBase.X` / `MyClass.X` | **BC30043**（C3 保留） | 已如此 |
| K10 | 成员体内**不限定**调用顶层方法 / 读未遮蔽顶层字段 | 零诊断 + **取到具体值**（隐式侧不变） | 已如此 |
| K11 | 脚本里声明的普通 `Class` 体内三关键字 | 零诊断并运行（D2 保留） | 已如此 |
| K12 | `SourceCodeKind.Regular` 普通类 `Me.x & MyClass.x & MyBase.ToString()` | 逐字不变，运行输出 `11C`（D4 保留） | 已如此 |

## 二、既有断言同步（回退后必须逐条对账，不得整删）

| 站点 | 现值 | 回退后 |
|---|---|---|
| `ScriptSemanticsTests.vb` 的 `TopLevelExplicitMe_ReportsKeywordNotAllowedInScript`（原 `:588` 一带） | 已翻成正向/改名 | **改回**拒绝侧（体内 `Me.sx` → BC36966），可保留改名后的形状，期望须是拒绝 |
| `TopLevelMyBaseInInstanceMethod_*` | 零诊断 + `SpecialType.System_Object` | 改回 **BC36966**（`MyBase` 兜底删除 ⇒ 该断言失去对象） |
| `TopLevelMyBaseInSharedMethod_*` | BC30043 | **保留 BC30043**（K9 与 C# 同形），只删注释里"成员体放行"的旧叙述 |
| `ExplicitMyBase*` 新增两格（`_BindsToSystemObject` / `_RunsAndPrintsTheScriptClassName`） | 正向 | 删除或改写成"成员体内 `MyBase` 被 BC36966 拒"的负格；`/script` 非交互类里 `MyBase` 是否合法按普通类判据另测 |
| `CodeGenScriptTests.vb` `MeKeyword` / `MyBaseAndMyClassKeyword` | 体内那条已转正向 | **回到每形两条 BC36966**（上游原期望），并在注释里指 D7 裁定 |
| `ScriptModeStatementConformanceTests.vb:480/:494/:508`、`:620` | 体内两格被换成 `Me.字段` / `MyClass.字段`，注释含"BC30456"转述 | 回收成 **体内显式关键字 → BC36966** 的形状；`:478` 顶层那条不动 |
| `ScriptModeParserArmConformanceTests.vb:166-175` | 注释已改写 | 注释再改一次：范围是"脚本类内"，不是"顶层代码" |
| `ScriptTopLevelCrashTests.vb:396` summary | "in the top-level code of a submission class" | 改回"in a submission class"（措辞随判据） |
| `ScriptSemanticsTests.vb:1257-1272`（A3，含刚回收的"属性 + 后随语句"形状） | 期望 BC36966 + 语句在场 | **保留语句**（那是 `auto-property-top-level-gate` 的成果），期望不变 |

## 三、代码级核对（档 3，`git diff` 级）
1. `GetNearestNonLambdaContainingMember` / `IsBindingTopLevelScriptCode` 若无消费者 ⇒ 删除，不留死代码；`GetBaseTypeOfScriptClass` 与 `BindMyBaseExpression` 的兜底调用移除。
2. `CanAccessMyBase` 断言恢复为 `ContainingType.IsClassType`，并**实测**确认回退后没有任何路径能撞上它（撞上 ⇒ 停手上报，不得反过来放宽）。
3. `Symbols\Source\ImplicitNamedTypeSymbol.vb` **不在**改动列表内（提交类符号层无基类型 = C# 同形，预期）。
4. 解析器三文件（`Parser\Parser.vb`、`BlockContext.vb`、`PropertyBlockContext.vb`）的 hunk **只属于** `auto-property-top-level-gate`，本任务不得触碰、不得混提。

## 四、回归与探针
1. 七门 gate Failed=0；基线以 `scripts\verify-vb-compiler-tests.ps1` 内记录为权威 ⇒ 逐门报实测四元组交 main（**不要自己改基线**）。
2. `Scripting\VisualBasicTest` 直跑带 `-automated`（MTP：`dotnet test` 会静默跑 0 个）。
3. 重建发布版宿主复跑 `tmp\spec-check-me\`：`R1`–`R6`、`B`、`D2`、`E@28`、`F`、`I`、`P` 由"零诊断/取值"变 **BC36966**；`A`/`J`/`L`/`C`/`G1`/`G2`/`K`/`M`/`N`/`O*`/`Q` 读数逐格不变；`E@24` 仍 BC30043。逐格列名，不许用总数代替。

## 五、终局判据
1. K1–K12 全绿且有值断言或精确诊断（含波浪线目标）；§二 每条既有断言都有落点（改写或删除都要写明理由），无一条"整删了事"。
2. §三 四条 diff 级证据齐；§四 三档全绿。
3. 账本：issue 28 记 **Not A Bug（撤销改判）**、`upstream-merge.md:280` 改写为「显式路径保持上游判据 + 共享诊断在先」、D7 第二实例已落（不需再写）；commit 号不预填。
