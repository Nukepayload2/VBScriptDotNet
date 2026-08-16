# 上游 Roslyn 合并账本

本文件记录 VBScriptDotNet 编译器 fork 与上游 dotnet/roslyn 的合并基准与修改面，保证后续低成本跟随上游。**维护者每次合并前先读本文件，合并后更新本文件。**

## 一、上游基准

| 字段 | 值 |
|------|-----|
| 仓库 | `{{Roslyn}}`（本机值见 `portal.local.md`；origin = dotnet/roslyn） |
| 基准 commit | `0e401fcf66cbfd4aeb27a78408ab91cab3a6f207` |
| 基准日期 | 2026-07-27 |
| 基准分支 | `release/stable`（[release/stable] Snap insiders to stable (#84648)） |

> 相关参照：`{{VbLs}}` 的 `roslyn.json` pin 在 `87e31f80d4cc6b9fb236828df8af28a3bd42ee55`，是 vb-ls 的 vendored roslyn 快照，与本 fork 基准不同，仅作参考。
> 注意：上游基准 ≠ 本 fork 的裁剪基线。本 fork 的 `Compilers\` 是**修剪过的 Roslyn 编译器树**（见 `compilers-index.md` 版本快照），非完整上游镜像。

## 二、本地修改面清单

以下文件/区域是 fork 相对上游的本地改动。按「新增 / 修改」分类。证据列给出文件:行号或 commit；修改面外文件可直接跟随上游。

### 2.1 脚本扩展名与交互模式（修改）

- `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:61-63` — `ScriptFileExtension` 改为 `.vbx`。
- 同文件 `:1522-1523` — `\i` 交互模式选项 / 无参启动进入交互式 / `vbi script.vbx` 直接执行。

### 2.2 脚本编译链（修改）

- `Compilers\VisualBasic\Portable\Compilation\VisualBasicScriptCompilationInfo.vb` — `PreviousScriptCompilation` 链。
- `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:345-368` — `CreateScriptCompilation`；`:712`/`:723` 引用复用；`:816-868` `HasSubmissionResult`（脚本末尾表达式判定，支撑 REPL 打印与退出码）。

### 2.3 byref-like / ref struct 支持（修改，跨 Symbols 与 Analysis）

- `Compilers\VisualBasic\Portable\Analysis\IteratorAndAsyncAnalysis\IteratorAndAsyncCaptureWalker.vb:96-154` — `IsRefLikeOrAllowsRefLikeType()` 装箱/捕获检查。
- `Compilers\VisualBasic\Portable\Symbols\ConstraintsHelper.vb`、`Symbols\Metadata\PE\PENamedTypeSymbol.vb`、`Symbols\Source\SourceModuleSymbol.vb`、`Symbols\Source\SourceNamedTypeSymbol.vb`、`Symbols\Source\SourceTypeParameterSymbol.vb`、`Symbols\TypeSymbol.vb` — ref struct / allows ref struct 相关符号逻辑。
- 对应设计：`spec\spec-byref-like-safety.md`；规则来源 `{{VBRefStructHelper}}`（BCX 系列错误码）。注意：编译器层面尚未 suppress ref struct obsolete error（见 `decisions.md` D1）。

### 2.4 REPL 裸表达式自动打印（optional-question-prefix，修改）

- `Compilers\VisualBasic\Portable\Parser\ParseStatement.vb:1115-1154` — `ExpressionStatement` / `ParseScriptExpressionStatement` / `MakeInvocationExpression`（裸表达式语句 + 方法组形状）。
- 绑定层方法组消歧与 `IsFinalStatementOfSubmission`：`Compilers\VisualBasic\Portable\Binder\Binder_Statements.vb`（`BindExpressionStatement` / `ReclassifyInvocationExpressionAsStatement`）。
- `VisualBasicCompilation.vb` `HasSubmissionResult` 方法组感知（Sub→False / Function→True）。
- 对应设计：`spec\spec-optional-question-prefix.md`；实施样本 `tasks\optional-question-prefix\`。

### 2.5 #Load 指令（修改）

- 脚本宿主 `Scripting\Core\`（common scripting fork）：`#Load` 指令解析与并入脚本编译。
- 编译器层 Script submission 链：`VisualBasicCompilation.vb` `CreateScriptCompilation`。

### 2.6 顶层代码 / 脚本提交语义（修改）

- `SourceCodeKind.Script` 支撑 `.vbx` 与交互提交；`Return` 按 `Function Main` 语义处理（typed submission → vbx exit code）。
- `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` — `RunScriptAsync` 用 `CreateInitialScript<int>` 直接返回退出码（2.0 修复，见 `spec\README.md` 版本历史）。

### 2.7 新增（相对上游的新文件/项目）

- `Interactive\vbi\` — vbi 交互解释器宿主。
- `Scripting\Core\`（common scripting fork）、`Scripting\VisualBasic\` — 脚本运行时。
- `Workspaces\SharedUtilitiesAndExtensions\Compiler\` — CompilerExtensions 局部移植（.shproj）。
- `Samples\`、`Installer\` — 样例与分发。

## 三、合并步骤

1. **拉取上游**：`git -C {{Roslyn}} fetch origin release/stable`，记录新 commit 到「一、上游基准」。
2. **Public API 对账**：`git -C {{Roslyn}} diff <base> <new> -- src/Compilers/VisualBasic/Portable/PublicAPI.Shipped.txt`，与本 fork 比对——**编译器 public API 面不得破坏**（长期约束，见 `proposals\proposal-vbscript-lsp.md`「补层与构建机制」）。
3. **逐区合并修改面**：对「二、本地修改面清单」逐条 3-way 评审（上游 base → 上游 new → 本地）。修改面外文件可直接跟随上游，不必逐个评审。
4. **构建与测试**：跑 `scripts\verify-vb-compiler-tests.ps1` 七门 gate（Compiler/Syntax/Symbol/Semantic/IOperation/Emit/CommandLine）；`Scripting\VisualBasicTest` 直接跑程序集 `-automated`（dotnet test 静默不跑）。
5. **文档同步**：合并后更新 `spec\README.md` 版本历史、`compilers-index.md` 版本快照、本账本「一、上游基准」。

## 四、合并纪律

- 本地修改保持「**新增为主、尽量不侵入既有 public API**」；`PublicAPI.Shipped.txt` 把关。
- 修改面外文件不本地化；需要本地化时先在「二、本地修改面清单」登记再改。
- 无法核实的上游变更标注 `Suspect` / `OPEN QUESTIONS`，不写死。
- 合并后任何测试 gate 不绿即视为未完成，不得静默跳过。

## 五、引用互链

- `compilers-index.md`「版本快照」节 → 上游基准见本文件。
- `decisions.md` D1/D2/D4 → 修改面语义依据。
- `spec\README.md` 版本历史 → 产品能力随版本归档。
