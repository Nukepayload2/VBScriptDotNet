# 详细设计（script-mode-coverage-parity）

本文档给**逐单元的改动蓝图与 pass 条件**。缺口矩阵（§B）与语法 ledger（在 `test-plan.md` §C）是本任务两张网的实体，**矩阵的「缺口」列是 U3–U9 的代办来源，ledger 的 `缺口` 行是 U10 的代办来源**。

---

## U1 · 判别性用例补测：顶层不推断的后果（**已从「缺陷收口」降级**）

> **本单元不是缺陷修复。** 初报的 `InvalidCastException` 已被判定为**既有已测行为 + 晚期绑定语义**的组合，不是编译器缺陷。推翻过程全文见 `design-overview.md` §3；此处只给最后一步的关键读数和它转化出的工作。

### 1.1 关键读数（**实锤，已运行**）

| 探针 | 形状 | 读数 |
|---|---|---|
| `r5/pin.py` `module-shared-sub-main` | 普通模式，模块共享方法，**不开推断** | exit `3762504530`，**与脚本顶层同一条 `InvalidCastException`** |
| `r5/decisive.py` `ord-optioninfer-on` | 普通模式，**`Option Infer On`** | **exit 0，`G=2:2,1:1,3:1`** |
| `r5/decisive.py` `script-top-optioninfer-on` | 脚本顶层，`Option Infer On` | exit `2147500034`，**晚期绑定**消息 |
| `r5/field_infer.py` `script-top-infer-linq` | 脚本顶层 `Dim values = {1,2,3,4}` 后 `From v In values` | **`BC36593`：表达式「`Object`」不可查询** |
| `r5/field_infer.py` `script-top-field` | 顶层 `Dim Field = 1` 取 `GetType().Name` | exit 0，`F=Int32`（**该探针无鉴别力**，见下 1.2） |

### 1.2 判定的构成（每条都是实锤）

1. **容器无关**：普通模式不开推断时抛同一条异常 ⇒ 初判「脚本顶层容器特有」被推翻。
2. **真正的变量是推断**：普通模式开推断后成功；脚本顶层开推断后仍以**晚期绑定**消息失败 ⇒ 分歧只在「顶层是否推断」。
3. **顶层确实不推断**：`field_infer.py` 的 `script-top-infer-linq` 里**编译器自己**把 `values` 报成 `Object`（`BC36593`）——这是编译器对自身绑定结果的陈述，比任何反射/`GetType()` 判别都硬。
4. **`GetType()` 判别不具鉴别力**：`script-top-field` 得 `F=Int32` 看着像「推断了」，但晚期绑定调 `Object.GetType()` 返回的是**运行时类型**，两种情形同值。**该形状此后不得用于判别推断与否。**

### 1.3 本单元的工作（**不碰产品源码**）

| # | 动作 | 落点 |
|---|---|---|
| 1 | 补**成对**用例：① 顶层 `Dim q = <LINQ 查询>` 不加 `As` ⇒ 断言表现为 `Object`（用**重载决议**判别，与 `ScriptModeStatementConformanceTests.vb:606` 同法）；② 加 `As` 或放进顶层 `Sub` 的局部 ⇒ 正常求值 | L4（`Scripting\VisualBasicTest`） |
| 2 | 补用例：顶层 `Dim q = <查询>` 后对 `q` 调晚期绑定成员 | 断言**失败形态**（异常类型 / 诊断），把它变成有网的行为 |
| 3 | 登记 **D5 分歧**（顶层不推断 vs C# 脚本 `var` 推断） | `issues\` 或 `OPEN QUESTIONS`，**交用户裁决** |
| 4 | 在 ledger（`test-plan.md` §C）里把「顶层推断字段」相关行的状态按 §1.2 的实锤定稿 | `test-plan.md` |

### 1.4 pass 条件

| 项 | 断言 |
|---|---|
| 成对用例 | ①与②都在；①断言**静态类型是 `Object`**（重载决议判别），②断言**求值成功且值正确** |
| 负向用例 | 晚期绑定成员调用断言**具体的失败形态**（异常类型或诊断 ID），不是「不抛异常」 |
| D5 分歧 | 已登记且明确写「交用户裁决」；**产品源码零改动** |
| ledger | 相关行状态与 §1.2 的实锤一致 |
| 回归 | 本单元**不改变任何产品行为** ⇒ 既有用例读数一字不变（可直接用既有全量跑验证） |

**判别性**：本单元的用例断言的是**既有行为**，其价值是「把它变成有网的行为」而非「驱动行为变化」。故**不适用**「撤销改动后用例必须失败」的判别法——`VBNetScriptMaintainer` 的「测试鉴别力须单独论证」在此的论证是：**成对用例的①与②必须给出不同结果**（①`Object`、②正确值）；若两条结果相同，说明该用例对「是否推断」不敏感，必须重写。

---

## U2 · VB 特有语法 ledger 建表

**产物**：`test-plan.md` §C 的 ledger 表。**不改任何代码、不写任何用例**。

### 2.1 建表步骤

1. 重跑 `README.md` §四 的计数命令，把命令与读数写进 ledger 头部。
2. 对 `statements.md` / `type-members.md` / `expressions.md` / `lexical-grammar.md` / `preprocessing-directives.md` 的每个 `##`/`###`/`####` 小节出一行。
3. 每行填：小节名 / 来源 `文件:行号` / **顶层容器**状态 / **嵌套容器**状态 / 依据（用例 `文件:行号` 或检索证据）。
4. 状态取值只能取 `已覆盖` / `新补` / `不适用` / `缺口` 四值（`README.md` §四）。
5. **第一遍只建表不补用例**——先让缺口的总量可见，再决定补测的排期。

### 2.2 pass 条件

- ledger 覆盖 `README.md` §四 表列出的**全部五个来源**。
- 每行**四值之一**，无空格。
- 每个 `不适用` 带**检索命令 + 命中情况**（可机械复核）。
- 头部含**计数命令与实际读数**。
- **建表完成时允许存在 `缺口` 行**——这是表的目的；`缺口` 归零是 U11 的 pass 条件。

---

## U3–U9 · 网一补测（五条轴 + 剩余 API 面）

**共同形状**：每族一个测试文件或既有文件的一个 Region，**不改产品源码**。用例一律走 §test-plan §3 的无副作用通道。

### U3 · 宿主对象（`globalsType`）绑定语义

对标 C# `InteractiveSessionTests.cs` 的 `HostObjectBinding_*` **8 格**（`IS:1528,1545,1553,1570,1580,1590,1598,1617`）+ `HostObjectInRootNamespace` 单列 + `HostObjectAssemblyReference1-3` + `StaticMethodCannotAccessGlobalInstance` / `StaticLocalFunctionCannotAccessGlobalInstance` / `LocalFunctionCanAccessGlobalInstance`。

**对齐说明**（C# → VB 概念映射，**必须**在用例注释里写明，因为这不是同形翻译）：

| C# 格 | VB 对偶 | 备注 |
|---|---|---|
| `HostObjectBinding_PublicClassMembers` | 宿主对象公共类成员 | 直接对应 |
| `HostObjectBinding_PublicGenericClassMembers` | 宿主对象公共泛型类成员 | 直接对应 |
| `HostObjectBinding_Interface` | `globalsType` 为**接口**时只暴露接口成员 | 直接对应 |
| `HostObjectBinding_PrivateClass` / `_PrivateMembers` | 私有类 / 私有成员可见性 | 直接对应 |
| `HostObjectBinding_PrivateClassImplementingPublicInterface` | 私有类实现公共接口 | 直接对应 |
| `HostObjectBinding_StaticMembers` | 宿主对象的 **`Shared`** 成员 | C# `static` → VB `Shared` |
| `HostObjectBinding_Overloads` | 宿主对象的**重载**成员 | 直接对应 |
| `HostObjectInRootNamespace` | **根命名空间**下的宿主对象 | VB 侧多一层：`VisualBasicCompilationOptions.RootNamespace` |
| **宿主成员与提交成员不构成方法组** | 同义（VB 也无跨源方法组） | 直接对应 |
| `StaticMethodCannotAccessGlobalInstance` | **`Shared Sub`** 读宿主实例成员 → 报错 | VB 无局部函数，用 `Shared Sub` 对偶 |
| `LocalFunctionCanAccessGlobalInstance` | **非 `Shared` 的顶层 `Sub`/lambda** 读宿主实例成员 → 可用 | 见上 |

**pass 条件**：**8** 格 `HostObjectBinding_*`（`IS:1528,1545,1553,1570,1580,1590,1598,1617`）+ `HostObjectInRootNamespace` 单列 1 格 + `HostObjectAssemblyReference1-3` 3 格 + `Shared`/局部函数对偶 3 格，各有用例；每格断言「诊断 / 运行结果」而非「不抛异常」；至少 1 格是**负向**（预期报错）用例。

### U4 · `<host>` / `<implicit>` 引用别名

**为什么是高价值**：本 fork **有意**尊重元数据引用别名（原版上游 VB 忽略别名），`Scripting` 的别名语义依赖这一行为——这是 fork 的**自有偏差**，却无任何 VB 测试锚定（**实锤**：以 `host` / `implicit` 两个纯模式检索 `Scripting\VisualBasicTest\**.vb`，命中全部是 `Microsoft.CodeAnalysis.Scripting.Hosting` 命名空间导入与英文散文（"the host"、"host RID"）、以及 VB 语言的「implicit Me / implicit local」措辞，**无一处是引用别名**）。

**期望值来源（U4 已不需要「先读生产侧」——规范已存在）**：本项**有规范性来源**，计划初稿未发现，现补：

| 来源 | 内容 |
|---|---|
| **`spec\spec-reference-directive.md:160-180`**（**规范，权威**） | 别名规则的定义：无别名或含 `global` 的引用把全局命名空间并入合并命名空间，**带其它别名的引用不并入**；`<host>` 施加于宿主对象程序集并**递归**，把其命名空间与全局类型挡在**非限定查找**之外；`<implicit>` 施加于解析器为**缺失依赖**补位的程序集；**「No escape hatch」**：VB 无 `extern alias`，带非全局别名的引用**从 VB 源码完全不可达**（隐藏是绝对的）；**普通编译同样适用**（`/nostdlib` 下命令行编译器对真实核心库施加非全局别名） |
| **`proposals\proposal-reference-directive.md:109-118`** | 生产锚点：`<host>` 于 `Scripting\Core\Script.cs:237-239` 构造、`:259-269` 挂载；`<implicit>` 于 `RuntimeMetadataReferenceResolver.cs:27-29` 构造、`:140-143` 应用；生效判据 `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.State.cs:721-725`（`aliases.Length = 0 OrElse aliases.IndexOf(GlobalAlias) >= 0`）与 `Compilers\VisualBasic\Portable\Symbols\MergedNamespaceSymbol.vb:107-120` |
| ~~C# 的 `IS:1640` 期望值~~ | **不得照抄**：C# 的别名集合构造路径与 VB 侧不同，且 VB 侧多一层「分支继承」与 `Imports` 累积的交互 |

**用例（按规范的四个可判断言展开）**：

| # | 用例 | 断言 |
|---|---|---|
| 1 | `<host>` 隐藏非限定查找 | 宿主对象类型所在**命名空间**的名字在脚本里**不可用非限定名访问**（按规范「keeps … out of unqualified lookup」） |
| 2 | 宿主对象成员仍可用 | 经 globals 实例/成员访问**照常可用**（隐藏只针对命名空间与全局类型的裸名） |
| 3 | **隐藏是绝对的（负向，规范里最硬的一条）** | 别名程序集里的类型**从脚本源码完全不可达**（`BC30451` / `BC30002` 一类），且**无任何语法能绕过**（VB 无 `extern alias`） |
| 4 | `<implicit>` 不并入全局命名空间 | 补位程序集满足程序集身份，但其类型**不**并入全局命名空间（不使原本无歧义的名字变歧义） |
| 5 | 递归传递 | 被宿主程序集引用的程序集**同样**带上 `<host>` |
| 6 | `CommandLineScriptGlobals` 作 `globalsType` | 别名集合与成员可见性 |
| 7 | **普通编译同规**（第二容器） | `/nostdlib` 下的普通编译同样施加非全局别名（规范明写「not specific to scripts」） |

**pass 条件**：

| # | 项 | 判据 |
|---|---|---|
| 1 | 期望值来源 | 用例注释引用 **`spec\spec-reference-directive.md` 的小节**（不引行号，按 `decisions.md` 的引用纪律精神）＋生产锚点 `文件:行号` |
| 2 | 上表 1–6 各有用例 | 逐项核对；第 7 项（普通编译）若实现成本过高，可**停手上报**并说明，不静默跳过 |
| 3 | 负向必须存在 | 上表第 3 项（绝对不可达）是规范的**最硬断言**，**必须有** |
| 4 | 不照抄 C# | 用例注释不得出现「与 C# `HostObjectAssemblyReference1` 期望一致」这类表述 |

**与既有 `ImportsAccumulationFailureTests` 的分工**：该文件测的是 **`Imports` 累积**（宿主侧 replay 链），U4 测的是**引用别名**（编译器侧合并过滤）。两者都涉及「为什么某个名字不可见」，但机制不同——用例注释须写明本用例走的是哪条（`MergedNamespaceSymbol.vb:107-120` 的别名过滤）。

### U5 · 异常与取消后的提交链状态

对标 `PreservingDeclarationsOnException1-4` + `PreservingDeclarationsOnCancellation1-3`（共 7 格）。

**现状**：VB 侧只有「会话活着」这类**弱断言**（`ScriptModeSubmissionConformanceTests.vb:256` / `:267`），**没有**断言「异常提交**之前**与**之中**的声明在后续提交仍可用」。

**用例**：`RunAsync` 抛异常后 `ContinueWith` 继续；异常提交自身残留的声明可用；`catchException` 过滤；`CancellationToken` 触发 `OperationCanceledException` 后声明保留。

**无副作用注意**：取消用例用**已取消的 token** 或**同步在 `RunAsync` 前取消**，不得依赖计时器 / 线程睡眠制造竞态（否则测试不稳定，违反 Restriction 的「可判」要求）。

### U6 · 命令行参数 `Args` 与搜索路径

对标 `Args_Interactive1/2` + `Args_InteractiveWithScript1` + `Args_Script1-5`（`CLR:224,240,254,287,304,321,353,370`，**8 格**）+ `SourceSearchPaths1` / `_Change1` / `ReferenceSearchPaths1` / `_Change1`（`CLR:517,548,585,621`，**4 格**）。**U6 格数合计 = 12**。

**现状**：`CommandLineRunnerTests.vb:554` 只测「`--` 后参数不报错」；脚本内读 `Args` **零覆盖**（**实锤**）。

**通道已存在**：`Core\Hosting\CommandLine\CommandLineScriptGlobals.cs` 有 `Args`；`VisualBasicScriptCompiler.vb` 传 `script.GlobalsType`——**实施期须先读这两处把行号写进用例注释**（记忆里的行号已过期，以实际读取为准）。

**搜索路径注意**：`/loadpath` / `/lib` 用例若需要真实目录，复用 `AppContext.BaseDirectory`（**已存在**，不新建）；**不得**写临时文件（违反无副作用纪律）。

### U7 · PDB / 调试信息与栈帧行号

对标 `Pdb_*` 12 格（`WithEmitDebugInformation` × `WithFilePath` × `WithFileEncoding` × string/stream 的 2×2×2 矩阵 + 无编码时的 `ERR_EncodinglessSyntaxTree`）。

**为什么值得**：`#Load` 树的行号映射是本 fork **出过 bug 的区域**（`issues\issue-vbx-load-span-shift.md`）；既有用例 `ScriptTests.vb:526` 的注释（**实锤**，`:526-528` 逐字含 `Issue #01: main.vbx line 3 … The bug inlined loaded.vbx into main.vbx and reported line 7; it must report line 3`）就是那条回归的证据。PDB 面是把这类回归锁到**调试信息层**的一种手段（**非唯一手段**；初稿的「唯一」是未穷举的推定，已降级）。

**无副作用注意**：**不落 PDB 文件**。断言落点改为：`Script.GetCompilation().Emit(MemoryStream, ...)` 的内存产物 + `ScriptOptions.EmitDebugInformation` / `FileEncoding` 落到 `VisualBasicCompilationOptions` 的形状。若某格**必须**落盘才能验（如真栈帧的 `GetFileName`），**停手问用户**，不静默跳过（`VBNetScriptMaintainer` 纪律）。

### U8 · ObjectFormatter 代理族与异常栈渲染

对标 `DebuggerDisplay_*`（`OF` 下实测 3 个命名族）+ `DebuggerProxy_*`（实测 29 个命名族）= **32 个命名族**，另加 `Array_Recursive` / `LargeGraph` / `LongMembers` 等未命名代理用例；再加 `StackTrace_*`（7 格）+ `FormatConstructorSignature` + tuple 格式化。

> **格数口径**：32 是**命名族的实测数**（复核者实锤），不是 36；「约 36」是初稿的估算值，已作废。U8 开工时以 `grep -oE "public void [A-Za-z0-9_]+" ObjectFormatterTests.cs` 重数并写进用例文件头。

**这是投入产出比最高的一块（推测）**：夹具**已经**移植完整（`Helpers\ObjectFormatterFixtures.vb` 含 `RecursiveProxy` / `ComplexProxy` / `RecursiveRootHidden` / 30+ 个 `<DebuggerDisplay>` 形状），测试却**一个都没写**（**实锤**）。写测试的成本显著低于造夹具。

**先决**：逐条读 C# 的 `ObjectFormatterTests.cs` 期望值，把夹具**逐个**对上 C# 的哪个测试；对不上的（VB 侧夹具多/少）**单独列出**，不硬凑。

**pass 条件**：

| # | 项 | 判据 |
|---|---|---|
| 1 | 夹具↔测试对账表 | 用例文件头写一张**夹具名 → C# 测试方法**的对照表；`ObjectFormatterFixtures.vb` 的每个夹具都在表里（**或**明确标注「VB 侧独有 / C# 侧无对应」并给理由） |
| 2 | 命名族格数 | U8 开工时重数 `grep -oE "public void [A-Za-z0-9_]+" CSharpTest/ObjectFormatterTests.cs`，把实测数写进文件头（初稿的「约 36」作废，实测命名族 32） |
| 3 | `StackTrace_*` | 7 格各 1 条，断言 `FormatException` 的栈帧签名（泛型方法/泛型类型/泛型类型内泛型方法/`dynamic`/`ref`/`out`/泛型 `ByRef`） |
| 4 | 逐字断言 | 格式化输出**逐字**断言，不用 `Contains` |
| 5 | 不硬凑 | 无 C# 对应的夹具，写「VB 侧独有」并给理由，**不**造期望值 |

### U9 · 脚本 API 面剩余缺口

**每条给出格数上界**（初稿的「矩阵」「族」「集合枚举」等开放术语无法验收，已改为有限格清单）。格数来源 = C# 侧对应族的实测方法数。

| # | 项 | 格数 | C# 对标 | 判据 |
|---|---|---|---|---|
| 1 | 分支提交链隔离 | **1** | `TestBranchingSubscripts`（`ST:452`） | 同一 `ScriptState` 分叉两条链，断言互不可见 |
| 2 | 重复 `EvaluateAsync` 不重跑 | **2** | `Submissions_ExecutionOrder1/2`（`IS:602,621`） | 断言副作用只发生一次 + 出错提交不污染链 |
| 3 | `ScriptState.Variables` 集合 | **3** | `ScriptVariables_Chain`（`ST:383`） | 名称/值/类型三元组序列；`GetVariable`；`SetValue` 回写 |
| 4 | `IsReadOnly` | **2** | `ScriptVariable_SetValue_Errors`（`ST:432`） | 顶层 `ReadOnly` 字段、`Const` 各 1 条，断言 `InvalidOperationException` |
| 5 | `GetImportScopes(0)` | **1** | `InteractiveSession_ImportScopes`（`IS:1174`） | 断言 alias/extern/xmlns/imports 结构 |
| 6 | `#Load` 返回语义 | **5** | `ReturnInLoadedFile*` 8 格（`ST:643…826`）中 VB 适用者 | 多文件 `#Load`；载入文件的 `Return` 与外层尾表达式的**优先关系**；`goto` 跨文件；裸 `Return`；void | 
| 7 | `#r` 相对路径与扩展名优先级 | **4** | `IS:1233,1251,1271,1301` | 相对父目录；相对根目录；`.exe`/`.dll`/`.winmd` 优先级（2 条） |
| 8 | `MissingAssemblySymbol` 降级 + 多次解析尝试 | **3** | `ISR:26,56,110` | 降级为 `MissingAssemblySymbol` 不阻断编译；多次解析尝试的计数 |
| 9 | `Script.Create` 的 `null` 参数 + `StreamWithOffset` | **3** | `ST:40,53,940` | `code:=Nothing` / `stream:=Nothing` 各抛 `ArgumentNullException`；带 offset 的流可读 |
| 10 | `Submission_HostConversions` / `_HostVarianceConversions` | **2** | `IS:1429,1482` | 宿主类型转换错误的诊断集合；`IEnumerable(Of Exception)` 协变 |
| 11 | 顶层 `Private` / `Protected` / `Friend` 字段可见性 | **3** | `IS:299,326,362` | 每个修饰符 1 条，断言跨提交可见性 |
| 12 | 递归基类型 | **1** | `RecursiveBaseType`（`IS:732`） | `B(Of T) : A(Of B(Of B(Of T)))` 可构造 |
| 13 | `AddReferences` / `AddNamespaces` 的 `null` 参数 | **4** | `K-SO:22,36,78,104,119,142` | `AddReferences`/`WithReferences`/`AddImports`/`WithImports` 各 1 条 |
| 14 | `AllowUnsafe` / `CheckOverflow` / `WarningLevel` 落到编译选项 | **3** | `K-SO:244,254,306` | **读编译对象的实际选项值**，不是读 `ScriptOptions` 属性 |

**上表 14 项格数合计 = 37**（1+2+3+2+1+5+4+3+3+2+3+1+4+3）；**加下方「另补」的 6 格，U9 总格数 = 43**。**验收 = 逐格有用例，且 14 项 + 另补 4 族全部有格覆盖**；若某格确为「VB 不适用」，在用例文件注释里写明**检索方式与理由**后置为不适用，并同步扣减该格的计数。

**另补**（**已定格数上界**，来源为矩阵对应行的 C# 方法数）：族 21 `Dynamic_Expando` = **1**；族 31 = **3**（闭包捕获跨提交、扩展方法、`PrivateImplementationDetails` 跨提交唯一性）；族 41 = **1**（隐式接收者 + `ByRef` 实参）；族 53 = **1**（`SharedLibCopy_Different`）。**另补合计 = 6**。

**U9 总格数 = 37 + 6 = 43。**

**pass 条件（U3–U9 共同）**：

1. 每格有用例，断言是**可判的**（具体值 / 具体诊断 ID），不是「不抛异常」。
2. 负向格（预期报错）与正向格**成对**出现；只有正向格的族不算覆盖。
3. 每族至少 **一条判别性论证**写进测试文件注释：把该用例断言的行为**撤销**后，这条用例必须失败。
4. 零副作用（`test-plan.md` §3）。
5. 若某格暴露产品缺陷 ⇒ **停手**，按 §U1 的流程单独收口（建 issue → 判定 → 修复 + 用例），不混在补测单元里改产品码。

---

## U10 · ledger 缺口填充

按 U2 的 ledger，逐行把 `缺口` 补成 `新补`。**优先级由 ledger 驱动，不由本文件预先指定**——U2 建表前不知道缺口总量。

**pass 条件**：ledger 中**已无 `缺口` 行**（全部已补），且每行 `新补` 都对应一个**实际测试方法**（`文件:行号`，指向 `<Fact>`/`<Theory>` 方法声明行），不允许「标了 `新补` 但没写用例」。

**与 U11 的时序**（初稿措辞自反，已修）：U10 结束时 ledger 处于「`缺口` = 0、`新补` = N」的**中间态**；U11 复核通过后，把这 N 行由 `新补` 改写为 `已覆盖`（并填入 `文件:行号`），**关闭时 ledger 只剩 `已覆盖` 与 `不适用` 两值**。U10 的 pass 条件只看「`缺口` 为 0 + 每个 `新补` 有真实方法」，**不要求**此时已无 `新补`。

---

## U11 · ledger 复核

**pass 条件（可机械核对）**：

1. **`缺口` 行数为 0**：`grep -c "| 缺口 |"` 于 ledger 实际使用的分隔符形式（U2 建表后把**精确模式**写入 ledger 头部，避免判据随格式漂移）。
2. 每行 `不适用` 都有**检索命令 + 命中情况**。
3. 每行 `已覆盖` / `新补` 都有 `文件:行号`，且该行号**指向 `<Fact>`/`<Theory>` 标注的方法声明行**（逐条核，**全量**，不是抽样）。
4. `新补` 已全部改为 `已覆盖`（关闭时 ledger 只剩 `已覆盖` 与 `不适用` 两值）。
5. **行集完备（判据①，全量差集）**：以 `Parser\ParseStatement.vb` / `ParseExpression.vb` 的 `SyntaxKind` 分派 arm 作**全量**反查，**差集必须为空**——差集非空 ⇒ 新增 ledger 行（**不是抽样 20 行**；初稿的抽样版本不足以支撑「完整覆盖」）。
6. **分母固定（判据③）**：ledger 头部报出**总行数与来源分解**（spec / parser arm / fork 指令各多少行）；U11 复核时与 U2 建表时的分母比对，只增不减。
7. **独立复核**：由验证者 agent 复核；其检索方式须与实施者**不同**（实施者按 spec 小节建表 ⇒ 验证者按 parser arm 反查）。

---

## U12 · 收口

| 项 | 命令 / 落点 | pass 条件 |
|---|---|---|
| 七门 gate | `powershell -File scripts\verify-vb-compiler-tests.ps1` | throw-on-mismatch 全过 |
| 门期望值 | `scripts\verify-vb-compiler-tests.ps1:8-14` | 新增用例推高的计数已同步（**只改数字，不改判据**） |
| Scripting 程序集 | `Scripting\VisualBasicTest` 直接跑程序集 `-automated` | 全绿；**须核 `TestCasesToRun > 0`**（`dotnet test` 对该 MTP 项目静默跑 0 个） |
| 公共 API | `PublicAPI.*.txt` | 零增量 |
| 共享发射层 | `Compilers\Core\Portable\CodeGen\` | 零 diff |
| 文档 | `issues\README.md` 状态列、`spec-scripting-dialect.md` Testing 节、`upstream-merge.md` | 同步完成；**不得预填/推测 commit 号** |
| 资源 | `VBResources.resx` ↔ 13 份 `xlf` | 若有新增条目则按原版规范同步；中性 resx 须先审为干净英文产品内容 |

---

## 账本与规范义务

| # | 义务 | 落点 |
|---|---|---|
| 1 | U1 及实施期新发现的每个缺陷建 issue，编号按 `issues\README.md` 现行最大（**20**）顺延 | `issues\` |
| 2 | ledger 是本任务的**交付物**，不是草稿——收口后仍留在 `test-plan.md` §C 供后续轮次复用 | `test-plan.md` |
| 3 | 三张网的产物位置：矩阵（`design-detailed.md` §B）、ledger（`test-plan.md` §C）、判别性论证（各测试文件注释） | 本目录 + `Scripting\VisualBasicTest\` |
| 4 | 探针脚本留在 `tmp\probes\u14\`（**不入库**，`tmp\` 已 git-ignored），但**结论**落进流水账 `tmp\vortex-logs\script-mode-coverage-parity\` 与本目录文档 | `tmp\` |
| 5 | 七门 gate 计数同步 | `scripts\verify-vb-compiler-tests.ps1:8-14` |
| 6 | 变更面登记 | `upstream-merge.md` |

---

## §B · C# 缺口矩阵

### B.0 计数口径核对

| 文件（`{{Roslyn}}\src\Scripting\` 下） | `[Fact]`/`[Theory]` | `[Conditional*]` | 合计 |
|---|---|---|---|
| `CSharpTest\ScriptTests.cs` | 72 | 8 | 80（其中 1 个在 `#if TODO` 内 → 71 有效） |
| `CSharpTest\InteractiveSessionTests.cs` | 91 | 5 | 96 |
| `CSharpTest\CommandLineRunnerTests.cs` | 12 | 23 | 35 |
| `CSharpTest\InteractiveSessionReferencesTests.cs` | 3 | 0 | 3 |
| `CSharpTest\ObjectFormatterTests.cs` | 49 | 2 | 51 |
| `CSharpTest\PrintOptionsTests.cs` | 9 | 0 | 9 |
| `CSharpTest\ScriptOptionsTests.cs` | 3 | 0 | 3 |
| `CoreTest\ScriptOptionsTests.cs` | 24 | 0 | 24 |
| `CoreTest\RuntimeMetadataReferenceResolverTests.cs` | 1 | 0 | 1 |
| `CoreTest\NuGetPackageResolverTests.cs` | 1 | 0 | 1 |
| **合计** | **265** | **38** | **303** |

族内方法数之和 = 303，与逐文件计数**差 0**（**实锤**）。核对用正则漏掉 `[Fact, WorkItem(...)]` 形式的属性行（`CommandLineRunnerTests.cs` 有 2 处），已单独计入。

**不在统计面**：`CSharpTest.Desktop\*`、`CoreTest.Desktop\*`（含 `CsiTests`、`GlobalAssemblyCacheTests`、`MetadataShadowCopyProviderTests`、Desktop 版 `ObjectFormatterTests` / `InteractiveSessionReferencesTests`）。

### B.1 矩阵

> 路径根 `{{Roslyn}}\src\Scripting\`；简写：`ST:`=`CSharpTest/ScriptTests.cs`，`IS:`=`CSharpTest/InteractiveSessionTests.cs`，`CLR:`=`CSharpTest/CommandLineRunnerTests.cs`，`OF:`=`CSharpTest/ObjectFormatterTests.cs`，`PO:`=`CSharpTest/PrintOptionsTests.cs`，`CSO:`=`CSharpTest/ScriptOptionsTests.cs`，`K-SO:`=`CoreTest/ScriptOptionsTests.cs`，`ISR:`=`CSharpTest/InteractiveSessionReferencesTests.cs`，`RMR:`=`CoreTest/RuntimeMetadataReferenceResolverTests.cs`，`NPR:`=`CoreTest/NuGetPackageResolverTests.cs`。
> VB 侧路径根 `{{VBScriptDotNet}}\Scripting\VisualBasicTest\`。

| # | C# 族名（方法） | C# 测的行为 | VB 侧对应覆盖 | 缺口性质 | 适用性 | 档 |
|---|---|---|---|---|---|---|
| 1 | `TestCreateScript` `_CodeIsNull` `TestCreateFromStreamScript` `_StreamIsNull` `StreamWithOffset` | 源码保真；`null` code/stream 抛 `ArgumentNullException`；带 offset 的流可读（`ST:33,40,46,53,940`） | `ScriptTests.vb:62,68` | 部分缺（null 参数族、`StreamWithOffset`） | 适用 | 中 |
| 2 | `TestGetCompilation` `TestGetCompilationSourceText` | 语法树文本 == `Script.Code`；`SourceText` **同一实例**（`ST:59,67`） | `ScriptTests.vb:185` | 弱缺口（同实例断言） | 适用 | 低 |
| 3 | `TestEmit_PortablePdb` `TestEmit_WindowsPdb` | 产出合法 PDB 目录（`ST:75,78`） | **缺** | 完全缺 | 适用 | 中（并入 U7） |
| 4 | `TestCreateScriptDelegate` `…WithGlobals` | delegate 无/有 globals 的调用与 `ArgumentException`（`ST:103,114`） | `ScriptTests.vb:116,128` | 已覆盖 | 适用 | — |
| 5 | `TestRunScript` `TestCreateAndRunScript` `TestCreateFromStreamAndRunScript` `TestEvalScript` `TestRunScriptWithSpecifiedReturnType` `TestRunVoidScript` `NoReturn` | Run/Eval/返回值与 `state.Script` 同一性；void 脚本 `ReturnValue is null`（`ST:126,133,142,151,158,165,503`） | `ScriptTests.vb:83,89,95,103,192` | 部分缺（stream 后 Run；**显式 void 断言已存在**——`ScriptTests.vb:192,194` 的 `TestRunVoidScript` 断言 `Assert.Null(state.ReturnValue)`） | 适用 | 中 |
| 6 | `TestRunExpressionStatement` `TestRunDynamicVoid*` `TestRunEmbedded*`（8 个） | `;` 结尾与内嵌语句的诊断位置（`ST:174,183,198,213,235,243,250,260`） | `CommandLineRunnerTests.vb:863,882,894,907,1059,1073,1088`（VB 对偶面） | — | **不适用**（C# 专有 `;`；VB 对偶面已覆盖） | 低 |
| 7 | `TestRunScriptWithGlobals` `TestRunCreatedScriptWith*Globals` `ContinueAsync_Error1/2` | globals 对象/`globalsType` 匹配与错误路径（`ST:284,291,300,309,318,327,335`） | `ScriptTests.vb:399,405,414,424,436` | 部分缺（`RunFromAsync` 的 null / 错 state） | 适用 | 中（并入 U9） |
| 8 | `TestRunScriptWithScriptState` `TestRepl` `TestBranchingSubscripts` | 链共享状态；**分支链互不可见**（`ST:344,352,452`） | `ScriptModeSubmissionConformanceTests.vb:30…215` | 部分缺（**分支链隔离**） | 适用（脚本模式核心面） | **高**（U9） |
| 9 | `TestCreateMethodDelegate`（`#if TODO`） | `state.CreateDelegate(Of T)`（`ST:372`） | 无 | — | 不适用（上游已禁用） | 低 |
| 10 | `ScriptVariables_Chain` `ScriptVariable_SetValue` `_Errors` | `Variables` 名称/值/类型三元组；`IsReadOnly`；`InvalidOperationException`（`ST:383,413,432`） | `ScriptTests.vb:136,151` | 部分缺（集合枚举、`IsReadOnly`） | 适用（VB 有 `ReadOnly`/`Const` 顶层字段） | **高**（U9） |
| 11 | `StaticDelegate0/1/2` | 顶层 `static` 成员取方法组；泛型类/泛型方法的 `static` 委托（`ST:469,478,486`） | `ScriptModeSubmissionConformanceTests.vb:164`；`ScriptTopLevelCrashTests.vb:553` | 部分缺（泛型 `Shared` 成员取方法组） | 适用（`static` → `Shared`） | 中 |
| 12 | `ReturnIntAsObject` `ReturnAwait` `ReturnInNestedScope*` `ReturnIntWithTrailingDoubleExpression` `ReturnGenericAsInterface` `ReturnNullable`（8 个） | 有类型脚本的返回值与尾表达式取值次序；嵌套块内 `return`；泛型/可空/接口协变（`ST:494,510,518,531,555,579,603,623`） | `ScriptTests.vb:158,167,177,210,216`；`CLR:436,449,461` | 部分缺（嵌套块内 `Return`、泛型/可空返回类型） | 适用 | 中（U9） |
| 13 | `ReturnInLoadedFile*` `MultipleLoadedFiles*` `LoadedFileWithGoto` `VoidReturn` `LoadedFileWithVoidReturn`（8 个） | `#load` 的返回语义：载入文件的 `return` 是否截断外层尾表达式、多 `#load`、`goto` 跨文件（`ST:643,661,684,707,742,777,808,826`） | `ScriptTests.vb:224,590,605,681` | 部分缺（多文件组合、优先关系、跨文件 `goto`） | 适用（`.vbx` 特有面） | **高**（U9） |
| 14 | `Pdb_*`（12 个） | `WithEmitDebugInformation` × `WithFilePath` × `WithFileEncoding` × string/stream 的 2×2×2 + 无编码 `ERR_EncodinglessSyntaxTree`；真栈帧 `GetFileName/Line/Column`（`ST:842–937`） | `ScriptOptionsTests.vb:153`（`MutationProperties_AreImmutableAndReturnSameInstanceWhenUnchanged`，仅选项属性） | **完全缺** | 适用 | **高**（U7） |
| 15 | `CreateScriptWithFeatureThatIsNotSupportedInTheSelectedLanguageVersion` `CreateScriptWithNullableContextWithCSharp8` | 语言版本门控诊断（`ST:949,962`） | `ScriptOptionsTests.vb:19,40,57` | 部分缺（低版本报 BC 诊断的矩阵） | 适用 | 中 |
| 16 | `SwitchPatternWithVar_*`（4 个） | C# `switch` 表达式 + 关系模式（`ST:974,997,1019,1044`） | **缺** | — | **不适用**（VB 无 switch 表达式/关系模式） | 低 |
| 17 | `Function_ReturningPartialType` `_CSharp13` | 单行 `class partial;`、跨提交 partial 方法（`ST:1067,1087`） | **缺** | — | **不适用**（C# 专有） | 低 |
| 18 | `ScriptInstantiation` | 脚本模式下 `new Script()` 报 CS8386（`ST:1099`） | **缺** | — | **不适用**（C# 专有） | 低 |
| 19 | `CompilationChain_NestedTypesClass/Struct` `_InterfaceTypes` `ScriptMemberAccessFromNestedClass` | 嵌套类/结构体访问外层顶层字段；嵌套类型继承接口；嵌套类**不能**直接访问顶层实例字段（`IS:43,64,85,102`） | `ScriptModeConformanceTests.vb:570,594,606,631,655`；`ScriptTopLevelCrashTests.vb:553` | 部分缺（**负向**：嵌套类不可访问顶层实例字段） | 适用 | 中（U3） |
| 20 | `AnonymousTypes_TopLevel_MultipleSubmissions/2` `_Redefinition` `_Empty`（4 个） | 跨提交匿名类型同一性与重定义（`IS:132,152,171,186`） | `InteractiveSessionTests.vb:142,162`；`ScriptModeSubmissionConformanceTests.vb:202` | 部分缺（空匿名类型 `_Empty`） | 适用 | 中 |
| 21 | `Dynamic_Expando` | `ExpandoObject` 跨提交动态成员（`IS:209`） | **缺** | 完全缺 | 适用（VB `Object` 晚期绑定） | 中（U9） |
| 22 | `Enums` | 顶层 `enum` 声明与求值（`IS:232`） | `ScriptModeConformanceTests.vb:325,570` | 已覆盖 | 适用 | — |
| 23 | `PInvoke` | 顶层 `[DllImport]` 反射元数据保真（`IS:254`） | `ScriptModeSubmissionConformanceTests.vb:426,437,448,459` | 已覆盖 | 适用 | — |
| 24 | `PrivateTopLevel` `NestedVisibility` `Fields_Visibility` `ExternDestructor` | 顶层 `private/internal/protected` 可见性与跨提交访问（`IS:299,326,362,387`） | `InteractiveSessionTests.vb:188`；`ScriptModeSubmissionConformanceTests.vb:107` | 部分缺（`Private`/`Protected` 顶层字段矩阵、`ExternDestructor`） | 适用（`ExternDestructor` 不适用） | 中（U9） |
| 25 | `CompilationChain_BasicFields` `_GlobalNamespaceAndUsings` `_CurrentSubmissionUsings` `_UsingDuplicates` `_GlobalImports`（5 个） | 顶层字段链接；全局/当前提交 `using`；重复 using；`AddImports`（`IS:402,409,421,445,461`） | `ScriptModeSubmissionConformanceTests.vb:56`；`ScriptOptionsTests.vb:65,78,88`；`InteractiveSessionTests.vb:25,36,50` | 已覆盖 | 适用 | — |
| 26 | `CompilationChain_Accessibility` `_SubmissionSlotResize` `_UsingNotHidingPreviousSubmission` `_DefinitionHidesGlobal` `_HostObjectMembersHidesGlobal` `_UsingNotHidingHostObjectMembers` `_DefinitionHidesHostObjectMembers`（7 个） | 提交间可见性；>16 提交 slot 扩容；**遮蔽规则**（`IS:473,517,530,550,570,580,591`） | `InteractiveSessionTests.vb:188`；`ScriptModeSubmissionConformanceTests.vb:129`（10 提交） | 部分缺（3 条遮蔽规则；slot 用 17 提交） | 适用 | **高**（U3 + U9） |
| 27 | `Submissions_ExecutionOrder1/2` | 重复 `EvaluateAsync` 不重跑；出错提交不污染链（`IS:602,621`） | `ScriptModeSubmissionConformanceTests.vb:42`；`ImportsAccumulationFailureTests.vb:309` | 部分缺（**重复求值不重跑**） | 适用 | **高**（U9） |
| 28 | `ObjectOverrides1/2/3` | 宿主对象 `Equals/GetHashCode/ToString` 的隐式接收者调用；这些名字**不**自动进脚本作用域（`IS:648,663,681`） | `ScriptModeSubmissionConformanceTests.vb:183` | 部分缺（宿主 override 绑定 + 「顶层无 Equals/GetHashCode/ReferenceEquals」） | 适用 | **高**（U3） |
| 29 | `CompilationChain_GenericTypes` `RecursiveBaseType` `CompilationChain_GenericMethods` | 顶层泛型类/泛型方法跨提交；**递归基类型** `B(Of T) : A(Of B(Of B(Of T)))`（`IS:715,732,741`） | `ScriptModeConformanceTests.vb:631`；`ScriptModeSubmissionConformanceTests.vb:149` | 部分缺（递归基类型） | 适用 | 中（U9） |
| 30 | `CompilationChain_Ldftn` `_GenericType` | `ldftn`/`ldvirtftn` 对静态/实例/虚方法 + 泛型（`IS:760,793`） | `ScriptModeSubmissionConformanceTests.vb:164` | 部分缺（泛型类型上的泛型方法组、虚方法组） | 适用 | 中 |
| 31 | `IfStatement` `ExprStmtParenthesesUsedToOverrideDefaultEval` `TopLevelLambda` `Closure` `Closure2` `UseDelegateMixStaticAndDynamic` `Arrays` `FieldInitializers` `FieldInitializersWithBlocks` `TestInteractiveClosures` `ExtensionMethods` `ImplicitlyTypedFields` `PrivateImplementationDetailsType`（13 个） | 顶层语句/闭包/委托/数组/**字段初始化器分桶次序**（静态 vs 实例 vs 常量）；`PrivateImplementationDetails` 跨提交唯一性；扩展方法（`IS:828…1036`） | `ScriptTopLevelCrashTests.vb:130,139,151,165,179,432`；`ScriptModeConformanceTests.vb:303,314,325,338,594,671` | 部分缺（**闭包捕获跨提交**、扩展方法、`PrivateImplementationDetails` 跨提交唯一性） | 适用 | 中（U9） |
| 32 | `NoAwait` `Await` `AwaitSubExpression` `AwaitVoid` `AwaitInLambda` `AwaitChain1/2`（7 个） | 顶层 `await`；子表达式位置；`await void`；lambda 内 await 被忽略；跨提交 await 链（`IS:1055…1132`） | `ScriptTests.vb:364,375,387`；`ScriptModeSubmissionConformanceTests.vb:283`；`ScriptModeStatementConformanceTests.vb:25` | 部分缺（`AwaitVoid`、lambda 内 await） | 适用 | 中 |
| 33 | `PatternVariableDeclaration` `CSharp9PatternForms` | C# 模式变量与 `or`/`and`/`not`/关系模式（`IS:1151,1159`） | **缺** | — | **不适用**（VB 无模式匹配） | 低 |
| 34 | `InteractiveSession_ImportScopes` | `SemanticModel.GetImportScopes(0)` 的 alias/extern/xmlns/imports 结构（`IS:1174`） | **缺** | 完全缺 | 适用（**高危面**，U9） | **高** |
| 35 | `ReferenceDirective_FileWithDependencies` `_RelativeToBaseParent` `_RelativeToBaseRoot` | `#r` 依赖链；相对路径相对脚本父目录/根目录解析（`IS:1203,1233,1251`） | `InteractiveSessionReferencesTests.vb:43,61,78,87` | 部分缺（显式相对路径） | 适用 | 中（U9） |
| 36 | `ExtensionPriority1/2` `UsingExternalAliasesForHiding` | `#r` 同名库在 `.exe`/`.dll`/`.winmd` 间的优先级；alias 遮蔽（`IS:1271,1301,1332`） | **缺** | 完全缺（优先级） | 适用（优先级）；**不适用**（alias，VB 无 `extern alias`） | 中（U9） |
| 37 | `UsingAlias` `Usings1/2` `AddNamespaces_Errors` | 顶层别名 `using D = ...`；`AddImports` 增量；非法 import 名诊断（`IS:1353,1365,1376,1390`） | `ImportsAccumulationFailureTests.vb:133…217`；`ScriptOptionsTests.vb:65,78,88` | 已覆盖（VB 侧更广） | 适用 | — |
| 38 | `Submission_HostConversions` `_HostVarianceConversions` | 有类型脚本的宿主类型转换错误与 `IEnumerable(Of Exception)` 协变（`IS:1429,1482`） | `ScriptTests.vb:167,177` | 部分缺（转换诊断矩阵、协变） | 适用 | 中（U9） |
| 39 | `HostObjectBinding_*`（**8 个**）`HostObjectInRootNamespace` | 宿主对象成员绑定：`globalsType` 过滤、私有成员、`Static`、**宿主成员不与提交成员构成方法组**、根命名空间（`IS:1528…1628`） | `ScriptTests.vb:399,405,414,424,436`；`ScriptTopLevelCrashTests.vb:870` | **完全缺**（除基础访问） | 适用（脚本模式核心面） | **高**（U3） |
| 40 | `HostObjectAssemblyReference1/2/3` | 宿主对象程序集的 `<host>`/`<implicit>` alias 及递归传递（`IS:1640,1697,1761`） | **缺**（以 `host`/`implicit` 两个纯模式检索 `Scripting\VisualBasicTest\**.vb`，命中全是 `Scripting.Hosting` 导入与英文散文，**无一处引用别名**） | **完全缺** | 适用且**本 fork 自有偏差**；**已有规范** `spec\spec-reference-directive.md:160-180` | **高**（U4） |
| 41 | `MethodCallWithImplicitReceiverAndOutVar` | 隐式接收者 + `out var` 推断（`IS:1836`） | `CommandLineRunnerTests.vb:1177,1200,1219,1444`（`TestByValSpanParameterIsUsable` / `TestTopLevelByValSpanArgumentReportsRestrictedLift` / `TestByRefSpanParameterReportsRestrictedType` / `TestByRefCopyOutDiscardsWriteBackInRepl`） | 部分缺（隐式接收者 + `ByRef` 实参） | 部分适用（VB 无 `out var`） | 中 |
| 42 | `StaticMethodCannotAccessGlobalInstance` `StaticLocalFunctionCannotAccessGlobalInstance` `LocalFunctionCanAccessGlobalInstance` | `static` 方法/局部函数不能读宿主实例成员（`IS:1855,1873,1895`） | `ScriptTopLevelCrashTests.vb:553` | 部分缺（`Shared Sub` + lambda 对偶） | 适用（VB 无局部函数） | **高**（U3） |
| 43 | `PreservingDeclarationsOnException1–4` | `catchException` 后前序提交声明保留；异常提交自身残留声明可用（`IS:1919,1942,1969,1997`） | **缺**（`catchException` 零命中，**实锤**） | **完全缺** | 适用（REPL 高价值面） | **高**（U5） |
| 44 | `PreservingDeclarationsOnCancellation1–3` | `CancellationToken` 触发 `OperationCanceledException` 后声明保留 + `catchException` 过滤（`IS:2025,2059,2093`） | **缺** | **完全缺** | 适用 | **高**（U5） |
| 45 | `LocalFunction_PreviousSubmissionAndGlobal` | 前序提交局部函数被后续提交 lambda 捕获（`IS:2126`） | **缺** | 完全缺 | 部分适用（VB 无局部函数；lambda 捕获对偶） | 中 |
| 46 | `Await`(REPL) `Void` `Tuples` `Help` `Version` `HelpCommand` `LangVersions` | REPL 提示符/logo/help/version；`Print` 的 void 与尾表达式；tuple 显示（`CLR:32,135,153,401,416,741,756`) | `CommandLineRunnerTests.vb:119,258,632,651,685,701,720,735,763,778,796,810,828` | 已覆盖（VB 侧更细） | 适用 | — |
| 47 | `TestDisplayResultsWithCurrentUICulture1/2` | 按 `DefaultThreadCurrentUICulture` 格式化（`CLR:66,101`） | `CommandLineRunnerTests.vb:565,597` | 已覆盖 | 适用 | — |
| 48 | `Exception` `ExceptionInGeneric` | REPL 未捕获异常的 `«Red»` 渲染与 stderr 镜像；泛型栈帧签名（`CLR:166,195`） | `CommandLineRunnerTests.vb:863,882`（仅语法错误） | **部分缺**（异常渲染 + 泛型栈帧） | 适用（`VisualBasicObjectFormatter.FormatException`） | **高**（U8） |
| 49 | `Args_Interactive1/2` `Args_InteractiveWithScript1` `Args_Script1–5`（8 个） | 脚本经宿主 `Args` 拿命令行参数；`--` 分隔、`@rsp`、前缀、相对路径、工作目录（`CLR:224,240,254,287,304,321,353,370`） | `CommandLineRunnerTests.vb:554`（仅「`--` 后不报错」）；脚本内读 `Args` **零覆盖**（**实锤**） | **完全缺** | 适用（通道存在） | **高**（U6） |
| 50 | `Script_NonExistingFile` `Script_BadUsings` `Script_NoHostNamespaces` `RelativePath` `InitialScript1` `InitialScript_Error` | 脚本文件不存在 / 坏 `-u` / 宿主命名空间不外泄 / 相对路径 / `/i` 初始脚本（`CLR:389,436,455,474,696,714`） | `CommandLineRunnerTests.vb:375,496,290,301,318,350,473` | 已覆盖（`/i` 初始脚本等价物缺） | 适用 | 中 |
| 51 | `ResponseFile` | `.rsp` 的 `/r:` 与 `/u:` 被解析（`CLR:656`） | `CommandLineRunnerTests.vb:187,206,301,473`（方法声明行；`484` 落在 `TestResponseFileReferencesAndImportsInScriptFile` 方法体内，不单列） | 已覆盖 | 适用 | — |
| 52 | `SourceSearchPaths1` `_Change1` `ReferenceSearchPaths_Change1` `ReferenceSearchPaths1` | `/loadpath`/`/loadpaths`、`/lib*`；REPL 中 `SourcePaths`/`ReferencePaths` 变更后重试（`CLR:517,585,621,548`） | **缺**（`loadpath`/`libpath`/`SourcePaths`/`ReferencePaths` 零命中，**实锤**） | 完全缺（`/lib*`） | 适用 | **高**（U6） |
| 53 | `SharedLibCopy_Different` | 两次 `#r` 同名不同内容的「AssemblyAlreadyLoaded」错误（`CLR:779`） | **缺** | 完全缺 | 适用 | 中（U9） |
| 54 | `DefaultLiteral` `InferredTupleNames` | 语言特性在 REPL 的端到端（`CLR:878,896`） | `CommandLineRunnerTests.vb:1059,1073,1088` | 部分缺（语言版本特性 × REPL 矩阵） | 适用 | 低 |
| 55 | `PreservingDeclarationsOnException`(REPL) | REPL 中提交抛异常后 `i+j+k` 仍可求值（`CLR:848`） | `ScriptModeSubmissionConformanceTests.vb:256,267`（只测会话存活） | **部分缺**（声明保全，正是 #43 的 REPL 面） | 适用 | **高**（U5） |
| 56 | `TestGetDirectoryName_Windows`（19 `InlineData`） | `PathUtilities.GetDirectoryName` 的 Windows 路径规则（**`CLR:512`**，方法声明行；`CLR:492` 落在其 `[InlineData]` 列表内） | **缺** | 完全缺 | 适用（纯工具函数，低价值） | 低 |
| 57 | `WithLanguageVersion` ×3 | 落到 `CSharpParseOptions`；同值不新建；非 C# 抛（`CSO:17,24,31`） | `ScriptOptionsTests.vb:19,32,57` | 已覆盖 | 适用 | — |
| 58 | `PO:*`（9 个） | `PrintOptions` 参数校验、进制、成员显示格式、转义、最大长度、省略号（`PO:18…126`） | `PrintOptionsTests.vb:13,18,27,36,50,66,90,103,129` | 已覆盖（VB 版未 Skip，比上游 VB 更全） | 适用 | — |
| 59 | `LibraryReference_NetStandard20` `_MissingDependency_MultipleResolveAttempts` `_MissingDependency` | NetStandard2.0 引用解析；多次解析尝试；降级为 `MissingAssemblySymbol`（`ISR:26,56,110`） | `InteractiveSessionReferencesTests.vb:43,61,78,87` | 部分缺（3 项） | 适用 | 中（U9） |
| 60 | `OF:Objects` `TupleType` `ValueTupleType` `ArrayMethodParameters` `ArrayOfInt32_NoMembers` `RecursiveRootHidden` | 对象格式化：嵌套泛型类型名、单行/隐藏成员格式、截断排序、tuple/ValueTuple、数组形参签名、`DebuggerTypeProxy` 递归隐藏根（`OF:31,76,83,90,97,107`） | `ObjectFormatterTests.vb:18,24,37,58,101,107` | 部分缺（tuple/ValueTuple、`ArrayOfInt32_NoMembers`、`RecursiveRootHidden`） | 适用 | **高**（U8） |
| 61 | `DebuggerDisplay_*` `DebuggerProxy_*` `DebuggerProxy_FrameworkTypes_*`（**32 个命名族**，另加未命名代理用例） | `[DebuggerDisplay]` 表达式解析（`{x}`/`{x,nq}`/`{new B()}`/转义/`ReturnVoid`）；`[DebuggerTypeProxy]` 递归；.NET 框架类型代理渲染（`OF:119…862`） | **缺**（夹具已移植，测试零引用，**实锤**） | **完全缺（最大单块）** | 适用（夹具已就位） | **高**（U8） |
| 62 | `FormatConstructorSignature` | 构造器签名格式化（`OF:883`） | **缺**（`ObjectFormatterTests.vb:101` 只测 `FormatMethodSignature`） | 部分缺 | 适用 | 中（U8） |
| 63 | `StackTrace_NonGeneric` `_GenericMethod` `_GenericType` `_GenericMethodInGenericType` `_Dynamic` `_RefOutParameters` `_GenericRefParameter`（7 个） | `FormatException` 的栈帧签名渲染：泛型方法/泛型类型/泛型类型内泛型方法/`dynamic`/`ref`/`out`/泛型 `ByRef`（`OF:931…1099`） | **缺**（`FormatException`/`StackFrame` 零命中，**实锤**） | **完全缺** | 适用（生产侧 `VisualBasicObjectFormatter.FormatException` 存在） | **高**（U8） |
| 64 | `K-SO:AddReferences` `_Errors` `WithReferences` `_Errors` | `AddReferences`/`WithReferences` 各重载的累积与 14 种 `ArgumentNullException`（`K-SO:22,36,61,78`） | `ScriptOptionsTests.vb:109,124,134` | 部分缺（null 参数族） | 适用 | 中（U9） |
| 65 | `K-SO:AddNamespaces` `AddImports_Errors` `WithImports_Errors` | import 名只做 CLR 命名空间校验（接受 `""`/`"blah."`/`"b\0lah"`/`".blah"`）；null 参数族（`K-SO:104,119,142`） | `ScriptOptionsTests.vb:65,78,88`；`ImportsAccumulationFailureTests.vb:133,144,151` | 部分缺（null 参数族） | 适用（VB 侧「怪名」覆盖更广） | 中（U9） |
| 66 | `K-SO:Imports_Are_AppliedTo_CompilationOption` | `ScriptOptions.Imports` 落到 `CompilationOptions`（`K-SO:163`） | `ScriptOptionsTests.vb:78,88` | 已覆盖 | 适用 | — |
| 67 | `K-SO` 其余 16 个 | `EmitDebugInformation`/`FileEncoding`/`AllowUnsafe`/`CheckOverflow`/`OptimizationLevel`/`WarningLevel` 的 setter、同值不新建、**落到 `CompilationOptions`**（`K-SO:171…306`） | `ScriptOptionsTests.vb:153`；`CommandLineRunnerTests.vb:137…277`（OptimizationLevel） | 部分缺（`AllowUnsafe`/`CheckOverflow`/`WarningLevel` 零覆盖，**实锤**） | 适用 | 中（U9） |
| 68 | `RMR:Resolve` | `ResolveReference` 对 `nuget:N/1.0` 与文件路径的解析（`RMR:20`） | `NuGetPackageResolverTests.vb`5 个 + `NuGetRestoreCoordinatorTests.vb`27 个 | 已覆盖（fork 侧远超上游） | 适用 | — |
| 69 | `NPR:ParsePackageNameAndVersion` | `TryParsePackageReference` 正反例（`NPR:15`） | `NuGetPackageResolverTests.vb:15,27,41,53,73` | 已覆盖 | 适用 | — |

### B.2 分档统计

| 档 | 族数（清单号） | 个数 | 对应单元 |
|---|---|---|---|
| **高** | 8、10、13、14、26、27、28、34、39、40、42、43、44、48、49、52、55、60、61、63 | **20** | U3（28/39/42）、U4（40）、U5（43/44/55）、U6（49/52）、U7（14）、U8（48/60/61/63）、U9（8/10/13/26/27/34） |
| **中** | 1、**2**、3、5、7、11、12、15、19、20、21、24、29、30、31、32、35、38、41、45、50、53、59、62、64、65、67 | **27** | U3（19）、U9（**2** 及其余） |
| **低（不适用）** | 6、9、16、17、18、33、54、56 | **8** | 仅记「不适用 + 理由 + 检索证据」（U2 ledger 或本表） |
| 已覆盖（无工作） | 4、22、23、25、37、46、47、51、57、58、66、68、69 | **13** | — |
| **跨档（单列）** | 36（`#r` 扩展名优先级 → 中；`extern alias` 遮蔽 → 低） | **1** | 矩阵里是**一个行、两个子项**，不计入任何一档 |

**合计核对**：20 + 27 + 8 + 13 = **68**，加跨档的族 36 = **69** ✓（与 §B.1 的 69 行一致）。

**「适用族」的分母 = 高 20 + 中 27 = 47**（两档就是「要补的」）；已覆盖的 13 已闭口、低档 8 与跨档 1 判为不适用，均不计入分母。

> **两次算错（记录以明判定纪律）**：
> 1. 初稿把族 36 同时计入「中」与「低」，两个错误**恰好互相抵消**，于是 `20+27+9=56`、`56+13=69` 在算术上「正好」成立——**错误抵消冒充了自洽**。另：初稿高档的「对应单元」列里错列了族 19（它属中档）。
> 2. 第一次修订把族 2 补进了**低（不适用）**档，但 §B.1 矩阵里族 2 的「适用性」列写的是 **`适用`**、缺口性质是「**弱缺口（同实例断言）**」——**一个被标「适用且带缺口」的族被静默排除出分母**，会让「适用族中无 `缺口`」这一判据**不可满足**。本次已把族 2 升入**中档**（弱缺口，必补同实例断言）。
>
> **教训：并集核对必须逐号遍历；且分档必须与矩阵行的「适用性」列逐行对齐——两处口径不一致会让结束条件失效。**
