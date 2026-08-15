This folder contains AI knowledge base files and decisions made by humans.

AI should load `*-index.md` before finding files.

## 目录结构

- `modvb\`：Anthony 的 ModVB 提案库（**参考来源**，AI 输入材料）。
- `csharplang\`：C# 语言设计文档镜像（参考来源）。
- `vblang\`：VB 语言设计文档镜像（参考来源）。
- `proposals\`：**VBScript.NET 产品自身**的提案（建议层，产品语法/能力增强）。
- `meetings\`：**VBScript.NET 产品自身**的会议纪要（会议层，评估对应 proposal，产出 LDM 状态 + 三态判定）。
- `spec\`：**VBScript.NET 产品自身**的规范说明（spec 层，登记版本历史与结构事实，不维护动态现状快照；各版本能力见 `proposals/vbscript-<版本>/` 归档）。
- `issues\`：**VBScript.NET 产品自身**的问题登记（bug 层，复现步骤 / 版本 / 预期 / 实际 / 根因 / 修复方向）。
- `tasks\`：VBScript.NET 语法优先级任务清单。
- `*-index.md`（`modvb-index.md` / `csharplang-index.md` / `vblang-index.md` / `compilers-index.md`）：AI 查找文件前应先读的索引。
- `decisions.md`：VBScript.NET 设计决策记录（M1–M8 映射 + D1–D4 决策）。

**产品层与参考来源分离**：`proposals/`、`meetings/`、`spec/` 描述 **VBScript.NET 产品自身**；`modvb/`（Anthony 提案库）只是参考来源，两者不可等同。AI 评估产品提案时，先读 `spec/README.md`（版本历史与规范说明）与 `proposals/README.md` / `meetings/README.md`，再以 `modvb/` 为借鉴。