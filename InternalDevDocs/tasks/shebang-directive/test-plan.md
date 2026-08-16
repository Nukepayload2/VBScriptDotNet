# 测试计划：`.vbx` 首行 `#!` shebang 指令

> 状态：测试计划（F3）。依据链：`design-overview.md`（行为对照表）→ `design-detailed.md`（M0/M1 落点）。
> 测试强度参考 C# 四层（`Compilers\CSharp\Test\Syntax\Parsing\ScriptParsingTests.cs:10201-10324` + `IgnoredDirectiveParsingTests.cs:160-814`）+ VB 语法特性综合。
> 无副作用纪律（CLAUDE.md）：禁止网络/文件写入/进程启动/注册表写入；内存 `StringReader/StringWriter`。

## 落点

| 层 | 测试项目 | 文件 |
|----|---------|------|
| L1 解析树形 | `Compilers\VisualBasicSyntaxTest\` | `Parser\ShebangDirectiveParsingTests.vb`（新增） |
| L2 语义诊断 | `Compilers\VisualBasicSyntaxTest\` | 同上（parse 级诊断，随树形同测） |
| L3 Scripting 编译 | `Scripting\VisualBasicTest\` | `ScriptTests.vb`（追加用例） |
| L4 REPL/边界 | `Scripting\VisualBasicTest\` | `InteractiveSessionTests.vb`（追加用例） |

**运行方式**：`Scripting\VisualBasicTest` 是 MTP 项目，`dotnet test` 静默不跑——**直接跑程序集 `-automated`**（记忆：`vb-scripting-test-runner`）；编译器语法测试走 `scripts\verify-vb-compiler-tests.ps1` 七门 gate 的 Syntax 门。

## 行为基准（C# 权威测试）

- 首行通过：`ShebangCorrectlyPlaced`（`IgnoredDirectiveParsingTests.cs:650`）——`#!` 变首个真实 token 前导 trivia，`EndOfLineTrivia` 在指令尾。
- 位置违规：`ShebangNotFirst`（:173）、`ShebangWithTriviaInBetween`（:714）、`ShebangIncorrectlyPlaced`（:748）——**error**（`error CS9378`）+ 仍解析为 shebang。
- 门控：`ShebangNotInScript`（`ScriptParsingTests.cs:10291`）——常规编译 `error CS9314`；`ShebangNotFirst(bool script, bool featureFlag)`（:173）——常规无 flag 双错误（9378+9314）。
- `#x`：`ShebangNoBang`（:10265）——`ERR_PPDirectiveExpected`（VB 对等 `ERR_ExpectedConditionalDirective`）。
- 注释内：`ShebangInComment`（:10279）——纯注释。

## L1 解析树形（`ShebangDirectiveParsingTests.vb`）

| # | 用例 | 断言 |
|---|------|------|
| L1-1 | `.vbx` 首行 `#!/opt/vbi-n2fork/vbi` + `Dim x = 1` | 首个真实 token 前导含 `ShebangDirectiveTrivia`；节点 = `HashToken` + `ExclamationToken` + 尾随 `SkippedTokensTrivia`（路径文本） |
| L1-2 | 同上 | `root.GetDirectives()` 返回该 `ShebangDirectiveTrivia` |
| L1-3 | 首行 shebang + 第 2 行 `Dim x = 1` + 第 3 行错误代码 | 错误诊断行号 = 3（**行号不漂移**） |
| L1-4 | 第二行 `#!x`（`// Comment\n#!x`） | 仍解析为 `ShebangDirectiveTrivia`（树结构完整），错误挂在 `#` token |
| L1-5 | `'#!/...`（注释内） | 纯 `CommentTrivia`，无 shebang 节点 |
| L1-6 | `#x` | `BadDirectiveTrivia` + `ERR_ExpectedConditionalDirective`（行为不变） |

## L2 语义诊断（同文件）

| # | 用例 | 期望诊断 |
|---|------|---------|
| L2-1 | script 首行 `#!cmd` | 无诊断 |
| L2-2 | script 第二行 `#!cmd` | `ERR_ShebangDirectiveNotOnFirstLine`（**error**）@ `#` |
| L2-3 | script 前置空白 `  #!cmd` | 同上 |
| L2-4 | script `# !cmd`（`#` 与 `!` 间隔空格） | 同上（`hashToken.HasTrailingTrivia`） |
| L2-5 | script `#! cmd` 与 `#!cmd`（路径前有无空格） | 均无诊断（路径任意内容） |
| L2-6 | 常规 `.vb`（非 script）首行 `#!cmd` | `ERR_ShebangDirectiveOnlyAllowedInScripts`（**error**） |
| L2-7 | 常规 `.vb` 非首行 `#!cmd` | `OnlyAllowedInScripts` + `NotOnFirstLine` 双错误（镜像 C# `ShebangNotFirst` 无 flag 分支） |
| L2-8 | 路径内容任意（空格、`/`、`#`、`-`、`/usr/bin/env -S python`） | 整行作 trivia，无错误 |
| L2-9 | `\uFEFF#!cmd`（BOM 作为源码字符） | **无 shebang 节点 + BC30037/BC30201**（VB 实际行为：`CharacterInfo.IsWhitespace` 不认 U+FEFF（Format 类），`#` 不被识别为指令）。附注：真实带 BOM 的文件经文件读取层解码会剥掉 BOM，`#` 仍位置 0 正常 |
| L2-10 | `#If False` 禁用区内 `#!` / 激活区内错位 `#!` | 禁用区内：**无节点、无诊断**（禁用区文本不当作指令解析；该边界 C# 侧无对等测试，VB 现状为不识别）；激活区内（非首行）：仍报 `NotOnFirstLine` |
| L2-11 | severity | 断言两枚错误码均为 **error**（非 warning） |

## L3 Scripting 编译（`ScriptTests.vb`）

| # | 用例 | 断言 |
|---|------|------|
| L3-1 | `.vbx` 首行 `#!/opt/vbi-n2fork/vbi` + `Dim x = 1` | 编译通过，0 诊断，`x` 语义正常 |
| L3-2 | 首行 `#!` + 后续 `#R "..."` / `#Load "file.vbx"`（内存 resolver） | 共存正常，指令各自解析 |
| L3-3 | `.vbx` 首行 shebang + 末尾表达式 | 脚本模式不设退出码（既有语义不受影响） |

## L4 REPL/边界（`InteractiveSessionTests.vb`）

| # | 用例 | 断言 |
|---|------|------|
| L4-1 | REPL 提交首行 `#!...` | 接受并空转（无错误、无输出），遵循 C# `csi` |
| L4-2 | `Content` API | `shebang.Content` 返回路径文本（`StringLiteralToken`，`ToString()` = 路径） |
| L4-3 | 多行提交：首行 `#!` + 代码 | 正常，shebang 不干扰提交语义 |
| L4-4 | `WithContent` | 改写路径后 `ToFullString()` 一致 |

## 既有测试影响核查

- `Compilers\VisualBasicSemanticTest\Compilation\CompilationAPITests.vb:271`——`";,*?<>#!@&"` 是**文件名非法字符**测试字符串，与 shebang 无关，**不受影响**（已核实）。
- 常规 `.vb` 编译路径新增两枚错误码，只影响「含 `#!` 的常规编译」这一原本就报错的场景，无既有合法代码回归。

## 验收条件

1. 四层矩阵全绿；两枚错误码 severity 断言 **error**；行号不漂移断言通过。
2. 无副作用纪律：编译诊断测试用内存源；Scripting 测试用内存 resolver / `StringReader`。
3. 与 proposal/meeting RESOLUTION 一致（error、仅 script、位置 0、Content API；REPL 空转，BOM/`#If False` 按实测：不识别、无节点无诊断，见 L2-9/L2-10）。
