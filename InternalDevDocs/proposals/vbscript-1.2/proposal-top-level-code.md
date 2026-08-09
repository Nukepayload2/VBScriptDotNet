# 顶层代码免包装 / Top-Level Code without Wrapper

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的顶层代码能力：`Dim` / `Sub` / `Function` / `Class` / `Module` 无需显式模块包装即可在脚本/REPL 中声明与执行，对齐 C# REPL 的顶层语句体验。

## Motivation
[motivation]: #motivation

传统 VB 要求代码位于 `Module` / `Class` 内；脚本与交互场景要求直接书写声明与语句。1.2 已支持免包装的顶层声明，是脚本可执行性与 REPL 直接输入的基础。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（1.2 版本归档，事实自包含）：

- `Dim` 变量、`Sub` / `Function` 过程、`Class` / `Module` 类型声明均可直接在顶层书写，无需额外模块包装。
- 与 globals（`Args`、`Print`）配合，顶层代码可直接使用脚本环境变量。

**1.2 损坏项（2.0 修复，不作为 1.2 能力）**：顶层 `Await`、顶层 `AddHandler` / `RemoveHandler` 在 1.2 为**损坏状态**（顶层不可用）；`Imports` 交互模式在 1.2 亦失效。三者均已在 2.0 beta 修复（见 `../../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。

## Drawbacks
[drawbacks]: #drawbacks

- 顶层声明的生命周期与作用域跨越多个提交，错误定位比单文件编译模糊（尤其多行续行场景）。

## Alternatives
[alternatives]: #alternatives

- 强制显式模块包装（传统 VB 形态，1.2 未采用）；顶层免包装是脚本/交互友好形态。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done）；顶层 `Await` / `AddHandler` 的修复归属 2.0。
