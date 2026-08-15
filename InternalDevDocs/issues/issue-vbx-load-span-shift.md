# [BUG] vbx `#Load` 文本内联导致后续 TextSpan 漂移

- **状态**：Fixed
- **发现**：2026-08-15
- **修复**：2026-08-15，分支 `with-modified-vbsyntax`（`#Load` 升为编译器指令 trivia + 多树提交；见文末「修复实现」）
- **严重度**：高（脚本诊断行号错误）
- **影响面**：vbx 脚本引擎——`vbi script.vbx` 执行路径与 Scripting API（`VisualBasicScriptCompiler`）
- **版本**：commit `e93a5ab38b04990daf38ddd61309e94278733e76`（`e93a5ab` "Consider LSP"，分支 `with-modified-vbsyntax`）

## 复现步骤

1. 创建 `loaded.vbx`（多行）：
   ```vb
   Dim a As Integer = 1
   Dim b As Integer = 2
   Function LoadedValue() As Integer
       Return 42
   End Function
   ```
2. 创建 `main.vbx`（`#Load` 在第 1 行，错误在第 3 行）：
   ```vb
   #Load "loaded.vbx"
   ? LoadedValue()
   Print(undefinedVar)
   ```
3. 运行 `vbi main.vbx`（或测试基座 `CreateRunner(args:={"main.vbx"})`，同 `Scripting\VisualBasicTest\CommandLineRunnerTests.vb` 的 `TestLoadDirectiveInScriptFile` 形状）。
4. 观察 `Print(undefinedVar)`（未声明，BC30451）的报告行号；再在 `loaded.vbx` 第 3 行放一个错误，观察它报告的文件与行号。

## 预期

与 C# `#load` 一致，零漂移：

- `Print(undefinedVar)` 的错误指向 **main.vbx 第 3 行**（真实位置）。
- `loaded.vbx` 内部的诊断指向 **loaded.vbx 的真实行号**（独立文件、独立行）。
- 主文件 `#Load` 之后的 span 一字不差。

## 实际

`ExpandLoadDirectives` 把 `#Load` 行替换为 loaded.vbx 的**内联文本**（5 行）后再解析：

- `Print(undefinedVar)` 报在 **main.vbx 第 7 行**（3 + 5 − 1 = 7），漂移 4 行。
- `loaded.vbx` 内部的诊断报在 main.vbx 的**内联偏移位置**——文件与行号都不对。
- 主文件 `#Load` 之后的所有 span 整体下移（N−1 行，N = 被加载文件行数）。

## 根因（源码核实）

fork 的 `#Load` 在**脚本层做纯文本内联改写**，不是编译器级处理：

- `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `ExpandLoadDirectives`（`:54-158`）：逐行扫描，命中 `#Load "file.vbx"` → 读文件 → 把内容**内联拼接**进源码 → 删除指令行；`CreateSubmission`（`:228`）用这份改写后的文本解析编译。
- 对比 C#：`#load` 是**编译器级指令 trivia**，由编译的 `SyntaxAndDeclarationManager` 把每个被加载文件解析成独立树，主树**不改写**（见下）。

## C# 参考实现（不漂移的正确做法，源码核实）

| 环节 | C# 位置 | 行为 |
|------|--------|------|
| 指令 | `LoadDirectiveTriviaSyntax`（编译器指令 trivia，script-only，错误码 `ERR_LoadDirectiveOnlyAllowedInScripts = 8097`） | `#load` 是语法树里的一条指令 trivia |
| 解析 | `SyntaxAndDeclarationManager.cs:282`（`new LoadDirective(resolvedFilePath, …)`，经 `SourceReferenceResolver` 解析路径）→ `:728` `TryGetLoadedSyntaxTree`（`loadedSyntaxTreeMap`） | 每个被加载文件解析成**独立 SyntaxTree**，FilePath = 真实路径；嵌套 `#load` 递归 `:445` |
| 合并 | `CSharpCompilation.cs:3135` `AppendLoadDirectiveDiagnostics`（遍历 `LoadDirectiveMap` 对加载树产诊断） | 加载树并入编译；主树 `#load` 行保留为 trivia，span 零漂移 |

**C# 的本质**：不是「把加载文件拼进主文件文本」，而是「主树 + N 棵独立加载树」的多树编译——诊断自然落在各自真实文件的真实位置。

## 修复方向（对齐 C#）

1. **`#Load` 升为编译器指令 trivia**：VB fork 已有 `#R` 先例（`Parser\ParseConditional.vb:82-83, 448-464` 的 `ParseReferenceDirective` + `IsScript` 门控 + `ReferenceDirectiveTriviaSyntax`）。
2. **脚本层从文本内联改为加载独立树**：`VisualBasicScriptCompiler.CreateSubmission` 对每个被加载文件解析为独立 `SyntaxTree`（`FilePath` = 真实路径），递归处理嵌套 `#Load`。
3. **多树编译入口**：基线 `VisualBasicCompilation.CreateScriptCompilation`（`Compilation\VisualBasicCompilation.vb:345`）把单树转发给 `Create(..., IEnumerable(Of SyntaxTree), ...)`（`:368`）——加多树重载即可，与 C# `CSharpCompilation.CreateScriptCompilation`（`:459`）同构。
4. **验证点**：
   - VB 脚本 binder 是否像 C# 一样能跨多棵树正确合并顶层声明；
   - 文件缺失时诊断落在主文件的 `#Load` 行；
   - `#Load` 循环引用检测（现有 `activeLoads` 去重逻辑保留）。

## 相关

- `../proposals/proposal-shebang-directive.md` / `../meetings/meeting-shebang-directive.md`：`#!` 是编译器 trivia（零漂移）、`#Load` 是宿主文本内联（有漂移）——本 bug 正是这组关键区别暴露的问题。指令三分表：`#R`（编译器）、`#Load`（宿主，应改编译器）、`#!`（编译器）。
- `../proposals/proposal-vbscript-lsp.md`：LSP 用「同一脚本 Project 多 Document」模型绕开编译器改写、保住 span；但 `vbi` 执行路径仍走 `ExpandLoadDirectives` 文本内联、照样漂移。

## 修复实现（2026-08-15）

`#Load` 升为编译器指令 trivia，脚本层改为多树加载，与 C# `#load` 对齐：

1. **编译器 `#Load` 指令**（镜像 `#R`）：
   - `Syntax\Syntax.xml`：新增 `LoadDirectiveTriviaSyntax` + `LoadKeyword` token；用基线 `VBSyntaxGenerator` 重新生成三个 `Generated\Syntax.xml.*.Generated.vb`（零删除、纯新增）。
   - `Syntax\SyntaxKind.vb`：`LoadDirectiveTrivia = 751`、`LoadKeyword = 793`（追加，不位移既有值）。
   - `Parser\ParseConditional.vb`：`Case SyntaxKind.LoadKeyword` 派发 + `ParseLoadDirective`（`IsScript` 门控 → `ERR_LoadDirectiveOnlyAllowedInScripts = 36967`）。
   - `Scanner\KeywordTable.vb`、`SyntaxKindFacts.vb`、`SyntaxNodeFactories.vb`、`SyntaxToken.vb`、`SyntaxFacts.vb`、`SyntaxFactory.vb`、`SyntaxNormalizer.vb`、`SyntaxNodeExtensions.vb`、`Parser.vb`、`Scanner\Directives.vb`：注册 `LoadKeyword` / `LoadDirectiveTrivia`。
   - `Errors\Errors.vb` / `ErrorFacts.vb` / `VBResources.resx`：新错误码与消息。
   - `Syntax\CompilationUnitSyntax.vb`：`GetLoadDirectives()`。
2. **提交类多树支持**：
   - `Compilation\VisualBasicCompilation.vb`：新增多树 `CreateScriptCompilation` 重载；`AddSyntaxTrees` 放开提交「单树」限制（改守「仅脚本树」）；`HasSubmissionResult` 用 `SyntaxTrees.Last()`（主文件）。
   - `Symbols\Source\SourceMemberContainerTypeSymbol.vb`：提交构造函数 / 脚本初始化器的 `SyntaxReferences.Single()` → `First()`。
   - `Binding\TopLevelCodeBinder.vb` + 三处调用（`BinderFactory.vb`、`BinderBuilder.vb`、`Binder_Initializers.vb`）：binder 根改为「所在树」的编译单元，支持跨树语义模型。
3. **脚本层多树加载**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 删除 `ExpandLoadDirectives` 文本内联，`CreateSubmission` 改为：解析主树 → 收集 `#Load` → 每文件独立 `SyntaxTree`（`FilePath` 真实路径、递归嵌套、`activeLoads` 环检测、缺失文件诊断锚定 `#Load` 行）→ 多树进 `CreateScriptCompilation`（加载树在前、主文件在后，执行序与旧内联一致）。
4. **验证**：
   - `Scripting\VisualBasicTest\ScriptTests.vb` 新增 6 个无副作用测试（内存 `SourceReferenceResolver`）：span 不漂移、加载文件内诊断指真实文件/行、缺失文件在 `#Load` 行、嵌套、环、跨树引用。
   - `ScriptTests` 45 通过、`CommandLineRunnerTests` 61 通过、语义 Submission/Script 32 通过、语法 Directive 237 通过。
   - `vbi` 实测：`main.vbx` 第 2 行 `Print(undefinedVar)` 报 BC30451 于 main.vbx:2（非旧行为 2+5−1=6）；加载文件内部错误报真实文件真实行。
   - `ScriptSemanticsTests.Errors_02` 更新：多脚本树合并不再抛 `InvalidOperationException`（旧 `SyntaxReferences.Single()` 限制），符号正常解析。
