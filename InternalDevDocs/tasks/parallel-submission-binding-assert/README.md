# 任务：查清"并行跑测试时编译器内部自检崩溃"到底是产品问题还是测试装置问题

（任务文件夹名：`parallel-submission-binding-assert`。对应缺陷登记：`..\..\issues\issue-parallel-submission-binding-assert.md`——给人看的，做任务不需要读它，本文件内容自足。）

## 1. 现象，用大白话讲

有一条单元测试叫 `CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning`，文件在
`Compilers\VisualBasicSemanticTest\Semantics\ScriptTopLevelDefiniteAssignmentTests.vb`。

- 只跑它所在的那一个测试类：**连续 6 次，全过**。
- 让它和另一个测试类**同时并行**跑：**4 次里红 2 次**（换成别的邻居类是 4 次里红 1 次；把范围放宽到 46 条那组是 4 次里红 3 次）。
- 崩的位置是编译器自己的一句检查（`Debug.Assert`）在 `Compilers\VisualBasic\Portable\Binding\Binder_Conversions.vb:442`：
  `argument.Type.IsSameTypeIgnoringAll(targetType)` —— 意思是"要转换的那个值的类型，和目标类型必须是同一个类型"，这句在并行时不成立。

**注意别读错**：崩的时候还会附带一大段"池化对象没归还"的清单（`BindingDiagnosticBag` ×3、`Binder_Initializers.vb:117` 和 `Binder_Invocation.vb:2688/2689` 的 `ArrayBuilder`）。那些归还代码写在正常路径上，异常半路抛出就会跳过了不执行——**是结果不是原因**。那几处的写法是上游原本就有的代码，别去"顺手修内存管理"。

## 2. 为什么第一步不是修

现在有两种完全不同的可能，修法相反：

- **可能 A：编译器真的不能并发跑。** 那就是产品缺陷，真实用户（IDE 后台分析 + 前台运行，或宿主开两个线程）也可能撞上，必须修产品代码。
- **可能 B：只是测试框架并行时，几个测试互相踩了同一份静态/共享状态。** 那才轮到改测试的组织方式。

在能证明是哪一种之前，**禁止**做这三件事：把这条测试设成跳过、放宽它的断言、或者"再跑一次绿了就算了"。那都是把问题藏起来。

## 3. 第一步要交付什么（不改任何代码，不编译）

1. **把复现缩到最小并且稳定**：找出"哪两个测试类一起跑"就一定能较高概率复现，连跑 10 次，把红/绿次数记下来。现在已知 4 次里 1~3 次，要更准的数字。
2. **脱离测试框架复现一次**：自己写一个临时小程序（放 `tmp\` 下面，不要提交），开两个线程各自编译并运行一段脚本，试两种变体：
   - 两个线程完全独立（各自的脚本链、各自的选项）；
   - 两个线程共用同一个 `ScriptOptions` 或共用同一个"上一次提交"。
   如果这里能复现同样的自检失败 ⇒ 基本就是可能 A（产品问题）。如果怎么组合都复现不出来 ⇒ 偏向可能 B。
3. **在失败现场把两个类型打印出来**：`argument.Type` 和 `targetType` 分别是什么、是什么符号。允许在 `tmp\` 下的临时复制工程里插打印代码，**不要**在仓库的产品代码里留下任何插桩。
4. 给出结论，只能三选一：**产品并发缺陷** / **测试装置共享状态** / **未定性（并列出已经试过哪些组合）**。
   缩不出复现**不等于**问题不存在，不许写"没复现所以没问题"。

## 4. 已排除的东西（省得重复劳动）

- **不是上一个任务（issue 33）引入的**：把那 9 行改动拿掉之后，同样的红照样出现。
- **失败的那条测试用的变量带显式 `As`**，按 `SourceMemberFieldSymbol.vb:135` 的判断根本不会进入脚本字段类型推断那条新路径。⇒ 排除了"推断这个字段"这条路，但**没有**排除"推断过程多做的那次绑定对共享状态的影响"（这条还没测，标为待查）。
- 相关代码已经在提交 `2173a56` 里了，所以这不是"未提交改动的收尾"，是一条独立缺陷。

## 5. 结论出来以后怎么修

- **若是产品并发缺陷**：找出并发下不成立的那个共享可变状态并消除它（或加正确的同步）。然后按 `test-plan.md` 第三节加并发用例把它钉住。
- **若是测试装置问题**：参考本仓已有的先例（`Scripting\VisualBasicTest\CommandLineRunnerTests.vb` 用 xUnit 的"不并行集合"把会改进程级状态的测试类隔离开）。要点是**把写共享状态的那一类关进串行集合**，而不是给受害的测试加锁、加延迟或跳过。
  并且必须写清楚"为什么之后不可能再重叠"的推理（哪个类是写方、串行阶段怎么排除并发）。跑 3 次全绿只是确认没引入新问题，**不能**当作论证。

## 6. 不许碰

- 上面第三节提到的"临时插桩只许在 `tmp\` 下"这一条不许破例改成改产品代码。
- `#Load` 的重复加载问题（另一条任务 `..\load-directive-dedup\`）。
- 上一个任务在 `SourceMemberFieldSymbol.vb` 加的 9 行。
- **禁止任何 git 写操作**（`git add`/`commit`/`stash`/`checkout`/`restore`）。工作树里有些文件已暂存是既有状态，别去整理。

## 7. 回报要求（每步都写）

- 第几步、目标、实际做了什么、产出（改了什么或得到什么结论）、结果（通过/打回/失败及原因）。
- 每条结论标：**跑出来的** / **读代码确认的** / **猜的**。猜的必须附上"要怎么跑才能证实"。
- 说明"普通 `.vb` 编译为什么不受影响"（若这一步没改产品代码，就复述：本任务落点在脚本提交路径，普通编译不经过 `CreateSubmission`）。
- 有没有超出范围的改动，有就写原因，没有写"无"。
- 日志写到 `tmp\vortex-logs\parallel-submission-binding-assert\{步骤号}-{角色}-{简述}.md`；开工先读同目录 `pitfalls.md`，发现新坑追加一条（1-2 行）。

## 8. 命令备忘

- 只跑相关的那几条：
  `dotnet test Compilers\VisualBasicSemanticTest\Microsoft.CodeAnalysis.VisualBasic.Semantic.UnitTests.vbproj --filter "FullyQualifiedName~ScriptTopLevelDefiniteAssignmentTests"`
  邻居类组合用 `--filter "FullyQualifiedName~A|FullyQualifiedName~B"` 这种或写法。
- 要拿到失败详情必须加：`--logger "console;verbosity=detailed"`，否则只有一行 `[FAIL]` 看不到异常内容。
- 长输出先重定向到文件再读尾巴；全量七个门和整套脚本测试由主线跑，子任务不跑。
- 一次只让一个任务碰编译产物（并行构建会锁 dll）。
