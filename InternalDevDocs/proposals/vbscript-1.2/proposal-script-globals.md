# 脚本 globals / Script Globals

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的脚本 globals：`CommandLineScriptGlobals` / `InteractiveScriptGlobals`，提供 `Args`、`Print` 与参考/源码搜索路径，对齐 C# REPL（csi）的 globals 机制。

## Motivation
[motivation]: #motivation

globals 把「脚本环境」暴露给顶层代码：命令行参数、打印方法、程序集与源码解析的搜索路径。这是脚本可带参执行、可打印输出、可引用外部源码的基础。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（1.2 版本归档，事实自包含）：

- `CommandLineScriptGlobals`：命令行脚本模式的 globals。
- `InteractiveScriptGlobals`：交互模式的 globals。
- 成员：`Args`（脚本参数，来自 `-- script-args`）、`Print`（打印输出）、参考/源码搜索路径（解析时查找程序集与源码的目录集）。

## Drawbacks
[drawbacks]: #drawbacks

- 两套 globals 部分重叠，交互与命令行形态的行为差异需要文档说明。

## Alternatives
[alternatives]: #alternatives

- 统一为单一 globals（1.2 未采用）；保留两套以区分交互与命令行形态。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done）。
