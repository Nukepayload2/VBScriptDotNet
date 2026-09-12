# Visual Basic Language Design Meeting
August 9, 2026

VBScript.NET 产品设计系列会议。本次我们不评 Anthony 提案库，而是回到产品本身：2.0 beta（`with-modified-vbsyntax` 分支）fork 了完整 Roslyn 编译器、修复了 Await/AddHandler/Imports/退出码、移植了 `#Load`，**理论上和 C# REPL 不应该有功能差距**。我们想把这个"理论上"落成"实际上"，于是把 C# REPL/scripting 的历史设计摆上桌，逐条对照 VB REPL 现状，评估两份新提案——一份是让 `?` 可选（对齐 csi 敲表达式即打印），另一份是 Avalonia 仿 PowerShell ISE 的 GUI。前者直接缩小与 C# REPL 的功能差距；后者是易用性投资，与会话主题相邻但性质不同。

## Agenda

* [现状盘点：VBScript.NET REPL 与 C# REPL 的功能差距](#现状盘点vbscriptnet-repl-与-c-repl-的功能差距)
* [借鉴：csharplang 中 REPL/scripting 相关设计](#借鉴csharplang-中-replscripting-相关设计)
* [Proposal: REPL 表达式开头问号可选（proposal-optional-question-prefix）](#proposal-repl-表达式开头问号可选proposal-optional-question-prefix)
* [Proposal: Avalonia UI 图形化 REPL/脚本编辑器（proposal-avalonia-ise-repl-ui）](#proposal-avalonia-ui-图形化-repl脚本编辑器proposal-avalonia-ise-repl-ui)

## 现状盘点：VBScript.NET REPL 与 C# REPL 的功能差距

我们先把产品现状摊开（1.2 版本事实见 `../proposals/vbx-1.2-beta/` 版本归档）。

- **1.2 beta（微软商店版，已发布）**：几乎原封不动。顶层 `Await`/`AddHandler` 是损坏状态，`Imports` 交互模式失效；仅通过 fork common scripting（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）做 workaround 启用了 vbx 文件执行——在 `RunScript` 里用 `Script.CreateInitialScript(Of Object)` 然后 `(ReturnValue As Integer?)` 取退出码（因上游 `CreateScriptCompilation` 把返回值硬编码为 `Object`）。**以上为 1.2 beta 历史状态；2.0 beta 的 `RunScriptAsync` 已改为 `Script.CreateInitialScript<int>` 并直接返回 `ReturnValue`。**
- **2.0 beta（当前，进行中）**：fork 完整 Roslyn 编译器进 `Compilers\`；已修复顶层 Await、顶层 AddHandler/RemoveHandler、Imports 跨提交累积、Function Main 退出码语义（`Return 42` → 退出码 42，裸 Return/无 Return → 0，**末尾表达式不再设退出码**）；已移植 C# interactive 的 `#Load`。理论上与 C# REPL 无功能差距。
- **剩余已知差距（REPL 语义层）**：顶层 `On Error Resume Next`、顶层 `RaiseEvent` 报告不支持诊断（BC30024/BC30188/BC30205/BC36956）；`SourceCodeKind.Interactive` 已上游 `[Obsolete]`（统一走 `Script`）。
- **最显眼的交互体验差距**：C# REPL（`csi`）直接输入 `DateTime.Now` 就求值打印；VB REPL 必须写 `? Now`，否则报 **BC30545**（属性访问必须分配给属性或使用属性值）。

这个"`?` 前缀 vs 裸表达式即打印"的差距，是本次评估的焦点之一。

## 借鉴：csharplang 中 REPL/scripting 相关设计

csharplang **没有专门的 REPL/scripting 提案或会议**，相关内容寄生在 Top-level statements / Simple programs / ignored directives 的设计里。我们引用了五份文件（均已 Grep 核实路径与原文）：

### 1. submission 状态保持的三场景（LDM-2020-01-22）

> "Three main scenarios: 1. Simple programs are simple… 2. Top-level functions… 3. Scripting/interactive. **Submission system allows state preservation across evaluations.**" → `csharplang\meetings\2020\LDM-2020-01-22.md`

顶层语句设计时把"脚本/交互"列为三大场景之一，明确 submission 系统允许跨求值保持状态。**这正是 VBScript.NET REPL 的 `Imports` 跨提交累积、globals（`CommandLineScriptGlobals`/`InteractiveScriptGlobals`）要保证的资产**——2.0 beta 修复 Imports 累积就是在兑现这一条。

### 2. "第三种方言"担忧（LDM-2019-09-11、LDM-2020-02-26）

C# 侧反复担心交互方言与普通语言分裂：

> "since the semantics of this design have subtle differences from CSX this would effectively create a **third dialect** of C#." → `csharplang\meetings\2020\LDM-2020-02-26.md`
> "CSX is designed to allow all values to be persisted… this makes a number of types of statements illegal… like **ref locals**… the new **`using var`** declaration form is nonsensical under the CSX design." → 同文件

2019-09-11 那次（`csharplang\meetings\2019\LDM-2019-09-11.md`）记录了一个事实：C# Interactive Window、Jupyter、try.net 都在用 "C# scripting" 这一事实上已经成了方言的语言，团队担心语言分裂，但结论是顶层语句成本高但值得做，排入 9.0。

**对 VBScript.NET 的含义**：VB REPL 就是 VB 的交互方言，`?` 前缀、顶层免包装、自动打印都是这个方言的合法成员。我们不担心"第三种方言"——我们本来就是产品自带的 REPL；但我们要**有意识地**保持 REPL 方言与"普通 VB 语义"的最小差异（例如 `?` 只是打印标记、退出码语义单独定义），避免 REPL 语义悄悄污染脚本模式。

### 3. 末尾表达式不进 C# 本体（LDM-2020-04-15）——对应 VBScript.NET 的退出码语义

> "unlike script we still don't allow expressions at the end. For the scripting dialect this is mostly for producing a result in an interactive setting." → `csharplang\meetings\2020\LDM-2020-04-15.md`
> **Decision**: "We are ok with this remaining distance, and would prefer not to have a notion of 'expression at the end produces result' in C#."

C# 明确否决"末尾表达式产生结果"进入语言本体，只在 interactive 的 `.csx` 里用它出结果。**这正好对应 VBScript.NET 2.0 beta 的 Function Main 退出码语义**：`Return 42` → 退出码 42、裸 Return/无 Return → 0、**末尾表达式不再设退出码**。两条线走的是同一个设计判断：交互式里"末尾表达式的值"是给用户看的输出，不是进程语义。

### 4. `#!` 与 `#:` 忽略指令（ignored-directives.md + LDM-2025-03-12）——与 .vbx 头部注释的类比

C# 14 的 ignored-directives（`csharplang\proposals\csharp-14.0\ignored-directives.md` + `csharplang\meetings\2025\LDM-2025-03-12.md`）接受 shebang `#!` 与 `#:` 前缀，由工具读取、语言核心忽略：

> "The language should ignore these directives, but compiler implementations and other tooling can recognize them." → `csharplang\proposals\csharp-14.0\ignored-directives.md`（Summary）
> "`#r` is already supported by C# scripting and other existing tooling." → 同文件（Alternatives）

**对 VBScript.NET 的含义**：`.vbx` 脚本头部的 `' Attribute TargetFramework = "net48"` 注释正是同类机制——**文件关联运行时按这行注释选择 net48 或 .NET 宿主**，语言核心忽略它，宿主/工具识别它。这与 C# 的 `#:` 设计同构，印证了"头部指令交给宿主"的做法是生态共识，不必在语言里立法。

### 5. 顶层语句的动机之一即脚本/交互（LDM-2020-04-15 / top-level-statements.md）

> "Part of the motivation for the feature was to decrease the syntactic distance between C# (.cs) and its scripting dialect (.csx)." → `csharplang\meetings\2020\LDM-2020-04-15.md`（Top-level statements 一节）

「缩小 .cs 与 .csx 的距离」出自 LDM-2020-04-15；顶层语句**语义化为生成 `Program.Main`**（`partial class Program { static async Task Main(...) }`）出自提案 `csharplang\proposals\csharp-9.0\top-level-statements.md`（其动机是减少 `Main` 样板）。

VBScript.NET 的顶层 Dim/Sub/Function/Class/Module 免包装是同一动机的 VB 落地——脚本文件免去 `Module`/`Main` 样板。

---

## Proposal: REPL 表达式开头问号可选（proposal-optional-question-prefix）

_Related: `../proposals/proposal-optional-question-prefix.md`；C# 对照：`csi` 输入表达式即打印（LDM-2020-04-15 的 interactive 设置）；`../proposals/vbx-1.2-beta/`（REPL 能力版本归档）_

### 场景与缺口

`? Now` 打印值；`Now` 报 **BC30545**（属性访问必须分配给属性或使用属性值）。对交互求值场景，这是噪音错误——用户就是想看值。C# REPL 里 `DateTime.Now` 直接打印。这是 VB REPL 与 C# REPL 最显眼的交互体验差距。

### 候选方案

**PROPOSAL A — 全量自动打印。** 凡"表达式作为语句"一律按 `?` 语义求值打印。最对齐 csi，但可能让用户本意是"调用了带副作用的表达式"也被打印，静默改变行为面最大。

**PROPOSAL B — 仅优化报错文案。** 保留强制 `?`，把 BC30545 换成友好提示（"输入 `? Now` 求值打印"）。零语义风险，但解决不了直觉落差。

**PROPOSAL C — 仅当"值被丢弃"类错误时自动打印（提案采用的方案）。** 判定提交是否为"表达式作为语句"且会产生值被丢弃类诊断；若是，自动按 `?` 语义求值打印。本身合法的语句（赋值 `x = 5`、带副作用调用 `Console.WriteLine(...)`）**行为不变**。行为变化严格限定在"原本就是错误"的提交上。

### 权衡：Q&A

- **对齐 csi vs 方言最小差异。** C# 的 interactive 方言敲表达式即打印（LDM-2020-04-15 承认这是 interactive 的专属行为）。VB 的 `?` 是交互方言的显式打印标记。C 方案在两者之间取中：保留 `?` 显式前缀的兼容，只把"原本就报错的裸表达式"变成打印——**既对齐 csi 的直觉，又不让任何合法语句改义**。A 方案越界，B 方案不够。
- **静默语义风险。** 会议最担心的是"写错的表达式静默打印而非报错"。C 方案把行为变化钉死在"错误→打印"，唯一损失是"用户可能没注意到那是错误"，但交互场景下"打印出值"本身就是反馈；且若用户确实要报错语义，仍可显式写 `?` 之外的其他语句。A 方案的静默面不可接受。
- **与退出码语义正交。** 自动打印只是交互式回显，脚本模式不启用（`vbi script.vbx` 的退出码仍由 `Return` 决定，末尾表达式不设退出码，与 LDM-2020-04-15 的判断同向）。两者不冲突。
- **判定清单维护。** "结果被丢弃"类诊断需逐项枚举（BC30545 及同类），且要随新语法回归。这是 C 方案的持续成本，但可控。

### RESOLUTION:

1. **采纳 C 方案方向（Active）**：REPL 表达式开头 `?` 可选——当提交是"表达式作为语句"且会产生值被丢弃类诊断时，自动按 `?` 前缀语义求值打印。
2. **显式 `?` 保留**：`? Now` 行为不变，兼容不破坏。
3. **合法语句不改义**：赋值、带副作用调用等合法语句行为完全不变；行为变化仅限"原本报错"的提交。
4. **与退出码正交**：自动打印仅在交互式 REPL 生效；`vbi script.vbx` 脚本模式不启用，退出码语义（2.0 beta 已修复）不受影响。
5. **实现要点**：在提交编译发现"表达式作为语句"类错误时，重按打印语义处理；打印格式复用 `?` 的 VB 格式（`ObjectFormatter`）。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——对齐 C# REPL 交互体验、纯增量（不改任何合法代码语义）、风险低，直接进入实现规划。实现前需补齐"结果被丢弃"诊断清单（`OPEN QUESTIONS`）。

---

## Proposal: Avalonia UI 图形化 REPL/脚本编辑器（proposal-avalonia-ise-repl-ui）

_Related: `../proposals/proposal-avalonia-ise-repl-ui.md`；UI 现状：REPL 本体为控制台 `vbi.exe`，商店版带 WinUI3 启动器包装（`vbichooser`/`vbicore`/`vbifw`）_

### 场景与缺口

REPL 本体是控制台 `vbi.exe`，商店版带 WinUI3 启动器包装（`vbichooser`/`vbicore`/`vbifw`）。脚本编辑体验差：无语法高亮、无独立编辑窗格、无选择执行。PowerShell ISE（脚本编辑窗格 + 控制台输出窗格）是脚本用户熟悉的心智模型。Avalonia 跨平台、Avalonia Edit 是成熟编辑器控件。

### 候选方案

**PROPOSAL A — Avalonia + Avalonia Edit 新 GUI（提案）。** 独立可执行文件，仿 ISE 布局，复用现有 Scripting 管线跑提交。

**PROPOSAL B — 在现有 WinUI3 商店包装上扩展。** 在 `vbicore` 里加编辑窗格；复用已有商店分发渠道，但 WinUI3 编辑器控件能力弱、跨平台无望。

**PROPOSAL C — 对接 VS Code / VS 扩展。** 用成熟 IDE 扩展获得编辑器能力；交付与分发成本高，偏离轻量脚本 REPL 定位。

**PROPOSAL D — 保持纯控制台。** 零 UI 成本，保持可管道化。

### 权衡：Q&A

- **这是功能差距还是易用性投资？** 与 optional-question-prefix 不同，GUI 不缩小"VB REPL 与 C# REPL 的功能差距"——C# 侧 csharplang 也没有任何 REPL GUI 提案。它解决的是脚本用户（尤其 VBScript/VBA 迁移者）的编辑体验。**价值真实，但属性不同：它是易用性投资，不是语言/REPL 语义对齐。**
- **A vs B：双技术栈 vs 复用商店栈。** Avalonia 给跨平台 + 成熟编辑控件，但引入第二套 UI 技术栈（现有商店版已是 WinUI3）；WinUI3 是商店版已有栈，分发顺畅，但编辑控件能力弱、锁定 Windows。会议倾向：**先不承诺替换商店版**，A 与 B 可并存共享 `vbi` 核心。
- **A vs D：脚本化资产。** 控制台 REPL 可被管道/自动化消费（`echo ... | vbi`），这是 REPL 的核心资产，GUI 不应替代它。任何 GUI 方案都应保留 `vbi.exe` 控制台本体。
- **成本。** Avalonia Edit 的 VB 语法高亮需接 Roslyn 分类器，工作量与维护面不小；降级方案（关键字规则）体验打折。相对 REPL 语义修复，GUI 的投入产出比低一档。
- **与 PowerShell ISE 心智模型。** ISE 已是微软弃维护的旧工具，但"上编辑下输出 + 选择执行"的布局心智模型仍被脚本用户广泛熟悉，Avalonia 复刻它风险低。

### RESOLUTION:

1. **方向认可（Consider，非立即实现）**：Avalonia + Avalonia Edit 仿 PowerShell ISE 是合理的易用性提升，方向成立。
2. **属性定位**：GUI 是易用性投资，不构成 REPL 功能差距；在 REPL 语义对齐（optional-question-prefix 等）落地后再排期。
3. **双栈并存**：Avalonia 版与商店版 WinUI3 包装先并存、共享 `vbi` 执行核心，不承诺替换；保留 `vbi.exe` 控制台本体。
4. **复用管线**：GUI 只做前端，提交执行全部走现有 Scripting 管线（`VisualBasicScript.vb` → `CommandLineRunner` → 改版 VB 编译器），不另起执行路径。
5. **待定项**：语法高亮接 Roslyn 分类器还是降级规则；跨平台范围；是否重建 `vbichooser` 的 TaskDialog——均列为 `OPEN QUESTIONS`，不阻塞方向。

### 状态

- **LDM 状态**：**Consider**。
- **三态判定：Consider**——价值真实（易用性、ISE 心智模型）但成本高（双 UI 栈、编辑器集成）、且不属于 REPL 功能差距本身；方向认可，暂不进入立即实现规划。提案状态归 active 根目录（Consider 归属 active）。

---

## RESOLUTION（本次会议汇总）

1. **optional-question-prefix → Active**：`?` 可选，仅当"表达式作为语句 + 值被丢弃"时自动打印；显式 `?` 保留；合法语句不改义；与退出码正交。直接缩小与 C# REPL 的交互差距。
2. **avalonia-ise-repl-ui → Consider**：方向认可，易用性投资，双栈并存 + 保留控制台本体 + 复用 Scripting 管线；待 REPL 语义对齐落地后排期。
3. **REPL 方言边界原则**：借鉴 csharplang（LDM-2020-02-26"第三种方言"担忧），VBScript.NET 有意识地保持 REPL 方言与普通 VB 语义的最小差异——`?` 是打印标记、末尾表达式不设退出码（LDM-2020-04-15 同向）、自动打印仅限交互式。
4. **头部指令交给宿主**：`.vbx` 的 `' Attribute TargetFramework = "net48"` 与 C# 的 `#!`/`#:`（ignored-directives.md + LDM-2025-03-12）同构，语言核心忽略、宿主识别，不必在语言里立法。
5. **测试纪律**：两项提案若有实现，必须补无副作用的单元测试（`Scripting\VisualBasicTest\`），不得引入网络/文件写入/进程启动等副作用。

## 附录：C# 生态与互操作考量

> 依据：`..\csharplang-index.md` 与 `..\csharplang` 镜像。C# 原文引用均先在 `..\csharplang` Grep 核实、逐字转抄并标注来源。

- **C# 没有专门的 REPL/scripting 提案**：REPL 内容寄生在 top-level statements / simple programs / ignored directives。最值得引用的文件见正文「借鉴」一节。
- **submission 状态保持**（LDM-2020-01-22）：C# 顶层语句把脚本/交互列为三大场景之一，submission 跨求值保持状态。VBScript.NET 的 Imports 累积与 globals 即此资产。
- **第三种方言担忧**（LDM-2020-02-26 / LDM-2019-09-11）：C# 担忧交互方言分裂；VBScript.NET 作为产品自带的 REPL 本来就是交互方言，但要有意识地控制与普通 VB 的差异面。
- **末尾表达式**（LDM-2020-04-15）：C# 否决进本体、只留 interactive；VBScript.NET 的"末尾表达式不设退出码"是同一判断。
- **忽略指令**（ignored-directives.md + LDM-2025-03-12）：C# 接受 `#!`/`#:` 由工具读取、语言忽略；`#r` 已由 C# scripting 支持。VBScript.NET 的 `#R`/`#Load` 与 `.vbx` 头部 `' Attribute TargetFramework` 注释是同类宿主指令。
- **对 VBScript.NET 的适应建议**：`?` 可选对齐 csi 是低风险纯增量（C# 无对应语法可冲突）；Avalonia GUI 与 C# 生态无对应，纯产品投资；两者都不涉及 ref struct/unsafe/AOT 等 C# interop 议题，与 `decisions.md` M1–M8 无交集。
