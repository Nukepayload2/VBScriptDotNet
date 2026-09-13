# 脚本顶层 `GoTo` + `Await` → 编译器 `NullReferenceException`

* 状态：**Open**
* 发现日期：2026-09-13
* 发现场景：调查「`.vbx` 顶层变量做成 locals」的可行性时顺带撞见

## 症状

`.vbx` 脚本顶层用 `GoTo` 跳过一段，之后接 `Await`：

```vbx
Imports System.Threading.Tasks
Console.WriteLine("A")
GoTo skip
Console.WriteLine("B")
skip:
Await Task.Delay(1)
Console.WriteLine("C")
```

实测（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`，自报 `2.0.0-Beta+5816a5c`）：

```
System.NullReferenceException: Object reference not set to an instance of an object.
  + Microsoft.CodeAnalysis.CodeGen.ILBuilder.BasicBlock.ShortenBranches(...) — CodeGen\BasicBlock.cs : 325
  + ... ILBuilder.ComputeOffsetsAndAdjustBranches : 854 / RealizeBlocks : 887 / Realize : 207
  + Microsoft.CodeAnalysis.VisualBasic.CodeGen.CodeGenerator.GenerateImpl() — VisualBasic\Portable\CodeGen\CodeGenerator.vb : 172
  ...
EXITCODE=3
```

**零编译诊断**，直接崩。

## 判别性对照（**已运行**）

同一形状放进**普通异步方法**：

```vbx
Class C
    Async Function F2() As Task
        Console.WriteLine("A")
        GoTo skip
        Console.WriteLine("B")
skip:
        Await Task.Delay(1)
        Console.WriteLine("C")
    End Function
End Class
```

实测：**正常编译通过，无错误、无崩溃**（同一次运行里唯一的两条错误来自同一个类的另一个方法，与本形状无关）。

⇒ **`GoTo` 跨 `Await` 在普通异步方法是合法且可编译的**；**在脚本顶层崩**。**脚本模式特有**。

## 根因（**已定位；本文初版归因有误，此处更正**）

> **更正**：本文最初把根因写成「与 `issue-script-top-level-await-in-try-crash.md` 同源（异步宿主 + 发射前置检查不一致）」。**该归因被实测推翻**——见下。

**真正的触发条件不是 `Await`，而是「顶层标签」**：`SourceMemberContainerTypeSymbol.vb:2613-2615` **显式丢弃**顶层的 `LabelStatement`（该处带上游 `TODO (tomat)` 标记）。标签没了，`GoTo` 的目标在发射期悬空，于共享的 `CodeGen.ILBuilder` 里撞空引用。

**判别性实测**（计划阶段跑）：
- 顶层 `GoTo` **不带 `Await`**（前向跳转，以及反向跳转）→ **同样崩在同一行**；
- 普通 `Async Function` 里的同形状 → **合法且跳转生效**（`exit 0`，输出按跳转走）。

⇒ 与 #10 是**两条独立的根因**；#10 走「初始化器路径 + 诊断不 gate 发射」，本条走「顶层标签被丢弃」。

**仍待查**：`LabelStatement` 被丢弃的确切用意（是否与「顶层标签不受支持」这条设计决定绑定），以及恢复它之后 `GoTo` 在脚本顶层的完整语义边界。

## 相关

- 姊妹问题（**根因不同**，勿混）：`issue-script-top-level-await-in-try-crash.md`。
- 本条的修复方向见 `../tasks/script-top-level-crashes/`：**判「修好」**——但会使 `spec-scripting-dialect.md:266-268` 那条「顶层 `GoTo` 运行效果不保证」的 Decision 失效，**须与规范同步**（该取舍待作者确认）。
