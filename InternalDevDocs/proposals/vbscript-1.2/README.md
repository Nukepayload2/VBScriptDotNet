# VBScript.NET 1.2 已实现特性归档（done）

本目录仿照 csharplang 的 `proposals/csharp-<版本>/` 与 modvb 的 `proposals/vbscript-<版本>/` 惯例，归档**已随 VBScript.NET 1.2 版（微软商店已发布）实现并定稿**的功能点提案（done 档）。所有条目均为**已发布能力记录**（对齐 C# REPL 的粗粒度功能点），不是新提案。

## 版本快照

- **1.2 beta（微软商店版）**：MSIX 包，包名 `N2ForkVBInteractivePreview`，Identity Version=`1.2.0.0`。
- 基于稳定版 Roslyn NuGet（net6.0 → net8.0），并加入 .NET Framework 4.8 支持。
- 几乎原封不动：仅通过改 common scripting 做 workaround 启用了 vbx 文件执行（**1.2 历史状态**）。
- 能力来源：Roslyn scripting 交互模式的基础能力，对齐 C# REPL。

## 功能点索引

| # | 文件名 | 功能点（一句话） |
|---|--------|------------------|
| 01 | `proposal-repl-interactive-session.md` | REPL 交互会话：`>` 提示符、多行续行 `.`、表达式求值以 VB 格式打印（`? ` 前缀） |
| 02 | `proposal-repl-directives.md` | 指令系统：`#R` 引用、`#help` / `/help` / `/version` / `/?`、`@vbi.rsp`、`/i`、`/`（stdin）、`-- script-args` |
| 03 | `proposal-top-level-code.md` | 顶层代码免包装：`Dim` / `Sub` / `Function` / `Class` / `Module` 无需模块包装 |
| 04 | `proposal-script-globals.md` | 脚本 globals：`CommandLineScriptGlobals` / `InteractiveScriptGlobals`（`Args`、`Print`、搜索路径） |
| 05 | `proposal-vbx-script-execution.md` | .vbx 脚本文件执行：`vbi script.vbx [-- args]`（common scripting workaround，1.2 历史态） |
| 06 | `proposal-runtime-host-selection.md` | 运行时宿主选择：按头部 `' Attribute TargetFramework = "net48"` 注释选择 net48 或 .NET 宿主 |

## 归档规则

- **判入标准**：功能已在 VBScript.NET 1.2 发布版中实现（对齐 C# REPL），状态行全部为完成态（`Implementation: Complete` / `Specification: Complete`）。
- **附注（1.2 损坏项，2.0 修复，不作为 1.2 能力）**：顶层 `Await`、顶层 `AddHandler` / `RemoveHandler`、`Imports` 交互模式在 1.2 为损坏状态；均已由 2.0 beta 修复（见 `../../meetings/meeting-vb-repl-parity-with-csharp-repl.md`）。`#Load` 为 2.0 从 C# interactive 移植，**不属于 1.2**。
- **会议纪要**：done 提案的会议纪要归档到 `meetings/vbscript-<版本>/`（本档暂无独立会议纪要，本档为 1.2 版本归档，事实自包含）。
