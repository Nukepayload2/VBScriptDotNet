# ModVB 建议文档撰写规范

本文档规定所有 proposal 文档的写作标准。所有 subagent 在撰写前必须先读本文件。

## 文档格式

严格遵循 vblang 的 `proposal-template.md`，结构固定为：

```markdown
# <功能英文名 / 中文名>

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

<一段话说明该功能。>

## Motivation
[motivation]: #motivation

<为什么需要？支持哪些使用场景？期望的结果是什么？>

## Detailed design
[design]: #detailed-design

<设计细节：语法、语义、边界情况。必须包含从 Anthony 原文摘录的 VB 代码示例，并配中文说明。>

## Drawbacks
[drawbacks]: #drawbacks

<为什么不应该做？权衡与代价。>

## Alternatives
[alternatives]: #alternatives

<考虑过哪些其他方案？不做这个功能的代价？>

## Unresolved questions
[unresolved]: #unresolved-questions

<哪些设计点仍待定？>
```

## 语言

- 正文一律使用 **简体中文（zh-CN）**。
- 代码示例保持 VB 语法原文（来自 Anthony 设计文档），可在旁边加中文注释。
- 文件名、章节锚点、状态复选框保持英文（与 vblang 一致）。

## 内容来源

- 唯一来源：`..\AnthonyDesign_wordpress.txt`
- 撰写时必须依据原文对应章节的代码与说明，不得凭空虚构语法。
- 原文是 HTML 抓取件，`<span class="k">` 等标签是行内高亮，解读时应忽略，只取其纯 VB 文本。
- `Let` 是 Anthony 提出的新声明关键字（代替 `Dim`），在示例中保留原文用法。

## 写作要求

1. 每份文档聚焦**一个**新语法，不越界讲其他建议。
2. Detailed design 必须有至少一段示例代码（引用原文，可精简）。
3. 状态行统一用 `* [x] Proposed`（即仅"已提议"状态），其余三项 `[ ]`。
4. 若原文对该项有明确疑虑或标注（如 "Not sure about this"、"Maybe"、"Not shown"），如实写进 Unresolved questions。
5. inactive 子目录下的建议同样遵循该模板，但可在 Summary 中标注"实验性 / 未定稿"。

## 文件命名与位置

- 文件名已由 `README.md` 索引定死，不得改名。
- 存放位置：`..\proposals\` 或其 `inactive/` 子目录。
