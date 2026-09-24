# LSP 补层与最小原型：概要设计（lsp-m0）

依据：`README.md`（共享源码事实 SF-1…SF-13、代办列表、停止规则）、`../../meetings/meeting-vbscript-lsp.md` RESOLUTION #1–#7。本文只定结构与做法，不含逐条语法状态。

## 一、目标与非目标

**目标**：在本仓库内得到一个能跑的 VBScript.NET 语言服务器（独立进程、stdio），对普通 `.vbproj` 与 `.vbx` 脚本双模式提供诊断／补全／hover，并把「修改语法 × Features 层假设」的适配面从未知变成有清单、有复现用例的已知。

**非目标**：VS Code / Zed 客户端、REPL 一进程壳、DAP、dotnet tool 上架（F12 仅到「能打包」）、Avalonia 集成、`' Attribute TargetFramework`、net48 引用集。

## 二、分层

| 层 | 内容 | 来源 | 本任务动作 |
|----|------|------|-----------|
| 编译器 | `Compilers\VisualBasic\Portable\`（本地修改面见 `../../upstream-merge.md` 第二节，不在本文重抄） | 已在树内 | 只读，不改 |
| IDE 栈 | Workspaces（Core + VisualBasic）、Features（Core + VisualBasic）、LanguageServer/Protocol、LanguageServer 宿主 | 上游基线 `{{Roslyn}}` `release/stable` @ `0e401fcf66c…` | **复制进仓库**（PROPOSAL C） |
| 接入层 | `.vbx` 扩展名注册、script-mode 文档形状、`#R`/`#Load` 引用来源 | 新建 | 本任务的主要未知（SF-9） |
| 宿主分发 | launcher + `PackAsTool` 打包 Target | 抄 `{{VbLs}}` 结构（SF-5） | 建骨架 |
| 客户端 | VS Code / Zed | 不在范围 | 不动 |

**长期约束**（继承 `../../meetings/meeting-vbscript-lsp.md:54`）：IDE 栈只消费编译器公共 API（`InternalsVisibleTo` 不授 IDE 栈），因此 fork 不得破坏公共 API 面；IDE 栈版本随复制时点固定，升级需重新复制对齐。

## 三、仓库结构

沿用 fork 已确立的那条约定——**根目录镜像上游 `src\` 的层名**（`Compilers\` 就是这么来的）。于是复制目标不是新造命名空间，而是把层补进同名根目录：

```
Workspaces\                      （已存在：SharedUtilitiesAndExtensions\）
  SharedUtilitiesAndExtensions\   现状保留，按 SF-12 决定补齐范围
  Core\                          ← 复制自 src\Workspaces\Core
  VisualBasic\                   ← 复制自 src\Workspaces\VisualBasic
Features\                         新建
  Core\                          ← 复制自 src\Features\Core
  VisualBasic\                   ← 复制自 src\Features\VisualBasic
LanguageServer\                   新建
  Protocol\                      ← 复制自 src\LanguageServer\Protocol
  Microsoft.CodeAnalysis.LanguageServer\   ← 复制自同名工程
  VBScriptDotNet.Scripting.Server\         新建：`.vbx` 接入层（路由 + script 形状 + 引用解析）
  VBScriptDotNet.Server.Test\              新建：无副作用单测
```

这样定的三条理由：一是 SF-12 的命名相撞被结构性消解——根 `Workspaces\` 本来就是上游 `src\Workspaces\` 的剪枝镜像，复制 `Core\`/`VisualBasic\` 进同棵树是顺理成章，不需要第二个「Workspaces」概念；二是 `<Import>` 与 `ProjectReference` 的相对路径形状与上游一致（层级差一层，机械改写），升级 diff 最小；三是 IDE 栈程序集名与强名保持不变，fork 编译器产出的 `Microsoft.CodeAnalysis.VisualBasic.dll` 直接对接，「换没换干净」可用 assembly identity 断言（`test-plan.md` T-4）。

新建工程（`VBScriptDotNet.*` 前缀）与复制品（保留上游工程名与版权头）在同一根目录下分列，便于对基线做 diff 式升级。

**复制闭包优先于目录计数**：SF-7 口径 A 的 3520 只是目录上限；实际集由 F1 从宿主工程递归求 `ProjectReference` 闭包得出，闭包外的目录一律不复制（不「整块搬来再说」）。

**仍未决的是共享源补齐范围**（SF-12）：根 `Workspaces\SharedUtilitiesAndExtensions\` 现有 377 个代码文件，上游同名整树 940，且上游另有 `Compiler/{Extensions,VisualBasic}` 与 `Workspace/` 三支——复制闭包要求哪些支、缺的那 563 个是否都在要求范围内，归 F1 定论。

## 四、两处上游侧改动

1. **VB Features 进 MEF 组合**——在 `Microsoft.CodeAnalysis.LanguageServer.csproj` 的组合注释（基线 `:72`）之下、C# Features 那行（基线 `:73`）之后加一行 VB Features 的 `ProjectReference`。这条不是编译依赖：LanguageServer 代码不 `using` VB Features，但 `ExportProvider` 只扫输出目录里的 DLL，缺它则 SF-2 那道 `GetLanguageService<ICommandLineParserService>` 门对 VB 不放行。按该门的写法（拿不到服务即判定项目不可用），可预期的症状是 **VB 项目根本不装载**，而非「装载了但没补全」——具体症状待 F4① 实测确认，不预先断言。
2. **`*.vbproj` 进自动装载枚举**——落点是 `HostWorkspace/AutoLoadProjectsInitializer.cs` 的 csproj glob 处（基线 `:106`）。本基线该方法已重构（同法内先枚举 `*.sln`/`*.slnx`，且用 `s_recursiveEnumerationOptions`），所以外部补丁的 hunk 上下文不可直接套用，按现状补一行。

两处改动各自配一条可断言的验证（F4）。

## 五、裁剪清单（Razor 与 VS 专用薄壳）

本基线的 Razor 耦合形态见 SF-6，裁剪做法是**摘依赖 + 清引用点**，不是删整棵 `src\Razor`：

- `csproj:76` `Tools/ExternalAccess/RazorCompiler` 引用 → 摘。
- `csproj:87` `src/Razor/src/Razor/src/Microsoft.VisualStudioCode.RazorExtension` 引用 → 摘。该处注释要求它作为真运行时依赖进 `deps.json`，摘后须验证 `deps.json` 与服务器启动路径不再期待该程序集（F5 的判据）。
- `csproj:79-80` AspNetCore / Copilot 薄壳 → 摘（Copilot 侧含 `LanguageServer/Handler/CopilotCompletion/*`，须与 `:87` 一并处理，避免 MEF 导入悬空）。
- 真 `using` Razor 命名空间的 2 个 `.cs` 分属两工程，处置不同（SF-6）：宿主侧 `HostWorkspace/Razor/TelemetryReporterWrapper.cs` 是 Razor 专用遥测包装 → 删并清理其导出点；协议层 `Protocol/Handler/SpellCheck/WorkspaceSpellCheckHandler.cs` 是拼写检查处理器，只因内部引用了 Razor 侧类型才命中 → **判「改写去掉 Razor 分支」还是「随薄壳一并摘除」**，不得盲删（会连带砍掉 spell-check 能力）。
- 其余字面含 “Razor” 的 `.cs`（生产口径 30 个，扣掉上面已单独处置的宿主侧 1 个后余 29；SF-6 已注明测试树另有 10 个不计）→ **逐个判**：多为 handler 内的 Razor 分支与注释，只删会导致死代码与悬空导入的才动；其余保留以缩小与基线的 diff。

## 六、构建对齐

- **Arcade 剥离**：在 fork 的 `Directory.Build.props` 补 `NetVSCode`、`IsShipping` 等 IDE 工程所需属性的 fallback 定义。可核实的静态前提是：fork 根 `Directory.Build.props` 不含任何 Arcade 导入（仍 pin XliffTasks 用于 `.xlf`），且 `Compilers\` 全部工程就在这套 props 下——「脱离 Arcade 能构建」本身属 F3/F6 的实测对象，不在此预先断言。
- **路径改写**：csproj 内 `..\..\..\Compilers\...` 等相对路径整体重映射到新结构（机械批量，F2/F3 分界即在此）。
- **公共 API 分析器**：fork 比基线多成员（SF-13 的 `PublicAPI.Unshipped.txt` 即一例）。IDE 栈工程不导出编译器 API，但复制品若带自己的 `PublicAPI.*.txt`，须决定「沿用基线文件」还是「按 fork 实际面重生」，并在 F6 出结论——这条不定，构建会在 API 一致性检查上随机红。
- **NuGet 依赖**：协议/框架/MEF/ServiceHub 各包本机缓存已有（SF-11），无需联网 vendor。

## 七、`.vbx` 接入形状

**事实前提**：SF-9——`SourceCodeKind` 在上游 LanguageServer 层出现 0 次，script 地基只在 Workspaces 层（`CommandLineProject.cs:167`、`Solution.cs:1457`）。所以接入层要做的是把「松散 `.vbx` → 一个 script 工程」这段补出来，而不是指望现成通路。

设计取向：

1. **文档路由与变更监视必须同一条路径覆盖 `.vbx`**。监视侧不存在静态白名单（SF-10）：`filters` 由调用方按被跟踪文件的扩展名派生后传入。因此 `.vbx` 会不会被跟踪，取决于调用方是谁、它怎么决定跟踪哪些文件——这一条与路由落点一起由 F8 定位，本文不预设计数或行号。
2. **一个 `.vbx` = 一个 script 工程**，`LanguageNames.VisualBasic` + `SourceCodeKind.Script`。`#Load` 引入的文件按 `../../meetings/meeting-vbscript-lsp.md:62` 的定论取「同一脚本 Project 的多 Document」形态：编辑器打开时作为普通 Document 纳入，未打开时按磁盘文本纳入隐藏文档。
3. **引用来源对齐 `vbi`**：编译引用取 `Interactive/vbi/vbi.coreclr.rsp` 的清单（固定 .NET 10，见 `README.md`「范围」节），不再引入 `' Attribute TargetFramework` 这条维度（SF-8：它是商店版 launcher 的派发信号，引擎不读；LSP 属 tool 分发形态，静默一致）。
4. **`#R` / `#Load` 走既有解析器**：语义面由 fork 编译器与共享 `ReferenceManager` 负责，接入层只负责把「会话内引用解析结果」喂成该 script 工程的 `MetadataReference` 集。跨提交/跨文件的符号可见性由 `SemanticModel` 自然给出，不另造虚拟文档。
5. **语言服务不复制一份**：`.vbx` 与 `.vb` 共用同一套 Features 提供器；两模式唯一区别仍是 script mode（`../../meetings/meeting-vbscript-lsp.md:71`）。

## 八、宿主与打包骨架

抄 `{{VbLs}}` 的三件结构（SF-5），不抄它的 vendoring 方式：

- **launcher**：`dotnet <LSP dll>` + 原样透传参数，`UseShellExecute=False` 且不重定向任何流 ⇒ 子进程继承 stdio，JSON-RPC 零胶水穿透；`WaitForExit` 后回传 `ExitCode`。
- **打包 Target**：`PackAsTool` + `ToolCommandName`；把语言服务器工程的输出整目录塞进包内 `tools/<tfm>/any/<lsp 目录>/`。区别在于本任务里被 build 的是**仓库内的** LanguageServer 工程，不依赖外部基线路径。
- **进程形态**：独立进程、stdio 起步，命名管道可选（供 proposal-03 的扩展连接）——依据 `../../meetings/meeting-vbscript-lsp.md:72`；分发取 dotnet tool，依据同会议 `:73`。

## 九、里程碑映射

| 会议里程碑 | 本任务内的工作项 |
|---|---|
| M0 复制 + 裁剪 + 对齐构建 + 最小 LSP 原型 | F1–F7、F11 |
| M1 `.vbx` 接入（诊断 + completion + hover） | F8、F9、F10（`.vbx` 侧） |
| M2 signatureHelp / semanticTokens / 格式化 / `#Load` 跳转 / `#R` 导航 | 本任务只登记适配面结论（F10），不实现 |
| M3 普通项目 + 自动模式切换 | F4/F7 已铺路，切换策略不在本任务 |
| M4 dotnet tool 打包 + IDE 扩展集成 | F12 到「能打包」；扩展集成属 proposal-03 |

## 十、风险与未决

| 项 | 状态 | 处置 |
|----|------|------|
| LanguageServer 层无 script 通路（SF-9） | 本任务头号未知 | F8 定位为唯一落点后再写 `design-detailed.md` |
| 松散文件路由的行级落点未定（SF-10） | 已检查（文件与职责边界已核） | F1/F8 期间定位，不预先断言行号 |
| 共享源补齐范围（SF-12） | 布局已按「根镜像上游层名」定案（§三），命名相撞消除；未决的是 `SharedUtilitiesAndExtensions` 要补到哪一支 | F1 按复制闭包的 `<Import>` 需求出定论，缺什么补什么，不整树对齐上游 |
| 修改语法 × Features 适配面规模 | 未知（这正是本任务要量的） | F10 按 README「口径」节派生并逐条实测 |
| 公共 API 分析器在 fork 多成员下的处置 | 未决 | F6 出结论 |
| 复制后 IDE 栈与基线的升级方式 | 未决（不影响闸门） | 本任务通过后另立条目登记进 `../../upstream-merge.md` |
