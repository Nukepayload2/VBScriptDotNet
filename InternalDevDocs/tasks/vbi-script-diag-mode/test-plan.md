# 测试计划：vbi 脚本模式诊断检查 `/check`

> 状态：测试计划（F3）。依据链：`../../proposals/proposal-vbi-script-diag-mode.md`（冻结，原 `/diag`）→ `../../meetings/meeting-vbi-script-diag-mode.md`（Active，RESOLUTION #1-#12）→ 详细设计 `design-detailed.md`。
> 测试宿：`Scripting\VisualBasicTest\`（MTP 项目，`dotnet test` 静默不跑，须直接跑程序集 `-automated`）。
> 无副作用纪律：内存 `TestConsoleIO`/`StringReader`；不启动进程、不网络、不注册表；临时 `.vbx` 文件（脚本用例必需）复用 `CreateIsolatedTempDirectory`（`CommandLineRunnerTests.vb:42-46`）。

## 1. 测试分层矩阵

| 层 | 适用性 | 说明 |
|----|--------|------|
| L1 解析 | ✅ | 脚本专属分支加 `/check` 开关 + `check` 布尔 + 互斥无文件错误（C1/C3，由 L4 解析断言 + 互斥用例覆盖） |
| L2 语义 | **N/A** | 无绑定/语义改动 |
| L3 API | ✅ | `Script.Compile()` 返回警告/错误语义（`/check` 依赖的消费端） |
| L4 宿主/REPL | ✅ | `CommandLineRunnerTests`：`Check` 解析断言 + 冒烟 + 退出码 + 互斥 + 回归 |

## 2. L3 API（`Script.Compile()` 语义，`Scripting\VisualBasicTest\`）

| # | 用例 | 断言 | 无副作用 |
|---|------|------|---------|
| A1 | `VisualBasicScript.Create("Console.WriteLine(1)")` → `script.Compile()`（干净脚本） | 无 Error 诊断（`HasAnyErrors` false） | 纯内存 |
| A2 | `Create("Dim unusedVar As Integer")`（未使用变量，BC42104）→ `Compile()` | 含 Warning 诊断、无 Error | 纯内存 |
| A3 | `Create("Console.WriteLine(notDeclared)")`（未声明，BC30451）→ `Compile()` | 含 Error 诊断、`HasAnyErrors` true | 纯内存 |

> 用途：证明 `Script.Compile()`（`Script.cs:231` → `CommonCompile` `:332-346`）成功仅警告、失败错误+警告、不抛异常——`/check` 宿主分支（C4）直接依赖此语义。命名空间 `Microsoft.CodeAnalysis.VisualBasic.Scripting`（`VisualBasicScript.vb:18`）。

## 3. L4 宿主（`CommandLineRunnerTests.vb`）

### 3.1 `Check` 解析断言

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| H1 | 默认 | `CreateRunner(args:={"/R:System"})` | `runner.Compiler.Arguments.Check = False` | 纯内存 |
| H2 | `/check` | `CreateRunner(args:={"/check", "/R:System"})`（带脚本文件时） | `runner.Compiler.Arguments.Check = True` | 纯内存 |

> `runner.Compiler` internal（`CommandLineRunner.cs:46`），测试同程序集可访问。此层证明 `/check` 经脚本分支 `Case "check"`（C1）→ 基类 `CommandLineArguments.Check`（C2）的链路。**H1 默认路径不受改动影响；H2 依赖 C1/C2，修复后转绿。**

### 3.2 冒烟（`/check` 脚本文件：诊断显示 + 不执行 + 退出码）

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| H3 | `/check` 干净脚本 | 临时 `clean.vbx`（`Console.WriteLine("RAN")`）+ `CreateRunner(args:={"/check", "clean.vbx"}, workingDirectory:=dir)` | exit 0；**stderr 无诊断**（干净通过）；**stdout 不含 `RAN`**（未执行） | 临时 `.vbx` 文件（脚本用例必需） |
| H4 | `/check` 有错误 | 临时 `err.vbx`（`Console.WriteLine(notDeclared)`）+ `/check` | exit 1；stderr 含 BC30451；**stdout 不含执行输出**（未执行） | 临时 `.vbx` 文件 |
| H5 | `/check` 仅警告 | 临时 `warn.vbx`（`Dim unusedVar As Integer` + `Console.WriteLine("RAN")`）+ `/check` | exit 0；stderr 含 BC42104 警告；**stdout 不含 `RAN`**（未执行） | 临时 `.vbx` 文件 |

> 冒烟断言核心：**`/check` 不执行**（stdout 无脚本输出）+ **全量显示警告**（H5，区别于现状成功路径吞警告）。仿既有脚本用例基建（`TestLoadDirectiveInScriptFile` `:248` 等）。

### 3.3 互斥与边界

| # | 用例 | 输入 | 断言 | 无副作用 |
|---|------|------|------|---------|
| H6 | `/check /i` 优先 | 临时 `err.vbx` + `CreateRunner(args:={"/check", "/i", "err.vbx"}, workingDirectory:=dir)` | exit 1、stderr 含 BC30451；**无 `>` 交互提示**（未进 REPL） | 临时 `.vbx` 文件 |
| H7 | `/check` 无文件 | `CreateRunner(args:={"/check", "/R:System"})`（无源文件） | exit 1；`Arguments.Errors` 含无文件错误（C3）；**无 `>` 交互提示** | 纯内存 |
| H8 | `/check t.vb` | `CreateRunner(args:={"/check", "t.vb"})` | 脚本解析器对非脚本扩展名 → `ERR_ExpectedSingleScript`、exit 1 | 纯内存 |

> **H8 边界说明**：真实 `vbi.exe` 中 `vbi /check t.vb` 由 `IsCompileInvocation`（`Vbi.Compile.vb:32-59`）先拦截进**编译模式**（`/check` 落 BC2007、照常发射）——那是 exe 分派层行为，`CommandLineRunnerTests` 直接构造 runner 不走该层，H8 断言的是脚本解析器对 `.vb` 扩展名的处理（`ERR_ExpectedSingleScript`）。`.vb` 边界（RESOLUTION #7）的**端到端验证放 F7 集成/手动**。

### 3.4 回归面

| # | 用例 | 断言 | 无副作用 |
|---|------|------|---------|
| H9 | 默认（无 `/check`）冒烟 | `CreateRunner(input:="? 1 + 2")` → `RunInteractive()` | 输出含 `3`（既有行为不破） | 纯内存 |

## 4. 端到端观察点

`/check` 分支在 `RunScriptAsync` 内 `script.Compile()` 后直接返回——**可观测面就是退出码 + stderr 诊断 + stdout 无执行输出**（H3-H7 已覆盖）。无需 internal 测试钩子（区别于 optimize 任务的 `Script.Options` 不可达问题）：`/check` 不执行，无副作用面需要额外观察。

- 组合证明：L3 A1-A3（`Script.Compile()` 语义）+ L4 H1-H7（解析 + 冒烟 + 互斥 + 退出码）+ C4 一行分支的 code review。
- `.vb` 边界（RESOLUTION #7）端到端放 F7 集成验证（`vbi /check t.vb` → 编译模式 BC2007）。

## 5. 验收 gate

- **plan 阶段标准（F3 完成）**：分层矩阵完整、无副作用纪律满足、观察点明确、回归面列出。
- **实现阶段标准（F6 完成）**：L3 A1-A3 + L4 H1-H9 全绿；`Scripting\VisualBasicTest` **全量 0 失败**（MTP 直接跑程序集 `-automated`）；回归面全量通过；无副作用纪律（临时文件仅限宿主必需——H3-H6 临时 `.vbx`，其余零文件写入、零进程、零网络）。
- **F7 集成**：`vbi /check foo.vbx` 实机冒烟（干净 0 / 错误 1 / 警告显示）+ `.vb` 边界 + MSYS 裸开关说明落帮助文本。
