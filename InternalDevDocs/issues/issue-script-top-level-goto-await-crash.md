# 脚本顶层 `GoTo` + `Await` → 编译器 `NullReferenceException`

* 状态：**Fixed**（48d8edb）
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

## 修复后须知的行为变化：顶层重复标签（今天静默通过）

**实测（已运行，2026-09-13 本机复测；Debug `vbi.exe`，自报 `2.0.0-Beta+3724b98e…`）**：顶层写**重复标签**，今天**零诊断、静默通过**：

```vbx
Console.WriteLine("A")
skip:
Console.WriteLine("B")
skip:
Console.WriteLine("C")
```

实测输出恰为 `A` / `B` / `C`、**无任何诊断**、`EXITCODE=0` ⇒ 顶层标签的**重复定义完全不被检查**（与本 issue 同源：`SourceMemberContainerTypeSymbol.vb:2613-2615` 丢弃 `LabelStatement` ⇒ 第二处标签语句从不进体，`BindLabelStatement` 从不执行，重复判别自然无从发生）。

**修复后**（顶层标签恢复收集 ⇒ 标签语句进入 `<Initialize>` 的实例初始化器序列并被绑定）会**新增一条重复标签诊断**：`Binder_Statements.vb:941-944` 的判别（`symbol.LabelName <> labelToken` ⇒ 这不是标签目标而是重复定义）报 `ERR_MultiplyDefined1`，即 **`BC30094`**（`Errors.vb:160`；资源文本「Label '{0}' is already defined in the current method.」；普通方法里的既有用例 `Compilers\VisualBasicSemanticTest\Binding\BindingErrorTests.vb:2618-2637`）。**已检查（源码）**。

这是**正确诊断**（普通方法与顶层标签同源同码），但**对脚本顶层是行为变化**：

- 今天跑得通的「顶层重复标签」脚本，修复后会变成 `error` ⇒ **不发射、不产生可运行产物**；
- 须在修复时**补回归用例**锁死两条边：① 顶层重复标签 ⇒ **恰好一条 `BC30094`**（且不再崩）；② 顶层单个标签（含孤立标签、多标签、前向/反向 `GoTo`）⇒ **零诊断**（对应 `Scripting\VisualBasicTest\ScriptTests.vb:299-309` 的既有正向锚点 `TestTopLevelGoToLabelStatementCompiles`，须零改动通过）。**当前 `../tasks/script-top-level-crashes/test-plan.md` 的 F11 用例表（F11-L2-1…6 / L3-1…2 / L4-1）未覆盖重复标签形状（已检查）**，修复时须补。

**待核实（不要照抄旧记录）**：计划阶段的记录把该诊断号写成「BC30112」，**与源码不符**——BC30112 是 `ERR_NamespaceNotExpression1`（`Errors.vb:175`，「'{0}' is a namespace and cannot be used as an expression.」），`Errors.vb` 里重复标签**只有** `ERR_MultiplyDefined1 = 30094` 一条路径（**已检查**）。本文按源码锚点记 `BC30094`；顶层路径实际落哪个号，**修复落地时用探针确认**（顶层走的是同一个 `BindLabelStatement`，故**推测**同号）。

**边界**：上节「静默通过」只对**不带 `GoTo`** 的重复标签成立；带 `GoTo` 的重复标签今天仍走本 issue 的崩溃路径（同一次复测：`GoTo skip` + 两处 `skip:` ⇒ `EXITCODE=3`）。

## 相关

- 姊妹问题（**根因不同**，勿混）：`issue-script-top-level-await-in-try-crash.md`。
- 本条的修复方向见 `../tasks/script-top-level-crashes/`：**判「修好」**——但会使 `spec-scripting-dialect.md:266-268` 那条「顶层 `GoTo` 运行效果不保证」的 Decision 失效，**须与规范同步**（该取舍待作者确认）。
