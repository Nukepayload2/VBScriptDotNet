# 共享 `CommonTypeNameFormatter.FormatGenericTypeName`：顶层泛型类型丢命名空间（嵌套泛型与非泛型都不丢）

- **状态**：**Open**（**上游来源**，目标是 `dotnet/roslyn`；**本 fork 不修**，见「性质」节。不预填 commit）
- **发现日期**：2026-09-16
- **发现场景**：`../tasks/script-mode-coverage-parity/` 的 U8（ObjectFormatter 代理族与异常栈渲染）补测。登记依据同任务 `README.md` **§八 义务 1** 末句「实施期若发现别的真缺陷，同样按此登记」。
- **影响面**：`Scripting\Core\Hosting\ObjectFormatter\` 是 **VB 与 C# 脚本共用的共享层**（本仓该目录整体自 `{{Roslyn}}` 同步）。缺陷在 `TypeNameFormatter` 的泛型分支，触发面是**栈帧签名的渲染**——`FormatException` 的每一帧都经 `FormatMethodSignature`（`CommonObjectFormatter.cs:112`），而它是本仓产品码里唯一以 `showNamespaces: true` 构造选项的调用点（`CommonObjectFormatter.cs:118`；`FormatObject` 走的是 `:57-60` 的 `showNamespaces: false`）。同理受影响的是任何以 `showNamespaces: true` 直接调用 `FormatTypeName` 的公共 API 使用者。
- **严重度**：低（显示层不对称，不崩、不误判语义）；但对「栈帧签名必须能唯一定位类型」的用途是实打实的退化——同一次输出里非泛型带全名、顶层泛型只剩短名。

## 触发面

`CommonTypeNameFormatter.FormatTypeName` 按「是否泛型」二选一（`CommonTypeNameFormatter.cs:56-63`），两条支路的命名空间口径不同：

| 支路 | 判定 | `ShowNamespaces:=True` 时的行为 | 命名空间 |
|---|---|---|---|
| `FormatGenericTypeName`（`:232`） | `typeInfo.IsGenericType` | **只有** `typeInfo.DeclaringType IsNot Nothing`（`:241`）时补 `nestedTypes.Last().Namespace`（`:251-258`）；**顶层泛型走 `:272-276` 的 `else`，直接 `AppendTypeInstantiation`，全程不碰 `Namespace`** | 嵌套有 / **顶层丢** |
| `FormatNonGenericTypeName`（`:65`） | 其余 | `:67-70` 直接 `return typeInfo.FullName.Replace('+', '.')` | **一律有** |

⇒ 「是否泛型」的判定**先于**「是否要命名空间」的判定，于是同一次 `FormatMethodSignature` 输出里会出现：非泛型类型带完整命名空间、顶层泛型类型不带。调用链与判据都是读码实锤（本 issue 全部行号取自本仓 `Scripting\Core\Hosting\ObjectFormatter\CommonTypeNameFormatter.cs`；`{{Roslyn}}` 的同名文件仅差 using 行，对应锚点为 `:240` / `:252` / `:274`）。

## 最小复现

**来源（如实标注）**：下面的读数由**验证轮的进程内直调**取得（未落成测试用例），本 issue 引用它；本单元（U8）不改产品码、也不新增判该缺陷失败的用例，故**未**在测试套件里复跑。三态：**实锤**（验证轮实测读数，与本节末的读码推断一致）。

```text
FormatMethodSignature(GetType(StackFixture).GetMethods()…)        → Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.StackFixture.Method()
FormatMethodSignature(GetType(StackFixture(Of Integer)).GetMethods()…) → StackFixture(Of Integer).Method()      ← 命名空间丢失
```

两个夹具都在 `ScriptModeObjectFormatterTests.vb` 末尾的 `#ExternalSource` 区域内（同一命名空间下的**顶层**类型，`Friend NotInheritable Class StackFixture` 与 `… Class StackFixture(Of T)`），唯一差别是后者为泛型。**同一份期望值里还有仓内旁证（实锤）**：该测试文件的 `StackTrace_NonGeneric` 用 `GetType(StackFixture).FullName` 断言（带命名空间），而 `StackTrace_GenericType` 用常量 `StackFixtureGenericName = "StackFixture(Of T)"` 断言（不带命名空间）——两格各自都是绿的，正是这条不对称的钉点。

## 边界（不要读成「所有泛型都丢命名空间」）

1. **只有顶层泛型丢**。嵌套泛型（`DeclaringType IsNot Nothing`）走 `:241-258`，命名空间照补。
2. **非泛型不丢**（顶层、嵌套都不丢）——`FormatNonGenericTypeName` 的 `FullName` 本身就含命名空间。
3. **只在 `ShowNamespaces:=True` 下可见**。`FormatObject` 走 `showNamespaces: false`（`CommonObjectFormatter.cs:57-60`），此时两条支路都不印命名空间，本缺陷不显形；所以受影响的调用面就是栈帧签名（`FormatException`）与直接传 `true` 的公共 API 使用者。
4. **不是「泛型参数没渲染」**：`StackFixture(Of T)`、`Method(Of U)` 这些泛型形状本身是对的（`AppendTypeInstantiation` 正常），缺的**只有**前缀命名空间。

## 性质：上游共享代码的缺陷，**不是本 fork 引入**

- **文件级实锤**：本轮实跑 `diff`，本仓 `Scripting\Core\Hosting\ObjectFormatter\CommonTypeNameFormatter.cs` 与 `{{Roslyn}}\src\Scripting\Core\Hosting\ObjectFormatter\CommonTypeNameFormatter.cs` 的**唯一差异是 using 行**（本仓多一行 `using Microsoft.CodeAnalysis.Collections;`，`PooledObjects` 的 using 位置不同）；`:232` / `:241` / `:251-258` / `:272-276` 这段逻辑两边逐字相同。
- **旁证（同目录其余文件）**：该目录另有三处非常量级差异，但都只是 C# 语法现代化写法（`indices[^1]` ↔ `indices[indices.Length - 1]`、集合表达式 `[obj]` ↔ `new object[] { obj }`），与本缺陷无关；**本缺陷所在的 `CommonTypeNameFormatter.cs` 是纯 using 差异**。
- **归属** ⇒ 修复应落在上游 `dotnet/roslyn`（`src/Scripting/Core/Hosting/ObjectFormatter/CommonTypeNameFormatter.cs`）。
- **本 fork 不修的理由**：修它只会在 `Scripting\Core\Hosting\ObjectFormatter\` 这个「与上游逐字同步」的目录里**徒增合并冲突面**，而不改变任何本 fork 独有的行为；本仓对该目录的既定做法是「与上游保持一致，缺陷按上游 issue 跟踪」。

## 预期行为

`ShowNamespaces:=True` 下，类型的命名空间口径应当与「是否泛型」无关：

```
Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.StackFixture(Of T).Method()
```

实现方向（**未实现、未验证，仅记录**）：`:272-276` 的 `else` 分支比照 `:251-258` 补 `typeInfo.Namespace`（同一处也可把命名空间拼接提到泛型/非泛型共同的入口，消除两处各写一遍的口径分裂）。**推测**上游可能把它归为 DevDiv #173210 那条 TODO 的同族问题（`FormatGenericTypeName` 里紧邻的 `:237-238` 注释），未向上游确认。

## 相关

- 仓内钉子（行为被断言，缺陷不显形于失败）：
  - `Scripting\VisualBasicTest\ScriptModeObjectFormatterTests.vb` 的 `StackTrace_GenericType`（常量 `StackFixtureGenericName`）与 `StackTrace_GenericMethodInGenericType`（同一常量的合取格）；注释里已把本值标为**特征钉死（characterization pin），不是正确性 oracle**。
  - 对照格：同文件的 `StackTrace_NonGeneric`（非泛型 ⇒ 带命名空间）。
- 同源登记：`../tasks/script-mode-coverage-parity/` 的 U8 流水账把该不对称列为「与 C# 基线的真实差异」第 8 条（当时三态为「行为 + 读码实锤；**是否算缺陷未判定**」；本 issue 把它升级为**登记在册的缺陷**，判定依据是本 issue「触发面」的两支路口径不一致 + 「预期行为」一条）。
- C# 基线为何没暴露该支路（**实锤读码**，两条各自独立）：① 它的栈帧夹具是 `ObjectFormatterTests` **内部的** `private static class Fixture` / `Fixture<T>` / `Fixture2` / `ParametersFixture`（`ObjectFormatterTests.cs:898` / `:913` / `:1023` / `:1056`）⇒ `DeclaringType IsNot Nothing`，走的是**补命名空间**那一支；② 那 7 格 `StackTrace_*` 全部挂着 `[Fact(Skip=…)]`（issue 19027 / 9221），从未运行。

## 后续（本任务不修）

- 本 issue 只登记与取证：**未改任何产品源码**，未新增用例（会恒红或需要放宽断言，见「相关」的钉子说明），不预填修复 commit。
- 若日后要修：属「对外可见输出形状变化」，按 `../decisions.md` 的提案闸门与 `../tasks/script-mode-coverage-parity/README.md` **§八 义务 4** 登记变更面；同时同步 `ScriptModeObjectFormatterTests.vb` 的 `StackFixtureGenericName` 与两处 `AtFileLine` 期望值，并复核 `ScriptModeObjectFormatterFixtures.vb` 的表。
- 上游若已修（`FormatGenericTypeName` 的 else 分支补命名空间），本仓同步后上述钉子会**应当**变红——那是修复的报警线，不要为保住绿而放宽断言。
