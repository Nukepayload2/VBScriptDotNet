# .vbx 脚本文件执行 / .vbx Script File Execution

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的 `.vbx` 脚本文件执行：`vbi script.vbx [-- args]`。1.2 通过修改 common scripting 做 workaround 启用（**1.2 历史状态**）。

## Motivation
[motivation]: #motivation

脚本文件执行是非交互的主使用形态。1.2 基于稳定版 Roslyn + common scripting fork，为启用 vbx 文件执行做了最小 workaround；2.0 已将该 workaround 清理为直接返回退出码。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（**1.2 历史状态**，1.2 版本归档，事实自包含）：

- `vbi script.vbx [-- args]`：以脚本文件方式执行，`--` 后参数透传 globals 的 `Args`。
- **workaround**：把 `Microsoft.CodeAnalysis.Scripting`（common scripting）源码 fork 进仓库（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`），在 `RunScript` 中用 `Script.CreateInitialScript(Of Object)` 创建初始脚本，再 `(ReturnValue As Integer?)` 取退出码——因上游 `CreateScriptCompilation` 把返回值硬编码为 `Object`。
- 2.0 beta 已改为 `Script.CreateInitialScript<int>` 直接返回退出码（`RunAsync(...).ReturnValue`），不再走 Object + 强转（见 `../../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。

## Drawbacks
[drawbacks]: #drawbacks

- 1.2 依赖 fork common scripting 并以 Object/强转取退出码，属历史 workaround；2.0 已以 `<int>` 泛型清理。

## Alternatives
[alternatives]: #alternatives

- 直接使用稳定版 common scripting（不 fork）——但无法取得整数退出码，故 1.2 选择 fork workaround 方案。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done）；workaround 清理归属 2.0。
