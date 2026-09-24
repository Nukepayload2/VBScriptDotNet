# 任务：LSP 补层与最小原型（lsp-m0）设计任务

本文件夹是 `proposal-vbscript-lsp` 的 **M0/M1 服务器侧闸门**设计任务存储（Vortex 代办列表 + 设计产物）。它的使命单一：把「从上游基线补 IDE 栈 + 接入 `.vbx`」这条路径跑到**可实测**，并产出「修改语法 × Features 层假设」适配面的定量结论。

- **依据链**：`../../proposals/proposal-vbscript-lsp.md`（提案，Active（Proposed））→ `../../meetings/meeting-vbscript-lsp.md`（LDM 会议，RESOLUTION #1–#7；**升级实现的闸门 = M0 原型验证通过**）→ `../../upstream-merge.md` 第一节（上游基准 commit / 分支，即本任务的复制基线）→ `../../compilers-index.md`（编译器索引）。
- **交付物**：`design-overview.md`（复制、裁剪、构建对齐、接入形状）、`test-plan.md`（验收矩阵）。`design-detailed.md` 是 F10 的下游产物：适配面的逐处落点要等实测结果，无实测即无详细设计对象。
- **调度方式**：Vortex 涡流触媒（实施者产出 → 验证者核对 → 打回修复 → 通过关闭），main 只调度。
- **流水账**：`<项目根>/tmp/vortex-logs/lsp-m0/`。

## 范围

**做（只做服务器侧）**

1. 从上游基线复制 IDE 栈进本仓库。
2. 两处上游侧改动落到复制品：VB Features 进 MEF 组合、`*.vbproj` 进自动装载枚举。
3. 裁剪 Razor 与 VS 专用薄壳依赖。
4. 脱离 Arcade，对齐 fork 构建（对着 fork 编译器编译）。
5. `.vbx` 接入为 script-mode 松散文件；`#R` / `#Load` 的引用解析接进 Workspaces。
6. dotnet tool 打包骨架。

**不做**

- **`' Attribute TargetFramework`**：商店版 launcher 的进程派发信号，编译器与 vbi 引擎都不读（SF-8）。LSP **不实现、不诊断、静默**——与 dotnet tool 分发形态下的既有行为逐字一致。
- **引用集切换**：固定 .NET 10，取 `Interactive/vbi/vbi.coreclr.rsp` 一侧（`vbi.vbproj:46-51` 表明 `vbi.desktop.rsp` 属 net48 分支）。
- **VS Code / Zed 客户端、REPL、DAP**：属 `proposal-vscode-extension-ise-repl-ui`；本任务只交服务器（`../../meetings/meeting-vbscript-lsp.md:80`）。
- **Avalonia ISE 集成**：那条路是进程内直连 fork 编译器，不经 LSP。

## 共享源码事实

| # | 事实 | 锚点 | 断言状态 |
|---|------|------|---------|
| SF-1 | fork 编译器的上游基线 = `{{Roslyn}}` 的 `release/stable` @ `0e401fcf66c…`（基准日期 2026-07-27） | `../../upstream-merge.md` 第一节；本机检出一致 | 实锤 |
| SF-2 | 上游装载工程前有一道硬门：要求该语言的 `ICommandLineParserService` 已在 MEF 组合中 | `src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/LanguageServerProjectLoader.cs:292` | 实锤 |
| SF-3 | 上游把 VB 掉在组合之外：`<!-- Dlls we don't directly reference but need to include to build the MEF composition -->` 注释下只列 C# Features；VB Features 工程确实存在 | `.../Microsoft.CodeAnalysis.LanguageServer.csproj:72-73`、`src/Features/VisualBasic/Portable/Microsoft.CodeAnalysis.VisualBasic.Features.vbproj` | 实锤 |
| SF-4 | `--autoLoadProjects` 只 glob `*.csproj`。该方法在本基线已重构（同法内改为枚举 `*.sln` + `*.slnx`，且用 `s_recursiveEnumerationOptions` 取代 `SearchOption.AllDirectories`），外部补丁的 hunk 上下文对不上，须按现状重写 | `src/LanguageServer/Microsoft.CodeAnalysis.LanguageServer/HostWorkspace/AutoLoadProjectsInitializer.cs:84-85, :106` | 实锤 |
| SF-5 | 成本基线 vb-ls 的**生产改动共 2 行**：`lsp-vb-support.patch`（135 行，其中 108 行是新增测试 `LanguageServerCompositionTests.cs`）+ `autoload-vb-projects.patch`（18 行，1 行改动）；launcher 35 行：`dotnet <LSP dll>` + 原样透传参数，`UseShellExecute=False` 且不重定向任何流 ⇒ 子进程继承 stdio，JSON-RPC 零胶水穿透；打包 59 行：`PackAsTool` + `ToolCommandName`，用 `TargetsForTfmSpecificContentInPackage` 挂一个 Target 现场 restore/build 上游 LanguageServer，再把其 `net10.0` 输出整目录塞进 `tools/net10.0/any/roslyn-lsp/` | `{{VbLs}}`：`vb-ls/patches/*.patch`、`vb-ls/Program.vb`、`vb-ls/vb-ls.vbproj:7-22, :29-58` | 实锤 |
| SF-6 | Razor 与 VS 专用薄壳在本基线的耦合形态：`csproj:76`（`Tools/ExternalAccess/RazorCompiler`）、`csproj:87`（`src/Razor/src/Razor/src/Microsoft.VisualStudioCode.RazorExtension`，注释要求它作为真运行时依赖进 `deps.json`）、`csproj:79-80`（AspNetCore / Copilot）。`src/LanguageServer` 内 `using` Razor 命名空间的 `.cs` = **2**，且分属两个工程：宿主 `HostWorkspace/Razor/TelemetryReporterWrapper.cs` 与协议层 `Protocol/Handler/SpellCheck/WorkspaceSpellCheckHandler.cs`。字面含 “Razor” 的 `.cs` = 40（其中测试树 10、生产 30；口径为字面串命中，含注释与分支名） | 同左 | 实锤 |
| SF-7 | 复制面规模（两种口径，不可混用）：**A · 六目录生产口径**（`.cs`+`.vb`）= Features/Core 1254 + Features/VisualBasic 478 + Workspaces/Core 781 + Workspaces/VisualBasic 87 + LanguageServer/Protocol 817 + LanguageServer 宿主 103 = **3520**。**B · 整树口径**（含 C# 与全部测试工程）= `src/Features` 2820 + `src/Workspaces` 2302 + `src/LanguageServer` 1126 = 6248。两者不是「加不加 C#」的差关系。A 也只是目录计数上限，实际复制集以工程引用闭包为准（F1 的职责） | `git ls-files` 计数 | 实锤（数字）／推测（闭包等价性） |
| SF-8 | `' Attribute TargetFramework` 的唯一读者是商店版 launcher：Regex 读 `' Attribute <name> = "<value>"`，认 `TargetFramework` 且值以 `net4` 开头时派发 `vbifw\vbi.exe`，否则派发 `vbichooser\vbi.exe`。`Interactive/` + `Scripting/` 全树读取点 **0** | `Installer/vbichooser/Program.vb:101-102, :110-111, :119, :123, :127`；另见 `../../meetings/meeting-vbi-nuget-reference.md:149` 登记的已知限制 | 实锤 |
| SF-9 | **`SourceCodeKind` 在上游 `src/LanguageServer` 层出现 0 次**；script 地基在 Workspaces 层（`fileArg.IsScript ? SourceCodeKind.Script : Regular`、`Solution.cs` 的 script 分支）。⇒ 「基线原生支持 Script 文档」成立于 Workspaces 层；LanguageServer 侧的 script 文档形状**没有现成通路**，是本任务的主要未知 | `src/Workspaces/Core/Portable/Workspace/CommandLineProject.cs:167`、`src/Workspaces/Core/Portable/Workspace/Solution/Solution.cs:1457`；`grep SourceCodeKind src/LanguageServer` = 0 | 实锤 |
| SF-10 | 松散文件项目化的入口候选：`src/LanguageServer/Protocol/Workspaces/LspWorkspaceManager.cs`（**在协议层，不在宿主 `HostWorkspace/`**）、`HostWorkspace/LanguageServerWorkspaceFactory.cs`、`HostWorkspace/LanguageServerProjectSystem.cs:116`（项目文件扩展名注册表）。**文件监视侧没有静态 `*.cs`/`*.vb` 白名单**：`HostWorkspace/FileWatching/DefaultFileChangeWatcher.FileChangeContext.cs:70-72` 按被跟踪文件自身的扩展名派生 `WatchedDirectory`，`filters` 由调用方经 `CreateContext` 传入（其测试里的 `["*.cs","*.vb"]` 是测试自喂的输入，`DefaultFileChangeWatcherTests.cs:109-110`）⇒ 调用方是谁、`.vbx` 要不要在那里登记，待 F8 定位，不预设计数。`HostWorkspace/ExtensionManager.cs` 是 MEF 的 `IExtensionManager`，与文件扩展名无关 | 同左 | 已检查（职责边界已核，行级落点待 F1/F8 定） |
| SF-11 | 协议与框架层不必从网络 vendor：本机 NuGet 缓存已含 `microsoft.codeanalysis.languageserver.protocol`、`microsoft.commonlanguageserverprotocol.framework`、`microsoft.visualstudio.languageserver.protocol{,.extensions,.internal}`、`microsoft.servicehub.framework`、`microsoft.visualstudio.composition`；协议源码在本基线的 `src/LanguageServer/Protocol`（**本基线无顶层 `src/Protocol`**） | `~/.nuget/packages`；`src/LanguageServer/Protocol/Microsoft.CodeAnalysis.LanguageServer.Protocol.csproj` | 实锤 |
| SF-12 | Workspaces / Features 工程靠 `<Import>` 拉共享源，复制闭包必须含它：`src/Workspaces/Core/Portable/Microsoft.CodeAnalysis.Workspaces.csproj:184-186` 导入 `SharedUtilitiesAndExtensions/Compiler/{Core,Extensions}` 与 `Workspace/Core` 三份 `.projitems`，`src/Workspaces/VisualBasic/Portable/*.vbproj:53` 另导入 `Compiler/VisualBasic`。本仓库根**已有**一棵同名剪枝树 `Workspaces\SharedUtilitiesAndExtensions\Compiler\Core\`（377 个 `.cs`/`.vb`，上游整树为 940，且上游另有 `Compiler/{Extensions,VisualBasic}` 与 `Workspace/` 两支）⇒ 复制前须定两件事：复用既有剪枝树还是补齐到上游面；以及它与新建 `LanguageServer\Workspaces\` 的目录语义相撞问题 | 同左 | 实锤 |
| SF-13 | 修改语法已进入编译器公共 API 面，且上游 VB 无对应构造。控制组：fork 有 `ShebangDirectiveTriviaSyntax.vb`、`ERR_ShebangDirectiveOnlyAllowedInScripts = 37003` / `ERR_ShebangDirectiveNotOnFirstLine = 37004`、`PublicAPI.Unshipped.txt` 的新节点与 `Content()`/`ExclamationToken()` 成员；上游 `src/Compilers/VisualBasic/Portable/` 对 “Shebang” **0 命中**，上游 C# 则有同名节点。两条诊断彼此独立：`ParseConditional.vb:503`（非 script 模式）与 `:511`（位置不在首行 / 中间夹 trivia）可在同一份源码上同时触发 | `Compilers/VisualBasic/Portable/Syntax/ShebangDirectiveTriviaSyntax.vb`、`Compilers/VisualBasic/Portable/Errors/Errors.vb:1633-1634`、`Compilers/VisualBasic/Portable/PublicAPI.Unshipped.txt:1-5`、`Compilers/VisualBasic/Portable/Parser/ParseConditional.vb:503, :511` | 实锤 |

## 语法适配面的口径（本任务不维护逐条清单）

M0 要量的「修改语法 × Features 假设」适配面，其输入集合按取用口径界定：

1. `../../spec/README.md` 在册的产品脚本方言能力条目；
2. fork 编译器 `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` 中基线不存在的成员（SF-13 的控制组比对法即按此执行）。

本任务只固定一条硬性探针要求：**首行 `#!` 的 `.vbx` 必须在 M0 走通解析、诊断、分类**（它是唯一有控制组证据的「上游 VB 不存在的语法节点」）。其余条目由 F10 报告按上述口径派生，不回写进本文件或会议纪要。

## 代办列表（Vortex 功能拆分）

| # | 功能 | 验收条件（pass 标准） | 验证档位 | 状态 |
|---|------|---------------------|---------|------|
| F1 | 复制清单闭合 | 从 LanguageServer 宿主工程递归求 `ProjectReference` 闭包，得目录级清单；每项标 复制／裁剪／跳过 + 理由；闭包内无未判定项；`<Import>` 进来的共享源（SF-12）补齐范围有定论；SF-7 口径 A 的 3520 上限被收敛为实际集；本条产出即 F2 核对脚本的输入清单 | 档 3 | 待办 |
| F2 | 复制 IDE 栈进仓库 | 按 `design-overview.md` §三 的根镜像布局落位；F1 清单内每个文件与基线**逐文件哈希一致**（差集只允许是 F3 的路径改写白名单行），由脚本核对 | 档 1（脚本）+ 档 3（清单齐全性） | 待办 |
| F3 | 路径与 Arcade 剥离 | 复制品在 fork 的 `Directory.Build.props` 下能求值出 `NetVSCode` / `IsShipping` 等所需属性；csproj 相对路径全部指向仓库内；无 Arcade 导入残留；改写行全部落在 F2 白名单内可枚举 | 档 3（静态核对）+ 档 2（MSBuild 求值实测） | 待办 |
| F4 | 两处上游侧改动落地 | ① `csproj:72-73` 锚点下补 VB Features 引用后，SF-2 那道 MEF 门对 VB 放行（可断言）；② `AutoLoadProjectsInitializer.cs` 按本基线现状补 `*.vbproj` 枚举，`--autoLoadProjects` 实测能装载一个 `.vbproj` | ① 档 1 ② 档 2 | 待办 |
| F5 | Razor / VS 专用薄壳裁剪 | 按 SF-6 逐条摘除；`deps.json` 无被裁项目残留；构建零错误 | 档 2 | 待办 |
| F6 | 构建对齐 | IDE 栈全部工程对着 fork 编译器编译通过；对「fork 比基线多公共 API 成员」的 `PublicAPI.*` 分析器处置有明确结论（SF-13） | 档 2 | 待办 |
| F7 | 最小 LSP 起进程 | `dotnet <LSP.dll> --stdio` 可拉起并完成 initialize 握手；一个 `.vbproj` + 一个松散 `.vb` 双文档并存 | 档 2 | 待办 |
| F8 | `.vbx` script-mode 接入 | SF-9 的未知收敛为唯一落点；`.vbx` 松散文档得到 `LanguageNames.VisualBasic` + `SourceCodeKind.Script`；文档路由与文件变更监视两条路径都吃得到 `.vbx`（落点按 SF-10 定位，不预设数目） | 档 1 | 待办 |
| F9 | `#R` / `#Load` 引用解析接入 | script 工程编译引用对齐 `Interactive/vbi/vbi.coreclr.rsp`；`#R` 指向的 dll、`#Load` 载入的文件在编辑器内均可见符号与诊断；含 `' Attribute TargetFramework` 的脚本**零新增诊断** | 档 1 | 待办 |
| F10 | 适配面实测报告 | 按上述口径派生条目，逐条给「Features 路径 OK／需改（文件:行）／崩（复现用例）」；含 `#!` 探针结论 | 档 2 | 待办 |
| F11 | 验收矩阵落测试 | `test-plan.md` 全绿；档 1 用例严守无副作用纪律（不发起网络、不写文件、不起进程、不改注册表），档 2 用例的进程与临时目录由夹具负责并在流水账登记 | 混合（按 `test-plan.md` §二 分表） | 待办 |
| F12 | 打包骨架（不阻塞闸门） | `PackAsTool` + 打包 Target 产出可 `dotnet tool install` 的包；launcher 的拉起方式与 F7 同源（同一 stdio 继承路径，不重定向任何流） | 档 2 | 待办 |

**闸门（分两段）**

- **M0 段** = F1–F7、F11：补层进仓库、裁剪、对齐构建、最小 LSP 起进程、双模式文档并存、验收矩阵绿。
- **M1 段** = F8–F10：`.vbx` script-mode 接入、`#R`/`#Load` 引用解析、适配面实测清单。
- 两段全过 = 本任务通过 = 会议 `../../meetings/meeting-vbscript-lsp.md:80` 所设的 proposal-03 升级前提（「M0/M1 通过后」）成立。F12 可后置。

**档位 tie-break 说明**：F2 的复制是纯机械动作，已为其造出可脚本化的核对（F1 清单内逐文件与基线哈希一致，差集限定为 F3 的路径改写白名单），故升档 1；只有清单齐全性那一半留在档 3。F1 的验收对象本身是「复制／裁剪／跳过」的取舍判断，无运行可证，落档 3，其结论不得当档 1 交付。F3/F4/F11 存在两档等价可行的验证方式，按可断言的部分拆档：静态核对该落点/该行为档 3，MSBuild 求值与真跑装载探测档 2，进程内断言档 1。F5/F6/F7/F10/F12 的行为只在真实构建或进程握手中显现，落档 2。

## 停止规则

- 复制闭包中出现「既无法编译、又无法裁剪」的 VS 专用依赖 → 停止，回决策层：PROPOSAL C 的前提被动摇，需重议 PROPOSAL B。
- `#!` 探针在 Features 层崩，且修复面扩大到整个分类器/格式化管线 → 停止，把「修改语法是否进 IDE 栈」升为用户决策。
- 任一功能落不进任何一档充分验证 → 验证者报「测不了」，main 向用户说明并询问，不静默降档、不以档 3 冒充档 1。
- F10 报告的适配面条目只写入本任务的产物，不回写 `../../meetings/`（判定文档不承载动态状态）。
