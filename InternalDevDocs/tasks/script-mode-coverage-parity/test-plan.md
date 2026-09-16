# 测试计划（script-mode-coverage-parity）

## 1. 层间分工

| 层 | 落点 | 本任务里管什么 |
|---|---|---|
| L1 解析 | `Compilers\VisualBasicSyntaxTest` | 只判语法树形状；本任务**预期零新增**（缺口是容器语义与覆盖，不是解析） |
| L2 语义 | `Compilers\VisualBasicSemanticTest` | 绑定期诊断与「绑定不抛异常」；U1（`Group By`）与任何新诊断落地在此 |
| L3 API | `Compilers\VisualBasicEmitTest` / `VisualBasicSymbolTest` | 成员表形状、IL 体级断言、发射不抛异常；U1 的 `Emit` 门 |
| L4 REPL/宿主 | `Scripting\VisualBasicTest` | **本任务的主战场**：宿主可见症状——编译要么成功要么给诊断、会话存活、运行结果正确、跨提交状态 |

**层间不重复**：L2 负责「诊断对不对」，L3 负责「产物形状对不对」，L4 负责「用户看到什么」。同一形状在三层都出现的，用不同的断言切面区分（沿用第二轮口径）。

**本任务的层间配比**：U3–U11 的绝大多数用例落在 **L4**（脚本模式特有面）；只有 U1（缺陷收口）需要 L2/L3/L4 三层齐备。这个配比是**有意**的——本任务补的是**宿主可见行为**的网，不是编译器内部的网。

---

## 2. 无副作用纪律（强制）

沿用第二轮，并补本任务新增的落点：

- 用例一律用内存 API：`VisualBasicScript.Create` / `RunAsync` / `EvaluateAsync` / `CommandLineRunner` + 内存 `StringReader`/`StringWriter`（`Helpers\TestConsoleIO.vb`）。
- `ScriptOptions` 只加内存引用（`AssemblyMetadata.CreateFromImage`）；`#Load` / `#r` 走内存 `SourceReferenceResolver`（第一轮已建立的 `MemorySourceReferenceResolver` 写法，见 `ScriptModeConformanceTests.vb:242`）。
- `BuildPaths.TempDir` 传**已存在的目录**（`AppContext.BaseDirectory`），避免创建目录（`ScriptModeConformanceTests.vb:182`）。
- **禁止**：网络请求、写文件、起进程、写注册表。

### 2.1 本任务新增的边角与处置

| 形状 | 风险 | 处置 |
|---|---|---|
| U7 PDB 用例 | 可能需落 PDB 文件 | **不落盘**：断言内存 `Emit(MemoryStream, …)` 产物。真栈帧**不落盘也可验**（生产把 PE 与 PDB 发到 `MemoryStream` 并从内存交给运行时，`StackFrame.GetFileName()` 读已加载符号而非磁盘）⇒ **本格不落盘**，不触发「停手问用户」 |
| U6 搜索路径用例 | 可能需真实目录 | 复用 `AppContext.BaseDirectory`（**已存在**）；**不得**新建目录或写文件 |
| U5 取消用例 | 可能用计时器/睡眠制造竞态 | 用**已取消的 token** 或 `RunAsync` 前同步取消；**不得**用 `Thread.Sleep` / 计时器 |
| 探针脚本 | 可能被测试引用 | `tmp\probes\` **不属于测试面**，不得被测试引用（`tmp\` 已 git-ignored） |

#### 2.1.1 既有测试文件里的落盘写法（**四个文件，全部实测**）

> **本清单是初稿的缺口**：初稿只披露了 1 个文件，实测有 **4 个**——而 U9 明确要在这四个文件里补用例（`design-detailed.md` §B 矩阵的族 59/64/65/13）。**执行者必须知道这些 helper 落盘，且不得复用。**

| 文件 | 落盘落点（**实锤**） | 新用例的写法 |
|---|---|---|
| `CommandLineRunnerTests.vb` | `CreateIsolatedTempDirectory`（`:44` `Directory.CreateDirectory`）、`CreateLibraryAssembly`（`:62` `compilation.Emit(assemblyPath)`）、`CreateCSharpLibraryAssembly`（`:86` 同上），另有多处 `File.WriteAllText`（`:191,210,243,391,410,414,427,440,452,464,484,487,508,521…`）与 `Directory.Delete`（`:198,217,250…`） | **不复用**上述 helper；新用例走 `AppContext.BaseDirectory` + 内存引用（`AssemblyMetadata.CreateFromImage`） |
| `InteractiveSessionReferencesTests.vb` | `CreateIsolatedTempDirectory`（`:15` `Directory.CreateDirectory`）+ `CreateLibraryAssembly`（`:37` `compilation.Emit(assemblyPath)`，**真写 DLL**） | 同上（族 59 在此补测） |
| `ScriptOptionsTests.vb` | `:136` `CreateDirectory`、`:138` `File.WriteAllBytes`、`:148` `Directory.Delete` | 同上（族 64/65 在此补测） |
| `ScriptTests.vb` | `:226` `CreateDirectory`、`:229` `File.WriteAllText`（`#Load` 用例）、`:236` `Delete` | 同上（族 13 的 `#Load` 返回语义矩阵在此补测） |

**「不改既有文件」的准确含义**（初稿措辞有歧义，已修）：**不修既有文件里的落盘写法**（那是既有技术债，不属本任务范围）；**可以在这些文件内新增无副作用用例**。两者不矛盾——本任务只对**新增部分**负无副作用责任。

### 2.2 变异判别力的三条方法论前提（**强制**；读数正文在此，不落在 `tmp\`）

> **为什么要抄进来**：本单元的判别力结论（「某用例有判别力 / 无判别力」）全部依赖下面三条**操作前提**。
> 前提本身此前只记在 `tmp\`（git-ignored）的流水账里 ⇒ **冷克隆复核不到**，读者会看到「换轨生效」的结论
> 却无法判断那次换轨**是不是静默失效了**。按任务 `README.md` §八 义务 7，关键读数须以**文本形式**落在
> 本目录或 `issues\`。以下读数均**实锤**（本轮自跑），沙箱全部在 `tmp\` 的副本上做、**仓库文件只读**。

#### 2.2.1 换轨命令里的路径**必须用正斜杠**（或加引号）

MSBuild 属性值里传 Windows 反斜杠路径会被 git-bash 的转义**吃掉**，且**静默**：目标文件不加载，
`<Compile Remove>` / `<Compile Include>` 两条都不跑，于是「变异版本」根本没参与编译，跑出来是**全绿**。

| 形态 | 命令片段（示意） | 读数 |
|---|---|---|
| 反斜杠 | `-p:CustomAfterMicrosoftCommonTargets=<项目根>\tmp\u9-fix\swap\u9fix-swap.targets` | `TestsFailed: 0` — **假阴性，变异没生效** |
| 正斜杠 | `-p:CustomAfterMicrosoftCommonTargets=<项目根>/tmp/u9-fix/swap/u9fix-swap.targets` | `TestsFailed: 1` — **变异生效** |

同一变异、同一命令，**唯一自变量是斜杠方向**。⇒ 换轨命令一律用 `C:/…` 形式的正斜杠。

#### 2.2.2 `-p:EnableSourceControlManagerQueries=false` **当且仅当**被换轨文件含 `#ExternalSource` 时必需

四格对照实验（两个自变量：被换轨文件**是否含 `#ExternalSource`** × 是否加该 flag；`-t:Rebuild`）：

| 格 | 被换轨的测试文件 | 含 `#ExternalSource` | 加 flag | 读数 |
|---|---|---|---|---|
| E1 | `ScriptModeObjectFormatterFixtures.vb`（`:244` 有 `#ExternalSource("z:\Fixture.vb", …)`） | 是 | **不加** | **`1 个错误`：`BC2001 找不到文件"z:\Fixture.vb"` @ 该文件 `(244,17)`** |
| E2 | 同上 | 是 | **加** | `0 个错误`，`BC2001` 计数 **0** |
| E3 | `ScriptModeApiSurfaceConformanceTests.vb`（`grep -c ExternalSource` = **0**） | 否 | **不加** | `0 个错误`，`BC2001` 计数 **0** |
| E4 | 同上 | 否 | **加** | `0 个错误`，`BC2001` 计数 **0** |

⇒ **结论**：flag 的**必要性取决于被换轨文件是否含 `#ExternalSource`**——
含 ⇒ **必需**（E1 与 E2 同文件、同命令，唯一自变量是该 flag）；不含 ⇒ **无害但非必要**（E3/E4 都零错误）。
**机制读码（实锤部分是报点链路本身）**：`Compilers\Core\Portable\CommandLine\CommonCompiler.cs` 的
`AcquireEmbeddedTexts`（声明于 `:413`）在 `:416` 有「`Arguments.EmbeddedFiles.IsEmpty` 则整段返回」的门，
非空时才在 `:442` 逐树调 `ResolveEmbeddedFilesFromExternalSourceDirectives`；`BC2001` 的报点在 VB 侧的该抽象
实现 `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCompiler.vb` 的
`ResolveEmbeddedFilesFromExternalSourceDirectives`（`:268`）：`resolver.ResolveReference(path, tree.FilePath)`
（`:286`）为 `Nothing` 时在指令处报 `ERR_FileNotFound`（`:290`）。
⇒ **报出 `BC2001` 要求 `Arguments.EmbeddedFiles` 非空**（`ResolveEmbeddedFilesFromExternalSourceDirectives`
经 grep 在共享层只有一个调用点，即 `:442`）——这是**实锤**（读码 + E1 确实报出了该错误）。
**但「该 flag 如何影响内嵌文件集」这一步本轮未能闭合，标推测**：同一四格用 `BeforeTargets="CoreCompile"`
的探针 target 打印 `EmbeddedFiles` 与三个开关，**四格 `EmbeddedFiles` 全为空**——与 E1 确实报出 `BC2001` 矛盾。
⇒ 该探针打印的**不是编译器命令行实际用的那个集合**（时间点或持有目标不同）。**不为它造机制解释**，
后续轮次若要闭合，须先把探针打到与编译器同一时点/同一持有目标上。
**另一层（推测）**：「本树不引用 SourceLink 包 ⇒ flag 非必要」在**本机这次**实测成立（E3/E4），
但 E1 反证它在含 `#ExternalSource` 的文件上**不成立** ⇒ 不要按「依赖树」推断该 flag 的必要性。

#### 2.2.3 换轨**必带正对照**

**静默失效产生的是假阴性**，而假阴性的读数是「全绿」——会被直接误读成「**该测试没有判别力**」
（进而得出「这条用例是摆设」的错误结论，甚至去改那条本来有判别力的用例）。
⇒ 每次换轨**先用一个已知必红的变异**跑一遍，确认它**确实变红**，再跑本次要测的变异。
**先例（实锤）**：本单元第一次跑 M-A 变异时因 §2.2.1 的反斜杠问题拿到 `TestsFailed: 0`，
正是**换轨正对照缺失**导致的假阴性；改成正斜杠后 `TestsFailed: 1`，变异才被确认生效。

**本单元变异判别力的两条实测锚点**（供复核时对照，方法是「把 throw 改成 return」；两处锚点均在
`Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`）：
① **M-A**：把同名同版本冲突分支的 throw（`:416-422`，`ScriptingResources.AssemblyAlreadyLoaded`）改成
`return loadedAssemblyWithEqualNameAndVersionOpt.Assembly` ⇒ **只** `ScriptModeApiSurfaceConformanceTests.SharedLibCopy_Different_ConflictingContentsAreRejected` 失败；
② **M-B**：把另一条（弱名称版本不匹配）分支的 throw（`:429-434`，`ScriptingResources.AssemblyAlreadyLoadedNotSigned`）改成返回 ⇒ **只** `SharedLibCopy_DifferentVersion_UnsignedConflictIsRejected` 失败。
两条各**只**打自己那格 ⇒ 两格互不代偿。（沙箱副本 md5 自证起点与仓库现值一致，改前/改后不同。）

---

## 3. 逐单元用例表

> 每个单元至少一条**判别性**用例：把该单元的改动/被测行为撤销后，这条用例必须失败。只加断言不加用例的，门计数不变但要核 `输出 dll mtime > 被测源文件 mtime`。
> 逐单元的 pass 条件全文见 `design-detailed.md` 对应单元；本表只给**分层落点**与**判别性论证要求**。

### U1 · 顶层不推断的后果（**非缺陷**，纯补测）

> 判定全文见 `design-overview.md` §3 与 `design-detailed.md` §U1。**产品源码零改动。**

| 层 | 用例 | 断言 |
|---|---|---|
| L4 | 顶层 `Dim q = <LINQ 查询>`（无 `As`） | **静态类型是 `Object`**——用**重载决议**判别（与 `ScriptModeStatementConformanceTests.vb:606` 同法），**不得**用 `GetType()` |
| L4 | 同一查询加 `As` 子句 / 放进顶层 `Sub` 的局部 | 求值成功且**值正确** |
| L4 | 顶层 `Dim q = <查询>` 后对 `q` 调晚期绑定成员 | 断言**具体失败形态**（异常类型 / 诊断 ID） |
| 对照 | 普通模式（经 **vbi 编译模式**，见 §C.3 探针纪律）同形状 | 普通模式**开 `Option Infer`** 时为 exit 0；不开时抛同一条异常 ⇒ 记录该对照，证明容器无关 |

**判别性论证**（**不适用**「撤销改动后必须失败」——本单元不改产品行为）：论证改为**成对用例必须给出不同结果**——不加 `As` 断言 `Object`、加 `As` 断言正确值；两条结果若相同，说明用例对「是否推断」不敏感，必须重写。

### U3 · 宿主对象绑定（L4 为主）

| 层 | 用例 | 断言 |
|---|---|---|
| L4 | `HostObjectBinding_*` **8 格**（`IS:1528,1545,1553,1570,1580,1590,1598,1617`）+ `HostObjectInRootNamespace` 单列 | 每格断言具体结果或具体诊断 ID（**不是**「不抛异常」） |
| L4 | `HostObjectAssemblyReference1-3` | 别名集合与递归传递（并入 U4） |
| L4 | **宿主成员不与提交成员构成方法组**；3 条遮蔽规则（族 26） | 负向：重载决议**不**跨源；正向：同名遮蔽后取到哪一侧 |
| L4 | `Shared Sub` / lambda 读宿主实例成员 | 负向（预期报错）+ 非 `Shared` 顶层 `Sub` 的正向对照 |
| 对照 | 普通编译上下文同形状 | 行为一致 |

### U4 · `<host>` / `<implicit>` 别名（L4 + L2）

**期望值来源已锚定**（初稿写「先读生产侧」，现已找到**规范性来源**）：`spec\spec-reference-directive.md` 的「Reference aliases」小节 + `proposals\proposal-reference-directive.md:109-118` 的生产锚点。详见 `design-detailed.md` §U4。

| 层 | 用例 | 断言 |
|---|---|---|
| L4 | `<host>` 隐藏非限定查找 | 宿主对象类型所在**命名空间**不能用非限定名访问 |
| L4 | 宿主对象成员仍可用 | 经 globals 实例/成员访问照常可用 |
| L4 | **隐藏是绝对的（负向，规范最硬一条）** | 别名程序集里的类型从脚本源码**完全不可达**，无语法可绕过（VB 无 `extern alias`） |
| L4 | `<implicit>` 不并入全局命名空间 | 补位程序集满足程序集身份，类型不并入全局命名空间 |
| L4 | 递归传递 | 被宿主程序集引用的程序集**同样**带 `<host>` |
| L4 | `CommandLineScriptGlobals` 作 `globalsType` | 别名集合与成员可见性 |
| L2 | 普通编译同规（`/nostdlib`） | 非全局别名对普通编译同样生效（规范明写「not specific to scripts」）；成本过高则停手上报 |

### U5 · 异常与取消后的提交链（L4）

| 层 | 用例 | 断言 |
|---|---|---|
| L4 | `catchException` 后 `ContinueWith` | 异常提交**之前**的声明可用 |
| L4 | 异常提交**之中**残留的声明 | 可用 |
| L4 | 已取消 token 触发 `OperationCanceledException` | 声明保留；`catchException` 过滤生效 |
| 对照 | `ScriptModeSubmissionConformanceTests.vb:256,267` 的「会话存活」断言 | **保留**（不替换），本单元加的是更强的「声明可用」 |

### U6 · 命令行参数 `Args` 与搜索路径（L4）

| 层 | 用例 | 断言 |
|---|---|---|
**格数上界 = 12**（`Args` 8 格 + 搜索路径 4 格，来源见 `design-detailed.md` §U6）。

| L4 | 脚本内读 `Args`（`.vbx` 执行 + REPL 两态） | 具体参数值 |
| L4 | `Args_Interactive1/2`、`Args_InteractiveWithScript1`、`Args_Script1-5`（**8 格**） | 逐格具体结果（`--` 分隔、`@rsp`、前缀 `@`/`-`/`/`、相对路径、工作目录） |
| L4 | `SourceSearchPaths1` / `_Change1` / `ReferenceSearchPaths1` / `_Change1`（**4 格**） | `/loadpath` / `/lib` 的解析结果（用 `AppContext.BaseDirectory`）；REPL 中 `SourcePaths` / `ReferencePaths` 变更后重试生效 |

### U7 · PDB / 调试信息与栈帧（L4，部分 L3）

| 层 | 用例 | 断言 |
|---|---|---|
| L3 | 字符串创建 / 流创建 × 代码来自文件 / 内联代码，编码开关折在第一组内 ⇒ 4 + 2 + 4 + 2 = 12 格 | 内存 `Emit` 产物的调试目录存在性；**不落盘** |
| L3 | 无编码时报 `ERR_EncodinglessSyntaxTree` | 具体诊断 |
| L4 | `#Load` 树的行号映射（与 `issues\issue-vbx-load-span-shift.md` 的回归呼应） | 诊断锚点行号 |
| — | 真栈帧 `GetFileName`/`Line`/`Column` | **内存 PDB 已可验，本格不落盘**：生产把 PE 与 PDB 发到 `MemoryStream` 并从内存交给运行时，`StackFrame.GetFileName()` 读已加载符号而非磁盘 |

### U8 · ObjectFormatter 代理族与异常栈（L4）

| 层 | 用例 | 断言 |
|---|---|---|
| L4 | `DebuggerDisplay_*` / `DebuggerProxy_*`（**32 个命名族**，夹具已就位） | 格式化字符串逐字断言 |
| L4 | `DebuggerProxy_FrameworkTypes_*` | 框架类型代理渲染 |
| L4 | `StackTrace_*` 7 格 | `FormatException` 的栈帧签名（泛型方法/泛型类型/`ByRef` 泛型参数） |
| L4 | `FormatConstructorSignature`、tuple/ValueTuple、`ArrayOfInt32_NoMembers`、`RecursiveRootHidden` | 逐格 |

**先决**：逐条读 C# 期望值把 30+ 夹具对上测试；对不上的**单独列出**，不硬凑。

### U9 · 脚本 API 面剩余缺口（L4）

**格数上界见 `design-detailed.md` §U9，合计 43**（上表 14 项 37 格 + 另补 4 族 6 格）。

按 `design-detailed.md` §B 矩阵中判为「高/中」且属 API 面的行逐项补：分支链隔离、重复求值不重跑、`Variables` 集合与 `IsReadOnly`、`GetImportScopes`、`#Load` 返回语义矩阵、`#r` 相对路径与扩展名优先级、`MissingAssemblySymbol` 降级、`null` 参数族、`Submission_HostConversions`、顶层 `Private`/`Protected` 可见性、递归基类型、`AllowUnsafe`/`CheckOverflow`/`WarningLevel` 落到编译选项。

**判别性原则**：`null` 参数族与负向可见性格必须**成对**（正例 + 反例）；「落到编译选项」类断言必须读**编译对象的实际选项值**，不是读 `ScriptOptions` 的属性。

### U10 · ledger 缺口填充（L4）

按 ledger 逐行补。**每行 `新补` 必须对应一个真实用例**，不允许「标了没写」。

### U11 · ledger 复核

见 `design-detailed.md` §U11 的五条可机械核对判据。

---

## 4. 全量回归口径

实现完成后必须全绿：

| 门 | 命令 |
|---|---|
| 宿主 / Scripting | `dotnet build Scripting\VisualBasicTest` 后直接跑程序集 `-automated`（`dotnet test` 对本 MTP 项目**静默跑 0 个**；须核 `TestCasesToRun > 0`） |
| 七门 | `powershell -File scripts\verify-vb-compiler-tests.ps1`（throw-on-mismatch；基线在 `:8-14`） |
| 公共 API | `PublicAPI.*.txt` 零增量 |
| 共享发射层 | `Compilers\Core\Portable\CodeGen\` 零 diff |

**新增用例会推高门计数** ⇒ 收口时同步 `scripts\verify-vb-compiler-tests.ps1` 的期望值（**只改数字，不改判据**）。

---

## 5. C# 脚本模式测试的族谱（用例形态蓝本）

上游 `{{Roslyn}}\src\Scripting\`（`CSharpTest\ScriptTests.cs` / `CommandLineRunnerTests.cs` / `InteractiveSessionTests.cs` / `ScriptOptionsTests.cs` / `ObjectFormatterTests.cs` / `InteractiveSessionReferencesTests.cs` + `CoreTest\ScriptOptionsTests.cs` / `RuntimeMetadataReferenceResolverTests.cs` / `NuGetPackageResolverTests.cs`）。

**只借用例形态，不借行为判据**——行为判据以本仓 spec（`spec\spec-scripting-dialect.md`）与普通上下文实测为准。

> **族谱与逐族归属见 `design-detailed.md` §B.1**（69 族，含 C# 基线 `文件:行号`、VB 侧覆盖 `文件:行号`、缺口性质与适用性）。本文件不重复维护该表，避免两处漂移。
>
> 族谱可重跑核对：`grep -hoE "public (async )?(void|Task) [A-Za-z0-9_]+" CSharpTest/*.cs CoreTest/*.cs`。

---

## C · VB 特有语法完整覆盖 ledger

> **本表是本任务的交付物之一**（见 `design-overview.md` §2），不是草稿。收口后仍留在此处供后续轮次复用。
> **表由 U2 建齐**（本节当前为**骨架 + 已实测的种子行**）；U10 填 `缺口`；U11 复核归零。

### C.0 枚举来源与计数命令

**命令**（工作目录 `InternalDevDocs\vblang\spec\`；**须在 U2 重跑并把实际读数抄进下表**）：

```bash
for f in statements.md type-members.md expressions.md lexical-grammar.md preprocessing-directives.md; do
  echo "$f  ##=$(grep -c '^## ' $f)  ###=$(grep -c '^### ' $f)  ####=$(grep -c '^#### ' $f)"
done
```

**本轮读数**（**实锤**，2026-09-15 跑）：

| 来源 | `##` | `###` | `####` | 小节合计 | 用途 |
|---|---|---|---|---|---|
| `statements.md` | 15 | 20 | 8 | **43** | 语句面 |
| `type-members.md` | 8 | 24 | 8 | **40** | 声明与成员面 |
| `expressions.md` | 26 | 52 | 4 | **82** | 表达式面（**筛选比例最大**，只取与容器无关或受容器影响的） |
| `lexical-grammar.md` | 6 | 12 | 0 | **18** | 字面量与词法面（只取 VB 特有的） |
| `preprocessing-directives.md` | 4 | 2 | — | **6**（`##`+`###`；另有 30 个 `#` 级条款） | 预处理面（只取本 fork 实际实现的） |

**合计待判行数 ≈ 43 + 40 + 82(筛后) + 18 + 6**，`expressions.md` 筛后预计落在 20–30 行区间（**推测**，U2 建表时确定）。

### C.1 状态取值（闭集）

| 状态 | 含义 | 收口时允许存在 |
|---|---|---|
| `已覆盖` | 已有用例覆盖该语法在脚本容器里的行为，给 `文件:行号` | 是 |
| `新补` | 本任务补了用例 | **否**（关闭时须全部改 `已覆盖`） |
| `不适用` | 脚本容器里不存在该语义或与容器无关；**须给理由 + 检索证据（命令 + 命中情况）** | 是（须带证据） |
| `缺口` | 应有行为但无用例 | **否（U11 后须为 0）** |

### C.2 表结构

每行两栏容器状态（**只填顶层栏不填嵌套栏的行不算覆盖**——判别力来自两栏的差）：

```
| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（用例 文件:行号） | 探针实测（非判据） |
```

**列规约**（初稿的种子行违反过，见 §C.3 修订说明）：

| 列 | 内容 | 约束 |
|---|---|---|
| 语法构造 | spec 小节名或 parser arm | — |
| 来源 | `文件:行号` | — |
| 顶层容器 / 嵌套容器 | **只能取 §C.1 四值之一** | 诊断码、探针读数一律**不得**出现在这两列 |
| 依据 | 指向**真实测试方法**的 `文件:行号`（`<Fact>`/`<Theory>` 方法声明行）；`不适用` 行填检索命令 + 命中情况 | `已覆盖`/`新补` 必须非空 |
| 探针实测 | 探针读数（参考用） | **不计入 `已覆盖`**（`README.md` §四 判据②） |

**两栏的定义见 `README.md` §四「两栏的精确定义」**——尤其注意「顶层声明（含 `Class`/`Structure`/`Partial` 等类型声明）属顶层容器」。初稿按「是否写在嵌套类型内」误用，导致 5 行两栏对调。

### C.3 · 完整 ledger（U2 建齐；原「种子行（格式示例）」已并入）

> **本节已是成品 ledger**，不再是格式示例。原始 24 条种子行**已并入**：与 spec 小节同构造的种子行（`ReDim`/`Erase`/`On Error`/`AddressOf`/`Declare`/`Option`/`MyClass`/`Partial`/`Structure`/`WithEvents`）合并进对应 spec 行，探针读数移入「探针实测」列；spec 标题树不含的种子行（`Static`/`Err`/`CallByName`/`IIf`/`Like`/`LBound`/`UBound`/XML 字面量/LINQ/`Shadows`）保留为 §C.3.F 的**补充行**。旧种子行的「要点 1–6」保留在 §C.3.G。

#### C.3.0 计数命令、分母与判定规则

**计数命令**（工作目录 `InternalDevDocs\vblang\spec\`）与 **2026-09-15 U2 重跑读数**（**实锤**）：

```bash
for f in statements.md type-members.md expressions.md lexical-grammar.md preprocessing-directives.md; do
  echo "$f  ##=$(grep -c '^## ' $f)  ###=$(grep -c '^### ' $f)  ####=$(grep -c '^#### ' $f)"
done
```
输出（逐字）：
```text
statements.md  ##=15  ###=20  ####=8
type-members.md  ##=8  ###=24  ####=8
expressions.md  ##=26  ###=52  ####=4
lexical-grammar.md  ##=6  ###=12  ####=0
preprocessing-directives.md  ##=4  ###=2  ####=0
```
读数与 `README.md` §四 登记值**逐格一致**（43 / 40 / 82 / 18 / 6）。

**分母（判据③，此后只增不减）**：

| 来源 | 行数 | 说明 |
|---|---|---|
| A · spec 标题树 | **189** | `statements` 43 + `type-members` 40 + `expressions` 82 + `lexical-grammar` 18 + `preprocessing-directives` 6；**全量出行，不预先筛选** |
| B · parser 分派 arm | **30** | 该构造在 spec 标题树中**无对应小节**时才另立行；有对应小节的 arm **并入该行**（§C.3.E 逐条列出并入关系，并入后 arm 的 `文件:行号` 同时写进「来源」列）。**24 → 27（修复轮）**：新拆出 `Namespace`、`MyBase`、`MyClass` 三行（原先分别并入「声明入口 arm」行与「`### Instance Expressions`」行 ⇒ 缺口被吸收、计数少报；拆出后那两行**不再覆盖**这三个构造，故不构成重复计数）——见 §C.3.H 第 3 条。**27 → 30（终态验证修复轮）**：新补 `End`/`Stop`（`Parser.vb:996`/`:1002`，`statements.md` 无对应小节；原先被登记为「已并入」却无任何行接收）与 `#Disable`/`#Enable`（`ParseConditional.vb:79`，`preprocessing-directives.md` 无对应小节）三行 |
| C · fork 新增指令 | **4** | `#Load` / `#R`·`#Reference` / `#!` / 裸 `#`（无法识别的指令） |
| D · 补充行（种子行保留） | **15** | spec 标题树与 parser arm 均不含、但属 VB 特有面且脚本容器可写；由原种子行并入 |
| **合计** | **238** | 232 → **235**（修复轮）→ **238**（终态验证修复轮补 `End`/`Stop`/`#Disable`·`#Enable` 三行）；判据③「只增不减」允许增行；两栏分母均为 **238** |

**来源 B 的取证修正（**实锤**，与 `README.md` §四 字面不同，U2 已核；行号经终态验证修复轮重跑）**：语句主分派表**不在** `ParseStatement.vb`，而在 `Parser.vb:935-1272`（**`End Select` 在 `:1272`**；原文写 `:1263`，那是 `Case Else` 体内的行）。`ParseStatement.vb` 里的五个子分派函数分别在 **`:24`**（`ParseContinueStatement`）/ **`:95`**（`ParseExitStatement`）/ **`:487`**（`ParseAnachronisticStatement`）/ **`:1198`**（`MakeAssignmentStatement`）/ **`:1418`**（`ParseExpressionBlockStatement`）——原文笼统写「`:933` 起」，而 `:933` 实落在 `ParseGoToStatement`（`:918` 起）的 `End Function` 附近**，不是任何子分派的起点**。U2 仍以 `ParseStatement.vb` 与 `ParseExpression.vb` 的 arm 为主，并**补入 `Parser.vb` 的语句主分派**——否则「实现实际支持什么」的机械枚举会漏掉语句面的大半。arm 出处逐行写在「来源」列。

**状态判定规则（U2 起用，后续轮次沿用）**——四值闭集，逐格独立判定：

| 状态 | U2 的操作定义 |
|---|---|
| `已覆盖` | 在 `Scripting\VisualBasicTest\*.vb` 里存在**指向真实 `<Fact>`/`<Theory>` 方法声明行**的用例，在该容器里触发了该构造。**逐条打开核对**过。**负向（断言诊断）用例算覆盖（main 裁定 5）**：本任务作者给定的判定原则逐字「崩编译器是 bug。**要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」**」——「报出诊断」是**两条合法出口之一**；`spec-scripting-dialect.md` 把 `BC36965`/`BC36966`/`BC37343` 等**明文规定**为脚本方言的规范行为，断言这些诊断的用例**正是**对该语法在该容器里的规范行为的覆盖。**但前提是该用例指向该语法本身**——只断言「该语法在此报错」才算；**依据指向 helper / 调用点实参 / 测试自身代码的，不算覆盖，一律 `缺口`（main 裁定 6）** |
| `缺口` | 该构造是**可写语法形态**（语句 / 表达式 / 声明 / 指令），脚本容器里写得出，但无测试方法覆盖 |
| `不适用` | 该构造**不是可写语法形态**：① 描述性 / 语义性小节（无产生式）；② 纯分组小节（子小节已逐行出行）；③ 词法级规则（与容器无交互，无独立可写形态）；④ 本 fork / 本平台无对应实现（WinRT 等）。必给理由 + 检索证据 |
| `新补` | 本任务补了用例（U10 填，U11 时须为 0） |

> **检索陷阱（**实锤**）**：模式含 `<` 的 Grep 会静默零命中（`<Fact` 在满是 `<Fact>` 的文件上返回 No matches）。本节所有检索一律用**纯词模式**（`Fact` / `Theory` / 关键字本身）复核。

**「依据」列的书写约定（U2 起用，U11 按此复核）**：

1. 单元格里出现 `文件:行号` 且**不带**「源码」前缀时，该 `文件:行号` **必须**指向 `<Fact>`/`<Theory>` 标注的**方法声明行**。**机械核对的口径与实测值（本轮修复轮重跑，口径写明）**：① **方法声明行索引** = `Scripting\VisualBasicTest\` 下 **21 个测试文件**的 **454** 条 `<Fact>`/`<Theory>` 属性行（属性行与紧随其后的方法声明行一一对应；属性与方法之间有 `<InlineData …>` 时仍算同一方法）。**该索引是移动靶**（U10 持续新增测试文件）——上列读数是**修复轮那一刻**的快照，**不写进判据**；U11 复核须用同一命令重测并报**当时**读数（见 §C.3.H 第 6b 条）；② **本轮修复前**（对修改前的同一张表实测）：测试文件 `文件:行号` 共 **261 处 / 去重 149 个**，去重后落在方法声明行的 **114 个**；把 parser / spec 的指向一并计入则为 **343 处 / 去重 209 个**。③ **修复后（本文件现值）**：测试文件 **307 处 / 去重 157 个**（去重后落在方法声明行 **118 个**，按出现次数计 **263 处**）；含 parser / spec 则全表 **395 处 / 去重 220 个**——增量来自 43 处错锚改写成「方法 `文件:N`；源码 `:M`」两段式（每条新增一个**方法**指针）与新增的 3 行。另：修复前该表有 **43 处**测试文件指向不落在方法声明行（42 处非属性行 + 1 处落在 `<Fact>` 属性行 `ScriptTopLevelCrashTests.vb:247`），已逐条改写为约定 2 的形式（清单见 §C.3.H 第 2 条）。`已覆盖` 行**全部**至少含一个合法方法指针。
2. 构造出现在该测试的**多行脚本源码字符串里**时，写作「方法 `XxxTests.vb:N`；源码 `:M`」——`:M` 是脚本源码字符串内的行，**不是**方法声明行，单独出现时**不作为判据**。
3. `不适用` 行的「依据」填**检索命令 + 命中情况**（可机械复核）；`缺口` 行的「依据」填检索命令 + 命中情况，或指向同表另一行的同一证据（并在括号里注明「全表同一条证据，不重复计入统计」）。
4. **「依据」列允许的三类指针（main 裁定，本轮补入；U11 机械核对按此执行）**：① **方法声明行**（`<Fact>`/`<Theory>` 标注的方法声明行）——`已覆盖`/`新补` 行**必有**至少一个；② **源码行**（该构造所在的多行脚本源码字符串内的行，写作 `源码 :M`）；③ **检索命中 / helper / 探针记录行**（写作 `检索` / `helper`，或直接在句中标明「测试自身代码」「探针记录行，非测试方法」）。**机械核对只校验 ① 存在且确为方法声明行**，②③ 不要求是方法行、不参与判据②——但**必须标注清楚类型**，不得用裸 `文件:行号` 冒充方法指针（这正是本轮 42 处错锚的成因）。

#### C.3.A `statements.md`（43 行；工作目录 `InternalDevDocs\vblang\spec\`）

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `## Control Flow` | `statements.md:27` | 不适用（分组小节：无产生式，其下 7 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' statements.md` = 20，子小节全部在 §C.3.A 出行 | — |
| `### Regular Methods` | `statements.md:35` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:118`（`TopLevelExitSubAndFunctionInMethod_Conform` 在顶层声明 `Function Value1`/`Sub Do1`）；嵌套 `:202`（`OnErrorInsideMethod_Conforms` 的 `Sub Swallow`/`Sub Jump` 体内） | — |
| `### Iterator Methods` | `statements.md:61` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:500`（`TopLevelIteratorFunction_Conforms` 的 `Iterator Function CountTo` 写在脚本顶层）；嵌套（迭代器方法写在脚本内的类型里）：`grep -n "Iterator Function" Scripting/VisualBasicTest/*.vb` 命中 4 文件，全部写在脚本顶层，无一在嵌套类型内；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_Synthesized.vb:1637`（`IteratorStateMachineAttribute_Method`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Iterator Function` 出现 **4** 处，位于该块声明的类型体内）| — |
| `### Async Methods` | `statements.md:105` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:484`（`TopLevelAsyncSubAndFunction_Conform`）；源码 `:486` `Async Sub FireAndForget()`、`:489` `Async Function ComputeAsync()`。嵌套：检索**本轮实测重跑**`grep -n "Async Sub\|Async Function" Scripting/VisualBasicTest/*.vb` → **75 行 / 8 文件**（含 `Helpers\` 的递归口径 **80 行 / 9 文件**）。逐条看：脚本源码内的命中只有 `ScriptModeConformanceTests.vb:486,489`（顶层 `Async Sub`/`Async Function` **声明**）、`ScriptModeSubmissionConformanceTests.vb:358,403` 与 `ScriptModeStatementConformanceTests.vb:432`（三处都是**方法体内的 lambda**，属 `## Lambda Expressions`），其余全是测试自身的方法签名与 helper；**无一处是脚本内声明类型里的 `Async` 方法声明** ⇒ 嵌套 `缺口`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:67`（`Simple_Void`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Async Sub` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#### Async Sub` | `statements.md:162` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:484`（源码 `:486` `Async Sub FireAndForget()`）；嵌套同上；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:67`（`Simple_Void`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（与 `### Async Methods` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Async Sub` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#### Mutable structures in async and iterator methods` | `statements.md:172` | 不适用（语义说明：`sed -n '172,188p' statements.md` 无 `antlr` 产生式，只述可变结构捕获规则） | 不适用（同上） | `sed -n '172,188p' statements.md`（**实锤**：纯散文） | — |
| `### Blocks and Labels` | `statements.md:189` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:194`（`TopLevelGoTo_JumpTakesEffect` 顶层 `skip:` 标签 + `GoTo`）；嵌套 `ScriptModeStatementConformanceTests.vb:197`（`Sub Jump()` 体内 `Fault:` 标签） | — |
| `### Local Variables and Parameters` | `statements.md:235` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`）；嵌套 方法 `ScriptModeStatementConformanceTests.vb:568`（`OptionInferOff_MakesLocalObject_Conforms`）；源码 `:574`（`Sub Probe()` 体内 `Dim inferred = 1`） | 普通 `Static` 局部见 §C.3.F 的 5 条补充行 |
| `## Local Declaration Statements` | `statements.md:327` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`）；嵌套 方法 `ScriptModeStatementConformanceTests.vb:568`（`OptionInferOff_MakesLocalObject_Conforms`）；源码 `:574`（`Sub Probe()` 体内 `Dim inferred = 1`）。arm：`Parser.vb:1029` | — |
| `### Implicit Local Declarations` | `statements.md:473` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:654`（`TopLevelUndeclaredName_IsReported`：顶层 `undeclared = 5` 不产生隐式局部，报 BC30451）；嵌套 `:637`（`OptionExplicitOff_ConformsInMethodBody` 的 `Sub Probe` 内隐式局部可用） | — |
| `## With Statement` | `statements.md:515` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:256`（`TopLevelWithBlock_Conform`）；另 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；REPL 顶层 `With sb : .Append("x")`）。嵌套：`grep -n "With " Scripting/VisualBasicTest/*.vb` 命中全部在脚本顶层或测试自身代码；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenWithBlock.vb:13`（`WithTestModuleField`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `With / End With` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## SyncLock Statement` | `statements.md:554` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:237`（`TopLevelUsingAndSyncLock_Conform`）；`ScriptTopLevelCrashTests.vb:74`（`SyncLock 5` 报 BC30582）；`:531`（`SyncLock` 内 `Await` 报 BC36943）。嵌套：无方法体内的 `SyncLock`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenSyncLock.vb:13`（`SimpleSyncLock`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17 复核：所引源码块内 `SyncLock` 语句 **1** 组（`SyncLock GetType(C1)` … `End SyncLock`）；`SyncLock` 原始词频 3，另 2 处是 `End SyncLock` 与字符串 `"Inside SyncLock."`，均不计入）| — |
| `## Event Statements` | `statements.md:621` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:279`（`TopLevelEvent_AccessorsRun`）；嵌套 `ScriptModeStatementConformanceTests.vb:351`（`NestedClassEventAddRemoveHandler_Conforms`，事件成员属于脚本内 `Class Button`） | — |
| `### RaiseEvent Statement` | `statements.md:633` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:322`（`RaiseEvent` 写在顶层 `Sub RaiseIt` 里 = 语义上的顶层容器路径，源码整段在脚本顶层）；另 `:299`（`Shared Event` 由顶层 `Shared Sub` 触发）；嵌套 `ScriptModeConformanceTests.vb:427`（`Sub RaiseIt()` 体内 `RaiseEvent Changed(...)`，源码 `:446`） | 种子行读数：顶层裸 `RaiseEvent` 报 BC30188（`ScriptModeStatementConformanceTests.vb:370`） |
| `### AddHandler and RemoveHandler Statements` | `statements.md:698` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:279`（脚本顶层 `AddHandler E, h` / `RemoveHandler E, h`）；`CommandLineRunnerTests.vb:532`（`.vbx` 顶层 `AddHandler` + lambda）；嵌套 `ScriptModeConformanceTests.vb:427`（`Sub Hook()` 体内 `AddHandler`，源码 `:443`） | — |
| `## Assignment Statements` | `statements.md:754` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements` 顶层 `total = total + stepValue`）；嵌套 `ScriptModeStatementConformanceTests.vb:457`（`Sub Bump()` 体内 `count += 1`） | — |
| `### Regular Assignment Statements` | `statements.md:766` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`，源码 `:251` `total = total + stepValue`）；嵌套 `ScriptModeStatementConformanceTests.vb:457`（`Sub Bump()` 体内 `count += 1`，源码 `:461`） | — |
| `### Compound Assignment Statements` | `statements.md:855` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:241`（`total += 3`）；嵌套 `ScriptModeStatementConformanceTests.vb:457`（`count += 1`）；另 方法 `CommandLineRunnerTests.vb:1020`（**REPL 路径**；`TestCompoundAssignmentDoesNotPrint`）。分派 arm：`ParseStatement.vb:1199`（`MakeAssignmentStatement` 的十个复合赋值 token） | — |
| `### Mid Assignment Statement` | `statements.md:902` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；`TestMidRedimWithDoNotPrint` 的 REPL 顶层 `Mid(s, 1, 2) = "ab"`）。arm：`Parser.vb:1113` 的 `MidKeyword` 分支 → `ParseStatement.vb:1647`。嵌套：无方法体内的 `Mid` 赋值；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\CompoundAssignment.vb:585`（`MidAssignment1`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Mid(...) = 赋值` 出现 **10** 处，位于该块声明的类型体内）| — |
| `## Invocation Statements` | `statements.md:932` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:204`（`TestCallStatementReturnValue` 顶层 `Call`）；`:241`（顶层裸调用）；嵌套 `ScriptModeConformanceTests.vb:427`（`Sub Hook()` 体内调用） | — |
| `## Conditional Statements` | `statements.md:974` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:276`（`TopLevelSelectCase_Conform` 的 `For` + `Select Case` 组合，源码整段在顶层）；方法 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`），源码 `:249-253`（顶层 `If … Else … End If`；**原文引 `:255`，该行实为 `Case 16`**）。嵌套 方法 `ScriptModeConformanceTests.vb:399`（`TopLevelCustomEvent_AccessorsAndRegistration_Conform`）；源码 `:410`（`RaiseEvent` 访问器体内 `If _handlers IsNot Nothing Then …`） | — |
| `### If...Then...Else Statements` | `statements.md:985` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`）；源码 `:249-253`（顶层 `If … Else … End If`；**原文写 `:255-259`，那一段实为 `Select Case`**）；方法 `ScriptModeStatementConformanceTests.vb:549`（`OptionCompareText_TakesEffect`）；源码 `:548`（顶层 `If` 取 `Option Compare`）。嵌套 方法 `ScriptModeConformanceTests.vb:399`；源码 `:410`（访问器体内 `If`） | — |
| `### Select Case Statements` | `statements.md:1080` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:276`（`TopLevelSelectCase_Conform`；含 `Case 1, 2` / `Case Is > 10` / `Case Else`）。arm：`Parser.vb:940`（`CaseKeyword`）/ `:943`（`SelectKeyword`）。嵌套：无方法体内的 `Select Case`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenSelectCase.vb:256`（`SimpleSelectCase_DateTime`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Select Case` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## Loop Statements` | `statements.md:1152` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:303`（`TopLevelLoops_Conform`）；嵌套 `:424`（`Iterator Function CountTo` 体内 `For i = 1 To n`） | — |
| `### While...End While and Do...Loop Statements` | `statements.md:1197` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:303`（`TopLevelLoops_Conform`；顶层 `While` / `Do … Loop Until` / `Do While … Loop`）；`:90`（`TopLevelExitLoopStatements_Conform`；`Exit While`/`Exit Do`）。嵌套：无方法体内的 `While`/`Do`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTests.vb:4262`（`DoLoop4`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Do...Loop` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### For...Next Statements` | `statements.md:1260` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:303`（`TopLevelLoops_Conform`；顶层 `For i = 1 To 3`）；嵌套 `:424`（`Iterator Function CountTo` 体内 `For`） | — |
| `### For Each...Next Statements` | `statements.md:1328` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:298`（顶层 `For Each text In …`）；嵌套 方法 `ScriptModeConformanceTests.vb:631`（`NestedGenericTypesAndConstraints_Conform`）；源码 `:643`（`Function FirstOf` 体内 `For Each item In items`） | — |
| `## Exception-Handling Statements` | `statements.md:1487` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:241`（源码 `:290-297` 顶层 `Try/Catch/Finally`）；方法 `ScriptModeSubmissionConformanceTests.vb:267`（**REPL 路径**；提交链）。嵌套：无方法体内的 `Try`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:13`（`TryCatchSimple`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17 复核：所引源码块内 `Try` / `Catch` / `Finally` 语句各 **1** 处（`End Try` 1 处）；原始词频 3/2/2 中另有 3 处落在字符串 `"Try"` / `"Catch" & …` / `"Finally"` 内、1 处是 `End Try` 终结符，均不计入）| — |
| `### Structured Exception-Handling Statements` | `statements.md:1498` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:268`（**REPL 路径**；`ReplHandledException_KeepsTheSessionAlive` 顶层 `Try/Catch`）；`ScriptTopLevelCrashTests.vb:509`（顶部 `Catch` 体内 `Await` 报 BC36943）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:180`（`TryCatchReuseLocal`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Try/Catch` 出现 **2** 处，位于该块声明的类型体内）| — |
| `#### Finally Blocks` | `statements.md:1541` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:241`（源码 `:295-296` 顶层 `Finally`）；`ScriptTopLevelCrashTests.vb:745`（`Finally` 体内 `GoTo` 报 BC30101）；`:765`（`Return` 出 `Finally`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:889`（`EmptyTryOrEmptyFinally`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Finally` 出现 **4** 处，位于该块声明的类型体内）| — |
| `#### Catch Blocks` | `statements.md:1556` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:509`（`Catch ex As System.Exception` 体内 `Await`）；`:642`（跨 `#Load` 树的 `Catch` 体，逐树一次诊断）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:869`（`ExitTryFromCatch`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Catch` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#### Throw Statement` | `statements.md:1631` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:257`（**REPL 路径**；`ReplUncaughtException_KeepsTheSessionAlive` 顶层 `Throw`）；`ScriptTests.vb:312`。嵌套：无方法体内的 `Throw`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:715`（`ThrowNothing`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Throw Nothing` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Unstructured Exception-Handling Statements` | `statements.md:1666` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:224`（`TopLevelOnErrorStatement_IsReported`；顶层 `On Error Resume Next` 报 BC36956）；嵌套 `:202`（`OnErrorInsideMethod_Conforms`；`Sub Swallow`/`Sub Jump` 内的 `On Error Resume Next` / `On Error GoTo Fault`） | — |
| `#### Error Statement` | `statements.md:1704` |已覆盖| 已覆盖 | 检索：`grep -n "Error [0-9]" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm 存在：`Parser.vb:1098` → `ParseStatement.vb:1569`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:15`（`ErrorHandler_WithValidLabel_No_Resume`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Error n` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeStatementConformanceTests.vb:689`（`TopLevelErrorStatement_IsCaughtByTheEnclosingTry`）。| — |
| `#### On Error Statement` | `statements.md:1714` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:224`（`TopLevelOnErrorStatement_IsReported`，BC36956）；另 `ScriptTests.vb:320`。嵌套 `ScriptModeStatementConformanceTests.vb:202`（`OnErrorInsideMethod_Conforms`；`On Error Resume Next` + `On Error GoTo Fault` 均实测生效） | 种子行读数（`On Error GoTo 0` / `-1`）：见 §C.3.F |
| `#### Resume Statement` | `statements.md:1742` |已覆盖| 已覆盖 | 检索：`grep -n "Resume" Scripting/VisualBasicTest/*.vb` 命中 `ScriptModeStatementConformanceTests.vb`（仅为 `On Error Resume Next` 的组成部分）与 `ScriptTests.vb:320`（同上）⇒ **独立 `Resume` / `Resume Next` 零覆盖**。种子行探针读数见 §C.3.F；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:41`（`ErrorHandler_WithGotoMinus1andMatchingLabel`）（U10a-fix 换锚）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Resume` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeStatementConformanceTests.vb:707`（`TopLevelResumeStatement_IsReported`）；方法 `:713`（`TopLevelResumeNextStatement_IsReported`）。| 种子行探针：`resume-statement-in-top-level-sub` / `resume-next-after-error`，两模式同读数（不计入 `已覆盖`） |
| `## Branch Statements` | `statements.md:1805` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:194`（`GoTo` 前向跳）、`:209`（反向 `GoTo` 成环）、`:225`（重复标签报 BC30094）；`ScriptModeStatementConformanceTests.vb:158`（跳出 `For`/`Using`/`While`）。嵌套 `ScriptModeStatementConformanceTests.vb:113`（方法体内 `Exit Sub`/`Exit Function`）；`:127`（顶层裸 `Exit Sub` 报 BC30065）。arm：`Parser.vb:937`/`:1005`/`:1008`/`:999`/`:991`/`:1223` | — |
| `## Array-Handling Statements` | `statements.md:1859` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`）。嵌套：无方法体内的 `ReDim`/`Erase`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\RedimStatementTests.vb:13`（`TestRedimWithSimpleArray`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `ReDim` 出现 **10** 处，位于该块声明的类型体内）| — |
| `### ReDim Statement` | `statements.md:1870` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`；源码 `:337` `ReDim Preserve dynamicArr(4)`、`:334` `ReDim dynamicArr(1)`）；另 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；REPL 顶层 `ReDim`）。arm：`Parser.vb:1023`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Emit\CompilationEmitTests.vb:3675`（`ReDimPreserve`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `ReDim Preserve` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Erase Statement` | `statements.md:1937` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`；源码 `:336` `Erase fixedArr`、`:337` `Erase dynamicArr`）；断言 `:338` 读回 `2/True/True`。arm：`Parser.vb:1211`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\EraseStatementTests.vb:15`（`Simple`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Erase` 出现 **2** 处，位于该块声明的类型体内）| — |
| `## Using statement` | `statements.md:1963` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:237`（`TopLevelUsingAndSyncLock_Conform`，源码 `:235-237`）；`:30`（`TopLevelAwaitInBlocks_Conform`；`Using` 内 `Await`）；`:163`（`TopLevelGoToOutOfBlocks_Conform`；`GoTo` 出 `Using`）。arm：`Parser.vb:949`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenUsingStatement.vb:13`（`ParameterAsResource`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Using / End Using` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## Await Statement` | `statements.md:2039` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:387`（`TestTopLevelBareAwaitStatement`）；方法 `ScriptModeStatementConformanceTests.vb:30`（`TopLevelAwaitInBlocks_Conform`，六种块内 `Await`）；方法 `:68`（`TopLevelAwaitInFinally_IsReported`，`Finally` 内报 BC36943）。嵌套 方法 `ScriptModeConformanceTests.vb:484`（`TopLevelAsyncSubAndFunction_Conform`）；源码 `:487`（`Async Sub FireAndForget` 体内 `Await`） | — |
| `## Yield Statement` | `statements.md:2051` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:84`（`TopLevelYieldWithoutIterator_IsReported`，顶层裸 `Yield` 报 BC30800）；方法 `:426`（`ReleaseOptimizedIteratorAndAsync_Conform`，顶层迭代器释放路径）。嵌套 方法 `ScriptModeConformanceTests.vb:500`（`TopLevelIteratorFunction_Conforms`）；源码 `:504`（`Iterator Function CountTo` 体内 `Yield i`）；方法 `ScriptModeSubmissionConformanceTests.vb:677`（`ThreeLevelNestedLambdasAwaitAndYield_Conform`）；源码 `:363`/`:368`（三层 lambda 内 `Yield`） | — |
#### C.3.B `type-members.md`（40 行；工作目录 `InternalDevDocs\vblang\spec\`）

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `## Interface Method Implementation` | `type-members.md:5` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:606`（`NestedInheritanceHierarchy_Conforms`：`Implements IShape` 于 `:619`、`Implements IShape.Area` 于 `:621`，两个 `Class` 都写在脚本顶层）。arm：`Parser.vb:1228`（`ImplementsKeyword`）。嵌套：无脚本内类型里的 `Implements` 再嵌套一层的用例；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_ObsoleteAttribute.vb:2728`（`Bug858839_1`）（U10a-fix 换锚）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Implements` 出现 **6** 处，位于该块声明的类型体内）| 种子行读数：探针 `implements-and-handles-in-nested-types` 脚本 `I=hi` |
| `## Methods` | `type-members.md:157` | 不适用（分组小节：无产生式，其下 9 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' type-members.md` = 24 | — |
| `### Regular, Async and Iterator Method Declarations` | `type-members.md:364` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:484`（`Async Sub`/`Async Function`）、`:500`（`Iterator Function`）、`:516`（`Overloads Function`）、`:347`（`Property`）。嵌套：无脚本内类型里的方法声明再嵌套一层的用例；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\IteratorTests.vb:17`（`BasicIterator`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Iterator Function` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### External Method Declarations` | `type-members.md:393` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeSubmissionConformanceTests.vb:748`（`TopLevelDeclareForms_Conform`，`:428-431` 覆盖 `Alias`/`Auto`/`Ansi`/`Unicode`）；`:770`（`TopLevelDeclareMarshalAs_Conform`；`MarshalAs` on `Declare` 参数，含数组与 `ByRef Out`）；`:759`（`TopLevelDllImportMethod_Conform`；`DllImport` 方法）。arm：`Parser.vb:1228`（`DeclareKeyword`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_MarshalAs.vb:1253`（`MarshalAs_AllParameterTargets_Declare`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Declare Function` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Overridable Methods` | `type-members.md:474` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:606`（`MustInherit`/`MustOverride`/`NotOverridable Overrides` 于 `:611-613`；`Overrides Function Area` 于 `:621`）；`:702`（`Public Overrides Function ToString`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenOverridingAndHiding.vb:40`（`TestSimpleOverrides`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `MustOverride / Overrides` 出现 **2** 处，位于该块声明的类型体内）| — |
| `### Shared Methods` | `type-members.md:549` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:671`（`TopLevelSharedExtensionMethod_Conforms` 的 `Shared Function Twice`）；`ScriptTopLevelCrashTests.vb:299`（`Shared Sub RaiseIt`）；`:713`（`Shared Sub New`）。嵌套：无脚本内类型里的 `Shared` 方法；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTests.vb:6182`（`InvokeGenericSharedMethods`）（U10a-fix 换锚）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Shared Sub` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Method Parameters` | `type-members.md:585` | 不适用（分组小节：无产生式，其下 5 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^#### ' type-members.md` = 8 | — |
| `#### Value Parameters` | `type-members.md:617` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:748`（`TopLevelDeclareForms_Conform`）；源码 `:428-431`（`Declare … (h As System.IntPtr, t As String, …)` 的按值参数）。嵌套 方法 `ScriptModeConformanceTests.vb:347`（`TopLevelHandWrittenProperty_Conforms`）；源码 `:354`（`Set(newValue As Integer)` 访问器参数） | — |
| `#### Reference Parameters` | `type-members.md:650` | 已覆盖 | 已覆盖 | 顶层：**正向** 方法 `CommandLineRunnerTests.vb:1444`（**REPL 路径**；`TestByRefCopyOutDiscardsWriteBackInRepl`）；源码 `:1445`（脚本顶层 `Sub M(ByRef v As Integer)`，断言 `ByRef` 的 copy-out 语义：被调用方的写回被丢弃，`s(0)` 仍为 `10`）；**负向（裁定 5 算覆盖）** 方法 `:1219`（`TestByRefSpanParameterReportsRestrictedType`）；源码 `:1220`（脚本顶层 `Sub F(ByRef s As Span(Of Integer))`，断言 `BC31396`——该诊断消息**逐字含「ByRef 参数类型」**，即报点正是 `ByRef` 修饰符本身；同文件 `:1177` 的 **`ByVal`** 同型参数不报错，构成对照）；`Declare` 的 `ByRef` Out 参数：方法 `ScriptModeSubmissionConformanceTests.vb:770`（`TopLevelDeclareMarshalAs_Conform`）；源码 `:453`（`<Out> ByRef value As Integer`，脚本顶层 `Declare` 语句）。**依据改正（main 裁定 6）**：本行原先还引 `CommandLineRunnerTests.vb:1200`（顶层 `ByVal Span` **调用点实参**报 BC37052）与 `:1177`（**`ByVal`** 参数，正向）——两处**都不是** `ByRef` 形参本身（前者是实参、后者是 `ByVal`），已从依据中移除。嵌套：**缺口**——脚本内声明类型的成员位里无 `ByRef` 参数用例（`grep -n "ByRef" Scripting/VisualBasicTest/*.vb` 实测 **13 行**，**终态验证修复轮改正读数**：原文只列 10 条；完整清单：**脚本源码 5 行** = `CommandLineRunnerTests.vb:1220`/`:1445`/`:1446`/`:1221`（脚本顶层的 `ByRef` 形参声明与 `v = 99` 写回）、`ScriptModeSubmissionConformanceTests.vb:453`（顶层 `Declare` 的 `<Out> ByRef`）；**测试自身代码 2 行** = `CommandLineRunnerTests.vb:1445` 上下文的 `Sub M(ByRef v As Integer)` 与 `ImportsAccumulationFailureTests.vb:176` 的 `' GlobalImport.Parse(String, ByRef)` 注释；**散文/注释/`#Region` 6 行** = `CommandLineRunnerTests.vb:1100`/`:1173`/`:1216`/`:1359`/`:1439`、`ImportsAccumulationFailureTests.vb:41`。**原文漏列的两条（`:1220`/`:1445`）恰是本行赖以成立的脚本源码行**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTryCatchThrow.vb:263`（`TryCatchByRef`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `ByRef 形参` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#### Optional Parameters` | `type-members.md:739` |已覆盖| 已覆盖 | 检索（**本轮实测重跑**）：`grep -n "Optional " Scripting/VisualBasicTest/*.vb` → **14 行 / 7 文件**（`CommandLineRunnerTests.vb:92-95`、`ScriptModeConformanceTests.vb:65,78,96`、`InteractiveSessionReferencesTests.vb:19`、`NoNuGetZeroRegressionTests.vb:124`、`NuGetReferenceDirectiveNTests.vb:32`、`NuGetRestoreCoordinatorTests.vb:145,558`、`NuGetRuntimeHandshakeTests.vb:241`）；含 `Helpers\` 的递归口径为 **30 行 / 11 文件**。**全部命中都是宿主与测试自身的参数签名**（`Optional x As T = Nothing`；`CommandLineRunnerTests.vb:673` 是 `#Region` 标题），**无一写在脚本源码字符串内** ⇒ 两栏仍 `缺口`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\OptionalArgumentTests.vb:21`（`TestOptionalOnGenericMethod`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Optional 形参` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeStatementConformanceTests.vb:728`（`TopLevelOptionalParameters_Conform`）。| — |
| `#### ParamArray Parameters` | `type-members.md:767` |已覆盖| 已覆盖 | 检索（**本轮实测重跑**）：`grep -n "ParamArray" Scripting/VisualBasicTest/*.vb` → **8 行 / 5 文件**（`NativeLibraryProbeTests.vb:18`、`ScriptTests.vb:455,459,468`、`ScriptModeConformanceTests.vb:194,220`、`VbiCompileModeTests.vb:100`、`NuGetRestoreEngineTests.vb:20`）；含 `Helpers\` 的递归口径为 **9 行 / 6 文件**（多 `Helpers\ObjectFormatterTestBase.vb:30`）。**全部命中都是宿主与测试自身的 helper 签名**（`ParamArray args As String()`），**无一写在脚本源码字符串内** ⇒ 两栏仍 `缺口`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:5410`（`Imported_SpillCall2`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `ParamArray` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeStatementConformanceTests.vb:741`（`TopLevelParamArrayParameters_Conform`）。| — |
| `### Event Handling` | `type-members.md:820` | 已覆盖 | 缺口 | 本小节 = **`Handles` 子句**（`type-members.md:824` 的产生式 `HandlesClause`）。顶层：方法 `ScriptTopLevelCrashTests.vb:360`（`TopLevelHandlesClauseOnInstanceWithEventsField_Delivers`，源码 `:365` 脚本顶层 `Sub OnIt(...) Handles hooked.SomethingHappened`）、方法 `:379`（`TopLevelHandlesClauseOnSharedWithEventsField_Delivers`，源码 `:386` 的 `Shared` 变体）、**负向（裁定 5 算覆盖）** 方法 `:844`（`CrossSubmissionHandles_IsReportedInsteadOfTerminatingTheProcess`，源码 `:849`，跨提交报 BC37343）、方法 `:870`（`HostObjectWithEventsHandles_IsReportedInsteadOfTerminatingTheProcess`，源码 `:873`，宿主对象变体）。嵌套：**缺口**——`grep -n "Handles " Scripting/VisualBasicTest/*.vb` 的**全部**命中都是脚本顶层的 `Sub` 声明（`ScriptTopLevelCrashTests.vb:365`/`:386`/`:849`/`:873`/`:897`/`:923`），**无一处**的 `Handles` 子句写在脚本内声明类型的成员位。**依据改正（main 裁定 6 的全表扫描发现）**：原文的嵌套依据 `ScriptModeConformanceTests.vb:399`（`Custom Event` 的 `AddHandler`/`RemoveHandler` **访问器体**）属**另一个构造**——访问器体不是 `Handles` 子句，该依据分别由 §C.3.A 的 `### AddHandler and RemoveHandler Statements` 行与 `### Custom Events` 行承载，**不能**替本小节 | — |
| `### Extension Methods` | `type-members.md:906` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:671`（`<Extension> Shared Function Twice` + `"abc".Twice()`）；`CommandLineRunnerTests.vb:1587`（`.vbx` 里的 `Extension Property`）、`:1607`（`Extension Operator`）、`:1625`（类型参数 `Shared` 成员）、`:1650` / `:1667`（负向：无 `Imports` 报 BC30456 / REPL `Imports` 作用域）。arm：`Parser.vb:1029` 的 `<`（属性列表）+ `:1113` 的 contextual 说明符。嵌套：缺口 | — |
| `### Partial Methods` | `type-members.md:1117` |已覆盖| 已覆盖 | 检索：`grep -n "Partial Sub\|Partial Function\|Partial Private" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。注意 `Partial Class` 有覆盖（`ScriptModeConformanceTests.vb:655`），但**Partial 方法**无；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\PartialMethodsTest.vb:15`（`MergePartialMethodAndParameterSymbols`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 `Partial Private Sub M(i As Integer)` **1** 处（`:21`）；块内另 4 处 `Partial` 是 `:20`/`:29`/`:100`/`:109` 的 `Partial Public Class C1` **部分类型**修饰符，属另一构造，不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeStatementConformanceTests.vb:759`（`TopLevelPartialMethods_Conform`）。| — |
| `## Constructors` | `type-members.md:1186` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTopLevelCrashTests.vb:678`（`TopLevelInstanceConstructor_IsReportedInsteadOfTerminatingTheProcess`：`Sub New()` 在脚本顶层报 BC37342）；方法 `:696`（`TopLevelInstanceConstructorAfterStatement_IsReportedInsteadOfTerminatingTheProcess`，非首语句位置）；方法 `:713`（`TopLevelSharedConstructor_StillCompiles`，`Shared Sub New` 合法）；方法 `:724`（`ReplTopLevelInstanceConstructor_IsReportedAndTheSessionContinues`，REPL 变体）。嵌套 方法 `ScriptModeConformanceTests.vb:631`（`NestedGenericTypesAndConstraints_Conform`）；源码 `:635`（`Class Box` 内的 `Public Sub New()`） | — |
| `### Instance Constructors` | `type-members.md:1206` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:678`（`Sub New()` 写在脚本顶层报 BC37342）；`:696`（非首语句位置）；`:724`（REPL 变体）。嵌套 `ScriptModeConformanceTests.vb:631`（`Class Box` 内的 `Public Sub New()`，源码 `:635`） | 种子行读数：BC37342 为脚本容器特有诊断（见 `spec-scripting-dialect.md`） |
| `### Shared Constructors` | `type-members.md:1267` | 已覆盖 | 缺口 | 顶层 `ScriptTopLevelCrashTests.vb:713`（`TopLevelSharedConstructor_StillCompiles`）；`ScriptModeStatementConformanceTests.vb:447`（`ReleaseOptimizedFieldInitializers_Conform` 的 `Shared` 字段初始化器落到合成的共享构造）。嵌套：缺口 | — |
| `## Events` | `type-members.md:1426` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:279`（`Event E As System.EventHandler` 写在脚本顶层）；`:299`（`Shared Event`）；`ScriptModeStatementConformanceTests.vb:370`（顶层裸 `RaiseEvent` 报 BC30188）。嵌套 `ScriptTopLevelCrashTests.vb:360`（`RaiserAndHookupSource` 的 `Class Raiser` 内声明 `Event SomethingHappened`，源码 `:993`） | — |
| `### Custom Events` | `type-members.md:1563` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:399`（`Custom Event Changed` 三个访问器全写在脚本顶层）；`:427`（跨方法 `RaiseEvent`）；`:456`（lambda 内 `RaiseEvent`）。嵌套：缺口 | — |
| `#### Custom events in WinRT assemblies` | `type-members.md:1649` | 不适用（本平台无对应实现：本 fork 的脚本容器不消费 WinRT 事件投影） | 不适用（同上） | 检索：`grep -rn "WinRT" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**） | — |
| `## Constants` | `type-members.md:1691` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:325`（`TopLevelConstForms_Conform`：`Integer`/`Date`/`Decimal`/`String` 四种 `Const`，源码 `:327-330` 逐字写在脚本顶层，`:331` 断言求值结果为 `2041`）；`ScriptTopLevelCrashTests.vb:165`（`Const d As Date` 与 `Shared` 字段初始化器共存）。**顶层栏依据（U10a-fix2 复算，解析器实锤）**：`Parser.vb:2093` 的 `Select Case Context.BlockKind`（函数 `ParseVarDeclStatement` 声明于 `:2086`）在 `:2102` 列出 `SyntaxKind.CompilationUnit` ⇒ `:2103` `isFieldDeclaration = True` ⇒ `:2110`/`:2111` 走 `SyntaxFactory.FieldDeclaration`；即**脚本顶层的 `Const` 是脚本类的成员常量**，正对本小节（`type-members.md:1693`「a *constant* is a constant value that is a member of a type」）。嵌套：**缺口**——U10a-fix 换的锚 `Emit` `CodeGen\CodeGenConstLocal.vb:124`（`TestIntegerConstLocal`）所引源码块内 `Module C` 在 indent 0、两处 `const i`/`const j` 在 indent 8（即 `Sub Main()` 体内）⇒ 该块内的是**局部常量**，属 `statements.md:327` `## Local Declaration Statements`（该行 `LocalModifier : 'Static'\|'Dim'\|'Const'`）面，**不是本小节所指的类型成员常量** ⇒ 按裁定 6 回退 `缺口`。（原 U10a 审计读数「`Const（局部）` 出现 2 处」即该块内的两个局部常量，不构成本小节的依据。）| — |
| `## Instance and Shared Variables` | `type-members.md:1771` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:303`（`Dim`/`As New`/数组上界/`ReadOnly`）；`ScriptTopLevelCrashTests.vb:130`（`Shared sx`）。嵌套：无脚本内类型里的字段声明 | — |
| `### Read-Only Variables` | `type-members.md:1891` | 已覆盖 | 缺口 | 顶层 方法 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`）；源码 `:308`（`ReadOnly ro As Integer = 5`）；方法 `ScriptTopLevelCrashTests.vb:139`（`TopLevelSharedReadOnlyFieldInitializer_RunsAndTheSubmissionLoads`，`Shared ReadOnly sx`）。嵌套：缺口 | — |
| `### WithEvents Variables` | `type-members.md:1950` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:456`（`WithEvents r As New Raiser`）；`:471`（`Shared WithEvents`）；`:360`/`:379`（带 `Handles` 子句）；方法 `ScriptModeSubmissionConformanceTests.vb:237`（**REPL 路径**；跨提交 `WithEvents` + `AddHandler`）。嵌套 `ScriptTopLevelCrashTests.vb:918`（`SameSubmissionHandles_StillDelivers`） | 种子行读数：`ScriptTopLevelCrashTests.vb:360,379,456,471,918` |
| `### Variable Initializers` | `type-members.md:2019` | 已覆盖 | 缺口 | 顶层 `ScriptTopLevelCrashTests.vb:130`（`Shared sx As Integer = 5`）；`:179`（实例初始化器仍走 `<Initialize>` 路径）；`:151`（数组上界初始化器）。嵌套 `ScriptTopLevelCrashTests.vb:61` 的 `Class C` 字段初始化器是**负向**用例（`Await` 在嵌套类初始化器里报 BC36937），不构成正向覆盖 ⇒ 嵌套仍记缺口 | — |
| `#### Regular Initializers` | `type-members.md:2153` | 已覆盖 | 缺口 | 顶层 `ScriptTopLevelCrashTests.vb:179`（`Dim iy As Integer = 7`）；`ScriptModeStatementConformanceTests.vb:442`（`Shared sharedValue As Integer = 5`）。嵌套：缺口 | — |
| `#### Object Initializers` | `type-members.md:2188` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:570`（`NestedTypeDeclarations_Conform`）；源码 `:586`（`New Widget With {.Value = 2}`）；方法 `ScriptModeStatementConformanceTests.vb:256`（`TopLevelWithBlock_Conform`）；源码 `:262`（`New Pair With {.Left = 1, .Right = 2}`——**原文写成 `ScriptModeConformanceTests.vb:262` 的简写，实为 `ScriptModeStatementConformanceTests.vb:262`**）；方法 `ScriptModeSubmissionConformanceTests.vb:203`（**REPL 路径**；`ReplAnonymousTypesAcrossSubmissions_Conform`）；源码 `:205-206`（`New With {Key .Id = 1}`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenObjectInitializer.vb:10`（`ObjectInitializerAsRefTypeEqualsNew`）。理由：该行为与容器无关——对象初始化器是对象创建表达式的通用形式，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `With { 对象初始化器` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#### Array-Size Initializers` | `type-members.md:2212` | 已覆盖 | 缺口 | 顶层 方法 `ScriptTopLevelCrashTests.vb:151`（`TopLevelSharedArrayFieldUpperBound_RunsAndTheSubmissionLoads`，`Shared arr(2) As Integer` 的上界）；方法 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`）；源码 `:307`（`Dim arr(2) As Integer`）。嵌套：缺口 | — |
| `### System.MarshalByRefObject Classes` | `type-members.md:2266` | 不适用（描述性章节：只述基类约束的语义，无独立可写语法形态） | 不适用（同上） | 检索：`grep -rn "MarshalByRefObject" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**） | — |
| `## Properties` | `type-members.md:2274` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:338`（自动属性 + 属性初始化器）、`:347`（手写 `Get`/`Set`）、`:364`（带参默认属性）、`:381`（`ReadOnly`/`Shared` 属性）。嵌套：无脚本内类型里的属性；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests.vb:362`（`TestAttributesOnProperty`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Property 声明` 出现 **3** 处，位于该块声明的类型体内）| — |
| `### Get Accessor Declarations` | `type-members.md:2603` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:347`（源码 `:351-353` `Get … End Get`）；`:364`（源码 `:367-371`）。arm：`Parser.vb:838` 附近（`GetKeyword` → `ParsePropertyOrEventAccessor`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests.vb:613`（`TestAttributesOnPropertyAndGetSet`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Get 访问器` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Set Accessor Declarations` | `type-members.md:2664` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:347`（源码 `:354-356` `Set(newValue As Integer) … End Set`）；`:364`（源码 `:372-374`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests.vb:613`（`TestAttributesOnPropertyAndGetSet`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（与 `### Get Accessor Declarations` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Set 访问器` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Default Properties` | `type-members.md:2681` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:364`（`Default Property Indexer(i As Integer) As Integer` + `Indexer(1) = 7`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests.vb:4007`（`SpecialNameAttributeFromSource`）（U10a-fix 换锚）。理由：该行为与容器无关——默认属性的声明与索引访问是通用语言面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Default Property` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Automatically Implemented Properties` | `type-members.md:2778` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:338`（`Property Auto As Integer` / `Property AutoWithInit As String = "init"`）；`:381`（`Shared Property SharedValue`）。嵌套：缺口 | — |
| `### Iterator Properties` | `type-members.md:2822` |已覆盖| 已覆盖 | 检索：`grep -n "Iterator Property" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenIterators.vb:428`（`ReadonlyProperty`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Iterator Property` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeStatementConformanceTests.vb:788`（`TopLevelIteratorProperty_Conform`）。| — |
| `## Operators` | `type-members.md:2853` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`，`Structure Money` 写在脚本顶层）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_ObsoleteAttribute.vb:353`（`TestObsoleteAttributeOnOperators`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Operator 声明` 出现 **7** 处，位于该块声明的类型体内）| — |
| `### Unary Operators` | `type-members.md:2925` | 已覆盖 | 已覆盖 | 方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:538` `Public Shared Operator -(a As Money)`、`:553`/`:556` `IsTrue`/`IsFalse`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UserDefinedUnaryOperators.vb:55`（`Plus1`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Operator +(...)` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Binary Operators` | `type-members.md:2953` | 已覆盖 | 已覆盖 | 方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:535` `Operator +`、`:541`/`:544` `Operator =` / `Operator <>`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UserDefinedBinaryOperators.vb:17`（`BasicTest`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Operator 声明` 出现 **20** 处，位于该块声明的类型体内）| — |
| `### Conversion Operators` | `type-members.md:3004` | 已覆盖 | 已覆盖 | 方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:547` `Widening Operator CType`、`:550` `Narrowing Operator CType`、`:560` 调用点 `CType(2.5D, Money)`。arm：`ParseExpression.vb:1623`（`CTypeKeyword`/`DirectCastKeyword`/`TryCastKeyword`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenNullable.vb:2245`（`LiftedConversionHasValue`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Operator CType` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Operator Mapping` | `type-members.md:3067` | 不适用（描述性映射表：`Operator`↔元数据名，无独立可写语法形态） | 不适用（同上） | 检索：`sed -n '3067,3080p' type-members.md`（**实锤**：纯映射表） | — |
#### C.3.C `expressions.md`（82 行；**全量出行**，工作目录 `InternalDevDocs\vblang\spec\`）

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `## Expression Classifications` | `expressions.md:24` | 不适用（语义说明：分类规则，无 `antlr` 产生式） | 不适用（同上） | `sed -n '24,60p' expressions.md`（**实锤**：纯散文） | — |
| `### Expression Reclassification` | `expressions.md:60` | 不适用（语义说明：同上） | 不适用（同上） | `sed -n '60,185p' expressions.md`（**实锤**：纯散文） | — |
| `## Constant Expressions` | `expressions.md:185` | 不适用（语义分类：何为常量表达式的判定规则，非可写构造） | 不适用（同上） | `sed -n '185,222p' expressions.md`（**实锤**） | — |
| `## Late-Bound Expressions` | `expressions.md:222` | 已覆盖 | 已覆盖 | 顶层（**依据改正，main 裁定 6 的全表扫描发现**）：**负向（裁定 5 算覆盖）** 方法 `CommandLineRunnerTests.vb:923`（**REPL 路径**；`TestLateBoundMemberAccessDoesNotPrint`）；源码 `:924`（REPL 顶层 `Dim o As Object = New System.Text.StringBuilder() : o.Append("x")`——**这就是对 `Object` 接收者的晚期绑定成员访问本身**，断言 `BC30491` 且不打印值）。**前置条件（辅助，不单独作为覆盖）** 方法 `ScriptModeStatementConformanceTests.vb:611`（`TopLevelInferredField_IsObject_Conforms`：顶层推断字段绑定为 `Object`，晚期绑定路径由此可达）；`ScriptModeStatementConformanceTests.vb:56` 是**探针记录行**（`script-top-object-receiver-array`，**非测试方法**）不计入。**原文只引前置条件、把真正的晚期绑定用例放在「探针实测」列**（「种子行关联」）——按裁定 6「依据须指向该语法本身」已改。嵌套：无方法体内对 `Object` 接收者的晚期绑定用例；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenLateBound.vb:13`（`LateAccess`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `晚期绑定成员调用（Option Strict Off）` 出现 **3** 处，位于该块声明的类型体内）| — |
| `## Simple Expressions` | `expressions.md:297` | 不适用（分组小节：无产生式，其下 5 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' expressions.md` = 52 | — |
| `### Literal Expressions` | `expressions.md:311` | 已覆盖 | 已覆盖 | 顶层 `InteractiveSessionTests.vb:109`（`StatementExpressions_IntLiteral`）；方法 `CommandLineRunnerTests.vb:720`（**REPL 路径**；裸 `1 + 2`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:124`（`TestIntegerConstLocal`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内字面量 `100` 出现 **3** 处（`:9` 的 `const i as integer = 100`、`:10` 的 `(i + 100)`、`:14` 的 `(j + 100)`），原读数 **1** 只计 `const i as integer = 100` 一行的书写、不是字面量出现数；该行的 `Const` **语句**本身不是本小节构造，不计入）| — |
| `### Parenthesized Expressions` | `expressions.md:321` |已覆盖| 已覆盖 | 检索：`grep -nE '= *\(' Scripting/VisualBasicTest/*.vb` 全部命中为测试自身代码或方法调用，**无脚本源码里的括号表达式断言**（**实锤**）。arm：`ParseExpression.vb:274`（`OpenParenToken`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\Parenthesized.vb:14`（`Bug4353`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `(Nothing)` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:44`（`TopLevelParenthesizedExpressions_Conform`）。| — |
| `### Instance Expressions` | `expressions.md:331` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:478`（`TopLevelExplicitMe_IsReported`）；源码 `:474`（顶层 `Return Me.ToString()` 报 BC36966）；隐式 `Me` 正向：方法 `ScriptModeStatementConformanceTests.vb:462`（`TopLevelImplicitMe_Conforms`）。嵌套 方法 `ScriptModeStatementConformanceTests.vb:487`（`TopLevelExplicitMeInMethodBody_IsReported`）；源码 `:485`（`Function Describe()` 体内 `Me.ToString()`）。**`MyBase`/`MyClass` 已拆成 §C.3.E 的独立行**——原行把两者一并吸收，掩盖了它们在**方法体内**的零覆盖（见 §C.3.H 第 3 条） | — |
| `### Simple Name Expressions` | `expressions.md:341` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:654`（`TopLevelUndeclaredName_IsReported`；顶层未声明裸名报 BC30451）；嵌套 `:637`（`OptionExplicitOff_ConformsInMethodBody`；`Sub Probe` 内隐式局部可解析）。另 `ScriptModeStatementConformanceTests.vb:510`（`TopLevelMyNamespace_IsReported`）/`:517`（`TopLevelMyNamespaceComputer_IsReported`）（`My` 命名空间裸名解析） | — |
| `### AddressOf Expressions` | `expressions.md:394` | 已覆盖 | 已覆盖 | 顶层 `ScriptTests.vb:338`（`TestTopLevelRemoveHandler`，脚本顶层 `AddHandler Changed, AddressOf Handler`，源码 `:340-341`）；`ScriptModeSubmissionConformanceTests.vb:164`（`Dim act As System.Action = AddressOf madeTarget.Hit`，REPL 顶层）。嵌套：无方法体内的 `AddressOf`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenDelegateCreation.vb:212`（`ValidDelegateAddressOfTest`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `AddressOf` 出现 **3** 处，位于该块声明的类型体内）| 种子行读数：同左（原种子行 `AddressOf` 一格为 已覆盖/缺口） |
| `## Type Expressions` | `expressions.md:406` | 不适用（分组小节：其下 4 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' expressions.md` = 52 | — |
| `### GetType Expressions` | `expressions.md:419` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:781`（`NestedComImportInterface_Conform` 源码 `:468` `GetType(IUnknownLike).Name`）；方法 `InteractiveSessionTests.vb:36`（**REPL 路径**；`Imports_DoNotReplaceInheritedOptionsImports`）；源码 `:42`/`:44`（`GetType(Console).FullName`）。**锚点改正（终态验证修复轮）**：原文引 `InteractiveSessionTests.vb:25` 并注为 `GetType(Console).FullName`，但 `:25` 是 `Imports_CrossSubmission`（同为 `ContinueWith` 提交链 = REPL 路径；脚本是 `? builder.GetType().FullName`，源码 `:30`），与 `GetType(Console)` 无关。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenGetTypeOperator.vb:12`（`CodeGen_GetType_Simple`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `GetType(...)` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### TypeOf...Is Expressions` | `expressions.md:482` |已覆盖| 已覆盖 | 检索：`grep -n "TypeOf" Scripting/VisualBasicTest/*.vb` 仅 1 命中 `ScriptModeConformanceTests.vb:86`——那是**测试自身 helper** `AssertReports`（方法 `:78`，**非 `<Fact>`/`<Theory>` 测试方法**）体内的 `TypeOf failure Is CompilationErrorException`，**不在脚本源码字符串里**。故两容器皆缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenIfOperator.vb:130`（`Branchless_Operations`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `TypeOf...Is` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:64`（`TopLevelTypeOfIsExpressions_Conform`）。| 探针 `script-top-object-receiver-array`（`Dim o = arr` 后 `o.Length`）exit 0，证明晚期绑定本身可用，但**不覆盖 `TypeOf...Is`** |
| `### Is Expressions` | `expressions.md:492` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:702`（`TopLevelObjectOverrides_Conform`，源码 `:711` `other IsNot Nothing`）；`ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`；源码 `:338` `fixedArr Is Nothing`）。嵌套 `ScriptModeConformanceTests.vb:399`（访问器体内 `If _handlers IsNot Nothing Then`，源码 `:410`） | — |
| `### GetXmlNamespace Expressions` | `expressions.md:508` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptOptionsTests.vb:88`（`AddImports_XmlNamespaceImportsAreUsedByScript`）；源码 `:93`（`? GetXmlNamespace(p).NamespaceName`）；可复用 helper（**非测试方法**）`ScriptModeConformanceTests.vb:44`（`MsvbReference`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:2849`（`GetXmlNamespaceWithTrivia`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `GetXmlNamespace(...)` 出现 **3** 处，位于该块声明的类型体内）| — |
| `## Member Access Expressions` | `expressions.md:561` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`）；源码 `:305`（`text.Length` 等顶层成员访问）；方法 `CommandLineRunnerTests.vb:685`（**REPL 路径**；`TestBarePropertyAccessPrints`）、方法 `:974`（**REPL 路径**；`TestBareQualifiedPropertyPrints`）。嵌套 方法 `ScriptModeConformanceTests.vb:347`（`TopLevelHandWrittenProperty_Conforms`）；源码 `:352`（`Get` 访问器体内 `Return backing`） | — |
| `### Identical Type and Member Names` | `expressions.md:710` | 不适用（语义歧义规则：述「同名时取谁」的决议规则，无独立可写形态） | 不适用（同上） | `sed -n '710,738p' expressions.md`（**实锤**：纯散文） | — |
| `### Default Instances` | `expressions.md:738` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；`TestMidRedimWithDoNotPrint`：`With sb : .Append("x")` 的默认实例成员访问）；`ScriptModeStatementConformanceTests.vb:251`（`With` 块内 `.Append`/`.Left`）。**嵌套栏维持 `已覆盖`（U10a-fix 豁免，编译器测试指针）**：`Symbol` `SymbolsTests\Source\GroupClassTests.vb:279`（`GroupClassRefersToItself`）。理由：默认实例的**合成**是通用语言面，与脚本容器无关——所引源码块内实见的是**组类声明**（`:288` 的 `<Microsoft.VisualBasic.MyGroupCollection("DefaultInstanceTestBase8", "Create1", "Dispose1", "")>` 标在 `Class MyTests15` 上，`:279` 方法的源码块从 `Imports System` 起），测试断言的是**合成成员清单**（`:310-316` 的 `m_MyTests15` / `get_MyTests15` / `set_MyTests15` / `Property MyTests15.MyTests15`，逐项断言在 `:319-322`）——正对 `expressions.md:744`「The default instance of a class is represented by a **compiler-generated property**」。**U10a-fix2 复核**：该块内默认实例的**成员访问 0 处**（组类名 `MyTests15` 的 4 处命中全在声明与断言清单里）。U10a 判「无正向用例」有误：`SymbolsTests\Source\` 下的 .vb 由默认 glob 编入测试程序集，其 `<Fact>` 实际执行（U10a 只检索了产品源码与 `GroupClassTests` 词，未检索 `MyGroupCollection`）。（原 U10a 审计读数「`MyGroupCollection 组类声明` 出现 1 处」正确。）（U10a 审计 2026-09-17 复核：块内 `MyGroupCollection` **1** 处，即 `:288` 组类声明上的属性本身；该词无同词异义，判**确证相关**，但依据落在组类**声明**侧——块内默认实例的成员访问 0 处，见上）| — |
| `#### Default Instances and Type Names` | `expressions.md:791` | 不适用（子小节：由宿主小节 `### Default Instances` 覆盖，本行不另计） | 不适用（同上） | 检索：`sed -n '791,816p' expressions.md`（**实锤**：语义说明） | — |
| `#### Group Classes` | `expressions.md:816` | 不适用（描述性章节：LINQ 分组类的合成规则，非可写语法形态） | 不适用（同上） | `sed -n '816,898p' expressions.md`（**实锤**） | — |
| `### Extension Method Collection` | `expressions.md:898` | 已覆盖 | 缺口 | 顶层 `CommandLineRunnerTests.vb:1587`（`TestExtensionPropertyInScriptFile`）、`:1607`（`Extension Operator`）、`:1625`（类型参数 `Shared` 成员）、`:1650`（负向 BC30456）、`:1667`（REPL `Imports` 作用域）；`ScriptModeConformanceTests.vb:671`。嵌套：缺口 | — |
| `## Dictionary Member Access Expressions` | `expressions.md:1095` |已覆盖| 已覆盖 | 检索：`grep -nE '![A-Za-z_]' Scripting/VisualBasicTest/*.vb` → 命中全部为 `<>`/`!=` 类符号或测试自身代码，**脚本源码里的 `!x` 字典访问 0 命中**（**实锤**）。arm：`ParseExpression.vb:233`（`ExclamationToken`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenWithBlock.vb:3004`（`TestWith_DictionaryAccess_Simple`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `!A 字典成员访问` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:96`（`TopLevelDictionaryMemberAccess_Conforms`）。| — |
| `## Invocation Expressions` | `expressions.md:1130` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`，顶层裸调用）；方法 `ScriptTests.vb:204`（`TestCallStatementReturnValue`，`Call` 语句）；方法 `:198`（`TestExpressionStatementReturnValue`，表达式语句）。嵌套 方法 `ScriptModeStatementConformanceTests.vb:202`（`OnErrorInsideMethod_Conforms`）；源码 `:201`/`:202`（`Sub Swallow()` 体内调用 `CInt(…)`/`System.Console.WriteLine(bad)`；**原文引 `:212`，那两行是脚本顶层的 `Swallow()`/`Jump()` 调用，不在方法体内**） | — |
| `## Overloaded Method Resolution:` | `expressions.md:1182` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:516`（`TopLevelOverloads_Conform`）；`ScriptModeStatementConformanceTests.vb:606`（用重载决议判别顶层字段静态类型）；`:563`/`:583`（`Tell(o As Object)` / `Tell(i As Integer)` 重载）。嵌套：**缺口**——U10a-fix 换的锚 `Emit` `BreakingChanges.vb:618`（`TestCrossLanguageOptionalAndParamarrayForHandles`）所引源码块内 `Goo`/`Goo2` 共 **4** 处出现，**全部是 `Function` 声明行**（`Function Goo(x As String) Handles …`、`Function Goo(x As String, ParamArray y() As Integer) Handles …`、`Function Goo2(Optional x As String = "") Handles …`、`Function Goo2(x As String, y() As Integer) Handles …`），**块内无任何对 `Goo`/`Goo2` 的调用** ⇒ 没有发生重载挑选；块内非声明的 6 行全是 `Diagnostic(ERRID.ERR_EventHandlerSignatureIncompatible2, "ev")` 断言（`Goo` 3 条 + `Goo2` 3 条 = 逐方法 × 逐 `Handles` 目标），出自 `Handles` 委托绑定兼容检查，**不是重载决议** ⇒ 按裁定 6 回退 `缺口`。（U10a 候选 `Semantic` `Semantics\OverloadResolution.vb:61`（`BasicTests`）**未采用**：其源码块内只有空壳类，重载成员位于 `<Compile Remove>` 的夹具 `Semantics\OverloadResolutionTestSource.vb`，机械校验会判为错锚。）| — |
| `## Index Expressions` | `expressions.md:1185` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:364`（`TopLevelDefaultPropertyWithParameters_Conforms`）；源码 `:367`（`Default Property Indexer(i As Integer)`）、`:375`（`Indexer(1) = 7`）；方法 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`）；源码 `:330`（`fixedArr(0) = 7` 数组索引）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:5611`（`Imported_Array02`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内数组索引表达式 **10** 处（`:21`/`:22`/`:27`/`:28`/`:33`×2/`:34`/`:39`×2/`:40`）；原读数 **11** 多的 1 处是 `:15` `Dim arr(Await GetVal(3)) As Integer` 的**数组上界**，不是索引表达式）| — |
| `## New Expressions` | `expressions.md:1212` | 不适用（分组小节：其下 4 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' expressions.md` = 52 | — |
| `### Object-Creation Expressions` | `expressions.md:1235` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`；源码 `:306` `Dim created As New System.Text.StringBuilder`）；方法 `:570`（`NestedTypeDeclarations_Conform`；源码 `:586` `New Widget With {.Value = 2}`）。嵌套 方法 `ScriptModeConformanceTests.vb:631`（`NestedGenericTypesAndConstraints_Conform`）；源码 `:636`（`Class Box` 的 `Sub New()` 体内 `Item = New T()`；**原文引 `:648`，该行是脚本顶层的 `Dim madeBox As New Box(Of …)`——`Function FirstOf` 只到 `:647`**） | — |
| `### Array Expressions` | `expressions.md:1416` | 不适用（分组小节：其下 2 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^#### ' expressions.md` = 4 | — |
| `#### Array creation expressions` | `expressions.md:1420` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:314`（`TopLevelArrayInitializers_Conform`）；源码 `:316`（`Dim arr() As Integer = {1, 2, 3}`）；方法 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`）；源码 `:331`（`Dim dynamicArr() As Integer = {1, 2, 3}`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:6003`（`Imported_Array05`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `New Integer()... 数组创建` 出现 **5** 处，位于该块声明的类型体内）| — |
| `#### Array Literals` | `expressions.md:1469` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:314`（`TopLevelArrayInitializers_Conform`）；源码 `:316-317`（含交错数组 `{New Integer() {1}, New Integer() {2}}`）；方法 `ScriptModeStatementConformanceTests.vb:303`（`TopLevelLoops_Conform`）；源码 `:304`（`For Each text In {"a", "bb"}`；**原文把该处写成简写 `:92`——`ScriptModeConformanceTests.vb:92` 是 `AssertEmits` 的文档注释，与数组字面量无关**）；方法 `ScriptModeConformanceTests.vb:631`（`NestedGenericTypesAndConstraints_Conform`）；源码 `:650`（`FirstOf({1, 2, 3})`）。arm：`ParseExpression.vb:400`（`OpenBraceToken`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:6802`（`Imported_StackSpill_Operator_Compound02`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `{1, 2, 3, 4} 数组字面量` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Delegate-Creation Expressions` | `expressions.md:1518` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:570`（`NestedTypeDeclarations_Conform`，源码 `:588` `Dim madeTransform As Transform = Function(x) …`）；`ScriptModeStatementConformanceTests.vb:351`（`NestedClassEventAddRemoveHandler_Conforms`；源码 `:356` `Dim handler As System.EventHandler = Sub(s, e) count += 1`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenDelegateCreation.vb:212`（`ValidDelegateAddressOfTest`）（U10a-fix 换锚）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `AddressOf` 出现 **3** 处，位于该块声明的类型体内）| — |
| `### Anonymous Object-Creation Expressions` | `expressions.md:1665` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:203`（**REPL 路径**；`ReplAnonymousTypesAcrossSubmissions_Conform`，源码 `:205-206` `New With {Key .Id = 1}`）；`InteractiveSessionTests.vb:142`、`:162`（跨提交匿名类型同一性）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_Synthesized.vb:510`（`AnonymousTypes`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `New With { ... }` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## Cast Expressions` | `expressions.md:1821` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:560`（`CType(2.5D, Money)` / `CType(1D, Money)`）；方法 `CommandLineRunnerTests.vb:1305`（**REPL 路径**；`TestExplicitCTypeSpanToObjectAndInterface`）；方法 `:1328`（`TestStringConcatenationOfSpanReportsOperatorError`，`CStr` 拼接报错）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\Conversions.vb:3459`（`DirectCast1`）（U10a-fix 换锚）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 `DirectCast(...)` **31** 处；原词频 32 多的 1 处是 `:40` 被注释掉的 `'PrintResult(DirectCast(Nothing, System.Guid))`，不计入）| — |
| `## Operator Expressions` | `expressions.md:1867` | 不适用（分组小节：其下 3 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' expressions.md` = 52 | — |
| `### Operator Precedence and Associativity` | `expressions.md:1884` | 不适用（语义说明：优先级表，无独立可写形态） | 不适用（同上） | `sed -n '1884,1909p' expressions.md`（**实锤**） | — |
| `### Object Operands` | `expressions.md:1909` | 不适用（语义说明：`Object` 操作数的运行时决议规则） | 不适用（同上） | `sed -n '1909,1935p' expressions.md`（**实锤**） | — |
| `### Operator Resolution` | `expressions.md:1935` | 不适用（语义说明：重载决议规则） | 不适用（同上） | `sed -n '1935,2041p' expressions.md`（**实锤**） | — |
| `## Arithmetic Operators` | `expressions.md:2041` | 不适用（分组小节：其下 8 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' expressions.md` = 52 | — |
| `### Unary Plus Operator` | `expressions.md:2061` |已覆盖| 已覆盖 | 检索：`grep -nE '= *\+[A-Za-z0-9]' Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`ParseExpression.vb:80`（一元运算符 arm）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UserDefinedUnaryOperators.vb:55`（`Plus1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `### Unary Operators` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17 复核：所引源码块内一元 `+` **2** 处——`:8` 的 `Operator +(x As C1) As C1` 声明与 `:16` 的 `Dim y = +x` 用法，原读数只记了声明）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:116`（`TopLevelUnaryPlusOperator_Conforms`）。| — |
| `### Unary Minus Operator` | `expressions.md:2079` | 已覆盖 | 已覆盖 | 方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:538` `Return New Money With {.Amount = -a.Amount}`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenNullable.vb:18`（`LiftedIntrinsicNegationLocal`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内一元 `-` **3** 处（`:6` `Dim y = -x`、`:9` `y = -x`、`:11` `y = -New Long?()`）；原读数 **4** 是两套模式对 `:11` 一行的重复计数）| — |
| `### Addition Operator` | `expressions.md:2102` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:720`（**REPL 路径**；`TestBareArithmeticExpressionPrints`：REPL 顶层 `1 + 2`）；`ScriptTests.vb:241`（顶层 `total = total + stepValue`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:220`（`Simple_Test`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内二元 `+` **2** 处（`:7` 字符串 `+`、`:20` 的 `Return x + y`）；原读数 **1** 只计了 `x + y` 一处）| — |
| `### Subtraction Operator` | `expressions.md:2146` |已覆盖| 已覆盖 | 检索：`grep -nE '[^<>=!-]- [A-Za-z0-9]' Scripting/VisualBasicTest/*.vb` 命中均为测试自身代码，**脚本源码里无独立减法断言**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\BinaryOperators.vb:503`（`Test7`）（U10a-fix 换锚）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `二元 -` 出现 **9** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:141`（`TopLevelSubtractionOperator_Conforms`）。| — |
| `### Multiplication Operator` | `expressions.md:2188` |已覆盖| 已覆盖 | 检索：`grep -nE '[^*]\* ' Scripting/VisualBasicTest/*.vb` 命中为注释分隔线，脚本源码里无独立乘法断言（**实锤**）。注：`Side * Side` 在 方法 `ScriptModeConformanceTests.vb:606`（`NestedInheritanceHierarchy_Conforms`）；源码 `:622`（`Function Area` 体内），但断言的是 `Implements` 而非乘法（**原文引 `:488`，该行是 `TopLevelAsyncSubAndFunction_Conform` 的 `"End Sub"`**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenIfOperator.vb:766`（`LambdaAsArgument_1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `z * z` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:158`（`TopLevelMultiplicationOperator_Conforms`）。| — |
| `### Division Operators` | `expressions.md:2228` |已覆盖| 已覆盖 | 检索：`grep -nE '/ ' Scripting/VisualBasicTest/*.vb` 命中为测试自身路径拼接，脚本源码里无 `/` / `\` 除法断言（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTuples.vb:11159`（`UnifyUnderlyingWithTuple_08`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 ` / 与 \ 除号` 出现 **4** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:177`（`TopLevelDivisionOperators_Conform`）。| — |
| `### Mod Operator` | `expressions.md:2303` | 已覆盖 | 已覆盖 | 方法 `ScriptModeStatementConformanceTests.vb:138`（`TopLevelContinueStatements_Conform`）；源码 `:137` `If i Mod 2 = 0 Then Continue For`。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenNullable.vb:1399`（`NothingOrZeroInBinaryExpression`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Mod` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Exponentiation Operator` | `expressions.md:2343` |已覆盖| 已覆盖 | 检索：`grep -nE '\^' Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:870`（`BadConstantValue_3`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `^` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:197`（`TopLevelExponentiationOperator_Conforms`）。| — |
| `## Relational Operators` | `expressions.md:2377` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:735`（**REPL 路径**；`TestBareComparisonExpressionPrints`：REPL 顶层 `x > 5`）；方法 `ScriptModeStatementConformanceTests.vb:549`（`OptionCompareText_TakesEffect`）；源码 `:548`（顶层 `"A" = "a"`）；方法 `ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`）；源码 `:542`（`Operator =` 体内的 `a.Amount = b.Amount`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:8070`（`FieldReuseOnStatementLevel`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 ` > 关系运算符` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## Like Operator` | `expressions.md:2446` |已覆盖| 已覆盖 | 检索：`grep -n "Like " Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`ParseExpression.vb:1199` 附近无独立 `Like` arm（由 `ParseRelationalExpression` 通用路径处理）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:508`（`Error_ErrorHandler_InVBCore_LikeOperator`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Like` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:216`（`TopLevelLikeOperator_Conforms`）。| 种子行探针：`like-operator-with-option-compare` 脚本读数 `L=True N=False`（无对照，不计入 `已覆盖`） |
| `## Concatenation Operator` | `expressions.md:2500` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:763`（**REPL 路径**；`TestBareStringConcatenationPrints`：REPL 顶层 `"a" & "b"`）；`ScriptTests.vb:241`（顶层 `log &= i` 等）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenStringConcat.vb:12`（`Concat001`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 ` & 连接符` 出现 **3** 处，位于该块声明的类型体内）| — |
| `## Logical Operators` | `expressions.md:2534` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:237`（`TopLevelUsingAndSyncLock_Conform`，源码 `:244` `locked = locked AndAlso True`）。嵌套 方法 `ScriptModeConformanceTests.vb:399`（`TopLevelCustomEvent_AccessorsAndRegistration_Conform`）；源码 `:410`（`RaiseEvent` 访问器体内的 `If _handlers IsNot Nothing Then _handlers(sender, e)`）。**状态改正（main 裁定 4）**：原文依据已指向访问器体（属嵌套容器）却把状态记作 `缺口`，**证据与状态矛盾**，本轮改 `已覆盖` | — |
| `### Short-circuiting Logical Operators` | `expressions.md:2624` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeStatementConformanceTests.vb:237`（`TopLevelUsingAndSyncLock_Conform`；源码 `:244` `AndAlso`）；嵌套 `ScriptModeConformanceTests.vb:399`（`RaiseEvent` 访问器体内 `IsNot` + `AndAlso`，源码 `:410`）。arm：`ParseExpression.vb:80`（`NotKeyword` 一元） | — |
| `## Shift Operators` | `expressions.md:2728` |已覆盖| 已覆盖 | 检索：`grep -nE '<<\|>>' Scripting/VisualBasicTest/*.vb` 命中为 `<>` 与泛型箭头外的比较符，**无 `<<` / `>>` 移位断言**（**实锤**）。arm：`ParseStatement.vb:1199` 的 `LessThanLessThanEqualsToken` / `GreaterThanGreaterThanEqualsToken`（复合赋值形态）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTests.vb:4097`（`ShiftMask`）（U10a-fix 换锚）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `>> 与 <<` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeOperatorConformanceTests.vb:237`（`TopLevelShiftOperators_Conform`）。| — |
| `## Boolean Expressions` | `expressions.md:2764` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:338`（`TopLevelAutoProperties_Conform`，源码 `:342` `Return Auto & "\|" & AutoWithInit`）；`CommandLineRunnerTests.vb:532`（`TestTopLevelAddHandlerInScriptFile`，源码 `:541` `Dim ran As Boolean = False`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenNullable.vb:3160`（`LogicalOperationsWithNullableEnum`）（U10a-fix 换锚）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a-fix 后与 `## Shift Operators` 行**不再共用证据**：该行已换锚为 `ShiftMask`）（U10a 审计 2026-09-17：所引源码块内 `Not / And / Or / Xor` 出现 **4** 处，位于该块声明的类型体内）| — |
| `## Lambda Expressions` | `expressions.md:2832` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeSubmissionConformanceTests.vb:677`（`ThreeLevelNestedLambdasAwaitAndYield_Conform`，三层嵌套 lambda + `Async`/`Iterator`）；方法 `ScriptModeStatementConformanceTests.vb:351`（`NestedClassEventAddRemoveHandler_Conforms`）；源码 `:356`（顶层 `Sub(s, e) count += 1`）；方法 `ScriptTopLevelCrashTests.vb:340`（`TopLevelInstanceEvent_RaiseInLambda_ReachesTheHandler`）；源码 `:347`（顶层 `Dim raiseIt As System.Action = Sub() RaiseEvent ...`）。嵌套 方法 `ScriptModeSubmissionConformanceTests.vb:721`（`NestedLambdasInsideIteratorMethod_Conform`，`Iterator Function Walk` 体内声明 `Async Function` lambda 与 `Sub()` lambda）；方法 `:705`（`ThreeLevelNestedLambdaWithAwaitInNonAsyncContext_IsReported`，三层嵌套 + 非 async 上下文 `Await` 报 BC30800）。arm：`ParseExpression.vb:405`（`SubKeyword`/`FunctionKeyword`） | — |
| `### Closures` | `expressions.md:3031` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:351`（`NestedClassEventAddRemoveHandler_Conforms`）；源码 `:356`（lambda 捕获顶层字段 `count`）；方法 `ScriptTopLevelCrashTests.vb:340`（`TopLevelInstanceEvent_RaiseInLambda_ReachesTheHandler`）；源码 `:347`（lambda 捕获 `count`）。嵌套：无「方法体内 lambda 捕获局部」的脚本用例；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenClosureLambdaTests.vb:1284`（`SimpleGenericClosureWithParamsAndTemp`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `lambda 捕获外部变量` 出现 **1** 处，位于该块声明的类型体内）| — |
| `## Query Expressions` | `expressions.md:3176` |已覆盖| 已覆盖 | **依据改正（U10b 复核，**实锤**）**：原文 `grep -nE "Group By\|Order By\|Aggregate\|Distinct\|From .* In " …` → `0 命中` 是**假阴性**——`-E`（ERE）模式下 `\|` 是**字面竖线**，该模式实际在找含字面 `Group By\|Order By\|…` 的整串，故必得 0 命中（根因实测重跑：把 `-E` 去掉、或按 ERE 规则改用不转义的交替符重写，均正常命中）。实测命中 `ScriptModeTopLevelInferenceTests.vb:62`（`Private Const GroupingQuery` 的字符串字面量，`grep -nE "Group By\|…" Scripting/VisualBasicTest/*.vb` 的**实锤**读数），该常量在 `:156` 被用作脚本源码 ⇒ **顶层查询在该轮之前就已覆盖**（方法 `ScriptModeTopLevelInferenceTests.vb:153`（`TopLevelQueryWithAsClause_Evaluates`），`<Fact>` 在 `:152`）。arm：`ParseExpression.vb:198`（`IdentifierToken` 的 `FromKeyword`/`AggregateKeyword` → `ParsePotentialQuery`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:2678`（`From1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `From 查询子句` 出现 **11** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:57`（`TopLevelQueryExpression_Conforms`）（本格给出**独立**锚点：`Dim sorted As … = From n In nums Order By n Select n`；上方那条既有锚点是 `Group By` 形态）| — |
| `### Range Variables` | `expressions.md:3218` |已覆盖| 已覆盖 | 检索：同上（LINQ 查询语法全族 0 命中，**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:14080`（`RangeVariableNameInference1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `From 查询子句` 出现 **15** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:75`（`TopLevelRangeVariables_Conform`）。| — |
| `### Queryable Types` | `expressions.md:3268` |已覆盖| 已覆盖 | 检索：同上。另：`ScriptModeStatementConformanceTests.vb:56` 是**探针记录行（非测试方法）**，其中记载的 `BC36593` 读数证明**顶层推断字段**不可查询，属顶层容器的事实；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:2678`（`From1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `## Query Expressions` 行**全表同一条证据，不重复计入统计**；`IQueryable` 源与可查询性由 `QueryExpressions.vb` 整族覆盖）（U10a 审计 2026-09-17：所引源码块内 `From 查询子句` 出现 **11** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:95`（`TopLevelQueryOverQueryableSource_Conforms`）；方法 `:111`（`TopLevelQueryOverNonQueryableSource_IsReported`）。| 探针 `script-top-infer-linq` 自报 `BC36593`（表达式「`Object`」不可查询） |
| `### Default Query Indexer` | `expressions.md:3365` |已覆盖| 已覆盖 | 检索：同上；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:13577`（`DefaultQueryIndexer1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `### Default Properties` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17 复核：所引源码块内默认查询索引器的**调用** `xx1(1)` **1** 处（`:79`）；块内 4 处 `ElementAtOrDefault`（`:10`/`:20`/`:38`/`:64`）全是索引器**声明**，不计入调用数）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:129`（`TopLevelDefaultQueryIndexer_Conforms`）。| — |
| `### From Query Operator` | `expressions.md:3389` |已覆盖| 已覆盖 | 检索：同上；另 `CommandLineRunnerTests.vb` 的 `From` 命中均为 `Task.FromResult` 等 CLR API，非查询语法（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:6815`（`From5`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `From 查询子句` 出现 **55** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:146`（`TopLevelFromQueryOperator_Conforms`）。| — |
| `### Join Query Operator` | `expressions.md:3475` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:7234`（`Join1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Join` 出现 **36** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:165`（`TopLevelJoinQueryOperator_Conforms`）。| — |
| `### Let Query Operator` | `expressions.md:3553` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:6144`（`Let3`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Let` 出现 **22** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:184`（`TopLevelLetQueryOperator_Conforms`）。| 种子行探针：`linq-let-and-multiple-from`（有读数、无对照，`run3.py`） |
| `### Select Query Operator` | `expressions.md:3606` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:1947`（`Select1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内查询 `Select` 子句 **8** 处（`:47`×2/`:50`×2/`:56`/`:58`/`:60`/`:62`）；`Select` 原始词频 10，多的 2 处分别是方法名 `Public Function [Select](…)`（`:6`）与字符串 `"Select"`（`:8`），不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:201`（`TopLevelSelectQueryOperator_Conforms`）。| — |
| `### Distinct Query Operator` | `expressions.md:3684` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:4079`（`Distinct2`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内查询 `Distinct` 子句 **3** 处（`:20` 1 处、`:22` 2 处）；原始词频 5，多的 2 处分别是方法声明 `Public Function Distinct() As QueryAble`（`:11`）与字符串 `"Distinct"`（`:12`），不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:220`（`TopLevelDistinctQueryOperator_Conforms`）。| 种子行探针：`aggregate-and-distinct-in-top-level`（有读数、无对照，`run3.py`） |
| `### Where Query Operator` | `expressions.md:3728` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:3011`（`Where1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内查询 `Where` 子句 **1** 处（`From s In q Where s`）；块内另 1 处 `Where` 是同名**方法声明** `Public Function Where(x As Func(Of Integer?, Boolean?)) As QueryAble`（夹具 `QueryAble` 类内），不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:234`（`TopLevelWhereQueryOperator_Conforms`）。| 种子行探针：`top-level-typed-dim-feeds-linq` 读数 `Q=2,4` |
| `### Partition Query Operators` | `expressions.md:3784` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:4276`（`SkipTake2`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内分区查询子句 **3** 处（`:26` 的 `Skip #12:00:00 AM#`、`:28` 的 `Take 1`、`:30` 的 `Take 2`）；原始词频 7，多的 4 处是方法声明 `Public Function Skip(count As Date)`（`:11`）/`Public Function Take(count As Integer)`（`:16`）各 1 处与字符串 `"Skip {0}"` 2 处，不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:251`（`TopLevelPartitionQueryOperators_Conform`）。| — |
| `### Order By Query Operator` | `expressions.md:3847` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:4482`（`OrderBy1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Order` 出现 **11** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:268`（`TopLevelOrderByQueryOperator_Conforms`）。| — |
| `### Group By Query Operator` | `expressions.md:3932` |已覆盖| 已覆盖 | 检索：同上（**实锤**）。**顶层特性（非缺陷，实锤）**：顶层 `Dim q = <查询>` 不推断 ⇒ `q` 为 `Object` ⇒ 晚期绑定找不到 `Select`/`Count`；判定见 `design-overview.md` §3 与 `design-detailed.md` §U1；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ExpressionTrees\CodeGenExprLambda.vb:6903`（`QueryGroupBy`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 `Group By` 子句 **2** 处（`Group x = a By a Into Group`、`Group By a Into Group`）；原始词频 4，另 2 处 `Group` 是子句内的 `Into Group`，不计入子句数）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:286`（`TopLevelGroupByQueryOperator_Conforms`）。| 种子行探针：`r5/pin.py` 五路（嵌套实例方法/共享方法/模块 + 顶层 `Sub`/共享 `Sub`）均 exit 0、`G=2:2,1:1,3:1` |
| `### Aggregate Query Operator` | `expressions.md:4065` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:11532`（`Aggregate1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Aggregate` 出现 **13** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:304`（`TopLevelAggregateQueryOperator_Conforms`）。| 种子行探针：`aggregate-and-distinct-in-top-level`（`run3.py`） |
| `### Group Join Query Operator` | `expressions.md:4129` |已覆盖| 已覆盖 | 检索：同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\QueryExpressions.vb:9797`（`GroupJoin1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Group Join` 出现 **19** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:322`（`TopLevelGroupJoinQueryOperator_Conforms`）。| — |
| `## Conditional Expressions` | `expressions.md:4187` |已覆盖| 已覆盖 | 检索：`grep -nE 'If\(' Scripting/VisualBasicTest/*.vb` → 命中均为**测试自身**的 `Assert.True(` / `Assert.Contains(` 形式，**脚本源码里的 `If(cond, a, b)` 三元条件表达式 0 命中**（**实锤**）。arm：`ParseExpression.vb:377`（`IfKeyword`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenIfOperator.vb:12`（`Branchless_Compare`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `If(...) 条件表达式` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:343`（`TopLevelConditionalExpression_Conforms`）。| — |
| `## XML Literal Expressions` | `expressions.md:4219` |已覆盖| 已覆盖 | 检索：`grep -nE '<[A-Za-z][A-Za-z0-9]*>' Scripting/VisualBasicTest/*.vb` 与 `grep -n "<%" Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（**实锤**）。arm：`ParseExpression.vb:278`（`LessThanToken`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:45`（`XDocument`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `XML 文档字面量` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:367`（`TopLevelXmlElementLiteral_Conforms`）。| 种子行探针：顶层 `X=1,2 V=a` / `E=4`；嵌套 `X=a`（`run2.py` / `run3.py`） |
| `### Lexical rules` | `expressions.md:4254` |已覆盖| 不适用（词法子节：本小节只有 `XMLCharacter`/`XMLString`/`XMLWhitespace` 三个词法产生式，没有可独立书写的语法构造，其行为由父行 `## XML Literal Expressions` 承载） | 检索：同 `## XML Literal Expressions`（**实锤**）；**嵌套栏改判 `不适用`（U10a）**：理由——词法子节：本小节只有 `XMLCharacter`/`XMLString`/`XMLWhitespace` 三个词法产生式，没有可独立书写的语法构造，其行为由父行 `## XML Literal Expressions` 承载。检索证据：`sed -n '4254,4272p' expressions.md` → 输出为 `antlr` 词法产生式块（无语句/表达式产生式）。；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:387`（`TopLevelXmlLiteralLexicalRules_Conform`）。| — |
| `### Embedded expressions` | `expressions.md:4346` |已覆盖| 已覆盖 | 检索：`grep -n "<%=" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:1595`（`ImplicitXmlnsAttributes_EmbeddedExpressionOutsideContent`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `<%= %> 内嵌表达式` 出现 **3** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:408`（`TopLevelXmlEmbeddedExpressions_Conform`）。| — |
| `### XML Documents` | `expressions.md:4381` |已覆盖| 已覆盖 | 检索：同 `## XML Literal Expressions`（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:45`（`XDocument`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `## XML Literal Expressions` 行**全表同一条证据，不重复计入统计**；`<?xml …?>` 声明由 `XDocument` 字面量语料覆盖）（U10a 审计 2026-09-17：所引源码块内 `XML 文档字面量` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:426`（`TopLevelXmlDocumentLiteral_Conforms`）。| — |
| `### XML Elements` | `expressions.md:4492` |已覆盖| 已覆盖 | 检索：同 `## XML Literal Expressions`；`XElement`/`XDocument` 命名在测试语料里仅出现于 NuGet/Roslyn API 路径（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:188`（`MemberAccess`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与该行 `## XML Member Access Expressions` 的用法**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `XML 元素字面量` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:443`（`TopLevelXmlElementShapes_Conform`）。| 种子行探针：顶层 `X=1,2 V=a` / `E=4` |
| `### XML Namespaces` | `expressions.md:4634` |已覆盖| 已覆盖 | 检索（**本轮实测重跑**）：`grep -n "xmlns" Scripting/VisualBasicTest/*.vb` → **17 行 / 3 文件**（`CommandLineRunnerTests.vb:292`、`ImportsAccumulationFailureTests.vb:55,56,57,64,65,68,79,136,145,157,219,220`、`ScriptOptionsTests.vb:66,68,74,91`）。**逐条重判**：其中 `ImportsAccumulationFailureTests.vb:55-57,64-65,68` 的 `Imports <xmlns:p="urn:test">` **确属脚本文本**（经 `VisualBasicScript.Create`/`ContinueWith` 真编译，方法 `:232` `ReplayPath_AcceptedClauseForms_DoNotThrow` 断言语料过宿主门），`:79` 属**负向**集合（方法 `:242` 断言被门拒绝）；其余是宿主自身代码（`CommandLineRunnerTests.vb:292` 的 `/Imports:<xmlns:…>` 开关、`ScriptOptionsTests.vb:66/68/74/91` 的 `ScriptOptions.Imports` 文本）。**但本行（`expressions.md:4634`：XML **字面量元素**内的 `xmlns` 属性名）与 `Imports <xmlns:…>`（**导入子句**面）不是同一构造**：脚本源码里 **XML 字面量的 `xmlns` 属性仍零覆盖**；导入子句的可达性已由 `### GetXmlNamespace Expressions` 行（方法 `ScriptOptionsTests.vb:88`）承载 ⇒ 本行维持 `缺口`/`缺口`（**结论性重判**，非读数改正）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:132`（`AttributeNamespace`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（`xmlns:` 属性名即 XML 命名空间字面量形态）（U10a 审计 2026-09-17 复核：所引源码块内**元素字面量里的 `xmlns` 属性** **5** 处；原始词频 6 多的 1 处是 `Imports <xmlns="http://roslyn/default1">` 的**导入子句**，属另一构造（与 §C.3.H 第 1 条对 `Imports <xmlns…>` 的同一处置），不计入）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:461`（`TopLevelXmlLiteralNamespaces_Conform`）。| — |
| `### XML Processing Instructions` | `expressions.md:4740` |已覆盖| 已覆盖 | 检索：`grep -n "?>" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:45`（`XDocument`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `## XML Literal Expressions` 行**全表同一条证据，不重复计入统计**；处理指令 `?>` 由 `XDocument` 字面量语料覆盖）（U10a 审计 2026-09-17 复核：所引源码块内 XML **处理指令** **3** 处（`:5` `<?p?>`、`:8` `<?q?>`、`:10` `<?r?>`）；原始词频 4 多的 1 处是 `:3` 的 `<?xml version="1.0"?>`——`expressions.md` 的 `XMLProcessingTarget` 产生式逐字排除 `xml`，它是 **XML 声明**、属 `### XML Documents` 面，不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:479`（`TopLevelXmlProcessingInstructionLiterals_Conform`）。| — |
| `### XML Comments` | `expressions.md:4758` |已覆盖| 已覆盖 | 检索：`grep -n "<!--" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:18`（`XComment`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `<!-- --> XML 注释` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:497`（`TopLevelXmlCommentLiterals_Conform`）。| — |
| `### CDATA sections` | `expressions.md:4773` |已覆盖| 已覆盖 | 检索：`grep -n "CDATA" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:2773`（`CDATAContent`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `CDATA 段` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:516`（`TopLevelXmlCDataLiterals_Conform`）。| — |
| `## XML Member Access Expressions` | `expressions.md:4787` |已覆盖| 已覆盖 | 检索：`grep -nE '\.@[A-Za-z]\|\.\.\.<' Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`ParseExpression.vb:1048`（XML 轴分派：`AtToken` / `LessThanToken` / `DotToken`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:188`（`MemberAccess`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `### XML Elements` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `.<x> 与 .@x 轴` 出现 **6** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeQueryAndXmlConformanceTests.vb:535`（`TopLevelXmlMemberAccessAxes_Conform`）。| 种子行探针：轴 `.@id` 有读数（`run2.py`） |
| `## Await Operator` | `expressions.md:4913` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:364`（`TestTopLevelAwaitReturnValue`）、方法 `:375`（`TestTopLevelAwaitInStatement`）、方法 `:387`（`TestTopLevelBareAwaitStatement`）；方法 `ScriptModeSubmissionConformanceTests.vb:283`（REPL 顶层 `? Await pending`）。嵌套 方法 `ScriptModeConformanceTests.vb:484`（`TopLevelAsyncSubAndFunction_Conform`）；源码 `:487`（`Async Sub FireAndForget` 体内 `Await`）；方法 `ScriptModeStatementConformanceTests.vb:30`（`TopLevelAwaitInBlocks_Conform`，六种块内 `Await`）；方法 `:68`（`TopLevelAwaitInFinally_IsReported`，`Finally` 内报 BC36943） | — |
#### C.3.D `lexical-grammar.md`（18 行）与 `preprocessing-directives.md`（6 行）

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `## Characters and Lines` | `lexical-grammar.md:31` | 不适用（分组小节：其下 4 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' lexical-grammar.md` = 12 | — |
| `### Line Terminators` | `lexical-grammar.md:41` | 不适用（与容器无关：词法规则，脚本单元与普通单元同规，无独立可写形态） | 不适用（同上） | 检索：`grep -c "vbCrLf" Scripting/VisualBasicTest/*.vb` 广泛命中（测试源码即以此构造多行脚本输入），但**无针对行终止符本身语义的测试方法** | — |
| `### Line Continuation` | `lexical-grammar.md:56` | 已覆盖 | 已覆盖 | 顶层 方法 `InteractiveSessionTests.vb:101`（`StatementExpressions_LineContinuation`）；方法 `CommandLineRunnerTests.vb:907`（**REPL 路径**；`TestMultiLineContinuationBareExpressionPrints`）；源码 `:908`（脚本源码里的**显式** `_` 续行：`1 + 2 _`）；嵌套 方法 `ScriptModeStatementConformanceTests.vb:426`（`ReleaseOptimizedIteratorAndAsync_Conform`）；源码 `:432-435`（脚本源码里跨行书写 **lambda 体**、按列对齐、**无** `_`——**原文把该处误记在 `ScriptModeConformanceTests.vb:432`，该行实为 custom event 的 `AddHandler` 访问器，与续行无关**）。**栏位改正（终态验证修复轮）**：`:432-435` 的续行书写在 `Async Function()` **lambda 体内**，按 `README.md` §四 两栏定义属**嵌套容器**，故从顶层栏移到本栏（顶层栏另有两处干净锚点 `InteractiveSessionTests.vb:101`、`CommandLineRunnerTests.vb:907`，顶层 `已覆盖` 结论不受影响）。嵌套栏 `缺口` → **`已覆盖`**：按本条口径重判，`:432-435` 是**嵌套容器里实际的续行书写**、构成正向覆盖（与裁定 4 / R5 同类的「证据与状态一致性」改正；原 `缺口` 是漏判） | — |
| `### White Space` | `lexical-grammar.md:167` | 不适用（与容器无关：词法规则，空白不入语法树） | 不适用（同上） | `sed -n '167,179p' lexical-grammar.md`（**实锤**：纯词法规则） | — |
| `### Comments` | `lexical-grammar.md:179` |已覆盖| 已覆盖 | 检索：`grep -nE "^\s*'" Scripting/VisualBasicTest/*.vb` 命中全部为**测试文件自身**的注释行；脚本源码字符串里**无 `'` 或 `REM` 注释**（`grep -n "REM " …` → 0 命中，**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:353`（`Error_ErrorHandler_WithMissingOrIncorrectLabels`）（U10a-fix 换锚）。理由：该行为与容器无关——注释是词法 trivia，写在方法体内不改变任何绑定语义，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `' 注释行` 出现 **3** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:27`（`TopLevelComments_Conform`）。| — |
| `## Identifiers` | `lexical-grammar.md:200` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:241`（`TestTopLevelExecutableStatements`；源码 `:243` `Dim total = 1` 等标识符声明与引用）。嵌套 方法 `ScriptModeStatementConformanceTests.vb:568`（`OptionInferOff_MakesLocalObject_Conforms`）；源码 `:574`（`Sub Probe()` 体内 `Dim inferred = 1` 的标识符声明）与 `:575`（同一方法体内的标识符引用）；**另一条嵌套依据（main 裁定 1 认可）** 方法 `ScriptModeConformanceTests.vb:570`（`NestedTypeDeclarations_Conform`）；源码 `:576`（脚本内 `Structure Point` 的成员名 `Public X As Integer`——**该成员位于脚本内声明类型的成员位，按裁定 1 属嵌套容器**；注意 `:575` 的 `Structure Point` **类型声明本身**仍属顶层容器，两者不是同一栏。**锚点改正（终态验证修复轮）**：原文写 `:575`，那是**类型声明行**、不是成员行） | — |
| `### Type Characters` | `lexical-grammar.md:287` |已覆盖| 已覆盖 | 检索：`grep -nE '\w\$\|\w%\|\w&' Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（**实锤**）。VB 类型字符（`$`/`%`/`&`/`!`/`#`/`@`）在脚本容器内零覆盖；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Binding\ImplicitVariableTests.vb:52`（`SimpleImplicitDeclaration2`）。理由：该行为与容器无关——类型字符是标识符的词法组成，解析后即普通类型引用，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `$ 类型字符` 出现 **3** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeLexicalConformanceTests.vb:51`（`TopLevelTypeCharactersUnderStrictOn_Conform`）；方法 `:69`（`TopLevelTypeCharacters_Conform`）。| — |
| `## Keywords` | `lexical-grammar.md:357` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:303`（`TopLevelDimForms_Conform`；`Dim`/`As`/`New`/`ReadOnly`/`Return` 等保留字在脚本顶层生效）。嵌套 方法 `ScriptModeStatementConformanceTests.vb:568`（`OptionInferOff_MakesLocalObject_Conforms`）；源码 `:574`（`Sub Probe()` 体内的保留字 `Dim`） | — |
| `## Literals` | `lexical-grammar.md:405` | 不适用（分组小节：其下 7 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' lexical-grammar.md` = 12 | — |
| `### Boolean Literals` | `lexical-grammar.md:421` | 已覆盖 | 已覆盖 | 顶层 `CommandLineRunnerTests.vb:532`（`TestTopLevelAddHandlerInScriptFile`，源码 `.vbx` 顶层 `Dim ran As Boolean = False`）；`ScriptModeConformanceTests.vb:531`（`TopLevelOperators_Conform`，源码 `:553`/`:556` 返回 `True`/`False`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:14`（`TestBooleanConstLocal`）。理由：该行为与容器无关——布尔字面量是通用词法面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `布尔字面量` 出现 **2** 处，位于该块声明的类型体内）| — |
| `### Integer Literals` | `lexical-grammar.md:431` | 已覆盖 | 已覆盖 | 顶层 方法 `InteractiveSessionTests.vb:109`（`StatementExpressions_IntLiteral`）；方法 `ScriptModeConformanceTests.vb:325`（`TopLevelConstForms_Conform`）；源码 `:328`（`Const ci As Integer = 3`）。嵌套：缺口。`&H` / `&O` / `&B` 进制前缀：`grep -n '&O\|&B' Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**），`&H` 命中仅为测试自身代码 ⇒ 进制字面量在脚本容器里**缺口**；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_MarshalAs.vb:68`（`SimpleTypes`）。理由：该行为与容器无关——整数字面量（含 `&H` 进制前缀）是通用词法面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `&H 整数字面量` 出现 **3** 处，位于该块声明的类型体内）| — |
| `### Floating-Point Literals` | `lexical-grammar.md:510` | 已覆盖 | 已覆盖 | 方法 `ScriptModeConformanceTests.vb:325`（`TopLevelConstForms_Conform`）；源码 `:329` `Const decimalValue As Decimal = 1.5D`。`F`/`R`/`S`/`D` 后缀的负数与指数形式未单测 ⇒ 该子形态仍在缺口面内。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:181`（`TestDoubleConstLocal`）。理由：该行为与容器无关——浮点字面量（含 `D` 等后缀）是通用词法面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `浮点字面量 3.1415926` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### String Literals` | `lexical-grammar.md:559` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:325`（源码 `:330` `Const text As String = "abc"`）、`ScriptModeConformanceTests.vb:303`（源码 `:305-309` 各 `Dim` 形态）。嵌套 `ScriptModeConformanceTests.vb:516`（`TopLevelOverloads_Conform`，`Function Describe` 体内源码 `:519` `Return "I" & x`）。**内插字符串** `$"…"` 单列在 §C.3.E（arm `ParseExpression.vb:409`） | — |
| `### Character Literals` | `lexical-grammar.md:611` |已覆盖| 已覆盖 | 检索：`grep -nE '"c"c' Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:68`（`TestCharConstLocal`）。理由：该行为与容器无关——字符字面量是通用词法面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `"a"c 字符字面量` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:92`（`TopLevelCharacterLiterals_Conform`）。| — |
| `### Date Literals` | `lexical-grammar.md:636` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:325`（源码 `:328` `Const dateValue As Date = #1/1/2020#`）；`ScriptTopLevelCrashTests.vb:165`（`TopLevelConstDateAndSharedFieldInitializers_BothRun`，源码 `:167` `Const d As Date = #1/1/2020#`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenConstLocal.vb:322`（`TestDateTimeConstLocal`）。理由：该行为与容器无关——日期字面量是通用词法面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `#3/2/2012# 日期字面量` 出现 **1** 处，位于该块声明的类型体内）| — |
| `### Nothing` | `lexical-grammar.md:711` | 已覆盖 | 已覆盖 | 顶层 方法 `InteractiveSessionTests.vb:117`（`StatementExpressions_Nothing`）；方法 `ScriptModeStatementConformanceTests.vb:332`（`TopLevelReDimAndErase_Conform`）；源码 `:338`（`fixedArr Is Nothing`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_ObsoleteAttribute.vb:2494`（`TestDeprecatedAttribute`）（U10a-fix 换锚）。理由：该行为与容器无关——`Nothing` 字面量是通用表达式，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Nothing` 出现 **4** 处，位于该块声明的类型体内）| — |
| `## Separators` | `lexical-grammar.md:721` | 不适用（与容器无关：词法/分隔符规则，无独立语义面） | 不适用（同上） | `sed -n '721,731p' lexical-grammar.md`（**实锤**：分隔符定义表）。注：语句分隔符 `:` 作为 **arm** 单列在 §C.3.E | — |
| `## Operator Characters` | `lexical-grammar.md:731` | 不适用（与容器无关：运算符字符集定义，无独立语义面） | 不适用（同上） | `sed -n '731,745p' lexical-grammar.md`（**实锤**） | — |
| `## Conditional Compilation` | `preprocessing-directives.md:5` | 不适用（分组小节：其下 2 个子小节逐行出行） | 不适用（同上） | 检索：`grep -c '^### ' preprocessing-directives.md` = 2 | — |
| `### Conditional Constant Directives` | `preprocessing-directives.md:111` |已覆盖| 已覆盖 | 检索：`grep -n "#Const" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`ParseConditional.vb:231`（`ParseConstDirective`）、分派 `:63` 的 `Case SyntaxKind.ConstKeyword`（**原文引 `:61`，那是 `ParseAnachronisticEndIfDirective` 调用行**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests_Conditional.vb:687`（`CaseInsensitivityTest`）。理由：该行为与容器无关——`#Const` 是编译单元级 trivia 指令，写在方法体内同样生效，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `#Const` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:115`（`TopLevelConstDirective_Conform`）。| — |
| `### Conditional Compilation Directives` | `preprocessing-directives.md:152` |已覆盖| 已覆盖 | 检索：`grep -n "#If\|#ElseIf\|#Else\b\|#End If" Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（`#Region` 的命中是测试文件自身代码，**实锤**）。arm（**行号本轮改正**，原文的 `:53`/`:57`/`:45`/`:49`/`:216` 在 ERE 分派里对不上）：`ParseConditional.vb:47` 的 `Case SyntaxKind.IfKeyword` → `ParseIfDirective`；`:51` 的 `Case ElseIfKeyword` → `ParseElseIfDirective`；`:43` 的 `Case ElseKeyword` → `ParseElseDirective`；`:55` 的 `Case EndKeyword` → `ParseEndDirective`；`:59` 的 `Case EndIfKeyword` → `ParseAnachronisticEndIfDirective`（拟古形式；原文引 `:216` 是该函数本身，不是 `Case` 行）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenLateBound.vb:3225`（`Bug18274_1`）。理由：该行为与容器无关——`#If`/`#ElseIf`/`#Else`/`#End If` 是编译单元级 trivia 指令，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 `#If` **28** 处、`#End If` **28** 处（配平）；原始词频 29 多的 1 处在注释行 `' System.Console.WriteLine("#If EarlyBound")`（`:79`）的字符串内，不计入）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:134`（`TopLevelConditionalCompilationDirectives_Conform`）。| — |
| `## External Source Directives` | `preprocessing-directives.md:196` |已覆盖| 已覆盖 | 检索：`grep -n "ExternalSource" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`ParseConditional.vb:275`（`ParseExternalSourceDirective`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `PDB\ChecksumTests.vb:217`（`NormalizedPath_ExternalSource`）。理由：该行为与容器无关——`#ExternalSource` 是源文本行区间映射指令，与容器种类无关。（U10a 审计 2026-09-17：所引源码块内 `#ExternalSource(...)` 出现 **5** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:162`（`TopLevelExternalSourceDirective_Conform`）；方法 `:175`（`TopLevelExternalSourceWithoutEnd_IsReported`）。| — |
| `## Region Directives` | `preprocessing-directives.md:231` |已覆盖| 已覆盖 | 检索：`grep -n "#Region" Scripting/VisualBasicTest/*.vb` 命中**全部为测试文件自身的 `#Region`**，无一处写在脚本源码字符串内（**实锤**）。arm：`ParseConditional.vb:261`（`ParseRegionDirective`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:711`（`ErrorHandler_Other_Constructor_Dispose`）（U10a-fix 换锚）。理由：该行为与容器无关——`#Region` 是纯 trivia 分组指令，不参与绑定，与容器种类无关。（U10a 审计 2026-09-17：所引源码块内 `#Region` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeLexicalConformanceTests.vb:187`（`TopLevelRegionDirective_Conform`）；方法 `:198`（`TopLevelRegionWithoutEnd_IsReported`）。| — |
| `## External Checksum Directives` | `preprocessing-directives.md:272` | 不适用（与容器无关：为 PDB / 源文件校验服务的指令，脚本容器不消费其产物；`spec-scripting-dialect.md` 亦无相关面） | 不适用（同上） | 检索：`grep -n "ExternalChecksum" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm 存在：`ParseConditional.vb:322` | — |

#### C.3.E parser 分派 arm 补充行（spec 标题树无对应小节者，**27 行**）

> **并入规则（判据①的实现方式）**：arm 与某个 spec 小节指同一构造时，该 arm 的 `文件:行号` **写进该 spec 行的「来源」列**（§C.3.A–D 已逐条落地），**不另立行**；只有 spec 标题树**完全没有**对应构造时才在下面另立行。这样「spec ∪ arm」的并集不会因重复计数而虚高，`SyntaxKind` 反查（U11）时差集仍为空。
>
> **本轮修复轮的并入关系变更（3 行）**：`Namespace`（原先并入「声明入口 arm」行）、`MyBase` 与 `MyClass`（原先并入 §C.3.C 的 `### Instance Expressions` 行）改为**独立行**——并入方式把它们的 `缺口` 吸收掉了（「声明入口 arm」行顶层栏＝`已覆盖`），使 U11 的差集反查卡住、计数少报。被并入的那两行**已删去这三个构造的表述**，故并集不重复计数（分母 232 → 235）。
>
> **已并入的 arm（骨架行，仅供 U11 反查）**：`Parser.vb:937`(GoTo)、`:940`(Case)、`:943`(Select)、`:946`+`:952`(With/While/SyncLock)、`:949`(Using)、`:955`(Try)、`:958`(Catch)、`:961`(Finally)、`:964`(If)、`:969`+`:976`(Else/ElseIf)、`:979`(Do)、`:982`(Loop)、`:985`(For)、`:988`(Next)、`:991`(EndIf/Wend)、`:996`(End)、`:999`(Return)、`:1002`(Stop)、`:1005`(Continue)、`:1008`(Exit)、`:1011`(On)、`:1014`(Resume)、`:1017`(Call)、`:1020`(RaiseEvent)、`:1023`(ReDim)、`:1026`(AddHandler/RemoveHandler)、`:1095`(Set/Let)、`:1098`(Error)、`:1101`(Throw)、`:1113`(Identifier 上下文关键字：`Mid`/`Async`/`Iterator`/`Await`/`Yield`/`Custom Event`)、`:1158`(Dot/`!`/`Me`/`MyBase`/`MyClass`/类型关键字/转换函数)、`:1211`(Erase)、`:1214`(Get)、`:1223`(Gosub)、`:1228`(声明入口：`Inherits`/`Implements`/`Imports`/`Option`/`Declare`/`Delegate`/`Interface`/`Property`/`Sub`/`Function`/`Operator`/`Event`/`Namespace`/`Class`/`Structure`/`Enum`/`Module`)、`:1249`(Question)；`ParseStatement.vb:33`(Continue 种类)、`:105`(Exit 种类)、`:207`(Exit 的 Operator/AddHandler/RemoveHandler/RaiseEvent 报错分支)、`:347`(`Case` 关系运算符)、`:487`(EndIf/Wend/Gosub)、`:1199`(十个复合赋值 token)、`:1435`(While/With/SyncLock)；`ParseExpression.vb:80`(一元 `-`/`Not`)、`:191`(主表达式分派)、`:1623`(`CType`/`DirectCast`/`TryCast`)、`:1048`(XML 轴)。

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `Option` 语句（`Option Strict/Compare/Infer/Explicit`） | `Parser.vb:825` → `ParseOptionStatement` | 已覆盖 | 不适用（`Option` 语句必须位于 `Imports` 与所有其它语句之前，脚本单元内不存在「写在方法体内」的合法形态；检索：`grep -n "Option " Scripting/VisualBasicTest/*.vb` 命中 27 处，全部为脚本源码首行） | 顶层 `ScriptModeStatementConformanceTests.vb:529`（`OptionStrictOn_TakesEffect`；`Option Strict On`）、`:539`（`OptionStrictOff_Conforms`；`Off`）、`:549`（`OptionCompareText_TakesEffect`；`Compare Text`）、`:559`（`OptionCompareBinaryDefault_Conforms`；`Compare Binary` 默认）、`:568`（`OptionInferOff_MakesLocalObject_Conforms`）/`:588`（`OptionInferDefault_MakesLocalInteger_Conforms`）（`Infer`）、`:637`（`OptionExplicitOff_ConformsInMethodBody`；`Explicit Off`）、`:663`（`OptionStatementAfterImport_IsReported`）/`:671`（`OptionStatementAfterStatement_IsReported`）（位置错误报 BC30627） | 种子行读数：`Option Infer` 各档见 §C.3.C 的 `## Late-Bound Expressions` 行 |
| `?` Print 语句（REPL/脚本） | `Parser.vb:1249` → `ParsePrintStatement` | 已覆盖 | 缺口 | 顶层 方法 `CommandLineRunnerTests.vb:828`（**REPL 路径**；`TestExplicitQuestionStillPrints`）、方法 `:844`（**REPL 路径**；`TestQuestionAssignmentPrintsComparisonResult`）、方法 `:939`（**REPL 路径**；`TestQuestionParenStillPrints`）、方法 `:424`（`.vbx` 顶层 `?` 不设退出码）；**helper 常量（非测试方法）** `ScriptModeConformanceTests.vb:53`（`ReplMarker = "? 13 * 29"`，`:55` 为期望值 `"377"`）。嵌套：无方法体内的独立 `?` 打印语句 | — |
| 顶层裸表达式语句 | `Parser.vb:1104` / `:1257` / `ParseStatement.vb:1126` → `ParseScriptExpressionStatement` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1059`（`.vbx` 顶层裸表达式为静默 no-op）、方法 `:1073`（裸属性同）、方法 `:436`（尾随表达式不设退出码）、方法 `:882`（**REPL 路径**；`TestNonFinalBareExpressionInSubmissionStillErrors`，非末句报 `BC31003`）；方法 `ScriptTests.vb:198`（`TestExpressionStatementReturnValue`）。嵌套 方法 `CommandLineRunnerTests.vb:1088`（`TestBareExpressionInNestedSubStillErrors`）；源码 `:1091`（`.vbx` 里的 `Sub S() : 1 + 2 : End Sub`），断言报错 ⇒ **负向覆盖**。**两处终态验证修复**：① `:882` 按裁定 8/10 的口径（`input:=` + `RunInteractive`）补标 `REPL 路径`；② 嵌套栏 `缺口` → **`已覆盖`**——原格依据已写着「该格即嵌套容器的负向覆盖」却记 `缺口`，**与裁定 4 的 `## Logical Operators` 同类**（证据与状态矛盾），按**裁定 5**（负向断言指向该语法本身即算覆盖；`Sub S()` 体内的裸表达式正是该语法）改判 | — |
| `Empty` 空语句 | `Parser.vb:1204` → `ParseEmptyStatement` |已覆盖| 已覆盖 | 检索：`grep -n "EmptyStatement" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。**嵌套栏改判 `已覆盖`（U10a-fix 豁免，编译器测试指针）**：`Syntax` `Parser\ParseStatements.vb:1680`（`ParseEmptyStatementFollowingColon`）。理由：空语句是纯语法构造，与容器无关——该方法源码块为 `Module M` → `Sub M()` 体内 `M() : ` / `If True Then Else : ` 等冒号后的空语句，**正是嵌套容器形态**，测试逐一断言 token 序列。（U10a 未采信的 `:1917` `ParseEmptyStatement` 是 `SyntaxFactory.ParseExecutableStatement` 直调，确非容器形态；`--tests-only` 过滤器吃掉了 `:1680`） （U10a 审计 2026-09-17：所引源码块内 `冒号后的空语句` 出现 **5** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:56`（`TopLevelEmptyStatements_Conform`）。| — |
| 块结束语句（`End Class`/`End Structure`/`End Module`/`End Interface`/`End Enum`/`End Property`/`End Sub`/`End Function`/`End Event`） | `Parser.vb:822` → `ParseGroupEndStatement` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:570`（`End Class`/`End Structure`/`End Interface`/`End Enum` 全部写在脚本顶层）；方法 `ScriptModeSubmissionConformanceTests.vb:69`（**REPL 路径**；提交链 `End Class`）。嵌套 `ScriptModeConformanceTests.vb:606`（脚本内 `Class Square`/`MustInherit Class ShapeBase` 的 `End Class`） | — |
| 过时 `Get` 语句 | `Parser.vb:1214` → `ERR_ObsoleteGetStatement` |已覆盖| 已覆盖 | 检索：`grep -nE "^\s*Get\s*$" Scripting/VisualBasicTest/*.vb` → 命中为访问器 `Get`（属 `### Get Accessor Declarations`），**无过时语句形态**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Syntax` `Parser\ParseErrorTests.vb:2614`（`BC30829ERR_ObsoleteGetStatement`）。理由：该行为与容器无关——过时 `Get` 语句是通用解析路径的诊断形态，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Get Me.text（过时语句）` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:85`（`TopLevelObsoleteGetStatement_IsReported`）。**spec 锚点可达性加注（U10b 复核，**实锤**）**：`Parser.vb:1214` 的 `Get` arm 在**裸编译单元顶层不可达**——声明上下文分派 `Parser.vb:843`/`:846` 先把 `GetKeyword`/`SetKeyword` 交给 `ParsePropertyOrEventAccessor`（对照：`AddHandler`/`RemoveHandler` 在 `:829`/`:835` 有 `IsScript AndAlso Context.BlockKind = SyntaxKind.CompilationUnit` 守卫，`Get`/`Set` **没有**）。⇒ 裸顶层 `Get` 报 **BC30188**（错位访问器），**不是** BC30829。顶层可写形态是「`If` 块内」——容器仍是脚本顶层、但已进入可执行语句上下文；单元 E 的用例**同时钉住两种形态**（`If` 块内 BC30829 + 裸顶层 BC30188）。| — |
| 过时 `Set` / `Let` 赋值语句 | `Parser.vb:1095` → `ParseStatement.vb:1452` `ParseAssignmentStatement` |已覆盖| 已覆盖 | 检索：`grep -nE "^\s*(Set\|Let) " Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（**实锤**）。**嵌套栏改判 `已覆盖`（U10a-fix 豁免，编译器测试指针）**：`Syntax` `Parser\ParseStatements.vb:2973`（`BC30807ERR_ObsoleteLetSetNotNeeded`）。理由：`Let`/`Set` 赋值语句是纯语法构造，与容器无关——该方法源码块为 `class c1` → `sub s` 体内 `let i = 0` / `set j = i`，**正是嵌套容器形态** （U10a 审计 2026-09-17：所引源码块内 `let / set 赋值` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:117`（`TopLevelObsoleteSetAndLetAssignment_IsReported`）。**spec 锚点可达性加注（U10b 复核，**实锤**）**：`Parser.vb:1095` 的 `Set`/`Let` 赋值 arm 在**裸编译单元顶层不可达**——声明上下文分派 `Parser.vb:843`/`:846` 先把 `SetKeyword` 交给 `ParsePropertyOrEventAccessor`（`AddHandler`/`RemoveHandler` 在 `:829`/`:835` 有 `IsScript AndAlso Context.BlockKind = SyntaxKind.CompilationUnit` 守卫，`Get`/`Set` **没有**）。⇒ 裸顶层 `Set` 报 **BC30188**（错位访问器），**不是** BC30807。顶层可写形态是「`If` 块内」；单元 E 的用例**同时钉住两种形态**（`If` 块内 BC30807×3 + 裸顶层 BC30188）。| — |
| 语句分隔符 `:` 与语句终止符 | `Parser.vb:1207` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；`TestMidRedimWithDoNotPrint`，REPL 顶层 `Dim s = "abcdef" : Mid(s, 1, 2) = "ab"` 单行多语句）；方法 `ScriptModeSubmissionConformanceTests.vb:238`（`ReplWithEventsAndAddHandlerAcrossSubmissions_Conform`；`<Fact>` 属性在 `:237`）；源码 `:247`（`Dim handlerCount As Integer = 0 : AddHandler …`；**原文引 `ScriptTopLevelCrashTests.vb:247`——该行是 `<Fact>` 属性、`:237` 是空行，该脚本行实在 `ScriptModeSubmissionConformanceTests.vb`**）；方法 `ScriptTopLevelCrashTests.vb:248`（`ReplTopLevelBackwardGoTo_JumpTakesEffectAndTheSessionContinues`）；源码 `:251`（`fin: i += 1 : If i < 3 Then GoTo fin`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenSingleLineIf.vb:11`（`SingleLineIf`）。理由：该行为与容器无关——语句分隔符 `:` 是通用词法/解析面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 ` : 语句分隔符` 出现 **4** 处，位于该块声明的类型体内）| — |
| 修饰符声明前缀（`Partial`/`Private`/`Protected`/`Public`/`Friend`/`NotOverridable`/`Overridable`/`MustInherit`/`MustOverride`/`Static`/`Shared`/`Shadows`/`WithEvents`/`Overloads`/`Overrides`/`Const`/`Dim`/`Widening`/`Narrowing`/`Default`/`ReadOnly`/`WriteOnly` / 属性列表 `<…>`） | `Parser.vb:1029` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeConformanceTests.vb:606`（`NestedInheritanceHierarchy_Conforms`，源码 `:611-613`/`:621` `MustInherit`/`MustOverride`/`NotOverridable Overrides`/`Overrides`/`Implements`）、方法 `:516`（`TopLevelOverloads_Conform`，`Overloads`）、方法 `:683`（`TopLevelMemberAttributes_Conform`，成员属性 `Obsolete`/`Conditional`/`MethodImpl`）、方法 `:303`（`ReadOnly`，源码 `:308`）、方法 `:338`（`TopLevelAutoProperties_Conform`，自动属性）、方法 `:381`（`TopLevelReadOnlyAndSharedProperties_Conform`，`Shared` 属性）。嵌套 方法 `ScriptModeConformanceTests.vb:606`（`NestedInheritanceHierarchy_Conforms`）；源码 `:612`/`:613`/`:620`/`:621`（脚本内 `Class ShapeBase`/`Class Square` 的**成员位修饰符** `Public MustOverride`/`Public NotOverridable Overrides`/`Public Side`/`Public Overrides`——按裁定 1 属嵌套容器）；方法 `ScriptModeStatementConformanceTests.vb:568`（`OptionInferOff_MakesLocalObject_Conforms`）；源码 `:574`（`Sub Probe()` 体内局部声明的 `Dim` 修饰符）。**口径精化（main 裁定 1）**：原文的嵌套依据 `ScriptModeConformanceTests.vb:611` 是 `MustInherit Class ShapeBase` —— **`MustInherit` 修饰的是类型声明本身、该声明按「关键澄清」属顶层容器**，故 `:611` 已改列顶层栏；但**同方法内 `:612`/`:613`/`:620`/`:621` 的成员位修饰符**按裁定 1 属嵌套容器，可作嵌套依据。`Static` 修饰符在脚本容器：见 §C.3.F | — |
| `Get` / `Set` 访问器语句 | `Parser.vb:838`-`:846` → `ParsePropertyOrEventAccessor` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:347`（`Get … End Get` / `Set(newValue As Integer) … End Set`，写在脚本顶层）；`CommandLineRunnerTests.vb:1040`（**REPL 路径**）的 `Get`/`Set` 属宿主侧。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `Attributes\AttributeTests.vb:613`（`TestAttributesOnPropertyAndGetSet`）。理由：该行为与容器无关——该成员声明位于普通类型体内，编译产物与普通编译单元一致，与脚本容器无关。（与 `### Get Accessor Declarations` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Get / Set 访问器` 出现 **2** 处，位于该块声明的类型体内）| — |
| `AddHandler` / `RemoveHandler` / `RaiseEvent` 访问器语句 | `Parser.vb:828`-`:836` | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:399`（`Custom Event` 的三个访问器全部写在脚本顶层）；`:427`（`RaiseEvent` 访问器体被调用点到） | — |
| 声明入口 arm（`Inherits`/`Implements`/`Imports`/`Option`/`Declare`/`Delegate`/`Interface`/`Property`/`Sub`/`Function`/`Operator`/`Event`/`Namespace`/`Class`/`Structure`/`Enum`/`Module`） | `Parser.vb:1228` | 已覆盖 | 不适用（**不存在合法形态**：这些入口关键字写在方法体内在任何 VB 里都非法——`README.md` §四「配套裁定 · 方法体内不存在合法形态的语法」。与 `Option` 语句的既有处置同理；`Namespace` 另有脚本特有的 BC36965 禁止面，见下一行） | 顶层 方法 `ScriptModeConformanceTests.vb:570`（`NestedTypeDeclarations_Conform`；源码 `:572`/`:575`/`:578`/`:581`/`:585` 的 `Class`/`Structure`/`Interface`/`Enum`/`Delegate`）、方法 `:594`（`NestedModuleDeclaration_Conforms`，`Module`）、方法 `:606`（`Inherits`，源码 `:618`）；方法 `ScriptModeSubmissionConformanceTests.vb:56`（**REPL 路径**；`ReplSubmissionImportsAccumulate_Conforms`，`Imports`）。`Namespace` 已**拆成独立行**（见下），不再由本行吸收 | — |
| `Namespace` 声明（脚本方言禁止面：`BC36965`） | `Parser.vb:1228`（`NamespaceKeyword`）；规范 `spec\spec-scripting-dialect.md`「Script-specific restrictions and diagnostics」 |已覆盖| 不适用（**不存在合法形态**：`Namespace` 写在方法体内在任何 VB 里都非法；脚本特有的 BC36965 禁止面只落在顶层栏） | **本轮新增的独立行**（原先被上一条「声明入口 arm」行吸收 ⇒ 缺口被吸收、`缺口` 计数**少报**）。顶层：检索 `grep -n "Namespace " Scripting/VisualBasicTest/*.vb` → **5 行 / 2 文件**（`CommandLineRunnerTests.vb:353,476`、`NuGetReferenceDirectiveNTests.vb:57,70` 等），**全部是测试自身构造的辅助程序集源码**（供 `/r:` 引用的 `Namespace ReferenceArgumentLibrary` 之类），**无一处写在脚本单元里**；`grep -rn "BC36965" Scripting/VisualBasicTest/ --include=*.vb` → **0 命中** ⇒ 脚本内 `Namespace` 应有 BC36965 行为而**无任何用例**；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:155`（`TopLevelNamespaceDeclaration_IsReported`）。| — |
| `MyBase` 表达式 | `Parser.vb:1158`（`MyBaseKeyword`） | 已覆盖 | 缺口 | **本轮新增的独立行**（原先被 §C.3.C 的 `### Instance Expressions` 行吸收）。顶层 方法 `ScriptTopLevelCrashTests.vb:401`（`TopLevelMyBase_IsReportedInsteadOfTerminatingTheProcess`）；源码 `:402`（顶层 `MyBase.ToString()` 报 BC36966）；方法 `:414`（`ReplTopLevelMyBase_IsReportedAndTheSessionContinues`，REPL 变体）。嵌套：**零覆盖**——`ScriptModeStatementConformanceTests.vb:482` 覆盖的是 **`Me`**（源码 `:485` `Return Me.ToString()`），**不能替 `MyBase`**。`MyBase`/`MyClass` 在 `README.md` §四「已知受容器影响」族清单内（来源 issue 15）⇒ **两栏都必须用脚本测试**，**本格的嵌套 `缺口` 不得豁免**（main 裁定 3） | — |
| `MyClass` 表达式 | `Parser.vb:1158`（`MyClassKeyword`） |已覆盖| 已覆盖 | **本轮新增的独立行**（原先被 §C.3.C 的 `### Instance Expressions` 行吸收）。顶层：**无用例**——脚本源码里唯一的 `MyClass` 命中（`ScriptModeStatementConformanceTests.vb:495`）写在 `Function Describe()` **体内**，故裸顶层 `MyClass` 零覆盖 ⇒ `缺口`。嵌套 方法 `ScriptModeStatementConformanceTests.vb:497`（`TopLevelExplicitMyClass_IsReported`）；源码 `:495`（`Function Describe()` 体内 `Return MyClass.ToString()` 报 BC36966）。本族在「已知受容器影响」清单内（main 裁定 3）⇒ 顶层 `缺口` 须由脚本测试补，**不得豁免**；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeParserArmConformanceTests.vb:174`（`TopLevelMyClassExpression_IsReported`）。| — |
| `End` 语句 | `Parser.vb:996`（`EndKeyword`）→ `Parser.vb:1710` `ParseEndStatement` | 已覆盖 | 已覆盖 | **终态验证修复轮新增行**（与 `Namespace` 同类的漏报：§C.3.E 的「已并入的 arm」清单把 `:996` 登记为已并入，但**没有任何行接收它**；`statements.md` 43 个标题里**无 `### End Statement`**，故按「spec 标题树完全没有则另立行」补出）。顶层（**负向，裁定 5 算覆盖**）方法 `ScriptModeStatementConformanceTests.vb:393`（`TopLevelEndStatement_IsReported`）；源码 `:391`（脚本顶层 `End` 报 `BC30678` `ERR_UnrecognizedEnd`）。嵌套（**负向**）方法 `:400`（`EndInsideMethod_IsReported`）；源码 `:399`（`Sub Finish()` 体内的 `End` 报 `BC30615` `ERR_EndDisallowedInDllProjects`——注意**方法体内合法写出、但本 fork 的脚本编译到 DLL 故被拒**，两栏诊断不同）。**可见性边界**：`End` **不会**产生可运行产物（这才是 §一「已知空白」的含义），故两栏都只到诊断为止，**不构造进程内运行方案** | — |
| `Stop` 语句 | `Parser.vb:1002`（`StopKeyword`）→ `ParseStatement.vb:1813` `ParseStopOrEndStatement` | 已覆盖 | 已覆盖 | **终态验证修复轮新增行**（同上，与 `Namespace` 同类的漏报；`statements.md` **无 `### Stop Statement`**）。顶层 方法 `ScriptModeStatementConformanceTests.vb:415`（`TopLevelStopStatement_EmitsWithoutRunning`）；源码 `:413`（脚本顶层 `Stop`）——按 `README.md` §一「已知空白」的既定处置，该格只验**发射**（`AssertEmits`），因为**跑它会把进程交给调试器**⇒ 顶层栏按该既定口径记 `已覆盖`。嵌套：**缺口**——`Stop` 写在方法体内是合法形态（与 `End` 不同，方法体内不报诊断），但**无任何脚本测试**覆盖该形态；`grep -nE '"Stop"' Scripting/VisualBasicTest/*.vb` 仅命中 `:413` 一处（脚本顶层）。**可见性边界同 `End`**；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenStopOrEnd.vb:44`（`StopStatement_SimpleTestWithEndOtherThanMain`）。理由：该行为与容器无关——`Stop` 写在方法体内是合法形态，编译器测试覆盖其发射。（U10a 审计 2026-09-17：所引源码块内 `Stop` 出现 **1** 处，位于该块声明的类型体内）| — |
| `#Disable` / `#Enable` 警告指令 | `ParseConditional.vb:79`（`Case SyntaxKind.EnableKeyword, SyntaxKind.DisableKeyword`）→ `:390` `ParseWarningDirective` |已覆盖| 不适用（同 `#Load`/`#R`/`#!` 的位置约束：警告指令只能在编译单元首个 token 之前，脚本单元内不存在方法体内形态） | **终态验证修复轮新增行**（arm 存在而 spec 标题树**无对应小节**：`grep -niE "Enable\|Disable\|Warning Directive" preprocessing-directives.md` 对**标题行**零命中，文件仅有 6 个标题 `## Conditional Compilation`/`### Conditional Constant Directives`/`### Conditional Compilation Directives`/`## External Source Directives`/`## Region Directives`/`## External Checksum Directives`；ledger 亦无此行）。顶层：检索 `grep -rn "#Disable\|#Enable" Scripting/VisualBasicTest/*.vb` → **0 命中** ⇒ `缺口`；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeParserArmConformanceTests.vb:205`（`TopLevelWarningDirectives_Conform`）。| — |
| `Mid` 赋值语句（contextual 路径） | `Parser.vb:1113` → `ParseStatement.vb:1647` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1040`（**REPL 路径**；REPL 顶层 `Mid(s, 1, 2) = "ab"`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\CompoundAssignment.vb:585`（`MidAssignment1`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（与 `### Mid Assignment Statement` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Mid(...) = 赋值` 出现 **10** 处，位于该块声明的类型体内）| — |
| `Async` / `Iterator` 说明符（contextual） | `Parser.vb:1113` → `ParseSpecifierDeclaration` | 已覆盖 | 已覆盖 | 顶层 `ScriptModeConformanceTests.vb:484`（`Async Sub`/`Async Function`）、`:500`（`Iterator Function`）；`ScriptModeStatementConformanceTests.vb:421`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenAsyncTests.vb:67`（`Simple_Void`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（与 `### Async Methods` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Async 说明符` 出现 **1** 处，位于该块声明的类型体内）| — |
| `Await` 语句（contextual 路径） | `Parser.vb:1113`（`:1145-1147` 的 `AwaitKeyword` + `Context.IsWithinAsyncMethodOrLambda` 分支）→ `ParseStatement.vb:1857` `ParseAwaitStatement` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptTests.vb:387`（`TestTopLevelBareAwaitStatement`）；嵌套 方法 `ScriptModeConformanceTests.vb:484`（`TopLevelAsyncSubAndFunction_Conform`）；源码 `:487`。**行号改正**：原文引 `Parser.vb:1152`，那是 `End If`（`:1153` 才是 `End If` 的收尾），`Await` 的上下文分派实在 `:1145-1147` | — |
| `Yield` 语句（contextual 路径） | `Parser.vb:1113`（`:1149-1151` 的 `YieldKeyword` + `Context.IsWithinIteratorMethodOrLambdaOrProperty` 分支）→ `ParseStatement.vb:1877` `ParseYieldStatement` | 已覆盖 | 已覆盖 | 顶层 方法 `ScriptModeStatementConformanceTests.vb:84`（`TopLevelYieldWithoutIterator_IsReported`）；嵌套 方法 `ScriptModeConformanceTests.vb:500`（`TopLevelIteratorFunction_Conforms`）；源码 `:504`。**行号改正**：原文引 `Parser.vb:1156`，那是 `Return ParseAssignmentOrInvocationStatement()`，`Yield` 的上下文分派实在 `:1149-1151` | — |
| `Custom Event` 声明头 | `Parser.vb:1113`（`CustomKeyword` + `PeekToken(1).Kind = EventKeyword`） | 已覆盖 | 缺口 | 顶层 `ScriptModeConformanceTests.vb:399`、`:427`、`:456`（三处都写在脚本顶层） | — |
| 拟古语句 `EndIf` / `Wend` / `Gosub` | `Parser.vb:991` / `:1223` → `ParseStatement.vb:487` |已覆盖| 已覆盖 | 检索：`grep -nE "EndIf\|Wend\|Gosub" Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Syntax` `Parser\ParseStatements.vb:2543`（`BC30089ERR_ObsoleteWhileWend`）。理由：该行为与容器无关——拟古 `Wend`/`EndIf` 是通用解析诊断面，与脚本容器无关。另 `Syntax` `Parser\ParseStatements.vb:2990`（`BC30814ERR_ObsoleteGosub`）覆盖 `Gosub`。（U10a 审计 2026-09-17：所引源码块内 `Wend / Gosub` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:235`（`TopLevelAnachronisticStatements_AreReported`）。| — |
| Label 语句（标识符作标签） | `Parser.vb:1113` / `:1104` → `ParseStatement.vb:1611` | 已覆盖 | 已覆盖 | 顶层 `ScriptTopLevelCrashTests.vb:194`（顶层 `skip:`）、`:209`（顶层 `top:`）、`:225`（重复标签报 BC30094）；`ScriptTests.vb:300`。嵌套 `ScriptModeStatementConformanceTests.vb:197`（`Sub Jump` 体内 `Fault:`，源码 `:209`） | — |
| 过时 `Error` 语句 | `Parser.vb:1098` → `ParseStatement.vb:1569` |已覆盖| 已覆盖 | 同 §C.3.A 的 `#### Error Statement` 行（**全表同一条证据**，不重复计入统计）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:15`（`ErrorHandler_WithValidLabel_No_Resume`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（与 `#### Error Statement` 行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Error n` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:262`（`TopLevelErrorStatement_Conforms`）。| — |
| `Global` 限定符（表达式） | `ParseExpression.vb:239` |已覆盖| 已覆盖 | 检索（**本轮实测重跑**）：`grep -nE "\bGlobal\." Scripting/VisualBasicTest/*.vb` → **1 命中**：`ImportsAccumulationFailureTests.vb:78`（`Yield "Imports Global.System.Text"`）；含 `Helpers\` 的递归口径多 1 处 `Helpers\ObjectFormatterFixtures.vb:4`（测试自身的 `Namespace Global.…`，非脚本源码；`obj\` 下的生成文件不计）。**逐条重判**：该命中是 `RejectedImportStatements` 的一条负向语料，方法 `ImportsAccumulationFailureTests.vb:242`（`ReplayPath_RejectedClauseForms_AreBlockedByTheSubmissionGate`）断言其**被宿主门拒绝**；它是 **`Imports` 子句里的 `Global`**，不是表达式里的 `Global.` 限定符（本行的 arm 是 `ParseExpression.vb:239`）⇒ 两栏仍 `缺口`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTests.vb:1868`（`ExplicitStandardModuleAttribute`）。理由：该行为与容器无关——`Global.` 限定符是通用名称解析面，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Global. 限定` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeParserArmConformanceTests.vb:287`（`TopLevelGlobalQualifier_Conforms`）。| — |
| 内插字符串 `$"…"` | `ParseExpression.vb:409`（`DollarSignDoubleQuoteToken`） |已覆盖| 已覆盖 | 检索：`grep -nE '\$"' Scripting/VisualBasicTest/*.vb` → 命中 3 文件，全部为**测试自身的 C# 风格字符串或 `$"…"` 插值用于构造测试文本**（`ImportsAccumulationFailureTests.vb` / `CommandLineRunnerTests.vb` / `ScriptModeConformanceTests.vb:154`——最后一处是**测试自身代码**，非 `<Fact>` 方法行），**无一写在脚本源码字符串里**；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\InterpolatedStringTests.vb:71`（`SimpleInterpolation`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `$"..." 内插字符串` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:309`（`TopLevelInterpolatedStrings_Conform`）。| — |
| 转换函数关键字表达式（`CBool`/`CByte`/`CChar`/`CDate`/`CDbl`/`CDec`/`CInt`/`CLng`/`CObj`/`CSByte`/`CShort`/`CSng`/`CStr`/`CUInt`/`CULng`/`CUShort`） | `ParseExpression.vb:356` | 已覆盖 | 已覆盖 | 顶层 方法 `CommandLineRunnerTests.vb:1328`（**REPL 路径**；`TestStringConcatenationOfSpanReportsOperatorError` 用到 `CStr`）；方法 `ScriptModeStatementConformanceTests.vb:202`（`OnErrorInsideMethod_Conforms`）；源码 `:201`（`CInt("not a number")`）。嵌套：缺口；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\Conversions.vb:3280`（`ExplicitConversions1`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内转换函数关键字表达式 **35** 处；原始词频 36 多的 1 处是 `:12` 被注释掉的 `'PrintResult(CChar(Nothing))`，不计入）| — |
| XML 轴分派（`.@x` / `...<x>` / `.<x>`） | `ParseExpression.vb:1048` |已覆盖| 已覆盖 | **与 §C.3.C 的 `## XML Member Access Expressions` 行是两行各自的构造，两行各算一格（U10b 裁决）**——本行 = `ParseExpression.vb:1048` 的 XML 轴分派 **arm 行**，`### XML Member Access Expressions` 行 = `expressions.md:4787` 的 **spec 小节行**；「同一条证据不重复计入统计」是给**同一行内部**列多指针用的约定，不跨行适用。两行的用例也可互相复用为同一语言面的证据（对照 `### Async Methods` / `#### Async Sub` 的既有写法）：本行的用例写在**语句位**，`## XML Member Access Expressions` 行的用例写在**字段初始化位**；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `XmlLiteralTests.vb:188`（`MemberAccess`）。理由：该行为与容器无关——该表达式写在方法体内，语义由通用 VB 语言面决定，与脚本容器无关。（与 `## XML Member Access Expressions` 行**同一条语言面证据，但两行各算一格**，见本行开头）（U10a 审计 2026-09-17：所引源码块内 `.<x> 与 .@x 轴` 出现 **6** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeParserArmConformanceTests.vb:338`（`TopLevelXmlAxisDispatch_Conforms`，XmlOptions，语句位三轴）；**另一行（§C.3.C `## XML Member Access Expressions`）的用例 `ScriptModeQueryAndXmlConformanceTests.vb:535`（`TopLevelXmlMemberAccessAxes_Conform`，字段初始化位）亦可复用**。| — |

#### C.3.F fork 新增指令与补充行（fork 指令 4 + 种子行保留补充行 15 = 19 行）

| 语法构造 | 来源 文件:行号 | 顶层容器 | 嵌套容器 | 依据（**只认测试方法**） | 探针实测（非判据） |
|---|---|---|---|---|---|
| `#Load` 指令 | `ParseConditional.vb:474`（`ParseLoadDirective`；分派 `:85` 的 `Case SyntaxKind.LoadKeyword`——**原文引 `:84`，那是 `ParseReferenceDirective` 的调用行**）；规范 `spec\spec-load-directive.md` | 已覆盖 | 不适用（指令只能在编译单元首个 token 之前，不存在方法体内形态；检索：`grep -n '"#Load' Scripting/VisualBasicTest/*.vb` 命中 21 处，**全部位于脚本源码字符串的首行**，**实锤**） | 顶层 `ScriptTests.vb:526`（`TestLoadDirectiveDoesNotShiftDiagnosticSpan`）、`:551`、`:572`、`:590`（嵌套 `#Load`）、`:605`（循环）、`:626`、`:640`（首个 token 之后报诊断）、`:667`、`:681`；`ScriptModeSubmissionConformanceTests.vb:301`、`:316`（一提交多棵）、`:333`（载入树回读先前提交）；`ScriptTopLevelCrashTests.vb:642` / `:803`（跨树诊断逐树一次）；方法 `CommandLineRunnerTests.vb:389`（**REPL 路径**；`TestLoadDirectiveInInteractive`）、`:408`、`:473` | — |
| `#R` / `#Reference` 指令 | `ParseConditional.vb:454`（`ParseReferenceDirective`；分派 `:82` 的 `Case SyntaxKind.ReferenceKeyword`——**原文引 `:80`，那是 `Case SyntaxKind.EnableKeyword, DisableKeyword`**）；规范 `spec\spec-reference-directive.md` | 已覆盖 | 不适用（同 `#Load` 的位置约束；检索：`grep -n '"#r\|"#R' Scripting/VisualBasicTest/*.vb` 命中均位于源码字符串首行） | 顶层 方法 `CommandLineRunnerTests.vb:318`（**REPL 路径**；`TestReferenceDirective`，REPL `#r "…"`）、`:350`（**REPL 路径**；`#r` + `/imports` 混合）、`:375`（**REPL 路径**；缺引用报诊断）；`ScriptTests.vb:654`（首个 token 之后报诊断）、`:719`（与 `#!`/`#Load` 共存） | 种子行关联：`<host>` / `<implicit>` 别名语义由 U4 补测，本行只判「指令本身」 |
| `#!` shebang 指令 | `ParseConditional.vb:494`（`ParseShebangDirective`；分派 `:92` 的 `Case SyntaxKind.ExclamationToken`——**原文引 `:107`，那是 `ParseConditionalCompilationExpression` 函数**）；规范 `spec\spec-shebang-directive.md` | 已覆盖 | 不适用（规范明写「confined to the first line and, more precisely, to the first character of the file」（`spec-shebang-directive.md` §"Position on the first line"），脚本单元内不存在嵌套形态；检索：`grep -n '#!' Scripting/VisualBasicTest/*.vb` 命中 14 处，全部为源码字符串首行或内容 API 断言） | 顶层 `ScriptTests.vb:701`（`TestShebangDirective_CompilesAndRuns`）、`:719`（与 `#R`/`#Load` 共存）、`:748`（尾随表达式不变）；`InteractiveSessionTests.vb:238`（REPL 首行 no-op）、`:250`（内容 API 返回路径文本）、`:264`（多行提交不干扰）、`:275`（`ToFullString` 保留行尾） | — |
| 裸 `#` / 无法识别的指令（`BadDirectiveTrivia`） | `ParseConditional.vb:561` → `ParseBadDirective`（分派 `:95` 的 `Case Else`——**原文引 `:107`，同上是函数行**） |已覆盖| 不适用（同指令位置约束） | 检索：`grep -nE "BadDirective\|ERR_ExpectedConditionalDirective" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:48`（`TopLevelBareHash_IsReported`）；方法 `:62`（`TopLevelUnrecognizedDirective_IsReported`）。| — |
| `Static` 局部变量（无初始化器） | `statements.md:327`（`## Local Declaration Statements`）已并入本行；arm `Parser.vb:1029` |已覆盖| 已覆盖 | 检索：`grep -n "Static " Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `FlowAnalysis\RegionAnalysisTestsWithStaticLocals.vb:194`（`TestDataFlowForValueTypes`）（U10a-fix 换锚）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（U10a 审计 2026-09-17：所引源码块内 `Static（无初始化器）` 出现 **6** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:79`（`StaticLocalWithoutInitializer_InTopLevelFunction_Conforms`）。| `static-local-in-nested-class-method` 与普通模式同读数 `N=123` |
| `Static` 局部变量（带初始化器） | 同上 |已覆盖| 已覆盖 | 同上一行的检索（**实锤**，全语料零 `Static ` 命中）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenStaticLocals.vb:14`（`CodeGen_SimpleStaticLocal`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面。（与上一行**全表同一条证据，不重复计入统计**）（U10a 审计 2026-09-17：所引源码块内 `Static（带初始化器）` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:94`（`StaticLocalWithInitializer_InTopLevelFunction_Conforms`）。| `static-local-initializer-runs-once`，两模式同读数 `N=676` |
| `Static` 局部变量在顶层 `Sub` 里 | 同上 |已覆盖| 已覆盖 | 同上；实现在 `Sub` 体内 ⇒ 语义上落在**嵌套容器**栏；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `FlowAnalysis\RegionAnalysisTestsWithStaticLocals.vb:45`（`Bug13053a`）。理由：该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面（`Static` 局部写在方法体内；语义上落在嵌套栏）。（U10a 审计 2026-09-17：所引源码块内 `Static（方法体内）` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:111`（`StaticLocalInTopLevelSub_KeepsPerSubStorage_Conforms`）。| `static-local-in-top-level-sub` 读数 `N=1/N=2/N=3` |
| `Static` 在 lambda / `Structure` / 泛型方法里 | 同上 |已覆盖| 缺口 | **U10a 豁免经审计不成立 → 嵌套栏回退 `缺口`**：所引源码块为 `Semantics\RegionAnalysisTestsWithStaticLocals.vb:62`（`XmlNameInsideEndTag`）的 `<file name="a.b">`，逐词检索块内：lambda 0 处（`Sub()`/`Function()` 命中 0）、`Structure` 0 处、泛型方法 0 处（`(Of T` 命中 0）；块内唯一的 `Static a = <tag> </tag>` 写在 `Module Module1` → `Sub S(par As Integer)`（普通方法）体内，不是本行所指的三种容器。该行构造的区分项（lambda / `Structure` / 泛型方法）**全部 0 命中** ⇒ 豁免不成立。（原 U10a 豁免依据「该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面（`Static` 在 lambda / 泛型方法内的绑定与诊断）」经审计不成立，留档备查。）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:136`（`StaticLocalInTopLevelLambda_IsReported`）；方法 `:150`（`StaticLocalInStructureMethod_IsReported`）；方法 `:167`（`StaticLocalInGenericMethod_IsReported`）。| 三格读数 `BC36672` / `BC31400` / `BC32068`（原种子行记「普通模式对照为 `PROBE?`」——**该对照无效，不作任何判定依据**，U10 若补测须重跑对照） |
| `Static` 在 async 方法（带初始化器） | `statements.md:105`（`### Async Methods`） |已覆盖| 缺口 | **U10a 豁免经审计不成立 → 嵌套栏回退 `缺口`**：所引源码块为 `Semantics\StaticLocalsSemanticTests.vb:602`（`Semantic_StaticLocalInTryCatchBlock`）的 `<file name="a.vb">`，逐词检索块内：`Async` 命中 **0**（`\bAsync\b`）；含 `Static a As Integer = 1` 的三个方法 `Sub CatchBlock()` / `Sub FinallyBlock()` / `Sub NotCalledCatchBlock()` 都是普通 `Sub`（`Static` 分别落在 `Try`/`Catch`/`Finally` 块内），不是 async 方法。该行构造的区分项（async 方法）**0 命中** ⇒ 豁免不成立。候选锚点 `Semantics\StaticLocalsSemanticTests.vb` 内的 async 变体待后续核实。（原 U10a 豁免依据「该行为与容器无关——方法体在脚本单元里编译成普通方法，编译器测试已覆盖该通用语言面（`Static` 局部在异常块内的语义）」经审计不成立，留档备查。）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:182`（`StaticLocalInAsyncMethod_IsReported`）。| 读数 `BC36955`；判据 `Binding\Binder_Statements.vb:1190-1191` |
| `Err` 对象（默认状态 / `Raise` / `Clear`） | `statements.md:1666`（`### Unstructured Exception-Handling Statements`） |已覆盖| 已覆盖 | 检索：`grep -nE "\bErr\b" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenUnstructuredErrorHandling.vb:517`（`Erl_Property_InClassAndStructureTypes`）（U10a-fix 换锚）。理由：该行为与容器无关——`Err` 对象是 `Microsoft.VisualBasic` 运行时对象，绑定与容器无关。（U10a 审计 2026-09-17：所引源码块内 `Err.` 出现 **28** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:206`（`ErrObjectDefaultRaiseAndClear_Conform`）。| `err-object-default-state` / `err-raise-and-clear`，两模式同读数 |
| `On Error GoTo 0` | `statements.md:1714` |已覆盖| 已覆盖 | 检索同上（`On Error Goto 0` 零命中；语料里的 `On Error` 只有 `Resume Next` 与 `GoTo Fault` 两种形态，见方法 `ScriptModeStatementConformanceTests.vb:197`；源码 `:200`/`:205`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `ErrorHandling.vb:64`（`Error_ErrorHandler_WithGoto0andNoMatchingLabel`）。理由：该行为与容器无关——`On Error GoTo 0` 是通用非结构化异常处理语句，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `On Error GoTo 0` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:232`（`OnErrorGoToZeroInTopLevelSub_DisarmsTheHandler`）。| 原种子行探针 `on-error-goto-zero-resets-handler` 读数 `BC30544`——**探针设计错**（`On Error` 与 `Try` 不能同方法），**该读数不作依据** |
| `On Error GoTo -1` | `statements.md:1714` |已覆盖| 已覆盖 | 检索同上（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UnstructuredExceptionHandling.vb:157`（`TestOnError_GotoM1`）。理由：该行为与容器无关——`On Error GoTo -1` 是通用非结构化异常处理语句，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `On Error GoTo -1` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:266`（`OnErrorGoToMinusOneInTopLevelSub_KeepsTheHandlerArmed`）。| 探针读数 `N1=13 N2=0`（两模式同）；探针不计入 `已覆盖` |
| `Resume` / `Resume Next`（独立语句） | `statements.md:1742` |已覆盖| 已覆盖 | 见 §C.3.A 的 `#### Resume Statement` 行（**全表同一条证据**，不重复计入统计）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UnstructuredExceptionHandling.vb:1090`（`ExplicitTry`）。理由：该行为与容器无关——独立 `Resume` / `Resume Next` 是通用非结构化异常处理语句，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Resume / Resume Next` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeStatementConformanceTests.vb:707`（`TopLevelResumeStatement_IsReported`）；方法 `:713`（`TopLevelResumeNextStatement_IsReported`）。| 探针 `resume-statement-in-top-level-sub` / `resume-next-after-error` 两模式同读数 |
| `On Error` 在顶层 lambda 里 | `statements.md:1714` + `expressions.md:2832` |已覆盖| 已覆盖 | 检索（**本轮实测重跑，命令已修**）：**实际命令** `grep -nE "Sub\(\)\|Function\(\)" Scripting/VisualBasicTest/*.vb`（**本单元格里的 `\|` 是 Markdown 表格转义，命令行里是一个管道符**）→ **45 行 / 11 文件**。原文把同一模式按字面（`\|` 被当成**字面管道符**）实跑得 **0 行**，读数却记成「命中 13 文件」——**命令与读数不自洽；本轮把表内写法与实跑命令统一并注明转义，读数取实测值**。逐条看：`grep -n "On Error" Scripting/VisualBasicTest/*.vb` 只命中 `ScriptModeStatementConformanceTests.vb:200,205,221`（脚本源码）、`:154,193,217`（注释/`#Region` 标题）与 `ScriptTests.vb:321`（测试自身的断言串），**无一处 lambda 体内含 `On Error`** ⇒ 两栏仍 `缺口`；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\UnstructuredExceptionHandling.vb:1237`（`InsideLambda`）。理由：该行为与容器无关——`On Error` 在 lambda 体内的诊断（BC36668）是通用语言面，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 **lambda 体内含 `On Error` 的 lambda 1 个**（`Dim y = Sub() On Error GoTo -1`）；块内共 3 个 lambda，另一个 lambda 体内含 `Resume`（`Dim z = Sub() Resume`）；原读数 **4** 把方法层的 `On Error GoTo 0` 与 lambda 内的 `Resume` 一并计入，非本行所指的「lambda 内 `On Error`」）；**顶层栏回填（U10b，2026-09-17）**：（上方检索读数与「仍 `缺口`」/「零覆盖」类结论都是**回填前**的状态，已被本轮取代。）顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:299`（`OnErrorInsideTopLevelLambda_IsReported`）；方法 `:313`（`LambdaCallingAMethodWithOnError_Conforms`）。| 探针读数 `BC36668`（`On Error`/`Resume` 不得在 lambda 内）；探针不计入 `已覆盖` |
| `CallByName` | `expressions.md:1130`（`## Invocation Expressions` 下的运行时函数） |已覆盖| 已覆盖 | 检索：`grep -n "CallByName" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenTests.vb:12638`（`Bug529162`）。理由：该行为与容器无关——`CallByName` 是 `Microsoft.VisualBasic.Interaction` 运行时函数，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `CallByName` 出现 **2** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeRuntimeFunctionTests.vb:29`（`TopLevelCallByName_Conform`）。| 探针 `callbyname-in-nested-class` 两模式同读数 `CB=abc`；探针不计入 `已覆盖` |
| `LBound` / `UBound` | `statements.md:1870`（`### ReDim Statement`） |已覆盖| 已覆盖 | 检索：`grep -n "LBound\|UBound" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。旁证：`ScriptModeStatementConformanceTests.vb:327-338` 的源码字符串**只有 `ReDim` 与 `Erase`，无 `LBound`/`UBound`**；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenMultiDimensionalArray.vb:646`（`DifferentVarAsBound`）。理由：该行为与容器无关——`LBound`/`UBound` 是 `Microsoft.VisualBasic` 运行时函数，与脚本容器无关。（U10a 审计 2026-09-17 复核：所引源码块内 `UBound(...)` **2** 处（同在 `New Short(UBound(z, 1), UBound(y, 1)) {}` 一行）；`LBound` **0 处**——本行是 `LBound`/`UBound` 两函数合行，块内只出现 `UBound`）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeRuntimeFunctionTests.vb:51`（`TopLevelLBoundAndUBound_Conform`）。| 探针 `redim-preserve-in-top-level-sub` 输出 `LB=0 UB=4`；探针不计入 `已覆盖` |
| `IIf` / `Choose` / `Switch`（运行时函数） | `expressions.md:4187`（`## Conditional Expressions` 的运行时对偶） |已覆盖| 缺口 | 检索：`grep -n "IIf(\|Choose(\|Switch(" Scripting/VisualBasicTest/*.vb` → 命中仅 `CommandLineRunnerTests.vb:1703` 的**测试方法名** `TestCheckFlagSetBySwitch`，非脚本源码（**实锤**）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeRuntimeFunctionTests.vb:65`（`TopLevelConditionalRuntimeFunctions_Conform`）。| 探针 `iif-and-choose-in-nested-class` 两模式同读数 `P=ay`；探针不计入 `已覆盖` |
| VB 运行时字符串/转换函数（`Len`/`Mid`/`InStr`/`Replace`/`Space`/`Str`/`Hex`） | `expressions.md:1130` |已覆盖| 已覆盖 | 检索：`grep -nE "\b(Len\|InStr\|Replace\|Space\|Str\|Hex)\(" Scripting/VisualBasicTest/*.vb` → **0 命中脚本源码**（**实锤**）。`Mid(` 的 4 处命中全部是 `Mid` **赋值语句**与测试自身代码（见 §C.3.A 的 `### Mid Assignment Statement` 行）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Semantic` `Semantics\MethodCalls.vb:5108`（`Bug18133_1`）。理由：该行为与容器无关——VB 运行时字符串/转换函数是运行时库调用，与脚本容器无关。另 `Emit` `CodeGen\CodeGenVBCore.vb:2769`（`VbRuntimeTypeAndUserNamespaceConflictOutsideOfVBCore`，`Microsoft.VisualBasic.InStr`）（U10a-fix 换锚）。理由：原指针 `SelectCase_Nothing001` 源码块内仅 `CStr(Nothing)`，不属本行列举的 `Microsoft.VisualBasic.Strings` 族。（U10a 审计 2026-09-17 复核：所引源码块内 `InStr(...)` **3** 处；本行列举的 7 个函数（`Len`/`Mid`/`InStr`/`Replace`/`Space`/`Str`/`Hex`）中块内只有 `InStr` 出现）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeRuntimeFunctionTests.vb:79`（`TopLevelRuntimeStringFunctions_Conform`）。| 探针两模式同读数；探针不计入 `已覆盖` |
| `Shadows` 修饰符 | `type-members.md:1771`（`## Instance and Shared Variables` 的修饰符） |已覆盖| 已覆盖 | 检索：`grep -n "Shadows" Scripting/VisualBasicTest/*.vb` → **0 命中**（**实锤**）。arm：`Parser.vb:1029`（修饰符 arm 内的 `ShadowsKeyword`）；**嵌套栏改判 `已覆盖`（U10a 豁免，编译器测试指针）**：`Emit` `CodeGen\CodeGenLateBound.vb:2549`（`Regress14733`）。理由：该行为与容器无关——`Shadows` 修饰符是通用成员声明修饰符，与脚本容器无关。（U10a 审计 2026-09-17：所引源码块内 `Shadows` 出现 **1** 处，位于该块声明的类型体内）；**顶层栏回填（U10b，2026-09-17）**：顶层 方法 `ScriptModeErrorHandlingAndStaticsTests.vb:336`（`TopLevelShadowsDeclaration_HidesNothing_Conforms`）；方法 `:367`（`ShadowsAndOverloadsOnTheSameDeclaration_Differ`）。**判别力局限（U10b 如实记载）**：顶层那一格（`:336`）的 `Shadows` **没有可遮蔽的对象**（提交类自身无同名基成员可遮），**本身不具判别力**；本格真正的判别力落在**类体内**同形状的 `Shadows` ↔ `Overloads` 对照（`:367`）。更进一步的实测局限：提交链上同一声明写 `Shadows` / `Overloads` / 不写，**逐字同结果**（根因 `Binder_Lookup.vb:858` 的 `LookupInSubmissions`「always overload」，即已登记的 `InternalDevDocs\issues\issue-shadowing-across-submissions-reports-overload-error.md`）。故本格顶层栏判 `已覆盖` **但非完全覆盖**——顶层只钉「`Shadows` 被接受且声明生效」，遮蔽语义由类体那半承载。| 探针 `shadows-in-nested-class` 两模式同读数 `S=derived/base`；探针不计入 `已覆盖` |

#### C.3.G U2 统计、边界情形与种子行设计要点

**四值统计（**U10a 之后的现值**；口径：按「顶层容器」「嵌套容器」两列**逐格**计数；分组小节与子小节各计一次（不合并）；`不适用` 的两种理由（非可写形态 / 语义与容器无交互）不分开计数；**来源列写作「同上」的行同样逐格计数**（§C.3.F 有 3 条这样的 `Static` 行）。两栏分母均为 **238**）**：

| 状态 | 顶层容器 | 嵌套容器 |
|---|---|---|
| `已覆盖` | **132** | **179** |
| `缺口` | **76** | **20** |
| `不适用` | **30** | **39** |
| `新补` | 0（U10 填，U11 时须为 0） | 0 |
| 合计 | 238 | 238 |

**（2026-09-15 修复轮 + main 裁定轮 1/2/3 + 终态验证修复轮 + U10a 之后的现值；五轮变化见下五张表。口径同下注：**来源列写作「同上」的行同样逐格计数**。）**

**第一轮（复核修复轮）的变化**——U2 建表时两栏分母均为 **232**，计数为 `已覆盖` 129/47、`缺口` 73/150、`不适用` 30/35。变化**全部是结论性改正**，没有一处是纯读数改正：

| 变化 | 顶层 | 嵌套 | 原因 |
|---|---|---|---|
| 「声明入口 arm」行（§C.3.E）：嵌套栏 `已覆盖` → `缺口` | — | 已覆盖 −1、缺口 +1 | 原文只列顶层锚点，**无任何嵌套指针**，`已覆盖` 依据不成立 |
| 新增 `Namespace` 独立行（§C.3.E） | 缺口 +1 | 缺口 +1 | 原先并入「声明入口 arm」行 ⇒ 缺口被吸收、**少报** |
| 新增 `MyBase` 独立行（§C.3.E） | 已覆盖 +1 | 缺口 +1 | 原先并入 `### Instance Expressions` 行；顶层有负向用例，方法体内**零覆盖** |
| 新增 `MyClass` 独立行（§C.3.E） | 缺口 +1 | 已覆盖 +1 | 同上；裸顶层 `MyClass` **零覆盖**，方法体内由 `:492` 覆盖 |
| **该轮净变化** | 已覆盖 +1、缺口 +2 | 已覆盖 ±0、缺口 +3 | 分母 +3 |

**第二轮（main 裁定轮）的变化**（分母不变，仍 235）：

| 变化 | 顶层 | 嵌套 | 依据 |
|---|---|---|---|
| 「声明入口 arm」行 嵌套栏 `缺口` → `不适用` | — | 缺口 −1、不适用 +1 | 裁定 2：入口关键字写在方法体内在任何 VB 里都非法 |
| `Namespace` 行 嵌套栏 `缺口` → `不适用` | — | 缺口 −1、不适用 +1 | 裁定 2（同上；BC36965 禁止面只落顶层栏） |
| `## Logical Operators` 行 嵌套栏 `缺口` → `已覆盖` | — | 缺口 −1、已覆盖 +1 | 裁定 4：依据本就指向访问器体（嵌套容器），状态与证据矛盾 |
| **该轮净变化** | ±0 | 已覆盖 +1、缺口 −3、不适用 +2 | 分母不变 |

**第三轮（main 裁定轮 2：裁定 5/6/7）的变化**（分母不变，仍 235）：

| 变化 | 顶层 | 嵌套 | 依据 |
|---|---|---|---|
| `### Event Handling` 行 嵌套栏 `已覆盖` → `缺口` | — | 已覆盖 −1、缺口 +1 | 裁定 6 的全表扫描：该依据是 `Custom Event` 的 `AddHandler`/`RemoveHandler` **访问器体**，属另一构造（本小节 = `Handles` 子句），**不是该语法** |
| `#### Reference Parameters` 行 嵌套栏 `已覆盖` → `缺口` | — | 已覆盖 −1、缺口 +1 | 同上：原依据 `CommandLineRunnerTests.vb:1177` 是 **`ByVal`** 参数、且在脚本顶层，非 `ByRef` 形参 |
| `## Late-Bound Expressions` 行 顶层栏 | 已覆盖 ±0 | — | 依据改正（换成真正的晚期绑定用例 `CommandLineRunnerTests.vb:923`），**状态不变** |
| `#### Reference Parameters` 行 顶层栏 | 已覆盖 ±0 | — | **未按裁定 6 的初判改 `缺口`**：该格有指向语法本身的正向依据（`CommandLineRunnerTests.vb:1444` 的 `Sub M(ByRef v As Integer)`）与负向依据（`:1219` 的 `BC31396`，其诊断消息逐字含「ByRef 参数类型」）⇒ 按裁定 5 算覆盖（详见 §C.3.I 的裁定 6 落实说明） |
| **该轮净变化** | ±0 | 已覆盖 −2、缺口 +2 | 分母不变 |

**第四轮（终态验证修复轮：R1–R13）的变化**（分母 235 → **238**）：

| 变化 | 顶层 | 嵌套 | 依据 |
|---|---|---|---|
| **R8** 新增 `#Disable`/`#Enable` 警告指令行（§C.3.E） | 缺口 **+1** | 不适用 **+1** | arm 在 `ParseConditional.vb:79`→`:390`，spec 标题树**无对应小节**（`preprocessing-directives.md` 仅 6 个标题，零命中 `Enable`/`Disable`/`Warning`），ledger 亦无此行 ⇒ 按「spec 完全没有则另立行」补出 |
| **R9** 新增 `End` 语句行（§C.3.E） | 已覆盖 **+1** | 已覆盖 **+1** | `statements.md` **无 `### End Statement`**；原「已并入的 arm」清单登记了 `:996` 却**没有任何行接收**（与 `Namespace` 同类漏报）⇒ 补行。两栏都有负向用例（顶层 `BC30678`、方法体内 `BC30615`），按裁定 5 算覆盖 |
| **R9** 新增 `Stop` 语句行（§C.3.E） | 已覆盖 **+1** | 缺口 **+1** | 同上；顶层按 `README.md` §一「已知空白」的既定处置只验发射（`AssertEmits`）⇒ `已覆盖`；嵌套（方法体内合法）无任何脚本测试 ⇒ `缺口` |
| **R5** `:504` 顶层裸表达式语句 嵌套栏 `缺口` → `已覆盖` | — | 缺口 −1、已覆盖 +1 | 依据本就写着「该格即嵌套容器的负向覆盖」，与裁定 4 同类（证据与状态矛盾）；按裁定 5 改判 |
| **R13** `:469` `### Line Continuation` 嵌套栏 `缺口` → `已覆盖` | — | 缺口 −1、已覆盖 +1 | `源码 :432-435` 的续行书写在 lambda 体内（嵌套容器），从顶层栏移入本栏后构成正向覆盖（同类一致性改正） |
| **该轮净变化** | 已覆盖 +2、缺口 +1 | 已覆盖 +3、缺口 −1、不适用 +1 | 分母 **+3** |

**第五轮（U10a：嵌套栏缺口填充）的变化**（分母不变，仍 **238**；只动嵌套栏，**顶层栏零改动**，符合 U10b/U10c 的分工）：

| 变化 | 顶层 | 嵌套 | 依据 |
|---|---|---|---|
| **130 格**嵌套栏 `缺口` → `已覆盖`（七门编译器测试指针豁免） | ±0 | 已覆盖 **+130**、缺口 **−130** | `README.md` §四「判据②的两栏差异化口径」：脚本单元内的方法体／普通类型体内成员位**编译产物就是普通方法/普通类型成员**，通用语言面由编译器测试覆盖是**有效证据**；每格「依据」列都写明「该行为与容器无关」的一句话理由，且指针经脚本核对确为 `<Fact>`/`<Theory>` **方法声明行**（U10a 的 131 个指针全部机械校验通过，见 §C.3.M） |
| **1 格**嵌套栏 `缺口` → `不适用`（`:515` `### Lexical rules`） | ±0 | 缺口 **−1**、不适用 **+1** | 该小节只有 `XMLCharacter`/`XMLString`/`XMLWhitespace` 三个**词法**产生式，没有可独立书写的语法构造（理由 + 检索证据已填进「依据」列） |
| **20 格**嵌套栏维持 `缺口` | ±0 | ±0 | 其中 **14 格**落在「已知受容器影响」族（**不得**豁免，须脚本测试）；**6 格**在七门编译器测试里也查无覆盖（本轮新查证）⇒ 无证据可用，按纪律留 `缺口`。逐格清单见 §C.3.M |
| **该轮净变化** | ±0 | 已覆盖 **+130**、缺口 **−131**、不适用 **+1** | 分母不变 |

**累计（U2 建表 → 现值）**：顶层 已覆盖 129 → **132**、缺口 73 → **76**、不适用 30 → **30**；嵌套 已覆盖 47 → **179**、缺口 150 → **20**、不适用 35 → **39**。`缺口` 合计 **223 → 227 → 96**（前四轮净 +4，U10a 净 −131）。**U10b/U10c 的工作面 = 顶层栏剩余 76 格**；U10 全部收口后 `缺口` 须为 0。

**来源分解（五轮后）**：spec 标题树 **189**（`statements` 43 + `type-members` 40 + `expressions` 82 + `lexical-grammar` 18 + `preprocessing-directives` 6）＋ parser arm 补充行 **30** ＋ fork 指令 **4** ＋ 补充行 **15** = **238**（**U10a 未增删任何行，判据③「分母固定」成立**）。`缺口` 合计 **96** 格（顶层 76 + 嵌套 20），即 U10b/U10c 与 U10a 收尾的工作面。

**U2 判定中遇到的边界情形（U10 / U11 必须知道的口径）**：

1. **「嵌套容器」的多数 `缺口` 是名义缺口，不是行为缺陷。**（**已裁定，见下**；**计数更新**：裁定轮 2 后为 **152 个**（U2 建表 150 → 修复轮 153（+3：`声明入口 arm` 行改记缺口、`Namespace`/`MyBase` 两新行的嵌套栏）→ 裁定轮 150（−3：裁定 2 把 `声明入口 arm` 与 `Namespace` 两行的嵌套栏转 `不适用`、裁定 4 把 `## Logical Operators` 转 `已覆盖`）→ 裁定轮 2 152（+2：`### Event Handling` 与 `#### Reference Parameters` 两行的嵌套栏按裁定 6 从 `已覆盖` 转 `缺口`）；同时 `不适用` 35 → **37**、`已覆盖` 47 → **46**）；下述裁定与 U10 的工作面**性质不变**）`With`/`SyncLock`/`Select Case`/`While`/`Do`/`Try`/`ReDim`/`Erase`/`Continue`/`Const` 等的父容器是**通用 VB 语言面**，行为由 `Compilers\VisualBasicTest` 的普通编译测试覆盖；`Scripting\VisualBasicTest` 里只是**没有**把它们写进方法体。

   **main 裁定（U2 实施期）**：`README.md` §四 新增「判据②的两栏差异化口径」——**顶层栏**必须用 `Scripting\VisualBasicTest` 的方法；**嵌套栏**可用**七门编译器测试项目**任一的方法（**项目清单见 `README.md` §四 的表**，注意不是 `Compilers\VisualBasicTest` 那个 143 用例的 Phase2 门——初稿写错，已修），须在「依据」列写明「该行为与容器无关」的理由；但**「已知受容器影响」的族清单**（隐式 `Me`/跨提交 `Handles`/共享初始化器/事件/扩展方法/`MyBase`/脚本类构造器/顶层分支与 `Finally`）**不适用**该豁免，仍须脚本测试。

   **对 U10 的直接影响**：嵌套栏可用编译器测试指针，**不必**为约 150 格逐条补测——只有清单内的族 + 顶层栏才是真正的工作面。**U10 开工前须先按该口径重判嵌套栏的 `缺口`（裁定轮后为 151 个；重判须同时套用裁定 1「成员位＝嵌套容器」与裁定 3「清单内的族不得豁免」，见 §C.3.I）**，把可豁免的改标 `已覆盖`（附编译器测试指针 + 理由），真正要补的才保留 `缺口`。

   **验证者的豁免估算（推测，逐行仍须 U10 打开确认）**：抽样 16 个高价值嵌套缺口（`With`/`SyncLock`/`Select Case`/`While`/`Do`/`Try`/`Finally`/`Catch`/`Throw`/`ReDim`/`Erase`/`Const`/`Property`/`Operator`/`Optional`/`ParamArray`），Semantic/Emit 门里**全部**有方法级覆盖（如 `VisualBasicSemanticTest\Binding\SyncLockTests.vb:12` `<Fact()>` + `:13 SyncLockDecimal`、`Semantics\SelectCaseTests.vb:12/13`、`Semantics\EraseStatementTests.vb:15`、`Semantics\RedimStatementTests.vb:13`）。**按族清单剔除后，约 130 格属可豁免的名义缺口**（该估算基于 U2 时的 150 格；现值 151 格，差额为 `MyBase` 行的嵌套缺口——**该格属清单内的族，按裁定 3 不得豁免**）。
2. **`Namespace` 的处置（修复轮 + main 裁定轮更新）**：`Namespace` 是脚本方言的**禁止面**（BC36965）。它**已从 §C.3.E 的「声明入口 arm」行拆出、单独立行**（**顶层 `缺口`**——禁止面且无正向用例；**嵌套 `不适用`**——`Namespace` 写在方法体内在任何 VB 里都非法，按 main 裁定 2）——原先并入时该 arm 的顶层栏＝`已覆盖`，把 `Namespace` 的缺口**吸收**掉，使 `缺口` 计数少报。U11 复核 arm 差集时**不得**把它算作已覆盖。
3. **分组小节与子小节的重复计数**：`##`/`###` 级分组小节记 `不适用`（理由「子小节逐行出行」），其行为由子小节行承载。**统计 `缺口` 时只看叶小节**，否则同一缺口会被数两遍。
4. **`### Literal Expressions` 与 `### Boolean/Integer/… Literals` 的关系**：前者是 `expressions.md` 的分类入口，后者是 `lexical-grammar.md` 的词法规则，**两个来源的两行**，各自独立判定（判据③要求分母固定，不合并）。
5. **`## Late-Bound Expressions` 的「已覆盖」依据**：`ScriptModeStatementConformanceTests.vb:606` 断言的是**顶层推断字段绑定为 `Object`**（用重载决议判别），它是晚期绑定路径可达的**前置**；真正「对 `Object` 接收者调成员」的晚期绑定行为由 `CommandLineRunnerTests.vb:923`（`TestLateBoundMemberAccessDoesNotPrint`）在 REPL 顶层覆盖。两处都不覆盖嵌套容器。
6. **`#### Custom events in WinRT assemblies` 判 `不适用` 的理由要区分两种**：① 本平台无 WinRT 面；② 脚本容器不消费该投影。**不是**「fork 未实现」——`type-members.md` 该小节描述的是元数据消费规则，实现存在，只是无脚本可写形态。
7. **`Option` 语句的嵌套栏判 `不适用`**：`Option` 必须位于 `Imports` 与所有其它语句之前，脚本单元内不存在「写在方法体内」的**合法**形态，故该栏不是缺口而是不适用（与通用「方法体内语法」的处置不同）。
8. **`#Load` / `#R` / `#!` 的嵌套栏判 `不适用`**：三者的位置约束（编译单元首个 token 之前 / 文件首字符）由规范明写，已给条文出处。

**种子行已暴露的表设计要点**（原样保留）：

1. **`缺口` 与 `不适用` 在种子行里大量出现的是「嵌套容器已覆盖、顶层容器缺」** ——这正是本任务的价值定位：**顶层容器的覆盖是薄弱面**，不是整条语法没测。
2. 探针的 `PROBE?` 格（普通模式对照因缺 `/r:` 失效）在种子行里**保留标记**，U2 重跑时补 `/r:System.Linq.dll` 后消解——**不得**把 `PROBE?` 当作 `不适用` 证据。
3. **探针读数不入 `已覆盖`**（判据②的口径，与初稿相反，已修）：初稿写「『有对照且一致』的格（`已覆盖`）」——**错**。探针不是测试面（`tmp\` 不入库、冷克隆不可复核），故**探针读数一律进「探针实测」列**；`已覆盖` 只认指向 `<Fact>`/`<Theory>` 方法声明行的 `文件:行号`。「有对照且一致」只说明该行为**已被理解**，不等于**已被测试覆盖**。
4. **`GetType()` 不得用于判别静态类型**：晚期绑定调 `Object.GetType()` 返回运行时类型，`Object` 与 `Int32` 两种情形同值（本轮踩坑，见 `design-detailed.md` §1.2 第 4 条）。判别推断与否一律用**重载决议**。
5. **对照失败先当探针问题**：本轮两度把「对照没跑起来」当成「缺陷成立」。任何 `COMPILE-ERR` / 异常读数在用作证据前，必须先确认对照**本身跑通**了（引用齐、语法对、运行时配置对）。
6. **种子行的探针标记**：`PROBE?`（对照无效）与 `缺口`（有脚本读数、无对照）是**两种不同的不确定**，不得混标；`PROBE?` **不作为任何判定依据**。

#### C.3.H 本次修复轮（独立复核后的修复记录，2026-09-15）

**触发**：独立验证者对 §C.3 全表（232 行）复核后**有条件通过**——机器可核对项（分母、两栏四值计数、189 个 spec 小节标题全量出行、状态列零污染、零破表）**全部复现通过**，但找出 13 类缺陷。本轮逐条修完，记录如下（**本节只登记修复事实与口径，不改变 §C.1/§C.2 的列规约**）。

**1. 假证据读数（6 条，最严重——「证据说了假话」）**：`#### Optional Parameters`（称 `grep -n "Optional "` **0 命中** → 实测 **14 行 / 7 文件**，含 `Helpers\` 的递归口径 **30 行 / 11 文件**）、`#### ParamArray Parameters`（称 0 命中 → **8 行 / 5 文件**，递归口径 **9 行 / 6 文件**）、`### XML Namespaces`（称 `grep -n "xmlns"` 0 命中 → **17 行 / 3 文件**）、`### Async Methods` 嵌套栏（称命中仅 1 文件 → **8 文件 / 75 行**）、`Global` 限定符（称 `grep -nE "\bGlobal\."` 0 命中 → **1 命中**）、`On Error` 在 lambda 里（**命令本身写错**：`"Sub\(\)\|Function\(\)"` 在 ERE 下 `\|` 是字面管道符、**实跑 0 行**，却被写成「命中 13 文件」→ 改正模式后 **45 行 / 11 文件**）。六条的**结论多数不变**（仍 `缺口`），但读数一律改成实测值，并写明「命中全为宿主自身代码、测试自身断言或测试自身 helper 签名，**无一写在脚本源码字符串内**」。**唯一例外是 `xmlns`**：`ImportsAccumulationFailureTests.vb:55-57,64-65,68` 的 `Imports <xmlns:p="urn:test">` 确属脚本文本（经真编译），但它是**导入子句**面；本行判的是 `expressions.md:4634` 的「XML 字面量元素内的 `xmlns` 属性」，**两者不是同一构造**，故该行维持 `缺口`/`缺口`（**结论性重判**：不因为「词法上出现 `xmlns`」而吸收掉 XML 字面量面的缺口）。

**2. 错锚与格式（把不指向方法声明行的 43 处指向全部改写为约定 2 的「方法 `文件:N`；源码 `:M`」形式）**：43 处 = 42 处非属性行 + 1 处落在 `<Fact>` 属性行的 `ScriptTopLevelCrashTests.vb:247`（已改指方法 `ScriptModeSubmissionConformanceTests.vb:237`——`<Fact>` 属性在 `:236`，`约定 1` 要的是**方法声明行**；源码 `:247`）。其中 4 处是**内容错锚**（不只是格式）：`## Invocation Expressions` 嵌套栏（`:212` 是脚本**顶层**调用 → 方法 `ScriptModeStatementConformanceTests.vb:197`；源码 `:201`）、`### Object-Creation Expressions` 嵌套栏（`:648` 是脚本顶层语句 → 方法 `ScriptModeConformanceTests.vb:631`；源码 `:636`）、`## Conditional Statements` 与 `### If...Then...Else Statements`（`ScriptTests.vb:255` 是 `Case 16` → 方法 `:241`；源码 `:249-253`）、`### Multiplication Operator`（`ScriptModeConformanceTests.vb:488` 是 `"End Sub"` → 方法 `:606`；源码 `:622`）。**另发现并修正 3 处原文未列出的同类错锚**：`#### Object Initializers` 的内联简写把 `New Pair With {…}` 记成 `ScriptModeConformanceTests.vb:262`（实为 `ScriptModeStatementConformanceTests.vb:262`，方法 `:251`）；`#### Array Literals` 的简写 `:92` 在 `ScriptModeConformanceTests.vb` 里是 `AssertEmits` 的文档注释（实为 `ScriptModeStatementConformanceTests.vb:304`，方法 `:298`）；`### Line Continuation` 的 `ScriptModeConformanceTests.vb:432` 是 custom event 的 `AddHandler` 访问器（实为 `ScriptModeStatementConformanceTests.vb:421`，源码 `:432-435`；且该处是**隐式**跨行书写、无 `_`，真正的显式 `_` 续行证据是 `CommandLineRunnerTests.vb:908` 的 `1 + 2 _`）。

**3. 漏报的构造（结论性改正，不是读数改正）**：`Namespace`、`MyBase`、`MyClass` 三个 `SyntaxKind` 反查**必然命中**的构造，原先分别被「声明入口 arm」行与 `### Instance Expressions` 行**吸收**，使 U11 的差集反查卡住、`缺口` 计数少报。本轮各立独立行并正确标状态：`Namespace` 两栏 `缺口`（`grep -rn "BC36965" Scripting/VisualBasicTest/ --include=*.vb` → **0 命中**）；`MyBase` 顶层已覆盖（方法 `ScriptTopLevelCrashTests.vb:401` 的负向 BC36966）+ 嵌套 `缺口`；`MyClass` 顶层 `缺口`（裸顶层零覆盖）+ 嵌套已覆盖（方法 `ScriptModeStatementConformanceTests.vb:492`；源码 `:495`）。**`MyBase`/`MyClass` 在 `README.md` §四「已知受容器影响」族清单内（来源 issue 15）⇒ 两栏都必须脚本测试**，不得用编译器测试顶替。

**4. 「嵌套容器」的口径缺口（本轮记录，**已由 main 裁定收口**）**：`README.md` §四「嵌套容器」的定义含两个分句——「写在**方法体内**」与「在脚本里声明的类型**再嵌套一层**」——而「关键澄清」只澄清了**类型声明本身**属顶层容器，**未定义「脚本内声明类型的成员位」算哪一栏**。修复轮采用**保守口径**（`已覆盖` 的嵌套依据一律取落在方法体内的构造），并把问题上报。**main 已裁定（见下「main 裁定轮」）**：成员位属**嵌套容器**，须先写进 `README.md` §四 再执行——`README.md` 已增「补充澄清 · 脚本内声明类型的『成员位』」与「配套裁定 · 方法体内不存在合法形态的语法」两段。

**5. parser 行号（全部实跑核对）**：`Await` 上下文分派 `Parser.vb:1152` → **`:1145-1147`**；`Yield` `Parser.vb:1156` → **`:1149-1151`**；`ParseConditional.vb` 的分派 `Case` 行 `:61`(Const)/`:53`(If)/`:57`(ElseIf)/`:45`(Else)/`:49`(End) → **`:63`/`:47`/`:51`/`:43`/`:55`**（另：`Case EndIfKeyword` 在 `:59`，原文的 `:216` 是该拟古解析函数本身）；`#Load` 分派 `:84` → **`:85`**；`#R` 分派 `:80` → **`:82`**；`#!` 与裸 `#` 的「分派 `:107`」→ **`:92`**（`Case ExclamationToken`）与 **`:95`**（`Case Else`），`:107` 是 `ParseConditionalCompilationExpression` 函数。各指令的解析函数行号（`ParseConditional.vb:231`/`:454`/`:474`/`:494`/`:561`）与 `Parser.vb` 语句主分派的关键 arm 行号（`:1204`/`:1207`/`:1211`/`:1214`/`:1223`/`:1228`/`:1249`/`:1257`）经核对**原样正确**。

**6. 可复现计数（把口径写死，便于冷克隆复核）**：分母 232 → **235**；**方法声明行索引 = 21 个测试文件的 454 条 `<Fact>`/`<Theory>` 属性行**（实测）。指向计数的**修复前 / 修复后**两组（均由本轮实跑得出，命令与口径写在 §C.3.0 约定 1）：测试文件 `文件:行号` **261 处 / 去重 149 个 → 307 处 / 去重 157 个**（去重后落在方法声明行的 **114 → 118 个**）；含 parser / spec 的全表 **343 处 / 去重 209 个 → 395 处 / 去重 220 个**（增量即「方法 + 源码」两段式改写与 3 条新行）。**注**：验证者报的「162 个不同 `文件:行号` 引用」「464 行的方法声明行索引」在本修订版的 `:235`/`:281` 处**查无原文**（本文件 `grep` 无「162 个」「464 行」这样的表述），故本轮的动作是**补上实测值与口径**，而不是替换原文数字。

**6b. 「方法声明行索引」是**移动靶**——使用时必须重测（实锤，本轮踩到）**：上面的 `454` 条 / `21 个测试文件` 是**修复轮那一刻**的实测值。测量期间另一个 agent 的 U1 交付落了盘——新增 `Scripting\VisualBasicTest\ScriptModeTopLevelInferenceTests.vb`，索引随即变为 **459 条 `<Fact>`/`<Theory>` 属性行 / 22 个测试文件**（全仓 `Scripting\VisualBasicTest\` 下 `.vb` 共 31 个）。**因此**：① 该索引**不写进判据**，只作「U2 建表时刻的快照」记录；② U11 复核时**必须用同一命令重测**并报出**当时的**读数（U10 会持续新增测试文件）；③ 本表内所有 `文件:行号` 指针的**有效性**不受影响（它们指向的既有文件不会被 U10 改动行号区间——U10 只新增文件/在既有文件尾部追加用例；若某次新增插在文件中部导致既有指针漂移，U11 须按新行号重核）。

**7. 本轮未做的事**：不改任何产品源码（`Compilers\`/`Scripting\` 下的代码零改动）、不写任何测试用例、不改 §C.0–C.2 与 §C.3 的列表头。`不适用` 行中「命中情况」只有定性描述的（如 `### White Space`、`## Separators`）保持原样——本轮只改**报出具体数字**的读数。

#### C.3.I main 裁定轮（2026-09-15，对修复轮上报的 5 项未决问题逐条裁定）

**裁定 1 · 「脚本内声明类型的成员位」= 嵌套容器**（已写入 `README.md` §四「补充澄清 · 脚本内声明类型的『成员位』」）：语法写在脚本单元里声明的类型的成员位时属**嵌套容器**——依据是**该语法的宿主是方法体**，按两栏定义「写在方法体内」即嵌套容器；**实锤支撑**为探针 `r5/pin.py` 的 `script-nested-instance-method` / `script-nested-shared-method` / `script-nested-module` 与普通模式同读数。**推论**：这类格**可**用编译器测试豁免（除非落在「已知受容器影响」清单内）。

**落实**：`## Identifiers`（§C.3.D）的嵌套依据已**恢复并保留**原文的成员位锚点 方法 `ScriptModeConformanceTests.vb:570`（`NestedTypeDeclarations_Conform`）；源码 `:575`（脚本内 `Structure Point` 的成员名 `Public X As Integer`），同时保留修复轮补的方法体内锚点（`Sub Probe` 体内 `Dim inferred = 1`）。「修饰符声明前缀」（§C.3.E）的嵌套依据同样补上**成员位修饰符**锚点 方法 `ScriptModeConformanceTests.vb:606`；源码 `:612`/`:613`/`:620`/`:621`，并精化说明：`:611` 的 `MustInherit Class ShapeBase` 修饰的是**类型声明本身**（按「关键澄清」属**顶层**），而 `:612`/`:613`/`:620`/`:621` 才是**成员位**（属**嵌套**）。两行的状态本来就是 `已覆盖`，故**计数不变**。

**裁定 2 · 方法体内写本就非法的语法 → 嵌套栏 `不适用`**（已写入 `README.md` §四「配套裁定 · 方法体内不存在合法形态的语法」）：适用于 `Namespace`、`Option` 语句、声明类 arm（`Class`/`Enum`/`Delegate` 等）——方法体内写这些在**任何 VB 里都非法**，故嵌套栏 `不适用`（理由「不存在合法形态」），与 `Option` 语句的既有处置同理；**顶层栏**仍按实际判定。**落实**：「声明入口 arm」行嵌套栏 `缺口` → **`不适用`**；`Namespace` 行嵌套栏 `缺口` → **`不适用`**（其顶层栏维持 `缺口`：BC36965 是脚本特有禁止面且无正向用例）。复核 `MyBase`/`MyClass` 两行：`MyBase` 是**可写**在方法体内的表达式（`ScriptModeStatementConformanceTests.vb:482` 就是方法体内的 `Me`），**不适用本条**，其嵌套栏仍按实际判 `缺口`；`MyClass` 的嵌套栏本就 `已覆盖`（方法体内 `:495`），顶层栏 `缺口` 维持。

**裁定 3 · 「已知受容器影响」清单内的族两栏都必须脚本测试**：`MyBase`/`MyClass` 在该清单内（来源 issue 15），**豁免对它们不适用**。**落实**：`MyBase` 行嵌套栏 `缺口` **不得豁免**（已在格内写明）；`MyClass` 行顶层 `缺口` **不得豁免**（同）。两行状态与裁定前一致，**计数不变**；两格的依据文字已按裁定补写。

**裁定 4 · `## Logical Operators` 的内部不一致是真缺陷**：`:429` 嵌套栏记 `缺口`，依据却指向 `RaiseEvent` 访问器体（属嵌套容器），**证据与状态矛盾**。**落实**：嵌套栏 `缺口` → **`已覆盖`**（依据即该访问器体内的锚点 方法 `ScriptModeConformanceTests.vb:399`；源码 `:410`）。

**问题 4 的处置（约定补条）**：在 §C.3.0 的书写约定里新增**第 4 条**——「依据」列允许三类指针：① **方法声明行**（`已覆盖`/`新补` 行**必有**至少一个）；② **源码行**（标 `源码 :M`）；③ **检索命中 / helper / 探针记录行**（标 `检索`/`helper` 或句内注明）。**机械核对只校验 ① 存在且确为方法声明行**，②③ 不参与判据②，但必须标注类型、不得用裸 `文件:行号` 冒充方法指针。修复轮留下的 44 处非方法行指向因此**不再构成缺陷**（已逐条标注类型）。

**其余三项未决问题的裁定**：**问题 2**（C11 指示与约定 1 冲突）——修复轮的处理正确（写方法声明行 `:237`、格内注明 `<Fact>` 在 `:236`），原指示的 `:236` 为笔误，以表内为准。**问题 3**（`### XML Namespaces` 维持 `缺口`/`缺口`）——**接受**该结论性重判，理由文字保留（本行判的是 XML 字面量元素内的 `xmlns` 属性；`Imports <xmlns:…>` 是导入子句面、已由 `### GetXmlNamespace Expressions` 行承载，两者非同一构造）。**问题 6**（改了 §C.3.0 分母表与 §C.3.E 标题行数）——**接受**：分母须与数据行一致。

**裁定轮后的计数（与 §C.3.G 的表一致）**：顶层 `已覆盖` 130 / `缺口` 75 / `不适用` 30 / `新补` 0；嵌套 `已覆盖` **48** / `缺口` **150** / `不适用` **37** / `新补` 0；两栏分母均 **235**（130+75+30 = 48+150+37 = 235，逐格复核通过）；`缺口` 合计 **225**。**（裁定轮 2 后见 §C.3.J：嵌套 46/152/37，缺口合计 227。）**

**仍存疑、留待 U10 逐行打开的格**（本轮**未**改动）：

1. **成员位带来的 `已覆盖` 升级面未做全量重判**——裁定 1 认可「成员位＝嵌套容器」后，嵌套栏里凡**有脚本内声明类型的成员位覆盖**的格，都可能可以升级。已确认存在的成员位锚点集中在 方法 `ScriptModeConformanceTests.vb:570`（`Class Widget`/`Structure Point`/`Interface IShape`/`Enum Kind`/`Delegate Transform` 的成员）、方法 `:594`（`Module Helpers` 的成员）、方法 `:606`（`Class ShapeBase`/`Class Square` 的成员）、方法 `:631`（`Class Box`/`Class Holder` 的成员）。受影响候选（**未改**）：`### Regular, Async and Iterator Method Declarations`、`### Overridable Methods`、`### Shared Methods`、`#### Value Parameters`、`## Properties`、`### Get/Set Accessor Declarations`、`### Extension Methods` 等——**逐格是否构成正向覆盖须 U10 打开源码确认**，本轮不预判。
2. ~~**`#### Reference Parameters` 的顶层栏**~~ —— **已收口（§C.3.J）**：该格有指向语法本身的正向依据（`CommandLineRunnerTests.vb:1444` 的 `Sub M(ByRef v As Integer)`）与负向依据（`:1219` 的 `BC31396`，诊断消息逐字含「ByRef 参数类型」），按裁定 5 维持 `已覆盖`；原引的 `:1200`/`:1177` 是 `ByVal` 实参/参数，已从依据中移除。**另发现原描述的偏差**：`:1219` 的脚本源码**确有** `ByRef s As Span(Of Integer)` 形参声明（非「宿主 API 的 ByRef Span 形参」）。
3. ~~**`### Instance Expressions` 行的 `MyBase` 顶层依据**是负向用例~~ —— **已由裁定 5 收口**：负向（断言诊断）用例**算** `已覆盖`，`MyBase` 顶层维持 `已覆盖`；裁定 3 的两栏要求**不**把它变成必补项。

#### C.3.J main 裁定轮 2（2026-09-15；裁定 5/6/7）

**裁定 5 · 负向（断言诊断）用例算 `已覆盖`**（已写入 `README.md` §四；本表 §C.3.0 的 `已覆盖` 操作定义已同步补入）：本任务作者给定的判定原则逐字「崩编译器是 bug。**要么让它别崩、正常跑；要么报诊断说「脚本不支持这样用」**」——「报出诊断」是**两条合法出口之一**；`spec-scripting-dialect.md` 把 `BC36965`/`BC36966`/`BC37343` 等**明文规定**为脚本方言的规范行为，断言这些诊断的用例**正是**对该语法在该容器里的规范行为的覆盖。**落实**：`MyBase` 顶层 `已覆盖` **维持**（依据 `ScriptTopLevelCrashTests.vb:401` 断言 `BC36966`）；裁定 3 的两栏要求**不**把它变成必补项。

**逐行复核以负向用例为依据的格（裁定 5 的动作）**：全表穷举出以下负向格，**逐条打开源码确认其断言指向该语法本身**，全部**维持 `已覆盖`**：

| 行 | 语法构造 | 负向锚点 | 断言的诊断 | 是否指向该语法本身 |
|---|---|---|---|---|
| §C.3.A | `#### Error Statement` / `#### Resume Statement` | — | — | 两行本就 `缺口`（零命中），无负向格 |
| §C.3.A | `#### On Error Statement` | 方法 `ScriptModeStatementConformanceTests.vb:219` | `BC36956` | ✔ 脚本顶层 `On Error Resume Next` 本身 |
| §C.3.A | `### Unstructured Exception-Handling Statements` | 方法 `:219` | `BC36956` | ✔ 同上 |
| §C.3.A | `## With Statement` / `## SyncLock Statement` | 方法 `ScriptTopLevelCrashTests.vb:74`、`:531` | `BC30582` / `BC36943` | ✔ 脚本顶层 `SyncLock` 语句本身 |
| §C.3.A | `### RaiseEvent Statement` | 方法 `ScriptModeStatementConformanceTests.vb:370` | `BC30188` | ✔ 脚本顶层裸 `RaiseEvent` 本身 |
| §C.3.A | `## Branch Statements` | 方法 `ScriptTopLevelCrashTests.vb:225`、`ScriptModeStatementConformanceTests.vb:127` | `BC30094` / `BC30065` | ✔ 重复标签 / 顶层裸 `Exit Sub` 本身 |
| §C.3.A | `### Structured Exception-Handling Statements` 等 | 方法 `ScriptTopLevelCrashTests.vb:509`、`:745`、`:765` | `BC36943` / `BC30101` 等 | ✔ 各自语句本身（`Catch`/`Finally` 体内） |
| §C.3.A | `## Conditional Statements`/`### If...Then...Else` | — | — | 正向（无负向锚点） |
| §C.3.B | `### Variable Initializers` 嵌套 | 方法 `ScriptTopLevelCrashTests.vb:61` | `BC36937` | ✖ 已在本轮之前按 `缺口` 处置（嵌套类初始化器里的 `Await`）——保持 `缺口` |
| §C.3.B | `### WithEvents Variables` | 方法 `ScriptTopLevelCrashTests.vb:844`/`:870`（同 `### Event Handling`） | `BC37343` | ✔ `Handles` 子句本身（本行归 §C.3.B 的 `### Event Handling`，其顶层栏维持 `已覆盖`） |
| §C.3.B | `### Reference Parameters` | 方法 `CommandLineRunnerTests.vb:1219` | `BC31396` | ✔ 脚本顶层 `ByRef` 形参本身（消息逐字含「ByRef 参数类型」） |
| §C.3.C | `### Instance Expressions` | 方法 `ScriptModeStatementConformanceTests.vb:473`、`:482`、`:492`、`ScriptTopLevelCrashTests.vb:401` | `BC36966` | ✔ `Me`/`MyClass`/`MyBase` 本身 |
| §C.3.C | `## Late-Bound Expressions` | 方法 `CommandLineRunnerTests.vb:923` | `BC30491` | ✔ 晚期绑定成员访问本身（本轮换成的依据） |
| §C.3.D | `## Comments` / `### Type Characters` 等 | — | — | 本就 `缺口` |
| §C.3.E | `## Global` 限定符 等 | 方法 `ImportsAccumulationFailureTests.vb:242` | （宿主门拒绝） | ✖ 该命中是 `Imports` 子句里的 `Global`、且断言的是**被门拒绝**，非表达式 `Global.` ⇒ 该行本就 `缺口` |
| §C.3.E | `?` Print / 顶层裸表达式 / 标签 等 | 方法 `CommandLineRunnerTests.vb:1059`、`:882`、`ScriptTopLevelCrashTests.vb:225` 等 | 诊断或 no-op | ✔ 各自构造本身 |
| §C.3.E | `Option` 语句 | 方法 `ScriptModeStatementConformanceTests.vb:658`/`:666` | `BC30627` | ✔ `Option` 语句位置本身 |
| §C.3.F | 各 fork 指令 | 方法 `ScriptTests.vb:640`/`:654`/`:719` | 位置诊断 | ✔ 指令本身 |

**穷举结果**：全部负向格中，**只有 `### Variable Initializers` 的嵌套格与 `Global` 行不属于「指向该语法本身」**，而两者**原本就记 `缺口`** ⇒ **裁定 5 不改变任何格的状态**。

**裁定 6 · 依据指向 helper / 调用点 / 测试自身代码 ⇒ 不是覆盖**（与裁定 5 配套的区别：负向用例**指向该语法本身**（只断言它报错）**算**覆盖；指向 helper / 调用点实参 / 测试自身代码 **不算**，一律 `缺口`）。**全表扫描的落实**：

1. **`#### Reference Parameters` 顶层栏 —— 未按初判改 `缺口`，并上报偏差（诚实记录）**。初判的事实前提是「三个依据都指向宿主 helper 的调用点实参」，**经打开源码核对，该前提不成立**：`CommandLineRunnerTests.vb:1220` 的脚本源码逐字是 `Sub F(ByRef s As Span(Of Integer))`——**脚本顶层 `Sub` 声明里的 `ByRef` 形参**，不是 helper 的参数；且 `:1219` 断言的 `BC31396`（`ERR_RestrictedType1`）其消息逐字含「**ByRef 参数类型**」，而同文件 `:1177` 的 **`ByVal`** 同型参数不报错——**诊断正由 `ByRef` 修饰符触发**。按**裁定 5**（负向用例指向该语法本身即算覆盖），该格**维持 `已覆盖`**。**同时按裁定 6 的实体要求移除了两处真正的非该语法依据**：`:1200`（顶层 `ByVal Span` **调用点实参**报 `BC37052`）与 `:1177`（**`ByVal`** 参数，正向），并**补入**两条指向语法本身的依据：方法 `:1444`（正向 `Sub M(ByRef v As Integer)` 的 copy-out 语义）与 `Declare` 的 `ByRef` Out 参数（方法 `ScriptModeSubmissionConformanceTests.vb:448`；源码 `:453`）。**嵌套栏**因无成员位用例，本轮改记 `缺口`。
2. **`### Event Handling` 嵌套栏 `已覆盖` → `缺口`（真错锚，已改）**：原依据 `ScriptModeConformanceTests.vb:399` 是 `Custom Event` 的 `AddHandler`/`RemoveHandler` **访问器体**，而本小节（`type-members.md:820`）的产生式是 **`HandlesClause`** —— **指向的是另一个构造**。全仓 `grep -n "Handles " Scripting/VisualBasicTest/*.vb` 的 **8 行**命中（**终态验证修复轮改正读数**：原文写「7 处」）——其中 **6 行是脚本源码**（`ScriptTopLevelCrashTests.vb:365`/`:386`/`:849`/`:873`/`:897`/`:923`，**全部**在脚本顶层 `Sub` 声明里）、**2 行是散文/注释**（`ScriptTopLevelCrashTests.vb:857` 的注释、`ScriptModeSubmissionConformanceTests.vb:233` 的文档注释）。**无一处落在嵌套容器** ⇒ 嵌套栏改 `缺口`，结论不变。
3. **`## Late-Bound Expressions` 顶层栏：依据改正，状态不变**：原依据 `ScriptModeStatementConformanceTests.vb:606` 断言的是**顶层推断字段绑定为 `Object`**（晚期绑定的**前置条件**，属 `## Local Declaration Statements` / 推断面），不是晚期绑定行为本身；真正的用例 `CommandLineRunnerTests.vb:923` 原被放在「探针实测」列的「种子行关联」里。按裁定 6，已把 `:923` 移入依据（脚本源码 `:924` 的 `o.Append("x")` 就是对 `Object` 接收者的晚期绑定成员访问；负向 `BC30491`，按裁定 5 算覆盖），前置条件降为辅助说明。**状态仍 `已覆盖`**。
4. **`?` Print / `### GetXmlNamespace Expressions` / 内插字符串 / `## Identifiers` / `### Conversion Operators` / `### Async Methods` 等 8 行**：扫描命中只因格内出现 `helper` / `宿主` / `调用点` / `测试自身` 等**标注词**，逐条打开后确认**主依据是指向语法本身的方法行**（helper 等只作辅助说明或反向说明）⇒ **不改**。逐条清单：§C.3.A `### Async Methods`（辅助：检索口径）、`## With Statement`（辅助：`CommandLineRunnerTests.vb:1040` 本身就是顶层 `With`）、§C.3.B `### Event Handling`（见上，已改）、`### Conversion Operators`（源码 `:560` 的调用点即在同一脚本源码内）、§C.3.C `## Late-Bound Expressions`（见上，已改）、`### GetXmlNamespace Expressions`（helper 仅作可复用说明）、`#### Array Literals`（三处源码锚点均是数组字面量）、§C.3.D `### Integer Literals`、§C.3.E `?` Print（helper 常量已标注）、`Get`/`Set` 访问器语句（宿主侧说明为反向标注）、`AddHandler`/`RemoveHandler`/`RaiseEvent` 访问器语句（主依据 `:399` 就是脚本顶层的三访问器声明）。

**裁定 7 · 成员位升级面留 U10**：确认 §C.3.I 的候选清单（`### Regular, Async and Iterator Method Declarations`、`### Overridable Methods`、`### Shared Methods`、`#### Value Parameters`、`## Properties`、`### Get/Set Accessor Declarations`、`### Extension Methods`）**留在文档中原样不动**，作为 U10「按豁免口径重判嵌套栏」的工作面；本轮**未改**这些格。

**裁定轮 2 后的计数（与 §C.3.G 的表一致）**：顶层 `已覆盖` **130** / `缺口` **75** / `不适用` 30 / `新补` 0；嵌套 `已覆盖` **46** / `缺口` **152** / `不适用` **37** / `新补` 0；两栏分母均 **235**（130+75+30 = 46+152+37 = 235，逐格脚本复核通过）；`缺口` 合计 **227**。

**仍存疑（本轮未动，留 U10）**：

1. **成员位升级面**（裁定 7 确认留 U10，见上）。
2. ~~**`CommandLineRunnerTests.vb` 的 REPL 用例被当作「顶层容器」依据**~~ —— **已由裁定 8 收口**（见 §C.3.K）：REPL 顶层与 `.vbx` 顶层**同属顶层容器**，状态不变；动作只是**在依据列注明该用例是 REPL 路径**。

#### C.3.K main 裁定轮 3（2026-09-15；裁定 6 修正 + 裁定 8）

**裁定 6 修正 · `#### Reference Parameters` 顶层栏维持 `已覆盖`（main 撤回初判）**：main 核实 `CommandLineRunnerTests.vb:1220` 后确认——脚本源码逐字是 `Sub F(ByRef s As Span(Of Integer))`，是**脚本顶层 `Sub` 声明里的 `ByRef` 形参**，不是 helper 调用点实参；连同 `BC31396`（`ERR_RestrictedType1`）消息逐字含「ByRef 参数类型」、以及 `:1177` 的 **`ByVal`** 同型参数**不报错**（干净的判别对），按裁定 5 ⇒ 维持 `已覆盖`。main 已把「main 初判错误 + 实施者据证反驳、以证据为准」的经过写进 `README.md` §四 作为判定纪律。

**落实**：该格**本轮未发生状态变化**——实施者在裁定轮 2 已**未执行**该初判（当时即打开源码核实并上报偏差），故 §C.3.G 的计数**不产生 −1**（main 预估的 −1 对应的是「若曾误改则需回退」，实际从未误改）。裁定轮 2 已做的部分**全部保留**：移除 `:1200`（顶层 `ByVal Span` 调用点实参）与 `:1177`（`ByVal` 参数）两条真非该语法的依据、补入 `:1444`（正向 `Sub M(ByRef v As Integer)` 的 copy-out）与 `ScriptModeSubmissionConformanceTests.vb:448`（`Declare` 的 `<Out> ByRef value As Integer`）。**嵌套栏**维持裁定轮 2 的 `缺口`（成员位无 `ByRef` 用例）。

**裁定 8 · REPL 顶层与 `.vbx` 顶层同属顶层容器**（已写入 `README.md` §四）：两者**都落在提交类顶层**（`TypeKind.Submission`/脚本类，顶层语句都由合成 `<Initialize>` 承载），就「该语法在顶层容器里的行为」而言是同一容器。**配套例外纪律**：REPL 是**提交链**（每条输入一个提交、经 `PreviousSubmission` 回溯），`.vbx` 通常是**单提交**（`#Load` 只加树不加提交）——**依赖提交链**的语法在两者间确有差异，但这类族**已在**「已知受容器影响」清单内（跨提交引用 / `Handles` / `WithEvents` / 隐式 `Me`），按清单规则本就**两栏都须脚本测试**，不受本条影响。

**落实（动作 = 在依据列注明「REPL 路径」，状态一律不变）**：判据（**经终态验证修复轮扩写，见下**）：该方法的测试体里含 **① `input:=` 且走 `RunInteractive`**，**或 ② 走 `ContinueWith` 提交链（`ScriptModeSubmissionConformanceTests.vb` 的 `Repl*` 方法）**。**只引用 `.vbx` 文件或 `RunInteractive` 直接编脚本的，不标注**——例如 `TestTopLevelAddHandlerInScriptFile`（`:532`）喂的是整份 `.vbx` 文件、`TestBareExpressionInNestedSubStillErrors`（`:1088`）也是 `.vbx` 文件，都不标注。**举例改正（终态验证修复轮）**：此处原举 `TestLoadDirectiveInInteractive`（`:389`）为「不标注」的例子，但**同节表格与 `:534` 行都给它标了**——按实跑判据（`:389` 的方法体确有 `input:=` 且走 `RunInteractive`）**标注是对的、举例文字错**，已换成上例。按此判据全表扫描出 **30 处**（① 类 **21** + ② 类 **8** + 终态验证轮补漏的 `:504` **1**），已逐个改标 `**REPL 路径**`：

| 行 | 语法构造 | 加标的 REPL 方法 |
|---|---|---|
| §C.3.A `:300` | `## With Statement` | `CommandLineRunnerTests.vb:1040` |
| §C.3.A `:307` | `### Compound Assignment Statements` | `:1020` |
| §C.3.A `:308` | `### Mid Assignment Statement` | `:1040` |
| §C.3.A `:328` | `### ReDim Statement` | `:1040` |
| §C.3.B `:345` | `#### Reference Parameters` | `:1444`（`:1200` 已按裁定 6 移出依据） |
| §C.3.C `:384` | `## Late-Bound Expressions` | `:923` |
| §C.3.C `:386` | `### Literal Expressions` | `:720` |
| §C.3.C `:396` | `## Member Access Expressions` | `:685`、`:974` |
| §C.3.C `:398` | `### Default Instances` | `:1040` |
| §C.3.C `:413` | `## Cast Expressions` | `:1305` |
| §C.3.C `:421` | `### Addition Operator` | `:720` |
| §C.3.C `:427` | `## Relational Operators` | `:735` |
| §C.3.C `:429` | `## Concatenation Operator` | `:763` |
| §C.3.D `:469` | `### Line Continuation` | `:907` |
| §C.3.E `:503` | `?` Print 语句 | `:828`、`:844`、`:939` |
| §C.3.E `:509` | 语句分隔符 `:` 与语句终止符 | `:1040`（另 `ScriptTopLevelCrashTests.vb:248` 同为 REPL 路径，已加标） |
| §C.3.E `:511` | `Get` / `Set` 访问器语句 | `:1040` |
| §C.3.E `:517` | `Mid` 赋值语句（contextual 路径） | `:1040` |
| §C.3.E `:527` | 转换函数关键字表达式 | `:1328` |
| §C.3.F `:534` | `#Load` 指令 | `:389` |
| §C.3.F `:535` | `#R` / `#Reference` 指令 | `:318`、`:350`、`:375` |
| §C.3.E `:504`（**终态验证轮补漏**） | 顶层裸表达式语句 | `:882` |
| §C.3.A `:317`/`:318`（**② 类**） | `## Exception-Handling Statements` / `### Structured Exception-Handling Statements` | `ScriptModeSubmissionConformanceTests.vb:267` |
| §C.3.A `:321`（**② 类**） | `#### Throw Statement` | `ScriptModeSubmissionConformanceTests.vb:256` |
| §C.3.B `:360`（**② 类**） | `### WithEvents Variables` | `ScriptModeSubmissionConformanceTests.vb:237` |
| §C.3.B `:363` / §C.3.C `:412`（**② 类**） | `#### Object Initializers` / `### Anonymous Object-Creation Expressions` | `ScriptModeSubmissionConformanceTests.vb:202` |
| §C.3.E `:506`（**② 类**） | 块结束语句 | `ScriptModeSubmissionConformanceTests.vb:69` |
| §C.3.E `:513`（**② 类**） | 声明入口 arm | `ScriptModeSubmissionConformanceTests.vb:56` |

> **② 类的口径补入（main 裁定 10）**：`ScriptModeSubmissionConformanceTests.vb` 的 `Repl*` 方法走 **`ContinueWith` 提交链**（该文件 `:20` 的文档注释自述「the multi tree cases below run through the script API instead (`ContinueWith` chains …)」）——**提交链就是裁定 8 所称的 REPL 路径**（区别于 `.vbx` 的单提交）。原文的加严口径只认 `input:=` + `RunInteractive`，把这一整类排除了却**未在排除说明里提到**⇒ 口径文本有缺口，本轮按裁定 10 扩写并补标这 8 格。

**裁定轮 3 后的计数（与 §C.3.G 的表一致，与裁定轮 2 相同）**：顶层 `已覆盖` **130** / `缺口` **75** / `不适用` 30 / `新补` 0；嵌套 `已覆盖` **46** / `缺口` **152** / `不适用` **37** / `新补` 0；两栏分母均 **235**；`缺口` 合计 **227**。**本轮无状态变化、无计数变化**（裁定 6 修正的是「若误改则回退」，而误改从未发生；裁定 8 只加标注）。**（终态验证修复轮后见 §C.3.L：分母 238、顶层 130/76/30、嵌套 49/151/38、`缺口` 合计 227。）**

#### C.3.L 终态验证修复轮（2026-09-15；R1–R13）

**A. 阻塞项（U11 前必须修）**

- **R1 · 裁定 8 漏标 1 格（已改）**：`:504`（顶层裸表达式语句）引 `CommandLineRunnerTests.vb:882`（`TestNonFinalBareExpressionInSubmissionStillErrors`，`:883 input:=` + `:885 RunInteractive()`）却无标注——**正是我自己定的判据下的漏网**。已补标 `REPL 路径`，§C.3.K 的读数由「21 处」改为**按扩写后的判据共 30 处**（① 类 21 + ② 类 8 + R1 补漏 1）。
- **R8 · `#Disable`/`#Enable` 缺行（已补）**：arm 在 `ParseConditional.vb:79`（`Case SyntaxKind.EnableKeyword, SyntaxKind.DisableKeyword` → `:390` `ParseWarningDirective`）；`preprocessing-directives.md` **无对应小节**（6 个标题零命中 `Enable`/`Disable`/`Warning Directive`），ledger 亦无此行 ⇒ 按 §C.3.E「spec 标题树完全没有则另立行」新增（顶层 `缺口`：`grep -rn "#Disable\|#Enable"` → **0 命中**；嵌套 `不适用`：位置约束同 `#Load`）。
- **R9 · `End` / `Stop` 缺行（已补）**：arm 在 `Parser.vb:996` / `:1002`；`statements.md` 43 个标题里**无 `### End Statement` / `### Stop Statement`**（已逐一确认）。**确认了断言的漏报性质**——§C.3.E 的「已并入的 arm」清单把 `:996`/`:1002` 登记为「已并入」，但**没有任何行接收它们**（逐行验：只在 `:498` 那一行的清单里出现），与 `Namespace` 同类，只因不在三件套名单里而漏过。**结果**：补两行后**找到了既有用例**——`End` 两栏都有负向用例（`ScriptModeStatementConformanceTests.vb:388` 顶层 `BC30678`；`:395` 方法体内 `BC30615`，两栏 `已覆盖`）；`Stop` 顶层有 `AssertEmits`（`:410`；按 `README.md` §一「已知空白」的既定处置只验发射，因跑它会把进程交给调试器）⇒ `已覆盖`，嵌套（方法体内合法）无测试 ⇒ `缺口`。**可见性边界**已在两行内写明。

**B. 新裁定**

- **裁定 9 · 「顶层的分支/`Finally` 语义」只约束顶层栏（已落实）**：`README.md` §四 的清单项已改标「**仅顶层栏**」，并在该节新增「**清单项的作用域**」段写明依据——issue 16 的根因是**顶层语句不做流分析**（`MethodCompiler.vb:625` 的 TODO），而**普通方法有流分析**（issue 16 自己实测「同形状放进普通方法报 BC30101」），故容器影响只在顶层；其余清单项**两栏都约束**。
- **裁定 10 · `Repl*` 提交链方法同样标 `REPL 路径`（已落实）**：**接受口径批评**——我的加严判据（只认 `input:=` + `RunInteractive`）把 `ScriptModeSubmissionConformanceTests.vb` 的 `Repl*` 整类排除，却**未在排除说明里提到这一类**，口径文本有缺口。已把判据扩写为「① `input:=` + `RunInteractive`，**或** ② 走 `ContinueWith` 提交链（`Repl*` 方法）」并给 8 格补标：`:267`（§C.3.A `:317`/`:318`）、`:256`（`:321`）、`:237`（§C.3.B `:360`）、`:202`（`:363`/§C.3.C `:412`）、`:69`（§C.3.E `:506`）、`:56`（`:513`）。

**C. 同类错锚（已改）**

- **R3**：`:392` 的 `InteractiveSessionTests.vb:25` → **`:36`**（`:25` 是 `Imports_CrossSubmission`，脚本为 `? builder.GetType().FullName`；`GetType(Console).FullName` 在 `:42`/`:44`）。
- **R4**：`:472` 的成员位锚点 `:575` → **`:576`**（`:575` 是 `Structure Point` **类型声明行**，成员 `Public X As Integer` 在 `:576`）。
- **R5**：`:504` 嵌套栏 `缺口` → **`已覆盖`**（原依据已写着「该格即嵌套容器的负向覆盖」，与裁定 4 同类；§C.3.J 裁定 5 的穷举表漏了此锚，已在此补入）。

**D. 轻微（读数/表述，已改）**

- **R6**：`grep -n "Handles "` 的读数 7 → **8 行**（6 脚本源码 + 2 散文/注释），结论不变。
- **R7**：`ByRef` 的「全部命中」10 条 → **13 行**，并逐条分类（脚本源码 5 / 测试自身 2 / 散文注释 6）；**原文漏列的 `:1220`/`:1445` 恰是本行赖以成立的脚本源码行**。
- **R10**：§C.3.0 的「`ParseStatement.vb:933` 起是本文件内的子分派」→ 实测五个子分派在 **`:24`/`:95`/`:487`/`:1198`/`:1418`**；`:933` 落在 `ParseGoToStatement`（`:918` 起）体内。
- **R11**：主分派范围 `Parser.vb:935-1263` → **`:935-1272`**（`End Select` 在 `:1272`；`:1263` 在 `Case Else` 体内）。`README.md` 两处同步改。
- **R12**：§C.3.K 的举例文字改正——原举 `TestLoadDirectiveInInteractive`（`:389`）为「不标注」的例子，但同节表格与 `:534` 行**都给它标了**；按实跑判据标注是对的、**举例文字错**，已换成 `TestTopLevelAddHandlerInScriptFile`（`:532`）与 `TestBareExpressionInNestedSubStillErrors`（`:1088`）两个真·`.vbx` 例。
- **R13**：`:469` 的 `源码 :432-435`（lambda 体内续行）从顶层栏**移入嵌套栏**，嵌套栏 `缺口` → **`已覆盖`**；顶层栏保留两处干净锚点（`InteractiveSessionTests.vb:101`、`CommandLineRunnerTests.vb:907`），顶层 `已覆盖` 结论不受影响。

**验证者已逐条开原文核对并判为正确、本轮未动的部分**：`Parser.vb` 的 46 个 arm 行、`ParseConditional.vb` 的全部 `Case` 行、7 处内容错锚。

**终态验证修复轮后的计数（与 §C.3.G 的表一致）**：顶层 `已覆盖` **130** / `缺口` **76** / `不适用` **30** / `新补` 0；嵌套 `已覆盖` **49** / `缺口` **151** / `不适用` **38** / `新补` 0；两栏分母均 **238**（130+76+30 = 49+151+38 = 238，逐格脚本复核通过）；`缺口` 合计 **227**。**本轮 `缺口` 净 ±0**（R5/R13 各 −1、R8 +1、R9 +1 ⇒ 嵌套 −1、顶层 +1：227 = 76+151）。

**仍存疑（本轮未动，留 U10）**：

1. **成员位升级面**（裁定 7 确认留 U10，见 §C.3.I）。
2. **`#### Reference Parameters` 的嵌套栏**按裁定轮 2 记 `缺口`：成员位无 `ByRef` 用例。若 U10 认为 `CommandLineRunnerTests.vb:1444` 的 `Sub M(ByRef v As Integer)` 虽在**脚本顶层**但**其形参声明位于方法签名位**（介于「方法体内」与「顶层」之间），该格可再议——本轮**未动**。
3. **`Stop` 顶层 `已覆盖` 的强度**：依据是 `AssertEmits`（只验发射、不验运行），而 `README.md` §一「已知空白」把 `End`/`Stop` 的不可进程内跑登记为**不承诺闭合的探索项**。若 U11 认定「只验发射」不足以称 `已覆盖`，该格应转 `缺口` 或新增一个状态口径——本轮按既定处置记 `已覆盖`。

#### C.3.M U10a 嵌套栏缺口填充轮（2026-09-16；131 格）

> **范围**：只动**嵌套容器**栏（顶层栏是 U10b/U10c 的工作面，本**轮零改动**，已用解析脚本逐格比对确认）。
> **实测起数**：嵌套栏 `缺口` **151** 格（与 main 给的 151 一致；口径：把 `test-plan.md` 的 §C.3.A–F 六个表体按未转义 `|` 切列，取第 5 列 == `缺口` 计数；脚本 `tmp\vortex-logs\script-mode-coverage-parity\count_nested.py`）。**收数** `缺口` **20** 格。

**处置结果（131 = 130 豁免 + 1 转 `不适用`）**：

| 处置 | 格数 | 依据 |
|---|---|---|
| 编译七门测试指针**豁免** → `已覆盖` | **130** | `README.md` §四「判据②的两栏差异化口径」：嵌套栏可用七门编译器测试任一方法，**但「依据」列必须写明「该行为与容器无关」**——本轮 130 格**每格都写了**该理由；未写理由的按 `缺口` 处理（本轮因此把 8 个候选格退回 `缺口`，见下） |
| 转 `不适用`（理由 + 检索证据） | **1** | `:515` `### Lexical rules`（`expressions.md:4254`）：只有 `XMLCharacter`/`XMLString`/`XMLWhitespace` 三个词法产生式，无独立可写形态。检索命令 `sed -n '4254,4272p' expressions.md` → 输出为 `antlr` 词法产生式块 |
| **维持 `缺口`** | **20** | 分两类，见下表 |

> **「全家共用一条证据」的行已在「依据」列括注**「全表同一条证据，不重复计入统计」（如 `#### Async Sub` 与 `### Async Methods` 共用 `CodeGenAsyncTests.vb:67`）。

**指针分布（130 格豁免所用的门）**：

| 门 | 条数 | 备注 |
|---|---|---|
| **Emit**（`Compilers\VisualBasicEmitTest\`，4370 用例） | **99** | 主力：`CodeGen\CodeGenTryCatchThrow.vb` / `CodeGenWithBlock.vb` / `CodeGenObjectInitializer.vb` / `ErrorHandling.vb` / `XmlLiteralTests.vb` 等 |
| **Semantic**（`Compilers\VisualBasicSemanticTest\`，5826 用例） | **28** | `Semantics\QueryExpressions.vb`（LINQ 全族）/ `Semantics\Conversions.vb` / `Semantics\UnstructuredExceptionHandling.vb` 等 |
| **Syntax**（`Compilers\VisualBasicSyntaxTest\`，4070 用例） | **3** | 拟古语句族（`Wend`/`EndIf`/`Gosub`）与过时 `Get` 语句 |
| Phase2 / Symbol / IOperation / CommandLine | **0** | 未使用；**尤其未用只有 143 用例的 `Compilers\VisualBasicTest`（Phase2 门）当通用语言面的来源**（`README.md` §四 明令警告的坑） |

**维持 `缺口` 的 20 格**：

*（甲）落在「已知受容器影响」族 ⇒ **不得**豁免，**必须**脚本测试（`README.md` §四 清单，来源 `issues\README.md` 条目 04–20）——**14 格**：*

| ledger 行 | 构造 | 命中族（issue） |
|---|---|---|
| `### Event Handling`（`:410`） | `Handles` 子句 | 跨提交引用 / `Handles` / `WithEvents`（14、19） |
| `### Extension Methods`（`:411`） | 扩展方法声明 | 扩展方法（04） |
| `### Extension Method Collection`（`:463`） | 扩展方法集合（`Imports` 面） | 扩展方法（04） |
| `### Shared Constructors`（`:415`） | 共享构造 / 共享字段初始化器 | 共享字段与属性初始化器（05、06、09） |
| `### Variable Initializers`（`:423`） | 字段初始化器 | 共享字段与属性初始化器（05、06、09） |
| `#### Regular Initializers`（`:424`） | 常规初始化器 | 同上 |
| `#### Array-Size Initializers`（`:426`） | 数组上界初始化器 | 同上 |
| `### Automatically Implemented Properties`（`:432`） | 自动实现属性（属性初始化器） | 同上 |
| `### Read-Only Variables`（`:421`） | `ReadOnly` 字段（含初始化器） | 同上（05、06、09） |
| `### Custom Events`（`:417`） | 自定义事件 | 事件与 `RaiseEvent`（13、20） |
| `Custom Event` 声明头（`:586`） | `Parser.vb:1113`（`CustomKeyword`） | 同上（13、20） |
| `AddHandler` / `RemoveHandler` / `RaiseEvent` 访问器语句（`:574`） | `Parser.vb:828-836` | 同上（13、20） |
| `MyBase` 表达式（`:577`） | `MyBase` | `MyBase` / `MyClass`（15） |
| `## Instance and Shared Variables`（`:420`） | 实例/共享字段声明 | 隐式 `Me` / 共享成员访问实例成员（08）——**保守判定**：该行覆盖「字段声明本身」，与 08 的缺陷面不完全重合，但字段声明**在脚本类里也是提交类成员**，按「判不准就留 `缺口`」的纪律不退让 |

*（乙）七门编译器测试里也查无覆盖 ⇒ **无证据可用**——**6 格**：*

| ledger 行 | 构造 | 本轮检索读数 |
|---|---|---|
| `### Default Instances`（`:460`） | 默认实例 / 组类（`My.Forms`） | `tsearch.py "MyGroupCollection\|DefaultInstance"` → **0 命中**（**实锤**）。注意：**`### Default Instances` 是「默认实例」而非 `With` 块的默认实例成员访问**——后者另由 `ScriptModeStatementConformanceTests.vb:251` 覆盖 |
| `## Overloaded Method Resolution:`（`:466`） | 重载决议 | `Semantics\OverloadResolution.vb` 有大量用例，但**本轮未能确证哪一条落在脚本容器的嵌套栏**（**推测**：该族在 `issues` 层无缺陷记录；按「未确证即留 `缺口`」的纪律不退让） |
| `?` Print 语句（`:565`） | `ParsePrintStatement`（`Parser.vb:1249`） | `tsearch.py "PrintStatement"` → **0 命中**（**实锤**）；编译器测试无 `?` 打印语句用例 |
| `Empty` 空语句（`:567`） | `ParseEmptyStatement`（`Parser.vb:1204`）；`EmptyToken` 由扫描器在 **EOF/多余 trivia** 处产生（`Scanner.vb:210`/`519`/`527` → `TokenFactories.vb:466`），**不是常规可写形态** | `tsearch.py "EmptyStatement"` 命中全部在 `Syntax\Generated\Syntax.Test.xml.Generated.vb`（**语法树生成器自动产出的形状测试**，只验节点构造、非语句语义）⇒ **不构成覆盖**，留 `缺口` |
| 过时 `Set` / `Let` 赋值语句（`:570`） | `Parser.vb:1095` → `ParseAssignmentStatement` | `tsearch.py "ObsoleteSet\|ObsoleteLet"` → **0 命中**（**实锤**）；两关键字在本 fork 里解析为普通赋值，**无「过时」诊断** |
| `IIf` / `Choose` / `Switch`（运行时函数）（`:615`） | `Microsoft.VisualBasic.Interaction` | `tsearch.py "IIf\(\|Choose\(\|Switch\("` → **0 命中**（**实锤**）；`CallByName` 有（`CodeGenTests.vb:12638`）而这三个无 |

> **两类的分界不是「有无指针」，而是「有指针也不许用」与「根本没指针」。** 甲类即便找到编译器测试指针也**不得**豁免（清单纪律）；乙类没有可用证据。**14 + 6 = 20**，与收数一致。

**纪律落实（本轮自我检查）**：

1. **分母固定（判据③）**：本轮**未增删任何 ledger 行**。机械核对：改前/改后按未转义 `|` 切列的行数均为 **238**，逐行行号与构造名零差异（脚本 `cmp.py` 比对 `test-plan.before.md` 与现值，输出 `before rows 238 after rows 238`，无 `DIFF` 行）。
2. **指针实体要求（判据②）**：**131 个指针全部机械校验为 `<Fact>`/`<Theory>` 标注的方法声明行**——校验脚本 `verify_ptrs.py` 对每个指针开原文核对 ① 该行是 `Sub`/`Function` 声明行、② 符号名逐字相符、③ 向上最近的非属性/非空行是 `<Fact>`/`<Theory>`。读数 **`pointers OK=131 BAD=0`**。
3. **未确证即留 `缺口`**：8 个候选格曾进入「拟豁免」名单，因**找不到确证的编译器测试指针**而**退回 `缺口`**（未凭「应该有人测过」的印象豁免）：`:460`（默认实例）、`:466`（重载决议）、`:565`（`?` 打印）、`:567`（空语句）、`:570`（过时 `Set`/`Let`）、`:574`（事件访问器）、`:586`（`Custom Event` 头）、`:615`（`IIf`/`Choose`/`Switch`）。其中 `:574`/`:586` 另受族清单约束（甲类），其余 6 格即乙类。
4. **`Nothing`/`Global.` 的落点纠正**：`:544` 与 `:590` 分别有可用指针（`Conversions.vb:22`、`CodeGenTests.vb:1868`），**均已豁免**。曾误判 `:544` 为「无用例」（`tsearch "Nothing Is"` → 0 命中），**换模式 `Nothing OrElse` 后命中**——教训：零命中主张必须换模式复核（与 §C.3.0 的 `<` 检索陷阱同族）。
5. **未写理由的按 `缺口`**：130 格**每格**都写了「该行为与容器无关——<一句话>」。理由分四种模板：方法体内语句（`R_METHOD`）、普通类型成员位声明（`R_DECL`）、方法体内表达式（`R_EXPR`）、及针对具体构造的专门理由（如 `xmlns:` 属性名、`#Region` trivia、`Shadows` 修饰符、`Stop` 在方法体内合法）。
6. **`MyBase` 的旧判据更正（实锤）**：`§C.3.L` 的「仍存疑 2」把 `#### Reference Parameters` 的 `CommandLineRunnerTests.vb:1444` 的 `Sub M(ByRef v As Integer)` 列为「介于方法体内与顶层之间、可再议」。**本轮核实：该 `Sub M` 是脚本顶层的 `Sub` 声明**（`Sub` 声明在源文件位置属顶层容器），故它**不是**嵌套栏依据 ⇒ 该格嵌套栏维持 `缺口`（`README.md` §四「两栏的精确定义」：判据 = 该构造**在不在方法体内**，`Sub M` 本身不构成「在方法体内」）。同理 `ScriptModeStatementConformanceTests.vb:492` 的 `MyClass` 写在 `Function Describe()` **体内**，确属嵌套（§C.3.E `MyClass` 行的既有判定正确）。

> **后续变动（本节各读数是 U10a 轮当时的读数，未改写）**：U10a-audit 轮回退 `:606` / `:607`，U10a-fix2 轮回退 `:419` / `:466` ⇒ 嵌套栏 `缺口` 现值见 §C.3.N。

#### C.3.N 术语「所引源码块」与计数口径（U10a-fix2，2026-09-17）

**背景**：§C.3.A–F 的「依据」列此前写作「审计块内 `X` 命中 **N** 次」，但**「审计块」在本文件里没有任何定义**，其含义只存在于 git-ignored 的 `tmp\` ⇒ 冷克隆复核不到（违反 §C.3.H 第 1 条自定的读数纪律）。本轮把该措辞全部改为自描述，并删去「构造实见且为嵌套容器形态，豁免成立」这类**自我背书**措辞（改为纯事实陈述）；术语与口径在此定义一次。

**术语 · 所引源码块**：该行「依据」列所引**编译器测试方法的方法体内编入的 VB 源码**——即 `<file name=…>` 夹具的块内文，或赋给变量的 `<![CDATA[…]]>` 内文；**不含** `expectedOutput` / `VerifyIL` 等断言串，也不含测试方法自身的 VB 代码。132 格的「依据」括注因此改为「所引源码块内 `X` 出现 **N** 处，位于该块声明的类型体内」；另 2 格（`:606` / `:607`）的「审计块为 …」同样改为「所引源码块为 …」。

**计数口径**：格内数字是该构造**在所引源码块内实际书写出现的次数**；对声明类构造即**声明数**（用 `End X` 配对数交叉核对）。**不是原始词频**——`\bX\b` 原始词频会把字符串字面量、IL 文本与诊断断言串一并计入。示例（本轮复算，实锤）：`Emit` `CodeGenTryCatchThrow.vb:889`（`EmptyTryOrEmptyFinally`）所引源码块内 `\bFinally\b` **原始词频 5**，**声明数 4**（多出的一处是字符串 `"Finally 1 "`），该块 `End Try` 配对亦为 4 组 ⇒ `:381` 行记的正是 4。同法抽核两格：`:434` 的 `## Operators` 记 7（原始词频 14 = 7 声明 + 7 `End Operator`）、`:436` 的 `### Binary Operators` 记 20（原始词频 40 = 20 + 20）、`:475` 的 `## Cast Expressions` 记 32（= 块内 `DirectCast(` 书写次数）。**本轮未逐格复算全部 134 格**，只做上述抽核。

**本轮（U10a-audit 与 fix2）共回退了 4 格**：`:606` / `:607`（U10a-audit 轮：`Static` 在 lambda/`Structure`/泛型方法里、`Static` 在 async 方法里）+ `:419`（fix2：`## Constants`）+ `:466`（fix2：`## Overloaded Method Resolution:`）。

**fix2 两格回退的依据要点（均可源码复核）**：`:419` 的锚 `Emit` `CodeGen\CodeGenConstLocal.vb:124`（`TestIntegerConstLocal`）所引源码块内 `Module C` 在 indent 0、两处 `const i`/`const j` 在 indent 8（`Sub Main()` 体内）⇒ 块内是**局部常量**，属 `statements.md:327` `## Local Declaration Statements`（该行 `LocalModifier : 'Static'` / `'Dim'` / `'Const'`）面，不是本小节所指的**类型成员常量**（`type-members.md:1693`「a *constant* is a constant value that is a member of a type」）；`:466` 的锚 `Emit` `BreakingChanges.vb:618`（`TestCrossLanguageOptionalAndParamarrayForHandles`）所引源码块内 `Goo`/`Goo2` 只有 **4 处 `Function` 声明、无任何调用**，块内 6 条 `ERR_EventHandlerSignatureIncompatible2` 出自 `Handles` 委托绑定兼容检查（逐方法 × 逐 `Handles` 目标）⇒ 没有发生重载挑选。

**`## Constants` 顶层栏维持 `已覆盖`（fix2 追加的源码依据）**：`Parser.vb:2093` 的 `Select Case Context.BlockKind`（函数 `ParseVarDeclStatement` 声明于 `:2086`）在 `:2102` 列出 `SyntaxKind.CompilationUnit` ⇒ `:2103` `isFieldDeclaration = True` ⇒ `:2110`/`:2111` 走 `SyntaxFactory.FieldDeclaration`；即**脚本顶层（`BlockKind = CompilationUnit`）的 `Const` 落成脚本类的成员常量**，正对本小节。**如实记录一处 spec 不完整**：fork 自己的 `spec\spec-scripting-dialect.md:56` 逐字写「The mapping is exhaustive over the four forms」，其四形态（`:58` `Dim`→字段、`:60` `Sub`/`Function`→成员、`:62` 类型→嵌套类型、`:64` 顶层可执行语句→初始化器）**未列 `Const`**——顶层 `Const` 落成成员常量是**解析器行为**（`:2102` 实锤），四形态表没覆盖它。这是 spec 的不完整、不是代码缺陷，也不影响本行判定；本轮不改 spec 文件（不在本轮范围）。

**回退后的四值（分母仍 238；本轮只动嵌套栏，顶层栏零变化）**：嵌套 `已覆盖` **179** / `缺口` **20** / `不适用` **39**；顶层 `已覆盖` **132** / `缺口` **76** / `不适用` **30**。**与 §C.3.G 的现值表一致**（该表在改动前就写着嵌套 179/20/39，而台账当时是 181/18——本轮的两格回退把台账对齐到该表）。`L419` / `L466` **无依赖行**：全表检索「（与 `XXX` 行全表同一条证据，不重复计入统计）」类括注与 `:419`/`:466` 的行号引用，没有一行引用这两行 ⇒ 无需级联回退。

#### C.3.O 方法指针行号重扫轮（U10c，2026-09-17；**只动「依据」列**）

**问题（实锤）**：§C.3.A–F 的「依据」列里指向 `Scripting\VisualBasicTest\*.vb` 的**方法指针行号大面积过期**。根因有二：① `ScriptModeStatementConformanceTests.vb` 在顶部**增了 5 行**（类文档注释的 `<para>` 段），该文件内**所有**行号整体下移 **5**——台账里早于那次改动写入的指针未同步，系统性偏移 **+5**；② `ScriptModeSubmissionConformanceTests.vb` 在 `:426` 之前**插入了大量新测试**，该文件中段指针偏移 **+322**。**本轮行号改正的偏移分布**（共 **79** 个可计偏移的改正事件）：`+5` × **64**、`+322` × **10**、`+1` × **5**。另有 1 处小偏移指向的括注是「原文引 …」即**引用旧值**的说明文字（`:571` 的裸 `:237`），按原样保留、不改。

**机械核对的口径（`tmp/u10c/verify-ptrs.py`，可复跑）**：数据行 = C.3.A–F 的 **238** 行；指针事件 = `` `文件.vb:N` `` 与 **裸** `` `:N` ``（裸指针的文件 = 同格内最近的前一个带文件名指针）；前置 6 字内含「源码」的事件是**源码行指针**，不参与核对。**判据②**：`已覆盖` 行**必须**至少有一个带文件名指针落在 `<Fact>`/`<Theory>` **方法声明行**；**双锚**：指针后括注里第一个「是该文件的 `<Fact>`/`<Theory>` 方法名」的反引号标识符，该指针**必须**指向**那个**方法的声明行（专门抓「偏移后碰巧撞上相邻方法声明行、行号合法但方法指错」）；**两段式**：括注以「源码」开头的 `方法 + 源码` 紧凑式，其 `N` 也必须是方法声明行。

**读数（改前 → 改后）**：判据②违规 **17 → 0**；双锚违规 **60 → 0**；两段式违规 **5 → 0**；**合计 82 → 0**。

**本轮改法**（共 **82** 个指针事件、涉及 **58** 个数据行；逐条对照见 `tmp/u10c/ptr-fix-map.tsv`）：① **79** 个指针行号改到真实声明行；② 原**只有行号、没有方法名**的指针，一并**补上该行的方法名**（后续漂移可被双锚机械发现）；③ 其中 **5** 个是**该指向脚本源码行却写成带文件名指针**的（`:390` / `:391` / `:456` / `:473` / `:493`），按约定 2 改写为「方法 `文件:N`；源码 `:M`」两段式并顺带补名；④ `:564`（`Option` 语句）整条指针链 9 个行号按同一 +5 偏移改正并逐个补名；⑤ `:451` 的 `/` 连结双指针分别在两端补名；⑥ 两处括注随之更正：`:390` 改好后方法行 `:332` 与源码指针 `:332` 撞号，源码指针按内容核对（`ReDim Preserve dynamicArr(4)` 实测在 `:337`）改为 `:337`；`:571` 的「`<Fact>` 属性在 `:236`」改为 `:237`（方法行 `:238` 的上一行）。

**纪律读数**：状态两栏**零改动**——顶层 `已覆盖` **208** / `缺口` **0** / `不适用` **30**、嵌套 `已覆盖` **179** / `缺口` **20** / `不适用` **39**（改前 = 改后）；数据行 **238**（无增删），行名序列逐字一致（机械比对通过）；逐列变动：`依据` **58** 格，`构造` / `来源` / `顶层容器` / `嵌套容器` / `探针实测` 各 **0** 格。

**范围说明（不改的地方）**：§C.3.H–N 是历轮**带日期的过程记录**，其行号读数是**当时**的读数、本轮**按原样保留**（与 §C.3.N 末尾「本节各读数是 … 当时的读数，未改写」同一处置）；§2.1.1 的两处指针指的是**测试自身代码行**（不是方法指针），亦不动。

**遗留（存疑，未改）**：双锚能机械生效的前提是「括注里写了方法名」。改后仍有 **73** 个带文件名的指针落在真实 `<Fact>` 方法声明行上、但**括注写的是该方法的内容描述而非方法名**（如 `` `ScriptTopLevelCrashTests.vb:299`（`Shared Sub RaiseIt`） ``），机械核对只能验到「落在某个 `<Fact>` 方法声明行」、**验不到「是不是本该指的那个方法」**。**推测**（非实锤）：逐条抽读了其中 **38** 个的所引方法体，方法名与括注描述**全部相符**（清单与证据见 `tmp/spot.txt`），故判无残留错指；但未做全量内容比对。——另需说明：本轮**未改** `ScriptModeConformanceTests.vb:488`（`:485` 行括注为「原文引 `Implements` 而非乘法」，属**引用旧值**的说明文字）与 `ScriptModeTopLevelInferenceTests.vb:156`（`:498` 行，那是**常量定义行**、该常量在 `:156` 被用作脚本源码，不是方法指针）。
