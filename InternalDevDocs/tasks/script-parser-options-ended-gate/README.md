# 任务：脚本命令行解析补 `optionsEnded` 门（script-parser-options-ended-gate）——任务计划

> **状态：已收口（F01–F04 全做完，待作者提交）**——OE-F01 产品码 + OE-F02 单测由子代理落地，F03 回归（`CommandLine` 门 483/476/7/0、`Scripting\VisualBasicTest` 直跑 766/0、重建 Debug 宿主 `vbi -- a.vbx` / `vbi -- @x` / `vbi -` 三格档 2）与 F04 账本已完成（`..\..\upstream-merge.md` §2.25(e)；issue 23 转 Fixed；队列台账 `..\csharp-script-parity-sweep\README.md` **#4** 转已验证）。流水账 `tmp\vortex-logs\script-parser-options-ended-gate\`。缺陷登记：`..\..\issues\issue-script-parser-no-options-ended-gate.md`（issue 23）。**子 agent 禁读 `issues\`**——症状与判据在本文件复述自足。

- **一句话**：`VisualBasicCommandLineParser.vb` 的符号 `Parse` 主循环缺 `optionsEnded` 门 ⇒ 合法输入 `["--", "@arg1"]` 撞 `Debug.Assert(Not arg.StartsWith("@"))`（`:198`），`["--", "/arg2", "script.vbx"]` 不撞断言却报 `BC2007` 且把源文件槽写成 `"-"`，而 C# 把 `/arg2` 当**源文件**。
- **D7 裁定（已自动裁）**：可移植 ⇒ **对齐全 csi 的现行做法（方向 A）**。判据在树内：`Compilers\CSharp\Portable\CommandLine\CSharpCommandLineParser.cs:169`（`optionsEnded` 门）与 `:174`（选项判定同样带门）；`--` 与 `-` 在 VB 侧同码（`VisualBasicCommandLineParser.vb:477` 的 `Case "-"`）⇒ 分隔符吞掉后的语义按 C# 处理。理由＝本 fork 就是产品，"上游 VB 也没门"不构成理由（`decisions.md` D6/D7）。

## 一、判据（F01 的边界）
1. `--` 之后：不再解析任何开关；余下参数按 C# 语义当**位置参数**（源文件 / 脚本实参）处理，**不报 `BC2007`**。
2. `--` 之后的 `@响应文件`、`-x`、`/x` 形态**一律不得**进入选项解析路径 ⇒ 也不得撞 `Debug.Assert(Not arg.StartsWith("@"))`。
3. 断言只作为"门之后不可能到达"的事后检查；**不得**靠放宽断言过关。
4. `--` **之前**的行为逐字不变（含未知开关的 `BC2007`/拒绝口径，按 `spec-script-optimization-level` 的现有争议不新增口径）。
5. 只有一处门不够：`:198` 的裸断言与 `:202` 的选项判定都要受同一 `optionsEnded` 控制；`Case "-"`（`:477`）的 `--`/`-` 同码问题按 C# 的语义分开处理，**不得**顺手改掉 `-` 的既有可用形状。

## 二、范围内
| # | 单元 | 说明 |
|---|---|---|
| F01 | 实现 | `Interactive\`/`Scripting` 侧的 `VisualBasicCommandLineParser.vb`（引入 `optionsEnded`，对齐 C# 两处门的形状） |
| F02 | 单元测试 | 见 `test-plan.md`：断言崩溃形状（Debug 构建）+ 三条行为格 + `--` 前不变的反例锁；`Compilers\VisualBasicCommandLineTest\**` 与宿主侧各一份 |
| F03 | 回归 | **由 main 跑**：七门 + `Scripting\VisualBasicTest` 直跑 `-automated` + 重建发布版 `vbi` 后跑真实命令行（`vbi -- @x`、`vbi -- /a.vbx`） |
| F04 | 账本 | issue 23 转 Fixed（commit 不预填）；`upstream-merge.md` 入账（该文件已在册 ⇒ **改写**在册条目并刷新锚点）；如影响 `spec` 的命令行口径由 main 落笔 |

### 非范围
- 不改响应文件（`@`）本身的语法与解析器；不引入新开关；不新增诊断码、不改 resx/xlf。
- 不碰 `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb` 与 `Parser\**`（另两条在办任务的面）。

## 三、风险与停止上报
- 回归面集中在 `CommandLine` 门（475 条）与宿主测试；若发现"门之后应合法"的形状在 C# 侧其实也报错（读码不够），**停手上报**，不要按想象改语义。
- 构建面独占（BC2012）；agent 禁跑全量回归。

## 四、派工前补的 C# 侧实读（解决 §一判据 4 的"非脚本面"疑问）

- **`optionsEnded` 的置位点本身就在脚本块里**：C# 的 `optionsEnded = true` 在 `CSharpCommandLineParser.cs:330`，而它外层是 `if (IsScriptCommandLineParser)`（`:308`，`case "-": // csi -- script.csx` 在 `:313`）。⇒ **csc（非脚本）路径上该标志恒为 `false`**，`:169`/`:174` 两处门对 vbc 等价于恒假析取 ⇒ "补门"对 VB 非脚本面的影响**可证为零**，不需要额外分叉理由（硬约束第 2、3 条在此自动满足）。
- **C# 区分 `-` 与 `--` 的判据是 `if (arg == "-")`**（`:315`），且带 `if (value != null) break;`（`:314`）⇒ VB 侧对应改法是 `Case "-"`（`VisualBasicCommandLineParser.vb:477`）里按 `arg` 是否逐字 `"-"` 分流，`"--"` 走置位。
- **一处已知的 C# 不对齐，不动**：VB 的 `Case "-"` **没有** `value IsNot Nothing → 交给后续通用处理` 这一条（C# `:314`）。补它会改变 `-` 带值时的既有形状（§一判据 5 明令"不得顺手改掉 `-` 的既有可用形状"）⇒ 只登记为后续问题，实施者若认为必须一并改，先在流水账里给读数（改前/改后各跑一次）再判，不许静默扩大。

## 五、main 收口补记（F03/F04，2026-09-24）

- **OE 格子编号确认**（流水账 §6 请 main 核对，因本目录缺 `test-plan.md`、实施者按 §一/§二 自拟）：`ScriptOptionsEndedTests`（`Compilers\VisualBasicCommandLineTest\`，8 格）+ `CommandLineRunnerTests` 宿主 region（2 格）落位与本计划 §二 F02 一致——**行为格 OE-A1/A2/A3**（`--` 之后 `@`/`/`/`-` 形状皆成源文件、不撞断言、不报 BC2007）、**不变锁 OE-B1/B2/B3**（`--` 之前照常、单 `-` 仍是 stdin、只有逐字 `--` 结束选项）、**非脚本免疫 OE-C1/C2**（vbc 形状修前＝修后）、**宿主 OE-H1/H2**（`vbi -- @main.vbx` / `vbi -- main.vbx /alpha @beta`）。命名以 OE-A/B/C/H 为准登记入册。
- **§一判据 4 的既有钉子回收（超子代理授权面，由 main 补做）**：`Scripting\VisualBasicTest\ScriptModeArgsTests.vb` 的格 2 `DoubleDashInteractiveMode_SeparatesTheSourceFileFromTheScriptArguments` 曾把"`--` 之后的 option 形状 token = BC2007"当 **D5 分叉**钉桩（第二断言 `Assert.Contains(… BC2007)`）。方向 A 落地后该分叉**消失**（C# 同形：`/arg2` 成源文件、零 BC2007），该钉子随行为改判——已改断为 `DoesNotContain BC2007` + `Contains SourceFiles 文件名 = "arg2"` + `ScriptArguments = {"script.vbx"}`（与绿灯的 OE-A2 同读法），并改写其 doc/内联注释里的 "divergence pinned (D5)" 旧措辞。格 7/格 8 的 "not asserted" 注为改进前口径，其断言在新门下一字不改仍通过（L2 实测复绿），措辞留待后续 polish。
- **回归读数（主线亲自复跑）**：`CommandLine` 门 483/476/7/0（`scripts\verify-vb-compiler-tests.ps1` 基线 475→483）；余六门数字逐字未变（Phase2 143、Syntax 4098/4095/3、Symbol 3407/3383/24、Semantic 5862/5758/104、IOperation 1574/1566/8、Emit 4380/4277/103）；`Scripting\VisualBasicTest` 直跑 **766/0**（首跑 1 红＝§4.5 已知偶发 `LibraryNotFoundMessage` 文化泄漏，与 `--` 无关，复跑即绿）。
