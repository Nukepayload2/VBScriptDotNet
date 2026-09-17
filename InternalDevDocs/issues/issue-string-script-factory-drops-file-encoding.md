# 字符串脚本工厂丢弃 `ScriptOptions.FileEncoding`：带路径 + 开调试信息的字符串脚本报 `BC37236`，C# 对偶正常发 PDB

- **状态**：**Open**（**交用户裁决 / 停手上报**；不自行改产品源码、不自行改语义，不预填 commit）
- **发现日期**：2026-09-16
- **发现场景**：`../tasks/script-mode-coverage-parity/` 的 U7（PDB / 调试信息与栈帧行号）补测。登记依据同任务 `README.md` **§八 义务 1** 末句（「实施期若发现别的真缺陷，同样按此登记」）。
- **影响面**：`Microsoft.CodeAnalysis.VisualBasic.Scripting` 的**公共 API 面**——`VisualBasicScript.Create(Of T)(code As String, …)` / `Create(code As String, …)`，及其下游 `RunAsync(String, …)` / `EvaluateAsync(String, …)`。**本 fork 产品自身不可触达**（取证见「性质判定」第 2 条）；触达者是使用该公共 API 的**外部宿主**。
- **严重度**：对本 fork 产品**低**（产品的 `FileEncoding` 恒为 `Nothing`，组装不出触发向量）；对公共 API 宿主**中**（契约不兑现，且失败方式是一条指向真实内部状态的诊断，宿主无从判断这是实现缺口）。

## 触发面

`ScriptOptions.FileEncoding` 的契约是「源文本**没有自带编码**时用它作为文本编码」，而调试信息的文档编码直接取自 `SourceText.Encoding`。`VisualBasicScript` 的两个**字符串**工厂重载**不把该选项交给 `SourceText`**；同产品的**流**重载与共享基类的 `ContinueWith(String)` 都交。

于是「字符串创建 + `WithFilePath` + `WithEmitDebugInformation(True)`」这一组合下：树带路径但编码为 `Nothing` ⇒ 命中 debug document 门（`Compilers\Core\Portable\Compilation\Compilation.cs`，符号 `CreateDebugDocuments`，`:2512`；条件 `:2518`，报点 `:2520`）⇒ 发射失败并报 `BC37236`。

该门的条件是**逐树**的：

```
:2518  if (!string.IsNullOrEmpty(tree.FilePath) && tree.GetText().Encoding == null)
:2520      diagnostics.Add(MessageProvider.CreateDiagnostic(MessageProvider.ERR_EncodinglessSyntaxTree, tree.GetRoot().GetLocation()));
```

⇒ **触发要求「有路径」**。无路径的字符串脚本（内联代码）不受影响——这解释了本 issue 只落在「code from a file」那一组形状上。

## 四处源码对照

| # | 工厂 | 证据 | 是否传编码 |
|---|---|---|---|
| ① | `VisualBasicScript.Create(Of T)(code As String, …)` | `Scripting\VisualBasic\VisualBasicScript.vb:29`：`SourceText.From(If(code, String.Empty))` | **否** |
| ② | `VisualBasicScript.Create(Of T)(code As Stream, …)`（fork 新增） | 同文件 `:39`：`SourceText.From(code, options?.FileEncoding)` | 是 |
| ③ | C# 对偶 `CSharpScript.Create<T>(string code, …)` | `{{Roslyn}}`（本机值见 `../upstream-merge.md` §一）`src\Scripting\CSharp\CSharpScript.cs:37`：`SourceText.From(code, options?.FileEncoding, SourceHashAlgorithms.Default)` | 是 |
| ④ | 共享基类 `Script.ContinueWith<TResult>(string code, …)` | `Scripting\Core\Script.cs:117`：`SourceText.From(code ?? "", options.FileEncoding)` | 是 |

①与②在**同一个类、相邻两个重载**里对同一个选项给出相反处理；①与④在同一产品内对同一个入口形态（字符串）给出相反处理。④是本条目最直接的判据：产品的**共享基类**已经确立了「字符串路径尊重 `FileEncoding`」这一内部契约，字符串工厂是与之相悖的那一处。

## 实测读数

触发向量（U7 用例的构造，与 C# 基线格里 `debug.csx` 同形）：

```vbnet
VisualBasicScript.Create(ThrowingCode, ScriptOptions … .WithFilePath("debug.vbx").
                                        WithEmitDebugInformation(True).
                                        WithFileEncoding(Encoding.UTF8))
```

| # | 向量 | 读数 | 三态 |
|---|---|---|---|
| 1 | 字符串 + 路径 + 开调试信息 + **无**编码（①，选项 `Nothing`） | 树的 `GetText().Encoding Is Nothing`；内存发射 `result.Success = False`，诊断**恰好 1 条** `BC37236`、严重级 `Error`、锚点 `debug.vbx:1`；宿主运行脚本时收到含同一 ID 的 `CompilationErrorException` | **实锤**（U7 格 1，`ScriptModePdbTests.vb:385`，`Pdb_String_CodeFromFile_WithDebugInformation_WithoutEncoding_ReportsBC37236`） |
| 2 | 字符串 + 路径 + 开调试信息 + **设**编码（①，`Assert.Same(Encoding.UTF8, options.FileEncoding)` 为真） | **与第 1 行逐项相同**：选项已设，树**仍然无编码**，发射仍报 1 条 `BC37236`、锚点 `debug.vbx:1`。C# 在同一形状下**发 PDB 成功**、帧名为 `debug.csx` | **实锤**（U7 格 2，`:422`，`Pdb_String_CodeFromFile_WithDebugInformation_WithEncoding_ReportsBC37236`；C# 对标 `ScriptTests.cs:859`） |
| 3 | **流** + 路径 + 开调试信息（②，不设编码） | 树**带**编码（`SourceText.From(Stream, Nothing, …)` 补 UTF-8 无 BOM，`Compilers\Core\Portable\Text\SourceText.cs`，符号 `From`，`:201`）；PE 的 portable CodeView 指向 `<asm>.pdb`；帧 = `debug.vbx` **1:1** | **实锤**（U7 格 5，`:497`，`Pdb_Stream_CodeFromFile_WithDebugInformation_FrameNamesTheScriptFile`） |
| 4 | 字符串 + **无**路径 + 开调试信息（①） | 门**不触发**（条件的 `Not String.IsNullOrEmpty(FilePath)` 半边为假），发射**成功**，帧 = `""` **1:1** | **实锤**（U7 格 7，`:540`，`Pdb_String_InlineCode_WithDebugInformation_WithoutEncoding_FrameNamesTheEmptyPath`） |

**第 2 行与第 3 行的对照是本 issue 的判别对**：同一路径、同一调试信息开关、同一（缺省）编码来源，只因**创建重载**不同（字符串 vs 流），一侧失败一侧成功 ⇒ 差异**只**来自字符串重载丢弃了选项。第 4 行排除「字符串路径本身发不出 PDB」这一相反解释。

**诊断文案**（**实锤**，读自资源）：`Compilers\VisualBasic\Portable\VBResources.resx:5303` 逐字 `Cannot emit debug information for a source text without encoding.`（条目名 `ERR_EncodinglessSyntaxTree`）。

## 诊断质量（**不是**错锚）

该文案说的是「**源文本没有编码**」——在字符串路径下这**正是树当时的真实内部状态**（`GetText().Encoding Is Nothing`，第 1、2 行读数）。**病灶指向是准的**：诊断指出的就是发射失败的直接原因。

错位发生在**更上游**：`ScriptOptions.FileEncoding` 这一**选项契约**与①的**工厂实现**不一致。诊断只能看见树，看不见「调用方设过选项却没人传下去」这件事——它没有说谎，也没有指错地方，它只是无法报告一个它视野之外的断层。因此本条目**不**主张「诊断指向错误病灶」。

## 性质判定

**1. 继承的上游不对称（实锤）。** 与 `{{Roslyn}}` 登记的合并基准（`../upstream-merge.md` §一，基准 commit `0e401fcf66cbfd4aeb27a78408ab91cab3a6f207`，2026-07-27）对照：

- 上游 VB 的同名文件**只有字符串重载**（`{{Roslyn}}\src\Scripting\VisualBasic\VisualBasicScript.vb:22-27`），该重载的调用与①**逐字同形**（同为 `SourceText.From(If(code, String.Empty))`）；上游 VB **没有**流重载（`grep -c "code As Stream"` 读数 **0**）。
- 本 fork 该文件相对上游是**纯新增、零删除**：`diff` 的全部 hunk 都是 `<`（fork 多出行）形态，**变更型 hunk 计数为 0**（新增 106 行）。⇒ ①**一个字没动**，②是 fork 自己新加的重载。

**2. 对 fork 产品不可触达（实锤）。** 两条独立取证：

- **文件模式不经过①**：`vbi script.vbx` 走 `CommandLineRunner.RunScriptAsync(ScriptOptions, **SourceText** code, …)`（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:238`），文本由 `TryReadFileContent`（`:132`）从磁盘字节读出、**自带编码**，再直接交给 `Script.CreateInitialScript<int>(_scriptCompiler, code, options, …)`（`:243`）——这是 `SourceText` 入口，**不是**字符串工厂。
- **交互模式不触发门**：REPL 是①在产品里的**唯一**调用者（`Scripting\VisualBasic\Hosting\VisualBasicReplServiceProvider.vb`，符号 `CreateScript`，`:39`；调用在 `:40`），而产品把调试信息开关写死为 `emitDebugInformation = !_compiler.Arguments.InteractiveMode`（`CommandLineRunner.cs:137`）⇒ **交互模式永不开调试信息**，`ScriptOptions` 的构造点 `:203-204` 也只在这里组装（本仓产品内唯一的 `New ScriptOptions(…)` 调用点），`fileEncoding: null`；`ScriptOptions.Default` 同样是 `null`（`Scripting\Core\ScriptOptions.cs:33`）。⇒ 产品里既无选项来源、也无触发开关。

**3. 对使用公共 API 的外部宿主是潜在缺陷。** `WithFileEncoding` 是**已发布**的公共 API（`Scripting\Core\PublicAPI.Shipped.txt:112`；属性本身在 `:101`），而产品内部对它的处理**自相矛盾**：`Script.ContinueWith(String)`（`:117`）尊重它，字符串工厂（`:29`）不尊重。外部宿主按 C# 的同名 API 形状迁移代码时，会得到「选项静默失效 + 一条看似在讲别的事的诊断」。

**4. 上游此后是否已修（推测）。** 上述对照锚定的是 `../upstream-merge.md` **已登记的那个基准**，本机未向远端拉取更新，故「上游在其后的提交里是否已把该参数补上」**未取证**。

## 可判的断言已就位（修复的报警线）

U7 的格 2（`ScriptModePdbTests.vb:422`，`Pdb_String_CodeFromFile_WithDebugInformation_WithEncoding_ReportsBC37236`）与格 8（`:565`，`Pdb_String_InlineCode_WithDebugInformation_WithEncoding_FrameNamesTheEmptyPath`）把该不对称**本身**做成了断言：`options.FileEncoding` 已设（`Assert.Same`）而树**仍无编码**，且门触发报 `BC37236`。

⇒ **这是修复的报警线**：字符串重载一旦开始尊重该选项，格 2 与格 8 的树断言会**立刻变红**。替换断言已一并写好——格 5（`:497`，`Pdb_Stream_CodeFromFile_WithDebugInformation_FrameNamesTheScriptFile`）走流路径到达同一状态，其「调试目录 / 文档表 / 帧」三层断言即修复后字符串格应改成的形态。

## 预期行为（两种，交用户裁决）

**这一条不自行改语义**。两个候选方向都须用户定案：

- **方向 A（对齐 C# 传编码）**：把 `:29` 改成 `SourceText.From(If(code, String.Empty), options?.FileEncoding)`。落点一处、改动一行，效果是与③（C# 字符串重载）及④（共享基类字符串重载）一致。**注意**：这会**改变既有行为**——今天靠 `BC37236` 失败的字符串脚本会改为**发射成功**；按 **D6** 兼容性约束只对 GA 版成立（本 fork 为 beta / preview），「改了既有语义」不构成否决理由，但格 2 / 格 8 必须同步改写成格 5 的断言形态。
- **方向 B（保持上游同形）**：不动 `:29`，承认字符串重载的契约是「不读 `FileEncoding`」——但这样需一并处理 `Script.ContinueWith(String)`（`:117`）与②的**同产品内不一致**，且 `WithFileEncoding` 的公共契约在字符串路径上永久空转。最小改动形态是**只把矛盾写进文档**，不改码。

两个方向都属 `../decisions.md` **D5**（基础功能的落地细节以 C# / csi 实现为设计蓝本）的适用面内。方向 A 是一次产品源码改动，**不在本补测任务实施**。

## 相关

- 分歧的用例钉子与注释：`Scripting\VisualBasicTest\ScriptModePdbTests.vb` 格 2（`Pdb_String_CodeFromFile_WithDebugInformation_WithEncoding_ReportsBC37236`，`:340`；注释逐字含 tripwire 说明与「replacement assertions 见格 5」）、格 8（`Pdb_String_InlineCode_WithDebugInformation_WithEncoding_FrameNamesTheEmptyPath`，`:483`）、格 5（`:415`）。文件头注释 `:46-70` 有同一分歧的概述。
- C# 基线：`{{Roslyn}}\src\Scripting\CSharpTest\ScriptTests.cs` 的 `Pdb_CreateFromString_CodeFromFile_WithEmitDebugInformation_WithFileEncoding_ResultInPdbEmitted`（`:859`）为判别对；同族 12 格见 `:842–:937`。
- 选项到树的其余通道（**不受影响**）：`FilePath` 经 `VisualBasicScriptCompiler.vb` 的 `CreateSubmission`（`:183` 的 `ParseSyntaxTree`）落到树；`EmitDebugInformation` 的唯一消费者是 `Scripting\Core\Script.cs` 的 `GetExecutor`（`:361-365`），**不进** `VisualBasicCompilationOptions`。
- 诊断身份：`Compilers\VisualBasic\Portable\Errors\Errors.vb:1688`（`ERR_EncodinglessSyntaxTree = 37236`）；C# 对偶 `Compilers\CSharp\Portable\Errors\ErrorCode.cs:1298`（`= 8055`）。两语言**共用**同一个门。
- 决策与义务：`../decisions.md` **D5**、**D6**；`../tasks/script-mode-coverage-parity/README.md` **§八 义务 1**（登记）、**义务 4**（变更面登记）。

## 后续（交用户裁决 / 停手上报）

- 本 issue 只登记与取证：**未改任何产品源码**（`Compilers\` / `Scripting\Core\` / `Scripting\VisualBasic\` 零改动），不预填修复 commit。
- 若用户选方向 A：按 `../tasks/script-mode-coverage-parity/README.md` **§八 义务 4** 登记变更面；须同步改写 U7 格 2 / 格 8（报警线已就位，会自行变红），并补一条「字符串重载设了编码时真的发出 PDB」的正向格。
- 若用户选方向 B：须决定是否把 `Script.ContinueWith(String)`（`:117`）与②的传参一并改回不传，或反过来把①补齐——「同产品内两套行为」不应保持现状。
