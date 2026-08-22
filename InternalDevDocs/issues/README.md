# VBScript.NET 问题登记（issues 层）

本文档登记 **VBScript.NET 产品自身**发现的 bug / 缺陷（issues 层）。与 `proposals/`（能力增强）、`meetings/`（会议评估）、`spec/`（规范）分离：issue 是**既存行为与预期不符**的记录，给出症状、根因、预期行为与修复方向。

## 目录组织

- `issues/` ↔ 根目录登记 Open / In Progress 的 bug。
- 修复验证通过后：在 issue 内标记 **Fixed** 并注明修复 commit；若涉及能力变更，转 `proposals/` 评估。

## 问题清单

| # | 文件 | 问题 | 状态 |
|---|------|------|------|
| 01 | `issue-vbx-load-span-shift.md` | vbx `#Load` 文本内联导致后续 TextSpan 漂移（预期：与 C# `#load` 一样零漂移，独立树合并） | **Open** |
| 02 | `issue-scripting-xml-linq-reference.md` | Scripting 63 失败：net10.0 把 Xml.Linq 移到 `System.Private.Xml.Linq`，`IncludeInternalXmlHelper` 嵌入 helper 树但绑定缺直接引用 → 每个脚本提交 24 个 BC30002 | **Fixed**（2026-08-20） |

> 状态约定：**Open**（待修复）/ **In Progress**（已认领）/ **Fixed**（已验证修复，注明 commit）。
