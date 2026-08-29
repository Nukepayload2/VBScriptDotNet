# 详细设计：vbi 脚本模式诊断检查 `/check`

> 状态：详细设计（F2）。依据链：`../../proposals/proposal-vbi-script-diag-mode.md`（冻结，原 `/diag`）→ `../../meetings/meeting-vbi-script-diag-mode.md`（Active，RESOLUTION #1-#12）→ 概要设计 `design-overview.md`。
> 改动清单逐条给出，实施者可照做。源码事实以任务 README「共享源码事实」为基准；行号以 2026-08-29 源码为准，F2 实现前复核。

## 1. 改动清单（C1–C7）

### C1. VB 脚本解析器脚本分支新增 `/check` 开关 + `check` 布尔

- **文件**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`
- **函数**：`Parse`（脚本专属 `Select Case name`，`:474-542`；布尔区 `:97` 附近）
- **改动形状**：
  - 布尔区（`Dim optimize As Boolean = False` 附近，`:97`）新增 `Dim check As Boolean = False`。
  - 脚本专属 `Select Case`（`i`/`i+` 等后）新增：
    ```vb
    Case "check"
        If value IsNot Nothing Then
            AddDiagnostic(diagnostics, ERRID.ERR_SwitchNeedsBool, "check")
            Continue For
        End If
        check = True
        Continue For
    ```
  - 构造 `VisualBasicCommandLineArguments`（`:1546-1551` 对象初始化器，`.InteractiveMode = interactiveMode` `:1549` 旁）新增 `.Check = check`（基类属性，见 C2）。
- **复用**：裸开关 + `value` 校验仿脚本分支 `/i`（`:485-490`）；不落 `WRN_BadSwitch`（`:1341`）。
- **不动**：非脚本分支（`Else` `Select Case`，`:543-1338`）、C# 侧。

### C2. 基类 `CommandLineArguments` 新增 `Check` 属性

- **文件**：`Compilers\Core\Portable\CommandLine\CommandLineArguments.cs`
- **位置**：`InteractiveMode`（`:31`）附近
- **改动形状**：仿 `InteractiveMode` 声明方式新增 `public bool Check { get; internal set; }`（默认 false；确切可写性修饰符 F2 实现时照抄 `InteractiveMode`）。
- **可达性**：基类属性 → 共享 runner（`CommandLineRunner.cs`）直接读 `_compiler.Arguments.Check`，无需 VB 专属 cast；VB 脚本解析器对象初始化器置位；C# 侧不置位天然 false。
- **不动**：`InteractiveMode` 及其余属性。

### C3. 互斥规则（解析器层：抑制自动交互 + 无文件错误）

- **文件**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb`
- **函数**：`Parse` 收尾区
- **改动形状 A（抑制自动交互）**：`:1542`
  ```vb
  ' 现状
  interactiveMode = interactiveMode Or (IsScriptCommandLineParser AndAlso sourceFiles.Count = 0)
  ' 改为
  interactiveMode = interactiveMode Or (IsScriptCommandLineParser AndAlso sourceFiles.Count = 0 AndAlso Not check)
  ```
- **改动形状 B（无文件错误）**：同一收尾区、`:1542` 之后新增：
  ```vb
  If check AndAlso sourceFiles.Count = 0 Then
      AddDiagnostic(diagnostics, ERRID.ERR_ExpectedSingleScript)   ' F2 复核消息文本；备选 ERR_ArgumentRequired 以 ":<script-file>"
  End If
  ```
  > 选用现有 ERRID 避免新增资源面；`ERR_ExpectedSingleScript` 是 runner `:122` 已用的脚本错误码。F2 实现时读 `MessageProvider`/`VBResources.resx` 核实消息可读性，必要时换 `ERR_ArgumentRequired`。
- **效果**：`vbi /check`（无文件）→ 错误诊断 + 不进交互；`vbi /check /i foo.vbx` → `/i` 置 `InteractiveMode` 由 C5 处理（runner 无视）。

### C4. 宿主 `RunScriptAsync` 新增 Check 分支（核心）

- **文件**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`
- **函数**：`RunScriptAsync`（`:201-222`）
- **改动形状**：`:206` 创建 `script` 之后、`:209` `RunAsync` 之前插入：
  ```csharp
  if (_compiler.Arguments.Check)
  {
      // /check：只编译拿诊断，不执行不落盘；退出码 0 = 无编译错误（可有警告），1 = 有错误
      var diagnostics = script.Compile(cancellationToken);
      return _compiler.ReportDiagnostics(diagnostics, _console.Error, errorLogger, compilation: null)
          ? CommonCompiler.Failed
          : CommonCompiler.Succeeded;
  }
  ```
- **语义**：`script.Compile()`（`Script.cs:231` → `CommonCompile` `:332-346`）成功仅警告、失败错误+警告、不抛异常；`ReportDiagnostics`（`CommonCompiler.cs:515-583`）返回 `hasErrors`、打印全部非 Hidden 诊断（**全量，非 `DisplayDiagnostics` 5 条截断**）；不调用 `RunAsync` → 脚本副作用零发生。
- **前置**：`code == null`（文件读取错误）已在 `RunInteractiveCoreAsync` `:126-140` 经 `diagnosticsInfos` 报错并返回，Check 分支只在 `code` 非空时到达。

### C5. 宿主 `RunInteractiveCoreAsync` Check 无视 InteractiveMode

- **文件**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`
- **函数**：`RunInteractiveCoreAsync`（`:80-153`）
- **改动形状**：`:144` 交互分流前处理：
  ```csharp
  if (_compiler.Arguments.Check)
  {
      // /check 优先于 /i：强制脚本路径，不进 REPL（无文件错误已在解析器层产出）
      return await RunScriptAsync(scriptOptions, code, errorLogger, cancellationToken);
  }
  if (_compiler.Arguments.InteractiveMode)
  {
      await RunInteractiveLoopAsync(...);
      return CommonCompiler.Succeeded;
  }
  else
  {
      return await RunScriptAsync(...);
  }
  ```
- **效果**：`vbi /check /i foo.vbx` → 只编译不执行，忽略 `/i`。

### C6. help 资源

- **文件**：`Scripting\VisualBasic\VBScriptingResources.resx`（`InteractiveHelp` 字符串）+ `Scripting\VisualBasic\VBScriptingResources.Designer.vb`
- **改动形状**：`InteractiveHelp` 新增：
  - `/check                         只编译、不运行、不产出文件（输出错误+全部警告）；与整数溢出开关 /removeintchecks 无关`
  - 裸开关族说明一行：`/i /nostdlib /optimize+ /check 为裸开关，git-bash/MSYS 下请设 MSYS2_ARG_CONV_EXCL='*'`
- **注意**：`/help` 输出经 `Vbi.vb:52-54` `PrintHelp` → `VBScriptingResources.InteractiveHelp`。既有 `s_logoAndHelpPrompt` 断言（`CommandLineRunnerTests.vb:28-34`）不含 `InteractiveHelp`——**实现 C6 无需改既有断言**（参照 script-optimization-level C2 的核验结论）。

### C7. 测试（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb` 等）

见 `test-plan.md`；此处列宿主测试所需访问点：

- `CreateRunner(args, input, responseFile, workingDirectory)`（`CommandLineRunnerTests.vb:91-116`）构造 runner。
- 解析断言：`runner.Compiler.Arguments.Check`（`runner.Compiler` internal，`CommandLineRunner.cs:46`）。
- 脚本文件用例：临时 `.vbx` + `CreateRunner(args:={"/check", "main.vbx"}, workingDirectory:=directory)`（复用 `CreateIsolatedTempDirectory` / 既有脚本测试基建）。

## 2. 不做的事（明确排除）

- **`/warnaserror`/`/nowarn`/`/ruleset` 透传**：脚本模式不识别（BC2007），v1 不带（RESOLUTION #6）；严格门控留分析器面。
- **分析器运行**：`/check` 只出编译器诊断，`CompilationWithAnalyzers` 独立立项（#11）。
- **普通执行路径前置警告**：`vbi foo.vbx` 成功路径吞警告现状不改（Follow-up #12，单独立项）。
- **`/diag` 保留**：命名已裁决 `/check`（#1），不加别名。
- **`Option Checked`**：源级 Option 是未来语言特性，非本提案范围（#8）。
- **语法/绑定层改动**：不涉及；解析器改动仅限 VB 脚本专属分支 + 基类加属性（C# 侧不置位）。
- **MSYS 代码级处理**：不自动转换参数，文档 + `MSYS2_ARG_CONV_EXCL`（#9）。

## 3. 边界与迁移影响

| 项 | 影响 |
|----|------|
| 默认行为 | 无 `/check` → 编译+执行（现状），`RunScriptAsync` 原路径不动（零回归面） |
| `/check foo.vbx` | 只编译、显示错误+全部警告、不执行；exit 0/1 |
| `/check /i foo.vbx` | `/check` 优先，忽略 `/i`（C5） |
| `/check`（无文件） | 解析器报错（C3），不进交互，exit 1 |
| `/check t.vb` | `IsCompileInvocation` 见 `.vb` → 编译模式，`/check` 落 BC2007 警告、照常发射（文档写明，不修） |
| `/check foo.vbx /warnaserror:on` | `/warnaserror:on` BC2007 警告被忽略（脚本模式不识别） |
| `#load` 链 | `CommonCompile` `GetPrecedingExecutors`（`Script.cs:337-338`）前置脚本同样内存 emit——"无落盘无执行"，不落盘不执行 |
| csi | 无关：本 fork 无 `Scripting\CSharp` 宿主；基类 `Check` C# 侧不置位 |
| 源级 `Option Strict On` | 逐文件覆盖默认 `Off`（`VisualBasicScriptCompiler.vb:204`），`/check` 严格性由文件内容决定 |
| 参数顺序 | `/check` 必须在脚本文件**之前**（`CommandLineParser.cs:539-545`：文件后参数一律脚本参数） |

## 4. 测试矩阵（概要）

- **L1 解析**：`/check` 识别（脚本分支 `Case "check"`）+ 互斥（`vbi /check` 无文件 → 错误；`/check /i` 后 `InteractiveMode` 被 C5 覆盖）。
- **L2 语义**：N/A——无绑定/语义改动。
- **L3 API**：`VisualBasicScript.Create` + `script.Compile()` 返回警告/错误语义（成功仅警告、失败错误+警告）——证明消费端现成。
- **L4 宿主**：`CommandLineRunnerTests`——`Check` 解析断言（默认 false、`/check` true）、`/check` 脚本文件冒烟（诊断显示 + 不执行 + 退出码 0/1）、`/check /i` 优先、`/check` 无文件报错、`/check` 配 `.vb` BC2007、既有用例回归。
- 详见 `test-plan.md`。
