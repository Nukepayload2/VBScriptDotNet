# VBScript.NET 产品会议索引

本文档登记 **VBScript.NET 产品自身**的设计会议（meetings 层）。会议纪要评估对应的产品提案（`../proposals/`），产出「**LDM 状态** + **三态判定**」，是提案状态分类（active/inactive/rejected/done）的判定依据。

**与 modvb 的关系**：`../modvb/meetings/` 是 Anthony 提案库的 LDM 会议纪要（AI 生成的参考材料）；本目录是 VBScript.NET 产品自身的设计会议，与产品提案 1:1 同步组织。

## 目录组织

- `meetings/` ↔ `proposals/`
- `meetings/inactive/` ↔ `proposals/inactive/`
- `meetings/rejected/` ↔ `proposals/rejected/`
- `meetings/vbscript-<版本>/` ↔ `proposals/vbscript-<版本>/`（done 归档，当前无成员）

## 会议纪要

| 文件名 | 对应提案 | 主题 | 三态判定 |
|--------|---------|------|---------|
| `meeting-vb-repl-parity-with-csharp-repl.md` | 现状盘点 + 评估 `proposal-optional-question-prefix.md` 与 `proposal-avalonia-ise-repl-ui.md` | 让 VB REPL 的功能追上 C# REPL 的讨论（2.0 beta 现状盘点、借鉴 csharplang、评估两个新提案） | optional-question-prefix = **Active**；avalonia-ise-repl-ui = **Consider** |
| `meeting-optional-question-prefix.md` | 评估 `proposal-optional-question-prefix.md`（REPL `?` 可选） | 深度评审：REPL 表达式开头问号可选——源码机制核实（PrintStatement / BC30545 / HasSubmissionResult）、候选方案（A/B/C/D）、实现落点（编译器层，跟随 C# REPL 设定）、边界与诊断族枚举 | optional-question-prefix = **Active** |

## 会议纪要格式

- 标题：`# Visual Basic Language Design Meeting` + 日期行。
- 结构：开场白 → `## Agenda` → 各 Proposal 讨论（场景与缺口 / 候选方案 / 权衡 Q&A / RESOLUTION / 状态）→ 附录。
- 状态：每个讨论项给 **LDM 状态** + **三态判定**（仿 modvb meeting 写法）。
