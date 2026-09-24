# 脚本解析器缺 `optionsEnded` 门：`--` 之后的 token 仍按 switch 处理，裸 `@` token 触发断言

- **状态**：**Fixed**（已验证，commit 待作者提交后补）——**方向 A（对齐全 csi）已落地**：主循环补 `optionsEnded` 门（`VisualBasicCommandLineParser.vb:172`/`:199`/`:203`，唯一写点 `:481` 在 `IsScriptCommandLineParser` 块内 ⇒ vbc 面可证零影响）。主线复跑 `CommandLine` 门 483/476/7/0、`Scripting\VisualBasicTest` 直跑 766/0、重建 Debug 宿主三格实跑（档 2：`vbi -- a.vbx` 跑通、`vbi -- @x` 不再撞断言转 `BC2001`、`vbi -` stdin 仍可用）。账本 `..\upstream-merge.md` §2.25(e)；计划 `..\tasks\script-parser-options-ended-gate\`。下方取证段为**登记时（修复前）**的原样记录，保留不改。
- **发现日期**：2026-09-16
- **发现场景**：`../tasks/script-mode-coverage-parity/` 的 U6（命令行参数 `Args` 与搜索路径）补测。登记依据同任务 `README.md` **§八 义务 1** 末句（「实施期若发现别的真缺陷，同样按此登记」）。
- **影响面**：`vbi` 的**脚本解析路径** —— `VisualBasicCommandLineParser.Script`，即 `vbi`（无参 REPL）、`vbi /i`、`vbi -- …`、`vbi script.vbx`。编译模式（`VisualBasicCommandLineParser.Default`）**不可达**本断言（取证见「根因」第 3 条）；**编译模式下 `Case "-"`（`:1339`，位于 `:475` 的 `If IsScriptCommandLineParser Then` 的 `Else` 分支 ⇒ 编译模式专属）同样把 `--` 当标准输入开关**，`@` token 则落响应文件分支（实测读数见「实测读数」第 5 行）。
- **严重度**：中。断言面需要「Debug 构建的编译器 + 合法输入」同时成立；Release 构建下只剩行为分歧（见「Release 构建」节，**推测**）。

## 触发面

共享的 `FlattenArgs`（`Compilers\Core\Portable\CommandLine\CommandLineParser.cs`，符号 `FlattenArgs`，`:500`）为 `--` 置 `optionsEnded`（`:547-552`），并据此：

- 对 `@` 前缀 token **跳过**响应文件分支（`:556` 的 `!optionsEnded` 门）；
- 把 `--` 之后的**下一个** token 起算作「已见源文件」（`:586` 的 `sourceFileSeen |= optionsEnded || !IsOption(arg)`）⇒ 再往后的 token 全部进脚本参数（`:539-545`）。

但 `--` **本身**仍被放进 `processedArgs`（`:551`）交给语言侧主循环。

C# 主循环为 `--` 单独置自己的 `optionsEnded`（`Compilers\CSharp\Portable\CommandLine\CSharpCommandLineParser.cs`，符号 `Parse`——**声明于 `:52`**——的内部锚点 `:330`，其 `case "-"` 在 `:313`、`if (arg == "-")` 在 `:315`），并把该标志**同时**用到断言与 option 判定上：

```
:169  Debug.Assert(optionsEnded || !arg.StartsWith("@", StringComparison.Ordinal));
:174  if (optionsEnded || !TryParseOption(arg, out nameMemory, out valueMemory))
```

VB 主循环（`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`，符号 `Parse`——**声明于 `:79`**——其主循环 `For Each` 在 `:197`）**没有**这个门：

```
:198  Debug.Assert(Not arg.StartsWith("@", StringComparison.Ordinal))     ' 裸断言，无 optionsEnded
:202  If Not TryParseOption(arg, name, value) Then … ParseFileArgument(…) ' 判定也无门
```

⇒ 同一输入向量，C# 把 `--` 之后的 option 形状 token 当源文件，VB 仍按 switch 分类；`--` 之后紧跟的裸 `@` token 则直接命中 `:198`。

## 实测读数

| # | 输入向量 | 读数 | 三态 |
|---|---|---|---|
| 1 | `["--", "@arg1"]` | **断言触发**：`Xunit.Sdk.TraceAssertException`，消息逐字 `Trace/Debug.Assert() Failure: Not arg.StartsWith("@", StringComparison.Ordinal)`；在该宿主下表现为**该测试失败**（`TestsFailed = 1`、`TestsTotal = 1`），不是进程崩溃。断言**不终止执行**（`Debug.Assert` 报告后返回），该次 `Parse` 因此仍跑完并返回：`SourceFiles = ["-", "<base>\\@arg1"]`（两项 `IsScript` 皆为真）、`ScriptArguments = []`、`Errors = []`——断言之外没有留下任何诊断 | **实锤**（本轮实跑；探针是 `ScriptModeArgsTests.vb` 内一次性的 `ParseScriptArguments({"--", "@arg1"}, BaseDirectory)` 调用，取数后已撤除） |
| 2 | `["--", "/arg2", "script.vbx"]` | **无断言**；`SourceFiles = ["-"]`、`Errors = [BC2007]`（`WRN_BadSwitch`，`Compilers\VisualBasic\Portable\Errors\Errors.vb:42`）、`ScriptArguments = ["script.vbx"]` | **实锤**（同上，探针读数逐字：`files=[-] redirected=True errs=[BC2007] args=[script.vbx] consoleRedirected=True`） |
| 3 | `["--", "script.vbx", "@arg1"]` | **无断言**：第 2 个 token 先占了源文件槽 ⇒ 第 3 个 token 已进脚本参数，没有 `@` token 到达主循环 | **实锤**（U6 格 7 用例常态跑通即此路径，`ScriptModeArgsTests.vb:375`） |
| 4 | `["@arg1"]`（无 `--`） | **无断言**：走响应文件分支，报 `BC2011`（`ERR_NoResponseFile`，`Errors.vb:46`，消息为「无法打开响应文件…」）；`SourceFiles` 与 `ScriptArguments` 皆空 | **实锤**（同第 1 行的探针手法，取数后已撤除） |
| 5 | `["--", "@arg1"]`（**编译模式**，`VisualBasicCommandLineParser.Default.Parse`） | **无断言**（`asserts = 0`）；`SourceFiles = ["-"]`（`IsScript = False`）、`Errors = [BC2011]`（同第 4 行的响应文件诊断） | **实锤**（本轮实跑；探针同第 1 行，取数后已撤除） |

**对照的意义**：断言不是「任何输入都能撞」——第 3、4 行给出同族输入**不撞**，门槛是「`--` 已置 `optionsEnded` **且**其后第一个 token 以 `@` 开头」这一具体组合。第 4 行同时排除了「裸 `@` token 一定能到主循环」的相反猜测：没有 `--` 时它被响应文件分支吃掉。第 5 行同一向量换成编译模式后 `asserts = 0` ⇒ 门槛还要求**脚本模式**：编译模式下 `optionsEnded` 恒 False（根因第 3 条），同一个 `@arg1` 被响应文件分支吃掉（`BC2011`）。

**第 1 行那两个源文件的来处**（源码级实锤，`VisualBasicCommandLineParser.vb`）：`--` 走 `:477-483` 的脚本分支 `Case "-"`，在 `Console.IsInputRedirected` 为真时以 `isScript:=True` 把 `"-"` 加进 `sourceFiles`；`@arg1` 属于**下一个** loop 迭代，该迭代先命中 `:198` 的断言，随后在 `:202` 的 `TryParseOption` 上失败、经 `:204` 的 `ParseFileArgument` 以 base directory 补全成 `<base>\@arg1` 并在 `:206` 加入源文件。⇒ **断言触发的那一刻 `SourceFiles` 只有 `["-"]`**，`@arg1` 是同一迭代内、断言之后加进去的；表里第 1 行的读数取自断言之后跑完的那次 `Parse`。

**第 2 行的可见后果与 C# 的差异**：C# 的 `optionsEnded` 使 `/arg2` 短路进 `ParseFileArgument`（`:174`）⇒ **C# 把 `/arg2` 当源文件**，而 VB 报 `BC2007` 并把它丢掉。这是**真行为分歧**，不只是诊断文案差异。

**关于 `["-"]` 的条件**：`SourceFiles = ["-"]` 只在 `Console.IsInputRedirected` 为真时成立；否则 `Case "-"`（`VisualBasicCommandLineParser.vb:477-484`）走 `:482` 报 `ERR_StdInOptionProvidedButConsoleInputIsNotRedirected`（`Errors.vb:1785`，即 **BC37318**）且不加源文件。第 2 行的读数在重定向 stdin 下取得（`consoleRedirected=True`）。

## 根因

1. **主循环缺门**：`VisualBasicCommandLineParser.vb:198` 的断言与 `:202` 的 `TryParseOption` 都不看 `optionsEnded`（该文件全无此局部变量）。C# 的对偶在 `CSharpCommandLineParser.cs:169` / `:174`。
2. **`--` 与 `-` 在 VB 里同码**：脚本分支的 `Case "-"`（`VisualBasicCommandLineParser.vb:477`）只比 `name`，不比 `arg`，因此 `--` 与 `-` 一样被当作标准输入开关；C# 在 `:315` 用 `if (arg == "-")` 把 `--` 摘出去，改走 `:330` 置 `optionsEnded`。这正是第 2 行「源文件槽变成 `-`」与「stdin 未重定向时报 BC37318」的直接原因。
3. **编译模式不可达**：`VisualBasicCommandLineParser.vb:86` 的 `scriptArgs = If(IsScriptCommandLineParser, New List(Of String)(), Nothing)` 使编译模式下 `scriptArgsOpt` 为 `Nothing`，于是 `FlattenArgs` 的 `--` 处理（`:530` 的 `scriptArgsOpt != null` 门）被跳过，`optionsEnded` **恒为 False** ⇒ `@` token 永远进响应文件分支，`:198` 的断言在编译模式不可达。实测印证（读数表第 5 行）：`--` 在编译模式落到 `:1339` 的 `Case "-"`——该 `Case` 位于 `:475` 的 `If IsScriptCommandLineParser Then` 的 `Else`（`:552`）分支内，脚本模式下不可达——以 `isScript:=False` 加入 `"-"` 源文件；同一个 `@arg1` 则因 `optionsEnded` 恒 False 而落响应文件分支报 `BC2011`，从未进 `processedArgs`、更未到 `:198`。

**性质判定**：`:198` 的断言宣称的不变量是「不会有 `@` token 到达主循环」。`--` 之后跟裸 `@` token 是**合法输入**（C# 侧即按源文件处理，见 `CommandLineParser.cs:537` 的注释表 `csi -- @script.csx a b c`），该合法输入**违反**了这一不变量 ⇒ 属「崩编译器是 bug」判定原则的**弱形态**：`Debug.Assert` 只报告、不终止（该次 `Parse` 照常跑完，见读数表第 1 行），可见后果取决于宿主装的 `TraceListener`——xunit 下表现为**该测试失败**，无人接管时只是落到 trace 输出，既不终止进程也不产生诊断。与 NRE / `InvalidOperationException` 那一类相比，破坏面小得多但仍是不该发生的不变量违反。

## 预期行为（两种，交用户裁决）

**这一条不自行改语义**。两个候选方向都须用户定案：

- **方向 A（按 D5 对齐全 csi）**：给 VB 主循环补上 `optionsEnded` 门——`--` 之后的 token 不再进 `TryParseOption`，落 `ParseFileArgument`；`:198` 的断言同时被门保护。落点集中在 `VisualBasicCommandLineParser.vb:197-202`，但**要一并决定** `--` 自身是否继续当标准输入开关（`:477` 的 `Case "-"` 是否补 `arg = "-"` 判定）。属 `../decisions.md` **D5** 节（基础功能的落地细节以 C# / csi 实现为设计蓝本）的适用面内。
- **方向 B（保持现状，只去掉断言）**：把 `:198` 的裸断言改成带门的形态（等价 C# 的 `optionsEnded || …`），消掉「合法输入撞断言」，行为分歧（`--` 之后的 option 形状 token 的分类、`--` 当标准输入开关）保留。这是最小改动，且不改任何可观察语义。

两者都在 `../decisions.md` **D4** 的提案闸门内（方向 A 改可观察行为、方向 B 改诊断/断言面），且方向 A 触及本仓 `README.md` **§一 非范围**的「不为『C# 有、VB 没有』的语言特性造 VB 实现」边界 ⇒ **不在本补测任务实施**。

## Release 构建

**推测**（未实跑 Release 构建验证）：`Debug.Assert` 的调用在 Release 构建下被条件编译移除 ⇒ 第 1 行的输入不再触发断言；但第 2 行的分类分歧与 `:202` 的无门判定与构建配置无关 ⇒ **行为分歧在 Release 下依然存在**。

## 相关

- 分歧的用例钉子与注释：`Scripting\VisualBasicTest\ScriptModeArgsTests.vb` 格 2（`DoubleDashInteractiveMode_SeparatesTheSourceFileFromTheScriptArguments`，`:249`）、格 7（`DoubleDashBeforeTheScriptPath_KeepsTheTrailingAtTokensAsArguments`，`:375`）、格 8（`DoubleDashScriptNameAndDashArguments_ParseTheScriptArguments`，`:402`；该格注释写明「`--` 在 VB 里落 `Case "-"`，该半格无 VB 对偶」）。
- 共享分流器（两语言共用，本分歧**不在**它）：`Compilers\Core\Portable\CommandLine\CommandLineParser.cs` 的 `FlattenArgs`（`:500` / `:530` / `:547` / `:556` / `:586`）。
- C# 对偶：`Compilers\CSharp\Portable\CommandLine\CSharpCommandLineParser.cs` 的 `Parse` —— **声明于 `:52`**（`:52` 的 `public new CSharpCommandLineArguments Parse(...)`），内部锚点 `:60`（`FlattenArgs` 调用，函数体第一行）/ `:137`（局部量 `bool optionsEnded = false`）/ `:169` / `:174` / `:313` / `:315` / `:330`。
- 决策与义务：`../decisions.md` **D4**、**D5**；`../tasks/script-mode-coverage-parity/README.md` **§八 义务 1**（登记）、**义务 4**（变更面登记）。

## 后续（交用户裁决 / 停手上报）

- 本 issue 只登记与取证：**未改任何产品源码**，未加用例（触发断言的输入不能进测试套件——它会终止该测试并让该格永久红），不预填修复 commit。
- 若用户选方向 A 或 B：按 `../tasks/script-mode-coverage-parity/README.md` **§八 义务 4** 登记变更面；方向 A 的新行为需补回归用例（`--` 之后 token 的分类），并评估 `--` 自身分类变化对既有格 2/8 注释的影响。
