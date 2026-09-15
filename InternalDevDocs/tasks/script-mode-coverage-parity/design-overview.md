# 总体设计（script-mode-coverage-parity）

## 1. 这一批缺口长什么样

本任务没有单一根因，只有**两张网**，各自有各自的破法：

### 网一：C# 有、VB 没有（缺口矩阵）

C# 脚本测试有 **303 个用例**（265 `[Fact]`/`[Theory]` + 38 `[Conditional*]`，见 `design-detailed.md` §B.0 的逐文件计数）；本 fork 的 VB 侧有约 **437 个测试方法**，脚本模式相关 **367 个**。**用例数上 VB 侧已经更多**——但两个数不可比，因为它们覆盖的**轴**不同：

- VB 侧多出来的量，集中在**第二轮建立的符合性矩阵**（`ScriptMode*ConformanceTests.vb` 三个文件共 91 个用例，逐格铺「声明 × 语句 × 提交」）与 **NuGet 还原链**（约 70 个用例，C# 侧无对应工程）。
- C# 侧多的，是**整条「宿主对象」轴**（`HostObjectBinding_*` **8 格** + `HostObjectInRootNamespace` + `HostObjectAssemblyReference1-3`）、**提交链的状态保全轴**（`PreservingDeclarationsOnException1-4` + `PreservingDeclarationsOnCancellation1-3`）、**调试信息轴**（`Pdb_*` 12 格）、**对象格式化代理轴**（`DebuggerDisplay_*` / `DebuggerProxy_*` **32 个命名族** + `StackTrace_*` 7 格）、**脚本命令行参数轴**（`Args_*` 8 格）。

这不是「VB 少测了几个点」，而是**四到五条完整的轴没有开**。缺口的形状是**轴级**的，不是点级的。

### 网二：VB 特有语法在脚本容器里没铺全（语法 ledger）

第二轮 U7 的矩阵按「声明 × 修饰 × 语句 × 引用 × 选项」五维铺，但它是一次**逐格发现**的产物：由第二轮当时已知的崩溃族驱动，铺到哪算哪。实测的反证（本任务探针，**已运行**）：

- **枚举来源本身不完整**：`design-overview.md` §2 最初只取 spec 标题树作清单，而 spec 是 `{{VbLang}}` 镜像、**不含 fork 新增语法**（`ShebangDirectiveTriviaSyntax.vb` 在仓库里却不在 spec 中）⇒ 行集改为 **spec ∪ parser 分派 arm ∪ fork 指令**三源并集（见 `README.md` §四 判据①）；
- `Static`（局部静态变量，VB 独有关键字）在整个 `Scripting\VisualBasicTest\` 里**零命中**；
- `Err` 对象、`Resume` 语句（独立形态）、`On Error GoTo 0` / `On Error GoTo -1`、`CallByName`、`LBound`/`UBound`、`IIf`/`Choose`/`Switch`、`Like`：**零命中**。

> **本节初稿的事实错误（已修，保留记录）**：初稿把 `Erase`、`AddressOf` 方法组、`Declare … Alias/Auto` 也列进「零命中」，**这三项是错的**——独立复核（**实锤**）：
> - `Erase`：`ScriptModeStatementConformanceTests.vb:327` 的 `TopLevelReDimAndErase_Conform` 在**脚本顶层**执行 `Erase fixedArr` / `Erase dynamicArr`（`:336,337`）并断言结果。
> - `AddressOf`：`ScriptTests.vb:338` 的 `TestTopLevelRemoveHandler` 在**脚本顶层**用 `AddHandler Changed, AddressOf Handler`（`:340,341`）。
> - `Declare … Alias/Auto`：`ScriptModeSubmissionConformanceTests.vb:426` 的 `TopLevelDeclareForms_Conform` 覆盖 `Alias`/`Auto`/`Ansi`/`Unicode`（`:428-431`）。
>
> **该错误是本轮最有价值的教训**：初稿的零命中主张用了**单次联合 Grep**（模式见 §5 自检表），而该 Grep 的**模式串里没有 `Erase`/`AddressOf`/`Declare`**——检索方式与被主张的对象不匹配，就把「我没检」写成了「它没有」。**此后所有零命中主张，检索模式必须覆盖被主张的每一个词项。**

这一网缺口的形状是**枚举不完整**：没有一张「VB 语法有哪些」的清单拿来做底，就永远不知道还差哪些格。

## 2. 两类缺口的破法不同

| 网 | 缺口的形状 | 破法 | 达成证据 |
|---|---|---|---|
| 网一（C# 对标） | **轴级缺失** | 以 C# 测试族为行建**矩阵**，逐族判「适用 / 不适用」并补测 | 矩阵每行的「VB 侧覆盖」列闭口 |
| 网二（VB 语法） | **枚举不完整** | 以 `vblang\spec\` 的标题树为**权威清单**建 **ledger**，逐项标状态并填缺口 | ledger 的 `缺口` 行数为 0，每个 `不适用` 带检索证据 |

**两张网都不以「用例数」为判据。** 用例数是副产品。这是本设计的核心剪枝（见 §5 剪枝自检第 1 条）。

### 为什么网二要用 spec 标题树而不是「VB 关键字表」

候选有四个，逐一否定（**已检查**：四个来源都打开读过）：

| 候选 | 问题 |
|---|---|
| `SyntaxKind` 枚举（`Syntax\SyntaxKind.vb`）**直接**当行 | 是**语法节点**的分类，不是**语法构造**的分类：一个构造拆成多个 kind（`WhileBlock` 拆 `WhileStatement` + `WhileBlock` + `EndWhileStatement`），也有 kind 不对应用户写法（`BadStatement`）。拿它**直接**当清单会得到「清单比语言大」的失真结果 |
| `Parser\ParseStatement.vb` / `ParseExpression.vb` 的**分派 arm** | **不反对，反而采纳**：这两个文件（实测 1914 / 1910 行）以 `SyntaxKind` 关键字为 arm 做 `Select Case` 分派，是「实现实际支持什么」的**机械可枚举**集合，每个 arm 天然对应一个构造。**用作判据①的第二源与 U11 的全量差集反查**（见 `README.md` §四） |
| `KeywordTable`（`Scanner\KeywordTable.vb`） | 只有关键字，没有**组合**：`For Each` / `Option Strict` / `On Error GoTo 0` / `Handles` 子句都是多关键字组合，单关键字表接不住 |
| `vblang\spec\statements.md` **标题树** | **选它（但只作三源之一）**：标题即语法构造，粒度正好（`### ReDim Statement`、`### Erase Statement`），且它是**规范**——「VB 有哪些语句」的定义性来源，不是实现细节。**局限（本轮踩出）**：它是 `{{VbLang}}` 镜像，**不含 fork 新增语法**；且标题并非都是构造（`#### Mutable structures in async and iterator methods` 是语义说明） |
| 第二轮 U7 的五维表 | 是上一轮的**产物**不是来源；用它当底等于把上一轮的枚举不完整当成新的底 |

**代价**：spec 标题树的粒度是「构造」，不是「构造 × 容器」。同一个 `### On Error Statement` 小节在脚本顶层与嵌套容器里行为不同，ledger 因此给每行两栏（顶层 / 嵌套）。这个两栏设计是**对的**——第二轮的核心教训就是判别力全部来自这两栏的差。

## 3. A1 的假阳性与它背后的真问题（**本轮已推翻自己的初步结论**）

### 3.1 初步结论（**已被推翻，保留作为记录**）

第一轮探针（`run4.py`）发现：脚本顶层 `Dim q = From w In words Group By k = w.Length Into g = Group Select k, c = g.Count()` 抛 `InvalidCastException`（exit `2147500034`），而**同一条查询放进脚本内嵌套类的方法**则正常（exit 0，`G=2:2,1:1,3:1`）。据此初步判定「**脚本顶层容器特有缺陷**」。

### 3.2 推翻过程（**实锤，已运行**）

| 轮次 | 对照 | 读数 | 说明 |
|---|---|---|---|
| `run4.py` | 普通模式三格 | `compile-err BC36593` | **探针无效**：用 `/imports:` 代替 `/r:`。此失败被误读为「对照不成立」 |
| `r4/ordinary_control.py` | 普通模式补真实 `/r:`（**先用 `vbc.exe` 直调**） | `BC30652`（需要 mscorlib 4.0.0.0） | **该行描述的是 `vbc.exe` 直调那一次**：桌面 `mscorlib` 与 net10 runtime 引用冲突 ⇒ 该通道无效。**随后改用 `vbi <file>.vb /out:` 编译模式重跑并成功**——磁盘上 `r4\ord-groupbyo.dll` / `ord-groupby-counto.dll` / `ord-lambda-multilineo.dll` 与三个 `.runtimeconfig.json` 均在（**实锤**：`COMPILE-ERR` 不会产出 `.dll`）。故 r4 的对照**是跑通了的**，只是其读数被 r5 的更严对照取代 |
| `r5/pin.py` | 普通模式经 **vbi 编译模式**（`/out:` + `/target:exe`） | `module-shared-sub-main` exit `3762504530`，**同一条 `InvalidCastException`** | **决定性反证**：普通模式抛的是同一条异常 ⇒ 「容器特有」不成立 |
| `r5/decisive.py` | 普通模式 + **`Option Infer On`** | **exit 0，`G=2:2,1:1,3:1`** | 关键变量是 **`Option Infer`**，不是容器 |
| `r5/decisive.py` | 脚本顶层 + `Option Infer On` | 仍 exit `2147500034`，消息是**晚期绑定**的 `Overload resolution failed` | ⇒ 顶层 `Dim q = …` **没有**因 `Option Infer On` 而推断 |
| `r5/field_infer.py` | 脚本顶层 `Dim values = {1,2,3,4}` 后 `From v In values` | **`BC36593`：表达式「`Object`」不可查询** | **编译器自己说 `values` 是 `Object`** ⇒ 顶层不推断，实锤 |
| `r5/decisive.py` | 顶层 `Dim o = arr` 后 `o.Length` | exit 0，`N=2` | 晚期绑定本身可用；失败的是**对匿名类型形状的晚期 `Select`** |

### 3.3 结论（**实锤**）

**A1 不是编译器缺陷。** 失败链条是：

1. 脚本顶层 `Dim q = <查询>` **不推断类型**，`q` 是 `Object` 字段；
2. 对 `Object` 调 `.Select(...)` 走**晚期绑定**；
3. 晚期绑定器对该匿名类型形状找不到 `Select` 重载，抛 `InvalidCastException`。

第 1 步是**既有已测行为**（共享源码事实 **F3**，`test-plan.md` §C.3 的种子行亦收录）。第 3 步是**晚期绑定失败的普通 VB 语义**。

**「编译器从未崩」的证据等级（初稿未标注，按复核意见补）**：

| 侧 | 等级 | 依据 |
|---|---|---|
| **普通侧** | **实锤** | 退出码 `3762504530` = `0xE0434352`（CLR 未处理异常）**取自 `dotnet <dll>` 运行期**（`r5/pin.py` 的 `run_ordinary` = `vbi <file>.vb /out:` **编译** + `dotnet <dll>` **运行**）；磁盘上 `module-shared-named-methodo.dll` 存在 ⇒ **编译成功**。消息文案 `Overload resolution failed because no accessible '{0}' can be called with these arguments:{1}` 实测是 **VB 资源串**（`Compilers\VisualBasic\Portable\VBResources.resx`），即运行库晚期绑定的失败形态，不是编译诊断 |
| **脚本侧** | **推测（高置信）** | 退出码与消息与普通侧**逐字相同** ⇒ 判为同一运行期机制。**未**单独插桩确证脚本侧也走运行库晚期绑定 |

**判别力来自 `Option Infer On` 那一对**：普通模式开推断后成功、脚本顶层开推断后仍以晚期绑定失败。这证明分歧点是**推断是否生效**，与容器无关。

### 3.4 唯一值得追踪的真问题（**D5 分歧**，非崩溃）

顶层 `Dim x = <expr>` 不推断，而 **C# 脚本的顶层 `var x = <expr>` 推断**——按 **D5**（基础功能以 C#/csi 实现为蓝本），这是一处分歧。

但它的性质是**能力/语义分歧**，不是本任务范围的缺陷修复，且**可能是有意**的（VB 的字段类型推断规则本就不如 `var` 宽松；`ScriptModeStatementConformanceTests.vb:606` / `:623` 把它当既定行为测了）。

**处置**：本任务**不改**该行为。只做两件事：

1. 把「顶层不推断」的后果**补成用例**（`Dim q = <查询>` → 晚期绑定失败 / 加 `As` 后正常），让它成为**有网的行为**而非偶然发现；
2. 把 D5 分歧登记为 `issues\` 条目或 `OPEN QUESTIONS`，**交用户裁决**是否要按 csi 对齐。**不得**在补测单元里顺手改语义（`design-detailed.md` §4 共同纪律）。

**因此 U1 从「缺陷收口」降级为「判别性用例补测 + 分歧登记」。**

## 4. 修法形状

| 单元 | 形状 | 一个单元改几处 |
|---|---|---|
| U1（A 组） | **纯测试 + 分歧登记**：A 组三条均已推翻，只剩「把顶层不推断的后果补成成对用例」与 D5 分歧登记 | **0 处产品代码** |
| U2（ledger 建表） | **纯文档产物**：不碰编译器，只产出 `test-plan.md` §C 的表 | 0 处代码 |
| U3–U9（网一补测） | **纯测试新增**：不碰编译器（除个别用例暴露缺陷时按 A1 同规则处理） | 每族 1 个测试文件或既有文件的 Region |
| U10–U11（ledger 填充） | **纯测试新增** | 每缺口 1+ 用例 |
| U12（收口） | **文档 + 门期望值** | 数字同步 |

**共同纪律**：U3–U11 是**补测**，默认**不改产品源码**。任何单元若在用例中发现产品缺陷，**停手**——按 A1 的处置流程（建 issue → 判定 → 修复 + 用例）单独收口，不混在补测单元里顺手改。

## 5. 全称主张剪枝自检

对本文件夹四份文档的**全称主张**（无 / 都 / 任何 / 唯一 / 一律 / 全部 / 零）逐条挂证据；举不出证据的降级为推测或删除。

| 主张 | 状态 | 证据 |
|---|---|---|
| 「`Static` 在本 fork 的 `Scripting\VisualBasicTest\` 里**零命中**」 | **实锤** | 两条独立检索：(1) `Grep "Static"` over `*.vb` 去掉 `Shared` 后逐行读，命中全部是 `CancellationToken` 形参与 `<DebuggerDisplay>` 夹具，**`Static ` 作为 VB 关键字一行未命中**；(2) 下行联合 Grep 的 `\bStatic\b` 分支同样零命中 |
| 「`<host>` / `<implicit>` / `catchException` / `GetImportScopes` / `FormatException` / `AllowUnsafe` / `CheckOverflow` / `WarningLevel` / `loadpath` / `libpath` / `SourcePaths` / `ReferencePaths` / `Static`（词）在 `VisualBasicTest` 里零命中」 | **实锤** | 单次联合 Grep（模式 `<host>\|<implicit>\|catchException\|GetImportScopes\|FormatException\|AllowUnsafe\|CheckOverflow\|WarningLevel\|loadpath\|libpath\|SourcePaths\|ReferencePaths\|\bStatic\b\|Args\.`，范围 `Scripting\VisualBasicTest\**\*.vb`）**共 14 处命中，逐行读后全部是 `System.EventArgs.Empty`**（`CommandLineRunnerTests.vb:537`、`ScriptModeConformanceTests.vb:446,474`、`ScriptModeStatementConformanceTests.vb:351,374`、`ScriptModeSubmissionConformanceTests.vb:243`、`ScriptTests.vb:45,360`、`ScriptTopLevelCrashTests.vb:303,330,347,438,941,995`）。**除 `Args\.` 这一模式外，其余 13 个模式零命中**；`Args.` 的命中亦全部是 `EventArgs.`，即**脚本宿主全局 `Args` 无任何用例** |
| 「`<host>` / `<implicit>` **作为引用别名**在 `VisualBasicTest` 里零命中」 | **实锤（独立复核）** | 上一行的联合 Grep 用到 `<>` 字面量，而 `<>` 在 VB 里是**不等运算符**，其字面量的安全复现不可靠 ⇒ **改用两个纯词模式独立复核**：以 `host` 检索 `Scripting\VisualBasicTest\**.vb`，命中全是 `Microsoft.CodeAnalysis.Scripting.Hosting` 命名空间导入与英文散文（"the host"、"host RID"、"host gate"）与 `ImportsAccumulationFailureTests` 的注释；以 `implicit` 检索，命中全是 VB 语言的 implicit（"Implicit Me"、"implicit local"、"implicit upper bound"）与英文散文。**无一处是引用别名** |
| 「`<host>` 的规范与生产锚点不存在」 | **已推翻（本项有规范）** | `spec\spec-reference-directive.md:160-180`「Reference aliases」给出**规范性**定义（含「No escape hatch」与「普通编译同规」）；`proposals\proposal-reference-directive.md:109-118` 给出生产锚点（`Script.cs:237-239`/`:259-269`、`RuntimeMetadataReferenceResolver.cs:27-29`/`:140-143`、生效判据 `CommonReferenceManager.State.cs:721-725` 与 `MergedNamespaceSymbol.vb:107-120`）。**U4 的期望值来源由此从「先读生产侧」升级为「引规范小节」** |
| ⚠️ **检索方式陷阱（方法学记录）** | **实锤** | **凡模式中含 `<` 的 Grep 都可能静默零命中**——不只是 `<>`。复核者的反例（**实锤**）：对 `ScriptTests.vb` 搜 `<Fact`（**不含 `>`**，且该文件满是 `<Fact>`）同样得「No matches found」；另对 `RuntimeMetadataReferenceResolver.cs` 搜 `<implicit>` 得零命中，而 `WithAliases` 模式返回的 `:29` 行**逐字含 `"<implicit>"`**。**根因不是「`<>` 在 VB 里是不等运算符」**（ripgrep 无 VB 语义）——是**模式通道对 `<` 的转义伪影**。**故含 `<` 的零命中主张一律改用纯词模式复核后方可采信**；本表的 `<host>`/`<implicit>` 一行即由此复核而来 |
| ⚠️ **复核者自审（牵连其一审结论）** | **实锤** | 复核者一审报告中「`Grep '<host>|<implicit>|catchException|…' → No matches found`（实锤）」**取自同一条不可靠通道，该条本身不成立**；其二审改用可靠通道复跑 12 个纯词模式，**结论仍为真**（全部 0 命中，`Static` 关键字模式亦 0）。⇒ 实质结论不变，**根因表述已按上一行改宽** |
| 「`DebuggerDisplay` 只在 `Helpers\ObjectFormatterFixtures.vb` 出现，**没有**测试引用」 | **实锤** | 独立 Grep（该模式不在上述联合 Grep 内）：命中全部落在 `Helpers\ObjectFormatterFixtures.vb:112-348`（含 `DebuggerTypeProxy` 于 `:317`）；测试文件零命中 |
| 「`CancellationToken` 只出现在 NuGet 协调器形参与 `ScriptTaskExtensions` 帮助方法签名，**没有**脚本运行时的取消语义测试」 | **实锤** | 独立 Grep 的全部命中已逐行读（`NuGetRestoreCoordinatorTests.vb` / `NuGetRuntimeHandshakeTests.vb` / `NoNuGetZeroRegressionTests.vb` 的形参与 `Implements` 签名，`Helpers\ScriptTaskExtensions.vb:5-21` 的帮助方法签名）；无一处断言「取消后声明保留」 |
| 「C# 侧共 303 个用例（265 + 38）」 | **实锤** | `design-detailed.md` §B.0 的逐文件计数表；族内方法数之和 = 303，与逐文件计数差 0 |
| 「VB 侧 Scripting 测试共约 437 个方法 / 脚本模式相关 367 个」 | **推测** | 基于 `<Fact>` 属性总数 451 减去 Helpers 与夹具成员；**Helpers 目录内逐文件属性数未逐一核对**。该数**不作验收判据**（见 §2 剪枝），故不升级为实锤 |
| 「spec 标题树实测 43 / 40 / 82 / 18 个小节」 | **实锤** | 本轮自跑 `grep -c '^## ' '^### ' '^#### '` 五文件循环，读数见 `README.md` §四 |
| ~~「A1 是脚本顶层特有」~~ | **已推翻** | 被 `r5/pin.py` 的 `module-shared-sub-main` 推翻：普通模式不开推断时**抛同一条异常**。保留此条以记录判定纪律——三轮探针的「对照失败」两度被误读为缺陷成立 |
| 「A1 的真变量是 `Option Infer`」 | **实锤** | `r5/decisive.py`：普通模式**开推断** exit 0（`G=2:2,1:1,3:1`），脚本顶层开推断仍以**晚期绑定**消息失败 ⇒ 分歧只在「顶层是否推断」 |
| 「脚本顶层 `Dim x = <expr>` 不推断」 | **实锤** | `r5/field_infer.py` 的 `script-top-infer-linq`：**编译器自己**报 `BC36593`，把 `values` 描述为 `Object`。这是编译器对自身绑定结果的陈述，强于任何反射判别 |
| 「A3 的探针不具鉴别力」 | **实锤** | `r5/field_infer.py` 的 `script-top-field` 得 `F=Int32`，但晚期绑定调 `Object.GetType()` 返回的是**运行时类型**，不能区分 `Object` 与 `Int32`。既有用例 `ScriptModeStatementConformanceTests.vb:606` 用**重载决议**判别得 `Object`，与此一致 |
| 「A 组三条**都**不是编译器缺陷」 | **实锤（三条各自）** | A1：见上三行；A2：**实锤（机制）+ 推测（该形状）**——`r5/decisive.py` 的 `script-top-object-receiver-array`（`Dim o = arr` 后 `o.Length` exit 0）证明晚期绑定本身可用，失败只发生在对匿名类型形状的晚期调用；**该结论由排除法得出，未对 A2 形状本身做「加 `As` 后是否正常」的直接对照**，故「该形状」一档标推测。A3：见上一行。**限定**：仅对本轮实测的三个形状成立，不推广 |
| 「网一缺口的形状是轴级的，不是点级的」 | **推测** | 基于矩阵中五条轴（宿主对象 / 声明保全 / 调试信息 / 对象格式化 / 命令行参数）**每条都是整条零命中**（**实锤**）推断「轴级」这一概括；若实施期发现某条轴其实有零散覆盖，该概括降级 |
| 「两张网都不以用例数为判据」 | 不适用 | 这是**设计决定**，不是事实主张；它的依据是 §2 表格与 §5 第 1 条剪枝 |
| 「判定原则的**全部**判别力来自两栏的差」（`README.md` 与本节各一处） | **推测 → 降级为「主要来源」** | 两栏之差的确是判别力的主要来源（第二轮的 C2/C3/C4 判定均由此得出，见 `tasks\script-top-level-crashes-2\README.md` §二），但**未穷举**其它判别手段（普通模式对照、诊断 ID 比对、`Option` 设定对照等本轮都用过）⇒ 不成立为「全部」 |
| 「U3–U11 默认不改产品源码」（`design-detailed.md` §4 共同纪律） | 不适用（**纪律声明**，非事实） | 与本节已有的先例同口径；实施期若用例暴露缺陷，按 §4 走独立收口，纪律本身不变 |
| 「ledger 中无 `缺口` 行 / 每个 `不适用` 带检索证据」 | 不适用（**目标**，非事实） | 已被 §四 的三项合取判据取代，此为目标表述 |
| 「用例**一律**用内存 API」（`test-plan.md` §2） | **待定** | 是**纪律**；既有四个文件已有落盘 helper（`test-plan.md` §2.1.1 已列），纪律只约束**新增**用例，收口时以「新增用例零落盘」为实际判据 |
| 「负向格与预期报错的正向格必须**成对**」（`test-plan.md` §3） | 不适用（**纪律**） | 同上 |
| 「ledger 行集**只取**与容器无关或受容器影响的」（初稿 §C.0） | **已推翻** | 该「只取」会缩小分母、使判据③失效；已改为**全量出行**，逐行标 `不适用（与容器无关）` 并给理由（`README.md` §四） |
| 「`Erase` / `AddressOf` / `Declare … Alias/Auto` 零命中」 | **已推翻（假主张）** | §1 的关键词清单初稿含此三项，**全错**：`Erase` 见 `ScriptModeStatementConformanceTests.vb:327,336,337`、`AddressOf` 见 `ScriptTests.vb:338,340,341`、`Declare … Alias/Auto` 见 `ScriptModeSubmissionConformanceTests.vb:426,428-431`（**实锤**，逐条打开读过）。根因：初稿的联合 Grep **模式串未含这三个词项**，把「未检」写成「没有」 |
| 「PDB 面是把回归锁到调试信息层的**唯一**手段」 | **推测 → 降级为「一种手段」** | 见 `design-detailed.md` §U7；未穷举其它手段，**不成立为「唯一」** |
| 「`GetImportScopes` 是**唯一**直接观测导入作用域结构的 API」 | **推测 → 降级为「一个直接观测点」** | 见 `design-detailed.md` §U9；未穷举 |
| F4「脚本类非限定成员引用**一律**经 `TryBindInteractiveReceiver`」 | **实锤（带限定）** | 第二轮 `tasks\script-top-level-crashes-2\design-overview.md` 的自检表已列同条并标注「**限定**：只对走了 `TryBindInteractiveReceiver` 的路径成立」。本轮沿用该限定，**不得**省去 |
| F8「`dotnet test` 对 MTP 项目**静默跑 0 个**」 | **实锤** | 第二轮 `test-plan.md` §4 与本仓库既有记忆条目均记此现象；收口时以「核 `TestCasesToRun > 0`」为实际判据，不依赖该主张本身 |
| 「普通上下文**一律** `vbc.exe`…脚本上下文**一律** `vbi.exe`」 | **已推翻（本轮踩坑）** | `vbc.exe` 的 `vbc.rsp` 用 `/sdkpath:` 指向桌面框架目录，与 net10 runtime 冲突（`BC30652`）⇒ 普通模式对照**改用 `vbi <file>.vb /out:` 编译模式**。见 `README.md` §七 探针纪律 |
| 「ledger 中**无** `缺口` 状态行；**每个** `不适用` 带检索证据」 | 不适用（**目标**而非事实） | 是结束条件的表述；其可判定性由三项合取判据（`README.md` §四）保证 |
| §B.1 矩阵中 **27 处** `缺`/`完全缺`/`部分缺` | **待定** | 计数属实（复核者实锤），但**逐条零命中证据未挂**——U2/U10 建表与补测时须逐条给检索命令；举不出证据的降级为 `推测` |
| §C.3 种子行 **23 处** `缺口` | **部分已推翻** | 复核者逐行核对后指出**至少 5 处误标**（`Erase`、`AddressOf`、`Declare`、`Partial`、`Structure`/`Enum`）与**5 处两栏对调**（`Option Strict On/Off`、`Option Compare Text`、`Partial`、`Structure`）。见 `test-plan.md` §C.3 的修订说明 |
