# Avalonia UI 图形化 REPL/脚本编辑器（仿 PowerShell ISE）/ Avalonia-based ISE-style REPL UI

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本提案给 `vbi` 增加一个基于 Avalonia 的图形化 REPL/脚本编辑器，仿 PowerShell ISE 布局（上方脚本编辑窗格 + 下方控制台输出窗格），以改进易用性。GUI 只做前端，提交执行仍复用现有 Scripting 管线（`VisualBasicScript.vb` / `CommandLineRunner` → 改版 VB 编译器）。

## Motivation
[motivation]: #motivation

- **现状痛点**：REPL 本体是控制台程序 `vbi.exe`，商店版带 WinUI3 启动器包装（`vbichooser`/`vbicore`/`vbifw`）。脚本编辑体验差：无语法高亮、无独立编辑窗口、无"选择执行"、无法在同一个窗口里一边写脚本一边看输出。
- **心智模型**：PowerShell ISE（脚本编辑窗格 + 控制台输出窗格 + 选择执行）是脚本用户熟悉的心智模型，VB 用户群（VBScript/VBA 迁移者）对此类"脚本编辑器 + 即时输出"布局接受度高。
- **技术选型**：Avalonia 跨平台（Windows/Linux/macOS），Avalonia Edit 是成熟的编辑器控件，提供语法高亮、多行编辑、行号等基础能力；可将 Roslyn 的分类器接到 Avalonia Edit 上获得 VB 语法高亮。

期望的结果：脚本用户可以在图形界面里编辑 `.vbx` 脚本、选中片段执行、在下方窗格即时看到 `?` 打印结果与诊断，降低上手门槛。

## Detailed design
[design]: #detailed-design

### 布局草图

```
+------------------------------------------------------------+
| 菜单栏：[文件] [编辑] [运行]                      标题栏       |
+------------------------------------------------------------+
|  脚本编辑窗格（Avalonia Edit，VB 语法高亮，多行）              |
|                                                             |
|  Dim x = 42                                                  |
|  Console.WriteLine("hello")                                  |
|  ? x + 1     ← 选择执行（Ctrl+Enter）                         |
|                                                             |
+------------------------------------------------------------+
|  控制台输出窗格（只读，REPL 输出 / 诊断 / 错误）               |
|  > ? x + 1                                                   |
|  43                                                          |
+------------------------------------------------------------+
|  状态栏：当前运行时（net48 / .NET）、光标位置                  |
+------------------------------------------------------------+
```

### Avalonia Edit 集成

- 使用 Avalonia Edit 的 `TextEditor` 作为脚本编辑控件，配置 VB 语法高亮。
- 高亮可复用 Roslyn 的 `Classification`（改版 VB 编译器的分类器），把分类结果映射到 Avalonia Edit 的着色渲染；若首版不想依赖完整 Roslyn 分类管线，可先用简单关键字规则做降级方案。
- 支持多行编辑、行号、选中片段执行（Ctrl+Enter 把选中文本作为单个提交送入管线）。

### 复用现有 Scripting 管线

- 提交执行与现有 REPL 完全一致：`VisualBasicScript.vb`（`RunInteractiveAsync`）→ `CommandLineRunner` → 改版 VB 编译器（`Compilers\VisualBasic\Portable\`）。
- GUI 把用户提交/脚本文件内容喂给管线，把 `?` 打印结果与诊断输出回显到控制台输出窗格。
- 指令照常支持：`#R`、`#Load "file.vbx"`（`#Load` 打开的文件可在编辑窗格新标签打开）、`#help`、`/help`、`/version`、`/?`、`@vbi.rsp`（`/r:` 与 `/imports:`）、`/i`、`-- script-args`。
- 脚本模式：打开 `.vbx` 文件时按文件头部 `' Attribute TargetFramework = "net48"` 注释选择 net48 或 .NET 宿主，与文件关联行为一致。

### 与现有 WinUI3 商店包装的关系

- **可并存**：Avalonia 版作为独立可执行文件/进程交付，商店版（WinUI3 包装）保留不动，两者共享同一 `vbi` 执行核心。
- **或逐步替换**：若 Avalonia 版成熟，商店版可逐步把启动器体验迁移到 Avalonia（`vbichooser` 的 TaskDialog 安全警告 + 运行时框架选择可在 Avalonia 里重建）。
- 无论哪种，`vbi.exe` 控制台本体都应保留（可脚本化/可管道化是 REPL 的核心资产）。

## Drawbacks
[drawbacks]: #drawbacks

- **双 UI 技术栈**：现有商店版已是 WinUI3，再引入 Avalonia 会形成两套 UI 技术栈，维护成本上升。
- **GUI 弱化可脚本化**：控制台 REPL 可被管道/自动化消费（`echo ... | vbi`），GUI 是交互式外壳，两者使用场景不同，需明确边界。
- **编辑器集成成本**：Avalonia Edit 的 VB 语法高亮需把 Roslyn 分类器接进来，工作量与维护面都不小；降级方案（关键字规则）体验打折。
- **优先级**：这是易用性投资，不缩小与 C# REPL 的"功能差距"本身（功能差距见 `meeting-vb-repl-parity-with-csharp-repl.md`）。

## Alternatives
[alternatives]: #alternatives

- **继续纯控制台**：零 UI 成本，保持可管道化，但脚本编辑体验维持现状。
- **在现有 WinUI3 商店包装上扩展**：在 `vbicore` 里加编辑窗格；复用已有商店分发渠道，但 WinUI3 编辑器控件能力弱、跨平台无望。
- **对接 Visual Studio / VS Code**：用成熟 IDE 扩展获得编辑能力；交付与分发成本高，且偏离"轻量脚本 REPL"定位。
- **Avalonia 但只做控制台前端（Terminal）**：不做完整 ISE 布局，先做一个带高亮的单窗格终端。

## Unresolved questions
[unresolved]: #unresolved-questions

- Avalonia 版与商店版 WinUI3 包装的关系：**并存还是逐步替换**？
- 是否需要去掉控制台版 `vbi.exe`（倾向保留，未定）？
- 跨平台范围：仅 Windows，还是 Linux/macOS 一并支持？
- VB 语法高亮：完整接 Roslyn 分类器，还是先做关键字规则降级？
- 是否把 `vbichooser` 的 TaskDialog（安全警告 + 运行时框架选择）在 Avalonia 里重建，还是复用现有启动器？
