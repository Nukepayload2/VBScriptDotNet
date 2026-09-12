# VBScript.NET 产品语法建议索引

本文档登记 **VBScript.NET 产品自身**的提案（proposals 层），按 vblang/csharplang 规范组织为「建议 → 会议 → spec」三层结构。每个独立的语法/能力增强对应一份提案文档。

**与 modvb 的关系**：`../modvb/`（Anthony 的 ModVB 提案库）是 VBScript.NET 的**参考来源**；本目录描述 VBScript.NET 产品自身的提案，不依赖 Anthony 原文，两者分离。产品提案基于产品版本归档（`vbx-1.2-beta/`）与设计推理。

## 建议状态（四类，仿 csharplang / modvb 组织）

| 状态 | 位置 | 含义 |
|------|------|------|
| **active** | `proposals/` 根目录 | 设计在推进（RESOLUTION **Active** / **Consider**），准备进入实现 |
| **inactive** | `proposals/inactive/` | 有前景但暂不优先 / 未定型（RESOLUTION **Table**） |
| **rejected** | `proposals/rejected/` | 否决（RESOLUTION **Reject**） |
| **done** | `proposals/vbscript-<版本>/` | 已随 VBScript.NET 发布版本实现并定稿（成员见下，1.2 版已归档） |

- **判定依据**：各会议纪要「三态判定」小节（`../meetings/`，与提案 1:1 同名镜像组织）。起草与定位提案可对照 `../dream-of-vbdev.md`（§2–§5/§7：主线/persona/情绪谱/六元素/三方张力；非权威输入，为提案提供动机与定位）。
- **分类反映当前设计意图**，不排斥后续复活（inactive/rejected 可因信号回升）或归档（active 完成后转 done）。
- 会议纪要目录与提案目录同步组织：`meetings/` ↔ `proposals/`、`meetings/inactive/` ↔ `proposals/inactive/`、`meetings/rejected/` ↔ `proposals/rejected/`。
- 状态行（模板顶部）：`Proposed / Prototype / Implementation / Specification` 复选框标记进度。

---

## Active（21 份，根目录）

> RESOLUTION = Active 或 Consider。编号为产品提案序列号。Proposed 未定三态的提案暂列 active 根目录，判定待 LDM 会议评估。

| # | 文件名 | 建议 |
|---|--------|------|
| 01 | `proposal-avalonia-ise-repl-ui.md` | Avalonia UI + Avalonia Edit 仿制 PowerShell ISE 的图形化 REPL/脚本编辑器（Consider，易用性提升） |
| 02 | `proposal-optional-question-prefix.md` | REPL 表达式开头问号 `?` 可选，对齐 C# REPL 自动打印表达式结果（Active） |
| 03 | `proposal-vscode-extension-ise-repl-ui.md` | VS Code 扩展：在 VS Code 内置 REPL + 脚本编辑（仿 vscode-powershell 架构，宣传/触达 vs 内存/轻量，与 Avalonia 双路线互补）（Proposed，待 LDM 评估） |
| 04 | `proposal-byref-like-safety.md` | byref-like 类型安全（对齐 C# `ref struct`）：识别 `IsByRefLikeAttribute` + suppress ref struct obsolete error + 移植 RefStructHelper BCX 规则进编译器 + REPL/脚本顶层约束；**对 vbx 与常规编译模式都生效**，追上 .NET 生态（`Span`/`allows ref struct` 接口）的重要一步（Proposed，设计来源 `meeting-byref-like-repl-safety.md`，服务于 p1 前置-1 D1） |
| 05 | `proposal-shebang-directive.md` | `.vbx` 首行 `#!` shebang 指令（编译器语法层，镜像 C#；**已实现**，见 `../tasks/shebang-directive/`） |
| 06 | `proposal-consume-csharp-extension-and-interface-shared.md` | 消费 C# 扩展成员（扩展属性/运算符，扩展方法已可用）与接口共享成员（SAIM：`T.Zero`/`T.Add` 经类型参数）；**RESOLUTION Active（D4 P1 判入）**，见 `../meetings/meeting-consume-csharp-extension-and-interface-shared.md` |
| 07 | `proposal-distribute-compiler-nuget-package-and-dotnet-tool.md` | 把 fork 编译器分发为 Toolset 风格 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta，.vbproj 引用即用 fork VB 编译器；不含 csc、不替代 dotnet 工具链）与 `.net tool`（`Nukepayload2.Compilers.VBScriptDotNet.Cli`，`vbi` 命令：复用现有 vbi 二进制，批量编译 + `.vbx` 执行 + shebang 解释器；编译器 NuGet 包不是 vbi，tool 才是 vbi）；**RESOLUTION Active**（四条 Unresolved 闭合：隐式分派 / 不补 VBCSCompiler / v1 仅 .NET SDK / 双行版本），见 `../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` |
| 08 | `proposal-script-optimization-level.md` | 脚本编译优化级别：`/optimize+` 对脚本生效（透传 `arguments.CompilationOptions.OptimizationLevel`，默认仍 Debug），服务高 CPU 脚本用例（Active，见 `../meetings/meeting-script-optimization-level.md`） |
| 09 | `proposal-vbi-script-diag-mode.md` | vbi 脚本模式诊断检查 `/check`：只编译输出错误+全部警告、不执行不落盘（复用 `Script.Compile()`，对标 `cargo check`），服务 AI 开发 vbx / CI 编译门；**命名由 meeting RESOLUTION 从提案原 `/diag` 改为 `/check`**（AI 先验标准）（Active，见 `../meetings/meeting-vbi-script-diag-mode.md`） |
| 10 | `proposal-consume-ref-readonly.md` | 消费 C# `ref readonly` 返回（ReadOnlySpan 索引器 / `GetPinnableReference`）：豁免 required `modreq(In)`（**返回 + 参数双路径**，顺带解锁虚方法/委托 `in` 参数）+ `ReturnsByRefReadonly` 只读追踪 + 按接收方分类写入语义（直接赋值/复合赋值/Mid/With 块成员写/链式成员写复用 `ERR_LValueRequired`(30068) 拒——With 与链式一致，复会 2026-09-01 修正 R8 定稿；ByRef 实参 copy-out 传副本写回丢弃，推断褪 ByRef）；**RESOLUTION Active（D4 P1）**，见 `../meetings/meeting-consume-ref-readonly.md`；For Each over ReadOnlySpan 顺带解锁（S24 已翻转通过）；**已实现，spec 见 `../spec/spec-consume-ref-readonly.md`** |
| 11 | `proposal-in-parameter-recognition.md` | 识别 `in` 参数（只读引用）并让只读来源（`ref readonly` 返回）传 `in` 参数从 copy-out 改零拷贝直传，对齐 C#（C# 侧 `ref readonly`→`in` 零拷贝直传实锤：规范 `readonly-ref.md` + 编译器 `EmitAddress.cs`）；三段实现（crack `[IsReadOnly]`→`RefKind.In` / 绑定放行 / 发射直传）；**R7 后续项、Proposed 待立项**（父 RESOLUTION `meeting-consume-ref-readonly.md` R7「v1 不做、等性能证据独立立项」） |
| 12 | `proposal-net472-desktop-branch.md` | Toolset 编译器包补 **net472 桌面分支**（`tasks/net472`：netstandard2.0 编译器库 + net472 `vbc.exe`+rsp+config + net472 任务 DLL），让 Visual Studio 的 .NET Framework MSBuild（Full host）能用 fork VB 编译器——**修正 distribute v1「net472 允许缺失」的未实测定案**；不做 bridge、不引 VBCSCompiler（沿用 `UseSharedCompilation=false`）、VB-only；props 按 `MSBuildRuntimeType` 分派（Core→netcore / Full→net472）；双老登评审 + RESOLUTION R1–R9，见 `../tasks/net472-desktop-branch/`（含实现与验证） |
| 13 | `proposal-vbi-nuget-reference.md` | vbi 脚本 NuGet 包引用：`#R "nuget:包名[, 版本]"`（大小写不敏感前缀，dotnet-interactive csx 逗号设计）→ 内容寻址临时 vbproj + dotnet CLI 还原 + 读 assets 喂编译/运行时；fork `NuGetPackageResolver` 把上游斜杠缝接活成状态化 seam，宿主环异步还原；SDK 门控（仅用 nuget 才要求 SDK，global.json 跨主版本 roll-forward）；无 nuget 零回归；**RESOLUTION Active**，见 `../meetings/meeting-vbi-nuget-reference.md`（2026-09-06；接线 C3 打回重设计 + U9 spike 先行） |
| 14 | `proposal-vbi-project-reference.md` | vbi 脚本/REPL 本地工程引用：`#R "project:<工程路径>"`（大小写不敏感前缀，第三种宿主解释前缀）→ spawn 真实 `dotnet build` 构建用户本地可变工程 + MSBuild 读真实输出路径（`TargetPath`）匹配「运行宿主可加载」产物 + 范围乙闭包（主产物 + ProjectReference 输出 + 工程自身 NuGet 依赖）；沿用近邻宿主驱动环 seam + 并行 `ProjectResolver` 抽象缝，共享编译器 Core 零改动（继承已落地 N>1）；always-spawn（SDK 增量 up-to-date），REPL 内改工程需重启会话；**RESOLUTION Active**，见 `../meetings/meeting-vbi-project-reference.md`（2026-09-09；U8 产物读取 + 范围乙闭包两 spike 硬门控先行） |
| 15 | `proposal-scripting-dialect.md` | 脚本方言的声明与提交模型（`SourceCodeKind.Script`）：顶层代码免包装 → 编译器合成 script class（顶层 `Dim`=字段 / 顶层 `Sub`/`Function`=实例成员 / 顶层 `Class`=嵌套类型 / 顶层可执行语句=实例初始化器）+ 提交链（`PreviousScriptCompilation`）与跨提交可见性（前序 script class 成员 + 宿主对象成员）+ 入口点合成（`<Initialize>`/`<Main>`/`<Factory>`，顶层 `Await` 的根因）+ 顶层 `AddHandler`/`RemoveHandler` + `Imports` 跨提交累积（宿主侧机制）+ 脚本专属限制与诊断族（BC36965/BC36966 等）；**RESOLUTION Active**，见 `../meetings/meeting-scripting-dialect.md`；三处 supersede 口径（§4 `<Factory>` 签名 `As T`→`Task(Of T)` / §7 显式 `Me` 系禁令范围收窄 / §8 收窄基准改写为「与 C# 顶层语句同向、与共享 `Script<T>` 有意分叉」），实施项另立跟踪 |
| 16 | `proposal-vbscript-lsp.md` | vbscript.net LSP——普通 VB 项目 + VBX 脚本：跨编辑器 LSP server（单进程双模式，普通 `.vbproj`/`.sln` 走 MSBuild workspace + 松散 `.vbx` 走 `SourceCodeKind.Script`，两模式唯一区别是 script mode）；成本基线 vb-ls（2 个微型 patch ≈110 行 + launcher）不可直接照搬（fork `Compilers\` 剪枝、无 Workspaces/Features/LanguageServer 层），补层 = 从本地基线 `{{Roslyn}}` 复制未修改 IDE 栈项目对齐 fork 构建；语言服务器独立进程 + dotnet tool 分发；里程碑 M0–M4；**RESOLUTION Active（Proposed）**，见 `../meetings/meeting-vbscript-lsp.md` |
| 17 | `proposal-reference-directive.md` | `#R` 引用指令本体（脚本源码层唯一程序集引用机制）的事实基线：语法/位置约束（编译期指令 trivia、仅 `SourceCodeKind.Script`、须在编译单元首个 token 之前）+ 声明表收集与增量脏标记 + 宿主 resolver 契约与解析顺序（操作数无语义，由宿主注入 resolver 决定）+ 单条 `#R`→N 展开（索引 0=主资产、余为依赖闭包）+ `<host>`/`<implicit>` 别名（无 `extern alias` 逃生口）+ 跨提交引用继承；`nuget:`/`project:` 前缀各归邻域提案；**RESOLUTION Active**，见 `../meetings/meeting-reference-directive.md` |
| 18 | `proposal-load-directive.md` | `#Load` 源文件加载指令（脚本多文件组织的唯一机制，已随 2.0 beta 落地）：编译器指令 trivia 语法 + 两处互斥门控（仅 `SourceCodeKind.Script` BC36967 / 须在编译单元首个 token 之前 BC37002）+ 宿主多树展开（`CollectLoadTrees` 深度优先、按引用树 `FilePath` 解析、加载树在前主树在后，树序即执行序）+ `#Load` 不进声明表、不产生引用脏标记（与 `#R` 的根差异）+ 多树 script class（顶层语句进同一 initializer，`Return` 为整个提交的退出码）+ 环/缺失共用 BC2001（现状）+ 菱形不去重（现状，与 C# 按解析后路径去重分叉）+ 头部指令类；**RESOLUTION Active**，见 `../meetings/meeting-load-directive.md` |
| 19 | `proposal-script-extension-methods.md` | 脚本类中扩展方法的合法形式与缺失诊断：四处缺口——①脚本类非 `Shared` 成员被当扩展方法却无诊断（Debug 断言终止 / Release codegen NRE）②顶层 `Module` 被降级成脚本类嵌套类型致扩展方法静默失效（调用点只报 BC30456）③扩展调用在降级期是「静态形状」而实例声明要求「实例形状」（候选 A 因此证伪）④承载在脚本类里的扩展方法**跨提交不可用**（REPL 提交 2 声明、提交 3 调用报 BC30456，而同形状普通成员跨提交正常；C# 侧以 `InSubmissionClassBinder` + 跨提交测试对齐）；含宿主与脚本类 kind 映射（`.vbx`/REPL 均为 `DeclarationKind.Submission`）、C# 对照、跨提交可见性基线；**十一条候选**（组① A/B/C/G；组② F/窄 D/D/E-a/E-b/E-c；组③ H）；**RESOLUTION Active（Proposed）**——**裁决 B + F + H 绑为一包采纳，A/C/D/窄 D/E-a/E-b/E-c 否决，G 表为可选**（G 受三项闸门：G1 属性/修饰符次序 spike、G2 B 的拼写代价、G3 信息提示；G 与 B 语义等同，故不属 interop 议题）；缺口②根因更正为 **VB 内部两个谓词分裂**（诊断侧 `AllowsExtensionMethods()` 不说嵌套 vs 收集侧 `MightContainExtensionMethods` 要求容器在命名空间层，`SourceMemberContainerTypeSymbol.vb:3341`）⇒ F 是**补回 C# 的嵌套检查**（CS1109，`SourceOrdinaryMethodSymbol.cs:230-232`，不豁免脚本类）而非新增概念；C 与窄 D 的代价被提案低估（`.vbx` 单提交是主场景且脚本无 `Namespace` 可改道；窄 D 破坏 `spec-scripting-dialect.md:125` 明文承诺，顶层 `Module` 跨提交可见已实测）；H 取形态 (a) 且**前置须先补登 `upstream-merge.md` 的 e307d0f 分歧**；见 `../meetings/meeting-script-extension-methods.md`（证据基线与 C# 对照另见 `../issues/issue-script-top-level-extension-method-crash.md`；D5 口径见 `../decisions.md`） |
| 20 | `proposal-with-events-in-submissions.md` | 提交里的 `WithEvents` / `Handles`：不新增语法，补语义与实现路径。现状不是静默失效而是**会话终止**（`Handles` 绑定的种类分派不接纳 `Submission`，`SourceMemberMethodSymbol.BindSingleHandlesClause` 落 `UnexpectedValue`；Release 下 4 条路径中 3 条终止——(a)(b) 退 9、(c) 退 1 且 REPL 会话终止（尾随提交不执行）、(d) 退 0；Debug 下 4 条全终止，退 35）；既有 `WithEvents` 挂/摘钩**已完整实现在属性 `Set` 访问器**且与类型种类无关，但候选 E 的复用**有前提**——挂/摘钩语句只在属性被赋值时执行，故 `WithEvents` 赋值初始化器与 `Handles` 子句必须**落在同一次提交**（§2.6）；同名前序 `WithEvents` 在绑定层是**遮蔽**而非替换（不可重载），跨提交 `RemoveHandler` 可摘，今天是「没有任何代码会把『新提交同名』当作替换信号并自动摘」；摘除的**真正自变量是「是否需要 relax stub」，与 `Handles`/命令式形状无关**（两种形状在直接兼容时都摘得掉、需 relax 时都摘不掉，且该限制同提交内同样成立）；C# 侧 `WithEvents`/`Handles` 零命中，故「VB 模型需要 C# 没有的机制」为真，但当前崩溃属**移植不完整**；上游编号 dotnet/roslyn#14073（`ImplicitNamedTypeSymbol.vb:213-219` 显式短路 + 注释点根因）；**Proposed，待 LDM 评估** |
| 21 | `proposal-submission-shared-members.md` | 提交/脚本类里的 `Shared` 成员：**非共享路径已与 C# 同形**（实例初始化器进 async `<Initialize>`，`MethodCompiler.vb:697-698`/`:1488-1490`；C# 侧 `MethodCompiler.cs:661-664` 用空初始化器集编译提交构造器），分叉只在共享这一半——①`Shared` 字段/属性带初始化器 → 宿主 `TypeLoadException`（**issue 05**：共享分支复用为实例版设计的 `SynthesizedSubmissionConstructorSymbol`，其形参表不随 `isShared` 分叉 ⇒ 发射出带参 `.cctor`；边界更正为「`StaticInitializers` 里有需注入的条目」，含 `Shared Dim arr(2) As Integer`）②共享初始化器含 `Await` → 断言终止（**issue 06**：绑定期 `IsInAsyncContext`（`Binder_Expressions.vb:4720-4728`）对脚本类**任何**字段/属性放行 `Await`，重写端却把它放进非 async 的共享构造器 ⇒ `AwaitOperator` 存活到 `EmitExpression.vb:206-209`；VB 对 C# CS8100 零对应诊断，三段 grep 全清单在案）+ **同族实测清点 40 条探针**（`Shared` 方法/`Shared Async` 健康；`Shared` 事件与 `Shared WithEvents` 的崩与 `Shared` **无关**，已新登记 **issue 07**「提交类由 `ImplicitNamedTypeSymbol` 承载 ⇒ `IsImplicitlyDeclared` 恒 True ⇒ 撞 `SynthesizedEventAccessorSymbol.vb:495` / `SourceWithEventsBackingFieldSymbol.vb:66` 断言」）+ spec 需改 11 处（含 `:56`/`:273` 两处「顶层四形式穷尽」与实测不符、`:176`「字段/属性初始化器是 async 上下文」对 `Shared` 未兑现）；**候选按专项调查（`tmp\meetings\script-extension-methods\investigation-shared-initializer-async.md`，承重锚点已独立复核）重排**——**丙（主候选）**：共享字段/属性初始化器**改道并入已有 async `<Initialize>`**（发射层通：`LocalRewriter_FieldOrPropertyInitializer.vb:47-54` 共享字段走 `stsfld` 不要求宿主方法共享；宿主契约零改动：`SynthesizedEntryPointSymbol.vb:340-389` + `GetSubmissionInitializer` 零消费点；`spec:64` 的源码顺序天然保住）**同时消掉 issue 05**，代价是**有意分叉**（C# 的静态初始化器绝不进 async 方法，仓内唯一书面理由是 `CodeGenAsyncTests.cs:8623-8625` 的测试注释）与 4 项待实证的时机语义；**甲**（共享分支改用无参 `SynthesizedConstructorSymbol`，复用 `EnsureCtor`）**仅在丙被否时才必要**；**乙**（移植 CS8100 到绑定期，须改 `Binder_Expressions.vb:4740-4744` 调用结构——只加 `GetAwaitInNonAsyncError` 分支不可达；C# 的静态属性初始化器因登记在 backing field 上（`SourceMemberContainerSymbol.cs:5917-5924`）同样命中 CS8100）**仅在丙被否时才是唯一修法**；**丙-2**（新造共享版 async 初始化器）机制通但**排序语义必破**、代价高、不主推；**「06 只能从崩改成报错」的早期表述已作废**；另新登记 **issue 08**（提交类共享成员不报 BC30369 → 运行期 `InvalidProgramException`；根因 `Binder_Expressions.vb:2257-2270` 的「No code in a script class is shared」前提）与 **issue 09**（初始化器里的 BC36937 报了但不阻止发射 → 嵌套类型字段/属性的 `Await` 崩，**与 `Shared` 无关**；issue 06 的触发面据此扩面）；**Proposed，待 LDM 评估** |

---

## Inactive（`inactive/`）

- 机制：RESOLUTION = Table（搁置/未定型）的提案放入 `inactive/`，与 modvb 的 inactive 目录同约定。

| # | 文件名 | 建议 | 三态判定 |
|---|--------|------|---------|
| 14 | `inactive/proposal-vbi-console-completion.md` | vbi 控制台 REPL 行内补全（inline suggestion，**控制台 UI 本体**）：v1 = L0 raw-key 行编辑器 + L1 行内灰字首候选，补全引擎浅档（不复制 Workspaces/Features），L2/L3 带触发延期 | **Table**（归档待触发，见 `../meetings/inactive/meeting-vbi-console-completion.md`：L0/L1 拆分、剥离保留浅档引擎路径等三件资产、复活闸门 G1–G3） |

---

## Rejected（暂无成员，`rejected/`）

- 机制：RESOLUTION = Reject 的提案放入 `rejected/`，与 modvb 的 rejected 目录同约定。
- 当前无成员。

---

## Done（vbx-1.2-beta/）

- 机制：特性随 VBScript.NET 发布版本实现并定稿后，归档到 `proposals/vbscript-<版本>/`（仿照 csharplang 的 `proposals/csharp-<版本>/` 与 modvb 的 `proposals/vbscript-1.0/` 归档规则）。
- **判入标准**：对应 LDM 判定 Active 且 `Implementation`/`Specification` 进度完成。
- **当前成员（1.2 版，微软商店已发布）**：VBScript.NET **1.2 beta** 已发布到微软商店（MSIX，`N2ForkVBInteractivePreview`，Identity Version=`1.2.0.0`），随其发布的「对齐 C# REPL」能力已归档到 [`vbx-1.2-beta/`](vbx-1.2-beta/README.md)：

  | # | 功能点（一句话） | 文件 |
  |---|------------------|------|
  | 01 | REPL 交互会话（`>` 提示符、多行续行 `.`、`?` 前缀打印） | `vbx-1.2-beta/proposal-repl-interactive-session.md` |
  | 02 | 指令系统（`#R`、`#help`、`/help`、`/version`、`/?`、`@vbi.rsp`、`/i`、`/` stdin、`-- script-args`） | `vbx-1.2-beta/proposal-repl-directives.md` |
  | 03 | 顶层代码免包装（`Dim` / `Sub` / `Function` / `Class` / `Module`） | `vbx-1.2-beta/proposal-top-level-code.md` |
  | 04 | 脚本 globals（`CommandLineScriptGlobals` / `InteractiveScriptGlobals`：`Args`、`Print`、搜索路径） | `vbx-1.2-beta/proposal-script-globals.md` |
  | 05 | .vbx 脚本执行（`vbi script.vbx [-- args]`，common scripting workaround） | `vbx-1.2-beta/proposal-vbx-script-execution.md` |
  | 06 | 运行时宿主选择（按 `' Attribute TargetFramework = "net48"` 注释选 net48 或 .NET 宿主） | `vbx-1.2-beta/proposal-runtime-host-selection.md` |

- 说明：1.2 是已发布版本，已随其发布的能力归档到 `vbx-1.2-beta/`；顶层 `Await`、`AddHandler` / `RemoveHandler`、`Imports` 为 1.2 损坏项、已由 2.0 修复（见 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`），不作为 1.2 功能点。
