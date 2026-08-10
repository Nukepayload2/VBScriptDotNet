# Visual Basic Language Design Meeting
August 10, 2026

本次会议是 `proposal-vscode-extension-ise-repl-ui` 的专用 1:1 会议。先导会议 `meeting-vb-repl-parity-with-csharp-repl.md`（2026-08-09）已把 avalonia-ise-repl-ui 判为 **Consider**（易用性投资、方向认可）、optional-question-prefix 判为 **Active**；本提案与其**并行互补**。本次不重复产品盘点和 csharplang 全景，而是回到源码把「VS Code 扩展如何运行 vbi REPL/脚本」的多条候选运行架构逐一摆出并权衡，并把 VS Code 的竞品 **Zed**（原生 Rust 编辑器）作为一条**并行/替代/补充**的候选路线摆上桌——两条技术路线（vscode-powershell 一进程模式 vs Zed 的 stdio 独立 LSP 模式）构成「运行方式」权衡的地基，本次会议把这些机制核实清楚，给出三态判定。

## Agenda

* [Proposal: VS Code 扩展 REPL/脚本编辑器](#proposal-vs-code-扩展-repl脚本编辑器)

## Proposal: VS Code 扩展 REPL/脚本编辑器

_Related: `../proposals/proposal-vscode-extension-ise-repl-ui.md`（主检对象）；姊妹 `../proposals/proposal-avalonia-ise-repl-ui.md`（Consider，并行互补）；先导 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`；active 提案 `../proposals/proposal-optional-question-prefix.md`（`?` 可选，衔接语法高亮/打印路径）；C# 对照：`csi` 敲表达式即打印（LDM-2020-04-15 interactive 设置）；`../proposals/vbx-1.2-beta/`（REPL 能力版本归档，含 `proposal-runtime-host-selection.md`）_

### 场景与缺口

- **现状痛点**（同姊妹提案，`proposal-vscode-extension-ise-repl-ui.md` Motivation）：REPL 本体是控制台 `vbi.exe`，商店版带 WinUI3 启动器包装（`vbichooser`/`vbicore`/`vbifw`）。脚本编辑体验差：无语法高亮、无独立编辑窗口、无「选择执行」、无法在同一个窗口里一边写脚本一边看输出。
- **宣传/触达 vs 内存/轻量（并行互补定位）**：VS Code 路线的核心卖点是**宣传与触达**——在微软生态/VS Code 市场里骑脸输出，IDE 级编辑能力（编辑器/终端/调试器/主题生态）全部白拿；代价是 **VS Code 内存占用明显高于 Avalonia 独立 GUI**，而轻量脚本 REPL 用户（VBScript/VBA 迁移者、低配机）对内存敏感。因此本路线**不是绝对超越 Avalonia，而是作为一种补充**（双路线并行互补，见 `Unresolved questions` 首位）。
- **本会议新增的场景维度**：竞品 **Zed**（原生 Rust、GPU 加速、**本体内存占用很低**）也支持标准 LSP——理论上 vbx 能以「Zed 扩展」形式集成进去。这把「运行方式」从「VS Code 单客户端」扩展成「VS Code / Zed 双客户端」+「Avalonia 独立 GUI」的三路线棋盘（详见候选方案）。

### 现状机制（源码核实）——本会议新增价值所在

本次会议不重复先导会议的方案盘点，而是把两条技术路线的「运行机制地基」逐条核实清楚，作为候选方案（PROPOSAL A–D）的实证底座。以下事实均按**仓库相对路径**标注，已源码核实，直接作为提案 Detailed design 的参照。

#### 路线一：vscode-powershell 一进程模式（`G:\Projects\vscode-powershell\`）

- **单扩展**：`package.json`（main→`dist/extension.js`，esbuild 打包）；`src/extension.ts` 激活入口；`src/session.ts` `SessionManager`（找 pwsh、终端内拉 LSP、持 LanguageClient）；`src/process.ts` `PowerShellProcess`（包装 `vscode.Terminal` 跑 pwsh + Start-EditorServices，轮询会话详情 JSON）。
- **集成控制台 PIC（Process In Console）**：真实 VS Code 集成终端（`vscode.window.createTerminal`，`src/process.ts:155-183`），pwsh 进程**就是**语言服务器，PSES 在**同一终端**内宿主 console REPL（`-EnableConsoleRepl`），输出原生渲染，PSReadLine 提供多行编辑/提示/历史。握手：PSES 写 `PSES-VSCode-<pid>-<id>.json`（含 `languageServicePipeName`/`debugServicePipeName`），`src/process.ts:288-350` 轮询。
- **语言服务器传输**：LSP over JSON-RPC，命名管道连接（`net.connect`，`src/session.ts:865-892`），**非** stdio 子进程。编辑功能全在 PSES（C#，仓库外独立模块），客户端几乎不注册 provider。
- **发一行/片段 vs 运行整脚本**：选中 = F8 `RunSelection` → 自定义 LSP `evaluate`（`src/features/Console.ts:199`，:14），或回退 `terminal.runSelectedText`；整脚本 = `PowerShell.Debug.Start` → `vscode.debug.startDebugging(LaunchCurrentFile)`，**走调试器**（`src/features/ExtensionCommands.ts:241-249`）。提示/输入经 LSP `showChoicePrompt`/`showInputPrompt` 上抛为 quick pick（`src/features/Console.ts:71-137`）。
- **ISE 兼容**：`src/features/ISECompatibility.ts` 是**设置变更器**（toggle 一批设置，不是布局引擎）；`PositionPanelLeft/Bottom`（`src/features/ExtensionCommands.ts:223-239`）移动终端面板拼「上编辑下输出」。
- **调试**：`DebugAdapterDescriptorFactory`（`src/features/DebugSession.ts:211/:386`）返回 `DebugAdapterNamedPipeServer(debugServicePipeName)`——调试适配器宿主在**已在跑的 PIC 进程内**，无独立 DAP 可执行文件。
- **可复制模式总结**：一个进程 = REPL + LSP + DAP；内置终端当 REPL；真实编辑功能全在服务器；「运行选中」= evaluate、「运行脚本」= 调试会话；ISE 兼容 = settings toggle；调试器宿主在运行时进程内。

#### 路线二：Zed 扩展机制（`G:\Projects\zed-powershell\` 与 `G:\Projects\zed-dotnet\`，本会议新加入的竞品维度）

- **Zed 定位**：原生 Rust、GPU 加速编辑器，**本体内存占用很低**（对比 VS Code 是 Electron/Chromium 外壳，内存占用明显更高）。**支持标准 LSP**（语言服务器经 **stdio** 启动），因此理论上任何语言都能以「Zed 扩展」集成进去——vbx 也不例外。
- **扩展形态（`zed-powershell` 实证）**：
  - `extension.toml`：`id`/`name`/`version`/`schema_version`；`[grammars.powershell]` 指向 tree-sitter grammar 仓库+commit（如 `airbus-cert/tree-sitter-powershell`）；`[language_servers.powershell-es]` 声明语言服务器。
  - `src/powershell.rs`：Rust cdylib，实现 `zed::Extension` trait；`language_server_command()` 返回 `zed::Command { command: pwsh, args: [-NoLogo -NoProfile -Command "Import-Module '<PSES>/PowerShellEditorServices.psd1'; Start-EditorServices -Stdio -SessionDetailsPath '<bundle>/powershell-es.session.json' -HostName zed ..."] }`——注意 **`-Stdio`**：Zed 语言服务器经 **stdio** 启动（非命名管道）。
  - 语言服务器二进制：`zed::latest_github_release("PowerShell/PowerShellEditorServices")` + `zed::download_file` 自动下载 PSES zip 解压（`src/powershell.rs:104-148`）。
  - `languages/powershell/config.toml`：`name`/`grammar`/`path_suffixes=["ps1","psm1"]`/`line_comments`/`brackets`/`tab_size`。
  - 语法高亮用 **tree-sitter**（`highlights.scm`/`indents.scm`/`brackets.scm`/`outline.scm`），**不是** TextMate grammar。
  - `Cargo.toml`：`zed_extension_api = "0.7.0"`，crate-type cdylib。
- **Zed .NET 实证（`zed-dotnet\csharp\`，与 vbx 同属 .NET 生态，最具参考性）**：
  - `extension.toml`：`[language_servers.roslyn]` 声明 Roslyn 语言服务器（`language = "CSharp"`）；`[grammars.c_sharp]` 指向 tree-sitter-c-sharp。
  - `src/language_servers/roslyn.rs`：语言服务器二进制从 **NuGet 包**下载（`roslyn-language-server.<rid>`，按平台 RID 选 win-x64/linux-x64/osx-arm64 等），`nuget.download_and_extract` 后 `zed::Command { command: "dotnet", args: ["exec", "<dll>", "--stdio", "--autoLoadProjects"] }`（或直接跑 .exe）。——**Roslyn 语言服务器经 `--stdio` 接入 Zed，这与 vbx「基于 fork 编译器构建 LSP 宿主」完全同构**。
  - `languages/csproj/tasks.json`：Zed 任务（Restore/Build，`$ZED_FILE` 变量，`use_new_terminal`）——Zed 有任务/终端面板机制可跑命令/脚本。
  - **Zed 集成 console REPL 的方式**：与 vscode-powershell 不同，Zed 的语言服务器经 stdio 是**独立进程**（不共享终端），PSES 的 `-EnableConsoleRepl` 在 Zed 里**没有等价物**（「语言服务器兼任 REPL」不可行）。Zed 里 REPL 要么在独立终端面板跑 `vbi.exe`，要么靠 Zed 任务机制跑脚本。这是与 VS Code 路线的重要架构差异。

#### VBScript.NET 产品事实（`../spec/README.md`、`../compilers-index.md`）

- **架构链**：`Interactive\vbi\Vbi.vb` → `Scripting\VisualBasic\VisualBasicScript.vb`（`RunInteractiveAsync`）→ `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（common scripting fork）→ `Compilers\VisualBasic\Portable\`（fork 的改版 VB 编译器）。
- **2.0 已修复**：顶层 Await/AddHandler/RemoveHandler、Imports 累积、Function Main 退出码（`Return 42`→42；裸 Return/无 Return→0；末尾表达式不设退出码）；已移植 `#Load`。
- **fork 编译器树**：修剪/本地化的 Roslyn 编译器树（netstandard2.0;net10.0），**无独立 Workspaces/LSP 层**——LSP 宿主须**自建**（建议新项目 `LanguageServer\`），直接引用 fork 编译器 `Compilers\VisualBasic\Portable\` 的语法树/语义模型/分类器。
- **语法高亮**：VB 无 VS Code 内置 grammar（PowerShell 有）；`.vbx` 须自备 TextMate grammar，或走 LSP semantic tokens。Zed 侧则用 tree-sitter grammar——VS Code 与 Zed 的 grammar 体系不同，需双份或各走语义 token。
- **运行时宿主选择**：`.vbx` 头部 `' Attribute TargetFramework = "net48"` 注释选 net48 或 .NET 宿主（对应 1.2 归档 `proposal-runtime-host-selection.md`）。

### 候选方案：不同运行方式（核心）

**PROPOSAL A — vscode-powershell 一进程模式（VS Code 内）。** 真实 VS Code 集成终端内跑一个 `vbi` 语言服务器进程 = REPL + LSP + DAP，命名管道连接（照抄 `src/process.ts`/`src/session.ts`），REPL 与语言服务器**共享进程状态**（submission 历史/Imports 累积/globals 直接可得——REPL 侧补全上下文天然完整）。`-EnableConsoleRepl` 等价物：`vbi` 语言服务器进程内宿主 console REPL，`?` 打印原生渲染。**缺点**：Zed 无此模式（stdio 独立进程做不到「兼任 REPL」）；与 VS Code 深度耦合（TS 客户端 + 命名管道 + 集成终端）。

**PROPOSAL B — 独立 LSP 宿主进程（stdio）+ 独立 REPL。** 语言服务器独立进程经 stdio（可**同时被 VS Code 与 Zed 复用**——参照 `zed-powershell` 的 `-Stdio` 与 `zed-dotnet` 的 `--stdio`）；REPL 在终端面板单独跑 `vbi.exe`，选中提交经管道灌进 REPL（类似 evaluate 但要解决进程间状态）。**缺点**：REPL 与 LSP 状态**分离**——提交历史/补全上下文要同步，评估 REPL 时 IntelliSense 上下文弱；「同窗写脚本看输出」仍满足，但「REPL 内敲的补全」体验打折。

**PROPOSAL C — Zed 扩展路线（本会议新增）。** 写一个 Zed 扩展（Rust cdylib + tree-sitter grammar + 语言服务器 stdio + 任务面板跑脚本），把 vbx 集成进 Zed。Zed 本体低内存、原生快，契合「轻量脚本 REPL 用户」。**缺点**：REPL 无「语言服务器兼任」等价物（-EnableConsoleRepl 在 Zed 不存在）；Zed 生态/用户盘面远小于 VS Code；调试需另行评估（无 DAP 兼任模式）。**定位：并行补充线，不阻塞主线。**

**PROPOSAL D — 弱化版：仅编辑不做 REPL。** LSP 只做 IntelliSense/diagnostics/格式化，REPL 仍控制台 `vbi`。范围小、起步快，但「同窗写脚本看输出」的核心诉求未满足，割裂（提案 Alternatives 已列）。作为首版降级路径保留。

### 权衡：Q&A

- **VS Code（宣传/触达 + 一进程 REPL）vs Zed（低内存 + 原生快）vs Avalonia（自主 GUI + 低内存）——三路线定位与互补关系。** VS Code = 微软生态内触达/宣传占优 + IDE 级编辑能力 + 一进程 REPL（A 模式独占）；Avalonia = 自主 GUI、完全控制 ISE 外观、低内存；Zed = 原生低内存 + 快，但 REPL 集成弱、生态小。三者**并行互补**：共享同一 `vbi` 执行核心，按场景分工（触达/宣传走 VS Code、轻量/低配走 Avalonia 或 Zed），**非绝对超越**（不得写成「VS Code 优于 Avalonia」）。
- **一进程（A）vs 独立 LSP（B/C）的 REPL 状态同步代价。** A 模式 REPL 与 LSP 同进程，submission 历史/Imports 累积/globals 直接共享，REPL 侧补全上下文天然完整（vscode-powershell 的 PSES 一进程正是为此）；B/C 模式 LSP 是独立进程，REPL 敲入的内容要回灌 LSP 才谈得上补全同步——代价随「REPL 敲的内容复杂度」增长。**结论：REPL 场景优先一进程（A），跨客户端复用靠「LSP 宿主核心可被多种宿主壳装载」解决（见 RESOLUTION）。**
- **内存对比：VS Code（Electron，高）> Avalonia（中等）> Zed（低）。** 三路线对「轻量脚本 REPL 用户」的适配度：VS Code 最差（用户得先付 Electron 内存代价），Avalonia/Zed 更友好。这是本提案定位「并行互补而非替代」的根本原因之一——**轻量/低配用户不该被逼进 VS Code**。
- **实现成本：A 复用 vscode-powershell 模式最顺；B/C 的 LSP 宿主可跨客户端复用，是最高价值资产。** A 是「照抄」——vscode-powershell 的进程/握手/evaluate/调试宿主模式全部可映射；B/C 的关键洞见是：**一个 LSP 宿主（基于 fork 编译器）同时服务 VS Code 与 Zed**——这是**最高价值资产**，一次构建、两端复用。首版不必在「A 还是 B」上二选一：把 LSP 宿主做成「核心可被 A 的一进程壳与 B/C 的 stdio 壳复用」即可。
- **与 avalonia 提案的关系。** 本提案（VS Code + Zed 双客户端）与 avalonia 仍为**并行互补，非替代**：共享同一 `vbi` 执行核心与打印路径（`ObjectFormatter`）；avalonia 主打自主 GUI/轻量，本提案主打生态触达/IDE 能力。判定依据维持先导会议 Consider 的先例。

### 深度追问：LDM 拷问清单

1. **一进程模式（A）的 REPL 状态同步优势 vs 跨客户端复用（B/C）——LSP 宿主作为独立资产，一次构建多次复用（VS Code + Zed）？** 会议结论：**两者不冲突**。REPL 体验要状态共享（A 一进程），跨客户端复用要 stdio（B/C）。化解：LSP 宿主拆成「编译器集成核心（基于 fork 编译器，提供补全/诊断/hover/格式化）」+「宿主壳（VS Code 一进程壳 = REPL+LSP+DAP 同一进程；stdio 壳 = 独立进程经 stdin/stdout）」两层。核心一次构建，壳按客户端选择。**LSP 宿主是两端共同的最大资产，必须先立起来。**
2. **`?` 可选（active 提案 `proposal-optional-question-prefix`，Active）与本扩展的语法高亮/打印路径如何衔接？** 编译器层定案后（Script kind 末尾表达式绑定为 RValue + 解析层裸表达式语句），`.vbx` 里裸表达式变「静默合法（no-op，不设退出码）」——本扩展的 grammar/semantic tokens 应**同时覆盖 `?` 前缀与裸表达式**（`?` 显式前缀保留兼容）；打印路径复用同一条 `ObjectFormatter`/`globals.Print`（两提案共享执行核心）。本会议确认：**打印路径单一化，不另起打印实现**。
3. **net48 与 .NET 双宿主在 VS Code / Zed 下如何选择与分发（自包含尺寸 vs 免预装）？** 与 1.2 `proposal-runtime-host-selection.md` 一致：`.vbx` 头部 `' Attribute TargetFramework = "net48"` 注释选宿主。VS Code 路线可随 `.vsix` 附带自包含 .NET 应用（免预装，但体积大；首版基线 net8.0 或对齐 2.0 的 .NET 10，未定，见 OPEN QUESTIONS）；Zed 路线可仿 `zed-dotnet` 从 NuGet 下载按 RID 的语言服务器（免随扩展携带）。net48 只在 Windows 有意义——**首版 Windows 优先**（LSP 宿主 netstandard2.0;net10.0 双 TFM 已具备）。
4. **grammar 体系分裂：VS Code 用 TextMate、Zed 用 tree-sitter——是否首版都走 semantic tokens 统一？** 两端 grammar 体系不同（TextMate vs tree-sitter），维护双份成本高。**建议首版以 LSP semantic tokens 为主**（fork 编译器的分类器直接产 token 数据，两端共用同一服务器输出）；TextMate/tree-sitter 只做最低限度兜底（关键字/注释基础着色）。这同时兑现「LSP 宿主一次构建两端复用」。
5. **内存定位：VS Code 路线对低配机/轻量用户不友好，是否因此把 Zed/Avalonia 作为低内存补充线？** **是**。会议确认：VS Code 路线主打宣传/触达，**不承诺低内存**；轻量/低配场景由 Avalonia（自主 GUI）与 Zed（低内存）补充。这强化三路线并行互补定位，也意味着本提案**不得以「内存优化」为卖点**与 avalonia 竞争，而是以生态触达取胜。
6. **Zed 生态风险：Zed 用户盘面小、LSP 是独立进程（无 REPL 兼任），是否只做「编辑 + 任务跑脚本」不做 REPL？** 会议倾向：Zed 扩展**首版只做「编辑 + 任务跑脚本」**（PROPOSAL D 的 Zed 版：IntelliSense/diagnostics + `tasks.json` 跑 `vbi script.vbx`），REPL 仍由终端面板跑 `vbi.exe`。理由：Zed 无「语言服务器兼任 REPL」机制，硬做 REPL 需另搭进程间状态同步（ROI 低）；Zed 定位本就是「编辑器」，REPL 让它做不擅长的事。**Zed = 轻量编辑补充线，Recognized 不阻塞主线。**
7. **测试纪律：无副作用单测；LSP 宿主可用编译器测试基类。** 照 `../compilers-index.md`：`Test\Utilities\VisualBasic` 的 `BasicTestBase`、`VisualBasicCommandLineTest` 的 `CommandLineTests` 是模板——LSP 宿主的补全/诊断/格式化逻辑应复用编译器测试基类做**无副作用**单测（不发起网络/文件写入/进程启动/注册表写入）。REPL 语义测试沿用 `Scripting\VisualBasicTest\`（`CommandLineRunnerTests.vb` 等）。TS 客户端侧逻辑薄，仅做纯函数单测（状态机/命令映射）。

### RESOLUTION:

1. **方向判定：采纳为方向，判定 Consider（参考 avalonia 先例，理由见下）。** VS Code 扩展路线（REPL + 脚本编辑 + 即时输出宿主进 VS Code）方向成立，价值真实（宣传/触达 + 一进程 REPL + IDE 级编辑能力），且 vscode-powershell 提供了**成熟可复制**的现成模式。但：与 avalonia 同理，这是**易用性投资**（不缩小与 C# REPL 的功能差距），且成本更高（TS 客户端 + C# LSP 宿主双组件、两套语言与构建链）——故参考 avalonia 的 Consider 先例给 **Consider**（方向认可、归 active 根目录，暂不立即进入实现规划）；待 LSP 宿主最小原型验证补全/诊断后，可升级为 Active。
2. **运行方式：首版走 A+B 结合——REPL 与 LSP 一进程为主，LSP 宿主设计成 stdio 可复用。** 在 VS Code 内以**一进程模式（A）**交付（集成终端内 `vbi` 语言服务器进程 = REPL+LSP+DAP，命名管道，REPL 状态同步优势保住）；同时把 **LSP 宿主拆成「编译器集成核心」+「宿主壳」两层**，核心经 stdio（`--stdio`）可被 Zed（C）与独立进程（B）复用。**LSP 宿主是两端共同的最大资产，必须先立起来**（先以独立 stdio 进程验证补全/诊断，再接一进程 REPL）。
3. **Zed 作为并行补充线（Recognized，不阻塞主线）。** 方向认可但不进入首版规划：Zed 扩展首版只做「编辑 + 任务跑脚本」（语义 token 高亮 + diagnostics + `tasks.json` 跑 `vbi script.vbx`），REPL 由终端面板跑 `vbi.exe`；**Zed 不做 REPL**（无「语言服务器兼任 REPL」机制，硬做 ROI 低）。复用同一 LSP 宿主核心（stdio）。
4. **三路线并行互补关系确认。** VS Code（生态触达/一进程 REPL）+ Zed（低内存编辑补充）+ Avalonia（自主 GUI/轻量）**并行互补，非替代**；共享同一 `vbi` 执行核心与 `ObjectFormatter` 打印路径。**不得以「内存优化」为卖点与 avalonia 竞争**（VS Code 内存高于 Avalonia/Zed 是硬性 drawback）。
5. **语法高亮首版以 LSP semantic tokens 为主**，TextMate/tree-sitter 只做最低兜底——兑现「LSP 宿主一次构建两端复用」，规避双 grammar 体系维护成本。
6. **首版 Windows 优先**（net48 宿主只在 Windows 有意义）；LSP 宿主 netstandard2.0;net10.0 双 TFM（fork 编译器已具备）。

### Implication:

- **spec 合并要点**（`../spec/README.md` 架构链不变，只新增服务层）：新增 `LanguageServer\` 项目（建议仓库根，自包含 .NET 应用），直接引用 `Compilers\VisualBasic\Portable\` 与 `Scripting\VisualBasic\VisualBasicScript.vb`/`CommandLineRunner.cs`；架构写清「编译器集成核心 + 宿主壳」两层（VS Code 一进程壳 / stdio 壳），命名管道握手（`PSES-VSCode-<pid>-<id>.json` 等价物）+ evaluate + `showChoicePrompt`/`showInputPrompt` + `DebugAdapterNamedPipeServer` 语义全部落文档。
- **最小原型（分三步，串行推进）**：① 先 **LSP 宿主独立进程（stdio）** 验证补全/诊断（基于 fork 编译器语法树/语义模型/分类器，复用 `BasicTestBase` 无副作用单测）→ ② 再加 **REPL 一进程**（VS Code 集成终端内宿主 console REPL，`?` 打印 + evaluate + 命名管道握手）→ ③ 再看 **Zed 扩展**（复用同一 LSP 核心 stdio 接入 + tree-sitter/task 面板）。
- **测试矩阵**：LSP 宿主——补全/诊断/格式化/hover 无副作用单测（`Test\Utilities\VisualBasic\BasicTestBase` 模板）；REPL 语义沿用 `Scripting\VisualBasicTest\CommandLineRunnerTests.vb`；TS 客户端仅纯函数单测；**全程不得引入网络/文件写入/进程启动/注册表写入等副作用**。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`LanguageServer\` 项目放仓库哪里（根目录 vs `Scripting\` 下）；它与 `Interactive\vbi\Vbi.vb` 的宿主关系（vbi 兼任 LSP 宿主 vs 独立宿主进程）——RESOLUTION #2 倾向「vbi 语言服务器进程内宿主 console REPL（一进程）+ 独立 stdio 壳复用核心」，仍需细化。
- `OPEN QUESTIONS`：语言服务器 TFM 基线——net8.0（1.2 基线）还是对齐 2.0 分支的 .NET 10；自包含尺寸 vs 免预装权衡。
- `OPEN QUESTIONS`：首版是否带调试器——`DebugAdapterNamedPipeServer` 复杂度高，是否推迟到里程碑 2、首版只做「编辑 + REPL」（提案 Alternatives/Unresolved 已列）。
- `OPEN QUESTIONS`：Zed 扩展是否立项（Recognized 但未排期）；tree-sitter VB grammar 选型（社区是否有成熟仓库，仿 `airbus-cert/tree-sitter-powershell` 的先例）。
- `TODO`：LSP 宿主核心最小原型（stdio，补全/诊断），复用编译器测试基类补无副作用单测。
- `TODO`：semantic tokens 管线（fork 编译器分类器 → token 数据），作为两端共用的高亮主通道。
- `TODO`：`?` 可选（active 提案）落地后，grammar/semantic tokens 同时覆盖 `?` 前缀与裸表达式，并回归打印路径单一化。
- `Follow-up`：与 Avalonia GUI（`proposal-avalonia-ise-repl-ui`）并行推进时共享执行核心与打印路径；双路线里程碑/主次判定见提案 `Unresolved questions` 首位。

### 状态

- **LDM 状态**：**Consider**。
- **三态判定：Consider**——方向成立（vscode-powershell 成熟模式可复制、宣传/触达占优、一进程 REPL 状态同步优势），但为易用性投资、成本高于 avalonia（TS + C# 双组件）；参考 avalonia 先例给 Consider（归 active 根目录）。升级 Active 的闸门：LSP 宿主最小原型（stdio 补全/诊断）验证通过。Zed 扩展 Recognized 不阻塞主线。

---

## 附录：Zed 与 VS Code 生态对照

> 依据：`G:\Projects\vscode-powershell\`、`G:\Projects\zed-powershell\`、`G:\Projects\zed-dotnet\`（均已源码核实）。产品事实以 `../spec/README.md` 与 `../compilers-index.md` 为准。

| 维度 | VS Code 路线（vscode-powershell 实证） | Zed 路线（zed-powershell / zed-dotnet 实证） |
|------|----------------------------------------|-----------------------------------------------|
| 运行时外壳 / 本体内存 | Electron/Chromium，内存占用明显更高 | 原生 Rust、GPU 加速，本体内存占用很低 |
| 语言服务器连接 | 命名管道 `net.connect`（`src/session.ts:865-892`），非 stdio | **stdio**（`language_server_command` → `zed::Command`，`-Stdio` / `--stdio`） |
| REPL 集成 | 集成终端内进程**兼任 REPL**（`-EnableConsoleRepl` 等价物），状态共享 | LSP 独立进程，**无「兼任 REPL」机制**；REPL 走终端面板 `vbi.exe` 或任务面板 |
| 语法高亮 | TextMate grammar（`.tmLanguage.json`；VB 需自备，可 vendored 派生） | tree-sitter grammar（`highlights.scm`/`indents.scm`/`outline.scm`） |
| 调试 | DAP，`DebugAdapterNamedPipeServer` 宿主在运行进程内（无独立 DAP 文件） | 无 DAP 兼任；调试需另行评估（任务面板/原生调试支持） |
| 语言服务器分发 | 随 `.vsix` 附带自包含 .NET 应用（免预装，体积大） | `zed::latest_github_release` / `nuget.download_and_extract` 自动按 RID 下载（`zed-powershell\src\powershell.rs:104-148`；`zed-dotnet\csharp\src\language_servers\roslyn.rs`） |
| 市场 / 分发 | VS Code 市场 / Open VSX（`.vsix`，`vsce package`） | Zed 扩展市场（`extension.toml` 清单） |
| 编辑功能来源 | 全在服务器（PSES 模式），TS 客户端薄 | 同左：全在 LSP 服务器（如 Roslyn 语言服务器），Rust 扩展薄 |
| 生态盘面 | 微软生态内、用户量大、IDE 能力白拿 | 用户盘面小但增长快、原生轻量 |
| 任务/脚本运行 | 终端 + 调试器（`vscode.debug.startDebugging`） | `tasks.json`（`$ZED_FILE`、`use_new_terminal`，`zed-dotnet\csharp\languages\csproj\tasks.json`） |

**vbx 在两端集成的差异与复用点**：

- **复用点（最高价值资产）**：同一个基于 fork 编译器构建的 **LSP 宿主核心**，经 VS Code 命名管道（一进程壳）或 Zed stdio（独立壳）两端接入——一次构建、多次复用。semantic tokens 主通道同样两端共用（fork 编译器分类器直接产 token 数据），规避 TextMate 与 tree-sitter 双 grammar 维护。
- **差异**：VS Code 能走一进程 REPL（`-EnableConsoleRepl` 等价物），REPL 状态与 LSP 共享、补全上下文完整；Zed 只能独立进程 LSP + 终端面板 REPL，REPL 场景天然弱一档——因此 **REPL 主线在 VS Code（A），Zed 只做「编辑 + 任务跑脚本」（C 的弱化版）**。
- **分发差异**：VS Code 侧随扩展带自包含 .NET 应用（免预装）；Zed 侧可仿 `zed-dotnet` 从 NuGet 按 RID 拉语言服务器（免随扩展携带）——两种分发可共享同一套 netstandard2.0;net10.0 构建物。
- **定位**：Zed 是低内存/原生快的补充线，服务「不想背 Electron 内存、但想要现代编辑器」的轻量用户；与 Avalonia（自主 GUI、完全控制 ISE 外观）在「轻量」上重叠但交付形态不同（Zed = 宿主进第三方编辑器，Avalonia = 自家 GUI）。三者并行互补，共享 `vbi` 执行核心。
