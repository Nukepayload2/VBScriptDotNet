# 任意块作用域与 Rust 式所有权 / Arbitrary Block Scopes & Rust-style Ownership

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。本建议合并原文 18.28、18.29、18.30 三节，共三个想法：

- **任意块作用域**（18.28）：人们一直在要求的通用块作用域，Anthony 未给出任何语法。
- **Rust 式所有权**（18.29）：为 `IDisposable` 等引入 `Take` / `Borrow` / `Give` / `Lend` 的所有权关键字，Anthony 的回应是"能不能？当然。该不该？谁知道？"。
- **AI 相关**（18.30）：仅一句叹息，没有任何内容。

三者均处于"记一笔备忘"的状态，远未定稿。

## Motivation
[motivation]: #motivation

- **任意块作用域**：Anthony 说 "Just remembered that. People keep asking for this."——他只是想起这个想法，人们一直在要求，未展开动机。
- **Rust 式所有权**：让资源所有权可显式表达、可跟踪（针对 `IDisposable` 等），但作者自己也拿不准是否该做。
- **AI**：原文只有 "Ugh. (sigh) yeah, I know…"，无任何可依内容。

## Detailed design
[design]: #detailed-design

### 18.28 任意块作用域

原文全部内容仅一句：

```vb
' 原文全部内容：
' Just remembered that. People keep asking for this.
' （刚想起来。人们一直在要求这个。）
```

没有任何语法或语义设计。VB 现有 `Using` / `SyncLock` / `With` 已提供受控的受限块；所谓"任意块作用域"应是一般性的块结构，让局部变量的作用域可以收窄到任意一组语句内——但这只是旁白推断，**原文未给出任何语法**。

### 18.29 Rust 式所有权

原文的回应是 "Could we? Of course. Should we? Who knows?"（能不能？当然。该不该？谁知道？），并列出四个候选关键字：

```vb
' Rust 式所有权的候选关键字（原文所列，无示例语法）：
Take, Borrow, Give, Lend
```

### 18.30 AI

原文全部内容仅一句：

```vb
' 原文全部内容：
' Ugh. (sigh) yeah, I know…
' （唉。（叹气）是啊，我知道……）
```

没有任何设计。

## Drawbacks
[drawbacks]: #drawbacks

- 三个想法都没有设计，谈不上直接代价；但任意块作用域与 Rust 式所有权一旦进入语言，都会与现有生命周期管理（GC、`Using`）产生深度纠缠。
- 所有权跟踪（`Take`/`Borrow`/`Give`/`Lend`）在托管语言里往往得不偿失，这正是作者"该不该？谁知道？"顾虑的来源。

## Alternatives
[alternatives]: #alternatives

- 任意块作用域：继续用 `Using`/`SyncLock`/`With` 的受限块，不引入通用块。
- 所有权：依赖 GC 与 `Using` 语句管理 `IDisposable`（现状）。
- AI：原文无内容，无法给出替代方案。

## Unresolved questions
[unresolved]: #unresolved-questions

- **任意块作用域**：具体语法形态（如假想的 `Scope ... End Scope` 之类）完全未定；与 `Using` 等现有块的关系；Anthony 原文明言只是"人们一直在要求"。
- **Rust 式所有权**：`Take` / `Borrow` / `Give` / `Lend` 各自的精确语义（谁持有、何时归还、借用期检查）；与 GC、`IDisposable`、`Using` 如何共存；作者的问题"该不该？谁知道？"仍未回答。
- **AI**：原文仅一句叹息，无任何内容可提炼。
