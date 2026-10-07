# 任务：字符串脚本工厂补传 `FileEncoding`（string-script-factory-file-encoding）——任务计划

> **状态：已收口（F01–F04 全做完，待作者提交）**——落地并复跑：`VisualBasicScript.vb:29` 改传 `options?.FileEncoding`（唯一改动，公共签名不变、`PublicAPI.*.txt` 未动）；回收 `ScriptModePdbTests.vb` 格 2 + 三处字符串+UTF8 树形子断言 + 新增 C3/C5 ⇒ `Scripting\VisualBasicTest` 直跑 **768/0**；七门全绿（编译器面结构性无关）。账本：`..\..\upstream-merge.md` §2.25(f)、issue 24 转 Fixed、队列台账 `..\csharp-script-parity-sweep\README.md` **#4**→**#5** 转已验证。流水账 `tmp\vortex-logs\string-script-factory-file-encoding\`。**子 agent 禁读 `issues\`**——症状与判据在本文件复述自足。

- **一句话**：`VisualBasicScript.Create(Of T)(code As String, …)`（`Scripting\VisualBasic\VisualBasicScript.vb:29`）把字符串折成 `SourceText` 时**不传** `ScriptOptions.FileEncoding`，而同文件的流重载（`:39`）、C# 对偶与共享基类都传 ⇒ 「字符串 + `WithFilePath` + `WithEmitDebugInformation(True)`」这一形状命中逐树 debug document 门（`Compilation.cs:2518`）报 `BC37236`。
- **D7 裁定（已自动裁）**：可移植 ⇒ 修。C# 侧与共享基类都传编码，本 fork 写不出"必须分叉"的理由；"上游 VB 也这样"与"产品内不可触达"都不是理由（`decisions.md` D6/D7：beta 期不用兼容性/可达性压缺陷）。

## 一、判据
1. 字符串重载与流重载在**同一 `ScriptOptions`** 下产出的 `SourceText` 属性一致：`Encoding` 相等、`CheckedPath`（由 `WithFilePath` 提供）相等。
2. 传 `WithFileEncoding(...)` 时，字符串形状**不得**再报 `BC37236`；不传时行为逐字不变（无编码 ⇒ 仍按既有门报同一条诊断，**不得**顺手放宽那道门）。
3. `ContinueWith(String)`（同一族的另一入口）与 `Create(String)` 采用**同一实现**，不留两份相似特例。
4. 公共面零改动：不加参数、不改签名、不动 `PublicAPI.*.txt`。

## 二、范围内
| # | 单元 | 说明 |
|---|---|---|
| F01 | 实现 | `Scripting\VisualBasic\VisualBasicScript.vb` 字符串工厂把 `options.FileEncoding` 传给 `SourceText.From`（与流重载同形）；`ContinueWith(String)` 若另有一份，收敛到同一处 |
| F02 | 单元测试 | 见 `test-plan.md`：正向（编码 + 文件路径 + debug ⇒ 发 PDB / 零 `BC37236`）、反例锁（不传编码时诊断不变）、两重载属性一致性对照、`ContinueWith` 同形格。**定向**跑 `Scripting\VisualBasicTest`（MTP ⇒ 直跑程序集带 `-automated`） |
| F03 | 回归（main 做） | 七门全量由 main 跑；agent 禁跑全量 |
| F04 | 账本 | issue 24 转 Fixed（commit 不预填）；`upstream-merge.md` 入账：该文件是**上游同名文件**（基线 commit 树里有，`git cat-file -e` rc=0）⇒ 记函数级分歧；两条既有钉桩（`ScriptModeApiSurfaceConformanceTests.vb:59-110` 一带的 U7 格 2 / 格 8 把该分歧做成断言）按新行为回收 |

### 非范围
- 不改 `Compilation.cs:2518` 的逐树 debug document 门本身；不引入新诊断码、不改 resx/xlf。
- 不碰 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `WarningLevel` 转发（那是队列 #6 的面，同文件不同函数，**排在其后**以免同文件冲突）。

## 三、风险与停止上报
- 该形状在**产品路径不可触达**（文件模式走 `TryReadFileContent`，REPL 的 `emitDebugInformation` 恒假）⇒ 测试必须**直接走公共 API**构造，不得靠宿主间接验证；若发现"修完仍不可达"⇒ 那正是本任务要修的形状，不算停手理由。
- 构建面独占（BC2012）；`Scripting\VisualBasicTest` 是 MTP 工程。
