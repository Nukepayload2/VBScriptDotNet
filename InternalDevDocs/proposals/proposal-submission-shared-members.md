# 提交/脚本类里的 `Shared` 成员 / Shared Members in Submissions

* [x] Proposed
* [ ] Prototype: [Not Started](pr/1)
* [ ] Implementation: [Not Started](pr/1)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

脚本顶层可以写 `Shared` 成员。这份提案要钉住的是**它们落在哪里、由谁承载、哪些形状今天不可用**：

- **`Shared` 方法（不隐式引用实例状态时）与 `Shared` 字段（无初始化器）健康**，与实例成员走同一条规则。反例见下条与 issue 08。
- **`Shared` 字段 / 属性的初始化器今天不可用**，而**非共享的同形状可用**——这就是本提案的动因。已登记的两条缺陷（issue 05 / issue 06）是同一个承重点的两个症状：
  - **issue 05**（`issues\issue-submission-shared-field-initializer-typeload.md`）：`Shared` 字段带初始化器 → 宿主 `TypeLoadException`。根因是提交类为**共享**分支复用了**为实例版设计**的构造器符号（`SynthesizedSubmissionConstructorSymbol`），该符号的形参表不随 `isShared` 分叉，于是发射出一个**带参数的 `.cctor`**。
  - **issue 06**（`issues\issue-script-shared-field-await-initializer-crash.md`）：`Shared` 字段初始化器含 `Await` → 编译器断言终止（`EXITCODE=35`）。根因是绑定期把脚本类的任何字段/属性初始化器都当作 **async 上下文**放行 `Await`，而重写端把**共享**初始化器放进一个**非 async** 的合成方法，`AwaitOperator` 因此存活到 codegen。
- **专项调查已回，两条结论改变了候选定价**（产物 `tmp\meetings\script-extension-methods\investigation-shared-initializer-async.md`，本提案已独立复核其承重锚点，见 §3、§5）：
  1. **「共享不能 `await`」是实现缺口，不是 CLR / 语言规则。** 语言规则（BC36950）只管**用户手写**的 `Async Shared Sub New`（`SourceMethodSymbol.vb:523-525`）；真正拦住的是「没有任何一步把 `Await` 从非 async 的 `.cctor` 里拿出来」（`AsyncRewriter.vb:364-368` + `LocalRewriter.vb:799-830`）。⇒ **issue 06 的目标不再只有「从崩改成报错」**：把共享初始化器**改道并入现有的 async `<Initialize>`**（候选 丙，本提案的**主候选**）在机制上通得过，且**同时消掉 issue 05**。
  2. **issue 06 的触发面被原登记写窄了**：**嵌套类型**（普通类，非脚本类）里字段/属性的 `Await` **同样崩**且**与 `Shared` 无关**；那条路的根因不同（诊断 BC36937 **报了但不阻止发射**），已登记为 **issue 09**。
- 本提案同时给出**同一族的实测清点**（哪些形状健康、哪些崩、哪些静默失效）——清点本身发现**另外三个独立缺陷**：顶层 `Event` / `WithEvents` 撞断言（**issue 07**）、共享成员不报 BC30369（**issue 08**）、初始化器诊断不 gate 发射（**issue 09**）；并发现 `spec\spec-scripting-dialect.md` 的两处「顶层形式穷尽」主张与实测不符。
- 修复候选：**丙（主候选）**＝共享初始化器改道并入现有 `<Initialize>`；**甲**＝拆出独立的共享构造器符号（D5 移植）；**乙**＝移植 CS8100 语义加绑定期诊断；**丙-2**＝新造一个共享版 async 初始化器（同向但更贵）。四者的改动面 / 代价 / 互相关系见 Alternatives。

**一句话结论**：VB 的**非共享**路径已经与 C# 同形（C# 的做法见 §5，实测对照见 §4），分叉**只在共享这一半**；因此本提案的修法是「把共享这一半按 C# 的形状补齐」，而不是新造模型。而且既然「共享不能 `await`」只是实现缺口，**补齐的候选里有一条能同时消掉 issue 05 与 issue 06 的顶层面**（丙）。

## Motivation
[motivation]: #motivation

- **顶层 `Shared` 不是边缘形状，`spec` 明文承诺了它。** 脚本声明/提交模型把顶层成员映射进合成脚本类，并明文写上「`Shared` 是成员修饰符」以及「顶层成员默认是实例成员，**除非声明为 `Shared`**」（`spec\spec-scripting-dialect.md:16`、`:48`、`:60`）。`spec` 同时把 `Shared` 字段初始化器所在的方法体写进了「顶层可执行语句进实例初始化器」的模型里（`:64`），并对「字段/属性初始化器是 async 上下文」作出明确承诺（`:176`）。今天，**共享字段/属性的初始化器**这一半与这几句话都不符（`Shared` 字段初始化器直接崩）——注意**只是这一半**：`Shared` 方法本身是健康的（探针 q04、q32 exit 0），所以这是**规范与实现的缺口**，不是新特性。
- **两条缺陷都是「用户可达 + 编译期零诊断」。** issue 05 是 `TypeLoadException`（宿主取入口点时才发现类型装不上），issue 06 是 `Assertion failed` 终止进程 —— 用户既拿不到诊断也拿不到可用行为，是与 `issue-vbi-imports-switch-nre.md` 同一形状的「宿主/编译器无守卫」故障。
- **C# 侧有完整的先例可照抄（D5）。** `decisions.md` **D5** 已把「提交构造器」列为 C# / csi 与 VB 分叉的**已证实例 1**：C# 把「提交构造器」与「静态初始化器构造器」分成两个类，VB 共用一个（`decisions.md` D5 的已证实例 1 列出的锚点即 issue 05 的两侧源码）。本提案把这条分叉的**全部后果**列清（不只 `TypeLoadException`，还有 `Await` 放不进去），并把 C# 的判据（CS8100）与落点逐个 `文件:行号` 核对，供 D5 的「落地详细设计」阶段直接引用。
- **它同时是 D6 的适用面。** 两种修法都只影响「今天恰好崩溃或静默错误的形状」，不改变任何**合法既有代码的正确语义**；`decisions.md` **D6** 也只解掉「兼容性」一条否决理由，其余理由（可行性、与 D5 的同形性、机制收益与代价、规范可表达性）仍须逐条论证——本提案的 Alternatives 就是按这四条给的。
- **上游合并面可低成本登记。** 甲 落在 `upstream-merge.md` §2.6「顶层代码 / 脚本提交语义」的语义面里——该条目**有锚点但全在宿主侧**（`:52` 的 `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`），**编译器侧一个都没有**；乙 落在已有登记面的文件内（`Binder_Expressions.vb` 在 §2.19、`Errors.vb` 在 §2.19 `:223` 与 §2.8 `:73`）。结论与义务见 §7。
- **共享这一半有一句没兑现的绑定期承诺，另一条链方向相反。** 绑定期对脚本类的**任何**字段/属性都算 async 上下文（`Binder_Expressions.vb:4726-4727`，不看 `IsShared`）——这是**放过**；而同一个 `Shared` 字段初始化器读宿主对象 / 前序提交变量时，接收者判据却**正确地**看了 `IsShared` 并拒绝（`Binder_Expressions.vb:2612` 的 `Not currentMember.IsShared`，报 BC30469）。同一条边界、两个方向相反的判据，说明**「脚本类里的共享面」没有被统一铺开**，本提案的三条缺口都长在这个母题上（另见 issue 08 的「No code in a script class is shared」示例注释）。

## Detailed design
[design]: #detailed-design

> 证据等级标注：本节每个关键设计点后标注证据等级（阶梯：未提供 / 已提供 / 已检查 / 已运行 / 已采纳 / 有结果支撑）。锚点均在本工作树逐行复核，故为**已检查**；探针结论为**已运行**。本阶段**未改编译器、未加单元测试**，故不出现「已采纳」/「有结果支撑」。

### 0. 实证入口与探针

- 编译器：Debug 构建 `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`（`2.0.0-Beta+5816a5c`，当期工作树）。
- 跑法：探针目录 `tmp\extprobe\` 下 `vbi.exe <probe>.vbx`，记录退出码。**本提案两轮共 47 条探针已跑并从该目录清除**；源码全文见 §4 的清单（故结论不依赖残留文件即可复跑）。第二轮 8 条（q37–q39、v1–v5）用于复核专项调查的两条结论（共享成员隐式 `Me`、嵌套类型的 `Await`），第二轮编号在前文的报告中写作 `s*` / `t*` / `u*` / `v*`，本提案统一用 v* 系列重跑了一遍。
- **专项调查的承重锚点由本提案独立复核**（不是转述）：`SourceMethodSymbol.vb:523-525`、`AsyncRewriter.vb:364-368`、`LocalRewriter.vb:799-830`、`SourceMemberFieldSymbol.vb:628-632`/`:665-673`、`SourceMemberContainerTypeSymbol.vb:2633`/`:2669-2681`/`:2838-2886`、`LocalRewriter_FieldOrPropertyInitializer.vb:47-54`、`SynthesizedEntryPointSymbol.vb:336-389`、`MethodCompiler.vb:1256-1257`/`:1272`/`:1315`、`BindingDiagnosticBag.vb:50-52`、`Binder_Expressions.vb:2611-2630`、`SynthesizedMethodBase.vb:195-200`、`SourceMemberContainerSymbol.cs:5917-5924` + `FieldOrPropertyInitializer.cs:18-20`。复核时发现一处**锚点漂移**：调查报告引的 `BindingDiagnosticBag.vb:58-60`（`GetInstance(template)`）实际在 **`:50-52`**（本提案按复核后的行号引用）。
- 退出码语义（实测归纳）：`0` 正常；`1` 报了编译错误；`34` 宿主 `TypeLoadException`（编译通过、类型装不上）；`35` 断言终止（Debug `Debug.Assert` → `Process terminated`）。
- 对照 Release 版 `Interactive\vbi\bin\Release\net10.0\vbi.exe`（`e307d0f`，较旧）**未跑**：本提案全部实测均来自当期 Debug 版。

### 1. 非共享路径已经与 C# 同形（这是本提案的判定基准）

VB 里「初始化器进哪个合成方法」由两处决定：

| 合成方法 | 拿到哪一组初始化器 | 锚点 |
|---|---|---|
| 脚本初始化器 `<Initialize>`（**实例**成员，`IsAsync = True`） | `processedInstanceInitializers` | `Compilation\MethodCompiler.vb:697-698`（`method.MethodKind = Constructor OrElse method.IsScriptInitializer` → 实例组）；`:1488-1490`（`BuildScriptInitializerBody`）；`Symbols\Source\SynthesizedInteractiveInitializerMethod.vb:51-55`（`IsAsync` 恒 True） |
| 共享构造器 `.cctor` | `processedStaticInitializers` | `MethodCompiler.vb:695-696`（`MethodKind = SharedConstructor` → 静态组） |

而**实例**侧的脚本提交构造器**不**把初始化器塞进自己的方法体：`MethodCompiler.vb:1481-1487` 明写 `If method.IsScriptConstructor Then body = block Else body = InitializerRewriter.BuildConstructorBody(...)`——实例脚本构造器走 `body = block`（不带初始化器），初始化器只走 `<Initialize>`。**实证**：`Dim y = Await Task.FromResult(2)` 正常（`INSTANCE-OK 2`，exit 0，探针 q18）；`ReadOnly Property P As Integer = Await Task.FromResult(7)` 正常（exit 0，探针 q29b）。**证据等级：已检查 + 已运行。**

**这一段与 C# 逐点同形**（C# 侧见 §5），所以下面的缺口是「共享那一半没跟上」，不是「VB 模型不同」。

### 2. 缺口一：共享初始化器落进「为实例版设计」的参数化构造器（issue 05）

链条四环（全部已检查）：

1. **提交类的共享分支会造构造器，且用的是实例版符号。** `Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737`：`TypeKind = Submission` 时，`If Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(initializers, False)` 成立就 `New SynthesizedSubmissionConstructorSymbol(syntaxRef, Me, isShared, binder, diagnostics)`——`isShared` 原样透传，**两类共用一个符号类**。
2. **该符号的形参表不查 `isShared`。** `Symbols\Source\SynthesizedSubmissionConstructorSymbol.vb:31-38` 无条件造一个 `submissionArray As Object()` 形参；`:40-44` 的 `Parameters` 直接回吐它。
3. **该符号被当作 `.cctor` 发射。** `Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb:59-63`（`Name` 随 `m_isShared` 取 `.cctor`，承重行 `:61`）、`:190-194`（`MethodKind` 随 `m_isShared` 取 `SharedConstructor`，**承重行 `:192`** 的 `Return If(m_isShared, MethodKind.SharedConstructor, MethodKind.Constructor)`）。
4. **于是共享初始化器真的被注入这个 `.cctor` 的方法体**：`MethodSymbol.vb:517-521` 的 `IsScriptConstructor` 要求 `MethodKind = Constructor`，共享时是 `SharedConstructor` ⇒ 为假 ⇒ `MethodCompiler.vb:1481-1487` 走 `BuildConstructorBody`（而不是 `body = block`），静态初始化器被写成赋值语句进 `.cctor`。

**净结果**：一个**带一个形参的 `.cctor`** 被发射 ⇒ 宿主 `TypeLoadException`（`Assembly.GetType` 抛在 `Scripting\Core\ScriptBuilder.cs:194`）。「带参数 `.cctor` 是非法元数据 ⇒ 类型装不上」这一环为**推测**（未 dump 元数据验证）；已实测的是最终症状与整条源码链。

**注意边界**：触发条件不是「有 `=` 初始化器」，而是「**`StaticInitializers` 里有需要注入构造器的条目**」。实测 `Shared Dim arr(2) As Integer`（无 `=`，但 `Binder_Initializers.vb:232-258` 的 `BindArrayFieldImplicitInitializer` 会补一条 initializer）同样 `TypeLoadException`（探针 q15）。已登记的 issue 05 边界表写成「有初始化器」，按本条应读作「有需要注入的初始化器条目」。

### 3. 缺口二：共享初始化器里的 `Await` 存活到 codegen（issue 06）

**绑定期放行**：`Binding\Binder_Expressions.vb:4720-4728` 的 `IsInAsyncContext()` 第二分支是

```vb
Return (containingMember.Kind = SymbolKind.Field OrElse containingMember.Kind = SymbolKind.Property) AndAlso
    containingMember.ContainingType.IsScriptClass
```

——**只问「是不是脚本类的字段/属性」，不问 `IsShared`**。于是共享字段初始化器里的 `Await` 在绑定期不报诊断（探针 p06 输出只有崩溃栈、无编译错误行）。**证据等级：已检查。**

**重写期不兑现**：共享初始化器进的是 `.cctor`（§2 第 4 环），而 `.cctor` 不是 async 方法，`Await` 不会被 async 重写消化 ⇒ `AwaitOperator` 存活到发射 ⇒ `CodeGen\EmitExpression.vb:206-209` 的 `Case Else` 抛 `ExceptionUtilities.UnexpectedValue`（Debug 下表现为断言终止）。**实测**：探针 p06 / q06 / q17 / q30 全部 `EXITCODE=35`，栈为 `EmitExpression.vb:209` ← `EmitConversion.vb:322`（`EmitDirectCastExpression`）← `EmitExpression.vb:1819`（`EmitAssignmentExpression` 的右值）← `MethodCompiler.vb:1673`（`codeGen.Generate()`，即 `If isAsyncStateMachine` 的 **else** 分支，`MethodCompiler.vb:1655-1678`）。**「初始化器落进非 async 的共享构造器」由此从 issue 06 的推断升级为实测**（`MethodCompiler.vb:1673` 是非 async 分支的调用点）。

**「嵌套一层就好了」不成立**：`Await` 藏在二元表达式里（探针 q06）同样崩；藏在 **lambda** 里（探针 q07）不崩在 `Await`、却仍触发 §2 的 `TypeLoadException`（lambda 的 `Await` 由 lambda 自己的 async 状态机消化，字段初始化器本身仍存在）。

**VB 侧没有任何等价诊断（无）**。判据与做法（可复跑）：

```text
grep -n "^        ERR_BadAwait" Compilers\VisualBasic\Portable\Errors\Errors.vb
  → 命中 6 条，全部清单：ERR_BadAwaitNothing = 36933（:1562）、
    ERR_BadAwaitNotInAsyncMethodOrLambda = 36937（:1566）、ERR_BadAwaitInTryHandler = 36943（:1572）、
    ERR_BadAwaitInNonAsyncMethod = 37057（:1642）、ERR_BadAwaitInNonAsyncVoidMethod = 37058（:1643）、
    ERR_BadAwaitInNonAsyncLambda = 37059（:1644）——没有一条与「静态/共享初始化器」有关。
grep -rni "staticinitializer" Compilers\VisualBasic\Portable\Errors\
  → 命中 2 条，都是 ERR_BadStaticInitializerInResumable = 36955（Errors.vb:1585；消息
    "Static variables cannot appear inside Async or Iterator methods."，VBResources.resx:4736-4738）
    ——那说的是 async/iterator 方法里的 Static **局部变量**，与本缺口无关。
grep -rni "await" Compilers\VisualBasic\Portable\VBResources.resx | grep -ci "static\|shared"
  → 0 命中。
```

`Await` 相关判据也确认无 Field/Property 分支：`Binder_Expressions.vb:5055-5069` 的 `GetAwaitInNonAsyncError()` 只有三种出口（在 lambda / `ContainingMember` 是 Method / 兜底 `ERR_BadAwaitNotInAsyncMethodOrLambda`）。**证据等级：已检查。**

### 3.1 为什么「非 async 方法里的 `Await`」不会被消化（这不是 CLR 规则）

**语言规则只挡用户手写的 async 构造器**：`Symbols\Source\SourceMethodSymbol.vb:523-525` 在**构造函数修饰符解码**里报 BC36950（`ERR_ConstructorAsync = 36950`，「Constructor must not have the 'Async' modifier.」）——它按 `Async` 标志判，与 `Shared` 无关（`Shared` 的修饰符处理在它之后，`:527-537`）。**没有任何规则**挡住编译器自己把 `await` 放进 `.cctor`。**证据等级：已检查。**

**真正拦住的是「没有一步把它拿出来」**：`Lowering\AsyncRewriter\AsyncRewriter.vb:364-368` 的 `GetAsyncMethodKind` 首行就是 `If Not method.IsAsync Then Return AsyncMethodKind.None`，而 `.cctor` 的 `IsAsync` 恒 False（`Symbols\SynthesizedSymbols\SynthesizedMethodBase.vb:195-200`；`SynthesizedConstructorBase` 不覆盖它）⇒ 共享构造器**永远不会**走异步重写；上游的 `Lowering\LocalRewriter\LocalRewriter.vb:799-830`（`VisitAwaitOperator`）又**刻意原样保留**该节点（注释「Await operator expression will be rewritten in AsyncRewriter」）⇒ 节点必然活到 `EmitExpression.vb:206-209`。**证据等级：已检查。**

⇒ 结论：**「共享不能 `await`」= 两个实现缺口（没有共享版 async 载体 + 诊断不 gate 发射），不是 CLR 硬约束，也不是语言规则。** `Symbols\` 下的合成成员里唯一 `IsAsync = True` 的是 `<Initialize>`（`SynthesizedInteractiveInitializerMethod.vb:51-55`）——**限定在该目录**，`Lowering\LambdaRewriter\SynthesizedLambdaMethod.vb:173` 的 `IsAsync` 是回吐 `Me._lambda.IsAsync`（转发，不是另一个 async 合成载体；`AsyncRewriter.vb:118` 也把 `SynthesizedLambdaMethod` 与 `SynthesizedInteractiveInitializerMethod` 并列成「放在命名类型里的合成方法」）。所以「共享初始化器也走 async」在机制上等价于「给它找一个 async 载体」——见 丙。**证据等级：已检查（源码逐行）。**

### 3.2 触发面比 issue 06 的登记更宽：两副面孔

| | 面孔 ①：顶层 `Shared` 字段/属性初始化器 | 面孔 ②：**嵌套类型**里的字段/属性初始化器 |
|---|---|---|
| 与 `Shared` 的关系 | 是 `Shared` 特有 | **与 `Shared` 无关**（实例字段、实例属性同样崩） |
| 绑定期 | `IsInAsyncContext`（`:4726-4727`）把脚本类的字段一律当 async 上下文 ⇒ **零诊断** | 嵌套类不是脚本类 ⇒ 正确返回 False ⇒ **报 BC36937**（`:4742-4743`） |
| 为什么还崩 | 初始化器进非 async 的 `.cctor` | **诊断报进了全局袋、不阻止发射**（`MethodCompiler.vb:1256-1257`/`:1272`/`:1315` + `BindingDiagnosticBag.vb:50-52`） |
| 归属 | **issue 06** | **issue 09（本提案新登记）** |

**本提案独立实测（4 条新探针，见 §4 探针清单）**：嵌套类**实例**字段（v1）、嵌套类**共享**字段（v2）、嵌套类**实例属性**（v3）三项的 `= Await Task.FromResult(9)` 全部 `EXITCODE=35` 且**零诊断输出**；而把 `Await` 的操作数换成不可 await 的 `5`（v4）就正常打印 `BC36937 + BC36930`、exit 1；再叠一个重复成员声明（v5）也打印 `BC30260 + BC36937`、exit 1。**v4/v5 证明该诊断确实存在于编译结果里，v1–v3 证明它自己不生效、也拦不住发射。** 这把面孔 ② 的根因锁死在「诊断落在哪个袋 / 是否给节点标错」上。**证据等级：已运行（自跑，不是转述）。**

⇒ **issue 06 原登记只写了面孔 ①，须按本表扩面**；而面孔 ② 的修法与 06 无关（它要的是「诊断 gate 发射」或「报错时给节点标错」），因此 **丙（或任何让顶层共享初始化器 `await` 合法的方案）都不会顺带修好面孔 ②**。

### 4. 同一族的实测清点（顶层形状逐项）

探针全部在当期 Debug `vbi.exe` 直跑 `.vbx`（`tmp\extprobe\`，跑完清除）。**同一份源码在实例与 `Shared` 两侧对照**是本节的关键设计。

| 顶层形状 | 非共享（实例） | `Shared` | 结论 |
|---|---|---|---|
| 字段，无初始化器 | — | ✅ exit 0（q01） | 健康 |
| 字段，有初始化器（`= 5` / `= Nothing` / `As New X` / 函数调用 / `Dictionary`） | ✅ exit 0（q36） | ❌ exit 34 `TypeLoadException`（p05、q08、q09、q10、q13、q14、q20、q21、q33） | **issue 05** |
| 字段，数组上界无 `=`（`arr(2)`） | — | ❌ exit 34（q15） | **issue 05**（边界比「有初始化器」宽，见 §2） |
| 字段，初始化器含 `Await` | ✅ exit 0（q18） | ❌ exit 35 断言（p06、q06、q17） | **issue 06** |
| 字段，初始化器含 `Await`（藏在 lambda 内） | — | ❌ exit 34（q07） | 落在 **issue 05**，非 06 |
| 属性，有初始化器 | ✅ exit 0（q27b） | ❌ exit 34（q02b、q26） | **issue 05** |
| 属性，初始化器含 `Await` | ✅ exit 0（q29b） | ❌ exit 35 断言（q30） | **issue 06** |
| 方法（**不隐式引用实例状态**时；含 `Shared Async … Await`） | ✅ | ✅ exit 0（q04、q32） | 健康——限定见下面两行 |
| 方法体**隐式**引用实例字段（不带限定写 `sx`） | ✅（实例方法本来如此） | ❌ 编译**零诊断**、运行期 `InvalidProgramException`（q38）；**普通类**同形状报 **BC30369**（q39） | **issue 08（新）** |
| 共享字段初始化器**调实例方法**（`Shared y = F()`） | — | ❌ 编译**零诊断**（普通类同形状报 BC30369，q37）；今天先停在 issue 05（q09） | **issue 08（新）** |
| （非顶层）**嵌套类型**里字段/属性的初始化器含 `Await` | ❌ exit 35 **零诊断**（v1 实例字段、v3 实例属性） | ❌ exit 35 **零诊断**（v2） | **issue 09（新）**，与 `Shared` **无关** |
| （对照）嵌套类型字段 `= Await 5`（不可 await）/ 再叠重复声明 | ✅ exit 1 打印 `BC36937 + BC36930`（v4）/ `BC30260 + BC36937`（v5） | 同左 | **不是缺口**：证明 BC36937 确实存在，上行是「不 gate 发射」而非「诊断缺失」 |
| `Event E As EventHandler` | ❌ exit 35 断言（q24） | ❌ exit 35 断言（q05） | **issue 07（新）**，与 `Shared` 无关 |
| `WithEvents r As New Raiser` | ❌ exit 35 断言（q25） | ❌ exit 35 断言（q03） | **issue 07（新）**，与 `Shared` 无关 |
| 嵌套类里的同形状（`Public Shared` 字段初始化器 / `Event` / `WithEvents`） | ✅ exit 0（q23、q34、q35；q16 的嵌套 `Shared` 成员默认 `Private` 不可访问，故以 q23 为有效对照） | ✅ 同左 | 健康——**缺口是「落在提交类本身」**，不是形状本身 |
| `Const d As Date = #…`（顶层、非 `Shared`） | ✅ exit 0（q22） | `Shared Const` 是编译错误 BC30233 = `ERR_BadConstFlags1`（「'{0}' is not valid on a constant declaration.」，`VBResources.resx:681-683`）（q11） | 健康（Date/Decimal 常量走另一条路，见 甲） |
| `Property … = …`（顶层）后跟另一条语句 | ❌ exit 1 BC30188 = `ERR_ExpectedDeclaration`（「Declaration expected.」，`VBResources.resx:591-593`）（q02、q19、q27、q29） | 同 | 文档化的实测边界：**该形状只能作为最后一条声明**。解析器为何这样切分**未深挖**（⇒ 推测：把下一行当属性块体） |

**探针清单（逐条源码与退出码；`¶` 是行分隔符，探针文件跑完已从 `tmp\extprobe\` 清除，故此处给出全文以便复跑）**：

```text
p05   Shared sx As Integer = 5                                                                        → 34
p06   Imports System.Threading.Tasks ¶ Shared Dim x = Await Task.FromResult(1) ¶ Console.WriteLine(x)   → 35  EmitExpression.vb:209
q01   Shared sx As Integer ¶ Console.WriteLine("NOINIT-OK " & sx)                                      → 0   NOINIT-OK 0
q02   Shared Property P As Integer = 7 ¶ Console.WriteLine("PROP-OK " & P)                             → 1   BC30188
q02b  Shared Property P As Integer = 7                                                                 → 34
q03   Class Raiser ¶     Event SomethingHappened As EventHandler ¶ End Class ¶ Shared WithEvents r As New Raiser
                                                                                                       → 35  SourceWithEventsBackingFieldSymbol.vb:66
q04   Shared Sub S() ¶     Console.WriteLine("SHAREDMETHOD-OK") ¶ End Sub ¶ S()                         → 0   SHAREDMETHOD-OK
q05   Shared Event E As EventHandler ¶ Console.WriteLine("SHAREDEVENT-OK")                              → 35  SynthesizedEventAccessorSymbol.vb:495
q06   Imports System.Threading.Tasks ¶ Shared Dim a = (Await Task.FromResult(1)) + 1 ¶ Console.WriteLine("NESTED-OK " & a)
                                                                                                       → 35  EmitExpression.vb:209
q07   Imports System.Threading.Tasks ¶ Shared Dim f = Task.Run(Async Function() Await Task.FromResult(1)) ¶ Console.WriteLine("LAMBDA-OK")
                                                                                                       → 34
q08   Shared Dim sb As New System.Text.StringBuilder("a") ¶ Console.WriteLine("NEW-OK " & sb.ToString())  → 34
q09   Function F() As Integer ¶     Return 3 ¶ End Function ¶ Shared Dim y = F() ¶ Console.WriteLine("CALL-OK " & y)
                                                                                                       → 34
q10   Shared ReadOnly sx As Integer = 5 ¶ Console.WriteLine("READONLY-OK " & sx)                        → 34
q11   Shared Const cc As Integer = 5 ¶ Console.WriteLine("CONST-OK " & cc)                              → 1   BC30233
q12   Shared Dim d As Date = #1/1/2020# ¶ Console.WriteLine("CONSTDATE-OK " & d.Year)                   → 34
q13   Shared Dim dec As Decimal = 1.5D ¶ Console.WriteLine("CONSTDEC-OK " & dec)                        → 34
q14   Shared s1 As Integer = 1 ¶ Dim i1 As Integer = 2 ¶ Console.WriteLine("BOTH-OK " & (s1 + i1))       → 34
q15   Shared Dim arr(2) As Integer ¶ Console.WriteLine("ARRAY-OK " & arr.Length)                        → 34
q16   Class C ¶     Shared sx As Integer = 5 ¶ End Class ¶ Console.WriteLine("NESTEDCLASS-OK " & C.sx)   → 1   BC30389（Private，探针本身写法问题）
q17   Imports System.Threading.Tasks ¶ Shared Dim x As Object = Await Task.FromResult(1) ¶ Console.WriteLine("OBJ-OK " & x)
                                                                                                       → 35  EmitExpression.vb:209
q18   Imports System.Threading.Tasks ¶ Dim y = Await Task.FromResult(2) ¶ Console.WriteLine("INSTANCE-OK " & y)
                                                                                                       → 0   INSTANCE-OK 2
q19   Shared ReadOnly Property P As Integer = 7 ¶ Console.WriteLine("ROPROP-OK " & P)                   → 1   BC30188
q20   Shared Dim s As String = Nothing ¶ Console.WriteLine("STR-OK " & (s Is Nothing))                  → 34
q21   （与 q14 同源，重跑确认）                                                                          → 34
q22   Const d As Date = #1/1/2020# ¶ Console.WriteLine("CONSTDATE-OK " & d.Year)                        → 0   CONSTDATE-OK 2020
q23   Public Class C ¶     Public Shared sx As Integer = 5 ¶ End Class ¶ Console.WriteLine("NESTEDCLASS-OK " & C.sx)
                                                                                                       → 0   NESTEDCLASS-OK 5
q24   Event E As EventHandler ¶ Console.WriteLine("EVENT-OK")                                           → 35  SynthesizedEventAccessorSymbol.vb:495
q25   Class Raiser ¶     Event SomethingHappened As EventHandler ¶ End Class ¶ WithEvents r As New Raiser ¶ Console.WriteLine("INSTANCE-WITHEVENTS-OK")
                                                                                                       → 35  SourceWithEventsBackingFieldSymbol.vb:66
q26   Shared ReadOnly Property P As Integer = 7                                                        → 34
q27   Property P As Integer = 7 ¶ Console.WriteLine("INSTPROP-OK " & P)                                 → 1   BC30188
q27b  Property P As Integer = 7                                                                        → 0
q29   Imports System.Threading.Tasks ¶ ReadOnly Property P As Integer = Await Task.FromResult(7) ¶ Console.WriteLine("INSTPROP-AWAIT-OK " & P)
                                                                                                       → 1   BC30188
q29b  Imports System.Threading.Tasks ¶ ReadOnly Property P As Integer = Await Task.FromResult(7)         → 0
q30   Imports System.Threading.Tasks ¶ Shared ReadOnly Property P As Integer = Await Task.FromResult(7)  → 35  EmitExpression.vb:209
q32   Imports System.Threading.Tasks ¶ Shared Async Function F() As Task(Of Integer) ¶     Await Task.Delay(1) ¶     Return 1 ¶ End Function ¶ Console.WriteLine("SHARED-ASYNC-OK " & (Await F()))
                                                                                                       → 0   SHARED-ASYNC-OK 1
q33   Imports System.Threading.Tasks ¶ Shared Dim d As New Dictionary(Of String, Integer) ¶ Console.WriteLine("DICT-OK " & d.Count)
                                                                                                       → 34
q34   Public Class Publisher ¶     Public Event E As EventHandler ¶ End Class ¶ Dim pub As New Publisher ¶ Console.WriteLine("NESTED-EVENT-OK")
                                                                                                       → 0   NESTED-EVENT-OK
q35   Public Class Raiser ¶     Public Event SomethingHappened As EventHandler ¶ End Class ¶ Public Class Holder ¶     Public WithEvents r As New Raiser ¶ End Class ¶ Dim h As New Holder ¶ Console.WriteLine("NESTED-WITHEVENTS-OK")
                                                                                                       → 0   NESTED-WITHEVENTS-OK
q36   Dim i1 As Integer = 2 ¶ Dim s1 As String = Nothing ¶ Console.WriteLine("INSTANCEFIELD-OK " & i1 & " " & (s1 Is Nothing))
                                                                                                       → 0   INSTANCEFIELD-OK 2 True
q38   Dim sx As Integer = 5 ¶ Shared Sub S() ¶     Console.WriteLine("SHARED-READS-INSTANCE " & sx) ¶ End Sub ¶ S()
                                                                                                       → 58  InvalidProgramException（栈顶 Submission#0.S()）
q39   Public Class C ¶     Public x As Integer = 5 ¶     Public Shared Sub S() ¶         Console.WriteLine("ORDINARY " & x) ¶     End Sub ¶ End Class ¶ C.S()
                                                                                                       → 1   BC30369（指向 x）
q37   Public Class C ¶     Public Shared x As Integer = F() ¶     Public Function F() As Integer ¶         Return 3 ¶     End Function ¶ End Class ¶ Console.WriteLine("ORDINARY " & C.x)
                                                                                                       → 1   BC30369（指向 F()）
v1    Imports System.Threading.Tasks ¶ Class C ¶     Dim s As Integer = Await Task.FromResult(9) ¶ End Class ¶ Console.WriteLine("NESTED-INSTANCE-FIELD " & (New C).s)
                                                                                                       → 35  EmitExpression.vb:209（零诊断输出）
v2    Imports System.Threading.Tasks ¶ Class C ¶     Shared s As Integer = Await Task.FromResult(9) ¶ End Class ¶ Console.WriteLine("NESTED-SHARED-FIELD " & C.s)
                                                                                                       → 35  EmitExpression.vb:209（零诊断输出）
v3    Imports System.Threading.Tasks ¶ Class C ¶     Property p As Integer = Await Task.FromResult(9) ¶ End Class ¶ Console.WriteLine("NESTED-INSTANCE-PROP " & (New C).p)
                                                                                                       → 35  EmitExpression.vb:209（零诊断输出）
v4    Imports System.Threading.Tasks ¶ Class C ¶     Public Shared s As Integer = Await 5 ¶ End Class
                                                                                                       → 1   BC36937 + BC36930（都打印）
v5    Imports System.Threading.Tasks ¶ Class C ¶     Public Shared s As Integer = Await Task.FromResult(9) ¶     Public Shared s As Integer = 3 ¶ End Class
                                                                                                       → 1   BC30260 + BC36937（都打印）
```

（q38 与 q39 是「共享方法体隐式引用实例字段」在**提交类**与**普通类**两侧的对照；q37 与 q09 是「共享字段初始化器调实例方法」的两侧对照——四条的完整源码与结论见 issue 08。v1–v3 是嵌套类型三形状的崩溃（零诊断），v4/v5 是对照组：同一条 BC36937 在「操作数带错」或「另有声明错误」时会正常打印 ⇒ 证明 v1–v3 的洞是「诊断不 gate 发射」，完整结论见 issue 09。）

（q16 与 q27 / q29 的 exit 1 是探针写法本身的问题——嵌套类成员默认 `Private`、顶层属性后跟语句被解析成属性块体——不作为缺陷证据，列出的目的是说明它们在表中的「健康」结论由 q23 / q27b / q29b 给出。）

**两条清点结论（都有逐项实测支撑）**：

1. **凡由 `Shared` 引发的缺口，集中在两个承重点：①共享字段/属性的初始化器；②共享成员体/初始化器里对实例成员的隐式引用（issue 08）。** 其余：`Shared` 方法（不隐式引用实例状态时，含 `Shared Async`）与无初始化器的 `Shared` 字段健康；`Shared` 事件 / `Shared WithEvents` 的崩**不由 `Shared` 引起**（实例形状同样崩），是另一个独立根因（issue 07）；嵌套类里的同形状全部健康（`Shared` 字段初始化器 / `Event` / `WithEvents` 三个形状四种探针 exit 0）。**注意 ② 是第二轮清点才浮出来的**：第一轮只问了「`Shared` 方法是否健康」，用的是不带限定名的探针（q04），因此**没有覆盖「方法体里隐式读实例字段」这一形状**——补测（q38）即崩。
2. **顶层 `Event`、`WithEvents`、`Property` 都是顶层四形式映射表之外的、解析器接受并可被声明表塞进脚本类的形状**（`Declarations\DeclarationTreeBuilder.vb:175-198`：非 `Regular` 时编译单元里除 namespace 外**每个**成员都进 `scriptChildren`）。其中 `Event` / `WithEvents` 落在提交类上会撞断言 ⇒ **已登记 issue 07**（`issues\issue-submission-implicit-type-member-asserts.md`）：提交类由 `ImplicitNamedTypeSymbol` 承载（`SourceMemberContainerTypeSymbol.vb:233-237`），它的 `IsImplicitlyDeclared` 恒 True（`ImplicitNamedTypeSymbol.vb:33-37`），撞上 `SynthesizedEventAccessorSymbol.vb:495` 与 `SourceWithEventsBackingFieldSymbol.vb:66` 的 `Debug.Assert(Not ContainingType.IsImplicitlyDeclared)`（同族第三处 `SynthesizedWithEventsAccessorSymbol.vb:93` 未单独实测）。

### 5. C# 的完整做法（D5 的对蓝本）

C# 把两件事拆成**两个类**，并且**由两个不同的符号承载两半**：

| | C# | 锚点 |
|---|---|---|
| 实例提交构造器 | `SynthesizedSubmissionConstructor : SynthesizedInstanceConstructor`，**只在实例分支创建** | `Symbols\Synthesized\SynthesizedSubmissionConstructor.cs:12`（类声明）、`Symbols\Source\SourceMemberContainerSymbol.cs:5701-5708`（`(!hasInstanceConstructor && !this.IsStatic && !this.IsInterface)` → `TypeKind.Submission ? new SynthesizedSubmissionConstructor(...) : new SynthesizedInstanceConstructor(...)`） |
| 静态初始化器构造器 | **`SynthesizedStaticConstructor`（另一个类，不派生实例构造器）**，独立一支 | `Symbols\Synthesized\SynthesizedStaticConstructor.cs:12-21`（类声明 + 只持 `containingType`）、`SourceMemberContainerSymbol.cs:5714-5719`（`if (!hasStaticConstructor && hasNonConstantInitializer(StaticInitializers))` → `new SynthesizedStaticConstructor(this)`） |
| 静态构造器的形参 | **恒空**：`ParameterCount` = 0、`Parameters` = Empty | `SynthesizedStaticConstructor.cs:72-78`、`:80-86` |
| 静态构造器的 kind / async | `MethodKind.StaticConstructor`（`:191-197`）、`IsStatic = True`（`:239-245`）、**`IsAsync = False`**（`:247-253`） | 同左 |
| 静态初始化器落到哪个方法体 | `.cctor` 体：`MethodCompiler.cs:547-550` 的三元式按 `MethodKind == StaticConstructor` 给 `processedStaticInitializers`（**承重行 `:549`**）；`SynthesizedStaticConstructor.CalculateShouldEmit` 用 `Binder.BindFieldInitializers(DeclaringCompilation, sourceType.IsScriptClass ? sourceType.GetScriptInitializer() : null, sourceType.StaticInitializers, …)` 绑定 | `Compiler\MethodCompiler.cs:547-550`（承重行 `:549`）、`SynthesizedStaticConstructor.cs:400-415` |
| 实例初始化器落到哪个方法体 | **不落 `.ctor`**：提交构造器用**空的**初始化器集编译 | `Compiler\MethodCompiler.cs:661-664`（`var processedInitializers = new Binder.ProcessedFieldInitializers() { BoundInitializers = ImmutableArray<BoundInitializer>.Empty }; CompileMethod(scriptCtor, …)`，注释「compile submission constructor last so that synthesized submission fields are collected from all script methods」） |
| 提交构造器的方法体组成 | 基构造器调用 + 提交登记（`MakeSubmissionInitialization`）+ 自己的 body，由 `ConstructScriptConstructorBody` 拼 | `Compiler\MethodBodySynthesizer.cs:22-30`（注释明写「Script field initializers have to be emitted after the call to the base constructor…」）、`:64-67`（`if (constructor.IsSubmissionConstructor) MakeSubmissionInitialization(...)`）、`:77+`（`MakeSubmissionInitialization` 定义） |
| `IsScriptConstructor` 判据 | `MethodKind == MethodKind.Constructor && ContainingType.IsScriptClass` | `Symbols\MethodSymbol.cs:664-670` |

**分工一句话**：实例初始化器 → 异步 `<Initialize>`；静态初始化器 → `SynthesizedStaticConstructor`（无参、非 async）；提交登记 → `SynthesizedSubmissionConstructor`（有参但**只**做登记）。**两半之间没有任何共享的符号**——VB 在共享这半边缺的正是「一个能承载共享初始化器的、形状正确的符号」（甲）**或**「把共享初始化器交给已有的 `<Initialize>` 承载」（丙）。注意两者与 C# 的关系不同：甲 是**追平** C# 的两类分立；丙 是**有意分叉**（C# 的静态初始化器绝不进 async 方法，见下条），需要自己的理由。

**CS8100 的判据（逐行读 `Binder\Binder_Await.cs:152-213`）**：

```csharp
case SymbolKind.Field:
    if (containingMemberOrLambda.ContainingType.IsScriptClass)
    {
        if (((FieldSymbol)containingMemberOrLambda).IsStatic)
            info = new CSDiagnosticInfo(ErrorCode.ERR_BadAwaitInStaticVariableInitializer);   // CS8100
        else
            return false;                                                                     // 实例：放行
    }
    break;
```

- 判据是「**脚本类的静态字段**初始化器」——`Binder_Await.cs:160`（只列 `SymbolKind.Field`）、`:161`（`ContainingType.IsScriptClass` 门控）、`:163-166`（`IsStatic` → CS8100）、`:167-170`（实例 → `return false`，即**不报错**）。消息文案为「The 'await' operator cannot be used in a static script variable initializer.」（`CSharpResources.resx:3831-3833`），错误码 `ERR_BadAwaitInStaticVariableInitializer = 8100`（`Errors\ErrorCode.cs:1335`）。
- **在绑定期报**：它是 `ReportBadAwaitWithoutAsync`（`:152-213`）的分支，由 `ReportBadAwaitDiagnostics`（`:46-50`）调用，入口是 `BindAwait`（`:29-44`），整个过程在绑定阶段完成。
- **`switch` 里没有 `Property` 分支，但属性初始化器仍会被 CS8100 覆盖（本提案已复核，推翻原先的「未核」）**：C# 把属性初始化器登记在**背后字段**上——`SourceMemberContainerSymbol.cs:5917-5924`（`AddInitializer(ref staticInitializers, backingField, initializer)` / `ref instanceInitializers, backingField, …`），`FieldOrPropertyInitializer.cs:18-20` 的字段注释逐字「The field being initialized (**possibly a backing field of a property**)」。因此判据看到的 `ContainingMemberOrLambda` 是那个 backing field ⇒ **静态属性初始化器同样命中 CS8100，实例属性初始化器同样被放行**。⇒ 移植 CS8100 语义时 VB 侧也必须覆盖**共享属性**初始化器（VB 的 `Binder_Initializers.vb` 属性初始化器的 `ContainingMember` 是 `PropertySymbol`，两边形状不同，见 Unresolved 4）。**证据等级：已检查（三处源码锚点）；未实测**（本树无 C# 运行环境）。
- **实例与静态的差异在 C# 里是有意为之**：实例放行是因为 C# 的脚本实例初始化器进 async `<Initialize>`（上表最后两行）；静态报错是因为静态初始化器进 `.cctor`。
- **C# 为什么「禁止」而不「合成一个能 await 的静态初始化器」？仓内只有一条书面理由，且不是决策记录**：`Compilers\CSharp\Test\Emit\CodeGen\CodeGenAsyncTests.cs:8623-8625` 的测试注释逐字「await should be disallowed in static field initializer / since the static initialization of the class must be / handled synchronously in the .cctor.」（用例 `AwaitInScriptStaticInitializer`，`WorkItem(5787)`）。**除此之外全仓未找到任何设计/决策痕迹**（`grep "synchronously in the .cctor"` 仅此 1 命中）。⇒ 「C# 如此决定」**不等于**「CLR 硬约束」；对 VB 而言，`decisions.md` D5 的判据是「C# 这么做**且 VB 没有对应理由**才是」——VB 若要选「让共享初始化器 `await`」（丙），需要给出的正是**分叉理由**，而不是假装 C# 禁止就等于不可行。**证据等级：已检查（单条注释 + 零命中）。**
- **结构上 C# 的静态初始化器只注入 `SynthesizedStaticConstructor`，该符号 `IsAsync = false`**（`SynthesizedStaticConstructor.cs:247-253`），C# 也没有任何 async 的静态/共享初始化器合成类（唯一 async 的是 `SynthesizedInteractiveInitializerMethod.cs`）。⇒ 「允许 `await`」在 C# 是**没做**，不是**做不了**。**证据等级：已检查。**

### 6. 可运行示例

下面这个脚本把本提案的全部形状写在一起。**今天的实测结果**（探针逐项）：`Shared counter` 与 `Shared total` 两行会让整份脚本 `TypeLoadException`；把 `Shared fromCall` 那行保留则运行期 `InvalidProgramException`（它调实例方法，见 issue 08）；其余行正常。

```vbnet
Imports System.Threading.Tasks

Class Raiser                                          ' 顶层类型 → 脚本类的嵌套类型
    Event SomethingHappened As EventHandler
End Class

Shared counter As Integer = 1                         ' 共享字段 + 初始化器：issue 05 承重点
Shared ReadOnly total As Integer = 2                  ' 共享 ReadOnly 字段同样在承重点上（探针 q10）
Dim instanceField As Integer = 3                      ' 实例字段 + 初始化器：健康（对照）

Shared Async Function ComputeAsync() As Task(Of Integer)   ' 共享方法（含 async）：健康（探针 q32）
    Await Task.Delay(1)
    Return counter + total + instanceField
End Function

Function Local() As Integer
    Return 10
End Function

Shared fromCall As Integer = Local()                  ' 共享初始化器调实例方法：今天零诊断（issue 08），普通类报 BC30369（探针 q37/q09）

Console.WriteLine("sum=" & (Await ComputeAsync()))    ' 顶层 Await 本身是合法的（进 async <Initialize>）
```

**修复后各条含义**（候选 丙 之后，见 Alternatives）：
`counter` / `total` 的初始化器改由异步 `<Initialize>` 承载（不再造共享构造器）；`instanceField` 的初始化器仍在 `<Initialize>`；`Shared Async Function` 与顶层 `Await` 与今天一致。**`Shared fromCall = Local()` 这一行是另一条独立缺陷**：共享成员体/初始化器里对实例成员的**隐式**引用在提交类不报 BC30369（普通类会报），后果是运行期 `InvalidProgramException`（issue 08，探针 q38 vs q39）——**丙 不会顺带修好它**，需要单独给诊断。

### 7. 与规范、与上游合并账的关系

**（a）`spec-scripting-dialect.md` 需改/需补的句子（逐句行号）**：

| 行 | 现文（摘） | 需要的改动 |
|---|---|---|
| `:11-18` | 顶层形式映射表（四行） | `Dim` 行需注明「声明 `Shared` 时是共享字段，其初始化器进共享构造器」；表外需补「解析器还接受 `Event` / `WithEvents` / `Property` 等成员声明」（实测：`DeclarationTreeBuilder.vb:175-198` 把除 namespace 外每个成员都塞进脚本类） |
| `:56` | 「The mapping is exhaustive over the four forms.」 | **与实测不符**（顶层 `Event` / `Property` 被接受并进脚本类），须改写或限定 |
| `:58` | 「Top-level `Dim` is a field… has instance state」 | 需补 `Shared` 分支与其初始化器的落点 |
| `:60` | 「Top-level `Sub`/`Function` are instance members unless declared `Shared`… A `Shared` method has no implicit receiver」 | 句子本身与实测一致（`Shared` 方法健康），建议补一句「`Shared` 方法（含 `Async`）与实例方法走同一规则」 |
| `:64` | 「…The top-level statements do not run in the script class constructor; they run in the initializer method.」 | 需补一句关于**共享**字段/属性初始化器的落点，**内容取决于候选**：选 丙 ⇒「共享字段/属性的初始化器与实例初始化器、顶层语句同序列、同进异步初始化器」；选 甲 ⇒「它们进（同步的）共享构造器，因此其中不得有 `Await`」——这正是 issue 06 的规范面 |
| `:66` | 顶层声明的作用域 | 与 `Shared` 无关，不改；仅当 §(b) 判定共享初始化器要迁到别的方法时需复核 |
| `:102` | 「`WithEvents` hookup constructors … are not synthesized for a submission class.」 | 与 issue 07 的断言是两件事（后者在**发射期**、无 `Handles` 也会撞），建议加指向 |
| `:176` | 「The binder treats a field or property initializer of a script class as an async context, so an `Await` in a field initializer is accepted.」 | **本提案要改的核心句**：绑定期确实对**任何**脚本类字段/属性都算 async 上下文（`Binder_Expressions.vb:4726-4727`），但**共享**初始化器并不落在 async 方法里，故该句对 `Shared` 是**未被兑现的承诺**。须补条件（拒收 or 兑现，取决于 丙） |
| `:190` | `WithEvents` 钩子按 kind 分派 | 建议加指向 issue 07 的边界 |
| `:273` | 「The four mappings are exhaustive over the declarations a compilation unit can contain, so no declaration is left without a home.」 | 同 `:56`，**与实测不符** |
| `:311` | C# 对照表「Top-level method」行 | 建议补一行「Top-level field initializer」：C# 实例→异步 `<Initialize>`、`static`→`.cctor` 且绑定期报 CS8100；VB 实例→同上，`Shared`→今天崩（§2/§3） |
| `:348` | 「`WithEvents` in a submission class … no diagnostic is reported for it, and the compilation does not complete.」 | 与 issue 07 同片边界，建议互相指向 |

**（b）上游合并账（`upstream-merge.md`）的登记面判定**：

- **甲** 改 `Symbols\Source\SourceMemberContainerTypeSymbol.vb`。该文件**不在**「二、本地修改面清单」的任何 §2.x 条目里出现（判据：`grep -n "SourceMemberContainer" InternalDevDocs\upstream-merge.md` **零命中**；注意 `SynthesizedSubmission*` 在 §二·补 的 `693e4a5` 行出现过，但那是 `Lowering\SynthesizedSubmissionFields.vb`——字段符号文件，不是本提案要动的 `Symbols\Source\` 下那两支构造器），但它属于 **§2.6「顶层代码 / 脚本提交语义（修改）」** 的语义面。**§2.6 有锚点，但两个都在宿主侧**（`:51` 的语义句 + `:52` 的 `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）——**编译器侧一个都没有**（`SourceMemberContainerTypeSymbol.vb` 等未出现）。⇒ **不新增登记条目类别，但须为 §2.6 补编译器侧的文件级锚点**（否则违反「文件面级判定」的补登记义务，见 `:232-237` 的口径）。
- **乙** 改 `Binding\Binder_Expressions.vb`（已在 §2.19 有锚点 `:211`、`:212`、`:214`）、`Errors\Errors.vb`（已在 **§2.19 `:223`** 与 **§2.8 `:73`**）、`Errors\ErrorFacts.vb`、`VBResources.resx`（§2.19 `:225`）与 13 份 xlf（`Compilers\VisualBasic\Portable\xlf\*.xlf`，实测 13 个文件）。⇒ 文件面已在册，**但按 `:233-235` 的口径「文件被某条登记 ≠ 该文件的所有改动点都被登记」，须以新锚点补登记**。新增错误码要占用 `Errors.vb` 的编号带（`ERR_NextAvailable = 37341`，`Errors.vb:1815`）。
- **丙**（若采纳）改的是**声明收集期**：`Symbols\Source\SourceMemberFieldSymbol.vb`（分桶 `:628-632`、`:665-673`）、`Symbols\Source\SourceMemberContainerTypeSymbol.vb`（属性分桶 `:2669-2681`、调试偏移 `:3242-3258`/`:3278-3301`），以及可能被牵动的 `Emit\NamedTypeSymbolAdapter.vb`（beforefieldinit `:451-511`）。这些文件与 §2.6 的语义面同域、**同样没有文件级锚点**；`LocalRewriter_FieldOrPropertyInitializer.vb` / `SynthesizedEntryPointSymbol.vb` / `AsyncRewriter.vb` **不需要改**（丙 的可行性正建立在此），故 丙 的登记面比 甲 略宽但仍集中在同一处。
- **不影响 `PublicAPI.*`**：甲 / 乙 / 丙 全在 internal / Friend 面（`SynthesizedConstructorSymbol` 是 `Friend NotInheritable`，诊断与绑定改动不出公共 API）。

## Drawbacks
[drawbacks]: #drawbacks

- **同一片区域另有三个独立缺陷（issue 07 / 08 / 09），本提案不修它们。** 顶层 `Event` / `WithEvents` 撞断言（07，`IsImplicitlyDeclared` 的断言族）、共享成员不报 BC30369（08，隐式 `Me` 的接收者解析）、初始化器诊断不 gate 发射（09）。三者与「共享初始化器的落点」只共享「落在脚本类/提交类上」这个前提，根因与修法都不同。本提案把它们登记清楚并划清边界，但不纳入修复范围——否则一份提案要同时动「构造器符号」「初始化器落点」「接收者解析」「隐式类型判定」「发射门」五条线。
- **选 丙 会让 `Shared` 与实例初始化器的执行时机变得相同。** 今天是「共享进 `.cctor`（`beforefieldinit`，首次访问类型时触发、每类型一次）/ 实例进 `<Initialize>`（每次提交执行一次）」；丙 之后两者都进 `<Initialize>`。这直接影响「源码顺序在前的顶层语句读顺序在后的 `Shared` 字段」「同一 `Script` 重复运行是否重赋值」这两个可观测量（Unresolved 2）。另需注意：**C# 有意让两者不同**（`SynthesizedStaticConstructor.cs:247-253` + 测试注释「must be handled synchronously in the .cctor」），所以丙 是**有意分叉**，规范必须多写一句，且要向 C# 用户解释差异。这是 丙 的主要代价。
- **丙 也不能让共享字段初始化器与实例字段完全等价。** `Shared` 字段初始化器**读不到宿主对象/前序提交变量**（`Binder_Expressions.vb:2612`；实测 REPL `Shared Dim s1 = Args` → BC30469）——这条限制在 丙 之后依然成立（Unresolved 3），本提案不承诺放开它。
- **顶层 `Property` / `Event` 等「表外形式」的规范状态仍含混。** 本提案只钉住实测行为（`Event`/`WithEvents` 崩、`Property` 实例可用），不改解析器；规范化留 Unresolved 6。

## Alternatives
[alternatives]: #alternatives

> 候选方案清单是本提案进 meeting 的前置条件。四条候选各自给 **改动面 / 承重锚点 / 代价 / 对既有行为的影响 / 不解决什么**。**主候选是 丙**（共享初始化器改道并入现有 `<Initialize>`）——专项调查（`tmp\meetings\script-extension-methods\investigation-shared-initializer-async.md`）证明「共享不能 `await`」是实现缺口，且 丙 同时消掉 issue 05。甲的定位因此改变：**丙 若采纳，甲 不再必要**（见「候选关系」）。
>
> **候选一览**（详细定价见各节）：
>
> | 候选 | 一句话 | 解决 05 | 解决 06（顶层面） | 代价 | 与 C# 的关系 |
> |---|---|---|---|---|---|
> | **丙（主）** | 共享字段/属性初始化器改道进已有 async `<Initialize>` | ✅ 同时消掉 | ✅ 变成「支持 `await`」 | 中（收集期分桶 + 调试偏移 + 静态桶切割） | **有意分叉**（须给理由） |
> | 甲 | 共享分支改用无参的共享 `SynthesizedConstructorSymbol` | ✅ | ❌（`.cctor` 仍非 async） | 小 | 追平 C# 的两类分立 |
> | 乙 | 移植 CS8100 语义，绑定期给共享初始化器的 `Await` 报错 | ❌ | ❌ 但把「崩」变「报错」 | 中（新错误码全链） | 追平 C# 的判据 |
> | 丙-2 | 新造一个共享版 async 初始化器 | ✅ | ✅ | 高（入口点 + 宿主契约 + 顺序语义必破） | 无先例 |

### 甲：共享分支改用一个无参的共享构造器符号（对齐 C# 的两类分立）

- **改动面**：`Symbols\Source\SourceMemberContainerTypeSymbol.vb:2726-2737` 的 `TypeKind = Submission` 分支按 `isShared` 分叉；共享分支复用同文件的 `EnsureCtor`（`:2771-2802`），它 `New` 的是 `SynthesizedConstructorSymbol`（`:2800`），形参恒空。
- **承重锚点**：`Symbols\Source\SynthesizedConstructorSymbol.vb:49-53`（`Parameters` 恒 `Empty`）、`SynthesizedConstructorBase.vb:59-63`（`Name` 取 `.cctor`，承重行 `:61`）、`:190-194`（`MethodKind` = `SharedConstructor`，**承重行 `:192`**）、`MethodCompiler.vb:695-696`（共享构造器拿静态初始化器组）、`:1481-1486`（非 `IsScriptConstructor` → `BuildConstructorBody` 把初始化器注入方法体）。
- **既有同形先例（这是「延申现有机制」的直接依据）**：`CreateSharedConstructorsForConstFieldsIfRequired`（`SourceMemberContainerTypeSymbol.vb:3195-3211`）**已经**为一个提交类造过无参共享 `SynthesizedConstructorSymbol`（Date/Decimal 常量路径）。实测探针 q22（顶层 `Const d As Date = #1/1/2020#`）exit 0 佐证这条路可用。
- **代价**：小（一处分支 + 复用既有符号类）。**未实测**的风险点：共享分支改用 `EnsureCtor` 后与 `CreateSharedConstructorsForConstFieldsIfRequired` 的交互——两个机制都检查 `Members` 里的 `.cctor` 名（`AddSymbolToMembers` 按 `sym.Name` 收，`SourceMemberContainerTypeSymbol.vb:2979-2991`；对方检查点 `:3199-3201`），命中即后者不造符号、常量初始化器仍进同一个共享构造器。**这个交互是推测，plan 阶段必须先实证。**
- **对既有行为的影响**：`GetScriptConstructor()`（`NamedTypeSymbol.vb:697-700`，`InstanceConstructors.Single()`）不受影响（共享构造器不进 `InstanceConstructors`）；`MethodCompiler.vb:568` 的两条 `Debug.Assert` 仍成立。**已修复的形状**（`Shared` 方法、无初始化器的 `Shared` 字段、`Const` Date/Decimal、嵌套类）路径不变。
- **不解决**：issue 06（共享构造器仍然不是 async）、issue 08、issue 09。**甲 与 丙 的关系见「候选关系」——丙 若采纳，甲 不再必要。**
- **定位变化（专项调查后）**：甲 是「追平 C# 的两类分立」（D5 的形状），但它只修「符号带错形参」这一半，**不动「初始化器落进哪个方法」**。今天它仍是 issue 05 的**最小**修法，只是不再自动成为首选。

### 乙：移植 CS8100 语义，在绑定期给「共享初始化器里的 `Await`」报错

- **改动面**：`Binding\Binder_Expressions.vb`（判据）+ `Errors\Errors.vb`（新错误码，`ERR_NextAvailable = 37341`，`Errors.vb:1815`）+ `Errors\ErrorFacts.vb`（报错/非报错分类）+ `VBResources.resx`（文案）+ 13 份 `xlf` + 单元测试。
- **承重锚点**：判据现状在 `Binder_Expressions.vb:4720-4728`（`IsInAsyncContext` 对脚本类的**任何** Field/Property 返回 True，无 `IsShared` 分叉）与 `:4740-4744`（只在 `Not IsInAsyncContext()` 时报 `GetAwaitInNonAsyncError()`）；错误出口清单在 `:5055-5069`（无 Field/Property 分支）。**读源码可得的一条硬事实：走 `GetAwaitInNonAsyncError` 加分支这条路不可达**——共享字段时 `IsInAsyncContext()` 为真，`:4743` 根本不执行 ⇒ 乙 必须改调用结构（在 `:4740-4744` 之后补子情形判据），而不是只往 `GetAwaitInNonAsyncError` 里加一个 `case`。**证据等级：已检查（源码逐行）。**
- **低成本子形态（备选，非推荐）**：让 `IsInAsyncContext` 对**共享**字段/属性返回 False ⇒ `Await` 落到 `:5068` 的 `ERR_BadAwaitNotInAsyncMethodOrLambda = 36937`（消息「'Await' can only be used when contained within a method or lambda expression marked with the 'Async' modifier.」，`VBResources.resx:4721-4723`）。**不推荐**：①消息不对症（用户写的是字段初始化器，不是方法/lambda）；②`IsInAsyncContext()` 另有 10 处以上消费者（`Binder_Statements.vb:335`/`:518`/`:1167`/`:1170`/`:2754`/`:3724`/`:5337`/`:5372`、`Binder_Lambda.vb:593`/`:634` 等），它们多数处于方法体内（`ContainingMember` 是 Method ⇒ 走 `:4722-4723` 的 Method 分支，不受影响），**故实际扩面可能只限字段/属性初始化器的绑定——但未逐个核**（推测）。
- **代价**：中（一条新错误码要走全错误码/分类/本地化链）。**收益**：与 C# 同向（D5），把「崩进程」变成「一条能定位到行的诊断」。
- **对既有行为的影响**：只影响今天**恰好崩溃**的形状（issue 06 的面孔 ①），不改变任何能正常编译的脚本的语义（D6 的适用面）。**未实测**：Release 构建下今天是否也崩（本提案只跑了 Debug）。
- **覆盖面（专项调查后必须写清）**：乙 只覆盖**共享**初始化器（面孔 ①）。**嵌套类型那条（面孔 ②，与 `Shared` 无关）不是「缺诊断」而是「诊断不 gate 发射」**（issue 09），乙 **修不了**它——那边已经有 BC36937。
- **若移植 CS8100，VB 侧还要覆盖共享属性**：C# 的 `switch` 虽只列 `SymbolKind.Field`，但它的属性初始化器登记在 backing field 上（`SourceMemberContainerSymbol.cs:5917-5924`、`FieldOrPropertyInitializer.cs:18-20`，本提案已复核），故静态属性同样命中 CS8100；而 VB 的属性初始化器 `ContainingMember` 是 `PropertySymbol`（`Binder_Initializers.vb:381-387` 一带），判据形状不同，**不能照抄**。

### 丙（**主候选**）：共享字段/属性的初始化器改道并入现有 `<Initialize>`

- **候选内容**：收集期不再把**共享**字段/属性的初始化器放进静态桶，而是与实例初始化器、顶层语句并进**同一条实例流**；于是它们由**已有的异步 `<Initialize>`**（`SynthesizedInteractiveInitializerMethod`，`IsAsync = True`）承载，`Await` 由 `AsyncRewriter` 正常消化。**不再为它们合成共享构造器。** 绑定期不用改——`MethodCompiler.vb:599-607` 今天已经把**同一个** `scriptInitializer` 传给静态桶与实例桶两条绑定路径（`Binder_Initializers.vb:122-124` 给它套 `TopLevelCodeBinder`），所以「`Await` 被当成 async 上下文」这一行为**改道前后完全一样**。
- **机制可行性（承重锚点，本提案已逐条复核）**：
  - **发射层通（读、写两侧都有锚点）**：**赋值侧**，共享字段在降级时按**被初始化符号**的 `IsShared` 决定要不要造 `Me`——`Lowering\LocalRewriter\LocalRewriter_FieldOrPropertyInitializer.vb:47-54`：`If Not initializedSymbols.First.IsShared Then meReferenceOpt = New BoundMeReference(...)`；共享字段拿 `Nothing` ⇒ 走 `stsfld`，**不要求宿主方法共享**。**读取侧**同向：`CodeGen\EmitExpression.vb:669-688` 的 `EmitFieldLoad` 按 `field.IsShared` 分派 `EmitStaticFieldLoad`（`:684-685`）与 `EmitInstanceFieldLoad`（`:687`），且 `:674` 只有在**非**共享字段时才碰接收者。⇒ 把共享字段初始化器放进**实例**方法 `<Initialize>`，读、写两条发射路径都不需要 `Me`。
  - **宿主契约零改动**：`<Initialize>` 的名字、签名、返回类型、调用点全部不变。调用点只有两个：`SynthesizedEntryPointSymbol.vb` 的 `<Main>` 与 `<Factory>`——`<Factory>` 的体是固定两步（建实例 → `Return submission.<Initialize>()`，`:340-389`，本提案已复核），它不关心 `<Initialize>` 的体里有什么。宿主按类型名+方法名反射取其运行时方法并绑成 `Func(Of Object(), Task(Of T))`（`Scripting\Core\ScriptBuilder.cs:161-163`、`:189-196`）。仓内 `GetSubmissionInitializer`（`VisualBasicCompilation.vb:921`、`CSharpCompilation.cs:1894`）**两处命中都是定义、零消费点**（本提案独立 grep 复核）。
  - **`<Initialize>` 的 `IsShared = False` 不需要改**：`SynthesizedInteractiveInitializerMethod.vb:87-91` 恒 False，且它必须保持（顶层语句读 `Print`/`Args`、写实例字段都依赖它），仓库里还有两处 `Debug.Assert(Not _topMethod.IsShared)` 守着（`LocalRewriter_PreviousSubmissionReference.vb:16`、`LocalRewriter_HostObjectMemberReference.vb:15`）。**丙 不需要碰它**——因为要不要 `Me` 由**被初始化符号**决定（上一条），不由宿主方法决定。
  - **`spec:64` 的「按源码顺序」天然保住**：并进的是**同一条**已存在的流。`AddMember` 按源顺序遍历成员（`ImplicitNamedTypeSymbol.vb:194-204`），字段与顶层语句都往同一队尾追加（`SourceMemberContainerTypeSymbol.vb:1565-1585`，其中 `:1571-1574` 明确断言「initializers should be added in syntax order」）；顶层语句**今天已经在这条流里**（`:2633`）。绑定与重写也各只有一条序列（`Binder_Initializers.vb:84-200`、`InitializerRewriter.vb:174-186`）。**这一条是 丙 优于 丙-2 的关键**（见下）。
- **它同时消掉 issue 05（机制 + 判别性实证）**：改道后 `StaticInitializers` 里不再有这些初始化器 ⇒ `AddDefaultConstructorIfNeeded` 的共享分支条件（`:2729` 的 `Not isShared OrElse Me.AnyInitializerToBeInjectedIntoConstructor(...)`）不成立 ⇒ **不再造那个带形参的共享提交构造器**（`:2735` + `SynthesizedSubmissionConstructorSymbol.vb:31-38`）⇒ 没有带参 `.cctor`。**判别性实证**：症状与「共享构造器是否被造出来」一一对应——静态桶**为空**（`Shared sx As Integer`，无初始化器）→ exit 0（q01）；静态桶**只有一条数组上界**条目（`Shared Dim arr(2) As Integer`）→ exit 34（q15）。⇒ 「清空静态桶 ⇒ issue 05 消失」在**不改码**的前提下已由这两条对照锁定。**证据等级：已检查（源码链条完整）+ 已运行（q01/q15）；未实际改码。**
- **必须一起做的切割（这些是「不能盲并」的原因）**：
  1. **静态桶同时装着隐式共享的 `Const` 字段**：`SourceMemberFieldSymbol.vb:665-673`（注释 `:669` 逐字「const fields are implicitly shared and get into this list」）——只能搬「可 async 的那些」，不是整桶搬。
  2. **Date/Decimal 常量的 `.cctor` 走另一条路**：`CreateSharedConstructorsForConstFieldsIfRequired`（`SourceMemberContainerTypeSymbol.vb:3195-3211`）读的正是 `StaticInitializers`，由 `MethodCompiler.vb:614-639` 单独编译；`AnyInitializerToBeInjectedIntoConstructor` 另有 `IsConst` / `IsConstButNotMetadataConstant` 分流（`:2356-2376`）。
  3. **`Shared` 的 `Handles` 会强制共享构造器——但这条对提交类空转**：该代码在 `AddWithEventsHookupConstructorsIfNeeded`（`:2804-2886`，调用点 `:2509`）内，紧跟 `If TypeKind = TypeKind.Submission` 的**提前返回**（`:2805-2806`，分支体只有一行 `'TODO: anything to do here?`）⇒ **提交类（`.vbx` / REPL）根本走不到 `:2838-2886` 的 `EnsureCtor`**（`:2845`、`:2879`），且 `Handles` 在提交类里本就不受支持（`spec:102`、`:348`）。只有**非提交脚本类**（`DeclarationKind.Script`、`TypeKind.Class`，`spec:98`）会走到。⇒ 对 `.vbx` / REPL 的切割而言**本条不成立**；锚点是真的，**适用性仅限非提交类型**。
  4. **调试信息的语法偏移按 `isShared` 选桶**：`SourceMemberContainerTypeSymbol.vb:3278-3301`（取 `StaticInitializersSyntaxLength` / `InstanceInitializersSyntaxLength`，`:3295`）与 `:3242-3258`（`IsScriptClass AndAlso Not isShared` 分支）。挪桶必须让「桶」与「方法 `IsShared`」保持一致。
  5. **`AddInitializer` 的 `aggregateSyntaxLength` 要传对应桶的长度**（`SourceMemberContainerTypeSymbol.vb:1565-1585`；`FieldOrPropertyInitializer.vb:33`）。
  6. **`beforefieldinit` 会随 `StaticInitializers` 变空而翻成 False**（`Emit\NamedTypeSymbolAdapter.vb:451-511`）；**无 `.cctor` 时该标志无副作用**（已检查）。
  7. **（断言级，比第 ① 条更硬）两个桶的 span 累加语义不对称**：静态桶的断言**跳过元数据常量**、实例桶**无条件全加**——`SourceMemberContainerTypeSymbol.vb:1523`（`If(Not i.IsMetadataConstant, i.Syntax.Span.Length, 0)`）vs `:1524`（`i.Syntax.Span.Length`，无筛选）；而两个桶共用的 `AddInitializer`（`:1565-1585`）**只有一条**累加规则，且是**跳过**元数据常量（`:1581-1584` + 注释 `:1578-1580`「Other constants do not need field initializers」）。⇒ **把隐式共享的 `Const`（元数据常量）误搬进实例桶，`:1524` 的断言立刻炸**（累加值小于桶内实际 span 之和）。第 ① 条说的是「不能整桶搬」，本条说明**误搬会被断言当场抓住**——切割时必须按 `IsMetadataConstant` 逐条筛，不是按 `IsConst`。
- **代价**：中——改动落在**声明收集期**（分桶）而不是绑定期，牵动上列 6 个约束点；且**用户可见的时机语义会变**（下表）。
- **时机语义变化（机制实锤 / 表现未跑 ⇒ 推测）**：今天共享初始化器在隐式 `.cctor` 里（`beforefieldinit`，首次访问类型时触发、每类型一次）；改道后每次 `<Initialize>` 都会执行（与实例字段语义一致）。逐条待实证项：**源码顺序在前的顶层语句读顺序在后的 `Shared` 字段**（今天可能读到已初始化值，改道后必读默认值）、**同一 `Script` 重复运行**（今天 `.cctor` 不重跑 ⇒ 共享字段不复位；改道后每次重赋值）、**失败提交**（宿主只重建构造器、不跑用户代码）、**跨提交读前序提交的共享成员**（走另一个类型的静态访问，正常路径不受影响）。**兼容性不是否决理由**：今天 `Shared` + 初始化器**必崩**（issue 05，实测），没有在用语义可破坏（D6，且此处连「既有语义」都不存在）。
- **与 C# 的关系：这是「有意分叉」，必须给理由**（D5 的判据是「C# 这么做**且 VB 没有对应理由**才是」）。C# 明确把静态初始化器钉在同步的 `.cctor`（`SynthesizedStaticConstructor.cs:247-253`，且测试注释写明「must be handled synchronously in the .cctor」），**不做** async 静态初始化器。可用的分叉理由（propose 阶段列出，**裁决在 meeting**）：①提交类的「静态」不是 CLR 意义上的进程级静态——每个提交是**新类型**、宿主**逐提交 await**，丙 不引入新的观察窗口；②`spec:176` 早已**无 `Shared` 限定词地**承诺「字段/属性初始化器是 async 上下文」⇒ 丙 是**兑现承诺**，乙/甲 是**收回承诺**，前者更符合「少留移植缺口」；③丙 同时消掉 issue 05，收益大于代价。**反面理由**：与 C# 形状不同，规范要多写一句，且会让「`Shared` 字段什么时候初始化」在 VB 与 C# 里不同。
- **不解决什么**：issue 08（共享成员隐式 `Me` 不报 BC30369）、issue 09（诊断不 gate 发射）、以及 BC30469 那条面——**共享字段初始化器仍读不到宿主对象/前序提交变量**（`TryBindInteractiveReceiver` 的门槛是**引用点成员**的共享性，`Binder_Expressions.vb:2611-2630` 的 `:2612`；实测 REPL 里 `Shared Dim s1 = Args` 报 BC30469）。要让共享字段初始化器与实例字段**完全**等价，这是第二个必须动的点。

### 丙-2：新造一个共享版 async 初始化器（同向但更贵）

- **候选内容**：另造一个**异步的共享**初始化器方法，由入口点在调用 `<Initialize>` 之前/之后显式调用。
- **机制通，但顺序语义必破**：两个独立方法之间的相对顺序**在 `spec:64` 的「单一序列」模型里无法表达**，实现里也没有机制——入口点的体是固定步骤（`<Factory>`：建实例 → 调 `<Initialize>` → Return，`SynthesizedEntryPointSymbol.vb:340-389`）。要表达「第 3 行的 `Shared` 字段初始化器在第 5 行的顶层语句之前」就得改写规范句子；实际只能退化成「共享初始化器整体先于/后于顶层语句」。
- **要改三处**：①`SynthesizedEntryPointSymbol` 的 `<Main>` / `<Factory>` body（加顺序）；②`<Factory>` 的返回类型与宿主契约（若要维持「返回 `Task(Of T)`」被 `Func(Of Object(), Task(Of T))` 消费，两个 task 必须合成一个，否则 `ScriptBuilder.cs:161-163` 的绑定形状要一起改）；③跨提交可见性依赖「前序提交的初始化已完成」，必须与宿主逐提交 await 的次序绑定并写进规范。
- **代价**：高；**收益与 丙 相同**（都让共享初始化器可 `await`），所以除非 丙 被证伪，没有理由选它。
- **状态**：**保留但不主推**。

### 候选关系（重排后的裁决要点）

- **丙 与 甲 不是「都要做」的关系，而是「丙 优先」**：丙 让 `StaticInitializers` 不再承载这些初始化器 ⇒ issue 05 自动消失 ⇒ **甲 只在 丙 被否时才需要**。若 meeting 选 丙，建议把 甲 记为「丙 被否时的备选」，而不是并行实施（并行会让两套机制同时存在）。
- **乙 的地位同样取决于 丙**：
  - **丙 采纳** ⇒ 顶层 `Shared` 字段/属性初始化器里的 `Await` **合法**，本场景**不需要**乙。此时要修的是 issue 09（嵌套类型那条：诊断报了但不 gate 发射）——那是**独立缺陷**，与 `Shared` 无关，**不能**用乙替代。
  - **丙 被否（只做 甲）** ⇒ 共享构造器仍非 async，`Await` 放不进去（甲 拆出的符号同样是普通 `.cctor`，C# 的同款符号 `IsAsync = False`，`SynthesizedStaticConstructor.cs:247-253`）⇒ 此时 乙 是**唯一**能把「崩」变成「报错」的修法，且「崩→报错」就是本场景的目标。
- **对旧表述的更正**：①本提案早期版本写过「甲与乙不可互相替代、06 只能从崩改成报错」——**该表述已作废**，按本条重写；②issue 06 与其登记行里「若按 D5 拆出独立的静态构造器符号，本条的落点会更清晰」仍然成立（落点更清晰），但**不能**读成「拆了就能支持 `Await`」；③真正决定 06 目标是「支持」还是「报错」的是 **丙 的取舍**，不是构造器符号的拆法。

### 与 C# 的对照（Alternatives 的对照基准）

| 维度 | C# | VB（现状） | VB（甲 之后） | VB（丙 之后） |
|---|---|---|---|---|
| 实例提交构造器 | `SynthesizedSubmissionConstructor`（有参，只做提交登记） | `SynthesizedSubmissionConstructorSymbol`（有参，只做提交登记） | 不变 | 不变 |
| 共享/静态初始化器载体 | `SynthesizedStaticConstructor`（无参、`MethodKind.StaticConstructor`、非 async） | **复用** `SynthesizedSubmissionConstructorSymbol(isShared:=True)`（**有参**、`MethodKind.SharedConstructor`） | 无参的共享 `SynthesizedConstructorSymbol`（仍是非 async `.cctor`） | **不造**任何共享构造器，初始化器进 `<Initialize>`（async） |
| 静态/共享初始化器里的 `await` | 绑定期 CS8100（`Binder_Await.cs:160-171`） | 绑定期放行 ⇒ 发射期崩（issue 06 面孔 ①） | 不变（仍崩）⇒ 须 乙 | **合法**（由 async `<Initialize>` 消化） |
| 实例初始化器里的 `await` | 合法（进 async `<Initialize>`） | 合法（实测 q18、q29b） | 不变 | 不变 |
| 初始化器执行时机 | 静态：类型初始化（`.cctor`，`beforefieldinit`）；实例：构造/初始化器 | 同左（共享走 `.cctor`） | 同左 | **两者都进 `<Initialize>`**（与 C# 有意分叉） |
| 「诊断报了但不 gate 发射」（嵌套类型） | 不适用（CS8100 在绑定期且发射被门住） | **崩**（issue 09） | 不变（须独立修） | 不变（须独立修） |

## Unresolved questions
[unresolved]: #unresolved-questions

1. **丙 与 甲 的取一。** 丙 让 issue 05 自动消失，因此 甲 只在「丙 被否」时才必要（见「候选关系」）。meeting 需要裁的是：**愿不愿意为「共享初始化器可 `await`」付一次有意分叉的代价**（分叉理由候选见 丙 节）。裁 丙 ⇒ 甲 转备选；裁 甲 ⇒ 必须同时裁 乙（否则 06 仍是崩）。
2. **丙 的用户可见时机语义（4 项，机制实锤但表现未跑）。** ①源码顺序在前的顶层语句读顺序在后的 `Shared` 字段；②同一个 `Script` 重复运行（共享字段是否随 `<Initialize>` 重赋值）；③失败提交（宿主只重建构造器、不跑用户代码）；④跨提交读前序提交的共享成员。**今天无法构造对照组**（该形状必崩），故只能按机制推断，plan 阶段需先造出可观测差异的实验。
3. **`Shared` 字段初始化器与宿主的最后一段距离：BC30469。** 即使 丙 采纳，共享字段初始化器**仍读不到宿主对象/前序提交变量**（`TryBindInteractiveReceiver` 的门槛是**引用点成员**的共享性，`Binder_Expressions.vb:2612`；实测 REPL `Shared Dim s1 = Args` → BC30469）。要让共享字段初始化器与实例字段**完全**等价，须改这条判据（改成按被引用成员的共享性、或按初始化器的实际落点判定）——**本提案不纳入范围，但规范必须写出这条限制**（属 `spec:176` 那片边界）。`spec` 要写的是「共享初始化器的表达式里，隐式接收者只有静态面」。
4. **CS8100 的移植形状（C# 侧事实已钉死，VB 侧形状待裁）**：C# 判据只列 `SymbolKind.Field`（`Binder_Await.cs:160`），但属性初始化器以 **backing field** 身份进判据（`SourceMemberContainerSymbol.cs:5917-5924`、`FieldOrPropertyInitializer.cs:18-20`，本提案已复核）⇒ **静态属性同样命中 CS8100**。VB 的属性初始化器 `ContainingMember` 是 `PropertySymbol`（不由 backing field 承载），所以乙 若采纳，判据要么同时看 `Property`、要么改到「初始化器所属符号」的公共形状上。**若 meeting 选 丙，本条降为「仅在丙被否时相关」。**
5. **`Shared` 初始化器崩在 Release 下是什么样（含 issue 09）。** 本提案全部实测在 Debug（`EXITCODE=35` 来自 `Debug.Assert`）；Release 下 `Debug.Assert` 被编译掉，是否继续发射、是否变成别的症状（NRE / 静默产生错误产物 / 正常）**未测**。issue 06 / 09 的「未复现」也各列了同一条。
6. **顶层表外形式（`Event` / `WithEvents` / `Property`）的规范地位。** 解析器接受它们、声明表把它们塞进脚本类（`DeclarationTreeBuilder.vb:175-198`），但 `spec` 的四形式映射表未列（`:11-18`）且两处主张「穷尽」（`:56`、`:273`）。需要裁定：是「受支持形状、要补进映射表」，还是「不受支持、要报诊断」。issue 07 只覆盖其中会崩的两个。
7. **issue 09 的修法（诊断为什么不 gate 发射）。** 本提案给出三条候选（报错时给节点标错 / 让发射门看见初始化器诊断 / 让 `GetInstance(template)` 复制诊断），各有影响面未清点项；且「除 `Await` 之外还有哪些初始化器绑定期诊断落进同一个洞」**未清点**。这条与 丙 无关，**必须独立修**，否则「嵌套类型的 `Await`」永远从崩进程开始。
8. **`spec:176` 那句「字段或属性初始化器是 async 上下文」的最终改法。** 若 丙 采纳 ⇒ 补「共享字段/属性的初始化器同样进异步初始化器，且其表达式里只有静态面可用」（与 Unresolved 3 配套）；若 丙 被否 ⇒ 补「共享字段/属性的 `Await` 报诊断（新码 vs 复用 `ERR_BadAwaitNotInAsyncMethodOrLambda` 待裁）」。两种改法的规范表述一并在此待裁。
9. **丙 之后静态桶剩下的两个消费方（`Handles` 与 Date/Decimal 常量）仍需逐一核。** ① **`Handles` 对提交类不适用**：`AddWithEventsHookupConstructorsIfNeeded` 对 `TypeKind.Submission` 在 `:2805-2806` 提前返回 ⇒ 提交类里 `Handles` **不**造共享构造器（`spec:102`、`:348` 同向；探针输出类型名为 `Submission#0`），故 `.vbx` / REPL 的切割不受它影响；**非提交脚本类**（`TypeKind.Class`）仍会被它造出共享构造器（`:2845`、`:2879`）。② Date/Decimal 常量那条路（`CreateSharedConstructorsForConstFieldsIfRequired`，`:3195-3211`）**仍读 `StaticInitializers`**，不随 丙 消失。⇒ 切割「只搬可 async 的那些」之后，这两处的输入集合必须逐一实证（**今天全是推测**）；切割口径见 丙 的第 ⑦ 条约束（按 `IsMetadataConstant` 逐条筛）。

## 全称主张自检

本文全文的全称主张逐条挂锚点或实测（举不出证据的已降级为推测或删除）：

| 全称主张 | 证据 |
|---|---|
| 「非共享路径已经与 C# 同形」（§1、Summary） | `MethodCompiler.vb:697-698`、`:1481-1484`、`:1488-1490` + C# `MethodCompiler.cs:661-664`、`:547-550`（承重行 `:549`）（已检查）+ 探针 q18 / q27b / q29b exit 0（已运行） |
| 「VB 侧没有任何等价的共享初始化器 `Await` 诊断（无）」 | 三段 grep 的**完整命中清单**（6 条 `ERR_BadAwait*` 全列 + `staticinitializer` 2 条 + resx 0 命中），见 §3 |
| 「凡由 `Shared` 引发的缺口，集中在『共享字段/属性的初始化器』与『共享成员里的隐式 `Me`』两个承重点」（§4 结论 1，已按 issue 08 收窄） | §4 表格逐行实测（两轮 47 条探针）＋嵌套类同形状对照（q23/q34/q35 exit 0，q16 因 `Private` 不可访问改用 q23）＋q38/q39 与 q37/q09 两组「提交类 vs 普通类」对照 |
| 「共享字段的读写都不要求宿主方法共享，故并入 `<Initialize>` 在发射层合法」（丙 的承重前提） | 赋值侧 `LocalRewriter_FieldOrPropertyInitializer.vb:47-54`（`If Not initializedSymbols.First.IsShared Then` 才造 `Me`）＋ 读取侧 `CodeGen\EmitExpression.vb:669-688`（`EmitFieldLoad` 按 `field.IsShared` 分派静态/实例装载，`:674` 只在非共享时碰接收者）（均逐行已检查） |
| 「丙 的宿主契约零改动」 | `SynthesizedEntryPointSymbol.vb:340-389`（`<Factory>` 体固定两步，逐行）＋ `GetSubmissionInitializer` 两处命中**都是定义、零消费点**（本提案独立 grep）（已检查） |
| 「BC36937 报了但不 gate 发射」 | `MethodCompiler.vb:1256-1257`/`:1272`/`:1315` ＋ `BindingDiagnosticBag.vb:50-52`（`GetInstance(template)` 只复制两个布尔）逐行（已检查）＋ 判别性实测：v1–v3 零诊断崩 vs v4/v5 打印 BC36937（已运行） |
| 「C# 的静态属性初始化器同样命中 CS8100」 | `Binder_Await.cs:160` ＋ `SourceMemberContainerSymbol.cs:5917-5924` ＋ `FieldOrPropertyInitializer.cs:18-20`（本提案已复核；**未实测**，本树无 C# 运行环境） |
| 「C# 『为什么禁止而不合成』仓内只有一条书面依据」 | `CodeGenAsyncTests.cs:8623-8625`（唯一命中，`grep "synchronously in the .cctor"` = 1）；**「为什么」本身未找到 ⇒ 动机降级为推测** |
| 「C# 的静态初始化器构造器无参、非 async，且与提交构造器是两个类」 | `SynthesizedStaticConstructor.cs:12-21`、`:72-86`、`:191-197`、`:239-253`；`SourceMemberContainerSymbol.cs:5701-5719`（已检查） |
| 「C# 的提交构造器用空初始化器集编译，故实例初始化器不进 `.ctor`」 | `MethodCompiler.cs:661-664`（已检查，逐行读到空数组字面量） |
| 「CS8100 只在脚本类的**静态字段**初始化器上报」 | `Binder_Await.cs:160-171` 逐行（已检查）；消息与码号 `CSharpResources.resx:3831-3833`、`ErrorCode.cs:1335` |
| 「走 `GetAwaitInNonAsyncError` 加分支这条路不可达」 | `Binder_Expressions.vb:4740-4744`（`ElseIf Not IsInAsyncContext()`）与 `:4726-4727`（共享字段为 True）对照（已检查） |
| 「同一处「顶层 `Property` 后跟语句」是解析错误（BC30188）」 | 探针 q02/q19/q27/q29 exit 1（已运行）；**「为什么」未深挖 ⇒ 标注为推测**（§4 表末行） |
| 「三处 `Not …IsImplicitlyDeclared` 断言是同族可枚举的全部」 | `grep -rn "IsImplicitlyDeclared" Compilers\VisualBasic\Portable --include=*.vb \| grep -i assert` **命中 6 条**，其中 **3 条**是 `Not …IsImplicitlyDeclared`（`SourceWithEventsBackingFieldSymbol.vb:66`、`SynthesizedEventAccessorSymbol.vb:495`、`SynthesizedWithEventsAccessorSymbol.vb:93`），其余 3 条是别的话题（`MethodCompiler.vb:1825`、`AnonymousDelegate_TypePublicSymbol.vb:26`、`SymbolExtensions.vb:426`）（已检查） |
| 「甲 / 乙 / 丙 / 丙-2 不影响 `PublicAPI.*`」 | `SynthesizedConstructorSymbol` 为 `Friend NotInheritable`（`SynthesizedConstructorSymbol.vb:14`）；其余改动在 `Friend`/方法体内（**文件面级判断**，未跑 PublicAPI 分析器 ⇒ 标推测） |
| 「丙 只搬『可 async 的那些』，不能整桶搬静态桶」 | `SourceMemberFieldSymbol.vb:665-673` 逐行（注释 `:669`「const fields are implicitly shared and get into this list」）＋ `SourceMemberContainerTypeSymbol.vb:2838-2886`（`Handles` 强制共享构造器）＋ `:3195-3211`（Decimal/Date 常量另路）（已检查） |
| **降级处理**：Release 下的行为（含 issue 09）、丙 的 4 项用户可见时机语义、`IsInAsyncContext` 消费者扩面、C#「为什么禁止而不合成」的动机、顶层表外形式的解析判据、丙 之后的三个静态桶消费方联动 | 分别见 Unresolved 5 / 2 / Alternatives 乙 / 剪枝表末行 / Unresolved 6 / Unresolved 9，正文均已标「推测」或「未核」 |

## 相关文档

- `issues\issue-submission-shared-field-initializer-typeload.md` — issue 05（共享字段初始化器 → `TypeLoadException`）；本提案 §2 是其根因链的复核与边界更正（数组上界形状）
- `issues\issue-script-shared-field-await-initializer-crash.md` — issue 06（共享字段初始化器 + `Await` → 断言终止）；本提案 §3 把其「推断」升级为实测（`MethodCompiler.vb:1673` 是非 async 分支）
- `issues\issue-submission-implicit-type-member-asserts.md` — **本提案新登记**：顶层 `Event` / `WithEvents` 落在提交类 → 断言终止（与 `Shared` 无关）
- `issues\issue-submission-shared-member-implicit-me.md` — **本提案新登记**：提交类的共享成员不报 BC30369（隐式 `Me` 到实例成员）→ 运行期 `InvalidProgramException`；根因是 `Binder_Expressions.vb:2257-2270` 的「No code in a script class is shared」前提
- `issues\issue-initializer-diagnostic-does-not-gate-emit.md` — **本提案新登记**：初始化器里的 BC36937 报了但不阻止发射 → 嵌套类型（与 `Shared` 无关）的字段/属性初始化器 `Await` 仍崩
- `tmp\meetings\script-extension-methods\investigation-shared-initializer-async.md` — 专项调查报告（共享初始化器 async 化）；本提案 **amend 的依据**，其承重锚点已由本提案独立复核（见 §0，含一处锚点漂移更正）
- `spec\spec-scripting-dialect.md` — 脚本声明/提交模型（本提案 §7a 逐句列出需改的 11 处行号）
- `decisions.md` **D5** — 「基础功能以 C#/csi 为蓝本」及其**已证实例 1**（提交构造器两类分立）；本提案 §5 是其两侧源码锚点的完整展开。按该文件的引用纪律，引用 D5 用节号不用行号
- `decisions.md` **D6** — 兼容性只对 GA 成立；本提案的候选裁决不因「改了既有语义」被否（但其余理由照旧适用）
- `upstream-merge.md` §2.6（顶层代码 / 脚本提交语义；**锚点全在宿主侧**，编译器侧无锚点）、§2.19 / §2.8（`Binder_Expressions.vb` / `Errors.vb` / `VBResources.resx` 的既有锚点）；本提案 §7b 给出登记面判定
- `Compilers\CSharp\Portable\Symbols\Synthesized\SynthesizedSubmissionConstructor.cs` / `SynthesizedStaticConstructor.cs` / `SynthesizedInstanceConstructor.cs`、`Symbols\Source\SourceMemberContainerSymbol.cs`、`Binder\Binder_Await.cs`、`Compiler\MethodCompiler.cs`、`Compiler\MethodBodySynthesizer.cs` — C# 对照基准（§5）
- `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb` / `SourceMemberFieldSymbol.vb` / `SynthesizedSubmissionConstructorSymbol.vb` / `SynthesizedConstructorSymbol.vb` / `SynthesizedEntryPointSymbol.vb`、`Symbols\SynthesizedSymbols\SynthesizedConstructorBase.vb` / `SynthesizedMethodBase.vb`、`Symbols\Source\SynthesizedInteractiveInitializerMethod.vb` / `ImplicitNamedTypeSymbol.vb`、`Symbols\MethodSymbol.vb`、`Compilation\MethodCompiler.vb`、`Binding\Binder_Expressions.vb` / `Binder_Initializers.vb` / `BindingDiagnosticBag.vb`、`Lowering\LocalRewriter\LocalRewriter_FieldOrPropertyInitializer.vb` / `LocalRewriter.vb` / `LocalRewriter_PreviousSubmissionReference.vb` / `LocalRewriter_HostObjectMemberReference.vb`、`Lowering\AsyncRewriter\AsyncRewriter.vb`、`CodeGen\EmitExpression.vb` / `EmitConversion.vb` — VB 侧承重锚点（§1–§5、Alternatives）
- `proposals\proposal-scripting-dialect.md` — 脚本方言提案（本提案只写 `Shared` 成员的差异面，不重复其模型）
- `proposals\proposal-with-events-in-submissions.md` — 提交里的 `WithEvents` / `Handles`（与 issue 07 同片边界但触发点不同，见 issue 07「关联」）
