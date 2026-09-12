# [BUG] vbi `/imports:<非法值>` 抛 NullReferenceException，`/imports:` 诊断丢失

- **状态**：Fixed（2026-09-10，方向 A，见文末「修复实现」）
- **发现**：2026-09-09（`tasks\imports-accumulation-diagnostics` 的 F1 实证中附带发现）
- **严重度**：中（用户看到 NRE 栈而非 `/imports:` 诊断；进程不死、退出码 1，REPL 与脚本均不可用该开关）
- **影响面**：vbi 的**脚本/REPL 宿主路径**——`vbi script.vbx`、`vbi /i`、`vbi`（无参进 REPL），经 `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`。vbi 编译模式（`vbi foo.vb /out:...`）**不受影响**：`Compilers\Core\Portable\CommandLine\CommonCompiler.cs:897` 在编译前先报 `Arguments.Errors` 并返回 `Failed`。
- **版本**：分支 `with-modified-vbsyntax` 工作树（测试文件 `Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb` 当前未跟踪）

## 复现步骤

1. 运行 `vbi /imports:Foo.`（`Foo.` 是语法非法的导入名）。同族坏值：`/imports:123`、`/imports:global`、`/imports:Foo =`、`/imports:<xmlns = "http://xml">`。
   - 开关值按 `;` / `,` 切分（`Compilers\Core\Portable\CommandLine\CommandLineParser.cs:1058` 的 `s_pathSeparators`，切分见 `:1061-1066` `ParseSeparatedPaths`），单值 `Foo.` 直接进 `GlobalImport.Parse`。
2. 观察 stderr 与退出码。
3. 对照：`vbi /imports:System.Text` 正常；`vbi foo.vb /out:foo.dll /imports:Foo.`（编译模式）不抛。

等价的内存复现（无副作用，已被测试钉住）：`ImportsAccumulationFailureTests.vb:319` `ReplHost_BadImportsSwitch_FailsWithNullReferenceExceptionBeforeTheFailedGate`（`CreateRunner({"/imports:Foo.", "/R:System"}, "? 1 + 2")` → `Assert.Throws(Of NullReferenceException)`）。

## 预期

- stderr 出现 `/imports:` 的编译诊断，消息形状 `Error in project-level import 'Foo.' at 'Foo.' : <原始语法错误消息>`（`Compilers\VisualBasic\Portable\VBResources.resx:1636` 的 `ERR_GeneralProjectImportsError3` + `Compilers\VisualBasic\Portable\GlobalImport.ImportDiagnosticInfo.vb:21-24` 渲染）；诊断 Id 继承被包装的原始语法错误 BC 码（`GlobalImport.ImportDiagnosticInfo.vb:30`）。
- 该诊断**解析期就已产出**并落在 `CommandLineArguments.Errors` 里（`GlobalImport.Parse` 非抛重载回吐诊断 → `ParseGlobalImports` 收进 `errors`），宿主只需在 `CommandLineRunner.cs:142-146` 的 Errors 门报出并返回 `CommonCompiler.Failed`（=1，`CommonCompiler.cs:67`）。
- 不抛 NRE、不打印异常栈。

## 实际

- `CommandLineRunner.cs:140` 的 `GetScriptOptions` 在 Errors 门之前执行 → `VisualBasicCompilationOptions.GetImports()` 对 `Nothing` 取属性 → `NullReferenceException`。
- 异常穿过宿主后被 `Interactive\vbi\Vbi.vb:68-72` 顶层 catch 兜住：`:69` `Console.Error.WriteLine(ex.ToString())` 打印完整栈，`:70-71` `PromptScriptError()` + 返回 1。stderr 未重定向时 `Vbi.vb:87` 写横幅 `The script has error. See the output for more information.`；真实控制台下改为 `Vbi.vb:90` 弹 MsgBox。
- 用户看不到 `/imports:` 诊断（诊断在 `Arguments.Errors` 里，但宿主在报告它之前就崩了）。

## 根因（源码核实）

四条锚点逐条复核（证据等级：**已检查**，2026-09-09 逐条 Read 本工作树）：

1. **坏子句解析成 `Nothing`**：`Compilers\VisualBasic\Portable\GlobalImport.vb:68-70` 的 `Parse(String, ByRef diagnostics)` 实现是 `Return Parse({importedNames}, diagnostics)(0)`（`:69`）。重载解析命中 `:103-108` 的 `Parse(IEnumerable(Of String), ByRef)`（非抛重载）。坏子句在 `Compilers\VisualBasic\Portable\OptionsValidator.vb:55-57` 被过滤——只有无语法错误的子句才进 `parsedImportList`（`:48` 构造 `GlobalImport`、`:53` 加诊断）——因此返回序列为空。VB 对非数组集合的整数索引绑定到 `ElementAtOrDefault`（`Compilers\VisualBasic\Portable\Binding\Binder_Invocation.vb:536` 注释 + `Compilers\VisualBasic\Portable\StringConstants.vb:24` `ElementAtMethod = "ElementAtOrDefault"`），空序列取默认值 → **返回 `Nothing`，不抛**。
2. **`Nothing` 被塞进 `GlobalImports`**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:1865-1874` `ParseGlobalImports`——`:1870` 拿到 `Nothing`，`:1871` `errors.AddRange(importDiagnostics)`（诊断确实进了 `Arguments.Errors`），`:1872` 无条件 `globalImports.Add(import)`。该 list 直接进 `VisualBasicCompilationOptions(globalImports:=globalImports, ...)`（`:1506`）。
3. **宿主在 Errors 门之前碰它**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:140` `GetScriptOptions(...)` 先于 `:142-146` 的 `Arguments.Errors` 门执行；`:200` `namespaces: CommandLineHelpers.GetImports(arguments)` → `Scripting\Core\Hosting\CommandLine\CommandLineHelpers.cs:17` `args.CompilationOptions.GetImports()`。
4. **NRE 落点**：`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb:343-352` 的 `GetImports()`，`:347` `If Not globalImport.IsXmlClause Then` 对 `Nothing` 取 `IsXmlClause`（内部访问 `_clause.GetSyntax()`）→ NRE。

**附：`Nothing` 毒丸的其它爆炸点**（同一 `GlobalImports` 集合，均取 `globalImport.Name` / `Clause`；证据等级：**已检查**）——说明「塞进 Nothing」本身是根因，而不只是 `GetImports` 一处：

- `Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:793` `Options.GlobalImports.Select(Function(x) x.Name)`（deterministic key）
- `Compilers\VisualBasic\Portable\Compilation\VisualBasicDeterministicKeyBuilder.vb:83-84` `import.Name` / `import.IsXmlClause`
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceModuleSymbol.vb:336`、`:381`/`:387`（`globalImport.Clause`）
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceNamedTypeSymbol.vb:1935`（`globalImport.Clause.Kind`）
- `Compilers\VisualBasic\Portable\SourceGeneration\VisualBasicSyntaxHelper.vb:132`（`globalImport.Clause`）

vbc 与 vbi 编译模式之所以不炸，只是因为错误场景下编译前就返回 `Failed`（`CommonCompiler.cs:897`）；`CommandLineArguments.CompilationOptions` 的库消费者拿这套 options 直接编译仍会踩上面任一访问点。

## 修复方向（候选，未拍板）

### A. 源头不塞 `Nothing`（编译器侧，共享文件）

- **落点**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:1865-1874`。
- **改动形状**：`:1872` 加门——`If import IsNot Nothing Then globalImports.Add(import)`。诊断仍在 `:1871` 进 `errors`，`Arguments.Errors` 不变。
- **影响**：vbc/vbi 编译模式的用户可见输出不变（错误仍由 `Arguments.Errors` 报出、编译前返回 `Failed`）；`GlobalImports` 不再带毒丸 → 上文六个访问点全部消失。需评估：是否有消费方依赖「坏 `/imports:` 也占 `GlobalImports` 一格」——全仓未见此类断言（`Compilers\VisualBasicCommandLineTest\CommandLineTests.vb:7243`、`:7300` 只数合法项，已检查）。证据等级：**推测**（未实施）。
- **上游合并代价**：`VisualBasicCommandLineParser.vb` 已在 `upstream-merge.md` §2.1 登记（`:61-63`、`:1522-1523`），但 `ParseGlobalImports` 区域未登记 → 需新增账本条目 + 3-way 评审义务；上游同码（该文件不在修改面内），属 fork 本地 diverge，可评估 PR 化。
- **测试**：翻转 `ImportsAccumulationFailureTests.vb:187` 与 `:319`；新增「坏 `/imports:` 后 `GlobalImports` 无 `Nothing`、`GetImports()` 返回空不抛」「`vbi /imports:Foo.` → stderr 含 project-level import 诊断、返回 `Failed`、无 NRE」「编译模式不回归」。

### B. `GetImports()` 防御性跳过 `Nothing`（编译器侧，最小）

- **落点**：`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb:343-352`。
- **改动形状**：`:347` 改 `If globalImport IsNot Nothing AndAlso Not globalImport.IsXmlClause Then`。
- **影响**：只堵宿主这一个出口；`GlobalImports` 里的 `Nothing` 仍在，其余五个访问点在「错误场景下仍被编译」时照旧 NRE（当前 vbc 路径不可达、库消费者可达）。改动面最小。
- **上游合并代价**：`VisualBasicCompilationOptions.vb` 未登记修改面 → 需新增账本条目 + 3-way 评审；`GetImports()` 的 `TODO: implement (only called from VBI)` 注释为上游文本（已检查）。
- **测试**：`:187` 的 `Assert.Throws(Of NullReferenceException)` 必须翻转（`Assert.Contains(..., g Is Nothing)` 仍成立）；`:319` 翻转。

### C. 宿主先过 Errors 门（宿主侧）

- **落点**：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:140` 与 `:142-146`。
- **改动形状**：把 `GetScriptOptions` 挪到 Errors 门之后。
- **影响（需评估）**：`GetScriptOptions` 会向 `diagnosticsInfos` 追加引用解析诊断（`:191` `ResolveMetadataReferences(metadataResolver, diagnostics, messageProvider, resolvedReferences)`；`Compilers\Core\Portable\CommandLine\CommandLineArguments.cs:416-439` 对每条未解析引用加 `DiagnosticInfo`）。直接后挪会让这些诊断失去报出机会——「`/imports:` 坏值 + `/r:` 坏引用」同现时现状两条都报，后挪后只剩一条；且 `GetScriptOptions` 返回 `null`（引用解析失败）时需要一道门兜住。保现状的做法是「引用解析留在门前 + ScriptOptions 构造（含 `GetImports`）移到门后」（即 D1），或在门后补一次 `ReportDiagnostics`（`CommonCompiler.cs:528` 的 `_reportedDiagnostics` 跨调用去重，重复项不会二次打印，可行但多一次调用）。仅治宿主症状，毒丸与其余访问点不变。
- **上游合并代价**：`CommandLineRunner.cs` 已在账本 §2.10/2.12/2.14/2.16 多次登记，新增改动需在账本追加说明 + 3-way 评审；`Scripting\Core` 是 fork 共享层（§2.7 已列为 fork 新增），零 public API 面变化。
- **测试**：`:319` 翻转（改为断言不抛 + stderr 含诊断 + 返回 `CommonCompiler.Failed`）；`:187` 保持原样。

### D. 其它候选

- **D1「懒构造」**：把 `GetScriptOptions` 拆成「门前解析引用（保留引用诊断）」与「门后构造 `ScriptOptions`（含 `GetImports`）」两段私有 helper。等价于 C 的无损版，落点同 `CommandLineRunner.cs:140`。
- **D2「宿主自算导入名」**：`Scripting\Core\Hosting\CommandLine\CommandLineHelpers.cs:15-18` 改为从 `arguments` 侧过滤 `Nothing` 后取 `Name`，不再调 `VisualBasicCompilationOptions.GetImports()`。零编译器改动 → 上游合并零代价；代价是 XML 过滤规则（`VisualBasicCompilationOptions.vb:347`）在宿主复制一份，存在漂移风险，且需先确认 `CommandLineArguments` 侧对 `Imports` 的可达性。
- **D3「改 `GlobalImport.Parse(String, ByRef)` 契约」**（`GlobalImport.vb:68-70`）：不可行——坏输入没有合法返回值可表达；改成抛异常只是把「宿主症状」换成「宿主崩溃」（宿主未捕获）。**不推荐**。
- **D4「解析期补诊断」**：在 `ParseGlobalImports` 内对 `import Is Nothing` 再补一条 `ERR_InvalidSwitchValue`——与原语法诊断重复，收益低。

## 相关

- `Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb`——本缺陷的实证与钉子（19 用例）。
- `InternalDevDocs\tasks\imports-accumulation-diagnostics\README.md`——F1「`Imports` 跨提交累积降级为诊断」已裁决关闭（零代码改动）；本缺陷是该实证过程中发现的**可达**缺陷，另立本 issue。
- `InternalDevDocs\upstream-merge.md`——方案 A/B 触及未登记的共享编译器文件，修复时需追加修改面条目与合并前评估义务。
- `InternalDevDocs\issues\issue-vbx-load-span-shift.md`——同一宿主路径（`Scripting\VisualBasic`）的前例，可作为修复落点风格参照。

## 测试影响清单（修复时核对）

| 用例（`ImportsAccumulationFailureTests.vb`） | 行 | 当前断言 | A | B | C | D1 | D2 |
|---|---|---|---|---|---|---|---|
| `CommandLineImportsSwitch_BadValue_LeavesNothingInGlobalImports` | `:187-197` | `GlobalImports` 含 `Nothing` + `GetImports()` 抛 NRE | 翻转 | 部分翻转（`Throws` 改不抛） | 保持 | 翻转 | 部分翻转 |
| `ReplHost_BadImportsSwitch_FailsWithNullReferenceExceptionBeforeTheFailedGate` | `:319-329` | `RunInteractive()` 抛 NRE | 翻转 | 翻转 | 翻转 | 翻转 | 翻转 |
| `GlobalImportParse_StringWithDiagnosticsOverload_ReturnsNothingOnError` | `:175-184` | `Parse(String, ByRef)` 返回 `Nothing` | 保持 | 保持 | 保持 | 保持 | 保持 |
| `CommandLineImportsSwitch_XmlClause_IsFilteredOutBeforeScriptOptionsImports` | `:200-208` | XML 子句被过滤、零错误 | 保持 | 保持 | 保持 | 保持 | 保持 |

> **编号核对**：任务转述的「A-6 / A-7 / C-3 钉缺陷行为」与文件实际编号不完全吻合。按 region 内序号，A-6 = `:187`（确为缺陷断言），A-7 = `:200`（断言的是**正确**的 XML 过滤行为，A/B/C 下均无需翻转）；按全文序号，A-6 = `:165`（断言错误落在 `Arguments.Errors`，属期望行为），A-7 = `:175`（钉住 `Parse(String, ByRef)` 返回 `Nothing` 的机制，仅 D3 类改法会触及）。C-3 = `:319`，与转述一致。上表按用例名逐条给出，不受编号歧义影响。

## 修复实现（2026-09-10）

采用**方向 A：源头不塞 `Nothing`**。

1. **改动**：`Compilers\VisualBasic\Portable\CommandLine\VisualBasicCommandLineParser.vb:1872-1876`（`ParseGlobalImports`）—— `globalImports.Add(import)` 加 `If import IsNot Nothing` 守卫；`:1871` 的诊断仍无条件进 `errors`。纯加性：合法子句路径逐字节不变，坏子句不再把 `Nothing` 放进 `GlobalImports`。
2. **毒丸访问点一次覆盖**：全仓命令行李解析里 `GlobalImport.Parse` 只有 `:1870` 一处调用（其余命中为测试与公共 API），命令行产生 `GlobalImports` 的唯一写入口就是 `ParseGlobalImports` → 六个消费点（`VisualBasicCompilationOptions.GetImports` :347、`VisualBasicCompilation` :793、`VisualBasicDeterministicKeyBuilder` :83-84、`SourceModuleSymbol` :336/:381、`SourceNamedTypeSymbol` :1935、`VisualBasicSyntaxHelper` :132）的 `Nothing` 毒丸一并消失，未逐个打补丁。公共 API `WithGlobalImports` 仍可传 `Nothing`（库调用方责任，不在本缺陷范围）。
3. **测试**（`Scripting\VisualBasicTest\ImportsAccumulationFailureTests.vb`）：
   - 翻转 `CommandLineImportsSwitch_BadValue_LeavesNothingInGlobalImports` → `CommandLineImportsSwitch_BadValue_IsReportedAndLeavesGlobalImportsClean`：断言 `Arguments.Errors` 有 Error 且消息含 `Foo.`、`GlobalImports` 无 `Nothing`、`GetImports()` 返回空且不抛。
   - 翻转 `ReplHost_BadImportsSwitch_FailsWithNullReferenceExceptionBeforeTheFailedGate` → `ReplHost_BadImportsSwitch_IsReportedAndFailsAtTheErrorsGate`：端到端断言 `RunInteractive()` 返回 1（`CommonCompiler.Failed`）、`Console.Error` 含 `Foo.`、REPL 未启动——即「宿主返回 `Failed` 且诊断可见」的端到端覆盖。
   - 新增 `CommandLineImportsSwitch_MixedGoodAndBadValues_KeepsOnlyTheGoodClause`：同一开关值 `/imports:Foo.,System.Text` 下坏子句只被丢弃、合法兄弟子句仍入 `GlobalImports`（守卫生效边界）。
   - 保持不动：`GlobalImportParse_StringWithDiagnosticsOverload_ReturnsNothingOnError`（钉 `Parse(String, ByRef)` 机制）、`CommandLineImportsSwitch_XmlClause_IsFilteredOutBeforeScriptOptionsImports`（断言的是正确行为）。
   - 类级 XML 注释与 `CommandLineImportsSwitch_BadValue_IsReportedAsArgumentsErrors` 注释里描述旧行为的句子同步更新。
4. **验证**（真跑）：
   - `Scripting\VisualBasicTest` 全量程序集直跑（`-automated`，MTP 不用 `dotnet test`）：**319/319 通过，0 失败 / 0 跳过**；本类 20/20。
   - 编译器七门 gate 全绿，与 `scripts\verify-vb-compiler-tests.ps1` 基线逐项一致：Phase2 143/143/0/0、Syntax 4070/4067/3/0、Symbol 3400/3376/24/0、Semantic 5784/5680/104/0、IOperation 1574/1566/8/0、Emit 4330/4227/103/0、CommandLine 475/468/7/0。
5. **上游合并**：`InternalDevDocs\upstream-merge.md` 新增 §2.18 条目（纯加性守卫；无坏值时行为不变；合并前对 `ParseGlobalImports` 做 3-way 评审）。
