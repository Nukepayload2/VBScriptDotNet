# Visual Basic Language Design Meeting
September 13, 2026

议题是 `proposal-top-level-implicit-shared`——**脚本/提交类的顶层成员默认按 `Shared` 处理**，把 `spec\spec-scripting-dialect.md:60` 的「顶层 `Sub`/`Function` 默认是实例成员」这半句反过来。这不是一条新语法，而是一次**声明语义的默认值翻转**；它第三次来到我们面前：第一轮在「容器机制上是 class／心智是 module」的错配里给同一方向留了 `Table`，第一次聚焦复会拿到一份把机制重做一遍的修订稿（初始化器**留在**异步初始化器、按源码序），我们逐条裁定后仍维持 `Table`，并写明了复活门槛。

**这一轮是第二次聚焦复会，不是新一轮全量评审。** 回来的是同一方向的又一份 delta：它对上一轮列的六条代价**逐条否认**，带来一条方向级论据（「csi 这套模型有缺陷」）与三条新证据（A：C# 自己的 `top-level-members` 提案；B：`Static` 是 VB 的局部声明修饰符；C：csi 的现状），并把五项待裁留给复会。我们按复会纪律办了两件事：两条独立路径各自就 delta 重新裁决（一条沿 VB 基因、一条沿 C# interop），只读 delta 相关材料；我们把两份意见融进本纪要，**原地重写**受影响的章节，而不是在旧记录后面贴补丁。**RESOLUTION 是会议的产物、也是唯一的权威**——delta 里写下的答复，在本纪要里一律只当输入，不当论据；每一条都以设计质量独立重判。「产品不发布 API 版本」一类的**分发决定**，我们不把它当事实陈述使用。

两条路径本轮独立收敛到同一组结论，而且比上一轮更硬，可以三句话说清：

- **事实层是干净的。** delta 新引的 C# 提案原文、VB 规范原文，我们逐字对过，**没有一处错**——证据 A 的七处 `文件:行号`、证据 B 的三条逐字、以及「`Static` 在成员层非法」这个事实判断，全部成立。这一点我们记正账。
- **但六条代价一条都没有被消解。** 其中两条的否认打错了对象（顶层过程内的 `Static` 局部、能力删除）；一条用产品决定代替设计论证（跨运行重置）；两条根本没有回应（规范偏离、顶层 `Property`/`Event`/`WithEvents` 的地位）。六条里只有「宿主对象」一条是正面认下的，而认下不等于定价。
- **新证据 A 被两路独立读成「分叉」，不是「同形」**，而且它示范的恰恰是**另一条容器**——C# 在做同类事时**新造**了一个 `static` 且 namespace 作用域的隐式类，而不是把一个**已存在、可实例化**容器的默认值翻过来。C# 侧官方记录里还有一句话 delta 一字未引：这条提案的 LDM triage 逐字写着「**by no means is there agreement on the feature at this point**」。

同时我们要**当面认领两笔上一轮漏掉的账**：证据 A 在「隐式 `static` + 显式修饰符被禁」这一轴上**确实与本设计同形**，上一轮 VB 路径只从「`Module` 的共享性来自不可实例化」这一面看它，漏了；另一笔是 C# 侧本轮的主场发现——C# 对「**类型级字段 + 异步初始化器**」有**显式拒绝**规则（CS8100），而这正是本设计要的默认形态，且它与条件 ①②③ 的顺序保证**不可兼得**。

## Agenda

* [Proposal: 脚本/提交类顶层成员默认 `Shared` / Implicitly Shared Top-Level Members](#proposal-脚本提交类顶层成员默认-shared--implicitly-shared-top-level-members)

## Proposal: 脚本/提交类顶层成员默认 `Shared` / Implicitly Shared Top-Level Members

_Related: [`../../proposals/inactive/proposal-top-level-implicit-shared.md`](../../proposals/inactive/proposal-top-level-implicit-shared.md)；被改写的语义 `../../spec/spec-scripting-dialect.md`（`:15` / `:16` / `:22` / `:35` / `:48` / `:58` / `:60` / `:64` / `:66` / `:273` / `:277` / `:300` / `:302` / `:311` 等）；前置依赖 `../../proposals/proposal-submission-shared-members.md` 与 `../meeting-submission-shared-members.md`（甲 + 乙 已裁定采纳、丙 否决）；同片区域的姊妹裁决 `../meeting-script-extension-methods.md`（方向 B「顶层成员默认共享」维持 Table）与 `../../proposals/proposal-script-extension-methods.md`；缺陷登记 `../../issues/issue-script-top-level-extension-method-crash.md`（issue 04）、`../../issues/issue-submission-shared-field-initializer-typeload.md`（issue 05）、`../../issues/issue-script-shared-field-await-initializer-crash.md`（issue 06）、`../../issues/issue-submission-implicit-type-member-asserts.md`（issue 07）、`../../issues/issue-submission-shared-member-implicit-me.md`（issue 08）、`../../issues/issue-initializer-diagnostic-does-not-gate-emit.md`（issue 09）；对照提案 `../../csharplang/proposals/top-level-members.md`（champion issue `dotnet/csharplang#9803`，**未实现**）与其 LDM 记录 `../../csharplang/meetings/2025/LDM-2025-12-17.md`；byref-like 既有定案 `../../spec/spec-byref-like-safety.md`（`:107` / `:237`）与 `../meeting-byref-like-repl-safety.md`（`:91`）；决策 `../../decisions.md` **D5**（基础功能以 C#/csi 为蓝本、须说明分叉理由）与 **D6**（兼容性约束只对 GA 成立，且**只**解掉兼容性一条）；上游合并面 `../../upstream-merge.md`_

> **来源标注**：正文引用的源码与规范文本均逐行复核（`文件:行号`）；前两轮的承重锚点（规范 `:16`/`:48`/`:58`/`:60`/`:64`/`:66`/`:90-91`/`:131`/`:137`/`:163`/`:165`/`:169`/`:176`/`:234`/`:243`/`:311`、字段与属性的初始化器分桶、提交构造器分叉、入口点合成条件、绑定门槛、扩展方法两谓词、受限类型判据、发射层读写两侧、宿主契约与公开 API）继续有效，本轮不逐条复述。**本轮新增复核的锚点**是六个簇：C# `top-level-members` 提案全文与其 LDM 记录、`LDM-2020-01-22` / `LDM-2020-07-13` / `LDM-2021-05-12` 的场景记录、`Binder_Await.cs:158-172` 与 `ErrorCode.cs:1335`、`Script.cs:361-369`/`:467`/`:480-491` 与 `CommandLineRunner.cs:238-269`/`:271-294`、`ScriptTests.vb:135-148`/`:364-372`/`:375-384`、`statements.md:329`/`:337`/`:341` 与 `type-members.md:551`；全部写在正文相应位置。

> **并入基线的上一轮实测**（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`，自报 `2.0.0-Beta+5816a5c`，探针写在系统临时目录、用后已删净、未写入仓库跟踪目录）：①分支/循环体内的 `Dim` 是**局部**（`BC30451`）⇒ 不参与字段分桶；②顶层语句写在 `Dim x = 5` 之前读 `x` 得 `BEFORE=0`/`AFTER=5`（`spec:64` 源码序今天成立）；③`Return` 守卫之后的顶层 `Dim x = Probe()` 不执行（`INIT RAN` 不输出，`EXIT=0`）；④同形状写成 `Shared Dim` ⇒ `TypeLoadException` `EXIT=34`（issue 05，栈到 `ScriptBuilder.cs:194`）；⑤`Option Explicit Off` 下 `x = 10` + `Dim x = 5` ⇒ 绑到**后面那个顶层字段**（`P1 x=5`）；⑥`Option Explicit Off` + 模块内 `y = 10` + `Dim y = 5` ⇒ **BC32000**；⑦顶层 `Function F()` 写在调用之前 ⇒ `P3 7`（方法一侧今天无顺序要求）；⑧`Option Explicit Off` 下无 `Dim z` ⇒ `P4 z=10`（隐式声明通路是活的）。同工具另并入：`Dim x = 5` 产出的字段是 `pub=True|static=False|initonly=False`；`Shared Sub` 体内 `Print` ⇒ **BC30469**；`Shared Sub` 内 `Static k` 两次调用得 `t=1`/`t=2`。csi 侧的两行对照（`static int a = 5` 读到 `5`；带守卫时不读该字段则初始化器一次都不跑）由复会前的一轮在本机实跑（`5.10.0-1.26380.3`），本轮作为 C# 侧「类型级字段的初始化器不受 `<Initialize>` 源码序支配」的对位基线保留。

> **本轮实测（P1–P10，同一二进制；探针写在 `tmp\extprobe\` 下、用后已删净）**：

| # | 形态 | 实测 | 读者 |
|---|---|---|---|
| 1 | 顶层 `Static x As Integer = 5` + `Console.WriteLine` | `error BC30235`（`ERR_BadDimFlags1`，`Errors.vb:254`），`EXIT=1` | **顶层（成员层）不能写 `Static`，这是现状**——delta 该句在事实层成立 |
| 2 | `Shared Sub S()` 体内 `Static k As Integer = 0` / `k += 1`，调用两次 | `k=1` / `k=2`，`EXIT=0` | **顶层过程体内的 `Static` 局部今天合法**——它才是代价 #1 的对象 |
| 3 | `Module M` 内 `Shared Sub F()` | `error BC30433`（`ERR_ModuleCantUseMethodSpecifier1`，`Errors.vb:361`），`EXIT=1` | 模块对「冗余 `Shared`」的既有裁法是**报错**（方法侧） |
| 4 | 顶层 `Sub S()` 体内 `Static k`，调用两次 | `k=1` / `k=2`，`EXIT=0` | 实例形状同样合法（与 P2 成对，隔离「是不是 `Shared` 才合法」） |
| 5 | 顶层 `Property P As Integer` + 随后 `Console.WriteLine(...)` | `error BC30188`，指向**第 2 行语句**，`EXIT=1` | 顶层 `Property` **能作为成员被解析**，且**会顶掉其后的顶层语句区** |
| 6 | `Module M` 内 `Shared Dim x As Integer = 5` | `error BC30593`（`ERR_ModuleCantUseVariableSpecifier1`，`Errors.vb:456`），`EXIT=1` | 模块对「冗余 `Shared`」的既有裁法是**报错**（字段侧） |
| 7 | 顶层 `Property P As Integer` + `Dim q As Integer = 1` | `EXIT=0`（无输出） | `Property` 之后**声明**合法 ⇒ P5 的失败不是「`Property` 本身非法」 |
| 8 | 顶层 `Property P As Integer` + `Sub S()` + `S()` | `P8 S`，`EXIT=0` | 语句在 `Sub` 之后合法（与 P5 成对），隔离「是不是所有成员声明都顶掉语句区」 |
| 9 | 语句 → `Property P As Integer` → 语句 | `error BC30188`，只指向**第 3 行**，`EXIT=1` | 语句写在 `Property` 之前合法、写在其后**被拒** ⇒ 是 `Property` **关闭**了顶层语句区 |
| 10 | `Dim x As Integer = 1` + `Console.WriteLine(x)` | `P10 1`，`EXIT=0` | 控制组：`Dim` 不会顶掉语句区 |

### 场景与缺口

提案要消掉的那个别扭是真的：今天要写一个顶层扩展方法，必须手写 `Shared`，而「顶层的东西天然是共享的」是很多写脚本的人的第一直觉；实现上顶层代码装在一个类里，成员默认实例。我们复核了这条路的**现状形状**，它比「别扭」更重一层：`SourceMethodSymbol.vb:1504` 与另一处同形的 `:1634` 是两处 `Debug.Assert(Me.IsShared)`，而**两处的守卫都不查 `IsShared`**（`:1500-1502` 只看 `MethodKind`、`AllowsExtensionMethods()`、`ParameterCount`；`:1627`/`:1630` 同族）。也就是说，一个不带 `Shared` 的顶层 `<Extension>` 成员走到的不是「写起来啰嗦」，而是**一条断言终止的路**——按提案自己的登记，这是 issue 04，一条**诊断缺口**。这一点改变了整场讨论的量级：收益侧不是「省一个关键字」，而是「让一条会崩的路变成一条报错的路」。缺口还有一个更口语的形状：`spec:48` 把 `<Extension>` 只允许出现在标准模块或脚本类上（`NamedTypeSymbolExtensions.vb:108-111`），而标准模块里写 `Shared` 是**错误**，脚本类里却**必须**写——同一个概念在两个容器里给出相反的仪式要求。

问题在**用什么去消它**。第一次复会我们沿着源码把「默认共享」的每一格走了一遍，结论是**它买到的与它花掉的不是一个量级**，并指出了修订稿唯一的实质推进（条件 ①②③ 把 `beforefieldinit` 的时机坑移出用户语义）与它换来的新代价。这一轮 delta 没有换机制，换的是**论证方式**：它不再重述取舍，而是宣布六条代价不存在，并给出一条方向级论据来证明「这条路是 C# 自己也在走的」。所以本轮的裁决分三层：**先把六条答复逐条判完**（第一议程），**再判那条方向级论据与它带来的三条证据**（第二、三、四议程），**最后把新发现的 C# 侧反轴与两条未回应的账落定**（第五、六、七议程）。

### 复会第一议程：六条代价的答复，逐条判定

判定口径先说清：**成立** = 该代价在修订稿之下真实存在且未被消解；**不成立** = 给出一个**按构造成立**的答复把代价取消；**「接受」不是「不成立」**，是「代价成立但未定价」。按这个口径，六条**全部成立**——差别只在于答复的质量。

#### 代价 1｜顶层过程里的 `Static` 局部变成整脚本一份 —— 答复「顶层不允许 `Static`」

**事实层答复站得住（实锤），作为答复不成立。** delta 那句话在**成员层**是对的，我们本轮实测加证：`Static` 与 `Dim`/`Const` **同产生式**，是局部声明修饰符（`statements.md:329` 逐字「A local declaration statement declares a new local variable, local constant, or **static variable**. … *Static variables* are similar to `Shared` variables and are declared using the `Static` modifier.」；`:337` 逐字 `LocalModifier : 'Static' | 'Dim' | 'Const'`），在成员层它确实非法——**P1 实锤**：顶层 `Static x As Integer = 5` → **BC30235 `ERR_BadDimFlags1`**（`Errors.vb:254`），`EXIT=1`。它的字段侧对偶正是模块的 `ERR_ModuleCantUseVariableSpecifier1`（`SourceMemberFieldSymbol.vb:453-456` 逐字注释「implicitly Shared, **and cannot be explicitly Shared**」）。**这一格记正账。**

**但代价从来不是「顶层能不能写 `Static` 声明」，而是「顶层过程体内的 `Static` 局部」。** 那是一个**局部**，顶层过程体就是它的作用域：**P2 实锤**（`Shared Sub S()` 内 `Static k` → `k=1`/`k=2`，`EXIT=0`），**P4** 用实例形状成对隔离（同样合法），⇒ 合法性不取决于 `Shared`。机制侧只有**一个入参**会翻：`SynthesizedStaticLocalBackingField.vb:37` 逐字 `isShared:=implicitlyDefinedBy.ContainingSymbol.IsShared` ⇒ 顶层过程一律变 `Shared` 之后，`Static` 局部随之 **per-instance → per-type**（`statements.md:341` 逐字）。**代价按构造成立。**

**答复的对象错位。** 代价说的是「过程**体内**的局部」，答复说的是「**成员层**的声明」；delta 的反问（「你见过哪个非本地变量的地方能用 `static` 吗？」）本身就把两层混成一层——在 C# 里成员 `static` 满处都是，在 VB 里它的对应物叫 `Shared`；**「修饰符叫什么名字」不等于「语义域在哪」**。

**「顶层不允许 `Static`」这句话有三种读法，后果完全不同**，而 delta 一句都没有钉死（C# 路径提出的三读）：

| 读法 | 内容 | 是否新规则 | 对 C# 兼容面 |
|---|---|---|---|
| **(i)** 顶层的**成员声明**不能带 `Static` | 否 | 成立但**无信息量**（今天就不成立） |
| **(ii)** 顶层**过程体内**不允许 `Static` **局部** | **是**（新禁令 + 新诊断） | **三条路里最 C# 对齐**（C# 无 `Static` 局部概念），但它**不改存储类别**，只把 per-instance→per-type 这条轴逐出 `spec:277` 的适用范围 |
| **(iii)** 顶层过程不能声明为 `Static` | 否 | 无信息量 |

**承重读法是 (ii)**，两条理由：(a) 六条代价的第 1 条原文是「顶层**过程里**的 `Static` 局部」，答复必须落在过程体内；(b) 只有 (ii) 会让今天的合法写法改变命运——而**默认共享之后每个顶层过程都变成 `Shared`**，`Static` 局部要么变 per-type、要么被禁。**(ii) 是一条新语言规则**，它与 `DESIGN-v3.md:8` 的 D-b 逐字「按 module 上下文的既有语义判定……**不新立规则**」**自相矛盾**：v3 选的是「接受 per-type」，v4 若选 (ii) 就是从「接受」改成「报诊断」，即上一轮纪要第四议程列出的三条路里的**第三条**。**v4 既未说明改口，也未写另两条为何没走。** ⇒ **两种读法之下答复都不成立**：读成「成员层」是**错位**，读成「过程体内」是**自相矛盾**。**建议修正点**三条：①这条答复从设计里删掉，不要在纪要里留下「顶层不允许 `Static`」这样的句子（下一轮会被读成「本设计要禁止 `Static` 局部」）；②代价 #1 按上一轮第四议程的裁决记（per-type + 跨运行不回初值 + 与「陈旧值」并成一条 + 方言规范补一句）；③若真实意图是 (ii)，必须明写成新规则，走「从崩改成报错」的同形处置（已采纳的乙先例）、撤掉 D-b、补诊断设计（错误码归属）、并写明另两条路被考虑过。

#### 代价 2｜共享顶层状态不随运行重置 —— 答复「合理；`.vbx`/`vbi` 不触发；不是公开发布的 API 层」

**这是六条里最接近成立的一条，但三半里只有两半成立。代价成立，正确写法是条件句。** 我们逐行核了三条落点，并且按更精确的口径落笔：

1. **「重置是 API 层的」⇒ 成立（已检查）**：每次 `RunAsync` 都新建执行状态——`Script.cs:467` 逐字 `var executionState = ScriptExecutionState.Create(globals);`（新 state ⇒ **新实例**）；编译与执行器**缓存在同一个 `Script<T>` 实例上**——`Script.cs:361-369` 的 `_lazyExecutor` + `Interlocked.CompareExchange`。⇒ **同一 `Script` 重跑 = 新实例 + 旧程序集** ⇒ 类型级字段留上一次运行的值，实例字段回初值。这条同时把上一轮纪要 Follow-up 里「`Script.cs` 的编译/执行器缓存 = **转述**（未亲核）」**升为已检查（源码逐行，未运行）**。
2. **「`.vbx` 执行与 `vbi` 交互执行都不触发」⇒ 成立（已检查）**：`CommandLineRunner.cs:238-269`（`RunScriptAsync`）每次执行**新建** `Script` 并且只 `RunAsync` 一次（`:256`）；`:271-294` 起的 `RunInteractiveLoopAsync` 把 `state` 一路传下去，续提交走 `:368-379` 的 `RunFromAsync` 分支；全文件无 `Reset`/`state = null` 之类的重跑路径。⇒ **产品 CLI 里确实没有重跑同一 `Script` 的路径**，这半句我们认账。
3. **「不是公开发布的 API 层」⇒ 不成立作为事实**。`Script<T>.RunAsync` 落在 **`Scripting\Core\PublicAPI.Shipped.txt:76-77`**（**Shipped**，不是 `Unshipped`），`ScriptState.GetVariable` `:139`、`ScriptState.ContinueWithAsync` `:134-137`，`ScriptRunner<T>` 是 `public delegate`；更关键的是 `Script.cs:480-491` 的 `CreateDelegate` 文档逐字承诺「Creates a delegate that **will run this script from the beginning when invoked**」——「从开头重跑」是这个公开 API 的**明示能力**，不是意外行为。而 `ScriptTests.vb:135-148`（`TestScriptVariableSetValue`，**被跟踪的测试**）三条断言里的第二条逐字就是「**重跑回初值**」（`:143` = `Await state.Script.RunAsync()`，`:144` = `Assert.Equal(1, rerunState.GetVariable("x").Value)`）。⇒「这个场景不会被触发」「不是公开发布的 API 层」两句**与这条实锤冲突**。
4. **锚点订正（本轮）**：`ScriptTests.vb` 的干净锚点统一到 **`:135-148`**（`:135` = `<Fact>`，`:148` = `End Function`；三条断言落在 **`:141`（`Value` 可写）/`:144`（重跑回 1）/`:147`（续提交读到 2）**，重跑断言在 `:143-144`）。上一轮纪要里 `:136-149` 的写法会多算一行，本纪要就地改正。

**落笔口径**：一条**经公开 API 可达、且被跟踪测试钉住**的语义差异，是真实的设计差异；「我们不发布 API 版本」是**分发决定**，它可以让差异「不出货」，但不能让差异在设计上不存在——`decisions.md` D6 只解掉兼容性一条，这一格落在 D6 明文保留的「机制收益与代价」里；`DECISIONS.md` 上浮页也已经把「出货面上无从观察」判为**撤回**、把「不发布 API 版本」判为「**产品决定，不是事实**」。⇒ 这一条在本纪要里的标准写法是条件句：**「今天在产品 CLI 上不可达；在公开 `Script<T>`/`ScriptRunner<T>` 的既有契约上可达（`CreateDelegate` 明示可重跑），可达性由『产品是否发布 API 版本』这个产品决定决定」**；Drawbacks 里按「**同一 `Script` 对象重跑 ⇒ 类型级字段留上一次运行的值**」写实，并附一条回归用例；**不要**用「不发布 API 版本」给它打折。

#### 代价 3｜宿主对象要整体改共享、改动翻倍 —— 答复「接受，就是要改共享」

**成立（接受 ≠ 消解）。** 这是六条里**唯一**一条正面认下的，但按口径，「接受」是「代价成立但未定价」。落点**只有一格**：每提交合成的 **`<host-object>` 字段**——`SynthesizedSubmissionFields.vb:57` 逐字 `New SynthesizedFieldSymbol(… "<host-object>", accessibility:=Accessibility.Private, isReadOnly:=True, isShared:=False)`（**实锤**）。`Args`/`Print`/`ReferencePaths`/`SourcePaths` 是**宿主对象类型自己的实例成员**（`InteractiveScriptGlobals.cs:32-45`），它们**不需要**也不能「按 shared 处理」——v3 那句「看着像 shared function 的就按 shared 处理」**在实现上没有落点**，必须改写成**字段级**的改动。定价仍是上一轮核成的**四处改动 + 三处写入点**：

- **(a) 字段共享性** `:57` 的 `isShared:=False` → `True`；
- **(b) `isReadOnly:=True` 必须同时放开**（两者在 `:57` 同行；共享只读字段只在共享构造器上下文里可写，而宿主对象是运行时才到的；漏掉它会把 BC30469 换成另一个形状的编译错误，不是「悄悄写不进去」）；
- **(c) `Binder_Expressions.vb:2611-2612` 的条件不能整体放开**：`If currentType.TypeKind = TYPEKIND.Submission AndAlso Not currentMember.IsShared Then` **一个条件同时把守前序提交（`:2613-2614`）与宿主对象（`:2616-2625`）两个分支**，必须拆成「宿主对象不看共享性」与「前序提交仍要求非共享」两条；
- **(d) 降级侧**：宿主对象那一支要改成**无接收者的静态字段访问**——两处 `Debug.Assert(Not _topMethod.IsShared)`（`LocalRewriter_HostObjectMemberReference.vb:15`、`LocalRewriter_PreviousSubmissionReference.vb:16`）与它们依附的 `New BoundMeReference(...)` 都要处理；发射层已支持（`EmitExpression.vb:2193-2203` 的 `EmitFieldStore` 只按 `IsShared` 选 `stsfld`/`stfld`，`:1955-1963` 在共享时不压接收者）。
- **三处写入点**同在 `SynthesizedSubmissionConstructorSymbol.vb` 的 `MakeSubmissionInitialization`：`:76-82`、`:87-96`（宿主对象赋值，接收者是 `Me`）、`:105-117`；须处理的是第二处。

**D5 论证仍缺席，而且这一笔本轮更硬。** C# 在**同一位置**是「**实例** `readonly` 字段 + 真构造器赋值」（与本 fork 现状同形），并且有一条**主动的显式检查**：`Binder_Expressions.cs:2445-2483`（`TryBindInteractiveReceiver`）的入口条件含 `isInstanceContext()`，实现里逐字 `if (containingMember.IsStatic) { return false; }` ⇒ C# 刻意**不给** static 上下文合成宿主对象接收者，落到普通实例成员引用规则（CS0120 = `ErrorCode.cs:103` `ERR_ObjectRequired`）；VB 的 BC30469 与它同义。⇒ D-c **不是「补一个漏」，而是在 C# 有显式检查的那一轴上主动反向**，而 `decisions.md` D5 是成文落地约束，要求说明「为什么 VB 必须分叉」——delta 一个字都没有。C# 的宿主模型允许「同一个 `Script` 的两次运行各带自己的 globals」，改成类型级一份之后这个能力就没了。**两条后果要按范围写准，不能扩大**：并发/重入受影响的是**同一个 `Script` 对象的重复/并发运行**（每次 `RunAsync` 都新建实例并经构造器写那个静态字段），不是「所有并发」；`spec:361`（逐字「A failed submission does not change the state of the session」）要重验的正是「同一 `Script` 对象在提交失败后重跑，会话状态不变」这一格。**四处 + 三处写入点、`isReadOnly`、D5 论证、并发与 `spec:361` 这五件不补，我们转反对。**

#### 代价 4｜顶层永远不能声明实例成员（能力删除）—— 答复「不是删除，是归位；`Module` 今天就是这个行为」

**成立。应答按两条账合起来写：相对 `Module` 是归位，相对 csx 是删掉一条腿。** 「归位」是**换标签**，不是对「能力被移除」的否认。判定要从**相对今天**看：今天一个**不带 `Shared`** 的顶层 `Sub` **是实例成员**，它能碰宿主对象与实例状态（基线实测「`Shared Sub` 体内 `Print` ⇒ BC30469」的反面就是这个）；翻转之后，**顶层没有任何写法**能声明一个能碰实例状态的成员（除非嵌套一个 `Class`）。这是**能力的移除**，不是默认值的翻转。两条账分别如下：

- **相对 `Module` 是归位——但它丢掉了使那半句成立的前提。** `Module` 的隐式共享是从「**不可实例化**」推出来的：`types.md:689` 逐字「A *standard module* is a type whose members are implicitly `Shared` **and scoped to the declaration space of the standard module's containing namespace**… **Standard modules may never be instantiated.**」，而 `spec:48` 逐字否认脚本类具备这些：「A script class is an ordinary class **and not a standard module**: a standard module's members are implicitly `Shared` and the module can never be instantiated, whereas a script class **is instantiated**」。模块的第三条前提（**冗余 `Shared` 是错误**）本轮拿到两条新实锤：**P3**（模块内 `Shared Sub` → **BC30433**）与 **P6**（模块内 `Shared Dim` → **BC30593**）。⇒「归位」论者的两难：要么接受「不可实例化」（那么 `<Factory>`/`<Main>` 造实例、`<host-object>` 字段、前序提交引用全都要重新交代——**与条件 ①②③ 自相矛盾**），要么它就不是归位，而是**保留一个可实例化容器、同时拿掉它的实例成员面**。
- **相对 csx 是删掉一条腿，而谱系把 csx 配给了这一轴。** `spec:302` 逐字把 csx 认作「**the closest counterpart**: it also synthesizes a submission class, also keeps top-level state across submissions, and also chains submissions.」；`spec:311` 的对照表行**把两条腿都写着**：逐字「Top-level method | an instance member of the submission class, **or a `static` member if declared `static`** | an instance member of the script class, **or a `Shared` member if declared `Shared`**」。⇒ 删掉一条腿在这条轴上是**能力删除**，不是默认翻转；而 csx 里 `static` 是 opt-in **且有用户可见后果**（实例腿允许 `Await` 初始化、类型级腿不允许——见第五议程）。
- **证据 A 补不上这个前提，反而同源。** `<>TopLevel` 的静态性来自它是 **`static` class**（`top-level-members.md:62`），即**同一条 Module 前提**（该提案 `:122` 自己就把 VB 的 `Module` 称作「something similar」）；而且提案 `:102`/`:169`/`:183` 还在**开放**非 `static` 顶层成员（「Should we allow non-`static` members?」「keep our doors open if we want to introduce some non-`static` top-level members in the future」）⇒ C# 不但没删实例轴，还在考虑**加**一条。

**建议修正点**：设计里这句话应改成「相对 `Module` 是归位；相对 csx（谱系配给本轴的蓝本）是**删除一条腿**」，并就此补 D5。

#### 代价 5｜规范「只换容器、不换含义」要改成刻意偏离 —— 无单独回应，由本轮裁

**成立。处置只有一条：承诺不能删，但必须加具名例外。** 本条与兼容性无关，D6 明文只解掉兼容性一条，它落在 D6 明文保留的「规范的可表达性 / 与既有不变式冲突」一类里。**详细处置见复会第六议程。**

#### 代价 6｜顶层属性/事件/`WithEvents` 被牵连 —— 无单独回应，由本轮裁

**成立，而且被证据 A 反向加重。** **详细处置见复会第七议程。**

### 复会第二议程：证据 A —— C# 的对应设计是同形，还是分叉

**结论先行：两路独立判「不足以翻转 D5」，而且它示范的是另一条容器。** 我们先给 delta 记正账，再订正一处被高估的强度，然后逐轴对照。

**正账：引证逐字准确。** delta 在证据 A 上给的七处 `文件:行号`（`:7`/`:57` 成员种类、`:62` 隐式类 `static`+`partial`、`:64` 生成名 `<>TopLevel`、`:69` 禁止写 `static`、`:51-52` Motivation、`:122-139` 援引 VB `Module`、`:169` 开放问题）我们逐条对过，**无一处错**；证据 B 的三条逐字也准确。**这份 delta 的事实层是干净的。**

**但「提案、未实现」这个定性低估了它的信息量。** 镜像里**有这条提案的 LDM 记录**，而且是 triage 逐字：

> `InternalDevDocs\csharplang\meetings\2025\LDM-2025-12-17.md:68-74`（`#### Top-Level Members` 节，champion issue `dotnet/csharplang#9803` 同号）逐字：「There are some strong feelings on this one in the LDM; nothing that we think would hold us from doing a deep design dive on it, but **by no means is there agreement on the feature at this point**. We'll put it into the working set to have that debate.」

（两路 grep：`9803` 在 `meetings/2025` 与 `meetings/2026` 仅此一处命中，2026 年无后续记录；提案正文也没有 `## Adopted` 之类的落地标记。）⇒ **正确口径**不是「C# 自己的 LDM 正在做同一件事」，而是「C# 有一个 championed 提案，进了 working set 准备深挖，LDM 内部**明确尚未达成一致**」。「by no means is there agreement」这几个字，delta 一字未引。**这一条必须写进设计的定性句**，否则读者会把它读成设计共识。

**还要订正第二条：三种顶层模型是三个并列场景，不是演进的三级路。** C# 自己的 LDM 记录把它们写成彼此冲突的场景：

> `LDM-2020-01-22.md:15-21` 逐字列出三个 scenarios：「1. Simple programs are simple -- remove the boilerplate for Main / 2. Top-level functions. Members outside of a class. / 3. Scripting/interactive. Submission system allows state preservation across evaluations.」紧接着 `:23` 逐字「Unfortunately, some of these proposals interact in difficult ways.」，并在 `:31-32` 把问题写成一句原话：「If you write `int x = ...;` is `x` now a global mutable variable for the entire program? Or is it a local variable in a generated Main method?」

⇒ **C# 9 选的是场景 (1)**，答案在 TLS 里给出的是**局部**（`SimpleProgramBinder.cs:26-39` 把顶层语句里的变量收成 `LocalSymbol`）；**证据 A 是场景 (2) 的复活**，而 C# 当年刻意把它留在门外（`LDM-2020-01-22:52-54` 逐字「We think (2) is interesting and worth considering. It may not be the highest priority, but **we need to make sure we don't rule it out entirely**.」）；**本方言对应的是场景 (3)**，且这是谱系自身的成文认定（`spec:302` 逐字把 csx 认作最近对应物）。因此 delta 拿证据 A 支撑本方向，是**拿另一张脸的模型给自己作证**——而 `spec:300` 自己逐字写着「the answers must not be conflated」。

**逐轴对照（三列）。** 我们把 csx、`<>TopLevel`、本设计放在一张表里读：

| 轴 | csx（C# 脚本方言） | `<>TopLevel`（证据 A） | 本设计 | 读数 |
|---|---|---|---|---|
| 声明形式 | — | **新增**（命名空间级成员今天在 C# 里是错误） | **翻转既有形式的默认**（`spec:48`/`:58`/`:60`） | **不同类：加法 vs 改法** |
| 顶层状态存储 | 提交类**实例**字段 | 隐式静态类的**静态**字段（`:34` `string? cache;`） | 脚本类**共享**字段 | 与 csx 反向；与 `<>TopLevel` **同形** ← 唯一真收获 |
| 顶层过程 | 实例方法（写 `static` 才共享） | 静态方法；`static` **禁止写**（`:69`） | 共享方法；`Shared` **仍可写**（未裁） | **近似同形**（差别恰在冗余修饰符） |
| 实例轴 | 保留（实例腿 + opt-in） | **保留开放**（`:102`/`:169`/`:183`） | **删除**（无反向拼法；`spec:311` 的另一条腿没了） | **反向** |
| 容器前提 | 可实例化（跨提交链） | 不可实例化的 **`static` class**（`:62`） | 脚本类**被实例化**（`spec:48`） | **反向（前提不同）** |
| 顶层语句 | 进 `<Initialize>`（async 实例方法） | **不在此容器**（`:59` 逐字「those are not allowed inside namespaces」） | 顶层语句正是该容器的初始化器（`spec:64`） | **反向** |
| 宿主对象 | 提交类**基类型**实例成员 | 不存在 | 存在，且被强制共享化（D-c） | **反向** |
| 成员种类 | csx 全类成员面 | **5 类，不含** `Property`/`Event`（`:57`；二者只在 Open questions `:173` 待议） | **6 形，含**三者 | **反向（蓝本更窄）** |
| 状态跨运行 | 跨提交保留 | 无 submission 链 | 跨运行/跨提交一份 | **反向** |
| 顺序规则 | 字段语义（允许前向引用） | 字段语义（`:189-190` 逐字 `int M() => s_field; // ok`） | D-e 若采纳 ⇒ local 语义 | **反向；D-e 采纳则更远** |
| 类型级字段 + 异步初始化器 | 实例腿允许 `Await`；**类型级（`static`）腿报 CS8100** | 静态类，提案未讨论 `Await` | **默认形态**（条件 ①②③） | **反向（C# 显式拒绝的那一格）** |

⇒ **1 轴同形 + 1 轴近似同形 + 8 轴反向/不同类**。上一轮纪要 `C# 生态对照` 里「三轴反向、两轴同形、一同向」的读数在证据 A 之下**不翻转**：`<>TopLevel` 恰好把「顶层状态存储」那一格从『与 csx 反向』变成『与 C# 新增面同形』，但同时把**过程 / 实例轴 / 容器前提 / 顶层语句 / 宿主对象 / 成员种类 / 顺序 / 异步初始化器**八格推成反向。

**决定性那一格是分叉，而且 C# 的选择站在上一轮的结论那一边。** C# 在做同一件事时，走的是「**另起一个 `static` + namespace 作用域**的容器」（`:61` 新造隐式类、`:62` `static`、`:65` 「has the namespace in which it is declared in」、用点靠 `using NS;` ⇒ `using static NS.<>TopLevel;`），**不是「把已有可实例化容器的默认值翻过来」**。而这个分叉方向恰好**复现了 `Module` 的两条前提**（不可实例化 + 成员作用域落命名空间），第三条（冗余修饰符报错）在 `:69` 也做了。⇒ 证据 A **不解 (a)**（它示范了一条能同时满足 `spec:35`/`:273`/`:277` 的路子：另造容器、不动既有形式的含义——本方向不是「收敛」，是**选择了 C# 没选的那条容器**）；**(c) 更不利**（C# 拿到同类收益时**没有付代价**，因为它换了容器；本设计要拿到「免写 `Shared`」，代价是重写一门方言的声明默认值）；**(d) 不变**（它**完全不碰**脚本/提交模型，`:80-83` 用的是 `using static`，与 submission chain 无关 ⇒ 本设计真正对象——**提交类的顶层语义**——的蓝本仍然只有 csi）。

**一条结构分辨，我们认为是本轮最有价值的一句。** `spec:303` 把文件执行面写成「the top-level code is semantically a **`static async Task Main`**」。但同一面在 C# 里（TLS）顶层变量是**局部**——**局部处在静态方法体内，却既不是静态字段、也不是实例字段**。⇒「顶层在 static 上下文里，所以顶层成员应当共享」这一步，是把**方法上下文的静态性**偷换成**类成员资格的静态性**。C# 自己的两个模式都有反例：TLS 的 static `Main` 体给局部，`<>TopLevel` 的 static 类给静态字段——**答案由容器是否可实例化决定，不由「静态上下文」决定**。

### 复会第三议程：方向级论据与「csi 消亡」的地位

delta 的方向级论据是：「**csi 消亡，C# 允许 top-level code，就说明了 csi 这套模型是有问题的。……不光是用户要写莫名其妙的 `Shared`，而且还牵扯了定义和使用的顺序问题。**」我们分两层判。

**第一层：前提一「csi 消亡」标 `Suspect`，两路都不采信、也不否定。** 核实的范围**只在仓内镜像**（不使用仓外检索）：`InternalDevDocs\csharplang\` 全树（含 2013–2026 全部 LDM 记录、`proposals\`（含 `inactive\`/`rejected\`）、`spec\`、`Language-Version-History.md`）里，`deprecat|retire|sunset|discontinu|no longer support` 的命中**全部无关**（`Obsolete`/`Deprecated` 属性、restricted types 的 "sunset"、扩展方法迁移策略）；`csi|C# script|scripting|\.csx` 的命中里**无一条**退役或弃用决议。镜像**既不能证实、也不能否证**它。反过来，仓内有**两条方向相反**的在册记录，我们不把它们当否证，只用来削弱那句断言：

- `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:56` 逐字 `<InternalsVisibleTo Include="csi" />`（**实锤**，只证明**本 fork 同步点**上游仍把 csi 当设计消费者——它同时列着 `Microsoft.CodeAnalysis.CSharp.Scripting` 与 `vbi`）；
- `LDM-2020-01-22.md:48-50` 逐字「(3) is also important partly because there are a number of products and scenarios **currently using the scripting system**. We should keep that in mind to make sure that we don't prevent a large number of use cases from ever using the new system.」；
- `LDM-2020-07-13.md:191-193` 逐字「This is **part 2 of the work we started in C# 9** with top-level statements: designing free-floating functions that can sit on the top level without any containing type. We need to continue examining this context of **scripting unification**」＋ `:207-209` 逐字「It's worth noting that **CSX has a limited form of this support already**, and implementing this in C# proper would bring us **one step closer to unifying the two dialects**.」⇒ C# 的在册意图是 **unify，不是 retire**。

⇒ **「csi 消亡」本轮撤出承重位。**

**第二层：即使前提为真，也推不出结论——这一层才是我们的裁。** 五条理由，两路独立成立：

1. **面不对。** `spec:26` 与 `spec:300` 逐字列出三张 C# 对照**脸**（scripting `.csx` / 文件执行 / 交互打印），并逐字要求「Wherever a C# comparison appears below, the specification states which of the three faces it refers to.」以及「the answers must not be conflated」。C# 新增 top-level code 是**文件执行面**（`csharp-9.0\top-level-statements.md` 的语义 = `Program.Main`），而 csi/csx 是**脚本面与交互面**。**在一个面上加了能力，不构成另一个面的模型有缺陷。**
2. **C# 允许 TLS 的成文理由不是「脚本模型坏了」。** `LDM-2020-01-22:43-46` 逐字：「There's wide consensus that (1) is very useful. … It's also a very large learning burden in that just to write "Hello, World" requires explaining methods, classes, static, etc.」——是**学习曲线与去样板**，与脚本模型无关。
3. **官方「继任模型」的形态也不支持本方向。** C# 9 的顶层变量是**局部**（`SimpleProgramBinder.cs:26-39`），`csharp-9.0\top-level-statements.md:185-203` 整节标题就是「Scope of top-level **local variables**」，`:199` 还专门规定「在顶层语句之外求值到顶层局部变量要报错」以保护未来的 top-level functions 场景。⇒ 拿「C# 转向 top-level code」当支持，是**拿另一种模型给自己作证**（而 `DESIGN-v3.md:19-21` 自己说非交互态顶层变量要变 locals——那就更不该在**提交面**上先把字段翻成共享）。
4. **两个活的 C# 方向都不走这条路。** 把它们与证据 A 合起来看：C# 目前两个「顶层代码」方向分别是「`Main` 里的**局部**」与「**新造 static + namespace 作用域**的隐式类」。**没有一个**是「翻转一个可实例化容器的成员默认值」。⇒ 证据 A + 方向级论据合起来**给本方向减分**。
5. **delta 点名的两个「缺陷」都不需要翻默认值来修。** 「莫名其妙写 `Shared`」是 **issue 04**——`SourceMethodSymbol.vb:1500-1504` 与 `:1630-1634` 两处 `Debug.Assert(Me.IsShared)`，**守卫都不查 `IsShared`**（本轮复核：`:1500-1502` 只看 `MethodKind`/`AllowsExtensionMethods()`/`ParameterCount`）⇒ 形状是「**该报诊断却断言终止**」，**缺的是一条诊断**，不是缺一个默认值；「定义和使用的顺序问题」是 csx 的 `beforefieldinit` 语义，而**本设计自己的条件 ①②③ 已经把它拆掉了**，何况 C# 的「先用后声明」诊断**只对 `LocalSymbol`**（`Binder_Expressions.cs:2233`/`:2244`/`:2328-2340`）——csx 的字段从来没有这条问题。⇒ 这条论据**自相矛盾**：如果类型级存储会产出「顺序诡异」，那么把它设为**默认**正是本设计在引入那个来源。

**基准裁定：「以 csi 为蓝本」（D5）本轮不动。** 证据 C 核不了；即便核成，也推不出「模型有缺陷」；即便有缺陷，也推不出「要翻默认值」。**真正被动摇的不是基准，是动机①「extension method 支持」那条收益的定价**——它应该被降级为「补一条诊断」，issue 04 独立立项。另外，若「csi 正在边缘化」为真，它的逻辑出口是「**本方向该缩小到交互面**」（`spec:26` 的三张脸里，只有交互面是 csi 的直接对位），而不是「提交面该翻共享」。

### 复会第四议程：证据 B 与「顶层不允许 `Static`」的落法

证据 B **逐字准确**（`statements.md:329`/`:337`/`:341`，两路均复核），它证明的是：`Static` 在 VB 里是**局部声明**修饰符、与 `Dim`/`Const` 同族，**不是成员修饰符**。这一条我们认——它同时是代价 1 事实层的正账（P1 实锤：成员层写 `Static` 报 BC30235）。但**落法不能是 delta 那条答复**，理由在代价 1 里写全：答复对象错位（代价在**过程体内**，P2/P4 实锤合法），而「顶层不允许 `Static`」这句话若按字面承重就变成**一条新规则**，与 `DESIGN-v3.md:8` 的 D-b「不新立规则」自相冲突，并且需要它自己的诊断（照已采纳的乙「从崩改成报错」的形状），而 v4 既未申报改口、也未写明另两条路。⇒ **落法三条**：①这条答复从设计里删掉，不要在纪要里留下「顶层不允许 `Static`」这样的句子；②代价 #1 按第一议程的裁决记（per-type + 跨运行不回初值 + 与「陈旧值」并成一条 + 方言规范补一句）；③若真实意图是读法 (ii)（顶层过程体内禁 `Static` 局部），**明写成新规则**，撤掉 D-b、补诊断设计与规范登记、写明另两条路（接受 per-type / 保留实例语义）被考虑过。

### 复会第五议程：C# 侧的新反轴 —— 类型级字段 + 异步初始化器（CS8100）

**这是本轮最硬的一条 C# 侧发现，也是 C# 路径的主场，必须写进设计。** C# 的**脚本类**里，`Await` 出现在**字段初始化器**中时的态度是**分腿**的：

> `Compilers\CSharp\Portable\Binder\Binder_Await.cs:158-172` 逐字：
> ```
> case SymbolKind.Field:
>     if (containingMemberOrLambda.ContainingType.IsScriptClass)
>     {
>         if (((FieldSymbol)containingMemberOrLambda).IsStatic)
>         {
>             info = new CSDiagnosticInfo(ErrorCode.ERR_BadAwaitInStaticVariableInitializer);
>         }
>         else
>         {
>             return false;
>         }
>     }
> ```
> 错误码：`Compilers\CSharp\Portable\Errors\ErrorCode.cs:1335` 逐字 `ERR_BadAwaitInStaticVariableInitializer = 8100`（CS8100）。

语义是：**实例**顶层字段 ⇒ 初始化器落在 async `<Initialize>` ⇒ `Await` **合法**；**类型级（`static`）**顶层字段 ⇒ 初始化器**不在** `<Initialize>` ⇒ `Await` **报 CS8100**。这是 C# **刻意**把两条腿分开的一条规则。

**本设计要的正是这两者的跨腿组合**：字段**类型级**（`Shared`，条件 ①）＋ 初始化器**留在** async `<Initialize>`（条件 ②③）＋ **顺序保证**。而本仓已有**在册测试**正落在这个形态上：`ScriptTests.vb:375-384`（`TestTopLevelAwaitInStatement`，源码逐字 `Dim value = Await Task.FromResult(13)`——顶层 `Dim` 带 `Await` 初始化器，**正中该格**）；`:364-372`（`TestTopLevelAwaitReturnValue`，`? Await Task.FromResult(11)`）是同一族里走同一条 async `<Initialize>` 的**姊妹形态**。⇒ 本设计之下，`Dim value = Await Task.FromResult(13)` 会把「**类型级字段 + `Await` 初始化**」变成**默认形态**，而 C# 的对应模型对该组合给出**显式错误**。且它与条件 ①②③ 的顺序保证**不可兼得**：保留顺序保证就必然保留 `.cctor` 之外的 async 初始化器，就必然踩这一格；要对齐 C# 就得放弃条件 ①②③ 的机制。这是**第四笔 D5 反向**（前三笔：宿主对象 D-c、默认共享轴、`Static` 局部的 `spec:277`）。补一条同向记录：`<>TopLevel` 的字段也没有 `Await` 初始化器的余地（`static` 类，且 `:34` 的字段示例无初始化器）⇒ **三个 C# 模型里没有一个**允许类型级顶层字段带 `Await`。**要求**：设计必须显式写出这一条偏离（它不是「补一个漏」，是「在 C# 有主动规则的地方反向」），或调整条件 ①②③。证据等级：判据行 **已检查**（逐行读到）；C# 侧实际运行**未跑**（本树无 C# 运行环境）⇒ 本条标「已检查（源码判据行）/ 未运行」。

### 复会第六议程：规范改写面 —— 承诺保留、加具名例外

本轮我们把上一轮的处置**精确化**。上一轮我们写的是「不得重写不变式」；现在我们把分界改准：

> **「删掉承诺」**（把 Soundness 三句整段抹掉——不可接受）vs **「给承诺加一个具名例外」**（承诺保留、例外点名——唯一自洽解）。
> **把 `:277` 原文留着不动，是第三种、也是最坏的处置**：Soundness 章节里留着一句已知为假的断言，而 `spec:275` 自己逐字要求偏离必须「**explicit in this specification**」。**一句不动的假全称句不是 explicit deviation，是沉默。**

为什么必有一个例外：`spec:35` 逐字「The four top-level forms keep their ordinary spelling and meaning; only their container changes.」、`:273` 逐字「The model is a containerization, not a new declaration system. Every top-level form keeps its ordinary spelling and its ordinary meaning」、`:277` 逐字「**A program that is legal both as a script and as ordinary code has the same meaning in both**; the differences are the placement of declarations, the synthesized entry point, and the result rule for typed submissions.」——翻转默认共享性会造出**反例**（同一段顶层 `Sub` / `Dim` 在脚本里是共享、在普通代码里是实例）⇒ `:277` 在修订稿之下**必为假**。C# 侧对这条处置没有替代方案，而且**加强**：`spec:300` 逐字「Visual Basic and C# answer the same questions differently in three separate places, and the answers must not be conflated.」——delta 的论据方式（拿文件执行 / top-level members 面的证据去改 declaration and state model 面，见第二议程）本身**违反**了这句。

**处置（照 `Imports` 既有写法，模板在 `spec:194-200`：`#### An explicit deviation from the Scope rule` + `**Decision**` 段 + 逐字「the deviation is deliberate and is confined to the scripting dialect. Its reason is…」）**：

1. **新增具名偏离小节**，标题照 `Imports` 那节的形状（例如 `#### An explicit deviation from the ordinary declaration semantics`），每节带 `**Decision**` 段，逐字说明**为什么方言必须偏离**。
2. **逐轴点名（五轴）**：①顶层成员默认共享性；②顶层状态的**存储类别**（instance → type-level）；③顶层过程内 `Static` 局部的存储类别；④顶层作用域（`spec:66` 嵌套类型可隐式读写顶层状态）＋**冗余 `Shared` 的语义**；⑤（若采纳 D-e）**文本顺序**。
3. `spec:35` / `:273` / `:277` **改到仍然为真**：三处都要**保留主张、加上具名例外的指引**（指向偏离小节）。**不是删，是加例外。**
4. **必改清单**（行号按行首单元格）：`:15`（`Dim` 行）、`:16`（`Sub`/`Function` 行）、`:48`（含「a script class **is instantiated**」**不能删**）、`:58`、`:60`、`:64`（**承诺保住、机制句要换**——「字段初始化器」这个发射概念不再是它的载体，改成「初始化赋值与顶层语句按源码序同列」）、`:66`（含新写的「为什么嵌套类型能碰到一个它无法命名的容器」）、`:310-311` 对照表行（并如实注明与 C# 的分叉）。`spec:66` 的反转要一并写清：嵌套类型里的代码会不限定名读到顶层共享成员（`:2612` 的把守对共享成员放行），而 BC36966 仍拒显式 `Me`/`MyBase`/`MyClass`、`Binder_Expressions.vb:2263` 只查 `IsScriptClass` ⇒ 嵌套类型**写不出**容器限定名，结果是**只能隐式碰到**的共享状态；模块也有这个性质，但模块的补偿是「不可实例化 + 作用域落命名空间」——脚本类两条都没有，所以「为什么」必须新写。
5. **语言规范本体两处**照上一轮登记：`type-members.md:1269`/`:1271`/`:2021`（顶层共享变量的初始化器**离开**了「先于首次引用、按类型声明文本序」那套语义；注意 `:1302-1310` 的免责句**不能**当偏离依据，它说的是「时机不确定」，不是「改为每次运行、按源码序」）与 `statements.md` 的 Local Variables and Parameters 节 + `:329`（D-e 把局部规则套到字段上，与 `:329` 的等价方向相反）。`statements.md:341` **不必改**，但方言规范要补一句「因此脚本顶层过程的 `Static` 局部是 per-type」。

### 复会第七议程：顶层 `Property` / `Event` / `WithEvents` 的地位

**这条必须显式裁一次，不裁则本方向不能复活。** 本轮我们先修正上一轮（以及第一轮会议）的框架——上一轮记的是「六形式重新入列 ⇒ 把 issue 07/09 从边角搬上主路」，读完 issue 07 的**隔离表**之后，正确的说法是：

> **这三者在顶层今天就已经不通，而且与 `Shared` 无关**（issue 07 `:55` 逐字：「触发条件 = 成员落在提交类本身，与 `Shared` 无关……实例形状同样终止」）。所以它不是「本方向使它们变坏」，而是「**本设计宣称的成员集（六形式）大于今天可工作、且被规范承认的集合（四形式）**」。

三条事实基线（本轮实锤）：

| 形式 | 今天在顶层 | 锚点 |
|---|---|---|
| `Property` | **能作为成员被解析**，但**会顶掉其后的顶层语句区**（后续语句 **BC30188**），而 `Dim`/`Sub` 不会 | **P5**（BC30188，`EXIT=1`）/**P9**（只第 3 行报错）/**P7**（`EXIT=0`）/**P8**（`EXIT=0`）/**P10**（控制组 `EXIT=0`） |
| `Event` | 落提交类 ⇒ **发射期断言终止**（`EXITCODE=35`，零编译诊断） | issue 07 `:44-55`（已运行）；`SynthesizedEventAccessorSymbol.vb:495` |
| `WithEvents` | 同上（另一个断言点） | issue 07 `:44-55`/`:55`；`SourceWithEventsBackingFieldSymbol.vb:66`（`:32` 还把 property 的 `IsShared` 传播给后备字段） |
| 规范地位 | `spec:11-18` 四形式表 + `:56`/`:273` 的「穷尽」主张**不含**这三者 | 逐字已读 |

**证据 A 反向加重。** `top-level-members.md:57` 逐字只允许 5 类：「Allowed kinds currently are: methods, operators, extension blocks, fields, constants.」——**不含** properties/indexers/events（后者只在 Open questions `:173` 被列为待议）。⇒ 成员集这一轴上**蓝本比本方向更窄**，用证据 A 论证「六形式共享化是 C# 同形」**不成立**，是**反向取证**。

**裁**：**不能靠「四形式穷尽」把它们排除**——那份穷尽句**已在册判与实测不符**（`pitfalls.md` P-019：`meeting-submission-shared-members.md:83(c)`；且 `spec:190` 把 `WithEvents` 叫 "field"）。三条路我们分档：

1. **`WithEvents`**：与 `Dim` 同形同路、照样产字段（`SourceMemberFieldSymbol.vb:573-574`/`:643-656`）⇒ **并入 `Dim` 那一档**（映射表 `:15` 补注），并按 **issue 07 独立修**（它是**已存在的缺陷**，不是本方向的代价）。
2. **`Property` / `Event`**：**要么补进映射表并修**，**要么给诊断并明文写出**。**不能「既无」**——今天 `Property` 的 BC30188 与 `Event` 的 exit 35 都是**无规范的意外行为**，而修订稿把成员集写成六形式，等于**替这三者背书**。
3. 无论选哪条：**issue 07（Event/WithEvents）与 issue 09 必须进前置缺陷清单**；并给「顶层 `Property` + 语句交错」一条独立验证用例（P5/P9 的形状）。

### 上一轮裁定在本 delta 之下的地位（不重开）

delta 没有触及这几格，我们只确认它们**继续有效**，并注明是否有修订稿带来的新读数：

- **D-e（「使用变量前一定要定义」）＝ 保留（倾向反对）。** delta 未提及，裁定继续有效。上一轮我们把它裁清成「两条路径表面相反、实际在回答两个不同的问题，而 D-e 把两个问题写成了一条规则」：
  - **第一套问题：名字在声明空间里根本不存在。** 这由 `Option Explicit` 决定：`Binder_Expressions.vb:2501-2508` 只在 `Lookup` 失败（`Not result.IsGoodOrAmbiguous`）且 `ImplicitVariableDeclarationAllowed` 且 `CanBeImplicitVariableDeclaration(node)` 时走 `DeclareImplicitLocalVariable`，否则落 BC30451。宿主侧的现实是 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb:220` **显式**写下 `optionExplicit:=True`（不是靠 `VisualBasicCompilationOptions.vb:80` 的默认值），与 `spec:223` 逐字「`Option Explicit` | `On` | Every variable must be declared before use.」一致 ⇒ **这半句就是现状，不带来任何新内容**。
  - **第二套问题：名字在、但引用点的文本位置在声明之前。** 这由一条**专属诊断**管：`Binder_Expressions.vb:3104-3124` 的 `GetLocalSymbolType` 先做 span 比较（`:3108-3109`），再要求 `Not localSymbol.IsImplicitlyDeclared`（`:3114`）、两侧都有源位置（`:3115-3116`）、同一语法树（`:3117`）三件事同时成立才报 **BC32000** `ERR_UseOfLocalBeforeDeclaration1`。**它与 `Option Explicit` 无关**——实测 ⑥ 就是 `Option Explicit Off` 下的同一形状，报的仍是 BC32000；而隐式声明的局部被当作在方法开头声明（`statements.md:491` 逐字），`:3114` 那个例外正是为此而设。
  - ⇒ **D-e 的措辞在两个方向上都不自洽**：**若照字面走第一套**，它要管的形状到不了那一格（顶层 `Dim` 是脚本类的**字段**，成员查找与文本顺序无关——实测 ⑤：`Option Explicit Off` 下 `x = 10` 绑到**后面那个顶层字段**，随后初始化器把它改回 5；查找必然命中 ⇒ `:2501` 的 `Not result.IsGoodOrAmbiguous` **恒假**，`Option Explicit` 分支对目标形状是**死分支**）；**若改走第二套**（把局部规则原样套上），`Option Explicit` 那半句就是**无关的装饰**，D-e 实为**无条件**的「先用即错」——比它广告的更强。
  - **还有一条更坏的**：假设 `Option Explicit Off` 下真打开了隐式声明分支（实测 ⑧ 证明这条通路在本 fork **是活的**，只是宿主默认把选项钉在 On），那会造出一个被提升到 `<Initialize>` 开头、**遮蔽同名顶层字段**的 `Object` 局部（`ImplicitVariableBinder.vb:122-126` + `statements.md:491`）⇒ 同一个名字在**初始化器体内**指向局部、在**顶层 `Sub` 体内**指向共享字段，**且不报错**。这不是兜底，是新挖的名字解析陷阱（**推测**：链条完整、锚点已复核，但 D-e 未实现故未跑）。
  - **四条反对理由逐条成立**：①它动的是「局部」那一侧，而 `spec:58` 逐字「The field is created on the script class, so it has instance state and survives across submissions. **It is not a local variable, and it is not scoped to a method body.**」——D-e 之后同一个顶层 `Dim` 会**同时**具备两套来源相反的属性（**存储/作用域/生存期**＝字段：与文本顺序无关、跨提交保留；**可见性**＝局部：文本顺序决定名字何时可用），而 VB 里没有任何一个构造有这种混合（`Dim` 在本语言其余部分要么是局部、要么是字段，两者的名字可用性规则从不交叉）；②`Option Explicit` 兜底与 VB 自身的局部规则不一致（见上）；③它要修的那个洞在修订稿之下**已经不存在**——条件 ①②③ 已把初始化器留在 `<Initialize>` 的源码序，顶层读一个「文本上在后面」的字段得到的是**该字段的默认值**（实测 ②：`BEFORE=0`/`AFTER=5`），**这正是字段语义的自然结果，不诡异**；C# 侧那种「诡异」来自 `beforefieldinit` 的 `.cctor` 语义，而那套语义修订稿**已经不用了**；④唯一剩下的理由（未来模式一致性）**只对齐了四分之一**——那个模式的宿主是**隐式 `Module`**（⇒ 拿它对齐就该同时得到「冗余 `Shared` 要报诊断」，而修订稿保留「`Shared` 仍可写」）、**每编译至多一个沉浸式文件**含顶层语句（⇒ 顶层 `Let` 是**入口文件的局部**，不是「所有顶层变量都是局部」）、且它是 `Consider`（返工后 `Active`），**不是已落地方案**。
  - **改判前置四条**：①规则**只在初始化器序列内**生效，并在规范里明写「顶层 `Sub`/`Function` 的方法体、嵌套类型体内**不适用**」——否则实测 ⑦（方法一侧今天无顺序要求）与 `spec:277` 的明文不变式正面冲突，而 D6 管不到 `:277`；②**删掉 `Option Explicit` 兜底**，改成 VB 自己的口径（`Option Explicit` 只管「有没有声明」，不管「在哪声明」），并把未开启时该分支的行为**显式写死**，不允许隐式 `Object` 遮蔽字段；③`#Load` 多树的顺序语义写死（`:3117` 的同树要求照抄会把跨树引用全部放行）；④把「未来对齐」补成**整包对齐**（含冗余 `Shared` 的裁决），否则这条理由只走一半。
- **初始化策略（条件 ①②③）＝ 原反例溶解 / 新反例成立。** 「原反例溶解」的正账继续有效（剥离落在脚本类快路径上、`Binder_Initializers.vb:278-280` 与 `LocalRewriter_FieldOrPropertyInitializer.vb:49-54` 证明「共享字段 + 实例桶初始化器」今天就能发射；必须发生在收集点，`:1571-1573` 的按桶断言会破）。**但「溶解」不得读成「零差异」**：守卫/提前退出跳过初始化器 ⇒ 类型级字段留下上一次运行的值——这一条在**代价 2 的新口径**（公开 API 可达）之下**更硬**，且与 `.cctor` 无关。上一轮那句被实测证伪的机制话（「`.cctor` 必然在 `<Initialize>` 之前跑完」）的更正继续有效：共享/静态字段初始化器进的是 `beforefieldinit` 的 `.cctor`，时机是「首次访问该字段之前、未指定」，**无访问则不保证运行**（`Emit\NamedTypeSymbolAdapter.vb:496-499` + `type-members.md:1302-1310` 自陈 uncertain）；残留 `.cctor` 只对 const-of-Decimal/DateTime 与共享属性有效，**不许写「没有 `.cctor`」**。
- **D-b 的措辞条件继续有效**：「不新立规则」这句在**条件 ①②③ + 接受 per-type** 的读法下是诚实的（`SynthesizedStaticLocalBackingField.vb:37` 只有 `isShared` 一个入参翻）；**「线程同步锁」这五个字要删**——它是**初始化守卫**（`LocalRewriter_LocalDeclaration.vb:245-257` 的 `SyncLock Var$Init` 状态机），不是并发保护，锁外的每次读写都没有同步；per-instance → per-type 与「跨运行不回初值」如实进 Drawbacks（与「陈旧值」并成一条）。**但请注意**：若 delta 的答复被读成读法 (ii)（禁止顶层过程内的 `Static` 局部），本条就**不再是**「接受 per-type」，而是**弃用 D-b**——两条不能同时留着（见第一议程与第四议程）。
- **模块惯用入口点这条证据成立，但不指向本方向。** 两个锚点都真：`type-members.md:2843-2844` 的规范正文示例确实写作 `Module Module1 / Sub Main()`；`SymbolDisplayTests.vb:3764-3765` 是 fork 自己的测试数据，写作 `Module Program / Sub Main(args As String())`。所以「VB 用户对入口点容器的直觉是『不用写 `Shared`』」可以入册为 **Motivation 的 VB 特有直觉**，不必再降级为猜测。**但它不构成方向的分叉理由**，四点同向：①模块的共享性是从「**不可实例化**」推出来的（`types.md:689` 逐字），而 `spec:48` 逐字否认脚本类具备该前提 ⇒ 这个锚点**只借到共享性，借不到使它成立的三条前提**；②习惯的**另一半**是「别写 `Shared`」——`type-members.md:551` 逐字「Methods defined in standard modules and interfaces **may not specify `Shared`**, because they are implicitly `Shared` already」，而修订稿把它留下了 ⇒ 这条证据是给「**冗余 `Shared` 必须显式裁一次**」**加压**的证据，不是给方向加分的证据；③入口点习惯属于**文件执行面**（`spec:26` 逐字列了三张 C# 对照脸），而这个 fork 在文件执行面上的在册裁决恰恰是「宿主为隐式模块、顶层变量为**局部**」（`modvb\meetings\meeting-top-level-code.md` RESOLUTION 1）⇒ 它已经有一个正确的落点，把它搬到 `TypeKind.Submission` 的**脚本类**上，是搬到一个规范明文说「不是模块」的容器里；④动机的**措辞本身要改**——delta 写的是「照顾 **VBScript** 老用户习惯」，锚点说的是 **VB.NET** 的模板习惯（两代人、两种语言），应改写为「与 VB.NET 的入口点/模块心智一致」，并把「VBScript 老用户」那半句降级为猜测或删除。**另有一条互斥，必须在两条之间选一条**：若拿 Module 惯性当分叉理由，就得接受 Module 的**初始化语义**（`type-members.md:1269` 逐字「they are run after the program begins executing, but **before any references to a member of the type**」、`:1271` 文本序、`:2021` 逐字「before the `Shared` variable is first referenced」，而紧随的 `:1302-1310` 自陈这套保证对**隐式**共享构造器**不适用**、输出「uncertain」）——那正是条件 ①②③ 要拆掉的那一套；两条在规范文本上互斥，**选机制就不要再拿 Module 论证初始化语义**。另外我们确认 C# 对 `<>TopLevel` 的 Drawbacks（`:95` 污染命名空间、`:96` 工具链更新、`:97` 入口点解析 breaking change）与本方向的入口点面（`spec:169` 不动）**不可类比**。

### 复活门槛重算

两路各算了一遍，我们把清单**合并**（去重后新增五格）。每一格都标出修订稿是否已实质回应：

| 格 | 本轮状态 | 判定 |
|---|---|---|
| **必须 1**（重写机制句） | 未变 | **实质达成**（文案要换：**变的是字段存储类别**，不是初始化器位置；**不许写「没有 `.cctor`」**） |
| **必须 2**（降级「零差异」） | 未变 | **原反例溶解 / 新反例成立**（新反例在代价 2 的新口径下更硬） |
| **必须 3**（宿主对象四处定价） | delta「接受」但未定价 | **仍是门槛**（(a)~(d) + 三处写入点 + `isReadOnly` + 并发范围 + `spec:361`） |
| **1(a)** 与 spec 自陈不变式冲突 | **不变**（证据 A 不解，且它示范了另一条路） | **仍是门槛** |
| **1(b)** | 见上 | **不再成立**（但换标签） |
| **1(d)** 与 D5 同形性反向 | **不变**（证据 A 是**分叉**且本身未实现、LDM 明确分歧） | **仍是门槛** |
| **5(a)** `spec:66` 反转要给理由 | 未变 | **仍是门槛** |
| **5(b)** 规范偏离写法 | 见复会第六议程（措辞已精确化） | **仍是门槛** |
| **6(一)** 注入点（`Submission` vs `IsScriptClass`）+ 独立用例 | 未变 | **仍是门槛** |
| **6(二)** 冗余 `Shared` 显式裁 | **更硬**：**P3 BC30433 / P6 BC30593** 两条实锤 ⇒ 先例答案是**报错**；C# 的 `:69` 对偶（禁写 `static`）也是**报错** | **仍是门槛，且更硬** |
| **(i)** 冗余 `Shared` 显式裁决 | 与 6(二) 同格（两处登记） | **仍是门槛** |
| **(ii)** 07/09 进前置缺陷清单 | 见复会第七议程 | **仍是门槛** |
| **(iii)** 若采纳 D-e：`#Load` 多树 + `:22`/`:58` 两句 | 未变 | **仍是门槛** |
| **(iv)** 「跳过初始化器 ⇒ 陈旧值」措辞/Drawback | 未变 | **仍是门槛** |
| **(v)** D-c 后果定价 | 见第一议程代价 3 | **仍是门槛** |
| **(vi)** 证据 A 的**定性订正**（改「提案、未实现、**LDM 明确分歧**」）＋ 与 C# 在**容器轴**上的分叉要写清 ＋ **不得再引它支撑方向、六形式成员集或冗余 `Shared` 的处置** | **本轮新增** | **新增门槛** |
| **(vii)** `Property`/`Event`/`WithEvents` 三态逐形式裁 + issue 07/09 进清单 | **本轮新增** | **新增门槛** |
| **(viii)** 方向级论据的证据链重建（「csi 消亡」标 `Suspect` 并逐出承重位；「csi 模型有缺陷」推不出） | **本轮新增** | **新增门槛** |
| **(ix)** 代价 2 的措辞改**条件句** ＋「产品不发布 API 版本」标为**产品决定**（不是事实）＋ `ScriptTests.vb` 锚点统一到 `:135-148` | **本轮新增** | **新增门槛** |
| **(x)** D5 必须处理 **CS8100 反轴**（类型级字段 + async 初始化器，与顺序保证不可兼得） | **本轮新增** | **新增门槛** |

⇒ **剩余门槛口径 = 必须 3 ＋ 1(a) ＋ 1(d) ＋ 5(a) ＋ 5(b) ＋ 6(一) ＋ 6(二) ＋ (i)~(v) ＋ (vi)~(x) ＝ 17 格**（其中 6(二) 与 (i) 是同一格的两处登记，去重后 **16 格**）。与上一轮口径（12 格）相比：**格子没有变少，新增五格**；其中 6(二)、1(d)、5(b) 因为 delta 的新增（六形式成员集、证据 A、规范偏离的精确化）而**更重或更硬**。

### 候选逐一权衡

**A（维持现状，顶层 = 实例）：我们不把它当作「什么都不做」。** 提案给它记的代价是「顶层扩展方法**永久**必须手写 `Shared`」——这条按复核改写为：缺 `Shared` 的现状不是「啰嗦」，是 `SourceMethodSymbol.vb:1504`/`:1634` 两处断言终止（issue 04）。把它当成「缺一条诊断」而不是「缺一个默认值」，A 的代价就从「永久仪式」缩成**一条诊断的形状设计**。这是整场讨论里最关键的一次重新定价，本轮没有变化。

**B（本提案，顶层默认共享）：维持 `Table`。** delta 的优点记全：机制可行且**真被修干净了一处**（条件 ①②③ 把 `beforefieldinit` 的时机坑从用户语义里移出）；宿主 API 零改动；入口点与跨提交机制不动（`spec:169` 的拼写不变）；ref struct 一轴是空结果；成员集不再收缩（这一处**同时消掉了上一轮针对「收缩未记账」的指控**——记在正账）；以及本轮的新正账：**引证逐字准确**、**「隐式 static + 禁止显式修饰符」这一轴与 C# 真同形**。但四类理由在复会重裁之后仍然压过来，且**全部落在 D6 明文保留的范围里**（我们不使用兼容性）：

1. **(a) 与 spec 自陈不变式冲突（加强）**：三句仍在；处置只能写成**具名偏离 + 承诺加例外**，不得留假全称句。
2. **(b) 原反例溶解 / 新反例成立**：不得读成「零差异成立」；新反例（跨运行陈旧值）在公开 API 契约上更硬。
3. **(c) 收益与代价不成比例**：**降**——「被 05/06 硬阻塞」这一腿溶解；**升**——宿主对象（四处 + 三处写入点、未定价、无 D5 论证）、陈旧值、`Static` 翻转（或新禁令）、07/09 随六形式重新入列、D5 的第四笔反轴（CS8100）、`spec:361` 待重验。收益侧仍是「少写一个 `Shared` 字」——issue 04 的崩面消失记在正账，但那本来就是「该有一条诊断」的东西。证据 A 把这句话坐实了：**同一收益，两种容器，两种代价**。
4. **(d) 与 D5 同形性反向（不变且新计两笔）**：C# 保留共享/实例这条轴（`<>TopLevel` 甚至还在**开放**非 static 成员），本设计删掉这条轴且**不提供任何反向拼法** ⇒ 是**能力删除**；新计两笔是宿主对象那一格与 CS8100 那一格。

**C（①a，只对带 `<Extension>` 的顶层成员隐式 `Shared`）：比 B 窄，成本账更清楚。** 它买到与 B 相同的直接收益，对缺口 ①②③④ 零收益，也不动声明语义，**不触碰** `spec:35`/`:273`/`:277` 这条线——这是它与 B 的实质分野，本轮不变。

**D（容器种类改 `Module`，即 ③）：已两次否决，本提案也不采用。** 上一轮已复核分界（`SourceMemberContainerTypeSymbol.vb:147-180` 的 `Select Case`、`Binder_Lookup.vb:583-584` 的跨提交入口、`MethodCompiler.vb:564` 的 `IsScriptClass` 分支都不动），**不要拿 ③ 的否决理由来拒本方向**；容器保持 `TypeKind.Submission`。

**真正要比较的仍然是 B 与「A + issue 04 诊断 + 甲 + 乙」。**

### VB 基因对照

- **保持 VB-like**：`vblang\meetings\2018\vbldm-notes-2018.06.13.md:33` 逐字「We strongly believe that Visual Basic has a stance - a way of doing things. We will strive to maintain consistency with things being "VB-like"」；`:34` 逐字「our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea」。**这两句与兼容性无关，D6 管不到它们。** 复会之后这句话**更准**而不是更假：同一个 `Dim` 既是字段又是局部候选（D-e），`Shared` 既必须写又可以不写（模块入口点证据的加压），顶层方法既没有实例又没有非共享的表达法（D-a）——**三处「两种解释并存」**，而这条尺子恰恰不允许并存。
- **`Shared` 承载的语义被取消**：`type-members.md:551` 的「It is invalid to refer to `Me`, `MyClass`, or `MyBase` in a shared method.」在脚本里就是「写 `Shared` = 你放弃了宿主对象、放弃了实例状态」。默认共享之后，写脚本的人**再没有任何顶层写法**（除非嵌套一个 `Class`）能声明一个访问宿主对象的顶层 `Sub`；而修订稿保留「`Shared` 仍可写（不是错误）」——它既没有照模块先例给冗余修饰符诊断，也没有把「写了会怎样」写进规范，`Shared` 因此**退化为同义反复**。C# 的 `:69` 逐字「The `static` modifier is **disallowed** (the members are implicitly static).」说明**蓝本在这一格上的答案是「禁用」**，本设计两个先例（模块 `type-members.md:551`、`<>TopLevel` `:69`）**同时反向**。
- **与 VBScript 基因的关系**：经典 VBScript 那半条类比仍然无仓内锚点、不作为论据；方向相反的那半条（VBScript 的执行模型是语句自上而下，`Dim x = 5` 的执行时机变化与该基因相反）仍然成立。
- **与主线关系**：主线对扩展表面积设高门槛（`evaluation-standard.md` §2.3 的「主线保守」），而对脚本方言没有任何成文计划要求翻转默认共享性——本方向是产品侧的独立延伸，不在主线议程上。

### C# 生态对照

先记结论：**这一方向在 C# interop 面上不是「安全/不安全」的问题，而是「同形性账」的问题**——而且本轮 delta 之后，账**更难算平**：证据 A 把「顶层状态存储」一格从反向变成同形，但同时把另外八格推成反向，并新暴露一条 C# 的**显式拒绝规则**（CS8100）。元数据契约与宿主契约不动（`spec:169` 的拼写、`:131` 逐字把宿主对象字段排除在契约外）；跨提交查找按 `TypeKind.Submission` 分派，全静态后这一面只会更简单。本树 `Scripting\` 下只有 `Core`/`VisualBasic`/`VisualBasicTest`/`VisualBasicTest.Desktop`，没有 C# 宿主。

逐轴读数见第二议程的三列表（**1 同形 + 1 近似同形 + 8 反向/不同类**）。此外四条本轮新增的 C# 侧事实要单列：

1. **CS8100（第五议程）**：C# 对「类型级字段 + 异步初始化器」有显式拒绝，本设计要它当默认；且与顺序保证不可兼得。
2. **C# 的 use-before-declaration 只对 `LocalSymbol`**（`Binder_Expressions.cs:2233`/`:2244`/`:2328-2340`）⇒ csx 的字段从来没有「顺序诡异」这条问题，delta 的论据自相矛盾。
3. **C# 对 static 上下文访问宿主对象有显式检查**（`Binder_Expressions.cs:2445-2483`，`:2473` 逐字 `if (containingMember.IsStatic) { return false; }`）⇒ D-c 是在 C# 有主动规则的地方反向。
4. **冗余修饰符**：C# 的 `:69` 逐字「The `static` modifier is **disallowed**」＋ VB 模块的 `type-members.md:551`「may not specify `Shared`」⇒ **两个先例同向，答案都是「禁用」**，而本设计保留「`Shared` 仍可写」且未裁。

**「两模式必须一致」这个前提，蓝本自己否了。** C# 的 csx（顶层变量是字段，允许先用后声明）与顶层语句模式（顶层变量是局部，报 CS0841）在这条轴上给出**相反答案**且被长期接受。这是 C# 路径对 D-e 的独立反驳，与 VB 路径的结论同向。

## RESOLUTION

1. **方向「顶层成员默认共享」维持 `Table`；本轮 delta 不足以翻案。** 我们不用兼容性作为理由（D6 明文只解掉兼容性一条，而本方向今天也没有「在用语义」会被破坏）。四类理由在本轮重裁之后的状态是：**(a) 加强**（处置只能写具名偏离 + 承诺加例外，不得留假全称句）；**(b) 原反例溶解 / 新反例成立**；**(c) 一升一降、净仍不成比例**（并新增 D5 的第四笔反轴）；**(d) 不变且新计两笔**（宿主对象、CS8100）。

2. **六条代价的答复逐条裁定：**
   - **(代价 1) `Static` 局部 → 答复不成立。** 事实层成立（**P1 实锤**：顶层 `Static x As Integer = 5` → **BC30235** `ERR_BadDimFlags1`，`Errors.vb:254`；证据 B 逐字准确）；但代价的对象是**过程体内**的 `Static` 局部（**P2/P4 实锤合法**），机制上只有 `isShared` 一个入参翻（`SynthesizedStaticLocalBackingField.vb:37`）⇒ per-instance → per-type。**答复对象错位**；而「顶层不允许 `Static`」按字面承重就是**新禁令**（读法 ii），与 `DESIGN-v3.md:8` 的 D-b「不新立规则」自相矛盾且未申报。**处置**：删掉这条答复；代价按上一轮第四议程的裁决记（per-type + 跨运行不回初值 + 与「陈旧值」并成一条 + 方言规范补一句 + 写明另两条路被考虑过）；若真取读法 ii，明写新规则 + 撤 D-b + 补诊断设计。
   - **(代价 2) 跨运行不重置 → 代价成立；答复三半里前两半成立、第三半不成立作为事实。** (a) 成立（`Script.cs:467` 每次新建 state、`:361-369` 执行器缓存在同一 `Script<T>` 上，**已检查**）；(b) 成立（`CommandLineRunner.cs:238-269`/`:271-294` 无重跑同一 `Script` 的路径，**已检查**）；(c) **不成立**——`Script<T>.RunAsync` 在 **`PublicAPI.Shipped.txt:76-77`**（Shipped），`Script.cs:480-491` 的 `CreateDelegate` 逐字承诺「will run this script from the beginning when invoked」，`ScriptTests.vb:135-148` 是被跟踪测试且断言恰好是「重跑回初值」（**实锤**）。⇒ **「不发布 API 版本」是产品决定，不是事实**（`DECISIONS.md` 已判过）；**标准写法是条件句**，不得当作事实陈述。**锚点订正**：`ScriptTests.vb` 统一到 `:135-148`（三条断言 `:141`/`:144`/`:147`）；先前标为「转述」的 `Script.cs` 缓存**升为已检查**（源码逐行，未运行）。
   - **(代价 3) 宿主对象 → 代价成立（接受 ≠ 定价）。** 落点只有一格（`SynthesizedSubmissionFields.vb:57` 的 `<host-object>` 字段）；定价 = **四处 + 三处写入点**，含 **`isReadOnly:=True` 必须同时放开**；**D5 论证缺席**（C# 在同一位置是实例 `readonly` 字段 + 真构造器赋值，且有主动的显式检查 `Binder_Expressions.cs:2445-2483`）。**五件不补则转反对。**
   - **(代价 4) 能力删除 → 答复不成立，代价成立。** 正确的两条账合起来写：**相对 `Module` 是归位**（但丢掉「不可实例化」这条前提——`types.md:689` 对 `spec:48`），**相对 csx 是删掉一条腿**（`spec:302`/`:311` 把本轴的蓝本配给 csx）；证据 A 补不上这个前提，反而同源（`<>TopLevel` 是 `static` class）。
   - **(代价 5) 规范偏离 → 代价成立**（未回应）。**处置唯一**：承诺保留 + **加具名例外**；不得留一句已知为假的假全称句（`spec:277`）。见第 6 条。
   - **(代价 6) 顶层 `Property`/`Event`/`WithEvents` → 代价成立**（未回应），且被证据 A **反向加重**（蓝本 `:57` 只允许 5 类，不含这三者）。见第 7 条。

3. **证据 A 的定性（必须写进设计）**：**是分叉，不是同形。** 逐轴读数是 **1 轴同形（顶层状态存储）+ 1 轴近似同形（顶层过程，差别在冗余修饰符）+ 8 轴反向/不同类**；上一轮基于 csx 的对照结论**不翻转**。**必须写入的两条 delta 未引的事实**：①该提案的 LDM triage 逐字「**by no means is there agreement on the feature at this point**」（`csharplang\meetings\2025\LDM-2025-12-17.md:68-74`，champion issue `dotnet/csharplang#9803` 同号，2026 年无后续记录）⇒ 定性应改为「**提案、未实现、且 LDM 内部明确分歧**」；②三种顶层模型是 `LDM-2020-01-22.md:15-21` 的**三个并列场景**（`:23` 逐字「some of these proposals interact in difficult ways」），**不是演进三级路**——C# 9 选场景 (1)（答案是**局部**）、证据 A 是场景 (2) 的复活、本方言对应场景 (3)。**记正账**：证据 A 与证据 B 的引证**逐字准确**，无一处错；证据 A 在「隐式 `static` + 显式修饰符被禁」这一轴**确实同形**（上一轮漏记）。**限制**：不得再引证据 A 支撑方向、支撑六形式成员集、或支撑冗余 `Shared` 的处置。

4. **方向级论据不成立；「csi 消亡」标 `Suspect` 并撤出承重位；「以 csi 为蓝本」（D5）本轮不动。** 仓内镜像是**既不能证实、也不能否证**的（全树无退役/弃用记录）；反有两条方向相反的在册记录（`Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:56` 仍列 `<InternalsVisibleTo Include="csi" />`，**实锤**；`LDM-2020-01-22:48-50` 与 `LDM-2020-07-13:191-193`/`:207-209` 的在册意图是 **unify，不是 retire**）。即便前提为真也推不出结论（面不对／C# 允许 TLS 的成文理由是去样板／官方继任模型给的是局部／两个活的 C# 方向都不走此路／delta 点名的两个「缺陷」都不需要翻默认值来修）。**真正被动摇的是动机①的定价**：降级为「补一条诊断」，issue 04 独立立项。

5. **CS8100 那一格必须正面处理（新反轴）。** C# 对「**类型级字段 + 异步初始化器**」有**显式拒绝**（`Binder_Await.cs:158-172` 的 `IsStatic ⇒ CS8100`；`ErrorCode.cs:1335`），而本设计要它当默认形态，且本仓已有**在册测试**覆盖该形态（`ScriptTests.vb:375-384` 的 `Dim value = Await Task.FromResult(13)`，`:364-372` 为同族姊妹形态）⇒ 它与条件 ①②③ 的顺序保证**不可兼得**。设计必须显式写出这条偏离，或调整条件 ①②③。证据等级：判据行**已检查**、运行**未跑**。

6. **规范改写面：只能写具名偏离、承诺加例外、不得重写不变式。** 处置照 `Imports` 既有写法（`spec:194-200`），**逐轴点名五轴**（默认共享性 / 存储类别 / `Static` 局部存储类别 / 嵌套类型作用域 + 冗余 `Shared` 语义 / 若采纳 D-e 的文本顺序）。必改清单：`:15`、`:16`、`:48`（「a script class **is instantiated**」不能删）、`:58`、`:60`、`:64`（改机制句、不改承诺）、`:66`（含新写的「为什么」）、`:310-311`；`spec:35`/`:273`/`:277` 三句**保留主张 + 加例外**（**不得留一句已知为假的假全称句**）；语言规范本体 `type-members.md:1269`/`:1271`/`:2021` 与 `statements.md` 的 Local Variables and Parameters 节 + `:329` 一并登记；`statements.md:341` 不必改。C# 侧补强：`spec:300` 的「must not be conflated」——delta 的论据方式本身违反它。

7. **顶层 `Property`/`Event`/`WithEvents` 必须显式裁一次（新门槛）**，且**不得引证据 A 作支撑**（蓝本 `:57` 只允许 5 类）。框架修正：这三者**今天在顶层就已经不通、且与 `Shared` 无关**（issue 07 `:55` 逐字 + `:44-55` 隔离表；P5/P7/P8/P9/P10）⇒ 成立的说法是「**本设计宣称的成员集（六形式）大于今天可工作、且被规范承认的集合（四形式）**」。裁：`WithEvents` 并入 `Dim` 档 + 按 issue 07 独立修；`Property`/`Event` 要么补进映射表并修、要么给诊断并明文写出，**不能「既无」**；**issue 07/09 进前置缺陷清单**，并给「顶层 `Property` + 语句交错」独立验证用例。

8. **复活门槛（重算后的完整清单）**：**必须 3 ＋ 1(a) ＋ 1(d) ＋ 5(a) ＋ 5(b) ＋ 6(一) ＋ 6(二) ＋ (i)~(v) ＋ (vi)~(x)** = **17 格**（去重后 16 格；与上一轮 12 格口径相比**没有变少，新增五格**）。其中**必须 1 已实质达成**（文案要换）；**必须 2 换标签**；**必须 3 从「拒绝理由」转为「已付费项」但后果未定价**——这是修订稿相对上一版唯一的实质推进。

9. **推荐路线不变（本方向不采纳时的替代，我们建议按它走）**：**维持实例基线 + issue 04 补诊断 + 甲 + 乙**。issue 04 升为独立立项、修法取「诊断而非断言」；甲 + 乙 按 `meeting-submission-shared-members` 的裁决落地，仍是本提案的前置且独立成立。

10. **边界裁定（与是否采纳无关，现在就要钉）**：
   - **注入点必须写死 `DeclarationKind.Submission` 还是 `IsScriptClass`**：非提交脚本类（`DeclarationKind.Script`）今天健康，而分桶点不含容器种类门；元数据可见性放宽按 `TypeKind.Submission` 分叉，两种 kind 本就有可观察差异。要求给「非提交脚本类 + 顶层 `Shared` 字段带初始化器」一条独立验证用例。
   - **冗余 `Shared` 要显式裁一次**：照模块先例（P3 **BC30433**、P6 **BC30593**、`SourceMemberFieldSymbol.vb:453-468`、`type-members.md:551`）与 C# 的先例（`top-level-members.md:69`「The `static` modifier is **disallowed**」）——**两个先例同向，答案都是「禁用」**——报诊断，还是明文允许并给出理由；写进规范影响面。
   - **07/09 进前置缺陷清单**：成员集合为六形式之后，`Shared Event`（`SynthesizedEventAccessorSymbol.vb:495` 断言）与 `Shared WithEvents`（`SourceWithEventsBackingFieldSymbol.vb:66`）从「显式 `Shared` 才踩」变成「顶层一律踩」，且 `WithEvents` 与 `Dim` 同形同路、照样产字段，**躲不掉**。

11. **口径订正（写进纪要，不改提案）**：
   - 上一轮正文「`.cctor` 必然在 `<Initialize>` 被调用之前跑完」**作废**（正确口径：`beforefieldinit`，首次访问之前、未指定，无访问则不保证运行；锚点 `NamedTypeSymbolAdapter.vb:496-499` + `type-members.md:1302-1310`）。
   - `ScriptTests.vb` 的锚点统一到 **`:135-148`**（三条断言 `:141`/`:144`/`:147`；重跑断言在 `:143-144`）；上一轮的 `:136-149` 作废。
   - 「`Script.cs` 的编译/执行器缓存」从**转述**升为 **已检查**（承重锚点 `Script.cs:361-369` + `:467`），并补一条同向实锤：`CommandLineRunner.cs:238-269`/`:271-294` 的产品 CLI 无重跑路径。
   - 上一轮 `spec:137`/`:165` 的引行应改为 `:131`/`:169`（事实判断不变）；`EmitAddress.vb:261-284` 的 `HasHome` 是 **5** 个分支；映射表行号按行首单元格（`:15` 是 `Dim` 行、`:16` 是 `Sub`/`Function` 行、`:14` 是分隔行）。

12. **正面记录（保留为提案资产，与方向取舍无关）**：ref struct 空结果；两种 `Span` 模式分野；只读无硬墙与它主动作出的自纠；`Symbol.vb:97-102`/`Symbol.cs:256-265` 的提交类可见性放宽；测试策略（避开 `Object` 接收者盲区、用 `Verification.FailsPEVerify` 锁住已知未过校验的形状）；条件 ①②③ 的剥离落在脚本类快路径（`SourceMemberContainerTypeSymbol.vb:3242-3258`）之上是被现有实现覆盖的（`Binder_Initializers.vb:278-280` 与 `LocalRewriter_FieldOrPropertyInitializer.vb:49-54`）但**必须发生在收集点**（`:1571-1573` 的按桶断言）；以及本轮新增的两条——**证据 A/B 的引证逐字准确**，与**「隐式修饰符 + 显式修饰符被禁」这一轴与 C# 真同形**。这些复核成立，计划阶段可独立使用。

13. **登记面**：本方向维持 `Table` 期间**不改** `spec` 与 `upstream-merge.md`；issue 04 升为独立立项时按 `proposal-submission-shared-members.md` §7b 的方式补登记，并注意 `upstream-merge.md`「二·补、已知欠账」节在合并前须逐 diff 复核。

## Implication

落定之后，`spec-scripting-dialect.md` 的 Soundness 一节保住了它对自己的承诺——方言仍然只改容器、不改 spelling 与 meaning，`Imports` 累积仍是**唯一**一条明文写下的偏离。我们不接受把 `:277` 原文留着不动那种处置：承诺保留、例外点名，是唯一自洽的写法；若将来真要翻转默认共享性，就按 RESOLUTION 6 的方式**新增一条逐轴点名的具名偏离**，而不是把不变式改写掉或静默留着假话。

本轮与我们前两轮一样放弃的是「让顶层省掉一个 `Shared`」这个便利，但放弃的理由比前两轮**更窄也更硬**：不是「不可行」（条件 ①②③ 的机制可以施工，我们公开认账），不是「怕 breaking change」（D6 让那条论据不成立），也不是「C# 没这么做」——而是因为**这条路的每一个活的 C# 参照都没有走它**：C# 想要同类收益时，要么把它放进 `Program.Main` 里的**局部**，要么**新造**一个 `static` 且 namespace 作用域的容器；而它对「类型级字段 + 异步初始化器」这个本设计的默认组合，给的是**一条显式错误**。同时，delta 用来证明「C# 正在做同一件事」的那份提案，在 C# 自己的记录里写着「**by no means is there agreement on the feature at this point**」。收益是「少写一个 `Shared` 字」，代价是让方言把自己承诺过不动的那一轴动掉，外加三处「两种解释并存」与四笔 D5 反向。复活条件写在 RESOLUTION 8/9；如果将来产品侧真的需要这一格，请带着完成后的门槛清单回来。

我们另外要留下一条方法论上的记录：**这一轮的教训是「引证准确 ≠ 论证成立」**。delta 的每一处 `文件:行号` 都对，但它把「别人也在做」当成了「这条路对」，把「我们不发布 API 版本」当成了「差异不存在」，把「成员层不能写 `Static`」当成了对「过程体内的 `Static` 局部」的答复。这三处都不是事实错误，是**论证层的错位**——而它们恰好都在同一份 delta 里出现，说明门槛清单在下一轮仍然应当逐格对照证据，而不是对照引证。

对相邻单元的影响：issue 04 与 issue 05/06/07/08/09 各自独立、分别立项，不要把多条并进同一份设计；`spec:66`/`spec:64`/`:58` 的措辞改动若发生，按 RESOLUTION 6 的偏离写法走。

## OPEN QUESTIONS / TODO / Follow-up

* **OPEN**：`spec:35`/`:273`/`:277` 的具名偏离**具体措辞**未定（写法已钉：承诺保留 + 加例外 + 逐轴点名五轴）；偏离小节要不要同时给「为什么方言必须偏离」的 `**Decision**` 段草稿。
* **OPEN**：`Static` 局部的落法二选一——**接受 per-type（D-b）**还是**读法 (ii) 新禁令**。若取后者，撤 `DESIGN-v3.md:8` 的 D-b、补诊断设计（错误码归属、是否走乙的「从崩改成报错」先例）、并写明另两条路被考虑过。
* **OPEN**：**CS8100 那一格怎么办**——放弃条件 ①②③ 的顺序保证、写一条显式偏离、还是把本方向**缩小到交互面**（`spec:26` 的三张脸里只有交互面是 csi 的直接对位）。三条路都要给锚点与后果。
* **OPEN**：冗余 `Shared` 的裁决（报诊断 vs 明文允许）——模块先例（`type-members.md:551`、P3/P6）与 C# 先例（`top-level-members.md:69`）**同向给「禁用」**；若选报错，本方向的用户可见面会再缩一分。
* **OPEN**：顶层 `Property`/`Event`/`WithEvents` 的逐形式三态（补进映射表并修 vs 给诊断并明文写出）；`WithEvents` 并入 `Dim` 档是否需要映射表 `:15` 的补注形态。
* **OPEN**：D-e 的判据域是「仅顶层语句级」还是「照 `Binder_Expressions.vb:3104-3124` 的 span 比较」；跨树（`#Load`）与跨提交（链）的引用口径。
* **OPEN**：非提交脚本类（`DeclarationKind.Script`）要不要随方向一起改。
* **OPEN**：类型级状态的并发语义——D-c 的宿主对象、D-b 的 `Static` 局部、条件 ①②③ 的每次运行重赋值三者是**同一格风险**，要求合并成一条 Drawback 并给出「同一 `Script` 对象重复/并发运行」的可观察后果。
* **OPEN**：`spec:361` 会话隔离在宿主对象这一格上的重验结论（范围写准为「同一 `Script` 对象在提交失败后重跑，会话状态不变」）。
* **OPEN**：D-c 漏掉的 `isReadOnly:=True` 那一步，以及 `SynthesizedSubmissionConstructorSymbol.vb:87-96` 的 `BoundHostObjectMemberReference` + `Me.<field>` 组合在共享上下文里是否成立。
* **TODO**：issue 04 的诊断形状（错误码归属、是否复用 BC30469、消息是否点名「顶层扩展方法需要 `Shared`」）——升为独立立项后的第一件事。
* **TODO**：把本轮的实测（P1–P10）与其机制锚点补进提案 §5/§8 的口径更正记录，计划阶段以纪要为准。
* **TODO**：`ScriptTests.vb:135-148` 的「重跑回初值」三条断言（`:141`/`:144`/`:147`）在甲 落地后应逐字节不变（甲 只动共享路径）——列为一条回归验证用例；并**另加**一条**守卫变体**的用例，把「跳过初始化器 ⇒ 陈旧值」这条新反例钉住；再加一条 `TestTopLevelAwaitInStatement` 形态（`ScriptTests.vb:375-384`）的用例，把 CS8100 那一格的可观察后果钉住。
* **TODO**：条件 ①②③ 的剥离**必须发生在收集点**（`SourceMemberFieldSymbol.vb:628-632`/`:665-673`、`SourceMemberContainerTypeSymbol.vb:2669-2681`），不能在装配点搬——`:1571-1573` 的按桶源码序断言会破。这条请进计划，别留在设计层含糊。
* **Follow-up**：若日后复活本方向，「修订稿形态的端到端输出」必须实跑（顶层 `Dim x = 5` 的初始化器落在 `<Initialize>` 的 IL 证据 + 守卫跳过后**同一 `Script` 对象重跑**读到什么），把本纪要与提案的「待定」一并升为实锤或推翻。
* **Suspect**：**「csi 消亡」**——仓内镜像（2013–2026 全 LDM + proposals + spec）**无任何退役/弃用记录**，无据不等于未退役；本轮**撤出承重位**，不作为论据、也不否定。
* **Suspect**：**经典 VBScript 是否有类成员层**（`Class … End Class` / `Me` / `Class_Initialize`）——本仓无锚点，不作为论据。
* **Suspect**：**`<>TopLevel` 的字段初始化器落点**——提案未写，按「`static` 类 ⇒ 静态构造器语义」推属**推测**；「csx 里 `static` 顶层字段的初始化器不带 `Await`」由 `Binder_Await.cs:163-166` 反推（判据行已检查、未实跑）。

## 状态

* **LDM 状态：Table**。方向「脚本/提交类顶层成员默认共享」维持 `Table`（这是它第二次聚焦复会——上一轮带回来的是机制重做，这一轮带回来的是六条代价的否认与三条新证据）；作为方向的完整论证**不通过**——被推翻的是它的**方向定价、论证方式与未裁项**，不是它的事实基线与机制可行性。delta 相对上一版**没有实质推进**；相对**第一版**（即第一次复会的那份修订稿），唯一的实质推进仍然是「必须 3」从拒绝理由变成已付费项（后果仍未定价）与条件 ①②③ 把 `beforefieldinit` 的时机坑移出用户语义。六条代价的答复已逐条裁定；复活门槛见 RESOLUTION 8。
* **三态判定**：
  * `proposal-top-level-implicit-shared` = **Table**（方向维持 Table；提案需按 RESOLUTION 8 的完整门槛返工后方可重新上会）
  * **推荐替代路线**（RESOLUTION 9）：维持实例基线（**Active**，现状）+ issue 04 补诊断（**Consider**，升独立立项）+ 甲/乙（**Active**，`meeting-submission-shared-members` 已裁定）+ ①a 维持 **Table**（若 A+诊断 路线不被接受，它是次优选择）
  * **修订稿的机制部分**：条件 ①②③ 的剥离落点（收集点、脚本类快路径覆盖、按桶断言） = **Consider**（可独立用于计划）；D-b「不新立规则」 = **Consider**（须按复会条件改措辞，且与读法 ii 二选一）
  * **证据 A（C# `top-level-members`）** = **Suspect**（就「可作蓝本」而言：提案、未实现、LDM 内部明确分歧）；其**文本引证** = 已检查（逐字准确）
  * **「csi 消亡」** = **Suspect**（仓内无据；已撤出承重位）
  * **正面资产**（RESOLUTION 12）：ref struct 空结果 / 两模式分野 / 只读无硬墙与自纠 / 提交类可见性放宽 / 测试策略 / 证据 A·B 的引证准确性 / 「隐式 static + 禁写修饰符」这一轴的同形 = **Active**（可独立用于 plan）
* **证据等级**：承重锚点**已检查**（逐行复核，无一处承重行或承重判据不成立）；本轮新增的 `Binder_Await.cs:158-172`、`ErrorCode.cs:1335`、`Script.cs:361-369`/`:467`/`:480-491`、`CommandLineRunner.cs:238-269`/`:271-294`、`PublicAPI.Shipped.txt:76-77`、`top-level-members.md` 全文与其 LDM triage、`LDM-2020-01-22`/`LDM-2020-07-13` 的场景记录、`statements.md:329`/`:337`/`:341`、`type-members.md:551`、`ScriptTests.vb:135-148`/`:364-372`/`:375-384` = **已检查（源码/规范逐行）**；本轮十条探针（P1–P10，BC30235 / `Shared Sub` 内 `Static` / BC30433 / BC30593 / BC30188 族 / 控制组）与并入基线的上一轮八条测量 = **已运行**（探针已删净、未入库）；**修订稿形态的端到端行为 = 未运行（待定）**（本阶段未改编译器）；「跳过初始化器 ⇒ 陈旧值」与「跨运行不回初值」= **推测**（机制已检查 + 基线已实锤，但未在修订稿实现上跑）；C# 侧的一切运行行为 = **未跑**（本树 `Compilers\Test\Utilities\` 只有 `VisualBasic`，C# 单测跑不了、无 C# 运行环境）；Release 行为 = 未跑（全部只跑 Debug `5816a5c`）；`<>TopLevel` 的初始化器落点 = 推测。

独立五维评审为不入库工作材料（git-ignored），不随本纪要入库。
