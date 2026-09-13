# `.vbx` 顶层变量默认本地变量 / Top-Level Locals in `.vbx` Scripts

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

**提案内容**：`.vbx` 脚本的**顶层变量**（顶层 `Dim`）默认是**本地变量（local）**，而不是今天的**脚本类字段**。`.vbx` 的策略因此向 **C# top-level code（C# 9 顶层语句）** 靠拢：那里的顶层变量就是 `Program.Main` 体内的局部变量（**实锤**：`Compilers\CSharp\Portable\Binder\SimpleProgramBinder.cs:26-39` 的 `BuildLocals` 把顶层语句里的变量收成 `LocalSymbol`）。

**边界一（与已归档姊妹提案划清界限）**：本提案**不涉及**「顶层成员默认 `Shared`」的**默认值翻转**。姊妹提案 `inactive\proposal-top-level-implicit-shared.md` 改的是**已成为成员的顶层 `Dim`/`Sub`/`Function` 的 `Shared` 默认值**（把 `spec-scripting-dialect.md:60` 的后半句反过来）；本提案改的是**另一个轴**——**顶层变量是不是字段（成员）**。若顶层 `Dim` 是局部，则「它是不是 `Shared`」这个问题**根本不成立**。两条提案在结论上不相交：姊妹提案**保留**「顶层变量是 `SourceMemberFieldSymbol`」这一前提（它只是把该成员的默认共享性翻过来），本提案**推翻**该前提。请勿以姊妹提案已被判 `Table` 为由拒本提案；也**不得**把本提案读成姊妹提案的变体。

**边界二（能力面，先钉死防误读）**：本提案**只改「顶层变量」这一类**。`WithEvents` / `Property` / `Event` 一类**必须落在元数据成员上**的声明**不是变量**，它们的落点另行裁定（作者的表述是「仍用共享字段存储」，本提案把它标为**作者主张、由调查核实**，见 §Motivation 与 §3 待回填项 T3）。

**边界三（作者明确接受的差异）**：**交互 shell（REPL）保持今天的默认——共享字段**。作者的判断是 REPL 是**多重 submission**，跨 submission 的状态保留必须落在字段上。**作者明确接受 `.vbx` 与 REPL 在这条轴上的分叉。**

> **本文件状态**：机制章节（§3）**待调查回填**。同方向有一份并行调查在跑，产物落在 `tmp\investigations\vbx-top-level-locals\submission-wall.md`；**撰写时该文件尚不存在**（已核对：目录 `tmp\investigations\` 不存在、`tmp` 全树无 `vbx-top-level-locals`/`submission-wall` 命中）。故 §3 按纪律写成**显式待回填标记**，逐项列出待回填项，**不猜**。Summary / Motivation / Drawbacks / Alternatives / 规范影响面 / Unresolved 六处**不依赖调查的部分已写实**。

## Motivation
[motivation]: #motivation

### M1. 与 C# 的哪一张脸对齐（**实锤**）

`spec-scripting-dialect.md:26` 把 C# 拆成**三张脸**并逐字要求不得混同（`:300` 逐字「the answers must not be conflated」）：**脚本面**（`.csx` / `Script<T>`）、**文件执行面**（C# 9 顶层语句）、**交互面**（`csi`）。三张脸对「顶层变量是什么」给出的是**两个不同答案**：

| C# 脸 | 顶层变量 | 锚点 | 证据等级 |
|---|---|---|---|
| 脚本面 `.csx` | 提交类的**字段** | `spec:310` 逐字「a field of the submission class, preserved across submissions」 | **实锤**（规范在册） |
| 文件执行面（C# 9 顶层语句） | `Main` 体内的**局部变量** | `SimpleProgramBinder.cs:26-39` `BuildLocals` 收 `LocalSymbol`；`DeclarationTreeBuilder.cs:142` 的 `acceptSimpleProgram` | **实锤**（源码逐行） |
| 交互面 `csi` | 提交类字段（与脚本面同形） | 同脚本面 | **实锤**（规范在册） |

`.vbx` 今天走的是**脚本面**：`vbi script.vbx` 经 `CommandLineRunner.cs:238-269` 的 `RunScriptAsync` → `Script.CreateInitialScript<int>`（`:243`）→ `VisualBasicScriptCompiler.CreateSubmission`（`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:164`）→ `VisualBasicCompilation.CreateScriptCompilation`（`:208`），而后者**恒传 `isSubmission:=True`**（`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:388`）。所以 `.vbx` 与 REPL 今天在编译器层面是**同一个东西**。本提案的动机就是：**`.vbx` 是「文件执行」，它的顶层变量按 M1 的第一列应当是局部，而不是照抄脚本面的字段模型。**

这条动机直接落在 `decisions.md` **D5**（基础功能的落地细节以 C# / csi 实现为设计蓝本，见 D5 正文）上，而且它与姊妹提案的处境**相反**：姊妹提案要在 C# 有显式反向规则的地方主动分叉（见 `meetings\inactive\meeting-top-level-implicit-shared.md` 的 RESOLUTION 3/5），本提案是**朝着 C# 的一张现成脸收敛**。D5 的落地约束仍要求说明「为什么 VB 必须分叉」——本提案需要分叉的**只有 REPL 那一半**（见 M4）。

### M2. 局部变量解锁今天被「字段」挡掉的能力（**实锤**）

byref-like 类型（`Span(Of T)` / `ReadOnlySpan(Of T)` 等）的检查**只挂在字段上**，且**没有 `Shared` 分支**：

- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb:140-145` 逐字：`Else` 分支里 `Dim restrictedType As TypeSymbol = Nothing` / `If varType.IsRefLikeOrAllowsRefLikeTypeOrArrayType(restrictedType) Then` / `binder.ReportDiagnostic(..., ERRID.ERR_RestrictedType1, restrictedType)`。**整个判据不看 `IsShared`**，只看「是不是字段」。
- 这正是 `spec\spec-byref-like-safety.md:107` 的明文理由：顶层声明 byref-like 是错误，**因为它会成为脚本类的字段**；`:237` 把实例字段与静态字段并列。

⇒ 顶层变量一旦是局部，**「byref-like 不能是字段」这条就不再挡住顶层 `Span` 声明**。C# 侧的两模式分野已被实测确认（**实锤**，`meetings\inactive\meeting-top-level-implicit-shared.md` §9 与「并入基线的上一轮实测」）：C# 顶层语句模式三条 `Span` 用法全 `EXITCODE=0`，csx 同形状报 **CS8345**。本提案把 `.vbx` 从 csx 那一列移到顶层语句那一列。

**降低断言强度**：局部也不等于无条件放行——`<Initialize>` 是异步方法（`SynthesizedInteractiveInitializerMethod.vb:51-55` 逐字 `IsAsync` 返回 `True`），异步方法里的 byref-like 局部**跨 `await` 仍受限制**。同族里「`<Initialize>` 恒 async 会双重挡住 ref struct」这条**已被实测推翻**（CS4007 只在跨 `await` 时发）⇒ 本轴的正收益是**推测**（机制链已核，`.vbx` 形态未跑），不是实锤。

### M3. 作者的技术判断（**作者主张，待调查核实**）

以下为**作者原话的方向性判断**，本提案**照录并标为作者主张**，其成立与否**由并行调查核实**（§3 待回填项 T1/T2），本文件不替它背书：

> 一个方法需要一个 maxstack；如果多重 submission 用 locals 就打破了 maxstack，并且把之前方法的 stack 像是 unscoped ref struct 那样逃逸到了新的 submission 方法，submission 状态机就变成了 stack-only，机制不好设计，很容易撞到坑。当然，如果能设计出 submission 也用 locals 的内存安全方案，也值得讨论。

**可独立核实的核心**（这部分不依赖作者的框架，是**实锤**）：局部变量的生存期止于**声明它的方法的返回**；而「状态跨 submission 保留」是提交模型的**成文要求**（`spec:22` 逐字「State is preserved across submissions because a top-level `Dim` is a field, not a local.」；`spec:131` 的 State preservation 整节）。**⇒ 「跨 submission 保留状态 ⇒ 变量必须是字段」这一步是可从规范逐字读出的**；作者给出的 `maxstack` / 「stack 逃逸」是**解释该结论的机制语言**，本提案不把它当承重论据。作者同时开出「若 submission 也能用 locals 的安全方案出现，值得讨论」这一口子——列入 Unresolved。

### M4. REPL 为什么保持字段（**实锤的规范依据 + 作者决定**）

`.`vbx` 是**单次执行**（`CommandLineRunner.cs:238-269` 每次新建 `Script`、只 `RunAsync` 一次），REPL 是**提交链**（`:271-294` 起，续提交走 `RunFromAsync`）。**规范依据**：`spec:22`/`:131` 把「状态跨 submission 保留」写成提交模型的成文契约，而该契约的载体就是字段。⇒ **REPL 保持字段是规范驱动的，不是口味问题**；`.vbx` 没有下一个 submission，所以它**不需要**这条契约，也就**不需要**为此付出「顶层变量必须是字段」的代价。作者接受这一分叉。

### M5. 需要论证的义务（D5，**待会议裁量**）

D5 明文要求：参照 C# 时**要说明「为什么 VB 必须分叉」**，而「C# 这么做」本身不是理由。本提案的形态是：**在 `.vbx` 半边与 C# 收敛（不需要分叉论证），在 REPL 半边与 C# 的脚本面分叉（需要论证）**。REPL 半边的分叉理由是 M4 给出的**成文契约**（`spec:22`/`:131`），而不是「VBScript 老用户习惯」一类无锚点类比——`meetings\inactive\meeting-top-level-implicit-shared.md` 已判后者「无仓内锚点、不作为论据」，本提案不重蹈。

## Detailed design
[design]: #detailed-design

> **证据等级标注**：阶梯为 未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑。本节的源码锚点均在工作树逐行复核（**已检查**）；标「实锤」的另有实测或规范逐字。**本阶段未改编译器、未加单元测试**，故不出现「已采纳」「有结果支撑」。
>
> **本节分两半**：§1、§2 是**可写实**的现状基线与边界；§3 是**待调查回填的机制核心**——同方向调查 `tmp\investigations\vbx-top-level-locals\submission-wall.md` 撰写时**尚不存在**，故按纪律留显式回填标记。

### 1. 本提案改什么 / 不改什么（**实锤**）

**改（一句话）**：`.vbx` 顶层 `Dim` 的**存储类别**从「脚本类字段」改成「`<Initialize>` 的局部变量」。

**不改**：

- **容器不变**：仍是脚本类（`DeclarationKind.Submission` / `TypeKind.Submission`）。本提案**不碰** `TypeKind`/`DeclarationKind`/`SourceTypeFlags`，**不碰**姊妹提案 §1 列出的那 27 + 6 处派发点，也**不碰**已被两次否决的「容器改 `Module`」面。
- **入口点形状不变**：`<Initialize>`（async，`SynthesizedInteractiveInitializerMethod.vb:51-55`）与 `<Factory>` 不动；`spec:169` 的「拼写不是公开契约」照旧。
- **提交链不变**：`PreviousScriptCompilation` 与跨提交查找机制不动（REPL 半边照旧使用）。
- **顶层语句仍进初始化器**：`SourceMemberContainerTypeSymbol.vb:2633` 一带把顶层语句无条件加进实例桶，这条路径**仍然在用**——变的只是同一序列里**字段初始化器**那一半是怎么来的。
- **`WithEvents`/`Property`/`Event` 的成员资格不变**：它们**照样是成员**（详见 §3 待回填项 T3）。
- **不翻转 `Shared` 默认值**：见 Summary 边界一。
- **不改 `Sub`/`Function` 的成员资格**（除非 §3 的裁决改变它）：见 §3 待回填项 T2。

### 2. 现状基线（**实锤**，供回填时对照）

| # | 事实 | 锚点 | 等级 |
|---|---|---|---|
| B1 | 顶层 `Dim` **在语法上就是 `FieldDeclaration`**——`SyntaxKind.CompilationUnit` 在 `isFieldDeclaration = True` 的 `Select Case` 里 | `Compilers\VisualBasic\Portable\Parser\Parser.vb:2092-2111` | **实锤**（源码逐行） |
| B2 | 因此顶层 `Dim` 是脚本类的**字段**，有实例状态、跨提交保留，且**不是**局部 | `spec-scripting-dialect.md:58` 逐字「It is not a local variable, and it is not scoped to a method body.」；`:22` 逐字「a top-level `Dim` is a field, not a local」 | **实锤**（规范逐字） |
| B3 | 顶层 `Sub`/`Function` 是脚本类的**实例成员**，其**方法体可以直接读写**顶层 `Dim` | `spec-scripting-dialect.md:60` 逐字「its body can read and write the top-level `Dim` variables directly」 | **实锤**（规范逐字） |
| B4 | `.vbx` 与 REPL **走同一个编译入口**，且 `CreateScriptCompilation` **恒传 `isSubmission:=True`** | `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:164`/`:208`；`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:388` | **实锤**（源码逐行） |
| B5 | 两者**唯一的入参差异是 `returnType`**：`.vbx` = `int`、REPL = `object`；**没有任何入参的含义是「顶层变量是局部还是字段」** | `CommandLineRunner.cs:243`（`CreateInitialScript<int>`）vs `:291`（`CreateInitialScript<object>`）；`Scripting\Core\Script.cs:323`（`ReturnType => typeof(T)`）；`VisualBasicScriptCompiler.vb:231` 把 `script.ReturnType` 传进 `CreateScriptCompilation` | **实锤**（源码逐行） |
| B6 | 顶层字段初始化器与顶层语句**同住实例桶、按源码位置排序** | `SourceMemberContainerTypeSymbol.vb:1571-1573` 逐字注释 `' initializers should be added in syntax order` + 两条严格递增断言 | **实锤**（源码逐行） |
| B7 | 分桶只看 `IsShared`：`SharedConstructor ⇒ 静态桶`、`Constructor / IsScriptInitializer ⇒ 实例桶` | `Compilers\VisualBasic\Portable\Compilation\MethodCompiler.vb:695-699` | **实锤**（源码逐行） |
| B8 | **VB 没有局部函数**：`LocalFunctionStatementSyntax` 在 VB 全树**零命中**，`LocalFunction` 在 `Compilers\VisualBasic\Portable\` 源码里**唯一命中**是 `Analysis\FlowAnalysis\VisualBasicDataFlowAnalysis.vb:289` 的 `UsedLocalFunctions` 覆写 | `grep`（排除 `bin`/`obj`） | **实锤**（`grep` 零命中） |
| B9 | **VB 没有「文件式程序 / 顶层语句」模式**（`SimpleProgram` 在 VB 全树**零命中**），C# 侧由 `acceptSimpleProgram` 提供 | `grep SimpleProgram Compilers\VisualBasic\` 零命中；`Compilers\CSharp\Portable\Declarations\DeclarationTreeBuilder.cs:142` | **实锤**（`grep` + 源码逐行） |
| B10 | 宿主对象字段是**实例**字段 | `Compilers\VisualBasic\Portable\Lowering\SynthesizedSubmissionFields.vb:57` 逐字 `isShared:=False`（同行 `isReadOnly:=True`） | **实锤**（源码逐行） |

### 3. 待回填：机制核心（**调查进行中，本文件不猜**）

> **回填来源**：`tmp\investigations\vbx-top-level-locals\submission-wall.md`（撰写时尚不存在）。
> **回填纪律**：本节每一项回填后须附 `文件:行号` 或实测；**未回填前不得以任何形式把猜测写成设计**。

**T1 — `.vbx` 今天为什么是 submission、能不能不走 submission。**
今天**已实锤**：`.vbx` 走 `RunScriptAsync` → `CreateSubmission` → `CreateScriptCompilation(isSubmission:=True)`（B4），所以它**是** submission，且**没有**「非 submission 脚本编译」的宿主入口在用。**待回填**：`.vbx` 若保留 `isSubmission:=True`，顶层变量可否只是 `<Initialize>` 的局部而**不改编译种类**？还是必须换到非 submission 的脚本编译（走 `<Main>` 而非 `<Factory>`，`spec:161-167`）？两条路各自要动哪些宿主/编译器入参？**这一项决定本提案的施工面量级。**

**T2 — 【最关键】顶层 `Sub`/`Function` 怎么办。**
现状：它们是脚本类**实例成员**，方法体可以直接读写顶层 `Dim`（B3）。**若顶层 `Dim` 变成 `<Initialize>` 的局部，则一个成员方法看不见另一个方法的局部**——这不是风格问题，是名字查找的结构问题。且 VB **没有局部函数**（B8），所以「把顶层 `Sub`/`Function` 降成局部函数从而捕获局部变量」这条路**在本语言里不存在现成载体**。**待回填**：三条候选各自的可行性、代价与用户可见后果——
  (a) 顶层 `Sub`/`Function` **保持成员**，代价是**它们不再能访问顶层变量**（能力删除，需明写）；
  (b) 造一个**捕获机制**（闭包 / display class），代价是回到「堆上状态」，与「局部」的动机相冲；
  (c) 顶层 `Sub`/`Function` 一并**改变成员资格**或**新造语法载体**（VB 无局部函数 ⇒ 属新机制）。
**在 T2 落定之前，本提案的 Detailed design 不能算写完。**

**T3 — `WithEvents` / `Event` / `Property` 留字段的落点（含成本）。**
作者表述为「仍在元数据上用**共享字段**存储」。**待回填**：①这三者今天在顶层**就已经不通、且与 `Shared` 无关**（`meetings\inactive\meeting-top-level-implicit-shared.md` 复会第七议程已实锤：`Property` 顶掉其后顶层语句区报 BC30188，`Event`/`WithEvents` 发射期断言终止 `EXITCODE=35`，据 issue 07）——所以本提案**不是**使它们变坏，而是**继续把它们留在今天就已损坏的状态里**，须显式裁一次「修 / 给诊断 / 明文写出」；②`WithEvents` 与 `Dim` **同形同路、照样产字段**（`SourceMemberFieldSymbol.vb:573-574`/`:643-656`；`spec:190` 把它叫 "field"），所以「`WithEvents` 留成员、`Dim` 变局部」会在**同一张映射表**里造出两条相邻但语义类别不同的行；③作者的「**共享**字段」这个措辞**须核实**：`WithEvents` 后备字段的 `IsShared` 是从被修饰的属性**传播**来的（`SourceWithEventsBackingFieldSymbol.vb:32`，见 `tmp\vortex-logs\top-level-implicit-shared\0-main-scope.md` 的 10 处赋值点表），「共享」不是它的固有属性。**这三条都要如实定价，不得只写「仍在元数据上用共享字段存储」一笔带过。**

**T4 — `#Load` 多树。**
今天多棵树合成**一个**提交（`VisualBasicScriptCompiler.vb:185-199` 的 `CollectLoadTrees` 深度优先、加载树在前主树在后）。**待回填**：加载树与主树**各自的顶层 `Dim`** 在「局部」模型下如何共存——是同一个初始化器方法体内的一段连续局部声明，还是每条树一个作用域？跨树引用（树 A 声明、树 B 读取）今天靠字段成立，改局部后**是否仍然成立**？被 `#Load` 的树里的顶层声明，与主树的 `Return` 的关系（`#Load` 的 `Return` 是整提交退出码）。

**T5 — `Return` / 末尾表达式。**
今天 `Return` 是 `<Initialize>` 的 return、尾部表达式按 result type 规则产出脚本结果（`spec:143-158`）。**待回填**：局部模型下这两条是否原样成立（预期不变——`Return` 本来就是 `<Initialize>` 的），以及**末尾表达式的「最终语句」判定**与局部声明序的交互。

**T6 — `Static` 局部与顶层过程。**
顶层过程体内的 `Static` 局部今天合法且 per-instance（`SynthesizedStaticLocalBackingField.vb:37` 的 `isShared:=implicitlyDefinedBy.ContainingSymbol.IsShared`，实测见复会第一议程 P2/P4）。**待回填**：本提案若同时改动顶层过程的成员资格（T2），这一格会被**连带牵动**——须给出「连带动还是不动」的结论。

### 4. 意图示例（**机制待回填**，仅供读者看清方向）

```vbnet
' 意图：.vbx 的顶层 Dim 是 <Initialize> 的局部变量，而不是脚本类字段
Dim counter As Integer = 0
Dim items As New List(Of Integer)

Sub Bump()                    ' 今天：脚本类的实例成员，能直接读写 counter
    counter += 1              ' ← 本提案下 counter 是 <Initialize> 的局部：
                              '   这一行怎么解释？【T2，待回填】
End Sub

Bump()                        ' 顶层语句：<Initialize> 的语句
Console.WriteLine(counter)    ' 期望 1（今天也是 1）
```

**读者注意**：这段代码里**唯一有争议的是 `Bump` 体**。顶层语句那一侧（声明与使用同处一个方法体）在局部模型下是**平凡成立**的；`Bump` 那一侧正是 T2 要回答的问题。**在 T2 回填前，本示例不作为规范承诺。**

### 5. 规范影响面（**部分写实**）

**已可确定的必改句**（`.vbx` 半边）——行号按 `spec\spec-scripting-dialect.md` 的行首单元格（照 `pitfalls.md` P-018 的纪律）：

| 行 | 现文要点 | 为什么必改 |
|---|---|---|
| `:15` | 映射表 `Dim x = …` → 「a **field** of the script class」 | 本提案的核心翻转点；须与 `:16`（`Sub`/`Function` 行）**成对**改写，因为 T2 决定 `:16` 怎么改 |
| `:22` | 「State is preserved across submissions because a top-level `Dim` is a field, not a local.」 | 对 `.vbx` 不再成立；对 REPL **仍然成立** ⇒ 句子要**按编译种类分叉**，不能整句删 |
| `:35` | 「The four top-level forms keep their ordinary spelling and meaning; only their container changes.」 | 顶层 `Dim` 的 meaning 变了（不再是成员）⇒ 需具名例外或改写 |
| `:58` | 「**Top-level `Dim` is a field.** … It is not a local variable, and it is not scoped to a method body.」 | **整段被推翻**（`.vbx` 半边）；REPL 半边保留 |
| `:60` | 「**Top-level `Sub` and `Function` are instance members …** its body can read and write the top-level `Dim` variables directly」 | 后半句是本提案**最直接的受损句** ⇒ 与 T2 同题，**必须等 T2** |
| `:64` | 「The synthesized initializer body is the sequence of **field initializers** and global statements in source order」 | 「field initializers」这一半在 `.vbx` 上不再成立（B6 的语法序机制还在，但成员种类换了） |
| `:66` | Scope：顶层声明的作用域是「top-level code of the compilation unit and the later submissions in the chain」 | `.vbx` 无「later submissions」；且局部的作用域是**方法体**，与嵌套类型的关系完全不同 |
| `:273` / `:277` | 「The model is a containerization, not a new declaration system. Every top-level form keeps its ordinary spelling and its ordinary meaning」／「A program that is legal both as a script and as ordinary code has the same meaning in both」 | 本提案在 `:277` 上造出**反例**（同一段顶层 `Dim` 在 `.vbx` 是局部、在普通代码/REPL 是字段）⇒ 处置只能是**承诺保留 + 具名例外**（照 `spec:194-200` 的 `Imports` 偏离写法），**不得留一句已知为假的假全称句**（这条纪律由姊妹提案的复会第六议程确立，本轮适用） |
| `:310` | 对照表行「Top-level variable \| a field of the submission class, preserved across submissions \| a field of the script class, preserved across submissions」 | 须**拆成 `.vbx` 与 REPL 两行**，并如实注明与 C# 脚本面的分叉 |
| `:311` | 对照表行「Top-level method \| an instance member of the submission class, or a `static` member if declared `static` \| an instance member of the script class, or a `Shared` member if declared `Shared`」 | 与 T2 同题 |

**建议增补**：`:11-18` 的映射表要**加一列或加一行**区分 `.vbx` 与提交（今天该表对两者给同一映射，本提案之后不再能保持单一映射）；`:348`（`WithEvents` in a submission class 的边界句）与 T3 联动；`:351-361` 的 Testing 节要补 `.vbx` 与 REPL 的**分叉用例**。

**不动**：`:131`（State preservation，REPL 半边）、`:137`（`<Initialize>`）、`:163`/`:165`（`<Main>`/`<Factory>` 的形状——注意 `spec:169` 已声明这些拼写不是公开契约）、`:234`/`:243`（`Me` 限制）、`:300-304`（三张脸的划分）。

**另一份要同步的文件**：`proposals\proposal-scripting-dialect.md`（Active #15）与 `spec` 同步，改的是同一批句子；本提案**不**改 `spec`（只登记影响面）。

## Drawbacks
[drawbacks]: #drawbacks

- **【最关键】顶层 `Sub`/`Function` 的可见性有结构风险**（T2）。今天它们的方法体能直接读写顶层 `Dim`（B3，**实锤**），而**成员方法看不见另一个方法的局部**；VB **又没有局部函数**（B8，**实锤**）作为「降级后仍能捕获」的现成载体。⇒ 本提案在最好的情况下是**能力收窄**（顶层过程不能再碰顶层变量），在最坏的情况下需要一个**新机制**。**这一条不落定，本提案不能上会。**
- **同上，规范承诺要重写而非微调。** `spec:58`/`:60`/`:64` 三句是**成文承诺**，不是实现细节；本提案把它们推翻，处置必须是「承诺保留 + 具名例外」（照 `Imports` 偏离写法），而不是删句或留假句。
- **`WithEvents`/`Property`/`Event` 的成本要如实定价**（T3）。这三者**今天在顶层就已损坏**（与 `Shared` 无关，issue 07 族），本提案把它们**继续留在损坏状态**，同时在同一张映射表里造出「变量是局部、成员是成员」的**混合模型**。混合模型本身不是错，但它是**两种解释并存**——而 VB-like 的门槛对此不友好（`vblang\meetings\2018\vbldm-notes-2018.06.13.md:33-34` 逐字：「Visual Basic has a stance」／「our bar for expansion of the surface area - making a second way to do things - will be relatively high」；**这两句与兼容性无关，D6 管不到**）。
- **`.vbx` 与 REPL 分叉需要一个新的宿主入参。** 今天两条路径的编译入参**没有一项**表达「局部还是字段」（B5，**实锤**）。这意味着分叉**不是零成本**：要么加一个入参（并被 `Script<T>` 公开 API 的兼容面牵住——`Script<T>.RunAsync` 在 `Scripting\Core\PublicAPI.Shipped.txt` 是 **Shipped**，见姊妹提案复会第一议程代价 2 的锚点），要么让宿主走两条编译路径。**具体量级待回填**（T1）。
- **与 C# 的一致性只买到了一半。** `.vbx` 向 C# 顶层语句收敛（M1），但 C# 顶层语句的顶层变量是**局部**这一点**不是**因为「局部更好」，而是因为 C# 选了「去样板」场景（`LDM-2020-01-22.md:15-21` 的三个并列场景，会议已核）。⇒ 本提案不能声称「C# 这么做所以对」，只能声称「`.vbx` 是文件执行面，该面在 C# 的答案是局部」。D5 的义务论证落在 **REPL 那半边**（M4/M5）。
- **ref struct 的正收益是推测，不是实锤。** 「局部解锁 byref-like」的机制链已核（M2），但 `.vbx` 形态未跑；且 `<Initialize>` 是 async（**实锤**），跨 `await` 的 byref-like 局部仍受限。**不得把这半条写成已兑现的收益。**
- **收益侧薄。** 与姊妹提案同病：本提案买到的是**分类的整洁**（顶层变量不再是成员）与**byref-like 的潜在放开**，而用户今天**没有一条被压住的写法**会因此解锁——除非 T2 给出更好的答案，否则「顶层过程读不到顶层变量」是净损失。**这一点必须在回填后重新定价。**
- **D6 不构成障碍，但也不构成理由。** `decisions.md` **D6** 只解掉「兼容性」一条否决理由（只有 beta / 无 GA，实证见 D6 正文），其余理由（可行性、机制收益与代价、与 D5 的同形性、规范可表达性）**不受影响**，仍须逐条论证。

## Alternatives
[alternatives]: #alternatives

### A. 维持现状（顶层 `Dim` = 脚本类字段）——**当前的基线**

- 收益：零机制工作；`spec:58`/`:60`/`:64` 不动；`.vbx` 与 REPL 保持同形；byref-like 顶层声明**继续**报 BC31396（这是 `spec-byref-like-safety.md:107` 的既有定案，不是缺陷）。
- 代价：`.vbx` 继续照抄脚本面的存储模型，与 C# 文件执行面的局部模型不一致（M1）；顶层 `Span` 继续被挡。
- **注**：这是本提案**必须打败的对照**，而不是「什么都不做」。若 T2 找不到让人放心的答案，A 就是推荐路线。

### B. 本提案（`.vbx` 顶层变量 = 局部，REPL 保持字段）

见 Summary / Detailed design。**承重项全部在 §3（T1–T6）**——在这些回填之前，B 的机制部分**不存在**。

### C. 只把 byref-like 那一格单独解锁（不动存储类别）

- 内容：保留顶层 `Dim` 是字段，只放开「字段不能是 byref-like」对顶层脚本字段的限制。
- **为什么单列**：本提案的**收益**（M2）与**代价**（T2/T3）是两件事。若会议认为 T2 的代价不可接受而 M2 的收益值得拿，这是唯一能把两者分开的路线。
- **代价**：与 `spec-byref-like-safety.md:107`/`:237` 与 `meeting-byref-like-repl-safety.md` 的既有定案**正面冲突**（那两处的理由逐字是「**因为是字段**」）⇒ 要走这条路必须重新论证字段侧的安全性，属**独立的提案**，不在本提案范围内。此处仅登记，不主张。

### D. 三条都不改，改在别处（呈现层 / 诊断层）

- 若本提案的真实痛点是「顶层 `Span` 写不了」，可以在**诊断文案**上解释「顶层声明是字段，所以不能是 byref-like」，把成本从「能力」搬到「解释」（`spec-byref-like-safety.md:107` 已经把这条理由写出来了）。
- 若真实痛点是「分类不整洁」，那属于**规范叙述**问题，不需要改语言。

### E. 只改 `.vbx` 而不加宿主入参（把分叉藏在别处）

- 内容：用**已有的**入参承载分叉——今天唯一随路径变化的是 `returnType`（`.vbx` = `int`，REPL = `object`，B5）。用返回类型当「局部/字段」的开关。
- **为什么不推荐（推测，未核）**：`returnType` 是**结果类型**的语义（`spec:143-158` 整节按 result type 定义脚本结果），把它当存储类别的开关是**两个语义挤一个入参**，且 `Script<T>` 是公开 API（`Scripting\Core\PublicAPI.Shipped.txt`）⇒ 用户可用 `Script<int>` 跑 REPL、用 `Script<object>` 跑 `.vbx`，分叉立刻失效。**此处登记为反例，供 T1 排除。**

## Unresolved questions
[unresolved]: #unresolved-questions

1. **【阻塞】顶层 `Sub`/`Function` 的落点**（T2）。保持成员则**失去**对顶层变量的访问（能力删除），降级则需要 VB 没有的局部函数载体（B8），捕获机制则把状态搬回堆。**三选一之前本提案没有 Detailed design。**
2. **【阻塞】`.vbx` 是否仍是 submission**（T1）。保留 `isSubmission:=True` 而只改变量存储类别，还是必须换到非 submission 的脚本编译（`<Main>` 路径）？两条路的施工面量级差多少？
3. **【阻塞】分叉的宿主载体**（B5）。加一个新入参（被 `Script<T>` Shipped API 牵住）还是走两条编译路径？备选 E 已被登记为反例，但替代方案未定。
4. **`WithEvents`/`Event`/`Property` 的逐形式三态**（T3）：修 / 给诊断 / 明文写出。以及作者「共享字段」措辞是否准确（`WithEvents` 后备字段的 `isShared` 是**传播**来的，不是固有属性）。
5. **`#Load` 多树的跨树可见性**（T4）：树 A 声明的顶层变量，树 B 能否读到？今天靠字段成立，改局部后必须重新给规则。
6. **`Return` / 末尾表达式在局部模型下是否原样成立**（T5）。预期不变（`Return` 本来就是 `<Initialize>` 的），但**未核**。
7. **顶层 `Static` 局部是否被连带牵动**（T6）。若 T2 改变顶层过程的成员资格，`SynthesizedStaticLocalBackingField.vb:37` 的 `isShared` 入参会随之翻。
8. **REPL 半边的分叉是否要写进 spec 的具名偏离**。本提案在 `spec:22`/`:310` 上按编译种类分叉——「同一语言里两种编译种类的同一条顶层 `Dim` 语义不同」是否需要一条 LIKE `Imports` 的显式偏离小节？与 `spec:26` 的「三张脸」框架如何措辞一致？
9. **byref-like 收益的实际边界**（M2）。局部解锁了字段限制，但 `<Initialize>` 是 async；跨 `await` 的边界与本提案形态下的实际表现**未跑**。
10. **D5 的「为什么 VB 必须分叉」落点**。本提案把分叉收窄到 REPL 半边，理由是 `spec:22`/`:131` 的成文契约（M4）——该理由是否足以满足 D5 的落地约束，**不是本提案能自证的**。
11. **是否存在「submission 也用 locals」的安全方案**（作者开的口子，M3）。若存在，本提案的 REPL 分叉就不必要——但作者判断它「机制不好设计」，本提案不预判。

## 全称主张自检

> 纪律：全文每一个全称主张（无 / 都 / 任何 / 唯一 / 不可能 / 零命中）逐条给 `文件:行号` 或实测；举不出证据即降级。

| # | 主张 | 证据 | 判定 |
|---|---|---|---|
| 1 | `LocalFunctionStatementSyntax` 在 VB 全树**零命中** ⇒ VB 没有局部函数 | `grep -rn`（排除 `bin`/`obj`）零命中；`LocalFunction` 源代码**唯一命中** `VisualBasicDataFlowAnalysis.vb:289` | **实锤**（`grep`） |
| 2 | `SimpleProgram` 在 VB 全树**零命中** | `grep -rn SimpleProgram Compilers\VisualBasic\` 零命中；C# 侧 `DeclarationTreeBuilder.cs:142` 有 | **实锤**（`grep`） |
| 3 | byref-like 判据**没有** `Shared` 分支，**只看**是不是字段 | `SourceMemberFieldSymbol.vb:140-145`（`Else` 分支整块无 `IsShared`） | **实锤**（源码逐行） |
| 4 | `.vbx` 与 REPL 走**同一个**编译入口，`CreateScriptCompilation` **恒**传 `isSubmission:=True` | `VisualBasicScriptCompiler.vb:164`/`:208`；`VisualBasicCompilation.vb:388` | **实锤**（源码逐行） |
| 5 | 两条路径**没有一项**编译入参表达「局部还是字段」；**唯一**差异是 `returnType` | `CommandLineRunner.cs:243` vs `:291`；`Script.cs:323`；`VisualBasicScriptCompiler.vb:231` | **实锤**（源码逐行）；「没有别的差异」这一范围判断限于**已读过的那三处入参构造点**，**推测**其完备 |
| 6 | 顶层 `Dim` **在语法上就是** `FieldDeclaration` | `Parser.vb:2092-2111`（`SyntaxKind.CompilationUnit` 在列） | **实锤**（源码逐行） |
| 7 | 顶层字段初始化器与顶层语句**同住实例桶、按源码序** | `SourceMemberContainerTypeSymbol.vb:1571-1573`（含两条严格递增断言） | **实锤**（源码逐行） |
| 8 | 分桶**只看** `IsShared` | `MethodCompiler.vb:695-699` | **实锤**（源码逐行） |
| 9 | `<Initialize>` **恒** async、**恒**非共享 | `SynthesizedInteractiveInitializerMethod.vb:51-55`（`IsAsync`→`True`）、`:87-91`（`IsShared`→`False`） | **实锤**（源码逐行） |
| 10 | 局部变量的生存期止于方法返回 ⇒ 跨 submission 状态必须落字段 | `spec:22` 逐字、`spec:131` 整节 | **实锤**（规范逐字） |
| 11 | C# 顶层语句的顶层变量是**局部** | `SimpleProgramBinder.cs:26-39` 的 `BuildLocals` | **实锤**（源码逐行）；C# 侧运行**未跑** |
| 12 | C# 两模式在 `Span` 上的分野（TLS 放行 / csx 报 CS8345） | `meetings\inactive\meeting-top-level-implicit-shared.md` §9 与「并入基线的上一轮实测」（三条 exit 0 / CS8345） | **实锤**（上游实测，转述保留三态） |
| 13 | 本提案的机制回填物**不存在** | `tmp\investigations\` 目录不存在；`tmp` 全树无 `vbx-top-level-locals`/`submission-wall` 命中 | **实锤**（`find`） |

**降级为「推测」的项**（正文均已标注）：T1–T6 全部结论（机制未回填）；byref-like 收益的实际边界（M2）；「分叉必须加宿主入参」的量级（Drawbacks）；全称主张 #5 的完备性。

**未复现**：`.vbx` 形态的端到端行为（本阶段未改编译器）；C# 侧的一切运行行为（本树无 C# 运行环境）；Release 行为（未跑）。

## 相关文档

- `proposals\inactive\proposal-top-level-implicit-shared.md` 与其纪要 `meetings\inactive\meeting-top-level-implicit-shared.md` — **姊妹提案**：方向不同（改顶层成员的 `Shared` 默认值），区域重叠（同一片顶层声明语义）。**本提案不翻转默认共享性**，见 Summary 边界一；该纪要复会第六/七议程确立的「承诺保留 + 具名例外」处置纪律与「`Property`/`Event`/`WithEvents` 三态逐形式裁」要求，本提案**继续适用**（§3 T3、§5）。
- `spec\spec-scripting-dialect.md` — 被改的语义（`:15`/`:16`/`:22`/`:35`/`:58`/`:60`/`:64`/`:66`/`:273`/`:277`/`:310`/`:311`），见 §5。
- `proposals\proposal-scripting-dialect.md`（Active #15） — 与 spec 同步的提案侧文本。
- `spec\spec-byref-like-safety.md`（`:107`/`:237`）与 `meetings\meeting-byref-like-repl-safety.md` — 顶层 byref-like 的既有定案；其理由逐字是「**因为是字段**」⇒ 本提案的 M2 与它同源，Alternative C 与它冲突。
- `proposals\proposal-load-directive.md`（Active #18） — `#Load` 多树模型（T4 的对象）。
- `proposals\proposal-submission-shared-members.md` 与 `proposals\proposal-with-events-in-submissions.md` — 提交类成员与 `WithEvents` 的既有裁决（T3 的对象）。
- `decisions.md` **D5**（以 C#/csi 为蓝本、须说明分叉理由）与 **D6**（兼容性只对 GA 成立，且**只**解掉兼容性一条）。**按节号引用，不按行号**（D6 正文的引用纪律）。
- `issues\` 07（`Event`/`WithEvents` 在提交类的断言终止）/ 09 —— T3 的既有缺陷登记。
- 工作材料（`tmp\`，不入库）：`tmp\investigations\vbx-top-level-locals\submission-wall.md`（**待产出**，§3 的回填来源）；`tmp\vortex-logs\top-level-implicit-shared\pitfalls.md`（P-001~P-020）。
