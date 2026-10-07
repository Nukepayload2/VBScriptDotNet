# 测试计划：脚本模式 `End` 语句解禁（script-mode-end-statement）

> 判据与锚点在 `README.md`，本文件只列格子。断言一律**断具体诊断码 / 具体 IL 文本 / 确切节点种类**，不得用"编译失败"或"通过"当唯一断言。
> **先自 dump、再定桩**：凡是条数、位置、IL 文本、脚本类方法名这类可从运行结果读出的值，一律先跑一次取读数再写断言，不许按推演预设（本仓既有教训）。

## 〇、危险清单（硬性，违反即事故）

| # | 禁止 | 理由 |
|---|---|---|
| D-1 | **不得在单元测试里运行 `End`**（`script.RunAsync()` / `CommandLineRunner.RunInteractive()` 提交含 `End` 的代码） | 修复后 `End` 下降为 `ProjectData.EndApp()`（`LocalRewriter.vb:865-879`）＝立即结束进程 ⇒ **整个测试进程（整门）会被杀掉**，症状看起来像"门挂了"而不是"某格红"。`Environment.Exit(0)` 同理。档 1 只允许 **编译 + `Emit(New MemoryStream())`**，即 `ScriptModeConformance.AssertEmits`（`ScriptModeConformanceTests.vb:96-114`）那一形态 |
| D-2 | 不得为让格子变绿而放宽/删除 `BC30615` 的类库钉桩 | 那是该码的正当用途（`Binder_Statements_Tests.vb:3616-3623` / `:3766`、`BindingErrorTests.vb:9680`） |
| D-3 | 不得新增 REPL 会话格 | `AssertReplSession`（`:194-209`）会真的执行提交 ⇒ 撞 D-1。REPL 面由档 2（ES-H5）取证 |

## 一、ES-A 正格（新增；档 1 · 已运行）

### ES-A（L1 解析层）→ 新文件 `Compilers\VisualBasicSyntaxTest\Parser\ScriptTopLevelEndStatementTests.vb`

形状照 `Parser\ScriptTopLevelAfterAutoPropertyTests.vb`（同类最近邻，`:19-27` 的 `Script` parse options 与 `ParseAndVerify(source, Script)` / `Parse(source, options:=Script)` 两个入口直接照搬）：

```vb
Private Shared ReadOnly Script As New VisualBasicParseOptions(kind:=SourceCodeKind.Script,
                                                              languageVersion:=LanguageVersion.Latest)
```

| 格 | 源串（`vbCrLf` 连接） | 期望 |
|---|---|---|
| ES-A1 | `System.Console.WriteLine("A1")` / `End` / `System.Console.WriteLine("A2")` | `ParseAndVerify(source, Script)`（**零诊断**）；`End` 那一项的确是 **`StopOrEndStatementSyntax`** 且**不是** `EndBlockStatementSyntax`。**种类钉法已按实测改正（原稿笔误）**：`SyntaxKind.StopOrEndStatement` **不是**节点种类——停止/结束语句的节点种类是 `SyntaxKind.StopStatement`=153 与 `SyntaxKind.EndStatement`=156（`SyntaxKind.vb:574/579`；语法工厂 `SyntaxFactory.StopOrEndStatement` 在 `ParseStatement.vb:1821`）⇒ 断言写 `Kind() = SyntaxKind.EndStatement` + `IsType(Of StopOrEndStatementSyntax)` + `IsNotType(Of EndBlockStatementSyntax)` |
| ES-A2 | `End : System.Console.WriteLine("A2")`（同行冒号） | 零诊断；`End` 仍解析成 `StopOrEndStatementSyntax`（走 `ParseEndStatement` 的 `CanFollowStatementButIsNotSelectFollowingExpression(Colon)` 支） |
| ES-A3 | `End ' trailing comment` | 零诊断 |
| ES-A4 | `End` 是编译单元**最后一项**（无后继语句） | 零诊断 |
| ES-A5 | 顶层 `If True Then` / `    End` / `End If` | 零诊断（块内的 `End` 走方法体分发器，**本任务不改它**；本格是"两道门都开着"的解析侧对照）。**结构钉法已按实测改正**：编译单元**只有 1 个成员**（那个 `MultiLineIfBlock`，`End If` 是它的 `EndIfStatement`），不是 2 个 ⇒ 断 `members.Count = 1` + `EndIfStatement` 的 `Kind()`/`IsType(EndBlockStatementSyntax)`（不得因为猜错成员数就把断言放宽成"只断零诊断"） |

### ES-A（L2 语义层）→ 新文件 `Compilers\VisualBasicSemanticTest\Semantics\ScriptEndStatementTests.vb`

夹具＝`BasicTestBase.CreateSubmission`（`Compilers\Test\Utilities\VisualBasic\BasicTestBase.vb:437-452`：内部走 `CreateScriptCompilation` ⇒ `IsSubmission:=True`，默认引用含 `MsvbRef_v4_0_30319_17929`，`parseOptions` 默认 `TestOptions.Script`），选项用 `ScriptSemanticsTests.vb:168-171` 的既有惯用法：

```vb
Private Shared Function ScriptCompilationOptions() As VisualBasicCompilationOptions
    Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
        WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
End Function
```

| 格 | 源串 | 期望 |
|---|---|---|
| ES-A6 | `System.Console.WriteLine("A6")` / `End` | **零诊断**（`CreateSubmission(source, options:=ScriptCompilationOptions()).VerifyDiagnostics()`）；这一格同时打**门 1 + 门 2** |
| ES-A7 | `Sub Finish()` / `    System.Console.WriteLine("A7")` / `    End` / `End Sub` / `Finish()` | 零诊断（门 2；方法体内） |
| ES-A8 | `Dim act As System.Action = Sub()` / `    End` / `End Sub` | 零诊断（门 2；lambda 体内） |
| ES-A9 | 两次提交链：sub1 = `Dim x = 1`，sub2 = `End`（`previous:=sub1`） | sub2 零诊断（提交链上的第二个提交同样受 `IsSubmission` 覆盖） |
| ES-A10 | `Sub Finish()` / `    End` / `End Sub`，**脚本树 + 非提交编译**（`VisualBasicCompilation.Create(GetUniqueName(), {Parse(source, options:=TestOptions.Script)}, {MsvbRef…}, TestOptions.ReleaseDll)`） | **仍报 `BC30615`**（这是"`IsSubmission` 而非 `IsScriptClass`"的直接锁：非提交的脚本树 + DLL 输出仍按类库处理。若这一格变绿，说明判据被放宽到了 `DeclarationKind.Script`，**打回**） |

### ES-A（L3 发射层）→ `Compilers\VisualBasicEmitTest\CodeGen\CodeGenStopOrEnd.vb` 新增一格

| 格 | 形状 | 期望 |
|---|---|---|
| ES-A11 | 提交里 `Sub Finish()` / `    End` / `End Sub`，`CompileAndVerify(compilation, verify:=Verification.FailsPEVerify)`（提交的 PEVerify 形态照 `Emit\SubmissionSharedHandlesHookupTests.vb:139`），对 `Finish` 取 IL | IL 里**确有** `call "Sub Microsoft.VisualBasic.CompilerServices.ProjectData.EndApp()"`（正本锚＝同文件 `:164-174` 的普通编译格 `EndStatement_SimpleTestWithEnd`）。**方法名/类名先自 dump IL 再定桩**（脚本类名由 `VisualBasicCompilation.CreateScriptCompilation` 生成，不许按印象写） |

**ES-A11 是唯一能证明"`End` 真的下降成 `EndApp` 调用"的档 1 格子**：零诊断格证明不了它——`LocalRewriter.VisitEndStatement`（`:865-873`）在 `WellKnownMember` 取不到时**静默**保留 `BoundEndStatement`，零诊断照样成立。

### ES-A（L4 宿主层）→ 改写 `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb`（见 ES-D）

## 二、ES-B 反例锁（非脚本面免疫；D10 的验证义务；档 1）

| 格 | 形状 | 期望 |
|---|---|---|
| ES-B1 | **Regular** 编译、编译单元顶层裸 `End`（`Parse(source, options:=TestOptions.Regular)`） | **`BC30678`**（`ParseGroupEndStatement` 的 `:1765-1769` 支），与改前逐字相同。仓内**现无**这一格（现有 BC30678 钉都是 `End If`/`End C`/`End Let` 一类**块结束**形状）⇒ 本格是新增的"门 1 没漏到普通编译"锁 |
| ES-B2 | `TestOptions.ReleaseDll` 普通编译里的 `End`（方法体内） | `BC30615` —— **既有钉，保持不动**：`Binder_Statements_Tests.StopAndEnd_1`（`:3616-3623`，把 `ReleaseExe` 改 DLL 后断 BC30615）、`Binder_Statements_Tests.EndWithSingleLineIfWithDll`（`:3766`）、`BindingErrorTests.BC30615ERR_EndDisallowedInDllProjects`（`:9680`）。**不新建、不修改**，只在验证报告里点名"这三处仍绿" |
| ES-B3 | Regular 普通编译里 `End` 的**发射** | `CodeGenStopOrEnd.EndStatement_SimpleTestWithEnd`（`:146-175`）仍绿（`EndApp` 调用不变） |
| ES-B4 | 脚本顶层**块结束语句**的诊断不变：`If True Then` / `End If` / `End Sub`（裸的）各一格 | 先自 dump 实际诊断集合（`Parse(source, options:=Script).GetDiagnostics()`）**再定桩**，硬要求只有一条：**改后与改前逐字相同**（这两条路径都经 `ParseEndStatement` → `ParseGroupEndStatement`（`:1748`）→ `DeclarationContext.RecoverFromMismatchedEnd` 的表 `:367-456`，本任务不碰）。取不到"改前"读数时，用"同一形状在 Regular 下的读数"作结构对照，并在流水账写明 |
| ES-B5 | 脚本顶层 `End` **不再**报 `BC30678`，且**不得**改报别的码 | ES-A1/A6 已覆盖零诊断；本格额外断 `Assert.DoesNotContain("BC30678")` 与 `DoesNotContain("BC30615")`（防"换码式修复"） |

## 三、ES-C 回收义务（既有负向钉 → 按新判据改写；档 1）

| 格 | 锚点 | 改法 |
|---|---|---|
| ES-C1 | `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:392-397` `TopLevelEndStatement_IsReported`（断 `BC30678`） | 改名为正向格（如 `TopLevelEndStatement_EmitsWithoutRunning`，与邻居 `:415-419` 的 `TopLevelStopStatement_EmitsWithoutRunning` 同命名式），体改用 `ScriptModeConformance.AssertEmits(...)`；`:387-391` 的 XML doc 全文重写（现文写「End cannot be produced by a script at all…」） |
| ES-C2 | 同文件 `:399-407` `EndInsideMethod_IsReported`（断 `BC30615`） | 同上改 `AssertEmits`（源码形状保留 `Sub Finish()` 体内 `End`），doc 说明"方法体内的 `End` 也已合法" |
| ES-C3 | 同 region 的 `:409-419` `TopLevelStopStatement_EmitsWithoutRunning` | **不动**（哨兵：证明改写没有波及邻居） |
| ES-C4 | `..\script-mode-coverage-parity\test-plan.md:583` 的 `End` 语句行 + `:675`（R9 行）+ `:860`（R9 段）+ `:1094`（计行裁定段） | 两栏由「负向，裁定 5 算覆盖」改成正向格指针（新方法名/行号）；"**可见性边界**：`End` **不会**产生可运行产物"这句**必须删掉并改写**（新行为：产生可运行产物，但进程级可观测面归档 2）；顺手改正行内已腐的源码锚（`Parser.vb:996`→声明位 `:829-830`／方法体位 `:1005-1006`，`Parser.vb:1710`→`:1719`）。`:584` 的 `Stop` 行不动 |
| ES-C5 | 全仓普查（F01 产出） | 除 ES-C1/C2 外**不应**再有脚本模式的负向钉；若普查出第三处，逐条列 `文件:行` 并按新判据改写（prose 钉也算） |

## 四、ES-D 文档产出（F05 由 main 落笔，不是测试格）

- `spec\spec-scripting-dialect.md`：新增一节（建议紧邻 `### Top-level `AddHandler` and `RemoveHandler``，`:189-198`），内容＝「`End` 在脚本/提交里合法，语义＝立即以退出码 0 结束进程；两道门各是什么；哪些诊断仍在（类库项目、块结束不匹配）」。**先英文正本、后 `spec\zh-CN\spec-scripting-dialect.md` 译文**。
- `..\..\upstream-merge.md`：登记 `Parser.vb`／`Binder_Statements.vb` 两个上游同名文件的改动点，并写"合并上游时必须保住的两个脚本分支"（D10 的 3-way 注意点）。
- `..\..\issues\README.md` 第 39 行状态；issue 正文补「修复后的新增行为变化」（README §六 三条）。

## 五、ES-H 宿主档 2（main 跑，F04）

重建 Debug 宿主（`dotnet build Interactive\vbi\vbi.vbproj --framework net10.0`，或经 `Scripting\VisualBasicTest` 项目引用连带产出）后逐格对账。探针在 `..\..\..\tmp\probe-end-stmt\`（**子 agent 不得改探针**，只读）。

| 格 | 探针 | 改前读数（已运行） | 期望改后 |
|---|---|---|---|
| ES-H1 | `a-end-top.vbx` | `BC30678`、`EXITCODE=1`、无 `A1` | 打印 `A1`、`EXITCODE=0`、**无 `A2`** |
| ES-H2 | `d-end-in-if.vbx` | `BC30615`、`EXITCODE=1` | 打印 `D1`、`EXITCODE=0`、**无 `D2`** |
| ES-H3 | `c2-end-sub.vbx` | `BC30615`、`EXITCODE=1` | 打印 `C-in`、`C1`、`EXITCODE=0`、**无 `C2`** |
| ES-H4 | `e-end-in-lambda.vbx` | `BC30615`、`EXITCODE=1` | 打印 `E1`、`EXITCODE=0`、**无 `E2`** |
| ES-H5 | REPL：把 `End` 作为一条提交喂给 `vbi`（`End` 后再喂一条无害提交） | `(1) : error BC30678` | 提交零诊断；`End` 处进程以 `EXITCODE=0` 结束；其后提交不执行 |
| ES-H6 | 哨兵 `b-exit-top.vbx`（`Environment.Exit(0)`） | `B1`、exit `0`、无 `B2` | **逐字不变** |
| ES-H7 | 哨兵 `x-exit.vbx` | `X1`、exit `0`、无 `X2` | **逐字不变** |
| ES-H8 | 哨兵 `normal.vb /target:exe` | `EXITCODE=0`、零诊断 | **逐字不变** |
| ES-H9 | 哨兵 `normal.vb /target:library` | `EXITCODE=1`、`BC30615` | **逐字不变** |
| ES-H10 | `vbi /check a-end-top.vbx` | `BC30678` | 零诊断、exit `0` |

宿主读数三坑：`MSYS_NO_PATHCONV=1`、诊断正文按 **CP936** 解码、`.vbx` 必须成文件（不要逐条 REPL 提交来验文件形状）。

## 六、回归门（main，F04）

- 七门 `scripts\verify-vb-compiler-tests.ps1`：Failed 全 0；**总数按本任务新增格数增加**，逐门"除新增格外数字与基线一字未动"（D10 的实证门槛）⇒ 跑完必须**同步脚本内七门基线**（新增格落在 Syntax / Semantic / Emit 三门）。
- `Scripting\VisualBasicTest` 直跑 `-automated`（`dotnet test` 静默不跑）：ES-C1/C2 是**改写**，故该套件总数不变；读最后一行 JSON 的 `"TestsFailed":0`。
- 已知偶发：L2 `ScriptModeArgsTests` 的文化泄漏类红（本机交接文档 §4.5，不入仓）——若再红，验证者必须给"与本次改动无关"或"由本次改动引入"二者之一的判定，**"复跑即绿"不是结论**。

### F04 实测读数（2026-10-07，主线亲跑）

七门一次跑完（`exit 0` + `All seven compiler test project net10.0 gates completed`），除本任务新增格外逐门数字与基线一字未动：

| 门 | 读数（Total/Passed/Skipped/Failed） | 与基线差 |
|---|---|---|
| Phase2 | 143/143/0/0 | 未变 |
| Syntax | 4106/4103/3/0 | **+8**（ES-A1..A5、B1、B4、B5） |
| Symbol | 3407/3383/24/0 | 未变 |
| Semantic | 5919/5815/104/0 | **+5**（ES-A6..A10） |
| IOperation | 1574/1566/8/0 | 未变 |
| Emit | 4389/4286/103/0 | **+1**（ES-A11） |
| CommandLine | 483/476/7/0 | 未变 |

`scripts\verify-vb-compiler-tests.ps1` 内基线已同步（Syntax 4098→4106、Semantic 5914→5919、Emit 4388→4389）。
`Scripting\VisualBasicTest` 直跑 `-automated`：`TestsTotal 777 / TestsFailed 0 / TestsSkipped 0 / TestsNotRun 0`（ES-C1/C2 是改写 ⇒ 总数不变）。本轮**没有**出现 §六 那条已知偶发红。

档 2（重建 Debug `vbi`，探针 `..\..\..\tmp\probe-end-stmt\`，原始输出 `tmp\vortex-logs\script-mode-end-statement\_es-h-probes.log`）：

| 格 | 改后读数（已运行） |
|---|---|
| ES-H1 `a-end-top.vbx` | `A1`；`EXIT=0`；无 `A2` |
| ES-H2 `d-end-in-if.vbx` | `D1`；`EXIT=0`；无 `D2` |
| ES-H3 `c2-end-sub.vbx` | `C1`、`C-in`；`EXIT=0`；无 `C2` |
| ES-H4 `e-end-in-lambda.vbx` | `E1`；`EXIT=0`；无 `E2` |
| ES-H5 REPL（先 `R1` 再 `End`） | 打印 `R1` 后 `EXIT=0`、零诊断、其后提交不执行（改前同输入报 `(1) : error BC30678`） |
| ES-H6 哨兵 `b-exit-top.vbx` | `B1`；`EXIT=0`；无 `B2`（逐字不变） |
| ES-H7 哨兵 `x-exit.vbx` | `X1`；`EXIT=0`；无 `X2`（逐字不变） |
| ES-H8 哨兵 `normal.vb /target:exe` | `EXIT=0`、零诊断（逐字不变） |
| ES-H9 哨兵 `normal.vb /target:library` | `EXIT=1`、`BC30615`（逐字不变） |
| ES-H10 `vbi /check a-end-top.vbx`、`/check d-end-in-if.vbx` | 零诊断、`EXIT=0`；对照 `/check c-end-sub.vbx`（故意写坏）报 BC30183/BC30205、`EXIT=1` ⇒ 通道是活的 |

**未验项（如实登记，不静默）**：`#Load` 进来的树里的 `End`（验证者无解析器夹具）；进程级行为只在 Windows / .NET 10 / Debug 宿主上实测。

## 七、验证者档位声明要求

每格结论标：所用档位（档 1 已运行 ／ 档 2 宿主实跑 ／ 档 3 审查）＋ 证据三态（实锤／推测／猜测）。档 3 结论不得顶档 1 交付。落不进任何档的格 ⇒ 报 main「测不了」，不许静默跳过或硬撑低档。
