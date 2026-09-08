# Visual Basic Language Design Meeting
September 8, 2026

本次会议评估 `proposal-vbi-console-completion`（vbi 控制台 REPL 行内补全）。会前，两条独立评审线各自把它核过一遍：一条从 VB 语言/设计资产出发，把提案引用的编译器、Scripting、宿主源码锚点逐条对回文件；另一条以 C#/dotnet 生态为基准，把它与上游 Roslyn 控制台驱动、dotnet/interactive 的外部基线对照。两条线对"源码地基诚实、引擎路径正确、但现在不该整包投"的判断高度一致，分歧只剩收尾姿态：一边主张归档等触发，另一边主张收编成方向认可的后置候选。我们把这条分歧放回提案本身与既有会议决议的时序里裁决，结论落在 RESOLUTION。

## Agenda

* [Proposal: vbi 控制台 REPL 行内补全（proposal-vbi-console-completion）](#proposal-vbi-控制台-repl-行内补全proposal-vbi-console-completion)

## Proposal: vbi 控制台 REPL 行内补全（proposal-vbi-console-completion）

_Related: `../../proposals/inactive/proposal-vbi-console-completion.md`（主检对象）；`../../proposals/proposal-vbscript-lsp.md` 与 `../meeting-vbscript-lsp.md`（LSP 补层 **Active**，M0–M4）；`../../proposals/proposal-vscode-extension-ise-repl-ui.md` 与 `../meeting-vscode-extension-ise-repl-ui.md`（**Consider**，RESOLUTION #1/#2：LSP 宿主先立、REPL 补全上下文天然完整是一进程 REPL 独占卖点）；`../../proposals/proposal-avalonia-ise-repl-ui.md` 与 `../meeting-vb-repl-parity-with-csharp-repl.md`（**Consider**；REPL 方言边界 / 易用性投资判例）_

### 场景与缺口

提案守的是四条编辑/UI 路线里"没人碰的控制台本体输入面"：用户仍在 Windows Terminal / ConHost 里直接面对 vbi 的 `> ` 提示符时，只有纯整行读入、没有任何补全接缝。它按 UI 复杂度把控制台补全分成 L0–L3，取 **v1 = L0（raw-key 行编辑器）+ L1（行内灰字首候选）**，砍掉 L3 下拉弹层，L2/L3 带触发条件延期；补全引擎 v1 走「浅档」——不复制 Workspaces/Features 补层，用"会话重建 + 复用提交管线产语义模型"做 `LookupSymbols` 查询取 top-1。提案自认这是易用性投资（与 Avalonia / VS Code 同判例），不改语言语义，管道/脚本化兼容必须保留。

### 现状机制（源码核实）——我们对提案源码锚点的复核

我们先把提案的源码锚点整条核过（两条独立评审线都核了，结论一致）：`> ` 提示写在 `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:298`、整行读 `_console.In.ReadLine()` 在 `:305`、EOF/null 语义（输入缓冲空则 `return`、非空则取消本次提交 `:306-315`）、`IsCompleteSubmission` 判续行写 `. `（`:319-326`）、提交用同一条 `ContinueWith` 续接（`:361`）——全部命中；`Scripting\Core\Hosting\CommandLine\ConsoleIO.cs` 只暴露三路 `TextReader/Writer` + 设色 `SetForegroundColor/ResetColor`、无键盘事件通道；`Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb` 确实只覆写 `ReadLine()` 整行喂、缺逐键接缝。浅档引擎"复用提交循环续接 + `GetCompilation` 惰性全链编译 + `CreateSubmission` 跨提交链式并入 + `GetGlobalImportsForCompilation` 收跨提交 Imports"这条路径（`CommandLineRunner.cs:361`、`Scripting\Core\Script.cs`、`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`），在提交语义上天然正确，且能脱离控制台在 `Scripting\VisualBasicTest` 全内存 harness 里做无副作用单测——**这是提案最有价值、最该保留的一块**。fork 无补层也核实无误：`VBInteractive.sln` 项目清单无 Workspaces/Features/LanguageServer，`Workspaces\` 下无 `.csproj`。

但翻的过程中，我们对三处引用/表述有不同读法。它们都不推翻源码地基，却决定"能不能现在按 v1 整包投"：

1. **`Interactive\vbi\Vbi.vb:86` 的重定向先例是输出侧，不是输入侧。** 提案把 `PromptScriptError` 里 `Console.IsOutputRedirected/IsErrorRedirected` 的探测，当 L0「stdin 重定向回退」可复用的既有先例。我们打开看，那是"输出被管道/文件捕获时不弹 MsgBox、横幅写 stderr"的**输出侧**判断——它保证自动化调用方不被弹窗阻塞，语义与 L0 要判的**输入侧**（`Console.IsInputRedirected` + 读源是否为真控制台）不对位。L0 一旦逐键接管，这一谓词的完备性直接决定"管道/脚本化资产不被破坏"的承诺是否成立；而测试里 `TestConsoleIO.In` 是 `StringReader`（无键盘），若谓词不把"测试读源"排除，全部既有交互测试会静默改走新路径。这是 v1 最不该留白的一条缝。
2. **多行态比"单行、行尾"的表述贵。** `CommandLineRunner.cs:317` 的 `input` 是 `StringBuilder` 累积多物理行，一个待提交可以是跨 `> ` / `. ` 多行的块（顶层 `For`、`Sub` 等）。L0 逐键接管实际要做的是**真多行编辑器**（跨行光标上下移、续行提示重绘、Enter 区分"缓冲内换行"与"提交"）。提案 L1 段把灰字描述为"光标在行尾尾随一段灰字"，对"续行 `. ` 态的行尾"与"整段待提交"两种行尾混在一起，多行成本被"单行、行尾"的表述低估。
3. **两平台 ROI 不对称没摊开。** Windows 上今天 `Console.ReadLine` 借 conhost 白送左右键 / Home / End / 回删 / 复制粘贴 / up-arrow 历史；net48 ConHost 与 Windows Terminal 行为还不一致（`Interactive\vbi\vbi.vbproj:7` 三 TFM `net10.0-windows;net10.0;net48`）。L0 一旦接管，这些全部转由我方自持——"替换系统白送件"是回归最易咬人的一类。而 Unix 侧今天其实没有行编辑（只读到换行），L0 在那是纯新增。也就是说 **L0 在 Windows 是"用重写换风险"，在 Unix 才是"用重写换新功能"**。

另一条来自生态对照的纠偏，我们也采纳：

4. **dotnet/interactive"≈310 行 glue"不是浅档成本小的旁证，是反证。** 提案用 dotnet/interactive 的薄 glue 证明"接线本身小、成本在补层本体"。但那条 glue（`InteractiveWorkspace` + `CompletionService.GetService(document).GetCompletionsAsync`）是趴在**完整 Workspaces/Features 层**（stock Roslyn NuGet）之上的——补全上下文、speculative model、KeywordRecommenders 全在 Features 里免费拿来。fork **没有**该层，所以"310 行"在 fork 的 v1 不成立：fork 要先付"复制 Workspaces/Features + 对 fork 编译"的补层成本，才有资格享受那 310 行的便宜。它更该读成**"工程级补全 = Features 层"的旁证**。

### 候选与权衡：时序、ROI 与"值洼地"判读

把上述事实摆回产品的编辑体验投资棋盘（这是本提案真正的裁判场）：

- **已经 Active 的是 LSP 补层**（`meeting-vbscript-lsp.md` RESOLUTION #1）。一旦补层落地，补全/hover/诊断由同一套 `VisualBasic.Features` 免费给出。补层无论以哪种形态落地，都会让"补全能力"有一个比浅档更强、更省的去处：若补层程序集可被 vbi 进程内引用（复制进 fork 仓库的读法），控制台 L1 直接 in-process 取 Features top-1，质量远高于自研浅档，**提案的浅档引擎会从"v1 的引擎"变成"会在补层后作废的平行玩具"**；若补层走独立 LanguageServer 进程（LSP 会议拍板的默认），补全被装进更成熟的编辑面，裸控制台更没有理由自己造一套引擎。两条子路径都指向同一结论——浅档不该此刻投入。L1 的引擎部分有一个与 Active 项目绑定的到期日：此刻投入等于和一个已经 Active 的项目赛跑，去建一个它即将取代的东西。
- **Avalonia / VS Code 两条易用性路线已判 Consider**，各自带"最小原型"升级闸门；其中 VS Code 会议 RESOLUTION #2 的关键洞见是 **"REPL 补全上下文天然完整"是一进程 REPL 的独占卖点**。换句话说，产品已经决定"人面对 REPL 敲字要补全"这个场景，由带行编辑能力的宿主（一进程 REPL / Avalonia 编辑面）承接，而不是 fork 自研控制台行编辑器。
- 控制台本体在产品叙事里是**"可管道化 / 可脚本化的核心资产"**（`meeting-vb-repl-parity-with-csharp-repl.md` 与 `proposal-avalonia-ise-repl-ui.md` "控制台本体应保留"）。可管道化的使用形态不需要补全（脚本是写好的、不是现场敲的）；需要补全的是"人面对终端逐键交互"的子集——而这个子集，只要 Avalonia / LSP / 一进程任一落地就被更成熟的编辑面承接。**本提案的价值洼地，恰是产品正在去投资的角落。**（此判读含对成熟路线落地时序的预测，标"待定"；但它不依赖任何对使用意愿的臆测，只依赖已固化的方向优先级。）
- **但逆潮流不等于没价值。** 上游 csi/vbi 控制台从无补全、生态共识把真补全放编辑器/前端进程——控制台本体补全确属逆潮流；反过来正因为没人做，若命中率达标它是真差分（用户直接敲 vbi 时唯一的建议源）。差别只在：这个差分值不值得**此刻**用最贵的预算去换，以及它的命中率/延迟这些硬数字还没有任何先验。

我们中间有人担心：归档会让这个"唯一没人占的洼地"从视野里消失、把真差分埋成烂尾。我们最后不这么处理——归档的不是"控制台补全这个概念"，而是"此刻捆绑投入 L0+L1 的工程"。便宜的引擎路径、L0 的独立价值、读行库决策这三件东西，我们从提案里剥离出来分别保留，并给每一条写清楚复活触发（见 RESOLUTION）。

### 深度追问：LDM 拷问清单

1. **普遍性与数据（Q8）。** 受益面 = "仍直接面对裸 vbi 提示符、且需要语言级补全"的用户，随编辑面成熟而收缩；可管道化用户不需要补全。提案没有量化受益面——不扣文档分（这类产品提案本来就难量化），但它强化"先拿证据、后付大成本"的纪律。
2. **更简单的替代（Q9）。** 真需要"REPL 内补全"时，更省的路是让 vbi 的 REPL 跑进**带行编辑能力的宿主**（PSReadLine 类机制在进程内 / 终端侧提供逐键 + 预测），而不是 fork 自研 raw-key 编辑器。`Unresolved` 首问"自研 vs 现成读行库"实际是 ROI 的决定性变量，不是待定小事：现成库（含 IME/宽字符/跨宿主，且大多预留补全 hook）一旦被采纳，L0 成本塌缩、L1 才谈得上；自研则是这个生态里最不该 reinvent 的轮子。默认答案应是"现成库优先，自研仅作库缺位时回退"。
3. **复杂度 / 成本 / 优先级（Q10）与值不值得做（Q12）。** 我们把四条路线的投资次序摆齐：LSP 补层（Active，正在花钱）> Avalonia / VS Code 一进程（Consider，带最小原型闸门，方向已认可）> 控制台本体补全（本提案）。本提案是第三贵的（fork 内重写多平台行编辑器）、覆盖的输入面功能最不全（控制台灰字无 doc/无弹层），却排在两条更成熟但未建成的路线之前推进——时序上不自洽。价值 × 成本 × 风险打分：价值真实但面窄，成本高且回归面大，风险集中（替换系统白送件 + 命中率无先验）。分数不足以支撑此刻 Active。
4. **回归 / 兼容（Q5/Q7 的宿主侧对应）。** 管道/EOF 语义方向对（回退设计已含 `CommandLineRunner.cs:306-315`），但承重墙谓词（输入侧判定）未定死前，承诺不成立，见现状机制第 1 点。

### RESOLUTION:

1. **三态判定：Table（归档待触发）。** vbi 控制台补全此刻不判 Active，也不判 Reject——方向不否决、差分不否认，判的是"此刻捆绑投入 L0+L1 不成立"。归档的是工程投入，不是概念。**proposal 归 inactive/ 目录归档**，本纪要镜像；`proposals/README.md` 索引与文件迁移由调度处理。
2. **L0 与 L1 拆开，不作为捆绑 v1。** 两路评审各自的顾虑在这里汇成一条：把"高风险地基（替换系统 `ReadLine` 白送件）"和"低置信度引擎（命中率无先验）"捆一个 v1，等于用一个高风险地基托一个低置信度引擎。两者生命周期、复活节奏、触发条件都不同，分开才有意义。本判定只对"整包 v1"生效，不预先判死 L0 / L1 各自。
3. **从提案剥离保留三件低成本资产（随归档不烂）：** ① 浅档引擎路径本身（会话重建 + 复用提交管线 + `GetGlobalImportsForCompilation`）在提交语义上天然正确，是"补层落地前唯一可无副作用单测的候选生成路径"，保留为探针候选；② L0 行编辑自持的独立价值（历史 / 光标 / IME 地基，未来任何控制台宿主形态都要）；③ "现成读行库优先"作为 L0 成本的决定性变量。
4. **复活闸门（任一命中即单线复活重评，不冷搁置）：**
   - **G1 – 现成读行库采纳**：若 .NET 侧出现 / 被采纳一款覆盖 IME/宽字符/跨宿主、预留补全 hook 的现成读行库，L0 成本塌缩时重估 L1；
   - **G2 – LSP 补层进展**：`proposal-vbscript-lsp` M0/M1 落地后，若补层程序集可被 vbi 控制台进程 in-process 引用，则 L1 引擎直接接 Features 取 top-1，届时只剩"L0 行编辑 + 控制台灰字呈现层"是剩余工作，可单线评估；若补层被否决或长期搁浅，则 ③ 的浅档引擎探针（非控制台、`Scripting\VisualBasicTest` 全内存 harness、无副作用，产出 top-1 命中率 / 排序确定性 / 全会话重建延迟三数字）成为继续推进的唯一证据依据；
   - **G3 – 探针证据（G2 否决分支的前置）**：非控制台引擎探针给出命中率 / 延迟达标基线。
   - 复活判定的主体是未来 LDM 重评；本会议不给任何时间承诺，只锁触发条件。
5. **对提案正文的修正点（不写回文件，仅本纪要记录）：** (a) 纠偏 dotnet/interactive ≈310 行 glue 的旁证用法，改读为"工程级补全 = Features 层"；(b) L0 启用谓词定为"读源为真控制台 且 `!Console.IsInputRedirected`"，不借 `Vbi.vb:86` 输出侧探测当输入侧判据，并写明测试 `StringReader` 一律走 `ReadLine` 回退；(c) 承认 L0 是真多行编辑器，把多行成本从"单行、行尾"表述里拿出来单列；(d) 把 Windows（换风险）/ Unix（换新功能）两平台 ROI 不对称写进成本模型；(e) 灰字质量红线：低置信度不触发（宁可不触发，也别被 Tab 肌肉记忆放大成"错得更快"），且要能无副作用断言；(f) 若将来推进，Features 触发点从"候选质量成瓶颈才评估"改为"LSP 补层落地即切换"。
6. **与既有决议自洽确认。** 本裁决不新增并行路线、不抢占 Active 补层的排程资源；维持"控制台 = 可管道化核心资产、管道不需要补全"的定位；编辑体验的增量投入继续指向已认可方向的闸门推进（LSP Active / Avalonia、VS Code Consider）。`decisions.md` D4"非语法提案按 LDM 风险评估定级"精神适用于本判例。

### Implication:

- `../../proposals/inactive/proposal-vbi-console-completion.md` 归档至 inactive/（文件迁移与 `proposals/README.md` 索引由调度处理）。
- 未来推进路径按命中闸门分支：G2 落地 → 复用 Features top-1，只补 L0 行编辑 + 控制台呈现层；补层否决 / 搁浅 → 非控制台浅档引擎探针先行（无副作用，产出命中率 / 延迟 / 排序确定性三数字）。
- 本提案与 `decisions.md` M1–M8 **无交集**（同 avalonia 判例：纯产品 / 宿主易用性投资，不动语言、元数据、编译器语义；`meeting-vb-repl-parity-with-csharp-repl.md` 附录已裁此类零摩擦）；D1–D3 语言语义决策不适用，D4 精神适用。
- 独立五维评审（五维评分表 + 三态建议 + 返工建议）为 git-ignored 工作材料，不入库，不在此展开。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：LSP 补层与 vbi 的宿主关系能否支持"控制台进程 in-process 消费 Features top-1"——`meeting-vbscript-lsp.md` 对"IDE 栈复制进 fork 仓库"与"IDE 栈留在本地基线"有内部张力，补层程序集是否可被 vbi 控制台进程引用未定。这不阻塞本裁决（无论宿主关系如何，浅档都不会是补层后最省的那条路），但它决定 G2 分支复活后 L1 的接法。
- `OPEN QUESTIONS`：现成读行库候选与 net48 兼容（G1 的落点）。
- `OPEN QUESTIONS`：net48 ConHost 灰字呈现退化口径（不启用 L1 只给 L0，还是反显 / 降亮）。
- `TODO`：归档本 proposal 到 `proposals/inactive/`、本纪要镜像，更新 `proposals/README.md`（调度处理）。
- `TODO`（作者返工，不写回文件）：按 RESOLUTION ⑤ 的 a–f 修提案正文，若未来复活提交时可直接再审。
- `Follow-up`：LSP 补层 M0/M1 落地或否决时，以本纪要 G1–G3 闸门复核是否单线复活 L0 / L1。

### 状态

- **LDM 状态**：**Table**。
- **三态判定：Table（归档待触发，非冷搁置）**——方向不否决、差分真实（控制台本体补全无先例是真洼地），但此刻捆绑投入 L0+L1 是拿最贵的预算投产品正在去投资的角落，且浅档引擎会在已 Active 的 LSP 补层落地后作废 / 被编辑面承接；归档的只是工程，引擎路径、L0 独立价值、读行库决策三件低成本资产剥离保留，复活闸门 G1–G3 显式锁定。

---

## 附录：C# 生态与互操作考量

> 依据 `../../decisions.md`（M1–M8 / D1–D4）与生态外部基线（外仓，源树标注）。

- **控制台本体补全在 .NET 生态无成功先例**：上游 csi/vbi 控制台驱动纯整行读入、从无补全，生态共识把真补全放编辑器 / 前端进程（Workspaces+Features 层）。dotnet/interactive 的 ≈310 行 glue 趴在完整 Features 层上，是"工程级补全 = Features 层"的旁证，不是"浅档便宜"的旁证。外部 UX 先例（PSReadLine PredictionSource / zsh-autosuggestions / fish）只在交互层成立、引擎层无可借鉴——浅档自研无处可抄，命中率只能实测。
- **M1–M8 无交集、D4 精神适用**：本提案改 `RunInteractiveLoopAsync` 的读入路径 + 宿主层候选生成，不动语言 / 元数据 / 类型系统 / 编译器语义，与 `decisions.md` M1–M8 零摩擦（同 avalonia 判例）；D1–D3 是语言语义决策、不适用；D4"非语法提案按 LDM 风险评估定级"适用于本判例。
