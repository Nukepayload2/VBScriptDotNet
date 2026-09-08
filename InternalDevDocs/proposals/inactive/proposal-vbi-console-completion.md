# vbi 控制台 REPL 行内智能补全（inline suggestion）/ Inline Console Completion for the vbi REPL

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

> 状态说明：本提案为 **Table（归档待触发，非冷搁置）**——对应会议纪要 `../../meetings/inactive/meeting-vbi-console-completion.md`（RESOLUTION：L0/L1 拆分、剥离保留三件低成本资产、复活闸门 G1–G3）。已归档至 `proposals/inactive/`；`proposals/README.md` 索引由验证/调度阶段更新。本文件正文为 propose 阶段冻结产物，纪要 RESOLUTION ⑤ 修正点 a–f 待未来复活重评时再审，不在此改写。

## Summary
[summary]: #summary

本提案的对象**是控制台 REPL 本体（控制台 UI）**——即用户直接在终端/控制台窗口里敲 `vbi.exe` 交互式会话时，输入行上获得**行内灰字首候选建议**并可一键接受（inline suggestion）。它不改编辑器、不改 GUI：**不是** Avalonia GUI（`proposal-avalonia-ise-repl-ui.md`）那条相对容易的路线，**也**不是编辑器 LSP（`proposal-vbscript-lsp.md`）那条相对容易的路线——那两条把「编辑/补全的宿主」换成 Avalonia/VS Code 等更成熟的编辑面，从而"借道变易"；而**本提案守住 vbi 自己的控制台输入面**，恰恰是这两条更易路线**没有覆盖**的短板：只要用户还在 Windows Terminal/ConHost 里直接面对 vbi 的 `> ` 提示符，就仍是纯 `Console.In.ReadLine()`、无任何补全（详见 Motivation）。

控制台补全的难度**不能笼统地说"难/易"，应随 UI 复杂度分档**（Detailed design 的 L0–L3）。本提案的取舍是：**v1 承诺范围 = L0（地基：raw-key 行编辑器）+ L1（行内灰字首候选建议）**——砍掉"下拉弹层"整层，用最小增量换最高的性价比；L2（Tab 顺序切换插入下一候选）、L3（下拉多候选弹层）列为**带触发条件的延期项**，不承诺 L3。

补全引擎 v1 走**「浅档」**：不复制 Workspaces/Features 补层，只做"会话重建 + 复用现有 Scripting/编译管线产语义模型"，用 `SemanticModel` 查询生成候选并排序取 top-1（L1 只需要"排第一"）；关键字与 REPL 指令用受控小表。真正的 Features 级补全（KeywordRecommenders 等）列为**可选升级项**，仅在候选质量成瓶颈时触发（触发代价是把 Workspaces/Features 的 VB 源复制进仓、对着 fork 编译器编译，规模大，见 Alternatives）。

兼容约束必须保留：vbi 可管道/脚本化——stdin 重定向或无控制台时回退现有 `ReadLine` 循环及其 EOF 语义；`.vbx` 脚本执行模式与编译模式不受影响；测试沿用 Scripting\VisualBasicTest 的全内存 harness（现有 `TestConsoleIO` 只能整行喂、缺"行中间按键"接缝，缺口记入 Detailed design「测试约束」小节）。

## Motivation
[motivation]: #motivation

- **输入面现状：没有任何可挂补全的接缝。** vbi 交互式会话的读行循环是纯整行读入：`RunInteractiveLoopAsync` 先写 `> ` 提示（实锤：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:298`），再用 `_console.In.ReadLine()` 逐行读（实锤：同文件 `:305`），判定 `IsCompleteSubmission` 决定是否续行并写 `. `（实锤：同文件 `:319-326`）。整行读意味着**逐键编辑、光标位置、灰字渲染在读取层不可见**——补全必须下探到"逐键接管输入行"，这正是 L0 raw-key 行编辑器是主要成本的原因。而控制台抽象 `ConsoleIO` 只暴露 `In/Out/Error` 三个 `TextReader/TextWriter`，唯一可覆写的是设色 `SetForegroundColor/ResetColor`（实锤：`Scripting\Core\Hosting\CommandLine\ConsoleIO.cs:15-33`）——没有键盘事件通道。
- **上游与控制台现实：csi/vbi 从无补全，真 IntelliSense 只在编辑器进程内。** 上游 Roslyn 的控制台 csi/vbi 驱动**从没有补全**；真实的 Interactive Window IntelliSense 100% 在 Workspaces+Features 层：提交是 `isSubmission:true` 的 project 链式引用前一个 submission，正在敲的未提交文本是活的 Document，补全经 `CompletionService.GetService(Document).GetCompletionsAsync` 获得（外部基线参考，非本仓：`roslyn\src\EditorFeatures\Core\Interactive\InteractiveSession.cs` 与 `dotnet/interactive` 死项目的 `src\Microsoft.DotNet.Interactive.CSharp\CSharpKernel.cs` + `InteractiveWorkspace.cs`——它用 stock Roslyn NuGet，fork 编译器不同，不能照搬）。**本提案的价值位置正是上游从没做过的"控制台自身输入面"**。
- **期望结果**：用户在终端里敲 `items.Sor`，行尾灰字浮现 `t`（组合即 `Sort`），按 Tab 一键接受，减少长标识符/成员的反复击键与试错。该 UX 有成熟外部先例（常识性先例，非本仓源码）：PSReadLine 的 `PredictionSource`、zsh-autosuggestions、fish 的自动建议——均为"行内灰字首候选 + 一键接受"。
- **产品定位判例**：这是**易用性投资，不缩小与 C# REPL 的"功能差距"本身**——与 `proposal-avalonia-ise-repl-ui.md`、`proposal-vscode-extension-ise-repl-ui.md` 同判例（功能差距见 `../../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。本提案不做语言语法改动，不涉及 `decisions.md` 的 M1–M8 语言语义映射；D1–D4 属语言/编译器决策面，仅 D4 的"非语法提案按 LDM 风险评估定级"精神适用。

## Detailed design
[detailed-design]: #detailed-design

### 一、难度分档 L0–L3 与取舍

控制台补全的成本**几乎全部随"呈现复杂度"上升**，故按 UI 复杂度分档，而非按功能分档：

| 档 | 内容 | 呈现增量 | 取舍 |
|----|------|---------|------|
| **L0（地基，主要成本，不可省）** | raw-key 行编辑器（接管 `Console.In.ReadLine()`，逐键维护缓冲/光标）+ stdin 重定向/无控制台回退 + 平台矩阵（`vbi` 目标框架 net10.0-windows / net10.0 / net48，实锤：`Interactive\vbi\vbi.vbproj:7`；net10.0-windows 走 Windows Terminal/ConHost，net10.0 跨平台走 Linux/macOS ANSI，net48 走 ConHost）+ IME/宽字符 | 无灰字，纯"把读行替换为逐键" | **主要成本**。任何行内补全（无论 L1/L2/L3）都依赖它；它单独也带来"行编辑能力自持"的回归风险（见 Drawbacks） |
| **L1（行内灰字首候选）** | 在 L0 行编辑器上：光标行尾尾随一段**灰字建议**（＝排第一的候选），Tab/专用键接受；接受后灰字落为正文 | 只追加尾随文本渲染 + 按键分支 | **增量小、性价比最高**（v1 承诺） |
| **L2（Tab 顺序切换插入下一候选）** | L1 之上：首候选不合意时再按 Tab 换下一个候选插入 | 小 | 小增量；**延期**，触发条件＝L1 首候选命中率不足 |
| **L3（下拉弹层）** | 多候选/滚动/高亮/过滤/commit | 最大 | **最大增量，控制台性价比最低**（终端重绘弹层、多行布局、滚动交互全成本，收益却低于 GUI 编辑器）；**不承诺** |

**v1 = L0 + L1**。L2/L3 作为带触发条件的延期项：需要多候选浏览场景时再评估 L3；L1 首候选命中率不足时才上 L2 循环。本提案不展开 L3 设计。

### 二、L0 地基要点

- **仅交互式控制台接管**：raw-key 行编辑器只替换 `RunInteractiveLoopAsync` 的整行读路径（实锤：`CommandLineRunner.cs:296-326`），且仅在"确实拥有交互式控制台"时启用。**stdin 重定向 / 无控制台**（管道、`echo ... | vbi`、CI）时回退现有 `ReadLine` 循环，EOF/null 语义不变：读入 null 且缓冲空则 `return`（实锤：同文件 `:306-311`），缓冲非空则取消本次提交续跑（实锤：同文件 `:313-315`）。重定向探测可复用产品的既有先例——`Vbi` 已在 `PromptScriptError` 用 `Console.IsOutputRedirected/IsErrorRedirected` 区分"被管道/文件捕获"（实锤：`Interactive\vbi\Vbi.vb:86`）。
- **平台矩阵是 L0 的成本主体**：Windows Terminal（VT 序列、灰字可用 SGR）、传统 ConHost（net48 尤其老旧，VT/SGR 支持退化，灰字可能要退到反显或字符变暗）、Linux/macOS（ANSI/VT）。逐键读取 API（`Console.ReadKey`）与逐键重绘的差异在三种宿主上都要验证（估计/待定：此项是 L0 大部分工作量，见 Unresolved）。
- **IME/宽字符**：中文等宽字符输入法组合必须正确处理（组合态不触发、不破坏缓冲光标）——对含中文用户群是硬约束（估计/待定方案，见 Unresolved）。

### 三、L1 行内灰字建议

在 L0 行编辑器内，仅在下列条件下查询并**尾随一段灰字**：光标在**行尾**、且最后一个字符构成"待补全前缀"（标识符字符或紧跟 `.`）；非交互/重定向时不产生灰字。接受键默认 Tab（可配）；继续键入与建议冲突的字符时灰字即丢弃。v1 灰字只补**到标识符结束**，不自动补括号/实参（避免过度替用户决策）。

**可运行会话示例**（`> ` 为 REPL 提示符；灰字部分用注释标注，真实控制台为暗色/灰色渲染）：

```
> Dim names = New List(Of String) From {"alpha", "beta"}
> names.Sor            ' ← 输入到这里停下，光标在行尾
        ┌t┘            ' ← 灰字：排第一的候选 = Sort() 的尾段 "t"（图示方括号仅为示意灰字位置）
> names.Sort           ' ← 按 Tab：灰字 "t" 落为正文，光标停在行尾
> names.Sort()         ' ← 继续敲 "()"，回车执行
```

同一机制也覆盖 REPL 指令小表与关键字（首候选来自受控小表）：

```
> Dim n = 1
> For i = 1 To<灰字: n>   ' 续行/关键字首候选示意（多行提交按现有 `. ` 续行语义，灰字只在行尾触发）
```

（上面只是打字过程的示意；实际灰字文本取决于候选生成与排序，见下。）

### 四、浅档引擎（v1）

**不复制 Workspaces/Features**（本仓确实无该层：`VBInteractive.sln:6-75` 项目清单无 Workspaces/Features/LanguageServer；`Workspaces\` 下仅有 `SharedUtilitiesAndExtensions\Compiler\` 的 CompilerExtensions `.shproj`；`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:60` 还留着向上游 Features 授 internals 的 `InternalsVisibleTo`，但仓内无对应项目）。候选来源：

1. **会话重建语义模型**：对"当前缓冲（未提交）文本"做 `state.Script.ContinueWith(buffer)`——正是现有循环提交下一段用的同一条续接（实锤：`CommandLineRunner.cs:361` 用 `state.Script.ContinueWith(code, options)`；续接工厂 `Script.ContinueWith` 见 `Scripting\Core\Script.cs:100-118`），`GetCompilation()` 惰性产出"全会话 + 当前未提交行"的编译（实锤：`Script.cs:144-153`）。VB 侧 `CreateSubmission` 把前一次提交作为 `previousSubmission` 链式并入 `VisualBasicCompilation.CreateScriptCompilation`（实锤：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:164-168,208-232`），跨提交 `Imports` 合并复用现有 `GetGlobalImportsForCompilation`（实锤：同文件 `:113-120`）。因此不需要 speculative model——整棵含未提交行的树就是真实绑定上下文。
2. **用公开语义面查候选**：`SemanticModel.LookupSymbols(position[, name])`（实锤：`Compilers\VisualBasic\Portable\Compilation\SemanticModel.vb:1591-1596`）、`LookupNamespacesAndTypes(...)`（实锤：同文件 `:1707-1711`）、`GetSymbolInfo(...)`（实锤：同文件 `:162`）。**VB 无任意表达式 speculative model**——公开的只有按节点类型的 `TryGetSpeculativeSemanticModel`（statement/initializer/attribute/type/range/method-body，实锤：同文件 `:2149-2310`），所以设计上**不做任意表达式投机**，而是依赖上面的真实树重建。典型两分支：
   - **成员访问**（缓冲以 `receiver.` 结尾）：对 `receiver` 取 `GetTypeInfo`，再对容器查 `LookupSymbols`/成员表，按前缀过滤。
   - **裸标识符 / 语句开头**：合并 `LookupSymbols` + `LookupNamespacesAndTypes` 的范围内符号 + 受控小表（VB 常用语句关键字 + REPL 指令，指令清单见归档 `../vbx-1.2-beta/proposal-repl-directives.md`），按前缀过滤后取 top-1。
3. **排序取 top-1**：排序/匹配规则是 Unresolved（VB 大小写不敏感，需稳定、可预期的次序；"最近提交的符号优先"等启发见 Unresolved）。L1 只消费排第一的候选，不渲染候选清单。
4. **触发与成本**：灰字不逐键求值，做空闲/去抖触发 + 取消过期查询（估计：中小会话每次重建在可接受延迟内，大会话需节流，精确阈值待定）。触发仅限行尾（见 Unresolved）。

升级路径：Features 级补全（KeywordRecommenders、CompletionService 语义补全上下文）只在**候选质量成瓶颈**（如 top-1 命中率过低、用户明显需要多候选）时评估——其触发代价是把 Workspaces/Features 的 VB 源复制进仓并对 fork 编译器编译（dotnet/interactive 的"薄 glue ≈ 310 行"证明接线本身小，真正成本在补层本体），属大工程，与 `proposal-vbscript-lsp.md` 的补层评估同源。

### 五、兼容与测试约束

- 管道/脚本化保留：见 L0 回退。脚本模式（`.vbx` 文件执行）走 `RunScriptAsync`（实锤：`CommandLineRunner.cs:238`）、编译模式走 `VbiCompileMode`（实锤：`Vbi.vb:58-60`），均不经交互循环，本提案只动交互循环的读入路径，二者不受影响。商店版 WinUI 包装 `Installer\vbicore\App.xaml.vb:17-21` 只是 `OnLaunched → Vbi.OnStartupAsync → Exit` 的壳，本提案不改它。
- 测试无副作用：沿用 `Scripting\VisualBasicTest` 全内存 harness（`TestConsoleIO` 用 `StringReader/StringWriter` 在内存中喂/收，实锤：`Scripting\VisualBasicTest\Helpers\TestConsoleIO.vb:30-44`），不启进程、不写盘、不联网。**如实的影响**：现有 `TestConsoleIO` 只覆写 `ReadLine()` 整行喂入（实锤：同文件 `:39-43`），缺"行中间按键"接缝——L0/L1 需要新增一个"逐键流"测试替身（喂 `ConsoleKeyInfo` 序列给行编辑器状态机），并沿用其颜色捕获（`Writer` 记 `«颜色»` 标记，实锤：同文件 `:62-72`）断言灰字输出。这是本提案要求的新测试基建缺口。

## Drawbacks
[drawbacks]: #drawbacks

- **v1 手感上限**：单行、行尾、灰字首候选。多行提交、光标行中、需要浏览多个候选时不补；不呈现 doc/悬停。离 IDE IntelliSense 仍有明显距离，可能被部分用户视为"不够"。
- **浅档引擎相对 Features 的质量差**：KeywordRecommenders、上下文敏感补全（`New`、`Imports` 后、泛型实参等）不在 v1；受控小表需要维护；排序启发式是拍脑袋，命中率无先验保证。
- **控制台不呈现文档**：即便有符号 doc，灰字渲染空间也放不下；只能补"名字"，补不了"说明"。
- **L0 是主要成本且有回归风险**：raw-key 行编辑器要自持光标/左右移动/历史/IME，替换掉现在"系统 ReadLine 白送的编辑"，做不好是净退步；平台矩阵（尤其 net48 ConHost）与 IME/宽字符测试是硬成本。
- **新测试接缝缺失**：现有 harness 无逐键喂入能力，需补测试替身（上文"测试约束"），否则 L0/L1 无法无副作用验证。
- **延迟/闪烁感知**：每次触发若走"全会话重建"语义编译，大会话可能明显卡顿；去抖/节流做不好则灰字闪现即被覆盖，体验反而差。

## Alternatives
[alternatives]: #alternatives

- **纯控制台现状（不做）**：零成本、可管道化保持最简；但输入摩擦维持现状（无逐键编辑增强、无任何建议），且这正是不做任何事的代价——上游 csi/vbi 数十年来就是这个样子，用户已习惯，但也意味着本产品在此处永远零差异化。
- **v1 就做 L3 下拉弹层**：跳过 L1/L2，直接多候选弹层。控制台弹层的终端重绘/滚动/高亮实现成本最高、收益在终端里又打不过 GUI 编辑器，与控制台 REPL 的定位（可管道、轻量）相悖；不推荐，故 v1 只做 L1。
- **借道 Avalonia / LSP（相对容易，但有既有提案，偏离本次对象）**：`proposal-avalonia-ise-repl-ui.md` 与 `proposal-vbscript-lsp.md` 两条路线把编辑/补全宿主换到 Avalonia/VS Code 等成熟编辑面，确实**更容易**（编辑面自带补全/渲染）。但它们**不改善 vbi 控制台自身的输入面**——用户直接在终端敲 vbi 时依旧裸奔。两者对象不同、并行不悖；本提案守控制台本体，不展开其 GUI/LSP 设计。
- **补层做 Features 级真 IntelliSense 引擎**（喂给控制台行编辑）：把 Workspaces/Features 的 VB 源复制进仓、对 fork 编译，再叠一层控制台呈现。功能最强，但补层规模大（与 `proposal-vbscript-lsp.md` 的补层评估同源），对"控制台单候选灰字"而言严重过度，v1 明确不做；仅在浅档候选质量成为瓶颈时作为升级触发点。

## Unresolved questions
[unresolved]: #unresolved-questions

- **行编辑器自研 vs 现成库**：自研 raw-key 状态机（完全可控、无第三方依赖）还是接读行库？.NET 生态无官方 readline；第三方库的许可/维护、net48 兼容都需评估。
- **IME/宽字符方案**：组合输入态如何不触发/不破坏灰字；中日韩宽字符的光标位移与回退（backspace）处理口径。
- **触发时机**：v1 是否仅限**行尾**触发？行中光标、多行续行中的建议是否直接排除？空闲去抖的目标延迟与大会话的节流阈值？
- **Tab 键位约定 = 接受 vs 循环**：L1 只有首候选时 Tab=接受；将来若上 L2，Tab 变成"接受当前并切下一候选"还是"先循环后接受"，需在设计时锁定键位模型避免肌肉记忆冲突（与行编辑的未来 Tab 缩进/词补全是否冲突也一并定）。
- **引擎放 Scripting 层 vs 编译器内**：候选生成放 `Scripting\Core` 宿主层（用公开编译 API，可在 Scripting 测试 harness 内无副作用验证）还是下沉编译器（拿到 Friend 级内部便利但把补全逻辑耦合进编译内核）？倾向前者（估计），待 LDM。
- **候选排序/匹配规则**：VB 大小写不敏感下前缀匹配与排序的可预期规则（例如"先范围符号后容器成员""最近声明优先"？）；同分时的确定性次序，直接影响 top-1 手感。
- **net48 ConHost VT 退化路径**：灰字在 net48 传统 ConHost 上用什么呈现（SGR 灰字不可用则退反显/降亮？还是 net48 直接不启用 L1、只给 L0？），与平台矩阵成本挂钩。
- **Features 升级触发阈值**：用什么客观信号决定"浅档候选质量成瓶颈"（top-1 命中率统计、用户显式反馈、还是特性需求），触发后才评估补层（触发代价 = 复制 Workspaces/Features VB 源 + 对 fork 编译）。
