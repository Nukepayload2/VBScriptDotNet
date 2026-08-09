# REPL 交互会话 / REPL Interactive Session

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的 REPL 交互会话能力：`>` 提示符、多行续行 `.`、表达式求值以 VB 格式打印（`? ` 前缀指令）。这是来自 Roslyn scripting 交互模式的基础能力，对齐 C# REPL（csi）。

## Motivation
[motivation]: #motivation

交互式 REPL 是脚本/交互环境的核心入口。`vbi` 启动后以 `>` 提示符逐条接受提交，支持多行续行与表达式结果打印，是后续所有 REPL 能力（指令、globals、顶层代码）的载体。1.2 已具备这套基础交互形态。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（1.2 版本归档，事实自包含）：

- **`>` 提示符**：`vbi` 交互模式下每次提交显示 `>` 提示。
- **多行续行 `.`**：提交尚未完整（如跨行语句）时以 `.` 续行提示继续输入。
- **表达式求值打印**：以 `? ` 前缀指令对表达式求值，结果按 VB 格式（`ObjectFormatter`）打印。
- **提交完整性判定**：交互层在提交时判定语法是否完整，不完整则进入续行，完整才提交编译执行。

## Drawbacks
[drawbacks]: #drawbacks

- 表达式打印依赖显式 `?` 前缀，与 C# REPL「敲表达式即打印」存在认知落差；该差距由 active 提案 `proposal-optional-question-prefix.md` 跟进（非 1.2 能力）。

## Alternatives
[alternatives]: #alternatives

- 保留强制 `?` 前缀（1.2 现状）；让前缀可选属后续设计方向（active 提案），不属于 1.2。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done），不留开放问题。
