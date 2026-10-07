# VBScript.NET 产品规范说明

本文档说明 **VBScript.NET 产品自身**的 spec 层。spec 层**不维护「当前状态」快照**（现状快照会随版本漂移，一旦疏于维护就会造成误导）——产品能力以**版本归档**为准：各已发布版本的能力归档在 `../proposals/vbscript-<版本>/`（如 1.2 已归档在 `../proposals/vbx-1.2-beta/`），各版本归档**自包含**，其能力事实即稳定记录。

**与 modvb 的关系**：`../modvb/spec/` 对应 Anthony 提案库的规范（当前为空）；本目录是 VBScript.NET 产品自身的规范说明，两者分离。

**撰写规范**：所有 spec 文档必须遵循本文「[撰写规范](#撰写规范)」节（vblang/csharplang 风格，英文、独立、格式对齐官方提案模板）。writer/编辑 agent 动笔前必须先读。简体中文以参考译文形式存放于 `zh-CN\`，地位与维护规则见「[zh-CN 译文](#zh-cn-译文)」节。

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
- **消费 C# `ref readonly` 返回**（已实现）：见 `spec-consume-ref-readonly.md`。
- **脚本方言的声明与提交模型**（顶层声明 → 合成 script class、提交链与跨提交可见性、入口点合成、顶层 `Await`/`AddHandler`、`Imports` 跨提交累积、脚本专属诊断族）：见 `spec-scripting-dialect.md`。
- **`#R` 引用指令**（脚本源码层唯一的程序集引用机制，已实现）：见 `spec-reference-directive.md`。
- 已移植 C# interactive 的 **`#Load`** 指令（编译器指令 trivia + 宿主多树展开契约、加载树先于主树、`Return` 为整个提交的退出码、失败通道与 C# 对照，已实现）：见 `spec-load-directive.md`。
- **理论上和 C# REPL 不应该有功能差距**。
- 代码内产品版本号已落 `2.0.0-Beta`（用户裁决：Scripting 库版本 `Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj:8-9` 由 `1.2.0/beta` 改为 `2.0.0/Beta`，`vbi --version` 显示 `2.0.0-Beta`）。

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

1. **英文正本**：正文、标题、章节锚点、代码注释一律英文，不使用中文。简体中文以参考译文形式存放于 `zh-CN\`，地位与规则见「zh-CN 译文」节——本条只约束英文正本，不约束该目录。
2. **独立性**：只引用公开产物（csharplang 提案、vblang spec 章节、dotnet/runtime 文档、GitHub issue/PR），禁止引用团队内部产物。
3. **格式**：严格遵循 vblang 提案模板结构（见下）。

### 禁止项（独立性红线）

spec 中**不得出现**以下任何内容：

- 内部路径：`tmp\vortex-logs`、`InternalDevDocs`、`tasks\...`、`meetings\...`、`decisions.md`
- 内部产物：任务 ID、vortex 日志、实施/验证记录文件名、"本 fork"、"VBScript.NET 产品"、团队内部会议名（如 `meeting-byref-like-repl-safety.md`）
- 中文正文或中文注释（约束英文正本；`zh-CN\` 译文按「zh-CN 译文」节处理，同样不得引入英文正本没有的内部引用）

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

- [ ] 全英文（正文、标题、锚点、代码注释；只约束英文正本，`zh-CN\` 译文见「zh-CN 译文」节）
- [ ] 无团队内部引用（见「禁止项」）
- [ ] 结构对齐 vblang 提案模板（六节 + 可选增补）
- [ ] 语态对齐官方文档（确定性、正式）
- [ ] VB 代码示例用 `vbnet` 块 + `' Error:` 注释
- [ ] 引用指向公开文档，文末锚点规范
- [ ] 品质对标 `ref-struct-interfaces.md`
- [ ] 已按「zh-CN 译文」节同步简体中文译文

### zh-CN 译文

`zh-CN\` 存放英文正本的简体中文参考译文，供中文读者阅读；**英文正本是规范文本（normative）**，措辞、事实或结构冲突一律以英文正本为准。新增与修改 spec 先改英文正本，再按本节回写译文。一篇正本对应一篇同名译文。

**目录与命名**：`zh-CN\spec-<feature>.md` 沿用正本文件名；`zh-CN\README.md` 是本目录的索引副本。每篇译文首部状态清单里的 `Specification:` 自链指向 `zh-CN\` 内的同名译文。

**骨架对齐**：标题层级、章节顺序、表格行列、列表项、引用块与围栏代码块的数量必须与正本一一对应。逐段全译，不得省略、压缩、合并段落，也不得增补正本没有的内容。节名译为中文（`## Summary` → `## 概述`），文档主标题 `# <Feature Name>` 同按此原则译为中文，其中的语言关键字与类型名按「保留英文原文」清单不译；模板锚点定义行 `[section]: #section` 与 `* [x] Proposed` 等状态清单一律照抄正本不译：

| 正本节名 | 译文节名 |
|---|---|
| `Summary` / `Motivation` / `Detailed design` | 概述 / 动机 / 详细设计 |
| `Drawbacks` / `Alternatives` / `Unresolved questions` | 缺点 / 替代方案 / 未解决的问题 |
| `Soundness` / `Considerations` / `Open Issues` | 健全性 / 考量 / 未决事项 |
| `Testing` / `Related Items` | 测试 / 相关条目 |

三级以下小节标题按同一原则译为中文名词短语，正文首次出现的关键概念在括号内附英文原词。

**保留英文原文**（不译、不改大小写与拼写）：

| 类别 | 例 |
|---|---|
| 语言与 API 标识符 | `SourceCodeKind.Script`、`ByRef`、`ParamArray`、`CreateInitialScript<int>` |
| 语法节点与 kind 名 | `ShebangDirectiveTriviaSyntax`、`DirectiveTriviaSyntax`、`ExclamationToken` |
| 诊断码与其 Message 原文 | `BC37003`、`ERR_ShebangDirectiveOnlyAllowedInScripts`、`"'#!' directives can be only used in scripts"` |
| 围栏代码块 | `vbnet` 块内的代码与 `' Error:` / `' Okay` 注释、ANTLR/BNF 文法示例 |
| 引用与链接 | GitHub URL、Markdown 链接锚点名（如 `[ignored-directives]`） |

**逐字引文**：正文引号内标出的第三方原句——vblang / csharplang 规范句、LDM 纪要结论、诊断消息文本、外部提案的原文摘录——一律逐字照抄正本英文，不译成中文。译文中出现引号即断言引号内就是那位作者写下的话，把中文塞进引号等于伪造引文。引号之外的中文散文正常翻译；需要帮读者理解时，在引文之后另写中文转述，不替换引文本身。

判定只看一件事：**引号内的话出自谁之手**。出自他人（规范、提案、会议纪要、诊断文本、代码或文档摘录，或由链接指向的外部来源）→ 逐字保留英文，并沿用正本的 ASCII 双引号，便于与正本逐字比对。出自本文作者（自铸术语、内部标签、反讽式强调，如 `position 0`、`no disk, no execution` 这类本篇自己造的说法）→ 照常译为中文，并使用直角引号「」；不得为凑"逐字"把作者自己的强调还原成英文。

**引号样式约定**：ASCII 双引号只用于英文逐字引文与正本照抄的标识性文本，「」只用于中文散文里的强调与自铸术语；一篇译文里不混用两套引号标记同一类内容。

**站内心锚**：正文中形如 `](#divergences-from-the-c-implementation)` 的站内心锚一律保持正本的英文 slug，不改成中文。节名译为中文会使 GitHub 生成的锚变成中文 slug、令这些链接失效，因此每个被站内心锚指向的标题在行末内嵌同名锚保住指向：`### 与 C# 实现的分叉 <a id="divergences-from-the-c-implementation"></a>`。校验口径：译文内每一个 `](#x)` 必须命中同文件的某个标题 slug 或某个显式 `id`，命中不到即断头引用。

**术语基线**：沿用仓库既有中文文档（`../decisions.md`、`../meetings/`、`../proposals/`）的行话，`zh-CN\` 目录内必须一致：

| 英文 | 中文 | 说明 |
|---|---|---|
| dialect / submission / diagnostic | 方言 / 提交 / 诊断 | 沿用既有译法 |
| top-level / interactive | 顶层 / 交互 | 沿用既有译法 |
| emit（编译器产出代码、IL、元数据） | 发射 | 既有文档主流行话；`发出` 只留给诊断与警告语境（"发出警告"），不用于 emit |
| lowering / lowered | 降级 | 既有文档主流行话；`降低` 只用于开销、复杂度等本义 |
| `**Decision**` 模板标记 | `**决策**` | 对齐本文「设计决策用 `**Decision**: …`」条 |
| 诊断表中的 `Message:` 标签 | `消息：` | 标签是译文散文；其后的英文消息原文逐字保留 |
| trivia | 保持原文不译 | 既有中文文档一律不译 |

其余关键概念首次出现用「中文（English）」形式附英文原词，同一篇内必须一致；仅单篇使用的术语（如 copy-out、value-shaped）由该篇自定并保持篇内一致。

**文体**：与正本同为规则规范文体——现在时、规范性陈述、无叙述主体。译文不得出现过程叙述、译注（如「原文此处含义不清」「意译」「原文为双关」）或对译文自身的说明；此类内容属于台账，不属于正文。独立性红线对译文同样生效：不得引入正本没有的内部路径或内部产物名。

**译者自检**（交付前逐项核对）：

- [ ] 文件内 `##` 节数、围栏代码块数、表格行数、锚点定义数与正本一致。
- [ ] 状态清单保留，`Specification:` 自链指向 `zh-CN\` 内同名文件。
- [ ] 诊断码、Message 原文、标识符、URL 逐字未改。
- [ ] 无省略、无合并、无增补，无译注与过程叙述。
- [ ] 术语与节名对照表、`zh-CN\` 目录内其它译文一致。

### xlf 本地化规范（对齐原版 Roslyn VB 编译器）

所有本地化 resx 及其 13 语言 xlf（`Scripting\VisualBasic\VBScriptingResources.resx`、`Scripting\Core\ScriptingResources.resx` 等）必须遵循原版 Roslyn VB 编译器的 xlf 规范与风格（基准：`src\Compilers\VisualBasic\Portable\xlf\VBResources.*.xlf`）：

1. **结构**：XLIFF 1.2（`urn:oasis:names:tc:xliff:document:1.2`），`<file datatype="xml" source-language="en" target-language="<lang>" original="../VBScriptingResources.resx">`，`<trans-unit>` = `<source>` + `<target state="translated">` + `<note />`。
2. **source 逐字节等于 resx**（XliffTasks 不变式）：resx 是唯一真值；改 resx 必须同步全部 13 个 xlf 的 source。
3. **中性资源必须干净英文产品内容**：帮助/描述一律英文、正式、产品相关（用户裁决）；禁止混入中文行、旧分支 URL、保留/未实现功能的宣传等与当前产品无关的内容。
4. **target 行对行翻译**：开关名与占位符原样，仅译描述；`state="translated"`；开关帮助不额外加行。
5. **维护流**：resx 改动 → XliffTasks（`UpdateXlfOnBuild`）同步 source → 各语言 target 补译/复核；不手工向 xlf 塞内容。

## 相关索引

- `../proposals/README.md` —— VBScript.NET 产品提案（已发布版本能力见 `../proposals/vbscript-<版本>/` 归档）
- `../meetings/README.md` —— VBScript.NET 产品会议
- `../compilers-index.md` —— 编译器索引
- `../decisions.md` —— 设计决策记录
- `zh-CN/README.md` —— 本目录各 spec 的简体中文参考译文索引（非规范文本，冲突以英文正本为准）
