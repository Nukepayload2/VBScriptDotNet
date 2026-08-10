# VS Code 扩展：REPL/脚本编辑器（仿 vscode-powershell）/ VS Code Extension: REPL + Script Editor

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

> 状态说明：本提案为**新提案**（proposed）。按 `proposals/README.md` 的 active 目录规则，先归 active 根目录（`proposals/`）；**三态判定：待 LDM 会议评估**（对应会议纪要尚未创建，见 `meetings/README.md` 的会议索引；`proposals/README.md` 的索引更新由验证/调度阶段处理，本文件不改动其他文件）。

## Related
[related]: #related

- 姊妹提案（双路线 A）：[`proposal-avalonia-ise-repl-ui.md`](proposal-avalonia-ise-repl-ui.md) —— Avalonia 独立 GUI 仿 PowerShell ISE（LDM 判定 **Consider**，易用性提升）。本提案（双路线 B）是它的**姊妹方案/另一条路线**：不另做自定义 GUI，而是把「脚本编辑 + REPL + 即时输出」宿主进 VS Code，架构参考微软官方 PowerShell 扩展 vscode-powershell。两条路线**并行互补**，非替代关系。
- 两提案同根同源：解决同一批现状痛点（脚本编辑体验差、无语法高亮、无选择执行、无法同窗写脚本看输出），交付形态不同——Avalonia = 自主 GUI，本提案 = 宿主进成熟 IDE 生态。**并行互补**关系（非替代），分工与主线见 `Unresolved questions` 首位开放问题与 `Alternatives`。

## Summary
[summary]: #summary

本提案给 VBScript.NET 增加一个 **VS Code 扩展**：把 `.vbx` 脚本编辑、REPL（`vbi`）、即时输出与脚本调试全部放进 VS Code，仿 vscode-powershell 的架构——**在集成终端里跑一个 vbi 语言服务器进程，该进程同时是 REPL 控制台、LSP 语言服务器与（脚本运行时）DAP 调试适配器宿主**，扩展的 TS 客户端经命名管道连接它。

与 Avalonia 提案的分工（**并行互补**，非替代）：Avalonia = 自主 GUI 的 ISE 仿制品（轻量、低内存）；本提案 = 复用成熟 IDE 生态（宣传/触达占优），脚本体验落在 VS Code 里。两者共享同一 `vbi` 执行核心（产品架构链：`Interactive\vbi\Vbi.vb` → `Scripting\VisualBasic\VisualBasicScript.vb`（`RunInteractiveAsync`）→ `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`（common scripting fork）→ `Compilers\VisualBasic\Portable\`（改版 VB 编译器））。本提案**不改动核心层**，只新增一个「LSP 宿主」服务层（建议新项目 `LanguageServer\`）与一个 TS 扩展客户端。

## Motivation
[motivation]: #motivation

- **现状痛点**（同 `proposal-avalonia-ise-repl-ui.md`）：REPL 本体是控制台程序 `vbi.exe`，商店版带 WinUI3 启动器包装（`vbichooser`/`vbicore`/`vbifw`）。脚本编辑体验差：无语法高亮、无独立编辑窗口、无"选择执行"、无法在同一个窗口里一边写脚本一边看输出。
- **vscode-powershell 证明的模式**：微软官方 PowerShell 扩展是**成熟**的「脚本编辑 + REPL + 调试」范例——`pwsh` 进程同时是 REPL 与语言服务器（PSES），扩展经命名管道连接，真实编辑功能全在服务器端（C#）实现、经 LSP 上抛。这个「一个进程 = REPL + LSP + DAP」模式与 VBScript.NET 的 `vbi` 天然吻合：`vbi` 已经是 REPL，只需要给它加一个 LSP/DAP 宿主壳。
- **定位核心：宣传/触达 vs 内存/轻量（并行互补）**：本路线最大的卖点是**宣传与触达**——VS Code 扩展做得好，等于「在微软官方生态 / VS Code 市场里骑脸输出」，站在微软生态内的触达与宣传优势最大，这是选择 VS Code 路线最硬的理由；编辑器/终端/调试器/主题生态等 IDE 级编辑能力全部白拿。代价是 **VS Code 内存占用明显高于 Avalonia 独立 GUI**，而轻量脚本 REPL 用户（VBScript/VBA 迁移者、低配机）对内存敏感。因此本提案**不是绝对超越 Avalonia，而是作为一种补充**：VS Code 路线 = 触达/宣传优势 + IDE 级编辑能力；Avalonia 路线 = 轻量独立 GUI + 低内存 + 不依赖 VS Code 运行时。
- **与 Avalonia 提案的关系（双路线）**：Avalonia 提案 = 独立 GUI，完全掌控外观与布局、内存占用低；本提案 = 宿主进 VS Code，白拿 IDE 级编辑能力、触达宣传占优，代价是内存占用大、体验交给 VS Code 框架约束。两条路线是**并行互补**关系，非替代关系：共享同一执行核心（`vbi`），按定位分工（触达/宣传走 VS Code、轻量/低内存走 Avalonia）。主线/场景分工需 LDM 会议判定（见 `Unresolved questions` 首位开放问题）。

期望的结果：脚本用户可以在 VS Code 里编辑 `.vbx` 脚本、按 F8 选中片段进 REPL、按 F5 运行整脚本进调试器，在集成终端即时看到 `?` 打印结果与诊断，编辑体验（IntelliSense/hover/诊断/格式化）由基于 fork 编译器构建的语言服务器提供。

## Detailed design
[design]: #detailed-design

### 组件总览（架构图）

仿 vscode-powershell 的单扩展架构：TS 客户端（薄）+ 集成终端内的 vbi 语言服务器（厚），对照产品架构链：

```
+---------------------------------------------------------------------------------+
| VS Code 宿主（extension host）                                                   |
|                                                                                 |
|  TS 客户端（薄）——命令 / 键绑定 / 会话生命周期 / 调试配置                          |
|    │                                                                             |
|    │  LSP over JSON-RPC（命名管道 net.connect，非 stdio）                          |
|    ▼                                                                             |
|  集成终端（vscode.window.createTerminal）                                        |
|   一个进程 = REPL + LSP + DAP                                                    |
|   vbi 语言服务器（自包含 .NET 应用，基于 fork 编译器构建）                         |
|     ├─ REPL 控制台：.vbx 提交求值 / ? 打印 / #R / #Load / #help                    |
|     ├─ LSP 端点：completion / hover / signatureHelp / diagnostics / ...           |
|     └─ DAP 端点：脚本调试（运行脚本时挂载）                                       |
+---------------------------------------------------------------------------------+
      │
      │  提交-编译-求值管线（复用，spec/README.md 架构链）
      ▼
  Interactive\vbi\Vbi.vb
    → Scripting\VisualBasic\VisualBasicScript.vb        (RunInteractiveAsync)
      → Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs   (common scripting fork)
        → Compilers\VisualBasic\Portable\               (改版 VB 编译器)
```

要点：`vbi` 语言服务器进程复用现有 REPL 执行核心（`VisualBasicScript.RunInteractiveAsync` → `CommandLineRunner` → fork 编译器），不做核心层改动；扩展客户端是「薄壳」，只做连接、命令、调试配置与会话生命周期管理（照抄 vscode-powershell 的 `src/session.ts` `SessionManager` 与 `src/process.ts` `PowerShellProcess` 职责）。

### 扩展入口与 manifest

- **`package.json`**：main 指向 `dist/extension.js`，用 esbuild 打包（照抄 vscode-powershell 的构建约定）。
- **activationEvents**：`onLanguage:vbx`（打开 .vbx 激活）、`onCommand:vbs.RunSelection`、`onDebug`（调试解析时激活）、`onStartupFinished`（可选，做启动检测/会话预热）。
- **contributes**：
  - `languages`：定义语言 `vbx`（extensions `[".vbx"]`、aliases）。注意：`vbx` 是**新语言 ID**，VS Code 不内置（对照：`powershell` 语言 ID 与 grammar 是 VS Code 内置的，vscode-powershell 因此不贡献 languages/grammars 块）。
  - `grammars`：提供 `grammars/vb.tmLanguage.json`（见「语法高亮」）。
  - `commands`：`vbs.RunSelection`（F8）、`vbs.Debug.Start`（F5）、`vbs.ToggleISEMode` / `EnableISEMode` / `DisableISEMode`、`vbs.PositionPanelBottom` / `PositionPanelLeft`（终端面板位置）、`vbs.ShowCommandExplorer`（Command Explorer 等价物）。
  - `keybindings`：F8 → `RunSelection`；F5 → `Debug.Start`；Ctrl+Enter → `RunSelection` 备用（对齐 ISE 心智）。
  - `debuggers`：type `vbs`，含默认 launch 片段（`currentFile` 模式）与 `variables`（命令绑定，照抄 vscode-powershell `package.json:529`）。
  - `viewsContainers` / `views`：Command Explorer 等价物（脚本/命令大纲视图）。
  - `menus`：编辑器标题栏/上下文菜单「运行选中」「运行脚本」。
- 激活入口 `src/extension.ts`：注册命令、构造 `SessionManager`、配置 `vscode.languages.setLanguageConfiguration("vbx", …)`（括号、注释、onEnterRules 等，照抄 `src/extension.ts:49` 的 powershell 做法）。

### 语言服务器（LSP）

- **实现语言与位置**：vscode-powershell 的 PSES 是仓库之外的独立 C# 模块；VBScript.NET **没有现成的 PSES 等价物**，需要新写一个「LSP 宿主」服务。建议新项目 `LanguageServer\`（仓库根，自包含 .NET 应用），直接引用 fork 编译器 `Compilers\VisualBasic\Portable\` 与 REPL 执行核心（`Scripting\VisualBasic\VisualBasicScript.vb`、`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）。
- **传输与连接**：标准 LSP over JSON-RPC（`vscode-languageclient/node`）。**非子进程 stdio**——扩展经**命名管道**连接（照抄 `src/session.ts:953-958` 与 `:865-892`：`net.connect(languageServicePipeName)`）。握手：服务器启动后把 `languageServicePipeName` / `debugServicePipeName` 写进会话详情 JSON 文件（仿 `PSES-VSCode-<pid>-<id>.json`），扩展轮询（照抄 `src/process.ts:288-350`）。
- **能力清单**（映射 fork 编译器的语法树/语义模型/分类器）：
  - completion / completionResolve（语法/语义补全）
  - hover、signatureHelp
  - diagnostics（`publishDiagnostics`，didChange 后去抖）
  - documentFormatting / rangeFormatting
  - foldingRange
  - semanticTokens（可选，见「语法高亮」）
  - codeLens / codeActions（可选，如 `#R` 引用提示、`#Load` 跳转）
- **客户端薄**：除少量例外（如 codeActions/help completion 的客户端侧配合），不注册 `languages.register*Provider`，全部由服务器上抛（对照 vscode-powershell 的 PSES 模式）。解析/编译 hook 全在服务器端。
- **didOpen/didChange 门控**：didOpen/didChange 的转发受 `started` promise 门控，避免启动期 stale 诊断（照抄 `src/session.ts:500-514`）。
- **服务器反向驱动编辑器**：预留自定义 `editor/*` LSP 请求（insertText、openFile、setStatusBar 等），如 `#Load "file.vbx"` 可在 VS Code 新标签打开被加载文件、把某个 `#R` 引用的程序集详情写进状态栏。

### 集成控制台（REPL）

- **真实 VS Code 集成终端**：`vscode.window.createTerminal`（照抄 `src/process.ts:155-183`），终端内跑 `vbi` 语言服务器进程（即 REPL + LSP）。服务器在自身进程内宿主 console REPL（等价 vscode-powershell 的 `-EnableConsoleRepl`），REPL 输出**原生渲染到终端**，扩展不转发输出。
- **发一行/片段**：`vbs.RunSelection`（F8）——会话未激活/无选中时回退 `workbench.action.terminal.runSelectedText`；否则发自定义 LSP `evaluate` 请求把选中提交进 REPL，再把终端拉到前台滚动到底（照抄 `src/features/Console.ts:199` 与 `:14`）。
- **交互输入上抛**：服务器把 REPL 需要的提示/输入（`Console.ReadLine`、`InputBox` 等价物）经自定义 LSP 请求 `showChoicePrompt` / `showInputPrompt` 上抛为 VS Code quick pick（照抄 `Console.ts:71-137`）。
- **指令支持**：`#R`、`#Load "file.vbx"`、`#help`、`/help`、`/version`、`/?`、`@vbi.rsp`（`/r:` 与 `/imports:`）、`/i`、`-- script-args`。
- **提交管线**：选中片段/输入行提交进 `VisualBasicScript.RunInteractiveAsync` → `CommandLineRunner` → fork 编译器；`?` 打印结果与诊断回显终端（`ObjectFormatter` 格式）。
- **多行编辑/历史/提示符**：由终端内置能力免费获得（对照 PSReadLine 提供 PSES 端多行编辑/提示符/历史）。

### 运行脚本 / 调试

- **整脚本走调试器**：运行按钮 → `vbs.Debug.Start` → `vscode.debug.startDebugging(…, currentFile)`（照抄 `src/features/ExtensionCommands.ts:241-249`）——**整脚本不进普通 evaluate，走调试会话**。
- **调试适配器宿主在运行进程内**：`DebugConfigurationProvider` + `DebugAdapterDescriptorFactory`，`createDebugAdapterDescriptor` 返回 `DebugAdapterNamedPipeServer(debugServicePipeName)`——**无独立 DAP 可执行文件**，调试适配器宿主在已在跑的 REPL/语言服务器进程内（照抄 `src/features/DebugSession.ts:211/:386`）。
- **临时调试控制台**：F5 时若没有激活会话，另起一个进程以 `-DebugServiceOnly` 模式跑脚本+调试（照抄 `src/session.ts:443-490`）。
- **脚本模式与退出码语义**（以 `spec/README.md` 为准，已修复）：`Function Main` 退出码语义——`Return 42` → 退出码 42；裸 Return / 无 Return → 0；**末尾表达式不再设退出码**。实现依据：2.0 已改为 `CommandLineRunner.RunScriptAsync` 用 `CreateInitialScript<int>` 直接返回 `RunAsync(...).ReturnValue`（不再走 Object + 强转）。调试结束后的进程退出码语义与脚本模式一致。
- **会话生命周期**：扩展持有会话（SessionManager 等价物），服务器异常/终端关闭时重启会话并重挂功能 handler（照抄 `src/languageClientConsumer.ts` 抽象基类）。

### 语法高亮

- **关键差异**：PowerShell grammar 是 VS Code 内置；VB **不是**。本扩展必须自备 `.vbx` 的 TextMate grammar（`grammars/vb.tmLanguage.json`）。
- **建议做法**：在 VS Code 内置 VB grammar（`source.vb`，随 VS Code 分发，vendored 一份）基础上**派生扩展** `.vbx` 特有构造：
  - `?` 前缀（REPL 表达式打印；注意前缀可选项由 active 提案 `proposal-optional-question-prefix.md` 跟进，grammar 应同时覆盖）
  - 指令行：`#R`、`#Load`、`#help`
  - 头部宿主选择注释：`' Attribute TargetFramework`（识别出 `.NET` / `net48` 字符串可着色/区分）
- **补充手段**：用 LSP **semantic tokens** 补齐 grammar 覆盖不到的语义高亮（fork 编译器的分类器直接产 semantic token 数据），可大幅提升高亮精度；是否首版启用见 `Unresolved questions`。

### ISE 心智模型

- **settings toggle 模式**（照抄 `src/features/ISECompatibility.ts`）：`vbs.ToggleISEMode` 是**设置变更器**不是布局引擎——翻转一批设置（ISE 配色主题、tab 补全、`focusConsoleOnExecute=false`、`showPanelMovementButtons=true`、显示 Command Explorer 等），disable 时恢复（仿 `ISECompatibilityFeature.settings:17`）。
- **「上编辑下输出」分栏**：用 VS Code 原生拼出来——`vbs.PositionPanelBottom` / `PositionPanelLeft` 把终端面板移到侧/底部，编辑器+终端并排（照抄 `src/features/ExtensionCommands.ts:223-239`）。
- **Command Explorer 等价物**：提供脚本/命令大纲视图，仿 ISE 右侧 Command Explorer。
- **ISE 主题**：附带 ISE 配色主题（对照 vscode-powershell 的 `themes/theme-psise`），随 `ToggleISEMode` 启用。

### 分发

- **`.vsix`**：`vsce package` 打包，发布到 VS Code 市场 / Open VSX。
- **语言服务器随扩展交付**：自包含 .NET 应用（首版以 **net8.0** 为基线 TFM；若需与 2.0 分支的 .NET 10 对齐则升基线，未定，见 `Unresolved questions`），随 `.vsix` 附带，用户无需预装 .NET 运行时。
- **运行时宿主选择**：`.vbx` 头部 `' Attribute TargetFramework = "net48"` 注释选 net48 或 .NET 宿主——扩展开文件/运行脚本时读这行选进程宿主，映射 `vbi` 的 `-framework` 行为（对应 1.2 归档 `proposal-runtime-host-selection.md`）。
- **首次运行握手**：首次激活检测 vbi 核心、选定宿主、起终端进程、轮询会话详情 JSON 拿到管道名，建立 LSP 连接。

## Drawbacks
[drawbacks]: #drawbacks

- **依赖 VS Code 生态与市场分发**：用户必须安装 VS Code；扩展走市场分发（VS Code 市场 / Open VSX）受平台政策约束，不似商店版 MSIX 可自主控制。
- **内存占用高于 Avalonia 独立 GUI**：VS Code 本身内存占用大（宿主 + TS 客户端 + 语言服务器进程），明显高于 Avalonia 单进程 GUI。轻量脚本 REPL 用户（VBScript/VBA 迁移者、低配机）对内存敏感——这是相对 Avalonia 路线的**硬性 drawback**，也是本提案定位为「并行互补而非替代」的根本原因之一。
- **双组件维护面**：TS 客户端 + C# 语言服务器两套代码、两种语言与构建链，LSP 协议边界有调试成本（对照 Avalonia 单进程 GUI 更简单）。
- **双路线并存需定义关系**：与 Avalonia 提案同为易用性投资，若两者并行推进会分散 UI/集成资源，需明确主次与里程碑。
- **编辑体验受 VS Code 束缚**：布局、主题、键位、字体渲染全部继承 VS Code 框架（对比 Avalonia 自主 GUI 可完全定制 ISE 外观）。
- **优先级**：同 Avalonia 提案——这是易用性投资，不缩小与 C# REPL 的「功能差距」本身（功能差距见 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。

## Alternatives
[alternatives]: #alternatives

- **继续纯控制台**：零集成成本，保持可管道化（`echo ... | vbi`），但脚本编辑体验维持现状。
- **Avalonia GUI**（[`proposal-avalonia-ise-repl-ui.md`](proposal-avalonia-ise-repl-ui.md)）：自主 GUI、完全控制 ISE 外观；代价是双 UI 技术栈（现有商店版 WinUI3 + Avalonia）、编辑器控件能力有限（Avalonia Edit 的 VB 高亮需接 Roslyn 分类器或关键字降级）。
- **WinUI3 商店扩展**：在 `vbicore` 加编辑窗格；复用现有商店分发渠道；但 WinUI3 编辑器控件能力弱、跨平台无望。
- **语言服务器仅编辑不做 REPL（弱化版）**：只做 `.vbx` 编辑体验（IntelliSense/diagnostics/格式化），REPL 仍用控制台 `vbi`——范围小、起步快，但「同窗写脚本看输出」的核心诉求未满足，割裂。

## Unresolved questions
[unresolved]: #unresolved-questions

- **双路线并存关系（首要开放问题）**：与 Avalonia 提案是**并行互补**而非替代——VS Code 路线主打宣传/触达与 IDE 级编辑能力，Avalonia 路线主打轻量独立 GUI 与低内存。**谁是主线、何时用哪条**：两条都持续推进并共享执行核心，还是按场景分工（触达/宣传优先用 VS Code、轻量/低内存场景用 Avalonia）？判定需 LDM 会议（本提案三态判定待会议评估）。
- **语言服务器项目放仓库哪里**：新根目录项目 `LanguageServer\`？还是归入 `Scripting\`？它与 `Interactive\vbi\Vbi.vb` 的宿主关系如何（vbi 是否兼任 LSP 宿主，还是独立宿主进程）？
- **.vbx grammar**：用 VS Code 内置 VB grammar（vendored）派生扩展，还是重写一份？semantic tokens 是否首版就启用？
- **是否首版就带调试器**：调试器宿主在 REPL 进程内（`DebugAdapterNamedPipeServer`）复杂度高，是否推迟到里程碑 2、首版只做「编辑 + REPL」？
- **跨平台范围**：net48 宿主只在 Windows 有意义；Linux/macOS 仅 .NET 宿主——首版是否 Windows 优先？
- **语言服务器 TFM 基线**：net8.0（1.2 基线）还是对齐 2.0 分支的 .NET 10？自包含尺寸与免预装之间的权衡？
