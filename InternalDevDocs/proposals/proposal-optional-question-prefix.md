# REPL 表达式开头问号可选 / Optional Question Mark Prefix for REPL Expressions

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [x] Specification: [Complete](../spec/spec-optional-question-prefix.md)

## Summary
[summary]: #summary

本提案让 REPL 表达式开头的 `?` **可选**：`Now` 直接求值打印，不必写 `?`。原本因"表达式作为语句、结果被丢弃"而报错的地方（如 **BC30545**），按"开头写了 `?`"的策略自动求值并打印。显式 `?` 前缀保留，行为兼容不变。

## Motivation
[motivation]: #motivation

- **现状**：REPL 里 `? Now` 打印值；但如果只写 `Now`，报 **BC30545: 属性访问必须分配给属性或使用属性值**（property access must be assigned to a property or use its value）。对交互式求值场景，这是一个噪音错误——用户就是想看 `Now` 的值。
- **对齐 C# REPL**：C# REPL（`csi`）里直接输入 `DateTime.Now` 即求值打印，无需任何前缀。VB REPL 要求 `?` 前缀是两者最显眼的功能差距（见 `meeting-vb-repl-parity-with-csharp-repl.md`）。
- **降低新用户认知负担**：新用户（尤其从 C# interactive / PowerShell 迁移者）会下意识直接敲表达式，得到一个看不懂的编译器错误。让 `?` 可选后，"敲表达式就出结果"成为直觉行为。

## Detailed design
[design]: #detailed-design

### 识别"表达式作为语句"的提交

- 当单个提交解析为一条**表达式语句**（expression statement）、且该表达式的值被丢弃时，进入"自动打印"判定。
- 判定依据：提交语法树的根是表达式语句；且该表达式不属于"本身就是合法语句"的形态（见下节区分）。

### 自动按 `?` 前缀语义求值并打印

- 当该表达式会导致"结果被丢弃"类诊断（**BC30545** 等）时，改为按 `?` 前缀语义求值，并以 VB 格式打印结果。
- 候选诊断清单（首版实现时逐项核实并维护）：`BC30545`（属性访问未使用）；以及同类"表达式的值被忽略"的报错。
- 实现思路：跟随 C# REPL 的设定，在编译器层把 Script kind 提交的**末尾表达式**绑定为 RValue（抑制「值被丢弃」类诊断）、解析层把语句首裸表达式解析为表达式语句，走 `?` 同一条打印路径（`HasSubmissionResult` + `globals.Print`），宿主层零改动。见 `../meetings/meeting-optional-question-prefix.md` RESOLUTION #4。

### `?` 显式前缀保留兼容

- `? Now` 照旧打印；无任何行为变化。显式前缀仍是"我就是要打印"的明确表达。

### 与非打印语句的区分（关键边界）

| 提交 | 现状 | 提案后 |
|------|------|--------|
| `? Now` | 打印 | 打印（不变） |
| `Now` | BC30545 报错 | **自动打印** |
| `x = 5` | 赋值，正常 | 赋值，**不打印**（赋值是合法语句） |
| `Console.WriteLine("hi")` | 调用，正常 | 调用，**不打印**（带副作用调用是合法语句） |
| `1 + 2` | 值被丢弃类错误（码待实证） | **自动打印** 3 |
| `x > 5` | 值被丢弃类错误（码待实证） | **自动打印** False |

- **判定原则**：仅当"表达式作为语句会产生值被丢弃类错误"时才自动打印；本身合法的语句（赋值、带副作用的调用、`ReDim`、`With` 等）**不改变行为**。这样把行为变化严格限定在"原本就是错误"的提交上，不引入静默语义漂移。

### 与 Function Main 退出码语义的关系

- 2.0 beta 已修复的退出码语义是：`Return 42` → 退出码 42，裸 `Return`/无 `Return` → 0，**末尾表达式不再设退出码**。
- 本提案的自动打印**与退出码正交**：自动打印只是把结果回显到交互式输出，不影响 `vbi script.vbx` 脚本模式的进程退出码。脚本模式不启用自动打印（仅交互式 REPL），避免脚本行为漂移。

### 边界情况

- 属性访问（`Now`、`DateTime.Now`）、无参方法调用、算术表达式（`1 + 2`）、比较表达式（`x > 5`）、字符串表达式（`"a" & "b"`）。
- 多行续行提交（`.` 续行）末尾的表达式是否自动打印——建议与单行提交一致：若整段提交以表达式语句收尾且值被丢弃，则打印。
- `Option Strict` 开关下表达式语句的错误形态是否一致；宽松模式下部分表达式可能被静默转换而不再报 BC30545，需在实现时核对。

## Drawbacks
[drawbacks]: #drawbacks

- **静默打印的风险**：原本"表达式作为语句"是错误，会暴露用户写错了（例如想调用一个 Sub 却写成了属性、或忘了赋值）。自动打印后，这类错误变成"打出一个值"，可能掩盖意图错误。
- **与 VB 语句优先的既有习惯冲突**：VB（继承 VB6/VBA）中裸表达式不是合法语句，`?` 是 REPL 特有的打印指令。把 `?` 变得可选，等于在 REPL 里引入"裸表达式合法"的特殊规则，与 C# 一样在交互方言与普通语言之间划出差异。
- **判定启发式的边界成本**：维护"哪些表达式算值被丢弃、哪些是合法语句"的清单，且要逐版本核对新语法带来的新形态，长期维护面。

## Alternatives
[alternatives]: #alternatives

- **保持强制 `?`**：现状，零改动；但对齐 C# REPL 的目标落空，BC30545 噪音保留。
- **报错文案优化**：保留强制 `?`，但把 BC30545 换成更友好的提示（"输入 `? Now` 求值并打印"）。成本低，但不解决"敲表达式就出结果"的直觉落差。
- **`.` 续行后自动进入打印模式**：只在多行续行时自动打印末尾表达式，单行仍强制 `?`。范围更窄，但与 C# REPL 对齐度不足。
- **引入新的打印关键字**（如 `print Now` 或保留 `?` 并新增 `Show`）：语法上更显式，但违背"降低认知负担"的初衷。

## Unresolved questions
[unresolved]: #unresolved-questions

- "结果被丢弃"的精确诊断清单（除 BC30545 外还有哪些错误码）需在实现时逐项枚举并回归。
- 自动打印是否仅限交互式 REPL，还是也作用于 `@vbi.rsp` / stdin 脚本输入（倾向仅交互式，待定）。
- 打印格式是否完全复用 `?` 的 VB 格式（`ObjectFormatter`），还是需要区分。
- 与未来可能的"表达式结果赋值"类新语法（如 `?` 之后带变量名）如何共存。
