# LSP 补层与最小原型：测试计划（lsp-m0）

依据：`README.md`（共享源码事实 SF-1…SF-13、代办列表 F1–F12）、`design-overview.md`。本文覆盖服务器侧 M0/M1 的验收矩阵；M2/M3 的能力矩阵待 F10 结论另立。

## 一、验收 gate

本任务通过（= M0 段 + M1 段，见 `README.md`「闸门（分两段）」）= T-1…T-13 全绿 **且** T-14 产出可核对的适配面清单（每条给「OK／需改（文件:行）／崩（复现用例）」三态之一）。缺 T-14 的清单不算通过——那正是会议留给 M0 的唯一实测入口。

## 二、验证档位与副作用纪律的分界

仓库既有纪律「无副作用单测」（`../../meetings/meeting-vbscript-lsp.md:64`）与本任务的部分行为「只有在真实构建／进程握手中才显现」相冲，按档分界，不混用：

| 层 | 用例 | 允许的验证手段 | 禁止 |
|----|------|--------------|------|
| **档 1 · 单测** | T-1…T-4、T-6（解析/诊断部分）、T-7…T-9、T-13 | 进程内断言；复用编译器测试基类 `Compilers\Test\Utilities\VisualBasic\BasicTestBase.vb` | 网络、写文件、起进程、改注册表 |
| **档 2 · 运行探测** | T-5、T-6（分类/格式化经 Features 路径）、T-10…T-12 | 在受控临时目录里拉起语言服务器进程并握手 | 写入仓库工作树；探测结果当档 1 交付 |
| **档 3 · 审查** | F1/F2 的清单闭合、T-14 中无法构造用例的「需改」条目 | 逐路径核对 + 文件:行 引用 | 冒充档 1 的「已运行」 |

档 2 用例的临时目录与进程生命周期由测试夹具负责，探测结论须在流水账登记档位与证据三态。

## 三、用例矩阵

| # | 场景 | 断言 | 档位 | 依据 |
|---|------|------|------|------|
| T-1 | VB Features 进 MEF 组合 | `SolutionServices.GetLanguageServices(VisualBasic).GetService<ICommandLineParserService>()` 非空；C# 侧同时非空（防误伤） | 1 | SF-2、SF-3、F4① |
| T-2 | 松散 `.vb` 文档 | `didOpen` 后 `document.Project.Language == VisualBasic`，`SourceCodeKind == Regular` | 1 | SF-10 |
| T-3 | 松散 `.vbx` 文档 | `document.Project.Language == VisualBasic` **且** `SourceCodeKind == Script` | 1 | SF-9、F8 |
| T-4 | 服务器用的是 fork 编译器（而非基线程序集） | 输出目录存在 VB 三件套（`Microsoft.CodeAnalysis.VisualBasic[.Workspaces/.Features].dll`）；并从该 VB 程序集反射得到 `Microsoft.CodeAnalysis.VisualBasic.Syntax.ShebangDirectiveTriviaSyntax`——基线 VB 编译器无此类型，故它是「换干净了」的判别 witness | 1 | SF-13、SF-5 |
| T-5 | `--autoLoadProjects` 认 VB 项目 | 指向含一个 `.vbproj`（+ 一个 `Program.vb`）的目录，自动装载后 `Project.Language == VisualBasic`、Documents 含源文件、`ParseOptions`/`CompilationOptions` 非空 | 2 | SF-4、F4② |
| T-6 | `#!` 探针（修改语法 × Features 的最小复现） | ①解析：首行 `#!/usr/bin/env vbx` 产出 `ShebangDirectiveTriviaSyntax` 节点；②诊断集合：常规 `.vb` 中首行 `#!` 得 37003，`.vbx` 中 `#!` 不在首行（或前置 trivia）得 37004，两条独立、可同时出现（`ParseConditional.vb:503` / `:511`），断言按集合而非二选一；③Features 路径：分类器、格式化、补全上下文在含 `#!` 的 `.vbx` 上不抛异常且不高亮错位 | 1 / 1 / 2 | SF-13、F10 |
| T-7 | `#R` 引用可见 | `.vbx` 内 `#R` 指向的 dll，其公开类型在编辑器内可补全、可 hover、无未解析引用诊断 | 1 | F8/F9 |
| T-8 | `#Load` 多文件 | 被 `.vbx` `#Load` 的文件：编辑器打开时作为同 Project 的 Document 参与，其内符号对宿主文件可见；未打开时按磁盘文本纳入隐藏文档，语义/诊断仍覆盖 | 1 | `design-overview.md` 七.2 |
| T-9 | 商店版特供头静默 | 首行含 `' Attribute TargetFramework = "net48"` 的 `.vbx`：**零新增诊断**，且编译引用集仍为 .NET 10 侧（`Interactive/vbi/vbi.coreclr.rsp`） | 1 | SF-8、README 范围节 |
| T-10 | Razor / 薄壳裁剪后仍自洽 | 构建产物 `deps.json` 不含被裁程序集；服务器进程可完成 `initialize` 握手；`.vbproj` + 松散 `.vb` + 松散 `.vbx` 三类文档并存互不污染 | 2 | SF-6、F5、F7 |
| T-11 | 最小 LSP 端到端 | `dotnet <LSP.dll> --stdio` 拉起 → `initialize`/`initialized` → 打开 `.vbx` 收到 `textDocument/publishDiagnostics`（内容含一条真实 fork 诊断） | 2 | F7 |
| T-12 | vbx 编辑能力非空 | `.vbx` 内：诊断（含 host object / 提交结果判定相关）、补全（含脚本顶层成员）、hover 三项均有可断言的非空结果 | 2 | `../../meetings/meeting-vbscript-lsp.md:75` M1 |
| T-13 | 编译器回归面不动 | `Compilers\*Test` 与 `Scripting\VisualBasicTest` 全量结果与复制前基线一致（IDE 栈复制不改编译器） | 1 | SF-1 |
| T-14 | 适配面清单 | 按 `README.md`「语法适配面的口径」派生条目，逐条给三态结论 + 文件:行 或复现用例；条目集合不回写进提案/会议纪要 | 2 / 3 | README 口径节 |

## 四、借自成本基线的部分

`{{VbLs}}` 随其 1 行 MEF 改动一起交了 108 行组合层测试（`LanguageServerCompositionTests.cs`）。本计划直接沿用其断言形状，并做三处加码：

| 原断言 | 本计划 | 加码理由 |
|---|---|---|
| C# 与 VB 的 `ICommandLineParserService` 均已组合 | T-1 | 原样沿用（这条正是 SF-2 那道门的守门断言） |
| 松散文件按扩展名映射到预期语言（`.cs`/`.vb`） | T-2 + **T-3** | 原基线只覆盖 Regular；`.vbx` 必须额外断言 `SourceCodeKind`，因为上游 LanguageServer 层零处该概念（SF-9） |
| 输出含 VB 三件套程序集（存在性） | **T-4** | 从「文件存在」升级为「是 fork 的那一个」，用 `ShebangDirectiveTriviaSyntax` 作判别 witness（SF-13 控制组） |
| `.vbproj` 经 project open 装载 | T-5 | 原样沿用，改档 2 真跑 |

不抄的部分：vb-ls 的 vendoring 方式（它 vendor 上游整树，本任务复制进仓库；见 `design-overview.md` 八）。

## 五、残余风险与不覆盖项

- **M2 能力面不覆盖**：signatureHelp / semanticTokens / 格式化 / `#Load` 跳转 / `#R` 导航在本任务只要求「不崩」（T-6③），不要求「正确」。格式化正确性待 F10 的适配面结论后另立任务。
- **LanguageServer 层测试工程是否随复制进来**未决：`Microsoft.CodeAnalysis.LanguageServer.UnitTests` 提供 `TestLspServer` 一类的夹具，T-2/T-3/T-11 高度依赖它。复制它=多一层依赖闭包（xunit、TestUtilities）；不复制=自建夹具。该判断属 F1 的闭包定论之一，结论进 F1 产物并同步到本节表格。
- **档 2 用例的机器时延**（拉起真实 MSBuild workspace + 首次 restore）可能超既有测试超时设定；T-5/T-10/T-11 的超时阈值待 F7 首次实测后登记，不预先臆测。
- **不测客户端**：任何 `.vsix` / Zed 扩展行为不在本矩阵内。
