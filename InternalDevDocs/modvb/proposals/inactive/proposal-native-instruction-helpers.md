# 平台原生指令辅助 / Platform Native Instruction Helpers

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

实验性 / 未定稿。Anthony 不想为 CLR 的每一个 opcode 添加"一次性关键字"，也不会走到直接引入 "IL Literals" 那么远；他提出一种类似 P/Invoke 的机制，让某些方法调用被内联为对应的 IL 指令，从而在不直接膨胀语言的前提下，把更多 IL 能力通过 VB 表达出来。

## Motivation
[motivation]: #motivation

- 一直有人要求为 [CLR 中的每一个 opcode](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes?view=net-10.0) 添加专门的关键字，Anthony 对此感到厌倦（原文："Tired of being asked to add a one-off keyword for every opcode in the CLR"）。
- 直接加入 "IL Literals"（在源代码里书写 IL）走得太远，需要一种"把活干了但不膨胀语言"的中间方案。
- 目标场景包括：
  - 针对写得不好的库做**大小写敏感的成员解析**；
  - **Checked / Unchecked 算术**；
  - **Volatile**；
  - **Localloc**。

## Detailed design
[design]: #detailed-design

原文并未提供示例代码，只列出一组目标场景。把原文列举的场景整理为注释形式：

```vb
' 原文列举的场景（无示例代码）：
' - Case-sensitive member resolution against poorly written libraries.
' - Checked/Unchecked arithmetic.
' - Volatile
' - Localloc
```

设计思路的关键一句：VB 已经能识别某些方法调用并把它们发射为内建 IL 指令。要做的事情只是**扩展这张内联列表**，并在运行时定义一个 well-known 模块，其中包含"形状正确"的共享方法，让更多 IL 可以通过 VB 表达（原文："It's just a matter of expanding the list and defining a well-known module in the runtime with the right shared methods in the right shapes to make more of IL expressible through VB"）。

当然，总会有一些指令太主流、值得用更自然的方式暴露。例如 [VB 语言规范](https://github.com/dotnet/vblang/blob/main/spec/expressions.md#cast-expressions) 明确指出 `DirectCast` 和 `TryCast` 就是为了让原生的 `unbox` 与 `isinst` 指令可直接使用而被加入的。也许还可以为 `System.Runtime.Intrinsics` 提供共享的基础设施，方便那些有意深入的用户。

## Drawbacks
[drawbacks]: #drawbacks

- 方法调用被隐式映射成 IL 指令后，"魔术调用"会增加调试与理解的难度。
- 大小写敏感成员解析与 VB 一贯的大小写不敏感语义相冲突，需谨慎界定生效范围。
- 运行时 well-known 模块的边界若定义不清，容易与用户自定义方法冲突。

## Alternatives
[alternatives]: #alternatives

- 直接加入 "IL Literals"，让用户在源码中书写 IL——Anthony 明确表示不会走这么远。
- 为每个 opcode 加一个关键字——正是 Anthony 想避免的"语言膨胀"。
- 完全不做：保留现状，靠 `DirectCast`/`TryCast` 等已内建的少量指令映射。

## Unresolved questions
[unresolved]: #unresolved-questions

- 具体选择哪些方法进入内联列表、运行时 well-known 模块的形状（类名/方法签名）如何定义，原文未说明。
- 大小写敏感解析如何与 VB 全局的大小写不敏感语义共存。
- `System.Runtime.Intrinsics` 的"共享基础设施"具体指什么，原文未展开。
