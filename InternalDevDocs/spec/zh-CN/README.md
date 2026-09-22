# VBScript.NET 产品规范说明 —— 简体中文译文

本目录收录 `../` 下英文 spec 正本的**简体中文参考译文**，非规范文本，只作阅读辅助。译文的地位、节名对照、保留英文清单、术语基线与维护规则统一定义于 [`../README.md` 的「zh-CN 译文」节](../README.md#zh-cn-译文)。

版本历史、架构链、测试概况与撰写规范见 [`../README.md`](../README.md)。

## 译文索引

| 译文 | 英文正本 | 主题 |
|---|---|---|
| `spec-scripting-dialect.md` | [`spec-scripting-dialect.md`](../spec-scripting-dialect.md) | 脚本方言的声明与提交模型：顶层声明合成 script class、提交链与跨提交可见性、入口点合成、顶层 `Await` 与 `AddHandler`、`Imports` 跨提交累积、脚本专属诊断族 |
| `spec-byref-like-safety.md` | [`spec-byref-like-safety.md`](../spec-byref-like-safety.md) | Byref 类似类型安全（byref-like）：ref struct 的返回、赋值、字段与泛型约束规则 |
| `spec-reference-directive.md` | [`spec-reference-directive.md`](../spec-reference-directive.md) | `#R` 引用指令：脚本源码层唯一的程序集引用机制 |
| `spec-load-directive.md` | [`spec-load-directive.md`](../spec-load-directive.md) | `#Load` 指令：编译器指令 trivia 与宿主多树展开契约、加载树先于主树、退出码与失败通道 |
| `spec-shebang-directive.md` | [`spec-shebang-directive.md`](../spec-shebang-directive.md) | Shebang 指令：`.vbx` 首行 `#!` 作为 directive trivia |
| `spec-optional-question-prefix.md` | [`spec-optional-question-prefix.md`](../spec-optional-question-prefix.md) | REPL 裸表达式自动打印与可选 `?` 前缀 |
| `spec-consume-csharp-extension-members.md` | [`spec-consume-csharp-extension-members.md`](../spec-consume-csharp-extension-members.md) | 消费 C# 扩展成员：扩展属性与扩展运算符 |
| `spec-consume-interface-shared-members.md` | [`spec-consume-interface-shared-members.md`](../spec-consume-interface-shared-members.md) | 消费 C# 接口共享成员（SAIM） |
| `spec-consume-ref-readonly.md` | [`spec-consume-ref-readonly.md`](../spec-consume-ref-readonly.md) | 消费 C# `ref readonly` 返回 |
| `spec-script-optimization-level.md` | [`spec-script-optimization-level.md`](../spec-script-optimization-level.md) | 脚本编译的优化级别 |
| `spec-vbi-script-diag-mode.md` | [`spec-vbi-script-diag-mode.md`](../spec-vbi-script-diag-mode.md) | 仅诊断的脚本检查模式（`/check`） |

## 阅读顺序建议

脚本语言层先看 `spec-scripting-dialect.md`；指令族看 `spec-reference-directive.md`、`spec-load-directive.md`、`spec-shebang-directive.md`；与 C# 类型互操作看 `spec-byref-like-safety.md`、`spec-consume-ref-readonly.md`、`spec-consume-interface-shared-members.md`、`spec-consume-csharp-extension-members.md`；宿主与编译开关看 `spec-script-optimization-level.md`、`spec-vbi-script-diag-mode.md`。

## 相关索引

- [`../README.md`](../README.md) —— 英文正本目录与撰写规范（规范文本）
- [`../../proposals/README.md`](../../proposals/README.md) —— VBScript.NET 产品提案（已发布版本能力见 `../../proposals/vbscript-<版本>/` 归档，如 1.2 已归档在 `../../proposals/vbx-1.2-beta/`）
- [`../../meetings/README.md`](../../meetings/README.md) —— VBScript.NET 产品会议
- [`../../compilers-index.md`](../../compilers-index.md) —— 编译器索引
- [`../../decisions.md`](../../decisions.md) —— 设计决策记录
