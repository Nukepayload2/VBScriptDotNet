# 概要设计：vbi 脚本模式诊断检查 `/check`

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-vbi-script-diag-mode.md`（冻结输入，原 `/diag`）→ `../../meetings/meeting-vbi-script-diag-mode.md`（Active，RESOLUTION #1-#12，命名裁决 `/check`）→ 任务 README「共享源码事实」。
> 本设计吸收会议 RESOLUTION #1-#12，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 本任务一律用 `/check`（RESOLUTION #1）。

## 1. 背景与目标

**目标（一句话）**：vbi 脚本模式新增 `/check` 开关——把 `.vbx` 作为脚本 submission 只编译、输出**错误+全部警告**、不执行不落盘，退出码 0/1 作编译门，服务 AI 开发 vbx / CI 预检。

- **现状缺口**：`RunScriptAsync`（`CommandLineRunner.cs:209`）成功路径直接 `script.RunAsync` 返回 ReturnValue，**警告被吞**；错误只在 `catch (CompilationErrorException)`（`:212-216`）时报。想"只看不跑"三条路都不通（脚本执行焊死运行、交互逐提交且 5 条截断 `:380`、编译模式落盘 `Vbi.Compile.vb:32-83`）。
- **现成积木**：`Script.Compile()`（`Script.cs:231` → `CommonCompile` `:332-346`）就是"编译不运行拿诊断"的 API——成功仅警告、失败错误+警告、不执行不落盘；交互模式 `BuildAndRunAsync`（`:296-320`）已在用同款路径。
- **命名（RESOLUTION #1）**：原提案 `/diag` 被会议否决——AI 先验（msbuild `/diag`/`dotnet -v:diag` 详细日志）与"不执行"方向相反；采纳 `/check`——AI 先验（cargo/node/biome check = 只检查不产产物）与语义吻合。
- **状态**：提案 Active（冻结）、会议 Active（RESOLUTION #1-#12）。默认行为零变化。

**RESOLUTION 吸收映射**：

| RESOLUTION | 内容 | 落在本设计 |
|-----------|------|-----------|
| #1 | 采纳 `/check`，帮助文本消歧 | 第 2、8 节 |
| #2 | `Check` 放基类 `CommandLineArguments` | 第 2、3 节 |
| #3 | 互斥落实成规则（`:1542` + `:144-152`） | 第 4 节 |
| #4 | 退出码 0/1，用 `ReportDiagnostics` 返回值 | 第 4 节 |
| #5 | 措辞"错误+全部警告" | 第 5 节 |
| #6 | 严重度配置 v1 不带 | 第 7 节 |
| #7 | `/check` 配 `.vb` 不生效 | 第 4 节 |
| #8 | 命名边界（与溢出开关区分） | 第 8 节 |
| #9 | MSYS 统一裸开关说明 | 第 9 节 |
| #10 | 内存 emit 接受，"无落盘无执行" | 第 6 节 |
| #11 | 分析器独立立项 | 第 7 节 |
| #12 | 普通执行前置警告 Follow-up | 第 7 节 |

## 2. 总体架构（四层触点）

```
vbi /check foo.vbx
   │
   ├─ 模式分派：IsCompileInvocation（Vbi.Compile.vb:32-59）——/check 不触发编译模式，落脚本
   │
   ├─ 解析器（C1+C3）：VisualBasicCommandLineParser.Script 脚本分支加 Case "check"
   │     check 布尔 → CommandLineArguments.Check（基类，C2）
   │     :1542 抑制自动交互 + 无文件错误（C3）
   │
   ├─ 宿主（C4+C5）：CommandLineRunner
   │     RunInteractiveCoreAsync：Check 置位无视 InteractiveMode（C5）
   │     RunScriptAsync：script.Compile() → ReportDiagnostics → 返回（C4）
   │
   └─ 输出：错误+全部警告到 stderr（ReportDiagnostics 全量，非 5 条截断）；exit 0/1
```

**核心观察**：`Script.Compile()` 与 `ReportDiagnostics` 都现成，交互模式已验证同款"先编译拿诊断"路径。改动 = 解析器识别开关 + 基类加标志 + 宿主把 `RunScriptAsync` 的 `RunAsync` 换成 `Compile` 分支 + 互斥规则 + help。

### 2.1 解析层（C1 脚本分支 + C3 互斥）

- 脚本专属分支（`:474-542`）新增 `Case "check"`，置 `check = True`（仿 `/optimize` 先例 `:525-541`）。
- 新增 `check` 布尔（默认 False，`:97` 区）。
- `:1542` 自动交互条件加 `AndAlso Not check`；`check` 置位且无源文件 → 产出错误诊断（无文件语义）。

### 2.2 基类参数（C2）

- `CommandLineArguments`（`Compilers\Core\Portable\CommandLine\CommandLineArguments.cs`）新增 `Check` 布尔（默认 false），仿 `InteractiveMode`（`:31`）——共享 runner 直接读，VB 解析器置位，C# 侧不置位天然不受影响。

### 2.3 宿主（C4 + C5）

- `RunScriptAsync`（`:201-222`）：`Check` 置位时——`script.Compile(cancellationToken)` → `_compiler.ReportDiagnostics(diagnostics, _console.Error, errorLogger, compilation: null)` → `return ReportDiagnostics(...) ? Failed : Succeeded`（复用返回值，一行退出码）。不调用 `RunAsync`。
- `RunInteractiveCoreAsync`（`:80-153`）：`Check` 置位时无视 `InteractiveMode`（`:144-152`），强制走 `RunScriptAsync`。

### 2.4 help（C6）

- `VBScriptingResources.InteractiveHelp` 加 `/check` 行 + 统一裸开关族 MSYS 说明（见第 8、9 节）。

## 3. 行为对照表

| 用法 | 行为 | 退出码 | 说明 |
|------|------|--------|------|
| 默认（无 `/check`） | 编译+执行（现状） | 脚本 ReturnValue | 零变化 |
| `vbi /check foo.vbx`（干净） | 只编译、显示警告（如有）、不执行 | **0** | AI/CI 编译门 |
| `vbi /check foo.vbx`（有错误） | 只编译、显示错误+警告、不执行 | **1** | 仿 vbc/dotnet build |
| `vbi /check foo.vbx`（`#load` 链） | 前置脚本同样内存 emit、不落盘不执行 | 0/1 | `#load` 边界（#10） |
| `vbi /check /i foo.vbx` | `/check` 优先，忽略 `/i`，仍只编译 | 0/1 | 互斥（#3） |
| `vbi /check`（无文件） | 报错退出，不进交互 | 1 | 互斥（#3） |
| `vbi /check t.vb` | 进编译模式，`/check` 落 BC2007 警告、照常发射 | 依编译 | 文档写明（#7） |
| `vbi /check foo.vbx /warnaserror:on` | `/warnaserror:on` BC2007 警告被忽略 | 0/1 | 严重度配置 v1 不带（#6） |

> 输出 = **错误+全部警告**（Info/Hidden 被滤，`Script.cs:340,344` + `CommonCompiler.cs:540-544`），全量显示（复用 `ReportDiagnostics`，非 `DisplayDiagnostics` 5 条截断）。

## 4. 判定原则

- **单一开关**：只加 `/check`，不评估严重度配置、不触发分析器。
- **默认零变化**：无 `/check` 时一切照旧（`RunScriptAsync` 原路径不动）。
- **互斥写死成规则**：`check` 置位 → 解析器抑制 `:1542` 自动交互 + 无文件报错；runner 无视 `InteractiveMode`。
- **退出码**：0 = 无编译错误（可有警告），1 = 有错误；`ReportDiagnostics` 返回值即 hasErrors。
- **严格性由文件决定**：脚本 submission 默认 `Option Strict Off`（`VisualBasicScriptCompiler.vb:204`），源级 `Option Strict On` 逐文件覆盖（实证 BC30574/BC30512）。

## 5. 与既有机制的关系

- **交互模式先例**：`BuildAndRunAsync`（`:296-320`）的 `newScript.Compile()` + `DisplayDiagnostics` + `HasAnyErrors()` 是"先诊断后执行"的同款路径；`/check` 把它接到**非交互一次性脚本**并去截断、改退出码。**不引入第二种做事方式。**
- **`/optimize` 先例**：`/check` 是脚本专属分支加开关的第二次应用（`/optimize` 在 `:525-541`）；勘误教训——脚本开关必须放脚本分支而非 vbc 分支。
- **csi 无关**：本 fork 无 `Scripting\CSharp` 宿主；共享 `CommandLineRunner` 仅 VB 消费，`Check` 在基类 C# 侧不置位。

## 6. 代价与边界

- **内存 emit**：`CommonCompile` 经 `GetExecutor` → `CreateExecutor` 内存发射 submission（脚本引擎标准路径），无落盘无执行；措辞统一"无落盘无执行"，`#load` 链前置脚本同样内存 emit。
- **严重度不可调**：脚本模式不支持 `/warnaserror`/`/nowarn`/`/ruleset`（BC2007），`/check` v1 不能把警告升级为错误——但严格度恰好等于默认 `dotnet build`/`vbc` 门，且 `Option Strict On` 可由文件控制。
- **`/check` 配 `.vb` 落空**：进编译模式被当未知开关（BC2007），文档写明。
- **MSYS 裸开关**：`/check` 第 4 个受 git-bash 路径转换影响的裸开关，零代码 + `MSYS2_ARG_CONV_EXCL='*'` + 帮助文本统一说明。
- **普通执行路径警告仍吞**：非 `/check` 的 `vbi foo.vbx` 成功路径照旧吞警告——已列 Follow-up（#12）单独立项，不并线。

## 7. 决策记录（原未决问题，全部已定案）

- **命名（定案，RESOLUTION #1）**：`/check`（AI 先验吻合）；`/diag` 否决（msbuild 详细日志先验方向相反）。帮助文本写"只编译、不运行、不产出文件"。
- **`Check` 位置（定案，#2）**：基类 `CommandLineArguments`，仿 `InteractiveMode`。
- **互斥（定案，#3）**：`/check` 需文件、优先于 `/i`；`:1542` + `:144-152` 落实成规则。
- **退出码（定案，#4）**：0/1，`ReportDiagnostics` 返回值。
- **措辞（定案，#5）**：错误+全部警告。
- **严重度配置（定案，#6）**：v1 不带，留分析器面。
- **`/check` 配 `.vb`（定案，#7）**：不生效，文档写明。
- **命名边界（定案，#8）**：与 `/removeintchecks`/`csc /checked` 区分；`Option Checked` 非准入条件。
- **MSYS（定案，#9）**：帮助文本统一裸开关族说明，零代码。
- **内存 emit（定案，#10）**：接受，"无落盘无执行"。
- **分析器（定案，#11）**：独立立项，不并线。
- **普通执行前置警告（定案，#12）**：Follow-up，单独立项。

## 8. help（定案，实现细节）

- `VBScriptingResources.InteractiveHelp` 加 `/check` 行：`/check` 只编译、不运行、不产出文件（输出错误+全部警告）；与整数溢出开关 `/removeintchecks` 无关（RESOLUTION #8）。
- 统一裸开关族 MSYS 说明（`/i`/`/nostdlib`/`/optimize+`/`/check`，git-bash 下需 `MSYS2_ARG_CONV_EXCL='*'`）（RESOLUTION #9）。
