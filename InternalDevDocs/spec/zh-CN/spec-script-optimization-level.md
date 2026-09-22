# 脚本编译的优化级别

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-script-optimization-level.md)

## 概述
[summary]: #summary

本规范通过既有的 `/optimize` 命令行开关，让 Visual Basic 脚本的编译器优化级别可配置——交互窗口与脚本文件执行皆适用。本特性之前，脚本宿主以 `OptimizationLevel.Debug` 编译每个提交。宿主构造脚本选项时把 `Debug` 写死，而脚本模式命令行解析器根本不识别 `/optimize`，于是该开关被当作未知选项拒绝。因此 CPU 密集型脚本永远无法以 Release 优化编译，而这一缺口在编译器层：JIT 分层编译无法挽回 Debug IL 已固定进程序集的那些 nop 与未经优化的局部变量布局。

本特性之后，`/optimize+`（或 `/optimize`）对脚本文件执行生效，并在交互启动时对交互窗口生效；`/optimize-` 恢复为 Debug；默认仍为 Debug。该开关既从命令行接受，也从响应文件接受，因此写了 `/optimize+` 的响应文件确立整个会话范围的 Release 默认。优化级别在交互会话启动时固定，会话中途不可更改。`/debug` 有意不予透传；脚本宿主对调试信息保持与 C# 交互宿主相同的策略。

C# 交互宿主共享本特性所改动的那个命令行 runner，但其脚本模式解析器未被扩展，因此 C# 交互的行为不变。

## 动机
[motivation]: #motivation

运行 CPU 密集代码——数值循环、状态机、重度使用闭包的路径——的脚本由 Debug IL 编译而来。Visual Basic 编译器的 Debug 构建会发射 nop 并使局部变量布局保持未经优化；JIT 随后在运行期优化热点路径，但它无法撤销已固定进 IL 的 Debug 决策：

- 标签与局部变量优化只在 Release 下运行，
- 合成局部变量槽只在 Release 下复用，而这正是迭代器、异步状态机与闭包获益最大之处，且
- 除非编译为 Release，代码生成器都选择一种对调试友好的发射风格。

这些都以 `OptimizationLevel.Release` 为门槛，因此脚本获益的唯一途径就是以 Release 编译。

该开关本身早已是 Visual Basic 命令行的一等成员：普通编译器把 `/optimize`、`/optimize+` 与 `/optimize-` 解析进编译选项。两处彼此独立的缺口使它对脚本无效。其一，脚本模式命令行解析器——脚本宿主使用的一个独立解析器——不识别 `/optimize`，因此传入该开关会产生未知选项警告。其二，即使开关进入了解析所得参数，脚本宿主也忽略编译选项，并以写死的 `OptimizationLevel.Debug` 构造每一个 `ScriptOptions`。消费端——脚本编译器——本已会从脚本选项中读取优化级别，因此补上这两处缺口即打通整条管线。

C# 交互宿主有同样的结构与同样的缺陷：它共享上文所述那个构造脚本选项的命令行 runner。C# 的脚本模式解析器同样不识别 `/optimize`。本特性修复 Visual Basic 一侧，并保持 C# 一侧不变。

## 详细设计
[design]: #detailed-design

### 脚本模式中的 `/optimize` 开关

Visual Basic 命令行的脚本模式解析器识别三种开关形态：

| 开关 | 效果 |
|---|---|
| `/optimize` | 启用优化 |
| `/optimize+` | 启用优化 |
| `/optimize-` | 禁用优化 |

每种形态都必须不带取值地给出；`/optimize:on` 之类的取值形态会被拒绝并报错，与普通编译器开关一致。该开关置位一个默认为 `False` 的局部布尔量。编译选项从该布尔量导出优化级别：置位时为 `OptimizationLevel.Release`，否则为 `OptimizationLevel.Debug`。

### 宿主透传

脚本宿主从解析所得的命令行参数构造其 `ScriptOptions`。该构造现在从参数的编译选项读取优化级别，而不再把 `Debug` 写死：

```csharp
optimizationLevel: arguments.CompilationOptions.OptimizationLevel,
```

由于开关布尔量默认为 `False`，未带 `/optimize` 启动的宿主以 `OptimizationLevel.Debug` 编译——与之前的行为完全相同。脚本选项构造中其余部分不变：允许 unsafe、溢出检查关闭、警告级别为 4、解析选项照旧透传。

### 行为

下表概述可观察行为。交互窗口与脚本文件执行共享同一条宿主路径，因此命令行上给出的开关对两者都适用。

| 调用 | 优化级别 | `#If DEBUG` | 备注 |
|---|---|---|---|
| 默认（无开关） | **Debug** | `False` | 未变 |
| `vbi /optimize+ script.vbx` | **Release** | `False` | CPU 密集型脚本 |
| 响应文件写 `/optimize+` | **Release** | `False` | 整个会话范围的默认 |
| `vbi /optimize+`（交互） | **Release** | `False` | 启动时固定 |
| `vbi /optimize-` | **Debug** | `False` | 显式 Debug |
| `vbi /define:DEBUG` | **Debug** | **`True`** | Debug 配置：默认优化加 `DEBUG` 符号 |

**响应文件。** 响应文件（`@file`）在解析之前被展开进参数列表，因此写在响应文件里的 `/optimize+` 与它出现在命令行上完全等效地生效。随宿主发布的默认响应文件既不含 `/optimize` 也不含 `/define`，所以开箱状态下宿主以 Debug 编译。

**交互窗口。** 优化级别在会话启动时固定。每个提交作为一次独立编译被编译，而宿主的选项更新路径有意保留优化级别，因此该级别在整个会话内保持一致。没有运行期开关可在会话中途改变它。

### Release 语义与 `DEBUG` 符号

不带 `/define:DEBUG` 的 `/optimize+` 意味着经过优化的 IL，且 `#If DEBUG` 求值为 `False`——与 MSBuild 的 Release 配置是同一组合（`Optimize=true` 且没有 `DEBUG` 定义）。Visual Basic 编译器默认不定义 `DEBUG`；预定义符号仅限于版本符号与目标符号，用户符号只来自显式的 `/define`。因此没有需要清除的符号：没有 `/define` 时，`#If DEBUG` 自然为 `False`。这些开关彼此正交——`/optimize+` 不定义任何符号，`/define:DEBUG` 也不影响优化级别——但把它们组合起来并非有意义的配置：`DEBUG` 符号置位的 Release 构建没有真实用例，因此本规范不将其作为一种配置给出。所期望的 Release 语义恰为 `/optimize+` 且不带 `/define:DEBUG`。

默认响应文件不定义 `DEBUG`，本规范也不添加它。所谓「Debug 配置」——`#If DEBUG` 为真且优化级别为 Debug——由用户以 `/define:DEBUG` 组装而成。另有一种做法曾予考虑并遭否决：在默认响应文件里定义 `DEBUG`、在 Release 响应文件里清除它。`/define` 开关是累积式的，可以覆盖符号但不能移除符号，因此 Release 响应文件除非写下 `DEBUG=False` 或替换整个响应文件就无法取消 `DEBUG` 定义——这一代价超过收益。

### 与调试信息的正交性

优化级别与调试信息的发射是彼此独立的开关。脚本宿主继续忽略 `/debug`：`emitDebugInformation` 仍为 `!InteractiveMode`，因此交互窗口不发射可移植调试文件，而脚本文件执行总是发射一个。由于脚本模式解析器不识别 `/debug`，在脚本模式传入它会产生未知选项警告（BC2007），会话继续；这与既有行为一致，且不受本特性影响。透传的只有 `/optimize`。

### 边界：C# 交互

C# 交互宿主共享本特性所改动其脚本选项构造的那个命令行 runner，但其脚本模式解析器不识别 `/optimize`。由于本规范不扩展 C# 解析器，共享的透传对 C# 交互读到的值永远是 `Debug`，因此 C# 交互保持其之前的行为。

## 缺点
[drawbacks]: #drawbacks

- **Release 下失去调试体验。** Release 编译去掉 nop 并复用局部变量槽，因此交互窗口里的断点与变量查看质量下降。传入 `/optimize+` 的用户是明确接受这一权衡的；默认会话不受影响。
- **会话级默认需要响应文件。** 「默认全部优化」靠把 `/optimize+` 写进响应文件来表达，而不是靠环境变量。对「改一行、全部优化」这一心智模型来说这是一层很小的间接，但响应文件本就是宿主的配置面。
- **级别在启动时固定。** 中途转向 CPU 密集的会话，除非重启否则无法重新优化（见替代方案）。

## 替代方案
[alternatives]: #alternatives

- **环境变量。** 读取形如 `VBI_OPTIMIZE` 的变量，优先级为命令行 > 环境变量 > 默认。否决理由：编译选项在该代码库中没有环境变量的先例，该变量容易与运行期的 `DOTNET_*` JIT 变量混淆，而会话级默认已可通过响应文件达成。
- **会话内的运行期开关。** 形如 `#optimize+` 的指令，在会话中途改变级别。否决理由：已编译的提交无法重编，因此该开关只影响后续提交，而一个在会话内悄然不一致的级别是不干净的语义。
- **默认为 Release。** 默认开启优化。否决理由：它改变每个会话的交互式调试体验，并把优化变成隐式行为。
- **脚本头部指令。** 逐文件的 `' Attribute Optimize = "release"` 指令。否决理由：这是 Roslyn 惯例之外的自造机制；按文件粒度的需求已由响应文件与命令行覆盖。
- **MSBuild 风格的配置。** 仿 `dotnet run` 的 `-c Release` / `--configuration` 开关。否决理由：脚本宿主不是 MSBuild 项目，也没有 `Configuration` 概念；编译器开关惯例 `/optimize+` 已承载既定的语义。
- **什么都不做。** CPU 密集型脚本永久停留在 Debug，本提案要补的缺口继续敞着。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 与 C# 交互宿主的对齐

C# 交互宿主以 Debug 编译，且在脚本模式下不识别 `/optimize`。本特性让该边界保持完整：Visual Basic 一侧获得该开关，C# 一侧不变。因此共享宿主代码对 C# 交互读到 `Debug`，对 Visual Basic 交互读到解析所得的级别。扩展 C# 一侧将是另一项独立改动，有意不在范围内。

### 交互方言

交互窗口是 Visual Basic 的一种独立方言，而优化级别加入那组按宿主调用而非按提交可配置的行为之中。这一区分只在会话边界上有意义：级别在进程启动时选定，其后保持恒定，因此该方言不会长出「混合级别」模式。

## 测试
[testing]: #testing

本特性由无副作用的内存测试来验证：这些测试不进行网络访问、不启动进程、不访问注册表。响应文件与脚本文件用例只写入既有的隔离临时目录测试装置，宿主必然要读取它。

- **API 测试。** 以默认选项调用 `VisualBasicScript.Create` 所得编译对象的选项报告 `OptimizationLevel.Debug`；配以 `ScriptOptions.WithOptimizationLevel(Release)` 时，编译选项报告 `OptimizationLevel.Release`。这些测试证明消费端本已遵守脚本选项。
- **宿主解析断言。** 无开关构建的宿主报告 `Debug`；`/optimize+` 报告 `Release`；`/optimize-` 报告 `Debug`；含 `/optimize+` 的响应文件报告 `Release`；不含 `/optimize` 的响应文件报告 `Debug`。
- **冒烟测试。** 以 `/optimize+` 启动的交互会话求值 `? 1 + 2` 并打印 `3`、无错误；以 `/optimize+` 运行的脚本文件正确执行；且默认会话不变。
- **调试信息边界。** 以 `/debug:portable` 启动的会话，在解析所得参数的错误里以及在错误流上产生未知选项警告 BC2007，且会话继续正常求值。
- **端到端观察。** 宿主创建的脚本对象是执行路径内的局部量，测试无法触达。端到端行为由组合确立：上述 API 消费测试加宿主解析断言，再配合对那一行透传的代码评审，并以冒烟测试作为兜底。未引入任何内部测试钩子。

本特性新增 11 个测试用例，全部通过；Visual Basic 脚本宿主的完整测试程序集报告 194 个测试、零失败。

## 相关条目
[related]: #related-items

- [vbc `/optimize`](https://learn.microsoft.com/en-us/dotnet/visual-basic/reference/command-line-compiler/optimize) —— Visual Basic 编译器选项，脚本模式开关遵循其语义
- [csc `/optimize`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation) —— C# 编译器选项，以及 C# 交互宿主的固定行为
