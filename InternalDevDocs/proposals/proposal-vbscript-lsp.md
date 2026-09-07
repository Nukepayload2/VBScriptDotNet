# vbscript.net LSP：普通 VB 项目与 VBX 脚本 / VBScript.NET Language Server for VB Projects and VBX Scripts

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

> 状态说明：本提案为**新提案**（proposed）。按 `proposals/README.md` 的 active 目录规则，先归 active 根目录（`proposals/`）；**三态判定：待 LDM 会议评估**（对应会议纪要尚未创建，见 `meetings/README.md` 的会议索引；`proposals/README.md` 的索引更新由验证/调度阶段处理，本文件不改动其他文件）。

## Related
[related]: #related

- **姊妹提案**：[`proposal-vscode-extension-ise-repl-ui.md`](proposal-vscode-extension-ise-repl-ui.md) —— 本提案提供**跨编辑器的通用 LSP server**，proposal-03 聚焦 **VS Code 扩展客户端 + 集成终端 REPL + DAP 调试器**。两提案**互补非替代**：本提案的 LSP 服务器作为**独立进程**，可被 proposal-03 的扩展经管道连接，也可独立被 NeoVim / Zed / 其他 LSP 客户端消费。本提案把 proposal-03 里「LSP 服务器」这条线单独展开、补足成本评估与双模式支持设计。
- **并行提案（非 LSP 集成）**：[`proposal-avalonia-ise-repl-ui.md`](proposal-avalonia-ise-repl-ui.md) —— Avalonia ISE 是**单进程、纯代码直连** fork 编译器（直接调用编译器 API / 分类器，**不走 LSP、无 JSON-RPC 序列化/反序列化开销**），**不依赖本 LSP**。两条独立集成路径，共享同一执行核心与 `ObjectFormatter` 打印路径；本 LSP 只服务外部编辑器客户端，不为 avalonia 增加序列化层。
- **潜在价值：完整版 VS** —— 本 LSP 复用的 IDE 栈（Workspaces / Features / LanguageServer）与完整版 VS 的 VB 语言服务**同源**（同一套 Roslyn 层），LSP 资产（补层机制、vbx script mode 接入）对完整版 VS 的 VB 支持是潜在复用点（未排期）。
- **外部参照（成本基线）**：[vb-ls](https://github.com/CoolCoderSuper/vb-ls) —— fork 上游 Roslyn LSP + patch 支持 Visual Basic 的开源语言服务器。本提案的成本评估以它为基线，并说明为何不能直接照搬。
- **本地基线（补层来源）**：`{{Roslyn}}` —— 完整 Roslyn 源码树（`release/stable`，实测 ≈ 20724 files，含全部 `Workspaces` / `Features` / `LanguageServer` 层）。本仓库只复制了其中编译器部分（`Compilers\`）并做了产品修改；**未复制的 IDE 层项目就是未修改的基线源码**，需要时从这里原样拉取，无需网络 vendor。

## Summary
[summary]: #summary

本提案给 VBScript.NET 提供**跨编辑器的 LSP server**（建议新项目 `LanguageServer\`，自包含 .NET 应用），**单进程双模式**：

1. **普通 VB 项目模式**（`.vbproj` / `.sln`）：用 MSBuild workspace 加载项目，提供完整 IDE 级能力。
2. **VBX 脚本模式**（松散 `.vbx` 文件）：同一套 Features 栈，仅以 `SourceCodeKind.Script` 语义建立单文件（可经 `#Load` 扩展）脚本编译接入，提供补全 / hover / 诊断 / 格式化等脚本编辑能力。

服务器按打开内容的根目录形态**自动切换模式**。两模式共用同一套「Roslyn LSP + Features」栈（从本地基线 `{{Roslyn}}` 复制 IDE 栈项目进仓库、对齐 fork 构建），**唯一区别是 script mode**：普通 vb 项目用 MSBuild workspace 加载（`SourceCodeKind.Regular`），vbx 脚本用单文件脚本编译接入（`SourceCodeKind.Script`，`CreateScriptCompilation` + `#R` / `#Load` / `' Attribute TargetFramework` 引用语义）。模式一的实现路径已由 vb-ls 证明成本极低（约 1 行 + 1 个项目引用 + 1 个测试）；模式二是在此基础上把 vbx 作为 script-mode 松散文件接入（扩展名注册 + 脚本引用解析 + 修改语法适配）。本提案的核心价值是把「vb-ls 不可用的 vbx 脚本」补上，并让普通 VB 项目也能吃到 fork 编译器能力（如 [byref-like 安全](proposal-byref-like-safety.md) 对常规模式生效）。

## Motivation
[motivation]: #motivation

### 现状痛点

- **vbx 脚本编辑体验是产品最短板**：`.vbx` 无语法高亮、无补全、无 hover、无诊断（现状痛点与 `proposal-avalonia-ise-repl-ui.md` 一致）。REPL 是控制台 `vbi.exe`，脚本编辑靠记事本级工具。
- **产品已具备全部原料**：fork 编译器（`Compilers\VisualBasic\Portable\`，`.vbx` 脚本链已内建：`SourceCodeKind.Script` + `.vbx` 扩展名 + `CreateScriptCompilation` + `VisualBasicScriptCompilationInfo`）、REPL 执行管线（`Interactive\vbi\Vbi.vb` → `Scripting\VisualBasic\VisualBasicScript.vb` → `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` → fork 编译器）。缺的只是**把它们暴露成 LSP** 的一层。
- **普通 VB 项目需要 LSP**：fork 编译器对常规编译模式也有增强（byref-like 安全 `spec-byref-like-safety.md` 明确"对 vbx 与常规编译模式都生效"），普通 `.vbproj` 用户也要吃这些能力；且与 vbx 双模式共享同一编辑体验是产品自洽。

### vb-ls 做法评估（成本基线）

[vb-ls](https://github.com/CoolCoderSuper/vb-ls) 的做法：

1. **vendor 整个上游 Roslyn**：`update-roslyn.sh` 按 `roslyn.json` 里 pin 的 commit `git fetch --depth 1` + `checkout` + `apply` patches，产物含完整 `Workspaces` / `Features` / `LanguageServer` 层（vb-ls 的 vendored 树实测：`src/Workspaces` ≈ 2296 files、`src/Features` ≈ 2817 files、`src/LanguageServer` ≈ 1112 files）。
2. **只打 2 个微型 patch**（合计约 110 行，其中 ~108 行是单元测试）：
   - `lsp-vb-support.patch`：给 LanguageServer csproj 加一个 `<ProjectReference>` 到 `Microsoft.CodeAnalysis.VisualBasic.Features.vbproj`（让 MEF 组合包含 VB IDE 功能）+ 一个组合测试；
   - `autoload-vb-projects.patch`：`AutoLoadProjectsInitializer.cs` 加一行 `Directory.EnumerateFiles(folderPath, "*.vbproj", …)`。
3. **launcher 模式**：`Program.vb`（28 行）只是 spawn `dotnet Microsoft.CodeAnalysis.LanguageServer.dll`，透传参数。
4. **分发**：打包成 dotnet tool `vb-ls` + VS Code 扩展。

**结论：vb-ls 覆盖普通 VB 项目的成本 ≈ 几行 patch。** 原因：上游 Roslyn 已为 VB 写好了整套 IDE 层（`Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll` / `.VisualBasic.Features.dll`），LSP 天生支持松散 `.cs`/`.vb` 文件（`CanonicalMiscellaneousFilesProjectProvider` 按扩展名映射语言）与 `.vbproj` 项目（MSBuild workspace）。**这部分无需自研，直接复用即可。**

**vb-ls 不支持 `.vbx` 的根本原因**（源码核实）：
- 上游 LSP 的松散文件发现器只认 `.cs`/`.csproj`（`FileBasedPrograms/FileBasedProgramsEntryPointDiscovery.cs` 的 `DirectoryEnumerator.GetKind` 枚举 `.cs`/`.csproj`），松散文件语言映射无 `.vbx` 注册；
- 上游编译器是原版 Roslyn：不认 `.vbx` 扩展名对应的 Script 语义、不懂 `#R` / `#Load` / `' Attribute TargetFramework` 注释、没有顶层 `Function Main` 退出码语义、不认识修改语法（如可选 `?` 前缀 `proposal-optional-question-prefix.md`、byref-like 约束）；
- 上游 Features 层绑定原版语法树（`SyntaxKind` 枚举、语法节点层次），修改语法会破坏其遍历/匹配假设。

**本项目与 vb-ls 的关键差异（决定不能直接照搬）**：`Compilers\` 是**剪枝过的 Roslyn 编译器树**（实测 ≈ 4763 files），只有编译器（Core/CSharp/VisualBasic/Shared/Test），**没有** `Workspaces` / `Features` / `LanguageServer` 层；`Workspaces\SharedUtilitiesAndExtensions\Compiler\` 只是 `CompilerExtensions` 的局部移植（`.shproj`，377 files），不是完整 Workspaces 层。**但缺的层不必从网络上 vendor**：本地基线 `{{Roslyn}}` 是完整、未修改的 Roslyn 源码树，与 fork 同源同线（编译核心逐字一致，`LanguageVersion` 同为 `VisualBasic17_13`），**未复制的 IDE 项目就是基线原样**。因此「补层」= 从基线拉取未修改的 IDE 栈项目、对着 fork 编译器编译；IDE 层不需要为 fork 适配——唯一要回答的是「未修改的 IDE 项目能否对着已修改的 fork 编译器编译/运行」（M0 原型验证）。真正的成本在模式 B 的 `.vbx` 脚本接入（扩展名注册 + 脚本引用解析 + 修改语法适配），见 Alternatives。

### 期望结果

脚本用户在任意 LSP 客户端里编辑 `.vbx`：补全、hover、诊断即时可见；普通 VB 项目用户获得完整项目级 IDE 能力；两种模式经同一进程自动切换，共享 fork 编译器语义。

## Detailed design
[design]: #detailed-design

### 组件总览（单进程双模式）

```
LSP 客户端（VS Code / NeoVim / Zed / 其他，经 stdio 或命名管道）
        │  LSP over JSON-RPC
        ▼
vbscript-ls（LanguageServer\，自包含 .NET 应用，单进程）
  │
  ├─ 根目录检测：目录含 .sln/.vbproj → 模式 A；松散 .vbx / 无项目文件 → 模式 B
  │
  ├─ 模式 A：普通 VB 项目
  │    └─ 复用 Roslyn LSP（基线 IDE 栈 + fork 编译器，见「模式 A」）
  │         ├─ MSBuild workspace 加载 .vbproj/.sln
  │         └─ 完整 Features：补全 / hover / 签名 / 诊断 / 重构 / 格式化
  │
  └─ 模式 B：VBX 脚本（script mode，见「模式 B」）
       └─ 同一套 Features/LSP 栈，仅脚本编译接入不同
            ├─ 单文件 Script compilation（CreateScriptCompilation，SourceCodeKind.Script）
            ├─ #R / #Load 引用解析（复用 ScriptMetadataResolver / ScriptSourceResolver）
            ├─ ' Attribute TargetFramework 宿主选择
            └─ 补全 / hover / 诊断 / 格式化 等由同一套 Features 提供
```

要点：两模式共用同一套「Roslyn LSP + Features」栈（IDE 层从本地基线 `{{Roslyn}}` 复制进仓库、对着 fork 编译器对齐构建），**区别只有 script mode**：普通项目走 MSBuild workspace（`SourceCodeKind.Regular`），vbx 走松散文件脚本编译（`SourceCodeKind.Script`）。模式 A 的路径已由 vb-ls 证明成本几行 patch；模式 B 是在此基础上把 `.vbx` 作为 script-mode 松散文件接入（扩展名注册 + 脚本引用解析 + 修改语法适配）。

### 模式 A：普通 VB 项目（复用路径）

- **实现**：从本地基线 `{{Roslyn}}` 复制 IDE 栈项目（Workspaces / Features / LanguageServer / Protocol）进仓库，对着 fork 编译器对齐构建；套用 vb-ls 的两个 patch（VB Features 进 MEF、autoload `*.vbproj`）。机制与裁剪范围见「补层与构建机制」。
- **为什么用 fork 编译器而非基线原版（已定）**：**Decision: 普通项目模式用 fork 编译器**——普通项目也吃 VBScript.NET 增强（byref-like 安全 `spec-byref-like-safety.md` 对常规模式生效），单一编译器、双模式共享语义。
- **风险**：fork 编译器相对基线有增值修改（如 `Binder_Expressions.vb` 的 host object 绑定与 `IsRefLikeOrAllowsRefLikeType`、`VisualBasicCompilation.vb` 的提交结果判定），未修改的 IDE 项目对着它编译/运行可能踩到 API 差异。**必须先做原型验证（M0）量化缺口**，见里程碑。

### 模式 B：VBX 脚本（script mode）

#### 脚本编译生命周期

- 打开一个 `.vbx` → 建立**单文档脚本项目**：用 fork 编译器的 `VisualBasicCompilation.CreateScriptCompilation`（`VisualBasicScriptCompilationInfo` 已内建），语言为 `LanguageNames.VisualBasic`，`SourceCodeKind.Script`，引用集与 `vbi` 默认一致（对齐 `Interactive\vbi\vbi.coreclr.rsp` 的 `/r:` 与 `/imports:`）。
- `#R "path"` 指令：解析路径 → `MetadataReference.CreateFromFile`（复用 `ScriptMetadataResolver`），并把引用加入当前脚本编译。
- `#Load "file.vbx"` 指令：把被加载的 `.vbx` 并入同一脚本编译（对齐 `ScriptSourceResolver`）。**呈现模型（已定）：同一脚本 Project 的多 Document 模型**——被加载文件在编辑器打开时作为普通 Document 纳入，未打开按磁盘文本纳入隐藏文档；Workspaces 原生支持 Script 文档（`CommandLineProject` / `Solution` 的 `CreateScriptCompilation` 路径），`SemanticModel` 自动覆盖被加载文件的符号与诊断。
- `' Attribute TargetFramework = "net48"` 头部注释：选择脚本宿主/引用集（.NET Framework 4.8 vs .NET），对齐 `proposal-runtime-host-selection.md`（1.2 归档）。LSP 侧仅影响引用解析目标，不启动进程。
- **修改语法感知**：诊断/补全/hover 全部基于 fork 编译器的 `SemanticModel`，可选 `?` 前缀（`spec-optional-question-prefix.md`）、byref-like 约束（`spec-byref-like-safety.md`）、顶层代码语义天然生效，无需额外适配。

#### 能力来源（同一 Features 栈，脚本差异点单列）

vbx 文档的 `Project.Language = VisualBasic` 与普通项目一致，**同一套 `Microsoft.CodeAnalysis.VisualBasic.Features`（补全 / hover / 签名 / 诊断 / 格式化 / 语义分类 / folding）自动生效**——这正是 vb-ls 验证过的机制（松散 `.vb` 文件已支持，`.vbx` 只需补扩展名注册）。脚本模式与普通项目的差异点集中在：

| 差异点 | 处理 |
|---|---|
| `SourceCodeKind` | `.vbx` 用 `SourceCodeKind.Script`，普通项目 `Regular` |
| 引用来源 | `#R` / vbi 默认引用集（对齐 `vbi.coreclr.rsp`），**复用 fork `ScriptMetadataResolver`（含 `nuget:` 前缀解析）** vs MSBuild 项目引用 |
| 松散文件路由 | 扩展名注册 `.vbx` → `VisualBasic` + script；无 `.sln/.vbproj` 目录进脚本模式 |
| 指令参数补全 | `#R "` / `#Load "` 参数内补全文件路径——LSP completion provider 的脚本指令上下文 |
| 修改语法 | `?` 前缀 / `#R` / byref-like 由 fork 编译器 `SemanticModel` 天然支持；Features 中匹配标准语法的代码路径需验证/补 patch（M0 定位） |

#### 脚本指令的 LSP 边界（甄别）

- **`#R` / `#Load` 文件路径补全——LSP 职责。** 光标位于指令参数内时补全 DLL / `.vbx` 文件路径，属 LSP completion provider（指令参数上下文识别，Roslyn Features 的 `CompletionProvider` 机制）。
- **`#R "nuget: Package, Version"`——产品层已有，LSP 零新增。** fork 的 Scripting 层已实现 `nuget:` 前缀解析（`RuntimeMetadataReferenceResolver` → `NuGetPackageResolver.TryParsePackageReference` / `ResolveNuGetPackage`）；LSP 构建脚本 compilation 时**复用同一 resolver**，诊断/补全/悬停与 vbi 执行保持一致，不重复实现 NuGet 解析（实际下载/加载属执行层）。
  > 勘误：'已实现'断言与本仓代码不符（`NuGetPackageResolver` 空转、注入 null）；本特性以 `tasks\vbi-nuget-reference\` + `meetings\meeting-vbi-nuget-reference.md` RESOLUTION 为准。
  >
  > 语法明示：本特性语法为 `#R "nuget:包名[, 版本]"`——前缀 `nuget:` 大小写不敏感，包名与版本以逗号分隔、各段首尾空白可忽略；解析层版本可省，v1 版本必填（缺省报「请指定版本」）。与官方 file-based 的 `#:package id@version`（`@` 分隔）写法不同：`.vbx`/vbi 属 Script/REPL 语义，只认 `#R "nuget:"`，不解析 `#:`/`@`。
- **`#!path/to/vbi`——编译器语法特性（C# 有、VB 无），非本提案范畴。** 甄别纠正：`#!` **不是执行层 hack，而是 C# 编译器语法层正式引入的脚本指令**——`ShebangDirectiveTrivia`（`CSharp.Generated.g4` 的 `shebang_directive_trivia` 文法；`DirectiveParser.cs` 的 `ParseShebangDirective`，与 `#r` / `#load` 并列解析，仅 Script / file-based programs 允许，专有诊断 `ERR_PPShebangNotOnFirstLine` / `ERR_PPShebangInProjectBasedProgram`），`.csx` 因此天然支持。**fork VB 编译器无等价物**——若产品要让 `.vbx` 支持 `#!`，需在 fork VB Parser/Scanner 加等价 shebang trivia（语法特性，与 `?` 前缀 / byref-like 同类），走编译器提案而非本 LSP。LSP 侧仅**弱相关**（语义模型天然继承该 trivia；文件发现可参考 Roslyn `FileBasedProgramsEntryPointDiscovery`），不参与 `#!` 的语义。

#### 传输与宿主

- **Decision: language server 是独立进程**（不塞进 `vbi`，也不与 REPL/DAP 并载），保持 REPL 执行器与长驻服务分离；proposal-03 的扩展作为外部客户端经管道连接本进程。
- 标准 LSP over JSON-RPC，stdio 起步（任意客户端可连），命名管道可选（供 proposal-03 连接）。

### 补层与构建机制（已调查定论）

**决策：从本地基线复制 IDE 栈项目进仓库，裁剪 VS 专用依赖，对齐 fork 构建。** 源码已在本机（`{{Roslyn}}`），缺的层直接复制进 fork——仓库自包含、构建只在 fork 内完成，不再依赖外部基线仓库。调查依据：

- **复制范围**：`Workspaces`（Core + VisualBasic + MSBuild）、`Features`（Core + VisualBasic）、`LanguageServer`、`Protocol`、`Scripting`（fork 已有）——约 4000+ files。复制后 IDE 栈与 fork 编译器同仓库、同源同线，编译期引用一致。
- **必须裁剪：Razor**（≈ 3279 files，VB 不需要）。LanguageServer 对 Razor/ExternalAccess 的**代码引用仅 4 处**（实测）：`LanguageServerProjectLoader.cs` 的 targets 路径字符串、`LanguageServerExportProviderBuilder.cs` 的 MEF DLL 声明、`CopilotCompletionResolveContextHandler.cs`、`Razor/TelemetryReporterWrapper.cs`——删除这 4 个引用点 + csproj 删 ProjectReference 即可。VS 专用薄壳 ExternalAccess（Copilot / AspNetCore / Xaml / TestDiscovery / VisualDiagnostics，共 ~100 files）一并裁掉。LanguageServer 对 CSharp.Features **无直接代码引用**（csproj 引用仅为 MEF 组合，vb-ls 的 patch 加 VB Features 同理）——CSharp.Features 是否裁留 M0 确认。
- **工程处理**：① csproj 相对路径（`..\..\..\Compilers\...`）指向 fork 的 `Compilers\`；② **Arcade 属性剥离**——fork 的 `Directory.Build.props` 补 `NetVSCode` / `IsShipping` 等 fallback（fork 的 `Compilers\` 已证明可脱离 Arcade 独立构建）；③ 删 Razor/Copilot 引用文件 + `LanguageServerExportProviderBuilder` 里的 Razor DLL 声明；④ MEF 组合验证（删 provider 后核心功能完整）。
- **兼容前提（同源同线）**：fork 与基线编译器的 `PublicAPI.Shipped/Unshipped.txt` **逐字一致**、编译器 `InternalsVisibleTo` 不授 `Workspaces` / `Features` / `LanguageServer`（只授 `Scripting` / `vbi` / 测试）→ 复制项目对着 fork 编译器编译无兼容障碍。
- **长期约束**：fork 编译器不得破坏 public API 面（`PublicAPI.Shipped.txt` 把关）；IDE 栈版本随复制时点固定，不随基线自动更新（需要升级时重新复制对齐）。

**TFM（已定）**：net10.0——对齐 fork 编译器与基线 LanguageServer（基线 `NetVSCode` = `net10.0`）。
**跨平台（已定）**：LSP 服务器 net10.0 跨平台；net48 脚本宿主仅 Windows；`' Attribute TargetFramework` 只影响脚本引用解析目标。首版 Windows 优先（商店版基线），服务器代码无跨平台壁垒。

### 仓库结构

```
LanguageServer\                      （新根目录项目，自包含 .NET 应用，net10.0 对齐 2.0）
  ├─ VBScriptDotNet.LanguageServer\  （LSP 宿主 + 双模式路由 + JSON-RPC）
  ├─ VBScriptDotNet.Scripting.Server\（模式 B：vbx 脚本编译接入：扩展名注册 + 引用解析）
  └─ VBScriptDotNet.Server.Test\     （无副作用单元测试，对齐 CLAUDE.md 规约）
```

### 里程碑

| 里程碑 | 内容 | 依赖 |
|---|---|---|
| M0 原型验证 | 复制 IDE 栈项目进仓库 + 裁剪 Razor/VS 专用依赖 + 对齐构建，跑通最小 LSP（一个 `.vbx` 松散文档 + 一个 `.vbproj`），实测验证兼容性 | 无 |
| M1 模式 B 基础 | `.vbx` 扩展名注册 + script 编译接入：诊断 + completion + hover 经 Features 生效，stdio 传输 | 无 |
| M2 模式 B 增强 | signatureHelp、semanticTokens、格式化（Features 免费提供）、`#Load` 跳转、`#R` 引用导航 | M1 |
| M3 模式 A | 普通 VB 项目（复用路径），根目录自动模式切换 | M0 |
| M4 分发 | **dotnet tool 打包（照 vb-ls）**，可配合 IDE 扩展（VS Code / NeoVim / Zed 客户端示例、proposal-03 集成） | M2, M3 |

## Drawbacks
[drawbacks]: #drawbacks

- **复制规模与版本固定**：复制 IDE 栈（~4000 files）进 fork，仓库体积显著膨胀、与"剪枝/本地化"哲学冲突；IDE 栈版本随复制时点固定，升级需重新复制对齐（不随基线自动更新）。长期约束 fork 编译器不得破坏 public API 面（`PublicAPI.Shipped.txt` 把关）。
- **修改语法与 Features 的适配**：`?` 前缀 / `#R` 等 fork 语法修改可能破坏 Features 中匹配标准语法树的代码路径（补全上下文、格式化、分类），需逐点验证并补 patch（M0 定位，规模未知）。
- **双模式维护面**：两模式共享同一套 LSP/Features 栈，维护面小；但模式边界（同一文件被项目引用又在脚本目录）需要定义路由优先级。
- **与 proposal-03 的关系需明确**：若两条提案并行，`LanguageServer\` 归属与宿主形态要定死，避免重复建设。
- **优先级**：这是工具链/易用性投资，不缩小与 C# REPL 的功能差距本身（见 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。

## Alternatives
[alternatives]: #alternatives

- **路线 A：全量上引 IDE 栈（vb-ls 同构）**——把基线的 Workspaces + Features + LanguageServer 全部拉进 fork 并对齐编译，vbx 模式也走 Features（改语法适配）。功能最全、与上游体验一致（格式化/重构/分类器全免费）；代价是 fork 仓库膨胀为近完整 Roslyn、fork 修改需持续同步 IDE 层假设，与"剪枝"哲学冲突。
- ~~**路线 B：轻量自建 LSP 宿主**~~——**不需要**（本提案即 LSP，两模式共用同一套 Features 栈，区别只有 script mode）：自研补全/分类/格式化纯属重复造轮子。
- ~~**路线 C：普通项目直接用 vb-ls 成品，只自建 vbx 模式**~~——**已否决**（LDM：普通项目用 fork 编译器）：普通 VB 项目零成本，但吃不到 fork 能力（byref-like 等），且需维护"两个语言服务器进程"。
- **已定方向（混合）**——模式 A 复用路径（复制 IDE 栈项目进仓库 + 裁剪 Razor/VS 专用依赖 + vb-ls patch），模式 B 在此之上把 `.vbx` 接入为 script-mode 松散文件；两模式共用同一套 Features 栈、单进程双模式。普通项目优先保功能广度，vbx 优先保脚本语义。**剩余成本：修改语法与 Features 的适配规模（M0 定位）。**

## Unresolved questions
[unresolved]: #unresolved-questions

None. 所有方向问题已定论（决策散落正文对应小节）：

- 普通项目模式用 fork 编译器（模式 A）；两模式唯一区别是 script mode。
- 补层走从本地基线复制 IDE 栈项目进仓库、裁剪 Razor/VS 专用依赖、对齐 fork 构建（「补层与构建机制」）。
- TFM net10.0、跨平台 Windows 优先（「补层与构建机制」）。
- `#Load` 用同一脚本 Project 的多 Document 模型（模式 B）。
- language server 是**独立进程**（「传输与宿主」）。
- **dotnet tool 分发**，可配合 IDE 扩展（里程碑 M4）。
