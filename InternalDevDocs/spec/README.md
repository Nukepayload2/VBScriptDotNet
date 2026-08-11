# VBScript.NET 产品规范说明

本文档说明 **VBScript.NET 产品自身**的 spec 层。spec 层**不维护「当前状态」快照**（现状快照会随版本漂移，一旦疏于维护就会造成误导）——产品能力以**版本归档**为准：各已发布版本的能力归档在 `../proposals/vbscript-<版本>/`（如 1.2 已归档在 `../proposals/vbscript-1.2/`），各版本归档**自包含**，其能力事实即稳定记录。

**与 modvb 的关系**：`../modvb/spec/` 对应 Anthony 提案库的规范（当前为空）；本目录是 VBScript.NET 产品自身的规范说明，两者分离。

## 版本历史（稳定事实，不会漂移）

### 1.0 / 1.1 / 1.2 beta（微软商店版，已发布）

- 2023-10 初始化，用稳定版 Roslyn NuGet（net6.0），后升级到 net8.0，并加入 .NET Framework 4.8 支持。
- **1.2 beta 已发布到微软商店**（MSIX 包，包名 `N2ForkVBInteractivePreview`，Identity Version=`1.2.0.0`）。
- 1.2 几乎原封不动：顶层 `Await` 和 `AddHandler` 为**损坏状态**（顶层不能用），`Imports` 交互模式失效。
- **1.2 历史状态——common scripting workaround 启用 vbx 文件执行**：把 `Microsoft.CodeAnalysis.Scripting`（common scripting）源码 fork 进仓库（`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`），在 `RunScript` 里用 `Script.CreateInitialScript(Of Object)` 然后 `(ReturnValue As Integer?)` 取退出码（因上游 `CreateScriptCompilation` 把返回值硬编码为 `Object`）。
- **2.0 beta 已改为 `CreateInitialScript<int>` 直接返回退出码**：`RunScriptAsync`（`CommandLineRunner.cs`）用 `Script.CreateInitialScript<int>(...)` 并直接返回 `RunAsync(...).ReturnValue`，不再走 Object + 强转。

### 2.0 beta（`with-modified-vbsyntax` 分支，进行中）

- **.NET 10 + 免注册 WinUI3**（WASDK 2.2.0）。
- **fork 完整 Roslyn 编译器源码**进 `Compilers\`。
- 已修复：
  - 顶层 `Await`
  - 顶层 `AddHandler` / `RemoveHandler`
  - `Imports` 跨提交累积
  - `Function Main` 退出码语义（`Return 42` → 退出码 42；裸 Return/无 Return → 0；**末尾表达式不再设退出码**）
- **REPL 裸表达式自动打印**（表达式开头 `?` 可选）：见 `spec-optional-question-prefix.md`。
- 已移植 C# interactive 的 **`#Load`** 指令。
- **理论上和 C# REPL 不应该有功能差距**。
- 注意：代码内产品版本号仍停在 `1.2.0-beta`，「2.0 beta」是当前里程碑叫法。

## 架构链（稳定结构事实）

```
Interactive\vbi\Vbi.vb
  → Scripting\VisualBasic\VisualBasicScript.vb        (RunInteractiveAsync)
    → Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs   (common scripting fork)
      → Compilers\VisualBasic\Portable\                (改版 VB 编译器)
```

## 测试概况（稳定结构事实）

- 测试目录：`Scripting\VisualBasicTest\`
- 主要测试类：
  - `CommandLineRunnerTests.vb`
  - `ScriptTests.vb`
  - `InteractiveSessionTests.vb`
  - `InteractiveSessionReferencesTests.vb`
  - `ObjectFormatterTests.vb`
  - `PrintOptionsTests.vb`
  - `ScriptOptionsTests.vb`

## 相关索引

- `../proposals/README.md` —— VBScript.NET 产品提案（已发布版本能力见 `../proposals/vbscript-<版本>/` 归档）
- `../meetings/README.md` —— VBScript.NET 产品会议
- `../compilers-index.md` —— 编译器索引
- `../decisions.md` —— 设计决策记录
