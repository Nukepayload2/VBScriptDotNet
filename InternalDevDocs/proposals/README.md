# VBScript.NET 产品语法建议索引

本文档登记 **VBScript.NET 产品自身**的提案（proposals 层），按 vblang/csharplang 规范组织为「建议 → 会议 → spec」三层结构。每个独立的语法/能力增强对应一份提案文档。

**与 modvb 的关系**：`../modvb/`（Anthony 的 ModVB 提案库）是 VBScript.NET 的**参考来源**；本目录描述 VBScript.NET 产品自身的提案，不依赖 Anthony 原文，两者分离。产品提案基于产品版本归档（`vbscript-1.2/`）与设计推理。

## 建议状态（四类，仿 csharplang / modvb 组织）

| 状态 | 位置 | 含义 |
|------|------|------|
| **active** | `proposals/` 根目录 | 设计在推进（RESOLUTION **Active** / **Consider**），准备进入实现 |
| **inactive** | `proposals/inactive/` | 有前景但暂不优先 / 未定型（RESOLUTION **Table**） |
| **rejected** | `proposals/rejected/` | 否决（RESOLUTION **Reject**） |
| **done** | `proposals/vbscript-<版本>/` | 已随 VBScript.NET 发布版本实现并定稿（成员见下，1.2 版已归档） |

- **判定依据**：各会议纪要「三态判定」小节（`../meetings/`，与提案 1:1 同名镜像组织）。
- **分类反映当前设计意图**，不排斥后续复活（inactive/rejected 可因信号回升）或归档（active 完成后转 done）。
- 会议纪要目录与提案目录同步组织：`meetings/` ↔ `proposals/`、`meetings/inactive/` ↔ `proposals/inactive/`、`meetings/rejected/` ↔ `proposals/rejected/`。
- 状态行（模板顶部）：`Proposed / Prototype / Implementation / Specification` 复选框标记进度。

---

## Active（11 份，根目录）

> RESOLUTION = Active 或 Consider。编号为产品提案序列号。Proposed 未定三态的提案暂列 active 根目录，判定待 LDM 会议评估。

| # | 文件名 | 建议 |
|---|--------|------|
| 01 | `proposal-avalonia-ise-repl-ui.md` | Avalonia UI + Avalonia Edit 仿制 PowerShell ISE 的图形化 REPL/脚本编辑器（Consider，易用性提升） |
| 02 | `proposal-optional-question-prefix.md` | REPL 表达式开头问号 `?` 可选，对齐 C# REPL 自动打印表达式结果（Active） |
| 03 | `proposal-vscode-extension-ise-repl-ui.md` | VS Code 扩展：在 VS Code 内置 REPL + 脚本编辑（仿 vscode-powershell 架构，宣传/触达 vs 内存/轻量，与 Avalonia 双路线互补）（Proposed，待 LDM 评估） |
| 04 | `proposal-byref-like-safety.md` | byref-like 类型安全（对齐 C# `ref struct`）：识别 `IsByRefLikeAttribute` + suppress ref struct obsolete error + 移植 RefStructHelper BCX 规则进编译器 + REPL/脚本顶层约束；**对 vbx 与常规编译模式都生效**，追上 .NET 生态（`Span`/`allows ref struct` 接口）的重要一步（Proposed，设计来源 `meeting-byref-like-repl-safety.md`，服务于 p1 前置-1 D1） |
| 05 | `proposal-shebang-directive.md` | `.vbx` 首行 `#!` shebang 指令（编译器语法层，镜像 C#；**已实现**，见 `../tasks/shebang-directive/`） |
| 06 | `proposal-consume-csharp-extension-and-interface-shared.md` | 消费 C# 扩展成员（扩展属性/运算符，扩展方法已可用）与接口共享成员（SAIM：`T.Zero`/`T.Add` 经类型参数）；**RESOLUTION Active（D4 P1 判入）**，见 `../meetings/meeting-consume-csharp-extension-and-interface-shared.md` |
| 07 | `proposal-distribute-compiler-nuget-package-and-dotnet-tool.md` | 把 fork 编译器分发为 Toolset 风格 NuGet 包（`Nukepayload2.Compilers.VBScriptDotNet` 2.0.0-Beta，.vbproj 引用即用 fork VB 编译器；不含 csc、不替代 dotnet 工具链）与 `.net tool`（`Nukepayload2.Compilers.VBScriptDotNet.Cli`，`vbi` 命令：复用现有 vbi 二进制，批量编译 + `.vbx` 执行 + shebang 解释器；编译器 NuGet 包不是 vbi，tool 才是 vbi）；**RESOLUTION Active**（四条 Unresolved 闭合：隐式分派 / 不补 VBCSCompiler / v1 仅 .NET SDK / 双行版本），见 `../meetings/meeting-distribute-compiler-nuget-package-and-dotnet-tool.md` |
| 08 | `proposal-script-optimization-level.md` | 脚本编译优化级别：`/optimize+` 对脚本生效（透传 `arguments.CompilationOptions.OptimizationLevel`，默认仍 Debug），服务高 CPU 脚本用例（Active，见 `../meetings/meeting-script-optimization-level.md`） |
| 09 | `proposal-vbi-script-diag-mode.md` | vbi 脚本模式诊断检查 `/check`：只编译输出错误+全部警告、不执行不落盘（复用 `Script.Compile()`，对标 `cargo check`），服务 AI 开发 vbx / CI 编译门；**命名由 meeting RESOLUTION 从提案原 `/diag` 改为 `/check`**（AI 先验标准）（Active，见 `../meetings/meeting-vbi-script-diag-mode.md`） |
| 10 | `proposal-consume-ref-readonly.md` | 消费 C# `ref readonly` 返回（ReadOnlySpan 索引器 / `GetPinnableReference`）：豁免 required `modreq(In)`（**返回 + 参数双路径**，顺带解锁虚方法/委托 `in` 参数）+ `ReturnsByRefReadonly` 只读追踪 + 按接收方分类写入语义（直接赋值/复合赋值/Mid/With 块成员写/链式成员写复用 `ERR_LValueRequired`(30068) 拒——With 与链式一致，复会 2026-09-01 修正 R8 定稿；ByRef 实参 copy-out 传副本写回丢弃，推断褪 ByRef）；**RESOLUTION Active（D4 P1）**，见 `../meetings/meeting-consume-ref-readonly.md`；For Each over ReadOnlySpan 顺带解锁（S24 已翻转通过）；**已实现，spec 见 `../spec/spec-consume-ref-readonly.md`** |
| 11 | `proposal-in-parameter-recognition.md` | 识别 `in` 参数（只读引用）并让只读来源（`ref readonly` 返回）传 `in` 参数从 copy-out 改零拷贝直传，对齐 C#（C# 侧 `ref readonly`→`in` 零拷贝直传实锤：规范 `readonly-ref.md` + 编译器 `EmitAddress.cs`）；三段实现（crack `[IsReadOnly]`→`RefKind.In` / 绑定放行 / 发射直传）；**R7 后续项、Proposed 待立项**（父 RESOLUTION `meeting-consume-ref-readonly.md` R7「v1 不做、等性能证据独立立项」） |
| 12 | `proposal-net472-desktop-branch.md` | Toolset 编译器包补 **net472 桌面分支**（`tasks/net472`：netstandard2.0 编译器库 + net472 `vbc.exe`+rsp+config + net472 任务 DLL），让 Visual Studio 的 .NET Framework MSBuild（Full host）能用 fork VB 编译器——**修正 distribute v1「net472 允许缺失」的未实测定案**；不做 bridge、不引 VBCSCompiler（沿用 `UseSharedCompilation=false`）、VB-only；props 按 `MSBuildRuntimeType` 分派（Core→netcore / Full→net472）；双老登评审 + RESOLUTION R1–R9，见 `../tasks/net472-desktop-branch/`（含实现与验证） |

---

## Inactive（暂无成员，`inactive/`）

- 机制：RESOLUTION = Table（搁置/未定型）的提案放入 `inactive/`，与 modvb 的 inactive 目录同约定。
- 当前无成员。

---

## Rejected（暂无成员，`rejected/`）

- 机制：RESOLUTION = Reject 的提案放入 `rejected/`，与 modvb 的 rejected 目录同约定。
- 当前无成员。

---

## Done（vbscript-1.2/）

- 机制：特性随 VBScript.NET 发布版本实现并定稿后，归档到 `proposals/vbscript-<版本>/`（仿照 csharplang 的 `proposals/csharp-<版本>/` 与 modvb 的 `proposals/vbscript-1.0/` 归档规则）。
- **判入标准**：对应 LDM 判定 Active 且 `Implementation`/`Specification` 进度完成。
- **当前成员（1.2 版，微软商店已发布）**：VBScript.NET **1.2 beta** 已发布到微软商店（MSIX，`N2ForkVBInteractivePreview`，Identity Version=`1.2.0.0`），随其发布的「对齐 C# REPL」能力已归档到 [`vbscript-1.2/`](vbscript-1.2/README.md)：

  | # | 功能点（一句话） | 文件 |
  |---|------------------|------|
  | 01 | REPL 交互会话（`>` 提示符、多行续行 `.`、`?` 前缀打印） | `vbscript-1.2/proposal-repl-interactive-session.md` |
  | 02 | 指令系统（`#R`、`#help`、`/help`、`/version`、`/?`、`@vbi.rsp`、`/i`、`/` stdin、`-- script-args`） | `vbscript-1.2/proposal-repl-directives.md` |
  | 03 | 顶层代码免包装（`Dim` / `Sub` / `Function` / `Class` / `Module`） | `vbscript-1.2/proposal-top-level-code.md` |
  | 04 | 脚本 globals（`CommandLineScriptGlobals` / `InteractiveScriptGlobals`：`Args`、`Print`、搜索路径） | `vbscript-1.2/proposal-script-globals.md` |
  | 05 | .vbx 脚本执行（`vbi script.vbx [-- args]`，common scripting workaround） | `vbscript-1.2/proposal-vbx-script-execution.md` |
  | 06 | 运行时宿主选择（按 `' Attribute TargetFramework = "net48"` 注释选 net48 或 .NET 宿主） | `vbscript-1.2/proposal-runtime-host-selection.md` |

- 说明：1.2 是已发布版本，已随其发布的能力归档到 `vbscript-1.2/`；顶层 `Await`、`AddHandler` / `RemoveHandler`、`Imports` 为 1.2 损坏项、已由 2.0 修复（见 `../meetings/meeting-vb-repl-parity-with-csharp-repl.md`），不作为 1.2 功能点。
