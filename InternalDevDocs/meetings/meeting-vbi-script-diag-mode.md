# Visual Basic Language Design Meeting
August 29, 2026

议题是 `proposal-vbi-script-diag-mode`——vbi 脚本模式的诊断检查模式,开关名重新裁决为 **`/check`**。触发点是产品侧真实用例:AI 开发 `.vbx` 需要一条「只拿诊断、不运行」的反馈通道,类比 fxcop 和 dotnet build。调查把现状摸得很干净:`Script.Compile()` 这个"编译不运行拿诊断"的 API 一直躺在 `Script.cs:231`,交互模式(`BuildAndRunAsync`)每轮 submission 都在用,唯独**非交互一次性脚本路径** `RunScriptAsync` 直接 `script.RunAsync`,把编译和执行焊死在一起。我们缺的不是机制,是一个开关。

## Agenda

* [Proposal: vbi 脚本模式诊断检查模式 `/check`](#proposal-vbi-脚本模式诊断检查模式-check)

## Proposal: vbi 脚本模式诊断检查模式 `/check`

_Related: [`../proposals/proposal-vbi-script-diag-mode.md`](../proposals/proposal-vbi-script-diag-mode.md);姊妹提案 `../proposals/proposal-script-optimization-level.md`(同为脚本模式加开关,勘误教训:脚本开关必须放脚本专属分支)_

### 场景与缺口

`vbi foo.vbx` 要么真跑(带副作用),要么只在出错时报诊断;**成功路径的警告被完全吞掉**——`RunScriptAsync` 的 `:209` 直接 `return (await script.RunAsync(...)).ReturnValue;`,没有任何诊断输出,`ReportDiagnostics` 只在 `catch (CompilationErrorException)`(`:212-216`)里出现。跑一个未使用变量的脚本,`vbi` 打印 `RAN OK` 就 exit 0,BC42104 一个字节都看不见。想"只看不跑",现有三条路没一条对得上:脚本执行焊死运行、交互模式逐提交且 `DisplayDiagnostics` 截断 5 条(`CommandLineRunner.cs:380`)、编译模式要落盘程序集(`Vbi.Compile.vb:32-83`)。

### 翻源码:积木早就在

我们翻过源码,结论收敛得很干净:`Script.Compile()`(`Script.cs:231` → `CommonCompile` `:332-346`)就是"编译不运行拿诊断"的 API——成功返回仅警告、失败 catch `CompilationErrorException` 返回错误+警告、不执行不落盘。交互模式 `BuildAndRunAsync`(`:296-320`)每一轮都在干我们想要的事:`newScript.Compile()` → `DisplayDiagnostics` → `HasAnyErrors()` 有错不跑。非交互路径只是没用它。

这背后的设计哲学值得点破:Roslyn 从第一天起就把 `Compilation` 的诊断与 `Emit` 解耦——`GetDiagnostics()` 独立于 `Emit()`,IDE 波浪线就是"无发射地持续出诊断"的日常化。`/check` 不是发明新机制,是把 Roslyn **已经算出来、却死在 `RunScriptAsync` 路径里**的诊断流接到 CLI 上。直接先例是同仓库已落地的 `/optimize` 脚本开关(`spec-script-optimization-level.md` 记录的勘误教训:脚本开关必须放脚本专属分支 `VisualBasicCommandLineParser.vb:474-542`,不是 vbc 分支),`/check` 是这一模式的第二次应用,风险已探明。

### 候选方案

**PROPOSAL A — `/check`(采用)。** 脚本专属分支加 `Case "check"`,`RunScriptAsync` 换 `script.Compile()` 分支。改动 ~4 触点,全延申既有机制。命名理由见下节——在"消除 AI 幻觉"标准下是唯一先验吻合的选项。

**PROPOSAL B — `/diag`(原提案,否决)。** 交付物命名("给我诊断报告")。但 `msbuild /diag` 与 `dotnet build -v:diag` 已把 "diag" 焊死成"诊断级/最详细构建日志"——含义是**"跑起来并输出详细日志"**,与本模式"不执行"**语义相反**。对 AI 工具链,这是会引发**相反行为预期**的真实幻觉源,比重议 `/check` 的联想顾虑严重得多。否决。

**PROPOSAL C — `/norun` / `/noemit`。** `tsc --noEmit` 的直接类比,按"抑制的动作"命名。可读性差,不采用。

**PROPOSAL D — `/analysis`。** 误导——本模式只出编译器诊断、不含分析器;且与 `/analyzer:` 概念纠缠。否决。

**PROPOSAL E — 什么都不做。** 警告永远不可见于成功路径。缺口不解决。

### 命名裁决:`/check` 与 AI 幻觉

本模式的主要消费者是 AI 工具链,所以"消除 AI 幻觉"是**首要命名标准**——AI 对裸开关的解读来自训练数据先验,不会先读帮助文本。

- **`/check` 的 AI 先验干净且吻合**:`cargo check`(类型检查不产二进制)、`node --check`(语法检查不运行)、`biome check`(检查不产出)——最强先验就是"验证性、无产物、不执行",与编译门语义完全一致。我们抽样了一个外部 LLM 对 `vbi foo.vbx /check` 的解读,其**最高概率猜测正是"只解析编译检查、不执行(dry-run)"**,`/checked`(溢出)仅作次选——先验方向正确。
- **`/diag` 的 AI 先验方向相反**:msbuild `/diag` / `dotnet -v:diag` = "最详细构建日志",AI 会据此预期"跑起来并打印详细日志"——恰好是本模式的反面。
- **零技术冲突**:VB 解析器是 `Select Case name` 精确匹配(`VisualBasicCommandLineParser.vb:212`),现无 `Case "check"`。`/check` 与 `removeintchecks*`(`:369,:378`)、`checksumalgorithm`(`:353`)不撞。
- **`/removeintchecks` 联想是次要顾虑,不是否决理由**:语义域完全不相交(运行时溢出行为开关 `checkOverflow` vs 编译门)、极性不构成误导(`/removeintchecks` 是"去掉检查",无人把裸 `/check` 读成"去掉整数检查")、帮助文本一句消歧。"check=溢出"在 .NET CLI 词汇表里有实底(`csc /checked`,`CSharpCommandLineParser.cs:410-425`),但它是可读性小噪音,不是会踩的坑。
- **`Option Checked` 不是准入条件**:源级 `Option` 语句目前只列 Explicit/Strict/Compare/Infer(`source-files-and-namespaces.md:63-69`),`:81` 明文溢出检查"只能通过编译环境指定";2017-08-23 LDM 留过一句 dove-tail 线索(`vbldm-notes-2017.08.23.md:29`)。"源级 Option 与 CLI 开关是两个命名空间、互不干扰"的结构论成立、方向合理,但它是**未来语言特性**,与今天 `vbi /check foo.vbx` 无任何语法碰撞——`/check` 的可接受性不依赖它。若未来落地,文档补一句"两命名空间"说明即可。
- **VB 基因**:meaningful words(`introduction.md:3`)轴 `/check`(完整动词)得分高于 `/diag`(截断缩写);且 `/check` 延续提案自身生态对照表的动词家族(`proposal:32-37`)。

### 权衡:Q&A

- **`Check` 标志放哪?这里有一个必须采纳的设计修正。** 提案原说放 `VisualBasicCommandLineArguments`,但执行分支在**共享代码** `CommandLineRunner.cs`,那里 `_compiler.Arguments` 是基类 `CommandLineArguments`——`/check` 要让共享 `RunScriptAsync` 读到就得强转,不优雅。先例摆着:`InteractiveMode` 就放在**基类**(`CommandLineArguments.cs:31`),脚本解析器只置位(`:1549`),共享 runner 直接读(`:144`)。`/check` 是同一类"共享 runner 要读的模式开关",照抄这个模式:**基类加 `Check`(默认 false),VB 脚本解析器置位,C# 侧不置位天然不受影响**。这比强转干净,也更贴"共享宿主代码不掺语言专属 cast"的既有纪律。
- **`vbi /check`(无文件)会怎样?这里有一个必须落实的坑。** `VisualBasicCommandLineParser.vb:1542` 有 `interactiveMode = interactiveMode Or (IsScriptCommandLineParser AndAlso sourceFiles.Count = 0)`——无源文件就强制交互,`/check` 会被当空气;`/check /i foo.vbx` 同理,`/i` 置位 `InteractiveMode`,runner 在 `CommandLineRunner.cs:144-152` 走 `RunInteractiveLoopAsync`。要兑现"无文件报错、`/check` 优先",必须在这两个点**写死成规则**:解析器层面 `check` 置位时抑制 `:1542` 的自动交互并在无文件时产出错误诊断,runner 层面 `Check` 置位时无视 `InteractiveMode`。
- **"完整诊断"的说法过实。** `CommonCompile` 成功路径 `Where(d => d.Severity == Warning)`(`Script.cs:340`)、失败路径 `Error or Warning`(`:344`)把 Info/Hidden 滤掉;`ReportDiagnostics` 本身也不打印 Hidden(`CommonCompiler.cs:540-544`)。所以 `/check` 输出是**错误+全部警告**。对编译门这语义正确(Info/Hidden 不阻塞),但文档措辞要准。
- **"半成品论"站得住吗?我们不这么看。** 提案 Drawbacks 自称对"严格编译门"是半成品,因为不支持 `/warnaserror`。但**不带 `/warnaserror` 的 `/check`,严格度恰好等于默认 `dotnet build`/`vbc` 门**——错误 fail、警告全显示;真正的严格度控制交给文件内 `Option Strict On`(实证 BC30574/BC30512),这正是 VB 的 `Option` 模型(`vblang\spec\source-files-and-namespaces.md:72,81`:Option 语句按文件生效、环境只提供缺省)。把警告升级为错误是另一个职责面,归入未来分析器面没错。
- **`/check` + `.vb` 呢?这里是文档义务。** `IsCompileInvocation`(`Vbi.Compile.vb:32-59`)只要看到 `.vb` 就进编译模式,`/check` 会落 BC2007 警告、然后照常发射——"想只检查结果还是落了盘"。这自纠正(警告告诉你 `/check` 没被认),但文档要写明"`/check` 是脚本模式开关,配 `.vb` 不生效"。
- **MSYS 裸开关怎么办?我们一致认为应统一文档化。** `/check` 是第 4 个受 git-bash/MSYS 路径转换影响的裸开关(前三个 `/i`、`/nostdlib`、`/optimize+`,实证 `C:/Program Files/Git/<name>` → BC2001)。处理正确是零代码 + `MSYS2_ARG_CONV_EXCL='*'`,但建议在 vbi 帮助文本里**统一**写一条裸开关族说明,而不是每个开关各注释一次。
- **退出码?与 .NET 惯例一致。** 0 = 无编译错误(可有警告),1 = 有错误。且 `CommonCompiler.ReportDiagnostics` 恰好返回 `hasErrors`(`:515-583`),实现可以直接 `return _compiler.ReportDiagnostics(...) ? Failed : Succeeded`,一行搞定。
- **`#load` 链呢?这里补充一个边界。** `CommonCompile` 经 `GetPrecedingExecutors`(`Script.cs:337-338`)会为前置脚本也构建 executor → 内存 emit。"零编译"措辞在带 `#load` 时须精确为"无落盘无执行"。

### RESOLUTION:

1. **采纳 `/check`**：脚本模式新增诊断检查开关,只编译输出错误+全部警告、不执行不落盘。**命名定案**:以"消除 AI 幻觉"为首要标准,`/check` 的 AI 先验(只检查不产产物)与语义吻合,`/diag` 的 AI 先验(msbuild `/diag` 详细日志)方向相反故否决。**帮助文本必须写**:`/check` = 只编译、不运行、不产出文件;并区分与 `/removeintchecks`/`csc /checked`(运行时溢出)无关。
2. **`Check` 标志放基类 `CommandLineArguments`(仿 `InteractiveMode` 先例 `CommandLineArguments.cs:31`)**：VB 脚本解析器置位,共享 runner 直接读,不掺语言专属 cast。
3. **互斥语义落实成规则**：解析器层面 `check` 置位抑制 `:1542` 自动交互、无文件产出错误;runner 层面 `Check` 置位无视 `InteractiveMode`(`:144-152`)。
4. **退出码契约**：0 = 无编译错误(可有警告),1 = 有错误;实现用 `ReportDiagnostics` 返回值。
5. **输出措辞**：错误+全部警告(Info/Hidden 被滤),文档不写"完整诊断"。
6. **严重度配置 v1 不带**：`/check` 严格度即默认 `dotnet build`/`vbc` 门;`/warnaserror`/`/nowarn` 留未来分析器面(oxlint 类比)。
7. **`/check` 配 `.vb` 不生效**：文档写明(`IsCompileInvocation` 会把 `.vb` 带进编译模式,`/check` 落 BC2007)。
8. **命名边界**：文档一行区分 `/check`(编译门)与 `/removeintchecks` / `csc /checked`(运行时溢出)是两回事;`Option Checked`(源级 Option 表达溢出)为未来语言特性、非本提案准入条件,若落地补"源级 Option 与 CLI 开关两命名空间互不干扰"说明。
9. **MSYS**：帮助文本统一写一条裸开关族说明(`/i`/`/nostdlib`/`/optimize+`/`/check`),要求 `MSYS2_ARG_CONV_EXCL='*'`,零代码。
10. **内存 emit 接受**：`Script.Compile()` 的 executor 构建是脚本引擎标准路径,措辞统一"无落盘无执行";`#load` 链前置脚本同样内存 emit,不阻塞。
11. **分析器/规则诊断独立立项**：`/check` 是编译器诊断(tsc 类比);fxcop 级规则诊断(oxlint 类比)需 `CompilationWithAnalyzers` + 脚本路径分析器管线,单独提案。
12. **Follow-up（不阻塞）**：普通执行路径 `vbi foo.vbx` 成功路径同样吞警告(`CommandLineRunner.cs:209`)——把"普通执行也先 `Compile()` 显示警告再 `RunAsync`"单独立项,注意会让既有脚本 stderr 多出警告、属行为变化需谨慎。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——全部源码声称逐条核实属实、无 Suspect;命名裁决以"消除 AI 幻觉"为首要标准、有外部 LLM 实测佐证(AI 对 `/check` 的头号猜测即本模式语义);机制纯增量、低仪式、零默认行为变化、复用既有积木、不引入第二种做事方式、严格度由文件 `Option Strict` 控制(教科书级贴 VB 设计思想);与 Roslyn「编译≠发射」理念同向、对 M1–M8 零新摩擦、命中 M5。两处设计实质修正(基类 `Check`、互斥落实成规则)与命名裁决已并入 RESOLUTION。进入实现规划。
