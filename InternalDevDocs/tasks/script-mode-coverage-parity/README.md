# 任务：脚本模式测试覆盖对齐 C# 与 VB 语法完整覆盖（script-mode-coverage-parity）——任务计划

> **状态：计划已产出，待独立审核后进入实施**。本文件夹是**四件套**：本 README（目标 / 判定表 / 范围 / 分批 / Vortex 代办 / 探针清单 / 依据链）+ `design-overview.md`（总体设计 + 全称主张剪枝自检）+ `design-detailed.md`（逐单元改动蓝图 + pass 条件）+ `test-plan.md`（L1–L4 矩阵 + 无副作用纪律 + 全量回归口径）。

- **一句话**：把**脚本模式（`.vbx` 文件执行 + vbi 交互/REPL）**的测试网，从「C# 侧有、VB 侧没有」的缺口面补齐到 **C# 同等或更高**，并把 **VB 特有语法**在脚本容器里的覆盖面拉到**可判定的完整**——两者的达成证据分别是「缺口矩阵全行闭口」与「语法 ledger 无 `缺口` 状态行」。
- **结束条件（用户给定，是本任务的唯一验收判据）**：
  1. **覆盖程度**达到 C# 同等水平或超过；
  2. **语法级别完整覆盖**。
- **作者给定的判定原则（沿用前两轮，仍是本任务的唯一行为判据）**：
  > 崩编译器是 bug。要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」。
  执行形式：**同形状放进普通编译上下文实测**——合法 ⇒ 走「修好」；报错 ⇒ 走「报错」并复用同一条诊断（或按已采纳会议决议的新码）。
- **与既有任务的关系**：第一轮 `tasks\script-top-level-crashes\`（commit `48d8edb`）与第二轮 `tasks\script-top-level-crashes-2\`（`d7b0fc8` / `7edb77d`）已把「顶层形状崩溃族」逐条判定收口，并在第二轮 U7 建立了符合性矩阵的**骨架**（`ScriptModeConformanceTests.vb` / `...StatementConformanceTests.vb` / `...SubmissionConformanceTests.vb`，三维共 91 个用例）。本任务是**第三轮**：不再以「已知崩溃族」为起点，而是以**C# 侧测试族谱**（缺口矩阵，§三）与**VB 语法清单**（ledger，§四）两张网为起点，把矩阵补成一张完整的网。
- **依据链**：`..\..\decisions.md` **D5**（基础功能以 C#/csi 实现为设计蓝本）→ `..\..\spec\spec-scripting-dialect.md`（脚本方言的规范面与 Testing 节）→ `..\..\compilers-index.md`（源码地图）→ 第二轮 `tasks\script-top-level-crashes-2\`（判定原则、探针方法、Vortex 流水账格式、`test-plan.md`§6 的 C# 族谱雏形）→ `{{Roslyn}}\src\Scripting\`（C# 基线：`CSharpTest` + `CoreTest`）→ `{{VBScriptDotNet}}\InternalDevDocs\vblang\spec\`（语法清单权威来源）。
- **调度方式**：Vortex 涡流（实施者 agent 按代办产出 → 验证者 agent 按 pass 条件核对 → 打回修复 → 通过关闭），main 只调度、串行交替、不可催促。流水账 `<项目根>/tmp/vortex-logs/script-mode-coverage-parity/`。经验沉淀走 `ItemMix/VortexStorageLearning.md`。

---

## 一、范围与非范围

### 范围内

| # | 块 | 内容 | 达成证据 |
|---|---|---|---|
| A | **误报澄清 + 分歧登记** | 本轮探针初报的三条缺陷**已逐条推翻**（§二 A 组）；唯一真问题（顶层不推断，D5 分歧）登记交用户裁决 | 每条推翻的实测对照（已在 §二 表中）+ 分歧的 `issues\` / `OPEN QUESTIONS` 条目 |
| B | **C# 缺口对标** | 缺口矩阵（§三）中判为**高 / 中**价值的族逐族补测；判为**低**的族显式记「不适用 + 理由 + 检索证据」 | 矩阵每一行的「VB 侧覆盖」列不再是「缺」，或该行有「不适用」结论文档 |
| C | **VB 特有语法完整覆盖** | 按 spec ∪ parser 分派 arm ∪ fork 指令三源建 **ledger**（§四），逐项标覆盖状态并填缺口 | **判据 ①②③ 同时成立**（§四「「完整覆盖」的判定」）：行集完备（`SyntaxKind` 反查**全量差集为空**）、`已覆盖` 只认真实测试方法、分母固定 |
| D | **收口** | 七门 gate + Scripting 程序集全跑 + 资源与文档同步 | 全绿；`issues\README.md` 状态列、`spec-scripting-dialect.md` Testing 节同步 |

### 非范围（显式）

- **不改容器种类**：脚本/提交类保持 `TypeKind.Submission` / 非提交脚本类的 `TypeKind.Class`（`meeting-script-extension-methods.md` R21 已裁）。
- **不给共享发射层加守卫**：`Compilers\Core\Portable\CodeGen\` 不出现 VB 形状知识。
- **不为「C# 有、VB 没有」的语言特性造 VB 实现**：矩阵中属「C# 专有语法」的行（`;` 结尾解析、`switch` 表达式、模式变量、`class partial;`、`extern alias`、`Script` 类型名冲突）记为不适用，**不实现**。本任务补的是**测试**，不是特性。
- **不动 `meetings\` / `proposals\`**：本任务若需新增诊断码，走 `issues\` + 既有新码流程；若某单元需要推翻既有 RESOLUTION，该单元**停手上报**，不自行改判。
- **不评估 csi fork 维护面**（沿用 `VBNetScriptMaintainer` 口径）。
- **重命名/搬移既有任务文件夹**：不改。

### 已知空白（本任务不承诺闭合，登记为探索项）

第二轮 `design-detailed.md` §U7「本任务的已知空白」列出的 `End` / `Stop`（不可进程内跑）——本轮沿用第二轮的处置（`AssertEmits` 只验发射），并记录其**不可测的边界**，不强行构造进程内方案。

---

## 二、判定表

> 判定依据 = **同形状在普通编译上下文里的实测行为**。普通上下文一律 `vbc.exe` 编译普通程序（`Module Entry / Sub Main`），脚本上下文一律 `vbi.exe <file>.vbx` 直跑。三态按 `manifest.md`「断言状态」标注。

### A 组 · 本轮探针初报的缺陷（**已逐条推翻**，保留作为判定纪律的记录）

> **重要**：本组三条**都不是编译器缺陷**。初步结论被后续对照推翻，推翻过程见 `design-overview.md` §3。保留在此是因为**推翻过程本身是本任务的证据样本**：三轮探针的对照组一次比一次严格，前两轮的「对照失败」都被误读成了「缺陷成立」。

| # | 形状（脚本顶层） | 初报症状 | **最终判定** | 依据 |
|---|---|---|---|---|
| **A1** | `Dim q = From w In words Group By k = w.Length Into g = Group Select k, c = g.Count()`（`words As String()`） | `InvalidCastException`（exit `2147500034`）；初判「脚本顶层容器特有」 | **非缺陷**：判别点是 **`Option Infer`**，不是容器。顶层 `Dim q = …` 不推断 ⇒ `q` 是 `Object` 字段 ⇒ 对 `Object` 的 `.Select(...)` 走晚期绑定 ⇒ 晚期绑定器对该匿名类型形状找不到 `Select` 重载 | **实锤（已运行）**：普通模式不开推断时**抛同一条异常**（`r5/pin.py` 的 `module-shared-sub-main`，exit `3762504530`；容器无关）；普通模式**开推断后成功**（`r5/decisive.py` 的 `ord-optioninfer-on`，exit 0，`G=2:2,1:1,3:1`）；脚本顶层开推断后仍以**晚期绑定**消息失败 ⇒ 分歧只在「推断是否生效」 |
| **A2** | `Into g = Group Select g` 后对 `q` 取 `.Count()` | `MissingMemberException`（exit `2148734226`） | **非缺陷**：与 A1 同一机制（`q` 是 `Object` ⇒ 晚期绑定 ⇒ 找不到成员） | 同上；`script-top-object-receiver-array`（`Dim o = arr` 后 `o.Length`）exit 0 证明晚期绑定本身可用 |
| **A3** | 顶层 `Dim values = {1, 2, 3, 4}` 后 `values.GetType().Name` | 探针输出 `Int32[]` | **非缺陷（探针不具鉴别力）**：晚期绑定调 `Object.GetType()` 返回的是**运行时类型**，`Int32[]` 不能区分「字段是 `Object`」与「字段是 `Int32()`」 | **实锤（已运行）**：`r5/field_infer.py` 的 `script-top-infer-linq` —— 编译器自己报 **`BC36593`：表达式「`Object`」不可查询**，直接证明 `values` 是 `Object`。与既有用例 `ScriptModeStatementConformanceTests.vb:606`（用**重载决议**判别，得 `object`）一致 |

### 真问题 · D5 分歧（**不是崩溃，交用户裁决**）

**实锤（已运行）**：脚本顶层 `Dim x = <expr>` **不推断类型**（`r5/field_infer.py` 的 `script-top-infer-linq` 得 `BC36593`，编译器自报 `Object`）；而 **C# 脚本的顶层 `var x = <expr>` 推断**。

按 **D5**（基础功能以 C#/csi 实现为蓝本），这是一处分歧。但其性质是**能力/语义分歧**，且**可能是有意**（VB 字段类型推断规则本就不如 `var` 宽松；`ScriptModeStatementConformanceTests.vb:606` / `:623` 已把它当既定行为测）。**本任务不改该行为**，只：① 把后果补成用例；② 登记为 `issues\` 条目或 `OPEN QUESTIONS` 交用户裁决。

### B 组 · 覆盖缺口（无行为判定，只有「补」）

见 §三 矩阵。B 组不产生行为判定，只产生用例与 ledger 行。

---

## 三、C# 缺口矩阵（B 组的工作面）

**矩阵全文见 `design-detailed.md` §B**（按族逐行，含 C# 基线 `文件:行号`、VB 侧覆盖 `文件:行号`、缺口性质、VB 侧适用性）。本 README 只登记**统计口径**与**族的价值分档**，避免两处维护。

### 口径

| 项 | C# 基线（`{{Roslyn}}\src\Scripting\`） | 本 fork（`Scripting\VisualBasicTest\`） |
|---|---|---|
| 统计范围 | `CSharpTest\*.cs` + `CoreTest\*.cs` 的 `[Fact]`/`[Theory]`/`[ConditionalFact]`/`[ConditionalTheory]` | 同目录 `*.vb` 的 `<Fact>`/`<Theory>` |
| 计数值 | **265 个 `[Fact]`/`[Theory]` + 38 个 `[Conditional*]` = 303**（逐文件计数见 `design-detailed.md` §B.0） | **451 个属性**（21 个文件）；含 `<Fact, WorkItem(...)>` 形式时为 **454**；`Helpers\*.vb` 的 `<Fact>` 计数实测为 **0**（故「451 减 Helpers 与夹具」这一步的减法对象是**测试类内的非 Fact 成员**，不是 Helpers） |
| 是否计入 Desktop 变体 | 否（`CSharpTest.Desktop\*` / `CoreTest.Desktop\*` 不在统计面） | 否（`VisualBasicTest.Desktop\` 只有 3 个用例） |

**口径的重要性**：本任务的结束条件之一是「达到 C# 同等或超过」，若不给口径就无法判定。上表的 VB 侧计数是**方法计数**，不是**覆盖率**——本任务不承诺「用例数超过 303」，承诺的是 **§三 矩阵的每一行闭口**（B 组）与 **§四 ledger 的三项合取判据**（C 组）。用例数只作**副产品指标**记录，不作验收判据。这一点是**剪枝结果**：以用例数当验收会诱导造冗余用例，与「覆盖缺口」的真实目标错位。

> **「达到 C# 同等或超过」如何判**：分母 = **适用族** = §B.2 的**高档 20 + 中档 27 = 47**（已覆盖的 13 已闭口、低档 8 与跨档 1 判为不适用，均不计入）。**判据 = 这 47 个族的矩阵行全部闭口**（「VB 侧覆盖」列不再有 `缺`/`完全缺`/`部分缺`）。比率与 C# 的方法计数**不作比较**——两侧测的轴不同（见 `design-overview.md` §1 网一）。
>
> **分母必须与 §B.1 矩阵的「适用性」列逐行对齐**：第一次修订曾把族 2（矩阵里标 `适用` + 「弱缺口」）错放进「低（不适用）」桶，等于把「适用且有缺口」的族排除出分母，会让判据**不可满足**。收口时须重新逐行核对 47 个族与矩阵 `适用性` 列一致。

### 分档

| 档 | 判据 | 处理 |
|---|---|---|
| **高** | VB 有对应语法概念 **且** 属脚本模式特有面（提交链 / 宿主对象 / 顶层声明 / 脚本宿主命令行） | 本任务必补 |
| **中** | VB 有对应概念，但普通编译模式也能测到 | 本任务必补（脚本容器下的形态） |
| **低** | C# 专有语法 / 本 fork 无对应实现 / 上游已禁用 | 记「不适用 + 理由 + 检索证据」，不补 |

分档的逐族落点在 `design-detailed.md` §B。**高 / 中档的族共 12 个**，对应 Vortex 单元 U4–U11。

---

## 四、VB 语法完整覆盖 ledger（C 组的工作面）

**ledger 全文见 `test-plan.md` §C**（放在 test-plan 而非 design-detailed，因为 ledger 就是「VB 特性维度」这张测试矩阵，正是 `VBNetScriptMaintainer` 对 `test-plan.md` 的验收要点之一；也是本任务唯一会被实施期持续改写的产物）。README 只定义**「完整覆盖」的可判定含义**与**枚举来源**。

### 枚举来源（权威性由来源文件本身保证）

| 维度 | 来源 | 用法 |
|---|---|---|
| 语句 | `InternalDevDocs\vblang\spec\statements.md`（`##`/`###`/`####` 标题树，实测 **43 个小节**：`##`×15 + `###`×20 + `####`×8，自 `## Control Flow` 到 `### Yield Statement`） | 每个小节一行 |
| 成员与声明 | `InternalDevDocs\vblang\spec\type-members.md`（实测 **40 个小节**：`##`×8 + `###`×24 + `####`×8，自 `## Interface Method Implementation` 到 `### Operator Mapping`） | 每个小节一行 |
| 表达式 | `InternalDevDocs\vblang\spec\expressions.md`（实测 **82 个小节**：`##`×26 + `###`×52 + `####`×4） | **全量出行**，逐行标 `不适用（与容器无关）` 并给理由——**不得在行集层面事先筛掉**（筛掉即缩小分母，判据③失效） |
| 字面量与词法 | `InternalDevDocs\vblang\spec\lexical-grammar.md`（实测 **18 个小节**：`##`×6 + `###`×12） | **全量出行**；与容器无关的标 `不适用` 并给理由 |
| 预处理 | `InternalDevDocs\vblang\spec\preprocessing-directives.md`（实测 `##`×4 + `###`×2） | **全量出行**；未实现的标 `不适用` 并给理由 |
| **实现分派 arm**（判据①的第二源） | **`Compilers\VisualBasic\Portable\Parser\Parser.vb:935-1272`**（语句主分派，**U2 实施期实锤修正**；`End Select` 在 `:1272`）＋ `ParseStatement.vb`（五个子分派在 `:24`/`:95`/`:487`/`:1198`/`:1418`：`Continue`/`Exit`/拟古/复合赋值/`While\|With\|SyncLock`）＋ `ParseExpression.vb`：以 `SyntaxKind` 关键字为 arm 的 `Select Case` 分派 | 逐个 arm 出行；**这是「实现实际支持什么」的机械枚举**，补 spec 不含的 fork 新增语法 |
| **fork 新增指令**（判据①的第三源） | 本 fork 的 `#Load`/`#R`/`#!`/`#`（spec 是 `{{VbLang}}` 镜像，**不含** fork 新增语法，故必须单独列源） | 逐个出行 |

> **为什么必须三源并集**：spec 标题树给的是**规范性命名与分组**（权威、可读），`Parser` 分派 arm 给的是**实现完备性**（机械可枚举、不可能漏），fork 指令补 spec 镜像的空白。任取其一都有缺口——只取 spec 会漏 fork 新增语法（`ShebangDirectiveTriviaSyntax.vb` 就在仓库里却不在 spec 中）；只取 parser 会得到一堆无语义分组的 `BadStatement` 类 kind（这正是 `design-overview.md` §2 反对 `SyntaxKind` **直接**当行的理由，但它不否定**用分派 arm 做差集完备性检查**）。
>
> **U11 的 `SyntaxKind` 反查是全量差集，不是抽样**：差集非空 ⇒ 新增 ledger 行。

> 上表的计数命令（**实锤**，本轮已跑）：`for f in statements.md type-members.md expressions.md lexical-grammar.md preprocessing-directives.md; do echo "$f $(grep -c '^## ' $f) $(grep -c '^### ' $f) $(grep -c '^#### ' $f)"; done`，工作目录 `InternalDevDocs\vblang\spec\`。**实施期须重跑核对**并把命令与读数写进 ledger 头部；spec 是 `{{VbLang}}` 镜像，上游更新会改计数。
>
> **标题并非都是语法构造**（**实锤**）：`statements.md` 的 `#### Mutable structures in async and iterator methods`（语义说明）、`### Regular/Iterator/Async Methods`（方法风味）、`#### Finally Blocks` / `#### Catch Blocks`（子块）——这类行照出，状态按「与容器无关」或「由宿主小节覆盖」标 `不适用` 并给理由，**不删行**。

### 两栏的**精确定义**（判据的基石，不得含糊）

> **本定义先于一切标注**。口径含糊会让「`缺口` 归零」这个唯一验收判据失去意义——U2 建表前必须先确认本节。

| 栏 | **精确定义** | 判别方法 |
|---|---|---|
| **顶层容器** | 该语法出现的位置是**脚本单元的顶层**：既包括顶层语句，也包括**直接写在脚本单元里的声明**（即使该声明本身是一个 `Class`/`Structure`/`Enum`/`Partial` 声明——**声明嵌套在提交类里，但对源文件而言它是顶层声明**） | 源码字符串里该构造**不在**任何 `Sub`/`Function`/`Property` 访问器体内 ⇒ 顶层容器 |
| **嵌套容器** | 同一语法写在**脚本单元内的方法体里**（`Sub`/`Function`/lambda/访问器体），或在脚本里声明的类型**再嵌套一层** | 源码字符串里该构造**在**某个方法体内 ⇒ 嵌套容器 |

**关键澄清（本清单最常见的误标来源）**：`NestedTypeDeclarations_Conform`（`ScriptModeConformanceTests.vb:570`）、`NestedPartialTypes_Conform`（`:655`）名字里的 "Nested" 指**该类型嵌套在提交类里**——但从**源文件位置**看，它们**是顶层声明**，属**顶层容器**。同理 `OptionStrictOn_TakesEffect`（`ScriptModeStatementConformanceTests.vb:524`）的源码整段写在顶层，属顶层容器。

**补充澄清 · 脚本内声明类型的「成员位」（main 裁定，U2 修复轮提出）**：语法写在**脚本单元里声明的类型的成员里**（如

```vbnet
Class Holder
    Public Sub Go()
        <语法在此>
    End Sub
End Class
```

）时，**属嵌套容器**——因为该语法的宿主是**方法体**，而按上表定义「写在方法体内」即嵌套容器。**判定依据（实锤）**：本任务的探针实测显示脚本内声明类型的方法体行为与普通方法一致（`r5/pin.py` 的 `script-nested-instance-method` / `script-nested-shared-method` / `script-nested-module` 与普通模式同读数）。**推论**：这类格**可**用编译器测试豁免（除非落在下文的「已知受容器影响」清单内）。

**配套裁定 · 方法体内不存在合法形态的语法**：`Namespace`、`Option` 语句、声明类 arm（`Class`/`Enum`/`Delegate` 等）在**方法体内写本就非法**（任何 VB 皆然），故其**嵌套栏一律 `不适用`**（理由：不存在合法形态），**不是 `缺口`**——与 `Option` 语句的既有处置同理。**顶层栏**仍按实际判定（如 `Namespace` 顶层栏 = `缺口`，因 BC36965 是脚本特有禁止面且无正向用例）。

### 「完整覆盖」的判定（三项合取）

**只判 `缺口` 行数为 0 不够**——行集可被缩小、`已覆盖` 可被探针读数满足、且无分母。判据升级为**三项同时成立**：

| # | 判据 | 机械核对方式 |
|---|---|---|
| ① | **行集完备**：ledger 的行集 = **spec 标题树**（规范性命名）∪ **parser 关键字分派 arm**（实现完备性）∪ **本 fork 新增指令**（`#Load`/`#R`/`#!`/`#`）。三源**并集**，不得只取其一。<br>**取证修正（U2 实施期，实锤；范围行号经终态验证修复轮改正）**：语句主分派**不在** `Parser\ParseStatement.vb`，在 **`Parser\Parser.vb:935-1272`**（`ParseStatement.vb` 只有 `Continue`/`Exit`/拟古/复合赋值/`While\|With\|SyncLock` 五个子分派，分别在 `:24`/`:95`/`:487`/`:1198`/`:1418`）；表达式分派在 `Parser\ParseExpression.vb` | U11 的 `SyntaxKind` 反查**差集必须为空**（不是抽样）；差集非空 ⇒ 新增 ledger 行 |
| ② | **状态可用**：`已覆盖` / `新补` **只认指向真实测试方法的 `文件:行号`**；**探针读数不得计入 `已覆盖`**（探针不是测试面，且 `tmp\` 不入库、冷克隆不可复核）。探针读数另立一列 `探针实测` 作参考 | 逐行核 `文件:行号` 指向的是 `<Fact>`/`<Theory>` 标注的方法声明 |
| ③ | **分母固定**：U2 结束时必须报出**总行数与来源分解**（spec 多少行、parser arm 多少行、fork 指令多少行），此后行集只增不减 | 分母写在 ledger 头部；U11 复核时比对 |

**「完整覆盖」= ①②③ 同时成立。** 这三项都由 U11 复核，且 ① 是**全量差集**而非抽样。

#### 判据②补充裁定 · 负向（断言诊断）用例**算** `已覆盖`（main 裁定，U2 裁决轮提出）

**问题**：许多格以「报 `BCxxxxx`」的**负向**用例为依据（如 `MyBase` 顶层依据 `ScriptTopLevelCrashTests.vb:401` 断言 `BC36966`）。若负向诊断不算「覆盖该容器里的行为」，这些格全部要转 `缺口`。

**裁定：算。** 依据是本任务**作者给定的判定原则**逐字：

> 崩编译器是 bug。**要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」。**

即「报出诊断」是**两条合法出口之一**，不是「未覆盖」。`spec\spec-scripting-dialect.md` 的「Script-specific restrictions and diagnostics」表把 `BC36965`/`BC36966`/`BC36973` 等**明文规定**为脚本方言的规范行为；断言这些诊断的用例**正是**对该语法在该容器里的规范行为的覆盖。

**判据**：一条用例只要**钉住了该语法在该容器里的规范行为**（无论是「正常运行并产生某结果」还是「报出某诊断 ID」），即为 `已覆盖`。**唯一不算**的是：断言的是**无关行为**、或依据指向的**不是该语法**（那是错锚，见判据②的实体要求）。

**对 U10 的影响**：负向格**不必**转 `缺口`。U10 重判嵌套栏时，把这条与豁免口径一并应用。

#### 判据②补充裁定 · 错锚一律判 `缺口`（main 裁定）

**问题**：部分格的依据指向**宿主 helper 的调用点实参**、而非脚本源码里该语法的实际出现。

**裁定**：凡依据**不指向该语法在脚本源码里的实际出现**（而是指向 helper、调用点、或测试自身代码）的格，**一律判 `缺口`**。这与「负向用例算覆盖」不冲突：负向用例的依据仍**指向该语法本身**（只是断言它报错）。

**裁定 6 的一处初判错误（已纠正，记录以明判定纪律）**：main 初判 `#### Reference Parameters` 顶层栏的三条依据「**都**指向宿主 helper 的调用点实参」⇒ 应判 `缺口`。**实施者核实后反驳，main 复核确认实施者正确**：`CommandLineRunnerTests.vb:1220` 的脚本源码逐字是 `Sub F(ByRef s As Span(Of Integer))`——是**脚本顶层 `Sub` 声明里的 `ByRef` 形参**（**实锤**，已读原文），且诊断 `BC31396` 的消息逐字含「ByRef 参数类型」；同文件 `:1177` 的 **`ByVal`** 同型参数**不报错**，构成干净的判别对（ByRef 触发、ByVal 不触发）。
⇒ **该格维持 `已覆盖`**（依裁定 5：负向用例指向该语法本身即算覆盖）。**教训：main 的裁定若基于未逐条打开的依据统计，可能是错的；实施者/验证者有据反驳时以证据为准。**

#### 判据②补充裁定 · REPL 顶层与 `.vbx` 顶层**同属顶层容器**（main 裁定）

**问题**：`CommandLineRunnerTests.vb` 的 REPL 用例（`?` Print、`## With`、`## SyncLock`、`### Default Instances`、`## Late-Bound Expressions` 等）被用作**顶层容器**的依据，是否成立？

**裁定：成立**，但**跨提交敏感的族除外**。理由：

- **容器种类相同**：REPL 提交与 `.vbx` 顶层**都落在提交类的顶层**（`TypeKind.Submission` / 脚本类），顶层语句都由合成的 `<Initialize>` 承载。就「该语法在顶层容器里的行为」而言，两者是**同一容器**。
- **例外**：REPL 是**提交链**（每条输入一个提交，经 `PreviousSubmission` 回溯），`.vbx` 通常是**单提交**（`#Load` 只加树不加提交）。因此**依赖提交链**的语法在两者间确有差异——这类族**已在**「已知受容器影响」清单内（跨提交引用 / `Handles` / `WithEvents` / 隐式 `Me`），按清单规则**两栏都须脚本测试**，不受本条影响。
- **纪律**：用 REPL 用例作顶层依据时，「依据」列**须注明该用例是 REPL 路径**（如 `方法 X:N（REPL）`），以便 U11 与后续复核区分。**判据扩写**（终态验证后）：REPL 路径的判据 = 「① `input:=` + `RunInteractive`」**或**「② 走 `ContinueWith` 提交链（`Repl*` 方法）」——两者都是提交链，都须标注。

#### 判据②补充裁定 · 「只验发射」对不可进程内跑的语句算 `已覆盖`（main 裁定）

**问题**：`Stop` 顶层栏的依据只有 `AssertEmits`（只验「编译 + 发射成功」，不运行），而 §一「已知空白」把 `End`/`Stop` 的「不可进程内跑」登记为**本任务不承诺闭合的探索项**。这算 `已覆盖` 吗？

**裁定：算。** 理由：

- 按**裁定 5** 的口径，`已覆盖` 的判据是「用例**钉住了该语法在该容器里的规范行为**」。对 `End`/`Stop` 而言，**可观测的规范行为就是「编译通过且发射成功」**——进程内运行会终止宿主，**该行为在本测试环境下不可观测**，故「发射」就是能钉住的最大切面。
- §一已把这一边界**显式登记**为探索项而非缺口 ⇒ 用 `AssertEmits` 覆盖它就是**该登记的既定处置**，不是偷工。
- **反例对照**：这与「依据指向 helper/调用点」的错锚**不同**——那些格的依据**根本不是该语法**；`Stop` 的依据**就是** `Stop` 本身，只是断言面受环境限制。

**纪律**：凡以「只验发射」为依据的 `已覆盖` 格，**须在「依据」列注明「（只验发射：该语句不可进程内跑）」**，使该限制对 U11 与后续复核可见。

#### 判据②的两栏差异化口径（U2 实施期发现，main 裁定；**可修订的现状决定点**）

**发现**：U2 建表时报出——嵌套容器栏的 150 个 `缺口` 里**多数是「名义缺口」**：`With`/`SyncLock`/`Select Case`/`While`/`Do`/`Try`/`ReDim`/`Erase`/`Continue`/`Const` 等的父容器是**通用 VB 语言面**，行为已由 `Compilers\VisualBasicTest` 的普通编译测试覆盖，只是没写进 `Scripting\VisualBasicTest` 的方法体。

**若按「`已覆盖` 一律只认 `Scripting\VisualBasicTest`」执行**，U10 要为约 150 格补测，其中绝大部分是**冗余用例**——这正是 `design-overview.md` §2 剪枝第 1 条警告的「以用例数当验收会诱导造冗余用例」。

**裁定**：判据②的「真实测试方法」**按两栏分别解释**——理由是**两栏的容器语义本就不同**：

| 栏 | 覆盖来源 | 理由 |
|---|---|---|
| **顶层容器** | **必须**是 `Scripting\VisualBasicTest` 的方法 | 顶层是**脚本特有的容器**（提交类成员 / 顶层语句）；普通编译测试**不可能**覆盖它 |
| **嵌套容器** | `Scripting\VisualBasicTest` **或**下列七门编译器测试项目的任一方法 | 脚本单元内的方法体**编译产物就是普通方法**（提交类的成员方法），故通用语言面由编译器测试覆盖是**有效证据** |
| **例外（嵌套容器）** | 仍**必须**是 `Scripting\VisualBasicTest` 的方法 | 当该语法落在**已知受容器影响**的族时（见下） |

**七门编译器测试项目**（**实锤**，`scripts\verify-vb-compiler-tests.ps1:8-14` 的 `Project` 列）：

| 门 | 项目路径 | 用例数 |
|---|---|---|
| Phase2 | `Compilers\VisualBasicTest\Microsoft.CodeAnalysis.VisualBasic.UnitTests.vbproj` | 143 |
| Syntax | `Compilers\VisualBasicSyntaxTest\Microsoft.CodeAnalysis.VisualBasic.Syntax.UnitTests.vbproj` | 4070 |
| Symbol | `Compilers\VisualBasicSymbolTest\Microsoft.CodeAnalysis.VisualBasic.Symbol.UnitTests.vbproj` | 3407 |
| **Semantic** | `Compilers\VisualBasicSemanticTest\Microsoft.CodeAnalysis.VisualBasic.Semantic.UnitTests.vbproj` | **5826** |
| IOperation | `Compilers\VisualBasicIOperationTest\Roslyn.Compilers.VisualBasic.IOperation.UnitTests.vbproj` | 1574 |
| **Emit** | `Compilers\VisualBasicEmitTest\Microsoft.CodeAnalysis.VisualBasic.Emit.UnitTests.vbproj` | **4370** |
| CommandLine | `Compilers\VisualBasicCommandLineTest\Microsoft.CodeAnalysis.VisualBasic.CommandLine.UnitTests.vbproj` | 475 |

> **⚠️ 初稿的路径错误（已修，记录以明判定纪律）**：本裁定初稿把豁免来源写成 **`Compilers\VisualBasicTest`**——那是上表**只有 143 用例的 Phase2 门**，**不是**通用语言面的所在。真正的覆盖面在 **SemanticTest(5826)** 与 **Emit(4370)** 等。按初稿的路径执行，U10 会得出「无覆盖」而**把全部 150 格保留为 `缺口`**，**豁免完全失效**。**教训：写路径类证据必须打开 `scripts\verify-vb-compiler-tests.ps1` 核对，不能凭目录名相似臆测。**

**「已知受容器影响」的族清单**（**有据可查**，来源 `issues\README.md` 的已修复条目 04–20；这些正是本 fork 反复出缺陷的面，容器确实改变其行为）：

- 隐式 `Me` / 共享成员访问实例成员（issue 08）
- 跨提交引用 / `Handles` 子句 / `WithEvents`（issue 14、19）
- 共享字段与属性初始化器、`Await` 初始化器（issue 05、06、09）
- 事件与 `RaiseEvent`（issue 13、20）
- 扩展方法（issue 04）
- `MyBase` / `MyClass`（issue 15）
- 顶层的分支/`Finally` 语义（issue 16）——**仅顶层栏**（见下「清单项的作用域」）
- 脚本类的构造器（issue 12）

**清单项的作用域（main 裁定 9，2026-09-15）**：上列清单**默认两项都约束**（顶栏与嵌套栏都必须脚本测试），**唯一例外是「顶层的分支/`Finally` 语义」（issue 16）——该项只约束顶层栏**，嵌套栏可豁免。

> **依据（实锤）**：issue 16 的根因是**顶层语句不做流分析**（`MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?`），而**普通方法有流分析**——issue 16 自己实测「同形状放进普通方法报 BC30101」。故容器的影响**只在顶层**；把它强加到嵌套栏会要求为普通方法体内本就正常的分支/`Finally` 造冗余用例。

**纪律**：
1. 嵌套栏用 `Compilers\VisualBasicTest` 的指针时，**「依据」列必须写明该行为与容器无关**（一句话理由）；未写理由的按 `缺口` 处理。
2. 上列清单内的族，嵌套栏**不得**用编译器测试顶替——**测试鉴别力须单独论证**（`VBNetScriptMaintainer` 纪律）。
3. 此口径**不降低**判据②的实体要求：指针仍须指向 `<Fact>`/`<Theory>` 标注的**方法声明行**，仍逐行核。

**这是可修订的现状决定点**：若实施期发现某个「名义缺口」其实是真缺口（容器确实改变了行为），该族**升级**进上列清单，其嵌套栏转为必须脚本测试。

### 状态取值（闭集，仅四值）

| 状态 | 含义 | 允许留白 |
|---|---|---|
| `已覆盖` | **已有测试方法**覆盖该语法在该容器里的行为，给 `文件:行号`（**必须是方法声明行**） | 否 |
| `新补` | 本任务补了测试方法，关闭后改为 `已覆盖` 并填 `文件:行号` | 否（关闭时须为 0） |
| `不适用` | 该语法在该容器里**不存在语义**或**与容器无关**，须给**理由** + **检索证据**（命令 + 命中情况） | 是，但必须有证据 |
| `缺口` | 该语法在该容器里应有行为，但无**测试方法**覆盖 | 是（**收口时必须为 0**） |

**状态列只能是这四值。** 诊断码、探针读数、「无对照」标记一律进 `依据` 列或独立的 `探针实测` 列——否则 `grep` 判据与表规则不自洽（本轮种子行已犯此错，见 `test-plan.md` §C.3 的修订说明）。

---

## 五、共享源码事实（本计划多单元复用的锚点，集中在此以免重复）

| # | 事实 | 证据 | 三态 |
|---|---|---|---|
| F1 | 脚本顶层语句不在 `<Initialize>` 的方法体里；该合成方法体是只含 `BoundLabelStatement(ExitLabel)` 的空壳 | `Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:135-142`（第二轮已复核） | 实锤 |
| F2 | 顶层语句的宿主方法体**不做流分析** | `Compilation\MethodCompiler.vb:625` 的 `' TODO: any flow analysis for initializers?`（第二轮已复核） | 实锤 |
| F3 | 顶层 `Dim x = <expr>`（无 `As` 子句）作为**字段**绑定为 `Object`，**不做类型推断**；`Option Strict On` 下报 `BC30209` | 既有用例 `ScriptModeStatementConformanceTests.vb:606`（`TopLevelInferredField_IsObject_Conforms`，重载决议判别）与 `:623`（`TopLevelInferredFieldWithStrictOn_IsReported`） | 实锤 |
| F4 | 脚本类非限定成员引用一律经 `TryBindInteractiveReceiver` 解析成 `BoundPreviousSubmissionReference`（同一次提交内也不例外） | `Binding\Binder_Expressions.vb:2570-2576` → `:2615-2634`；插桩实测见第二轮 README §二 C2 行 | 实锤 |
| F5 | 脚本类的显式 `Me` / `MyBase` / `MyClass` 报 `BC36966`（`ERR_KeywordNotAllowedInScript`）；脚本里 `Namespace` 报 `BC36965` | `spec\spec-scripting-dialect.md` 的「Script-specific restrictions and diagnostics」表；报点 `Errors\Errors.vb` 的 `ERR_KeywordNotAllowedInScript` | 实锤 |
| F6 | 顶层 `On Error` / `Resume` 报 `BC36956`（`ERR_ResumablesCannotContainOnError`）；顶层 `RaiseEvent` 报 unsupported-statement 诊断；顶层 `Yield` 报 `BC36966` | 判据 `Binding\Binder_Statements.vb:1190-1191`（`IsInAsyncContext` 分支）与既有用例 `ScriptModeStatementConformanceTests.vb:219` / `ScriptTests.vb:359` | 实锤（诊断存在）；**推测**（`On Error` 的判据就是 `:1190` 那一处，未逐条插桩确证） |
| F7 | 测试宿主统一走 `CommandLineRunner` + 内存 `ConsoleIO`；`ScriptModeConformance.CreateRunner` 把 `BuildPaths.TempDir` 指向 `AppContext.BaseDirectory`（不建目录） | `Scripting\VisualBasicTest\ScriptModeConformanceTests.vb:178-187` | 实锤 |
| F8 | `Scripting\VisualBasicTest` 是 MTP 项目，`dotnet test` 静默跑 0 个；须直接跑程序集 `-automated` | 第二轮 `test-plan.md` §4；本仓库记忆条目「VB scripting test runner」 | 实锤 |

---

## 六、Vortex 代办（分批）

> main 只调度；实施者与验证者 background agent 串行交替。每单元一组（实施者产出 → 验证者按 pass 条件核对 → 打回修复 → 通过关闭）。**单元可并行度低**：多数单元触碰同一批测试文件（`ScriptMode*ConformanceTests.vb`），串行可避免冲突。

| 单元 | 内容 | 依赖 | 对应 TaskCreate |
|---|---|---|---|
| **U1** | A 组的**判别性用例补测**（非缺陷修复）：把「顶层 `Dim x = <expr>` 不推断」的后果补成成对用例（不加 `As` ⇒ 晚期绑定失败；加 `As` ⇒ 正常），并登记 D5 分歧 | 无 | #3 |
| **U2** | VB 特有语法 ledger 建表（**先建表后补用例**，表本身是本任务的验收产物） | 无 | #11 |
| **U3** | 宿主对象（`globalsType`）绑定语义补测 | 无 | #4 |
| **U4** | `<host>` / `<implicit>` 引用别名补测 | 无 | #5 |
| **U5** | 异常与取消后的提交链状态补测 | 无 | #6 |
| **U6** | 命令行参数 `Args` 与搜索路径补测 | 无 | #7 |
| **U7** | PDB / 调试信息与栈帧行号补测 | 无 | #8 |
| **U8** | ObjectFormatter 代理族与异常栈渲染补测 | 无 | #9 |
| **U9** | 脚本 API 面剩余缺口补测 | 无 | #10 |
| **U10** | ledger 缺口填充（U2 建表后剩余的 `缺口` 行） | U2 | #11 |
| **U11** | ledger 复核：`缺口` 行归零 + 每行 `不适用` 带证据 | U10 | #11 |
| **U12** | 全量收口（七门 gate + Scripting 程序集 + 文档同步） | 全部 | #12 |

**顺序**：U1 → U2 →（U3…U9 各自独立）→ U10 → U11 → U12。

---

## 七、探针清单（证据来源，非测试面）

| 探针脚本 | 覆盖什么 | 状态 |
|---|---|---|
| `tmp\probes\u14\run.py` | 第一轮 VB 特有语法冒烟：60 个形状，脚本列 | 已运行（`**实锤**`，输出见本任务流水账） |
| `tmp\probes\u14\run2.py` | 第二轮：为每个形状补**普通模式对照组**（`vbc` + `Module Entry / Sub Main`）；修正第一轮设计错的探针（`MyClass` 在 `Shadows` 上自递归、`Extension` 必须 `Shared`、`Yield` 需 `Iterator` 修饰符） | 已运行 |
| `tmp\probes\u14\run3.py` | 第三轮：零覆盖关键字（`Static` / `Err` / `Resume` / `Option` 各档）+ `On Error` 各变体 + LINQ/XML/`Declare` 变体，38 个形状 | 已运行 |
| `tmp\probes\u14\run4.py` | 第四轮：A1 的隔离（顶层 vs 嵌套类；`Into g = Group` vs `Into c = Count()` vs 方法语法）——**结论被第五轮推翻** | 已运行 |
| `tmp\probes\u14\r4\ordinary_control.py` | 补普通模式对照，用运行时真实 `/r:`（`Microsoft.NETCore.App\10.0.12`） | 已运行。**先用 `vbc.exe` 直调**那次得 `BC30652`（桌面 `mscorlib` 4.0.0.0 与 net10 runtime 引用冲突 ⇒ 该通道无效）；**随后改用 `vbi <file>.vb /out:` 编译模式重跑并成功**（产物 `r4\ord-groupbyo.dll` / `ord-groupby-counto.dll` / `ord-lambda-multilineo.dll` + 三个 `.runtimeconfig.json` 均在，**实锤**）。其读数后被 `r5` 的更严对照取代 |
| `tmp\probes\u14\r5\pin.py` | 第五轮：普通模式改经 **vbi 编译模式**（`/out:` + `/target:exe`）；一次只变一个维度（普通/脚本 × Module/类/顶层 Sub） | 已运行（**决定性反证**：普通模式抛同一条异常） |
| `tmp\probes\u14\r5\decisive.py` | 判别 `Option Infer` 变量：普通模式开/关推断、脚本顶层开推断、显式 `As` | 已运行（**关键读数**：普通模式开推断 exit 0） |
| `tmp\probes\u14\r5\field_infer.py` | 终判：`Option Infer On` 下框架字段 vs 顶层字段的推断行为 | 已运行（**终判读数**：编译器自报顶层是 `Object`） |

**探针纪律**（沿用第二轮，**本轮新增两条，都是踩坑换来的**）：
- 探针**不属于测试面**，不得被测试引用；`tmp\` 已被 `.gitignore` 忽略。
- **普通模式对照不要用 `vbc.exe` 直接编**：该 fork 的 `vbc.rsp` 用 `/sdkpath:` 指向桌面框架目录，与 net10 runtime 引用冲突（`BC30652` 需要 mscorlib 4.0.0.0）。**改用 `vbi <file>.vb /out:<dll> /target:exe` 编译模式**（它用脚本宿主的引用集合），再用 `dotnet <dll>` 跑——**须把 `vbi.runtimeconfig.json` 复制为 `<dll 同名>.runtimeconfig.json`**，否则 `dotnet` 报 `hostpolicy.dll not found`。此三项读数（`pin.py` / `decisive.py` / `field_infer.py`）即以此通道取得。
- **对照组失败 ≠ 缺陷成立**：本轮前两轮的「对照失败」都被误读为缺陷成立。**任何对照失败先当作探针问题排查**（引用、语法、运行时配置），直到对照**通过**并且**行为与目标容器不同**，才可判「容器特有」。

---

## 八、资源与文档同步义务

| # | 义务 | 落点 |
|---|---|---|
| 1 | **D5 分歧**（顶层 `Dim x = <expr>` 不推断 vs C# 脚本 `var x` 推断）登记：建 `issues\issue-*.md` 或写入 `OPEN QUESTIONS`，按 `issues\README.md` 现行最大编号顺延（现最大为 **20**）；正文含触发面 / 实测读数 + **交用户裁决**的明确表述（**不自行改语义**）。实施期若发现别的真缺陷，同样按此登记 | `issues\` |
| 7 | **探针读数落账**：本任务四份文档里所有 `实锤（已运行）` 的读数，其探针脚本在 `tmp\`（**git-ignored、不入库**）⇒ **冷克隆上不可复核**。关键读数（A 组的三轮对照、种子行的对照结果）必须**以文本形式抄进本目录文档或 `issues\`**，不得只留在 `tmp\` | 本目录 + `issues\` |
| 2 | `issues\README.md` 状态列随修复同步（不得预填/推测 commit 号） | `issues\README.md` |
| 3 | `spec-scripting-dialect.md` 的 **Testing** 节补本任务新增的验证面（该节现在只覆盖到第二轮的能力面） | spec 阶段 |
| 4 | 本任务变更面登记 | `upstream-merge.md` |
| 5 | 七门 gate 期望值同步（**只改数字，不改判据**） | `scripts\verify-vb-compiler-tests.ps1:8-14` |
| 6 | 若 `VBResources.resx` 有新增条目，13 份 `xlf` 按原版 VB 编译器规范同步（中性 resx 须先审为干净英文产品内容） | `Compilers\VisualBasic\Portable\xlf\` |

---

## 九、D4 优先级判定（`design-overview.md` §6）

编排表「任务计划」行的验收要点含「**P1–P4 依 D4 判入**」。按 `decisions.md` **D4**（引用按节号，不按行号）：

| 单元 | D4 归属 | 依据 |
|---|---|---|
| U1–U11 | **不入 P1–P4** | D4 是**提案**（proposals/）的优先级判定闸门。本任务是**测试补强 + 分歧登记**，**不引入能力变更** ⇒ 无提案可判。**U1 亦不入**：经复核，U1 的产物是「既有行为的用例」而非能力变更（见 `design-overview.md` §3.4） |
| （若实施期发现真缺陷） | **按 D4 单独判定** | 若某缺陷的修复**引入能力变更**（如新增诊断码、改变合法既有代码的语义），该单元**停手上报**，按 D4 两档规则重判并考虑转 `proposals/` |

**P1 硬约束（D4「不引起无谓的 regression / breaking change」）对本任务的影响**：

- 本任务**默认不触碰产品源码**（`design-overview.md` §4 共同纪律），故**不触 P1 硬约束**。
- 唯一例外是实施期发现的真缺陷。届时的判据沿用 D4 修正理解：**改变「合法既有代码的正确语义」= 破坏；纠正错误用法（如本该报错却崩）= 修错，不算回归**。
- **适用版本边界**：按 **D6**，兼容性约束**只对正式版（GA）成立**；本 fork 只有 beta / preview（`proposals\README.md:73` 逐字「1.2 beta」），故「这会改掉既有语义」**不是**否决理由。但 D6 只解掉兼容性这一条，**实现可行性、与 D5 的同形性、机制收益与代价**仍须逐条论证。

---

## 十、停止条件

- 缺少关键输入、权限或负责人。
- 验收标准无法被检查（如 ledger 的 `不适用` 无法给出检索证据）。
- 需要修改非范围中明令禁止的范围（如改容器种类、给共享发射层加守卫）。
- 连续尝试没有新证据。
- 发现任务应升级到其它触媒（如 A1 的修复面扩大到需要 propose/meeting 阶段）。
