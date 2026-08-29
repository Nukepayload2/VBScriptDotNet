# VBScript.NET 产品规范说明

本文档说明 **VBScript.NET 产品自身**的 spec 层。spec 层**不维护「当前状态」快照**（现状快照会随版本漂移，一旦疏于维护就会造成误导）——产品能力以**版本归档**为准：各已发布版本的能力归档在 `../proposals/vbscript-<版本>/`（如 1.2 已归档在 `../proposals/vbscript-1.2/`），各版本归档**自包含**，其能力事实即稳定记录。

**与 modvb 的关系**：`../modvb/spec/` 对应 Anthony 提案库的规范（当前为空）；本目录是 VBScript.NET 产品自身的规范说明，两者分离。

**撰写规范**：所有 spec 文档必须遵循本文「[撰写规范](#撰写规范)」节（vblang/csharplang 风格，英文、独立、格式对齐官方提案模板）。writer/编辑 agent 动笔前必须先读。

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
- **byref-like 类型安全（ref struct 支持，已实现）**：见 `spec-byref-like-safety.md`。
- **REPL 裸表达式自动打印**（表达式开头 `?` 可选）：见 `spec-optional-question-prefix.md`。
- **`.vbx` 首行 `#!` shebang 指令**（编译器语法层，已实现）：见 `spec-shebang-directive.md`。
- **消费 C# 扩展成员**（扩展属性/运算符，扩展方法本就可用，已实现）：见 `spec-consume-csharp-extension-members.md`。
- **消费 C# 接口共享成员**（接口共享成员 SAIM，已实现）：见 `spec-consume-interface-shared-members.md`。
- 已移植 C# interactive 的 **`#Load`** 指令。
- **理论上和 C# REPL 不应该有功能差距**。
- 代码内产品版本号已落 `2.0.0-Beta`（2026-08-23 用户裁决：Scripting 库版本 `Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:8-9` 由 `1.2.0/beta` 改为 `2.0.0/Beta`，`vbi --version` 显示 `2.0.0-Beta`）。

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

## 撰写规范

本文规定 `InternalDevDocs\spec\` 下所有 spec 文档的写作标准。**所有 writer / 编辑 agent 在撰写或修改 spec 前必须先读本节。** 违反本规范的 spec 视为不合格，需重写。

### 总原则

spec 必须做到**与真实 vblang / csharplang 官方文档品质对等**——让读者看不出是团队外写的。三条硬性要求，缺一不可：

1. **英文**：正文、标题、章节锚点、代码注释一律英文，不使用中文。
2. **独立性**：只引用公开产物（csharplang 提案、vblang spec 章节、dotnet/runtime 文档、GitHub issue/PR），禁止引用团队内部产物。
3. **格式**：严格遵循 vblang 提案模板结构（见下）。

### 禁止项（独立性红线）

spec 中**不得出现**以下任何内容：

- 内部路径：`tmp\vortex-logs`、`InternalDevDocs`、`tasks\...`、`meetings\...`、`decisions.md`
- 内部产物：任务 ID、vortex 日志、实施/验证记录文件名、"本 fork"、"VBScript.NET 产品"、团队内部会议名（如 `meeting-byref-like-repl-safety.md`）
- 中文正文或中文注释

允许引用（公开产物）：`csharplang\proposals\...`（如 `ref-struct-interfaces.md`、`span-safety.md`）、`vblang\spec\...`（如 `introduction.md`）、`dotnet/runtime` 的 byreflike-generics 设计文档、GitHub issues / PRs（按真实作者风格引用）。

### 文档格式（vblang 提案模板）

```markdown
# <Feature Name>

* [x] Proposed
* [ ] Prototype: [Complete](<prototype-link>)
* [ ] Implementation: [In Progress](<impl-link>)
* [ ] Specification: [Not Started](<spec-link>)

## Summary
[summary]: #summary

## Motivation
[motivation]: #motivation

## Detailed design
[design]: #detailed-design

## Drawbacks
[drawbacks]: #drawbacks

## Alternatives
[alternatives]: #alternatives

## Unresolved questions
[unresolved]: #unresolved-questions
```

依内容可增补 `## Soundness`、`## Considerations`、`## Open Issues`、`## Related Items` 等小节（对齐 csharplang 提案惯例）。每节保留锚点（`[section]: #section`）。

**`Unresolved questions` 节纪律**：已完成特性此节只写 `None.`（干净一行），**不得**在「未解决」节下列出已解决的问答清单——那会造成自相矛盾（"None" 却列内容）。已解决的决策写进正文对应小节（Alternatives / Detailed design / Considerations），不留在未决区。

### 语态与措辞（对齐 vblang/csharplang）

- 正式、简洁、确定性语态：`This proposal will …`、`The language will allow …`、`Note that …`、`A … is defined as …`、`… shall …`。
- 设计决策用 `**Decision**: …` 格式，附理由。
- 小节标题用英文名词短语（如 `ref struct Generic Parameters`、`Representation in metadata`）。
- 引用用 Markdown 链接 + 文末 `[anchor]: <url>` 锚点定义。

### 代码示例

- VB 代码块语言标签用 `vbnet`，块内用 `' Error: ...` / `' Okay` 注释展示预期编译器行为。
- 文法示例用 ```ANTLR 或 BNF 形式。
- C# 对应物只作文字引用（"the C# equivalent is specified in …"），除非必要不贴 C# 代码块。

### 品质基准（风格样板）

| 样板 | 用途 |
|---|---|
| `csharplang\proposals\csharp-13.0\ref-struct-interfaces.md` | **主样板**：ref struct 接口 + `allows ref struct` 反约束（本特性 C# 对应物）——语态、结构、边界与工程考量的标准 |
| `csharplang\proposals\csharp-7.2\span-safety.md` | ref-like 规则权威出处（Introduction 语体、规则叙述） |
| `vblang\proposals\overload-resolution-priority.md` | 真实 vblang 提案实例（vblang 侧写法） |
| `vblang\spec\introduction.md` | 正文规范语体（strongly/loosely typed 等确定性叙述） |

与样板同等深度：同样详尽的边界情况、同样的工程考量（runtime support / API versioning）、同样的形式化论证（Soundness）。

### 检查清单（writer 完成后自检）

- [ ] 全英文（正文、标题、锚点、代码注释）
- [ ] 无团队内部引用（见「禁止项」）
- [ ] 结构对齐 vblang 提案模板（六节 + 可选增补）
- [ ] 语态对齐官方文档（确定性、正式）
- [ ] VB 代码示例用 `vbnet` 块 + `' Error:` 注释
- [ ] 引用指向公开文档，文末锚点规范
- [ ] 品质对标 `ref-struct-interfaces.md`

### xlf 本地化规范（对齐原版 Roslyn VB 编译器）

所有本地化 resx 及其 13 语言 xlf（`Scripting\VisualBasic\VBScriptingResources.resx`、`Scripting\Core\ScriptingResources.resx` 等）必须遵循原版 Roslyn VB 编译器的 xlf 规范与风格（基准：`src\Compilers\VisualBasic\Portable\xlf\VBResources.*.xlf`）：

1. **结构**：XLIFF 1.2（`urn:oasis:names:tc:xliff:document:1.2`），`<file datatype="xml" source-language="en" target-language="<lang>" original="../VBScriptingResources.resx">`，`<trans-unit>` = `<source>` + `<target state="translated">` + `<note />`。
2. **source 逐字节等于 resx**（XliffTasks 不变式）：resx 是唯一真值；改 resx 必须同步全部 13 个 xlf 的 source。
3. **中性资源必须干净英文产品内容**：帮助/描述一律英文、正式、产品相关（2026-08-29 用户裁决）；禁止混入中文行、旧分支 URL、保留/未实现功能的宣传等与当前产品无关的内容。
4. **target 行对行翻译**：开关名与占位符原样，仅译描述；`state="translated"`；开关帮助不额外加行。
5. **维护流**：resx 改动 → XliffTasks（`UpdateXlfOnBuild`）同步 source → 各语言 target 补译/复核；不手工向 xlf 塞内容。

## 相关索引

- `../proposals/README.md` —— VBScript.NET 产品提案（已发布版本能力见 `../proposals/vbscript-<版本>/` 归档）
- `../meetings/README.md` —— VBScript.NET 产品会议
- `../compilers-index.md` —— 编译器索引
- `../decisions.md` —— 设计决策记录
