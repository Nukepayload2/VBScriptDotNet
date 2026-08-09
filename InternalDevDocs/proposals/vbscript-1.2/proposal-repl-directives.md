# REPL 指令系统 / REPL Directives

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的指令系统：`#R` 引用程序集、`#help`、`/help`、`/version`、`/?`、`@vbi.rsp`（`/r:` 与 `/imports:`）、`/i` 强制交互、`/`（stdin 脚本）、`-- script-args`。这些指令对齐 C# REPL（csi）的交互与命令行入口能力。

## Motivation
[motivation]: #motivation

指令是 REPL 与命令行工具的「配置面」：程序集引用、帮助、版本、响应文件、stdin 输入、参数透传。1.2 已具备以上指令，支撑脚本与交互两种使用形态。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（1.2 版本归档，事实自包含）：

- `#R`：引用程序集。
- `#help`、`/help`、`/?`：帮助信息。
- `/version`：版本信息。
- `@vbi.rsp`：响应文件，含 `/r:`（引用）与 `/imports:`（导入命名空间）。
- `/i`：强制进入交互模式。
- `/`：stdin 脚本。
- `-- script-args`：脚本参数透传给 globals 的 `Args`。

注意：`#Load "file.vbx"` 是 **2.0** 从 C# interactive 移植的，**不属于 1.2**，不列入本档。

## Drawbacks
[drawbacks]: #drawbacks

- 指令与顶层代码共用解析路径，指令拼写错误可能被当作源码编译，产生误导性诊断。

## Alternatives
[alternatives]: #alternatives

- 保持与 csi 一致的指令形态（1.2 现状）；2.0 补充 `#Load` 以缩小与 C# interactive 的功能差距。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done）。
