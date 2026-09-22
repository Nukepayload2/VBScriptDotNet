# Shebang 指令

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-shebang-directive.md)

## 概述
[summary]: #summary

本规范为 Visual Basic 脚本方言定义一条 shebang 指令（shebang directive）：位于脚本文件最开头的 `#!`，其后是操作系统用来启动该脚本的解释器路径。编译器把 `#!` 行识别为一条指令 trivia，并将整行作为 trivia 消费，从而使一个以 shebang 开头的脚本文件——Linux 与 macOS 上可执行脚本的标准机制——能够干净地编译、报告正确的行号，并被语法树的每一个消费者理解。在脚本文件中，shebang 行被语言忽略：它不是一条可执行指示（instruction），不绑定到任何符号，也没有任何语义内容。

shebang 指令仅限于脚本方言。在普通编译中，前置的 `#!` 是错误。该指令限于第一行，更确切地说，限于文件的第一个字符。任何位于他处的 shebang 都是错误，但仍被识别并消费，以使树保持良构。两种违规都作为错误报告，与参考 C# 实现一致。

其 C# 对应物在面向 C# 14 的 [ignored-directives] 提案中规定，其中 `#!` 与 `#:` 是**被忽略的预处理指令**（ignored preprocessing directive）：语言忽略它们，而编译器与工具链仍能识别它们。

## 动机
[motivation]: #motivation

作为一个可执行文件分发的脚本必须告诉操作系统由哪个解释器运行它。在 Linux 与 macOS 上，标准机制是 shebang 行：内核读取文件的第一行，把 `#!` 之后的一切作为解释器路径取用，并把脚本文件作为参数来启动解释器。当一个 Visual Basic 脚本解释器安装在某个已知路径上时，一个脚本文件以下列形式开头

```vbnet
#!/opt/vbi/vbi
Dim x = 1
```

要使这样一个文件可执行，消费它的编译器必须接受第一行。在本特性之前，第一行会产生一个解析错误：`#` 开始一条条件编译指令，紧随的 `!` 匹配不到任何指令分支，该行的其余部分被词法分析为杂散记号。

shebang 行被每一个读取该文件的程序消费，而不只是被解释器。一个打开可执行脚本的编辑器、语言服务器或语法高亮器也必须识别第一行；否则，一个在命令行上正确运行的文件被打开时会满是错误。把 shebang 做成语法树的一条指令 trivia，给了每个消费者一棵单一的、共享的树，其中第一行是一条无害的指令。这正是 C# 编译器把 shebang 当作被忽略的预处理指令、而非在宿主中将其剥离的同一理由。

## 详细设计
[design]: #detailed-design

### 识别

当一条条件编译指令以 `#` 开头、且其后跟随的记号是一个 `!` 时，一条 shebang 指令被识别。该指令由一个 `ShebangDirectiveTriviaSyntax` 节点表示——一个派生自 `DirectiveTriviaSyntax` 的结构化 trivia 节点，带有 `HashToken` 与一个 `ExclamationToken` 子节点。其语法 kind 为 `ShebangDirectiveTrivia`。

这一记号序列不需要任何新的词法器工作：`#` 后跟 `!` 无法作为日期文本被扫描，于是回退到一个哈希记号，而 `!` 作为既有的感叹号记号被词法分析。`#` 后跟任何其他记号保持其既有行为：一条格式错误的条件编译指令。

### 首行位置

`#` 必须是文件的第一个字符：位置 0，没有前导 trivia，其前面甚至不能有字节顺序标记（byte-order mark）。当字节顺序标记已被文件读取层解码时，`#` 位于位置 0，该指令有效；当该标记存留进源文本时，`#` 不是第一个字符，该指令是错误。前导空格、前导空行，或第二行上的 shebang，都是错误。

一个位置错误但仍被识别为指令的 `#!`——位于第一行之外的某行、前面有空白、或写作 `# !` 使两记号之间有 trivia——报告 `ERR_ShebangDirectiveNotOnFirstLine`（一个错误），并仍作为一条 shebang trivia 被消费，从而树保持完整、文件其余部分正常解析。这与参考 C# 实现一致，后者报告位置错误并仍解析该指令。

存留进源文本的字节顺序标记是本规则的一个例外：前导 BOM 字符（U+FEFF）不被当作空白，因此 `#` 根本不被识别为指令。不产生任何 shebang 节点，且该行以一般化的解析错误（BC30037/BC30201）而非 `ERR_ShebangDirectiveNotOnFirstLine` 解析失败。

### 模式门控

shebang 指令仅在脚本方言（`SourceCodeKind.Script`）中被允许。普通编译中的 `#!` 是错误。脚本方言既服务于脚本文件也服务于交互提交，因此两者都接受前置的 shebang；一个以 `#!` 开头的交互提交被接受并忽略，与 C# 交互行为一致。

### 指令行被忽略

从 `#!` 到行末的一切——解释器路径及任何尾随空格——都被作为 trivia 消费，不参与语义。该路径是原始文本，不是字符串文本，因此不受字符串文本规则约束，也从不被解析到某个文件。该行作为感叹号记号的尾随 trivia 保留在树中。由于该指令是 trivia 而非语句，后续诊断的行号不会漂移：shebang 行占据第 1 行，其后每一行都保留自己的行号。

### 内容

该节点通过一个 `Content` 属性暴露解释器路径，该属性以字符串文本记号的形式返回路径文本，并配有一个 `WithContent` 方法，产生一条路径不同的新指令。该属性为读取路径的工具链而提供——例如，一个想要显示或校验运行该文件所用解释器的编辑器。

### 错误

| 诊断码 | 诊断 | 条件 |
|---|---|---|
| BC37003 | `ERR_ShebangDirectiveOnlyAllowedInScripts` | 普通（非脚本）编译中的 `#!`。消息："'#!' directives can be only used in scripts" |
| BC37004 | `ERR_ShebangDirectiveNotOnFirstLine` | `#` 不是文件的第一个字符，或 `#` 携带尾随 trivia（如 `# !`）。消息："'#!' must be the first characters on the first line of the file" |

两者都作为错误报告，与参考 C# 实现一致。这两个错误可以同时出现：位于第一行之外某行的普通编译中的 `#!`，既报告模式错误也报告位置错误。

## 缺点
[drawbacks]: #drawbacks

- 语法树获得一个新的 public 节点类型，且语法 kind 枚举获得一个值，扩大了语法 API 表面积。
- 交互窗口中的 `#!` 被接受并被无声地忽略。一个期望该行起某种作用的用户得不到任何反馈。这与 C# 交互一致，但它是毫无效果的行为。
- 位置规则对字节顺序标记要求严格。一个在 `#` 之前有未解码字节顺序标记的文件会报告错误，即便 shell 仍会运行该文件；C# 14 也拒绝 `#` 之前的 BOM，只是在 VB 中该标记不被当作空白，因此 `#` 甚至不被识别为指令，且该失败是一般化的解析错误（BC30037/BC30201）。

## 替代方案
[alternatives]: #alternatives

- **在宿主中剥离该行。** 解释器在编译前移除第一行。被否决：它把每一个诊断的行号都偏移一行——在一个脚本会报告错误的语言中这是严重代价——而且它只修好了解释器。打开同一文件的编辑器和语言服务器仍会报告错误，因此一个能在命令行运行的文件被打开时仍然满是错误。
- **在扫描器中把整条 `#!` 行词法分析为单个类注释 trivia 记号。** 可行，但它放弃了指令节点的形态：指令发现无法找到 shebang，且树偏离了本特性所镜像的 C# 形态。
- **复用一条既有的指令。** `#R` 要求一个字符串文本，而 `#ExternalSource` 是一条行映射指令；两者都不匹配一个原始文本的首行解释器路径。
- **什么都不做。** 可执行的 Visual Basic 脚本在 Linux 或 macOS 上无法存在，因为每一个这样一个脚本的第一行都是一个必然的编译错误。

## 未解决的问题
[unresolved]: #unresolved-questions

无。

## 考量
[considerations]: #considerations

### 与 C# 实现对齐

C# 14 把 `#!` 与 `#:` 当作被忽略的预处理指令（[ignored-directives]）；当 `#` 后跟 `!` 时 shebang 被识别，当它不是文件第一个字符或出现在基于项目的程序中时被报告为错误，并作为一条预处理消息被消费到行末。C# 语言纪要曾考虑把位置违规报告为警告——该代码无害，只有 shell 会无法识别它——但参考实现把它报告为错误，本规范遵从该实现。普通编译同理：C# 曾考虑把 shebang 豁免于基于项目的程序错误，但并未如此；本规范把模式违规报告为错误。

### 交互方言

脚本方言既服务于脚本文件也服务于交互提交。解析器无法判断一次提交来自文件还是来自交互窗口，因此 shebang 在两者中都被接受。在交互窗口中，前置的 `#!` 是一个无害的空操作，与 C# 交互一致。这正是 C# 语言纪要为其脚本方言所接受的、交互方言与普通代码之间的同一差距（[LDM-2020-04-15][ldm-2020-04-15]）。

### 被禁用的条件编译区域

位于某个被假 `#If` 排除的区域内部的 `#!` 根本不被识别为指令：被禁用区域的文本不会被解析以查找指令，因此不产生 shebang 节点，也不报告诊断。位置与模式检查仅适用于实际被识别的那些指令——即处于活动区域中的 shebang。这一边界在 C# 一侧没有对应的测试；就实现而言，VB 的行为是不识别被禁用区域内部的 shebang。

### 编辑器渲染

由于 shebang 行是被语言忽略的内容，编辑器将其归类为注释。这与 C# 参考实现一致，后者把一条 shebang 指令与 `//` 和 `/* */` 注释一并归类。整行，包括解释器路径，都被渲染为注释。

## 测试
[testing]: #testing

本特性由无副作用的内存内测试来检验：它们不进行网络访问、文件写入、进程启动或注册表访问。

- **解析器测试。** 脚本文件中前置的 shebang 被解析为第一个真实记号上的一条 shebang 指令 trivia；该指令可通过指令 API 被发现；第二行上的 shebang 仍被解析为一条 shebang，且错误附在 `#` 上；注释内部的 `#!` 仍是注释；`#` 后跟一个非 `!` 记号保持其格式错误指令的行为；shebang 之后各诊断的行号不会漂移。
- **语义测试。** 脚本文件中前置的 shebang 不报告诊断；位于第一行之外某行的 shebang、带前导空白的 shebang，以及两记号之间有 trivia 的 `# !`，报告位置错误；路径内容——空格、斜杠、`#`、`-`、一个 `env -S` 参数列表——被接受而不报错；普通编译报告模式错误；非首行的普通编译报告两个错误；且两个错误都是错误，而非警告。
- **边界测试。** 存留进源文本的字节顺序标记会阻止对 shebang 的识别；被禁用区域中的 `#!` 不产生任何指令；活动区域中一个位置错误的 shebang 仍报告位置错误；`Content` 属性返回解释器路径；`WithContent` 用一个新路径重建该指令。
- **端到端脚本测试。** 一个第一行为 shebang、其后为普通代码的 `.vbx` 文件以零诊断编译，且代码正常运作；shebang 与其后的 `#R` 与 `#Load` 指令共存；一个以 shebang 开头的脚本中的尾随表达式保持既有的退出码语义；一个以 `#!` 开头的交互提交被接受且不产生输出。

## 相关条目
[related]: #related-items

- [ignored-directives](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md) —— 将 `#!` 与 `#:` 作为被忽略的预处理指令引入的 C# 14 提案
- [LDM-2020-07-20](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-07-20.md) —— 对 shebang 场景（issue #3507）的分诊
- [LDM-2020-09-28](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-09-28.md) —— 重新分诊至未来版本，取决于 `dotnet run` 工具链
- [LDM-2020-04-15](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md) —— C# 与其脚本方言之间所被接受的差距

[ignored-directives]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md
[ldm-2020-07-20]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-07-20.md
[ldm-2020-09-28]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-09-28.md
[ldm-2020-04-15]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md
