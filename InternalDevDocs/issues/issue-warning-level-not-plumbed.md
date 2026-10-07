# VB 脚本侧 `ScriptOptions.WarningLevel` 不落地：属性生效、编译对象恒为 1

- **状态**：**Fixed**（已验证，commit 待作者提交后补）——**采方向 A 补齐转发、对齐 C#**。落点经复核：`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb:580` 新增公共 `WithWarningLevel(warningLevel As Integer)`（私有构造器 `:266` 仍硬编码 `warningLevel:=1`），`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:259` 已转发 `WithWarningLevel(script.Options.WarningLevel)`。⇒ **本条不是"上游不修"的缺口**（对照问题单 25 那类）；下面 B2/B3/B5/B6 的**实锤读数是修复前的基线**，保留作对照。
- **发现日期**：2026-09-16
- **发现场景**：`../tasks/script-mode-coverage-parity/` 的 U9（脚本 API 面剩余缺口）第 **14** 项
  （`AllowUnsafe` / `CheckOverflow` / `WarningLevel` 落到编译选项）。C# 基线
  `{{Roslyn}}\src\Scripting\CoreTest\ScriptOptionsTests.cs:312`（`WarningLevel_Is_AppliedTo_CompilationOption`）
  断言该选项**落到编译对象**；VB 对偶格读出来的是**恒为 1**。登记依据同任务 `README.md` **§八 义务 1**。
- **影响面**：**`ScriptOptions.WithWarningLevel(int)` 这个公共 API 在 VB 脚本路径上无效**（属性存得住、编译时被丢）。
  影响面受限于**产品里没有路径调它**（见「触发面」末段的检索）。`ScriptOptions` 本身是**共享**类型
  （`Scripting\Core\ScriptOptions.cs`），C# 脚本路径**落地**、VB 脚本路径**不落地** ⇒ 同一个 API 在两条语言路径上
  行为不同。
- **本 fork 的变更面**：`Scripting\` 与 `Scripting\VisualBasic\` 是**上游目录**（**不是**本 fork 新增）。
  判据（**自己复跑**，基线 commit 见 `../upstream-merge.md` §一，上游树路径为 `src\Scripting\…`）：

  | 判据 | 读数 |
  |---|---|
  | `src/Scripting/VisualBasic/VisualBasicScriptCompiler.vb` | **rc=0（EXISTS）** |
  | `src/Scripting/VisualBasic/VisualBasicScript.vb` | **rc=0（EXISTS）** |
  | `src/Scripting/Core/ScriptOptions.cs` | **rc=0（EXISTS）** |
  | 对照：`src/Scripting/VisualBasic/Hosting/NuGetPackageSession.vb` | **rc=128（absent）** |
  | 对照：`src/Scripting/Core/Hosting/AssemblyLoader/NativeLibraryProbe.cs` | **rc=128（absent）** |

  ⇒ 上游树里**有**该目录与该文件，**没有**的只是目录内若干 fork 新增文件（如 `Hosting\NuGet*.vb`）。
  故**方向 A 改的 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 是上游同名文件**，其合并冲突面
  与 `#25`（共享文件缺陷）**同级**——不是「新增目录内的改动、不增加冲突面」。
  **修与不修的成本差别在公共 API**（见方向 A 的①）；「不修」的理由只落在「上游 VB 亦不落地、
  本 fork 未计划实现该能力」，**不是**「改了会增加冲突」。
- **严重度**：低。不崩、不产错值；错误表现是「设了 `WarningLevel` 但警告等级没变」（默认等级 1 下的警告集合本就不大），
  且**产品自身不调用**该 API。⇒ 归**上游继承的缺口**，登记不修。

## 触发面

**自己的实测读数**（复跑；探针 `tmp\u9-fix\probe\Issue27.cs`，读数 `tmp\u9-fix\i27.txt`——均在
`<项目根>` 的 `tmp\` 下，已 git-ignored）：

| # | 读数 | 值 | 三态 |
|---|---|---|---|
| B1 | `ScriptOptions.Default.WarningLevel` | `4` | **实锤** |
| B2 | `ScriptOptions.Default.WithWarningLevel(0).WarningLevel` | `0` | **实锤** |
| B3 | `ScriptOptions.Default.WithWarningLevel(3).WarningLevel` | `3` | **实锤** |
| B4 | `VisualBasicScript.Create("? 1 + 1", ScriptOptions.Default).GetCompilation().Options.WarningLevel` | `1` | **实锤** |
| B5 | 同上，选项换成 `WithWarningLevel(0)` | **`1`**（**不是 0**） | **实锤** |
| B6 | 同上，选项换成 `WithWarningLevel(3)` | **`1`**（**不是 3**） | **实锤** |
| B7 | **同族对照**：`WithOptimizationLevel(Release)` → 编译对象 `.Options.OptimizationLevel` | `Release`（**落地**） | **实锤** |
| B8 | **同族对照**：`WithCheckOverflow(True)` → 编译对象 `.Options.CheckOverflow` | `True`（**落地**） | **实锤** |
| B9 | `GetType(VisualBasicCompilationOptions).GetMethod("WithWarningLevel")` | `Nothing`（**该类型无此方法**） | **实锤** |
| B10 | `VisualBasicCompilationOptions` 构造器形参名里含 `warning` 的 | `[]`（**无**；形参名共 31 个） | **实锤** |
| B12 | 基类 `CompilationOptions.WarningLevel` 的 setter | `protected`（外部只能经构造实参传） | **实锤** |

⇒ **`B2`/`B3` 证明属性本身生效，`B5`/`B6` 证明编译对象不采纳**；`B7`/`B8` 是**排除平凡解释**的对照——
同族的另外两个选项**确实**会落地，所以「编译对象全是默认值」不是原因。`B9`/`B10` 把「VB 侧没有这个形参」
做成机械可判的读数（同一个断言形状已写进用例
`Scripting\VisualBasicTest\ScriptOptionsTests.vb:313` 的 `WarningLevel_DoesNotReachTheCompilationOption`，
它是**修复的报警线**：一旦落地，该用例即红）。
**产品内无调用者**：`grep -rn --exclude-dir=obj --exclude-dir=bin "WithWarningLevel" Scripting/ Compilers/VisualBasic/`
命中 **5** 处——**定义 1**（`Scripting\Core\ScriptOptions.cs:386`）、**公共 API 1**（`Scripting\Core\PublicAPI.Shipped.txt:125`）、
**用例 3**（`Scripting\VisualBasicTest\ScriptOptionsTests.vb:314,315,316`）⇒ **产品源码里没有任何调用点**。
（**检索范围口径**：`Compilers/VisualBasic/` 是 VB 侧范围；若把范围放大到 `Scripting/ Compilers/`，命中数变成 **203** ——
多出来的全在 C# 侧（`Compilers\CSharp\…`，主要是 `Compilers\CSharp\Test\` 下的用例），与本 issue 的 VB 路径无关。
两个范围都**零产品调用点**；本条只对 VB 侧范围读数。）

## 根因

**VB 侧的选项转发缺这一项**（自己读码，双锚）：

- 生产侧：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb`，符号 **`CreateSubmission`（声明于 `:164`**，
  签名 `Public Overrides Function CreateSubmission(script As Script) As Compilation`）——其
  `VisualBasicCompilation.CreateScriptCompilation` 调用**起于 `:208`**，实参表里语言相关选项**只有两项**：
  `optimizationLevel:=script.Options.OptimizationLevel`（**`:223`**）与
  `checkOverflow:=script.Options.CheckOverflow`（**`:224`**）。**没有 `warningLevel`**（该文件全无此标识符）。
- 接收侧：`Compilers\VisualBasic\Portable\VisualBasicCompilationOptions.vb`——构造处的
  `warningLevel:=1` **硬编码在 `:266`**（`Me.WarningLevel` 由基类 `CompilationOptions` 的构造器接收，
  而该属性**没有** `WithWarningLevel`（`B9`）也**没有**构造形参（`B10`）⇒ **VB 侧唯一能设它的途径就是这里**）。
- **C# 对偶是落地的**：`{{Roslyn}}\src\Scripting\CSharp\CSharpScriptCompiler.cs:65` 有
  `warningLevel: script.Options.WarningLevel`。⇒ **两条语言路径行为不同**，本仓 VB 侧是该能力的缺口。

## 性质判定：**上游继承的 VB 侧缺口，非 fork 引入**

1. **上游 VB 也不落地（实锤）**：`{{Roslyn}}\src\Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的
   `grep -c warningLevel` = **0**（C# 对偶同文件为 `1`）。⇒ 这是**上游 VB 从未实现**的一项，不是本 fork 的偏差。
2. **不是「VB 语言没有这个概念」**：`warningLevel` 是**共享** `CompilationOptions` 上的属性
   （`Compilers\Core\Portable\Compilation\CompilationOptions.cs:148` 的 `public int WarningLevel { get; protected set; }`，
   构造处 `:308`），VB 编译选项**能**接收它（`VisualBasicCompilationOptions.vb:266` 就在传），只是脚本编译路径
   没把用户选项接上去。⇒ 是**实现缺口**，不是语言语义分歧。
3. **产品不可触达（实锤）**：本仓产品源码里零调用点（上节检索）⇒ 修复的**风险面**是「已发布的公共 API 行为变化」，
   不是「产品行为变化」。

## 候选方向（**不自行选，不修**）

- **方向 A（补齐转发，按 D5 对齐 C#）**：在 `VisualBasicScriptCompiler.vb:223-224` 之后加
  `warningLevel:=script.Options.WarningLevel`。**一行改动**，但**要先确认**：① `VisualBasicCompilationOptions`
  的 `warningLevel:=1` 是**硬编码**（`:266`）——把用户选项一路传到构造器时，`:266` 那处是不是要**变成形参**
  （即 VB 编译选项类型要不要**新增** `warningLevel` 构造形参 + 可能的 `WithWarningLevel`）？这一步会**动公共 API**
  （`Compilers\VisualBasic\Portable\PublicAPI.Shipped.txt` / `.Unshipped.txt`），落 `../decisions.md` **D4** 闸门内。
  ② 退出行为变化：低等级（0/1）下**隐藏**的警告会重新出现 ⇒ 既有脚本可能开始报警告（不是错误，`WarningLevel`
  不影响 `generalDiagnosticOption`），需评估对既有测试的影响。
- **方向 B（只记差异，不落地）**：接受「VB 侧 `WarningLevel` 无效」，把它写进
  `spec\spec-scripting-dialect.md` 的规范面或 `../upstream-merge.md` 的变更面登记，让使用者知道该 API 在 VB 脚本
  路径上是 no-op。**代价**：公共 API 上留一个静默无效的方法（`WithWarningLevel` 在 `PublicAPI.Shipped.txt:125`，
  属**已发布**面），对使用者不友好。
- **方向 C（判上游来源、本 fork 不修）**：既然上游 VB 同样不落地，按 `#25`（`issue-generic-type-name-loses-namespace.md`）
  的先例把它登记为**上游来源**、目标 `dotnet/roslyn`。**与 `#25` 的合并冲突面同级**：两条都落在**上游树里已有的文件**上
  （`#25` 是共享文件 `CommonTypeNameFormatter.cs`；本 issue 是 `Scripting\VisualBasic\VisualBasicScriptCompiler.vb`
  ——上游同名文件，判据见「本 fork 的变更面」的 `cat-file -e` 表）。⇒ 方向 C 的理由应落在「这是上游 VB 的既有缺口、
  本 fork 未计划实现该能力」，**不是**「改了会增加冲突」（按方向 A 改它**同样**增加冲突面）。
  **这是本 issue 的默认倾向**（见「状态」的「低优先级，不修」），但**交用户裁决**。

## 三态标注

| 断言 | 三态 | 依据 |
|---|---|---|
| `WarningLevel` 属性生效、编译对象不采纳 | **实锤** | `B1`–`B6` 六条读数自跑 |
| 根因是 VB 脚本编译路径缺转发 + VB 选项类型无该形参 | **实锤（读码）** | `VisualBasicScriptCompiler.vb:208`（调用起）/`:223`/`:224`；`VisualBasicCompilationOptions.vb:266`（硬编码 1）；`B9`/`B10`（类型面无此形参） |
| C# 侧落地 | **实锤** | `{{Roslyn}}\src\Scripting\CSharp\CSharpScriptCompiler.cs:65` |
| 上游 VB 也不落地 ⇒ 上游继承，非 fork 引入 | **实锤** | `{{Roslyn}}\src\Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `grep -c warningLevel` = 0 |
| 产品内无调用点 | **实锤** | `grep -rn --exclude-dir=obj --exclude-dir=bin "WithWarningLevel" Scripting/ Compilers/VisualBasic/` 命中 5 处，全是定义 / 公共 API / 用例 |
| 方向 A 需要动公共 API、方向 B/C 的取舍 | **推测** | 只做了落点定位与检索，未做实现试验；「`:266` 那处是否需要变成形参」**未取证**（取决于方向 A 的实现形式），标推测 |

## 相关

- 钉住该缺口的用例（修复的报警线）：`Scripting\VisualBasicTest\ScriptOptionsTests.vb:313`
  （`WarningLevel_DoesNotReachTheCompilationOption`，`:315`/`:316` 两条读数，另含 `:321`/`:322` 的同族对照）。
- C# 基线：`{{Roslyn}}\src\Scripting\CoreTest\ScriptOptionsTests.cs:312`（`WarningLevel_Is_AppliedTo_CompilationOption`）。
- 同类先例（上游来源、本 fork 不修）：`#25`（`issue-generic-type-name-loses-namespace.md`）。
- 决策与义务：`../decisions.md` **D4**、**D5**；`../tasks/script-mode-coverage-parity/README.md` **§八 义务 1**、**义务 4**。

## 后续（不修）

- 本 issue 只登记与取证：**未改任何产品源码**，不预填修复 commit。
- 若用户选方向 A：按 `../tasks/script-mode-coverage-parity/README.md` **§八 义务 4** 登记变更面，同步
  `PublicAPI.*.txt`，并**改** `ScriptOptionsTests.vb:313` 那条用例（从「断言不落地」改成「断言落地」）。
- 若用户选方向 C：与 `#25` 一同归入「上游来源」，在本仓 `../upstream-merge.md` 的变更面登记里注明该缺口的存在，
  避免后续轮次把它当作新发现重复上报。
