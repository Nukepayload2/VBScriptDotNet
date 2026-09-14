# 测试计划（script-top-level-crashes-2）

## 1. 层间分工

| 层 | 落点 | 本项目里管什么 |
|---|---|---|
| L1 解析 | `Compilers\VisualBasicSyntaxTest` | 只判语法树形状；本任务五族**均不是**解析期缺陷，预期零新增 |
| L2 语义 | `Compilers\VisualBasicSemanticTest` | 绑定期诊断（`BC36966` / `BC30101` / 新构造器码）与「绑定不抛异常」 |
| L3 API | `Compilers\VisualBasicEmitTest` / `VisualBasicSymbolTest` | 成员表形状、IL 体级断言、发射不抛异常 |
| L4 REPL/宿主 | `Scripting\VisualBasicTest` | 宿主可见症状：编译要么成功要么给诊断、会话存活、运行结果正确 |

**层间不重复**：L2 负责「诊断对不对」，L3 负责「产物形状对不对」，L4 负责「用户看到什么」。同一形状在三层都出现的，用不同的断言切面区分（`test-plan` 第一轮同一口径）。

## 2. 逐单元用例表

> 每个单元至少一条**判别性**用例：把该单元的改动撤销后，这条用例必须失败。只加断言不加用例的，门计数不变但要核 `输出 dll mtime > 被测源文件 mtime`。

### U4 · 显式 `MyBase`

| 层 | 用例 | 断言 |
|---|---|---|
| L2 | 顶层裸 `MyBase.ToString()`；顶层 `Sub` 内；顶层 `Shared Sub` 内 | 三者都报 `BC36966`，且 `GetDiagnostics()` **不抛** |
| L3 | 同形状的 `VisualBasicScript.Create(...).Compile()` | 诊断集合非空、`Compile` 返回而非终止 |
| L4 | REPL 单条提交里写 `MyBase.ToString()` | 会话打印 `BC36966` 并**继续**（下一条提交仍执行） |
| 对照 | 普通 `Module` / `Structure` 里同形状 | `BC32001` / `BC30044` 不变 |

### U3 · `Handles` 子句

| 层 | 用例 | 断言 |
|---|---|---|
| L3 | 顶层 `WithEvents` + 顶层 `Sub … Handles` | 编译成功；**投递可观测**：raise 后计数 = 1 |
| L3 | 顶层 `Shared WithEvents` + 顶层 `Shared Sub … Handles` | 同上 |
| L4 | REPL 两步提交：先声明 `WithEvents`，再声明带 `Handles` 的 `Sub` | 该形状被判为**报错**（跨提交 `WithEvents` 变量上的 `Handles` → `BC37343`，判据 `SourceMemberMethodSymbol.vb:707-711` 的 `TryCast(Me.ContainingType, SourceNamedTypeSymbol)` 失败分支），由独立单元 U9 落地；用例断言诊断与「会话存活」 |
| 对照 | 嵌套类里的同形状 | 行为不变 |

### U2 · 实例事件 `RaiseEvent`

| 层 | 用例 | 断言 |
|---|---|---|
| L3 | 顶层 `Event E` + 顶层 `Sub` 里 `RaiseEvent E` | 编译成功；handler 计数 = 1 |
| L3 | 顶层 lambda 里 `RaiseEvent E` | 编译成功（不终止） |
| L4 | REPL：`Event` + `AddHandler` + 顶层 `Sub` raise | 会话存活、计数 = 1 |
| 对照 | 顶层 `Shared Event` 路径 | 行为不变（既有 `TopLevelSharedEvent_RaiseReachesTheHandler`） |
| 对照 | 嵌套类的实例事件 raise | 行为不变 |

### U1 · 提交类实例构造器

| 层 | 用例 | 断言 |
|---|---|---|
| L2 | `Sub New()` / `Sub New(x As Integer)` | 报新码、诊断位置在声明处 |
| L2 | `Shared Sub New()` | **不报**新码（对照） |
| L3 | `VisualBasicScript.Create(...).Compile()` | 不终止；诊断集合含新码 |
| L4 | REPL 里 `Sub New()` | 会话打印新码并继续 |
| 对照 | 普通类里三个构造器形状 | 行为不变 |
| 注册链 | `DiagnosticTests.TestIsBuildOnlyDiagnostic` | 新码在册（Semantic 门） |

### U5 · 顶层 `Finally` 跳出

| 层 | 用例 | 断言 |
|---|---|---|
| L2 | 顶层 `Try/Finally` 里 `GoTo` 到块外标签 | 报 `BC30101`，位置 = `GoTo` 的标签 |
| L2 | **同块的其余分支种类**：`Return`、`Return <值>`、`Exit For`/`While`/`Do`/`Select`、`Continue For` | 同样报 `BC30101`（只按 `GoTo` 修会漏掉这一整组） |
| L2 | 嵌套 `Finally`、`Finally` 在 `Using` / `Catch` 内、多 `Catch` 的 `Try`、`Await` 之后的 `Finally` | 同上 |
| L3 | 同形状 | `Emit` 不抛、不产出可运行产物 |
| L4 | REPL 同形状 | 会话打印 `BC30101` 并继续 |
| 对照 | 其余 10 个块跳出形状 + `goto-into-*`（跳**进**保护块，由绑定层另行报错） | **零新增诊断**（回归锁） |

## 3. 无副作用纪律（强制）

- 用例一律用内存 API：`VisualBasicScript.Create` / `VisualBasicScript.RunAsync` / `CommandLineRunner` + 内存 `StringReader`/`StringWriter`；`ScriptOptions` 只加内存引用（`AssemblyMetadata.CreateFromImage`）。
- **禁止**：网络请求、写文件、起进程、写注册表。
- 多树（`#Load`）形状用内存 `SourceReferenceResolver`（第一轮已建立的 `InMemorySourceReferenceResolver` 写法）。
- `BuildPaths.TempDir` 传**已存在的目录**（`AppContext.BaseDirectory`），避免创建目录。
- 探针脚本（`tmp\probes\`）不属于测试面，不得被测试引用。

## 4. 全量回归口径

实现完成后必须全绿：

| 门 | 命令 |
|---|---|
| 宿主 / Scripting | `dotnet build Scripting\VisualBasicTest` 后直接跑程序集 `-automated`（`dotnet test` 对本 MTP 项目**静默跑 0 个**；须核 `TestCasesToRun > 0`） |
| 七门 | `powershell -File scripts\verify-vb-compiler-tests.ps1`（throw-on-mismatch；七门基线在 `:8-14`） |
| 公共 API | `PublicAPI.*.txt` 零增量 |
| 共享发射层 | `Compilers\Core\Portable\CodeGen\` 零 diff |

**新增用例会推高门计数** ⇒ 收口时同步 `scripts\verify-vb-compiler-tests.ps1` 的期望值（只改数字，不改判据）。

## 5. VB 特有语法维度（U7 的矩阵骨架）

U7 的矩阵按「声明 × 修饰 × 语句 × 引用 × 选项」五维铺，逐格断言「不终止」。格子清单见 `design-detailed.md` §U7 的表。**每格至少一条**，空格即为下一轮修复线索。

## 6. C# 脚本模式测试的族谱（U7 的用例形态蓝本）

上游 `{{Roslyn}}\src\Scripting\CSharpTest\`（`ScriptTests.cs` / `CommandLineRunnerTests.cs` / `InteractiveSessionTests.cs` / `ScriptOptionsTests.cs` / `ObjectFormatterTests.cs`）。**只借用例形态，不借行为判据**——行为判据以本仓 spec 与普通上下文实测为准。按族谱逐族在 VB 侧找对应格：

| C# 测试族 | 覆盖什么 | VB 侧对应格 |
|---|---|---|
| `CompilationChain_*`（Fields / GlobalNamespaceAndUsings / CurrentSubmissionUsings / UsingDuplicates / GlobalImports / Accessibility / SubmissionSlotResize / GenericTypes / GenericMethods / Ldftn / NestedTypesClass / NestedTypesStruct / InterfaceTypes） | 跨提交的状态、导入、可见性、槽位扩容、泛型、方法指针 | 跨提交字段读写、`#Load` 多树、`Imports` 累积、嵌套类型可见性 |
| `AnonymousTypes_TopLevel_*`（MultipleSubmissions / Redefinition / Empty） | 匿名类型跨提交的身份与重定义 | VB 匿名类型（`Key`/非 `Key`）× 跨提交 |
| `Submissions_ExecutionOrder1/2` | 提交执行顺序 | REPL 多提交顺序 |
| `Fields_Visibility` / `PrivateTopLevel` / `NestedVisibility` | 顶层成员的可见性 | 顶层 `Private`/`Public`/`Friend` + 嵌套类型内的访问 |
| `ObjectOverrides1/2/3` | `ToString`/`Equals`/`GetHashCode` 覆盖 | 顶层 `Overrides` |
| `Exception` / `ExceptionInGeneric` / `PreservingDeclarationsOnException` | 异常与提交存活 | `On Error`/`Try` 未捕获异常后会话存活 |
| `PInvoke` / `ExternDestructor` | 平台调用与终结器 | 顶层 `Declare` / 嵌套类 `Finalize` |
| `Enums` / `Dynamic_Expando` / `DefaultLiteral` / `InferredTupleNames` / `Tuples` | 字面量与推断 | `Option Infer` 各档、元组 |
| `Args_Interactive*` / `Args_Script*` / `Help` / `Version` / `ResponseFile` / `RelativePath` / `SourceSearchPaths*` / `ReferenceSearchPaths*` / `InitialScript*` / `LangVersions` / `Script_NonExistingFile` / `Script_BadUsings` / `Script_NoHostNamespaces` | 宿主命令行面 | `CommandLineRunnerTests.vb` 既有覆盖，本任务只补缺口 |

> 族谱由 `grep -hoE "public (async )?(void|Task) [A-Za-z0-9_]+" CSharpTest/*.cs` 一次性列出，可重跑核对。
