# 任务：把 `ScriptOptions.WarningLevel` 转发进 VB 编译选项（script-warning-level-plumbing）

> 队列 `..\csharp-script-parity-sweep\README.md` #6；缺陷登记 `..\..\issues\issue-warning-level-not-plumbed.md`（issue 27）。
> **性质**：D7 可移植、方向 A（对齐 C#）——本 fork 就是产品，"上游 VB 亦不落地"不再是理由。

## 一、判据（对齐 C#）

- C# 侧 `CSharpCompilationOptions` **有**公共 `WithWarningLevel(int)`（`PublicAPI.Shipped.txt:204`），且脚本编译路径把 `ScriptOptions.WarningLevel` 转发进编译对象（上游 `CSharpScriptCompiler.cs`）。
- VB 侧此前：`VisualBasicCompilationOptions` **无** `WithWarningLevel`、其私有构造器把 `warningLevel` **硬编码为 1**（`VisualBasicCompilationOptions.vb:266`），脚本编译器（`VisualBasicScriptCompiler.vb` 的 `CreateSubmission`）只转发 `optimizationLevel` / `checkOverflow`，不转发 `warningLevel` ⇒ `script.Compile().Options.WarningLevel` 恒为 1，无论 `ScriptOptions` 取何值。
- **目标形状**：脚本编译对象的 `WarningLevel` 反映 `ScriptOptions.WarningLevel`（默认 4，非用户改动；`WithWarningLevel(0)`→0、`(3)`→3）。普通编译（非脚本）默认仍为 1——改动是逐调用点、非默认翻转。

## 二、落点

1. `Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb`
   - 新增 `Public Function WithWarningLevel(warningLevel As Integer) As VisualBasicCompilationOptions`（相等返回 `Me`，否则拷贝后置 `WarningLevel`——因拷贝构造器经私有构造器把 `warningLevel` 钉在 1，故必须在拷贝后显式赋值）。
   - 修拷贝构造器 `Friend Sub New(other)`：在 `MyClass.New(...)` 之后加 `Me.WarningLevel = other.WarningLevel`，使 `With*` 链式组合不丢级别（与 `CSharpCompilationOptions` 同形；脚本路径 `.WithIgnoreCorLibraryDuplicatedTypes(True).WithWarningLevel(...)` 亦依赖此）。
2. `Compilers\VisualBasic\Portable\PublicAPI.Unshipped.txt`：加一行 `...VB.VisualBasicCompilationOptions.WithWarningLevel(warningLevel As Integer) -> ...VB.VisualBasicCompilationOptions`（否则 PublicAPI 分析器报 RS0016）。
3. `Scripting\VisualBasic\VisualBasicScriptCompiler.vb`：`CreateSubmission` 里在 `WithIgnoreCorLibraryDuplicatedTypes(True)` 后链 `.WithWarningLevel(script.Options.WarningLevel)`。

## 三、非脚本面免疫（HANDOFF §2②）

- 门：普通编译的 `VisualBasicCompilationOptions` 仍走公共构造器 ⇒ 私有构造器仍产 `warningLevel:=1`；`WithWarningLevel` 是**逐调用点**的新可选 API，无既有调用者 ⇒ 非脚本产品行为逐字不变。
- 拷贝构造器新增的 `Me.WarningLevel = other.WarningLevel` 只把"拷贝是否保留级别"从"恒 1"改成"等于源级别"；普通编译里所有对象的 `WarningLevel` 本就是 1 ⇒ 拷贝前后同为 1，无差。仅当某对象经 `WithWarningLevel` 得到非 1 后，拷贝才保留之——非脚本路径不产这种对象。

## 四、行为效力与测试口径（档 1）

- 实测（读码）：`CompilationOptions.WarningLevel` 在 VB 绑定/发射面**无活消费点**（VB 源码里只有 `MessageProvider.GetWarningLevel(code)` 按**诊断码**查严重级别，与 `CompilationOptions.WarningLevel` 无关）⇒ 本条是**纯转发奇偶对齐**（C# 亦如此），不是改变报哪些诊断。故测试**只断转发值抵达编译对象**，不（也无法）断"某诊断随级别增减"。
- 正/反对照（同容器）：
  - 编译层 `VisualBasicCompilationOptionsTests.WithWarningLevel`——默认 1；`WithWarningLevel(0)`→0 且不动源；相等返回 `Same`；拷贝与 `With*` 链式保留 0；`Equals`/`GetHashCode` 参与比较。
  - 脚本层 `ScriptOptionsTests.WarningLevel_ReachesTheCompilationOption`（原 `WarningLevel_DoesNotReachTheCompilationOption`，D5 分叉钉桩回收）——`WithWarningLevel(0)`/`(3)`/默认(4) 分别抵达编译对象的 0/3/4；保留 `OptimizationLevel`/`CheckOverflow` 对照。
  - 反例锁（非脚本免疫）：`New VisualBasicCompilationOptions(ConsoleApplication).WarningLevel = 1`（上面正例首断即是）；普通 `CreateCompilation` 不受影响由全量七门数字不变佐证。

## 五、验收

- 定向：`VisualBasicCompilationOptionsTests`（+1 格 ⇒ Semantic 5869→**5870**）与 `ScriptOptionsTests`（改名格，L2 计数不变）。
- 全量：七门（Semantic +1）+ L2 `-automated`（769/0）+ **重建发布版宿主**跑 `vbi` 默认脚本编译对象 `WarningLevel=4`（档 2，可选）。
- 账本：`upstream-merge.md` 新增 `§2.25(h)`（`VisualBasicCompilationOptions.vb` 系上游同名文件 ⇒ 登记公共面分歧 + 拷贝构造器改动 + 复核指引：上游若给公共构造器加 `warningLevel` 形参，本 `WithWarningLevel` 的"拷贝后赋值"可改回构造器传参）；`PublicAPI.Unshipped.txt` 入账本公共面注记；issue 27 → Fixed（commit 不预填）；队列 #6 转已验证；`ScriptModeApiSurfaceConformanceTests.vb` 文档串里"registered divergence"措辞已随回收改写。
